// ============================================================================
// 模块名称: LogitechMr0097Helper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   Logitech G PRO X SUPERLIGHT 2 (MR0097) 内置 HID++ 2.0 专属驱动协议引擎：
//   1. 支持 2.4G Lightspeed 接收器 (046D:C54D, Slot 1/2) 与 USB 有线直连 (046D:C09B, Slot FF)
//      仅使用 FF00/0002 (20/20/0) 长报文控制端点，并通过特性 0003 核验精确型号 40A9:C09B:0000。
//   2. 独家接管且无其余配对设备的接收器自动屏蔽 LogitechDeviceHelper 越界重复访问，
//      同时保留 External (.fp-driver.cs) Priority 300 外部脚本热覆盖优先级。
//   3. 支持 1004 统一电量/充电读取、2202 只读 XY 独立 DPI 读取、8061 只读回报率读取、
//      8100 板载 5 档位格式 7 读取与已有无线档位安全切换（严禁任意 DPI/回报率越权写入）。
//   4. 感谢社区开发者 @GaiusC 贡献实机抓包帧 (2026-11-08/09) 与板载档位回放验证。
// 依赖关系: HidTransport.cs, DriverStateStore.cs, HardwareDriverRegistry.cs, MouseBatteryInfo.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、协议解析或签名时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止 $"..." 插值、禁止 ?. 空条件运算符、禁止 nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading;

namespace RazerBatteryTray
{
    public static class LogitechMr0097Helper
    {
        public const string DriverKey = "BuiltIn:LogitechMR0097";
        internal const string ModelName = "Logitech G PRO X SUPERLIGHT 2";
        private const int CommandReplyTimeoutMs = 500;
        // Owner capture 2026-11-09: cold ROOT 0003 response took 1059 ms; warm retry took 7 ms.
        // Give only identity discovery a longer bounded receive window; never resend writes.
        private const int IdentityRootReplyTimeoutMs = 2000;

        private static readonly object hidLock = new object();
        private static readonly Dictionary<string, Endpoint> active = new Dictionary<string, Endpoint>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, MouseBatteryInfo> last = new Dictionary<string, MouseBatteryInfo>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, HashSet<string>> verifiedPaths = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> claimedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal sealed class Profile
        {
            public byte Feature;
            public int Stage;
            public int[] X;
            public int[] Y;
        }

        internal sealed class Endpoint
        {
            internal HidDeviceDescriptor Descriptor;
            internal byte Slot;
            internal byte DpiFeature;
            internal Profile Profile;
        }

