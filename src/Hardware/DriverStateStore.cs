// ============================================================================
// 模块名称: DriverStateStore.cs
// 所属区域: Hardware (硬件状态持久化护栏层)
// 核心职责:
//   为所有内置驱动与外部扩展包 (.fp-adapter.json / .fp-driver.cs) 提供统一的注册表冷启动与
//   无线休眠状态缓存（电量百分比、充电态、DPI 档位、回报率及自定义动作开关状态），
//   确保外部扩展包在无线设备开机休眠时具备与内置驱动 100% 对等的状态记忆恢复能力。
// 依赖关系: Microsoft.Win32
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑或注册表键结构时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止 $"..." 插值、禁止 ?. 空条件运算符、禁止 nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Text;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    public static class DriverStateStore
    {
        private const string BaseRegistryPath = @"Software\RazerBatteryTray\DeviceState";

        private static string SanitizeKey(string rawKey)
        {
            if (string.IsNullOrEmpty(rawKey)) return "Default";
            StringBuilder sb = new StringBuilder(rawKey.Length);
            for (int i = 0; i < rawKey.Length; i++)
            {
                char c = rawKey[i];
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-' || c == '.')
                {
                    sb.Append(c);
                }
                else
                {
                    sb.Append('_');
                }
            }
            return sb.ToString();
        }

        public static void SaveBatteryState(string deviceKey, int batteryPercent, bool isCharging, string deviceName)
        {
            if (string.IsNullOrEmpty(deviceKey) || batteryPercent < 0 || batteryPercent > 100) return;
            try
            {
                string subPath = BaseRegistryPath + "\\" + SanitizeKey(deviceKey);
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(subPath))
                {
                    if (key != null)
                    {
                        key.SetValue("BatteryPercent", batteryPercent, RegistryValueKind.DWord);
                        key.SetValue("IsCharging", isCharging ? 1 : 0, RegistryValueKind.DWord);
                        if (!string.IsNullOrEmpty(deviceName))
                        {
                            key.SetValue("DeviceName", deviceName, RegistryValueKind.String);
                        }
                        key.SetValue("LastUpdated", DateTime.Now.ToString("o"), RegistryValueKind.String);
                    }
                }
            }
            catch { }
        }

        public static bool TryLoadBatteryState(string deviceKey, out int batteryPercent, out bool isCharging, out string deviceName, out DateTime lastUpdated)
        {
            batteryPercent = -1;
            isCharging = false;
            deviceName = "";
            lastUpdated = DateTime.MinValue;
            if (string.IsNullOrEmpty(deviceKey)) return false;

            try
            {
                string subPath = BaseRegistryPath + "\\" + SanitizeKey(deviceKey);
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(subPath, false))
                {
                    if (key == null) return false;
                    object bpObj = key.GetValue("BatteryPercent");
                    if (bpObj == null) return false;
                    int bp = Convert.ToInt32(bpObj);
                    if (bp < 0 || bp > 100) return false;

                    batteryPercent = bp;
                    object chgObj = key.GetValue("IsCharging");
                    if (chgObj != null) isCharging = (Convert.ToInt32(chgObj) != 0);

                    string dn = key.GetValue("DeviceName") as string;
                    if (!string.IsNullOrEmpty(dn)) deviceName = dn;

                    string dtStr = key.GetValue("LastUpdated") as string;
                    if (!string.IsNullOrEmpty(dtStr))
                    {
                        DateTime parsed;
                        if (DateTime.TryParse(dtStr, out parsed)) lastUpdated = parsed;
                    }
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public static void SaveDpiState(string deviceKey, int dpiX, int dpiY, int stage, int pollingRate)
        {
            if (string.IsNullOrEmpty(deviceKey)) return;
            try
            {
                string subPath = BaseRegistryPath + "\\" + SanitizeKey(deviceKey);
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(subPath))
                {
                    if (key != null)
                    {
                        if (dpiX > 0) key.SetValue("DpiX", dpiX, RegistryValueKind.DWord);
                        if (dpiY > 0) key.SetValue("DpiY", dpiY, RegistryValueKind.DWord);
                        if (stage > 0) key.SetValue("DpiStage", stage, RegistryValueKind.DWord);
                        if (pollingRate > 0) key.SetValue("PollingRate", pollingRate, RegistryValueKind.DWord);
                    }
                }
            }
            catch { }
        }

        public static bool TryLoadDpiState(string deviceKey, out int dpiX, out int dpiY, out int stage, out int pollingRate)
        {
            dpiX = 0;
            dpiY = 0;
            stage = 0;
            pollingRate = 0;
            if (string.IsNullOrEmpty(deviceKey)) return false;

            try
            {
                string subPath = BaseRegistryPath + "\\" + SanitizeKey(deviceKey);
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(subPath, false))
                {
                    if (key == null) return false;
                    object dx = key.GetValue("DpiX");
                    object dy = key.GetValue("DpiY");
                    object st = key.GetValue("DpiStage");
                    object pr = key.GetValue("PollingRate");
                    if (dx != null) dpiX = Convert.ToInt32(dx);
                    if (dy != null) dpiY = Convert.ToInt32(dy);
                    if (st != null) stage = Convert.ToInt32(st);
                    if (pr != null) pollingRate = Convert.ToInt32(pr);
                    return dpiX > 0 || pollingRate > 0;
                }
            }
            catch
            {
                return false;
            }
        }

        public static void SaveCustomActionValue(string deviceKey, string actionId, int value)
        {
            if (string.IsNullOrEmpty(deviceKey) || string.IsNullOrEmpty(actionId)) return;
            try
            {
                string subPath = BaseRegistryPath + "\\" + SanitizeKey(deviceKey) + "\\Actions";
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(subPath))
                {
                    if (key != null)
                    {
                        key.SetValue(SanitizeKey(actionId), value, RegistryValueKind.DWord);
                    }
                }
            }
            catch { }
        }

        public static bool TryLoadCustomActionValue(string deviceKey, string actionId, out int value)
        {
            value = 0;
            if (string.IsNullOrEmpty(deviceKey) || string.IsNullOrEmpty(actionId)) return false;
            try
            {
                string subPath = BaseRegistryPath + "\\" + SanitizeKey(deviceKey) + "\\Actions";
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(subPath, false))
                {
                    if (key == null) return false;
                    object obj = key.GetValue(SanitizeKey(actionId));
                    if (obj == null) return false;
                    value = Convert.ToInt32(obj);
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        public static void ClearDeviceState(string deviceKey)
        {
            if (string.IsNullOrEmpty(deviceKey)) return;
            try
            {
                string subPath = BaseRegistryPath + "\\" + SanitizeKey(deviceKey);
                Registry.CurrentUser.DeleteSubKeyTree(subPath, false);
            }
            catch { }
        }
    }
}
