// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// 1. 目标平台与编译规范：严格维持 C# 5.0 语法标准与 .NET Framework 4.0 兼容性。严禁引入任何第三方包或依赖。
// 2. 编码规范：所有文件严格保存为 UTF-8 带签名（BOM）格式，防止中文字符乱码。
// 3. 架构规范：本文件属于 MainForm 的分部类 (partial class) 之一。修改时请保持职责清晰，任何新增方法或逻辑请同步维护 ARCHITECTURE.md。
// 模块描述: MainForm 核心定义、字段声明、构造中枢、窗口生命周期与配置管理
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Media;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    public partial class MainForm : Form
    {
        // Windows 11 DWM Immersive Dark Mode & Styling API
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        [DllImport("user32.dll")]
        static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        const int DWMWA_BORDER_COLOR = 34;
        const int DWMWA_CAPTION_COLOR = 35;
        const int DWMWA_TEXT_COLOR = 36;

        // Hardware Change Notifications (USB Plug/Unplug, Wireless/Wired switch)
        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVICEARRIVAL = 0x8000;
        private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
        private const int DBT_DEVNODES_CHANGED = 0x0007;

        [StructLayout(LayoutKind.Sequential)]
        struct DEV_BROADCAST_DEVICEINTERFACE
        {
            public int dbcc_size;
            public int dbcc_devicetype;
            public int dbcc_reserved;
            public Guid dbcc_classguid;
            public short dbcc_name;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        const ushort HID_USAGE_PAGE_GENERIC = 0x01;
        const ushort HID_USAGE_GENERIC_MOUSE = 0x02;
        const uint RIDEV_INPUTSINK = 0x00000100;
        const int WM_INPUT = 0x00FF;
        const uint RID_HEADER = 0x10000005;
        const uint RIDI_DEVICENAME = 0x20000007;

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool RegisterRawInputDevices([MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] RAWINPUTDEVICE[] pRawInputDevices, uint uiNumDevices, uint cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        static extern int GetRawInputData(IntPtr hRawInput, uint uiCommand, out RAWINPUTHEADER pData, ref int pcbSize, int cbSizeHeader);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        static extern uint GetRawInputDeviceInfoW(IntPtr hDevice, uint uiCommand, StringBuilder pData, ref uint pcbSize);

        [DllImport("user32.dll", SetLastError = true)]
        static extern IntPtr RegisterDeviceNotification(IntPtr hRecipient, IntPtr NotificationFilter, uint Flags);

        [DllImport("user32.dll", SetLastError = true)]
        static extern bool UnregisterDeviceNotification(IntPtr Handle);

        const uint DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;
        const int DBT_DEVTYP_DEVICEINTERFACE = 5;

        private readonly Dictionary<IntPtr, bool> mouseDeviceWakeMatchCache = new Dictionary<IntPtr, bool>();
        private volatile bool isPassiveWakePollingQueued = false;

        private IntPtr hDevNotify = IntPtr.Zero;
        private System.Windows.Forms.Timer deviceChangeTimer1;
        private System.Windows.Forms.Timer deviceChangeTimer2;
        private int userSelectedInterval = 60000;

        private NotifyIcon trayIcon;
        private ContextMenuStrip contextMenu;
        private ToolStripMenuItem statusMenuItem;
        private ToolStripMenuItem autoStartMenuItem;
        private ToolStripMenuItem autoStartShowUIMenuItem;
        private ToolStripMenuItem lowBatteryAlertMenuItem;
        private ToolStripMenuItem dpiOsdMenuItem;
        private ToolStripMenuItem styleCapsuleItem;
        private ToolStripMenuItem styleNumItem;
        private ToolStripMenuItem stylePureNumItem;
        private ToolStripMenuItem osdStyleMenu;
        private ToolStripMenuItem osdStyleCapsuleItem;
        private ToolStripMenuItem osdStyleGaugeItem;
        private ToolStripMenuItem osdStyleCompactItem;
        private ToolStripMenuItem int30sMenuItem;
        private ToolStripMenuItem int1mMenuItem;
        private ToolStripMenuItem int5mMenuItem;
        private ToolStripMenuItem dpiMenu;
        private ToolStripMenuItem rateMenu;
        private ToolStripMenuItem showItem;
        private ToolStripMenuItem refreshItem;
        private ToolStripMenuItem prioMenuItem;
        private ToolStripMenuItem addDevMenuItem;
        private ToolStripMenuItem createDesktopLnkItem;
        private ToolStripMenuItem aboutMenuItem;
        private ToolStripMenuItem checkUpdateMenuItem;
        private ToolStripMenuItem exitItem;
        private ToolStripItem lastClickedTrayItem = null;
        private System.Windows.Forms.Timer updateTimer;

        public bool ShouldKeepTrayMenuOpen(ToolStripItem item)
        {
            if (item == null) return false;

            if (item == statusMenuItem ||
                item == showItem ||
                item == refreshItem ||
                item == prioMenuItem ||
                item == addDevMenuItem ||
                item == createDesktopLnkItem ||
                item == aboutMenuItem ||
                item == checkUpdateMenuItem ||
                item == exitItem)
            {
                return false;
            }

            ToolStripMenuItem mi = item as ToolStripMenuItem;
            if (mi != null && dynamicDeviceMenuItems != null && dynamicDeviceMenuItems.Contains(mi))
            {
                return false;
            }

            string text = item.Text ?? "";
            if (text.Contains("打开控制面板") ||
                text.Contains("立即刷新") ||
                text.Contains("设备优先级") ||
                text.Contains("手动添加新设备") ||
                text.Contains("创建桌面快捷方式") ||
                text.Contains("关于") ||
                text.Contains("检查更新") ||
                text.Contains("退出程序"))
            {
                return false;
            }

            return true;
        }

        // Mouse Sleeping & Instant Wakeup
        private volatile bool isMouseSleeping = false;
        private System.Windows.Forms.Timer bootPollTimer;
        private int bootPollCount = 0;

        // AutoStart Window Visibility Control
        private bool isAutoStartLaunch = false;
        private bool autoStartShowMainWindow = false;
        private bool allowVisibleCore = false;

        // Window Position Memory
        private int savedWindowX = -9999;
        private int savedWindowY = -9999;
        private bool isRestoringPosition = false;

        // Background DPI Listener Thread
        private Thread dpiMonitorThread;
        private volatile bool isDpiMonitorRunning = false;
        private int lastMonitoredDpi = -1;
        private int lastMonitoredStage = -1;
        private int lastMonitoredRapooDpi = -1;
        private int lastMonitoredRapooStage = -1;
        private Dictionary<string, int> lastMonitoredRazerDpis = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, int> lastMonitoredRazerStages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, int> pendingRazerDpiCandidates = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, int> pendingRazerStageCandidates = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private int pendingRapooDpiCandidate = -1;
        private int pendingRapooStageCandidate = -1;
        private int lastMonitoredValkyrieDpi = -1;
        private int lastMonitoredValkyrieStage = -1;
        private int pendingValkyrieDpiCandidate = -1;
        private int pendingValkyrieStageCandidate = -1;
        private DpiOsdForm osdForm;

        // Visual controls - Card 1: Status
        private RoundedCard cardBattery;
        private Label lblDeviceName;
        private Label lblBatteryBig;
        private StatusPill pillStatus;
        private ModernProgressBar barBattery;
        private Label lblUpdateTime;

        // Visual controls - Card 2: Performance & Tuning
        private RoundedCard cardPerformance;
        private Label lblPerfTitle;
        private Label lblDpiTitle;
        private ModernSegmentButton[] btnDpiStages;
        private ModernButton btnDpiManage;
        private List<int> dpiStageList = new List<int> { 400, 800, 1600, 3000, 6400 };
        private int[] dpiStageValues = new int[] { 400, 800, 1600, 3000, 6400 };
        private Label lblRateTitle;
        private ModernSegmentButton[] btnRates;
        private int[] pollingRateValues = new int[] { 1000, 2000, 4000, 8000 };
        private bool autoGameRateEnabled = false;
        private int gamePollingRate = 4000;
        private int desktopPollingRate = 1000;
        private bool isGamingActive = false;
        private int inGameTickCount = 0;
        private int outOfGameTickCount = 0;
        private bool gameRateNotifyOsd = true;
        private bool gameRateNotifyToast = true;
        private bool gameRateNotifySound = true;
        private System.Windows.Forms.Timer gameMonitorTimer;

        private ModernCheckBox chkAutoGameRate;
        private ModernButton btnManageGames;
        private Label lblLiveRateBadge;
        private Label lblGameRateTitle;
        private ModernSegmentButton[] btnGameRates;
        private Label lblNotifyModeTitle;
        private ModernCheckBox chkNotifyOsd;
        private ModernCheckBox chkNotifyToast;
        private ModernCheckBox chkNotifySound;

        private List<string> monitoredGameProcesses = new List<string> { "cs2.exe", "b1-win64-shipping.exe", "valorant-win64-shipping.exe" };
        private bool fallbackFullscreenEnabled = false;
        private System.Windows.Forms.Timer cardAnimTimer = null;
        private int animStartCardH = 0;
        private int animTargetCardH = 0;
        private DateTime animStartTime = DateTime.MinValue;
        private const int ANIM_DURATION_MS = 160;

        private ToolStripMenuItem autoGameRateMenu;
        private ToolStripMenuItem autoGameRateToggleItem;
        private RapooStatusTile tileRapooDpi;

        // Gamepad Dedicated Controls
        private ModernButton btnGamepadVibrate;
        private ModernButton btnGamepadJoyCpl;

        // Universal Keyboard Controls
        private ThreeLocksHudControl hudThreeLocks;
        private ModernButton btnUniversalWinLock;
        private ModernButton btnKeyTestSpeed;
        private ModernButton btnKeyboardSettings;

        // Audio Headset Controls
        private ModernButton btnAudioSetDefault;
        private ModernButton btnVolumeMixer;
        private ModernButton btnAudioSettings;

        // Generic Mouse Controls
        private ModernButton btnMouseRateTest;

        // NuPhy Hardware Console Controls
        private ModernButton btnNuphyAutoSleep;
        private ModernButton btnNuphyWinLock;
        private ModernButton btnNuphyBacklight;
        private ModernButton btnNuphySidelight;
        private Label lblNuphyBrightTitle;
        private TactileSmoothSliderControl sliderNuphyBrightness;
        private Label lblNuphyBrightVal;
        private volatile int nuphyTargetBrightness = -1;
        private volatile bool isNuphyBrightnessWorkerActive = false;
        private readonly object nuphyBrightnessSyncRoot = new object();

        // NuPhy Tray Dynamic Items
        private ToolStripMenuItem nuphySleepMenuItem;
        private ToolStripMenuItem nuphyWinLockMenuItem;
        private ToolStripMenuItem nuphyBacklightMenu;
        private ToolStripMenuItem nuphySidelightMenuItem;
        private ToolStripSeparator nuphyTraySep;

        // Visual controls - Card 3: Settings & Preferences
        private RoundedCard cardSettings;
        private Label lblSettingsTitle;
        private Label lblStyleTitle;
        private ModernSegmentButton btnStyleCapsule;
        private ModernSegmentButton btnStyleNum;
        private ModernSegmentButton btnStylePureNum;
        private Label lblIntervalTitle;
        private ModernSegmentButton btnInt30s;
        private ModernSegmentButton btnInt1m;
        private ModernSegmentButton btnInt5m;
        private Label lblOsdStyleTitle;
        private ModernSegmentButton btnOsdStyleCapsule;
        private ModernSegmentButton btnOsdStyleGauge;
        private ModernSegmentButton btnOsdStyleCompact;
        private SubtleDivider divSettings;
        private ModernCheckBox chkAutoStart;
        private ModernCheckBox chkAutoStartShowUI;
        private ModernCheckBox chkLowAlert;
        private ModernCheckBox chkDpiOsd;
        private Label lblSettingsTip;

        // Visual controls - Bottom Actions
        private ModernButton btnRefresh;
        private ModernButton btnHideToTray;

        private const string AppName = "FerrisPulse";
        private static readonly string[] LegacyAppNames = new string[] { "RazerBatteryTray", "RazerBattery", "RazerTray" };
        private const string RunRegistryKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string StartupApprovedRunKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        private const string ConfigRegistryKey = @"Software\FerrisPulse";

        private bool lowBatteryAlertEnabled = true;
        private bool dpiOsdEnabled = true;
        private bool lastLowAlertFired = false;
        private bool isUpdatingUI = false;
        private int trayStyle = 0; // 0 = Capsule, 1 = Number, 2 = Pure Number
        private int osdStyle = 0; // 0 = Centered Capsule, 1 = Stepped Gauge, 2 = Compact Top-Right
        private float dpiScale = 1.0f;
        private MouseBatteryInfo lastInfo = null;

        // Multi-Device Controls & State
        private FocusDeviceSelector focusDeviceSelector;
        private ModernButton btnTopPriority;
        private ModernButton btnTopAddDevice;
        private ModernButton btnTopAbout;
        private HeadsetStatusTile tileHeadsetAudio;
        private ToolTip toolTipTop;
        private StatusPill badgePrimary;
        private List<MouseBatteryInfo> activeDevices = new List<MouseBatteryInfo>();
        private MouseBatteryInfo selectedDevice = null;
        private ToolStripSeparator deviceMenuSep = null;
        private List<ToolStripMenuItem> dynamicDeviceMenuItems = new List<ToolStripMenuItem>();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern bool DestroyIcon(IntPtr handle);


        private DateTime lastBalloonTime = DateTime.MinValue;
        private string lastBalloonTitle = "";
        private ModernDeviceSwitcherFlyout switcherFlyout = null;
        private DateTime lastFlyoutCloseTime = DateTime.MinValue;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x02000000; // WS_CLIPCHILDREN: 杜绝主窗体背景绘制涂抹子控件
                return cp;
            }
        }

        public MainForm(bool isAutoStart = false)
        {
            this.isAutoStartLaunch = isAutoStart;
            this.AutoScaleMode = AutoScaleMode.None;

            using (Graphics g = this.CreateGraphics())
            {
                dpiScale = g.DpiX / 96.0f;
                if (dpiScale < 1.0f) dpiScale = 1.0f;
            }

            Screen primaryScrn = Screen.PrimaryScreen;
            if (primaryScrn != null)
            {
                dpiScale = DpiScaleHelper.ClampScaleToScreen(dpiScale, primaryScrn, 748);
            }

            try
            {
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico");
                if (File.Exists(icoPath))
                {
                    this.Icon = new Icon(icoPath);
                }
                else
                {
                    this.Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                }
            }
            catch { }

            osdForm = new DpiOsdForm(dpiScale);

            RazerDeviceHelper.LoadHardwareCache();
            RapooDeviceHelper.LoadHardwareCache();
            VgnDeviceHelper.LoadHardwareCache();
            NuphyDeviceHelper.LoadHardwareCache();

            LogitechDeviceHelper.LoadHardwareCache();
            InitializeFormUI();
            InitializeTray();
            LoadConfig();
            isRestoringPosition = true;
            try
            {
                RestoreWindowPosition();
            }
            finally
            {
                isRestoringPosition = false;
            }

            updateTimer = new System.Windows.Forms.Timer();
            updateTimer.Interval = userSelectedInterval;
            updateTimer.Tick += (s, e) => RefreshBatteryStatus(false);
            updateTimer.Start();

            RefreshBatteryStatus(false);
            StartBootPolling();
            StartDpiMonitor();
            StartGameMonitor();
            StartPeriodicUpdateCheck();
        }

        protected override void SetVisibleCore(bool value)
        {
            if (isAutoStartLaunch && !autoStartShowMainWindow && !allowVisibleCore)
            {
                value = false;
                if (!this.IsHandleCreated) CreateHandle();
            }
            base.SetVisibleCore(value);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyModernWin11Theme();
            RegisterUsbNotification();
        }

        internal void HandleDpiChanged(float newScale, Rectangle suggestedBounds)
        {
            if (newScale < 1.0f) newScale = 1.0f;

            Screen screen = Screen.FromRectangle(!suggestedBounds.IsEmpty ? suggestedBounds : this.Bounds);
            // 屏幕自适应防溢出安全上限约束：无论系统缩放多大，确保窗口高度绝对容纳在工作区内，底部主按钮永不被截断
            newScale = DpiScaleHelper.ClampScaleToScreen(newScale, screen, 748);

            this.dpiScale = newScale;

            if (osdForm != null && !osdForm.IsDisposed)
            {
                osdForm.UpdateDpiScale(dpiScale);
            }

            if (!suggestedBounds.IsEmpty)
            {
                // 只采用 Windows 建议的起点坐标，绝不接受其因之前截断折算出的错误高度
                this.SetBounds(suggestedBounds.Left, suggestedBounds.Top, 0, 0, BoundsSpecified.Location);
            }

            ApplyDpiScaleToControls();
        }

        protected override void OnLoad(EventArgs e)
        {
            isRestoringPosition = true;
            try
            {
                base.OnLoad(e);
                RestoreWindowPosition();
            }
            finally
            {
                isRestoringPosition = false;
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
                if (this.WindowState == FormWindowState.Normal && !isAutoStartLaunch)
                {
                    isRestoringPosition = true;
                    try
                    {
                        RestoreWindowPosition();
                    }
                    finally
                    {
                        isRestoringPosition = false;
                    }
                }
                this.TopMost = true;
                this.BringToFront();
                this.Activate();
                this.TopMost = false;
            }
            catch { }
            SyncAllDevicesPhysicalState();
        }

        protected override void OnVisibleChanged(EventArgs e)
        {
            base.OnVisibleChanged(e);
            if (this.Visible && this.WindowState != FormWindowState.Minimized)
            {
                SyncAllDevicesPhysicalState();
                UpdatePollingRateDisplayStates();
                UpdateManageGamesButtonText();
            }
        }

        private void RestoreWindowPosition()
        {
            try
            {
                if (savedWindowX <= -9000 || savedWindowY <= -9000)
                {
                    var workingArea = Screen.PrimaryScreen.WorkingArea;
                    this.StartPosition = FormStartPosition.Manual;
                    this.Location = new Point(
                        workingArea.Left + Math.Max(0, (workingArea.Width - this.Width) / 2),
                        workingArea.Top + Math.Max(0, (workingArea.Height - this.Height) / 2)
                    );
                    return;
                }

                Rectangle targetRect = new Rectangle(savedWindowX, savedWindowY, this.Width, this.Height);
                bool isVisibleOnAnyScreen = false;
                foreach (var scr in Screen.AllScreens)
                {
                    Rectangle intersection = Rectangle.Intersect(scr.WorkingArea, targetRect);
                    if (intersection.Width >= 100 && intersection.Height >= 100)
                    {
                        isVisibleOnAnyScreen = true;
                        break;
                    }
                }

                this.StartPosition = FormStartPosition.Manual;
                if (isVisibleOnAnyScreen)
                {
                    this.Location = new Point(savedWindowX, savedWindowY);
                }
                else
                {
                    var workingArea = Screen.PrimaryScreen.WorkingArea;
                    this.Location = new Point(
                        workingArea.Left + Math.Max(0, (workingArea.Width - this.Width) / 2),
                        workingArea.Top + Math.Max(0, (workingArea.Height - this.Height) / 2)
                    );
                }
            }
            catch
            {
                this.StartPosition = FormStartPosition.CenterScreen;
            }
        }

        private void ShowWindow(MouseBatteryInfo targetDev = null)
        {
            allowVisibleCore = true;
            isRestoringPosition = true;
            try
            {
                RestoreWindowPosition();
            }
            finally
            {
                isRestoringPosition = false;
            }
            var devToSelect = targetDev ?? GetPrimaryDevice();
            if (devToSelect != null)
            {
                SelectDeviceView(devToSelect);
            }
            this.Show();
            this.WindowState = FormWindowState.Normal;
            this.TopMost = true;
            this.BringToFront();
            this.Activate();
            this.TopMost = false;
            SyncAllDevicesPhysicalState();
        }

        private void ToggleWindow()
        {
            if (this.Visible && this.WindowState != FormWindowState.Minimized)
            {
                if (this.WindowState == FormWindowState.Normal && this.Location.X > -3000 && this.Location.Y > -3000)
                {
                    savedWindowX = this.Location.X;
                    savedWindowY = this.Location.Y;
                    SaveConfig();
                }
                this.Hide();
            }
            else
            {
                ShowWindow();
            }
        }

        private void SafeShowBalloonTip(int timeout, string title, string text, ToolTipIcon icon)
        {
            if (trayIcon == null) return;
            try
            {
                DateTime now = DateTime.Now;
                if ((now - lastBalloonTime).TotalSeconds < 3.5 && string.Equals(lastBalloonTitle, title, StringComparison.OrdinalIgnoreCase))
                {
                    return; // Suppress duplicate burst notifications
                }
                lastBalloonTime = now;
                lastBalloonTitle = title ?? "";
                trayIcon.ShowBalloonTip(timeout, title, text, ToolTipIcon.None);
            }
            catch { }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (this.WindowState == FormWindowState.Normal && this.Location.X > -3000 && this.Location.Y > -3000)
            {
                savedWindowX = this.Location.X;
                savedWindowY = this.Location.Y;
                SaveConfig();
            }

            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                this.Hide();
            }
            else
            {
                StopDpiMonitor();
                base.OnFormClosing(e);
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
        }

        private void SetDpiOsdEnabled(bool enabled, bool showNotification = false)
        {
            dpiOsdEnabled = enabled;

            isUpdatingUI = true;
            try
            {
                if (chkDpiOsd != null && chkDpiOsd.Checked != enabled) chkDpiOsd.Checked = enabled;
                if (dpiOsdMenuItem != null) dpiOsdMenuItem.Checked = enabled;
            }
            finally
            {
                isUpdatingUI = false;
            }

            SaveConfig();
        }

        private void SetInterval(int ms, bool showNotification = false)
        {
            userSelectedInterval = ms;
            if (updateTimer != null) updateTimer.Interval = ms;
            SaveConfig();

            isUpdatingUI = true;
            try
            {
                if (int30sMenuItem != null) int30sMenuItem.Checked = (ms == 30000);
                if (int1mMenuItem != null) int1mMenuItem.Checked = (ms == 60000);
                if (int5mMenuItem != null) int5mMenuItem.Checked = (ms == 300000);

                if (btnInt30s != null) btnInt30s.Selected = (ms == 30000);
                if (btnInt1m != null) btnInt1m.Selected = (ms == 60000);
                if (btnInt5m != null) btnInt5m.Selected = (ms == 300000);
            }
            finally
            {
                isUpdatingUI = false;
            }
        }

        private void SetTrayStyle(int style, bool showNotification = false)
        {
            trayStyle = style;
            if (trayStyle < 0 || trayStyle > 2) trayStyle = 0;
            SaveConfig();

            isUpdatingUI = true;
            try
            {
                if (styleCapsuleItem != null) styleCapsuleItem.Checked = (trayStyle == 0);
                if (styleNumItem != null) styleNumItem.Checked = (trayStyle == 1);
                if (stylePureNumItem != null) stylePureNumItem.Checked = (trayStyle == 2);

                if (btnStyleCapsule != null) btnStyleCapsule.Selected = (trayStyle == 0);
                if (btnStyleNum != null) btnStyleNum.Selected = (trayStyle == 1);
                if (btnStylePureNum != null) btnStylePureNum.Selected = (trayStyle == 2);
            }
            finally
            {
                isUpdatingUI = false;
            }

            if (lastInfo != null)
            {
                UpdateTrayIcon(lastInfo.BatteryPercent, lastInfo.IsCharging, lastInfo.IsConnected);
            }
            else
            {
                UpdateTrayIcon(-1, false, false);
            }
        }

        private void SetOsdStyle(int style, bool showNotification = false)
        {
            osdStyle = style;
            if (osdStyle < 0 || osdStyle > 2) osdStyle = 0;
            SaveConfig();

            if (osdForm != null)
            {
                osdForm.OsdStyle = osdStyle;
            }

            isUpdatingUI = true;
            try
            {
                if (osdStyleCapsuleItem != null) osdStyleCapsuleItem.Checked = (osdStyle == 0);
                if (osdStyleGaugeItem != null) osdStyleGaugeItem.Checked = (osdStyle == 1);
                if (osdStyleCompactItem != null) osdStyleCompactItem.Checked = (osdStyle == 2);

                if (btnOsdStyleCapsule != null) btnOsdStyleCapsule.Selected = (osdStyle == 0);
                if (btnOsdStyleGauge != null) btnOsdStyleGauge.Selected = (osdStyle == 1);
                if (btnOsdStyleCompact != null) btnOsdStyleCompact.Selected = (osdStyle == 2);
            }
            finally
            {
                isUpdatingUI = false;
            }

            // Trigger instant live preview so the user immediately sees their chosen style!
            if (dpiOsdEnabled && osdForm != null)
            {
                int previewDpi = (lastInfo != null && lastInfo.Dpi > 0) ? lastInfo.Dpi : 3000;
                int previewStage = (lastInfo != null && lastInfo.DpiStage > 0) ? lastInfo.DpiStage : 4;
                int previewCount = (lastInfo != null && lastInfo.DpiStageCount > 0) ? lastInfo.DpiStageCount : 5;
                string b = (selectedDevice != null) ? selectedDevice.Brand : ((lastInfo != null) ? lastInfo.Brand : "Razer");
                osdForm.ShowDpi(previewDpi, previewStage, previewCount, b);
            }
        }

        private void UpdateMenuStatusTexts()
        {
            if (lowBatteryAlertMenuItem != null)
            {
                lowBatteryAlertMenuItem.Text = "低电量气泡通知 (≤20%)";
                lowBatteryAlertMenuItem.Checked = lowBatteryAlertEnabled;
            }
            bool autoStart = IsAutoStartEnabled();
            if (autoStartMenuItem != null)
            {
                autoStartMenuItem.Text = "开机自动启动";
                autoStartMenuItem.Checked = autoStart;
            }
            if (autoStartShowUIMenuItem != null)
            {
                autoStartShowUIMenuItem.Text = "开机弹出主窗口";
                autoStartShowUIMenuItem.Checked = autoStartShowMainWindow;
                autoStartShowUIMenuItem.Enabled = autoStart;
            }
            if (chkAutoStartShowUI != null)
            {
                chkAutoStartShowUI.Checked = autoStartShowMainWindow;
                chkAutoStartShowUI.Enabled = autoStart;
            }
            if (dpiOsdMenuItem != null)
            {
                dpiOsdMenuItem.Text = "DPI 切换屏幕提示 (OSD)";
                dpiOsdMenuItem.Checked = dpiOsdEnabled;
            }
        }

        private void LoadConfig()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(ConfigRegistryKey) ?? Registry.CurrentUser.OpenSubKey(@"Software\RazerBatteryTray"))
                {
                    if (key != null)
                    {
                        var val = key.GetValue("LowBatteryAlert");
                        if (val != null) lowBatteryAlertEnabled = (int)val == 1;

                        var dVal = key.GetValue("DpiOsdAlert");
                        if (dVal != null) dpiOsdEnabled = (int)dVal == 1;

                        var autoShowVal = key.GetValue("AutoStartShowUI");
                        if (autoShowVal != null) autoStartShowMainWindow = (int)autoShowVal == 1;

                        var sVal = key.GetValue("TrayIconStyle");
                        if (sVal != null)
                        {
                            trayStyle = (int)sVal;
                            if (trayStyle < 0 || trayStyle > 2) trayStyle = 0;
                        }

                        var oVal = key.GetValue("OsdStyle");
                        if (oVal != null)
                        {
                            osdStyle = (int)oVal;
                            if (osdStyle < 0 || osdStyle > 2) osdStyle = 0;
                        }

                        var rVal = key.GetValue("RefreshInterval");
                        if (rVal != null)
                        {
                            userSelectedInterval = (int)rVal;
                            if (userSelectedInterval != 30000 && userSelectedInterval != 60000 && userSelectedInterval != 300000)
                            {
                                userSelectedInterval = 60000;
                            }
                        }

                        var autoGameVal = key.GetValue("AutoGameRateEnabled");
                        if (autoGameVal != null) autoGameRateEnabled = (int)autoGameVal == 1;

                        var gameRateVal = key.GetValue("GameTargetPollingRate");
                        if (gameRateVal != null) gamePollingRate = (int)gameRateVal;

                        var deskRateVal = key.GetValue("DesktopPollingRate");
                        if (deskRateVal != null) desktopPollingRate = (int)deskRateVal;

                        var notifyOsdVal = key.GetValue("GameRateNotifyOsd");
                        if (notifyOsdVal != null) gameRateNotifyOsd = (int)notifyOsdVal == 1;

                        var notifyToastVal = key.GetValue("GameRateNotifyToast");
                        if (notifyToastVal != null) gameRateNotifyToast = (int)notifyToastVal == 1;

                        var notifySoundVal = key.GetValue("GameRateNotifySound");
                        if (notifySoundVal != null) gameRateNotifySound = (int)notifySoundVal == 1;

                        var procListVal = key.GetValue("MonitoredGameProcesses") as string;
                        if (!string.IsNullOrEmpty(procListVal))
                        {
                            monitoredGameProcesses.Clear();
                            string[] items = procListVal.Split(new char[] { ';', '|', ',' }, StringSplitOptions.RemoveEmptyEntries);
                            for (int i = 0; i < items.Length; i++)
                            {
                                string trimmed = items[i].Trim();
                                if (!string.IsNullOrEmpty(trimmed))
                                {
                                    monitoredGameProcesses.Add(trimmed);
                                }
                            }
                        }

                        var fallbackVal = key.GetValue("FallbackFullscreenEnabled");
                        if (fallbackVal != null) fallbackFullscreenEnabled = (int)fallbackVal == 1;

                        var customDpiVal = key.GetValue("CustomDpiStages") as string;
                        if (!string.IsNullOrEmpty(customDpiVal))
                        {
                            string[] parts = customDpiVal.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                            List<int> loaded = new List<int>();
                            for (int i = 0; i < parts.Length && loaded.Count < 5; i++)
                            {
                                int pDpi;
                                if (int.TryParse(parts[i].Trim(), out pDpi) && pDpi >= 100 && pDpi <= 20000)
                                {
                                    loaded.Add(pDpi);
                                }
                            }
                            if (loaded.Count > 0)
                            {
                                dpiStageList = loaded;
                                dpiStageValues = dpiStageList.ToArray();
                            }
                        }

                        var wxVal = key.GetValue("WindowX");
                        if (wxVal != null) savedWindowX = (int)wxVal;
                        var wyVal = key.GetValue("WindowY");
                        if (wyVal != null) savedWindowY = (int)wyVal;
                    }
                }
            }
            catch { }

            CleanupLegacyAutoStart();
            bool autoStart = IsAutoStartEnabled();
            if (autoStart)
            {
                SyncAutoStartRegistry();
            }

            isUpdatingUI = true;
            try
            {
                UpdateMenuStatusTexts();
                if (chkLowAlert != null) chkLowAlert.Checked = lowBatteryAlertEnabled;
                if (chkDpiOsd != null) chkDpiOsd.Checked = dpiOsdEnabled;
                if (chkAutoStart != null) chkAutoStart.Checked = autoStart;
                if (chkAutoStartShowUI != null)
                {
                    chkAutoStartShowUI.Checked = autoStartShowMainWindow;
                    chkAutoStartShowUI.Enabled = autoStart;
                }
                if (autoStartShowUIMenuItem != null)
                {
                    autoStartShowUIMenuItem.Checked = autoStartShowMainWindow;
                    autoStartShowUIMenuItem.Enabled = autoStart;
                }

                if (styleCapsuleItem != null) styleCapsuleItem.Checked = (trayStyle == 0);
                if (styleNumItem != null) styleNumItem.Checked = (trayStyle == 1);
                if (stylePureNumItem != null) stylePureNumItem.Checked = (trayStyle == 2);
                if (btnStyleCapsule != null) btnStyleCapsule.Selected = (trayStyle == 0);
                if (btnStyleNum != null) btnStyleNum.Selected = (trayStyle == 1);
                if (btnStylePureNum != null) btnStylePureNum.Selected = (trayStyle == 2);

                if (osdForm != null) osdForm.OsdStyle = osdStyle;
                if (osdStyleCapsuleItem != null) osdStyleCapsuleItem.Checked = (osdStyle == 0);
                if (osdStyleGaugeItem != null) osdStyleGaugeItem.Checked = (osdStyle == 1);
                if (osdStyleCompactItem != null) osdStyleCompactItem.Checked = (osdStyle == 2);
                if (btnOsdStyleCapsule != null) btnOsdStyleCapsule.Selected = (osdStyle == 0);
                if (btnOsdStyleGauge != null) btnOsdStyleGauge.Selected = (osdStyle == 1);
                if (btnOsdStyleCompact != null) btnOsdStyleCompact.Selected = (osdStyle == 2);

                if (int30sMenuItem != null) int30sMenuItem.Checked = (userSelectedInterval == 30000);
                if (int1mMenuItem != null) int1mMenuItem.Checked = (userSelectedInterval == 60000);
                if (int5mMenuItem != null) int5mMenuItem.Checked = (userSelectedInterval == 300000);
                if (btnInt30s != null) btnInt30s.Selected = (userSelectedInterval == 30000);
                if (btnInt1m != null) btnInt1m.Selected = (userSelectedInterval == 60000);
                if (btnInt5m != null) btnInt5m.Selected = (userSelectedInterval == 300000);

                if (chkAutoGameRate != null) chkAutoGameRate.Checked = autoGameRateEnabled;
                if (autoGameRateToggleItem != null) autoGameRateToggleItem.Checked = autoGameRateEnabled;
                if (chkNotifyOsd != null) chkNotifyOsd.Checked = gameRateNotifyOsd;
                if (chkNotifyToast != null) chkNotifyToast.Checked = gameRateNotifyToast;
                if (chkNotifySound != null) chkNotifySound.Checked = gameRateNotifySound;
                UpdateGameRateButtonSelection(gamePollingRate);
                UpdateDesktopRateButtonSelection(desktopPollingRate);
                UpdateManageGamesButtonText();
                SetAutoGameRateEnabled(autoGameRateEnabled);
            }
            finally
            {
                isUpdatingUI = false;
            }
            RelayoutDpiButtons();
        }

        private void SaveConfig()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(ConfigRegistryKey))
                {
                    if (key != null)
                    {
                        key.SetValue("LowBatteryAlert", lowBatteryAlertEnabled ? 1 : 0);
                        key.SetValue("DpiOsdAlert", dpiOsdEnabled ? 1 : 0);
                        key.SetValue("AutoStartShowUI", autoStartShowMainWindow ? 1 : 0);
                        key.SetValue("TrayIconStyle", trayStyle);
                        key.SetValue("OsdStyle", osdStyle);
                        key.SetValue("RefreshInterval", userSelectedInterval);
                        key.SetValue("AutoGameRateEnabled", autoGameRateEnabled ? 1 : 0);
                        key.SetValue("GameTargetPollingRate", gamePollingRate);
                        key.SetValue("DesktopPollingRate", desktopPollingRate);
                        key.SetValue("GameRateNotifyOsd", gameRateNotifyOsd ? 1 : 0);
                        key.SetValue("GameRateNotifyToast", gameRateNotifyToast ? 1 : 0);
                        key.SetValue("GameRateNotifySound", gameRateNotifySound ? 1 : 0);
                        if (monitoredGameProcesses != null)
                        {
                            string procListStr = string.Join(";", monitoredGameProcesses.ToArray());
                            key.SetValue("MonitoredGameProcesses", procListStr);
                        }
                        key.SetValue("FallbackFullscreenEnabled", fallbackFullscreenEnabled ? 1 : 0);
                        if (savedWindowX > -3000 && savedWindowY > -3000)
                        {
                            key.SetValue("WindowX", savedWindowX);
                            key.SetValue("WindowY", savedWindowY);
                        }
                    }
                }
            }
            catch { }
        }

        public static void CleanupLegacyAutoStart()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true))
                {
                    if (key != null)
                    {
                        foreach (var legacyName in LegacyAppNames)
                        {
                            try { key.DeleteValue(legacyName, false); } catch { }
                        }
                    }
                }
            }
            catch { }

            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(StartupApprovedRunKey, true))
                {
                    if (key != null)
                    {
                        foreach (var legacyName in LegacyAppNames)
                        {
                            try { key.DeleteValue(legacyName, false); } catch { }
                        }
                    }
                }
            }
            catch { }
        }

        private bool IsAutoStartEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, false))
                {
                    if (key != null)
                    {
                        var val = key.GetValue(AppName);
                        if (val != null) return true;
                        foreach (var legacyName in LegacyAppNames)
                        {
                            if (key.GetValue(legacyName) != null) return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private void SyncAutoStartRegistry()
        {
            CleanupLegacyAutoStart();
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true))
                {
                    if (key != null)
                    {
                        var val = key.GetValue(AppName) as string;
                        string exePath = Application.ExecutablePath;
                        string expected = "\"" + exePath + "\" --autostart";
                        if (!string.IsNullOrEmpty(val))
                        {
                            if (!string.Equals(val.Trim(), expected, StringComparison.OrdinalIgnoreCase))
                            {
                                key.SetValue(AppName, expected);
                            }
                        }
                        else
                        {
                            key.SetValue(AppName, expected);
                        }
                    }
                }
            }
            catch { }
        }

        private void SetAutoStart(bool enabled, bool showNotification = false)
        {
            CleanupLegacyAutoStart();
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(RunRegistryKey, true))
                {
                    if (key != null)
                    {
                        if (enabled)
                        {
                            string exePath = Application.ExecutablePath;
                            key.SetValue(AppName, "\"" + exePath + "\" --autostart");
                        }
                        else
                        {
                            key.DeleteValue(AppName, false);
                            try
                            {
                                using (var appKey = Registry.CurrentUser.OpenSubKey(StartupApprovedRunKey, true))
                                {
                                    if (appKey != null)
                                    {
                                        appKey.DeleteValue(AppName, false);
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show("设置开机启动失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            isUpdatingUI = true;
            try
            {
                UpdateMenuStatusTexts();
                if (chkAutoStart != null && chkAutoStart.Checked != enabled) chkAutoStart.Checked = enabled;
                if (chkAutoStartShowUI != null) chkAutoStartShowUI.Enabled = enabled;
                if (autoStartShowUIMenuItem != null) autoStartShowUIMenuItem.Enabled = enabled;
            }
            finally
            {
                isUpdatingUI = false;
            }

        }

        private void SetAutoStartShowUI(bool showUI, bool showNotification = false)
        {
            autoStartShowMainWindow = showUI;
            SaveConfig();

            isUpdatingUI = true;
            try
            {
                if (chkAutoStartShowUI != null && chkAutoStartShowUI.Checked != showUI) chkAutoStartShowUI.Checked = showUI;
                if (autoStartShowUIMenuItem != null) autoStartShowUIMenuItem.Checked = showUI;
            }
            finally
            {
                isUpdatingUI = false;
            }
        }

        private void SetLowBatteryAlert(bool enabled, bool showNotification = false)
        {
            lowBatteryAlertEnabled = enabled;

            isUpdatingUI = true;
            try
            {
                UpdateMenuStatusTexts();
                if (chkLowAlert != null && chkLowAlert.Checked != enabled) chkLowAlert.Checked = enabled;
            }
            finally
            {
                isUpdatingUI = false;
            }

            SaveConfig();
        }

    }
}
