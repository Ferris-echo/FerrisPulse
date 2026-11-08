// ============================================================================
// 模块名称: CorsairDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   美商海盗船 (Corsair) HS80 MAX Wireless 无线耳机 2.4G 接收器 (1B1C:0A97)
//   Bragi HID 协议实现：MI_04 / UsagePage 0xFF42 控制集合、Report ID 0x02 下发
//   GET 属性、0x01 响应解析；属性 0x0F = 电量 (LE16 / 10)，0x10 = 供电状态
//   (0=未知 / 1=充电中 / 2=电池供电 / 3=已接电源但未充电)，并接入 DeviceManager 统一调度。
// 依赖关系: RazerDeviceHelper.cs (HID P/Invoke 基座), MouseBatteryInfo.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、协议解析或签名时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止字符串插值、空条件运算符、nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// 4. [协议来源]: Corsair Bragi HID 协议依据 ToastKiste21/corsair-hs80-max-re (MIT)
//    docs/bragi_protocol.md 与 BrokeSkill/hs80-max-control (MIT) Hs80.Core 实现，
//    并经本机实机 (1B1C:0A97) 抓包复核：
//      TX: 02 09 02 0F 00 ... -> RX: 01 01 02 00 EA 01 ... (0x01EA = 490 -> 49.0%)
//      TX: 02 09 02 10 00 ... -> RX: 01 01 02 00 02 ...    (状态 2 = 电池供电)
//      注: 社区文档曾将 2 记为“已接电源/充满”；本机连续采样 37%->36% 下降时状态恒为 2，
//          且系统无 0A96 有线设备枚举，故按物理事实将 2 判定为“电池供电”，仅 1 视为充电中。
//      TX: 02 09 02 12 00 ... -> RX: 01 01 02 00 96 0A ... (0x0A96 = HS80 MAX 子设备 PID)
// 5. [端点隔离]: 仅接管 0A97/0A96，绝不对 1B1C 其它型号 (键盘 / SLIPSTREAM 接收器) 盲发本协议。
// ============================================================================

