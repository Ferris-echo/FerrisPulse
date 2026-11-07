// ============================================================================
// 模块名称: ModernDarkScrollPanel.cs
// 所属区域: UI/Controls (基础原子控件层)
// 核心职责:
//   现代化暗黑微晶滚动容器，彻底废除 Win32 原生粗糙白色横向/竖向滚动条，
//   提供纯 GDI+ 双缓冲矢量纤细圆角悬浮滚动条、平滑鼠标滚轮交互与自适应换行。
//   原生集成直接文本双缓冲绘制引擎 (Direct GDI+ Double-Buffered Rendering)，
//   彻底杜绝 Windows Forms 在 WS_CLIPCHILDREN 容器内多透明 Label 滚动时发生的残影、文字撕裂与重叠覆盖缺陷。
// 依赖关系: ModernThemeHelper
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
    public class ModernDarkScrollPanel : Panel
    {
        private class ChangelogVisualItem
        {
            public string Text;
            public bool IsHeader;
            public Font Font;
            public Color Color;
            public int Top;
            public int Height;
        }

        private List<ChangelogVisualItem> changelogItems;
        private int totalContentHeight = 0;
        private float currentDpiScale = 1.0f;
        private IList<string> rawChangelogLines;

        private Panel contentPanel;
        private int scrollOffset = 0;
        private bool isDraggingThumb = false;
        private int dragStartY = 0;
        private int dragStartOffset = 0;
        private bool isThumbHovered = false;

        private int scrollBarWidth = 6;
        private int scrollBarMargin = 3;

        private Color thumbColorNormal = Color.FromArgb(60, 68, 86);
        private Color thumbColorHover = Color.FromArgb(90, 102, 130);
        private Color thumbColorDrag = Color.FromArgb(0, 200, 83);
        private Color trackColor = Color.FromArgb(18, 20, 26);

        public Panel Content
        {
            get { return contentPanel; }
        }

        public int ScrollOffset
        {
            get { return scrollOffset; }
            set
            {
                int max = GetMaxScroll();
                int newVal = Math.Max(0, Math.Min(max, value));
                if (scrollOffset != newVal)
                {
                    scrollOffset = newVal;
                    if (contentPanel != null && contentPanel.Visible)
                    {
                        contentPanel.Top = -scrollOffset;
                    }
                    Invalidate();
                }
            }
        }

        public ModernDarkScrollPanel()
        {
            this.AutoScroll = false;
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.BackColor = Color.FromArgb(25, 27, 34);

            contentPanel = new Panel();
            contentPanel.Location = new Point(0, 0);
            contentPanel.BackColor = Color.FromArgb(25, 27, 34);
            contentPanel.AutoSize = false;
            contentPanel.Visible = false; // 默认使用纯双缓冲绘制引擎，消除子控件移动残影
            this.Controls.Add(contentPanel);

            this.MouseWheel += OnPanelMouseWheel;
            contentPanel.MouseWheel += OnPanelMouseWheel;
            contentPanel.ControlAdded += OnChildAdded;
        }

        private void OnChildAdded(object sender, ControlEventArgs e)
        {
            if (e.Control != null)
            {
                contentPanel.Visible = true;
                HookChildMouseWheel(e.Control);
            }
        }

        public void HookChildMouseWheel(Control ctrl)
        {
            if (ctrl == null) return;
            ctrl.MouseWheel -= OnPanelMouseWheel;
            ctrl.MouseWheel += OnPanelMouseWheel;
            foreach (Control sub in ctrl.Controls)
            {
                HookChildMouseWheel(sub);
            }
        }

        public void SetChangelog(IList<string> lines, float dpiScale)
        {
            this.rawChangelogLines = lines;
            this.currentDpiScale = (dpiScale > 0.1f) ? dpiScale : 1.0f;

            if (contentPanel != null)
            {
                contentPanel.Controls.Clear();
                contentPanel.Visible = false;
            }

            RebuildChangelogItems();
            this.scrollOffset = 0;
            Invalidate();
        }

        private void RebuildChangelogItems()
        {
            IList<string> lines = this.rawChangelogLines;
            if (lines == null || lines.Count == 0)
            {
                lines = new List<string>() { "•  常规性能提升与稳定性修复" };
            }

            List<string> displayItems = new List<string>();
            for (int i = 0; i < lines.Count; i++)
            {
                string entry = lines[i];
                if (string.IsNullOrEmpty(entry)) continue;

                if (entry.Contains("【") && entry.IndexOf("【") != entry.LastIndexOf("【"))
                {
                    string[] parts = System.Text.RegularExpressions.Regex.Split(entry, @"(?=【)");
                    foreach (string p in parts)
                    {
                        string cp = p.Trim().TrimEnd(',', '，', ';', '；');
                        if (!string.IsNullOrEmpty(cp)) displayItems.Add(cp);
                    }
                }
                else
                {
                    displayItems.Add(entry);
                }
            }

            this.Text = string.Join(" | ", displayItems.ToArray());

            int availW = this.ClientSize.Width - scrollBarWidth - (scrollBarMargin * 2) - 12;
            if (availW < 50) availW = 400;

            Font headerFont = new Font("Microsoft YaHei UI", 9.2F * currentDpiScale, FontStyle.Bold);
            Font bodyFont = new Font("Microsoft YaHei UI", 9.0F * currentDpiScale, FontStyle.Regular);

            int curY = 4;
            List<ChangelogVisualItem> list = new List<ChangelogVisualItem>();

            for (int i = 0; i < displayItems.Count; i++)
            {
                string raw = displayItems[i];
                bool isHeader = raw.StartsWith("一、") || raw.StartsWith("二、") || raw.StartsWith("三、") ||
                                raw.StartsWith("四、") || raw.StartsWith("五、") || raw.StartsWith("六、") ||
                                raw.StartsWith("特别说明") || raw.StartsWith("特别鸣谢") ||
                                raw.StartsWith("升级提醒") || raw.StartsWith("后续适配");

                string drawText = isHeader ? raw : ("•  " + raw);
                Font font = isHeader ? headerFont : bodyFont;
                Color color = isHeader ? Color.FromArgb(240, 245, 255) : Color.FromArgb(195, 203, 220);

                if (isHeader && i > 0) curY += (int)(6 * currentDpiScale);

                Size measured = TextRenderer.MeasureText(drawText, font, new Size(availW, 0),
                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                int itemH = Math.Max((int)(24 * currentDpiScale), measured.Height + 4);

                ChangelogVisualItem item = new ChangelogVisualItem();
                item.Text = drawText;
                item.IsHeader = isHeader;
                item.Font = font;
                item.Color = color;
                item.Top = curY;
                item.Height = itemH;
                list.Add(item);

                curY += itemH + (int)(6 * currentDpiScale);
            }

            this.changelogItems = list;
            this.totalContentHeight = curY + 8;
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            if (changelogItems != null && changelogItems.Count > 0)
            {
                RebuildChangelogItems();
            }
            UpdateContentLayout();
        }

        public void UpdateContentLayout()
        {
            if (contentPanel == null || !contentPanel.Visible) return;

            int availW = this.ClientSize.Width - scrollBarWidth - (scrollBarMargin * 2);
            if (availW < 10) availW = 10;
            contentPanel.Width = availW;

            int totalH = 0;
            foreach (Control c in contentPanel.Controls)
            {
                if (c.Visible)
                {
                    int bottom = c.Top + c.Height;
                    if (bottom > totalH) totalH = bottom;
                }
            }
            contentPanel.Height = totalH;

            int max = GetMaxScroll();
            if (scrollOffset > max)
            {
                ScrollOffset = max;
            }
            else
            {
                contentPanel.Top = -scrollOffset;
            }
            Invalidate();
        }

        private int GetMaxScroll()
        {
            int contentH = (changelogItems != null && changelogItems.Count > 0) ? totalContentHeight : (contentPanel != null ? contentPanel.Height : 0);
            int max = contentH - this.ClientSize.Height;
            return max > 0 ? max : 0;
        }

        private void OnPanelMouseWheel(object sender, MouseEventArgs e)
        {
            int max = GetMaxScroll();
            if (max <= 0) return;

            int delta = e.Delta > 0 ? -36 : 36;
            ScrollOffset += delta;
        }

        private Rectangle GetThumbRectangle()
        {
            int max = GetMaxScroll();
            if (max <= 0 || this.ClientSize.Height <= 0) return Rectangle.Empty;

            int clientH = this.ClientSize.Height;
            int contentH = (changelogItems != null && changelogItems.Count > 0) ? totalContentHeight : (contentPanel != null ? contentPanel.Height : 0);
            if (contentH <= clientH) return Rectangle.Empty;

            float ratio = (float)clientH / (float)contentH;
            int thumbH = (int)(clientH * ratio);
            if (thumbH < 24) thumbH = 24;
            if (thumbH > clientH - 8) thumbH = clientH - 8;

            int trackH = clientH - thumbH;
            int thumbY = (int)((float)scrollOffset / (float)max * trackH);
            int thumbX = this.ClientSize.Width - scrollBarWidth - scrollBarMargin;

            return new Rectangle(thumbX, thumbY, scrollBarWidth, thumbH);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);

            Graphics g = e.Graphics;
            g.Clear(this.BackColor);
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int clientW = this.ClientSize.Width;
            int clientH = this.ClientSize.Height;
            int availW = clientW - scrollBarWidth - (scrollBarMargin * 2) - 12;
            if (availW < 10) availW = 10;

            // 1. 绘制日志条目（硬件级内存双缓冲，每次滚动先全清底色，绝不产生残影、重叠与水平撕裂）
            if (changelogItems != null && changelogItems.Count > 0)
            {
                for (int i = 0; i < changelogItems.Count; i++)
                {
                    ChangelogVisualItem item = changelogItems[i];
                    int drawY = item.Top - scrollOffset;

                    // 视口剔除：仅绘制位于当前可视窗口内的条目
                    if (drawY + item.Height >= 0 && drawY <= clientH)
                    {
                        Rectangle rect = new Rectangle(4, drawY, availW, item.Height);
                        TextRenderer.DrawText(g, item.Text, item.Font, rect, item.Color,
                            TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                    }
                }
            }

            // 2. 绘制滚动条
            int max = GetMaxScroll();
            if (max <= 0) return;

            int trackX = clientW - scrollBarWidth - scrollBarMargin;

            using (SolidBrush trackBrush = new SolidBrush(trackColor))
            {
                using (GraphicsPath trackPath = ModernThemeHelper.GetRoundedRectangle(new Rectangle(trackX, 2, scrollBarWidth, clientH - 4), scrollBarWidth / 2))
                {
                    g.FillPath(trackBrush, trackPath);
                }
            }

            Rectangle thumbRect = GetThumbRectangle();
            if (thumbRect.IsEmpty) return;

            Color thumbColor = thumbColorNormal;
            if (isDraggingThumb) thumbColor = thumbColorDrag;
            else if (isThumbHovered) thumbColor = thumbColorHover;

            using (SolidBrush thumbBrush = new SolidBrush(thumbColor))
            {
                using (GraphicsPath thumbPath = ModernThemeHelper.GetRoundedRectangle(thumbRect, scrollBarWidth / 2))
                {
                    g.FillPath(thumbBrush, thumbPath);
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            Rectangle thumbRect = GetThumbRectangle();
            bool hovered = (!thumbRect.IsEmpty && thumbRect.Contains(e.Location));
            if (isThumbHovered != hovered)
            {
                isThumbHovered = hovered;
                Invalidate();
            }

            if (isDraggingThumb)
            {
                int max = GetMaxScroll();
                int clientH = this.ClientSize.Height;
                int thumbH = thumbRect.Height;
                int trackH = clientH - thumbH;
                if (trackH > 0 && max > 0)
                {
                    int deltaY = e.Y - dragStartY;
                    float deltaRatio = (float)deltaY / (float)trackH;
                    int newOffset = dragStartOffset + (int)(deltaRatio * max);
                    ScrollOffset = newOffset;
                }
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                Rectangle thumbRect = GetThumbRectangle();
                if (!thumbRect.IsEmpty && thumbRect.Contains(e.Location))
                {
                    isDraggingThumb = true;
                    dragStartY = e.Y;
                    dragStartOffset = scrollOffset;
                    Invalidate();
                }
                else
                {
                    int trackX = this.ClientSize.Width - scrollBarWidth - (scrollBarMargin * 2);
                    if (e.X >= trackX)
                    {
                        int max = GetMaxScroll();
                        int clientH = this.ClientSize.Height;
                        if (clientH > 0)
                        {
                            float clickRatio = (float)e.Y / (float)clientH;
                            ScrollOffset = (int)(clickRatio * max);
                        }
                    }
                }
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (isDraggingThumb)
            {
                isDraggingThumb = false;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (isThumbHovered)
            {
                isThumbHovered = false;
                Invalidate();
            }
        }
    }
}
