// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// GS3108T-PRO 身份隔离、USB 有线 / 2.4G 传输判别与 2.4G 电量查询。C# 5 / .NET 4.0 / UTF-8 BOM。
// 依据用户本机官方 config.xml 的 mode 表：USB=产品串 GS3108T-PRO + MI_00，2.4G=产品串 GS3108T-PRO 2.4G + MI_03；
// 电量协议依据 DeviceDriver.exe 的 435840/44F4C0/4511E0 静态分析与实机抓包；不写配置。
using System;
using System.Collections.Generic;
using System.Threading;
namespace RazerBatteryTray
{
    public static class GanssDeviceHelper
    {
        public const string TransportWired = "USB 有线";
        public const string TransportWireless = "2.4G 无线";

        private static readonly object gate = new object();
        private static int battery = -1;
        private static DateTime sampled = DateTime.MinValue;
        private static DateTime lastAttempt = DateTime.MinValue;
        public static string LastResponseHex = "";
        public static bool IsKnownKeyboardName(string name)
        {
            return name != null && name.Trim().StartsWith("GS3108T-PRO", StringComparison.OrdinalIgnoreCase);
        }
        public static bool IsWirelessKeyboardName(string name)
        {
            return IsKnownKeyboardName(name) && name.IndexOf("2.4G", StringComparison.OrdinalIgnoreCase) >= 0;
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
            if (hid && IsWirelessKeyboardName(config.OriginalName) && config.Transport != TransportWireless)
            { config.Transport = TransportWireless; changed = true; }
            return changed;
        }

        // 官方 config.xml 的 USB 模式身份：产品串不带 2.4G、顶层 MI_00 键盘集合。
        // 接收器自身也会枚举 MI_00 键盘集合，所以产品串必须不带 2.4G，二者缺一不可。
        public static bool IsWiredKeyboardEndpoint(HidDeviceDescriptor device)
        {
            if (device == null || !IsKnownKeyboardName(device.ProductString)) return false;
            if (device.ProductString.IndexOf("2.4G", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (device.InstanceId == null || device.InstanceId.IndexOf("&COL", StringComparison.OrdinalIgnoreCase) >= 0) return false;
            if (device.InstanceId.IndexOf("&MI_00", StringComparison.OrdinalIgnoreCase) < 0) return false;
            return device.UsagePage == 0x01 && (device.Usage == 0x06 || device.Usage == 0x02);
        }

        // 有线复合设备的厂商接口（实机 FF13/01 65 字节；部分固件为 FF68/61）。
        // 仅用于给 MI_00 键盘集合加第二重证据，避免把只有残留接口的充电态误判成有线。
        public static bool HasWiredVendorInterface(HidDeviceDescriptor[] descriptors)
        {
            if (descriptors == null) return false;
            foreach (HidDeviceDescriptor d in descriptors)
            {
                if (d == null || !IsKnownKeyboardName(d.ProductString)) continue;
                if (d.ProductString.IndexOf("2.4G", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (d.InstanceId != null && d.InstanceId.IndexOf("&COL", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                if (d.UsagePage == 0xFF13 && d.Usage == 0x01 && d.InputReportLength == 65 && d.OutputReportLength == 65) return true;
                if (d.UsagePage == 0xFF68 && d.Usage == 0x61) return true;
            }
            return false;
        }

        // 电量端点保持既有实机验证过的 2.4G FF60/61 33 字节；不得把有线 FF13/65 字节接口当电量端点。
        public static HidDeviceDescriptor FindBatteryEndpoint(HidDeviceDescriptor[] descriptors)
        {
            if (descriptors == null) return null;
            foreach (HidDeviceDescriptor d in descriptors)
            {
                if (d == null || !IsKnownKeyboardName(d.ProductString)) continue;
                if (d.ProductString.IndexOf("2.4G", StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (d.UsagePage == 0xFF60 && d.Usage == 0x61 && d.InputReportLength == 33 && d.OutputReportLength == 33)
                    return d;
            }
            return null;
        }

        // 有线模式下射频关闭、接收器不再回报电量：只能把最后一次 2.4G 读数作为历史信息展示。
        public static int LastKnownBatteryPercent { get { return battery; } }
        public static DateTime LastKnownSampleTime { get { return sampled; } }
        public static string FormatWiredFootnote(int lastPercent, DateTime lastSample)
        {
            if (lastPercent >= 0 && lastSample != DateTime.MinValue)
                return "USB 有线 · 上次 2.4G 读数 " + lastPercent + "%（" + lastSample.ToString("MM-dd HH:mm:ss") + "）";
            return "USB 有线 · 供电中（2.4G 电量不可读）";
        }

        // 判定规则：MI_00 键盘集合 + 厂商配置接口同时成立才是有线；任一缺失则保持 2.4G 或未知。
        public static string ResolveTransport(HidDeviceDescriptor[] descriptors)
        {
            bool wiredKeyboard = false;
            if (descriptors != null)
            {
                foreach (HidDeviceDescriptor d in descriptors)
                {
                    if (IsWiredKeyboardEndpoint(d)) { wiredKeyboard = true; break; }
                }
            }
            if (wiredKeyboard && HasWiredVendorInterface(descriptors)) return TransportWired;
            if (FindBatteryEndpoint(descriptors) != null) return TransportWireless;
            return "";
        }

        public static string FindWiredDeviceName(HidDeviceDescriptor[] descriptors)
        {
            if (descriptors != null)
            {
                foreach (HidDeviceDescriptor d in descriptors)
                {
                    if (IsWiredKeyboardEndpoint(d)) return d.ProductString.Trim();
                }
            }
            return "GS3108T-PRO";
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
                List<HidDeviceDescriptor> descriptors = HidTransport.Enumerate(0x05AC, 0x024F);
                HidDeviceDescriptor[] all = descriptors.ToArray();
                HidDeviceDescriptor endpoint = FindBatteryEndpoint(all);
                string transport = ResolveTransport(all);
                if (transport.Length == 0) { battery = -1; sampled = DateTime.MinValue; return null; }

                // 有线模式下射频已关闭，向接收器发查询必然无应答；不再做无意义的写入与等待。
                bool wired = transport == TransportWired;
                if (!wired && endpoint != null && (DateTime.UtcNow - lastAttempt).TotalSeconds >= 15)
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

                // 有线模式不把旧 2.4G 样本伪装成当前电量：百分比交给 UI 隐藏，采样时间保留给“上次读数”脚注。
                int reportedBattery = wired ? -1 : battery;
                DateTime reportedSampled = wired ? sampled : (endpoint != null ? sampled : DateTime.MinValue);

                return new MouseBatteryInfo {
                    DeviceId = "HID:05AC:024F", HardwareFingerprint = "05AC:024F", Brand = "GANSS",
                    Category = DeviceCategory.Keyboard,
                    DeviceName = transport == TransportWired ? FindWiredDeviceName(all) : endpoint.ProductString,
                    Transport = transport,
                    IsConnected = true, IsDonglePresent = endpoint != null,
                    BatteryPercent = reportedBattery, LastUpdated = reportedSampled,
                    IsCharging = false // Official battery query supplies percentage only; no invented charge flag.
                };
            }
        }
    }
}
