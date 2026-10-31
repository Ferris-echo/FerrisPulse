// ============================================================================
// 模块名称: ModernDarkDropdown.cs
// 所属区域: UI/Controls (高级功能控件层)
// 核心职责:
//   现代化暗黑风下拉选择组合框，集成 AnimatedContextMenuStrip 与 ModernDarkMenuRenderer，
//   自适应高分屏 DPI 缩放，具备悬停高亮、展开箭头动态切换与项变更事件通知。
// 依赖关系: AnimatedContextMenuStrip, Theme.ModernDarkMenuRenderer, RoundedCard
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
    public class ModernDarkDropdown : Control
    {
        public float DpiScale = 1.0f;
        public List<string> Items = new List<string>();
        private int selectedIndex = 0;
        public int SelectedIndex
        {
            get { return selectedIndex; }
            set
            {
                if (value >= 0 && value < Items.Count && selectedIndex != value)
                {
                    selectedIndex = value;
                    Invalidate();
                    if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
                }
            }
        }
        public string SelectedItem { get { return (SelectedIndex >= 0 && SelectedIndex < Items.Count) ? Items[SelectedIndex] : ""; } }

        public event EventHandler SelectedIndexChanged;
        private bool isHovered = false;
        private bool isMenuOpen = false;
        private AnimatedContextMenuStrip activeMenu = null;

        public ModernDarkDropdown()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.FromArgb(18, 19, 23);
            ForeColor = Color.White;
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
        }

        protected override void OnMouseEnter(EventArgs e) { isHovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { isHovered = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (isMenuOpen)
            {
                if (activeMenu != null)
                {
                    activeMenu.Close();
                    activeMenu = null;
                }
                isMenuOpen = false;
                Invalidate();
                return;
            }
            ShowDropdownMenu();
        }

        private void ShowDropdownMenu()
        {
            if (Items.Count == 0) return;
            activeMenu = new AnimatedContextMenuStrip();
            activeMenu.Renderer = new ModernDarkMenuRenderer();
            DpiScaleHelper.SetControlBaseFont(activeMenu, "Microsoft YaHei UI", 9F, FontStyle.Regular, DpiScale);
            activeMenu.ShowImageMargin = false;
            activeMenu.ShowCheckMargin = false;
            activeMenu.MinimumSize = new Size(this.Width, 0);

            activeMenu.Closed += (s, ev) => {
                isMenuOpen = false;
                activeMenu = null;
                Invalidate();
            };

            for (int i = 0; i < Items.Count; i++)
            {
                int idx = i;
                string text = Items[i];
                var mi = new ToolStripMenuItem(text, null, (s, ev) => {
                    this.SelectedIndex = idx;
                });
                mi.AutoSize = false;
                mi.Height = (int)(28 * DpiScale);
                mi.Width = this.Width;
                mi.ForeColor = (idx == selectedIndex) ? Color.FromArgb(0, 230, 118) : Color.FromArgb(235, 240, 250);
                activeMenu.Items.Add(mi);
            }

            isMenuOpen = true;
            Invalidate();
            activeMenu.Show(this, new Point(0, this.Height + 2));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int r = (int)(6 * DpiScale);
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Color bg = (isHovered || isMenuOpen) ? Color.FromArgb(38, 43, 56) : Color.FromArgb(28, 32, 42);
            Color border = (isHovered || isMenuOpen) ? Color.FromArgb(0, 230, 118) : Color.FromArgb(50, 56, 72);

            using (var path = RoundedCard.GetRoundedRectangle(rect, r))
            {
                using (var brush = new SolidBrush(bg)) g.FillPath(brush, path);
                using (var pen = new Pen(border, isMenuOpen ? 1.5f : 1f)) g.DrawPath(pen, path);
            }

            string text = SelectedItem;
            using (var font = DpiScaleHelper.CreateScaledFont("Microsoft YaHei UI", 9F, FontStyle.Regular, DpiScale))
            {
                int textW = Width - (int)(32 * DpiScale);
                TextRenderer.DrawText(g, text, font, new Rectangle((int)(10 * DpiScale), 0, textW, Height),
                    Color.FromArgb(240, 245, 255), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }

            using (var chvFont = DpiScaleHelper.CreateScaledFont("Segoe UI", 8.5F, FontStyle.Bold, DpiScale))
            using (var chvBrush = new SolidBrush(isMenuOpen ? Color.FromArgb(0, 230, 118) : Color.FromArgb(160, 170, 195)))
            {
                string arrow = isMenuOpen ? "▴" : "▾";
                g.DrawString(arrow, chvFont, chvBrush, Width - (int)(20 * DpiScale), (Height / 2) - (int)(8 * DpiScale));
            }
        }
    }
}
