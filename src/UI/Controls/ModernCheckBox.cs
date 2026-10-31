// ============================================================================
// 模块名称: ModernCheckBox.cs
// 所属区域: UI/Controls (基础原子控件层)
// 核心职责:
//   现代化深色暗黑风复选框，具备鼠标平滑高亮悬停、圆角方框及矢量平滑对勾绘制，
//   勾选框与文本间距跟随控件字体高度等比无极缩放，彻底杜绝高行高下勾选框过度膨胀遮挡文字。
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
using System.Drawing.Text;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class ModernCheckBox : Control
    {
        private bool isChecked = false;
        private bool isHovered = false;

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return isChecked; }
            set
            {
                if (isChecked != value)
                {
                    isChecked = value;
                    Invalidate();
                    if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
                }
            }
        }

        public ModernCheckBox()
        {
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { if (!Enabled) return; base.OnMouseEnter(e); isHovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { if (!Enabled) return; base.OnMouseLeave(e); isHovered = false; Invalidate(); }
        protected override void OnClick(EventArgs e) { if (!Enabled) return; Checked = !Checked; base.OnClick(e); }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            base.OnPaint(pevent);
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color parentBg = (Parent is RoundedCard) ? ((RoundedCard)Parent).CardColor : (Parent != null ? Parent.BackColor : Color.FromArgb(25, 27, 34));
            g.Clear(parentBg);

            float ratio = Math.Max(0.45f, Font != null ? Font.Height / 15.5f : (Height > 0 ? Height / 24.0f : 1.0f));
            int boxSize = Math.Max(8, (int)Math.Round(14 * ratio));
            if (Height > 4 && boxSize > Height - 2) boxSize = Height - 2;
            int boxY = (Height - boxSize) / 2;
            Rectangle boxRect = new Rectangle(1, boxY, boxSize, boxSize);

            Color boxBg;
            Color boxBorder;
            Color textCol;

            if (!Enabled)
            {
                boxBg = isChecked ? Color.FromArgb(20, 36, 26) : Color.FromArgb(22, 24, 30);
                boxBorder = isChecked ? Color.FromArgb(35, 75, 45) : Color.FromArgb(36, 40, 48);
                textCol = Color.FromArgb(100, 108, 122);
            }
            else
            {
                boxBg = isChecked ? (isHovered ? Color.FromArgb(0, 230, 118) : Color.FromArgb(0, 200, 83)) : (isHovered ? Color.FromArgb(38, 42, 54) : Color.FromArgb(28, 30, 38));
                boxBorder = isChecked ? Color.FromArgb(0, 230, 118) : (isHovered ? Color.FromArgb(90, 98, 120) : Color.FromArgb(58, 63, 78));
                textCol = isHovered ? Color.White : (ForeColor != Control.DefaultForeColor && ForeColor != Color.Empty ? ForeColor : Color.FromArgb(220, 226, 238));
            }

            int cornerR = Math.Max(2, (int)Math.Round(3.5f * ratio));
            using (GraphicsPath path = RoundedCard.GetRoundedRectangle(boxRect, cornerR))
            {
                using (SolidBrush brush = new SolidBrush(boxBg))
                {
                    g.FillPath(brush, path);
                }
                using (Pen pen = new Pen(boxBorder, 1f))
                {
                    g.DrawPath(pen, path);
                }
            }

            if (isChecked)
            {
                float bScale = boxSize / 16.0f;
                Color checkPenColor = !Enabled ? Color.FromArgb(70, 120, 85) : Color.FromArgb(10, 24, 15);
                using (Pen checkPen = new Pen(checkPenColor, Math.Max(1.1f, 2.0f * bScale)))
                {
                    checkPen.StartCap = LineCap.Round;
                    checkPen.EndCap = LineCap.Round;
                    PointF[] checkPoints = new PointF[]
                    {
                        new PointF(boxRect.Left + 3.5f * bScale, boxRect.Top + 8.5f * bScale),
                        new PointF(boxRect.Left + 6.5f * bScale, boxRect.Top + 11.5f * bScale),
                        new PointF(boxRect.Left + 12.5f * bScale, boxRect.Top + 4.5f * bScale)
                    };
                    g.DrawLines(checkPen, checkPoints);
                }
            }

            int textX = boxRect.Right + Math.Max(3, (int)Math.Round(5 * ratio));
            Rectangle textRect = new Rectangle(textX, 0, Math.Max(4, Width - textX), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, textCol,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
        }
    }
}
