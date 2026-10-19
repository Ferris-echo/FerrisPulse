// ============================================================================
// 模块名称: DpiOsdForm.cs
// 所属区域: UI/Forms (业务对话框层)
// 核心职责:
//   现代化屏幕悬浮 OSD 指示器，具备完全不抢占游戏前台焦点（WS_EX_NOACTIVATE | WS_EX_TOPMOST）、DPI/XY轴独立灵敏度与回报率变动实时屏幕居中或角落弹窗提示、支持雷蛇/雷柏/瓦尔基里品牌适配、支持居中胶囊、能量阶梯、微型指示段三套风格。
// 依赖关系: Theme.ModernThemeHelper, Controls.RoundedCard
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
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class DpiOsdForm : DpiAwareForm
    {
        [DllImport("user32.dll")]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        private static readonly IntPtr HWND_TOPMOST = new IntPtr(-1);
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_SHOWWINDOW = 0x0040;
        private const uint SWP_HIDEWINDOW = 0x0080;
        private const uint SWP_NOOWNERZORDER = 0x0200;
        private const uint SWP_NOSENDCHANGING = 0x0400;

        private int currentDpi = 3000;
        private int currentDpiY = 0;
        private int currentStage = 4;
        private int totalStages = 5;
        private int osdStyle = 0; // 0 = Centered Capsule, 1 = Stepped Gauge, 2 = Compact Top-Right
        private bool isRateMode = false;
        private int currentRate = 1000;
        private string currentRateTitle = "电竞回报率";
        private System.Windows.Forms.Timer displayTimer;
        private System.Windows.Forms.Timer fadeTimer;
        private string currentBrand = "Razer";

        public int OsdStyle
        {
            get { return osdStyle; }
            set { osdStyle = value; }
        }

        public void UpdateDpiScale(float scale)
        {
            if (scale < 1.0f) scale = 1.0f;
            this.dpiScale = scale;
            this.Size = new Size((int)(160 * dpiScale), (int)(58 * dpiScale));
            UpdateOsdRegion(this.Width, this.Height);
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE: never steal focus from full-screen games
                cp.ExStyle |= 0x00000080; // WS_EX_TOOLWINDOW: hide from Alt+Tab
                cp.ExStyle |= 0x00000008; // WS_EX_TOPMOST: render above foreground windows
                cp.ExStyle |= 0x00000020; // WS_EX_TRANSPARENT: mouse clicks pass directly through to games
                return cp;
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_MOUSEACTIVATE = 0x0021;
            const int MA_NOACTIVATE = 3;
            if (m.Msg == WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)MA_NOACTIVATE;
                return;
            }
            base.WndProc(ref m);
        }

        private void UpdateOsdRegion(int w, int h)
        {
            try
            {
                using (GraphicsPath path = RoundedCard.GetRoundRectF(new RectangleF(0, 0, w, h), 8f * dpiScale))
                {
                    Region oldRgn = this.Region;
                    this.Region = new Region(path);
                    if (oldRgn != null)
                    {
                        oldRgn.Dispose();
                    }
                }
            }
            catch { }
        }

        protected override void OnDpiScaleChanged(float oldScale, float newScale)
        {
            base.OnDpiScaleChanged(oldScale, newScale);
            UpdateDpiScale(newScale);
        }

        public DpiOsdForm() : this(0f)
        {
        }

        public DpiOsdForm(float scale) : base(scale)
        {
            if (scale > 0f) this.dpiScale = scale;
            this.FormBorderStyle = FormBorderStyle.None;
            this.ShowInTaskbar = false;
            // Note: Do NOT set this.TopMost = true; WinForms TopMost setter calls SetWindowPos without SWP_NOACTIVATE!
            this.StartPosition = FormStartPosition.Manual;
            this.BackColor = Color.FromArgb(17, 19, 25);
            this.DoubleBuffered = true;
            this.Size = new Size((int)(160 * dpiScale), (int)(58 * dpiScale));
            UpdateOsdRegion(this.Width, this.Height);

            // Pre-create native HWND so it never incurs creation latency during gaming
            IntPtr forceHandle = this.Handle;

            displayTimer = new System.Windows.Forms.Timer();
            displayTimer.Interval = 2000;
            displayTimer.Tick += (s, e) =>
            {
                displayTimer.Stop();
                fadeTimer.Start();
            };

            fadeTimer = new System.Windows.Forms.Timer();
            fadeTimer.Interval = 20;
            fadeTimer.Tick += (s, e) =>
            {
                if (this.Opacity > 0.08)
                {
                    this.Opacity -= 0.12;
                }
                else
                {
                    fadeTimer.Stop();
                    HideOsd();
                }
            };
        }

        public void HideOsd()
        {
            try
            {
                if (this.IsHandleCreated)
                {
                    SetWindowPos(this.Handle, IntPtr.Zero, 0, 0, 0, 0,
                        SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_HIDEWINDOW | SWP_NOSENDCHANGING);
                }
            }
            catch { }
        }

        public void ShowDpi(int dpi, int stage, int count, string brand = "Razer")
        {
            ShowDpi(dpi, dpi, stage, count, brand);
        }

        public void ShowDpi(int dpiX, int dpiY, int stage, int count, string brand = "Razer")
        {
            this.isRateMode = false;
            this.currentDpi = dpiX;
            this.currentDpiY = (dpiY > 0) ? dpiY : dpiX;
            this.currentStage = stage;
            this.totalStages = count > 0 ? count : 5;
            this.currentBrand = !string.IsNullOrEmpty(brand) ? brand : "Razer";

            displayTimer.Stop();
            fadeTimer.Stop();
            this.Opacity = 1.0;

            bool isRapoo = (this.currentBrand == "Rapoo");
            bool isXyDiff = (this.currentDpiY > 0 && this.currentDpiY != this.currentDpi);

            int targetW;
            int targetH;
            if (osdStyle == 0)
            {
                targetW = isXyDiff ? 200 : 160;
                targetH = isRapoo ? 50 : 58;
            }
            else if (osdStyle == 1)
            {
                int maxD = Math.Max(dpiX, this.currentDpiY);
                int baseW = isRapoo ? ((maxD >= 10000) ? 152 : 140) : ((maxD >= 10000) ? 192 : 184);
                targetW = isXyDiff ? (baseW + 36) : baseW;
                targetH = isRapoo ? 52 : 62;
            }
            else
            {
                int maxD = Math.Max(dpiX, this.currentDpiY);
                int baseW = isRapoo ? ((maxD >= 10000) ? 152 : 140) : ((maxD >= 10000) ? 178 : 170);
                targetW = isXyDiff ? (baseW + 36) : baseW;
                targetH = isRapoo ? 52 : 62;
            }

            int scaledW = (int)(targetW * dpiScale);
            int scaledH = (int)(targetH * dpiScale);

            this.Size = new Size(scaledW, scaledH);
            UpdateOsdRegion(scaledW, scaledH);

            Screen targetScreen = Screen.FromPoint(Cursor.Position);
            if (targetScreen == null) targetScreen = Screen.PrimaryScreen;
            Rectangle wa = targetScreen.WorkingArea;
            int margin = (int)(24 * dpiScale);

            int targetX = wa.Right - scaledW - margin;
            int targetY = wa.Bottom - scaledH - margin;

            // Pure Win32 display without activation or Z-order change that could minimize full-screen games
            SetWindowPos(this.Handle, HWND_TOPMOST, targetX, targetY, scaledW, scaledH,
                SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_NOSENDCHANGING);

            this.Invalidate();
            displayTimer.Start();
        }

        public void ShowRate(int hz, string modeTitle, string brand = "Razer")
        {
            this.isRateMode = true;
            this.currentRate = hz;
            this.currentRateTitle = !string.IsNullOrEmpty(modeTitle) ? modeTitle : "电竞回报率";
            this.currentBrand = !string.IsNullOrEmpty(brand) ? brand : "Razer";

            displayTimer.Stop();
            fadeTimer.Stop();
            this.Opacity = 1.0;

            int targetW = 186;
            int targetH = 70;
            int scaledW = (int)(targetW * dpiScale);
            int scaledH = (int)(targetH * dpiScale);

            this.Size = new Size(scaledW, scaledH);
            UpdateOsdRegion(scaledW, scaledH);

            Screen targetScreen = Screen.FromPoint(Cursor.Position);
            if (targetScreen == null) targetScreen = Screen.PrimaryScreen;
            Rectangle wa = targetScreen.WorkingArea;
            int margin = (int)(24 * dpiScale);

            int targetX = wa.Right - scaledW - margin;
            int targetY = wa.Bottom - scaledH - margin;

            // Pure Win32 display without activation or Z-order change that could minimize full-screen games
            SetWindowPos(this.Handle, HWND_TOPMOST, targetX, targetY, scaledW, scaledH,
                SWP_NOACTIVATE | SWP_SHOWWINDOW | SWP_NOSENDCHANGING);

            this.Invalidate();
            displayTimer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            // 1. Common Card Background & Subtle Dark Edge
            RectangleF rect = new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f);
            using (GraphicsPath path = RoundedCard.GetRoundRectF(rect, 8f * dpiScale))
            {
                using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(17, 19, 25)))
                {
                    g.FillPath(bgBrush, path);
                }
                using (Pen borderPen = new Pen(Color.FromArgb(42, 47, 60), 1.0f))
                {
                    g.DrawPath(borderPen, path);
                }
            }

            using (FontFamily ffYaHei = new FontFamily("Microsoft YaHei UI"))
            using (FontFamily ffSegoe = new FontFamily("Segoe UI"))
            {
                StringFormat sf = StringFormat.GenericTypographic;
                bool isRapoo = (currentBrand == "Rapoo");
                bool isValkyrie = (currentBrand == "Valkyrie");
                Color brandAccent = isRapoo ? Color.FromArgb(0, 210, 255) : (isValkyrie ? Color.FromArgb(255, 64, 129) : Color.FromArgb(0, 230, 118));
                string titleText = isRapoo ? "雷柏 DPI" : (isValkyrie ? "瓦尔基里 DPI" : "鼠标 DPI");

                if (isRateMode)
                {
                    bool isGaming = currentRateTitle.Contains("电竞");
                    Color rateAccent = isGaming ? Color.FromArgb(255, 59, 71) : Color.FromArgb(0, 230, 118);


                    float topY = 7.5f * dpiScale;
                    float titleSize = 9.2f * dpiScale;
                    using (GraphicsPath titlePath = new GraphicsPath())
                    {
                        titlePath.AddString(currentRateTitle, ffYaHei, (int)FontStyle.Bold, titleSize, PointF.Empty, sf);
                        RectangleF titleBounds = titlePath.GetBounds();



                        float textStartX = (Width - titleBounds.Width) / 2f;






                        using (Matrix mTitle = new Matrix())
                        {
                            mTitle.Translate(textStartX - titleBounds.Left, topY - titleBounds.Top);
                            titlePath.Transform(mTitle);
                        }
                        using (SolidBrush titleBrush = new SolidBrush(Color.FromArgb(195, 205, 225)))
                            g.FillPath(titleBrush, titlePath);
                    }

                    float numFontSize = 20.5f * dpiScale;
                    float unitFontSize = 10f * dpiScale;
                    using (GraphicsPath numPath = new GraphicsPath())
                    using (GraphicsPath unitPath = new GraphicsPath())
                    {
                        string numStr = currentRate.ToString();
                        numPath.AddString(numStr, ffSegoe, (int)FontStyle.Bold, numFontSize, PointF.Empty, sf);
                        RectangleF numBounds = numPath.GetBounds();

                        unitPath.AddString("Hz", ffSegoe, (int)FontStyle.Bold, unitFontSize, PointF.Empty, sf);
                        RectangleF unitBounds = unitPath.GetBounds();

                        float valGap = 4f * dpiScale;

                        float valStartX = (Width - numBounds.Width) / 2f;

                        float targetNumTop = 23.5f * dpiScale;
                        using (Matrix mNum = new Matrix())
                        {
                            mNum.Translate(valStartX - numBounds.Left, targetNumTop - numBounds.Top);
                            numPath.Transform(mNum);
                        }
                        using (SolidBrush whiteBrush = new SolidBrush(Color.FromArgb(248, 250, 255)))
                            g.FillPath(whiteBrush, numPath);

                        float targetUnitTop = targetNumTop + numBounds.Height - unitBounds.Height;
                        using (Matrix mUnit = new Matrix())
                        {
                            mUnit.Translate(valStartX + numBounds.Width + valGap - unitBounds.Left, targetUnitTop - unitBounds.Top);
                            unitPath.Transform(mUnit);
                        }
                        using (SolidBrush accentBrush = new SolidBrush(rateAccent))
                            g.FillPath(accentBrush, unitPath);
                    }
                    // 3. Bottom Tactical Subtitle Badge ("HYPER-POLLING SPEED" / "ENERGY SAVING MODE")
                    string subText = isGaming ? "HYPER-POLLING SPEED" : "ENERGY SAVING MODE";
                    float subFontSize = 7.2f * dpiScale;
                    using (GraphicsPath subPath = new GraphicsPath())
                    {
                        subPath.AddString(subText, ffSegoe, (int)FontStyle.Bold, subFontSize, PointF.Empty, sf);
                        RectangleF subBounds = subPath.GetBounds();

                        float pillPadX = 7f * dpiScale;
                        float pillPadY = 2.2f * dpiScale;
                        float bDotD = 3.5f * dpiScale;
                        float bDotGap = 4.5f * dpiScale;
                        float pillW = bDotD + bDotGap + subBounds.Width + pillPadX * 2f;
                        float pillH = subBounds.Height + pillPadY * 2f;
                        float pillX = (Width - pillW) / 2f;
                        float pillY = 48.5f * dpiScale;

                        RectangleF pillRect = new RectangleF(pillX, pillY, pillW, pillH);
                        using (GraphicsPath pillPath = RoundedCard.GetRoundRectF(pillRect, 3f * dpiScale))
                        {
                            using (SolidBrush pillBgBrush = new SolidBrush(Color.FromArgb(25, 30, 42)))
                            {
                                g.FillPath(pillBgBrush, pillPath);
                            }
                            using (Pen pillPen = new Pen(Color.FromArgb(48, 56, 74), 1f))
                            {
                                g.DrawPath(pillPen, pillPath);
                            }
                        }

                        float dotX = pillX + pillPadX;
                        float dotY = pillY + (pillH - bDotD) / 2f;
                        using (SolidBrush dotBrush = new SolidBrush(rateAccent))
                        {
                            g.FillEllipse(dotBrush, dotX, dotY, bDotD, bDotD);
                        }

                        using (Matrix mSub = new Matrix())
                        {
                            mSub.Translate(dotX + bDotD + bDotGap - subBounds.Left, pillY + pillPadY - subBounds.Top);
                            subPath.Transform(mSub);
                        }
                        using (SolidBrush subBrush = new SolidBrush(Color.FromArgb(170, 185, 205)))
                        {
                            g.FillPath(subBrush, subPath);
                        }
                    }
                    return;
                }

                if (osdStyle == 0)
                {
                    // ================= STYLE 0: 居中电竞胶囊 (Ultra-Compact, Symmetrical HUD) =================
                    // 1. Top Title (Centered Dot + Title)
                    float topY = (isRapoo ? 6.5f : 7f) * dpiScale;
                    float titleSize = (isRapoo ? 9.2f : 10f) * dpiScale;
                    using (GraphicsPath titlePath = new GraphicsPath())
                    {
                        titlePath.AddString(titleText, ffYaHei, (int)FontStyle.Regular, titleSize, PointF.Empty, sf);
                        RectangleF titleBounds = titlePath.GetBounds();

                        float dotD = 4f * dpiScale;
                        float dotGap = 4.5f * dpiScale;
                        float topContentW = dotD + dotGap + titleBounds.Width;
                        float topStartX = (Width - topContentW) / 2f;

                        float dotY = topY + (titleBounds.Height - dotD) / 2f;
                        using (SolidBrush dotBrush = new SolidBrush(brandAccent))
                            g.FillEllipse(dotBrush, topStartX, dotY, dotD, dotD);

                        using (Matrix mTitle = new Matrix())
                        {
                            mTitle.Translate(topStartX + dotD + dotGap - titleBounds.Left, topY - titleBounds.Top);
                            titlePath.Transform(mTitle);
                        }
                        using (SolidBrush titleBrush = new SolidBrush(Color.FromArgb(145, 155, 175)))
                            g.FillPath(titleBrush, titlePath);

                        // 3. Bottom Capsules (Row 3) - Only for Razer (preserve 5 stages)
                        float segY = Height;
                        if (!isRapoo)
                        {
                            int segCount = totalStages > 0 ? totalStages : 5;
                            float segW = 19f * dpiScale;
                            float segH = 3.5f * dpiScale;
                            float segGap = 4f * dpiScale;
                            float totalSegW = segCount * segW + (segCount - 1) * segGap;
                            float segStartX = (Width - totalSegW) / 2f;
                            float bottomMargin = 8f * dpiScale;
                            segY = Height - bottomMargin - segH;

                            for (int i = 1; i <= segCount; i++)
                            {
                                RectangleF segRect = new RectangleF(segStartX + (i - 1) * (segW + segGap), segY, segW, segH);
                                using (GraphicsPath sp = RoundedCard.GetRoundRectF(segRect, 1.75f * dpiScale))
                                {
                                    Color c = (i == currentStage) ? brandAccent : Color.FromArgb(36, 40, 52);
                                    using (SolidBrush b = new SolidBrush(c))
                                        g.FillPath(b, sp);
                                }
                            }
                        }

                        // 2. Middle Value ("3000" or "1600 × 1200" + "DPI") - Perfectly Centered & Exact Baseline Aligned
                        bool isXyDiff = (currentDpiY > 0 && currentDpiY != currentDpi);
                        string numStr = isXyDiff ? string.Format("{0} × {1}", currentDpi, currentDpiY) : currentDpi.ToString();
                        float numFontSize = (isXyDiff ? 15.5f : 20f) * dpiScale;
                        float unitFontSize = (isXyDiff ? 9f : 10f) * dpiScale;

                        using (GraphicsPath numPath = new GraphicsPath())
                        using (GraphicsPath unitPath = new GraphicsPath())
                        {
                            numPath.AddString(numStr, ffSegoe, (int)FontStyle.Bold, numFontSize, PointF.Empty, sf);
                            RectangleF numBounds = numPath.GetBounds();

                            unitPath.AddString("DPI", ffSegoe, (int)FontStyle.Bold, unitFontSize, PointF.Empty, sf);
                            RectangleF unitBounds = unitPath.GetBounds();

                            float valGap = 4f * dpiScale;
                            float totalValW = numBounds.Width + valGap + unitBounds.Width;
                            float valStartX = (Width - totalValW) / 2f;

                            float midZoneTop = topY + titleBounds.Height;
                            float midZoneBottom = isRapoo ? (Height - 5f * dpiScale) : segY;
                            float midZoneH = midZoneBottom - midZoneTop;
                            float targetNumTop = midZoneTop + (midZoneH - numBounds.Height) / 2f;
                            float targetBaseline = targetNumTop + numBounds.Height;

                            using (Matrix mNum = new Matrix())
                            {
                                mNum.Translate(valStartX - numBounds.Left, targetNumTop - numBounds.Top);
                                numPath.Transform(mNum);
                            }
                            using (SolidBrush whiteBrush = new SolidBrush(Color.FromArgb(248, 250, 255)))
                                g.FillPath(whiteBrush, numPath);

                            float targetUnitTop = targetBaseline - unitBounds.Height;
                            using (Matrix mUnit = new Matrix())
                            {
                                mUnit.Translate(valStartX + numBounds.Width + valGap - unitBounds.Left, targetUnitTop - unitBounds.Top);
                                unitPath.Transform(mUnit);
                            }
                            using (SolidBrush accentBrush = new SolidBrush(brandAccent))
                                g.FillPath(accentBrush, unitPath);
                        }
                    }
                }
                else if (osdStyle == 1)
                {
                    // ================= STYLE 1: 右侧阶梯能量计 (Faithful & Optical Left Aligned) =================
                    float padLeft = 14f * dpiScale;
                    float padRight = 14f * dpiScale;

                    float titleFontSize = 9.5f * dpiScale;
                    using (GraphicsPath titlePath = new GraphicsPath())
                    {
                        titlePath.AddString(titleText, ffYaHei, (int)FontStyle.Regular, titleFontSize, PointF.Empty, sf);
                        RectangleF titleBounds = titlePath.GetBounds();

                        bool isXyDiff = (currentDpiY > 0 && currentDpiY != currentDpi);
                        string numStr = isXyDiff ? string.Format("{0} × {1}", currentDpi, currentDpiY) : currentDpi.ToString();
                        int maxD = Math.Max(currentDpi, currentDpiY);
                        float numFontSize = isXyDiff
                            ? ((maxD >= 10000) ? 14.5f : 16.0f) * dpiScale
                            : ((currentDpi >= 10000 ? 19.5f : 21.5f) * dpiScale);
                        float unitFontSize = (isXyDiff ? 9f : 10f) * dpiScale;

                        using (GraphicsPath numPath = new GraphicsPath())
                        using (GraphicsPath unitPath = new GraphicsPath())
                        {
                            numPath.AddString(numStr, ffSegoe, (int)FontStyle.Bold, numFontSize, PointF.Empty, sf);
                            RectangleF numBounds = numPath.GetBounds();

                            unitPath.AddString("DPI", ffSegoe, (int)FontStyle.Bold, unitFontSize, PointF.Empty, sf);
                            RectangleF unitBounds = unitPath.GetBounds();

                            float rowGap = 3.5f * dpiScale;
                            float totalLeftBlockH = titleBounds.Height + rowGap + numBounds.Height;
                            float startY = (Height - totalLeftBlockH) / 2f;

                            // 1. Top Left: Dot + Title
                            float topY = startY;
                            float dotD = 4.5f * dpiScale;
                            float dotGap = 5f * dpiScale;
                            float dotX = padLeft;
                            float dotY = topY + (titleBounds.Height - dotD) / 2f;

                            using (SolidBrush dotBrush = new SolidBrush(brandAccent))
                                g.FillEllipse(dotBrush, dotX, dotY, dotD, dotD);

                            float titleX = dotX + dotD + dotGap;
                            using (Matrix mTitle = new Matrix())
                            {
                                mTitle.Translate(titleX - titleBounds.Left, topY - titleBounds.Top);
                                titlePath.Transform(mTitle);
                            }
                            using (SolidBrush titleBrush = new SolidBrush(Color.FromArgb(145, 155, 175)))
                                g.FillPath(titleBrush, titlePath);

                            // 2. Large Value: Optical left anchor with dotX
                            float numY = topY + titleBounds.Height + rowGap;
                            float baselineY = numY + numBounds.Height;

                            using (Matrix mNum = new Matrix())
                            {
                                mNum.Translate(padLeft - numBounds.Left, numY - numBounds.Top);
                                numPath.Transform(mNum);
                            }
                            using (SolidBrush whiteBrush = new SolidBrush(Color.FromArgb(248, 250, 255)))
                                g.FillPath(whiteBrush, numPath);

                            // Unit: Exact baseline alignment
                            float valGap = 4f * dpiScale;
                            float unitX = padLeft + numBounds.Width + valGap;
                            float unitY = baselineY - unitBounds.Height;

                            using (Matrix mUnit = new Matrix())
                            {
                                mUnit.Translate(unitX - unitBounds.Left, unitY - unitBounds.Top);
                                unitPath.Transform(mUnit);
                            }
                            using (SolidBrush accentBrush = new SolidBrush(brandAccent))
                                g.FillPath(accentBrush, unitPath);

                            // 3. Right Stepped Energy Bars - Only for Razer
                            if (!isRapoo)
                            {
                                int barCount = totalStages > 0 ? totalStages : 5;
                                float barW = 4.5f * dpiScale;
                                float barGap = 4f * dpiScale;
                                float totalBarsW = barCount * barW + (barCount - 1) * barGap;
                                float barStartX = Width - padRight - totalBarsW;
                                float barBaseY = baselineY + (0.5f * dpiScale);

                                float minH = 8f * dpiScale;
                                float maxH = 25f * dpiScale;
                                float stepH = (maxH - minH) / (barCount - 1);

                                for (int i = 1; i <= barCount; i++)
                                {
                                    float barH = minH + (i - 1) * stepH;
                                    float barY = barBaseY - barH;
                                    RectangleF barRect = new RectangleF(barStartX + (i - 1) * (barW + barGap), barY, barW, barH);
                                    using (GraphicsPath bp = RoundedCard.GetRoundRectF(barRect, 1.75f * dpiScale))
                                    {
                                        Color c;
                                        if (i == currentStage)
                                            c = brandAccent; // Active stage
                                        else if (i < currentStage)
                                            c = Color.FromArgb(0, 135, 68);  // Lower stages
                                        else
                                            c = Color.FromArgb(38, 43, 56);  // Higher stages

                                        using (SolidBrush b = new SolidBrush(c))
                                            g.FillPath(b, bp);
                                    }
                                }
                            }
                        }
                    }
                }
                else
                {
                    // ================= STYLE 2: 顶置微型指示段 (Faithful & Balanced Compact) =================
                    float padLeft = 14f * dpiScale;
                    float padRight = 14f * dpiScale;

                    float titleFontSize = 9.5f * dpiScale;
                    using (GraphicsPath titlePath = new GraphicsPath())
                    {
                        titlePath.AddString(titleText, ffYaHei, (int)FontStyle.Regular, titleFontSize, PointF.Empty, sf);
                        RectangleF titleBounds = titlePath.GetBounds();

                        bool isXyDiff = (currentDpiY > 0 && currentDpiY != currentDpi);
                        string numStr = isXyDiff ? string.Format("{0} × {1}", currentDpi, currentDpiY) : currentDpi.ToString();
                        int maxD = Math.Max(currentDpi, currentDpiY);
                        float numFontSize = isXyDiff
                            ? ((maxD >= 10000) ? 14.5f : 16.0f) * dpiScale
                            : ((currentDpi >= 10000 ? 19.5f : 21.5f) * dpiScale);
                        float unitFontSize = (isXyDiff ? 9f : 10f) * dpiScale;

                        using (GraphicsPath numPath = new GraphicsPath())
                        using (GraphicsPath unitPath = new GraphicsPath())
                        {
                            numPath.AddString(numStr, ffSegoe, (int)FontStyle.Bold, numFontSize, PointF.Empty, sf);
                            RectangleF numBounds = numPath.GetBounds();

                            unitPath.AddString("DPI", ffSegoe, (int)FontStyle.Bold, unitFontSize, PointF.Empty, sf);
                            RectangleF unitBounds = unitPath.GetBounds();

                            float rowGap = 3.5f * dpiScale;
                            float totalLeftBlockH = titleBounds.Height + rowGap + numBounds.Height;
                            float startY = (Height - totalLeftBlockH) / 2f;
                            float topY = startY;

                            // 1. Top Left: Accent Bar + Title
                            float barW = 3f * dpiScale;
                            float barGap = 5.5f * dpiScale;
                            float barH = titleBounds.Height - 1f * dpiScale;
                            RectangleF pillRect = new RectangleF(padLeft, topY + 0.5f * dpiScale, barW, barH);
                            using (GraphicsPath vp = RoundedCard.GetRoundRectF(pillRect, 1.25f * dpiScale))
                            using (SolidBrush vb = new SolidBrush(brandAccent))
                                g.FillPath(vb, vp);

                            float titleX = padLeft + barW + barGap;
                            using (Matrix mTitle = new Matrix())
                            {
                                mTitle.Translate(titleX - titleBounds.Left, topY - titleBounds.Top);
                                titlePath.Transform(mTitle);
                            }
                            using (SolidBrush titleBrush = new SolidBrush(Color.FromArgb(145, 155, 175)))
                                g.FillPath(titleBrush, titlePath);

                            // 2. Top Right: Mini Capsules - Only for Razer
                            if (!isRapoo)
                            {
                                int segCount = totalStages > 0 ? totalStages : 5;
                                float segW = 11f * dpiScale;
                                float segH = 4.2f * dpiScale;
                                float segGap = 3.5f * dpiScale;
                                float totalSegW = segCount * segW + (segCount - 1) * segGap;
                                float segStartX = Width - padRight - totalSegW;
                                float segY = topY + (titleBounds.Height - segH) / 2f;

                                for (int i = 1; i <= segCount; i++)
                                {
                                    RectangleF segRect = new RectangleF(segStartX + (i - 1) * (segW + segGap), segY, segW, segH);
                                    using (GraphicsPath sp = RoundedCard.GetRoundRectF(segRect, 1.75f * dpiScale))
                                    {
                                        Color c = (i == currentStage) ? brandAccent : Color.FromArgb(38, 43, 56);
                                        using (SolidBrush b = new SolidBrush(c))
                                            g.FillPath(b, sp);
                                    }
                                }
                            }

                            // 3. Bottom Row: Number + Unit
                            float numY = topY + titleBounds.Height + rowGap;
                            float baselineY = numY + numBounds.Height;

                            using (Matrix mNum = new Matrix())
                            {
                                mNum.Translate(padLeft - numBounds.Left, numY - numBounds.Top);
                                numPath.Transform(mNum);
                            }
                            using (SolidBrush whiteBrush = new SolidBrush(Color.FromArgb(248, 250, 255)))
                                g.FillPath(whiteBrush, numPath);

                            float valGap = 4f * dpiScale;
                            float unitX = padLeft + numBounds.Width + valGap;
                            float unitY = baselineY - unitBounds.Height;

                            using (Matrix mUnit = new Matrix())
                            {
                                mUnit.Translate(unitX - unitBounds.Left, unitY - unitBounds.Top);
                                unitPath.Transform(mUnit);
                            }
                            using (SolidBrush accentBrush = new SolidBrush(brandAccent))
                                g.FillPath(accentBrush, unitPath);
                        }
                    }
                }
            }
        }
    }
}