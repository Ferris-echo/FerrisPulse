// ============================================================================
// 模块名称: HardwareCommandBus.cs
// 所属区域: Hardware (统一硬件指令分发总线层)
// 核心职责:
//   向 UI 层与全屏游戏监控调度层提供完全与品牌解耦的统一硬件控制入口（SetDpi、
//   UpdateHardwareStages、SetPollingRate、FastQueryDpi、SyncCustomActions、ExecuteCustomAction），
//   并内置按物理 VID:PID 粒度的通道互斥锁 (HidChannelLock)，防止实时切档监听与电量查询抢包。
// 依赖关系: IHardwareDriver.cs, HardwareDriverRegistry.cs, DriverStateStore.cs, MouseBatteryInfo.cs
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
    public static class HardwareCommandBus
    {
        private static readonly object lockTableSync = new object();
        private static readonly Dictionary<string, object> deviceLocks = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        public static object GetDeviceLock(int vid, int pid)
        {
            string key = string.Format("{0:X4}:{1:X4}", vid, pid);
            lock (lockTableSync)
            {
                object lk;
                if (!deviceLocks.TryGetValue(key, out lk))
                {
                    lk = new object();
                    deviceLocks[key] = lk;
                }
                return lk;
            }
        }

        public static object GetDeviceLock(MouseBatteryInfo dev)
        {
            int vid, pid;
            if (HardwareDriverBase.TryExtractDeviceVidPid(dev, out vid, out pid))
            {
                return GetDeviceLock(vid, pid);
            }
            string fallback = (dev != null && !string.IsNullOrEmpty(dev.DeviceId)) ? dev.DeviceId : "GLOBAL_DEV";
            lock (lockTableSync)
            {
                object lk;
                if (!deviceLocks.TryGetValue(fallback, out lk))
                {
                    lk = new object();
                    deviceLocks[fallback] = lk;
                }
                return lk;
            }
        }

        public static bool FastQueryDpi(MouseBatteryInfo dev, out int dpiX, out int dpiY, out int stage, out int count)
        {
            dpiX = 0;
            dpiY = 0;
            stage = 0;
            count = 0;
            if (dev == null) return false;

            IHardwareDriver drv = HardwareDriverRegistry.ResolveDriver(dev);
            if (drv == null) return false;

            lock (GetDeviceLock(dev))
            {
                try
                {
                    return drv.FastQueryDpi(dev, out dpiX, out dpiY, out stage, out count);
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool SetDpi(MouseBatteryInfo dev, int dpiX, int dpiY, int stage)
        {
            if (dev == null) return false;
            IHardwareDriver drv = HardwareDriverRegistry.ResolveDriver(dev);
            if (drv == null) return false;

            lock (GetDeviceLock(dev))
            {
                try
                {
                    bool ok = drv.SetDpi(dev, dpiX, dpiY, stage);
                    if (ok)
                    {
                        dev.Dpi = dpiX;
                        dev.DpiY = dpiY > 0 ? dpiY : dpiX;
                        if (stage > 0) dev.DpiStage = stage;
                        DriverStateStore.SaveDpiState(dev.DeviceId, dev.Dpi, dev.DpiY, dev.DpiStage, dev.PollingRate);
                    }
                    return ok;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool UpdateHardwareStages(MouseBatteryInfo dev, int activeStage, int[] stagesX, int[] stagesY)
        {
            if (dev == null || stagesX == null || stagesX.Length == 0) return false;
            IHardwareDriver drv = HardwareDriverRegistry.ResolveDriver(dev);
            if (drv == null) return false;

            lock (GetDeviceLock(dev))
            {
                try
                {
                    bool ok = drv.UpdateHardwareStages(dev, activeStage, stagesX, stagesY);
                    if (ok)
                    {
                        dev.DpiStages = stagesX;
                        dev.DpiStagesY = stagesY;
                        dev.DpiStageCount = stagesX.Length;
                        if (activeStage >= 1 && activeStage <= stagesX.Length)
                        {
                            dev.DpiStage = activeStage;
                            dev.Dpi = stagesX[activeStage - 1];
                            if (stagesY != null && activeStage <= stagesY.Length)
                            {
                                dev.DpiY = stagesY[activeStage - 1];
                            }
                        }
                        DriverStateStore.SaveDpiState(dev.DeviceId, dev.Dpi, dev.DpiY, dev.DpiStage, dev.PollingRate);
                    }
                    return ok;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool SetPollingRate(MouseBatteryInfo dev, int rateHz)
        {
            if (dev == null || rateHz <= 0) return false;
            IHardwareDriver drv = HardwareDriverRegistry.ResolveDriver(dev);
            if (drv == null) return false;

            lock (GetDeviceLock(dev))
            {
                try
                {
                    bool ok = drv.SetPollingRate(dev, rateHz);
                    if (ok)
                    {
                        dev.PollingRate = rateHz;
                        DriverStateStore.SaveDpiState(dev.DeviceId, dev.Dpi, dev.DpiY, dev.DpiStage, dev.PollingRate);
                    }
                    return ok;
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool SyncCustomActions(MouseBatteryInfo dev)
        {
            if (dev == null) return false;
            IHardwareDriver drv = HardwareDriverRegistry.ResolveDriver(dev);
            if (drv == null) return false;

            lock (GetDeviceLock(dev))
            {
                try
                {
                    return drv.SyncCustomActions(dev);
                }
                catch
                {
                    return false;
                }
            }
        }

        public static bool ExecuteCustomAction(MouseBatteryInfo dev, string actionId, int value)
        {
            if (dev == null || string.IsNullOrEmpty(actionId)) return false;
            IHardwareDriver drv = HardwareDriverRegistry.ResolveDriver(dev);
            if (drv == null) return false;

            lock (GetDeviceLock(dev))
            {
                try
                {
                    bool ok = drv.ExecuteCustomAction(dev, actionId, value);
                    if (ok)
                    {
                        CustomDeviceAction act = dev.Capabilities.FindAction(actionId);
                        if (act != null)
                        {
                            act.IntValue = value;
                            act.BoolValue = (value != 0);
                        }
                        if (!string.IsNullOrEmpty(dev.DeviceId))
                        {
                            DriverStateStore.SaveCustomActionValue(dev.DeviceId, actionId, value);
                        }
                    }
                    return ok;
                }
                catch
                {
                    return false;
                }
            }
        }
    }
}
