// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// GS3108T-PRO 身份隔离、2.4G 电量查询。C# 5 / .NET 4.0 / UTF-8 BOM。
// 依据用户本机官方 DeviceDriver.exe 的 435840/44F4C0/4511E0 静态分析；不写配置。
using System;
using System.Threading;
namespace RazerBatteryTray
{
    public static class GanssDeviceHelper
    {
        private static readonly object gate = new object();
        private static int battery = -1;
        private static DateTime sampled = DateTime.MinValue;
        private static DateTime lastAttempt = DateTime.MinValue;
        public static string LastResponseHex = "";
        public static bool IsKnownKeyboardName(string name)
        {
            return name != null && name.Trim().StartsWith("GS3108T-PRO", StringComparison.OrdinalIgnoreCase);
        }
        public static bool MatchesDeviceModel(CustomDeviceConfig config, MouseBatteryInfo device)
        {
            return config != null && device != null && device.Brand == "GANSS" && IsKnownKeyboardName(config.OriginalName);
        }
        public static bool NormalizeConfig(CustomDeviceConfig config)
        {
            if (config == null || !IsKnownKeyboardName(config.OriginalName)) return false;
            bool changed = config.Category != DeviceCategory.Keyboard;
            config.Category = DeviceCategory.Keyboard;
            bool hid = config.DeviceId != null && config.DeviceId.StartsWith("HID:", StringComparison.OrdinalIgnoreCase);
            if (hid && config.OriginalName.IndexOf("2.4G", StringComparison.OrdinalIgnoreCase) >= 0 && config.Transport != "2.4G 无线")
            { config.Transport = "2.4G 无线"; changed = true; }
            return changed;
        }
        public static byte[] BuildBatteryQuery()
        {
            byte[] report = new byte[33];
            report[1] = 0x20; report[2] = 1; report[32] = 0x21;
            return report;
        }
        public static bool TryParseBattery(byte[] report, out int percent)
        {
            percent = -1;
            if (report == null) return false;
            int offset = report.Length == 33 && report[0] == 0 ? 1 : 0;
            if (report.Length != 32 + offset || report[offset] != 0x20 || report[offset + 1] != 1 ||
                report[offset + 2] != 0 || report[offset + 3] > 100) return false;
            percent = report[offset + 3];
            return true;
        }
        public static MouseBatteryInfo QueryDevice()
        {
            lock (gate)
            {
                HidDeviceDescriptor endpoint = null;
                foreach (HidDeviceDescriptor d in HidTransport.Enumerate(0x05AC, 0x024F))
                    if (IsKnownKeyboardName(d.ProductString) && d.ProductString.IndexOf("2.4G", StringComparison.OrdinalIgnoreCase) >= 0 &&
                        d.UsagePage == 0xFF60 && d.Usage == 0x61 && d.InputReportLength == 33 && d.OutputReportLength == 33)
                    { endpoint = d; break; }
                if (endpoint == null) { battery = -1; sampled = DateTime.MinValue; return null; }
                if ((DateTime.UtcNow - lastAttempt).TotalSeconds >= 15)
                {
                    lastAttempt = DateTime.UtcNow;
                    IntPtr handle = HidTransport.Open(endpoint.DevicePath);
                    try
                    {
                        if (HidTransport.IsValidHandle(handle) && RapooNativeIo.Write(handle, BuildBatteryQuery(), 300))
                        {
                            DateTime deadline = DateTime.UtcNow.AddMilliseconds(500);
                            do
                            {
                                byte[] response;
                                if (!RapooNativeIo.Read(handle, 33, 100, out response)) continue;
                                LastResponseHex = BitConverter.ToString(response);
                                int percent;
                                if (TryParseBattery(response, out percent))
                                { battery = percent; sampled = DateTime.Now; LastResponseHex = BitConverter.ToString(response); break; }
                            } while (DateTime.UtcNow < deadline);
                        }
                    }
                    finally { HidTransport.Close(handle); }
                }
                return new MouseBatteryInfo {
                    DeviceId = "HID:05AC:024F", HardwareFingerprint = "05AC:024F", Brand = "GANSS",
                    Category = DeviceCategory.Keyboard, DeviceName = endpoint.ProductString, Transport = "2.4G 无线",
                    IsConnected = true, IsDonglePresent = true, BatteryPercent = battery, LastUpdated = sampled,
                    IsCharging = false // Official battery query supplies percentage only; no invented charge flag.
                };
            }
        }
    }
}
