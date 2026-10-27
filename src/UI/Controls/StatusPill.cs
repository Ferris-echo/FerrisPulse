// ============================================================================
// 模块名称: StatusPill.cs
// 所属区域: UI/Controls (基础原子控件层)
// 核心职责:
//   现代化胶囊型状态药丸标签，用于展示连接/充电状态（如 "2.4G 无线"、"⚡ 充电" 等），
//   具备根据文本宽度自适应伸缩、抗锯齿半透明药丸背景与边缘勾边。
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
    public class StatusPill : Control
    {
        private string statusText = "检测中...";
        private Color statusColor = Color.FromArgb(0, 230, 118);

        public void SetStatus(string text, Color color)
        {
            this.statusText = text ?? "";
            this.Text = this.statusText;
            // 有真实状态即应展示（初始状态可能被显式隐藏，避免展示占位文案）
            if (!this.Visible) this.Visible = true;
            this.statusColor = color;
            try
            {
                using (var g = CreateGraphics())
                {
                    var sz = TextRenderer.MeasureText(g, this.statusText, Font);
                    this.Width = Math.Max(78, sz.Width + 28);
                }
            }
            catch
            {
                this.Width = 78;
            }
            Invalidate();
        }

        public bool IsHardwareBadge { get; set; }

        public StatusPill()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color parentBg = (Parent is RoundedCard) ? ((RoundedCard)Parent).CardColor : (Parent != null ? Parent.BackColor : Color.FromArgb(25, 27, 34));
            g.Clear(parentBg);

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            int cornerRadius = 6;

            if (IsHardwareBadge)
            {
                using (GraphicsPath path = RoundedCard.GetRoundedRectangle(rect, cornerRadius))
                {
                    // 1. 钛合金/暗黑碳晶微光渐变底色
                    using (LinearGradientBrush bgBrush = new LinearGradientBrush(
                        rect,
                        Color.FromArgb(30, 35, 46),
                        Color.FromArgb(18, 21, 28),
                        LinearGradientMode.Vertical))
                    {
                        g.FillPath(bgBrush, path);
                    }

                    // 2. 顶部微光高光反光线 (高雅金属质感)
                    using (Pen highlightPen = new Pen(Color.FromArgb(50, 255, 255, 255), 1f))
                    {
                        g.DrawLine(highlightPen, rect.Left + cornerRadius, rect.Top + 1, rect.Right - cornerRadius, rect.Top + 1);
                    }

                    // 3. 霓虹呼吸外围细线
                    using (Pen borderPen = new Pen(Color.FromArgb(130, statusColor.R, statusColor.G, statusColor.B), 1.2f))
                    {
                        g.DrawPath(borderPen, path);
                    }
                }

                // 4. 左侧微型发光微晶指示灯 (Micro Glow Dot)
                int dotX = 10;
                int dotY = Height / 2;
                int dotRadius = 2;

                using (SolidBrush haloBrush = new SolidBrush(Color.FromArgb(60, statusColor.R, statusColor.G, statusColor.B)))
                {
                    g.FillEllipse(haloBrush, dotX - 4, dotY - 4, 8, 8);
                }
                using (SolidBrush dotBrush = new SolidBrush(statusColor))
                {
                    g.FillEllipse(dotBrush, dotX - dotRadius, dotY - dotRadius, dotRadius * 2, dotRadius * 2);
                }

                // 5. 纯净抗锯齿文字排印 (向右避开微晶指示灯)
                Rectangle textRect = new Rectangle(dotX + 6, 0, Width - dotX - 10, Height);
                TextRenderer.DrawText(g, statusText, Font, textRect, Color.FromArgb(238, 244, 255),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            else
            {
                // 默认形态：主界面等区域最初的高级清爽微透胶囊（扁平优雅、中心对称）
                using (GraphicsPath path = RoundedCard.GetRoundedRectangle(rect, cornerRadius))
                {
                    using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(24, statusColor.R, statusColor.G, statusColor.B)))
                    {
                        g.FillPath(bgBrush, path);
                    }

                    using (Pen borderPen = new Pen(Color.FromArgb(85, statusColor.R, statusColor.G, statusColor.B), 1f))
                    {
                        g.DrawPath(borderPen, path);
                    }
                }

                TextRenderer.DrawText(g, statusText, Font, rect, statusColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }
    }
}
