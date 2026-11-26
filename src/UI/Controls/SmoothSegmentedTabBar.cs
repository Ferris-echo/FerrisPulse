// ============================================================================
// 模块名称: SmoothSegmentedTabBar.cs
// 所属区域: UI/Controls (复合展示控件层)
// 核心职责:
//   现代化平滑分段选项卡栏，支持多个连接设备的水平分栏、60FPS 滑动光晕指示条动画、
//   品牌胶囊徽章（雷蛇/雷柏/VGN/NuPhy等）、主设备图钉标记与电量百分比对齐展示。
// 依赖关系: Models.MouseBatteryInfo, Models.DeviceCategory, RoundedCard
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
    public class SmoothSegmentedTabBar : Control
    {
        private List<MouseBatteryInfo> devices = new List<MouseBatteryInfo>();
        private int selectedIndex = 0;
        private int hoverIndex = -1;
        private float currentIndicatorX = 0f;
        private float currentIndicatorW = 0f;
        private float targetIndicatorX = 0f;
        private float targetIndicatorW = 0f;
        private Color currentIndicatorColor = Color.FromArgb(0, 230, 118);
        private Color targetIndicatorColor = Color.FromArgb(0, 230, 118);
        private Timer animTimer;
        public float DpiScale { get; set; }
        public event Action<MouseBatteryInfo> DeviceSelected;

        public SmoothSegmentedTabBar()
        {
            DpiScale = 1.0f;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            DoubleBuffered = true;

            animTimer = new Timer();
            animTimer.Interval = 15;
            animTimer.Tick += (s, e) =>
            {
                float dx = targetIndicatorX - currentIndicatorX;
                float dw = targetIndicatorW - currentIndicatorW;
                if (Math.Abs(dx) < 0.6f && Math.Abs(dw) < 0.6f)
                {
                    currentIndicatorX = targetIndicatorX;
                    currentIndicatorW = targetIndicatorW;
                    currentIndicatorColor = targetIndicatorColor;
                    animTimer.Stop();
                }
                else
                {
                    currentIndicatorX += dx * 0.32f;
                    currentIndicatorW += dw * 0.32f;
                    currentIndicatorColor = Color.FromArgb(
                        Math.Max(0, Math.Min(255, (int)(currentIndicatorColor.R + (targetIndicatorColor.R - currentIndicatorColor.R) * 0.32f))),
                        Math.Max(0, Math.Min(255, (int)(currentIndicatorColor.G + (targetIndicatorColor.G - currentIndicatorColor.G) * 0.32f))),
                        Math.Max(0, Math.Min(255, (int)(currentIndicatorColor.B + (targetIndicatorColor.B - currentIndicatorColor.B) * 0.32f)))
                    );
                }
                Invalidate();
            };
        }

        public void SetDevices(List<MouseBatteryInfo> newDevices, string currentSelectedId)
        {
            this.devices = newDevices != null ? new List<MouseBatteryInfo>(newDevices) : new List<MouseBatteryInfo>();
            if (this.devices.Count == 0)
            {
                this.Visible = false;
                return;
            }
            this.Visible = true;

            int newSel = 0;
            if (!string.IsNullOrEmpty(currentSelectedId))
            {
                int idx = this.devices.FindIndex(d => d.DeviceId == currentSelectedId);
                if (idx >= 0) newSel = idx;
            }
            if (newSel >= this.devices.Count) newSel = 0;
            selectedIndex = newSel;
            UpdateTargetMetrics(false);
            Invalidate();
        }

        public void SelectDevice(string deviceId)
        {
            if (devices == null || devices.Count == 0) return;
            int idx = devices.FindIndex(d => d.DeviceId == deviceId);
            if (idx >= 0 && idx != selectedIndex)
            {
                selectedIndex = idx;
                UpdateTargetMetrics(true);
            }
        }

        private void UpdateTargetMetrics(bool animate)
        {
            if (devices == null || devices.Count == 0) return;
            int count = devices.Count;
            int pad = (int)(3 * DpiScale);
            int availW = Width - (pad * 2);
            int tabW = Math.Max(10, availW / count);

            targetIndicatorX = pad + (selectedIndex * tabW);
            targetIndicatorW = tabW;
            targetIndicatorColor = devices[selectedIndex].BrandColor;

            if (!animate || currentIndicatorW <= 0f)
            {
                currentIndicatorX = targetIndicatorX;
                currentIndicatorW = targetIndicatorW;
                currentIndicatorColor = targetIndicatorColor;
                animTimer.Stop();
            }
            else
            {
                animTimer.Start();
            }
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            UpdateTargetMetrics(false);
        }

        private int GetTabAt(int x)
        {
            if (devices == null || devices.Count == 0) return -1;
            int count = devices.Count;
            int pad = (int)(3 * DpiScale);
            int availW = Width - (pad * 2);
            int tabW = Math.Max(10, availW / count);
            if (x < pad || x > Width - pad) return -1;
            int idx = (x - pad) / tabW;
            if (idx >= 0 && idx < count) return idx;
            return -1;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int idx = GetTabAt(e.X);
            if (idx != hoverIndex)
            {
                hoverIndex = idx;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoverIndex != -1)
            {
                hoverIndex = -1;
                Invalidate();
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                int idx = GetTabAt(e.X);
                if (idx >= 0 && idx < devices.Count)
                {
                    if (idx != selectedIndex)
                    {
                        selectedIndex = idx;
                        UpdateTargetMetrics(true);
                        if (DeviceSelected != null)
                        {
                            DeviceSelected(devices[selectedIndex]);
                        }
                    }
                }
            }
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color bg = Parent != null ? Parent.BackColor : Color.FromArgb(18, 19, 23);
            g.Clear(bg);

            if (devices == null || devices.Count == 0) return;
            int count = devices.Count;
            int pad = (int)(3 * DpiScale);
            int availW = Width - (pad * 2);
            int tabW = Math.Max(10, availW / count);
            int tabH = Height - (pad * 2);

            // 1. Draw track container
            Rectangle trackRect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath trackPath = RoundedCard.GetRoundedRectangle(trackRect, (int)(8 * DpiScale)))
            {
                using (SolidBrush tb = new SolidBrush(Color.FromArgb(24, 26, 33))) g.FillPath(tb, trackPath);
                using (Pen tp = new Pen(Color.FromArgb(38, 42, 54), 1.0f)) g.DrawPath(tp, trackPath);
            }

            // 2. Draw Hover background if hovering non-selected
            if (hoverIndex >= 0 && hoverIndex < count && hoverIndex != selectedIndex)
            {
                Rectangle hRect = new Rectangle(pad + (hoverIndex * tabW), pad, tabW, tabH);
                using (GraphicsPath hPath = RoundedCard.GetRoundedRectangle(hRect, (int)(6 * DpiScale)))
                using (SolidBrush hb = new SolidBrush(Color.FromArgb(35, 39, 50)))
                {
                    g.FillPath(hb, hPath);
                }
            }

            // 3. Draw Active Sliding Pill
            if (currentIndicatorW > 0)
            {
                Rectangle indRectI = new Rectangle((int)currentIndicatorX, pad, (int)currentIndicatorW, tabH);
                if (indRectI.Width > 4 && indRectI.Height > 4)
                {
                    using (GraphicsPath indPath = RoundedCard.GetRoundedRectangle(indRectI, (int)(6 * DpiScale)))
                    {
                        Color pillFill = Color.FromArgb(28, currentIndicatorColor);
                        using (SolidBrush pb = new SolidBrush(pillFill)) g.FillPath(pb, indPath);
                        using (Pen pp = new Pen(currentIndicatorColor, 1.6f)) g.DrawPath(pp, indPath);
                    }
                }
            }

            // 4. Render Tab Contents
            for (int i = 0; i < count; i++)
            {
                var dev = devices[i];
                bool isSel = (i == selectedIndex);
                int tabX = pad + (i * tabW);
                Rectangle tRect = new Rectangle(tabX, pad, tabW, tabH);

                // Brand Pill: [雷蛇] or [雷柏]
                int bpW = (int)(32 * DpiScale);
                int bpH = (int)(18 * DpiScale);
                int bpY = tRect.Y + (tabH - bpH) / 2;
                Rectangle bpRect = new Rectangle(tabX + (int)(6 * DpiScale), bpY, bpW, bpH);
                string bpText = dev.Brand == "Rapoo" ? "雷柏" : (dev.Brand == "VGN" ? "VGN" : (dev.Brand == "Razer" ? "雷蛇" : (dev.Category == DeviceCategory.Headset ? "耳机" : (dev.Category == DeviceCategory.Gamepad ? "手柄" : (dev.Category == DeviceCategory.Keyboard ? "键盘" : (dev.Category == DeviceCategory.Mouse ? (!string.IsNullOrEmpty(dev.Brand) && dev.Brand != "Custom" ? dev.Brand : "鼠标") : "外设"))))));
                Color bpBg = isSel ? (dev.Brand == "Rapoo" ? Color.FromArgb(0, 150, 230) : (dev.Brand == "VGN" ? Color.FromArgb(240, 110, 0) : ((dev.Brand == "NuPhy" || dev.Category == DeviceCategory.Keyboard) ? Color.FromArgb(0, 229, 255) : Color.FromArgb(0, 200, 83)))) :
                                      (dev.Brand == "Rapoo" ? Color.FromArgb(24, 44, 60) : (dev.Brand == "VGN" ? Color.FromArgb(48, 30, 16) : ((dev.Brand == "NuPhy" || dev.Category == DeviceCategory.Keyboard) ? Color.FromArgb(12, 40, 48) : Color.FromArgb(30, 42, 36))));
                Color bpFg = isSel ? (dev.Brand == "Rapoo" ? Color.FromArgb(10, 24, 35) : (dev.Brand == "VGN" ? Color.FromArgb(30, 15, 0) : ((dev.Brand == "NuPhy" || dev.Category == DeviceCategory.Keyboard) ? Color.FromArgb(5, 25, 30) : Color.FromArgb(10, 24, 15)))) : dev.BrandColor;

                using (GraphicsPath bpPath = RoundedCard.GetRoundedRectangle(bpRect, (int)(4 * DpiScale)))
                using (SolidBrush bpBrush = new SolidBrush(bpBg))
                    g.FillPath(bpBrush, bpPath);

                using (Font bpFont = DpiScaleHelper.CreateScaledFont("Microsoft YaHei UI", 7.2f, FontStyle.Bold, DpiScale))
                {
                    TextRenderer.DrawText(g, bpText, bpFont, bpRect, bpFg,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }

                // 1. Battery % ALWAYS anchored to far right for perfect vertical alignment across tabs
                string bStr = dev.BatteryPercent > 0 ? (dev.BatteryPercent + "%") : "--%";
                int curRight = tabX + tabW - (int)(8 * DpiScale);
                using (Font bFont = DpiScaleHelper.CreateScaledFont("Microsoft YaHei UI", 8.2f, FontStyle.Bold, DpiScale))
                {
                    Size szBatt = TextRenderer.MeasureText(g, bStr, bFont, Size.Empty, TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
                    int battW = szBatt.Width + (int)(4 * DpiScale);
                    int battX = curRight - battW;
                    Rectangle battRect = new Rectangle(battX, tRect.Y, battW, tabH);
                    Color bCol = isSel ? dev.BrandColor : Color.FromArgb(160, 170, 185);
                    TextRenderer.DrawText(g, bStr, bFont, battRect, bCol,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                    curRight = battX - (int)(4 * DpiScale);
                }

                // 2. Micro-capsule for Primary Device
                if (count > 1 && dev.IsPrimary)
                {
                    int pinW = (int)(20 * DpiScale);
                    int pinH = (int)(17 * DpiScale);
                    int pinY = tRect.Y + (tabH - pinH) / 2;
                    Rectangle pinRect = new Rectangle(curRight - pinW, pinY, pinW, pinH);
                    Color pBg = Color.FromArgb(isSel ? 35 : 20, dev.BrandColor);
                    Color pBorder = Color.FromArgb(isSel ? 150 : 80, dev.BrandColor);

                    using (GraphicsPath pPath = RoundedCard.GetRoundedRectangle(pinRect, (int)(4 * DpiScale)))
                    {
                        using (SolidBrush pb = new SolidBrush(pBg)) g.FillPath(pb, pPath);
                        using (Pen pp = new Pen(pBorder, 1f)) g.DrawPath(pp, pPath);
                    }

                    using (Font pFont = DpiScaleHelper.CreateScaledFont("Segoe UI Emoji", 7.0f, FontStyle.Regular, DpiScale))
                    {
                        TextRenderer.DrawText(g, "📌", pFont, pinRect, dev.BrandColor,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                    }
                    curRight = pinRect.Left - (int)(5 * DpiScale);
                }

                // 3. Model Short Name
                int nameLeft = bpRect.Right + (int)(6 * DpiScale);
                int nameW = curRight - nameLeft;
                if (nameW > 10)
                {
                    Rectangle nameRect = new Rectangle(nameLeft, tRect.Y, nameW, tabH);
                    Color nameColor = isSel ? Color.White : Color.FromArgb(175, 185, 200);
                    using (Font nFont = DpiScaleHelper.CreateScaledFont("Microsoft YaHei UI", 8.0f, isSel ? FontStyle.Bold : FontStyle.Regular, DpiScale))
                    {
                        TextRenderer.DrawText(g, dev.DisplayName, nFont, nameRect, nameColor,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                    }
                }
            }
        }
    }
}
