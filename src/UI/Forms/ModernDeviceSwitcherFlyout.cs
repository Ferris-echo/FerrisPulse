// ============================================================================
// 模块名称: ModernDeviceSwitcherFlyout.cs
// 所属区域: UI/Forms (业务对话框层)
// 核心职责:
//   现代化设备切换弹出飞出窗体（Flyout），具备系统阴影（CS_DROPSHADOW）、指数渐变平滑展开/折叠退出动画、多设备卡片悬停高亮与一键切换。
// 依赖关系: Models.MouseBatteryInfo, Theme.ModernThemeHelper, Controls.RoundedCard
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
    public class ModernDeviceSwitcherFlyout : DpiAwareForm
    {
        public List<MouseBatteryInfo> Devices = new List<MouseBatteryInfo>();
        public MouseBatteryInfo SelectedDevice = null;
        public new float DpiScale
        {
            get { return this.dpiScale; }
            set { this.dpiScale = value > 0 ? value : 1.0f; }
        }
        public Action<MouseBatteryInfo> OnSelectDevice;

        private int hoveredCardIdx = -1;
        private System.Windows.Forms.Timer animTimer;
        private int fullTargetHeight = 0;
        private double currentAnimProgress = 0.0;
        private bool isClosing = false;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ClassStyle |= 0x00020000; // CS_DROPSHADOW
                return cp;
            }
        }

        public ModernDeviceSwitcherFlyout() : this(0f)
        {
        }

        public ModernDeviceSwitcherFlyout(float scale) : base(scale)
        {
            if (scale > 0) this.dpiScale = scale;
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.Manual;
            this.BackColor = Color.FromArgb(20, 23, 31);
            this.DoubleBuffered = true;
            this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            animTimer = new System.Windows.Forms.Timer();
            animTimer.Interval = 12;
            animTimer.Tick += AnimTimer_Tick;

            this.Deactivate += (s, e) => {
                CloseSmoothly();
            };
        }

        private void AnimTimer_Tick(object sender, EventArgs e)
        {
            if (!isClosing)
            {
                // Opening animation: Exponential ease-out
                currentAnimProgress += (1.0 - currentAnimProgress) * 0.38 + 0.02;
                if (currentAnimProgress >= 0.98)
                {
                    currentAnimProgress = 1.0;
                    animTimer.Stop();
                }
                this.Opacity = Math.Min(1.0, currentAnimProgress * 1.08);
                int curH = Math.Max((int)(24 * DpiScale), (int)(fullTargetHeight * currentAnimProgress));
                this.Height = curH;
                UpdateRegion(this.Width, curH);
            }
            else
            {
                // Closing animation: Smooth slide & fade-out
                currentAnimProgress -= 0.18;
                if (currentAnimProgress <= 0.05)
                {
                    animTimer.Stop();
                    base.Close();
                    return;
                }
                this.Opacity = Math.Max(0.0, currentAnimProgress);
                int curH = Math.Max((int)(20 * DpiScale), (int)(fullTargetHeight * currentAnimProgress));
                this.Height = curH;
                UpdateRegion(this.Width, curH);
            }
        }

        public void CloseSmoothly()
        {
            if (isClosing) return;
            isClosing = true;
            if (!animTimer.Enabled) animTimer.Start();
        }

        public new void Close()
        {
            CloseSmoothly();
        }

        protected override bool ProcessDialogKey(Keys keyData)
        {
            if (keyData == Keys.Escape)
            {
                CloseSmoothly();
                return true;
            }
            return base.ProcessDialogKey(keyData);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            currentAnimProgress = 0.05;
            this.Opacity = 0.05;
            this.Height = (int)(24 * DpiScale);
            UpdateRegion(this.Width, this.Height);
            animTimer.Start();
        }

        private void UpdateRegion(int w, int h)
        {
            try
            {
                using (var path = CreateRoundedPath(new Rectangle(0, 0, w, h), (int)(10 * DpiScale)))
                {
                    this.Region = new Region(path);
                }
            }
            catch { }
        }

        public void CalcSize(int targetWidth)
        {
            int pad = (int)(12 * DpiScale);
            int headerH = (int)(32 * DpiScale);
            int cardH = (int)(56 * DpiScale);
            int cardGap = (int)(8 * DpiScale);

            int count = (Devices != null && Devices.Count > 0) ? Devices.Count : 1;
            fullTargetHeight = pad + headerH + (count * (cardH + cardGap)) + pad;
            this.Size = new Size(targetWidth, fullTargetHeight);
            UpdateRegion(targetWidth, fullTargetHeight);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            int pad = (int)(12 * DpiScale);
            int cardH = (int)(56 * DpiScale);
            int cardGap = (int)(8 * DpiScale);
            int cardW = Width - (pad * 2);

            int newHovCard = -1;
            int cardStartY = pad + (int)(32 * DpiScale);

            if (Devices != null && Devices.Count > 0)
            {
                for (int i = 0; i < Devices.Count; i++)
                {
                    Rectangle cardRect = new Rectangle(pad, cardStartY + i * (cardH + cardGap), cardW, cardH);
                    if (cardRect.Contains(e.Location))
                    {
                        newHovCard = i;
                        break;
                    }
                }
            }

            if (newHovCard != hoveredCardIdx)
            {
                hoveredCardIdx = newHovCard;
                Cursor = (hoveredCardIdx >= 0) ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            hoveredCardIdx = -1;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;

            int pad = (int)(12 * DpiScale);
            int cardH = (int)(56 * DpiScale);
            int cardGap = (int)(8 * DpiScale);
            int cardW = Width - (pad * 2);
            int cardStartY = pad + (int)(32 * DpiScale);

            if (Devices != null && Devices.Count > 0)
            {
                for (int i = 0; i < Devices.Count; i++)
                {
                    Rectangle cardRect = new Rectangle(pad, cardStartY + i * (cardH + cardGap), cardW, cardH);
                    if (cardRect.Contains(e.Location))
                    {
                        var dev = Devices[i];
                        CloseSmoothly();
                        if (OnSelectDevice != null) OnSelectDevice(dev);
                        return;
                    }
                }
            }
        }

        private static GraphicsPath CreateRoundedPath(Rectangle rect, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            if (d > rect.Width) d = rect.Width;
            if (d > rect.Height) d = rect.Height;
            if (d <= 0) { path.AddRectangle(rect); return path; }
            path.AddArc(rect.X, rect.Y, d, d, 180, 90);
            path.AddArc(rect.Right - d, rect.Y, d, d, 270, 90);
            path.AddArc(rect.Right - d, rect.Bottom - d, d, d, 0, 90);
            path.AddArc(rect.X, rect.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int pad = (int)(12 * DpiScale);
            int w = Width;
            int h = Height;

            // 1. Outer Border & Dark Fill
            using (var borderPath = CreateRoundedPath(new Rectangle(0, 0, w - 1, h - 1), (int)(10 * DpiScale)))
            {
                using (var bgBrush = new SolidBrush(Color.FromArgb(20, 23, 31))) g.FillPath(bgBrush, borderPath);
                using (var borderPen = new Pen(Color.FromArgb(48, 54, 72), 1.2f)) g.DrawPath(borderPen, borderPath);
            }

            // 2. Header
            int curY = pad;
            using (var titleFont = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold))
            using (var titleBrush = new SolidBrush(Color.FromArgb(170, 180, 205)))
            {
                g.DrawString("外设切换与监控", titleFont, titleBrush, pad + 2, curY + 2);
            }

            int onlineCount = 0;
            if (Devices != null)
            {
                foreach (var d in Devices) if (d.IsConnected) onlineCount++;
            }
            string statusStr = string.Format("● {0} 个设备在线", onlineCount);
            using (var stFont = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Bold))
            using (var stBrush = new SolidBrush(Color.FromArgb(0, 230, 118)))
            {
                var sz = TextRenderer.MeasureText(statusStr, stFont);
                g.DrawString(statusStr, stFont, stBrush, w - pad - sz.Width, curY + 4);
            }

            curY += (int)(32 * DpiScale);

            // 3. Device Cards
            int cardH = (int)(56 * DpiScale);
            int cardGap = (int)(8 * DpiScale);
            int cardW = w - (pad * 2);

            if (Devices != null && Devices.Count > 0)
            {
                for (int i = 0; i < Devices.Count; i++)
                {
                    var dev = Devices[i];
                    bool isSel = (dev == SelectedDevice);
                    bool isHov = (i == hoveredCardIdx);
                    Rectangle cardRect = new Rectangle(pad, curY, cardW, cardH);

                    Color cardBg = isSel 
                        ? Color.FromArgb(28, 38, 48) 
                        : (isHov ? Color.FromArgb(34, 40, 54) : Color.FromArgb(25, 29, 40));
                    Color cardBorder = isSel 
                        ? dev.BrandColor 
                        : (isHov ? Color.FromArgb(70, 80, 105) : Color.FromArgb(40, 46, 62));

                    using (var cPath = CreateRoundedPath(cardRect, (int)(8 * DpiScale)))
                    {
                        using (var cb = new SolidBrush(cardBg)) g.FillPath(cb, cPath);
                        using (var cp = new Pen(cardBorder, isSel ? 1.5f : 1f)) g.DrawPath(cp, cPath);
                    }

                    // Left: Icon Circle Badge
                    int iconSize = (int)(34 * DpiScale);
                    int iconX = pad + (int)(10 * DpiScale);
                    int iconY = curY + (cardH - iconSize) / 2;
                    Rectangle iconRect = new Rectangle(iconX, iconY, iconSize, iconSize);

                    using (var iconPath = CreateRoundedPath(iconRect, iconSize / 2))
                    {
                        using (var ib = new SolidBrush(Color.FromArgb(32, dev.BrandColor.R, dev.BrandColor.G, dev.BrandColor.B))) g.FillPath(ib, iconPath);
                        using (var ip = new Pen(Color.FromArgb(90, dev.BrandColor.R, dev.BrandColor.G, dev.BrandColor.B), 1f)) g.DrawPath(ip, iconPath);
                    }

                    // Vector Icon: Perfectly centered inside iconRect!
                    ModernThemeHelper.DrawModernDeviceIcon(g, iconRect, dev.Category, dev.BrandColor);

                    // Middle: Device Name & Subtitle
                    int textX = iconX + iconSize + (int)(10 * DpiScale);
                    using (var nameFont = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold))
                    using (var nameBrush = new SolidBrush(Color.FromArgb(245, 248, 255)))
                    {
                        g.DrawString(dev.DisplayName, nameFont, nameBrush, textX, curY + (int)(10 * DpiScale));
                    }

                    // Subtitle
                    string subText = dev.IsConnected ? string.Format("● {0} 在线", dev.Transport) : (dev.BatteryPercent > 0 ? string.Format("● 离线 (记忆 {0}%)", dev.BatteryPercent) : "● 离线");
                    Color subCol = dev.IsConnected ? Color.FromArgb(0, 230, 118) : Color.FromArgb(140, 145, 160);
                    if (dev.IsPrimary) subText += "  [托盘常驻]";

                    using (var subFont = new Font("Microsoft YaHei UI", 8F))
                    using (var subBrush = new SolidBrush(subCol))
                    {
                        g.DrawString(subText, subFont, subBrush, textX, curY + (int)(32 * DpiScale));
                    }

                    // Right: Battery & Selected Check
                    int cardRight = pad + cardW;
                    int checkSlotW = (int)(26 * DpiScale);

                    if (isSel)
                    {
                        using (var checkFont = new Font("Segoe UI", 11.5F, FontStyle.Bold))
                        using (var checkBrush = new SolidBrush(dev.BrandColor))
                        {
                            var chkSz = TextRenderer.MeasureText("✓", checkFont);
                            g.DrawString("✓", checkFont, checkBrush, cardRight - (int)(17 * DpiScale) - (chkSz.Width / 2), curY + (cardH - chkSz.Height) / 2);
                        }
                    }

                    int rightAnchor = cardRight - checkSlotW - (int)(4 * DpiScale);

                    bool isWired = dev.IsConnected && dev.Category == DeviceCategory.Keyboard && dev.Transport != null && 
                                   (dev.Transport.IndexOf("有线", StringComparison.OrdinalIgnoreCase) >= 0 || dev.Transport.IndexOf("USB", StringComparison.OrdinalIgnoreCase) >= 0);
                    bool isCharging = dev.IsConnected && (dev.IsCharging || isWired || (dev.Brand == "NuPhy" && dev.Category == DeviceCategory.Keyboard && dev.BatteryPercent >= 100));

                    if (isCharging)
                    {
                        string chgText = "充电中";
                        using (var battFont = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold))
                        using (var battBrush = new SolidBrush(dev.BrandColor))
                        {
                            var bSz = TextRenderer.MeasureText(chgText, battFont);
                            float iconH = 12.5f * DpiScale;
                            float iconW = iconH * 0.55f;
                            float spacing = 5f * DpiScale;
                            float totalW = iconW + spacing + bSz.Width;

                            float startX = rightAnchor - totalW;
                            float iconCx = startX + iconW / 2f;
                            float iconCy = curY + cardH / 2f;

                            ModernThemeHelper.DrawVectorLightning(g, iconCx, iconCy, iconH, dev.BrandColor);
                            g.DrawString(chgText, battFont, battBrush, startX + iconW + spacing, curY + (cardH - bSz.Height) / 2);
                        }
                    }
                    else
                    {
                        string battStr = (dev.IsConnected && dev.BatteryPercent > 0)
                            ? (dev.BatteryPercent + "%")
                            : (dev.IsConnected && dev.Category == DeviceCategory.Keyboard ? "免驱" : "--");

                        using (var battFont = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold))
                        using (var battBrush = new SolidBrush(dev.IsConnected ? dev.BrandColor : Color.FromArgb(140, 145, 160)))
                        {
                            var bSz = TextRenderer.MeasureText(battStr, battFont);
                            g.DrawString(battStr, battFont, battBrush, rightAnchor - bSz.Width, curY + (cardH - bSz.Height) / 2);
                        }
                    }

                    curY += cardH + cardGap;
                }
            }
            else
            {
                Rectangle cardRect = new Rectangle(pad, curY, cardW, cardH);
                using (var cPath = CreateRoundedPath(cardRect, (int)(8 * DpiScale)))
                {
                    using (var cb = new SolidBrush(Color.FromArgb(25, 29, 40))) g.FillPath(cb, cPath);
                    using (var cp = new Pen(Color.FromArgb(40, 46, 62), 1f)) g.DrawPath(cp, cPath);
                }
                using (var tipFont = new Font("Microsoft YaHei UI", 9F))
                using (var tipBrush = new SolidBrush(Color.FromArgb(160, 170, 190)))
                {
                    string tip = "暂无在线外设，请通过顶部添加按钮配置设备";
                    var sz = TextRenderer.MeasureText(tip, tipFont);
                    g.DrawString(tip, tipFont, tipBrush, pad + (cardW - sz.Width) / 2, curY + (cardH - sz.Height) / 2);
                }
                curY += cardH + cardGap;
            }
        }
    }
}