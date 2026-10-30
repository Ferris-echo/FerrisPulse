// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// ATK V75X / VGN V98 PRO 共用 320F:5088 Telink 接收器的电量查询：
// Report 4、非零序号、0x1A、byte[8]=电量、byte[9]=充电；仅使用 Win32 HID 与 C# 5。
// 两者协议一致，必须靠产品串精确区分（VGN S99 等其它同 VID 型号仍拒绝）。
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

        public static bool IsAtkV75x(string productString)
        {
            return !string.IsNullOrEmpty(productString) &&
                productString.IndexOf("ATK V75X", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// 同一个 320F:5088 Telink 接收器被 ATK V75X 与 VGN V98 PRO 共用（协议完全一致，
        /// 仅产品串不同）。必须靠产品串精确区分，严禁仅凭 VID/PID 泛化——例如 VGN S99
        /// 虽然同为 320F 系但也必须继续拒绝。
        /// </summary>
        public static bool IsVgnV98Pro(string productString)
        {
            if (string.IsNullOrEmpty(productString)) return false;
            if (productString.IndexOf("VGN", StringComparison.OrdinalIgnoreCase) < 0) return false;
            return productString.IndexOf("V98", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsSupportedDevice(HidDeviceDescriptor device)
        {
            if (device == null || device.VendorId != 0x320F) return false;
            if (device.ProductId != 0x5088 && device.ProductId != 0x5055) return false;
            return IsAtkV75x(device.ProductString) || IsVgnV98Pro(device.ProductString);
        }

        public static string ResolveModelName(string productString)
        {
            if (IsVgnV98Pro(productString)) return "VGN V98 PRO";
            if (IsAtkV75x(productString)) return "ATK V75X";
            return "ATK 320F 键盘";
        }

        public static string ResolveBrand(string productString)
        {
            if (IsVgnV98Pro(productString)) return "VGN";
            return "ATK";
        }

        public static string ResolveDeviceIdPrefix(string productString)
        {
            return IsVgnV98Pro(productString) ? "VGN" : "ATK";
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
            return ParseBatteryResponseEx(report, requestCode, "ATK V75X", "ATK");
        }

        public static MouseBatteryInfo ParseBatteryResponseEx(byte[] report, ushort requestCode, string modelName, string brand)
        {
            if (report == null || report.Length < 10 || requestCode == 0 || report[0] != 4 ||
                report[1] != (byte)requestCode || report[2] != (byte)(requestCode >> 8) ||
                report[3] != 0x1A || report[8] > 100 || report[9] > 1) return null;
            return new MouseBatteryInfo
            {
                Brand = brand,
                Category = DeviceCategory.Keyboard,
                DeviceName = modelName,
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
                    string modelName = ResolveModelName(device.ProductString);
                    string brand = ResolveBrand(device.ProductString);
                    string deviceId = ResolveDeviceIdPrefix(device.ProductString) + ":320F:" + device.ProductId.ToString("X4");
                    if (!present.Add(deviceId)) continue;
                    MouseBatteryInfo info;
                    cache.TryGetValue(deviceId, out info);
                    if (due || info == null)
                    {
                        MouseBatteryInfo fresh = QueryBattery(device, modelName, brand);
                        if (fresh != null) info = fresh;
                        else
                        {
                            if (info == null) info = new MouseBatteryInfo { Brand = brand, Category = DeviceCategory.Keyboard, DeviceName = modelName, BatteryPercent = -1, ExactBatteryPercent = -1 };
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

        private static MouseBatteryInfo QueryBattery(HidDeviceDescriptor device, string modelName, string brand)
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
                    MouseBatteryInfo info = ParseBatteryResponseEx(response, sequence, modelName, brand);
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