// ============================================================================
// 模块名称: SonyInzoneHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   索尼 (Sony) INZONE 系列 2.4GHz USB 接收器私有 HID 协议适配：
//   - INZONE Buds (WF-G700N, 054C:0EC2)：双耳 + 充电盒三路电量；
//   - INZONE H5   (WH-G500, 054C:0EBF)：头戴式单路电量 + 充电状态。
//   控制通道 UsagePage 0xFF04 / Usage 0x0001、Report ID 0x02、64 字节报告，
//   载体内为 Sony 私有 HCI 帧（key id 0xC396，event 0x04 = BatteryInfo）。
//   电池事件按型号分宽度：头戴 2 字节 [charger, percent]，入耳 6 字节三组状态对。
// 依赖关系: HidTransport.cs, MouseBatteryInfo.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 修改帧格式、电量解析或设备识别时必须同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止字符串插值、空条件运算符、nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;

namespace RazerBatteryTray
{
    public class SonyInzoneBudsState
    {
        public int LeftBattery;      // -1 表示盒内/离线
        public bool LeftCharging;
        public int RightBattery;     // -1 表示盒内/离线
        public bool RightCharging;
        public int CaseBattery;      // -1 表示不可用
        public bool CaseCharging;
        public bool IsConnected;
        public bool IsHeadset;       // true = 头戴式（仅单路电量，状态字节即充电标志）

        public SonyInzoneBudsState()
        {
            LeftBattery = -1;
            RightBattery = -1;
            CaseBattery = -1;
            LeftCharging = false;
            RightCharging = false;
            CaseCharging = false;
            IsConnected = false;
            IsHeadset = false;
        }
    }

    public static class SonyInzoneHelper
    {
        private static readonly object hidLock = new object();

        public const int VendorIdSony = 0x054C;
        public const int PidInzoneBuds = 0x0EC2;
        public const int PidInzoneH5 = 0x0EBF;

        public const int TargetUsagePage = 0xFF04;
        public const int TargetUsage = 0x0001;
        public const byte ReportId = 0x02;
        public const int ReportLength = 64;

        // Sony 私有 HCI 帧常量（与 openinzone PROTOCOL.md / sony-h5-tray proto.rs 一致）
        public const byte SonyKeyLo = 0x96;      // key id 0xC396 低字节
        public const byte SonyKeyHi = 0xC3;      // key id 0xC396 高字节
        public const byte AddressRxFromPc = 0x41; // dst=RX(4) << 4 | src=PC(1)
        public const byte EventIdBattery = 0x04;  // BatteryInfo
        public const byte EventTypeGet = 0x01;
        public const byte EventTypeRet = 0x10;

        private const int ReadTimeoutMs = 500;
        private const int QueryThrottleMs = 1000;

        private static ushort sequenceCounter = 1;
        private static DateTime lastQueryTime = DateTime.MinValue;
        private static List<MouseBatteryInfo> cachedDevices = new List<MouseBatteryInfo>();

        // 型号档案：新增 INZONE 型号只需在此追加一条，无需改动查询主流程。
        private class SonyModelProfile
        {
            public int ProductId;
            public string DeviceId;
            public string DeviceName;
            public string CustomName;
            public string HardwareId;
            public string Fingerprint;
            public DeviceCategory Category;
            public bool IsHeadset;
            public string SleepTooltip;

            public SonyModelProfile(int productId, string deviceId, string deviceName, string customName,
                string hardwareId, string fingerprint, DeviceCategory category, bool isHeadset, string sleepTooltip)
            {
                ProductId = productId;
                DeviceId = deviceId;
                DeviceName = deviceName;
                CustomName = customName;
                HardwareId = hardwareId;
                Fingerprint = fingerprint;
                Category = category;
                IsHeadset = isHeadset;
                SleepTooltip = sleepTooltip;
            }
        }

        private static SonyModelProfile[] BuildProfiles()
        {
            SonyModelProfile[] profiles = new SonyModelProfile[2];
            profiles[0] = new SonyModelProfile(
                PidInzoneH5,
                "Sony:054C:0EBF",
                "Sony INZONE H5 (WH-G500)",
                "INZONE H5",
                "HID\\VID_054C&PID_0EBF",
                "Sony:054C:0EBF:H5",
                DeviceCategory.Headset,
                true,
                "耳机未连接 (2.4G 接收器在线)");
            profiles[1] = new SonyModelProfile(
                PidInzoneBuds,
                "Sony:054C:0EC2",
                "Sony INZONE Buds (WF-G700N)",
                "INZONE Buds",
                "HID\\VID_054C&PID_0EC2",
                "Sony:054C:0EC2:Buds",
                DeviceCategory.Earbuds,
                false,
                "耳塞在充电盒内休眠 (2.4G 接收器在线)");
            return profiles;
        }

