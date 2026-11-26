// ============================================================================
// 模块名称: EdifierDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   漫步者 (EDIFIER) 花再 Halo Space 2.4GHz 无线头戴耳机私有 EC 协议适配：
//   Vendor HID 接口 (UsagePage 0xFF14 / Usage 0x0001)、64 字节中断报文、
//   0x2E/0xAA 帧头 + 0xEC 设备码、载荷 XOR 0xA5 掩码、8 位累加和校验（自下标 1 起算），
//   电量 (0xD0) / 设备名称 (0xC9) / 固件版本 (0xC6) / MAC (0xC8) 查询与解析。
// 依赖关系: HidTransport.cs, MouseBatteryInfo.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 修改帧格式、加密掩码、电量解析或设备识别时必须同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止字符串插值、空条件运算符、nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;

namespace RazerBatteryTray
{
    /// <summary>
    /// 漫步者 EC 协议单次交互结果（Halo Space 头戴耳机）。
    /// 电量未知/未回包时 BatteryPercent 保持 -1，严禁伪造 0 或固定样本值。
    /// </summary>
    public class EdifierEcState
    {
        public int BatteryPercent;      // 0..100，-1 表示未知
        public string DeviceName;       // 0xC9 查询，实机为 "花再 Halo Space"
        public string FirmwareVersion;  // 0xC6 查询，实机为 "1.0.2"
        public string MacAddress;       // 0xC8 查询，形如 "B4:E7:B3:31:52:D3"
        public bool IsConnected;

        public EdifierEcState()
        {
            BatteryPercent = -1;
            DeviceName = null;
            FirmwareVersion = null;
            MacAddress = null;
            IsConnected = false;
        }
    }

    /// <summary>
    /// 漫步者 EC (Edifier Control) 私有协议驱动。
    /// 协议要点（均已实机抓包验证，见 docs/EDIFIER_HALO_SPACE_PROTOCOL.md）：
    ///   Tx: [0x2E][0xAA][0xEC][index][lenHi][lenLo][payload ^ 0xA5][sum(buf[1..]) &amp; 0xFF]，补齐 64 字节；
    ///   Rx: [0x2F][0xBB][0xEC][index][lenHi][lenLo][payload ^ 0xA5][sum(buf[1..]) &amp; 0xFF]。
    /// 长度、校验和与填充字节均为明文，仅 payload 参与 XOR 0xA5 掩码。
    /// </summary>
    public static class EdifierDeviceHelper
    {
        private static readonly object hidLock = new object();

        public const int VendorIdEdifier = 0x2D99;   // 11673，漫步者专用 VID
        public const int PidHaloSpace = 0xE049;      // "漫步者 Halo Space D1"

        public const int TargetUsagePage = 0xFF14;   // 65300，厂商私有控制通道
        public const int TargetUsage = 0x0001;
        public const int ReportLength = 64;

        // 帧常量（EC 协议）
        public const byte TxCommon = 0x2E;
        public const byte TxHeader = 0xAA;
        public const byte RxCommon = 0x2F;
        public const byte RxHeader = 0xBB;
        public const byte DeviceTypeEc = 0xEC;
        public const byte DeviceTypeEd = 0xED;
        public const byte PayloadXorMask = 0xA5;

        // 命令索引（CommandIndexType）
        public const byte CmdBatteryQuery = 0xD0;    // 电量查询 -> int(0-100)
        public const byte CmdNameQuery = 0xC9;       // 设备名称查询 -> UTF-8
        public const byte CmdVersionQuery = 0xC6;    // 固件版本查询 -> [major, minor, patch]
        public const byte CmdMacQuery = 0xC8;        // BT MAC 查询 -> 6 字节

        private const int ReadTimeoutMs = 400;
        private const int QueryThrottleMs = 1000;
        private const int ReadAttempts = 3;

        private static DateTime lastQueryTime = DateTime.MinValue;
        private static List<MouseBatteryInfo> cachedDevices = new List<MouseBatteryInfo>();
        private static string cachedDeviceName = null;
        private static string cachedFirmwareVersion = null;
        private static string cachedMacAddress = null;

