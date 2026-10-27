// ============================================================================
// 模块名称: HeadsetStatusTile.cs
// 所属区域: UI/Controls (复合展示控件层)
// 核心职责:
//   蓝牙/音频耳机设备状态卡片，展示连接状态、一键跳转 Windows 系统声音设置按钮，
//   自适应高分屏 DPI 缩放。
// 依赖关系: RoundedCard
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、属性或绘图逻辑时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（❌ 严禁 $"..." 插值，必须使用 string.Format 或 + 拼接）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class HeadsetStatusTile : Control
    {
        public float DpiScale { get; set; }
        public string Title { get; set; }
        public string StatusText { get; set; }
        public string Footnote { get; set; }
        public Color AccentColor { get; set; }

        public event Action ActionClicked;

        private Rectangle btnRect;
        private bool isBtnHovered = false;

        public HeadsetStatusTile()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            DpiScale = 1.0f;
            AccentColor = Color.FromArgb(90, 160, 255);
            Title = "蓝牙高质量音频 · Windows 原生 PnP 状态感知";
            StatusText = "已连接并处于就绪状态";
            Footnote = "支持一键打开系统声音面板切换默认输出端点";
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hover = btnRect.Contains(e.Location);
            if (hover != isBtnHovered)
            {
                isBtnHovered = hover;
                Cursor = isBtnHovered ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (isBtnHovered)
            {
                isBtnHovered = false;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (btnRect.Contains(e.Location))
            {
                if (ActionClicked != null) ActionClicked();
                else
                {
                    try { Process.Start("ms-settings:sound"); } catch (Exception diagEx) { Diagnostics.Log("UI/Controls/HeadsetStatusTile.cs: 已忽略异常", diagEx); }
                }
            }
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
            int r = (int)(8 * DpiScale);

            using (var path = RoundedCard.GetRoundedRectangle(new Rectangle(0, 0, w - 1, h - 1), r))
            {
                using (var brush = new SolidBrush(Color.FromArgb(17, 20, 27))) g.FillPath(brush, path);
                using (var pen = new Pen(Color.FromArgb(38, 43, 56), 1f)) g.DrawPath(pen, path);
            }

            int padX = (int)(14 * DpiScale);
            using (var titleFont = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Regular, GraphicsUnit.Point))
            using (var valFont = new Font("Microsoft YaHei UI", 10.2F, FontStyle.Bold, GraphicsUnit.Point))
            using (var footFont = new Font("Microsoft YaHei UI", 7.6F, FontStyle.Regular, GraphicsUnit.Point))
            {
                var sz1 = TextRenderer.MeasureText(Title, titleFont);
                var sz2 = TextRenderer.MeasureText("● " + StatusText, valFont);
                var sz3 = TextRenderer.MeasureText(Footnote, footFont);

                int gap1 = (int)(3.5f * DpiScale);
                int gap2 = (int)(3.5f * DpiScale);
                int totalTextH = sz1.Height + gap1 + sz2.Height + gap2 + sz3.Height;
                int startY = (h - totalTextH) / 2;

                int y1 = startY;
                int y2 = y1 + sz1.Height + gap1;
                int y3 = y2 + sz2.Height + gap2;

                using (var titleBrush = new SolidBrush(Color.FromArgb(145, 155, 175)))
                    g.DrawString(Title, titleFont, titleBrush, padX, y1);

                using (var valBrush = new SolidBrush(AccentColor))
                    g.DrawString("● " + StatusText, valFont, valBrush, padX, y2);

                using (var footBrush = new SolidBrush(Color.FromArgb(115, 125, 145)))
                    g.DrawString(Footnote, footFont, footBrush, padX, y3);

                // Action button on right
                int bW = (int)(138 * DpiScale);
                int bH = (int)(26 * DpiScale);
                int bX = w - bW - padX;
                int bY = (h - bH) / 2;
                btnRect = new Rectangle(bX, bY, bW, bH);

                Color btnBg = isBtnHovered ? Color.FromArgb(40, AccentColor.R, AccentColor.G, AccentColor.B) : Color.FromArgb(24, AccentColor.R, AccentColor.G, AccentColor.B);
                Color btnBorder = isBtnHovered ? AccentColor : Color.FromArgb(70, AccentColor.R, AccentColor.G, AccentColor.B);

                using (var bPath = RoundedCard.GetRoundedRectangle(btnRect, (int)(5 * DpiScale)))
                {
                    using (var bBrush = new SolidBrush(btnBg)) g.FillPath(bBrush, bPath);
                    using (var bPen = new Pen(btnBorder, 1f)) g.DrawPath(bPen, bPath);
                }

                using (var bFont = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Bold))
                {
                    TextRenderer.DrawText(g, "系统声音设置 ↗", bFont, btnRect, AccentColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }
            }
        }
    }
}
