// ============================================================================
// 模块名称: HardwareDriverRegistry.cs
// 所属区域: Hardware (硬件驱动注册与优先级仲裁层)
// 核心职责:
//   管理全系统所有内置驱动包 (Priority 100)、FDAS v2.0 JSON 扩展包 (Priority 200)
//   与 C# 动态脚本扩展包 (Priority 300) 的注册、卸载、按 VID:PID 优先级热覆盖仲裁、
//   设备能力自动补齐以及 RawInput 唤醒路径动态匹配。
// 依赖关系: IHardwareDriver.cs, HardwareDriverBase.cs, MouseBatteryInfo.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止 $"..." 插值、禁止 ?. 空条件运算符、禁止 nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;

namespace RazerBatteryTray
{
    public static class HardwareDriverRegistry
    {
        private static readonly object syncRoot = new object();
        private static readonly List<IHardwareDriver> drivers = new List<IHardwareDriver>();

        public static void RegisterDriver(IHardwareDriver driver)
        {
            if (driver == null || string.IsNullOrEmpty(driver.DriverId)) return;
            lock (syncRoot)
            {
                for (int i = drivers.Count - 1; i >= 0; i--)
                {
                    if (string.Equals(drivers[i].DriverId, driver.DriverId, StringComparison.OrdinalIgnoreCase))
                    {
                        drivers.RemoveAt(i);
                    }
                }
                drivers.Add(driver);
                drivers.Sort(CompareDriversByPriorityDesc);
            }
        }

        private static int CompareDriversByPriorityDesc(IHardwareDriver a, IHardwareDriver b)
        {
            if (a == null && b == null) return 0;
            if (a == null) return 1;
            if (b == null) return -1;
            int cmp = b.Priority.CompareTo(a.Priority);
            if (cmp != 0) return cmp;
            return string.Compare(a.DriverId, b.DriverId, StringComparison.OrdinalIgnoreCase);
        }

        public static void UnregisterExternalDrivers()
        {
            lock (syncRoot)
            {
                for (int i = drivers.Count - 1; i >= 0; i--)
                {
                    if (drivers[i] != null && drivers[i].IsExternalPackage)
                    {
                        drivers.RemoveAt(i);
                    }
                }
            }
        }

        public static bool UnregisterDriver(string driverId)
        {
            if (string.IsNullOrEmpty(driverId)) return false;
            lock (syncRoot)
            {
                bool removed = false;
                for (int i = drivers.Count - 1; i >= 0; i--)
                {
                    if (drivers[i] != null && string.Equals(drivers[i].DriverId, driverId, StringComparison.OrdinalIgnoreCase))
                    {
                        drivers.RemoveAt(i);
                        removed = true;
                    }
                }
                return removed;
            }
        }

        public static List<IHardwareDriver> GetAllDriversOrdered()
        {
            BuiltInDrivers.EnsureRegistered();
            lock (syncRoot)
            {
                return new List<IHardwareDriver>(drivers);
            }
        }

        public static void ReloadExternalPackages()
        {
            FdasRuntimeHelper.ReloadAdapters();
        }

        public static IHardwareDriver FindDriverForDevice(MouseBatteryInfo dev)
        {
            return ResolveDriver(dev);
        }

        public static IHardwareDriver ResolveDriver(MouseBatteryInfo dev)
        {
            if (dev == null) return null;
            List<IHardwareDriver> snapshot = GetAllDriversOrdered();

            int vid, pid;
            bool hasVidPid = HardwareDriverBase.TryExtractDeviceVidPid(dev, out vid, out pid);

            // 1. 最高优先级仲裁：若外部扩展包或内置驱动精确匹配该设备物理 VID:PID，按 Priority (300 > 200 > 100) 胜出
            if (hasVidPid)
            {
                for (int i = 0; i < snapshot.Count; i++)
                {
                    IHardwareDriver drv = snapshot[i];
                    if (drv != null && drv.MatchesVidPid(vid, pid))
                    {
                        return drv;
                    }
                }
            }

            // 2. 其次按显式绑定的 DriverId 匹配
            if (!string.IsNullOrEmpty(dev.DriverId))
            {
                for (int i = 0; i < snapshot.Count; i++)
                {
                    IHardwareDriver drv = snapshot[i];
                    if (drv != null && string.Equals(drv.DriverId, dev.DriverId, StringComparison.OrdinalIgnoreCase))
                    {
                        return drv;
                    }
                }
            }

            // 3. 兜底按 CanHandle (品牌 / 特征前缀) 匹配
            for (int i = 0; i < snapshot.Count; i++)
            {
                IHardwareDriver drv = snapshot[i];
                if (drv != null && drv.CanHandle(dev))
                {
                    return drv;
                }
            }

            return null;
        }

        public static void EnsureCapabilitiesInitialized(MouseBatteryInfo dev)
        {
            if (dev == null) return;
            IHardwareDriver drv = ResolveDriver(dev);
            if (drv != null && (!FdasRuntimeHelper.IsFdasDevice(dev.DeviceId) || drv.IsExternalPackage))
            {
                drv.PopulateCapabilities(dev);
            }
            else if (FdasRuntimeHelper.IsFdasMouse(dev))
            {
                dev.Capabilities.SupportsDpi = true;
                dev.Capabilities.SupportsDpiStages = true;
                dev.Capabilities.SupportsPollingRate = true;
                if (dev.SupportedPollingRates != null && dev.SupportedPollingRates.Length > 0)
                {
                    dev.Capabilities.SupportedPollingRates = dev.SupportedPollingRates;
                }
                else
                {
                    dev.Capabilities.SupportedPollingRates = new int[] { 125, 250, 500, 1000, 2000, 4000, 8000 };
                }
            }
        }

        public static bool IsKnownMouseHardwarePath(string devPath)
        {
            if (string.IsNullOrEmpty(devPath)) return false;
            string lower = devPath.ToLowerInvariant();

            // 1. 检查注册表中所有已挂载的驱动（含外部 .fp-adapter.json 与 .fp-driver.cs 扩展包）
            int vid, pid;
            if (HardwareDriverBase.TryExtractVidPidFromString(lower, out vid, out pid))
            {
                List<IHardwareDriver> snapshot = GetAllDriversOrdered();
                for (int i = 0; i < snapshot.Count; i++)
                {
                    IHardwareDriver drv = snapshot[i];
                    if (drv != null && drv.MatchesVidPid(vid, pid))
                    {
                        return true;
                    }
                }
            }

            // 2. 兼容内置核心鼠标厂商 VID 快速匹配
            if (lower.Contains("vid_1532") ||
                lower.Contains("vid_046d") ||
                lower.Contains("vid_24ae") ||
                lower.Contains("vid_3554") ||
                lower.Contains("vid_258a") ||
                lower.Contains("vid_3837") ||
                lower.Contains("vid_373b") ||
                lower.Contains("vid_373e") ||
                lower.Contains("vid_37b0") ||
                lower.Contains("vid_3710") ||
                lower.Contains("vid_36a7") ||
                lower.Contains("vid_3367"))
            {
                return true;
            }

            return false;
        }
    }
}
