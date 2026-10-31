// ============================================================================
// 模块名称: RazerDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   雷蛇 (Razer) 私有 HID 协议通信实现：
//   1. 扫描与识别雷蛇 2.4G 接收器与有线/无线鼠标 (VID 0x1532)
//   2. 构造与发送 90 字节私有报文读取硬件电池百分比与充电状态
//   3. 实时查询与固化写入板载 DPI 档位与 8000Hz 超高回报率 (全系支持 Focus Pro 35K Gen-2 与巴蛇 V3 专业版 35K)
// 依赖关系: NativeMethods.cs, MouseBatteryInfo.cs, RazerDeviceHardwareState.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、报文结构或新增 PID 时，【必须】同步更新上方说明。
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
    public static class RazerDeviceHelper
    {
        internal static readonly object hidLock = new object();
        private static readonly object stateCacheLock = new object();
        private static readonly Dictionary<string, RazerDeviceHardwareState> deviceHardwareStates =
            new Dictionary<string, RazerDeviceHardwareState>(StringComparer.OrdinalIgnoreCase);

        private static RazerDeviceHardwareState TryGetState(string normId)
        {
            if (string.IsNullOrEmpty(normId)) return null;
            lock (stateCacheLock)
            {
                RazerDeviceHardwareState st;
                if (deviceHardwareStates.TryGetValue(normId, out st))
                {
                    return st;
                }
                return null;
            }
        }

        private static RazerDeviceHardwareState GetOrCreateState(string normId)
        {
            if (string.IsNullOrEmpty(normId)) return null;
            lock (stateCacheLock)
            {
                RazerDeviceHardwareState st;
                if (!deviceHardwareStates.TryGetValue(normId, out st))
                {
                    st = new RazerDeviceHardwareState { DeviceId = normId };
                    deviceHardwareStates[normId] = st;
                }
                return st;
            }
        }

        public static RazerDeviceHardwareState GetDeviceState(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return null;
            string normId = NormalizeRazerDeviceId(deviceId);
            return TryGetState(normId);
        }

        public static int NormalizeRazerPid(int pid)
        {
            switch (pid)
            {
                case 0x00F0: return 0x00EF; // DeathAdder V4 Pro Carbon Fiber Dongle -> Wired
                case 0x00BF: return 0x00BE; // DeathAdder V4 Pro Dongle -> DeathAdder V4 Pro Wired
                case 0x00E6: return 0x00E5; // Viper V4 Pro Dongle -> Viper V4 Pro Wired
                case 0x00C1: return 0x00C0; // Viper V3 Pro Dongle -> Viper V3 Pro Wired
                case 0x00C2: return 0x00C0; // Viper V3 Pro Sentinels/SE Wired -> Viper V3 Pro
                case 0x00C3: return 0x00C0; // Viper V3 Pro Sentinels/SE Dongle -> Viper V3 Pro
                case 0x00DF: return 0x00DE; // Viper V3 Pro SE Dongle -> Viper V3 Pro SE Wired
                case 0x00AD: return 0x00AC; // Viper V3 HyperSpeed Dongle -> Wired
                case 0x00B7: return 0x00B6; // DeathAdder V3 Pro Dongle -> Wired
                case 0x00B3: return 0x00B2; // DeathAdder V3 HyperSpeed Dongle -> Wired
                case 0x00B5: return 0x00B4; // Viper V2 Pro Dongle -> Wired
                case 0x00A6: return 0x00A5; // Viper V2 Pro Dongle -> Wired
                case 0x009B: return 0x009A; // Basilisk V3 Pro Dongle -> Wired
                case 0x00AB: return 0x00AA; // Basilisk V3 Pro Alternate Dongle -> Wired
                case 0x00CD: return 0x00CC; // Basilisk V3 Pro 35K Dongle -> Wired
                case 0x00D7: return 0x00D6; // Basilisk V3 Pro 35K Phantom Green Dongle -> Wired
                case 0x00C5: return 0x00C4; // Basilisk V3 35K Dongle -> Wired
                case 0x00A8: return 0x00A7; // Viper Mini Signature Edition Dongle -> Wired
                case 0x0090: return 0x008F; // Naga V2 Pro Dongle -> Wired
                case 0x0092: return 0x0091; // Naga Pro Dongle -> Wired
                case 0x00A3: return 0x00A2; // DeathAdder V2 Pro Dongle -> Wired
                case 0x0088: return 0x0084; // Viper Ultimate Dongle -> Wired
                case 0x007B: return 0x007A; // Basilisk Ultimate Dongle -> Wired
                case 0x007D: return 0x007C; // DeathAdder V2 Mini Dongle -> Wired
                case 0xB007: return 0xB007; // DeathAdder V4 HyperSpeed
                case 0xB005: return 0xB005; // Viper V4 HyperSpeed
                default:
                    return pid;
            }
        }

        public static DeviceCategory GetRazerDeviceCategory(int pid, string productName)
        {
            string name = !string.IsNullOrEmpty(productName) ? productName.ToLowerInvariant() : "";

            // 1. Name keywords heuristic (highest priority for user clarity)
            if (name.Contains("keyboard") || 
                name.Contains("blackwidow") || 
                name.Contains("huntsman") || 
                name.Contains("cynosa") || 
                name.Contains("ornata") || 
                name.Contains("deathstalker") || 
                name.Contains("tartarus") || 
                name.Contains("orbweaver") || 
                name.Contains("anansi") || 
                name.Contains("nostromo"))
            {
                return DeviceCategory.Keyboard;
            }

            if (name.Contains("headset") || 
                name.Contains("kraken") || 
                name.Contains("blackshark") || 
                name.Contains("nari") || 
                name.Contains("hammerhead") || 
                name.Contains("barracuda") ||
                name.Contains("nommo") ||
                name.Contains("leviathan") ||
                name.Contains("seiren") ||
                name.Contains("speaker") ||
                name.Contains("soundbar") ||
                name.Contains("audio"))
            {
                return DeviceCategory.Headset;
            }

            // 2. Razer Macro PID Range Allocation Architecture
            // Keyboards & Keypads: 0x0100 .. 0x02FF
            if (pid >= 0x0100 && pid <= 0x02FF)
            {
                return DeviceCategory.Keyboard;
            }

            // Headsets & Audio: 0x0500 .. 0x05FF
            if (pid >= 0x0500 && pid <= 0x05FF)
            {
                return DeviceCategory.Headset;
            }

            // Mice: 0x0001 .. 0x00FE & 0xB000 .. 0xB0FF
            if ((pid >= 0x0001 && pid <= 0x00FE) || (pid >= 0xB000 && pid <= 0xB0FF))
            {
                return DeviceCategory.Mouse;
            }

            // Default fallback
            return DeviceCategory.Mouse;
        }

        public static bool IsRazerKeyboard(int pid, string productName)
        {
            return GetRazerDeviceCategory(pid, productName) == DeviceCategory.Keyboard;
        }

        public static MouseBatteryInfo CreateFallbackDeviceInfo(string normId, int curPid, string resolvedModelName, bool isDongle, RazerDeviceHardwareState stState)
        {
            DeviceCategory devCategory = GetRazerDeviceCategory(curPid, resolvedModelName);
            bool isNonBatteryDevice = (devCategory == DeviceCategory.Keyboard || devCategory == DeviceCategory.Headset);

            int cachedPct;
            double cachedExact;
            bool isSleeping;
            string transport;

            if (isNonBatteryDevice)
            {
                // 键盘、音箱等无电池设备：严格隔离电量，绝不继承鼠标的全局静态电量，显示免驱/供电中
                cachedPct = -1;
                cachedExact = 0.0;
                isSleeping = false; // 有线供电外设保持在线状态
                transport = "USB 有线";
            }
            else
            {
                // 鼠标等无线设备待机防抖决策
                cachedPct = (stState != null && stState.BatteryPercent > 0) ? stState.BatteryPercent : (CachedBatteryPercent > 0 ? CachedBatteryPercent : 50);
                cachedExact = (stState != null && stState.ExactBatteryPercent > 0) ? stState.ExactBatteryPercent : (CachedExactBatteryPercent > 0 ? CachedExactBatteryPercent : (double)cachedPct);
                
                bool isActuallySleeping = false;
                if (stState != null)
                {
                    bool failuresThreshold = stState.ConsecutiveFailures >= 3;
                    bool timeThreshold = (stState.LastSuccessfulCommunication == DateTime.MinValue) ||
                                         ((DateTime.Now - stState.LastSuccessfulCommunication).TotalSeconds > 45);
                    isActuallySleeping = failuresThreshold && timeThreshold;
                }
                else
                {
                    isActuallySleeping = true;
                }

                isSleeping = isActuallySleeping;
                transport = isDongle ? "2.4G 无线" : "USB 有线";
            }

            string dName = !string.IsNullOrEmpty(resolvedModelName) && !resolvedModelName.Equals("Razer Mouse", StringComparison.OrdinalIgnoreCase)
                ? resolvedModelName 
                : ((stState != null && !string.IsNullOrEmpty(stState.DeviceName)) ? stState.DeviceName : "雷蛇外设 (待机)");

            return new MouseBatteryInfo
            {
                DeviceId = normId,
                Brand = "Razer",
                Category = devCategory,
                IsConnected = true,
                IsSleeping = isSleeping,
                IsDonglePresent = isDongle,
                Transport = transport,
                HardwareId = "HID:1532:" + curPid.ToString("X4"),
                DeviceName = dName,
                BatteryPercent = cachedPct,
                ExactBatteryPercent = cachedExact,
                IsCharging = false,
                LastUpdated = (stState != null && stState.LastUpdated != DateTime.MinValue) ? stState.LastUpdated : DateTime.Now,
                Dpi = (stState != null && stState.Dpi > 0) ? stState.Dpi : 800,
                DpiY = (stState != null && stState.DpiY > 0) ? stState.DpiY : ((stState != null && stState.Dpi > 0) ? stState.Dpi : 800),
                DpiStage = (stState != null && stState.DpiStage > 0) ? stState.DpiStage : 1,
                DpiStageCount = (stState != null && stState.DpiStageCount > 0) ? stState.DpiStageCount : 5,
                DpiStages = (stState != null) ? stState.DpiStages : null,
                DpiStagesY = (stState != null) ? stState.DpiStagesY : null,
                IsXyIndependent = (stState != null) ? stState.IsXyIndependent : false,
                XyRatio = (stState != null && stState.XyRatio > 0) ? stState.XyRatio : 1.0,
                PollingRate = (stState != null && stState.PollingRate > 0) ? stState.PollingRate : 1000,
                MaxPollingRate = GetMaxPollingRateForModel(curPid, !string.IsNullOrEmpty(resolvedModelName) ? resolvedModelName : (stState != null ? stState.DeviceName : CachedDeviceName)),
                SupportedPollingRates = GetSupportedPollingRatesForModel(curPid, !string.IsNullOrEmpty(resolvedModelName) ? resolvedModelName : (stState != null ? stState.DeviceName : CachedDeviceName))
            };
        }

        public static byte[] GetAdaptiveTransactionIds(byte preferredTid)
        {
            if (preferredTid == 0x3F)
            {
                return new byte[] { 0x3F, 0x1F, 0xFF };
            }
            if (preferredTid == 0xFF)
            {
                return new byte[] { 0xFF, 0x1F, 0x3F };
            }
            return new byte[] { 0x1F, 0x3F, 0xFF };
        }

        /// <summary>
        /// 根据雷蛇鼠标硬件型号 PID 与产品名称识别支持的回报率档位
        /// 8K 超高刷电竞鼠标预设 125Hz ~ 8000Hz (含 125, 500 低档位)
        /// 传统 1000Hz 鼠标预设 125Hz, 500Hz, 1000Hz
        /// </summary>
        public static int[] GetSupportedPollingRatesForModel(int pid, string modelName)
        {
            if (pid == 0x00A5 || pid == 0x00A6) return new int[] { 125, 250, 500, 1000 };
            bool is8k = false;
            if (pid != 0)
            {
                switch (pid)
                {
                    case 0x0071: // Viper 8KHz
                    case 0x00B0: // DeathAdder V3 (Wired 8K)
                    case 0x00B6: case 0x00B7: // DeathAdder V3 Pro
                    case 0x00EF: case 0x00F0: // DeathAdder V4 Pro Carbon
                    case 0x00BE: case 0x00BF: // DeathAdder V4 Pro
                    case 0x00B2: case 0x00B3: // DeathAdder V3 HyperSpeed
                    case 0x00B4: case 0x00B5: // Viper V2 Pro
                    case 0x00C0: case 0x00C1: case 0x00C2: case 0x00C3: // Viper V3 Pro
                    case 0x00DE: case 0x00DF: // Viper V3 Pro SE
                    case 0x00E5: case 0x00E6: // Viper V4 Pro
                    case 0x00AC: case 0x00AD: // Viper V3 HyperSpeed
                    case 0x00A7: case 0x00A8: // Viper Mini Signature Edition
                    case 0x009A: case 0x009B: // Basilisk V3 Pro
                    case 0x00AA: case 0x00AB: // Basilisk V3 Pro Alternate
                    case 0x00CC: case 0x00CD: // Basilisk V3 Pro 35K
                    case 0x00D6: case 0x00D7: // Basilisk V3 Pro 35K Phantom Green
                    case 0x00C4: case 0x00C5: // Basilisk V3 35K
                    case 0x00A5: case 0x00A6: // Cobra Pro
                    case 0x008F: case 0x0090: // Naga V2 Pro
                    case 0x0095: // Naga V2 HyperSpeed
                    case 0x00B9: // HyperPolling Wireless Dongle
                    case 0x02CF: case 0x02D0: case 0x02D1: case 0x02D8:
                    case 0x02E4: case 0x02E5: case 0x02E6: case 0x02EA: // Huntsman 8KHz keyboards
                    case 0xB007: case 0xB005: // V4 HyperSpeed mice
                        is8k = true;
                        break;
                }
            }

            if (!is8k && !string.IsNullOrEmpty(modelName))
            {
                string name = modelName.ToLowerInvariant();
                if (name.Contains("8k") || name.Contains("8000") ||
                    name.Contains("v4 pro") || name.Contains("v3 pro") ||
                    name.Contains("signature edition") || name.Contains("35k") ||
                    name.Contains("hyperpolling"))
                {
                    is8k = true;
                }
            }

            if (is8k)
            {
                return new int[] { 125, 250, 500, 1000, 2000, 4000, 8000 };
            }
            else
            {
                return new int[] { 125, 250, 500, 1000 };
            }
        }

        /// <summary>
        /// 取该型号支持的最高回报率，用于 MouseBatteryInfo.MaxPollingRate（UI 档位兜底与提示文案）。
        /// </summary>
        public static int GetMaxPollingRateForModel(int pid, string modelName)
        {
            int[] rates = GetSupportedPollingRatesForModel(pid, modelName);
            int max = 1000;
            if (rates != null)
            {
                for (int i = 0; i < rates.Length; i++)
                {
                    if (rates[i] > max) max = rates[i];
                }
            }
            return max;
        }

        private static readonly Dictionary<int, string> RazerPidToOfficialName = new Dictionary<int, string>
        {
            // Viper series
            { 0x00E5, "Razer Viper V4 Pro" },
            { 0x00E6, "Razer Viper V4 Pro" },
            { 0x00C0, "Razer Viper V3 Pro" },
            { 0x00C1, "Razer Viper V3 Pro" },
            { 0x00C2, "Razer Viper V3 Pro" },
            { 0x00C3, "Razer Viper V3 Pro" },
            { 0x00DE, "Razer Viper V3 Pro SE" },
            { 0x00DF, "Razer Viper V3 Pro SE" },
            { 0x00AC, "Razer Viper V3 HyperSpeed" },
            { 0x00AD, "Razer Viper V3 HyperSpeed" },
            { 0x00B4, "Razer Viper V2 Pro" },
            { 0x00B5, "Razer Viper V2 Pro" },
            { 0x00A7, "Razer Viper Mini Signature Edition" },
            { 0x00A8, "Razer Viper Mini Signature Edition" },
            { 0x0084, "Razer Viper Ultimate" },
            { 0x0088, "Razer Viper Ultimate" },
            { 0x0078, "Razer Viper Mini" },
            { 0x0071, "Razer Viper 8KHz" },
            { 0x0070, "Razer Viper" },

            // Accessories & Dongles
            { 0x00B9, "Razer HyperPolling Wireless Dongle" },

            // DeathAdder series
            { 0x00EF, "Razer DeathAdder V4 Pro Carbon" },
            { 0x00F0, "Razer DeathAdder V4 Pro Carbon" },
            { 0x00BE, "Razer DeathAdder V4 Pro" },
            { 0x00BF, "Razer DeathAdder V4 Pro" },
            { 0x00B6, "Razer DeathAdder V3 Pro" },
            { 0x00B7, "Razer DeathAdder V3 Pro" },
            { 0x00B2, "Razer DeathAdder V3 HyperSpeed" },
            { 0x00B3, "Razer DeathAdder V3 HyperSpeed" },
            { 0x00B0, "Razer DeathAdder V3" },
            { 0x00A2, "Razer DeathAdder V2 Pro" },
            { 0x00A3, "Razer DeathAdder V2 Pro" },
            { 0x009C, "Razer DeathAdder V2 X HyperSpeed" },
            { 0x008A, "Razer DeathAdder V2" },
            { 0x007C, "Razer DeathAdder V2 Mini" },
            { 0x007D, "Razer DeathAdder V2 Mini" },
            { 0x005C, "Razer DeathAdder Elite" },

            // Basilisk series
            { 0x00CC, "Razer Basilisk V3 Pro 35K" },
            { 0x00CD, "Razer Basilisk V3 Pro 35K" },
            { 0x00D6, "Razer Basilisk V3 Pro 35K (Phantom Green)" },
            { 0x00D7, "Razer Basilisk V3 Pro 35K (Phantom Green)" },
            { 0x00C4, "Razer Basilisk V3 35K" },
            { 0x00C5, "Razer Basilisk V3 35K" },
            { 0x009A, "Razer Basilisk V3 Pro" },
            { 0x009B, "Razer Basilisk V3 Pro" },
            { 0x00AA, "Razer Basilisk V3 Pro" },
            { 0x00AB, "Razer Basilisk V3 Pro" },
            { 0x00B8, "Razer Basilisk V3" },
            { 0x0085, "Razer Basilisk X HyperSpeed" },
            { 0x0086, "Razer Basilisk X HyperSpeed" },
            { 0x007A, "Razer Basilisk Ultimate" },
            { 0x007B, "Razer Basilisk Ultimate" },
            { 0x0064, "Razer Basilisk" },

            // Viper V2 Pro and Cobra
            { 0x00A5, "Razer Viper V2 Pro" },
            { 0x00A6, "Razer Viper V2 Pro" },
            { 0x00A4, "Razer Cobra" },

            // Naga series
            { 0x008F, "Razer Naga V2 Pro" },
            { 0x0090, "Razer Naga V2 Pro" },
            { 0x0095, "Razer Naga V2 HyperSpeed" },
            { 0x0091, "Razer Naga Pro" },
            { 0x0092, "Razer Naga Pro" },
            { 0x0080, "Razer Naga Left-Handed Edition" },
            { 0x0067, "Razer Naga Trinity" },

            // Orochi series
            { 0x0099, "Razer Orochi V2" },

            // Web Synapse: HyperSpeed Mice (工程驱动与下一代极速版)
            { 0xB007, "Razer DeathAdder V4 HyperSpeed" },
            { 0xB005, "Razer Viper V4 HyperSpeed" },

            // Web Synapse: Huntsman Keyboards (猎魂光蛛系列键盘)
            { 0x0257, "Razer Huntsman Mini" },
            { 0x0269, "Razer Huntsman Mini Japan" },
            { 0x02B0, "Razer Huntsman V3 Pro Mini" },
            { 0x02CF, "Razer Huntsman V3 Pro 8KHz" },
            { 0x02D0, "Razer Huntsman V3 Pro Tenkeyless 8KHz" },
            { 0x02D1, "Razer Huntsman V3 Pro Mini 8KHz" },
            { 0x02D8, "Razer Huntsman Signature Edition" },
            { 0x02E4, "Razer Huntsman V3 HE Magnetic Mini 65% 8KHz" },
            { 0x02E5, "Razer Huntsman V3 Tenkeyless 8KHz" },
            { 0x02E6, "Razer Huntsman V3 Pro Low-profile Tenkeyless 8KHz" },
            { 0x02EA, "Razer Huntsman V3 HE Magnetic Tenkeyless 8KHz" },

            // Web Synapse: Keypad (单手小键盘)
            { 0x02EB, "Razer Tartarus V2 Pro" },

            // Web Synapse: Blade Laptops (灵刃笔记本内置系统键盘/控制器)
            { 0x02E0, "Razer Blade 16 (2026)" },
            { 0x02E1, "Razer Blade 18 (2026)" }
        };

        public static string QueryPnpDeviceDesc(int pid)
        {
            if (pid == 0) return null;
            try
            {
                string pidHex = pid.ToString("X4");
                string[] enumRoots = new string[] { @"SYSTEM\CurrentControlSet\Enum\USB", @"SYSTEM\CurrentControlSet\Enum\HID" };
                foreach (var rootPath in enumRoots)
                {
                    using (var rootKey = Registry.LocalMachine.OpenSubKey(rootPath))
                    {
                        if (rootKey == null) continue;
                        foreach (var subName in rootKey.GetSubKeyNames())
                        {
                            if (subName.IndexOf("VID_1532", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                subName.IndexOf("PID_" + pidHex, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                using (var devKey = rootKey.OpenSubKey(subName))
                                {
                                    if (devKey == null) continue;
                                    foreach (var instName in devKey.GetSubKeyNames())
                                    {
                                        using (var instKey = devKey.OpenSubKey(instName))
                                        {
                                            if (instKey == null) continue;

                                            string[] valueNames = new string[] { "DeviceDesc", "FriendlyName", "BusReportedDeviceDesc" };
                                            foreach (var valName in valueNames)
                                            {
                                                string desc = instKey.GetValue(valName) as string;
                                                if (string.IsNullOrEmpty(desc)) continue;

                                                int semi = desc.LastIndexOf(';');
                                                if (semi >= 0 && semi < desc.Length - 1)
                                                {
                                                    desc = desc.Substring(semi + 1).Trim();
                                                }

                                                if (desc.StartsWith("Razer ", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    if (desc.IndexOf("Composite", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                        desc.IndexOf("Input Device", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                        desc.IndexOf("HID-compliant", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                        desc.IndexOf("Control Device", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                        desc.IndexOf("Keyboard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                        desc.Equals("Razer Mouse", StringComparison.OrdinalIgnoreCase) ||
                                                        desc.Equals("Razer Wireless Mouse", StringComparison.OrdinalIgnoreCase))
                                                    {
                                                        continue;
                                                    }

                                                    if (desc.IndexOf("Dongle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                        desc.IndexOf("Receiver", StringComparison.OrdinalIgnoreCase) >= 0)
                                                    {
                                                        string stripped = desc.Replace("Dongle", "").Replace("Receiver", "").Trim();
                                                        if (stripped.StartsWith("Razer ", StringComparison.OrdinalIgnoreCase) &&
                                                            !stripped.Equals("Razer", StringComparison.OrdinalIgnoreCase) &&
                                                            !stripped.Equals("Razer HyperPolling Wireless", StringComparison.OrdinalIgnoreCase) &&
                                                            !stripped.Equals("Razer USB", StringComparison.OrdinalIgnoreCase))
                                                        {
                                                            return stripped;
                                                        }
                                                        continue;
                                                    }

                                                    return desc;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        public static string GetRazerOfficialModelName(int pid, string fallbackProdName = null)
        {
            // 特殊识别：炼狱蝰蛇 V4 Pro 碳纤维版 (DeathAdder V4 Pro Carbon)
            bool isCarbon = (fallbackProdName != null &&
                (fallbackProdName.IndexOf("Carbon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                 fallbackProdName.IndexOf("碳纤维", StringComparison.OrdinalIgnoreCase) >= 0));

            if (pid != 0)
            {
                // 官方专属 PID 直接命中 (有线 0x00EF / 无线 0x00F0)
                if (pid == 0x00EF || pid == 0x00F0 || NormalizeRazerPid(pid) == 0x00EF)
                {
                    return "Razer DeathAdder V4 Pro Carbon";
                }

                string pnpName = QueryPnpDeviceDesc(pid);
                if (!string.IsNullOrEmpty(pnpName))
                {
                    if (pnpName.IndexOf("Carbon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                        pnpName.IndexOf("碳纤维", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return "Razer DeathAdder V4 Pro Carbon";
                    }
                    if (isCarbon && (pid == 0x00BE || pid == 0x00BF || NormalizeRazerPid(pid) == 0x00BE))
                    {
                        return "Razer DeathAdder V4 Pro Carbon";
                    }
                    return pnpName;
                }

                int normPid = NormalizeRazerPid(pid);
                if (normPid != pid && normPid != 0)
                {
                    pnpName = QueryPnpDeviceDesc(normPid);
                    if (!string.IsNullOrEmpty(pnpName))
                    {
                        if (pnpName.IndexOf("Carbon", StringComparison.OrdinalIgnoreCase) >= 0 ||
                            pnpName.IndexOf("碳纤维", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            return "Razer DeathAdder V4 Pro Carbon";
                        }
                        if (isCarbon && (normPid == 0x00BE))
                        {
                            return "Razer DeathAdder V4 Pro Carbon";
                        }
                        return pnpName;
                    }
                }

                if (isCarbon && (pid == 0x00BE || pid == 0x00BF || normPid == 0x00BE))
                {
                    return "Razer DeathAdder V4 Pro Carbon";
                }

                string official;
                if (RazerPidToOfficialName.TryGetValue(pid, out official)) return official;
                if (normPid != pid && normPid != 0 && RazerPidToOfficialName.TryGetValue(normPid, out official)) return official;
            }

            if (!string.IsNullOrEmpty(fallbackProdName))
            {
                if (isCarbon && fallbackProdName.IndexOf("DeathAdder", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return "Razer DeathAdder V4 Pro Carbon";
                }

                string clean = fallbackProdName.Trim();
                if (clean.StartsWith("Razer ", StringComparison.OrdinalIgnoreCase)) clean = clean.Substring(6).Trim();

                if (!clean.Equals("Mouse", StringComparison.OrdinalIgnoreCase) &&
                    !clean.Equals("Wireless Mouse", StringComparison.OrdinalIgnoreCase) &&
                    !clean.Equals("HyperPolling Wireless Dongle", StringComparison.OrdinalIgnoreCase) &&
                    !clean.Equals("USB Receiver", StringComparison.OrdinalIgnoreCase) &&
                    !clean.Equals("HID-compliant mouse", StringComparison.OrdinalIgnoreCase) &&
                    clean.Length > 2)
                {
                    return "Razer " + clean;
                }
            }

            string combined = fallbackProdName ?? "";
            if (combined.IndexOf("DeathAdder", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (isCarbon) return "Razer DeathAdder V4 Pro Carbon";
                return "Razer DeathAdder";
            }
            if (combined.IndexOf("Viper", StringComparison.OrdinalIgnoreCase) >= 0) return "Razer Viper";
            if (combined.IndexOf("Basilisk", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (combined.IndexOf("35K", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return combined.IndexOf("Pro", StringComparison.OrdinalIgnoreCase) >= 0 ? "Razer Basilisk V3 Pro 35K" : "Razer Basilisk V3 35K";
                }
                if (combined.IndexOf("Pro", StringComparison.OrdinalIgnoreCase) >= 0) return "Razer Basilisk V3 Pro";
                if (combined.IndexOf("Ultimate", StringComparison.OrdinalIgnoreCase) >= 0) return "Razer Basilisk Ultimate";
                if (combined.IndexOf("HyperSpeed", StringComparison.OrdinalIgnoreCase) >= 0) return "Razer Basilisk X HyperSpeed";
                return "Razer Basilisk";
            }
            if (combined.IndexOf("Cobra", StringComparison.OrdinalIgnoreCase) >= 0) return "Razer Cobra";
            if (combined.IndexOf("Naga", StringComparison.OrdinalIgnoreCase) >= 0) return "Razer Naga";
            if (combined.IndexOf("Orochi", StringComparison.OrdinalIgnoreCase) >= 0) return "Razer Orochi";

            return "Razer Gaming Mouse";
        }

        public static string GetRazerDisplayName(int pid, string deviceName = null)
        {
            string target = null;
            if (pid != 0)
            {
                string official = GetRazerOfficialModelName(pid, deviceName);
                if (!string.IsNullOrEmpty(official) && !official.Equals("Razer Gaming Mouse", StringComparison.OrdinalIgnoreCase))
                {
                    target = official;
                }
            }

            if (string.IsNullOrEmpty(target) && !string.IsNullOrEmpty(deviceName))
            {
                string clean = deviceName.Trim();
                if (!clean.Equals("Razer Mouse", StringComparison.OrdinalIgnoreCase) &&
                    !clean.Equals("Razer Wireless Mouse", StringComparison.OrdinalIgnoreCase) &&
                    !clean.Equals("雷蛇无线鼠标", StringComparison.OrdinalIgnoreCase) &&
                    !clean.Equals("雷蛇电竞鼠标", StringComparison.OrdinalIgnoreCase) &&
                    !clean.Equals("雷蛇无线设备", StringComparison.OrdinalIgnoreCase))
                {
                    if (clean.StartsWith("Razer ", StringComparison.OrdinalIgnoreCase)) target = clean;
                    else target = "Razer " + clean;
                }
            }

            return !string.IsNullOrEmpty(target) ? target : "Razer Gaming Mouse";
        }

        public static string NormalizeRazerDeviceId(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return deviceId;
            if (deviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase))
            {
                string pidPart = deviceId.Substring("Razer:1532:".Length).Trim();
                int pid;
                if (int.TryParse(pidPart, System.Globalization.NumberStyles.HexNumber, null, out pid))
                {
                    int normPid = NormalizeRazerPid(pid);
                    return "Razer:1532:" + normPid.ToString("X4");
                }
            }
            return deviceId;
        }

        public static bool DetermineIsDongle(int curPid, string prodName, int batteryPercent, bool isCharging)
        {
            // 物理铁律：在 USB 总线通信中，若外设具备电池电量且当前未处于充电状态，
            // 则该 USB 端点 100% 为无线接收器（Dongle），正通过 2.4G RF 遥测远端电池
            if (batteryPercent > 0 && !isCharging)
            {
                return true;
            }

            int normPid = NormalizeRazerPid(curPid);
            if (normPid != curPid) return true;

            // 纯无线/干电池无线设备（无有线模式）
            if (curPid == 0x0099 || // Orochi V2
                curPid == 0x0085 || curPid == 0x0086 || // Basilisk X HyperSpeed
                curPid == 0x00AC || // Viper V3 HyperSpeed
                curPid == 0x009C || // DeathAdder V2 X HyperSpeed (AA 干电池，纯无线)
                curPid == 0x0095 || // Naga V2 HyperSpeed
                curPid == 0x008D || curPid == 0x008E) // Atheris
            {
                return true;
            }

            // 描述字符串包含接收器/底座/无线关键字
            if (!string.IsNullOrEmpty(prodName))
            {
                if (prodName.IndexOf("Dongle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    prodName.IndexOf("Receiver", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    prodName.IndexOf("Dock", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    prodName.IndexOf("Wireless", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    prodName.IndexOf("HyperSpeed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    prodName.IndexOf("HyperPolling", StringComparison.OrdinalIgnoreCase) >= 0)
                {
                    return true;
                }
            }

            // 接收器 PID 集合
            if (curPid == 0x00F0 || curPid == 0x00BF || curPid == 0x00B7 || curPid == 0x00B5 || curPid == 0x00A6 ||
                curPid == 0x007D || curPid == 0x009B || curPid == 0x00AD || curPid == 0x00A8 ||
                curPid == 0x00C5 || curPid == 0x00C1 || curPid == 0x00C3 || curPid == 0x0088 ||
                curPid == 0x0072 || curPid == 0x0090 || curPid == 0x00A3 || curPid == 0x00B3 ||
                curPid == 0x0008 || curPid == 0x007B || curPid == 0x0092 || curPid == 0x00B9 ||
                curPid == 0x00E6 || curPid == 0x00DF || curPid == 0x00CD || curPid == 0x00D7 || curPid == 0x00AB)
            {
                return true;
            }

            return false;
        }

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
            catch { }
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
            catch { }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [DllImport("hid.dll", SetLastError = true)]
        public static extern void HidD_GetHidGuid(out Guid HidGuid);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, string Enumerator, IntPtr hwndParent, uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, uint MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, uint DeviceInterfaceDetailDataSize, out uint RequiredSize, IntPtr DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetAttributes(IntPtr HidDeviceObject, ref HIDD_ATTRIBUTES Attributes);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetPreparsedData(IntPtr HidDeviceObject, out IntPtr PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_FreePreparsedData(IntPtr PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern int HidP_GetCaps(IntPtr PreparsedData, out HIDP_CAPS Capabilities);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_SetFeature(IntPtr HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetFeature(IntPtr HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetInputReport(IntPtr HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_SetOutputReport(IntPtr HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

        [DllImport("hid.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool HidD_GetProductString(IntPtr HidDeviceObject, StringBuilder Buffer, int BufferLength);

        public const uint DIGCF_PRESENT = 0x02;
        public const uint DIGCF_DEVICEINTERFACE = 0x10;
        public const uint FILE_SHARE_READ = 0x00000001;
        public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint OPEN_EXISTING = 3;

        static byte CalculateCrc(byte[] report, int startOffset)
        {
            byte crc = 0;
            for (int i = startOffset + 2; i < startOffset + 88; i++)
            {
                crc ^= report[i];
            }
            return crc;
        }

        internal static byte[] CreateRazerReport(byte transactionId, byte commandClass, byte commandId, byte dataSize, int totalLength, bool prependedZero, byte[] args = null)
        {
            byte[] report = new byte[totalLength];
            int offset = prependedZero ? 1 : 0;

            report[offset + 0] = 0x00;
            report[offset + 1] = transactionId;
            report[offset + 2] = 0x00;
            report[offset + 3] = 0x00;
            report[offset + 4] = 0x00;
            report[offset + 5] = dataSize;
            report[offset + 6] = commandClass;
            report[offset + 7] = commandId;

            if (args != null)
            {
                for (int i = 0; i < args.Length && i < 80; i++)
                {
                    report[offset + 8 + i] = args[i];
                }
            }

            report[offset + 88] = CalculateCrc(report, offset);
            report[offset + 89] = 0x00;

            return report;
        }

        public static RazerDeviceHardwareState ParseDpiStagesReport(byte[] report, int offset)
        {
            if (report == null || offset < 0 || report.Length < offset + 11 ||
                report[offset] != 2 || report[offset + 6] != 4 || report[offset + 7] != 0x86) return null;
            int activeStage = report[offset + 9];
            int count = report[offset + 10];
            if (count < 1 || count > 5 || activeStage < 1 || activeStage > count || report.Length < offset + 11 + count * 7) return null;
            RazerDeviceHardwareState state = new RazerDeviceHardwareState();
            state.DpiStages = new int[count];
            state.DpiStagesY = new int[count];
            state.DpiStage = activeStage;
            state.DpiStageCount = count;
            for (int stageIndex = 0; stageIndex < count; stageIndex++)
            {
                int stageOffset = offset + 11 + stageIndex * 7;
                int dpiX = (report[stageOffset + 1] << 8) | report[stageOffset + 2];
                int dpiY = (report[stageOffset + 3] << 8) | report[stageOffset + 4];
                if (report[stageOffset] != stageIndex + 1 || dpiX < 100 || dpiX > 50000 || dpiY < 100 || dpiY > 50000) return null;
                state.DpiStages[stageIndex] = dpiX;
                state.DpiStagesY[stageIndex] = dpiY;
            }
            state.Dpi = state.DpiStages[activeStage - 1];
            state.DpiY = state.DpiStagesY[activeStage - 1];
            state.IsXyIndependent = state.Dpi != state.DpiY;
            state.XyRatio = (double)state.DpiY / state.Dpi;
            return state;
        }

        public static List<MouseBatteryInfo> QueryAllRazerDevices()
        {
            lock (hidLock)
            {
                var detectedByNormId = new Dictionary<string, MouseBatteryInfo>(StringComparer.OrdinalIgnoreCase);

                Guid hidGuid;
                HidD_GetHidGuid(out hidGuid);

                IntPtr devInfo = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1))
                {
                    return new List<MouseBatteryInfo>();
                }

                SP_DEVICE_INTERFACE_DATA ifData = new SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(ifData);

                uint memberIdx = 0;
                try
                {
                    while (SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                    {
                        uint reqSize;
                        SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);

                        IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                        if (SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                        {
                            IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                            string devicePath = Marshal.PtrToStringAuto(pDevicePath);

                            if (devicePath != null && devicePath.ToLower().Contains("vid_1532"))
                            {
                                IntPtr handle = CreateFile(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                                if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                {
                                    try
                                    {
                                        IntPtr preparsed;
                                        if (HidD_GetPreparsedData(handle, out preparsed))
                                        {
                                            HIDP_CAPS caps;
                                            HidP_GetCaps(preparsed, out caps);
                                            HidD_FreePreparsedData(preparsed);

                                            if (caps.FeatureReportByteLength >= 90)
                                            {
                                                StringBuilder prod = new StringBuilder(256);
                                                string prodName = "Razer Mouse";
                                                if (HidD_GetProductString(handle, prod, prod.Capacity) && prod.Length > 0)
                                                {
                                                    prodName = prod.ToString();
                                                }

                                                int curPid = 0;
                                                HIDD_ATTRIBUTES attr = new HIDD_ATTRIBUTES();
                                                attr.Size = Marshal.SizeOf(attr);
                                                if (HidD_GetAttributes(handle, ref attr))
                                                {
                                                    curPid = attr.ProductID;
                                                }
                                                else if (devicePath != null)
                                                {
                                                    int pidIdx = devicePath.IndexOf("pid_", StringComparison.OrdinalIgnoreCase);
                                                    if (pidIdx >= 0 && devicePath.Length >= pidIdx + 8)
                                                    {
                                                        int.TryParse(devicePath.Substring(pidIdx + 4, 4), System.Globalization.NumberStyles.HexNumber, null, out curPid);
                                                    }
                                                }

                                                int normPid = NormalizeRazerPid(curPid);
                                                bool isDongle = DetermineIsDongle(curPid, prodName, 0, false);
                                                string normId = "Razer:1532:" + (normPid != 0 ? normPid.ToString("X4") : (!string.IsNullOrEmpty(prodName) ? prodName : "Mouse"));
                                                string resolvedModelName = GetRazerOfficialModelName(curPid, prodName);

                                                byte[] transIds = new byte[] { 0x1F, 0x3F, 0xFF };
                                                bool prepended = (caps.FeatureReportByteLength == 91);
                                                int offset = prepended ? 1 : 0;
                                                bool querySuccess = false;

                                                int pct = 0;
                                                double exactPct = 0.0;
                                                bool isCharging = false;
                                                int liveDpi = 0;
                                                int liveDpiY = 0;
                                                int activeStage = 0;
                                                int stageCount = 0;
                                                int[] stageList = null;
                                                int[] stageListY = null;
                                                int pollingRate = 0;
                                                byte successfulTid = 0x1F;

                                                foreach (byte tid in transIds)
                                                {
                                                    // 1. Query Battery
                                                    byte[] req = CreateRazerReport(tid, 0x07, 0x80, 0x02, caps.FeatureReportByteLength, prepended);
                                                    if (HidD_SetFeature(handle, req, req.Length))
                                                    {
                                                        Thread.Sleep(15);
                                                        byte[] resp = new byte[caps.FeatureReportByteLength];
                                                        if (prepended) resp[0] = 0x00;

                                                        if (HidD_GetFeature(handle, resp, resp.Length))
                                                        {
                                                            byte status = resp[offset + 0];
                                                            byte cmdClass = resp[offset + 6];
                                                            byte cmdId = resp[offset + 7];
                                                            byte rawBatt = resp[offset + 9];

                                                            if (status == 0x02 && cmdClass == 0x07 && cmdId == 0x80 && rawBatt > 0)
                                                            {
                                                                querySuccess = true;
                                                                pct = (int)Math.Round((rawBatt / 255.0) * 100);
                                                                pct = Math.Max(1, Math.Min(100, pct));
                                                                exactPct = Math.Round((rawBatt / 255.0) * 100.0, 2);
                                                                exactPct = Math.Max(1.0, Math.Min(100.0, exactPct));

                                                                 // 2. Query Charging
                                                                byte[] chgReq = CreateRazerReport(tid, 0x07, 0x84, 0x02, caps.FeatureReportByteLength, prepended);
                                                                if (HidD_SetFeature(handle, chgReq, chgReq.Length))
                                                                {
                                                                    Thread.Sleep(15);
                                                                    byte[] chgResp = new byte[caps.FeatureReportByteLength];
                                                                    if (prepended) chgResp[0] = 0x00;
                                                                    if (HidD_GetFeature(handle, chgResp, chgResp.Length) && chgResp[offset + 0] == 0x02 && chgResp[offset + 6] == 0x07 && chgResp[offset + 7] == 0x84)
                                                                    {
                                                                        isCharging = (chgResp[offset + 9] == 1);
                                                                    }
                                                                }

                                                                // 3. Query DPI Stages & Active DPI (带 3 次退避与 BUSY 0x01 过滤)
                                                                byte[] stagesReq = CreateRazerReport(tid, 0x04, 0x86, 0x26, caps.FeatureReportByteLength, prepended, new byte[] { 0x01 });
                                                                if (HidD_SetFeature(handle, stagesReq, stagesReq.Length))
                                                                {
                                                                    byte[] stResp = new byte[caps.FeatureReportByteLength];
                                                                    if (prepended) stResp[0] = 0x00;
                                                                    for (int attempt = 0; attempt < 3; attempt++)
                                                                    {
                                                                        Thread.Sleep(15);
                                                                        if (HidD_GetFeature(handle, stResp, stResp.Length))
                                                                        {
                                                                            byte stStatus = stResp[offset + 0];
                                                                            byte stClass = stResp[offset + 6];
                                                                            byte stId = stResp[offset + 7];
                                                                            if (stStatus == 0x01) continue; // BUSY
                                                                            if (stStatus == 0x02 && stClass == 0x04 && stId == 0x86)
                                                                            {
                                                                                RazerDeviceHardwareState parsedStages = ParseDpiStagesReport(stResp, offset);
                                                                                if (parsedStages != null)
                                                                                {
                                                                                    activeStage = parsedStages.DpiStage;
                                                                                    stageCount = parsedStages.DpiStageCount;
                                                                                    stageList = parsedStages.DpiStages;
                                                                                    stageListY = parsedStages.DpiStagesY;
                                                                                    liveDpi = parsedStages.Dpi;
                                                                                    liveDpiY = parsedStages.DpiY;
                                                                                    break;
                                                                                }
                                                                            }
                                                                        }
                                                                    }
                                                                }

                                                                if (liveDpi <= 0)
                                                                {
                                                                    byte[] dpiReq = CreateRazerReport(tid, 0x04, 0x85, 0x07, caps.FeatureReportByteLength, prepended, new byte[] { 0x00 });
                                                                    if (HidD_SetFeature(handle, dpiReq, dpiReq.Length))
                                                                    {
                                                                        byte[] dpiResp = new byte[caps.FeatureReportByteLength];
                                                                        if (prepended) dpiResp[0] = 0x00;
                                                                        for (int attempt = 0; attempt < 3; attempt++)
                                                                        {
                                                                            Thread.Sleep(15);
                                                                            if (HidD_GetFeature(handle, dpiResp, dpiResp.Length))
                                                                            {
                                                                                byte dStatus = dpiResp[offset + 0];
                                                                                byte dClass = dpiResp[offset + 6];
                                                                                byte dId = dpiResp[offset + 7];
                                                                                if (dStatus == 0x01) continue;
                                                                                if (dStatus == 0x02 && dClass == 0x04 && dId == 0x85)
                                                                                {
                                                                                    int readLiveDpi = (dpiResp[offset + 9] << 8) | dpiResp[offset + 10];
                                                                                    if (readLiveDpi >= 100 && readLiveDpi <= 50000)
                                                                                    {
                                                                                        liveDpi = readLiveDpi;
                                                                                        int readLiveDpiY = (dpiResp[offset + 11] << 8) | dpiResp[offset + 12];
                                                                                        liveDpiY = readLiveDpiY >= 100 && readLiveDpiY <= 50000 ? readLiveDpiY : readLiveDpi;
                                                                                        break;
                                                                                    }
                                                                                }
                                                                            }
                                                                        }
                                                                    }
                                                                }

                                                                // 4. Query Polling Rate
                                                                byte[] pollReq = CreateRazerReport(tid, 0x00, 0xC0, 0x01, caps.FeatureReportByteLength, prepended);
                                                                if (HidD_SetFeature(handle, pollReq, pollReq.Length))
                                                                {
                                                                    Thread.Sleep(15);
                                                                    byte[] pollResp = new byte[caps.FeatureReportByteLength];
                                                                    if (prepended) pollResp[0] = 0x00;
                                                                    if (HidD_GetFeature(handle, pollResp, pollResp.Length) && pollResp[offset + 0] == 0x02 && pollResp[offset + 6] == 0x00 && pollResp[offset + 7] == 0xC0)
                                                                    {
                                                                        byte rawPoll = pollResp[offset + 9];
                                                                        switch (rawPoll)
                                                                        {
                                                                            case 0x01: pollingRate = 8000; break;
                                                                            case 0x02: pollingRate = 4000; break;
                                                                            case 0x04: pollingRate = 2000; break;
                                                                            case 0x08: pollingRate = 1000; break;
                                                                            case 0x10: pollingRate = 500; break;
                                                                            case 0x20: pollingRate = 250; break;
                                                                            case 0x40: pollingRate = 125; break;
                                                                        }
                                                                    }
                                                                }

                                                                break; // Succeeded with this tid
                                                            }
                                                        }
                                                    }
                                                }

                                                RazerDeviceHardwareState stState = GetOrCreateState(normId);

                                                if (querySuccess)
                                                {
                                                    isDongle = DetermineIsDongle(curPid, prodName, pct, isCharging);

                                                    // Device responded actively!
                                                    stState.DevicePath = devicePath;
                                                    stState.ReportLength = caps.FeatureReportByteLength;
                                                    stState.IsPrepended = prepended;
                                                    stState.BatteryPercent = pct;
                                                    stState.ExactBatteryPercent = exactPct;
                                                    stState.DeviceName = resolvedModelName;

                                                    // 物理状态防破坏锁：当快速连续点击刷新或遇接收器忙时，若单次未读出档位，平滑继承已知有效档位，严禁清零！
                                                    if (activeStage <= 0 && stState.DpiStage > 0)
                                                    {
                                                        activeStage = stState.DpiStage;
                                                        stageCount = stState.DpiStageCount;
                                                        stageList = stState.DpiStages;
                                                        stageListY = stState.DpiStagesY;
                                                    }
                                                    if (liveDpi <= 0 && stState.Dpi > 0)
                                                    {
                                                        liveDpi = stState.Dpi;
                                                        liveDpiY = stState.DpiY;
                                                    }

                                                    stState.Dpi = liveDpi;
                                                    stState.DpiY = liveDpiY;
                                                    stState.IsXyIndependent = liveDpiY > 0 && liveDpi != liveDpiY;
                                                    stState.XyRatio = liveDpi > 0 ? (double)liveDpiY / liveDpi : 1.0;
                                                    stState.DpiStage = activeStage;
                                                    stState.DpiStageCount = stageCount;
                                                    stState.DpiStages = stageList;
                                                    stState.DpiStagesY = stageListY;
                                                    stState.PollingRate = pollingRate;
                                                    stState.ActiveTransactionId = successfulTid;
                                                    stState.LastUpdated = DateTime.Now;
                                                    stState.ConsecutiveFailures = 0;
                                                    stState.LastSuccessfulCommunication = DateTime.Now;

                                                    var activeDev = new MouseBatteryInfo
                                                    {
                                                        DeviceId = normId,
                                                        Brand = "Razer",
                                                        Category = GetRazerDeviceCategory(curPid, resolvedModelName),
                                                        IsConnected = true,
                                                        IsSleeping = false,
                                                        IsDonglePresent = isDongle,
                                                        Transport = isDongle ? "2.4G 无线" : "USB 有线",
                                                        HardwareId = "HID:1532:" + curPid.ToString("X4"),
                                                        DeviceName = resolvedModelName,
                                                        BatteryPercent = pct,
                                                        ExactBatteryPercent = exactPct,
                                                        IsCharging = isCharging,
                                                        LastUpdated = DateTime.Now,
                                                        Dpi = liveDpi,
                                                        DpiY = liveDpiY,
                                                        IsXyIndependent = stState.IsXyIndependent,
                                                        XyRatio = stState.XyRatio,
                                                        DpiStage = activeStage,
                                                        DpiStageCount = stageCount,
                                                        DpiStages = stageList,
                                                        DpiStagesY = stageListY,
                                                        PollingRate = pollingRate,
                                                        MaxPollingRate = GetMaxPollingRateForModel(curPid, resolvedModelName),
                                                        SupportedPollingRates = GetSupportedPollingRatesForModel(curPid, resolvedModelName)
                                                    };

                                                    // If already present: Wired connection takes precedence over Dongle!
                                                    if (detectedByNormId.ContainsKey(normId))
                                                    {
                                                        var existing = detectedByNormId[normId];
                                                        if (existing.IsSleeping || (!isDongle && existing.IsDonglePresent))
                                                        {
                                                            detectedByNormId[normId] = activeDev;
                                                        }
                                                    }
                                                    else
                                                    {
                                                        detectedByNormId[normId] = activeDev;
                                                    }
                                                }
                                                else
                                                {
                                                    stState.ConsecutiveFailures++;

                                                    // Device didn't respond (e.g. asleep or off).
                                                    // Save handle path so wake attempts or FastQueryDpi can reach it.
                                                    if (string.IsNullOrEmpty(stState.DevicePath) || !isDongle)
                                                    {
                                                        stState.DevicePath = devicePath;
                                                        stState.ReportLength = caps.FeatureReportByteLength;
                                                        stState.IsPrepended = prepended;
                                                    }

                                                    if (!detectedByNormId.ContainsKey(normId))
                                                    {
                                                        detectedByNormId[normId] = CreateFallbackDeviceInfo(normId, curPid, resolvedModelName, isDongle, stState);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        CloseHandle(handle);
                                    }
                                }
                            }
                        }

                        Marshal.FreeHGlobal(detailBuffer);
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(devInfo);
                }

                // Update primary/legacy cached fields from the best active device
                if (detectedByNormId.Count > 0)
                {
                    MouseBatteryInfo primary = null;
                    foreach (var dev in detectedByNormId.Values)
                    {
                        if (dev.Category == DeviceCategory.Mouse && !dev.IsSleeping && dev.BatteryPercent > 0) { primary = dev; break; }
                    }
                    if (primary == null)
                    {
                        foreach (var dev in detectedByNormId.Values)
                        {
                            if (dev.Category == DeviceCategory.Mouse) { primary = dev; break; }
                        }
                    }
                    if (primary == null)
                    {
                        foreach (var dev in detectedByNormId.Values)
                        {
                            if (!dev.IsSleeping) { primary = dev; break; }
                        }
                    }
                    if (primary == null)
                    {
                        var enumerator = detectedByNormId.Values.GetEnumerator();
                        if (enumerator.MoveNext()) primary = enumerator.Current;
                    }

                    if (primary != null && primary.BatteryPercent > 0)
                    {
                        CachedBatteryPercent = primary.BatteryPercent;
                        CachedExactBatteryPercent = primary.ExactBatteryPercent;
                        CachedDeviceName = primary.DeviceName;
                        if (primary.Dpi > 0) CachedDpi = primary.Dpi;
                        if (primary.DpiY > 0) CachedDpiY = primary.DpiY;
                        CachedIsXyIndependent = primary.IsXyIndependent;
                        CachedXyRatio = primary.XyRatio;
                        if (primary.DpiStage > 0) CachedDpiStage = primary.DpiStage;
                        if (primary.DpiStageCount > 0) CachedDpiStageCount = primary.DpiStageCount;
                        if (primary.DpiStages != null) CachedDpiStages = primary.DpiStages;
                        if (primary.DpiStagesY != null) CachedDpiStagesY = primary.DpiStagesY;
                        if (primary.PollingRate > 0) CachedPollingRate = primary.PollingRate;
                        CachedLastUpdated = primary.LastUpdated;

                        RazerDeviceHardwareState pState = TryGetState(primary.DeviceId);
                        if (pState != null)
                        {
                            cachedRazerDpiPath = pState.DevicePath;
                            cachedRazerReportLength = pState.ReportLength;
                            cachedRazerPrepended = pState.IsPrepended;
                        }
                    }
                    SaveHardwareCache();
                }

                return new List<MouseBatteryInfo>(detectedByNormId.Values);
            }
        }

        public static MouseBatteryInfo QueryRazerDeviceInfo()
        {
            var list = QueryAllRazerDevices();
            if (list != null && list.Count > 0)
            {
                var active = list.Find(x => !x.IsSleeping);
                return active ?? list[0];
            }
            return new MouseBatteryInfo { IsConnected = false, IsSleeping = false, IsDonglePresent = false };
        }

        public static bool FastQueryDpi(out int curDpi, out int curDpiY, out int activeStage, out int stageCount, string targetDeviceId = null)
        {
            bool res = FastQueryDpi(out curDpi, out activeStage, out stageCount, targetDeviceId);
            curDpiY = (CachedDpiY > 0) ? CachedDpiY : curDpi;
            return res;
        }

        public static bool FastQueryDpi(out int curDpi, out int activeStage, out int stageCount, string targetDeviceId = null)
        {
            curDpi = 0;
            activeStage = 0;
            stageCount = 0;

            string normTargetId = NormalizeRazerDeviceId(targetDeviceId);

            lock (hidLock)
            {
                var st = TryGetState(normTargetId);
                if (st != null && !string.IsNullOrEmpty(st.DevicePath))
                {
                    if (TryQueryRazerDpiByPath(st.DevicePath, st.ReportLength, st.IsPrepended, out curDpi, out activeStage, out stageCount))
                    {
                        st.Dpi = curDpi;
                        st.DpiStage = activeStage;
                        st.DpiStageCount = stageCount;
                        st.LastUpdated = DateTime.Now;
                        return true;
                    }
                }
                else if (!string.IsNullOrEmpty(cachedRazerDpiPath))
                {
                    if (TryQueryRazerDpiByPath(cachedRazerDpiPath, cachedRazerReportLength, cachedRazerPrepended, out curDpi, out activeStage, out stageCount))
                    {
                        return true;
                    }
                }

                // Rate-limit full SetupDi enumeration so we don't spam Windows PnP when device is unplugged
                if ((DateTime.Now - lastRazerEnumTime).TotalMilliseconds < 10000)
                {
                    return false;
                }
                lastRazerEnumTime = DateTime.Now;

                Guid hidGuid;
                HidD_GetHidGuid(out hidGuid);

                IntPtr devInfo = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return false;

                SP_DEVICE_INTERFACE_DATA ifData = new SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(ifData);

                uint memberIdx = 0;
                try
                {
                    while (SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                    {
                        uint reqSize;
                        SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                        IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                        if (SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                        {
                            IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                            string devicePath = Marshal.PtrToStringAuto(pDevicePath);

                            if (devicePath != null && devicePath.ToLower().Contains("vid_1532"))
                            {
                                IntPtr handle = CreateFile(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                                if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                {
                                    try
                                    {
                                        IntPtr preparsed;
                                        if (HidD_GetPreparsedData(handle, out preparsed))
                                        {
                                            HIDP_CAPS caps;
                                            HidP_GetCaps(preparsed, out caps);
                                            HidD_FreePreparsedData(preparsed);

                                            if (caps.FeatureReportByteLength >= 90)
                                            {
                                                int curPid = 0;
                                                HIDD_ATTRIBUTES attr = new HIDD_ATTRIBUTES();
                                                attr.Size = Marshal.SizeOf(attr);
                                                if (HidD_GetAttributes(handle, ref attr))
                                                {
                                                    curPid = attr.ProductID;
                                                }
                                                int normPid = NormalizeRazerPid(curPid);
                                                string curNormId = "Razer:1532:" + normPid.ToString("X4");

                                                if (!string.IsNullOrEmpty(normTargetId) && !curNormId.Equals(normTargetId, StringComparison.OrdinalIgnoreCase))
                                                {
                                                    continue;
                                                }

                                                bool prepended = (caps.FeatureReportByteLength == 91);
                                                if (TryQueryRazerDpiByHandle(handle, caps.FeatureReportByteLength, prepended, out curDpi, out activeStage, out stageCount))
                                                {
                                                    cachedRazerDpiPath = devicePath;
                                                    cachedRazerReportLength = caps.FeatureReportByteLength;
                                                    cachedRazerPrepended = prepended;

                                                    st = GetOrCreateState(curNormId);
                                                    st.DevicePath = devicePath;
                                                    st.ReportLength = caps.FeatureReportByteLength;
                                                    st.IsPrepended = prepended;
                                                    st.Dpi = curDpi;
                                                    st.DpiY = (CachedDpiY > 0) ? CachedDpiY : curDpi;
                                                    st.DpiStage = activeStage;
                                                    st.DpiStageCount = stageCount;
                                                    st.DpiStages = CachedDpiStages;
                                                    st.DpiStagesY = CachedDpiStagesY;
                                                    st.IsXyIndependent = CachedIsXyIndependent;
                                                    st.XyRatio = CachedXyRatio;
                                                    st.LastUpdated = DateTime.Now;

                                                    Marshal.FreeHGlobal(detailBuffer);
                                                    return true;
                                                }
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        CloseHandle(handle);
                                    }
                                }
                            }
                        }
                        Marshal.FreeHGlobal(detailBuffer);
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(devInfo);
                }
            }
            return false;
        }

        private static bool TryQueryRazerDpiByPath(string path, int reportLength, bool prepended, out int curDpi, out int activeStage, out int stageCount, byte preferredTid = 0x1F)
        {
            curDpi = 0;
            activeStage = 0;
            stageCount = 0;

            IntPtr handle = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle.ToInt64() == -1)
            {
                cachedRazerDpiPath = null; // Invalidate if dongle unplugged
                return false;
            }

            try
            {
                return TryQueryRazerDpiByHandle(handle, reportLength, prepended, out curDpi, out activeStage, out stageCount, preferredTid);
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private static bool TryQueryRazerDpiByHandle(IntPtr handle, int reportLength, bool prepended, out int curDpi, out int activeStage, out int stageCount, byte preferredTid = 0x1F)
        {
            curDpi = 0;
            activeStage = 0;
            stageCount = 0;

            int offset = prepended ? 1 : 0;
            byte[] tids = GetAdaptiveTransactionIds(preferredTid);

            // Phase 1: Try reading Stage Table (0x04 0x86) across adaptive Transaction IDs
            foreach (byte tid in tids)
            {
                byte[] stagesReq = CreateRazerReport(tid, 0x04, 0x86, 0x26, reportLength, prepended, new byte[] { 0x01 });

                if (HidD_SetFeature(handle, stagesReq, stagesReq.Length))
                {
                    byte[] resp = new byte[reportLength];
                    if (prepended) resp[0] = 0x00;

                    // Retry up to 3 times with 12ms delay to allow wireless dongle to process request
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        Thread.Sleep(12);

                        if (HidD_GetFeature(handle, resp, resp.Length))
                        {
                            byte status = resp[offset + 0];
                            byte cmdClass = resp[offset + 6];
                            byte cmdId = resp[offset + 7];

                            if (status == 0x01) // BUSY
                            {
                                continue;
                            }

                            // Strictly verify packet is the response to our 0x04 0x86 query
                            if (status == 0x02 && cmdClass == 0x04 && cmdId == 0x86)
                            {
                                int readStage = resp[offset + 9];
                                int readCount = resp[offset + 10];
                                if (readCount >= 1 && readCount <= 5 && readStage >= 1 && readStage <= readCount)
                                {
                                    int readDpiX = 0;
                                    int readDpiY = 0;
                                    int[] readStagesX = new int[readCount];
                                    int[] readStagesY = new int[readCount];
                                    bool anyDiff = false;
                                    for (int s = 0; s < readCount; s++)
                                    {
                                        int stOffset = offset + 11 + (s * 7);
                                        int sx = (resp[stOffset + 1] << 8) | resp[stOffset + 2];
                                        int sy = (resp[stOffset + 3] << 8) | resp[stOffset + 4];
                                        if (sy <= 0) sy = sx;
                                        readStagesX[s] = sx;
                                        readStagesY[s] = sy;
                                        if (sx != sy) anyDiff = true;

                                        int stNum = resp[stOffset];
                                        if (stNum == readStage)
                                        {
                                            readDpiX = sx;
                                            readDpiY = sy;
                                        }
                                    }
                                    if (readDpiX >= 100 && readDpiX <= 50000)
                                    {
                                        curDpi = readDpiX;
                                        activeStage = readStage;
                                        stageCount = readCount;
                                        CachedDpi = curDpi;
                                        CachedDpiY = (readDpiY > 0) ? readDpiY : curDpi;
                                        CachedDpiStage = activeStage;
                                        CachedDpiStageCount = stageCount;
                                        CachedDpiStages = readStagesX;
                                        CachedDpiStagesY = readStagesY;
                                        CachedIsXyIndependent = (CachedDpi != CachedDpiY) || anyDiff;
                                        CachedXyRatio = (CachedDpi > 0) ? ((double)CachedDpiY / CachedDpi) : 1.0;
                                        return true;
                                    }
                                }
                            }
                            else
                            {
                                // Packet in buffer belonged to a different query (e.g. stale battery 0x07 0x80); retry
                                continue;
                            }
                        }
                    }
                }
            }

            // Phase 2: Fallback to Live XY DPI (0x04 0x85) for mice without onboard stage memory
            foreach (byte tid in tids)
            {
                byte[] dpiReq = CreateRazerReport(tid, 0x04, 0x85, 0x07, reportLength, prepended, new byte[] { 0x00 });
                if (HidD_SetFeature(handle, dpiReq, dpiReq.Length))
                {
                    byte[] dpiResp = new byte[reportLength];
                    if (prepended) dpiResp[0] = 0x00;

                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        Thread.Sleep(12);
                        if (HidD_GetFeature(handle, dpiResp, dpiResp.Length))
                        {
                            byte status = dpiResp[offset + 0];
                            byte cmdClass = dpiResp[offset + 6];
                            byte cmdId = dpiResp[offset + 7];

                            if (status == 0x01) continue; // BUSY

                            if (status == 0x02 && cmdClass == 0x04 && cmdId == 0x85)
                            {
                                int readDpiX = (dpiResp[offset + 9] << 8) | dpiResp[offset + 10];
                                int readDpiY = (dpiResp[offset + 11] << 8) | dpiResp[offset + 12];
                                if (readDpiY <= 0) readDpiY = readDpiX;
                                if (readDpiX >= 100 && readDpiX <= 50000)
                                {
                                    curDpi = readDpiX;
                                    activeStage = (CachedDpiStage > 0 ? CachedDpiStage : 1);
                                    stageCount = (CachedDpiStageCount > 0 ? CachedDpiStageCount : 5);
                                    CachedDpi = curDpi;
                                    CachedDpiY = readDpiY;
                                    CachedIsXyIndependent = (CachedDpi != CachedDpiY);
                                    CachedXyRatio = (CachedDpi > 0) ? ((double)CachedDpiY / CachedDpi) : 1.0;
                                    return true;
                                }
                            }
                            else
                            {
                                continue;
                            }
                        }
                    }
                }
            }

            return false;
        }

        public static bool SetRazerDpiStage(int targetStage, string targetDeviceId = null)
        {
            string normTargetId = NormalizeRazerDeviceId(targetDeviceId);

            lock (hidLock)
            {
                var st = TryGetState(normTargetId);
                if (st != null && !string.IsNullOrEmpty(st.DevicePath))
                {
                    byte preferredTid = st.ActiveTransactionId != 0 ? st.ActiveTransactionId : (byte)0x1F;
                    if (TrySetRazerDpiStageByPath(st.DevicePath, st.ReportLength, st.IsPrepended, targetStage, preferredTid))
                    {
                        st.DpiStage = targetStage;
                        st.LastUpdated = DateTime.Now;
                        CachedDpiStage = targetStage;
                        SaveHardwareCache();
                        return true;
                    }
                }

                Guid hidGuid;
                HidD_GetHidGuid(out hidGuid);

                IntPtr devInfo = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return false;

                SP_DEVICE_INTERFACE_DATA ifData = new SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(ifData);

                uint memberIdx = 0;
                try
                {
                    while (SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                    {
                        uint reqSize;
                        SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                        IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                        if (SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                        {
                            IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                            string devicePath = Marshal.PtrToStringAuto(pDevicePath);

                            if (devicePath != null && devicePath.ToLower().Contains("vid_1532"))
                            {
                                IntPtr handle = CreateFile(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                                if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                {
                                    try
                                    {
                                        IntPtr preparsed;
                                        if (HidD_GetPreparsedData(handle, out preparsed))
                                        {
                                            HIDP_CAPS caps;
                                            HidP_GetCaps(preparsed, out caps);
                                            HidD_FreePreparsedData(preparsed);

                                            if (caps.FeatureReportByteLength >= 90)
                                            {
                                                int curPid = 0;
                                                HIDD_ATTRIBUTES attr = new HIDD_ATTRIBUTES();
                                                attr.Size = Marshal.SizeOf(attr);
                                                if (HidD_GetAttributes(handle, ref attr))
                                                {
                                                    curPid = attr.ProductID;
                                                }
                                                int normPid = NormalizeRazerPid(curPid);
                                                string curNormId = "Razer:1532:" + normPid.ToString("X4");

                                                if (!string.IsNullOrEmpty(normTargetId) && !curNormId.Equals(normTargetId, StringComparison.OrdinalIgnoreCase))
                                                {
                                                    continue;
                                                }

                                                bool prepended = (caps.FeatureReportByteLength == 91);
                                                st = GetOrCreateState(curNormId);
                                                byte preferredTid = st.ActiveTransactionId != 0 ? st.ActiveTransactionId : (byte)0x1F;
                                                if (TrySetRazerDpiStageByHandle(handle, caps.FeatureReportByteLength, prepended, targetStage, preferredTid))
                                                {
                                                    st.DevicePath = devicePath;
                                                    st.ReportLength = caps.FeatureReportByteLength;
                                                    st.IsPrepended = prepended;
                                                    st.DpiStage = targetStage;
                                                    st.LastUpdated = DateTime.Now;

                                                    CachedDpiStage = targetStage;
                                                    SaveHardwareCache();

                                                    Marshal.FreeHGlobal(detailBuffer);
                                                    return true;
                                                }
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        CloseHandle(handle);
                                    }
                                }
                            }
                        }
                        Marshal.FreeHGlobal(detailBuffer);
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(devInfo);
                }
            }
            return false;
        }

        private static bool TrySetRazerDpiStageByPath(string path, int reportLength, bool prepended, int targetStage, byte preferredTid = 0x1F)
        {
            IntPtr handle = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;
            try
            {
                return TrySetRazerDpiStageByHandle(handle, reportLength, prepended, targetStage, preferredTid);
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private static bool TrySetRazerDpiStageByHandle(IntPtr handle, int reportLength, bool prepended, int targetStage, byte preferredTid = 0x1F)
        {
            int offset = prepended ? 1 : 0;
            byte[] tids = GetAdaptiveTransactionIds(preferredTid);

            foreach (byte tid in tids)
            {
                byte[] stagesReq = CreateRazerReport(tid, 0x04, 0x86, 0x26, reportLength, prepended, new byte[] { 0x01 });
                if (HidD_SetFeature(handle, stagesReq, stagesReq.Length))
                {
                    Thread.Sleep(15);
                    byte[] resp = new byte[reportLength];
                    if (prepended) resp[0] = 0x00;
                    if (HidD_GetFeature(handle, resp, resp.Length) && resp[offset + 0] == 0x02 && resp[offset + 6] == 0x04 && resp[offset + 7] == 0x86)
                    {
                        byte[] fullPayload = new byte[0x26];
                        Array.Copy(resp, offset + 8, fullPayload, 0, 0x26);
                        fullPayload[1] = (byte)targetStage;

                        byte[] setReq = CreateRazerReport(tid, 0x04, 0x06, 0x26, reportLength, prepended, fullPayload);
                        if (HidD_SetFeature(handle, setReq, setReq.Length))
                        {
                            Thread.Sleep(20);
                            byte[] setResp = new byte[reportLength];
                            if (prepended) setResp[0] = 0x00;
                            if (HidD_GetFeature(handle, setResp, setResp.Length) && setResp[offset + 0] == 0x02 && setResp[offset + 6] == 0x04 && setResp[offset + 7] == 0x06)
                            {
                                return true;
                            }
                        }
                    }
                }
            }
            return false;
        }

        public class RazerDpiCapability
        {
            public int MinDpi;
            public int MaxDpi;
            public int Step;
            public string Description;
        }

        public static RazerDpiCapability GetDpiCapability(string deviceName, int pid)
        {
            string name = !string.IsNullOrEmpty(deviceName) ? deviceName.ToLowerInvariant() : "";
            // DeathAdder V2 X HyperSpeed: 14K 光学传感器，50 DPI 步进
            if (pid == 0x009C || name.Contains("deathadder v2 x"))
            {
                var cap14k = new RazerDpiCapability();
                cap14k.MinDpi = 100;
                cap14k.MaxDpi = 14000;
                cap14k.Step = 50;
                cap14k.Description = "Razer 14K 光学传感器";
                return cap14k;
            }
            bool isCarbon = (pid == 0x00EF || pid == 0x00F0 || name.Contains("carbon") || name.Contains("碳纤维"));

            // 1. Focus Pro 50K Gen-3: 毒蝰 V4 Pro (Viper V4 Pro)、炼狱蝰蛇 V4 Pro 碳纤维版 (50,000 DPI，帧同步 FrameSync，1-DPI 精准步进)
            if (pid == 0x00E5 || pid == 0x00E6 || name.Contains("viper v4 pro") ||
                ((pid == 0x00EF || pid == 0x00F0 || pid == 0x00BE || pid == 0x00BF || name.Contains("deathadder v4 pro")) && isCarbon))
            {
                var cap = new RazerDpiCapability();
                cap.MinDpi = 100;
                cap.MaxDpi = 50000;
                cap.Step = 1;
                cap.Description = "Focus Pro 50K Gen-3 · 1-DPI 精准步进 (帧同步)";
                return cap;
            }

            // 2. 第二代 Focus Pro 45K: 炼狱蝰蛇 V4 Pro 标准版 (45,000 DPI，900 IPS，85 G，1-DPI 精准步进)
            if ((pid == 0x00BE || pid == 0x00BF || name.Contains("deathadder v4 pro")) && !isCarbon)
            {
                var cap = new RazerDpiCapability();
                cap.MinDpi = 100;
                cap.MaxDpi = 45000;
                cap.Step = 1;
                cap.Description = "第二代 Focus Pro 45K · 1-DPI 精准步进";
                return cap;
            }

                        // 3. Focus Pro 33K: Razer DeathAdder V4 HyperSpeed 与 Razer Viper V4 HyperSpeed (33,000 DPI、1-DPI 精准步进)
            if (pid == 0xB007 || pid == 0xB005 || name.Contains("deathadder v4 hyperspeed") || name.Contains("viper v4 hyperspeed"))
            {
                var cap = new RazerDpiCapability();
                cap.MinDpi = 100;
                cap.MaxDpi = 33000;
                cap.Step = 1;
                cap.Description = "Focus Pro 33K · 1-DPI 精准步进";
                return cap;
            }

            // 4. Focus Pro 35K Gen-2: Viper V3 Pro SE, Basilisk V3 Pro 35K, Basilisk V3 35K
            if (pid == 0x00DF || pid == 0x00DE || pid == 0x00CC || pid == 0x00CD ||
                pid == 0x00D6 || pid == 0x00D7 || pid == 0x00C4 || pid == 0x00C5 ||
                name.Contains("viper v3 pro") || name.Contains("35k"))
            {
                var cap = new RazerDpiCapability();
                cap.MinDpi = 100;
                cap.MaxDpi = 35000;
                cap.Step = 1;
                cap.Description = "Focus Pro 35K Gen-2 · 1-DPI 精准步进";
                return cap;
            }
            // Focus Pro 30K: DeathAdder V3 Pro, Viper V2 Pro, Basilisk V3 Pro, Cobra Pro
            if (pid == 0x00B7 || pid == 0x00B6 || pid == 0x00A6 || pid == 0x00A5 || pid == 0x009B || pid == 0x009A || pid == 0x00AA || pid == 0x00AC || pid == 0x00AB || name.Contains("v3 pro") || name.Contains("v2 pro") || name.Contains("cobra pro"))
            {
                var cap = new RazerDpiCapability();
                cap.MinDpi = 100;
                cap.MaxDpi = 30000;
                cap.Step = 1;
                cap.Description = "Focus Pro 30K 传感器";
                return cap;
            }
            // Focus+ 20K: Viper Ultimate, DeathAdder V2 Pro, Basilisk Ultimate
            if (name.Contains("ultimate") || name.Contains("v2 pro") || name.Contains("8khz") || name.Contains("focus+"))
            {
                var cap = new RazerDpiCapability();
                cap.MinDpi = 100;
                cap.MaxDpi = 20000;
                cap.Step = 50;
                cap.Description = "Focus+ 20K 传感器";
                return cap;
            }
            // Entry level: Viper Mini, DeathAdder Essential
            if (name.Contains("mini"))
            {
                var cap = new RazerDpiCapability();
                cap.MinDpi = 200;
                cap.MaxDpi = 8500;
                cap.Step = 100;
                cap.Description = "8500 DPI 光学传感器";
                return cap;
            }
            if (name.Contains("essential"))
            {
                var cap = new RazerDpiCapability();
                cap.MinDpi = 200;
                cap.MaxDpi = 6400;
                cap.Step = 100;
                cap.Description = "6400 DPI 光学传感器";
                return cap;
            }
            // Default fallback
            var def = new RazerDpiCapability();
            def.MinDpi = 100;
            def.MaxDpi = 35000;
            def.Step = 1;
            def.Description = "雷蛇高精度光学传感器";
            return def;
        }

        public static bool UpdateRazerHardwareStages(int activeStage, int[] stages, string targetDeviceId = null)
        {
            return UpdateRazerHardwareStages(activeStage, stages, stages, targetDeviceId);
        }

        public static bool UpdateRazerHardwareStages(int activeStage, int[] stagesX, int[] stagesY, string targetDeviceId = null)
        {
            if (stagesX == null || stagesX.Length == 0) return false;
            if (stagesY == null || stagesY.Length < stagesX.Length) stagesY = stagesX;
            int count = Math.Max(1, Math.Min(5, stagesX.Length));
            if (activeStage < 1 || activeStage > count) activeStage = 1;

            string normTargetId = NormalizeRazerDeviceId(targetDeviceId);

            lock (hidLock)
            {
                var st = TryGetState(normTargetId);
                if (st != null && !string.IsNullOrEmpty(st.DevicePath))
                {
                    byte preferredTid = st.ActiveTransactionId != 0 ? st.ActiveTransactionId : (byte)0x1F;
                    if (TryUpdateRazerHardwareStagesByPath(st.DevicePath, st.ReportLength, st.IsPrepended, activeStage, stagesX, stagesY, count, preferredTid))
                    {
                        st.DpiStage = activeStage;
                        st.DpiStageCount = count;
                        st.DpiStages = stagesX;
                        st.DpiStagesY = stagesY;
                        st.Dpi = stagesX[activeStage - 1];
                        st.DpiY = stagesY[activeStage - 1];
                        st.IsXyIndependent = (st.Dpi != st.DpiY);
                        st.XyRatio = (st.Dpi > 0) ? ((double)st.DpiY / st.Dpi) : 1.0;
                        st.LastUpdated = DateTime.Now;

                        CachedDpiStage = activeStage;
                        CachedDpiStageCount = count;
                        CachedDpiStages = stagesX;
                        CachedDpiStagesY = stagesY;
                        CachedDpi = stagesX[activeStage - 1];
                        CachedDpiY = stagesY[activeStage - 1];
                        CachedIsXyIndependent = (CachedDpi != CachedDpiY);
                        CachedXyRatio = (CachedDpi > 0) ? ((double)CachedDpiY / CachedDpi) : 1.0;
                        CachedLastUpdated = DateTime.Now;
                        SaveHardwareCache();
                        return true;
                    }
                }

                Guid hidGuid;
                HidD_GetHidGuid(out hidGuid);

                IntPtr devInfo = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return false;

                SP_DEVICE_INTERFACE_DATA ifData = new SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(ifData);

                uint memberIdx = 0;
                try
                {
                    while (SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                    {
                        uint reqSize;
                        SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                        IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                        if (SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                        {
                            IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                            string devicePath = Marshal.PtrToStringAuto(pDevicePath);

                            if (devicePath != null && devicePath.ToLower().Contains("vid_1532"))
                            {
                                IntPtr handle = CreateFile(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                                if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                {
                                    try
                                    {
                                        IntPtr preparsed;
                                        if (HidD_GetPreparsedData(handle, out preparsed))
                                        {
                                            HIDP_CAPS caps;
                                            HidP_GetCaps(preparsed, out caps);
                                            HidD_FreePreparsedData(preparsed);

                                            if (caps.FeatureReportByteLength >= 90)
                                            {
                                                int curPid = 0;
                                                HIDD_ATTRIBUTES attr = new HIDD_ATTRIBUTES();
                                                attr.Size = Marshal.SizeOf(attr);
                                                if (HidD_GetAttributes(handle, ref attr))
                                                {
                                                    curPid = attr.ProductID;
                                                }
                                                int normPid = NormalizeRazerPid(curPid);
                                                string curNormId = "Razer:1532:" + normPid.ToString("X4");

                                                if (!string.IsNullOrEmpty(normTargetId) && !curNormId.Equals(normTargetId, StringComparison.OrdinalIgnoreCase))
                                                {
                                                    continue;
                                                }

                                                bool prepended = (caps.FeatureReportByteLength == 91);
                                                st = GetOrCreateState(curNormId);
                                                byte preferredTid = st.ActiveTransactionId != 0 ? st.ActiveTransactionId : (byte)0x1F;
                                                if (TryUpdateRazerHardwareStagesByHandle(handle, caps.FeatureReportByteLength, prepended, activeStage, stagesX, stagesY, count, preferredTid))
                                                {
                                                    st.DevicePath = devicePath;
                                                    st.ReportLength = caps.FeatureReportByteLength;
                                                    st.IsPrepended = prepended;
                                                    st.DpiStage = activeStage;
                                                    st.DpiStageCount = count;
                                                    st.DpiStages = stagesX;
                                                    st.DpiStagesY = stagesY;
                                                    st.Dpi = stagesX[activeStage - 1];
                                                    st.DpiY = stagesY[activeStage - 1];
                                                    st.IsXyIndependent = (st.Dpi != st.DpiY);
                                                    st.XyRatio = (st.Dpi > 0) ? ((double)st.DpiY / st.Dpi) : 1.0;
                                                    st.LastUpdated = DateTime.Now;

                                                    CachedDpiStage = activeStage;
                                                    CachedDpiStageCount = count;
                                                    CachedDpiStages = stagesX;
                                                    CachedDpiStagesY = stagesY;
                                                    CachedDpi = stagesX[activeStage - 1];
                                                    CachedDpiY = stagesY[activeStage - 1];
                                                    CachedIsXyIndependent = (CachedDpi != CachedDpiY);
                                                    CachedXyRatio = (CachedDpi > 0) ? ((double)CachedDpiY / CachedDpi) : 1.0;
                                                    CachedLastUpdated = DateTime.Now;
                                                    SaveHardwareCache();

                                                    Marshal.FreeHGlobal(detailBuffer);
                                                    return true;
                                                }
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        CloseHandle(handle);
                                    }
                                }
                            }
                        }
                        Marshal.FreeHGlobal(detailBuffer);
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(devInfo);
                }
            }
            return false;
        }

        private static bool TryUpdateRazerHardwareStagesByPath(string path, int reportLength, bool prepended, int activeStage, int[] stagesX, int[] stagesY, int count, byte preferredTid = 0x1F)
        {
            IntPtr handle = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;
            try
            {
                return TryUpdateRazerHardwareStagesByHandle(handle, reportLength, prepended, activeStage, stagesX, stagesY, count, preferredTid);
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private static bool TryUpdateRazerHardwareStagesByHandle(IntPtr handle, int reportLength, bool prepended, int activeStage, int[] stagesX, int[] stagesY, int count, byte preferredTid = 0x1F)
        {
            int offset = prepended ? 1 : 0;
            byte[] tids = GetAdaptiveTransactionIds(preferredTid);

            // Phase 1: Try writing multi-stage EEPROM table (0x04 0x06) across adaptive Transaction IDs
            foreach (byte tid in tids)
            {
                byte[] fullPayload = new byte[0x26];
                byte[] stagesReq = CreateRazerReport(tid, 0x04, 0x86, 0x26, reportLength, prepended, new byte[] { 0x01 });
                if (HidD_SetFeature(handle, stagesReq, stagesReq.Length))
                {
                    Thread.Sleep(15);
                    byte[] resp = new byte[reportLength];
                    if (prepended) resp[0] = 0x00;
                    if (HidD_GetFeature(handle, resp, resp.Length) && resp[offset + 0] == 0x02)
                    {
                        Array.Copy(resp, offset + 8, fullPayload, 0, 0x26);
                    }
                }

                fullPayload[0] = 0x01; // VARSTORE
                fullPayload[1] = (byte)activeStage;
                fullPayload[2] = (byte)count;

                for (int s = 0; s < count; s++)
                {
                    int stOffset = 3 + (s * 7);
                    fullPayload[stOffset] = (byte)(s + 1);
                    int dpiX = Math.Max(100, Math.Min(50000, stagesX[s]));
                    int dpiY = (stagesY != null && s < stagesY.Length) ? Math.Max(100, Math.Min(50000, stagesY[s])) : dpiX;
                    fullPayload[stOffset + 1] = (byte)((dpiX >> 8) & 0xFF);
                    fullPayload[stOffset + 2] = (byte)(dpiX & 0xFF);
                    fullPayload[stOffset + 3] = (byte)((dpiY >> 8) & 0xFF);
                    fullPayload[stOffset + 4] = (byte)(dpiY & 0xFF);
                    fullPayload[stOffset + 5] = 0x00;
                    fullPayload[stOffset + 6] = 0x00;
                }

                byte[] setReq = CreateRazerReport(tid, 0x04, 0x06, 0x26, reportLength, prepended, fullPayload);
                if (HidD_SetFeature(handle, setReq, setReq.Length))
                {
                    Thread.Sleep(20);
                    byte[] setResp = new byte[reportLength];
                    if (prepended) setResp[0] = 0x00;
                    if (HidD_GetFeature(handle, setResp, setResp.Length) && setResp[offset + 0] == 0x02)
                    {
                        return true;
                    }
                }
            }

            // Phase 2: Dual-Protocol Fallback for mice without onboard stage memory (e.g. DeathAdder Essential, Viper Mini)
            // Directly write active stage DPI into optical sensor register via 0x04 0x05
            int activeDpiX = (activeStage >= 1 && activeStage <= stagesX.Length) ? stagesX[activeStage - 1] : stagesX[0];
            int activeDpiY = (stagesY != null && activeStage >= 1 && activeStage <= stagesY.Length) ? stagesY[activeStage - 1] : activeDpiX;
            if (TrySetRazerDpiByHandle(handle, reportLength, prepended, activeDpiX, activeDpiY, preferredTid))
            {
                return true;
            }

            return false;
        }

        public static bool SetRazerDpi(int dpi, string targetDeviceId = null)
        {
            return SetRazerDpi(dpi, dpi, targetDeviceId);
        }

        public static bool SetRazerDpi(int dpiX, int dpiY, string targetDeviceId = null)
        {
            string normTargetId = NormalizeRazerDeviceId(targetDeviceId);

            lock (hidLock)
            {
                var st = TryGetState(normTargetId);
                if (st != null && !string.IsNullOrEmpty(st.DevicePath))
                {
                    byte preferredTid = st.ActiveTransactionId != 0 ? st.ActiveTransactionId : (byte)0x1F;
                    if (TrySetRazerDpiByPath(st.DevicePath, st.ReportLength, st.IsPrepended, dpiX, dpiY, preferredTid))
                    {
                        st.Dpi = dpiX;
                        st.DpiY = dpiY;
                        st.IsXyIndependent = (dpiX != dpiY);
                        st.XyRatio = (dpiX > 0) ? ((double)dpiY / dpiX) : 1.0;
                        st.LastUpdated = DateTime.Now;

                        CachedDpi = dpiX;
                        CachedDpiY = dpiY;
                        CachedIsXyIndependent = (dpiX != dpiY);
                        CachedXyRatio = (dpiX > 0) ? ((double)dpiY / dpiX) : 1.0;
                        CachedLastUpdated = DateTime.Now;
                        SaveHardwareCache();
                        return true;
                    }
                }

                Guid hidGuid;
                HidD_GetHidGuid(out hidGuid);

                IntPtr devInfo = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return false;

                SP_DEVICE_INTERFACE_DATA ifData = new SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(ifData);

                uint memberIdx = 0;
                try
                {
                    while (SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                    {
                        uint reqSize;
                        SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                        IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                        if (SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                        {
                            IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                            string devicePath = Marshal.PtrToStringAuto(pDevicePath);

                            if (devicePath != null && devicePath.ToLower().Contains("vid_1532"))
                            {
                                IntPtr handle = CreateFile(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                                if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                {
                                    try
                                    {
                                        IntPtr preparsed;
                                        if (HidD_GetPreparsedData(handle, out preparsed))
                                        {
                                            HIDP_CAPS caps;
                                            HidP_GetCaps(preparsed, out caps);
                                            HidD_FreePreparsedData(preparsed);

                                            if (caps.FeatureReportByteLength >= 90)
                                            {
                                                int curPid = 0;
                                                HIDD_ATTRIBUTES attr = new HIDD_ATTRIBUTES();
                                                attr.Size = Marshal.SizeOf(attr);
                                                if (HidD_GetAttributes(handle, ref attr))
                                                {
                                                    curPid = attr.ProductID;
                                                }
                                                int normPid = NormalizeRazerPid(curPid);
                                                string curNormId = "Razer:1532:" + normPid.ToString("X4");

                                                if (!string.IsNullOrEmpty(normTargetId) && !curNormId.Equals(normTargetId, StringComparison.OrdinalIgnoreCase))
                                                {
                                                    continue;
                                                }

                                                bool prepended = (caps.FeatureReportByteLength == 91);
                                                st = GetOrCreateState(curNormId);
                                                byte preferredTid = st.ActiveTransactionId != 0 ? st.ActiveTransactionId : (byte)0x1F;
                                                if (TrySetRazerDpiByHandle(handle, caps.FeatureReportByteLength, prepended, dpiX, dpiY, preferredTid))
                                                {
                                                    st.DevicePath = devicePath;
                                                    st.ReportLength = caps.FeatureReportByteLength;
                                                    st.IsPrepended = prepended;
                                                    st.Dpi = dpiX;
                                                    st.DpiY = dpiY;
                                                    st.IsXyIndependent = (dpiX != dpiY);
                                                    st.XyRatio = (dpiX > 0) ? ((double)dpiY / dpiX) : 1.0;
                                                    st.LastUpdated = DateTime.Now;

                                                    cachedRazerDpiPath = devicePath;
                                                    cachedRazerReportLength = caps.FeatureReportByteLength;
                                                    cachedRazerPrepended = prepended;
                                                    CachedDpi = dpiX;
                                                    CachedDpiY = dpiY;
                                                    CachedIsXyIndependent = (dpiX != dpiY);
                                                    CachedXyRatio = (dpiX > 0) ? ((double)dpiY / dpiX) : 1.0;
                                                    CachedLastUpdated = DateTime.Now;
                                                    SaveHardwareCache();

                                                    Marshal.FreeHGlobal(detailBuffer);
                                                    return true;
                                                }
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        CloseHandle(handle);
                                    }
                                }
                            }
                        }
                        Marshal.FreeHGlobal(detailBuffer);
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(devInfo);
                }
            }
            return false;
        }

        private static bool TrySetRazerDpiByPath(string path, int reportLength, bool prepended, int dpiX, int dpiY, byte preferredTid = 0x1F)
        {
            IntPtr handle = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;
            try
            {
                return TrySetRazerDpiByHandle(handle, reportLength, prepended, dpiX, dpiY, preferredTid);
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private static bool TrySetRazerDpiByHandle(IntPtr handle, int reportLength, bool prepended, int dpiX, int dpiY, byte preferredTid = 0x1F)
        {
            int offset = prepended ? 1 : 0;
            byte[] args = new byte[7];
            args[0] = 0x01; // VARSTORE
            args[1] = (byte)((dpiX >> 8) & 0xFF);
            args[2] = (byte)(dpiX & 0xFF);
            args[3] = (byte)((dpiY >> 8) & 0xFF);
            args[4] = (byte)(dpiY & 0xFF);
            args[5] = 0x00;
            args[6] = 0x00;

            byte[] tids = GetAdaptiveTransactionIds(preferredTid);
            foreach (byte tid in tids)
            {
                byte[] req = CreateRazerReport(tid, 0x04, 0x05, 0x07, reportLength, prepended, args);
                if (HidD_SetFeature(handle, req, req.Length))
                {
                    Thread.Sleep(15);
                    byte[] resp = new byte[reportLength];
                    if (prepended) resp[0] = 0x00;
                    if (HidD_GetFeature(handle, resp, resp.Length) && resp[offset + 0] == 0x02 && resp[offset + 6] == 0x04 && resp[offset + 7] == 0x05)
                    {
                        return true;
                    }
                }
            }
            return false;
        }

        public static bool SetRazerPollingRate(int hz, string targetDeviceId = null)
        {
            string normTargetId = NormalizeRazerDeviceId(targetDeviceId);

            byte rateByte = 0x02; // default 4000
            switch (hz)
            {
                case 8000: rateByte = 0x01; break;
                case 4000: rateByte = 0x02; break;
                case 2000: rateByte = 0x04; break;
                case 1000: rateByte = 0x08; break;
                case 500:  rateByte = 0x10; break;
                case 250:  rateByte = 0x20; break;
                case 125:  rateByte = 0x40; break;
            }

            lock (hidLock)
            {
                var st = TryGetState(normTargetId);
                if (st != null && !string.IsNullOrEmpty(st.DevicePath))
                {
                    byte preferredTid = st.ActiveTransactionId != 0 ? st.ActiveTransactionId : (byte)0x1F;
                    if (TrySetRazerPollingRateByPath(st.DevicePath, st.ReportLength, st.IsPrepended, rateByte, preferredTid))
                    {
                        st.PollingRate = hz;
                        st.LastUpdated = DateTime.Now;
                        CachedPollingRate = hz;
                        SaveHardwareCache();
                        return true;
                    }
                }

                Guid hidGuid;
                HidD_GetHidGuid(out hidGuid);

                IntPtr devInfo = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return false;

                SP_DEVICE_INTERFACE_DATA ifData = new SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(ifData);

                uint memberIdx = 0;
                try
                {
                    while (SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                    {
                        uint reqSize;
                        SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                        IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                        if (SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                        {
                            IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                            string devicePath = Marshal.PtrToStringAuto(pDevicePath);

                            if (devicePath != null && devicePath.ToLower().Contains("vid_1532"))
                            {
                                IntPtr handle = CreateFile(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                                if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                {
                                    try
                                    {
                                        IntPtr preparsed;
                                        if (HidD_GetPreparsedData(handle, out preparsed))
                                        {
                                            HIDP_CAPS caps;
                                            HidP_GetCaps(preparsed, out caps);
                                            HidD_FreePreparsedData(preparsed);

                                            if (caps.FeatureReportByteLength >= 90)
                                            {
                                                int curPid = 0;
                                                HIDD_ATTRIBUTES attr = new HIDD_ATTRIBUTES();
                                                attr.Size = Marshal.SizeOf(attr);
                                                if (HidD_GetAttributes(handle, ref attr))
                                                {
                                                    curPid = attr.ProductID;
                                                }
                                                int normPid = NormalizeRazerPid(curPid);
                                                string curNormId = "Razer:1532:" + normPid.ToString("X4");

                                                if (!string.IsNullOrEmpty(normTargetId) && !curNormId.Equals(normTargetId, StringComparison.OrdinalIgnoreCase))
                                                {
                                                    continue;
                                                }

                                                bool prepended = (caps.FeatureReportByteLength == 91);
                                                st = GetOrCreateState(curNormId);
                                                byte preferredTid = st.ActiveTransactionId != 0 ? st.ActiveTransactionId : (byte)0x1F;
                                                if (TrySetRazerPollingRateByHandle(handle, caps.FeatureReportByteLength, prepended, rateByte, preferredTid))
                                                {
                                                    st.DevicePath = devicePath;
                                                    st.ReportLength = caps.FeatureReportByteLength;
                                                    st.IsPrepended = prepended;
                                                    st.PollingRate = hz;
                                                    st.LastUpdated = DateTime.Now;

                                                    CachedPollingRate = hz;
                                                    SaveHardwareCache();

                                                    Marshal.FreeHGlobal(detailBuffer);
                                                    return true;
                                                }
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        CloseHandle(handle);
                                    }
                                }
                            }
                        }
                        Marshal.FreeHGlobal(detailBuffer);
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(devInfo);
                }
            }
            return false;
        }

        private static bool TrySetRazerPollingRateByPath(string path, int reportLength, bool prepended, byte rateByte, byte preferredTid = 0x1F)
        {
            IntPtr handle = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;
            try
            {
                return TrySetRazerPollingRateByHandle(handle, reportLength, prepended, rateByte, preferredTid);
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private static bool TrySetRazerPollingRateByHandle(IntPtr handle, int reportLength, bool prepended, byte rateByte, byte preferredTid = 0x1F)
        {
            int offset = prepended ? 1 : 0;
            byte[] args = new byte[] { 0x01, rateByte };
            byte[] tids = GetAdaptiveTransactionIds(preferredTid);

            foreach (byte tid in tids)
            {
                byte[] req = CreateRazerReport(tid, 0x00, 0x40, 0x02, reportLength, prepended, args);
                if (HidD_SetFeature(handle, req, req.Length))
                {
                    Thread.Sleep(15);
                    byte[] resp = new byte[reportLength];
                    if (prepended) resp[0] = 0x00;
                    if (HidD_GetFeature(handle, resp, resp.Length) && resp[offset + 0] == 0x02)
                    {
                        return true;
                    }
                }
            }
            return false;
        }
    }
}