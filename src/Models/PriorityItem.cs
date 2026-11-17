// ============================================================================
// 模块名称: PriorityItem.cs
// 所属区域: Models (数据契约层)
// 核心职责:
//   外设优先级管理与拖拽重排界面的数据项模型
// 依赖关系: DeviceCategory.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑或字段时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System.Drawing;

namespace RazerBatteryTray
{
    public class PriorityItem
    {
        public string DeviceId { get; set; }
        public string Name { get; set; }
        public string Icon { get; set; }
        public DeviceCategory Category { get; set; }
        public int Battery { get; set; }
        public bool IsConnected { get; set; }
        public bool IsSleeping { get; set; }
        public Color AccentColor { get; set; }
        public int PriorityIndex { get; set; }
        public float CurrentY { get; set; }
        public float TargetY { get; set; }
        public bool IsCustom { get; set; }
        public string HardwareFingerprint { get; set; }
    }
}
