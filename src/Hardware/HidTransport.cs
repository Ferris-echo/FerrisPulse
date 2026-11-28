// ============================================================================
// 模块名称: HidTransport.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
// CORE_CAPABILITY: ZERO-DEPENDENCY WIN32 HID TRANSPORT
//   零依赖 Win32 HID 底层通信总线：SetupAPI 枚举、HidD 属性/能力查询、
//   重叠 I/O 读写与超时取消（含 HidD_SetOutputReport 封装）。ReadReportExact 拒绝实际短读；旧 ReadReport 保持兼容。
//   另暴露进程内 HID 枚举计数器（EnumerationCount），仅服务于 ScanTelemetry 的 scan_trace.log 统计。
//   Enumerate 仅在非标准纯光标/键盘端点读取 ProductString，避免向高刷鼠标实时端点发送冗余 EP0 控制请求。
//   供 Flydigi / MCHOSE / VXE 等品牌 Helper 复用。
// 依赖关系: 仅 .NET 4.0 核心程序集 + hid.dll / setupapi.dll / kernel32.dll
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 修改枚举、读写或超时逻辑时必须同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止字符串插值、空条件运算符、nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace RazerBatteryTray
{
    public class HidDeviceDescriptor
    {
        public string DevicePath;
        public string InstanceId;
        public int VendorId;
        public int ProductId;
        public int VersionNumber;
        public int UsagePage;
        public int Usage;
        public int InputReportLength;
        public int OutputReportLength;
        public int FeatureReportLength;
        public string ProductString;
        public string ManufacturerString;
        public string SerialNumber;
    }

    public static class HidTransport
    {
        private static Guid HidGuid = new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");

        private const uint DIGCF_PRESENT = 0x00000002;
        private const uint DIGCF_DEVICEINTERFACE = 0x00000010;
        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        private const int ERROR_IO_PENDING = 997;

        // [扫描遥测 M2] 进程内 HID 全量枚举调用计数器：仅用于 scan_trace.log 统计，
        // 由 ScanTelemetry.Begin() 在每个扫描代次开头清零。任何逻辑判断都不得依赖它。
        private static int enumerationCount;
        internal static int EnumerationCount { get { return Interlocked.CompareExchange(ref enumerationCount, 0, 0); } }
        internal static void ResetEnumerationCount() { Interlocked.Exchange(ref enumerationCount, 0); }

        // ===== [P1 按扫描代次的 HID 枚举快照] =====
        // 一次全量扫描里 26+ 个品牌 helper 会各自"从零问系统要一遍设备列表"，本机 23 个 HID 接口被重复
        // 枚举 40+ 次。这里给同一次扫描提供只读快照：
        //   1. BeginScanGeneration() 由扫描入口调用，记录代次与扫描线程 ID；
        //   2. 只有"扫描线程自己"能命中快照——UI / 硬件写入等其它线程永远走实时枚举，
        //      因此写路径（SetDpi / SetPollingRate 等）绝不可能复用到过期句柄（铁律：写路径实时开路）；
        //   3. EndScanGeneration() 在扫描结束立刻作废快照，快照生命周期严格限定在单次扫描内；
        //   4. 热插拔（WM_DEVICECHANGE）调用 NotifyTopologyChanged() 立刻作废，保证代次内也能看见新设备。
        private static readonly object scanCacheLock = new object();
        private static int scanGeneration;
        private static int scanThreadId;
        private static int cachedGeneration = -1;
        private static List<HidDeviceDescriptor> cachedScanDescriptors;
        private static int enumerationCacheHits;

        internal static int EnumerationCacheHits { get { return Interlocked.CompareExchange(ref enumerationCacheHits, 0, 0); } }

        internal static void ResetEnumerationCacheHits() { Interlocked.Exchange(ref enumerationCacheHits, 0); }

        internal static void BeginScanGeneration()
        {
            lock (scanCacheLock)
            {
                scanGeneration++;
                scanThreadId = Thread.CurrentThread.ManagedThreadId;
                cachedScanDescriptors = null;
                cachedGeneration = -1;
            }
        }

        internal static void EndScanGeneration()
        {
            lock (scanCacheLock)
            {
                scanThreadId = 0;
                cachedScanDescriptors = null;
                cachedGeneration = -1;
            }
        }

        /// <summary>
        /// 设备拓扑可能已变化（WM_DEVICECHANGE / 手动刷新）：立刻作废快照并推进代次。
        /// </summary>
        internal static void NotifyTopologyChanged()
        {
            lock (scanCacheLock)
            {
                scanGeneration++;
                cachedScanDescriptors = null;
                cachedGeneration = -1;
            }
        }

        private static HidDeviceDescriptor CloneDescriptor(HidDeviceDescriptor src)
        {
            if (src == null) return null;
            HidDeviceDescriptor copy = new HidDeviceDescriptor();
            copy.DevicePath = src.DevicePath;
            copy.InstanceId = src.InstanceId;
            copy.VendorId = src.VendorId;
            copy.ProductId = src.ProductId;
            copy.VersionNumber = src.VersionNumber;
            copy.UsagePage = src.UsagePage;
            copy.Usage = src.Usage;
            copy.InputReportLength = src.InputReportLength;
            copy.OutputReportLength = src.OutputReportLength;
            copy.FeatureReportLength = src.FeatureReportLength;
            copy.ProductString = src.ProductString;
            copy.ManufacturerString = src.ManufacturerString;
            copy.SerialNumber = src.SerialNumber;
            return copy;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVINFO_DATA
        {
            public uint cbSize;
            public Guid ClassGuid;
            public uint DevInst;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        public struct SP_DEVICE_INTERFACE_DATA
        {
            public uint cbSize;
            public Guid InterfaceClassGuid;
            public uint Flags;
            public IntPtr Reserved;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct HIDD_ATTRIBUTES
        {
            public uint Size;
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

        [StructLayout(LayoutKind.Sequential)]
        private struct OVERLAPPED
        {
            public IntPtr Internal;
            public IntPtr InternalHigh;
            public uint Offset;
            public uint OffsetHigh;
            public IntPtr hEvent;
        }

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr SetupDiGetClassDevs(ref Guid ClassGuid, IntPtr Enumerator, IntPtr hwndParent, uint Flags);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiEnumDeviceInterfaces(IntPtr DeviceInfoSet, IntPtr DeviceInfoData, ref Guid InterfaceClassGuid, uint MemberIndex, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr DeviceInfoSet, ref SP_DEVICE_INTERFACE_DATA DeviceInterfaceData, IntPtr DeviceInterfaceDetailData, uint DeviceInterfaceDetailDataSize, out uint RequiredSize, ref SP_DEVINFO_DATA DeviceInfoData);

        [DllImport("setupapi.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool SetupDiGetDeviceInstanceId(IntPtr DeviceInfoSet, ref SP_DEVINFO_DATA DeviceInfoData, StringBuilder DeviceInstanceId, uint DeviceInstanceIdSize, out uint RequiredSize);

        [DllImport("setupapi.dll", SetLastError = true)]
        private static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetAttributes(IntPtr HidDeviceObject, ref HIDD_ATTRIBUTES Attributes);

        [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool HidD_GetProductString(IntPtr HidDeviceObject, byte[] Buffer, uint BufferLength);

        [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool HidD_GetManufacturerString(IntPtr HidDeviceObject, byte[] Buffer, uint BufferLength);

        [DllImport("hid.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool HidD_GetSerialNumberString(IntPtr HidDeviceObject, byte[] Buffer, uint BufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetPreparsedData(IntPtr HidDeviceObject, ref IntPtr PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_FreePreparsedData(IntPtr PreparsedData);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern int HidP_GetCaps(IntPtr PreparsedData, ref HIDP_CAPS Capabilities);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_SetFeature(IntPtr HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetFeature(IntPtr HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_SetOutputReport(IntPtr HidDeviceObject, byte[] lpReportBuffer, int ReportBufferLength);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFile(string FileName, uint DesiredAccess, uint ShareMode, IntPtr SecurityAttributes, uint CreationDisposition, uint FlagsAndAttributes, IntPtr TemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetOverlappedResult(IntPtr hFile, ref OVERLAPPED lpOverlapped, out uint lpNumberOfBytesTransferred, bool bWait);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, ref OVERLAPPED lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, ref OVERLAPPED lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CancelIo(IntPtr hFile);

        public static bool IsValidHandle(IntPtr handle)
        {
            return handle != IntPtr.Zero && handle.ToInt64() != -1;
        }

        private static string ReadUnicodeString(byte[] buffer)
        {
            if (buffer == null) return "";
            int length = 0;
            while (length + 1 < buffer.Length && !(buffer[length] == 0 && buffer[length + 1] == 0))
            {
                length += 2;
            }
            return Encoding.Unicode.GetString(buffer, 0, length).Trim();
        }

        /// <summary>
        /// 枚举当前 HID 端点。扫描线程内（BeginScanGeneration 之后）命中本代次快照，
        /// 其它线程与扫描之外一律实时枚举，保证写路径永远拿到活句柄。
        /// </summary>
        public static List<HidDeviceDescriptor> Enumerate(int vendorId, int productId)
        {
            bool inScanThread;
            List<HidDeviceDescriptor> snapshot;
            lock (scanCacheLock)
            {
                inScanThread = scanThreadId != 0 && scanThreadId == Thread.CurrentThread.ManagedThreadId;
                snapshot = (inScanThread && cachedGeneration == scanGeneration) ? cachedScanDescriptors : null;
            }

            if (!inScanThread) return EnumerateFresh(vendorId, productId);

            if (snapshot == null)
            {
                snapshot = EnumerateFresh(0, 0);
                List<HidDeviceDescriptor> cloned = new List<HidDeviceDescriptor>(snapshot.Count);
                for (int i = 0; i < snapshot.Count; i++) cloned.Add(CloneDescriptor(snapshot[i]));
                lock (scanCacheLock)
                {
                    cachedScanDescriptors = cloned;
                    cachedGeneration = scanGeneration;
                }
                snapshot = cloned;
            }
            else
            {
                Interlocked.Increment(ref enumerationCacheHits);
            }

            // 返回防御性副本：helper 可能对自己的结果集合做排序 / 过滤，绝不能污染共享快照
            List<HidDeviceDescriptor> result = new List<HidDeviceDescriptor>();
            for (int i = 0; i < snapshot.Count; i++)
            {
                HidDeviceDescriptor d = snapshot[i];
                if (d == null) continue;
                if (vendorId != 0 && d.VendorId != vendorId) continue;
                if (productId != 0 && d.ProductId != productId) continue;
                result.Add(CloneDescriptor(d));
            }
            return result;
        }

        /// <summary>
        /// 无条件实时枚举（原始实现）。写路径、扫描之外以及需要跳过快照的调用方使用。
        /// </summary>
        public static List<HidDeviceDescriptor> EnumerateFresh(int vendorId, int productId)
        {
            Interlocked.Increment(ref enumerationCount);
            List<HidDeviceDescriptor> result = new List<HidDeviceDescriptor>();
            Guid guid = HidGuid;
            IntPtr deviceInfoSet = SetupDiGetClassDevs(ref guid, IntPtr.Zero, IntPtr.Zero, DIGCF_PRESENT | DIGCF_DEVICEINTERFACE);
            if (!IsValidHandle(deviceInfoSet)) return result;

            SP_DEVICE_INTERFACE_DATA interfaceData = new SP_DEVICE_INTERFACE_DATA();
            interfaceData.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVICE_INTERFACE_DATA));
            try
            {
                uint index = 0;
                while (SetupDiEnumDeviceInterfaces(deviceInfoSet, IntPtr.Zero, ref guid, index, ref interfaceData))
                {
                    index++;
                    SP_DEVINFO_DATA infoData = new SP_DEVINFO_DATA();
                    infoData.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));
                    uint requiredSize = 0;
                    SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, IntPtr.Zero, 0, out requiredSize, ref infoData);
                    if (requiredSize == 0) continue;

                    IntPtr detail = Marshal.AllocHGlobal((int)requiredSize);
                    try
                    {
                        Marshal.WriteInt32(detail, IntPtr.Size == 8 ? 8 : 6);
                        if (!SetupDiGetDeviceInterfaceDetail(deviceInfoSet, ref interfaceData, detail, requiredSize, out requiredSize, ref infoData)) continue;

                        IntPtr pathPointer = new IntPtr(detail.ToInt64() + 4);
                        string path = Marshal.PtrToStringUni(pathPointer);
                        if (string.IsNullOrEmpty(path)) continue;

                        StringBuilder instanceId = new StringBuilder(512);
                        uint instanceSize = 0;
                        SetupDiGetDeviceInstanceId(deviceInfoSet, ref infoData, instanceId, (uint)instanceId.Capacity, out instanceSize);

                        IntPtr handle = CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
                        if (!IsValidHandle(handle)) continue;
                        try
                        {
                            HIDD_ATTRIBUTES attributes = new HIDD_ATTRIBUTES();
                            attributes.Size = (uint)Marshal.SizeOf(typeof(HIDD_ATTRIBUTES));
                            if (!HidD_GetAttributes(handle, ref attributes)) continue;
                            if (vendorId != 0 && attributes.VendorID != vendorId) continue;
                            if (productId != 0 && attributes.ProductID != productId) continue;

                            HidDeviceDescriptor descriptor = new HidDeviceDescriptor();
                            descriptor.DevicePath = path;
                            descriptor.InstanceId = instanceId.ToString();
                            descriptor.VendorId = attributes.VendorID;
                            descriptor.ProductId = attributes.ProductID;
                            descriptor.VersionNumber = attributes.VersionNumber;

                            HIDP_CAPS caps = GetCaps(handle);
                            descriptor.UsagePage = caps.UsagePage;
                            descriptor.Usage = caps.Usage;
                            descriptor.InputReportLength = caps.InputReportByteLength;
                            descriptor.OutputReportLength = caps.OutputReportByteLength;
                            descriptor.FeatureReportLength = caps.FeatureReportByteLength;

                            // 仅在非标准纯光标/键盘输入端点时读取 ProductString，避免向高刷实时运动通道发送多余 EP0 控制请求
                            bool isPureInputCollection = (caps.UsagePage == 0x0001 && (caps.Usage == 0x0002 || caps.Usage == 0x0006) && caps.FeatureReportByteLength == 0);
                            if (!isPureInputCollection)
                            {
                                byte[] stringBuffer = new byte[512];
                                if (HidD_GetProductString(handle, stringBuffer, (uint)stringBuffer.Length))
                                {
                                    descriptor.ProductString = ReadUnicodeString(stringBuffer);
                                }
                            }

                            result.Add(descriptor);
                        }
                        finally
                        {
                            CloseHandle(handle);
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detail);
                    }
                }
            }
            finally
            {
                SetupDiDestroyDeviceInfoList(deviceInfoSet);
            }
            return result;
        }

        public static HIDP_CAPS GetCaps(IntPtr handle)
        {
            HIDP_CAPS caps = new HIDP_CAPS();
            caps.Reserved = new ushort[17];
            if (!IsValidHandle(handle)) return caps;
            IntPtr preparsed = IntPtr.Zero;
            if (HidD_GetPreparsedData(handle, ref preparsed))
            {
                HidP_GetCaps(preparsed, ref caps);
                HidD_FreePreparsedData(preparsed);
            }
            return caps;
        }

        public static IntPtr Open(string path)
        {
            if (string.IsNullOrEmpty(path)) return new IntPtr(-1);
            IntPtr handle = CreateFile(path, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (IsValidHandle(handle)) return handle;
            handle = CreateFile(path, GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
            if (IsValidHandle(handle)) return handle;
            return CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        }

        public static IntPtr OpenRead(string path)
        {
            if (string.IsNullOrEmpty(path)) return new IntPtr(-1);
            return CreateFile(path, GENERIC_READ, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
        }

        public static IntPtr OpenFeature(string path)
        {
            if (string.IsNullOrEmpty(path)) return new IntPtr(-1);
            return CreateFile(path, 0, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
        }

        public static bool ReadFeatureReport(IntPtr handle, byte reportId, int length, out byte[] data)
        {
            data = null;
            if (!IsValidHandle(handle) || length < 2 || length > 4096) return false;
            byte[] report = new byte[length];
            report[0] = reportId;
            if (!HidD_GetFeature(handle, report, length) || report[0] != reportId) return false;
            data = report;
            return true;
        }

        public static void Close(IntPtr handle)
        {
            if (IsValidHandle(handle)) CloseHandle(handle);
        }

        public static bool WriteReport(IntPtr handle, byte[] buffer, int length, int timeoutMs)
        {
            if (!IsValidHandle(handle) || buffer == null || length <= 0 || length > buffer.Length) return false;
            IntPtr evt = CreateEvent(IntPtr.Zero, true, false, null);
            if (evt == IntPtr.Zero) return false;
            OVERLAPPED overlapped = new OVERLAPPED();
            overlapped.hEvent = evt;
            GCHandle pinned = default(GCHandle);
            try
            {
                pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                uint written = 0;
                bool ok = WriteFile(handle, buffer, (uint)length, out written, ref overlapped);
                if (!ok && Marshal.GetLastWin32Error() == ERROR_IO_PENDING)
                {
                    uint wait = WaitForSingleObject(evt, (uint)timeoutMs);
                    if (wait != 0)
                    {
                        CancelIo(handle);
                        GetOverlappedResult(handle, ref overlapped, out written, true);
                        return false;
                    }
                    ok = GetOverlappedResult(handle, ref overlapped, out written, false);
                }
                return ok && written == length;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (pinned.IsAllocated) pinned.Free();
                CloseHandle(evt);
            }
        }

        public static bool ReadReport(IntPtr handle, int length, int timeoutMs, out byte[] data)
        {
            return ReadReportCore(handle, length, timeoutMs, false, out data);
        }

        // 固定长度协议必须校验 Win32 实际字节数，不能把分配缓冲区的补零当作收到的数据。
        public static bool ReadReportExact(IntPtr handle, int length, int timeoutMs, out byte[] data)
        {
            return ReadReportCore(handle, length, timeoutMs, true, out data);
        }

        private static bool ReadReportCore(IntPtr handle, int length, int timeoutMs, bool requireExactLength, out byte[] data)
        {
            data = null;
            if (!IsValidHandle(handle) || length <= 0) return false;
            IntPtr evt = CreateEvent(IntPtr.Zero, true, false, null);
            if (evt == IntPtr.Zero) return false;
            OVERLAPPED overlapped = new OVERLAPPED();
            overlapped.hEvent = evt;
            GCHandle pinned = default(GCHandle);
            try
            {
                byte[] buffer = new byte[length];
                pinned = GCHandle.Alloc(buffer, GCHandleType.Pinned);
                uint read = 0;
                bool ok = ReadFile(handle, buffer, (uint)length, out read, ref overlapped);
                if (!ok && Marshal.GetLastWin32Error() == ERROR_IO_PENDING)
                {
                    uint wait = WaitForSingleObject(evt, (uint)timeoutMs);
                    if (wait != 0)
                    {
                        CancelIo(handle);
                        GetOverlappedResult(handle, ref overlapped, out read, true);
                        return false;
                    }
                    ok = GetOverlappedResult(handle, ref overlapped, out read, false);
                }
                if (ok && read > 0 && (!requireExactLength || read == (uint)length))
                {
                    if (read != buffer.Length) Array.Resize(ref buffer, (int)read);
                    data = buffer;
                    return true;
                }
                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (pinned.IsAllocated) pinned.Free();
                CloseHandle(evt);
            }
        }

        public static bool SetFeature(IntPtr handle, byte[] buffer)
        {
            if (!IsValidHandle(handle) || buffer == null || buffer.Length == 0) return false;
            return HidD_SetFeature(handle, buffer, buffer.Length);
        }

        public static bool GetFeature(IntPtr handle, byte[] buffer)
        {
            if (!IsValidHandle(handle) || buffer == null || buffer.Length == 0) return false;
            return HidD_GetFeature(handle, buffer, buffer.Length);
        }

        public static bool SetOutputReport(IntPtr handle, byte[] buffer)
        {
            if (!IsValidHandle(handle) || buffer == null || buffer.Length == 0) return false;
            return HidD_SetOutputReport(handle, buffer, buffer.Length);
        }

        public static bool DrainReports(IntPtr handle, int length, int maxReports)
        {
            bool any = false;
            for (int i = 0; i < maxReports; i++)
            {
                byte[] data;
                if (!ReadReport(handle, length, 1, out data)) break;
                any = true;
            }
            return any;
        }
    }
}
