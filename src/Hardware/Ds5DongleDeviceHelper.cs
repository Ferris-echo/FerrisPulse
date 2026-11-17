// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// Read-only sqlCRT DS5Dongle 3.22H support; hardware evidence contributed by Go2Heart.
// Protocol source: https://github.com/sqlCRT/ds5dongle-bl618-opensource
// Source branch master, tag v3.22, commit f7e36e1fb13151a7e9d376fe2f74f29cca104185.
// Reference files: src/usb_gamepad.c and src/main.c (protocol only; independent C# implementation).
// GET_REPORT F8 identifies firmware; F9 contains percent at [12], state at [13].
// Never send SET_REPORT, output reports, configuration or flash commands here.
using System;
using System.Collections.Generic;
using System.Text;

namespace RazerBatteryTray
{
    public static class Ds5DongleDeviceHelper
    {
        private static readonly object sync = new object();
        private static readonly Dictionary<string, MouseBatteryInfo> cache = new Dictionary<string, MouseBatteryInfo>();
        private static DateTime lastAttempt = DateTime.MinValue;

        public static bool IsSupportedEndpoint(HidDeviceDescriptor device)
        {
            return device != null && device.VendorId == 0x054C && device.ProductId == 0x0CE6 &&
                device.UsagePage == 1 && device.Usage == 5 && device.InputReportLength == 64 &&
                device.OutputReportLength == 48 && device.FeatureReportLength == 128;
        }

        public static bool IsSupportedFirmware(byte[] report)
        {
            if (report == null || report.Length != 128 || report[0] != 0xF8) return false;
            string version = Encoding.ASCII.GetString(report, 1, report.Length - 1).TrimEnd('\0');
            return string.Equals(version, "LCT616-DS5 3.22H", StringComparison.Ordinal);
        }

        public static MouseBatteryInfo ParseBatteryReport(byte[] report)
        {
            if (report == null || report.Length != 128 || report[0] != 0xF9 ||
                (report[2] & 0xFC) != 0x80) return null;
            int percent = report[12];
            int state = report[13];
            // 0xFF is the firmware's disconnected/unknown sentinel. Faults stay unknown.
            if (percent > 100 || state > 2) return null;
            return new MouseBatteryInfo
            {
                Brand = "Sony", Category = DeviceCategory.Gamepad,
                DeviceName = "DualSense (DS5Dongle 3.22H)", Transport = "Bluetooth / USB 接收器",
                BatteryPercent = percent, ExactBatteryPercent = percent,
                IsCharging = state == 1, IsConnected = true, IsDonglePresent = true,
                LastUpdated = DateTime.Now, DriverId = "BuiltIn:Ds5Dongle",
                CustomTooltip = "DS5Dongle 3.22H：只读 F9 电量；10% 硬件档位，非 1% 精度。"
            };
        }

        public static List<MouseBatteryInfo> QueryDevices()
        {
            lock (sync)
            {
                List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
                HashSet<string> present = new HashSet<string>();
                bool due = (DateTime.UtcNow - lastAttempt).TotalSeconds >= 5;
                foreach (HidDeviceDescriptor device in HidTransport.Enumerate(0x054C, 0x0CE6))
                {
                    if (!IsSupportedEndpoint(device)) continue;
                    present.Add(device.DevicePath);
                    MouseBatteryInfo info;
                    cache.TryGetValue(device.DevicePath, out info);
                    if (due)
                    {
                        bool identified = false;
                        MouseBatteryInfo fresh = null;
                        IntPtr handle = HidTransport.OpenFeature(device.DevicePath);
                        try
                        {
                            byte[] version;
                            identified = HidTransport.ReadFeatureReport(handle, 0xF8, 128, out version) && IsSupportedFirmware(version);
                            byte[] battery;
                            if (identified && HidTransport.ReadFeatureReport(handle, 0xF9, 128, out battery))
                                fresh = ParseBatteryReport(battery);
                        }
                        finally { HidTransport.Close(handle); }
                        if (fresh != null) info = fresh;
                        else if (identified || info != null)
                        {
                            // Do not present an old level/charging flag as a current reading.
                            info = new MouseBatteryInfo
                            {
                                Brand = "Sony", Category = DeviceCategory.Gamepad,
                                DeviceName = "DualSense (DS5Dongle 3.22H)", Transport = "Bluetooth / USB 接收器",
                                BatteryPercent = -1, ExactBatteryPercent = -1, IsConnected = false,
                                IsDonglePresent = true, IsSleeping = true, IsCharging = false,
                                DriverId = "BuiltIn:Ds5Dongle",
                                CustomTooltip = "接收器在位；手柄电量未知、未连接或读取失败。"
                            };
                        }
                        if (info != null)
                        {
                            info.DeviceId = "Sony:" + device.InstanceId;
                            info.HardwareId = "HID:054C:0CE6";
                            info.HardwareFingerprint = info.HardwareId;
                            new Ds5DongleDriver().PopulateCapabilities(info);
                            cache[device.DevicePath] = info;
                        }
                    }
                    if (info != null) result.Add(info);
                }
                List<string> removed = new List<string>();
                foreach (string path in cache.Keys) if (!present.Contains(path)) removed.Add(path);
                foreach (string path in removed) cache.Remove(path);
                if (due) lastAttempt = DateTime.UtcNow;
                return result;
            }
        }
    }

    public sealed class Ds5DongleDriver : HardwareDriverBase
    {
        public override string DriverId { get { return "BuiltIn:Ds5Dongle"; } }
        public override string Brand { get { return "Sony"; } }
        public override bool CanHandle(MouseBatteryInfo device)
        {
            return device != null && string.Equals(device.DriverId, DriverId, StringComparison.Ordinal);
        }
        public override void PopulateCapabilities(MouseBatteryInfo device)
        {
            if (device == null) return;
            device.DriverId = DriverId;
            if (device.Capabilities == null) device.Capabilities = new DeviceCapabilities();
            device.Capabilities.SupportsBattery = true;
            device.Capabilities.SupportsDpi = false;
            device.Capabilities.SupportsDpiStages = false;
            device.Capabilities.SupportsPollingRate = false;
        }
        public override List<MouseBatteryInfo> QueryDevices() { return Ds5DongleDeviceHelper.QueryDevices(); }
    }
}
