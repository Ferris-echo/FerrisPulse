// ============================================================================
// 模块名称: ResetConfirmDialog.cs
// 所属区域: UI/Forms (业务对话框层)
// 核心职责:
//   深度重置与卸载清理确认对话框：
//   提供【一键重置并重启（推荐排障）】与【彻底清理并退出（纯净卸载）】双模式选择，
//   向用户清晰展示即将清除的历史注册表、旧版开机自启与设备缓存范围。
// 依赖关系: DpiAwareForm, ModernButton, RoundedCard, ModernThemeHelper
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本对话框交互模式时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Drawing;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public enum ResetActionMode
    {
        None = 0,
        ResetAndRestart = 1,
        CleanAndExit = 2
    }

    public class ResetConfirmDialog : DpiAwareForm
    {
        private RoundedCard cardInfo;
        private Label lblDesc;
        private Label lblItem1;
        private Label lblItem2;
        private Label lblItem3;
        private ModernButton btnResetRestart;
        private ModernButton btnCleanExit;
        private ModernButton btnCancel;

        public ResetActionMode SelectedMode { get; private set; }

        public ResetConfirmDialog() : this(1.0f)
        {
        }

        public ResetConfirmDialog(float scale) : base(scale)
        {
            this.SelectedMode = ResetActionMode.None;
            InitializeResetDialogUI();
        }

        private void InitializeResetDialogUI()
        {
            this.Text = "重置与清理数据 - FerrisPulse · 灵脉";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.BackColor = Color.FromArgb(18, 19, 23);
            this.ForeColor = Color.White;
            this.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.DoubleBuffered = true;
            this.KeyPreview = true;
            this.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    this.SelectedMode = ResetActionMode.None;
                    this.DialogResult = DialogResult.Cancel;
                    this.Close();
                }
            };

            lblDesc = new Label();
            lblDesc.Text = "清理旧版残留配置、自启动项与设备缓存：";
            lblDesc.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular);
            lblDesc.ForeColor = Color.FromArgb(170, 178, 195);
            lblDesc.BackColor = Color.Transparent;
            lblDesc.AutoSize = false;
            this.Controls.Add(lblDesc);

            cardInfo = new RoundedCard();
            cardInfo.CardColor = Color.FromArgb(25, 27, 34);
            cardInfo.BorderColor = Color.FromArgb(44, 48, 62);
            this.Controls.Add(cardInfo);

            lblItem1 = new Label();
            lblItem1.Text = "✔  清理所有旧版本开机自启动项与旧后台进程（根治双开冲突）";
            lblItem1.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular);
            lblItem1.ForeColor = Color.FromArgb(220, 228, 242);
            lblItem1.BackColor = cardInfo.CardColor;
            lblItem1.AutoSize = true;
            cardInfo.Controls.Add(lblItem1);

            lblItem2 = new Label();
            lblItem2.Text = "✔  清空历代注册表数据（含 FerrisPulse 与旧版 RazerBatteryTray）";
            lblItem2.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular);
            lblItem2.ForeColor = Color.FromArgb(220, 228, 242);
            lblItem2.BackColor = cardInfo.CardColor;
            lblItem2.AutoSize = true;
            cardInfo.Controls.Add(lblItem2);

            lblItem3 = new Label();
            lblItem3.Text = "✔  重置被隐藏设备黑名单、自定义向导旧配置与全部电量/DPI 缓存";
            lblItem3.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular);
            lblItem3.ForeColor = Color.FromArgb(220, 228, 242);
            lblItem3.BackColor = cardInfo.CardColor;
            lblItem3.AutoSize = true;
            cardInfo.Controls.Add(lblItem3);

            btnResetRestart = new ModernButton();
            btnResetRestart.Text = "重置配置并重启（推荐）";
            btnResetRestart.Font = new Font("Microsoft YaHei UI", 9.2F, FontStyle.Bold);
            btnResetRestart.NormalColor = Color.FromArgb(22, 52, 40);
            btnResetRestart.HoverColor = Color.FromArgb(30, 72, 55);
            btnResetRestart.PressedColor = Color.FromArgb(16, 40, 30);
            btnResetRestart.BorderColor = Color.FromArgb(0, 215, 110);
            btnResetRestart.ForeColor = Color.FromArgb(0, 235, 125);
            btnResetRestart.TextColor = Color.FromArgb(0, 235, 125);
            btnResetRestart.Cursor = Cursors.Hand;
            btnResetRestart.Click += delegate(object s, EventArgs e)
            {
                this.SelectedMode = ResetActionMode.ResetAndRestart;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };
            this.Controls.Add(btnResetRestart);

            btnCleanExit = new ModernButton();
            btnCleanExit.Text = "彻底清理数据并退出";
            btnCleanExit.Font = new Font("Microsoft YaHei UI", 9.0F, FontStyle.Bold);
            btnCleanExit.NormalColor = Color.FromArgb(52, 24, 28);
            btnCleanExit.HoverColor = Color.FromArgb(72, 32, 38);
            btnCleanExit.PressedColor = Color.FromArgb(38, 18, 22);
            btnCleanExit.BorderColor = Color.FromArgb(225, 75, 85);
            btnCleanExit.ForeColor = Color.FromArgb(255, 125, 135);
            btnCleanExit.TextColor = Color.FromArgb(255, 125, 135);
            btnCleanExit.Cursor = Cursors.Hand;
            btnCleanExit.Click += delegate(object s, EventArgs e)
            {
                this.SelectedMode = ResetActionMode.CleanAndExit;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };
            this.Controls.Add(btnCleanExit);

            btnCancel = new ModernButton();
            btnCancel.Text = "取  消";
            btnCancel.Font = new Font("Microsoft YaHei UI", 9.0F, FontStyle.Regular);
            btnCancel.NormalColor = Color.FromArgb(34, 38, 48);
            btnCancel.HoverColor = Color.FromArgb(46, 52, 66);
            btnCancel.PressedColor = Color.FromArgb(26, 30, 38);
            btnCancel.BorderColor = Color.FromArgb(56, 62, 78);
            btnCancel.ForeColor = Color.FromArgb(195, 202, 218);
            btnCancel.TextColor = Color.FromArgb(195, 202, 218);
            btnCancel.Cursor = Cursors.Hand;
            btnCancel.Click += delegate(object s, EventArgs e)
            {
                this.SelectedMode = ResetActionMode.None;
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };
            this.Controls.Add(btnCancel);

            ApplyResetDialogLayout();
            this.modernChromeTitlePadLeftBase = 22;
            AttachModernChrome("深度重置与历史数据清理", null, 0, false, false);
        }

        private void ApplyResetDialogLayout()
        {
            int clientW = Scale(460);
            int padX = Scale(22);
            int contentW = clientW - (padX * 2);

            if (lblDesc != null)
            {
                lblDesc.Location = new Point(padX, Scale(58));
                lblDesc.Size = new Size(contentW, Scale(38));
            }

            int cardY = Scale(102);
            int cardH = Scale(104);
            if (cardInfo != null)
            {
                cardInfo.Location = new Point(padX, cardY);
                cardInfo.Size = new Size(contentW, cardH);
                cardInfo.CornerRadius = Scale(8);

                if (lblItem1 != null) lblItem1.Location = new Point(Scale(16), Scale(14));
                if (lblItem2 != null) lblItem2.Location = new Point(Scale(16), Scale(42));
                if (lblItem3 != null) lblItem3.Location = new Point(Scale(16), Scale(70));
            }

            int btnH = Scale(36);
            int btnGap = Scale(10);
            int b1Y = cardY + cardH + Scale(16);
            int b2Y = b1Y + btnH + btnGap;
            int b3Y = b2Y + btnH + btnGap;

            if (btnResetRestart != null)
            {
                btnResetRestart.Location = new Point(padX, b1Y);
                btnResetRestart.Size = new Size(contentW, btnH);
                btnResetRestart.CornerRadius = Scale(6);
            }

            if (btnCleanExit != null)
            {
                btnCleanExit.Location = new Point(padX, b2Y);
                btnCleanExit.Size = new Size(contentW, btnH);
                btnCleanExit.CornerRadius = Scale(6);
            }

            if (btnCancel != null)
            {
                btnCancel.Location = new Point(padX, b3Y);
                btnCancel.Size = new Size(contentW, Scale(32));
                btnCancel.CornerRadius = Scale(6);
            }

            int clientH = b3Y + Scale(32) + Scale(18);
            this.ClientSize = new Size(clientW, clientH);
            LayoutModernChrome();
        }

        protected override void OnApplyDpiScale(float scale)
        {
            base.OnApplyDpiScale(scale);
            ApplyResetDialogLayout();
            this.Invalidate(true);
        }
    }
}
