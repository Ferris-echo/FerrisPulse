// ============================================================================
// 模块名称: LightingHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   雷蛇巴蛇终极版 (Razer Basilisk Ultimate, PID 0x0086 有线 / 0x0088 接收器) 灯光控制
//   基于 Razer Extended Matrix (class=0x0F) 14颗RGB灯珠寻址与硬件内置灯效/亮度控制
// 依赖关系: System.Drawing, hid.dll, setupapi.dll, kernel32.dll
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、协议解析或签名时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（❌ 严禁 $"..." 插值，必须使用 string.Format 或 + 拼接）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    public static class LightingHelper
    {
        // ---------- 对外常量 ----------
        public const byte ZoneAll = 0x00;    // 整只鼠标
        public const byte ZoneWheel = 0x01;  // 滚轮
        public const byte ZoneLogo = 0x04;   // Logo

        public const int EffectOff = 0;
        public const int EffectStatic = 1;
        public const int EffectBreathing = 2;
        public const int EffectSpectrum = 3;
        public const int EffectWave = 4;
        public const int EffectReactive = 5;

        // 14 列 LED 矩阵
        public const int MatrixLeds = 14;

        // ---------- 状态缓存（供 UI 显示） ----------
        public static int LastEffect = -1;
        public static Color LastColor = Color.White;
        public static int LastBrightness = 255;
        public static byte LastZone = ZoneAll;
        public static bool LastOperationSucceeded = false;

        private static readonly object SyncRoot = new object();
        private static IntPtr cachedHandle = IntPtr.Zero;
        private static string cachedPath = null;

        // ---------- P/Invoke ----------
        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string enumerator, IntPtr hwndParent, uint flags);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll")]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("hid.dll")]
        private static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("hid.dll")]
        private static extern bool HidD_GetPreparsedData(IntPtr hDevice, out IntPtr preparsedData);

        [DllImport("hid.dll")]
        private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll")]
        private static extern uint HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_SetFeature(IntPtr hDevice, byte[] reportBuffer, int reportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetFeature(IntPtr hDevice, byte[] reportBuffer, int reportBufferLength);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll")]
        private static extern bool CloseHandle(IntPtr hObject);

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public uint Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
        {
            public ushort UsagePage;
            public ushort Usage;
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

        private const uint DIGCF_PRESENT = 0x02;
        private const uint DIGCF_DEVICEINTERFACE = 0x10;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_RW = 0x03;
        private const uint OPEN_EXISTING = 0x03;
        private const int ReportLength = 91;
        private const byte DefaultTid = 0x1F;
        private const byte ClassLighting = 0x0F;
        private const byte StorageDefault = 0x01;

        // ---------- 句柄管理 ----------

        /// <summary>枚举并打开鼠标灯光集合；失败返回 IntPtr.Zero。</summary>
        private static IntPtr AcquireHandle()
        {
            // 铁律：测试模式下严禁下发真实硬件写指令
            if (MainForm.DisableConfigSaveForTesting ||
                HardwareCommandBus.DisableHardwareWriteForTesting ||
                string.Equals(Environment.GetEnvironmentVariable("FERRISPULSE_TEST_MODE"), "1", StringComparison.OrdinalIgnoreCase))
            {
                return IntPtr.Zero;
            }

            if (cachedHandle != IntPtr.Zero) return cachedHandle;

            Guid hidGuid;
            HidD_GetHidGuid(out hidGuid);
            IntPtr devInfo = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return IntPtr.Zero;

            SP_DEVICE_INTERFACE_DATA ifData = new SP_DEVICE_INTERFACE_DATA();
            ifData.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
            uint index = 0;
            try
            {
                while (SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, index, ref ifData))
                {
                    index++;
                    uint requiredSize;
                    SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out requiredSize, IntPtr.Zero);
                    if (requiredSize == 0) continue;

                    IntPtr detail = Marshal.AllocHGlobal((int)requiredSize);
                    Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 5);
                    try
                    {
                        if (!SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detail, requiredSize, out requiredSize, IntPtr.Zero)) continue;
                        string path = Marshal.PtrToStringAuto(new IntPtr(detail.ToInt64() + 4));
                        if (path == null) continue;

                        string lower = path.ToLowerInvariant();
                        // 巴蛇终极版: 有线 0086 / 2.4G 接收器 0088
                        if (lower.IndexOf("vid_1532&pid_0088", StringComparison.Ordinal) < 0 &&
                            lower.IndexOf("vid_1532&pid_0086", StringComparison.Ordinal) < 0) continue;
                        if (lower.IndexOf("\\kbd", StringComparison.Ordinal) >= 0) continue;

                        IntPtr handle = TryOpenPath(path);
                        if (handle == IntPtr.Zero) continue;
                        cachedHandle = handle;
                        cachedPath = path;
                        return handle;
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detail);
                        ifData.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(devInfo);
            }
            return IntPtr.Zero;
        }

        private static IntPtr TryOpenPath(string path)
        {
            IntPtr handle = CreateFile(path, GENERIC_WRITE, FILE_SHARE_RW, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle.ToInt64() == -1)
            {
                handle = CreateFile(path, 0, FILE_SHARE_RW, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                if (handle == IntPtr.Zero || handle.ToInt64() == -1) return IntPtr.Zero;
            }

            IntPtr prep;
            if (!HidD_GetPreparsedData(handle, out prep))
            {
                CloseHandle(handle);
                return IntPtr.Zero;
            }
            HIDP_CAPS caps;
            HidP_GetCaps(prep, out caps);
            HidD_FreePreparsedData(prep);

            bool isMouseTop = (caps.UsagePage == 0x01 && caps.Usage == 0x02) ||
                              (caps.UsagePage == 0x02 && caps.Usage == 0x01);
            if (isMouseTop && caps.FeatureReportByteLength == ReportLength) return handle;

            CloseHandle(handle);
            return IntPtr.Zero;
        }

        private static void ReleaseHandle()
        {
            if (cachedHandle != IntPtr.Zero)
            {
                try { CloseHandle(cachedHandle); }
                catch { }
                cachedHandle = IntPtr.Zero;
                cachedPath = null;
            }
        }

        // ---------- 报文收发 ----------

        private static byte ComputeCrc(byte[] report)
        {
            byte crc = 0;
            for (int i = 3; i < 88; i++) crc ^= report[i];
            return crc;
        }

        private static bool Send(byte tid, byte cmd, byte dataSize, byte[] args)
        {
            lock (SyncRoot)
            {
                for (int attempt = 0; attempt < 4; attempt++)
                {
                    IntPtr handle = AcquireHandle();
                    if (handle == IntPtr.Zero)
                    {
                        LastOperationSucceeded = false;
                        return false;
                    }

                    byte[] report = new byte[ReportLength];
                    report[2] = tid;
                    report[6] = dataSize;
                    report[7] = ClassLighting;
                    report[8] = cmd;
                    if (args != null && args.Length > 0)
                    {
                        Buffer.BlockCopy(args, 0, report, 9, Math.Min(args.Length, ReportLength - 9));
                    }
                    report[89] = ComputeCrc(report);

                    if (!HidD_SetFeature(handle, report, ReportLength))
                    {
                        ReleaseHandle();
                        continue;
                    }

                    Thread.Sleep(31);

                    byte[] response = new byte[ReportLength];
                    if (!HidD_GetFeature(handle, response, ReportLength))
                    {
                        ReleaseHandle();
                        continue;
                    }

                    if (response[1] == 0x02 && response[7] == ClassLighting && response[8] == cmd)
                    {
                        LastOperationSucceeded = true;
                        return true;
                    }
                    if (response[1] == 0x05)
                    {
                        LastOperationSucceeded = false;
                        return false;
                    }
                    Thread.Sleep(10);
                }

                ReleaseHandle();
                LastOperationSucceeded = false;
                return false;
            }
        }

        // ---------- 灯效控制 ----------

        public static bool SetStatic(byte zone, Color color)
        {
            byte[] args = new byte[] { StorageDefault, zone, 0x01, 0x00, 0x00, 0x01, color.R, color.G, color.B };
            bool ok = Send(DefaultTid, 0x02, (byte)args.Length, args);
            if (ok) { LastEffect = EffectStatic; LastColor = color; LastZone = zone; }
            return ok;
        }

        public static bool SetOff(byte zone)
        {
            byte[] args = new byte[] { StorageDefault, zone, 0x00, 0x00, 0x00, 0x00 };
            bool ok = Send(DefaultTid, 0x02, (byte)args.Length, args);
            if (ok) { LastEffect = EffectOff; LastZone = zone; }
            return ok;
        }

        public static bool SetSpectrum(byte zone)
        {
            byte[] args = new byte[] { StorageDefault, zone, 0x03, 0x00, 0x00, 0x00 };
            bool ok = Send(DefaultTid, 0x02, (byte)args.Length, args);
            if (ok) { LastEffect = EffectSpectrum; LastZone = zone; }
            return ok;
        }

        public static bool SetWave(byte zone, int direction)
        {
            byte dir = (byte)(direction < 0 ? 0 : (direction > 2 ? 2 : direction));
            byte[] args = new byte[] { StorageDefault, zone, 0x04, dir, 0x28, 0x00 };
            bool ok = Send(DefaultTid, 0x02, (byte)args.Length, args);
            if (ok) { LastEffect = EffectWave; LastZone = zone; }
            return ok;
        }

        public static bool SetBreathing(byte zone, Color color, int speed)
        {
            byte sp = (byte)(speed < 1 ? 1 : (speed > 4 ? 4 : speed));
            byte[] args = new byte[] { StorageDefault, zone, 0x02, 0x01, 0x00, sp, color.R, color.G, color.B };
            bool ok = Send(0x00, 0x02, (byte)args.Length, args);
            if (ok) { LastEffect = EffectBreathing; LastColor = color; LastZone = zone; }
            return ok;
        }

        public static bool SetReactive(byte zone, Color color, int speed)
        {
            byte sp = (byte)(speed < 1 ? 1 : (speed > 4 ? 4 : speed));
            byte[] args = new byte[] { StorageDefault, zone, 0x05, 0x00, sp, 0x01, color.R, color.G, color.B };
            bool ok = Send(DefaultTid, 0x02, (byte)args.Length, args);
            if (ok) { LastEffect = EffectReactive; LastColor = color; LastZone = zone; }
            return ok;
        }

        public static bool SetBrightness(byte zone, int value)
        {
            byte b = (byte)(value < 0 ? 0 : (value > 255 ? 255 : value));
            byte[] args = new byte[] { StorageDefault, zone, b };
            bool ok = Send(DefaultTid, 0x04, (byte)args.Length, args);
            if (ok) { LastBrightness = b; LastZone = zone; }
            return ok;
        }

        // 实测位置映射：
        //   LED 0      = 滚轮
        //   LED 1      = Logo
        //   LED 2..9   = 左灯带（8 颗，4 段）
        //   LED 10..13 = 右灯带（4 颗联动）
        public const int WheelLed = 0;
        public const int LogoLed = 1;
        public const int LeftStripStart = 2;
        public const int LeftStripEnd = 9;
        public const int RightStripStart = 10;
        public const int RightStripEnd = 13;
        public const int LeftSegmentCount = 4;

        private static readonly Color[] ledColors = CreateInitialLedColors();

        private static Color[] CreateInitialLedColors()
        {
            Color[] arr = new Color[MatrixLeds];
            for (int i = 0; i < arr.Length; i++) arr[i] = Color.Black;
            return arr;
        }

        private static bool FlushFrame()
        {
            byte[] enterCustom = new byte[] { 0x00, 0x00, 0x08, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00 };
            if (!Send(DefaultTid, 0x02, (byte)enterCustom.Length, enterCustom)) return false;

            byte[] frame = new byte[5 + MatrixLeds * 3];
            frame[0] = 0x00;
            frame[1] = 0x00;
            frame[2] = 0x00;                  // row = 0
            frame[3] = 0x00;                  // startCol
            frame[4] = (byte)(MatrixLeds - 1); // endCol
            for (int i = 0; i < MatrixLeds; i++)
            {
                frame[5 + i * 3] = ledColors[i].R;
                frame[6 + i * 3] = ledColors[i].G;
                frame[7 + i * 3] = ledColors[i].B;
            }
            return Send(DefaultTid, 0x03, 71, frame);
        }

        public static bool SetLedRange(int start, int end, Color color)
        {
            if (start < 0) start = 0;
            if (end > MatrixLeds - 1) end = MatrixLeds - 1;
            if (end < start) return false;
            for (int i = start; i <= end; i++) ledColors[i] = color;
            bool ok = FlushFrame();
            if (ok) { LastEffect = EffectStatic; LastColor = color; }
            return ok;
        }

        public static bool SetLeftSegment(int segmentIndex, int segmentCount, Color color)
        {
            if (segmentCount < 1) segmentCount = 1;
            if (segmentIndex < 0) segmentIndex = 0;
            if (segmentIndex >= segmentCount) segmentIndex = segmentCount - 1;
            int total = LeftStripEnd - LeftStripStart + 1;
            int start = LeftStripStart + total * segmentIndex / segmentCount;
            int end = LeftStripStart + total * (segmentIndex + 1) / segmentCount - 1;
            if (end < start) end = start;
            return SetLedRange(start, end, color);
        }

        public static bool SetLeftStripColor(Color color) { return SetLedRange(LeftStripStart, LeftStripEnd, color); }
        public static bool SetRightStripColor(Color color) { return SetLedRange(RightStripStart, RightStripEnd, color); }
        public static bool SetWheelColor(Color color) { return SetLedRange(WheelLed, WheelLed, color); }
        public static bool SetLogoColor(Color color) { return SetLedRange(LogoLed, LogoLed, color); }

        public static bool SetAllColor(Color color)
        {
            return SetLedRange(WheelLed, RightStripEnd, color);
        }

        public static bool SetAllOff()
        {
            for (int i = 0; i < ledColors.Length; i++) ledColors[i] = Color.Black;
            bool frameOk = FlushFrame();
            SetOff(ZoneAll);
            LastEffect = EffectOff;
            return frameOk;
        }

        public static void RestoreUserLighting()
        {
            switch (LastEffect)
            {
                case EffectOff:
                    SetAllOff();
                    break;
                case EffectStatic:
                    FlushFrame();
                    break;
                case EffectSpectrum:
                    SetSpectrum(ZoneAll);
                    break;
                case EffectWave:
                    SetWave(ZoneAll, 1);
                    break;
                case EffectBreathing:
                    SetBreathing(ZoneAll, LastColor, 2);
                    break;
                case EffectReactive:
                    SetReactive(ZoneAll, LastColor, 2);
                    break;
                default:
                    break;
            }
        }

        static LightingHelper()
        {
            try { SystemEvents.PowerModeChanged += OnPowerModeChanged; }
            catch { }
        }

        private static void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume) return;
            ThreadPool.QueueUserWorkItem(delegate { ReapplyAfterResume(); });
        }

        public static void ReapplyAfterResume()
        {
            try
            {
                if (LastEffect < 0) return;
                lock (SyncRoot)
                {
                    ReleaseHandle();
                }
                Thread.Sleep(400);
                RestoreUserLighting();
            }
            catch { }
        }

        public static void Shutdown()
        {
            lock (SyncRoot)
            {
                ReleaseHandle();
            }
        }
    }
}
