// ============================================================================
// 模块名称: MchoseDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   迈从 (MCHOSE) G87 V2 / Z75S 键盘 2.4G 电量与充电状态适配：
//   G87 V2 保留 65 字节 0x55/0x4A/0x87 路径；Z75S 使用 FF02/0002 集合、
//   20 字节 0x05 型号握手与 0x4A 电量查询，严格验包及独立端点缓存。
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
        public const int VendorIdZ75s = 0x258A;
        public const int PidZ75sWireless = 0x01BB;
        private const int Z75sReportLength = 20;
        private const string Z75sModelToken = "0A0000000009";
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
        private const int ReadTimeoutMs = 320;
        private const int QueryThrottleMs = 5000;

        private static DateTime lastQueryTime = DateTime.MinValue;
        private static List<MouseBatteryInfo> cachedDevices = new List<MouseBatteryInfo>();
        private static int cachedBattery = -1;
        private static bool cachedCharging = false;
        private static string cachedDeviceName = "MCHOSE G87 V2";
        private static DateTime cachedLastUpdated = DateTime.MinValue;

        static MchoseDeviceHelper()
        {
            LoadHardwareCache();
        }

        public static bool IsSupportedVidPid(int vid, int pid)
        {
            return (vid == VendorIdMchose && pid == PidG87V2) ||
                (vid == VendorIdZ75s && pid == PidZ75sWireless);
        }

        public static bool IsMchoseKeyboard(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return false;
            string lower = identifier.ToLowerInvariant();
            bool isVid = lower.Contains("vid_3837") || lower.Contains("3837:") || lower.Contains("3837&");
            if (isVid && (lower.Contains("pid_3033") || lower.Contains(":3033"))) return true;
            bool isZ75sVid = lower.Contains("vid_258a") || lower.Contains("258a:") || lower.Contains("258a&");
            return isZ75sVid && (lower.Contains("pid_01bb") || lower.Contains(":01bb"));
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

        public static bool ParseZ75sIdentityResponse(byte[] report, int length)
        {
            return IsZ75sResponse(report, length, 0x05, 6) && report[5] == 0x0A &&
                report[6] == 0 && report[7] == 0 && report[8] == 0 && report[9] == 0 && report[10] == 9;
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

        private static bool IsZ75sEndpoint(HidDeviceDescriptor descriptor)
        {
            return descriptor != null && descriptor.VendorId == VendorIdZ75s &&
                descriptor.ProductId == PidZ75sWireless && descriptor.UsagePage == 0xFF02 &&
                descriptor.Usage == 2 && descriptor.InputReportLength == Z75sReportLength &&
                descriptor.OutputReportLength == Z75sReportLength && !string.IsNullOrEmpty(descriptor.DevicePath);
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
            bool hasPrevious = previous != null && previous.BatteryPercent >= 0 && previous.BatteryPercent <= 100;
            MouseBatteryInfo info = new MouseBatteryInfo();
            info.DeviceId = "MCHOSE:258A:01BB";
            info.HardwareFingerprint = "HID:258A:01BB";
            info.HardwareId = descriptor.InstanceId;
            info.DeviceName = "MCHOSE Z75S";
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
            if (!fresh) info.CustomTooltip = "Z75S 接收器在线，键盘暂未响应；电量为上次真实读数（如有）";
            return info;
        }

        private static MouseBatteryInfo LoadZ75sCache(HidDeviceDescriptor descriptor)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(GetZ75sCachePath(descriptor), false))
                {
                    if (key == null || !Z75sModelToken.Equals(key.GetValue("ModelToken") as string, StringComparison.Ordinal) ||
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
                    key.SetValue("ModelToken", Z75sModelToken, RegistryValueKind.String);
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
            out int battery, out bool charging)
        {
            verified = false;
            differentModel = false;
            battery = -1;
            charging = false;
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
                verified = ParseZ75sIdentityResponse(rx, rx.Length);
                if (!verified)
                {
                    differentModel = true;
                    return false;
                }
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
                List<HidDeviceDescriptor> descriptors = HidTransport.Enumerate(VendorIdZ75s, PidZ75sWireless);
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
                    bool fresh = QueryZ75sDevice(descriptor, out verified, out differentModel, out battery, out charging);
                    if (differentModel)
                    {
                        z75sSamples.Remove(endpoint);
                        try { Registry.CurrentUser.DeleteSubKeyTree(GetZ75sCachePath(descriptor), false); } catch { }
                        continue;
                    }
                    if (!verified && previous == null) continue; // 冷启动无型号响应时不猜测 Z75S。
                    MouseBatteryInfo info = CreateZ75sInfo(descriptor, previous, battery, charging, fresh);
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
            List<MouseBatteryInfo> result = QueryAllG87Devices();
            result.AddRange(QueryAllZ75sDevices());
            return result;
        }

        private static List<MouseBatteryInfo> QueryAllG87Devices()
        {
            lock (hidLock)
            {
                if ((DateTime.Now - lastQueryTime).TotalMilliseconds < QueryThrottleMs)
                {
                    return new List<MouseBatteryInfo>(cachedDevices);
                }
                lastQueryTime = DateTime.Now;

                List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
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
                if (candidates.Count == 0) return result;
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
