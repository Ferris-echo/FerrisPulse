// ============================================================================
// 模块名称: LowBatteryAlertSettingsDialog.cs
// 所属区域: UI/Forms (业务对话框层)
// 核心职责:
//   低电量提醒分级配置窗口，支持用户分别开关「低电量系统通知提醒」、
//   「低电量主界面颜色提醒」与「低电量托盘图标变色提醒」三项独立策略，
//   关闭变色提醒后全程保留设备品牌色/自定义主题色。
// 依赖关系: Forms.DpiAwareForm, Controls.RoundedCard, Controls.ModernCheckBox, Controls.ModernButton
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑或交互契约时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（❌ 严禁 $"..." 插值、?.、nameof、out var）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Drawing;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class LowBatteryAlertSettingsDialog : DpiAwareForm
    {
        private ModernCheckBox chkNotify;
        private ModernCheckBox chkUiColor;
        private ModernCheckBox chkTrayColor;
        private ModernButton btnSave;
        private ModernButton btnCancel;

        public bool ResultNotifyEnabled { get; private set; }
        public bool ResultUiColorEnabled { get; private set; }
        public bool ResultTrayColorEnabled { get; private set; }

        public LowBatteryAlertSettingsDialog(bool notifyEnabled, bool uiColorEnabled, bool trayColorEnabled, float scale)
            : base(scale)
        {
            this.ResultNotifyEnabled = notifyEnabled;
            this.ResultUiColorEnabled = uiColorEnabled;
            this.ResultTrayColorEnabled = trayColorEnabled;

            this.Text = "低电量提醒设置 (≤20%)";
            int clientW = (int)(412 * dpiScale);
            int clientH = (int)(306 * dpiScale);
            this.ClientSize = new Size(clientW, clientH);
            this.BackColor = Color.FromArgb(18, 19, 23);
            this.ForeColor = Color.White;
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            int pad = (int)(18 * dpiScale);
            int innerW = clientW - (pad * 2);

            var lblTitle = new Label();
            lblTitle.Text = "低电量提醒方式设置";
            lblTitle.Font = new Font("Microsoft YaHei UI", 11.2F, FontStyle.Bold, GraphicsUnit.Point);
            lblTitle.ForeColor = Color.FromArgb(240, 245, 255);
            lblTitle.Location = new Point(pad, (int)(14 * dpiScale));
            lblTitle.AutoSize = true;
            this.Controls.Add(lblTitle);

            var lblSub = new Label();
            lblSub.Text = "设备电量 ≤20% 且未充电时触发；关闭颜色提醒后将始终保持品牌色。";
            lblSub.Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Regular, GraphicsUnit.Point);
            lblSub.ForeColor = Color.FromArgb(145, 155, 175);
            lblSub.Location = new Point(pad, (int)(40 * dpiScale));
            lblSub.Size = new Size(innerW, (int)(20 * dpiScale));
            this.Controls.Add(lblSub);

            var card = new RoundedCard();
            card.CardColor = Color.FromArgb(25, 27, 34);
            card.BorderColor = Color.FromArgb(42, 46, 58);
            card.CornerRadius = (int)(10 * dpiScale);
            card.Location = new Point(pad, (int)(66 * dpiScale));
            card.Size = new Size(innerW, (int)(178 * dpiScale));
            this.Controls.Add(card);

            int cPad = (int)(14 * dpiScale);
            int rowW = innerW - (cPad * 2);

            chkNotify = AddOptionRow(
                card,
                cPad,
                (int)(12 * dpiScale),
                rowW,
                "低电量系统通知提醒",
                "电量降至 20% 及以下时弹出 Windows 系统气泡提醒",
                notifyEnabled);

            chkUiColor = AddOptionRow(
                card,
                cPad,
                (int)(68 * dpiScale),
                rowW,
                "低电量主界面颜色提醒",
                "主窗口电量数字、进度条与胶囊在 ≤20% 变黄、≤10% 变红",
                uiColorEnabled);

            chkTrayColor = AddOptionRow(
                card,
                cPad,
                (int)(124 * dpiScale),
                rowW,
                "低电量托盘图标变色提醒",
                "状态栏图标在 ≤20% 变黄、≤10% 变红（关闭则始终显示品牌色）",
                trayColorEnabled);

            int btnH = (int)(34 * dpiScale);
            int btnY = clientH - btnH - (int)(14 * dpiScale);
            int saveW = (int)(116 * dpiScale);
            int cancelW = (int)(84 * dpiScale);
            int gap = (int)(10 * dpiScale);

            btnSave = new ModernButton();
            btnSave.Text = "确定并保存";
            btnSave.Font = new Font("Microsoft YaHei UI", 9.0F, FontStyle.Bold, GraphicsUnit.Point);
            btnSave.NormalColor = Color.FromArgb(0, 200, 83);
            btnSave.HoverColor = Color.FromArgb(0, 230, 118);
            btnSave.PressedColor = Color.FromArgb(0, 160, 65);
            btnSave.ForeColor = Color.FromArgb(10, 24, 15);
            btnSave.CornerRadius = (int)(7 * dpiScale);
            btnSave.Location = new Point(clientW - pad - saveW, btnY);
            btnSave.Size = new Size(saveW, btnH);
            btnSave.Click += (s, e) => CommitAndClose();
            this.Controls.Add(btnSave);

            btnCancel = new ModernButton();
            btnCancel.Text = "取消";
            btnCancel.Font = new Font("Microsoft YaHei UI", 9.0F, FontStyle.Regular, GraphicsUnit.Point);
            btnCancel.NormalColor = Color.FromArgb(32, 36, 46);
            btnCancel.HoverColor = Color.FromArgb(46, 52, 66);
            btnCancel.PressedColor = Color.FromArgb(24, 28, 36);
            btnCancel.BorderColor = Color.FromArgb(56, 62, 78);
            btnCancel.ForeColor = Color.FromArgb(200, 210, 225);
            btnCancel.CornerRadius = (int)(7 * dpiScale);
            btnCancel.Location = new Point(btnSave.Left - gap - cancelW, btnY);
            btnCancel.Size = new Size(cancelW, btnH);
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnCancel);

            this.KeyPreview = true;
            this.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; CommitAndClose(); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; this.DialogResult = DialogResult.Cancel; this.Close(); }
            };

            this.modernChromeTitlePadLeftBase = 18;
            AttachModernChrome("低电量提醒方式设置 (≤20%)", lblTitle, 14, true, true);
        }

        private ModernCheckBox AddOptionRow(RoundedCard parent, int x, int y, int width, string title, string desc, bool initialChecked)
        {
            var chk = new ModernCheckBox();
            chk.Text = title;
            chk.Font = new Font("Microsoft YaHei UI", 9.0F, FontStyle.Bold, GraphicsUnit.Point);
            chk.ForeColor = Color.FromArgb(232, 238, 248);
            chk.Checked = initialChecked;
            chk.Location = new Point(x, y);
            chk.Size = new Size(width, (int)(22 * dpiScale));
            parent.Controls.Add(chk);

            var lbl = new Label();
            lbl.Text = desc;
            lbl.Font = new Font("Microsoft YaHei UI", 7.8F, FontStyle.Regular, GraphicsUnit.Point);
            lbl.ForeColor = Color.FromArgb(135, 145, 165);
            lbl.BackColor = parent.CardColor;
            lbl.Location = new Point(x + (int)(20 * dpiScale), y + (int)(23 * dpiScale));
            lbl.Size = new Size(Math.Max(40, width - (int)(20 * dpiScale)), (int)(18 * dpiScale));
            parent.Controls.Add(lbl);

            return chk;
        }

        private void CommitAndClose()
        {
            this.ResultNotifyEnabled = chkNotify != null && chkNotify.Checked;
            this.ResultUiColorEnabled = chkUiColor != null && chkUiColor.Checked;
            this.ResultTrayColorEnabled = chkTrayColor != null && chkTrayColor.Checked;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