        public static bool IsSupportedVidPid(int vid, int pid)
        {
            return vid == VendorIdEdifier && pid == PidHaloSpace;
        }

        public static bool IsEdifierEcDevice(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return false;
            string lower = identifier.ToLowerInvariant();
            bool isEdifierVid = lower.Contains("vid_2d99") || lower.Contains("2d99:") || lower.Contains("2d99&");
            if (!isEdifierVid) return false;
            return lower.Contains("pid_e049") || lower.Contains(":e049") || lower.Contains("e049&");
        }

        /// <summary>
        /// 8 位累加和：sum(buffer[offset .. offset+count-1]) &amp; 0xFF。
        /// 漫步者 EC 协议在计算时【不含首字节】(Tx 的 0x2E / Rx 的 0x2F)，
        /// 因此调用方必须从下标 1（0xAA / 0xBB 帧头）起算。
        /// </summary>
        public static byte CalculateChecksum(byte[] buffer, int offset, int count)
        {
            if (buffer == null || offset < 0 || count <= 0 || offset + count > buffer.Length)
            {
                return 0;
            }
            int sum = 0;
            for (int i = 0; i < count; i++)
            {
                sum += buffer[offset + i];
            }
            return (byte)(sum & 0xFF);
        }

        /// <summary>载荷掩码：加解密同为 XOR 0xA5（设密与解密的对称运算）。</summary>
        public static byte[] MaskPayload(byte[] payload)
        {
            if (payload == null) return new byte[0];
            byte[] result = new byte[payload.Length];
            for (int i = 0; i < payload.Length; i++)
            {
                result[i] = (byte)(payload[i] ^ PayloadXorMask);
            }
            return result;
        }

        /// <summary>
        /// 构造 64 字节 Tx 帧。payload 长度上限 57（64 - 6 字节头 - 1 字节校验）。
        /// 以 0xD0 电量查询为例输出: 2E AA EC D0 00 00 66 00 ... 00
        /// </summary>
        public static byte[] BuildEcFrame(byte cmdIndex, byte[] payload)
        {
            byte[] raw = (payload == null) ? new byte[0] : payload;
            if (raw.Length > ReportLength - 7) return null;

            byte[] frame = new byte[ReportLength];
            frame[0] = TxCommon;
            frame[1] = TxHeader;
            frame[2] = DeviceTypeEc;
            frame[3] = cmdIndex;
            frame[4] = (byte)((raw.Length >> 8) & 0xFF);
            frame[5] = (byte)(raw.Length & 0xFF);
            for (int i = 0; i < raw.Length; i++)
            {
                frame[6 + i] = (byte)(raw[i] ^ PayloadXorMask);
            }
            int crcPos = 6 + raw.Length;
            frame[crcPos] = CalculateChecksum(frame, 1, crcPos - 1);
            return frame;
        }

        /// <summary>
        /// 解析 Rx 报文。仅在 index 命中、设备码合法且累加和正确时返回 true，
        /// 并输出【已解掩码】的载荷。设备 64 字节缓冲区尾部会残留历史报文，
        /// 因此这里只截取 [payloadOffset .. payloadOffset+length) 区间。
        /// </summary>
        public static bool TryParseEcReport(byte[] report, byte expectedIndex, out byte[] payload)
        {
            payload = null;
            if (report == null || report.Length < 8) return false;

            // 设备应答以 0x2F/0xBB 开头（= Tx 的 0x2E/0xAA 各 +1/+0x11）；
            // 兼容偏移 0 的裸帧形态（TempoHub 解析器同样双分支处理）。
            int baseOffset;
            if (report[0] == RxCommon || report[0] == 0x55) baseOffset = 1;
            else if (report[0] == TxCommon || report[0] == TxHeader) baseOffset = 0;
            else return false;

            int codeOffset = baseOffset + 1;
            int indexOffset = baseOffset + 2;
            int lenOffset = baseOffset + 3;
            int payloadOffset = baseOffset + 5;

            if (payloadOffset + 1 > report.Length) return false;

            byte code = report[codeOffset];
            if (code != DeviceTypeEc && code != DeviceTypeEd) return false;
            if (report[indexOffset] != expectedIndex) return false;

            int length = (report[lenOffset] << 8) | report[lenOffset + 1];
            if (length <= 0 || payloadOffset + length + 1 > report.Length) return false;

            int crcPos = payloadOffset + length;
            byte expectedCrc = CalculateChecksum(report, baseOffset, crcPos - baseOffset);
            if (report[crcPos] != expectedCrc) return false;

            byte[] masked = new byte[length];
            for (int i = 0; i < length; i++)
            {
                masked[i] = report[payloadOffset + i];
            }
            payload = MaskPayload(masked);
            return true;
        }

