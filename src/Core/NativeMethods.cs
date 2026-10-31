// ============================================================================
// 模块名称: NativeMethods.cs
// 所属区域: Core (核心管理层)
// 核心职责:
//   集中管理 Windows Win32 API P/Invoke 声明、COM 互操作接口、消息常量与 TaskbarCreated 消息
// 依赖关系: 无外部依赖
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在新增或修改 Win32 API 签名时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Runtime.InteropServices;
using System.Text;

namespace RazerBatteryTray
{
    internal static class NativeMethods
    {
        // ==================== DPI Awareness ====================
        [DllImport("user32.dll")]
        public static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        public static extern uint GetDpiForWindow(IntPtr hWnd);

        [DllImport("kernel32.dll", CharSet = CharSet.Ansi, ExactSpelling = true, SetLastError = true)]
        public static extern IntPtr GetProcAddress(IntPtr hModule, string procName);

        [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern IntPtr GetModuleHandle(string lpModuleName);

        // ==================== System Uptime & Process Queries ====================
        [DllImport("kernel32.dll")]
        public static extern ulong GetTickCount64();

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        public const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        // ==================== AppUserModelID & Shell ====================
        [DllImport("shell32.dll", SetLastError = true)]
        public static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

        [DllImport("shell32.dll")]
        public static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

        // ==================== Window Messages & Taskbar ====================
        [DllImport("user32.dll")]
        public static extern bool ReleaseCapture();

        [DllImport("user32.dll")]
        public static extern IntPtr SendMessage(IntPtr hWnd, int Msg, IntPtr wParam, IntPtr lParam);

        public const int WM_NCLBUTTONDOWN = 0x00A1;
        public const int HT_CAPTION = 2;

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern int RegisterWindowMessage(string lpString);

        // TaskbarCreated: 当 Explorer 启动/重启/通知栏重建时由系统广播
        public static readonly int WM_TASKBARCREATED = RegisterWindowMessage("TaskbarCreated");

        public const int WM_INPUT = 0x00FF;
        public const int WM_SIZING = 0x0214;
        public const int WM_ENTERSIZEMOVE = 0x0231;
        public const int WM_EXITSIZEMOVE = 0x0232;
        public const int WM_DPICHANGED = 0x02E0;
        public const int WM_DEVICECHANGE = 0x0219;

        public const int WMSZ_LEFT = 1;
        public const int WMSZ_RIGHT = 2;
        public const int WMSZ_TOP = 3;
        public const int WMSZ_TOPLEFT = 4;
        public const int WMSZ_TOPRIGHT = 5;
        public const int WMSZ_BOTTOM = 6;
        public const int WMSZ_BOTTOMLEFT = 7;
        public const int WMSZ_BOTTOMRIGHT = 8;

        public const int DBT_DEVICEARRIVAL = 0x8000;
        public const int DBT_DEVICEREMOVECOMPLETE = 0x8004;
        public const int DBT_DEVNODES_CHANGED = 0x0007;
        public const int DBT_DEVTYP_DEVICEINTERFACE = 5;
        public const uint DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;
        public const uint DEVICE_NOTIFY_ALL_INTERFACE_CLASSES = 0x00000004;

        // ==================== Raw Input ====================
        public const ushort HID_USAGE_PAGE_GENERIC = 0x01;
        public const ushort HID_USAGE_GENERIC_MOUSE = 0x02;
        public const uint RIDEV_REMOVE = 0x00000001;
        public const uint RIDEV_INPUTSINK = 0x00000100;
        public const uint RID_HEADER = 0x10000005;
        public const uint RIDI_DEVICENAME = 0x20000007;

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTDEVICE
        {
            public ushort usUsagePage;
            public ushort usUsage;
            public uint dwFlags;
            public IntPtr hwndTarget;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct RAWINPUTHEADER
        {
            public uint dwType;
            public uint dwSize;
            public IntPtr hDevice;
            public IntPtr wParam;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterRawInputDevices(
            [MarshalAs(UnmanagedType.LPArray, SizeParamIndex = 1)] RAWINPUTDEVICE[] pRawInputDevices, 
            uint uiNumDevices, 
            uint cbSize);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern int GetRawInputData(
            IntPtr hRawInput, 
            uint uiCommand, 
            out RAWINPUTHEADER pData, 
            ref int pcbSize, 
            int cbSizeHeader);

        [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        public static extern uint GetRawInputDeviceInfoW(
            IntPtr hDevice, 
            uint uiCommand, 
            StringBuilder pData, 
            ref uint pcbSize);

        // ==================== Device Notification ====================
        [StructLayout(LayoutKind.Sequential)]
        public struct DEV_BROADCAST_DEVICEINTERFACE
        {
            public int dbcc_size;
            public int dbcc_devicetype;
            public int dbcc_reserved;
            public Guid dbcc_classguid;
            public short dbcc_name;
        }

        [DllImport("user32.dll", SetLastError = true)]
        public static extern IntPtr RegisterDeviceNotification(IntPtr hRecipient, IntPtr NotificationFilter, uint Flags);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterDeviceNotification(IntPtr Handle);

        // ==================== Foreground & Window APIs ====================
        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        [DllImport("user32.dll")]
        public static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", SetLastError = true)]
        public static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll", CharSet = CharSet.Auto, SetLastError = true)]
        public static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        public static extern bool DestroyIcon(IntPtr handle);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool AllowSetForegroundWindow(uint dwProcessId);

        public const uint ASFW_ANY = unchecked((uint)-1);

        [DllImport("user32.dll")]
        public static extern bool SetForegroundWindow(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool ShowWindowAsync(IntPtr hWnd, int nCmdShow);

        [DllImport("user32.dll")]
        public static extern bool BringWindowToTop(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool fAttach);

        [DllImport("kernel32.dll")]
        public static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        public static extern bool PostMessage(IntPtr hWnd, int msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool ChangeWindowMessageFilterEx(IntPtr hWnd, uint message, uint action, IntPtr pChangeFilterStruct);

        public const int SW_HIDE = 0;
        public const int SW_SHOWNORMAL = 1;
        public const int SW_SHOWMINIMIZED = 2;
        public const int SW_MAXIMIZE = 3;
        public const int SW_SHOW = 5;
        public const int SW_RESTORE = 9;

        public const uint MSGFLT_RESET = 0;
        public const uint MSGFLT_ALLOW = 1;
        public const uint MSGFLT_DISALLOW = 2;

        [DllImport("user32.dll")]
        public static extern bool IsIconic(IntPtr hWnd);

        [DllImport("user32.dll")]
        public static extern bool AnimateWindow(IntPtr hWnd, int dwTime, int dwFlags);

        public const int AW_HOR_POSITIVE = 0x00000001;
        public const int AW_HOR_NEGATIVE = 0x00000002;
        public const int AW_VER_POSITIVE = 0x00000004;
        public const int AW_VER_NEGATIVE = 0x00000008;
        public const int AW_CENTER       = 0x00000010;
        public const int AW_HIDE         = 0x00010000;
        public const int AW_ACTIVATE     = 0x00020000;
        public const int AW_SLIDE        = 0x00040000;
        public const int AW_BLEND        = 0x00080000;

        /// <summary>
        /// 穿透 Windows 前台防抢焦点机制 (Focus Stealing Prevention)，强制将指定窗口置于桌面前台并获取输入焦点
        /// </summary>
        public static void ForceForegroundWindow(IntPtr hWnd)
        {
            if (hWnd == IntPtr.Zero) return;
            try
            {
                if (IsIconic(hWnd))
                {
                    ShowWindow(hWnd, SW_RESTORE);
                }
                else
                {
                    ShowWindow(hWnd, SW_SHOW);
                }

                IntPtr fgWnd = GetForegroundWindow();
                uint fgPid = 0;
                uint fgThreadId = GetWindowThreadProcessId(fgWnd, out fgPid);
                uint curThreadId = GetCurrentThreadId();

                if (fgThreadId != 0 && fgThreadId != curThreadId)
                {
                    AttachThreadInput(curThreadId, fgThreadId, true);
                    SetForegroundWindow(hWnd);
                    BringWindowToTop(hWnd);
                    AttachThreadInput(curThreadId, fgThreadId, false);
                }
                else
                {
                    SetForegroundWindow(hWnd);
                    BringWindowToTop(hWnd);
                }
            }
            catch { }
        }

        // ==================== Shell User Notification State ====================
        public const int QUNS_NOT_PRESENT = 1;
        public const int QUNS_BUSY = 2;
        public const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
        public const int QUNS_PRESENTATION_MODE = 4;
        public const int QUNS_ACCEPTS_NOTIFICATIONS = 5;
        public const int QUNS_QUIET_TIME = 6;
        public const int QUNS_APP = 7;

        [DllImport("shell32.dll")]
        public static extern int SHQueryUserNotificationState(out int pquns);

        // ==================== DWM Window Attributes ====================
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        public const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;

        [DllImport("dwmapi.dll")]
        public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);
    }

    #region ShellLink COM Interfaces
    [ComImport]
    [Guid("00021401-0000-0000-C000-000000000046")]
    [ClassInterface(ClassInterfaceType.None)]
    internal class ShellLink { }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("000214F9-0000-0000-C000-000000000046")]
    internal interface IShellLinkW
    {
        void GetPath([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszFile, int cchMaxPath, out IntPtr pfd, uint fFlags);
        void GetIDList(out IntPtr ppidl);
        void SetIDList(IntPtr pidl);
        void GetDescription([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszName, int cchMaxName);
        void SetDescription([MarshalAs(UnmanagedType.LPWStr)] string pszName);
        void GetWorkingDirectory([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszDir, int cchMaxPath);
        void SetWorkingDirectory([MarshalAs(UnmanagedType.LPWStr)] string pszDir);
        void GetArguments([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszArgs, int cchMaxPath);
        void SetArguments([MarshalAs(UnmanagedType.LPWStr)] string pszArgs);
        void GetHotkey(out short pwHotkey);
        void SetHotkey(short wHotkey);
        void GetShowCmd(out int piShowCmd);
        void SetShowCmd(int iShowCmd);
        void GetIconLocation([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder pszIconPath, int cchIconPath, out int piIcon);
        void SetIconLocation([MarshalAs(UnmanagedType.LPWStr)] string pszIconPath, int iIcon);
        void SetRelativePath([MarshalAs(UnmanagedType.LPWStr)] string pszPathRel, uint dwReserved);
        void Resolve(IntPtr hwnd, uint fFlags);
        void SetPath([MarshalAs(UnmanagedType.LPWStr)] string pszFile);
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("886D8EEB-8CF2-4446-8D02-CDBA1DBDCF99")]
    internal interface IPropertyStore
    {
        [PreserveSig] int GetCount(out uint cProps);
        [PreserveSig] int GetAt(uint iProp, out PROPERTYKEY pkey);
        [PreserveSig] int GetValue(ref PROPERTYKEY key, out PROPVARIANT pv);
        [PreserveSig] int SetValue(ref PROPERTYKEY key, ref PROPVARIANT pv);
        [PreserveSig] int Commit();
    }

    [ComImport]
    [InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    [Guid("0000010b-0000-0000-C000-000000000046")]
    internal interface IPersistFile
    {
        void GetClassID(out Guid pClassID);
        [PreserveSig] int IsDirty();
        void Load([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, uint dwMode);
        void Save([MarshalAs(UnmanagedType.LPWStr)] string pszFileName, [MarshalAs(UnmanagedType.Bool)] bool fRemember);
        void SaveCompleted([MarshalAs(UnmanagedType.LPWStr)] string pszFileName);
        void GetCurFile([Out, MarshalAs(UnmanagedType.LPWStr)] StringBuilder ppszFileName);
    }

    [StructLayout(LayoutKind.Sequential, Pack = 4)]
    internal struct PROPERTYKEY
    {
        public Guid fmtid;
        public uint pid;
    }

    [StructLayout(LayoutKind.Explicit, Size = 16)]
    internal struct PROPVARIANT
    {
        [FieldOffset(0)] public ushort vt;
        [FieldOffset(2)] public ushort wReserved1;
        [FieldOffset(4)] public ushort wReserved2;
        [FieldOffset(6)] public ushort wReserved3;
        [FieldOffset(8)] public IntPtr pwszVal;
    }
    #endregion
}
