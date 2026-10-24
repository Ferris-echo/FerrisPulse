// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// ASUS ROG Azoth through OMNI: pair list on FF02, keyboard power on FF00.
// C# 5 / .NET 4 only; UTF-8 BOM. Read-only, no pairing/settings writes.
// Protocol reference: seerge/g-helper PeripheralsProvider.cs and AsusKeyboard.cs.
// Independently implemented against local 1ACE/1A85 captures; see docs/ROG_AZOTH_ADAPTATION.md.
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace RazerBatteryTray
{
    public static class AsusDeviceHelper
    {
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, MouseBatteryInfo> Cache =
            new Dictionary<string, MouseBatteryInfo>(StringComparer.OrdinalIgnoreCase);

        public static bool IsKeyboardEndpoint(HidDeviceDescriptor d)
        {
            return d != null && d.VendorId == 0x0B05 && d.ProductId == 0x1ACE &&
                d.UsagePage == 0xFF00 && d.Usage == 1 &&
                d.InputReportLength == 64 && d.OutputReportLength == 64;
        }

        public static int ParseAzothPairing(byte[] r)
        {
            if (r == null || r.Length != 64 || r[0] != 1 || r[1] != 0xA0 || r[2] != 0 || r[3] != 1) return 0;
            for (int offset = 5; offset + 3 < r.Length; offset += 4)
            {
                int pid = r[offset] | (r[offset + 1] << 8);
                if (pid == 0) break;
                if (pid == 0x1A85) return pid;
            }
            return 0;
        }

        public static bool TryParseBattery(byte[] r, out int battery, out bool charging)
        {
            battery = -1;
            charging = false;
            if (r == null || r.Length != 64 || r[0] != 2 || r[1] != 0x12 || r[2] != 1 ||
                r[5] != 0 || r[6] > 100 || r[9] > 1) return false;
            battery = r[6];
            charging = r[9] == 1;
            return true;
        }

        private static string ReceiverInstance(string path)
        {
            if (string.IsNullOrEmpty(path)) return "";
            string[] parts = path.Split('#');
            if (parts.Length < 3) return path;
            int cut = parts[2].LastIndexOf('&');
            return cut > 0 ? parts[2].Substring(0, cut) : parts[2];
        }

        private static byte[] Query(HidDeviceDescriptor endpoint, byte report, byte command, byte subcommand)
        {
            IntPtr handle = HidTransport.Open(endpoint.DevicePath);
            if (!HidTransport.IsValidHandle(handle)) return null;
            try
            {
                HidTransport.DrainReports(handle, 64, 3);
                byte[] tx = new byte[64];
                tx[0] = report;
                tx[1] = command;
                tx[2] = subcommand;
                if (!HidTransport.WriteReport(handle, tx, 64, 300)) return null;
                Stopwatch timer = Stopwatch.StartNew();
                while (timer.ElapsedMilliseconds < 450)
                {
                    byte[] rx;
                    int remaining = Math.Max(1, 450 - (int)timer.ElapsedMilliseconds);
                    if (!HidTransport.ReadReport(handle, 64, remaining, out rx)) return null;
                    if (rx.Length != 64 || rx[0] != report) continue;
                    if (rx[1] == 0xFF && rx[2] == 0xAA) return null;
                    if (rx[1] == command && rx[2] == subcommand) return rx;
                }
                return null;
            }
            finally { HidTransport.Close(handle); }
        }

        public static List<MouseBatteryInfo> QueryAllKeyboards()
        {
            lock (Sync)
            {
                List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
                List<HidDeviceDescriptor> endpoints = HidTransport.Enumerate(0x0B05, 0x1ACE);
                foreach (HidDeviceDescriptor keyboard in endpoints)
                {
                    if (!IsKeyboardEndpoint(keyboard)) continue;
                    string instance = ReceiverInstance(keyboard.DevicePath);
                    HidDeviceDescriptor receiver = endpoints.Find(delegate(HidDeviceDescriptor d)
                    {
                        return d.UsagePage == 0xFF02 && d.Usage == 1 && d.InputReportLength == 64 &&
                            d.OutputReportLength == 64 && ReceiverInstance(d.DevicePath) == instance;
                    });
                    if (receiver == null) continue;
                    int pid = ParseAzothPairing(Query(receiver, 1, 0xA0, 0));
                    if (pid != 0x1A85) continue; // Never assume every OMNI receiver contains an Azoth.
                    string id = "ASUS:0B05:1ACE:KB:" + instance;
                    MouseBatteryInfo previous;
                    Cache.TryGetValue(id, out previous);
                    MouseBatteryInfo info = new MouseBatteryInfo();
                    info.DeviceId = id;
                    info.HardwareFingerprint = "HID:0B05:1ACE:KB:" + instance;
                    info.HardwareId = keyboard.DevicePath;
                    info.Brand = "ASUS";
                    info.Category = DeviceCategory.Keyboard;
                    info.DeviceName = "ROG 夜魔 (Azoth)";
                    info.Transport = "2.4G 无线";
                    info.IsDonglePresent = true;
                    info.BatteryPercent = previous != null ? previous.BatteryPercent : -1;
                    info.LastUpdated = previous != null ? previous.LastUpdated : DateTime.MinValue;
                    int battery;
                    bool charging;
                    byte[] rx = Query(keyboard, 2, 0x12, 1);
                    if (!TryParseBattery(rx, out battery, out charging))
                    {
                        // One bounded retry after a read-only version probe; never a settings handshake.
                        Query(keyboard, 2, 0x12, 0);
                        rx = Query(keyboard, 2, 0x12, 1);
                    }
                    if (TryParseBattery(rx, out battery, out charging))
                    {
                        info.BatteryPercent = battery;
                        info.IsCharging = charging;
                        info.IsConnected = true;
                        info.LastUpdated = DateTime.Now;
                    }
                    else
                    {
                        info.IsSleeping = true;
                        info.IsConnected = false;
                        info.IsCharging = false;
                    }
                    info.ExactBatteryPercent = info.BatteryPercent;
                    Cache[id] = info;
                    result.Add(info);
                }
                return result;
            }
        }

        // Older versions saved the receiver as a Dongle. Upgrade only an unambiguous keyboard match.
        // Keep its saved registration and user alias, but never copy Dongle/Mouse classification.
        public static bool MergeLegacyKeyboardConfig(List<MouseBatteryInfo> devices, CustomDeviceConfig cfg)
        {
            if (devices == null || cfg == null ||
                (cfg.Category != DeviceCategory.Dongle && cfg.Category != DeviceCategory.Generic && cfg.Category != DeviceCategory.Keyboard)) return false;
            if (!string.Equals(cfg.DeviceId, "HID:0B05:1ACE", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(cfg.DeviceId, "Gaming:0B05:1ACE", StringComparison.OrdinalIgnoreCase)) return false;
            List<MouseBatteryInfo> matches = devices.FindAll(delegate(MouseBatteryInfo d)
            {
                return d.DeviceId != null && d.DeviceId.StartsWith("ASUS:0B05:1ACE:KB:", StringComparison.OrdinalIgnoreCase) && d.Category == DeviceCategory.Keyboard;
            });
            if (matches.Count != 1) return false;
            matches[0].IsCustom = true;
            if (!string.IsNullOrEmpty(cfg.CustomName) && !cfg.CustomName.Equals("ROG OMNI RECEIVER", StringComparison.OrdinalIgnoreCase))
                matches[0].CustomName = cfg.CustomName;
            return true;
        }
    }
}