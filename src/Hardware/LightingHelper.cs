// 模块名称: LightingHelper.cs
// 功能说明: 雷蛇巴蛇终极版（Basilisk Ultimate）灯光控制
//           基于实测报文逆向（有线 PID 0086 / 2.4G 接收器 PID 0088 通用）
//
// ============================ 协议速查（全部实测 status=0x02） ============================
// 通道: 鼠标顶层集合 mi_00（无 \kbd 后缀），UsagePage/Usage 为 01:02 或 02:01，FeatureReportByteLength=91
// 打开: CreateFile 必须用 GENERIC_WRITE(0x40000000)，RzDev 过滤驱动拒绝带 GENERIC_READ 的句柄
// 报文: 91 字节 feature report
//   [0]=0x00  [1]=status(0x02 成功 / 0x03 被拒 / 0x05 不支持)
//   [2]=TID   [6]=dsize   [7]=class(0x0F=灯光)   [8]=cmd
//   [9..]=args   [89]=XOR(byte[3..87])
//
// 分区编号（实测）: 0x00 = 整只鼠标（全局）  0x01 = 滚轮  0x04 = Logo
//   0x00/0x01/0x04/0x0A 接受命令，其余编号返回 0x03
//
// 灯效（class=0x0F cmd=0x02，TID 默认 0x1F）: args = { storage=0x01, zone, effect, ... }
//   静态: {01, zone, 01, 00, 00, 01, R, G, B}              dsize=9
//   灭灯: {01, zone, 00, 00, 00, 00}                       dsize=6
//   光谱: {01, zone, 03, 00, 00, 00}                       dsize=6
//   波浪: {01, zone, 04, 方向(0-2), 40, 00}                dsize=6
//   响应: {01, zone, 05, 00, 速度, 01, R, G, B}            dsize=9
//   呼吸: TID 必须为 0x00，{01, zone, 02, 01, 00, 01, R, G, B}  dsize=9
//
// 亮度（class=0x0F cmd=0x04）: args = {01, zone, 0..255}   dsize=3
//
// 逐 LED 自定义帧（1×14 矩阵）:
//   1) class=0x0F cmd=0x02 args={00,00,08,00,00,00,00,00,00,00,00,00} dsize=12  ← 切换自定义帧模式
//   2) class=0x0F cmd=0x03 args={00,00, row=0, startCol, endCol, R,G,B...} dsize=71
//
// 注意: 电量联动要求设备活跃，休眠后命令会失败；失败时静默忽略，不打扰用户。
// ========================================================================================
//
// 依赖关系: 仅依赖 System.Drawing（Color）与系统 hid.dll / setupapi.dll / kernel32.dll
// 被调用: MainForm.Tray（灯光子菜单）/ MainForm.Hardware（电量联动）

