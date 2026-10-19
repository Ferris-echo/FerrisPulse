// ============================================================================
// 模块名称: RazerDeviceHardwareState.cs
// 所属区域: Models (数据契约层)
// 核心职责:
//   雷蛇硬件内部私有状态缓存（包含 ReportLength、DPI 档位矩阵、XY 独立轴灵敏度、轮询率等）
// 依赖关系: 无
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑或字段时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;

namespace RazerBatteryTray
{
    public class RazerDeviceHardwareState
    {
        public string DeviceId;
        public string DevicePath;
        public int ReportLength;
        public bool IsPrepended;
        public int BatteryPercent;
        public double ExactBatteryPercent;
        public string DeviceName;
        public int Dpi;
        public int DpiY;
        public int DpiStage;
        public int DpiStageCount;
        public int[] DpiStages;
        public int[] DpiStagesY;
        public bool IsXyIndependent;
        public double XyRatio;
        public int PollingRate;
        public byte ActiveTransactionId;
        public DateTime LastUpdated;
        public int ConsecutiveFailures;
        public DateTime LastSuccessfulCommunication;

        public RazerDeviceHardwareState()
        {
            ReportLength = 90;
            IsPrepended = false;
            ActiveTransactionId = 0x1F;
            LastUpdated = DateTime.MinValue;
            ConsecutiveFailures = 0;
            LastSuccessfulCommunication = DateTime.MinValue;
            DpiY = 0;
            DpiStagesY = null;
            IsXyIndependent = false;
            XyRatio = 1.0;
        }
    }
}
