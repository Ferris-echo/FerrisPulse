// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// Standalone PRO X SUPERLIGHT 2 / MR0097 package, C# 5 / .NET 4.
// Thanks to GaiusC for verified RF/USB captures and onboard-stage replay (2026-11-08/09).
// Exact model 40A9:C09B:0000; RF 046D:C54D, USB 046D:C09B, FF00/2, 20/20/0.
// Battery RX 11 01 06 19 59 08 00 (89%); USB 11 FF 06 19 3C 04 01 (60%, charging).
// DPI RX 11 01 09 59 00 06 40 03 20 06 40 03 20 (1600/1600).
// 1004/2202/8061 reads only. 8100 C0 selects an existing verified wireless stage;
// arbitrary DPI/rate writes and profile-table edits are intentionally unsupported.
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Reflection;
using System.Text.RegularExpressions;
using System.Threading;
using RazerBatteryTray;

namespace FerrisPulse.Packages.LogitechMr0097
{
    public sealed class Mr0097Driver : HardwareDriverBase
    {
        public override string DriverId { get { return Protocol.DriverKey; } }
        public override string Brand { get { return "Logitech"; } }
        public override int Priority { get { return 300; } }
        public override bool IsExternalPackage { get { return true; } }
        public string[] ClaimedDevicePaths { get { return Protocol.ClaimedDevicePaths; } }
        public override bool MatchesVidPid(int vid, int pid)
        {
            return vid == 0x046D && (pid == 0xC54D || pid == 0xC09B);
        }
        public override bool CanHandle(MouseBatteryInfo dev)
        {
            return Protocol.IsVerifiedDevice(dev);
        }
        public override void PopulateCapabilities(MouseBatteryInfo dev)
        {
            if (dev == null) return;
            if (!CanHandle(dev))
            {
                dev.Capabilities.SupportsBattery = false;
                dev.Capabilities.SupportsDpi = false;
                dev.Capabilities.SupportsDpiStages = false;
                dev.Capabilities.SupportsPollingRate = false;
                dev.Capabilities.SupportsXyIndependence = false;
                dev.Capabilities.SupportsHardwareDpiMonitor = false;
                return;
            }
            dev.DriverId = DriverId;
            dev.Capabilities.SupportsBattery = true;
            dev.Capabilities.SupportsDpi = false;
            dev.Capabilities.SupportsPollingRate = false;
            dev.Capabilities.SupportsXyIndependence = false;
            dev.Capabilities.SupportsHardwareDpiMonitor = true;
            dev.Capabilities.SupportsDpiStages = dev.IsDonglePresent && !dev.IsSleeping && dev.DpiStageCount == 5;
            dev.Capabilities.MinDpi = 100; dev.Capabilities.MaxDpi = 44000; dev.Capabilities.DpiStep = 50;
            dev.Capabilities.SensorDescription = "HERO 2 · 已验证只读性能 / 现有板载档位选择";
            Protocol.SetOptional(dev, "SupportsDpiWrite", false);
            dev.SupportsPollingRateWrite = false;
        }
        public override List<MouseBatteryInfo> QueryDevices()
        {
            List<MouseBatteryInfo> list = Protocol.QueryDevices();
            foreach (MouseBatteryInfo dev in list) PopulateCapabilities(dev);
            return list;
        }
        public override bool FastQueryDpi(MouseBatteryInfo dev, out int x, out int y, out int stage, out int count)
        {
            x = y = stage = count = 0;
            return CanHandle(dev) && Protocol.FastQueryDpi(dev, out x, out y, out stage, out count);
        }
        public override bool SetDpi(MouseBatteryInfo dev, int x, int y, int stage)
        {
            return CanHandle(dev) && Protocol.SelectOnboardStage(dev, stage, x, y > 0 ? y : x);
        }
        // All remaining write entry points inherit the base class's unconditional false.
    }

