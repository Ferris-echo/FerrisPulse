// ============================================================================
// 模块名称: RunningProcessItem.cs
// 所属区域: Models (数据契约层)
// 核心职责:
//   游戏守护进程管理器中的进程数据项模型，支持进程名、主窗口标题、物理 PID、可执行文件全路径与高清图标绑定。
// 依赖关系: System.Drawing
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑或字段时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System.Drawing;

namespace RazerBatteryTray
{
    public class RunningProcessItem
    {
        public string ProcessName { get; set; }
        public string WindowTitle { get; set; }
        public int Pid { get; set; }
        public string ExePath { get; set; }
        public Image IconImage { get; set; }

        public override string ToString()
        {
            if (string.IsNullOrEmpty(WindowTitle))
                return ProcessName;
            return WindowTitle + " (" + ProcessName + ")";
        }
    }
}
