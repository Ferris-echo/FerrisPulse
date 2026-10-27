// ============================================================================
// 模块名称: SteelSeriesDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   赛睿 (SteelSeries) Arctis Nova 5 / 5X 无线耳机 2.4G 接收器专属 HID 协议实现：
//   0x00B0 状态查询输出报文、0x00B0 状态上报（连接 / 电量 / 充电）解析、
//   离线电量缓存与注册表持久化，并接入 DeviceManager 统一调度。
// 依赖关系: RazerDeviceHelper.cs (HID P/Invoke 基座), MouseBatteryInfo.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、协议解析或签名时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止字符串插值、空条件运算符、nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// 4. [协议来源]: 协议依据 Linux hid-steelseries Arctis 系列驱动补丁与 HeadsetControl
//    开源逆向实现（Nova 5: request 00 B0 -> input byte3=battery, byte4=charging）。
// ============================================================================

using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    public static class SteelSeriesDeviceHelper
    {
        private static readonly object hidLock = new object();
        private const string CacheRegistryKey = @"Software\RazerBatteryTray\SteelSeriesCache";

        // ---- 协议常量 (65 字节 HID 报文，首字节为 Report ID 0x00) ----
        private const byte StatusReportId = 0x00;        // 无编号报文的 Report ID 占位
        private const byte CmdStatusRequest = 0xB0;      // 状态查询 / 状态上报标识
        private const int PacketLength = 65;             // 含 Report ID 的完整报文长度
        private const int OffsetConnection = 2;          // 连接状态: 0x02 = 耳机离线
        private const int OffsetBattery = 4;             // 电量百分比 (0-100)
        private const int OffsetCharging = 5;            // 充电状态: 0x01 = 充电中
        private const byte StatusOffline = 0x02;         // 耳机未开机 / 未连接
        private const byte ChargingActive = 0x01;        // 正在充电
        private const ushort VendorUsagePage = 0xFFC0;   // 赛睿厂商自定义 Usage Page
        private const int HidpStatusSuccess = 0x00110000;

        // ---- 硬件标识 ----
        public const int VendorIdSteelSeries = 0x1038;
        public const int PidArctisNova5 = 0x2232;        // Arctis Nova 5 基站
        public const int PidArctisNova5X = 0x2253;       // Arctis Nova 5X 基站

        private const int ReadBudgetMs = 360;            // 单次轮询读取总预算
        private const int SingleReadTimeoutMs = 140;     // 单报文读取等待上限
        private const int QueryThrottleMs = 700;         // 硬件查询节流窗口

        // ---- 运行态缓存 ----
        public static int CachedBatteryPercent = -1;
        public static bool CachedIsCharging = false;
        public static bool CachedIsConnected = false;
        public static string CachedDeviceName = "Arctis Nova 5";
        public static DateTime CachedLastUpdated = DateTime.MinValue;

        private static bool cachedDonglePresent = false;
        private static int detectedPid = PidArctisNova5;
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
            if (vid != VendorIdSteelSeries) return false;
            return (pid == PidArctisNova5 || pid == PidArctisNova5X);
        }

        // 支持 "HID:1038:2232" 指纹、设备接口路径等任意包含 VID/PID 的标识
        public static bool IsSteelSeriesDevice(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return false;
            string lower = identifier.ToLowerInvariant();
            bool isVid = lower.Contains("vid_1038") || lower.Contains("1038:") || lower.Contains("1038&");
            if (!isVid) return false;
            return lower.Contains("pid_2232") || lower.Contains("pid_2253") ||
                   lower.Contains(":2232") || lower.Contains(":2253");
        }

        public static string GetModelName(int pid, string productString)
        {
            if (!string.IsNullOrEmpty(productString))
            {
                string prod = productString.Trim();
                if (prod.IndexOf("Nova 5X", StringComparison.OrdinalIgnoreCase) >= 0) return "Arctis Nova 5X";
                if (prod.IndexOf("Nova 5", StringComparison.OrdinalIgnoreCase) >= 0) return "Arctis Nova 5";
                if (prod.IndexOf("Arctis", StringComparison.OrdinalIgnoreCase) >= 0) return prod;
            }

            switch (pid)
            {
                case PidArctisNova5X:
                    return "Arctis Nova 5X";
                case PidArctisNova5:
                    return "Arctis Nova 5";
            }
            return "Arctis Nova 无线耳机";
        }

        // ====================================================================
        // 离线缓存持久化（接收器断电 / 收纳后仍可显示上次电量）
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
            catch (Exception diagEx) { Diagnostics.Log("Hardware/SteelSeriesDeviceHelper.cs: 已忽略异常", diagEx); }
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
            catch (Exception diagEx) { Diagnostics.Log("Hardware/SteelSeriesDeviceHelper.cs: 已忽略异常", diagEx); }
        }

        // ====================================================================
        // 报文解析（纯函数，便于自动化断言）
        // rx 缓冲区首字节为 Report ID 0x00，随后为 0xB0 状态上报标识。
        // 对应 Linux 驱动 raw_event 去掉 Report ID 后的 data 布局：
        //   data[1] 连接状态 / data[3] 电量 / data[4] 充电状态
        // ====================================================================
        public static void ParseStatusReport(byte[] rx, int length, ref int connected, ref int charging, ref int battery)
        {
            if (rx == null || length < 6) return;
            if (rx[0] != StatusReportId) return;
            if (rx[1] != CmdStatusRequest) return;

            // 0x02 = 耳机离线（接收器在线但耳机未开机 / 超出范围）
            if (rx[OffsetConnection] == StatusOffline)
            {
                connected = 0;
                charging = 0;
                return;
            }

            connected = 1;

            byte level = rx[OffsetBattery];
            if (level <= 100) battery = level;

            charging = (rx[OffsetCharging] == ChargingActive) ? 1 : 0;
        }

        // ====================================================================
        // 对外查询入口
        // ====================================================================
        public static MouseBatteryInfo QuerySteelSeriesDeviceInfo()
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
            info.DeviceId = string.Format("SteelSeries:{0:X4}:{1:X4}", VendorIdSteelSeries, detectedPid);
            info.HardwareFingerprint = string.Format("HID:{0:X4}:{1:X4}", VendorIdSteelSeries, detectedPid);
            info.Brand = "SteelSeries";
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
        // 设备枚举：仅接受 65 字节输出报文 + 0xFFC0 厂商集合的接口
        // ====================================================================
        private static string FindReceiverPath()
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
                                if (!string.IsNullOrEmpty(devicePath) && devicePath.ToLowerInvariant().Contains("vid_1038"))
                                {
                                    if (MatchCollection(devicePath)) return devicePath;
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
            catch (Exception diagEx) { Diagnostics.Log("Hardware/SteelSeriesDeviceHelper.cs: 已忽略异常", diagEx); }

            return null;
        }

        private static bool MatchCollection(string devicePath)
        {
            IntPtr handle = RazerDeviceHelper.CreateFile(devicePath, 0,
                RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                IntPtr.Zero, RazerDeviceHelper.OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;

            try
            {
                RazerDeviceHelper.HIDD_ATTRIBUTES attr = new RazerDeviceHelper.HIDD_ATTRIBUTES();
                attr.Size = Marshal.SizeOf(attr);
                if (!RazerDeviceHelper.HidD_GetAttributes(handle, ref attr)) return false;
                if (!IsSupportedVidPid(attr.VendorID, attr.ProductID)) return false;

                IntPtr preparsed;
                if (!RazerDeviceHelper.HidD_GetPreparsedData(handle, out preparsed)) return false;

                try
                {
                    RazerDeviceHelper.HIDP_CAPS caps;
                    int status = RazerDeviceHelper.HidP_GetCaps(preparsed, out caps);
                    if (status != HidpStatusSuccess) return false;
                    if (caps.UsagePage != VendorUsagePage) return false;
                    if (caps.OutputReportByteLength != PacketLength) return false;
                    if (caps.InputReportByteLength != PacketLength) return false;
                    return true;
                }
                finally
                {
                    RazerDeviceHelper.HidD_FreePreparsedData(preparsed);
                }
            }
            catch
            {
                return false;
            }
            finally
            {
                RazerDeviceHelper.CloseHandle(handle);
            }
        }

        // ====================================================================
        // 硬件会话：状态查询 + 限时读取
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

                // 2. 下发状态查询报文 00 B0
                SendStatusRequest(handle, evt);

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
                    if (connected == 0 && readCount >= 1) break;
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

        private static bool SendStatusRequest(IntPtr handle, IntPtr evt)
        {
            byte[] tx = new byte[PacketLength];
            tx[0] = StatusReportId;
            tx[1] = CmdStatusRequest;

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