        public static bool IsSupportedVidPid(int vid, int pid)
        {
            if (vid != VendorIdSony) return false;
            return pid == PidInzoneBuds || pid == PidInzoneH5;
        }

        public static bool IsSonyInzoneDevice(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return false;
            string lower = identifier.ToLowerInvariant();
            bool isSonyVid = lower.Contains("vid_054c") || lower.Contains("054c:") || lower.Contains("054c&");
            if (!isSonyVid) return false;
            return lower.Contains("pid_0ec2") || lower.Contains(":0ec2")
                || lower.Contains("pid_0ebf") || lower.Contains(":0ebf");
        }

        public static byte CalculateQueryChecksum(byte seqLo, byte seqHi)
        {
            int sum = 0x96 + 0xC3 + 0x41 + 0x04 + 0x01 + seqLo + seqHi;
            return (byte)(sum & 0xFF);
        }

        public static byte[] BuildQueryPacket(ushort sequence)
        {
            byte[] packet = new byte[ReportLength];
            packet[0] = ReportId; // 0x02
            packet[1] = 0x0C;     // Payload length = 12 bytes
            packet[2] = 0x01;
            packet[3] = 0x00;
            packet[4] = 0xFC;
            packet[5] = 0x08;
            packet[6] = 0x96;
            packet[7] = 0xC3;
            packet[8] = AddressRxFromPc;
            packet[9] = EventIdBattery;
            packet[10] = EventTypeGet;
            byte seqLo = (byte)(sequence & 0xFF);
            byte seqHi = (byte)((sequence >> 8) & 0xFF);
            packet[11] = seqLo;
            packet[12] = seqHi;
            packet[13] = CalculateQueryChecksum(seqLo, seqHi);
            return packet;
        }

        public static byte CalculateResponseChecksum(byte[] buffer, int offset, int length)
        {
            if (buffer == null || offset < 0 || length <= 0 || offset + length > buffer.Length)
            {
                return 0;
            }

            int sum = 0;
            for (int i = 0; i < length; i++)
            {
                sum += buffer[offset + i];
            }
            return (byte)(sum & 0xFF);
        }

        // 入耳式（Buds）六字节电量槽：[status_L, pct_L, status_R, pct_R, status_Case, pct_Case]
        public static bool ParseBatteryResponse(byte[] buffer, out SonyInzoneBudsState state)
        {
            state = new SonyInzoneBudsState();
            if (buffer == null || buffer.Length < 20)
            {
                return false;
            }

            // 报文结构校验: Report ID == 0x02, Header[6..7] == 0x96 0xC3, Header[8] == 0x14
            if (buffer[0] != ReportId || buffer[6] != SonyKeyLo || buffer[7] != SonyKeyHi)
            {
                return false;
            }

            // 校验和核验: sum(buffer[6..18]) ≡ buffer[19]
            byte calculatedChecksum = CalculateResponseChecksum(buffer, 6, 13);
            if (buffer[19] != calculatedChecksum)
            {
                return false;
            }

            byte lStat = buffer[13];
            byte lBatt = buffer[14];
            byte rStat = buffer[15];
            byte rBatt = buffer[16];
            byte cStat = buffer[17];
            byte cBatt = buffer[18];

            state.LeftBattery = (lBatt <= 100) ? (int)lBatt : -1;
            state.LeftCharging = (lStat == 0x01);

            state.RightBattery = (rBatt <= 100) ? (int)rBatt : -1;
            state.RightCharging = (rStat == 0x01);

            state.CaseBattery = (cBatt <= 100) ? (int)cBatt : -1;
            state.CaseCharging = (cStat == 0x01);

            state.IsConnected = (state.LeftBattery >= 0 || state.RightBattery >= 0 || state.CaseBattery >= 0);
            return true;
        }

