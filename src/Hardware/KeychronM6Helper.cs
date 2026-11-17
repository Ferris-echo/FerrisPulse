// ============================================================================
// 模块名称: KeychronM6Helper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   Keychron M6 鼠标内置专用 HID 驱动协议引擎：
//   1. 支持 Keychron Link-KM 接收器 (3434:D026) 与 M6 USB 有线模式 (3434:D060)，
//      严格限定 FFC1/0001 (64/64/0) 控制端点并通过 B5 02 身份帧核验 3434:D060。
//   2. 支持 B3 06 电量/充电实时状态读取 (offset 20 低 7 位电量，0x80 位实测充电标志)。
//   3. 支持 HKCU\Software\FerrisPulse\Keychron\WiredReceivers 的确认关联关系解析，
//      通过 PhysicalUnitId 自动合并同实物 USB/RF 状态（实时优先于休眠，有线优先于接收器）。
//   4. 感谢社区开发者 @GaiusC 贡献实机身份/电量抓取 (2026-11-08) 与有线/拔线充电实测 (2026-11-09)。
// 依赖关系: HidTransport.cs, DriverStateStore.cs, HardwareDriverRegistry.cs, MouseBatteryInfo.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、协议解析或签名时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止 $"..." 插值、禁止 ?. 空条件运算符、禁止 nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    public static class KeychronM6Helper
    {
        public const string DriverKey = "BuiltIn:KeychronM6";
        internal const string ReceiverKey = "Keychron:3434:D026:MS";
        internal const string WiredKey = "Keychron:3434:D060:MS";
        internal const string ModelName = "Keychron M6";
        internal const string LinksKey = @"Software\FerrisPulse\Keychron\WiredReceivers";

        private static readonly object hidLock = new object();
        private static readonly Dictionary<string, string> verifiedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, MouseBatteryInfo> lastReadings =
            new Dictionary<string, MouseBatteryInfo>(StringComparer.OrdinalIgnoreCase);

        internal static bool IsControlEndpoint(HidDeviceDescriptor d)
        {
            return d != null && d.VendorId == 0x3434 && (d.ProductId == 0xD026 || d.ProductId == 0xD060) &&
                d.UsagePage == 0xFFC1 && d.Usage == 1 && d.InputReportLength == 64 &&
                d.OutputReportLength == 64 && d.FeatureReportLength == 0;
        }

        internal static bool IsM6Identity(byte[] report)
        {
            return report != null && report.Length == 64 && report[0] == 0xB6 && report[1] == 2 &&
                report[4] == 0x34 && report[5] == 0x34 && report[6] == 0x60 && report[7] == 0xD0;
        }

        private static string DeviceKey(HidDeviceDescriptor endpoint)
        {
            return endpoint.ProductId == 0xD026 ? ReceiverKey : WiredKey;
        }

        internal static bool AcceptIdentity(HidDeviceDescriptor endpoint, byte[] identity)
        {
            if (!IsControlEndpoint(endpoint) || string.IsNullOrEmpty(endpoint.DevicePath)) return false;
            lock (hidLock)
            {
                if (!IsM6Identity(identity))
                {
                    verifiedPaths.Remove(endpoint.DevicePath);
                    return false;
                }
                verifiedPaths[endpoint.DevicePath] = DeviceKey(endpoint);
                return true;
            }
        }

        public static bool IsVerifiedDevice(MouseBatteryInfo dev)
        {
            if (dev == null || dev.Category != DeviceCategory.Mouse || string.IsNullOrEmpty(dev.HardwareId)) return false;
            lock (hidLock)
            {
                string key;
                return verifiedPaths.TryGetValue(dev.HardwareId, out key) &&
                    string.Equals(key, dev.DeviceId, StringComparison.OrdinalIgnoreCase);
            }
        }

        internal static bool TryParseBattery(byte[] report, out int percent, out bool charging)
        {
            percent = -1;
            charging = false;
            if (report == null || report.Length != 64 || report[0] != 0xB4 || report[1] != 6) return false;
            int value = report[20] & 0x7F;
            if (value > 100) return false;
            percent = value;
            charging = (report[20] & 0x80) != 0;
            return true;
        }

        private static byte[] Query(IntPtr handle, byte report, byte command, byte target)
        {
            byte[] tx = new byte[64];
            tx[0] = report;
            tx[1] = command;
            tx[2] = target;
            if (!HidTransport.WriteReport(handle, tx, 64, 500)) return null;
            Stopwatch timer = Stopwatch.StartNew();
            int remaining;
            while ((remaining = 500 - (int)timer.ElapsedMilliseconds) > 0)
            {
                byte[] rx;
                if (!HidTransport.ReadReportExact(handle, 64, remaining, out rx)) return null;
                if (rx[0] == report + 1 && rx[1] == command) return rx;
            }
            return null;
        }

        private static string ReceiverUnit(string path)
        {
            return string.IsNullOrEmpty(path) ? null : ReceiverKey + ":Path:" + path.ToUpperInvariant();
        }

        // New mappings may use Keychron:ReceiverPath:<exact RF control endpoint path>.
        // The old ReceiverKey-only mapping is accepted only with one verified RF endpoint.
        internal static string ResolveReceiverLink(string link)
        {
            lock (hidLock)
            {
                const string prefix = "Keychron:ReceiverPath:";
                if (link != null && link.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    string path = link.Substring(prefix.Length);
                    string key;
                    return verifiedPaths.TryGetValue(path, out key) && key == ReceiverKey ? ReceiverUnit(path) : null;
                }
                if (link != ReceiverKey) return null;
                string only = null;
                foreach (KeyValuePair<string, string> entry in verifiedPaths)
                {
                    if (entry.Value != ReceiverKey) continue;
                    if (only != null) return null;
                    only = entry.Key;
                }
                return ReceiverUnit(only);
            }
        }

        private static string GetLinkedReceiver(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(LinksKey))
                {
                    string link = key == null ? null : key.GetValue(path) as string;
                    return ResolveReceiverLink(link);
                }
            }
            catch
            {
                return null;
            }
        }

        internal static MouseBatteryInfo CreateState(HidDeviceDescriptor endpoint, int percent,
            bool charging, bool sleeping, DateTime updated)
        {
            bool wireless = endpoint.ProductId == 0xD026;
            MouseBatteryInfo dev = new MouseBatteryInfo();
            dev.DeviceId = wireless ? ReceiverKey : WiredKey;
            dev.HardwareFingerprint = "HID:3434:" + endpoint.ProductId.ToString("X4") + ":MS";
            dev.HardwareId = endpoint.DevicePath;
            dev.PhysicalUnitId = wireless ? ReceiverUnit(endpoint.DevicePath) : GetLinkedReceiver(endpoint.DevicePath);
            dev.DriverId = DriverKey;
            dev.Brand = "Keychron";
            dev.DeviceName = ModelName;
            dev.Category = DeviceCategory.Mouse;
            dev.Transport = wireless ? "2.4G 无线" : "USB 有线";
            dev.BatteryPercent = percent;
            dev.ExactBatteryPercent = percent;
            dev.IsConnected = true;
            dev.IsDonglePresent = wireless;
            dev.IsSleeping = sleeping;
            dev.IsCharging = !sleeping && charging;
            dev.IsChargingUnknown = sleeping;
            dev.LastUpdated = updated;
            dev.Capabilities.SupportsBattery = true;
            dev.SupportsDpiWrite = false;
            dev.SupportsPollingRateWrite = false;
            return dev;
        }

        internal static string PhysicalUnit(MouseBatteryInfo state)
        {
            return state.IsDonglePresent ? ReceiverUnit(state.HardwareId) : GetLinkedReceiver(state.HardwareId);
        }

        internal static void MergeTransportState(List<MouseBatteryInfo> devices, MouseBatteryInfo state)
        {
            string unit = PhysicalUnit(state);
            int existing = devices.FindIndex(delegate(MouseBatteryInfo d)
            {
                return !string.IsNullOrEmpty(unit) && string.Equals(PhysicalUnit(d), unit, StringComparison.OrdinalIgnoreCase);
            });
            if (existing < 0) devices.Add(state);
            else if (!state.IsSleeping && (devices[existing].IsSleeping || !state.IsDonglePresent)) devices[existing] = state;
        }

        internal static MouseBatteryInfo CachedState(HidDeviceDescriptor endpoint)
        {
            MouseBatteryInfo previous;
            if (lastReadings.TryGetValue(endpoint.DevicePath, out previous))
            {
                return CreateState(endpoint, previous.BatteryPercent, false, true, previous.LastUpdated);
            }
            string cacheKey = (endpoint.ProductId == 0xD026 ? ReceiverKey : WiredKey) + ":" + endpoint.DevicePath;
            string name;
            DateTime updated;
            int percent;
            bool charging;
            if (DriverStateStore.TryLoadBatteryState(cacheKey, out percent, out charging, out name, out updated) && name == ModelName)
            {
                return CreateState(endpoint, percent, false, true, updated);
            }
            return null;
        }

        public static List<MouseBatteryInfo> QueryDevices()
        {
            List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
            lock (hidLock)
            {
                verifiedPaths.Clear();
                List<MouseBatteryInfo> readings = new List<MouseBatteryInfo>();
                foreach (HidDeviceDescriptor endpoint in HidTransport.Enumerate(0x3434, 0))
                {
                    if (!IsControlEndpoint(endpoint)) continue;
                    if (HardwareDriverRegistry.IsExternallyOwnedPath(endpoint.DevicePath)) continue;
                    string cacheKey = (endpoint.ProductId == 0xD026 ? ReceiverKey : WiredKey) + ":" + endpoint.DevicePath;
                    IntPtr handle = HidTransport.Open(endpoint.DevicePath);
                    if (!HidTransport.IsValidHandle(handle)) continue;
                    try
                    {
                        byte[] identity = Query(handle, 0xB5, 2, endpoint.ProductId == 0xD026 ? (byte)1 : (byte)0);
                        bool verified = identity != null && AcceptIdentity(endpoint, identity);
                        if (identity != null && !verified)
                        {
                            lastReadings.Remove(endpoint.DevicePath);
                            DriverStateStore.ClearDeviceState(cacheKey);
                            continue;
                        }
                        int percent;
                        bool charging;
                        MouseBatteryInfo state = null;
                        if (verified && TryParseBattery(Query(handle, 0xB3, 6, 0), out percent, out charging))
                        {
                            state = CreateState(endpoint, percent, charging, false, DateTime.Now);
                            lastReadings[endpoint.DevicePath] = state;
                            DriverStateStore.SaveBatteryState(cacheKey, percent, charging, ModelName);
                        }
                        else
                        {
                            state = CachedState(endpoint);
                        }
                        if (verified || state != null)
                        {
                            verifiedPaths[endpoint.DevicePath] = DeviceKey(endpoint);
                        }
                        if (state != null) readings.Add(state);
                    }
                    finally
                    {
                        HidTransport.Close(handle);
                    }
                }
                // Resolve legacy associations only after every current verified receiver is known.
                foreach (MouseBatteryInfo state in readings)
                {
                    state.PhysicalUnitId = PhysicalUnit(state);
                    MergeTransportState(result, state);
                }
            }
            return result;
        }
    }
}
