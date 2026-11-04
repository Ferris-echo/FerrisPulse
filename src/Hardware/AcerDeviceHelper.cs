// ============================================================================
// 模块名称: AcerDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   Acer（宏碁）OMR 系列 2.4G 无线鼠标适配（XCTECH 兮宸代工接收器
//   VID 249A/PID 5C2F，含 248A 有线变体）。通过 UsagePage 0xFF01 / Usage 0x10
//   的 33 字节厂商通道发送 0x10 设备信息查询帧，解析设备 ID、固件版本、
//   电池百分比与充电标志。
// 依赖关系: HidTransport.cs, MouseBatteryInfo.cs, Microsoft.Win32
//
// 多品牌共用说明（2026-10-27 实测确认）:
//   249A:5C2F / 248A:5C2E、5D2E 是 XCTECH 代工接收器的公共 ID，被 Acer OMR 系列与
//   Valkyrie VK M3 系列共用，协议完全一致（0x10 查询帧，[13] 充电、[14] 电量）。
//   型号靠接收器回传的 dev_id 区分，映射见 GetModelNameFromDevId：
//     R331/R332 -> Acer OMR331；M220 -> Valkyrie VK M3 Lite；E023 -> Valkyrie VK M3。
//   实测样本（VK M3 Lite / M220，电量 75%、未充电）:
//     TX: 00 10 00 00 ...(33B)
//     RX: 00 10 00 01 0B 4D 32 32 30 84 01 11 01 00 4B 00 ...
//         [4]=0x0B 负载长度, [5..8]="M220", [13]=0x00 未充电, [14]=0x4B=75%。
//   注意：本机 VK M3 的厂商集合 UsagePage=0x0001 / Usage=0x0000（不是 0xFF01/0x10），
//   依赖下方 SelectChannel 的「OutputReportLength==33」兜底分支才能选中，
//   该兜底分支不可删除。
//
// 协议实测（OMR331 旗舰版 / dev_id R332，2026-10-22 与 2026-10-25 抓包）:
//   TX: 00 10 00 00 ... (33 字节, 首字节为 Report ID 0x00)
//   RX: 00 10 00 01 0B 52 33 33 32 85 01 19 01 01 64 01 ... F0
//       [1]=0x10 命令回显, [2]=0x00 成功, [4]=0x0B 负载长度,
//       [5..8]="R332" 设备 ID, [9..10]=0x0185 固件版本,
//       [13]=充电标志(0x01 充电中 / 0x00 未充电),
//       [14]=0x64 电池百分比(100), [15]=恒定 0x01 的设备状态位。
// 充电位 A/B 实测（2026-10-25, 各连续 8 帧, 帧内完全稳定）:
//       充电中 100%: 00 10 00 01 0B 52 33 33 32 85 01 19 01 |01| 64 01 ... F0
//       拔线后 58% : 00 10 00 01 0B 52 33 33 32 85 01 19 01 |00| 3A 01 ... C5
//       两次抓包除电量外，唯一稳定差异就是 [13]；[12]/[14]/[15] 均不变。
//   充电时电量字段是「哨兵值」: 插线瞬间 [14] 由 0x3A(58%) 直接变成 0x64(100%)，
//   物理上不可能瞬间充满，说明固件在外部供电时把百分比填成 100 表示“已接电源”。
//   因此充电状态下 100% 不能当作真实电量展示（否则一插线就跳 100%），
//   由 ResolveDisplayedBattery() 退回到最近一次非充电状态下的实测值。
//   历史误判说明: 旧实现把 [15] 当充电位，但 [15] 是恒定 0x01 的设备状态位，
//   真正随充电切换的是它前面一格 [13]，故旧逻辑永远读不到充电状态。
//   设备不支持 Feature Report（FeatureReportByteLength=0），必须走
//   WriteFile / ReadFile 的 Input/Output Report 通道。
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 修改命令字、字段偏移或解析逻辑时必须同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止字符串插值、空条件运算符、nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    public static class AcerDeviceHelper
    {
        private static readonly object hidLock = new object();
        private const string CacheRegistryKey = @"Software\RazerBatteryTray\AcerCache";
        private const string HiddenDevicesRegPath = @"Software\RazerBatteryTray\HiddenDevices";
        private static bool legacyHiddenCleared = false;

        public const int VendorIdWireless = 0x249A;
        public const int VendorIdWired = 0x248A;
        public const int ProductIdDongle = 0x5C2F;
        public const int ProductIdWired1 = 0x5D2E;
        public const int ProductIdWired2 = 0x5E2E;

        public static bool IsAcerMouse(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return false;
            string lower = identifier.ToLowerInvariant();
            if ((lower.Contains("vid_249a") || lower.Contains("249a:")) &&
                (lower.Contains("pid_5c2f") || lower.Contains(":5c2f")))
            {
                return true;
            }
            if ((lower.Contains("vid_248a") || lower.Contains("248a:")) &&
                (lower.Contains("pid_5d2e") || lower.Contains(":5d2e") || lower.Contains("pid_5e2e") || lower.Contains(":5e2e")))
            {
                return true;
            }
            return lower.Contains("omr331") || lower.Contains("omr332") || lower.Contains("omr304") ||
                   lower.Contains("omr305") || lower.Contains("omr350") || lower.Contains("omr402");
        }

        public const int VendorUsagePage = 0xFF01;
        public const int VendorUsage = 0x0010;

        private const byte CommandDeviceInfo = 0x10;
        private const int ReportLength = 33;
        private const int CommandOffset = 1;
        private const int BatteryFieldOffset = 13;
        // 充电标志位相对命令字回显的偏移 = 12（电池百分比前一格）。
        // 2026-10-25 A/B 实测：充电 0x01、未充电 0x00，各 8 帧稳定；
        // 旧值 14 对应恒定为 0x01 的设备状态位，导致充电状态永远读不到。
        private const int ChargingFieldOffset = 12;
        private const int ReadTimeoutMs = 320;
        private const int QueryThrottleMs = 3000;

        private static DateTime lastQueryTime = DateTime.MinValue;
        private static List<MouseBatteryInfo> cachedDevices = new List<MouseBatteryInfo>();
        private static int cachedBattery = -1;
        // 最近一次「非充电」状态下实测到的真实电量：充电时设备回 100 哨兵值，
        // 展示时用它替代，避免一插线就跳 100%（-1 表示尚无有效实测值）。
        private static int cachedLastRealBattery = -1;
        private const int ChargingBatterySentinel = 100;
        private static bool cachedCharging = false;
        private static string cachedDeviceName = "Acer 2.4G 无线鼠标";
        private static string cachedDevId = "";
        private static DateTime cachedLastUpdated = DateTime.MinValue;

        static AcerDeviceHelper()
        {
            LoadHardwareCache();
        }

        public static bool IsSupportedVidPid(int vid, int pid)
        {
            if (vid == VendorIdWireless && pid == ProductIdDongle) return true;
            if (vid == VendorIdWired)
            {
                if (pid == ProductIdWired1 || pid == ProductIdWired2 || pid == ProductIdDongle) return true;
            }
            return false;
        }

        /// <summary>
        /// 解析接收器回传的设备 ID（如 R332）并映射为 Acer 型号名。
        /// 映射表来源：Acer MouseHub config.xml 的 device/mode 节点。
        /// </summary>
        public static string GetModelNameFromDevId(string devId)
        {
            if (string.IsNullOrEmpty(devId)) return "Acer 2.4G 无线鼠标";
            string id = devId.Trim().ToUpperInvariant();
            switch (id)
            {
                case "E546": return "Acer OMW143 无线鼠标";
                case "E512": return "Acer OMR212 无线鼠标";
                case "M120": return "Acer OMW120 无线鼠标";
                case "M129": return "Acer OMR248 无线鼠标";
                case "T220": return "Acer OMW112 无线鼠标";
                case "N139": return "Acer OMR139 无线鼠标";
                case "M117": return "Acer OMW110 电竞版无线鼠标";
                case "E513": return "Acer OMW110 无线鼠标";
                case "N820": return "Acer OMR238 无线鼠标";
                case "M116": return "Acer OMR255 无线鼠标";
                case "M041": return "Acer OMW120 屏显版无线鼠标";
                case "M216": return "Acer OMR216 无线鼠标";
                // XCTECH 接收器 (249A:5C2F) 为多品牌共用，型号由接收器回传的 dev_id 区分。
                // M220 / E023 来自 Valkyrie「VK M3 Series」官方驱动 config.xml：
                //   <mouse name="VK M3 lite" ... dev_id="M220">
                //   <mouse name="VK M3"      ... dev_id="E023">
                // 协议与 Acer OMR 系列完全相同（0x10 查询帧，[13] 充电、[14] 电量）。
                case "M220": return "Valkyrie VK M3 Lite 无线鼠标";
                case "E023": return "Valkyrie VK M3 无线鼠标";
                case "M226": return "Acer OMR216 无线鼠标 (二代)";
                case "M300": return "Acer OMR255 无线鼠标";
                case "M111": return "Acer OMR247 无线鼠标";
                case "R120": return "Acer OMW120-XC10000 无线鼠标";
                case "E547": return "Acer OMR319 无线鼠标";
                case "E5M5": return "Acer OMW110 旗舰版无线鼠标";
                case "E548": return "Acer OMW143 无线鼠标";
                case "E549": return "Acer OMW143 无线鼠标";
                case "R331": return "Acer OMR331 标准版无线鼠标";
                case "R332": return "Acer OMR331 旗舰版无线鼠标";
                case "ZH41": return "Acer OMR332 无线鼠标";
                case "LT36": return "Acer OMR323 无线鼠标";
                case "T524": return "Acer OMR320 标准版无线鼠标";
                case "T504": return "Acer OMR320 电竞版无线鼠标";
                case "M318": return "Acer OMR318 无线鼠标";
                case "MM34": return "Acer OMR272 无线鼠标";
                case "G821": return "Acer OMR329 无线鼠标";
                case "G823": return "Acer OMR330 无线鼠标";
                case "E558": return "Acer OMW143 无线鼠标";
                case "M319": return "Acer OMW120 无线鼠标";
                case "G349": return "Acer OMR333 无线鼠标";
                case "M043": return "Acer OMW120 无线鼠标";
                case "2349": return "Acer OMW325 无线鼠标";
                case "R632": return "Acer OMR319 PRO 无线鼠标";
            }
            return "Acer 无线鼠标 (" + id + ")";
        }

        /// <summary>
        /// 构建 0x10 设备信息查询帧（33 字节，Report ID 0x00 + 32 字节负载）。
        /// </summary>
        public static byte[] BuildInfoQueryFrame()
        {
            byte[] frame = new byte[ReportLength];
            frame[0] = 0x00;
            frame[1] = CommandDeviceInfo;
            return frame;
        }

        /// <summary>
        /// 解析 0x10 设备信息应答帧，提取设备 ID、固件版本、电量与充电标志。
        /// 字段偏移以命令字节为基准，兼容带/不带 Report ID 前导的读取缓冲区。
        /// </summary>
        public static bool ParseInfoResponse(byte[] report, int length, out string devId,
            out int batteryPercent, out bool isCharging, out int firmwareVersion)
        {
            devId = "";
            batteryPercent = -1;
            isCharging = false;
            firmwareVersion = 0;
            if (report == null || length < ReportLength) return false;

            int commandIndex = -1;
            for (int i = 0; i < 3 && i + ChargingFieldOffset < length; i++)
            {
                if (report[i] == CommandDeviceInfo && report[i + 1] == 0x00)
                {
                    commandIndex = i;
                    break;
                }
            }
            if (commandIndex < 0) return false;

            int payloadLength = report[commandIndex + 3];
            if (payloadLength < 11) return false;

            char[] idChars = new char[4];
            for (int i = 0; i < 4; i++)
            {
                char c = (char)report[commandIndex + 4 + i];
                if (c < 0x20 || c > 0x7E) return false;
                idChars[i] = c;
            }
            devId = new string(idChars);

            firmwareVersion = report[commandIndex + 8] | (report[commandIndex + 9] << 8);

            // [10]=传感器 ID（决定 DPI 编码与量程），[11]=回报率能力位。
            // 2026-11-01 实机抓包：VK M3 Lite（dev_id M220）为 0x11 / 0x01。
            if (commandIndex + 11 < length)
            {
                lastSensorId = report[commandIndex + 10];
                lastRateCapability = report[commandIndex + 11];
            }
            lastDevId = devId;

            int battery = report[commandIndex + BatteryFieldOffset];
            if (battery < 0 || battery > 100) return false;
            batteryPercent = battery;
            // 充电标志：插上充电线时设备置 1，拔线后回 0（2026-10-25 A/B 实测，
            // 各 8 帧稳定）。注意它位于电池百分比前一格，而非早期的 [15]。
            isCharging = report[commandIndex + ChargingFieldOffset] != 0;
            return true;
        }

        /// <summary>
        /// 把「充电状态下设备回报的原始电量」换算成可展示电量。
        /// 该接收器在外部供电时会把电量字段填成 100 作为哨兵值（实测：拔线 58%，
        /// 插线瞬间就变 100%，物理上不可能），因此充电且原始值为 100 时，
        /// 退回到最近一次非充电状态下的实测值；没有历史值则原样返回。
        /// </summary>
        public static int ResolveDisplayedBattery(int rawBattery, bool isCharging, int lastRealBattery)
        {
            if (!isCharging) return rawBattery;
            if (rawBattery >= ChargingBatterySentinel && lastRealBattery >= 0 && lastRealBattery < ChargingBatterySentinel)
            {
                return lastRealBattery;
            }
            return rawBattery;
        }

        // ====================================================================
        // XCTECH 通用协议：DPI 档位 / 回报率
        // --------------------------------------------------------------------
        // 帧结构（33 字节 = Report ID 0x00 + 32 字节负载）：
        //   [0]  = 0x00            Report ID
        //   [1]  = 命令字
        //   [2]  = 0x00            请求固定 0；应答里是状态码
        //                          （0x00 成功 / 0x01 已转发但鼠标未应答 / 0x02 鼠标休眠）
        //   [3]  = (分片序号<<4)|分片数   读请求 0x00，单包写 0x01
        //   [4]  = 负载长度        （0x03 DPI 写厂商写死 0x25，实际负载 0x19=25 字节）
        //   [5..31] = 负载
        //   [32] = 校验和 = sum([5..31]) & 0xFF
        //
        // 依据（双向印证，2026-11-01）：
        //   1) AJAZZ AJ179 官方驱动反编译文档 docs/PROTOCOL.md（同一 249A:5C2F 接收器协议族）；
        //   2) 本机 VK M3 Series 官方驱动运行时抓到的真实应答帧：
        //      00 10 00 01 0b "M220" 84 01 11 01 00 27 01 ...  → dev_id/固件/传感器/电量/在线
        //      00 02 00 01 01 08 ...                            → 回报率标志 0x08=125Hz
        //      00 03 00 01 25 50 | dc00 dc00 | 1800 1800 | ...  → DPI 表：档位头 + 6×(X,Y)
        // 早期实现踩坑记录：把 [3] 当长度、负载从 [4] 起写入，导致所有写命令校验和错位被
        // 设备静默丢弃；读命令必须 [3]=0x00，写命令才 [3]=0x01。
        // ====================================================================

        private const byte CommandSetPollingRate = 0x02;
        private const byte CommandSetDpiStages = 0x03;
        private const byte CommandSync = 0x11;
        private const byte CommandGetPollingRate = 0x12;
        private const byte CommandGetDpiStages = 0x13;
        private const int DpiWriteLengthQuirk = 0x25;
        private const int PayloadOffset = 5;
        private const int MaxPayloadLength = 27;
        public const int MaxDpiStages = 6;

        private static int lastSensorId = 0;
        private static int lastRateCapability = 0;
        private static string lastDevId = "";
        private static readonly int[] cachedStageX = new int[MaxDpiStages];
        private static readonly int[] cachedStageY = new int[MaxDpiStages];
        private static int cachedStageHeader = 0;
        private static int cachedActiveStage = 0;
        private static int cachedRateFlag = 0;
        private static bool hasCachedStages = false;
        private static DateTime performanceCacheTime = DateTime.MinValue;
        private static DateTime performanceBackoffUntil = DateTime.MinValue;

        /// <summary>最近一次 0x10 应答里的传感器 ID（决定 DPI 编码方式与量程）。</summary>
        public static int LastSensorId { get { return lastSensorId; } }

        /// <summary>传感器 ID 已识别，说明该设备支持本协议的 DPI / 回报率控制。</summary>
        public static bool SupportsPerformanceControl { get { return lastSensorId != 0; } }

        /// <summary>最近一次 0x10 应答里的回报率能力位（决定可选档位数量）。</summary>
        public static int LastRateCapability { get { return lastRateCapability; } }

        /// <summary>最近一次读到的回报率（Hz），未读到返回 0。</summary>
        public static int CachedPollingRate { get { return RateHzFromFlag(cachedRateFlag); } }

        /// <summary>
        /// 按 XCTECH 帧规范构建 33 字节厂商帧。
        /// </summary>
        public static byte[] BuildVendorFrame(byte command, byte[] payload, bool isWrite, int lengthOverride)
        {
            byte[] frame = new byte[ReportLength];
            frame[0] = 0x00;
            frame[1] = command;
            frame[2] = 0x00;
            frame[3] = (byte)(isWrite ? 0x01 : 0x00);
            int payloadLength = payload == null ? 0 : payload.Length;
            if (payloadLength > MaxPayloadLength) payloadLength = MaxPayloadLength;
            frame[4] = (byte)(lengthOverride > 0 ? lengthOverride : payloadLength);
            if (payload != null)
            {
                Array.Copy(payload, 0, frame, PayloadOffset, payloadLength);
            }
            int sum = 0;
            for (int i = PayloadOffset; i < ReportLength - 1; i++)
            {
                sum += frame[i];
            }
            frame[ReportLength - 1] = (byte)(sum & 0xFF);
            return frame;
        }

        /// <summary>
        /// 定位应答帧里的命令回显位置。
        /// 实测：本接收器对写命令的回显帧状态位可能是 0x00 也可能是 0x01（鼠标休眠时），
        /// 但数据区照常有效，因此只按命令字匹配，不校验状态位。
        /// 注意只能扫前两个字节：0x11 同步帧的应答形如
        /// <c>00 11 02 FF …</c>，第三字节恰好是 0x02，扫到那里会把回报率误读成 0。
        /// </summary>
        private static int FindCommandEcho(byte[] report, byte command)
        {
            if (report == null) return -1;
            for (int i = 0; i < 2 && i < report.Length; i++)
            {
                if (report[i] == command) return i;
            }
            return -1;
        }

        /// <summary>
        /// 发送一条厂商命令。读命令（needReply=true）在发送前会补一次 0x11 同步帧，
        /// 并在约 3 秒内轮询等待「命令回显 + 状态 0x00」的应答帧。
        /// </summary>
        private static bool VendorRequest(IntPtr handle, int inputLength, byte command,
            byte[] payload, bool isWrite, int lengthOverride, bool needReply, out byte[] reply)
        {
            reply = null;
            HidTransport.DrainReports(handle, inputLength, 6);

            byte[] sync = BuildVendorFrame(CommandSync, null, false, 0);
            HidTransport.WriteReport(handle, sync, sync.Length, 400);

            byte[] frame = BuildVendorFrame(command, payload, isWrite, lengthOverride);
            if (!HidTransport.WriteReport(handle, frame, frame.Length, 650)) return false;
            if (!needReply) return true;

            for (int attempt = 0; attempt < 5; attempt++)
            {
                byte[] rx;
                if (!HidTransport.ReadReport(handle, inputLength, 500, out rx)) continue;
                if (rx == null || rx.Length < ReportLength) continue;
                if (FindCommandEcho(rx, command) < 0) continue;
                reply = rx;
                return true;
            }
            return false;
        }

        /// <summary>传感器 ID → DPI 范围与步进（来源：官方驱动 0x431350 反编译表）。</summary>
        public static void GetDpiRange(int sensorId, out int minDpi, out int maxDpi, out int step)
        {
            switch (sensorId)
            {
                case 0x95: minDpi = 50; maxDpi = 26000; step = 50; return;
                case 0x96: minDpi = 50; maxDpi = 32000; step = 50; return;
                case 0x11: minDpi = 50; maxDpi = 18000; step = 50; return;
                case 0x05: minDpi = 50; maxDpi = 12000; step = 50; return;
                case 0x35: minDpi = 100; maxDpi = 16000; step = 100; return;
                case 0x25: minDpi = 100; maxDpi = 10000; step = 100; return;
                case 0x26: minDpi = 200; maxDpi = 10000; step = 100; return;
                case 0x04: minDpi = 100; maxDpi = 8000; step = 100; return;
                case 0x10:
                case 0x12: minDpi = 100; maxDpi = 4800; step = 100; return;
                default: minDpi = 50; maxDpi = 26000; step = 50; return;
            }
        }

        /// <summary>DPI 原值 → 实际 DPI（按传感器 ID 反解）。</summary>
        public static int DecodeDpiRaw(int sensorId, int raw)
        {
            if (raw < 0) return 0;
            switch (sensorId)
            {
                case 0x95: return (raw + 1) * 50;
                case 0x96:
                    if (raw >= 521) return (raw - 494) * 1000;
                    return (raw + 1) * 50;
                case 0x11:
                    if (raw >= 221) return 13000 + (raw - 221) * 1000;
                    if (raw > 200) return 10000 + (raw - 200) * 100;
                    return raw * 50;
                case 0x05:
                    if (raw > 128) return 6400 + (raw - 128) * 100;
                    return raw * 50;
                case 0x25:
                    if (raw > 50) return 5000 + (raw - 50) * 500;
                    return raw * 100;
                case 0x26:
                    if (raw > 50) return 5000 + (raw - 50) * 500;
                    return (raw + 1) * 100;
                case 0x04:
                case 0x10:
                case 0x12:
                case 0x35:
                    return raw * 100;
                default:
                    return (raw + 1) * 50;
            }
        }

        /// <summary>实际 DPI → 设备原值（按传感器 ID 编码，自动夹到量程内）。</summary>
        public static int EncodeDpiRaw(int sensorId, int dpi)
        {
            int minDpi, maxDpi, step;
            GetDpiRange(sensorId, out minDpi, out maxDpi, out step);
            if (dpi < minDpi) dpi = minDpi;
            if (dpi > maxDpi) dpi = maxDpi;

            switch (sensorId)
            {
                case 0x95:
                    return dpi / 50 - 1;
                case 0x96:
                    if (dpi > 26000) return 519 + (dpi - 26000) / 1000;
                    return dpi / 50 - 1;
                case 0x11:
                    if (dpi >= 13000) return 221 + (dpi - 13000) / 1000;
                    if (dpi > 10000) return 200 + (dpi - 10000) / 100;
                    return dpi / 50;
                case 0x05:
                    if (dpi > 6400) return 128 + (dpi - 6400) / 100;
                    return dpi / 50;
                case 0x25:
                    if (dpi > 5000) return 50 + (dpi - 5000) / 500;
                    return dpi / 100;
                case 0x26:
                    if (dpi > 5000) return 50 + (dpi - 5000) / 500;
                    return dpi / 100 - 1;
                case 0x04:
                case 0x10:
                case 0x12:
                case 0x35:
                    return dpi / 100;
                default:
                    return dpi / 50 - 1;
            }
        }

        /// <summary>回报率 Hz → 位标志（0x08=125 / 0x04=250 / 0x02=500 / 0x01=1000 / 0x10=2000）。</summary>
        public static int RateFlagFromHz(int hz)
        {
            switch (hz)
            {
                case 125: return 0x08;
                case 250: return 0x04;
                case 500: return 0x02;
                case 1000: return 0x01;
                case 2000: return 0x10;
                case 4000: return 0x20;
                case 8000: return 0x40;
                default: return 0;
            }
        }

        public static int RateHzFromFlag(int flag)
        {
            switch (flag)
            {
                case 0x08: return 125;
                case 0x04: return 250;
                case 0x02: return 500;
                case 0x01: return 1000;
                case 0x10: return 2000;
                case 0x20: return 4000;
                case 0x40: return 8000;
                default: return 0;
            }
        }

        /// <summary>
        /// 0x10 应答里的「回报率能力」字节 → 可选档位。
        /// 官方驱动映射：0x10→4 档、0x20→6 档、0x40→7 档，其余按 5 档处理。
        /// 例外：VK M3 Lite（M220）的应答该字节为 0x01，与其官方驱动
        /// device/mouse_vk m3 lite.xml 的 report_max="4" 一致，故最高只到 1000Hz。
        /// </summary>
        public static int[] SupportedRates(int capability)
        {
            switch (capability)
            {
                case 0x01:
                    return new int[] { 125, 250, 500, 1000 };
                case 0x10:
                    return new int[] { 125, 250, 500, 1000 };
                case 0x20:
                    return new int[] { 125, 250, 500, 1000, 2000, 4000 };
                case 0x40:
                    return new int[] { 125, 250, 500, 1000, 2000, 4000, 8000 };
                default:
                    return new int[] { 125, 250, 500, 1000, 2000 };
            }
        }

        /// <summary>重新枚举并挑出厂商配置通道（UsagePage 0xFF01 / Usage 0x10）。</summary>
        public static HidDeviceDescriptor FindVendorChannel()
        {
            List<HidDeviceDescriptor> descriptors = new List<HidDeviceDescriptor>();
            descriptors.AddRange(HidTransport.Enumerate(VendorIdWireless, ProductIdDongle));
            if (descriptors.Count == 0)
            {
                descriptors.AddRange(HidTransport.Enumerate(VendorIdWired, ProductIdWired1));
                descriptors.AddRange(HidTransport.Enumerate(VendorIdWired, ProductIdWired2));
            }
            return SelectVendorChannel(descriptors);
        }

        /// <summary>用缓存填充 DPI / 回报率（设备休眠时也能维持 UI 显示）。</summary>
        private static bool FillPerformanceFromCache(out int dpiX, out int dpiY,
            out int activeStage, out int stageCount, out int rateHz)
        {
            activeStage = cachedActiveStage;
            stageCount = MaxDpiStages;
            dpiX = DecodeDpiRaw(lastSensorId, cachedStageX[cachedActiveStage]);
            dpiY = DecodeDpiRaw(lastSensorId, cachedStageY[cachedActiveStage]);
            rateHz = RateHzFromFlag(cachedRateFlag);
            return hasCachedStages && dpiX > 0;
        }

        /// <summary>
        /// 读取 DPI 表与回报率。forceRefresh=false 时优先命中 1.2 秒缓存；
        /// 设备休眠（接收器回 0x01/0x02 状态）时回退到最近一次成功值。
        /// </summary>
        public static bool TryQueryPerformance(bool forceRefresh, out int dpiX, out int dpiY,
            out int activeStage, out int stageCount, out int rateHz)
        {
            // 读取本身是一次幂等回写（设备无纯读命令），因此缓存时间不能太短：
            // 太短会让后台 DPI 监视每次轮询都往设备写一遍，既磨损板载存储，
            // 也可能覆盖用户刚用鼠标 DPI 键切换到的档位。
            if (!forceRefresh && hasCachedStages &&
                (DateTime.Now - performanceCacheTime).TotalMilliseconds < 4000)
            {
                return FillPerformanceFromCache(out dpiX, out dpiY, out activeStage, out stageCount, out rateHz);
            }

            // 设备休眠时读命令必然空转：失败后退避 5 秒，避免 UI 线程反复阻塞。
            if (!forceRefresh && DateTime.Now < performanceBackoffUntil)
            {
                return FillPerformanceFromCache(out dpiX, out dpiY, out activeStage, out stageCount, out rateHz);
            }

            bool ok = QueryPerformanceFromDevice(out dpiX, out dpiY, out activeStage, out stageCount, out rateHz);
            if (ok)
            {
                performanceBackoffUntil = DateTime.MinValue;
                return true;
            }
            performanceBackoffUntil = DateTime.Now.AddSeconds(5);

            if (hasCachedStages)
            {
                return FillPerformanceFromCache(out dpiX, out dpiY, out activeStage, out stageCount, out rateHz);
            }
            dpiX = 0;
            dpiY = 0;
            activeStage = 0;
            stageCount = 0;
            rateHz = 0;
            return false;
        }

        /// <summary>
        /// 确保传感器 ID 已知。DPI 原始值的编解码完全依赖传感器 ID（0x11 与未知传感器的
        /// 公式差一个「+1」），进程刚启动、缓存里又没有传感器 ID 时必须先补一次 0x10 只读查询，
        /// 否则会把设备里的 16 解成 850、220 解成 11050，回写时还会把错误值写进设备。
        /// </summary>
        public static bool EnsureDeviceInfo()
        {
            if (lastSensorId != 0) return true;
            lock (hidLock)
            {
                HidDeviceDescriptor descriptor = FindVendorChannel();
                if (descriptor == null) return false;
                int battery;
                bool charging;
                string devId;
                string modelName;
                if (!QueryDevice(descriptor, out battery, out charging, out devId, out modelName)) return false;
                if (lastSensorId == 0) return false;
                SavePerformanceCache();
                return true;
            }
        }

        /// <summary>
        /// 本接收器没有独立的「读」命令：0x12 / 0x13 读命令的应答恒为 len=0（无数据）。
        /// 唯一的读法是「写命令 + 回显应答」——把当前（或已知）配置写下去，设备在应答帧里
        /// 回吐完整的权威状态，因此写入是幂等的、不会改变设置。
        /// </summary>
        private static bool QueryPerformanceFromDevice(out int dpiX, out int dpiY,
            out int activeStage, out int stageCount, out int rateHz)
        {
            dpiX = 0;
            dpiY = 0;
            activeStage = 0;
            stageCount = 0;
            rateHz = 0;

            lock (hidLock)
            {
                // 没有传感器 ID 就无法把缓存的原始值换算成 DPI，先补一次 0x10（只读）。
                EnsureDeviceInfo();

                HidDeviceDescriptor descriptor = FindVendorChannel();
                if (descriptor == null) return false;

                IntPtr handle = HidTransport.Open(descriptor.DevicePath);
                if (!HidTransport.IsValidHandle(handle)) return false;
                try
                {
                    int inputLength = descriptor.InputReportLength;
                    if (inputLength < ReportLength) inputLength = ReportLength;

                    // ---- DPI：用缓存（没有缓存则用该机型的出厂表）回写一次，再从应答取权威值 ----
                    int[] seedX = new int[MaxDpiStages];
                    int[] seedY = new int[MaxDpiStages];
                    int seedActive = cachedActiveStage;
                    int seedHeader = cachedStageHeader;

                    if (hasCachedStages)
                    {
                        for (int i = 0; i < MaxDpiStages; i++)
                        {
                            seedX[i] = DecodeDpiRaw(lastSensorId, cachedStageX[i]);
                            seedY[i] = DecodeDpiRaw(lastSensorId, cachedStageY[i]);
                        }
                    }
                    else
                    {
                        GetFactoryDefaults(lastDevId, seedX, seedY);
                        // 没有缓存时不知道设备当前档位：默认取最高档，避免把用户鼠标
                        // 意外切到最低档（本机型出厂最高档即 12000 DPI）。
                        seedActive = MaxDpiStages - 1;
                        seedHeader = 0;
                    }

                    byte[] payload = BuildDpiPayload(seedX, seedY, seedActive, seedHeader);
                    byte[] reply;
                    if (!VendorRequest(handle, inputLength, CommandSetDpiStages, payload,
                            true, DpiWriteLengthQuirk, true, out reply))
                    {
                        return false;
                    }
                    int echo = FindCommandEcho(reply, CommandSetDpiStages);
                    if (echo < 0) return false;

                    int header = reply[echo + 4];
                    int[] xs = new int[MaxDpiStages];
                    int[] ys = new int[MaxDpiStages];
                    for (int i = 0; i < MaxDpiStages; i++)
                    {
                        int baseIndex = echo + 5 + i * 4;
                        if (baseIndex + 3 >= reply.Length) return false;
                        xs[i] = reply[baseIndex] | (reply[baseIndex + 1] << 8);
                        ys[i] = reply[baseIndex + 2] | (reply[baseIndex + 3] << 8);
                    }

                    int stage = (header >> 4) & 0x0F;
                    if (stage >= MaxDpiStages) stage = 0;

                    cachedStageHeader = header;
                    cachedActiveStage = stage;
                    for (int i = 0; i < MaxDpiStages; i++)
                    {
                        cachedStageX[i] = xs[i];
                        cachedStageY[i] = ys[i];
                    }
                    hasCachedStages = true;

                    // ---- 回报率：同理回写一次，应答里带权威档位 ----
                    int rateFlag = cachedRateFlag != 0 ? cachedRateFlag : 0x01;
                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        byte[] rateReply;
                        if (VendorRequest(handle, inputLength, CommandSetPollingRate, new byte[] { (byte)rateFlag },
                                true, 0, true, out rateReply))
                        {
                            int rateEcho = FindCommandEcho(rateReply, CommandSetPollingRate);
                            if (rateEcho >= 0 && rateEcho + 4 < rateReply.Length)
                            {
                                cachedRateFlag = rateReply[rateEcho + 4];
                                break;
                            }
                        }
                        System.Threading.Thread.Sleep(150);
                    }

                    performanceCacheTime = DateTime.Now;
                    SavePerformanceCache();

                    activeStage = stage;
                    stageCount = MaxDpiStages;
                    dpiX = DecodeDpiRaw(lastSensorId, xs[stage]);
                    dpiY = DecodeDpiRaw(lastSensorId, ys[stage]);
                    rateHz = RateHzFromFlag(cachedRateFlag);
                    return dpiX > 0;
                }
                catch
                {
                    return false;
                }
                finally
                {
                    HidTransport.Close(handle);
                }
            }
        }

        /// <summary>组装 0x03 的 25 字节负载：[(档位<<4)|计数字节] + 6×(Xlo Xhi Ylo Yhi)。</summary>
        private static byte[] BuildDpiPayload(int[] stagesX, int[] stagesY, int activeStage, int headerSeed)
        {
            byte[] payload = new byte[1 + MaxDpiStages * 4];
            payload[0] = (byte)(((activeStage & 0x0F) << 4) | (headerSeed & 0x0F));
            for (int i = 0; i < MaxDpiStages; i++)
            {
                int x = EncodeDpiRaw(lastSensorId, i < stagesX.Length ? stagesX[i] : 0);
                int y = EncodeDpiRaw(lastSensorId, i < stagesY.Length ? stagesY[i] : 0);
                payload[1 + i * 4 + 0] = (byte)(x & 0xFF);
                payload[1 + i * 4 + 1] = (byte)((x >> 8) & 0xFF);
                payload[1 + i * 4 + 2] = (byte)(y & 0xFF);
                payload[1 + i * 4 + 3] = (byte)((y >> 8) & 0xFF);
            }
            return payload;
        }

        /// <summary>
        /// 出厂 DPI 表。VK M3 Lite（dev_id M220）取自其官方驱动 device/mouse_vk m3 lite.xml：
        /// 800 / 1200 / 2000 / 5000 / 8000 / 12000；其余机型按通用 6 档兜底。
        /// </summary>
        public static void GetFactoryDefaults(string devId, int[] stagesX, int[] stagesY)
        {
            int[] table;
            if (!string.IsNullOrEmpty(devId) && devId.ToUpperInvariant() == "M220")
            {
                table = new int[] { 800, 1200, 2000, 5000, 8000, 12000 };
            }
            else
            {
                table = new int[] { 800, 1200, 2400, 3200, 6400, 12000 };
            }
            for (int i = 0; i < MaxDpiStages; i++)
            {
                int v = i < table.Length ? table[i] : table[table.Length - 1];
                stagesX[i] = v;
                stagesY[i] = v;
            }
        }

        /// <summary>取出缓存的 6 档 DPI 表（单位：实际 DPI），供「只改一档」场景复用。</summary>
        public static bool TryGetCachedStages(out int[] stagesX, out int[] stagesY, out int activeStage)
        {
            stagesX = new int[MaxDpiStages];
            stagesY = new int[MaxDpiStages];
            activeStage = cachedActiveStage;
            if (!hasCachedStages) return false;
            for (int i = 0; i < MaxDpiStages; i++)
            {
                stagesX[i] = DecodeDpiRaw(lastSensorId, cachedStageX[i]);
                stagesY[i] = DecodeDpiRaw(lastSensorId, cachedStageY[i]);
            }
            return true;
        }

        /// <summary>下发完整 DPI 表（6 档，X/Y 独立，同时指定当前档位）。</summary>
        public static bool WriteDpiStages(int[] stagesX, int[] stagesY, int activeStage)
        {
            if (stagesX == null || stagesY == null) return false;
            if (activeStage < 0) activeStage = 0;
            if (activeStage >= MaxDpiStages) activeStage = MaxDpiStages - 1;

            lock (hidLock)
            {
                HidDeviceDescriptor descriptor = FindVendorChannel();
                if (descriptor == null) return false;

                int countNibble = cachedStageHeader & 0x0F;
                byte[] payload = BuildDpiPayload(stagesX, stagesY, activeStage, countNibble);

                IntPtr handle = HidTransport.Open(descriptor.DevicePath);
                if (!HidTransport.IsValidHandle(handle)) return false;
                try
                {
                    int inputLength = descriptor.InputReportLength;
                    if (inputLength < ReportLength) inputLength = ReportLength;
                    byte[] reply;
                    if (!VendorRequest(handle, inputLength, CommandSetDpiStages, payload,
                            true, DpiWriteLengthQuirk, true, out reply))
                    {
                        return false;
                    }

                    // 同步缓存：优先用设备回显的权威值，取不到再用本次写入值。
                    int echo = FindCommandEcho(reply, CommandSetDpiStages);
                    bool parsed = false;
                    if (echo >= 0 && echo + 5 + (MaxDpiStages - 1) * 4 + 3 < reply.Length)
                    {
                        int header = reply[echo + 4];
                        int st = (header >> 4) & 0x0F;
                        if (st < MaxDpiStages) activeStage = st;
                        for (int i = 0; i < MaxDpiStages; i++)
                        {
                            int baseIndex = echo + 5 + i * 4;
                            cachedStageX[i] = reply[baseIndex] | (reply[baseIndex + 1] << 8);
                            cachedStageY[i] = reply[baseIndex + 2] | (reply[baseIndex + 3] << 8);
                        }
                        cachedStageHeader = header;
                        parsed = true;
                    }

                    if (!parsed)
                    {
                        for (int i = 0; i < MaxDpiStages; i++)
                        {
                            cachedStageX[i] = EncodeDpiRaw(lastSensorId, i < stagesX.Length ? stagesX[i] : 0);
                            cachedStageY[i] = EncodeDpiRaw(lastSensorId, i < stagesY.Length ? stagesY[i] : 0);
                        }
                        cachedStageHeader = payload[0];
                    }

                    cachedActiveStage = activeStage;
                    hasCachedStages = true;
                    performanceCacheTime = DateTime.Now;
                    SavePerformanceCache();
                    return true;
                }
                catch
                {
                    return false;
                }
                finally
                {
                    HidTransport.Close(handle);
                }
            }
        }

        /// <summary>切换回报率（写入档位位标志）。</summary>
        public static bool WritePollingRate(int rateHz)
        {
            int flag = RateFlagFromHz(rateHz);
            if (flag == 0) return false;

            lock (hidLock)
            {
                HidDeviceDescriptor descriptor = FindVendorChannel();
                if (descriptor == null) return false;

                IntPtr handle = HidTransport.Open(descriptor.DevicePath);
                if (!HidTransport.IsValidHandle(handle)) return false;
                try
                {
                    int inputLength = descriptor.InputReportLength;
                    if (inputLength < ReportLength) inputLength = ReportLength;
                    byte[] reply;
                    byte[] payload = new byte[] { (byte)flag };
                    if (!VendorRequest(handle, inputLength, CommandSetPollingRate, payload,
                            true, 0, true, out reply))
                    {
                        return false;
                    }
                    int echo = FindCommandEcho(reply, CommandSetPollingRate);
                    if (echo >= 0 && echo + 4 < reply.Length)
                    {
                        // 应答里的负载首字节是设备认可的权威档位；若设备拒绝超量程档位，
                        // 这里回吐的是原值，因此按「回读值 == 请求值」判定成功。
                        int actual = reply[echo + 4];
                        cachedRateFlag = actual != 0 ? actual : flag;
                    }
                    else
                    {
                        cachedRateFlag = flag;
                    }
                    SavePerformanceCache();
                    return cachedRateFlag == flag;
                }
                catch
                {
                    return false;
                }
                finally
                {
                    HidTransport.Close(handle);
                }
            }
        }

        /// <summary>把 DPI 表 / 当前档 / 回报率标志 / 传感器 ID 写入注册表，供下次启动直接复用。</summary>
        private static void SavePerformanceCache()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(CacheRegistryKey))
                {
                    if (key == null) return;
                    // 传感器 ID 与回报率能力字节必须落盘：DPI 原始值的编解码完全依赖传感器 ID，
                    // 否则进程刚启动（尚未收到 0x10 应答）时会把 16 解成 850 而不是 800。
                    if (lastSensorId != 0)
                    {
                        key.SetValue("SensorId", lastSensorId, RegistryValueKind.DWord);
                        key.SetValue("RateCap", lastRateCapability, RegistryValueKind.DWord);
                    }
                    if (!hasCachedStages) return;
                    string table = "";
                    for (int i = 0; i < MaxDpiStages; i++)
                    {
                        if (i > 0) table += ",";
                        table += cachedStageX[i].ToString() + ":" + cachedStageY[i].ToString();
                    }
                    key.SetValue("DpiTable", table, RegistryValueKind.String);
                    key.SetValue("DpiActive", cachedActiveStage, RegistryValueKind.DWord);
                    key.SetValue("RateFlag", cachedRateFlag, RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        private static void LoadHardwareCache()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(CacheRegistryKey, false))
                {
                    if (key == null) return;
                    object batt = key.GetValue("BatteryPercent");
                    if (batt != null) cachedBattery = Convert.ToInt32(batt);
                    object lastReal = key.GetValue("LastRealBattery");
                    if (lastReal != null) cachedLastRealBattery = Convert.ToInt32(lastReal);
                    object chg = key.GetValue("IsCharging");
                    if (chg != null) cachedCharging = Convert.ToInt32(chg) == 1;
                    string name = key.GetValue("DeviceName") as string;
                    if (!string.IsNullOrEmpty(name)) cachedDeviceName = name;
                    string devId = key.GetValue("DevId") as string;
                    if (!string.IsNullOrEmpty(devId)) cachedDevId = devId;
                    string updated = key.GetValue("LastUpdated") as string;
                    if (!string.IsNullOrEmpty(updated))
                    {
                        DateTime dt;
                        if (DateTime.TryParse(updated, out dt)) cachedLastUpdated = dt;
                    }

                    // 性能状态持久化：DPI 原始档位表 + 当前档 + 回报率标志。
                    string table = key.GetValue("DpiTable") as string;
                    if (!string.IsNullOrEmpty(table))
                    {
                        string[] parts = table.Split(',');
                        int n = 0;
                        for (int i = 0; i < parts.Length && n < MaxDpiStages; i++)
                        {
                            string[] xy = parts[i].Split(':');
                            if (xy.Length != 2) continue;
                            int xv, yv;
                            if (!int.TryParse(xy[0], out xv)) continue;
                            if (!int.TryParse(xy[1], out yv)) continue;
                            cachedStageX[n] = xv;
                            cachedStageY[n] = yv;
                            n++;
                        }
                        if (n == MaxDpiStages) hasCachedStages = true;
                    }
                    object act = key.GetValue("DpiActive");
                    if (act != null) cachedActiveStage = Convert.ToInt32(act);
                    object rateFlag = key.GetValue("RateFlag");
                    if (rateFlag != null) cachedRateFlag = Convert.ToInt32(rateFlag);
                    // 传感器 / 回报率能力字节：决定缓存里原始值的解码方式与可选档位数。
                    object sensor = key.GetValue("SensorId");
                    if (sensor != null)
                    {
                        int sv = Convert.ToInt32(sensor);
                        if (sv >= 0 && sv <= 0xFF) lastSensorId = sv;
                    }
                    object rateCap = key.GetValue("RateCap");
                    if (rateCap != null)
                    {
                        int rv = Convert.ToInt32(rateCap);
                        if (rv >= 0 && rv <= 0xFF) lastRateCapability = rv;
                    }
                }
            }
            catch { }
        }

        private static void SaveHardwareCache(int battery, bool charging, string deviceName, string devId)
        {
            cachedBattery = battery;
            cachedCharging = charging;
            cachedDeviceName = deviceName;
            cachedDevId = devId;
            cachedLastUpdated = DateTime.Now;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(CacheRegistryKey))
                {
                    if (key == null) return;
                    key.SetValue("BatteryPercent", battery, RegistryValueKind.DWord);
                    key.SetValue("IsCharging", charging ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("DeviceName", deviceName, RegistryValueKind.String);
                    key.SetValue("DevId", devId, RegistryValueKind.String);
                    key.SetValue("LastUpdated", DateTime.Now.ToString("o"), RegistryValueKind.String);
                }
            }
            catch { }
        }

        private static void SaveLastRealBattery(int battery)
        {
            cachedLastRealBattery = battery;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(CacheRegistryKey))
                {
                    if (key == null) return;
                    key.SetValue("LastRealBattery", battery, RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        private static HidDeviceDescriptor SelectVendorChannel(List<HidDeviceDescriptor> descriptors)
        {
            HidDeviceDescriptor best = null;
            for (int i = 0; i < descriptors.Count; i++)
            {
                HidDeviceDescriptor d = descriptors[i];
                if (d.OutputReportLength < ReportLength || d.InputReportLength < ReportLength) continue;
                if (d.UsagePage == VendorUsagePage && d.Usage == VendorUsage) return d;
                if (best == null && d.OutputReportLength == ReportLength) best = d;
            }
            return best;
        }

        private static bool QueryDevice(HidDeviceDescriptor descriptor, out int batteryPercent,
            out bool isCharging, out string devId, out string modelName)
        {
            batteryPercent = -1;
            isCharging = false;
            devId = "";
            modelName = GetModelNameFromDevId("");

            IntPtr handle = HidTransport.Open(descriptor.DevicePath);
            if (!HidTransport.IsValidHandle(handle)) return false;
            try
            {
                int inputLength = descriptor.InputReportLength;
                if (inputLength < ReportLength) inputLength = ReportLength;

                HidTransport.DrainReports(handle, inputLength, 8);
                byte[] frame = BuildInfoQueryFrame();
                if (!HidTransport.WriteReport(handle, frame, frame.Length, 650)) return false;

                for (int attempt = 0; attempt < 4; attempt++)
                {
                    byte[] rx;
                    if (!HidTransport.ReadReport(handle, inputLength, ReadTimeoutMs, out rx)) continue;
                    string id;
                    int battery;
                    bool charging;
                    int firmware;
                    if (!ParseInfoResponse(rx, inputLength, out id, out battery, out charging, out firmware)) continue;
                    batteryPercent = battery;
                    isCharging = charging;
                    devId = id;
                    modelName = GetModelNameFromDevId(id);
                    return true;
                }
            }
            catch
            {
            }
            finally
            {
                HidTransport.Close(handle);
            }
            return false;
        }

        /// <summary>
        /// 清理旧版“免驱/通用 HID 向导”留下的隐藏记录。
        /// 仅当用户尚未显式隐藏新的 ACER: 设备 ID 时才迁移；用户后续主动隐藏会被尊重。
        /// </summary>
        public static void ClearLegacyHiddenEntries()
        {
            if (legacyHiddenCleared) return;
            legacyHiddenCleared = true;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(HiddenDevicesRegPath, true))
                {
                    if (key == null) return;
                    if (key.GetValue("ACER:249A:5C2F") != null) return;
                    string[] legacyNames = new string[]
                    {
                        "HID:249A:5C2F", "Gaming:249A:5C2F", "Valkyrie:249A:5C2F",
                        "Rapoo:249A:5C2F", "VGN:249A:5C2F", "Logitech:249A:5C2F",
                        "Apple:249A:5C2F", "AppleHID:249A:5C2F", "AppleBT:249A:5C2F",
                        "BT:249A:5C2F", "BLE:249A:5C2F", "HyperX:249A:5C2F"
                    };
                    for (int i = 0; i < legacyNames.Length; i++)
                    {
                        if (key.GetValue(legacyNames[i]) != null)
                        {
                            key.DeleteValue(legacyNames[i], false);
                        }
                    }
                }
            }
            catch { }
        }

        public static List<MouseBatteryInfo> QueryAllAcerDevices()
        {
            lock (hidLock)
            {
                ClearLegacyHiddenEntries();
                if ((DateTime.Now - lastQueryTime).TotalMilliseconds < QueryThrottleMs)
                {
                    return new List<MouseBatteryInfo>(cachedDevices);
                }
                lastQueryTime = DateTime.Now;

                List<HidDeviceDescriptor> descriptors = new List<HidDeviceDescriptor>();
                descriptors.AddRange(HidTransport.Enumerate(VendorIdWireless, ProductIdDongle));
                if (descriptors.Count == 0)
                {
                    descriptors.AddRange(HidTransport.Enumerate(VendorIdWired, ProductIdWired1));
                    descriptors.AddRange(HidTransport.Enumerate(VendorIdWired, ProductIdWired2));
                }

                HidDeviceDescriptor selected = SelectVendorChannel(descriptors);
                if (selected == null) return new List<MouseBatteryInfo>();

                int battery = -1;
                bool charging = false;
                string devId = "";
                string modelName = GetModelNameFromDevId("");
                bool ok = QueryDevice(selected, out battery, out charging, out devId, out modelName);

                bool isWireless = selected.VendorId == VendorIdWireless;
                string deviceId = string.Format("ACER:{0:X4}:{1:X4}", selected.VendorId, selected.ProductId);

                MouseBatteryInfo info = new MouseBatteryInfo();
                info.DeviceId = deviceId;
                info.HardwareFingerprint = string.Format("HID:{0:X4}:{1:X4}", selected.VendorId, selected.ProductId);
                info.HardwareId = selected.InstanceId;
                info.Brand = "Acer";
                info.Category = DeviceCategory.Mouse;
                info.Transport = isWireless ? "2.4G 无线" : "USB 有线";
                info.IsCustom = false;
                info.IsDonglePresent = true;
                info.IsConnected = true;
                info.DeviceName = modelName;

                if (ok)
                {
                    // 充电时设备把电量填成 100 哨兵值，展示层用最近一次真实实测值替代，
                    // 避免“一插线就 100%”；非充电状态下才刷新真实值缓存。
                    int displayBattery = ResolveDisplayedBattery(battery, charging, cachedLastRealBattery);
                    if (!charging)
                    {
                        SaveLastRealBattery(battery);
                    }
                    info.BatteryPercent = displayBattery;
                    info.ExactBatteryPercent = displayBattery;
                    info.IsCharging = charging;
                    info.IsSleeping = false;
                    info.LastUpdated = DateTime.Now;
                    SaveHardwareCache(displayBattery, charging, modelName, devId);
                }
                else
                {
                    // 接收器在线但本轮查询未拿到应答（可能被官方 MouseHub 占用通道）：
                    // 保持在线卡片，优先回显缓存电量，绝不伪造 -1% 为已识别电量。
                    info.BatteryPercent = cachedBattery >= 0 ? cachedBattery : -1;
                    info.ExactBatteryPercent = cachedBattery >= 0 ? cachedBattery : 0;
                    info.IsCharging = cachedBattery >= 0 ? cachedCharging : false;
                    info.IsSleeping = false;
                    info.LastUpdated = cachedBattery >= 0 ? cachedLastUpdated : DateTime.Now;
                    if (cachedBattery < 0)
                    {
                        info.CustomTooltip = "Acer 2.4G 接收器在线；0x10 设备信息查询暂未返回电量";
                    }
                    if (!string.IsNullOrEmpty(cachedDeviceName)) info.DeviceName = cachedDeviceName;
                }

                List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
                result.Add(info);
                cachedDevices = new List<MouseBatteryInfo>(result);
                return result;
            }
        }
    }
}
