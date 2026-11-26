// ============================================================================
// 模块名称: VgnN75ProHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   VGN N75 PRO 三模机械键盘（2.4G 接收器）的电量/充电状态读取与型号识别。
//
//   物理指纹（实机 Win32 枚举，2026-10-31）:
//     VID 0x320F / PID 0x5088 / REV 0x0100
//     ProductString "VGN N75 PRO 2.4G Dongle" / Manufacturer "Telink"
//     厂商集合 UsagePage 0xFF1C / Usage 0x0092 / Input 64 / Output 64 / Feature 0
//
//   ⚠️ 共享 PID 冲突（铁律 9 / 13②）:
//     0x320F:0x5088 同时是 ATK V75X 的接收器公版 PID。两品牌协议同源但外观/型号不同，
//     因此严禁仅凭 VID:PID 认领；必须叠加 ProductString 品牌与型号门禁，并与
//     AtkDeviceHelper 使用互斥的产品串、互不相同的 DeviceId / HardwareFingerprint。
//
//   协议（实机抓包，非文档推测）:
//     TX 64B: [0]=0x04(Report ID) [1..2]=递增序号(小端，非零) [3]=0x1A(电量查询)
//     RX 64B: [0]=0x04 [1..2]=序号回显 [3]=0x1A [8]=电量(0..100) [9]=1 充电 / 0 电池供电
//     实测样本: 请求 04 01 00 1A ... -> 应答 04 01 00 1A 00 00 00 00 4F 00 ...（79%，未充电）
//     实测 04 34 12 1A ... -> 应答序号原样回显 34 12，可据此丢弃陈旧/串线帧。
//     充电位双向实测: 插 USB-C 充电器 -> 04 07 00 1A ... 4F 01（79% 充电中，电量 78->79->80 爬升）；
//     拔线后回落 4F 00，排除「常亮假充电位」（对照铁律 5 / 13②）。
//
//   能力边界（物理事实优先，铁律 13③）:
//     仅开启“电量 + 充电状态”；本型号的灯光/按键/回报率写指令未实测，一律不声明。
//     有线 USB 与蓝牙形态的 PID 未取得实机样本，故不认领。
// 依赖关系: HidTransport.cs, MouseBatteryInfo.cs, DeviceCapabilities.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、协议解析或签名时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止字符串插值、空条件运算符、nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    public static class VgnN75ProHelper
    {
        private static readonly object hidLock = new object();
        private const string CacheRegistryKey = @"Software\RazerBatteryTray\VgnN75ProCache";

        public const int VgnVid = 0x320F;
        public const int N75ProDonglePid = 0x5088;

        public const byte VendorReportId = 0x04;
        public const byte OpcodeBattery = 0x1A;
        public const int VendorUsagePage = 0xFF1C;
        public const int VendorUsage = 0x0092;
        public const int VendorReportLength = 64;

        private const int QueryTimeoutMs = 300;
        private const int WriteTimeoutMs = 200;
        private const int QueryThrottleSeconds = 5;

        public static int CachedBatteryPercent = -1;
        public static bool CachedIsCharging = false;
        public static string CachedDeviceName = "VGN N75 PRO";
        public static DateTime CachedLastUpdated = DateTime.MinValue;

        private static readonly Dictionary<string, MouseBatteryInfo> cache = new Dictionary<string, MouseBatteryInfo>();
        private static DateTime lastQuery = DateTime.MinValue;
        private static ushort sequence;

        static VgnN75ProHelper()
        {
            LoadHardwareCache();
        }

        /// <summary>
        /// 严格四元组 + 产品串门禁：VID/PID 与 ATK V75X 完全相同，只能靠品牌型号串互斥。
        /// 仅认领实测过的 "VGN N75 PRO 2.4G Dongle"；同 PID 的 ATK V75X、VGN S99、有线/蓝牙形态一律不碰。
        /// </summary>
        public static bool IsSupportedDevice(HidDeviceDescriptor device)
        {
            if (device == null) return false;
            if (device.VendorId != VgnVid || device.ProductId != N75ProDonglePid) return false;
            if (string.IsNullOrEmpty(device.ProductString)) return false;
            // 四重门禁：品牌 + 型号 + 子型号 + 实测连接形态（2.4G Dongle）。
            // 有线/蓝牙形态未取得实机样本，宁可暂不认领，也不套用未经证实的报文。
            string product = device.ProductString;
            if (product.IndexOf("VGN", StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (product.IndexOf("N75", StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (product.IndexOf("PRO", StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (product.IndexOf("DONGLE", StringComparison.OrdinalIgnoreCase) < 0) return false;
            return true;
        }

        /// <summary>
        /// 端点隔离：只接受 0xFF1C / 0x0092 的 64/64 字节厂商集合，绝不触碰受保护的键盘集合。
        /// </summary>
        public static bool IsVendorEndpoint(HidDeviceDescriptor device)
        {
            if (device == null) return false;
            return device.UsagePage == VendorUsagePage && device.Usage == VendorUsage &&
                   device.InputReportLength == VendorReportLength &&
                   device.OutputReportLength == VendorReportLength;
        }

        public static bool IsNativeDevice(MouseBatteryInfo dev)
        {
            if (dev == null) return false;
            if (!string.Equals(dev.Brand, "VGN", StringComparison.OrdinalIgnoreCase)) return false;
            if (dev.Category != DeviceCategory.Keyboard) return false;
            if (string.IsNullOrEmpty(dev.DeviceId)) return false;
            return dev.DeviceId.StartsWith("VGN:320F:5088", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 由产品串推导展示型号："VGN N75 PRO 2.4G Dongle" -> "VGN N75 PRO"。
        /// </summary>
        public static string GetModelName(string productString)
        {
            if (string.IsNullOrEmpty(productString)) return "VGN N75 PRO";
            string name = productString.Trim();
            string[] suffixes = new string[] { " 2.4G DONGLE", " 2.4G Dongle", " 2.4G", " DONGLE", " Dongle", " RECEIVER", " Receiver" };
            for (int i = 0; i < suffixes.Length; i++)
            {
                if (name.Length > suffixes[i].Length && name.EndsWith(suffixes[i], StringComparison.OrdinalIgnoreCase))
                {
                    name = name.Substring(0, name.Length - suffixes[i].Length).Trim();
                    break;
                }
            }
            if (name.Length == 0) return "VGN N75 PRO";
            return name;
        }

        public static byte[] BuildBatteryQuery(ushort requestCode, int length)
        {
            if (requestCode == 0 || length < 10 || length > 65) throw new ArgumentOutOfRangeException("requestCode");
            byte[] report = new byte[length];
            report[0] = VendorReportId;
            report[1] = (byte)requestCode;
            report[2] = (byte)(requestCode >> 8);
            report[3] = OpcodeBattery;
            return report;
        }

        /// <summary>
        /// 解析电应答：必须完整回显序号与操作码，杜绝把上一次查询/其他通道的陈旧帧当成本次结果。
        /// </summary>
        public static bool TryParseBattery(byte[] report, ushort requestCode, out int battery, out bool charging)
        {
            battery = -1;
            charging = false;
            if (report == null || report.Length < 10 || requestCode == 0) return false;
            if (report[0] != VendorReportId) return false;
            if (report[1] != (byte)requestCode || report[2] != (byte)(requestCode >> 8)) return false;
            if (report[3] != OpcodeBattery) return false;
            if (report[8] > 100 || report[9] > 1) return false;
            battery = report[8];
            charging = (report[9] == 1);
            return true;
        }

        public static List<MouseBatteryInfo> QueryDevices()
        {
            lock (hidLock)
            {
                List<HidDeviceDescriptor> devices = HidTransport.Enumerate(VgnVid, N75ProDonglePid);
                HashSet<string> present = new HashSet<string>();
                List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
                bool due = (DateTime.UtcNow - lastQuery).TotalSeconds >= QueryThrottleSeconds;

                for (int i = 0; i < devices.Count; i++)
                {
                    HidDeviceDescriptor device = devices[i];
                    if (!IsSupportedDevice(device) || !IsVendorEndpoint(device)) continue;

                    string deviceId = "VGN:320F:5088:KB";
                    if (!present.Add(deviceId)) continue;

                    MouseBatteryInfo info;
                    cache.TryGetValue(deviceId, out info);
                    if (due || info == null)
                    {
                        int battery;
                        bool charging;
                        bool ok = TryReadBattery(device, out battery, out charging);
                        if (ok)
                        {
                            if (info == null) info = CreateEmptyInfo();
                            info.BatteryPercent = battery;
                            info.ExactBatteryPercent = battery;
                            info.IsCharging = charging;
                            info.IsConnected = true;
                            info.IsSleeping = false;
                            info.LastUpdated = DateTime.Now;
                            CachedBatteryPercent = battery;
                            CachedIsCharging = charging;
                            CachedLastUpdated = info.LastUpdated;
                            SaveHardwareCache();
                        }
                        else
                        {
                            // 接收器仍枚举得到 => 物理链路在线，只是键盘侧进入省电休眠（2.4G 不回应）。
                            // 与 Logitech / Rapoo 约定一致：保持 IsConnected 并置 IsSleeping，
                            // 主界面显示「休眠中」、托盘沿用最近一次缓存电量，绝不伪造新读数。
                            if (info == null) info = CreateEmptyInfo();
                            info.IsConnected = true;
                            info.IsSleeping = true;
                            info.IsCharging = false;
                            info.BatteryPercent = CachedBatteryPercent > 0 ? CachedBatteryPercent : -1;
                            info.ExactBatteryPercent = info.BatteryPercent;
                        }
                    }

                    info.DeviceId = deviceId;
                    info.HardwareId = "HID:320F:5088:KB";
                    info.HardwareFingerprint = info.HardwareId;
                    info.Transport = "2.4G 无线";
                    info.IsDonglePresent = true;
                    info.IsCustom = false;
                    result.Add(info);
                }

                List<string> removed = new List<string>();
                foreach (string key in cache.Keys)
                {
                    if (!present.Contains(key)) removed.Add(key);
                }
                for (int i = 0; i < removed.Count; i++) cache.Remove(removed[i]);

                if (due) lastQuery = DateTime.UtcNow;
                return result;
            }
        }

        private static MouseBatteryInfo CreateEmptyInfo()
        {
            MouseBatteryInfo info = new MouseBatteryInfo();
            info.Brand = "VGN";
            info.Category = DeviceCategory.Keyboard;
            info.DeviceName = CachedDeviceName;
            info.BatteryPercent = -1;
            info.ExactBatteryPercent = -1;
            // 带上休眠缓存时间戳，避免卡片显示 80% 却同时写「尚未读取电量」的矛盾文案。
            info.LastUpdated = CachedLastUpdated;
            info.Capabilities = new DeviceCapabilities();
            info.Capabilities.SupportsBattery = true;
            info.Capabilities.SupportsDpi = false;
            info.Capabilities.SupportsDpiStages = false;
            info.Capabilities.SupportsPollingRate = false;
            info.Capabilities.SupportsXyIndependence = false;
            info.Capabilities.SupportsHardwareDpiMonitor = false;
            info.CustomTooltip = "VGN N75 PRO 2.4G：FF1C/0092 64 字节厂商通道，Report 4 + 0x1A 查询；回包 [8]=电量，[9]=充电。";
            return info;
        }

        private static bool TryReadBattery(HidDeviceDescriptor device, out int battery, out bool charging)
        {
            battery = -1;
            charging = false;
            IntPtr handle = HidTransport.Open(device.DevicePath);
            if (!HidTransport.IsValidHandle(handle)) return false;
            try
            {
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    ushort requestCode = NextSequence();
                    byte[] request = BuildBatteryQuery(requestCode, device.OutputReportLength);
                    if (!HidTransport.WriteReport(handle, request, request.Length, WriteTimeoutMs)) continue;

                    Stopwatch timer = Stopwatch.StartNew();
                    while (timer.ElapsedMilliseconds < QueryTimeoutMs)
                    {
                        int remain = QueryTimeoutMs - (int)timer.ElapsedMilliseconds;
                        if (remain < 20) remain = 20;
                        byte[] response;
                        if (!HidTransport.ReadReportExact(handle, device.InputReportLength, remain, out response)) break;
                        int value;
                        bool isCharging;
                        if (TryParseBattery(response, requestCode, out value, out isCharging))
                        {
                            battery = value;
                            charging = isCharging;
                            return true;
                        }
                    }
                }
            }
            catch
            {
                return false;
            }
            finally
            {
                HidTransport.Close(handle);
            }
            return false;
        }

        private static ushort NextSequence()
        {
            sequence = (ushort)(sequence == ushort.MaxValue ? 1 : sequence + 1);
            return sequence;
        }

        public static void LoadHardwareCache()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(CacheRegistryKey, false))
                {
                    if (key == null) return;
                    object percent = key.GetValue("BatteryPercent");
                    if (percent != null)
                    {
                        int value = Convert.ToInt32(percent);
                        if (value >= 0 && value <= 100) CachedBatteryPercent = value;
                    }
                    object charging = key.GetValue("IsCharging");
                    if (charging != null) CachedIsCharging = (Convert.ToInt32(charging) == 1);
                    string name = key.GetValue("DeviceName") as string;
                    if (!string.IsNullOrEmpty(name)) CachedDeviceName = name;
                    string updated = key.GetValue("LastUpdated") as string;
                    if (!string.IsNullOrEmpty(updated))
                    {
                        DateTime dt;
                        if (DateTime.TryParse(updated, out dt)) CachedLastUpdated = dt;
                    }
                }
            }
            catch { }
        }

        public static void SaveHardwareCache()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(CacheRegistryKey))
                {
                    if (key == null) return;
                    if (CachedBatteryPercent >= 0) key.SetValue("BatteryPercent", CachedBatteryPercent);
                    key.SetValue("IsCharging", CachedIsCharging ? 1 : 0);
                    if (!string.IsNullOrEmpty(CachedDeviceName)) key.SetValue("DeviceName", CachedDeviceName);
                    if (CachedLastUpdated != DateTime.MinValue) key.SetValue("LastUpdated", CachedLastUpdated.ToString("o"));
                }
            }
            catch { }
        }
    }
}
