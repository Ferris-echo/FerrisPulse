// ============================================================================
// 模块名称: RapooStatusTile.cs
// 所属区域: UI/Controls (复合展示控件层)
// 核心职责:
//   雷柏/高级外设状态小磁贴卡片，展示标题、右上角状态徽章、中心大号数值与单位、
//   以及底部细说明文本，自适应 DPI 缩放。
// 依赖关系: RoundedCard, Helpers.DpiScaleHelper
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
    public class RapooStatusTile : Control
    {
        public float DpiScale { get; set; }
        public string Title { get; set; }
        public string Badge { get; set; }
        public string ValueText { get; set; }
        public string UnitText { get; set; }
        public string Footnote { get; set; }
        public Color AccentColor { get; set; }

        public RapooStatusTile()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            DpiScale = 1.0f;
            AccentColor = Color.FromArgb(0, 210, 255);
            Title = "";
            Badge = "";
            ValueText = "--";
            UnitText = "";
            Footnote = "";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color parentBg = (Parent is RoundedCard) ? ((RoundedCard)Parent).CardColor : (Parent != null ? Parent.BackColor : Color.FromArgb(25, 27, 34));
            g.Clear(parentBg);

            int w = Width;
            int h = Height;
            int r = Math.Max(3, (int)(8 * DpiScale));

            // Background & subtle border
            using (var path = RoundedCard.GetRoundedRectangle(new Rectangle(0, 0, w - 1, h - 1), r))
            {
                using (var brush = new SolidBrush(Color.FromArgb(17, 20, 27)))
                {
                    g.FillPath(brush, path);
                }
                using (var pen = new Pen(Color.FromArgb(38, 43, 56), 1f))
                {
                    g.DrawPath(pen, path);
                }
            }

            // Top Header: Title (left) & Badge (right)
            int padX = (int)(14 * DpiScale);
            int topY = (int)(9 * DpiScale);
            using (var titleFont = DpiScaleHelper.CreateScaledFont("Microsoft YaHei UI", 8.2F, FontStyle.Regular, DpiScale))
            using (var titleBrush = new SolidBrush(Color.FromArgb(145, 155, 175)))
            {
                g.DrawString(Title, titleFont, titleBrush, padX, topY);
            }

            if (!string.IsNullOrEmpty(Badge))
            {
                using (var badgeFont = DpiScaleHelper.CreateScaledFont("Microsoft YaHei UI", 8F, FontStyle.Regular, DpiScale))
                {
                    var sz = TextRenderer.MeasureText(Badge, badgeFont);
                    int bX = w - padX - sz.Width - (int)(8 * DpiScale);
                    int bY = topY - (int)(1 * DpiScale);
                    int bW = sz.Width + (int)(8 * DpiScale);
                    int bH = sz.Height + (int)(2 * DpiScale);

                    using (var bPath = RoundedCard.GetRoundedRectangle(new Rectangle(bX, bY, bW, bH), Math.Max(2, (int)(4 * DpiScale))))
                    {
                        using (var bBrush = new SolidBrush(Color.FromArgb(28, AccentColor.R, AccentColor.G, AccentColor.B)))
                        {
                            g.FillPath(bBrush, bPath);
                        }
                        using (var bPen = new Pen(Color.FromArgb(70, AccentColor.R, AccentColor.G, AccentColor.B), 1f))
                        {
                            g.DrawPath(bPen, bPath);
                        }
                    }

                    using (var bTextBrush = new SolidBrush(AccentColor))
                    {
                        g.DrawString(Badge, badgeFont, bTextBrush, bX + (int)(4 * DpiScale), bY + (int)(1 * DpiScale));
                    }
                }
            }

            // Bottom Footnote / Hint
            int footY = Math.Max(topY + (int)(32 * DpiScale), h - (int)(18 * DpiScale));

            // Center: Big ValueText + UnitText - mathematically centered between Title and Footnote
            int valY = (topY + footY) / 2 - (int)(9 * DpiScale);
            using (var valFont = DpiScaleHelper.CreateScaledFont("Microsoft YaHei UI", 14.5F, FontStyle.Bold, DpiScale))
            using (var valBrush = new SolidBrush(AccentColor))
            {
                g.DrawString(ValueText, valFont, valBrush, padX - (int)(2 * DpiScale), valY);
                var valSz = TextRenderer.MeasureText(ValueText, valFont);

                if (!string.IsNullOrEmpty(UnitText))
                {
                    using (var unitFont = DpiScaleHelper.CreateScaledFont("Microsoft YaHei UI", 8.5F, FontStyle.Regular, DpiScale))
                    using (var unitBrush = new SolidBrush(Color.FromArgb(135, 145, 165)))
                    {
                        int unitX = padX + valSz.Width - (int)(6 * DpiScale);
                        int unitY = valY + (int)(5 * DpiScale);
                        g.DrawString(UnitText, unitFont, unitBrush, unitX, unitY);
                    }
                }
            }

            using (var footFont = DpiScaleHelper.CreateScaledFont("Microsoft YaHei UI", 7.6F, FontStyle.Regular, DpiScale))
            using (var footBrush = new SolidBrush(Color.FromArgb(115, 125, 145)))
            {
                g.DrawString(Footnote, footFont, footBrush, padX, footY);
            }
        }
    }
}
