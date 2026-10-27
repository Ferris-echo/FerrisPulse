// ============================================================================
// 模块名称: RapooDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   雷柏 (Rapoo) 2.4G/有线双模无线鼠标 HID 报文协议通讯实现 (支持全量设备库识别、Report 0x07 实时遥测电量/DPI双通道监听、DPI、XY 独立档位及 125~8000Hz 动态回报率配置、
//   智能接收器与有线模式探测、EEPROM 写入静默避让保护与双 DPI 极速轮询输出)
// 依赖关系: NativeMethods.cs, MouseBatteryInfo.cs, RazerDeviceHelper.cs
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
    public static class RapooDeviceHelper
    {
        private static readonly object hidLock = new object();
        private const string CacheRegistryKey = @"Software\RazerBatteryTray\RapooCache";

        public static int CachedBatteryPercent = 0;
        public static bool CachedIsCharging = false;
        public static string CachedDeviceName = "雷柏无线游戏鼠标";
        public static DateTime CachedLastUpdated = DateTime.MinValue;
        public static int CachedDpi = 1600;
        public static int CachedDpiY = 1600;
        public static int CachedDpiStage = 1;
        public static int CachedDpiStageCount = 5;
        public static int[] CachedDpiStages = new int[] { 400, 800, 1200, 1600, 3200 };
        public static int[] CachedDpiStagesY = new int[] { 400, 800, 1200, 1600, 3200 };
        public static bool CachedIsXyIndependent = false;
        public static double CachedXyRatio = 1.0;
        public static int CachedPollingRate = 1000;

        public const byte BANKS_COMM = 0x00;
        public const byte BANKS_SYSTEM = 0x08;
        public const byte BANKS_BUTTON = 0x06;

        public const byte ADDR_POLLING_HZ = 0x80;
        public const byte ADDR_KEY_SCANNING_RATE = 0x81;
        public const byte ADDR_LOD_HEIGHT = 0x84;
        public const byte ADDR_MOTION_SYNC = 0x85;
        public const byte ADDR_DPI_TABLE_A = 0x88;
        public const byte ADDR_SLOT_COUNT = 0x96;
        public const byte ADDR_CURRENT_DPI = 0x98;
        public const byte ADDR_DEBOUNCE = 0xC0;
        public const byte ADDR_SLEEP_TIME = 0xC2;
        public const byte ADDR_LINEAR_RIPPLE = 0xC3;
        public const byte ADDR_DPI_TABLE_B = 0xC8;

        const uint GENERIC_READ = 0x80000000;
        const uint GENERIC_WRITE = 0x40000000;
        const uint FILE_SHARE_READ = 1;
        const uint FILE_SHARE_WRITE = 2;
        const uint OPEN_EXISTING = 3;
        const uint FILE_FLAG_OVERLAPPED = 0x40000000;

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetProductString(IntPtr hidDeviceObject, StringBuilder buffer, int bufferLength);

        private static DateTime eepromQuietUntil = DateTime.MinValue;

        public static void SetEepromQuietPeriod(int milliseconds = 800)
        {
            eepromQuietUntil = DateTime.Now.AddMilliseconds(milliseconds);
        }

        private static DateTime lastErrorBackoffUntil = DateTime.MinValue;

        public static bool IsEepromQuietActive()
        {
            return DateTime.Now < eepromQuietUntil;
        }

        static RapooDeviceHelper()
        {
            LoadHardwareCache();
        }

        public static void LoadHardwareCache()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(CacheRegistryKey, false))
                {
                    if (key != null)
                    {
                        var bp = key.GetValue("BatteryPercent");
                        if (bp != null) CachedBatteryPercent = Convert.ToInt32(bp);

                        var chg = key.GetValue("IsCharging");
                        if (chg != null) CachedIsCharging = (Convert.ToInt32(chg) == 1);

                        var dn = key.GetValue("DeviceName") as string;
                        if (!string.IsNullOrEmpty(dn)) CachedDeviceName = dn;

                        var dpi = key.GetValue("Dpi");
                        if (dpi != null) CachedDpi = Convert.ToInt32(dpi);

                        var dpiY = key.GetValue("DpiY");
                        if (dpiY != null) CachedDpiY = Convert.ToInt32(dpiY);
                        else CachedDpiY = CachedDpi;

                        var st = key.GetValue("DpiStage");
                        if (st != null) CachedDpiStage = Convert.ToInt32(st);

                        var stc = key.GetValue("DpiStageCount");
                        if (stc != null) CachedDpiStageCount = Convert.ToInt32(stc);

                        var stListStr = key.GetValue("DpiStages") as string;
                        if (!string.IsNullOrEmpty(stListStr))
                        {
                            string[] parts = stListStr.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length > 0)
                            {
                                List<int> parsed = new List<int>();
                                foreach (var p in parts)
                                {
                                    int v;
                                    if (int.TryParse(p.Trim(), out v)) parsed.Add(v);
                                }
                                if (parsed.Count > 0) CachedDpiStages = parsed.ToArray();
                            }
                        }

                        var stListStrY = key.GetValue("DpiStagesY") as string;
                        if (!string.IsNullOrEmpty(stListStrY))
                        {
                            string[] partsY = stListStrY.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                            if (partsY.Length > 0)
                            {
                                List<int> parsedY = new List<int>();
                                foreach (var p in partsY)
                                {
                                    int v;
                                    if (int.TryParse(p.Trim(), out v)) parsedY.Add(v);
                                }
                                if (parsedY.Count > 0) CachedDpiStagesY = parsedY.ToArray();
                            }
                        }
                        else if (CachedDpiStages != null)
                        {
                            CachedDpiStagesY = (int[])CachedDpiStages.Clone();
                        }

                        var xyInd = key.GetValue("IsXyIndependent");
                        if (xyInd != null) CachedIsXyIndependent = (Convert.ToInt32(xyInd) == 1);

                        var xyRat = key.GetValue("XyRatio");
                        if (xyRat != null)
                        {
                            double rat;
                            if (double.TryParse(xyRat.ToString(), out rat) && rat > 0) CachedXyRatio = rat;
                        }

                        var pr = key.GetValue("PollingRate");
                        if (pr != null) CachedPollingRate = Convert.ToInt32(pr);

                        var updatedStr = key.GetValue("LastUpdated") as string;
                        if (!string.IsNullOrEmpty(updatedStr))
                        {
                            DateTime dt;
                            if (DateTime.TryParse(updatedStr, out dt)) CachedLastUpdated = dt;
                        }
                    }
                }
            }
            catch { }
        }

        public static void SaveHardwareCache()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(CacheRegistryKey))
                {
                    if (key != null)
                    {
                        if (CachedBatteryPercent > 0) key.SetValue("BatteryPercent", CachedBatteryPercent);
                        key.SetValue("IsCharging", CachedIsCharging ? 1 : 0);
                        if (!string.IsNullOrEmpty(CachedDeviceName)) key.SetValue("DeviceName", CachedDeviceName);
                        if (CachedDpi > 0) key.SetValue("Dpi", CachedDpi);
                        if (CachedDpiY > 0) key.SetValue("DpiY", CachedDpiY);
                        if (CachedDpiStage > 0) key.SetValue("DpiStage", CachedDpiStage);
                        if (CachedDpiStageCount > 0) key.SetValue("DpiStageCount", CachedDpiStageCount);
                        if (CachedDpiStages != null && CachedDpiStages.Length > 0)
                        {
                            string stStr = string.Join(",", Array.ConvertAll(CachedDpiStages, s => s.ToString()));
                            key.SetValue("DpiStages", stStr);
                        }
                        if (CachedDpiStagesY != null && CachedDpiStagesY.Length > 0)
                        {
                            string stStrY = string.Join(",", Array.ConvertAll(CachedDpiStagesY, s => s.ToString()));
                            key.SetValue("DpiStagesY", stStrY);
                        }
                        key.SetValue("IsXyIndependent", CachedIsXyIndependent ? 1 : 0);
                        key.SetValue("XyRatio", CachedXyRatio.ToString("F4"));
                        if (CachedPollingRate > 0) key.SetValue("PollingRate", CachedPollingRate);
                        if (CachedLastUpdated != DateTime.MinValue) key.SetValue("LastUpdated", CachedLastUpdated.ToString("o"));
                    }
                }
            }
            catch { }
        }

        private static Dictionary<int, string> rapooCatalog = null;
        private static readonly object catalogLock = new object();

        private static void InitializeRapooCatalog()
        {
            if (rapooCatalog != null) return;
            lock (catalogLock)
            {
                if (rapooCatalog != null) return;
                var cat = new Dictionary<int, string>();
                cat[0x1031] = "雷柏 MT750L 三模无线鼠标";
                cat[0x1032] = "雷柏 MT750S 三模无线鼠标";
                cat[0x1033] = "雷柏 MT750 Pro 三模无线鼠标";
                cat[0x1040] = "雷柏 MT760 多模无线鼠标";
                cat[0x1041] = "雷柏 MT760 Mini 多模无线鼠标";
                cat[0x1050] = "雷柏 MT550 多模无线鼠标";
                cat[0x1051] = "雷柏 MT550S 多模无线鼠标";
                cat[0x1060] = "雷柏 M700 多模无线鼠标";
                cat[0x1065] = "雷柏 M650 多模无线鼠标";
                cat[0x1070] = "雷柏 M500 多模无线鼠标";
                cat[0x1080] = "雷柏 Ralemo Air 1 气垫鼠标";
                cat[0x1215] = "雷柏 VT3 Pro 双模鼠标";
                cat[0x1244] = "雷柏 VT3 Pro Max";
                cat[0x1410] = "雷柏 VT0";
                cat[0x1411] = "雷柏 VT3S";
                cat[0x1412] = "雷柏 VT3";
                cat[0x1413] = "雷柏 VT7 双模电竞鼠标";
                cat[0x1414] = "雷柏 VT9";
                cat[0x1415] = "雷柏 VT1";
                cat[0x1416] = "雷柏 VT9 无线游戏鼠标";
                cat[0x1417] = "雷柏 VT9 Pro 双模鼠标";
                cat[0x1418] = "雷柏 V300 Pro 双模鼠标";
                cat[0x1419] = "雷柏 V300SE 双模鼠标";
                cat[0x141A] = "雷柏 VT9 Air 双模无线鼠标";
                cat[0x1420] = "雷柏 VT0 MAX";
                cat[0x1421] = "雷柏 VT3S MAX";
                cat[0x1422] = "雷柏 VT2";
                cat[0x1423] = "雷柏 VT2 MAX";
                cat[0x1441] = "雷柏 ESPR8PRO";
                cat[0x1444] = "雷柏 VT1 MAX";
                cat[0x144A] = "雷柏 VT0 MAX MASTER";
                cat[0x144B] = "雷柏 VT3S MAX MASTER";
                cat[0x144D] = "雷柏 VPR8 PLUS";
                cat[0x144E] = "雷柏 ESPR8";
                cat[0x1450] = "雷柏 ESPR7PRO";
                cat[0x1451] = "雷柏 VT6";
                cat[0x1452] = "雷柏 VT6 MAX";
                cat[0x1453] = "雷柏 VT9S V2";
                cat[0x1458] = "雷柏 ESPR7";
                cat[0x1459] = "雷柏 VT7S V2";
                cat[0x145A] = "雷柏 VPR8 RT MASTER";
                cat[0x145B] = "雷柏 VT4 V2";
                cat[0x145D] = "雷柏 VT2S V2";
                cat[0x1460] = "雷柏 VT7 V2";
                cat[0x1462] = "雷柏 VT3 V2";
                cat[0x1464] = "雷柏 VT3S V2";
                cat[0x1466] = "雷柏 VT9 V2";
                cat[0x1468] = "雷柏 VT2 V2";
                cat[0x146A] = "雷柏 VT1S V2";
                cat[0x146C] = "雷柏 VT8 V2";
                cat[0x146D] = "雷柏 VT3 MAX MASTER V2";
                cat[0x146E] = "雷柏 VT3S MAX MASTER V2";
                cat[0x1470] = "雷柏 VT7 MAX MASTER V2";
                cat[0x1471] = "雷柏 VT7S MAX MASTER V2";
                cat[0x1472] = "雷柏 VT2 ULTRA V2";
                cat[0x1473] = "雷柏 VT9 MAX MASTER V2";
                cat[0x1474] = "雷柏 VT9S MAX MASTER V2";
                cat[0x1475] = "雷柏 VT2 MAX MASTER V2";
                cat[0x1476] = "雷柏 VT2S MAX MASTER V2";
                cat[0x1477] = "雷柏 VT4 MAX MASTER V2";
                cat[0x1478] = "雷柏 VT8 MAX MASTER V2";
                cat[0x14A1] = "雷柏 VT0 Air MAX";
                cat[0x14A2] = "雷柏 VT3S Air Max";
                cat[0x14A4] = "雷柏 VT2K MAX";
                cat[0x14A5] = "雷柏 VT3 PRO V2";
                cat[0x14A6] = "雷柏 VT3S PRO V2";
                cat[0x14A7] = "雷柏 VT7 PRO V2";
                cat[0x14A8] = "雷柏 VT7S PRO V2";
                cat[0x14A9] = "雷柏 VT9 PRO V2";
                cat[0x14AA] = "雷柏 VT9S PRO V2";
                cat[0x14FF] = "雷柏 V2M 4K 极速电竞接收器";
                cat[0x1500] = "雷柏 V3M 8K 极速电竞接收器";
                cat[0x1501] = "雷柏 V1P 极速电竞接收器";
                cat[0x1905] = "雷柏 V700diy-98";
                cat[0x1906] = "雷柏 V700diy-87";
                cat[0x1907] = "雷柏 V700diy-75";
                cat[0x1914] = "雷柏 A83 Air";
                cat[0x1A00] = "雷柏 VT7 SE";
                cat[0x1A11] = "雷柏 VT1 SE";
                cat[0x1B01] = "雷柏 VT9S MAX V2";
                cat[0x1B02] = "雷柏 VT7S MAX V2";
                cat[0x1B03] = "雷柏 VT4 MAX V2";
                cat[0x1B04] = "雷柏 VT2S MAX V2";
                cat[0x1B05] = "雷柏 VT7 MAX V2";
                cat[0x1B06] = "雷柏 VT3 MAX V2";
                cat[0x1B07] = "雷柏 VT3S MAX V2";
                cat[0x1B08] = "雷柏 VT9 MAX V2";
                cat[0x1B09] = "雷柏 VT2 MAX V2";
                cat[0x1B0A] = "雷柏 VT1S MAX V2";
                cat[0x1B0B] = "雷柏 VT8 MAX V2";
                cat[0x1B0C] = "雷柏 VT3 Air MAX V2";
                cat[0x1B0D] = "雷柏 VT3S Air MAX V2";
                cat[0x1B0E] = "雷柏 VT7 Air MAX V2";
                cat[0x1B0F] = "雷柏 VT7S Air MAX V2";
                cat[0x1B10] = "雷柏 VT9 Air MAX V2";
                cat[0x1B11] = "雷柏 VT9S Air MAX V2";
                cat[0x1B12] = "雷柏 VT2 Air MAX V2";
                cat[0x1B13] = "雷柏 VT2S Air MAX V2";
                cat[0x1B14] = "雷柏 VT4 Air MAX V2";
                cat[0x1B15] = "雷柏 VT8 Air MAX V2";
                cat[0x1B20] = "雷柏 VT2 RT V2";
                cat[0x1B21] = "雷柏 VT2 RT MASTER V2";
                cat[0x3026] = "雷柏 Default";
                cat[0x3102] = "雷柏 VT3 Max (二代)";
                cat[0x3103] = "雷柏 VT9 Pro Mini";
                cat[0x3104] = "雷柏 VT1 Pro 双模鼠标";
                cat[0x3105] = "雷柏 VT1 Pro Max";
                cat[0x3106] = "雷柏 VT1 Air MAX";
                cat[0x3107] = "雷柏 VT3 Air MAX";
                cat[0x4104] = "雷柏 VT1 Pro 双模鼠标 (有线)";
                cat[0x4105] = "雷柏 VT1 Pro Max (有线)";
                cat[0x4215] = "雷柏 VT3 Pro 双模鼠标 (有线)";
                cat[0x4244] = "雷柏 VT3 Pro Max (有线)";
                cat[0x4610] = "雷柏 VT0";
                cat[0x4611] = "雷柏 VT3S";
                cat[0x4612] = "雷柏 VT3";
                cat[0x4613] = "雷柏 VT7 双模电竞鼠标 (有线)";
                cat[0x4614] = "雷柏 VT9";
                cat[0x4615] = "雷柏 VT1";
                cat[0x4616] = "雷柏 VT9 双模游戏鼠标 (有线)";
                cat[0x4617] = "雷柏 VT9 Pro 双模鼠标 (有线)";
                cat[0x4618] = "雷柏 V300 Pro 双模鼠标 (有线)";
                cat[0x4619] = "雷柏 V300SE 双模鼠标 (有线)";
                cat[0x461A] = "雷柏 VT9 Air 双模无线鼠标 (有线)";
                cat[0x4620] = "雷柏 VT0 MAX";
                cat[0x4621] = "雷柏 VT3S MAX";
                cat[0x4622] = "雷柏 VT2";
                cat[0x4623] = "雷柏 VT2 MAX";
                cat[0x4631] = "雷柏 ESM612PRO";
                cat[0x4632] = "雷柏 ESM622PRO";
                cat[0x4633] = "雷柏 ESM652PRO";
                cat[0x4635] = "雷柏 ESM672PRO";
                cat[0x4637] = "雷柏 ESM622MAX";
                cat[0x4640] = "雷柏 ESM672MAX";
                cat[0x4644] = "雷柏 VT1 MAX";
                cat[0x464A] = "雷柏 VT0 MAX MASTER";
                cat[0x464B] = "雷柏 VT3S MAX MASTER";
                cat[0x464D] = "雷柏 ESM612ACE";
                cat[0x4651] = "雷柏 VT6";
                cat[0x4652] = "雷柏 VT6 MAX";
                cat[0x4653] = "雷柏 VT9S V2";
                cat[0x4659] = "雷柏 VT7S V2";
                cat[0x465B] = "雷柏 VT4 V2";
                cat[0x465D] = "雷柏 VT2S V2";
                cat[0x4660] = "雷柏 VT7 V2";
                cat[0x4662] = "雷柏 VT3 V2";
                cat[0x4664] = "雷柏 VT3S V2";
                cat[0x4666] = "雷柏 VT9 V2";
                cat[0x4668] = "雷柏 VT2 V2";
                cat[0x466A] = "雷柏 VT1S V2";
                cat[0x466C] = "雷柏 VT8 V2";
                cat[0x466D] = "雷柏 VT3 MAX MASTER V2";
                cat[0x466E] = "雷柏 VT3S MAX MASTER V2";
                cat[0x4670] = "雷柏 VT7 MAX MASTER V2";
                cat[0x4671] = "雷柏 VT7S MAX MASTER V2";
                cat[0x4672] = "雷柏 VT2 ULTRA V2";
                cat[0x4673] = "雷柏 VT9 MAX MASTER V2";
                cat[0x4674] = "雷柏 VT9S MAX MASTER V2";
                cat[0x4675] = "雷柏 VT2 MAX MASTER V2";
                cat[0x4676] = "雷柏 VT2S MAX MASTER V2";
                cat[0x4677] = "雷柏 VT4 MAX MASTER V2";
                cat[0x4678] = "雷柏 VT8 MAX MASTER V2";
                cat[0x4681] = "雷柏 ESM612MAX";
                cat[0x46A1] = "雷柏 VT0 Air MAX";
                cat[0x46A2] = "雷柏 VT3S Air Max";
                cat[0x46A4] = "雷柏 VT2K MAX";
                cat[0x46A5] = "雷柏 VT3 PRO V2";
                cat[0x46A6] = "雷柏 VT3S PRO V2";
                cat[0x46A7] = "雷柏 VT7 PRO V2";
                cat[0x46A8] = "雷柏 VT7S PRO V2";
                cat[0x46A9] = "雷柏 VT9 PRO V2";
                cat[0x46AA] = "雷柏 VT9S PRO V2";
                cat[0x4805] = "雷柏 V700diy-98";
                cat[0x4806] = "雷柏 V700diy-87";
                cat[0x4807] = "雷柏 V700diy-75";
                cat[0x4814] = "雷柏 A83 Air";
                cat[0x481A] = "雷柏 ESK900-98RT";
                cat[0x481B] = "雷柏 ESK900-83RT";
                cat[0x4825] = "雷柏 P6 PLUS";
                cat[0x4827] = "雷柏 P5 PLUS";
                cat[0x482A] = "雷柏 ESK900-75RT";
                cat[0x4832] = "雷柏 VT68";
                cat[0x4A00] = "雷柏 VT7 SE";
                cat[0x4A11] = "雷柏 VT1 SE";
                cat[0x4B01] = "雷柏 VT9S MAX V2";
                cat[0x4B02] = "雷柏 VT7S MAX V2";
                cat[0x4B03] = "雷柏 VT4 MAX V2";
                cat[0x4B04] = "雷柏 VT2S MAX V2";
                cat[0x4B05] = "雷柏 VT7 MAX V2";
                cat[0x4B06] = "雷柏 VT3 MAX V2";
                cat[0x4B07] = "雷柏 VT3S MAX V2";
                cat[0x4B08] = "雷柏 VT9 MAX V2";
                cat[0x4B09] = "雷柏 VT2 MAX V2";
                cat[0x4B0A] = "雷柏 VT1S MAX V2";
                cat[0x4B0B] = "雷柏 VT8 MAX V2";
                cat[0x4B0C] = "雷柏 VT3 Air MAX V2";
                cat[0x4B0D] = "雷柏 VT3S Air MAX V2";
                cat[0x4B0E] = "雷柏 VT7 Air MAX V2";
                cat[0x4B0F] = "雷柏 VT7S Air MAX V2";
                cat[0x4B10] = "雷柏 VT9 Air MAX V2";
                cat[0x4B11] = "雷柏 VT9S Air MAX V2";
                cat[0x4B12] = "雷柏 VT2 Air MAX V2";
                cat[0x4B13] = "雷柏 VT2S Air MAX V2";
                cat[0x4B14] = "雷柏 VT4 Air MAX V2";
                cat[0x4B15] = "雷柏 VT8 Air MAX V2";
                cat[0x4B20] = "雷柏 VT2 RT V2";
                cat[0x4B21] = "雷柏 VT2 RT MASTER V2";
                cat[0x5745] = "雷柏 ESK800-75RT";
                rapooCatalog = cat;
            }
        }

        public static string GetModelName(int pid)
        {
            InitializeRapooCatalog();
            string name;
            if (rapooCatalog != null && rapooCatalog.TryGetValue(pid, out name))
            {
                return name;
            }

            // Check if wired offset (wired PID = wireless PID + 0x3200 in Rapoo protocol)
            if (pid >= 0x4000)
            {
                int wirelessPid = pid - 0x3200;
                if (rapooCatalog != null && rapooCatalog.TryGetValue(wirelessPid, out name))
                {
                    return name + " (有线)";
                }
            }

            return pid > 0 ? string.Format("雷柏无线鼠标 (PID: 0x{0:X4})", pid) : "雷柏无线游戏鼠标";
        }

        public static int[] GetSupportedPollingRatesForModel(int pid, string modelName)
        {
            bool is1kOnly = false;
            if (!string.IsNullOrEmpty(modelName))
            {
                string lower = modelName.ToLowerInvariant();
                if (lower.Contains(" se") || lower.Contains("v300se") || lower.Contains("vt960") ||
                    lower.Contains("vt350") || lower.Contains("v300w") || lower.Contains("vt200") ||
                    lower.Contains("vt30") || lower.Contains("vt20") || lower.Contains("v20"))
                {
                    is1kOnly = true;
                }
            }
            if (pid == 0x1A00 || pid == 0x1A11 || pid == 0x4619) // VT7 SE, VT1 SE, V300SE
            {
                is1kOnly = true;
            }

            if (is1kOnly)
            {
                return new int[] { 125, 250, 500, 1000 };
            }
            return new int[] { 125, 250, 500, 1000, 2000, 4000, 8000 };
        }

        #region Unified Direct Synchronous HID Endpoint Handles

        private static IntPtr cachedHOut = IntPtr.Zero;
        private static IntPtr cachedHFeat = IntPtr.Zero;
        private static string cachedCol05Path = null;
        private static string cachedCol06Path = null;
        private static string cachedCol09Path = null;
        private static DateTime lastEnumTime = DateTime.MinValue;

        private static SafeFileHandle rapooStreamHandle = null;
        private static FileStream rapooFileStream = null;
        private static IAsyncResult rapooPendingRead = null;
        private static byte[] rapooReadBuf = new byte[19];
        private static DateTime lastTelemetryTime = DateTime.MinValue;

        public static void CloseRapooStream()
        {
            lock (hidLock)
            {
                try
                {
                    if (rapooFileStream != null)
                    {
                        rapooFileStream.Close();
                        rapooFileStream = null;
                    }
                }
                catch { }

                try
                {
                    if (rapooStreamHandle != null)
                    {
                        rapooStreamHandle.Close();
                        rapooStreamHandle = null;
                    }
                }
                catch { }

                rapooPendingRead = null;
                CloseRapooHandles();
            }
        }

        private static bool EnsureRapooStream()
        {
            if (rapooFileStream != null && rapooStreamHandle != null && !rapooStreamHandle.IsInvalid && !rapooStreamHandle.IsClosed)
            {
                return true;
            }

            if (string.IsNullOrEmpty(cachedCol09Path))
            {
                IntPtr ho, hf;
                EnsureHandles(out ho, out hf);
            }

            if (string.IsNullOrEmpty(cachedCol09Path)) return false;

            try
            {
                IntPtr h = RazerDeviceHelper.CreateFile(cachedCol09Path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                if (h != IntPtr.Zero && h.ToInt64() != -1)
                {
                    rapooStreamHandle = new SafeFileHandle(h, true);
                    rapooFileStream = new FileStream(rapooStreamHandle, FileAccess.ReadWrite, 19, true);
                    rapooReadBuf = new byte[19];
                    rapooPendingRead = rapooFileStream.BeginRead(rapooReadBuf, 0, 19, null, null);
                    return true;
                }
                else
                {
                    cachedCol09Path = null;
                }
            }
            catch
            {
                cachedCol09Path = null;
            }
            return false;
        }

        public static bool ProcessTelemetryReport(byte[] report)
        {
            if (report == null || report.Length < 18 || report[0] != 0x07) return false;

            int batt = report[8];
            if (batt > 0 && batt <= 100)
            {
                CachedBatteryPercent = batt;
            }

            CachedIsCharging = (report[7] == 0x02);

            int stage = report[2] + 1;
            if (stage > 0) CachedDpiStage = stage;

            int dpiX = report[3] | (report[4] << 8);
            if (dpiX > 0) CachedDpi = dpiX;

            int dpiY = report[5] | (report[6] << 8);
            if (dpiY > 0) CachedDpiY = dpiY;

            int stageCount = report.Length > 17 ? report[17] : 0;
            if (stageCount > 0 && stageCount <= 10) CachedDpiStageCount = stageCount;

            lastTelemetryTime = DateTime.Now;
            CachedLastUpdated = DateTime.Now;
            SaveHardwareCache();
            return true;
        }

        public static bool PollTelemetryStream()
        {
            lock (hidLock)
            {
                try
                {
                    if (EnsureRapooStream())
                    {
                        if (rapooPendingRead != null && rapooPendingRead.IsCompleted)
                        {
                            int bytesRead = rapooFileStream.EndRead(rapooPendingRead);
                            bool handled = false;
                            if (bytesRead >= 18)
                            {
                                handled = ProcessTelemetryReport(rapooReadBuf);
                            }

                            rapooReadBuf = new byte[19];
                            rapooPendingRead = rapooFileStream.BeginRead(rapooReadBuf, 0, 19, null, null);
                            if (handled) return true;
                        }
                    }
                }
                catch
                {
                    CloseRapooStream();
                }
                return false;
            }
        }

        public static void CloseRapooHandles()
        {
            lock (hidLock)
            {
                if (cachedHOut != IntPtr.Zero)
                {
                    try { RazerDeviceHelper.CloseHandle(cachedHOut); } catch { }
                    cachedHOut = IntPtr.Zero;
                }
                if (cachedHFeat != IntPtr.Zero)
                {
                    try { RazerDeviceHelper.CloseHandle(cachedHFeat); } catch { }
                    cachedHFeat = IntPtr.Zero;
                }
            }
        }

        private static bool EnsureHandles(out IntPtr hOut, out IntPtr hFeat)
        {
            hOut = IntPtr.Zero;
            hFeat = IntPtr.Zero;

            if (cachedHOut != IntPtr.Zero && cachedHFeat != IntPtr.Zero)
            {
                hOut = cachedHOut;
                hFeat = cachedHFeat;
                return true;
            }

            CloseRapooHandles();

            // 1. Try opening cached paths if known
            if (!string.IsNullOrEmpty(cachedCol05Path) && !string.IsNullOrEmpty(cachedCol06Path))
            {
                // ho 用于 HidD_SetOutputReport 仅需写权限；hf 用于 HidD_GetFeature 遵循 0 权限铁律，杜绝占用独占管道
                IntPtr ho = RazerDeviceHelper.CreateFile(cachedCol05Path, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                IntPtr hf = RazerDeviceHelper.CreateFile(cachedCol06Path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (ho != IntPtr.Zero && ho.ToInt64() != -1 && hf != IntPtr.Zero && hf.ToInt64() != -1)
                {
                    cachedHOut = ho;
                    cachedHFeat = hf;
                    hOut = ho;
                    hFeat = hf;
                    return true;
                }
                if (ho != IntPtr.Zero && ho.ToInt64() != -1) RazerDeviceHelper.CloseHandle(ho);
                if (hf != IntPtr.Zero && hf.ToInt64() != -1) RazerDeviceHelper.CloseHandle(hf);
                cachedCol05Path = null;
                cachedCol06Path = null;
            }

            // 2. Rate-limited device enumeration
            if ((DateTime.Now - lastEnumTime).TotalMilliseconds < 1000)
            {
                return false;
            }
            lastEnumTime = DateTime.Now;

            Guid hidGuid;
            RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
            IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
            if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return false;

            RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
            ifData.cbSize = Marshal.SizeOf(ifData);
            uint memberIdx = 0;

            IntPtr foundOut = IntPtr.Zero;
            IntPtr foundFeat = IntPtr.Zero;
            string pathOut = null;
            string pathFeat = null;

            try
            {
                while (RazerDeviceHelper.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                {
                    uint reqSize;
                    RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                    IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                    Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                    if (RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                    {
                        IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                        string devicePath = Marshal.PtrToStringAuto(pDevicePath);
                        if (devicePath != null && devicePath.IndexOf("vid_24ae", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            // 探测阶段使用 0 权限获取端点拓扑能力，避免触碰任何独占锁
                            IntPtr h = RazerDeviceHelper.CreateFile(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                            if (h != IntPtr.Zero && h.ToInt64() != -1)
                            {
                                IntPtr preparsed;
                                if (RazerDeviceHelper.HidD_GetPreparsedData(h, out preparsed))
                                {
                                    RazerDeviceHelper.HIDP_CAPS caps;
                                    RazerDeviceHelper.HidP_GetCaps(preparsed, out caps);
                                    RazerDeviceHelper.HidD_FreePreparsedData(preparsed);

                                    if (caps.UsagePage >= 0xFF00)
                                    {
                                        if (caps.OutputReportByteLength == 33 && foundOut == IntPtr.Zero)
                                        {
                                            RazerDeviceHelper.CloseHandle(h);
                                            foundOut = RazerDeviceHelper.CreateFile(devicePath, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                                            pathOut = devicePath;
                                            Marshal.FreeHGlobal(detailBuffer);
                                            continue;
                                        }
                                        if (caps.FeatureReportByteLength == 33 && foundFeat == IntPtr.Zero)
                                        {
                                            foundFeat = h; // h 保持 0 权限安全直连
                                            pathFeat = devicePath;
                                            Marshal.FreeHGlobal(detailBuffer);
                                            continue;
                                        }
                                        if (caps.InputReportByteLength == 19 && cachedCol09Path == null)
                                        {
                                            cachedCol09Path = devicePath;
                                        }
                                    }
                                }
                                RazerDeviceHelper.CloseHandle(h);
                            }
                        }
                    }
                    Marshal.FreeHGlobal(detailBuffer);
                }

                if (foundOut != IntPtr.Zero && foundFeat != IntPtr.Zero)
                {
                    cachedHOut = foundOut;
                    cachedHFeat = foundFeat;
                    cachedCol05Path = pathOut;
                    cachedCol06Path = pathFeat;
                    hOut = foundOut;
                    hFeat = foundFeat;
                    return true;
                }

                if (foundOut != IntPtr.Zero) RazerDeviceHelper.CloseHandle(foundOut);
                if (foundFeat != IntPtr.Zero) RazerDeviceHelper.CloseHandle(foundFeat);
                return false;
            }
            finally
            {
                RazerDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);
            }
        }

        public static bool ReadRegister(byte bank, byte addr, byte readLen, out byte[] data)
        {
            data = null;
            if (DateTime.Now < lastErrorBackoffUntil) return false;

            lock (hidLock)
            {
                IntPtr hOut, hFeat;
                if (!EnsureHandles(out hOut, out hFeat))
                {
                    lastErrorBackoffUntil = DateTime.Now.AddMilliseconds(400);
                    return false;
                }

                byte[] cmd = new byte[33];
                cmd[0] = 0x06; // Report ID
                cmd[1] = 0xA5; // Sync tag
                cmd[2] = 0xA4; // Read EEPROM opcode
                cmd[3] = readLen; // Length to read
                cmd[4] = addr; // Target register address
                cmd[5] = bank; // Target bank (BANKS_SYSTEM = 0x08)

                bool sendOk = RazerDeviceHelper.HidD_SetOutputReport(hOut, cmd, 33);
                if (!sendOk)
                {
                    CloseRapooHandles();
                    lastErrorBackoffUntil = DateTime.Now.AddMilliseconds(400);
                    return false;
                }

                byte[] feat = new byte[33];
                for (int retry = 0; retry < 3; retry++)
                {
                    if (retry > 0) Thread.Sleep(4);
                    feat[0] = 0x08; // Report ID 0x08
                    bool fOk = RazerDeviceHelper.HidD_GetFeature(hFeat, feat, 33);
                    if (fOk)
                    {
                        if (feat[0] == 0x01)
                        {
                            data = new byte[readLen];
                            Array.Copy(feat, 4, data, 0, Math.Min((int)readLen, 29));
                            return true;
                        }
                        else if (feat[0] == 0x02)
                        {
                            // Mouse busy (2.4G 空中往返中)，等待重试
                            continue;
                        }
                    }
                }

                // 适度减少重试次数（3次），未响应时温和避让 400ms，杜绝死循环卡死
                lastErrorBackoffUntil = DateTime.Now.AddMilliseconds(400);
                return false;
            }
        }

        public static bool WriteRegister(byte bank, byte addr, byte[] payload)
        {
            if (payload == null || payload.Length == 0) return false;
            lock (hidLock)
            {
                IntPtr hOut, hFeat;
                if (!EnsureHandles(out hOut, out hFeat)) return false;

                int dataLen = payload.Length;
                byte[] cmd = new byte[33];
                cmd[0] = 0x06; // Report ID
                cmd[1] = 0xA5; // Sync tag
                cmd[2] = 0xA5; // Write EEPROM opcode
                cmd[3] = (byte)dataLen;
                cmd[4] = addr;
                cmd[5] = bank;
                cmd[6] = 0x00;
                cmd[7] = 0x00;
                Array.Copy(payload, 0, cmd, 8, Math.Min(dataLen, 25));

                bool sendOk = RazerDeviceHelper.HidD_SetOutputReport(hOut, cmd, 33);
                if (!sendOk)
                {
                    CloseRapooHandles();
                    lastErrorBackoffUntil = DateTime.Now.AddMilliseconds(400);
                    return false;
                }

                // Read acknowledge feature report (最多等待 2 次重试)
                byte[] feat = new byte[33];
                for (int retry = 0; retry < 3; retry++)
                {
                    if (retry > 0) Thread.Sleep(4);
                    feat[0] = 0x08;
                    bool fOk = RazerDeviceHelper.HidD_GetFeature(hFeat, feat, 33);
                    if (fOk && feat[0] != 0x02)
                    {
                        break;
                    }
                }

                return true;
            }
        }

        #endregion

        public static MouseBatteryInfo QueryRapooDeviceInfo()
        {
            lock (hidLock)
            {
                IntPtr hOut, hFeat;
                bool dongleFound = EnsureHandles(out hOut, out hFeat);
                if (!dongleFound)
                {
                    return null;
                }

                // 0. 消费遥测输入报告（非阻塞同步电量、实时 DPI、充电状态）
                PollTelemetryStream();
                if (CachedBatteryPercent <= 0 && rapooPendingRead != null)
                {
                    rapooPendingRead.AsyncWaitHandle.WaitOne(50);
                    PollTelemetryStream();
                }

                // 1. Test if mouse is awake and responding over RF
                byte[] stageData;
                bool isOnline = ReadRegister(BANKS_SYSTEM, ADDR_CURRENT_DPI, 1, out stageData);
                int stage = (isOnline && stageData != null && stageData.Length > 0) ? (stageData[0] + 1) : CachedDpiStage;

                // 2. Try reading Product ID from register: Bank 0x00, Addr 0x04, len 2
                int effPid = 0;
                byte[] pidData;
                if (ReadRegister(BANKS_COMM, 0x04, 2, out pidData) && pidData != null && pidData.Length >= 2)
                {
                    effPid = pidData[0] | (pidData[1] << 8);
                }
                if (effPid == 0)
                {
                    // Parse from cached device path
                    if (!string.IsNullOrEmpty(cachedCol05Path))
                    {
                        int pidIdx = cachedCol05Path.IndexOf("pid_", StringComparison.OrdinalIgnoreCase);
                        if (pidIdx >= 0 && pidIdx + 8 <= cachedCol05Path.Length)
                        {
                            string hex = cachedCol05Path.Substring(pidIdx + 4, 4);
                            int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out effPid);
                        }
                    }
                }
                if (effPid == 0) effPid = 0x1413;

                string name = GetModelName(effPid);
                if (string.IsNullOrEmpty(name)) name = CachedDeviceName;

                // 3. Read DPI table if available
                byte[] tableData;
                if (ReadRegister(BANKS_SYSTEM, ADDR_DPI_TABLE_A, 10, out tableData) && tableData != null && tableData.Length >= 2)
                {
                    int numStages = tableData.Length / 2;
                    int[] stages = new int[numStages];
                    for (int i = 0; i < numStages; i++)
                    {
                        stages[i] = tableData[i * 2] | (tableData[i * 2 + 1] << 8);
                    }
                    if (stages[0] >= 50)
                    {
                        CachedDpiStages = stages;
                        CachedDpiStageCount = numStages;
                    }
                }

                if (stage > 0) CachedDpiStage = stage;
                if (CachedDpiStages != null && stage <= CachedDpiStages.Length)
                {
                    CachedDpi = CachedDpiStages[stage - 1];
                }

                int curDpiX = CachedDpi > 0 ? CachedDpi : 1600;
                int curDpiY = CachedDpiY > 0 ? CachedDpiY : curDpiX;
                int curPoll = CachedPollingRate > 0 ? CachedPollingRate : 1000;

                int physicalUsbPid = 0;
                if (!string.IsNullOrEmpty(cachedCol05Path))
                {
                    int pidIdx = cachedCol05Path.IndexOf("pid_", StringComparison.OrdinalIgnoreCase);
                    if (pidIdx >= 0 && pidIdx + 8 <= cachedCol05Path.Length)
                    {
                        string hex = cachedCol05Path.Substring(pidIdx + 4, 4);
                        int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out physicalUsbPid);
                    }
                }

                string prodStr = null;
                try
                {
                    StringBuilder sb = new StringBuilder(256);
                    if (HidD_GetProductString(hOut, sb, sb.Capacity))
                    {
                        prodStr = sb.ToString();
                    }
                }
                catch { }

                bool isReceiver = false;
                if (!string.IsNullOrEmpty(prodStr))
                {
                    string pLower = prodStr.ToLowerInvariant();
                    if (pLower.Contains("receiver") || pLower.Contains("dongle") || pLower.Contains("2.4g") || 
                        pLower.Contains("wireless") || prodStr.Contains("接收器") || prodStr.Contains("无线"))
                    {
                        isReceiver = true;
                    }
                }
                if (!isReceiver && physicalUsbPid > 0)
                {
                    if (physicalUsbPid == 0x14FF || physicalUsbPid == 0x1500 || physicalUsbPid == 0x1501 ||
                        (physicalUsbPid >= 0x1000 && physicalUsbPid < 0x4000))
                    {
                        isReceiver = true;
                    }
                }

                string transport = isReceiver ? "2.4G 无线" : (effPid >= 0x4000 && physicalUsbPid >= 0x4000 ? "USB 有线" : "2.4G 无线");
                if (transport == "2.4G 无线" && name.EndsWith(" (有线)"))
                {
                    name = name.Substring(0, name.Length - 5).Trim();
                }

                int batt = CachedBatteryPercent > 0 ? CachedBatteryPercent : 100;

                CachedLastUpdated = DateTime.Now;
                SaveHardwareCache();

                string devIdStr = "Rapoo:24AE:" + effPid.ToString("X4");

                return new MouseBatteryInfo
                {
                    DeviceId = devIdStr,
                    HardwareFingerprint = "HID:24AE:" + effPid.ToString("X4"),
                    Brand = "Rapoo",
                    DeviceName = name,
                    IsConnected = true,
                    IsSleeping = !isOnline,
                    IsDonglePresent = true,
                    Transport = transport,
                    BatteryPercent = batt,
                    IsCharging = CachedIsCharging,
                    LastUpdated = DateTime.Now,
                    Dpi = curDpiX,
                    DpiY = curDpiY,
                    DpiStage = stage > 0 ? stage : 1,
                    DpiStageCount = CachedDpiStageCount > 0 ? CachedDpiStageCount : 5,
                    DpiStages = (CachedDpiStages != null && CachedDpiStages.Length > 0) ? CachedDpiStages : new int[] { 400, 800, 1200, 1600, 3200 },
                    DpiStagesY = (CachedDpiStagesY != null && CachedDpiStagesY.Length > 0) ? CachedDpiStagesY : new int[] { 400, 800, 1200, 1600, 3200 },
                    IsXyIndependent = CachedIsXyIndependent,
                    XyRatio = CachedXyRatio,
                    PollingRate = curPoll,
                    SupportedPollingRates = GetSupportedPollingRatesForModel(effPid, name)
                };
            }
        }

        public static bool FastQueryDpi(out int curDpi, out int activeStage, out int stageCount)
        {
            int dummyY;
            return FastQueryDpi(out curDpi, out dummyY, out activeStage, out stageCount);
        }

        public static bool FastQueryDpi(out int curDpiX, out int curDpiY, out int activeStage, out int stageCount)
        {
            curDpiX = 0;
            curDpiY = 0;
            activeStage = 0;
            stageCount = 0;

            // 1. 先非阻塞消费遥测流 (Report 7)，若有实时报文或近期收到遥测则极速返回
            bool gotTelemetry = PollTelemetryStream();
            if (gotTelemetry || (DateTime.Now - lastTelemetryTime).TotalSeconds < 3.0)
            {
                if (CachedDpiStage > 0 && CachedDpi > 0)
                {
                    activeStage = CachedDpiStage;
                    stageCount = CachedDpiStageCount > 0 ? CachedDpiStageCount : 5;
                    curDpiX = CachedDpi;
                    curDpiY = CachedDpiY > 0 ? CachedDpiY : curDpiX;
                    return true;
                }
            }

            // 2. 若遥测尚未收到，兜底尝试 EEPROM 寄存器读取
            if (IsEepromQuietActive()) return false;

            byte[] data;
            if (ReadRegister(BANKS_SYSTEM, ADDR_CURRENT_DPI, 1, out data) && data != null && data.Length > 0)
            {
                int stageIdx = data[0];
                int count = CachedDpiStageCount > 0 ? CachedDpiStageCount : 5;
                if (stageIdx >= 0 && stageIdx < count)
                {
                    activeStage = stageIdx + 1;
                    stageCount = count;
                    if (CachedDpiStages != null && stageIdx < CachedDpiStages.Length)
                    {
                        curDpiX = CachedDpiStages[stageIdx];
                    }
                    else
                    {
                        curDpiX = CachedDpi;
                    }

                    if (CachedDpiStagesY != null && stageIdx < CachedDpiStagesY.Length)
                    {
                        curDpiY = CachedDpiStagesY[stageIdx];
                    }
                    else
                    {
                        curDpiY = curDpiX;
                    }

                    CachedDpiStage = activeStage;
                    CachedDpi = curDpiX;
                    CachedDpiY = curDpiY;

                    return true;
                }
            }

            return false;
        }

        public static bool FastQueryPollingRate(out int hz)
        {
            hz = 0;
            byte[] rateData;
            if (ReadRegister(BANKS_SYSTEM, ADDR_POLLING_HZ, 1, out rateData) && rateData != null && rateData.Length > 0)
            {
                hz = DecodeRapooPollingRate(rateData[0]);
                if (hz > 0)
                {
                    CachedPollingRate = hz;
                    return true;
                }
            }
            return false;
        }

        #region Rapoo 硬件控制协议实现 (DPI / 回报率 / 档位)

        public static byte EncodeRapooPollingRate(int hz)
        {
            switch (hz)
            {
                case 8000: return 0x81;
                case 4000: return 0x82;
                case 2000: return 0x84;
                case 1000: return 0x01;
                case 500:  return 0x02;
                case 250:  return 0x04;
                case 125:  return 0x08;
                default:   return 0x01;
            }
        }

        public static int DecodeRapooPollingRate(byte code)
        {
            switch (code)
            {
                case 0x81: return 8000;
                case 0x82: return 4000;
                case 0x84: return 2000;
                case 0x01: return 1000;
                case 0x02: return 500;
                case 0x04: return 250;
                case 0x08: return 125;
                default:   return 1000;
            }
        }

        public static byte GetRapooPerfModeAddr(int hz)
        {
            switch (hz)
            {
                case 125:  return 0xDC;
                case 250:  return 0xDD;
                case 500:  return 0xDE;
                case 1000: return 0xDF;
                case 2000: return 0xE0;
                case 4000: return 0xE1;
                case 8000: return 0xE2;
                default:   return 0xDF;
            }
        }

        public static byte GetRapooPerfModeCode(int hz)
        {
            if (hz <= 250) return 0x01; // low
            if (hz <= 1000) return 0x02; // hp
            if (hz <= 2000) return 0x04; // sport
            return 0x05; // oc (4000 / 8000)
        }

        public static bool SetRapooPollingRate(int hz)
        {
            try
            {
                byte code = EncodeRapooPollingRate(hz);
                bool ok1 = WriteRegister(BANKS_SYSTEM, ADDR_POLLING_HZ, new byte[] { code });

                byte perfAddr = GetRapooPerfModeAddr(hz);
                byte perfCode = GetRapooPerfModeCode(hz);
                bool ok2 = WriteRegister(BANKS_SYSTEM, perfAddr, new byte[] { perfCode });

                if (ok1 || ok2)
                {
                    SetEepromQuietPeriod(800);
                    CachedPollingRate = hz;
                    SaveHardwareCache();
                    return true;
                }
            }
            catch { }
            return false;
        }

        public static bool SetRapooDpiStage(int targetStage)
        {
            try
            {
                int idx = Math.Max(0, targetStage - 1);
                bool ok = WriteRegister(BANKS_SYSTEM, ADDR_CURRENT_DPI, new byte[] { (byte)idx });
                if (ok)
                {
                    SetEepromQuietPeriod(800);
                    CachedDpiStage = targetStage;
                    if (CachedDpiStages != null && idx < CachedDpiStages.Length)
                    {
                        CachedDpi = CachedDpiStages[idx];
                    }
                    if (CachedDpiStagesY != null && idx < CachedDpiStagesY.Length)
                    {
                        CachedDpiY = CachedDpiStagesY[idx];
                    }
                    SaveHardwareCache();
                    return true;
                }
            }
            catch { }
            return false;
        }

        public static bool SetRapooDpi(int dpi)
        {
            return SetRapooXyDpi(dpi, dpi, CachedDpiStage > 0 ? CachedDpiStage : 1);
        }

        public static bool SetRapooXyDpi(int dpiX, int dpiY, int stageIndex)
        {
            if (CachedDpiStages == null || CachedDpiStages.Length == 0)
            {
                CachedDpiStages = new int[] { 400, 800, 1200, 1600, 3200 };
            }
            if (CachedDpiStagesY == null || CachedDpiStagesY.Length != CachedDpiStages.Length)
            {
                CachedDpiStagesY = (int[])CachedDpiStages.Clone();
            }

            int idx = Math.Max(0, Math.Min(stageIndex - 1, CachedDpiStages.Length - 1));
            int[] newStagesX = (int[])CachedDpiStages.Clone();
            int[] newStagesY = (int[])CachedDpiStagesY.Clone();
            newStagesX[idx] = dpiX;
            newStagesY[idx] = dpiY;

            return UpdateRapooHardwareStages(idx + 1, newStagesX, newStagesY);
        }

        public static bool UpdateRapooHardwareStages(int activeStage, int[] stages)
        {
            return UpdateRapooHardwareStages(activeStage, stages, stages);
        }

        public static bool UpdateRapooHardwareStages(int activeStage, int[] stagesX, int[] stagesY)
        {
            if (stagesX == null || stagesX.Length == 0) return false;
            if (stagesY == null || stagesY.Length < stagesX.Length) stagesY = stagesX;

            try
            {
                int count = Math.Min(6, Math.Max(1, stagesX.Length));
                int idx = Math.Max(0, Math.Min(activeStage - 1, count - 1));

                // 1. 写 X 轴 DPI 表 (Bank 0x08, Addr 0x88, count * 2 bytes, u16le)
                byte[] tableA = new byte[count * 2];
                for (int i = 0; i < count; i++)
                {
                    int v = stagesX[i];
                    tableA[i * 2] = (byte)(v & 0xFF);
                    tableA[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
                }
                bool okA = WriteRegister(BANKS_SYSTEM, ADDR_DPI_TABLE_A, tableA);

                // 2. 写 Y 轴 DPI 表 (Bank 0x08, Addr 0xC8, count * 2 bytes, u16le)
                byte[] tableB = new byte[count * 2];
                for (int i = 0; i < count; i++)
                {
                    int v = stagesY[i];
                    tableB[i * 2] = (byte)(v & 0xFF);
                    tableB[i * 2 + 1] = (byte)((v >> 8) & 0xFF);
                }
                bool okB = WriteRegister(BANKS_SYSTEM, ADDR_DPI_TABLE_B, tableB);

                // 3. 写有效档位数 (Bank 0x08, Addr 0x96, 0..5 对应 1..6 档)
                byte slotCountCode = (byte)(count - 1);
                WriteRegister(BANKS_SYSTEM, ADDR_SLOT_COUNT, new byte[] { slotCountCode });

                // 4. 写当前激活档位 (Bank 0x08, Addr 0x98, 0-indexed)
                byte activeIdxCode = (byte)idx;
                WriteRegister(BANKS_SYSTEM, ADDR_CURRENT_DPI, new byte[] { activeIdxCode });

                if (okA || okB)
                {
                    SetEepromQuietPeriod(800);
                    CachedDpi = stagesX[idx];
                    CachedDpiY = stagesY[idx];
                    CachedDpiStage = idx + 1;
                    CachedDpiStageCount = count;
                    CachedDpiStages = stagesX;
                    CachedDpiStagesY = stagesY;
                    CachedIsXyIndependent = (CachedDpi != CachedDpiY);
                    CachedXyRatio = (CachedDpi > 0) ? ((double)CachedDpiY / CachedDpi) : 1.0;
                    SaveHardwareCache();
                    return true;
                }
            }
            catch { }
            return false;
        }

        #endregion
    }
}