        // 头戴式（H5 等）两字节电量槽：[status(=charger), percent]
        // 帧长固定：hci_len = 14（12 字节头 + 2 字节载荷），校验位在 buffer[15]。
        public static bool ParseHeadsetBatteryResponse(byte[] buffer, out SonyInzoneBudsState state)
        {
            state = new SonyInzoneBudsState();
            if (buffer == null || buffer.Length < 16)
            {
                return false;
            }

            if (buffer[0] != ReportId || buffer[6] != SonyKeyLo || buffer[7] != SonyKeyHi)
            {
                return false;
            }

            // hci_len 必须为 14，且事件 id 必须是 BatteryInfo(0x04)
            int hciLength = buffer[1];
            if (hciLength != 14 || buffer[9] != EventIdBattery)
            {
                return false;
            }

            int checksumPos = 2 + hciLength - 1; // = 15
            if (checksumPos >= buffer.Length)
            {
                return false;
            }

            // 事件校验和: sum(hci[3..cs-1])，换算到报告缓冲区即 buffer[5 .. checksumPos-1]
            byte calculatedChecksum = CalculateResponseChecksum(buffer, 5, checksumPos - 5);
            if (buffer[checksumPos] != calculatedChecksum)
            {
                return false;
            }

            byte status = buffer[13];
            byte percent = buffer[14];

            state.IsHeadset = true;
            state.LeftBattery = (percent <= 100) ? (int)percent : -1;
            state.LeftCharging = (status != 0x00);
            state.RightBattery = -1;
            state.RightCharging = false;
            state.CaseBattery = -1;
            state.CaseCharging = false;
            state.IsConnected = (state.LeftBattery >= 0);
            return true;
        }

        // 按电池槽宽度自适应分派，避免为每个型号维护 PID 表：
        // payload 2 字节 -> 头戴；payload 6 字节 -> 入耳。
        public static bool ParseBatteryResponseAuto(byte[] buffer, out SonyInzoneBudsState state)
        {
            state = new SonyInzoneBudsState();
            if (buffer == null || buffer.Length < 16)
            {
                return false;
            }

            int hciLength = buffer[1];
            if (hciLength < 12 || hciLength > buffer.Length - 2)
            {
                return false;
            }

            int payloadLength = hciLength - 12;
            if (payloadLength == 2)
            {
                return ParseHeadsetBatteryResponse(buffer, out state);
            }
            if (payloadLength == 6)
            {
                return ParseBatteryResponse(buffer, out state);
            }
            return false;
        }

        public static string BuildTooltip(SonyInzoneBudsState state)
        {
            if (state == null)
            {
                return "";
            }

            if (state.IsHeadset)
            {
                if (!state.IsConnected || state.LeftBattery < 0)
                {
                    return "耳机未连接 (2.4G 接收器在线)";
                }
                return string.Format("电量: {0}%{1}", state.LeftBattery, state.LeftCharging ? "⚡" : "");
            }

            if (!state.IsConnected)
            {
                return "耳塞在充电盒内休眠 (2.4G 接收器在线)";
            }

            List<string> parts = new List<string>();
            if (state.LeftBattery >= 0)
            {
                parts.Add(string.Format("左耳: {0}%{1}", state.LeftBattery, state.LeftCharging ? "⚡" : ""));
            }
            if (state.RightBattery >= 0)
            {
                parts.Add(string.Format("右耳: {0}%{1}", state.RightBattery, state.RightCharging ? "⚡" : ""));
            }
            if (state.CaseBattery >= 0)
            {
                parts.Add(string.Format("充电盒: {0}%{1}", state.CaseBattery, state.CaseCharging ? "⚡" : ""));
            }

            if (parts.Count == 0)
            {
                return "耳塞在充电盒内休眠";
            }
            return string.Join(" | ", parts.ToArray());
        }

        public static int ResolvePrimaryBatteryPercent(SonyInzoneBudsState state)
        {
            if (state == null) return -1;
            if (state.LeftBattery >= 0 && state.RightBattery >= 0)
            {
                return Math.Min(state.LeftBattery, state.RightBattery);
            }
            if (state.LeftBattery >= 0) return state.LeftBattery;
            if (state.RightBattery >= 0) return state.RightBattery;
            if (state.CaseBattery >= 0) return state.CaseBattery;
            return -1;
        }

        public static List<MouseBatteryInfo> QueryAllSonyInzoneDevices()
        {
            lock (hidLock)
            {
                DateTime now = DateTime.Now;
                if ((now - lastQueryTime).TotalMilliseconds < QueryThrottleMs && cachedDevices.Count > 0)
                {
                    return new List<MouseBatteryInfo>(cachedDevices);
                }

                List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
                try
                {
                    SonyModelProfile[] profiles = BuildProfiles();
                    for (int i = 0; i < profiles.Length; i++)
                    {
                        MouseBatteryInfo dev = QuerySingleModel(profiles[i]);
                        if (dev != null)
                        {
                            result.Add(dev);
                        }
                    }
                }
                catch
                {
                    // 任何底层异常都不得打断主轮询总线
                }

                cachedDevices = result;
                lastQueryTime = now;
                return new List<MouseBatteryInfo>(result);
            }
        }

