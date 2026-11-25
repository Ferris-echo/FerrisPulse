// ============================================================================
// 模块名称: PriorityReorderControl.cs
// 所属区域: UI/Controls (高级功能控件层)
// 核心职责:
//   现代化设备托盘优先级拖拽排序控件，支持静止态全宽卡片内嵌纯白个位数序号 (1/2/3...)，
//   拖拽时平滑剥离让出左侧静止数字轨道（目标落位高亮品牌色微光徽章、松手丝滑合回全宽），
//   物理弹簧阻尼回弹、双色主题色点个性化触发、微型矢量删除/隐藏设备按钮与全矢量无极缩放。
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
        public int ActiveTraySlotCount { get; set; }

        private int draggedIndex = -1;
        private int currentDropIndex = -1;
        private int dragCurrentY = 0;
        private int dragOffsetY = 0;
        private bool isDragging = false;
        private PriorityItem settlingItem = null;
        private Timer animTimer;
        private int scrollY = 0;
        private int hoveredDeleteIndex = -1;
        private int hoveredSwatchIndex = -1;
        private float dragSplitProgress = 0.0f;

        public float DragSplitProgress { get { return dragSplitProgress; } }

        public event Action OrderChanged;
        public event Action<int> SwatchClicked;

        public PriorityReorderControl()
        {
            ActiveTraySlotCount = 1;
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

                float targetSplit = isDragging ? 1.0f : 0.0f;
                float splitDiff = targetSplit - dragSplitProgress;
                if (Math.Abs(splitDiff) > 0.01f)
                {
                    dragSplitProgress += splitDiff * 0.28f;
                    needsRedraw = true;
                }
                else if (dragSplitProgress != targetSplit)
                {
                    dragSplitProgress = targetSplit;
                    needsRedraw = true;
                }

                if (settlingItem != null && Math.Abs(settlingItem.TargetY - settlingItem.CurrentY) < 0.5f)
                {
                    settlingItem.CurrentY = settlingItem.TargetY;
                    settlingItem = null;
                    needsRedraw = true;
                }

                if (needsRedraw) Invalidate();
                else if (!isDragging && settlingItem == null && Math.Abs(dragSplitProgress) < 0.01f) animTimer.Stop();
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

        private void GetLayoutMetrics(out int padX, out int rankColW, out int cardStartX, out int cardW)
        {
            int maxScroll = GetMaxScroll();
            int rightMargin = maxScroll > 0 ? (int)(10 * DpiScale) : 0;
            padX = 0;
            rankColW = 0;
            cardStartX = 0;
            cardW = Math.Max(80, Width - rightMargin - 1);
        }

        public int GetLiveDisplayRank(int itemIndex)
        {
            if (!isDragging || draggedIndex < 0 || currentDropIndex < 0)
            {
                return itemIndex + 1;
            }
            if (itemIndex == draggedIndex)
            {
                return currentDropIndex + 1;
            }
            if (draggedIndex < currentDropIndex)
            {
                if (itemIndex > draggedIndex && itemIndex <= currentDropIndex) return itemIndex;
                return itemIndex + 1;
            }
            if (draggedIndex > currentDropIndex)
            {
                if (itemIndex >= currentDropIndex && itemIndex < draggedIndex) return itemIndex + 2;
                return itemIndex + 1;
            }
            return itemIndex + 1;
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && Items.Count > 0)
            {
                int itemH = GetItemHeight();
                int padX;
                int rankColW;
                int cardStartX;
                int cardW;
                GetLayoutMetrics(out padX, out rankColW, out cardStartX, out cardW);
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

                        int swW = Math.Max(14, (int)(20 * DpiScale));
                        int swX = delX - swW - (int)(6 * DpiScale);
                        int swY = (int)y + (itemH - swW) / 2;
                        Rectangle swRect = new Rectangle(swX, swY, swW, swW);
                        if (swRect.Contains(e.Location))
                        {
                            var handler = SwatchClicked;
                            if (handler != null) handler(i);
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
                int newHoverSw = -1;
                int itemH = GetItemHeight();
                int padX;
                int rankColW;
                int cardStartX;
                int cardW;
                GetLayoutMetrics(out padX, out rankColW, out cardStartX, out cardW);
                int gripW = (int)(18 * DpiScale);
                int delW = (int)(22 * DpiScale);
                int swW = Math.Max(14, (int)(20 * DpiScale));

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
                    int swX = delX - swW - (int)(6 * DpiScale);
                    int swY = (int)y + (itemH - swW) / 2;
                    Rectangle swRect = new Rectangle(swX, swY, swW, swW);
                    if (swRect.Contains(e.Location))
                    {
                        newHoverSw = i;
                        break;
                    }
                }
                if (newHoverDel != hoveredDeleteIndex || newHoverSw != hoveredSwatchIndex)
                {
                    hoveredDeleteIndex = newHoverDel;
                    hoveredSwatchIndex = newHoverSw;
                    Invalidate();
                }
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoveredDeleteIndex != -1 || hoveredSwatchIndex != -1)
            {
                hoveredDeleteIndex = -1;
                hoveredSwatchIndex = -1;
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

        private static Color BlendColor(Color a, Color b, float t)
        {
            if (t <= 0f) return a;
            if (t >= 1f) return b;
            int r = (int)Math.Round(a.R + (b.R - a.R) * t);
            int g = (int)Math.Round(a.G + (b.G - a.G) * t);
            int bl = (int)Math.Round(a.B + (b.B - a.B) * t);
            return Color.FromArgb(r, g, bl);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(BackColor);

            int maxScroll = GetMaxScroll();
            int itemH = GetItemHeight();
            int itemGap = GetItemGap();
            int totalH = itemH + itemGap;
            int padX;
            int rankColW;
            int cardStartX;
            int cardW;
            GetLayoutMetrics(out padX, out rankColW, out cardStartX, out cardW);

            float p = dragSplitProgress;
            int railW = (int)(36 * DpiScale);
            int splitShift = (int)Math.Round(railW * p);
            int drawCardX = cardStartX + splitShift;
            int drawCardW = Math.Max(60, cardW - splitShift);
            int fixedIconX = cardStartX + (int)(49 * DpiScale);

            // 1. Stationary left rank rail (fades in smoothly as cards peel right during drag, exact center match with resting digit)
            if (p > 0.05f)
            {
                float railFade = Math.Min(1.0f, Math.Max(0.0f, (p - 0.15f) / 0.85f));
                Color dragAccent = (draggedIndex >= 0 && draggedIndex < Items.Count)
                    ? Items[draggedIndex].AccentColor
                    : Color.FromArgb(0, 230, 118);

                for (int i = 0; i < Items.Count; i++)
                {
                    int slotY = i * totalH - scrollY;
                    if (slotY + itemH < 0 || slotY > Height) continue;

                    bool isTargetSlot = isDragging && (currentDropIndex == i);
                    int slotRankX = cardStartX + (int)(7 * DpiScale);
                    int slotRankW = (int)(22 * DpiScale);
                    Rectangle slotRankRect = new Rectangle(slotRankX, slotY, slotRankW, itemH);

                    if (isTargetSlot)
                    {
                        Color targetTextCol = BlendColor(BackColor, dragAccent, railFade);
                        using (var railFont = DpiScaleHelper.CreateScaledFont("Segoe UI", 12.5F, FontStyle.Bold, DpiScale))
                        {
                            TextRenderer.DrawText(g, (i + 1).ToString(), railFont, slotRankRect, targetTextCol,
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                        }
                    }
                    else
                    {
                        Color idleTextCol = BlendColor(BackColor, Color.FromArgb(148, 160, 184), railFade);
                        using (var railFont = DpiScaleHelper.CreateScaledFont("Segoe UI", 11.5F, FontStyle.Bold, DpiScale))
                        {
                            TextRenderer.DrawText(g, (i + 1).ToString(), railFont, slotRankRect, idleTextCol,
                                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                        }
                    }
                }
            }

            // 1.5 Recessed dashed drop-target slot placeholder matching split card bounds
            if (isDragging && currentDropIndex >= 0 && currentDropIndex < Items.Count)
            {
                int slotY = currentDropIndex * totalH - scrollY;
                if (slotY + itemH >= 0 && slotY <= Height)
                {
                    int slotR = Math.Max(3, (int)(8 * DpiScale));
                    Rectangle slotRect = new Rectangle(drawCardX, slotY, drawCardW, itemH);
                    Color accentRef = (draggedIndex >= 0 && draggedIndex < Items.Count)
                        ? Items[draggedIndex].AccentColor
                        : Color.FromArgb(0, 230, 118);
                    Color slotPenCol = Color.FromArgb(95, accentRef);
                    using (var slotPath = RoundedCard.GetRoundedRectangle(slotRect, slotR))
                    {
                        using (var slotBrush = new SolidBrush(Color.FromArgb(18, 22, 30)))
                        {
                            g.FillPath(slotBrush, slotPath);
                        }
                        using (var slotPen = new Pen(slotPenCol, 1.2f))
                        {
                            slotPen.DashStyle = DashStyle.Dash;
                            g.DrawPath(slotPen, slotPath);
                        }
                    }
                }
            }

            // 2. Draw non-dragged and non-settling cards
            for (int i = 0; i < Items.Count; i++)
            {
                if (isDragging && i == draggedIndex) continue;
                if (Items[i] == settlingItem) continue;
                int drawY = (int)Items[i].CurrentY - scrollY;
                if (drawY + itemH < 0 || drawY > Height) continue;
                int displayRank = GetLiveDisplayRank(i);
                DrawCard(g, Items[i], displayRank, drawCardX, drawY, drawCardW, itemH, fixedIconX, false, false, (hoveredDeleteIndex == i), (hoveredSwatchIndex == i));
            }

            // 3. Draw settling card on top layer with smooth easing into slot
            if (settlingItem != null)
            {
                int settleIdx = Items.IndexOf(settlingItem);
                int settleRank = settleIdx >= 0 ? (settleIdx + 1) : settlingItem.PriorityIndex;
                int settleY = (int)settlingItem.CurrentY - scrollY;
                DrawCard(g, settlingItem, settleRank, drawCardX, settleY, drawCardW, itemH, fixedIconX, true, false, false, false);
            }

            // 4. Draw dragged floating card on very top (hides internal rank digit cleanly while moving)
            if (isDragging && draggedIndex >= 0 && draggedIndex < Items.Count)
            {
                int dragRank = GetLiveDisplayRank(draggedIndex);
                int floatScreenY = (dragCurrentY - dragOffsetY) - scrollY;
                DrawCard(g, Items[draggedIndex], dragRank, drawCardX, floatScreenY, drawCardW, itemH, fixedIconX, true, true, false, false);
            }

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

        private void DrawCard(Graphics g, PriorityItem item, int displayRank, int x, int y, int w, int h, int fixedIconX, bool isElevated, bool isActivelyDragged, bool isDelHovered, bool isSwHovered)
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

            // 0. Embedded pure-white single digit & hairline divider (aligned identically to rail digit; fades out during split)
            float p = dragSplitProgress;
            if (!isActivelyDragged && p < 0.65f)
            {
                float embedVis = Math.Max(0.0f, 1.0f - (p / 0.55f));
                int rankX = x + (int)(7 * DpiScale);
                int rankW = (int)(22 * DpiScale);
                Color rankCol = BlendColor(cardBg, Color.FromArgb(240, 245, 255), embedVis);
                using (var rankFont = DpiScaleHelper.CreateScaledFont("Segoe UI", 11.5F, FontStyle.Bold, DpiScale))
                {
                    Rectangle rankRect = new Rectangle(rankX, y, rankW, h);
                    TextRenderer.DrawText(g, displayRank.ToString(), rankFont, rankRect, rankCol,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                }

                int sepX = rankX + rankW + (int)(8 * DpiScale);
                int sepMarginY = (int)(14 * DpiScale);
                int sepAlpha = isElevated
                    ? Math.Max(0, Math.Min(255, (int)(70 * embedVis)))
                    : Math.Max(0, Math.Min(255, (int)(255 * embedVis)));
                Color sepCol = isElevated
                    ? Color.FromArgb(sepAlpha, 255, 255, 255)
                    : Color.FromArgb(sepAlpha, 44, 50, 66);
                using (var sepPen = new Pen(sepCol, 1.0f))
                {
                    g.DrawLine(sepPen, sepX, y + sepMarginY, sepX, y + h - sepMarginY);
                }
            }

            // 1. Icon (constant screen X passed from OnPaint — zero horizontal jitter across split animation!)
            int iconBoxSize = (int)(22 * DpiScale);
            int iconX = fixedIconX;
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

            // 3.5 Color Swatch Button to the left of Delete Button (dual-color ring if unlinked)
            int swW = Math.Max(14, (int)(20 * DpiScale));
            int swX = delX - swW - (int)(6 * DpiScale);
            int swY = y + (h - swW) / 2;
            if (isSwHovered)
            {
                using (var swHoverPath = RoundedCard.GetRoundedRectangle(new Rectangle(swX, swY, swW, swW), Math.Max(3, swW / 2)))
                using (var swHoverBg = new SolidBrush(Color.FromArgb(40, 255, 255, 255)))
                {
                    g.FillPath(swHoverBg, swHoverPath);
                }
            }
            int dotD = Math.Max(8, (int)(11 * DpiScale));
            int dotX = swX + (swW - dotD) / 2;
            int dotY = swY + (swW - dotD) / 2;
            Color outerTrayCol = (!item.LinkTrayColorToTheme && !item.TrayAccentColor.IsEmpty) ? item.TrayAccentColor : item.AccentColor;
            using (var innerBrush = new SolidBrush(item.AccentColor))
            {
                g.FillEllipse(innerBrush, dotX, dotY, dotD, dotD);
            }
            Color ringColor = (!item.LinkTrayColorToTheme && outerTrayCol.ToArgb() != item.AccentColor.ToArgb())
                ? outerTrayCol
                : Color.FromArgb(140, 255, 255, 255);
            using (var ringPen = new Pen(ringColor, Math.Max(1.2f, 1.5f * DpiScale)))
            {
                g.DrawEllipse(ringPen, dotX, dotY, dotD, dotD);
            }

            int lastRightX = swX;

            // 4. Battery / Status Pill before Delete Button (三态：离线 / 待机 / 在线)
            string battStr;
            Color battColor;
            if (!item.IsConnected)
            {
                battStr = item.Battery > 0 ? (item.Battery + "% 离线") : "离线";
                battColor = Color.FromArgb(140, 145, 155);
            }
            else if (item.IsSleeping)
            {
                battStr = item.Battery > 0 ? (item.Battery + "% 待机") : "待机";
                battColor = Color.FromArgb(120, 150, 195);
            }
            else
            {
                battStr = item.Battery > 0 ? (item.Battery + "%") : "--";
                battColor = item.AccentColor;
            }
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
