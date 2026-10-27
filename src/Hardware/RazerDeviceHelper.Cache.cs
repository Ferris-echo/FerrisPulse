// ============================================================================
// 模块名称: RazerDeviceHelper.Cache.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   雷蛇设备状态缓存：休眠/待机期间保留最后一次成功的电量、DPI、回报率读数，
//   并在进程重启后从注册表恢复，避免设备休眠时界面显示空白。
//   本文件为 RazerDeviceHelper 的分部类，由 RazerDeviceHelper.cs 拆分而来。
// 依赖关系: RazerDeviceHelper.cs（共享 hidLock / deviceHardwareStates）
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 新增缓存字段时必须同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    public static partial class RazerDeviceHelper
    {
        // Hardware Cache for Standby / Sleep Retention
        public static int CachedBatteryPercent = 0;
        public static double CachedExactBatteryPercent = 0.0;
        public static string CachedDeviceName = "";
        public static int CachedDpi = 0;
        public static int CachedDpiY = 0;
        public static int CachedDpiStage = 0;
        public static int CachedDpiStageCount = 0;
        public static int[] CachedDpiStages = null;
        public static int[] CachedDpiStagesY = null;
        public static bool CachedIsXyIndependent = false;
        public static double CachedXyRatio = 1.0;
        public static int CachedPollingRate = 0;
        public static DateTime CachedLastUpdated = DateTime.MinValue;

        private static string cachedRazerDpiPath = null;
        private static int cachedRazerReportLength = 90;
        private static bool cachedRazerPrepended = false;
        private static DateTime lastRazerEnumTime = DateTime.MinValue;

        private const string CacheRegistryKey = @"Software\RazerBatteryTray\HardwareCache";

        public static void LoadHardwareCache()
        {
            try
            {
                using (var rootKey = Registry.CurrentUser.OpenSubKey(CacheRegistryKey))
                {
                    if (rootKey != null)
                    {
                        var batt = rootKey.GetValue("BatteryPercent");
                        if (batt != null)
                        {
                            int b = (int)batt;
                            if (b > 0) CachedBatteryPercent = b;
                        }

                        var exactBatt = rootKey.GetValue("ExactBatteryPercent");
                        if (exactBatt != null)
                        {
                            double eb;
                            if (double.TryParse(exactBatt.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out eb))
                            {
                                if (eb > 0) CachedExactBatteryPercent = eb;
                            }
                        }

                        var dev = rootKey.GetValue("DeviceName");
                        if (dev != null) CachedDeviceName = (string)dev;

                        var dpi = rootKey.GetValue("Dpi");
                        if (dpi != null) CachedDpi = (int)dpi;

                        var st = rootKey.GetValue("DpiStage");
                        if (st != null) CachedDpiStage = (int)st;

                        var stCount = rootKey.GetValue("DpiStageCount");
                        if (stCount != null) CachedDpiStageCount = (int)stCount;

                        var stStr = rootKey.GetValue("DpiStages") as string;
                        if (!string.IsNullOrEmpty(stStr))
                        {
                            string[] parts = stStr.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                            CachedDpiStages = new int[parts.Length];
                            for (int i = 0; i < parts.Length; i++)
                            {
                                int p;
                                if (int.TryParse(parts[i], out p)) CachedDpiStages[i] = p;
                            }
                        }

                        var dpiY = rootKey.GetValue("DpiY");
                        if (dpiY != null) CachedDpiY = (int)dpiY;

                        var stStrY = rootKey.GetValue("DpiStagesY") as string;
                        if (!string.IsNullOrEmpty(stStrY))
                        {
                            string[] partsY = stStrY.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                            CachedDpiStagesY = new int[partsY.Length];
                            for (int i = 0; i < partsY.Length; i++)
                            {
                                int p;
                                if (int.TryParse(partsY[i], out p)) CachedDpiStagesY[i] = p;
                            }
                        }

                        var isXy = rootKey.GetValue("IsXyIndependent");
                        if (isXy != null) CachedIsXyIndependent = ((int)isXy == 1);

                        var xyRatio = rootKey.GetValue("XyRatio");
                        if (xyRatio != null)
                        {
                            double r;
                            if (double.TryParse(xyRatio.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out r))
                                CachedXyRatio = r;
                        }

                        var poll = rootKey.GetValue("PollingRate");
                        if (poll != null) CachedPollingRate = (int)poll;

                        var updatedStr = rootKey.GetValue("LastUpdated") as string;
                        if (!string.IsNullOrEmpty(updatedStr))
                        {
                            DateTime dt;
                            if (DateTime.TryParse(updatedStr, out dt)) CachedLastUpdated = dt;
                        }

                        string[] subKeyNames = rootKey.GetSubKeyNames();
                        foreach (string subName in subKeyNames)
                        {
                            using (var devKey = rootKey.OpenSubKey(subName))
                            {
                                if (devKey != null)
                                {
                                    string normId = subName.Replace('_', ':');
                                    var state = new RazerDeviceHardwareState();
                                    state.DeviceId = normId;
                                    var sb = devKey.GetValue("BatteryPercent");
                                    if (sb != null) state.BatteryPercent = (int)sb;
                                    var seb = devKey.GetValue("ExactBatteryPercent");
                                    if (seb != null)
                                    {
                                        double eb;
                                        if (double.TryParse(seb.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out eb))
                                            state.ExactBatteryPercent = eb;
                                    }
                                    var sdn = devKey.GetValue("DeviceName") as string;
                                    if (!string.IsNullOrEmpty(sdn)) state.DeviceName = sdn;
                                    var sdpi = devKey.GetValue("Dpi");
                                    if (sdpi != null) state.Dpi = (int)sdpi;
                                    var sst = devKey.GetValue("DpiStage");
                                    if (sst != null) state.DpiStage = (int)sst;
                                    var sstc = devKey.GetValue("DpiStageCount");
                                    if (sstc != null) state.DpiStageCount = (int)sstc;
                                    var sstStr = devKey.GetValue("DpiStages") as string;
                                    if (!string.IsNullOrEmpty(sstStr))
                                    {
                                        string[] parts = sstStr.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                                        state.DpiStages = new int[parts.Length];
                                        for (int i = 0; i < parts.Length; i++)
                                        {
                                            int p;
                                            if (int.TryParse(parts[i], out p)) state.DpiStages[i] = p;
                                        }
                                    }
                                    var sdpiY = devKey.GetValue("DpiY");
                                    if (sdpiY != null) state.DpiY = (int)sdpiY;
                                    var sstStrY = devKey.GetValue("DpiStagesY") as string;
                                    if (!string.IsNullOrEmpty(sstStrY))
                                    {
                                        string[] partsY = sstStrY.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                                        state.DpiStagesY = new int[partsY.Length];
                                        for (int i = 0; i < partsY.Length; i++)
                                        {
                                            int p;
                                            if (int.TryParse(partsY[i], out p)) state.DpiStagesY[i] = p;
                                        }
                                    }
                                    var sisXy = devKey.GetValue("IsXyIndependent");
                                    if (sisXy != null) state.IsXyIndependent = ((int)sisXy == 1);
                                    var sratio = devKey.GetValue("XyRatio");
                                    if (sratio != null)
                                    {
                                        double r;
                                        if (double.TryParse(sratio.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out r))
                                            state.XyRatio = r;
                                    }
                                    var spoll = devKey.GetValue("PollingRate");
                                    if (spoll != null) state.PollingRate = (int)spoll;
                                    var sdt = devKey.GetValue("LastUpdated") as string;
                                    if (!string.IsNullOrEmpty(sdt))
                                    {
                                        DateTime dt;
                                        if (DateTime.TryParse(sdt, out dt)) state.LastUpdated = dt;
                                    }
                                    lock (stateCacheLock)
                                    {
                                        deviceHardwareStates[normId] = state;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/RazerDeviceHelper.cs: 已忽略异常", diagEx); }
        }

        public static void SaveHardwareCache()
        {
            try
            {
                using (var rootKey = Registry.CurrentUser.CreateSubKey(CacheRegistryKey))
                {
                    if (rootKey != null)
                    {
                        KeyValuePair<string, RazerDeviceHardwareState>[] statesCopy;
                        lock (stateCacheLock)
                        {
                            var list = new List<KeyValuePair<string, RazerDeviceHardwareState>>(deviceHardwareStates.Count);
                            foreach (var kvp in deviceHardwareStates) list.Add(kvp);
                            statesCopy = list.ToArray();
                        }
                        foreach (var kvp in statesCopy)
                        {
                            var st = kvp.Value;
                            if (st == null) continue;
                            string subKeyName = kvp.Key.Replace(':', '_');
                            using (var devKey = rootKey.CreateSubKey(subKeyName))
                            {
                                if (devKey != null)
                                {
                                    if (st.BatteryPercent > 0) devKey.SetValue("BatteryPercent", st.BatteryPercent);
                                    if (st.ExactBatteryPercent > 0) devKey.SetValue("ExactBatteryPercent", st.ExactBatteryPercent.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                                    if (!string.IsNullOrEmpty(st.DeviceName)) devKey.SetValue("DeviceName", st.DeviceName);
                                    if (st.Dpi > 0) devKey.SetValue("Dpi", st.Dpi);
                                    if (st.DpiY > 0) devKey.SetValue("DpiY", st.DpiY);
                                    if (st.DpiStage > 0) devKey.SetValue("DpiStage", st.DpiStage);
                                    if (st.DpiStageCount > 0) devKey.SetValue("DpiStageCount", st.DpiStageCount);
                                    if (st.DpiStages != null && st.DpiStages.Length > 0)
                                    {
                                        string stStr = string.Join(",", Array.ConvertAll(st.DpiStages, s => s.ToString()));
                                        devKey.SetValue("DpiStages", stStr);
                                    }
                                    if (st.DpiStagesY != null && st.DpiStagesY.Length > 0)
                                    {
                                        string stStrY = string.Join(",", Array.ConvertAll(st.DpiStagesY, s => s.ToString()));
                                        devKey.SetValue("DpiStagesY", stStrY);
                                    }
                                    devKey.SetValue("IsXyIndependent", st.IsXyIndependent ? 1 : 0);
                                    devKey.SetValue("XyRatio", st.XyRatio.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
                                    if (st.PollingRate > 0) devKey.SetValue("PollingRate", st.PollingRate);
                                    if (st.LastUpdated != DateTime.MinValue) devKey.SetValue("LastUpdated", st.LastUpdated.ToString("o"));
                                }
                            }
                        }

                        if (CachedBatteryPercent > 0) rootKey.SetValue("BatteryPercent", CachedBatteryPercent);
                        if (CachedExactBatteryPercent > 0) rootKey.SetValue("ExactBatteryPercent", CachedExactBatteryPercent.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                        if (!string.IsNullOrEmpty(CachedDeviceName)) rootKey.SetValue("DeviceName", CachedDeviceName);
                        if (CachedDpi > 0) rootKey.SetValue("Dpi", CachedDpi);
                        if (CachedDpiY > 0) rootKey.SetValue("DpiY", CachedDpiY);
                        if (CachedDpiStage > 0) rootKey.SetValue("DpiStage", CachedDpiStage);
                        if (CachedDpiStageCount > 0) rootKey.SetValue("DpiStageCount", CachedDpiStageCount);
                        if (CachedDpiStages != null && CachedDpiStages.Length > 0)
                        {
                            string stStr = string.Join(",", Array.ConvertAll(CachedDpiStages, s => s.ToString()));
                            rootKey.SetValue("DpiStages", stStr);
                        }
                        if (CachedDpiStagesY != null && CachedDpiStagesY.Length > 0)
                        {
                            string stStrY = string.Join(",", Array.ConvertAll(CachedDpiStagesY, s => s.ToString()));
                            rootKey.SetValue("DpiStagesY", stStrY);
                        }
                        rootKey.SetValue("IsXyIndependent", CachedIsXyIndependent ? 1 : 0);
                        rootKey.SetValue("XyRatio", CachedXyRatio.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
                        if (CachedPollingRate > 0) rootKey.SetValue("PollingRate", CachedPollingRate);
                        if (CachedLastUpdated != DateTime.MinValue) rootKey.SetValue("LastUpdated", CachedLastUpdated.ToString("o"));
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/RazerDeviceHelper.cs: 已忽略异常", diagEx); }
        }
    }
}
