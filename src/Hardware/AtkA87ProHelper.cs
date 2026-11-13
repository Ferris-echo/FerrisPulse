// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// ATK A87 PRO (373B:111F) 键盘电量：VIA 风格 FF60/0x61 33 字节中断端点，
// Tx 00 02 96 24，Rx 00 02 96 24 <电量> <状态>。严格 C# 5.0 / .NET 4.0。
// 社区贡献者 @gallopcsy 提供真机抓包；本文件为 FDAS v2.0 包的核心融合实现。
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RazerBatteryTray
{
    public static class AtkA87ProHelper
    {
        private static readonly object hidLock = new object();
        private static readonly Dictionary<string, MouseBatteryInfo> cache = new Dictionary<string, MouseBatteryInfo>();
        private static DateTime lastQuery = DateTime.MinValue;

        public const int VendorId = 0x373B;
        public const int ProductId = 0x111F;
        public const int TargetUsagePage = 0xFF60;
        public const int TargetUsage = 0x0061;
        public const int ReportLength = 33;
        public const int BatteryByteOffset = 4;
        public const int StatusByteOffset = 5;
        public const byte ChargingBitMask = 0x01;

        // 400ms 而非 300ms：实测 300ms 会在键盘唤醒瞬间被设备静默丢弃，
        // 表现为永久 -1%。这是本机型最容易踩的坑，禁止下调。
        private const int WriteTimeoutMs = 400;
        private const int ReadWindowMs = 1200;

        public static bool IsSupportedDevice(HidDeviceDescriptor device)
        {
            return device != null && device.VendorId == VendorId && device.ProductId == ProductId &&
                   device.UsagePage == TargetUsagePage && device.Usage == TargetUsage &&
                   device.InputReportLength == ReportLength && device.OutputReportLength == ReportLength;
        }

        public static byte[] BuildBatteryQuery()
        {
            byte[] report = new byte[ReportLength];
            report[1] = 0x02; // id_get_keyboard_value
            report[2] = 0x96; // channel id
            report[3] = 0x24; // sub command
            return report;
        }

        // 返回 false 时调用方必须保留上一次缓存读数，绝不可显示 0%。
        public static bool ParseBatteryResponse(byte[] report, int length, out int batteryPercent, out bool isCharging)
        {
            batteryPercent = -1;
            isCharging = false;
            if (report == null || length < 6 || report.Length < 6) return false;
            if (report[1] != 0x02 || report[2] != 0x96 || report[3] != 0x24) return false;
            int level = report[BatteryByteOffset];
            byte status = report[StatusByteOffset];
            if (level < 0 || level > 100) return false;
            // 字节 4 为 0 且状态位为 0 时键盘处于休眠，该帧无意义。
            if (level == 0 && status == 0x00) return false;
            batteryPercent = level;
            isCharging = (status & ChargingBitMask) != 0;
            return true;
        }

        public static List<MouseBatteryInfo> QueryAllA87ProDevices()
        {
            lock (hidLock)
            {
                List<HidDeviceDescriptor> devices = HidTransport.Enumerate(VendorId, ProductId);
                HashSet<string> present = new HashSet<string>();
                List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
                bool due = (DateTime.UtcNow - lastQuery).TotalSeconds >= 5;
                string deviceId = "ATK:" + VendorId.ToString("X4") + ":" + ProductId.ToString("X4");
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
                        bool fresh = QueryBattery(device, out percent, out charging);
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
                            // 首次即失败：如实标记未知，不臆造数值。
                            if (info == null) info = NewInfo();
                            info.BatteryPercent = -1;
                            info.ExactBatteryPercent = -1;
                            info.IsCharging = false;
                            info.IsSleeping = true;
                            info.IsConnected = true;
                        }
                        else
                        {
                            // 已有缓存：保留上次读数，仅标记休眠。
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
            info.Brand = "ATK";
            info.Category = DeviceCategory.Keyboard;
            info.DeviceName = "ATK A87 PRO";
            info.BatteryPercent = -1;
            info.ExactBatteryPercent = -1;
            info.IsConnected = true;
            info.Capabilities.SupportsBattery = true;
            info.Capabilities.SupportsDpi = false;
            info.Capabilities.SupportsPollingRate = false;
            return info;
        }

        private static bool QueryBattery(HidDeviceDescriptor device, out int percent, out bool charging)
        {
            percent = -1;
            charging = false;
            IntPtr handle = HidTransport.Open(device.DevicePath);
            if (!HidTransport.IsValidHandle(handle)) return false;
            try
            {
                byte[] request = BuildBatteryQuery();
                // 禁止在写入前调用 DrainReports：实测排空后紧接着的 ReadReport 会失败。
                if (!HidTransport.WriteReport(handle, request, request.Length, WriteTimeoutMs)) return false;
                Stopwatch timer = Stopwatch.StartNew();
                while (timer.ElapsedMilliseconds < ReadWindowMs)
                {
                    int remain = Math.Max(1, ReadWindowMs - (int)timer.ElapsedMilliseconds);
                    byte[] response;
                    if (!HidTransport.ReadReport(handle, ReportLength, remain, out response)) break;
                    int level;
                    bool isCharging;
                    if (ParseBatteryResponse(response, response == null ? 0 : response.Length, out level, out isCharging))
                    {
                        percent = level;
                        charging = isCharging;
                        return true;
                    }
                }
            }
            finally
            {
                HidTransport.Close(handle);
            }
            return false;
        }
    }
}