using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    public static partial class CorsairDeviceHelper
    {
        private static readonly object hidLock = new object();
        private const string CacheRegistryKey = @"Software\RazerBatteryTray\CorsairCache";

        // ---- Bragi 协议常量 (64 字节报文，含 Report ID) ----
        private const byte ReportIdOut = 0x02;          // Host -> Device 控制报文
        private const byte ReportIdIn = 0x01;           // Device -> Host 响应
        private const byte CmdGet = 0x02;               // GET 属性
        private const int PacketLength = 64;
        private const byte RouteRoot = 0x08;            // 接收器本体
        private const byte RouteChild = 0x09;           // 子设备 (耳机 child_id = 1)
        private const byte PropBatteryLevel = 0x0F;     // 电量 0-1000 (LE16, /10 = 百分比)
        private const byte PropBatteryStatus = 0x10;    // 0=放电 1=充电中 2=已接电源/充满 3=异常
        private const byte PropProductId = 0x12;        // 子设备 PID
        private const ushort UsagePageBragi = 0xFF42;
        private const int HidpStatusSuccess = 0x00110000;

        // ---- 硬件标识 ----
        public const int VendorIdCorsair = 0x1B1C;
        public const int PidHs80MaxReceiver = 0x0A97;   // HS80 MAX 2.4G 接收器
        public const int PidHs80MaxHeadset = 0x0A96;    // HS80 MAX 耳机子设备 (USB 有线直连时出现)

        private const int QueryThrottleMs = 700;        // 硬件查询节流窗口
        private const int SingleReadTimeoutMs = 220;    // 单报文读取等待上限
        private const int ReadBudgetMs = 520;           // 单属性读取总预算
        private const int IdentityBudgetMs = 220;       // 身份握手预算（设备正常时 <10ms 响应）
        private const int MinWriteGapMs = 55;           // Bragi 固件要求的最小写间隔

        // ---- 运行态缓存 ----
        public static int CachedBatteryPercent = -1;
        public static bool CachedIsCharging = false;
        public static bool CachedIsConnected = false;
        public static string CachedDeviceName = "HS80 MAX Wireless";
        public static DateTime CachedLastUpdated = DateTime.MinValue;

        private static bool cachedDonglePresent = false;
        private static int detectedPid = PidHs80MaxReceiver;
        private static int lastQueryTick = 0;
        private static bool hasQueried = false;
        private static long lastWriteTick = 0;

        // ---- Win32 重叠 I/O 常量 ----
        private const uint GenericRead = 0x80000000;
        private const uint GenericWrite = 0x40000000;
        private const uint FileFlagOverlapped = 0x40000000;
        private const int ErrorIoPending = 997;
        private const uint WaitObject0 = 0;

        [StructLayout(LayoutKind.Sequential)]
        private struct HID_OVERLAPPED
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
        private static extern bool ResetEvent(IntPtr hEvent);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, ref HID_OVERLAPPED lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, ref HID_OVERLAPPED lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetOverlappedResult(IntPtr hFile, ref HID_OVERLAPPED lpOverlapped, out uint lpNumberOfBytesTransferred, bool bWait);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CancelIo(IntPtr hFile);

        // ====================================================================
        // 设备识别
        // ====================================================================
        public static bool IsSupportedVidPid(int vid, int pid)
        {
            if (vid != VendorIdCorsair) return false;
            return (pid == PidHs80MaxReceiver || pid == PidHs80MaxHeadset);
        }

        // 支持 "HID:1B1C:0A97" 指纹、设备接口路径等任意包含 VID/PID 的标识
        public static bool IsCorsairDevice(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return false;
            string lower = identifier.ToLowerInvariant();
            bool isVid = lower.Contains("vid_1b1c") || lower.Contains("1b1c:") || lower.Contains("1b1c&");
            if (!isVid) return false;
            return lower.Contains("pid_0a97") || lower.Contains("pid_0a96") ||
                   lower.Contains(":0a97") || lower.Contains(":0a96");
        }

        public static string GetModelName(int pid, string productString)
        {
            if (!string.IsNullOrEmpty(productString))
            {
                string prod = productString.Trim();
                if (prod.IndexOf("HS80 MAX", StringComparison.OrdinalIgnoreCase) >= 0) return "HS80 MAX Wireless";
                if (prod.IndexOf("HS80", StringComparison.OrdinalIgnoreCase) >= 0) return "HS80 Wireless";
                if (prod.IndexOf("CORSAIR", StringComparison.OrdinalIgnoreCase) >= 0 && prod.IndexOf("Receiver", StringComparison.OrdinalIgnoreCase) < 0)
                {
                    return prod;
                }
            }

            switch (pid)
            {
                case PidHs80MaxHeadset:
                    return "HS80 MAX Wireless (USB 有线)";
                case PidHs80MaxReceiver:
                default:
                    return "HS80 MAX Wireless";
            }
        }

        public static void LoadHardwareCache()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(CacheRegistryKey, false))
                {
                    if (key == null) return;

                    object batt = key.GetValue("BatteryPercent");
                    if (batt != null) CachedBatteryPercent = Convert.ToInt32(batt);

                    object chg = key.GetValue("IsCharging");
                    if (chg != null) CachedIsCharging = (Convert.ToInt32(chg) == 1);

                    string name = key.GetValue("DeviceName") as string;
                    if (!string.IsNullOrEmpty(name)) CachedDeviceName = name;

                    string updated = key.GetValue("LastUpdated") as string;
                    if (!string.IsNullOrEmpty(updated))
                    {
                        DateTime dt;
                        if (DateTime.TryParse(updated, out dt)) CachedLastUpdated = dt;
                    }
                }
            }
            catch { }
        }

        public static void SaveHardwareCache()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(CacheRegistryKey))
                {
                    if (key == null) return;
                    if (CachedBatteryPercent >= 0) key.SetValue("BatteryPercent", CachedBatteryPercent);
                    key.SetValue("IsCharging", CachedIsCharging ? 1 : 0);
                    if (!string.IsNullOrEmpty(CachedDeviceName)) key.SetValue("DeviceName", CachedDeviceName);
                    if (CachedLastUpdated != DateTime.MinValue) key.SetValue("LastUpdated", CachedLastUpdated.ToString("o"));
                }
            }
            catch { }
        }

        // ====================================================================
        // Bragi 响应解析（纯函数，便于自动化断言）
        // 响应布局（带 Report ID）: [0]=0x01 [1]=child [2]=cmd(0x02) [3]=err [4..]=payload
        // 部分 HID 后端会剥离 Report ID: [0]=child [1]=cmd [2]=err [3..]=payload
        // ====================================================================
        public static bool TryGetPayload(byte[] rx, int length, out int err, out int payloadStart)
        {
            err = -1;
            payloadStart = -1;
            if (rx == null || length < 5 || length > rx.Length) return false;

            int cmdIndex;
            if (rx[2] == CmdGet) cmdIndex = 2;
            else if (rx[1] == CmdGet) cmdIndex = 1;
            else return false;

            int errIndex = cmdIndex + 1;
            if (errIndex >= length) return false;

            err = rx[errIndex];
            payloadStart = errIndex + 1;
            return true;
        }

        public static bool TryParseBattery(byte[] rx, int length, out double percent)
        {
            percent = -1;
            int err;
            int start;
            if (!TryGetPayload(rx, length, out err, out start)) return false;
            if (err != 0) return false;
            if (start + 1 >= length) return false;

            int raw = rx[start] | (rx[start + 1] << 8);
            if (raw < 0 || raw > 1000) return false;   // 0-1000 = 0.0%-100.0%
            percent = raw / 10.0;
            return true;
        }

        public static bool TryParseChargeState(byte[] rx, int length, out int state)
        {
            state = -1;
            int err;
            int start;
            if (!TryGetPayload(rx, length, out err, out start)) return false;
            if (err != 0) return false;
            if (start >= length) return false;

            int value = rx[start];
            if (value < 0 || value > 3) return false;
            state = value;
            return true;
        }

        public static bool IsChargingState(int state)
        {
            // 实机验证：1 = 充电中；2 = 电池供电（下降沿时状态恒为 2）；
            // 3 = 已接电源但未充电/已充满；0 = 未知。严禁把 2 误报为充电闪电。
            return state == 1;
        }

        // ====================================================================
        // 对外查询入口
        // ====================================================================
        public static MouseBatteryInfo QueryCorsairDeviceInfo()
        {
            lock (hidLock)
            {
                if (hasQueried)
                {
                    int elapsed = Environment.TickCount - lastQueryTick;
                    if (elapsed >= 0 && elapsed < QueryThrottleMs)
                    {
                        return cachedDonglePresent ? BuildDeviceInfo() : null;
                    }
                }

                string path = FindControlPath();
                if (string.IsNullOrEmpty(path))
                {
                    cachedDonglePresent = false;
                    CachedIsConnected = false;
                    CachedIsCharging = false;
                    lastQueryTick = Environment.TickCount;
                    hasQueried = true;
                    return null;
                }

                cachedDonglePresent = true;
                ReadDeviceState(path);
                lastQueryTick = Environment.TickCount;
                hasQueried = true;
                return BuildDeviceInfo();
            }
        }

        private static MouseBatteryInfo BuildDeviceInfo()
        {
            MouseBatteryInfo info = new MouseBatteryInfo();
            info.DeviceId = string.Format("Corsair:{0:X4}:{1:X4}", VendorIdCorsair, detectedPid);
            info.HardwareFingerprint = string.Format("HID:{0:X4}:{1:X4}", VendorIdCorsair, detectedPid);
            info.HardwareId = string.Format("USB\\VID_{0:X4}&PID_{1:X4}", VendorIdCorsair, detectedPid);
            info.DriverId = "BuiltIn:CorsairHeadset";
            info.Brand = "Corsair";
            info.Category = DeviceCategory.Headset;
            info.DeviceName = CachedDeviceName;
            info.Transport = "2.4G 无线";
            info.IsCustom = false;
            info.IsDonglePresent = cachedDonglePresent;
            info.IsConnected = CachedIsConnected;
            info.IsSleeping = cachedDonglePresent && !CachedIsConnected;
            info.IsCharging = CachedIsConnected && CachedIsCharging;
            info.BatteryPercent = CachedBatteryPercent;
            info.ExactBatteryPercent = CachedBatteryPercent >= 0 ? CachedBatteryPercent : 0;
            info.LastUpdated = (CachedLastUpdated == DateTime.MinValue) ? DateTime.Now : CachedLastUpdated;
            info.Capabilities.SupportsBattery = true;
            info.Capabilities.SupportsDpi = false;
            info.Capabilities.SupportsDpiStages = false;
            info.Capabilities.SupportsPollingRate = false;
            return info;
        }

        // ====================================================================
        // 设备枚举：只接受 MI_04 控制集合
        // 无 Report Descriptor 的受限 HID 栈下仍可用；最终以 Bragi PID 握手确认身份。
        // ====================================================================
        private static string FindControlPath()
        {
            try
            {
                Guid hidGuid;
                RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
                IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return null;

                RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(ifData);
                uint memberIdx = 0;
                string fallback = null;

                try
                {
                    while (RazerDeviceHelper.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                    {
                        uint reqSize;
                        RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                        if (reqSize == 0) continue;

                        IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                        try
                        {
                            if (RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                            {
                                IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                                string devicePath = Marshal.PtrToStringAuto(pDevicePath);
                                if (string.IsNullOrEmpty(devicePath)) continue;

                                string lower = devicePath.ToLowerInvariant();
                                if (lower.IndexOf("vid_1b1c", StringComparison.Ordinal) < 0) continue;
                                if (lower.IndexOf("pid_0a97", StringComparison.Ordinal) < 0 &&
                                    lower.IndexOf("pid_0a96", StringComparison.Ordinal) < 0) continue;
                                if (lower.IndexOf("mi_04", StringComparison.Ordinal) < 0) continue;

                                // 优先 col01 控制集合；其余 MI_04 集合作为回退候选
                                if (lower.IndexOf("col01", StringComparison.Ordinal) >= 0) return devicePath;
                                if (fallback == null) fallback = devicePath;
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(detailBuffer);
                        }
                        ifData.cbSize = Marshal.SizeOf(ifData);
                    }
                }
                finally
                {
                    RazerDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);
                }

                return fallback;
            }
            catch { }

            return null;
        }

        // ====================================================================
        // 硬件会话：PID 身份确认 -> 电量 -> 充电状态
        // ====================================================================
        private static void ReadDeviceState(string path)
        {
            IntPtr handle = RazerDeviceHelper.CreateFile(path, GenericRead | GenericWrite,
                RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                IntPtr.Zero, RazerDeviceHelper.OPEN_EXISTING, FileFlagOverlapped, IntPtr.Zero);

            if (handle == IntPtr.Zero || handle.ToInt64() == -1)
            {
                CachedIsConnected = false;
                CachedIsCharging = false;
                return;
            }

            IntPtr evt = CreateEvent(IntPtr.Zero, true, false, null);
            if (evt == IntPtr.Zero)
            {
                RazerDeviceHelper.CloseHandle(handle);
                CachedIsConnected = false;
                return;
            }

            try
            {
                RazerDeviceHelper.HIDD_ATTRIBUTES attr = new RazerDeviceHelper.HIDD_ATTRIBUTES();
                attr.Size = Marshal.SizeOf(attr);
                if (RazerDeviceHelper.HidD_GetAttributes(handle, ref attr))
                {
                    detectedPid = attr.ProductID;
                    StringBuilder sbProd = new StringBuilder(256);
                    string prod = RazerDeviceHelper.HidD_GetProductString(handle, sbProd, sbProd.Capacity) ? sbProd.ToString().Trim() : null;
                    CachedDeviceName = GetModelName(attr.ProductID, prod);
                }

                // 1. Bragi 身份握手：子设备 PID 必须为 0A96（HS80 MAX 耳机）
                if (ReadIdentityPid(handle, evt) != PidHs80MaxHeadset)
                {
                    CachedIsConnected = false;
                    CachedIsCharging = false;
                    return;
                }

                // 2. 电量属性 0x0F
                double percent = -1;
                bool batteryOk = false;
                byte[] batteryRx = SendGet(handle, evt, RouteChild, PropBatteryLevel);
                if (batteryRx != null)
                {
                    batteryOk = TryParseBattery(batteryRx, batteryRx.Length, out percent);
                }

                // 3. 充电状态属性 0x10
                int chargeState = -1;
                byte[] chargeRx = SendGet(handle, evt, RouteChild, PropBatteryStatus);
                if (chargeRx != null)
                {
                    TryParseChargeState(chargeRx, chargeRx.Length, out chargeState);
                }

                if (batteryOk)
                {
                    int newPercent = (int)Math.Round(percent);
                    if (newPercent < 0) newPercent = 0;
                    if (newPercent > 100) newPercent = 100;

                    bool newCharging = (chargeState >= 0) ? IsChargingState(chargeState) : CachedIsCharging;
                    int prevBatt = CachedBatteryPercent;
                    bool prevChg = CachedIsCharging;

                    CachedBatteryPercent = newPercent;
                    CachedIsCharging = newCharging;
                    CachedIsConnected = true;
                    CachedLastUpdated = DateTime.Now;

                    if (prevBatt != CachedBatteryPercent || prevChg != CachedIsCharging)
                    {
                        SaveHardwareCache();
                    }
                }
                else
                {
                    // 接收器在线但耳机休眠 / 关机：保留上次电量，不伪造在线
                    CachedIsConnected = false;
                    CachedIsCharging = false;
                }
            }
            catch
            {
                CachedIsConnected = false;
            }
            finally
            {
                CloseHandle(evt);
                RazerDeviceHelper.CloseHandle(handle);
            }
        }

        // 返回 0x0A96 表示耳机子设备在线；0 表示仅接收器在线或耳机休眠。
        private static int ReadIdentityPid(IntPtr handle, IntPtr evt)
        {
            int pid;
            byte[] rx = SendGet(handle, evt, RouteChild, PropProductId, IdentityBudgetMs);
            if (rx != null)
            {
                if (TryReadPid(rx, rx.Length, out pid)) return pid;
                return 0;   // 接收器已响应但不含耳机 PID，无需再等根路由
            }

            // 有线直连时子设备可能改用根路由应答
            rx = SendGet(handle, evt, RouteRoot, PropProductId, IdentityBudgetMs);
            if (rx != null && TryReadPid(rx, rx.Length, out pid)) return pid;
            return 0;
        }

        private static bool TryReadPid(byte[] rx, int length, out int pid)
        {
            pid = -1;
            int err;
            int start;
            if (!TryGetPayload(rx, length, out err, out start)) return false;
            if (err != 0) return false;
            if (start + 1 >= length) return false;

            int value = rx[start] | (rx[start + 1] << 8);
            if (value != PidHs80MaxHeadset) return false;
            pid = value;
            return true;
        }

        // ====================================================================
        // Bragi 收发原语
        // ====================================================================
        private static byte[] SendGet(IntPtr handle, IntPtr evt, byte route, byte prop)
        {
            return SendGet(handle, evt, route, prop, ReadBudgetMs);
        }

        private static byte[] SendGet(IntPtr handle, IntPtr evt, byte route, byte prop, int readBudgetMs)
        {
            try
            {
                // 1. 丢弃积压的通知 / 历史响应，避免误配
                byte[] drain = new byte[PacketLength];
                for (int i = 0; i < 4; i++)
                {
                    if (ReadReport(handle, evt, drain, 0) <= 0) break;
                }

                // 2. 写入 GET 帧（Report ID 0x02 + 64 字节负载）
                byte[] tx = new byte[PacketLength];
                tx[0] = ReportIdOut;
                tx[1] = route;
                tx[2] = CmdGet;
                tx[3] = prop;

                if (!WriteReport(handle, evt, tx)) return null;

                // 3. 限时等待命令字节为 0x02 的响应
                byte[] rx = new byte[PacketLength];
                long deadline = Environment.TickCount + readBudgetMs;
                while (true)
                {
                    int remain = (int)(deadline - Environment.TickCount);
                    if (remain <= 0) break;

                    int timeout = (remain < SingleReadTimeoutMs) ? remain : SingleReadTimeoutMs;
                    int len = ReadReport(handle, evt, rx, timeout);
                    if (len <= 0) continue;
                    if (len > 2 && rx[2] == CmdGet)
                    {
                        byte[] copy = new byte[len];
                        Array.Copy(rx, copy, len);
                        return copy;
                    }
                }
            }
            catch { }

            return null;
        }

        private static bool WriteReport(IntPtr handle, IntPtr evt, byte[] tx)
        {
            // Bragi 固件要求两次写入之间至少 50ms，否则会丢帧
            long gap = Environment.TickCount - lastWriteTick;
            if (gap >= 0 && gap < MinWriteGapMs)
            {
                System.Threading.Thread.Sleep((int)(MinWriteGapMs - gap));
            }

            HID_OVERLAPPED ov = new HID_OVERLAPPED();
            ov.hEvent = evt;
            ResetEvent(evt);

            uint written = 0;
            lastWriteTick = Environment.TickCount;
            if (WriteFile(handle, tx, (uint)tx.Length, out written, ref ov))
            {
                return true;
            }

            int err = Marshal.GetLastWin32Error();
            if (err != ErrorIoPending)
            {
                return false;
            }

            uint wait = WaitForSingleObject(evt, 220);
            if (wait != WaitObject0)
            {
                CancelIo(handle);
                GetOverlappedResult(handle, ref ov, out written, true);
                return false;
            }
            return GetOverlappedResult(handle, ref ov, out written, false);
        }

        private static int ReadReport(IntPtr handle, IntPtr evt, byte[] buffer, int timeoutMs)
        {
            HID_OVERLAPPED ov = new HID_OVERLAPPED();
            ov.hEvent = evt;
            ResetEvent(evt);

            uint read = 0;
            if (ReadFile(handle, buffer, (uint)buffer.Length, out read, ref ov))
            {
                return (int)read;
            }

            int err = Marshal.GetLastWin32Error();
            if (err != ErrorIoPending)
            {
                return -1;
            }

            uint wait = WaitForSingleObject(evt, (uint)timeoutMs);
            if (wait != WaitObject0)
            {
                CancelIo(handle);
                GetOverlappedResult(handle, ref ov, out read, true);
                return 0;
            }

            if (GetOverlappedResult(handle, ref ov, out read, false))
            {
                return (int)read;
            }
            return -1;
        }
    }
}
