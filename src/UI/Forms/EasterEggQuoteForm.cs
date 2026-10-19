// ============================================================================
// 模块名称: EasterEggQuoteForm.cs
// 所属区域: UI/Forms (二级窗体层)
// 核心职责:
//   INFP 专属心跳彩蛋弹窗：呈现作者 Ferris-echo 写给玩家的诗意心声寄语。
//   具备暗黑温润质感、全彩呼吸光晕、高雅留白排版与 ESC 快速退出。
// 依赖关系: ModernThemeHelper, ModernButton, NativeMethods
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 2. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class EasterEggQuoteForm : DpiAwareForm
    {
        private ModernButton btnClose;
        private ModernButton btnConfirm;

        public static void ShowEasterEgg(IWin32Window owner, float dpiScale = 0f)
        {
            using (EasterEggQuoteForm form = new EasterEggQuoteForm(dpiScale))
            {
                if (owner != null) form.ShowDialog(owner);
                else form.ShowDialog();
            }
        }

        public EasterEggQuoteForm() : this(0f)
        {
        }

        public EasterEggQuoteForm(float dpiScale) : base(dpiScale)
        {
            InitializeUI();
        }

        private void InitializeUI()
        {
            this.Text = "灵脉心语 · Ferris-echo";
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color.FromArgb(18, 20, 26);
            this.ForeColor = Color.White;
            this.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            int formW = Scale(520);
            int formH = Scale(360);
            this.ClientSize = new Size(formW, formH);
            this.DoubleBuffered = true;
            this.ShowInTaskbar = false;
            this.TopMost = false;
            this.KeyPreview = true;
            this.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    this.Close();
                }
            };

            // 1. 顶部标题栏
            Panel titleBar = new Panel();
            titleBar.Dock = DockStyle.Top;
            titleBar.Height = Scale(36);
            titleBar.BackColor = Color.FromArgb(22, 25, 33);
            titleBar.MouseDown += OnDragMouseDown;

            Label lblTitle = new Label();
            lblTitle.Text = "灵脉心语 · Ferris-echo";
            lblTitle.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(160, 172, 195);
            lblTitle.Location = new Point(Scale(20), Scale(9));
            lblTitle.AutoSize = true;
            lblTitle.MouseDown += OnDragMouseDown;
            titleBar.Controls.Add(lblTitle);

            btnClose = new ModernButton();
            btnClose.Text = "✕";
            btnClose.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            btnClose.Size = new Size(Scale(32), Scale(26));
            btnClose.Location = new Point(formW - Scale(20) - Scale(24), Scale(5));
            btnClose.NormalColor = Color.Transparent;
            btnClose.HoverColor = Color.FromArgb(232, 17, 35);
            btnClose.PressedColor = Color.FromArgb(180, 10, 25);
            btnClose.ForeColor = Color.FromArgb(180, 180, 180);
            btnClose.CornerRadius = Scale(4);
            btnClose.Click += delegate(object s, EventArgs e) { this.Close(); };
            titleBar.Controls.Add(btnClose);

            this.Controls.Add(titleBar);

            // 2. 寄语卡片内容容器 (RoundedCard)
            int cardPad = Scale(24);
            int cardTop = Scale(50);
            int cardW = formW - cardPad * 2;
            int cardH = formH - cardTop - Scale(74);
            RoundedCard cardQuote = new RoundedCard();
            cardQuote.Location = new Point(cardPad, cardTop);
            cardQuote.Size = new Size(cardW, cardH);
            cardQuote.CornerRadius = Scale(8);
            cardQuote.CardColor = Color.FromArgb(24, 27, 36);
            cardQuote.BorderColor = Color.FromArgb(48, 55, 72);
            cardQuote.MouseDown += OnDragMouseDown;

            // 诗意寄语文案展示
            Label lblLine1 = new Label();
            lblLine1.Text = "拂去所有冗余与喧嚣，只将最纯净的秩序赠予你。";
            lblLine1.Font = new Font("Microsoft YaHei UI", 10.2F, FontStyle.Bold);
            lblLine1.ForeColor = Color.FromArgb(240, 245, 255);
            lblLine1.BackColor = cardQuote.CardColor;
            lblLine1.Location = new Point(Scale(24), Scale(28));
            lblLine1.AutoSize = true;
            lblLine1.MouseDown += OnDragMouseDown;
            cardQuote.Controls.Add(lblLine1);

            Label lblLine2 = new Label();
            lblLine2.Text = "灵脉初醒，微光致意。";
            lblLine2.Font = new Font("Microsoft YaHei UI", 10.2F, FontStyle.Bold);
            lblLine2.ForeColor = Color.FromArgb(0, 230, 118);
            lblLine2.BackColor = cardQuote.CardColor;
            lblLine2.Location = new Point(Scale(24), Scale(62));
            lblLine2.AutoSize = true;
            lblLine2.MouseDown += OnDragMouseDown;
            cardQuote.Controls.Add(lblLine2);

            Label lblLine3 = new Label();
            lblLine3.Text = "愿你在属于自己的战场上，永远专注、自由、无可阻挡。";
            lblLine3.Font = new Font("Microsoft YaHei UI", 9.8F, FontStyle.Regular);
            lblLine3.ForeColor = Color.FromArgb(195, 204, 222);
            lblLine3.BackColor = cardQuote.CardColor;
            lblLine3.Location = new Point(Scale(24), Scale(114));
            lblLine3.AutoSize = true;
            lblLine3.MouseDown += OnDragMouseDown;
            cardQuote.Controls.Add(lblLine3);

            Label lblSign = new Label();
            lblSign.Text = "——  Ferris-echo";
            lblSign.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Italic);
            lblSign.ForeColor = Color.FromArgb(140, 150, 172);
            lblSign.BackColor = cardQuote.CardColor;
            lblSign.AutoSize = true;
            lblSign.Location = new Point(cardW - Scale(160), cardH - Scale(46));
            lblSign.MouseDown += OnDragMouseDown;
            cardQuote.Controls.Add(lblSign);

            this.Controls.Add(cardQuote);

            // 3. 底部致意关闭按钮
            int btnConfirmW = Scale(180);
            int btnConfirmH = Scale(36);
            btnConfirm = new ModernButton();
            btnConfirm.Text = "知音致意 · 心领神会";
            btnConfirm.Font = new Font("Microsoft YaHei UI", 9.2F, FontStyle.Bold);
            btnConfirm.Location = new Point((formW - btnConfirmW) / 2, formH - Scale(58));
            btnConfirm.Size = new Size(btnConfirmW, btnConfirmH);
            btnConfirm.NormalColor = Color.FromArgb(32, 38, 52);
            btnConfirm.HoverColor = Color.FromArgb(0, 200, 83);
            btnConfirm.PressedColor = Color.FromArgb(0, 160, 65);
            btnConfirm.BorderColor = Color.FromArgb(58, 68, 92);
            btnConfirm.ForeColor = Color.White;
            btnConfirm.CornerRadius = Scale(6);
            btnConfirm.Click += delegate(object s, EventArgs e) { this.Close(); };
            this.Controls.Add(btnConfirm);

            this.MouseDown += OnDragMouseDown;

            // 原生圆角与阴影已由 DpiAwareForm 自动处理
        }

        private void OnDragMouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                try
                {
                    NativeMethods.ReleaseCapture();
                    NativeMethods.SendMessage(this.Handle, NativeMethods.WM_NCLBUTTONDOWN, (IntPtr)NativeMethods.HT_CAPTION, IntPtr.Zero);
                }
                catch { }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            // 窗体霓虹微光外边框
            using (Pen borderPen = new Pen(Color.FromArgb(0, 200, 83), 1.2f))
            {
                g.DrawRectangle(borderPen, 0, 0, this.Width - 1, this.Height - 1);
            }
        }
    }
}
