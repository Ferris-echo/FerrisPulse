// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// 启明星2 (Nova 2 Lite) 只读设备信息、电量及充电哨兵解析。
// C#5 / .NET4 / UTF-8 BOM；仅 Win32 HID，不依赖官方 DLL。
using System;
using System.Collections.Generic;
namespace RazerBatteryTray
{
    public static class GameSirDeviceHelper
    {
        private static readonly object gate = new object();
        private static readonly Dictionary<string, MouseBatteryInfo> states = new Dictionary<string, MouseBatteryInfo>();
        private static readonly Dictionary<string, DateTime> attempts = new Dictionary<string, DateTime>();
        private static readonly Dictionary<string, string> pathIds = new Dictionary<string, string>();
        public static string LastResponseHex = "";

        public static byte[] BuildInfoQuery()
        {
            byte[] command = new byte[65]; command[1] = 1; command[2] = 1; return command;
        }
        public static bool IsSupportedEndpoint(HidDeviceDescriptor d)
        {
            return d != null && d.VendorId == 0x3537 && (d.ProductId == 0x1098 || d.ProductId == 0x100F) &&
                d.UsagePage == 0xFF7A && d.Usage == 1 && d.InputReportLength == 65 && d.OutputReportLength == 65;
        }
        public static MouseBatteryInfo CreateDeviceInfo(byte[] report, int pid, MouseBatteryInfo previous)
        {
            if (report == null || report.Length != 65 || report[0] != 0 || report[1] != 1 ||
                (pid != 0x100F && pid != 0x1098)) return null;
            int raw = report[19];
            if (raw > 100 && raw != 255) return null;
            bool hasIdentity = false;
            for (int i = 2; i < 8; i++) if (report[i] != 0) hasIdentity = true;
            if (!hasIdentity) return null;
            string identity = "GameSir:Nova2Lite:" + BitConverter.ToString(report, 2, 6).Replace("-", "");
            bool same = previous != null && previous.DeviceId == identity;
            bool charging = raw == 255;
            return new MouseBatteryInfo {
                DeviceId = identity, HardwareFingerprint = "HID:3537:" + pid.ToString("X4"),
                Brand = "GameSir", DeviceName = "盖世小鸡 启明星2", Category = DeviceCategory.Gamepad,
                IsConnected = true, IsDonglePresent = pid == 0x1098, Transport = pid == 0x100F ? "USB 有线" : "2.4G 无线",
                BatteryPercent = charging ? (same ? previous.BatteryPercent : -1) : raw,
                IsCharging = charging, LastUpdated = charging ? (same ? previous.LastUpdated : DateTime.MinValue) : DateTime.Now,
                CustomTooltip = charging ? "正在充电；固件当前不提供实际百分比" : "手柄实时电量"
            };
        }
        // 手柄 MAC 级的唯一身份：隐藏该设备本身足矣，不能被共享的 HID:3537:xxxx 指纹连带隐藏。
        public static bool HasPerUnitIdentity(string deviceId)
        {
            return deviceId != null && deviceId.StartsWith("GameSir:Nova2Lite:", StringComparison.OrdinalIgnoreCase);
        }
        public static bool MatchesConfig(CustomDeviceConfig config)
        {
            if (config == null) return false;
            string ids = (config.DeviceId ?? "") + " " + (config.HardwareFingerprint ?? "");
            return ids.IndexOf("3537:1098", StringComparison.OrdinalIgnoreCase) >= 0 ||
                ids.IndexOf("3537:100F", StringComparison.OrdinalIgnoreCase) >= 0;
        }
        public static MouseBatteryInfo FindConfiguredDevice(CustomDeviceConfig config, List<MouseBatteryInfo> devices)
        {
            if (config == null || devices == null) return null;
            foreach (MouseBatteryInfo device in devices)
                if (device.Brand == "GameSir" && device.Category == DeviceCategory.Gamepad &&
                    string.Equals(device.DeviceId, config.DeviceId, StringComparison.OrdinalIgnoreCase)) return device;
            // A known MAC must never fall back to a different controller of the same model.
            if ((config.DeviceId ?? "").StartsWith("GameSir:Nova2Lite:", StringComparison.OrdinalIgnoreCase)) return null;
            string ids = (config.DeviceId ?? "") + " " + (config.HardwareFingerprint ?? "");
            MouseBatteryInfo match = null;
            foreach (MouseBatteryInfo device in devices)
            {
                if (device.Brand != "GameSir" || device.Category != DeviceCategory.Gamepad ||
                    string.IsNullOrEmpty(device.HardwareFingerprint)) continue;
                string fingerprint = device.HardwareFingerprint;
                if (fingerprint.StartsWith("HID:", StringComparison.OrdinalIgnoreCase)) fingerprint = fingerprint.Substring(4);
                if (ids.IndexOf(fingerprint, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (match != null) return null;
                match = device;
            }
            return match;
        }
        public static List<MouseBatteryInfo> QueryDevices()
        {
            lock (gate)
            {
                List<HidDeviceDescriptor> endpoints = HidTransport.Enumerate(0x3537, 0);
                // Wired first. Do not replace a successful wired snapshot with a receiver copy.
                endpoints.Sort(delegate(HidDeviceDescriptor a, HidDeviceDescriptor b) {
                    return (a.ProductId == 0x100F ? 0 : 1).CompareTo(b.ProductId == 0x100F ? 0 : 1);
                });
                Dictionary<string, MouseBatteryInfo> found = new Dictionary<string, MouseBatteryInfo>();
                foreach (HidDeviceDescriptor endpoint in endpoints)
                {
                    if (!IsSupportedEndpoint(endpoint)) continue;
                    DateTime last;
                    string id;
                    if (attempts.TryGetValue(endpoint.DevicePath, out last) && (DateTime.UtcNow - last).TotalSeconds < 5)
                    {
                        MouseBatteryInfo cached;
                        if (pathIds.TryGetValue(endpoint.DevicePath, out id) && states.TryGetValue(id, out cached) &&
                            cached.HardwareFingerprint == "HID:3537:" + endpoint.ProductId.ToString("X4") && !found.ContainsKey(id)) found.Add(id, cached);
                        continue;
                    }
                    attempts[endpoint.DevicePath] = DateTime.UtcNow;
                    pathIds.Remove(endpoint.DevicePath);
                    IntPtr handle = HidTransport.Open(endpoint.DevicePath);
                    try
                    {
                        if (!HidTransport.IsValidHandle(handle) || !RapooNativeIo.Write(handle, BuildInfoQuery(), 300)) continue;
                        DateTime deadline = DateTime.UtcNow.AddMilliseconds(500);
                        int reports = 0;
                        while (DateTime.UtcNow < deadline && reports++ < 128)
                        {
                            byte[] response;
                            if (!RapooNativeIo.Read(handle, 65, 80, out response)) continue;
                            MouseBatteryInfo parsed = CreateDeviceInfo(response, endpoint.ProductId, null);
                            if (parsed == null) continue; // Ignore F4 button reports and unrelated replies.
                            MouseBatteryInfo previous;
                            states.TryGetValue(parsed.DeviceId, out previous);
                            parsed = CreateDeviceInfo(response, endpoint.ProductId, previous);
                            LastResponseHex = BitConverter.ToString(response);
                            pathIds[endpoint.DevicePath] = parsed.DeviceId;
                            if (!found.ContainsKey(parsed.DeviceId))
                            { states[parsed.DeviceId] = parsed; found.Add(parsed.DeviceId, parsed); }
                            break;
                        }
                    }
                    finally { HidTransport.Close(handle); }
                }
                // Keep known controller rows offline without pretending a silent receiver is a live pad.
                List<MouseBatteryInfo> result = new List<MouseBatteryInfo>(found.Values);
                foreach (MouseBatteryInfo known in states.Values)
                    if (!found.ContainsKey(known.DeviceId)) result.Add(new MouseBatteryInfo {
                        DeviceId = known.DeviceId, HardwareFingerprint = known.HardwareFingerprint,
                        Brand = "GameSir", DeviceName = known.DeviceName, Category = DeviceCategory.Gamepad,
                        Transport = known.Transport, BatteryPercent = known.BatteryPercent, LastUpdated = known.LastUpdated,
                        IsConnected = false, IsCharging = false
                    });
                return result;
            }
        }
    }
}
