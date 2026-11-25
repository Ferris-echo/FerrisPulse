// ============================================================================
// 模块名称: ModernInputBox.cs
// 所属区域: UI/Controls (高级功能控件层)
// 核心职责:
//   现代化深色暗黑风圆角输入框控件，内部宿主无边框原生 TextBox 并自绘制外层抗锯齿圆角背景、
//   自动继承父容器/RoundedCard 底色消除边角锯齿、悬停微光边框与聚焦发光外圈（DpiScale 自适应）。
// 依赖关系: RoundedCard
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、属性或绘图逻辑时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（❌ 严禁 $"..." 插值，必须使用 string.Format 或 + 拼接）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class ModernInputBox : Control
    {
        public float DpiScale = 1.0f;
        public Color FillColor = Color.FromArgb(20, 23, 31);
        public Color AccentBorderColor = Color.FromArgb(0, 230, 118);

        private TextBox innerBox;
        private bool isFocused = false;
        private bool isHovered = false;

        public TextBox InnerTextBox { get { return innerBox; } }

        public override string Text
        {
            get { return innerBox != null ? innerBox.Text : base.Text; }
            set
            {
                string safe = value ?? "";
                base.Text = safe;
                if (innerBox != null && innerBox.Text != safe) innerBox.Text = safe;
            }
        }

        public ModernInputBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.FromArgb(18, 19, 23);
            ForeColor = Color.FromArgb(245, 250, 255);
            Cursor = Cursors.IBeam;
            DoubleBuffered = true;

            innerBox = new TextBox();
            innerBox.BorderStyle = BorderStyle.None;
            innerBox.BackColor = FillColor;
            innerBox.ForeColor = Color.FromArgb(245, 250, 255);
            innerBox.Font = new Font("Microsoft YaHei UI", 9.5F);
            innerBox.GotFocus += (s, e) => { isFocused = true; Invalidate(); };
            innerBox.LostFocus += (s, e) => { isFocused = false; Invalidate(); };
            innerBox.MouseEnter += (s, e) => { isHovered = true; Invalidate(); };
            innerBox.MouseLeave += (s, e) => { isHovered = false; Invalidate(); };
            innerBox.TextChanged += (s, e) => {
                if (base.Text != innerBox.Text) base.Text = innerBox.Text;
            };
            this.Controls.Add(innerBox);
        }

        public void SelectAll()
        {
            if (innerBox != null) innerBox.SelectAll();
        }

        public new bool Focus()
        {
            if (innerBox != null) return innerBox.Focus();
            return base.Focus();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            if (innerBox != null && this.Font != null)
            {
                innerBox.Font = this.Font;
                LayoutInnerBox();
            }
        }

        protected override void OnForeColorChanged(EventArgs e)
        {
            base.OnForeColorChanged(e);
            if (innerBox != null) innerBox.ForeColor = this.ForeColor;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (innerBox != null) innerBox.Focus();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutInnerBox();
        }

        private void LayoutInnerBox()
        {
            if (innerBox == null) return;
            int padH = Math.Max(8, (int)(10 * DpiScale));
            int prefH = innerBox.PreferredHeight;
            if (prefH <= 0) prefH = Math.Max(14, (int)(18 * DpiScale));
            int h = Math.Min(Math.Max(12, Height - 4), prefH);
            int y = Math.Max(2, (Height - h) / 2);
            innerBox.BackColor = FillColor;
            innerBox.SetBounds(padH, y, Math.Max(10, Width - (padH * 2)), h);
        }

        private Color ResolveParentBackColor()
        {
            Control p = this.Parent;
            while (p != null)
            {
                if (p is RoundedCard) return ((RoundedCard)p).CardColor;
                if (p.BackColor != Color.Transparent && p.BackColor.A == 255) return p.BackColor;
                p = p.Parent;
            }
            return Color.FromArgb(18, 19, 23);
        }

        protected override void OnMouseEnter(EventArgs e) { isHovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { isHovered = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(ResolveParentBackColor());

            int r = Math.Max(4, (int)(7 * DpiScale));
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Color border = isFocused
                ? AccentBorderColor
                : (isHovered ? Color.FromArgb(84, 102, 134) : Color.FromArgb(52, 58, 76));

            using (var path = RoundedCard.GetRoundedRectangle(rect, r))
            {
                using (var brush = new SolidBrush(FillColor)) g.FillPath(brush, path);
                using (var pen = new Pen(border, isFocused ? 1.5f : 1.0f)) g.DrawPath(pen, path);
            }
        }
    }
}
