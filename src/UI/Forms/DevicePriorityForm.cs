// ============================================================================
// 模块名称: DevicePriorityForm.cs
// 所属区域: UI/Forms (业务对话框层)
// 核心职责:
//   多外设托盘图标展示优先级调整窗口，承载 PriorityReorderControl，提供保存排序、重置、以及持久化至 ConfigManager 的完整交互。
// 依赖关系: Controls.PriorityReorderControl, Controls.ModernButton, Core.ConfigManager
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、属性或绘图逻辑时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（❌ 严禁 $"..." 插值，必须使用 string.Format 或 + 拼接）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class DevicePriorityForm : DpiAwareForm
    {
        private PriorityReorderControl reorderControl;
        private ModernButton btnSave;
        private ModernButton btnCancel;

        public DevicePriorityForm(List<MouseBatteryInfo> devices) : this(devices, 1.0f) { }

        public DevicePriorityForm(List<MouseBatteryInfo> devices, float scale) : base(scale)
        {
            this.Text = "设备优先级管理 (拖拽排序)";
            this.Size = new Size((int)(460 * dpiScale), (int)(520 * dpiScale));
            this.MinimumSize = new Size((int)(440 * dpiScale), (int)(480 * dpiScale));
            this.BackColor = Color.FromArgb(18, 19, 23);
            this.ForeColor = Color.White;
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            int pad = (int)(16 * dpiScale);

            var lblTitle = new Label();
            lblTitle.Text = "设备优先级管理";
            lblTitle.Font = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(240, 245, 255);
            lblTitle.Location = new Point(pad, (int)(12 * dpiScale));
            lblTitle.AutoSize = true;
            this.Controls.Add(lblTitle);

            var lblTip = new Label();
            lblTip.Text = "按住右侧把手上下拖动卡片可调整顺位。第 1 位设备默认常驻任务栏（离线时自动顺延下一位在线设备）。点击「✕」可移除自定义设备。";
            lblTip.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblTip.ForeColor = Color.FromArgb(145, 155, 175);
            lblTip.Location = new Point(pad, (int)(38 * dpiScale));
            lblTip.Size = new Size(this.ClientSize.Width - (pad * 2), (int)(32 * dpiScale));
            this.Controls.Add(lblTip);

            int btnW = (int)(110 * dpiScale);
            int btnH = (int)(34 * dpiScale);
            int bottomPad = (int)(16 * dpiScale);
            int btnY = this.ClientSize.Height - bottomPad - btnH;

            btnCancel = new ModernButton();
            btnCancel.Text = "取消";
            btnCancel.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
            btnCancel.NormalColor = Color.FromArgb(36, 39, 49);
            btnCancel.HoverColor = Color.FromArgb(48, 52, 65);
            btnCancel.PressedColor = Color.FromArgb(28, 30, 38);
            btnCancel.BorderColor = Color.FromArgb(58, 63, 78);
            btnCancel.ForeColor = Color.FromArgb(230, 235, 245);
            btnCancel.CornerRadius = (int)(8 * dpiScale);
            btnCancel.Location = new Point(this.ClientSize.Width - pad - (btnW * 2) - (int)(10 * dpiScale), btnY);
            btnCancel.Size = new Size(btnW, btnH);
            btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnCancel);

            btnSave = new ModernButton();
            btnSave.Text = "确定并保存";
            btnSave.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
            btnSave.NormalColor = Color.FromArgb(0, 200, 83);
            btnSave.HoverColor = Color.FromArgb(0, 230, 118);
            btnSave.PressedColor = Color.FromArgb(0, 160, 65);
            btnSave.ForeColor = Color.FromArgb(10, 24, 15);
            btnSave.CornerRadius = (int)(8 * dpiScale);
            btnSave.Location = new Point(this.ClientSize.Width - pad - btnW, btnY);
            btnSave.Size = new Size(btnW, btnH);
            btnSave.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnSave.Click += (s, e) => {
                var ids = new List<string>();
                foreach (var it in reorderControl.Items) ids.Add(it.DeviceId);
                DeviceManager.SavePriorityList(ids);
                if (ids.Count > 0) DeviceManager.SetConfiguredPrimaryDeviceId(ids[0]);
                this.DialogResult = DialogResult.OK;
                this.Close();
            };
            this.Controls.Add(btnSave);

            int listH = btnY - (int)(76 * dpiScale) - (int)(14 * dpiScale);
            reorderControl = new PriorityReorderControl();
            reorderControl.DpiScale = dpiScale;
            reorderControl.Location = new Point(pad, (int)(76 * dpiScale));
            reorderControl.Size = new Size(this.ClientSize.Width - (pad * 2), listH);
            reorderControl.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            if (devices != null)
            {
                for (int i = 0; i < devices.Count; i++)
                {
                    var d = devices[i];
                    reorderControl.Items.Add(new PriorityItem
                    {
                        DeviceId = d.DeviceId,
                        Name = d.DisplayName,
                        Icon = d.CategoryIcon,
                        Category = d.Category,
                        Battery = d.BatteryPercent,
                        IsConnected = d.IsConnected && !d.IsSleeping,
                        AccentColor = d.BrandColor,
                        PriorityIndex = i + 1,
                        IsCustom = d.IsCustom
                    });
                }
                reorderControl.ResetPositions();
            }
            this.Controls.Add(reorderControl);
        }
    }
}