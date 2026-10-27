// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// ATK V75X Evision 电量查询：Report 4、非零序号、0x1A；仅使用 Win32 HID 与 C# 5。
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RazerBatteryTray
{
    public static partial class AtkDeviceHelper
    {
        private static readonly object hidLock = new object();
        private static readonly Dictionary<string, MouseBatteryInfo> cache = new Dictionary<string, MouseBatteryInfo>();
        private static DateTime lastQuery = DateTime.MinValue;
        private static ushort sequence;

        public static bool IsSupportedDevice(HidDeviceDescriptor device)
        {
            return device != null && device.VendorId == 0x320F &&
                (device.ProductId == 0x5088 || device.ProductId == 0x5055) &&
                !string.IsNullOrEmpty(device.ProductString) &&
                device.ProductString.IndexOf("ATK V75X", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static byte[] BuildBatteryQuery(ushort requestCode, int length)
        {
            if (requestCode == 0 || length < 10 || length > 65) throw new ArgumentOutOfRangeException("requestCode");
            byte[] report = new byte[length];
            report[0] = 4;
            report[1] = (byte)requestCode;
            report[2] = (byte)(requestCode >> 8);
            report[3] = 0x1A;
            return report;
        }

        public static MouseBatteryInfo ParseBatteryResponse(byte[] report, ushort requestCode)
        {
            if (report == null || report.Length < 10 || requestCode == 0 || report[0] != 4 ||
                report[1] != (byte)requestCode || report[2] != (byte)(requestCode >> 8) ||
                report[3] != 0x1A || report[8] > 100 || report[9] > 1) return null;
            return new MouseBatteryInfo
            {
                Brand = "ATK",
                Category = DeviceCategory.Keyboard,
                DeviceName = "ATK V75X",
                BatteryPercent = report[8],
                ExactBatteryPercent = report[8],
                IsCharging = report[9] == 1,
                IsConnected = true,
                LastUpdated = DateTime.Now
            };
        }

        public static List<MouseBatteryInfo> QueryAllAtkDevices()
        {
            lock (hidLock)
            {
                List<HidDeviceDescriptor> devices = HidTransport.Enumerate(0x320F, 0);
                HashSet<string> present = new HashSet<string>();
                List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
                bool due = (DateTime.UtcNow - lastQuery).TotalSeconds >= 5;
                foreach (HidDeviceDescriptor device in devices)
                {
                    if (!IsSupportedDevice(device) || device.UsagePage != 0xFF1C || device.Usage != 0x92 ||
                        device.InputReportLength != 64 || device.OutputReportLength != 64) continue;
                    string deviceId = "ATK:320F:" + device.ProductId.ToString("X4");
                    if (!present.Add(deviceId)) continue;
                    MouseBatteryInfo info;
                    cache.TryGetValue(deviceId, out info);
                    if (due || info == null)
                    {
                        MouseBatteryInfo fresh = QueryBattery(device);
                        if (fresh != null) info = fresh;
                        else
                        {
                            if (info == null) info = new MouseBatteryInfo { Brand = "ATK", Category = DeviceCategory.Keyboard, DeviceName = "ATK V75X", BatteryPercent = -1, ExactBatteryPercent = -1 };
                            info.IsConnected = false;
                            info.IsSleeping = true;
                            info.IsCharging = false;
                        }
                        info.DeviceId = deviceId;
                        info.HardwareId = "HID:320F:" + device.ProductId.ToString("X4");
                        info.HardwareFingerprint = info.HardwareId;
                        info.Transport = device.ProductId == 0x5088 ? "2.4G 无线" : "USB 有线";
                        info.IsDonglePresent = device.ProductId == 0x5088;
                        cache[deviceId] = info;
                    }
                    result.Add(info);
                }
                List<string> removed = new List<string>();
                foreach (string deviceId in cache.Keys) if (!present.Contains(deviceId)) removed.Add(deviceId);
                foreach (string deviceId in removed) cache.Remove(deviceId);
                if (due) lastQuery = DateTime.UtcNow;
                return result;
            }
        }

        private static MouseBatteryInfo QueryBattery(HidDeviceDescriptor device)
        {
            IntPtr handle = HidTransport.Open(device.DevicePath);
            if (!HidTransport.IsValidHandle(handle)) return null;
            try
            {
                sequence = (ushort)(sequence == ushort.MaxValue ? 1 : sequence + 1);
                byte[] request = BuildBatteryQuery(sequence, device.OutputReportLength);
                if (!HidTransport.WriteReport(handle, request, request.Length, 200)) return null;
                Stopwatch timer = Stopwatch.StartNew();
                while (timer.ElapsedMilliseconds < 400)
                {
                    byte[] response;
                    if (!HidTransport.ReadReport(handle, device.InputReportLength, Math.Max(1, 400 - (int)timer.ElapsedMilliseconds), out response)) break;
                    MouseBatteryInfo info = ParseBatteryResponse(response, sequence);
                    if (info != null) return info;
                }
            }
            finally
            {
                HidTransport.Close(handle);
            }
            return null;
        }
    }
}