// ============================================================================
// 模块名称: ModernSegmentButton.cs
// 所属区域: UI/Controls (基础原子控件层)
// 核心职责:
//   分段按钮项（用于 DPI 档位选择、回报率选择等），具备选中/预设/悬停多种高光模式，支持长文本/双轴自适应缩放。
//   支持自定义品牌强调色（AccentColor）。
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
    public class ModernSegmentButton : Control
    {
        private bool isSelected = false;
        private bool isHovered = false;
        private bool isPreset = false;
        private Color accentColor = Color.FromArgb(0, 200, 83);

        public bool Selected
        {
            get { return isSelected; }
            set { if (isSelected != value) { isSelected = value; Invalidate(); } }
        }

        public bool IsPreset
        {
            get { return isPreset; }
            set { if (isPreset != value) { isPreset = value; Invalidate(); } }
        }

        public Color AccentColor
        {
            get { return accentColor; }
            set { if (accentColor != value) { accentColor = value; Invalidate(); } }
        }

        public ModernSegmentButton()
        {
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        // 只读态（设备固件不支持某项写入，例如回报率）：
        // 视觉上等同禁用（灰化、无 hover 高亮、无手型光标），但控件仍保持 Enabled=true，
        // 这样才能继续收到鼠标消息、由原生 ToolTip 在悬停时弹出说明、移开即消失。
        // 点击事件仍会派发，由业务层闸门拦截并给出提示，绝不下发硬件报文。
        private bool isReadOnly = false;
        public bool IsReadOnly
        {
            get { return isReadOnly; }
            set { if (isReadOnly != value) { isReadOnly = value; Invalidate(); } }
        }

        protected override void OnMouseEnter(EventArgs e) { if (!Enabled || isReadOnly) return; base.OnMouseEnter(e); isHovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { if (!Enabled || isReadOnly) return; base.OnMouseLeave(e); isHovered = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            base.OnPaint(pevent);
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color parentBg = (Parent is RoundedCard) ? ((RoundedCard)Parent).CardColor : (Parent != null ? Parent.BackColor : Color.FromArgb(25, 27, 34));
            g.Clear(parentBg);

            Color bg;
            Color border;
            Color fg;

            if (!Enabled || isReadOnly)
            {
                if (isSelected)
                {
                    bg = Color.FromArgb(20, 32, 24);
                    border = Color.FromArgb(35, 75, 45);
                    fg = Color.FromArgb(90, 140, 105);
                }
                else
                {
                    bg = Color.FromArgb(22, 24, 30);
                    border = Color.FromArgb(36, 40, 48);
                    fg = Color.FromArgb(80, 85, 95);
                }
            }
            else if (isSelected)
            {
                if (isPreset)
                {
                    bg = isHovered ? Color.FromArgb(24, 48, 32) : Color.FromArgb(18, 34, 24);
                    border = isHovered ? Color.FromArgb(0, 230, 118) : Color.FromArgb(0, 180, 80);
                    fg = isHovered ? Color.White : Color.FromArgb(140, 240, 180);
                }
                else
                {
                    bg = isHovered ? ControlPaint.Light(accentColor, 0.15f) : accentColor;
                    border = accentColor;
                    fg = Color.FromArgb(10, 24, 15);
                }
            }
            else
            {
                bg = isHovered ? Color.FromArgb(38, 42, 54) : Color.FromArgb(28, 30, 38);
                border = isHovered ? Color.FromArgb(80, 88, 108) : Color.FromArgb(48, 52, 65);
                fg = isHovered ? Color.White : Color.FromArgb(180, 188, 205);
            }

            float ratio = Math.Max(0.55f, Height > 0 ? Height / 28.0f : 1.0f);
            int cornerR = Math.Max(3, (int)Math.Round(6 * ratio));
            Rectangle rect = new Rectangle(0, 0, Math.Max(4, Width - 1), Math.Max(4, Height - 1));
            using (GraphicsPath path = RoundedCard.GetRoundedRectangle(rect, cornerR))
            {
                using (SolidBrush brush = new SolidBrush(bg))
                {
                    g.FillPath(brush, path);
                }
                using (Pen pen = new Pen(border, 1f))
                {
                    g.DrawPath(pen, path);
                }
            }

            Font useFont = (isSelected && Font.Style != FontStyle.Bold) ? new Font(Font, FontStyle.Bold) : Font;
            Font drawFont = useFont;
            Font tempFont = null;
            try
            {
                int safeW = Math.Max(8, Width - 4);
                Size sz = TextRenderer.MeasureText(g, Text, drawFont, new Size(int.MaxValue, int.MaxValue),
                    TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                if (sz.Width > safeW && drawFont.Size > 4.0f)
                {
                    float targetSize = drawFont.Size * ((float)safeW / (float)sz.Width);
                    if (targetSize < 4.0f) targetSize = 4.0f;
                    tempFont = new Font(drawFont.FontFamily, targetSize, drawFont.Style);
                    drawFont = tempFont;
                }

                TextRenderer.DrawText(g, Text, drawFont, ClientRectangle, fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }
            finally
            {
                if (tempFont != null) tempFont.Dispose();
                if (useFont != null && useFont != Font) useFont.Dispose();
            }
        }
    }
}