using System;
using System.Drawing;
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

        // 14 列 LED 矩阵（设备库 matrixDimensions = [1,14]）
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
                    // x64 下 SP_DEVICE_INTERFACE_DETAIL_DATA.cbSize = 8，路径在 +4 偏移
                    Marshal.WriteInt32(detail, 8);
                    try
                    {
                        if (!SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detail, requiredSize, out requiredSize, IntPtr.Zero)) continue;
                        string path = Marshal.PtrToStringAuto(new IntPtr(detail.ToInt64() + 4));
                        if (path == null) continue;

                        string lower = path.ToLowerInvariant();
                        // 巴蛇终极版: 有线 0086 / 2.4G 接收器 0088
                        if (lower.IndexOf("vid_1532&pid_0088", StringComparison.Ordinal) < 0
                            && lower.IndexOf("vid_1532&pid_0086", StringComparison.Ordinal) < 0) continue;
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
            // 过滤驱动拒绝带 GENERIC_READ 的句柄，优先纯写；不行再退回 access=0
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

            bool isMouseTop = (caps.UsagePage == 0x01 && caps.Usage == 0x02)
                           || (caps.UsagePage == 0x02 && caps.Usage == 0x01);
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

        /// <summary>发送一条灯光命令并等待匹配回包；0x03 竞争时重试。</summary>
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
                        // 句柄可能因休眠/重枚举失效，丢弃后重试一次
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

                    // 只认回显 class/cmd 一致且 status=0x02 的包，否则是陈旧周期报
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
                    // status=0x03 视为瞬时竞争，稍后重试
                    Thread.Sleep(10);
                }

                // 多次失败通常是句柄失活，清掉缓存以便下次重新枚举
                ReleaseHandle();
                LastOperationSucceeded = false;
                return false;
            }
        }

        // ---------- 灯效 ----------

        /// <summary>设置静态色。</summary>
        public static bool SetStatic(byte zone, Color color)
        {
            byte[] args = new byte[] { StorageDefault, zone, 0x01, 0x00, 0x00, 0x01, color.R, color.G, color.B };
            bool ok = Send(DefaultTid, 0x02, (byte)args.Length, args);
            if (ok) { LastEffect = EffectStatic; LastColor = color; LastZone = zone; }
            return ok;
        }

        /// <summary>关闭指定分区的灯。</summary>
        public static bool SetOff(byte zone)
        {
            byte[] args = new byte[] { StorageDefault, zone, 0x00, 0x00, 0x00, 0x00 };
            bool ok = Send(DefaultTid, 0x02, (byte)args.Length, args);
            if (ok) { LastEffect = EffectOff; LastZone = zone; }
            return ok;
        }

        /// <summary>光谱循环。</summary>
        public static bool SetSpectrum(byte zone)
        {
            byte[] args = new byte[] { StorageDefault, zone, 0x03, 0x00, 0x00, 0x00 };
            bool ok = Send(DefaultTid, 0x02, (byte)args.Length, args);
            if (ok) { LastEffect = EffectSpectrum; LastZone = zone; }
            return ok;
        }

        /// <summary>波浪效果；direction 取 1 或 2。</summary>
        public static bool SetWave(byte zone, int direction)
        {
            byte dir = (byte)(direction < 0 ? 0 : (direction > 2 ? 2 : direction));
            byte[] args = new byte[] { StorageDefault, zone, 0x04, dir, 0x28, 0x00 };
            bool ok = Send(DefaultTid, 0x02, (byte)args.Length, args);
            if (ok) { LastEffect = EffectWave; LastZone = zone; }
            return ok;
        }

        /// <summary>单色呼吸；speed 取 1-4。该特效 TID 必须为 0x00。</summary>
        public static bool SetBreathing(byte zone, Color color, int speed)
        {
            byte sp = (byte)(speed < 1 ? 1 : (speed > 4 ? 4 : speed));
            byte[] args = new byte[] { StorageDefault, zone, 0x02, 0x01, 0x00, sp, color.R, color.G, color.B };
            bool ok = Send(0x00, 0x02, (byte)args.Length, args);
            if (ok) { LastEffect = EffectBreathing; LastColor = color; LastZone = zone; }
            return ok;
        }

        /// <summary>响应（按下鼠标时闪一下）；speed 取 1-4。</summary>
        public static bool SetReactive(byte zone, Color color, int speed)
        {
            byte sp = (byte)(speed < 1 ? 1 : (speed > 4 ? 4 : speed));
            byte[] args = new byte[] { StorageDefault, zone, 0x05, 0x00, sp, 0x01, color.R, color.G, color.B };
            bool ok = Send(DefaultTid, 0x02, (byte)args.Length, args);
            if (ok) { LastEffect = EffectReactive; LastColor = color; LastZone = zone; }
            return ok;
        }

        /// <summary>设置分区亮度，0-255。</summary>
        public static bool SetBrightness(byte zone, int value)
        {
            byte b = (byte)(value < 0 ? 0 : (value > 255 ? 255 : value));
            byte[] args = new byte[] { StorageDefault, zone, b };
            bool ok = Send(DefaultTid, 0x04, (byte)args.Length, args);
            if (ok) { LastBrightness = b; LastZone = zone; }
            return ok;
        }

        // 实测位置映射（逐颗点灯验证）：
        //   LED 0      = 滚轮
        //   LED 1      = Logo
        //   LED 2..9   = 左灯带（8 颗）
        //   LED 10..13 = 右灯带（4 颗）
        // 滚轮与 Logo 虽然有独立分区 zone 01/04，但同时也在矩阵里；
        // 为避免分区命令与整帧重发互相覆盖，统一走矩阵控制。
        public const int WheelLed = 0;
        public const int LogoLed = 1;
        public const int LeftStripStart = 2;
        public const int LeftStripEnd = 9;
        public const int RightStripStart = 10;
        public const int RightStripEnd = 13;
        public const int LeftSegmentCount = 4;   // 左灯带 8 颗，按 4 段各 2 颗

        private static readonly Color[] ledColors = CreateInitialLedColors();

        private static Color[] CreateInitialLedColors()
        {
            // 固件不支持读取矩阵当前状态，初值用黑色：首次操作某个分区时，
            // 其余灯的灯珠会保持熄灭，而不是被刷成白色。
            Color[] arr = new Color[MatrixLeds];
            for (int i = 0; i < arr.Length; i++) arr[i] = Color.Black;
            return arr;
        }

        /// <summary>整帧重发（颜色取自内部缓存的每颗 LED 状态）。</summary>
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

        /// <summary>按索引区间上色（start / end 均含端点）。
        /// 注意：固件忽略帧头 startCol，永远从 LED 0 开始按字节数填充，
        /// 因此这里必须整帧 14 颗一起下发，区间外的灯珠用缓存色补回。</summary>
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

        /// <summary>左灯带按段上色；段号 0 为靠近滚轮那端。</summary>
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

        /// <summary>一键把整只鼠标设成同一颜色（滚轮 + Logo + 左右灯带，整帧一次下发）。</summary>
        public static bool SetAllColor(Color color)
        {
            return SetLedRange(WheelLed, RightStripEnd, color);
        }


        /// <summary>一键关闭整只鼠标的灯（滚轮 + Logo + 左右灯带全部置黑）。</summary>
        public static bool SetAllOff()
        {
            for (int i = 0; i < ledColors.Length; i++) ledColors[i] = Color.Black;
            bool frameOk = FlushFrame();
            SetOff(ZoneAll);
            LastEffect = EffectOff;
            return frameOk;
        }

        // ---------- 电量联动 ----------

        /// <summary>
        /// 按电量渲染灯光状态：低电预警红色呼吸，充电中绿色呼吸，其余恢复用户上次设置。
        /// 只在状态发生变化时下发命令，避免轮询时反复刷 HID。
        /// </summary>
        public static void ApplyBatteryIndicator(int batteryPercent, bool isCharging, bool wasCharging)
        {
            try
            {
                if (batteryPercent < 0) return;

                if (isCharging)
                {
                    if (!wasCharging) SetBreathing(ZoneAll, Color.LimeGreen, 2);
                    return;
                }

                if (batteryPercent <= 20)
                {
                    SetBreathing(ZoneAll, Color.Red, 4);
                    return;
                }

                // 从充电态或低电态恢复：还原用户上次的颜色/灯效
                if (wasCharging)
                {
                    RestoreUserLighting();
                }
            }
            catch
            {
                // 灯光只是附加功能，任何异常都不应影响电量显示
            }
        }

        /// <summary>恢复用户最后一次设定的灯效。</summary>
        public static void RestoreUserLighting()
        {
            switch (LastEffect)
            {
                case EffectOff:
                    SetAllOff();
                    break;
                case EffectStatic:
                    // 整帧重发即可完整还原每颗 LED 的缓存颜色（含分区上色的结果）
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
                    // 用户从未设过灯效：什么都不做，避免唤醒后凭空开出光谱循环
                    break;
            }
        }

        // ---------- 睡眠 / 唤醒恢复 ----------

        static LightingHelper()
        {
            try { SystemEvents.PowerModeChanged += OnPowerModeChanged; }
            catch { }
        }

        private static void OnPowerModeChanged(object sender, PowerModeChangedEventArgs e)
        {
            if (e.Mode != PowerModes.Resume) return;
            // 睡眠期间鼠标断电，固件唤醒后会恢复默认蓝色灯效；
            // 且旧 HID 句柄已失效，必须重建句柄后再重发上次的灯光状态。
            ThreadPool.QueueUserWorkItem(delegate { ReapplyAfterResume(); });
        }

        /// <summary>唤醒后重建句柄并重发用户上次的灯效（含全黑状态）。</summary>
        public static void ReapplyAfterResume()
        {
            try
            {
                if (LastEffect < 0) return;   // 从未设置过，交给固件默认
                lock (SyncRoot)
                {
                    ReleaseHandle();
                }
                Thread.Sleep(400);            // 等设备重新枚举完成
                RestoreUserLighting();
            }
            catch
            {
                // 唤醒恢复失败不影响主功能
            }
        }

        /// <summary>释放缓存句柄（程序退出时调用）。</summary>
        public static void Shutdown()
        {
            lock (SyncRoot)
            {
                ReleaseHandle();
            }
        }
    }
}