        public static bool IsOwnedPath(string path)
        {
            if (string.IsNullOrEmpty(path)) return false;
            lock (hidLock)
            {
                return claimedPaths.Contains(path);
            }
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
            return ma.Success && mb.Success &&
                string.Equals(ma.Groups[1].Value, mb.Groups[1].Value, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(ma.Groups[2].Value, mb.Groups[2].Value, StringComparison.OrdinalIgnoreCase);
        }

        private static void ClaimInterface(HidDeviceDescriptor verified, List<HidDeviceDescriptor> all)
        {
            if (!string.IsNullOrEmpty(verified.DevicePath)) claimedPaths.Add(verified.DevicePath);
            if (all == null) return;
            foreach (HidDeviceDescriptor candidate in all)
            {
                if (candidate.VendorId == verified.VendorId && candidate.ProductId == verified.ProductId &&
                    candidate.UsagePage == 0xFF00 && candidate.FeatureReportLength == 0 &&
                    ((candidate.Usage == 1 && candidate.InputReportLength == 7 && candidate.OutputReportLength == 7) || IsControlEndpoint(candidate)) &&
                    SameProtocolInterface(verified.DevicePath, candidate.DevicePath) &&
                    !string.IsNullOrEmpty(candidate.DevicePath))
                {
                    claimedPaths.Add(candidate.DevicePath);
                }
            }
        }

        private static byte[] Command(IntPtr handle, byte slot, byte feature, byte function, params byte[] args)
        {
            return CommandCore(handle, slot, feature, function, CommandReplyTimeoutMs, args);
        }

        private static byte[] CommandCore(IntPtr handle, byte slot, byte feature, byte function, int timeoutMs, params byte[] args)
        {
            byte[] tx = new byte[20];
            tx[0] = 0x11;
            tx[1] = slot;
            tx[2] = feature;
            tx[3] = (byte)(function | 9);
            if (args != null && args.Length > 0) Array.Copy(args, 0, tx, 4, args.Length);
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
        internal static byte[] ReceiveReply(byte[] tx, int timeoutMs, Func<int, byte[]> read, Func<long> elapsed)
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

        public static bool IsVerifiedDevice(MouseBatteryInfo dev)
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

        internal static byte[] ReadIdentityCore(byte slot, out bool wrongModel, Func<byte, int, byte[]> exchange)
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
            percent = -1;
            charging = false;
            if (rx == null || rx.Length < 7 || rx[0] != 0x11 || rx[1] != slot || feature == 0 || feature == 0xFF ||
                rx[2] != feature || (rx[3] >> 4) != 1 || rx[4] > 100) return false;
            percent = rx[4];
            charging = rx[6] >= 1 && rx[6] <= 4;
            return true;
        }

        internal static void ReadExtendedDpi(byte[] rx, byte slot, byte feature, out int x, out int y)
        {
            x = y = -1;
            if (rx == null || rx.Length < 13 || rx[0] != 0x11 || rx[1] != slot || feature == 0 || feature == 0xFF ||
                rx[2] != feature || (rx[3] >> 4) != 5 || rx[4] != 0) return;
            int currentX = (rx[5] << 8) | rx[6];
            int currentY = (rx[9] << 8) | rx[10];
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
            Profile profile = new Profile();
            profile.Feature = feature;
            profile.Stage = index + 1;
            profile.X = new int[5];
            profile.Y = new int[5];
            for (int i = 0; i < 5; i++)
            {
                int offset = 4 + i * 5;
                int x = data[offset] | (data[offset + 1] << 8);
                int y = data[offset + 2] | (data[offset + 3] << 8);
                if (x < 100 || x > 44000 || y < 100 || y > 44000) return null;
                profile.X[i] = x;
                profile.Y[i] = y;
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
            byte[] data = new byte[32];
            Array.Copy(first, 4, data, 0, 16);
            Array.Copy(next, 4, data, 16, 16);
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

        internal static MouseBatteryInfo CreateState(HidDeviceDescriptor d, byte slot, int percent, bool charging,
            bool sleeping, DateTime updated, string unit)
        {
            bool wireless = d.ProductId == 0xC54D;
            MouseBatteryInfo dev = new MouseBatteryInfo();
            dev.DeviceId = DeviceKey(d, slot);
            dev.HardwareFingerprint = dev.DeviceId;
            dev.HardwareId = d.DevicePath;
            dev.DriverId = DriverKey;
            dev.Brand = "Logitech";
            dev.DeviceName = ModelName;
            dev.Category = DeviceCategory.Mouse;
            dev.Transport = wireless ? "Lightspeed 无线" : "USB 有线";
            dev.IsConnected = true;
            dev.IsDonglePresent = wireless;
            dev.IsSleeping = sleeping;
            dev.BatteryPercent = percent;
            dev.ExactBatteryPercent = percent;
            dev.IsCharging = !sleeping && charging;
            dev.IsChargingUnknown = sleeping || percent < 0;
            dev.LastUpdated = updated;
            dev.SupportsPollingRateWrite = false;
            dev.SupportsDpiWrite = false;
            dev.PhysicalUnitId = unit;
            return dev;
        }

        private static string Unit(byte[] identity)
        {
            string id = BitConverter.ToString(identity, 5, 4);
            return id == "00-00-00-00" || id == "FF-FF-FF-FF" ? null : "Logitech:40A9:Unit:" + id;
        }

        internal static MouseBatteryInfo CachedState(HidDeviceDescriptor d, byte slot)
        {
            string key = CacheKey(d, slot);
            MouseBatteryInfo previous;
            if (last.TryGetValue(key, out previous))
            {
                return CreateState(d, slot, previous.BatteryPercent, false, true, previous.LastUpdated, previous.PhysicalUnitId);
            }
            int percent;
            bool charging;
            string name;
            DateTime updated;
            if (DriverStateStore.TryLoadBatteryState(key, out percent, out charging, out name, out updated) && name == ModelName)
            {
                return CreateState(d, slot, percent, false, true, updated, null);
            }
            return null;
        }

        public static List<MouseBatteryInfo> QueryDevices()
        {
            List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
            lock (hidLock)
            {
                claimedPaths.Clear();
                active.Clear();
                verifiedPaths.Clear();
                List<HidDeviceDescriptor> all = HidTransport.Enumerate(0x046D, 0);
                foreach (HidDeviceDescriptor d in all)
                {
                    if (!IsControlEndpoint(d)) continue;
                    if (HardwareDriverRegistry.IsExternallyOwnedPath(d.DevicePath)) continue;
                    IntPtr handle = HidTransport.Open(d.DevicePath);
                    if (!HidTransport.IsValidHandle(handle)) continue;
                    try
                    {
                        bool verified = false;
                        bool otherModel = false;
                        byte[] slots = d.ProductId == 0xC54D ? new byte[] { 1, 2 } : new byte[] { 0xFF };
                        foreach (byte slot in slots)
                        {
                            bool wrongModel;
                            byte[] identity = ReadIdentity(handle, slot, out wrongModel);
                            string key = CacheKey(d, slot);
                            if (wrongModel)
                            {
                                otherModel = true;
                                last.Remove(key);
                                DriverStateStore.ClearDeviceState(key);
                                continue;
                            }
                            if (identity == null)
                            {
                                MouseBatteryInfo cached = CachedState(d, slot);
                                if (cached != null)
                                {
                                    RememberVerifiedPath(d, slot);
                                    result.Add(cached);
                                    verified = true;
                                }
                                continue;
                            }
                            if (!AcceptIdentity(d, slot, identity[2], identity)) continue;
                            verified = true;
                            byte batteryFeature = Feature(handle, slot, 0x1004);
                            int percent = -1;
                            bool charging = false;
                            bool fresh = batteryFeature != 0 &&
                                TryReadUnifiedBattery(Command(handle, slot, batteryFeature, 0x10), slot, batteryFeature, out percent, out charging);
                            MouseBatteryInfo dev = fresh
                                ? CreateState(d, slot, percent, charging, false, DateTime.Now, Unit(identity))
                                : CachedState(d, slot);
                            if (dev == null) dev = CreateState(d, slot, -1, false, false, DateTime.MinValue, Unit(identity));
                            dev.PhysicalUnitId = Unit(identity);

                            byte dpiFeature = Feature(handle, slot, 0x2202);
                            int x = -1;
                            int y = -1;
                            if (dpiFeature != 0) ReadExtendedDpi(Command(handle, slot, dpiFeature, 0x50), slot, dpiFeature, out x, out y);
                            dev.Dpi = x > 0 ? x : 0;
                            dev.DpiY = y > 0 ? y : 0;
                            dev.IsXyIndependent = x > 0 && y > 0 && x != y;

                            byte rateFeature = Feature(handle, slot, 0x8061);
                            if (rateFeature != 0)
                            {
                                dev.PollingRate = ReadReportRate(Command(handle, slot, rateFeature, 0x20, d.ProductId == 0xC54D ? (byte)1 : (byte)0), slot, rateFeature);
                            }
                            dev.SupportedPollingRates = new int[] { 125, 250, 500, 1000, 2000, 4000, 8000 };

                            Profile profile = d.ProductId == 0xC54D ? ReadProfile(handle, slot) : null;
                            if (profile != null)
                            {
                                dev.DpiStages = profile.X;
                                dev.DpiStagesY = profile.Y;
                                dev.DpiStage = profile.Stage;
                                dev.DpiStageCount = 5;
                            }
                            if (fresh)
                            {
                                last[key] = dev;
                                DriverStateStore.SaveBatteryState(key, percent, charging, ModelName);
                            }
                            RememberActiveEndpoint(d, slot, dpiFeature, profile);
                            result.Add(dev);
                        }
                        // A receiver with a responsive different paired model is not wholly owned.
                        if (verified && !otherModel) ClaimInterface(d, all);
                    }
                    finally
                    {
                        HidTransport.Close(handle);
                    }
                }
            }
            return result;
        }

        internal static void RememberActiveEndpoint(HidDeviceDescriptor d, byte slot, byte dpiFeature, Profile profile)
        {
            active[CacheKey(d, slot)] = new Endpoint { Descriptor = d, Slot = slot, DpiFeature = dpiFeature, Profile = profile };
        }

        internal static Endpoint FindEndpoint(MouseBatteryInfo dev)
        {
            if (dev == null || string.IsNullOrEmpty(dev.DeviceId) || string.IsNullOrEmpty(dev.HardwareId)) return null;
            // Logical IDs remain stable for tray bindings; command routing must include the
            // physical control path and receiver slot to distinguish two identical receivers.
            for (int index = 0; index < 3; index++)
            {
                byte slot = index < 2 ? (byte)(index + 1) : (byte)0xFF;
                Endpoint endpoint;
                if (active.TryGetValue(PathSlotKey(dev.HardwareId, slot), out endpoint) &&
                    string.Equals(dev.DeviceId, DeviceKey(endpoint.Descriptor, slot), StringComparison.OrdinalIgnoreCase))
                {
                    return endpoint;
                }
            }
            return null;
        }

        public static bool FastQueryDpi(MouseBatteryInfo dev, out int x, out int y, out int stage, out int count)
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
                        if (index != null && index[4] < 5)
                        {
                            stage = index[4] + 1;
                            count = 5;
                        }
                    }
                    return true;
                }
                finally
                {
                    HidTransport.Close(handle);
                }
            }
        }

        public static bool SelectOnboardStage(MouseBatteryInfo dev, int stage, int x, int y)
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
                    profile.Stage = stage;
                    ep.Profile = profile;
                    return true;
                }
                finally
                {
                    HidTransport.Close(handle);
                }
            }
        }
    }
}
