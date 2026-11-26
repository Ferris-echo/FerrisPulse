// ============================================================================
// 模块名称: RapooDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   雷柏 (Rapoo) 2.4G/有线双模无线鼠标 HID 报文协议通讯实现 (支持全量设备库识别、33B EEPROM + 非阻塞 19B 老型号自动回退、Report 0x07 实时遥测电量/DPI双通道监听、DPI、XY 独立档位及 125~8000Hz 动态回报率配置、
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
    public static partial class RapooDeviceHelper
    {
        private static readonly object hidLock = new object();
        private const string CacheRegistryKey = @"Software\RazerBatteryTray\RapooCache";

        public static int CachedBatteryPercent = -1;
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

        public static void SetEepromQuietPeriod(int milliseconds = 350)
        {
            eepromQuietUntil = DateTime.Now.AddMilliseconds(milliseconds);
        }

        public static bool IsEepromQuietActive()
        {
            return DateTime.Now < eepromQuietUntil;
        }



        public static void LoadHardwareCache()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(CacheRegistryKey, false))
                {
                    if (key != null)
                    {
                        // Battery and its sample time load only after physical endpoint matching.

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
                        if (CachedBatteryPercent >= 0 && CachedBatteryPercent <= 100 && CachedLastUpdated != DateTime.MinValue && activeRapooGroup != null)
                        {
                            key.SetValue("BatteryPercent", CachedBatteryPercent);
                            key.SetValue("PhysicalGroup", activeRapooGroup);
                            key.SetValue("BatterySource", "HidStatus07");
                        }
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
        private static DateTime lastEnumTime = DateTime.MinValue;

        public static void CloseRapooStream()
        {
            lock (hidLock)
            {
                StopStatusReader();
                CloseRapooHandles();
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
            bool valid = HidTransport.IsValidHandle(cachedHOut) && HidTransport.IsValidHandle(cachedHFeat);
            if ((DateTime.UtcNow - lastEnumTime).TotalSeconds < 2)
            {
                if (valid) { hOut = cachedHOut; hFeat = cachedHFeat; }
                return valid;
            }
            lastEnumTime = DateTime.UtcNow;
            RapooEndpointSet selected = RapooProtocol.SelectEndpoints(HidTransport.Enumerate(0x24AE, 0));
            if (selected == null)
            {
                CloseRapooHandles();
                StopStatusReader();
                return false;
            }
            bool same = activeRapooEndpoints != null &&
                string.Equals(selected.Control.DevicePath, cachedCol05Path, StringComparison.OrdinalIgnoreCase) &&
                string.Equals(selected.Feature.DevicePath, cachedCol06Path, StringComparison.OrdinalIgnoreCase);
            if (!same || !valid)
            {
                CloseRapooHandles();
                StopStatusReader();
                if (!string.Equals(activeRapooGroup, selected.GroupKey, StringComparison.OrdinalIgnoreCase))
                {
                    bool firstEnumeration = string.IsNullOrEmpty(activeRapooGroup);
                    ResetRapooLiveState();
                    activeRapooGroup = selected.GroupKey;
                    LoadBatteryCacheForGroup(activeRapooGroup);
                    if (firstEnumeration)
                    {
                        // 程序启动首次枚举：从注册表恢复 DPI/档位/回报率配置缓存。
                        // 避免启动时因缓存为空强制走完整寄存器查询路径，
                        // 对 2.4G 接收器的 Write 控制传输会暂停鼠标输入。
                        LoadHardwareCache();
                    }
                }
                IntPtr control = RazerDeviceHelper.CreateFile(selected.Control.DevicePath, GENERIC_WRITE,
                    FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                IntPtr feature = RazerDeviceHelper.CreateFile(selected.Feature.DevicePath, 0,
                    FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (!HidTransport.IsValidHandle(control) || !HidTransport.IsValidHandle(feature))
                {
                    HidTransport.Close(control);
                    HidTransport.Close(feature);
                    return false;
                }
                cachedHOut = control;
                cachedHFeat = feature;
                cachedCol05Path = selected.Control.DevicePath;
                cachedCol06Path = selected.Feature.DevicePath;
                // Unlock the same control protocol used by rapoo-tray; no settings change.
                byte[] unlock = new byte[33];
                unlock[0] = 0x06; unlock[1] = 0xA5; unlock[2] = 0xA3;
                RapooNativeIo.Write(cachedHOut, unlock, 500);
            }
            activeRapooEndpoints = selected;
            EnsureStatusReader(selected.Status);
            hOut = cachedHOut;
            hFeat = cachedHFeat;
            return true;
        }

        public static bool ReadRegister(byte bank, byte addr, byte readLen, out byte[] data)
        {
            data = null;
            if (readLen == 0 || readLen > 25) return false;
            lock (hidLock)
            {
                IntPtr hOut, hFeat;
                if (!EnsureHandles(out hOut, out hFeat)) return false;

                byte[] cmd = new byte[33];
                cmd[0] = 0x06; // Report ID
                cmd[1] = 0xA5; // Sync tag
                cmd[2] = 0xA4; // Read EEPROM opcode
                cmd[3] = readLen; // Length to read
                cmd[4] = addr; // Target register address
                cmd[5] = bank; // Target bank (BANKS_SYSTEM = 0x08)

                bool sendOk = RapooNativeIo.Write(hOut, cmd, 500);
                if (!sendOk)
                {
                    CloseRapooHandles();
                    return false;
                }

                byte[] feat = new byte[33];
                for (int retry = 0; retry < 5; retry++)
                {
                    Thread.Sleep(5);
                    feat[0] = 0x08; // Report ID 0x08
                    bool fOk = RazerDeviceHelper.HidD_GetFeature(hFeat, feat, 33);
                    if (fOk && RapooProtocol.TryParseFeature(feat, readLen, out data)) return true;
                }

                return false;
            }
        }

        public static bool WriteRegister(byte bank, byte addr, byte[] payload)
        {
            lock (hidLock) { return WriteRegisterForGroup(activeRapooGroup, bank, addr, payload); }
        }

        private static bool WriteRegisterForGroup(string expectedGroup, byte bank, byte addr, byte[] payload)
        {
            if (string.IsNullOrEmpty(expectedGroup) || payload == null || payload.Length == 0 || payload.Length > 25) return false;
            lock (hidLock)
            {
                IntPtr hOut, hFeat;
                if (!EnsureHandles(out hOut, out hFeat) || !string.Equals(expectedGroup, activeRapooGroup, StringComparison.OrdinalIgnoreCase)) return false;

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

                bool sendOk = RapooNativeIo.Write(hOut, cmd, 500);
                if (!sendOk)
                {
                    CloseRapooHandles();
                    return false;
                }

                // A successful USB write/ACK is not evidence the setting changed.
                // Read back the exact register bytes before publishing success.
                for (int retry = 0; retry < 3; retry++)
                {
                    Thread.Sleep(20);
                    byte[] actual;
                    if (!ReadRegister(bank, addr, (byte)payload.Length, out actual)) continue;
                    if (!string.Equals(expectedGroup, activeRapooGroup, StringComparison.OrdinalIgnoreCase)) return false;
                    bool equal = true;
                    for (int i = 0; i < payload.Length; i++)
                    {
                        if (actual[i] != payload[i]) { equal = false; break; }
                    }
                    if (equal) return true;
                }
                return false;
            }
        }

        #endregion

        public static bool TryParseLegacy19ByteReport(
            byte[] buf,
            int length,
            out int battery,
            out bool isCharging,
            out int dpi,
            out int stage,
            out int pid,
            out int totalStages)
        {
            battery = -1;
            isCharging = false;
            dpi = 0;
            stage = 0;
            pid = 0;
            totalStages = 5;

            if (buf == null || length < 18 || buf.Length < 18) return false;
            if (buf[0] != 0x07) return false;

            int parsedBattery = buf[8];
            if (parsedBattery < 0 || parsedBattery > 100) return false;

            dpi = buf[3] | (buf[4] << 8);
            stage = buf[7];
            battery = parsedBattery;
            isCharging = (buf[9] != 0);
            pid = buf[13] | (buf[14] << 8);
            totalStages = buf[17] > 0 ? buf[17] : 5;
            return true;
        }

        private static MouseBatteryInfo QueryLegacy19ByteRapooDevice()
        {
            if (HardwareCommandBus.IsHardwareSandboxActive) return null;
            try
            {
                List<HidDeviceDescriptor> descriptors = HidTransport.Enumerate(0x24AE, 0);
                if (descriptors == null || descriptors.Count == 0) return null;

                bool foundLegacyEndpoint = false;
                int fallbackPid = 0;
                string fallbackProductName = "";

                for (int i = 0; i < descriptors.Count; i++)
                {
                    HidDeviceDescriptor ep = descriptors[i];
                    if (ep == null || string.IsNullOrEmpty(ep.DevicePath)) continue;
                    if (ep.ProductId == 0x1851 || ep.ProductId == 0x1870) continue;
                    if (HardwareDriverRegistry.IsExternallyOwnedPath(ep.DevicePath)) continue;
                    if (ep.UsagePage < 0xFF00 || ep.InputReportLength != 19) continue;

                    foundLegacyEndpoint = true;
                    if (fallbackPid == 0 && ep.ProductId > 0) fallbackPid = ep.ProductId;
                    if (string.IsNullOrEmpty(fallbackProductName) && !string.IsNullOrEmpty(ep.ProductString))
                    {
                        fallbackProductName = ep.ProductString;
                    }

                    IntPtr handle = HidTransport.Open(ep.DevicePath);
                    if (!HidTransport.IsValidHandle(handle)) continue;

                    try
                    {
                        byte[] query = new byte[19];
                        query[0] = 0x07;
                        query[1] = 0x10;
                        query[2] = 0x02;
                        int sum = 0;
                        for (int b = 0; b < 18; b++) sum += query[b];
                        query[18] = (byte)(sum & 0xFF);

                        if (!RazerDeviceHelper.HidD_SetOutputReport(handle, query, 19)) continue;

                        for (int attempt = 0; attempt < 2; attempt++)
                        {
                            byte[] buf;
                            if (!HidTransport.ReadReport(handle, 19, 300, out buf)) continue;

                            int battery;
                            bool isCharging;
                            int dpi;
                            int stage;
                            int pid;
                            int totalStages;
                            if (TryParseLegacy19ByteReport(buf, buf.Length, out battery, out isCharging, out dpi, out stage, out pid, out totalStages))
                            {
                                int effPid = pid > 0 ? pid : (ep.ProductId > 0 ? ep.ProductId : 0x4613);
                                string name = GetModelName(effPid);
                                if (string.IsNullOrEmpty(name))
                                {
                                    name = !string.IsNullOrEmpty(ep.ProductString) ? ep.ProductString : CachedDeviceName;
                                }

                                string prodLower = (ep.ProductString ?? "").ToLowerInvariant();
                                bool isReceiver = prodLower.Contains("receiver") || prodLower.Contains("dongle") ||
                                    (ep.ProductId >= 0x1000 && ep.ProductId < 0x4000);
                                string transport = isReceiver ? "2.4G 无线" : "USB 有线";
                                if (transport == "2.4G 无线" && name.EndsWith(" (有线)"))
                                {
                                    name = name.Substring(0, name.Length - 5).Trim();
                                }

                                int[] stages = new int[] { 400, 800, 1200, 1600, 3200 };
                                if (battery > 0) CachedBatteryPercent = battery;
                                CachedIsCharging = isCharging;
                                if (!string.IsNullOrEmpty(name)) CachedDeviceName = name;
                                if (dpi > 0)
                                {
                                    CachedDpi = dpi;
                                    CachedDpiY = dpi;
                                }
                                if (stage > 0) CachedDpiStage = stage;
                                CachedDpiStageCount = totalStages;
                                CachedDpiStages = stages;
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
                                    IsSleeping = false,
                                    IsDonglePresent = true,
                                    Transport = transport,
                                    BatteryPercent = battery,
                                    ExactBatteryPercent = battery,
                                    IsCharging = isCharging,
                                    LastUpdated = DateTime.Now,
                                    Dpi = dpi > 0 ? dpi : CachedDpi,
                                    DpiY = dpi > 0 ? dpi : CachedDpi,
                                    DpiStage = stage > 0 ? stage : CachedDpiStage,
                                    DpiStageCount = totalStages,
                                    DpiStages = stages,
                                    DpiStagesY = stages,
                                    PollingRate = CachedPollingRate > 0 ? CachedPollingRate : 1000,
                                    SupportedPollingRates = GetSupportedPollingRatesForModel(effPid, name)
                                };
                            }
                        }
                    }
                    finally
                    {
                        HidTransport.Close(handle);
                    }
                }

                if (foundLegacyEndpoint)
                {
                    int effPid = fallbackPid > 0 ? fallbackPid : 0x4613;
                    string name = GetModelName(effPid);
                    if (string.IsNullOrEmpty(name))
                    {
                        name = !string.IsNullOrEmpty(fallbackProductName) ? fallbackProductName : CachedDeviceName;
                    }
                    if (name.EndsWith(" (有线)") && effPid < 0x4000)
                    {
                        name = name.Substring(0, name.Length - 5).Trim();
                    }
                    int cachedBatt = CachedBatteryPercent > 0 ? CachedBatteryPercent : -1;
                    return new MouseBatteryInfo
                    {
                        DeviceId = "Rapoo:24AE:" + effPid.ToString("X4"),
                        HardwareFingerprint = "HID:24AE:" + effPid.ToString("X4"),
                        Brand = "Rapoo",
                        DeviceName = name,
                        IsConnected = true,
                        IsSleeping = true,
                        IsDonglePresent = true,
                        Transport = "2.4G 无线",
                        BatteryPercent = cachedBatt,
                        ExactBatteryPercent = cachedBatt,
                        IsCharging = false,
                        LastUpdated = CachedLastUpdated != DateTime.MinValue ? CachedLastUpdated : DateTime.Now,
                        Dpi = CachedDpi > 0 ? CachedDpi : 1600,
                        DpiY = CachedDpiY > 0 ? CachedDpiY : (CachedDpi > 0 ? CachedDpi : 1600),
                        DpiStage = CachedDpiStage > 0 ? CachedDpiStage : 1,
                        DpiStageCount = CachedDpiStageCount > 0 ? CachedDpiStageCount : 5,
                        DpiStages = (CachedDpiStages != null && CachedDpiStages.Length > 0) ? CachedDpiStages : new int[] { 400, 800, 1200, 1600, 3200 },
                        DpiStagesY = (CachedDpiStagesY != null && CachedDpiStagesY.Length > 0) ? CachedDpiStagesY : new int[] { 400, 800, 1200, 1600, 3200 },
                        PollingRate = CachedPollingRate > 0 ? CachedPollingRate : 1000,
                        SupportedPollingRates = GetSupportedPollingRatesForModel(effPid, name)
                    };
                }
            }
            catch { }
            return null;
        }

        public static MouseBatteryInfo QueryRapooDeviceInfo()
        {
            lock (hidLock)
            {
                IntPtr hOut, hFeat;
                if (!EnsureHandles(out hOut, out hFeat)) return null;
                string queryGroup = activeRapooGroup;
                int physicalPid = activeRapooEndpoints.Control.ProductId;
                int modelPid = physicalPid;
                bool online;
                if (physicalPid == 0x14FF || physicalPid == 0x1500 || physicalPid == 0x1501)
                {
                    return QueryLegacy19ByteRapooDevice();
                }

                // 缓存有效快速路径：不发送任何控制命令，直接用缓存与状态帧构建结果。
                // VT3 的 2.4G 接收器在处理 Write/GetFeature 控制传输时会暂停转发鼠标输入报告，
                // 导致每次查询期间鼠标卡住。DPI/回报率变化由 0x07 状态广播帧被动更新缓存，
                // 因此启动与周期刷新无需主动读寄存器。
                if (HasValidPerformanceCache(modelPid))
                {
                    online = (DateTime.UtcNow - StatusLastUpdated).TotalSeconds < 5;
                    activeRapooModelPid = modelPid;
                    return BuildRapooDeviceInfo(modelPid, online, physicalPid >= 0x4000);
                }

                // 缓存缺失或设备变化：执行完整寄存器查询（仅在首次或设备切换时发生）
                byte[] stageData;
                online = ReadRegister(BANKS_SYSTEM, ADDR_CURRENT_DPI, 1, out stageData);
                // Status broadcast silence is normal for an idle VT3. A successful
                // control read or a recent input report is positive online evidence.
                online = online || (DateTime.UtcNow - StatusLastUpdated).TotalSeconds < 5;
                if (physicalPid == 0x14FF || physicalPid == 0x1500 || physicalPid == 0x1501)
                {
                    byte[] reported;
                    if (ReadRegister(BANKS_COMM, 0x04, 2, out reported))
                    {
                        int resolved = ResolveRapooProductId(physicalPid, reported[0] | (reported[1] << 8));
                        if (resolved != physicalPid) modelPid = resolved;
                    }
                }
                if (!string.Equals(queryGroup, activeRapooGroup, StringComparison.OrdinalIgnoreCase)) return null;
                if (activeRapooModelPid != 0 && activeRapooModelPid != modelPid) ResetRapooLiveState();
                activeRapooModelPid = modelPid;
                RefreshRapooPerformanceCache(stageData);
                if (!string.Equals(queryGroup, activeRapooGroup, StringComparison.OrdinalIgnoreCase)) return null;
                CachedDeviceName = GetModelName(modelPid);
                SaveHardwareCache();
                return BuildRapooDeviceInfo(modelPid, online, physicalPid >= 0x4000);
            }
        }

        // 判断缓存是否足够构建完整的设备快照（电量、DPI、档位表、回报率均有效）
        // 注意：activeRapooModelPid 是内存状态，程序重启后为 0，因此不参与缓存有效性判断。
        // 注册表缓存仅保存当前设备数据，电量与性能字段有效即可直接复用。
        private static bool HasValidPerformanceCache(int modelPid)
        {
            return CachedBatteryPercent >= 0 &&
                   CachedDpi > 0 &&
                   CachedDpiStages != null && CachedDpiStages.Length > 0 &&
                   CachedPollingRate > 0;
        }

        public static int ResolveRapooProductId(int physicalPid, int reportedPid)
        {
            if ((physicalPid == 0x14FF || physicalPid == 0x1500 || physicalPid == 0x1501) &&
                reportedPid > 0 && reportedPid < 0xFFFF && reportedPid != 0x14FF && reportedPid != 0x1500 && reportedPid != 0x1501)
                return reportedPid;
            return physicalPid;
        }

        private static void RefreshRapooPerformanceCache(byte[] stageData)
        {
            if (IsEepromQuietActive()) return;
            byte[] slots;
            if (!ReadRegister(BANKS_SYSTEM, ADDR_SLOT_COUNT, 1, out slots) || slots[0] >= 6) return;
            int count = slots[0] + 1;
            byte[] tableX, tableY;
            if (ReadRegister(BANKS_SYSTEM, ADDR_DPI_TABLE_A, (byte)(count * 2), out tableX) &&
                ReadRegister(BANKS_SYSTEM, ADDR_DPI_TABLE_B, (byte)(count * 2), out tableY))
            {
                int[] valuesX = new int[count];
                int[] valuesY = new int[count];
                for (int i = 0; i < count; i++)
                {
                    valuesX[i] = tableX[i * 2] | (tableX[i * 2 + 1] << 8);
                    valuesY[i] = tableY[i * 2] | (tableY[i * 2 + 1] << 8);
                    if (valuesX[i] < 50 || valuesY[i] < 50) return;
                }
                CachedDpiStages = valuesX;
                CachedDpiStagesY = valuesY;
                CachedDpiStageCount = count;
                if (stageData != null && stageData.Length > 0 && stageData[0] < count)
                {
                    CachedDpiStage = stageData[0] + 1;
                    CachedDpi = valuesX[stageData[0]];
                    CachedDpiY = valuesY[stageData[0]];
                    CachedIsXyIndependent = CachedDpi != CachedDpiY;
                    CachedXyRatio = (double)CachedDpiY / CachedDpi;
                }
            }
            int hz;
            FastQueryPollingRate(out hz);
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

            if (IsEepromQuietActive()) return false;
            lock (hidLock)
            {
                // 缓存有效快速路径：直接返回缓存的 DPI/档位，不发送控制命令。
                // VT3 的 2.4G 接收器处理 Write/GetFeature 时会暂停鼠标输入转发，
                // DpiMonitorWorker 每 200ms 轮询一次，若每次都发命令会导致鼠标持续卡顿。
                // DPI 变化由 0x07 状态广播帧被动更新 CachedDpi，无需主动查询。
                if (CachedDpi > 0 && CachedDpiY > 0 && CachedDpiStageCount > 0)
                {
                    curDpiX = CachedDpi;
                    curDpiY = CachedDpiY;
                    activeStage = CachedDpiStage;
                    stageCount = CachedDpiStageCount;
                    return true;
                }
            }

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
            // 缓存有效快速路径：回报率变化不频繁，直接返回缓存值，不发送控制命令。
            // 避免 DpiMonitorWorker 轮询时对 2.4G 接收器发 ReadRegister 导致鼠标卡顿。
            if (CachedPollingRate > 0)
            {
                hz = CachedPollingRate;
                return true;
            }
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
                default:   return 0;
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
                default:   return 0;
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
            if (HardwareCommandBus.IsHardwareSandboxActive) return false;
            if (hz != 125 && hz != 250 && hz != 500 && hz != 1000 && hz != 2000 && hz != 4000 && hz != 8000) return false;
            try
            {
                // rapoo-tray uses only bank 08 / address 80 for polling rate.
                // Do not write speculative per-rate performance registers.
                if (!WriteRegister(BANKS_SYSTEM, ADDR_POLLING_HZ, new byte[] { EncodeRapooPollingRate(hz) })) return false;
                CachedPollingRate = hz;
                SetEepromQuietPeriod(350);
                SaveHardwareCache();
                return true;
            }
            catch { return false; }
        }

        private static bool ApplyConfirmedRapooDpiStage(string expectedGroup, int targetStage)
        {
            if (string.IsNullOrEmpty(expectedGroup) || !string.Equals(expectedGroup, activeRapooGroup, StringComparison.OrdinalIgnoreCase) ||
                targetStage < 1 || targetStage > CachedDpiStageCount || targetStage > CachedDpiStages.Length ||
                targetStage > CachedDpiStagesY.Length) return false;
            int idx = targetStage - 1;
            CachedDpiStage = targetStage;
            CachedDpi = CachedDpiStages[idx];
            CachedDpiY = CachedDpiStagesY[idx];
            CachedIsXyIndependent = CachedDpi != CachedDpiY;
            CachedXyRatio = (double)CachedDpiY / CachedDpi;
            return true;
        }

        public static bool SetRapooDpiStage(int targetStage)
        {
            if (HardwareCommandBus.IsHardwareSandboxActive) return false;
            if (targetStage < 1 || targetStage > 6 || CachedDpiStageCount == 0 || (CachedDpiStages != null && targetStage > CachedDpiStages.Length)) return false;
            try
            {
                string expectedGroup = activeRapooGroup;
                if (targetStage < 1 || targetStage > CachedDpiStageCount || targetStage > CachedDpiStages.Length ||
                    targetStage > CachedDpiStagesY.Length) return false;
                if (!WriteRegisterForGroup(expectedGroup, BANKS_SYSTEM, ADDR_CURRENT_DPI, new byte[] { (byte)(targetStage - 1) })) return false;
                if (!ApplyConfirmedRapooDpiStage(expectedGroup, targetStage)) return false;
                SetEepromQuietPeriod(350);
                SaveHardwareCache();
                return true;
            }
            catch { return false; }
        }

        public static bool SetRapooDpi(int dpi)
        {
            return SetRapooXyDpi(dpi, dpi, CachedDpiStage > 0 ? CachedDpiStage : 1);
        }

        public static bool SetRapooXyDpi(int dpiX, int dpiY, int stageIndex)
        {
            if (dpiX < 50 || dpiX > 65535 || dpiY < 50 || dpiY > 65535 || stageIndex < 1 || stageIndex > 6) return false;
            if (CachedDpiStages == null || CachedDpiStages.Length == 0)
            {
                CachedDpiStages = new int[] { 400, 800, 1200, 1600, 3200 };
            }
            if (CachedDpiStagesY == null || CachedDpiStagesY.Length != CachedDpiStages.Length)
                CachedDpiStagesY = (int[])CachedDpiStages.Clone();
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
            if (HardwareCommandBus.IsHardwareSandboxActive) return false;
            if (stagesX == null || stagesX.Length == 0) return false;
            if (stagesY == null || stagesY.Length < stagesX.Length) stagesY = stagesX;

            try
            {
                string expectedGroup = activeRapooGroup;
                int count = stagesX.Length;
                int idx = activeStage - 1;
                byte[] tableX = new byte[count * 2];
                byte[] tableY = new byte[count * 2];
                for (int i = 0; i < count; i++)
                {
                    tableX[i * 2] = (byte)(stagesX[i] & 0xFF);
                    tableX[i * 2 + 1] = (byte)(stagesX[i] >> 8);
                    tableY[i * 2] = (byte)(stagesY[i] & 0xFF);
                    tableY[i * 2 + 1] = (byte)(stagesY[i] >> 8);
                }
                // Each operation is verified. Partial failure is never reported as success;
                // the next hardware refresh recovers actual values instead of fake targets.
                if (!WriteRegisterForGroup(expectedGroup, BANKS_SYSTEM, ADDR_DPI_TABLE_A, tableX) ||
                    !WriteRegisterForGroup(expectedGroup, BANKS_SYSTEM, ADDR_DPI_TABLE_B, tableY) ||
                    !WriteRegisterForGroup(expectedGroup, BANKS_SYSTEM, ADDR_SLOT_COUNT, new byte[] { (byte)(count - 1) }) ||
                    !WriteRegisterForGroup(expectedGroup, BANKS_SYSTEM, ADDR_CURRENT_DPI, new byte[] { (byte)idx })) return false;
                CachedDpiStages = (int[])stagesX.Clone();
                CachedDpiStagesY = (int[])stagesY.Clone();
                CachedDpiStageCount = count;
                CachedDpiStage = activeStage;
                CachedDpi = stagesX[idx];
                CachedDpiY = stagesY[idx];
                CachedIsXyIndependent = CachedDpi != CachedDpiY;
                CachedXyRatio = (double)CachedDpiY / CachedDpi;
                SetEepromQuietPeriod(350);
                SaveHardwareCache();
                return true;
            }
            catch { return false; }
        }

        #endregion
    }
}
