// ============================================================================
// 模块名称: AsusDeviceHelper.Rog.cs / Hardware
// 核心职责:
//   华硕 ROG AZOTH 96 HE Wireless (0B05:1AD0)、ROG DELTA II (0B05:1AFA) 与
//   ROG AZOTH 96 HE 有线版 (0B05:1C10) 的精确端点隔离、只读电量/充电解析、
//   冷启动休眠缓存与有线优先仲裁。
// 协议依据 & 贡献署名:
//   由 B 站社区贡献者“工程机风格”提供实机抓包验证与驱动实现，结合 G-Helper 协议交叉核对。
// 依赖关系: AsusDeviceHelper.cs, HidTransport.cs, DriverStateStore.cs, MouseBatteryInfo.cs
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// 1. C# 5.0 规范，保留 BOM，无外部依赖（禁止 $"..." / ?. / nameof / out var）。
// 2. 仅精确型号与四元组端点白名单，未实机验证的 DPI/回报率写指令能力位一律为 false。
// 3. 查询失败保留最后真实电量并清除实时在线/充电声明，绝不虚构电量。
// ============================================================================
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RazerBatteryTray
{
    public static partial class AsusDeviceHelper
    {
        public const int Azoth96HeWirelessPid = 0x1AD0; // ROG SpeedNova 8K Receiver -> AZOTH 96 HE Wireless
        public const int DeltaIIPid = 0x1AFA;           // ROG DELTA II (2.4GHz)
        public const int Azoth96HeWiredPid = 0x1C10;    // ROG AZOTH 96 HE (USB 有线)

        private const int RogUsagePageVendor = 0xFF00;
        private const int RogUsageControl = 0x0001;

        private const byte RogReportIdWirelessKeyboard = 0x02;
        private const byte RogReportIdWiredKeyboard = 0x00;
        private const byte RogReportIdHeadset = 0xCC;

        private const int RogWirelessPacketSize = 64;
        private const int RogWiredPacketSize = 65;
        private const int RogHeadsetPacketSize = 64;

        private static readonly Dictionary<string, RogDevState> rogStates =
            new Dictionary<string, RogDevState>(StringComparer.OrdinalIgnoreCase);

        private sealed class RogDevState
        {
            public int Battery = -1;
            public bool Charging;
            public bool Ready;
            public bool ChargingKnown;
            public bool HasQueried;
            public int QueryTick;
            public DateTime LastUpdated = DateTime.MinValue;
        }

        public static bool IsRogEndpoint(HidDeviceDescriptor d, int pid, int packetSize)
        {
            return d != null &&
                   d.VendorId == VendorId &&
                   d.ProductId == pid &&
                   d.UsagePage == RogUsagePageVendor &&
                   d.Usage == RogUsageControl &&
                   d.InputReportLength == packetSize &&
                   d.OutputReportLength == packetSize;
        }

        public static bool TryParseRogReady(byte[] rx, byte expectedReportId, int expectedLength)
        {
            if (rx == null || rx.Length != expectedLength || rx.Length < 6) return false;
            if (rx[0] != expectedReportId || rx[1] != 0x12 || rx[2] != 0x00) return false;
            return rx[5] != 0xFA;
        }

        public static bool TryParseRogAzoth96Battery(byte[] rx, byte expectedReportId, int expectedLength, out int battery, out bool charging)
        {
            battery = -1;
            charging = false;
            if (rx == null || rx.Length != expectedLength || rx.Length < 10) return false;
            if (rx[1] == 0xFF && rx[2] == 0xAA) return false;
            if (rx[0] != expectedReportId || rx[1] != 0x12 || rx[2] != 0x01) return false;
            if (rx[5] != 0 || rx[6] > 100 || rx[9] > 1) return false;
            battery = rx[6];
            charging = (rx[9] == 1);
            return true;
        }

        private static RogDevState GetRogState(string path, int pid, string defaultName)
        {
            string key = path ?? "";
            RogDevState st;
            if (!rogStates.TryGetValue(key, out st))
            {
                st = new RogDevState();
                string deviceId = string.Format("ASUS:{0:X4}:{1:X4}", VendorId, pid);
                int cachedBatt;
                bool cachedChg;
                string cachedName;
                DateTime cachedUpdated;
                if (DriverStateStore.TryLoadBatteryState(deviceId, out cachedBatt, out cachedChg, out cachedName, out cachedUpdated))
                {
                    st.Battery = cachedBatt;
                    st.LastUpdated = cachedUpdated;
                }
                rogStates.Add(key, st);
            }
            return st;
        }

        private static bool NeedRogQuery(RogDevState st)
        {
            int elapsed = unchecked(Environment.TickCount - st.QueryTick);
            return !st.HasQueried || elapsed < 0 || elapsed >= QueryIntervalMs;
        }

        private static byte[] QueryRogPacket(IntPtr handle, byte reportId, int packetSize, byte key)
        {
            byte[] tx = new byte[packetSize];
            tx[0] = reportId;
            tx[1] = 0x12;
            tx[2] = key;
            if (!HidTransport.WriteReport(handle, tx, tx.Length, QueryTimeoutMs)) return null;

            Stopwatch deadline = Stopwatch.StartNew();
            for (int i = 0; i < 8; i++)
            {
                int remaining = QueryTimeoutMs - (int)deadline.ElapsedMilliseconds;
                if (remaining <= 0) break;

                byte[] rx;
                if (!HidTransport.ReadReport(handle, packetSize, remaining, out rx)) break;
                if (rx == null || rx.Length < 6) continue;
                if (rx[1] == 0xFF && rx[2] == 0xAA) return null;
                if (rx[0] == reportId && rx[1] == 0x12 && rx[2] == key) return rx;
            }
            return null;
        }

        private static void ReadAzoth96KeyboardState(
            HidDeviceDescriptor d,
            RogDevState st,
            byte reportId,
            int packetSize,
            int pid,
            string deviceName)
        {
            st.Ready = false;
            st.Charging = false;
            if (d == null || string.IsNullOrEmpty(d.DevicePath)) return;

            IntPtr handle = HidTransport.Open(d.DevicePath);
            if (!HidTransport.IsValidHandle(handle)) return;
            try
            {
                HidTransport.DrainReports(handle, packetSize, 3);

                byte[] readyPacket = QueryRogPacket(handle, reportId, packetSize, 0x00);
                st.Ready = TryParseRogReady(readyPacket, reportId, packetSize);

                byte[] battPacket = QueryRogPacket(handle, reportId, packetSize, 0x01);
                int battery;
                bool charging;
                if (TryParseRogAzoth96Battery(battPacket, reportId, packetSize, out battery, out charging))
                {
                    st.Battery = battery;
                    st.Charging = charging;
                    st.LastUpdated = DateTime.Now;
                    string deviceId = string.Format("ASUS:{0:X4}:{1:X4}", VendorId, pid);
                    DriverStateStore.SaveBatteryState(deviceId, st.Battery, st.Charging, deviceName);
                }
            }
            finally
            {
                HidTransport.Close(handle);
            }
        }

        private static void ReadWirelessKeyboardState(HidDeviceDescriptor d, RogDevState st)
        {
            ReadAzoth96KeyboardState(
                d, st, RogReportIdWirelessKeyboard, RogWirelessPacketSize,
                Azoth96HeWirelessPid, "ROG AZOTH 96 HE Wireless");
        }

        private static void ReadWiredKeyboardState(HidDeviceDescriptor d, RogDevState st)
        {
            ReadAzoth96KeyboardState(
                d, st, RogReportIdWiredKeyboard, RogWiredPacketSize,
                Azoth96HeWiredPid, "ROG AZOTH 96 HE");
        }

        private static void ReadDeltaIIHeadsetState(HidDeviceDescriptor d, RogDevState st)
        {
            st.Ready = false;
            st.Charging = false;
            st.ChargingKnown = false;
            if (d == null || string.IsNullOrEmpty(d.DevicePath)) return;

            IntPtr handle = HidTransport.Open(d.DevicePath);
            if (!HidTransport.IsValidHandle(handle)) return;
            try
            {
                HidTransport.DrainReports(handle, RogHeadsetPacketSize, 3);

                byte[] battPacket = QueryRogPacket(handle, RogReportIdHeadset, RogHeadsetPacketSize, 0x07);
                int battery;
                if (!TryParseBatteryReport(battPacket, out battery)) return;

                st.Battery = battery;
                st.LastUpdated = DateTime.Now;
                st.Ready = true;

                byte[] chgPacket = QueryRogPacket(handle, RogReportIdHeadset, RogHeadsetPacketSize, 0x08);
                int charging;
                if (TryParseChargingReport(chgPacket, out charging))
                {
                    st.Charging = (charging == 1);
                    st.ChargingKnown = true;
                }

                string deviceId = string.Format("ASUS:{0:X4}:{1:X4}", VendorId, DeltaIIPid);
                DriverStateStore.SaveBatteryState(deviceId, st.Battery, st.Charging, "ROG DELTA II (2.4GHz)");
            }
            finally
            {
                HidTransport.Close(handle);
            }
        }

        internal static MouseBatteryInfo BuildRogDeviceInfo(HidDeviceDescriptor d, int battery, bool charging, bool ready, DateTime updated)
        {
            int pid = d != null ? d.ProductId : 0;
            MouseBatteryInfo info = new MouseBatteryInfo();
            info.DeviceId = string.Format("ASUS:{0:X4}:{1:X4}", VendorId, pid);
            info.HardwareFingerprint = string.Format("HID:{0:X4}:{1:X4}", VendorId, pid);
            info.HardwareId = info.HardwareFingerprint;
            info.Brand = "ASUS";
            info.DriverId = "BuiltIn:Asus";
            info.BatteryPercent = battery;
            info.ExactBatteryPercent = battery;
            info.LastUpdated = updated;

            info.Capabilities.SupportsDpi = false;
            info.Capabilities.SupportsDpiStages = false;
            info.Capabilities.SupportsXyIndependence = false;
            info.Capabilities.SupportsPollingRate = false;
            info.Capabilities.SupportsHardwareDpiMonitor = false;

            if (pid == DeltaIIPid)
            {
                info.DeviceName = "ROG DELTA II (2.4GHz)";
                info.Category = DeviceCategory.Headset;
                info.Transport = "2.4G 无线";
                info.IsDonglePresent = true;
                info.IsConnected = ready || battery >= 0;
                info.IsSleeping = !ready && battery >= 0;
                info.IsCharging = ready && charging;
                info.Capabilities.SupportsBattery = true;
                info.CustomTooltip = !ready ? "接收器在位；耳机状态未确认，显示最后真实电量" : null;
            }
            else if (pid == Azoth96HeWiredPid)
            {
                info.DeviceName = "ROG AZOTH 96 HE";
                info.Category = DeviceCategory.Keyboard;
                info.Transport = "USB 有线";
                info.IsConnected = true;
                info.IsCharging = ready && charging;
                info.Capabilities.SupportsBattery = (battery >= 0);
                info.CustomTooltip = battery < 0
                    ? "USB 有线键盘；控制端点被奥创中心（Armoury Crate）独占时无法读取电量"
                    : null;
            }
            else
            {
                info.DeviceName = "ROG AZOTH 96 HE Wireless";
                info.Category = DeviceCategory.Keyboard;
                info.Transport = "2.4G 无线 (SpeedNova 8K)";
                info.IsDonglePresent = true;
                info.IsConnected = ready || battery >= 0;
                info.IsSleeping = !ready && battery >= 0;
                info.IsCharging = ready && charging;
                info.Capabilities.SupportsBattery = (battery >= 0);
                info.CustomTooltip = battery < 0
                    ? "无线键盘深度休眠，唤醒（敲键）后 5 秒内自动读到电量；控制端点被奥创中心（Armoury Crate）独占时无法读取"
                    : null;
            }

            return info;
        }

        public static List<MouseBatteryInfo> QueryAllRogDevices()
        {
            lock (hidLock)
            {
                List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
                List<HidDeviceDescriptor> descriptors = HidTransport.Enumerate(VendorId, 0);
                HashSet<int> seen = new HashSet<int>();

                for (int i = 0; i < descriptors.Count; i++)
                {
                    HidDeviceDescriptor d = descriptors[i];
                    if (!IsRogEndpoint(d, DeltaIIPid, RogHeadsetPacketSize)) continue;
                    if (!seen.Add(DeltaIIPid)) continue;

                    RogDevState st = GetRogState(d.DevicePath, DeltaIIPid, "ROG DELTA II (2.4GHz)");
                    if (NeedRogQuery(st))
                    {
                        try { ReadDeltaIIHeadsetState(d, st); }
                        catch { st.Ready = false; st.Charging = false; st.ChargingKnown = false; }
                        st.QueryTick = Environment.TickCount;
                        st.HasQueried = true;
                    }

                    MouseBatteryInfo info = BuildRogDeviceInfo(d, st.Battery, st.Charging, st.Ready, st.LastUpdated);
                    info.IsChargingUnknown = st.Ready && !st.ChargingKnown;
                    if (info.IsChargingUnknown) info.CustomTooltip = "电量已读取；充电状态未知";
                    result.Add(info);
                }
                return result;
            }
        }

        public static List<MouseBatteryInfo> QueryAllRogKeyboards()
        {
            lock (hidLock)
            {
                List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
                List<HidDeviceDescriptor> descriptors = HidTransport.Enumerate(VendorId, 0);
                HashSet<int> seen = new HashSet<int>();

                for (int i = 0; i < descriptors.Count; i++)
                {
                    HidDeviceDescriptor d = descriptors[i];
                    if (d == null || d.VendorId != VendorId) continue;

                    if (d.ProductId == Azoth96HeWirelessPid && IsRogEndpoint(d, Azoth96HeWirelessPid, RogWirelessPacketSize))
                    {
                        if (!seen.Add(Azoth96HeWirelessPid)) continue;
                        RogDevState st = GetRogState(d.DevicePath, Azoth96HeWirelessPid, "ROG AZOTH 96 HE Wireless");
                        if (NeedRogQuery(st))
                        {
                            try { ReadWirelessKeyboardState(d, st); }
                            catch { st.Ready = false; st.Charging = false; }
                            st.QueryTick = Environment.TickCount;
                            st.HasQueried = true;
                        }
                        result.Add(BuildRogDeviceInfo(d, st.Battery, st.Charging, st.Ready, st.LastUpdated));
                    }
                    else if (d.ProductId == Azoth96HeWiredPid && IsRogEndpoint(d, Azoth96HeWiredPid, RogWiredPacketSize))
                    {
                        if (!seen.Add(Azoth96HeWiredPid)) continue;
                        RogDevState st = GetRogState(d.DevicePath, Azoth96HeWiredPid, "ROG AZOTH 96 HE");
                        if (NeedRogQuery(st))
                        {
                            try { ReadWiredKeyboardState(d, st); }
                            catch { st.Ready = false; st.Charging = false; }
                            st.QueryTick = Environment.TickCount;
                            st.HasQueried = true;
                        }
                        result.Add(BuildRogDeviceInfo(d, st.Battery, st.Charging, st.Ready, st.LastUpdated));
                    }
                }

                DeviceManager.ApplyCrossTransportKeyboardArbitration(result);
                return result;
            }
        }
    }
}
