// ============================================================================
// 模块名称: VgnDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   VGN / VXE / 蜻蜓 / ATK 系列鼠标私有 HID 协议通信实现；
//   通过 CompX VID、FF02/0002 与严格 17/17 字节端点三重隔离鼠标，
//   避免向同 VID 的 MCHOSE G75 PRO (FA09, 20/20) 键盘发送鼠标报文。
//   特别鸣谢社区贡献者：“yaphets”。
// 依赖关系: NativeMethods.cs, MouseBatteryInfo.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、协议解析或签名时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace RazerBatteryTray
{
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
        private static extern bool ResetEvent(IntPtr hEvent);

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

        public static int CachedBatteryPercent = -1;
        public static bool CachedIsCharging = false;
        public static string CachedDeviceName = "";
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
                                CachedBatteryPercent = -1;
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

        public static bool IsVgnProtocolEndpoint(int vid, int pid, int usagePage, int usage, int inputLength, int outputLength)
        {
            if (vid != 0x3554 || pid == 0xFA09) return false;
            return usagePage == 0xFF02 && usage == 0x0002 && inputLength == 17 && outputLength == 17;
        }

        public static bool ParseBatteryResponse(byte[] report, int length, out int batteryPercent, out bool isCharging)
        {
            batteryPercent = -1;
            isCharging = false;
            if (report == null || length < 17 || report.Length < 17) return false;
            if (report[0] != 0x08 || report[1] != 0x04) return false;

            int sum = 0;
            for (int i = 1; i <= 15; i++) sum += report[i];
            byte crc = (byte)(85 - (sum & 0xFF));
            byte expected = (byte)(crc - 8);
            if (report[16] != expected) return false;

            int battery = report[6];
            if (battery < 0 || battery > 100) return false;
            batteryPercent = battery;
            isCharging = report[7] == 1;
            return true;
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
                ResetEvent(hEvent);
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
            ResetEvent(hEvent);
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
            ResetEvent(hEvent);
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
            if (pid == 0xF503) return "VGN Dragonfly F1 Pro";
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

        public static int[] GetSupportedPollingRatesForModel(int pid, string modelName)
        {
            string name = !string.IsNullOrEmpty(modelName) ? modelName.ToLowerInvariant() : "";
            if (pid == 0xFC05 || pid == 0xFB24 || name.Contains(" r1 se") || (name.Contains("dragonfly f1") && !name.Contains("pro") && !name.Contains("moba")))
            {
                // SE / Standard 1K models
                return new int[] { 125, 250, 500, 1000 };
            }
            if (pid == 0xFB3E || name.Contains("f2") || name.Contains("8k") || name.Contains("8000") || name.Contains("atk"))
            {
                // 8K models
                return new int[] { 125, 250, 500, 1000, 2000, 4000, 8000 };
            }
            // 4K models (F1 Pro/Pro Max/MOBA, R1 Pro/Pro Max)
            return new int[] { 125, 250, 500, 1000, 2000, 4000 };
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
                int foundPid = 0;
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
                            if (devicePath != null &&
                                (devicePath.ToLower().Contains("vid_3554") || devicePath.ToLower().Contains("vgn")) &&
                                !devicePath.ToLower().Contains("pid_fa09"))
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
                                        string endpointProduct = "";
                                        StringBuilder sb = new StringBuilder(256);
                                        if (RazerDeviceHelper.HidD_GetProductString(handle, sb, sb.Capacity))
                                        {
                                            endpointProduct = sb.ToString().Trim();
                                        }

                                        RazerDeviceHelper.HIDD_ATTRIBUTES attr = new RazerDeviceHelper.HIDD_ATTRIBUTES();
                                        attr.Size = Marshal.SizeOf(attr);
                                        bool hasAttributes = RazerDeviceHelper.HidD_GetAttributes(handle, ref attr);

                                        IntPtr preparsed;
                                        if (RazerDeviceHelper.HidD_GetPreparsedData(handle, out preparsed))
                                        {
                                            RazerDeviceHelper.HIDP_CAPS caps;
                                            RazerDeviceHelper.HidP_GetCaps(preparsed, out caps);
                                            RazerDeviceHelper.HidD_FreePreparsedData(preparsed);

                                            // 仅允许 CompX 鼠标 17/17 字节端点，严禁命中 G75 的 FA09 20/20 键盘端点。
                                            if (hasAttributes && IsVgnProtocolEndpoint(attr.VendorID, attr.ProductID,
                                                caps.UsagePage, caps.Usage, caps.InputReportByteLength, caps.OutputReportByteLength))
                                            {
                                                foundVgnDongle = true;
                                                foundPid = attr.ProductID;
                                                if (!string.IsNullOrEmpty(endpointProduct)) foundName = endpointProduct;
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
                                                            if (rxBat != null)
                                                            {
                                                                int bVal;
                                                                bool charging;
                                                                if (ParseBatteryResponse(rxBat, rxBat.Length, out bVal, out charging))
                                                                {
                                                                    foundBattery = bVal;
                                                                    foundCharging = charging;
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
                    if (string.IsNullOrEmpty(devName)) devName = !string.IsNullOrEmpty(CachedDeviceName) ? CachedDeviceName : ("VGN 游戏鼠标 (PID: " + (foundPid > 0 ? foundPid.ToString("X4") : "3554") + ")");
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
                        displayBattery = -1;
                    }

                    bool displayCharging = (foundBattery >= 0) ? foundCharging : CachedIsCharging;

                    return new MouseBatteryInfo
                    {
                        DeviceId = "VGN:3554:" + (foundPid > 0 ? foundPid.ToString("X4") : "3554"),
                        Brand = "VGN",
                        DeviceName = devName,
                        Category = DeviceCategory.Mouse,
                        Transport = "2.4G 无线",
                        IsConnected = isMouseOnline,
                        IsSleeping = !isMouseOnline,
                        IsDonglePresent = true,
                        BatteryPercent = displayBattery,
                        ExactBatteryPercent = displayBattery >= 0 ? displayBattery : 0,
                        IsCharging = displayCharging,
                        LastUpdated = (foundBattery >= 0) ? DateTime.Now : (CachedLastUpdated != DateTime.MinValue ? CachedLastUpdated : DateTime.Now),
                        Dpi = 1600,
                        DpiStage = 2,
                        DpiStageCount = 5,
                        DpiStages = new int[] { 400, 800, 1600, 3200, 6400 },
                        PollingRate = 1000,
                        SupportedPollingRates = GetSupportedPollingRatesForModel(foundPid, devName)
                    };
                }

                return null;
            }
        }
    }
}