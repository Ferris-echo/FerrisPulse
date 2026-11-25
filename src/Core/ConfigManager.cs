// ============================================================================
// 模块名称: ConfigManager.cs
// 所属区域: Core (核心管理层)
// 核心职责:
//   集中管理用户配置与状态在 Windows 注册表 (HKCU\Software\FerrisPulse) 的持久化存取
// 依赖关系: 无外部依赖
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在新增或修改配置键值时，【必须】同步更新上方说明与键名列表。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    public static class ConfigManager
    {
        public const string ConfigRegistryKey = @"Software\FerrisPulse";
        public const string LegacyConfigKey = @"Software\RazerBatteryTray";
        public const string DesktopShortcutInitialized = "DesktopShortcutInitialized";
        public const string AutoCheckUpdate = "AutoCheckUpdate";
        public const string LastUpdateCheckTime = "LastUpdateCheckTime";
        public const string MultiTrayEnabledKey = "MultiTrayEnabled";
        public const string MultiTrayMaxCountKey = "MultiTrayMaxCount";
        public const string LowBatteryNotifyEnabledKey = "LowBatteryNotifyEnabled";
        public const string LowBatteryUiColorEnabledKey = "LowBatteryUiColorEnabled";
        public const string LowBatteryTrayColorEnabledKey = "LowBatteryTrayColorEnabled";

        public static int GetInt(string name, int defaultValue)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(ConfigRegistryKey, false))
                {
                    if (key != null)
                    {
                        var val = key.GetValue(name);
                        if (val != null) return Convert.ToInt32(val);
                    }
                }
                using (var key = Registry.CurrentUser.OpenSubKey(LegacyConfigKey, false))
                {
                    if (key != null)
                    {
                        var val = key.GetValue(name);
                        if (val != null) return Convert.ToInt32(val);
                    }
                }
            }
            catch { }
            return defaultValue;
        }

        public static bool GetBool(string name, bool defaultValue)
        {
            int intVal = GetInt(name, defaultValue ? 1 : 0);
            return intVal == 1;
        }

        public static string GetString(string name, string defaultValue)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(ConfigRegistryKey, false))
                {
                    if (key != null)
                    {
                        var val = key.GetValue(name);
                        if (val != null) return val.ToString();
                    }
                }
                using (var key = Registry.CurrentUser.OpenSubKey(LegacyConfigKey, false))
                {
                    if (key != null)
                    {
                        var val = key.GetValue(name);
                        if (val != null) return val.ToString();
                    }
                }
            }
            catch { }
            return defaultValue;
        }

        public static void SetInt(string name, int value)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(ConfigRegistryKey))
                {
                    if (key != null)
                    {
                        key.SetValue(name, value, RegistryValueKind.DWord);
                    }
                }
            }
            catch { }
        }

        public static void SetBool(string name, bool value)
        {
            SetInt(name, value ? 1 : 0);
        }

        public static void SetString(string name, string value)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(ConfigRegistryKey))
                {
                    if (key != null)
                    {
                        key.SetValue(name, value ?? "", RegistryValueKind.String);
                    }
                }
            }
            catch { }
        }

        public static bool GetAutoCheckUpdate()
        {
            return GetBool(AutoCheckUpdate, true);
        }

        public static void SetAutoCheckUpdate(bool enabled)
        {
            SetBool(AutoCheckUpdate, enabled);
        }

        public static string GetLastUpdateCheckTime()
        {
            return GetString(LastUpdateCheckTime, string.Empty);
        }

        public static void SetLastUpdateCheckTime(string timeStr)
        {
            SetString(LastUpdateCheckTime, timeStr);
        }

        public static bool GetMultiTrayEnabled()
        {
            return GetBool(MultiTrayEnabledKey, false);
        }

        public static void SetMultiTrayEnabled(bool enabled)
        {
            SetBool(MultiTrayEnabledKey, enabled);
        }

        public static int GetMultiTrayMaxCount()
        {
            int count = GetInt(MultiTrayMaxCountKey, 3);
            if (count < 2) count = 2;
            if (count > 16) count = 16;
            return count;
        }

        public static void SetMultiTrayMaxCount(int count)
        {
            if (count < 2) count = 2;
            if (count > 16) count = 16;
            SetInt(MultiTrayMaxCountKey, count);
        }

        public static bool GetLowBatteryNotifyEnabled()
        {
            return GetBool(LowBatteryNotifyEnabledKey, true);
        }

        public static void SetLowBatteryNotifyEnabled(bool enabled)
        {
            SetBool(LowBatteryNotifyEnabledKey, enabled);
        }

        public static bool GetLowBatteryUiColorEnabled()
        {
            return GetBool(LowBatteryUiColorEnabledKey, true);
        }

        public static void SetLowBatteryUiColorEnabled(bool enabled)
        {
            SetBool(LowBatteryUiColorEnabledKey, enabled);
        }

        public static bool GetLowBatteryTrayColorEnabled()
        {
            return GetBool(LowBatteryTrayColorEnabledKey, false);
        }

        public static void SetLowBatteryTrayColorEnabled(bool enabled)
        {
            SetBool(LowBatteryTrayColorEnabledKey, enabled);
        }
    }
}
