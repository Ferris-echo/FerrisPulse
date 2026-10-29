// ============================================================================
// 模块名称: DeviceManager.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   全品牌外设扫描调度中心：统一设备轮询、多设备优先级仲裁与缓存管理
// 依赖关系: 所有品牌 DeviceHelper, MouseBatteryInfo.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、协议解析或签名时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace RazerBatteryTray
{
    public static class DeviceManager
    {
        private const string PrimaryDeviceRegKey = @"Software\RazerBatteryTray";
        private const string PrimaryDeviceValName = "PrimaryDeviceId";
        private const string PriorityOrderValName = "PriorityOrder";
        private const string AutoSwitchValName = "AutoSwitchPriority";
        private const string CustomDevicesRegPath = @"Software\RazerBatteryTray\CustomDevices";
        private const string HiddenDevicesRegPath = @"Software\RazerBatteryTray\HiddenDevices";

        public static List<string> GetDeviceIdVariants(string deviceId)
        {
            var variants = new List<string>();
            if (string.IsNullOrEmpty(deviceId)) return variants;
            variants.Add(deviceId);

            if (deviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase))
            {
                string norm = RazerDeviceHelper.NormalizeRazerDeviceId(deviceId);
                if (!variants.Contains(norm)) variants.Add(norm);
            }

            int colonIdx = deviceId.IndexOf(':');
            if (colonIdx > 0 && colonIdx < deviceId.Length - 1)
            {
                string suffix = deviceId.Substring(colonIdx + 1);
                string cleanSuffix = suffix;
                if (cleanSuffix.EndsWith(":MS", StringComparison.OrdinalIgnoreCase) || cleanSuffix.EndsWith(":KB", StringComparison.OrdinalIgnoreCase))
                {
                    cleanSuffix = cleanSuffix.Substring(0, cleanSuffix.Length - 3);
                }

                string[] commonPrefixes = new string[] { "Gaming", "HID", "FDAS", "Valkyrie", "Rapoo", "VGN", "Zaopin", "Logitech", "Apple", "AppleHID", "AppleBT", "BT", "BLE", "HyperX", "Flydigi", "MCHOSE", "Sony", "SteelSeries" };
                for (int i = 0; i < commonPrefixes.Length; i++)
                {
                    string p = commonPrefixes[i];
                    string cand1 = p + ":" + suffix;
                    if (!variants.Contains(cand1)) variants.Add(cand1);
                    string cand2 = p + ":" + cleanSuffix;
                    if (!variants.Contains(cand2)) variants.Add(cand2);
                }
            }
            return variants;
        }

        public static bool IsDeviceHidden(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return false;
            try
            {
                var variants = GetDeviceIdVariants(deviceId);
                using (var key = Registry.CurrentUser.OpenSubKey(HiddenDevicesRegPath, false))
                {
                    if (key != null)
                    {
                        for (int i = 0; i < variants.Count; i++)
                        {
                            object val = key.GetValue(variants[i]);
                            if (val != null && Convert.ToInt32(val) == 1) return true;
                        }
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/DeviceManager.cs: 已忽略异常", diagEx); }
            return false;
        }

        public static void HideOrDeleteDevice(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return;
            var variants = GetDeviceIdVariants(deviceId);

            bool hidden = false;
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(HiddenDevicesRegPath))
                {
                    if (key != null)
                    {
                        for (int i = 0; i < variants.Count; i++)
                        {
                            key.SetValue(variants[i], 1, RegistryValueKind.DWord);
                        }
                        hidden = true;
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/DeviceManager.cs: 已忽略异常", diagEx); }

            // 只有「隐藏标记」确实写入成功，才继续删除用户配置与优先级排序。
            // 否则会留下半完成状态：设备没被隐藏，但用户的自定义配置和排序已经被清空。
            if (!hidden) return;

            for (int i = 0; i < variants.Count; i++)
            {
                DeleteCustomDevice(variants[i]);
            }

            try
            {
                var prio = GetPriorityList();
                if (prio != null)
                {
                    bool chg = false;
                    for (int i = 0; i < variants.Count; i++)
                    {
                        if (prio.Remove(variants[i])) chg = true;
                    }
                    if (chg) SavePriorityList(prio);
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/DeviceManager.cs: 已忽略异常", diagEx); }
        }

        public static void UnhideDevice(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return;
            var variants = GetDeviceIdVariants(deviceId);

            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(HiddenDevicesRegPath, true))
                {
                    if (key != null)
                    {
                        for (int i = 0; i < variants.Count; i++)
                        {
                            key.DeleteValue(variants[i], false);
                        }
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/DeviceManager.cs: 已忽略异常", diagEx); }
        }

        public static string GetConfiguredPrimaryDeviceId()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(PrimaryDeviceRegKey, false))
                {
                    if (key != null)
                    {
                        return key.GetValue(PrimaryDeviceValName) as string;
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/DeviceManager.cs: 已忽略异常", diagEx); }
            return null;
        }

        public static void SetConfiguredPrimaryDeviceId(string deviceId)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(PrimaryDeviceRegKey))
                {
                    if (key != null && !string.IsNullOrEmpty(deviceId))
                    {
                        key.SetValue(PrimaryDeviceValName, deviceId);
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/DeviceManager.cs: 已忽略异常", diagEx); }
        }

        public static bool IsAutoSwitchEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(PrimaryDeviceRegKey, false))
                {
                    if (key != null)
                    {
                        object v = key.GetValue(AutoSwitchValName);
                        if (v != null) return Convert.ToInt32(v) == 1;
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/DeviceManager.cs: 已忽略异常", diagEx); }
            return true;
        }

        public static void SetAutoSwitchEnabled(bool enabled)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(PrimaryDeviceRegKey))
                {
                    if (key != null)
                    {
                        key.SetValue(AutoSwitchValName, enabled ? 1 : 0);
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/DeviceManager.cs: 已忽略异常", diagEx); }
        }

        public static List<string> GetPriorityList()
        {
            var list = new List<string>();
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(PrimaryDeviceRegKey, false))
                {
                    if (key != null)
                    {
                        string raw = key.GetValue(PriorityOrderValName) as string;
                        if (!string.IsNullOrEmpty(raw))
                        {
                            string[] parts = raw.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var p in parts)
                            {
                                string trimmed = p.Trim();
                                if (!string.IsNullOrEmpty(trimmed) && !list.Contains(trimmed))
                                {
                                    list.Add(trimmed);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/DeviceManager.cs: 已忽略异常", diagEx); }
            return list;
        }

        public static void SavePriorityList(List<string> priorityList)
        {
            if (priorityList == null) return;
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(PrimaryDeviceRegKey))
                {
                    if (key != null)
                    {
                        key.SetValue(PriorityOrderValName, string.Join(",", priorityList.ToArray()));
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/DeviceManager.cs: 已忽略异常", diagEx); }
        }

        public static List<CustomDeviceConfig> LoadCustomDevices()
        {
            var list = new List<CustomDeviceConfig>();
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(CustomDevicesRegPath, false))
                {
                    if (key != null)
                    {
                        foreach (var subName in key.GetSubKeyNames())
                        {
                            try
                            {
                                using (var sub = key.OpenSubKey(subName, false))
                                {
                                    if (sub != null)
                                    {
                                        var cfg = new CustomDeviceConfig();
                                        cfg.DeviceId = sub.GetValue("DeviceId", subName) as string;
                                        cfg.HardwareFingerprint = sub.GetValue("HardwareFingerprint", "") as string;
                                        cfg.CustomName = sub.GetValue("CustomName", "") as string;
                                        cfg.OriginalName = sub.GetValue("OriginalName", "") as string;
                                        cfg.Category = (DeviceCategory)Convert.ToInt32(sub.GetValue("Category", 0));
                                        cfg.Transport = sub.GetValue("Transport", "Bluetooth") as string;
                                        cfg.CachedBattery = Convert.ToInt32(sub.GetValue("CachedBattery", 100));
                                        string ls = sub.GetValue("LastSeen") as string;
                                        DateTime dt;
                                        if (DateTime.TryParse(ls, out dt)) cfg.LastSeen = dt;
                                        else cfg.LastSeen = DateTime.Now;

                                        // Auto-migration for keyboards misclassified as mice or with dummy 100% battery
                                        string lowerCheck = (cfg.DeviceId + " " + cfg.OriginalName + " " + cfg.CustomName).ToLowerInvariant();
                                        bool isKnownKeyboard = lowerCheck.Contains("nuphy") || lowerCheck.Contains("wh80") ||
                                                               lowerCheck.Contains("air75") || lowerCheck.Contains("air96") ||
                                                               lowerCheck.Contains("air60") || lowerCheck.Contains("halo") ||
                                                               lowerCheck.Contains("keychron") || lowerCheck.Contains("keyboard") ||
                                                               (cfg.OriginalName != null && cfg.OriginalName.Contains("键盘")) ||
                                                               (cfg.CustomName != null && cfg.CustomName.Contains("键盘"));

                                        if (isKnownKeyboard && (cfg.Category != DeviceCategory.Keyboard || cfg.CachedBattery == 100))
                                        {
                                            cfg.Category = DeviceCategory.Keyboard;
                                            if (cfg.CachedBattery == 100 && (cfg.Transport.Contains("2.4G") || cfg.Transport.Contains("USB") || cfg.DeviceId.StartsWith("HID:19F5")))
                                            {
                                                cfg.CachedBattery = -1;
                                            }
                                            SaveCustomDevice(cfg);
                                        }

                                        list.Add(cfg);
                                    }
                                }
                            }
                            catch (Exception diagEx) { Diagnostics.Log("Hardware/DeviceManager.cs: 已忽略异常", diagEx); }
                        }
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/DeviceManager.cs: 已忽略异常", diagEx); }
            return list;
        }

        public static void SaveCustomDevice(CustomDeviceConfig dev)
        {
            if (dev == null || string.IsNullOrEmpty(dev.DeviceId)) return;
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(CustomDevicesRegPath + @"\" + dev.DeviceId))
                {
                    if (key != null)
                    {
                        key.SetValue("DeviceId", dev.DeviceId);
                        key.SetValue("HardwareFingerprint", dev.HardwareFingerprint ?? "");
                        key.SetValue("CustomName", dev.CustomName ?? "");
                        key.SetValue("OriginalName", dev.OriginalName ?? "");
                        key.SetValue("Category", (int)dev.Category);
                        key.SetValue("Transport", dev.Transport ?? "Bluetooth");
                        key.SetValue("CachedBattery", dev.CachedBattery);
                        key.SetValue("LastSeen", dev.LastSeen.ToString("o"));
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/DeviceManager.cs: 已忽略异常", diagEx); }
        }

        public static void DeleteCustomDevice(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(CustomDevicesRegPath, true))
                {
                    if (key != null)
                    {
                        // 必须用 throwOnMissingSubKey=false：DeleteCustomDevice 会对同一设备的
                        // 十多个 ID 变体逐个调用，绝大多数变体并不存在，单参重载会逐个抛 ArgumentException
                        // （实测每次删除设备产生 12+ 条异常日志）。
                        key.DeleteSubKeyTree(deviceId, false);
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/DeviceManager.cs: 已忽略异常", diagEx); }

            try
            {
                var prio = GetPriorityList();
                if (prio != null && prio.Remove(deviceId))
                {
                    SavePriorityList(prio);
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/DeviceManager.cs: 已忽略异常", diagEx); }

            try
            {
                string p = GetConfiguredPrimaryDeviceId();
                if (string.Equals(p, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    SetConfiguredPrimaryDeviceId("");
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/DeviceManager.cs: 已忽略异常", diagEx); }
        }

        public static void CleanupDuplicateLogitechDevices()
        {
            try
            {
                var customConfigs = LoadCustomDevices();
                foreach (var c in customConfigs)
                {
                    if (c.DeviceId != null && c.DeviceId.StartsWith("HID:046D:", StringComparison.OrdinalIgnoreCase))
                    {
                        DeleteCustomDevice(c.DeviceId);
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/DeviceManager.cs: 已忽略异常", diagEx); }
        }



        public static void CleanupDuplicateNuphyDevices()
        {
            try
            {
                var customConfigs = LoadCustomDevices();
                CustomDeviceConfig dongleCfg = null;
                CustomDeviceConfig wiredCfg = null;
                foreach (var c in customConfigs)
                {
                    if (c.DeviceId.Equals("HID:19F5:8F01", StringComparison.OrdinalIgnoreCase)) dongleCfg = c;
                    else if (c.DeviceId.Equals("HID:19F5:A011", StringComparison.OrdinalIgnoreCase)) wiredCfg = c;
                }
                if (dongleCfg != null && wiredCfg != null)
                {
                    DeleteCustomDevice("HID:19F5:A011");
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/DeviceManager.cs: 已忽略异常", diagEx); }
        }

        public static void CleanupDuplicateRazerDevices()
        {
            try
            {
                var customConfigs = LoadCustomDevices();
                foreach (var c in customConfigs)
                {
                    if (c.DeviceId != null && c.DeviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase))
                    {
                        string normId = RazerDeviceHelper.NormalizeRazerDeviceId(c.DeviceId);
                        if (!c.DeviceId.Equals(normId, StringComparison.OrdinalIgnoreCase))
                        {
                            DeleteCustomDevice(c.DeviceId);
                            c.DeviceId = normId;
                            if (string.IsNullOrEmpty(c.HardwareFingerprint)) c.HardwareFingerprint = normId;
                            SaveCustomDevice(c);
                        }
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/DeviceManager.cs: 已忽略异常", diagEx); }
        }

        public static void CleanupDuplicateValkyrieDevices()
        {
            try
            {
                var customConfigs = LoadCustomDevices();
                foreach (var c in customConfigs)
                {
                    if (c.DeviceId != null && (c.DeviceId.StartsWith("HID:374A:", StringComparison.OrdinalIgnoreCase) || c.DeviceId.StartsWith("HID:347A:", StringComparison.OrdinalIgnoreCase)))
                    {
                        DeleteCustomDevice(c.DeviceId);
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/DeviceManager.cs: 已忽略异常", diagEx); }
        }

        public static void CleanupDuplicateRapooDevices()
        {
            try
            {
                var customConfigs = LoadCustomDevices();
                foreach (var c in customConfigs)
                {
                    if (c.DeviceId != null && (c.DeviceId.StartsWith("Rapoo:24AE:", StringComparison.OrdinalIgnoreCase) || c.DeviceId.StartsWith("HID:24AE:", StringComparison.OrdinalIgnoreCase)))
                    {
                        DeleteCustomDevice(c.DeviceId);
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/DeviceManager.cs: 已忽略异常", diagEx); }
        }

        public static void CleanupDuplicateSteelSeriesDevices()
        {
            try
            {
                var customConfigs = LoadCustomDevices();
                foreach (var c in customConfigs)
                {
                    if (c.DeviceId != null && (c.DeviceId.StartsWith("HID:1038:2232", StringComparison.OrdinalIgnoreCase) ||
                                               c.DeviceId.StartsWith("HID:1038:2253", StringComparison.OrdinalIgnoreCase)))
                    {
                        DeleteCustomDevice(c.DeviceId);
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/DeviceManager.cs: 已忽略异常", diagEx); }
        }


        public static void CleanupDuplicateHyperXDevices()
        {
            try
            {
                var customConfigs = LoadCustomDevices();
                foreach (var c in customConfigs)
                {
                    if (c.DeviceId != null && (c.DeviceId.StartsWith("HyperX:03F0:", StringComparison.OrdinalIgnoreCase) || 
                                              c.DeviceId.StartsWith("HID:03F0:05B7", StringComparison.OrdinalIgnoreCase) ||
                                              c.DeviceId.StartsWith("HID:03F0:0C9D", StringComparison.OrdinalIgnoreCase)))
                    {
                        DeleteCustomDevice(c.DeviceId);
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/DeviceManager.cs: 已忽略异常", diagEx); }
        }

        public static void UpdateCustomDeviceBattery(string deviceId, int battery, DateTime lastSeen)
        {
            if (string.IsNullOrEmpty(deviceId)) return;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(CustomDevicesRegPath + @"\" + deviceId, true))
                {
                    if (key != null)
                    {
                        key.SetValue("CachedBattery", battery);
                        key.SetValue("LastSeen", lastSeen.ToString("o"));
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/DeviceManager.cs: 已忽略异常", diagEx); }
        }

        private static void CleanupPhantomAppleDevices()
        {
            try
            {
                var paired = AppleDeviceHelper.GetPairedAppleAudioDevices();
                bool hasPaired = (paired != null && paired.Count > 0);

                if (!hasPaired)
                {
                    // 本机没有配对任何苹果音频设备，彻底清理 CustomDevices 和 PriorityList 中的误扫幽灵残留
                    using (var rootKey = Registry.CurrentUser.OpenSubKey(CustomDevicesRegPath, true))
                    {
                        if (rootKey != null)
                        {
                            string[] subNames = rootKey.GetSubKeyNames();
                            foreach (string sub in subNames)
                            {
                                if (sub.StartsWith("Apple:", StringComparison.OrdinalIgnoreCase) ||
                                    sub.StartsWith("AppleBT:", StringComparison.OrdinalIgnoreCase) ||
                                    sub.StartsWith("BLE:004C:", StringComparison.OrdinalIgnoreCase))
                                {
                                    rootKey.DeleteSubKeyTree(sub, false);
                                }
                            }
                        }
                    }

                    var prio = GetPriorityList();
                    if (prio != null)
                    {
                        bool changed = false;
                        for (int i = prio.Count - 1; i >= 0; i--)
                        {
                            if (prio[i].StartsWith("Apple:", StringComparison.OrdinalIgnoreCase) ||
                                prio[i].StartsWith("AppleBT:", StringComparison.OrdinalIgnoreCase) ||
                                prio[i].StartsWith("BLE:004C:", StringComparison.OrdinalIgnoreCase))
                            {
                                prio.RemoveAt(i);
                                changed = true;
                            }
                        }
                        if (changed) SavePriorityList(prio);
                    }
                }
                else
                {
                    // 深度清洗：抹平历史遗留的 85% / 90% 虚假硬编码电量缓存
                    using (var rootKey = Registry.CurrentUser.OpenSubKey(CustomDevicesRegPath, true))
                    {
                        if (rootKey != null)
                        {
                            string[] subNames = rootKey.GetSubKeyNames();
                            foreach (string sub in subNames)
                            {
                                if (sub.StartsWith("Apple:", StringComparison.OrdinalIgnoreCase) ||
                                    sub.StartsWith("AppleBT:", StringComparison.OrdinalIgnoreCase) ||
                                    sub.StartsWith("AppleHID:", StringComparison.OrdinalIgnoreCase))
                                {
                                    using (var devKey = rootKey.OpenSubKey(sub, true))
                                    {
                                        if (devKey != null)
                                        {
                                            object bVal = devKey.GetValue("CachedBattery");
                                            if (bVal != null)
                                            {
                                                int b = Convert.ToInt32(bVal);
                                                if (b == 85 || b == 90)
                                                {
                                                    devKey.SetValue("CachedBattery", -1);
                                                }
                                            }
                                            string origName = devKey.GetValue("OriginalName") as string;
                                            string custName = devKey.GetValue("CustomName") as string;
                                            if (!string.IsNullOrEmpty(origName) && origName.IndexOf("iAP", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                devKey.DeleteValue("OriginalName", false);
                                            }
                                            if (!string.IsNullOrEmpty(custName) && custName.IndexOf("iAP", StringComparison.OrdinalIgnoreCase) >= 0)
                                            {
                                                devKey.DeleteValue("CustomName", false);
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/DeviceManager.cs: 已忽略异常", diagEx); }
        }

        public static List<MouseBatteryInfo> QueryAllDevices()
        {
            CleanupDuplicateNuphyDevices();

            CleanupDuplicateLogitechDevices();
            CleanupDuplicateRazerDevices();
            CleanupDuplicateValkyrieDevices();
            CleanupDuplicateRapooDevices();
            CleanupDuplicateHyperXDevices();

            CleanupDuplicateSteelSeriesDevices();
            CleanupPhantomAppleDevices();

            List<MouseBatteryInfo> list = new List<MouseBatteryInfo>();

            // 1. Query Razer
            try
            {
                var razerList = RazerDeviceHelper.QueryAllRazerDevices();
                if (razerList != null && razerList.Count > 0)
                {
                    foreach (var rz in razerList)
                    {
                        if (rz != null && (rz.IsConnected || rz.IsDonglePresent))
                        {
                            rz.IsCustom = false;
                            list.Add(rz);
                        }
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("QueryAllDevices 分支失败（该品牌设备可能因此不显示）", diagEx); }

            // 2. Query Rapoo
            try
            {
                var rapoo = RapooDeviceHelper.QueryRapooDeviceInfo();
                if (rapoo != null && (rapoo.IsConnected || rapoo.IsDonglePresent))
                {
                    rapoo.Category = DeviceCategory.Mouse;
                    rapoo.IsCustom = false;
                    list.Add(rapoo);
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("QueryAllDevices 分支失败（该品牌设备可能因此不显示）", diagEx); }

            // 2.5 Query VGN
            try
            {
                var vgn = VgnDeviceHelper.QueryVgnDeviceInfo();
                if (vgn != null && (vgn.IsConnected || vgn.IsDonglePresent))
                {
                    vgn.Category = DeviceCategory.Mouse;
                    vgn.IsCustom = false;
                    list.Add(vgn);
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("QueryAllDevices 分支失败（该品牌设备可能因此不显示）", diagEx); }

            // 2.6 Query NuPhy
            try
            {
                var nuphy = NuphyDeviceHelper.QueryNuphyDeviceInfo();
                if (nuphy != null && (nuphy.IsConnected || nuphy.IsDonglePresent))
                {
                    nuphy.Category = DeviceCategory.Keyboard;
                    nuphy.IsCustom = false;
                    list.Add(nuphy);
                    if (nuphy.BatteryPercent >= 0)
                    {
                        UpdateCustomDeviceBattery(nuphy.DeviceId, nuphy.BatteryPercent, DateTime.Now);
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("QueryAllDevices 分支失败（该品牌设备可能因此不显示）", diagEx); }



            // 2.7 Query Logitech (Lightspeed, Unifying, Nano, Wired)
            try
            {
                var logiDevices = LogitechDeviceHelper.QueryAllLogitechDevices();
                if (logiDevices != null && logiDevices.Count > 0)
                {
                    foreach (var dev in logiDevices)
                    {
                        if (dev != null && (dev.IsConnected || dev.IsDonglePresent))
                        {
                            dev.IsCustom = false;
                            list.Add(dev);
                            if (dev.BatteryPercent >= 0)
                            {
                                UpdateCustomDeviceBattery(dev.DeviceId, dev.BatteryPercent, DateTime.Now);
                            }
                        }
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("QueryAllDevices 分支失败（该品牌设备可能因此不显示）", diagEx); }

            // 2.8 Query Valkyrie (Keyboards, Mice, 2.4G Dongles)
            try
            {
                var vkDevices = ValkyrieDeviceHelper.QueryAllValkyrieDevices();
                if (vkDevices != null && vkDevices.Count > 0)
                {
                    foreach (var dev in vkDevices)
                    {
                        if (dev != null && (dev.IsConnected || dev.IsDonglePresent))
                        {
                            dev.IsCustom = false;
                            list.Add(dev);
                            if (dev.BatteryPercent >= 0)
                            {
                                UpdateCustomDeviceBattery(dev.DeviceId, dev.BatteryPercent, DateTime.Now);
                            }
                        }
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("QueryAllDevices 分支失败（该品牌设备可能因此不显示）", diagEx); }

            // 2.9 Query Universal Gaming Devices (OpenMouse & All-in-One: Lamzu, Vaxee, WLmouse, Pulsar, ATK/VXE, Attack Shark, etc.)
            try
            {
                var gamingDevices = UniversalGamingDeviceHelper.QueryAllGamingDevices();
                if (gamingDevices != null && gamingDevices.Count > 0)
                {
                    foreach (var dev in gamingDevices)
                    {
                        if (dev != null && (dev.IsConnected || dev.IsDonglePresent))
                        {
                            bool alreadyInList = false;
                            for (int i = 0; i < list.Count; i++)
                            {
                                if (list[i].DeviceId.Equals(dev.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                                    (!string.IsNullOrEmpty(list[i].HardwareFingerprint) && list[i].HardwareFingerprint.Equals(dev.HardwareFingerprint, StringComparison.OrdinalIgnoreCase)))
                                {
                                    alreadyInList = true;
                                    break;
                                }
                            }

                            if (!alreadyInList)
                            {
                                dev.IsCustom = false;
                                list.Add(dev);
                                if (dev.BatteryPercent >= 0)
                                {
                                    UpdateCustomDeviceBattery(dev.DeviceId, dev.BatteryPercent, DateTime.Now);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("QueryAllDevices 分支失败（该品牌设备可能因此不显示）", diagEx); }

            // 2.95 Query Flydigi (APEX 6 / APEX 6 Pro / Vader 5 Pro gamepads)
            try
            {
                var flydigiDevices = FlydigiDeviceHelper.QueryAllFlydigiDevices();
                if (flydigiDevices != null && flydigiDevices.Count > 0)
                {
                    foreach (var dev in flydigiDevices)
                    {
                        if (dev != null && (dev.IsConnected || dev.IsDonglePresent))
                        {
                            bool alreadyInList = false;
                            for (int i = 0; i < list.Count; i++)
                            {
                                if (list[i].DeviceId.Equals(dev.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                                    (!string.IsNullOrEmpty(list[i].HardwareFingerprint) && list[i].HardwareFingerprint.Equals(dev.HardwareFingerprint, StringComparison.OrdinalIgnoreCase)))
                                {
                                    alreadyInList = true;
                                    break;
                                }
                            }

                            if (!alreadyInList)
                            {
                                dev.IsCustom = false;
                                list.Add(dev);
                                if (dev.BatteryPercent >= 0)
                                {
                                    UpdateCustomDeviceBattery(dev.DeviceId, dev.BatteryPercent, DateTime.Now);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("QueryAllDevices 分支失败（该品牌设备可能因此不显示）", diagEx); }

            // 2.96 Query MCHOSE (G87 V2 tri-mode mechanical keyboard)
            try
            {
                var mchoseDevices = MchoseDeviceHelper.QueryAllMchoseDevices();
                if (mchoseDevices != null && mchoseDevices.Count > 0)
                {
                    foreach (var dev in mchoseDevices)
                    {
                        if (dev != null && (dev.IsConnected || dev.IsDonglePresent))
                        {
                            bool alreadyInList = false;
                            for (int i = 0; i < list.Count; i++)
                            {
                                if (list[i].DeviceId.Equals(dev.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                                    (!string.IsNullOrEmpty(list[i].HardwareFingerprint) && list[i].HardwareFingerprint.Equals(dev.HardwareFingerprint, StringComparison.OrdinalIgnoreCase)))
                                {
                                    alreadyInList = true;
                                    break;
                                }
                            }

                            if (!alreadyInList)
                            {
                                dev.IsCustom = false;
                                list.Add(dev);
                                if (dev.BatteryPercent >= 0)
                                {
                                    UpdateCustomDeviceBattery(dev.DeviceId, dev.BatteryPercent, DateTime.Now);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("QueryAllDevices 分支失败（该品牌设备可能因此不显示）", diagEx); }

            // 2.965 Query MCHOSE Mouse (A5 V2 Ultra tri-mode gaming mouse)
            try
            {
                var mchoseMice = MchoseMouseHelper.QueryAllMchoseMice();
                if (mchoseMice != null && mchoseMice.Count > 0)
                {
                    foreach (var dev in mchoseMice)
                    {
                        if (dev != null && (dev.IsConnected || dev.IsDonglePresent))
                        {
                            bool alreadyInList = false;
                            for (int i = 0; i < list.Count; i++)
                            {
                                if (list[i].DeviceId.Equals(dev.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                                    (!string.IsNullOrEmpty(list[i].HardwareFingerprint) && list[i].HardwareFingerprint.Equals(dev.HardwareFingerprint, StringComparison.OrdinalIgnoreCase)))
                                {
                                    alreadyInList = true;
                                    break;
                                }
                            }

                            if (!alreadyInList)
                            {
                                dev.IsCustom = false;
                                list.Add(dev);
                                if (dev.BatteryPercent >= 0)
                                {
                                    UpdateCustomDeviceBattery(dev.DeviceId, dev.BatteryPercent, DateTime.Now);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("QueryAllDevices 分支失败（该品牌设备可能因此不显示）", diagEx); }

            // 2.97 Query Sony (INZONE Buds WF-G700N wireless earbuds)
            try
            {
                var sonyDevices = SonyInzoneHelper.QueryAllSonyInzoneDevices();
                if (sonyDevices != null && sonyDevices.Count > 0)
                {
                    foreach (var dev in sonyDevices)
                    {
                        if (dev != null && (dev.IsConnected || dev.IsDonglePresent))
                        {
                            bool alreadyInList = false;
                            for (int i = 0; i < list.Count; i++)
                            {
                                if (list[i].DeviceId.Equals(dev.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                                    (!string.IsNullOrEmpty(list[i].HardwareFingerprint) && list[i].HardwareFingerprint.Equals(dev.HardwareFingerprint, StringComparison.OrdinalIgnoreCase)))
                                {
                                    alreadyInList = true;
                                    break;
                                }
                            }

                            if (!alreadyInList)
                            {
                                dev.IsCustom = false;
                                list.Add(dev);
                                if (dev.BatteryPercent >= 0)
                                {
                                    UpdateCustomDeviceBattery(dev.DeviceId, dev.BatteryPercent, DateTime.Now);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("QueryAllDevices 分支失败（该品牌设备可能因此不显示）", diagEx); }

            // 2.10 Query Apple & Beats Devices (AirPods, Beats Studio/Fit/Solo, Magic Mouse/Trackpad/Keyboard)
            try
            {
                var appleDevices = AppleDeviceHelper.QueryAllAppleDevices();
                if (appleDevices != null && appleDevices.Count > 0)
                {
                    foreach (var dev in appleDevices)
                    {
                        if (dev != null && (dev.IsConnected || dev.IsDonglePresent))
                        {
                            bool alreadyInList = false;
                            for (int i = 0; i < list.Count; i++)
                            {
                                if (list[i].DeviceId.Equals(dev.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                                    (!string.IsNullOrEmpty(list[i].HardwareFingerprint) && list[i].HardwareFingerprint.Equals(dev.HardwareFingerprint, StringComparison.OrdinalIgnoreCase)))
                                {
                                    alreadyInList = true;
                                    break;
                                }
                            }

                            if (!alreadyInList)
                            {
                                dev.IsCustom = false;
                                list.Add(dev);
                                if (dev.BatteryPercent >= 0)
                                {
                                    UpdateCustomDeviceBattery(dev.DeviceId, dev.BatteryPercent, DateTime.Now);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("QueryAllDevices 分支失败（该品牌设备可能因此不显示）", diagEx); }

            // 2.11 Query HyperX Devices (Cloud III Wireless, etc.)
            try
            {
                var hp = HyperXDeviceHelper.QueryHyperXDeviceInfo();
                if (hp != null && (hp.IsConnected || hp.IsDonglePresent))
                {
                    bool alreadyInList = false;
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i].DeviceId.Equals(hp.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrEmpty(list[i].HardwareFingerprint) && list[i].HardwareFingerprint.Equals(hp.HardwareFingerprint, StringComparison.OrdinalIgnoreCase)))
                        {
                            alreadyInList = true;
                            break;
                        }
                    }

                    if (!alreadyInList)
                    {
                        hp.IsCustom = false;
                        list.Add(hp);
                        if (hp.BatteryPercent >= 0)
                        {
                            UpdateCustomDeviceBattery(hp.DeviceId, hp.BatteryPercent, DateTime.Now);
                        }
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("QueryAllDevices 分支失败（该品牌设备可能因此不显示）", diagEx); }

                        // 2.12 Query SteelSeries Devices (Arctis Nova 5 / 5X Wireless Headset)
            try
            {
                var ss = SteelSeriesDeviceHelper.QuerySteelSeriesDeviceInfo();
                if (ss != null && (ss.IsConnected || ss.IsDonglePresent))
                {
                    bool alreadyInList = false;
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i].DeviceId.Equals(ss.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrEmpty(list[i].HardwareFingerprint) && list[i].HardwareFingerprint.Equals(ss.HardwareFingerprint, StringComparison.OrdinalIgnoreCase)))
                        {
                            alreadyInList = true;
                            break;
                        }
                    }

                    if (!alreadyInList)
                    {
                        ss.IsCustom = false;
                        list.Add(ss);
                        if (ss.BatteryPercent >= 0)
                        {
                            UpdateCustomDeviceBattery(ss.DeviceId, ss.BatteryPercent, DateTime.Now);
                        }
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("QueryAllDevices 分支失败（该品牌设备可能因此不显示）", diagEx); }

            // 2.13 Query Razer wireless headsets (BlackShark V2 Pro 2.4, 64B PA/PI protocol)
            try
            {
                var razerHeadsets = RazerHeadsetHelper.QueryAllRazerHeadsets();
                if (razerHeadsets != null && razerHeadsets.Count > 0)
                {
                    foreach (var dev in razerHeadsets)
                    {
                        if (dev != null && (dev.IsConnected || dev.IsDonglePresent))
                        {
                            bool alreadyInList = false;
                            for (int i = 0; i < list.Count; i++)
                            {
                                if (list[i].DeviceId.Equals(dev.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                                    (!string.IsNullOrEmpty(list[i].HardwareFingerprint) && list[i].HardwareFingerprint.Equals(dev.HardwareFingerprint, StringComparison.OrdinalIgnoreCase)))
                                {
                                    alreadyInList = true;
                                    break;
                                }
                            }

                            if (!alreadyInList)
                            {
                                dev.IsCustom = false;
                                list.Add(dev);
                                if (dev.BatteryPercent >= 0)
                                {
                                    UpdateCustomDeviceBattery(dev.DeviceId, dev.BatteryPercent, DateTime.Now);
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("QueryAllDevices 分支失败（该品牌设备可能因此不显示）", diagEx); }


            // 2.14 Query FDAS Runtime Dynamic Devices (Local adapters/*.fp-adapter.json)
            try
            {
                var fdasDevices = FdasRuntimeHelper.QueryAllFdasDevices();
                if (fdasDevices != null && fdasDevices.Count > 0)
                {
                    foreach (var dev in fdasDevices)
                    {
                        if (dev != null && dev.IsConnected)
                        {
                            var variants = GetDeviceIdVariants(dev.DeviceId);
                            int existingIdx = -1;
                            for (int i = 0; i < list.Count; i++)
                            {
                                if (list[i].DeviceId.Equals(dev.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                                    (!string.IsNullOrEmpty(list[i].HardwareFingerprint) && list[i].HardwareFingerprint.Equals(dev.HardwareFingerprint, StringComparison.OrdinalIgnoreCase)))
                                {
                                    existingIdx = i;
                                    break;
                                }
                                for (int vi = 0; vi < variants.Count; vi++)
                                {
                                    if (list[i].DeviceId.Equals(variants[vi], StringComparison.OrdinalIgnoreCase) ||
                                        (!string.IsNullOrEmpty(list[i].HardwareFingerprint) && list[i].HardwareFingerprint.Equals(variants[vi], StringComparison.OrdinalIgnoreCase)))
                                    {
                                        existingIdx = i;
                                        break;
                                    }
                                }
                                if (existingIdx >= 0) break;
                            }

                            if (existingIdx >= 0)
                            {
                                var oldDev = list[existingIdx];
                                dev.IsCustom = oldDev.IsCustom;
                                if (!string.IsNullOrEmpty(oldDev.CustomName)) dev.CustomName = oldDev.CustomName;
                                list[existingIdx] = dev;
                            }
                            else
                            {
                                dev.IsCustom = false;
                                list.Add(dev);
                            }

                            if (dev.BatteryPercent >= 0)
                            {
                                UpdateCustomDeviceBattery(dev.DeviceId, dev.BatteryPercent, DateTime.Now);
                            }
                        }
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("QueryAllDevices 分支失败（该品牌设备可能因此不显示）", diagEx); }

            // 3. Query Custom Devices (Bluetooth / Generic HID)
            try
            {
                var customConfigs = LoadCustomDevices();
                foreach (var cfg in customConfigs)
                {
                    bool isLogitech = (cfg.DeviceId != null && (cfg.DeviceId.StartsWith("Logitech:046D:", StringComparison.OrdinalIgnoreCase) || cfg.DeviceId.IndexOf("046D", StringComparison.OrdinalIgnoreCase) >= 0)) ||
                                      (!string.IsNullOrEmpty(cfg.HardwareFingerprint) && cfg.HardwareFingerprint.IndexOf("046D", StringComparison.OrdinalIgnoreCase) >= 0);



                    bool isNuphy = NuphyDeviceHelper.IsNuphyDevice(cfg.HardwareFingerprint) ||
                                   NuphyDeviceHelper.IsNuphyDevice(cfg.DeviceId) ||
                                   NuphyDeviceHelper.IsNuphyDevice(cfg.OriginalName);

                    bool isValkyrie = ValkyrieDeviceHelper.IsValkyrieDevice(cfg.HardwareFingerprint) ||
                                      ValkyrieDeviceHelper.IsValkyrieDevice(cfg.DeviceId) ||
                                      ValkyrieDeviceHelper.IsValkyrieDevice(cfg.OriginalName);

                    bool isRapoo = (cfg.DeviceId != null && (cfg.DeviceId.StartsWith("Rapoo:24AE:", StringComparison.OrdinalIgnoreCase) || cfg.DeviceId.StartsWith("HID:24AE:", StringComparison.OrdinalIgnoreCase))) ||
                                   (!string.IsNullOrEmpty(cfg.HardwareFingerprint) && cfg.HardwareFingerprint.IndexOf("24AE", StringComparison.OrdinalIgnoreCase) >= 0);

                    bool isHyperX = HyperXDeviceHelper.IsHyperXDevice(cfg.HardwareFingerprint) ||
                                    HyperXDeviceHelper.IsHyperXDevice(cfg.DeviceId) ||
                                    HyperXDeviceHelper.IsHyperXDevice(cfg.OriginalName);



                    bool isSteelSeries = SteelSeriesDeviceHelper.IsSteelSeriesDevice(cfg.HardwareFingerprint) ||

                                         SteelSeriesDeviceHelper.IsSteelSeriesDevice(cfg.DeviceId) ||

                                         SteelSeriesDeviceHelper.IsSteelSeriesDevice(cfg.OriginalName);

                    bool isRazer = (cfg.DeviceId != null && cfg.DeviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase)) ||
                                   (!string.IsNullOrEmpty(cfg.HardwareFingerprint) && cfg.HardwareFingerprint.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase));

                    string normCfgId = isRazer ? RazerDeviceHelper.NormalizeRazerDeviceId(cfg.DeviceId) : cfg.DeviceId;
                    string normFpId = (isRazer && !string.IsNullOrEmpty(cfg.HardwareFingerprint)) ? RazerDeviceHelper.NormalizeRazerDeviceId(cfg.HardwareFingerprint) : cfg.HardwareFingerprint;

                    // Check if already in list (either direct DeviceId match, variant match across prefixes, or normalized Razer match)
                    var existDev = list.Find(x => {
                        if (x.DeviceId.Equals(cfg.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrEmpty(cfg.HardwareFingerprint) && x.DeviceId.Equals(cfg.HardwareFingerprint, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrEmpty(x.HardwareFingerprint) && x.HardwareFingerprint.Equals(cfg.DeviceId, StringComparison.OrdinalIgnoreCase)))
                        {
                            return true;
                        }
                        var variants = GetDeviceIdVariants(x.DeviceId);
                        for (int vi = 0; vi < variants.Count; vi++)
                        {
                            if (variants[vi].Equals(cfg.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                                (!string.IsNullOrEmpty(cfg.HardwareFingerprint) && variants[vi].Equals(cfg.HardwareFingerprint, StringComparison.OrdinalIgnoreCase)))
                            {
                                return true;
                            }
                        }
                        if (isRazer && (x.Brand == "Razer" || (x.DeviceId != null && x.DeviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase))))
                        {
                            string normX = RazerDeviceHelper.NormalizeRazerDeviceId(x.DeviceId);
                            return normX.Equals(normCfgId, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(normFpId) && normX.Equals(normFpId, StringComparison.OrdinalIgnoreCase));
                        }
                        return false;
                    });

                    if (existDev != null)
                    {
                        existDev.IsCustom = true;
                        if (cfg.Category != DeviceCategory.Generic)
                        {
                            existDev.Category = cfg.Category;
                        }
                        if (!string.IsNullOrEmpty(cfg.CustomName))
                        {
                            bool keepOfficialName = (existDev.Brand == "Flydigi" && cfg.CustomName.IndexOf("Controller", StringComparison.OrdinalIgnoreCase) >= 0) ||
                                                    (existDev.Brand == "MCHOSE" && cfg.CustomName.IndexOf("2.4G", StringComparison.OrdinalIgnoreCase) >= 0) || (existDev.Brand == "Sony" && cfg.CustomName.IndexOf("INZONE", StringComparison.OrdinalIgnoreCase) >= 0);
                            if (keepOfficialName)
                            {
                                cfg.CustomName = existDev.DeviceName;
                                cfg.OriginalName = existDev.DeviceName;
                                DeviceManager.SaveCustomDevice(cfg);
                            }
                            else if (isRazer && (
                                cfg.CustomName.Equals("Razer Mouse", StringComparison.OrdinalIgnoreCase) ||
                                cfg.CustomName.Equals("Razer Wireless Mouse", StringComparison.OrdinalIgnoreCase) ||
                                cfg.CustomName.Equals("HyperPolling Wireless Dongle", StringComparison.OrdinalIgnoreCase) ||
                                cfg.CustomName.Equals("Razer HyperPolling Wireless Dongle", StringComparison.OrdinalIgnoreCase) ||
                                cfg.CustomName.Equals("雷蛇无线鼠标", StringComparison.OrdinalIgnoreCase) ||
                                cfg.CustomName.Equals("雷蛇电竞鼠标", StringComparison.OrdinalIgnoreCase) ||
                                cfg.CustomName.Equals("雷蛇无线设备", StringComparison.OrdinalIgnoreCase)))
                            {
                                cfg.CustomName = existDev.DisplayName;
                                cfg.OriginalName = existDev.DeviceName;
                                DeviceManager.SaveCustomDevice(cfg);
                            }
                            if (!keepOfficialName) existDev.CustomName = cfg.CustomName;
                        }
                        continue;
                    }

                    if (isLogitech && list.Exists(x => x.Brand == "Logitech" || x.Brand == "罗技"))
                    {
                        var existLogi = list.Find(x => x.Brand == "Logitech" || x.Brand == "罗技");
                        if (existLogi != null)
                        {
                            existLogi.IsCustom = true;
                            if (cfg.Category != DeviceCategory.Generic) existLogi.Category = cfg.Category;
                            if (!string.IsNullOrEmpty(cfg.CustomName)) existLogi.CustomName = cfg.CustomName;
                        }
                        continue;
                    }

                    if (isNuphy && list.Exists(x => x.Brand == "NuPhy"))
                    {
                        var existNuphy = list.Find(x => x.Brand == "NuPhy");
                        if (existNuphy != null)
                        {
                            existNuphy.IsCustom = true;
                            if (cfg.Category != DeviceCategory.Generic) existNuphy.Category = cfg.Category;
                            if (!string.IsNullOrEmpty(cfg.CustomName)) existNuphy.CustomName = cfg.CustomName;
                        }
                        continue;
                    }

                    if (isValkyrie)
                    {
                        bool isKb = (cfg.DeviceId != null && cfg.DeviceId.EndsWith(":KB")) || cfg.Category == DeviceCategory.Keyboard;
                        var existVk = list.Find(x => (x.Brand == "Valkyrie" || x.Brand == "瓦尔基里") &&
                            (isKb ? (x.Category == DeviceCategory.Keyboard || (x.DeviceId != null && x.DeviceId.EndsWith(":KB")))
                                  : (x.Category != DeviceCategory.Keyboard && (x.DeviceId == null || !x.DeviceId.EndsWith(":KB")))));
                        if (existVk != null)
                        {
                            existVk.IsCustom = true;
                            if (cfg.Category != DeviceCategory.Generic) existVk.Category = cfg.Category;
                            if (!string.IsNullOrEmpty(cfg.CustomName)) existVk.CustomName = cfg.CustomName;
                            continue;
                        }
                    }

                    if (isRapoo && list.Exists(x => x.Brand == "Rapoo" || x.Brand == "雷柏"))
                    {
                        var existRapoo = list.Find(x => x.Brand == "Rapoo" || x.Brand == "雷柏");
                        if (existRapoo != null)
                        {
                            existRapoo.IsCustom = true;
                            if (cfg.Category != DeviceCategory.Generic) existRapoo.Category = cfg.Category;
                            if (!string.IsNullOrEmpty(cfg.CustomName)) existRapoo.CustomName = cfg.CustomName;
                        }
                        continue;
                    }

                    if (isHyperX && list.Exists(x => x.Brand == "HyperX"))
                    {
                        var existHp = list.Find(x => x.Brand == "HyperX");
                        if (existHp != null)
                        {
                            existHp.IsCustom = true;
                            if (cfg.Category != DeviceCategory.Generic) existHp.Category = cfg.Category;
                            if (!string.IsNullOrEmpty(cfg.CustomName)) existHp.CustomName = cfg.CustomName;
                        }
                        continue;
                    }



                    if (isSteelSeries && list.Exists(x => x.Brand == "SteelSeries"))
                    {
                        var existSs = list.Find(x => x.Brand == "SteelSeries");
                        if (existSs != null)
                        {
                            existSs.IsCustom = true;
                            if (cfg.Category != DeviceCategory.Generic) existSs.Category = cfg.Category;
                            if (!string.IsNullOrEmpty(cfg.CustomName)) existSs.CustomName = cfg.CustomName;
                        }
                        continue;
                    }

                    int batt = -1;
                    string friendlyName = null;
                    bool connected = false;
                    if (!string.IsNullOrEmpty(cfg.HardwareFingerprint) && cfg.HardwareFingerprint.StartsWith("VGN:", StringComparison.OrdinalIgnoreCase))
                    {
                        var vgn = VgnDeviceHelper.QueryVgnDeviceInfo();
                        if (vgn != null)
                        {
                            connected = vgn.IsConnected;
                            batt = vgn.BatteryPercent;
                            friendlyName = vgn.DeviceName;
                        }
                    }
                    else if (!string.IsNullOrEmpty(cfg.HardwareFingerprint) && cfg.HardwareFingerprint.StartsWith("HID:", StringComparison.OrdinalIgnoreCase))
                    {
                        if (isNuphy)
                        {
                            var nuphy = NuphyDeviceHelper.QueryNuphyDeviceInfo();
                            if (nuphy != null && nuphy.IsConnected && (nuphy.DeviceId.Equals(cfg.DeviceId, StringComparison.OrdinalIgnoreCase) || nuphy.DeviceId.Equals(cfg.HardwareFingerprint, StringComparison.OrdinalIgnoreCase)))
                            {
                                connected = true;
                                batt = nuphy.BatteryPercent;
                                friendlyName = nuphy.DeviceName;
                            }
                            else
                            {
                                connected = false;
                                batt = NuphyDeviceHelper.CachedBatteryPercent > 0 ? NuphyDeviceHelper.CachedBatteryPercent : -1;
                            }
                        }
                        else if (isSteelSeries)
                        {
                            var ss = SteelSeriesDeviceHelper.QuerySteelSeriesDeviceInfo();
                            if (ss != null && (ss.IsConnected || ss.IsDonglePresent))
                            {
                                connected = ss.IsConnected;
                                batt = ss.BatteryPercent;
                                friendlyName = ss.DeviceName;
                            }
                            else
                            {
                                connected = false;
                                batt = SteelSeriesDeviceHelper.CachedBatteryPercent > 0 ? SteelSeriesDeviceHelper.CachedBatteryPercent : -1;
                            }
                        }
                        else if (isLogitech)
                        {
                            var logi = LogitechDeviceHelper.QueryLogitechDeviceInfo(cfg.HardwareFingerprint);
                            if (logi != null && (logi.IsConnected || logi.IsDonglePresent))
                            {
                                connected = logi.IsConnected;
                                batt = logi.BatteryPercent;
                                friendlyName = logi.DeviceName;
                            }
                            else
                            {
                                connected = false;
                                batt = LogitechDeviceHelper.GetCachedBatteryForDevice(cfg.DeviceId);
                            }
                        }
                        else if (isValkyrie)
                        {
                            bool isKb = (cfg.DeviceId != null && cfg.DeviceId.EndsWith(":KB")) || cfg.Category == DeviceCategory.Keyboard;
                            int cachedVk = isKb ? ValkyrieDeviceHelper.CachedKbBattery : ValkyrieDeviceHelper.CachedMouseBattery;
                            connected = isKb ? ValkyrieDeviceHelper.CachedKbOnline : true;
                            batt = cachedVk > 0 ? cachedVk : (cfg.CachedBattery > 0 ? cfg.CachedBattery : -1);
                            friendlyName = isKb ? ValkyrieDeviceHelper.CachedKbName : ValkyrieDeviceHelper.CachedMouseName;
                            if ((batt <= 0 || !connected) && !string.IsNullOrEmpty(cfg.HardwareFingerprint))
                            {
                                int fallbackBatt;
                                bool fbChg;
                                if (GenericHidHelper.QueryHidBattery(cfg.HardwareFingerprint, out fallbackBatt, out fbChg) && fallbackBatt > 0)
                                {
                                    batt = fallbackBatt;
                                    connected = true;
                                    if (isKb)
                                    {
                                        ValkyrieDeviceHelper.CachedKbBattery = batt;
                                        ValkyrieDeviceHelper.CachedKbOnline = true;
                                    }
                                }
                                else if (!connected && GenericHidHelper.IsDeviceConnected(cfg.HardwareFingerprint))
                                {
                                    connected = true;
                                }
                            }
                        }
                        else if (isRapoo)
                        {
                            var rp = RapooDeviceHelper.QueryRapooDeviceInfo();
                            if (rp != null && (rp.IsConnected || rp.IsDonglePresent))
                            {
                                connected = rp.IsConnected;
                                batt = rp.BatteryPercent;
                                friendlyName = rp.DeviceName;
                            }
                            else
                            {
                                connected = false;
                                batt = RapooDeviceHelper.CachedBatteryPercent > 0 ? RapooDeviceHelper.CachedBatteryPercent : -1;
                            }
                        }
                        else if (isHyperX)
                        {
                            var hp = HyperXDeviceHelper.QueryHyperXDeviceInfo();
                            if (hp != null && (hp.IsConnected || hp.IsDonglePresent))
                            {
                                connected = hp.IsConnected;
                                batt = hp.BatteryPercent;
                                friendlyName = hp.DeviceName;
                            }
                            else
                            {
                                connected = false;
                                batt = HyperXDeviceHelper.CachedBatteryPercent > 0 ? HyperXDeviceHelper.CachedBatteryPercent : -1;
                            }
                        }
                        else
                        {
                            bool isChg = false;
                            connected = GenericHidHelper.IsDeviceConnected(cfg.HardwareFingerprint);
                            if (connected)
                            {
                                int readBatt = -1;
                                if (GenericHidHelper.QueryHidBattery(cfg.HardwareFingerprint, out readBatt, out isChg))
                                {
                                    batt = readBatt;
                                }
                                else
                                {
                                    batt = cfg.CachedBattery > 0 ? cfg.CachedBattery : -1;
                                }
                            }
                        }
                    }
                    else if (!string.IsNullOrEmpty(cfg.HardwareFingerprint))
                    {
                        connected = BluetoothDeviceHelper.QueryByMac(cfg.HardwareFingerprint, cfg.OriginalName ?? cfg.CustomName, out batt, out friendlyName);
                    }

                    var devInfo = new MouseBatteryInfo();
                    devInfo.DeviceId = cfg.DeviceId;
                    devInfo.HardwareFingerprint = cfg.HardwareFingerprint;
                    devInfo.Category = cfg.Category;
                    devInfo.CustomName = cfg.CustomName;

                    var fdasDef = FdasRuntimeHelper.FindAdapterByDeviceId(cfg.DeviceId) ?? 
                                  (!string.IsNullOrEmpty(cfg.HardwareFingerprint) ? FdasRuntimeHelper.FindAdapterByDeviceId(cfg.HardwareFingerprint) : null);
                    if (fdasDef != null)
                    {
                        devInfo.Brand = fdasDef.Brand;
                        if (fdasDef.Category != DeviceCategory.Generic) devInfo.Category = fdasDef.Category;
                        devInfo.DeviceName = !string.IsNullOrEmpty(cfg.CustomName) ? cfg.CustomName : (!string.IsNullOrEmpty(cfg.OriginalName) ? cfg.OriginalName : (fdasDef.Brand + " " + fdasDef.Model));
                        if (devInfo.Category == DeviceCategory.Mouse)
                        {
                            devInfo.DpiStages = (fdasDef.DefaultStages != null && fdasDef.DefaultStages.Length > 0) ? fdasDef.DefaultStages : new int[] { 400, 800, 1600, 3200, 6400 };
                            devInfo.DpiStageCount = devInfo.DpiStages.Length;
                            devInfo.Dpi = fdasDef.CurrentDpi > 0 ? fdasDef.CurrentDpi : (devInfo.DpiStages.Length > 2 ? devInfo.DpiStages[2] : devInfo.DpiStages[0]);
                            devInfo.DpiStage = fdasDef.CurrentDpiStage > 0 ? fdasDef.CurrentDpiStage : 3;
                            devInfo.IsXyIndependent = fdasDef.SupportsXyIndependence;
                            devInfo.SupportedPollingRates = (fdasDef.SupportedRates != null && fdasDef.SupportedRates.Length > 0) ? fdasDef.SupportedRates : new int[] { 125, 250, 500, 1000, 2000, 4000, 8000 };
                            devInfo.MaxPollingRate = fdasDef.MaxRate > 0 ? fdasDef.MaxRate : 8000;
                            devInfo.PollingRate = fdasDef.CurrentPollingRate > 0 ? fdasDef.CurrentPollingRate : (fdasDef.ActiveRate > 0 ? fdasDef.ActiveRate : 1000);
                        }
                    }
                    else
                    {
                        devInfo.Brand = isNuphy ? "NuPhy" : (isRazer ? "Razer" : (isLogitech ? "Logitech" : (isValkyrie ? "Valkyrie" : (isRapoo ? "Rapoo" : (isHyperX ? "HyperX" : "Custom")))));
                        devInfo.DeviceName = !string.IsNullOrEmpty(friendlyName) ? friendlyName : (!string.IsNullOrEmpty(cfg.OriginalName) ? cfg.OriginalName : cfg.CustomName);
                    }
                    if (!string.IsNullOrEmpty(devInfo.DeviceName) && devInfo.DeviceName.IndexOf("Wireless iAP", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        devInfo.DeviceName = (devInfo.Brand == "Beats") ? "Beats 蓝牙耳机" : "Apple 音频外设";
                    }
                    if (!string.IsNullOrEmpty(devInfo.CustomName) && devInfo.CustomName.IndexOf("Wireless iAP", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        devInfo.CustomName = null;
                    }
                    // 连接形态优先用设备实时上报值：皂品/VGN 会随「有线直连 / 2.4G 接收器」
                    // 切换而改变 Transport，若用持久化配置覆盖，界面会一直显示添加设备时
                    // 的旧值（实测 2026-10-29：有线连接仍显示「2.4G 无线」）。
                    // 只有实时值为空时才回退到配置/默认值。
                    if (string.IsNullOrEmpty(devInfo.Transport))
                    {
                        devInfo.Transport = cfg.Transport ?? (isRazer ? "2.4G 无线" : (isRapoo ? "2.4G 无线" : (isHyperX ? "2.4G 无线" : "Bluetooth")));
                    }
                    devInfo.IsCustom = true;
                    if (devInfo.Brand == "NuPhy")
                    {
                        if (devInfo.Category == DeviceCategory.Generic) devInfo.Category = DeviceCategory.Keyboard;
                        devInfo.IsCharging = NuphyDeviceHelper.CachedIsCharging;
                    }
                    else if (devInfo.Brand == "HyperX")
                    {
                        if (devInfo.Category == DeviceCategory.Generic) devInfo.Category = DeviceCategory.Headset;
                        devInfo.IsCharging = HyperXDeviceHelper.CachedIsCharging;
                    }
                    else if (devInfo.Brand == "Logitech" || devInfo.Brand == "罗技")
                    {
                        if (devInfo.Category == DeviceCategory.Generic) devInfo.Category = DeviceCategory.Mouse;
                        devInfo.IsCharging = LogitechDeviceHelper.CachedIsCharging;
                    }
                    else if (devInfo.Brand == "Rapoo" || devInfo.Brand == "雷柏")
                    {
                        if (devInfo.Category == DeviceCategory.Generic) devInfo.Category = DeviceCategory.Mouse;
                    }
                    else if (devInfo.Brand == "Valkyrie" || devInfo.Brand == "瓦尔基里")
                    {
                        if (devInfo.Category == DeviceCategory.Generic)
                        {
                            devInfo.Category = ((cfg.DeviceId != null && cfg.DeviceId.EndsWith(":KB")) || (cfg.OriginalName != null && cfg.OriginalName.Contains("键盘"))) ? DeviceCategory.Keyboard : DeviceCategory.Mouse;
                        }
                    }
                    else if (devInfo.Brand == "Razer" || devInfo.Brand == "雷蛇")
                    {
                        int rzPid = 0;
                        string normId = RazerDeviceHelper.NormalizeRazerDeviceId(cfg.DeviceId);
                        if (!string.IsNullOrEmpty(normId) && normId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase))
                        {
                            string hex = normId.Substring("Razer:1532:".Length).Trim();
                            int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out rzPid);
                        }
                        string officialName = RazerDeviceHelper.GetRazerOfficialModelName(rzPid, cfg.OriginalName);
                        string prestigiousDisplayName = RazerDeviceHelper.GetRazerDisplayName(rzPid, officialName);
                        if (devInfo.Category == DeviceCategory.Generic)
                        {
                            devInfo.Category = RazerDeviceHelper.GetRazerDeviceCategory(rzPid, officialName);
                        }

                        if (string.IsNullOrEmpty(cfg.CustomName) ||
                            cfg.CustomName.Equals("Razer Mouse", StringComparison.OrdinalIgnoreCase) ||
                            cfg.CustomName.Equals("Razer Wireless Mouse", StringComparison.OrdinalIgnoreCase) ||
                            cfg.CustomName.Equals("HyperPolling Wireless Dongle", StringComparison.OrdinalIgnoreCase) ||
                            cfg.CustomName.Equals("Razer HyperPolling Wireless Dongle", StringComparison.OrdinalIgnoreCase) ||
                            cfg.CustomName.Equals("雷蛇无线鼠标", StringComparison.OrdinalIgnoreCase) ||
                            cfg.CustomName.Equals("雷蛇电竞鼠标", StringComparison.OrdinalIgnoreCase) ||
                            cfg.CustomName.Equals("雷蛇无线设备", StringComparison.OrdinalIgnoreCase))
                        {
                            cfg.CustomName = prestigiousDisplayName;
                            cfg.OriginalName = officialName;
                            DeviceManager.SaveCustomDevice(cfg);
                        }
                        devInfo.CustomName = cfg.CustomName;
                        devInfo.DeviceName = officialName;

                        var rzState = RazerDeviceHelper.GetDeviceState(cfg.DeviceId);
                        if (rzState != null)
                        {
                            devInfo.IsCharging = false;
                            if (rzState.BatteryPercent > 0)
                            {
                                devInfo.BatteryPercent = rzState.BatteryPercent;
                                devInfo.ExactBatteryPercent = rzState.ExactBatteryPercent;
                            }
                        }
                    }

                    if (connected)
                    {
                        devInfo.IsConnected = true;
                        devInfo.IsSleeping = false;
                        devInfo.IsDonglePresent = true;
                        int effectiveBatt = batt >= 0 ? batt : (cfg.CachedBattery > 0 ? cfg.CachedBattery : -1);
                        devInfo.BatteryPercent = effectiveBatt;
                        devInfo.ExactBatteryPercent = effectiveBatt >= 0 ? effectiveBatt : -1;
                        devInfo.LastUpdated = DateTime.Now;

                        if (batt >= 0)
                        {
                            UpdateCustomDeviceBattery(cfg.DeviceId, batt, DateTime.Now);
                        }
                    }
                    else
                    {
                        // Offline memory feature: retain last-known battery level and sync timestamp!
                        devInfo.IsConnected = false;
                        devInfo.IsSleeping = false;
                        devInfo.IsDonglePresent = false;
                        devInfo.IsCharging = false;
                        if (isRazer)
                        {
                            var rzState = RazerDeviceHelper.GetDeviceState(cfg.DeviceId);
                            int cachedRzBatt = (rzState != null && rzState.BatteryPercent > 0) ? rzState.BatteryPercent : (cfg.CachedBattery > 0 ? cfg.CachedBattery : -1);
                            devInfo.BatteryPercent = cachedRzBatt;
                            devInfo.ExactBatteryPercent = devInfo.BatteryPercent;
                        }
                        else if (isLogitech)
                        {
                            int cachedLogi = LogitechDeviceHelper.GetCachedBatteryForDevice(cfg.DeviceId);
                            devInfo.BatteryPercent = cachedLogi > 0 ? cachedLogi : (cfg.CachedBattery > 0 ? cfg.CachedBattery : -1);
                            devInfo.ExactBatteryPercent = devInfo.BatteryPercent;
                        }
                        else if (isValkyrie)
                        {
                            bool isKb = (cfg.DeviceId != null && cfg.DeviceId.EndsWith(":KB")) || cfg.Category == DeviceCategory.Keyboard;
                            int cachedVk = isKb ? ValkyrieDeviceHelper.CachedKbBattery : ValkyrieDeviceHelper.CachedMouseBattery;
                            devInfo.BatteryPercent = cachedVk > 0 ? cachedVk : (cfg.CachedBattery > 0 ? cfg.CachedBattery : -1);
                            devInfo.ExactBatteryPercent = devInfo.BatteryPercent;
                        }
                        else if (isRapoo)
                        {
                            int cachedRp = RapooDeviceHelper.CachedBatteryPercent;
                            devInfo.BatteryPercent = cachedRp > 0 ? cachedRp : (cfg.CachedBattery > 0 ? cfg.CachedBattery : -1);
                            devInfo.ExactBatteryPercent = devInfo.BatteryPercent;
                        }
                        else if (isHyperX)
                        {
                            int cachedHp = HyperXDeviceHelper.CachedBatteryPercent;
                            devInfo.BatteryPercent = cachedHp > 0 ? cachedHp : (cfg.CachedBattery > 0 ? cfg.CachedBattery : -1);
                            devInfo.ExactBatteryPercent = devInfo.BatteryPercent;
                        }
                        else
                        {
                            if (batt >= 0)
                            {
                                devInfo.BatteryPercent = batt;
                                if (batt != cfg.CachedBattery)
                                {
                                    cfg.CachedBattery = batt;
                                    SaveCustomDevice(cfg);
                                }
                            }
                            else
                            {
                                devInfo.BatteryPercent = cfg.CachedBattery > 0 ? cfg.CachedBattery : -1;
                            }
                            devInfo.ExactBatteryPercent = devInfo.BatteryPercent;
                        }
                        devInfo.LastUpdated = cfg.LastSeen;
                    }

                    list.Add(devInfo);
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("QueryAllDevices 分支失败（该品牌设备可能因此不显示）", diagEx); }

            // 3.5 Check custom nicknames configured for Razer or Rapoo devices
            try
            {
                var customConfigs = LoadCustomDevices();
                foreach (var d in list)
                {
                    if (!d.IsCustom)
                    {
                        string normD = (d.Brand == "Razer" || d.Brand == "雷蛇" || (d.DeviceId != null && d.DeviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase))) ? RazerDeviceHelper.NormalizeRazerDeviceId(d.DeviceId) : d.DeviceId;
                        var match = customConfigs.Find(c => {
                            string normC = (c.DeviceId != null && c.DeviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase)) ? RazerDeviceHelper.NormalizeRazerDeviceId(c.DeviceId) : c.DeviceId;
                            string normFp = (!string.IsNullOrEmpty(c.HardwareFingerprint) && c.HardwareFingerprint.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase)) ? RazerDeviceHelper.NormalizeRazerDeviceId(c.HardwareFingerprint) : c.HardwareFingerprint;
                            return (normC != null && normC.Equals(normD, StringComparison.OrdinalIgnoreCase)) ||
                                   (!string.IsNullOrEmpty(normFp) && normFp.Equals(normD, StringComparison.OrdinalIgnoreCase));
                        });
                        if (match != null && !string.IsNullOrEmpty(match.CustomName))
                        {
                            d.CustomName = match.CustomName;
                            d.IsCustom = true;
                        }
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("QueryAllDevices 分支失败（该品牌设备可能因此不显示）", diagEx); }

            // 3.6 Guarantee no duplicate NuPhy devices in list
            var nuphyDevices = list.FindAll(x => x.Brand == "NuPhy" || NuphyDeviceHelper.IsNuphyDevice(x.DeviceId) || NuphyDeviceHelper.IsNuphyDevice(x.DeviceName));
            if (nuphyDevices.Count > 1)
            {
                var best = nuphyDevices.Find(x => x.IsConnected && x.BatteryPercent > 0) ??
                           nuphyDevices.Find(x => x.IsConnected) ??
                           nuphyDevices[0];
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (nuphyDevices.Contains(list[i]) && list[i] != best)
                    {
                        list.RemoveAt(i);
                    }
                }
            }

            // 3.65 Guarantee no duplicate Razer devices for the same normalized mouse
            try
            {
                var razerGroups = new Dictionary<string, List<MouseBatteryInfo>>(StringComparer.OrdinalIgnoreCase);
                foreach (var d in list)
                {
                    if (d.Brand == "Razer" || d.Brand == "雷蛇" || (d.DeviceId != null && d.DeviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase)))
                    {
                        string normId = RazerDeviceHelper.NormalizeRazerDeviceId(d.DeviceId);
                        if (!razerGroups.ContainsKey(normId)) razerGroups[normId] = new List<MouseBatteryInfo>();
                        razerGroups[normId].Add(d);
                    }
                }

                foreach (var kvp in razerGroups)
                {
                    if (kvp.Value.Count > 1)
                    {
                        var best = kvp.Value.Find(x => x.IsConnected && x.BatteryPercent > 0) ??
                                   kvp.Value.Find(x => x.IsConnected) ??
                                   kvp.Value.Find(x => x.IsDonglePresent) ??
                                   kvp.Value[0];
                        for (int i = list.Count - 1; i >= 0; i--)
                        {
                            if (kvp.Value.Contains(list[i]) && list[i] != best)
                            {
                                list.RemoveAt(i);
                            }
                        }
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("QueryAllDevices 分支失败（该品牌设备可能因此不显示）", diagEx); }

            // 3.66 Guarantee no duplicate Valkyrie devices in list
            var vkList = list.FindAll(x => x.Brand == "Valkyrie" || x.Brand == "瓦尔基里" || ValkyrieDeviceHelper.IsValkyrieDevice(x.DeviceId));
            if (vkList.Count > 1)
            {
                var vkGroups = new Dictionary<string, List<MouseBatteryInfo>>(StringComparer.OrdinalIgnoreCase);
                foreach (var d in vkList)
                {
                    string key = d.DeviceId ?? "";
                    if (!vkGroups.ContainsKey(key)) vkGroups[key] = new List<MouseBatteryInfo>();
                    vkGroups[key].Add(d);
                }
                foreach (var kvp in vkGroups)
                {
                    if (kvp.Value.Count > 1)
                    {
                        var customEntry = kvp.Value.Find(x => x.IsCustom && !string.IsNullOrEmpty(x.CustomName));
                        if (customEntry != null)
                        {
                            var vkPrimary = kvp.Value.Find(x => x.IsConnected && x.BatteryPercent > 0) ??
                                            kvp.Value.Find(x => x.IsConnected) ??
                                            kvp.Value.Find(x => x.IsDonglePresent) ??
                                            kvp.Value[0];
                            if (string.IsNullOrEmpty(vkPrimary.CustomName))
                            {
                                vkPrimary.CustomName = customEntry.CustomName;
                                vkPrimary.IsCustom = true;
                            }
                        }
                    }
                }
                foreach (var kvp in vkGroups)
                {
                    if (kvp.Value.Count > 1)
                    {
                        var best = kvp.Value.Find(x => x.IsConnected && x.BatteryPercent > 0) ??
                                   kvp.Value.Find(x => x.IsConnected) ??
                                   kvp.Value.Find(x => x.IsDonglePresent) ??
                                   kvp.Value[0];
                        for (int i = list.Count - 1; i >= 0; i--)
                        {
                            if (kvp.Value.Contains(list[i]) && list[i] != best)
                            {
                                list.RemoveAt(i);
                            }
                        }
                    }
                }
            }

            // 3.7 Filter out user-hidden devices
            list.RemoveAll(d => IsDeviceHidden(d.DeviceId) || (!string.IsNullOrEmpty(d.HardwareFingerprint) && IsDeviceHidden(d.HardwareFingerprint)));

            // 4. Priority Ordering and Primary Selection
            var prioList = GetPriorityList();
            bool prioChanged = false;

            var validIds = new List<string>();
            foreach (var d in list) validIds.Add(d.DeviceId);

            for (int i = prioList.Count - 1; i >= 0; i--)
            {
                if (!validIds.Contains(prioList[i]))
                {
                    prioList.RemoveAt(i);
                    prioChanged = true;
                }
            }

            foreach (var d in list)
            {
                if (!prioList.Contains(d.DeviceId))
                {
                    prioList.Add(d.DeviceId);
                    prioChanged = true;
                }
            }
            if (prioChanged)
            {
                SavePriorityList(prioList);
            }

            list.Sort((a, b) =>
            {
                int ia = prioList.IndexOf(a.DeviceId);
                int ib = prioList.IndexOf(b.DeviceId);
                if (ia < 0) ia = 9999;
                if (ib < 0) ib = 9999;
                return ia.CompareTo(ib);
            });

            for (int i = 0; i < list.Count; i++)
            {
                list[i].PriorityIndex = i + 1;
            }

            // 托盘常驻设备全自动轮换规则：
            // list 已严格按用户在设备优先级页面调整的顺序从高到低排列。
            // 1. 高优先设备在线则展示高优先 (第一个 IsConnected 的设备)；
            // 2. 高优先设备离线则顺延下一个在线设备；
            // 3. 如果全部设备都离线，则回退展示第 1 位设备 (list[0])，并展示离线记忆电量！
            MouseBatteryInfo primary = list.Find(d => d.IsConnected);
            if (primary == null && list.Count > 0)
            {
                primary = list[0];
            }

            foreach (var dev in list)
            {
                dev.IsPrimary = (dev == primary);
            }

            return list;
        }
    }
}