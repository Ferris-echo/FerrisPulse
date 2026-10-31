// ============================================================================
// 模块名称: FocusDeviceSelector.cs
// 所属区域: UI/Controls (复合展示控件层)
// 核心职责:
//   主界面焦点设备选择胶囊卡片，集成现代矢量设备图标、设备型号短名（抗裁切截断）、
//   电量药丸胶囊与下拉展开箭头，支持点击弹出设备切换飞出面板与无极等比缩放。
// 依赖关系: Models.DeviceCategory, Theme.ModernThemeHelper, RoundedCard, Helpers.DpiScaleHelper
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
    public class FocusDeviceSelector : Control
    {
        public float DpiScale { get; set; }
        public string DeviceIcon { get; set; }
        public DeviceCategory Category { get; set; }
        public string DeviceName { get; set; }
        public string BatteryText { get; set; }
        public string StatusText { get; set; }
        public Color AccentColor { get; set; }
        public bool IsHovered { get; set; }

        public event Action SelectorClicked;

        public FocusDeviceSelector()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(18, 19, 23);
            DpiScale = 1.0f;
            DeviceIcon = "🖱️";
            Category = DeviceCategory.Mouse;
            DeviceName = "未连接设备";
            BatteryText = "--%";
            StatusText = "未连接";
            AccentColor = Color.FromArgb(0, 230, 118);
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            IsHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            IsHovered = false;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (SelectorClicked != null) SelectorClicked();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(BackColor);

            int w = Width;
            int h = Height;
            int r = Math.Max(3, (int)(8 * DpiScale));

            Color bg = IsHovered ? Color.FromArgb(32, 36, 46) : Color.FromArgb(25, 27, 34);
            Color border = IsHovered ? AccentColor : Color.FromArgb(45, 50, 65);

            using (var path = RoundedCard.GetRoundedRectangle(new Rectangle(0, 0, w - 1, h - 1), r))
            {
                using (var brush = new SolidBrush(bg)) g.FillPath(brush, path);
                using (var pen = new Pen(border, IsHovered ? 1.5f : 1.0f)) g.DrawPath(pen, path);
            }

            // 1. Icon (left) - Vector centered!
            int iconBoxSize = (int)(22 * DpiScale);
            int iconX = (int)(10 * DpiScale);
            int iconY = (h - iconBoxSize) / 2;
            Rectangle iconRect = new Rectangle(iconX, iconY, iconBoxSize, iconBoxSize);
            ModernThemeHelper.DrawModernDeviceIcon(g, iconRect, Category, AccentColor);

            // 2. Chevron "▾" (far right)
            int chvW = (int)(14 * DpiScale);
            int chvX = w - (int)(14 * DpiScale);
            using (var chvFont = DpiScaleHelper.CreateScaledFont("Segoe UI", 9F, FontStyle.Bold, DpiScale))
            {
                Rectangle chvRect = new Rectangle(chvX - chvW, 0, chvW + (int)(4 * DpiScale), h);
                TextRenderer.DrawText(g, "▾", chvFont, chvRect, Color.FromArgb(150, 160, 180),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }

            // 3. Battery Pill
            int pW = 0;
            using (var battFont = DpiScaleHelper.CreateScaledFont("Microsoft YaHei UI", 8.2F, FontStyle.Bold, DpiScale))
            {
                string bStr = BatteryText ?? "";
                var bSz = TextRenderer.MeasureText(bStr, battFont, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
                pW = bSz.Width + Math.Max(8, (int)(12 * DpiScale));
                int pH = Math.Max(12, (int)(20 * DpiScale));
                int pX = chvX - chvW - pW - Math.Max(4, (int)(6 * DpiScale));
                int pY = (h - pH) / 2;
                Rectangle pillRect = new Rectangle(pX, pY, pW, pH);

                using (var pPath = RoundedCard.GetRoundedRectangle(pillRect, Math.Max(2, (int)(4 * DpiScale))))
                {
                    using (var pBrush = new SolidBrush(Color.FromArgb(30, AccentColor.R, AccentColor.G, AccentColor.B)))
                    {
                        g.FillPath(pBrush, pPath);
                    }
                    using (var pPen = new Pen(Color.FromArgb(80, AccentColor.R, AccentColor.G, AccentColor.B), 1f))
                    {
                        g.DrawPath(pPen, pPath);
                    }
                }

                TextRenderer.DrawText(g, bStr, battFont, pillRect, AccentColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }

            // 4. Device Name (truncated if necessary)
            int nameX = iconX + (int)(24 * DpiScale);
            int maxNameW = chvX - chvW - pW - nameX - (int)(12 * DpiScale);
            if (maxNameW < 30) maxNameW = 30;

            using (var nameFont = DpiScaleHelper.CreateScaledFont("Microsoft YaHei UI", 9.2F, FontStyle.Bold, DpiScale))
            {
                Rectangle nameRect = new Rectangle(nameX, 0, maxNameW, h);
                TextRenderer.DrawText(g, DeviceName ?? "", nameFont, nameRect, Color.FromArgb(240, 243, 250),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
        }
    }
}
