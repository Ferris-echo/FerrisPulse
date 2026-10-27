// ============================================================================
// 模块名称: CustomDeviceAction.cs
// 所属区域: Models (数据契约层)
// 核心职责:
//   声明式外设自定义动作契约模型（支持开关 Toggle、滑块 Slider、触发按钮 Button、预设菜单 PresetMenu），
//   用于将键盘、耳机、鼠标等专有硬件控制（如自动休眠、Win键锁定、背光开关、亮度调节等）与 UI 彻底解耦。
// 依赖关系: System
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑或属性时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止 $"..." 插值、禁止 ?. 空条件运算符、禁止 nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;

namespace RazerBatteryTray
{
    public enum CustomActionType
    {
        Toggle = 0,
        Slider = 1,
        Button = 2,
        PresetMenu = 3
    }

    public class CustomDeviceAction
    {
        public string ActionId { get; set; }
        public string Title { get; set; }
        public CustomActionType ActionType { get; set; }
        public string ActiveText { get; set; }
        public string InactiveText { get; set; }
        public string ValueSuffix { get; set; }
        public bool BoolValue { get; set; }
        public int IntValue { get; set; }
        public int MinValue { get; set; }
        public int MaxValue { get; set; }
        public int[] Presets { get; set; }
        public bool ShowInTrayMenu { get; set; }

        public CustomDeviceAction()
        {
            ActionId = "";
            Title = "";
            ActionType = CustomActionType.Toggle;
            ActiveText = "";
            InactiveText = "";
            ValueSuffix = "%";
            BoolValue = false;
            IntValue = 0;
            MinValue = 0;
            MaxValue = 100;
            Presets = new int[0];
            ShowInTrayMenu = true;
        }

        public string GetFormattedButtonText()
        {
            if (ActionType == CustomActionType.Toggle)
            {
                if (BoolValue && !string.IsNullOrEmpty(ActiveText)) return ActiveText;
                if (!BoolValue && !string.IsNullOrEmpty(InactiveText)) return InactiveText;
                return string.Format("{0}: {1}", Title ?? "", BoolValue ? "已开启" : "已关闭");
            }
            if (ActionType == CustomActionType.Slider)
            {
                string suffix = ValueSuffix ?? "%";
                return string.Format("{0}: {1}{2}", Title ?? "", IntValue, suffix);
            }
            if (!string.IsNullOrEmpty(ActiveText)) return ActiveText;
            return Title ?? "";
        }

        public CustomDeviceAction Clone()
        {
            CustomDeviceAction copy = new CustomDeviceAction();
            copy.ActionId = this.ActionId;
            copy.Title = this.Title;
            copy.ActionType = this.ActionType;
            copy.ActiveText = this.ActiveText;
            copy.InactiveText = this.InactiveText;
            copy.ValueSuffix = this.ValueSuffix;
            copy.BoolValue = this.BoolValue;
            copy.IntValue = this.IntValue;
            copy.MinValue = this.MinValue;
            copy.MaxValue = this.MaxValue;
            if (this.Presets != null)
            {
                copy.Presets = new int[this.Presets.Length];
                Array.Copy(this.Presets, copy.Presets, this.Presets.Length);
            }
            copy.ShowInTrayMenu = this.ShowInTrayMenu;
            return copy;
        }
    }
}
