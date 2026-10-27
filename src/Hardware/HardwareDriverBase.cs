// ============================================================================
// 模块名称: HardwareDriverBase.cs
// 所属区域: Hardware (硬件驱动插件基类层)
// 核心职责:
//   提供 IHardwareDriver 的抽象默认实现与 VID:PID 提取辅助函数，大幅简化内置驱动封装
//   与外部 .fp-driver.cs C# 5.0 动态脚本扩展包的编写。
// 依赖关系: IHardwareDriver.cs, MouseBatteryInfo.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑或属性时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止 $"..." 插值、禁止 ?. 空条件运算符、禁止 nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;

namespace RazerBatteryTray
{
    public abstract class HardwareDriverBase : IHardwareDriver
    {
        public abstract string DriverId { get; }
        public abstract string Brand { get; }

        public virtual int Priority
        {
            get { return IsExternalPackage ? 300 : 100; }
        }

        public virtual bool IsExternalPackage
        {
            get { return false; }
        }

        private string sourceFilePath = "";
        public virtual string SourceFilePath
        {
            get { return sourceFilePath; }
            set { sourceFilePath = value ?? ""; }
        }

        public virtual bool MatchesVidPid(int vid, int pid)
        {
            return false;
        }

        public virtual bool CanHandle(MouseBatteryInfo dev)
        {
            if (dev == null) return false;
            if (!string.IsNullOrEmpty(dev.DriverId) &&
                string.Equals(dev.DriverId, this.DriverId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            int vid, pid;
            if (TryExtractDeviceVidPid(dev, out vid, out pid))
            {
                if (MatchesVidPid(vid, pid)) return true;
            }

            if (!string.IsNullOrEmpty(dev.Brand) && !string.IsNullOrEmpty(this.Brand) &&
                string.Equals(dev.Brand, this.Brand, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        public virtual void PopulateCapabilities(MouseBatteryInfo dev)
        {
            if (dev == null) return;
            if (string.IsNullOrEmpty(dev.DriverId)) dev.DriverId = this.DriverId;
        }

        public virtual List<MouseBatteryInfo> QueryDevices()
        {
            return new List<MouseBatteryInfo>();
        }

        public virtual bool FastQueryDpi(MouseBatteryInfo dev, out int dpiX, out int dpiY, out int stage, out int count)
        {
            dpiX = 0;
            dpiY = 0;
            stage = 0;
            count = 0;
            return false;
        }

        public virtual bool SetDpi(MouseBatteryInfo dev, int dpiX, int dpiY, int stage)
        {
            return false;
        }

        public virtual bool UpdateHardwareStages(MouseBatteryInfo dev, int activeStage, int[] stagesX, int[] stagesY)
        {
            return false;
        }

        public virtual bool SetPollingRate(MouseBatteryInfo dev, int rateHz)
        {
            return false;
        }

        public virtual bool SyncCustomActions(MouseBatteryInfo dev)
        {
            return false;
        }

        public virtual bool ExecuteCustomAction(MouseBatteryInfo dev, string actionId, int value)
        {
            return false;
        }

        public static bool TryExtractDeviceVidPid(MouseBatteryInfo dev, out int vid, out int pid)
        {
            vid = 0;
            pid = 0;
            if (dev == null) return false;
            if (TryExtractVidPidFromString(dev.DeviceId, out vid, out pid)) return true;
            if (TryExtractVidPidFromString(dev.HardwareFingerprint, out vid, out pid)) return true;
            if (TryExtractVidPidFromString(dev.HardwareId, out vid, out pid)) return true;
            return false;
        }

        public static bool TryExtractVidPidFromString(string identifier, out int vid, out int pid)
        {
            vid = 0;
            pid = 0;
            if (string.IsNullOrEmpty(identifier)) return false;
            string s = identifier.Trim().ToLowerInvariant();

            int vidIdx = s.IndexOf("vid_");
            int pidIdx = s.IndexOf("pid_");
            if (vidIdx >= 0 && pidIdx >= 0 && vidIdx + 8 <= s.Length && pidIdx + 8 <= s.Length)
            {
                int v, p;
                if (int.TryParse(s.Substring(vidIdx + 4, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v) &&
                    int.TryParse(s.Substring(pidIdx + 4, 4), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out p))
                {
                    vid = v;
                    pid = p;
                    return true;
                }
            }

            string[] parts = s.Split(new char[] { ':', '&', '#' }, StringSplitOptions.RemoveEmptyEntries);
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i].Length == 4 && parts[i + 1].Length == 4)
                {
                    int v, p;
                    if (int.TryParse(parts[i], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v) &&
                        int.TryParse(parts[i + 1], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out p))
                    {
                        vid = v;
                        pid = p;
                        return true;
                    }
                }
            }

            return false;
        }
    }
}
