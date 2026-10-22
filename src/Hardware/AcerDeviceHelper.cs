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
// 协议实测（OMR331 旗舰版 / dev_id R332，2026-10-22 抓包）:
//   TX: 00 10 00 00 ... (33 字节, 首字节为 Report ID 0x00)
//   RX: 00 10 00 01 0B 52 33 33 32 85 01 19 01 00 4E 01 ... D9
//       [1]=0x10 命令回显, [2]=0x00 成功, [4]=0x0B 负载长度,
//       [5..8]="R332" 设备 ID, [9..10]=0x0185 固件版本,
//       [14]=0x4E 电池百分比(78)。
//       [15] 为不稳定的设备状态位（未充电时实测 0x01/0x00 均出现），
//       严禁据此判定充电状态；充电位尚未定位，当前一律按未充电处理。
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

        public const int VendorUsagePage = 0xFF01;
        public const int VendorUsage = 0x0010;

        private const byte CommandDeviceInfo = 0x10;
        private const int ReportLength = 33;
        private const int CommandOffset = 1;
        private const int BatteryFieldOffset = 13;
        private const int ChargingFieldOffset = 14;
        private const int ReadTimeoutMs = 320;
        private const int QueryThrottleMs = 3000;

        private static DateTime lastQueryTime = DateTime.MinValue;
        private static List<MouseBatteryInfo> cachedDevices = new List<MouseBatteryInfo>();
        private static int cachedBattery = -1;
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

            int battery = report[commandIndex + BatteryFieldOffset];
            if (battery < 0 || battery > 100) return false;
            batteryPercent = battery;
            // 实测 byte[15] 在鼠标未充电时也会在 0x01/0x00 之间漂移
            // （与设备活动状态相关，且会被官方 MouseHub 的通道占用影响），
            // 不能作为充电标志。充电位尚未定位，这里一律按未充电处理。
            isCharging = false;
            return true;
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
                    string devId = key.GetValue("DevId") as string;
                    if (!string.IsNullOrEmpty(devId)) cachedDevId = devId;
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
                    info.BatteryPercent = battery;
                    info.ExactBatteryPercent = battery;
                    info.IsCharging = charging;
                    info.IsSleeping = false;
                    info.LastUpdated = DateTime.Now;
                    SaveHardwareCache(battery, charging, modelName, devId);
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
