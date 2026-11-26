// ============================================================================
// 模块名称: ModernButton.cs
// 所属区域: UI/Controls (基础原子控件层)
// 核心职责:
//   现代化自绘制按钮，具备鼠标悬停、按下三态动态着色、抗锯齿圆角背景以及可选的
//   矢量 8 齿齿轮、信息圆环或几何交叉关闭按钮绘制能力（无字符基线偏差）。
// 依赖关系: RoundedCard
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、属性或绘图逻辑时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（❌ 严禁 $"..." 插值，必须使用 string.Format 或 + 拼接）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class ModernButton : Control
    {
        private bool isHovered = false;
        private bool isPressed = false;

        public Color NormalColor { get; set; }
        public Color HoverColor { get; set; }
        public Color PressedColor { get; set; }
        public Color BorderColor { get; set; }
        public int CornerRadius { get; set; }
        public bool IsGearButton { get; set; }
        public bool IsInfoButton { get; set; }
        public bool IsCloseButton { get; set; }
        public Color TextColor
        {
            get { return this.ForeColor; }
            set { this.ForeColor = value; }
        }

        public ModernButton()
        {
            NormalColor = Color.FromArgb(0, 200, 83);
            HoverColor = Color.FromArgb(0, 230, 118);
            PressedColor = Color.FromArgb(0, 160, 65);
            BorderColor = Color.Transparent;
            CornerRadius = 8;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); isHovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); isHovered = false; isPressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { isPressed = true; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); isPressed = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            base.OnPaint(pevent);
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            Color parentBg = (Parent is RoundedCard) ? ((RoundedCard)Parent).CardColor : (Parent != null ? Parent.BackColor : Color.FromArgb(18, 19, 23));
            g.Clear(parentBg);

            Color fill = NormalColor;
            if (isPressed) fill = PressedColor;
            else if (isHovered) fill = HoverColor;

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = RoundedCard.GetRoundedRectangle(rect, CornerRadius))
            {
                using (SolidBrush brush = new SolidBrush(fill))
                {
                    g.FillPath(brush, path);
                }

                bool skipBorderOnActiveClose = IsCloseButton && (isHovered || isPressed);
                if (BorderColor != Color.Transparent && !skipBorderOnActiveClose)
                {
                    using (Pen pen = new Pen(BorderColor, 1f))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }

            if (IsGearButton)
            {
                Color iconColor = isPressed ? Color.FromArgb(0, 229, 255) : (isHovered ? Color.White : ForeColor);
                DrawGearIcon(g, rect, iconColor);
            }
            else if (IsInfoButton)
            {
                Color iconColor = isPressed ? Color.FromArgb(200, 205, 215) : (isHovered ? Color.White : ForeColor);
                DrawInfoIcon(g, rect, iconColor);
            }
            else if (IsCloseButton)
            {
                Color iconColor = (isPressed || isHovered) ? Color.White : ForeColor;
                DrawCloseIcon(g, rect, iconColor);
            }
            else if (!string.IsNullOrEmpty(Text))
            {
                TextRenderer.DrawText(g, Text, Font, ClientRectangle, ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }

        private void DrawGearIcon(Graphics g, Rectangle bounds, Color color)
        {
            float cx = bounds.X + bounds.Width / 2f;
            float cy = bounds.Y + bounds.Height / 2f;
            float rOuter = bounds.Height * 0.28f;
            float rRoot = rOuter * 0.72f;
            float rHole = rOuter * 0.38f;
            int teeth = 8;

            using (GraphicsPath path = new GraphicsPath())
            {
                List<PointF> pts = new List<PointF>();
                double step = 2.0 * Math.PI / teeth;
                for (int i = 0; i < teeth; i++)
                {
                    double a0 = i * step - Math.PI / 2.0;
                    double a1 = a0 + step * 0.22;
                    double a2 = a0 + step * 0.45;
                    double a3 = a0 + step * 0.67;

                    pts.Add(new PointF(cx + (float)(rRoot * Math.Cos(a0)), cy + (float)(rRoot * Math.Sin(a0))));
                    pts.Add(new PointF(cx + (float)(rOuter * Math.Cos(a1)), cy + (float)(rOuter * Math.Sin(a1))));
                    pts.Add(new PointF(cx + (float)(rOuter * Math.Cos(a2)), cy + (float)(rOuter * Math.Sin(a2))));
                    pts.Add(new PointF(cx + (float)(rRoot * Math.Cos(a3)), cy + (float)(rRoot * Math.Sin(a3))));
                }
                path.AddPolygon(pts.ToArray());
                path.AddEllipse(cx - rHole, cy - rHole, rHole * 2, rHole * 2);

                using (SolidBrush brush = new SolidBrush(color))
                {
                    g.FillPath(brush, path);
                }
            }
        }

        private void DrawInfoIcon(Graphics g, Rectangle bounds, Color color)
        {
            float cx = bounds.X + bounds.Width / 2f;
            float cy = bounds.Y + bounds.Height / 2f;

            // 设定与相邻按钮 ➕、📶 视觉比重 100% 严格一致的圆环外径与线条粗细
            float r = Math.Min(bounds.Width, bounds.Height) * 0.235f; // ~9.0px (外径 ~18px)
            float stroke = Math.Max(1.8f, r * 0.22f); // ~2.0f 笔画粗细

            using (Pen circlePen = new Pen(color, stroke))
            {
                circlePen.Alignment = PenAlignment.Center;
                g.DrawEllipse(circlePen, cx - r, cy - r, r * 2f, r * 2f);
            }

            // 'i' 的上圆点
            float dotR = stroke * 0.65f; // 半径 ~1.3f (直径 ~2.6f)
            float dotY = cy - r * 0.44f;
            using (SolidBrush brush = new SolidBrush(color))
            {
                g.FillEllipse(brush, cx - dotR, dotY - dotR, dotR * 2f, dotR * 2f);

                // 'i' 的竖立主干
                float stemW = stroke * 1.15f; // 宽度 ~2.3f
                float stemTop = cy - r * 0.08f;
                float stemBottom = cy + r * 0.54f;
                float stemH = stemBottom - stemTop;
                RectangleF stemRect = new RectangleF(cx - stemW / 2f, stemTop, stemW, stemH);
                using (GraphicsPath stemPath = RoundedCard.GetRoundRectF(stemRect, stemW / 2f))
                {
                    g.FillPath(brush, stemPath);
                }
            }
        }

        private void DrawCloseIcon(Graphics g, Rectangle bounds, Color color)
        {
            float cx = bounds.X + bounds.Width / 2.0f;
            float cy = bounds.Y + bounds.Height / 2.0f;
            float dim = Math.Min(bounds.Width, bounds.Height);
            float half = Math.Max(3.6f, dim * 0.175f);
            float stroke = Math.Max(1.25f, dim * 0.052f);

            using (Pen pen = new Pen(color, stroke))
            {
                pen.StartCap = LineCap.Round;
                pen.EndCap = LineCap.Round;
                g.DrawLine(pen, cx - half, cy - half, cx + half, cy + half);
                g.DrawLine(pen, cx + half, cy - half, cx - half, cy + half);
            }
        }
    }
}
