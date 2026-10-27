// ============================================================================
// 模块名称: MchoseDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   迈从 (MCHOSE) G87 V2 三模机械键盘 2.4G 电量与充电状态适配：
//   0x4A 无线电池查询、0x87 有线电池查询、Report ID 0x13/0x09、
//   64 字节 HID 报告与离线电量缓存。
// 依赖关系: HidTransport.cs, MouseBatteryInfo.cs, Microsoft.Win32
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 修改命令字、校验和或电量解析时必须同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止字符串插值、空条件运算符、nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    public static class MchoseDeviceHelper
    {
        private static readonly object hidLock = new object();
        private const string CacheRegistryKey = @"Software\RazerBatteryTray\MchoseCache";

        public const int VendorIdMchose = 0x3837;
        public const int PidG87V2 = 0x3033;

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
            return vid == VendorIdMchose && pid == PidG87V2;
        }

        public static bool IsMchoseKeyboard(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return false;
            string lower = identifier.ToLowerInvariant();
            bool isVid = lower.Contains("vid_3837") || lower.Contains("3837:") || lower.Contains("3837&");
            if (!isVid) return false;
            return lower.Contains("pid_3033") || lower.Contains(":3033");
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
            catch (Exception diagEx) { Diagnostics.Log("Hardware/MchoseDeviceHelper.cs: 已忽略异常", diagEx); }
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
            catch (Exception diagEx) { Diagnostics.Log("Hardware/MchoseDeviceHelper.cs: 已忽略异常", diagEx); }
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
            catch (Exception diagEx) { Diagnostics.Log("Hardware/MchoseDeviceHelper.cs: 已忽略异常", diagEx); }
            finally
            {
                HidTransport.Close(handle);
            }
            return false;
        }

        public static List<MouseBatteryInfo> QueryAllMchoseDevices()
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
