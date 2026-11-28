// ============================================================================
// 模块名称: DeviceManager.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   全品牌外设扫描调度中心：统一设备轮询、多设备优先级仲裁与缓存管理
//   包含 Logitech MR0097 与 Keychron M6 内置专用驱动调度及物理身份合并。
//   扫描链路带 ScanTelemetry 分段遥测（各品牌 helper 耗时 + HID 枚举次数 → scan_trace.log）。
//   全量扫描有在途合流闸门（scanInFlight/pendingRescan）；自定义设备配置为只读缓存 + 启动期一次性迁移。
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
        private const string TrayLowBatteryFirstValName = "TrayLowBatteryFirst";
        public const int LowBatteryPriorityThreshold = 30;
        private const string CustomDevicesRegPath = @"Software\RazerBatteryTray\CustomDevices";
        private const string HiddenDevicesRegPath = @"Software\RazerBatteryTray\HiddenDevices";

        // ===== [M1 全量扫描在途合流器] =====
        // 背景：定时器 / 开机轮询 / USB 设备变更 / 手动刷新 / 页签切换会同时命中刷新入口，
        // 而 QueryAllDevicesCore 内部靠 ScanSyncRoot 串行，锁只把排队变成阻塞，扫描照样堆积。
        // 策略：scanInFlight 做在途令牌，抢不到令牌的调用只置 pendingRescan 并立刻返回最近一次快照；
        //       在途扫描结束后若发现 pendingRescan 被置位，补跑一次（最多一次，防止持续负载下饿死）。
        //       严禁用 Thread.Sleep 等锁。
        private static int scanInFlight;
        private static int pendingRescan;
        private static int coreScanInvocationCount;
        private static List<MouseBatteryInfo> lastScanSnapshot;
        private static readonly object scanSnapshotLock = new object();
        private static readonly ManualResetEvent scanSnapshotReady = new ManualResetEvent(false);
        private const int ColdStartSnapshotWaitMs = 12000;

        // ===== [P2 自定义设备配置只读缓存] =====
        // 改造前 LoadCustomDevices 在"枚举子键"的同时写回注册表（键盘品类迁移 / Ganss 归一化 /
        // 复合外设品类纠错），一次刷新被调用 10+ 次 → 每轮都在写 HKCU。现在：
        //   1. 读路径严格只读，且带进程内缓存（一次扫描只读一次注册表）；
        //   2. 写入口（Save/Delete）与每次扫描开头显式作废缓存；
        //   3. 历史脏数据纠正集中到 MigrateCustomDevicesOnce()（启动时一次性、幂等、带版本号）。
        private static List<CustomDeviceConfig> customDevicesCache;
        private static readonly object customDevicesCacheLock = new object();
        private const string MigrationRootRegPath = @"Software\FerrisPulse";
        private const string MigrationVersionValName = "MigrationVersion";
        private const int CurrentMigrationVersion = 1;

        // [P3] 前缀表提为静态只读：原实现每次调用都 new 一个 30 元素数组，而本函数在 UI 刷新
        // 与设备合并热路径上按设备反复调用（单设备最多生成 ~60 个候选串）。
        private static readonly string[] CommonDeviceIdPrefixes = new string[] { "Razer", "Gaming", "HID", "FDAS", "Script", "Valkyrie", "Rapoo", "RapooVT", "VGN", "VXE", "Zaopin", "Logitech", "Apple", "AppleHID", "AppleBT", "BT", "BLE", "HyperX", "Flydigi", "MCHOSE", "Sony", "SteelSeries", "Corsair", "ACER", "ASUS", "ATK", "EGG", "Ironcat", "KZZI", "Keychron" };

        private static void AddDeviceIdVariant(List<string> variants, HashSet<string> seen, string candidate)
        {
            if (string.IsNullOrEmpty(candidate)) return;
            if (seen.Add(candidate)) variants.Add(candidate);
        }

        public static List<string> GetDeviceIdVariants(string deviceId)
        {
            List<string> variants = new List<string>(64);
            if (string.IsNullOrEmpty(deviceId)) return variants;

            // [P3] 去重从 List.Contains 线性扫描换成 HashSet(OrdinalIgnoreCase)，命中仍是 O(1)
            HashSet<string> seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            AddDeviceIdVariant(variants, seen, deviceId);

            if (deviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase))
            {
                AddDeviceIdVariant(variants, seen, RazerDeviceHelper.NormalizeRazerDeviceId(deviceId));
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

                string normRazerSuffix = null;
                if (cleanSuffix.StartsWith("1532:", StringComparison.OrdinalIgnoreCase))
                {
                    string normRazerId = RazerDeviceHelper.NormalizeRazerDeviceId("Razer:" + cleanSuffix);
                    if (!string.IsNullOrEmpty(normRazerId) && normRazerId.Length > 6)
                    {
                        normRazerSuffix = normRazerId.Substring(6);
                    }
                }

                for (int i = 0; i < CommonDeviceIdPrefixes.Length; i++)
                {
                    string p = CommonDeviceIdPrefixes[i];
                    AddDeviceIdVariant(variants, seen, p + ":" + suffix);
                    AddDeviceIdVariant(variants, seen, p + ":" + cleanSuffix);
                    if (!string.IsNullOrEmpty(normRazerSuffix))
                    {
                        AddDeviceIdVariant(variants, seen, p + ":" + normRazerSuffix);
                    }
                }
            }
            return variants;
        }

        /// <summary>
        /// 生成托盘/状态栏多设备摘要：鼠标、键盘、耳机各取一台最优在线设备。
        /// 例："鼠标 51%⚡  |  键盘 100%  |  耳机 37%"。键盘有线供电时显示“供电中”。
        /// </summary>
        public static string BuildTrayBatterySummary(List<MouseBatteryInfo> devices)
        {
            if (devices == null || devices.Count == 0) return "";

            MouseBatteryInfo mouse = null;
            MouseBatteryInfo keyboard = null;
            MouseBatteryInfo headset = null;
            for (int i = 0; i < devices.Count; i++)
            {
                MouseBatteryInfo d = devices[i];
                if (d == null || !d.IsConnected) continue;
                if (IsDeviceHidden(d.DeviceId)) continue;

                if (d.Category == DeviceCategory.Mouse)
                {
                    if (IsBetterTrayCandidate(d, mouse)) mouse = d;
                }
                else if (d.Category == DeviceCategory.Keyboard)
                {
                    if (IsBetterTrayCandidate(d, keyboard)) keyboard = d;
                }
                else if (d.Category == DeviceCategory.Headset || d.Category == DeviceCategory.Earbuds)
                {
                    if (IsBetterTrayCandidate(d, headset)) headset = d;
                }
            }

            List<string> parts = new List<string>(3);
            string mouseText = FormatTrayBattery(mouse);
            string keyboardText = FormatTrayBattery(keyboard);
            string headsetText = FormatTrayBattery(headset);
            if (!string.IsNullOrEmpty(mouseText)) parts.Add("鼠标 " + mouseText);
            if (!string.IsNullOrEmpty(keyboardText)) parts.Add("键盘 " + keyboardText);
            if (!string.IsNullOrEmpty(headsetText)) parts.Add("耳机 " + headsetText);
            return string.Join("  |  ", parts.ToArray());
        }

        private static bool IsWiredDeviceTransport(MouseBatteryInfo dev)
        {
            if (dev == null || dev.IsDonglePresent || string.IsNullOrEmpty(dev.Transport)) return false;
            return (dev.Transport.IndexOf("USB", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    dev.Transport.IndexOf("有线", StringComparison.OrdinalIgnoreCase) >= 0) &&
                   dev.Transport.IndexOf("2.4G", StringComparison.OrdinalIgnoreCase) < 0 &&
                   dev.Transport.IndexOf("无线", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static bool IsWiredKeyboardTransport(MouseBatteryInfo dev)
        {
            if (dev == null || dev.Category != DeviceCategory.Keyboard || string.IsNullOrEmpty(dev.Transport)) return false;
            return (dev.Transport.IndexOf("USB", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    dev.Transport.IndexOf("有线", StringComparison.OrdinalIgnoreCase) >= 0) &&
                   dev.Transport.IndexOf("2.4G", StringComparison.OrdinalIgnoreCase) < 0 &&
                   dev.Transport.IndexOf("无线", StringComparison.OrdinalIgnoreCase) < 0;
        }

        private static int ScoreTrayCandidate(MouseBatteryInfo dev)
        {
            if (dev == null) return -1;
            int score = 0;
            if (IsWiredKeyboardTransport(dev)) score += 4;
            if (!dev.IsSleeping) score += 2;
            if (dev.BatteryPercent >= 0) score += 1;
            return score;
        }

        private static bool IsBetterTrayCandidate(MouseBatteryInfo candidate, MouseBatteryInfo current)
        {
            if (candidate == null) return false;
            if (current == null) return true;
            return ScoreTrayCandidate(candidate) > ScoreTrayCandidate(current);
        }

        private static string FormatTrayBattery(MouseBatteryInfo dev)
        {
            if (dev == null) return null;

            if (IsWiredKeyboardTransport(dev)) return "供电中";
            if (dev.BatteryPercent < 0 && !dev.IsSleeping && !dev.IsCharging && IsWiredDeviceTransport(dev)) return "供电中";
            if (dev.IsSleeping) return "休眠";
            if (dev.IsCharging)
            {
                return dev.BatteryPercent > 0 ? (dev.BatteryPercent.ToString() + "%⚡") : "充电中";
            }
            if (dev.BatteryPercent > 0) return dev.BatteryPercent.ToString() + "%";
            return "--";
        }

        private static string NormalizeKeyboardModelKey(MouseBatteryInfo dev)
        {
            if (dev == null) return "";
            string raw = !string.IsNullOrEmpty(dev.DeviceName) ? dev.DeviceName : dev.CustomName;
            if (string.IsNullOrEmpty(raw)) return "";
            StringBuilder sb = new StringBuilder(raw.Length);
            for (int i = 0; i < raw.Length; i++)
            {
                char c = raw[i];
                if (!char.IsWhiteSpace(c)) sb.Append(char.ToUpperInvariant(c));
            }
            return sb.ToString();
        }

        private static bool AreSameKeyboardModel(string keyA, string keyB)
        {
            if (string.IsNullOrEmpty(keyA) || string.IsNullOrEmpty(keyB)) return false;
            return keyA.IndexOf(keyB, StringComparison.Ordinal) >= 0 ||
                   keyB.IndexOf(keyA, StringComparison.Ordinal) >= 0;
        }

        /// <summary>
        /// 三通道在线仲裁（USB 有线 / 蓝牙 优先于同型号 2.4G）：
        /// 仅当有线读到真实电量或充电响应，或蓝牙条目在线且电量 >= 0 时，将同型号 2.4G 条目置为离线休眠。
        /// 仅插线充电而键盘仍停留在 2.4G 通道时不会误判压制。
        /// </summary>
        public static void ApplyCrossTransportKeyboardArbitration(List<MouseBatteryInfo> devices)
        {
            if (devices == null || devices.Count < 2) return;

            List<MouseBatteryInfo> activeWired = new List<MouseBatteryInfo>();
            List<MouseBatteryInfo> activeBt = new List<MouseBatteryInfo>();
            for (int i = 0; i < devices.Count; i++)
            {
                MouseBatteryInfo d = devices[i];
                if (d == null || d.Category != DeviceCategory.Keyboard || !d.IsConnected) continue;
                if (IsWiredKeyboardTransport(d) && (d.BatteryPercent >= 0 || d.IsCharging))
                {
                    activeWired.Add(d);
                }
                else if (d.DeviceId != null && d.DeviceId.StartsWith("BT_", StringComparison.OrdinalIgnoreCase) && d.BatteryPercent >= 0)
                {
                    activeBt.Add(d);
                }
            }

            if (activeWired.Count == 0 && activeBt.Count == 0) return;

            for (int i = 0; i < devices.Count; i++)
            {
                MouseBatteryInfo d = devices[i];
                if (d == null || d.Category != DeviceCategory.Keyboard) continue;
                if (IsWiredKeyboardTransport(d)) continue;
                if (d.DeviceId != null && d.DeviceId.StartsWith("BT_", StringComparison.OrdinalIgnoreCase)) continue;

                string wirelessKey = NormalizeKeyboardModelKey(d);
                if (string.IsNullOrEmpty(wirelessKey)) continue;

                bool matchedWired = false;
                for (int wi = 0; wi < activeWired.Count; wi++)
                {
                    if (activeWired[wi] != d && AreSameKeyboardModel(wirelessKey, NormalizeKeyboardModelKey(activeWired[wi])))
                    {
                        matchedWired = true;
                        break;
                    }
                }
                if (matchedWired)
                {
                    d.IsConnected = false;
                    d.IsSleeping = true;
                    d.IsCharging = false;
                    d.CustomTooltip = "键盘当前走 USB 有线，2.4G 通道视为离线";
                    continue;
                }

                for (int bi = 0; bi < activeBt.Count; bi++)
                {
                    if (activeBt[bi] != d && AreSameKeyboardModel(wirelessKey, NormalizeKeyboardModelKey(activeBt[bi])))
                    {
                        d.IsConnected = false;
                        d.IsSleeping = true;
                        d.IsCharging = false;
                        d.CustomTooltip = "键盘当前走蓝牙，2.4G 通道视为离线";
                        break;
                    }
                }
            }
        }

        // 共享的 VID:PID 指纹（如旧通用记录的 HID:3537:1098）只对没有唯一身份的设备生效。
        // 启明星2 的 DeviceId 带手柄 MAC，旧通用记录留下的一次隐藏不得连带藏掉新品牌卡片，
        // 也不能误伤同型号的其它手柄；显式隐藏该 MAC 仍然有效。
        public static bool ShouldHideDevice(MouseBatteryInfo device, bool deviceIdHidden, bool fingerprintHidden)
        {
            if (device == null) return false;
            if (deviceIdHidden) return true;
            if (!fingerprintHidden) return false;
            if (IsBrandScopedDeviceId(device.DeviceId)) return false;
            return !GameSirDeviceHelper.HasPerUnitIdentity(device.DeviceId);
        }

        private static bool IsBrandScopedDeviceId(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return false;
            int colon = deviceId.IndexOf(':');
            if (colon <= 0) return false;
            string prefix = deviceId.Substring(0, colon);
            return !prefix.Equals("HID", StringComparison.OrdinalIgnoreCase) &&
                   !prefix.Equals("Gaming", StringComparison.OrdinalIgnoreCase) &&
                   !prefix.Equals("BT", StringComparison.OrdinalIgnoreCase) &&
                   !prefix.Equals("BLE", StringComparison.OrdinalIgnoreCase);
        }

        // [P3] 隐藏集合进程内快照：原实现每次 IsDeviceHidden 都打开一次注册表键，
        // 而它在 UI 刷新与 GetAllKnownDevicesForPriority 里按设备反复调用。
        private static HashSet<string> hiddenDeviceIdSet;
        private static readonly object hiddenCacheLock = new object();

        public static void InvalidateHiddenDevicesCache()
        {
            lock (hiddenCacheLock) { hiddenDeviceIdSet = null; }
        }

        private static HashSet<string> GetHiddenDeviceIdSet()
        {
            lock (hiddenCacheLock)
            {
                if (hiddenDeviceIdSet != null) return hiddenDeviceIdSet;
                HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(HiddenDevicesRegPath, false))
                    {
                        if (key != null)
                        {
                            string[] names = key.GetValueNames();
                            for (int i = 0; i < names.Length; i++)
                            {
                                object val = key.GetValue(names[i]);
                                if (val == null) continue;
                                try { if (Convert.ToInt32(val) == 1) set.Add(names[i]); }
                                catch { }
                            }
                        }
                    }
                }
                catch { }
                hiddenDeviceIdSet = set;
                return set;
            }
        }

        public static bool IsDeviceHidden(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return false;
            try
            {
                HashSet<string> hidden = GetHiddenDeviceIdSet();
                if (hidden.Count == 0) return false;
                var variants = GetDeviceIdVariants(deviceId);
                for (int i = 0; i < variants.Count; i++)
                {
                    if (hidden.Contains(variants[i])) return true;
                }
            }
            catch { }
            return false;
        }

        public static void HideOrDeleteDevice(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return;
            UpdateCorsairUserHiddenMarker(deviceId, true);
            var variants = GetDeviceIdVariants(deviceId);

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
                    }
                }
            }
            catch { }
            // [P3] 隐藏状态已变，立刻作废隐藏集合快照
            InvalidateHiddenDevicesCache();

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
            catch { }
        }

        public static void UnhideDevice(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return;
            UpdateCorsairUserHiddenMarker(deviceId, false);
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
            catch { }
            // [P3] 取消隐藏后立刻可见
            InvalidateHiddenDevicesCache();
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

        public static bool IsTrayLowBatteryFirstEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(PrimaryDeviceRegKey, false))
                {
                    if (key != null)
                    {
                        object v = key.GetValue(TrayLowBatteryFirstValName);
                        if (v != null) return Convert.ToInt32(v) == 1;
                    }
                }
            }
            catch { }
            return true;
        }

        public static void SetTrayLowBatteryFirstEnabled(bool enabled)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(PrimaryDeviceRegKey))
                {
                    if (key != null)
                    {
                        key.SetValue(TrayLowBatteryFirstValName, enabled ? 1 : 0, RegistryValueKind.DWord);
                    }
                }
            }
            catch { }
        }

        public static int FindPriorityIndex(List<string> prioList, string deviceId)
        {
            if (prioList == null || string.IsNullOrEmpty(deviceId)) return -1;
            for (int i = 0; i < prioList.Count; i++)
            {
                if (string.Equals(prioList[i], deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    return i;
                }
            }

            var variants = GetDeviceIdVariants(deviceId);
            for (int vi = 0; vi < variants.Count; vi++)
            {
                string cand = variants[vi];
                for (int i = 0; i < prioList.Count; i++)
                {
                    if (string.Equals(prioList[i], cand, StringComparison.OrdinalIgnoreCase))
                    {
                        return i;
                    }
                }
            }
            return -1;
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

        /// <summary>
        /// 获取所有已知设备（包含当前在线与历史离线设备），供“设备优先级管理”展示并维持持久化顺序
        /// </summary>
        public static List<MouseBatteryInfo> GetAllKnownDevicesForPriority(List<MouseBatteryInfo> activeDevices)
        {
            var result = new List<MouseBatteryInfo>();
            var seenIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            if (activeDevices != null)
            {
                for (int i = 0; i < activeDevices.Count; i++)
                {
                    var d = activeDevices[i];
                    if (d == null || IsDeviceHidden(d.DeviceId)) continue;
                    if (!string.IsNullOrEmpty(d.HardwareFingerprint) && IsDeviceHidden(d.HardwareFingerprint)) continue;
                    result.Add(d);
                    seenIds.Add(d.DeviceId);
                    if (!string.IsNullOrEmpty(d.HardwareFingerprint)) seenIds.Add(d.HardwareFingerprint);
                    var v = GetDeviceIdVariants(d.DeviceId);
                    for (int vi = 0; vi < v.Count; vi++) seenIds.Add(v[vi]);
                }
            }

            var prioList = GetPriorityList();
            var customConfigs = LoadCustomDevices();

            // Append offline devices from prioList
            for (int i = 0; i < prioList.Count; i++)
            {
                string pid = prioList[i];
                if (string.IsNullOrEmpty(pid) || IsDeviceHidden(pid) || seenIds.Contains(pid)) continue;

                var dev = CreateOfflineDeviceInfo(pid, customConfigs);
                if (dev != null)
                {
                    result.Add(dev);
                    seenIds.Add(dev.DeviceId);
                    if (!string.IsNullOrEmpty(dev.HardwareFingerprint)) seenIds.Add(dev.HardwareFingerprint);
                }
            }

            // Append any custom devices that might not be in prioList
            if (customConfigs != null)
            {
                for (int i = 0; i < customConfigs.Count; i++)
                {
                    var cfg = customConfigs[i];
                    if (cfg == null || string.IsNullOrEmpty(cfg.DeviceId) || IsDeviceHidden(cfg.DeviceId) || seenIds.Contains(cfg.DeviceId)) continue;
                    if (!string.IsNullOrEmpty(cfg.HardwareFingerprint) && (IsDeviceHidden(cfg.HardwareFingerprint) || seenIds.Contains(cfg.HardwareFingerprint))) continue;

                    var dev = CreateOfflineDeviceInfo(cfg.DeviceId, customConfigs);
                    if (dev != null)
                    {
                        result.Add(dev);
                        seenIds.Add(dev.DeviceId);
                        if (!string.IsNullOrEmpty(dev.HardwareFingerprint)) seenIds.Add(dev.HardwareFingerprint);
                    }
                }
            }

            // Sort all known devices according to prioList order
            result.Sort(delegate(MouseBatteryInfo a, MouseBatteryInfo b)
            {
                int ia = prioList.IndexOf(a.DeviceId);
                int ib = prioList.IndexOf(b.DeviceId);
                if (ia < 0) ia = 9999;
                if (ib < 0) ib = 9999;
                return ia.CompareTo(ib);
            });

            ApplyDeviceAppearances(result);

            for (int i = 0; i < result.Count; i++)
            {
                result[i].PriorityIndex = i + 1;
            }

            return result;
        }

        private static MouseBatteryInfo CreateOfflineDeviceInfo(string deviceId, List<CustomDeviceConfig> customConfigs)
        {
            var dev = new MouseBatteryInfo();
            dev.DeviceId = deviceId;
            dev.Category = DeviceCategory.Generic;
            dev.IsConnected = false;
            dev.BatteryPercent = -1;

            int batt = -1;
            bool isChg = false;
            string devName = null;
            DateTime dt = DateTime.MinValue;
            if (DriverStateStore.TryLoadBatteryState(deviceId, out batt, out isChg, out devName, out dt))
            {
                dev.BatteryPercent = batt;
                dev.IsCharging = isChg;
                dev.DeviceName = devName;
                dev.LastUpdated = dt;
            }

            CustomDeviceConfig cfg = null;
            if (customConfigs != null)
            {
                cfg = customConfigs.Find(delegate(CustomDeviceConfig c) {
                    if (c == null) return false;
                    return (c.DeviceId != null && c.DeviceId.Equals(deviceId, StringComparison.OrdinalIgnoreCase)) ||
                           (!string.IsNullOrEmpty(c.HardwareFingerprint) && c.HardwareFingerprint.Equals(deviceId, StringComparison.OrdinalIgnoreCase));
                });
            }

            if (cfg != null)
            {
                dev.IsCustom = true;
                dev.HardwareFingerprint = cfg.HardwareFingerprint;
                if (!string.IsNullOrEmpty(cfg.CustomName)) dev.CustomName = cfg.CustomName;
                if (!string.IsNullOrEmpty(cfg.OriginalName) && string.IsNullOrEmpty(dev.DeviceName)) dev.DeviceName = cfg.OriginalName;
                if (cfg.Category != DeviceCategory.Generic) dev.Category = cfg.Category;
                if (dev.BatteryPercent < 0 && cfg.CachedBattery > 0) dev.BatteryPercent = cfg.CachedBattery;
            }

            // If Razer device, resolve official name, category, model rates, and hardware cache state
            if (deviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase))
            {
                dev.Brand = "Razer";
                string hex = deviceId.Substring("Razer:1532:".Length).Trim();
                int pid = 0;
                int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out pid);
                string officialName = RazerDeviceHelper.GetRazerOfficialModelName(pid, dev.DeviceName);
                if (string.IsNullOrEmpty(dev.DeviceName)) dev.DeviceName = officialName;
                if (dev.Category == DeviceCategory.Generic) dev.Category = RazerDeviceHelper.GetRazerDeviceCategory(pid, officialName);
                if (dev.Category == DeviceCategory.Mouse)
                {
                    dev.SupportedPollingRates = RazerDeviceHelper.GetSupportedPollingRatesForModel(pid, officialName);
                }

                string normRzId = RazerDeviceHelper.NormalizeRazerDeviceId(deviceId);
                if (dev.BatteryPercent < 0 && !string.IsNullOrEmpty(normRzId) && !string.Equals(normRzId, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    int nb; bool nchg; string ndn; DateTime ndt;
                    if (DriverStateStore.TryLoadBatteryState(normRzId, out nb, out nchg, out ndn, out ndt))
                    {
                        dev.BatteryPercent = nb;
                        dev.IsCharging = nchg;
                        if (string.IsNullOrEmpty(dev.DeviceName) && !string.IsNullOrEmpty(ndn)) dev.DeviceName = ndn;
                        if (dev.LastUpdated == DateTime.MinValue) dev.LastUpdated = ndt;
                    }
                }

                var rzState = RazerDeviceHelper.GetDeviceState(deviceId);
                if (rzState != null)
                {
                    if (dev.BatteryPercent < 0 && rzState.BatteryPercent > 0) dev.BatteryPercent = rzState.BatteryPercent;
                    if (dev.ExactBatteryPercent <= 0 && rzState.ExactBatteryPercent > 0) dev.ExactBatteryPercent = rzState.ExactBatteryPercent;
                    if (dev.LastUpdated == DateTime.MinValue && rzState.LastUpdated != DateTime.MinValue) dev.LastUpdated = rzState.LastUpdated;
                    if (rzState.DpiStages != null && rzState.DpiStages.Length > 0) dev.DpiStages = (int[])rzState.DpiStages.Clone();
                    if (rzState.DpiStagesY != null && rzState.DpiStagesY.Length > 0) dev.DpiStagesY = (int[])rzState.DpiStagesY.Clone();
                    if (rzState.Dpi > 0) dev.Dpi = rzState.Dpi;
                    if (rzState.DpiY > 0) dev.DpiY = rzState.DpiY;
                    if (rzState.DpiStage > 0) dev.DpiStage = rzState.DpiStage;
                    if (rzState.DpiStageCount > 0) dev.DpiStageCount = rzState.DpiStageCount;
                    dev.IsXyIndependent = rzState.IsXyIndependent;
                    if (rzState.XyRatio > 0) dev.XyRatio = rzState.XyRatio;
                    if (rzState.PollingRate > 0) dev.PollingRate = rzState.PollingRate;
                }
                else if (!string.IsNullOrEmpty(RazerDeviceHelper.CachedDeviceName) &&
                         string.Equals(RazerDeviceHelper.CachedDeviceName, officialName, StringComparison.OrdinalIgnoreCase))
                {
                    if (dev.BatteryPercent < 0 && RazerDeviceHelper.CachedBatteryPercent > 0) dev.BatteryPercent = RazerDeviceHelper.CachedBatteryPercent;
                    if (dev.LastUpdated == DateTime.MinValue && RazerDeviceHelper.CachedLastUpdated != DateTime.MinValue) dev.LastUpdated = RazerDeviceHelper.CachedLastUpdated;
                    if (RazerDeviceHelper.CachedDpiStages != null && RazerDeviceHelper.CachedDpiStages.Length > 0) dev.DpiStages = (int[])RazerDeviceHelper.CachedDpiStages.Clone();
                    if (RazerDeviceHelper.CachedDpiStagesY != null && RazerDeviceHelper.CachedDpiStagesY.Length > 0) dev.DpiStagesY = (int[])RazerDeviceHelper.CachedDpiStagesY.Clone();
                    if (RazerDeviceHelper.CachedDpi > 0) dev.Dpi = RazerDeviceHelper.CachedDpi;
                    if (RazerDeviceHelper.CachedDpiY > 0) dev.DpiY = RazerDeviceHelper.CachedDpiY;
                    if (RazerDeviceHelper.CachedDpiStage > 0) dev.DpiStage = RazerDeviceHelper.CachedDpiStage;
                    if (RazerDeviceHelper.CachedDpiStageCount > 0) dev.DpiStageCount = RazerDeviceHelper.CachedDpiStageCount;
                    dev.IsXyIndependent = RazerDeviceHelper.CachedIsXyIndependent;
                    if (RazerDeviceHelper.CachedXyRatio > 0) dev.XyRatio = RazerDeviceHelper.CachedXyRatio;
                    if (RazerDeviceHelper.CachedPollingRate > 0) dev.PollingRate = RazerDeviceHelper.CachedPollingRate;
                }
            }
            else if (deviceId.StartsWith("Keychron:3434:D026:MS", StringComparison.OrdinalIgnoreCase) ||
                     deviceId.StartsWith("Keychron:3434:D060:MS", StringComparison.OrdinalIgnoreCase))
            {
                dev.Brand = "Keychron";
                dev.Category = DeviceCategory.Mouse;
                dev.DeviceName = "Keychron M6";
            }
            else if (deviceId.StartsWith("Logitech:046D:", StringComparison.OrdinalIgnoreCase) || deviceId.IndexOf("046D", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                dev.Brand = "Logitech";
                if (dev.Category == DeviceCategory.Generic) dev.Category = DeviceCategory.Mouse;
            }
            else if (deviceId.StartsWith("Rapoo:24AE:", StringComparison.OrdinalIgnoreCase))
            {
                dev.Brand = "Rapoo";
                if (dev.Category == DeviceCategory.Generic) dev.Category = DeviceCategory.Mouse;
                if (dev.Category == DeviceCategory.Mouse)
                {
                    string hex = deviceId.Substring("Rapoo:24AE:".Length).Trim();
                    int pid = 0;
                    int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out pid);
                    dev.SupportedPollingRates = RapooDeviceHelper.GetSupportedPollingRatesForModel(pid, dev.DeviceName);
                }
            }
            else if (deviceId.IndexOf(":KB", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     deviceId.IndexOf("NuPhy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     deviceId.IndexOf("WH80", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     deviceId.IndexOf("19F5", StringComparison.OrdinalIgnoreCase) >= 0 ||
                     (!string.IsNullOrEmpty(dev.DeviceName) &&
                      (dev.DeviceName.IndexOf("NuPhy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       dev.DeviceName.IndexOf("WH80", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       dev.DeviceName.IndexOf("Air75", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       dev.DeviceName.IndexOf("Air96", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       dev.DeviceName.IndexOf("Air60", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       dev.DeviceName.IndexOf("Keyboard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                       dev.DeviceName.Contains("键盘"))))
            {
                if (deviceId.IndexOf("NuPhy", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    deviceId.IndexOf("WH80", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    deviceId.IndexOf("19F5", StringComparison.OrdinalIgnoreCase) >= 0 ||
                    (!string.IsNullOrEmpty(dev.DeviceName) && dev.DeviceName.IndexOf("NuPhy", StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    if (string.IsNullOrEmpty(dev.Brand)) dev.Brand = "NuPhy";
                }
                if (dev.Category == DeviceCategory.Generic) dev.Category = DeviceCategory.Keyboard;
            }

            if (string.IsNullOrEmpty(dev.DeviceName))
            {
                dev.DeviceName = !string.IsNullOrEmpty(dev.CustomName) ? dev.CustomName : deviceId;
            }

            if (dev.Category == DeviceCategory.Mouse)
            {
                int cachedDx, cachedDy, cachedSt, cachedPr;
                bool loadedDpi = DriverStateStore.TryLoadDpiState(deviceId, out cachedDx, out cachedDy, out cachedSt, out cachedPr);
                if (!loadedDpi && deviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase))
                {
                    string normRz = RazerDeviceHelper.NormalizeRazerDeviceId(deviceId);
                    if (!string.IsNullOrEmpty(normRz))
                    {
                        loadedDpi = DriverStateStore.TryLoadDpiState(normRz, out cachedDx, out cachedDy, out cachedSt, out cachedPr);
                    }
                }
                if (loadedDpi)
                {
                    if (dev.Dpi <= 0 && cachedDx > 0) dev.Dpi = cachedDx;
                    if (dev.DpiY <= 0 && cachedDy > 0) dev.DpiY = cachedDy;
                    if (dev.DpiStage <= 0 && cachedSt > 0) dev.DpiStage = cachedSt;
                    if (dev.PollingRate <= 0 && cachedPr > 0) dev.PollingRate = cachedPr;
                }
                if (dev.DpiStages != null && dev.DpiStages.Length > 0 && dev.DpiStageCount <= 0)
                {
                    dev.DpiStageCount = dev.DpiStages.Length;
                }
            }

            return dev;
        }

        /// <summary>
        /// 读取已登记的自定义设备（纯只读）。进程内带缓存，写入口显式失效，
        /// 每次全量扫描开始时也会失效一次，保证一次刷新只读一次注册表。
        /// </summary>
        public static List<CustomDeviceConfig> LoadCustomDevices()
        {
            lock (customDevicesCacheLock)
            {
                if (customDevicesCache != null) return CloneCustomDevices(customDevicesCache);
            }

            List<CustomDeviceConfig> loaded = LoadCustomDevicesFromRegistry();
            lock (customDevicesCacheLock)
            {
                customDevicesCache = loaded;
                return CloneCustomDevices(loaded);
            }
        }

        /// <summary>
        /// 作废自定义设备缓存。所有写 CustomDevices 注册表的入口都必须调用。
        /// </summary>
        public static void InvalidateCustomDevicesCache()
        {
            lock (customDevicesCacheLock) { customDevicesCache = null; }
        }

        private static List<CustomDeviceConfig> CloneCustomDevices(List<CustomDeviceConfig> source)
        {
            List<CustomDeviceConfig> copy = new List<CustomDeviceConfig>();
            if (source == null) return copy;
            for (int i = 0; i < source.Count; i++)
            {
                CustomDeviceConfig src = source[i];
                if (src == null) continue;
                CustomDeviceConfig dst = new CustomDeviceConfig();
                dst.DeviceId = src.DeviceId;
                dst.HardwareFingerprint = src.HardwareFingerprint;
                dst.CustomName = src.CustomName;
                dst.OriginalName = src.OriginalName;
                dst.Category = src.Category;
                dst.Transport = src.Transport;
                dst.CachedBattery = src.CachedBattery;
                dst.LastSeen = src.LastSeen;
                dst.ThemeColorHex = src.ThemeColorHex;
                dst.TrayColorHex = src.TrayColorHex;
                dst.LinkTrayColorToTheme = src.LinkTrayColorToTheme;
                copy.Add(dst);
            }
            return copy;
        }

        /// <summary>
        /// 真正读注册表的实现（纯只读，绝不在读路径写回）。历史脏数据纠正见 MigrateCustomDevicesOnce。
        /// </summary>
        private static List<CustomDeviceConfig> LoadCustomDevicesFromRegistry()
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

                                        // 历史脏数据纠正已抽到 MigrateCustomDevicesOnce()（启动时一次性执行），
                                        // 读路径严格只读，绝不再顺带写注册表（P2）。

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

        /// <summary>
        /// 一次性历史数据迁移（幂等）：把改造前塞在读取路径里的三类纠正集中到启动时执行。
        /// 规则：① 键盘被误记成鼠标/占位 100% 电量；② Ganss 配置归一化；③ 复合外设品类权威纠错。
        /// 执行完成后写入 MigrationVersion，之后启动不再重复纠正。
        /// </summary>
        public static void MigrateCustomDevicesOnce()
        {
            bool migrated = false;
            try
            {
                using (var verKey = Registry.CurrentUser.OpenSubKey(MigrationRootRegPath, false))
                {
                    if (verKey != null)
                    {
                        int done = 0;
                        try { done = Convert.ToInt32(verKey.GetValue(MigrationVersionValName, 0)); }
                        catch { }
                        if (done >= CurrentMigrationVersion) return;
                    }
                }

                List<CustomDeviceConfig> configs = LoadCustomDevicesFromRegistry();
                for (int i = 0; i < configs.Count; i++)
                {
                    CustomDeviceConfig cfg = configs[i];
                    if (cfg == null || string.IsNullOrEmpty(cfg.DeviceId)) continue;
                    DeviceCategory beforeCategory = cfg.Category;
                    int beforeBattery = cfg.CachedBattery;
                    if (ApplyLegacyConfigNormalization(cfg) ||
                        cfg.Category != beforeCategory || cfg.CachedBattery != beforeBattery)
                    {
                        SaveCustomDevice(cfg);
                    }
                }
                migrated = true;
            }
            catch { }

            if (!migrated) return;
            try
            {
                using (var verKey = Registry.CurrentUser.CreateSubKey(MigrationRootRegPath))
                {
                    if (verKey != null) verKey.SetValue(MigrationVersionValName, CurrentMigrationVersion);
                }
            }
            catch { }
        }

        /// <summary>
        /// 历史脏数据纠正规则（原实现写在 LoadCustomDevices 的读取循环里）。返回是否被改写。
        /// </summary>
        private static bool ApplyLegacyConfigNormalization(CustomDeviceConfig cfg)
        {
            if (cfg == null || string.IsNullOrEmpty(cfg.DeviceId)) return false;
            bool changed = false;
            try
            {
                // ① 键盘被误记成鼠标，或 2.4G/USB 键盘被记成占位 100% 电量
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
                    changed = true;
                    string transport = cfg.Transport ?? "";
                    if (cfg.CachedBattery == 100 && (transport.Contains("2.4G") || transport.Contains("USB") || cfg.DeviceId.StartsWith("HID:19F5")))
                    {
                        cfg.CachedBattery = -1;
                    }
                }

                // ② Ganss 配置归一化
                if (GanssDeviceHelper.NormalizeConfig(cfg)) changed = true;

                // ③ 品类权威纠错：VID/PID 能唯一确定品类时以硬件事实为准。
                // 复合外设（如 3837:1018）同时暴露键盘与鼠标接口，手动添加时容易被记成 Keyboard；
                // 错误一旦固化，鼠标驱动 CanHandle 会拒绝接管，DPI/回报率控件整块消失。
                DeviceCategory authoritativeCat;
                if (MchoseDeviceHelper.TryGetAuthoritativeCategory(cfg.DeviceId, cfg.HardwareFingerprint, out authoritativeCat) &&
                    cfg.Category != authoritativeCat)
                {
                    cfg.Category = authoritativeCat;
                    changed = true;
                }
            }
            catch { }
            return changed;
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
                InvalidateCustomDevicesCache();
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
                InvalidateCustomDevicesCache();
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
            catch { }
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
            catch { }
        }


        public static void CleanupDuplicateHyperXDevices()
        {
            try
            {
                var customConfigs = LoadCustomDevices();
                foreach (var c in customConfigs)
                {
                    // 飓风3S (02CC / 06BE) 由专属通道独立供电量，不得被旧版飓风3去重逻辑误删配置
                    if (c.DeviceId != null && !HyperXCloud3SHelper.IsCloud3SIdentifier(c.DeviceId) &&
                        (c.DeviceId.StartsWith("HyperX:03F0:", StringComparison.OrdinalIgnoreCase) ||
                         c.DeviceId.StartsWith("HID:03F0:05B7", StringComparison.OrdinalIgnoreCase) ||
                         c.DeviceId.StartsWith("HID:03F0:0C9D", StringComparison.OrdinalIgnoreCase)))
                    {
                        DeleteCustomDevice(c.DeviceId);
                    }
                }
            }
            catch { }
        }

        public static void CleanupDuplicateBluetoothAudioDevices()
        {
            try
            {
                var customConfigs = LoadCustomDevices();
                var audioConfigs = customConfigs.FindAll(delegate(CustomDeviceConfig c) {
                    return (c.Category == DeviceCategory.Earbuds || c.Category == DeviceCategory.Headset) ||
                           (c.DeviceId != null && c.DeviceId.StartsWith("BT_", StringComparison.OrdinalIgnoreCase));
                });

                if (audioConfigs.Count > 1)
                {
                    var groups = new Dictionary<string, List<CustomDeviceConfig>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var c in audioConfigs)
                    {
                        string name = (c.OriginalName ?? c.CustomName ?? "").Trim();
                        if (string.IsNullOrEmpty(name)) name = c.DeviceId ?? "";
                        if (!groups.ContainsKey(name)) groups[name] = new List<CustomDeviceConfig>();
                        groups[name].Add(c);
                    }

                    foreach (var kvp in groups)
                    {
                        if (kvp.Value.Count > 1)
                        {
                            CustomDeviceConfig best = kvp.Value[0];
                            for (int i = 1; i < kvp.Value.Count; i++)
                            {
                                var cand = kvp.Value[i];
                                if ((cand.CachedBattery > 0 && best.CachedBattery <= 0) ||
                                    (cand.LastSeen > best.LastSeen && cand.CachedBattery >= best.CachedBattery))
                                {
                                    best = cand;
                                }
                            }

                            for (int i = 0; i < kvp.Value.Count; i++)
                            {
                                if (kvp.Value[i] != best)
                                {
                                    DeleteCustomDevice(kvp.Value[i].DeviceId);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        /// <summary>
        /// 清理迈从鼠标的历史通用记录。
        ///
        /// 背景：旧版通用 HID 扫描曾把 K7 V2 Ultra+（3837:1018）当成"无主 HID 鼠标"，
        /// 在 HKCU\...\CustomDevices 下写下 "HID:3837:1018" 的通用配置。如今该型号已由
        /// MchoseMouse4DHelper 原生接管（DeviceId 为 "MCHOSE:3837:1018"），两条记录并存
        /// 会让主界面出现"同一台鼠标两张卡片"，其中通用卡永远读不到电量（-1%）。
        /// 这里只删除通用前缀的记录，原生 "MCHOSE:" 记录保持不动。
        /// </summary>
        public static void CleanupDuplicateMchoseDevices()
        {
            try
            {
                var customConfigs = LoadCustomDevices();
                foreach (var c in customConfigs)
                {
                    if (c.DeviceId == null) continue;
                    if (!c.DeviceId.StartsWith("HID:3837:", StringComparison.OrdinalIgnoreCase)) continue;

                    int pid = 0;
                    string pidHex = c.DeviceId.Substring("HID:3837:".Length).Trim();
                    bool pidOk = int.TryParse(pidHex, System.Globalization.NumberStyles.HexNumber,
                        System.Globalization.CultureInfo.InvariantCulture, out pid);

                    // 仅当该 PID 已被 4D 原生驱动接管时才删除通用记录，
                    // 否则会误删尚未适配的迈从设备的用户自定义配置。
                    if (pidOk && MchoseMouse4DHelper.IsSupportedPid(pid))
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

        // 记录用户对 Corsair 设备的主动隐藏意图，迁移逻辑据此决定是否自动恢复显示
        private static void UpdateCorsairUserHiddenMarker(string deviceId, bool hidden)
        {
            if (string.IsNullOrEmpty(deviceId)) return;
            try
            {
                string lowerId = deviceId.ToLowerInvariant();
                if (lowerId.Contains("1b1c") &&
                    (lowerId.Contains("0a97") || lowerId.Contains("0a96") || lowerId.Contains("1bf2")))
                {
                    using (RegistryKey root = Registry.CurrentUser.CreateSubKey(@"Software\RazerBatteryTray"))
                    {
                        if (root != null)
                        {
                            if (hidden) root.SetValue("CorsairUserHidden", 1, RegistryValueKind.DWord);
                            else root.DeleteValue("CorsairUserHidden", false);
                        }
                    }
                }
            }
            catch { }
        }

        public static void CleanupDuplicateCorsairDevices()
        {
            try
            {
                var customConfigs = LoadCustomDevices();
                foreach (var c in customConfigs)
                {
                    if (c.DeviceId != null && (c.DeviceId.StartsWith("HID:1B1C:0A97", StringComparison.OrdinalIgnoreCase) ||
                                               c.DeviceId.StartsWith("HID:1B1C:0A96", StringComparison.OrdinalIgnoreCase) ||
                                               c.DeviceId.StartsWith("HID:1B1C:1BF2", StringComparison.OrdinalIgnoreCase)))
                    {
                        DeleteCustomDevice(c.DeviceId);
                    }
                }
            }
            catch { }

            try
            {
                using (RegistryKey root = Registry.CurrentUser.CreateSubKey(@"Software\RazerBatteryTray"))
                {
                    if (root != null)
                    {
                        object userHidden = root.GetValue("CorsairUserHidden");
                        bool allowUnhide = (userHidden == null) || (Convert.ToInt32(userHidden) == 0);
                        if (allowUnhide)
                        {
                            UnhideDevice("Corsair:1B1C:0A97");
                            UnhideDevice("Corsair:1B1C:0A96");
                            UnhideDevice("Corsair:1B1C:1BF2");
                        }
                    }
                }
            }
            catch { }
        }

        private static bool IsRapooOfficeConfig(CustomDeviceConfig cfg)
        {
            if (cfg == null) return false;
            if (!string.IsNullOrEmpty(cfg.DeviceId) && cfg.DeviceId.IndexOf("24AE:1870", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            if (!string.IsNullOrEmpty(cfg.HardwareFingerprint) && cfg.HardwareFingerprint.IndexOf("24AE:1870", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            return false;
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
                InvalidateCustomDevicesCache();
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
            catch { }
        }

        public static void MergeExternalDriverDevice(List<MouseBatteryInfo> list, MouseBatteryInfo dev)
        {
            if (list == null || dev == null) return;

            var variants = GetDeviceIdVariants(dev.DeviceId);
            int devVid, devPid;
            bool hasVidPid = HardwareDriverBase.TryExtractDeviceVidPid(dev, out devVid, out devPid);
            IHardwareDriver owner = HardwareDriverRegistry.ResolveDriver(dev);
            bool strictOwner = HardwareDriverRegistry.RequiresStrictIdentity(owner);

            int existingIdx = -1;
            for (int i = 0; i < list.Count; i++)
            {
                MouseBatteryInfo cur = list[i];
                if (cur == null) continue;

                bool sameUnit = !string.IsNullOrEmpty(cur.PhysicalUnitId) &&
                    string.Equals(cur.PhysicalUnitId, dev.PhysicalUnitId, StringComparison.OrdinalIgnoreCase);
                bool samePath = !string.IsNullOrEmpty(cur.HardwareId) &&
                    string.Equals(cur.HardwareId, dev.HardwareId, StringComparison.OrdinalIgnoreCase);
                if (!sameUnit && !samePath && strictOwner && !owner.CanHandle(cur)) continue;
                if (!sameUnit && !string.IsNullOrEmpty(cur.HardwareId) && !string.IsNullOrEmpty(dev.HardwareId) &&
                    !string.Equals(cur.HardwareId, dev.HardwareId, StringComparison.OrdinalIgnoreCase)) continue;
                if (!string.IsNullOrEmpty(cur.PhysicalUnitId) && !string.IsNullOrEmpty(dev.PhysicalUnitId) && !sameUnit) continue;
                if (sameUnit) { existingIdx = i; break; }

                if (!string.IsNullOrEmpty(cur.DeviceId) && cur.DeviceId.Equals(dev.DeviceId, StringComparison.OrdinalIgnoreCase))
                {
                    existingIdx = i;
                    break;
                }
                if (!string.IsNullOrEmpty(cur.HardwareFingerprint) && !string.IsNullOrEmpty(dev.HardwareFingerprint) &&
                    cur.HardwareFingerprint.Equals(dev.HardwareFingerprint, StringComparison.OrdinalIgnoreCase))
                {
                    existingIdx = i;
                    break;
                }

                for (int vi = 0; vi < variants.Count; vi++)
                {
                    if ((!string.IsNullOrEmpty(cur.DeviceId) && cur.DeviceId.Equals(variants[vi], StringComparison.OrdinalIgnoreCase)) ||
                        (!string.IsNullOrEmpty(cur.HardwareFingerprint) && cur.HardwareFingerprint.Equals(variants[vi], StringComparison.OrdinalIgnoreCase)))
                    {
                        existingIdx = i;
                        break;
                    }
                }
                if (existingIdx >= 0) break;

                if (hasVidPid)
                {
                    int curVid, curPid;
                    if (HardwareDriverBase.TryExtractDeviceVidPid(cur, out curVid, out curPid))
                    {
                        if (curVid == devVid && curPid == devPid && (!strictOwner || owner.CanHandle(cur)) &&
                            (cur.Category == dev.Category || cur.Category == DeviceCategory.Generic || dev.Category == DeviceCategory.Generic))
                        {
                            existingIdx = i;
                            break;
                        }
                    }
                }
            }

            if (existingIdx >= 0)
            {
                MouseBatteryInfo oldDev = list[existingIdx];
                bool samePhysical = (!string.IsNullOrEmpty(oldDev.PhysicalUnitId) &&
                    string.Equals(oldDev.PhysicalUnitId, dev.PhysicalUnitId, StringComparison.OrdinalIgnoreCase)) ||
                    (!string.IsNullOrEmpty(oldDev.HardwareId) && string.Equals(oldDev.HardwareId, dev.HardwareId, StringComparison.OrdinalIgnoreCase));
                if (samePhysical)
                {
                    bool oldLive = !oldDev.IsSleeping && (oldDev.IsConnected || oldDev.IsDonglePresent);
                    bool newLive = !dev.IsSleeping && (dev.IsConnected || dev.IsDonglePresent);
                    if (oldLive && !newLive) return;
                    if (oldLive && newLive && !oldDev.IsDonglePresent && dev.IsDonglePresent) return;
                    if (oldLive == newLive && oldDev.IsDonglePresent == dev.IsDonglePresent && oldDev.LastUpdated > dev.LastUpdated) return;
                }
                dev.IsCustom = oldDev.IsCustom;
                if (!string.IsNullOrEmpty(oldDev.CustomName)) dev.CustomName = oldDev.CustomName;
                list[existingIdx] = dev;
            }
            else
            {
                dev.IsCustom = false;
                list.Add(dev);
            }
        }

        private static void AddDiscoveredDevicesIfAbsent(List<MouseBatteryInfo> list, List<MouseBatteryInfo> devices)
        {
            if (list == null || devices == null || devices.Count == 0) return;
            for (int di = 0; di < devices.Count; di++)
            {
                AddDiscoveredDeviceIfAbsent(list, devices[di]);
            }
        }

        private static void AddDiscoveredDeviceIfAbsent(List<MouseBatteryInfo> list, MouseBatteryInfo dev)
        {
            if (list == null || dev == null || (!dev.IsConnected && !dev.IsDonglePresent)) return;
            for (int i = 0; i < list.Count; i++)
            {
                if (string.Equals(list[i].DeviceId, dev.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                    (!string.IsNullOrEmpty(list[i].HardwareFingerprint) &&
                     string.Equals(list[i].HardwareFingerprint, dev.HardwareFingerprint, StringComparison.OrdinalIgnoreCase)))
                {
                    return;
                }
            }
            dev.IsCustom = false;
            list.Add(dev);
            if (dev.BatteryPercent >= 0)
            {
                UpdateCustomDeviceBattery(dev.DeviceId, dev.BatteryPercent, DateTime.Now);
            }
        }

        private static void CleanupMatchedLegacyCustomDevices(List<MouseBatteryInfo> nativeList)
        {
            if (nativeList == null || nativeList.Count == 0) return;
            try
            {
                HashSet<string> activeKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                for (int i = 0; i < nativeList.Count; i++)
                {
                    var d = nativeList[i];
                    if (d == null) continue;
                    if (!string.IsNullOrEmpty(d.DeviceId))
                    {
                        activeKeys.Add(d.DeviceId);
                        HardwareDiagnosticService.AddVidPidKeysToSet(activeKeys, d.DeviceId);
                        var vars = GetDeviceIdVariants(d.DeviceId);
                        for (int vi = 0; vi < vars.Count; vi++) activeKeys.Add(vars[vi]);
                    }
                    if (!string.IsNullOrEmpty(d.HardwareFingerprint))
                    {
                        activeKeys.Add(d.HardwareFingerprint);
                        HardwareDiagnosticService.AddVidPidKeysToSet(activeKeys, d.HardwareFingerprint);
                    }
                }

                var customConfigs = LoadCustomDevices();
                foreach (var c in customConfigs)
                {
                    if (c == null || string.IsNullOrEmpty(c.DeviceId)) continue;
                    if (!c.DeviceId.StartsWith("HID:", StringComparison.OrdinalIgnoreCase)) continue;

                    bool matchedActive = activeKeys.Contains(c.DeviceId);
                    if (!matchedActive && !string.IsNullOrEmpty(c.HardwareFingerprint))
                    {
                        matchedActive = activeKeys.Contains(c.HardwareFingerprint);
                    }
                    if (!matchedActive)
                    {
                        HashSet<string> cfgVp = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        HardwareDiagnosticService.AddVidPidKeysToSet(cfgVp, c.DeviceId);
                        if (!string.IsNullOrEmpty(c.HardwareFingerprint)) HardwareDiagnosticService.AddVidPidKeysToSet(cfgVp, c.HardwareFingerprint);
                        foreach (string vp in cfgVp)
                        {
                            if (activeKeys.Contains(vp)) { matchedActive = true; break; }
                        }
                    }

                    if (matchedActive &&
                        (c.DeviceId.StartsWith("HID:046D:", StringComparison.OrdinalIgnoreCase) ||
                         c.DeviceId.StartsWith("HID:24AE:", StringComparison.OrdinalIgnoreCase) ||
                         c.DeviceId.StartsWith("HID:374A:", StringComparison.OrdinalIgnoreCase) ||
                         c.DeviceId.StartsWith("HID:347A:", StringComparison.OrdinalIgnoreCase) ||
                         c.DeviceId.StartsWith("HID:3837:", StringComparison.OrdinalIgnoreCase) ||
                         c.DeviceId.StartsWith("HID:1038:", StringComparison.OrdinalIgnoreCase) ||
                         (!HyperXCloud3SHelper.IsCloud3SIdentifier(c.DeviceId) && c.DeviceId.StartsWith("HID:03F0:", StringComparison.OrdinalIgnoreCase))))
                    {
                        DeleteCustomDevice(c.DeviceId);
                    }
                }
            }
            catch { }
        }

        public static List<MouseBatteryInfo> QueryAllDevices()
        {
            // [M1 扫描合流] 抢不到在途令牌说明已有全量扫描在跑：只登记"需要补跑"，
            // 立刻返回最近一次完成扫描的快照，绝不再往 ScanSyncRoot 上堆第二个扫描。
            if (Interlocked.CompareExchange(ref scanInFlight, 1, 0) != 0)
            {
                Interlocked.Exchange(ref pendingRescan, 1);
                // 冷启动首个扫描期间尚无任何快照：有界等待首个快照就绪，避免 UI 先被空列表刷白。
                // 这不是"等锁"——令牌已由他人持有，本次调用不会再发起扫描；超时后照常返回空快照。
                if (!HasScanSnapshot()) { try { scanSnapshotReady.WaitOne(ColdStartSnapshotWaitMs); } catch { } }
                return GetLastScanSnapshot();
            }

            List<MouseBatteryInfo> result = null;
            try
            {
                bool rescanned = false;
                while (true)
                {
                    Interlocked.Exchange(ref pendingRescan, 0);
                    // [P1] 本代次扫描期内的 HID 枚举快照只对扫描线程生效；扫描一结束立刻作废，
                    // 写路径与 UI 线程永远走实时枚举。
                    HidTransport.BeginScanGeneration();
                    try
                    {
                        // A complete scan owns its script claim snapshot through built-in enumeration.
                        lock (HardwareDriverRegistry.ScanSyncRoot) { result = QueryAllDevicesCore(); }
                    }
                    finally
                    {
                        HidTransport.EndScanGeneration();
                    }
                    StoreLastScanSnapshot(result);
                    if (Interlocked.CompareExchange(ref pendingRescan, 0, 0) != 1) break;
                    if (rescanned) break; // 只补跑一次，避免持续触发下扫描永不停歇
                    rescanned = true;
                }
                return result;
            }
            finally
            {
                Interlocked.Exchange(ref pendingRescan, 0);
                Interlocked.Exchange(ref scanInFlight, 0);
            }
        }

        private static bool HasScanSnapshot()
        {
            lock (scanSnapshotLock) { return lastScanSnapshot != null; }
        }

        private static List<MouseBatteryInfo> GetLastScanSnapshot()
        {
            lock (scanSnapshotLock)
            {
                if (lastScanSnapshot == null) return new List<MouseBatteryInfo>();
                return new List<MouseBatteryInfo>(lastScanSnapshot);
            }
        }

        private static void StoreLastScanSnapshot(List<MouseBatteryInfo> devices)
        {
            try
            {
                lock (scanSnapshotLock) { lastScanSnapshot = devices == null ? null : new List<MouseBatteryInfo>(devices); }
                scanSnapshotReady.Set();
            }
            catch { }
        }

        /// <summary>
        /// 测试探针：实际进入扫描核心的次数（合流后应为 1~2 次，与入口调用次数无关）。
        /// </summary>
        internal static int CoreScanInvocationCount
        {
            get { return Interlocked.CompareExchange(ref coreScanInvocationCount, 0, 0); }
        }

        internal static void ResetScanCoalescingProbe()
        {
            Interlocked.Exchange(ref coreScanInvocationCount, 0);
        }

        private static List<MouseBatteryInfo> QueryAllDevicesCore()
        {
            Interlocked.Increment(ref coreScanInvocationCount);
            // [M2 扫描遥测] 分段计时基线：扫描结束由 ScanTelemetry.Complete 汇总一行写入 scan_trace.log
            ScanTimer scanTimer = ScanTelemetry.Begin();
            // [P2] 一次全量扫描只读一次自定义设备注册表；扫描开头的作废同时兜住"绕过写入口的外部改动"
            InvalidateCustomDevicesCache();
            // [P3] 隐藏集合同理（AcerDeviceHelper 等会直接写 HiddenDevices 键）
            InvalidateHiddenDevicesCache();
            scanTimer.TimeAction("EnsureRegistry", BuiltInDrivers.EnsureRegistered);
            scanTimer.Time("ScriptDriverLoader", CsScriptDriverLoader.LoadAndRegisterScriptDrivers);
            List<MouseBatteryInfo> scriptDevices = scanTimer.Time("ExternalScripts", HardwareDriverRegistry.QueryExternalDevices);
            CleanupDuplicateNuphyDevices();
            CleanupDuplicateRazerDevices();
            CleanupDuplicateCorsairDevices();
            CleanupPhantomAppleDevices();
            CleanupDuplicateBluetoothAudioDevices();

            List<MouseBatteryInfo> list = new List<MouseBatteryInfo>();

            // 1. Query Razer
            try
            {
                var razerList = scanTimer.Time("Razer", RazerDeviceHelper.QueryAllRazerDevices);
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

            // Nova 2 Lite: native device-information query, wired/wireless deduplication.
            try { list.AddRange(scanTimer.Time("GameSir", GameSirDeviceHelper.QueryDevices)); } catch { }

            // GS3108T-PRO: identify by product string AND descriptor, never Apple VID alone.
            try
            {
                var ganss = scanTimer.Time("Ganss", GanssDeviceHelper.QueryDevice);
                if (ganss != null) list.Add(ganss);
            }
            catch { }

            // 公版 CompX 2.4G 无线键盘 (05AC:024F "2.4G Wireless Receiver", 33B 0xFF60/0x61 电量端点)
            try
            {
                var compX = scanTimer.Time("CompxKeyboard", CompxWirelessKeyboardHelper.QueryDevice);
                if (compX != null && (compX.IsConnected || compX.IsDonglePresent))
                {
                    compX.IsCustom = false;
                    list.Add(compX);
                }
            }
            catch { }

            // 2. Query Rapoo
            try
            {
                var rapoo = scanTimer.Time("Rapoo", RapooDeviceHelper.QueryRapooDeviceInfo);
                if (rapoo != null && (rapoo.IsConnected || rapoo.IsDonglePresent))
                {
                    rapoo.Category = DeviceCategory.Mouse;
                    rapoo.IsCustom = false;
                    list.Add(rapoo);
                }
            }
            catch { }

            // 2.05 Query Rapoo MT760 office series (24AE:1870, FF00:000E 32-byte vendor collection)
            try
            {
                var rapooOffice = scanTimer.Time("RapooMT760", RapooDeviceHelper.QueryRapooOfficeDeviceInfo);
                if (rapooOffice != null && (rapooOffice.IsConnected || rapooOffice.IsDonglePresent))
                {
                    rapooOffice.Category = DeviceCategory.Mouse;
                    rapooOffice.IsCustom = false;
                    list.Add(rapooOffice);
                    if (rapooOffice.BatteryPercent >= 0)
                    {
                        UpdateCustomDeviceBattery(rapooOffice.DeviceId, rapooOffice.BatteryPercent, DateTime.Now);
                    }
                }
            }
            catch { }

            // 2.1 Query Rapoo VT-series 2.4G receiver 24AE:1851 (0xBA protocol, no feature endpoint)
            try
            {
                var rapooVt = scanTimer.Time("RapooVT", RapooVt1851DeviceHelper.QueryDevice);
                if (rapooVt != null)
                {
                    rapooVt.Category = DeviceCategory.Mouse;
                    rapooVt.IsCustom = false;
                    list.Add(rapooVt);
                }
            }
            catch { }

            // 2.45 Query VGN (2.4G wireless keyboard receiver, VID 3151 / PID 5038)
            try
            {
                var royuan = scanTimer.Time("VgnKeyboard", VgnKeyboardHelper.QueryVgnKeyboardInfo);
                if (royuan != null && (royuan.IsConnected || royuan.IsDonglePresent))
                {
                    royuan.IsCustom = false;
                    list.Add(royuan);
                    if (royuan.BatteryPercent >= 0)
                    {
                        UpdateCustomDeviceBattery(royuan.DeviceId, royuan.BatteryPercent, DateTime.Now);
                    }
                }
            }
            catch { }

            // 2.46 Query VGN V108 (2.4G wireless keyboard receiver, VID 391D / PID 2A02)
            try
            {
                var v108 = scanTimer.Time("VgnV108", VgnKeyboardHelper.QueryVgnV108Info);
                if (v108 != null && (v108.IsConnected || v108.IsDonglePresent))
                {
                    v108.IsCustom = false;
                    list.Add(v108);
                    if (v108.BatteryPercent >= 0)
                    {
                        UpdateCustomDeviceBattery(v108.DeviceId, v108.BatteryPercent, DateTime.Now);
                    }
                }
            }
            catch { }

            // 2.5 Query VGN
            try
            {
                var vgn = scanTimer.Time("Vgn", VgnDeviceHelper.QueryVgnDeviceInfo);
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
                var nuphy = scanTimer.Time("NuPhy", NuphyDeviceHelper.QueryNuphyDeviceInfo);
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



            // 2.69 Query Logitech MR0097 (PRO X SUPERLIGHT 2, Built-in Dedicated Driver)
            try
            {
                var mr0097Devices = scanTimer.Time("LogitechMR0097", LogitechMr0097Helper.QueryDevices);
                if (mr0097Devices != null && mr0097Devices.Count > 0)
                {
                    foreach (var dev in mr0097Devices)
                    {
                        if (dev != null && (dev.IsConnected || dev.IsDonglePresent))
                        {
                            HardwareDriverRegistry.EnsureCapabilitiesInitialized(dev);
                            MergeExternalDriverDevice(list, dev);
                            if (dev.BatteryPercent >= 0 && !dev.IsSleeping)
                            {
                                UpdateCustomDeviceBattery(dev.DeviceId, dev.BatteryPercent, dev.LastUpdated);
                            }
                        }
                    }
                }
            }
            catch { }

            // 2.7 Query Logitech (Lightspeed, Unifying, Nano, Wired)
            try
            {
                var logiDevices = scanTimer.Time("Logitech", LogitechDeviceHelper.QueryAllLogitechDevices);
                if (logiDevices != null && logiDevices.Count > 0)
                {
                    foreach (var dev in logiDevices)
                    {
                        if (dev != null && (dev.IsConnected || dev.IsDonglePresent))
                        {
                            if (list.Exists(x => string.Equals(x.DriverId, LogitechMr0097Helper.DriverKey, StringComparison.OrdinalIgnoreCase) &&
                                string.Equals(x.DeviceId, dev.DeviceId, StringComparison.OrdinalIgnoreCase)))
                            {
                                continue;
                            }
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
                var vkDevices = scanTimer.Time("Valkyrie", ValkyrieDeviceHelper.QueryAllValkyrieDevices);
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

            // 2.88 Query VXE Dragonfly R1 NearLink (373B:1216 BITMOUSE 64B, by 冻干咸鱼)
            try
            {
                var vxeNearLinkDevs = scanTimer.Time("VxeNearLink", VxeNearLinkDeviceHelper.QueryAllDevices);
                if (vxeNearLinkDevs != null && vxeNearLinkDevs.Count > 0)
                {
                    foreach (var dev in vxeNearLinkDevs)
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
            catch { }

            // 2.9 Query Universal Gaming Devices (OpenMouse & All-in-One: Lamzu, Vaxee, WLmouse, Pulsar, ATK/VXE, Attack Shark, etc.)
            try
            {
                var gamingDevices = scanTimer.Time("UniversalGaming", UniversalGamingDeviceHelper.QueryAllGamingDevices);
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
            catch { }

            // 2.92 Query Endgame Gear (XM2we / OP1we 2.4G 双端点 Beken 引擎)
            try
            {
                var eggDevices = scanTimer.Time("EndgameGear", EndgameGearDeviceHelper.QueryAllEndgameGearDevices);
                if (eggDevices != null && eggDevices.Count > 0)
                {
                    foreach (var dev in eggDevices)
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
            catch { }

            // 2.95 Query Flydigi (APEX 6 / APEX 6 Pro / Vader 5 Pro gamepads)
            try
            {
                var flydigiDevices = scanTimer.Time("Flydigi", FlydigiDeviceHelper.QueryAllFlydigiDevices);
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
            catch { }

            // 2.96 Query MCHOSE (G87 V2 tri-mode mechanical keyboard)
            try
            {
                var mchoseDevices = scanTimer.Time("Mchose", MchoseDeviceHelper.QueryAllMchoseDevices);
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
            catch { }

            // 2.965 Query MCHOSE Mouse (A5 V2 Ultra tri-mode gaming mouse)
            try
            {
                var mchoseMice = scanTimer.Time("MchoseMouse", MchoseMouseHelper.QueryAllMchoseMice);
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
            catch { }

            // 2.965 Query KZZI (K75 tri-mode keyboard, CompX 3554:FA09 shared PID)
            try
            {
                var kzzi = scanTimer.Time("Kzzi", KzziDeviceHelper.QueryKzziDevice);
                if (kzzi != null && kzzi.IsDonglePresent)
                {
                    bool alreadyInList = false;
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i].DeviceId.Equals(kzzi.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrEmpty(list[i].HardwareFingerprint) && list[i].HardwareFingerprint.Equals(kzzi.HardwareFingerprint, StringComparison.OrdinalIgnoreCase)))
                        {
                            alreadyInList = true;
                            break;
                        }
                    }
                    if (!alreadyInList)
                    {
                        kzzi.IsCustom = false;
                        list.Add(kzzi);
                    }
                }
            }
            catch { }

            // 2.965b Query BLE peripherals (Xbox controller / Bluetooth keyboard / mouse)
            try
            {
                var bleDevices = scanTimer.Time("Ble", BleDeviceHelper.QueryAllBlePeripherals);
                if (bleDevices != null && bleDevices.Count > 0)
                {
                    foreach (var dev in bleDevices)
                    {
                        if (dev != null && dev.IsConnected)
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
            catch { }

            // 2.966 Query Ironcat Zero wireless mouse (25A7:FA7C 2.4G)
            try
            {
                var ironcatDevices = scanTimer.Time("Ironcat", IroncatDeviceHelper.QueryAllIroncatDevices);
                if (ironcatDevices != null && ironcatDevices.Count > 0)
                {
                    foreach (var dev in ironcatDevices)
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
            catch { }

            // 2.967 Query MCHOSE G series mouse (G7 2.4G receiver, A8A5:2255)
            try
            {
                var mchoseGMice = scanTimer.Time("MchoseG", MchoseGMouseHelper.QueryAllMchoseGMice);
                if (mchoseGMice != null && mchoseGMice.Count > 0)
                {
                    foreach (var dev in mchoseGMice)
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
            catch { }

            // 2.968 Query MOONDROP headset (GM-01 PRO, 35D8:0133)
            try
            {
                var moondropDevices = scanTimer.Time("Moondrop", MoondropDeviceHelper.QueryAllMoondropDevices);
                if (moondropDevices != null && moondropDevices.Count > 0)
                {
                    foreach (var dev in moondropDevices)
                    {
                        if (dev == null || !dev.IsConnected) continue;

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
                        }
                    }
                }
            }
            catch { }

            // 2.97 Query Sony (INZONE Buds WF-G700N wireless earbuds)
            try
            {
                var sonyDevices = scanTimer.Time("SonyInzone", SonyInzoneHelper.QueryAllSonyInzoneDevices);
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
            catch { }

            // 2.98 Query Edifier (Halo Space 2.4G wireless headset, EC private protocol)
            try
            {
                var edifierDevices = scanTimer.Time("Edifier", EdifierDeviceHelper.QueryAllEdifierDevices);
                if (edifierDevices != null && edifierDevices.Count > 0)
                {
                    foreach (var dev in edifierDevices)
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
            catch { }

            // 2.995 Query VGN N75 PRO (320F:5088 2.4G, 产品串隔离)
            try
            {
                var vgnN75 = scanTimer.Time("VgnN75", VgnN75ProHelper.QueryDevices);
                if (vgnN75 != null && vgnN75.Count > 0)
                {
                    foreach (var dev in vgnN75)
                    {
                        if (dev == null || !(dev.IsConnected || dev.IsDonglePresent)) continue;
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
            catch { }

            // 2.996 Query LEOBOG GM5 PRO (04F3:026F 2.4G, 0x000A/0x0000 32 字节状态帧)
            try
            {
                var leobogDevices = scanTimer.Time("Leobog", LeobogDeviceHelper.QueryDevices);
                if (leobogDevices != null && leobogDevices.Count > 0)
                {
                    foreach (var dev in leobogDevices)
                    {
                        if (dev == null || !(dev.IsConnected || dev.IsDonglePresent)) continue;
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
            catch { }

            // 2.99 Query ATK (V75X / VGN V98 PRO) & Sony (DualSense Edge / PULSE Explore)
            try
            {
                var atkDevices = scanTimer.Time("Atk", AtkDeviceHelper.QueryAllAtkDevices);
                if (atkDevices != null && atkDevices.Count > 0)
                {
                    foreach (var dev in atkDevices)
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
            catch { }

            // 2.995 Query ATK A87 PRO 键盘 / 迈从 V9 Turbo+ 耳机 / 北通 BTP-KP20D 手柄 (PR #93, @gallopcsy)
            try { AddDiscoveredDevicesIfAbsent(list, scanTimer.Time("AtkA87Pro", AtkA87ProHelper.QueryAllA87ProDevices)); } catch { }
            try { AddDiscoveredDevicesIfAbsent(list, scanTimer.Time("MchoseHeadset", MchoseHeadsetHelper.QueryAllMchoseHeadsetDevices)); } catch { }
            try { AddDiscoveredDevicesIfAbsent(list, scanTimer.Time("Beitong", BeitongDeviceHelper.QueryAllBeitongDevices)); } catch { }

            try
            {
                var sonyDevices = scanTimer.Time("Sony", SonyDeviceHelper.QueryAllSonyDevices);
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
            catch { }

            // 2.10 Query Apple & Beats Devices (AirPods, Beats Studio/Fit/Solo, Magic Mouse/Trackpad/Keyboard)
            try
            {
                var appleDevices = scanTimer.Time("Apple", AppleDeviceHelper.QueryAllAppleDevices);
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
            catch { }

            // 2.11 Query HyperX Devices (Cloud III Wireless, etc.)
            try
            {
                var hp = scanTimer.Time("HyperX", HyperXDeviceHelper.QueryHyperXDeviceInfo);
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
            catch { }

            // 2.11.1 Query HyperX Cloud III S Wireless (03F0:02CC / 06BE, 64B 0x01C0 电量集合)
            try
            {
                scanTimer.TimeAction("HyperXCloud3SCache", HyperXCloud3SHelper.LoadHardwareCache);
                scanTimer.TimeAction("HyperXCloud3S", delegate { AddDiscoveredDeviceIfAbsent(list, HyperXCloud3SHelper.QueryDevice()); });
            }
            catch { }

                        // 2.12 Query SteelSeries Devices (Arctis Nova 5 / 5X Wireless Headset)
            try
            {
                var ss = scanTimer.Time("SteelSeries", SteelSeriesDeviceHelper.QuerySteelSeriesDeviceInfo);
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
            catch { }

            // 2.12b Query Corsair HS80 MAX Wireless headset (1B1C:0A97, Bragi HID control collection)
            try
            {
                var corsair = scanTimer.Time("Corsair", CorsairDeviceHelper.QueryCorsairDeviceInfo);
                if (corsair != null && (corsair.IsConnected || corsair.IsDonglePresent))
                {
                    bool alreadyInList = false;
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i].DeviceId.Equals(corsair.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrEmpty(list[i].HardwareFingerprint) && list[i].HardwareFingerprint.Equals(corsair.HardwareFingerprint, StringComparison.OrdinalIgnoreCase)))
                        {
                            alreadyInList = true;
                            break;
                        }
                    }

                    if (!alreadyInList)
                    {
                        corsair.IsCustom = false;
                        list.Add(corsair);
                        if (corsair.BatteryPercent >= 0)
                        {
                            UpdateCustomDeviceBattery(corsair.DeviceId, corsair.BatteryPercent, DateTime.Now);
                        }
                    }
                }
            }
            catch { }

            // 2.12c Query Corsair M75 AIR Wireless mouse (SLIPSTREAM 1B1C:1BDC / child PID 1BF2, Bragi 65B)
            try
            {
                var corsairMouse = scanTimer.Time("CorsairMouse", CorsairDeviceHelper.QueryCorsairMouseInfo);
                if (corsairMouse != null && (corsairMouse.IsConnected || corsairMouse.IsDonglePresent))
                {
                    bool alreadyInList = false;
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (list[i].DeviceId.Equals(corsairMouse.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrEmpty(list[i].HardwareFingerprint) && list[i].HardwareFingerprint.Equals(corsairMouse.HardwareFingerprint, StringComparison.OrdinalIgnoreCase)))
                        {
                            alreadyInList = true;
                            break;
                        }
                    }

                    if (!alreadyInList)
                    {
                        corsairMouse.IsCustom = false;
                        list.Add(corsairMouse);
                        if (corsairMouse.BatteryPercent >= 0)
                        {
                            UpdateCustomDeviceBattery(corsairMouse.DeviceId, corsairMouse.BatteryPercent, DateTime.Now);
                        }
                    }
                }
            }
            catch { }

            // 2.13 Query Razer wireless headsets (BlackShark V2 Pro 2.4, 64B PA/PI protocol)
            try
            {
                // 保留显式调用写法：test_brand_razer.ps1 的源码契约断言要求 DeviceManager 中出现 RazerHeadsetHelper.QueryAllRazerHeadsets()
            var razerHeadsets = scanTimer.Time("RazerHeadset", delegate { return RazerHeadsetHelper.QueryAllRazerHeadsets(); });
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
            catch { }


            // 2.13b Query Acer OMR Series Mice (OMR331 Flagship 2.4G / Wired)
            try
            {
                var acerDevices = scanTimer.Time("Acer", AcerDeviceHelper.QueryAllAcerDevices);
                if (acerDevices != null && acerDevices.Count > 0)
                {
                    foreach (var dev in acerDevices)
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
            catch { }

            // 2.13c Query SinoWealth (赛诺韦尔 / Valkyrie 贴牌) 2.4G 鼠标 (VID 258A / PID 010C)
            try
            {
                var swDevices = scanTimer.Time("SinoWealth", SinoWealthDeviceHelper.QueryAllSinoWealthDevices);
                if (swDevices != null && swDevices.Count > 0)
                {
                    foreach (var dev in swDevices)
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
            catch { }

            // 2.13d Query ASUS Devices (PELTA, Scope II RX, Azoth Omni Keyboards)
            try
            {
                var asusDevices = scanTimer.Time("Asus", AsusDeviceHelper.QueryAllAsusDevices);
                if (asusDevices != null && asusDevices.Count > 0)
                {
                    foreach (var dev in asusDevices)
                    {
                        if (dev != null && (dev.IsConnected || dev.IsDonglePresent))
                        {
                            if (!list.Exists(x => string.Equals(x.HardwareFingerprint, dev.HardwareFingerprint, StringComparison.OrdinalIgnoreCase)))
                                list.Add(dev);
                        }
                    }
                }

                var asusKbs = scanTimer.Time("AsusKeyboard", AsusDeviceHelper.QueryAllKeyboards);
                if (asusKbs != null && asusKbs.Count > 0)
                {
                    foreach (var dev in asusKbs)
                    {
                        if (dev != null && (dev.IsConnected || dev.IsDonglePresent))
                        {
                            if (!list.Exists(x => string.Equals(x.DeviceId, dev.DeviceId, StringComparison.OrdinalIgnoreCase)))
                                list.Add(dev);
                        }
                    }
                }

                // 2.13d1 ROG DELTA II 无线耳机 (0B05:1AFA)
                var rogDevices = scanTimer.Time("AsusRog", AsusDeviceHelper.QueryAllRogDevices);
                if (rogDevices != null && rogDevices.Count > 0)
                {
                    foreach (var dev in rogDevices)
                    {
                        if (dev != null && (dev.IsConnected || dev.IsDonglePresent))
                        {
                            if (!list.Exists(x => string.Equals(x.HardwareFingerprint, dev.HardwareFingerprint, StringComparison.OrdinalIgnoreCase)))
                                list.Add(dev);
                        }
                    }
                }

                // 2.13d2 ROG AZOTH 96 HE 无线/有线键盘 (0B05:1AD0 / 0B05:1C10)
                var rogKbs = scanTimer.Time("AsusRogKeyboard", AsusDeviceHelper.QueryAllRogKeyboards);
                if (rogKbs != null && rogKbs.Count > 0)
                {
                    foreach (var dev in rogKbs)
                    {
                        if (dev != null && (dev.IsConnected || dev.IsDonglePresent))
                        {
                            if (!list.Exists(x => string.Equals(x.DeviceId, dev.DeviceId, StringComparison.OrdinalIgnoreCase)))
                                list.Add(dev);
                        }
                    }
                }
            }
            catch { }

            // 2.14 Query FDAS Runtime Dynamic Devices (Local adapters/*.fp-adapter.json, Priority 200)
            try
            {
                var fdasDevices = scanTimer.Time("Fdas", FdasRuntimeHelper.QueryAllFdasDevices);
                if (fdasDevices != null && fdasDevices.Count > 0)
                {
                    foreach (var dev in fdasDevices)
                    {
                        if (dev != null && dev.IsConnected)
                        {
                            MergeExternalDriverDevice(list, dev);
                            if (dev.BatteryPercent >= 0)
                            {
                                UpdateCustomDeviceBattery(dev.DeviceId, dev.BatteryPercent, DateTime.Now);
                            }
                        }
                    }
                }
            }
            catch { }

            // 2.14b Query Keychron M6 (Link-KM / USB, Built-in Dedicated Driver)
            try
            {
                var m6Devices = scanTimer.Time("KeychronM6", KeychronM6Helper.QueryDevices);
                if (m6Devices != null && m6Devices.Count > 0)
                {
                    foreach (var dev in m6Devices)
                    {
                        if (dev != null && (dev.IsConnected || dev.IsDonglePresent))
                        {
                            HardwareDriverRegistry.EnsureCapabilitiesInitialized(dev);
                            MergeExternalDriverDevice(list, dev);
                            if (dev.BatteryPercent >= 0 && !dev.IsSleeping)
                            {
                                UpdateCustomDeviceBattery(dev.DeviceId, dev.BatteryPercent, dev.LastUpdated);
                            }
                        }
                    }
                }
            }
            catch { }

            // 2.15 Query C# 5.0 Dynamic Script Drivers (Local adapters/*.fp-driver.cs, Priority 300)
            try
            {
                foreach (MouseBatteryInfo sDev in scriptDevices)
                {
                    MergeExternalDriverDevice(list, sDev);
                    if (sDev.BatteryPercent >= 0 && !sDev.IsSleeping)
                    {
                        UpdateCustomDeviceBattery(sDev.DeviceId, sDev.BatteryPercent, sDev.LastUpdated);
                    }
                }
            }
            catch { }

            CleanupMatchedLegacyCustomDevices(list);

            // 3. Query Custom Devices (Bluetooth / Generic HID)
            try
            {
                var customConfigs = LoadCustomDevices();
                foreach (var cfg in customConfigs)
                {
                    string internalIdentity = (cfg.DeviceId ?? "") + " " + (cfg.HardwareFingerprint ?? "");
                    string internalName = (cfg.OriginalName ?? "") + " " + (cfg.CustomName ?? "");
                    if (GenericHidHelper.IsInternalHidDevice(internalName, internalIdentity))
                    {
                        continue;
                    }

                    bool isLogitech = (cfg.DeviceId != null && (cfg.DeviceId.StartsWith("Logitech:046D:", StringComparison.OrdinalIgnoreCase) || cfg.DeviceId.IndexOf("046D", StringComparison.OrdinalIgnoreCase) >= 0)) ||
                                      (!string.IsNullOrEmpty(cfg.HardwareFingerprint) && cfg.HardwareFingerprint.IndexOf("046D", StringComparison.OrdinalIgnoreCase) >= 0);



                    bool isNuphy = NuphyDeviceHelper.IsNuphyDevice(cfg.HardwareFingerprint) ||
                                   NuphyDeviceHelper.IsNuphyDevice(cfg.DeviceId) ||
                                   NuphyDeviceHelper.IsNuphyDevice(cfg.OriginalName);

                    bool isVgnKeyboard = VgnKeyboardHelper.IsVgnNeon75(cfg.HardwareFingerprint) ||
                                    VgnKeyboardHelper.IsVgnNeon75(cfg.DeviceId) ||
                                    VgnKeyboardHelper.IsVgnNeon75(cfg.OriginalName);

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

                    if (GameSirDeviceHelper.MatchesConfig(cfg) || (cfg.DeviceId ?? "").StartsWith("GameSir:Nova2Lite:", StringComparison.OrdinalIgnoreCase))
                    {
                        var gameSir = GameSirDeviceHelper.FindConfiguredDevice(cfg, list);
                        if (gameSir != null)
                        {
                            gameSir.IsCustom = true;
                            if (!string.IsNullOrEmpty(cfg.CustomName) && cfg.CustomName.IndexOf("XBOX", StringComparison.OrdinalIgnoreCase) < 0)
                                gameSir.CustomName = cfg.CustomName;
                            continue;
                        }
                    }

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
                        if (string.Equals(existDev.DriverId, BleDeviceHelper.DriverIdValue, StringComparison.OrdinalIgnoreCase) &&
                            existDev.BatteryPercent < 0 &&
                            !string.IsNullOrEmpty(cfg.HardwareFingerprint) &&
                            !cfg.HardwareFingerprint.StartsWith("HID:", StringComparison.OrdinalIgnoreCase))
                        {
                            int btBatt;
                            string btName;
                            if (BluetoothDeviceHelper.QueryByMac(cfg.HardwareFingerprint, cfg.OriginalName ?? cfg.CustomName, out btBatt, out btName) && btBatt >= 0)
                            {
                                existDev.BatteryPercent = btBatt;
                                existDev.ExactBatteryPercent = btBatt;
                            }
                        }
                        if (VgnN75ProHelper.IsNativeDevice(existDev) && cfg.Category == DeviceCategory.Dongle)
                        {
                            cfg.Category = DeviceCategory.Keyboard;
                            SaveCustomDevice(cfg);
                        }
                        if (LeobogDeviceHelper.IsNativeDevice(existDev) && cfg.Category == DeviceCategory.Dongle)
                        {
                            cfg.Category = DeviceCategory.Mouse;
                            SaveCustomDevice(cfg);
                        }
                        DeviceCategory mergeTruthCat;
                        bool mergeTruthKnown = MchoseDeviceHelper.TryGetAuthoritativeCategory(cfg.DeviceId, cfg.HardwareFingerprint, out mergeTruthCat);
                        if (mergeTruthKnown && mergeTruthCat != DeviceCategory.Generic)
                        {
                            // 硬件事实优先：驱动已按 VID/PID 正确归类时，
                            // 不允许注册表里的历史错值把品类再改回去。
                            existDev.Category = mergeTruthCat;
                        }
                        else if (cfg.Category != DeviceCategory.Generic)
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
                            bool isOfficeRapoo = IsRapooOfficeConfig(cfg);
                            var rp = isOfficeRapoo ? RapooDeviceHelper.QueryRapooOfficeDeviceInfo() : RapooDeviceHelper.QueryRapooDeviceInfo();
                            if (rp != null && (rp.IsConnected || rp.IsDonglePresent))
                            {
                                connected = rp.IsConnected;
                                batt = rp.BatteryPercent;
                                friendlyName = rp.DeviceName;
                            }
                            else
                            {
                                connected = false;
                                int cachedRapoo = isOfficeRapoo ? RapooDeviceHelper.CachedOfficeBatteryPercent : RapooDeviceHelper.CachedBatteryPercent;
                                batt = cachedRapoo > 0 ? cachedRapoo : -1;
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
                    devInfo.Transport = cfg.Transport ?? (isRazer ? "2.4G 无线" : (isRapoo ? "2.4G 无线" : (isHyperX ? "2.4G 无线" : "Bluetooth")));
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
                        devInfo.ExactBatteryPercent = effectiveBatt >= 0 ? effectiveBatt : 0;
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
                            int cachedRp = IsRapooOfficeConfig(cfg) ? RapooDeviceHelper.CachedOfficeBatteryPercent : RapooDeviceHelper.CachedBatteryPercent;
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
            catch { }

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
            catch { }

            ApplyDeviceAppearances(list);

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
            catch { }

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

            // 3.67 蓝牙耳机/TWS 耳塞同名与同一设备多端点智能去重
            // 分体式 TWS 耳机（如三星 Galaxy Buds、AirPods 等）在 Windows 下常建立音频通道与 BLE 信标多端点，
            // 此处对同一物理耳机的重复卡片进行聚合，优先保留已连接与带有效电量的主卡片，断联时统一显示单一离线卡片。
            try
            {
                var btAudioList = list.FindAll(delegate(MouseBatteryInfo x) {
                    return (x.Category == DeviceCategory.Earbuds || x.Category == DeviceCategory.Headset) &&
                           (x.IsCustom || (x.DeviceId != null && x.DeviceId.StartsWith("BT_", StringComparison.OrdinalIgnoreCase)));
                });
                if (btAudioList.Count > 1)
                {
                    var audioGroups = new Dictionary<string, List<MouseBatteryInfo>>(StringComparer.OrdinalIgnoreCase);
                    foreach (var d in btAudioList)
                    {
                        string groupKey = !string.IsNullOrEmpty(d.DeviceName) ? d.DeviceName.Trim() : (d.CustomName ?? "").Trim();
                        if (string.IsNullOrEmpty(groupKey)) groupKey = d.DeviceId ?? "";
                        if (!audioGroups.ContainsKey(groupKey)) audioGroups[groupKey] = new List<MouseBatteryInfo>();
                        audioGroups[groupKey].Add(d);
                    }

                    foreach (var kvp in audioGroups)
                    {
                        if (kvp.Value.Count > 1)
                        {
                            MouseBatteryInfo best = kvp.Value.Find(delegate(MouseBatteryInfo x) { return x.IsConnected && x.BatteryPercent > 0; }) ??
                                                    kvp.Value.Find(delegate(MouseBatteryInfo x) { return x.IsConnected; }) ??
                                                    kvp.Value.Find(delegate(MouseBatteryInfo x) { return x.BatteryPercent > 0; }) ??
                                                    kvp.Value[0];

                            var withCustomName = kvp.Value.Find(delegate(MouseBatteryInfo x) {
                                return !string.IsNullOrEmpty(x.CustomName) && !x.CustomName.Equals(x.DeviceName, StringComparison.OrdinalIgnoreCase);
                            });
                            if (withCustomName != null && string.IsNullOrEmpty(best.CustomName))
                            {
                                best.CustomName = withCustomName.CustomName;
                            }

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
            }
            catch { }

            // 3.675 三通道键盘在线仲裁（有线/蓝牙有真实响应时将同型号 2.4G 条目置为离线休眠）
            ApplyCrossTransportKeyboardArbitration(list);

            // 3.68 VHF 虚拟 HID 兜底过滤（如网易 UU 远程 uuycinput.sys 创建的虚拟键鼠）
            // 这类设备由远程控制软件通过内核 VHF 总线注册，没有物理电池，
            // 既不是 2.4G 外设也不能监控电量，必须从最终设备列表剔除。
            list.RemoveAll(delegate(MouseBatteryInfo d)
            {
                return VirtualHidFilter.IsVirtualHidIdentity(d.DeviceId, d.HardwareFingerprint, d.HardwareId);
            });

            // 3.7 Filter out user-hidden devices
            list.RemoveAll(d => ShouldHideDevice(d, IsDeviceHidden(d.DeviceId), !string.IsNullOrEmpty(d.HardwareFingerprint) && IsDeviceHidden(d.HardwareFingerprint)));

            // 4. Priority Ordering and Primary Selection
            var prioList = GetPriorityList();
            bool prioChanged = false;

            // Remove only user-hidden devices from prioList; NEVER purge offline devices!
            for (int i = prioList.Count - 1; i >= 0; i--)
            {
                if (IsDeviceHidden(prioList[i]))
                {
                    prioList.RemoveAt(i);
                    prioChanged = true;
                }
            }

            foreach (var d in list)
            {
                if (FindPriorityIndex(prioList, d.DeviceId) < 0)
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
                int ia = FindPriorityIndex(prioList, a.DeviceId);
                int ib = FindPriorityIndex(prioList, b.DeviceId);
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
            // 1. 若开启“低电量优先”，仅当在线设备电量低于 30% 时才触发置顶预警（并在低于 30% 的设备中选取电量最低者；并列时按手动顺位）；
            // 2. 否则（全部设备电量 >= 30% 或无有效读数时），100% 遵循用户手动设置的第 1 顺位在线设备，保持日常托盘图标稳定；
            // 3. 如果全部设备都离线，则回退展示第 1 位设备 (list[0])，并展示离线记忆电量！
            MouseBatteryInfo primary = null;
            if (IsTrayLowBatteryFirstEnabled())
            {
                int lowestBatt = LowBatteryPriorityThreshold; // 仅当设备电量低于 30% 时才触发置顶
                for (int i = 0; i < list.Count; i++)
                {
                    var cand = list[i];
                    if (cand != null && cand.IsConnected && cand.BatteryPercent > 0 && cand.BatteryPercent < lowestBatt)
                    {
                        lowestBatt = cand.BatteryPercent;
                        primary = cand;
                    }
                }
            }
            if (primary == null)
            {
                primary = list.Find(d => d.IsConnected);
            }
            if (primary == null && list.Count > 0)
            {
                primary = list[0];
            }

            foreach (var dev in list)
            {
                dev.IsPrimary = (dev == primary);
                HardwareDriverRegistry.EnsureCapabilitiesInitialized(dev);
            }

            ScanTelemetry.Complete(scanTimer, list.Count);
            return list;
        }

        public class DeviceAppearanceConfig
        {
            public string DeviceId { get; set; }
            public string CustomName { get; set; }
            public string ThemeColorHex { get; set; }
            public string TrayColorHex { get; set; }
            private bool linkTrayColorToTheme = true;
            public bool LinkTrayColorToTheme
            {
                get { return linkTrayColorToTheme; }
                set { linkTrayColorToTheme = value; }
            }
        }

        private const string DeviceAppearanceRegKey = @"Software\FerrisPulse\DeviceAppearance";

        private static string GetCanonicalAppearanceId(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return "";
            string id = deviceId;
            if (id.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase))
            {
                id = RazerDeviceHelper.NormalizeRazerDeviceId(id);
            }
            return id.Replace('\\', '_').Replace('/', '_');
        }

        public static void SaveDeviceAppearance(MouseBatteryInfo dev, string customName, string themeColorHex, string trayColorHex, bool linkTrayColorToTheme)
        {
            if (dev == null || string.IsNullOrEmpty(dev.DeviceId)) return;
            string cleanName = string.IsNullOrEmpty(customName) ? "" : customName.Trim();
            string cleanTheme = string.IsNullOrEmpty(themeColorHex) ? "" : themeColorHex.Trim();
            string cleanTray = linkTrayColorToTheme ? cleanTheme : (string.IsNullOrEmpty(trayColorHex) ? "" : trayColorHex.Trim());

            dev.CustomName = cleanName;
            dev.CustomThemeColorHex = cleanTheme;
            dev.CustomTrayColorHex = cleanTray;
            dev.LinkTrayColorToTheme = linkTrayColorToTheme;

            SaveDeviceAppearance(dev.DeviceId, cleanName, cleanTheme, cleanTray, linkTrayColorToTheme);
        }

        public static void SaveDeviceAppearance(string deviceId, string customName, string themeColorHex, string trayColorHex, bool linkTrayColorToTheme)
        {
            if (string.IsNullOrEmpty(deviceId)) return;
            string subName = GetCanonicalAppearanceId(deviceId);
            if (string.IsNullOrEmpty(subName)) return;

            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(DeviceAppearanceRegKey + @"\" + subName))
                {
                    if (key != null)
                    {
                        key.SetValue("DeviceId", deviceId);
                        key.SetValue("CustomName", customName ?? "");
                        key.SetValue("ThemeColorHex", themeColorHex ?? "");
                        key.SetValue("TrayColorHex", linkTrayColorToTheme ? (themeColorHex ?? "") : (trayColorHex ?? ""));
                        key.SetValue("LinkTrayColorToTheme", linkTrayColorToTheme ? 1 : 0, RegistryValueKind.DWord);
                    }
                }
            }
            catch { }
        }

        public static DeviceAppearanceConfig GetDeviceAppearance(string deviceId, string hardwareFingerprint = null)
        {
            if (string.IsNullOrEmpty(deviceId) && string.IsNullOrEmpty(hardwareFingerprint)) return null;
            var candidates = new List<string>();
            if (!string.IsNullOrEmpty(deviceId))
            {
                candidates.Add(GetCanonicalAppearanceId(deviceId));
                var variants = GetDeviceIdVariants(deviceId);
                for (int i = 0; i < variants.Count; i++) candidates.Add(GetCanonicalAppearanceId(variants[i]));
            }
            if (!string.IsNullOrEmpty(hardwareFingerprint))
            {
                candidates.Add(GetCanonicalAppearanceId(hardwareFingerprint));
            }

            try
            {
                using (var rootKey = Registry.CurrentUser.OpenSubKey(DeviceAppearanceRegKey, false))
                {
                    if (rootKey == null) return null;
                    for (int i = 0; i < candidates.Count; i++)
                    {
                        string subName = candidates[i];
                        if (string.IsNullOrEmpty(subName)) continue;
                        using (var sub = rootKey.OpenSubKey(subName, false))
                        {
                            if (sub != null)
                            {
                                var cfg = new DeviceAppearanceConfig();
                                cfg.DeviceId = sub.GetValue("DeviceId", subName) as string;
                                cfg.CustomName = sub.GetValue("CustomName", "") as string;
                                cfg.ThemeColorHex = sub.GetValue("ThemeColorHex", "") as string;
                                cfg.TrayColorHex = sub.GetValue("TrayColorHex", "") as string;
                                object linkObj = sub.GetValue("LinkTrayColorToTheme", 1);
                                cfg.LinkTrayColorToTheme = Convert.ToInt32(linkObj) != 0;
                                return cfg;
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        public static void ClearDeviceAppearance(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return;
            try
            {
                using (var rootKey = Registry.CurrentUser.OpenSubKey(DeviceAppearanceRegKey, true))
                {
                    if (rootKey != null)
                    {
                        rootKey.DeleteSubKeyTree(GetCanonicalAppearanceId(deviceId), false);
                        var variants = GetDeviceIdVariants(deviceId);
                        for (int i = 0; i < variants.Count; i++)
                        {
                            rootKey.DeleteSubKeyTree(GetCanonicalAppearanceId(variants[i]), false);
                        }
                    }
                }
            }
            catch { }
        }

        public static void ApplyDeviceAppearances(List<MouseBatteryInfo> list)
        {
            if (list == null || list.Count == 0) return;
            try
            {
                for (int i = 0; i < list.Count; i++)
                {
                    var dev = list[i];
                    if (dev == null) continue;
                    var app = GetDeviceAppearance(dev.DeviceId, dev.HardwareFingerprint);
                    if (app == null) continue;
                    if (!string.IsNullOrEmpty(app.CustomName))
                    {
                        dev.CustomName = app.CustomName;
                    }
                    dev.CustomThemeColorHex = app.ThemeColorHex ?? "";
                    dev.CustomTrayColorHex = app.TrayColorHex ?? "";
                    dev.LinkTrayColorToTheme = app.LinkTrayColorToTheme;
                }
            }
            catch { }
        }
    }
}