// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// 迈从 V9 Turbo+ (3837:600A) 耳机电量：请求-应答式 FF90/0x0001 64 字节中断端点，
// Tx 55 65 01，Rx 55 65 <电量> <状态>。严格 C# 5.0 / .NET 4.0。
// 社区贡献者 @gallopcsy 提供真机抓包；本文件为 FDAS v2.0 包的核心融合实现。
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RazerBatteryTray
{
    public static class MchoseHeadsetHelper
    {
        private static readonly object hidLock = new object();
        private static readonly Dictionary<string, MouseBatteryInfo> cache = new Dictionary<string, MouseBatteryInfo>();
        private static DateTime lastQuery = DateTime.MinValue;

        public const int VendorId = 0x3837;
        public const int ProductId = 0x600A;
        public const int TargetUsagePage = 0xFF90;
        public const int TargetUsage = 0x0001;
        public const int ReportLength = 64;
        public const int BatteryByteOffset = 2;
        public const int StatusByteOffset = 3;

        // 55 65 00 1A = 链路降级帧，电量字段不可用，不是充电位。
        // 对照实验：同为充电状态时，PC USB 供电 44 次应答出现 9 次 0x1A + 15 次超时；
        // 充电宝供电 86 次应答出现 0 次 0x1A + 0 次超时。故 0x1A 不表示充电。
        public const byte DegradedStatus = 0x1A;

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
            report[0] = 0x55;
            report[1] = 0x65;
            report[2] = 0x01;
            return report;
        }

        // 返回 false 时调用方必须保留缓存读数；0x1A 帧绝不能渲染为 0%。
        public static bool ParseBatteryResponse(byte[] report, int length, out int batteryPercent)
        {
            batteryPercent = -1;
            if (report == null || length < 4 || report.Length < 4) return false;
            if (report[0] != 0x55 || report[1] != 0x65) return false;
            if (report[StatusByteOffset] == DegradedStatus) return false;
            int level = report[BatteryByteOffset];
            if (level < 0 || level > 100) return false;
            batteryPercent = level;
            return true;
        }

        public static List<MouseBatteryInfo> QueryAllMchoseHeadsetDevices()
        {
            lock (hidLock)
            {
                List<HidDeviceDescriptor> devices = HidTransport.Enumerate(VendorId, ProductId);
                HashSet<string> present = new HashSet<string>();
                List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
                bool due = (DateTime.UtcNow - lastQuery).TotalSeconds >= 5;
                string deviceId = "MCHOSE:" + VendorId.ToString("X4") + ":" + ProductId.ToString("X4");
                foreach (HidDeviceDescriptor device in devices)
                {
                    if (!IsSupportedDevice(device)) continue;
                    if (!present.Add(deviceId)) continue;
                    MouseBatteryInfo info;
                    cache.TryGetValue(deviceId, out info);
                    if (due || info == null)
                    {
                        int percent;
                        bool fresh = QueryBattery(device, out percent);
                        if (fresh)
                        {
                            if (info == null) info = NewInfo();
                            info.BatteryPercent = percent;
                            info.ExactBatteryPercent = percent;
                            info.IsSleeping = false;
                            info.IsConnected = true;
                            info.LastUpdated = DateTime.Now;
                        }
                        else if (info == null || info.BatteryPercent < 0)
                        {
                            if (info == null) info = NewInfo();
                            info.BatteryPercent = -1;
                            info.ExactBatteryPercent = -1;
                            info.IsSleeping = true;
                            info.IsConnected = true;
                        }
                        else
                        {
                            info.IsSleeping = true;
                        }
                        // 本机型不暴露充电状态，恒为 false，绝不臆造。
                        info.IsCharging = false;
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
            info.Brand = "MCHOSE";
            info.Category = DeviceCategory.Headset;
            info.DeviceName = "MCHOSE V9 Turbo+";
            info.BatteryPercent = -1;
            info.ExactBatteryPercent = -1;
            info.IsConnected = true;
            info.Capabilities.SupportsBattery = true;
            info.Capabilities.SupportsDpi = false;
            info.Capabilities.SupportsPollingRate = false;
            return info;
        }

        private static bool QueryBattery(HidDeviceDescriptor device, out int percent)
        {
            percent = -1;
            IntPtr handle = HidTransport.Open(device.DevicePath);
            if (!HidTransport.IsValidHandle(handle)) return false;
            try
            {
                byte[] request = BuildBatteryQuery();
                if (!HidTransport.WriteReport(handle, request, request.Length, WriteTimeoutMs)) return false;
                Stopwatch timer = Stopwatch.StartNew();
                while (timer.ElapsedMilliseconds < ReadWindowMs)
                {
                    int remain = Math.Max(1, ReadWindowMs - (int)timer.ElapsedMilliseconds);
                    byte[] response;
                    if (!HidTransport.ReadReport(handle, ReportLength, remain, out response)) break;
                    int level;
                    if (ParseBatteryResponse(response, response == null ? 0 : response.Length, out level))
                    {
                        percent = level;
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
