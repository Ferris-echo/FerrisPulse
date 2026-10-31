// ============================================================================
// 模块名称: PriorityReorderControl.cs
// 所属区域: UI/Controls (高级功能控件层)
// 核心职责:
//   现代化设备托盘优先级拖拽排序控件，支持卡片流体跟随拖拽、物理弹簧阻尼回弹过渡动画、
//   固定序号槽位渲染、微型矢量删除/隐藏设备按钮、平滑细滚动条与全矢量无极缩放。
// 依赖关系: Models.PriorityItem, Hardware.DeviceManager, Theme.ModernThemeHelper, RoundedCard, Helpers.DpiScaleHelper
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
using System.Drawing.Text;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class PriorityReorderControl : Control
    {
        public List<PriorityItem> Items = new List<PriorityItem>();
        public float DpiScale = 1.0f;

        private int draggedIndex = -1;
        private int currentDropIndex = -1;
        private int dragCurrentY = 0;
        private int dragOffsetY = 0;
        private bool isDragging = false;
        private PriorityItem settlingItem = null;
        private Timer animTimer;
        private int scrollY = 0;
        private int hoveredDeleteIndex = -1;

        public event Action OrderChanged;

        public PriorityReorderControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(18, 19, 23);
            DoubleBuffered = true;

            animTimer = new Timer();
            animTimer.Interval = 15; // ~60 FPS
            animTimer.Tick += (s, e) => {
                bool needsRedraw = false;
                for (int i = 0; i < Items.Count; i++)
                {
                    if (isDragging && i == draggedIndex) continue;
                    float diff = Items[i].TargetY - Items[i].CurrentY;
                    if (Math.Abs(diff) > 0.4f)
                    {
                        Items[i].CurrentY += diff * 0.35f;
                        needsRedraw = true;
                    }
                    else
                    {
                        Items[i].CurrentY = Items[i].TargetY;
                    }
                }

                if (settlingItem != null && Math.Abs(settlingItem.TargetY - settlingItem.CurrentY) < 0.5f)
                {
                    settlingItem.CurrentY = settlingItem.TargetY;
                    settlingItem = null;
                    needsRedraw = true;
                }

                if (needsRedraw) Invalidate();
                else if (!isDragging && settlingItem == null) animTimer.Stop();
            };
        }

        public int GetItemHeight()
        {
            return (int)(54 * DpiScale);
        }

        public int GetItemGap()
        {
            return (int)(8 * DpiScale);
        }

        public int GetContentHeight()
        {
            return Items.Count * (GetItemHeight() + GetItemGap());
        }

        public int GetMaxScroll()
        {
            int max = GetContentHeight() - Height;
            return max > 0 ? max : 0;
        }

        public void ResetPositions()
        {
            int totalH = GetItemHeight() + GetItemGap();
            for (int i = 0; i < Items.Count; i++)
            {
                Items[i].CurrentY = i * totalH;
                Items[i].TargetY = i * totalH;
            }
            int max = GetMaxScroll();
            if (scrollY > max) scrollY = max;
            Invalidate();
        }

        public void DeleteItem(int index)
        {
            if (index < 0 || index >= Items.Count) return;
            Items.RemoveAt(index);
            for (int i = 0; i < Items.Count; i++) Items[i].PriorityIndex = i + 1;
            ResetPositions();
            if (OrderChanged != null) OrderChanged();
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int maxScroll = GetMaxScroll();
            if (maxScroll > 0)
            {
                scrollY -= Math.Sign(e.Delta) * (int)(45 * DpiScale);
                if (scrollY < 0) scrollY = 0;
                if (scrollY > maxScroll) scrollY = maxScroll;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && Items.Count > 0)
            {
                int itemH = GetItemHeight();
                int padX = (int)(8 * DpiScale);
                int rankColW = (int)(26 * DpiScale);
                int cardStartX = padX + rankColW + (int)(8 * DpiScale);
                int maxScroll = GetMaxScroll();
                int rightMargin = maxScroll > 0 ? (int)(12 * DpiScale) : 0;
                int cardW = Width - cardStartX - (int)(10 * DpiScale) - rightMargin;
                int gripW = (int)(18 * DpiScale);
                int delW = (int)(22 * DpiScale);

                for (int i = 0; i < Items.Count; i++)
                {
                    float y = Items[i].CurrentY - scrollY;
                    if (e.Y >= y && e.Y <= y + itemH)
                    {
                        int gripX = cardStartX + cardW - gripW - (int)(14 * DpiScale);
                        int delX = gripX - delW - (int)(8 * DpiScale);
                        int delY = (int)y + (itemH - delW) / 2;
                        Rectangle delRect = new Rectangle(delX, delY, delW, delW);
                        if (delRect.Contains(e.Location))
                        {
                            var it = Items[i];
                            var dr = MessageBox.Show(this.FindForm(),
                                "确定要移除设备「" + it.Name + "」吗？\n移除后该设备将不再在任务栏托盘及主界面中显示。",
                                "移除设备",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Question);
                            if (dr == DialogResult.Yes)
                            {
                                DeviceManager.HideOrDeleteDevice(it.DeviceId);
                                if (!string.IsNullOrEmpty(it.HardwareFingerprint))
                                {
                                    DeviceManager.HideOrDeleteDevice(it.HardwareFingerprint);
                                }
                                DeleteItem(i);
                            }
                            return;
                        }

                        isDragging = true;
                        settlingItem = null;
                        draggedIndex = i;
                        currentDropIndex = i;
                        dragCurrentY = e.Y + scrollY;
                        dragOffsetY = (int)((e.Y + scrollY) - Items[i].CurrentY);
                        Capture = true;
                        animTimer.Start();
                        Invalidate();
                        break;
                    }
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (isDragging)
            {
                int maxScroll = GetMaxScroll();
                int edgeThreshold = (int)(24 * DpiScale);
                if (e.Y < edgeThreshold && scrollY > 0)
                {
                    scrollY = Math.Max(0, scrollY - (int)(8 * DpiScale));
                }
                else if (e.Y > Height - edgeThreshold && scrollY < maxScroll)
                {
                    scrollY = Math.Min(maxScroll, scrollY + (int)(8 * DpiScale));
                }

                dragCurrentY = e.Y + scrollY;
                int itemH = GetItemHeight();
                int totalH = itemH + GetItemGap();

                float floatCenter = (dragCurrentY - dragOffsetY) + (itemH / 2f);
                int targetSlot = (int)(floatCenter / totalH);
                if (targetSlot < 0) targetSlot = 0;
                if (targetSlot >= Items.Count) targetSlot = Items.Count - 1;

                if (targetSlot != currentDropIndex)
                {
                    currentDropIndex = targetSlot;
                    for (int k = 0; k < Items.Count; k++)
                    {
                        if (k == draggedIndex) continue;
                        int slot = k;
                        if (draggedIndex < targetSlot)
                        {
                            if (k > draggedIndex && k <= targetSlot) slot = k - 1;
                        }
                        else if (draggedIndex > targetSlot)
                        {
                            if (k >= targetSlot && k < draggedIndex) slot = k + 1;
                        }
                        Items[k].TargetY = slot * totalH;
                    }
                    animTimer.Start();
                }
                Invalidate();
            }
            else
            {
                int newHoverDel = -1;
                int itemH = GetItemHeight();
                int padX = (int)(8 * DpiScale);
                int rankColW = (int)(26 * DpiScale);
                int cardStartX = padX + rankColW + (int)(8 * DpiScale);
                int maxScroll = GetMaxScroll();
                int rightMargin = maxScroll > 0 ? (int)(12 * DpiScale) : 0;
                int cardW = Width - cardStartX - (int)(10 * DpiScale) - rightMargin;
                int gripW = (int)(18 * DpiScale);
                int delW = (int)(22 * DpiScale);

                for (int i = 0; i < Items.Count; i++)
                {
                    float y = Items[i].CurrentY - scrollY;
                    int gripX = cardStartX + cardW - gripW - (int)(14 * DpiScale);
                    int delX = gripX - delW - (int)(8 * DpiScale);
                    int delY = (int)y + (itemH - delW) / 2;
                    Rectangle delRect = new Rectangle(delX, delY, delW, delW);
                    if (delRect.Contains(e.Location))
                    {
                        newHoverDel = i;
                        break;
                    }
                }
                if (newHoverDel != hoveredDeleteIndex)
                {
                    hoveredDeleteIndex = newHoverDel;
                    Invalidate();
                }
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoveredDeleteIndex != -1)
            {
                hoveredDeleteIndex = -1;
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (isDragging)
            {
                isDragging = false;
                Capture = false;

                int floatY = dragCurrentY - dragOffsetY;
                var item = Items[draggedIndex];
                item.CurrentY = floatY;

                if (draggedIndex != currentDropIndex && currentDropIndex >= 0)
                {
                    Items.RemoveAt(draggedIndex);
                    Items.Insert(currentDropIndex, item);
                    for (int i = 0; i < Items.Count; i++) Items[i].PriorityIndex = i + 1;
                    if (OrderChanged != null) OrderChanged();
                }

                int totalH = GetItemHeight() + GetItemGap();
                for (int i = 0; i < Items.Count; i++)
                {
                    Items[i].TargetY = i * totalH;
                }

                settlingItem = item;
                draggedIndex = -1;
                currentDropIndex = -1;
                animTimer.Start();
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(BackColor);

            int maxScroll = GetMaxScroll();
            int rightMargin = maxScroll > 0 ? (int)(12 * DpiScale) : 0;
            int itemH = GetItemHeight();
            int itemGap = GetItemGap();
            int totalH = itemH + itemGap;
            int padX = (int)(8 * DpiScale);
            int rankColW = (int)(26 * DpiScale);
            int cardStartX = padX + rankColW + (int)(8 * DpiScale);
            int cardW = Width - cardStartX - (int)(10 * DpiScale) - rightMargin;

            var state = g.Save();
            g.TranslateTransform(0, -scrollY);

            // 1. Draw FIXED slot numbers on the left
            for (int i = 0; i < Items.Count; i++)
            {
                float slotY = i * totalH;
                if (slotY + itemH < scrollY || slotY > scrollY + Height) continue;
                DrawFixedSlotRank(g, i + 1, padX, (int)slotY, rankColW, itemH);
            }

            // 2. Draw non-dragged and non-settling cards
            for (int i = 0; i < Items.Count; i++)
            {
                if (isDragging && i == draggedIndex) continue;
                if (Items[i] == settlingItem) continue;
                float cardY = Items[i].CurrentY;
                if (cardY + itemH < scrollY || cardY > scrollY + Height) continue;
                DrawCard(g, Items[i], cardStartX, (int)cardY, cardW, itemH, false, (hoveredDeleteIndex == i));
            }

            // 3. Draw settling card on top layer with smooth easing into slot
            if (settlingItem != null)
            {
                DrawCard(g, settlingItem, cardStartX, (int)settlingItem.CurrentY, cardW, itemH, true, false);
            }

            // 4. Draw dragged floating card on very top
            if (isDragging && draggedIndex >= 0 && draggedIndex < Items.Count)
            {
                int floatY = dragCurrentY - dragOffsetY;
                DrawCard(g, Items[draggedIndex], cardStartX, floatY, cardW, itemH, true, false);
            }

            g.Restore(state);

            // Draw slim modern scrollbar if content exceeds height
            if (maxScroll > 0)
            {
                int sbW = Math.Max(2, (int)(4 * DpiScale));
                int sbX = Width - sbW - (int)(4 * DpiScale);
                int trackH = Height;
                int thumbH = Math.Max((int)(28 * DpiScale), (int)((float)Height / GetContentHeight() * trackH));
                int thumbY = (int)((float)scrollY / maxScroll * (trackH - thumbH));

                using (var thumbPath = RoundedCard.GetRoundedRectangle(new Rectangle(sbX, thumbY, sbW, thumbH), Math.Max(1, sbW / 2)))
                using (var thumbBrush = new SolidBrush(Color.FromArgb(90, 110, 130, 160)))
                {
                    g.FillPath(thumbBrush, thumbPath);
                }
            }
        }

        private void DrawFixedSlotRank(Graphics g, int rank, int x, int y, int w, int h)
        {
            int rankSize = Math.Max(14, (int)(22 * DpiScale));
            int rx = x + (w - rankSize) / 2;
            int ry = y + (h - rankSize) / 2;
            Rectangle rankRect = new Rectangle(rx, ry, rankSize, rankSize);

            bool isTop = (rank == 1);
            Color rBg = isTop ? Color.FromArgb(32, 0, 230, 118) : Color.FromArgb(28, 32, 42);
            Color rBorder = isTop ? Color.FromArgb(0, 230, 118) : Color.FromArgb(55, 62, 78);
            Color rFg = isTop ? Color.FromArgb(0, 230, 118) : Color.FromArgb(160, 170, 190);

            using (var rPath = RoundedCard.GetRoundedRectangle(rankRect, Math.Max(4, rankSize / 2)))
            {
                using (var rb = new SolidBrush(rBg)) g.FillPath(rb, rPath);
                using (var rp = new Pen(rBorder, isTop ? 1.5f : 1.0f)) g.DrawPath(rp, rPath);
            }

            using (var rf = DpiScaleHelper.CreateScaledFont("Segoe UI", 8.8F, FontStyle.Bold, DpiScale))
            {
                string rStr = rank.ToString();
                TextRenderer.DrawText(g, rStr, rf, rankRect, rFg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }
        }

        private void DrawCard(Graphics g, PriorityItem item, int x, int y, int w, int h, bool isElevated, bool isDelHovered)
        {
            int r = Math.Max(3, (int)(8 * DpiScale));
            Rectangle rect = new Rectangle(x, y, w, h);

            if (isElevated)
            {
                using (var sPath = RoundedCard.GetRoundedRectangle(new Rectangle(x + 2, y + 4, w, h), r))
                using (var sBrush = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
                {
                    g.FillPath(sBrush, sPath);
                }
            }

            Color cardBg = isElevated ? Color.FromArgb(34, 38, 50) : Color.FromArgb(25, 27, 34);
            Color borderCol = isElevated ? item.AccentColor : Color.FromArgb(42, 46, 58);

            using (var path = RoundedCard.GetRoundedRectangle(rect, r))
            {
                using (var brush = new SolidBrush(cardBg)) g.FillPath(brush, path);
                using (var pen = new Pen(borderCol, isElevated ? 1.8f : 1.0f)) g.DrawPath(pen, path);
            }

            // 1. Icon (left aligned inside card) - Vector centered!
            int iconBoxSize = (int)(22 * DpiScale);
            int iconX = x + (int)(12 * DpiScale);
            int iconY = y + (h - iconBoxSize) / 2;
            Rectangle iconRect = new Rectangle(iconX, iconY, iconBoxSize, iconBoxSize);
            ModernThemeHelper.DrawModernDeviceIcon(g, iconRect, item.Category, item.AccentColor);

            // 2. Fluent Vector Grip Handle on far right
            int gripW = (int)(16 * DpiScale);
            int gripX = x + w - gripW - (int)(14 * DpiScale);
            int barW = Math.Max(6, (int)(12 * DpiScale));
            int barH = Math.Max(1, (int)(2 * DpiScale));
            int barGap = Math.Max(2, (int)(3 * DpiScale));
            int gripStartY = y + (h - (barH * 3 + barGap * 2)) / 2;
            Color gripColor = isElevated ? item.AccentColor : Color.FromArgb(110, 120, 140);
            using (var gripBrush = new SolidBrush(gripColor))
            {
                for (int b = 0; b < 3; b++)
                {
                    int by = gripStartY + b * (barH + barGap);
                    using (var gp = RoundedCard.GetRoundedRectangle(new Rectangle(gripX + (gripW - barW) / 2, by, barW, barH), Math.Max(1, (int)(1 * DpiScale))))
                    {
                        g.FillPath(gripBrush, gp);
                    }
                }
            }

            // 3. Sleek Pure Vector Micro Delete Button
            int delW = Math.Max(14, (int)(22 * DpiScale));
            int delX = gripX - delW - (int)(8 * DpiScale);
            int delY = y + (h - delW) / 2;
            Rectangle delRect = new Rectangle(delX, delY, delW, delW);

            if (isDelHovered)
            {
                using (var delPath = RoundedCard.GetRoundedRectangle(delRect, Math.Max(2, (int)(5 * DpiScale))))
                using (var delBg = new SolidBrush(Color.FromArgb(45, 255, 82, 82)))
                {
                    g.FillPath(delBg, delPath);
                }
            }

            Color xCol = isDelHovered ? Color.FromArgb(255, 100, 100) : Color.FromArgb(100, 115, 135);
            float penW = Math.Max(1.3f, 2.0f * DpiScale);
            using (var xPen = new Pen(xCol, penW))
            {
                xPen.StartCap = LineCap.Round;
                xPen.EndCap = LineCap.Round;
                int cPad = Math.Max(3, (int)(6 * DpiScale));
                g.DrawLine(xPen, delX + cPad, delY + cPad, delX + delW - cPad, delY + delW - cPad);
                g.DrawLine(xPen, delX + delW - cPad, delY + cPad, delX + cPad, delY + delW - cPad);
            }

            int lastRightX = delX;

            // 4. Battery / Status Pill before Delete Button
            string battStr = item.IsConnected ? (item.Battery + "%") : (item.Battery > 0 ? (item.Battery + "% 离线") : "离线");
            Color battColor = item.IsConnected ? item.AccentColor : Color.FromArgb(140, 145, 155);
            int battX = lastRightX;
            using (var battFont = DpiScaleHelper.CreateScaledFont("Microsoft YaHei UI", 8.8F, FontStyle.Bold, DpiScale))
            {
                var bSz = TextRenderer.MeasureText(battStr, battFont, new Size(int.MaxValue, int.MaxValue), TextFormatFlags.NoPadding);
                battX = lastRightX - bSz.Width - (int)(10 * DpiScale);
                Rectangle battRect = new Rectangle(battX, y, bSz.Width + (int)(4 * DpiScale), h);
                TextRenderer.DrawText(g, battStr, battFont, battRect, battColor,
                    TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }

            // 5. Device Name (left aligned, clipped before Battery text to prevent overlap at any scale)
            int nameX = iconX + iconBoxSize + (int)(10 * DpiScale);
            int maxNameW = Math.Max(30, battX - nameX - (int)(8 * DpiScale));
            using (var nameFont = DpiScaleHelper.CreateScaledFont("Microsoft YaHei UI", 9.2F, FontStyle.Bold, DpiScale))
            {
                Rectangle nameRect = new Rectangle(nameX, y, maxNameW, h);
                TextRenderer.DrawText(g, item.Name ?? "", nameFont, nameRect, Color.FromArgb(240, 245, 255),
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }
        }
    }
}
