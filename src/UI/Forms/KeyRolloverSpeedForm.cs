// ============================================================================
// 模块名称: KeyRolloverSpeedForm.cs
// 所属区域: UI/Forms (业务对话框层)
// 核心职责:
//   机械键盘全键无冲 (NKRO) 独立测速与矩阵测试窗口，采用独占抢占式底层键盘钩子（WH_KEYBOARD_LL），屏蔽系统快捷键与 IME 干扰，实时呈现按键并发与延迟。
// 依赖关系: Controls.ModernButton, Controls.RoundedCard
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、属性或绘图逻辑时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（❌ 严禁 $"..." 插值，必须使用 string.Format 或 + 拼接）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Drawing.Text;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class KeyRolloverSpeedForm : DpiAwareForm
    {
        private HashSet<Keys> heldKeys = new HashSet<Keys>();
        private int maxConcurrent = 0;
        private int totalPresses = 0;
        private long lastDownTick = 0;
        private double lastDeltaMs = 0;
        private Label lblTitle;
        private Label lblConcurrent;
        private Label lblLatency;
        private Label lblCounter;
        private Panel keysPanel;
        private ModernButton btnReset;
        private ModernButton btnShieldToggle;
        private ModernButton btnClose;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private LowLevelKeyboardProc hookProc;
        private IntPtr hookId = IntPtr.Zero;
        private bool isShieldEnabled = true;
        private bool isFormActive = true;
        private uint currentProcessId = 0;

        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_KEYUP = 0x0101;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_SYSKEYUP = 0x0105;
        private const int WM_SYSCOMMAND = 0x0112;
        private const int SC_KEYMENU = 0xF100;

        private const int WM_IME_SETCONTEXT = 0x0281;
        private const int WM_IME_NOTIFY = 0x0282;
        private const int WM_IME_STARTCOMPOSITION = 0x010D;
        private const int WM_IME_COMPOSITION = 0x010F;
        private const int WM_IME_ENDCOMPOSITION = 0x010E;

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("imm32.dll")]
        private static extern IntPtr ImmAssociateContext(IntPtr hWnd, IntPtr hIMC);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        public KeyRolloverSpeedForm() : this(0f)
        {
        }

        public KeyRolloverSpeedForm(float scale) : base(scale)
        {
            this.Text = "FerrisPulse · 机械键盘全键无冲 (NKRO) 测速仪 [独占抢占模式]";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.TopMost = true;
            this.BackColor = Color.FromArgb(16, 20, 28);
            this.ForeColor = Color.FromArgb(240, 245, 255);
            this.ClientSize = new Size((int)(520 * scale), (int)(360 * scale));
            this.KeyPreview = true;
            this.ImeMode = ImeMode.Disable;

            try
            {
                currentProcessId = (uint)Process.GetCurrentProcess().Id;
            }
            catch { }

            int pad = (int)(16 * scale);

            lblTitle = new Label();
            lblTitle.Text = "⚡ 机械键盘按键矩阵与全键无冲 (NKRO) 测速仪 [独占抢占]";
            lblTitle.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(0, 229, 255);
            lblTitle.SetBounds(pad, (int)(12 * scale), ClientSize.Width - (pad * 2), (int)(24 * scale));
            this.Controls.Add(lblTitle);

            int cardW = (ClientSize.Width - (pad * 2) - (int)(16 * scale)) / 3;
            int cardY = (int)(42 * scale);
            int cardH = (int)(62 * scale);

            Panel p1 = CreateMetricPanel("当前/最大并发", "0 键", out lblConcurrent, pad, cardY, cardW, cardH, Color.FromArgb(0, 230, 118));
            Panel p2 = CreateMetricPanel("击键间隔延迟", "-- ms", out lblLatency, pad + cardW + (int)(8 * scale), cardY, cardW, cardH, Color.FromArgb(0, 229, 255));
            Panel p3 = CreateMetricPanel("累计击键计数", "0 次", out lblCounter, pad + (cardW + (int)(8 * scale)) * 2, cardY, cardW, cardH, Color.FromArgb(255, 183, 77));
            this.Controls.AddRange(new Control[] { p1, p2, p3 });

            keysPanel = new Panel();
            keysPanel.SetBounds(pad, (int)(114 * scale), ClientSize.Width - (pad * 2), (int)(180 * scale));
            keysPanel.BackColor = Color.FromArgb(22, 26, 36);
            keysPanel.ImeMode = ImeMode.Disable;
            keysPanel.Paint += KeysPanel_Paint;
            this.Controls.Add(keysPanel);

            int btnH = (int)(32 * scale);
            int btnY = ClientSize.Height - btnH - (int)(12 * scale);
            int resetW = (int)(92 * scale);
            int closeW = (int)(92 * scale);
            int shieldW = ClientSize.Width - (pad * 2) - resetW - closeW - (int)(16 * scale);

            btnReset = new ModernButton();
            btnReset.Text = "清空重置";
            btnReset.Font = new Font("Microsoft YaHei UI", 8.5F);
            btnReset.NormalColor = Color.FromArgb(32, 38, 52);
            btnReset.HoverColor = Color.FromArgb(48, 56, 76);
            btnReset.BorderColor = Color.FromArgb(60, 70, 96);
            btnReset.TextColor = Color.FromArgb(190, 200, 220);
            btnReset.CornerRadius = (int)(6 * scale);
            btnReset.SetBounds(pad, btnY, resetW, btnH);
            btnReset.Click += (s, e) => {
                heldKeys.Clear();
                maxConcurrent = 0;
                totalPresses = 0;
                lastDeltaMs = 0;
                UpdateLabels();
                keysPanel.Invalidate();
            };
            this.Controls.Add(btnReset);

            btnShieldToggle = new ModernButton();
            btnShieldToggle.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            btnShieldToggle.CornerRadius = (int)(6 * scale);
            btnShieldToggle.SetBounds(pad + resetW + (int)(8 * scale), btnY, shieldW, btnH);
            btnShieldToggle.Cursor = Cursors.Hand;
            UpdateShieldButtonUI();
            btnShieldToggle.Click += (s, e) => {
                isShieldEnabled = !isShieldEnabled;
                UpdateShieldButtonUI();
            };
            this.Controls.Add(btnShieldToggle);

            btnClose = new ModernButton();
            btnClose.Text = "完成退出";
            btnClose.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            btnClose.NormalColor = Color.FromArgb(0, 180, 90);
            btnClose.HoverColor = Color.FromArgb(0, 210, 105);
            btnClose.BorderColor = Color.FromArgb(0, 230, 118);
            btnClose.TextColor = Color.White;
            btnClose.CornerRadius = (int)(6 * scale);
            btnClose.SetBounds(ClientSize.Width - closeW - pad, btnY, closeW, btnH);
            btnClose.Click += (s, e) => this.Close();
            this.Controls.Add(btnClose);

            InstallHook();
        }

        private void UpdateShieldButtonUI()
        {
            if (btnShieldToggle == null) return;
            if (isShieldEnabled)
            {
                btnShieldToggle.Text = "🛡️ 独占抢夺键盘: 已开启 (阻断系统及一切软件热键)";
                btnShieldToggle.NormalColor = Color.FromArgb(20, 42, 36);
                btnShieldToggle.HoverColor = Color.FromArgb(28, 58, 48);
                btnShieldToggle.BorderColor = Color.FromArgb(0, 230, 118);
                btnShieldToggle.TextColor = Color.FromArgb(0, 230, 118);
            }
            else
            {
                btnShieldToggle.Text = "⚪ 独占抢夺键盘: 已关闭 (允许系统及第三方响应)";
                btnShieldToggle.NormalColor = Color.FromArgb(42, 34, 20);
                btnShieldToggle.HoverColor = Color.FromArgb(58, 46, 28);
                btnShieldToggle.BorderColor = Color.FromArgb(255, 183, 77);
                btnShieldToggle.TextColor = Color.FromArgb(255, 183, 77);
            }
            btnShieldToggle.Invalidate();
        }

        private void DisableIme()
        {
            try
            {
                this.ImeMode = ImeMode.Disable;
                if (this.IsHandleCreated)
                {
                    ImmAssociateContext(this.Handle, IntPtr.Zero);
                }
            }
            catch { }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            DisableIme();
        }

        protected override void OnActivated(EventArgs e)
        {
            base.OnActivated(e);
            isFormActive = true;
            DisableIme();
            ReinstallHook();
        }

        protected override void OnDeactivate(EventArgs e)
        {
            base.OnDeactivate(e);
            isFormActive = false;
        }

        protected override void WndProc(ref Message m)
        {
            // Block system menu loop or modal freeze triggered by Alt or F10
            if (m.Msg == WM_SYSCOMMAND)
            {
                int wp = m.WParam.ToInt32() & 0xFFF0;
                if (wp == SC_KEYMENU)
                {
                    m.Result = IntPtr.Zero;
                    return;
                }
            }
            // Block Chinese/English IME composition popups & message filters
            if (m.Msg == WM_IME_SETCONTEXT || m.Msg == WM_IME_NOTIFY || m.Msg == WM_IME_STARTCOMPOSITION || m.Msg == WM_IME_COMPOSITION || m.Msg == WM_IME_ENDCOMPOSITION)
            {
                m.Result = IntPtr.Zero;
                return;
            }
            base.WndProc(ref m);
        }

        private void InstallHook()
        {
            if (hookId == IntPtr.Zero)
            {
                try
                {
                    hookProc = HookCallback;
                    using (Process curProcess = Process.GetCurrentProcess())
                    using (ProcessModule curModule = curProcess.MainModule)
                    {
                        hookId = SetWindowsHookEx(WH_KEYBOARD_LL, hookProc, GetModuleHandle(curModule.ModuleName), 0);
                    }
                }
                catch { }
            }
        }

        private void UninstallHook()
        {
            if (hookId != IntPtr.Zero)
            {
                try
                {
                    UnhookWindowsHookEx(hookId);
                }
                catch { }
                hookId = IntPtr.Zero;
            }
        }

        private void ReinstallHook()
        {
            UninstallHook();
            InstallHook();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            UninstallHook();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            UninstallHook();
            base.Dispose(disposing);
        }

        private IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int msg = wParam.ToInt32();
                bool isDown = (msg == WM_KEYDOWN || msg == WM_SYSKEYDOWN);
                bool isUp = (msg == WM_KEYUP || msg == WM_SYSKEYUP);

                if (isDown || isUp)
                {
                    bool isTargetActive = isFormActive;
                    if (isTargetActive)
                    {
                        IntPtr fg = GetForegroundWindow();
                        if (fg != IntPtr.Zero && fg != this.Handle && Form.ActiveForm != this)
                        {
                            uint fgPid;
                            GetWindowThreadProcessId(fg, out fgPid);
                            if (fgPid != currentProcessId && fgPid != 0)
                            {
                                isTargetActive = false;
                            }
                        }
                    }

                    if (isTargetActive)
                    {
                        int vkCode = Marshal.ReadInt32(lParam);
                        Keys k = (Keys)vkCode;

                        if (k == Keys.Escape && isDown)
                        {
                            try
                            {
                                this.BeginInvoke(new Action(() => this.Close()));
                            }
                            catch { }
                            return new IntPtr(1); // Swallow ESC
                        }

                        if (isDown)
                        {
                            try
                            {
                                this.BeginInvoke(new Action(() => ProcessKeyDown(k)));
                            }
                            catch { }
                        }
                        else if (isUp)
                        {
                            try
                            {
                                this.BeginInvoke(new Action(() => ProcessKeyUp(k)));
                            }
                            catch { }
                        }

                        if (isShieldEnabled)
                        {
                            // Completely swallow key! Do NOT let it propagate to Windows OS or other apps!
                            return new IntPtr(1);
                        }
                    }
                }
            }
            return CallNextHookEx(hookId, nCode, wParam, lParam);
        }

        private void ProcessKeyDown(Keys k)
        {
            long now = Stopwatch.GetTimestamp();
            if (lastDownTick > 0)
            {
                lastDeltaMs = (double)(now - lastDownTick) * 1000.0 / Stopwatch.Frequency;
            }
            lastDownTick = now;

            if (!heldKeys.Contains(k))
            {
                heldKeys.Add(k);
                totalPresses++;
                if (heldKeys.Count > maxConcurrent) maxConcurrent = heldKeys.Count;
            }

            UpdateLabels();
            keysPanel.Invalidate();
        }

        private void ProcessKeyUp(Keys k)
        {
            heldKeys.Remove(k);
            UpdateLabels();
            keysPanel.Invalidate();
        }

        private Panel CreateMetricPanel(string title, string defVal, out Label valLabel, int x, int y, int w, int h, Color accent)
        {
            Panel p = new Panel();
            p.SetBounds(x, y, w, h);
            p.BackColor = Color.FromArgb(24, 28, 40);

            Label lt = new Label();
            lt.Text = title;
            lt.Font = new Font("Microsoft YaHei UI", 7.8F);
            lt.ForeColor = Color.FromArgb(140, 150, 175);
            lt.SetBounds((int)(6 * dpiScale), (int)(4 * dpiScale), w - (int)(12 * dpiScale), (int)(16 * dpiScale));
            p.Controls.Add(lt);

            valLabel = new Label();
            valLabel.Text = defVal;
            valLabel.Font = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Bold);
            valLabel.ForeColor = accent;
            valLabel.SetBounds((int)(6 * dpiScale), (int)(22 * dpiScale), w - (int)(12 * dpiScale), (int)(32 * dpiScale));
            p.Controls.Add(valLabel);

            return p;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { this.Close(); return; }
            ProcessKeyDown(e.KeyCode);
            e.Handled = true;
            base.OnKeyDown(e);
        }

        protected override void OnKeyUp(KeyEventArgs e)
        {
            ProcessKeyUp(e.KeyCode);
            e.Handled = true;
            base.OnKeyUp(e);
        }

        private void UpdateLabels()
        {
            string nkro = maxConcurrent >= 6 ? " (NKRO)" : "";
            lblConcurrent.Text = string.Format("{0} 键 / 峰值: {1}{2}", heldKeys.Count, maxConcurrent, nkro);
            lblLatency.Text = lastDeltaMs > 0 ? string.Format("{0:0.1} ms", lastDeltaMs) : "-- ms";
            lblCounter.Text = totalPresses + " 次";
        }

        private static string GetFriendlyKeyName(Keys k)
        {
            switch (k)
            {
                case Keys.LWin: return "Win (左)";
                case Keys.RWin: return "Win (右)";
                case Keys.LMenu: return "Alt (左)";
                case Keys.RMenu: return "Alt (右)";
                case Keys.LControlKey: return "Ctrl (左)";
                case Keys.RControlKey: return "Ctrl (右)";
                case Keys.LShiftKey: return "Shift (左)";
                case Keys.RShiftKey: return "Shift (右)";
                case Keys.Return: return "Enter";
                case Keys.Back: return "Backspace";
                case Keys.Capital: return "Caps Lock";
                case Keys.Space: return "Space";
                case Keys.Tab: return "Tab";
                case Keys.Escape: return "Esc";
                case Keys.PageUp: return "Page Up";
                case Keys.PageDown: return "Page Down";
                case Keys.PrintScreen: return "PrtScn";
                case Keys.Scroll: return "Scroll Lock";
                case Keys.Pause: return "Pause";
                case Keys.Apps: return "Menu";
                case Keys.Oemtilde: return "~ `";
                case Keys.OemMinus: return "- _";
                case Keys.Oemplus: return "+ =";
                case Keys.OemOpenBrackets: return "[ {";
                case Keys.OemCloseBrackets: return "] }";
                case Keys.OemPipe: return "\\ |";
                case Keys.OemSemicolon: return "; :";
                case Keys.OemQuotes: return "' \"";
                case Keys.Oemcomma: return ", <";
                case Keys.OemPeriod: return ". >";
                case Keys.OemQuestion: return "/ ?";
                case Keys.D0: return "0";
                case Keys.D1: return "1";
                case Keys.D2: return "2";
                case Keys.D3: return "3";
                case Keys.D4: return "4";
                case Keys.D5: return "5";
                case Keys.D6: return "6";
                case Keys.D7: return "7";
                case Keys.D8: return "8";
                case Keys.D9: return "9";
                case Keys.NumPad0: return "Num 0";
                case Keys.NumPad1: return "Num 1";
                case Keys.NumPad2: return "Num 2";
                case Keys.NumPad3: return "Num 3";
                case Keys.NumPad4: return "Num 4";
                case Keys.NumPad5: return "Num 5";
                case Keys.NumPad6: return "Num 6";
                case Keys.NumPad7: return "Num 7";
                case Keys.NumPad8: return "Num 8";
                case Keys.NumPad9: return "Num 9";
                default: return k.ToString();
            }
        }

        private void KeysPanel_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            if (heldKeys.Count == 0)
            {
                using (Font f = new Font("Microsoft YaHei UI", 9F))
                {
                    TextRenderer.DrawText(g, "请在键盘上同时按下任意按键（支持全键并发与无冲测试，系统热键已屏蔽）...", f,
                        new Rectangle(0, 0, keysPanel.Width, keysPanel.Height), Color.FromArgb(100, 110, 135),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                return;
            }

            int x = (int)(10 * dpiScale);
            int y = (int)(10 * dpiScale);
            int keyH = (int)(28 * dpiScale);

            foreach (Keys k in heldKeys)
            {
                string name = GetFriendlyKeyName(k);
                int w = Math.Max((int)(42 * dpiScale), (int)(g.MeasureString(name, Font).Width + (20 * dpiScale)));
                if (x + w > keysPanel.Width - (int)(10 * dpiScale))
                {
                    x = (int)(10 * dpiScale);
                    y += keyH + (int)(6 * dpiScale);
                }

                Rectangle r = new Rectangle(x, y, w, keyH);
                using (var path = RoundedCard.GetRoundedRectangle(r, (int)(4 * dpiScale)))
                {
                    using (var b = new SolidBrush(Color.FromArgb(40, 0, 230, 118))) g.FillPath(b, path);
                    using (var p = new Pen(Color.FromArgb(0, 230, 118), 1.2f)) g.DrawPath(p, path);
                }
                using (Font f = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold))
                {
                    TextRenderer.DrawText(g, name, f, r, Color.FromArgb(0, 230, 118),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }

                x += w + (int)(6 * dpiScale);
            }
        }
    }
}