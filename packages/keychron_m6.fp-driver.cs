// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// Standalone Keychron M6 package. C# 5 / .NET 4, no host-specific helper calls.
// Protocol: Keychron Launcher main.281a047df556abfc.js. Thanks to GaiusC
// for real identity/battery captures (2026-11-08) and USB/RF charging verification (2026-11-09).
// TX: B5 02 01 ... (RF identity), B5 02 00 ... (USB identity), B3 06 00 ... (status).
// RX: B6 02 04 00 34 34 60 D0 ...; B4 06 ... byte[20]=56/D6 (86%, discharge/charge).
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using Microsoft.Win32;
using RazerBatteryTray;

namespace FerrisPulse.Packages.KeychronM6
{
    public sealed class KeychronM6Driver : HardwareDriverBase
    {
        public override string DriverId { get { return Protocol.DriverKey; } }
        public override string Brand { get { return "Keychron"; } }
        public override bool IsExternalPackage { get { return true; } }
        public override int Priority { get { return 300; } }
        public string[] ClaimedDevicePaths { get { return Protocol.ClaimedDevicePaths; } }
        public override bool MatchesVidPid(int vid, int pid)
        {
            return vid == 0x3434 && (pid == 0xD026 || pid == 0xD060);
        }
        public override bool CanHandle(MouseBatteryInfo dev)
        {
            return Protocol.IsVerifiedDevice(dev);
        }
        public override void PopulateCapabilities(MouseBatteryInfo dev)
        {
            if (dev == null) return;
            bool verified = CanHandle(dev);
            if (verified) dev.DriverId = DriverId;
            dev.Capabilities.SupportsBattery = verified;
            dev.Capabilities.SupportsDpi = false;
            dev.Capabilities.SupportsDpiStages = false;
            dev.Capabilities.SupportsPollingRate = false;
            dev.Capabilities.SupportsXyIndependence = false;
            dev.Capabilities.SupportsHardwareDpiMonitor = false;
        }
        public override List<MouseBatteryInfo> QueryDevices()
        {
            List<MouseBatteryInfo> devices = Protocol.QueryDevices();
            foreach (MouseBatteryInfo dev in devices) PopulateCapabilities(dev);
            return devices;
        }
    }

