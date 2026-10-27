// ============================================================================
// 模块名称: HidTransport.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   零依赖 Win32 HID 底层通信总线：SetupAPI 枚举、HidD 属性/能力查询、
//   重叠 I/O 读写与超时取消。供 Flydigi / MCHOSE 等新品牌 Helper 复用。
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

        public static List<HidDeviceDescriptor> Enumerate(int vendorId, int productId)
        {
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

                            byte[] stringBuffer = new byte[512];
                            if (HidD_GetProductString(handle, stringBuffer, (uint)stringBuffer.Length))
                            {
                                descriptor.ProductString = ReadUnicodeString(stringBuffer);
                            }
                            Array.Clear(stringBuffer, 0, stringBuffer.Length);
                            if (HidD_GetManufacturerString(handle, stringBuffer, (uint)stringBuffer.Length))
                            {
                                descriptor.ManufacturerString = ReadUnicodeString(stringBuffer);
                            }
                            Array.Clear(stringBuffer, 0, stringBuffer.Length);
                            if (HidD_GetSerialNumberString(handle, stringBuffer, (uint)stringBuffer.Length))
                            {
                                descriptor.SerialNumber = ReadUnicodeString(stringBuffer);
                            }

                            HIDP_CAPS caps = GetCaps(handle);
                            descriptor.UsagePage = caps.UsagePage;
                            descriptor.Usage = caps.Usage;
                            descriptor.InputReportLength = caps.InputReportByteLength;
                            descriptor.OutputReportLength = caps.OutputReportByteLength;
                            descriptor.FeatureReportLength = caps.FeatureReportByteLength;

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
            try
            {
                uint written = 0;
                bool ok = WriteFile(handle, buffer, (uint)length, out written, ref overlapped);
                if (!ok && Marshal.GetLastWin32Error() == ERROR_IO_PENDING)
                {
                    uint wait = WaitForSingleObject(evt, (uint)timeoutMs);
                    if (wait != 0)
                    {
                        CancelIo(handle);
                        // 必须等 I/O 真正结束（bWait=true）再让 OVERLAPPED 出栈：
                        // 若超时后直接返回，栈上的 OVERLAPPED 会在内核完成时被写入 → 原生访问违例 → 进程静默消失。
                        GetOverlappedResult(handle, ref overlapped, out written, true); // 等真正完成，避免栈上 OVERLAPPED 悬垂
                        return false;
                    }
                    ok = GetOverlappedResult(handle, ref overlapped, out written, false);
                }
                return ok && written > 0;
            }
            catch
            {
                return false;
            }
            finally
            {
                CloseHandle(evt);
            }
        }

        public static bool ReadReport(IntPtr handle, int length, int timeoutMs, out byte[] data)
        {
            data = null;
            if (!IsValidHandle(handle) || length <= 0) return false;
            IntPtr evt = CreateEvent(IntPtr.Zero, true, false, null);
            if (evt == IntPtr.Zero) return false;
            OVERLAPPED overlapped = new OVERLAPPED();
            overlapped.hEvent = evt;
            try
            {
                byte[] buffer = new byte[length];
                uint read = 0;
                bool ok = ReadFile(handle, buffer, (uint)length, out read, ref overlapped);
                if (!ok && Marshal.GetLastWin32Error() == ERROR_IO_PENDING)
                {
                    uint wait = WaitForSingleObject(evt, (uint)timeoutMs);
                    if (wait != 0)
                    {
                        CancelIo(handle);
                        // 必须等 I/O 真正结束（bWait=true）再让 OVERLAPPED 出栈：
                        // 若超时后直接返回，栈上的 OVERLAPPED 会在内核完成时被写入 → 原生访问违例 → 进程静默消失。
                        GetOverlappedResult(handle, ref overlapped, out read, true); // 等真正完成，避免栈上 OVERLAPPED 悬垂
                        return false;
                    }
                    ok = GetOverlappedResult(handle, ref overlapped, out read, false);
                }
                if (ok && read > 0)
                {
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
