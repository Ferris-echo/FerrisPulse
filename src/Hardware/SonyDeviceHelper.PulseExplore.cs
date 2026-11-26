// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
using System;
using System.Collections.Generic;

namespace RazerBatteryTray
{
    public static partial class SonyDeviceHelper
    {
        private static readonly Dictionary<string, MouseBatteryInfo> pulseCache = new Dictionary<string, MouseBatteryInfo>();
        private static DateTime lastPulseQuery = DateTime.MinValue;

        public static SonyPulseExploreState ParsePulseExploreReport(byte[] report)
        {
            if (report == null || report.Length != 64 || report[0] != 0xB0 || report[1] != 0x50 || report[2] != 1 ||
                report[29] != 0x11 || report[30] != 0) return null;
            bool linked = (report[39] & 1) != 0;
            return new SonyPulseExploreState
            {
                LeftBatteryPercent = ((report[55] & 15) * 100 + 7) / 15,
                RightBatteryPercent = ((report[55] >> 4) * 100 + 7) / 15,
                IsLeftOnline = linked && (report[56] & 0x10) != 0,
                IsRightOnline = linked && (report[56] & 0x20) != 0,
                IsConnected = linked,
                LeftCharging = null,
                RightCharging = null,
                PairingIdentity = GetPulsePairingIdentity(report)
            };
        }

        public static SonyPulseExploreState ParsePulseExploreUsbReport(byte[] report)
        {
            if (report == null || report.Length != 64 || report[0] != 0xB0 || report[1] != 0x50 || report[2] != 1 ||
                report[29] != 0x11 || report[30] != 1) return null;
            bool left = (report[56] & 0x10) != 0;
            bool right = (report[56] & 0x20) != 0;
            int leftPercent = ((report[55] & 15) * 100 + 7) / 15;
            int rightPercent = ((report[55] >> 4) * 100 + 7) / 15;
            return new SonyPulseExploreState
            {
                IsUsbCaseConnected = true,
                IsLeftDocked = left, IsRightDocked = right,
                LeftBatteryPercent = left ? leftPercent : -1, RightBatteryPercent = right ? rightPercent : -1,
                LeftCharging = left ? (bool?)(leftPercent < 100) : null,
                RightCharging = right ? (bool?)(rightPercent < 100) : null,
                PairingIdentity = GetPulsePairingIdentity(report)
            };
        }

        public static SonyPulseExploreState MergePulseExploreUsbState(SonyPulseExploreState wireless, SonyPulseExploreState usb)
        {
            if (wireless == null || usb == null || !usb.IsUsbCaseConnected || string.IsNullOrEmpty(wireless.PairingIdentity) ||
                !string.Equals(wireless.PairingIdentity, usb.PairingIdentity, StringComparison.Ordinal)) return wireless;
            return new SonyPulseExploreState
            {
                PairingIdentity = wireless.PairingIdentity,
                IsConnected = wireless.IsConnected,
                IsLeftOnline = wireless.IsLeftOnline && !usb.IsLeftDocked,
                IsRightOnline = wireless.IsRightOnline && !usb.IsRightDocked,
                LeftBatteryPercent = usb.IsLeftDocked ? usb.LeftBatteryPercent : wireless.LeftBatteryPercent,
                RightBatteryPercent = usb.IsRightDocked ? usb.RightBatteryPercent : wireless.RightBatteryPercent,
                IsUsbCaseConnected = true, IsLeftDocked = usb.IsLeftDocked, IsRightDocked = usb.IsRightDocked,
                LeftCharging = usb.IsLeftDocked ? usb.LeftCharging : null,
                RightCharging = usb.IsRightDocked ? usb.RightCharging : null
            };
        }

        private static string GetPulsePairingIdentity(byte[] report)
        {
            bool nonzero = false;
            for (int index = 3; index < 17; index++) if (report[index] != 0) nonzero = true;
            return nonzero ? BitConverter.ToString(report, 3, 14) : null;
        }

        public static MouseBatteryInfo CreatePulseExploreInfo(SonyPulseExploreState state)
        {
            int percent = -1;
            if (state != null)
            {
                if (state.IsLeftOnline) percent = state.LeftBatteryPercent;
                if (state.IsRightOnline) percent = percent < 0 ? state.RightBatteryPercent : Math.Min(percent, state.RightBatteryPercent);
                if (percent < 0 && state.IsUsbCaseConnected)
                {
                    if (state.IsLeftDocked) percent = state.LeftBatteryPercent;
                    if (state.IsRightDocked) percent = percent < 0 ? state.RightBatteryPercent : Math.Min(percent, state.RightBatteryPercent);
                }
            }
            string details = state == null ? "耳塞状态读取失败，请唤醒耳塞后重试。" :
                FormatPulseEar("左耳", state.LeftBatteryPercent, state.IsLeftOnline, state.IsLeftDocked) + "；" +
                FormatPulseEar("右耳", state.RightBatteryPercent, state.IsRightOnline, state.IsRightDocked) + "。";
            bool connected = state != null && (state.IsConnected || state.IsUsbCaseConnected);
            bool charging = state != null && !state.IsLeftOnline && !state.IsRightOnline &&
                (state.LeftCharging == true || state.RightCharging == true);
            return new MouseBatteryInfo
            {
                Brand = "Sony", Category = DeviceCategory.Earbuds, DeviceName = "PULSE Explore",
                Transport = state != null && state.IsUsbCaseConnected ? "USB 充电盒" : "PS Link 2.4G",
                IsDonglePresent = state == null || !state.IsUsbCaseConnected,
                IsConnected = connected, IsSleeping = !connected,
                BatteryPercent = percent, ExactBatteryPercent = percent,
                PulseExploreState = state,
                IsCharging = charging, LastUpdated = DateTime.Now,
                CustomTooltip = details + " 电量来自0..15档位换算，非1%精度；" +
                    (state != null && state.IsUsbCaseConnected ? "USB可确认在盒充电/已满；离盒耳塞充电状态未知；" : "充电状态未知；") + "充电盒自身电量未知。"
            };
        }

