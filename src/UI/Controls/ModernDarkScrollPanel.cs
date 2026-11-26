// ============================================================================
// 模块名称: ModernDarkScrollPanel.cs
// 所属区域: UI/Controls (基础原子控件层)
// 核心职责:
//   现代化暗黑微晶滚动容器，彻底废除 Win32 原生粗糙白色横向/竖向滚动条，
//   提供纯 GDI+ 双缓冲矢量纤细圆角悬浮滚动条、平滑鼠标滚轮交互与自适应换行。
//   原生集成直接文本双缓冲绘制引擎 (Direct GDI+ Double-Buffered Rendering)，
//   支持更新日志排版与诊断快照多色日志高亮模式，彻底杜绝闪烁、残影与滚动撕裂。
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
            public bool IsVersionBanner;
            public string VersionTitle;
            public string VersionDate;
            public string VersionBadge;
            public Font Font;
            public Font DateFont;
            public Color Color;
            public int Top;
            public int Height;
        }

        private List<ChangelogVisualItem> changelogItems;
        private int totalContentHeight = 0;
        private float currentDpiScale = 1.0f;
        private IList<string> rawChangelogLines;
        private bool isDiagnosticMode = false;
        private string rawDiagnosticText = null;

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
            this.isDiagnosticMode = false;
            this.rawDiagnosticText = null;
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

        public void SetDiagnosticText(string text, float dpiScale)
        {
            this.isDiagnosticMode = true;
            this.rawDiagnosticText = text ?? "";
            this.rawChangelogLines = null;
            this.currentDpiScale = (dpiScale > 0.1f) ? dpiScale : 1.0f;

            if (contentPanel != null)
            {
                contentPanel.Controls.Clear();
                contentPanel.Visible = false;
            }

            RebuildDiagnosticItems();
            this.scrollOffset = 0;
            Invalidate();
        }

        public void UpdateDpiScale(float dpiScale)
        {
            float normalized = (dpiScale > 0.1f) ? dpiScale : 1.0f;
            if (Math.Abs(this.currentDpiScale - normalized) < 0.001f) return;
            this.currentDpiScale = normalized;

            if (isDiagnosticMode)
            {
                RebuildDiagnosticItems();
            }
            else if (rawChangelogLines != null)
            {
                RebuildChangelogItems();
            }
            Invalidate();
        }

        public void HandleWheelDelta(int delta)
        {
            int max = GetMaxScroll();
            if (max <= 0 || delta == 0) return;
            ScrollOffset += (delta > 0 ? -42 : 42);
        }

        private void RebuildDiagnosticItems()
        {
            string text = this.rawDiagnosticText ?? "";
            string[] lines = text.Split(new string[] { "\r\n", "\n" }, StringSplitOptions.None);
            this.Text = text;

            int availW = this.ClientSize.Width - scrollBarWidth - (scrollBarMargin * 2) - 12;
            if (availW < 50) availW = 540;

            Font monoBold = DpiScaleHelper.GetCachedScaledFont("Consolas", 8.8F, FontStyle.Bold, currentDpiScale);
            Font monoRegular = DpiScaleHelper.GetCachedScaledFont("Consolas", 8.6F, FontStyle.Regular, currentDpiScale);
            TextFormatFlags measureFlags = TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix;

            int curY = 4;
            List<ChangelogVisualItem> list = new List<ChangelogVisualItem>();

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i];
                if (line.Length == 0)
                {
                    curY += Math.Max(6, (int)(8 * currentDpiScale));
                    continue;
                }

                bool isHeader = line.StartsWith("===") || line.StartsWith("[1.") || line.StartsWith("[2.") || line.StartsWith("[3.");
                Font font = isHeader ? monoBold : monoRegular;
                Color color = Color.FromArgb(215, 224, 240);

                if (isHeader)
                {
                    if (i > 0) curY += (int)(4 * currentDpiScale);
                    color = Color.FromArgb(130, 200, 255);
                }
                else if (line.Contains("[已识别]") || line.Contains(" OK "))
                {
                    color = Color.FromArgb(0, 226, 120);
                }
                else if (line.Contains("[未匹配内置驱动]"))
                {
                    color = Color.FromArgb(255, 196, 77);
                }
                else if (line.Contains("SKIPPED") || line.Contains("OPEN_FAIL") || line.Contains("NO_BATT") || line.Contains("NO_REPLY") || line.Contains("Error"))
                {
                    color = Color.FromArgb(255, 125, 138);
                }
                else if (line.StartsWith("      - "))
                {
                    color = Color.FromArgb(148, 160, 182);
                }

                Size measured = TextRenderer.MeasureText(line, font, new Size(availW, 0), measureFlags);
                int minRowH = (int)(17 * currentDpiScale);
                int itemH = Math.Max(minRowH, measured.Height + 2);

                ChangelogVisualItem item = new ChangelogVisualItem();
                item.Text = line;
                item.IsHeader = isHeader;
                item.Font = font;
                item.Color = color;
                item.Top = curY;
                item.Height = itemH;
                list.Add(item);

                curY += itemH + (int)(2 * currentDpiScale);
            }

            this.changelogItems = list;
            this.totalContentHeight = curY + 8;
        }

        private void RebuildChangelogItems()
        {
            IList<string> lines = this.rawChangelogLines;
            if (lines == null || lines.Count == 0)
            {
                lines = new List<string>() { "•  常规性能提升与稳定性修复" };
            }

            List<string> displayItems = new List<string>();
            List<string> accessibleTextParts = new List<string>();
            for (int i = 0; i < lines.Count; i++)
            {
                string entry = lines[i];
                if (string.IsNullOrEmpty(entry)) continue;

                if (entry.StartsWith(ReleaseNotesService.VersionBannerPrefix))
                {
                    displayItems.Add(entry);
                    string[] segs = entry.Substring(ReleaseNotesService.VersionBannerPrefix.Length).Split('|');
                    string vt = segs.Length > 0 ? segs[0] : "";
                    string vd = segs.Length > 1 ? segs[1] : "";
                    accessibleTextParts.Add("[" + vt + (string.IsNullOrEmpty(vd) ? "" : (" " + vd)) + "]");
                }
                else if (entry.Contains("【") && entry.IndexOf("【") != entry.LastIndexOf("【"))
                {
                    string[] parts = System.Text.RegularExpressions.Regex.Split(entry, @"(?=【)");
                    foreach (string p in parts)
                    {
                        string cp = p.Trim().TrimEnd(',', '，', ';', '；');
                        if (!string.IsNullOrEmpty(cp))
                        {
                            displayItems.Add(cp);
                            accessibleTextParts.Add(cp);
                        }
                    }
                }
                else
                {
                    displayItems.Add(entry);
                    accessibleTextParts.Add(entry);
                }
            }

            this.Text = string.Join(" | ", accessibleTextParts.ToArray());

            int availW = this.ClientSize.Width - scrollBarWidth - (scrollBarMargin * 2) - 12;
            if (availW < 50) availW = 400;

            Font bannerTitleFont = DpiScaleHelper.GetCachedScaledFont("Microsoft YaHei UI", 10.0F, FontStyle.Bold, currentDpiScale);
            Font bannerDateFont = DpiScaleHelper.GetCachedScaledFont("Microsoft YaHei UI", 8.6F, FontStyle.Regular, currentDpiScale);
            Font headerFont = DpiScaleHelper.GetCachedScaledFont("Microsoft YaHei UI", 10.0F, FontStyle.Bold, currentDpiScale);
            Font bodyFont = DpiScaleHelper.GetCachedScaledFont("Microsoft YaHei UI", 9.5F, FontStyle.Regular, currentDpiScale);

            int curY = 4;
            List<ChangelogVisualItem> list = new List<ChangelogVisualItem>();

            for (int i = 0; i < displayItems.Count; i++)
            {
                string raw = displayItems[i];
                if (raw.StartsWith(ReleaseNotesService.VersionBannerPrefix))
                {
                    if (i > 0) curY += (int)(12 * currentDpiScale);
                    string payload = raw.Substring(ReleaseNotesService.VersionBannerPrefix.Length);
                    string[] segs = payload.Split('|');
                    string verTitle = segs.Length > 0 ? segs[0].Trim() : "";
                    string verDate = segs.Length > 1 ? segs[1].Trim() : "";
                    string verBadge = segs.Length > 2 ? segs[2].Trim() : "";

                    int bannerH = Math.Max(34, (int)(36 * currentDpiScale));
                    ChangelogVisualItem bannerItem = new ChangelogVisualItem();
                    bannerItem.IsVersionBanner = true;
                    bannerItem.IsHeader = true;
                    bannerItem.VersionTitle = verTitle;
                    bannerItem.VersionDate = verDate;
                    bannerItem.VersionBadge = verBadge;
                    bannerItem.Text = verTitle;
                    bannerItem.Font = bannerTitleFont;
                    bannerItem.DateFont = bannerDateFont;
                    bannerItem.Color = Color.FromArgb(0, 230, 118);
                    bannerItem.Top = curY;
                    bannerItem.Height = bannerH;
                    list.Add(bannerItem);

                    curY += bannerH + (int)(10 * currentDpiScale);
                    continue;
                }

                bool isHeader = raw.StartsWith("一、") || raw.StartsWith("二、") || raw.StartsWith("三、") ||
                                raw.StartsWith("四、") || raw.StartsWith("五、") || raw.StartsWith("六、") ||
                                raw.StartsWith("特别说明") || raw.StartsWith("特别鸣谢") ||
                                raw.StartsWith("升级提醒") || raw.StartsWith("后续适配");

                string drawText = isHeader ? raw : ("•  " + raw);
                Font font = isHeader ? headerFont : bodyFont;
                Color color = isHeader ? Color.FromArgb(240, 245, 255) : Color.FromArgb(195, 203, 220);

                if (isHeader && i > 0 && !(displayItems[i - 1].StartsWith(ReleaseNotesService.VersionBannerPrefix)))
                {
                    curY += (int)(12 * currentDpiScale);
                }

                Size measured = TextRenderer.MeasureText(drawText, font, new Size(availW, 0),
                    TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                int itemH = Math.Max((int)(24 * currentDpiScale), measured.Height + (int)(4 * currentDpiScale));

                ChangelogVisualItem item = new ChangelogVisualItem();
                item.Text = drawText;
                item.IsHeader = isHeader;
                item.Font = font;
                item.Color = color;
                item.Top = curY;
                item.Height = itemH;
                list.Add(item);

                curY += itemH + (int)(7 * currentDpiScale);
            }

            this.changelogItems = list;
            this.totalContentHeight = curY + 8;
        }

        protected override void OnResize(EventArgs eventargs)
        {
            base.OnResize(eventargs);
            if (isDiagnosticMode)
            {
                RebuildDiagnosticItems();
            }
            else if (changelogItems != null && changelogItems.Count > 0)
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
                TextFormatFlags drawFlags = isDiagnosticMode
                    ? (TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl | TextFormatFlags.NoPrefix)
                    : (TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                for (int i = 0; i < changelogItems.Count; i++)
                {
                    ChangelogVisualItem item = changelogItems[i];
                    int drawY = item.Top - scrollOffset;

                    // 视口剔除：仅绘制位于当前可视窗口内的条目
                    if (drawY + item.Height >= 0 && drawY <= clientH)
                    {
                        if (item.IsVersionBanner)
                        {
                            DrawVersionBannerItem(g, item, drawY, availW);
                        }
                        else
                        {
                            Rectangle rect = new Rectangle(4, drawY, availW, item.Height);
                            TextRenderer.DrawText(g, item.Text, item.Font, rect, item.Color, drawFlags);
                        }
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

        private void DrawVersionBannerItem(Graphics g, ChangelogVisualItem item, int drawY, int availW)
        {
            Rectangle bannerRect = new Rectangle(4, drawY, Math.Max(40, availW - 4), item.Height);
            int radius = Math.Max(4, (int)(6 * currentDpiScale));

            using (GraphicsPath bgPath = ModernThemeHelper.GetRoundedRectangle(bannerRect, radius))
            using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(26, 36, 36)))
            using (Pen borderPen = new Pen(Color.FromArgb(42, 78, 66), 1))
            {
                g.FillPath(bgBrush, bgPath);
                g.DrawPath(borderPen, bgPath);
            }

            int barW = Math.Max(3, (int)(3 * currentDpiScale));
            int barPad = Math.Max(5, (int)(6 * currentDpiScale));
            using (SolidBrush accentBrush = new SolidBrush(Color.FromArgb(0, 226, 120)))
            {
                g.FillRectangle(accentBrush, bannerRect.X + 1, bannerRect.Y + barPad, barW, Math.Max(6, bannerRect.Height - barPad * 2));
            }

            int textLeft = bannerRect.X + barW + Math.Max(10, (int)(10 * currentDpiScale));
            Size verSize = TextRenderer.MeasureText(item.VersionTitle ?? "", item.Font);
            Rectangle verRect = new Rectangle(textLeft, bannerRect.Y, verSize.Width + 6, bannerRect.Height);
            TextRenderer.DrawText(g, item.VersionTitle ?? "", item.Font, verRect, Color.FromArgb(240, 248, 255),
                TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.NoPrefix);

            if (!string.IsNullOrEmpty(item.VersionBadge))
            {
                Font badgeFont = item.DateFont ?? item.Font;
                Size badgeTextSize = TextRenderer.MeasureText(item.VersionBadge, badgeFont);
                int padH = Math.Max(6, (int)(7 * currentDpiScale));
                int pillW = badgeTextSize.Width + padH * 2;
                int pillH = Math.Max(16, (int)(18 * currentDpiScale));
                int pillX = verRect.Right + Math.Max(4, (int)(6 * currentDpiScale));
                int pillY = bannerRect.Y + (bannerRect.Height - pillH) / 2;
                Rectangle pillRect = new Rectangle(pillX, pillY, pillW, pillH);

                using (GraphicsPath pillPath = ModernThemeHelper.GetRoundedRectangle(pillRect, pillH / 2))
                using (SolidBrush pillBrush = new SolidBrush(Color.FromArgb(0, 185, 86)))
                {
                    g.FillPath(pillBrush, pillPath);
                }
                TextRenderer.DrawText(g, item.VersionBadge, badgeFont, pillRect, Color.FromArgb(12, 24, 16),
                    TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter | TextFormatFlags.NoPrefix);
            }

            if (!string.IsNullOrEmpty(item.VersionDate))
            {
                Font dateFont = item.DateFont ?? item.Font;
                Rectangle dateRect = new Rectangle(bannerRect.X, bannerRect.Y, bannerRect.Width - Math.Max(10, (int)(12 * currentDpiScale)), bannerRect.Height);
                TextRenderer.DrawText(g, item.VersionDate, dateFont, dateRect, Color.FromArgb(148, 164, 178),
                    TextFormatFlags.VerticalCenter | TextFormatFlags.Right | TextFormatFlags.NoPrefix);
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
