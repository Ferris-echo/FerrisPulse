﻿// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️

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

        private int userSelectedInterval = 10000;



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

        private ToolStripMenuItem int10sMenuItem;

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

        // 窗口是否处于后台（最小化或隐藏）。由 UI 线程在 ApplyAdaptiveInterval 中更新，
        // 供 DpiMonitorWorker 等后台线程读取——后台线程不能直接读 WindowState/Visible
        // （最小化时 Visible 仍为 true，只判 Visible 会漏掉「最小化到托盘」这种最常见场景）。
        private volatile bool isBackgroundMode = false;

        // 是否已经历过首次显示（用于省电模式下跳过启动时的首次查询）
        private bool hasShownOnce = false;

        // 唤醒刷新标记（仅在窗口可见时由 GetLastInputInfo 触发，10 秒检测、60 秒节流）。
        // 注意：它**不能**绕过省电守卫——启动时点击图标的输入会被误判为「唤醒」，
        // 因此静默窗口内所有自动刷新一律被拦下，只有用户主动操作才放行。
        private volatile bool wakeRefreshPending = false;
        private DateTime lastWakeRefreshUtc = DateTime.MinValue;

        private System.Windows.Forms.Timer bootPollTimer;

        private int bootPollCount = 0;



        // AutoStart Window Visibility Control

        private bool isAutoStartLaunch = false;

        private bool autoStartShowMainWindow = false;

        private bool allowVisibleCore = false;



        // Single Instance Wakeup Event Listener

        private EventWaitHandle singleInstanceWakeupEvent = null;

        private Thread wakeupListenerThread = null;

        private volatile bool isWakeupListenerRunning = false;



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



        private DateTime lastRapooDpiPollTime = DateTime.MinValue;
        private DateTime lastGameOptOsdTime = DateTime.MinValue;

        private Dictionary<string, int> lastMonitoredRazerDpis = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, int> lastMonitoredRazerStages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, int> pendingRazerDpiCandidates = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        private Dictionary<string, int> pendingRazerStageCandidates = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);



        private int lastMonitoredValkyrieDpi = -1;

        private int lastMonitoredValkyrieStage = -1;

        private int pendingValkyrieDpiCandidate = -1;

        private int pendingValkyrieStageCandidate = -1;

        private int lastMonitoredLogitechDpi = -1;

        private int lastMonitoredLogitechStage = -1;

        private int pendingLogitechDpiCandidate = -1;

        private int pendingLogitechStageCandidate = -1;

        private int lastMonitoredZaopinStage = -1;

        private int pendingZaopinStageCandidate = -1;

        private DateTime lastZaopinDpiPollTime = DateTime.MinValue;

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

        private ModernButton btnRateManage;

        // 默认档位刻意不含 8000：这是「设备数据尚未到达」时的占位值，
        // 用 8000 会让只支持 4K 的鼠标（如皂品 Z2）在启动阶段显示出不存在的档位。
        // 真实档位由设备查询结果经 RelayoutPerformanceControls 重建。
        private int[] pollingRateValues = new int[] { 125, 250, 500, 1000 };

        // 上一次渲染到界面的档位签名。只有变化时才重排按钮：
        // 启动时会先按上面的默认值排一次版，真实数据到达后若不重排，
        // 8000Hz 会一直留在界面上（实测 2026-10-30）。
        private string lastRenderedRateSig = "";

        // 托盘「放停后鼠标休眠时间」的本地写入记录：设备回读可能滞后，
        // 10 秒内以本地值为准，避免勾选状态回跳。
        private int lastSleepSeconds = -1;
        private DateTime lastSleepWriteUtc = DateTime.MinValue;

        private bool autoGameRateEnabled = false;

        private int gamePollingRate = 4000;

        private int desktopPollingRate = 1000;

        private bool autoGameDpiEnabled = false;

        private int gameDpiStage = 2;

        private int desktopDpiStage = 0;

        private bool isGamingActive = false;

        private int inGameTickCount = 0;

        private int outOfGameTickCount = 0;

        private bool gameRateNotifyOsd = true;

        private bool gameRateNotifyToast = true;

        private bool gameRateNotifySound = true;

        private System.Windows.Forms.Timer gameMonitorTimer;



        private ModernCheckBox chkAutoGameOpt;

        private ModernCheckBox chkAutoGameRate;

        private ModernCheckBox chkAutoGameDpi;

        private ModernButton btnManageGames;

        private Label lblLiveRateBadge;

        private Label lblGameDpiTitle;

        private ModernSegmentButton[] btnGameDpiStages;

        private ModernButton btnGameDpiManage;

        private Label lblGameRateTitle;

        private ModernSegmentButton[] btnGameRates;

        private Label lblNotifyModeTitle;

        private ModernCheckBox chkNotifyOsd;

        private ModernCheckBox chkNotifyToast;

        private ModernCheckBox chkNotifySound;



        private List<string> monitoredGameProcesses = new List<string> { "cs2.exe", "b1-win64-shipping.exe", "valorant-win64-shipping.exe" };

        private bool fallbackFullscreenEnabled = false;

        private int gameOptMode = 1; // 0 = 仅前台激活模式, 1 = 进程存活常驻模式 (推荐)

        public int GameOptMode
        {
            get { return gameOptMode; }
            set { gameOptMode = value; }
        }

        private System.Windows.Forms.Timer cardAnimTimer = null;

        private int animStartCardH = 0;

        private int animTargetCardH = 0;

        private DateTime animStartTime = DateTime.MinValue;

        private const int ANIM_DURATION_MS = 160;



        private ToolStripMenuItem autoGameRateMenu;

        private ToolStripMenuItem autoGameRateToggleItem;

        private ToolStripMenuItem autoGameDpiToggleItem;

        private ToolStripMenuItem gameRateSub;

        private ToolStripMenuItem gameDpiSub;

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

        private ModernSegmentButton[] btnIntPresets;

        private ModernButton btnIntCustom;

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

            catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.cs: 已忽略异常", diagEx); }



            osdForm = new DpiOsdForm(dpiScale);



            RazerDeviceHelper.LoadHardwareCache();

            RapooDeviceHelper.LoadHardwareCache();

            VgnDeviceHelper.LoadHardwareCache();

            NuphyDeviceHelper.LoadHardwareCache();



            LogitechDeviceHelper.LoadHardwareCache();

            HyperXDeviceHelper.LoadHardwareCache();

            SteelSeriesDeviceHelper.LoadHardwareCache();

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

            StartWakeupEventListener();

        }



        private void StartWakeupEventListener()

        {

            try

            {

                bool createdNew;

                singleInstanceWakeupEvent = new EventWaitHandle(false, EventResetMode.AutoReset, Program.SingleInstanceEventName, out createdNew);

                isWakeupListenerRunning = true;

                wakeupListenerThread = new Thread(ListenForWakeupEvents);

                wakeupListenerThread.IsBackground = true;

                wakeupListenerThread.Name = "FerrisPulseWakeupListener";

                wakeupListenerThread.Start();

            }

            catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.cs: 已忽略异常", diagEx); }

        }



        private void ListenForWakeupEvents()

        {

            while (isWakeupListenerRunning && singleInstanceWakeupEvent != null)

            {

                try

                {

                    if (singleInstanceWakeupEvent.WaitOne())

                    {

                        if (!isWakeupListenerRunning) break;

                        if (!this.IsDisposed)

                        {

                            try

                            {

                                this.BeginInvoke(new Action(() =>

                                {

                                    ShowWindow();

                                }));

                            }

                            catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.cs: 已忽略异常", diagEx); }

                        }

                    }

                }

                catch (ThreadAbortException)

                {

                    break;

                }

                catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.cs: 已忽略异常", diagEx); }

            }

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
            try
            {
                // UIPI 白名单豁免：允许双击启动的新进程或不同完整性进程穿透 Windows 权限边界发送唤醒和通知栏重建消息
                NativeMethods.ChangeWindowMessageFilterEx(this.Handle, (uint)Program.WM_SHOWME, NativeMethods.MSGFLT_ALLOW, IntPtr.Zero);
                if (NativeMethods.WM_TASKBARCREATED != 0)
                {
                    NativeMethods.ChangeWindowMessageFilterEx(this.Handle, (uint)NativeMethods.WM_TASKBARCREATED, NativeMethods.MSGFLT_ALLOW, IntPtr.Zero);
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.cs: 已忽略异常", diagEx); }
            ApplyModernWin11Theme();
            RegisterUsbNotification();
            if (isMouseSleeping)
            {
                EnableMouseWakeDetector(true);
            }
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            if (hDevNotify != IntPtr.Zero)
            {
                try { UnregisterDeviceNotification(hDevNotify); } catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.cs: 已忽略异常", diagEx); }
                hDevNotify = IntPtr.Zero;
            }
            base.OnHandleDestroyed(e);
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

            catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.cs: 已忽略异常", diagEx); }

            SyncAllDevicesPhysicalState();

        }



        protected override void OnVisibleChanged(EventArgs e)

        {

            base.OnVisibleChanged(e);

            ApplyAdaptiveInterval();


            if (this.Visible && this.WindowState != FormWindowState.Minimized)

            {

                // 窗口重新可见：隐藏期间完全不查询（省电策略），此刻需立即补一次，
                // 否则要等下一个 tick（最长一个刷新间隔）才更新，用户会看到旧电量。
                // 例外：省电模式下**首次显示**不查询——那正是「启动即把鼠标从 LP 推到 HP」的元凶（仅 ≤1000Hz 无线成立）。
                bool skipFirstQuery = VgnDeviceHelper.EcoModeEnabled && !hasShownOnce;
                hasShownOnce = true;
                if (!skipFirstQuery)
                {
                    RefreshBatteryStatus(false);
                    // SyncAllDevicesPhysicalState 内部也会 RefreshBatteryStatus，必须一起跳过
                    SyncAllDevicesPhysicalState();
                }
                else
                {
                    // 省电模式启动不查询：用上次缓存值填充界面，避免显示「未连接设备」误导用户
                    ShowEcoCachedState();
                }

                UpdatePollingRateDisplayStates();

                UpdateManageGamesButtonText();

            }

        }



        protected override void OnResize(EventArgs e)

        {

            base.OnResize(e);

            ApplyAdaptiveInterval();

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

                Screen targetScreen = null;

                foreach (var scr in Screen.AllScreens)

                {

                    Rectangle intersection = Rectangle.Intersect(scr.WorkingArea, targetRect);

                    if (intersection.Width >= 100 && intersection.Height >= 100)

                    {

                        targetScreen = scr;

                        break;

                    }

                }



                if (targetScreen == null)

                {

                    targetScreen = Screen.PrimaryScreen;

                }



                var wa = targetScreen.WorkingArea;

                int clampedX = Math.Max(wa.Left, Math.Min(savedWindowX, Math.Max(wa.Left, wa.Right - this.Width)));

                int clampedY = Math.Max(wa.Top, Math.Min(savedWindowY, Math.Max(wa.Top, wa.Bottom - this.Height)));



                this.StartPosition = FormStartPosition.Manual;

                this.Location = new Point(clampedX, clampedY);

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

                var devToSelect = targetDev ?? GetPrimaryDevice();

                if (devToSelect != null)

                {

                    SelectDeviceView(devToSelect);

                }



                if (!this.Visible)
                {
                    try
                    {
                        this.RecreateHandle();
                        RestoreWindowPosition();
                    }
                    catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.cs: 已忽略异常", diagEx); }

                    this.WindowState = FormWindowState.Normal;
                    this.Show();
                }
                else if (this.WindowState == FormWindowState.Minimized)
                {
                    this.WindowState = FormWindowState.Normal;
                }

                NativeMethods.ForceForegroundWindow(this.Handle);

                this.Activate();

                SyncAllDevicesPhysicalState();

            }

            finally

            {

                isRestoringPosition = false;

            }

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

            catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.cs: 已忽略异常", diagEx); }

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

                Diagnostics.Log(string.Format("MainForm 关闭：reason={0}", e.CloseReason));

                StopDpiMonitor();

                base.OnFormClosing(e);

            }

        }



        protected override void OnFormClosed(FormClosedEventArgs e)

        {

            isWakeupListenerRunning = false;

            if (singleInstanceWakeupEvent != null)

            {

                try { singleInstanceWakeupEvent.Set(); } catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.cs: 已忽略异常", diagEx); }

                try { singleInstanceWakeupEvent.Close(); } catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.cs: 已忽略异常", diagEx); }

                singleInstanceWakeupEvent = null;

            }

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



        // 按当前间隔点亮对应预设按钮
        private void ApplyPresetSelection(int ms)

        {

            if (btnIntPresets == null) return;

            int[] presets = new int[] { 10000, 30000, 60000 };

            // 省电模式会强制 2 分钟间隔，预设按钮必须灰显并取消选中，
            // 否则界面显示「1 分钟」而实际跑 2 分钟，用户无从判断。
            bool ecoLocked = VgnDeviceHelper.EcoModeEnabled;

            for (int i = 0; i < btnIntPresets.Length && i < presets.Length; i++)

            {

                if (btnIntPresets[i] == null) continue;

                btnIntPresets[i].Enabled = !ecoLocked;

                btnIntPresets[i].Selected = !ecoLocked && (presets[i] == ms);

            }

            if (btnIntCustom != null) btnIntCustom.Enabled = !ecoLocked;

            if (lblIntervalTitle != null)
            {
                lblIntervalTitle.Text = ecoLocked ? "自动刷新·省电" : "自动刷新频率";
                lblIntervalTitle.ForeColor = ecoLocked ? Color.FromArgb(255, 183, 77) : Color.FromArgb(160, 168, 185);
            }

            if (toolTipTop != null && lblIntervalTitle != null)
            {
                toolTipTop.SetToolTip(lblIntervalTitle, ecoLocked
                    ? "省电模式已开启：实际刷新间隔锁定为 2 分钟，关闭省电模式后恢复"
                    : "");
            }

        }

        // 打开刷新频率档位弹窗（结构同 DPI 档位配置）
        private void OpenRefreshIntervalDialog()

        {

            using (RefreshIntervalDialog dlg = new RefreshIntervalDialog(userSelectedInterval))

            {

                if (dlg.ShowDialog(this) == DialogResult.OK)

                {

                    SetInterval(dlg.SelectedIntervalMs, true);

                }

            }

        }

        // 自适应刷新：窗口可见时用用户设定（默认 10 秒），最小化/隐藏时不低于 30 秒以省电
        private void ApplyAdaptiveInterval()

        {

            if (updateTimer == null) return;

            // 线程安全守卫：本方法读取 WindowState/Visible（有线程亲和性），
            // 后台线程误调用会抛 InvalidOperationException 并可能静默终止进程。
            if (this.InvokeRequired)

            {

                try { this.BeginInvoke(new Action(ApplyAdaptiveInterval)); }

                catch (Exception diagEx) { Diagnostics.Log("MainForm.cs: ApplyAdaptiveInterval 跨线程投递失败", diagEx); }

                return;

            }

            bool background = (this.WindowState == FormWindowState.Minimized) || !this.Visible;
            isBackgroundMode = background;

            // 与官方驱动一致的休眠策略（实测结论，勿轻易改动）：
            //   窗口可见 → 按用户设定间隔轮询
            //   窗口隐藏/最小化 → **停止轮询，且不注册 RawInput**，让鼠标真正进入休眠。
            // 为什么连 RawInput 也要取消：RIDEV_INPUTSINK 注册后 Windows 会持续从鼠标
            //   数据口读取输入报告，设备因此永远不挂起（实测：零查询 44 秒后接收器灯仍亮；
            //   反之仅官方驱动在跑时灯灭）。代价：隐藏期间托盘电量不刷新（官方同样如此）。
            if (background)
            {
                if (updateTimer.Enabled) updateTimer.Stop();
                EnableMouseWakeDetector(false);
                // 方案 A：隐藏期**不做唤醒刷新**——任何查询都会把鼠标从 LP 推到 HP（≤1000Hz 无线场景）且不会
                // 自动恢复，与省电目标冲突。隐藏期保持零 I/O，仅在窗口可见时刷新。
                StopWakeCheckTimer();
            }
            else
            {
                // 省电模式：窗口可见时也不按用户设定间隔轮询，强制放宽到 2 分钟。
                // 原因：每次查询都会把鼠标从 LP 低功耗推到 HP 高性能并打断其休眠计时
                //（仅 ≤1000Hz 无线成立；≥2000Hz 恒为 Corded 高性能态，无此影响），
                // 30 秒一次的轮询等于让鼠标永远无法进入深度休眠（实测：官方驱动在跑时
                // 鼠标能休眠，只开本程序就不行）。2 分钟是「省电」与「电量不过期太久」的折中。
                int effectiveInterval = userSelectedInterval;
                if (VgnDeviceHelper.EcoModeEnabled && effectiveInterval < 120000)
                {
                    effectiveInterval = 120000;
                }
                if (updateTimer.Interval != effectiveInterval) updateTimer.Interval = effectiveInterval;
                if (!updateTimer.Enabled) updateTimer.Start();
                EnableMouseWakeDetector(false);
                StartWakeCheckTimer();   // 前台也常驻：移动鼠标即刻刷新（纯 API，零 USB 开销）
            }

        }

        // 省电模式启动时：不查设备，直接用上次缓存值填充界面
        private void ShowEcoCachedState()
        {
            try
            {
                VgnDeviceHelper.LoadHardwareCache();
                int cached = VgnDeviceHelper.CachedBatteryPercent;
                bool hasCache = cached > 0 && cached <= 100;

                if (lblDeviceName != null) lblDeviceName.Text = "皂品 Z2";
                if (lblBatteryBig != null)
                {
                    lblBatteryBig.Text = hasCache ? (cached.ToString() + "%") : "--%";
                    lblBatteryBig.ForeColor = Color.FromArgb(150, 160, 175);
                }
                if (pillStatus != null)
                {
                    pillStatus.SetStatus(hasCache ? "缓存数据" : "待刷新", Color.FromArgb(255, 183, 77));
                    if (lblBatteryBig != null)
                    {
                        pillStatus.Location = new Point(lblBatteryBig.Right + (int)(16 * dpiScale), (int)(56 * dpiScale));
                    }
                }
                if (barBattery != null) barBattery.StopFlow(hasCache ? cached : 0, Color.FromArgb(255, 183, 77));
                if (lblUpdateTime != null)
                {
                    lblUpdateTime.Text = "省电模式：启动 90 秒内不自动刷新，点「立即刷新」可强制获取";
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.cs: 填充缓存态失败", diagEx); }
        }

        // ===== 唤醒刷新：GetLastInputInfo 检测系统输入，**完全不碰 HID 设备** =====
        // 隐藏期每 10 秒读一次「系统最后一次输入时间」，发现有新输入（用户动了鼠标/键盘）
        // 就触发一次电量刷新（60 秒节流）。相比 RawInput 的 RIDEV_INPUTSINK，
        // 这不产生任何 USB 读取，不会阻止设备挂起。
        private System.Windows.Forms.Timer wakeCheckTimer = null;
        private uint lastInputTick = 0;

        [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [System.Runtime.InteropServices.DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        private void StartWakeCheckTimer()
        {
            if (wakeCheckTimer == null)
            {
                wakeCheckTimer = new System.Windows.Forms.Timer();
                wakeCheckTimer.Interval = 3000;
                wakeCheckTimer.Tick += (s2, e2) =>
                {
                    try
                    {
                        LASTINPUTINFO info = new LASTINPUTINFO();
                        info.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(LASTINPUTINFO));
                        if (!GetLastInputInfo(ref info)) return;

                        if (info.dwTime == lastInputTick) return;   // 无新输入
                        lastInputTick = info.dwTime;

                        DateTime now = DateTime.UtcNow;
                        // 唤醒刷新节流：普通模式 15 秒（回报率等状态跟手），
                        // 省电模式保持 60 秒——否则鼠标一动就 15 秒刷一次，
                        // 会把省电模式 2 分钟的轮询上限架空。
                        int wakeThrottleSec = VgnDeviceHelper.EcoModeEnabled ? 60 : 15;
                        if ((now - lastWakeRefreshUtc).TotalSeconds < wakeThrottleSec) return;
                        lastWakeRefreshUtc = now;

                        wakeRefreshPending = true;   // 唤醒触发的刷新（启动静默窗口内仍会被守卫拦下）
                        RefreshBatteryStatus(false);
                    }
                    catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.cs: 唤醒检测失败", diagEx); }
                };
            }
            if (!wakeCheckTimer.Enabled) wakeCheckTimer.Start();
        }

        private void StopWakeCheckTimer()
        {
            if (wakeCheckTimer != null && wakeCheckTimer.Enabled) wakeCheckTimer.Stop();
        }

        private void SetInterval(int ms, bool showNotification = false)

        {

            userSelectedInterval = ms;

            ApplyAdaptiveInterval();

            SaveConfig();



            isUpdatingUI = true;

            try

            {

                if (int10sMenuItem != null) int10sMenuItem.Checked = (ms == 10000);

                if (int30sMenuItem != null) int30sMenuItem.Checked = (ms == 30000);

                if (int1mMenuItem != null) int1mMenuItem.Checked = (ms == 60000);

                if (int5mMenuItem != null) int5mMenuItem.Checked = (ms == 300000);



                ApplyPresetSelection(ms);

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




    }
}

