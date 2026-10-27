// ============================================================================
// 模块名称: LogitechDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   罗技 (Logitech) HID++ 1.0/2.0 私有协议通信、电量检测、DPI调节 (Feature 0x2201/0x2202 支持XY轴独立) 与回报率设置 (Feature 0x8060/0x8061 最高8000Hz) 实现
//   另含 G PRO X Wireless 耳机 (046D:0ABA) 专属 HID++ 4.2 电量通道 (Feature 0x1F20 ADC_MEASUREMENT)
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
public static class LogitechDeviceHelper
{
    private static readonly object hidLock = new object();
    private const string CacheRegistryKey = @"Software\RazerBatteryTray\LogitechCache";

    [StructLayout(LayoutKind.Sequential)]
    private struct OVERLAPPED
    {
        public IntPtr Internal;
        public IntPtr InternalHigh;
        public uint Offset;
        public uint OffsetHigh;
        public IntPtr hEvent;
    }

    [StructLayout(LayoutKind.Sequential)]
    public struct SP_DEVICE_INTERFACE_DATA
    {
        public int cbSize;
        public Guid interfaceClassGuid;
        public int flags;
        public IntPtr reserved;
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
    public struct HIDD_ATTRIBUTES
    {
        public int Size;
        public ushort VendorID;
        public ushort ProductID;
        public ushort VersionNumber;
    }

    [DllImport("hid.dll", SetLastError = true)]
    public static extern void HidD_GetHidGuid(out Guid hidGuid);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr SetupDiGetClassDevs(ref Guid classGuid, string enumerator, IntPtr hwndParent, uint flags);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiEnumDeviceInterfaces(IntPtr deviceInfoSet, IntPtr deviceInfoData, ref Guid interfaceClassGuid, uint memberIndex, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData);

