// ============================================================================
// 模块名称: EndgameGearDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   Endgame Gear (VID 0x3367) 无线鼠标专属驱动。
//   已实机适配: XM2we (0x1961) 2.4G 接收器 —— 17 字节 Beken 0x55 校验和引擎。
//   核心难点: 该接收器为【双端点分离】结构，命令下发与应答回读不在同一个集合：
//     - 命令: UsagePage 0xFF02 / Usage 0x0002，17 字节 Feature Report，Report ID 0x08
//     - 应答: UsagePage 0xFF01 / Usage 0x0000，17 字节 Interrupt IN，Report ID 0x09
//   因此严禁沿用“SetFeature 后原地 GetFeature”的单句柄范式（会永远读到全零）。
//
//   实机抓包 (XM2we, 电量 65%):
//     TX Feature : 08 04 00 00 00 00 00 00 00 00 00 00 00 00 00 00 49
//     RX Input   : 09 04 00 00 00 02 41 00 00 00 00 00 00 00 00 00 05
//                  ↑  Report ID 0x09   ↑cmd   ↑len=2 ↑65% ↑未充电      ↑0x55 校验和
//
// 依赖关系: RazerDeviceHelper.cs (P/Invoke), MouseBatteryInfo.cs, DriverStateStore.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、协议解析或签名时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止 $"..." 插值、禁止 ?. 空条件运算符、禁止 nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// 4. [物理事实优先]: DPI / 回报率写指令尚未实机验证，必须保持 SupportsDpi = false，
//    严禁凭空开启能力开关或盲扫异构协议。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace RazerBatteryTray
{
    public static class EndgameGearDeviceHelper
    {
        public const int VendorId = 0x3367;

        // XM2we 2.4G 接收器（本机实机验证：3367:1961 / "XM2we Gaming Receiver"）
        public const int PidXm2we = 0x1961;
        // OP1we 2.4G 接收器（与 XM2we 同族，社区同协议；本机无实机，仅按同族识别）
        public const int PidOp1we = 0x1960;

        public const string DriverId = "BuiltIn:EndgameGear";

        private const byte CmdReportId = 0x08;
        private const byte ResponseReportId = 0x09;
        private const byte CmdBattery = 0x04;
        private const int FrameLength = 17;
        private const byte ChecksumSeed = 0x55;

        private const ushort UsagePageCommand = 0xFF02;
        private const ushort UsagePageResponse = 0xFF01;
        private const ushort UsageCommand = 0x0002;
        private const ushort UsageResponse = 0x0000;

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        private const int ERROR_IO_PENDING = 997;
        private const uint WAIT_OBJECT_0 = 0;
        private const int ReadTimeoutMs = 600;
        private const int MaxReadAttempts = 4;

        private static readonly object queryLock = new object();

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
        private static extern bool ResetEvent(IntPtr hEvent);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, ref OVERLAPPED lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetOverlappedResult(IntPtr hFile, ref OVERLAPPED lpOverlapped, out uint lpNumberOfBytesTransferred, bool bWait);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CancelIo(IntPtr hFile);

        #region 协议编解码 (纯函数, 便于离线回放测试)

        /// <summary>
        /// Beken 0x55 互补校验和：sum = ReportID + payload，返回 (0x55 - (sum &amp; 0xFF)) &amp; 0xFF。
        /// </summary>
        public static byte ComputeChecksum(byte reportId, byte[] buffer, int offset, int count)
        {
            int sum = reportId;
            for (int i = 0; i < count; i++)
            {
                sum += buffer[offset + i] & 0xFF;
            }
            return (byte)((ChecksumSeed - (sum & 0xFF)) & 0xFF);
        }

        /// <summary>
        /// 校验一帧 17 字节报文：前 16 字节累加后按 0x55 取补，应与第 17 字节相等。
        /// </summary>
        public static bool VerifyChecksum(byte[] frame, int length)
        {
            if (frame == null || length < FrameLength || frame.Length < FrameLength) return false;
            int sum = 0;
            for (int i = 0; i < FrameLength - 1; i++)
            {
                sum += frame[i] & 0xFF;
            }
            return ((ChecksumSeed - (sum & 0xFF)) & 0xFF) == frame[FrameLength - 1];
        }

        /// <summary>
        /// 构造电量查询帧：08 04 00 ... 49 (Report ID 0x08 / CMD 0x04)。
        /// </summary>
        public static byte[] BuildBatteryQuery()
        {
            byte[] tx = new byte[FrameLength];
            tx[0] = CmdReportId;
            tx[1] = CmdBattery;
            tx[FrameLength - 1] = ComputeChecksum(CmdReportId, tx, 1, FrameLength - 2);
            return tx;
        }

        /// <summary>
        /// 解析应答帧（Report ID 0x09 / CMD 0x04）：
        ///   [0]=0x09 [1]=0x04 [2..4]=保留 [5]=数据长度 [6]=电量 [7]=充电(1) [8..9]=电压 mV [16]=校验和
        /// </summary>
        public static bool ParseBatteryFrame(byte[] frame, int length, out int percent, out bool isCharging, out int millivolts)
        {
            percent = -1;
            isCharging = false;
            millivolts = 0;

            if (frame == null || length < 8 || frame.Length < length) return false;
            if (frame[0] != ResponseReportId) return false;
            if (frame[1] != CmdBattery) return false;
            if (!VerifyChecksum(frame, length)) return false;

            int payloadLength = frame[5];
            if (payloadLength < 2) return false;

            int pct = frame[6];
            if (pct > 100) return false;

            percent = pct;
            // 物理事实优先：仅当状态位严格等于 0x01 才认定充电，规避无线链路的伪充电抖动位。
            isCharging = (frame[7] == 0x01);

            if (payloadLength >= 4 && length >= 10)
            {
                millivolts = (frame[8] << 8) | frame[9];
            }
            return true;
        }

        public static bool IsSupportedPid(int pid)
        {
            return pid == PidXm2we || pid == PidOp1we;
        }

        public static string ResolveModelName(int pid)
        {
            if (pid == PidXm2we) return "XM2we Wireless";
            if (pid == PidOp1we) return "OP1we Wireless";
            return "";
        }

        #endregion

        #region 双端点枚举与实机查询

        private sealed class EndgameEndpoint
        {
            public int Pid;
            public string CommandPath;
            public string ResponsePath;
            public string ProductString;
        }

        /// <summary>
        /// 扫描全部 Endgame Gear 接收器并返回电量信息。设备休眠时回退 DriverStateStore 缓存。
        /// </summary>
        public static List<MouseBatteryInfo> QueryAllEndgameGearDevices()
        {
            List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
            Dictionary<int, EndgameEndpoint> endpoints = EnumerateEndpoints();
            if (endpoints.Count == 0) return result;

            lock (queryLock)
            {
                foreach (KeyValuePair<int, EndgameEndpoint> pair in endpoints)
                {
                    EndgameEndpoint ep = pair.Value;
                    if (ep == null || string.IsNullOrEmpty(ep.CommandPath) || string.IsNullOrEmpty(ep.ResponsePath)) continue;

                    int percent;
                    bool charging;
                    int millivolts;
                    bool ok = QueryBattery(ep.CommandPath, ep.ResponsePath, out percent, out charging, out millivolts);

                    string deviceKey = string.Format("EGG:{0:X4}:{1:X4}", VendorId, ep.Pid);

                    int cachedPercent;
                    bool cachedCharging;
                    string cachedName;
                    DateTime cachedUpdated;
                    bool hasCache = DriverStateStore.TryLoadBatteryState(deviceKey, out cachedPercent, out cachedCharging, out cachedName, out cachedUpdated);

                    if (ok && percent >= 0 && percent <= 100)
                    {
                        DriverStateStore.SaveBatteryState(deviceKey, percent, charging, ResolveModelName(ep.Pid));
                    }

                    MouseBatteryInfo info = new MouseBatteryInfo();
                    info.DeviceId = deviceKey;
                    info.HardwareFingerprint = string.Format("HID:{0:X4}:{1:X4}", VendorId, ep.Pid);
                    info.HardwareId = info.HardwareFingerprint;
                    info.DriverId = DriverId;
                    info.DeviceName = ResolveModelName(ep.Pid);
                    info.Brand = "Endgame Gear";
                    info.Category = DeviceCategory.Mouse;
                    info.Transport = "2.4G 无线";
                    info.IsDonglePresent = true;
                    info.IsCustom = false;

                    if (ok)
                    {
                        info.IsConnected = true;
                        info.IsSleeping = false;
                        info.BatteryPercent = percent;
                        info.ExactBatteryPercent = percent;
                        info.IsCharging = charging;
                        info.BatteryVoltageMv = millivolts;
                        info.LastUpdated = DateTime.Now;
                    }
                    else if (hasCache)
                    {
                        info.IsConnected = false;
                        info.IsSleeping = true;
                        info.BatteryPercent = cachedPercent;
                        info.ExactBatteryPercent = cachedPercent;
                        info.IsCharging = cachedCharging;
                        info.LastUpdated = cachedUpdated;
                    }
                    else
                    {
                        info.IsConnected = false;
                        info.IsSleeping = true;
                        info.BatteryPercent = -1;
                        info.ExactBatteryPercent = 0;
                        info.IsCharging = false;
                        info.LastUpdated = DateTime.Now;
                    }

                    info.MaxPollingRate = 1000;
                    info.SupportedPollingRates = new int[] { 125, 250, 500, 1000 };
                    info.PollingRate = 1000;

                    info.Capabilities.SupportsBattery = true;
                    info.Capabilities.SupportsDpi = false;
                    info.Capabilities.SupportsDpiStages = false;
                    info.Capabilities.SupportsXyIndependence = false;
                    info.Capabilities.SupportsPollingRate = false;
                    info.Capabilities.SupportsHardwareDpiMonitor = false;

                    result.Add(info);
                }
            }

            return result;
        }

        private static Dictionary<int, EndgameEndpoint> EnumerateEndpoints()
        {
            Dictionary<int, EndgameEndpoint> map = new Dictionary<int, EndgameEndpoint>();
            Guid hidGuid;
            RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
            IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
            if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return map;

            RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
            ifData.cbSize = Marshal.SizeOf(ifData);
            uint memberIndex = 0;

            try
            {
                while (RazerDeviceHelper.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIndex++, ref ifData))
                {
                    uint requiredSize;
                    RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out requiredSize, IntPtr.Zero);
                    if (requiredSize == 0) continue;

                    IntPtr detailBuffer = Marshal.AllocHGlobal((int)requiredSize);
                    // SP_DEVICE_INTERFACE_DETAIL_DATA.cbSize: x64 = 8, x86(Unicode) = 6
                    Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);

                    try
                    {
                        if (!RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, requiredSize, out requiredSize, IntPtr.Zero))
                        {
                            continue;
                        }

                        string devicePath = Marshal.PtrToStringUni(new IntPtr(detailBuffer.ToInt64() + 4));
                        if (string.IsNullOrEmpty(devicePath)) continue;
                        // 键盘集合受内核防监听保护，且与鼠标电量无关，直接跳过。
                        if (devicePath.EndsWith("\\kbd", StringComparison.OrdinalIgnoreCase)) continue;

                        int vid;
                        int pid;
                        ushort usagePage;
                        ushort usage;
                        ushort inputLength;
                        ushort featureLength;
                        string productString;
                        if (!ProbeInterface(devicePath, out vid, out pid, out usagePage, out usage, out inputLength, out featureLength, out productString))
                        {
                            continue;
                        }

                        if (vid != VendorId || !IsSupportedPid(pid)) continue;

                        EndgameEndpoint ep;
                        if (!map.TryGetValue(pid, out ep))
                        {
                            ep = new EndgameEndpoint();
                            ep.Pid = pid;
                            ep.ProductString = productString;
                            map[pid] = ep;
                        }

                        if (usagePage == UsagePageCommand && usage == UsageCommand && featureLength == FrameLength)
                        {
                            ep.CommandPath = devicePath;
                        }
                        else if (usagePage == UsagePageResponse && usage == UsageResponse && inputLength == FrameLength)
                        {
                            ep.ResponsePath = devicePath;
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detailBuffer);
                    }
                }
            }
            finally
            {
                RazerDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);
            }

            return map;
        }

        private static bool ProbeInterface(string devicePath, out int vid, out int pid, out ushort usagePage, out ushort usage, out ushort inputLength, out ushort featureLength, out string productString)
        {
            vid = 0;
            pid = 0;
            usagePage = 0;
            usage = 0;
            inputLength = 0;
            featureLength = 0;
            productString = "";

            // 探测属性只需 FILE_READ_ATTRIBUTES(0)，可安全打开键盘/厂商集合。
            IntPtr handle = RazerDeviceHelper.CreateFile(
                devicePath,
                0,
                RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                IntPtr.Zero,
                RazerDeviceHelper.OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;

            try
            {
                RazerDeviceHelper.HIDD_ATTRIBUTES attr = new RazerDeviceHelper.HIDD_ATTRIBUTES();
                attr.Size = Marshal.SizeOf(attr);
                if (!RazerDeviceHelper.HidD_GetAttributes(handle, ref attr)) return false;

                vid = attr.VendorID;
                pid = attr.ProductID;
                if (vid != VendorId) return true;

                IntPtr preparsed;
                if (RazerDeviceHelper.HidD_GetPreparsedData(handle, out preparsed) && preparsed != IntPtr.Zero)
                {
                    try
                    {
                        RazerDeviceHelper.HIDP_CAPS caps;
                        if (RazerDeviceHelper.HidP_GetCaps(preparsed, out caps) == unchecked((int)0x00110000))
                        {
                            usagePage = caps.UsagePage;
                            usage = caps.Usage;
                            inputLength = caps.InputReportByteLength;
                            featureLength = caps.FeatureReportByteLength;
                        }
                    }
                    finally
                    {
                        RazerDeviceHelper.HidD_FreePreparsedData(preparsed);
                    }
                }

                StringBuilder sb = new StringBuilder(260);
                if (RazerDeviceHelper.HidD_GetProductString(handle, sb, sb.Capacity))
                {
                    productString = sb.ToString().Trim();
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

            return true;
        }

        /// <summary>
        /// 双端点查询：Feature 0x08 下发，Interrupt IN 0x09 回收。
        /// </summary>
        public static bool QueryBattery(string commandPath, string responsePath, out int percent, out bool isCharging, out int millivolts)
        {
            percent = -1;
            isCharging = false;
            millivolts = 0;
            if (string.IsNullOrEmpty(commandPath) || string.IsNullOrEmpty(responsePath)) return false;

            IntPtr responseHandle = RazerDeviceHelper.CreateFile(
                responsePath,
                GENERIC_READ,
                RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                IntPtr.Zero,
                RazerDeviceHelper.OPEN_EXISTING,
                FILE_FLAG_OVERLAPPED,
                IntPtr.Zero);

            if (responseHandle == IntPtr.Zero || responseHandle.ToInt64() == -1) return false;

            IntPtr commandHandle = RazerDeviceHelper.CreateFile(
                commandPath,
                GENERIC_READ | GENERIC_WRITE,
                RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                IntPtr.Zero,
                RazerDeviceHelper.OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (commandHandle == IntPtr.Zero || commandHandle.ToInt64() == -1)
            {
                RazerDeviceHelper.CloseHandle(responseHandle);
                return false;
            }

            IntPtr hEvent = CreateEvent(IntPtr.Zero, true, false, null);
            if (hEvent == IntPtr.Zero)
            {
                RazerDeviceHelper.CloseHandle(commandHandle);
                RazerDeviceHelper.CloseHandle(responseHandle);
                return false;
            }

            try
            {
                byte[] tx = BuildBatteryQuery();
                if (!RazerDeviceHelper.HidD_SetFeature(commandHandle, tx, tx.Length)) return false;

                for (int attempt = 0; attempt < MaxReadAttempts; attempt++)
                {
                    byte[] rx = new byte[FrameLength];
                    // 超时即判定鼠标休眠/关机：立即结束本轮，避免多次串行等待阻塞后台轮询。
                    if (!ReadFrame(responseHandle, hEvent, rx, ReadTimeoutMs)) break;
                    if (ParseBatteryFrame(rx, rx.Length, out percent, out isCharging, out millivolts)) return true;
                }
            }
            catch
            {
            }
            finally
            {
                RazerDeviceHelper.CloseHandle(hEvent);
                RazerDeviceHelper.CloseHandle(commandHandle);
                RazerDeviceHelper.CloseHandle(responseHandle);
            }

            return false;
        }

        private static bool ReadFrame(IntPtr handle, IntPtr hEvent, byte[] buffer, int timeoutMs)
        {
            OVERLAPPED overlapped = new OVERLAPPED();
            overlapped.hEvent = hEvent;
            ResetEvent(hEvent);

            uint bytesRead = 0;
            bool ok = ReadFile(handle, buffer, (uint)buffer.Length, out bytesRead, ref overlapped);

            if (!ok)
            {
                int error = Marshal.GetLastWin32Error();
                if (error != ERROR_IO_PENDING) return false;
                if (WaitForSingleObject(hEvent, (uint)timeoutMs) != WAIT_OBJECT_0)
                {
                    CancelIo(handle);
                    return false;
                }
                if (!GetOverlappedResult(handle, ref overlapped, out bytesRead, false)) return false;
            }

            return bytesRead > 0;
        }

        #endregion
    }
}
