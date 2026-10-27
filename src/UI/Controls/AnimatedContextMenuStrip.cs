// ============================================================================
// 模块名称: AnimatedContextMenuStrip.cs
// 所属区域: UI/Controls (高级功能控件层)
// 核心职责:
//   提供带有平滑展开动画（Win32 AnimateWindow AW_SLIDE | AW_VER_POSITIVE）的上下文右键菜单容器。
// 依赖关系: 无
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、属性或绘图逻辑时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（❌ 严禁 $"..." 插值，必须使用 string.Format 或 + 拼接）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class AnimatedContextMenuStrip : ContextMenuStrip
    {
        private const int AW_SLIDE = 0x00040000;
        private const int AW_VER_POSITIVE = 0x00000004;

        [DllImport("user32.dll")]
        private static extern bool AnimateWindow(IntPtr hWnd, int time, int flags);

        protected override void SetVisibleCore(bool value)
        {
            if (value && !IsHandleCreated)
            {
                CreateControl();
            }
            if (value)
            {
                try
                {
                    AnimateWindow(this.Handle, 120, AW_SLIDE | AW_VER_POSITIVE);
                }
                catch (Exception diagEx) { Diagnostics.Log("UI/Controls/AnimatedContextMenuStrip.cs: 已忽略异常", diagEx); }
            }
            base.SetVisibleCore(value);
        }
    }
}
