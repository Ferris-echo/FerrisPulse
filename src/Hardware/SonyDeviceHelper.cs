// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// Sony DualSense Edge USB 电量；PULSE Explore 双耳只读协议见 SonyDeviceHelper.PulseExplore.cs。
using System;
using System.Collections.Generic;

namespace RazerBatteryTray
{
    public static partial class SonyDeviceHelper
    {
        private static readonly object hidLock = new object();
        private static readonly Dictionary<string, MouseBatteryInfo> cache = new Dictionary<string, MouseBatteryInfo>();
        private static DateTime lastQuery = DateTime.MinValue;

        public static MouseBatteryInfo ParseDualSenseUsbReport(byte[] report)
        {
            if (report == null || report.Length != 64 || report[0] != 1) return null;
            int capacity = report[53] & 15;
            int status = report[53] >> 4;
            if (capacity > 10 || status > 2) return null;
            int percent = status == 2 ? 100 : Math.Min(capacity * 10 + 5, 100);
            return new MouseBatteryInfo
            {
                Brand = "Sony",
                Category = DeviceCategory.Gamepad,
                DeviceName = "DualSense Edge",
                Transport = "USB 有线",
                BatteryPercent = percent,
                ExactBatteryPercent = percent,
                IsCharging = status == 1,
                IsConnected = true,
                LastUpdated = DateTime.Now,
                CustomTooltip = "电量来自 10% 硬件档位，显示区间中点估值；非 1% 精度。"
            };
        }

        public static List<MouseBatteryInfo> QueryAllSonyDevices()
        {
            lock (hidLock)
            {
                List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
                HashSet<string> present = new HashSet<string>();
                bool due = (DateTime.UtcNow - lastQuery).TotalSeconds >= 5;
                foreach (HidDeviceDescriptor device in HidTransport.Enumerate(0x054C, 0x0DF2))
                {
                    if (device.UsagePage != 1 || device.Usage != 5 || device.InputReportLength != 64) continue;
                    present.Add(device.DevicePath);
                    MouseBatteryInfo info;
                    cache.TryGetValue(device.DevicePath, out info);
                    if (due || info == null)
                    {
                        MouseBatteryInfo fresh = null;
                        IntPtr handle = HidTransport.OpenRead(device.DevicePath);
                        try
                        {
                            byte[] report;
                            if (HidTransport.ReadReport(handle, 64, 200, out report)) fresh = ParseDualSenseUsbReport(report);
                        }
                        finally { HidTransport.Close(handle); }
                        if (fresh != null) info = fresh;
                        else
                        {
                            if (info == null) info = new MouseBatteryInfo { Brand = "Sony", Category = DeviceCategory.Gamepad, DeviceName = "DualSense Edge", Transport = "USB 有线", BatteryPercent = -1, ExactBatteryPercent = -1 };
                            info.IsConnected = true;
                            info.IsSleeping = true;
                            info.IsCharging = false;
                        }
                        info.DeviceId = "Sony:" + device.InstanceId;
                        info.HardwareId = "HID:054C:0DF2";
                        info.HardwareFingerprint = info.HardwareId;
                        cache[device.DevicePath] = info;
                    }
                    result.Add(info);
                }
                List<string> removed = new List<string>();
                foreach (string path in cache.Keys) if (!present.Contains(path)) removed.Add(path);
                foreach (string path in removed) cache.Remove(path);
                if (due) lastQuery = DateTime.UtcNow;
                result.AddRange(QueryAllPulseExploreDevices());
                return result;
            }
        }
    }
}