// ============================================================================
// 模块名称: JzM5Helper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   京东京造 JZ M5 三模鼠标 2.4G 接收器 (362D:D107 Wireless Link-KM) 内置专用 HID 驱动：
//   1. 严格限定 FFC1/0001 Input/Output=64/64 Feature=0 厂商配置端点，走 Keychron/Wukong
//      Launcher "8k" 协议族 B3/B4 (64B) 与 B5/B6 (21B) 请求-应答帧。
//   2. B5 02 版本握手 (VID/PID/固件/工作模式) 确认身份，B3 06 读取基础信息并按
//      [19] 低 7 位解析电量、bit7 解析充电标志。
//   3. 真实抓包样本回放：02 02 00 2D 36 07 D1 07 01 01... => VID 362D / PID D107 /
//      固件 1.0.7 / workMode=1(2.4G)；06 基础信息样本 [19]=0x58 => 88% 放电。
//   4. 命中设备声明 SupportsDpi/SupportsPollingRate=false：该接收器配置通道可写 DPI/回报率，
//      但本驱动为只读电量引擎，绝不盲发 0x40/0x41 等写指令，避免误操作 EEPROM/RF 配对表。
//   5. 通道由 HardwareCommandBus 按 VID:PID 细粒度互斥；读取失败走 DriverStateStore 休眠缓存。
// 依赖协议源: 官方 WebHID wukong.jd.com main bundle + V-Conet/wukong-driver 逆向文档
//   https://github.com/V-Conet/wukong-driver (docs/PROTOCOL.md, src/protocol.rs)
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑或协议解析时，必须同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 $"..." 插值、?. 空条件、nameof、表达式主体成员。
// 3. [编码格式锁定]: 本文件严格保存为 UTF-8 with BOM。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RazerBatteryTray
{
    public static class JzM5Helper
    {
        public const string DriverKey = "BuiltIn:JzM5Mouse";
        internal const string StateKey = "JZM5:362D:D107:MS";
        internal const string ModelName = "JZ M5";
        internal const string BrandName = "京造";

        public const int VendorIdForIsolation = 0x362D;
        internal const int ReceiverPidForIsolation = 0xD107;
        private const int VendorId = VendorIdForIsolation;
        private const int ReceiverPid = ReceiverPidForIsolation;
        private const byte ReportOutLarge = 0xB3;
        private const byte ReportInLarge = 0xB4;
        private const byte ReportOutSmall = 0xB5;
        private const byte ReportInSmall = 0xB6;
        private const int ReportLength = 64;
        private const int ReadTimeoutMs = 700;

        private static readonly object hidLock = new object();
        private static readonly Dictionary<string, string> verifiedPaths =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, MouseBatteryInfo> lastReadings =
            new Dictionary<string, MouseBatteryInfo>(StringComparer.OrdinalIgnoreCase);

        public static bool IsControlEndpoint(HidDeviceDescriptor d)
        {
            return d != null && d.VendorId == VendorId && d.ProductId == ReceiverPid &&
                   d.UsagePage == 0xFFC1 && d.Usage == 0x0001 &&
                   d.InputReportLength == 64 && d.OutputReportLength == 64 && d.FeatureReportLength == 0;
        }

        public static bool IsVersionIdentity(byte[] report)
        {
            // WebHID payload (after report id): 02 02 00 2D 36 07 D1 07 01 01 ...
            // Win32 inclusive buffer: B6 02 02 00 2D 36 07 D1 07 01 01 ...
            if (report == null || report.Length < 10) return false;
            int off = report[0] == ReportInSmall ? 1 : 0;
            return report[off] == 0x02 &&
                   report[off + 3] == 0x2D && report[off + 4] == 0x36 &&
                   report[off + 5] == 0x07 && report[off + 6] == 0xD1;
        }

        public static bool TryParseBattery(byte[] report, out int percent, out bool charging)
        {
            percent = -1;
            charging = false;
            if (report == null || report.Length < 21) return false;
            int off = report[0] == ReportInLarge ? 1 : 0;
            if (report[off] != 0x06) return false;
            int raw = report[off + 19] & 0x7F;
            if (raw > 100) return false;
            percent = raw;
            charging = (report[off + 19] & 0x80) != 0;
            return true;
        }

        private static byte[] Query(IntPtr handle, byte report, byte command)
        {
            // HID 端点要求写满整份 64 字节报告；短写 (21B/64B) 会被设备静默丢弃，握手永不成功。
            byte[] tx = new byte[ReportLength];
            tx[0] = report;
            tx[1] = command;
            if (!HidTransport.WriteReport(handle, tx, tx.Length, 500)) return null;
            Stopwatch timer = Stopwatch.StartNew();
            while (timer.ElapsedMilliseconds < ReadTimeoutMs)
            {
                int remaining = ReadTimeoutMs - (int)timer.ElapsedMilliseconds;
                byte[] rx;
                if (!HidTransport.ReadReport(handle, ReportLength, remaining, out rx)) return null;
                if (rx == null || rx.Length < 2) continue;
                // Launcher matches on first payload byte and accepts acks on either input report.
                byte first = rx[0] == ReportInLarge || rx[0] == ReportInSmall ? rx[1] : rx[0];
                if (first == command || (first == 0xE4 && rx.Length > 3 && rx[3] == command)) return rx;
            }
            return null;
        }

        private static MouseBatteryInfo CachedState(HidDeviceDescriptor endpoint)
        {
            string cacheKey = StateKey + ":Path:" + endpoint.DevicePath;
            string name;
            DateTime updated;
            int percent;
            bool charging;
            if (DriverStateStore.TryLoadBatteryState(cacheKey, out percent, out charging, out name, out updated))
            {
                return CreateState(endpoint, percent, false, true, updated);
            }
            MouseBatteryInfo previous;
            if (lastReadings.TryGetValue(endpoint.DevicePath, out previous))
            {
                return CreateState(endpoint, previous.BatteryPercent, false, true, previous.LastUpdated);
            }
            return null;
        }

        public static MouseBatteryInfo CreateState(HidDeviceDescriptor endpoint, int percent,
            bool charging, bool sleeping, DateTime updated)
        {
            MouseBatteryInfo dev = new MouseBatteryInfo();
            dev.DeviceId = StateKey;
            dev.HardwareFingerprint = "HID:362D:D107:MS";
            dev.HardwareId = endpoint.DevicePath;
            dev.PhysicalUnitId = StateKey + ":Path:" + endpoint.DevicePath.ToUpperInvariant();
            dev.DriverId = DriverKey;
            dev.Brand = BrandName;
            dev.DeviceName = ModelName;
            dev.Category = DeviceCategory.Mouse;
            dev.Transport = "2.4G 无线";
            dev.BatteryPercent = percent;
            dev.ExactBatteryPercent = percent;
            dev.IsConnected = true;
            dev.IsDonglePresent = true;
            dev.IsSleeping = sleeping;
            dev.IsCharging = !sleeping && charging;
            dev.IsChargingUnknown = sleeping;
            dev.LastUpdated = updated;
            dev.Capabilities.SupportsBattery = true;
            dev.Capabilities.SupportsDpi = false;
            dev.Capabilities.SupportsDpiStages = false;
            dev.Capabilities.SupportsPollingRate = false;
            dev.Capabilities.SupportsXyIndependence = false;
            dev.Capabilities.SupportsHardwareDpiMonitor = false;
            dev.Capabilities.SensorDescription = "原相 PAW3809 · 京造 JZ M5 (2.4G 只读电量)";
            dev.SupportsDpiWrite = false;
            dev.SupportsPollingRateWrite = false;
            return dev;
        }

        public static bool IsVerifiedDevice(MouseBatteryInfo dev)
        {
            if (dev == null || dev.Category != DeviceCategory.Mouse || string.IsNullOrEmpty(dev.HardwareId)) return false;
            lock (hidLock)
            {
                string key;
                return verifiedPaths.TryGetValue(dev.HardwareId, out key) &&
                       string.Equals(key, StateKey, StringComparison.OrdinalIgnoreCase);
            }
        }

        public static List<MouseBatteryInfo> QueryDevices()
        {
            List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
            lock (hidLock)
            {
                verifiedPaths.Clear();
                foreach (HidDeviceDescriptor endpoint in HidTransport.Enumerate(VendorId, ReceiverPid))
                {
                    if (!IsControlEndpoint(endpoint)) continue;
                    if (HardwareDriverRegistry.IsExternallyOwnedPath(endpoint.DevicePath)) continue;
                    IntPtr handle = HidTransport.Open(endpoint.DevicePath);
                    if (!HidTransport.IsValidHandle(handle)) continue;
                    try
                    {
                        byte[] identity = Query(handle, ReportOutSmall, 0x02);
                        if (!IsVersionIdentity(identity))
                        {
                            lastReadings.Remove(endpoint.DevicePath);
                            continue;
                        }
                        verifiedPaths[endpoint.DevicePath] = StateKey;
                        int percent;
                        bool charging;
                        byte[] info = Query(handle, ReportOutLarge, 0x06);
                        MouseBatteryInfo state;
                        if (TryParseBattery(info, out percent, out charging))
                        {
                            state = CreateState(endpoint, percent, charging, false, DateTime.Now);
                            lastReadings[endpoint.DevicePath] = state;
                            DriverStateStore.SaveBatteryState(
                                StateKey + ":Path:" + endpoint.DevicePath, percent, charging, ModelName);
                        }
                        else
                        {
                            state = CachedState(endpoint);
                        }
                        if (state != null) result.Add(state);
                    }
                    finally
                    {
                        HidTransport.Close(handle);
                    }
                }
            }
            return result;
        }
    }
}