    internal static class Protocol
    {
        internal const string DriverKey = "External:LogitechMR0097";
        internal const string ModelName = "Logitech G PRO X SUPERLIGHT 2";
        private const int CommandReplyTimeoutMs = 500;
        // Owner capture 2026-11-09: cold ROOT 0003 response took 1059 ms; warm retry took 7 ms.
        // Give only identity discovery a longer bounded receive window; never resend writes.
        private const int IdentityRootReplyTimeoutMs = 2000;
        private static readonly object hidLock = new object();
        private static readonly Dictionary<string, Endpoint> active = new Dictionary<string, Endpoint>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, MouseBatteryInfo> last = new Dictionary<string, MouseBatteryInfo>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, HashSet<string>> verifiedPaths = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly List<string> claims = new List<string>();
        internal static string[] ClaimedDevicePaths { get { lock (hidLock) { return claims.ToArray(); } } }
        internal sealed class Profile
        {
            public byte Feature;
            public int Stage;
            public int[] X;
            public int[] Y;
        }
        private sealed class Endpoint
        {
            internal HidDeviceDescriptor Descriptor;
            internal byte Slot;
            internal byte DpiFeature;
            internal Profile Profile;
        }
        internal static void SetOptional(MouseBatteryInfo dev, string name, object value)
        {
            PropertyInfo property = typeof(MouseBatteryInfo).GetProperty(name);
            if (property != null && property.CanWrite) property.SetValue(dev, value, null);
        }
        internal static bool IsControlEndpoint(HidDeviceDescriptor d)
        {
            return d != null && d.VendorId == 0x046D && (d.ProductId == 0xC54D || d.ProductId == 0xC09B) &&
                d.UsagePage == 0xFF00 && d.Usage == 2 && d.InputReportLength == 20 &&
                d.OutputReportLength == 20 && d.FeatureReportLength == 0;
        }
        // Exact Windows HID interface ancestry. Collection number and its instance suffix vary;
        // VID/PID/interface and the complete parent-instance token must remain identical.
        internal static bool SameProtocolInterface(string a, string b)
        {
            string pattern = @"^\\\\\?\\hid#(vid_[0-9a-f]{4}&pid_[0-9a-f]{4}&mi_[0-9a-f]{2})&col[0-9a-f]{2}#([^#]+)&[0-9a-f]{4}#\{4d1e55b2-f16f-11cf-88cb-001111000030\}$";
            Match ma = Regex.Match(a ?? "", pattern, RegexOptions.IgnoreCase);
            Match mb = Regex.Match(b ?? "", pattern, RegexOptions.IgnoreCase);
            return ma.Success && mb.Success && string.Equals(ma.Groups[1].Value, mb.Groups[1].Value, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(ma.Groups[2].Value, mb.Groups[2].Value, StringComparison.OrdinalIgnoreCase);
        }
        private static void ClaimInterface(HidDeviceDescriptor verified, List<HidDeviceDescriptor> all)
        {
            if (!claims.Contains(verified.DevicePath)) claims.Add(verified.DevicePath);
            foreach (HidDeviceDescriptor candidate in all)
            {
                if (candidate.VendorId == verified.VendorId && candidate.ProductId == verified.ProductId &&
                    candidate.UsagePage == 0xFF00 && candidate.FeatureReportLength == 0 &&
                    ((candidate.Usage == 1 && candidate.InputReportLength == 7 && candidate.OutputReportLength == 7) || IsControlEndpoint(candidate)) &&
                    SameProtocolInterface(verified.DevicePath, candidate.DevicePath) && !claims.Contains(candidate.DevicePath))
                    claims.Add(candidate.DevicePath);
            }
        }
        private static byte[] Command(IntPtr handle, byte slot, byte feature, byte function, params byte[] args)
        {
            return CommandCore(handle, slot, feature, function, CommandReplyTimeoutMs, args);
        }
        private static byte[] CommandCore(IntPtr handle, byte slot, byte feature, byte function, int timeoutMs, params byte[] args)
        {
            byte[] tx = new byte[20]; tx[0] = 0x11; tx[1] = slot; tx[2] = feature; tx[3] = (byte)(function | 9);
            Array.Copy(args, 0, tx, 4, args.Length);
            if (!HidTransport.WriteReport(handle, tx, 20, 300)) return null;
            Stopwatch timer = Stopwatch.StartNew();
            return ReceiveReply(tx, timeoutMs, delegate(int remaining)
            {
                byte[] rx;
                return HidTransport.ReadReportExact(handle, 20, remaining, out rx) ? rx : null;
            }, delegate { return timer.ElapsedMilliseconds; });
        }
        // A single deadline covers all unrelated reports. Transport is supplied by CommandCore;
        // deterministic frame/clock replay exercises this same production receive loop offline.
        private static byte[] ReceiveReply(byte[] tx, int timeoutMs, Func<int, byte[]> read, Func<long> elapsed)
        {
            if (tx == null || tx.Length != 20 || read == null || elapsed == null || timeoutMs <= 0 || timeoutMs > IdentityRootReplyTimeoutMs) return null;
            long remaining;
            while ((remaining = timeoutMs - elapsed()) > 0)
            {
                byte[] rx = read((int)remaining);
                if (rx == null || elapsed() >= timeoutMs) return null;
                if (rx.Length != 20 || rx[0] != tx[0] || rx[1] != tx[1]) continue;
                if (rx[2] == tx[2] && rx[3] == tx[3]) return rx;
                if (rx[2] == 0xFF && rx[3] == tx[2] && rx[4] == tx[3]) return null;
            }
            return null;
        }
        private static byte Feature(IntPtr handle, byte slot, int id)
        {
            byte[] root = Command(handle, slot, 0, 0, (byte)(id >> 8), (byte)id);
            return root != null && root[4] != 0xFF ? root[4] : (byte)0;
        }
        internal static bool IsModelIdentity(byte[] info, byte slot, byte feature)
        {
            return info != null && info.Length == 20 && info[0] == 0x11 && info[1] == slot &&
                feature != 0 && feature != 0xFF && info[2] == feature && info[3] == 9 &&
                info[11] == 0x40 && info[12] == 0xA9 && info[13] == 0xC0 && info[14] == 0x9B && info[15] == 0 && info[16] == 0;
        }
        private static string DeviceKey(HidDeviceDescriptor d, byte slot)
        {
            return "Logitech:046D:" + d.ProductId.ToString("X4") + (d.ProductId == 0xC54D ? ":" + slot : "");
        }
        private static void RememberVerifiedPath(HidDeviceDescriptor d, byte slot)
        {
            HashSet<string> keys;
            if (!verifiedPaths.TryGetValue(d.DevicePath, out keys))
            {
                keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                verifiedPaths[d.DevicePath] = keys;
            }
            keys.Add(DeviceKey(d, slot));
        }
        internal static bool AcceptIdentity(HidDeviceDescriptor d, byte slot, byte feature, byte[] identity)
        {
            if (!IsControlEndpoint(d) || string.IsNullOrEmpty(d.DevicePath) ||
                (d.ProductId == 0xC54D ? slot != 1 && slot != 2 : slot != 0xFF)) return false;
            lock (hidLock)
            {
                if (!IsModelIdentity(identity, slot, feature))
                {
                    HashSet<string> keys;
                    if (verifiedPaths.TryGetValue(d.DevicePath, out keys)) keys.Remove(DeviceKey(d, slot));
                    return false;
                }
                RememberVerifiedPath(d, slot);
                return true;
            }
        }
        internal static bool IsVerifiedDevice(MouseBatteryInfo dev)
        {
            if (dev == null || dev.Category != DeviceCategory.Mouse || string.IsNullOrEmpty(dev.HardwareId) || string.IsNullOrEmpty(dev.DeviceId)) return false;
            lock (hidLock)
            {
                HashSet<string> keys;
                return verifiedPaths.TryGetValue(dev.HardwareId, out keys) && keys.Contains(dev.DeviceId);
            }
        }
        private static byte[] ReadIdentity(IntPtr handle, byte slot, out bool wrongModel)
        {
            return ReadIdentityCore(slot, out wrongModel, delegate(byte feature, int timeoutMs)
            {
                return CommandCore(handle, slot, feature, 0, timeoutMs,
                    feature == 0 ? new byte[] { 0, 3 } : new byte[0]);
            });
        }
        private static byte[] ReadIdentityCore(byte slot, out bool wrongModel, Func<byte, int, byte[]> exchange)
        {
            wrongModel = false;
            byte[] root = exchange(0, IdentityRootReplyTimeoutMs);
            byte feature = root != null && root[4] != 0xFF ? root[4] : (byte)0;
            if (feature == 0) return null;
            byte[] info = exchange(feature, CommandReplyTimeoutMs);
            if (info == null) return null;
            wrongModel = !IsModelIdentity(info, slot, feature);
            return wrongModel ? null : info;
        }
        internal static bool TryReadUnifiedBattery(byte[] rx, byte slot, byte feature, out int percent, out bool charging)
        {
            percent = -1; charging = false;
            if (rx == null || rx.Length < 7 || rx[0] != 0x11 || rx[1] != slot || feature == 0 || feature == 0xFF ||
                rx[2] != feature || (rx[3] >> 4) != 1 || rx[4] > 100) return false;
            percent = rx[4]; charging = rx[6] >= 1 && rx[6] <= 4;
            return true;
        }
        internal static void ReadExtendedDpi(byte[] rx, byte slot, byte feature, out int x, out int y)
        {
            x = y = -1;
            if (rx == null || rx.Length < 13 || rx[0] != 0x11 || rx[1] != slot || feature == 0 || feature == 0xFF ||
                rx[2] != feature || (rx[3] >> 4) != 5 || rx[4] != 0) return;
            int currentX = (rx[5] << 8) | rx[6], currentY = (rx[9] << 8) | rx[10];
            if (currentX == 0) currentX = (rx[7] << 8) | rx[8];
            if (currentY == 0) currentY = (rx[11] << 8) | rx[12];
            if (currentX >= 50 && currentX <= 50000) x = currentX;
            if (currentY >= 50 && currentY <= 50000) y = currentY;
        }
        internal static int ReadReportRate(byte[] rx, byte slot, byte feature)
        {
            if (rx == null || rx.Length < 5 || rx[0] != 0x11 || rx[1] != slot || feature == 0 || feature == 0xFF ||
                rx[2] != feature || (rx[3] >> 4) != 2 || rx[4] > 6) return 0;
            return 125 << rx[4];
        }
        internal static Profile DecodeOnboardDpiProfile(byte[] data, int index, byte feature)
        {
            if (data == null || data.Length < 29 || index < 0 || index >= 5 || feature == 0 || feature == 0xFF) return null;
            Profile profile = new Profile(); profile.Feature = feature; profile.Stage = index + 1;
            profile.X = new int[5]; profile.Y = new int[5];
            for (int i = 0; i < 5; i++)
            {
                int offset = 4 + i * 5;
                int x = data[offset] | data[offset + 1] << 8, y = data[offset + 2] | data[offset + 3] << 8;
                if (x < 100 || x > 44000 || y < 100 || y > 44000) return null;
                profile.X[i] = x; profile.Y[i] = y;
            }
            return profile;
        }
        private static Profile ReadProfile(IntPtr handle, byte slot)
        {
            byte feature = Feature(handle, slot, 0x8100);
            if (feature == 0) return null;
            byte[] desc = Command(handle, slot, feature, 0);
            if (desc == null || desc[4] != 1 || desc[5] != 7) return null;
            byte[] mode = Command(handle, slot, feature, 0x20);
            if (mode == null || mode[4] != 1) return null;
            byte[] sector = Command(handle, slot, feature, 0x40);
            if (sector == null) return null;
            byte[] first = Command(handle, slot, feature, 0x50, sector[4], sector[5], 0, 0);
            byte[] next = Command(handle, slot, feature, 0x50, sector[4], sector[5], 0, 16);
            byte[] index = Command(handle, slot, feature, 0xB0);
            if (first == null || next == null || index == null) return null;
            byte[] data = new byte[32]; Array.Copy(first, 4, data, 0, 16); Array.Copy(next, 4, data, 16, 16);
            return DecodeOnboardDpiProfile(data, index[4], feature);
        }
        private static string CacheKey(HidDeviceDescriptor d, byte slot)
        {
            return PathSlotKey(d.DevicePath, slot);
        }
        private static string PathSlotKey(string path, byte slot)
        {
            return DriverKey + ":" + path + ":" + slot;
        }
        private static MouseBatteryInfo CreateState(HidDeviceDescriptor d, byte slot, int percent, bool charging,
            bool sleeping, DateTime updated, string unit)
        {
            bool wireless = d.ProductId == 0xC54D;
            MouseBatteryInfo dev = new MouseBatteryInfo();
            dev.DeviceId = DeviceKey(d, slot);
            dev.HardwareFingerprint = dev.DeviceId; dev.HardwareId = d.DevicePath;
            dev.DriverId = DriverKey; dev.Brand = "Logitech"; dev.DeviceName = ModelName; dev.Category = DeviceCategory.Mouse;
            dev.Transport = wireless ? "Lightspeed 无线" : "USB 有线";
            dev.IsConnected = true; dev.IsDonglePresent = wireless; dev.IsSleeping = sleeping;
            dev.BatteryPercent = percent; dev.ExactBatteryPercent = percent;
            dev.IsCharging = !sleeping && charging; dev.IsChargingUnknown = sleeping || percent < 0; dev.LastUpdated = updated;
            dev.SupportsPollingRateWrite = false; SetOptional(dev, "SupportsDpiWrite", false); SetOptional(dev, "PhysicalUnitId", unit);
            return dev;
        }
        private static string Unit(byte[] identity)
        {
            string id = BitConverter.ToString(identity, 5, 4);
            return id == "00-00-00-00" || id == "FF-FF-FF-FF" ? null : "Logitech:40A9:Unit:" + id;
        }
        private static MouseBatteryInfo CachedState(HidDeviceDescriptor d, byte slot)
        {
            string key = CacheKey(d, slot); MouseBatteryInfo previous;
            if (last.TryGetValue(key, out previous))
            {
                PropertyInfo p = typeof(MouseBatteryInfo).GetProperty("PhysicalUnitId");
                return CreateState(d, slot, previous.BatteryPercent, false, true, previous.LastUpdated,
                    p == null ? null : p.GetValue(previous, null) as string);
            }
            int percent; bool charging; string name; DateTime updated;
            if (DriverStateStore.TryLoadBatteryState(key, out percent, out charging, out name, out updated) && name == ModelName)
                return CreateState(d, slot, percent, false, true, updated, null);
            return null;
        }
        internal static List<MouseBatteryInfo> QueryDevices()
        {
            List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
            lock (hidLock)
            {
                claims.Clear(); active.Clear(); verifiedPaths.Clear();
                List<HidDeviceDescriptor> all = HidTransport.Enumerate(0x046D, 0);
                foreach (HidDeviceDescriptor d in all)
                {
                    if (!IsControlEndpoint(d)) continue;
                    IntPtr handle = HidTransport.Open(d.DevicePath);
                    if (!HidTransport.IsValidHandle(handle)) continue;
                    try
                    {
                        bool verified = false, otherModel = false;
                        byte[] slots = d.ProductId == 0xC54D ? new byte[] { 1, 2 } : new byte[] { 0xFF };
                        foreach (byte slot in slots)
                        {
                            bool wrongModel; byte[] identity = ReadIdentity(handle, slot, out wrongModel);
                            string key = CacheKey(d, slot);
                            if (wrongModel)
                            {
                                otherModel = true; last.Remove(key); DriverStateStore.ClearDeviceState(key); continue;
                            }
                            if (identity == null)
                            {
                                MouseBatteryInfo cached = CachedState(d, slot);
                                if (cached != null) { RememberVerifiedPath(d, slot); result.Add(cached); verified = true; }
                                continue;
                            }
                            if (!AcceptIdentity(d, slot, identity[2], identity)) continue;
                            verified = true;
                            byte batteryFeature = Feature(handle, slot, 0x1004);
                            int percent = -1; bool charging = false;
                            bool fresh = batteryFeature != 0 && TryReadUnifiedBattery(Command(handle, slot, batteryFeature, 0x10), slot, batteryFeature, out percent, out charging);
                            MouseBatteryInfo dev = fresh ? CreateState(d, slot, percent, charging, false, DateTime.Now, Unit(identity)) : CachedState(d, slot);
                            if (dev == null) dev = CreateState(d, slot, -1, false, false, DateTime.MinValue, Unit(identity));
                            SetOptional(dev, "PhysicalUnitId", Unit(identity));
                            byte dpiFeature = Feature(handle, slot, 0x2202);
                            int x = -1, y = -1;
                            if (dpiFeature != 0) ReadExtendedDpi(Command(handle, slot, dpiFeature, 0x50), slot, dpiFeature, out x, out y);
                            dev.Dpi = x > 0 ? x : 0; dev.DpiY = y > 0 ? y : 0; dev.IsXyIndependent = x > 0 && y > 0 && x != y;
                            byte rateFeature = Feature(handle, slot, 0x8061);
                            if (rateFeature != 0) dev.PollingRate = ReadReportRate(Command(handle, slot, rateFeature, 0x20, d.ProductId == 0xC54D ? (byte)1 : (byte)0), slot, rateFeature);
                            dev.SupportedPollingRates = new int[] {125,250,500,1000,2000,4000,8000};
                            Profile profile = d.ProductId == 0xC54D ? ReadProfile(handle, slot) : null;
                            if (profile != null)
                            {
                                dev.DpiStages = profile.X; dev.DpiStagesY = profile.Y; dev.DpiStage = profile.Stage; dev.DpiStageCount = 5;
                            }
                            if (fresh)
                            {
                                last[key] = dev; DriverStateStore.SaveBatteryState(key, percent, charging, ModelName);
                            }
                            RememberActiveEndpoint(d, slot, dpiFeature, profile);
                            result.Add(dev);
                        }
                        // A receiver with a responsive different paired model is not wholly owned.
                        if (verified && !otherModel) ClaimInterface(d, all);
                    }
                    finally { HidTransport.Close(handle); }
                }
            }
            return result;
        }
        private static void RememberActiveEndpoint(HidDeviceDescriptor d, byte slot, byte dpiFeature, Profile profile)
        {
            active[CacheKey(d, slot)] = new Endpoint { Descriptor = d, Slot = slot, DpiFeature = dpiFeature, Profile = profile };
        }
        private static Endpoint FindEndpoint(MouseBatteryInfo dev)
        {
            if (dev == null || string.IsNullOrEmpty(dev.DeviceId) || string.IsNullOrEmpty(dev.HardwareId)) return null;
            // Logical IDs remain stable for tray bindings; command routing must include the
            // physical control path and receiver slot to distinguish two identical receivers.
            for (int index = 0; index < 3; index++)
            {
                byte slot = index < 2 ? (byte)(index + 1) : (byte)0xFF;
                Endpoint endpoint;
                if (active.TryGetValue(PathSlotKey(dev.HardwareId, slot), out endpoint) &&
                    string.Equals(dev.DeviceId, DeviceKey(endpoint.Descriptor, slot), StringComparison.OrdinalIgnoreCase)) return endpoint;
            }
            return null;
        }
        internal static bool FastQueryDpi(MouseBatteryInfo dev, out int x, out int y, out int stage, out int count)
        {
            x = y = stage = count = 0;
            lock (hidLock)
            {
                Endpoint ep = FindEndpoint(dev);
                if (ep == null || ep.DpiFeature == 0) return false;
                IntPtr handle = HidTransport.Open(ep.Descriptor.DevicePath);
                if (!HidTransport.IsValidHandle(handle)) return false;
                try
                {
                    ReadExtendedDpi(Command(handle, ep.Slot, ep.DpiFeature, 0x50), ep.Slot, ep.DpiFeature, out x, out y);
                    if (x <= 0 || y <= 0) return false;
                    if (ep.Profile != null)
                    {
                        byte[] index = Command(handle, ep.Slot, ep.Profile.Feature, 0xB0);
                        if (index != null && index[4] < 5) { stage = index[4] + 1; count = 5; }
                    }
                    return true;
                }
                finally { HidTransport.Close(handle); }
            }
        }
        internal static bool SelectOnboardStage(MouseBatteryInfo dev, int stage, int x, int y)
        {
            lock (hidLock)
            {
                Endpoint ep = FindEndpoint(dev);
                if (ep == null || ep.Descriptor.ProductId != 0xC54D || ep.Profile == null || ep.DpiFeature == 0 || stage < 1 || stage > 5 ||
                    ep.Profile.X[stage - 1] != x || ep.Profile.Y[stage - 1] != y) return false;
                IntPtr handle = HidTransport.Open(ep.Descriptor.DevicePath);
                if (!HidTransport.IsValidHandle(handle)) return false;
                try
                {
                    bool wrongModel;
                    if (ReadIdentity(handle, ep.Slot, out wrongModel) == null) return false;
                    Profile profile = ReadProfile(handle, ep.Slot);
                    if (profile == null || profile.X[stage - 1] != x || profile.Y[stage - 1] != y) return false;
                    if (Command(handle, ep.Slot, profile.Feature, 0xC0, (byte)(stage - 1)) == null) return false;
                    Thread.Sleep(30);
                    byte[] index = Command(handle, ep.Slot, profile.Feature, 0xB0);
                    if (index == null || index[4] != stage - 1) return false;
                    int actualX, actualY;
                    ReadExtendedDpi(Command(handle, ep.Slot, ep.DpiFeature, 0x50), ep.Slot, ep.DpiFeature, out actualX, out actualY);
                    if (actualX != x || actualY != y) return false;
                    profile.Stage = stage; ep.Profile = profile;
                    return true;
                }
                finally { HidTransport.Close(handle); }
            }
        }
    }
}
