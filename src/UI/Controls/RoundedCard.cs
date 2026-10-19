// ============================================================================
// 模块名称: RoundedCard.cs
// 所属区域: UI/Controls (基础原子控件层)
// 核心职责:
//   现代化深色亚克力质感圆角卡片面板容器，原生双缓冲，支持自定义圆角、背景色及微光边框。
//   原生启用 WS_CLIPCHILDREN 与 WS_CLIPSIBLINGS 硬件级窗口剪裁，杜绝折叠动画期间子控件画面溢出与撕裂。
//   提供全系统通用的静态 GetRoundedRectangle / GetRoundRectF 几何切角路径生成工具。
// 依赖关系: 无
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
    public class RoundedCard : Panel
    {
        private Color cardColor = Color.FromArgb(25, 27, 34);
        public Color CardColor
        {
            get { return cardColor; }
            set { cardColor = value; base.BackColor = value; Invalidate(); }
        }

        public override Color BackColor
        {
            get { return cardColor; }
            set
            {
                cardColor = value;
                base.BackColor = value;
                Invalidate();
            }
        }

        public Color BorderColor { get; set; }
        public int CornerRadius { get; set; }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x02000000; // WS_CLIPCHILDREN
                cp.Style |= 0x04000000; // WS_CLIPSIBLINGS
                return cp;
            }
        }

        public RoundedCard()
        {
            cardColor = Color.FromArgb(25, 27, 34);
            BorderColor = Color.FromArgb(42, 46, 58);
            CornerRadius = 10;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            base.BackColor = cardColor;
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            // 保持 base.BackColor 与 cardColor 一致，确保内部 WinForms 子控件 (Label等) 继承无色差底色
            base.BackColor = cardColor;
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            Invalidate();
        }

        private Color GetEffectiveParentBackColor()
        {
            Control p = this.Parent;
            while (p != null)
            {
                if (p.BackColor != Color.Transparent && p.BackColor.A == 255)
                {
                    return p.BackColor;
                }
                p = p.Parent;
            }
            return Color.FromArgb(18, 19, 23);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color parentBg = GetEffectiveParentBackColor();
            g.Clear(parentBg);

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = GetRoundedRectangle(rect, CornerRadius))
            {
                using (SolidBrush brush = new SolidBrush(cardColor))
                {
                    g.FillPath(brush, path);
                }
            }
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color parentBg = GetEffectiveParentBackColor();
            g.Clear(parentBg);

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = GetRoundedRectangle(rect, CornerRadius))
            {
                using (SolidBrush brush = new SolidBrush(cardColor))
                {
                    g.FillPath(brush, path);
                }

                if (BorderColor != Color.Transparent)
                {
                    using (Pen pen = new Pen(BorderColor, 1f))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }

            base.OnPaint(pevent);
        }

        public static GraphicsPath GetRoundedRectangle(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            if (d > bounds.Width) d = bounds.Width;
            if (d > bounds.Height) d = bounds.Height;
            if (d <= 0) d = 1;

            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static GraphicsPath GetRoundRectF(RectangleF bounds, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }
}
