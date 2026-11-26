// ============================================================================
// 模块名称: DpiAwareForm.cs
// 所属区域: UI/Forms (统一窗体基类)
// 核心职责:
//   全软件统一的高 DPI 与无极缩放自适应感知窗体基类：
//   1. 强制关闭 WinForms 默认非线性字体拉伸 (AutoScaleMode = AutoScaleMode.None)
//   2. 初始化时支持同步主窗口无极缩放比例 (0.60x~2.50x) 或自动探测物理屏幕高 DPI 缩放比
//   3. 原生拦截 Windows 跨屏拖拽 WM_DPICHANGED 动态响应消息并更新布局与重绘
//   4. 统一内置 Windows 11 暗黑 Mica / Acrylic 原生圆角与防眩光主题
//   5. 为所有子类提供统一的 OnApplyDpiScale 生命周期钩子（含 OnLoad 二次兜底装配），
//      确保子类构造函数中添加的所有控件 100% 完成跨系统 DPI 矢量字体缩放
//   6. 提供统一的现代无边框一体化大标题引擎 (AttachModernChrome) 与原生 DWM 开关动画支持
//      (CreateParams WS_CAPTION|WS_SYSMENU + WM_NCCALCSIZE 零非客户区 + WM_ERASEBKGND 防白闪)，
//      跨全软件所有二级弹窗默认启用 52px 一体化大标题 + 暗色胶囊关闭按钮、拖拽移动、1px 暗黑边框与原生动画
// 依赖关系: Helpers.DpiScaleHelper, Helpers.ModernThemeHelper, Core.NativeMethods, Controls.ModernButton
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改窗体基类逻辑时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class DpiAwareForm : Form
    {
        protected float dpiScale = 1.0f;
        private bool isHandleCreatedDpiProbed = false;
        private bool hasExplicitInitialScale = false;

        protected Panel modernChromeTitleBar;
        protected Label modernChromeTitleLabel;
        protected ModernButton modernChromeCloseButton;
        protected bool modernChromeEnabled = false;
        protected bool modernChromeDrawBorder = true;
        protected bool modernChromeUseIntegratedHeroHeader = true;
        protected int modernChromeHeaderBaseHeight = 38;
        protected int modernChromeTitlePadLeftBase = 16;
        private bool modernChromeEscWired = false;
        private bool isCreatingHandle = false;

        private const int WS_CAPTION = 0x00C00000;
        private const int WS_SYSMENU = 0x00080000;
        private const int WS_THICKFRAME = 0x00040000;
        private const int WS_MINIMIZEBOX = 0x00020000;
        private const int WS_MAXIMIZEBOX = 0x00010000;
        private const int WS_POPUP = unchecked((int)0x80000000);
        private const int WM_ERASEBKGND = 0x0014;
        private const int WM_NCCALCSIZE = 0x0083;
        private const uint SWP_NOSIZE = 0x0001;
        private const uint SWP_NOMOVE = 0x0002;
        private const uint SWP_NOZORDER = 0x0004;
        private const uint SWP_NOACTIVATE = 0x0010;
        private const uint SWP_FRAMECHANGED = 0x0020;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter, int X, int Y, int cx, int cy, uint uFlags);

        protected virtual bool UseNativeDwmFramelessAnimation
        {
            get { return modernChromeEnabled; }
        }

        public float DpiScale
        {
            get { return dpiScale; }
            protected set
            {
                if (value < DpiScaleHelper.MinScale) value = DpiScaleHelper.MinScale;
                dpiScale = value;
            }
        }

        public DpiAwareForm() : this(0f)
        {
        }

        public DpiAwareForm(float initialScale)
        {
            this.AutoScaleMode = AutoScaleMode.None;
            if (initialScale >= DpiScaleHelper.MinScale)
            {
                this.dpiScale = initialScale;
                this.hasExplicitInitialScale = true;
            }
        }

        protected virtual bool CanCloseViaModernChrome()
        {
            return true;
        }

        protected void AttachModernChrome(
            string title,
            Control redundantTitleToRemove = null,
            int shiftExistingDeltaBase = 0,
            bool growClientHeight = true,
            bool enableEscClose = true)
        {
            bool alreadyAttached = modernChromeEnabled;
            string titleText = title ?? string.Empty;
            this.Text = titleText;
            // 先开启标志位，再切换无边框，保证已创建句柄时 CreateParams 立即挂载 WS_CAPTION | WS_SYSMENU
            modernChromeEnabled = true;
            this.FormBorderStyle = FormBorderStyle.None;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.DoubleBuffered = true;

            if (redundantTitleToRemove != null)
            {
                try
                {
                    if (this.Controls.Contains(redundantTitleToRemove))
                    {
                        this.Controls.Remove(redundantTitleToRemove);
                    }
                    redundantTitleToRemove.Visible = false;
                    redundantTitleToRemove.Dispose();
                }
                catch { }
            }

            if (!alreadyAttached && shiftExistingDeltaBase != 0)
            {
                int shiftY = Scale(shiftExistingDeltaBase);
                Dictionary<Control, AnchorStyles> savedAnchors = new Dictionary<Control, AnchorStyles>();
                for (int i = 0; i < this.Controls.Count; i++)
                {
                    Control c = this.Controls[i];
                    if (c == modernChromeTitleBar) continue;
                    savedAnchors[c] = c.Anchor;
                    c.Anchor = AnchorStyles.Top | AnchorStyles.Left;
                }

                if (growClientHeight)
                {
                    this.ClientSize = new Size(this.ClientSize.Width, Math.Max(40, this.ClientSize.Height + shiftY));
                }

                for (int i = 0; i < this.Controls.Count; i++)
                {
                    Control c = this.Controls[i];
                    if (c == modernChromeTitleBar) continue;
                    c.Top += shiftY;
                }

                foreach (KeyValuePair<Control, AnchorStyles> kv in savedAnchors)
                {
                    kv.Key.Anchor = kv.Value;
                }
            }

            if (this.IsHandleCreated)
            {
                try
                {
                    this.UpdateStyles();
                    SetWindowPos(this.Handle, IntPtr.Zero, 0, 0, 0, 0, SWP_NOMOVE | SWP_NOSIZE | SWP_NOZORDER | SWP_NOACTIVATE | SWP_FRAMECHANGED);
                    ModernThemeHelper.ApplyModernWin11Theme(this.Handle);
                    ModernThemeHelper.ApplyFramelessLegacyShadowIfNeeded(this.Handle);
                }
                catch { }
            }

            if (modernChromeTitleBar == null)
            {
                modernChromeTitleBar = new Panel();
                modernChromeTitleBar.BackColor = this.BackColor;
                modernChromeTitleBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                modernChromeTitleBar.MouseDown += ModernChromeDrag_MouseDown;

                modernChromeTitleLabel = new Label();
                modernChromeTitleLabel.Text = titleText;
                modernChromeTitleLabel.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold);
                modernChromeTitleLabel.ForeColor = Color.FromArgb(148, 160, 182);
                modernChromeTitleLabel.BackColor = Color.Transparent;
                modernChromeTitleLabel.AutoSize = false;
                modernChromeTitleLabel.AutoEllipsis = true;
                modernChromeTitleLabel.TextAlign = ContentAlignment.MiddleLeft;
                modernChromeTitleLabel.MouseDown += ModernChromeDrag_MouseDown;
                modernChromeTitleBar.Controls.Add(modernChromeTitleLabel);

                modernChromeCloseButton = new ModernButton();
                modernChromeCloseButton.IsCloseButton = true;
                modernChromeCloseButton.Text = "";
                modernChromeCloseButton.NormalColor = Color.Transparent;
                modernChromeCloseButton.HoverColor = Color.FromArgb(215, 45, 58);
                modernChromeCloseButton.PressedColor = Color.FromArgb(175, 28, 42);
                modernChromeCloseButton.ForeColor = Color.FromArgb(156, 166, 186);
                modernChromeCloseButton.Anchor = AnchorStyles.Top | AnchorStyles.Right;
                modernChromeCloseButton.Click += delegate(object s, EventArgs e)
                {
                    RequestModernChromeClose();
                };
                modernChromeTitleBar.Controls.Add(modernChromeCloseButton);

                this.Controls.Add(modernChromeTitleBar);
            }
            else if (modernChromeTitleLabel != null)
            {
                modernChromeTitleLabel.Text = titleText;
            }

            modernChromeEnabled = true;
            LayoutModernChrome();
            modernChromeTitleBar.BringToFront();

            if (enableEscClose && !modernChromeEscWired)
            {
                modernChromeEscWired = true;
                this.KeyPreview = true;
                this.KeyDown += delegate(object s, KeyEventArgs e)
                {
                    if (!e.Handled && e.KeyCode == Keys.Escape)
                    {
                        RequestModernChromeClose();
                    }
                };
            }
        }

        private void RequestModernChromeClose()
        {
            if (!CanCloseViaModernChrome()) return;
            if (this.Modal && this.DialogResult == DialogResult.None)
            {
                this.DialogResult = DialogResult.Cancel;
            }
            this.Close();
        }

        protected void LayoutModernChrome()
        {
            if (!modernChromeEnabled || modernChromeTitleBar == null) return;

            int barW = Math.Max(40, this.ClientSize.Width - 2);
            modernChromeTitleBar.BackColor = this.BackColor;

            if (modernChromeUseIntegratedHeroHeader)
            {
                int heroBarH = Math.Max(36, Scale(52));
                int topInset = Scale(12);
                int rowH = Math.Max(24, heroBarH - topInset - Scale(2));
                int sidePad = Math.Max(8, Scale(modernChromeTitlePadLeftBase) - 1);
                modernChromeTitleBar.Bounds = new Rectangle(1, 1, barW, heroBarH);

                if (modernChromeCloseButton != null)
                {
                    int closeSize = Math.Max(22, Scale(28));
                    modernChromeCloseButton.Size = new Size(closeSize, closeSize);
                    modernChromeCloseButton.Location = new Point(Math.Max(0, barW - sidePad - closeSize), topInset + Math.Max(0, (rowH - closeSize) / 2));
                    modernChromeCloseButton.CornerRadius = Math.Max(5, Scale(7));
                    modernChromeCloseButton.NormalColor = Color.FromArgb(28, 32, 42);
                    modernChromeCloseButton.BorderColor = Color.FromArgb(44, 50, 64);
                    modernChromeCloseButton.HoverColor = Color.FromArgb(215, 45, 58);
                    modernChromeCloseButton.PressedColor = Color.FromArgb(175, 28, 42);
                    modernChromeCloseButton.ForeColor = Color.FromArgb(182, 192, 212);
                }

                if (modernChromeTitleLabel != null)
                {
                    int rightLimit = (modernChromeCloseButton != null ? modernChromeCloseButton.Left : barW) - Scale(10);
                    modernChromeTitleLabel.ForeColor = Color.FromArgb(242, 246, 255);
                    modernChromeTitleLabel.Font = DpiScaleHelper.GetCachedScaledFont("Microsoft YaHei UI", 13.0F, FontStyle.Bold, this.dpiScale);
                    modernChromeTitleLabel.Bounds = new Rectangle(sidePad, topInset, Math.Max(40, rightLimit - sidePad), rowH);
                }
                return;
            }

            int barH = Math.Max(26, Scale(modernChromeHeaderBaseHeight));
            modernChromeTitleBar.Bounds = new Rectangle(1, 1, barW, barH);

            if (modernChromeCloseButton != null)
            {
                int closeSize = Math.Max(22, Scale(28));
                modernChromeCloseButton.Size = new Size(closeSize, closeSize);
                modernChromeCloseButton.Location = new Point(Math.Max(0, barW - Scale(8) - closeSize), Math.Max(0, (barH - closeSize) / 2));
                modernChromeCloseButton.CornerRadius = Math.Max(4, Scale(6));
            }

            if (modernChromeTitleLabel != null)
            {
                int padLeft = Scale(modernChromeTitlePadLeftBase);
                int rightLimit = (modernChromeCloseButton != null ? modernChromeCloseButton.Left : barW) - Scale(8);
                modernChromeTitleLabel.Bounds = new Rectangle(padLeft, 0, Math.Max(40, rightLimit - padLeft), barH);
            }
        }

        private void ModernChromeDrag_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                try
                {
                    NativeMethods.ReleaseCapture();
                    NativeMethods.SendMessage(this.Handle, NativeMethods.WM_NCLBUTTONDOWN, (IntPtr)NativeMethods.HT_CAPTION, IntPtr.Zero);
                }
                catch { }
            }
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (modernChromeEnabled)
            {
                LayoutModernChrome();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (modernChromeEnabled && modernChromeDrawBorder)
            {
                using (Pen borderPen = new Pen(Color.FromArgb(42, 46, 58), 1))
                {
                    e.Graphics.DrawRectangle(borderPen, 0, 0, this.Width - 1, this.Height - 1);
                }
            }
        }

        protected override void CreateHandle()
        {
            isCreatingHandle = true;
            try
            {
                base.CreateHandle();
            }
            finally
            {
                isCreatingHandle = false;
            }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                if (UseNativeDwmFramelessAnimation &&
                    this.FormBorderStyle == FormBorderStyle.None &&
                    (isCreatingHandle || this.IsHandleCreated))
                {
                    // 在句柄创建及后续样式刷新阶段挂载 WS_CAPTION | WS_SYSMENU（剥离 WS_POPUP/WS_THICKFRAME），
                    // 同时保持句柄创建前 WinForms ClientSize == Size 零偏移，从而在无边框外观下保留 Windows 原生 DWM 弹出与关闭动画。
                    cp.Style &= ~WS_POPUP;
                    cp.Style &= ~WS_THICKFRAME;
                    if (!this.MaximizeBox) cp.Style &= ~WS_MAXIMIZEBOX;
                    if (!this.MinimizeBox) cp.Style &= ~WS_MINIMIZEBOX;
                    cp.Style |= WS_CAPTION | WS_SYSMENU;
                }
                return cp;
            }
        }

        protected override void SetClientSizeCore(int x, int y)
        {
            if (UseNativeDwmFramelessAnimation && this.FormBorderStyle == FormBorderStyle.None)
            {
                this.SetBoundsCore(this.Left, this.Top, x, y, BoundsSpecified.Size);
                return;
            }
            base.SetClientSizeCore(x, y);
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (UseNativeDwmFramelessAnimation && this.FormBorderStyle == FormBorderStyle.None)
            {
                using (SolidBrush b = new SolidBrush(this.BackColor))
                {
                    e.Graphics.FillRectangle(b, e.ClipRectangle);
                }
                return;
            }
            base.OnPaintBackground(e);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ModernThemeHelper.ApplyModernWin11Theme(this.Handle);
            if (UseNativeDwmFramelessAnimation && this.FormBorderStyle == FormBorderStyle.None)
            {
                ModernThemeHelper.ApplyFramelessLegacyShadowIfNeeded(this.Handle);
            }

            if (!isHandleCreatedDpiProbed)
            {
                if (!hasExplicitInitialScale && this.dpiScale <= 1.0f)
                {
                    float probedScale = DpiScaleHelper.GetDpiScaleForWindow(this.Handle);
                    if (probedScale >= DpiScaleHelper.MinScale)
                    {
                        this.dpiScale = probedScale;
                    }
                }
                Screen scrn = Screen.FromHandle(this.Handle);
                if (scrn != null)
                {
                    this.dpiScale = DpiScaleHelper.ClampScaleToScreen(this.dpiScale, scrn, 700);
                }
                isHandleCreatedDpiProbed = true;
                OnApplyDpiScale(this.dpiScale);
            }
        }

        protected override void OnLoad(EventArgs e)
        {
            base.OnLoad(e);
            // 二次兜底：若子类构造函数在 Controls.Add 之前触发了 Handle 创建（如设置 MinimumSize），
            // 此处在所有子控件完整挂载后再执行一次幂等字体与 DPI 缩放同步。
            OnApplyDpiScale(this.dpiScale);
        }

        protected override void WndProc(ref Message m)
        {
            if (UseNativeDwmFramelessAnimation && this.FormBorderStyle == FormBorderStyle.None)
            {
                if (m.Msg == WM_NCCALCSIZE)
                {
                    // 将整个窗口矩形视为客户区（0px 系统非客户区标题栏与边框），由自绘暗黑标题栏渲染
                    m.Result = IntPtr.Zero;
                    return;
                }
                if (m.Msg == WM_ERASEBKGND)
                {
                    // 彻底拦截系统默认浅色背景擦除，消除 DWM 弹出动画首帧白闪
                    m.Result = (IntPtr)1;
                    return;
                }
            }

            if (m.Msg == NativeMethods.WM_DPICHANGED)
            {
                int newDpiX = m.WParam.ToInt32() & 0xFFFF;
                float newScale = newDpiX / 96.0f;
                if (newScale < DpiScaleHelper.MinScale) newScale = DpiScaleHelper.MinScale;

                NativeMethods.RECT suggestedRect = (NativeMethods.RECT)Marshal.PtrToStructure(m.LParam, typeof(NativeMethods.RECT));
                Rectangle suggestedBounds = Rectangle.FromLTRB(suggestedRect.Left, suggestedRect.Top, suggestedRect.Right, suggestedRect.Bottom);

                Screen currentScreen = Screen.FromRectangle(!suggestedBounds.IsEmpty ? suggestedBounds : this.Bounds);
                newScale = DpiScaleHelper.ClampScaleToScreen(newScale, currentScreen, 700);

                HandleDpiChangedInternal(newScale, suggestedBounds);
                m.Result = IntPtr.Zero;
                return;
            }

            base.WndProc(ref m);

            if (modernChromeEnabled && modernChromeTitleBar != null && m.Msg == 0x84 && (int)m.Result == 1)
            {
                Point pt = this.PointToClient(new Point(m.LParam.ToInt32()));
                if (pt.Y >= 0 && pt.Y <= modernChromeTitleBar.Bottom)
                {
                    if (modernChromeCloseButton != null)
                    {
                        Rectangle closeRect = modernChromeCloseButton.Bounds;
                        closeRect.Offset(modernChromeTitleBar.Location);
                        if (closeRect.Contains(pt)) return;
                    }
                    m.Result = (IntPtr)2; // HTCAPTION
                }
            }
        }

        private void HandleDpiChangedInternal(float newScale, Rectangle suggestedBounds)
        {
            float oldScale = this.dpiScale;
            this.dpiScale = newScale;

            if (!suggestedBounds.IsEmpty)
            {
                this.SetBounds(suggestedBounds.Left, suggestedBounds.Top, suggestedBounds.Width, suggestedBounds.Height, BoundsSpecified.All);
            }

            OnDpiScaleChanged(oldScale, newScale);
            OnApplyDpiScale(newScale);
            this.Invalidate(true);
        }

        /// <summary>
        /// 当屏幕 DPI 发生变更或首次载入时触发，子类重写以更新自定义布局与绘图。
        /// </summary>
        protected virtual void OnApplyDpiScale(float scale)
        {
            DpiScaleHelper.ApplyScaledFontsToControlTree(this, scale);
            if (modernChromeEnabled)
            {
                LayoutModernChrome();
            }
        }

        /// <summary>
        /// 当跨显示器拖拽触发 WM_DPICHANGED 缩放比变更时触发。
        /// </summary>
        protected virtual void OnDpiScaleChanged(float oldScale, float newScale)
        {
        }

        protected int Scale(int value)
        {
            return DpiScaleHelper.Scale(value, this.dpiScale);
        }

        protected Size Scale(Size size)
        {
            return DpiScaleHelper.Scale(size, this.dpiScale);
        }

        protected Point Scale(Point pt)
        {
            return DpiScaleHelper.Scale(pt, this.dpiScale);
        }

        protected Rectangle Scale(Rectangle rect)
        {
            return DpiScaleHelper.Scale(rect, this.dpiScale);
        }
    }
}
