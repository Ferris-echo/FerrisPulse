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

        public static bool IsDeviceHidden(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return false;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(HiddenDevicesRegPath, false))
                {
                    if (key != null)
                    {
                        object val = key.GetValue(deviceId);
                        if (val != null && Convert.ToInt32(val) == 1) return true;
                        if (deviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase))
                        {
                            string norm = RazerDeviceHelper.NormalizeRazerDeviceId(deviceId);
                            if (!norm.Equals(deviceId, StringComparison.OrdinalIgnoreCase))
                            {
                                object v2 = key.GetValue(norm);
                                if (v2 != null && Convert.ToInt32(v2) == 1) return true;
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public static void HideOrDeleteDevice(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return;
            string norm = deviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase)
                ? RazerDeviceHelper.NormalizeRazerDeviceId(deviceId) : deviceId;

            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(HiddenDevicesRegPath))
                {
                    if (key != null)
                    {
                        key.SetValue(deviceId, 1, RegistryValueKind.DWord);
                        if (!norm.Equals(deviceId, StringComparison.OrdinalIgnoreCase))
                        {
                            key.SetValue(norm, 1, RegistryValueKind.DWord);
                        }
                    }
                }
            }
            catch { }

            DeleteCustomDevice(deviceId);
            if (!norm.Equals(deviceId, StringComparison.OrdinalIgnoreCase))
            {
                DeleteCustomDevice(norm);
            }

            try
            {
                var prio = GetPriorityList();
                if (prio != null)
                {
                    bool chg = prio.Remove(deviceId);
                    if (prio.Remove(norm)) chg = true;
                    if (chg) SavePriorityList(prio);
                }
            }
            catch { }
        }

        public static void UnhideDevice(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return;
            string norm = deviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase)
                ? RazerDeviceHelper.NormalizeRazerDeviceId(deviceId) : deviceId;

            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(HiddenDevicesRegPath, true))
                {
                    if (key != null)
                    {
                        key.DeleteValue(deviceId, false);
                        if (!norm.Equals(deviceId, StringComparison.OrdinalIgnoreCase))
                        {
                            key.DeleteValue(norm, false);
                        }
                    }
                }
            }
            catch { }
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
            catch { }
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
            catch { }
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
            catch { }
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
            catch { }
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
            catch { }
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
            catch { }
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
                            catch { }
                        }
                    }
                }
            }
            catch { }
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
            catch { }
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
                        key.DeleteSubKeyTree(deviceId);
                    }
                }
            }
            catch { }

            try
            {
                var prio = GetPriorityList();
                if (prio != null && prio.Remove(deviceId))
                {
                    SavePriorityList(prio);
                }
            }
            catch { }

            try
            {
                string p = GetConfiguredPrimaryDeviceId();
                if (string.Equals(p, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    SetConfiguredPrimaryDeviceId("");
                }
            }
            catch { }
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
            catch { }
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
            catch { }
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
            catch { }
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
            catch { }
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
            catch { }
        }

        public static List<MouseBatteryInfo> QueryAllDevices()
        {
            CleanupDuplicateNuphyDevices();

            CleanupDuplicateLogitechDevices();
            CleanupDuplicateRazerDevices();
            CleanupDuplicateValkyrieDevices();

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
            catch { }

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
            catch { }

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
            catch { }

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
            catch { }



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
            catch { }

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
            catch { }

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

                    bool isRazer = (cfg.DeviceId != null && cfg.DeviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase)) ||
                                   (!string.IsNullOrEmpty(cfg.HardwareFingerprint) && cfg.HardwareFingerprint.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase));

                    string normCfgId = isRazer ? RazerDeviceHelper.NormalizeRazerDeviceId(cfg.DeviceId) : cfg.DeviceId;
                    string normFpId = (isRazer && !string.IsNullOrEmpty(cfg.HardwareFingerprint)) ? RazerDeviceHelper.NormalizeRazerDeviceId(cfg.HardwareFingerprint) : cfg.HardwareFingerprint;

                    // Check if already in list (either direct DeviceId match or normalized Razer match)
                    var existDev = list.Find(x => {
                        if (x.DeviceId.Equals(cfg.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrEmpty(cfg.HardwareFingerprint) && x.DeviceId.Equals(cfg.HardwareFingerprint, StringComparison.OrdinalIgnoreCase)) ||
                            (!string.IsNullOrEmpty(x.HardwareFingerprint) && x.HardwareFingerprint.Equals(cfg.DeviceId, StringComparison.OrdinalIgnoreCase)))
                        {
                            return true;
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
                            if (isRazer && (
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
                            existDev.CustomName = cfg.CustomName;
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

                    if (isValkyrie && list.Exists(x => x.Brand == "Valkyrie" || x.Brand == "瓦尔基里"))
                    {
                        var existVk = list.Find(x => x.Brand == "Valkyrie" || x.Brand == "瓦尔基里");
                        if (existVk != null)
                        {
                            existVk.IsCustom = true;
                            if (cfg.Category != DeviceCategory.Generic) existVk.Category = cfg.Category;
                            if (!string.IsNullOrEmpty(cfg.CustomName)) existVk.CustomName = cfg.CustomName;
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
                            bool isKb = cfg.DeviceId != null && cfg.DeviceId.EndsWith(":KB");
                            int cachedVk = isKb ? ValkyrieDeviceHelper.CachedKbBattery : ValkyrieDeviceHelper.CachedMouseBattery;
                            connected = false;
                            batt = cachedVk > 0 ? cachedVk : (cfg.CachedBattery > 0 ? cfg.CachedBattery : -1);
                            friendlyName = isKb ? ValkyrieDeviceHelper.CachedKbName : ValkyrieDeviceHelper.CachedMouseName;
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
                    devInfo.Brand = isNuphy ? "NuPhy" : (isRazer ? "Razer" : (isLogitech ? "Logitech" : (isValkyrie ? "Valkyrie" : "Custom")));
                    devInfo.Category = cfg.Category;
                    devInfo.CustomName = cfg.CustomName;
                    devInfo.DeviceName = !string.IsNullOrEmpty(friendlyName) ? friendlyName : (!string.IsNullOrEmpty(cfg.OriginalName) ? cfg.OriginalName : cfg.CustomName);
                    devInfo.Transport = cfg.Transport ?? (isRazer ? "2.4G 无线" : "Bluetooth");
                    devInfo.IsCustom = true;
                    if (devInfo.Brand == "NuPhy")
                    {
                        if (devInfo.Category == DeviceCategory.Generic) devInfo.Category = DeviceCategory.Keyboard;
                        devInfo.IsCharging = NuphyDeviceHelper.CachedIsCharging;
                    }
                    else if (devInfo.Brand == "Logitech")
                    {
                        if (devInfo.Category == DeviceCategory.Generic) devInfo.Category = DeviceCategory.Mouse;
                        devInfo.IsCharging = LogitechDeviceHelper.CachedIsCharging;
                    }
                    else if (devInfo.Brand == "Valkyrie")
                    {
                        if (devInfo.Category == DeviceCategory.Generic)
                        {
                            devInfo.Category = (cfg.DeviceId != null && cfg.DeviceId.EndsWith(":KB")) ? DeviceCategory.Keyboard : DeviceCategory.Mouse;
                        }
                    }
                    else if (devInfo.Brand == "Razer")
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
                        devInfo.BatteryPercent = batt;
                        devInfo.ExactBatteryPercent = batt >= 0 ? batt : 0;
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
                            bool isKb = cfg.DeviceId != null && cfg.DeviceId.EndsWith(":KB");
                            int cachedVk = isKb ? ValkyrieDeviceHelper.CachedKbBattery : ValkyrieDeviceHelper.CachedMouseBattery;
                            devInfo.BatteryPercent = cachedVk > 0 ? cachedVk : (cfg.CachedBattery > 0 ? cfg.CachedBattery : -1);
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
            catch { }

            // 3.5 Check custom nicknames configured for Razer or Rapoo devices
            try
            {
                var customConfigs = LoadCustomDevices();
                foreach (var d in list)
                {
                    if (!d.IsCustom)
                    {
                        string normD = (d.Brand == "Razer" || (d.DeviceId != null && d.DeviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase))) ? RazerDeviceHelper.NormalizeRazerDeviceId(d.DeviceId) : d.DeviceId;
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
            catch { }

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
                    if (d.Brand == "Razer" || (d.DeviceId != null && d.DeviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase)))
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
            catch { }

            // 3.66 Guarantee no duplicate Valkyrie devices in list
            var vkList = list.FindAll(x => x.Brand == "Valkyrie" || ValkyrieDeviceHelper.IsValkyrieDevice(x.DeviceId));
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
            list.RemoveAll(d => IsDeviceHidden(d.DeviceId));

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