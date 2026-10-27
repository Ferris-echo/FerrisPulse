// ============================================================================
// 模块名称: HyperXDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   极度未知 (HyperX) 无线耳机 2.4G 接收器专属 HID 协议实现：
//   0x66 报文体状态查询（0x82 连接 / 0x8A 充电 / 0x89 电量）与飓风3无线版
//   (Cloud III Wireless) 电量、充电、在线状态解析、离线电量缓存与注册表持久化
// 依赖关系: RazerDeviceHelper.cs (HID P/Invoke 基座), MouseBatteryInfo.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、协议解析或签名时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止字符串插值、空条件运算符、nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    public static class HyperXDeviceHelper
    {
        private static readonly object hidLock = new object();
        private const string CacheRegistryKey = @"Software\RazerBatteryTray\HyperXCache";

        // ---- 协议常量 (0x66 62 字节报文体) ----
        private const byte ReportId = 0x66;              // 状态查询与上报固定 Report ID
        private const int PacketLength = 62;             // 0x66 报文固定总长度（含 Report ID）
        private const byte CmdGetConnected = 0x82;       // 查询无线连接状态
        private const byte CmdGetCharging = 0x8A;        // 查询充电状态
        private const byte CmdGetBattery = 0x89;         // 查询电量
        private const byte RspConnected = 0x0B;          // 连接状态主动上报
        private const byte RspCharging = 0x0C;           // 充电状态主动上报
        private const byte RspBattery = 0x0D;            // 电量主动上报

        private const int OffsetCmd = 1;                 // 命令 / 响应码
        private const int OffsetState = 2;               // 状态位 1
        private const int OffsetState2 = 3;              // 状态位 2
        private const int OffsetLevel = 4;               // 电量百分比

        // ---- 硬件标识 ----
        public const int VendorIdHp = 0x03F0;            // HP Inc.（HyperX 现属 HP）
        public const int PidCloud3Wireless = 0x05B7;     // 飓风3无线版接收器
        public const int PidCloud3WirelessAlt = 0x0C9D;  // 飓风3无线版接收器（批次变体）
        private const int PidCloud3SWireless = 0x06BE;   // 飓风3S无线接收器：报文协议不同，显式排除

        private const int ReadBudgetMs = 240;            // 单次轮询读取总预算
        private const int SingleReadTimeoutMs = 120;     // 单报文读取等待上限
        private const int QueryThrottleMs = 700;         // 硬件查询节流窗口
        private const int HidpStatusSuccess = 0x00110000; // HidP_GetCaps 成功返回值

        // ---- 运行态缓存 ----
        public static int CachedBatteryPercent = -1;
        public static bool CachedIsCharging = false;
        public static bool CachedIsConnected = false;
        public static string CachedDeviceName = "HyperX 飓风3 无线";
        public static DateTime CachedLastUpdated = DateTime.MinValue;

        private static bool cachedDonglePresent = false;
        private static int detectedPid = PidCloud3Wireless;
        private static int lastQueryTick = 0;
        private static bool hasQueried = false;

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
            if (vid != VendorIdHp) return false;
            return (pid == PidCloud3Wireless || pid == PidCloud3WirelessAlt);
        }

        // 支持 "HID:03F0:05B7" 指纹、设备接口路径等任意包含 VID/PID 的标识
        public static bool IsHyperXDevice(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return false;
            string lower = identifier.ToLowerInvariant();
            bool isVid = lower.Contains("vid_03f0") || lower.Contains("03f0:") || lower.Contains("03f0&");
            if (!isVid) return false;
            return lower.Contains("pid_05b7") || lower.Contains("pid_0c9d") ||
                   lower.Contains(":05b7") || lower.Contains(":0c9d");
        }

        public static string GetModelName(int pid, string productString)
        {
            switch (pid)
            {
                case PidCloud3Wireless:
                case PidCloud3WirelessAlt:
                    return "HyperX 飓风3 无线";
            }

            if (!string.IsNullOrEmpty(productString))
            {
                string cleaned = productString.Replace("HP, Inc", "").Replace("HyperX", "").Trim();
                if (cleaned.Length > 0)
                {
                    return "HyperX " + cleaned;
                }
            }
            return "HyperX 无线耳机";
        }

        // ====================================================================
        // 离线缓存持久化（断电/收纳后仍可显示上次电量）
        // ====================================================================
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
            catch (Exception diagEx) { Diagnostics.Log("Hardware/HyperXDeviceHelper.cs: 已忽略异常", diagEx); }
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
            catch (Exception diagEx) { Diagnostics.Log("Hardware/HyperXDeviceHelper.cs: 已忽略异常", diagEx); }
        }

        // ====================================================================
        // 报文解析（纯函数，便于自动化断言）
        // rx 缓冲区首字节为 Report ID 0x66
        // ====================================================================
        public static void ParseStatusReport(byte[] rx, int length, ref int connected, ref int charging, ref int battery)
        {
            if (rx == null || length < 5) return;
            if (rx[0] != ReportId) return;

            switch (rx[OffsetCmd])
            {
                case CmdGetConnected:
                case RspConnected:
                    connected = (rx[OffsetState] == 1) ? 1 : 0;
                    break;

                case CmdGetBattery:
                case RspBattery:
                    {
                        byte state1 = rx[OffsetState];
                        byte state2 = rx[OffsetState2];
                        byte level = rx[OffsetLevel];
                        // 两个状态位同时为 0 表示该电量报文无效（耳机未开机 / 未连接）
                        if (state1 == 0 && state2 == 0) return;
                        if (level <= 100) battery = level;
                        break;
                    }

                case CmdGetCharging:
                case RspCharging:
                    {
                        byte val = rx[OffsetState];
                        if (val == 1)
                        {
                            charging = 1;
                        }
                        else if (val == 2)
                        {
                            // 2 = 充满：直接锁定 100%
                            charging = 0;
                            battery = 100;
                        }
                        else if (val == 0)
                        {
                            charging = 0;
                        }
                        break;
                    }
            }
        }

        // ====================================================================
        // 对外查询入口
        // ====================================================================
        public static MouseBatteryInfo QueryHyperXDeviceInfo()
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

                string path = FindReceiverPath();
                if (string.IsNullOrEmpty(path))
                {
                    // 接收器未插：清空在线状态，交由 DeviceManager 决定是否展示
                    cachedDonglePresent = false;
                    CachedIsConnected = false;
                    CachedIsCharging = false;
                    lastQueryTick = Environment.TickCount;
                    hasQueried = true;
                    return null;
                }

                cachedDonglePresent = ReadDeviceState(path);
                lastQueryTick = Environment.TickCount;
                hasQueried = true;

                if (!cachedDonglePresent) return null;
                return BuildDeviceInfo();
            }
        }

        private static MouseBatteryInfo BuildDeviceInfo()
        {
            MouseBatteryInfo info = new MouseBatteryInfo();
            info.DeviceId = string.Format("HyperX:{0:X4}:{1:X4}", VendorIdHp, detectedPid);
            info.HardwareFingerprint = string.Format("HID:{0:X4}:{1:X4}", VendorIdHp, detectedPid);
            info.Brand = "HyperX";
            info.Category = DeviceCategory.Headset;
            info.DeviceName = CachedDeviceName;
            info.Transport = "2.4G 无线";
            info.IsCustom = false;
            info.IsDonglePresent = cachedDonglePresent;
            info.IsConnected = CachedIsConnected;
            info.IsSleeping = false;
            info.IsCharging = CachedIsConnected && CachedIsCharging;
            info.BatteryPercent = CachedBatteryPercent;
            info.ExactBatteryPercent = CachedBatteryPercent >= 0 ? CachedBatteryPercent : 0;
            info.LastUpdated = (CachedLastUpdated == DateTime.MinValue) ? DateTime.Now : CachedLastUpdated;
            return info;
        }

        // ====================================================================
        // 设备枚举：仅接受 62 字节输出报文（0x66 厂商集合）的接口
        // ====================================================================
        private static string FindReceiverPath()
        {
            string relaxedPath = null;

            try
            {
                Guid hidGuid;
                RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
                IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return null;

                RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(ifData);
                uint memberIdx = 0;

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
                                if (!string.IsNullOrEmpty(devicePath) && devicePath.ToLowerInvariant().Contains("vid_03f0"))
                                {
                                    int kind = MatchCollection(devicePath);
                                    if (kind == 2)
                                    {
                                        return devicePath;
                                    }
                                    if (kind == 1 && string.IsNullOrEmpty(relaxedPath))
                                    {
                                        relaxedPath = devicePath;
                                    }
                                }
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
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/HyperXDeviceHelper.cs: 已忽略异常", diagEx); }

            return relaxedPath;
        }

        // 返回值: 2 = 白名单 PID 精确命中, 1 = 未知批次 PID 宽松命中, 0 = 不匹配
        private static int MatchCollection(string devicePath)
        {
            IntPtr handle = RazerDeviceHelper.CreateFile(devicePath, 0,
                RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                IntPtr.Zero, RazerDeviceHelper.OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle.ToInt64() == -1) return 0;

            try
            {
                RazerDeviceHelper.HIDD_ATTRIBUTES attr = new RazerDeviceHelper.HIDD_ATTRIBUTES();
                attr.Size = Marshal.SizeOf(attr);
                if (!RazerDeviceHelper.HidD_GetAttributes(handle, ref attr)) return 0;
                if (GetOutputReportLength(handle) != PacketLength) return 0;

                if (IsSupportedVidPid(attr.VendorID, attr.ProductID)) return 2;

                // 飓风3S 无线接收器使用另一套报文协议，避免误解析
                if (attr.VendorID == VendorIdHp && attr.ProductID == PidCloud3SWireless) return 0;

                // 兼容未知批次 PID：VID 属于 HP 且产品名明确标注 HyperX / Cloud 系列
                if (attr.VendorID == VendorIdHp)
                {
                    StringBuilder sb = new StringBuilder(256);
                    if (RazerDeviceHelper.HidD_GetProductString(handle, sb, sb.Capacity))
                    {
                        string prod = sb.ToString().ToLowerInvariant();
                        if (prod.Contains("hyperx") || prod.Contains("cloud")) return 1;
                    }
                }
                return 0;
            }
            catch
            {
                return 0;
            }
            finally
            {
                RazerDeviceHelper.CloseHandle(handle);
            }
        }

        private static int GetOutputReportLength(IntPtr handle)
        {
            IntPtr preparsed;
            if (!RazerDeviceHelper.HidD_GetPreparsedData(handle, out preparsed)) return 0;

            try
            {
                RazerDeviceHelper.HIDP_CAPS caps;
                int status = RazerDeviceHelper.HidP_GetCaps(preparsed, out caps);
                if (status != HidpStatusSuccess) return 0;
                return caps.OutputReportByteLength;
            }
            finally
            {
                RazerDeviceHelper.HidD_FreePreparsedData(preparsed);
            }
        }

        // ====================================================================
        // 硬件会话：三连查询 + 限时读取
        // ====================================================================
        private static bool ReadDeviceState(string path)
        {
            IntPtr handle = RazerDeviceHelper.CreateFile(path, GenericRead | GenericWrite,
                RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                IntPtr.Zero, RazerDeviceHelper.OPEN_EXISTING, FileFlagOverlapped, IntPtr.Zero);

            if (handle == IntPtr.Zero || handle.ToInt64() == -1)
            {
                CachedIsConnected = false;
                CachedIsCharging = false;
                return false;
            }

            IntPtr evt = CreateEvent(IntPtr.Zero, true, false, null);
            if (evt == IntPtr.Zero)
            {
                RazerDeviceHelper.CloseHandle(handle);
                CachedIsConnected = false;
                return false;
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

                byte[] rx = new byte[PacketLength];

                // 1. 丢弃接收器积压的历史状态报文
                for (int i = 0; i < 8; i++)
                {
                    if (ReadReport(handle, evt, rx, 0) <= 0) break;
                }

                // 2. 依次下发连接 / 充电 / 电量查询
                SendReport(handle, evt, CmdGetConnected);
                SendReport(handle, evt, CmdGetCharging);
                SendReport(handle, evt, CmdGetBattery);

                int connected = -1;
                int charging = -1;
                int battery = -1;
                int readCount = 0;
                long deadline = Environment.TickCount + ReadBudgetMs;

                while (true)
                {
                    int remain = (int)(deadline - Environment.TickCount);
                    if (remain <= 0) break;

                    int timeout = (remain < SingleReadTimeoutMs) ? remain : SingleReadTimeoutMs;
                    int len = ReadReport(handle, evt, rx, timeout);
                    if (len <= 0) continue;

                    readCount++;
                    ParseStatusReport(rx, len, ref connected, ref charging, ref battery);

                    if (connected >= 0 && battery >= 0) break;
                    if (connected == 0 && readCount >= 2) break;
                }

                if (connected >= 0)
                {
                    CachedIsConnected = (connected == 1);
                }
                else
                {
                    // 完全无响应：耳机未开机或已休眠
                    CachedIsConnected = false;
                }

                if (battery >= 0)
                {
                    int prevBatt = CachedBatteryPercent;
                    bool prevChg = CachedIsCharging;

                    CachedBatteryPercent = battery;
                    if (charging >= 0) CachedIsCharging = (charging == 1);
                    CachedLastUpdated = DateTime.Now;

                    if (prevBatt != CachedBatteryPercent || prevChg != CachedIsCharging)
                    {
                        SaveHardwareCache();
                    }
                }
                else
                {
                    CachedIsCharging = false;
                }

                if (!CachedIsConnected)
                {
                    CachedIsCharging = false;
                }

                return true;
            }
            catch
            {
                return true;
            }
            finally
            {
                CloseHandle(evt);
                RazerDeviceHelper.CloseHandle(handle);
            }
        }

        private static bool SendReport(IntPtr handle, IntPtr evt, byte cmd)
        {
            byte[] tx = new byte[PacketLength];
            tx[0] = ReportId;
            tx[OffsetCmd] = cmd;

            HID_OVERLAPPED ov = new HID_OVERLAPPED();
            ov.hEvent = evt;
            ResetEvent(evt);

            uint written = 0;
            if (WriteFile(handle, tx, (uint)tx.Length, out written, ref ov))
            {
                return true;
            }

            int err = Marshal.GetLastWin32Error();
            if (err != ErrorIoPending)
            {
                // 回退：部分固件仅接受控制端点 SET_REPORT
                return RazerDeviceHelper.HidD_SetOutputReport(handle, tx, tx.Length);
            }

            uint wait = WaitForSingleObject(evt, 80);
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
