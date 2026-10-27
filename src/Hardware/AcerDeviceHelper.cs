// ============================================================================
// 模块名称: AcerDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   宏碁 (Acer) OMR 系列无线/有线游戏鼠标私有 HID 协议通信实现
//   协议特征：UsagePage 0xFF01 / Usage 0x0010, 33 字节命令/响应帧 (Cmd 0x10)
//   支持 OMR331 旗舰版 (R332) 等 30+ 款 Acer MouseHub 全系无线鼠标电量读取与离线缓存。
//   特别鸣谢社区贡献者：“Zero”。
// 依赖关系: HidTransport.cs, MouseBatteryInfo.cs, Microsoft.Win32
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、协议解析或签名时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止字符串插值、空条件运算符、nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    public static class AcerDeviceHelper
    {
        private static readonly object hidLock = new object();
        private const string CacheRegistryKey = @"Software\RazerBatteryTray\AcerCache";

        public const int VendorIdDongle = 0x249A;
        public const int VendorIdWired = 0x248A;
        public const int PidOmrDongle = 0x5C2F;
        public const int PidOmrWired1 = 0x5D2E;
        public const int PidOmrWired2 = 0x5E2E;

        private const int ReportLength = 33;
        private const byte CommandDeviceInfo = 0x10;
        private const int ReadTimeoutMs = 300;
        private const int QueryThrottleMs = 4000;

        private static DateTime lastQueryTime = DateTime.MinValue;
        private static List<MouseBatteryInfo> cachedDevices = new List<MouseBatteryInfo>();
        private static int cachedBattery = -1;
        private static bool cachedCharging = false;
        private static string cachedDeviceName = "Acer OMR331 旗舰版无线鼠标";
        private static DateTime cachedLastUpdated = DateTime.MinValue;

        static AcerDeviceHelper()
        {
            LoadHardwareCache();
        }

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

        /// <summary>
        /// 依据 Acer MouseHub 官方 config.xml 中的 dev_id 映射具体鼠标型号。
        /// 注意：OMR331 旗舰版在硬件固件中的 dev_id 为 "R332"，标准版为 "R331"。
        /// </summary>
        public static string GetModelNameByDevId(string devId, string fallbackProductString)
        {
            if (!string.IsNullOrEmpty(devId))
            {
                string key = devId.Trim().ToUpperInvariant();
                switch (key)
                {
                    case "R332": return "Acer OMR331 旗舰版无线鼠标";
                    case "R331": return "Acer OMR331 标准版无线鼠标";
                    case "R330": return "Acer OMR330 无线鼠标";
                    case "R350": return "Acer OMR350 无线鼠标";
                    case "R352": return "Acer OMR352 无线鼠标";
                    case "R305": return "Acer OMR305 无线鼠标";
                    case "R304": return "Acer OMR304 无线鼠标";
                    case "R303": return "Acer OMR303 无线鼠标";
                    case "R402": return "Acer OMR402 无线鼠标";
                    case "R403": return "Acer OMR403 无线鼠标";
                    case "R251": return "Acer OMR251 无线鼠标";
                    case "R243": return "Acer OMR243 无线鼠标";
                    case "R242": return "Acer OMR242 无线鼠标";
                    case "R222": return "Acer OMR222 无线鼠标";
                    case "R214": return "Acer OMR214 无线鼠标";
                    case "R060": return "Acer OMR060 无线鼠标";
                    case "R050": return "Acer OMR050 无线鼠标";
                    case "R920": return "Acer OMR920 无线鼠标";
                    case "R930": return "Acer OMR930 无线鼠标";
                    case "W510": return "Acer OMW510 游戏鼠标";
                    case "W520": return "Acer OMW520 游戏鼠标";
                }
            }

            if (!string.IsNullOrEmpty(fallbackProductString))
            {
                string trimmed = fallbackProductString.Trim();
                if (!trimmed.Equals("Wireless Gaming Mouse", StringComparison.OrdinalIgnoreCase) &&
                    !trimmed.Equals("Gaming Mouse", StringComparison.OrdinalIgnoreCase))
                {
                    return trimmed;
                }
            }

            return !string.IsNullOrEmpty(cachedDeviceName) ? cachedDeviceName : "Acer OMR331 旗舰版无线鼠标";
        }

        /// <summary>
        /// 构建 Acer OMR 系列 33 字节设备信息与电量查询帧：
        /// [0]=0x00 (Report ID), [1]=0x10 (CommandDeviceInfo), 其余全 0。
        /// </summary>
        public static byte[] BuildDeviceInfoQuery()
        {
            byte[] frame = new byte[ReportLength];
            frame[0] = 0x00;
            frame[1] = CommandDeviceInfo;
            return frame;
        }

        /// <summary>
        /// 解析 Acer OMR 系列 0x10 命令响应帧：
        /// [0]=0x00, [1]=0x10, [2]=0x00, [3]=0x01, [4]=0x0B(payload len),
        /// [5..8]=ASCII dev_id (如 "R332"), [9..10]=FW version,
        /// [14]=battery (0..100), [15]=接收器无线链路微状态（非充电状态下也会在 0x01/0x00 间跳变，故不作为充电标志）。
        /// </summary>
        public static bool ParseDeviceInfoResponse(byte[] report, int length, out int batteryPercent, out bool isCharging, out string devId)
        {
            batteryPercent = -1;
            isCharging = false;
            devId = null;

            if (report == null || length < 16) return false;

            int offset = 0;
            if (report[0] == 0x00 && report[1] == CommandDeviceInfo)
            {
                offset = 1;
            }
            else if (report[0] == CommandDeviceInfo)
            {
                offset = 0;
            }
            else
            {
                return false;
            }

            if (offset + 14 >= length) return false;
            if (report[offset + 1] != 0x00) return false;

            StringBuilder sb = new StringBuilder(4);
            for (int i = 0; i < 4; i++)
            {
                byte b = report[offset + 4 + i];
                if (b >= 0x20 && b <= 0x7E)
                {
                    sb.Append((char)b);
                }
            }
            if (sb.Length > 0)
            {
                devId = sb.ToString();
            }

            int batt = report[offset + 13];
            if (batt <= 0 || batt > 100) return false;

            batteryPercent = batt;
            // 重要：2.4G 接收器下 report[offset + 14] 在未插线时也会因唤醒/休眠在 0x01 与 0x00 间漂移，
            // 若直接作为充电状态会导致主界面闪电图标与百分比反复横跳。
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
            deviceName = GetModelNameByDevId(null, descriptor.ProductString);

            IntPtr handle = HidTransport.Open(descriptor.DevicePath);
            if (!HidTransport.IsValidHandle(handle)) return false;

            try
            {
                int outLen = descriptor.OutputReportLength >= ReportLength ? descriptor.OutputReportLength : ReportLength;
                int inLen = descriptor.InputReportLength >= ReportLength ? descriptor.InputReportLength : ReportLength;

                HidTransport.DrainReports(handle, inLen, 8);
                byte[] query = BuildDeviceInfoQuery();
                byte[] tx = new byte[outLen];
                Array.Copy(query, 0, tx, 0, Math.Min(query.Length, tx.Length));

                if (!HidTransport.WriteReport(handle, tx, outLen, 400)) return false;

                for (int attempt = 0; attempt < 4; attempt++)
                {
                    byte[] rx;
                    if (!HidTransport.ReadReport(handle, inLen, ReadTimeoutMs, out rx)) continue;

                    int batt;
                    bool chg;
                    string devId;
                    if (ParseDeviceInfoResponse(rx, rx.Length, out batt, out chg, out devId))
                    {
                        batteryPercent = batt;
                        isCharging = chg;
                        deviceName = GetModelNameByDevId(devId, descriptor.ProductString);
                        return true;
                    }
                }
            }
            catch { }
            finally
            {
                HidTransport.Close(handle);
            }

            return false;
        }

        public static List<MouseBatteryInfo> QueryAllAcerDevices()
        {
            lock (hidLock)
            {
                if ((DateTime.Now - lastQueryTime).TotalMilliseconds < QueryThrottleMs && cachedDevices.Count > 0)
                {
                    return new List<MouseBatteryInfo>(cachedDevices);
                }
                lastQueryTime = DateTime.Now;

                List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
                List<HidDeviceDescriptor> descriptors = new List<HidDeviceDescriptor>();
                descriptors.AddRange(HidTransport.Enumerate(VendorIdDongle, PidOmrDongle));
                descriptors.AddRange(HidTransport.Enumerate(VendorIdWired, PidOmrWired1));
                descriptors.AddRange(HidTransport.Enumerate(VendorIdWired, PidOmrWired2));

                List<HidDeviceDescriptor> candidates = new List<HidDeviceDescriptor>();
                for (int i = 0; i < descriptors.Count; i++)
                {
                    HidDeviceDescriptor d = descriptors[i];
                    if (d.UsagePage == 0xFF01 && d.Usage == 0x0010 &&
                        d.InputReportLength >= ReportLength && d.OutputReportLength >= ReportLength)
                    {
                        candidates.Add(d);
                    }
                }

                if (candidates.Count == 0)
                {
                    cachedDevices.Clear();
                    return result;
                }

                HidDeviceDescriptor selected = candidates[0];
                int battery = -1;
                bool charging = false;
                string name = cachedDeviceName;
                bool ok = false;

                for (int i = 0; i < candidates.Count; i++)
                {
                    if (QueryDevice(candidates[i], out battery, out charging, out name))
                    {
                        selected = candidates[i];
                        ok = true;
                        break;
                    }
                }

                bool isWireless = (selected.VendorId == VendorIdDongle);
                MouseBatteryInfo info = new MouseBatteryInfo();
                info.DeviceId = string.Format("Acer:{0:X4}:{1:X4}", selected.VendorId, selected.ProductId);
                info.HardwareFingerprint = string.Format("HID:{0:X4}:{1:X4}", selected.VendorId, selected.ProductId);
                info.HardwareId = selected.InstanceId;
                info.DriverId = "BuiltIn:AcerMouse";
                info.DeviceName = name;
                info.Brand = "Acer";
                info.Category = DeviceCategory.Mouse;
                info.Transport = isWireless ? "2.4G 无线" : "USB 有线";
                info.IsCustom = false;
                info.IsDonglePresent = isWireless;
                info.MaxPollingRate = 1000;
                info.SupportedPollingRates = new int[] { 125, 250, 500, 1000 };
                info.Capabilities.SupportsBattery = true;
                info.Capabilities.SupportsDpi = false;
                info.Capabilities.SupportsDpiStages = false;
                info.Capabilities.SupportsPollingRate = false;

                if (ok)
                {
                    info.IsConnected = true;
                    info.IsSleeping = false;
                    info.IsCharging = isWireless ? false : true;
                    info.BatteryPercent = battery;
                    info.ExactBatteryPercent = battery;
                    info.LastUpdated = DateTime.Now;
                    SaveHardwareCache(battery, info.IsCharging, name);
                }
                else
                {
                    info.IsConnected = false;
                    info.IsSleeping = true;
                    info.IsCharging = false;
                    info.BatteryPercent = cachedBattery > 0 ? cachedBattery : -1;
                    info.ExactBatteryPercent = cachedBattery > 0 ? cachedBattery : 0;
                    info.LastUpdated = cachedBattery > 0 ? cachedLastUpdated : DateTime.Now;
                }

                result.Add(info);
                cachedDevices = new List<MouseBatteryInfo>(result);
                return result;
            }
        }
    }
}
