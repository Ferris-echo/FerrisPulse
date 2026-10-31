// ============================================================================
// 模块名称: MchoseGMouseHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   MCHOSE（迈从）G 系列 2.4G 无线鼠标（G7 接收器 0xA8A5:0x2255）私有 HID 协议驱动。
//   协议取自迈从官方 G 系列网页驱动的 WebHID 实现（/gmouse）：
//     - 端点：UsagePage 0xFF01 / Usage 0x0010，Input/Output 均为 65 字节；
//     - 请求帧：[0]=Report ID 0x00，[1]=0x55，[2]=0x30 电量查询，
//       [3..8]=0xA5 0x0B 0x2E 0x01 0x01 0x01，其余补 0；
//     - 应答帧：[1]=0xAA 应答标识，[2]=0x30 命令回显，
//       电量百分比 = [9]，充电标志 = [10]（1=正在充电）。
//   Windows 缓冲区首字节为 Report ID，因此各偏移比官方 WebHID 的 data 视图大 1。
//   物理事实优先：本机型未实测 DPI / 回报率写指令，故不声明 SupportsDpi / SupportsPollingRate。
// 依赖关系: HidTransport.cs, MouseBatteryInfo.cs, Microsoft.Win32
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 修改命令字、帧偏移或电量解析时必须同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止字符串插值、空条件运算符、nameof、表达式主体）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    public static class MchoseGMouseHelper
    {
        private static readonly object hidLock = new object();
        private const string CacheRegistryKey = @"Software\RazerBatteryTray\MchoseGMouseCache";

        public const int VendorIdMchoseG = 0xA8A5;
        public const int PidG7Wireless = 0x2255;

        private const int FrameLength = 65;
        private const byte RequestPrefix = 0x55;
        private const byte ResponseMarker = 0xAA;
        private const byte CommandBattery = 0x30;
        private const byte CommandVersion = 0x03;
        private const byte CommandOnline = 0xED;

        // 以下偏移均以“含 Report ID 的 Windows 缓冲区”为准。
        private const int BatteryOffset = 9;
        private const int ChargeOffset = 10;

        private const int PidG3V2Wired = 0x40A6;  // 官方脚本中 0x40A6 的 G3 V2 有线枚举值
        private const int ReadTimeoutMs = 400;
        private const int QueryThrottleMs = 5000;

        private static DateTime lastQueryTime = DateTime.MinValue;
        private static List<MouseBatteryInfo> cachedDevices = new List<MouseBatteryInfo>();
        private static DateTime cachedLastUpdated = DateTime.MinValue;
        private static int cachedBattery = -1;
        private static bool cachedCharging = false;
        private static string cachedDeviceName = "MCHOSE G7";

        static MchoseGMouseHelper()
        {
            LoadHardwareCache();
        }

        public static bool IsSupportedVidPid(int vid, int pid)
        {
            return vid == VendorIdMchoseG && pid == PidG7Wireless;
        }

        public static bool IsMchoseGMouse(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return false;
            string lower = identifier.ToLowerInvariant();
            bool vidOk = lower.Contains("vid_a8a5") || lower.Contains("a8a5:") || lower.Contains("a8a5&");
            bool pidOk = lower.Contains("pid_2255") || lower.Contains(":2255") || lower.Contains("2255&");
            return vidOk && pidOk;
        }

        /// <summary>
        /// 构建 65 字节查询帧。Windows HID 缓冲区首字节必须是 Report ID（本机型为 0）。
        /// </summary>
        public static byte[] BuildBatteryFrame()
        {
            byte[] frame = new byte[FrameLength];
            frame[0] = 0x00;
            frame[1] = RequestPrefix;
            frame[2] = CommandBattery;
            frame[3] = 0xA5;
            frame[4] = 0x0B;
            frame[5] = 0x2E;
            frame[6] = 0x01;
            frame[7] = 0x01;
            frame[8] = 0x01;
            return frame;
        }

        public static byte[] BuildVersionFrame()
        {
            byte[] frame = new byte[FrameLength];
            frame[0] = 0x00;
            frame[1] = RequestPrefix;
            frame[2] = CommandVersion;
            return frame;
        }

        public static byte[] BuildOnlineFrame()
        {
            byte[] frame = new byte[FrameLength];
            frame[0] = 0x00;
            frame[1] = RequestPrefix;
            frame[2] = CommandOnline;
            return frame;
        }

        /// <summary>
        /// 解析电量应答。兼容 Windows 缓冲区自带 Report ID 的排布（真实实机路径）；
        /// 若调用方传入的是已剥离 Report ID 的 64 字节视图，偏移自动回退 1 字节。
        /// </summary>
        public static bool ParseBatteryResponse(byte[] report, int length, out int batteryPercent, out bool isCharging)
        {
            batteryPercent = -1;
            isCharging = false;
            if (report == null || length < 12 || report.Length < length) return false;

            int baseIndex = -1;
            if (report[1] == ResponseMarker && report[2] == CommandBattery) baseIndex = 1;
            else if (report[0] == ResponseMarker && report[1] == CommandBattery) baseIndex = 0;
            if (baseIndex < 0) return false;

            int battery = report[baseIndex + 8];
            int charge = report[baseIndex + 9];
            if (battery < 0 || battery > 100) return false;

            batteryPercent = battery;
            // 0x01 表示已接充电线；0x00 表示电池供电。不得把电量 100 直接当作充电中。
            isCharging = charge == 0x01;
            return true;
        }

        public static bool ParseVersionResponse(byte[] report, int length, out string version)
        {
            version = null;
            if (report == null || length < 28 || report.Length < length) return false;
            int baseIndex = -1;
            if (report[1] == ResponseMarker && report[2] == CommandVersion) baseIndex = 1;
            else if (report[0] == ResponseMarker && report[1] == CommandVersion) baseIndex = 0;
            if (baseIndex < 0) return false;

            char[] chars = new char[3];
            for (int i = 0; i < 3; i++)
            {
                int value = report[baseIndex + 23 + i];
                if (value < 0x30 || value > 0x39) return false;
                chars[i] = (char)value;
            }
            version = new string(chars);
            return true;
        }

        private static bool IsGMouseEndpoint(HidDeviceDescriptor descriptor)
        {
            return descriptor != null && descriptor.VendorId == VendorIdMchoseG &&
                descriptor.ProductId == PidG7Wireless && descriptor.UsagePage == 0xFF01 &&
                descriptor.Usage == 0x0010 && descriptor.InputReportLength == FrameLength &&
                descriptor.OutputReportLength == FrameLength && !string.IsNullOrEmpty(descriptor.DevicePath);
        }

        private static string GetEndpointKey(HidDeviceDescriptor descriptor)
        {
            if (string.IsNullOrEmpty(descriptor.InstanceId)) return descriptor.DevicePath;
            return descriptor.InstanceId;
        }

        private static string GetCachePath(HidDeviceDescriptor descriptor)
        {
            // 可逆编码实例标识，避免不同接收器之间的缓存键碰撞。
            byte[] bytes = System.Text.Encoding.UTF8.GetBytes(GetEndpointKey(descriptor).ToUpperInvariant());
            return CacheRegistryKey + @"\G7\" + BitConverter.ToString(bytes).Replace("-", "");
        }

        private static bool Exchange(IntPtr handle, byte[] tx, Stopwatch timer, byte command,
            out byte[] response)
        {
            response = null;
            int remaining = ReadTimeoutMs - (int)timer.ElapsedMilliseconds;
            if (remaining <= 0) return false;
            if (!HidTransport.WriteReport(handle, tx, tx.Length, remaining)) return false;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                remaining = ReadTimeoutMs - (int)timer.ElapsedMilliseconds;
                if (remaining <= 0) break;
                byte[] data;
                if (!HidTransport.ReadReportExact(handle, FrameLength, remaining, out data)) continue;
                if (data == null || data.Length != FrameLength) continue;
                if (data[1] != ResponseMarker || data[2] != command) continue;
                response = data;
                return true;
            }
            return false;
        }

        private static bool QueryDevice(HidDeviceDescriptor descriptor, out int battery, out bool charging)
        {
            battery = -1;
            charging = false;
            if (!IsGMouseEndpoint(descriptor)) return false;

            IntPtr handle = HidTransport.Open(descriptor.DevicePath);
            if (!HidTransport.IsValidHandle(handle)) return false;
            try
            {
                Stopwatch timer = Stopwatch.StartNew();
                byte[] response;
                if (!Exchange(handle, BuildBatteryFrame(), timer, CommandBattery, out response)) return false;
                return ParseBatteryResponse(response, response.Length, out battery, out charging);
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

        private static bool QueryVersion(HidDeviceDescriptor descriptor, out string version)
        {
            version = null;
            if (!IsGMouseEndpoint(descriptor)) return false;

            IntPtr handle = HidTransport.Open(descriptor.DevicePath);
            if (!HidTransport.IsValidHandle(handle)) return false;
            try
            {
                Stopwatch timer = Stopwatch.StartNew();
                byte[] response;
                if (!Exchange(handle, BuildVersionFrame(), timer, CommandVersion, out response)) return false;
                return ParseVersionResponse(response, response.Length, out version);
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

        /// <summary>
        /// 官方脚本按 VID 判定有线/2.4G：0x40A6（G3 V2 有线）为有线，其余（含 0xA8A5 接收器）为 2.4G。
        /// </summary>
        public static string GetTransport(int vid)
        {
            return vid == PidG3V2Wired ? "USB 有线" : "2.4G 无线";
        }

        private static string GetModelName(string productString)
        {
            if (!string.IsNullOrEmpty(productString))
            {
                string name = productString.Trim();
                name = name.Replace("MCHOSE ", "");
                name = name.Replace(" 2.4G Wireless", "");
                name = name.Replace(" 2.4G", "");
                if (name.Length > 0) return "MCHOSE " + name;
            }
            return "MCHOSE G7";
        }

        private static MouseBatteryInfo CreateInfo(HidDeviceDescriptor descriptor, MouseBatteryInfo previous,
            int battery, bool charging, bool fresh)
        {
            bool hasPrevious = previous != null && previous.BatteryPercent >= 0 && previous.BatteryPercent <= 100;
            MouseBatteryInfo info = new MouseBatteryInfo();
            info.DeviceId = string.Format("MCHOSE:{0:X4}:{1:X4}", VendorIdMchoseG, PidG7Wireless);
            info.HardwareFingerprint = string.Format("HID:{0:X4}:{1:X4}", VendorIdMchoseG, PidG7Wireless);
            info.HardwareId = descriptor.InstanceId;
            info.DriverId = "BuiltIn:MchoseGMouse";
            info.DeviceName = GetModelName(descriptor.ProductString);
            info.Brand = "MCHOSE";
            info.Category = DeviceCategory.Mouse;
            info.Transport = GetTransport(descriptor.VendorId);
            info.IsCustom = false;
            info.IsConnected = true;
            info.IsDonglePresent = true;
            info.Capabilities.SupportsBattery = true;
            info.IsSleeping = !fresh;
            info.BatteryPercent = fresh ? battery : (hasPrevious ? previous.BatteryPercent : -1);
            info.ExactBatteryPercent = info.BatteryPercent;
            info.IsCharging = fresh ? charging : (hasPrevious && previous.IsCharging);
            info.LastUpdated = fresh ? DateTime.Now : (hasPrevious ? previous.LastUpdated : DateTime.MinValue);
            if (!fresh) info.CustomTooltip = "G7 接收器在线，鼠标暂未响应；电量为上次真实读数（如有）";
            return info;
        }

        private static void LoadHardwareCache()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(CacheRegistryKey, false))
                {
                    if (key == null) return;
                    object batt = key.GetValue("BatteryPercent");
                    if (batt != null)
                    {
                        int value = Convert.ToInt32(batt);
                        if (value >= 0 && value <= 100) cachedBattery = value;
                    }
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

        private static MouseBatteryInfo LoadCache(HidDeviceDescriptor descriptor)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(GetCachePath(descriptor), false))
                {
                    if (key == null ||
                        !GetEndpointKey(descriptor).Equals(key.GetValue("Endpoint") as string, StringComparison.OrdinalIgnoreCase))
                        return null;
                    MouseBatteryInfo info = CreateInfo(descriptor, null, -1, false, false);
                    int battery = Convert.ToInt32(key.GetValue("BatteryPercent", -1));
                    DateTime updated;
                    if (battery >= 0 && battery <= 100 && DateTime.TryParse(key.GetValue("LastUpdated") as string,
                        System.Globalization.CultureInfo.InvariantCulture, System.Globalization.DateTimeStyles.RoundtripKind, out updated))
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

        private static void SaveCache(HidDeviceDescriptor descriptor, MouseBatteryInfo info)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(GetCachePath(descriptor)))
                {
                    if (key == null) return;
                    key.SetValue("Endpoint", GetEndpointKey(descriptor), RegistryValueKind.String);
                    if (info.BatteryPercent < 0) return;
                    key.SetValue("BatteryPercent", info.BatteryPercent, RegistryValueKind.DWord);
                    key.SetValue("IsCharging", info.IsCharging ? 1 : 0, RegistryValueKind.DWord);
                    key.SetValue("LastUpdated", info.LastUpdated.ToString("o", System.Globalization.CultureInfo.InvariantCulture), RegistryValueKind.String);
                }
            }
            catch { }
        }

        public static List<MouseBatteryInfo> QueryAllMchoseGMice()
        {
            lock (hidLock)
            {
                if ((DateTime.Now - lastQueryTime).TotalMilliseconds < QueryThrottleMs)
                    return new List<MouseBatteryInfo>(cachedDevices);
                lastQueryTime = DateTime.Now;

                List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
                List<HidDeviceDescriptor> descriptors = HidTransport.Enumerate(VendorIdMchoseG, PidG7Wireless);
                HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < descriptors.Count; i++)
                {
                    HidDeviceDescriptor descriptor = descriptors[i];
                    if (!IsGMouseEndpoint(descriptor)) continue;
                    string endpoint = GetEndpointKey(descriptor);
                    if (!seen.Add(endpoint)) continue;

                    MouseBatteryInfo previous = null;
                    for (int c = 0; c < cachedDevices.Count; c++)
                    {
                        if (string.Equals(cachedDevices[c].HardwareId, descriptor.InstanceId, StringComparison.OrdinalIgnoreCase))
                        {
                            previous = cachedDevices[c];
                            break;
                        }
                    }
                    if (previous == null) previous = LoadCache(descriptor);

                    int battery;
                    bool charging;
                    bool fresh = QueryDevice(descriptor, out battery, out charging);
                    MouseBatteryInfo info = CreateInfo(descriptor, previous, battery, charging, fresh);
                    if (fresh) SaveHardwareCache(battery, charging, info.DeviceName);
                    if (fresh) SaveCache(descriptor, info);
                    result.Add(info);
                }

                cachedDevices = new List<MouseBatteryInfo>(result);
                return result;
            }
        }
    }
}