        private static MouseBatteryInfo QuerySingleModel(SonyModelProfile profile)
        {
            List<HidDeviceDescriptor> descriptors;
            try
            {
                descriptors = HidTransport.Enumerate(VendorIdSony, profile.ProductId);
            }
            catch
            {
                return null;
            }

            if (descriptors == null || descriptors.Count == 0)
            {
                return null; // 该型号接收器未插入
            }

            // 寻找匹配 TargetUsagePage 0xFF04 与 TargetUsage 0x0001 的通讯集合
            HidDeviceDescriptor targetDescriptor = null;
            for (int i = 0; i < descriptors.Count; i++)
            {
                HidDeviceDescriptor d = descriptors[i];
                if (d.UsagePage == TargetUsagePage && d.Usage == TargetUsage)
                {
                    targetDescriptor = d;
                    break;
                }
            }

            // 宽松回退：匹配 OutputReportLength == 64
            if (targetDescriptor == null)
            {
                for (int i = 0; i < descriptors.Count; i++)
                {
                    if (descriptors[i].OutputReportLength == ReportLength)
                    {
                        targetDescriptor = descriptors[i];
                        break;
                    }
                }
            }

            if (targetDescriptor == null)
            {
                targetDescriptor = descriptors[0];
            }

            IntPtr handle = HidTransport.Open(targetDescriptor.DevicePath);
            if (!HidTransport.IsValidHandle(handle))
            {
                // 接收器句柄无法打开：保留在线占位，绝不伪造电量
                return CreatePlaceholderDevice(profile);
            }

            SonyInzoneBudsState state = null;
            try
            {
                HidTransport.DrainReports(handle, ReportLength, 3);

                ushort seq = sequenceCounter++;
                if (sequenceCounter == 0) sequenceCounter = 1;

                byte[] queryPacket = BuildQueryPacket(seq);
                bool wrote = HidTransport.WriteReport(handle, queryPacket, queryPacket.Length, ReadTimeoutMs);

                if (wrote)
                {
                    byte[] resp;
                    bool readOk = HidTransport.ReadReport(handle, ReportLength, ReadTimeoutMs, out resp);
                    if (readOk && resp != null)
                    {
                        SonyInzoneBudsState parsedState;
                        if (ParseBatteryResponseAuto(resp, out parsedState))
                        {
                            state = parsedState;
                        }
                    }
                }
            }
            finally
            {
                HidTransport.Close(handle);
            }

            return CreateDeviceFromState(profile, state);
        }

        private static MouseBatteryInfo CreatePlaceholderDevice(SonyModelProfile profile)
        {
            MouseBatteryInfo info = new MouseBatteryInfo();
            info.DeviceId = profile.DeviceId;
            info.Brand = "Sony";
            info.Category = profile.Category;
            info.DeviceName = profile.DeviceName;
            info.CustomName = profile.CustomName;
            info.Transport = "2.4G 无线";
            info.IsDonglePresent = true;
            info.IsConnected = false;
            info.IsSleeping = true;
            info.BatteryPercent = -1;
            info.CustomTooltip = profile.SleepTooltip;
            info.HardwareId = profile.HardwareId;
            info.HardwareFingerprint = profile.Fingerprint;
            info.LastUpdated = DateTime.Now;
            return info;
        }

        private static MouseBatteryInfo CreateDeviceFromState(SonyModelProfile profile, SonyInzoneBudsState state)
        {
            MouseBatteryInfo info = CreatePlaceholderDevice(profile);
            if (state == null)
            {
                return info;
            }

            int primary = ResolvePrimaryBatteryPercent(state);
            info.IsConnected = state.IsConnected;
            info.IsSleeping = !state.IsConnected;
            info.BatteryPercent = primary;
            info.ExactBatteryPercent = (primary >= 0) ? (double)primary : 0.0;
            if (state.IsHeadset)
            {
                info.IsCharging = state.LeftCharging;
            }
            else
            {
                info.IsCharging = state.LeftCharging || state.RightCharging || state.CaseCharging;
            }
            info.CustomTooltip = BuildTooltip(state);
            info.LastUpdated = DateTime.Now;
            return info;
        }
    }
}
