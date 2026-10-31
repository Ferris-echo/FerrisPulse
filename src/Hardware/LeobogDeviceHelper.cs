// ============================================================================
// 模块名称: LeobogDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   LEOBOG GM5 PRO 无线鼠标（VID 0x04F3 / PID 0x026F 2.4G 接收器）的状态帧读取。
//
//   物理指纹（实机 Win32 枚举，2026-10-31）:
//     VID 0x04F3 / PID 0x026F / REV 0x0301
//     ProductString "2.4G Wireless Receiver" / Manufacturer "0"
//     厂商集合 UsagePage 0x000A / Usage 0x0000 / Input 32 字节（另有 32 字节 Feature）
//     同接收器还带 0x0001/0x0006 键盘与 0x000C/0x0001 多媒体集合（键鼠一体公版固件）
//
//   协议（实机 60 秒连续抓包 + 关/开机对照，非文档推测）:
//     接收器每约 150ms 主动上报一帧 32 字节状态：
//       [0]=0x04 (Report ID)  [1]=在线位(1 在线 / 0 休眠或关机)
//       [4]=电量百分比(0..100)  [8]=0x01  [9]=0x60（含义未确认，疑似另一路电池，暂不使用）
//     实测对照：
//       鼠标开机      -> 04 01 01 01 5A 00 00 00 01 60 ...  (在线, 0x5A=90)
//       关闭鼠标电源  -> 04 00 01 01 5A 00 00 00 01 60 ...  ([1] 立即变 0)
//       重新开机瞬间  -> 04 01 01 01 64 00 00 00 01 60 ...  (0x64=100，电量计复位后回落 0x5A)
//
//   ⚠️ 能力边界（物理事实优先）:
//     仅声明「电量 + 在线/休眠」。充电位在抓包中未见可辨识标志位（[1] 只表达在线性，
//     0x64 只是开机瞬态），故不声明充电，避免伪造闪电图标。
//     DPI/回报率/灯光写指令未实测，一律不声明。
// 依赖关系: HidTransport.cs, MouseBatteryInfo.cs, DeviceCapabilities.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、协议解析或签名时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止字符串插值、空条件运算符、nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    public static class LeobogDeviceHelper
    {
        private static readonly object hidLock = new object();
        private const string CacheRegistryKey = @"Software\RazerBatteryTray\LeobogCache";

        public const int LeobogVid = 0x04F3;
        public const int Gm5ProPid = 0x026F;
        public const int VendorUsagePage = 0x000A;
        public const int VendorUsage = 0x0000;
        public const int StatusReportLength = 32;
        public const byte StatusReportId = 0x04;

        private const int QueryTimeoutMs = 800;
        private const int QueryThrottleSeconds = 5;

        public static int CachedBatteryPercent = -1;
        public static bool CachedIsOnline = false;
        public static string CachedDeviceName = "LEOBOG GM5 PRO";
        public static DateTime CachedLastUpdated = DateTime.MinValue;
        public static string LastReadError = "";

        private static readonly Dictionary<string, MouseBatteryInfo> cache = new Dictionary<string, MouseBatteryInfo>();
        private static DateTime lastQuery = DateTime.MinValue;

        static LeobogDeviceHelper()
        {
            LoadHardwareCache();
        }

        public static bool IsSupportedDevice(HidDeviceDescriptor device)
        {
            if (device == null) return false;
            return device.VendorId == LeobogVid && device.ProductId == Gm5ProPid;
        }

        /// <summary>
        /// 只认领 0x000A/0x0000 的 32 字节输入集合；键盘/多媒体集合一律不碰。
        /// </summary>
        public static bool IsVendorEndpoint(HidDeviceDescriptor device)
        {
            if (device == null) return false;
            return device.UsagePage == VendorUsagePage && device.Usage == VendorUsage &&
                   device.InputReportLength == StatusReportLength;
        }

        public static bool IsNativeDevice(MouseBatteryInfo dev)
        {
            if (dev == null) return false;
            if (!string.Equals(dev.Brand, "LEOBOG", StringComparison.OrdinalIgnoreCase)) return false;
            if (dev.Category != DeviceCategory.Mouse) return false;
            if (string.IsNullOrEmpty(dev.DeviceId)) return false;
            return dev.DeviceId.StartsWith("LEOBOG:04F3:026F", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 解析 32 字节状态帧：[1] 在线位、[4] 电量百分比。
        /// 校验 Report ID 与取值范围，拒绝任何越界帧。
        /// </summary>
        public static bool TryParseStatus(byte[] frame, out bool online, out int battery)
        {
            online = false;
            battery = -1;
            if (frame == null || frame.Length < 10) return false;
            if (frame[0] != StatusReportId) return false;
            if (frame[1] > 1) return false;
            int level = frame[4];
            if (level > 100) return false;
            online = (frame[1] == 1);
            battery = level;
            return true;
        }

        public static List<MouseBatteryInfo> QueryDevices()
        {
            lock (hidLock)
            {
                List<HidDeviceDescriptor> devices = HidTransport.Enumerate(LeobogVid, Gm5ProPid);
                HashSet<string> present = new HashSet<string>();
                List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
                bool due = (DateTime.UtcNow - lastQuery).TotalSeconds >= QueryThrottleSeconds;

                for (int i = 0; i < devices.Count; i++)
                {
                    HidDeviceDescriptor device = devices[i];
                    if (!IsSupportedDevice(device) || !IsVendorEndpoint(device)) continue;

                    string deviceId = "LEOBOG:04F3:026F";
                    if (!present.Add(deviceId)) continue;

                    MouseBatteryInfo info;
                    cache.TryGetValue(deviceId, out info);
                    if (due || info == null)
                    {
                        bool online;
                        int battery;
                        bool ok = TryReadStatus(device, out online, out battery);
                        if (info == null) info = CreateEmptyInfo();
                        if (ok)
                        {
                            info.BatteryPercent = battery;
                            info.ExactBatteryPercent = battery;
                            info.IsConnected = online;
                            info.IsSleeping = !online;
                            info.IsCharging = false;
                            info.LastUpdated = DateTime.Now;
                            CachedBatteryPercent = battery;
                            CachedIsOnline = online;
                            CachedLastUpdated = info.LastUpdated;
                            SaveHardwareCache();
                        }
                        else
                        {
                            // 读不到帧时维持“接收器在场但鼠标休眠”的诚实状态，沿用缓存电量。
                            info.IsConnected = CachedIsOnline;
                            info.IsSleeping = true;
                            info.IsCharging = false;
                            info.BatteryPercent = CachedBatteryPercent > 0 ? CachedBatteryPercent : -1;
                            info.ExactBatteryPercent = info.BatteryPercent;
                        }
                    }

                    info.DeviceId = deviceId;
                    info.HardwareId = "HID:04F3:026F";
                    info.HardwareFingerprint = info.HardwareId;
                    info.Transport = "2.4G 无线";
                    info.IsDonglePresent = true;
                    info.IsCustom = false;
                    cache[deviceId] = info;
                    result.Add(info);
                }

                List<string> removed = new List<string>();
                foreach (string key in cache.Keys)
                {
                    if (!present.Contains(key)) removed.Add(key);
                }
                for (int i = 0; i < removed.Count; i++) cache.Remove(removed[i]);

                if (due) lastQuery = DateTime.UtcNow;
                return result;
            }
        }

        private static MouseBatteryInfo CreateEmptyInfo()
        {
            MouseBatteryInfo info = new MouseBatteryInfo();
            info.Brand = "LEOBOG";
            info.Category = DeviceCategory.Mouse;
            info.DeviceName = CachedDeviceName;
            info.BatteryPercent = -1;
            info.ExactBatteryPercent = -1;
            info.LastUpdated = CachedLastUpdated;
            info.Capabilities = new DeviceCapabilities();
            info.Capabilities.SupportsBattery = true;
            info.Capabilities.SupportsDpi = false;
            info.Capabilities.SupportsDpiStages = false;
            info.Capabilities.SupportsPollingRate = false;
            info.Capabilities.SupportsXyIndependence = false;
            info.Capabilities.SupportsHardwareDpiMonitor = false;
            info.CustomTooltip = "LEOBOG GM5 PRO 2.4G：0x000A 状态帧 [1]=在线位，[4]=电量百分比；充电位未实测，不显示闪电。";
            return info;
        }

        private static bool TryReadStatus(HidDeviceDescriptor device, out bool online, out int battery)
        {
            online = false;
            battery = -1;
            IntPtr handle = HidTransport.Open(device.DevicePath);
            if (!HidTransport.IsValidHandle(handle))
            {
                LastReadError = "invalid handle";
                return false;
            }
            int attempts = 0;
            string lastHex = "";
            try
            {
                Stopwatch timer = Stopwatch.StartNew();
                while (timer.ElapsedMilliseconds < QueryTimeoutMs)
                {
                    int remain = QueryTimeoutMs - (int)timer.ElapsedMilliseconds;
                    if (remain < 20) remain = 20;
                    byte[] frame;
                    attempts++;
                    if (!HidTransport.ReadReportExact(handle, device.InputReportLength, remain, out frame)) continue;
                    lastHex = "";
                    for (int k = 0; k < frame.Length; k++) lastHex += frame[k].ToString("X2") + " ";
                    bool isOnline;
                    int level;
                    if (TryParseStatus(frame, out isOnline, out level))
                    {
                        online = isOnline;
                        battery = level;
                        LastReadError = "";
                        return true;
                    }
                }
                LastReadError = "loop_end attempts=" + attempts + " last=" + lastHex;
            }
            catch (Exception ex)
            {
                LastReadError = ex.ToString();
                return false;
            }
            finally
            {
                HidTransport.Close(handle);
            }
            return false;
        }

        public static void LoadHardwareCache()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(CacheRegistryKey, false))
                {
                    if (key == null) return;
                    object percent = key.GetValue("BatteryPercent");
                    if (percent != null)
                    {
                        int value = Convert.ToInt32(percent);
                        if (value >= 0 && value <= 100) CachedBatteryPercent = value;
                    }
                    object online = key.GetValue("IsOnline");
                    if (online != null) CachedIsOnline = (Convert.ToInt32(online) == 1);
                    string name = key.GetValue("DeviceName") as string;
                    if (!string.IsNullOrEmpty(name)) CachedDeviceName = name;
                    string updated = key.GetValue("LastUpdated") as string;
                    if (!string.IsNullOrEmpty(updated))
                    {
                        DateTime dt;
                        if (DateTime.TryParse(updated, out dt)) CachedLastUpdated = dt;
                    }
                }
            }
            catch { }
        }

        public static void SaveHardwareCache()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(CacheRegistryKey))
                {
                    if (key == null) return;
                    if (CachedBatteryPercent >= 0) key.SetValue("BatteryPercent", CachedBatteryPercent);
                    key.SetValue("IsOnline", CachedIsOnline ? 1 : 0);
                    if (!string.IsNullOrEmpty(CachedDeviceName)) key.SetValue("DeviceName", CachedDeviceName);
                    if (CachedLastUpdated != DateTime.MinValue) key.SetValue("LastUpdated", CachedLastUpdated.ToString("o"));
                }
            }
            catch { }
        }
    }
}