        /// <summary>下发一条命令并等待 index 匹配的应答；返回已解掩码载荷。</summary>
        public static bool QueryDevice(IntPtr handle, byte cmdIndex, byte[] requestPayload, out byte[] responsePayload)
        {
            responsePayload = null;
            byte[] frame = BuildEcFrame(cmdIndex, requestPayload);
            if (frame == null) return false;
            if (!HidTransport.WriteReport(handle, frame, frame.Length, ReadTimeoutMs)) return false;

            for (int attempt = 0; attempt < ReadAttempts; attempt++)
            {
                byte[] report;
                if (!HidTransport.ReadReport(handle, ReportLength, ReadTimeoutMs, out report)) return false;
                if (report == null) return false;
                byte[] parsed;
                if (TryParseEcReport(report, cmdIndex, out parsed))
                {
                    responsePayload = parsed;
                    return true;
                }
            }
            return false;
        }

        /// <summary>电量查询 (0xD0)：载荷首字节即百分比（实机 0x86 ^ 0xA5 = 0x23 = 35%）。</summary>
        public static int ParseBatteryPayload(byte[] payload)
        {
            if (payload == null || payload.Length < 1) return -1;
            int percent = payload[0];
            if (percent < 0 || percent > 100) return -1;
            return percent;
        }

        /// <summary>固件版本 (0xC6)：3 字节 [major, minor, patch]，实机 1.0.2。</summary>
        public static string ParseVersionPayload(byte[] payload)
        {
            if (payload == null || payload.Length < 3) return null;
            return string.Format("{0}.{1}.{2}", payload[0], payload[1], payload[2]);
        }