    [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern bool SetupDiGetDeviceInterfaceDetail(IntPtr deviceInfoSet, ref SP_DEVICE_INTERFACE_DATA deviceInterfaceData, IntPtr deviceInterfaceDetailData, uint deviceInterfaceDetailDataSize, out uint requiredSize, IntPtr deviceInfoData);

    [DllImport("setupapi.dll", SetLastError = true)]
    public static extern bool SetupDiDestroyDeviceInfoList(IntPtr deviceInfoSet);

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr CreateFile(string lpFileName, uint dwDesiredAccess, uint dwShareMode, IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetPreparsedData(IntPtr hidDeviceObject, out IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_FreePreparsedData(IntPtr preparsedData);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern int HidP_GetCaps(IntPtr preparsedData, out HIDP_CAPS capabilities);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetProductString(IntPtr hidDeviceObject, StringBuilder buffer, int bufferLength);

    [DllImport("hid.dll", SetLastError = true)]
    public static extern bool HidD_GetAttributes(IntPtr hidDeviceObject, ref HIDD_ATTRIBUTES attributes);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ReadFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, ref OVERLAPPED lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, ref OVERLAPPED lpOverlapped);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool GetOverlappedResult(IntPtr hFile, ref OVERLAPPED lpOverlapped, out uint lpNumberOfBytesTransferred, bool bWait);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CancelIo(IntPtr hFile);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool ResetEvent(IntPtr hEvent);

    private const uint GENERIC_READ = 0x80000000;
    private const uint GENERIC_WRITE = 0x40000000;
    private const uint FILE_SHARE_READ = 0x00000001;
    private const uint FILE_SHARE_WRITE = 0x00000002;
    private const uint OPEN_EXISTING = 3;
    private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
    private const uint WAIT_OBJECT_0 = 0;

    public class LogitechEndpoint
    {
        public string DeviceId;
        public string DevicePath;
        public byte DevIndex;
        public int InputReportLength;
        public int OutputReportLength;
        public int CurrentDpi;
        public int CurrentDpiY;
        public int PollingRate;
        public byte DpiFeatureIndex;
        public byte ExtDpiFeatureIndex;
        public byte RateFeatureIndex;
        public byte ExtRateFeatureIndex;
        public int[] SupportedRates;
        public DateTime LastSeen;
    }

    private static Dictionary<string, LogitechEndpoint> activeEndpoints = new Dictionary<string, LogitechEndpoint>(StringComparer.OrdinalIgnoreCase);

    public static int CachedBatteryPercent = -1;
    public static bool CachedIsCharging = false;
    public static string CachedDeviceName = "罗技无线游戏鼠标";
    public static int CachedDpi = 1600;
    public static int CachedDpiY = 1600;
    public static int CachedPollingRate = 1000;
    public static DateTime CachedLastUpdated = DateTime.MinValue;
    public static bool HasValidHardwareReading = false;
    private static Dictionary<string, int> deviceBatteryCache = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

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
                    }

                    var dn = key.GetValue("DeviceName") as string;
                    if (!string.IsNullOrEmpty(dn)) CachedDeviceName = dn;

                    var chg = key.GetValue("IsCharging");
                    if (chg != null) CachedIsCharging = (Convert.ToInt32(chg) != 0);

                    var dpiObj = key.GetValue("Dpi");
                    if (dpiObj != null) CachedDpi = Convert.ToInt32(dpiObj);

                    var dpiYObj = key.GetValue("DpiY");
                    if (dpiYObj != null) CachedDpiY = Convert.ToInt32(dpiYObj);

                    var pollObj = key.GetValue("PollingRate");
                    if (pollObj != null) CachedPollingRate = Convert.ToInt32(pollObj);

                    var updatedStr = key.GetValue("LastUpdated") as string;
                    if (!string.IsNullOrEmpty(updatedStr))
                    {
                        DateTime dt;
                        if (DateTime.TryParse(updatedStr, out dt)) CachedLastUpdated = dt;
                    }
                }
            }
        }
        catch (Exception diagEx) { Diagnostics.Log("Hardware/LogitechDeviceHelper.cs: 已忽略异常", diagEx); }
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
                    if (CachedDpi > 0) key.SetValue("Dpi", CachedDpi);
                    if (CachedDpiY > 0) key.SetValue("DpiY", CachedDpiY);
                    if (CachedPollingRate > 0) key.SetValue("PollingRate", CachedPollingRate);
                    if (CachedLastUpdated != DateTime.MinValue) key.SetValue("LastUpdated", CachedLastUpdated.ToString("o"));
                }
            }
        }
        catch (Exception diagEx) { Diagnostics.Log("Hardware/LogitechDeviceHelper.cs: 已忽略异常", diagEx); }
    }

    public static int GetCachedBatteryForDevice(string deviceId)
    {
        if (string.IsNullOrEmpty(deviceId)) return CachedBatteryPercent;
        int val;
        if (deviceBatteryCache.TryGetValue(deviceId, out val) && val > 0)
        {
            return val;
        }
        return (HasValidHardwareReading && CachedBatteryPercent > 0) ? CachedBatteryPercent : -1;
    }

    private static byte[] SendAndReceive(IntPtr handle, IntPtr hEvent, byte[] tx, int inLen, int timeoutMs, int outLen = 0, byte matchDevIdx = 0xFF, byte matchFeatIdx = 0xFF, byte matchSwid = 0xFF)
    {
        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return null;

        byte[] txBuffer = tx;
        if (outLen > 0 && tx != null && tx.Length != outLen)
        {
            txBuffer = new byte[outLen];
            Array.Copy(tx, txBuffer, Math.Min(tx.Length, outLen));
        }

        // Drain pending reports
        byte[] drain = new byte[inLen];
        for (int i = 0; i < 5; i++)
        {
            OVERLAPPED dOl = new OVERLAPPED();
            dOl.hEvent = hEvent;
            uint readBytes;
            ResetEvent(hEvent); // 手动重置事件必须每次 I/O 前清零
            bool ok = ReadFile(handle, drain, (uint)drain.Length, out readBytes, ref dOl);
            if (!ok && Marshal.GetLastWin32Error() == 997)
            {
                if (WaitForSingleObject(hEvent, 10) == WAIT_OBJECT_0)
                {
                    GetOverlappedResult(handle, ref dOl, out readBytes, false);
                    continue;
                }
                CancelIo(handle);
                // 必须等 I/O 真正结束（bWait=true）再让 OVERLAPPED 出栈：
                // 若超时后直接返回，栈上的 OVERLAPPED 会在内核完成时被写入 → 原生访问违例 → 进程静默消失。
                GetOverlappedResult(handle, ref dOl, out readBytes, true);
            }
            break;
        }

        // Write command
        OVERLAPPED wOl = new OVERLAPPED();
        wOl.hEvent = hEvent;
        uint written;
        ResetEvent(hEvent);
        bool wOk = WriteFile(handle, txBuffer, (uint)txBuffer.Length, out written, ref wOl);
        if (!wOk && Marshal.GetLastWin32Error() == 997)
        {
            if (WaitForSingleObject(hEvent, 500) == WAIT_OBJECT_0)
            {
                GetOverlappedResult(handle, ref wOl, out written, false);
            }
            else
            {
                CancelIo(handle);
                // 必须等 I/O 真正结束（bWait=true）再让 OVERLAPPED 出栈：
                // 若超时后直接返回，栈上的 OVERLAPPED 会在内核完成时被写入 → 原生访问违例 → 进程静默消失。
                GetOverlappedResult(handle, ref wOl, out written, true);
                return null;
            }
        }

        // Read response with deadline
        DateTime deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            int remainingMs = (int)(deadline - DateTime.UtcNow).TotalMilliseconds;
            if (remainingMs <= 0) break;

            byte[] rx = new byte[inLen];
            OVERLAPPED rOl = new OVERLAPPED();
            rOl.hEvent = hEvent;
            uint rxRead = 0;
            ResetEvent(hEvent);
            bool rOk = ReadFile(handle, rx, (uint)rx.Length, out rxRead, ref rOl);
            if (!rOk && Marshal.GetLastWin32Error() == 997)
            {
                if (WaitForSingleObject(hEvent, (uint)remainingMs) == WAIT_OBJECT_0)
                {
                    GetOverlappedResult(handle, ref rOl, out rxRead, false);
                }
                else
                {
                    CancelIo(handle);
                    // 必须等 I/O 真正结束（bWait=true）再让 OVERLAPPED 出栈：
                    // 若超时后直接返回，栈上的 OVERLAPPED 会在内核完成时被写入 → 原生访问违例 → 进程静默消失。
                    GetOverlappedResult(handle, ref rOl, out rxRead, true);
                    return null;
                }
            }

            if (rxRead > 0)
            {
                if (matchDevIdx != 0xFF)
                {
                    if (rx.Length > 1 && rx[1] == matchDevIdx)
                    {
                        if (rx.Length > 2 && rx[2] == 0xFF) return null; // Error frame
                        if (matchFeatIdx != 0xFF && rx.Length > 2 && rx[2] != matchFeatIdx) continue;
                        if (matchSwid != 0xFF && rx.Length > 3 && (rx[3] & 0x0F) != (matchSwid & 0x0F)) continue;
                        return rx;
                    }
                    continue;
                }
                return rx;
            }
        }

        return null;
    }

    public static string GetModelName(int pid, string productString)
    {
        // 1. Specific PID database
        switch (pid)
        {
            // --- 罗技 G PRO / GPW 系列全系硬件矩阵 (涵盖 1代 至 5代) ---
            // GPW 5代 (GPW 5 / PRO X3 SUPERSTRIKE / 雪豹 / Ultra - HITS电磁微动, 8K, 48000DPI)
            case 0x40BD:
            case 0x40A9: return "Logitech G PRO X3 SUPERSTRIKE (GPW 5代)";
            case 0xC0A9:
            case 0xC0A8: return "Logitech G PRO X3 SUPERSTRIKE (GPW 5代 有线模式)";
            // GPW 4代 (GPW 4 / PRO X SUPERLIGHT 2C / 小手版 / 朱雀)
            case 0x40B5: return "Logitech G PRO X SUPERLIGHT 2C (GPW 4代)";
            case 0xC0B5: return "Logitech G PRO X SUPERLIGHT 2C (GPW 4代 有线模式)";
            // GPW 3代 (GPX 2 / PRO X SUPERLIGHT 2 / DEX)
            case 0x4093: return "Logitech G PRO X SUPERLIGHT 2";
            case 0xC098: return "Logitech G PRO X SUPERLIGHT 2 (有线模式)";
            case 0x4099: return "Logitech G PRO X SUPERLIGHT 2 DEX";
            case 0xC099: return "Logitech G PRO X SUPERLIGHT 2 DEX (有线模式)";
            case 0x40A4: return "Logitech G PRO 2 LIGHTSPEED";
            case 0xC0A4: return "Logitech G PRO 2 LIGHTSPEED (有线模式)";
            // GPW 2代 (GPX / PRO X SUPERLIGHT)
            case 0x4092:
            case 0xC094: return "Logitech G PRO X SUPERLIGHT";
            // GPW 1代 (经典 GPW / LOL 联名)
            case 0x4079:
            case 0xC088: return "Logitech G PRO Wireless (GPW)";
            case 0x408D: return "Logitech G PRO Wireless (英雄联盟联名款)";
            case 0x407A:
            case 0xC08C: return "Logitech G PRO HERO 游戏鼠标";
            case 0xC076: return "Logitech G PRO 游戏鼠标 (经典3366版)";
            case 0x4087:
            case 0xC08D: return "Logitech G502 LIGHTSPEED";
            case 0x408E:
            case 0xC096: return "Logitech G502 X PLUS / LIGHTSPEED";
            case 0xC095: return "Logitech G502 X (有线版)";
            case 0xC08B: return "Logitech G502 HERO (有线版)";
            case 0xC07D: return "Logitech G502 Proteus Spectrum";
            case 0xC084: return "Logitech G203 / G102 Prodigy";
            case 0xC092: return "Logitech G203 / G102 LIGHTSYNC";
            case 0x4074: return "Logitech G304 / G305 LIGHTSPEED";
            case 0x409E: return "Logitech G309 LIGHTSPEED";
            case 0x4070: return "Logitech G703 LIGHTSPEED (HERO)";
            case 0x4071: return "Logitech G903 LIGHTSPEED (HERO)";
            case 0x406E: return "Logitech G604 LIGHTSPEED";
            case 0x4068: return "Logitech G603 LIGHTSPEED";
            case 0x4069:
            case 0xB023: return "Logitech MX Master 3";
            case 0x4090: return "Logitech MX Master 3S";
            case 0x408F: return "Logitech MX Anywhere 3";
            case 0x406B: return "Logitech MX Keys 无线键盘";
            case 0x408A: return "Logitech MX Keys Mini";
            case 0x4082: return "Logitech G913 TKL 机械键盘";
            case 0x4086: return "Logitech G913 机械键盘";
            case 0x40A1: return "Logitech G915 X LIGHTSPEED 机械键盘";
            case 0x4098: return "Logitech G515 LIGHTSPEED TKL 机械键盘";
            case 0x409B: return "Logitech G PRO X 60 游戏键盘";
            case 0x4088: return "Logitech G733 LIGHTSPEED 耳机";
            case 0x4097: return "Logitech G PRO X 2 LIGHTSPEED 耳机";
            case 0x408C:
            case 0x0ABA: return "Logitech G PRO X Wireless 游戏耳机";
            case 0x4085: return "Logitech G435 无线耳机";
            case 0x4080: return "Logitech G533 无线耳机";
            case 0x407B: return "Logitech G935 无线耳机";
        }

        if (!string.IsNullOrEmpty(productString))
        {
            string p = productString.Trim();
            if (p.Length > 2 && 
                !p.Equals("USB Receiver", StringComparison.OrdinalIgnoreCase) &&
                !p.Equals("Lightspeed Receiver", StringComparison.OrdinalIgnoreCase) &&
                !p.Equals("Logitech Receiver", StringComparison.OrdinalIgnoreCase))
            {
                return p.StartsWith("Logitech", StringComparison.OrdinalIgnoreCase) ? p : ("Logitech " + p);
            }
        }

        // Receiver defaults
        if (pid == 0xC54F) return "Logitech G PRO X3 SUPERSTRIKE 8K 接收器";
        if (pid == 0xC547) return "Logitech Lightspeed 游戏无线接收器 (0xC547)";
        if (pid == 0xC548) return "Logitech G PRO X SUPERLIGHT 2 4K/8K 接收器";
        if (pid == 0xC539) return "Logitech Lightspeed 游戏无线接收器";
        if (pid == 0xC53A) return "Logitech POWERPLAY 无线充电系统";
        if (pid == 0xC545) return "Logitech G502 X LIGHTSPEED 接收器";
        if (pid == 0xC52B) return "Logitech Unifying 优联接收器";
        if (pid == 0xC542) return "Logitech Bolt 安全商业接收器";

        return "Logitech 罗技无线游戏外设";
    }

    public static DeviceCategory DetectCategory(int pid, string name)
    {
        string text = (name + " " + pid.ToString("X4")).ToUpperInvariant();
        if (text.Contains("KEYBOARD") || text.Contains("键盘") || text.Contains("G913") || text.Contains("G915") || text.Contains("MX KEYS") || text.Contains("PRO X 60"))
        {
            return DeviceCategory.Keyboard;
        }
        if (text.Contains("HEADSET") || text.Contains("HEADPHONE") || text.Contains("耳机") || text.Contains("G733") || text.Contains("G533") || text.Contains("G935") || text.Contains("G435") || text.Contains("PRO X 2"))
        {
            return DeviceCategory.Headset;
        }
        return DeviceCategory.Mouse;
    }

    private class LogitechRawEndpoint
    {
        public string DevicePath;
        public int VendorId;
        public int ProductId;
        public string ProductString;
        public int InputReportByteLength;
        public int OutputReportByteLength;
        public ushort UsagePage;
        public ushort Usage;
    }

    // ========================================================================
    // Logitech G PRO X Wireless 耳机（046D:0ABA）专属 HID++ 4.2 电量通道
    // 该型号没有标准 0x1000/0x1004 电量特性，电量来自 0x1F20 ADC_MEASUREMENT。
    // 帧格式（Report 0x11 / 0xFF43，20 字节）：
    //   Ping:  11 FF 00 1B 00 00 <mark>
    //   Feat:  11 FF 00 0B 1F 20
    //   ADC:   11 FF <featIdx> 0B
    // 回复 ADC: 11 FF <featIdx> 0B <mvHi> <mvLo> <flags>
    // flags bit0=有效，bit1=正在充电。
    // ========================================================================

    public static int ProXVoltageToPercent(int millivolts)
    {
        int[] volts = new int[] { 4200, 4100, 4000, 3900, 3800, 3777, 3750, 3700, 3650, 3600, 3550, 3500 };
        int[] pcts = new int[] { 100, 90, 80, 70, 60, 57, 50, 40, 30, 20, 10, 0 };

        if (millivolts >= volts[0]) return 100;
        if (millivolts <= volts[volts.Length - 1]) return 0;

        for (int i = 0; i < volts.Length - 1; i++)
        {
            if (millivolts <= volts[i] && millivolts >= volts[i + 1])
            {
                int vHigh = volts[i];
                int vLow = volts[i + 1];
                int pHigh = pcts[i];
                int pLow = pcts[i + 1];
                double ratio = (double)(millivolts - vLow) / (double)(vHigh - vLow);
                return (int)Math.Round(pLow + (pHigh - pLow) * ratio);
            }
        }
        return 0;
    }

    public static bool ParseProXAdcPayload(byte[] payload, out int millivolts, out int flags)
    {
        millivolts = 0;
        flags = 0;
        if (payload == null || payload.Length < 3) return false;
        millivolts = (payload[0] << 8) | payload[1];
        flags = payload[2];
        return millivolts >= 2500 && millivolts <= 4300;
    }

    private static MouseBatteryInfo BuildProXHeadsetInfo(int battery, bool charging, bool connected, DateTime lastUpdated)
    {
        string deviceId = "Logitech:046D:0ABA";
        MouseBatteryInfo info = new MouseBatteryInfo();
        info.DeviceId = deviceId;
        info.HardwareFingerprint = deviceId;
        info.HardwareId = deviceId;
        info.Brand = "Logitech";
        info.Category = DeviceCategory.Headset;
        info.DeviceName = "Logitech G PRO X Wireless 游戏耳机";
        info.Transport = "Lightspeed 无线";
        info.IsCustom = false;
        info.IsConnected = connected;
        info.IsSleeping = !connected;
        info.IsDonglePresent = true;
        info.BatteryPercent = battery;
        info.ExactBatteryPercent = battery;
        info.IsCharging = charging;
        info.LastUpdated = lastUpdated;
        info.Dpi = 0;
        info.DpiY = 0;
        info.PollingRate = 0;
        info.MaxPollingRate = 0;
        info.SupportedPollingRates = null;
        info.DpiStages = null;
        info.DpiStagesY = null;
        return info;
    }

    private static MouseBatteryInfo QueryProXHeadsetViaHidpp(IntPtr handle, IntPtr hEvent, HIDP_CAPS caps, string product)
    {
        byte sw = 0x0B;
        byte mark = 0x5A;
        int inLen = caps.InputReportByteLength;
        if (inLen < 7) return null;

        byte[] ping = new byte[inLen];
        ping[0] = 0x11;
        ping[1] = 0xFF;
        ping[2] = 0x00;
        ping[3] = (byte)(0x10 | sw);
        ping[6] = mark;
        byte[] rx = SendAndReceive(handle, hEvent, ping, inLen, 400);

        string deviceId = "Logitech:046D:0ABA";
        if (rx == null || rx.Length < 7 || rx[6] != mark)
        {
            int cached = GetCachedBatteryForDevice(deviceId);
            if (cached >= 0)
            {
                return BuildProXHeadsetInfo(cached, CachedIsCharging, false, CachedLastUpdated != DateTime.MinValue ? CachedLastUpdated : DateTime.Now);
            }
            return BuildProXHeadsetInfo(-1, false, false, DateTime.Now);
        }

        byte[] feat = new byte[inLen];
        feat[0] = 0x11;
        feat[1] = 0xFF;
        feat[2] = 0x00;
        feat[3] = sw;
        feat[4] = 0x1F;
        feat[5] = 0x20;
        byte[] rf = SendAndReceive(handle, hEvent, feat, inLen, 300);
        if (rf == null || rf.Length < 5 || rf[4] == 0)
        {
            int cached = GetCachedBatteryForDevice(deviceId);
            return BuildProXHeadsetInfo(cached, CachedIsCharging, true, DateTime.Now);
        }

        byte featIdx = rf[4];
        byte[] adc = new byte[inLen];
        adc[0] = 0x11;
        adc[1] = 0xFF;
        adc[2] = featIdx;
        adc[3] = sw;
        byte[] ra = SendAndReceive(handle, hEvent, adc, inLen, 350);
        if (ra == null || ra.Length < 7)
        {
            int cached = GetCachedBatteryForDevice(deviceId);
            return BuildProXHeadsetInfo(cached, CachedIsCharging, true, DateTime.Now);
        }

        int millivolts;
        int flags;
        bool valid = ParseProXAdcPayload(new byte[] { ra[4], ra[5], ra[6] }, out millivolts, out flags);
        if (!valid)
        {
            int cached = GetCachedBatteryForDevice(deviceId);
            return BuildProXHeadsetInfo(cached, CachedIsCharging, true, DateTime.Now);
        }

        bool adcValid = (flags & 0x01) != 0;
        bool charging = adcValid && ((flags & 0x02) != 0);
        int percent = ProXVoltageToPercent(millivolts);
        if (!adcValid)
        {
            int cached = GetCachedBatteryForDevice(deviceId);
            return BuildProXHeadsetInfo(cached, CachedIsCharging, true, DateTime.Now);
        }

        CachedBatteryPercent = percent;
        CachedIsCharging = charging;
        CachedLastUpdated = DateTime.Now;
        CachedDeviceName = "Logitech G PRO X Wireless 游戏耳机";
        HasValidHardwareReading = true;
        deviceBatteryCache[deviceId] = percent;
        SaveHardwareCache();
        return BuildProXHeadsetInfo(percent, charging, true, DateTime.Now);
    }

    public static MouseBatteryInfo QueryLogitechProXHeadset()
    {
        Guid hidGuid;
        HidD_GetHidGuid(out hidGuid);
        IntPtr devInfo = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
        if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return null;

        SP_DEVICE_INTERFACE_DATA ifData = new SP_DEVICE_INTERFACE_DATA();
        ifData.cbSize = Marshal.SizeOf(ifData);
        uint memberIdx = 0;

        try
        {
            while (SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
            {
                uint reqSize;
                SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                if (reqSize == 0) continue;

                IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                try
                {
                    Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);
                    if (!SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero)) continue;

                    string path = Marshal.PtrToStringUni(new IntPtr(detailBuffer.ToInt64() + 4));
                    if (string.IsNullOrEmpty(path) || path.IndexOf("vid_046d", StringComparison.OrdinalIgnoreCase) < 0) continue;

                    IntPtr handle = CreateFile(
                        path,
                        GENERIC_READ | GENERIC_WRITE,
                        FILE_SHARE_READ | FILE_SHARE_WRITE,
                        IntPtr.Zero,
                        OPEN_EXISTING,
                        FILE_FLAG_OVERLAPPED,
                        IntPtr.Zero);
                    if (handle == IntPtr.Zero || handle == new IntPtr(-1)) continue;

                    try
                    {
                        HIDD_ATTRIBUTES attr = new HIDD_ATTRIBUTES();
                        attr.Size = Marshal.SizeOf(attr);
                        if (!HidD_GetAttributes(handle, ref attr)) continue;
                        if (attr.VendorID != 0x046D || attr.ProductID != 0x0ABA) continue;

                        IntPtr preparsed;
                        if (!HidD_GetPreparsedData(handle, out preparsed)) continue;
                        HIDP_CAPS caps;
                        int capsStatus = HidP_GetCaps(preparsed, out caps);
                        HidD_FreePreparsedData(preparsed);
                        if (capsStatus != 0x00110000) continue;
                        if (caps.UsagePage != 0xFF43 || caps.InputReportByteLength < 7) continue;

                        StringBuilder sb = new StringBuilder(256);
                        string product = HidD_GetProductString(handle, sb, sb.Capacity) ? sb.ToString().Trim() : "";

                        IntPtr hEvent = CreateEvent(IntPtr.Zero, true, false, null);
                        if (hEvent == IntPtr.Zero) continue;
                        try
                        {
                            return QueryProXHeadsetViaHidpp(handle, hEvent, caps, product);
                        }
                        finally
                        {
                            CloseHandle(hEvent);
                        }
                    }
                    finally
                    {
                        CloseHandle(handle);
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
            SetupDiDestroyDeviceInfoList(devInfo);
        }
        return null;
    }

    public static List<MouseBatteryInfo> QueryAllLogitechDevices()
    {
        lock (hidLock)
        {
            List<MouseBatteryInfo> results = new List<MouseBatteryInfo>();
            List<LogitechRawEndpoint> rawEndpoints = new List<LogitechRawEndpoint>();

            try
            {
                MouseBatteryInfo headset = QueryLogitechProXHeadset();
                if (headset != null) results.Add(headset);
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/LogitechDeviceHelper.cs: 已忽略异常", diagEx); }


            Guid hidGuid;
            HidD_GetHidGuid(out hidGuid);
            IntPtr devInfo = SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
            if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return results;

            SP_DEVICE_INTERFACE_DATA ifData = new SP_DEVICE_INTERFACE_DATA();
            uint memberIdx = 0;

            try
            {
                ifData.cbSize = Marshal.SizeOf(ifData);
                while (SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                {
                    uint reqSize;
                    SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                    if (reqSize == 0) continue;

                    IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                    Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                    if (SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                    {
                        IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                        string devicePath = Marshal.PtrToStringAuto(pDevicePath);
                        if (devicePath != null && devicePath.ToLower().Contains("vid_046d"))
                        {
                            IntPtr handle = CreateFile(
                                devicePath,
                                0,
                                FILE_SHARE_READ | FILE_SHARE_WRITE,
                                IntPtr.Zero,
                                OPEN_EXISTING,
                                0,
                                IntPtr.Zero);

                            if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                            {
                                try
                                {
                                    int foundVid = 0;
                                    int foundPid = 0;
                                    string foundName = null;

                                    HIDD_ATTRIBUTES attr = new HIDD_ATTRIBUTES();
                                    attr.Size = Marshal.SizeOf(attr);
                                    if (HidD_GetAttributes(handle, ref attr))
                                    {
                                        foundVid = attr.VendorID;
                                        foundPid = attr.ProductID;
                                    }

                                    StringBuilder sb = new StringBuilder(256);
                                    if (HidD_GetProductString(handle, sb, sb.Capacity))
                                    {
                                        string pStr = sb.ToString().Trim();
                                        if (!string.IsNullOrEmpty(pStr) && pStr.Length > 2) foundName = pStr;
                                    }

                                    IntPtr preparsed;
                                    if (HidD_GetPreparsedData(handle, out preparsed))
                                    {
                                        HIDP_CAPS caps;
                                        HidP_GetCaps(preparsed, out caps);
                                        HidD_FreePreparsedData(preparsed);

                                        bool isHidppEndpoint = (caps.UsagePage == 0xFF00 || caps.UsagePage == 0xFF43) && 
                                                               (caps.InputReportByteLength >= 7 && caps.OutputReportByteLength >= 7);

                                        if (isHidppEndpoint)
                                        {
                                            LogitechRawEndpoint rep = new LogitechRawEndpoint();
                                            rep.DevicePath = devicePath;
                                            rep.VendorId = foundVid;
                                            rep.ProductId = foundPid;
                                            rep.ProductString = foundName;
                                            rep.InputReportByteLength = caps.InputReportByteLength;
                                            rep.OutputReportByteLength = caps.OutputReportByteLength;
                                            rep.UsagePage = caps.UsagePage;
                                            rep.Usage = caps.Usage;
                                            rawEndpoints.Add(rep);
                                        }
                                    }
                                }
                                finally
                                {
                                    CloseHandle(handle);
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
                SetupDiDestroyDeviceInfoList(devInfo);
            }

            // Phase 2: Group by (VendorId, ProductId) and select the best endpoint (20-byte HID++ 2.0 preferred)
            Dictionary<string, List<LogitechRawEndpoint>> groups = new Dictionary<string, List<LogitechRawEndpoint>>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < rawEndpoints.Count; i++)
            {
                string key = rawEndpoints[i].VendorId.ToString("X4") + ":" + rawEndpoints[i].ProductId.ToString("X4");
                if (!groups.ContainsKey(key)) groups[key] = new List<LogitechRawEndpoint>();
                groups[key].Add(rawEndpoints[i]);
            }

            foreach (var kvp in groups)
            {
                var group = kvp.Value;
                LogitechRawEndpoint bestEndpoint = null;
                for (int i = 0; i < group.Count; i++)
                {
                    if (group[i].OutputReportByteLength >= 20 && group[i].InputReportByteLength >= 20)
                    {
                        bestEndpoint = group[i];
                        break;
                    }
                }
                if (bestEndpoint == null) bestEndpoint = group[0];

                int foundVid = bestEndpoint.VendorId;
                int foundPid = bestEndpoint.ProductId;
                if (foundPid == 0x0ABA) continue; // G PRO X 耳机已由专属通道 QueryLogitechProXHeadset() 处理
                string foundName = bestEndpoint.ProductString;

                IntPtr handle = CreateFile(
                    bestEndpoint.DevicePath,
                    GENERIC_READ | GENERIC_WRITE,
                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    OPEN_EXISTING,
                    FILE_FLAG_OVERLAPPED,
                    IntPtr.Zero);

                if (handle == IntPtr.Zero || handle.ToInt64() == -1) continue;

                try
                {
                    IntPtr hEvent = CreateEvent(IntPtr.Zero, true, false, null);
                    if (hEvent != IntPtr.Zero)
                    {
                        try
                        {
                            bool isDongle = (foundPid == 0xC547 || foundPid == 0xC539 || foundPid == 0xC53A || 
                                             foundPid == 0xC545 || foundPid == 0xC548 || foundPid == 0xC54F || 
                                             foundPid == 0xC52B || foundPid == 0xC534 || foundPid == 0xC52F || 
                                             foundPid == 0xC542 || (foundPid >= 0xC500 && foundPid <= 0xC5FF));

                            byte[] targetIndices = isDongle 
                                ? ((foundPid == 0xC52B || foundPid == 0xC542) ? new byte[] { 0x01, 0x02, 0x03, 0x04, 0x05, 0x06 } : new byte[] { 0x01, 0x02 }) 
                                : new byte[] { 0xFF };

                            foreach (byte devIdx in targetIndices)
                            {
                                bool deviceOnline = false;
                                string detectedName = null;
                                int batteryPercent = -1;
                                bool isCharging = false;
                                int detectedPid = foundPid;

                                // 1. HID++ 2.0 Ping: Feature 0x0000 ROOT, Func 1 (getProtocolVersion) | SoftwareId 8 = 0x18, ping token 0x5A
                                byte[] txPing = new byte[20];
                                txPing[0] = 0x11;
                                txPing[1] = devIdx;
                                txPing[2] = 0x00; // Feature 0x0000
                                txPing[3] = 0x18; // Func 1 (getProtocolVersion) | swid 8
                                txPing[4] = 0x00;
                                txPing[5] = 0x00;
                                txPing[6] = 0x5A; // Ping token

                                byte[] rxPing = SendAndReceive(handle, hEvent, txPing, bestEndpoint.InputReportByteLength, 350, bestEndpoint.OutputReportByteLength, devIdx, 0x00, 0x08);
                                if (rxPing != null && rxPing.Length >= 7 && rxPing[6] == 0x5A)
                                {
                                    deviceOnline = true;
                                }
                                else
                                {
                                    // Try Short Report ping (Report ID 0x10)
                                    byte[] txShortPing = new byte[7] { 0x10, devIdx, 0x00, 0x18, 0x00, 0x00, 0x5A };
                                    byte[] rxShortPing = SendAndReceive(handle, hEvent, txShortPing, bestEndpoint.InputReportByteLength, 250, bestEndpoint.OutputReportByteLength, devIdx, 0x00, 0x08);
                                    if (rxShortPing != null && rxShortPing.Length >= 7 && rxShortPing[6] == 0x5A)
                                    {
                                        deviceOnline = true;
                                    }
                                    else
                                    {
                                        // Legacy fallback ping with func 0
                                        byte[] txLegacyPing = new byte[20];
                                        txLegacyPing[0] = 0x11;
                                        txLegacyPing[1] = devIdx;
                                        txLegacyPing[2] = 0x00;
                                        txLegacyPing[3] = 0x00;
                                        txLegacyPing[4] = 0x00;
                                        txLegacyPing[5] = 0x00;
                                        txLegacyPing[6] = 0x5A;
                                        byte[] rxLegacyPing = SendAndReceive(handle, hEvent, txLegacyPing, bestEndpoint.InputReportByteLength, 200, bestEndpoint.OutputReportByteLength);
                                        if (rxLegacyPing != null && rxLegacyPing.Length >= 7 && rxLegacyPing[6] == 0x5A)
                                        {
                                            deviceOnline = true;
                                        }
                                    }
                                }

                                // 2. Discover Features if online
                                int hardwareDpi = -1;
                                int hardwareDpiY = -1;
                                byte extDpiFeatIdx = 0;
                                byte dpiFeatIdx = 0;
                                byte extRateFeatIdx = 0;
                                byte rateFeatIdx = 0;
                                int detectedRate = 1000;
                                int[] supportedRates = new int[] { 1000, 2000, 4000, 8000 };
                                if (deviceOnline)
                                {
                                    // Feature 0x0005: Device Name Type
                                    byte[] txFeatName = new byte[20];
                                    txFeatName[0] = 0x11;
                                    txFeatName[1] = devIdx;
                                    txFeatName[2] = 0x00;
                                    txFeatName[3] = 0x08; // getFeature, swid 8
                                    txFeatName[4] = 0x00;
                                    txFeatName[5] = 0x05; // Feature ID: 0x0005
                                    byte[] rxFeatName = SendAndReceive(handle, hEvent, txFeatName, bestEndpoint.InputReportByteLength, 250, bestEndpoint.OutputReportByteLength, devIdx, 0x00, 0x08);
                                    if (rxFeatName != null && rxFeatName.Length >= 5 && rxFeatName[4] > 0)
                                    {
                                        byte nameFeatIdx = rxFeatName[4];
                                        byte[] txNameCount = new byte[20];
                                        txNameCount[0] = 0x11;
                                        txNameCount[1] = devIdx;
                                        txNameCount[2] = nameFeatIdx;
                                        txNameCount[3] = 0x08; // Func 0: getCount, swid 8
                                        byte[] rxNameCount = SendAndReceive(handle, hEvent, txNameCount, bestEndpoint.InputReportByteLength, 200, bestEndpoint.OutputReportByteLength, devIdx, nameFeatIdx, 0x08);
                                        if (rxNameCount != null && rxNameCount.Length >= 5 && rxNameCount[4] > 0)
                                        {
                                            int nameLen = rxNameCount[4];
                                            byte[] txGetName = new byte[20];
                                            txGetName[0] = 0x11;
                                            txGetName[1] = devIdx;
                                            txGetName[2] = nameFeatIdx;
                                            txGetName[3] = 0x18; // Func 1: getName, swid 8
                                            txGetName[4] = 0x00;
                                            byte[] rxGetName = SendAndReceive(handle, hEvent, txGetName, bestEndpoint.InputReportByteLength, 250, bestEndpoint.OutputReportByteLength, devIdx, nameFeatIdx, 0x08);
                                            if (rxGetName != null && rxGetName.Length > 4)
                                            {
                                                StringBuilder nsb = new StringBuilder();
                                                for (int c = 4; c < rxGetName.Length && (c - 4) < nameLen; c++)
                                                {
                                                    if (rxGetName[c] == 0) break;
                                                    if (rxGetName[c] >= 32 && rxGetName[c] <= 126)
                                                    {
                                                        nsb.Append((char)rxGetName[c]);
                                                    }
                                                }
                                                string str = nsb.ToString().Trim();
                                                if (str.Length > 2) detectedName = str;
                                            }
                                        }
                                    }

                                    // Feature 0x1004: Unified Battery
                                    byte[] txFeat1004 = new byte[20];
                                    txFeat1004[0] = 0x11;
                                    txFeat1004[1] = devIdx;
                                    txFeat1004[2] = 0x00;
                                    txFeat1004[3] = 0x08; // getFeature, swid 8
                                    txFeat1004[4] = 0x10;
                                    txFeat1004[5] = 0x04; // 0x1004
                                    byte[] rxFeat1004 = SendAndReceive(handle, hEvent, txFeat1004, bestEndpoint.InputReportByteLength, 250, bestEndpoint.OutputReportByteLength, devIdx, 0x00, 0x08);
                                    if (rxFeat1004 != null && rxFeat1004.Length >= 5 && rxFeat1004[4] > 0)
                                    {
                                        byte batFeatIdx = rxFeat1004[4];
                                        byte[] txBat = new byte[20];
                                        txBat[0] = 0x11;
                                        txBat[1] = devIdx;
                                        txBat[2] = batFeatIdx;
                                        txBat[3] = 0x18; // Func 1 (0x10) | swid 8
                                        byte[] rxBat = SendAndReceive(handle, hEvent, txBat, bestEndpoint.InputReportByteLength, 300, bestEndpoint.OutputReportByteLength, devIdx, batFeatIdx, 0x08);
                                        if (rxBat != null && rxBat.Length > 4)
                                        {
                                            int pct = rxBat[4];
                                            if (pct >= 0 && pct <= 100)
                                            {
                                                batteryPercent = pct;
                                                if (rxBat.Length > 6)
                                                {
                                                    isCharging = (rxBat[6] >= 1 && rxBat[6] <= 4) || (rxBat[5] == 1);
                                                }
                                                else
                                                {
                                                    isCharging = (rxBat[5] == 1);
                                                }
                                            }
                                        }
                                    }

                                    // Feature 0x1000: Standard Battery Status fallback
                                    if (batteryPercent < 0)
                                    {
                                        byte[] txFeat1000 = new byte[20];
                                        txFeat1000[0] = 0x11;
                                        txFeat1000[1] = devIdx;
                                        txFeat1000[2] = 0x00;
                                        txFeat1000[3] = 0x08;
                                        txFeat1000[4] = 0x10;
                                        txFeat1000[5] = 0x00; // 0x1000
                                        byte[] rxFeat1000 = SendAndReceive(handle, hEvent, txFeat1000, bestEndpoint.InputReportByteLength, 250, bestEndpoint.OutputReportByteLength, devIdx, 0x00, 0x08);
                                        if (rxFeat1000 != null && rxFeat1000.Length >= 5 && rxFeat1000[4] > 0)
                                        {
                                            byte batFeatIdx = rxFeat1000[4];
                                            byte[] txBat = new byte[20];
                                            txBat[0] = 0x11;
                                            txBat[1] = devIdx;
                                            txBat[2] = batFeatIdx;
                                            txBat[3] = 0x08; // Func 0, swid 8
                                            byte[] rxBat = SendAndReceive(handle, hEvent, txBat, bestEndpoint.InputReportByteLength, 300, bestEndpoint.OutputReportByteLength, devIdx, batFeatIdx, 0x08);
                                            if (rxBat != null && rxBat.Length >= 5)
                                            {
                                                int pct = rxBat[4];
                                                if (pct >= 0 && pct <= 100)
                                                {
                                                    batteryPercent = pct;
                                                    if (rxBat.Length > 6) isCharging = (rxBat[6] == 1);
                                                }
                                            }
                                        }
                                    }

                                    // Feature 0x1001: Battery Voltage fallback (GPW older firmwares)
                                    if (batteryPercent < 0)
                                    {
                                        byte[] txFeat1001 = new byte[20];
                                        txFeat1001[0] = 0x11;
                                        txFeat1001[1] = devIdx;
                                        txFeat1001[2] = 0x00;
                                        txFeat1001[3] = 0x08;
                                        txFeat1001[4] = 0x10;
                                        txFeat1001[5] = 0x01; // 0x1001
                                        byte[] rxFeat1001 = SendAndReceive(handle, hEvent, txFeat1001, bestEndpoint.InputReportByteLength, 250, bestEndpoint.OutputReportByteLength, devIdx, 0x00, 0x08);
                                        if (rxFeat1001 != null && rxFeat1001.Length >= 5 && rxFeat1001[4] > 0)
                                        {
                                            byte batFeatIdx = rxFeat1001[4];
                                            byte[] txBat = new byte[20];
                                            txBat[0] = 0x11;
                                            txBat[1] = devIdx;
                                            txBat[2] = batFeatIdx;
                                            txBat[3] = 0x08;
                                            byte[] rxBat = SendAndReceive(handle, hEvent, txBat, bestEndpoint.InputReportByteLength, 300, bestEndpoint.OutputReportByteLength, devIdx, batFeatIdx, 0x08);
                                            if (rxBat != null && rxBat.Length >= 6)
                                            {
                                                int vMv = (rxBat[4] << 8) | rxBat[5];
                                                if (rxBat.Length > 6) isCharging = (rxBat[6] == 1 || rxBat[6] == 2);
                                                if (vMv > 3000 && vMv < 4500)
                                                {
                                                    int pct = (int)((vMv - 3600) * 100 / (4150 - 3600));
                                                    batteryPercent = Math.Max(1, Math.Min(100, pct));
                                                }
                                            }
                                        }
                                    }

                                    // HID++ 1.0 Sub-ID 0x0D Fallback
                                    if (batteryPercent < 0)
                                    {
                                        byte[] txHidpp1 = new byte[7] { 0x10, devIdx, 0x0D, 0x00, 0x00, 0x00, 0x00 };
                                        byte[] rxHidpp1 = SendAndReceive(handle, hEvent, txHidpp1, bestEndpoint.InputReportByteLength, 250, bestEndpoint.OutputReportByteLength);
                                        if (rxHidpp1 != null && rxHidpp1.Length >= 5 && rxHidpp1[2] == 0x0D)
                                        {
                                            int pct = rxHidpp1[3];
                                            if (pct >= 0 && pct <= 100)
                                            {
                                                batteryPercent = pct;
                                                isCharging = (rxHidpp1[4] == 1);
                                            }
                                        }
                                    }

                                    hardwareDpiY = -1;
                                    extDpiFeatIdx = 0;
                                    dpiFeatIdx = 0;
                                    extRateFeatIdx = 0;
                                    rateFeatIdx = 0;
                                    detectedRate = 1000;
                                    supportedRates = new int[] { 1000, 2000, 4000, 8000 };

                                    // Feature 0x2202: Extended Adjustable DPI (XY independent)
                                    byte[] txFeat2202 = new byte[20];
                                    txFeat2202[0] = 0x11;
                                    txFeat2202[1] = devIdx;
                                    txFeat2202[2] = 0x00;
                                    txFeat2202[3] = 0x08;
                                    txFeat2202[4] = 0x22;
                                    txFeat2202[5] = 0x02; // 0x2202
                                    byte[] rxFeat2202 = SendAndReceive(handle, hEvent, txFeat2202, bestEndpoint.InputReportByteLength, 200, bestEndpoint.OutputReportByteLength, devIdx, 0x00, 0x08);
                                    if (rxFeat2202 != null && rxFeat2202.Length >= 5 && rxFeat2202[4] > 0)
                                    {
                                        extDpiFeatIdx = rxFeat2202[4];
                                        byte[] txGetExtDpi = new byte[20];
                                        txGetExtDpi[0] = 0x11;
                                        txGetExtDpi[1] = devIdx;
                                        txGetExtDpi[2] = extDpiFeatIdx;
                                        txGetExtDpi[3] = 0x58; // Func 5 (0x50) | swid 8 = 0x58
                                        txGetExtDpi[4] = 0x00; // Sensor 0
                                        byte[] rxGetExtDpi = SendAndReceive(handle, hEvent, txGetExtDpi, bestEndpoint.InputReportByteLength, 200, bestEndpoint.OutputReportByteLength, devIdx, extDpiFeatIdx, 0x08);
                                        if (rxGetExtDpi != null && rxGetExtDpi.Length >= 9)
                                        {
                                            int dx = (rxGetExtDpi[5] << 8) | rxGetExtDpi[6];
                                            int dy = (rxGetExtDpi[7] << 8) | rxGetExtDpi[8];
                                            if (dx >= 50 && dx <= 50000) hardwareDpi = dx;
                                            if (dy >= 50 && dy <= 50000) hardwareDpiY = dy;
                                        }
                                    }

                                    // Feature 0x2201: Adjustable DPI (Unified)
                                    byte[] txFeat2201 = new byte[20];
                                    txFeat2201[0] = 0x11;
                                    txFeat2201[1] = devIdx;
                                    txFeat2201[2] = 0x00;
                                    txFeat2201[3] = 0x08;
                                    txFeat2201[4] = 0x22;
                                    txFeat2201[5] = 0x01; // 0x2201
                                    byte[] rxFeat2201 = SendAndReceive(handle, hEvent, txFeat2201, bestEndpoint.InputReportByteLength, 200, bestEndpoint.OutputReportByteLength, devIdx, 0x00, 0x08);
                                    if (rxFeat2201 != null && rxFeat2201.Length >= 5 && rxFeat2201[4] > 0)
                                    {
                                        dpiFeatIdx = rxFeat2201[4];
                                        if (hardwareDpi <= 0)
                                        {
                                            byte[] txGetDpi = new byte[20];
                                            txGetDpi[0] = 0x11;
                                            txGetDpi[1] = devIdx;
                                            txGetDpi[2] = dpiFeatIdx;
                                            txGetDpi[3] = 0x18; // Func 1 (0x10) | swid 8
                                            txGetDpi[4] = 0x00; // Sensor 0
                                            byte[] rxGetDpi = SendAndReceive(handle, hEvent, txGetDpi, bestEndpoint.InputReportByteLength, 250, bestEndpoint.OutputReportByteLength, devIdx, dpiFeatIdx, 0x08);
                                            if (rxGetDpi != null && rxGetDpi.Length >= 7)
                                            {
                                                int dVal = (rxGetDpi[5] << 8) | rxGetDpi[6];
                                                if (dVal >= 50 && dVal <= 50000)
                                                {
                                                    hardwareDpi = dVal;
                                                }
                                            }
                                        }
                                    }

                                    // Feature 0x8061: Extended Adjustable Report Rate
                                    byte[] txFeat8061 = new byte[20];
                                    txFeat8061[0] = 0x11;
                                    txFeat8061[1] = devIdx;
                                    txFeat8061[2] = 0x00;
                                    txFeat8061[3] = 0x08;
                                    txFeat8061[4] = 0x80;
                                    txFeat8061[5] = 0x61; // 0x8061
                                    byte[] rxFeat8061 = SendAndReceive(handle, hEvent, txFeat8061, bestEndpoint.InputReportByteLength, 200, bestEndpoint.OutputReportByteLength, devIdx, 0x00, 0x08);
                                    if (rxFeat8061 != null && rxFeat8061.Length >= 5 && rxFeat8061[4] > 0)
                                    {
                                        extRateFeatIdx = rxFeat8061[4];
                                        byte[] txGetRate = new byte[20];
                                        txGetRate[0] = 0x11;
                                        txGetRate[1] = devIdx;
                                        txGetRate[2] = extRateFeatIdx;
                                        txGetRate[3] = 0x28; // Func 2 (0x20) | swid 8
                                        byte[] rxGetRate = SendAndReceive(handle, hEvent, txGetRate, bestEndpoint.InputReportByteLength, 200, bestEndpoint.OutputReportByteLength, devIdx, extRateFeatIdx, 0x08);
                                        if (rxGetRate != null && rxGetRate.Length >= 5)
                                        {
                                            int code = rxGetRate[4];
                                            if (code == 6) detectedRate = 8000;
                                            else if (code == 5) detectedRate = 4000;
                                            else if (code == 4) detectedRate = 2000;
                                            else if (code == 3) detectedRate = 1000;
                                            else if (code == 2) detectedRate = 500;
                                            else if (code == 1) detectedRate = 250;
                                            else if (code == 0) detectedRate = 125;
                                        }
                                        supportedRates = new int[] { 125, 250, 500, 1000, 2000, 4000, 8000 };
                                    }
                                    else
                                    {
                                        // Feature 0x8060: Standard Adjustable Report Rate
                                        byte[] txFeat8060 = new byte[20];
                                        txFeat8060[0] = 0x11;
                                        txFeat8060[1] = devIdx;
                                        txFeat8060[2] = 0x00;
                                        txFeat8060[3] = 0x08;
                                        txFeat8060[4] = 0x80;
                                        txFeat8060[5] = 0x60; // 0x8060
                                        byte[] rxFeat8060 = SendAndReceive(handle, hEvent, txFeat8060, bestEndpoint.InputReportByteLength, 200, bestEndpoint.OutputReportByteLength, devIdx, 0x00, 0x08);
                                        if (rxFeat8060 != null && rxFeat8060.Length >= 5 && rxFeat8060[4] > 0)
                                        {
                                            rateFeatIdx = rxFeat8060[4];
                                            byte[] txGetRate = new byte[20];
                                            txGetRate[0] = 0x11;
                                            txGetRate[1] = devIdx;
                                            txGetRate[2] = rateFeatIdx;
                                            txGetRate[3] = 0x18; // Func 1 (0x10) | swid 8
                                            byte[] rxGetRate = SendAndReceive(handle, hEvent, txGetRate, bestEndpoint.InputReportByteLength, 200, bestEndpoint.OutputReportByteLength, devIdx, rateFeatIdx, 0x08);
                                            if (rxGetRate != null && rxGetRate.Length >= 5)
                                            {
                                                int ms = rxGetRate[4];
                                                if (ms == 1) detectedRate = 1000;
                                                else if (ms == 2) detectedRate = 500;
                                                else if (ms == 4) detectedRate = 250;
                                                else if (ms == 8) detectedRate = 125;
                                            }
                                            supportedRates = new int[] { 125, 250, 500, 1000 };
                                        }
                                    }
                                }

                                string finalName = !string.IsNullOrEmpty(detectedName) ? detectedName : GetModelName(detectedPid, foundName);
                                DeviceCategory cat = DetectCategory(detectedPid, finalName);
                                string deviceId = "Logitech:" + foundVid.ToString("X4") + ":" + detectedPid.ToString("X4") + (devIdx != 0xFF ? (":" + devIdx) : "");

                                if (deviceOnline)
                                {
                                    LogitechEndpoint ep = new LogitechEndpoint
                                    {
                                        DeviceId = deviceId,
                                        DevicePath = bestEndpoint.DevicePath,
                                        DevIndex = devIdx,
                                        InputReportLength = bestEndpoint.InputReportByteLength,
                                        OutputReportLength = bestEndpoint.OutputReportByteLength,
                                        CurrentDpi = (hardwareDpi > 0) ? hardwareDpi : CachedDpi,
                                        CurrentDpiY = (hardwareDpiY > 0) ? hardwareDpiY : ((hardwareDpi > 0) ? hardwareDpi : CachedDpiY),
                                        PollingRate = (detectedRate > 0) ? detectedRate : CachedPollingRate,
                                        DpiFeatureIndex = dpiFeatIdx,
                                        ExtDpiFeatureIndex = extDpiFeatIdx,
                                        RateFeatureIndex = rateFeatIdx,
                                        ExtRateFeatureIndex = extRateFeatIdx,
                                        SupportedRates = supportedRates,
                                        LastSeen = DateTime.Now
                                    };
                                    activeEndpoints[deviceId] = ep;
                                }

                                if (batteryPercent >= 0)
                                {
                                    CachedBatteryPercent = batteryPercent;
                                    CachedIsCharging = isCharging;
                                    CachedLastUpdated = DateTime.Now;
                                    CachedDeviceName = finalName;
                                    HasValidHardwareReading = true;
                                    deviceBatteryCache[deviceId] = batteryPercent;
                                    SaveHardwareCache();
                                }

                                int displayBattery = (batteryPercent >= 0) ? batteryPercent : GetCachedBatteryForDevice(deviceId);
                                bool displayCharging = (batteryPercent >= 0) ? isCharging : CachedIsCharging;

                                if (deviceOnline || isDongle)
                                {
                                    int finalDpiX = (hardwareDpi > 0) ? hardwareDpi : CachedDpi;
                                    int finalDpiY = (hardwareDpiY > 0) ? hardwareDpiY : finalDpiX;
                                    int finalRate = (detectedRate > 0) ? detectedRate : CachedPollingRate;

                                    MouseBatteryInfo info = new MouseBatteryInfo
                                    {
                                        DeviceId = deviceId,
                                        HardwareFingerprint = deviceId,
                                        Brand = "Logitech",
                                        DeviceName = finalName,
                                        Category = cat,
                                        Transport = isDongle ? "Lightspeed 无线" : "USB 有线",
                                        IsConnected = true,
                                        IsSleeping = !deviceOnline,
                                        IsDonglePresent = true,
                                        IsCustom = false,
                                        BatteryPercent = displayBattery,
                                        ExactBatteryPercent = displayBattery,
                                        IsCharging = displayCharging,
                                        LastUpdated = (batteryPercent >= 0) ? DateTime.Now : (CachedLastUpdated != DateTime.MinValue ? CachedLastUpdated : DateTime.Now),
                                        Dpi = finalDpiX,
                                        DpiY = finalDpiY,
                                        IsXyIndependent = (finalDpiY != finalDpiX),
                                        DpiStage = 2,
                                        DpiStageCount = 5,
                                        DpiStages = new int[] { 400, 800, 1600, 3200, 6400 },
                                        DpiStagesY = new int[] { 400, 800, 1600, 3200, 6400 },
                                        PollingRate = finalRate,
                                        SupportedPollingRates = supportedRates
                                    };

                                    if (deviceOnline)
                                    {
                                        results.Add(info);
                                        if (!isDongle) break;
                                    }
                                    else if (isDongle && devIdx == 0x01 && results.Count == 0)
                                    {
                                        results.Add(info);
                                    }
                                }
                            }
                        }
                        finally
                        {
                            CloseHandle(hEvent);
                        }
                    }
                }
                finally
                {
                    CloseHandle(handle);
                }
            }

            return results;
        }
    }

    private static LogitechEndpoint FindEndpoint(string targetDeviceId)
    {
        if (!string.IsNullOrEmpty(targetDeviceId))
        {
            LogitechEndpoint ep;
            if (activeEndpoints.TryGetValue(targetDeviceId, out ep) && ep != null) return ep;
        }
        if (activeEndpoints.Count > 0)
        {
            foreach (var kvp in activeEndpoints)
            {
                if (kvp.Value != null) return kvp.Value;
            }
        }

        QueryAllLogitechDevices();

        if (!string.IsNullOrEmpty(targetDeviceId))
        {
            LogitechEndpoint ep;
            if (activeEndpoints.TryGetValue(targetDeviceId, out ep) && ep != null) return ep;
        }
        foreach (var kvp in activeEndpoints)
        {
            if (kvp.Value != null) return kvp.Value;
        }
        return null;
    }

    public static bool SetLogitechDpi(int dpi, string targetDeviceId = null)
    {
        return SetLogitechDpi(dpi, dpi, targetDeviceId);
    }

    public static bool SetLogitechDpi(int dpiX, int dpiY, string targetDeviceId = null)
    {
        lock (hidLock)
        {
            LogitechEndpoint ep = FindEndpoint(targetDeviceId);
            if (ep == null) return false;

            IntPtr handle = CreateFile(
                ep.DevicePath,
                GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_FLAG_OVERLAPPED,
                IntPtr.Zero);

            if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;

            IntPtr hEvent = CreateEvent(IntPtr.Zero, true, false, null);
            if (hEvent == IntPtr.Zero)
            {
                CloseHandle(handle);
                return false;
            }

            try
            {
                bool success = false;
                int inLen = ep.InputReportLength > 0 ? ep.InputReportLength : 20;

                // 1. Try Feature 0x2202 (Extended Adjustable DPI with independent X/Y)
                if (ep.ExtDpiFeatureIndex > 0)
                {
                    byte[] tx = new byte[20];
                    tx[0] = 0x11;
                    tx[1] = ep.DevIndex;
                    tx[2] = ep.ExtDpiFeatureIndex;
                    tx[3] = 0x60; // Func 6: Set Extended DPI
                    tx[4] = 0x00; // Sensor 0
                    tx[5] = (byte)(dpiX >> 8);
                    tx[6] = (byte)(dpiX & 0xFF);
                    tx[7] = (byte)(dpiY >> 8);
                    tx[8] = (byte)(dpiY & 0xFF);

                    byte[] rx = SendAndReceive(handle, hEvent, tx, inLen, 350);
                    if (rx != null && rx.Length >= 4 && rx[2] == ep.ExtDpiFeatureIndex)
                    {
                        success = true;
                    }
                }

                // 2. Fallback to Feature 0x2201 (Adjustable DPI unified)
                if (!success && ep.DpiFeatureIndex > 0)
                {
                    byte[] tx = new byte[20];
                    tx[0] = 0x11;
                    tx[1] = ep.DevIndex;
                    tx[2] = ep.DpiFeatureIndex;
                    tx[3] = 0x30; // Func 3: Set Sensor DPI
                    tx[4] = 0x00; // Sensor 0
                    tx[5] = (byte)(dpiX >> 8);
                    tx[6] = (byte)(dpiX & 0xFF);

                    byte[] rx = SendAndReceive(handle, hEvent, tx, inLen, 350);
                    if (rx != null && rx.Length >= 4 && rx[2] == ep.DpiFeatureIndex)
                    {
                        success = true;
                    }
                }

                if (success)
                {
                    ep.CurrentDpi = dpiX;
                    ep.CurrentDpiY = dpiY;
                    CachedDpi = dpiX;
                    CachedDpiY = dpiY;
                    SaveHardwareCache();
                }

                return success;
            }
            finally
            {
                CloseHandle(hEvent);
                CloseHandle(handle);
            }
        }
    }

    public static bool SetLogitechPollingRate(int hz, string targetDeviceId = null)
    {
        lock (hidLock)
        {
            LogitechEndpoint ep = FindEndpoint(targetDeviceId);
            if (ep == null) return false;

            IntPtr handle = CreateFile(
                ep.DevicePath,
                GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                FILE_FLAG_OVERLAPPED,
                IntPtr.Zero);

            if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;

            IntPtr hEvent = CreateEvent(IntPtr.Zero, true, false, null);
            if (hEvent == IntPtr.Zero)
            {
                CloseHandle(handle);
                return false;
            }

            try
            {
                bool success = false;
                int inLen = ep.InputReportLength > 0 ? ep.InputReportLength : 20;

                // 1. Try Feature 0x8061 (Extended Adjustable Report Rate, 1K/2K/4K/8K)
                if (ep.ExtRateFeatureIndex > 0)
                {
                    byte rateIdx;
                    if (hz >= 8000) rateIdx = 6;
                    else if (hz >= 4000) rateIdx = 5;
                    else if (hz >= 2000) rateIdx = 4;
                    else if (hz >= 1000) rateIdx = 3;
                    else if (hz >= 500) rateIdx = 2;
                    else if (hz >= 250) rateIdx = 1;
                    else rateIdx = 0; // 125Hz

                    byte[] tx = new byte[20];
                    tx[0] = 0x11;
                    tx[1] = ep.DevIndex;
                    tx[2] = ep.ExtRateFeatureIndex;
                    tx[3] = 0x30; // Func 3: Set report rate
                    tx[4] = rateIdx;

                    byte[] rx = SendAndReceive(handle, hEvent, tx, inLen, 350);
                    if (rx != null && rx.Length >= 4 && rx[2] == ep.ExtRateFeatureIndex)
                    {
                        success = true;
                    }
                }

                // 2. Fallback to Feature 0x8060 (Standard Adjustable Report Rate)
                if (!success && ep.RateFeatureIndex > 0)
                {
                    byte msInterval;
                    if (hz >= 1000) msInterval = 1;
                    else if (hz >= 500) msInterval = 2;
                    else if (hz >= 250) msInterval = 4;
                    else msInterval = 8;

                    byte[] tx = new byte[20];
                    tx[0] = 0x11;
                    tx[1] = ep.DevIndex;
                    tx[2] = ep.RateFeatureIndex;
                    tx[3] = 0x20; // Func 2: Set report rate
                    tx[4] = msInterval;

                    byte[] rx = SendAndReceive(handle, hEvent, tx, inLen, 350);
                    if (rx != null && rx.Length >= 4 && rx[2] == ep.RateFeatureIndex)
                    {
                        success = true;
                    }
                }

                if (success)
                {
                    ep.PollingRate = hz;
                    CachedPollingRate = hz;
                    SaveHardwareCache();
                }

                return success;
            }
            finally
            {
                CloseHandle(hEvent);
                CloseHandle(handle);
            }
        }
    }

    public static bool FastQueryDpi(out int curDpi, out int activeStage, out int stageCount, string targetDeviceId = null)
    {
        int dpiY;
        return FastQueryDpi(out curDpi, out dpiY, out activeStage, out stageCount, targetDeviceId);
    }

    public static bool FastQueryDpi(out int curDpiX, out int curDpiY, out int activeStage, out int stageCount, string targetDeviceId = null)
    {
        curDpiX = -1;
        curDpiY = -1;
        activeStage = 1;
        stageCount = 5;

        LogitechEndpoint ep = FindEndpoint(targetDeviceId);
        if (ep == null) return false;

        if (ep.CurrentDpi > 0)
        {
            curDpiX = ep.CurrentDpi;
            curDpiY = ep.CurrentDpiY > 0 ? ep.CurrentDpiY : ep.CurrentDpi;
            activeStage = MatchDpiStage(curDpiX);
            return true;
        }

        return false;
    }

    private static int MatchDpiStage(int dpi)
    {
        int[] standardStages = new int[] { 400, 800, 1600, 3200, 6400 };
        for (int i = 0; i < standardStages.Length; i++)
        {
            if (Math.Abs(standardStages[i] - dpi) <= 50) return i + 1;
        }
        return 1;
    }

    public static MouseBatteryInfo QueryLogitechDeviceInfo(string deviceFingerprint = null)
        {
            var list = QueryAllLogitechDevices();
            if (list == null || list.Count == 0) return null;
            if (!string.IsNullOrEmpty(deviceFingerprint))
            {
                foreach (var d in list)
                {
                    if (d.DeviceId != null && (d.DeviceId.Equals(deviceFingerprint, StringComparison.OrdinalIgnoreCase) ||
                                               (d.HardwareFingerprint != null && d.HardwareFingerprint.Equals(deviceFingerprint, StringComparison.OrdinalIgnoreCase))))
                    {
                        return d;
                    }
                }
            }
            return list[0];
        }
    }
}