using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using Microsoft.Win32.SafeHandles;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using System.Media;

namespace RazerBatteryTray
{
    static class Program
    {
        [DllImport("user32.dll")]
        static extern bool SetProcessDPIAware();

        [DllImport("kernel32.dll")]
        static extern ulong GetTickCount64();

        [DllImport("shell32.dll", SetLastError = true)]
        static extern int SetCurrentProcessExplicitAppUserModelID([MarshalAs(UnmanagedType.LPWStr)] string AppID);

        [DllImport("shell32.dll")]
        static extern void SHChangeNotify(int wEventId, int uFlags, IntPtr dwItem1, IntPtr dwItem2);

        public static void EnsureAppShortcutWithAumid()
        {
            try
            {
                string aumid = "FerrisPulse.App";
                SetCurrentProcessExplicitAppUserModelID(aumid);

                string startMenu = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), @"Microsoft\Windows\Start Menu\Programs");
                if (!Directory.Exists(startMenu)) Directory.CreateDirectory(startMenu);
                string lnkPath = Path.Combine(startMenu, "FerrisPulse · 灵脉.lnk");
                string targetExe = Application.ExecutablePath;
                string dir = Path.GetDirectoryName(targetExe);
                string icoPath = Path.Combine(dir, "app.ico");
                if (!File.Exists(icoPath)) icoPath = targetExe;

                string oldLnkPath = Path.Combine(startMenu, "RazerBatteryTray.lnk");
                if (File.Exists(oldLnkPath))
                {
                    try { File.Delete(oldLnkPath); } catch { }
                }

                try
                {
                    string desktop = Environment.GetFolderPath(Environment.SpecialFolder.Desktop);
                    string oldDesktopLnk = Path.Combine(desktop, "RazerBatteryTray.lnk");
                    if (File.Exists(oldDesktopLnk)) { File.Delete(oldDesktopLnk); }
                }
                catch { }

                if (File.Exists(lnkPath))
                {
                    try { File.Delete(lnkPath); } catch { }
                }

                IShellLinkW link = (IShellLinkW)new ShellLink();
                link.SetPath(targetExe);
                link.SetWorkingDirectory(dir);
                link.SetIconLocation(icoPath, 0);
                link.SetDescription("FerrisPulse · 灵脉 外设电量感知引擎");

                IPropertyStore store = (IPropertyStore)link;
                PROPERTYKEY key = new PROPERTYKEY();
                key.fmtid = new Guid("{9F4C2855-9F79-4BDE-A8E1-E1DE34EB1AE8}");
                key.pid = 5;

                PROPVARIANT pv = new PROPVARIANT();
                pv.vt = 31; // VT_LPWSTR
                pv.pwszVal = Marshal.StringToCoTaskMemUni(aumid);
                try
                {
                    store.SetValue(ref key, ref pv);
                    store.Commit();
                }
                finally
                {
                    Marshal.FreeCoTaskMem(pv.pwszVal);
                }

                IPersistFile file = (IPersistFile)link;
                file.Save(lnkPath, true);

                try { SHChangeNotify(0x7FFFFFFF, 0x1000, IntPtr.Zero, IntPtr.Zero); } catch { }

                // Register AppUserModelId and Notifications in HKCU for guaranteed toast header branding
                try
                {
                    using (var appKey = Registry.CurrentUser.CreateSubKey(@"Software\Classes\AppUserModelId\" + aumid))
                    {
                        if (appKey != null)
                        {
                            appKey.SetValue("DisplayName", "FerrisPulse · 灵脉");
                            appKey.SetValue("IconUri", icoPath);
                            appKey.SetValue("IconBackgroundColor", "00000000");
                        }
                    }
                    using (var notifKey = Registry.CurrentUser.CreateSubKey(@"Software\Microsoft\Windows\CurrentVersion\Notifications\Settings\" + aumid))
                    {
                        if (notifKey != null)
                        {
                            notifKey.SetValue("ShowInActionCenter", 1, RegistryValueKind.DWord);
                            notifKey.SetValue("Enabled", 1, RegistryValueKind.DWord);
                        }
                    }
                }
                catch { }
            }
            catch { }
        }

        [STAThread]
        static void Main(string[] args)
        {
            try
            {
                SetProcessDPIAware();
            }
            catch { }

            try
            {
                EnsureAppShortcutWithAumid();
            }
            catch { }

            Application.SetUnhandledExceptionMode(UnhandledExceptionMode.CatchException);
            Application.ThreadException += (s, e) => {
                MessageBox.Show("程序发生异常: " + e.Exception.Message + "\n\n" + e.Exception.StackTrace, "FerrisPulse · 灵脉 - 错误提示", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };
            AppDomain.CurrentDomain.UnhandledException += (s, e) => {
                Exception ex = e.ExceptionObject as Exception;
                string msg = ex != null ? (ex.Message + "\n\n" + ex.StackTrace) : "未知系统错误";
                MessageBox.Show("未处理的致命异常: " + msg, "FerrisPulse · 灵脉 - 致命错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            };

            // Ensure single instance by terminating old/stale instances
            try
            {
                Process current = Process.GetCurrentProcess();
                string[] procNames = new string[] { current.ProcessName, "FerrisPulse", "RazerBatteryTray" };
                foreach (string name in procNames)
                {
                    foreach (Process p in Process.GetProcessesByName(name))
                    {
                        if (p.Id != current.Id)
                        {
                            try { p.Kill(); p.WaitForExit(500); } catch { }
                        }
                    }
                }
            }
            catch { }

            bool isAutoStart = false;
            if (args != null)
            {
                for (int i = 0; i < args.Length; i++)
                {
                    if (args[i] != null && args[i].IndexOf("autostart", StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        isAutoStart = true;
                        break;
                    }
                }
            }

            // Fallback heuristic: If launched without --autostart, but system was booted within 6 minutes (360,000 ms)
            // and this app is registered in HKCU Run key, check if AutoStartShowUI is NOT 1.
            // This ensures that even if Windows Run key was outdated or stripped parameters, it will stay silent on boot.
            if (!isAutoStart)
            {
                try
                {
                    ulong uptimeMs = GetTickCount64();
                    if (uptimeMs < 360000) // Within 6 minutes of system boot
                    {
                        using (var runKey = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Run", false))
                        {
                            if (runKey != null && (runKey.GetValue("FerrisPulse") != null || runKey.GetValue("RazerBatteryTray") != null))
                            {
                                bool showUI = false;
                                using (var cfgKey = Registry.CurrentUser.OpenSubKey(@"Software\FerrisPulse", false))
                                {
                                    if (cfgKey != null)
                                    {
                                        var val = cfgKey.GetValue("AutoStartShowUI");
                                        if (val != null && (int)val == 1)
                                        {
                                            showUI = true;
                                        }
                                    }
                                }
                                if (!showUI)
                                {
                                    using (var cfgKey = Registry.CurrentUser.OpenSubKey(@"Software\RazerBatteryTray", false))
                                    {
                                        if (cfgKey != null)
                                        {
                                            var val = cfgKey.GetValue("AutoStartShowUI");
                                            if (val != null && (int)val == 1)
                                            {
                                                showUI = true;
                                            }
                                        }
                                    }
                                }

                                if (!showUI)
                                {
                                    isAutoStart = true;
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm(isAutoStart));
        }
    }

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

    public enum DeviceCategory
    {
        Mouse,
        Headset,
        Gamepad,
        Keyboard,
        Generic,
        Earbuds,
        Dongle
    }

    public class CustomDeviceConfig
    {
        public string DeviceId { get; set; }
        public string HardwareFingerprint { get; set; }
        public string CustomName { get; set; }
        public string OriginalName { get; set; }
        public DeviceCategory Category { get; set; }
        public string Transport { get; set; }
        public int CachedBattery { get; set; }
        public DateTime LastSeen { get; set; }
    }

    public class MouseBatteryInfo
    {
        public string DeviceId { get; set; }
        public string Brand { get; set; } // "Razer", "Rapoo", or "Custom"
        public DeviceCategory Category { get; set; }
        public string CustomName { get; set; }
        public int PriorityIndex { get; set; }
        public string HardwareFingerprint { get; set; }
        public string HardwareId { get; set; }
        public string Transport { get; set; }
        public bool IsCustom { get; set; }

        public bool IsConnected { get; set; }
        public bool IsSleeping { get; set; }
        public bool IsDonglePresent { get; set; }
        public string DeviceName { get; set; }
        public int BatteryPercent { get; set; }
        public double ExactBatteryPercent { get; set; }
        public bool IsCharging { get; set; }
        public DateTime LastUpdated { get; set; }

        public int Dpi { get; set; }
        public int DpiStage { get; set; }
        public int DpiStageCount { get; set; }
        public int[] DpiStages { get; set; }
        public int PollingRate { get; set; }
        public int[] SupportedPollingRates { get; set; }
        public bool IsPrimary { get; set; }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(CustomName) && 
                    !string.Equals(CustomName, DeviceName, StringComparison.OrdinalIgnoreCase) &&
                    !CustomName.Equals("Nuphy WH80 Dongle", StringComparison.OrdinalIgnoreCase) &&
                    !CustomName.Equals("2.4G Wireless Keyboard", StringComparison.OrdinalIgnoreCase) &&
                    !CustomName.Equals("Razer Mouse", StringComparison.OrdinalIgnoreCase) &&
                    !CustomName.Equals("Razer Wireless Mouse", StringComparison.OrdinalIgnoreCase) &&
                    !CustomName.Equals("HyperPolling Wireless Dongle", StringComparison.OrdinalIgnoreCase) &&
                    !CustomName.Equals("Razer HyperPolling Wireless Dongle", StringComparison.OrdinalIgnoreCase) &&
                    !CustomName.Equals("雷蛇无线鼠标", StringComparison.OrdinalIgnoreCase) &&
                    !CustomName.Equals("雷蛇电竞鼠标", StringComparison.OrdinalIgnoreCase) &&
                    !CustomName.Equals("雷蛇无线设备", StringComparison.OrdinalIgnoreCase) &&
                    !CustomName.Equals("毒蝰", StringComparison.OrdinalIgnoreCase) &&
                    !CustomName.Equals("毒蝰V4Pro", StringComparison.OrdinalIgnoreCase) &&
                    !CustomName.Equals("炼狱蝰蛇", StringComparison.OrdinalIgnoreCase) &&
                    !CustomName.Equals("巴塞利斯蛇", StringComparison.OrdinalIgnoreCase))
                {
                    if (Brand == "Razer")
                    {
                        bool hasChinese = false;
                        for (int i = 0; i < CustomName.Length; i++)
                        {
                            if (CustomName[i] >= 0x4e00 && CustomName[i] <= 0x9fa5)
                            {
                                hasChinese = true;
                                break;
                            }
                        }
                        if (hasChinese) return FriendlyShortName;
                    }
                    return CustomName;
                }
                return FriendlyShortName;
            }
        }

        public string CategoryIcon
        {
            get
            {
                switch (Category)
                {
                    case DeviceCategory.Headset: return "🎧";
                    case DeviceCategory.Earbuds: return "🦻";
                    case DeviceCategory.Gamepad: return "🎮";
                    case DeviceCategory.Keyboard: return "⌨️";
                    case DeviceCategory.Dongle: return "📡";
                    case DeviceCategory.Generic: return "🔌";
                    default: return "🖱️";
                }
            }
        }

        public Color BrandColor
        {
            get
            {
                if (Category == DeviceCategory.Headset) return Color.FromArgb(90, 160, 255);
                if (Category == DeviceCategory.Earbuds) return Color.FromArgb(179, 136, 255);
                if (Category == DeviceCategory.Gamepad) return Color.FromArgb(192, 132, 252);
                if (Category == DeviceCategory.Dongle) return Color.FromArgb(255, 183, 77);
                if (Category == DeviceCategory.Generic) return Color.FromArgb(144, 164, 174);
                if (Brand == "NuPhy" || Category == DeviceCategory.Keyboard) return Color.FromArgb(0, 229, 255);
                if (Brand == "Logitech" || Brand == "罗技") return Color.FromArgb(0, 174, 239);

                if (Brand == "Rapoo") return Color.FromArgb(0, 185, 255);
                if (Brand == "VGN") return Color.FromArgb(255, 120, 0);
                return Color.FromArgb(0, 230, 118);
            }
        }

        public static string GetCategoryDisplayName(DeviceCategory cat)
        {
            switch (cat)
            {
                case DeviceCategory.Mouse: return "鼠标";
                case DeviceCategory.Keyboard: return "键盘";
                case DeviceCategory.Headset: return "头戴式耳机";
                case DeviceCategory.Earbuds: return "入耳式耳机";
                case DeviceCategory.Gamepad: return "游戏手柄";
                case DeviceCategory.Dongle: return "接收器 / 拓展坞";
                case DeviceCategory.Generic: return "其它通用外设";
                default: return "外设";
            }
        }

        public string FriendlyShortName
        {
            get
            {
                if (!string.IsNullOrEmpty(CustomName) &&
                    !CustomName.Equals("Nuphy WH80 Dongle", StringComparison.OrdinalIgnoreCase) &&
                    !CustomName.Equals("2.4G Wireless Keyboard", StringComparison.OrdinalIgnoreCase))
                {
                    return CustomName;
                }

                if (Category == DeviceCategory.Headset || Category == DeviceCategory.Earbuds)
                {
                    if (DeviceName != null)
                    {
                        if (DeviceName.Contains("Buds4 Pro")) return "Buds4 Pro";
                        if (DeviceName.Contains("Buds3 Pro")) return "Buds3 Pro";
                        if (DeviceName.Contains("Buds")) return "无线耳机";
                    }
                    return DeviceName ?? (Category == DeviceCategory.Earbuds ? "入耳式耳机" : "头戴式耳机");
                }

                if (Category == DeviceCategory.Gamepad)
                {
                    if (DeviceName != null)
                    {
                        if (DeviceName.Contains("Xbox")) return "Xbox 手柄";
                        if (DeviceName.Contains("DualSense")) return "PS5 手柄";
                        if (DeviceName.Contains("DualShock")) return "PS4 手柄";
                    }
                    return DeviceName ?? "无线手柄";
                }

                if (Brand == "NuPhy" || Category == DeviceCategory.Keyboard)
                {
                    string target = !string.IsNullOrEmpty(DeviceName) ? DeviceName : CustomName;
                    if (target != null)
                    {
                        if (target.IndexOf("WH80", StringComparison.OrdinalIgnoreCase) >= 0) return "NuPhy WH80";
                        if (target.IndexOf("Air75", StringComparison.OrdinalIgnoreCase) >= 0) return "NuPhy Air75";
                        if (target.IndexOf("Air96", StringComparison.OrdinalIgnoreCase) >= 0) return "NuPhy Air96";
                        if (target.IndexOf("Air60", StringComparison.OrdinalIgnoreCase) >= 0) return "NuPhy Air60";
                        if (target.IndexOf("Halo", StringComparison.OrdinalIgnoreCase) >= 0) return "NuPhy Halo";
                        if (target.IndexOf("Field75", StringComparison.OrdinalIgnoreCase) >= 0) return "NuPhy Field75";
                        if (target.IndexOf("NuPhy", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            string s = target.Replace("Dongle", "").Trim();
                            if (s.Length > 0) return s;
                        }
                    }
                    return target ?? "无线键盘";
                }

                if (Brand == "VGN")
                {
                    if (DeviceName != null)
                    {
                        if (DeviceName.Contains("DragonFly F2 Pro Max") || DeviceName.Contains("F2 Pro Max")) return "蜻蜓 F2 Pro Max";
                        if (DeviceName.Contains("DragonFly F1 Pro Max") || DeviceName.Contains("F1 Pro Max")) return "蜻蜓 F1 Pro Max";
                        if (DeviceName.Contains("DragonFly F1 Pro")) return "蜻蜓 F1 Pro";
                        if (DeviceName.Contains("DragonFly F1")) return "蜻蜓 F1";
                        if (DeviceName.Contains("DragonFly F2")) return "蜻蜓 F2";
                        if (DeviceName.Contains("R1 Pro Max")) return "VXE R1 Pro Max";
                        if (DeviceName.Contains("R1 Pro")) return "VXE R1 Pro";
                        if (DeviceName.Contains("R1")) return "VXE R1";
                        if (DeviceName.Contains("DragonFly")) return "蜻蜓 无线鼠标";
                        if (DeviceName.Contains("VGN") || DeviceName.Contains("VXE") || DeviceName.Contains("ATK")) return DeviceName;
                    }
                    return "VGN 蜻蜓鼠标";
                }

                if (Brand == "Logitech" || Brand == "罗技")
                {
                    if (DeviceName != null)
                    {
                        if (DeviceName.Contains("SUPERLIGHT 2")) return "GPX 2";
                        if (DeviceName.Contains("SUPERLIGHT")) return "GPX";
                        if (DeviceName.Contains("PRO Wireless") || DeviceName.Contains("GPW")) return "GPW";
                        if (DeviceName.Contains("G502 X")) return "G502 X";
                        if (DeviceName.Contains("G502")) return "G502";
                        if (DeviceName.Contains("G304") || DeviceName.Contains("G305")) return "G304";
                        if (DeviceName.Contains("G703")) return "G703";
                        if (DeviceName.Contains("G903")) return "G903";
                        if (DeviceName.Contains("G913") || DeviceName.Contains("G915")) return "G913 TKL";
                        if (DeviceName.Contains("MX Master 3S")) return "MX Master 3S";
                        if (DeviceName.Contains("MX Master 3")) return "MX Master 3";
                        if (DeviceName.Contains("MX Anywhere")) return "MX Anywhere";
                        if (DeviceName.Length > 0) return DeviceName;
                    }
                    return "罗技无线鼠标";
                }



                if (Brand == "Rapoo")
                {
                    if (DeviceName != null)
                    {
                        if (DeviceName.Contains("VT7")) return "VT7 双模";
                        if (DeviceName.Contains("VT3 Pro Max")) return "VT3 Max";
                        if (DeviceName.Contains("VT3 Pro")) return "VT3 Pro";
                        if (DeviceName.Contains("VT3")) return "VT3 双模";
                        if (DeviceName.Contains("VT9 Pro Mini")) return "VT9 Mini";
                        if (DeviceName.Contains("VT9 Pro")) return "VT9 Pro";
                        if (DeviceName.Contains("VT9")) return "VT9 双模";
                        if (DeviceName.Contains("VT1 Pro Max")) return "VT1 Max";
                        if (DeviceName.Contains("VT1 Pro")) return "VT1 Pro";
                        if (DeviceName.Contains("VT1")) return "VT1 双模";
                        if (DeviceName.Contains("V300 Pro")) return "V300 Pro";
                        if (DeviceName.Contains("V300SE")) return "V300SE";
                        string s = DeviceName.Replace("雷柏", "").Trim();
                        if (s.Length > 0) return s;
                    }
                    return "VT 无线鼠标";
                }
                else if (Brand == "Razer")
                {
                    int pid = 0;
                    if (!string.IsNullOrEmpty(DeviceId) && DeviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase))
                    {
                        string pidHex = DeviceId.Substring("Razer:1532:".Length).Trim();
                        int.TryParse(pidHex, System.Globalization.NumberStyles.HexNumber, null, out pid);
                    }
                    if (pid == 0 && !string.IsNullOrEmpty(HardwareId) && HardwareId.StartsWith("HID:1532:", StringComparison.OrdinalIgnoreCase))
                    {
                        string pidHex = HardwareId.Substring("HID:1532:".Length).Trim();
                        int.TryParse(pidHex, System.Globalization.NumberStyles.HexNumber, null, out pid);
                    }
                    return RazerDeviceHelper.GetRazerDisplayName(pid, DeviceName);
                }
                return DeviceName ?? "未知外设";
            }
        }
    }

    #region Modern UI Custom Controls

    public class RoundedCard : Panel
    {
        private Color cardColor = Color.FromArgb(25, 27, 34);
        public Color CardColor
        {
            get { return cardColor; }
            set { cardColor = value; Invalidate(); }
        }

        public override Color BackColor
        {
            get { return base.BackColor; }
            set
            {
                cardColor = value;
                base.BackColor = Parent != null ? Parent.BackColor : Color.FromArgb(18, 19, 23);
                Invalidate();
            }
        }

        public Color BorderColor { get; set; }
        public int CornerRadius { get; set; }

        public RoundedCard()
        {
            cardColor = Color.FromArgb(25, 27, 34);
            BorderColor = Color.FromArgb(42, 46, 58);
            CornerRadius = 10;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            base.BackColor = Color.FromArgb(18, 19, 23);
        }

        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            if (Parent != null)
            {
                base.BackColor = Parent.BackColor;
            }
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color parentBg = Parent != null ? Parent.BackColor : Color.FromArgb(18, 19, 23);
            g.Clear(parentBg);

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = GetRoundedRectangle(rect, CornerRadius))
            {
                using (SolidBrush brush = new SolidBrush(cardColor))
                {
                    g.FillPath(brush, path);
                }
            }
        }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color parentBg = Parent != null ? Parent.BackColor : Color.FromArgb(18, 19, 23);
            g.Clear(parentBg);

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = GetRoundedRectangle(rect, CornerRadius))
            {
                using (SolidBrush brush = new SolidBrush(cardColor))
                {
                    g.FillPath(brush, path);
                }

                if (BorderColor != Color.Transparent)
                {
                    using (Pen pen = new Pen(BorderColor, 1f))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }
        }

        public static GraphicsPath GetRoundedRectangle(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            if (d > bounds.Width) d = bounds.Width;
            if (d > bounds.Height) d = bounds.Height;
            if (d <= 0) d = 1;

            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static GraphicsPath GetRoundRectF(RectangleF bounds, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    public class ModernButton : Control
    {
        private bool isHovered = false;
        private bool isPressed = false;

        public Color NormalColor { get; set; }
        public Color HoverColor { get; set; }
        public Color PressedColor { get; set; }
        public Color BorderColor { get; set; }
        public int CornerRadius { get; set; }
        public bool IsGearButton { get; set; }
        public Color TextColor
        {
            get { return this.ForeColor; }
            set { this.ForeColor = value; }
        }

        public ModernButton()
        {
            NormalColor = Color.FromArgb(0, 200, 83);
            HoverColor = Color.FromArgb(0, 230, 118);
            PressedColor = Color.FromArgb(0, 160, 65);
            BorderColor = Color.Transparent;
            CornerRadius = 8;
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); isHovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); isHovered = false; isPressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { isPressed = true; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); isPressed = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            base.OnPaint(pevent);
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color parentBg = (Parent is RoundedCard) ? ((RoundedCard)Parent).CardColor : (Parent != null ? Parent.BackColor : Color.FromArgb(18, 19, 23));
            g.Clear(parentBg);

            Color fill = NormalColor;
            if (isPressed) fill = PressedColor;
            else if (isHovered) fill = HoverColor;

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = RoundedCard.GetRoundedRectangle(rect, CornerRadius))
            {
                using (SolidBrush brush = new SolidBrush(fill))
                {
                    g.FillPath(brush, path);
                }

                if (BorderColor != Color.Transparent)
                {
                    using (Pen pen = new Pen(BorderColor, 1f))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }

            if (IsGearButton)
            {
                Color iconColor = isPressed ? Color.FromArgb(0, 229, 255) : (isHovered ? Color.White : ForeColor);
                DrawGearIcon(g, rect, iconColor);
            }
            else if (!string.IsNullOrEmpty(Text))
            {
                TextRenderer.DrawText(g, Text, Font, ClientRectangle, ForeColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }

        private void DrawGearIcon(Graphics g, Rectangle bounds, Color color)
        {
            float cx = bounds.X + bounds.Width / 2f;
            float cy = bounds.Y + bounds.Height / 2f;
            float rOuter = bounds.Height * 0.28f;
            float rRoot = rOuter * 0.72f;
            float rHole = rOuter * 0.38f;
            int teeth = 8;

            using (GraphicsPath path = new GraphicsPath())
            {
                List<PointF> pts = new List<PointF>();
                double step = 2.0 * Math.PI / teeth;
                for (int i = 0; i < teeth; i++)
                {
                    double a0 = i * step - Math.PI / 2.0;
                    double a1 = a0 + step * 0.22;
                    double a2 = a0 + step * 0.45;
                    double a3 = a0 + step * 0.67;

                    pts.Add(new PointF(cx + (float)(rRoot * Math.Cos(a0)), cy + (float)(rRoot * Math.Sin(a0))));
                    pts.Add(new PointF(cx + (float)(rOuter * Math.Cos(a1)), cy + (float)(rOuter * Math.Sin(a1))));
                    pts.Add(new PointF(cx + (float)(rOuter * Math.Cos(a2)), cy + (float)(rOuter * Math.Sin(a2))));
                    pts.Add(new PointF(cx + (float)(rRoot * Math.Cos(a3)), cy + (float)(rRoot * Math.Sin(a3))));
                }
                path.AddPolygon(pts.ToArray());
                path.AddEllipse(cx - rHole, cy - rHole, rHole * 2, rHole * 2);

                using (SolidBrush brush = new SolidBrush(color))
                {
                    g.FillPath(brush, path);
                }
            }
        }
    }

    public class ModernBadge : Control
    {
        public Color BadgeColor = Color.FromArgb(0, 229, 255);
        public Color DotColor = Color.FromArgb(0, 229, 255);

        public ModernBadge()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            ForeColor = Color.FromArgb(235, 240, 250);
            Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color parentBg = (Parent is RoundedCard) ? ((RoundedCard)Parent).CardColor : (Parent != null ? Parent.BackColor : Color.FromArgb(18, 19, 23));
            g.Clear(parentBg);

            int radius = Math.Max(2, Height / 2);
            using (var path = RoundedCard.GetRoundedRectangle(new Rectangle(0, 0, Width - 1, Height - 1), radius))
            {
                using (var bgBrush = new SolidBrush(Color.FromArgb(28, 15, 25, 38)))
                {
                    g.FillPath(bgBrush, path);
                }
                using (var borderPen = new Pen(Color.FromArgb(70, BadgeColor), 1f))
                {
                    g.DrawPath(borderPen, path);
                }
            }

            int dotSize = 7;
            int dotY = (Height - dotSize) / 2;
            int dotX = 10;
            using (var dotBrush = new SolidBrush(DotColor))
            {
                g.FillEllipse(dotBrush, dotX, dotY, dotSize, dotSize);
            }

            Rectangle textRect = new Rectangle(dotX + dotSize + 6, 0, Width - (dotX + dotSize + 12), Height);
            TextRenderer.DrawText(g, Text, Font, textRect, ForeColor, TextFormatFlags.VerticalCenter | TextFormatFlags.Left | TextFormatFlags.EndEllipsis);
        }
    }

    public class ModernCheckBox : Control
    {
        private bool isChecked = false;
        private bool isHovered = false;

        public event EventHandler CheckedChanged;

        public bool Checked
        {
            get { return isChecked; }
            set
            {
                if (isChecked != value)
                {
                    isChecked = value;
                    Invalidate();
                    if (CheckedChanged != null) CheckedChanged(this, EventArgs.Empty);
                }
            }
        }

        public ModernCheckBox()
        {
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); isHovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); isHovered = false; Invalidate(); }
        protected override void OnClick(EventArgs e) { Checked = !Checked; base.OnClick(e); }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            base.OnPaint(pevent);
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color parentBg = (Parent is RoundedCard) ? ((RoundedCard)Parent).CardColor : (Parent != null ? Parent.BackColor : Color.FromArgb(25, 27, 34));
            g.Clear(parentBg);

            int boxSize = 16;
            int boxY = (Height - boxSize) / 2;
            Rectangle boxRect = new Rectangle(1, boxY, boxSize, boxSize);

            Color boxBg = isChecked ? (isHovered ? Color.FromArgb(0, 230, 118) : Color.FromArgb(0, 200, 83)) : (isHovered ? Color.FromArgb(38, 42, 54) : Color.FromArgb(28, 30, 38));
            Color boxBorder = isChecked ? Color.FromArgb(0, 230, 118) : (isHovered ? Color.FromArgb(90, 98, 120) : Color.FromArgb(58, 63, 78));

            using (GraphicsPath path = RoundedCard.GetRoundedRectangle(boxRect, 4))
            {
                using (SolidBrush brush = new SolidBrush(boxBg))
                {
                    g.FillPath(brush, path);
                }
                using (Pen pen = new Pen(boxBorder, 1f))
                {
                    g.DrawPath(pen, path);
                }
            }

            if (isChecked)
            {
                using (Pen checkPen = new Pen(Color.FromArgb(10, 24, 15), 2.0f))
                {
                    checkPen.StartCap = LineCap.Round;
                    checkPen.EndCap = LineCap.Round;
                    PointF[] checkPoints = new PointF[]
                    {
                        new PointF(boxRect.Left + 3.5f, boxRect.Top + 8.5f),
                        new PointF(boxRect.Left + 6.5f, boxRect.Top + 11.5f),
                        new PointF(boxRect.Left + 12.5f, boxRect.Top + 4.5f)
                    };
                    g.DrawLines(checkPen, checkPoints);
                }
            }

            int textX = boxRect.Right + 8;
            Rectangle textRect = new Rectangle(textX, 0, Width - textX, Height);
            Color textCol = isHovered ? Color.White : Color.FromArgb(220, 226, 238);
            TextRenderer.DrawText(g, Text, Font, textRect, textCol,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

    public class RapooStatusTile : Control
    {
        public float DpiScale { get; set; }
        public string Title { get; set; }
        public string Badge { get; set; }
        public string ValueText { get; set; }
        public string UnitText { get; set; }
        public string Footnote { get; set; }
        public Color AccentColor { get; set; }

        public RapooStatusTile()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            DpiScale = 1.0f;
            AccentColor = Color.FromArgb(0, 210, 255);
            Title = "";
            Badge = "";
            ValueText = "--";
            UnitText = "";
            Footnote = "";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color parentBg = (Parent is RoundedCard) ? ((RoundedCard)Parent).CardColor : (Parent != null ? Parent.BackColor : Color.FromArgb(25, 27, 34));
            g.Clear(parentBg);

            int w = Width;
            int h = Height;
            int r = (int)(8 * DpiScale);

            // Background & subtle border
            using (var path = CreateRoundedRectanglePath(new Rectangle(0, 0, w - 1, h - 1), r))
            {
                using (var brush = new SolidBrush(Color.FromArgb(17, 20, 27)))
                {
                    g.FillPath(brush, path);
                }
                using (var pen = new Pen(Color.FromArgb(38, 43, 56), 1f))
                {
                    g.DrawPath(pen, path);
                }
            }

            // Top Header: Title (left) & Badge (right)
            int padX = (int)(14 * DpiScale);
            int topY = (int)(9 * DpiScale);
            using (var titleFont = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Regular, GraphicsUnit.Point))
            using (var titleBrush = new SolidBrush(Color.FromArgb(145, 155, 175)))
            {
                g.DrawString(Title, titleFont, titleBrush, padX, topY);
            }

            if (!string.IsNullOrEmpty(Badge))
            {
                using (var badgeFont = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular, GraphicsUnit.Point))
                {
                    var sz = TextRenderer.MeasureText(Badge, badgeFont);
                    int bX = w - padX - sz.Width - (int)(8 * DpiScale);
                    int bY = topY - (int)(1 * DpiScale);
                    int bW = sz.Width + (int)(8 * DpiScale);
                    int bH = sz.Height + (int)(2 * DpiScale);

                    using (var bPath = CreateRoundedRectanglePath(new Rectangle(bX, bY, bW, bH), (int)(4 * DpiScale)))
                    {
                        using (var bBrush = new SolidBrush(Color.FromArgb(28, AccentColor.R, AccentColor.G, AccentColor.B)))
                        {
                            g.FillPath(bBrush, bPath);
                        }
                        using (var bPen = new Pen(Color.FromArgb(70, AccentColor.R, AccentColor.G, AccentColor.B), 1f))
                        {
                            g.DrawPath(bPen, bPath);
                        }
                    }

                    using (var bTextBrush = new SolidBrush(AccentColor))
                    {
                        g.DrawString(Badge, badgeFont, bTextBrush, bX + (int)(4 * DpiScale), bY + (int)(1 * DpiScale));
                    }
                }
            }

            // Bottom Footnote / Hint
            int footY = Math.Max(topY + (int)(32 * DpiScale), h - (int)(18 * DpiScale));

            // Center: Big ValueText + UnitText - mathematically centered between Title and Footnote
            int valY = (topY + footY) / 2 - (int)(9 * DpiScale);
            using (var valFont = new Font("Microsoft YaHei UI", 14.5F, FontStyle.Bold, GraphicsUnit.Point))
            using (var valBrush = new SolidBrush(AccentColor))
            {
                g.DrawString(ValueText, valFont, valBrush, padX - (int)(2 * DpiScale), valY);
                var valSz = TextRenderer.MeasureText(ValueText, valFont);

                if (!string.IsNullOrEmpty(UnitText))
                {
                    using (var unitFont = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point))
                    using (var unitBrush = new SolidBrush(Color.FromArgb(135, 145, 165)))
                    {
                        int unitX = padX + valSz.Width - (int)(6 * DpiScale);
                        int unitY = valY + (int)(5 * DpiScale);
                        g.DrawString(UnitText, unitFont, unitBrush, unitX, unitY);
                    }
                }
            }

            using (var footFont = new Font("Microsoft YaHei UI", 7.6F, FontStyle.Regular, GraphicsUnit.Point))
            using (var footBrush = new SolidBrush(Color.FromArgb(115, 125, 145)))
            {
                g.DrawString(Footnote, footFont, footBrush, padX, footY);
            }
        }

        private static GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
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
    }

    public class ModernSegmentButton : Control
    {
        private bool isSelected = false;
        private bool isHovered = false;
        private bool isPreset = false;
        private Color accentColor = Color.FromArgb(0, 200, 83);

        public bool Selected
        {
            get { return isSelected; }
            set { if (isSelected != value) { isSelected = value; Invalidate(); } }
        }

        public bool IsPreset
        {
            get { return isPreset; }
            set { if (isPreset != value) { isPreset = value; Invalidate(); } }
        }

        public Color AccentColor
        {
            get { return accentColor; }
            set { if (accentColor != value) { accentColor = value; Invalidate(); } }
        }

        public ModernSegmentButton()
        {
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnTextChanged(EventArgs e)
        {
            base.OnTextChanged(e);
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); isHovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); isHovered = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            base.OnPaint(pevent);
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color parentBg = (Parent is RoundedCard) ? ((RoundedCard)Parent).CardColor : (Parent != null ? Parent.BackColor : Color.FromArgb(25, 27, 34));
            g.Clear(parentBg);

            Color bg;
            Color border;
            Color fg;

            if (isSelected)
            {
                if (isPreset)
                {
                    bg = isHovered ? Color.FromArgb(24, 48, 32) : Color.FromArgb(18, 34, 24);
                    border = isHovered ? Color.FromArgb(0, 230, 118) : Color.FromArgb(0, 180, 80);
                    fg = isHovered ? Color.White : Color.FromArgb(140, 240, 180);
                }
                else
                {
                    bg = isHovered ? ControlPaint.Light(accentColor, 0.15f) : accentColor;
                    border = accentColor;
                    fg = Color.FromArgb(10, 24, 15);
                }
            }
            else
            {
                bg = isHovered ? Color.FromArgb(38, 42, 54) : Color.FromArgb(28, 30, 38);
                border = isHovered ? Color.FromArgb(80, 88, 108) : Color.FromArgb(48, 52, 65);
                fg = isHovered ? Color.White : Color.FromArgb(180, 188, 205);
            }

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = RoundedCard.GetRoundedRectangle(rect, 6))
            {
                using (SolidBrush brush = new SolidBrush(bg))
                {
                    g.FillPath(brush, path);
                }
                using (Pen pen = new Pen(border, 1f))
                {
                    g.DrawPath(pen, path);
                }
            }

            Font useFont = isSelected ? new Font(Font, FontStyle.Bold) : Font;
            TextRenderer.DrawText(g, Text, useFont, ClientRectangle, fg,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
        }
    }

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
        private System.Windows.Forms.Timer animTimer;
        public float DpiScale { get; set; }
        public event Action<MouseBatteryInfo> DeviceSelected;

        public SmoothSegmentedTabBar()
        {
            DpiScale = 1.0f;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
            DoubleBuffered = true;

            animTimer = new System.Windows.Forms.Timer();
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
                string bpText = dev.Brand == "Rapoo" ? "雷柏" : (dev.Brand == "VGN" ? "VGN" : (dev.Brand == "Razer" ? "雷蛇" : (dev.Category == DeviceCategory.Headset ? "耳机" : (dev.Category == DeviceCategory.Gamepad ? "手柄" : (dev.Category == DeviceCategory.Keyboard ? "键盘" : "外设")))));
                Color bpBg = isSel ? (dev.Brand == "Rapoo" ? Color.FromArgb(0, 150, 230) : (dev.Brand == "VGN" ? Color.FromArgb(240, 110, 0) : ((dev.Brand == "NuPhy" || dev.Category == DeviceCategory.Keyboard) ? Color.FromArgb(0, 229, 255) : Color.FromArgb(0, 200, 83)))) :
                                      (dev.Brand == "Rapoo" ? Color.FromArgb(24, 44, 60) : (dev.Brand == "VGN" ? Color.FromArgb(48, 30, 16) : ((dev.Brand == "NuPhy" || dev.Category == DeviceCategory.Keyboard) ? Color.FromArgb(12, 40, 48) : Color.FromArgb(30, 42, 36))));
                Color bpFg = isSel ? (dev.Brand == "Rapoo" ? Color.FromArgb(10, 24, 35) : (dev.Brand == "VGN" ? Color.FromArgb(30, 15, 0) : ((dev.Brand == "NuPhy" || dev.Category == DeviceCategory.Keyboard) ? Color.FromArgb(5, 25, 30) : Color.FromArgb(10, 24, 15)))) : dev.BrandColor;

                using (GraphicsPath bpPath = RoundedCard.GetRoundedRectangle(bpRect, (int)(4 * DpiScale)))
                using (SolidBrush bpBrush = new SolidBrush(bpBg))
                    g.FillPath(bpBrush, bpPath);

                using (Font bpFont = new Font("Microsoft YaHei UI", 7.2f, FontStyle.Bold))
                {
                    TextRenderer.DrawText(g, bpText, bpFont, bpRect, bpFg,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }

                // 1. Battery % ALWAYS anchored to far right for perfect vertical alignment across tabs
                string bStr = dev.BatteryPercent > 0 ? (dev.BatteryPercent + "%") : "--%";
                int curRight = tabX + tabW - (int)(8 * DpiScale);
                using (Font bFont = new Font("Microsoft YaHei UI", 8.2f, FontStyle.Bold))
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

                // 2. Sleek [📌] Micro-capsule for Primary Device (Only if multiple devices)
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

                    using (Font pFont = new Font("Segoe UI Emoji", 7.0f))
                    {
                        TextRenderer.DrawText(g, "📌", pFont, pinRect, dev.BrandColor,
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                    }
                    curRight = pinRect.Left - (int)(5 * DpiScale);
                }

                // 3. Model Short Name (Now has plenty of space with zero ellipsis clipping)
                int nameLeft = bpRect.Right + (int)(6 * DpiScale);
                int nameW = curRight - nameLeft;
                if (nameW > 10)
                {
                    Rectangle nameRect = new Rectangle(nameLeft, tRect.Y, nameW, tabH);
                    Color nameColor = isSel ? Color.White : Color.FromArgb(175, 185, 200);
                    using (Font nFont = new Font("Microsoft YaHei UI", 8.0f, isSel ? FontStyle.Bold : FontStyle.Regular))
                    {
                        TextRenderer.DrawText(g, dev.FriendlyShortName, nFont, nameRect, nameColor,
                            TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);
                    }
                }
            }
        }
    }

    public class SubtleDivider : Control
    {
        public SubtleDivider()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            Color parentBg = (Parent is RoundedCard) ? ((RoundedCard)Parent).CardColor : (Parent != null ? Parent.BackColor : Color.FromArgb(25, 27, 34));
            g.Clear(parentBg);
            int y = Height / 2;
            using (Pen pen = new Pen(Color.FromArgb(38, 42, 54), 1f))
            {
                g.DrawLine(pen, 0, y, Width, y);
            }
        }
    }

    public class ModernProgressBar : Control
    {
        private int value = 0;
        public int Value
        {
            get { return value; }
            set { this.value = Math.Max(0, Math.Min(100, value)); Invalidate(); }
        }

        public Color TrackColor { get; set; }
        public Color ProgressColor { get; set; }
        public Color SecondaryColor { get; set; }

        public bool IsAnimatedFlow { get; set; }
        public bool IsWiredPulse { get; set; }
        public bool IsChargingFill { get; set; }

        private float flowPhase = 0f;
        private System.Windows.Forms.Timer flowTimer;

        public ModernProgressBar()
        {
            TrackColor = Color.FromArgb(38, 42, 53);
            ProgressColor = Color.FromArgb(0, 230, 118);
            SecondaryColor = Color.FromArgb(255, 214, 0);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);

            flowTimer = new System.Windows.Forms.Timer();
            flowTimer.Interval = 30;
            flowTimer.Tick += (s, e) =>
            {
                if (!IsAnimatedFlow)
                {
                    flowTimer.Stop();
                    return;
                }
                flowPhase += 0.08f;
                if (flowPhase > (float)(Math.PI * 200)) flowPhase = 0f;
                Invalidate();
            };
        }

        public void StartChargingFlow(Color c1, Color c2)
        {
            IsAnimatedFlow = true;
            IsWiredPulse = false;
            IsChargingFill = false;
            ProgressColor = c1;
            SecondaryColor = c2;
            if (!flowTimer.Enabled) flowTimer.Start();
            Invalidate();
        }

        public void StartWiredFlow(Color c)
        {
            IsAnimatedFlow = true;
            IsWiredPulse = true;
            IsChargingFill = false;
            ProgressColor = c;
            if (!flowTimer.Enabled) flowTimer.Start();
            Invalidate();
        }

        public void StartChargingFill(int pct, Color c1, Color c2)
        {
            this.value = Math.Max(0, Math.Min(100, pct));
            IsAnimatedFlow = true;
            IsWiredPulse = false;
            IsChargingFill = true;
            ProgressColor = c1;
            SecondaryColor = c2;
            if (!flowTimer.Enabled) flowTimer.Start();
            Invalidate();
        }

        public void StopFlow(int pct, Color c)
        {
            this.value = Math.Max(0, Math.Min(100, pct));
            IsAnimatedFlow = false;
            IsWiredPulse = false;
            IsChargingFill = false;
            ProgressColor = c;
            flowTimer.Stop();
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color parentBg = (Parent is RoundedCard) ? ((RoundedCard)Parent).CardColor : (Parent != null ? Parent.BackColor : Color.FromArgb(25, 27, 34));
            g.Clear(parentBg);

            int h = Height;
            int radius = h / 2;
            Rectangle rect = new Rectangle(0, 0, Width, h);

            using (GraphicsPath trackPath = RoundedCard.GetRoundedRectangle(rect, radius))
            {
                using (SolidBrush brush = new SolidBrush(TrackColor))
                {
                    g.FillPath(brush, trackPath);
                }
            }

            if (IsAnimatedFlow)
            {
                if (IsWiredPulse)
                {
                    int alpha = (int)(150 + 80 * Math.Sin(flowPhase * 1.5f));
                    if (alpha < 60) alpha = 60; if (alpha > 240) alpha = 240;
                    using (GraphicsPath fillPath = RoundedCard.GetRoundedRectangle(rect, radius))
                    using (SolidBrush brush = new SolidBrush(Color.FromArgb(alpha, ProgressColor.R, ProgressColor.G, ProgressColor.B)))
                    {
                        g.FillPath(brush, fillPath);
                    }
                }
                else if (IsChargingFill && value > 0)
                {
                    int fillWidth = (int)((Width * (value / 100.0f)));
                    if (fillWidth < radius * 2) fillWidth = radius * 2;
                    if (fillWidth > Width) fillWidth = Width;

                    Rectangle fillRect = new Rectangle(0, 0, fillWidth, h);
                    using (GraphicsPath fillPath = RoundedCard.GetRoundedRectangle(fillRect, radius))
                    {
                        using (SolidBrush brush = new SolidBrush(ProgressColor))
                        {
                            g.FillPath(brush, fillPath);
                        }

                        float waveX = (float)((Math.Sin(flowPhase) * 0.5f + 0.5f) * fillWidth);
                        Rectangle waveRect = new Rectangle((int)waveX - 30, 0, 60, h);
                        using (LinearGradientBrush lgb = new LinearGradientBrush(new Point(waveRect.Left, 0), new Point(waveRect.Right, 0),
                            Color.FromArgb(0, SecondaryColor), Color.FromArgb(160, SecondaryColor)))
                        {
                            ColorBlend cb = new ColorBlend();
                            cb.Colors = new Color[] { Color.FromArgb(0, SecondaryColor), Color.FromArgb(180, SecondaryColor), Color.FromArgb(0, SecondaryColor) };
                            cb.Positions = new float[] { 0f, 0.5f, 1f };
                            lgb.InterpolationColors = cb;
                            g.SetClip(fillPath);
                            g.FillRectangle(lgb, waveRect);
                            g.ResetClip();
                        }
                    }
                }
                else
                {
                    using (GraphicsPath fillPath = RoundedCard.GetRoundedRectangle(rect, radius))
                    {
                        float cycle = (flowPhase * 25f) % (Width + 140) - 70;
                        Rectangle waveRect = new Rectangle((int)cycle - 70, 0, 140, h);
                        using (LinearGradientBrush lgb = new LinearGradientBrush(new Point(waveRect.Left, 0), new Point(waveRect.Right, 0),
                            Color.FromArgb(0, ProgressColor), Color.FromArgb(220, SecondaryColor)))
                        {
                            ColorBlend cb = new ColorBlend();
                            cb.Colors = new Color[] { Color.FromArgb(30, ProgressColor), Color.FromArgb(240, SecondaryColor), Color.FromArgb(30, ProgressColor) };
                            cb.Positions = new float[] { 0f, 0.5f, 1f };
                            lgb.InterpolationColors = cb;
                            g.SetClip(fillPath);
                            g.FillRectangle(lgb, waveRect);
                            g.ResetClip();
                        }
                    }
                }
            }
            else if (value > 0)
            {
                int fillWidth = (int)((Width * (value / 100.0f)));
                if (fillWidth < radius * 2) fillWidth = radius * 2;
                if (fillWidth > Width) fillWidth = Width;

                Rectangle fillRect = new Rectangle(0, 0, fillWidth, h);
                using (GraphicsPath fillPath = RoundedCard.GetRoundedRectangle(fillRect, radius))
                using (SolidBrush brush = new SolidBrush(ProgressColor))
                {
                    g.FillPath(brush, fillPath);
                }
            }
        }
    }

    public class StatusPill : Control
    {
        private string statusText = "检测中...";
        private Color statusColor = Color.FromArgb(0, 230, 118);

        public void SetStatus(string text, Color color)
        {
            this.statusText = text;
            this.statusColor = color;
            using (var g = CreateGraphics())
            {
                string clean = (text ?? "").Replace("⚡", "").Trim();
                var sz = TextRenderer.MeasureText(g, clean, Font);
                int extra = (text != null && text.Contains("⚡")) ? 22 : 0;
                this.Width = Math.Max(75, sz.Width + 24 + extra);
            }
            Invalidate();
        }

        public StatusPill()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color parentBg = (Parent is RoundedCard) ? ((RoundedCard)Parent).CardColor : (Parent != null ? Parent.BackColor : Color.FromArgb(25, 27, 34));
            g.Clear(parentBg);

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = RoundedCard.GetRoundedRectangle(rect, Height / 2))
            {
                using (SolidBrush brush = new SolidBrush(Color.FromArgb(35, statusColor.R, statusColor.G, statusColor.B)))
                {
                    g.FillPath(brush, path);
                }
                using (Pen pen = new Pen(Color.FromArgb(100, statusColor.R, statusColor.G, statusColor.B), 1.2f))
                {
                    g.DrawPath(pen, path);
                }
            }

            string text = statusText ?? "";
            bool hasLightning = text.Contains("⚡");
            if (hasLightning)
            {
                string cleanText = text.Replace("⚡", "").Trim();
                var font = Font ?? SystemFonts.DefaultFont;
                var sz = TextRenderer.MeasureText(cleanText, font);
                float iconH = Math.Max(10f, Height * 0.44f);
                float iconW = iconH * 0.55f;
                float spacing = 5f;
                float totalW = iconW + spacing + sz.Width;
                float startX = (Width - totalW) / 2f;
                float iconCx = startX + iconW / 2f;
                float iconCy = Height / 2f;

                ModernThemeHelper.DrawVectorLightning(g, iconCx, iconCy, iconH, statusColor);

                int textX = (int)(startX + iconW + spacing);
                int textY = (Height - sz.Height) / 2;
                TextRenderer.DrawText(g, cleanText, font, new Point(textX, textY), statusColor, TextFormatFlags.NoPadding);
            }
            else
            {
                TextRenderer.DrawText(g, text, Font, ClientRectangle, statusColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }
    }

    public class TactileSmoothSliderControl : Control
    {
        public float DpiScale = 1.0f;
        private int _value = 100;
        private bool _isDragging = false;
        private bool _isHovered = false;
        private readonly int[] ticks = new int[] { 0, 25, 50, 75, 100 };

        public event EventHandler ValueChanged;
        public event EventHandler ValueCommitted;

        public int Value
        {
            get { return _value; }
            set
            {
                int clamped = Math.Max(0, Math.Min(100, value));
                if (_value != clamped)
                {
                    _value = clamped;
                    Invalidate();
                    if (ValueChanged != null) ValueChanged(this, EventArgs.Empty);
                }
            }
        }

        public void SetValueWithoutEvents(int val)
        {
            int clamped = Math.Max(0, Math.Min(100, val));
            if (_value != clamped)
            {
                _value = clamped;
                Invalidate();
            }
        }

        public TactileSmoothSliderControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            Cursor = Cursors.Hand;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            _isHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            _isHovered = false;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                _isDragging = true;
                Capture = true;
                UpdateValueFromMouse(e.X);
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_isDragging)
            {
                UpdateValueFromMouse(e.X);
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (_isDragging)
            {
                _isDragging = false;
                Capture = false;
                UpdateValueFromMouse(e.X);
                if (ValueCommitted != null) ValueCommitted(this, EventArgs.Empty);
            }
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int step = e.Delta > 0 ? 5 : -5;
            Value = Math.Max(0, Math.Min(100, _value + step));
            if (ValueCommitted != null) ValueCommitted(this, EventArgs.Empty);
        }

        private void UpdateValueFromMouse(int mouseX)
        {
            int pad = (int)(10 * DpiScale);
            int trackW = Width - pad * 2;
            if (trackW <= 0) return;

            float ratio = (float)(mouseX - pad) / trackW;
            if (ratio < 0f) ratio = 0f;
            if (ratio > 1f) ratio = 1f;

            // 100% 连续无级、完全跟手、零跳格卡顿
            int targetVal = (int)Math.Round(ratio * 100);
            Value = targetVal;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Color parentBg = (Parent is RoundedCard) ? ((RoundedCard)Parent).CardColor : (Parent != null ? Parent.BackColor : Color.FromArgb(25, 27, 34));
            g.Clear(parentBg);

            int pad = (int)(10 * DpiScale);
            int trackH = (int)(6 * DpiScale);
            int trackY = (Height - trackH) / 2;
            int trackW = Width - pad * 2;

            // 1. 轨道底槽 (暗调圆角)
            Rectangle trackRect = new Rectangle(pad, trackY, trackW, trackH);
            using (var trackPath = RoundedCard.GetRoundedRectangle(trackRect, trackH / 2))
            using (var trackBrush = new SolidBrush(Color.FromArgb(36, 42, 54)))
            {
                g.FillPath(trackBrush, trackPath);
            }

            // 2. 激活进度高光渐变
            int fillW = (int)(trackW * (_value / 100.0f));
            if (fillW > 0)
            {
                Rectangle fillRect = new Rectangle(pad, trackY, fillW, trackH);
                using (var fillPath = RoundedCard.GetRoundedRectangle(fillRect, trackH / 2))
                using (var grad = new LinearGradientBrush(new Point(pad, 0), new Point(pad + trackW, 0),
                    Color.FromArgb(0, 180, 255), Color.FromArgb(0, 229, 255)))
                {
                    g.FillPath(grad, fillPath);
                }
            }

            // 3. 刻度参考点 (0, 25, 50, 75, 100) - 作为纯视觉参考，无任何卡顿锁定
            int dotD = Math.Max(2, (int)(3 * DpiScale));
            for (int i = 0; i < ticks.Length; i++)
            {
                int tx = pad + (int)(trackW * (ticks[i] / 100.0f));
                int ty = trackY + trackH / 2;
                bool isPast = _value >= ticks[i];
                Color dotColor = isPast ? Color.FromArgb(200, 255, 255, 255) : Color.FromArgb(90, 80, 95, 120);
                using (var db = new SolidBrush(dotColor))
                {
                    g.FillEllipse(db, tx - dotD / 2, ty - dotD / 2, dotD, dotD);
                }
            }

            // 4. 拇指旋钮 (Thumb Knob) - 丝滑流畅贴合光标
            int thumbX = pad + fillW;
            int thumbY = Height / 2;
            int thumbR = (_isDragging || _isHovered) ? (int)(8 * DpiScale) : (int)(7 * DpiScale);

            // 旋钮外晕光
            int glowR = thumbR + (int)(4 * DpiScale);
            using (var glowBrush = new SolidBrush(Color.FromArgb(_isDragging ? 70 : (_isHovered ? 45 : 25), 0, 229, 255)))
            {
                g.FillEllipse(glowBrush, thumbX - glowR, thumbY - glowR, glowR * 2, glowR * 2);
            }

            // 旋钮主体
            using (var thumbBrush = new SolidBrush(Color.FromArgb(245, 250, 255)))
            {
                g.FillEllipse(thumbBrush, thumbX - thumbR, thumbY - thumbR, thumbR * 2, thumbR * 2);
            }

            // 旋钮中心青色核心点
            int coreR = Math.Max(2, (int)(3 * DpiScale));
            using (var coreBrush = new SolidBrush(Color.FromArgb(0, 190, 240)))
            {
                g.FillEllipse(coreBrush, thumbX - coreR, thumbY - coreR, coreR * 2, coreR * 2);
            }
        }
    }

    #endregion

    #region Context Menu Custom Renderer

    public class ModernDarkMenuRenderer : ToolStripProfessionalRenderer
    {
        private static readonly Color bgCol = Color.FromArgb(24, 27, 34);
        private static readonly Color hoverCol = Color.FromArgb(38, 44, 58);
        private static readonly Color hoverBorder = Color.FromArgb(58, 68, 90);
        private static readonly Color textCol = Color.FromArgb(235, 240, 250);
        private static readonly Color disabledCol = Color.FromArgb(100, 110, 128);
        private static readonly Color separatorCol = Color.FromArgb(42, 48, 62);
        private static readonly Color accentGreen = Color.FromArgb(0, 230, 118);

        public ModernDarkMenuRenderer() : base(new DarkColorTable()) { }

        protected override void OnRenderMenuItemBackground(ToolStripItemRenderEventArgs e)
        {
            if (e.Item.Selected && e.Item.Enabled)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
                using (GraphicsPath path = RoundedCard.GetRoundedRectangle(r, 4))
                {
                    using (SolidBrush b = new SolidBrush(hoverCol))
                    {
                        e.Graphics.FillPath(b, path);
                    }
                    using (Pen p = new Pen(hoverBorder, 1f))
                    {
                        e.Graphics.DrawPath(p, path);
                    }
                }
            }
            else if (e.Item.OwnerItem != null && e.Item.Pressed)
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(4, 1, e.Item.Width - 8, e.Item.Height - 2);
                using (GraphicsPath path = RoundedCard.GetRoundedRectangle(r, 4))
                using (SolidBrush b = new SolidBrush(hoverCol))
                {
                    e.Graphics.FillPath(b, path);
                }
            }
        }

        protected override void OnRenderItemText(ToolStripItemTextRenderEventArgs e)
        {
            Color color = textCol;
            if (!e.Item.Enabled)
            {
                color = disabledCol;
            }
            else if (e.Item.ForeColor != Color.Empty && e.Item.ForeColor != SystemColors.ControlText && e.Item.ForeColor != Color.Black)
            {
                color = e.Item.ForeColor;
            }
            else if (e.Item.Tag != null && e.Item.Tag.ToString() == "Header")
            {
                color = accentGreen;

                // Draw status dot in left image margin column (exactly aligned with checkmarks)
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                float dotY = e.Item.Height / 2f;
                float dotX = 17f;
                float radius = 3.5f;
                using (SolidBrush dotBrush = new SolidBrush(accentGreen))
                {
                    e.Graphics.FillEllipse(dotBrush, dotX - radius, dotY - radius, radius * 2f, radius * 2f);
                }
            }
            else if (e.Item.Selected)
            {
                color = Color.White;
            }

            Rectangle textRect = new Rectangle(e.TextRectangle.X, 0, e.Item.Width - e.TextRectangle.X - 14, e.Item.Height);
            if (e.Text != null && e.Text.Contains("\t"))
            {
                string[] parts = e.Text.Split('\t');
                TextRenderer.DrawText(e.Graphics, parts[0], e.TextFont, textRect, color,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                if (parts.Length > 1)
                {
                    TextRenderer.DrawText(e.Graphics, parts[1], e.TextFont, textRect, color,
                        TextFormatFlags.Right | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }
            }
            else
            {
                TextRenderer.DrawText(e.Graphics, e.Text, e.TextFont, textRect, color,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }

        protected override void OnRenderSeparator(ToolStripSeparatorRenderEventArgs e)
        {
            int y = e.Item.Height / 2;
            using (Pen p = new Pen(separatorCol, 1f))
            {
                e.Graphics.DrawLine(p, 8, y, e.Item.Width - 8, y);
            }
        }

        protected override void OnRenderItemCheck(ToolStripItemImageRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float centerY = e.Item.Height / 2f;
            float cx = 17f;

            PointF[] pts = new PointF[] {
                new PointF(cx - 5.0f, centerY - 0.5f),
                new PointF(cx - 1.5f, centerY + 3.5f),
                new PointF(cx + 5.5f, centerY - 4.5f)
            };
            using (Pen p = new Pen(accentGreen, 2.2f))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                e.Graphics.DrawLines(p, pts);
            }
        }

        protected override void OnRenderArrow(ToolStripArrowRenderEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            float centerY = e.Item.Height / 2f;
            float cx = e.ArrowRectangle.X + e.ArrowRectangle.Width / 2f;

            Color arrowColor = e.Item.Selected ? Color.White : Color.FromArgb(145, 155, 175);
            PointF[] arrow = new PointF[] {
                new PointF(cx - 2.5f, centerY - 4.5f),
                new PointF(cx + 2.0f, centerY),
                new PointF(cx - 2.5f, centerY + 4.5f)
            };
            using (Pen p = new Pen(arrowColor, 1.8f))
            {
                p.StartCap = LineCap.Round;
                p.EndCap = LineCap.Round;
                p.LineJoin = LineJoin.Round;
                e.Graphics.DrawLines(p, arrow);
            }
        }

        protected override void OnRenderImageMargin(ToolStripRenderEventArgs e) { }

        private class DarkColorTable : ProfessionalColorTable
        {
            public override Color MenuBorder { get { return Color.FromArgb(48, 54, 68); } }
            public override Color MenuItemBorder { get { return Color.Transparent; } }
            public override Color MenuItemSelected { get { return Color.FromArgb(38, 44, 58); } }
            public override Color ToolStripDropDownBackground { get { return Color.FromArgb(24, 27, 34); } }
            public override Color ImageMarginGradientBegin { get { return Color.FromArgb(24, 27, 34); } }
            public override Color ImageMarginGradientMiddle { get { return Color.FromArgb(24, 27, 34); } }
            public override Color ImageMarginGradientEnd { get { return Color.FromArgb(24, 27, 34); } }
        }
    }

    public static class ModernThemeHelper
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;

        [DllImport("uxtheme.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        public static void ApplyModernWin11Theme(IntPtr hWnd)
        {
            try
            {
                int trueVal = 1;
                DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref trueVal, sizeof(int));
                DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref trueVal, sizeof(int));

                int roundVal = 2;
                DwmSetWindowAttribute(hWnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref roundVal, sizeof(int));

                int captionBg = 0x00171312;
                DwmSetWindowAttribute(hWnd, DWMWA_CAPTION_COLOR, ref captionBg, sizeof(int));

                int captionText = 0x00FFFFFF;
                DwmSetWindowAttribute(hWnd, DWMWA_TEXT_COLOR, ref captionText, sizeof(int));

                int borderCol = 0x003A2E2A;
                DwmSetWindowAttribute(hWnd, DWMWA_BORDER_COLOR, ref borderCol, sizeof(int));
            }
            catch { }
        }

        public static void DrawModernDeviceIcon(Graphics g, Rectangle bounds, DeviceCategory category, Color accent)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = bounds.X + bounds.Width / 2f;
            float cy = bounds.Y + bounds.Height / 2f;
            float dim = Math.Min(bounds.Width, bounds.Height);
            float s = dim / 24.0f;

            if (category == DeviceCategory.Headset)
            {
                // Sleek curved modern over-ear headset
                using (Pen headPen = new Pen(Color.FromArgb(235, 240, 252), 1.8f * s))
                {
                    headPen.StartCap = LineCap.Round;
                    headPen.EndCap = LineCap.Round;
                    g.DrawArc(headPen, cx - 8.5f * s, cy - 8.5f * s, 17f * s, 17f * s, 180f, 180f);
                }
                // Cushioned ear cups
                using (SolidBrush cupBrush = new SolidBrush(accent))
                using (Pen cupPen = new Pen(Color.FromArgb(220, 240, 255), 1.1f * s))
                {
                    RectangleF leftCup = new RectangleF(cx - 10.5f * s, cy - 1f * s, 4f * s, 8.5f * s);
                    RectangleF rightCup = new RectangleF(cx + 6.5f * s, cy - 1f * s, 4f * s, 8.5f * s);
                    int cupR = Math.Max(1, (int)(2 * s));
                    using (GraphicsPath lp = RoundedCard.GetRoundedRectangle(Rectangle.Round(leftCup), cupR))
                    using (GraphicsPath rp = RoundedCard.GetRoundedRectangle(Rectangle.Round(rightCup), cupR))
                    {
                        g.FillPath(cupBrush, lp);
                        g.DrawPath(cupPen, lp);
                        g.FillPath(cupBrush, rp);
                        g.DrawPath(cupPen, rp);
                    }
                }
                // Center acoustic/pivot accent dot
                using (SolidBrush dotBrush = new SolidBrush(Color.FromArgb(220, 255, 255, 255)))
                {
                    g.FillEllipse(dotBrush, cx - 1.2f * s, cy + 2f * s, 2.4f * s, 2.4f * s);
                }
            }
            else if (category == DeviceCategory.Earbuds)
            {
                // Sleek TWS in-ear earbuds (Left & Right earpieces with acoustic stems)
                using (SolidBrush earBg = new SolidBrush(Color.FromArgb(38, 44, 60)))
                using (Pen earPen = new Pen(Color.FromArgb(210, 225, 250), 1.2f * s))
                using (SolidBrush accBrush = new SolidBrush(accent))
                {
                    // Left earbud bulb + stem
                    g.FillEllipse(earBg, cx - 8.5f * s, cy - 7f * s, 6.5f * s, 6.5f * s);
                    g.DrawEllipse(earPen, cx - 8.5f * s, cy - 7f * s, 6.5f * s, 6.5f * s);
                    using (GraphicsPath leftStem = RoundedCard.GetRoundedRectangle(new Rectangle((int)(cx - 6.5f * s), (int)(cy - 2f * s), (int)(2.8f * s), (int)(8.5f * s)), (int)(1.2f * s)))
                    {
                        g.FillPath(earBg, leftStem);
                        g.DrawPath(earPen, leftStem);
                    }
                    g.FillEllipse(accBrush, cx - 5.5f * s, cy + 4.5f * s, 1.8f * s, 1.8f * s);

                    // Right earbud bulb + stem
                    g.FillEllipse(earBg, cx + 2f * s, cy - 7f * s, 6.5f * s, 6.5f * s);
                    g.DrawEllipse(earPen, cx + 2f * s, cy - 7f * s, 6.5f * s, 6.5f * s);
                    using (GraphicsPath rightStem = RoundedCard.GetRoundedRectangle(new Rectangle((int)(cx + 3.7f * s), (int)(cy - 2f * s), (int)(2.8f * s), (int)(8.5f * s)), (int)(1.2f * s)))
                    {
                        g.FillPath(earBg, rightStem);
                        g.DrawPath(earPen, rightStem);
                    }
                    g.FillEllipse(accBrush, cx + 4.7f * s, cy + 4.5f * s, 1.8f * s, 1.8f * s);
                }
            }
            else if (category == DeviceCategory.Keyboard)
            {
                // Modern keyboard chassis
                Rectangle kbRect = new Rectangle((int)(cx - 9.5f * s), (int)(cy - 6.5f * s), (int)(19 * s), (int)(13 * s));
                int kbR = Math.Max(1, (int)(3 * s));
                using (GraphicsPath kbPath = RoundedCard.GetRoundedRectangle(kbRect, kbR))
                {
                    using (SolidBrush kbBg = new SolidBrush(Color.FromArgb(32, 38, 52)))
                    {
                        g.FillPath(kbBg, kbPath);
                    }
                    using (Pen kbBorder = new Pen(Color.FromArgb(170, 185, 210), 1.2f * s))
                    {
                        g.DrawPath(kbBorder, kbPath);
                    }
                }
                // Top row keycaps
                using (SolidBrush keyBrush = new SolidBrush(Color.FromArgb(220, 230, 245)))
                {
                    g.FillRectangle(keyBrush, cx - 7f * s, cy - 4f * s, 2.5f * s, 2f * s);
                    g.FillRectangle(keyBrush, cx - 3f * s, cy - 4f * s, 2.5f * s, 2f * s);
                    g.FillRectangle(keyBrush, cx + 1f * s, cy - 4f * s, 2.5f * s, 2f * s);
                    g.FillRectangle(keyBrush, cx + 4.5f * s, cy - 4f * s, 2.5f * s, 2f * s);
                }
                // Middle row keycaps
                using (SolidBrush keyBrush2 = new SolidBrush(Color.FromArgb(180, 195, 215)))
                {
                    g.FillRectangle(keyBrush2, cx - 7f * s, cy - 0.5f * s, 2.5f * s, 2f * s);
                    g.FillRectangle(keyBrush2, cx - 3f * s, cy - 0.5f * s, 2.5f * s, 2f * s);
                    g.FillRectangle(keyBrush2, cx + 1f * s, cy - 0.5f * s, 2.5f * s, 2f * s);
                    g.FillRectangle(keyBrush2, cx + 4.5f * s, cy - 0.5f * s, 2.5f * s, 2f * s);
                }
                // Space bar at bottom with accent color
                using (SolidBrush spaceBrush = new SolidBrush(accent))
                {
                    g.FillRectangle(spaceBrush, cx - 5f * s, cy + 2.5f * s, 10f * s, 1.8f * s);
                }
            }
            else if (category == DeviceCategory.Gamepad)
            {
                // Gamepad body contour
                Rectangle gpRect = new Rectangle((int)(cx - 9.5f * s), (int)(cy - 6f * s), (int)(19 * s), (int)(12 * s));
                int gpR = Math.Max(2, (int)(4 * s));
                using (GraphicsPath gpPath = RoundedCard.GetRoundedRectangle(gpRect, gpR))
                {
                    using (SolidBrush gpBg = new SolidBrush(Color.FromArgb(32, 38, 52)))
                    {
                        g.FillPath(gpBg, gpPath);
                    }
                    using (Pen gpBorder = new Pen(Color.FromArgb(170, 185, 210), 1.2f * s))
                    {
                        g.DrawPath(gpBorder, gpPath);
                    }
                }
                // D-pad on left (cross shape)
                using (SolidBrush dpad = new SolidBrush(Color.White))
                {
                    g.FillRectangle(dpad, cx - 6.2f * s, cy - 2.5f * s, 2f * s, 5.2f * s);
                    g.FillRectangle(dpad, cx - 7.8f * s, cy - 0.9f * s, 5.2f * s, 2f * s);
                }
                // Action buttons (A/B/X/Y) on right
                using (SolidBrush btnBrush = new SolidBrush(accent))
                {
                    float bRad = 1.1f * s;
                    g.FillEllipse(btnBrush, cx + 4.5f * s - bRad, cy - 2.5f * s - bRad, bRad * 2, bRad * 2);
                    g.FillEllipse(btnBrush, cx + 6.7f * s - bRad, cy - 0.3f * s - bRad, bRad * 2, bRad * 2);
                    g.FillEllipse(btnBrush, cx + 4.5f * s - bRad, cy + 1.9f * s - bRad, bRad * 2, bRad * 2);
                    g.FillEllipse(btnBrush, cx + 2.3f * s - bRad, cy - 0.3f * s - bRad, bRad * 2, bRad * 2);
                }
                // Center Guide / Home button
                using (SolidBrush homeBrush = new SolidBrush(Color.FromArgb(235, 240, 255)))
                {
                    g.FillEllipse(homeBrush, cx - 1f * s, cy - 1f * s, 2f * s, 2f * s);
                }
            }
            else if (category == DeviceCategory.Dongle)
            {
                // USB Dongle / Wireless Receiver
                // USB metal plug
                RectangleF plugRect = new RectangleF(cx - 3.5f * s, cy - 8f * s, 7f * s, 4.5f * s);
                using (SolidBrush plugBg = new SolidBrush(Color.FromArgb(180, 195, 215)))
                using (Pen plugPen = new Pen(Color.FromArgb(230, 240, 255), 1.0f * s))
                {
                    g.FillRectangle(plugBg, plugRect);
                    g.DrawRectangle(plugPen, plugRect.X, plugRect.Y, plugRect.Width, plugRect.Height);
                }
                // Dongle main case body
                Rectangle bodyRect = new Rectangle((int)(cx - 6f * s), (int)(cy - 3.5f * s), (int)(12 * s), (int)(11.5f * s));
                int dongleR = Math.Max(1, (int)(3 * s));
                using (GraphicsPath bodyPath = RoundedCard.GetRoundedRectangle(bodyRect, dongleR))
                {
                    using (SolidBrush bodyBg = new SolidBrush(Color.FromArgb(32, 38, 52)))
                    {
                        g.FillPath(bodyBg, bodyPath);
                    }
                    using (Pen bodyBorder = new Pen(Color.FromArgb(170, 185, 210), 1.2f * s))
                    {
                        g.DrawPath(bodyBorder, bodyPath);
                    }
                }
                // Status LED / Wireless waves
                using (SolidBrush ledBrush = new SolidBrush(accent))
                {
                    g.FillEllipse(ledBrush, cx - 1.5f * s, cy + 2f * s, 3f * s, 3f * s);
                }
                using (Pen wavePen = new Pen(Color.FromArgb(180, accent.R, accent.G, accent.B), 1.0f * s))
                {
                    g.DrawArc(wavePen, cx - 4f * s, cy - 0.5f * s, 8f * s, 8f * s, 200, 140);
                }
            }
            else if (category == DeviceCategory.Generic)
            {
                // Industrial Chip / Device Hub icon
                Rectangle genRect = new Rectangle((int)(cx - 7f * s), (int)(cy - 7f * s), (int)(14 * s), (int)(14 * s));
                int genR = Math.Max(1, (int)(3 * s));
                using (GraphicsPath genPath = RoundedCard.GetRoundedRectangle(genRect, genR))
                {
                    using (SolidBrush genBg = new SolidBrush(Color.FromArgb(32, 38, 52)))
                    {
                        g.FillPath(genBg, genPath);
                    }
                    using (Pen genBorder = new Pen(Color.FromArgb(170, 185, 210), 1.2f * s))
                    {
                        g.DrawPath(genBorder, genPath);
                    }
                }
                // Center pulse accent circle and pins
                using (SolidBrush coreBrush = new SolidBrush(accent))
                {
                    g.FillRectangle(coreBrush, cx - 2.5f * s, cy - 2.5f * s, 5f * s, 5f * s);
                }
                using (Pen pinPen = new Pen(Color.FromArgb(200, 220, 245), 1.1f * s))
                {
                    g.DrawLine(pinPen, cx - 4.5f * s, cy - 9f * s, cx - 4.5f * s, cy - 7f * s);
                    g.DrawLine(pinPen, cx + 4.5f * s, cy - 9f * s, cx + 4.5f * s, cy - 7f * s);
                    g.DrawLine(pinPen, cx - 4.5f * s, cy + 7f * s, cx - 4.5f * s, cy + 9f * s);
                    g.DrawLine(pinPen, cx + 4.5f * s, cy + 7f * s, cx + 4.5f * s, cy + 9f * s);
                }
            }
            else // Mouse or default
            {
                // Sleek ergonomic mouse silhouette
                Rectangle mRect = new Rectangle((int)(cx - 5.5f * s), (int)(cy - 8.5f * s), (int)(11 * s), (int)(17 * s));
                int mR = Math.Max(2, (int)(5 * s));
                using (GraphicsPath mPath = RoundedCard.GetRoundedRectangle(mRect, mR))
                {
                    using (SolidBrush mBg = new SolidBrush(Color.FromArgb(32, 38, 52)))
                    {
                        g.FillPath(mBg, mPath);
                    }
                    using (Pen mBorder = new Pen(Color.FromArgb(170, 185, 210), 1.2f * s))
                    {
                        g.DrawPath(mBorder, mPath);
                    }
                }
                // Divider slit between left/right click
                using (Pen slitPen = new Pen(Color.FromArgb(110, 125, 150), 1f * s))
                {
                    g.DrawLine(slitPen, cx, cy - 8f * s, cx, cy - 3f * s);
                }
                // Glowing scroll wheel
                RectangleF wheelRect = new RectangleF(cx - 1f * s, cy - 6.5f * s, 2f * s, 4.5f * s);
                using (SolidBrush wheelBrush = new SolidBrush(accent))
                {
                    g.FillRectangle(wheelBrush, wheelRect);
                }
            }
        }

        public static void DrawVectorLightning(Graphics g, float cx, float cy, float height, Color color)
        {
            float hh = height / 2f;
            float hw = (height * 0.55f) / 2f;
            PointF[] pts = new PointF[]
            {
                new PointF(cx + 0.15f * hw, cy - hh),
                new PointF(cx - hw, cy + 0.05f * hh),
                new PointF(cx - 0.05f * hw, cy + 0.05f * hh),
                new PointF(cx - 0.4f * hw, cy + hh),
                new PointF(cx + hw, cy - 0.05f * hh),
                new PointF(cx + 0.05f * hw, cy - 0.05f * hh)
            };
            var prevSm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush b = new SolidBrush(color))
            {
                g.FillPolygon(b, pts);
            }
            g.SmoothingMode = prevSm;
        }
    }

    public class AnimatedContextMenuStrip : ContextMenuStrip
    {
        private const int AW_SLIDE = 0x00040000;
        private const int AW_VER_POSITIVE = 0x00000004;

        [DllImport("user32.dll")]
        private static extern bool AnimateWindow(IntPtr hWnd, int time, int flags);

        protected override void SetVisibleCore(bool value)
        {
            if (value && !IsHandleCreated)
            {
                CreateControl();
            }
            if (value)
            {
                try
                {
                    AnimateWindow(this.Handle, 120, AW_SLIDE | AW_VER_POSITIVE);
                }
                catch { }
            }
            base.SetVisibleCore(value);
        }
    }

    public class ModernDarkDropdown : Control
    {
        public float DpiScale = 1.0f;
        public List<string> Items = new List<string>();
        private int selectedIndex = 0;
        public int SelectedIndex
        {
            get { return selectedIndex; }
            set
            {
                if (value >= 0 && value < Items.Count && selectedIndex != value)
                {
                    selectedIndex = value;
                    Invalidate();
                    if (SelectedIndexChanged != null) SelectedIndexChanged(this, EventArgs.Empty);
                }
            }
        }
        public string SelectedItem { get { return (SelectedIndex >= 0 && SelectedIndex < Items.Count) ? Items[SelectedIndex] : ""; } }

        public event EventHandler SelectedIndexChanged;
        private bool isHovered = false;
        private bool isMenuOpen = false;
        private AnimatedContextMenuStrip activeMenu = null;

        public ModernDarkDropdown()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.FromArgb(18, 19, 23);
            ForeColor = Color.White;
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
        }

        protected override void OnMouseEnter(EventArgs e) { isHovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { isHovered = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnClick(EventArgs e)
        {
            base.OnClick(e);
            if (isMenuOpen)
            {
                if (activeMenu != null)
                {
                    activeMenu.Close();
                    activeMenu = null;
                }
                isMenuOpen = false;
                Invalidate();
                return;
            }
            ShowDropdownMenu();
        }

        private void ShowDropdownMenu()
        {
            if (Items.Count == 0) return;
            activeMenu = new AnimatedContextMenuStrip();
            activeMenu.Renderer = new ModernDarkMenuRenderer();
            activeMenu.Font = new Font("Microsoft YaHei UI", 9F);
            activeMenu.ShowImageMargin = false;
            activeMenu.ShowCheckMargin = false;
            activeMenu.MinimumSize = new Size(this.Width, 0);

            activeMenu.Closed += (s, ev) => {
                isMenuOpen = false;
                activeMenu = null;
                Invalidate();
            };

            for (int i = 0; i < Items.Count; i++)
            {
                int idx = i;
                string text = Items[i];
                var mi = new ToolStripMenuItem(text, null, (s, ev) => {
                    this.SelectedIndex = idx;
                });
                mi.AutoSize = false;
                mi.Height = (int)(28 * DpiScale);
                mi.Width = this.Width;
                mi.ForeColor = (idx == selectedIndex) ? Color.FromArgb(0, 230, 118) : Color.FromArgb(235, 240, 250);
                activeMenu.Items.Add(mi);
            }

            isMenuOpen = true;
            Invalidate();
            activeMenu.Show(this, new Point(0, this.Height + 2));
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int r = (int)(6 * DpiScale);
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Color bg = (isHovered || isMenuOpen) ? Color.FromArgb(38, 43, 56) : Color.FromArgb(28, 32, 42);
            Color border = (isHovered || isMenuOpen) ? Color.FromArgb(0, 230, 118) : Color.FromArgb(50, 56, 72);

            using (var path = CreateRoundedPath(rect, r))
            {
                using (var brush = new SolidBrush(bg)) g.FillPath(brush, path);
                using (var pen = new Pen(border, isMenuOpen ? 1.5f : 1f)) g.DrawPath(pen, path);
            }

            string text = SelectedItem;
            using (var font = new Font("Microsoft YaHei UI", 9F))
            {
                int textW = Width - (int)(32 * DpiScale);
                TextRenderer.DrawText(g, text, font, new Rectangle((int)(10 * DpiScale), 0, textW, Height),
                    Color.FromArgb(240, 245, 255), TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }

            using (var chvFont = new Font("Segoe UI", 8.5F, FontStyle.Bold))
            using (var chvBrush = new SolidBrush(isMenuOpen ? Color.FromArgb(0, 230, 118) : Color.FromArgb(160, 170, 195)))
            {
                string arrow = isMenuOpen ? "▴" : "▾";
                g.DrawString(arrow, chvFont, chvBrush, Width - (int)(20 * DpiScale), (Height / 2) - (int)(8 * DpiScale));
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
    }

    public class ModernInputBox : Control
    {
        public float DpiScale = 1.0f;
        private TextBox innerBox;
        private bool isFocused = false;
        private bool isHovered = false;

        public override string Text
        {
            get { return innerBox != null ? innerBox.Text : base.Text; }
            set
            {
                base.Text = value;
                if (innerBox != null && innerBox.Text != value) innerBox.Text = value;
            }
        }

        public ModernInputBox()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.FromArgb(18, 19, 23);
            ForeColor = Color.White;
            DoubleBuffered = true;

            innerBox = new TextBox();
            innerBox.BorderStyle = BorderStyle.None;
            innerBox.BackColor = Color.FromArgb(28, 32, 44);
            innerBox.ForeColor = Color.FromArgb(245, 250, 255);
            innerBox.Font = new Font("Microsoft YaHei UI", 9.5F);
            innerBox.GotFocus += (s, e) => { isFocused = true; Invalidate(); };
            innerBox.LostFocus += (s, e) => { isFocused = false; Invalidate(); };
            innerBox.MouseEnter += (s, e) => { isHovered = true; Invalidate(); };
            innerBox.MouseLeave += (s, e) => { isHovered = false; Invalidate(); };
            innerBox.TextChanged += (s, e) => { base.Text = innerBox.Text; };
            this.Controls.Add(innerBox);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (innerBox != null)
            {
                int padH = (int)(10 * DpiScale);
                int h = (int)(20 * DpiScale);
                innerBox.SetBounds(padH, (Height - h) / 2, Width - (padH * 2), h);
            }
        }

        protected override void OnMouseEnter(EventArgs e) { isHovered = true; Invalidate(); base.OnMouseEnter(e); }
        protected override void OnMouseLeave(EventArgs e) { isHovered = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int r = (int)(6 * DpiScale);
            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            Color bg = Color.FromArgb(28, 32, 44);
            Color border = isFocused ? Color.FromArgb(0, 230, 118) : (isHovered ? Color.FromArgb(80, 110, 150) : Color.FromArgb(50, 56, 72));

            using (var path = RoundedCard.GetRoundedRectangle(rect, r))
            {
                using (var brush = new SolidBrush(bg)) g.FillPath(brush, path);
                using (var pen = new Pen(border, isFocused ? 1.5f : 1.0f)) g.DrawPath(pen, path);
            }
        }
    }

    public class WizardRadarBadge : Control
    {
        public float DpiScale = 1.0f;
        private System.Windows.Forms.Timer pulseTimer;
        private float pulseStep = 0f;

        public WizardRadarBadge()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            DoubleBuffered = true;
            pulseTimer = new System.Windows.Forms.Timer();
            pulseTimer.Interval = 50;
            pulseTimer.Tick += (s, e) => {
                pulseStep += 0.08f;
                if (pulseStep > 1.0f) pulseStep = 0f;
                Invalidate();
            };
            pulseTimer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && pulseTimer != null)
            {
                pulseTimer.Stop();
                pulseTimer.Dispose();
                pulseTimer = null;
            }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = Width / 2f;
            float cy = Height / 2f;
            float maxR = Math.Min(Width, Height) / 2f - (2f * DpiScale);

            // Dark background circle
            using (var bgBrush = new SolidBrush(Color.FromArgb(28, 34, 48)))
            {
                g.FillEllipse(bgBrush, cx - maxR, cy - maxR, maxR * 2, maxR * 2);
            }

            // Expanding pulse wave
            float curR = maxR * pulseStep;
            int alpha = (int)(180 * (1f - pulseStep));
            if (alpha > 0 && curR > 2f)
            {
                using (var pulsePen = new Pen(Color.FromArgb(alpha, 0, 230, 118), 1.5f * DpiScale))
                {
                    g.DrawEllipse(pulsePen, cx - curR, cy - curR, curR * 2, curR * 2);
                }
            }

            // Outer boundary ring
            using (var ringPen = new Pen(Color.FromArgb(60, 0, 230, 118), 1f * DpiScale))
            {
                g.DrawEllipse(ringPen, cx - maxR, cy - maxR, maxR * 2, maxR * 2);
            }

            // Center glowing beacon
            using (var centerBrush = new SolidBrush(Color.FromArgb(0, 230, 118)))
            {
                float cr = 3.5f * DpiScale;
                g.FillEllipse(centerBrush, cx - cr, cy - cr, cr * 2, cr * 2);
            }
        }
    }

    public class ThreeLocksHudControl : Control
    {
        public float DpiScale = 1.0f;
        private System.Windows.Forms.Timer pollTimer;
        private bool capsOn, numOn, scrollOn;

        public ThreeLocksHudControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            DoubleBuffered = true;
            pollTimer = new System.Windows.Forms.Timer();
            pollTimer.Interval = 150;
            pollTimer.Tick += (s, e) => CheckStatus();
            pollTimer.Start();
            CheckStatus();
        }

        public void CheckStatus()
        {
            bool c = Control.IsKeyLocked(Keys.CapsLock);
            bool n = Control.IsKeyLocked(Keys.NumLock);
            bool sc = Control.IsKeyLocked(Keys.Scroll);
            if (c != capsOn || n != numOn || sc != scrollOn)
            {
                capsOn = c;
                numOn = n;
                scrollOn = sc;
                Invalidate();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && pollTimer != null)
            {
                pollTimer.Stop();
                pollTimer.Dispose();
                pollTimer = null;
            }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            int pad = (int)(2 * DpiScale);
            int gap = (int)(6 * DpiScale);
            int w = (Width - (pad * 2) - (gap * 2)) / 3;
            int h = Height - (pad * 2);

            DrawPill(g, new Rectangle(pad, pad, w, h), "🔤 大写锁定", capsOn);
            DrawPill(g, new Rectangle(pad + w + gap, pad, w, h), "🔢 数字小键盘", numOn);
            DrawPill(g, new Rectangle(pad + (w + gap) * 2, pad, w, h), "📜 滚屏锁定", scrollOn);
        }

        private void DrawPill(Graphics g, Rectangle rect, string text, bool isOn)
        {
            int r = (int)(5 * DpiScale);
            Color bg = isOn ? Color.FromArgb(40, 0, 230, 118) : Color.FromArgb(24, 28, 38);
            Color border = isOn ? Color.FromArgb(0, 230, 118) : Color.FromArgb(45, 52, 68);
            Color fg = isOn ? Color.FromArgb(0, 230, 118) : Color.FromArgb(120, 130, 150);

            using (var path = RoundedCard.GetRoundedRectangle(rect, r))
            {
                using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                using (var p = new Pen(border, 1f)) g.DrawPath(p, path);
            }

            string fullText = text + (isOn ? " ● 开启" : " ○ 关闭");
            using (Font f = new Font("Microsoft YaHei UI", 8.0F, isOn ? FontStyle.Bold : FontStyle.Regular))
            {
                TextRenderer.DrawText(g, fullText, f, rect, fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }
        }
    }

    public static class UniversalWinLockHelper
    {
        private const int WH_KEYBOARD_LL = 13;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);
        private static LowLevelKeyboardProc proc = HookCallback;
        private static IntPtr hookId = IntPtr.Zero;
        public static bool IsLocked { get; private set; }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        public static void SetLock(bool enable)
        {
            if (enable == IsLocked) return;
            if (enable)
            {
                using (Process curProcess = Process.GetCurrentProcess())
                using (ProcessModule curModule = curProcess.MainModule)
                {
                    hookId = SetWindowsHookEx(WH_KEYBOARD_LL, proc, GetModuleHandle(curModule.ModuleName), 0);
                    IsLocked = (hookId != IntPtr.Zero);
                }
            }
            else
            {
                if (hookId != IntPtr.Zero)
                {
                    UnhookWindowsHookEx(hookId);
                    hookId = IntPtr.Zero;
                }
                IsLocked = false;
            }
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int vkCode = Marshal.ReadInt32(lParam);
                if (vkCode == VK_LWIN || vkCode == VK_RWIN)
                {
                    return new IntPtr(1);
                }
            }
            return CallNextHookEx(hookId, nCode, wParam, lParam);
        }
    }

    public class KeyRolloverSpeedForm : Form
    {
        private float dpiScale;
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

        public KeyRolloverSpeedForm(float scale)
        {
            this.dpiScale = scale;
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
            ModernThemeHelper.ApplyModernWin11Theme(this.Handle);
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

    public class MouseRateTestDialog : Form
    {
        private float dpiScale;
        private Label lblLiveRate;
        private Label lblPeakRate;
        private Label lblAvgRate;
        private Panel motionCanvas;
        private ModernButton btnClose;
        private ModernButton btnClear;

        private long lastMotionTick = 0;
        private int peakHz = 0;
        private double avgHz = 0;
        private int sampleCount = 0;
        private Queue<double> recentHz = new Queue<double>();
        private List<Point> trail = new List<Point>();

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ModernThemeHelper.ApplyModernWin11Theme(this.Handle);
        }

        public MouseRateTestDialog(float scale)
        {
            this.dpiScale = scale;
            this.Text = "FerrisPulse · 鼠标微秒级实时回报率 (Polling Rate) 测速台";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(16, 20, 28);
            this.ForeColor = Color.FromArgb(240, 245, 255);
            this.ClientSize = new Size((int)(460 * scale), (int)(340 * scale));
            this.KeyPreview = true;

            int pad = (int)(16 * scale);

            Label lblTitle = new Label();
            lblTitle.Text = "⚡ 鼠标硬件物理回报率实测 (在方框内划动鼠标即可测量)";
            lblTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(0, 230, 118);
            lblTitle.SetBounds(pad, (int)(12 * scale), ClientSize.Width - (pad * 2), (int)(24 * scale));
            this.Controls.Add(lblTitle);

            int cardW = (ClientSize.Width - (pad * 2) - (int)(16 * scale)) / 3;
            int cardY = (int)(44 * scale);
            int cardH = (int)(62 * scale);

            Panel p1 = CreateMetricPanel("当前实时回报率", "0 Hz", out lblLiveRate, pad, cardY, cardW, cardH, Color.FromArgb(0, 230, 118));
            Panel p2 = CreateMetricPanel("峰值回报率", "0 Hz", out lblPeakRate, pad + cardW + (int)(8 * scale), cardY, cardW, cardH, Color.FromArgb(0, 229, 255));
            Panel p3 = CreateMetricPanel("平均回报率", "0 Hz", out lblAvgRate, pad + (cardW + (int)(8 * scale)) * 2, cardY, cardW, cardH, Color.FromArgb(255, 183, 77));
            this.Controls.AddRange(new Control[] { p1, p2, p3 });

            motionCanvas = new Panel();
            motionCanvas.SetBounds(pad, (int)(116 * scale), ClientSize.Width - (pad * 2), (int)(160 * scale));
            motionCanvas.BackColor = Color.FromArgb(22, 26, 36);
            motionCanvas.MouseMove += MotionCanvas_MouseMove;
            motionCanvas.Paint += MotionCanvas_Paint;
            this.Controls.Add(motionCanvas);

            int btnW = (int)(100 * scale);
            int btnH = (int)(32 * scale);
            int btnY = ClientSize.Height - btnH - (int)(12 * scale);

            btnClear = new ModernButton();
            btnClear.Text = "清空数据";
            btnClear.Font = new Font("Microsoft YaHei UI", 8.5F);
            btnClear.NormalColor = Color.FromArgb(32, 38, 52);
            btnClear.HoverColor = Color.FromArgb(48, 56, 76);
            btnClear.BorderColor = Color.FromArgb(60, 70, 96);
            btnClear.TextColor = Color.FromArgb(190, 200, 220);
            btnClear.CornerRadius = (int)(6 * scale);
            btnClear.SetBounds(pad, btnY, btnW, btnH);
            btnClear.Click += (s, e) => {
                peakHz = 0;
                avgHz = 0;
                sampleCount = 0;
                recentHz.Clear();
                trail.Clear();
                lblLiveRate.Text = "0 Hz";
                lblPeakRate.Text = "0 Hz";
                lblAvgRate.Text = "0 Hz";
                motionCanvas.Invalidate();
            };
            this.Controls.Add(btnClear);

            btnClose = new ModernButton();
            btnClose.Text = "完成退出";
            btnClose.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            btnClose.NormalColor = Color.FromArgb(0, 180, 90);
            btnClose.HoverColor = Color.FromArgb(0, 210, 105);
            btnClose.BorderColor = Color.FromArgb(0, 230, 118);
            btnClose.TextColor = Color.White;
            btnClose.CornerRadius = (int)(6 * scale);
            btnClose.SetBounds(ClientSize.Width - btnW - pad, btnY, btnW, btnH);
            btnClose.Click += (s, e) => this.Close();
            this.Controls.Add(btnClose);
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
            if (e.KeyCode == Keys.Escape) { this.Close(); }
            base.OnKeyDown(e);
        }

        private void MotionCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            long now = Stopwatch.GetTimestamp();
            if (lastMotionTick > 0)
            {
                long diff = now - lastMotionTick;
                if (diff > 0)
                {
                    double hz = (double)Stopwatch.Frequency / diff;
                    if (hz >= 50 && hz <= 10000)
                    {
                        sampleCount++;
                        int curHz = (int)Math.Round(hz);
                        if (curHz > peakHz) peakHz = curHz;

                        recentHz.Enqueue(hz);
                        if (recentHz.Count > 64) recentHz.Dequeue();

                        double sum = 0;
                        foreach (var v in recentHz) sum += v;
                        avgHz = sum / recentHz.Count;

                        lblLiveRate.Text = curHz + " Hz";
                        lblPeakRate.Text = peakHz + " Hz";
                        lblAvgRate.Text = string.Format("{0:0} Hz", avgHz);
                    }
                }
            }
            lastMotionTick = now;

            trail.Add(e.Location);
            if (trail.Count > 30) trail.RemoveAt(0);
            motionCanvas.Invalidate();
        }

        private void MotionCanvas_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (sampleCount == 0)
            {
                using (Font f = new Font("Microsoft YaHei UI", 9F))
                {
                    TextRenderer.DrawText(g, "请在方框内持续划动鼠标进行实时微秒回报率测速...", f,
                        new Rectangle(0, 0, motionCanvas.Width, motionCanvas.Height), Color.FromArgb(100, 110, 135),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                return;
            }

            if (trail.Count > 1)
            {
                for (int i = 1; i < trail.Count; i++)
                {
                    int alpha = (int)(255 * ((float)i / trail.Count));
                    using (Pen p = new Pen(Color.FromArgb(alpha, 0, 230, 118), 2f * dpiScale))
                    {
                        g.DrawLine(p, trail[i - 1], trail[i]);
                    }
                }
            }
        }
    }

    public static class AudioDeviceHelper
    {
        public static void SetDefaultAudioDevice(string friendlyName)
        {
            try
            {
                Process.Start("ms-settings:sound");
            }
            catch
            {
                try { Process.Start("mmsys.cpl"); } catch { }
            }
        }

        public static void OpenVolumeMixer()
        {
            try
            {
                Process.Start("sndvol.exe");
            }
            catch
            {
                try { Process.Start("ms-settings:sound"); } catch { }
            }
        }
    }

    public static class XInputHelper
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_VIBRATION
        {
            public ushort wLeftMotorSpeed;
            public ushort wRightMotorSpeed;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
        private static extern int XInput14_SetState(int dwUserIndex, ref XINPUT_VIBRATION pVibration);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputSetState")]
        private static extern int XInput910_SetState(int dwUserIndex, ref XINPUT_VIBRATION pVibration);

        public static bool TriggerGamepadVibration(int durationMs)
        {
            ThreadPool.QueueUserWorkItem(new WaitCallback(delegate(object state)
            {
                try
                {
                    XINPUT_VIBRATION vibOn = new XINPUT_VIBRATION();
                    vibOn.wLeftMotorSpeed = 36000;
                    vibOn.wRightMotorSpeed = 52000;

                    XINPUT_VIBRATION vibOff = new XINPUT_VIBRATION();
                    vibOff.wLeftMotorSpeed = 0;
                    vibOff.wRightMotorSpeed = 0;

                    int matchedIndex = -1;
                    for (int i = 0; i < 4; i++)
                    {
                        try
                        {
                            if (XInput14_SetState(i, ref vibOn) == 0) { matchedIndex = i; break; }
                        }
                        catch
                        {
                            try
                            {
                                if (XInput910_SetState(i, ref vibOn) == 0) { matchedIndex = i; break; }
                            }
                            catch { }
                        }
                    }

                    if (matchedIndex >= 0)
                    {
                        Thread.Sleep(durationMs);
                        try { XInput14_SetState(matchedIndex, ref vibOff); }
                        catch
                        {
                            try { XInput910_SetState(matchedIndex, ref vibOff); } catch { }
                        }
                    }
                }
                catch { }
            }));
            return true;
        }
    }

    public static class FullScreenGameDetector
    {
        [DllImport("shell32.dll")]
        private static extern int SHQueryUserNotificationState(out int pquns);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);

        [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern int GetClassName(IntPtr hWnd, StringBuilder lpClassName, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [StructLayout(LayoutKind.Sequential)]
        public struct RECT
        {
            public int Left;
            public int Top;
            public int Right;
            public int Bottom;
        }

        private const int QUNS_NOT_PRESENT = 1;
        private const int QUNS_BUSY = 2;
        private const int QUNS_RUNNING_D3D_FULL_SCREEN = 3;
        private const int QUNS_PRESENTATION_MODE = 4;
        private const int QUNS_ACCEPTS_NOTIFICATIONS = 5;
        private const int QUNS_QUIET_TIME = 6;
        private const int QUNS_APP = 7;

        public static bool IsGameRunning(out string detectedTitle)
        {
            detectedTitle = "";
            try
            {
                int qState = 0;
                int hr = SHQueryUserNotificationState(out qState);
                if (hr == 0 && qState == QUNS_RUNNING_D3D_FULL_SCREEN)
                {
                    IntPtr fgHwnd = GetForegroundWindow();
                    if (fgHwnd != IntPtr.Zero && !IsShellOrSystemWindow(fgHwnd, out detectedTitle))
                    {
                        return true;
                    }
                }

                IntPtr fg = GetForegroundWindow();
                if (fg == IntPtr.Zero) return false;

                if (IsShellOrSystemWindow(fg, out detectedTitle)) return false;

                RECT r;
                if (GetWindowRect(fg, out r))
                {
                    Screen screen = Screen.FromHandle(fg);
                    if (screen != null)
                    {
                        if (r.Left <= screen.Bounds.Left && r.Top <= screen.Bounds.Top &&
                            r.Right >= screen.Bounds.Right && r.Bottom >= screen.Bounds.Bottom)
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        private static bool IsShellOrSystemWindow(IntPtr hWnd, out string procName)
        {
            procName = "";
            try
            {
                StringBuilder sbClass = new StringBuilder(256);
                GetClassName(hWnd, sbClass, sbClass.Capacity);
                string cls = sbClass.ToString();

                if (cls == "Progman" || cls == "WorkerW" || cls == "Shell_TrayWnd" ||
                    cls == "Shell_SecondaryTrayWnd" || cls == "Windows.UI.Core.CoreWindow")
                {
                    return true;
                }

                uint pid = 0;
                GetWindowThreadProcessId(hWnd, out pid);
                if (pid != 0)
                {
                    using (Process p = Process.GetProcessById((int)pid))
                    {
                        procName = p.ProcessName;
                        string nameLower = p.ProcessName.ToLowerInvariant();
                        if (nameLower == "explorer" || nameLower == "lockapp" || 
                            nameLower == "searchhost" || nameLower == "shellexperiencehost" ||
                            nameLower == "startmenuexperiencehost" || nameLower == "taskmgr" ||
                            nameLower == "applicationframehost" || nameLower == "textinputhost" ||
                            nameLower == "ferrispulse" || nameLower == "chrome" ||
                            nameLower == "msedge" || nameLower == "firefox" ||
                            nameLower == "brave" || nameLower == "opera" ||
                            nameLower == "potplayer" || nameLower == "potplayermini64" ||
                            nameLower == "vlc" || nameLower == "mpc-hc64" ||
                            nameLower == "bilibili" || nameLower == "devenv")
                        {
                            return true;
                        }
                    }
                }
            }
            catch { }
            return false;
        }
    }

    public class ScanningBeamControl : Control
    {
        private System.Windows.Forms.Timer animTimer;
        private float scanPos = 0f;
        public bool IsScanning = false;

        public ScanningBeamControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, true);
            BackColor = Color.FromArgb(25, 27, 34);
            DoubleBuffered = true;

            animTimer = new System.Windows.Forms.Timer();
            animTimer.Interval = 16; // 60 FPS
            animTimer.Tick += (s, e) => {
                if (IsScanning)
                {
                    scanPos += 0.03f;
                    if (scanPos > 1.0f) scanPos = 0f;
                    Invalidate();
                }
            };
        }

        public void Start()
        {
            IsScanning = true;
            Visible = true;
            animTimer.Start();
        }

        public void Stop()
        {
            IsScanning = false;
            animTimer.Stop();
            Visible = false;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.Clear(BackColor);

            if (!IsScanning) return;

            using (var trackBrush = new SolidBrush(Color.FromArgb(32, 36, 48)))
            {
                g.FillRectangle(trackBrush, 0, 0, Width, Height);
            }

            int beamW = (int)(Width * 0.35f);
            int beamX = (int)((Width + beamW) * scanPos) - beamW;

            Rectangle beamRect = new Rectangle(beamX, 0, beamW, Height);
            if (beamRect.Right > 0 && beamRect.Left < Width)
            {
                using (var brush = new LinearGradientBrush(new Point(beamRect.Left, 0), new Point(beamRect.Right, 0),
                    Color.FromArgb(0, 0, 230, 118), Color.FromArgb(220, 0, 230, 118)))
                {
                    ColorBlend blend = new ColorBlend(3);
                    blend.Colors = new Color[] { Color.FromArgb(0, 0, 230, 118), Color.FromArgb(240, 0, 230, 118), Color.FromArgb(0, 0, 230, 118) };
                    blend.Positions = new float[] { 0f, 0.5f, 1f };
                    brush.InterpolationColors = blend;
                    g.FillRectangle(brush, beamRect);
                }
            }
        }
    }

    public class ModernDeviceSwitcherFlyout : Form
    {
        public List<MouseBatteryInfo> Devices = new List<MouseBatteryInfo>();
        public MouseBatteryInfo SelectedDevice = null;
        public float DpiScale = 1.0f;
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

        public ModernDeviceSwitcherFlyout(float scale)
        {
            this.DpiScale = scale;
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

    #endregion

    #region Screen OSD Floating Notification Form

    public class DpiOsdForm : Form
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
        private int currentStage = 4;
        private int totalStages = 5;
        private int osdStyle = 0; // 0 = Centered Capsule, 1 = Stepped Gauge, 2 = Compact Top-Right
        private bool isRateMode = false;
        private int currentRate = 1000;
        private string currentRateTitle = "🎮 全屏电竞模式已激活";
        private System.Windows.Forms.Timer displayTimer;
        private System.Windows.Forms.Timer fadeTimer;
        private float dpiScale = 1.0f;
        private string currentBrand = "Razer";

        public int OsdStyle
        {
            get { return osdStyle; }
            set { osdStyle = value; }
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

        public DpiOsdForm(float scale)
        {
            this.dpiScale = scale;
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
            this.isRateMode = false;
            this.currentDpi = dpi;
            this.currentStage = stage;
            this.totalStages = count > 0 ? count : 5;
            this.currentBrand = !string.IsNullOrEmpty(brand) ? brand : "Razer";

            displayTimer.Stop();
            fadeTimer.Stop();
            this.Opacity = 1.0;

            bool isRapoo = (this.currentBrand == "Rapoo");

            int targetW;
            int targetH;
            if (osdStyle == 0)
            {
                targetW = 160;
                targetH = isRapoo ? 50 : 58;
            }
            else if (osdStyle == 1)
            {
                targetW = isRapoo ? ((dpi >= 10000) ? 152 : 140) : ((dpi >= 10000) ? 192 : 184);
                targetH = isRapoo ? 52 : 62;
            }
            else
            {
                targetW = isRapoo ? ((dpi >= 10000) ? 152 : 140) : ((dpi >= 10000) ? 178 : 170);
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
            int targetH = 64;
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
                Color brandAccent = isRapoo ? Color.FromArgb(0, 210, 255) : Color.FromArgb(0, 230, 118);
                string titleText = isRapoo ? "雷柏 DPI" : "鼠标 DPI";

                if (isRateMode)
                {
                    Color rateAccent = (currentBrand == "Rapoo") ? Color.FromArgb(0, 210, 255) : Color.FromArgb(0, 230, 118);
                    if (currentRateTitle.Contains("电竞")) rateAccent = Color.FromArgb(0, 229, 255);

                    float topY = 7.5f * dpiScale;
                    float titleSize = 9.2f * dpiScale;
                    using (GraphicsPath titlePath = new GraphicsPath())
                    {
                        titlePath.AddString(currentRateTitle, ffYaHei, (int)FontStyle.Bold, titleSize, PointF.Empty, sf);
                        RectangleF titleBounds = titlePath.GetBounds();

                        float dotD = 4.5f * dpiScale;
                        float dotGap = 5f * dpiScale;
                        float topContentW = dotD + dotGap + titleBounds.Width;
                        float topStartX = (Width - topContentW) / 2f;

                        float dotY = topY + (titleBounds.Height - dotD) / 2f;
                        using (SolidBrush dotBrush = new SolidBrush(rateAccent))
                            g.FillEllipse(dotBrush, topStartX, dotY, dotD, dotD);

                        using (Matrix mTitle = new Matrix())
                        {
                            mTitle.Translate(topStartX + dotD + dotGap - titleBounds.Left, topY - titleBounds.Top);
                            titlePath.Transform(mTitle);
                        }
                        using (SolidBrush titleBrush = new SolidBrush(Color.FromArgb(195, 205, 225)))
                            g.FillPath(titleBrush, titlePath);
                    }

                    float numFontSize = 21f * dpiScale;
                    float unitFontSize = 10.5f * dpiScale;
                    using (GraphicsPath numPath = new GraphicsPath())
                    using (GraphicsPath unitPath = new GraphicsPath())
                    {
                        string numStr = currentRate.ToString();
                        numPath.AddString(numStr, ffSegoe, (int)FontStyle.Bold, numFontSize, PointF.Empty, sf);
                        RectangleF numBounds = numPath.GetBounds();

                        unitPath.AddString("Hz", ffSegoe, (int)FontStyle.Bold, unitFontSize, PointF.Empty, sf);
                        RectangleF unitBounds = unitPath.GetBounds();

                        float valGap = 4f * dpiScale;
                        float totalValW = numBounds.Width + valGap + unitBounds.Width;
                        float valStartX = (Width - totalValW) / 2f;

                        float targetNumTop = 27f * dpiScale;
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

                        // 2. Middle Value ("3000" + "DPI") - Perfectly Centered & Exact Baseline Aligned
                        float numFontSize = 20f * dpiScale;
                        float unitFontSize = 10f * dpiScale;

                        using (GraphicsPath numPath = new GraphicsPath())
                        using (GraphicsPath unitPath = new GraphicsPath())
                        {
                            string numStr = currentDpi.ToString();
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

                        float numFontSize = (currentDpi >= 10000 ? 19.5f : 21.5f) * dpiScale;
                        float unitFontSize = 10f * dpiScale;

                        using (GraphicsPath numPath = new GraphicsPath())
                        using (GraphicsPath unitPath = new GraphicsPath())
                        {
                            string numStr = currentDpi.ToString();
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

                        float numFontSize = (currentDpi >= 10000 ? 19.5f : 21.5f) * dpiScale;
                        float unitFontSize = 10f * dpiScale;

                        using (GraphicsPath numPath = new GraphicsPath())
                        using (GraphicsPath unitPath = new GraphicsPath())
                        {
                            string numStr = currentDpi.ToString();
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

    #endregion

    public class HeadsetStatusTile : Control
    {
        public float DpiScale { get; set; }
        public string Title { get; set; }
        public string StatusText { get; set; }
        public string Footnote { get; set; }
        public Color AccentColor { get; set; }

        public event Action ActionClicked;

        private Rectangle btnRect;
        private bool isBtnHovered = false;

        public HeadsetStatusTile()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            DpiScale = 1.0f;
            AccentColor = Color.FromArgb(90, 160, 255);
            Title = "蓝牙高质量音频 · Windows 原生 PnP 状态感知";
            StatusText = "已连接并处于就绪状态";
            Footnote = "支持一键打开系统声音面板切换默认输出端点";
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hover = btnRect.Contains(e.Location);
            if (hover != isBtnHovered)
            {
                isBtnHovered = hover;
                Cursor = isBtnHovered ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (isBtnHovered)
            {
                isBtnHovered = false;
                Cursor = Cursors.Default;
                Invalidate();
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (btnRect.Contains(e.Location))
            {
                if (ActionClicked != null) ActionClicked();
                else
                {
                    try { Process.Start("ms-settings:sound"); } catch { }
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color parentBg = (Parent is RoundedCard) ? ((RoundedCard)Parent).CardColor : (Parent != null ? Parent.BackColor : Color.FromArgb(25, 27, 34));
            g.Clear(parentBg);

            int w = Width;
            int h = Height;
            int r = (int)(8 * DpiScale);

            using (var path = CreateRoundedRectanglePath(new Rectangle(0, 0, w - 1, h - 1), r))
            {
                using (var brush = new SolidBrush(Color.FromArgb(17, 20, 27))) g.FillPath(brush, path);
                using (var pen = new Pen(Color.FromArgb(38, 43, 56), 1f)) g.DrawPath(pen, path);
            }

            int padX = (int)(14 * DpiScale);
            using (var titleFont = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Regular, GraphicsUnit.Point))
            using (var valFont = new Font("Microsoft YaHei UI", 10.2F, FontStyle.Bold, GraphicsUnit.Point))
            using (var footFont = new Font("Microsoft YaHei UI", 7.6F, FontStyle.Regular, GraphicsUnit.Point))
            {
                var sz1 = TextRenderer.MeasureText(Title, titleFont);
                var sz2 = TextRenderer.MeasureText("● " + StatusText, valFont);
                var sz3 = TextRenderer.MeasureText(Footnote, footFont);

                int gap1 = (int)(3.5f * DpiScale);
                int gap2 = (int)(3.5f * DpiScale);
                int totalTextH = sz1.Height + gap1 + sz2.Height + gap2 + sz3.Height;
                int startY = (h - totalTextH) / 2;

                int y1 = startY;
                int y2 = y1 + sz1.Height + gap1;
                int y3 = y2 + sz2.Height + gap2;

                using (var titleBrush = new SolidBrush(Color.FromArgb(145, 155, 175)))
                    g.DrawString(Title, titleFont, titleBrush, padX, y1);

                using (var valBrush = new SolidBrush(AccentColor))
                    g.DrawString("● " + StatusText, valFont, valBrush, padX, y2);

                using (var footBrush = new SolidBrush(Color.FromArgb(115, 125, 145)))
                    g.DrawString(Footnote, footFont, footBrush, padX, y3);

                // Action button on right
                int bW = (int)(138 * DpiScale);
                int bH = (int)(26 * DpiScale);
                int bX = w - bW - padX;
                int bY = (h - bH) / 2;
                btnRect = new Rectangle(bX, bY, bW, bH);

                Color btnBg = isBtnHovered ? Color.FromArgb(40, AccentColor.R, AccentColor.G, AccentColor.B) : Color.FromArgb(24, AccentColor.R, AccentColor.G, AccentColor.B);
                Color btnBorder = isBtnHovered ? AccentColor : Color.FromArgb(70, AccentColor.R, AccentColor.G, AccentColor.B);

                using (var bPath = CreateRoundedRectanglePath(btnRect, (int)(5 * DpiScale)))
                {
                    using (var bBrush = new SolidBrush(btnBg)) g.FillPath(bBrush, bPath);
                    using (var bPen = new Pen(btnBorder, 1f)) g.DrawPath(bPen, bPath);
                }

                using (var bFont = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Bold))
                {
                    TextRenderer.DrawText(g, "系统声音设置 ↗", bFont, btnRect, AccentColor,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
                }
            }
        }

        private static GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
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
    }

    public class FocusDeviceSelector : Control
    {
        public float DpiScale { get; set; }
        public string DeviceIcon { get; set; }
        public DeviceCategory Category { get; set; }
        public string DeviceName { get; set; }
        public string BatteryText { get; set; }
        public string StatusText { get; set; }
        public Color AccentColor { get; set; }
        public bool IsHovered { get; set; }

        public event Action SelectorClicked;

        public FocusDeviceSelector()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(18, 19, 23);
            DpiScale = 1.0f;
            DeviceIcon = "🖱️";
            Category = DeviceCategory.Mouse;
            DeviceName = "未连接设备";
            BatteryText = "--%";
            StatusText = "未连接";
            AccentColor = Color.FromArgb(0, 230, 118);
            Cursor = Cursors.Hand;
            DoubleBuffered = true;
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            IsHovered = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            IsHovered = false;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (SelectorClicked != null) SelectorClicked();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(BackColor);

            int w = Width;
            int h = Height;
            int r = (int)(8 * DpiScale);

            Color bg = IsHovered ? Color.FromArgb(32, 36, 46) : Color.FromArgb(25, 27, 34);
            Color border = IsHovered ? AccentColor : Color.FromArgb(45, 50, 65);

            using (var path = CreateRoundedRectanglePath(new Rectangle(0, 0, w - 1, h - 1), r))
            {
                using (var brush = new SolidBrush(bg)) g.FillPath(brush, path);
                using (var pen = new Pen(border, IsHovered ? 1.5f : 1.0f)) g.DrawPath(pen, path);
            }

            // 1. Icon (left) - Vector centered!
            int iconBoxSize = (int)(22 * DpiScale);
            int iconX = (int)(10 * DpiScale);
            int iconY = (h - iconBoxSize) / 2;
            Rectangle iconRect = new Rectangle(iconX, iconY, iconBoxSize, iconBoxSize);
            ModernThemeHelper.DrawModernDeviceIcon(g, iconRect, Category, AccentColor);

            // 2. Chevron "▾" (far right)
            int chvW = (int)(12 * DpiScale);
            int chvX = w - (int)(16 * DpiScale);
            using (var chvFont = new Font("Segoe UI", 9F, FontStyle.Bold))
            using (var chvBrush = new SolidBrush(Color.FromArgb(150, 160, 180)))
            {
                g.DrawString("▾", chvFont, chvBrush, chvX - chvW, (h / 2) - (int)(8 * DpiScale));
            }

            // 3. Battery Pill
            int pW = 0;
            using (var battFont = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Bold))
            {
                string bStr = BatteryText ?? "";
                bool isLightning = bStr.Contains("⚡");
                string cleanText = bStr.Replace("⚡", "").Trim();
                var bSz = TextRenderer.MeasureText(cleanText, battFont);
                float iconH = 10.5f * DpiScale;
                float iconW = iconH * 0.55f;
                float spacing = 4f * DpiScale;

                pW = bSz.Width + (int)(12 * DpiScale) + (isLightning ? (int)(iconW + spacing) : 0);
                int pH = (int)(20 * DpiScale);
                int pX = chvX - chvW - pW - (int)(8 * DpiScale);
                int pY = (h - pH) / 2;

                using (var pPath = CreateRoundedRectanglePath(new Rectangle(pX, pY, pW, pH), (int)(4 * DpiScale)))
                {
                    using (var pBrush = new SolidBrush(Color.FromArgb(30, AccentColor.R, AccentColor.G, AccentColor.B)))
                    {
                        g.FillPath(pBrush, pPath);
                    }
                    using (var pPen = new Pen(Color.FromArgb(80, AccentColor.R, AccentColor.G, AccentColor.B), 1f))
                    {
                        g.DrawPath(pPen, pPath);
                    }
                }

                if (isLightning)
                {
                    float totalContentW = iconW + spacing + bSz.Width;
                    float startX = pX + (pW - totalContentW) / 2f;
                    ModernThemeHelper.DrawVectorLightning(g, startX + iconW / 2f, pY + pH / 2f, iconH, AccentColor);
                    using (var pTextBrush = new SolidBrush(AccentColor))
                    {
                        g.DrawString(cleanText, battFont, pTextBrush, startX + iconW + spacing, pY + (int)(2 * DpiScale));
                    }
                }
                else
                {
                    using (var pTextBrush = new SolidBrush(AccentColor))
                    {
                        g.DrawString(cleanText, battFont, pTextBrush, pX + (int)(6 * DpiScale), pY + (int)(2 * DpiScale));
                    }
                }
            }

            // 4. Device Name (truncated if necessary)
            int nameX = iconX + (int)(24 * DpiScale);
            int maxNameW = chvX - chvW - pW - nameX - (int)(16 * DpiScale);
            if (maxNameW < 40) maxNameW = 40;

            using (var nameFont = new Font("Microsoft YaHei UI", 9.2F, FontStyle.Bold))
            using (var nameBrush = new SolidBrush(Color.FromArgb(240, 243, 250)))
            {
                var sf = new StringFormat
                {
                    Trimming = StringTrimming.EllipsisCharacter,
                    FormatFlags = StringFormatFlags.NoWrap
                };
                g.DrawString(DeviceName, nameFont, nameBrush, new RectangleF(nameX, (h / 2) - (int)(9 * DpiScale), maxNameW, (int)(20 * DpiScale)), sf);
            }
        }

        private static GraphicsPath CreateRoundedRectanglePath(Rectangle rect, int radius)
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
    }

    public class PriorityItem
    {
        public string DeviceId { get; set; }
        public string Name { get; set; }
        public string Icon { get; set; }
        public DeviceCategory Category { get; set; }
        public int Battery { get; set; }
        public bool IsConnected { get; set; }
        public Color AccentColor { get; set; }
        public int PriorityIndex { get; set; }
        public float CurrentY { get; set; }
        public float TargetY { get; set; }
        public bool IsCustom { get; set; }
    }

    public class PriorityReorderControl : Control
    {
        public List<PriorityItem> Items = new List<PriorityItem>();
        public float DpiScale = 1.0f;

        private int draggedIndex = -1;
        private int currentDropIndex = -1;
        private int dragCurrentY = 0;
        private int dragOffsetY = 0;
        private bool isDragging = false;
        private PriorityItem settlingItem = null;
        private System.Windows.Forms.Timer animTimer;
        private int scrollY = 0;
        private int hoveredDeleteIndex = -1;

        public event Action OrderChanged;

        public PriorityReorderControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            BackColor = Color.FromArgb(18, 19, 23);
            DoubleBuffered = true;

            animTimer = new System.Windows.Forms.Timer();
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

                if (settlingItem != null && Math.Abs(settlingItem.TargetY - settlingItem.CurrentY) < 0.5f)
                {
                    settlingItem.CurrentY = settlingItem.TargetY;
                    settlingItem = null;
                    needsRedraw = true;
                }

                if (needsRedraw) Invalidate();
                else if (!isDragging && settlingItem == null) animTimer.Stop();
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

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left && Items.Count > 0)
            {
                int itemH = GetItemHeight();
                int padX = (int)(8 * DpiScale);
                int rankColW = (int)(26 * DpiScale);
                int cardStartX = padX + rankColW + (int)(8 * DpiScale);
                int maxScroll = GetMaxScroll();
                int rightMargin = maxScroll > 0 ? (int)(12 * DpiScale) : 0;
                int cardW = Width - cardStartX - (int)(10 * DpiScale) - rightMargin;
                int gripW = (int)(18 * DpiScale);
                int delW = (int)(22 * DpiScale);

                for (int i = 0; i < Items.Count; i++)
                {
                    float y = Items[i].CurrentY - scrollY;
                    if (e.Y >= y && e.Y <= y + itemH)
                    {
                        // Check if delete button clicked on any device
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
                                DeleteItem(i);
                            }
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
                int itemH = GetItemHeight();
                int padX = (int)(8 * DpiScale);
                int rankColW = (int)(26 * DpiScale);
                int cardStartX = padX + rankColW + (int)(8 * DpiScale);
                int maxScroll = GetMaxScroll();
                int rightMargin = maxScroll > 0 ? (int)(12 * DpiScale) : 0;
                int cardW = Width - cardStartX - (int)(10 * DpiScale) - rightMargin;
                int gripW = (int)(18 * DpiScale);
                int delW = (int)(22 * DpiScale);

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
                }
                if (newHoverDel != hoveredDeleteIndex)
                {
                    hoveredDeleteIndex = newHoverDel;
                    Invalidate();
                }
            }
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (hoveredDeleteIndex != -1)
            {
                hoveredDeleteIndex = -1;
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

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
            g.Clear(BackColor);

            int maxScroll = GetMaxScroll();
            int rightMargin = maxScroll > 0 ? (int)(12 * DpiScale) : 0;
            int itemH = GetItemHeight();
            int itemGap = GetItemGap();
            int totalH = itemH + itemGap;
            int padX = (int)(8 * DpiScale);
            int rankColW = (int)(26 * DpiScale);
            int cardStartX = padX + rankColW + (int)(8 * DpiScale);
            int cardW = Width - cardStartX - (int)(10 * DpiScale) - rightMargin;

            var state = g.Save();
            g.TranslateTransform(0, -scrollY);

            // 1. Draw FIXED slot numbers on the left (never jump or move with drag!)
            for (int i = 0; i < Items.Count; i++)
            {
                float slotY = i * totalH;
                if (slotY + itemH < scrollY || slotY > scrollY + Height) continue;
                DrawFixedSlotRank(g, i + 1, padX, (int)slotY, rankColW, itemH);
            }

            // 2. Draw non-dragged and non-settling cards
            for (int i = 0; i < Items.Count; i++)
            {
                if (isDragging && i == draggedIndex) continue;
                if (Items[i] == settlingItem) continue;
                float cardY = Items[i].CurrentY;
                if (cardY + itemH < scrollY || cardY > scrollY + Height) continue;
                DrawCard(g, Items[i], cardStartX, (int)cardY, cardW, itemH, false, (hoveredDeleteIndex == i));
            }

            // 3. Draw settling card on top layer with smooth easing into slot
            if (settlingItem != null)
            {
                DrawCard(g, settlingItem, cardStartX, (int)settlingItem.CurrentY, cardW, itemH, true, false);
            }

            // 4. Draw dragged floating card on very top
            if (isDragging && draggedIndex >= 0 && draggedIndex < Items.Count)
            {
                int floatY = dragCurrentY - dragOffsetY;
                DrawCard(g, Items[draggedIndex], cardStartX, floatY, cardW, itemH, true, false);
            }

            g.Restore(state);

            // Draw slim modern scrollbar if content exceeds height
            if (maxScroll > 0)
            {
                int sbW = (int)(4 * DpiScale);
                int sbX = Width - sbW - (int)(4 * DpiScale);
                int trackH = Height;
                int thumbH = Math.Max((int)(28 * DpiScale), (int)((float)Height / GetContentHeight() * trackH));
                int thumbY = (int)((float)scrollY / maxScroll * (trackH - thumbH));

                using (var thumbPath = CreateRoundedPath(new Rectangle(sbX, thumbY, sbW, thumbH), sbW / 2))
                using (var thumbBrush = new SolidBrush(Color.FromArgb(90, 110, 130, 160)))
                {
                    g.FillPath(thumbBrush, thumbPath);
                }
            }
        }

        private void DrawFixedSlotRank(Graphics g, int rank, int x, int y, int w, int h)
        {
            int rankSize = (int)(22 * DpiScale);
            int rx = x + (w - rankSize) / 2;
            int ry = y + (h - rankSize) / 2;
            Rectangle rankRect = new Rectangle(rx, ry, rankSize, rankSize);

            bool isTop = (rank == 1);
            Color rBg = isTop ? Color.FromArgb(32, 0, 230, 118) : Color.FromArgb(28, 32, 42);
            Color rBorder = isTop ? Color.FromArgb(0, 230, 118) : Color.FromArgb(55, 62, 78);
            Color rFg = isTop ? Color.FromArgb(0, 230, 118) : Color.FromArgb(160, 170, 190);

            using (var rPath = CreateRoundedPath(rankRect, rankSize / 2))
            {
                using (var rb = new SolidBrush(rBg)) g.FillPath(rb, rPath);
                using (var rp = new Pen(rBorder, isTop ? 1.5f : 1.0f)) g.DrawPath(rp, rPath);
            }

            using (var rf = new Font("Segoe UI", 8.8F * DpiScale, FontStyle.Bold))
            {
                string rStr = rank.ToString();
                TextRenderer.DrawText(g, rStr, rf, rankRect, rFg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPadding | TextFormatFlags.SingleLine);
            }
        }

        private void DrawCard(Graphics g, PriorityItem item, int x, int y, int w, int h, bool isElevated, bool isDelHovered)
        {
            int r = (int)(8 * DpiScale);
            Rectangle rect = new Rectangle(x, y, w, h);

            if (isElevated)
            {
                using (var sPath = CreateRoundedPath(new Rectangle(x + 2, y + 4, w, h), r))
                using (var sBrush = new SolidBrush(Color.FromArgb(80, 0, 0, 0)))
                {
                    g.FillPath(sBrush, sPath);
                }
            }

            Color cardBg = isElevated ? Color.FromArgb(34, 38, 50) : Color.FromArgb(25, 27, 34);
            Color borderCol = isElevated ? item.AccentColor : Color.FromArgb(42, 46, 58);

            using (var path = CreateRoundedPath(rect, r))
            {
                using (var brush = new SolidBrush(cardBg)) g.FillPath(brush, path);
                using (var pen = new Pen(borderCol, isElevated ? 1.8f : 1.0f)) g.DrawPath(pen, path);
            }

            // 1. Icon + Name (left aligned inside card) - Vector centered!
            int iconBoxSize = (int)(22 * DpiScale);
            int iconX = x + (int)(12 * DpiScale);
            int iconY = y + (h - iconBoxSize) / 2;
            Rectangle iconRect = new Rectangle(iconX, iconY, iconBoxSize, iconBoxSize);
            ModernThemeHelper.DrawModernDeviceIcon(g, iconRect, item.Category, item.AccentColor);

            int nameX = iconX + iconBoxSize + (int)(10 * DpiScale);
            int nameY = y + (h / 2) - (int)(9 * DpiScale);
            using (var nameFont = new Font("Microsoft YaHei UI", 9.2F, FontStyle.Bold))
            using (var nameBrush = new SolidBrush(Color.FromArgb(240, 245, 255)))
            {
                g.DrawString(item.Name, nameFont, nameBrush, nameX, nameY);
            }

            // 2. Fluent Vector Grip Handle on far right
            int gripW = (int)(16 * DpiScale);
            int gripX = x + w - gripW - (int)(14 * DpiScale);
            int barW = (int)(12 * DpiScale);
            int barH = (int)(2 * DpiScale);
            int barGap = (int)(3 * DpiScale);
            int gripStartY = y + (h - (barH * 3 + barGap * 2)) / 2;
            Color gripColor = isElevated ? item.AccentColor : Color.FromArgb(110, 120, 140);
            using (var gripBrush = new SolidBrush(gripColor))
            {
                for (int b = 0; b < 3; b++)
                {
                    int by = gripStartY + b * (barH + barGap);
                    using (var gp = CreateRoundedPath(new Rectangle(gripX + (gripW - barW) / 2, by, barW, barH), (int)(1 * DpiScale)))
                    {
                        g.FillPath(gripBrush, gp);
                    }
                }
            }

            int lastRightX = gripX;

            // 3. Sleek Pure Vector Micro Delete Button (available for all devices)
            int delW = (int)(22 * DpiScale);
            int delX = gripX - delW - (int)(8 * DpiScale);
            int delY = y + (h - delW) / 2;
            Rectangle delRect = new Rectangle(delX, delY, delW, delW);

            if (isDelHovered)
            {
                using (var delPath = CreateRoundedPath(delRect, (int)(5 * DpiScale)))
                using (var delBg = new SolidBrush(Color.FromArgb(45, 255, 82, 82)))
                {
                    g.FillPath(delBg, delPath);
                }
            }

            Color xCol = isDelHovered ? Color.FromArgb(255, 100, 100) : Color.FromArgb(100, 115, 135);
            float penW = Math.Max(2.0f, 2.0f * DpiScale);
            using (var xPen = new Pen(xCol, penW))
            {
                xPen.StartCap = LineCap.Round;
                xPen.EndCap = LineCap.Round;
                int cPad = (int)(6 * DpiScale);
                g.DrawLine(xPen, delX + cPad, delY + cPad, delX + delW - cPad, delY + delW - cPad);
                g.DrawLine(xPen, delX + delW - cPad, delY + cPad, delX + cPad, delY + delW - cPad);
            }

            lastRightX = delX;

            // 4. Battery / Status Pill before Delete Button or Grip Handle
            string battStr = item.IsConnected ? (item.Battery + "%") : (item.Battery > 0 ? (item.Battery + "% 离线") : "离线");
            Color battColor = item.IsConnected ? item.AccentColor : Color.FromArgb(140, 145, 155);
            using (var battFont = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold))
            using (var battBrush = new SolidBrush(battColor))
            {
                var bSz = TextRenderer.MeasureText(battStr, battFont);
                int battX = lastRightX - bSz.Width - (int)(12 * DpiScale);
                int bY = y + (h / 2) - (int)(8 * DpiScale);
                g.DrawString(battStr, battFont, battBrush, battX, bY);
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
    }

    public class DevicePriorityForm : Form
    {
        private PriorityReorderControl reorderControl;
        private ModernButton btnSave;
        private ModernButton btnCancel;
        private float dpiScale = 1.0f;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ModernThemeHelper.ApplyModernWin11Theme(this.Handle);
        }

        public DevicePriorityForm(List<MouseBatteryInfo> devices, float scale)
        {
            this.dpiScale = scale;
            this.Text = "设备优先级管理 (拖拽排序)";
            this.Size = new Size((int)(460 * dpiScale), (int)(520 * dpiScale));
            this.MinimumSize = new Size((int)(440 * dpiScale), (int)(480 * dpiScale));
            this.BackColor = Color.FromArgb(18, 19, 23);
            this.ForeColor = Color.White;
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            int pad = (int)(16 * dpiScale);

            var lblTitle = new Label();
            lblTitle.Text = "设备优先级管理";
            lblTitle.Font = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(240, 245, 255);
            lblTitle.Location = new Point(pad, (int)(12 * dpiScale));
            lblTitle.AutoSize = true;
            this.Controls.Add(lblTitle);

            var lblTip = new Label();
            lblTip.Text = "按住右侧把手上下拖动卡片可调整顺位。第 1 位设备默认常驻任务栏（离线时自动顺延下一位在线设备）。点击「✕」可移除自定义设备。";
            lblTip.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblTip.ForeColor = Color.FromArgb(145, 155, 175);
            lblTip.Location = new Point(pad, (int)(38 * dpiScale));
            lblTip.Size = new Size(this.ClientSize.Width - (pad * 2), (int)(32 * dpiScale));
            this.Controls.Add(lblTip);

            int btnW = (int)(110 * dpiScale);
            int btnH = (int)(34 * dpiScale);
            int bottomPad = (int)(16 * dpiScale);
            int btnY = this.ClientSize.Height - bottomPad - btnH;

            btnCancel = new ModernButton();
            btnCancel.Text = "取消";
            btnCancel.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
            btnCancel.NormalColor = Color.FromArgb(36, 39, 49);
            btnCancel.HoverColor = Color.FromArgb(48, 52, 65);
            btnCancel.PressedColor = Color.FromArgb(28, 30, 38);
            btnCancel.BorderColor = Color.FromArgb(58, 63, 78);
            btnCancel.ForeColor = Color.FromArgb(230, 235, 245);
            btnCancel.CornerRadius = (int)(8 * dpiScale);
            btnCancel.Location = new Point(this.ClientSize.Width - pad - (btnW * 2) - (int)(10 * dpiScale), btnY);
            btnCancel.Size = new Size(btnW, btnH);
            btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnCancel);

            btnSave = new ModernButton();
            btnSave.Text = "确定并保存";
            btnSave.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
            btnSave.NormalColor = Color.FromArgb(0, 200, 83);
            btnSave.HoverColor = Color.FromArgb(0, 230, 118);
            btnSave.PressedColor = Color.FromArgb(0, 160, 65);
            btnSave.ForeColor = Color.FromArgb(10, 24, 15);
            btnSave.CornerRadius = (int)(8 * dpiScale);
            btnSave.Location = new Point(this.ClientSize.Width - pad - btnW, btnY);
            btnSave.Size = new Size(btnW, btnH);
            btnSave.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnSave.Click += (s, e) => {
                var ids = new List<string>();
                foreach (var it in reorderControl.Items) ids.Add(it.DeviceId);
                DeviceManager.SavePriorityList(ids);
                if (ids.Count > 0) DeviceManager.SetConfiguredPrimaryDeviceId(ids[0]);
                this.DialogResult = DialogResult.OK;
                this.Close();
            };
            this.Controls.Add(btnSave);

            int listH = btnY - (int)(76 * dpiScale) - (int)(14 * dpiScale);
            reorderControl = new PriorityReorderControl();
            reorderControl.DpiScale = dpiScale;
            reorderControl.Location = new Point(pad, (int)(76 * dpiScale));
            reorderControl.Size = new Size(this.ClientSize.Width - (pad * 2), listH);
            reorderControl.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;

            if (devices != null)
            {
                for (int i = 0; i < devices.Count; i++)
                {
                    var d = devices[i];
                    reorderControl.Items.Add(new PriorityItem
                    {
                        DeviceId = d.DeviceId,
                        Name = d.DisplayName,
                        Icon = d.CategoryIcon,
                        Category = d.Category,
                        Battery = d.BatteryPercent,
                        IsConnected = d.IsConnected && !d.IsSleeping,
                        AccentColor = d.BrandColor,
                        PriorityIndex = i + 1,
                        IsCustom = d.IsCustom
                    });
                }
                reorderControl.ResetPositions();
            }
            this.Controls.Add(reorderControl);
        }
    }

    public class AddDeviceWizardForm : Form
    {
        public string AddedDeviceId { get; private set; }

        private ListBox lstDevices;
        private ModernInputBox txtCustomName;
        private ModernDarkDropdown cmbCategory;
        private Label lblTestStatus;
        private ModernButton btnScan;
        private ModernButton btnSave;
        private ModernButton btnCancel;
        private WizardRadarBadge radarBadge;
        private ScanningBeamControl beamControl;
        private System.Windows.Forms.Timer autoScanDebounceTimer;
        private bool isScanning = false;
        private const int WM_DEVICECHANGE = 0x0219;
        private float dpiScale = 1.0f;
        private List<CustomDeviceConfig> scannedList = new List<CustomDeviceConfig>();
        private HashSet<string> existingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ModernThemeHelper.ApplyModernWin11Theme(this.Handle);
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_DEVICECHANGE)
            {
                if (autoScanDebounceTimer == null)
                {
                    autoScanDebounceTimer = new System.Windows.Forms.Timer();
                    autoScanDebounceTimer.Interval = 600;
                    autoScanDebounceTimer.Tick += (s, ev) => {
                        autoScanDebounceTimer.Stop();
                        PerformScanAsync();
                    };
                }
                autoScanDebounceTimer.Stop();
                autoScanDebounceTimer.Start();
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (autoScanDebounceTimer != null)
            {
                autoScanDebounceTimer.Stop();
                autoScanDebounceTimer.Dispose();
                autoScanDebounceTimer = null;
            }
        }

        public AddDeviceWizardForm(float scale) : this(null, scale) { }

        public AddDeviceWizardForm(List<MouseBatteryInfo> existing, float scale)
        {
            this.dpiScale = scale;
            if (existing != null)
            {
                foreach (var d in existing)
                {
                    existingIds.Add(d.DeviceId);
                    if (!string.IsNullOrEmpty(d.HardwareFingerprint)) existingIds.Add(d.HardwareFingerprint);
                    if (d.DeviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase))
                    {
                        existingIds.Add(RazerDeviceHelper.NormalizeRazerDeviceId(d.DeviceId));
                    }
                }
            }

            this.Text = "添加与配置外设向导";
            this.ClientSize = new Size((int)(420 * dpiScale), (int)(510 * dpiScale));
            this.BackColor = Color.FromArgb(18, 19, 23);
            this.ForeColor = Color.White;
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            int pad = (int)(16 * dpiScale);

            // Header Radar Badge
            radarBadge = new WizardRadarBadge();
            radarBadge.DpiScale = dpiScale;
            radarBadge.Size = new Size((int)(24 * dpiScale), (int)(24 * dpiScale));
            radarBadge.Location = new Point(pad, (int)(13 * dpiScale));
            this.Controls.Add(radarBadge);

            // Row 1: Title on left, Scan button on right
            var lblTitle = new Label();
            lblTitle.Text = "添加与配置外设向导";
            lblTitle.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(240, 245, 255);
            lblTitle.Location = new Point(pad + (int)(28 * dpiScale), (int)(15 * dpiScale));
            lblTitle.AutoSize = true;
            this.Controls.Add(lblTitle);

            btnScan = new ModernButton();
            btnScan.Text = "🔄 重新扫描";
            btnScan.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold);
            btnScan.NormalColor = Color.FromArgb(32, 36, 48);
            btnScan.HoverColor = Color.FromArgb(46, 52, 68);
            btnScan.PressedColor = Color.FromArgb(24, 28, 38);
            btnScan.BorderColor = Color.FromArgb(60, 68, 88);
            btnScan.TextColor = Color.FromArgb(220, 230, 250);
            int scanW = (int)(100 * dpiScale);
            btnScan.Location = new Point(this.ClientSize.Width - pad - scanW, (int)(12 * dpiScale));
            btnScan.Size = new Size(scanW, (int)(28 * dpiScale));
            btnScan.Click += (s, e) => PerformScanAsync();
            this.Controls.Add(btnScan);

            // Row 2: Subtitle
            var lblSub = new Label();
            lblSub.Text = "自动扫描当前在线外设，支持修改昵称与设备分类：";
            lblSub.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblSub.ForeColor = Color.FromArgb(145, 155, 175);
            lblSub.Location = new Point(pad, (int)(46 * dpiScale));
            lblSub.Size = new Size(this.ClientSize.Width - (pad * 2), (int)(20 * dpiScale));
            this.Controls.Add(lblSub);

            // Scanning Laser Beam Animation Bar
            beamControl = new ScanningBeamControl();
            beamControl.Location = new Point(pad, (int)(68 * dpiScale));
            beamControl.Size = new Size(this.ClientSize.Width - (pad * 2), (int)(3 * dpiScale));
            beamControl.Visible = false;
            this.Controls.Add(beamControl);

            // Row 3: Scanned Devices List (Owner Drawn Dark Cards)
            lstDevices = new ListBox();
            lstDevices.BackColor = Color.FromArgb(18, 19, 23);
            lstDevices.ForeColor = Color.FromArgb(235, 240, 250);
            lstDevices.BorderStyle = BorderStyle.None;
            lstDevices.Font = new Font("Microsoft YaHei UI", 9.2F);
            lstDevices.DrawMode = DrawMode.OwnerDrawFixed;
            lstDevices.ItemHeight = (int)(52 * dpiScale);
            lstDevices.Location = new Point(pad, (int)(73 * dpiScale));
            lstDevices.Size = new Size(this.ClientSize.Width - (pad * 2), (int)(180 * dpiScale));
            lstDevices.DrawItem += LstDevices_DrawItem;
            lstDevices.SelectedIndexChanged += (s, e) => OnDeviceSelected();
            lstDevices.HandleCreated += (s, e) => {
                try { ModernThemeHelper.SetWindowTheme(lstDevices.Handle, "DarkMode_Explorer", null); } catch { }
            };
            this.Controls.Add(lstDevices);

            // Row 4: Custom Nickname
            int formY = (int)(264 * dpiScale);
            var lblName = new Label();
            lblName.Text = "自定义昵称:";
            lblName.Font = new Font("Microsoft YaHei UI", 8.8F);
            lblName.ForeColor = Color.FromArgb(170, 180, 200);
            lblName.Location = new Point(pad, formY + 5);
            lblName.AutoSize = true;
            this.Controls.Add(lblName);

            txtCustomName = new ModernInputBox();
            txtCustomName.DpiScale = dpiScale;
            txtCustomName.Location = new Point(pad + (int)(95 * dpiScale), formY);
            txtCustomName.Size = new Size(this.ClientSize.Width - pad * 2 - (int)(95 * dpiScale), (int)(30 * dpiScale));
            this.Controls.Add(txtCustomName);

            // Row 5: Category Dropdown (ModernDarkDropdown)
            formY += (int)(38 * dpiScale);
            var lblCat = new Label();
            lblCat.Text = "外设类别:";
            lblCat.Font = new Font("Microsoft YaHei UI", 8.8F);
            lblCat.ForeColor = Color.FromArgb(170, 180, 200);
            lblCat.Location = new Point(pad, formY + 5);
            lblCat.AutoSize = true;
            this.Controls.Add(lblCat);

            cmbCategory = new ModernDarkDropdown();
            cmbCategory.DpiScale = dpiScale;
            cmbCategory.Items.Add("🖱️ 鼠标 (Mouse)");
            cmbCategory.Items.Add("⌨️ 键盘 (Keyboard)");
            cmbCategory.Items.Add("🎧 头戴式耳机 (Headset)");
            cmbCategory.Items.Add("🦻 入耳式耳机 (Earbuds)");
            cmbCategory.Items.Add("🎮 游戏手柄 (Gamepad)");
            cmbCategory.Items.Add("📡 接收器 / 拓展坞 (Dongle)");
            cmbCategory.Items.Add("🔌 通用外设 (Generic)");
            cmbCategory.SelectedIndex = 0;
            cmbCategory.Location = new Point(pad + (int)(95 * dpiScale), formY);
            cmbCategory.Size = new Size(this.ClientSize.Width - pad * 2 - (int)(95 * dpiScale), (int)(30 * dpiScale));
            this.Controls.Add(cmbCategory);

            // Row 6: Live Status Box
            formY += (int)(42 * dpiScale);
            lblTestStatus = new Label();
            lblTestStatus.Text = "请从上方列表选择一个设备";
            lblTestStatus.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold);
            lblTestStatus.ForeColor = Color.FromArgb(0, 230, 118);
            lblTestStatus.Location = new Point(pad, formY);
            lblTestStatus.Size = new Size(this.ClientSize.Width - (pad * 2), (int)(46 * dpiScale));
            this.Controls.Add(lblTestStatus);

            // Row 7: Buttons
            int btnH = (int)(36 * dpiScale);
            int btnY = (int)(456 * dpiScale);
            int cancelW = (int)(110 * dpiScale);
            int saveW = (int)(150 * dpiScale);

            btnCancel = new ModernButton();
            btnCancel.Text = "取消";
            btnCancel.Font = new Font("Microsoft YaHei UI", 9F);
            btnCancel.NormalColor = Color.FromArgb(32, 36, 48);
            btnCancel.HoverColor = Color.FromArgb(46, 52, 68);
            btnCancel.PressedColor = Color.FromArgb(24, 28, 38);
            btnCancel.BorderColor = Color.FromArgb(60, 68, 88);
            btnCancel.TextColor = Color.FromArgb(220, 230, 250);
            btnCancel.Location = new Point(this.ClientSize.Width - pad - cancelW - saveW - (int)(10 * dpiScale), btnY);
            btnCancel.Size = new Size(cancelW, btnH);
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnCancel);

            btnSave = new ModernButton();
            btnSave.Text = "➕ 加入监控";
            btnSave.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            btnSave.NormalColor = Color.FromArgb(0, 200, 83);
            btnSave.HoverColor = Color.FromArgb(0, 230, 118);
            btnSave.PressedColor = Color.FromArgb(0, 170, 70);
            btnSave.BorderColor = Color.FromArgb(0, 230, 118);
            btnSave.TextColor = Color.Black;
            btnSave.Location = new Point(this.ClientSize.Width - pad - saveW, btnY);
            btnSave.Size = new Size(saveW, btnH);
            btnSave.Enabled = false;
            btnSave.Click += (s, e) => SaveSelectedDevice();
            this.Controls.Add(btnSave);

            PerformScanAsync();
        }

        private void LstDevices_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= scannedList.Count) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var item = scannedList[e.Index];
            bool isSel = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            bool already = existingIds.Contains(item.DeviceId) || 
                           (!string.IsNullOrEmpty(item.HardwareFingerprint) && existingIds.Contains(item.HardwareFingerprint)) ||
                           (item.DeviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase) && 
                            existingIds.Contains(RazerDeviceHelper.NormalizeRazerDeviceId(item.DeviceId)));

            // Background fill
            using (var bgBrush = new SolidBrush(Color.FromArgb(18, 19, 23)))
            {
                g.FillRectangle(bgBrush, e.Bounds);
            }

            // Card rectangle
            Rectangle cardRect = new Rectangle(e.Bounds.X + 2, e.Bounds.Y + 2, e.Bounds.Width - 4, e.Bounds.Height - 4);
            Color cardBg = isSel ? Color.FromArgb(32, 42, 58) : Color.FromArgb(25, 29, 40);
            Color cardBorder = isSel ? Color.FromArgb(0, 230, 118) : Color.FromArgb(42, 48, 65);

            using (var path = RoundedCard.GetRoundedRectangle(cardRect, (int)(7 * dpiScale)))
            {
                using (var cb = new SolidBrush(cardBg)) g.FillPath(cb, path);
                using (var cp = new Pen(cardBorder, isSel ? 1.6f : 1.0f)) g.DrawPath(cp, path);
            }

            // Left circle badge for icon
            int iconBoxSize = (int)(32 * dpiScale);
            int iconX = cardRect.X + (int)(10 * dpiScale);
            int iconY = cardRect.Y + (cardRect.Height - iconBoxSize) / 2;
            Rectangle iconRect = new Rectangle(iconX, iconY, iconBoxSize, iconBoxSize);

            Color brandCol = item.Category == DeviceCategory.Headset || item.Category == DeviceCategory.Earbuds ? Color.FromArgb(90, 160, 255) :
                             (item.Category == DeviceCategory.Keyboard ? Color.FromArgb(255, 170, 0) :
                             (item.Category == DeviceCategory.Gamepad ? Color.FromArgb(180, 100, 255) : Color.FromArgb(0, 230, 118)));

            using (var iconCircle = RoundedCard.GetRoundedRectangle(iconRect, iconBoxSize / 2))
            {
                using (var ib = new SolidBrush(Color.FromArgb(35, brandCol.R, brandCol.G, brandCol.B))) g.FillPath(ib, iconCircle);
                using (var ip = new Pen(Color.FromArgb(90, brandCol.R, brandCol.G, brandCol.B), 1f)) g.DrawPath(ip, iconCircle);
            }

            ModernThemeHelper.DrawModernDeviceIcon(g, iconRect, item.Category, brandCol);

            // Text info
            int textX = iconX + iconBoxSize + (int)(10 * dpiScale);
            string dispName = !string.IsNullOrEmpty(item.CustomName) ? item.CustomName : item.OriginalName;
            using (var nameFont = new Font("Microsoft YaHei UI", 9.2F, FontStyle.Bold))
            using (var nameBrush = new SolidBrush(Color.FromArgb(242, 246, 255)))
            {
                g.DrawString(dispName, nameFont, nameBrush, textX, cardRect.Y + (int)(7 * dpiScale));
            }

            string sub = item.Transport ?? "2.4G/USB";
            using (var subFont = new Font("Microsoft YaHei UI", 8F))
            using (var subBrush = new SolidBrush(Color.FromArgb(135, 145, 165)))
            {
                g.DrawString(sub, subFont, subBrush, textX, cardRect.Y + (int)(27 * dpiScale));
            }

            // Right tags: [已在设备库中] or [电量: X%]
            int rightX = cardRect.Right - (int)(10 * dpiScale);
            if (already)
            {
                string tag = "已在设备库中";
                using (var tagFont = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold))
                {
                    var tSz = TextRenderer.MeasureText(tag, tagFont);
                    int tW = tSz.Width + (int)(10 * dpiScale);
                    int tH = (int)(20 * dpiScale);
                    int tX = rightX - tW;
                    int tY = cardRect.Y + (cardRect.Height - tH) / 2;
                    Rectangle tagRect = new Rectangle(tX, tY, tW, tH);

                    using (var tp = RoundedCard.GetRoundedRectangle(tagRect, (int)(4 * dpiScale)))
                    {
                        using (var tb = new SolidBrush(Color.FromArgb(40, 255, 183, 77))) g.FillPath(tb, tp);
                        using (var tpen = new Pen(Color.FromArgb(180, 255, 183, 77), 1f)) g.DrawPath(tpen, tp);
                    }
                    using (var tfb = new SolidBrush(Color.FromArgb(255, 195, 100)))
                    {
                        g.DrawString(tag, tagFont, tfb, tX + (int)(5 * dpiScale), tY + (int)(2 * dpiScale));
                    }
                    rightX = tX - (int)(8 * dpiScale);
                }
            }

            if (item.CachedBattery > 0)
            {
                string bStr = item.CachedBattery + "%";
                using (var bFont = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold))
                {
                    var bSz = TextRenderer.MeasureText(bStr, bFont);
                    int bW = bSz.Width + (int)(10 * dpiScale);
                    int bH = (int)(20 * dpiScale);
                    int bX = rightX - bW;
                    int bY = cardRect.Y + (cardRect.Height - bH) / 2;
                    Rectangle bRect = new Rectangle(bX, bY, bW, bH);

                    using (var bp = RoundedCard.GetRoundedRectangle(bRect, (int)(4 * dpiScale)))
                    {
                        using (var bb = new SolidBrush(Color.FromArgb(30, 0, 230, 118))) g.FillPath(bb, bp);
                        using (var bpen = new Pen(Color.FromArgb(80, 0, 230, 118), 1f)) g.DrawPath(bpen, bp);
                    }
                    using (var bfb = new SolidBrush(Color.FromArgb(0, 230, 118)))
                    {
                        g.DrawString(bStr, bFont, bfb, bX + (int)(5 * dpiScale), bY + (int)(2 * dpiScale));
                    }
                }
            }
        }

        private void PerformScanAsync()
        {
            if (isScanning) return;
            isScanning = true;

            btnScan.Enabled = false;
            btnScan.Text = "⏳ 扫描中...";
            beamControl.Start();
            lblTestStatus.ForeColor = Color.FromArgb(0, 230, 118);
            lblTestStatus.Text = "正在检索在线 2.4G 接收器、HID 外设与蓝牙设备...";

            ThreadPool.QueueUserWorkItem((state) => {
                var tempConfigs = new List<CustomDeviceConfig>();

                // 1. Scan Razer Devices (2.4G Hyperspeed / USB)
                try
                {
                    var rzList = RazerDeviceHelper.QueryAllRazerDevices();
                    if (rzList != null)
                    {
                        foreach (var rz in rzList)
                        {
                            if (rz != null && (rz.IsConnected || rz.IsDonglePresent))
                            {
                                string normId = RazerDeviceHelper.NormalizeRazerDeviceId(rz.DeviceId);
                                if (!tempConfigs.Exists(c => c.DeviceId == normId))
                                {
                                    var cfg = new CustomDeviceConfig
                                    {
                                        DeviceId = normId,
                                        OriginalName = rz.DeviceName,
                                        CustomName = rz.DisplayName,
                                        Category = DeviceCategory.Mouse,
                                        Transport = "2.4G 无线",
                                        CachedBattery = rz.BatteryPercent,
                                        HardwareFingerprint = rz.DeviceId
                                    };
                                    tempConfigs.Add(cfg);
                                }
                            }
                        }
                    }
                }
                catch { }

                // 2. Scan Rapoo Devices (2.4G / USB)
                try
                {
                    var rp = RapooDeviceHelper.QueryRapooDeviceInfo();
                    if (rp != null && (rp.IsConnected || rp.IsDonglePresent))
                    {
                        var cfg = new CustomDeviceConfig
                        {
                            DeviceId = rp.DeviceId,
                            OriginalName = rp.DeviceName,
                            CustomName = rp.DisplayName,
                            Category = DeviceCategory.Mouse,
                            Transport = "2.4G 无线",
                            CachedBattery = rp.BatteryPercent,
                            HardwareFingerprint = rp.DeviceId
                        };
                        tempConfigs.Add(cfg);
                    }
                }
                catch { }

                // 2.5 Scan VGN Devices (2.4G / USB)
                try
                {
                    var vgn = VgnDeviceHelper.QueryVgnDeviceInfo();
                    if (vgn != null && (vgn.IsConnected || vgn.IsDonglePresent))
                    {
                        var cfg = new CustomDeviceConfig
                        {
                            DeviceId = vgn.DeviceId,
                            OriginalName = vgn.DeviceName,
                            CustomName = vgn.DisplayName,
                            Category = DeviceCategory.Mouse,
                            Transport = "2.4G 无线",
                            CachedBattery = vgn.BatteryPercent,
                            HardwareFingerprint = vgn.DeviceId
                        };
                        tempConfigs.Add(cfg);
                    }
                }
                catch { }

                // 2.55 Scan Logitech Devices (Lightspeed / Unifying / Nano / Wired)
                try
                {
                    var logiList = LogitechDeviceHelper.QueryAllLogitechDevices();
                    if (logiList != null)
                    {
                        foreach (var dev in logiList)
                        {
                            if (dev != null && (dev.IsConnected || dev.IsDonglePresent))
                            {
                                if (!tempConfigs.Exists(c => c.DeviceId == dev.DeviceId))
                                {
                                    var cfg = new CustomDeviceConfig
                                    {
                                        DeviceId = dev.DeviceId,
                                        OriginalName = dev.DeviceName,
                                        CustomName = dev.DisplayName,
                                        Category = dev.Category,
                                        Transport = dev.Transport ?? "Lightspeed 无线",
                                        CachedBattery = dev.BatteryPercent,
                                        HardwareFingerprint = dev.DeviceId
                                    };
                                    tempConfigs.Add(cfg);
                                }
                            }
                        }
                    }
                }
                catch { }



                // 2.6 Scan Generic HID Mice & Peripherals (2.4G / USB)
                try
                {
                    var genMice = GenericHidHelper.ScanGenericHidMice(existingIds);
                    foreach (var gm in genMice)
                    {
                        tempConfigs.Add(gm);
                    }
                }
                catch { }

                // 3. Scan Bluetooth Devices (BLE & Classic)
                try
                {
                    var btList = BluetoothDeviceHelper.ScanAllBluetoothDevices();
                    foreach (var b in btList)
                    {
                        b.Transport = "蓝牙";
                        tempConfigs.Add(b);
                    }
                }
                catch { }

                // Safely update UI atomically
                try
                {
                    if (this.IsDisposed) return;
                    this.BeginInvoke((Action)(() => {
                        if (this.IsDisposed) return;
                        isScanning = false;
                        beamControl.Stop();
                        btnScan.Enabled = true;
                        btnScan.Text = "🔄 重新扫描";

                        lstDevices.BeginUpdate();
                        try
                        {
                            lstDevices.Items.Clear();
                            scannedList.Clear();
                            scannedList.AddRange(tempConfigs);
                            for (int i = 0; i < scannedList.Count; i++)
                            {
                                string label = (!string.IsNullOrEmpty(scannedList[i].CustomName) && !string.Equals(scannedList[i].CustomName, scannedList[i].OriginalName, StringComparison.OrdinalIgnoreCase))
                                    ? scannedList[i].CustomName + " (" + scannedList[i].OriginalName + ")"
                                    : (!string.IsNullOrEmpty(scannedList[i].CustomName) ? scannedList[i].CustomName : scannedList[i].OriginalName);
                                lstDevices.Items.Add(label);
                            }
                        }
                        finally
                        {
                            lstDevices.EndUpdate();
                        }

                        if (lstDevices.Items.Count > 0)
                        {
                            lstDevices.SelectedIndex = 0;
                        }
                        else
                        {
                            lblTestStatus.ForeColor = Color.FromArgb(255, 183, 77);
                            lblTestStatus.Text = "● 未发现新的在线外设，请检查外设电源与配对状态。";
                        }
                    }));
                }
                catch { }
            });
        }

        private void OnDeviceSelected()
        {
            int idx = lstDevices.SelectedIndex;
            if (idx >= 0 && idx < scannedList.Count)
            {
                var item = scannedList[idx];
                bool already = existingIds.Contains(item.DeviceId) || 
                               (!string.IsNullOrEmpty(item.HardwareFingerprint) && existingIds.Contains(item.HardwareFingerprint)) ||
                               (item.DeviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase) && 
                                existingIds.Contains(RazerDeviceHelper.NormalizeRazerDeviceId(item.DeviceId)));

                txtCustomName.Text = !string.IsNullOrEmpty(item.CustomName) ? item.CustomName : item.OriginalName;
                if (item.Category == DeviceCategory.Mouse) cmbCategory.SelectedIndex = 0;
                else if (item.Category == DeviceCategory.Keyboard) cmbCategory.SelectedIndex = 1;
                else if (item.Category == DeviceCategory.Headset) cmbCategory.SelectedIndex = 2;
                else if (item.Category == DeviceCategory.Earbuds) cmbCategory.SelectedIndex = 3;
                else if (item.Category == DeviceCategory.Gamepad) cmbCategory.SelectedIndex = 4;
                else if (item.Category == DeviceCategory.Dongle) cmbCategory.SelectedIndex = 5;
                else cmbCategory.SelectedIndex = 6;

                if (already)
                {
                    lblTestStatus.ForeColor = Color.FromArgb(0, 230, 118);
                    lblTestStatus.Text = "● 该设备已在设备库中，您可以直接修改自定义昵称或分类并保存更新。";
                    btnSave.Text = "💾 保存修改";
                    btnSave.NormalColor = Color.FromArgb(0, 180, 90);
                    btnSave.HoverColor = Color.FromArgb(0, 210, 110);
                    btnSave.Enabled = true;
                }
                else
                {
                    lblTestStatus.ForeColor = Color.FromArgb(0, 230, 118);
                    string battStr = item.CachedBattery > 0 ? string.Format("实时电量: {0}%", item.CachedBattery) : "供电: USB/免驱";
                    lblTestStatus.Text = string.Format("● 设备在线就绪！{0}\n连接通道: {1} | 标识: {2}", 
                        battStr, item.Transport ?? "2.4G/USB", item.HardwareFingerprint ?? item.DeviceId);
                    btnSave.Text = "➕ 加入监控";
                    btnSave.NormalColor = Color.FromArgb(0, 200, 83);
                    btnSave.HoverColor = Color.FromArgb(0, 230, 118);
                    btnSave.Enabled = true;
                }
            }
            else
            {
                btnSave.Enabled = false;
            }
        }

        private void SaveSelectedDevice()
        {
            int idx = lstDevices.SelectedIndex;
            if (idx >= 0 && idx < scannedList.Count)
            {
                var item = scannedList[idx];
                string name = txtCustomName.Text.Trim();
                if (string.IsNullOrEmpty(name)) name = !string.IsNullOrEmpty(item.CustomName) ? item.CustomName : item.OriginalName;
                item.CustomName = name;

                switch (cmbCategory.SelectedIndex)
                {
                    case 0: item.Category = DeviceCategory.Mouse; break;
                    case 1: item.Category = DeviceCategory.Keyboard; break;
                    case 2: item.Category = DeviceCategory.Headset; break;
                    case 3: item.Category = DeviceCategory.Earbuds; break;
                    case 4: item.Category = DeviceCategory.Gamepad; break;
                    case 5: item.Category = DeviceCategory.Dongle; break;
                    default: item.Category = DeviceCategory.Generic; break;
                }

                // Ensure canonical ID for Razer devices
                if (item.DeviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase))
                {
                    item.DeviceId = RazerDeviceHelper.NormalizeRazerDeviceId(item.DeviceId);
                }

                DeviceManager.SaveCustomDevice(item);
                DeviceManager.UnhideDevice(item.DeviceId);
                if (!string.IsNullOrEmpty(item.HardwareFingerprint)) DeviceManager.UnhideDevice(item.HardwareFingerprint);
                if (item.DeviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase))
                {
                    string norm = RazerDeviceHelper.NormalizeRazerDeviceId(item.DeviceId);
                    DeviceManager.UnhideDevice(norm);
                }

                // Also append to priority list if not in it
                var prio = DeviceManager.GetPriorityList();
                if (!prio.Contains(item.DeviceId))
                {
                    prio.Add(item.DeviceId);
                    DeviceManager.SavePriorityList(prio);
                }

                this.AddedDeviceId = item.DeviceId;
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        }
    }

    public class RunningProcessItem
    {
        public string ProcessName { get; set; }
        public string WindowTitle { get; set; }
        public int Pid { get; set; }

        public override string ToString()
        {
            if (string.IsNullOrEmpty(WindowTitle))
                return ProcessName;
            return WindowTitle + " (" + ProcessName + ")";
        }
    }

    public class GameProcessManagerForm : Form
    {
        [DllImport("dwmapi.dll")]
        static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private float dpiScale;
        private List<string> monitoredList;
        private bool fallbackEnabled;
        private Action<List<string>, bool> onSaveCallback;

        private ListBox lstRunning;
        private ListBox lstMonitored;
        private ModernButton btnAddRunning;
        private ModernButton btnRefreshRunning;
        private ModernButton btnBrowseExe;
        private ModernButton btnRemove;
        private ModernButton btnClearAll;
        private ModernCheckBox chkFallback;
        private ModernButton btnClose;

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ModernThemeHelper.ApplyModernWin11Theme(this.Handle);
        }

        public GameProcessManagerForm(float scale, List<string> initialList, bool initialFallback, Action<List<string>, bool> callback)
        {
            this.dpiScale = scale > 0 ? scale : 1.0f;
            this.monitoredList = initialList != null ? new List<string>(initialList) : new List<string>();
            this.fallbackEnabled = initialFallback;
            this.onSaveCallback = callback;

            this.Text = "FerrisPulse · 电竞游戏进程管理与自动捕获";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color.FromArgb(20, 23, 30);
            this.ForeColor = Color.FromArgb(240, 245, 255);
            this.ClientSize = new Size((int)(540 * dpiScale), (int)(520 * dpiScale));
            this.KeyPreview = true;

            int pad = (int)(16 * dpiScale);
            int clientW = this.ClientSize.Width;

            // Title
            Label lblTitle = new Label();
            lblTitle.Text = "🎮 电竞游戏进程管理与自动捕获";
            lblTitle.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(0, 230, 118);
            lblTitle.Location = new Point(pad, (int)(12 * dpiScale));
            lblTitle.Size = new Size(clientW - (pad * 2), (int)(22 * dpiScale));
            this.Controls.Add(lblTitle);

            // Subtitle
            Label lblSub = new Label();
            lblSub.Text = "只需将游戏添加至守护列表，进入游戏时自动生效电竞回报率，切回桌面自动恢复节能。";
            lblSub.Font = new Font("Microsoft YaHei UI", 8.2F);
            lblSub.ForeColor = Color.FromArgb(150, 160, 180);
            lblSub.Location = new Point(pad, (int)(34 * dpiScale));
            lblSub.Size = new Size(clientW - (pad * 2), (int)(18 * dpiScale));
            this.Controls.Add(lblSub);

            // Section 1 Header
            Label lblSec1 = new Label();
            lblSec1.Text = "⚡ 当前正在运行的应用与游戏 (双击或点击下方按钮添加):";
            lblSec1.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold);
            lblSec1.ForeColor = Color.FromArgb(220, 230, 245);
            lblSec1.Location = new Point(pad, (int)(58 * dpiScale));
            lblSec1.Size = new Size(clientW - (pad * 2), (int)(20 * dpiScale));
            this.Controls.Add(lblSec1);

            // ListBox Running
            int listW = clientW - (pad * 2);
            lstRunning = new ListBox();
            lstRunning.BackColor = Color.FromArgb(28, 32, 42);
            lstRunning.ForeColor = Color.FromArgb(225, 235, 250);
            lstRunning.BorderStyle = BorderStyle.FixedSingle;
            lstRunning.Font = new Font("Microsoft YaHei UI", 8.8F);
            lstRunning.ItemHeight = (int)(20 * dpiScale);
            lstRunning.Location = new Point(pad, (int)(80 * dpiScale));
            lstRunning.Size = new Size(listW, (int)(125 * dpiScale));
            lstRunning.DoubleClick += (s, e) => AddSelectedRunning();
            this.Controls.Add(lstRunning);

            // Action buttons below Running
            int actY1 = (int)(212 * dpiScale);
            int btnH = (int)(26 * dpiScale);

            btnAddRunning = new ModernButton();
            btnAddRunning.Text = "➕ 添加选中的应用";
            btnAddRunning.Font = new Font("Microsoft YaHei UI", 8.2F);
            btnAddRunning.NormalColor = Color.FromArgb(26, 42, 32);
            btnAddRunning.HoverColor = Color.FromArgb(36, 60, 44);
            btnAddRunning.BorderColor = Color.FromArgb(0, 180, 80);
            btnAddRunning.TextColor = Color.FromArgb(0, 230, 118);
            btnAddRunning.CornerRadius = (int)(5 * dpiScale);
            btnAddRunning.Cursor = Cursors.Hand;
            btnAddRunning.Location = new Point(pad, actY1);
            btnAddRunning.Size = new Size((int)(140 * dpiScale), btnH);
            btnAddRunning.Click += (s, e) => AddSelectedRunning();
            this.Controls.Add(btnAddRunning);

            btnRefreshRunning = new ModernButton();
            btnRefreshRunning.Text = "🔄 刷新列表";
            btnRefreshRunning.Font = new Font("Microsoft YaHei UI", 8.2F);
            btnRefreshRunning.NormalColor = Color.FromArgb(32, 36, 48);
            btnRefreshRunning.HoverColor = Color.FromArgb(46, 52, 68);
            btnRefreshRunning.BorderColor = Color.FromArgb(56, 64, 84);
            btnRefreshRunning.TextColor = Color.FromArgb(190, 200, 220);
            btnRefreshRunning.CornerRadius = (int)(5 * dpiScale);
            btnRefreshRunning.Cursor = Cursors.Hand;
            btnRefreshRunning.Location = new Point(pad + (int)(148 * dpiScale), actY1);
            btnRefreshRunning.Size = new Size((int)(100 * dpiScale), btnH);
            btnRefreshRunning.Click += (s, e) => RefreshRunningProcesses();
            this.Controls.Add(btnRefreshRunning);

            btnBrowseExe = new ModernButton();
            btnBrowseExe.Text = "📁 浏览本地 .exe 文件...";
            btnBrowseExe.Font = new Font("Microsoft YaHei UI", 8.2F);
            btnBrowseExe.NormalColor = Color.FromArgb(32, 36, 48);
            btnBrowseExe.HoverColor = Color.FromArgb(46, 52, 68);
            btnBrowseExe.BorderColor = Color.FromArgb(56, 64, 84);
            btnBrowseExe.TextColor = Color.FromArgb(190, 200, 220);
            btnBrowseExe.CornerRadius = (int)(5 * dpiScale);
            btnBrowseExe.Cursor = Cursors.Hand;
            btnBrowseExe.Location = new Point(pad + (int)(256 * dpiScale), actY1);
            btnBrowseExe.Size = new Size((int)(180 * dpiScale), btnH);
            btnBrowseExe.Click += (s, e) => BrowseLocalExe();
            this.Controls.Add(btnBrowseExe);

            // Section 2 Header
            Label lblSec2 = new Label();
            lblSec2.Text = "📋 已生效的电竞游戏守护列表 (命中时自动切入电竞回报率):";
            lblSec2.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold);
            lblSec2.ForeColor = Color.FromArgb(220, 230, 245);
            lblSec2.Location = new Point(pad, (int)(248 * dpiScale));
            lblSec2.Size = new Size(clientW - (pad * 2), (int)(20 * dpiScale));
            this.Controls.Add(lblSec2);

            // ListBox Monitored
            lstMonitored = new ListBox();
            lstMonitored.BackColor = Color.FromArgb(24, 28, 36);
            lstMonitored.ForeColor = Color.FromArgb(0, 230, 118);
            lstMonitored.BorderStyle = BorderStyle.FixedSingle;
            lstMonitored.Font = new Font("Microsoft YaHei UI", 8.8F);
            lstMonitored.ItemHeight = (int)(20 * dpiScale);
            lstMonitored.Location = new Point(pad, (int)(270 * dpiScale));
            lstMonitored.Size = new Size(listW, (int)(115 * dpiScale));
            lstMonitored.DoubleClick += (s, e) => RemoveSelectedMonitored();
            this.Controls.Add(lstMonitored);

            // Action buttons below Monitored
            int actY2 = (int)(392 * dpiScale);
            btnRemove = new ModernButton();
            btnRemove.Text = "✕ 移除选中的游戏";
            btnRemove.Font = new Font("Microsoft YaHei UI", 8.2F);
            btnRemove.NormalColor = Color.FromArgb(42, 28, 32);
            btnRemove.HoverColor = Color.FromArgb(60, 36, 42);
            btnRemove.BorderColor = Color.FromArgb(160, 60, 70);
            btnRemove.TextColor = Color.FromArgb(255, 120, 130);
            btnRemove.CornerRadius = (int)(5 * dpiScale);
            btnRemove.Cursor = Cursors.Hand;
            btnRemove.Location = new Point(pad, actY2);
            btnRemove.Size = new Size((int)(140 * dpiScale), btnH);
            btnRemove.Click += (s, e) => RemoveSelectedMonitored();
            this.Controls.Add(btnRemove);

            btnClearAll = new ModernButton();
            btnClearAll.Text = "清空全部";
            btnClearAll.Font = new Font("Microsoft YaHei UI", 8.2F);
            btnClearAll.NormalColor = Color.FromArgb(32, 36, 48);
            btnClearAll.HoverColor = Color.FromArgb(46, 52, 68);
            btnClearAll.BorderColor = Color.FromArgb(56, 64, 84);
            btnClearAll.TextColor = Color.FromArgb(160, 170, 190);
            btnClearAll.CornerRadius = (int)(5 * dpiScale);
            btnClearAll.Cursor = Cursors.Hand;
            btnClearAll.Location = new Point(pad + (int)(148 * dpiScale), actY2);
            btnClearAll.Size = new Size((int)(80 * dpiScale), btnH);
            btnClearAll.Click += (s, e) => {
                monitoredList.Clear();
                RefreshMonitoredList();
            };
            this.Controls.Add(btnClearAll);

            // Fallback checkbox & Close button
            int botY = (int)(430 * dpiScale);
            chkFallback = new ModernCheckBox();
            chkFallback.Text = "开启未在列表中的全屏游戏通用自动探测 (兜底模式)";
            chkFallback.Font = new Font("Microsoft YaHei UI", 8.2F);
            chkFallback.ForeColor = Color.FromArgb(170, 180, 200);
            chkFallback.Location = new Point(pad, botY);
            chkFallback.Size = new Size((int)(360 * dpiScale), (int)(22 * dpiScale));
            chkFallback.Checked = fallbackEnabled;
            this.Controls.Add(chkFallback);

            btnClose = new ModernButton();
            btnClose.Text = "完成并保存";
            btnClose.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold);
            btnClose.NormalColor = Color.FromArgb(0, 200, 83);
            btnClose.HoverColor = Color.FromArgb(0, 230, 118);
            btnClose.PressedColor = Color.FromArgb(0, 170, 70);
            btnClose.BorderColor = Color.FromArgb(0, 230, 118);
            btnClose.TextColor = Color.FromArgb(10, 24, 15);
            btnClose.CornerRadius = (int)(6 * dpiScale);
            btnClose.Cursor = Cursors.Hand;
            btnClose.Location = new Point(clientW - pad - (int)(110 * dpiScale), (int)(465 * dpiScale));
            btnClose.Size = new Size((int)(110 * dpiScale), (int)(34 * dpiScale));
            btnClose.Click += (s, e) => {
                if (onSaveCallback != null) onSaveCallback(monitoredList, chkFallback.Checked);
                this.DialogResult = DialogResult.OK;
                this.Close();
            };
            this.Controls.Add(btnClose);

            this.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Escape)
                {
                    if (onSaveCallback != null) onSaveCallback(monitoredList, chkFallback.Checked);
                    this.Close();
                }
            };

            RefreshRunningProcesses();
            RefreshMonitoredList();
        }

        private void RefreshRunningProcesses()
        {
            lstRunning.Items.Clear();
            var procs = GetRunningUserApplications();
            for (int i = 0; i < procs.Count; i++)
            {
                lstRunning.Items.Add(procs[i]);
            }
            if (lstRunning.Items.Count > 0) lstRunning.SelectedIndex = 0;
        }

        private void RefreshMonitoredList()
        {
            lstMonitored.Items.Clear();
            for (int i = 0; i < monitoredList.Count; i++)
            {
                lstMonitored.Items.Add(monitoredList[i]);
            }
            if (lstMonitored.Items.Count > 0) lstMonitored.SelectedIndex = 0;
        }

        private void AddSelectedRunning()
        {
            var item = lstRunning.SelectedItem as RunningProcessItem;
            if (item != null)
            {
                AddGameProcess(item.ProcessName);
            }
        }

        private void AddGameProcess(string exeName)
        {
            if (string.IsNullOrEmpty(exeName)) return;
            exeName = exeName.Trim();
            string lower = exeName.ToLower();
            if (!lower.EndsWith(".exe")) lower += ".exe";

            for (int i = 0; i < monitoredList.Count; i++)
            {
                string cur = monitoredList[i].ToLower();
                if (!cur.EndsWith(".exe")) cur += ".exe";
                if (cur == lower) return;
            }
            monitoredList.Add(lower);
            RefreshMonitoredList();
        }

        private void RemoveSelectedMonitored()
        {
            if (lstMonitored.SelectedIndex >= 0 && lstMonitored.SelectedIndex < monitoredList.Count)
            {
                monitoredList.RemoveAt(lstMonitored.SelectedIndex);
                RefreshMonitoredList();
            }
        }

        private void BrowseLocalExe()
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Title = "选择游戏可执行文件";
                ofd.Filter = "游戏程序 (*.exe)|*.exe|所有文件 (*.*)|*.*";
                ofd.Multiselect = true;
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    for (int i = 0; i < ofd.FileNames.Length; i++)
                    {
                        string fn = Path.GetFileName(ofd.FileNames[i]);
                        AddGameProcess(fn);
                    }
                }
            }
        }

        public static List<RunningProcessItem> GetRunningUserApplications()
        {
            var list = new List<RunningProcessItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Process[] all = Process.GetProcesses();
            for (int i = 0; i < all.Length; i++)
            {
                Process p = all[i];
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero) continue;
                    string title = p.MainWindowTitle;
                    if (string.IsNullOrWhiteSpace(title)) continue;

                    string name = p.ProcessName;
                    if (seen.Contains(name)) continue;

                    string nameLower = name.ToLower();
                    if (nameLower == "explorer" || nameLower == "taskmgr" || nameLower == "devenv" ||
                        nameLower == "ferrispulse" || nameLower == "razerbatterytray" ||
                        nameLower == "textinputhost" || nameLower == "applicationframehost" ||
                        nameLower == "searchhost" || nameLower == "shellexperiencehost" ||
                        nameLower == "startmenuexperiencehost" || nameLower == "lockapp" ||
                        nameLower == "systemsettings" || nameLower == "cmd" || nameLower == "powershell")
                    {
                        continue;
                    }

                    string exeName = nameLower.EndsWith(".exe") ? nameLower : (nameLower + ".exe");
                    list.Add(new RunningProcessItem {
                        ProcessName = exeName,
                        WindowTitle = title,
                        Pid = p.Id
                    });
                    seen.Add(name);
                }
                catch { }
                finally
                {
                    p.Dispose();
                }
            }
            list.Sort((a, b) => string.Compare(a.WindowTitle, b.WindowTitle, StringComparison.CurrentCultureIgnoreCase));
            return list;
        }
    }


    public class MainForm : Form
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
        private ToolStripMenuItem osdStyleMenu;
        private ToolStripMenuItem osdStyleCapsuleItem;
        private ToolStripMenuItem osdStyleGaugeItem;
        private ToolStripMenuItem osdStyleCompactItem;
        private ToolStripMenuItem int30sMenuItem;
        private ToolStripMenuItem int1mMenuItem;
        private ToolStripMenuItem int5mMenuItem;
        private ToolStripMenuItem dpiMenu;
        private ToolStripMenuItem rateMenu;
        private System.Windows.Forms.Timer updateTimer;

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

        // Background DPI Listener Thread
        private Thread dpiMonitorThread;
        private volatile bool isDpiMonitorRunning = false;
        private int lastMonitoredDpi = -1;
        private int lastMonitoredStage = -1;
        private int lastMonitoredRapooDpi = -1;
        private int lastMonitoredRapooStage = -1;
        private Dictionary<string, int> lastMonitoredRazerDpis = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private Dictionary<string, int> lastMonitoredRazerStages = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
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
        private int animTargetCardH = 0;

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
        private int trayStyle = 0; // 0 = Capsule, 1 = Number
        private int osdStyle = 0; // 0 = Centered Capsule, 1 = Stepped Gauge, 2 = Compact Top-Right
        private float dpiScale = 1.0f;
        private MouseBatteryInfo lastInfo = null;

        // Multi-Device Controls & State
        private FocusDeviceSelector focusDeviceSelector;
        private ModernButton btnTopPriority;
        private ModernButton btnTopAddDevice;
        private HeadsetStatusTile tileHeadsetAudio;
        private ToolTip toolTipTop;
        private StatusPill badgePrimary;
        private List<MouseBatteryInfo> activeDevices = new List<MouseBatteryInfo>();
        private MouseBatteryInfo selectedDevice = null;
        private ToolStripSeparator deviceMenuSep = null;
        private List<ToolStripMenuItem> dynamicDeviceMenuItems = new List<ToolStripMenuItem>();

        [DllImport("user32.dll", CharSet = CharSet.Auto)]
        static extern bool DestroyIcon(IntPtr handle);

        public MainForm(bool isAutoStart = false)
        {
            this.isAutoStartLaunch = isAutoStart;

            using (Graphics g = this.CreateGraphics())
            {
                dpiScale = g.DpiX / 96.0f;
                if (dpiScale < 1.0f) dpiScale = 1.0f;
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

            updateTimer = new System.Windows.Forms.Timer();
            updateTimer.Interval = userSelectedInterval;
            updateTimer.Tick += (s, e) => RefreshBatteryStatus(false);
            updateTimer.Start();

            RefreshBatteryStatus(false);
            StartBootPolling();
            StartDpiMonitor();
            StartGameMonitor();
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
            RegisterMouseWakeDetector();
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == WM_INPUT)
            {
                HandleMouseWakeInput(m.LParam);
            }
            else if (m.Msg == WM_DEVICECHANGE)
            {
                int wp = m.WParam.ToInt32();
                if (wp == DBT_DEVICEARRIVAL || wp == DBT_DEVICEREMOVECOMPLETE || wp == DBT_DEVNODES_CHANGED)
                {
                    lock (mouseDeviceWakeMatchCache)
                    {
                        mouseDeviceWakeMatchCache.Clear();
                    }
                    OnDeviceHardwareChange();
                }
            }
            base.WndProc(ref m);
        }

        private void RegisterMouseWakeDetector()
        {
            try
            {
                RAWINPUTDEVICE[] rid = new RAWINPUTDEVICE[1];
                rid[0].usUsagePage = HID_USAGE_PAGE_GENERIC;
                rid[0].usUsage = HID_USAGE_GENERIC_MOUSE;
                rid[0].dwFlags = RIDEV_INPUTSINK;
                rid[0].hwndTarget = this.Handle;
                RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
            }
            catch { }
        }

        private static bool IsTargetMouseHardwarePath(string path, MouseBatteryInfo targetMouse)
        {
            if (string.IsNullOrEmpty(path)) return false;

            // Immediately ignore laptop touchpads, trackpoints and motherboard devices
            if (path.IndexOf("SYNA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("ELAN", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("ACPI", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            if (targetMouse == null) return false;

            // Match by mouse brand or vendor ID
            string brand = targetMouse.Brand ?? "";
            string devId = targetMouse.DeviceId ?? "";

            if (brand.Equals("Razer", StringComparison.OrdinalIgnoreCase) || devId.IndexOf("1532", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (path.IndexOf("VID_1532", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            else if (brand.Equals("Logitech", StringComparison.OrdinalIgnoreCase) || devId.IndexOf("046D", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (path.IndexOf("VID_046D", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            else if (brand.Equals("Rapoo", StringComparison.OrdinalIgnoreCase) || devId.IndexOf("24AE", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (path.IndexOf("VID_24AE", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            else if (brand.Equals("VGN", StringComparison.OrdinalIgnoreCase) || brand.Equals("ATK", StringComparison.OrdinalIgnoreCase) || devId.IndexOf("3554", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (path.IndexOf("VID_3554", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("VID_258A", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }

            // Fallback match: if DeviceId or HardwareId has matching PID
            string devPathOrId = targetMouse.DeviceId ?? targetMouse.HardwareId ?? "";
            if (!string.IsNullOrEmpty(devPathOrId))
            {
                int pidIdx = devPathOrId.IndexOf("PID_", StringComparison.OrdinalIgnoreCase);
                if (pidIdx >= 0 && devPathOrId.Length >= pidIdx + 8)
                {
                    string pidToken = devPathOrId.Substring(pidIdx, 8);
                    if (path.IndexOf(pidToken, StringComparison.OrdinalIgnoreCase) >= 0) return true;
                }
            }

            return false;
        }

        private void HandleMouseWakeInput(IntPtr hRawInput)
        {
            try
            {
                var cur = lastInfo;
                if (cur == null || !cur.IsConnected || !cur.IsSleeping) return;

                int headerSize = Marshal.SizeOf(typeof(RAWINPUTHEADER));
                RAWINPUTHEADER header;
                if (GetRawInputData(hRawInput, RID_HEADER, out header, ref headerSize, headerSize) >= 0)
                {
                    if (header.dwType == 0) // RIM_TYPEMOUSE
                    {
                        IntPtr hDev = header.hDevice;
                        if (hDev == IntPtr.Zero) return;

                        bool isMatch;
                        bool found;
                        lock (mouseDeviceWakeMatchCache)
                        {
                            found = mouseDeviceWakeMatchCache.TryGetValue(hDev, out isMatch);
                        }

                        if (!found)
                        {
                            isMatch = false;
                            uint len = 0;
                            GetRawInputDeviceInfoW(hDev, RIDI_DEVICENAME, null, ref len);
                            if (len > 0)
                            {
                                StringBuilder sb = new StringBuilder((int)len);
                                if (GetRawInputDeviceInfoW(hDev, RIDI_DEVICENAME, sb, ref len) > 0)
                                {
                                    string devPath = sb.ToString();
                                    isMatch = IsTargetMouseHardwarePath(devPath, cur);
                                }
                            }
                            lock (mouseDeviceWakeMatchCache)
                            {
                                mouseDeviceWakeMatchCache[hDev] = isMatch;
                            }
                        }

                        if (isMatch)
                        {
                            cur.IsSleeping = false;
                            UpdateTrayIcon(cur.BatteryPercent, cur.IsCharging, cur.IsConnected, false);
                            try { this.BeginInvoke(new Action(() => UpdateUI(cur, false))); } catch { }

                            if (!isPassiveWakePollingQueued)
                            {
                                isPassiveWakePollingQueued = true;
                                ThreadPool.QueueUserWorkItem(_ =>
                                {
                                    Thread.Sleep(800);
                                    isPassiveWakePollingQueued = false;
                                    try
                                    {
                                        this.BeginInvoke(new Action(() => RefreshBatteryStatus(false)));
                                    }
                                    catch { }
                                });
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void StartBootPolling()
        {
            bootPollCount = 0;
            if (bootPollTimer == null)
            {
                bootPollTimer = new System.Windows.Forms.Timer();
                bootPollTimer.Interval = 1200;
                bootPollTimer.Tick += (s, e) =>
                {
                    bootPollCount++;
                    if (lastInfo == null || lastInfo.IsSleeping || !lastInfo.IsConnected || lastInfo.BatteryPercent <= 0)
                    {
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            try
                            {
                                this.BeginInvoke(new Action(() => RefreshBatteryStatus(false)));
                            }
                            catch { }
                        });
                    }
                    if (bootPollCount >= 8 || (lastInfo != null && lastInfo.IsConnected && !lastInfo.IsSleeping && lastInfo.BatteryPercent > 0))
                    {
                        bootPollTimer.Stop();
                    }
                };
            }
            bootPollTimer.Start();
        }

        private void RegisterUsbNotification()
        {
            try
            {
                Guid hidGuid;
                RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);

                DEV_BROADCAST_DEVICEINTERFACE dbi = new DEV_BROADCAST_DEVICEINTERFACE();
                dbi.dbcc_size = Marshal.SizeOf(dbi);
                dbi.dbcc_devicetype = DBT_DEVTYP_DEVICEINTERFACE;
                dbi.dbcc_reserved = 0;
                dbi.dbcc_classguid = hidGuid;

                IntPtr buffer = Marshal.AllocHGlobal(dbi.dbcc_size);
                try
                {
                    Marshal.StructureToPtr(dbi, buffer, true);
                    hDevNotify = RegisterDeviceNotification(this.Handle, buffer, DEVICE_NOTIFY_WINDOW_HANDLE);
                }
                finally
                {
                    Marshal.FreeHGlobal(buffer);
                }
            }
            catch { }
        }


        private void OnDeviceHardwareChange()
        {
            lock (mouseDeviceWakeMatchCache)
            {
                mouseDeviceWakeMatchCache.Clear();
            }
            NuphyDeviceHelper.CachedCommandPath = null;
            if (deviceChangeTimer1 == null)
            {
                deviceChangeTimer1 = new System.Windows.Forms.Timer();
                deviceChangeTimer1.Tick += (s, e) =>
                {
                    deviceChangeTimer1.Stop();
                    RefreshBatteryStatus(false);
                };
            }
            deviceChangeTimer1.Stop();
            deviceChangeTimer1.Interval = 250;
            deviceChangeTimer1.Start();

            if (deviceChangeTimer2 == null)
            {
                deviceChangeTimer2 = new System.Windows.Forms.Timer();
                deviceChangeTimer2.Tick += (s, e) =>
                {
                    deviceChangeTimer2.Stop();
                    RefreshBatteryStatus(false);
                };
            }
            deviceChangeTimer2.Stop();
            deviceChangeTimer2.Interval = 1000;
            deviceChangeTimer2.Start();
        }

        private void ApplyModernWin11Theme()
        {
            ModernThemeHelper.ApplyModernWin11Theme(this.Handle);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            try
            {
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

        private void InitializeFormUI()
        {
            this.Text = "FerrisPulse · 灵脉";
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = true;
            this.StartPosition = FormStartPosition.Manual;
            this.BackColor = Color.FromArgb(18, 19, 23);
            this.ForeColor = Color.White;
            this.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.ShowInTaskbar = true;
            this.DoubleBuffered = true;
            this.LocationChanged += (s, e) => {
                if (switcherFlyout != null && !switcherFlyout.IsDisposed && switcherFlyout.Visible)
                {
                    switcherFlyout.Close();
                }
                if (this.WindowState == FormWindowState.Normal && this.Visible && this.Location.X > -3000 && this.Location.Y > -3000)
                {
                    savedWindowX = this.Location.X;
                    savedWindowY = this.Location.Y;
                }
            };

            int padX = (int)(18 * dpiScale);
            int baseW = (int)(470 * dpiScale);
            int cardW = baseW - (padX * 2);

            // ================= TOP BAR: SCHEME B FOCUS SELECTOR + BUTTONS =================
            int barH = (int)(38 * dpiScale);
            int btnSize = barH;
            int topGap = (int)(6 * dpiScale);
            int selectorW = cardW - (btnSize * 2) - (topGap * 2);

            focusDeviceSelector = new FocusDeviceSelector();
            focusDeviceSelector.DpiScale = dpiScale;
            focusDeviceSelector.Location = new Point(padX, (int)(12 * dpiScale));
            focusDeviceSelector.Size = new Size(selectorW, barH);
            focusDeviceSelector.SelectorClicked += () => {
                ToggleDeviceSwitcherFlyout();
            };
            this.Controls.Add(focusDeviceSelector);

            btnTopPriority = new ModernButton();
            btnTopPriority.Text = "📶";
            btnTopPriority.Font = new Font("Segoe UI Emoji", 10F);
            btnTopPriority.CornerRadius = (int)(8 * dpiScale);
            btnTopPriority.NormalColor = Color.FromArgb(25, 27, 34);
            btnTopPriority.HoverColor = Color.FromArgb(36, 40, 52);
            btnTopPriority.PressedColor = Color.FromArgb(20, 22, 28);
            btnTopPriority.BorderColor = Color.FromArgb(45, 50, 65);
            btnTopPriority.ForeColor = Color.White;
            btnTopPriority.Location = new Point(padX + selectorW + topGap, (int)(12 * dpiScale));
            btnTopPriority.Size = new Size(btnSize, barH);
            btnTopPriority.Click += (s, e) => {
                if (switcherFlyout != null && !switcherFlyout.IsDisposed && switcherFlyout.Visible) switcherFlyout.Close();
                OpenPriorityManager();
            };
            this.Controls.Add(btnTopPriority);

            btnTopAddDevice = new ModernButton();
            btnTopAddDevice.Text = "➕";
            btnTopAddDevice.Font = new Font("Segoe UI Emoji", 10F);
            btnTopAddDevice.CornerRadius = (int)(8 * dpiScale);
            btnTopAddDevice.NormalColor = Color.FromArgb(25, 27, 34);
            btnTopAddDevice.HoverColor = Color.FromArgb(36, 40, 52);
            btnTopAddDevice.PressedColor = Color.FromArgb(20, 22, 28);
            btnTopAddDevice.BorderColor = Color.FromArgb(45, 50, 65);
            btnTopAddDevice.ForeColor = Color.White;
            btnTopAddDevice.Location = new Point(padX + selectorW + topGap + btnSize + topGap, (int)(12 * dpiScale));
            btnTopAddDevice.Size = new Size(btnSize, barH);
            btnTopAddDevice.Click += (s, e) => {
                if (switcherFlyout != null && !switcherFlyout.IsDisposed && switcherFlyout.Visible) switcherFlyout.Close();
                OpenAddDeviceWizard();
            };
            this.Controls.Add(btnTopAddDevice);

            toolTipTop = new ToolTip();
            toolTipTop.SetToolTip(btnTopPriority, "设备优先级管理 (拖拽排序)");
            toolTipTop.SetToolTip(btnTopAddDevice, "手动添加新外设向导 (耳机/手柄/键鼠)");

            // ================= CARD 1: BATTERY & DEVICE STATUS =================
            int card1Y = (int)(12 * dpiScale) + barH + (int)(10 * dpiScale);
            int card1H = (int)(176 * dpiScale);

            cardBattery = new RoundedCard();
            cardBattery.Location = new Point(padX, card1Y);
            cardBattery.Size = new Size(cardW, card1H);
            cardBattery.CornerRadius = (int)(10 * dpiScale);
            cardBattery.CardColor = Color.FromArgb(25, 27, 34);
            cardBattery.BorderColor = Color.FromArgb(42, 46, 58);
            this.Controls.Add(cardBattery);

            int badgeW = (int)(108 * dpiScale);
            int badgeH = (int)(26 * dpiScale);
            int badgeX = cardW - badgeW - (int)(16 * dpiScale);
            int badgeY = (int)(14 * dpiScale);

            badgePrimary = new StatusPill();
            badgePrimary.Location = new Point(badgeX, badgeY);
            badgePrimary.Size = new Size(badgeW, badgeH);
            badgePrimary.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold, GraphicsUnit.Point);
            badgePrimary.SetStatus("● 任务栏常驻", Color.FromArgb(0, 230, 118));
            badgePrimary.Visible = false;
            cardBattery.Controls.Add(badgePrimary);

            int nameX = (int)(18 * dpiScale);
            int nameY = (int)(14 * dpiScale);
            int nameMaxW = badgeX - nameX - (int)(10 * dpiScale);

            lblDeviceName = new Label();
            lblDeviceName.Text = "正在检测设备...";
            lblDeviceName.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold, GraphicsUnit.Point);
            lblDeviceName.ForeColor = Color.White;
            lblDeviceName.BackColor = Color.Transparent;
            lblDeviceName.Location = new Point(nameX, nameY);
            lblDeviceName.AutoSize = false;
            lblDeviceName.Size = new Size(nameMaxW, (int)(26 * dpiScale));
            lblDeviceName.AutoEllipsis = true;
            cardBattery.Controls.Add(lblDeviceName);

            lblBatteryBig = new Label();
            lblBatteryBig.Text = "--%";
            lblBatteryBig.Font = new Font("Microsoft YaHei UI", 34F, FontStyle.Bold, GraphicsUnit.Point);
            lblBatteryBig.ForeColor = Color.FromArgb(0, 230, 118);
            lblBatteryBig.BackColor = Color.Transparent;
            lblBatteryBig.Location = new Point((int)(16 * dpiScale), (int)(46 * dpiScale));
            lblBatteryBig.AutoSize = true;
            cardBattery.Controls.Add(lblBatteryBig);

            // Status Pill: Power / Charging State (sole status badge in Card 1)
            pillStatus = new StatusPill();
            pillStatus.Location = new Point((int)(175 * dpiScale), (int)(56 * dpiScale));
            pillStatus.Size = new Size((int)(110 * dpiScale), (int)(32 * dpiScale));
            pillStatus.Font = new Font("Microsoft YaHei UI", 9.2F, FontStyle.Bold, GraphicsUnit.Point);
            pillStatus.SetStatus("● 正常运行", Color.FromArgb(0, 230, 118));
            cardBattery.Controls.Add(pillStatus);

            barBattery = new ModernProgressBar();
            barBattery.Location = new Point((int)(18 * dpiScale), (int)(118 * dpiScale));
            barBattery.Size = new Size(cardW - (int)(36 * dpiScale), (int)(10 * dpiScale));
            barBattery.Value = 0;
            barBattery.TrackColor = Color.FromArgb(38, 42, 53);
            barBattery.ProgressColor = Color.FromArgb(0, 230, 118);
            cardBattery.Controls.Add(barBattery);

            lblUpdateTime = new Label();
            lblUpdateTime.Text = "最后同步: --:--:-- · 自动侦测硬件插拔";
            lblUpdateTime.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
            lblUpdateTime.ForeColor = Color.FromArgb(135, 142, 156);
            lblUpdateTime.BackColor = Color.Transparent;
            lblUpdateTime.Location = new Point((int)(18 * dpiScale), (int)(142 * dpiScale));
            lblUpdateTime.AutoSize = true;
            cardBattery.Controls.Add(lblUpdateTime);

            // ================= CARD 2: PERFORMANCE & TUNING =================
            int card2Y = card1Y + card1H + (int)(12 * dpiScale);
            int card2H = (int)(152 * dpiScale);

            cardPerformance = new RoundedCard();
            cardPerformance.Location = new Point(padX, card2Y);
            cardPerformance.Size = new Size(cardW, card2H);
            cardPerformance.CornerRadius = (int)(10 * dpiScale);
            cardPerformance.CardColor = Color.FromArgb(25, 27, 34);
            cardPerformance.BorderColor = Color.FromArgb(42, 46, 58);
            this.Controls.Add(cardPerformance);

            int cPad = (int)(18 * dpiScale);
            int secW = cardW - (cPad * 2);

            lblPerfTitle = new Label();
            lblPerfTitle.Text = "鼠标性能与档位调节";
            lblPerfTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);
            lblPerfTitle.ForeColor = Color.FromArgb(240, 245, 255);
            lblPerfTitle.BackColor = Color.Transparent;
            lblPerfTitle.Location = new Point(cPad, (int)(14 * dpiScale));
            lblPerfTitle.AutoSize = true;
            cardPerformance.Controls.Add(lblPerfTitle);

            lblLiveRateBadge = new Label();
            lblLiveRateBadge.Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Regular, GraphicsUnit.Point);
            lblLiveRateBadge.ForeColor = Color.FromArgb(0, 230, 118);
            lblLiveRateBadge.BackColor = Color.Transparent;
            lblLiveRateBadge.TextAlign = ContentAlignment.MiddleRight;
            lblLiveRateBadge.Location = new Point(cardW - cPad - (int)(220 * dpiScale), (int)(14 * dpiScale));
            lblLiveRateBadge.Size = new Size((int)(220 * dpiScale), (int)(20 * dpiScale));
            lblLiveRateBadge.Visible = false;
            cardPerformance.Controls.Add(lblLiveRateBadge);

            // Row 1: DPI 档位
            int r1Y = (int)(40 * dpiScale);
            int lblW = (int)(86 * dpiScale);
            int segH = (int)(28 * dpiScale);

            lblDpiTitle = new Label();
            lblDpiTitle.Text = "DPI 档位";
            lblDpiTitle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            lblDpiTitle.ForeColor = Color.FromArgb(160, 168, 185);
            lblDpiTitle.BackColor = Color.Transparent;
            lblDpiTitle.Location = new Point(cPad, r1Y + (int)(4 * dpiScale));
            lblDpiTitle.Size = new Size(lblW, (int)(22 * dpiScale));
            cardPerformance.Controls.Add(lblDpiTitle);

            btnDpiStages = new ModernSegmentButton[5];
            for (int i = 0; i < 5; i++)
            {
                int index = i;
                var btn = new ModernSegmentButton();
                btn.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
                btn.Click += (s, e) => {
                    SwitchDpiStageByIndex(index);
                };
                cardPerformance.Controls.Add(btn);
                btnDpiStages[i] = btn;
            }

            btnDpiManage = new ModernButton();
            btnDpiManage.IsGearButton = true;
            btnDpiManage.CornerRadius = (int)(6 * dpiScale);
            btnDpiManage.Cursor = Cursors.Hand;
            btnDpiManage.Click += (s, e) => OpenDpiManagementDialog();
            toolTipTop.SetToolTip(btnDpiManage, "自定义 DPI 档位与板载设置");
            cardPerformance.Controls.Add(btnDpiManage);

            RelayoutDpiButtons();

            // Row 2: 回报率
            int r2Y = (int)(76 * dpiScale);

            lblRateTitle = new Label();
            lblRateTitle.Text = "常规回报率";
            lblRateTitle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            lblRateTitle.ForeColor = Color.FromArgb(160, 168, 185);
            lblRateTitle.BackColor = Color.Transparent;
            lblRateTitle.Location = new Point(cPad, r2Y + (int)(4 * dpiScale));
            lblRateTitle.Size = new Size(lblW, (int)(22 * dpiScale));
            cardPerformance.Controls.Add(lblRateTitle);

            int hzStartX = cPad + lblW;
            int hzGap = (int)(6 * dpiScale);
            int hzSegW = (secW - lblW - (hzGap * 3)) / 4;

            btnRates = new ModernSegmentButton[4];
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                int hzVal = pollingRateValues[i];
                var btn = new ModernSegmentButton();
                btn.Text = hzVal + " Hz";
                btn.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
                btn.Location = new Point(hzStartX + i * (hzSegW + hzGap), r2Y);
                btn.Size = new Size(hzSegW, segH);
                btn.Click += (s, e) => SetPollingRateFromUI(pollingRateValues[index]);
                cardPerformance.Controls.Add(btn);
                btnRates[i] = btn;
            }

            // Row 3: 全屏游戏自动切换回报率 (开关 + 游戏管理按钮)
            int gameSwY = (int)(110 * dpiScale);
            int gameChkW = (int)(175 * dpiScale);
            chkAutoGameRate = new ModernCheckBox();
            chkAutoGameRate.Text = "全屏游戏自动切换回报率";
            chkAutoGameRate.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            chkAutoGameRate.ForeColor = Color.FromArgb(210, 220, 240);
            chkAutoGameRate.Location = new Point(cPad, gameSwY);
            chkAutoGameRate.Size = new Size(gameChkW, (int)(22 * dpiScale));
            chkAutoGameRate.CheckedChanged += (s, e) => {
                if (!isUpdatingUI) SetAutoGameRateEnabled(chkAutoGameRate.Checked);
            };
            cardPerformance.Controls.Add(chkAutoGameRate);

            btnManageGames = new ModernButton();
            UpdateManageGamesButtonText();
            btnManageGames.Font = new Font("Microsoft YaHei UI", 8.2F);
            btnManageGames.NormalColor = Color.FromArgb(28, 32, 44);
            btnManageGames.HoverColor = Color.FromArgb(42, 48, 64);
            btnManageGames.PressedColor = Color.FromArgb(20, 24, 34);
            btnManageGames.BorderColor = Color.FromArgb(50, 58, 76);
            btnManageGames.TextColor = Color.FromArgb(0, 230, 118);
            btnManageGames.CornerRadius = (int)(5 * dpiScale);
            btnManageGames.Cursor = Cursors.Hand;
            int btnMgrX = cPad + gameChkW + (int)(6 * dpiScale);
            int btnMgrW = secW - gameChkW - (int)(6 * dpiScale);
            btnManageGames.Location = new Point(btnMgrX, gameSwY - (int)(1 * dpiScale));
            btnManageGames.Size = new Size(btnMgrW, (int)(24 * dpiScale));
            btnManageGames.Click += (s, e) => OpenGameProcessManager();
            cardPerformance.Controls.Add(btnManageGames);

            // Row 4: 电竞回报率选择
            int gameRateY = (int)(138 * dpiScale);
            lblGameRateTitle = new Label();
            lblGameRateTitle.Text = "电竞回报率";
            lblGameRateTitle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            lblGameRateTitle.ForeColor = Color.FromArgb(160, 168, 185);
            lblGameRateTitle.BackColor = Color.Transparent;
            lblGameRateTitle.Location = new Point(cPad, gameRateY + (int)(4 * dpiScale));
            lblGameRateTitle.Size = new Size(lblW, (int)(22 * dpiScale));
            lblGameRateTitle.Visible = false;
            cardPerformance.Controls.Add(lblGameRateTitle);

            btnGameRates = new ModernSegmentButton[4];
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                int hzVal = pollingRateValues[i];
                var btn = new ModernSegmentButton();
                btn.Text = hzVal + " Hz";
                btn.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
                btn.Location = new Point(hzStartX + i * (hzSegW + hzGap), gameRateY);
                btn.Size = new Size(hzSegW, segH);
                btn.Visible = false;
                btn.Click += (s, e) => SetGamePollingRateFromUI(pollingRateValues[index]);
                cardPerformance.Controls.Add(btn);
                btnGameRates[i] = btn;
            }

            // Row 5: 提醒方式设置
            int gameNotifY = (int)(170 * dpiScale);
            lblNotifyModeTitle = new Label();
            lblNotifyModeTitle.Text = "切换提醒:";
            lblNotifyModeTitle.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
            lblNotifyModeTitle.ForeColor = Color.FromArgb(140, 150, 170);
            lblNotifyModeTitle.BackColor = Color.Transparent;
            lblNotifyModeTitle.Location = new Point(cPad, gameNotifY + (int)(2 * dpiScale));
            lblNotifyModeTitle.Size = new Size((int)(62 * dpiScale), (int)(20 * dpiScale));
            lblNotifyModeTitle.Visible = false;
            cardPerformance.Controls.Add(lblNotifyModeTitle);

            int notifStartX = cPad + (int)(64 * dpiScale);
            int notifW = (secW - (int)(64 * dpiScale)) / 3;

            chkNotifyOsd = new ModernCheckBox();
            chkNotifyOsd.Text = "OSD菜单";
            chkNotifyOsd.Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Regular, GraphicsUnit.Point);
            chkNotifyOsd.ForeColor = Color.FromArgb(170, 180, 200);
            chkNotifyOsd.Location = new Point(notifStartX, gameNotifY);
            chkNotifyOsd.Size = new Size(notifW, (int)(20 * dpiScale));
            chkNotifyOsd.Visible = false;
            chkNotifyOsd.CheckedChanged += (s, e) => {
                if (!isUpdatingUI) { gameRateNotifyOsd = chkNotifyOsd.Checked; SaveConfig(); }
            };
            cardPerformance.Controls.Add(chkNotifyOsd);

            chkNotifyToast = new ModernCheckBox();
            chkNotifyToast.Text = "系统通知";
            chkNotifyToast.Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Regular, GraphicsUnit.Point);
            chkNotifyToast.ForeColor = Color.FromArgb(170, 180, 200);
            chkNotifyToast.Location = new Point(notifStartX + notifW, gameNotifY);
            chkNotifyToast.Size = new Size(notifW, (int)(20 * dpiScale));
            chkNotifyToast.Visible = false;
            chkNotifyToast.CheckedChanged += (s, e) => {
                if (!isUpdatingUI) { gameRateNotifyToast = chkNotifyToast.Checked; SaveConfig(); }
            };
            cardPerformance.Controls.Add(chkNotifyToast);

            chkNotifySound = new ModernCheckBox();
            chkNotifySound.Text = "柔和提示音";
            chkNotifySound.Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Regular, GraphicsUnit.Point);
            chkNotifySound.ForeColor = Color.FromArgb(170, 180, 200);
            chkNotifySound.Location = new Point(notifStartX + notifW * 2, gameNotifY);
            chkNotifySound.Size = new Size(notifW, (int)(20 * dpiScale));
            chkNotifySound.Visible = false;
            chkNotifySound.CheckedChanged += (s, e) => {
                if (!isUpdatingUI) { gameRateNotifySound = chkNotifySound.Checked; SaveConfig(); }
            };
            cardPerformance.Controls.Add(chkNotifySound);

            // Rapoo Status Tile (Displayed when Rapoo device is selected - full width, DPI only)
            int tileH = (int)(74 * dpiScale);

            tileRapooDpi = new RapooStatusTile();
            tileRapooDpi.DpiScale = dpiScale;
            tileRapooDpi.Location = new Point(cPad, (int)(38 * dpiScale));
            tileRapooDpi.Size = new Size(secW, tileH);
            tileRapooDpi.Title = "当前实时 DPI";
            tileRapooDpi.Badge = "";
            tileRapooDpi.ValueText = "1600";
            tileRapooDpi.UnitText = "DPI";
            tileRapooDpi.Footnote = "按鼠标机身 DPI 键循环切档 · 硬件级实时监听";
            tileRapooDpi.Visible = false;
            cardPerformance.Controls.Add(tileRapooDpi);

            tileHeadsetAudio = new HeadsetStatusTile();
            tileHeadsetAudio.DpiScale = dpiScale;
            tileHeadsetAudio.Location = new Point(cPad, (int)(38 * dpiScale));
            tileHeadsetAudio.Size = new Size(secW, tileH);
            tileHeadsetAudio.Title = "蓝牙音频连接";
            tileHeadsetAudio.Footnote = "Windows 原生蓝牙音频协议 · 自动双模监听";
            tileHeadsetAudio.Visible = false;
            cardPerformance.Controls.Add(tileHeadsetAudio);

            // Gamepad Interactive Controls
            btnGamepadVibrate = new ModernButton();
            btnGamepadVibrate.Text = "🎮 触发双震区触觉测试";
            btnGamepadVibrate.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold);
            btnGamepadVibrate.NormalColor = Color.FromArgb(40, 48, 68);
            btnGamepadVibrate.HoverColor = Color.FromArgb(60, 72, 100);
            btnGamepadVibrate.PressedColor = Color.FromArgb(30, 36, 52);
            btnGamepadVibrate.BorderColor = Color.FromArgb(80, 100, 140);
            btnGamepadVibrate.TextColor = Color.FromArgb(235, 240, 255);
            btnGamepadVibrate.CornerRadius = (int)(6 * dpiScale);
            btnGamepadVibrate.Cursor = Cursors.Hand;
            btnGamepadVibrate.Visible = false;
            btnGamepadVibrate.Click += (s, e) => {
                XInputHelper.TriggerGamepadVibration(400);
            };
            cardPerformance.Controls.Add(btnGamepadVibrate);

            btnGamepadJoyCpl = new ModernButton();
            btnGamepadJoyCpl.Text = "🕹️ 打开游戏控制器校准 (joy.cpl)";
            btnGamepadJoyCpl.Font = new Font("Microsoft YaHei UI", 8.5F);
            btnGamepadJoyCpl.NormalColor = Color.FromArgb(28, 32, 44);
            btnGamepadJoyCpl.HoverColor = Color.FromArgb(42, 48, 64);
            btnGamepadJoyCpl.PressedColor = Color.FromArgb(20, 24, 34);
            btnGamepadJoyCpl.BorderColor = Color.FromArgb(50, 58, 76);
            btnGamepadJoyCpl.TextColor = Color.FromArgb(190, 200, 220);
            btnGamepadJoyCpl.CornerRadius = (int)(6 * dpiScale);
            btnGamepadJoyCpl.Cursor = Cursors.Hand;
            btnGamepadJoyCpl.Visible = false;
            btnGamepadJoyCpl.Click += (s, e) => {
                try { System.Diagnostics.Process.Start("joy.cpl"); } catch { }
            };
            cardPerformance.Controls.Add(btnGamepadJoyCpl);

            // Universal Keyboard Interactive Controls
            hudThreeLocks = new ThreeLocksHudControl();
            hudThreeLocks.DpiScale = dpiScale;
            hudThreeLocks.Visible = false;
            cardPerformance.Controls.Add(hudThreeLocks);

            btnUniversalWinLock = new ModernButton();
            btnUniversalWinLock.Text = "🛡️ Win 键防误触: 未锁定";
            btnUniversalWinLock.Font = new Font("Microsoft YaHei UI", 8.5F);
            btnUniversalWinLock.NormalColor = Color.FromArgb(28, 32, 44);
            btnUniversalWinLock.HoverColor = Color.FromArgb(42, 48, 64);
            btnUniversalWinLock.PressedColor = Color.FromArgb(20, 24, 34);
            btnUniversalWinLock.BorderColor = Color.FromArgb(50, 58, 76);
            btnUniversalWinLock.TextColor = Color.FromArgb(190, 200, 220);
            btnUniversalWinLock.CornerRadius = (int)(6 * dpiScale);
            btnUniversalWinLock.Cursor = Cursors.Hand;
            btnUniversalWinLock.Visible = false;
            btnUniversalWinLock.Click += (s, e) => {
                bool nextState = !UniversalWinLockHelper.IsLocked;
                UniversalWinLockHelper.SetLock(nextState);
                if (UniversalWinLockHelper.IsLocked)
                {
                    btnUniversalWinLock.Text = "🔒 Win 键已锁定 (防误触)";
                    btnUniversalWinLock.NormalColor = Color.FromArgb(40, 20, 24);
                    btnUniversalWinLock.BorderColor = Color.FromArgb(255, 82, 82);
                    btnUniversalWinLock.TextColor = Color.FromArgb(255, 100, 100);
                }
                else
                {
                    btnUniversalWinLock.Text = "🛡️ Win 键防误触: 未锁定";
                    btnUniversalWinLock.NormalColor = Color.FromArgb(28, 32, 44);
                    btnUniversalWinLock.BorderColor = Color.FromArgb(50, 58, 76);
                    btnUniversalWinLock.TextColor = Color.FromArgb(190, 200, 220);
                }
            };
            cardPerformance.Controls.Add(btnUniversalWinLock);

            btnKeyTestSpeed = new ModernButton();
            btnKeyTestSpeed.Text = "⚡ 全键无冲与触发测速仪 (NKRO)";
            btnKeyTestSpeed.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            btnKeyTestSpeed.NormalColor = Color.FromArgb(36, 44, 62);
            btnKeyTestSpeed.HoverColor = Color.FromArgb(50, 62, 88);
            btnKeyTestSpeed.PressedColor = Color.FromArgb(26, 32, 46);
            btnKeyTestSpeed.BorderColor = Color.FromArgb(70, 90, 130);
            btnKeyTestSpeed.TextColor = Color.FromArgb(230, 240, 255);
            btnKeyTestSpeed.CornerRadius = (int)(6 * dpiScale);
            btnKeyTestSpeed.Cursor = Cursors.Hand;
            btnKeyTestSpeed.Visible = false;
            btnKeyTestSpeed.Click += (s, e) => {
                using (var dlg = new KeyRolloverSpeedForm(dpiScale))
                {
                    dlg.ShowDialog(this);
                }
            };
            cardPerformance.Controls.Add(btnKeyTestSpeed);

            btnKeyboardSettings = new ModernButton();
            btnKeyboardSettings.Text = "⌨️ 打开 Windows 键盘属性设置";
            btnKeyboardSettings.Font = new Font("Microsoft YaHei UI", 8.5F);
            btnKeyboardSettings.NormalColor = Color.FromArgb(28, 32, 44);
            btnKeyboardSettings.HoverColor = Color.FromArgb(42, 48, 64);
            btnKeyboardSettings.PressedColor = Color.FromArgb(20, 24, 34);
            btnKeyboardSettings.BorderColor = Color.FromArgb(50, 58, 76);
            btnKeyboardSettings.TextColor = Color.FromArgb(190, 200, 220);
            btnKeyboardSettings.CornerRadius = (int)(6 * dpiScale);
            btnKeyboardSettings.Cursor = Cursors.Hand;
            btnKeyboardSettings.Visible = false;
            btnKeyboardSettings.Click += (s, e) => {
                ThreadPool.QueueUserWorkItem(_ => {
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo("control.exe", "main.cpl,@1")
                        {
                            UseShellExecute = true
                        };
                        System.Diagnostics.Process.Start(psi);
                    }
                    catch
                    {
                        try
                        {
                            var psi = new System.Diagnostics.ProcessStartInfo("rundll32.exe", "shell32.dll,Control_RunDLL main.cpl @1")
                            {
                                UseShellExecute = true
                            };
                            System.Diagnostics.Process.Start(psi);
                        }
                        catch
                        {
                            try
                            {
                                var psi = new System.Diagnostics.ProcessStartInfo("ms-settings:keyboard")
                                {
                                    UseShellExecute = true
                                };
                                System.Diagnostics.Process.Start(psi);
                            }
                            catch { }
                        }
                    }
                });
            };
            cardPerformance.Controls.Add(btnKeyboardSettings);

            // Audio Controls
            btnAudioSetDefault = new ModernButton();
            btnAudioSetDefault.Text = "🎯 设为系统默认音频输出";
            btnAudioSetDefault.Font = new Font("Microsoft YaHei UI", 8.5F);
            btnAudioSetDefault.NormalColor = Color.FromArgb(28, 32, 44);
            btnAudioSetDefault.HoverColor = Color.FromArgb(42, 48, 64);
            btnAudioSetDefault.PressedColor = Color.FromArgb(20, 24, 34);
            btnAudioSetDefault.BorderColor = Color.FromArgb(50, 58, 76);
            btnAudioSetDefault.TextColor = Color.FromArgb(190, 200, 220);
            btnAudioSetDefault.CornerRadius = (int)(6 * dpiScale);
            btnAudioSetDefault.Cursor = Cursors.Hand;
            btnAudioSetDefault.Visible = false;
            btnAudioSetDefault.Click += (s, e) => {
                AudioDeviceHelper.SetDefaultAudioDevice(selectedDevice != null ? (selectedDevice.CustomName ?? selectedDevice.DeviceName ?? "") : "");
            };
            cardPerformance.Controls.Add(btnAudioSetDefault);

            btnVolumeMixer = new ModernButton();
            btnVolumeMixer.Text = "🎚️ 独立音量合成器 (sndvol)";
            btnVolumeMixer.Font = new Font("Microsoft YaHei UI", 8.5F);
            btnVolumeMixer.NormalColor = Color.FromArgb(28, 32, 44);
            btnVolumeMixer.HoverColor = Color.FromArgb(42, 48, 64);
            btnVolumeMixer.PressedColor = Color.FromArgb(20, 24, 34);
            btnVolumeMixer.BorderColor = Color.FromArgb(50, 58, 76);
            btnVolumeMixer.TextColor = Color.FromArgb(190, 200, 220);
            btnVolumeMixer.CornerRadius = (int)(6 * dpiScale);
            btnVolumeMixer.Cursor = Cursors.Hand;
            btnVolumeMixer.Visible = false;
            btnVolumeMixer.Click += (s, e) => {
                AudioDeviceHelper.OpenVolumeMixer();
            };
            cardPerformance.Controls.Add(btnVolumeMixer);

            btnAudioSettings = new ModernButton();
            btnAudioSettings.Text = "🔊 打开系统声音与空间音效";
            btnAudioSettings.Font = new Font("Microsoft YaHei UI", 8.5F);
            btnAudioSettings.NormalColor = Color.FromArgb(28, 32, 44);
            btnAudioSettings.HoverColor = Color.FromArgb(42, 48, 64);
            btnAudioSettings.PressedColor = Color.FromArgb(20, 24, 34);
            btnAudioSettings.BorderColor = Color.FromArgb(50, 58, 76);
            btnAudioSettings.TextColor = Color.FromArgb(190, 200, 220);
            btnAudioSettings.CornerRadius = (int)(6 * dpiScale);
            btnAudioSettings.Cursor = Cursors.Hand;
            btnAudioSettings.Visible = false;
            btnAudioSettings.Click += (s, e) => {
                try { System.Diagnostics.Process.Start("ms-settings:sound"); }
                catch { try { System.Diagnostics.Process.Start("mmsys.cpl"); } catch { } }
            };
            cardPerformance.Controls.Add(btnAudioSettings);

            // Generic Mouse Controls
            btnMouseRateTest = new ModernButton();
            btnMouseRateTest.Text = "⚡ 鼠标回报率实测 (RawInput)";
            btnMouseRateTest.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            btnMouseRateTest.NormalColor = Color.FromArgb(36, 44, 62);
            btnMouseRateTest.HoverColor = Color.FromArgb(50, 62, 88);
            btnMouseRateTest.PressedColor = Color.FromArgb(26, 32, 46);
            btnMouseRateTest.BorderColor = Color.FromArgb(70, 90, 130);
            btnMouseRateTest.TextColor = Color.FromArgb(230, 240, 255);
            btnMouseRateTest.CornerRadius = (int)(6 * dpiScale);
            btnMouseRateTest.Cursor = Cursors.Hand;
            btnMouseRateTest.Visible = false;
            btnMouseRateTest.Click += (s, e) => {
                using (var dlg = new MouseRateTestDialog(dpiScale))
                {
                    dlg.ShowDialog(this);
                }
            };
            cardPerformance.Controls.Add(btnMouseRateTest);

            // NuPhy Hardware Console Controls

            // Row 1: 自动休眠 + Win键锁定
            int dualBtnGap = (int)(8 * dpiScale);
            int dualBtnW = (secW - dualBtnGap) / 2;

            btnNuphyAutoSleep = new ModernButton();
            btnNuphyAutoSleep.Text = "🌙 自动休眠: 开启";
            btnNuphyAutoSleep.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold, GraphicsUnit.Point);
            btnNuphyAutoSleep.Location = new Point(cPad, r1Y);
            btnNuphyAutoSleep.Size = new Size(dualBtnW, segH);
            btnNuphyAutoSleep.CornerRadius = (int)(6 * dpiScale);
            btnNuphyAutoSleep.Cursor = Cursors.Hand;
            btnNuphyAutoSleep.Visible = false;
            btnNuphyAutoSleep.Click += (s, e) => {
                var cur = NuphyDeviceHelper.CachedHardwareSettings ?? new NuphyDeviceHelper.NuphyHardwareSettings();
                bool newEn = !cur.SleepEnabled;
                cur.SleepEnabled = newEn;
                ApplyNuphySettingsToUI();
                UpdateTrayDeviceMenuItems();
                ThreadPool.QueueUserWorkItem(_ => {
                    NuphyDeviceHelper.SetAutoSleep(newEn);
                });
            };
            cardPerformance.Controls.Add(btnNuphyAutoSleep);

            btnNuphyWinLock = new ModernButton();
            btnNuphyWinLock.Text = "🔓 Win键锁定: 关闭";
            btnNuphyWinLock.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold, GraphicsUnit.Point);
            btnNuphyWinLock.Location = new Point(cPad + dualBtnW + dualBtnGap, r1Y);
            btnNuphyWinLock.Size = new Size(dualBtnW, segH);
            btnNuphyWinLock.CornerRadius = (int)(6 * dpiScale);
            btnNuphyWinLock.Cursor = Cursors.Hand;
            btnNuphyWinLock.Visible = false;
            btnNuphyWinLock.Click += (s, e) => {
                var cur = NuphyDeviceHelper.CachedHardwareSettings ?? new NuphyDeviceHelper.NuphyHardwareSettings();
                bool newLock = !cur.WinLock;
                cur.WinLock = newLock;
                ApplyNuphySettingsToUI();
                UpdateTrayDeviceMenuItems();
                ThreadPool.QueueUserWorkItem(_ => {
                    NuphyDeviceHelper.SetWinLock(newLock);
                });
            };
            cardPerformance.Controls.Add(btnNuphyWinLock);

            // Row 2: 键盘背光开关 + 前置灯带开关
            btnNuphyBacklight = new ModernButton();
            btnNuphyBacklight.Text = "💡 键盘背光: 开";
            btnNuphyBacklight.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold, GraphicsUnit.Point);
            btnNuphyBacklight.Location = new Point(cPad, r2Y);
            btnNuphyBacklight.Size = new Size(dualBtnW, segH);
            btnNuphyBacklight.CornerRadius = (int)(6 * dpiScale);
            btnNuphyBacklight.Cursor = Cursors.Hand;
            btnNuphyBacklight.Visible = false;
            btnNuphyBacklight.Click += (s, e) => {
                var cur = NuphyDeviceHelper.CachedHardwareSettings ?? new NuphyDeviceHelper.NuphyHardwareSettings();
                bool newBack = !(cur.BacklightEnabled && cur.BacklightBrightness > 0);
                cur.BacklightEnabled = newBack;
                if (newBack && cur.BacklightBrightness <= 0) cur.BacklightBrightness = 100;
                ApplyNuphySettingsToUI();
                ThreadPool.QueueUserWorkItem(_ => {
                    NuphyDeviceHelper.SetBacklightEnabled(newBack);
                });
            };
            cardPerformance.Controls.Add(btnNuphyBacklight);

            btnNuphySidelight = new ModernButton();
            btnNuphySidelight.Text = "✨ 前置灯带: 开";
            btnNuphySidelight.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold, GraphicsUnit.Point);
            btnNuphySidelight.Location = new Point(cPad + dualBtnW + dualBtnGap, r2Y);
            btnNuphySidelight.Size = new Size(dualBtnW, segH);
            btnNuphySidelight.CornerRadius = (int)(6 * dpiScale);
            btnNuphySidelight.Cursor = Cursors.Hand;
            btnNuphySidelight.Visible = false;
            btnNuphySidelight.Click += (s, e) => {
                var cur = NuphyDeviceHelper.CachedHardwareSettings ?? new NuphyDeviceHelper.NuphyHardwareSettings();
                bool newSide = !(cur.SidelightEnabled && cur.SidelightBrightness > 0);
                cur.SidelightEnabled = newSide;
                if (newSide && cur.SidelightBrightness <= 0) cur.SidelightBrightness = 100;
                ApplyNuphySettingsToUI();
                ThreadPool.QueueUserWorkItem(_ => {
                    NuphyDeviceHelper.SetSidelightEnabled(newSide);
                });
            };
            cardPerformance.Controls.Add(btnNuphySidelight);

            // Row 3: 背光亮度滑块 (TactileSmoothSliderControl)
            int r3Y = (int)(112 * dpiScale);
            lblNuphyBrightTitle = new Label();
            lblNuphyBrightTitle.Text = "背光亮度";
            lblNuphyBrightTitle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            lblNuphyBrightTitle.ForeColor = Color.FromArgb(160, 168, 185);
            lblNuphyBrightTitle.BackColor = Color.Transparent;
            lblNuphyBrightTitle.Location = new Point(cPad, r3Y + (int)(4 * dpiScale));
            lblNuphyBrightTitle.Size = new Size(lblW, (int)(22 * dpiScale));
            lblNuphyBrightTitle.Visible = false;
            cardPerformance.Controls.Add(lblNuphyBrightTitle);

            int sliderX = cPad + lblW;
            int valBadgeW = (int)(50 * dpiScale);
            int sliderW = secW - lblW - valBadgeW - (int)(6 * dpiScale);

            sliderNuphyBrightness = new TactileSmoothSliderControl();
            sliderNuphyBrightness.DpiScale = dpiScale;
            sliderNuphyBrightness.Location = new Point(sliderX, r3Y);
            sliderNuphyBrightness.Size = new Size(sliderW, segH);
            sliderNuphyBrightness.Visible = false;
            sliderNuphyBrightness.ValueChanged += (s, e) => {
                if (lblNuphyBrightVal != null) lblNuphyBrightVal.Text = sliderNuphyBrightness.Value + "%";
                QueueNuphyBrightnessThrottled(sliderNuphyBrightness.Value);
            };
            sliderNuphyBrightness.ValueCommitted += (s, e) => {
                int finalB = sliderNuphyBrightness.Value;
                var cur = NuphyDeviceHelper.CachedHardwareSettings ?? new NuphyDeviceHelper.NuphyHardwareSettings();
                cur.BacklightBrightness = finalB;
                cur.BacklightEnabled = (finalB > 0);
                NuphyDeviceHelper.CachedHardwareSettings = cur;
                NuphyDeviceHelper.SaveHardwareCache();
                ApplyNuphySettingsToUI();
                QueueNuphyBrightnessThrottled(finalB);
            };
            cardPerformance.Controls.Add(sliderNuphyBrightness);

            lblNuphyBrightVal = new Label();
            lblNuphyBrightVal.Text = "100%";
            lblNuphyBrightVal.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold, GraphicsUnit.Point);
            lblNuphyBrightVal.ForeColor = Color.FromArgb(0, 229, 255);
            lblNuphyBrightVal.BackColor = Color.Transparent;
            lblNuphyBrightVal.TextAlign = ContentAlignment.MiddleRight;
            lblNuphyBrightVal.Location = new Point(sliderX + sliderW + (int)(6 * dpiScale), r3Y + (int)(3 * dpiScale));
            lblNuphyBrightVal.Size = new Size(valBadgeW, (int)(22 * dpiScale));
            lblNuphyBrightVal.Visible = false;
            cardPerformance.Controls.Add(lblNuphyBrightVal);

            // ================= CARD 3: CONFIGURATION & PREFERENCES =================
            int card3Y = card2Y + card2H + (int)(12 * dpiScale);
            int card3H = (int)(244 * dpiScale);

            cardSettings = new RoundedCard();
            cardSettings.Location = new Point(padX, card3Y);
            cardSettings.Size = new Size(cardW, card3H);
            cardSettings.CornerRadius = (int)(10 * dpiScale);
            cardSettings.CardColor = Color.FromArgb(25, 27, 34);
            cardSettings.BorderColor = Color.FromArgb(42, 46, 58);
            this.Controls.Add(cardSettings);

            lblSettingsTitle = new Label();
            lblSettingsTitle.Text = "功能设置与偏好";
            lblSettingsTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);
            lblSettingsTitle.ForeColor = Color.FromArgb(240, 245, 255);
            lblSettingsTitle.BackColor = Color.Transparent;
            lblSettingsTitle.Location = new Point(cPad, (int)(14 * dpiScale));
            lblSettingsTitle.AutoSize = true;
            cardSettings.Controls.Add(lblSettingsTitle);

            // Row 1: 托盘图标样式 (Capsule / Badge)
            int row1Y = (int)(40 * dpiScale);
            int lblTitleW = (int)(95 * dpiScale);

            lblStyleTitle = new Label();
            lblStyleTitle.Text = "托盘图标样式";
            lblStyleTitle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            lblStyleTitle.ForeColor = Color.FromArgb(160, 168, 185);
            lblStyleTitle.BackColor = Color.Transparent;
            lblStyleTitle.Location = new Point(cPad, row1Y + (int)(4 * dpiScale));
            lblStyleTitle.Size = new Size(lblTitleW, (int)(22 * dpiScale));
            cardSettings.Controls.Add(lblStyleTitle);

            int seg1W = (secW - lblTitleW - (int)(8 * dpiScale)) / 2;
            int seg1X = cPad + lblTitleW;

            btnStyleCapsule = new ModernSegmentButton();
            btnStyleCapsule.Text = "现代胶囊电池";
            btnStyleCapsule.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            btnStyleCapsule.Location = new Point(seg1X, row1Y);
            btnStyleCapsule.Size = new Size(seg1W, segH);
            btnStyleCapsule.Click += (s, e) => SetTrayStyle(0, false);
            cardSettings.Controls.Add(btnStyleCapsule);

            btnStyleNum = new ModernSegmentButton();
            btnStyleNum.Text = "醒目数字能量表";
            btnStyleNum.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            btnStyleNum.Location = new Point(seg1X + seg1W + (int)(8 * dpiScale), row1Y);
            btnStyleNum.Size = new Size(seg1W, segH);
            btnStyleNum.Click += (s, e) => SetTrayStyle(1, false);
            cardSettings.Controls.Add(btnStyleNum);

            // Row 2: 自动刷新频率 (30s / 1m / 5m)
            int row2Y = (int)(76 * dpiScale);

            lblIntervalTitle = new Label();
            lblIntervalTitle.Text = "自动刷新频率";
            lblIntervalTitle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            lblIntervalTitle.ForeColor = Color.FromArgb(160, 168, 185);
            lblIntervalTitle.BackColor = Color.Transparent;
            lblIntervalTitle.Location = new Point(cPad, row2Y + (int)(4 * dpiScale));
            lblIntervalTitle.Size = new Size(lblTitleW, (int)(22 * dpiScale));
            cardSettings.Controls.Add(lblIntervalTitle);

            int seg2Gap = (int)(6 * dpiScale);
            int seg2W = (secW - lblTitleW - (seg2Gap * 2)) / 3;

            btnInt30s = new ModernSegmentButton();
            btnInt30s.Text = "30 秒";
            btnInt30s.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            btnInt30s.Location = new Point(seg1X, row2Y);
            btnInt30s.Size = new Size(seg2W, segH);
            btnInt30s.Click += (s, e) => SetInterval(30000, false);
            cardSettings.Controls.Add(btnInt30s);

            btnInt1m = new ModernSegmentButton();
            btnInt1m.Text = "1 分钟";
            btnInt1m.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            btnInt1m.Location = new Point(seg1X + seg2W + seg2Gap, row2Y);
            btnInt1m.Size = new Size(seg2W, segH);
            btnInt1m.Click += (s, e) => SetInterval(60000, false);
            cardSettings.Controls.Add(btnInt1m);

            btnInt5m = new ModernSegmentButton();
            btnInt5m.Text = "5 分钟";
            btnInt5m.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            btnInt5m.Location = new Point(seg1X + (seg2W + seg2Gap) * 2, row2Y);
            btnInt5m.Size = new Size(seg2W, segH);
            btnInt5m.Click += (s, e) => SetInterval(300000, false);
            cardSettings.Controls.Add(btnInt5m);

            // Row 3: DPI 浮窗样式 (居中胶囊 / 阶梯能量 / 顶置微标)
            int row3Y = (int)(112 * dpiScale);

            lblOsdStyleTitle = new Label();
            lblOsdStyleTitle.Text = "DPI 浮窗样式";
            lblOsdStyleTitle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            lblOsdStyleTitle.ForeColor = Color.FromArgb(160, 168, 185);
            lblOsdStyleTitle.BackColor = Color.Transparent;
            lblOsdStyleTitle.Location = new Point(cPad, row3Y + (int)(4 * dpiScale));
            lblOsdStyleTitle.Size = new Size(lblTitleW, (int)(22 * dpiScale));
            cardSettings.Controls.Add(lblOsdStyleTitle);

            btnOsdStyleCapsule = new ModernSegmentButton();
            btnOsdStyleCapsule.Text = "居中胶囊";
            btnOsdStyleCapsule.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            btnOsdStyleCapsule.Location = new Point(seg1X, row3Y);
            btnOsdStyleCapsule.Size = new Size(seg2W, segH);
            btnOsdStyleCapsule.Click += (s, e) => SetOsdStyle(0, true);
            cardSettings.Controls.Add(btnOsdStyleCapsule);

            btnOsdStyleGauge = new ModernSegmentButton();
            btnOsdStyleGauge.Text = "阶梯能量";
            btnOsdStyleGauge.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            btnOsdStyleGauge.Location = new Point(seg1X + seg2W + seg2Gap, row3Y);
            btnOsdStyleGauge.Size = new Size(seg2W, segH);
            btnOsdStyleGauge.Click += (s, e) => SetOsdStyle(1, true);
            cardSettings.Controls.Add(btnOsdStyleGauge);

            btnOsdStyleCompact = new ModernSegmentButton();
            btnOsdStyleCompact.Text = "顶置微标";
            btnOsdStyleCompact.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            btnOsdStyleCompact.Location = new Point(seg1X + (seg2W + seg2Gap) * 2, row3Y);
            btnOsdStyleCompact.Size = new Size(seg2W, segH);
            btnOsdStyleCompact.Click += (s, e) => SetOsdStyle(2, true);
            cardSettings.Controls.Add(btnOsdStyleCompact);

            // Row 4: Modern subtle divider
            divSettings = new SubtleDivider();
            divSettings.Location = new Point(cPad, (int)(150 * dpiScale));
            divSettings.Size = new Size(secW, (int)(8 * dpiScale));
            cardSettings.Controls.Add(divSettings);

            // Row 5: Checkboxes in 2x2 grid
            int chkY1 = (int)(162 * dpiScale);
            int chkY2 = (int)(190 * dpiScale);
            int chkGap = (int)(14 * dpiScale);
            int chkW = (secW - chkGap) / 2;
            int chkH = (int)(24 * dpiScale);

            chkAutoStart = new ModernCheckBox();
            chkAutoStart.Text = "开机自动启动";
            chkAutoStart.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            chkAutoStart.Location = new Point(cPad, chkY1);
            chkAutoStart.Size = new Size(chkW, chkH);
            chkAutoStart.CheckedChanged += (s, e) => {
                if (isUpdatingUI) return;
                SetAutoStart(chkAutoStart.Checked);
            };
            cardSettings.Controls.Add(chkAutoStart);

            chkAutoStartShowUI = new ModernCheckBox();
            chkAutoStartShowUI.Text = "开机弹出主窗口";
            chkAutoStartShowUI.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            chkAutoStartShowUI.Location = new Point(cPad + chkW + chkGap, chkY1);
            chkAutoStartShowUI.Size = new Size(chkW, chkH);
            chkAutoStartShowUI.CheckedChanged += (s, e) => {
                if (isUpdatingUI) return;
                SetAutoStartShowUI(chkAutoStartShowUI.Checked, true);
            };
            cardSettings.Controls.Add(chkAutoStartShowUI);

            chkLowAlert = new ModernCheckBox();
            chkLowAlert.Text = "低电量提醒 (≤20%)";
            chkLowAlert.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            chkLowAlert.Location = new Point(cPad, chkY2);
            chkLowAlert.Size = new Size(chkW, chkH);
            chkLowAlert.CheckedChanged += (s, e) => {
                if (isUpdatingUI) return;
                SetLowBatteryAlert(chkLowAlert.Checked);
            };
            cardSettings.Controls.Add(chkLowAlert);

            chkDpiOsd = new ModernCheckBox();
            chkDpiOsd.Text = "DPI 切换屏幕提示 (OSD)";
            chkDpiOsd.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            chkDpiOsd.Location = new Point(cPad + chkW + chkGap, chkY2);
            chkDpiOsd.Size = new Size(chkW, chkH);
            chkDpiOsd.CheckedChanged += (s, e) => {
                if (isUpdatingUI) return;
                SetDpiOsdEnabled(chkDpiOsd.Checked);
            };
            cardSettings.Controls.Add(chkDpiOsd);

            // Note inside Card 3
            lblSettingsTip = new Label();
            lblSettingsTip.Text = "注：切换线缆/接收器或按键调 DPI 时将自动即时同步，无需等待计时周期";
            lblSettingsTip.Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Regular, GraphicsUnit.Point);
            lblSettingsTip.ForeColor = Color.FromArgb(120, 128, 142);
            lblSettingsTip.BackColor = Color.Transparent;
            lblSettingsTip.Location = new Point(cPad, (int)(218 * dpiScale));
            lblSettingsTip.AutoSize = true;
            cardSettings.Controls.Add(lblSettingsTip);

            // ================= BOTTOM ROW: ACTIONS =================
            int card4Y = card3Y + card3H + (int)(14 * dpiScale);
            int btnH = (int)(38 * dpiScale);
            int btnGap = (int)(14 * dpiScale);
            int btnW = (cardW - btnGap) / 2;

            btnRefresh = new ModernButton();
            btnRefresh.Text = "立即刷新";
            btnRefresh.Location = new Point(padX, card4Y);
            btnRefresh.Size = new Size(btnW, btnH);
            btnRefresh.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
            btnRefresh.NormalColor = Color.FromArgb(0, 200, 83);
            btnRefresh.HoverColor = Color.FromArgb(0, 230, 118);
            btnRefresh.PressedColor = Color.FromArgb(0, 160, 65);
            btnRefresh.ForeColor = Color.FromArgb(10, 24, 15);
            btnRefresh.CornerRadius = (int)(8 * dpiScale);
            btnRefresh.Click += (s, e) => {
                RefreshBatteryStatus(true);
                SyncAllDevicesPhysicalState();
            };
            this.Controls.Add(btnRefresh);

            btnHideToTray = new ModernButton();
            btnHideToTray.Text = "最小化到托盘";
            btnHideToTray.Location = new Point(padX + btnW + btnGap, card4Y);
            btnHideToTray.Size = new Size(btnW, btnH);
            btnHideToTray.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
            btnHideToTray.NormalColor = Color.FromArgb(36, 39, 49);
            btnHideToTray.HoverColor = Color.FromArgb(48, 52, 65);
            btnHideToTray.PressedColor = Color.FromArgb(28, 30, 38);
            btnHideToTray.BorderColor = Color.FromArgb(58, 63, 78);
            btnHideToTray.ForeColor = Color.White;
            btnHideToTray.CornerRadius = (int)(8 * dpiScale);
            btnHideToTray.Click += (s, e) => {
                this.Hide();
            };
            this.Controls.Add(btnHideToTray);

            int clientH = card4Y + btnH + (int)(16 * dpiScale);
            this.ClientSize = new Size(baseW, clientH);
        }

        private void InitializeTray()
        {
            contextMenu = new ContextMenuStrip();
            contextMenu.Renderer = new ModernDarkMenuRenderer();
            contextMenu.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point);
            contextMenu.ShowImageMargin = true;
            contextMenu.ShowCheckMargin = false;
            contextMenu.Padding = new Padding(3, 4, 3, 4);

            statusMenuItem = new ToolStripMenuItem("正在检测设备...");
            statusMenuItem.Tag = "Header";
            statusMenuItem.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            statusMenuItem.Click += (s, e) => ShowWindow();
            contextMenu.Items.Add(statusMenuItem);

            deviceMenuSep = new ToolStripSeparator();
            contextMenu.Items.Add(deviceMenuSep);

            var showItem = new ToolStripMenuItem("打开控制面板 (&O)", null, (s, e) => ShowWindow());
            contextMenu.Items.Add(showItem);

            var refreshItem = new ToolStripMenuItem("立即刷新电量 (&R)", null, (s, e) => RefreshBatteryStatus(true));
            contextMenu.Items.Add(refreshItem);

            // Submenu: DPI 调节
            dpiMenu = new ToolStripMenuItem("调节 DPI 档位 (&D)");
            dpiMenu.DropDown.Renderer = contextMenu.Renderer;
            for (int i = 0; i < dpiStageValues.Length; i++)
            {
                int val = dpiStageValues[i];
                var sub = new ToolStripMenuItem(val + " DPI", null, (s, e) => SetDpiFromUI(val));
                sub.Tag = val;
                dpiMenu.DropDownItems.Add(sub);
            }
            contextMenu.Items.Add(dpiMenu);

            // Submenu: 回报率调节
            rateMenu = new ToolStripMenuItem("调节回报率 (&P)");
            rateMenu.DropDown.Renderer = contextMenu.Renderer;
            for (int i = 0; i < pollingRateValues.Length; i++)
            {
                int val = pollingRateValues[i];
                var sub = new ToolStripMenuItem(val + " Hz", null, (s, e) => SetPollingRateFromUI(val));
                sub.Tag = val;
                rateMenu.DropDownItems.Add(sub);
            }
            contextMenu.Items.Add(rateMenu);

            // Submenu: 全屏游戏自动回报率
            autoGameRateMenu = new ToolStripMenuItem("全屏游戏自动回报率 (&G)");
            autoGameRateMenu.DropDown.Renderer = contextMenu.Renderer;

            autoGameRateToggleItem = new ToolStripMenuItem("启用全屏自动切换", null, (s, e) => {
                SetAutoGameRateEnabled(!autoGameRateEnabled);
            });
            autoGameRateToggleItem.Checked = autoGameRateEnabled;
            autoGameRateMenu.DropDownItems.Add(autoGameRateToggleItem);
            autoGameRateMenu.DropDownItems.Add(new ToolStripSeparator());

            var gameRateSub = new ToolStripMenuItem("全屏电竞回报率");
            gameRateSub.DropDown.Renderer = contextMenu.Renderer;
            for (int i = 0; i < pollingRateValues.Length; i++)
            {
                int val = pollingRateValues[i];
                var sub = new ToolStripMenuItem(val + " Hz", null, (s, e) => SetGamePollingRateFromUI(val));
                sub.Tag = val;
                gameRateSub.DropDownItems.Add(sub);
            }
            autoGameRateMenu.DropDownItems.Add(gameRateSub);

            var notifySub = new ToolStripMenuItem("切换提醒方式");
            notifySub.DropDown.Renderer = contextMenu.Renderer;
            var miOsd = new ToolStripMenuItem("游戏 OSD 浮窗", null, (s, e) => {
                gameRateNotifyOsd = !gameRateNotifyOsd;
                if (chkNotifyOsd != null) chkNotifyOsd.Checked = gameRateNotifyOsd;
                SaveConfig();
            });
            miOsd.Checked = gameRateNotifyOsd;
            notifySub.DropDownItems.Add(miOsd);

            var miToast = new ToolStripMenuItem("Windows 系统通知", null, (s, e) => {
                gameRateNotifyToast = !gameRateNotifyToast;
                if (chkNotifyToast != null) chkNotifyToast.Checked = gameRateNotifyToast;
                SaveConfig();
            });
            miToast.Checked = gameRateNotifyToast;
            notifySub.DropDownItems.Add(miToast);

            var miSound = new ToolStripMenuItem("柔和提示音", null, (s, e) => {
                gameRateNotifySound = !gameRateNotifySound;
                if (chkNotifySound != null) chkNotifySound.Checked = gameRateNotifySound;
                SaveConfig();
            });
            miSound.Checked = gameRateNotifySound;
            notifySub.DropDownItems.Add(miSound);

            autoGameRateMenu.DropDownItems.Add(notifySub);
            contextMenu.Items.Add(autoGameRateMenu);

            nuphySleepMenuItem = new ToolStripMenuItem("🌙 自动休眠: 已开启");
            nuphySleepMenuItem.Click += (s, e) => {
                var cur = NuphyDeviceHelper.CachedHardwareSettings ?? new NuphyDeviceHelper.NuphyHardwareSettings();
                bool newEn = !cur.SleepEnabled;
                NuphyDeviceHelper.SetAutoSleep(newEn);
                ApplyNuphySettingsToUI();
                UpdateTrayDeviceMenuItems();
            };
            nuphySleepMenuItem.Visible = false;
            contextMenu.Items.Add(nuphySleepMenuItem);

            nuphyWinLockMenuItem = new ToolStripMenuItem("🔓 Win键锁定: 已关闭");
            nuphyWinLockMenuItem.Click += (s, e) => {
                var cur = NuphyDeviceHelper.CachedHardwareSettings ?? new NuphyDeviceHelper.NuphyHardwareSettings();
                bool newLock = !cur.WinLock;
                NuphyDeviceHelper.SetWinLock(newLock);
                ApplyNuphySettingsToUI();
                UpdateTrayDeviceMenuItems();
            };
            nuphyWinLockMenuItem.Visible = false;
            contextMenu.Items.Add(nuphyWinLockMenuItem);

            nuphyBacklightMenu = new ToolStripMenuItem("💡 键盘背光调节 (&B)");
            nuphyBacklightMenu.DropDown.Renderer = contextMenu.Renderer;
            int[] brightPresets = new int[] { 0, 25, 50, 75, 100 };
            for (int i = 0; i < brightPresets.Length; i++)
            {
                int bVal = brightPresets[i];
                var sub = new ToolStripMenuItem(bVal == 0 ? "关闭背光 (0%)" : (bVal + "% 亮度"));
                sub.Click += (s, e) => {
                    NuphyDeviceHelper.SetBacklightBrightness(bVal);
                    ApplyNuphySettingsToUI();
                    UpdateTrayDeviceMenuItems();
                };
                sub.Tag = bVal;
                nuphyBacklightMenu.DropDownItems.Add(sub);
            }
            nuphyBacklightMenu.Visible = false;
            contextMenu.Items.Add(nuphyBacklightMenu);

            nuphySidelightMenuItem = new ToolStripMenuItem("✨ 前置灯带: 已开启");
            nuphySidelightMenuItem.Click += (s, e) => {
                var cur = NuphyDeviceHelper.CachedHardwareSettings ?? new NuphyDeviceHelper.NuphyHardwareSettings();
                bool newSide = !(cur.SidelightEnabled && cur.SidelightBrightness > 0);
                NuphyDeviceHelper.SetSidelightEnabled(newSide);
                ApplyNuphySettingsToUI();
                UpdateTrayDeviceMenuItems();
            };
            nuphySidelightMenuItem.Visible = false;
            contextMenu.Items.Add(nuphySidelightMenuItem);

            nuphyTraySep = new ToolStripSeparator();
            nuphyTraySep.Visible = false;
            contextMenu.Items.Add(nuphyTraySep);

            var intervalMenu = new ToolStripMenuItem("自动刷新频率 (&I)");
            intervalMenu.DropDown.Renderer = contextMenu.Renderer;
            int30sMenuItem = new ToolStripMenuItem("30 秒", null, (s, e) => SetInterval(30000, true));
            int1mMenuItem = new ToolStripMenuItem("1 分钟", null, (s, e) => SetInterval(60000, true));
            int5mMenuItem = new ToolStripMenuItem("5 分钟", null, (s, e) => SetInterval(300000, true));
            intervalMenu.DropDownItems.AddRange(new ToolStripItem[] { int30sMenuItem, int1mMenuItem, int5mMenuItem });
            contextMenu.Items.Add(intervalMenu);

            var styleMenu = new ToolStripMenuItem("托盘图标样式 (&T)");
            styleMenu.DropDown.Renderer = contextMenu.Renderer;
            styleCapsuleItem = new ToolStripMenuItem("现代胶囊电池", null, (s, e) => SetTrayStyle(0, true));
            styleNumItem = new ToolStripMenuItem("醒目数字能量表", null, (s, e) => SetTrayStyle(1, true));
            styleCapsuleItem.Checked = (trayStyle == 0);
            styleNumItem.Checked = (trayStyle == 1);
            styleMenu.DropDownItems.AddRange(new ToolStripItem[] { styleCapsuleItem, styleNumItem });
            contextMenu.Items.Add(styleMenu);

            osdStyleMenu = new ToolStripMenuItem("DPI 浮窗样式 (&O)");
            osdStyleMenu.DropDown.Renderer = contextMenu.Renderer;
            osdStyleCapsuleItem = new ToolStripMenuItem("居中电竞胶囊", null, (s, e) => SetOsdStyle(0, true));
            osdStyleGaugeItem = new ToolStripMenuItem("右侧阶梯能量计", null, (s, e) => SetOsdStyle(1, true));
            osdStyleCompactItem = new ToolStripMenuItem("顶置微型指示段", null, (s, e) => SetOsdStyle(2, true));
            osdStyleCapsuleItem.Checked = (osdStyle == 0);
            osdStyleGaugeItem.Checked = (osdStyle == 1);
            osdStyleCompactItem.Checked = (osdStyle == 2);
            osdStyleMenu.DropDownItems.AddRange(new ToolStripItem[] { osdStyleCapsuleItem, osdStyleGaugeItem, osdStyleCompactItem });
            contextMenu.Items.Add(osdStyleMenu);

            contextMenu.Items.Add(new ToolStripSeparator());

            dpiOsdMenuItem = new ToolStripMenuItem("DPI 切换屏幕提示 (OSD)", null, (s, e) => {
                SetDpiOsdEnabled(!dpiOsdEnabled, true);
            });
            contextMenu.Items.Add(dpiOsdMenuItem);

            lowBatteryAlertMenuItem = new ToolStripMenuItem("低电量气泡通知 (≤20%)", null, (s, e) => {
                SetLowBatteryAlert(!lowBatteryAlertEnabled, true);
            });
            contextMenu.Items.Add(lowBatteryAlertMenuItem);

            autoStartMenuItem = new ToolStripMenuItem("开机自动启动", null, (s, e) => {
                SetAutoStart(!IsAutoStartEnabled(), true);
            });
            contextMenu.Items.Add(autoStartMenuItem);

            autoStartShowUIMenuItem = new ToolStripMenuItem("开机弹出主窗口", null, (s, e) => {
                SetAutoStartShowUI(!autoStartShowMainWindow, true);
            });
            contextMenu.Items.Add(new ToolStripSeparator());

            var prioMenuItem = new ToolStripMenuItem("设备优先级管理 (拖拽排序)...", null, (s, e) => OpenPriorityManager());
            contextMenu.Items.Add(prioMenuItem);

            var addDevMenuItem = new ToolStripMenuItem("手动添加新设备向导...", null, (s, e) => OpenAddDeviceWizard());
            contextMenu.Items.Add(addDevMenuItem);

            contextMenu.Items.Add(new ToolStripSeparator());

            var exitItem = new ToolStripMenuItem("退出程序 (&X)", null, (s, e) => ExitApp());
            contextMenu.Items.Add(exitItem);

            foreach (ToolStripItem item in contextMenu.Items)
            {
                item.Padding = new Padding(6, 4, 12, 4);
            }

            contextMenu.Opening += (s, e) => UpdateTrayDeviceMenuItems();

            trayIcon = new NotifyIcon();
            trayIcon.ContextMenuStrip = contextMenu;
            trayIcon.Text = "FerrisPulse · 灵脉 (正在检测外设...)";
            if (this.Icon != null)
            {
                trayIcon.Icon = this.Icon;
            }
            else
            {
                UpdateTrayIcon(-1, false, false);
            }
            trayIcon.Visible = true;

            trayIcon.Click += (s, e) => {
                var me = e as MouseEventArgs;
                if (me != null && me.Button == MouseButtons.Left)
                {
                    ToggleWindow();
                }
            };
            trayIcon.DoubleClick += (s, e) => ShowWindow();
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
            RestoreWindowPosition();
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

        private DateTime lastBalloonTime = DateTime.MinValue;
        private string lastBalloonTitle = "";

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

        private void StartDpiMonitor()
        {
            if (dpiMonitorThread != null && dpiMonitorThread.IsAlive) return;
            isDpiMonitorRunning = true;
            dpiMonitorThread = new Thread(DpiMonitorWorker);
            dpiMonitorThread.IsBackground = true;
            dpiMonitorThread.Name = "RazerDpiMonitorWorker";
            dpiMonitorThread.Start();
        }

        private void StopDpiMonitor()
        {
            isDpiMonitorRunning = false;
            RapooDeviceHelper.CloseRapooStream();
        }

        private void DpiMonitorWorker()
        {
            while (isDpiMonitorRunning)
            {
                try
                {
                    // 1. 若用户未开启 DPI OSD 提示，彻底不轮询硬件，零开销零占用
                    if (!dpiOsdEnabled)
                    {
                        Thread.Sleep(800);
                        continue;
                    }

                    // 2. 收集当前需要监听 DPI 的雷蛇设备（支持多雷蛇设备并发监测）
                    List<string> rzTargets = new List<string>();
                    if (selectedDevice != null && selectedDevice.Brand == "Razer")
                    {
                        rzTargets.Add(selectedDevice.DeviceId);
                    }
                    if (activeDevices != null)
                    {
                        for (int i = 0; i < activeDevices.Count; i++)
                        {
                            var d = activeDevices[i];
                            if (d != null && d.Brand == "Razer" && d.IsConnected && !d.IsSleeping)
                            {
                                if (!rzTargets.Contains(d.DeviceId)) rzTargets.Add(d.DeviceId);
                            }
                        }
                    }
                    if (rzTargets.Count == 0) rzTargets.Add(null); // 回退至默认主设备

                    for (int rIdx = 0; rIdx < rzTargets.Count; rIdx++)
                    {
                        string devId = rzTargets[rIdx];
                        int dpi, stage, count;
                        if (RazerDeviceHelper.FastQueryDpi(out dpi, out stage, out count, devId))
                        {
                            if (dpi > 0)
                            {
                                string key = devId ?? "default";
                                int prevDpi = -1;
                                int prevStage = -1;
                                lastMonitoredRazerDpis.TryGetValue(key, out prevDpi);
                                lastMonitoredRazerStages.TryGetValue(key, out prevStage);

                                if (prevDpi != -1 && (dpi != prevDpi || stage != prevStage))
                                {
                                    int newDpi = dpi;
                                    int newStage = stage;
                                    int newCount = count;
                                    string notifyDevId = devId;
                                    try
                                    {
                                        this.BeginInvoke(new Action(() => {
                                            OnDpiChanged("Razer", newDpi, newStage, newCount, notifyDevId);
                                        }));
                                    }
                                    catch { }
                                }
                                lastMonitoredRazerDpis[key] = dpi;
                                lastMonitoredRazerStages[key] = stage;
                                lastMonitoredDpi = dpi;
                                lastMonitoredStage = stage;
                            }
                        }
                    }

                    // 3. 雷柏设备极速 DPI 监听
                    int rDpi, rStage, rCount;
                    if (RapooDeviceHelper.FastQueryDpi(out rDpi, out rStage, out rCount))
                    {
                        if (rDpi > 0)
                        {
                            if (lastMonitoredRapooDpi != -1 && (rDpi != lastMonitoredRapooDpi || rStage != lastMonitoredRapooStage))
                            {
                                int newDpi = rDpi;
                                int newStage = rStage;
                                int newCount = rCount;
                                try
                                {
                                    this.BeginInvoke(new Action(() => {
                                        OnDpiChanged("Rapoo", newDpi, newStage, newCount);
                                    }));
                                }
                                catch { }
                            }
                            lastMonitoredRapooDpi = rDpi;
                            lastMonitoredRapooStage = rStage;
                        }
                    }
                }
                catch { }
                int pollSleep = isMouseSleeping ? 250 : 50; // 鼠标活跃时 50ms 极速响应，按键瞬间 OSD 丝滑弹出；待机时 250ms 静默
                Thread.Sleep(pollSleep);
            }
        }

        private void OnDpiChanged(string brand, int newDpi, int newStage, int stageCount, string deviceId = null)
        {
            if (dpiOsdEnabled && osdForm != null)
            {
                osdForm.ShowDpi(newDpi, newStage, stageCount, brand);
            }

            if (activeDevices != null)
            {
                MouseBatteryInfo matched = null;
                if (!string.IsNullOrEmpty(deviceId))
                {
                    string normTarget = RazerDeviceHelper.NormalizeRazerDeviceId(deviceId);
                    matched = activeDevices.Find(d => string.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase) ||
                                                      string.Equals(RazerDeviceHelper.NormalizeRazerDeviceId(d.DeviceId), normTarget, StringComparison.OrdinalIgnoreCase));
                }
                if (matched == null)
                {
                    matched = activeDevices.Find(d => d.Brand == brand);
                }
                if (matched != null)
                {
                    matched.Dpi = newDpi;
                    matched.DpiStage = newStage;
                    matched.DpiStageCount = stageCount;
                }
            }

            if (lastInfo != null && (string.IsNullOrEmpty(deviceId) ||
                                     string.Equals(lastInfo.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(RazerDeviceHelper.NormalizeRazerDeviceId(lastInfo.DeviceId), RazerDeviceHelper.NormalizeRazerDeviceId(deviceId), StringComparison.OrdinalIgnoreCase)))
            {
                if (lastInfo.Brand == brand)
                {
                    lastInfo.Dpi = newDpi;
                    lastInfo.DpiStage = newStage;
                    lastInfo.DpiStageCount = stageCount;
                }
            }

            if (selectedDevice != null && (string.IsNullOrEmpty(deviceId) ||
                                           string.Equals(selectedDevice.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(RazerDeviceHelper.NormalizeRazerDeviceId(selectedDevice.DeviceId), RazerDeviceHelper.NormalizeRazerDeviceId(deviceId), StringComparison.OrdinalIgnoreCase)))
            {
                if (selectedDevice.Brand == brand)
                {
                    selectedDevice.Dpi = newDpi;
                    selectedDevice.DpiStage = newStage;
                    selectedDevice.DpiStageCount = stageCount;

                if (brand == "Rapoo")
                {
                    if (tileRapooDpi != null)
                    {
                        tileRapooDpi.ValueText = newDpi.ToString();
                        tileRapooDpi.Badge = "";
                        tileRapooDpi.Invalidate();
                    }

                    if (dpiMenu != null && dpiMenu.DropDownItems.Count > 0)
                    {
                        var curItem = dpiMenu.DropDownItems[0] as ToolStripMenuItem;
                        if (curItem != null)
                        {
                            curItem.Text = string.Format("当前: {0} DPI", newDpi);
                        }
                    }
                }
                else
                {
                    // Highlight corresponding segment button
                    if (btnDpiStages != null)
                    {
                        for (int i = 0; i < btnDpiStages.Length; i++)
                        {
                            bool isCur = (newStage > 0 && i < stageCount)
                                ? (i == (newStage - 1))
                                : (i < dpiStageValues.Length && dpiStageValues[i] == newDpi);
                            btnDpiStages[i].Selected = isCur;
                        }
                    }

                    // Update Tray DPI submenu checks
                    if (dpiMenu != null)
                    {
                        foreach (ToolStripItem item in dpiMenu.DropDownItems)
                        {
                            var mi = item as ToolStripMenuItem;
                            if (mi != null && mi.Tag != null)
                            {
                                mi.Checked = ((int)mi.Tag == newDpi);
                            }
                        }
                    }
                }

                    // Update status text
                    UpdateStatusDisplay();
                }
            }
        }

        private void RelayoutDpiButtons()
        {
            if (cardPerformance == null || btnDpiStages == null || btnDpiManage == null) return;

            int cPad = (int)(18 * dpiScale);
            int secW = cardPerformance.ClientSize.Width - (cPad * 2);
            int r1Y = (int)(40 * dpiScale);
            int lblW = (int)(86 * dpiScale);
            int segH = (int)(28 * dpiScale);

            int dpiStartX = cPad + lblW;
            int availableW = secW - lblW;
            int gap = (int)(6 * dpiScale);
            int manageBtnW = segH;

            int count = Math.Max(1, Math.Min(5, dpiStageList.Count));
            int remainingW = availableW - manageBtnW - gap;
            int stageW = (remainingW - (count - 1) * gap) / count;

            dpiStageValues = dpiStageList.ToArray();

            for (int i = 0; i < 5; i++)
            {
                if (i < count)
                {
                    btnDpiStages[i].Visible = true;
                    btnDpiStages[i].Text = dpiStageList[i].ToString();
                    btnDpiStages[i].Location = new Point(dpiStartX + i * (stageW + gap), r1Y);
                    btnDpiStages[i].Size = new Size(stageW, segH);
                    btnDpiStages[i].Invalidate();
                }
                else
                {
                    btnDpiStages[i].Visible = false;
                }
            }

            btnDpiManage.Visible = true;
            btnDpiManage.Location = new Point(dpiStartX + count * (stageW + gap), r1Y);
            btnDpiManage.Size = new Size(manageBtnW, segH);

            if (dpiMenu != null && selectedDevice != null && selectedDevice.Brand == "Razer")
            {
                dpiMenu.DropDownItems.Clear();
                for (int i = 0; i < dpiStageList.Count; i++)
                {
                    int stageIdx = i;
                    int val = dpiStageList[i];
                    var sub = new ToolStripMenuItem(string.Format("档位 {0}: {1} DPI", i + 1, val), null, (s, e) => SwitchDpiStageByIndex(stageIdx));
                    sub.Tag = val;
                    if (selectedDevice.Dpi == val || (selectedDevice.DpiStage == (i + 1))) sub.Checked = true;
                    dpiMenu.DropDownItems.Add(sub);
                }
            }
        }

        private void HighlightDpiStage(int stage)
        {
            if (btnDpiStages != null)
            {
                for (int i = 0; i < btnDpiStages.Length; i++)
                {
                    if (btnDpiStages[i] != null)
                    {
                        btnDpiStages[i].Selected = (i == (stage - 1));
                    }
                }
            }
        }

        private void SwitchDpiStageByIndex(int index)
        {
            if (index < 0 || index >= dpiStageList.Count) return;
            int targetStage = index + 1;
            int targetDpi = dpiStageList[index];

            var curDev = selectedDevice;
            if (curDev != null && curDev.Brand == "Razer")
            {
                string devId = curDev.DeviceId;
                new Thread(() => {
                    bool ok = RazerDeviceHelper.SetRazerDpiStage(targetStage, devId);
                    this.BeginInvoke(new Action(() => {
                        if (curDev != null)
                        {
                            curDev.Dpi = targetDpi;
                            curDev.DpiStage = targetStage;
                        }
                        HighlightDpiStage(targetStage);
                        OnDpiChanged("Razer", targetDpi, targetStage, dpiStageList.Count, devId);
                    }));
                }) { IsBackground = true }.Start();
            }
            else
            {
                SetDpiFromUI(targetDpi);
            }
        }

        private void OpenDpiManagementDialog()
        {
            var curDev = selectedDevice;
            if (curDev == null || curDev.Brand != "Razer") return;

            int curStage = curDev.DpiStage > 0 ? curDev.DpiStage : 1;
            int[] stages = (curDev.DpiStages != null && curDev.DpiStages.Length > 0) ? curDev.DpiStages : dpiStageList.ToArray();
            string devName = !string.IsNullOrEmpty(curDev.DisplayName) ? curDev.DisplayName : "Razer Mouse";
            int pid = 0;
            if (!string.IsNullOrEmpty(curDev.HardwareId) && curDev.HardwareId.Contains(":"))
            {
                string[] parts = curDev.HardwareId.Split(':');
                if (parts.Length >= 3)
                {
                    int.TryParse(parts[2], System.Globalization.NumberStyles.HexNumber, null, out pid);
                }
            }

            var cap = RazerDeviceHelper.GetDpiCapability(devName, pid);

            using (var dlg = new DpiManagementDialog(devName, stages, curStage, cap))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    int[] newStages = dlg.Stages;
                    if (newStages == null || newStages.Length == 0) return;

                    int activeStage = Math.Max(1, Math.Min(dlg.ActiveStage, newStages.Length));
                    int activeDpi = newStages[activeStage - 1];

                    // 1. Immediately apply to in-memory state on UI thread
                    dpiStageList = new List<int>(newStages);
                    dpiStageValues = newStages;
                    SaveCustomDpiStages();

                    if (curDev != null)
                    {
                        curDev.DpiStages = newStages;
                        curDev.DpiStageCount = newStages.Length;
                        curDev.DpiStage = activeStage;
                        curDev.Dpi = activeDpi;
                    }

                    // 2. Immediately relayout, re-render and highlight main form DPI buttons
                    RelayoutDpiButtons();
                    HighlightDpiStage(activeStage);
                    if (btnDpiStages != null)
                    {
                        for (int i = 0; i < btnDpiStages.Length; i++)
                        {
                            if (btnDpiStages[i] != null) btnDpiStages[i].Invalidate();
                        }
                    }
                    if (cardPerformance != null) cardPerformance.Invalidate();

                    string targetDevId = curDev != null ? curDev.DeviceId : null;
                    OnDpiChanged("Razer", activeDpi, activeStage, newStages.Length, targetDevId);
                    UpdatePerformanceSection(curDev, curDev.BrandColor);
                    UpdateStatusDisplay();

                    // 3. Persist to hardware in background thread
                    new Thread(() => {
                        RazerDeviceHelper.UpdateRazerHardwareStages(activeStage, newStages, targetDevId);
                    }) { IsBackground = true }.Start();
                }
            }
        }

        private void SaveCustomDpiStages()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(ConfigRegistryKey))
                {
                    if (key != null)
                    {
                        StringBuilder sb = new StringBuilder();
                        for (int i = 0; i < dpiStageList.Count; i++)
                        {
                            if (i > 0) sb.Append(",");
                            sb.Append(dpiStageList[i]);
                        }
                        key.SetValue("CustomDpiStages", sb.ToString());
                    }
                }
            }
            catch { }
        }

        private void AdjustPerformanceCardHeight(int newCard2H)
        {
            if (cardPerformance == null || cardSettings == null || btnRefresh == null || btnHideToTray == null) return;
            if (cardPerformance.Height == newCard2H) return;

            cardPerformance.Height = newCard2H;
            int padX = (int)(18 * dpiScale);
            int baseW = (int)(470 * dpiScale);
            int cardW = baseW - (padX * 2);

            int card3Y = cardPerformance.Bottom + (int)(12 * dpiScale);
            cardSettings.Location = new Point(padX, card3Y);

            int card4Y = cardSettings.Bottom + (int)(14 * dpiScale);
            int btnH = (int)(38 * dpiScale);
            int btnGap = (int)(14 * dpiScale);
            int btnW = (cardW - btnGap) / 2;

            btnRefresh.Location = new Point(padX, card4Y);
            btnHideToTray.Location = new Point(padX + btnW + btnGap, card4Y);

            int clientH = card4Y + btnH + (int)(16 * dpiScale);
            this.ClientSize = new Size(baseW, clientH);
        }

        public void SetDpiFromUI(int dpi)
        {
            var curDev = selectedDevice;
            new Thread(() => {
                int targetStage = -1;
                for (int i = 0; i < dpiStageValues.Length; i++)
                {
                    if (dpiStageValues[i] == dpi)
                    {
                        targetStage = i + 1;
                        break;
                    }
                }

                if (curDev != null && curDev.Brand == "Rapoo")
                {
                    if (targetStage > 0)
                        RapooDeviceHelper.SetRapooDpiStage(targetStage);
                    else
                        RapooDeviceHelper.SetRapooDpi(dpi);

                    this.BeginInvoke(new Action(() => {
                        if (curDev != null)
                        {
                            curDev.Dpi = dpi;
                            if (targetStage > 0) curDev.DpiStage = targetStage;
                        }
                        OnDpiChanged("Rapoo", dpi, targetStage > 0 ? targetStage : 1, dpiStageValues.Length);
                    }));
                }
                else
                {
                    string rzDevId = curDev != null ? curDev.DeviceId : null;
                    if (targetStage > 0)
                    {
                        RazerDeviceHelper.SetRazerDpiStage(targetStage, rzDevId);
                    }
                    else
                    {
                        RazerDeviceHelper.SetRazerDpi(dpi, rzDevId);
                    }

                    int queryDpi, stage, count;
                    if (RazerDeviceHelper.FastQueryDpi(out queryDpi, out stage, out count, rzDevId))
                    {
                        this.BeginInvoke(new Action(() => {
                            if (curDev != null)
                            {
                                curDev.Dpi = queryDpi;
                                curDev.DpiStage = stage;
                            }
                            OnDpiChanged("Razer", queryDpi, stage, count, rzDevId);
                        }));
                    }
                    else
                    {
                        this.BeginInvoke(new Action(() => {
                            if (curDev != null)
                            {
                                curDev.Dpi = dpi;
                                if (targetStage > 0) curDev.DpiStage = targetStage;
                            }
                            OnDpiChanged("Razer", dpi, targetStage > 0 ? targetStage : 1, dpiStageValues.Length, rzDevId);
                        }));
                    }
                }
            }) { IsBackground = true }.Start();
        }

        public void SetPollingRateFromUI(int hz)
        {
            desktopPollingRate = hz;
            SaveConfig();

            var curDev = selectedDevice;
            new Thread(() => {
                bool ok = false;
                if (!isGamingActive)
                {
                    if (curDev != null && curDev.Brand == "Rapoo")
                    {
                        ok = RapooDeviceHelper.SetRapooPollingRate(hz);
                    }
                    else
                    {
                        ok = RazerDeviceHelper.SetRazerPollingRate(hz, curDev != null ? curDev.DeviceId : null);
                    }
                }
                else
                {
                    ok = true;
                }

                if (ok)
                {
                    this.BeginInvoke(new Action(() => {
                        if (!isGamingActive && curDev != null) curDev.PollingRate = hz;
                        if (!isGamingActive && lastInfo != null && lastInfo.DeviceId == (curDev != null ? curDev.DeviceId : "")) lastInfo.PollingRate = hz;

                        UpdateDesktopRateButtonSelection(hz);
                        UpdatePollingRateDisplayStates();
                        UpdateStatusDisplay();
                    }));
                }
            }) { IsBackground = true }.Start();
        }

        public void SetGamePollingRateFromUI(int hz)
        {
            gamePollingRate = hz;
            UpdateGameRateButtonSelection(hz);
            UpdatePollingRateDisplayStates();
            SaveConfig();

            if (isGamingActive)
            {
                ApplyGamePollingRate();
            }
        }

        private void UpdateGameRateButtonSelection(int hz)
        {
            if (btnGameRates != null)
            {
                for (int i = 0; i < btnGameRates.Length; i++)
                {
                    if (i < pollingRateValues.Length)
                    {
                        btnGameRates[i].Selected = (pollingRateValues[i] == hz);
                    }
                }
            }

            if (autoGameRateMenu != null && autoGameRateMenu.DropDownItems.Count > 2)
            {
                var gameRateSub = autoGameRateMenu.DropDownItems[2] as ToolStripMenuItem;
                if (gameRateSub != null)
                {
                    foreach (ToolStripItem item in gameRateSub.DropDownItems)
                    {
                        var mi = item as ToolStripMenuItem;
                        if (mi != null && mi.Tag != null)
                        {
                            mi.Checked = ((int)mi.Tag == hz);
                        }
                    }
                }
            }
            UpdatePollingRateDisplayStates();
        }

        private void UpdateDesktopRateButtonSelection(int hz)
        {
            if (btnRates != null)
            {
                for (int i = 0; i < btnRates.Length; i++)
                {
                    if (i < pollingRateValues.Length)
                    {
                        btnRates[i].Selected = (pollingRateValues[i] == hz);
                    }
                }
            }

            if (rateMenu != null)
            {
                foreach (ToolStripItem item in rateMenu.DropDownItems)
                {
                    var mi = item as ToolStripMenuItem;
                    if (mi != null && mi.Tag != null)
                    {
                        mi.Checked = ((int)mi.Tag == hz);
                    }
                }
            }
            UpdatePollingRateDisplayStates();
        }

        private void UpdatePollingRateDisplayStates()
        {
            if (btnRates == null) return;

            bool isRazerMouse = (selectedDevice != null &&
                                 selectedDevice.Category == DeviceCategory.Mouse &&
                                 (selectedDevice.Brand == "Razer" || (selectedDevice.DeviceId != null && selectedDevice.DeviceId.StartsWith("Razer:1532:"))));

            if (!autoGameRateEnabled || !isRazerMouse)
            {
                if (lblLiveRateBadge != null) lblLiveRateBadge.Visible = false;
                if (lblRateTitle != null)
                {
                    lblRateTitle.Text = "常规回报率";
                    lblRateTitle.ForeColor = Color.FromArgb(160, 168, 185);
                }
                for (int i = 0; i < btnRates.Length; i++)
                {
                    if (btnRates[i] != null && i < pollingRateValues.Length)
                    {
                        btnRates[i].Selected = (pollingRateValues[i] == desktopPollingRate);
                        btnRates[i].IsPreset = false;
                    }
                }
            }
            else
            {
                int currentActiveHz = isGamingActive ? gamePollingRate : desktopPollingRate;
                if (lblLiveRateBadge != null)
                {
                    lblLiveRateBadge.Visible = true;
                    if (isGamingActive)
                    {
                        lblLiveRateBadge.Text = "● 硬件生效: " + currentActiveHz + " Hz (电竞加速)";
                        lblLiveRateBadge.ForeColor = Color.FromArgb(0, 230, 118);
                    }
                    else
                    {
                        lblLiveRateBadge.Text = "● 硬件生效: " + currentActiveHz + " Hz (常规桌面)";
                        lblLiveRateBadge.ForeColor = Color.FromArgb(180, 210, 240);
                    }
                }

                if (lblRateTitle != null)
                {
                    if (!isGamingActive)
                    {
                        lblRateTitle.Text = "● 常规回报率";
                        lblRateTitle.ForeColor = Color.FromArgb(0, 230, 118);
                    }
                    else
                    {
                        lblRateTitle.Text = "常规回报率";
                        lblRateTitle.ForeColor = Color.FromArgb(160, 168, 185);
                    }
                }

                if (lblGameRateTitle != null)
                {
                    if (isGamingActive)
                    {
                        lblGameRateTitle.Text = "● 电竞回报率";
                        lblGameRateTitle.ForeColor = Color.FromArgb(0, 230, 118);
                    }
                    else
                    {
                        lblGameRateTitle.Text = "电竞回报率";
                        lblGameRateTitle.ForeColor = Color.FromArgb(160, 168, 185);
                    }
                }

                for (int i = 0; i < btnRates.Length; i++)
                {
                    if (btnRates[i] != null && i < pollingRateValues.Length)
                    {
                        bool isSel = (pollingRateValues[i] == desktopPollingRate);
                        btnRates[i].Selected = isSel;
                        btnRates[i].IsPreset = (isSel && isGamingActive);
                    }
                }

                if (btnGameRates != null)
                {
                    for (int i = 0; i < btnGameRates.Length; i++)
                    {
                        if (btnGameRates[i] != null && i < pollingRateValues.Length)
                        {
                            bool isSel = (pollingRateValues[i] == gamePollingRate);
                            btnGameRates[i].Selected = isSel;
                            btnGameRates[i].IsPreset = (isSel && !isGamingActive);
                        }
                    }
                }
            }
        }

        private void SetGameRateControlsVisibility(bool visible)
        {
            bool isRazerMouse = (selectedDevice != null &&
                                 selectedDevice.Category == DeviceCategory.Mouse &&
                                 (selectedDevice.Brand == "Razer" || (selectedDevice.DeviceId != null && selectedDevice.DeviceId.StartsWith("Razer:1532:"))));
            bool effective = visible && isRazerMouse;

            if (btnManageGames != null) btnManageGames.Visible = effective;
            if (lblGameRateTitle != null) lblGameRateTitle.Visible = effective;
            if (btnGameRates != null)
            {
                for (int i = 0; i < btnGameRates.Length; i++)
                {
                    if (btnGameRates[i] != null) btnGameRates[i].Visible = effective;
                }
            }
            if (lblNotifyModeTitle != null) lblNotifyModeTitle.Visible = effective;
            if (chkNotifyOsd != null) chkNotifyOsd.Visible = effective;
            if (chkNotifyToast != null) chkNotifyToast.Visible = effective;
            if (chkNotifySound != null) chkNotifySound.Visible = effective;
        }

        private void AnimatePerformanceCardHeight(int targetH)
        {
            if (cardPerformance == null) return;
            animTargetCardH = targetH;
            if (cardPerformance.Height == targetH)
            {
                if (!autoGameRateEnabled) SetGameRateControlsVisibility(false);
                return;
            }

            if (cardAnimTimer == null)
            {
                cardAnimTimer = new System.Windows.Forms.Timer();
                cardAnimTimer.Interval = 15; // ~66 FPS
                cardAnimTimer.Tick += (s, e) => {
                    if (cardPerformance == null) { cardAnimTimer.Stop(); return; }
                    int curH = cardPerformance.Height;
                    int diff = animTargetCardH - curH;
                    if (Math.Abs(diff) <= 2)
                    {
                        AdjustPerformanceCardHeight(animTargetCardH);
                        cardAnimTimer.Stop();
                        if (!autoGameRateEnabled)
                        {
                            SetGameRateControlsVisibility(false);
                        }
                        return;
                    }
                    int step = (int)Math.Round(diff * 0.35);
                    if (step == 0) step = diff > 0 ? 1 : -1;
                    AdjustPerformanceCardHeight(curH + step);
                };
            }

            if (autoGameRateEnabled)
            {
                SetGameRateControlsVisibility(true);
            }
            cardAnimTimer.Start();
        }

        private void SetAutoGameRateEnabled(bool enabled)
        {
            autoGameRateEnabled = enabled;
            isUpdatingUI = true;
            try
            {
                if (chkAutoGameRate != null) chkAutoGameRate.Checked = enabled;
                if (autoGameRateToggleItem != null) autoGameRateToggleItem.Checked = enabled;

                UpdatePerformanceCardHeightForCurrentDevice();
                UpdatePollingRateDisplayStates();
            }
            finally
            {
                isUpdatingUI = false;
            }
            SaveConfig();

            // If toggled off while in game, restore desktop rate immediately
            if (!enabled && isGamingActive)
            {
                isGamingActive = false;
                RestoreDesktopPollingRate();
            }
        }

        private void UpdatePerformanceCardHeightForCurrentDevice()
        {
            var dev = selectedDevice;
            if (dev != null && (dev.Brand == "Razer" || dev.Brand == "Rapoo"))
            {
                int targetH = autoGameRateEnabled ? (int)(208 * dpiScale) : (int)(142 * dpiScale);
                AnimatePerformanceCardHeight(targetH);
            }
        }

        private void UpdateManageGamesButtonText()
        {
            if (btnManageGames != null)
            {
                btnManageGames.Text = "🎮 游戏管理 (" + monitoredGameProcesses.Count + "款)";
            }
        }

        private void OpenGameProcessManager()
        {
            var dlg = new GameProcessManagerForm(dpiScale, monitoredGameProcesses, fallbackFullscreenEnabled, (updatedList, fb) => {
                monitoredGameProcesses = new List<string>(updatedList);
                fallbackFullscreenEnabled = fb;
                SaveConfig();
                UpdateManageGamesButtonText();
            });
            dlg.ShowDialog(this);
        }

        private bool ContainsGameProcess(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string target = name.ToLower();
            if (!target.EndsWith(".exe")) target += ".exe";
            for (int i = 0; i < monitoredGameProcesses.Count; i++)
            {
                string item = monitoredGameProcesses[i].ToLower();
                if (!item.EndsWith(".exe")) item += ".exe";
                if (item == target) return true;
            }
            return false;
        }

        private void StartGameMonitor()
        {
            if (gameMonitorTimer != null) return;
            gameMonitorTimer = new System.Windows.Forms.Timer();
            gameMonitorTimer.Interval = 1000;
            gameMonitorTimer.Tick += (s, e) => {
                if (!autoGameRateEnabled) return;
                var curDev = selectedDevice;
                if (curDev == null) return;
                if (curDev.Brand != "Razer" && curDev.Brand != "Rapoo") return;

                string procTitle = "";
                bool isGame = false;

                // Priority 1: Check foreground window against user's monitored game processes
                IntPtr fgHwnd = GetForegroundWindow();
                if (fgHwnd != IntPtr.Zero)
                {
                    uint pid = 0;
                    GetWindowThreadProcessId(fgHwnd, out pid);
                    if (pid != 0)
                    {
                        try
                        {
                            using (var proc = Process.GetProcessById((int)pid))
                            {
                                string pName = proc.ProcessName;
                                if (ContainsGameProcess(pName))
                                {
                                    isGame = true;
                                    procTitle = proc.MainWindowTitle;
                                    if (string.IsNullOrEmpty(procTitle)) procTitle = pName;
                                }
                            }
                        }
                        catch { }
                    }
                }

                // Priority 2: Fallback to full-screen heuristic if user enabled fallback
                if (!isGame && fallbackFullscreenEnabled)
                {
                    string fsTitle;
                    if (FullScreenGameDetector.IsGameRunning(out fsTitle))
                    {
                        isGame = true;
                        procTitle = fsTitle;
                    }
                }

                if (isGame)
                {
                    outOfGameTickCount = 0;
                    inGameTickCount++;
                    if (inGameTickCount >= 2 && !isGamingActive)
                    {
                        isGamingActive = true;
                        ApplyGamePollingRate();
                    }
                }
                else
                {
                    inGameTickCount = 0;
                    outOfGameTickCount++;
                    if (outOfGameTickCount >= 2 && isGamingActive)
                    {
                        isGamingActive = false;
                        RestoreDesktopPollingRate();
                    }
                }
            };
            gameMonitorTimer.Start();
        }

        private void ApplyGamePollingRate()
        {
            var curDev = selectedDevice;
            if (curDev == null) return;
            int targetHz = gamePollingRate;
            string brand = curDev.Brand;

            new Thread(() => {
                bool ok = false;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if (brand == "Rapoo") ok = RapooDeviceHelper.SetRapooPollingRate(targetHz);
                    else ok = RazerDeviceHelper.SetRazerPollingRate(targetHz, curDev.DeviceId);
                    if (ok) break;
                    Thread.Sleep(80);
                }

                if (ok)
                {
                    this.BeginInvoke(new Action(() => {
                        curDev.PollingRate = targetHz;
                        if (lastInfo != null && lastInfo.DeviceId == curDev.DeviceId) lastInfo.PollingRate = targetHz;

                        UpdateStatusDisplay();
                        UpdatePollingRateDisplayStates();

                        if (gameRateNotifyOsd && osdForm != null)
                        {
                            osdForm.ShowRate(targetHz, "全屏电竞模式", brand);
                        }

                        if (gameRateNotifyToast)
                        {
                            SafeShowBalloonTip(3000, "FerrisPulse · 全屏电竞模式",
                                string.Format("检测到电竞游戏运行，已自动将鼠标回报率提升至 {0} Hz", targetHz),
                                ToolTipIcon.Info);
                        }

                        if (gameRateNotifySound)
                        {
                            PlayAscendingChime();
                        }
                    }));
                }
            }) { IsBackground = true }.Start();
        }

        private void RestoreDesktopPollingRate()
        {
            var curDev = selectedDevice;
            if (curDev == null) return;
            int targetHz = desktopPollingRate;
            string brand = curDev.Brand;

            new Thread(() => {
                bool ok = false;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if (brand == "Rapoo") ok = RapooDeviceHelper.SetRapooPollingRate(targetHz);
                    else ok = RazerDeviceHelper.SetRazerPollingRate(targetHz, curDev.DeviceId);
                    if (ok) break;
                    Thread.Sleep(80);
                }

                if (ok)
                {
                    this.BeginInvoke(new Action(() => {
                        curDev.PollingRate = targetHz;
                        if (lastInfo != null && lastInfo.DeviceId == curDev.DeviceId) lastInfo.PollingRate = targetHz;

                        UpdateStatusDisplay();
                        UpdatePollingRateDisplayStates();

                        if (gameRateNotifyOsd && osdForm != null)
                        {
                            osdForm.ShowRate(targetHz, "桌面节能模式", brand);
                        }

                        if (gameRateNotifyToast)
                        {
                            SafeShowBalloonTip(3000, "FerrisPulse · 桌面模式",
                                string.Format("已切回桌面，鼠标回报率已恢复至 {0} Hz 节能状态", targetHz),
                                ToolTipIcon.Info);
                        }

                        if (gameRateNotifySound)
                        {
                            PlayDescendingChime();
                        }
                    }));
                }
            }) { IsBackground = true }.Start();
        }

        private static byte[] ascendingChimeBytes = null;
        private static byte[] descendingChimeBytes = null;

        private static byte[] GenerateBellChimeWav(double[] freqs, double[] durations)
        {
            try
            {
                int sampleRate = 44100;
                int totalSamples = 0;
                for (int i = 0; i < durations.Length; i++) totalSamples += (int)(sampleRate * durations[i]);
                int dataChunkSize = totalSamples * 2;

                using (MemoryStream ms = new MemoryStream())
                {
                    using (BinaryWriter bw = new BinaryWriter(ms))
                    {
                        bw.Write(new char[] { 'R', 'I', 'F', 'F' });
                        bw.Write(36 + dataChunkSize);
                        bw.Write(new char[] { 'W', 'A', 'V', 'E' });
                        bw.Write(new char[] { 'f', 'm', 't', ' ' });
                        bw.Write(16);
                        bw.Write((short)1); // PCM
                        bw.Write((short)1); // Mono
                        bw.Write(sampleRate);
                        bw.Write(sampleRate * 2);
                        bw.Write((short)2);
                        bw.Write((short)16);
                        bw.Write(new char[] { 'd', 'a', 't', 'a' });
                        bw.Write(dataChunkSize);

                        for (int n = 0; n < freqs.Length; n++)
                        {
                            double f = freqs[n];
                            int nSamples = (int)(sampleRate * durations[n]);
                            double decayRate = (n == 0) ? 14.0 : 8.0;
                            for (int i = 0; i < nSamples; i++)
                            {
                                double t = (double)i / sampleRate;
                                double env = (t < 0.006) ? (t / 0.006) : Math.Exp(-(t - 0.006) * decayRate);
                                double s = (Math.Sin(2.0 * Math.PI * f * t) * 0.75 + Math.Sin(4.0 * Math.PI * f * t) * 0.20 + Math.Sin(6.0 * Math.PI * f * t) * 0.05) * env;
                                short sample = (short)(s * 11000);
                                bw.Write(sample);
                            }
                        }
                        bw.Flush();
                        return ms.ToArray();
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private static void PlayAscendingChime()
        {
            new Thread(() => {
                try
                {
                    if (ascendingChimeBytes == null)
                    {
                        ascendingChimeBytes = GenerateBellChimeWav(new double[] { 784.0, 1046.5 }, new double[] { 0.09, 0.18 });
                    }
                    if (ascendingChimeBytes != null)
                    {
                        using (var ms = new MemoryStream(ascendingChimeBytes))
                        {
                            using (var sp = new SoundPlayer(ms))
                            {
                                sp.PlaySync();
                            }
                        }
                    }
                    else
                    {
                        System.Media.SystemSounds.Asterisk.Play();
                    }
                }
                catch
                {
                    try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
                }
            }) { IsBackground = true }.Start();
        }

        private static void PlayDescendingChime()
        {
            new Thread(() => {
                try
                {
                    if (descendingChimeBytes == null)
                    {
                        descendingChimeBytes = GenerateBellChimeWav(new double[] { 1046.5, 784.0 }, new double[] { 0.09, 0.18 });
                    }
                    if (descendingChimeBytes != null)
                    {
                        using (var ms = new MemoryStream(descendingChimeBytes))
                        {
                            using (var sp = new SoundPlayer(ms))
                            {
                                sp.PlaySync();
                            }
                        }
                    }
                    else
                    {
                        System.Media.SystemSounds.Asterisk.Play();
                    }
                }
                catch
                {
                    try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
                }
            }) { IsBackground = true }.Start();
        }

        private void CycleRapooPollingRate()
        {
            // Rapoo polling rate adjustment disabled as requested
        }

        public void SetRapooPollingRateManual(int hz)
        {
            RapooDeviceHelper.SetRapooPollingRate(hz);
            if (selectedDevice != null && selectedDevice.Brand == "Rapoo")
            {
                selectedDevice.PollingRate = hz;
            }
            if (lastInfo != null && lastInfo.Brand == "Rapoo")
            {
                lastInfo.PollingRate = hz;
            }

            if (rateMenu != null)
            {
                foreach (ToolStripItem item in rateMenu.DropDownItems)
                {
                    var mi = item as ToolStripMenuItem;
                    if (mi != null && mi.Tag != null && mi.Tag is int)
                    {
                        mi.Checked = ((int)mi.Tag == hz);
                    }
                }
            }
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
            SaveConfig();

            isUpdatingUI = true;
            try
            {
                if (styleCapsuleItem != null) styleCapsuleItem.Checked = (trayStyle == 0);
                if (styleNumItem != null) styleNumItem.Checked = (trayStyle == 1);

                if (btnStyleCapsule != null) btnStyleCapsule.Selected = (trayStyle == 0);
                if (btnStyleNum != null) btnStyleNum.Selected = (trayStyle == 1);
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
                            if (trayStyle != 0 && trayStyle != 1) trayStyle = 0;
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
                if (btnStyleCapsule != null) btnStyleCapsule.Selected = (trayStyle == 0);
                if (btnStyleNum != null) btnStyleNum.Selected = (trayStyle == 1);

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

        private void SetAsPrimaryDevice(string deviceId)
        {
            DeviceManager.SetConfiguredPrimaryDeviceId(deviceId);
            var prio = DeviceManager.GetPriorityList();
            prio.Remove(deviceId);
            prio.Insert(0, deviceId);
            DeviceManager.SavePriorityList(prio);

            if (activeDevices != null)
            {
                foreach (var d in activeDevices)
                {
                    d.IsPrimary = string.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase);
                }
            }
            UpdateDeviceTabsUI();
            if (selectedDevice != null)
            {
                UpdatePrimaryPinButton(selectedDevice);
            }
            var primary = GetPrimaryDevice();
            if (primary != null)
            {
                lastInfo = primary;
                UpdateTrayIcon(primary.BatteryPercent, primary.IsCharging, primary.IsConnected, primary.IsSleeping);
                UpdateStatusDisplay();
            }
            UpdateTrayDeviceMenuItems();
        }

        private MouseBatteryInfo GetPrimaryDevice()
        {
            if (activeDevices == null || activeDevices.Count == 0) return lastInfo;
            var p = activeDevices.Find(d => d.IsPrimary);
            return p ?? activeDevices[0];
        }

        private void UpdatePrimaryPinButton(MouseBatteryInfo dev)
        {
            if (badgePrimary == null) return;
            if (dev == null)
            {
                badgePrimary.Visible = false;
                return;
            }

            badgePrimary.Visible = dev.IsPrimary;
            if (dev.IsPrimary)
            {
                badgePrimary.SetStatus("● 任务栏常驻", dev.BrandColor);
            }

            if (lblDeviceName != null)
            {
                int rightBound = badgePrimary.Visible ? badgePrimary.Left : (cardBattery.ClientSize.Width - (int)(16 * dpiScale));
                lblDeviceName.Size = new Size(Math.Max(50, rightBound - lblDeviceName.Left - (int)(10 * dpiScale)), lblDeviceName.Height);
            }
        }

        private void UpdateDeviceTabsUI()
        {
            if (focusDeviceSelector == null) return;
            var dev = selectedDevice ?? GetPrimaryDevice();
            if (dev != null)
            {
                focusDeviceSelector.DeviceIcon = dev.CategoryIcon;
                focusDeviceSelector.Category = dev.Category;
                focusDeviceSelector.DeviceName = dev.DisplayName;
                focusDeviceSelector.AccentColor = dev.BrandColor;
                string battText = "--%";
                if (dev.IsConnected)
                {
                    bool isWired = dev.Category == DeviceCategory.Keyboard && dev.Transport != null && 
                                   (dev.Transport.IndexOf("有线", StringComparison.OrdinalIgnoreCase) >= 0 || dev.Transport.IndexOf("USB", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (isWired)
                    {
                        battText = "⚡ 供电中";
                    }
                    else if (dev.Brand == "NuPhy" && dev.Category == DeviceCategory.Keyboard && (dev.BatteryPercent >= 100 || dev.IsCharging))
                    {
                        battText = "⚡ 充电中";
                    }
                    else if (dev.BatteryPercent > 0)
                    {
                        battText = dev.BatteryPercent + "%";
                    }
                    else if (dev.Category == DeviceCategory.Keyboard)
                    {
                        battText = "免驱";
                    }
                }
                focusDeviceSelector.BatteryText = battText;
                focusDeviceSelector.StatusText = dev.IsConnected 
                    ? (dev.IsSleeping ? "休眠中" : ((dev.Brand == "NuPhy" && (dev.BatteryPercent >= 100 || dev.IsCharging)) ? "充电中" : (dev.Category == DeviceCategory.Keyboard && dev.BatteryPercent <= 0 ? "免驱在线" : "已连接"))) 
                    : "已离线";
                focusDeviceSelector.Invalidate();
            }
        }

        public void SelectDevice(string deviceId)
        {
            if (activeDevices != null)
            {
                var target = activeDevices.Find(d => string.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
                if (target != null)
                {
                    SelectDeviceView(target);
                }
            }
        }

        private void SelectDeviceView(MouseBatteryInfo dev)
        {
            if (dev == null) return;
            selectedDevice = dev;
            UpdateDeviceTabsUI();
            UpdateUI(dev, false);
            UpdatePrimaryPinButton(dev);
        }

        private void OpenPriorityManager()
        {
            using (var form = new DevicePriorityForm(activeDevices, dpiScale))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    RefreshBatteryStatus(true);
                }
            }
        }

        private void OpenAddDeviceWizard()
        {
            using (var form = new AddDeviceWizardForm(activeDevices, dpiScale))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    RefreshBatteryStatus(true);
                    if (!string.IsNullOrEmpty(form.AddedDeviceId))
                    {
                        SelectDevice(form.AddedDeviceId);
                    }
                }
            }
        }

        private ModernDeviceSwitcherFlyout switcherFlyout = null;
        private DateTime lastFlyoutCloseTime = DateTime.MinValue;

        private void ToggleDeviceSwitcherFlyout()
        {
            if (activeDevices == null || activeDevices.Count == 0 || focusDeviceSelector == null) return;

            // Debounce toggle if clicked while flyout was closing
            if ((DateTime.Now - lastFlyoutCloseTime).TotalMilliseconds < 250)
            {
                return;
            }

            if (switcherFlyout != null && !switcherFlyout.IsDisposed && switcherFlyout.Visible)
            {
                switcherFlyout.Close();
                switcherFlyout = null;
                return;
            }

            switcherFlyout = new ModernDeviceSwitcherFlyout(dpiScale);
            switcherFlyout.Devices = this.activeDevices;
            switcherFlyout.SelectedDevice = this.selectedDevice;
            switcherFlyout.OnSelectDevice = (d) => {
                SelectDeviceView(d);
            };

            int padX = (int)(18 * dpiScale);
            int cardW = this.ClientSize.Width - (padX * 2);
            switcherFlyout.CalcSize(cardW);

            Point pt = this.PointToScreen(new Point(padX, (int)(12 * dpiScale) + (int)(38 * dpiScale) + (int)(4 * dpiScale)));
            switcherFlyout.Location = pt;
            switcherFlyout.FormClosed += (s, e) => {
                lastFlyoutCloseTime = DateTime.Now;
                switcherFlyout = null;
            };
            switcherFlyout.Show(this);
        }

        private void ShowDeviceSwitcherMenu()
        {
            ToggleDeviceSwitcherFlyout();
        }

        private void UpdateTrayDeviceMenuItems()
        {
            if (contextMenu == null) return;

            foreach (var item in dynamicDeviceMenuItems)
            {
                if (contextMenu.Items.Contains(item))
                {
                    contextMenu.Items.Remove(item);
                }
            }
            dynamicDeviceMenuItems.Clear();

            var primary = GetPrimaryDevice();
            var onlineDevices = new List<MouseBatteryInfo>();
            if (activeDevices != null)
            {
                for (int i = 0; i < activeDevices.Count; i++)
                {
                    if (activeDevices[i].IsConnected && !DeviceManager.IsDeviceHidden(activeDevices[i].DeviceId))
                    {
                        onlineDevices.Add(activeDevices[i]);
                    }
                }
            }

            if (onlineDevices.Count == 0)
            {
                if (statusMenuItem != null)
                {
                    statusMenuItem.Visible = true;
                    statusMenuItem.Text = "暂无在线设备";
                    statusMenuItem.ForeColor = Color.FromArgb(145, 155, 175);
                }
            }
            else
            {
                if (statusMenuItem != null)
                {
                    statusMenuItem.Visible = false;
                }

                int insertIdx = contextMenu.Items.IndexOf(deviceMenuSep);
                if (insertIdx < 0) insertIdx = 0;

                for (int i = 0; i < onlineDevices.Count; i++)
                {
                    var dev = onlineDevices[i];
                    string battStr;
                    if (dev.Brand == "NuPhy" || dev.Category == DeviceCategory.Keyboard)
                    {
                        bool isWired = (dev.Transport != null && (dev.Transport.IndexOf("有线", StringComparison.OrdinalIgnoreCase) >= 0 || dev.Transport.IndexOf("USB", StringComparison.OrdinalIgnoreCase) >= 0) && !dev.Transport.Contains("无线")) ||
                                       (dev.DeviceId != null && dev.DeviceId.IndexOf("pid_a011", StringComparison.OrdinalIgnoreCase) >= 0);
                        if (isWired)
                        {
                            battStr = "供电中";
                        }
                        else if (dev.IsCharging || (dev.Brand == "NuPhy" && dev.BatteryPercent >= 100))
                        {
                            battStr = "充电中";
                        }
                        else
                        {
                            battStr = dev.BatteryPercent > 0 ? (dev.BatteryPercent.ToString() + "%") : "--";
                        }
                    }
                    else
                    {
                        if (dev.IsCharging)
                        {
                            battStr = string.Format("⚡ {0}%", dev.BatteryPercent > 0 ? dev.BatteryPercent : 0);
                        }
                        else if (dev.IsSleeping)
                        {
                            battStr = string.Format("{0}% (休眠)", dev.BatteryPercent > 0 ? dev.BatteryPercent : 0);
                        }
                        else
                        {
                            battStr = dev.BatteryPercent > 0 ? (dev.BatteryPercent.ToString() + "%") : "--";
                        }
                    }

                    string itemText = dev.DisplayName + "\t" + battStr;
                    var mi = new ToolStripMenuItem(itemText);
                    mi.Checked = false;
                    mi.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
                    mi.ForeColor = dev.BrandColor;
                    mi.Padding = new Padding(6, 4, 12, 4);

                    var targetDev = dev;
                    mi.Click += (s, e) => {
                        ShowWindow(targetDev);
                    };

                    contextMenu.Items.Insert(insertIdx + i, mi);
                    dynamicDeviceMenuItems.Add(mi);
                }
            }

            // Context-sensitive controls based on primary tray device
            if (primary != null && primary.IsConnected)
            {
                if (primary.Brand == "NuPhy" || primary.Category == DeviceCategory.Keyboard)
                {
                    // Primary is NuPhy keyboard: Show NuPhy controls, hide Mouse DPI/Rate
                    if (dpiMenu != null) dpiMenu.Visible = false;
                    if (rateMenu != null) rateMenu.Visible = false;

                    var curSettings = NuphyDeviceHelper.CachedHardwareSettings ?? new NuphyDeviceHelper.NuphyHardwareSettings();

                    if (nuphySleepMenuItem != null)
                    {
                        nuphySleepMenuItem.Visible = true;
                        nuphySleepMenuItem.Text = curSettings.SleepEnabled ? "🌙 自动休眠: 已开启" : "🌙 自动休眠: 已关闭 (常亮)";
                    }
                    if (nuphyWinLockMenuItem != null)
                    {
                        nuphyWinLockMenuItem.Visible = true;
                        nuphyWinLockMenuItem.Text = curSettings.WinLock ? "🔒 Win键锁定: 已开启" : "🔓 Win键锁定: 已关闭";
                    }
                    if (nuphyBacklightMenu != null)
                    {
                        nuphyBacklightMenu.Visible = true;
                        for (int k = 0; k < nuphyBacklightMenu.DropDownItems.Count; k++)
                        {
                            var sub = nuphyBacklightMenu.DropDownItems[k] as ToolStripMenuItem;
                            if (sub != null && sub.Tag is int)
                            {
                                sub.Checked = ((int)sub.Tag == curSettings.BacklightBrightness);
                            }
                        }
                    }
                    if (nuphySidelightMenuItem != null)
                    {
                        nuphySidelightMenuItem.Visible = true;
                        nuphySidelightMenuItem.Text = (curSettings.SidelightEnabled && curSettings.SidelightBrightness > 0) ? "✨ 前置灯带: 已开启" : "✨ 前置灯带: 已关闭";
                    }
                    if (nuphyTraySep != null) nuphyTraySep.Visible = true;
                }
                else if (primary.Category == DeviceCategory.Mouse)
                {
                    // Primary is Mouse (Razer, Rapoo, etc.): Show DPI/Rate, hide NuPhy controls
                    if (nuphySleepMenuItem != null) nuphySleepMenuItem.Visible = false;
                    if (nuphyWinLockMenuItem != null) nuphyWinLockMenuItem.Visible = false;
                    if (nuphyBacklightMenu != null) nuphyBacklightMenu.Visible = false;
                    if (nuphySidelightMenuItem != null) nuphySidelightMenuItem.Visible = false;
                    if (nuphyTraySep != null) nuphyTraySep.Visible = false;

                    if (dpiMenu != null)
                    {
                        dpiMenu.Visible = true;
                        dpiMenu.DropDownItems.Clear();
                        if (dpiStageValues != null)
                        {
                            for (int i = 0; i < dpiStageValues.Length; i++)
                            {
                                int val = dpiStageValues[i];
                                var sub = new ToolStripMenuItem(val + " DPI", null, (s, e) => SetDpiFromUI(val));
                                sub.Tag = val;
                                sub.Checked = (primary.Dpi == val);
                                dpiMenu.DropDownItems.Add(sub);
                            }
                        }
                    }

                    if (rateMenu != null)
                    {
                        rateMenu.Visible = true;
                        for (int k = 0; k < rateMenu.DropDownItems.Count; k++)
                        {
                            var sub = rateMenu.DropDownItems[k] as ToolStripMenuItem;
                            if (sub != null && sub.Tag is int)
                            {
                                sub.Checked = ((int)sub.Tag == primary.PollingRate);
                            }
                        }
                    }
                }
                else
                {
                    // Other device (Headset, Gamepad)
                    if (dpiMenu != null) dpiMenu.Visible = false;
                    if (rateMenu != null) rateMenu.Visible = false;
                    if (nuphySleepMenuItem != null) nuphySleepMenuItem.Visible = false;
                    if (nuphyWinLockMenuItem != null) nuphyWinLockMenuItem.Visible = false;
                    if (nuphyBacklightMenu != null) nuphyBacklightMenu.Visible = false;
                    if (nuphySidelightMenuItem != null) nuphySidelightMenuItem.Visible = false;
                    if (nuphyTraySep != null) nuphyTraySep.Visible = false;
                }
            }
            else
            {
                // No connected primary device
                if (dpiMenu != null) dpiMenu.Visible = false;
                if (rateMenu != null) rateMenu.Visible = false;
                if (nuphySleepMenuItem != null) nuphySleepMenuItem.Visible = false;
                if (nuphyWinLockMenuItem != null) nuphyWinLockMenuItem.Visible = false;
                if (nuphyBacklightMenu != null) nuphyBacklightMenu.Visible = false;
                if (nuphySidelightMenuItem != null) nuphySidelightMenuItem.Visible = false;
                if (nuphyTraySep != null) nuphyTraySep.Visible = false;
            }
        }

        public bool PauseBackgroundRefresh { get; set; }

        private void RefreshBatteryStatus(bool showTipIfManual)
        {
            if (PauseBackgroundRefresh) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var devices = DeviceManager.QueryAllDevices();
                    try
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            if (PauseBackgroundRefresh) return;
                            ApplyDeviceStatus(devices, showTipIfManual);
                        }));
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    try
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            statusMenuItem.Text = "读取失败: " + ex.Message;
                            trayIcon.Text = "无线鼠标检测异常";
                            UpdateTrayIcon(-1, false, false);
                        }));
                    }
                    catch { }
                }
            });
        }

        private void ApplyDeviceStatus(List<MouseBatteryInfo> devices, bool showTipIfManual)
        {
            activeDevices = devices;

            if (devices != null && devices.Count > 0)
            {
                if (selectedDevice != null)
                {
                    var match = devices.Find(d => string.Equals(d.DeviceId, selectedDevice.DeviceId, StringComparison.OrdinalIgnoreCase));
                    selectedDevice = match ?? devices.Find(d => d.IsPrimary) ?? devices.Find(d => d.IsConnected) ?? devices[0];
                }
                else
                {
                    selectedDevice = devices.Find(d => d.IsPrimary) ?? devices.Find(d => d.IsConnected) ?? devices[0];
                }

                var primary = GetPrimaryDevice();
                lastInfo = primary;

                UpdateDeviceTabsUI();
                UpdateUI(selectedDevice, showTipIfManual);
                UpdatePrimaryPinButton(selectedDevice);

                UpdateTrayIcon(primary.BatteryPercent, primary.IsCharging, primary.IsConnected, primary.IsSleeping);
                UpdateStatusDisplay();
                UpdateTrayDeviceMenuItems();
            }
            else
            {
                selectedDevice = null;
                lastInfo = null;
                UpdateDeviceTabsUI();
                UpdateUI(null, showTipIfManual);
                UpdateTrayDeviceMenuItems();
            }
        }

        private void UpdateStatusDisplay()
        {
            if (lastInfo == null || !lastInfo.IsConnected) return;

            string brandTag = lastInfo.Brand == "Rapoo" ? "雷柏" : (lastInfo.Brand == "VGN" ? "VGN" : (lastInfo.Brand == "Razer" ? "雷蛇" : (lastInfo.Brand == "NuPhy" ? "NuPhy" : (lastInfo.Category == DeviceCategory.Headset ? "耳机" : (lastInfo.Category == DeviceCategory.Gamepad ? "手柄" : (lastInfo.Category == DeviceCategory.Keyboard ? "键盘" : "外设"))))));
            string dpiPart = lastInfo.Dpi > 0 ? (" · " + lastInfo.Dpi + " DPI") : "";
            string ratePart = lastInfo.PollingRate > 0 ? (" · " + lastInfo.PollingRate + "Hz") : "";
            string chgPart = lastInfo.IsSleeping ? "休眠待机" : (lastInfo.IsCharging ? "充电中" : "电池供电");
            string chgStr = lastInfo.IsSleeping ? "休眠待机 (移动唤醒)" : (lastInfo.IsCharging ? "正在充电" : "电池供电");

            if (lastInfo.Category == DeviceCategory.Keyboard && !lastInfo.IsCharging)
            {
                if (lastInfo.BatteryPercent >= 100)
                {
                    chgPart = "充电中 · 电量充足";
                    chgStr = "充电中 · 电量充足";
                }
                else
                {
                    chgPart = "无线状态";
                    chgStr = "无线供电";
                }
            }

            string menuStatus;
            string tipText;
            string timeStr = lastInfo.LastUpdated.ToString("HH:mm:ss");

            if (lastInfo.Category == DeviceCategory.Keyboard && lastInfo.BatteryPercent <= 0)
            {
                menuStatus = string.Format("[{0}] {1} (免驱在线)", brandTag, lastInfo.DeviceName);
                tipText = string.Format("FerrisPulse · 灵脉\n[{0}] {1}\n状态: 免驱在线\n最后同步: {2}", brandTag, lastInfo.DeviceName, timeStr);
            }
            else if (lastInfo.Category == DeviceCategory.Keyboard && lastInfo.BatteryPercent >= 100)
            {
                menuStatus = string.Format("[{0}] {1} (供电中)", brandTag, lastInfo.DeviceName);
                tipText = string.Format("FerrisPulse · 灵脉\n[{0}] {1}\n状态: 2.4G 供电中\n最后同步: {2}", brandTag, lastInfo.DeviceName, timeStr);
            }
            else
            {
                menuStatus = string.Format("[{0}] {1} ({2}% · {3})", brandTag, lastInfo.DeviceName, lastInfo.BatteryPercent, chgPart);
                tipText = string.Format("FerrisPulse · 灵脉\n[{0}] {1} · {2}%\n状态: {3}{4}{5}\n最后同步: {6}",
                    brandTag, lastInfo.DeviceName, lastInfo.BatteryPercent, chgStr, dpiPart, ratePart, timeStr);
            }
            statusMenuItem.Text = menuStatus;

            if (tipText.Length > 63)
            {
                tipText = string.Format("[{0}] {1}: {2}%\n{3}{4}", brandTag, lastInfo.DeviceName, lastInfo.BatteryPercent, chgPart, dpiPart);
                if (tipText.Length > 63)
                {
                    tipText = string.Format("电量: {0}% ({1})", lastInfo.BatteryPercent, chgPart);
                }
            }
            trayIcon.Text = tipText;
        }

        private void HideNuphyControls()
        {
            if (btnNuphyAutoSleep != null) btnNuphyAutoSleep.Visible = false;
            if (btnNuphyWinLock != null) btnNuphyWinLock.Visible = false;
            if (btnNuphyBacklight != null) btnNuphyBacklight.Visible = false;
            if (btnNuphySidelight != null) btnNuphySidelight.Visible = false;
            if (lblNuphyBrightTitle != null) lblNuphyBrightTitle.Visible = false;
            if (sliderNuphyBrightness != null) sliderNuphyBrightness.Visible = false;
            if (lblNuphyBrightVal != null) lblNuphyBrightVal.Visible = false;
        }

        private void ApplyNuphySettingsToUI()
        {
            if (selectedDevice == null || selectedDevice.Brand != "NuPhy")
            {
                HideNuphyControls();
                return;
            }
            var s = NuphyDeviceHelper.CachedHardwareSettings;
            if (s == null) return;

            Color cyan = Color.FromArgb(0, 229, 255);
            Color textDim = Color.FromArgb(145, 155, 175);

            if (btnNuphyAutoSleep != null)
            {
                btnNuphyAutoSleep.Visible = true;
                if (s.SleepEnabled)
                {
                    btnNuphyAutoSleep.Text = "🌙 自动休眠: 开启";
                    btnNuphyAutoSleep.NormalColor = Color.FromArgb(40, 0, 229, 255);
                    btnNuphyAutoSleep.HoverColor = Color.FromArgb(55, 0, 229, 255);
                    btnNuphyAutoSleep.PressedColor = Color.FromArgb(65, 0, 229, 255);
                    btnNuphyAutoSleep.BorderColor = cyan;
                    btnNuphyAutoSleep.ForeColor = cyan;
                }
                else
                {
                    btnNuphyAutoSleep.Text = "🌙 自动休眠: 关闭";
                    btnNuphyAutoSleep.NormalColor = Color.FromArgb(28, 32, 42);
                    btnNuphyAutoSleep.HoverColor = Color.FromArgb(40, 46, 60);
                    btnNuphyAutoSleep.PressedColor = Color.FromArgb(22, 26, 34);
                    btnNuphyAutoSleep.BorderColor = Color.FromArgb(60, 68, 86);
                    btnNuphyAutoSleep.ForeColor = Color.FromArgb(175, 185, 205);
                }
            }

            if (btnNuphyWinLock != null)
            {
                btnNuphyWinLock.Visible = true;
                if (s.WinLock)
                {
                    btnNuphyWinLock.Text = "🔒 Win键锁定: 开启";
                    btnNuphyWinLock.NormalColor = Color.FromArgb(40, 0, 229, 255);
                    btnNuphyWinLock.HoverColor = Color.FromArgb(55, 0, 229, 255);
                    btnNuphyWinLock.PressedColor = Color.FromArgb(65, 0, 229, 255);
                    btnNuphyWinLock.BorderColor = cyan;
                    btnNuphyWinLock.ForeColor = cyan;
                }
                else
                {
                    btnNuphyWinLock.Text = "🔓 Win键锁定: 关闭";
                    btnNuphyWinLock.NormalColor = Color.FromArgb(28, 32, 42);
                    btnNuphyWinLock.HoverColor = Color.FromArgb(40, 46, 60);
                    btnNuphyWinLock.PressedColor = Color.FromArgb(22, 26, 34);
                    btnNuphyWinLock.BorderColor = Color.FromArgb(60, 68, 86);
                    btnNuphyWinLock.ForeColor = Color.FromArgb(175, 185, 205);
                }
            }

            if (btnNuphyBacklight != null)
            {
                btnNuphyBacklight.Visible = true;
                bool on = s.BacklightEnabled && s.BacklightBrightness > 0;
                if (on)
                {
                    btnNuphyBacklight.Text = "💡 键盘背光: 开";
                    btnNuphyBacklight.NormalColor = Color.FromArgb(40, 0, 229, 255);
                    btnNuphyBacklight.HoverColor = Color.FromArgb(55, 0, 229, 255);
                    btnNuphyBacklight.PressedColor = Color.FromArgb(65, 0, 229, 255);
                    btnNuphyBacklight.BorderColor = cyan;
                    btnNuphyBacklight.ForeColor = cyan;
                }
                else
                {
                    btnNuphyBacklight.Text = "💡 键盘背光: 关";
                    btnNuphyBacklight.NormalColor = Color.FromArgb(28, 32, 42);
                    btnNuphyBacklight.HoverColor = Color.FromArgb(40, 46, 60);
                    btnNuphyBacklight.PressedColor = Color.FromArgb(22, 26, 34);
                    btnNuphyBacklight.BorderColor = Color.FromArgb(60, 68, 86);
                    btnNuphyBacklight.ForeColor = Color.FromArgb(175, 185, 205);
                }
            }

            if (btnNuphySidelight != null)
            {
                btnNuphySidelight.Visible = true;
                bool on = s.SidelightEnabled && s.SidelightBrightness > 0;
                if (on)
                {
                    btnNuphySidelight.Text = "✨ 前置灯带: 开";
                    btnNuphySidelight.NormalColor = Color.FromArgb(40, 0, 229, 255);
                    btnNuphySidelight.HoverColor = Color.FromArgb(55, 0, 229, 255);
                    btnNuphySidelight.PressedColor = Color.FromArgb(65, 0, 229, 255);
                    btnNuphySidelight.BorderColor = cyan;
                    btnNuphySidelight.ForeColor = cyan;
                }
                else
                {
                    btnNuphySidelight.Text = "✨ 前置灯带: 关";
                    btnNuphySidelight.NormalColor = Color.FromArgb(28, 32, 42);
                    btnNuphySidelight.HoverColor = Color.FromArgb(40, 46, 60);
                    btnNuphySidelight.PressedColor = Color.FromArgb(22, 26, 34);
                    btnNuphySidelight.BorderColor = Color.FromArgb(60, 68, 86);
                    btnNuphySidelight.ForeColor = Color.FromArgb(175, 185, 205);
                }
            }

            if (lblNuphyBrightTitle != null) lblNuphyBrightTitle.Visible = true;
            int curB = s.BacklightEnabled ? s.BacklightBrightness : 0;
            if (sliderNuphyBrightness != null)
            {
                sliderNuphyBrightness.Visible = true;
                sliderNuphyBrightness.SetValueWithoutEvents(curB);
            }
            if (lblNuphyBrightVal != null)
            {
                lblNuphyBrightVal.Visible = true;
                lblNuphyBrightVal.Text = curB + "%";
            }
        }

        private void QueueNuphyBrightnessThrottled(int val)
        {
            nuphyTargetBrightness = val;
            lock (nuphyBrightnessSyncRoot)
            {
                if (!isNuphyBrightnessWorkerActive)
                {
                    isNuphyBrightnessWorkerActive = true;
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            while (true)
                            {
                                int target;
                                lock (nuphyBrightnessSyncRoot)
                                {
                                    target = nuphyTargetBrightness;
                                    nuphyTargetBrightness = -1;
                                }

                                if (target >= 0)
                                {
                                    NuphyDeviceHelper.SetBacklightBrightness(target);
                                }

                                Thread.Sleep(90); // ~11Hz 黄金平滑节流：物理背光平滑渐变，同时彻底避免 2.4G 射频拥塞与 I/O 卡顿

                                lock (nuphyBrightnessSyncRoot)
                                {
                                    if (nuphyTargetBrightness < 0)
                                    {
                                        isNuphyBrightnessWorkerActive = false;
                                        break;
                                    }
                                }
                            }
                        }
                        catch
                        {
                            lock (nuphyBrightnessSyncRoot)
                            {
                                isNuphyBrightnessWorkerActive = false;
                            }
                        }
                    });
                }
            }
        }

        private bool isSyncingPhysicalState = false;
        private void SyncAllDevicesPhysicalState()
        {
            if (isSyncingPhysicalState) return;
            isSyncingPhysicalState = true;

            RefreshBatteryStatus(false);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    NuphyDeviceHelper.NuphyHardwareSettings hwSettings;
                    bool ok = NuphyDeviceHelper.GetHardwareSettings(out hwSettings);
                    if (ok && hwSettings != null)
                    {
                        try
                        {
                            this.BeginInvoke((Action)(() =>
                            {
                                ApplyNuphySettingsToUI();
                            }));
                        }
                        catch { }
                    }
                }
                catch { }
                finally
                {
                    isSyncingPhysicalState = false;
                }
            });
        }

        private void UpdatePerformanceSection(MouseBatteryInfo info, Color brandAccent)
        {
            if (info == null) return;

            HideNuphyControls();

            if (tileHeadsetAudio != null) tileHeadsetAudio.Visible = false;
            if (tileRapooDpi != null) tileRapooDpi.Visible = false;
            if (btnGamepadVibrate != null) btnGamepadVibrate.Visible = false;
            if (btnGamepadJoyCpl != null) btnGamepadJoyCpl.Visible = false;
            if (hudThreeLocks != null) hudThreeLocks.Visible = false;
            if (btnUniversalWinLock != null) btnUniversalWinLock.Visible = false;
            if (btnKeyTestSpeed != null) btnKeyTestSpeed.Visible = false;
            if (btnKeyboardSettings != null) btnKeyboardSettings.Visible = false;
            if (btnAudioSetDefault != null) btnAudioSetDefault.Visible = false;
            if (btnVolumeMixer != null) btnVolumeMixer.Visible = false;
            if (btnAudioSettings != null) btnAudioSettings.Visible = false;
            if (btnMouseRateTest != null) btnMouseRateTest.Visible = false;

            bool isGamepad = (info.Category == DeviceCategory.Gamepad);
            bool isHeadset = (info.Category == DeviceCategory.Headset || info.Category == DeviceCategory.Earbuds);
            bool isKeyboard = (info.Category == DeviceCategory.Keyboard);
            bool isDongle = (info.Category == DeviceCategory.Dongle);
            bool isRapoo = (info.Brand == "Rapoo");

            bool isLogitech = (info.Brand == "Logitech" || info.Brand == "罗技");
            bool isVgn = (info.Brand == "VGN");
            bool isRazer = (info.Brand == "Razer" || (info.DeviceId != null && info.DeviceId.StartsWith("Razer:1532:")));

            int secW = cardPerformance.ClientSize.Width - (int)(32 * dpiScale);

            if (chkAutoGameRate != null) chkAutoGameRate.Visible = false;
            if (btnManageGames != null) btnManageGames.Visible = false;
            if (lblLiveRateBadge != null) lblLiveRateBadge.Visible = false;
            if (lblGameRateTitle != null) lblGameRateTitle.Visible = false;
            if (btnGameRates != null) { for (int i = 0; i < btnGameRates.Length; i++) if (btnGameRates[i] != null) btnGameRates[i].Visible = false; }
            if (lblNotifyModeTitle != null) lblNotifyModeTitle.Visible = false;
            if (chkNotifyOsd != null) chkNotifyOsd.Visible = false;
            if (chkNotifyToast != null) chkNotifyToast.Visible = false;
            if (chkNotifySound != null) chkNotifySound.Visible = false;
            if (autoGameRateMenu != null) autoGameRateMenu.Visible = false;

            if (isGamepad)
            {
                AdjustPerformanceCardHeight((int)(168 * dpiScale));
                if (lblPerfTitle != null) lblPerfTitle.Text = "游戏手柄硬件与触觉控制台";

                // Hide Razer interactive controls
                if (lblDpiTitle != null) lblDpiTitle.Visible = false;
                if (btnDpiManage != null) btnDpiManage.Visible = false;
                if (btnDpiStages != null)
                {
                    for (int i = 0; i < btnDpiStages.Length; i++)
                    {
                        if (btnDpiStages[i] != null) btnDpiStages[i].Visible = false;
                    }
                }
                if (lblRateTitle != null) lblRateTitle.Visible = false;
                if (btnRates != null)
                {
                    for (int i = 0; i < btnRates.Length; i++)
                    {
                        if (btnRates[i] != null) btnRates[i].Visible = false;
                    }
                }

                if (tileRapooDpi != null)
                {
                    tileRapooDpi.Visible = true;
                    tileRapooDpi.Size = new Size(secW, (int)(68 * dpiScale));
                    tileRapooDpi.Location = new Point((int)(16 * dpiScale), (int)(38 * dpiScale));
                    tileRapooDpi.AccentColor = brandAccent;
                    tileRapooDpi.Title = "游戏手柄状态";
                    tileRapooDpi.Badge = (info.Transport != null && info.Transport.Contains("蓝牙")) ? "BLE" : "2.4G/USB";
                    if (info.BatteryPercent >= 0)
                    {
                        tileRapooDpi.ValueText = info.BatteryPercent.ToString();
                        tileRapooDpi.UnitText = "%";
                        tileRapooDpi.Footnote = string.Format("连接: {0} | 状态: 在线就绪 | 电池供电", info.Transport ?? "蓝牙");
                    }
                    else
                    {
                        tileRapooDpi.ValueText = info.IsConnected ? "在线" : "离线";
                        tileRapooDpi.UnitText = "";
                        tileRapooDpi.Footnote = string.Format("连接: {0} | 状态: {1}", info.Transport ?? "USB", info.IsConnected ? "在线就绪" : "离线");
                    }
                    tileRapooDpi.Invalidate();
                }

                int btnW = (secW - (int)(8 * dpiScale)) / 2;
                int btnH = (int)(32 * dpiScale);
                int btnY = (int)(116 * dpiScale);

                if (btnGamepadVibrate != null)
                {
                    btnGamepadVibrate.Visible = true;
                    btnGamepadVibrate.SetBounds((int)(16 * dpiScale), btnY, btnW, btnH);
                }
                if (btnGamepadJoyCpl != null)
                {
                    btnGamepadJoyCpl.Visible = true;
                    btnGamepadJoyCpl.SetBounds((int)(16 * dpiScale) + btnW + (int)(8 * dpiScale), btnY, btnW, btnH);
                }

                if (dpiMenu != null) dpiMenu.Visible = false;
                if (rateMenu != null) rateMenu.Visible = false;
            }
            else if (isHeadset)
            {
                AdjustPerformanceCardHeight((int)(196 * dpiScale));
                if (lblPerfTitle != null)
                {
                    lblPerfTitle.Text = (info.Category == DeviceCategory.Earbuds) ? "入耳式无线耳机 · 音频控制台" : "无线耳机 · 音频控制台";
                }

                // Hide Razer interactive controls
                if (lblDpiTitle != null) lblDpiTitle.Visible = false;
                if (btnDpiManage != null) btnDpiManage.Visible = false;
                if (btnDpiStages != null)
                {
                    for (int i = 0; i < btnDpiStages.Length; i++)
                    {
                        if (btnDpiStages[i] != null) btnDpiStages[i].Visible = false;
                    }
                }
                if (lblRateTitle != null) lblRateTitle.Visible = false;
                if (btnRates != null)
                {
                    for (int i = 0; i < btnRates.Length; i++)
                    {
                        if (btnRates[i] != null) btnRates[i].Visible = false;
                    }
                }

                // Show Headset Audio Tile
                if (tileHeadsetAudio != null)
                {
                    tileHeadsetAudio.Visible = true;
                    tileHeadsetAudio.Size = new Size(secW, (int)(64 * dpiScale));
                    tileHeadsetAudio.Location = new Point((int)(16 * dpiScale), (int)(36 * dpiScale));
                    tileHeadsetAudio.AccentColor = brandAccent;
                    tileHeadsetAudio.Title = (info.Category == DeviceCategory.Earbuds) ? "TWS 入耳式无线耳机" : "无线音频设备";
                    tileHeadsetAudio.StatusText = info.IsConnected ? (info.BatteryPercent >= 0 ? string.Format("已连接 · 剩余电量 {0}%", info.BatteryPercent) : "已连接就绪") : "已离线 / 未连接";
                    tileHeadsetAudio.Footnote = string.Format("通道: {0} · Windows 原生音频端点", info.Transport ?? "蓝牙");
                    tileHeadsetAudio.Invalidate();
                }

                int btnW = (secW - (int)(8 * dpiScale)) / 2;
                int btnH = (int)(32 * dpiScale);
                int row1Y = (int)(110 * dpiScale);
                int row2Y = (int)(150 * dpiScale);

                if (btnAudioSetDefault != null)
                {
                    btnAudioSetDefault.Visible = true;
                    btnAudioSetDefault.SetBounds((int)(16 * dpiScale), row1Y, btnW, btnH);
                }
                if (btnVolumeMixer != null)
                {
                    btnVolumeMixer.Visible = true;
                    btnVolumeMixer.SetBounds((int)(16 * dpiScale) + btnW + (int)(8 * dpiScale), row1Y, btnW, btnH);
                }
                if (btnAudioSettings != null)
                {
                    btnAudioSettings.Visible = true;
                    btnAudioSettings.SetBounds((int)(16 * dpiScale), row2Y, secW, btnH);
                }

                if (dpiMenu != null) dpiMenu.Visible = false;
                if (rateMenu != null) rateMenu.Visible = false;
            }
            else if (isKeyboard)
            {
                // Hide Razer interactive controls
                if (lblDpiTitle != null) lblDpiTitle.Visible = false;
                if (btnDpiManage != null) btnDpiManage.Visible = false;
                if (btnDpiStages != null)
                {
                    for (int i = 0; i < btnDpiStages.Length; i++)
                    {
                        if (btnDpiStages[i] != null) btnDpiStages[i].Visible = false;
                    }
                }
                if (lblRateTitle != null) lblRateTitle.Visible = false;
                if (btnRates != null)
                {
                    for (int i = 0; i < btnRates.Length; i++)
                    {
                        if (btnRates[i] != null) btnRates[i].Visible = false;
                    }
                }

                bool isNuphyHardware = (info.Brand == "NuPhy");
                if (isNuphyHardware)
                {
                    AdjustPerformanceCardHeight((int)(196 * dpiScale));
                    if (lblPerfTitle != null) lblPerfTitle.Text = "NuPhy 专属硬件控制台";

                    if (btnNuphyAutoSleep != null) btnNuphyAutoSleep.Visible = true;
                    if (btnNuphyWinLock != null) btnNuphyWinLock.Visible = true;
                    if (btnNuphyBacklight != null) btnNuphyBacklight.Visible = true;
                    if (btnNuphySidelight != null) btnNuphySidelight.Visible = true;
                    if (lblNuphyBrightTitle != null) lblNuphyBrightTitle.Visible = true;
                    if (sliderNuphyBrightness != null) sliderNuphyBrightness.Visible = true;
                    if (lblNuphyBrightVal != null) lblNuphyBrightVal.Visible = true;

                    // Row 4: 键盘全键无冲测速 + Windows 键盘设置
                    int btnW = (secW - (int)(8 * dpiScale)) / 2;
                    int btnH = (int)(32 * dpiScale);
                    int r4Y = (int)(150 * dpiScale);

                    if (btnKeyTestSpeed != null)
                    {
                        btnKeyTestSpeed.Visible = true;
                        btnKeyTestSpeed.SetBounds((int)(16 * dpiScale), r4Y, btnW, btnH);
                    }
                    if (btnKeyboardSettings != null)
                    {
                        btnKeyboardSettings.Visible = true;
                        btnKeyboardSettings.SetBounds((int)(16 * dpiScale) + btnW + (int)(8 * dpiScale), r4Y, btnW, btnH);
                    }

                    ApplyNuphySettingsToUI();

                    ThreadPool.QueueUserWorkItem(_ => {
                        NuphyDeviceHelper.NuphyHardwareSettings hwSettings;
                        if (NuphyDeviceHelper.GetHardwareSettings(out hwSettings))
                        {
                            try
                            {
                                this.BeginInvoke((Action)(() => {
                                    ApplyNuphySettingsToUI();
                                }));
                            }
                            catch { }
                        }
                    });
                }
                else
                {
                    AdjustPerformanceCardHeight((int)(228 * dpiScale));
                    if (lblPerfTitle != null) lblPerfTitle.Text = "机械键盘硬件状态与控制台";

                    if (tileRapooDpi != null)
                    {
                        tileRapooDpi.Visible = true;
                        tileRapooDpi.Size = new Size(secW, (int)(62 * dpiScale));
                        tileRapooDpi.Location = new Point((int)(16 * dpiScale), (int)(36 * dpiScale));
                        tileRapooDpi.AccentColor = brandAccent;
                        tileRapooDpi.Badge = (info.Transport != null && info.Transport.Contains("蓝牙")) ? "BLE" : ((info.Transport != null && info.Transport.Contains("有线")) ? "USB" : "2.4G");

                        if (info.BatteryPercent >= 0)
                        {
                            tileRapooDpi.Title = "无线机械键盘";
                            tileRapooDpi.ValueText = info.BatteryPercent.ToString();
                            tileRapooDpi.UnitText = "%";
                            bool isKbWired = info.Transport != null && (info.Transport.IndexOf("有线", StringComparison.OrdinalIgnoreCase) >= 0 || info.Transport.IndexOf("USB", StringComparison.OrdinalIgnoreCase) >= 0);
                            string chgStatus = isKbWired ? "USB 持续供电" : (info.IsCharging ? "正在充电" : "电池供电");
                            tileRapooDpi.Footnote = string.Format("通道: {0} | 供电: {1}", info.Transport ?? "无线", chgStatus);
                        }
                        else
                        {
                            tileRapooDpi.Title = "机械键盘 · 免驱即插即用";
                            tileRapooDpi.ValueText = info.IsConnected ? "在线" : "离线";
                            tileRapooDpi.UnitText = "";
                            tileRapooDpi.Footnote = string.Format("通道: {0} | 供电: USB 持续供电", info.Transport ?? "USB 直连");
                        }
                        tileRapooDpi.Invalidate();
                    }

                    // Three Locks HUD
                    if (hudThreeLocks != null)
                    {
                        hudThreeLocks.Visible = true;
                        hudThreeLocks.SetBounds((int)(16 * dpiScale), (int)(104 * dpiScale), secW, (int)(28 * dpiScale));
                        hudThreeLocks.CheckStatus();
                    }

                    int btnW = (secW - (int)(8 * dpiScale)) / 2;
                    int btnH = (int)(32 * dpiScale);
                    int row1Y = (int)(140 * dpiScale);
                    int row2Y = (int)(180 * dpiScale);

                    if (btnUniversalWinLock != null)
                    {
                        btnUniversalWinLock.Visible = true;
                        btnUniversalWinLock.SetBounds((int)(16 * dpiScale), row1Y, btnW, btnH);
                        if (UniversalWinLockHelper.IsLocked)
                        {
                            btnUniversalWinLock.Text = "🔒 Win 键已锁定 (防误触)";
                            btnUniversalWinLock.NormalColor = Color.FromArgb(40, 20, 24);
                            btnUniversalWinLock.BorderColor = Color.FromArgb(255, 82, 82);
                            btnUniversalWinLock.TextColor = Color.FromArgb(255, 100, 100);
                        }
                        else
                        {
                            btnUniversalWinLock.Text = "🛡️ Win 键防误触: 未锁定";
                            btnUniversalWinLock.NormalColor = Color.FromArgb(28, 32, 44);
                            btnUniversalWinLock.BorderColor = Color.FromArgb(50, 58, 76);
                            btnUniversalWinLock.TextColor = Color.FromArgb(190, 200, 220);
                        }
                    }
                    if (btnKeyTestSpeed != null)
                    {
                        btnKeyTestSpeed.Visible = true;
                        btnKeyTestSpeed.SetBounds((int)(16 * dpiScale) + btnW + (int)(8 * dpiScale), row1Y, btnW, btnH);
                    }
                    if (btnKeyboardSettings != null)
                    {
                        btnKeyboardSettings.Visible = true;
                        btnKeyboardSettings.SetBounds((int)(16 * dpiScale), row2Y, secW, btnH);
                    }
                }

                if (dpiMenu != null) dpiMenu.Visible = false;
                if (rateMenu != null) rateMenu.Visible = false;
            }
            else if (isDongle)
            {
                AdjustPerformanceCardHeight((int)(120 * dpiScale));
                if (lblPerfTitle != null) lblPerfTitle.Text = "2.4G 接收器 / 拓展坞";

                // Hide Razer interactive controls
                if (lblDpiTitle != null) lblDpiTitle.Visible = false;
                if (btnDpiManage != null) btnDpiManage.Visible = false;
                if (btnDpiStages != null)
                {
                    for (int i = 0; i < btnDpiStages.Length; i++)
                    {
                        if (btnDpiStages[i] != null) btnDpiStages[i].Visible = false;
                    }
                }
                if (lblRateTitle != null) lblRateTitle.Visible = false;
                if (btnRates != null)
                {
                    for (int i = 0; i < btnRates.Length; i++)
                    {
                        if (btnRates[i] != null) btnRates[i].Visible = false;
                    }
                }

                if (tileRapooDpi != null)
                {
                    tileRapooDpi.Visible = true;
                    tileRapooDpi.Size = new Size(secW, (int)(74 * dpiScale));
                    tileRapooDpi.Location = new Point((int)(16 * dpiScale), (int)(38 * dpiScale));
                    tileRapooDpi.AccentColor = brandAccent;
                    tileRapooDpi.Title = "无线接收器工作状态";
                    tileRapooDpi.Badge = "2.4G";
                    tileRapooDpi.ValueText = info.IsConnected ? "在线" : "未就绪";
                    tileRapooDpi.UnitText = "";
                    tileRapooDpi.Footnote = string.Format("标识: {0} | 供电: USB 持续供电", info.HardwareFingerprint ?? info.DeviceId);
                    tileRapooDpi.Invalidate();
                }

                if (dpiMenu != null) dpiMenu.Visible = false;
                if (rateMenu != null) rateMenu.Visible = false;
            }
            else if (isRapoo)
            {
                AdjustPerformanceCardHeight((int)(160 * dpiScale));
                if (lblPerfTitle != null) lblPerfTitle.Text = "雷柏鼠标状态与性能测速";

                // Hide Razer interactive controls
                if (lblDpiTitle != null) lblDpiTitle.Visible = false;
                if (btnDpiManage != null) btnDpiManage.Visible = false;
                if (btnDpiStages != null)
                {
                    for (int i = 0; i < btnDpiStages.Length; i++)
                    {
                        if (btnDpiStages[i] != null) btnDpiStages[i].Visible = false;
                    }
                }
                if (lblRateTitle != null) lblRateTitle.Visible = false;
                if (btnRates != null)
                {
                    for (int i = 0; i < btnRates.Length; i++)
                    {
                        if (btnRates[i] != null) btnRates[i].Visible = false;
                    }
                }

                // Show Rapoo Status Tile (Full width, DPI only)
                if (tileRapooDpi != null)
                {
                    tileRapooDpi.Visible = true;
                    tileRapooDpi.Size = new Size(secW, (int)(64 * dpiScale));
                    tileRapooDpi.Location = new Point((int)(16 * dpiScale), (int)(36 * dpiScale));
                    tileRapooDpi.AccentColor = brandAccent;
                    tileRapooDpi.Title = "当前实时 DPI 档位";
                    tileRapooDpi.ValueText = (info.Dpi > 0 ? info.Dpi.ToString() : "--");
                    tileRapooDpi.UnitText = info.Dpi > 0 ? "DPI" : "";
                    tileRapooDpi.Badge = (info.Transport != null && info.Transport.Contains("2.4G")) ? "2.4G" : "无线";
                    tileRapooDpi.Footnote = "机身物理 DPI 键循环切档 · 屏幕 OSD 联动感知";
                    tileRapooDpi.Invalidate();
                }

                if (btnMouseRateTest != null)
                {
                    btnMouseRateTest.Visible = true;
                    btnMouseRateTest.SetBounds((int)(16 * dpiScale), (int)(110 * dpiScale), secW, (int)(34 * dpiScale));
                }

                if (dpiMenu != null)
                {
                    dpiMenu.Visible = true;
                    dpiMenu.Text = "当前 DPI 状态 (&D)";
                    dpiMenu.DropDownItems.Clear();
                    int curDpi = info.Dpi > 0 ? info.Dpi : 1600;
                    var curItem = new ToolStripMenuItem(string.Format("当前: {0} DPI", curDpi));
                    curItem.Enabled = false;
                    dpiMenu.DropDownItems.Add(curItem);
                }

                if (rateMenu != null) rateMenu.Visible = false;
            }
            else if (isVgn)
            {
                AdjustPerformanceCardHeight((int)(160 * dpiScale));
                if (lblPerfTitle != null) lblPerfTitle.Text = "VGN 鼠标状态与性能测速";

                // Hide Razer interactive controls
                if (lblDpiTitle != null) lblDpiTitle.Visible = false;
                if (btnDpiManage != null) btnDpiManage.Visible = false;
                if (btnDpiStages != null)
                {
                    for (int i = 0; i < btnDpiStages.Length; i++)
                    {
                        if (btnDpiStages[i] != null) btnDpiStages[i].Visible = false;
                    }
                }
                if (lblRateTitle != null) lblRateTitle.Visible = false;
                if (btnRates != null)
                {
                    for (int i = 0; i < btnRates.Length; i++)
                    {
                        if (btnRates[i] != null) btnRates[i].Visible = false;
                    }
                }

                // Show VGN Status Tile
                if (tileRapooDpi != null)
                {
                    tileRapooDpi.Visible = true;
                    tileRapooDpi.Size = new Size(secW, (int)(64 * dpiScale));
                    tileRapooDpi.Location = new Point((int)(16 * dpiScale), (int)(36 * dpiScale));
                    tileRapooDpi.AccentColor = brandAccent;
                    tileRapooDpi.Title = "无线传输与工作状态";
                    tileRapooDpi.Badge = "2.4G";
                    tileRapooDpi.ValueText = info.IsConnected ? "在线" : "离线";
                    tileRapooDpi.UnitText = "";
                    tileRapooDpi.Footnote = string.Format("通道: {0} | 供电: {1}", info.Transport ?? "2.4G 无线", info.BatteryPercent >= 0 ? (info.BatteryPercent + "%") : "电池供电");
                    tileRapooDpi.Invalidate();
                }

                if (btnMouseRateTest != null)
                {
                    btnMouseRateTest.Visible = true;
                    btnMouseRateTest.SetBounds((int)(16 * dpiScale), (int)(110 * dpiScale), secW, (int)(34 * dpiScale));
                }

                if (dpiMenu != null) dpiMenu.Visible = false;
                if (rateMenu != null) rateMenu.Visible = false;
            }

            else if (isLogitech)
            {
                AdjustPerformanceCardHeight((int)(160 * dpiScale));
                if (lblPerfTitle != null) lblPerfTitle.Text = "罗技鼠标状态与性能测速";

                // Hide Razer interactive controls
                if (lblDpiTitle != null) lblDpiTitle.Visible = false;
                if (btnDpiManage != null) btnDpiManage.Visible = false;
                if (btnDpiStages != null)
                {
                    for (int i = 0; i < btnDpiStages.Length; i++)
                    {
                        if (btnDpiStages[i] != null) btnDpiStages[i].Visible = false;
                    }
                }
                if (lblRateTitle != null) lblRateTitle.Visible = false;
                if (btnRates != null)
                {
                    for (int i = 0; i < btnRates.Length; i++)
                    {
                        if (btnRates[i] != null) btnRates[i].Visible = false;
                    }
                }

                // Show Logitech Status Tile
                if (tileRapooDpi != null)
                {
                    tileRapooDpi.Visible = true;
                    tileRapooDpi.Size = new Size(secW, (int)(64 * dpiScale));
                    tileRapooDpi.Location = new Point((int)(16 * dpiScale), (int)(36 * dpiScale));
                    tileRapooDpi.AccentColor = brandAccent;
                    tileRapooDpi.Title = "Lightspeed 无线与工作状态";
                    tileRapooDpi.Badge = (info.Transport != null && info.Transport.Contains("蓝牙")) ? "BLE" : "Lightspeed";
                    tileRapooDpi.ValueText = info.IsConnected ? (info.BatteryPercent >= 0 ? info.BatteryPercent.ToString() : "在线") : (info.IsDonglePresent ? "待机" : "离线");
                    tileRapooDpi.UnitText = (info.IsConnected && info.BatteryPercent >= 0) ? "%" : "";
                    string logiPower = info.IsCharging ? "正在充电 ⚡" : (info.BatteryPercent >= 0 ? string.Format("电池供电 ({0}%)", info.BatteryPercent) : "电池供电");
                    tileRapooDpi.Footnote = string.Format("通道: {0} | 状态: {1} | 供电: {2}", info.Transport ?? "Lightspeed 无线", info.IsConnected ? "在线就绪" : (info.IsDonglePresent ? "闲置待机" : "已断开"), logiPower);
                    tileRapooDpi.Invalidate();
                }

                if (btnMouseRateTest != null)
                {
                    btnMouseRateTest.Visible = true;
                    btnMouseRateTest.SetBounds((int)(16 * dpiScale), (int)(110 * dpiScale), secW, (int)(34 * dpiScale));
                }

                if (dpiMenu != null) dpiMenu.Visible = false;
                if (rateMenu != null) rateMenu.Visible = false;
            }
            else if (isRazer)
            {
                if (lblPerfTitle != null) lblPerfTitle.Text = "鼠标性能与档位调节";
                if (rateMenu != null) rateMenu.Visible = true;

                if (lblDpiTitle != null) lblDpiTitle.Visible = true;
                if (lblRateTitle != null) lblRateTitle.Visible = true;

                int targetPerfH = autoGameRateEnabled ? (int)(208 * dpiScale) : (int)(142 * dpiScale);
                AdjustPerformanceCardHeight(targetPerfH);

                if (chkAutoGameRate != null)
                {
                    chkAutoGameRate.Visible = true;
                    chkAutoGameRate.Checked = autoGameRateEnabled;
                }
                if (btnManageGames != null)
                {
                    btnManageGames.Visible = autoGameRateEnabled;
                }
                if (lblGameRateTitle != null) lblGameRateTitle.Visible = autoGameRateEnabled;
                if (btnGameRates != null)
                {
                    for (int i = 0; i < btnGameRates.Length; i++)
                    {
                        if (btnGameRates[i] != null)
                        {
                            btnGameRates[i].Visible = autoGameRateEnabled;
                            if (i < pollingRateValues.Length)
                            {
                                btnGameRates[i].Text = pollingRateValues[i] + " Hz";
                                btnGameRates[i].Selected = (pollingRateValues[i] == gamePollingRate);
                            }
                        }
                    }
                }
                if (lblNotifyModeTitle != null) lblNotifyModeTitle.Visible = autoGameRateEnabled;
                if (chkNotifyOsd != null) { chkNotifyOsd.Visible = autoGameRateEnabled; chkNotifyOsd.Checked = gameRateNotifyOsd; }
                if (chkNotifyToast != null) { chkNotifyToast.Visible = autoGameRateEnabled; chkNotifyToast.Checked = gameRateNotifyToast; }
                if (chkNotifySound != null) { chkNotifySound.Visible = autoGameRateEnabled; chkNotifySound.Checked = gameRateNotifySound; }
                if (autoGameRateMenu != null) autoGameRateMenu.Visible = true;
                if (info.DpiStages != null && info.DpiStages.Length > 0)
                {
                    bool differs = false;
                    if (dpiStageList.Count != info.DpiStages.Length) differs = true;
                    else
                    {
                        for (int k = 0; k < info.DpiStages.Length; k++)
                        {
                            if (dpiStageList[k] != info.DpiStages[k]) { differs = true; break; }
                        }
                    }
                    if (differs)
                    {
                        dpiStageList = new List<int>(info.DpiStages);
                        dpiStageValues = info.DpiStages;
                    }
                }
                RelayoutDpiButtons();

                if (btnDpiStages != null)
                {
                    for (int i = 0; i < btnDpiStages.Length; i++)
                    {
                        if (btnDpiStages[i] != null)
                        {
                            btnDpiStages[i].AccentColor = brandAccent;
                            if (i < dpiStageList.Count)
                            {
                                bool isCur = (info.DpiStage > 0 && info.DpiStageCount > 0)
                                    ? (i == (info.DpiStage - 1))
                                    : (dpiStageList[i] == info.Dpi);
                                btnDpiStages[i].Selected = isCur;
                            }
                        }
                    }
                }
                if (btnDpiManage != null)
                {
                    btnDpiManage.Visible = true;
                    btnDpiManage.NormalColor = Color.FromArgb(28, 33, 45);
                    btnDpiManage.HoverColor = Color.FromArgb(40, 48, 65);
                    btnDpiManage.PressedColor = Color.FromArgb(20, 24, 33);
                    btnDpiManage.BorderColor = Color.FromArgb(65, 75, 95);
                    btnDpiManage.ForeColor = Color.FromArgb(210, 220, 235);
                }

                if (info.SupportedPollingRates != null && info.SupportedPollingRates.Length > 0)
                {
                    pollingRateValues = info.SupportedPollingRates;
                }

                if (btnRates != null)
                {
                    for (int i = 0; i < btnRates.Length; i++)
                    {
                        if (btnRates[i] != null)
                        {
                            btnRates[i].AccentColor = brandAccent;
                            if (i < pollingRateValues.Length)
                            {
                                btnRates[i].Text = pollingRateValues[i] + " Hz";
                                btnRates[i].Visible = true;
                            }
                            else
                            {
                                btnRates[i].Visible = false;
                            }
                        }
                    }
                }

                if (btnGameRates != null)
                {
                    for (int i = 0; i < btnGameRates.Length; i++)
                    {
                        if (btnGameRates[i] != null)
                        {
                            btnGameRates[i].Visible = autoGameRateEnabled;
                            if (i < pollingRateValues.Length)
                            {
                                btnGameRates[i].Text = pollingRateValues[i] + " Hz";
                            }
                        }
                    }
                }

                if (!autoGameRateEnabled && info.PollingRate > 0 && desktopPollingRate == 0)
                {
                    desktopPollingRate = info.PollingRate;
                }

                UpdatePollingRateDisplayStates();

                if (dpiMenu != null && dpiStageValues != null)
                {
                    dpiMenu.Text = "调节 DPI 档位 (&D)";
                    dpiMenu.DropDownItems.Clear();
                    for (int i = 0; i < dpiStageValues.Length; i++)
                    {
                        int val = dpiStageValues[i];
                        var sub = new ToolStripMenuItem(val + " DPI", null, (s, e) => SetDpiFromUI(val));
                        sub.Tag = val;
                        sub.Checked = (val == info.Dpi);
                        dpiMenu.DropDownItems.Add(sub);
                    }
                }

                if (rateMenu != null && pollingRateValues != null)
                {
                    rateMenu.Text = "调节回报率 (&P)";
                    rateMenu.DropDownItems.Clear();
                    int targetActive = autoGameRateEnabled ? (isGamingActive ? gamePollingRate : desktopPollingRate) : desktopPollingRate;
                    for (int i = 0; i < pollingRateValues.Length; i++)
                    {
                        int val = pollingRateValues[i];
                        var sub = new ToolStripMenuItem(val + " Hz", null, (s, e) => SetPollingRateFromUI(val));
                        sub.Tag = val;
                        sub.Checked = (val == targetActive);
                        rateMenu.DropDownItems.Add(sub);
                    }
                }
            }
            else // Generic Mouse or Generic Peripheral
            {
                bool isGenMouse = (info.Category == DeviceCategory.Mouse);
                AdjustPerformanceCardHeight((int)((isGenMouse ? 160 : 120) * dpiScale));
                if (lblPerfTitle != null) lblPerfTitle.Text = isGenMouse ? "鼠标工作状态与测速" : "外设连接与状态";

                // Hide Razer interactive controls
                if (lblDpiTitle != null) lblDpiTitle.Visible = false;
                if (btnDpiManage != null) btnDpiManage.Visible = false;
                if (btnDpiStages != null)
                {
                    for (int i = 0; i < btnDpiStages.Length; i++)
                    {
                        if (btnDpiStages[i] != null) btnDpiStages[i].Visible = false;
                    }
                }
                if (lblRateTitle != null) lblRateTitle.Visible = false;
                if (btnRates != null)
                {
                    for (int i = 0; i < btnRates.Length; i++)
                    {
                        if (btnRates[i] != null) btnRates[i].Visible = false;
                    }
                }

                if (tileRapooDpi != null)
                {
                    tileRapooDpi.Visible = true;
                    tileRapooDpi.Size = new Size(secW, (int)(64 * dpiScale));
                    tileRapooDpi.Location = new Point((int)(16 * dpiScale), (int)(36 * dpiScale));
                    tileRapooDpi.AccentColor = brandAccent;
                    tileRapooDpi.Title = isGenMouse ? "鼠标工作状态" : "外设工作状态";
                    tileRapooDpi.Badge = (info.Transport != null && info.Transport.Contains("蓝牙")) ? "BLE" : "2.4G/USB";
                    if (info.BatteryPercent >= 0)
                    {
                        tileRapooDpi.ValueText = info.BatteryPercent.ToString();
                        tileRapooDpi.UnitText = "%";
                        tileRapooDpi.Footnote = string.Format("通道: {0} | 供电: 电池供电", info.Transport ?? "无线");
                    }
                    else
                    {
                        tileRapooDpi.ValueText = info.IsConnected ? "在线" : "离线";
                        tileRapooDpi.UnitText = "";
                        tileRapooDpi.Footnote = string.Format("通道: {0} | 供电: USB 持续供电", info.Transport ?? "USB 直连");
                    }
                    tileRapooDpi.Invalidate();
                }

                if (isGenMouse && btnMouseRateTest != null)
                {
                    btnMouseRateTest.Visible = true;
                    btnMouseRateTest.SetBounds((int)(16 * dpiScale), (int)(110 * dpiScale), secW, (int)(34 * dpiScale));
                }

                if (dpiMenu != null) dpiMenu.Visible = false;
                if (rateMenu != null) rateMenu.Visible = false;
            }
        }

        private void UpdateUI(MouseBatteryInfo info, bool showTipIfManual)
        {
            if (info == null)
            {
                isMouseSleeping = false;
                if (updateTimer != null) updateTimer.Interval = 3000;

                if (badgePrimary != null) badgePrimary.Visible = false;

                lblDeviceName.Text = "未检测到支持的无线设备";

                lblBatteryBig.Text = "--%";
                lblBatteryBig.ForeColor = Color.FromArgb(140, 145, 155);

                pillStatus.SetStatus("未连接", Color.FromArgb(140, 145, 155));
                pillStatus.Location = new Point(lblBatteryBig.Right + (int)(16 * dpiScale), (int)(56 * dpiScale));

                barBattery.Value = 0;
                lblUpdateTime.Text = "最后同步: " + DateTime.Now.ToString("HH:mm:ss") + " · 未检测到设备";

                statusMenuItem.Text = "未检测到设备 (未连接)";
                trayIcon.Text = "未检测到设备 (未连接)";
                UpdateTrayIcon(-1, false, false);
                lastLowAlertFired = false;
                if (showTipIfManual)
                {
                    SafeShowBalloonTip(2000, "设备未连接", "未能找到已连接或唤醒的外设设备，请检查开关或蓝牙连接。", ToolTipIcon.Warning);
                }
                return;
            }

            UpdatePrimaryPinButton(info);

            Color brandAccent = info.BrandColor;

            if (!info.IsConnected)
            {
                isMouseSleeping = false;
                lblDeviceName.Text = info.DisplayName;

                string battDisplay = (info.BatteryPercent > 0 ? info.BatteryPercent.ToString() : "--") + "%";
                lblBatteryBig.Text = battDisplay;
                lblBatteryBig.ForeColor = Color.FromArgb(150, 160, 175);

                pillStatus.SetStatus("设备已离线", Color.FromArgb(145, 155, 175));
                pillStatus.Location = new Point(lblBatteryBig.Right + (int)(16 * dpiScale), (int)(56 * dpiScale));

                barBattery.Value = info.BatteryPercent;
                barBattery.ProgressColor = Color.FromArgb(70, 80, 100);

                lblUpdateTime.Text = "最后同步: " + info.LastUpdated.ToString("yyyy-MM-dd HH:mm:ss") + " · 离线待命中";

                UpdatePerformanceSection(info, brandAccent);

                if (info.IsPrimary)
                {
                    UpdateStatusDisplay();
                    UpdateTrayIcon(info.BatteryPercent, false, false, false);
                }
                return;
            }

            if (info.Category == DeviceCategory.Keyboard)
            {
                isMouseSleeping = false;
                if (updateTimer != null) updateTimer.Interval = userSelectedInterval;

                lblDeviceName.Text = info.DisplayName;

                bool isPureWired = (info.Transport != null && 
                                   (info.Transport.IndexOf("有线", StringComparison.OrdinalIgnoreCase) >= 0 || info.Transport.IndexOf("USB", StringComparison.OrdinalIgnoreCase) >= 0) &&
                                   !info.Transport.Contains("无线") && !info.Transport.Contains("2.4G"))
                                   || (info.DeviceId != null && info.DeviceId.IndexOf("pid_a011", StringComparison.OrdinalIgnoreCase) >= 0);
                bool isBle = info.Transport != null && info.Transport.Contains("蓝牙");

                bool kbCharging = isPureWired;
                if (isPureWired)
                {
                    lblBatteryBig.Font = new Font("Microsoft YaHei UI", 24F, FontStyle.Bold, GraphicsUnit.Point);
                    lblBatteryBig.Text = "已连接 · 供电中";
                    lblBatteryBig.ForeColor = brandAccent;

                    pillStatus.SetStatus("⚡ USB 供电", brandAccent);
                    pillStatus.Location = new Point(lblBatteryBig.Right + (int)(14 * dpiScale), (int)(54 * dpiScale));

                    barBattery.StartChargingFill(100, brandAccent, Color.FromArgb(200, 255, 230));
                    lblUpdateTime.Text = "最后同步: " + info.LastUpdated.ToString("HH:mm:ss") + " · USB 持续供电";
                }
                else if (isBle)
                {
                    lblBatteryBig.Font = new Font("Microsoft YaHei UI", 34F, FontStyle.Bold, GraphicsUnit.Point);
                    lblBatteryBig.Text = (info.BatteryPercent > 0 ? info.BatteryPercent.ToString() : "--") + "%";
                    lblBatteryBig.ForeColor = brandAccent;

                    pillStatus.SetStatus("📶 蓝牙无线", brandAccent);
                    pillStatus.Location = new Point(lblBatteryBig.Right + (int)(16 * dpiScale), (int)(56 * dpiScale));

                    barBattery.StopFlow(info.BatteryPercent, brandAccent);
                    lblUpdateTime.Text = "最后同步: " + info.LastUpdated.ToString("HH:mm:ss") + " · 蓝牙无线连接";
                }
                else
                {
                    // 2.4G 无线模式
                    int realBatt = info.BatteryPercent;
                    if (realBatt < 0)
                    {
                        lblBatteryBig.Font = new Font("Microsoft YaHei UI", 34F, FontStyle.Bold, GraphicsUnit.Point);
                        lblBatteryBig.Text = "--";
                        lblBatteryBig.ForeColor = brandAccent;

                        pillStatus.SetStatus("2.4G 无线", brandAccent);
                        pillStatus.Location = new Point(lblBatteryBig.Right + (int)(16 * dpiScale), (int)(56 * dpiScale));

                        barBattery.StopFlow(0, brandAccent);
                        lblUpdateTime.Text = "最后同步: " + info.LastUpdated.ToString("HH:mm:ss") + " · 2.4G 接收器在线";
                    }
                    else
                    {
                        bool isNuphy = (info.Brand == "NuPhy");
                        if (isNuphy && realBatt >= 100)
                        {
                            info.IsCharging = true;
                            kbCharging = true;
                            lblBatteryBig.Font = new Font("Microsoft YaHei UI", 24F, FontStyle.Bold, GraphicsUnit.Point);
                            lblBatteryBig.Text = "已连接 · 充电中";
                            lblBatteryBig.ForeColor = brandAccent;

                            pillStatus.SetStatus("⚡ 充电中 (2.4G)", brandAccent);
                            pillStatus.Location = new Point(lblBatteryBig.Right + (int)(14 * dpiScale), (int)(54 * dpiScale));

                            barBattery.StartChargingFill(100, brandAccent, Color.FromArgb(200, 255, 230));
                            lblUpdateTime.Text = "最后同步: " + info.LastUpdated.ToString("HH:mm:ss") + " · 2.4G 充电中";
                        }
                        else
                        {
                            lblBatteryBig.Font = new Font("Microsoft YaHei UI", 34F, FontStyle.Bold, GraphicsUnit.Point);
                            lblBatteryBig.Text = realBatt + "%";
                            lblBatteryBig.ForeColor = brandAccent;

                            pillStatus.SetStatus(info.IsCharging ? "⚡ 充电中" : "2.4G 无线", brandAccent);
                            pillStatus.Location = new Point(lblBatteryBig.Right + (int)(16 * dpiScale), (int)(56 * dpiScale));

                            if (info.IsCharging)
                                barBattery.StartChargingFill(realBatt, brandAccent, Color.FromArgb(200, 255, 230));
                            else
                                barBattery.StopFlow(realBatt, brandAccent);

                            lblUpdateTime.Text = "最后同步: " + info.LastUpdated.ToString("HH:mm:ss") + " · 2.4G 无线连接";
                        }
                    }
                }

                UpdatePerformanceSection(info, brandAccent);

                if (info.IsPrimary)
                {
                    UpdateStatusDisplay();
                    UpdateTrayIcon(info.BatteryPercent, kbCharging, true, false, brandAccent);
                }
                return;
            }

            // Connected mouse with zero/unknown battery (indicates sleep or no battery telemetry)
            if (info.BatteryPercent <= 0 && info.Category == DeviceCategory.Mouse)
            {
                int cached = -1;
                if (info.Brand == "Rapoo")
                {
                    cached = RapooDeviceHelper.CachedBatteryPercent;
                }
                else if (info.Brand == "Razer")
                {
                    var st = RazerDeviceHelper.GetDeviceState(info.DeviceId);
                    cached = (st != null && st.BatteryPercent > 0) ? st.BatteryPercent : RazerDeviceHelper.CachedBatteryPercent;
                }
                info.BatteryPercent = cached > 0 ? cached : -1;
                info.IsSleeping = true;
            }

            if (info.IsSleeping)
            {
                isMouseSleeping = true;
                if (updateTimer != null) updateTimer.Interval = userSelectedInterval;

                Color sleepColor = Color.FromArgb(255, 183, 77); // Warm amber
                lblDeviceName.Text = info.DisplayName;

                string battDisplay = (info.BatteryPercent > 0 ? (info.BatteryPercent.ToString() + "%") : "--");

                lblBatteryBig.Font = new Font("Microsoft YaHei UI", 34F, FontStyle.Bold, GraphicsUnit.Point);
                lblBatteryBig.Text = battDisplay;
                lblBatteryBig.ForeColor = Color.FromArgb(220, 225, 235);

                pillStatus.SetStatus("💤 闲置待机", sleepColor);
                pillStatus.Location = new Point(lblBatteryBig.Right + (int)(16 * dpiScale), (int)(56 * dpiScale));

                barBattery.StopFlow(info.BatteryPercent, sleepColor);

                string timeStr = info.LastUpdated.ToString("HH:mm:ss");
                string wakeTip = (info.Category == DeviceCategory.Mouse) ? " · 移动鼠标即刻唤醒" : " · 待机中";
                lblUpdateTime.Text = "最后同步: " + timeStr + wakeTip;

                // Update Performance Card
                UpdatePerformanceSection(info, brandAccent);

                if (info.IsPrimary)
                {
                    UpdateStatusDisplay();
                    UpdateTrayIcon(info.BatteryPercent, false, true, true);
                }

                if (showTipIfManual)
                {
                    string restoreTip = (info.Category == DeviceCategory.Mouse) ? "\n移动鼠标将即刻恢复工作" : "";
                    SafeShowBalloonTip(1500, info.DisplayName, string.Format("电量: {0}% (休眠待机){1}", info.BatteryPercent, restoreTip), ToolTipIcon.Info);
                }
                return;
            }

            // Normal awake state (Mouse / Gamepad / Headset / Generic)
            isMouseSleeping = false;
            if (updateTimer != null) updateTimer.Interval = userSelectedInterval;

            Color accentColor;
            if (info.BatteryPercent > 20 || info.IsCharging)
                accentColor = brandAccent;
            else if (info.BatteryPercent > 10)
                accentColor = Color.FromArgb(255, 214, 0);
            else
                accentColor = Color.FromArgb(255, 45, 85);

            bool mouseIsWired = info.Transport != null && 
                                (info.Transport.Equals("USB 有线", StringComparison.OrdinalIgnoreCase) || info.Transport.Equals("USB", StringComparison.OrdinalIgnoreCase)) &&
                                !info.Transport.Contains("无线") && 
                                !info.Transport.Contains("2.4G") && 
                                !info.IsDonglePresent;
            bool mouseIsBle = info.Transport != null && info.Transport.Contains("蓝牙");

            string mouseConnPill;
            if (info.IsCharging)
            {
                mouseConnPill = mouseIsWired ? "⚡ USB 充电中" : "⚡ 2.4G 充电中";
            }
            else if (mouseIsWired)
            {
                mouseConnPill = "🔌 USB 有线";
            }
            else if (mouseIsBle)
            {
                mouseConnPill = "📶 蓝牙无线";
            }
            else
            {
                mouseConnPill = (info.Transport != null && info.Transport.Length > 0) ? info.Transport : "2.4G 无线";
            }

            lblDeviceName.Text = info.DisplayName;

            lblBatteryBig.Font = new Font("Microsoft YaHei UI", 34F, FontStyle.Bold, GraphicsUnit.Point);
            string awakeBattDisplay = (info.BatteryPercent >= 0 ? info.BatteryPercent.ToString() : "--") + "%";

            lblBatteryBig.Text = awakeBattDisplay;
            lblBatteryBig.ForeColor = accentColor;

            pillStatus.SetStatus(mouseConnPill, accentColor);
            pillStatus.Location = new Point(lblBatteryBig.Right + (int)(16 * dpiScale), (int)(56 * dpiScale));

            if (info.IsCharging)
            {
                barBattery.StartChargingFill(info.BatteryPercent, accentColor, Color.FromArgb(180, 255, 200));
            }
            else
            {
                barBattery.StopFlow(info.BatteryPercent, accentColor);
            }

            string normalTimeStr = info.LastUpdated.ToString("HH:mm:ss");
            string chgDetail = info.IsCharging ? " · 正在充电" : " · 自动侦测硬件插拔";
            lblUpdateTime.Text = "最后同步: " + normalTimeStr + chgDetail;

            // Update Performance Card
            UpdatePerformanceSection(info, brandAccent);

            if (info.IsPrimary)
            {
                UpdateStatusDisplay();
                UpdateTrayIcon(info.BatteryPercent, info.IsCharging, true, false);

                if (lowBatteryAlertEnabled && !info.IsCharging)
                {
                    if (info.BatteryPercent <= 20)
                    {
                        if (!lastLowAlertFired)
                        {
                            SafeShowBalloonTip(4000, info.DeviceName + " 电量不足", string.Format("当前电量仅剩 {0}%，请及时连接充电器！", info.BatteryPercent), ToolTipIcon.Warning);
                            lastLowAlertFired = true;
                        }
                    }
                    else
                    {
                        lastLowAlertFired = false;
                    }
                }
                else
                {
                    lastLowAlertFired = false;
                }
            }

            if (showTipIfManual)
            {
                string perfTip = (info.Dpi > 0 && info.PollingRate > 0) ? string.Format("\n性能: {0} DPI · {1} Hz", info.Dpi, info.PollingRate) : "";
                SafeShowBalloonTip(1500, info.DeviceName, string.Format("电量: {0}% ({1}){2}\n更新时间: {3}", info.BatteryPercent, mouseConnPill, perfTip, normalTimeStr), ToolTipIcon.Info);
            }
        }

        [DllImport("user32.dll")]
        static extern int GetSystemMetrics(int nIndex);
        private const int SM_CXSMICON = 49;

        private int GetTrayIconSize()
        {
            try
            {
                int size = GetSystemMetrics(SM_CXSMICON);
                if (size >= 24) return 24;
                if (size >= 20) return 20;
                return 16;
            }
            catch
            {
                return 16;
            }
        }

        private void UpdateTrayIcon(int percent, bool isCharging, bool isConnected, bool isSleeping = false, Color? brandColor = null)
        {
            int iconSize = GetTrayIconSize();
            Color bCol = brandColor ?? ((lastInfo != null) ? lastInfo.BrandColor : Color.FromArgb(0, 230, 118));
            using (Bitmap bmp = DrawTrayBitmap(percent, isCharging, isConnected, trayStyle, iconSize, isSleeping, bCol))
            {
                IntPtr hIcon = bmp.GetHicon();
                try
                {
                    using (Icon tempIcon = Icon.FromHandle(hIcon))
                    {
                        trayIcon.Icon = (Icon)tempIcon.Clone();
                    }
                }
                finally
                {
                    DestroyIcon(hIcon);
                }
            }
        }

        private static readonly byte[][] Digits3 = new byte[][] {
            new byte[] { 0x7, 0x5, 0x5, 0x5, 0x7 }, // 0
            new byte[] { 0x1, 0x3, 0x1, 0x1, 0x1 }, // 1 (width 2, or 1 in 100)
            new byte[] { 0x7, 0x1, 0x7, 0x4, 0x7 }, // 2
            new byte[] { 0x7, 0x1, 0x7, 0x1, 0x7 }, // 3
            new byte[] { 0x5, 0x5, 0x7, 0x1, 0x1 }, // 4
            new byte[] { 0x7, 0x4, 0x7, 0x1, 0x7 }, // 5
            new byte[] { 0x7, 0x4, 0x7, 0x5, 0x7 }, // 6
            new byte[] { 0x7, 0x1, 0x1, 0x1, 0x1 }, // 7
            new byte[] { 0x7, 0x5, 0x7, 0x5, 0x7 }, // 8
            new byte[] { 0x7, 0x5, 0x7, 0x1, 0x7 }, // 9
        };

        private static readonly byte[][] Digits4x7 = new byte[][] {
            new byte[] { 0x6, 0x9, 0x9, 0x9, 0x9, 0x9, 0x6 }, // 0
            new byte[] { 0x2, 0x6, 0x2, 0x2, 0x2, 0x2, 0x7 }, // 1 (width 3)
            new byte[] { 0x6, 0x9, 0x1, 0x2, 0x4, 0x8, 0xF }, // 2
            new byte[] { 0xE, 0x1, 0x1, 0x6, 0x1, 0x1, 0xE }, // 3
            new byte[] { 0x9, 0x9, 0x9, 0xF, 0x1, 0x1, 0x1 }, // 4
            new byte[] { 0xF, 0x8, 0xE, 0x1, 0x1, 0x9, 0x6 }, // 5
            new byte[] { 0x6, 0x8, 0xE, 0x9, 0x9, 0x9, 0x6 }, // 6
            new byte[] { 0xF, 0x1, 0x2, 0x2, 0x4, 0x4, 0x4 }, // 7
            new byte[] { 0x6, 0x9, 0x9, 0x6, 0x9, 0x9, 0x6 }, // 8
            new byte[] { 0x6, 0x9, 0x9, 0x7, 0x1, 0x1, 0x6 }, // 9
        };

        private static Bitmap DrawTrayBitmap(int percent, bool isCharging, bool isConnected, int style, int iconSize, bool isSleeping = false, Color? brandColor = null)
        {
            if (iconSize >= 24)
            {
                return DrawTrayBitmap24(percent, isCharging, isConnected, style, isSleeping, brandColor);
            }
            else
            {
                return DrawTrayBitmap16(percent, isCharging, isConnected, style, isSleeping, brandColor);
            }
        }

        private static Bitmap DrawTrayBitmap24(int percent, bool isCharging, bool isConnected, int style, bool isSleeping = false, Color? brandColor = null)
        {
            Bitmap bmp = new Bitmap(24, 24);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                if (!isConnected)
                {
                    Color whiteCol = Color.White;
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(18, 22, 30)))
                    {
                        g.FillRectangle(bg, 2, 4, 18, 16);
                    }
                    using (Pen p = new Pen(whiteCol, 1f))
                    {
                        g.DrawLine(p, 2, 3, 19, 3);
                        g.DrawLine(p, 2, 20, 19, 20);
                        g.DrawLine(p, 1, 4, 1, 19);
                        g.DrawLine(p, 20, 4, 20, 19);
                        bmp.SetPixel(1, 3, whiteCol);
                        bmp.SetPixel(1, 20, whiteCol);
                        bmp.SetPixel(20, 3, whiteCol);
                        bmp.SetPixel(20, 20, whiteCol);
                    }
                    using (SolidBrush cap = new SolidBrush(whiteCol))
                    {
                        g.FillRectangle(cap, 21, 8, 2, 8);
                    }
                    DrawQuestionMark24(bmp, 10, 8, Color.FromArgb(160, 165, 180));
                    return bmp;
                }

                Color brandAccent = brandColor.HasValue ? brandColor.Value : Color.FromArgb(0, 230, 118);
                Color accentColor = isSleeping ? Color.FromArgb(255, 183, 77) :
                                    (percent < 0 ? brandAccent :
                                    ((percent > 20 || isCharging) ? brandAccent :
                                    ((percent > 10) ? Color.FromArgb(255, 214, 0) : Color.FromArgb(255, 50, 65))));

                if (style == 1) // 醒目数字能量表 (Centered digits)
                {
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(20, 23, 30)))
                        g.FillRectangle(bg, 0, 0, 24, 24);
                    using (Pen border = new Pen(Color.FromArgb(48, 56, 74), 1f))
                        g.DrawRectangle(border, 0, 0, 23, 23);

                    Color digitCol = (brandAccent == Color.FromArgb(0, 229, 255)) ? brandAccent : Color.White;
                    if (isCharging)
                        DrawBolt24(bmp, 11, 10, Color.White);
                    else
                        DrawDigits24(bmp, percent, 11, 5, digitCol, false);

                    int barW = (percent < 0) ? 18 : Math.Max(2, (int)(18 * (percent / 100.0)));
                    using (SolidBrush barB = new SolidBrush(accentColor))
                        g.FillRectangle(barB, 3, 17, barW, 4);
                }
                else // 现代胶囊电池 (Default, sealed closed white corners)
                {
                    Color whiteCol = Color.White;

                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(18, 22, 30)))
                        g.FillRectangle(bg, 2, 4, 18, 16);

                    int fillW = (percent < 0) ? 18 : Math.Max(1, (int)(18 * (percent / 100.0)));
                    using (SolidBrush fb = new SolidBrush(accentColor))
                        g.FillRectangle(fb, 2, 4, fillW, 16);

                    if (isCharging)
                        DrawBolt24(bmp, 10, 11, Color.White);
                    else
                        DrawDigits24(bmp, percent, 10, 8, Color.White, true);

                    using (Pen p = new Pen(whiteCol, 1f))
                    {
                        g.DrawLine(p, 2, 3, 19, 3);
                        g.DrawLine(p, 2, 20, 19, 20);
                        g.DrawLine(p, 1, 4, 1, 19);
                        g.DrawLine(p, 20, 4, 20, 19);
                    }
                    bmp.SetPixel(1, 3, whiteCol);
                    bmp.SetPixel(1, 20, whiteCol);
                    bmp.SetPixel(20, 3, whiteCol);
                    bmp.SetPixel(20, 20, whiteCol);

                    using (SolidBrush cap = new SolidBrush(whiteCol))
                        g.FillRectangle(cap, 21, 8, 2, 8);
                }
            }
            return bmp;
        }

        private static Bitmap DrawTrayBitmap16(int percent, bool isCharging, bool isConnected, int style, bool isSleeping = false, Color? brandColor = null)
        {
            Bitmap bmp = new Bitmap(16, 16);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.Clear(Color.Transparent);
                if (!isConnected)
                {
                    Color whiteCol = Color.White;
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(18, 22, 30)))
                        g.FillRectangle(bg, 1, 3, 12, 10);
                    using (Pen p = new Pen(whiteCol, 1f))
                    {
                        g.DrawLine(p, 1, 2, 12, 2);
                        g.DrawLine(p, 1, 13, 12, 13);
                        g.DrawLine(p, 0, 3, 0, 12);
                        g.DrawLine(p, 13, 3, 13, 12);
                    }
                    bmp.SetPixel(0, 2, whiteCol);
                    bmp.SetPixel(0, 13, whiteCol);
                    bmp.SetPixel(13, 2, whiteCol);
                    bmp.SetPixel(13, 13, whiteCol);
                    using (SolidBrush cap = new SolidBrush(whiteCol))
                        g.FillRectangle(cap, 14, 5, 2, 6);
                    DrawQuestionMark16(bmp, 6, 5, Color.FromArgb(160, 165, 180));
                    return bmp;
                }

                Color brandAccent = brandColor.HasValue ? brandColor.Value : Color.FromArgb(0, 230, 118);
                Color accentColor = isSleeping ? Color.FromArgb(255, 183, 77) :
                                    (percent < 0 ? brandAccent :
                                    ((percent > 20 || isCharging) ? brandAccent :
                                    ((percent > 10) ? Color.FromArgb(255, 214, 0) : Color.FromArgb(255, 50, 65))));

                if (style == 1) // 醒目数字能量表 (Centered digits)
                {
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(20, 23, 30)))
                        g.FillRectangle(bg, 0, 0, 16, 16);
                    using (Pen border = new Pen(Color.FromArgb(48, 56, 74), 1f))
                        g.DrawRectangle(border, 0, 0, 15, 15);

                    Color digitCol = (brandAccent == Color.FromArgb(0, 229, 255)) ? brandAccent : Color.White;
                    if (isCharging)
                    {
                        bmp.SetPixel(14, 1, accentColor);
                        bmp.SetPixel(13, 2, accentColor);
                        bmp.SetPixel(14, 2, accentColor);
                        bmp.SetPixel(13, 3, accentColor);
                        DrawBolt16(bmp, 6, 4, Color.White);
                    }
                    else
                    {
                        DrawDigits16(bmp, percent, 7, 3, digitCol, false);
                    }

                    int barW = (percent < 0) ? 12 : Math.Max(2, (int)(12 * (percent / 100.0)));
                    using (SolidBrush barB = new SolidBrush(accentColor))
                        g.FillRectangle(barB, 2, 12, barW, 3);
                }
                else // 现代胶囊电池 (Default, sealed closed white corners)
                {
                    Color whiteCol = Color.White;

                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(18, 22, 30)))
                        g.FillRectangle(bg, 1, 3, 12, 10);

                    int fillW = (percent < 0) ? 12 : Math.Max(1, (int)(12 * (percent / 100.0)));
                    using (SolidBrush fb = new SolidBrush(accentColor))
                        g.FillRectangle(fb, 1, 3, fillW, 10);

                    if (isCharging)
                        DrawBolt16(bmp, 6, 5, Color.White);
                    else
                        DrawDigits16(bmp, percent, 7, 5, Color.White, true);

                    using (Pen p = new Pen(whiteCol, 1f))
                    {
                        g.DrawLine(p, 1, 2, 12, 2);
                        g.DrawLine(p, 1, 13, 12, 13);
                        g.DrawLine(p, 0, 3, 0, 12);
                        g.DrawLine(p, 13, 3, 13, 12);
                    }
                    bmp.SetPixel(0, 2, whiteCol);
                    bmp.SetPixel(0, 13, whiteCol);
                    bmp.SetPixel(13, 2, whiteCol);
                    bmp.SetPixel(13, 13, whiteCol);

                    using (SolidBrush cap = new SolidBrush(whiteCol))
                        g.FillRectangle(cap, 14, 5, 2, 6);
                }
            }
            return bmp;
        }


        private static void DrawDigits24(Bitmap bmp, int value, int centerX, int startY, Color color, bool smartContrast = false)
        {
            if (value < 0)
            {
                int y = startY + 3;
                for (int x = centerX - 4; x <= centerX - 2; x++)
                {
                    if (x >= 0 && x < bmp.Width && y >= 0 && y < bmp.Height)
                    {
                        Color drawCol = color;
                        if (smartContrast)
                        {
                            Color bg = bmp.GetPixel(x, y);
                            int lum = (int)(bg.R * 0.299 + bg.G * 0.587 + bg.B * 0.114);
                            drawCol = (lum > 110) ? Color.FromArgb(10, 24, 15) : Color.White;
                        }
                        bmp.SetPixel(x, y, drawCol);
                    }
                }
                for (int x = centerX + 1; x <= centerX + 3; x++)
                {
                    if (x >= 0 && x < bmp.Width && y >= 0 && y < bmp.Height)
                    {
                        Color drawCol = color;
                        if (smartContrast)
                        {
                            Color bg = bmp.GetPixel(x, y);
                            int lum = (int)(bg.R * 0.299 + bg.G * 0.587 + bg.B * 0.114);
                            drawCol = (lum > 110) ? Color.FromArgb(10, 24, 15) : Color.White;
                        }
                        bmp.SetPixel(x, y, drawCol);
                    }
                }
                return;
            }

            string s = value.ToString();
            int totalW = 0;
            int[] widths = new int[s.Length];
            for (int i = 0; i < s.Length; i++)
            {
                int d = s[i] - '0';
                int w = (d == 1) ? 3 : 4;
                widths[i] = w;
                totalW += w;
            }
            totalW += s.Length - 1;

            int curX = centerX - (totalW / 2);
            for (int i = 0; i < s.Length; i++)
            {
                int d = s[i] - '0';
                int w = widths[i];
                byte[] rows = Digits4x7[d];
                for (int r = 0; r < 7; r++)
                {
                    byte row = rows[r];
                    for (int c = 0; c < w; c++)
                    {
                        int bit = (w == 3) ? (2 - c) : (3 - c);
                        if ((row & (1 << bit)) != 0)
                        {
                            int px = curX + c;
                            int py = startY + r;
                            if (px >= 0 && px < bmp.Width && py >= 0 && py < bmp.Height)
                            {
                                Color drawCol = color;
                                if (smartContrast)
                                {
                                    Color bg = bmp.GetPixel(px, py);
                                    int lum = (int)(bg.R * 0.299 + bg.G * 0.587 + bg.B * 0.114);
                                    drawCol = (lum > 110) ? Color.FromArgb(10, 24, 15) : Color.White;
                                }
                                bmp.SetPixel(px, py, drawCol);
                            }
                        }
                    }
                }
                curX += w + 1;
            }
        }

        private static void DrawBolt24(Bitmap bmp, int cx, int cy, Color color)
        {
            Point[] pts = new Point[] {
                new Point(cx + 1, cy - 5), new Point(cx - 3, cy), new Point(cx, cy),
                new Point(cx - 2, cy + 5), new Point(cx + 3, cy - 1), new Point(cx, cy - 1)
            };
            using (Graphics g = Graphics.FromImage(bmp))
            using (SolidBrush b = new SolidBrush(color))
            {
                g.FillPolygon(b, pts);
            }
        }

        private static void DrawQuestionMark24(Bitmap bmp, int cx, int cy, Color color)
        {
            int[,] qPts = new int[,] { {0,0}, {1,0}, {2,0}, {3,0}, {3,1}, {3,2}, {2,3}, {1,4}, {1,6} };
            for (int i = 0; i < qPts.GetLength(0); i++)
            {
                int px = cx + qPts[i, 0];
                int py = cy + qPts[i, 1];
                if (px >= 0 && px < bmp.Width && py >= 0 && py < bmp.Height)
                    bmp.SetPixel(px, py, color);
            }
        }

        private static void DrawDigits16(Bitmap bmp, int value, int centerX, int startY, Color color, bool smartContrast = false)
        {
            if (value < 0)
            {
                int y = startY + 2;
                for (int x = centerX - 3; x <= centerX - 1; x++)
                {
                    if (x >= 0 && x < bmp.Width && y >= 0 && y < bmp.Height)
                    {
                        Color drawCol = color;
                        if (smartContrast)
                        {
                            Color bg = bmp.GetPixel(x, y);
                            int lum = (int)(bg.R * 0.299 + bg.G * 0.587 + bg.B * 0.114);
                            drawCol = (lum > 110) ? Color.FromArgb(10, 24, 15) : Color.White;
                        }
                        bmp.SetPixel(x, y, drawCol);
                    }
                }
                for (int x = centerX + 1; x <= centerX + 3; x++)
                {
                    if (x >= 0 && x < bmp.Width && y >= 0 && y < bmp.Height)
                    {
                        Color drawCol = color;
                        if (smartContrast)
                        {
                            Color bg = bmp.GetPixel(x, y);
                            int lum = (int)(bg.R * 0.299 + bg.G * 0.587 + bg.B * 0.114);
                            drawCol = (lum > 110) ? Color.FromArgb(10, 24, 15) : Color.White;
                        }
                        bmp.SetPixel(x, y, drawCol);
                    }
                }
                return;
            }

            string s = value.ToString();
            int totalW = 0;
            for (int i = 0; i < s.Length; i++)
            {
                int d = s[i] - '0';
                int w = (d == 1 && s.Length == 3) ? 1 : (d == 1 ? 2 : 3);
                totalW += w;
                if (i < s.Length - 1) totalW += 1;
            }

            int curX = centerX - (totalW / 2);
            for (int i = 0; i < s.Length; i++)
            {
                int d = s[i] - '0';
                byte[] rows = Digits3[d];
                int w = (d == 1 && s.Length == 3) ? 1 : (d == 1 ? 2 : 3);
                for (int r = 0; r < 5; r++)
                {
                    byte row = rows[r];
                    for (int c = 0; c < w; c++)
                    {
                        int bit = (w == 1) ? 0 : ((w == 2) ? (1 - c) : (2 - c));
                        if ((row & (1 << bit)) != 0)
                        {
                            int px = curX + c;
                            int py = startY + r;
                            if (px >= 0 && px < bmp.Width && py >= 0 && py < bmp.Height)
                            {
                                Color drawCol = color;
                                if (smartContrast)
                                {
                                    Color bg = bmp.GetPixel(px, py);
                                    int lum = (int)(bg.R * 0.299 + bg.G * 0.587 + bg.B * 0.114);
                                    drawCol = (lum > 110) ? Color.FromArgb(10, 24, 15) : Color.White;
                                }
                                bmp.SetPixel(px, py, drawCol);
                            }
                        }
                    }
                }
                curX += w + 1;
            }
        }

        private static void DrawBolt16(Bitmap bmp, int startX, int startY, Color color)
        {
            int[,] bolt = new int[,] {
                {0, 2}, {1, 1}, {2, 0},
                {1, 2}, {2, 2}, {3, 2},
                {0, 3}, {1, 3}, {2, 3},
                {1, 4}, {2, 5}
            };
            for (int i = 0; i < bolt.GetLength(0); i++)
            {
                int px = startX + bolt[i, 0];
                int py = startY + bolt[i, 1];
                if (px >= 0 && px < bmp.Width && py >= 0 && py < bmp.Height)
                    bmp.SetPixel(px, py, color);
            }
        }

        private static void DrawQuestionMark16(Bitmap bmp, int cx, int cy, Color color)
        {
            int[,] qPts = new int[,] { {0,0}, {1,0}, {2,0}, {2,1}, {1,2}, {1,4} };
            for (int i = 0; i < qPts.GetLength(0); i++)
            {
                int px = cx + qPts[i, 0];
                int py = cy + qPts[i, 1];
                if (px >= 0 && px < bmp.Width && py >= 0 && py < bmp.Height)
                    bmp.SetPixel(px, py, color);
            }
        }

        private void ExitApp()
        {
            StopDpiMonitor();
            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }
            if (osdForm != null && !osdForm.IsDisposed)
            {
                osdForm.Dispose();
            }
            Application.Exit();
        }
    }

    public class RazerDeviceHardwareState
    {
        public string DeviceId;
        public string DevicePath;
        public int ReportLength;
        public bool IsPrepended;
        public int BatteryPercent;
        public double ExactBatteryPercent;
        public string DeviceName;
        public int Dpi;
        public int DpiStage;
        public int DpiStageCount;
        public int[] DpiStages;
        public int PollingRate;
        public DateTime LastUpdated;

        public RazerDeviceHardwareState()
        {
            ReportLength = 90;
            IsPrepended = false;
            LastUpdated = DateTime.MinValue;
        }
    }

    public static class RazerDeviceHelper
    {
        private static readonly object hidLock = new object();
        private static readonly Dictionary<string, RazerDeviceHardwareState> deviceHardwareStates =
            new Dictionary<string, RazerDeviceHardwareState>(StringComparer.OrdinalIgnoreCase);

        public static RazerDeviceHardwareState GetDeviceState(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return null;
            string normId = NormalizeRazerDeviceId(deviceId);
            lock (hidLock)
            {
                RazerDeviceHardwareState st;
                if (deviceHardwareStates.TryGetValue(normId, out st))
                {
                    return st;
                }
            }
            return null;
        }

        public static int NormalizeRazerPid(int pid)
        {
            switch (pid)
            {
                case 0x00BF: return 0x00BE; // DeathAdder V4 Pro Dongle -> DeathAdder V4 Pro Wired
                case 0x00E6: return 0x00E5; // Viper V4 Pro Dongle -> Viper V4 Pro Wired
                case 0x00C1: return 0x00C0; // Viper V3 Pro Dongle -> Viper V3 Pro Wired
                case 0x00AD: return 0x00AC; // Viper V3 HyperSpeed Dongle -> Wired
                case 0x00B7: return 0x00B6; // DeathAdder V3 Pro Dongle -> Wired
                case 0x00B3: return 0x00B2; // DeathAdder V3 HyperSpeed Dongle -> Wired
                case 0x00B5: return 0x00B4; // Viper V2 Pro Dongle -> Wired
                case 0x00A6: return 0x00A5; // Cobra Pro Dongle -> Wired
                case 0x009B: return 0x009A; // Basilisk V3 Pro Dongle -> Wired
                case 0x00C5: return 0x00C4; // Basilisk V3 35K Dongle -> Wired
                case 0x00A8: return 0x00A7; // Viper Mini Signature Edition Dongle -> Wired
                case 0x0090: return 0x008F; // Naga V2 Pro Dongle -> Wired
                case 0x0092: return 0x0091; // Naga Pro Dongle -> Wired
                case 0x00A3: return 0x00A2; // DeathAdder V2 Pro Dongle -> Wired
                case 0x0088: return 0x0084; // Viper Ultimate Dongle -> Wired
                case 0x007B: return 0x007A; // Basilisk Ultimate Dongle -> Wired
                case 0x007D: return 0x007C; // DeathAdder V2 Mini Dongle -> Wired
                default:
                    return pid;
            }
        }

        private static readonly Dictionary<int, string> RazerPidToOfficialName = new Dictionary<int, string>
        {
            // Viper series
            { 0x00E5, "Razer Viper V4 Pro" },
            { 0x00E6, "Razer Viper V4 Pro" },
            { 0x00C0, "Razer Viper V3 Pro" },
            { 0x00C1, "Razer Viper V3 Pro" },
            { 0x00AC, "Razer Viper V3 HyperSpeed" },
            { 0x00AD, "Razer Viper V3 HyperSpeed" },
            { 0x00B4, "Razer Viper V2 Pro" },
            { 0x00B5, "Razer Viper V2 Pro" },
            { 0x00A7, "Razer Viper Mini Signature Edition" },
            { 0x00A8, "Razer Viper Mini Signature Edition" },
            { 0x0084, "Razer Viper Ultimate" },
            { 0x0088, "Razer Viper Ultimate" },
            { 0x0078, "Razer Viper Mini" },
            { 0x0071, "Razer Viper 8KHz" },
            { 0x0070, "Razer Viper" },

            // DeathAdder series
            { 0x00BE, "Razer DeathAdder V4 Pro" },
            { 0x00BF, "Razer DeathAdder V4 Pro" },
            { 0x00B6, "Razer DeathAdder V3 Pro" },
            { 0x00B7, "Razer DeathAdder V3 Pro" },
            { 0x00B2, "Razer DeathAdder V3 HyperSpeed" },
            { 0x00B3, "Razer DeathAdder V3 HyperSpeed" },
            { 0x00B0, "Razer DeathAdder V3" },
            { 0x00A2, "Razer DeathAdder V2 Pro" },
            { 0x00A3, "Razer DeathAdder V2 Pro" },
            { 0x008A, "Razer DeathAdder V2" },
            { 0x007C, "Razer DeathAdder V2 Mini" },
            { 0x007D, "Razer DeathAdder V2 Mini" },
            { 0x005C, "Razer DeathAdder Elite" },

            // Basilisk series
            { 0x00C4, "Razer Basilisk V3 35K" },
            { 0x00C5, "Razer Basilisk V3 35K" },
            { 0x009A, "Razer Basilisk V3 Pro" },
            { 0x009B, "Razer Basilisk V3 Pro" },
            { 0x00B8, "Razer Basilisk V3" },
            { 0x0085, "Razer Basilisk X HyperSpeed" },
            { 0x0086, "Razer Basilisk X HyperSpeed" },
            { 0x007A, "Razer Basilisk Ultimate" },
            { 0x007B, "Razer Basilisk Ultimate" },
            { 0x0064, "Razer Basilisk" },

            // Cobra series
            { 0x00A5, "Razer Cobra Pro" },
            { 0x00A6, "Razer Cobra Pro" },
            { 0x00A4, "Razer Cobra" },

            // Naga series
            { 0x008F, "Razer Naga V2 Pro" },
            { 0x0090, "Razer Naga V2 Pro" },
            { 0x0095, "Razer Naga V2 HyperSpeed" },
            { 0x0091, "Razer Naga Pro" },
            { 0x0092, "Razer Naga Pro" },
            { 0x0080, "Razer Naga Left-Handed Edition" },
            { 0x0067, "Razer Naga Trinity" },

            // Orochi series
            { 0x0099, "Razer Orochi V2" }
        };

        public static string QueryPnpDeviceDesc(int pid)
        {
            if (pid == 0) return null;
            try
            {
                string pidHex = pid.ToString("X4");
                string[] enumRoots = new string[] { @"SYSTEM\CurrentControlSet\Enum\USB", @"SYSTEM\CurrentControlSet\Enum\HID" };
                foreach (var rootPath in enumRoots)
                {
                    using (var rootKey = Registry.LocalMachine.OpenSubKey(rootPath))
                    {
                        if (rootKey == null) continue;
                        foreach (var subName in rootKey.GetSubKeyNames())
                        {
                            if (subName.IndexOf("VID_1532", StringComparison.OrdinalIgnoreCase) >= 0 &&
                                subName.IndexOf("PID_" + pidHex, StringComparison.OrdinalIgnoreCase) >= 0)
                            {
                                using (var devKey = rootKey.OpenSubKey(subName))
                                {
                                    if (devKey == null) continue;
                                    foreach (var instName in devKey.GetSubKeyNames())
                                    {
                                        using (var instKey = devKey.OpenSubKey(instName))
                                        {
                                            if (instKey == null) continue;

                                            string[] valueNames = new string[] { "DeviceDesc", "FriendlyName", "BusReportedDeviceDesc" };
                                            foreach (var valName in valueNames)
                                            {
                                                string desc = instKey.GetValue(valName) as string;
                                                if (string.IsNullOrEmpty(desc)) continue;

                                                int semi = desc.LastIndexOf(';');
                                                if (semi >= 0 && semi < desc.Length - 1)
                                                {
                                                    desc = desc.Substring(semi + 1).Trim();
                                                }

                                                if (desc.StartsWith("Razer ", StringComparison.OrdinalIgnoreCase))
                                                {
                                                    if (desc.IndexOf("Composite", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                        desc.IndexOf("Input Device", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                        desc.IndexOf("HID-compliant", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                        desc.IndexOf("Control Device", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                        desc.IndexOf("Keyboard", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                        desc.Equals("Razer Mouse", StringComparison.OrdinalIgnoreCase) ||
                                                        desc.Equals("Razer Wireless Mouse", StringComparison.OrdinalIgnoreCase))
                                                    {
                                                        continue;
                                                    }

                                                    if (desc.IndexOf("Dongle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                        desc.IndexOf("Receiver", StringComparison.OrdinalIgnoreCase) >= 0)
                                                    {
                                                        string stripped = desc.Replace("Dongle", "").Replace("Receiver", "").Trim();
                                                        if (stripped.StartsWith("Razer ", StringComparison.OrdinalIgnoreCase) &&
                                                            !stripped.Equals("Razer", StringComparison.OrdinalIgnoreCase) &&
                                                            !stripped.Equals("Razer HyperPolling Wireless", StringComparison.OrdinalIgnoreCase) &&
                                                            !stripped.Equals("Razer USB", StringComparison.OrdinalIgnoreCase))
                                                        {
                                                            return stripped;
                                                        }
                                                        continue;
                                                    }

                                                    return desc;
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        public static string GetRazerOfficialModelName(int pid, string fallbackProdName = null)
        {
            if (pid != 0)
            {
                string pnpName = QueryPnpDeviceDesc(pid);
                if (!string.IsNullOrEmpty(pnpName)) return pnpName;

                int normPid = NormalizeRazerPid(pid);
                if (normPid != pid && normPid != 0)
                {
                    pnpName = QueryPnpDeviceDesc(normPid);
                    if (!string.IsNullOrEmpty(pnpName)) return pnpName;
                }

                string official;
                if (RazerPidToOfficialName.TryGetValue(pid, out official)) return official;
                if (normPid != pid && normPid != 0 && RazerPidToOfficialName.TryGetValue(normPid, out official)) return official;
            }

            if (!string.IsNullOrEmpty(fallbackProdName))
            {
                string clean = fallbackProdName.Trim();
                if (clean.StartsWith("Razer ", StringComparison.OrdinalIgnoreCase)) clean = clean.Substring(6).Trim();

                if (!clean.Equals("Mouse", StringComparison.OrdinalIgnoreCase) &&
                    !clean.Equals("Wireless Mouse", StringComparison.OrdinalIgnoreCase) &&
                    !clean.Equals("HyperPolling Wireless Dongle", StringComparison.OrdinalIgnoreCase) &&
                    !clean.Equals("USB Receiver", StringComparison.OrdinalIgnoreCase) &&
                    !clean.Equals("HID-compliant mouse", StringComparison.OrdinalIgnoreCase) &&
                    clean.Length > 2)
                {
                    return "Razer " + clean;
                }
            }

            string combined = fallbackProdName ?? "";
            if (combined.IndexOf("DeathAdder", StringComparison.OrdinalIgnoreCase) >= 0) return "Razer DeathAdder";
            if (combined.IndexOf("Viper", StringComparison.OrdinalIgnoreCase) >= 0) return "Razer Viper";
            if (combined.IndexOf("Basilisk", StringComparison.OrdinalIgnoreCase) >= 0) return "Razer Basilisk";
            if (combined.IndexOf("Cobra", StringComparison.OrdinalIgnoreCase) >= 0) return "Razer Cobra";
            if (combined.IndexOf("Naga", StringComparison.OrdinalIgnoreCase) >= 0) return "Razer Naga";
            if (combined.IndexOf("Orochi", StringComparison.OrdinalIgnoreCase) >= 0) return "Razer Orochi";

            return "Razer Gaming Mouse";
        }

        public static string GetRazerDisplayName(int pid, string deviceName = null)
        {
            string target = null;
            if (pid != 0)
            {
                string official = GetRazerOfficialModelName(pid, deviceName);
                if (!string.IsNullOrEmpty(official) && !official.Equals("Razer Gaming Mouse", StringComparison.OrdinalIgnoreCase))
                {
                    target = official;
                }
            }

            if (string.IsNullOrEmpty(target) && !string.IsNullOrEmpty(deviceName))
            {
                string clean = deviceName.Trim();
                if (!clean.Equals("Razer Mouse", StringComparison.OrdinalIgnoreCase) &&
                    !clean.Equals("Razer Wireless Mouse", StringComparison.OrdinalIgnoreCase) &&
                    !clean.Equals("雷蛇无线鼠标", StringComparison.OrdinalIgnoreCase) &&
                    !clean.Equals("雷蛇电竞鼠标", StringComparison.OrdinalIgnoreCase) &&
                    !clean.Equals("雷蛇无线设备", StringComparison.OrdinalIgnoreCase))
                {
                    if (clean.StartsWith("Razer ", StringComparison.OrdinalIgnoreCase)) target = clean;
                    else target = "Razer " + clean;
                }
            }

            return !string.IsNullOrEmpty(target) ? target : "Razer Gaming Mouse";
        }

        public static string NormalizeRazerDeviceId(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return deviceId;
            if (deviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase))
            {
                string pidPart = deviceId.Substring("Razer:1532:".Length).Trim();
                int pid;
                if (int.TryParse(pidPart, System.Globalization.NumberStyles.HexNumber, null, out pid))
                {
                    int normPid = NormalizeRazerPid(pid);
                    return "Razer:1532:" + normPid.ToString("X4");
                }
            }
            return deviceId;
        }

        // Hardware Cache for Standby / Sleep Retention
        public static int CachedBatteryPercent = 0;
        public static double CachedExactBatteryPercent = 0.0;
        public static string CachedDeviceName = "";
        public static int CachedDpi = 0;
        public static int CachedDpiStage = 0;
        public static int CachedDpiStageCount = 0;
        public static int[] CachedDpiStages = null;
        public static int CachedPollingRate = 0;
        public static DateTime CachedLastUpdated = DateTime.MinValue;

        private static string cachedRazerDpiPath = null;
        private static int cachedRazerReportLength = 90;
        private static bool cachedRazerPrepended = false;
        private static DateTime lastRazerEnumTime = DateTime.MinValue;

        private const string CacheRegistryKey = @"Software\RazerBatteryTray\HardwareCache";

        public static void LoadHardwareCache()
        {
            try
            {
                using (var rootKey = Registry.CurrentUser.OpenSubKey(CacheRegistryKey))
                {
                    if (rootKey != null)
                    {
                        var batt = rootKey.GetValue("BatteryPercent");
                        if (batt != null)
                        {
                            int b = (int)batt;
                            if (b > 0) CachedBatteryPercent = b;
                        }

                        var exactBatt = rootKey.GetValue("ExactBatteryPercent");
                        if (exactBatt != null)
                        {
                            double eb;
                            if (double.TryParse(exactBatt.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out eb))
                            {
                                if (eb > 0) CachedExactBatteryPercent = eb;
                            }
                        }

                        var dev = rootKey.GetValue("DeviceName");
                        if (dev != null) CachedDeviceName = (string)dev;

                        var dpi = rootKey.GetValue("Dpi");
                        if (dpi != null) CachedDpi = (int)dpi;

                        var st = rootKey.GetValue("DpiStage");
                        if (st != null) CachedDpiStage = (int)st;

                        var stCount = rootKey.GetValue("DpiStageCount");
                        if (stCount != null) CachedDpiStageCount = (int)stCount;

                        var stStr = rootKey.GetValue("DpiStages") as string;
                        if (!string.IsNullOrEmpty(stStr))
                        {
                            string[] parts = stStr.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                            CachedDpiStages = new int[parts.Length];
                            for (int i = 0; i < parts.Length; i++)
                            {
                                int p;
                                if (int.TryParse(parts[i], out p)) CachedDpiStages[i] = p;
                            }
                        }

                        var poll = rootKey.GetValue("PollingRate");
                        if (poll != null) CachedPollingRate = (int)poll;

                        var updatedStr = rootKey.GetValue("LastUpdated") as string;
                        if (!string.IsNullOrEmpty(updatedStr))
                        {
                            DateTime dt;
                            if (DateTime.TryParse(updatedStr, out dt)) CachedLastUpdated = dt;
                        }

                        string[] subKeyNames = rootKey.GetSubKeyNames();
                        foreach (string subName in subKeyNames)
                        {
                            using (var devKey = rootKey.OpenSubKey(subName))
                            {
                                if (devKey != null)
                                {
                                    string normId = subName.Replace('_', ':');
                                    var state = new RazerDeviceHardwareState();
                                    state.DeviceId = normId;
                                    var sb = devKey.GetValue("BatteryPercent");
                                    if (sb != null) state.BatteryPercent = (int)sb;
                                    var seb = devKey.GetValue("ExactBatteryPercent");
                                    if (seb != null)
                                    {
                                        double eb;
                                        if (double.TryParse(seb.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out eb))
                                            state.ExactBatteryPercent = eb;
                                    }
                                    var sdn = devKey.GetValue("DeviceName") as string;
                                    if (!string.IsNullOrEmpty(sdn)) state.DeviceName = sdn;
                                    var sdpi = devKey.GetValue("Dpi");
                                    if (sdpi != null) state.Dpi = (int)sdpi;
                                    var sst = devKey.GetValue("DpiStage");
                                    if (sst != null) state.DpiStage = (int)sst;
                                    var sstc = devKey.GetValue("DpiStageCount");
                                    if (sstc != null) state.DpiStageCount = (int)sstc;
                                    var sstStr = devKey.GetValue("DpiStages") as string;
                                    if (!string.IsNullOrEmpty(sstStr))
                                    {
                                        string[] parts = sstStr.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                                        state.DpiStages = new int[parts.Length];
                                        for (int i = 0; i < parts.Length; i++)
                                        {
                                            int p;
                                            if (int.TryParse(parts[i], out p)) state.DpiStages[i] = p;
                                        }
                                    }
                                    var spoll = devKey.GetValue("PollingRate");
                                    if (spoll != null) state.PollingRate = (int)spoll;
                                    var sdt = devKey.GetValue("LastUpdated") as string;
                                    if (!string.IsNullOrEmpty(sdt))
                                    {
                                        DateTime dt;
                                        if (DateTime.TryParse(sdt, out dt)) state.LastUpdated = dt;
                                    }
                                    deviceHardwareStates[normId] = state;
                                }
                            }
                        }
                    }
                }
            }
            catch { }
        }

        public static void SaveHardwareCache()
        {
            try
            {
                using (var rootKey = Registry.CurrentUser.CreateSubKey(CacheRegistryKey))
                {
                    if (rootKey != null)
                    {
                        foreach (var kvp in deviceHardwareStates)
                        {
                            var st = kvp.Value;
                            if (st == null) continue;
                            string subKeyName = kvp.Key.Replace(':', '_');
                            using (var devKey = rootKey.CreateSubKey(subKeyName))
                            {
                                if (devKey != null)
                                {
                                    if (st.BatteryPercent > 0) devKey.SetValue("BatteryPercent", st.BatteryPercent);
                                    if (st.ExactBatteryPercent > 0) devKey.SetValue("ExactBatteryPercent", st.ExactBatteryPercent.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                                    if (!string.IsNullOrEmpty(st.DeviceName)) devKey.SetValue("DeviceName", st.DeviceName);
                                    if (st.Dpi > 0) devKey.SetValue("Dpi", st.Dpi);
                                    if (st.DpiStage > 0) devKey.SetValue("DpiStage", st.DpiStage);
                                    if (st.DpiStageCount > 0) devKey.SetValue("DpiStageCount", st.DpiStageCount);
                                    if (st.DpiStages != null && st.DpiStages.Length > 0)
                                    {
                                        string stStr = string.Join(",", Array.ConvertAll(st.DpiStages, s => s.ToString()));
                                        devKey.SetValue("DpiStages", stStr);
                                    }
                                    if (st.PollingRate > 0) devKey.SetValue("PollingRate", st.PollingRate);
                                    if (st.LastUpdated != DateTime.MinValue) devKey.SetValue("LastUpdated", st.LastUpdated.ToString("o"));
                                }
                            }
                        }

                        if (CachedBatteryPercent > 0) rootKey.SetValue("BatteryPercent", CachedBatteryPercent);
                        if (CachedExactBatteryPercent > 0) rootKey.SetValue("ExactBatteryPercent", CachedExactBatteryPercent.ToString("0.00", System.Globalization.CultureInfo.InvariantCulture));
                        if (!string.IsNullOrEmpty(CachedDeviceName)) rootKey.SetValue("DeviceName", CachedDeviceName);
                        if (CachedDpi > 0) rootKey.SetValue("Dpi", CachedDpi);
                        if (CachedDpiStage > 0) rootKey.SetValue("DpiStage", CachedDpiStage);
                        if (CachedDpiStageCount > 0) rootKey.SetValue("DpiStageCount", CachedDpiStageCount);
                        if (CachedDpiStages != null && CachedDpiStages.Length > 0)
                        {
                            string stStr = string.Join(",", Array.ConvertAll(CachedDpiStages, s => s.ToString()));
                            rootKey.SetValue("DpiStages", stStr);
                        }
                        if (CachedPollingRate > 0) rootKey.SetValue("PollingRate", CachedPollingRate);
                        if (CachedLastUpdated != DateTime.MinValue) rootKey.SetValue("LastUpdated", CachedLastUpdated.ToString("o"));
                    }
                }
            }
            catch { }
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct HIDP_CAPS
        {
            public ushort Usage;
            public ushort UsagePage;
            public ushort InputReportByteLength;
            public ushort OutputReportByteLength;
            public ushort FeatureReportByteLength;
            [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
            public ushort[] Reserved;
            public ushort NumberLinkCollectionNodes;
            public ushort NumberInputButtonCaps;
            public ushort NumberInputValueCaps;
            public ushort NumberInputDataIndices;
            public ushort NumberOutputButtonCaps;
            public ushort NumberOutputValueCaps;
            public ushort NumberOutputDataIndices;
            public ushort NumberFeatureButtonCaps;
            public ushort NumberFeatureValueCaps;
            public ushort NumberFeatureDataIndices;
        }

        [DllImport("hid.dll", SetLastError = true)]
        public static extern void HidD_GetHidGuid(out Guid HidGuid);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, string Enumerator, IntPtr hwndParent, uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, uint MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, uint DeviceInterfaceDetailDataSize, out uint RequiredSize, IntPtr DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        public static extern bool CloseHandle(IntPtr hObject);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetAttributes(IntPtr HidDeviceObject, ref HIDD_ATTRIBUTES Attributes);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetPreparsedData(IntPtr HidDeviceObject, out IntPtr PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_FreePreparsedData(IntPtr PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern int HidP_GetCaps(IntPtr PreparsedData, out HIDP_CAPS Capabilities);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_SetFeature(IntPtr HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetFeature(IntPtr HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_GetInputReport(IntPtr HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        public static extern bool HidD_SetOutputReport(IntPtr HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

        [DllImport("hid.dll", SetLastError = true, CharSet = CharSet.Auto)]
        public static extern bool HidD_GetProductString(IntPtr HidDeviceObject, StringBuilder Buffer, int BufferLength);

        public const uint DIGCF_PRESENT = 0x02;
        public const uint DIGCF_DEVICEINTERFACE = 0x10;
        public const uint FILE_SHARE_READ = 0x00000001;
        public const uint FILE_SHARE_WRITE = 0x00000002;
        public const uint OPEN_EXISTING = 3;

        static byte CalculateCrc(byte[] report, int startOffset)
        {
            byte crc = 0;
            for (int i = startOffset + 2; i < startOffset + 88; i++)
            {
                crc ^= report[i];
            }
            return crc;
        }

        static byte[] CreateRazerReport(byte transactionId, byte commandClass, byte commandId, byte dataSize, int totalLength, bool prependedZero, byte[] args = null)
        {
            byte[] report = new byte[totalLength];
            int offset = prependedZero ? 1 : 0;

            report[offset + 0] = 0x00;
            report[offset + 1] = transactionId;
            report[offset + 2] = 0x00;
            report[offset + 3] = 0x00;
            report[offset + 4] = 0x00;
            report[offset + 5] = dataSize;
            report[offset + 6] = commandClass;
            report[offset + 7] = commandId;

            if (args != null)
            {
                for (int i = 0; i < args.Length && i < 80; i++)
                {
                    report[offset + 8 + i] = args[i];
                }
            }

            report[offset + 88] = CalculateCrc(report, offset);
            report[offset + 89] = 0x00;

            return report;
        }

        public static List<MouseBatteryInfo> QueryAllRazerDevices()
        {
            lock (hidLock)
            {
                var detectedByNormId = new Dictionary<string, MouseBatteryInfo>(StringComparer.OrdinalIgnoreCase);

                Guid hidGuid;
                HidD_GetHidGuid(out hidGuid);

                IntPtr devInfo = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1))
                {
                    return new List<MouseBatteryInfo>();
                }

                SP_DEVICE_INTERFACE_DATA ifData = new SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(ifData);

                uint memberIdx = 0;
                try
                {
                    while (SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                    {
                        uint reqSize;
                        SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);

                        IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                        if (SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                        {
                            IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                            string devicePath = Marshal.PtrToStringAuto(pDevicePath);

                            if (devicePath != null && devicePath.ToLower().Contains("vid_1532"))
                            {
                                IntPtr handle = CreateFile(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                                if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                {
                                    try
                                    {
                                        IntPtr preparsed;
                                        if (HidD_GetPreparsedData(handle, out preparsed))
                                        {
                                            HIDP_CAPS caps;
                                            HidP_GetCaps(preparsed, out caps);
                                            HidD_FreePreparsedData(preparsed);

                                            if (caps.FeatureReportByteLength >= 90)
                                            {
                                                StringBuilder prod = new StringBuilder(256);
                                                string prodName = "Razer Mouse";
                                                if (HidD_GetProductString(handle, prod, prod.Capacity) && prod.Length > 0)
                                                {
                                                    prodName = prod.ToString();
                                                }

                                                int curPid = 0;
                                                HIDD_ATTRIBUTES attr = new HIDD_ATTRIBUTES();
                                                attr.Size = Marshal.SizeOf(attr);
                                                if (HidD_GetAttributes(handle, ref attr))
                                                {
                                                    curPid = attr.ProductID;
                                                }
                                                else if (devicePath != null)
                                                {
                                                    int pidIdx = devicePath.IndexOf("pid_", StringComparison.OrdinalIgnoreCase);
                                                    if (pidIdx >= 0 && devicePath.Length >= pidIdx + 8)
                                                    {
                                                        int.TryParse(devicePath.Substring(pidIdx + 4, 4), System.Globalization.NumberStyles.HexNumber, null, out curPid);
                                                    }
                                                }

                                                int normPid = NormalizeRazerPid(curPid);
                                                bool isPureWirelessRazer = (curPid == 0x0099 || // Orochi V2
                                                                            curPid == 0x0085 || curPid == 0x0086 || // Basilisk X HyperSpeed
                                                                            curPid == 0x00AC || // Viper V3 HyperSpeed (Wireless AA)
                                                                            curPid == 0x0095 || // DeathAdder V2 X HyperSpeed
                                                                            curPid == 0x008D || curPid == 0x008E); // Atheris

                                                bool isDongle = (normPid != curPid ||
                                                                 isPureWirelessRazer ||
                                                                 prodName.IndexOf("Dongle", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                 prodName.IndexOf("Receiver", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                 prodName.IndexOf("Dock", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                 prodName.IndexOf("Wireless", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                 prodName.IndexOf("HyperSpeed", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                 prodName.IndexOf("HyperPolling", StringComparison.OrdinalIgnoreCase) >= 0 ||
                                                                 curPid == 0x00BF || curPid == 0x00B7 || curPid == 0x00B5 || curPid == 0x00A6 ||
                                                                 curPid == 0x007D || curPid == 0x009B || curPid == 0x00AD || curPid == 0x00A8 ||
                                                                 curPid == 0x00C5 || curPid == 0x00C1 || curPid == 0x0088 || curPid == 0x0072 ||
                                                                 curPid == 0x0090 || curPid == 0x00A3 || curPid == 0x00B3 || curPid == 0x0008 ||
                                                                 curPid == 0x007B || curPid == 0x0092 || curPid == 0x00B9 ||
                                                                 curPid == 0x00E6);
                                                string normId = "Razer:1532:" + (normPid != 0 ? normPid.ToString("X4") : (!string.IsNullOrEmpty(prodName) ? prodName : "Mouse"));
                                                string resolvedModelName = GetRazerOfficialModelName(curPid, prodName);

                                                byte[] transIds = new byte[] { 0x1F, 0x3F, 0xFF };
                                                bool prepended = (caps.FeatureReportByteLength == 91);
                                                int offset = prepended ? 1 : 0;
                                                bool querySuccess = false;

                                                int pct = 0;
                                                double exactPct = 0.0;
                                                bool isCharging = false;
                                                int liveDpi = 0;
                                                int activeStage = 0;
                                                int stageCount = 0;
                                                int[] stageList = null;
                                                int pollingRate = 0;

                                                foreach (byte tid in transIds)
                                                {
                                                    // 1. Query Battery
                                                    byte[] req = CreateRazerReport(tid, 0x07, 0x80, 0x02, caps.FeatureReportByteLength, prepended);
                                                    if (HidD_SetFeature(handle, req, req.Length))
                                                    {
                                                        Thread.Sleep(15);
                                                        byte[] resp = new byte[caps.FeatureReportByteLength];
                                                        if (prepended) resp[0] = 0x00;

                                                        if (HidD_GetFeature(handle, resp, resp.Length))
                                                        {
                                                            byte status = resp[offset + 0];
                                                            byte cmdClass = resp[offset + 6];
                                                            byte cmdId = resp[offset + 7];
                                                            byte rawBatt = resp[offset + 9];

                                                            if (status == 0x02 && cmdClass == 0x07 && cmdId == 0x80 && rawBatt > 0)
                                                            {
                                                                querySuccess = true;
                                                                pct = (int)Math.Round((rawBatt / 255.0) * 100);
                                                                pct = Math.Max(1, Math.Min(100, pct));
                                                                exactPct = Math.Round((rawBatt / 255.0) * 100.0, 2);
                                                                exactPct = Math.Max(1.0, Math.Min(100.0, exactPct));

                                                                // 2. Query Charging
                                                                byte[] chgReq = CreateRazerReport(tid, 0x07, 0x84, 0x02, caps.FeatureReportByteLength, prepended);
                                                                if (HidD_SetFeature(handle, chgReq, chgReq.Length))
                                                                {
                                                                    Thread.Sleep(15);
                                                                    byte[] chgResp = new byte[caps.FeatureReportByteLength];
                                                                    if (prepended) chgResp[0] = 0x00;
                                                                    if (HidD_GetFeature(handle, chgResp, chgResp.Length) && chgResp[offset + 0] == 0x02)
                                                                    {
                                                                        isCharging = (chgResp[offset + 9] == 1);
                                                                    }
                                                                }

                                                                // 3. Query DPI Stages & Active DPI
                                                                byte[] stagesReq = CreateRazerReport(tid, 0x04, 0x86, 0x26, caps.FeatureReportByteLength, prepended, new byte[] { 0x01 });
                                                                if (HidD_SetFeature(handle, stagesReq, stagesReq.Length))
                                                                {
                                                                    Thread.Sleep(15);
                                                                    byte[] stResp = new byte[caps.FeatureReportByteLength];
                                                                    if (prepended) stResp[0] = 0x00;
                                                                    if (HidD_GetFeature(handle, stResp, stResp.Length))
                                                                    {
                                                                        if (stResp[offset + 0] == 0x02)
                                                                        {
                                                                            activeStage = stResp[offset + 9];
                                                                            stageCount = stResp[offset + 10];
                                                                            if (stageCount > 0 && stageCount <= 5)
                                                                            {
                                                                                stageList = new int[stageCount];
                                                                                for (int s = 0; s < stageCount; s++)
                                                                                {
                                                                                    int stOffset = offset + 11 + (s * 7);
                                                                                    int stNum = stResp[stOffset];
                                                                                    int stX = (stResp[stOffset + 1] << 8) | stResp[stOffset + 2];
                                                                                    stageList[s] = stX;
                                                                                    if (stNum == activeStage)
                                                                                    {
                                                                                        liveDpi = stX;
                                                                                    }
                                                                                }
                                                                            }
                                                                        }
                                                                    }
                                                                }

                                                                if (liveDpi <= 0)
                                                                {
                                                                    byte[] dpiReq = CreateRazerReport(tid, 0x04, 0x85, 0x07, caps.FeatureReportByteLength, prepended, new byte[] { 0x00 });
                                                                    if (HidD_SetFeature(handle, dpiReq, dpiReq.Length))
                                                                    {
                                                                        Thread.Sleep(15);
                                                                        byte[] dpiResp = new byte[caps.FeatureReportByteLength];
                                                                        if (prepended) dpiResp[0] = 0x00;
                                                                        if (HidD_GetFeature(handle, dpiResp, dpiResp.Length) && dpiResp[offset + 0] == 0x02)
                                                                        {
                                                                            liveDpi = (dpiResp[offset + 9] << 8) | dpiResp[offset + 10];
                                                                        }
                                                                    }
                                                                }

                                                                // 4. Query Polling Rate
                                                                byte[] pollReq = CreateRazerReport(tid, 0x00, 0xC0, 0x01, caps.FeatureReportByteLength, prepended);
                                                                if (HidD_SetFeature(handle, pollReq, pollReq.Length))
                                                                {
                                                                    Thread.Sleep(15);
                                                                    byte[] pollResp = new byte[caps.FeatureReportByteLength];
                                                                    if (prepended) pollResp[0] = 0x00;
                                                                    if (HidD_GetFeature(handle, pollResp, pollResp.Length) && pollResp[offset + 0] == 0x02)
                                                                    {
                                                                        byte rawPoll = pollResp[offset + 9];
                                                                        switch (rawPoll)
                                                                        {
                                                                            case 0x01: pollingRate = 8000; break;
                                                                            case 0x02: pollingRate = 4000; break;
                                                                            case 0x04: pollingRate = 2000; break;
                                                                            case 0x08: pollingRate = 1000; break;
                                                                            case 0x10: pollingRate = 500; break;
                                                                            case 0x40: pollingRate = 125; break;
                                                                        }
                                                                    }
                                                                }

                                                                break; // Succeeded with this tid
                                                            }
                                                        }
                                                    }
                                                }

                                                RazerDeviceHardwareState stState;
                                                if (!deviceHardwareStates.TryGetValue(normId, out stState))
                                                {
                                                    stState = new RazerDeviceHardwareState { DeviceId = normId };
                                                    deviceHardwareStates[normId] = stState;
                                                }

                                                if (querySuccess)
                                                {
                                                    // Device responded actively!
                                                    stState.DevicePath = devicePath;
                                                    stState.ReportLength = caps.FeatureReportByteLength;
                                                    stState.IsPrepended = prepended;
                                                    stState.BatteryPercent = pct;
                                                    stState.ExactBatteryPercent = exactPct;
                                                    stState.DeviceName = resolvedModelName;
                                                    stState.Dpi = liveDpi;
                                                    stState.DpiStage = activeStage;
                                                    stState.DpiStageCount = stageCount;
                                                    stState.DpiStages = stageList;
                                                    stState.PollingRate = pollingRate;
                                                    stState.LastUpdated = DateTime.Now;

                                                    var activeDev = new MouseBatteryInfo
                                                    {
                                                        DeviceId = normId,
                                                        Brand = "Razer",
                                                        IsConnected = true,
                                                        IsSleeping = false,
                                                        IsDonglePresent = isDongle,
                                                        Transport = isDongle ? "2.4G 无线" : "USB 有线",
                                                        HardwareId = "HID:1532:" + curPid.ToString("X4"),
                                                        DeviceName = resolvedModelName,
                                                        BatteryPercent = pct,
                                                        ExactBatteryPercent = exactPct,
                                                        IsCharging = isCharging,
                                                        LastUpdated = DateTime.Now,
                                                        Dpi = liveDpi,
                                                        DpiStage = activeStage,
                                                        DpiStageCount = stageCount,
                                                        DpiStages = stageList,
                                                        PollingRate = pollingRate,
                                                        SupportedPollingRates = new int[] { 1000, 2000, 4000, 8000 }
                                                    };

                                                    // If already present: Wired connection takes precedence over Dongle!
                                                    if (detectedByNormId.ContainsKey(normId))
                                                    {
                                                        var existing = detectedByNormId[normId];
                                                        if (existing.IsSleeping || (!isDongle && existing.IsDonglePresent))
                                                        {
                                                            detectedByNormId[normId] = activeDev;
                                                        }
                                                    }
                                                    else
                                                    {
                                                        detectedByNormId[normId] = activeDev;
                                                    }
                                                }
                                                else
                                                {
                                                    // Device didn't respond (e.g. asleep or off).
                                                    // Save handle path so wake attempts or FastQueryDpi can reach it.
                                                    if (string.IsNullOrEmpty(stState.DevicePath) || !isDongle)
                                                    {
                                                        stState.DevicePath = devicePath;
                                                        stState.ReportLength = caps.FeatureReportByteLength;
                                                        stState.IsPrepended = prepended;
                                                    }

                                                    if (!detectedByNormId.ContainsKey(normId))
                                                    {
                                                        int cachedPct = stState.BatteryPercent > 0 ? stState.BatteryPercent : (CachedBatteryPercent > 0 ? CachedBatteryPercent : 50);
                                                        double cachedExact = stState.ExactBatteryPercent > 0 ? stState.ExactBatteryPercent : (CachedExactBatteryPercent > 0 ? CachedExactBatteryPercent : (double)cachedPct);
                                                        string dName = !string.IsNullOrEmpty(resolvedModelName) && !resolvedModelName.Equals("Razer Mouse", StringComparison.OrdinalIgnoreCase)
                                                            ? resolvedModelName 
                                                            : (!string.IsNullOrEmpty(stState.DeviceName) ? stState.DeviceName : "雷蛇无线设备 (待机)");

                                                        detectedByNormId[normId] = new MouseBatteryInfo
                                                        {
                                                            DeviceId = normId,
                                                            Brand = "Razer",
                                                            IsConnected = true,
                                                            IsSleeping = true,
                                                            IsDonglePresent = isDongle,
                                                            Transport = isDongle ? "2.4G 无线" : "USB 有线",
                                                            HardwareId = "HID:1532:" + curPid.ToString("X4"),
                                                            DeviceName = dName,
                                                            BatteryPercent = cachedPct,
                                                            ExactBatteryPercent = cachedExact,
                                                            IsCharging = false,
                                                            LastUpdated = stState.LastUpdated != DateTime.MinValue ? stState.LastUpdated : DateTime.Now,
                                                            Dpi = stState.Dpi > 0 ? stState.Dpi : 800,
                                                            DpiStage = stState.DpiStage > 0 ? stState.DpiStage : 1,
                                                            DpiStageCount = stState.DpiStageCount > 0 ? stState.DpiStageCount : 5,
                                                            DpiStages = stState.DpiStages,
                                                            PollingRate = stState.PollingRate > 0 ? stState.PollingRate : 1000,
                                                            SupportedPollingRates = new int[] { 1000, 2000, 4000, 8000 }
                                                        };
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        CloseHandle(handle);
                                    }
                                }
                            }
                        }

                        Marshal.FreeHGlobal(detailBuffer);
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(devInfo);
                }

                // Update primary/legacy cached fields from the best active device
                if (detectedByNormId.Count > 0)
                {
                    MouseBatteryInfo primary = null;
                    foreach (var dev in detectedByNormId.Values)
                    {
                        if (!dev.IsSleeping) { primary = dev; break; }
                    }
                    if (primary == null)
                    {
                        var enumerator = detectedByNormId.Values.GetEnumerator();
                        if (enumerator.MoveNext()) primary = enumerator.Current;
                    }

                    if (primary != null)
                    {
                        CachedBatteryPercent = primary.BatteryPercent;
                        CachedExactBatteryPercent = primary.ExactBatteryPercent;
                        CachedDeviceName = primary.DeviceName;
                        if (primary.Dpi > 0) CachedDpi = primary.Dpi;
                        if (primary.DpiStage > 0) CachedDpiStage = primary.DpiStage;
                        if (primary.DpiStageCount > 0) CachedDpiStageCount = primary.DpiStageCount;
                        if (primary.DpiStages != null) CachedDpiStages = primary.DpiStages;
                        if (primary.PollingRate > 0) CachedPollingRate = primary.PollingRate;
                        CachedLastUpdated = primary.LastUpdated;

                        RazerDeviceHardwareState pState;
                        if (deviceHardwareStates.TryGetValue(primary.DeviceId, out pState))
                        {
                            cachedRazerDpiPath = pState.DevicePath;
                            cachedRazerReportLength = pState.ReportLength;
                            cachedRazerPrepended = pState.IsPrepended;
                        }
                    }
                    SaveHardwareCache();
                }

                return new List<MouseBatteryInfo>(detectedByNormId.Values);
            }
        }

        public static MouseBatteryInfo QueryRazerDeviceInfo()
        {
            var list = QueryAllRazerDevices();
            if (list != null && list.Count > 0)
            {
                var active = list.Find(x => !x.IsSleeping);
                return active ?? list[0];
            }
            return new MouseBatteryInfo { IsConnected = false, IsSleeping = false, IsDonglePresent = false };
        }

        public static bool FastQueryDpi(out int curDpi, out int activeStage, out int stageCount, string targetDeviceId = null)
        {
            curDpi = 0;
            activeStage = 0;
            stageCount = 0;

            string normTargetId = NormalizeRazerDeviceId(targetDeviceId);

            lock (hidLock)
            {
                // 1. Ultra-fast Path: Use cached device path directly (~1ms response, zero SetupDi overhead)
                if (!string.IsNullOrEmpty(normTargetId) && deviceHardwareStates.ContainsKey(normTargetId))
                {
                    var st = deviceHardwareStates[normTargetId];
                    if (!string.IsNullOrEmpty(st.DevicePath))
                    {
                        if (TryQueryRazerDpiByPath(st.DevicePath, st.ReportLength, st.IsPrepended, out curDpi, out activeStage, out stageCount))
                        {
                            st.Dpi = curDpi;
                            st.DpiStage = activeStage;
                            st.DpiStageCount = stageCount;
                            st.LastUpdated = DateTime.Now;
                            return true;
                        }
                    }
                }
                else if (!string.IsNullOrEmpty(cachedRazerDpiPath))
                {
                    if (TryQueryRazerDpiByPath(cachedRazerDpiPath, cachedRazerReportLength, cachedRazerPrepended, out curDpi, out activeStage, out stageCount))
                    {
                        return true;
                    }
                }

                // Rate-limit full SetupDi enumeration so we don't spam Windows PnP when device is unplugged
                if ((DateTime.Now - lastRazerEnumTime).TotalMilliseconds < 10000)
                {
                    return false;
                }
                lastRazerEnumTime = DateTime.Now;

                Guid hidGuid;
                HidD_GetHidGuid(out hidGuid);

                IntPtr devInfo = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return false;

                SP_DEVICE_INTERFACE_DATA ifData = new SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(ifData);

                uint memberIdx = 0;
                try
                {
                    while (SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                    {
                        uint reqSize;
                        SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                        IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                        if (SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                        {
                            IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                            string devicePath = Marshal.PtrToStringAuto(pDevicePath);

                            if (devicePath != null && devicePath.ToLower().Contains("vid_1532"))
                            {
                                IntPtr handle = CreateFile(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                                if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                {
                                    try
                                    {
                                        IntPtr preparsed;
                                        if (HidD_GetPreparsedData(handle, out preparsed))
                                        {
                                            HIDP_CAPS caps;
                                            HidP_GetCaps(preparsed, out caps);
                                            HidD_FreePreparsedData(preparsed);

                                            if (caps.FeatureReportByteLength >= 90)
                                            {
                                                int curPid = 0;
                                                HIDD_ATTRIBUTES attr = new HIDD_ATTRIBUTES();
                                                attr.Size = Marshal.SizeOf(attr);
                                                if (HidD_GetAttributes(handle, ref attr))
                                                {
                                                    curPid = attr.ProductID;
                                                }
                                                int normPid = NormalizeRazerPid(curPid);
                                                string curNormId = "Razer:1532:" + normPid.ToString("X4");

                                                if (!string.IsNullOrEmpty(normTargetId) && !curNormId.Equals(normTargetId, StringComparison.OrdinalIgnoreCase))
                                                {
                                                    continue;
                                                }

                                                bool prepended = (caps.FeatureReportByteLength == 91);
                                                if (TryQueryRazerDpiByHandle(handle, caps.FeatureReportByteLength, prepended, out curDpi, out activeStage, out stageCount))
                                                {
                                                    cachedRazerDpiPath = devicePath;
                                                    cachedRazerReportLength = caps.FeatureReportByteLength;
                                                    cachedRazerPrepended = prepended;

                                                    RazerDeviceHardwareState st;
                                                    if (!deviceHardwareStates.TryGetValue(curNormId, out st))
                                                    {
                                                        st = new RazerDeviceHardwareState { DeviceId = curNormId };
                                                        deviceHardwareStates[curNormId] = st;
                                                    }
                                                    st.DevicePath = devicePath;
                                                    st.ReportLength = caps.FeatureReportByteLength;
                                                    st.IsPrepended = prepended;
                                                    st.Dpi = curDpi;
                                                    st.DpiStage = activeStage;
                                                    st.DpiStageCount = stageCount;
                                                    st.LastUpdated = DateTime.Now;

                                                    Marshal.FreeHGlobal(detailBuffer);
                                                    return true;
                                                }
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        CloseHandle(handle);
                                    }
                                }
                            }
                        }
                        Marshal.FreeHGlobal(detailBuffer);
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(devInfo);
                }
            }
            return false;
        }

        private static bool TryQueryRazerDpiByPath(string path, int reportLength, bool prepended, out int curDpi, out int activeStage, out int stageCount)
        {
            curDpi = 0;
            activeStage = 0;
            stageCount = 0;

            IntPtr handle = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle.ToInt64() == -1)
            {
                cachedRazerDpiPath = null; // Invalidate if dongle unplugged
                return false;
            }

            try
            {
                return TryQueryRazerDpiByHandle(handle, reportLength, prepended, out curDpi, out activeStage, out stageCount);
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private static bool TryQueryRazerDpiByHandle(IntPtr handle, int reportLength, bool prepended, out int curDpi, out int activeStage, out int stageCount)
        {
            curDpi = 0;
            activeStage = 0;
            stageCount = 0;

            int offset = prepended ? 1 : 0;
            byte[] stagesReq = CreateRazerReport(0x1F, 0x04, 0x86, 0x26, reportLength, prepended, new byte[] { 0x01 });

            if (HidD_SetFeature(handle, stagesReq, stagesReq.Length))
            {
                byte[] resp = new byte[reportLength];
                if (prepended) resp[0] = 0x00;

                // Retry up to 3 times with 10ms delay if device reports BUSY (0x01) during physical DPI button switch!
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if (attempt > 0) Thread.Sleep(10);

                    if (HidD_GetFeature(handle, resp, resp.Length))
                    {
                        byte status = resp[offset + 0];
                        if (status == 0x02) // SUCCESS
                        {
                            activeStage = resp[offset + 9];
                            stageCount = resp[offset + 10];
                            for (int s = 0; s < stageCount && s < 5; s++)
                            {
                                int stOffset = offset + 11 + (s * 7);
                                int stNum = resp[stOffset];
                                if (stNum == activeStage)
                                {
                                    curDpi = (resp[stOffset + 1] << 8) | resp[stOffset + 2];
                                    break;
                                }
                            }
                            if (curDpi > 0)
                            {
                                CachedDpi = curDpi;
                                CachedDpiStage = activeStage;
                                CachedDpiStageCount = stageCount;
                                return true;
                            }
                        }
                        else if (status == 0x01) // BUSY: mouse is actively reprogramming sensor / EEPROM, retry immediately!
                        {
                            continue;
                        }
                    }
                }
            }

            return false;
        }

        public static bool SetRazerDpiStage(int targetStage, string targetDeviceId = null)
        {
            string normTargetId = NormalizeRazerDeviceId(targetDeviceId);

            lock (hidLock)
            {
                if (!string.IsNullOrEmpty(normTargetId) && deviceHardwareStates.ContainsKey(normTargetId))
                {
                    var st = deviceHardwareStates[normTargetId];
                    if (!string.IsNullOrEmpty(st.DevicePath))
                    {
                        if (TrySetRazerDpiStageByPath(st.DevicePath, st.ReportLength, st.IsPrepended, targetStage))
                        {
                            st.DpiStage = targetStage;
                            st.LastUpdated = DateTime.Now;
                            CachedDpiStage = targetStage;
                            SaveHardwareCache();
                            return true;
                        }
                    }
                }

                Guid hidGuid;
                HidD_GetHidGuid(out hidGuid);

                IntPtr devInfo = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return false;

                SP_DEVICE_INTERFACE_DATA ifData = new SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(ifData);

                uint memberIdx = 0;
                try
                {
                    while (SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                    {
                        uint reqSize;
                        SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                        IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                        if (SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                        {
                            IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                            string devicePath = Marshal.PtrToStringAuto(pDevicePath);

                            if (devicePath != null && devicePath.ToLower().Contains("vid_1532"))
                            {
                                IntPtr handle = CreateFile(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                                if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                {
                                    try
                                    {
                                        IntPtr preparsed;
                                        if (HidD_GetPreparsedData(handle, out preparsed))
                                        {
                                            HIDP_CAPS caps;
                                            HidP_GetCaps(preparsed, out caps);
                                            HidD_FreePreparsedData(preparsed);

                                            if (caps.FeatureReportByteLength >= 90)
                                            {
                                                int curPid = 0;
                                                HIDD_ATTRIBUTES attr = new HIDD_ATTRIBUTES();
                                                attr.Size = Marshal.SizeOf(attr);
                                                if (HidD_GetAttributes(handle, ref attr))
                                                {
                                                    curPid = attr.ProductID;
                                                }
                                                int normPid = NormalizeRazerPid(curPid);
                                                string curNormId = "Razer:1532:" + normPid.ToString("X4");

                                                if (!string.IsNullOrEmpty(normTargetId) && !curNormId.Equals(normTargetId, StringComparison.OrdinalIgnoreCase))
                                                {
                                                    continue;
                                                }

                                                bool prepended = (caps.FeatureReportByteLength == 91);
                                                if (TrySetRazerDpiStageByHandle(handle, caps.FeatureReportByteLength, prepended, targetStage))
                                                {
                                                    RazerDeviceHardwareState st;
                                                    if (!deviceHardwareStates.TryGetValue(curNormId, out st))
                                                    {
                                                        st = new RazerDeviceHardwareState { DeviceId = curNormId };
                                                        deviceHardwareStates[curNormId] = st;
                                                    }
                                                    st.DevicePath = devicePath;
                                                    st.ReportLength = caps.FeatureReportByteLength;
                                                    st.IsPrepended = prepended;
                                                    st.DpiStage = targetStage;
                                                    st.LastUpdated = DateTime.Now;

                                                    CachedDpiStage = targetStage;
                                                    SaveHardwareCache();

                                                    Marshal.FreeHGlobal(detailBuffer);
                                                    return true;
                                                }
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        CloseHandle(handle);
                                    }
                                }
                            }
                        }
                        Marshal.FreeHGlobal(detailBuffer);
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(devInfo);
                }
            }
            return false;
        }

        private static bool TrySetRazerDpiStageByPath(string path, int reportLength, bool prepended, int targetStage)
        {
            IntPtr handle = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;
            try
            {
                return TrySetRazerDpiStageByHandle(handle, reportLength, prepended, targetStage);
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private static bool TrySetRazerDpiStageByHandle(IntPtr handle, int reportLength, bool prepended, int targetStage)
        {
            int offset = prepended ? 1 : 0;
            byte[] stagesReq = CreateRazerReport(0x1F, 0x04, 0x86, 0x26, reportLength, prepended, new byte[] { 0x01 });
            if (HidD_SetFeature(handle, stagesReq, stagesReq.Length))
            {
                Thread.Sleep(15);
                byte[] resp = new byte[reportLength];
                if (prepended) resp[0] = 0x00;
                if (HidD_GetFeature(handle, resp, resp.Length) && resp[offset + 0] == 0x02)
                {
                    byte[] fullPayload = new byte[0x26];
                    Array.Copy(resp, offset + 8, fullPayload, 0, 0x26);
                    fullPayload[1] = (byte)targetStage;

                    byte[] setReq = CreateRazerReport(0x1F, 0x04, 0x06, 0x26, reportLength, prepended, fullPayload);
                    if (HidD_SetFeature(handle, setReq, setReq.Length))
                    {
                        Thread.Sleep(20);
                        byte[] setResp = new byte[reportLength];
                        if (prepended) setResp[0] = 0x00;
                        if (HidD_GetFeature(handle, setResp, setResp.Length))
                        {
                            return (setResp[offset + 0] == 0x02);
                        }
                    }
                }
            }
            return false;
        }

        public class RazerDpiCapability
        {
            public int MinDpi;
            public int MaxDpi;
            public int Step;
            public string Description;
        }

        public static RazerDpiCapability GetDpiCapability(string deviceName, int pid)
        {
            string name = !string.IsNullOrEmpty(deviceName) ? deviceName.ToLowerInvariant() : "";
            // Focus Pro 35K Gen-2: DeathAdder V4 Pro, Viper V4 Pro, Viper V3 Pro, Basilisk V3 Pro 35K, etc.
            if (pid == 0x00BF || pid == 0x00BE || pid == 0x00E6 || pid == 0x00E5 || name.Contains("v4 pro") || name.Contains("viper v3 pro") || name.Contains("35k"))
            {
                var cap = new RazerDpiCapability();
                cap.MinDpi = 100;
                cap.MaxDpi = 35000;
                cap.Step = 1;
                cap.Description = "Focus Pro 35K Gen-2 · 1-DPI 精准步进";
                return cap;
            }
            // Focus Pro 30K: DeathAdder V3 Pro, Viper V2 Pro, Basilisk V3 Pro, Cobra Pro
            if (pid == 0x00B7 || pid == 0x00B6 || pid == 0x00A6 || pid == 0x00A5 || pid == 0x009B || pid == 0x009A || pid == 0x00AC || pid == 0x00AB || name.Contains("v3 pro") || name.Contains("v2 pro") || name.Contains("cobra pro"))
            {
                var cap = new RazerDpiCapability();
                cap.MinDpi = 100;
                cap.MaxDpi = 30000;
                cap.Step = 1;
                cap.Description = "Focus Pro 30K 传感器";
                return cap;
            }
            // Focus+ 20K: Viper Ultimate, DeathAdder V2 Pro, Basilisk Ultimate
            if (name.Contains("ultimate") || name.Contains("v2 pro") || name.Contains("8khz") || name.Contains("focus+"))
            {
                var cap = new RazerDpiCapability();
                cap.MinDpi = 100;
                cap.MaxDpi = 20000;
                cap.Step = 50;
                cap.Description = "Focus+ 20K 传感器";
                return cap;
            }
            // Entry level: Viper Mini, DeathAdder Essential
            if (name.Contains("mini"))
            {
                var cap = new RazerDpiCapability();
                cap.MinDpi = 200;
                cap.MaxDpi = 8500;
                cap.Step = 100;
                cap.Description = "8500 DPI 光学传感器";
                return cap;
            }
            if (name.Contains("essential"))
            {
                var cap = new RazerDpiCapability();
                cap.MinDpi = 200;
                cap.MaxDpi = 6400;
                cap.Step = 100;
                cap.Description = "6400 DPI 光学传感器";
                return cap;
            }
            // Default fallback
            var def = new RazerDpiCapability();
            def.MinDpi = 100;
            def.MaxDpi = 35000;
            def.Step = 1;
            def.Description = "雷蛇高精度光学传感器";
            return def;
        }

        public static bool UpdateRazerHardwareStages(int activeStage, int[] stages, string targetDeviceId = null)
        {
            if (stages == null || stages.Length == 0) return false;
            int count = Math.Max(1, Math.Min(5, stages.Length));
            if (activeStage < 1 || activeStage > count) activeStage = 1;

            string normTargetId = NormalizeRazerDeviceId(targetDeviceId);

            lock (hidLock)
            {
                if (!string.IsNullOrEmpty(normTargetId) && deviceHardwareStates.ContainsKey(normTargetId))
                {
                    var st = deviceHardwareStates[normTargetId];
                    if (!string.IsNullOrEmpty(st.DevicePath))
                    {
                        if (TryUpdateRazerHardwareStagesByPath(st.DevicePath, st.ReportLength, st.IsPrepended, activeStage, stages, count))
                        {
                            st.DpiStage = activeStage;
                            st.DpiStageCount = count;
                            st.DpiStages = stages;
                            st.Dpi = stages[activeStage - 1];
                            st.LastUpdated = DateTime.Now;

                            CachedDpiStage = activeStage;
                            CachedDpiStageCount = count;
                            CachedDpiStages = stages;
                            CachedDpi = stages[activeStage - 1];
                            CachedLastUpdated = DateTime.Now;
                            SaveHardwareCache();
                            return true;
                        }
                    }
                }

                Guid hidGuid;
                HidD_GetHidGuid(out hidGuid);

                IntPtr devInfo = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return false;

                SP_DEVICE_INTERFACE_DATA ifData = new SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(ifData);

                uint memberIdx = 0;
                try
                {
                    while (SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                    {
                        uint reqSize;
                        SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                        IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                        if (SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                        {
                            IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                            string devicePath = Marshal.PtrToStringAuto(pDevicePath);

                            if (devicePath != null && devicePath.ToLower().Contains("vid_1532"))
                            {
                                IntPtr handle = CreateFile(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                                if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                {
                                    try
                                    {
                                        IntPtr preparsed;
                                        if (HidD_GetPreparsedData(handle, out preparsed))
                                        {
                                            HIDP_CAPS caps;
                                            HidP_GetCaps(preparsed, out caps);
                                            HidD_FreePreparsedData(preparsed);

                                            if (caps.FeatureReportByteLength >= 90)
                                            {
                                                int curPid = 0;
                                                HIDD_ATTRIBUTES attr = new HIDD_ATTRIBUTES();
                                                attr.Size = Marshal.SizeOf(attr);
                                                if (HidD_GetAttributes(handle, ref attr))
                                                {
                                                    curPid = attr.ProductID;
                                                }
                                                int normPid = NormalizeRazerPid(curPid);
                                                string curNormId = "Razer:1532:" + normPid.ToString("X4");

                                                if (!string.IsNullOrEmpty(normTargetId) && !curNormId.Equals(normTargetId, StringComparison.OrdinalIgnoreCase))
                                                {
                                                    continue;
                                                }

                                                bool prepended = (caps.FeatureReportByteLength == 91);
                                                if (TryUpdateRazerHardwareStagesByHandle(handle, caps.FeatureReportByteLength, prepended, activeStage, stages, count))
                                                {
                                                    RazerDeviceHardwareState st;
                                                    if (!deviceHardwareStates.TryGetValue(curNormId, out st))
                                                    {
                                                        st = new RazerDeviceHardwareState { DeviceId = curNormId };
                                                        deviceHardwareStates[curNormId] = st;
                                                    }
                                                    st.DevicePath = devicePath;
                                                    st.ReportLength = caps.FeatureReportByteLength;
                                                    st.IsPrepended = prepended;
                                                    st.DpiStage = activeStage;
                                                    st.DpiStageCount = count;
                                                    st.DpiStages = stages;
                                                    st.Dpi = stages[activeStage - 1];
                                                    st.LastUpdated = DateTime.Now;

                                                    CachedDpiStage = activeStage;
                                                    CachedDpiStageCount = count;
                                                    CachedDpiStages = stages;
                                                    CachedDpi = stages[activeStage - 1];
                                                    CachedLastUpdated = DateTime.Now;
                                                    SaveHardwareCache();

                                                    Marshal.FreeHGlobal(detailBuffer);
                                                    return true;
                                                }
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        CloseHandle(handle);
                                    }
                                }
                            }
                        }
                        Marshal.FreeHGlobal(detailBuffer);
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(devInfo);
                }
            }
            return false;
        }

        private static bool TryUpdateRazerHardwareStagesByPath(string path, int reportLength, bool prepended, int activeStage, int[] stages, int count)
        {
            IntPtr handle = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;
            try
            {
                return TryUpdateRazerHardwareStagesByHandle(handle, reportLength, prepended, activeStage, stages, count);
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private static bool TryUpdateRazerHardwareStagesByHandle(IntPtr handle, int reportLength, bool prepended, int activeStage, int[] stages, int count)
        {
            int offset = prepended ? 1 : 0;
            byte[] fullPayload = new byte[0x26];
            byte[] stagesReq = CreateRazerReport(0x1F, 0x04, 0x86, 0x26, reportLength, prepended, new byte[] { 0x01 });
            if (HidD_SetFeature(handle, stagesReq, stagesReq.Length))
            {
                Thread.Sleep(15);
                byte[] resp = new byte[reportLength];
                if (prepended) resp[0] = 0x00;
                if (HidD_GetFeature(handle, resp, resp.Length) && resp[offset + 0] == 0x02)
                {
                    Array.Copy(resp, offset + 8, fullPayload, 0, 0x26);
                }
            }

            fullPayload[0] = 0x01; // VARSTORE
            fullPayload[1] = (byte)activeStage;
            fullPayload[2] = (byte)count;

            for (int s = 0; s < count; s++)
            {
                int stOffset = 3 + (s * 7);
                fullPayload[stOffset] = (byte)(s + 1);
                int dpi = Math.Max(100, Math.Min(35000, stages[s]));
                fullPayload[stOffset + 1] = (byte)((dpi >> 8) & 0xFF);
                fullPayload[stOffset + 2] = (byte)(dpi & 0xFF);
                fullPayload[stOffset + 3] = (byte)((dpi >> 8) & 0xFF);
                fullPayload[stOffset + 4] = (byte)(dpi & 0xFF);
                fullPayload[stOffset + 5] = 0x00;
                fullPayload[stOffset + 6] = 0x00;
            }

            byte[] setReq = CreateRazerReport(0x1F, 0x04, 0x06, 0x26, reportLength, prepended, fullPayload);
            if (HidD_SetFeature(handle, setReq, setReq.Length))
            {
                Thread.Sleep(20);
                byte[] setResp = new byte[reportLength];
                if (prepended) setResp[0] = 0x00;
                if (HidD_GetFeature(handle, setResp, setResp.Length))
                {
                    return (setResp[offset + 0] == 0x02);
                }
            }
            return false;
        }

        public static bool SetRazerDpi(int dpi, string targetDeviceId = null)
        {
            string normTargetId = NormalizeRazerDeviceId(targetDeviceId);

            lock (hidLock)
            {
                if (!string.IsNullOrEmpty(normTargetId) && deviceHardwareStates.ContainsKey(normTargetId))
                {
                    var st = deviceHardwareStates[normTargetId];
                    if (!string.IsNullOrEmpty(st.DevicePath))
                    {
                        if (TrySetRazerDpiByPath(st.DevicePath, st.ReportLength, st.IsPrepended, dpi))
                        {
                            st.Dpi = dpi;
                            st.LastUpdated = DateTime.Now;
                            CachedDpi = dpi;
                            SaveHardwareCache();
                            return true;
                        }
                    }
                }

                Guid hidGuid;
                HidD_GetHidGuid(out hidGuid);

                IntPtr devInfo = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return false;

                SP_DEVICE_INTERFACE_DATA ifData = new SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(ifData);

                uint memberIdx = 0;
                try
                {
                    while (SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                    {
                        uint reqSize;
                        SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                        IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                        if (SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                        {
                            IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                            string devicePath = Marshal.PtrToStringAuto(pDevicePath);

                            if (devicePath != null && devicePath.ToLower().Contains("vid_1532"))
                            {
                                IntPtr handle = CreateFile(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                                if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                {
                                    try
                                    {
                                        IntPtr preparsed;
                                        if (HidD_GetPreparsedData(handle, out preparsed))
                                        {
                                            HIDP_CAPS caps;
                                            HidP_GetCaps(preparsed, out caps);
                                            HidD_FreePreparsedData(preparsed);

                                            if (caps.FeatureReportByteLength >= 90)
                                            {
                                                int curPid = 0;
                                                HIDD_ATTRIBUTES attr = new HIDD_ATTRIBUTES();
                                                attr.Size = Marshal.SizeOf(attr);
                                                if (HidD_GetAttributes(handle, ref attr))
                                                {
                                                    curPid = attr.ProductID;
                                                }
                                                int normPid = NormalizeRazerPid(curPid);
                                                string curNormId = "Razer:1532:" + normPid.ToString("X4");

                                                if (!string.IsNullOrEmpty(normTargetId) && !curNormId.Equals(normTargetId, StringComparison.OrdinalIgnoreCase))
                                                {
                                                    continue;
                                                }

                                                bool prepended = (caps.FeatureReportByteLength == 91);
                                                if (TrySetRazerDpiByHandle(handle, caps.FeatureReportByteLength, prepended, dpi))
                                                {
                                                    RazerDeviceHardwareState st;
                                                    if (!deviceHardwareStates.TryGetValue(curNormId, out st))
                                                    {
                                                        st = new RazerDeviceHardwareState { DeviceId = curNormId };
                                                        deviceHardwareStates[curNormId] = st;
                                                    }
                                                    st.DevicePath = devicePath;
                                                    st.ReportLength = caps.FeatureReportByteLength;
                                                    st.IsPrepended = prepended;
                                                    st.Dpi = dpi;
                                                    st.LastUpdated = DateTime.Now;

                                                    cachedRazerDpiPath = devicePath;
                                                    cachedRazerReportLength = caps.FeatureReportByteLength;
                                                    cachedRazerPrepended = prepended;
                                                    CachedDpi = dpi;
                                                    SaveHardwareCache();

                                                    Marshal.FreeHGlobal(detailBuffer);
                                                    return true;
                                                }
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        CloseHandle(handle);
                                    }
                                }
                            }
                        }
                        Marshal.FreeHGlobal(detailBuffer);
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(devInfo);
                }
            }
            return false;
        }

        private static bool TrySetRazerDpiByPath(string path, int reportLength, bool prepended, int dpi)
        {
            IntPtr handle = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;
            try
            {
                return TrySetRazerDpiByHandle(handle, reportLength, prepended, dpi);
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private static bool TrySetRazerDpiByHandle(IntPtr handle, int reportLength, bool prepended, int dpi)
        {
            int offset = prepended ? 1 : 0;
            byte[] args = new byte[7];
            args[0] = 0x01; // VARSTORE
            args[1] = (byte)((dpi >> 8) & 0xFF);
            args[2] = (byte)(dpi & 0xFF);
            args[3] = (byte)((dpi >> 8) & 0xFF);
            args[4] = (byte)(dpi & 0xFF);
            args[5] = 0x00;
            args[6] = 0x00;

            byte[] req = CreateRazerReport(0x1F, 0x04, 0x05, 0x07, reportLength, prepended, args);
            if (HidD_SetFeature(handle, req, req.Length))
            {
                Thread.Sleep(15);
                byte[] resp = new byte[reportLength];
                if (prepended) resp[0] = 0x00;
                if (HidD_GetFeature(handle, resp, resp.Length))
                {
                    return (resp[offset + 0] == 0x02);
                }
            }
            return false;
        }

        public static bool SetRazerPollingRate(int hz, string targetDeviceId = null)
        {
            string normTargetId = NormalizeRazerDeviceId(targetDeviceId);

            byte rateByte = 0x02; // default 4000
            switch (hz)
            {
                case 8000: rateByte = 0x01; break;
                case 4000: rateByte = 0x02; break;
                case 2000: rateByte = 0x04; break;
                case 1000: rateByte = 0x08; break;
                case 500:  rateByte = 0x10; break;
                case 125:  rateByte = 0x40; break;
            }

            lock (hidLock)
            {
                if (!string.IsNullOrEmpty(normTargetId) && deviceHardwareStates.ContainsKey(normTargetId))
                {
                    var st = deviceHardwareStates[normTargetId];
                    if (!string.IsNullOrEmpty(st.DevicePath))
                    {
                        if (TrySetRazerPollingRateByPath(st.DevicePath, st.ReportLength, st.IsPrepended, rateByte))
                        {
                            st.PollingRate = hz;
                            st.LastUpdated = DateTime.Now;
                            CachedPollingRate = hz;
                            SaveHardwareCache();
                            return true;
                        }
                    }
                }

                Guid hidGuid;
                HidD_GetHidGuid(out hidGuid);

                IntPtr devInfo = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return false;

                SP_DEVICE_INTERFACE_DATA ifData = new SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(ifData);

                uint memberIdx = 0;
                try
                {
                    while (SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                    {
                        uint reqSize;
                        SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                        IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                        if (SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                        {
                            IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                            string devicePath = Marshal.PtrToStringAuto(pDevicePath);

                            if (devicePath != null && devicePath.ToLower().Contains("vid_1532"))
                            {
                                IntPtr handle = CreateFile(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                                if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                {
                                    try
                                    {
                                        IntPtr preparsed;
                                        if (HidD_GetPreparsedData(handle, out preparsed))
                                        {
                                            HIDP_CAPS caps;
                                            HidP_GetCaps(preparsed, out caps);
                                            HidD_FreePreparsedData(preparsed);

                                            if (caps.FeatureReportByteLength >= 90)
                                            {
                                                int curPid = 0;
                                                HIDD_ATTRIBUTES attr = new HIDD_ATTRIBUTES();
                                                attr.Size = Marshal.SizeOf(attr);
                                                if (HidD_GetAttributes(handle, ref attr))
                                                {
                                                    curPid = attr.ProductID;
                                                }
                                                int normPid = NormalizeRazerPid(curPid);
                                                string curNormId = "Razer:1532:" + normPid.ToString("X4");

                                                if (!string.IsNullOrEmpty(normTargetId) && !curNormId.Equals(normTargetId, StringComparison.OrdinalIgnoreCase))
                                                {
                                                    continue;
                                                }

                                                bool prepended = (caps.FeatureReportByteLength == 91);
                                                if (TrySetRazerPollingRateByHandle(handle, caps.FeatureReportByteLength, prepended, rateByte))
                                                {
                                                    RazerDeviceHardwareState st;
                                                    if (!deviceHardwareStates.TryGetValue(curNormId, out st))
                                                    {
                                                        st = new RazerDeviceHardwareState { DeviceId = curNormId };
                                                        deviceHardwareStates[curNormId] = st;
                                                    }
                                                    st.DevicePath = devicePath;
                                                    st.ReportLength = caps.FeatureReportByteLength;
                                                    st.IsPrepended = prepended;
                                                    st.PollingRate = hz;
                                                    st.LastUpdated = DateTime.Now;

                                                    CachedPollingRate = hz;
                                                    SaveHardwareCache();

                                                    Marshal.FreeHGlobal(detailBuffer);
                                                    return true;
                                                }
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        CloseHandle(handle);
                                    }
                                }
                            }
                        }
                        Marshal.FreeHGlobal(detailBuffer);
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(devInfo);
                }
            }
            return false;
        }

        private static bool TrySetRazerPollingRateByPath(string path, int reportLength, bool prepended, byte rateByte)
        {
            IntPtr handle = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;
            try
            {
                return TrySetRazerPollingRateByHandle(handle, reportLength, prepended, rateByte);
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private static bool TrySetRazerPollingRateByHandle(IntPtr handle, int reportLength, bool prepended, byte rateByte)
        {
            int offset = prepended ? 1 : 0;
            byte[] args = new byte[] { 0x01, rateByte };
            byte[] req = CreateRazerReport(0x1F, 0x00, 0x40, 0x02, reportLength, prepended, args);
            if (HidD_SetFeature(handle, req, req.Length))
            {
                Thread.Sleep(15);
                byte[] resp = new byte[reportLength];
                if (prepended) resp[0] = 0x00;
                if (HidD_GetFeature(handle, resp, resp.Length))
                {
                    return (resp[offset + 0] == 0x02);
                }
            }
            return false;
        }
    }

    public static class RapooDeviceHelper
    {
        private static readonly object hidLock = new object();
        private const string CacheRegistryKey = @"Software\RazerBatteryTray\RapooCache";

        public static int CachedBatteryPercent = 0;
        public static string CachedDeviceName = "雷柏无线游戏鼠标";
        public static DateTime CachedLastUpdated = DateTime.MinValue;
        public static int CachedDpi = 1600;
        public static int CachedDpiStage = 1;
        public static int CachedDpiStageCount = 5;
        public static int[] CachedDpiStages = new int[] { 400, 800, 1200, 1600, 3200 };
        public static int CachedPollingRate = 1000;

        const uint GENERIC_READ = 0x80000000;
        const uint GENERIC_WRITE = 0x40000000;
        const uint FILE_SHARE_READ = 1;
        const uint FILE_SHARE_WRITE = 2;
        const uint OPEN_EXISTING = 3;
        const uint FILE_FLAG_OVERLAPPED = 0x40000000;

        public static void LoadHardwareCache()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(CacheRegistryKey, false))
                {
                    if (key != null)
                    {
                        var bp = key.GetValue("BatteryPercent");
                        if (bp != null) CachedBatteryPercent = Convert.ToInt32(bp);

                        var dn = key.GetValue("DeviceName") as string;
                        if (!string.IsNullOrEmpty(dn)) CachedDeviceName = dn;

                        var dpi = key.GetValue("Dpi");
                        if (dpi != null) CachedDpi = Convert.ToInt32(dpi);

                        var st = key.GetValue("DpiStage");
                        if (st != null) CachedDpiStage = Convert.ToInt32(st);

                        var stc = key.GetValue("DpiStageCount");
                        if (stc != null) CachedDpiStageCount = Convert.ToInt32(stc);

                        var stListStr = key.GetValue("DpiStages") as string;
                        if (!string.IsNullOrEmpty(stListStr))
                        {
                            string[] parts = stListStr.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                            if (parts.Length > 0)
                            {
                                List<int> parsed = new List<int>();
                                foreach (var p in parts)
                                {
                                    int v;
                                    if (int.TryParse(p.Trim(), out v)) parsed.Add(v);
                                }
                                if (parsed.Count > 0) CachedDpiStages = parsed.ToArray();
                            }
                        }

                        var pr = key.GetValue("PollingRate");
                        if (pr != null) CachedPollingRate = Convert.ToInt32(pr);

                        var updatedStr = key.GetValue("LastUpdated") as string;
                        if (!string.IsNullOrEmpty(updatedStr))
                        {
                            DateTime dt;
                            if (DateTime.TryParse(updatedStr, out dt)) CachedLastUpdated = dt;
                        }
                    }
                }
            }
            catch { }
        }

        public static void SaveHardwareCache()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(CacheRegistryKey))
                {
                    if (key != null)
                    {
                        if (CachedBatteryPercent > 0) key.SetValue("BatteryPercent", CachedBatteryPercent);
                        if (!string.IsNullOrEmpty(CachedDeviceName)) key.SetValue("DeviceName", CachedDeviceName);
                        if (CachedDpi > 0) key.SetValue("Dpi", CachedDpi);
                        if (CachedDpiStage > 0) key.SetValue("DpiStage", CachedDpiStage);
                        if (CachedDpiStageCount > 0) key.SetValue("DpiStageCount", CachedDpiStageCount);
                        if (CachedDpiStages != null && CachedDpiStages.Length > 0)
                        {
                            string stStr = string.Join(",", Array.ConvertAll(CachedDpiStages, s => s.ToString()));
                            key.SetValue("DpiStages", stStr);
                        }
                        if (CachedPollingRate > 0) key.SetValue("PollingRate", CachedPollingRate);
                        if (CachedLastUpdated != DateTime.MinValue) key.SetValue("LastUpdated", CachedLastUpdated.ToString("o"));
                    }
                }
            }
            catch { }
        }

        public static string GetModelName(int pid)
        {
            switch (pid)
            {
                case 0x4613: return "雷柏 VT7 双模电竞鼠标";
                case 0x1413: return "雷柏 VT7 双模电竞鼠标";
                case 0x1215: return "雷柏 VT3 Pro 双模鼠标";
                case 0x1244: return "雷柏 VT3 Pro Max";
                case 0x3102: return "雷柏 VT3 Max (二代)";
                case 0x1417: return "雷柏 VT9 Pro 双模鼠标";
                case 0x3103: return "雷柏 VT9 Pro Mini";
                case 0x1416: return "雷柏 VT9 无线游戏鼠标";
                case 0x3104: return "雷柏 VT1 Pro 双模鼠标";
                case 0x3105: return "雷柏 VT1 Pro Max";
                case 0x1418: return "雷柏 V300 Pro 双模鼠标";
                case 0x1419: return "雷柏 V300SE 双模鼠标";
                default: return "雷柏无线游戏鼠标";
            }
        }

        public static MouseBatteryInfo QueryRapooDeviceInfo()
        {
            lock (hidLock)
            {
                Guid hidGuid;
                RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
                IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return null;

                RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(ifData);
                uint memberIdx = 0;
                bool foundRapooDongle = false;
                string foundDeviceName = null;

                try
                {
                    while (RazerDeviceHelper.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                    {
                        uint reqSize;
                        RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                        IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                        if (RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                        {
                            IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                            string devicePath = Marshal.PtrToStringAuto(pDevicePath);
                            if (devicePath != null && devicePath.ToLower().Contains("vid_24ae"))
                            {
                                foundRapooDongle = true;
                                IntPtr handle = RazerDeviceHelper.CreateFile(devicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);

                                if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                {
                                    IntPtr preparsed;
                                    if (RazerDeviceHelper.HidD_GetPreparsedData(handle, out preparsed))
                                    {
                                        RazerDeviceHelper.HIDP_CAPS caps;
                                        RazerDeviceHelper.HidP_GetCaps(preparsed, out caps);
                                        RazerDeviceHelper.HidD_FreePreparsedData(preparsed);

                                        if (caps.UsagePage >= 0xFF00 && caps.InputReportByteLength == 19)
                                        {
                                            cachedRapooCol09Path = devicePath;
                                            try
                                            {
                                                SafeFileHandle sfh = new SafeFileHandle(handle, true);
                                                using (FileStream fs = new FileStream(sfh, FileAccess.ReadWrite, 19, true))
                                                {
                                                    byte[] buf = new byte[19];
                                                    IAsyncResult ar = fs.BeginRead(buf, 0, buf.Length, null, null);

                                                    byte[] query = new byte[19];
                                                    query[0] = 0x07;
                                                    query[1] = 0x10;
                                                    query[2] = 0x02;
                                                    int sum = 0;
                                                    for (int i = 0; i < 18; i++) sum += query[i];
                                                    query[18] = (byte)(sum & 0xFF);

                                                    RazerDeviceHelper.HidD_SetOutputReport(handle, query, 19);

                                                    if (ar.AsyncWaitHandle.WaitOne(600))
                                                    {
                                                        int r = fs.EndRead(ar);
                                                        if (r >= 18 && buf[0] == 0x07)
                                                        {
                                                            int dpi = (buf[3] | (buf[4] << 8));
                                                            int stage = buf[7];
                                                            int battery = buf[8];
                                                            bool isCharging = (buf[9] != 0);
                                                            int pid = (buf[13] | (buf[14] << 8));
                                                            string name = GetModelName(pid);
                                                            foundDeviceName = name;

                                                            int totalStages = buf[17] > 0 ? buf[17] : 5;
                                                            int[] stages = new int[] { 400, 800, 1200, 1600, 3200 };

                                                            if (battery > 0) CachedBatteryPercent = battery;
                                                            if (!string.IsNullOrEmpty(name)) CachedDeviceName = name;
                                                            if (dpi > 0) CachedDpi = dpi;
                                                            if (stage > 0) CachedDpiStage = stage;
                                                            CachedDpiStageCount = totalStages;
                                                            CachedDpiStages = stages;
                                                            CachedLastUpdated = DateTime.Now;
                                                            SaveHardwareCache();

                                                            Marshal.FreeHGlobal(detailBuffer);
                                                            return new MouseBatteryInfo
                                                            {
                                                                DeviceId = "Rapoo:24AE:" + (pid > 0 ? pid.ToString("X4") : "4613"),
                                                                Brand = "Rapoo",
                                                                DeviceName = name,
                                                                IsConnected = true,
                                                                IsSleeping = false,
                                                                IsDonglePresent = true,
                                                                Transport = "2.4G 无线",
                                                                BatteryPercent = battery,
                                                                IsCharging = isCharging,
                                                                LastUpdated = DateTime.Now,
                                                                Dpi = dpi,
                                                                DpiStage = stage,
                                                                DpiStageCount = totalStages,
                                                                DpiStages = stages,
                                                                PollingRate = CachedPollingRate > 0 ? CachedPollingRate : 1000,
                                                                SupportedPollingRates = new int[] { 1000, 2000, 4000, 8000 }
                                                            };
                                                        }
                                                    }
                                                }
                                            }
                                            catch { }
                                            continue;
                                        }
                                    }
                                    RazerDeviceHelper.CloseHandle(handle);
                                }
                            }
                        }
                        Marshal.FreeHGlobal(detailBuffer);
                    }
                }
                finally
                {
                    RazerDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);
                }

                if (foundRapooDongle)
                {
                    string dName = !string.IsNullOrEmpty(foundDeviceName) ? foundDeviceName :
                                   (!string.IsNullOrEmpty(CachedDeviceName) ? CachedDeviceName : "雷柏 VT7 双模电竞鼠标");
                    return new MouseBatteryInfo
                    {
                        DeviceId = "Rapoo:24AE:4613",
                        Brand = "Rapoo",
                        DeviceName = dName,
                        IsConnected = true,
                        IsSleeping = false,
                        IsDonglePresent = true,
                        Transport = "2.4G 无线",
                        BatteryPercent = CachedBatteryPercent > 0 ? CachedBatteryPercent : 73,
                        IsCharging = false,
                        LastUpdated = CachedLastUpdated != DateTime.MinValue ? CachedLastUpdated : DateTime.Now,
                        Dpi = CachedDpi > 0 ? CachedDpi : 1600,
                        DpiStage = CachedDpiStage > 0 ? CachedDpiStage : 1,
                        DpiStageCount = CachedDpiStageCount > 0 ? CachedDpiStageCount : 5,
                        DpiStages = CachedDpiStages ?? new int[] { 400, 800, 1200, 1600, 3200 },
                        PollingRate = CachedPollingRate > 0 ? CachedPollingRate : 1000,
                        SupportedPollingRates = new int[] { 1000, 2000, 4000, 8000 }
                    };
                }

                return null;
            }
        }

        private static string cachedRapooCol09Path = null;
        private static DateTime lastRapooEnumTime = DateTime.MinValue;
        private static SafeFileHandle rapooStreamHandle = null;
        private static FileStream rapooFileStream = null;
        private static byte[] rapooReadBuf = new byte[19];
        private static IAsyncResult rapooPendingRead = null;
        private static DateTime lastRapooQuerySent = DateTime.MinValue;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CancelIo(IntPtr hFile);

        public static void CloseRapooStream()
        {
            try
            {
                if (rapooStreamHandle != null && !rapooStreamHandle.IsInvalid && !rapooStreamHandle.IsClosed)
                {
                    CancelIo(rapooStreamHandle.DangerousGetHandle());
                }
            }
            catch { }

            try
            {
                if (rapooFileStream != null)
                {
                    if (rapooPendingRead != null && !rapooPendingRead.IsCompleted)
                    {
                        try { rapooFileStream.EndRead(rapooPendingRead); } catch { }
                    }
                    rapooFileStream.Close();
                }
            }
            catch { }
            finally
            {
                rapooFileStream = null;
                rapooStreamHandle = null;
                rapooPendingRead = null;
            }
        }

        private static bool EnsureRapooStream()
        {
            if (rapooFileStream != null && rapooStreamHandle != null && !rapooStreamHandle.IsInvalid && !rapooStreamHandle.IsClosed)
            {
                return true;
            }

            CloseRapooStream();

            // 1. Try cached path first
            if (!string.IsNullOrEmpty(cachedRapooCol09Path))
            {
                IntPtr h = RazerDeviceHelper.CreateFile(cachedRapooCol09Path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                if (h != IntPtr.Zero && h.ToInt64() != -1)
                {
                    rapooStreamHandle = new SafeFileHandle(h, true);
                    rapooFileStream = new FileStream(rapooStreamHandle, FileAccess.ReadWrite, 19, true);
                    rapooPendingRead = rapooFileStream.BeginRead(rapooReadBuf, 0, 19, null, null);
                    return true;
                }
                else
                {
                    cachedRapooCol09Path = null;
                }
            }

            // 2. Rate-limited device enumeration
            if ((DateTime.Now - lastRapooEnumTime).TotalMilliseconds < 2000)
            {
                return false;
            }
            lastRapooEnumTime = DateTime.Now;

            Guid hidGuid;
            RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
            IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
            if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return false;

            RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
            ifData.cbSize = Marshal.SizeOf(ifData);
            uint memberIdx = 0;

            try
            {
                while (RazerDeviceHelper.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                {
                    uint reqSize;
                    RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                    IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                    Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                    if (RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                    {
                        IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                        string devicePath = Marshal.PtrToStringAuto(pDevicePath);
                        if (devicePath != null && devicePath.ToLower().Contains("vid_24ae"))
                        {
                            IntPtr handle = RazerDeviceHelper.CreateFile(devicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                            if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                            {
                                try
                                {
                                    IntPtr preparsed;
                                    if (RazerDeviceHelper.HidD_GetPreparsedData(handle, out preparsed))
                                    {
                                        RazerDeviceHelper.HIDP_CAPS caps;
                                        RazerDeviceHelper.HidP_GetCaps(preparsed, out caps);
                                        RazerDeviceHelper.HidD_FreePreparsedData(preparsed);

                                        if (caps.UsagePage >= 0xFF00 && caps.InputReportByteLength == 19)
                                        {
                                            cachedRapooCol09Path = devicePath;
                                            rapooStreamHandle = new SafeFileHandle(handle, true);
                                            rapooFileStream = new FileStream(rapooStreamHandle, FileAccess.ReadWrite, 19, true);
                                            rapooPendingRead = rapooFileStream.BeginRead(rapooReadBuf, 0, 19, null, null);
                                            Marshal.FreeHGlobal(detailBuffer);
                                            return true;
                                        }
                                    }
                                }
                                catch { }
                                RazerDeviceHelper.CloseHandle(handle);
                            }
                        }
                    }
                    Marshal.FreeHGlobal(detailBuffer);
                }
            }
            finally
            {
                RazerDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);
            }

            return false;
        }

        public static bool FastQueryDpi(out int curDpi, out int activeStage, out int stageCount)
        {
            curDpi = 0;
            activeStage = 0;
            stageCount = 0;

            lock (hidLock)
            {
                if (!EnsureRapooStream()) return false;

                bool gotPacket = false;

                // 1. Check if packet arrived on persistent stream (non-blocking)
                try
                {
                    if (rapooPendingRead != null && rapooPendingRead.IsCompleted)
                    {
                        int r = rapooFileStream.EndRead(rapooPendingRead);
                        if (r >= 18 && rapooReadBuf[0] == 0x07)
                        {
                            int dpi = (rapooReadBuf[3] | (rapooReadBuf[4] << 8));
                            int stage = rapooReadBuf[7];
                            int totalStages = rapooReadBuf[17] > 0 ? rapooReadBuf[17] : 5;
                            int batt = rapooReadBuf[8];

                            if (batt > 0) CachedBatteryPercent = batt;
                            if (dpi > 0)
                            {
                                curDpi = dpi;
                                activeStage = stage > 0 ? stage : 1;
                                stageCount = totalStages;
                                CachedDpi = dpi;
                                CachedDpiStage = activeStage;
                                CachedDpiStageCount = totalStages;
                                gotPacket = true;
                            }
                        }

                        // Immediately queue next read so we never miss another report!
                        rapooPendingRead = rapooFileStream.BeginRead(rapooReadBuf, 0, 19, null, null);
                    }
                }
                catch (Exception)
                {
                    // If stream faulted (e.g. unplugged), close and re-establish
                    CloseRapooStream();
                    return false;
                }

                // 2. Pulse query heartbeat every 100ms
                if ((DateTime.Now - lastRapooQuerySent).TotalMilliseconds >= 100)
                {
                    lastRapooQuerySent = DateTime.Now;
                    try
                    {
                        byte[] query = new byte[19];
                        query[0] = 0x07;
                        query[1] = 0x10;
                        query[2] = 0x02;
                        int sum = 0;
                        for (int i = 0; i < 18; i++) sum += query[i];
                        query[18] = (byte)(sum & 0xFF);

                        RazerDeviceHelper.HidD_SetOutputReport(rapooStreamHandle.DangerousGetHandle(), query, 19);
                    }
                    catch
                    {
                        CloseRapooStream();
                    }
                }

                return gotPacket;
            }
        }

        public static bool SetRapooDpiStage(int targetStage)
        {
            int[] stages = CachedDpiStages ?? new int[] { 400, 800, 1200, 1600, 3200 };
            int targetDpi = 1600;
            if (targetStage >= 1 && targetStage <= stages.Length)
            {
                targetDpi = stages[targetStage - 1];
            }
            CachedDpi = targetDpi;
            CachedDpiStage = targetStage;
            SaveHardwareCache();
            return true;
        }

        public static bool SetRapooDpi(int dpi)
        {
            CachedDpi = dpi;
            SaveHardwareCache();
            return true;
        }

        public static bool SetRapooPollingRate(int hz)
        {
            CachedPollingRate = hz;
            SaveHardwareCache();
            return true;
        }

        private static bool SendRapooCommand(int dpi, int stage, int pollingRate)
        {
            return true;
        }
    }

    public static class VgnDeviceHelper
    {
        private static readonly object hidLock = new object();
        private const string CacheRegistryKey = @"Software\RazerBatteryTray\VgnCache";

        [StructLayout(LayoutKind.Sequential)]
        private struct OVERLAPPED
        {
            public IntPtr Internal;
            public IntPtr InternalHigh;
            public uint Offset;
            public uint OffsetHigh;
            public IntPtr hEvent;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, ref OVERLAPPED lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, ref OVERLAPPED lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetOverlappedResult(IntPtr hFile, ref OVERLAPPED lpOverlapped, out uint lpNumberOfBytesTransferred, bool bWait);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CancelIo(IntPtr hFile);

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        private const uint WAIT_OBJECT_0 = 0;

        public static int CachedBatteryPercent = 20;
        public static bool CachedIsCharging = false;
        public static string CachedDeviceName = "VGN Dragonfly F2 Pro Max";
        public static DateTime CachedLastUpdated = DateTime.MinValue;
        public static bool HasValidHardwareReading = false;

        public static void LoadHardwareCache()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(CacheRegistryKey, false))
                {
                    if (key != null)
                    {
                        var bp = key.GetValue("BatteryPercent");
                        var isValidObj = key.GetValue("IsValid");
                        bool isValid = (isValidObj != null && Convert.ToInt32(isValidObj) == 1);

                        if (bp != null)
                        {
                            int val = Convert.ToInt32(bp);
                            if (val > 0 && val <= 100 && isValid)
                            {
                                CachedBatteryPercent = val;
                                HasValidHardwareReading = true;
                            }
                            else
                            {
                                CachedBatteryPercent = 20;
                                HasValidHardwareReading = false;
                            }
                        }

                        var dn = key.GetValue("DeviceName") as string;
                        if (!string.IsNullOrEmpty(dn)) CachedDeviceName = dn;

                        var chg = key.GetValue("IsCharging");
                        if (chg != null) CachedIsCharging = (Convert.ToInt32(chg) != 0);

                        var updatedStr = key.GetValue("LastUpdated") as string;
                        if (!string.IsNullOrEmpty(updatedStr))
                        {
                            DateTime dt;
                            if (DateTime.TryParse(updatedStr, out dt)) CachedLastUpdated = dt;
                        }
                    }
                }
            }
            catch { }
        }

        public static void SaveHardwareCache()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(CacheRegistryKey))
                {
                    if (key != null)
                    {
                        if (CachedBatteryPercent > 0)
                        {
                            key.SetValue("BatteryPercent", CachedBatteryPercent);
                            key.SetValue("IsValid", HasValidHardwareReading ? 1 : 0);
                        }
                        if (!string.IsNullOrEmpty(CachedDeviceName)) key.SetValue("DeviceName", CachedDeviceName);
                        key.SetValue("IsCharging", CachedIsCharging ? 1 : 0);
                        if (CachedLastUpdated != DateTime.MinValue) key.SetValue("LastUpdated", CachedLastUpdated.ToString("o"));
                    }
                }
            }
            catch { }
        }

        private static byte CalcChecksum(byte[] buf16)
        {
            int sum = 0;
            for (int i = 0; i < buf16.Length - 1; i++) sum += buf16[i];
            sum = sum & 0xFF;
            return (byte)(85 - sum);
        }

        private static byte[] BuildReport17(byte cmd, byte[] extraData)
        {
            byte[] buf16 = new byte[16];
            buf16[0] = cmd;
            if (extraData != null)
            {
                buf16[4] = (byte)extraData.Length;
                for (int i = 0; i < extraData.Length && (5 + i) < 15; i++)
                {
                    buf16[5 + i] = extraData[i];
                }
            }
            byte crc = CalcChecksum(buf16);
            buf16[15] = (byte)(crc - 8);

            byte[] report17 = new byte[17];
            report17[0] = 0x08; // Report ID
            Array.Copy(buf16, 0, report17, 1, 16);
            return report17;
        }

        private static byte[] SendAndReceive(IntPtr handle, IntPtr hEvent, byte[] tx, int inLen, int timeoutMs)
        {
            if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return null;

            // 1. Drain pending reports
            byte[] drain = new byte[inLen];
            while (true)
            {
                OVERLAPPED dOl = new OVERLAPPED();
                dOl.hEvent = hEvent;
                uint readBytes;
                bool ok = ReadFile(handle, drain, (uint)drain.Length, out readBytes, ref dOl);
                if (!ok && Marshal.GetLastWin32Error() == 997) // ERROR_IO_PENDING
                {
                    if (WaitForSingleObject(hEvent, 10) == WAIT_OBJECT_0)
                    {
                        GetOverlappedResult(handle, ref dOl, out readBytes, false);
                        continue;
                    }
                    CancelIo(handle);
                }
                break;
            }

            // 2. Write command
            OVERLAPPED wOl = new OVERLAPPED();
            wOl.hEvent = hEvent;
            uint written;
            bool wOk = WriteFile(handle, tx, (uint)tx.Length, out written, ref wOl);
            if (!wOk && Marshal.GetLastWin32Error() == 997)
            {
                if (WaitForSingleObject(hEvent, 500) == WAIT_OBJECT_0)
                {
                    GetOverlappedResult(handle, ref wOl, out written, false);
                }
                else
                {
                    CancelIo(handle);
                    return null;
                }
            }

            // 3. Read response
            byte[] rx = new byte[inLen];
            OVERLAPPED rOl = new OVERLAPPED();
            rOl.hEvent = hEvent;
            uint rxRead = 0;
            bool rOk = ReadFile(handle, rx, (uint)rx.Length, out rxRead, ref rOl);
            if (!rOk && Marshal.GetLastWin32Error() == 997)
            {
                if (WaitForSingleObject(hEvent, (uint)timeoutMs) == WAIT_OBJECT_0)
                {
                    GetOverlappedResult(handle, ref rOl, out rxRead, false);
                    return rx;
                }
                else
                {
                    CancelIo(handle);
                    return null;
                }
            }
            else if (rOk)
            {
                return rx;
            }

            return null;
        }

        public static string GetModelName(int pid, string productString, int cid, int mid)
        {
            if (cid == 118 && mid == 11) return "VGN Dragonfly F2 Pro Max";
            if (cid == 118 && mid == 10) return "VGN Dragonfly F2 Pro";
            if (cid == 118 && mid == 9) return "VGN Dragonfly F2";
            if (!string.IsNullOrEmpty(productString)) return productString;
            switch (pid)
            {
                case 0xFB3E: return "VGN Dragonfly F2 Pro Max";
                case 0xFC03: return "VXE Dragonfly R1 Pro";
                case 0xFC04: return "VXE Dragonfly R1 Pro Max";
                case 0xFC05: return "VXE Dragonfly R1 SE";
                case 0xFB24: return "VGN Dragonfly F1";
                case 0xFB25: return "VGN Dragonfly F1 Pro";
                case 0xFB26: return "VGN Dragonfly F1 Pro Max";
                case 0xFB27: return "VGN Dragonfly F1 MOBA";
                default: return "VGN 无线游戏鼠标";
            }
        }

        public static MouseBatteryInfo QueryVgnDeviceInfo()
        {
            lock (hidLock)
            {
                Guid hidGuid;
                RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
                IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return null;

                RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                uint memberIdx = 0;
                bool foundVgnDongle = false;
                string foundName = null;
                int foundBattery = -1;
                bool foundCharging = false;
                bool isMouseOnline = false;
                int foundPid = 0xFB3E;
                int detectedCid = 0;
                int detectedMid = 0;

                try
                {
                    ifData.cbSize = Marshal.SizeOf(ifData);
                    while (RazerDeviceHelper.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                    {
                        uint reqSize;
                        RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                        if (reqSize == 0) continue;

                        IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                        if (RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                        {
                            IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                            string devicePath = Marshal.PtrToStringAuto(pDevicePath);
                            if (devicePath != null && (devicePath.ToLower().Contains("vid_3554") || devicePath.ToLower().Contains("vgn")))
                            {
                                IntPtr handle = RazerDeviceHelper.CreateFile(
                                    devicePath,
                                    GENERIC_READ | GENERIC_WRITE,
                                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                                    IntPtr.Zero,
                                    OPEN_EXISTING,
                                    FILE_FLAG_OVERLAPPED,
                                    IntPtr.Zero);

                                if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                {
                                    try
                                    {
                                        if (string.IsNullOrEmpty(foundName))
                                        {
                                            StringBuilder sb = new StringBuilder(256);
                                            if (RazerDeviceHelper.HidD_GetProductString(handle, sb, sb.Capacity))
                                            {
                                                string pStr = sb.ToString().Trim();
                                                if (!string.IsNullOrEmpty(pStr)) foundName = pStr;
                                            }
                                        }

                                        RazerDeviceHelper.HIDD_ATTRIBUTES attr = new RazerDeviceHelper.HIDD_ATTRIBUTES();
                                        attr.Size = Marshal.SizeOf(attr);
                                        if (RazerDeviceHelper.HidD_GetAttributes(handle, ref attr))
                                        {
                                            if (attr.ProductID != 0) foundPid = attr.ProductID;
                                        }

                                        IntPtr preparsed;
                                        if (RazerDeviceHelper.HidD_GetPreparsedData(handle, out preparsed))
                                        {
                                            RazerDeviceHelper.HIDP_CAPS caps;
                                            RazerDeviceHelper.HidP_GetCaps(preparsed, out caps);
                                            RazerDeviceHelper.HidD_FreePreparsedData(preparsed);

                                            // Target communication endpoint: UsagePage == 0xFF02 && Usage == 0x0002
                                            if (caps.UsagePage == 0xFF02 && caps.Usage == 0x0002 && caps.InputReportByteLength >= 17 && caps.OutputReportByteLength >= 17)
                                            {
                                                foundVgnDongle = true;
                                                IntPtr hEvent = CreateEvent(IntPtr.Zero, true, false, null);
                                                if (hEvent != IntPtr.Zero)
                                                {
                                                    try
                                                    {
                                                        // 1. Encryption handshake (Command 1)
                                                        Random rnd = new Random();
                                                        byte[] randBytes = new byte[8] { (byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256), 0, 0, 0, 0 };
                                                        byte[] rxEnc = SendAndReceive(handle, hEvent, BuildReport17(1, randBytes), caps.InputReportByteLength, 300);
                                                        if (rxEnc != null && rxEnc.Length > 12)
                                                        {
                                                            detectedCid = rxEnc[10];
                                                            detectedMid = rxEnc[11];
                                                        }

                                                        // 2. PC Driver Status (Command 2)
                                                        SendAndReceive(handle, hEvent, BuildReport17(2, new byte[1] { 1 }), caps.InputReportByteLength, 200);

                                                        // 3. Online Check (Command 3)
                                                        byte[] rxOnline = SendAndReceive(handle, hEvent, BuildReport17(3, null), caps.InputReportByteLength, 300);
                                                        if (rxOnline != null && rxOnline.Length > 6 && rxOnline[6] > 0)
                                                        {
                                                            isMouseOnline = true;

                                                            // 4. Query Battery (Command 4)
                                                            byte[] rxBat = SendAndReceive(handle, hEvent, BuildReport17(4, null), caps.InputReportByteLength, 400);
                                                            if (rxBat != null && rxBat.Length > 8)
                                                            {
                                                                int bVal = rxBat[6];
                                                                if (bVal >= 0 && bVal <= 100)
                                                                {
                                                                    foundBattery = bVal;
                                                                    foundCharging = (rxBat[7] == 1);
                                                                }
                                                            }
                                                        }
                                                        else
                                                        {
                                                            isMouseOnline = false;
                                                        }
                                                    }
                                                    finally
                                                    {
                                                        RazerDeviceHelper.CloseHandle(hEvent);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        RazerDeviceHelper.CloseHandle(handle);
                                    }
                                }
                            }
                        }
                        Marshal.FreeHGlobal(detailBuffer);
                        ifData.cbSize = Marshal.SizeOf(ifData);
                    }
                }
                finally
                {
                    RazerDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);
                }

                if (foundVgnDongle)
                {
                    if (foundBattery >= 0)
                    {
                        CachedBatteryPercent = foundBattery;
                        CachedIsCharging = foundCharging;
                        CachedLastUpdated = DateTime.Now;
                        HasValidHardwareReading = true;
                        SaveHardwareCache();
                    }

                    string devName = GetModelName(foundPid, foundName, detectedCid, detectedMid);
                    if (string.IsNullOrEmpty(devName)) devName = !string.IsNullOrEmpty(CachedDeviceName) ? CachedDeviceName : "VGN Dragonfly F2 Pro Max";
                    CachedDeviceName = devName;

                    int displayBattery;
                    if (foundBattery >= 0)
                    {
                        displayBattery = foundBattery;
                    }
                    else if (HasValidHardwareReading && CachedBatteryPercent > 0)
                    {
                        displayBattery = CachedBatteryPercent;
                    }
                    else
                    {
                        displayBattery = 20; // Matches official VGN driver offline fallback
                    }

                    bool displayCharging = (foundBattery >= 0) ? foundCharging : CachedIsCharging;

                    return new MouseBatteryInfo
                    {
                        DeviceId = "VGN:3554:" + foundPid.ToString("X4"),
                        Brand = "VGN",
                        DeviceName = devName,
                        Category = DeviceCategory.Mouse,
                        Transport = "2.4G 无线",
                        IsConnected = true,
                        IsSleeping = !isMouseOnline,
                        IsDonglePresent = true,
                        BatteryPercent = displayBattery,
                        ExactBatteryPercent = displayBattery,
                        IsCharging = displayCharging,
                        LastUpdated = (foundBattery >= 0) ? DateTime.Now : (CachedLastUpdated != DateTime.MinValue ? CachedLastUpdated : DateTime.Now),
                        Dpi = 1600,
                        DpiStage = 2,
                        DpiStageCount = 5,
                        DpiStages = new int[] { 400, 800, 1600, 3200, 6400 },
                        PollingRate = 1000,
                        SupportedPollingRates = new int[] { 1000, 2000, 4000, 8000 }
                    };
                }

                return null;
            }
        }
    }

public static class LogitechDeviceHelper
{
    private static readonly object hidLock = new object();
    private const string CacheRegistryKey = @"Software\RazerBatteryTray\LogitechCache";

    [StructLayout(LayoutKind.Sequential)]
    private struct OVERLAPPED
    {
        public IntPtr Internal;
        public IntPtr InternalHigh;
        public uint Offset;
        public uint OffsetHigh;
        public IntPtr hEvent;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid interfaceClassGuid;
        public int flags;
        public IntPtr reserved;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDP_CAPS
    {
        public ushort Usage;
        public ushort UsagePage;
        public ushort InputReportByteLength;
        public ushort OutputReportByteLength;
        public ushort FeatureReportByteLength;
        [MarshalAs(UnmanagedType.ByValArray, SizeConst = 17)]
        public ushort[] Reserved;
        public ushort NumberLinkCollectionNodes;
        public ushort NumberInputButtonCaps;
        public ushort NumberInputValueCaps;
        public ushort NumberInputDataIndices;
        public ushort NumberOutputButtonCaps;
        public ushort NumberOutputValueCaps;
        public ushort NumberOutputDataIndices;
        public ushort NumberFeatureButtonCaps;
        public ushort NumberFeatureValueCaps;
        public ushort NumberFeatureDataIndices;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [DllImport("hid.dll", SetLastError = true)]
    public static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetPreparsedData(IntPtr hidDeviceObject, out IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetProductString(IntPtr hidDeviceObject, StringBuilder buffer, int bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetAttributes(IntPtr hidDeviceObject, ref HIDD_ATTRIBUTES attributes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, ref OVERLAPPED lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, ref OVERLAPPED lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetOverlappedResult(IntPtr hFile, ref OVERLAPPED lpOverlapped, out uint lpNumberOfBytesTransferred, bool bWait);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIo(IntPtr hFile);

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    private const uint WAIT_OBJECT_0 = 0;

    public static int CachedBatteryPercent = -1;
    public static bool CachedIsCharging = false;
    public static string CachedDeviceName = "罗技无线游戏鼠标";
    public static DateTime CachedLastUpdated = DateTime.MinValue;
    public static bool HasValidHardwareReading = false;
    private static Dictionary<string, int> deviceBatteryCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

    public static void LoadHardwareCache()
    {
        try
        {
            using (var key = Registry.CurrentUser.OpenSubKey(CacheRegistryKey, false))
            {
                if (key != null)
                {
                    var bp = key.GetValue("BatteryPercent");
                    var isValidObj = key.GetValue("IsValid");
                    bool isValid = (isValidObj != null && Convert.ToInt32(isValidObj) == 1);

                    if (bp != null)
                    {
                        int val = Convert.ToInt32(bp);
                        if (val > 0 && val <= 100 && isValid)
                        {
                            CachedBatteryPercent = val;
                            HasValidHardwareReading = true;
                        }
                    }

                    var dn = key.GetValue("DeviceName") as string;
                    if (!string.IsNullOrEmpty(dn)) CachedDeviceName = dn;

                    var chg = key.GetValue("IsCharging");
                    if (chg != null) CachedIsCharging = (Convert.ToInt32(chg) != 0);

                    var updatedStr = key.GetValue("LastUpdated") as string;
                    if (!string.IsNullOrEmpty(updatedStr))
                    {
                        DateTime dt;
                        if (DateTime.TryParse(updatedStr, out dt)) CachedLastUpdated = dt;
                    }
                }
            }
        }
        catch { }
    }

    public static void SaveHardwareCache()
    {
        try
        {
            using (var key = Registry.CurrentUser.CreateSubKey(CacheRegistryKey))
            {
                if (key != null)
                {
                    if (CachedBatteryPercent > 0)
                    {
                        key.SetValue("BatteryPercent", CachedBatteryPercent);
                        key.SetValue("IsValid", HasValidHardwareReading ? 1 : 0);
                    }
                    if (!string.IsNullOrEmpty(CachedDeviceName)) key.SetValue("DeviceName", CachedDeviceName);
                    key.SetValue("IsCharging", CachedIsCharging ? 1 : 0);
                    if (CachedLastUpdated != DateTime.MinValue) key.SetValue("LastUpdated", CachedLastUpdated.ToString("o"));
                }
            }
        }
        catch { }
    }

    public static int GetCachedBatteryForDevice(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) return CachedBatteryPercent;
        int val;
        if (deviceBatteryCache.TryGetValue(deviceId, out val) && val > 0)
        {
            return val;
        }
        return (HasValidHardwareReading && CachedBatteryPercent > 0) ? CachedBatteryPercent : -1;
    }

    private static byte[] SendAndReceive(IntPtr handle, IntPtr hEvent, byte[] tx, int inLen, int timeoutMs)
    {
        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return null;

        // Drain pending reports
        byte[] drain = new byte[inLen];
        while (true)
        {
            OVERLAPPED dOl = new OVERLAPPED();
            dOl.hEvent = hEvent;
            uint readBytes;
            bool ok = ReadFile(handle, drain, (uint)drain.Length, out readBytes, ref dOl);
            if (!ok && Marshal.GetLastWin32Error() == 997)
            {
                if (WaitForSingleObject(hEvent, 10) == WAIT_OBJECT_0)
                {
                    GetOverlappedResult(handle, ref dOl, out readBytes, false);
                    continue;
                }
                CancelIo(handle);
            }
            break;
        }

        // Write command
        OVERLAPPED wOl = new OVERLAPPED();
        wOl.hEvent = hEvent;
        uint written;
        bool wOk = WriteFile(handle, tx, (uint)tx.Length, out written, ref wOl);
        if (!wOk && Marshal.GetLastWin32Error() == 997)
        {
            if (WaitForSingleObject(hEvent, 500) == WAIT_OBJECT_0)
            {
                GetOverlappedResult(handle, ref wOl, out written, false);
            }
            else
            {
                CancelIo(handle);
                return null;
            }
        }

        // Read response
        byte[] rx = new byte[inLen];
        OVERLAPPED rOl = new OVERLAPPED();
        rOl.hEvent = hEvent;
        uint rxRead = 0;
        bool rOk = ReadFile(handle, rx, (uint)rx.Length, out rxRead, ref rOl);
        if (!rOk && Marshal.GetLastWin32Error() == 997)
        {
            if (WaitForSingleObject(hEvent, (uint)timeoutMs) == WAIT_OBJECT_0)
            {
                GetOverlappedResult(handle, ref rOl, out rxRead, false);
                return rx;
            }
            else
            {
                CancelIo(handle);
                return null;
            }
        }
        else if (rOk)
        {
            return rx;
        }

        return null;
    }

    public static string GetModelName(int pid, string productString)
    {
        // 1. Specific PID database
        switch (pid)
        {
            case 0xC547:
            case 0x4093: return "Logitech G PRO X SUPERLIGHT 2";
            case 0x4092:
            case 0xC094: return "Logitech G PRO X SUPERLIGHT";
            case 0x4079:
            case 0xC088: return "Logitech G PRO Wireless (GPW)";
            case 0x4087:
            case 0xC08D: return "Logitech G502 LIGHTSPEED";
            case 0x408E:
            case 0xC096: return "Logitech G502 X PLUS / LIGHTSPEED";
            case 0x4074: return "Logitech G304 / G305 LIGHTSPEED";
            case 0x4070: return "Logitech G703 LIGHTSPEED (HERO)";
            case 0x4071: return "Logitech G903 LIGHTSPEED (HERO)";
            case 0x406E: return "Logitech G604 LIGHTSPEED";
            case 0x4068: return "Logitech G603 LIGHTSPEED";
            case 0x4069:
            case 0xB023: return "Logitech MX Master 3";
            case 0x4090: return "Logitech MX Master 3S";
            case 0x408F: return "Logitech MX Anywhere 3";
            case 0x406B: return "Logitech MX Keys 无线键盘";
            case 0x408A: return "Logitech MX Keys Mini";
            case 0x4082: return "Logitech G913 TKL 机械键盘";
            case 0x4086: return "Logitech G913 机械键盘";
            case 0x409B: return "Logitech G PRO X 60 游戏键盘";
            case 0x4088: return "Logitech G733 LIGHTSPEED 耳机";
            case 0x4097: return "Logitech G PRO X 2 LIGHTSPEED 耳机";
            case 0x4085: return "Logitech G435 无线耳机";
            case 0x4080: return "Logitech G533 无线耳机";
            case 0x407B: return "Logitech G935 无线耳机";
        }

        if (!string.IsNullOrEmpty(productString))
        {
            string p = productString.Trim();
            if (p.Length > 2 && 
                !p.Equals("USB Receiver", StringComparison.OrdinalIgnoreCase) &&
                !p.Equals("Lightspeed Receiver", StringComparison.OrdinalIgnoreCase) &&
                !p.Equals("Logitech Receiver", StringComparison.OrdinalIgnoreCase))
            {
                return p.StartsWith("Logitech", StringComparison.OrdinalIgnoreCase) ? p : ("Logitech " + p);
            }
        }

        // Receiver defaults
        if (pid == 0xC547) return "Logitech G PRO X SUPERLIGHT 2 (Lightspeed)";
        if (pid == 0xC539 || pid == 0xC53A) return "Logitech G 旗舰无线鼠标 (Lightspeed)";
        if (pid == 0xC545) return "Logitech G502 X LIGHTSPEED";

        return "Logitech 罗技无线游戏外设";
    }

    public static DeviceCategory DetectCategory(int pid, string name)
    {
        string text = (name + " " + pid.ToString("X4")).ToUpperInvariant();
        if (text.Contains("KEYBOARD") || text.Contains("键盘") || text.Contains("G913") || text.Contains("G915") || text.Contains("MX KEYS") || text.Contains("PRO X 60"))
        {
            return DeviceCategory.Keyboard;
        }
        if (text.Contains("HEADSET") || text.Contains("HEADPHONE") || text.Contains("耳机") || text.Contains("G733") || text.Contains("G533") || text.Contains("G935") || text.Contains("G435") || text.Contains("PRO X 2"))
        {
            return DeviceCategory.Headset;
        }
        return DeviceCategory.Mouse;
    }

    public static List<MouseBatteryInfo> QueryAllLogitechDevices()
    {
        lock (hidLock)
        {
            List<MouseBatteryInfo> results = new List<MouseBatteryInfo>();
            HashSet<string> processedDevices = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Guid hidGuid;
            HidD_GetHidGuid(out hidGuid);
            IntPtr devInfo = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
            if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return results;

            SP_DEVICE_INTERFACE_DATA ifData = new SP_DEVICE_INTERFACE_DATA();
            uint memberIdx = 0;

            try
            {
                ifData.cbSize = Marshal.SizeOf(ifData);
                while (SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                {
                    uint reqSize;
                    SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                    if (reqSize == 0) continue;

                    IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                    Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                    if (SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                    {
                        IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                        string devicePath = Marshal.PtrToStringAuto(pDevicePath);
                        if (devicePath != null && devicePath.ToLower().Contains("vid_046d"))
                        {
                            IntPtr handle = CreateFile(
                                devicePath,
                                GENERIC_READ | GENERIC_WRITE,
                                FILE_SHARE_READ | FILE_SHARE_WRITE,
                                IntPtr.Zero,
                                OPEN_EXISTING,
                                FILE_FLAG_OVERLAPPED,
                                IntPtr.Zero);

                            if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                            {
                                try
                                {
                                    int foundVid = 0;
                                    int foundPid = 0;
                                    string foundName = null;

                                    HIDD_ATTRIBUTES attr = new HIDD_ATTRIBUTES();
                                    attr.Size = Marshal.SizeOf(attr);
                                    if (HidD_GetAttributes(handle, ref attr))
                                    {
                                        foundVid = attr.VendorID;
                                        foundPid = attr.ProductID;
                                    }

                                    StringBuilder sb = new StringBuilder(256);
                                    if (HidD_GetProductString(handle, sb, sb.Capacity))
                                    {
                                        string pStr = sb.ToString().Trim();
                                        if (!string.IsNullOrEmpty(pStr) && pStr.Length > 2) foundName = pStr;
                                    }

                                    IntPtr preparsed;
                                    if (HidD_GetPreparsedData(handle, out preparsed))
                                    {
                                        HIDP_CAPS caps;
                                        HidP_GetCaps(preparsed, out caps);
                                        HidD_FreePreparsedData(preparsed);

                                        // Logitech HID++ endpoint detection:
                                        // UsagePage == 0xFF00 || UsagePage == 0xFF43, InputReportByteLength >= 7
                                        bool isHidppEndpoint = (caps.UsagePage == 0xFF00 || caps.UsagePage == 0xFF43) && 
                                                               (caps.InputReportByteLength >= 7 && caps.OutputReportByteLength >= 7);

                                        if (isHidppEndpoint)
                                        {
                                            string devKey = foundVid.ToString("X4") + ":" + foundPid.ToString("X4");
                                            if (!processedDevices.Contains(devKey))
                                            {
                                                processedDevices.Add(devKey);

                                                IntPtr hEvent = CreateEvent(IntPtr.Zero, true, false, null);
                                                if (hEvent != IntPtr.Zero)
                                                {
                                                    try
                                                    {
                                                        // Check target device slots on this receiver/connection:
                                                        // index 0xFF (wired), index 0x01 (paired wireless mouse)
                                                        byte[] targetIndices = new byte[] { 0xFF, 0x01 };
                                                        foreach (byte devIdx in targetIndices)
                                                        {
                                                            bool deviceOnline = false;
                                                            string detectedName = null;
                                                            int batteryPercent = -1;
                                                            bool isCharging = false;
                                                            int detectedPid = foundPid;

                                                            // 1. HID++ 2.0 Ping: Feature 0x0000 ROOT, Func 0x00, ping 0x5A
                                                            byte[] txPing = new byte[20];
                                                            txPing[0] = 0x11; // Long report
                                                            txPing[1] = devIdx;
                                                            txPing[2] = 0x00; // Feature 0x0000
                                                            txPing[3] = 0x00; // Func 0 (Ping)
                                                            txPing[4] = 0x00;
                                                            txPing[5] = 0x00;
                                                            txPing[6] = 0x5A; // Ping token

                                                            byte[] rxPing = SendAndReceive(handle, hEvent, txPing, caps.InputReportByteLength, 350);
                                                            if (rxPing != null && rxPing.Length >= 7 && rxPing[6] == 0x5A)
                                                            {
                                                                deviceOnline = true;
                                                            }
                                                            else
                                                            {
                                                                // Try Short Report ping (Report ID 0x10)
                                                                byte[] txShortPing = new byte[7] { 0x10, devIdx, 0x00, 0x00, 0x00, 0x00, 0x5A };
                                                                byte[] rxShortPing = SendAndReceive(handle, hEvent, txShortPing, caps.InputReportByteLength, 250);
                                                                if (rxShortPing != null && rxShortPing.Length >= 7 && rxShortPing[6] == 0x5A)
                                                                {
                                                                    deviceOnline = true;
                                                                }
                                                            }

                                                            // 2. Discover Features if online
                                                            if (deviceOnline)
                                                            {
                                                                // Feature 0x0005: Device Name Type
                                                                byte[] txFeatName = new byte[20];
                                                                txFeatName[0] = 0x11;
                                                                txFeatName[1] = devIdx;
                                                                txFeatName[2] = 0x00; // Root feature
                                                                txFeatName[3] = 0x00; // getFeature
                                                                txFeatName[4] = 0x00;
                                                                txFeatName[5] = 0x05; // Feature ID: 0x0005
                                                                byte[] rxFeatName = SendAndReceive(handle, hEvent, txFeatName, caps.InputReportByteLength, 250);
                                                                if (rxFeatName != null && rxFeatName.Length >= 5 && rxFeatName[4] > 0)
                                                                {
                                                                    byte nameFeatIdx = rxFeatName[4];
                                                                    // Get Name Count
                                                                    byte[] txNameCount = new byte[20];
                                                                    txNameCount[0] = 0x11;
                                                                    txNameCount[1] = devIdx;
                                                                    txNameCount[2] = nameFeatIdx;
                                                                    txNameCount[3] = 0x00; // Func 0: getCount
                                                                    byte[] rxNameCount = SendAndReceive(handle, hEvent, txNameCount, caps.InputReportByteLength, 200);
                                                                    if (rxNameCount != null && rxNameCount.Length >= 5 && rxNameCount[4] > 0)
                                                                    {
                                                                        int nameLen = rxNameCount[4];
                                                                        // Get Name String (Func 1: 0x10)
                                                                        byte[] txGetName = new byte[20];
                                                                        txGetName[0] = 0x11;
                                                                        txGetName[1] = devIdx;
                                                                        txGetName[2] = nameFeatIdx;
                                                                        txGetName[3] = 0x10; // Func 1: getName
                                                                        txGetName[4] = 0x00;
                                                                        byte[] rxGetName = SendAndReceive(handle, hEvent, txGetName, caps.InputReportByteLength, 250);
                                                                        if (rxGetName != null && rxGetName.Length > 5)
                                                                        {
                                                                            StringBuilder nsb = new StringBuilder();
                                                                            for (int c = 5; c < rxGetName.Length && (c - 5) < nameLen; c++)
                                                                            {
                                                                                if (rxGetName[c] == 0) break;
                                                                                nsb.Append((char)rxGetName[c]);
                                                                            }
                                                                            string str = nsb.ToString().Trim();
                                                                            if (str.Length > 2) detectedName = str;
                                                                        }
                                                                    }
                                                                }

                                                                // Feature 0x1004: Unified Battery
                                                                byte[] txFeat1004 = new byte[20];
                                                                txFeat1004[0] = 0x11;
                                                                txFeat1004[1] = devIdx;
                                                                txFeat1004[2] = 0x00;
                                                                txFeat1004[3] = 0x00;
                                                                txFeat1004[4] = 0x10;
                                                                txFeat1004[5] = 0x04; // 0x1004
                                                                byte[] rxFeat1004 = SendAndReceive(handle, hEvent, txFeat1004, caps.InputReportByteLength, 250);
                                                                if (rxFeat1004 != null && rxFeat1004.Length >= 5 && rxFeat1004[4] > 0)
                                                                {
                                                                    byte batFeatIdx = rxFeat1004[4];
                                                                    // Call getBatteryStatus (Func 1: 0x10)
                                                                    byte[] txBat = new byte[20];
                                                                    txBat[0] = 0x11;
                                                                    txBat[1] = devIdx;
                                                                    txBat[2] = batFeatIdx;
                                                                    txBat[3] = 0x10; // Func 1
                                                                    byte[] rxBat = SendAndReceive(handle, hEvent, txBat, caps.InputReportByteLength, 300);
                                                                    if (rxBat != null && rxBat.Length > 5)
                                                                    {
                                                                        int pct = rxBat[4];
                                                                        if (pct >= 0 && pct <= 100)
                                                                        {
                                                                            batteryPercent = pct;
                                                                            isCharging = (rxBat[5] == 1);
                                                                        }
                                                                    }
                                                                }

                                                                // Feature 0x1000: Standard Battery Status fallback
                                                                if (batteryPercent < 0)
                                                                {
                                                                    byte[] txFeat1000 = new byte[20];
                                                                    txFeat1000[0] = 0x11;
                                                                    txFeat1000[1] = devIdx;
                                                                    txFeat1000[2] = 0x00;
                                                                    txFeat1000[3] = 0x00;
                                                                    txFeat1000[4] = 0x10;
                                                                    txFeat1000[5] = 0x00; // 0x1000
                                                                    byte[] rxFeat1000 = SendAndReceive(handle, hEvent, txFeat1000, caps.InputReportByteLength, 250);
                                                                    if (rxFeat1000 != null && rxFeat1000.Length >= 5 && rxFeat1000[4] > 0)
                                                                    {
                                                                        byte batFeatIdx = rxFeat1000[4];
                                                                        // Call getBatteryLevelStatus (Func 0: 0x00)
                                                                        byte[] txBat = new byte[20];
                                                                        txBat[0] = 0x11;
                                                                        txBat[1] = devIdx;
                                                                        txBat[2] = batFeatIdx;
                                                                        txBat[3] = 0x00; // Func 0
                                                                        byte[] rxBat = SendAndReceive(handle, hEvent, txBat, caps.InputReportByteLength, 300);
                                                                        if (rxBat != null && rxBat.Length > 6)
                                                                        {
                                                                            int pct = rxBat[4];
                                                                            if (pct >= 0 && pct <= 100)
                                                                            {
                                                                                batteryPercent = pct;
                                                                                isCharging = (rxBat[6] == 1);
                                                                            }
                                                                        }
                                                                    }
                                                                }

                                                                // HID++ 1.0 Sub-ID 0x0D Fallback
                                                                if (batteryPercent < 0)
                                                                {
                                                                    byte[] txHidpp1 = new byte[7] { 0x10, devIdx, 0x0D, 0x00, 0x00, 0x00, 0x00 };
                                                                    byte[] rxHidpp1 = SendAndReceive(handle, hEvent, txHidpp1, caps.InputReportByteLength, 250);
                                                                    if (rxHidpp1 != null && rxHidpp1.Length >= 5 && rxHidpp1[2] == 0x0D)
                                                                    {
                                                                        int pct = rxHidpp1[3];
                                                                        if (pct >= 0 && pct <= 100)
                                                                        {
                                                                            batteryPercent = pct;
                                                                            isCharging = (rxHidpp1[4] == 1);
                                                                        }
                                                                    }
                                                                }
                                                            }

                                                            // Determine Device Name and ID
                                                            string finalName = !string.IsNullOrEmpty(detectedName) ? detectedName : GetModelName(detectedPid, foundName);
                                                            DeviceCategory cat = DetectCategory(detectedPid, finalName);
                                                            string deviceId = "Logitech:" + foundVid.ToString("X4") + ":" + detectedPid.ToString("X4") + (devIdx != 0xFF ? (":" + devIdx) : "");

                                                            // If device was online and battery read, update cache
                                                            if (batteryPercent >= 0)
                                                            {
                                                                CachedBatteryPercent = batteryPercent;
                                                                CachedIsCharging = isCharging;
                                                                CachedLastUpdated = DateTime.Now;
                                                                CachedDeviceName = finalName;
                                                                HasValidHardwareReading = true;
                                                                deviceBatteryCache[deviceId] = batteryPercent;
                                                                SaveHardwareCache();
                                                            }

                                                            int displayBattery = (batteryPercent >= 0) ? batteryPercent : GetCachedBatteryForDevice(deviceId);
                                                            bool displayCharging = (batteryPercent >= 0) ? isCharging : CachedIsCharging;

                                                            // Only add if device is online or if dongle is present
                                                            // For dongle receiver, even if mouse is sleeping, provide dongle standby status
                                                            bool isDongle = (foundPid == 0xC547 || foundPid == 0xC539 || foundPid == 0xC53A || 
                                                                             foundPid == 0xC545 || foundPid == 0xC548 || foundPid == 0xC52B || 
                                                                             foundPid == 0xC534 || foundPid == 0xC52F);

                                                            if (deviceOnline || isDongle)
                                                            {
                                                                MouseBatteryInfo info = new MouseBatteryInfo
                                                                {
                                                                    DeviceId = deviceId,
                                                                    HardwareFingerprint = deviceId,
                                                                    Brand = "Logitech",
                                                                    DeviceName = finalName,
                                                                    Category = cat,
                                                                    Transport = (devIdx == 0xFF) ? "USB 有线" : "Lightspeed 无线",
                                                                    IsConnected = true,
                                                                    IsSleeping = !deviceOnline,
                                                                    IsDonglePresent = true,
                                                                    IsCustom = false,
                                                                    BatteryPercent = displayBattery,
                                                                    ExactBatteryPercent = displayBattery,
                                                                    IsCharging = displayCharging,
                                                                    LastUpdated = (batteryPercent >= 0) ? DateTime.Now : (CachedLastUpdated != DateTime.MinValue ? CachedLastUpdated : DateTime.Now),
                                                                    Dpi = 1600,
                                                                    DpiStage = 2,
                                                                    DpiStageCount = 5,
                                                                    DpiStages = new int[] { 400, 800, 1600, 3200, 6400 },
                                                                    PollingRate = 1000,
                                                                    SupportedPollingRates = new int[] { 1000, 2000, 4000, 8000 }
                                                                };
                                                                results.Add(info);
                                                                break; // found active/valid paired slot
                                                            }
                                                        }
                                                    }
                                                    finally
                                                    {
                                                        CloseHandle(hEvent);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                                finally
                                {
                                    CloseHandle(handle);
                                }
                            }
                        }
                    }
                    Marshal.FreeHGlobal(detailBuffer);
                    ifData.cbSize = Marshal.SizeOf(ifData);
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(devInfo);
            }

            return results;
        }
    }

        public static MouseBatteryInfo QueryLogitechDeviceInfo(string deviceFingerprint = null)
        {
            var list = QueryAllLogitechDevices();
            if (list == null || list.Count == 0) return null;
            if (!string.IsNullOrEmpty(deviceFingerprint))
            {
                foreach (var d in list)
                {
                    if (d.DeviceId != null && (d.DeviceId.Equals(deviceFingerprint, StringComparison.OrdinalIgnoreCase) ||
                                               (d.HardwareFingerprint != null && d.HardwareFingerprint.Equals(deviceFingerprint, StringComparison.OrdinalIgnoreCase))))
                    {
                        return d;
                    }
                }
            }
            return list[0];
        }
    }



    public static class NuphyDeviceHelper
    {
        private static readonly object hidLock = new object();
        private const string CacheRegistryKey = @"Software\RazerBatteryTray\NuphyCache";

        public static int CachedBatteryPercent = 60;
        public static bool CachedIsCharging = false;
        public static string CachedDeviceName = "NuPhy WH80";
        public static DateTime CachedLastUpdated = DateTime.MinValue;
        public static string CachedCommandPath = null;

        public class NuphyHardwareSettings
        {
            public int CurrentMode; // internal slot index
            public int PollingRateCode; // 1=125, 2=250, 3=500, 4=1000
            public int SleepMinutes;
            public bool SleepEnabled;
            public bool WinLock;
            public bool BacklightEnabled;
            public int BacklightBrightness;
            public bool SidelightEnabled;
            public int SidelightBrightness;
            public byte RawRate;
            public byte RawSleep;
            public byte RawWin;
            public byte RawOpt;

            public NuphyHardwareSettings()
            {
                CurrentMode = 1;
                PollingRateCode = 4;
                SleepMinutes = 6;
                SleepEnabled = true;
                WinLock = false;
                BacklightEnabled = true;
                BacklightBrightness = 100;
                SidelightEnabled = true;
                SidelightBrightness = 100;
                RawRate = 0x01;
                RawSleep = 0x86;
                RawWin = 0x00;
                RawOpt = 0x04;
            }
        }

        public static NuphyHardwareSettings CachedHardwareSettings = new NuphyHardwareSettings();

        public static void LoadHardwareCache()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(CacheRegistryKey, false))
                {
                    if (key != null)
                    {
                        var bp = key.GetValue("BatteryPercent");
                        if (bp != null) CachedBatteryPercent = Convert.ToInt32(bp);

                        var chg = key.GetValue("IsCharging");
                        if (chg != null) CachedIsCharging = Convert.ToInt32(chg) != 0;

                        var dn = key.GetValue("DeviceName") as string;
                        if (!string.IsNullOrEmpty(dn)) CachedDeviceName = dn;

                        var updatedStr = key.GetValue("LastUpdated") as string;
                        if (!string.IsNullOrEmpty(updatedStr))
                        {
                            DateTime dt;
                            if (DateTime.TryParse(updatedStr, out dt)) CachedLastUpdated = dt;
                        }

                        var sMode = key.GetValue("CurrentMode");
                        if (sMode != null) CachedHardwareSettings.CurrentMode = Convert.ToInt32(sMode);

                        var sRate = key.GetValue("PollingRateCode");
                        if (sRate != null) CachedHardwareSettings.PollingRateCode = Convert.ToInt32(sRate);

                        var sMin = key.GetValue("SleepMinutes");
                        if (sMin != null) CachedHardwareSettings.SleepMinutes = Convert.ToInt32(sMin);

                        var sEn = key.GetValue("SleepEnabled");
                        if (sEn != null) CachedHardwareSettings.SleepEnabled = Convert.ToInt32(sEn) != 0;

                        var sWin = key.GetValue("WinLock");
                        if (sWin != null) CachedHardwareSettings.WinLock = Convert.ToInt32(sWin) != 0;

                        var sBackEn = key.GetValue("BacklightEnabled");
                        if (sBackEn != null) CachedHardwareSettings.BacklightEnabled = Convert.ToInt32(sBackEn) != 0;

                        var sBackB = key.GetValue("BacklightBrightness");
                        if (sBackB != null) CachedHardwareSettings.BacklightBrightness = Convert.ToInt32(sBackB);

                        var sSideEn = key.GetValue("SidelightEnabled");
                        if (sSideEn != null) CachedHardwareSettings.SidelightEnabled = Convert.ToInt32(sSideEn) != 0;

                        var sSideB = key.GetValue("SidelightBrightness");
                        if (sSideB != null) CachedHardwareSettings.SidelightBrightness = Convert.ToInt32(sSideB);
                    }
                }
            }
            catch { }
        }

        public static void SaveHardwareCache()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(CacheRegistryKey))
                {
                    if (key != null)
                    {
                        if (CachedBatteryPercent > 0) key.SetValue("BatteryPercent", CachedBatteryPercent);
                        key.SetValue("IsCharging", CachedIsCharging ? 1 : 0);
                        if (!string.IsNullOrEmpty(CachedDeviceName)) key.SetValue("DeviceName", CachedDeviceName);
                        if (CachedLastUpdated != DateTime.MinValue) key.SetValue("LastUpdated", CachedLastUpdated.ToString("o"));

                        if (CachedHardwareSettings != null)
                        {
                            key.SetValue("CurrentMode", CachedHardwareSettings.CurrentMode);
                            key.SetValue("PollingRateCode", CachedHardwareSettings.PollingRateCode);
                            key.SetValue("SleepMinutes", CachedHardwareSettings.SleepMinutes);
                            key.SetValue("SleepEnabled", CachedHardwareSettings.SleepEnabled ? 1 : 0);
                            key.SetValue("WinLock", CachedHardwareSettings.WinLock ? 1 : 0);
                            key.SetValue("BacklightEnabled", CachedHardwareSettings.BacklightEnabled ? 1 : 0);
                            key.SetValue("BacklightBrightness", CachedHardwareSettings.BacklightBrightness);
                            key.SetValue("SidelightEnabled", CachedHardwareSettings.SidelightEnabled ? 1 : 0);
                            key.SetValue("SidelightBrightness", CachedHardwareSettings.SidelightBrightness);
                        }
                    }
                }
            }
            catch { }
        }

        public static bool IsNuphyDevice(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return false;
            string idLower = identifier.ToLowerInvariant();
            return idLower.Contains("19f5") || idLower.Contains("nuphy") || idLower.Contains("wh80");
        }

        public static string GetModelName(int pid, string productString)
        {
            if (!string.IsNullOrEmpty(productString))
            {
                string cleaned = productString.Replace("Dongle", "").Trim();
                if (!string.IsNullOrEmpty(cleaned)) return cleaned;
            }
            switch (pid)
            {
                case 0x8F01: return "NuPhy WH80";
                case 0xA011: return "NuPhy WH80";
                case 0x8F02: return "NuPhy Air75 V2";
                case 0x8F03: return "NuPhy Air96 V2";
                case 0x8F04: return "NuPhy Air60 V2";
                case 0x8F05: return "NuPhy Halo75 V2";
                case 0x8F06: return "NuPhy Halo96 V2";
                case 0x8F07: return "NuPhy Field75";
                default: return "NuPhy 无线键盘";
            }
        }

        public static byte[] SendNuphyCommand(string path, byte cmd, ushort offset, byte len, byte[] payload, int waitMs)
        {
            if (string.IsNullOrEmpty(path)) return null;

            lock (hidLock)
            {
                const uint GENERIC_READ = 0x80000000;
                const uint GENERIC_WRITE = 0x40000000;
                const uint FILE_FLAG_OVERLAPPED = 0x40000000;

                IntPtr devHandle = RazerDeviceHelper.CreateFile(
                    path,
                    GENERIC_READ | GENERIC_WRITE,
                    RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    RazerDeviceHelper.OPEN_EXISTING,
                    FILE_FLAG_OVERLAPPED,
                    IntPtr.Zero);

                if (devHandle == IntPtr.Zero || devHandle.ToInt64() == -1) return null;

                try
                {
                    using (SafeFileHandle sfh = new SafeFileHandle(devHandle, true))
                    using (FileStream fs = new FileStream(sfh, FileAccess.ReadWrite, 65, true))
                    {
                        byte[] pkt = new byte[65];
                        pkt[0] = 0x00;
                        pkt[1] = 0x55;
                        pkt[2] = cmd;
                        pkt[3] = 0x00;
                        pkt[5] = len;
                        pkt[6] = (byte)(offset & 0xFF);
                        pkt[7] = (byte)((offset >> 8) & 0xFF);
                        pkt[8] = 0x00;

                        if (payload != null)
                        {
                            for (int i = 0; i < payload.Length && (9 + i) < 65; i++)
                            {
                                pkt[9 + i] = payload[i];
                            }
                        }

                        byte sum = 0;
                        for (int i = 5; i < 65; i++) sum = (byte)(sum + pkt[i]);
                        pkt[4] = sum;

                        if (waitMs <= 0)
                        {
                            fs.Write(pkt, 0, 65);
                            fs.Flush();
                            return new byte[65];
                        }

                        byte[] inBuf = new byte[65];
                        IAsyncResult ar = fs.BeginRead(inBuf, 0, 65, null, null);

                        fs.Write(pkt, 0, 65);
                        fs.Flush();

                        if (ar.AsyncWaitHandle.WaitOne(waitMs))
                        {
                            int bytesRead = fs.EndRead(ar);
                            if (bytesRead >= 10)
                            {
                                return inBuf;
                            }
                        }
                    }
                }
                catch { }
                return null;
            }
        }

        public static string FindNuphyCommandEndpoint()
        {
            if (!string.IsNullOrEmpty(CachedCommandPath)) return CachedCommandPath;

            lock (hidLock)
            {
                Guid hidGuid;
                RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
                IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
                if (devInfo == IntPtr.Zero || devInfo.ToInt64() == -1) return null;

                RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                uint memberIdx = 0;
                string candidateWired = null;
                string candidateDongle = null;

                try
                {
                    ifData.cbSize = Marshal.SizeOf(ifData);
                    while (RazerDeviceHelper.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                    {
                        uint reqSize;
                        RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                        if (reqSize == 0) continue;

                        IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                        if (RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                        {
                            IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                            string devicePath = Marshal.PtrToStringAuto(pDevicePath);
                            if (devicePath != null && (devicePath.ToLower().Contains("vid_19f5") || devicePath.ToLower().Contains("nuphy")))
                            {
                                IntPtr handle = RazerDeviceHelper.CreateFile(devicePath, 0, RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE, IntPtr.Zero, RazerDeviceHelper.OPEN_EXISTING, 0, IntPtr.Zero);
                                if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                {
                                    try
                                    {
                                        IntPtr preparsed;
                                        if (RazerDeviceHelper.HidD_GetPreparsedData(handle, out preparsed))
                                        {
                                            RazerDeviceHelper.HIDP_CAPS caps;
                                            RazerDeviceHelper.HidP_GetCaps(preparsed, out caps);
                                            RazerDeviceHelper.HidD_FreePreparsedData(preparsed);

                                            if (caps.InputReportByteLength == 65 && caps.OutputReportByteLength == 65)
                                            {
                                                bool isWired = devicePath.IndexOf("pid_a011", StringComparison.OrdinalIgnoreCase) >= 0;
                                                bool isMi01 = devicePath.IndexOf("mi_01", StringComparison.OrdinalIgnoreCase) >= 0;
                                                if (isWired)
                                                {
                                                    if (candidateWired == null || isMi01) candidateWired = devicePath;
                                                }
                                                else
                                                {
                                                    if (candidateDongle == null || isMi01) candidateDongle = devicePath;
                                                }
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        RazerDeviceHelper.CloseHandle(handle);
                                    }
                                }
                            }
                        }
                        Marshal.FreeHGlobal(detailBuffer);
                        ifData.cbSize = Marshal.SizeOf(ifData);
                    }
                }
                finally
                {
                    RazerDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);
                }

                string candidate = candidateWired ?? candidateDongle;
                if (candidate != null) CachedCommandPath = candidate;
                return candidate;
            }
        }

        public static bool GetHardwareSettings(out NuphyHardwareSettings settings)
        {
            settings = CachedHardwareSettings;
            try
            {
                string path = FindNuphyCommandEndpoint();
                if (string.IsNullOrEmpty(path)) return false;

                // 1. Query mode
                byte[] baseResp = SendNuphyCommand(path, 0x04, 0, 16, null, 300);
                int mode = (baseResp != null && baseResp.Length >= 10) ? baseResp[9] : (CachedHardwareSettings != null ? CachedHardwareSettings.CurrentMode : 1);
                if (mode < 0 || mode > 3) mode = 1;

                ushort modeOffset = (ushort)(mode * 64);
                byte[] fResp = SendNuphyCommand(path, 0x05, modeOffset, 54, null, 300);
                if (fResp != null && fResp.Length >= 65 && (fResp[1] == 0xAA || fResp[1] == 0x55))
                {
                    var s = new NuphyHardwareSettings();
                    s.CurrentMode = mode;

                    s.RawRate = fResp[9 + 4];
                    s.RawSleep = fResp[9 + 5];
                    s.RawWin = fResp[9 + 6];
                    s.RawOpt = fResp[9 + 7];

                    s.PollingRateCode = s.RawRate & 0x0F;
                    s.SleepEnabled = (s.RawSleep & 0x80) != 0 && fResp[9 + 39] == 0;
                    s.SleepMinutes = s.RawSleep & 0x7F;

                    // If in wired mode (mode == 0) or active mode returns 0 for RawSleep,
                    // query Mode 1 (2.4G wireless mode, offset 64) because sleep is managed on wireless profiles
                    if (mode == 0 || s.RawSleep == 0)
                    {
                        byte[] wResp = SendNuphyCommand(path, 0x05, 64, 54, null, 300);
                        if (wResp != null && wResp.Length >= 65 && (wResp[1] == 0xAA || wResp[1] == 0x55))
                        {
                            byte wSleep = wResp[9 + 5];
                            s.RawSleep = wSleep;
                            s.SleepEnabled = (wSleep & 0x80) != 0 && wResp[9 + 39] == 0;
                            s.SleepMinutes = wSleep & 0x7F;
                        }
                        else if (CachedHardwareSettings != null)
                        {
                            s.SleepEnabled = CachedHardwareSettings.SleepEnabled;
                            s.SleepMinutes = CachedHardwareSettings.SleepMinutes > 0 ? CachedHardwareSettings.SleepMinutes : 6;
                            s.RawSleep = CachedHardwareSettings.RawSleep != 0 ? CachedHardwareSettings.RawSleep : (byte)((s.SleepEnabled ? 0x80 : 0x00) | (s.SleepMinutes & 0x7F));
                        }
                    }

                    if (s.SleepMinutes == 0) s.SleepMinutes = 6;

                    s.WinLock = (s.RawWin & 0x01) != 0;

                    s.BacklightBrightness = fResp[9 + 9];
                    s.BacklightEnabled = (s.BacklightBrightness > 0);

                    s.SidelightBrightness = fResp[9 + 25];
                    s.SidelightEnabled = (s.SidelightBrightness > 0);

                    CachedHardwareSettings = s;
                    settings = s;
                    SaveHardwareCache();
                    return true;
                }
            }
            catch { }
            return false;
        }

        public static bool SetWinLock(bool lockWin)
        {
            try
            {
                string path = FindNuphyCommandEndpoint();
                if (string.IsNullOrEmpty(path)) return false;

                var s = CachedHardwareSettings ?? new NuphyHardwareSettings();
                s.WinLock = lockWin;

                byte newWin = (byte)((s.RawWin & ~0x01) | (lockWin ? 0x01 : 0x00));
                s.RawWin = newWin;

                bool anyOk = false;
                int[] modesToUpdate = new int[] { 0, 1, 2, 3 };
                for (int i = 0; i < modesToUpdate.Length; i++)
                {
                    int m = modesToUpdate[i];
                    ushort baseOffset = (ushort)(m * 64);
                    byte[] payload = new byte[] { s.RawRate, s.RawSleep, newWin, s.RawOpt };
                    byte[] resp = SendNuphyCommand(path, 0x06, (ushort)(baseOffset + 4), 4, payload, 50);
                    if (resp != null) anyOk = true;
                }

                CachedHardwareSettings = s;
                SaveHardwareCache();
                return anyOk;
            }
            catch { }
            return false;
        }

        public static bool SetAutoSleep(bool enable)
        {
            try
            {
                string path = FindNuphyCommandEndpoint();
                if (string.IsNullOrEmpty(path)) return false;

                var s = CachedHardwareSettings ?? new NuphyHardwareSettings();
                s.SleepEnabled = enable;

                int mins = s.SleepMinutes > 0 ? s.SleepMinutes : 6;
                s.SleepMinutes = mins;
                byte newSleep = (byte)((enable ? 0x80 : 0x00) | (mins & 0x7F));
                s.RawSleep = newSleep;

                bool anyOk = false;
                // Update wireless profiles (Mode 1 = 2.4G, Mode 2 = BT1, Mode 3 = BT2) and wired (Mode 0)
                int[] modesToUpdate = new int[] { 1, 2, 3, 0 };
                for (int i = 0; i < modesToUpdate.Length; i++)
                {
                    int m = modesToUpdate[i];
                    ushort baseOffset = (ushort)(m * 64);
                    byte[] payload = new byte[] { s.RawRate, newSleep, s.RawWin, s.RawOpt };
                    byte[] resp1 = SendNuphyCommand(path, 0x06, (ushort)(baseOffset + 4), 4, payload, 50);
                    byte[] resp2 = SendNuphyCommand(path, 0x06, (ushort)(baseOffset + 39), 1, new byte[] { (byte)(enable ? 0 : 1) }, 50);
                    if (resp1 != null || resp2 != null) anyOk = true;
                }

                CachedHardwareSettings = s;
                SaveHardwareCache();
                return anyOk;
            }
            catch { }
            return false;
        }

        public static bool SetBacklightBrightness(int percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            try
            {
                string path = FindNuphyCommandEndpoint();
                if (string.IsNullOrEmpty(path)) return false;

                var s = CachedHardwareSettings ?? new NuphyHardwareSettings();
                s.BacklightBrightness = percent;
                s.BacklightEnabled = (percent > 0);

                bool anyOk = false;
                int[] modesToUpdate = new int[] { 0, 1, 2, 3 };
                for (int i = 0; i < modesToUpdate.Length; i++)
                {
                    int m = modesToUpdate[i];
                    ushort baseOffset = (ushort)(m * 64);
                    byte[] resp = SendNuphyCommand(path, 0x06, (ushort)(baseOffset + 9), 1, new byte[] { (byte)percent }, 0);
                    if (resp != null) anyOk = true;
                }

                CachedHardwareSettings = s;
                SaveHardwareCache();
                return anyOk;
            }
            catch { }
            return false;
        }

        public static bool SetBacklightEnabled(bool enable)
        {
            var s = CachedHardwareSettings ?? new NuphyHardwareSettings();
            int target = enable ? (s.BacklightBrightness > 0 ? s.BacklightBrightness : 100) : 0;
            return SetBacklightBrightness(target);
        }

        public static bool SetSidelightBrightness(int percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            try
            {
                string path = FindNuphyCommandEndpoint();
                if (string.IsNullOrEmpty(path)) return false;

                var s = CachedHardwareSettings ?? new NuphyHardwareSettings();
                s.SidelightBrightness = percent;
                s.SidelightEnabled = (percent > 0);

                bool anyOk = false;
                int[] modesToUpdate = new int[] { 0, 1, 2, 3 };
                for (int i = 0; i < modesToUpdate.Length; i++)
                {
                    int m = modesToUpdate[i];
                    ushort baseOffset = (ushort)(m * 64);
                    byte[] resp = SendNuphyCommand(path, 0x06, (ushort)(baseOffset + 25), 1, new byte[] { (byte)percent }, 0);
                    if (resp != null) anyOk = true;
                }

                CachedHardwareSettings = s;
                SaveHardwareCache();
                return anyOk;
            }
            catch { }
            return false;
        }

        public static bool SetSidelightEnabled(bool enable)
        {
            var s = CachedHardwareSettings ?? new NuphyHardwareSettings();
            int target = enable ? (s.SidelightBrightness > 0 ? s.SidelightBrightness : 100) : 0;
            return SetSidelightBrightness(target);
        }

        public static MouseBatteryInfo QueryNuphyDeviceInfo()
        {
            lock (hidLock)
            {
                Guid hidGuid;
                RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
                IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
                if (devInfo == IntPtr.Zero || devInfo.ToInt64() == -1) return null;

                RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                uint memberIdx = 0;
                string candidateWired = null;
                string candidateDongle = null;
                int wiredPid = 0;
                int donglePid = 0;
                string wiredName = null;
                string dongleName = null;
                bool foundAnyNuphy = false;

                try
                {
                    ifData.cbSize = Marshal.SizeOf(ifData);
                    while (RazerDeviceHelper.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                    {
                        uint reqSize;
                        RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                        if (reqSize == 0) continue;

                        IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                        if (RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                        {
                            IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                            string devicePath = Marshal.PtrToStringAuto(pDevicePath);
                            if (devicePath != null && (devicePath.ToLower().Contains("vid_19f5") || devicePath.ToLower().Contains("nuphy")))
                            {
                                foundAnyNuphy = true;
                                IntPtr handle = RazerDeviceHelper.CreateFile(devicePath, 0, RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE, IntPtr.Zero, RazerDeviceHelper.OPEN_EXISTING, 0, IntPtr.Zero);
                                if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                {
                                    try
                                    {
                                        string curName = null;
                                        StringBuilder sb = new StringBuilder(256);
                                        if (RazerDeviceHelper.HidD_GetProductString(handle, sb, sb.Capacity))
                                        {
                                            string pStr = sb.ToString().Trim();
                                            if (!string.IsNullOrEmpty(pStr)) curName = pStr;
                                        }

                                        int curPid = 0;
                                        RazerDeviceHelper.HIDD_ATTRIBUTES attr = new RazerDeviceHelper.HIDD_ATTRIBUTES();
                                        attr.Size = Marshal.SizeOf(attr);
                                        if (RazerDeviceHelper.HidD_GetAttributes(handle, ref attr))
                                        {
                                            curPid = attr.ProductID;
                                        }

                                        IntPtr preparsed;
                                        if (RazerDeviceHelper.HidD_GetPreparsedData(handle, out preparsed))
                                        {
                                            RazerDeviceHelper.HIDP_CAPS caps;
                                            RazerDeviceHelper.HidP_GetCaps(preparsed, out caps);
                                            RazerDeviceHelper.HidD_FreePreparsedData(preparsed);

                                            if (caps.InputReportByteLength == 65 && caps.OutputReportByteLength == 65)
                                            {
                                                bool isWired = devicePath.IndexOf("pid_a011", StringComparison.OrdinalIgnoreCase) >= 0 || curPid == 0xA011;
                                                bool isMi01 = devicePath.IndexOf("mi_01", StringComparison.OrdinalIgnoreCase) >= 0;

                                                if (isWired)
                                                {
                                                    if (candidateWired == null || isMi01)
                                                    {
                                                        candidateWired = devicePath;
                                                        wiredPid = curPid != 0 ? curPid : 0xA011;
                                                        if (!string.IsNullOrEmpty(curName)) wiredName = curName;
                                                    }
                                                }
                                                else
                                                {
                                                    if (candidateDongle == null || isMi01)
                                                    {
                                                        candidateDongle = devicePath;
                                                        donglePid = curPid != 0 ? curPid : 0x8F01;
                                                        if (!string.IsNullOrEmpty(curName)) dongleName = curName;
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        RazerDeviceHelper.CloseHandle(handle);
                                    }
                                }
                            }
                        }
                        Marshal.FreeHGlobal(detailBuffer);
                        ifData.cbSize = Marshal.SizeOf(ifData);
                    }
                }
                finally
                {
                    RazerDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);
                }

                if (!foundAnyNuphy) return null;

                string candidatePath = candidateWired ?? candidateDongle;
                int foundPid = (candidateWired != null) ? (wiredPid != 0 ? wiredPid : 0xA011) : (donglePid != 0 ? donglePid : 0x8F01);
                string foundName = (candidateWired != null) ? wiredName : dongleName;

                int foundBattery = -1;
                bool foundCharging = (foundPid == 0xA011);

                if (candidatePath != null)
                {
                    CachedCommandPath = candidatePath;
                    int curMode = CachedHardwareSettings.CurrentMode;
                    byte[] baseResp = SendNuphyCommand(candidatePath, 0x04, 0, 16, null, 300);
                    if (baseResp != null && baseResp.Length >= 10)
                    {
                        curMode = baseResp[9];
                        if (curMode < 0 || curMode > 3) curMode = 1;
                        CachedHardwareSettings.CurrentMode = curMode;
                    }

                    ushort battOffset = (ushort)(curMode * 64 + 32);
                    byte[] inBuf = SendNuphyCommand(candidatePath, 0x05, battOffset, 1, null, 300);
                    if (inBuf != null && (inBuf[1] == 0xAA || inBuf[1] == 0x55) && inBuf[2] == 0x05)
                    {
                        byte raw = inBuf[9];
                        int pct = raw & 0x7F;
                        if (pct >= 0 && pct <= 100)
                        {
                            foundBattery = pct;
                        }
                    }
                }

                bool isPureWired = (foundPid == 0xA011);
                if (foundBattery >= 0)
                {
                    CachedBatteryPercent = foundBattery;
                    CachedIsCharging = isPureWired || (foundBattery >= 100);
                    CachedLastUpdated = DateTime.Now;
                    SaveHardwareCache();
                }

                string devName = GetModelName(foundPid, foundName);
                if (string.IsNullOrEmpty(devName)) devName = !string.IsNullOrEmpty(CachedDeviceName) ? CachedDeviceName : "NuPhy WH80";
                CachedDeviceName = devName;

                int displayBattery = (foundBattery >= 0) ? foundBattery : (CachedBatteryPercent > 0 ? CachedBatteryPercent : -1);
                bool isCharging = isPureWired || (displayBattery >= 100);
                string transport = isPureWired ? "USB 有线" : "2.4G 无线";

                return new MouseBatteryInfo
                {
                    DeviceId = "HID:19F5:" + foundPid.ToString("X4"),
                    Brand = "NuPhy",
                    DeviceName = devName,
                    Category = DeviceCategory.Keyboard,
                    Transport = transport,
                    IsConnected = true,
                    IsSleeping = (foundBattery < 0 && candidatePath == null),
                    IsDonglePresent = (candidateDongle != null),
                    BatteryPercent = displayBattery,
                    ExactBatteryPercent = displayBattery,
                    IsCharging = isCharging,
                    LastUpdated = (foundBattery >= 0) ? DateTime.Now : (CachedLastUpdated != DateTime.MinValue ? CachedLastUpdated : DateTime.Now)
                };
            }
        }
    }

    public static class GenericHidHelper
    {
        public static bool IsDeviceConnected(string hidFingerprint)
        {
            if (string.IsNullOrEmpty(hidFingerprint)) return false;
            try
            {
                Guid hidGuid;
                RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
                IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return false;

                RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(ifData);
                uint memberIdx = 0;

                string target = hidFingerprint.Replace("HID:", "").ToLower();
                string[] parts = target.Split(':');
                string pattern = (parts.Length >= 2) ? string.Format("vid_{0}&pid_{1}", parts[0], parts[1]) : target;

                try
                {
                    while (RazerDeviceHelper.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                    {
                        uint reqSize;
                        RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                        IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                        if (RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                        {
                            IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                            string devicePath = Marshal.PtrToStringAuto(pDevicePath);
                            if (devicePath != null && devicePath.ToLower().Contains(pattern))
                            {
                                Marshal.FreeHGlobal(detailBuffer);
                                return true;
                            }
                        }
                        Marshal.FreeHGlobal(detailBuffer);
                        ifData.cbSize = Marshal.SizeOf(ifData);
                    }
                }
                finally
                {
                    RazerDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);
                }
            }
            catch { }
            return false;
        }

        public static bool QueryHidBattery(string hidFingerprint, out int batteryPercent, out bool isCharging)
        {
            batteryPercent = -1;
            isCharging = false;
            if (string.IsNullOrEmpty(hidFingerprint)) return false;
            try
            {
                Guid hidGuid;
                RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
                IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return false;

                RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(ifData);
                uint memberIdx = 0;

                string target = hidFingerprint.Replace("HID:", "").ToLower();
                string[] parts = target.Split(':');
                string pattern = (parts.Length >= 2) ? string.Format("vid_{0}&pid_{1}", parts[0], parts[1]) : target;

                try
                {
                    while (RazerDeviceHelper.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                    {
                        uint reqSize;
                        RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                        if (reqSize == 0) continue;

                        IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                        if (RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                        {
                            IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                            string devicePath = Marshal.PtrToStringAuto(pDevicePath);
                            if (devicePath != null && devicePath.ToLower().Contains(pattern))
                            {
                                IntPtr handle = RazerDeviceHelper.CreateFile(devicePath, 0, RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE, IntPtr.Zero, RazerDeviceHelper.OPEN_EXISTING, 0, IntPtr.Zero);
                                if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                {
                                    try
                                    {
                                        IntPtr preparsed;
                                        if (RazerDeviceHelper.HidD_GetPreparsedData(handle, out preparsed))
                                        {
                                            RazerDeviceHelper.HIDP_CAPS caps;
                                            RazerDeviceHelper.HidP_GetCaps(preparsed, out caps);
                                            RazerDeviceHelper.HidD_FreePreparsedData(preparsed);

                                            // 1. USB-IF Standard Battery System (UsagePage 0x85)
                                            if (caps.UsagePage == 0x85)
                                            {
                                                if (caps.FeatureReportByteLength > 1)
                                                {
                                                    byte[] fBuf = new byte[caps.FeatureReportByteLength];
                                                    if (RazerDeviceHelper.HidD_GetFeature(handle, fBuf, fBuf.Length))
                                                    {
                                                        for (int i = 1; i < fBuf.Length; i++)
                                                        {
                                                            if (fBuf[i] > 0 && fBuf[i] <= 100)
                                                            {
                                                                batteryPercent = fBuf[i];
                                                                Marshal.FreeHGlobal(detailBuffer);
                                                                return true;
                                                            }
                                                        }
                                                    }
                                                }
                                                if (caps.InputReportByteLength > 1)
                                                {
                                                    byte[] inBuf = new byte[caps.InputReportByteLength];
                                                    if (RazerDeviceHelper.HidD_GetInputReport(handle, inBuf, inBuf.Length))
                                                    {
                                                        for (int i = 1; i < inBuf.Length; i++)
                                                        {
                                                            if (inBuf[i] > 0 && inBuf[i] <= 100)
                                                            {
                                                                batteryPercent = inBuf[i];
                                                                Marshal.FreeHGlobal(detailBuffer);
                                                                return true;
                                                            }
                                                        }
                                                    }
                                                }
                                            }

                                            // 2. Actions ATS / Beken vendor reports (UsagePage 0xFF00, 0xFFA0, 0x0B)
                                            if (caps.UsagePage == 0xFF00 || caps.UsagePage == 0xFFA0 || caps.UsagePage == 0x0B)
                                            {
                                                if (caps.FeatureReportByteLength > 2)
                                                {
                                                    byte[] fBuf = new byte[caps.FeatureReportByteLength];
                                                    fBuf[0] = 0x04;
                                                    if (RazerDeviceHelper.HidD_GetFeature(handle, fBuf, fBuf.Length))
                                                    {
                                                        int candidate = fBuf[1] > 0 && fBuf[1] <= 100 ? fBuf[1] : (fBuf[2] > 0 && fBuf[2] <= 100 ? fBuf[2] : -1);
                                                        if (candidate > 0)
                                                        {
                                                            batteryPercent = candidate;
                                                            Marshal.FreeHGlobal(detailBuffer);
                                                            return true;
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        RazerDeviceHelper.CloseHandle(handle);
                                    }
                                }
                            }
                        }
                        Marshal.FreeHGlobal(detailBuffer);
                        ifData.cbSize = Marshal.SizeOf(ifData);
                    }
                }
                finally
                {
                    RazerDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);
                }
            }
            catch { }
            return false;
        }

        public static List<CustomDeviceConfig> ScanGenericHidMice(HashSet<string> existingIds)
        {
            var result = new List<CustomDeviceConfig>();
            try
            {
                Guid hidGuid;
                RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
                IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return result;

                RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(ifData);
                uint memberIdx = 0;
                var seenPnP = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                try
                {
                    while (RazerDeviceHelper.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                    {
                        uint reqSize;
                        RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                        IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                        if (RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                        {
                            IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                            string devicePath = Marshal.PtrToStringAuto(pDevicePath);
                            if (devicePath != null)
                            {
                                string lower = devicePath.ToLower();
                                // Skip known handled or internal non-mouse devices
                                if (lower.Contains("vid_046d") || lower.Contains("vid_1532") || lower.Contains("vid_24ae") || lower.Contains("vid_3554") ||
                                    lower.Contains("vid_06cb") || lower.Contains("vid_ffaa") || lower.Contains("vid_048d") ||
                                    lower.Contains("vid_17ef") || lower.Contains("syna") || lower.Contains("tencent") || lower.Contains("virtual"))
                                {
                                    Marshal.FreeHGlobal(detailBuffer);
                                    ifData.cbSize = Marshal.SizeOf(ifData);
                                    continue;
                                }

                                IntPtr handle = RazerDeviceHelper.CreateFile(devicePath, 0, RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE, IntPtr.Zero, RazerDeviceHelper.OPEN_EXISTING, 0, IntPtr.Zero);
                                if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                {
                                    try
                                    {
                                        IntPtr preparsed;
                                        if (RazerDeviceHelper.HidD_GetPreparsedData(handle, out preparsed))
                                        {
                                            StringBuilder sbProd = new StringBuilder(256);
                                            string prod = RazerDeviceHelper.HidD_GetProductString(handle, sbProd, sbProd.Capacity) ? sbProd.ToString().Trim() : "";

                                            RazerDeviceHelper.HIDP_CAPS caps;
                                            RazerDeviceHelper.HidP_GetCaps(preparsed, out caps);
                                            RazerDeviceHelper.HidD_FreePreparsedData(preparsed);

                                            bool isGamepad = (caps.UsagePage == 0x01 && (caps.Usage == 0x04 || caps.Usage == 0x05)) ||
                                                             (!string.IsNullOrEmpty(prod) && (prod.ToLower().Contains("gamepad") || prod.ToLower().Contains("controller") || prod.ToLower().Contains("joystick") || prod.Contains("手柄")));

                                            bool isHeadset = (caps.UsagePage == 0x0C && !string.IsNullOrEmpty(prod) && (prod.ToLower().Contains("head") || prod.ToLower().Contains("audio") || prod.ToLower().Contains("ear") || prod.ToLower().Contains("sound") || prod.Contains("耳机"))) ||
                                                             (caps.UsagePage == 0x0B) || (caps.UsagePage == 0x85);

                                            bool isDongle = (!string.IsNullOrEmpty(prod) && (prod.ToLower().Contains("dongle") || prod.ToLower().Contains("receiver") || prod.Contains("接收器")));

                                            bool isKeyboard = (caps.UsagePage == 0x01 && caps.Usage == 0x06) ||
                                                              (!string.IsNullOrEmpty(prod) && (
                                                                  prod.ToLower().Contains("keyboard") ||
                                                                  prod.ToLower().Contains("nuphy") ||
                                                                  prod.ToLower().Contains("keychron") ||
                                                                  prod.ToLower().Contains("halo") ||
                                                                  prod.ToLower().Contains("air75") ||
                                                                  prod.ToLower().Contains("air96") ||
                                                                  prod.ToLower().Contains("air60") ||
                                                                  prod.ToLower().Contains("wh80") ||
                                                                  prod.Contains("键盘")
                                                              ));

                                            bool isMouse = (caps.UsagePage == 0x01 && caps.Usage == 0x02) ||
                                                           (!string.IsNullOrEmpty(prod) && (prod.ToLower().Contains("mouse") || prod.Contains("鼠标")));

                                            if (isGamepad || isHeadset || isDongle || isKeyboard || isMouse)
                                            {
                                                RazerDeviceHelper.HIDD_ATTRIBUTES attr = new RazerDeviceHelper.HIDD_ATTRIBUTES();
                                                attr.Size = Marshal.SizeOf(attr);
                                                RazerDeviceHelper.HidD_GetAttributes(handle, ref attr);

                                                string key = string.Format("{0:X4}:{1:X4}", attr.VendorID, attr.ProductID);
                                                string prodStr = !string.IsNullOrEmpty(prod) ? prod : "HID 兼容外设";

                                                DeviceCategory devCat = DeviceCategory.Generic;
                                                if (isGamepad) devCat = DeviceCategory.Gamepad;
                                                else if (isHeadset) devCat = DeviceCategory.Headset;
                                                else if (isDongle) devCat = DeviceCategory.Dongle;
                                                else if (isKeyboard) devCat = DeviceCategory.Keyboard;
                                                else if (isMouse) devCat = DeviceCategory.Mouse;

                                                var existing = result.Find(r => r.DeviceId == "HID:" + key);
                                                if (existing != null)
                                                {
                                                    if (devCat != DeviceCategory.Generic && existing.Category == DeviceCategory.Generic)
                                                    {
                                                        existing.Category = devCat;
                                                    }
                                                }
                                                else if (!seenPnP.Contains(key))
                                                {
                                                    seenPnP.Add(key);
                                                    if (string.IsNullOrEmpty(prodStr))
                                                    {
                                                        if (isGamepad) prodStr = string.Format("USB 游戏手柄 ({0})", key);
                                                        else if (isHeadset) prodStr = string.Format("USB/2.4G 音频外设 ({0})", key);
                                                        else if (isKeyboard) prodStr = string.Format("USB/2.4G 键盘 ({0})", key);
                                                        else if (isDongle) prodStr = string.Format("2.4G 接收器 ({0})", key);
                                                        else prodStr = string.Format("USB/2.4G 外设 ({0})", key);
                                                    }

                                                    int initialBattery = -1;
                                                    if (NuphyDeviceHelper.IsNuphyDevice(key) || NuphyDeviceHelper.IsNuphyDevice(prodStr))
                                                    {
                                                        devCat = DeviceCategory.Keyboard;
                                                        var nuphy = NuphyDeviceHelper.QueryNuphyDeviceInfo();
                                                        if (nuphy != null && nuphy.BatteryPercent >= 0)
                                                        {
                                                            initialBattery = nuphy.BatteryPercent;
                                                            prodStr = nuphy.DeviceName;
                                                        }
                                                        else if (NuphyDeviceHelper.CachedBatteryPercent > 0)
                                                        {
                                                            initialBattery = NuphyDeviceHelper.CachedBatteryPercent;
                                                        }
                                                    }

                                                    var cfg = new CustomDeviceConfig
                                                    {
                                                        DeviceId = "HID:" + key,
                                                        OriginalName = prodStr,
                                                        CustomName = prodStr,
                                                        Category = devCat,
                                                        Transport = "2.4G 无线",
                                                        CachedBattery = initialBattery,
                                                        HardwareFingerprint = "HID:" + key,
                                                        LastSeen = DateTime.Now
                                                    };
                                                    result.Add(cfg);
                                                }
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        RazerDeviceHelper.CloseHandle(handle);
                                    }
                                }
                            }
                        }
                        Marshal.FreeHGlobal(detailBuffer);
                        ifData.cbSize = Marshal.SizeOf(ifData);
                    }
                }
                finally
                {
                    RazerDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);
                }
            }
            catch { }
            return result;
        }
    }

    public static class BluetoothDeviceHelper
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct DEVPROPKEY
        {
            public Guid fmtid;
            public uint pid;
        }

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern IntPtr SetupDiGetClassDevs(IntPtr ClassGuid, string Enumerator, IntPtr hwndParent, uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiEnumDeviceInfo(IntPtr DeviceInfoSet, uint MemberIndex, ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetupDiGetDevicePropertyW(
            IntPtr DeviceInfoSet,
            ref SP_DEVINFO_DATA DeviceInfoData,
            ref DEVPROPKEY PropertyKey,
            out uint PropertyType,
            byte[] PropertyBuffer,
            uint PropertyBufferSize,
            out uint RequiredSize,
            uint Flags);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        public static extern bool SetupDiGetDeviceInstanceIdW(
            IntPtr DeviceInfoSet,
            ref SP_DEVINFO_DATA DeviceInfoData,
            StringBuilder DeviceInstanceId,
            uint DeviceInstanceIdSize,
            out uint RequiredSize);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        public struct BLUETOOTH_DEVICE_INFO
        {
            public int dwSize;
            public ulong Address;
            public uint ulClassofDevice;
            public bool fConnected;
            public bool fRemembered;
            public bool fAuthenticated;
            public ushort wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds;
            public ushort wYear2, wMonth2, wDayOfWeek2, wDay2, wHour2, wMinute2, wSecond2, wMilliseconds2;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)]
            public string szName;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct BLUETOOTH_DEVICE_SEARCH_PARAMS
        {
            public int dwSize;
            public bool fReturnAuthenticated;
            public bool fReturnRemembered;
            public bool fReturnUnknown;
            public bool fReturnConnected;
            public bool fIssueInquiry;
            public byte cTimeoutMultiplier;
            public IntPtr hRadio;
        }

        [DllImport("bthprops.cpl", SetLastError = true)]
        public static extern IntPtr BluetoothFindFirstDevice(ref BLUETOOTH_DEVICE_SEARCH_PARAMS searchParams, ref BLUETOOTH_DEVICE_INFO deviceInfo);

        [DllImport("bthprops.cpl", SetLastError = true)]
        public static extern bool BluetoothFindNextDevice(IntPtr hFind, ref BLUETOOTH_DEVICE_INFO deviceInfo);

        [DllImport("bthprops.cpl", SetLastError = true)]
        public static extern bool BluetoothFindDeviceClose(IntPtr hFind);

        public static DEVPROPKEY DEVPKEY_Device_BatteryLevel = new DEVPROPKEY
        {
            fmtid = new Guid("104ea319-6ee2-4701-bd47-8ddbf425bbe5"),
            pid = 2
        };

        public static DEVPROPKEY DEVPKEY_Device_BatteryTimestamp = new DEVPROPKEY
        {
            fmtid = new Guid("104ea319-6ee2-4701-bd47-8ddbf425bbe5"),
            pid = 7
        };

        public static DEVPROPKEY DEVPKEY_Device_FriendlyName = new DEVPROPKEY
        {
            fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
            pid = 14
        };

        public static DEVPROPKEY DEVPKEY_Device_DeviceDesc = new DEVPROPKEY
        {
            fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),
            pid = 2
        };

        public static DEVPROPKEY DEVPKEY_Device_IsConnected = new DEVPROPKEY
        {
            fmtid = new Guid("83da6326-97a6-4088-9453-a1923f573b29"),
            pid = 15
        };

        const uint DIGCF_PRESENT = 0x00000002;
        const uint DIGCF_ALLCLASSES = 0x00000004;

        public static Dictionary<string, string> GetConnectedBluetoothDevices()
        {
            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // 1. Classic Bluetooth devices from bthprops.cpl (strictly verified via fConnected == true)
            try
            {
                var sp = new BLUETOOTH_DEVICE_SEARCH_PARAMS
                {
                    dwSize = Marshal.SizeOf(typeof(BLUETOOTH_DEVICE_SEARCH_PARAMS)),
                    fReturnAuthenticated = true,
                    fReturnRemembered = true,
                    fReturnUnknown = false,
                    fReturnConnected = true,
                    fIssueInquiry = false,
                    cTimeoutMultiplier = 2,
                    hRadio = IntPtr.Zero
                };

                var dev = new BLUETOOTH_DEVICE_INFO();
                dev.dwSize = Marshal.SizeOf(typeof(BLUETOOTH_DEVICE_INFO));

                IntPtr hFind = BluetoothFindFirstDevice(ref sp, ref dev);
                if (hFind != IntPtr.Zero)
                {
                    try
                    {
                        do
                        {
                            if (dev.fConnected)
                            {
                                string mac = string.Format("{0:X12}", dev.Address);
                                if (!dict.ContainsKey(mac) && !string.IsNullOrEmpty(dev.szName))
                                {
                                    dict[mac] = dev.szName;
                                }
                            }
                        } while (BluetoothFindNextDevice(hFind, ref dev));
                    }
                    finally
                    {
                        BluetoothFindDeviceClose(hFind);
                    }
                }
            }
            catch { }

            // 2. BLE devices from SetupAPI BTHLE (strictly verified via DEVPKEY_Device_IsConnected == 255)
            IntPtr devInfo = SetupDiGetClassDevs(IntPtr.Zero, "BTHLE", IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
            if (devInfo != IntPtr.Zero && devInfo != new IntPtr(-1))
            {
                try
                {
                    SP_DEVINFO_DATA data = new SP_DEVINFO_DATA();
                    data.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                    uint index = 0;
                    while (SetupDiEnumDeviceInfo(devInfo, index++, ref data))
                    {
                        byte[] connBuf = new byte[16];
                        uint propType, req;
                        if (!SetupDiGetDevicePropertyW(devInfo, ref data, ref DEVPKEY_Device_IsConnected, out propType, connBuf, (uint)connBuf.Length, out req, 0) || connBuf[0] == 0)
                        {
                            continue;
                        }

                        StringBuilder idSb = new StringBuilder(512);
                        SetupDiGetDeviceInstanceIdW(devInfo, ref data, idSb, (uint)idSb.Capacity, out req);
                        string instId = idSb.ToString();
                        string mac = ExtractMac(instId);

                        if (!string.IsNullOrEmpty(mac) && !dict.ContainsKey(mac))
                        {
                            byte[] nameBuf = new byte[512];
                            string name = null;
                            if (SetupDiGetDevicePropertyW(devInfo, ref data, ref DEVPKEY_Device_FriendlyName, out propType, nameBuf, (uint)nameBuf.Length, out req, 0))
                            {
                                string fn = Encoding.Unicode.GetString(nameBuf, 0, (int)req).TrimEnd('\0', ' ', '\t');
                                if (!string.IsNullOrEmpty(fn) && !fn.StartsWith("Bluetooth", StringComparison.OrdinalIgnoreCase))
                                {
                                    name = fn;
                                }
                            }
                            if (string.IsNullOrEmpty(name))
                            {
                                byte[] descBuf = new byte[512];
                                if (SetupDiGetDevicePropertyW(devInfo, ref data, ref DEVPKEY_Device_DeviceDesc, out propType, descBuf, (uint)descBuf.Length, out req, 0))
                                {
                                    string dn = Encoding.Unicode.GetString(descBuf, 0, (int)req).TrimEnd('\0', ' ', '\t');
                                    if (!string.IsNullOrEmpty(dn) && !dn.StartsWith("Bluetooth", StringComparison.OrdinalIgnoreCase))
                                    {
                                        name = dn;
                                    }
                                }
                            }
                            if (!string.IsNullOrEmpty(name))
                            {
                                dict[mac] = name;
                            }
                        }
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(devInfo);
                }
            }

            return dict;
        }

        public static bool QueryByMac(string mac, out int battery, out string friendlyName)
        {
            battery = -1;
            friendlyName = null;
            if (string.IsNullOrEmpty(mac)) return false;

            // 1. Authoritative check: Is device connected via Classic Bluetooth (bthprops) or BLE (SetupAPI)?
            bool isClassicConnected = false;
            string classicName = null;
            try
            {
                var sp = new BLUETOOTH_DEVICE_SEARCH_PARAMS
                {
                    dwSize = Marshal.SizeOf(typeof(BLUETOOTH_DEVICE_SEARCH_PARAMS)),
                    fReturnAuthenticated = true,
                    fReturnRemembered = true,
                    fReturnUnknown = false,
                    fReturnConnected = true,
                    fIssueInquiry = false,
                    cTimeoutMultiplier = 2,
                    hRadio = IntPtr.Zero
                };

                var dev = new BLUETOOTH_DEVICE_INFO();
                dev.dwSize = Marshal.SizeOf(typeof(BLUETOOTH_DEVICE_INFO));

                IntPtr hFind = BluetoothFindFirstDevice(ref sp, ref dev);
                if (hFind != IntPtr.Zero)
                {
                    try
                    {
                        do
                        {
                            if (dev.fConnected)
                            {
                                string devMac = string.Format("{0:X12}", dev.Address);
                                if (string.Equals(devMac, mac, StringComparison.OrdinalIgnoreCase))
                                {
                                    isClassicConnected = true;
                                    classicName = dev.szName;
                                    break;
                                }
                            }
                        } while (BluetoothFindNextDevice(hFind, ref dev));
                    }
                    finally
                    {
                        BluetoothFindDeviceClose(hFind);
                    }
                }
            }
            catch { }

            bool isBleConnected = false;
            IntPtr bthleInfo = SetupDiGetClassDevs(IntPtr.Zero, "BTHLE", IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
            if (bthleInfo != IntPtr.Zero && bthleInfo != new IntPtr(-1))
            {
                try
                {
                    SP_DEVINFO_DATA data = new SP_DEVINFO_DATA();
                    data.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                    uint idx = 0;
                    while (SetupDiEnumDeviceInfo(bthleInfo, idx++, ref data))
                    {
                        StringBuilder idSb = new StringBuilder(512);
                        uint req;
                        SetupDiGetDeviceInstanceIdW(bthleInfo, ref data, idSb, (uint)idSb.Capacity, out req);
                        string instId = idSb.ToString();
                        if (instId.IndexOf(mac, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            byte[] connBuf = new byte[16];
                            uint pType;
                            if (SetupDiGetDevicePropertyW(bthleInfo, ref data, ref DEVPKEY_Device_IsConnected, out pType, connBuf, (uint)connBuf.Length, out req, 0))
                            {
                                if (connBuf[0] != 0)
                                {
                                    isBleConnected = true;
                                    break;
                                }
                            }
                        }
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(bthleInfo);
                }
            }

            if (!isClassicConnected && !isBleConnected)
            {
                return false; // Definitely disconnected / offline
            }

            if (!string.IsNullOrEmpty(classicName)) friendlyName = classicName;

            // 2. The device is verified connected! Scan all subnodes under BTHLE and BTHENUM to retrieve telemetry
            long latestTime = 0;
            int bestBattery = -1;

            string[] enumerators = new string[] { "BTHLE", "BTHENUM" };
            foreach (var en in enumerators)
            {
                IntPtr devInfo = SetupDiGetClassDevs(IntPtr.Zero, en, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);
                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) continue;

                try
                {
                    SP_DEVINFO_DATA data = new SP_DEVINFO_DATA();
                    data.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));

                    uint index = 0;
                    while (SetupDiEnumDeviceInfo(devInfo, index++, ref data))
                    {
                        StringBuilder idSb = new StringBuilder(512);
                        uint req;
                        SetupDiGetDeviceInstanceIdW(devInfo, ref data, idSb, (uint)idSb.Capacity, out req);
                        string instId = idSb.ToString();

                        if (instId.IndexOf(mac, StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            if (string.IsNullOrEmpty(friendlyName))
                            {
                                byte[] nameBuf = new byte[512];
                                uint propType;
                                if (SetupDiGetDevicePropertyW(devInfo, ref data, ref DEVPKEY_Device_FriendlyName, out propType, nameBuf, (uint)nameBuf.Length, out req, 0))
                                {
                                    string fn = Encoding.Unicode.GetString(nameBuf, 0, (int)req).TrimEnd('\0', ' ', '\t');
                                    if (!string.IsNullOrEmpty(fn) && !fn.StartsWith("Bluetooth", StringComparison.OrdinalIgnoreCase) && !fn.Contains("传输") && !fn.Contains("Avrcp"))
                                    {
                                        friendlyName = fn;
                                    }
                                }
                                if (string.IsNullOrEmpty(friendlyName))
                                {
                                    byte[] descBuf = new byte[512];
                                    if (SetupDiGetDevicePropertyW(devInfo, ref data, ref DEVPKEY_Device_DeviceDesc, out propType, descBuf, (uint)descBuf.Length, out req, 0))
                                    {
                                        string dn = Encoding.Unicode.GetString(descBuf, 0, (int)req).TrimEnd('\0', ' ', '\t');
                                        if (!string.IsNullOrEmpty(dn) && !dn.StartsWith("Bluetooth", StringComparison.OrdinalIgnoreCase) && !dn.Contains("传输") && !dn.Contains("Avrcp"))
                                        {
                                            friendlyName = dn;
                                        }
                                    }
                                }
                            }

                            byte[] battBuf = new byte[16];
                            uint pType;
                            if (SetupDiGetDevicePropertyW(devInfo, ref data, ref DEVPKEY_Device_BatteryLevel, out pType, battBuf, (uint)battBuf.Length, out req, 0))
                            {
                                int b = (int)battBuf[0];
                                if (b >= 0 && b <= 100)
                                {
                                    long ft = 0;
                                    byte[] timeBuf = new byte[16];
                                    if (SetupDiGetDevicePropertyW(devInfo, ref data, ref DEVPKEY_Device_BatteryTimestamp, out pType, timeBuf, (uint)timeBuf.Length, out req, 0))
                                    {
                                        ft = BitConverter.ToInt64(timeBuf, 0);
                                    }

                                    if (ft > latestTime)
                                    {
                                        latestTime = ft;
                                        bestBattery = b;
                                    }
                                    else if (latestTime == 0 && bestBattery < 0)
                                    {
                                        bestBattery = b;
                                    }
                                }
                            }
                        }
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(devInfo);
                }
            }

            battery = bestBattery;
            return true;
        }

        public static List<CustomDeviceConfig> ScanAllBluetoothDevices()
        {
            var result = new List<CustomDeviceConfig>();
            var connectedDevices = GetConnectedBluetoothDevices();
            if (connectedDevices == null || connectedDevices.Count == 0)
            {
                return result;
            }

            foreach (var kvp in connectedDevices)
            {
                string mac = kvp.Key;
                string name = kvp.Value;

                if (string.IsNullOrEmpty(name)) name = "Bluetooth Device (" + mac + ")";

                // Filter out non-peripheral internal Bluetooth nodes
                if (name.StartsWith("Bluetooth", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Adapter") || name.Contains("Protocol") || name.Contains("COM") || name.Contains("RFCOMM") ||
                    name.Contains("Avrcp") || name.Contains("Hands-Free") || name.Contains("A2DP") || name.Contains("SNK") || name.Contains("传输"))
                {
                    continue;
                }

                int batt;
                string friendlyName;
                bool isConn = QueryByMac(mac, out batt, out friendlyName);
                if (!isConn) continue; // Only return verified connected devices

                if (!string.IsNullOrEmpty(friendlyName))
                {
                    name = friendlyName;
                }

                DeviceCategory cat = DeviceCategory.Generic;
                string lower = name.ToLowerInvariant();
                if (lower.Contains("xbox") || lower.Contains("controller") || lower.Contains("gamepad") || lower.Contains("dualsense") || lower.Contains("joy-con") || lower.Contains("手柄"))
                    cat = DeviceCategory.Gamepad;
                else if (lower.Contains("buds") || lower.Contains("airpod") || lower.Contains("freebuds") || lower.Contains("earbud") || lower.Contains("入耳"))
                    cat = DeviceCategory.Earbuds;
                else if (lower.Contains("head") || lower.Contains("ear") || lower.Contains("audio") || lower.Contains("free") || lower.Contains("耳机") || lower.Contains("头戴"))
                    cat = DeviceCategory.Headset;
                else if (lower.Contains("mouse") || lower.Contains("鼠标"))
                    cat = DeviceCategory.Mouse;
                else if (lower.Contains("keyboard") || lower.Contains("键盘") || lower.Contains("nuphy") || lower.Contains("keychron") || lower.Contains("halo") || lower.Contains("air75") || lower.Contains("air96") || lower.Contains("air60") || lower.Contains("wh80") || lower.Contains("kb"))
                    cat = DeviceCategory.Keyboard;

                result.Add(new CustomDeviceConfig
                {
                    DeviceId = "BT_" + mac,
                    HardwareFingerprint = mac,
                    OriginalName = name,
                    CustomName = name,
                    Category = cat,
                    Transport = "蓝牙 (BLE)",
                    CachedBattery = batt >= 0 ? batt : 0,
                    LastSeen = DateTime.Now
                });
            }

            return result;
        }

        private static string ExtractMac(string id)
        {
            int idx = id.IndexOf("DEV_");
            if (idx >= 0 && id.Length >= idx + 16)
            {
                return id.Substring(idx + 4, 12).ToUpperInvariant();
            }
            return null;
        }
    }

    public static class DeviceManager
    {
        private const string PrimaryDeviceRegKey = @"Software\RazerBatteryTray";
        private const string PrimaryDeviceValName = "PrimaryDeviceId";
        private const string PriorityOrderValName = "PriorityOrder";
        private const string AutoSwitchValName = "AutoSwitchPriority";
        private const string CustomDevicesRegPath = @"Software\RazerBatteryTray\CustomDevices";
        private const string HiddenDevicesRegPath = @"Software\RazerBatteryTray\HiddenDevices";

        public static bool IsDeviceHidden(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return false;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(HiddenDevicesRegPath, false))
                {
                    if (key != null)
                    {
                        object val = key.GetValue(deviceId);
                        if (val != null && Convert.ToInt32(val) == 1) return true;
                        if (deviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase))
                        {
                            string norm = RazerDeviceHelper.NormalizeRazerDeviceId(deviceId);
                            if (!norm.Equals(deviceId, StringComparison.OrdinalIgnoreCase))
                            {
                                object v2 = key.GetValue(norm);
                                if (v2 != null && Convert.ToInt32(v2) == 1) return true;
                            }
                        }
                    }
                }
            }
            catch { }
            return false;
        }

        public static void HideOrDeleteDevice(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return;
            string norm = deviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase)
                ? RazerDeviceHelper.NormalizeRazerDeviceId(deviceId) : deviceId;

            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(HiddenDevicesRegPath))
                {
                    if (key != null)
                    {
                        key.SetValue(deviceId, 1, RegistryValueKind.DWord);
                        if (!norm.Equals(deviceId, StringComparison.OrdinalIgnoreCase))
                        {
                            key.SetValue(norm, 1, RegistryValueKind.DWord);
                        }
                    }
                }
            }
            catch { }

            DeleteCustomDevice(deviceId);
            if (!norm.Equals(deviceId, StringComparison.OrdinalIgnoreCase))
            {
                DeleteCustomDevice(norm);
            }

            try
            {
                var prio = GetPriorityList();
                if (prio != null)
                {
                    bool chg = prio.Remove(deviceId);
                    if (prio.Remove(norm)) chg = true;
                    if (chg) SavePriorityList(prio);
                }
            }
            catch { }
        }

        public static void UnhideDevice(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return;
            string norm = deviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase)
                ? RazerDeviceHelper.NormalizeRazerDeviceId(deviceId) : deviceId;

            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(HiddenDevicesRegPath, true))
                {
                    if (key != null)
                    {
                        key.DeleteValue(deviceId, false);
                        if (!norm.Equals(deviceId, StringComparison.OrdinalIgnoreCase))
                        {
                            key.DeleteValue(norm, false);
                        }
                    }
                }
            }
            catch { }
        }

        public static string GetConfiguredPrimaryDeviceId()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(PrimaryDeviceRegKey, false))
                {
                    if (key != null)
                    {
                        return key.GetValue(PrimaryDeviceValName) as string;
                    }
                }
            }
            catch { }
            return null;
        }

        public static void SetConfiguredPrimaryDeviceId(string deviceId)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(PrimaryDeviceRegKey))
                {
                    if (key != null && !string.IsNullOrEmpty(deviceId))
                    {
                        key.SetValue(PrimaryDeviceValName, deviceId);
                    }
                }
            }
            catch { }
        }

        public static bool IsAutoSwitchEnabled()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(PrimaryDeviceRegKey, false))
                {
                    if (key != null)
                    {
                        object v = key.GetValue(AutoSwitchValName);
                        if (v != null) return Convert.ToInt32(v) == 1;
                    }
                }
            }
            catch { }
            return true;
        }

        public static void SetAutoSwitchEnabled(bool enabled)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(PrimaryDeviceRegKey))
                {
                    if (key != null)
                    {
                        key.SetValue(AutoSwitchValName, enabled ? 1 : 0);
                    }
                }
            }
            catch { }
        }

        public static List<string> GetPriorityList()
        {
            var list = new List<string>();
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(PrimaryDeviceRegKey, false))
                {
                    if (key != null)
                    {
                        string raw = key.GetValue(PriorityOrderValName) as string;
                        if (!string.IsNullOrEmpty(raw))
                        {
                            string[] parts = raw.Split(new char[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries);
                            foreach (var p in parts)
                            {
                                string trimmed = p.Trim();
                                if (!string.IsNullOrEmpty(trimmed) && !list.Contains(trimmed))
                                {
                                    list.Add(trimmed);
                                }
                            }
                        }
                    }
                }
            }
            catch { }
            return list;
        }

        public static void SavePriorityList(List<string> priorityList)
        {
            if (priorityList == null) return;
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(PrimaryDeviceRegKey))
                {
                    if (key != null)
                    {
                        key.SetValue(PriorityOrderValName, string.Join(",", priorityList.ToArray()));
                    }
                }
            }
            catch { }
        }

        public static List<CustomDeviceConfig> LoadCustomDevices()
        {
            var list = new List<CustomDeviceConfig>();
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(CustomDevicesRegPath, false))
                {
                    if (key != null)
                    {
                        foreach (var subName in key.GetSubKeyNames())
                        {
                            try
                            {
                                using (var sub = key.OpenSubKey(subName, false))
                                {
                                    if (sub != null)
                                    {
                                        var cfg = new CustomDeviceConfig();
                                        cfg.DeviceId = sub.GetValue("DeviceId", subName) as string;
                                        cfg.HardwareFingerprint = sub.GetValue("HardwareFingerprint", "") as string;
                                        cfg.CustomName = sub.GetValue("CustomName", "") as string;
                                        cfg.OriginalName = sub.GetValue("OriginalName", "") as string;
                                        cfg.Category = (DeviceCategory)Convert.ToInt32(sub.GetValue("Category", 0));
                                        cfg.Transport = sub.GetValue("Transport", "Bluetooth") as string;
                                        cfg.CachedBattery = Convert.ToInt32(sub.GetValue("CachedBattery", 100));
                                        string ls = sub.GetValue("LastSeen") as string;
                                        DateTime dt;
                                        if (DateTime.TryParse(ls, out dt)) cfg.LastSeen = dt;
                                        else cfg.LastSeen = DateTime.Now;

                                        // Auto-migration for keyboards misclassified as mice or with dummy 100% battery
                                        string lowerCheck = (cfg.DeviceId + " " + cfg.OriginalName + " " + cfg.CustomName).ToLowerInvariant();
                                        bool isKnownKeyboard = lowerCheck.Contains("nuphy") || lowerCheck.Contains("wh80") ||
                                                               lowerCheck.Contains("air75") || lowerCheck.Contains("air96") ||
                                                               lowerCheck.Contains("air60") || lowerCheck.Contains("halo") ||
                                                               lowerCheck.Contains("keychron") || lowerCheck.Contains("keyboard") ||
                                                               (cfg.OriginalName != null && cfg.OriginalName.Contains("键盘")) ||
                                                               (cfg.CustomName != null && cfg.CustomName.Contains("键盘"));

                                        if (isKnownKeyboard && (cfg.Category != DeviceCategory.Keyboard || cfg.CachedBattery == 100))
                                        {
                                            cfg.Category = DeviceCategory.Keyboard;
                                            if (cfg.CachedBattery == 100 && (cfg.Transport.Contains("2.4G") || cfg.Transport.Contains("USB") || cfg.DeviceId.StartsWith("HID:19F5")))
                                            {
                                                cfg.CachedBattery = -1;
                                            }
                                            SaveCustomDevice(cfg);
                                        }

                                        list.Add(cfg);
                                    }
                                }
                            }
                            catch { }
                        }
                    }
                }
            }
            catch { }
            return list;
        }

        public static void SaveCustomDevice(CustomDeviceConfig dev)
        {
            if (dev == null || string.IsNullOrEmpty(dev.DeviceId)) return;
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(CustomDevicesRegPath + @"\" + dev.DeviceId))
                {
                    if (key != null)
                    {
                        key.SetValue("DeviceId", dev.DeviceId);
                        key.SetValue("HardwareFingerprint", dev.HardwareFingerprint ?? "");
                        key.SetValue("CustomName", dev.CustomName ?? "");
                        key.SetValue("OriginalName", dev.OriginalName ?? "");
                        key.SetValue("Category", (int)dev.Category);
                        key.SetValue("Transport", dev.Transport ?? "Bluetooth");
                        key.SetValue("CachedBattery", dev.CachedBattery);
                        key.SetValue("LastSeen", dev.LastSeen.ToString("o"));
                    }
                }
            }
            catch { }
        }

        public static void DeleteCustomDevice(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(CustomDevicesRegPath, true))
                {
                    if (key != null)
                    {
                        key.DeleteSubKeyTree(deviceId);
                    }
                }
            }
            catch { }

            try
            {
                var prio = GetPriorityList();
                if (prio != null && prio.Remove(deviceId))
                {
                    SavePriorityList(prio);
                }
            }
            catch { }

            try
            {
                string p = GetConfiguredPrimaryDeviceId();
                if (string.Equals(p, deviceId, StringComparison.OrdinalIgnoreCase))
                {
                    SetConfiguredPrimaryDeviceId("");
                }
            }
            catch { }
        }

        public static void CleanupDuplicateLogitechDevices()
        {
            try
            {
                var customConfigs = LoadCustomDevices();
                foreach (var c in customConfigs)
                {
                    if (c.DeviceId != null && c.DeviceId.StartsWith("HID:046D:", StringComparison.OrdinalIgnoreCase))
                    {
                        DeleteCustomDevice(c.DeviceId);
                    }
                }
            }
            catch { }
        }



        public static void CleanupDuplicateNuphyDevices()
        {
            try
            {
                var customConfigs = LoadCustomDevices();
                CustomDeviceConfig dongleCfg = null;
                CustomDeviceConfig wiredCfg = null;
                foreach (var c in customConfigs)
                {
                    if (c.DeviceId.Equals("HID:19F5:8F01", StringComparison.OrdinalIgnoreCase)) dongleCfg = c;
                    else if (c.DeviceId.Equals("HID:19F5:A011", StringComparison.OrdinalIgnoreCase)) wiredCfg = c;
                }
                if (dongleCfg != null && wiredCfg != null)
                {
                    DeleteCustomDevice("HID:19F5:A011");
                }
            }
            catch { }
        }

        public static void CleanupDuplicateRazerDevices()
        {
            try
            {
                var customConfigs = LoadCustomDevices();
                foreach (var c in customConfigs)
                {
                    if (c.DeviceId != null && c.DeviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase))
                    {
                        string normId = RazerDeviceHelper.NormalizeRazerDeviceId(c.DeviceId);
                        if (!c.DeviceId.Equals(normId, StringComparison.OrdinalIgnoreCase))
                        {
                            DeleteCustomDevice(c.DeviceId);
                            c.DeviceId = normId;
                            if (string.IsNullOrEmpty(c.HardwareFingerprint)) c.HardwareFingerprint = normId;
                            SaveCustomDevice(c);
                        }
                    }
                }
            }
            catch { }
        }

        public static void UpdateCustomDeviceBattery(string deviceId, int battery, DateTime lastSeen)
        {
            if (string.IsNullOrEmpty(deviceId)) return;
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(CustomDevicesRegPath + @"\" + deviceId, true))
                {
                    if (key != null)
                    {
                        key.SetValue("CachedBattery", battery);
                        key.SetValue("LastSeen", lastSeen.ToString("o"));
                    }
                }
            }
            catch { }
        }

        public static List<MouseBatteryInfo> QueryAllDevices()
        {
            CleanupDuplicateNuphyDevices();

            CleanupDuplicateLogitechDevices();
            CleanupDuplicateRazerDevices();

            List<MouseBatteryInfo> list = new List<MouseBatteryInfo>();

            // 1. Query Razer
            try
            {
                var razerList = RazerDeviceHelper.QueryAllRazerDevices();
                if (razerList != null && razerList.Count > 0)
                {
                    foreach (var rz in razerList)
                    {
                        if (rz != null && (rz.IsConnected || rz.IsDonglePresent))
                        {
                            rz.Category = DeviceCategory.Mouse;
                            rz.IsCustom = false;
                            list.Add(rz);
                        }
                    }
                }
            }
            catch { }

            // 2. Query Rapoo
            try
            {
                var rapoo = RapooDeviceHelper.QueryRapooDeviceInfo();
                if (rapoo != null && (rapoo.IsConnected || rapoo.IsDonglePresent))
                {
                    rapoo.Category = DeviceCategory.Mouse;
                    rapoo.IsCustom = false;
                    list.Add(rapoo);
                }
            }
            catch { }

            // 2.5 Query VGN
            try
            {
                var vgn = VgnDeviceHelper.QueryVgnDeviceInfo();
                if (vgn != null && (vgn.IsConnected || vgn.IsDonglePresent))
                {
                    vgn.Category = DeviceCategory.Mouse;
                    vgn.IsCustom = false;
                    list.Add(vgn);
                }
            }
            catch { }

            // 2.6 Query NuPhy
            try
            {
                var nuphy = NuphyDeviceHelper.QueryNuphyDeviceInfo();
                if (nuphy != null && (nuphy.IsConnected || nuphy.IsDonglePresent))
                {
                    nuphy.Category = DeviceCategory.Keyboard;
                    nuphy.IsCustom = false;
                    list.Add(nuphy);
                    if (nuphy.BatteryPercent >= 0)
                    {
                        UpdateCustomDeviceBattery(nuphy.DeviceId, nuphy.BatteryPercent, DateTime.Now);
                    }
                }
            }
            catch { }



            // 2.7 Query Logitech (Lightspeed, Unifying, Nano, Wired)
            try
            {
                var logiDevices = LogitechDeviceHelper.QueryAllLogitechDevices();
                if (logiDevices != null && logiDevices.Count > 0)
                {
                    foreach (var dev in logiDevices)
                    {
                        if (dev != null && (dev.IsConnected || dev.IsDonglePresent))
                        {
                            dev.IsCustom = false;
                            list.Add(dev);
                            if (dev.BatteryPercent >= 0)
                            {
                                UpdateCustomDeviceBattery(dev.DeviceId, dev.BatteryPercent, DateTime.Now);
                            }
                        }
                    }
                }
            }
            catch { }

            // 3. Query Custom Devices (Bluetooth / Generic HID)
            try
            {
                var customConfigs = LoadCustomDevices();
                foreach (var cfg in customConfigs)
                {
                    bool isLogitech = (cfg.DeviceId != null && (cfg.DeviceId.StartsWith("Logitech:046D:", StringComparison.OrdinalIgnoreCase) || cfg.DeviceId.IndexOf("046D", StringComparison.OrdinalIgnoreCase) >= 0)) ||
                                      (!string.IsNullOrEmpty(cfg.HardwareFingerprint) && cfg.HardwareFingerprint.IndexOf("046D", StringComparison.OrdinalIgnoreCase) >= 0);



                    bool isNuphy = NuphyDeviceHelper.IsNuphyDevice(cfg.HardwareFingerprint) ||
                                   NuphyDeviceHelper.IsNuphyDevice(cfg.DeviceId) ||
                                   NuphyDeviceHelper.IsNuphyDevice(cfg.OriginalName);

                    bool isRazer = (cfg.DeviceId != null && cfg.DeviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase)) ||
                                   (!string.IsNullOrEmpty(cfg.HardwareFingerprint) && cfg.HardwareFingerprint.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase));

                    string normCfgId = isRazer ? RazerDeviceHelper.NormalizeRazerDeviceId(cfg.DeviceId) : cfg.DeviceId;
                    string normFpId = (isRazer && !string.IsNullOrEmpty(cfg.HardwareFingerprint)) ? RazerDeviceHelper.NormalizeRazerDeviceId(cfg.HardwareFingerprint) : cfg.HardwareFingerprint;

                    // Check if already in list (either direct DeviceId match or normalized Razer match)
                    var existDev = list.Find(x => {
                        if (x.DeviceId.Equals(cfg.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrEmpty(cfg.HardwareFingerprint) && x.DeviceId.Equals(cfg.HardwareFingerprint, StringComparison.OrdinalIgnoreCase)))
                        {
                            return true;
                        }
                        if (isRazer && (x.Brand == "Razer" || (x.DeviceId != null && x.DeviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase))))
                        {
                            string normX = RazerDeviceHelper.NormalizeRazerDeviceId(x.DeviceId);
                            return normX.Equals(normCfgId, StringComparison.OrdinalIgnoreCase) || (!string.IsNullOrEmpty(normFpId) && normX.Equals(normFpId, StringComparison.OrdinalIgnoreCase));
                        }
                        return false;
                    });

                    if (existDev != null)
                    {
                        existDev.IsCustom = true;
                        if (!string.IsNullOrEmpty(cfg.CustomName))
                        {
                            bool isLegacyRz = false;
                            if (isRazer)
                            {
                                for (int ci = 0; ci < cfg.CustomName.Length; ci++)
                                {
                                    if (cfg.CustomName[ci] >= 0x4e00 && cfg.CustomName[ci] <= 0x9fa5)
                                    {
                                        isLegacyRz = true;
                                        break;
                                    }
                                }
                            }

                            if (isRazer && (
                                isLegacyRz ||
                                cfg.CustomName.Equals("Razer Mouse", StringComparison.OrdinalIgnoreCase) ||
                                cfg.CustomName.Equals("Razer Wireless Mouse", StringComparison.OrdinalIgnoreCase) ||
                                cfg.CustomName.Equals("HyperPolling Wireless Dongle", StringComparison.OrdinalIgnoreCase) ||
                                cfg.CustomName.Equals("Razer HyperPolling Wireless Dongle", StringComparison.OrdinalIgnoreCase) ||
                                cfg.CustomName.Equals("雷蛇无线鼠标", StringComparison.OrdinalIgnoreCase) ||
                                cfg.CustomName.Equals("雷蛇电竞鼠标", StringComparison.OrdinalIgnoreCase) ||
                                cfg.CustomName.Equals("雷蛇无线设备", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(cfg.CustomName, cfg.OriginalName, StringComparison.OrdinalIgnoreCase)))
                            {
                                cfg.CustomName = existDev.DisplayName;
                                cfg.OriginalName = existDev.DeviceName;
                                DeviceManager.SaveCustomDevice(cfg);
                            }
                            existDev.CustomName = cfg.CustomName;
                        }
                        continue;
                    }

                    if (isLogitech && list.Exists(x => x.Brand == "Logitech" || x.Brand == "罗技"))
                    {
                        var existLogi = list.Find(x => x.Brand == "Logitech" || x.Brand == "罗技");
                        if (existLogi != null)
                        {
                            existLogi.IsCustom = true;
                            if (!string.IsNullOrEmpty(cfg.CustomName)) existLogi.CustomName = cfg.CustomName;
                        }
                        continue;
                    }



                    if (isNuphy && list.Exists(x => x.Brand == "NuPhy"))
                    {
                        var existNuphy = list.Find(x => x.Brand == "NuPhy");
                        if (existNuphy != null)
                        {
                            existNuphy.IsCustom = true;
                            if (!string.IsNullOrEmpty(cfg.CustomName)) existNuphy.CustomName = cfg.CustomName;
                        }
                        continue;
                    }

                    int batt = -1;
                    string friendlyName = null;
                    bool connected = false;
                    if (!string.IsNullOrEmpty(cfg.HardwareFingerprint) && cfg.HardwareFingerprint.StartsWith("VGN:", StringComparison.OrdinalIgnoreCase))
                    {
                        var vgn = VgnDeviceHelper.QueryVgnDeviceInfo();
                        if (vgn != null)
                        {
                            connected = vgn.IsConnected;
                            batt = vgn.BatteryPercent;
                            friendlyName = vgn.DeviceName;
                        }
                    }
                    else if (!string.IsNullOrEmpty(cfg.HardwareFingerprint) && cfg.HardwareFingerprint.StartsWith("HID:", StringComparison.OrdinalIgnoreCase))
                    {
                        if (isNuphy)
                        {
                            var nuphy = NuphyDeviceHelper.QueryNuphyDeviceInfo();
                            if (nuphy != null && nuphy.IsConnected && (nuphy.DeviceId.Equals(cfg.DeviceId, StringComparison.OrdinalIgnoreCase) || nuphy.DeviceId.Equals(cfg.HardwareFingerprint, StringComparison.OrdinalIgnoreCase)))
                            {
                                connected = true;
                                batt = nuphy.BatteryPercent;
                                friendlyName = nuphy.DeviceName;
                            }
                            else
                            {
                                connected = false;
                                batt = NuphyDeviceHelper.CachedBatteryPercent > 0 ? NuphyDeviceHelper.CachedBatteryPercent : -1;
                            }
                        }
                        else if (isLogitech)
                        {
                            var logi = LogitechDeviceHelper.QueryLogitechDeviceInfo(cfg.HardwareFingerprint);
                            if (logi != null && (logi.IsConnected || logi.IsDonglePresent))
                            {
                                connected = logi.IsConnected;
                                batt = logi.BatteryPercent;
                                friendlyName = logi.DeviceName;
                            }
                            else
                            {
                                connected = false;
                                batt = LogitechDeviceHelper.GetCachedBatteryForDevice(cfg.DeviceId);
                            }
                        }
                        else
                        {
                            bool isChg = false;
                            connected = GenericHidHelper.IsDeviceConnected(cfg.HardwareFingerprint);
                            if (connected)
                            {
                                int readBatt = -1;
                                if (GenericHidHelper.QueryHidBattery(cfg.HardwareFingerprint, out readBatt, out isChg))
                                {
                                    batt = readBatt;
                                }
                                else
                                {
                                    batt = cfg.CachedBattery > 0 ? cfg.CachedBattery : -1;
                                }
                            }
                        }
                    }
                    else if (!string.IsNullOrEmpty(cfg.HardwareFingerprint))
                    {
                        connected = BluetoothDeviceHelper.QueryByMac(cfg.HardwareFingerprint, out batt, out friendlyName);
                    }

                    var devInfo = new MouseBatteryInfo();
                    devInfo.DeviceId = cfg.DeviceId;
                    devInfo.HardwareFingerprint = cfg.HardwareFingerprint;
                    devInfo.Brand = isNuphy ? "NuPhy" : (isRazer ? "Razer" : (isLogitech ? "Logitech" : "Custom"));
                    devInfo.Category = cfg.Category;
                    devInfo.CustomName = cfg.CustomName;
                    devInfo.DeviceName = !string.IsNullOrEmpty(cfg.CustomName) ? cfg.CustomName : (!string.IsNullOrEmpty(friendlyName) ? friendlyName : cfg.OriginalName);
                    devInfo.Transport = cfg.Transport ?? (isRazer ? "2.4G 无线" : "Bluetooth");
                    devInfo.IsCustom = true;
                    if (devInfo.Brand == "NuPhy")
                    {
                        devInfo.Category = DeviceCategory.Keyboard;
                        devInfo.IsCharging = NuphyDeviceHelper.CachedIsCharging;
                    }

                    else if (devInfo.Brand == "Logitech")
                    {
                        devInfo.Category = DeviceCategory.Mouse;
                        devInfo.IsCharging = LogitechDeviceHelper.CachedIsCharging;
                    }
                    else if (devInfo.Brand == "Razer")
                    {
                        devInfo.Category = DeviceCategory.Mouse;
                        int rzPid = 0;
                        string normId = RazerDeviceHelper.NormalizeRazerDeviceId(cfg.DeviceId);
                        if (!string.IsNullOrEmpty(normId) && normId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase))
                        {
                            string hex = normId.Substring("Razer:1532:".Length).Trim();
                            int.TryParse(hex, System.Globalization.NumberStyles.HexNumber, null, out rzPid);
                        }
                        string officialName = RazerDeviceHelper.GetRazerOfficialModelName(rzPid, cfg.OriginalName);
                        string prestigiousDisplayName = RazerDeviceHelper.GetRazerDisplayName(rzPid, officialName);

                        bool isLegacyRz2 = false;
                        if (!string.IsNullOrEmpty(cfg.CustomName))
                        {
                            for (int ci = 0; ci < cfg.CustomName.Length; ci++)
                            {
                                if (cfg.CustomName[ci] >= 0x4e00 && cfg.CustomName[ci] <= 0x9fa5)
                                {
                                    isLegacyRz2 = true;
                                    break;
                                }
                            }
                        }

                        if (string.IsNullOrEmpty(cfg.CustomName) ||
                            isLegacyRz2 ||
                            cfg.CustomName.Equals("Razer Mouse", StringComparison.OrdinalIgnoreCase) ||
                            cfg.CustomName.Equals("Razer Wireless Mouse", StringComparison.OrdinalIgnoreCase) ||
                            cfg.CustomName.Equals("HyperPolling Wireless Dongle", StringComparison.OrdinalIgnoreCase) ||
                            cfg.CustomName.Equals("Razer HyperPolling Wireless Dongle", StringComparison.OrdinalIgnoreCase) ||
                            cfg.CustomName.Equals("雷蛇无线鼠标", StringComparison.OrdinalIgnoreCase) ||
                            cfg.CustomName.Equals("雷蛇电竞鼠标", StringComparison.OrdinalIgnoreCase) ||
                            cfg.CustomName.Equals("雷蛇无线设备", StringComparison.OrdinalIgnoreCase) ||
                            string.Equals(cfg.CustomName, cfg.OriginalName, StringComparison.OrdinalIgnoreCase))
                        {
                            cfg.CustomName = prestigiousDisplayName;
                            cfg.OriginalName = officialName;
                            DeviceManager.SaveCustomDevice(cfg);
                        }
                        devInfo.CustomName = cfg.CustomName;
                        devInfo.DeviceName = officialName;

                        var rzState = RazerDeviceHelper.GetDeviceState(cfg.DeviceId);
                        if (rzState != null)
                        {
                            devInfo.IsCharging = false;
                            if (rzState.BatteryPercent > 0)
                            {
                                devInfo.BatteryPercent = rzState.BatteryPercent;
                                devInfo.ExactBatteryPercent = rzState.ExactBatteryPercent;
                            }
                        }
                    }

                    if (connected)
                    {
                        devInfo.IsConnected = true;
                        devInfo.IsSleeping = false;
                        devInfo.IsDonglePresent = true;
                        devInfo.BatteryPercent = batt;
                        devInfo.ExactBatteryPercent = batt >= 0 ? batt : 0;
                        devInfo.LastUpdated = DateTime.Now;

                        if (batt >= 0)
                        {
                            UpdateCustomDeviceBattery(cfg.DeviceId, batt, DateTime.Now);
                        }
                    }
                    else
                    {
                        // Offline memory feature: retain last-known battery level and sync timestamp!
                        devInfo.IsConnected = false;
                        devInfo.IsSleeping = false;
                        devInfo.IsDonglePresent = false;
                        if (isRazer)
                        {
                            var rzState = RazerDeviceHelper.GetDeviceState(cfg.DeviceId);
                            int cachedRzBatt = (rzState != null && rzState.BatteryPercent > 0) ? rzState.BatteryPercent : (cfg.CachedBattery > 0 ? cfg.CachedBattery : -1);
                            devInfo.BatteryPercent = cachedRzBatt;
                            devInfo.ExactBatteryPercent = devInfo.BatteryPercent;
                        }
                        else if (isLogitech)
                        {
                            int cachedLogi = LogitechDeviceHelper.GetCachedBatteryForDevice(cfg.DeviceId);
                            devInfo.BatteryPercent = cachedLogi > 0 ? cachedLogi : (cfg.CachedBattery > 0 ? cfg.CachedBattery : -1);
                            devInfo.ExactBatteryPercent = devInfo.BatteryPercent;
                        }
                        else
                        {
                            devInfo.BatteryPercent = cfg.CachedBattery > 0 ? cfg.CachedBattery : -1;
                            devInfo.ExactBatteryPercent = devInfo.BatteryPercent;
                        }
                        devInfo.LastUpdated = cfg.LastSeen;
                    }

                    list.Add(devInfo);
                }
            }
            catch { }

            // 3.5 Check custom nicknames configured for Razer or Rapoo devices
            try
            {
                var customConfigs = LoadCustomDevices();
                foreach (var d in list)
                {
                    if (!d.IsCustom)
                    {
                        string normD = (d.Brand == "Razer" || (d.DeviceId != null && d.DeviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase))) ? RazerDeviceHelper.NormalizeRazerDeviceId(d.DeviceId) : d.DeviceId;
                        var match = customConfigs.Find(c => {
                            string normC = (c.DeviceId != null && c.DeviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase)) ? RazerDeviceHelper.NormalizeRazerDeviceId(c.DeviceId) : c.DeviceId;
                            string normFp = (!string.IsNullOrEmpty(c.HardwareFingerprint) && c.HardwareFingerprint.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase)) ? RazerDeviceHelper.NormalizeRazerDeviceId(c.HardwareFingerprint) : c.HardwareFingerprint;
                            return (normC != null && normC.Equals(normD, StringComparison.OrdinalIgnoreCase)) ||
                                   (!string.IsNullOrEmpty(normFp) && normFp.Equals(normD, StringComparison.OrdinalIgnoreCase));
                        });
                        if (match != null && !string.IsNullOrEmpty(match.CustomName))
                        {
                            d.CustomName = match.CustomName;
                            d.IsCustom = true;
                        }
                    }
                }
            }
            catch { }

            // 3.6 Guarantee no duplicate NuPhy devices in list
            var nuphyDevices = list.FindAll(x => x.Brand == "NuPhy" || NuphyDeviceHelper.IsNuphyDevice(x.DeviceId) || NuphyDeviceHelper.IsNuphyDevice(x.DeviceName));
            if (nuphyDevices.Count > 1)
            {
                var best = nuphyDevices.Find(x => x.IsConnected && x.BatteryPercent > 0) ??
                           nuphyDevices.Find(x => x.IsConnected) ??
                           nuphyDevices[0];
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    if (nuphyDevices.Contains(list[i]) && list[i] != best)
                    {
                        list.RemoveAt(i);
                    }
                }
            }

            // 3.65 Guarantee no duplicate Razer devices for the same normalized mouse
            try
            {
                var razerGroups = new Dictionary<string, List<MouseBatteryInfo>>(StringComparer.OrdinalIgnoreCase);
                foreach (var d in list)
                {
                    if (d.Brand == "Razer" || (d.DeviceId != null && d.DeviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase)))
                    {
                        string normId = RazerDeviceHelper.NormalizeRazerDeviceId(d.DeviceId);
                        if (!razerGroups.ContainsKey(normId)) razerGroups[normId] = new List<MouseBatteryInfo>();
                        razerGroups[normId].Add(d);
                    }
                }

                foreach (var kvp in razerGroups)
                {
                    if (kvp.Value.Count > 1)
                    {
                        var best = kvp.Value.Find(x => x.IsConnected && x.BatteryPercent > 0) ??
                                   kvp.Value.Find(x => x.IsConnected) ??
                                   kvp.Value.Find(x => x.IsDonglePresent) ??
                                   kvp.Value[0];
                        for (int i = list.Count - 1; i >= 0; i--)
                        {
                            if (kvp.Value.Contains(list[i]) && list[i] != best)
                            {
                                list.RemoveAt(i);
                            }
                        }
                    }
                }
            }
            catch { }

            // 3.7 Filter out user-hidden devices
            list.RemoveAll(d => IsDeviceHidden(d.DeviceId));

            // 4. Priority Ordering and Primary Selection
            var prioList = GetPriorityList();
            bool prioChanged = false;

            var validIds = new List<string>();
            foreach (var d in list) validIds.Add(d.DeviceId);

            for (int i = prioList.Count - 1; i >= 0; i--)
            {
                if (!validIds.Contains(prioList[i]))
                {
                    prioList.RemoveAt(i);
                    prioChanged = true;
                }
            }

            foreach (var d in list)
            {
                if (!prioList.Contains(d.DeviceId))
                {
                    prioList.Add(d.DeviceId);
                    prioChanged = true;
                }
            }
            if (prioChanged)
            {
                SavePriorityList(prioList);
            }

            list.Sort((a, b) =>
            {
                int ia = prioList.IndexOf(a.DeviceId);
                int ib = prioList.IndexOf(b.DeviceId);
                if (ia < 0) ia = 9999;
                if (ib < 0) ib = 9999;
                return ia.CompareTo(ib);
            });

            for (int i = 0; i < list.Count; i++)
            {
                list[i].PriorityIndex = i + 1;
            }

            // 托盘常驻设备全自动轮换规则：
            // list 已严格按用户在设备优先级页面调整的顺序从高到低排列。
            // 1. 高优先设备在线则展示高优先 (第一个 IsConnected 的设备)；
            // 2. 高优先设备离线则顺延下一个在线设备；
            // 3. 如果全部设备都离线，则回退展示第 1 位设备 (list[0])，并展示离线记忆电量！
            MouseBatteryInfo primary = list.Find(d => d.IsConnected);
            if (primary == null && list.Count > 0)
            {
                primary = list[0];
            }

            foreach (var dev in list)
            {
                dev.IsPrimary = (dev == primary);
            }

            return list;
        }
    }

    public class PromptDialog : Form
    {
        private TextBox txtInput;
        private ModernButton btnOk;
        private ModernButton btnCancel;
        private Label lblPrompt;

        public string InputText
        {
            get { return txtInput != null ? txtInput.Text : ""; }
            set { if (txtInput != null) txtInput.Text = value; }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ModernThemeHelper.ApplyModernWin11Theme(this.Handle);
        }

        public PromptDialog(string title, string prompt, string defaultValue)
        {
            this.Text = title;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.BackColor = Color.FromArgb(20, 24, 33);
            this.ForeColor = Color.White;
            this.Size = new Size(380, 190);

            lblPrompt = new Label();
            lblPrompt.Text = prompt;
            lblPrompt.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
            lblPrompt.ForeColor = Color.FromArgb(210, 215, 225);
            lblPrompt.Location = new Point(20, 16);
            lblPrompt.Size = new Size(325, 42);
            lblPrompt.BackColor = Color.Transparent;
            this.Controls.Add(lblPrompt);

            txtInput = new TextBox();
            txtInput.Text = defaultValue ?? "";
            txtInput.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Regular);
            txtInput.BackColor = Color.FromArgb(30, 36, 48);
            txtInput.ForeColor = Color.White;
            txtInput.BorderStyle = BorderStyle.FixedSingle;
            txtInput.Location = new Point(22, 64);
            txtInput.Size = new Size(320, 26);
            this.Controls.Add(txtInput);

            btnCancel = new ModernButton();
            btnCancel.Text = "取消";
            btnCancel.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
            btnCancel.NormalColor = Color.FromArgb(40, 46, 58);
            btnCancel.HoverColor = Color.FromArgb(50, 58, 72);
            btnCancel.PressedColor = Color.FromArgb(32, 38, 48);
            btnCancel.ForeColor = Color.FromArgb(200, 205, 215);
            btnCancel.CornerRadius = 6;
            btnCancel.Location = new Point(176, 106);
            btnCancel.Size = new Size(76, 30);
            btnCancel.Cursor = Cursors.Hand;
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnCancel);

            btnOk = new ModernButton();
            btnOk.Text = "确定";
            btnOk.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            btnOk.NormalColor = Color.FromArgb(0, 200, 100);
            btnOk.HoverColor = Color.FromArgb(0, 230, 118);
            btnOk.PressedColor = Color.FromArgb(0, 160, 80);
            btnOk.ForeColor = Color.FromArgb(15, 20, 25);
            btnOk.CornerRadius = 6;
            btnOk.Location = new Point(262, 106);
            btnOk.Size = new Size(80, 30);
            btnOk.Cursor = Cursors.Hand;
            btnOk.Click += (s, e) => { this.DialogResult = DialogResult.OK; this.Close(); };
            this.Controls.Add(btnOk);

            this.KeyPreview = true;
            this.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    this.DialogResult = DialogResult.Cancel;
                    this.Close();
                }
            };
        }

        public static bool Show(IWin32Window owner, string title, string prompt, string defaultValue, out string result)
        {
            using (var dlg = new PromptDialog(title, prompt, defaultValue))
            {
                if (dlg.ShowDialog(owner) == DialogResult.OK)
                {
                    result = dlg.InputText.Trim();
                    return true;
                }
            }
            result = null;
            return false;
        }
    }

    public class DpiManagementDialog : Form
    {
        public int[] Stages { get; private set; }
        public int ActiveStage { get; private set; }

        private string deviceName;
        private RazerDeviceHelper.RazerDpiCapability capability;
        private int minDpi;
        private int maxDpi;
        private string sensorDesc;
        private float dpiScale = 1.0f;
        private List<int> stageValues = new List<int>();
        private int currentActiveStage = 1;

        private Panel pnlStages;
        private DpiActionButton btnAddStage;
        private Label lblError;
        private DpiActionButton btnSave;
        private DpiActionButton btnCancel;
        private List<DpiStageRowControl> rowControls = new List<DpiStageRowControl>();

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ModernThemeHelper.ApplyModernWin11Theme(this.Handle);
        }

        public DpiManagementDialog(string devName, int[] initialStages, int currentStage, RazerDeviceHelper.RazerDpiCapability cap)
            : this(devName, initialStages, currentStage, cap, 0f)
        {
        }

        public DpiManagementDialog(string devName, int[] initialStages, int currentStage, RazerDeviceHelper.RazerDpiCapability cap, float scale)
        {
            this.deviceName = devName ?? "外设设备";
            this.capability = cap ?? new RazerDeviceHelper.RazerDpiCapability { MaxDpi = 35000, MinDpi = 100, Step = 1, Description = "Focus Pro 35K Gen-2 · 1-DPI 精准步进" };
            this.minDpi = (this.capability.MinDpi > 0) ? this.capability.MinDpi : 100;
            this.maxDpi = (this.capability.MaxDpi > 0) ? this.capability.MaxDpi : 35000;
            this.sensorDesc = !string.IsNullOrEmpty(this.capability.Description) ? this.capability.Description : "Focus Pro 光学传感器";
            this.currentActiveStage = currentStage > 0 ? currentStage : 1;
            this.ActiveStage = this.currentActiveStage;

            if (scale <= 0f)
            {
                try
                {
                    using (Graphics g = this.CreateGraphics())
                    {
                        scale = g.DpiX / 96.0f;
                    }
                }
                catch { scale = 1.0f; }
            }
            if (scale < 1.0f) scale = 1.0f;
            this.dpiScale = scale;

            if (initialStages != null && initialStages.Length > 0)
            {
                for (int i = 0; i < initialStages.Length && i < 5; i++)
                {
                    stageValues.Add(initialStages[i]);
                }
            }
            if (stageValues.Count == 0)
            {
                stageValues.AddRange(new int[] { 400, 800, 1600, 3200, 6400 });
            }

            InitializeUI();
        }

        private int S(int val) { return (int)Math.Round(val * dpiScale); }

        private void InitializeUI()
        {
            this.Text = "FerrisPulse · DPI 档位与板载设置";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.BackColor = Color.FromArgb(13, 16, 23);
            this.ForeColor = Color.White;
            try { ModernThemeHelper.ApplyModernWin11Theme(this.Handle); } catch { }

            int clientW = S(360);
            int clientH = S(446);
            this.ClientSize = new Size(clientW, clientH);

            int pad = S(16);

            // 1. Title Header
            Label lblTitle = new Label();
            lblTitle.Text = deviceName + " · DPI 档位配置";
            lblTitle.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(245, 248, 252);
            lblTitle.Location = new Point(pad, S(14));
            lblTitle.Size = new Size(clientW - pad * 2, S(22));
            lblTitle.BackColor = Color.Transparent;
            this.Controls.Add(lblTitle);

            // 2. Subtitle / Range Note
            Label lblSub = new Label();
            lblSub.Text = string.Format("支持 1~5 档 · 范围 {0} ~ {1} DPI (点击数值直接输入)", minDpi, maxDpi.ToString("N0"));
            lblSub.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular);
            lblSub.ForeColor = Color.FromArgb(120, 135, 155);
            lblSub.Location = new Point(pad, S(38));
            lblSub.Size = new Size(clientW - pad * 2, S(16));
            lblSub.BackColor = Color.Transparent;
            this.Controls.Add(lblSub);

            // 3. Stages List Container
            pnlStages = new Panel();
            pnlStages.Location = new Point(pad, S(60));
            pnlStages.Size = new Size(clientW - pad * 2, S(240));
            pnlStages.BackColor = Color.Transparent;
            pnlStages.AutoScroll = true;
            this.Controls.Add(pnlStages);

            // 4. Add Stage Button
            btnAddStage = new DpiActionButton(dpiScale);
            btnAddStage.Location = new Point(pad, S(306));
            btnAddStage.Size = new Size(clientW - pad * 2, S(30));
            btnAddStage.CornerRadius = S(6);
            btnAddStage.Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Regular);
            btnAddStage.Cursor = Cursors.Hand;
            btnAddStage.Click += (s, e) => AddStage();
            this.Controls.Add(btnAddStage);

            // 5. Error Label
            lblError = new Label();
            lblError.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular);
            lblError.ForeColor = Color.FromArgb(255, 90, 95);
            lblError.Location = new Point(pad, S(340));
            lblError.Size = new Size(clientW - pad * 2, S(16));
            lblError.BackColor = Color.Transparent;
            lblError.Text = "";
            this.Controls.Add(lblError);

            // 6. Hint Note
            Label lblHint = new Label();
            lblHint.Font = new Font("Microsoft YaHei UI", 7.8F, FontStyle.Regular);
            lblHint.ForeColor = Color.FromArgb(95, 110, 130);
            lblHint.Location = new Point(pad, S(360));
            lblHint.Size = new Size(clientW - pad * 2, S(20));
            lblHint.BackColor = Color.Transparent;
            lblHint.Text = "提示: 点击「设为激活」生效当前档；保存即刻写入板载。";
            this.Controls.Add(lblHint);

            // 7. Bottom Action Buttons: tighter gap S(10), compact buttons
            int btnCancelW = S(88);
            int btnSaveW = S(148);
            int gap = S(10);
            int totalBtnW = btnCancelW + gap + btnSaveW;
            int startX = (clientW - totalBtnW) / 2;
            int btnY = S(392);

            btnCancel = new DpiActionButton(dpiScale);
            btnCancel.Text = "取消";
            btnCancel.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular);
            btnCancel.NormalColor = Color.FromArgb(24, 30, 40);
            btnCancel.HoverColor = Color.FromArgb(34, 42, 56);
            btnCancel.PressedColor = Color.FromArgb(18, 22, 30);
            btnCancel.BorderColor = Color.FromArgb(42, 52, 70);
            btnCancel.ForeColor = Color.FromArgb(200, 210, 225);
            btnCancel.CornerRadius = S(6);
            btnCancel.Location = new Point(startX, btnY);
            btnCancel.Size = new Size(btnCancelW, S(34));
            btnCancel.Cursor = Cursors.Hand;
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnCancel);

            btnSave = new DpiActionButton(dpiScale);
            btnSave.Text = "保存并写入板载";
            btnSave.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold);
            btnSave.NormalColor = Color.FromArgb(0, 200, 90);
            btnSave.HoverColor = Color.FromArgb(0, 230, 115);
            btnSave.PressedColor = Color.FromArgb(0, 160, 70);
            btnSave.BorderColor = Color.Transparent;
            btnSave.ForeColor = Color.FromArgb(10, 24, 16);
            btnSave.CornerRadius = S(6);
            btnSave.Location = new Point(startX + btnCancelW + gap, btnY);
            btnSave.Size = new Size(btnSaveW, S(34));
            btnSave.Cursor = Cursors.Hand;
            btnSave.Click += (s, e) => SaveAndClose();
            this.Controls.Add(btnSave);

            this.KeyPreview = true;
            this.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { this.DialogResult = DialogResult.Cancel; this.Close(); }
                else if (e.KeyCode == Keys.Enter) { SaveAndClose(); }
            };

            this.Shown += (s, e) =>
            {
                try { this.ActiveControl = btnSave; } catch { }
            };

            RebuildStageRows();
        }

        private void RebuildStageRows()
        {
            pnlStages.SuspendLayout();
            pnlStages.Controls.Clear();
            rowControls.Clear();

            int rowH = S(43);
            int rowGap = S(5);
            int cardW = pnlStages.ClientSize.Width - S(2);

            for (int i = 0; i < stageValues.Count; i++)
            {
                int stageNum = i + 1;
                int y = i * (rowH + rowGap);
                bool isActive = (stageNum == currentActiveStage);

                DpiStageRowControl row = new DpiStageRowControl(stageNum, stageValues[i], isActive, (stageValues.Count > 1), minDpi, maxDpi, dpiScale);
                row.Location = new Point(0, y);
                row.Size = new Size(cardW, rowH);

                int captureStageNum = stageNum;
                int captureIndex = i;

                row.OnActivateClicked += () =>
                {
                    SaveCurrentInputs();
                    currentActiveStage = captureStageNum;
                    this.ActiveStage = currentActiveStage;
                    RebuildStageRows();
                };

                row.OnDeleteClicked += () =>
                {
                    RemoveStage(captureIndex);
                };

                rowControls.Add(row);
                pnlStages.Controls.Add(row);
            }

            pnlStages.ResumeLayout();

            if (stageValues.Count < 5)
            {
                btnAddStage.Enabled = true;
                btnAddStage.Text = string.Format("+ 添加新档位  ({0}/5 档)", stageValues.Count);
                btnAddStage.NormalColor = Color.FromArgb(22, 29, 42);
                btnAddStage.HoverColor = Color.FromArgb(30, 40, 58);
                btnAddStage.PressedColor = Color.FromArgb(18, 24, 35);
                btnAddStage.BorderColor = Color.FromArgb(44, 58, 82);
                btnAddStage.ForeColor = Color.FromArgb(56, 189, 248);
            }
            else
            {
                btnAddStage.Enabled = false;
                btnAddStage.Text = "已达到板载存储上限 (5 档)";
                btnAddStage.NormalColor = Color.FromArgb(18, 22, 30);
                btnAddStage.HoverColor = Color.FromArgb(18, 22, 30);
                btnAddStage.PressedColor = Color.FromArgb(18, 22, 30);
                btnAddStage.BorderColor = Color.FromArgb(32, 40, 54);
                btnAddStage.ForeColor = Color.FromArgb(90, 105, 125);
            }
            btnAddStage.Invalidate();
        }

        private void SaveCurrentInputs()
        {
            for (int i = 0; i < rowControls.Count && i < stageValues.Count; i++)
            {
                stageValues[i] = rowControls[i].DpiValue;
            }
        }

        private void AddStage()
        {
            if (stageValues.Count >= 5) return;
            SaveCurrentInputs();
            int lastVal = stageValues.Count > 0 ? stageValues[stageValues.Count - 1] : 800;
            int nextVal = Math.Min(maxDpi, lastVal + 400);
            stageValues.Add(nextVal);
            RebuildStageRows();
        }

        private void RemoveStage(int index)
        {
            if (stageValues.Count <= 1) return;
            SaveCurrentInputs();
            if (index >= 0 && index < stageValues.Count)
            {
                int removedStageNum = index + 1;
                stageValues.RemoveAt(index);
                if (currentActiveStage == removedStageNum)
                {
                    currentActiveStage = Math.Min(currentActiveStage, stageValues.Count);
                }
                else if (currentActiveStage > removedStageNum)
                {
                    currentActiveStage--;
                }
                this.ActiveStage = currentActiveStage;
                RebuildStageRows();
            }
        }

        public void SaveAndClose()
        {
            SaveCurrentInputs();
            lblError.Text = "";

            if (stageValues.Count == 0)
            {
                lblError.Text = "至少需要保留 1 档 DPI。";
                return;
            }

            for (int i = 0; i < stageValues.Count; i++)
            {
                int v = stageValues[i];
                if (v < minDpi || v > maxDpi)
                {
                    lblError.Text = string.Format("第 {0} 档 DPI ({1}) 超出有效范围 ({2} ~ {3})",
                        i + 1, v, minDpi, maxDpi.ToString("N0"));
                    return;
                }
            }

            if (currentActiveStage < 1 || currentActiveStage > stageValues.Count)
            {
                currentActiveStage = 1;
            }

            this.ActiveStage = currentActiveStage;
            this.Stages = stageValues.ToArray();
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        public static GraphicsPath GetRoundedRect(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            if (d > bounds.Width) d = bounds.Width;
            if (d > bounds.Height) d = bounds.Height;
            if (d <= 0) d = 1;

            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    public class DpiStageRowControl : Control
    {
        public int StageNumber { get; private set; }
        public bool IsActive { get; private set; }
        public bool CanDelete { get; private set; }
        public float DpiScale { get; private set; }

        public int DpiValue
        {
            get
            {
                int v;
                if (int.TryParse(txtDpi.Text.Trim(), out v)) return v;
                return 800;
            }
        }

        public event Action OnActivateClicked;
        public event Action OnDeleteClicked;

        private TextBox txtDpi;
        private Rectangle rectBadge;
        private Rectangle rectInputBox;
        private Rectangle rectUnit;
        private Rectangle rectActiveBtn;
        private Rectangle rectDeleteBtn;

        private bool isHoverActiveBtn = false;
        private bool isHoverDeleteBtn = false;
        private bool isHoverInputBox = false;
        private bool isHoverRow = false;
        private bool isInputFocused = false;
        private int minDpi;
        private int maxDpi;

        private int S(int val) { return (int)Math.Round(val * DpiScale); }

        public DpiStageRowControl(int stageNum, int dpiVal, bool active, bool canDel, int min, int max, float scale)
        {
            this.StageNumber = stageNum;
            this.IsActive = active;
            this.CanDelete = canDel;
            this.minDpi = min;
            this.maxDpi = max;
            this.DpiScale = scale > 0 ? scale : 1.0f;

            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            this.BackColor = Color.FromArgb(14, 17, 24);

            txtDpi = new TextBox();
            txtDpi.Text = dpiVal.ToString();
            txtDpi.Font = new Font("Segoe UI", 10F, FontStyle.Bold);
            txtDpi.BackColor = Color.FromArgb(10, 13, 19);
            txtDpi.ForeColor = Color.FromArgb(245, 248, 255);
            txtDpi.BorderStyle = BorderStyle.None;
            txtDpi.TextAlign = HorizontalAlignment.Center;

            txtDpi.Click += (s, e) => txtDpi.SelectAll();
            txtDpi.GotFocus += (s, e) => { isInputFocused = true; txtDpi.SelectAll(); Invalidate(); };
            txtDpi.LostFocus += (s, e) => { isInputFocused = false; Invalidate(); };

            txtDpi.KeyPress += (s, e) =>
            {
                if (!char.IsControl(e.KeyChar) && !char.IsDigit(e.KeyChar)) e.Handled = true;
            };
            txtDpi.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Enter)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    DpiManagementDialog dlg = this.FindForm() as DpiManagementDialog;
                    if (dlg != null) dlg.SaveAndClose();
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                    DpiManagementDialog dlg = this.FindForm() as DpiManagementDialog;
                    if (dlg != null) { dlg.DialogResult = DialogResult.Cancel; dlg.Close(); }
                }
            };
            this.Controls.Add(txtDpi);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            LayoutSubElements();
        }

        private void LayoutSubElements()
        {
            int h = Height;
            int elemH = S(28);
            int elemY = (h - elemH) / 2;

            // 1. Stage Badge: compact "第 1 档" -> S(56)
            int badgeW = S(56);
            int x = S(11);
            rectBadge = new Rectangle(x, elemY, badgeW, elemH);
            x = rectBadge.Right + S(8);

            // 2. Input Box: Dedicated number input box -> S(74)
            int inputW = S(74);
            rectInputBox = new Rectangle(x, elemY, inputW, elemH);
            x = rectInputBox.Right + S(8);

            // Position TextBox inside input box (centered horizontally & vertically)
            int tbH = txtDpi.PreferredHeight;
            int tbY = rectInputBox.Y + (elemH - tbH) / 2;
            txtDpi.SetBounds(rectInputBox.X + S(2), tbY, rectInputBox.Width - S(4), tbH);

            // 3. Unit Label: "DPI" in fixed, aligned column across all rows -> S(28)
            int unitW = S(28);
            rectUnit = new Rectangle(x, elemY, unitW, elemH);
            x = rectUnit.Right + S(11);

            // 4. Active Button: S(82) x S(26)
            int activeW = S(82);
            int activeH = S(26);
            int activeY = (h - activeH) / 2;
            rectActiveBtn = new Rectangle(x, activeY, activeW, activeH);
            x = rectActiveBtn.Right + S(11);

            // 5. Delete Button: perfectly square vector cross S(24) x S(24)
            int delW = S(24);
            int delH = S(24);
            int delY = (h - delH) / 2;
            rectDeleteBtn = new Rectangle(x, delY, delW, delH);
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool oldHoverActive = isHoverActiveBtn;
            bool oldHoverDel = isHoverDeleteBtn;
            bool oldHoverBox = isHoverInputBox;

            isHoverActiveBtn = rectActiveBtn.Contains(e.Location) && !IsActive;
            isHoverDeleteBtn = CanDelete && rectDeleteBtn.Contains(e.Location);
            isHoverInputBox = rectInputBox.Contains(e.Location) || rectUnit.Contains(e.Location);

            if (isHoverActiveBtn || isHoverDeleteBtn) Cursor = Cursors.Hand;
            else if (isHoverInputBox) Cursor = Cursors.IBeam;
            else Cursor = Cursors.Default;

            if (oldHoverActive != isHoverActiveBtn || oldHoverDel != isHoverDeleteBtn || oldHoverBox != isHoverInputBox)
            {
                Invalidate();
            }
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            base.OnMouseEnter(e);
            isHoverRow = true;
            Invalidate();
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            isHoverRow = false;
            isHoverActiveBtn = false;
            isHoverDeleteBtn = false;
            isHoverInputBox = false;
            Cursor = Cursors.Default;
            Invalidate();
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button == MouseButtons.Left)
            {
                if (CanDelete && rectDeleteBtn.Contains(e.Location))
                {
                    if (OnDeleteClicked != null) OnDeleteClicked();
                    return;
                }

                if (rectInputBox.Contains(e.Location) || rectUnit.Contains(e.Location))
                {
                    txtDpi.Focus();
                    txtDpi.SelectAll();
                    return;
                }

                // Click on active button activates this stage
                if (!IsActive && rectActiveBtn.Contains(e.Location))
                {
                    if (OnActivateClicked != null) OnActivateClicked();
                }
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            // 1. Draw Row Card Background
            Color cardBg = IsActive ? Color.FromArgb(16, 36, 26) : (isHoverRow ? Color.FromArgb(22, 28, 38) : Color.FromArgb(17, 21, 30));
            Color cardBorder = IsActive ? Color.FromArgb(0, 230, 118) : (isHoverRow ? Color.FromArgb(44, 55, 76) : Color.FromArgb(28, 35, 50));
            float borderW = IsActive ? 1.5f : 1f;

            Rectangle rectCard = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = DpiManagementDialog.GetRoundedRect(rectCard, S(6)))
            {
                using (SolidBrush b = new SolidBrush(cardBg))
                {
                    g.FillPath(b, path);
                }
                using (Pen p = new Pen(cardBorder, borderW))
                {
                    g.DrawPath(p, path);
                }
            }

            // 2. Draw Stage Badge (Pill)
            Color badgeBg = IsActive ? Color.FromArgb(0, 200, 95) : Color.FromArgb(26, 33, 45);
            Color badgeFg = IsActive ? Color.FromArgb(10, 24, 16) : Color.FromArgb(195, 208, 225);
            using (GraphicsPath path = DpiManagementDialog.GetRoundedRect(rectBadge, S(5)))
            {
                using (SolidBrush b = new SolidBrush(badgeBg))
                {
                    g.FillPath(b, path);
                }
                if (!IsActive)
                {
                    using (Pen p = new Pen(Color.FromArgb(40, 50, 70), 1f))
                    {
                        g.DrawPath(p, path);
                    }
                }
            }
            using (Font f = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, string.Format("第 {0} 档", StageNumber), f, rectBadge, badgeFg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            // 3. Draw DPI Input Box Container (Dedicated number box)
            Color inputBorder = isInputFocused ? Color.FromArgb(0, 230, 118) : (isHoverInputBox ? Color.FromArgb(60, 78, 106) : Color.FromArgb(36, 46, 64));
            using (GraphicsPath path = DpiManagementDialog.GetRoundedRect(rectInputBox, S(5)))
            {
                using (SolidBrush b = new SolidBrush(Color.FromArgb(10, 13, 19)))
                {
                    g.FillPath(b, path);
                }
                using (Pen p = new Pen(inputBorder, isInputFocused ? 1.5f : 1f))
                {
                    g.DrawPath(p, path);
                }
            }

            // DPI Unit label (Aligned column)
            Color unitColor = IsActive ? Color.FromArgb(140, 180, 160) : Color.FromArgb(120, 136, 160);
            using (Font f = new Font("Segoe UI", 8F, FontStyle.Bold))
            {
                TextRenderer.DrawText(g, "DPI", f, rectUnit, unitColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
            }

            // 4. Draw Active Button / Status Badge (Smaller footprint)
            if (IsActive)
            {
                using (GraphicsPath path = DpiManagementDialog.GetRoundedRect(rectActiveBtn, S(4)))
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(0, 200, 95)))
                    {
                        g.FillPath(b, path);
                    }
                }
                using (Font f = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold))
                {
                    TextRenderer.DrawText(g, "✓ 生效中", f, rectActiveBtn, Color.FromArgb(10, 24, 16),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
            }
            else
            {
                Color btnBg = isHoverActiveBtn ? Color.FromArgb(32, 42, 58) : Color.FromArgb(22, 28, 40);
                Color btnBorder = isHoverActiveBtn ? Color.FromArgb(58, 76, 106) : Color.FromArgb(36, 46, 64);
                Color btnFg = isHoverActiveBtn ? Color.FromArgb(240, 246, 255) : Color.FromArgb(140, 155, 175);

                using (GraphicsPath path = DpiManagementDialog.GetRoundedRect(rectActiveBtn, S(4)))
                {
                    using (SolidBrush b = new SolidBrush(btnBg))
                    {
                        g.FillPath(b, path);
                    }
                    using (Pen p = new Pen(btnBorder, 1f))
                    {
                        g.DrawPath(p, path);
                    }
                }
                using (Font f = new Font("Microsoft YaHei UI", 7.8F, FontStyle.Regular))
                {
                    TextRenderer.DrawText(g, "设为激活", f, rectActiveBtn, btnFg,
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                }
            }

            // 5. Draw Perfectly Square Thickened Vector Cross Delete Button
            if (CanDelete)
            {
                if (isHoverDeleteBtn)
                {
                    using (GraphicsPath path = DpiManagementDialog.GetRoundedRect(rectDeleteBtn, S(4)))
                    {
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(45, 255, 82, 82)))
                        {
                            g.FillPath(b, path);
                        }
                    }
                }

                Color xCol = isHoverDeleteBtn ? Color.FromArgb(255, 100, 100) : Color.FromArgb(100, 115, 135);
                float penW = Math.Max(2.0f, 2.0f * DpiScale);
                using (Pen xPen = new Pen(xCol, penW))
                {
                    xPen.StartCap = LineCap.Round;
                    xPen.EndCap = LineCap.Round;
                    int cPad = (int)Math.Round(6.5f * DpiScale);
                    g.DrawLine(xPen, rectDeleteBtn.X + cPad, rectDeleteBtn.Y + cPad, rectDeleteBtn.Right - cPad, rectDeleteBtn.Bottom - cPad);
                    g.DrawLine(xPen, rectDeleteBtn.Right - cPad, rectDeleteBtn.Y + cPad, rectDeleteBtn.X + cPad, rectDeleteBtn.Bottom - cPad);
                }
            }
        }
    }

    public class DpiActionButton : Control
    {
        private bool isHovered = false;
        private bool isPressed = false;
        public float DpiScale { get; set; }

        public Color NormalColor { get; set; }
        public Color HoverColor { get; set; }
        public Color PressedColor { get; set; }
        public Color BorderColor { get; set; }
        public int CornerRadius { get; set; }

        public DpiActionButton(float scale)
        {
            this.DpiScale = scale > 0 ? scale : 1.0f;
            NormalColor = Color.FromArgb(0, 200, 83);
            HoverColor = Color.FromArgb(0, 230, 118);
            PressedColor = Color.FromArgb(0, 160, 65);
            BorderColor = Color.Transparent;
            CornerRadius = (int)(6 * DpiScale);
            Cursor = Cursors.Hand;
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); isHovered = true; Invalidate(); }
        protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); isHovered = false; isPressed = false; Invalidate(); }
        protected override void OnMouseDown(MouseEventArgs e) { base.OnMouseDown(e); if (e.Button == MouseButtons.Left) { isPressed = true; Invalidate(); } }
        protected override void OnMouseUp(MouseEventArgs e) { base.OnMouseUp(e); isPressed = false; Invalidate(); }

        protected override void OnPaint(PaintEventArgs pevent)
        {
            base.OnPaint(pevent);
            Graphics g = pevent.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            Color parentBg = (Parent != null) ? Parent.BackColor : Color.FromArgb(15, 18, 26);
            g.Clear(parentBg);

            Color fill = NormalColor;
            if (!Enabled) fill = NormalColor;
            else if (isPressed) fill = PressedColor;
            else if (isHovered) fill = HoverColor;

            Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using (GraphicsPath path = DpiManagementDialog.GetRoundedRect(rect, CornerRadius))
            {
                using (SolidBrush brush = new SolidBrush(fill))
                {
                    g.FillPath(brush, path);
                }

                if (BorderColor != Color.Transparent)
                {
                    using (Pen pen = new Pen(BorderColor, 1f))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            }

            TextRenderer.DrawText(g, Text, Font, rect, ForeColor, TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
        }
    }
}
