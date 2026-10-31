// ============================================================================
// 模块名称: MchoseDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   迈从 (MCHOSE) G87 V2 / G75 PRO / G98V2 / Z75S 三模机械键盘电量与充电状态适配：
//   G87 V2 使用 65 字节 0x55/0x4A/0x87 协议；G75 PRO (3554:FA09)
//   使用 FF02/0002、20 字节 Report ID 0x13 / 命令 0x4A 协议与独立缓存。
//   SINOWEALTH 系 2.4G 接收器（Z75S 258A:01BB、G98V2 41E4:2004）共用同一套
//   20 字节 0x13/0x4A 报文，仅型号标识字节不同（Z75S=0x09、G98V2=0x18）。
//   特别鸣谢社区贡献者：“yaphets”。
// 依赖关系: HidTransport.cs, MouseBatteryInfo.cs, Microsoft.Win32
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 修改命令字、校验和或电量解析时必须同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止字符串插值、空条件运算符、nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    public static class MchoseDeviceHelper
    {
        private static readonly object hidLock = new object();
        private const string CacheRegistryKey = @"Software\RazerBatteryTray\MchoseCache";

        public const int VendorIdMchose = 0x3837;
        public const int PidG87V2 = 0x3033;
        public const int VendorIdCompX = 0x3554;
        public const int PidG75Pro = 0xFA09;
        public const int VendorIdZ75s = 0x258A;
        public const int PidZ75sWireless = 0x01BB;
        // G98V2 的 2.4G 接收器由 SINOWEALTH 方案提供，报文与 Z75S 完全一致，仅型号字节不同。
        public const int VendorIdG98V2 = 0x41E4;
        public const int PidG98V2Wireless = 0x2004;
        private const int Z75sReportLength = 20;
        private const byte Z75sModelByte = 0x09;
        private const byte G98V2ModelByte = 0x18;
        private const string Z75sModelToken = "0A0000000009";
        private const string G98V2ModelToken = "0A0000000018";
        private static readonly object z75sLock = new object();
        private static DateTime lastZ75sQueryTime = DateTime.MinValue;
        private static List<MouseBatteryInfo> cachedZ75sDevices = new List<MouseBatteryInfo>();
        private static readonly Dictionary<string, MouseBatteryInfo> z75sSamples =
            new Dictionary<string, MouseBatteryInfo>(StringComparer.OrdinalIgnoreCase);

        private const byte WirelessReportId = 0x13;
        private const byte WiredReportId = 0x09;
        private const byte WirelessBatteryCommand = 0x4A;
        private const byte WiredBatteryCommand = 0x87;
        private const byte PerformanceCommand = 0x55;
        private const int PerformanceFrameLength = 64;
        private const int PerformanceBatteryOffset = 40;
        private const int PerformanceChargingOffset = 41;
        private const int ReportLength = 65;
        private const int G75ReportLength = 20;
        private const int ReadTimeoutMs = 320;
        private const int QueryThrottleMs = 5000;

        private static DateTime lastQueryTime = DateTime.MinValue;
        private static List<MouseBatteryInfo> cachedDevices = new List<MouseBatteryInfo>();
        private static int cachedBattery = -1;
        private static bool cachedCharging = false;
        private static string cachedDeviceName = "MCHOSE G87 V2";
        private static DateTime cachedLastUpdated = DateTime.MinValue;
        private static int cachedG75Battery = -1;
        private static bool cachedG75Charging = false;
        private static string cachedG75DeviceName = "MCHOSE G75 PRO";
        private static DateTime cachedG75LastUpdated = DateTime.MinValue;

        static MchoseDeviceHelper()
        {
            LoadHardwareCache();
        }

        public static bool IsSupportedVidPid(int vid, int pid)
        {
            return (vid == VendorIdMchose && pid == PidG87V2) ||
                   (vid == VendorIdCompX && pid == PidG75Pro) ||
                   (vid == VendorIdZ75s && pid == PidZ75sWireless) ||
                   (vid == VendorIdG98V2 && pid == PidG98V2Wireless);
        }

        public static bool IsMchoseKeyboard(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return false;
            string lower = identifier.ToLowerInvariant();
            bool isG87 = (lower.Contains("vid_3837") || lower.Contains("3837:") || lower.Contains("3837&")) &&
                         (lower.Contains("pid_3033") || lower.Contains(":3033"));
            bool isG75 = (lower.Contains("vid_3554") || lower.Contains("3554:") || lower.Contains("3554&")) &&
                         (lower.Contains("pid_fa09") || lower.Contains(":fa09"));
            bool isZ75s = (lower.Contains("vid_258a") || lower.Contains("258a:") || lower.Contains("258a&")) &&
                          (lower.Contains("pid_01bb") || lower.Contains(":01bb"));
            bool isG98V2 = (lower.Contains("vid_41e4") || lower.Contains("41e4:") || lower.Contains("41e4&")) &&
                           (lower.Contains("pid_2004") || lower.Contains(":2004"));
            return isG87 || isG75 || isZ75s || isG98V2;
        }

        public static string GetModelName(string productString)
        {
            if (!string.IsNullOrEmpty(productString))
            {
                string name = productString.Trim();
                name = name.Replace("MCHOSE ", "");
                name = name.Replace(" 2.4G", "");
                name = name.Replace(" 2.4G Wireless", "");
                if (name.Length > 0) return "MCHOSE " + name;
            }
            return "MCHOSE G87 V2";
        }

        public static string GetModelName(int pid, string productString)
        {
            if (pid == PidG75Pro) return "MCHOSE G75 PRO";
            return GetModelName(productString);
        }

        /// <summary>
        /// 构建 0x4A 无线电池查询帧。校验和覆盖 Report ID 0x13 在内的前 19 字节。
        /// </summary>
        public static byte[] BuildWirelessBatteryFrame()
        {
            byte[] frame = new byte[ReportLength];
            frame[0] = WirelessReportId;
            frame[1] = WirelessBatteryCommand;
            frame[2] = 0x01;
            int sum = 0;
            for (int i = 0; i <= 18; i++) sum += frame[i];
            frame[19] = (byte)(sum & 0xFF);
            return frame;
        }

        /// <summary>
        /// 构建 G75 PRO 2.4G 电量查询帧。实机端点要求恰好 20 字节，
        /// 校验和为前 19 字节无符号和的低 8 位。
        /// </summary>
        public static byte[] BuildG75BatteryFrame()
        {
            byte[] frame = new byte[G75ReportLength];
            frame[0] = WirelessReportId;
            frame[1] = WirelessBatteryCommand;
            int sum = 0;
            for (int i = 0; i < G75ReportLength - 1; i++) sum += frame[i];
            frame[G75ReportLength - 1] = (byte)(sum & 0xFF);
            return frame;
        }

        public static bool ParseG75BatteryResponse(byte[] report, int length, out int batteryPercent, out bool isCharging)
        {
            batteryPercent = -1;
            isCharging = false;
            if (report == null || length < G75ReportLength || report.Length < G75ReportLength) return false;
            if (report[0] != WirelessReportId || (report[1] & 0x7F) != WirelessBatteryCommand) return false;

            int sum = 0;
            for (int i = 0; i < G75ReportLength - 1; i++) sum += report[i];
            if ((byte)(sum & 0xFF) != report[G75ReportLength - 1]) return false;

            int battery = report[5];
            byte state = report[6];
            if (battery < 0 || battery > 100) return false;
            if (state != 0x01 && state != 0x10) return false;

            batteryPercent = battery;
            isCharging = state == 0x10;
            return true;
        }

        public static byte[] BuildWiredBatteryFrame()
        {
            byte[] frame = new byte[ReportLength];
            frame[0] = WiredReportId;
            frame[1] = WiredBatteryCommand;
            frame[4] = 0x01;
            frame[6] = 0x02;
            return frame;
        }

        /// <summary>
        /// 官方 M HUB SDK 实际使用的性能配置查询帧（未编号报告，前导 Report ID 0 由调用方补）：
        /// 55 05 00 38 38 00... 回复 AA 05 ...，offset 40 为 rf_battery，41 为 isCharging。
        /// </summary>
        public static byte[] BuildPerformanceFrame()
        {
            byte[] frame = new byte[PerformanceFrameLength];
            frame[0] = PerformanceCommand;
            frame[1] = 0x05;
            frame[3] = 0x38;
            frame[4] = 0x38;
            return frame;
        }

        public static bool ParsePerformanceResponse(byte[] report, int length, out int batteryPercent, out bool isCharging)
        {
            batteryPercent = -1;
            isCharging = false;
            if (report == null || length < PerformanceBatteryOffset + 2) return false;

            int offset = -1;
            if (report[0] == 0xAA && report[1] == 0x05) offset = 0;
            else if (report[0] == 0x00 && length >= PerformanceBatteryOffset + 3 && report[1] == 0xAA && report[2] == 0x05) offset = 1;
            if (offset < 0) return false;
            if (length < offset + PerformanceBatteryOffset + 2) return false;

            int battery = report[offset + PerformanceBatteryOffset];
            if (battery < 0 || battery > 100) return false;
            batteryPercent = battery;
            isCharging = report[offset + PerformanceChargingOffset] != 0;
            return true;
        }

        /// <summary>
        /// 解析 MCHOSE 键盘电池回复：
        /// 无线 0x4A: [header 4B][battery 0-100][full/charge bitfield]... [checksum]
        /// 有线 0x87: [header 4B][6B padding][battery][bitfield]... [checksum]
        /// </summary>
        public static bool ParseBatteryResponse(byte[] report, int length, out int batteryPercent, out bool isCharging, out bool isFull)
        {
            batteryPercent = -1;
            isCharging = false;
            isFull = false;
            if (report == null || length < 8) return false;

            int offset = 0;
            if (report[0] == 0x00 || report[0] == WirelessReportId || report[0] == WiredReportId)
            {
                if (length >= 8 && (report[1] == WirelessBatteryCommand || report[1] == WiredBatteryCommand)) offset = 1;
            }

            byte command = report[offset];
            int dataIndex = -1;
            if (command == WirelessBatteryCommand) dataIndex = offset + 4;
            else if (command == WiredBatteryCommand) dataIndex = offset + 10;
            if (dataIndex < 0 || dataIndex + 1 >= length) return false;

            int battery = report[dataIndex];
            int bits = report[dataIndex + 1];
            if (battery < 0 || battery > 100) return false;

            batteryPercent = battery;
            isFull = (bits & 0x0F) != 0;
            isCharging = ((bits >> 4) & 0x0F) != 0;
            return true;
        }

        /// <summary>
        /// Z75S: Windows 缓冲区已包含 Report ID 0x13，不再补前导 0。
        /// 官方 M HUB 的 0x05 型号标识为 0A0000000009；同 VID/PID 的 Z75 必须排除。
        /// </summary>
        public static byte[] BuildZ75sIdentityFrame()
        {
            return BuildZ75sFrame(0x05);
        }

        public static byte[] BuildZ75sBatteryFrame()
        {
            return BuildZ75sFrame(WirelessBatteryCommand);
        }

        private static byte[] BuildZ75sFrame(byte command)
        {
            byte[] frame = new byte[Z75sReportLength];
            frame[0] = WirelessReportId;
            frame[1] = command;
            frame[2] = 1;
            frame[19] = (byte)((frame[0] + frame[1] + frame[2]) & 0xFF);
            return frame;
        }

        private static bool IsZ75sResponse(byte[] report, int length, byte command, byte payloadLength)
        {
            if (report == null || length != Z75sReportLength || report.Length < length) return false;
            if (report[0] != WirelessReportId || report[1] != command || report[2] != 1 ||
                report[3] != 0 || report[4] != payloadLength) return false;
            int sum = 0;
            for (int i = 0; i < 19; i++) sum += report[i];
            return report[19] == (byte)(sum & 0xFF);
        }

        /// <summary>
        /// 校验 SINOWEALTH 系接收器的型号握手应答，并回传型号标识字节。
        /// 已知取值：0x09 = Z75S，0x18 = G98V2。
        /// </summary>
        public static bool ParseSinowealthIdentityResponse(byte[] report, int length, out byte modelByte)
        {
            modelByte = 0;
            if (!IsZ75sResponse(report, length, 0x05, 6)) return false;
            if (report[5] != 0x0A || report[6] != 0 || report[7] != 0 || report[8] != 0 || report[9] != 0) return false;
            byte value = report[10];
            if (value != Z75sModelByte && value != G98V2ModelByte) return false;
            modelByte = value;
            return true;
        }

        public static bool ParseZ75sIdentityResponse(byte[] report, int length)
        {
            byte modelByte;
            return ParseSinowealthIdentityResponse(report, length, out modelByte) && modelByte == Z75sModelByte;
        }

        public static bool ParseZ75sBatteryResponse(byte[] report, int length, out int batteryPercent, out bool isCharging)
        {
            batteryPercent = -1;
            isCharging = false;
            if (!IsZ75sResponse(report, length, WirelessBatteryCommand, 2) || report[5] > 100) return false;
            batteryPercent = report[5];
            // 官方 little-endian bit4(fullStatus) 在低半字节，chargeStatus 在高半字节。
            // 满电位本身不等于“充电中”；实机插线状态是 0x10。
            isCharging = (report[6] & 0xF0) != 0;
            return true;
        }

        /// <summary>
        /// 只接受已实测的 SINOWEALTH 接收器组合：258A:01BB（Z75S）与 41E4:2004（G98V2）。
        /// 必须同时锁定 UsagePage/Usage 与 20 字节的 In/Out 长度，禁止宽松匹配。
        /// </summary>
        private static bool IsZ75sEndpoint(HidDeviceDescriptor descriptor)
        {
            if (descriptor == null || string.IsNullOrEmpty(descriptor.DevicePath)) return false;
            bool knownPair = (descriptor.VendorId == VendorIdZ75s && descriptor.ProductId == PidZ75sWireless) ||
                             (descriptor.VendorId == VendorIdG98V2 && descriptor.ProductId == PidG98V2Wireless);
            return knownPair && descriptor.UsagePage == 0xFF02 &&
                descriptor.Usage == 2 && descriptor.InputReportLength == Z75sReportLength &&
                descriptor.OutputReportLength == Z75sReportLength;
        }

        private static bool IsG98V2Endpoint(HidDeviceDescriptor descriptor)
        {
            return descriptor != null && descriptor.VendorId == VendorIdG98V2 &&
                descriptor.ProductId == PidG98V2Wireless;
        }

        private static string GetZ75sEndpointKey(HidDeviceDescriptor descriptor)
        {
            return string.IsNullOrEmpty(descriptor.InstanceId) ? descriptor.DevicePath : descriptor.InstanceId;
        }

        private static string GetZ75sCachePath(HidDeviceDescriptor descriptor)
        {
            // 可逆编码实例标识，避免替换分隔符造成两个接收器的缓存键碰撞。
            byte[] bytes = Encoding.UTF8.GetBytes(GetZ75sEndpointKey(descriptor).ToUpperInvariant());
            return CacheRegistryKey + @"\Z75S\" + BitConverter.ToString(bytes).Replace("-", "");
        }

        private static MouseBatteryInfo CreateZ75sInfo(HidDeviceDescriptor descriptor, MouseBatteryInfo previous,
            int battery, bool charging, bool fresh)
        {
            byte modelByte = IsG98V2Endpoint(descriptor) ? G98V2ModelByte : Z75sModelByte;
            return BuildSinowealthInfo(descriptor, previous, battery, charging, fresh, modelByte);
        }

        private static MouseBatteryInfo BuildSinowealthInfo(HidDeviceDescriptor descriptor, MouseBatteryInfo previous,
            int battery, bool charging, bool fresh, byte modelByte)
        {
            bool isG98V2 = modelByte == G98V2ModelByte;
            string modelName = isG98V2 ? "MCHOSE G98V2" : "MCHOSE Z75S";
            string modelId = isG98V2 ? "MCHOSE:41E4:2004" : "MCHOSE:258A:01BB";
            bool hasPrevious = previous != null && previous.BatteryPercent >= 0 && previous.BatteryPercent <= 100;
            MouseBatteryInfo info = new MouseBatteryInfo();
            info.DeviceId = modelId;
            info.HardwareFingerprint = isG98V2 ? "HID:41E4:2004" : "HID:258A:01BB";
            info.HardwareId = descriptor.InstanceId;
            info.DeviceName = modelName;
            info.Brand = "MCHOSE";
            info.Category = DeviceCategory.Keyboard;
            info.Transport = "2.4G 无线";
            info.IsCustom = false;
            info.IsConnected = true;
            info.IsDonglePresent = true;
            info.IsSleeping = !fresh;
            info.BatteryPercent = fresh ? battery : (hasPrevious ? previous.BatteryPercent : -1);
            info.ExactBatteryPercent = info.BatteryPercent;
            info.IsCharging = fresh ? charging : (hasPrevious && previous.IsCharging);
            info.LastUpdated = fresh ? DateTime.Now : (hasPrevious ? previous.LastUpdated : DateTime.MinValue);
            if (!fresh) info.CustomTooltip = modelName + " 接收器在线，键盘暂未响应；电量为上次真实读数（如有）";
            return info;
        }

        private static bool IsKnownModelToken(string token)
        {
            return Z75sModelToken.Equals(token, StringComparison.Ordinal) ||
                   G98V2ModelToken.Equals(token, StringComparison.Ordinal);
        }

        private static string GetModelToken(HidDeviceDescriptor descriptor)
        {
            return IsG98V2Endpoint(descriptor) ? G98V2ModelToken : Z75sModelToken;
        }

        private static MouseBatteryInfo LoadZ75sCache(HidDeviceDescriptor descriptor)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(GetZ75sCachePath(descriptor), false))
                {
                    if (key == null || !IsKnownModelToken(key.GetValue("ModelToken") as string) ||
                        !GetZ75sEndpointKey(descriptor).Equals(key.GetValue("Endpoint") as string, StringComparison.OrdinalIgnoreCase)) return null;
                    MouseBatteryInfo info = CreateZ75sInfo(descriptor, null, -1, false, false);
                    int battery = Convert.ToInt32(key.GetValue("BatteryPercent", -1));
                    DateTime updated;
                    if (battery >= 0 && battery <= 100 && DateTime.TryParse(key.GetValue("LastUpdated") as string,
                        CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out updated))
                    {
                        info.BatteryPercent = battery;
                        info.ExactBatteryPercent = battery;
                        info.IsCharging = Convert.ToInt32(key.GetValue("IsCharging", 0)) == 1;
                        info.LastUpdated = updated;
                    }
                    return info;
                }
            }
            catch { return null; }
        }

        private static void SaveZ75sCache(HidDeviceDescriptor descriptor, MouseBatteryInfo info)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(GetZ75sCachePath(descriptor)))
                {
                    if (key == null) return;
                    key.SetValue("ModelToken", GetModelToken(descriptor), RegistryValueKind.String);
                    key.SetValue("Endpoint", GetZ75sEndpointKey(descriptor), RegistryValueKind.String);
                    if (info.BatteryPercent < 0) return; // 只持久化真实电量；型号握手成功也可单独记忆。
                    key.SetValue("BatteryPercent", info.BatteryPercent, RegistryValueKind.DWord);
                    key.SetValue("IsCharging", info.IsCharging ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("LastUpdated", info.LastUpdated.ToString("o", CultureInfo.InvariantCulture), RegistryValueKind.String);
                }
            }
            catch { }
        }

        // 一次事务共用一个截止时间，无论成功/超时都由外层 5 秒节流。
        private static bool ExchangeZ75sReport(IntPtr handle, byte[] tx, Stopwatch timer, out byte[] rx)
        {
            rx = null;
            int remaining = ReadTimeoutMs - (int)timer.ElapsedMilliseconds;
            if (remaining <= 0 || !HidTransport.WriteReport(handle, tx, tx.Length, remaining)) return false;
            for (int attempt = 0; attempt < 3; attempt++)
            {
                remaining = ReadTimeoutMs - (int)timer.ElapsedMilliseconds;
                if (remaining <= 0) break;
                byte[] data;
                if (!HidTransport.ReadReportExact(handle, Z75sReportLength, remaining, out data)) break;
                if (!IsZ75sResponse(data, data.Length, tx[1], tx[1] == 0x05 ? (byte)6 : (byte)2)) continue;
                rx = data;
                return true;
            }
            return false;
        }

        private static bool QueryZ75sDevice(HidDeviceDescriptor descriptor, out bool verified, out bool differentModel,
            out int battery, out bool charging, out byte modelByte)
        {
            verified = false;
            differentModel = false;
            battery = -1;
            charging = false;
            modelByte = 0;
            // 只打开厂商配置集合，按键集合（Usage 1/6、\\kbd）绝不进入中断 I/O。
            if (!IsZ75sEndpoint(descriptor)) return false;
            IntPtr handle = HidTransport.Open(descriptor.DevicePath);
            if (!HidTransport.IsValidHandle(handle)) return false;
            try
            {
                Stopwatch timer = Stopwatch.StartNew();
                HidTransport.DrainReports(handle, Z75sReportLength, 3);
                byte[] rx;
                if (!ExchangeZ75sReport(handle, BuildZ75sIdentityFrame(), timer, out rx)) return false;
                byte detected;
                verified = ParseSinowealthIdentityResponse(rx, rx.Length, out detected);
                if (!verified)
                {
                    differentModel = true;
                    return false;
                }
                modelByte = detected;
                if (!ExchangeZ75sReport(handle, BuildZ75sBatteryFrame(), timer, out rx)) return false;
                return ParseZ75sBatteryResponse(rx, rx.Length, out battery, out charging);
            }
            catch { return false; }
            finally { HidTransport.Close(handle); }
        }

        private static List<MouseBatteryInfo> QueryAllZ75sDevices()
        {
            lock (z75sLock)
            {
                DateTime now = DateTime.UtcNow;
                if ((now - lastZ75sQueryTime).TotalMilliseconds < QueryThrottleMs)
                    return new List<MouseBatteryInfo>(cachedZ75sDevices);
                lastZ75sQueryTime = now;
                List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
                List<HidDeviceDescriptor> descriptors = new List<HidDeviceDescriptor>();
                descriptors.AddRange(HidTransport.Enumerate(VendorIdZ75s, PidZ75sWireless));
                descriptors.AddRange(HidTransport.Enumerate(VendorIdG98V2, PidG98V2Wireless));
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < descriptors.Count; i++)
                {
                    HidDeviceDescriptor descriptor = descriptors[i];
                    if (!IsZ75sEndpoint(descriptor)) continue;
                    string endpoint = GetZ75sEndpointKey(descriptor);
                    if (!seen.Add(endpoint)) continue;
                    MouseBatteryInfo previous;
                    if (!z75sSamples.TryGetValue(endpoint, out previous)) previous = LoadZ75sCache(descriptor);
                    bool verified;
                    bool differentModel;
                    int battery;
                    bool charging;
                    byte modelByte;
                    bool fresh = QueryZ75sDevice(descriptor, out verified, out differentModel, out battery, out charging, out modelByte);
                    if (differentModel)
                    {
                        z75sSamples.Remove(endpoint);
                        try { Registry.CurrentUser.DeleteSubKeyTree(GetZ75sCachePath(descriptor), false); } catch { }
                        continue;
                    }
                    if (!verified && previous == null) continue; // 冷启动无型号响应时不猜测机型。
                    byte effectiveModel = verified ? modelByte
                        : (IsG98V2Endpoint(descriptor) ? G98V2ModelByte : Z75sModelByte);
                    MouseBatteryInfo info = BuildSinowealthInfo(descriptor, previous, battery, charging, fresh, effectiveModel);
                    z75sSamples[endpoint] = info;
                    if (verified) SaveZ75sCache(descriptor, info);
                    result.Add(info);
                }
                // 拔下接收器后立即从下一轮枚举结果移除；持久化记录仅供其再次接入时使用。
                cachedZ75sDevices = new List<MouseBatteryInfo>(result);
                return result;
            }
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
                    object chg = key.GetValue("IsCharging");
                    if (chg != null) cachedCharging = Convert.ToInt32(chg) == 1;
                    string name = key.GetValue("DeviceName") as string;
                    if (!string.IsNullOrEmpty(name)) cachedDeviceName = name;
                    string updated = key.GetValue("LastUpdated") as string;
                    if (!string.IsNullOrEmpty(updated))
                    {
                        DateTime dt;
                        if (DateTime.TryParse(updated, out dt)) cachedLastUpdated = dt;
                    }

                    object g75Batt = key.GetValue("G75BatteryPercent");
                    if (g75Batt != null) cachedG75Battery = Convert.ToInt32(g75Batt);
                    object g75Chg = key.GetValue("G75IsCharging");
                    if (g75Chg != null) cachedG75Charging = Convert.ToInt32(g75Chg) == 1;
                    string g75Name = key.GetValue("G75DeviceName") as string;
                    if (!string.IsNullOrEmpty(g75Name)) cachedG75DeviceName = g75Name;
                    string g75Updated = key.GetValue("G75LastUpdated") as string;
                    if (!string.IsNullOrEmpty(g75Updated))
                    {
                        DateTime g75Date;
                        if (DateTime.TryParse(g75Updated, out g75Date)) cachedG75LastUpdated = g75Date;
                    }
                }
            }
            catch { }
        }

        private static void SaveHardwareCache(int battery, bool charging, string deviceName)
        {
            cachedBattery = battery;
            cachedCharging = charging;
            cachedDeviceName = deviceName;
            cachedLastUpdated = DateTime.Now;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(CacheRegistryKey))
                {
                    if (key == null) return;
                    key.SetValue("BatteryPercent", battery, RegistryValueKind.DWord);
                    key.SetValue("IsCharging", charging ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("DeviceName", deviceName, RegistryValueKind.String);
                    key.SetValue("LastUpdated", DateTime.Now.ToString("o"), RegistryValueKind.String);
                }
            }
            catch { }
        }

        private static void SaveG75HardwareCache(int battery, bool charging, string deviceName)
        {
            cachedG75Battery = battery;
            cachedG75Charging = charging;
            cachedG75DeviceName = deviceName;
            cachedG75LastUpdated = DateTime.Now;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(CacheRegistryKey))
                {
                    if (key == null) return;
                    key.SetValue("G75BatteryPercent", battery, RegistryValueKind.DWord);
                    key.SetValue("G75IsCharging", charging ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("G75DeviceName", deviceName, RegistryValueKind.String);
                    key.SetValue("G75LastUpdated", DateTime.Now.ToString("o"), RegistryValueKind.String);
                }
            }
            catch { }
        }

        private static bool QueryG75Device(HidDeviceDescriptor descriptor, out int batteryPercent, out bool isCharging)
        {
            batteryPercent = -1;
            isCharging = false;
            if (descriptor == null || descriptor.VendorId != VendorIdCompX || descriptor.ProductId != PidG75Pro) return false;
            if (descriptor.UsagePage != 0xFF02 || descriptor.Usage != 0x0002 ||
                descriptor.InputReportLength != G75ReportLength || descriptor.OutputReportLength != G75ReportLength) return false;

            IntPtr handle = HidTransport.Open(descriptor.DevicePath);
            if (!HidTransport.IsValidHandle(handle)) return false;
            try
            {
                HidTransport.DrainReports(handle, G75ReportLength, 8);
                byte[] query = BuildG75BatteryFrame();
                if (!HidTransport.WriteReport(handle, query, query.Length, 650)) return false;

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    byte[] response;
                    if (!HidTransport.ReadReport(handle, G75ReportLength, ReadTimeoutMs, out response)) continue;
                    int battery;
                    bool charging;
                    if (!ParseG75BatteryResponse(response, response.Length, out battery, out charging)) continue;
                    batteryPercent = battery;
                    isCharging = charging;
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

        private static void AppendG75Device(List<MouseBatteryInfo> result)
        {
            List<HidDeviceDescriptor> descriptors = HidTransport.Enumerate(VendorIdCompX, PidG75Pro);
            HidDeviceDescriptor selected = null;
            for (int i = 0; i < descriptors.Count; i++)
            {
                HidDeviceDescriptor descriptor = descriptors[i];
                if (descriptor.UsagePage == 0xFF02 && descriptor.Usage == 0x0002 &&
                    descriptor.InputReportLength == G75ReportLength && descriptor.OutputReportLength == G75ReportLength)
                {
                    selected = descriptor;
                    break;
                }
            }
            if (selected == null) return;

            int battery;
            bool charging;
            bool ok = QueryG75Device(selected, out battery, out charging);
            string name = GetModelName(PidG75Pro, selected.ProductString);
            if (!ok && !string.IsNullOrEmpty(cachedG75DeviceName)) name = cachedG75DeviceName;

            MouseBatteryInfo info = new MouseBatteryInfo();
            info.DeviceId = string.Format("MCHOSE:{0:X4}:{1:X4}", VendorIdCompX, PidG75Pro);
            info.HardwareFingerprint = string.Format("HID:{0:X4}:{1:X4}", VendorIdCompX, PidG75Pro);
            info.HardwareId = selected.InstanceId;
            info.DriverId = "BuiltIn:MchoseKeyboard";
            info.DeviceName = name;
            info.Brand = "MCHOSE";
            info.Category = DeviceCategory.Keyboard;
            info.Transport = "2.4G 无线";
            info.IsCustom = false;
            info.IsDonglePresent = true;
            info.Capabilities.SupportsBattery = true;

            if (ok)
            {
                info.IsConnected = true;
                info.IsSleeping = false;
                info.IsCharging = charging;
                info.BatteryPercent = battery;
                info.ExactBatteryPercent = battery;
                info.LastUpdated = DateTime.Now;
                SaveG75HardwareCache(battery, charging, name);
            }
            else
            {
                info.IsConnected = false;
                info.IsSleeping = true;
                info.IsCharging = cachedG75Battery >= 0 ? cachedG75Charging : false;
                info.BatteryPercent = cachedG75Battery >= 0 ? cachedG75Battery : -1;
                info.ExactBatteryPercent = cachedG75Battery >= 0 ? cachedG75Battery : 0;
                info.LastUpdated = cachedG75Battery >= 0 ? cachedG75LastUpdated : DateTime.Now;
                info.CustomTooltip = "MCHOSE G75 PRO 接收器在线；键盘可能休眠，正在保留上次电量";
            }

            result.Add(info);
        }

        private static bool QueryDevice(HidDeviceDescriptor descriptor, out int batteryPercent, out bool isCharging, out string deviceName)
        {
            batteryPercent = -1;
            isCharging = false;
            deviceName = GetModelName(descriptor.ProductString);

            IntPtr handle = HidTransport.Open(descriptor.DevicePath);
            if (!HidTransport.IsValidHandle(handle)) return false;
            try
            {
                HidTransport.HIDP_CAPS caps = HidTransport.GetCaps(handle);
                int outputLength = caps.OutputReportByteLength;
                int inputLength = caps.InputReportByteLength;
                if (outputLength < ReportLength || inputLength < ReportLength)
                {
                    outputLength = descriptor.OutputReportLength;
                    inputLength = descriptor.InputReportLength;
                }
                if (outputLength < ReportLength || inputLength < ReportLength) return false;

                // 首选官方 SDK 的性能配置查询（G87 V2 固件实际走此通道）。
                HidTransport.DrainReports(handle, inputLength, 8);
                byte[] perf = BuildPerformanceFrame();
                byte[] perfTx = new byte[outputLength];
                // 未编号报告：Windows HID 缓冲区首字节必须是 Report ID 0。
                perfTx[0] = 0x00;
                Array.Copy(perf, 0, perfTx, 1, Math.Min(perf.Length, perfTx.Length - 1));
                if (HidTransport.WriteReport(handle, perfTx, outputLength, 650))
                {
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        byte[] rx;
                        if (!HidTransport.ReadReport(handle, inputLength, ReadTimeoutMs, out rx)) continue;
                        int battery;
                        bool charging;
                        if (!ParsePerformanceResponse(rx, inputLength, out battery, out charging)) continue;
                        batteryPercent = battery;
                        isCharging = charging;
                        return true;
                    }
                }

                // 兼容旧固件的 0x4A/0x87 路径。
                byte[] wireless = BuildWirelessBatteryFrame();
                byte[] wired = BuildWiredBatteryFrame();
                byte[][] frames = new byte[][] { wireless, wired };
                for (int f = 0; f < frames.Length; f++)
                {
                    HidTransport.DrainReports(handle, inputLength, 8);
                    byte[] tx = new byte[outputLength];
                    tx[0] = 0x00;
                    Array.Copy(frames[f], 0, tx, 1, Math.Min(frames[f].Length, tx.Length - 1));
                    if (!HidTransport.WriteReport(handle, tx, outputLength, 650)) continue;

                    for (int attempt = 0; attempt < 2; attempt++)
                    {
                        byte[] rx;
                        if (!HidTransport.ReadReport(handle, inputLength, ReadTimeoutMs, out rx)) continue;
                        int battery;
                        bool charging;
                        bool full;
                        if (!ParseBatteryResponse(rx, inputLength, out battery, out charging, out full)) continue;
                        batteryPercent = battery;
                        isCharging = charging;
                        if (full && batteryPercent < 100) isCharging = true;
                        return true;
                    }
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

        public static List<MouseBatteryInfo> QueryAllMchoseDevices()
        {
            List<MouseBatteryInfo> result = QueryAllG87AndG75Devices();
            result.AddRange(QueryAllZ75sDevices());
            return result;
        }

        private static List<MouseBatteryInfo> QueryAllG87AndG75Devices()
        {
            lock (hidLock)
            {
                if ((DateTime.Now - lastQueryTime).TotalMilliseconds < QueryThrottleMs)
                {
                    return new List<MouseBatteryInfo>(cachedDevices);
                }
                lastQueryTime = DateTime.Now;

                List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
                AppendG75Device(result);
                List<HidDeviceDescriptor> descriptors = HidTransport.Enumerate(VendorIdMchose, PidG87V2);
                List<HidDeviceDescriptor> candidates = new List<HidDeviceDescriptor>();
                HidDeviceDescriptor primary = null;
                for (int i = 0; i < descriptors.Count; i++)
                {
                    HidDeviceDescriptor d = descriptors[i];
                    if (d.InputReportLength < ReportLength || d.OutputReportLength < ReportLength) continue;
                    if (primary == null && d.UsagePage == 1 && d.Usage == 0) primary = d;
                    candidates.Add(d);
                }
                if (candidates.Count == 0)
                {
                    cachedDevices = new List<MouseBatteryInfo>(result);
                    return result;
                }
                if (primary != null)
                {
                    candidates.Remove(primary);
                    candidates.Insert(0, primary);
                }

                int battery = -1;
                bool charging = false;
                string name = GetModelName(candidates[0].ProductString);
                bool ok = false;
                HidDeviceDescriptor selected = candidates[0];
                for (int i = 0; i < candidates.Count; i++)
                {
                    if (QueryDevice(candidates[i], out battery, out charging, out name))
                    {
                        selected = candidates[i];
                        ok = true;
                        break;
                    }
                }

                MouseBatteryInfo info = new MouseBatteryInfo();
                info.DeviceId = string.Format("MCHOSE:{0:X4}:{1:X4}", VendorIdMchose, PidG87V2);
                info.HardwareFingerprint = string.Format("HID:{0:X4}:{1:X4}", VendorIdMchose, PidG87V2);
                info.HardwareId = selected.InstanceId;
                info.DeviceName = name;
                info.Brand = "MCHOSE";
                info.Category = DeviceCategory.Keyboard;
                info.Transport = "2.4G 无线";
                info.IsCustom = false;
                info.IsDonglePresent = true;
                info.LastUpdated = DateTime.Now;

                if (ok)
                {
                    info.IsConnected = true;
                    info.IsSleeping = false;
                    info.IsCharging = charging;
                    info.BatteryPercent = battery;
                    info.ExactBatteryPercent = battery;
                    SaveHardwareCache(battery, charging, name);
                }
                else
                {
                    // 接收器与键盘均在线，但当前固件对 0x4A 查询只回固定状态帧：
                    // 不得把设备误标为“离线”。保留在线卡片；有缓存则显示上次电量，
                    // 无缓存则显示电量未知，等待后续抓包补齐真实命令。
                    info.IsConnected = true;
                    info.IsSleeping = false;
                    info.IsCharging = cachedBattery >= 0 ? cachedCharging : false;
                    info.BatteryPercent = cachedBattery >= 0 ? cachedBattery : -1;
                    info.ExactBatteryPercent = cachedBattery >= 0 ? cachedBattery : 0;
                    info.LastUpdated = cachedBattery >= 0 ? cachedLastUpdated : DateTime.Now;
                    info.CustomTooltip = "MCHOSE 2.4G 接收器在线；当前固件未返回 0x4A 电量报文";
                }

                result.Add(info);
                cachedDevices = new List<MouseBatteryInfo>(result);
                return result;
            }
        }
    }
}
