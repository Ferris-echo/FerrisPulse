// ============================================================================
// 模块名称: CacheClearConfirmDialog.cs
// 所属区域: UI/Forms (业务对话框层)
// 核心职责:
//   「清除缓存」二次确认对话框：先向用户透明展示将清除的范围（设备识别记忆、
//   应用设置、图标与运行缓存）、备份位置与保留项（adapters 自定义驱动、自启动、
//   快捷方式），用户点「清除缓存并重启」后才真正执行，点「取消」或 Esc 不做任何改动。
// 依赖关系: DpiAwareForm, ModernButton, RoundedCard, CacheResetManager
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 清理范围变化时（CacheResetManager 增删目标），必须同步更新本对话框文案。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class CacheClearConfirmDialog : DpiAwareForm
    {
        private RoundedCard cardInfo;
        private Label lblDesc;
        private Label lblItem1;
        private Label lblItem2;
        private Label lblItem3;
        private Label lblItem4;
        private Label lblItem5;
        private Label lblHint;
        private ModernButton btnConfirm;
        private ModernButton btnCancel;

        /// <summary>用户是否确认执行（true 才会真正清理）。</summary>
        public bool Confirmed { get; private set; }

        public CacheClearConfirmDialog() : this(1.0f)
        {
        }

        public CacheClearConfirmDialog(float scale) : base(scale)
        {
            this.Confirmed = false;
            InitializeCacheClearUI();
        }

        private static Label CreateItemLabel(RoundedCard card, string text)
        {
            Label lbl = new Label();
            lbl.Text = text;
            lbl.Font = new Font("Microsoft YaHei UI", 8.6F, FontStyle.Regular);
            lbl.ForeColor = Color.FromArgb(220, 228, 242);
            lbl.BackColor = card.CardColor;
            lbl.AutoSize = true;
            card.Controls.Add(lbl);
            return lbl;
        }

        private void InitializeCacheClearUI()
        {
            this.Text = "清除缓存与识别状态 - FerrisPulse · 灵脉";
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
                    this.Confirmed = false;
                    this.DialogResult = DialogResult.Cancel;
                    this.Close();
                }
            };

            lblDesc = new Label();
            lblDesc.Text = "将清除以下数据，让外设重新识别（清除前自动备份）：";
            lblDesc.Font = new Font("Microsoft YaHei UI", 8.6F, FontStyle.Regular);
            lblDesc.ForeColor = Color.FromArgb(170, 178, 195);
            lblDesc.BackColor = Color.Transparent;
            lblDesc.AutoSize = false;
            this.Controls.Add(lblDesc);

            cardInfo = new RoundedCard();
            cardInfo.CardColor = Color.FromArgb(25, 27, 34);
            cardInfo.BorderColor = Color.FromArgb(44, 48, 62);
            this.Controls.Add(cardInfo);

            lblItem1 = CreateItemLabel(cardInfo, "✔  设备识别与电量缓存（含隐藏设备黑名单与自定义设备条目）");
            lblItem2 = CreateItemLabel(cardInfo, "✔  应用设置与设备外观（窗口位置·托盘数量·低电量提醒恢复默认）");
            lblItem3 = CreateItemLabel(cardInfo, "✔  游戏图标与标题缓存、扩展包运行缓存");
            lblItem4 = CreateItemLabel(cardInfo, "✔  备份至 %LOCALAPPDATA%\\FerrisPulse-Backup\\（可双击 .reg 还原）");
            lblItem5 = CreateItemLabel(cardInfo, "✔  保留 adapters 自定义驱动脚本、自启动项与快捷方式");

            lblHint = new Label();
            lblHint.Text = "执行后程序会自动重启；若外设仍未正确识别，请把备份目录一并反馈。";
            lblHint.Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Regular);
            lblHint.ForeColor = Color.FromArgb(255, 171, 0);
            lblHint.BackColor = Color.Transparent;
            lblHint.AutoSize = false;
            this.Controls.Add(lblHint);

            btnConfirm = new ModernButton();
            btnConfirm.Text = "清除缓存并重启";
            btnConfirm.Font = new Font("Microsoft YaHei UI", 9.2F, FontStyle.Bold);
            btnConfirm.NormalColor = Color.FromArgb(22, 52, 40);
            btnConfirm.HoverColor = Color.FromArgb(30, 72, 55);
            btnConfirm.PressedColor = Color.FromArgb(16, 40, 30);
            btnConfirm.BorderColor = Color.FromArgb(0, 215, 110);
            btnConfirm.ForeColor = Color.FromArgb(0, 235, 125);
            btnConfirm.TextColor = Color.FromArgb(0, 235, 125);
            btnConfirm.Cursor = Cursors.Hand;
            btnConfirm.Click += delegate(object s, EventArgs e)
            {
                this.Confirmed = true;
                this.DialogResult = DialogResult.OK;
                this.Close();
            };
            this.Controls.Add(btnConfirm);

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
                this.Confirmed = false;
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };
            this.Controls.Add(btnCancel);

            ApplyCacheClearLayout();
            this.modernChromeTitlePadLeftBase = 22;
            AttachModernChrome("清除缓存与识别状态", null, 0, false, false);
        }

        private void ApplyCacheClearLayout()
        {
            int clientW = Scale(470);
            int padX = Scale(22);
            int contentW = clientW - (padX * 2);

            if (lblDesc != null)
            {
                lblDesc.Location = new Point(padX, Scale(58));
                lblDesc.Size = new Size(contentW, Scale(22));
            }

            int cardY = Scale(84);
            int rowH = Scale(26);
            int cardH = Scale(12) + (rowH * 5) + Scale(10);
            if (cardInfo != null)
            {
                cardInfo.Location = new Point(padX, cardY);
                cardInfo.Size = new Size(contentW, cardH);
                cardInfo.CornerRadius = Scale(8);

                int y = Scale(12);
                if (lblItem1 != null) { lblItem1.Location = new Point(Scale(14), y); y += rowH; }
                if (lblItem2 != null) { lblItem2.Location = new Point(Scale(14), y); y += rowH; }
                if (lblItem3 != null) { lblItem3.Location = new Point(Scale(14), y); y += rowH; }
                if (lblItem4 != null) { lblItem4.Location = new Point(Scale(14), y); y += rowH; }
                if (lblItem5 != null) { lblItem5.Location = new Point(Scale(14), y); }
            }

            int hintY = cardY + cardH + Scale(8);
            if (lblHint != null)
            {
                lblHint.Location = new Point(padX, hintY);
                lblHint.Size = new Size(contentW, Scale(20));
            }

            int btnH = Scale(36);
            int btnGap = Scale(10);
            int b1Y = hintY + Scale(20) + Scale(10);
            int b2Y = b1Y + btnH + btnGap;

            if (btnConfirm != null)
            {
                btnConfirm.Location = new Point(padX, b1Y);
                btnConfirm.Size = new Size(contentW, btnH);
                btnConfirm.CornerRadius = Scale(6);
            }

            if (btnCancel != null)
            {
                btnCancel.Location = new Point(padX, b2Y);
                btnCancel.Size = new Size(contentW, Scale(32));
                btnCancel.CornerRadius = Scale(6);
            }

            int clientH = b2Y + Scale(32) + Scale(18);
            this.ClientSize = new Size(clientW, clientH);
            LayoutModernChrome();
        }

        protected override void OnApplyDpiScale(float scale)
        {
            base.OnApplyDpiScale(scale);
            ApplyCacheClearLayout();
            this.Invalidate(true);
        }
    }
}
