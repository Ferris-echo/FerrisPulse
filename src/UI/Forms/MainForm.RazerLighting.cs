// ============================================================================
// 模块名称: MainForm.RazerLighting.cs
// 所属区域: UI (主窗体分部类)
// 核心职责:
//   雷蛇 Chroma 灯效入口。为「已实机验证支持 Chroma 扩展矩阵」的鼠标在托盘右键菜单
//   的「灯光调节」子菜单顶部注入「打开雷蛇灯效控制台…」项，点击弹出 RazerLightingDialog。
//   能力判定统一走 RazerChromaHelper.IsSupportedDeviceId，UI 层不出现任何 VID/PID 字面量。
// 依赖关系: RazerChromaHelper.cs, RazerLightingDialog.cs, MainForm.Tray.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 修改显示条件或菜单结构时同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止字符串插值、空条件运算符、nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public partial class MainForm
    {
        private ToolStripMenuItem razerConsoleMenuItem;

        /// <summary>
        /// 在「灯光调节」子菜单顶部注入 Chroma 控制台入口。仅注入一次，之后只切换可见性。
        /// </summary>
        private void EnsureRazerConsoleMenuItem(ToolStripMenuItem lightingRoot)
        {
            if (lightingRoot == null) return;
            if (razerConsoleMenuItem != null && razerConsoleMenuItem.Owner == lightingRoot.DropDown) return;

            razerConsoleMenuItem = new ToolStripMenuItem("🎨 打开雷蛇灯效控制台…");
            razerConsoleMenuItem.Click += delegate
            {
                MouseBatteryInfo target = selectedDevice;
                if (target == null) return;
                if (!RazerChromaHelper.IsSupportedDeviceId(target.DeviceId)) return;
                contextMenu.Close();
                BeginInvoke(new Action(delegate
                {
                    using (RazerLightingDialog dialog = new RazerLightingDialog(target.DeviceId, dpiScale))
                    {
                        dialog.ShowDialog(this);
                    }
                }));
            };

            lightingRoot.DropDownItems.Insert(0, razerConsoleMenuItem);
            lightingRoot.DropDownItems.Insert(1, new ToolStripSeparator());
        }

        /// <summary>当前选中设备是否支持 Chroma 扩展矩阵灯效（供托盘菜单门禁）。</summary>
        private bool IsCurrentDeviceChromaCapable()
        {
            MouseBatteryInfo target = selectedDevice;
            if (target == null) return false;
            return RazerChromaHelper.IsSupportedDeviceId(target.DeviceId);
        }
    }
}
