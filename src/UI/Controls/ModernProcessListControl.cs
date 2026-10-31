// ============================================================================
// 模块名称: ModernProcessListControl.cs
// 所属区域: UI/Controls (高级功能控件层)
// 核心职责:
//   现代化暗黑圆角流体进程卡片列表控件，支持高清进程图标绘制、友好双行排版、
//   平滑鼠标滚轮滚动、现代微型滚动条、以及与设备优先级管理页完全统一的矢量级微型添加 [+] 与删除 [✕] 按钮。
// 依赖关系: RoundedCard, DpiScaleHelper
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑或字段时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Text;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public enum ProcessListActionType
    {
        Add,
        Delete
    }

    public class ProcessListItem
    {
        public string ProcessName { get; set; }
        public string Title { get; set; }
        public string Subtitle { get; set; }
        public Image Icon { get; set; }
        public object Tag { get; set; }

        public ProcessListItem() { }

        public ProcessListItem(string processName, string title, string subtitle, Image icon, object tag = null)
        {
            this.ProcessName = processName;
            this.Title = title;
            this.Subtitle = subtitle;
            this.Icon = icon;
            this.Tag = tag;
        }
    }

    public class ModernProcessListControl : Control
    {
        public float DpiScale { get; set; }
        public ProcessListActionType ActionType { get; set; }
        public string EmptyPlaceholderText { get; set; }

        public event Action<ProcessListItem> OnActionClicked;
        public event Action<ProcessListItem> OnItemDoubleClicked;
        public event Action<ProcessListItem> OnItemSelected;

        private List<ProcessListItem> items = new List<ProcessListItem>();
        private int hoveredIndex = -1;
        private int hoveredActionIndex = -1;
        private int selectedIndex = -1;
        private int scrollY = 0;
        private ToolTip itemToolTip;
        private int lastTooltipItemIndex = -2;

        private int S(int val) { return (int)Math.Round(val * DpiScale); }

        public ModernProcessListControl()
        {
            this.DpiScale = 1.0f;
            this.ActionType = ProcessListActionType.Add;
            this.EmptyPlaceholderText = "暂无数据";

            SetStyle(ControlStyles.UserPaint |
                     ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer |
                     ControlStyles.ResizeRedraw, true);

            this.BackColor = Color.FromArgb(16, 20, 28);
            this.Cursor = Cursors.Default;

            itemToolTip = new ToolTip();
            itemToolTip.AutoPopDelay = 8000;
            itemToolTip.InitialDelay = 350;
            itemToolTip.ReshowDelay = 150;
            itemToolTip.ShowAlways = true;
        }

        public void SetItems(IEnumerable<ProcessListItem> newItems)
        {
            items.Clear();
            if (newItems != null)
            {
                items.AddRange(newItems);
            }
            if (selectedIndex >= items.Count) selectedIndex = items.Count - 1;
            ClampScroll();
            Invalidate();
        }

        public List<ProcessListItem> GetItems()
        {
            return new List<ProcessListItem>(items);
        }

        public int ItemCount
        {
            get { return items.Count; }
        }

        public ProcessListItem SelectedItem
        {
            get
            {
                if (selectedIndex >= 0 && selectedIndex < items.Count)
                    return items[selectedIndex];
                return null;
            }
        }

        private int GetItemHeight() { return S(44); }
        private int GetItemGap() { return S(4); }
        private int GetTotalItemHeight() { return GetItemHeight() + GetItemGap(); }

        private int GetMaxScroll()
        {
            int totalH = items.Count * GetTotalItemHeight() + S(4);
            return Math.Max(0, totalH - Height);
        }

        private void ClampScroll()
        {
            int max = GetMaxScroll();
            if (scrollY > max) scrollY = max;
            if (scrollY < 0) scrollY = 0;
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int maxScroll = GetMaxScroll();
            if (maxScroll > 0)
            {
                scrollY -= Math.Sign(e.Delta) * S(44);
                ClampScroll();
                UpdateHoverState(e.Location);
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            UpdateHoverState(e.Location);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoveredIndex != -1 || hoveredActionIndex != -1)
            {
                hoveredIndex = -1;
                hoveredActionIndex = -1;
                lastTooltipItemIndex = -1;
                if (itemToolTip != null) itemToolTip.SetToolTip(this, null);
                Invalidate();
            }
        }

        private void UpdateHoverState(Point pt)
        {
            int newHover = -1;
            int newActionHover = -1;

            int itemH = GetItemHeight();
            int totalH = GetTotalItemHeight();
            int padX = S(6);
            int maxScroll = GetMaxScroll();
            int rightMargin = maxScroll > 0 ? S(10) : S(4);
            int cardW = Width - padX * 2 - rightMargin;
            int btnW = Math.Max(14, S(22));

            for (int i = 0; i < items.Count; i++)
            {
                int itemY = S(4) + i * totalH - scrollY;
                if (itemY + itemH < 0 || itemY > Height) continue;

                Rectangle itemRect = new Rectangle(padX, itemY, cardW, itemH);
                if (itemRect.Contains(pt))
                {
                    newHover = i;
                    int btnX = padX + cardW - btnW - S(8);
                    int btnY = itemY + (itemH - btnW) / 2;
                    Rectangle btnRect = new Rectangle(btnX, btnY, btnW, btnW);
                    if (btnRect.Contains(pt))
                    {
                        newActionHover = i;
                    }
                    break;
                }
            }

            if (newHover != hoveredIndex || newActionHover != hoveredActionIndex)
            {
                hoveredIndex = newHover;
                hoveredActionIndex = newActionHover;
                this.Cursor = (newActionHover != -1) ? Cursors.Hand : Cursors.Default;

                if (newHover != lastTooltipItemIndex)
                {
                    lastTooltipItemIndex = newHover;
                    if (newHover >= 0 && newHover < items.Count)
                    {
                        string tip = BuildItemTooltip(items[newHover]);
                        if (itemToolTip != null) itemToolTip.SetToolTip(this, tip);
                    }
                    else
                    {
                        if (itemToolTip != null) itemToolTip.SetToolTip(this, null);
                    }
                }

                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                int itemH = GetItemHeight();
                int totalH = GetTotalItemHeight();
                int padX = S(6);
                int maxScroll = GetMaxScroll();
                int rightMargin = maxScroll > 0 ? S(10) : S(4);
                int cardW = Width - padX * 2 - rightMargin;
                int btnW = Math.Max(14, S(22));

                for (int i = 0; i < items.Count; i++)
                {
                    int itemY = S(4) + i * totalH - scrollY;
                    if (itemY + itemH < 0 || itemY > Height) continue;

                    Rectangle itemRect = new Rectangle(padX, itemY, cardW, itemH);
                    if (itemRect.Contains(e.Location))
                    {
                        selectedIndex = i;
                        int btnX = padX + cardW - btnW - S(8);
                        int btnY = itemY + (itemH - btnW) / 2;
                        Rectangle btnRect = new Rectangle(btnX, btnY, btnW, btnW);

                        if (btnRect.Contains(e.Location))
                        {
                            if (OnActionClicked != null) OnActionClicked(items[i]);
                        }
                        else
                        {
                            if (OnItemSelected != null) OnItemSelected(items[i]);
                        }
                        Invalidate();
                        break;
                    }
                }
            }
        }

        protected override void OnMouseDoubleClick(MouseEventArgs e)
        {
            base.OnMouseDoubleClick(e);
            if (e.Button == MouseButtons.Left && selectedIndex >= 0 && selectedIndex < items.Count)
            {
                if (OnItemDoubleClicked != null) OnItemDoubleClicked(items[selectedIndex]);
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // 1. Draw Container Rounded Background & Border
            Rectangle bounds = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath basePath = RoundedCard.GetRoundedRectangle(bounds, S(8)))
            {
                using (SolidBrush bgBrush = new SolidBrush(BackColor))
                {
                    g.FillPath(bgBrush, basePath);
                }
                using (Pen borderPen = new Pen(Color.FromArgb(34, 42, 58), 1f))
                {
                    g.DrawPath(borderPen, basePath);
                }
            }

            // 2. Empty State
            if (items.Count == 0)
            {
                string emptyMsg = string.IsNullOrEmpty(EmptyPlaceholderText) ? "暂无数据" : EmptyPlaceholderText;
                using (Font f = DpiScaleHelper.CreateScaledFont("Microsoft YaHei UI", 8.5F, FontStyle.Regular, DpiScale))
                {
                    TextRenderer.DrawText(g, emptyMsg, f, bounds, Color.FromArgb(100, 115, 135),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.WordBreak);
                }
                return;
            }

            // 3. Render Items
            int maxScroll = GetMaxScroll();
            int rightMargin = maxScroll > 0 ? S(10) : S(4);
            int padX = S(6);
            int cardW = Width - padX * 2 - rightMargin;
            int itemH = GetItemHeight();
            int itemGap = GetItemGap();
            int totalH = itemH + itemGap;

            // Clip inner items
            GraphicsState clipState = g.Save();
            using (GraphicsPath innerClip = RoundedCard.GetRoundedRectangle(new Rectangle(1, 1, Width - 2, Height - 2), S(7)))
            {
                g.SetClip(innerClip);
            }

            for (int i = 0; i < items.Count; i++)
            {
                int itemY = S(4) + i * totalH - scrollY;
                if (itemY + itemH < 0 || itemY > Height) continue;

                ProcessListItem item = items[i];
                bool isHovered = (i == hoveredIndex);
                bool isSelected = (i == selectedIndex);
                bool isActionHovered = (i == hoveredActionIndex);

                Rectangle cardRect = new Rectangle(padX, itemY, cardW, itemH);
                int cardRadius = S(6);

                // Background & border
                Color cardBg;
                Color cardBorder;

                if (isSelected)
                {
                    cardBg = Color.FromArgb(28, 36, 50);
                    cardBorder = (ActionType == ProcessListActionType.Delete) ? Color.FromArgb(0, 200, 95) : Color.FromArgb(56, 189, 248);
                }
                else if (isHovered)
                {
                    cardBg = Color.FromArgb(24, 30, 42);
                    cardBorder = Color.FromArgb(46, 58, 80);
                }
                else
                {
                    cardBg = Color.FromArgb(19, 23, 32);
                    cardBorder = Color.FromArgb(30, 38, 52);
                }

                using (GraphicsPath cardPath = RoundedCard.GetRoundedRectangle(cardRect, cardRadius))
                {
                    using (SolidBrush cBrush = new SolidBrush(cardBg))
                    {
                        g.FillPath(cBrush, cardPath);
                    }
                    using (Pen cPen = new Pen(cardBorder, isSelected ? 1.5f : 1f))
                    {
                        g.DrawPath(cPen, cardPath);
                    }
                }

                // Left: 22x22 Icon
                int iconSize = Math.Max(16, S(22));
                int iconX = padX + S(8);
                int iconY = itemY + (itemH - iconSize) / 2;
                Rectangle iconRect = new Rectangle(iconX, iconY, iconSize, iconSize);

                if (item.Icon != null)
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.DrawImage(item.Icon, iconRect);
                }
                else
                {
                    // Fallback generic vector badge
                    using (GraphicsPath iconBgPath = RoundedCard.GetRoundedRectangle(iconRect, S(4)))
                    {
                        using (SolidBrush icoBg = new SolidBrush(Color.FromArgb(28, 36, 50)))
                        {
                            g.FillPath(icoBg, iconBgPath);
                        }
                    }
                    using (Font fontIco = DpiScaleHelper.CreateScaledFont("Segoe UI Emoji", 8.5F, FontStyle.Regular, DpiScale))
                    {
                        string fallbackEmoji = (ActionType == ProcessListActionType.Delete) ? "🎮" : "⚡";
                        TextRenderer.DrawText(g, fallbackEmoji, fontIco, iconRect, Color.FromArgb(160, 180, 200),
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                    }
                }

                // Right: Action Button [+] or [✕]
                int btnW = Math.Max(14, S(22));
                int btnX = padX + cardW - btnW - S(8);
                int btnY = itemY + (itemH - btnW) / 2;
                Rectangle btnRect = new Rectangle(btnX, btnY, btnW, btnW);

                DrawActionButton(g, btnX, btnY, btnW, isActionHovered);

                // Middle: Two-line Title & Subtitle
                int textX = iconX + iconSize + S(8);
                int maxTextW = btnX - textX - S(6);

                if (maxTextW > 20)
                {
                    string primaryText = item.Title ?? item.ProcessName ?? "";
                    string secondaryText = item.Subtitle ?? item.ProcessName ?? "";

                    Color primaryColor = (ActionType == ProcessListActionType.Delete)
                        ? Color.FromArgb(0, 230, 118)
                        : Color.FromArgb(242, 246, 252);

                    Color secondaryColor = Color.FromArgb(115, 130, 150);

                    using (Font fTitle = DpiScaleHelper.CreateScaledFont("Microsoft YaHei UI", 8.4F, FontStyle.Bold, DpiScale))
                    using (Font fSub = DpiScaleHelper.CreateScaledFont("Segoe UI", 7.4F, FontStyle.Regular, DpiScale))
                    {
                        Rectangle rectTitle = new Rectangle(textX, itemY + S(4), maxTextW, S(18));
                        Rectangle rectSub = new Rectangle(textX, itemY + S(22), maxTextW, S(16));

                        TextRenderer.DrawText(g, primaryText, fTitle, rectTitle, primaryColor,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.WordEllipsis);

                        TextRenderer.DrawText(g, secondaryText, fSub, rectSub, secondaryColor,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.WordEllipsis);
                    }
                }
            }

            g.Restore(clipState);

            // 4. Sleek Modern Minimalist Scrollbar
            if (maxScroll > 0)
            {
                int scrollTrackH = Height - S(16);
                int scrollTrackY = S(8);
                int scrollTrackX = Width - S(7);
                int barW = Math.Max(2, S(3));

                float ratio = (float)Height / (items.Count * GetTotalItemHeight());
                int thumbH = Math.Max(S(24), (int)(scrollTrackH * ratio));
                float thumbProgress = (float)scrollY / maxScroll;
                int thumbY = scrollTrackY + (int)((scrollTrackH - thumbH) * thumbProgress);

                Rectangle thumbRect = new Rectangle(scrollTrackX, thumbY, barW, thumbH);
                using (GraphicsPath tPath = RoundedCard.GetRoundedRectangle(thumbRect, barW / 2))
                {
                    using (SolidBrush tBrush = new SolidBrush(Color.FromArgb(60, 75, 100)))
                    {
                        g.FillPath(tBrush, tPath);
                    }
                }
            }
        }

        // ====================================================================
        // Sleek Pure Vector Action Button: Twin architecture with PriorityReorderControl
        // ====================================================================
        private void DrawActionButton(Graphics g, int btnX, int btnY, int btnW, bool isHovered)
        {
            Rectangle btnRect = new Rectangle(btnX, btnY, btnW, btnW);

            if (ActionType == ProcessListActionType.Delete)
            {
                // [✕] Delete Button: Exact twin from PriorityReorderControl.cs
                if (isHovered)
                {
                    using (GraphicsPath delPath = RoundedCard.GetRoundedRectangle(btnRect, Math.Max(2, S(5))))
                    using (SolidBrush delBg = new SolidBrush(Color.FromArgb(45, 255, 82, 82)))
                    {
                        g.FillPath(delBg, delPath);
                    }
                }

                Color xCol = isHovered ? Color.FromArgb(255, 100, 100) : Color.FromArgb(100, 115, 135);
                float penW = Math.Max(1.3f, 2.0f * DpiScale);
                using (Pen xPen = new Pen(xCol, penW))
                {
                    xPen.StartCap = LineCap.Round;
                    xPen.EndCap = LineCap.Round;
                    int cPad = Math.Max(3, S(6));
                    g.DrawLine(xPen, btnX + cPad, btnY + cPad, btnX + btnW - cPad, btnY + btnW - cPad);
                    g.DrawLine(xPen, btnX + btnW - cPad, btnY + cPad, btnX + cPad, btnY + btnW - cPad);
                }
            }
            else
            {
                // [+] Add Button: Exact twin architecture with green hover
                if (isHovered)
                {
                    using (GraphicsPath addPath = RoundedCard.GetRoundedRectangle(btnRect, Math.Max(2, S(5))))
                    using (SolidBrush addBg = new SolidBrush(Color.FromArgb(45, 0, 230, 118)))
                    {
                        g.FillPath(addBg, addPath);
                    }
                }

                Color plusCol = isHovered ? Color.FromArgb(0, 230, 118) : Color.FromArgb(100, 115, 135);
                float penW = Math.Max(1.3f, 2.0f * DpiScale);
                using (Pen plusPen = new Pen(plusCol, penW))
                {
                    plusPen.StartCap = LineCap.Round;
                    plusPen.EndCap = LineCap.Round;
                    int cPad = Math.Max(3, S(6));
                    int midX = btnX + btnW / 2;
                    int midY = btnY + btnW / 2;
                    g.DrawLine(plusPen, btnX + cPad, midY, btnX + btnW - cPad, midY);
                    g.DrawLine(plusPen, midX, btnY + cPad, midX, btnY + btnW - cPad);
                }
            }
        }
         private string BuildItemTooltip(ProcessListItem item)
        {
            if (item == null) return string.Empty;

            StringBuilder sb = new StringBuilder();
            if (!string.IsNullOrEmpty(item.Title))
            {
                sb.AppendLine(item.Title);
            }

            RunningProcessItem rp = item.Tag as RunningProcessItem;
            if (rp != null)
            {
                sb.Append("进程: ").Append(rp.ProcessName);
                if (rp.Pid > 0)
                {
                    sb.Append(" (PID: ").Append(rp.Pid).Append(")");
                }
                sb.AppendLine();
                if (!string.IsNullOrEmpty(rp.ExePath))
                {
                    sb.Append("路径: ").Append(rp.ExePath);
                }
            }
            else
            {
                if (!string.IsNullOrEmpty(item.ProcessName))
                {
                    sb.Append("进程: ").AppendLine(item.ProcessName);
                }
                if (!string.IsNullOrEmpty(item.Subtitle))
                {
                    sb.Append("状态: ").Append(item.Subtitle);
                }
            }
            return sb.ToString().TrimEnd();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (itemToolTip != null)
                {
                    itemToolTip.Dispose();
                    itemToolTip = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}