        private static string GetPulseEarStatus(bool online, bool docked, int percent)
        {
            if (docked) return percent == 100 ? "USB在盒，已满" : "USB在盒充电";
            if (percent < 0) return "USB未提供离盒耳塞电量";
            return online ? "在线" : "入盒/离线，上次上报值";
        }

        private static string FormatPulseEar(string side, int percent, bool online, bool docked)
        {
            return side + "：" + (percent < 0 ? "未知" : "约" + percent + "%") + "（" + GetPulseEarStatus(online, docked, percent) + "）";
        }

        public static List<MouseBatteryInfo> QueryAllPulseExploreDevices()
        {
            lock (hidLock)
            {
                HashSet<string> present = new HashSet<string>();
                bool due = (DateTime.UtcNow - lastPulseQuery).TotalSeconds >= 5;
                foreach (HidDeviceDescriptor device in HidTransport.Enumerate(0x054C, 0))
                {
                    bool usb = device.ProductId == 0x0ED0 && device.UsagePage == 0xFF13 && device.Usage == 1 &&
                        device.OutputReportLength == 62 && string.Equals(device.ProductString, "PULSE Explore", StringComparison.OrdinalIgnoreCase);
                    bool wireless = device.ProductId == 0x0ECC && device.UsagePage == 0xFF01 && device.Usage == 0x20 && device.OutputReportLength == 0;
                    if ((!usb && !wireless) || device.FeatureReportLength != 64 || device.InputReportLength != 64) continue;
                    present.Add(device.DevicePath);
                    MouseBatteryInfo info;
                    pulseCache.TryGetValue(device.DevicePath, out info);
                    if (due || info == null)
                    {
                        SonyPulseExploreState state = null;
                        byte[] report = null;
                        bool read = false;
                        IntPtr handle = HidTransport.OpenFeature(device.DevicePath);
                        try
                        {
                            read = HidTransport.ReadFeatureReport(handle, 0xB0, 64, out report);
                            if (read) state = usb ? ParsePulseExploreUsbReport(report) : ParsePulseExploreReport(report);
                        }
                        finally { HidTransport.Close(handle); }
                        if (state != null) info = CreatePulseExploreInfo(state);
                        else if (read && (report[29] != 0 || report[30] != 0))
                        {
                            pulseCache.Remove(device.DevicePath);
                            continue;
                        }
                        else if (info != null) info = CreatePulseExploreInfo(null);
                        else continue;
                        info.DeviceId = "Sony:" + device.InstanceId;
                        info.HardwareId = "HID:054C:" + device.ProductId.ToString("X4");
                        info.HardwareFingerprint = info.HardwareId;
                        info.Transport = usb ? "USB 充电盒" : "PS Link 2.4G";
                        info.IsDonglePresent = wireless;
                        pulseCache[device.DevicePath] = info;
                    }
                }
                List<string> removed = new List<string>();
                foreach (string path in pulseCache.Keys) if (!present.Contains(path)) removed.Add(path);
                foreach (string path in removed) pulseCache.Remove(path);
                if (due) lastPulseQuery = DateTime.UtcNow;
                List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
                HashSet<string> mergedUsb = new HashSet<string>();
                foreach (KeyValuePair<string, MouseBatteryInfo> entry in pulseCache)
                {
                    MouseBatteryInfo device = entry.Value;
                    if (!device.IsDonglePresent) continue;
                    SonyPulseExploreState combined = device.PulseExploreState;
                    foreach (KeyValuePair<string, MouseBatteryInfo> candidate in pulseCache)
                    {
                        if (candidate.Value.IsDonglePresent || candidate.Value.PulseExploreState == null) continue;
                        SonyPulseExploreState merged = MergePulseExploreUsbState(combined, candidate.Value.PulseExploreState);
                        if (object.ReferenceEquals(merged, combined)) continue;
                        combined = merged;
                        mergedUsb.Add(candidate.Key);
                        break;
                    }
                    MouseBatteryInfo display = CreatePulseExploreInfo(combined);
                    CopyPulseIdentity(device, display);
                    if (combined != null && combined.IsUsbCaseConnected) display.Transport = "PS Link 2.4G / USB充电盒";
                    result.Add(display);
                }
                foreach (KeyValuePair<string, MouseBatteryInfo> entry in pulseCache)
                    if (!entry.Value.IsDonglePresent && !mergedUsb.Contains(entry.Key)) result.Add(entry.Value);
                return result;
            }
        }

        private static void CopyPulseIdentity(MouseBatteryInfo source, MouseBatteryInfo target)
        {
            target.DeviceId = source.DeviceId;
            target.HardwareId = source.HardwareId;
            target.HardwareFingerprint = source.HardwareFingerprint;
            target.Transport = source.Transport;
            target.IsDonglePresent = source.IsDonglePresent;
            target.LastUpdated = source.LastUpdated;
        }
    }
}