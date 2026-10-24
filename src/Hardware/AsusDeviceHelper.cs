// ============================================================================
// 模块名称: AsusDeviceHelper.cs / Hardware
// 核心职责: ROG PELTA 0B05:1B84 的电量/充电双查询；Scope II RX 有线键盘仅识别。
// 协议依据: G-Helper AsusHeadset/Pelta 协议说明及本机 2026-10-24 实测，独立实现。
// 依赖关系: HidTransport, MouseBatteryInfo；仅 Win32 HID / .NET 4.0。
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// C# 5.0；UTF-8 BOM；仅精确型号白名单；禁止配置写入及虚构电量。
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
        private const int QueryIntervalMs = 5000;
        // First RF reply after wake measured at 750ms; keep a bounded 1s query budget.
        private const int QueryTimeoutMs = 1000;
        private static readonly object hidLock = new object();
        private static readonly Dictionary<string, DeviceState> states = new Dictionary<string, DeviceState>(StringComparer.OrdinalIgnoreCase);

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
            // Unsolicited notifications can precede the matching reply. All reads share one deadline.
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
                        continue; // Wired keyboard: do not send any power/configuration commands.
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
                        // Always advance on failure too: sleeping hardware must not cause rapid retries.
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
    }
}