    internal static class Protocol
    {
        internal const string DriverKey = "External:KeychronM6";
        private const string ReceiverKey = "Keychron:3434:D026:MS";
        private const string WiredKey = "Keychron:3434:D060:MS";
        private const string ModelName = "Keychron M6";
        private const string LinksKey = @"Software\FerrisPulse\Keychron\WiredReceivers";
        private static readonly object hidLock = new object();
        private static readonly Dictionary<string, string> verifiedPaths = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> claims = new List<string>();
        internal static string[] ClaimedDevicePaths { get { lock (hidLock) { return claims.ToArray(); } } }
        private static readonly Dictionary<string, MouseBatteryInfo> lastReadings =
            new Dictionary<string, MouseBatteryInfo>(StringComparer.OrdinalIgnoreCase);
        internal static bool IsControlEndpoint(HidDeviceDescriptor d)
        {
            return d != null && d.VendorId == 0x3434 && (d.ProductId == 0xD026 || d.ProductId == 0xD060) &&
                d.UsagePage == 0xFFC1 && d.Usage == 1 && d.InputReportLength == 64 &&
                d.OutputReportLength == 64 && d.FeatureReportLength == 0;
        }
        internal static bool IsM6Identity(byte[] report)
        {
            return report != null && report.Length == 64 && report[0] == 0xB6 && report[1] == 2 &&
                report[4] == 0x34 && report[5] == 0x34 && report[6] == 0x60 && report[7] == 0xD0;
        }
        private static string DeviceKey(HidDeviceDescriptor endpoint)
        {
            return endpoint.ProductId == 0xD026 ? ReceiverKey : WiredKey;
        }
        internal static bool AcceptIdentity(HidDeviceDescriptor endpoint, byte[] identity)
        {
            if (!IsControlEndpoint(endpoint) || string.IsNullOrEmpty(endpoint.DevicePath)) return false;
            lock (hidLock)
            {
                if (!IsM6Identity(identity)) { verifiedPaths.Remove(endpoint.DevicePath); return false; }
                verifiedPaths[endpoint.DevicePath] = DeviceKey(endpoint);
                return true;
            }
        }
        internal static bool IsVerifiedDevice(MouseBatteryInfo dev)
        {
            if (dev == null || dev.Category != DeviceCategory.Mouse || string.IsNullOrEmpty(dev.HardwareId)) return false;
            lock (hidLock)
            {
                string key;
                return verifiedPaths.TryGetValue(dev.HardwareId, out key) && string.Equals(key, dev.DeviceId, StringComparison.OrdinalIgnoreCase);
            }
        }
        internal static bool TryParseBattery(byte[] report, out int percent, out bool charging)
        {
            percent = -1; charging = false;
            if (report == null || report.Length != 64 || report[0] != 0xB4 || report[1] != 6) return false;
            int value = report[20] & 0x7F;
            if (value > 100) return false;
            percent = value; charging = (report[20] & 0x80) != 0;
            return true;
        }
        private static byte[] Query(IntPtr handle, byte report, byte command, byte target)
        {
            byte[] tx = new byte[64]; tx[0] = report; tx[1] = command; tx[2] = target;
            if (!HidTransport.WriteReport(handle, tx, 64, 500)) return null;
            Stopwatch timer = Stopwatch.StartNew(); int remaining;
            while ((remaining = 500 - (int)timer.ElapsedMilliseconds) > 0)
            {
                byte[] rx;
                if (!HidTransport.ReadReportExact(handle, 64, remaining, out rx)) return null;
                if (rx[0] == report + 1 && rx[1] == command) return rx;
            }
            return null;
        }
        private static string ReceiverUnit(string path)
        {
            return string.IsNullOrEmpty(path) ? null : ReceiverKey + ":Path:" + path.ToUpperInvariant();
        }
        // New mappings may use Keychron:ReceiverPath:<exact RF control endpoint path>.
        // The old ReceiverKey-only mapping is accepted only with one verified RF endpoint.
        internal static string ResolveReceiverLink(string link)
        {
            lock (hidLock)
            {
                const string prefix = "Keychron:ReceiverPath:";
                if (link != null && link.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    string path = link.Substring(prefix.Length), key;
                    return verifiedPaths.TryGetValue(path, out key) && key == ReceiverKey ? ReceiverUnit(path) : null;
                }
                if (link != ReceiverKey) return null;
                string only = null;
                foreach (KeyValuePair<string, string> entry in verifiedPaths)
                {
                    if (entry.Value != ReceiverKey) continue;
                    if (only != null) return null;
                    only = entry.Key;
                }
                return ReceiverUnit(only);
            }
        }
        private static string GetLinkedReceiver(string path)
        {
            if (string.IsNullOrEmpty(path)) return null;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(LinksKey))
                {
                    string link = key == null ? null : key.GetValue(path) as string;
                    return ResolveReceiverLink(link);
                }
            }
            catch { return null; }
        }
        internal static MouseBatteryInfo CreateState(HidDeviceDescriptor endpoint, int percent,
            bool charging, bool sleeping, DateTime updated)
        {
            bool wireless = endpoint.ProductId == 0xD026;
            MouseBatteryInfo dev = new MouseBatteryInfo();
            dev.DeviceId = wireless ? ReceiverKey : WiredKey;
            dev.HardwareFingerprint = "HID:3434:" + endpoint.ProductId.ToString("X4") + ":MS";
            dev.HardwareId = endpoint.DevicePath;
            // Optional local-host enhancement; official hosts can still load this package.
            PropertyInfo unitProperty = typeof(MouseBatteryInfo).GetProperty("PhysicalUnitId");
            if (unitProperty != null && unitProperty.CanWrite)
                unitProperty.SetValue(dev, wireless ? ReceiverUnit(endpoint.DevicePath) : GetLinkedReceiver(endpoint.DevicePath), null);
            dev.DriverId = DriverKey;
            dev.Brand = "Keychron"; dev.DeviceName = ModelName; dev.Category = DeviceCategory.Mouse;
            dev.Transport = wireless ? "2.4G 无线" : "USB 有线";
            dev.BatteryPercent = percent; dev.ExactBatteryPercent = percent;
            dev.IsConnected = true; dev.IsDonglePresent = wireless; dev.IsSleeping = sleeping;
            dev.IsCharging = !sleeping && charging; dev.IsChargingUnknown = sleeping;
            dev.LastUpdated = updated; dev.Capabilities.SupportsBattery = true;
            return dev;
        }
        private static string PhysicalUnit(MouseBatteryInfo state)
        {
            return state.IsDonglePresent ? ReceiverUnit(state.HardwareId) : GetLinkedReceiver(state.HardwareId);
        }
        private static void MergeTransportState(List<MouseBatteryInfo> devices, MouseBatteryInfo state)
        {
            string unit = PhysicalUnit(state);
            int existing = devices.FindIndex(delegate(MouseBatteryInfo d)
            {
                return !string.IsNullOrEmpty(unit) && string.Equals(PhysicalUnit(d), unit, StringComparison.OrdinalIgnoreCase);
            });
            if (existing < 0) devices.Add(state);
            else if (!state.IsSleeping && (devices[existing].IsSleeping || !state.IsDonglePresent)) devices[existing] = state;
        }
        private static MouseBatteryInfo CachedState(HidDeviceDescriptor endpoint)
        {
            MouseBatteryInfo previous;
            if (lastReadings.TryGetValue(endpoint.DevicePath, out previous))
                return CreateState(endpoint, previous.BatteryPercent, false, true, previous.LastUpdated);
            string cacheKey = (endpoint.ProductId == 0xD026 ? ReceiverKey : WiredKey) + ":" + endpoint.DevicePath;
            string name; DateTime updated; int percent; bool charging;
            if (DriverStateStore.TryLoadBatteryState(cacheKey, out percent, out charging, out name, out updated) && name == ModelName)
                return CreateState(endpoint, percent, false, true, updated);
            return null;
        }
        internal static List<MouseBatteryInfo> QueryDevices()
        {
            List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
            lock (hidLock)
            {
                claims.Clear(); verifiedPaths.Clear();
                List<MouseBatteryInfo> readings = new List<MouseBatteryInfo>();
                foreach (HidDeviceDescriptor endpoint in HidTransport.Enumerate(0x3434, 0))
                {
                    if (!IsControlEndpoint(endpoint)) continue;
                    string cacheKey = (endpoint.ProductId == 0xD026 ? ReceiverKey : WiredKey) + ":" + endpoint.DevicePath;
                    IntPtr handle = HidTransport.Open(endpoint.DevicePath);
                    if (!HidTransport.IsValidHandle(handle)) continue;
                    try
                    {
                        byte[] identity = Query(handle, 0xB5, 2, endpoint.ProductId == 0xD026 ? (byte)1 : (byte)0);
                        bool verified = identity != null && AcceptIdentity(endpoint, identity);
                        if (identity != null && !verified)
                        {
                            lastReadings.Remove(endpoint.DevicePath); DriverStateStore.ClearDeviceState(cacheKey); continue;
                        }
                        int percent; bool charging; MouseBatteryInfo state = null;
                        if (verified && TryParseBattery(Query(handle, 0xB3, 6, 0), out percent, out charging))
                        {
                            state = CreateState(endpoint, percent, charging, false, DateTime.Now);
                            lastReadings[endpoint.DevicePath] = state;
                            DriverStateStore.SaveBatteryState(cacheKey, percent, charging, ModelName);
                        }
                        else state = CachedState(endpoint);
                        // Keep claims before RF/USB merging, including verified same-path sleeping state.
                        if (verified || state != null)
                        {
                            verifiedPaths[endpoint.DevicePath] = DeviceKey(endpoint);
                            claims.Add(endpoint.DevicePath);
                        }
                        if (state != null) readings.Add(state);
                    }
                    finally { HidTransport.Close(handle); }
                }
                // Resolve legacy associations only after every current verified receiver is known.
                PropertyInfo unitProperty = typeof(MouseBatteryInfo).GetProperty("PhysicalUnitId");
                foreach (MouseBatteryInfo state in readings)
                {
                    if (unitProperty != null && unitProperty.CanWrite) unitProperty.SetValue(state, PhysicalUnit(state), null);
                    MergeTransportState(result, state);
                }
            }
            return result;
        }
    }
}
