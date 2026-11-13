// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// 北通 BTP-KP20D (20BC:5159) 手柄电量：纯推送式 00FF/0x0003 64 字节中断帧，
// 02 15 <电量> .. <状态>，无查询指令。严格 C# 5.0 / .NET 4.0。
// 社区贡献者 @gallopcsy 提供真机抓包；本文件为 FDAS v2.0 包的核心融合实现。
using System;
using System.Collections.Generic;

namespace RazerBatteryTray
{
    public static class BeitongDeviceHelper
    {
        private static readonly object hidLock = new object();
        private static readonly Dictionary<string, MouseBatteryInfo> cache = new Dictionary<string, MouseBatteryInfo>();
        private static DateTime lastQuery = DateTime.MinValue;

        public const int VendorId = 0x20BC;
        public const int ProductId = 0x5159;
        public const int TargetUsagePage = 0x00FF;
        public const int TargetUsage = 0x0003;
        public const int ReportLength = 64;
        public const int BatteryByteOffset = 2;
        public const int StatusByteOffset = 4;
        public const byte ChargingBitMask = 0x01;

        private const int ReportWaitMs = 200;

        public static bool IsSupportedDevice(HidDeviceDescriptor device)
        {
            return device != null && device.VendorId == VendorId && device.ProductId == ProductId &&
                   device.UsagePage == TargetUsagePage && device.Usage == TargetUsage &&
                   device.InputReportLength == ReportLength;
        }

        // 推送式协议没有查询帧，此处仅用于契约自检与文档对照。
        public static byte[] BuildBatteryQuery()
        {
            return new byte[0];
        }

        public static bool ParseBatteryResponse(byte[] report, int length, out int batteryPercent, out bool isCharging)
        {
            batteryPercent = -1;
            isCharging = false;
            if (report == null || length < 6 || report.Length < 6) return false;
            if (report[0] != 0x02 || report[1] != 0x15) return false;
            int level = report[BatteryByteOffset];
            if (level < 0 || level > 100) return false;
            batteryPercent = level;
            isCharging = (report[StatusByteOffset] & ChargingBitMask) != 0;
            return true;
        }

        public static List<MouseBatteryInfo> QueryAllBeitongDevices()
        {
            lock (hidLock)
            {
                List<HidDeviceDescriptor> devices = HidTransport.Enumerate(VendorId, ProductId);
                HashSet<string> present = new HashSet<string>();
                List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
                bool due = (DateTime.UtcNow - lastQuery).TotalSeconds >= 5;
                string deviceId = "BEITONG:" + VendorId.ToString("X4") + ":" + ProductId.ToString("X4");
                foreach (HidDeviceDescriptor device in devices)
                {
                    if (!IsSupportedDevice(device)) continue;
                    if (!present.Add(deviceId)) continue;
                    MouseBatteryInfo info;
                    cache.TryGetValue(deviceId, out info);
                    if (due || info == null)
                    {
                        int percent;
                        bool charging;
                        bool fresh = TryReadPending(device, out percent, out charging);
                        if (fresh)
                        {
                            if (info == null) info = NewInfo();
                            info.BatteryPercent = percent;
                            info.ExactBatteryPercent = percent;
                            info.IsCharging = charging;
                            info.IsSleeping = false;
                            info.IsConnected = true;
                            info.LastUpdated = DateTime.Now;
                        }
                        else if (info == null || info.BatteryPercent < 0)
                        {
                            if (info == null) info = NewInfo();
                            info.BatteryPercent = -1;
                            info.ExactBatteryPercent = -1;
                            info.IsCharging = false;
                            info.IsSleeping = true;
                            info.IsConnected = true;
                        }
                        else
                        {
                            info.IsSleeping = true;
                            info.IsCharging = false;
                        }
                        info.DeviceId = deviceId;
                        info.HardwareId = "HID:" + VendorId.ToString("X4") + ":" + ProductId.ToString("X4");
                        info.HardwareFingerprint = info.HardwareId;
                        info.Transport = "2.4G 无线";
                        info.IsDonglePresent = true;
                        cache[deviceId] = info;
                    }
                    result.Add(info);
                }
                List<string> removed = new List<string>();
                foreach (string key in cache.Keys) if (!present.Contains(key)) removed.Add(key);
                foreach (string key in removed) cache.Remove(key);
                if (due) lastQuery = DateTime.UtcNow;
                return result;
            }
        }

        private static MouseBatteryInfo NewInfo()
        {
            MouseBatteryInfo info = new MouseBatteryInfo();
            info.Brand = "BEITONG";
            info.Category = DeviceCategory.Gamepad;
            info.DeviceName = "BEITONG BTP-KP20D";
            info.BatteryPercent = -1;
            info.ExactBatteryPercent = -1;
            info.IsConnected = true;
            info.Capabilities.SupportsBattery = true;
            info.Capabilities.SupportsDpi = false;
            info.Capabilities.SupportsPollingRate = false;
            return info;
        }

        // 纯推送协议：不写任何指令，只等待设备自己推来的状态帧。
        private static bool TryReadPending(HidDeviceDescriptor device, out int percent, out bool charging)
        {
            percent = -1;
            charging = false;
            IntPtr handle = HidTransport.Open(device.DevicePath);
            if (!HidTransport.IsValidHandle(handle)) return false;
            try
            {
                byte[] response;
                if (!HidTransport.ReadReport(handle, ReportLength, ReportWaitMs, out response)) return false;
                int level;
                bool isCharging;
                if (!ParseBatteryResponse(response, response == null ? 0 : response.Length, out level, out isCharging)) return false;
                percent = level;
                charging = isCharging;
                return true;
            }
            finally
            {
                HidTransport.Close(handle);
            }
        }
    }
}