        /// <summary>设备名称 (0xC9)：UTF-8 明文，实机 "花再 Halo Space"。</summary>
        public static string ParseNamePayload(byte[] payload)
        {
            if (payload == null || payload.Length < 1) return null;
            try
            {
                string name = Encoding.UTF8.GetString(payload).Trim('\0', ' ');
                if (string.IsNullOrEmpty(name)) return null;
                return name;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>BT MAC (0xC8)：6 字节，按 B4:E7:B3:31:52:D3 格式化。</summary>
        public static string ParseMacPayload(byte[] payload)
        {
            if (payload == null || payload.Length < 6) return null;
            StringBuilder sb = new StringBuilder(17);
            for (int i = 0; i < 6; i++)
            {
                if (i > 0) sb.Append(":");
                sb.Append(payload[i].ToString("X2"));
            }
            return sb.ToString();
        }

        public static string BuildTooltip(EdifierEcState state)
        {
            if (state == null || state.BatteryPercent < 0)
            {
                return "耳机休眠或未连接 (2.4G 接收器在线)";
            }

            string name = string.IsNullOrEmpty(state.DeviceName) ? "漫步者 Halo Space" : state.DeviceName;
            string text = name + " · 电量 " + state.BatteryPercent + "%";
            if (!string.IsNullOrEmpty(state.FirmwareVersion))
            {
                text = text + " · 固件 " + state.FirmwareVersion;
            }
            return text;
        }

        /// <summary>
        /// 轮询所有已连接的漫步者 EC 设备（当前仅 Halo Space）。
        /// 节流 1 秒；每次轮询只发 0xD0 电量查询，名称/版本/MAC 首次成功后即缓存，
        /// 避免占用 HID 通道（单条命令往返约 120ms）。
        /// </summary>
        public static List<MouseBatteryInfo> QueryAllEdifierDevices()
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
                    List<HidDeviceDescriptor> descriptors = HidTransport.Enumerate(VendorIdEdifier, PidHaloSpace);
                    if (descriptors == null || descriptors.Count == 0)
                    {
                        cachedDevices = result;
                        lastQueryTime = now;
                        return result;
                    }

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

                    // 宽松回退：部分固件的 usage 描述可能不同，按 64 字节中断端点匹配
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
                        result.Add(CreatePlaceholderDevice(targetDescriptor));
                        cachedDevices = result;
                        lastQueryTime = now;
                        return result;
                    }

                    EdifierEcState state = null;
                    try
                    {
                        HidTransport.DrainReports(handle, ReportLength, 3);

                        EdifierEcState ecState = new EdifierEcState();
                        byte[] payload;

                        if (QueryDevice(handle, CmdBatteryQuery, null, out payload))
                        {
                            ecState.BatteryPercent = ParseBatteryPayload(payload);
                        }

                        if (string.IsNullOrEmpty(cachedDeviceName))
                        {
                            byte[] namePayload;
                            if (QueryDevice(handle, CmdNameQuery, null, out namePayload))
                            {
                                cachedDeviceName = ParseNamePayload(namePayload);
                            }
                        }
                        if (string.IsNullOrEmpty(cachedFirmwareVersion))
                        {
                            byte[] versionPayload;
                            if (QueryDevice(handle, CmdVersionQuery, null, out versionPayload))
                            {
                                cachedFirmwareVersion = ParseVersionPayload(versionPayload);
                            }
                        }
                        if (string.IsNullOrEmpty(cachedMacAddress))
                        {
                            byte[] macPayload;
                            if (QueryDevice(handle, CmdMacQuery, null, out macPayload))
                            {
                                cachedMacAddress = ParseMacPayload(macPayload);
                            }
                        }

                        ecState.DeviceName = cachedDeviceName;
                        ecState.FirmwareVersion = cachedFirmwareVersion;
                        ecState.MacAddress = cachedMacAddress;
                        ecState.IsConnected = (ecState.BatteryPercent >= 0);
                        state = ecState;
                    }
                    finally
                    {
                        HidTransport.Close(handle);
                    }

                    result.Add(CreateDeviceFromState(targetDescriptor, state));
                    cachedDevices = result;
                    lastQueryTime = now;
                    return result;
                }
                catch
                {
                    lastQueryTime = now;
                    return result;
                }
            }
        }

        private static MouseBatteryInfo CreatePlaceholderDevice(HidDeviceDescriptor desc)
        {
            MouseBatteryInfo info = new MouseBatteryInfo();
            info.DeviceId = "Edifier:2D99:E049";
            info.Brand = "Edifier";
            info.Category = DeviceCategory.Headset;
            info.DeviceName = string.IsNullOrEmpty(cachedDeviceName) ? "漫步者 花再 Halo Space" : cachedDeviceName;
            info.CustomName = "Halo Space";
            info.Transport = "2.4G 无线";
            info.IsDonglePresent = true;
            info.IsConnected = false;
            info.IsSleeping = true;
            info.BatteryPercent = -1;
            info.CustomTooltip = "耳机休眠或未连接 (2.4G 接收器在线)";
            info.HardwareId = "HID\\VID_2D99&PID_E049";
            info.HardwareFingerprint = "Edifier:2D99:E049:HaloSpace";
            info.LastUpdated = DateTime.Now;
            return info;
        }

        private static MouseBatteryInfo CreateDeviceFromState(HidDeviceDescriptor desc, EdifierEcState state)
        {
            MouseBatteryInfo info = CreatePlaceholderDevice(desc);
            if (state == null || state.BatteryPercent < 0)
            {
                return info;
            }

            info.IsConnected = true;
            info.IsSleeping = false;
            info.BatteryPercent = state.BatteryPercent;
            info.ExactBatteryPercent = state.BatteryPercent;
            // Halo Space 在 2.4G 无线模式下接入充电器会触发设备侧强制关机（厂商既定行为），
            // 加上固件本身不响应 0xF2 状态查询（实机多长度载荷均无回包），
            // 无线模式下本就不存在可读的充电态，故恒为 false，严禁臆造闪电图标。
            info.IsCharging = false;
            info.CustomTooltip = BuildTooltip(state);
            info.LastUpdated = DateTime.Now;
            return info;
        }
    }
}
