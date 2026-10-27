// ============================================================================
// 模块名称: RazerHeadsetHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   雷蛇无线耳机 (BlackShark V2 Pro 2.4, 1532:0555) 私有 64 字节 HID 协议实现。
//   该机型没有 Feature Report (HidP_GetCaps.FeatureReportByteLength = 0)，所以既有的
//   90 字节 Feature 报文链路 (RazerDeviceHelper) 完全无法识别它，必须走中断 OUT 端点。
//
//   ── 报文格式 (由 USBPcap 抓取 Synapse 实测流量还原) ──
//   命令 (HOST → 64B):  02 80 08 00 00 'P' 'A' 08 00 03 <CMD> 00 ...
//   应答 (64B → HOST):  02 <len> 'P' 'I' <seq> <4B 计数> 00 <len16 LE> <CMD> 01 01 <value...>
//   命令表 (来自 Synapse 日志 commandDesc):
//     0x00 Get Serial Number            0x20 Get wireless Connection Status
//     0x21 Get Battery Status           0x2A Get charging status
//     0x2C Get Auto power off Status
//
//   传输必须用 WriteFile 写中断 OUT 端点；HidD_SetOutputReport 会被拒绝 (ERROR_INVALID_PARAMETER)。
//
// 依赖关系: MouseBatteryInfo.cs, DeviceCategory.cs (零第三方依赖, 纯 Win32 P/Invoke)
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、协议解析或签名时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    public static class RazerHeadsetHelper
    {
        private static readonly object hidLock = new object();
        private const string CacheRegistryKey = @"Software\RazerBatteryTray\RazerHeadsetCache";

        // ==================== 设备标识 ====================
        public const int VidRazer = 0x1532;
        public const int PidBlackSharkV2Pro = 0x0555;

        private const ushort UsagePageVendor = 0xFF00;

        // ==================== 协议常量 ====================
        public const int ReportSize = 64;
        public const byte ReportId = 0x02;
        public const byte OutMagicP = 0x50; // 'P'
        public const byte OutMagicA = 0x41; // 'A'
        public const byte InMagicI = 0x49;  // 'I'
        public const byte Sequence = 0x08;

        public const byte CmdGetSerial = 0x00;
        public const byte CmdGetConnection = 0x20;
        public const byte CmdGetBattery = 0x21;
        public const byte CmdGetCharging = 0x2A;
        public const byte CmdGetAutoPowerOff = 0x2C;

        // ==================== Win32 常量 ====================
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        private const uint DIGCF_PRESENT = 0x02;
        private const uint DIGCF_DEVICEINTERFACE = 0x10;
        private const int ERROR_IO_PENDING = 997;
        private const uint WAIT_OBJECT_0 = 0;
        private const int SendTimeoutMs = 300;
        private const int ReadWindowMs = 400;
        private const int ReadChunkMs = 80;

        #region 数据结构

        [StructLayout(LayoutKind.Sequential)]
        private struct SP_DEVICE_INTERFACE_DATA
        {
            public int cbSize;
            public Guid InterfaceClassGuid;
            public int Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDD_ATTRIBUTES
        {
            public int Size;
            public ushort VendorID;
            public ushort ProductID;
            public ushort VersionNumber;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDP_CAPS
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
        private struct OVERLAPPED
        {
            public IntPtr Internal;
            public IntPtr InternalHigh;
            public int Offset;
            public int OffsetHigh;
            public IntPtr hEvent;
        }

        #endregion

        #region Win32 P/Invoke

        [DllImport("hid.dll")]
        private static extern void HidD_GetHidGuid(out Guid hidGuid);

        [DllImport("hid.dll")]
        private static extern bool HidD_GetAttributes(IntPtr hidDeviceObject, ref HIDD_ATTRIBUTES attributes);

        [DllImport("hid.dll")]
        private static extern bool HidD_GetPreparsedData(IntPtr hidDeviceObject, out IntPtr preparsedData);

        [DllImport("hid.dll")]
        private static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

        [DllImport("hid.dll")]
        private static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);

        [DllImport("hid.dll", CharSet = CharSet.Unicode)]
        private static extern bool HidD_GetProductString(IntPtr hidDeviceObject, StringBuilder buffer, int bufferLength);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, IntPtr enumerator, IntPtr hwndParent, uint flags);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string fileName, uint desiredAccess, uint shareMode, IntPtr securityAttributes, uint creationDisposition, uint flagsAndAttributes, IntPtr templateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, ref OVERLAPPED lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, ref OVERLAPPED lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetOverlappedResult(IntPtr hFile, ref OVERLAPPED lpOverlapped, out uint lpNumberOfBytesTransferred, bool bWait);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CancelIo(IntPtr hFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, IntPtr lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ResetEvent(IntPtr hEvent);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        #endregion

        #region 协议编解码 (纯函数, 可独立单测)

        /// <summary>
        /// 构造 64 字节命令帧: 02 80 08 00 00 'P' 'A' 08 00 03 CMD 00 ...
        /// </summary>
        public static byte[] BuildFrame(byte cmd)
        {
            byte[] frame = new byte[ReportSize];
            frame[0] = ReportId;
            frame[1] = 0x80;
            frame[2] = 0x08;
            frame[5] = OutMagicP;
            frame[6] = OutMagicA;
            frame[7] = Sequence;
            frame[9] = 0x03;
            frame[10] = cmd;
            return frame;
        }

        /// <summary>
        /// 解析应答帧, 返回 value 字节 (rx[15])。帧不合法/命令不匹配/状态非成功时返回 -1。
        /// </summary>
        public static int ParseReplyValue(byte[] reply, byte cmd)
        {
            if (reply == null || reply.Length < 16) return -1;
            if (reply[2] != OutMagicP || reply[3] != InMagicI) return -1;
            if (reply[12] != cmd) return -1;
            if (reply[13] != 0x01) return -1;
            return reply[15];
        }

        public static bool IsRazerHeadset(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return false;
            string lower = identifier.ToLowerInvariant();
            if (lower.Contains("vid_1532&pid_0555") || lower.Contains("1532:0555") || lower.Contains("blackshark"))
            {
                return true;
            }
            return false;
        }

        public static string GetHeadsetModelName(int pid, string productString)
        {
            if (!string.IsNullOrEmpty(productString))
            {
                string cleaned = productString.Trim();
                if (cleaned.Length > 0 && cleaned.IndexOf("HID", StringComparison.OrdinalIgnoreCase) != 0)
                {
                    return cleaned;
                }
            }
            switch (pid)
            {
                case PidBlackSharkV2Pro: return "Razer BlackShark V2 Pro 2.4";
            }
            return "Razer 无线耳机";
        }

        #endregion

        #region 设备枚举与查询

        public static List<MouseBatteryInfo> QueryAllRazerHeadsets()
        {
            List<MouseBatteryInfo> list = new List<MouseBatteryInfo>();

            lock (hidLock)
            {
                Guid hidGuid;
                HidD_GetHidGuid(out hidGuid);
                IntPtr devInfo = SetupDiGetClassDevs(ref hidGuid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return list;

                try
                {
                    SP_DEVICE_INTERFACE_DATA ifData = new SP_DEVICE_INTERFACE_DATA();
                    ifData.cbSize = Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
                    uint memberIdx = 0;
                    HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    while (SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                    {
                        string devicePath = GetDevicePath(devInfo, ref ifData);
                        if (string.IsNullOrEmpty(devicePath) || seenPaths.Contains(devicePath)) continue;
                        seenPaths.Add(devicePath);

                        MouseBatteryInfo info = QuerySingleDevice(devicePath);
                        if (info != null) list.Add(info);
                    }
                }
                finally
                {
                    SetupDiDestroyDeviceInfoList(devInfo);
                }
            }

            return list;
        }

        private static MouseBatteryInfo QuerySingleDevice(string devicePath)
        {
            HIDD_ATTRIBUTES attr;
            HIDP_CAPS caps;
            if (!ReadInterfaceIdentity(devicePath, out attr, out caps)) return null;
            if (attr.VendorID != VidRazer) return null;
            if (attr.ProductID != PidBlackSharkV2Pro) return null;
            if (caps.UsagePage != UsagePageVendor) return null;
            if (caps.OutputReportByteLength < ReportSize) return null;

            string deviceId = string.Format("RazerHeadset:{0:X4}:{1:X4}", VidRazer, attr.ProductID);

            MouseBatteryInfo dev = new MouseBatteryInfo();
            dev.DeviceId = deviceId;
            dev.HardwareId = devicePath;
            dev.HardwareFingerprint = string.Format("HID:{0:X4}:{1:X4}", VidRazer, attr.ProductID);
            dev.Brand = "Razer";
            dev.Category = DeviceCategory.Headset;
            dev.Transport = "2.4G 无线";
            dev.IsCustom = false;
            dev.IsDonglePresent = true;

            IntPtr handle = OpenDevice(devicePath, true);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1))
            {
                return null;
            }

            try
            {
                dev.DeviceName = GetHeadsetModelName(attr.ProductID, ReadProductString(handle));

                IntPtr evt = CreateEvent(IntPtr.Zero, true, false, IntPtr.Zero);
                if (evt == IntPtr.Zero) return null;

                try
                {
                    int battery = QueryValue(handle, evt, CmdGetBattery);
                    int charging = QueryValue(handle, evt, CmdGetCharging);
                    int connection = QueryValue(handle, evt, CmdGetConnection);
                    dev.LastUpdated = DateTime.Now;

                    if (battery >= 0 && battery <= 100)
                    {
                        dev.IsConnected = connection != 0;
                        dev.IsSleeping = connection == 0;
                        dev.BatteryPercent = battery;
                        dev.IsCharging = charging > 0;
                        SaveCache(deviceId, battery, charging);
                    }
                    else
                    {
                        // 耳机休眠 / 关机: 接收器仍在, 回退最后已知电量
                        dev.IsConnected = false;
                        dev.IsSleeping = true;
                        int cachedBattery;
                        int cachedCharge;
                        if (LoadCache(deviceId, out cachedBattery, out cachedCharge))
                        {
                            dev.BatteryPercent = cachedBattery;
                            dev.IsCharging = cachedCharge > 0;
                        }
                        else
                        {
                            dev.BatteryPercent = -1;
                        }
                    }
                }
                finally
                {
                    CloseHandle(evt);
                }
            }
            finally
            {
                CloseHandle(handle);
            }

            return dev;
        }

        private static int QueryValue(IntPtr handle, IntPtr evt, byte cmd)
        {
            byte[] tx = BuildFrame(cmd);
            for (int attempt = 0; attempt < 2; attempt++)
            {
                if (!SendFrame(handle, evt, tx)) continue;
                byte[] rx = ReadReply(handle, evt, cmd);
                if (rx != null)
                {
                    int value = ParseReplyValue(rx, cmd);
                    if (value >= 0) return value;
                }
            }
            return -1;
        }

        private static bool ReadInterfaceIdentity(string devicePath, out HIDD_ATTRIBUTES attr, out HIDP_CAPS caps)
        {
            attr = new HIDD_ATTRIBUTES();
            caps = new HIDP_CAPS();

            IntPtr handle = OpenDevice(devicePath, false);
            if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return false;

            try
            {
                attr.Size = Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));
                if (!HidD_GetAttributes(handle, ref attr)) return false;
                if (attr.VendorID != VidRazer) return true;

                IntPtr preparsed;
                if (!HidD_GetPreparsedData(handle, out preparsed) || preparsed == IntPtr.Zero) return true;

                try
                {
                    HidP_GetCaps(preparsed, out caps);
                }
                finally
                {
                    HidD_FreePreparsedData(preparsed);
                }
                return true;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private static IntPtr OpenDevice(string devicePath, bool forIo)
        {
            if (forIo)
            {
                return CreateFile(devicePath, GENERIC_READ | GENERIC_WRITE,
                    FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            }
            return CreateFile(devicePath, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        }

        private static string ReadProductString(IntPtr handle)
        {
            StringBuilder sb = new StringBuilder(256);
            return HidD_GetProductString(handle, sb, sb.Capacity) ? sb.ToString().Trim() : string.Empty;
        }

        private static string GetDevicePath(IntPtr devInfo, ref SP_DEVICE_INTERFACE_DATA ifData)
        {
            uint required = 0;
            SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out required, IntPtr.Zero);
            if (required == 0) return null;

            IntPtr buffer = Marshal.AllocHGlobal((int)required);
            try
            {
                Marshal.WriteInt32(buffer, IntPtr.Size == 8 ? 8 : 6);
                if (!SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, buffer, required, out required, IntPtr.Zero))
                {
                    return null;
                }
                return Marshal.PtrToStringUni(new IntPtr(buffer.ToInt64() + 4));
            }
            finally
            {
                Marshal.FreeHGlobal(buffer);
            }
        }

        #endregion

        #region 中断端点收发

        private static bool SendFrame(IntPtr handle, IntPtr evt, byte[] tx)
        {
            ResetEvent(evt);
            OVERLAPPED ov = new OVERLAPPED();
            ov.hEvent = evt;
            uint written = 0;
            bool ok = WriteFile(handle, tx, (uint)tx.Length, out written, ref ov);

            if (!ok)
            {
                if (Marshal.GetLastWin32Error() != ERROR_IO_PENDING) return false;
                if (WaitForSingleObject(evt, SendTimeoutMs) != WAIT_OBJECT_0)
                {
                    CancelIo(handle);
                    return false;
                }
                if (!GetOverlappedResult(handle, ref ov, out written, false)) return false;
            }
            return written > 0;
        }

        private static byte[] ReadReply(IntPtr handle, IntPtr evt, byte cmd)
        {
            byte[] buffer = new byte[ReportSize];
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(ReadWindowMs);

            while (DateTime.UtcNow < deadline)
            {
                ResetEvent(evt);
                OVERLAPPED ov = new OVERLAPPED();
                ov.hEvent = evt;
                uint read = 0;
                bool ok = ReadFile(handle, buffer, (uint)buffer.Length, out read, ref ov);

                if (!ok)
                {
                    if (Marshal.GetLastWin32Error() != ERROR_IO_PENDING) return null;
                    if (WaitForSingleObject(evt, ReadChunkMs) != WAIT_OBJECT_0)
                    {
                        CancelIo(handle);
                        continue;
                    }
                    if (!GetOverlappedResult(handle, ref ov, out read, false)) continue;
                }

                if (read >= 16 && buffer[2] == OutMagicP && buffer[3] == InMagicI && buffer[12] == cmd)
                {
                    return (byte[])buffer.Clone();
                }
            }
            return null;
        }

        #endregion

        #region 注册表缓存 (休眠回退)

        private static void SaveCache(string deviceId, int battery, int charging)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(CacheRegistryKey))
                {
                    if (key == null) return;
                    key.SetValue(deviceId, string.Format("{0}|{1}|{2}", battery, charging, DateTime.Now.Ticks.ToString()));
                }
            }
            catch { }  // 该文件被 test_razer_headset_blackshark 单独编译，不能引用 Diagnostics
        }

        private static bool LoadCache(string deviceId, out int battery, out int charging)
        {
            battery = -1;
            charging = 0;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(CacheRegistryKey))
                {
                    if (key == null) return false;
                    string raw = key.GetValue(deviceId) as string;
                    if (string.IsNullOrEmpty(raw)) return false;

                    string[] parts = raw.Split('|');
                    if (parts.Length < 2) return false;

                    int parsedBattery;
                    if (!int.TryParse(parts[0], out parsedBattery)) return false;
                    if (parsedBattery < 0 || parsedBattery > 100) return false;

                    int parsedCharging;
                    int.TryParse(parts[1], out parsedCharging);
                    battery = parsedBattery;
                    charging = parsedCharging;
                    return true;
                }
            }
            catch
            {
                return false;
            }
        }

        #endregion
    }
}
