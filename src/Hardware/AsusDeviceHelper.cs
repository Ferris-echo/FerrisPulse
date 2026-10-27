// ============================================================================
// 模块名称: AsusDeviceHelper.cs / Hardware
// 核心职责:
//   1. ROG PELTA 0B05:1B84 的电量/充电双查询；Scope II RX (1AB5) 有线键盘仅识别。
//   2. ROG 夜魔 Azoth (1A85) 经由 ROG OMNI 接收器 (1ACE) 的配对/电量/充电双模探测。
// 协议依据:
//   G-Helper AsusHeadset/Pelta/AsusKeyboard 协议说明及本机实测，独立实现。
// 依赖关系: HidTransport.cs, MouseBatteryInfo.cs；仅 Win32 HID / .NET 4.0。
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// 1. C# 5.0 规范，保留 BOM，无外部依赖。
// 2. 仅精确型号白名单，禁止配置写入及虚构电量。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RazerBatteryTray
{
    public static class AsusDeviceHelper
    {
        public const int VendorId = 0x0B05;
        public const int PeltaReceiverPid = 0x1B84;
        public const int ScopeIIRxPid = 0x1AB5;
        public const int OmniReceiverPid = 0x1ACE;
        public const int AzothPid = 0x1A85;

        private const int QueryIntervalMs = 5000;
        private const int QueryTimeoutMs = 1000;
        private static readonly object hidLock = new object();
        private static readonly Dictionary<string, DeviceState> states = new Dictionary<string, DeviceState>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, MouseBatteryInfo> keyboardCache = new Dictionary<string, MouseBatteryInfo>(StringComparer.OrdinalIgnoreCase);

        private sealed class DeviceState
        {
            public int Battery = -1;
            public bool Charging;
            public bool Ready;
            public bool ChargingKnown;
            public bool HasQueried;
            public int QueryTick;
            public DateTime LastUpdated = DateTime.MinValue;
        }

        #region ROG PELTA & Scope II RX (PR #39)

        public static bool IsSupportedEndpoint(HidDeviceDescriptor descriptor)
        {
            if (descriptor == null || descriptor.VendorId != VendorId) return false;
            if (descriptor.ProductId != PeltaReceiverPid && descriptor.ProductId != ScopeIIRxPid) return false;
            int length = descriptor.ProductId == PeltaReceiverPid ? 64 : 65;
            return descriptor.UsagePage == 0xFF00 && descriptor.Usage == 1 &&
                descriptor.InputReportLength == length && descriptor.OutputReportLength == length;
        }

        private static bool IsReply(byte[] report, byte key)
        {
            if (report == null || report.Length != 64) return false;
            return report[0] == 0xCC && report[1] == 0x12 && report[2] == key &&
                report[3] == 0 && report[4] == 0 && !(report[5] == 0xFF && report[6] == 0xAA);
        }

        public static bool TryParseBatteryReport(byte[] report, out int battery)
        {
            battery = -1;
            if (!IsReply(report, 7) || report[6] > 100) return false;
            battery = report[6];
            return true;
        }

        public static bool TryParseChargingReport(byte[] report, out int charging)
        {
            charging = -1;
            if (!IsReply(report, 8) || report[5] > 1) return false;
            charging = report[5];
            return true;
        }

        private static byte[] Query(IntPtr handle, byte key)
        {
            byte[] tx = new byte[64];
            tx[0] = 0xCC;
            tx[1] = 0x12;
            tx[2] = key;
            Stopwatch deadline = Stopwatch.StartNew();
            if (!HidTransport.WriteReport(handle, tx, tx.Length, QueryTimeoutMs)) return null;
            for (int i = 0; i < 8; i++)
            {
                int remaining = QueryTimeoutMs - (int)deadline.ElapsedMilliseconds;
                if (remaining <= 0) break;
                byte[] rx;
                if (!HidTransport.ReadReport(handle, 64, remaining, out rx)) break;
                if (rx != null && rx.Length >= 3 && rx[0] == 0xCC && rx[1] == 0xFF && rx[2] == 0xAA) return null;
                if (IsReply(rx, key)) return rx;
            }
            return null;
        }

        private static void ReadState(HidDeviceDescriptor descriptor, DeviceState state)
        {
            state.Ready = false;
            state.Charging = false;
            state.ChargingKnown = false;
            IntPtr handle = HidTransport.Open(descriptor.DevicePath);
            if (!HidTransport.IsValidHandle(handle)) return;
            try
            {
                HidTransport.DrainReports(handle, 64, 3);
                int battery;
                if (!TryParseBatteryReport(Query(handle, 7), out battery)) return;
                state.Battery = battery;
                state.LastUpdated = DateTime.Now;
                state.Ready = true;
                int charging;
                if (TryParseChargingReport(Query(handle, 8), out charging))
                {
                    state.Charging = charging == 1;
                    state.ChargingKnown = true;
                }
            }
            finally
            {
                HidTransport.Close(handle);
            }
        }

        internal static MouseBatteryInfo BuildDeviceInfo(HidDeviceDescriptor descriptor, int battery, bool charging, bool ready, DateTime updated)
        {
            bool keyboard = descriptor.ProductId == ScopeIIRxPid;
            MouseBatteryInfo info = new MouseBatteryInfo();
            info.DeviceId = string.Format("ASUS:{0:X4}:{1:X4}", VendorId, descriptor.ProductId);
            info.HardwareFingerprint = string.Format("HID:{0:X4}:{1:X4}", VendorId, descriptor.ProductId);
            info.HardwareId = info.HardwareFingerprint;
            info.Brand = "ASUS";
            info.DeviceName = keyboard ? "ROG STRIX SCOPE II RX" : "ROG PELTA";
            info.Category = keyboard ? DeviceCategory.Keyboard : DeviceCategory.Headset;
            info.Transport = keyboard ? "USB 有线" : "2.4G 无线";
            info.IsConnected = keyboard || ready || battery >= 0;
            info.IsSleeping = !keyboard && !ready && battery >= 0;
            info.IsDonglePresent = !keyboard;
            info.IsCharging = !keyboard && ready && charging;
            info.BatteryPercent = keyboard ? -1 : battery;
            info.ExactBatteryPercent = info.BatteryPercent;
            info.LastUpdated = updated;
            info.CustomTooltip = keyboard ? "USB 有线键盘，无电池" : (!ready ? "接收器在位；耳机状态未确认，显示最后真实电量" : null);
            return info;
        }

        public static List<MouseBatteryInfo> QueryAllAsusDevices()
        {
            lock (hidLock)
            {
                List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
                List<HidDeviceDescriptor> descriptors = HidTransport.Enumerate(VendorId, 0);
                HashSet<int> seen = new HashSet<int>();
                foreach (HidDeviceDescriptor descriptor in descriptors)
                {
                    if (!IsSupportedEndpoint(descriptor) || !seen.Add(descriptor.ProductId)) continue;
                    if (descriptor.ProductId == ScopeIIRxPid)
                    {
                        result.Add(BuildDeviceInfo(descriptor, -1, false, true, DateTime.Now));
                        continue;
                    }
                    DeviceState state;
                    if (!states.TryGetValue(descriptor.DevicePath, out state))
                    {
                        state = new DeviceState();
                        states.Add(descriptor.DevicePath, state);
                    }
                    int elapsed = unchecked(Environment.TickCount - state.QueryTick);
                    if (!state.HasQueried || elapsed < 0 || elapsed >= QueryIntervalMs)
                    {
                        try { ReadState(descriptor, state); }
                        catch { state.Ready = false; state.Charging = false; state.ChargingKnown = false; }
                        state.QueryTick = Environment.TickCount;
                        state.HasQueried = true;
                    }
                    MouseBatteryInfo info = BuildDeviceInfo(descriptor, state.Battery, state.Charging, state.Ready, state.LastUpdated);
                    info.IsChargingUnknown = state.Ready && !state.ChargingKnown;
                    if (info.IsChargingUnknown) info.CustomTooltip = "电量已读取；充电状态未知";
                    result.Add(info);
                }
                return result;
            }
        }

        #endregion

        #region ROG 夜魔 Azoth & OMNI Receiver (PR #40)

        public static bool IsKeyboardEndpoint(HidDeviceDescriptor d)
        {
            return d != null && d.VendorId == VendorId && d.ProductId == OmniReceiverPid &&
                d.UsagePage == 0xFF00 && d.Usage == 1 &&
                d.InputReportLength == 64 && d.OutputReportLength == 64;
        }

        public static int ParseAzothPairing(byte[] r)
        {
            if (r == null || r.Length != 64 || r[0] != 1 || r[1] != 0xA0 || r[2] != 0 || r[3] != 1) return 0;
            for (int offset = 5; offset + 3 < r.Length; offset += 4)
            {
                int pid = r[offset] | (r[offset + 1] << 8);
                if (pid == 0) break;
                if (pid == AzothPid) return pid;
            }
            return 0;
        }

        public static bool TryParseBattery(byte[] r, out int battery, out bool charging)
        {
            battery = -1;
            charging = false;
            if (r == null || r.Length != 64 || r[0] != 2 || r[1] != 0x12 || r[2] != 1 ||
                r[5] != 0 || r[6] > 100 || r[9] > 1) return false;
            battery = r[6];
            charging = r[9] == 1;
            return true;
        }

        private static string ReceiverInstance(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string[] parts = path.Split('#');
            if (parts.Length < 3) return path;
            int cut = parts[2].LastIndexOf('&');
            return cut > 0 ? parts[2].Substring(0, cut) : parts[2];
        }

        private static byte[] Query(HidDeviceDescriptor endpoint, byte report, byte command, byte subcommand)
        {
            IntPtr handle = HidTransport.Open(endpoint.DevicePath);
            if (!HidTransport.IsValidHandle(handle)) return null;
            try
            {
                HidTransport.DrainReports(handle, 64, 3);
                byte[] tx = new byte[64];
                tx[0] = report;
                tx[1] = command;
                tx[2] = subcommand;
                if (!HidTransport.WriteReport(handle, tx, 64, 300)) return null;
                Stopwatch timer = Stopwatch.StartNew();
                while (timer.ElapsedMilliseconds < 450)
                {
                    byte[] rx;
                    int remaining = Math.Max(1, 450 - (int)timer.ElapsedMilliseconds);
                    if (!HidTransport.ReadReport(handle, 64, remaining, out rx)) return null;
                    if (rx.Length != 64 || rx[0] != report) continue;
                    if (rx[1] == 0xFF && rx[2] == 0xAA) return null;
                    if (rx[1] == command && rx[2] == subcommand) return rx;
                }
                return null;
            }
            finally { HidTransport.Close(handle); }
        }

        public static List<MouseBatteryInfo> QueryAllKeyboards()
        {
            lock (hidLock)
            {
                List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
                List<HidDeviceDescriptor> endpoints = HidTransport.Enumerate(VendorId, OmniReceiverPid);
                foreach (HidDeviceDescriptor keyboard in endpoints)
                {
                    if (!IsKeyboardEndpoint(keyboard)) continue;
                    string instance = ReceiverInstance(keyboard.DevicePath);
                    HidDeviceDescriptor receiver = endpoints.Find(delegate(HidDeviceDescriptor d)
                    {
                        return d.UsagePage == 0xFF02 && d.Usage == 1 && d.InputReportLength == 64 &&
                            d.OutputReportLength == 64 && ReceiverInstance(d.DevicePath) == instance;
                    });
                    if (receiver == null) continue;
                    int pid = ParseAzothPairing(Query(receiver, 1, 0xA0, 0));
                    if (pid != AzothPid) continue;
                    string id = "ASUS:0B05:1ACE:KB:" + instance;
                    MouseBatteryInfo previous;
                    keyboardCache.TryGetValue(id, out previous);
                    MouseBatteryInfo info = new MouseBatteryInfo();
                    info.DeviceId = id;
                    info.HardwareFingerprint = "HID:0B05:1ACE:KB:" + instance;
                    info.HardwareId = keyboard.DevicePath;
                    info.Brand = "ASUS";
                    info.Category = DeviceCategory.Keyboard;
                    info.DeviceName = "ROG 夜魔 (Azoth)";
                    info.Transport = "2.4G 无线";
                    info.IsDonglePresent = true;
                    info.BatteryPercent = previous != null ? previous.BatteryPercent : -1;
                    info.LastUpdated = previous != null ? previous.LastUpdated : DateTime.MinValue;
                    int battery;
                    bool charging;
                    byte[] rx = Query(keyboard, 2, 0x12, 1);
                    if (!TryParseBattery(rx, out battery, out charging))
                    {
                        Query(keyboard, 2, 0x12, 0);
                        rx = Query(keyboard, 2, 0x12, 1);
                    }
                    if (TryParseBattery(rx, out battery, out charging))
                    {
                        info.BatteryPercent = battery;
                        info.IsCharging = charging;
                        info.IsConnected = true;
                        info.LastUpdated = DateTime.Now;
                    }
                    else
                    {
                        info.IsSleeping = true;
                        info.IsConnected = false;
                        info.IsCharging = false;
                    }
                    info.ExactBatteryPercent = info.BatteryPercent;
                    keyboardCache[id] = info;
                    result.Add(info);
                }
                return result;
            }
        }

        public static bool MergeLegacyKeyboardConfig(List<MouseBatteryInfo> devices, CustomDeviceConfig cfg)
        {
            if (devices == null || cfg == null ||
                (cfg.Category != DeviceCategory.Dongle && cfg.Category != DeviceCategory.Generic && cfg.Category != DeviceCategory.Keyboard)) return false;
            if (!string.Equals(cfg.DeviceId, "HID:0B05:1ACE", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(cfg.DeviceId, "Gaming:0B05:1ACE", StringComparison.OrdinalIgnoreCase)) return false;
            List<MouseBatteryInfo> matches = devices.FindAll(delegate(MouseBatteryInfo d)
            {
                return d.DeviceId != null && d.DeviceId.StartsWith("ASUS:0B05:1ACE:KB:", StringComparison.OrdinalIgnoreCase) && d.Category == DeviceCategory.Keyboard;
            });
            if (matches.Count != 1) return false;
            matches[0].IsCustom = true;
            if (!string.IsNullOrEmpty(cfg.CustomName) && !cfg.CustomName.Equals("ROG OMNI RECEIVER", StringComparison.OrdinalIgnoreCase))
                matches[0].CustomName = cfg.CustomName;
            return true;
        }

        #endregion
    }
}
