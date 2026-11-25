// ============================================================================
// 模块名称: SlidingSegmentTrack.cs
// 所属区域: UI/Controls (基础视觉控件层)
// 核心职责:
//   平滑滑动分段胶囊选择条控件，支持 60 FPS 三次缓出 (Cubic Ease-Out) 选中胶囊平移与宽幅自适配过渡、
//   内置末尾“自定义”内联数字编辑槽位与滚轮/键盘步进调节，适配 Per-Monitor V2 DPI 无极矢量缩放与零依赖纯 GDI/GDI+ 渲染。
// 依赖关系: Helpers.DpiScaleHelper, RoundedCard
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
    public class SlidingSegmentTrack : Control
    {
        private int[] optionValues = new int[0];
        private string[] optionLabels = new string[0];
        private int selectedIndex = -1;
        private int hoveredIndex = -1;

        private float thumbX = 0f;
        private float thumbW = 0f;
        private float animStartX = 0f;
        private float animTargetX = 0f;
        private float animStartW = 0f;
        private float animTargetW = 0f;
        private DateTime animStartTime;
        private Timer animTimer;
        private const int SlideDurationMs = 180;

        private bool enableCustomSlot = false;
        private int customMinValue = 2;
        private int customMaxValue = 16;
        private int preEditValue = 3;
        private bool isCustomEditing = false;
        private string editBuffer = "";
        private bool caretVisible = true;
        private Timer caretTimer;
        private bool committingInlineEdit = false;

        public float DpiScale { get; set; }
        public int SelectedValue { get; private set; }
        public float CurrentThumbX { get { return thumbX; } }
        public int OptionCount { get { return optionValues.Length; } }
        public bool HasCustomSlot { get { return enableCustomSlot; } }
        public bool IsCustomEditing { get { return isCustomEditing; } }
        public int TotalSlotCount { get { return optionValues.Length + (enableCustomSlot ? 1 : 0); } }

        public Color TrackColor { get; set; }
        public Color TrackBorderColor { get; set; }
        public Color ThumbFillColor { get; set; }
        public Color ThumbBorderColor { get; set; }
        public Color ActiveTextColor { get; set; }
        public Color InactiveTextColor { get; set; }
        public Color HoverFillColor { get; set; }

        public event Action<int> SelectedValueChanged;

        public SlidingSegmentTrack()
        {
            DpiScale = 1.0f;
            TrackColor = Color.FromArgb(16, 18, 25);
            TrackBorderColor = Color.FromArgb(38, 44, 58);
            HoverFillColor = Color.FromArgb(26, 30, 42);
            ThumbFillColor = Color.FromArgb(20, 56, 40);
            ThumbBorderColor = Color.FromArgb(34, 112, 74);
            ActiveTextColor = Color.FromArgb(0, 230, 118);
            InactiveTextColor = Color.FromArgb(155, 167, 190);

            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.Selectable, true);
            DoubleBuffered = true;
            TabStop = true;
            Cursor = Cursors.Hand;
        }

        public void SetOptions(int[] values, string[] labels, int initialValue)
        {
            SetOptions(values, labels, initialValue, false, 2, 16);
        }

        public void SetOptions(int[] values, string[] labels, int initialValue, bool enableCustom, int customMin, int customMax)
        {
            optionValues = values ?? new int[0];
            optionLabels = labels ?? new string[0];
            enableCustomSlot = enableCustom;
            customMinValue = Math.Max(1, customMin);
            customMaxValue = Math.Max(customMinValue, customMax);

            int presetIdx = FindIndexByValue(initialValue);
            if (presetIdx >= 0)
            {
                selectedIndex = presetIdx;
                SelectedValue = optionValues[presetIdx];
            }
            else if (enableCustomSlot)
            {
                selectedIndex = optionValues.Length;
                SelectedValue = Math.Max(customMinValue, Math.Min(customMaxValue, initialValue));
            }
            else
            {
                selectedIndex = -1;
                SelectedValue = initialValue;
            }
            SnapThumbToSelected();
            Invalidate();
        }

        public void SelectValue(int value, bool animate)
        {
            int nextIdx = FindIndexByValue(value);
            if (nextIdx >= 0)
            {
                SelectedValue = optionValues[nextIdx];
                selectedIndex = nextIdx;
            }
            else if (enableCustomSlot)
            {
                SelectedValue = Math.Max(customMinValue, Math.Min(customMaxValue, value));
                selectedIndex = optionValues.Length;
            }
            else
            {
                SelectedValue = value;
                selectedIndex = -1;
            }

            if (selectedIndex < 0)
            {
                if (animTimer != null && animTimer.Enabled) animTimer.Stop();
                Invalidate();
                return;
            }

            float targetX, targetW;
            int dummyPad, dummyH;
            GetSlotBounds(selectedIndex, out targetX, out targetW, out dummyPad, out dummyH);
            if (!animate || !IsHandleCreated || !Visible)
            {
                if (animTimer != null && animTimer.Enabled) animTimer.Stop();
                thumbX = targetX;
                thumbW = targetW;
                Invalidate();
                return;
            }

            if (Math.Abs(thumbX - targetX) < 0.5f && Math.Abs(thumbW - targetW) < 0.5f)
            {
                if (animTimer != null && animTimer.Enabled) animTimer.Stop();
                thumbX = targetX;
                thumbW = targetW;
                Invalidate();
                return;
            }

            animStartX = thumbX;
            animTargetX = targetX;
            animStartW = (thumbW > 1f) ? thumbW : targetW;
            animTargetW = targetW;
            animStartTime = DateTime.UtcNow;

            if (animTimer == null)
            {
                animTimer = new Timer();
                animTimer.Interval = 15;
                animTimer.Tick += (s, e) => {
                    if (IsDisposed)
                    {
                        animTimer.Stop();
                        return;
                    }
                    double elapsed = (DateTime.UtcNow - animStartTime).TotalMilliseconds;
                    float progress = (float)(elapsed / SlideDurationMs);
                    if (progress >= 1.0f)
                    {
                        animTimer.Stop();
                        thumbX = animTargetX;
                        thumbW = animTargetW;
                        Invalidate();
                        return;
                    }
                    float rem = 1.0f - progress;
                    float ease = 1.0f - (rem * rem * rem);
                    thumbX = animStartX + (animTargetX - animStartX) * ease;
                    thumbW = animStartW + (animTargetW - animStartW) * ease;
                    Invalidate();
                };
            }

            if (!animTimer.Enabled) animTimer.Start();
        }

        public void SnapThumbToSelected()
        {
            if (animTimer != null && animTimer.Enabled) animTimer.Stop();
            int total = TotalSlotCount;
            if (selectedIndex >= 0 && selectedIndex < total)
            {
                int dummyPad, dummyH;
                GetSlotBounds(selectedIndex, out thumbX, out thumbW, out dummyPad, out dummyH);
            }
            else if (total > 0)
            {
                int dummyPad, dummyH;
                GetSlotBounds(0, out thumbX, out thumbW, out dummyPad, out dummyH);
            }
            else
            {
                thumbX = Math.Max(2, (int)(3 * DpiScale));
                thumbW = 10f;
            }
        }

        public void CommitPendingCustomInput()
        {
            if (IsCustomEditing)
            {
                CommitInlineEdit(true);
            }
        }

        private void StopCaretTimer()
        {
            if (caretTimer != null && caretTimer.Enabled)
            {
                caretTimer.Stop();
            }
        }

        private void BeginInlineEdit()
        {
            if (!enableCustomSlot) return;
            preEditValue = SelectedValue;
            isCustomEditing = true;
            editBuffer = "";
            caretVisible = true;

            // 选中胶囊平移至自定义槽位
            selectedIndex = optionValues.Length;
            float sx, sw;
            int trackPad, segH;
            GetSlotBounds(selectedIndex, out sx, out sw, out trackPad, out segH);
            if (!IsHandleCreated || !Visible)
            {
                thumbX = sx;
                thumbW = sw;
            }
            else if (Math.Abs(thumbX - sx) > 0.5f || Math.Abs(thumbW - sw) > 0.5f)
            {
                animStartX = thumbX;
                animTargetX = sx;
                animStartW = (thumbW > 1f) ? thumbW : sw;
                animTargetW = sw;
                animStartTime = DateTime.UtcNow;
                if (animTimer == null)
                {
                    SelectValue(SelectedValue, true);
                }
                else if (!animTimer.Enabled)
                {
                    animTimer.Start();
                }
            }

            if (IsHandleCreated)
            {
                if (caretTimer == null)
                {
                    caretTimer = new Timer();
                    caretTimer.Interval = 530;
                    caretTimer.Tick += (s, e) => {
                        if (IsDisposed || !isCustomEditing)
                        {
                            StopCaretTimer();
                            return;
                        }
                        caretVisible = !caretVisible;
                        Invalidate();
                    };
                }
                if (!caretTimer.Enabled) caretTimer.Start();
                this.Focus();
            }
            Invalidate();
        }

        private void CommitInlineEdit(bool raiseEvent)
        {
            if (committingInlineEdit) return;
            committingInlineEdit = true;
            try
            {
                StopCaretTimer();
                isCustomEditing = false;
                string raw = (editBuffer ?? "").Trim();
                editBuffer = "";

                int parsed;
                if (!string.IsNullOrEmpty(raw) && int.TryParse(raw, out parsed))
                {
                    int clamped = Math.Max(customMinValue, Math.Min(customMaxValue, parsed));
                    bool changed = (clamped != preEditValue) || (clamped != SelectedValue);
                    SelectValue(clamped, true);
                    if (raiseEvent && changed)
                    {
                        var handler = SelectedValueChanged;
                        if (handler != null) handler(clamped);
                    }
                }
                else
                {
                    SelectValue(preEditValue, true);
                }
                Invalidate();
            }
            finally
            {
                committingInlineEdit = false;
            }
        }

        private void CancelInlineEdit()
        {
            if (committingInlineEdit) return;
            committingInlineEdit = true;
            try
            {
                StopCaretTimer();
                isCustomEditing = false;
                editBuffer = "";
                SelectValue(preEditValue, true);
                Invalidate();
            }
            finally
            {
                committingInlineEdit = false;
            }
        }

        protected override bool IsInputKey(Keys keyData)
        {
            if (isCustomEditing)
            {
                Keys key = keyData & Keys.KeyCode;
                if (key == Keys.Enter || key == Keys.Escape || key == Keys.Back ||
                    key == Keys.Up || key == Keys.Down || key == Keys.Left || key == Keys.Right)
                {
                    return true;
                }
            }
            return base.IsInputKey(keyData);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (!isCustomEditing) return;

            if (char.IsDigit(e.KeyChar))
            {
                editBuffer = (editBuffer.Length >= 2) ? e.KeyChar.ToString() : (editBuffer + e.KeyChar);
                caretVisible = true;
                Invalidate();
                e.Handled = true;
            }
            else if (e.KeyChar == '\b' && editBuffer.Length > 0)
            {
                editBuffer = editBuffer.Substring(0, editBuffer.Length - 1);
                caretVisible = true;
                Invalidate();
                e.Handled = true;
            }
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (!isCustomEditing) return;

            if (e.KeyCode == Keys.Enter)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                CommitInlineEdit(true);
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.Handled = true;
                e.SuppressKeyPress = true;
                CancelInlineEdit();
            }
            else if (e.KeyCode == Keys.Up || e.KeyCode == Keys.Down)
            {
                e.Handled = true;
                int cur;
                if (string.IsNullOrEmpty(editBuffer) || !int.TryParse(editBuffer, out cur))
                {
                    cur = SelectedValue;
                }
                cur += (e.KeyCode == Keys.Up) ? 1 : -1;
                cur = Math.Max(customMinValue, Math.Min(customMaxValue, cur));
                editBuffer = cur.ToString();
                caretVisible = true;
                Invalidate();
            }
        }

        protected override void OnLostFocus(EventArgs e)
        {
            base.OnLostFocus(e);
            if (isCustomEditing)
            {
                CommitInlineEdit(true);
            }
        }

        private int FindIndexByValue(int value)
        {
            for (int i = 0; i < optionValues.Length; i++)
            {
                if (optionValues[i] == value) return i;
            }
            return -1;
        }

        private void GetSlotBounds(int slotIndex, out float sx, out float sw, out int trackPad, out int segH)
        {
            trackPad = Math.Max(2, (int)(3 * DpiScale));
            int pillGap = Math.Max(2, (int)((enableCustomSlot ? 2 : 3) * DpiScale));
            int total = Math.Max(1, TotalSlotCount);
            float availW = Math.Max(12f, Width - 1 - trackPad * 2 - pillGap * (total - 1));
            segH = Math.Max(10, Height - 1 - trackPad * 2);

            if (!enableCustomSlot || total <= 1)
            {
                sw = availW / total;
                sx = trackPad + slotIndex * (sw + pillGap);
                return;
            }

            float extraW = Math.Min(availW * 0.14f, 14f * DpiScale);
            float presetW = Math.Max(10f, (availW - extraW) / total);
            float customW = Math.Max(presetW, presetW + extraW);
            bool isCustom = (slotIndex >= optionValues.Length);
            sw = isCustom ? customW : presetW;
            sx = trackPad + Math.Min(slotIndex, optionValues.Length) * (presetW + pillGap);
        }

        private int HitTestSegment(Point pt)
        {
            if (pt.Y < 0 || pt.Y > Height) return -1;
            int total = TotalSlotCount;
            for (int i = 0; i < total; i++)
            {
                float sx, sw;
                int trackPad, segH;
                GetSlotBounds(i, out sx, out sw, out trackPad, out segH);
                if (pt.X >= sx && pt.X <= sx + sw)
                {
                    return i;
                }
            }
            return -1;
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            SnapThumbToSelected();
            Invalidate();
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int hit = HitTestSegment(e.Location);
            if (hit != hoveredIndex)
            {
                hoveredIndex = hit;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoveredIndex != -1)
            {
                hoveredIndex = -1;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            int hit = HitTestSegment(e.Location);
            if (hit >= 0 && hit < optionValues.Length)
            {
                if (IsCustomEditing)
                {
                    CommitInlineEdit(false);
                }
                int val = optionValues[hit];
                if (val != SelectedValue || selectedIndex != hit)
                {
                    SelectValue(val, true);
                    var handler = SelectedValueChanged;
                    if (handler != null) handler(val);
                }
            }
            else if (enableCustomSlot && hit == optionValues.Length)
            {
                BeginInlineEdit();
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!enableCustomSlot || e.Delta == 0) return;
            if (hoveredIndex == optionValues.Length || selectedIndex == optionValues.Length || IsCustomEditing)
            {
                int nextVal = Math.Max(customMinValue, Math.Min(customMaxValue, SelectedValue + (e.Delta > 0 ? 1 : -1)));
                if (nextVal != SelectedValue)
                {
                    SelectValue(nextVal, true);
                    if (IsCustomEditing)
                    {
                        editBuffer = nextVal.ToString();
                        caretVisible = true;
                    }
                    var handler = SelectedValueChanged;
                    if (handler != null) handler(nextVal);
                }
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

            Color parentBg = (Parent != null) ? Parent.BackColor : BackColor;
            g.Clear(parentBg);

            int w = Math.Max(4, Width - 1);
            int h = Math.Max(4, Height - 1);
            int trackRadius = Math.Max(3, (int)(7 * DpiScale));

            using (var trackPath = RoundedCard.GetRoundedRectangle(new Rectangle(0, 0, w, h), trackRadius))
            {
                using (var trackBrush = new SolidBrush(TrackColor)) g.FillPath(trackBrush, trackPath);
                using (var trackPen = new Pen(TrackBorderColor, 1.0f)) g.DrawPath(trackPen, trackPath);
            }

            int pillRadius = Math.Max(2, (int)(5 * DpiScale));
            int total = TotalSlotCount;
            if (total <= 0) return;

            // Hover background on non-selected slot
            if (hoveredIndex >= 0 && hoveredIndex < total && hoveredIndex != selectedIndex)
            {
                float hX, hW;
                int trackPad, segH;
                GetSlotBounds(hoveredIndex, out hX, out hW, out trackPad, out segH);
                Rectangle hoverRect = new Rectangle((int)Math.Round(hX), trackPad, Math.Max(4, (int)Math.Round(hW)), segH);
                using (var hoverPath = RoundedCard.GetRoundedRectangle(hoverRect, pillRadius))
                using (var hoverBrush = new SolidBrush(HoverFillColor))
                {
                    g.FillPath(hoverBrush, hoverPath);
                }
            }

            // Sliding active capsule (with refined luminous focus border while inline editing)
            if (selectedIndex >= 0 && selectedIndex < total)
            {
                float sX, sW;
                int trackPad, segH;
                GetSlotBounds(selectedIndex, out sX, out sW, out trackPad, out segH);
                int tx = (int)Math.Round(thumbX);
                int tw = Math.Max(4, (int)Math.Round(thumbW > 1f ? thumbW : sW));
                Rectangle thumbRect = new Rectangle(tx, trackPad, tw, segH);
                bool activeEditFocus = isCustomEditing && selectedIndex == optionValues.Length;
                Color capFill = activeEditFocus ? Color.FromArgb(18, 64, 45) : ThumbFillColor;
                Color capBorder = activeEditFocus ? Color.FromArgb(62, 210, 136) : ThumbBorderColor;
                float capStroke = activeEditFocus ? 1.3f : 1.0f;
                using (var thumbPath = RoundedCard.GetRoundedRectangle(thumbRect, pillRadius))
                {
                    using (var thumbBrush = new SolidBrush(capFill)) g.FillPath(thumbBrush, thumbPath);
                    using (var thumbPen = new Pen(capBorder, capStroke)) g.DrawPath(thumbPen, thumbPath);
                }
            }

            // Slot labels with smooth proximity color interpolation (cached font: never dispose Control.DefaultFont!)
            Font labelFont = DpiScaleHelper.GetCachedScaledFont("Microsoft YaHei UI", 8.2F, FontStyle.Bold, DpiScale);
            for (int i = 0; i < total; i++)
            {
                float sx, sw;
                int trackPad, segH;
                GetSlotBounds(i, out sx, out sw, out trackPad, out segH);

                bool isCustom = (enableCustomSlot && i == optionValues.Length);
                Rectangle textRect = new Rectangle((int)Math.Round(sx), trackPad, Math.Max(4, (int)Math.Round(sw)), segH);

                if (isCustom && isCustomEditing)
                {
                    string displayText = string.IsNullOrEmpty(editBuffer) ? preEditValue.ToString() : editBuffer;
                    Color editColor = string.IsNullOrEmpty(editBuffer) ? Color.FromArgb(120, 0, 230, 118) : ActiveTextColor;
                    TextRenderer.DrawText(g, displayText, labelFont, textRect, editColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                    if (caretVisible)
                    {
                        Size measured = TextRenderer.MeasureText(g, displayText, labelFont, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                        float caretX = sx + sw / 2f + measured.Width / 2f + 1.5f;
                        float caretH = Math.Max(10f, measured.Height - 2f);
                        float caretTop = trackPad + (segH - caretH) / 2f;
                        using (Pen caretPen = new Pen(Color.FromArgb(230, 110, 255, 180), 1.4f))
                        {
                            g.DrawLine(caretPen, caretX, caretTop, caretX, caretTop + caretH);
                        }
                    }
                    continue;
                }

                string lbl;
                if (isCustom)
                {
                    lbl = (selectedIndex == optionValues.Length) ? (SelectedValue.ToString() + " 个") : "自定义";
                }
                else
                {
                    lbl = (i < optionLabels.Length && optionLabels[i] != null) ? optionLabels[i] : optionValues[i].ToString();
                }

                float prox = (selectedIndex >= 0) ? Math.Max(0f, 1.0f - Math.Abs(thumbX - sx) / Math.Max(1.0f, sw)) : 0f;
                Color baseInactive = (isCustom && hoveredIndex == i) ? BlendColor(InactiveTextColor, ActiveTextColor, 0.35f) : InactiveTextColor;
                Color fg = BlendColor(baseInactive, ActiveTextColor, prox);
                TextRenderer.DrawText(g, lbl, labelFont, textRect, fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (animTimer != null)
                {
                    try { animTimer.Stop(); animTimer.Dispose(); } catch { }
                    animTimer = null;
                }
                if (caretTimer != null)
                {
                    try { caretTimer.Stop(); caretTimer.Dispose(); } catch { }
                    caretTimer = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
