// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// 模块: 雷柏 VT3 纯协议解析与同一物理 HID 父接口的端点选择。
// C# 5.0 / .NET 4.0; UTF-8 BOM; 无设备 I/O 或第三方依赖。
// 报文布局移植自 rapoo-tray (MIT, Copyright (c) 2026 Iris)。
// 完整许可见 docs/rapoo-tray-MIT.md。
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace RazerBatteryTray
{
    public sealed class RapooStatusReport
    {
        public int DpiStage;
        public int DpiX;
        public int DpiY;
        public int BatteryPercent;
        public bool IsCharging;
    }

    public sealed class RapooEndpointSet
    {
        public HidDeviceDescriptor Control;
        public HidDeviceDescriptor Feature;
        public HidDeviceDescriptor Status;
        public string GroupKey;
    }

    public static class RapooProtocol
    {
        public static bool IsValidDpiConfiguration(int activeStage, int[] stagesX, int[] stagesY)
        {
            if (stagesX == null || stagesY == null || stagesX.Length < 1 || stagesX.Length > 6 ||
                stagesX.Length != stagesY.Length || activeStage < 1 || activeStage > stagesX.Length) return false;
            for (int i = 0; i < stagesX.Length; i++)
            {
                // Validate the wire format; firmware readback verifies sensor support.
                if (stagesX[i] < 50 || stagesX[i] > 65535 || stagesY[i] < 50 || stagesY[i] > 65535) return false;
            }
            return true;
        }

        public static bool TryParseStatus(byte[] data, out RapooStatusReport status)
        {
            status = null;
            if (data == null || data.Length < 9 || data[0] != 0x07) return false;
            int mode = data[1] & 0xF0;
            if ((mode != 0x10 && mode != 0x20) || data[2] >= 6) return false;
            int dpiX = data[3] | (data[4] << 8);
            int dpiY = data[5] | (data[6] << 8);
            if (dpiX < 50 || dpiY < 50) return false;
            status = new RapooStatusReport();
            status.DpiStage = data[2] + 1;
            status.DpiX = dpiX;
            status.DpiY = dpiY;
            status.BatteryPercent = data[8] <= 100 ? data[8] : -1;
            // USB direct connection is not proof of charging. Use report flags.
            status.IsCharging = (data[7] & 0x02) != 0 ||
                (data.Length > 9 && (data[9] == 1 || data[9] == 2));
            return true;
        }

        public static bool TryParseFeature(byte[] data, int length, out byte[] payload)
        {
            payload = null;
            if (data == null || length < 1 || length > 25 || data.Length < 4) return false;
            int offset;
            if (data[0] == 0x01) offset = 4;
            else if (data[0] == 0x08 && data[1] == 0x01) offset = 5;
            else return false;
            if (data.Length < offset + length) return false;
            payload = new byte[length];
            Array.Copy(data, offset, payload, 0, length);
            return true;
        }

        private static string GetGroupKey(HidDeviceDescriptor device)
        {
            // HID sibling collections share the parent-instance prefix. Keep MI and
            // parent identity; remove only collection identifiers, never just VID/PID.
            string instance = device.InstanceId;
            if (string.IsNullOrEmpty(instance)) return null;
            instance = instance.ToUpperInvariant();
            if (instance.IndexOf("&COL", StringComparison.Ordinal) >= 0)
            {
                instance = Regex.Replace(instance, @"&COL[0-9A-F]{2}", "");
                int lastSlash = instance.LastIndexOf('\\');
                int lastAmp = instance.LastIndexOf('&');
                if (lastAmp > lastSlash) instance = instance.Substring(0, lastAmp);
            }
            return device.ProductId.ToString("X4") + ":" + instance;
        }

        public static RapooEndpointSet SelectEndpoints(List<HidDeviceDescriptor> devices)
        {
            if (devices == null) return null;
            Dictionary<string, RapooEndpointSet> groups = new Dictionary<string, RapooEndpointSet>(StringComparer.OrdinalIgnoreCase);
            foreach (HidDeviceDescriptor device in devices)
            {
                if (device == null || device.VendorId != 0x24AE || device.UsagePage != 0xFF00) continue;
                string key = GetGroupKey(device);
                if (key == null) continue;
                RapooEndpointSet group;
                if (!groups.TryGetValue(key, out group))
                {
                    group = new RapooEndpointSet();
                    group.GroupKey = key;
                    groups.Add(key, group);
                }
                if (device.Usage == 0x0E && device.OutputReportLength == 33) group.Control = device;
                if (device.Usage == 0x0F && device.FeatureReportLength == 33) group.Feature = device;
                if (device.Usage == 0x02 && device.InputReportLength >= 9 && device.InputReportLength <= 65) group.Status = device;
            }
            RapooEndpointSet selected = null;
            foreach (RapooEndpointSet group in groups.Values)
            {
                if (group.Control == null || group.Feature == null) continue;
                if (selected == null || Score(group) > Score(selected) ||
                    (Score(group) == Score(selected) && string.CompareOrdinal(group.GroupKey, selected.GroupKey) < 0)) selected = group;
            }
            return selected;
        }

        private static int Score(RapooEndpointSet group)
        {
            return (group.Status != null ? 2 : 0) + (group.Control.ProductId >= 0x4000 ? 1 : 0);
        }
    }
}
