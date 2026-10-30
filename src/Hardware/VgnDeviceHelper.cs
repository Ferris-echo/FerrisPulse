// ============================================================================
// 模块名称: VgnDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   VGN / VXE / 蜻蜓 / ATK 系列鼠标私有 HID 协议通信实现
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
        public static int CachedBatteryVoltageMv = -1;

        // 锂电池电压（mV）→ 电量百分比：复刻官方驱动 Config.ini 的 BatteryParam 表
        // （21 个阈值对应 0%~100%，每档 5%），档间线性插值，保证与官方读数口径一致
        private static readonly int[] OfficialVoltageTable = new int[]
        {
            3050, 3420, 3480, 3540, 3600, 3660, 3720, 3760, 3800, 3840, 3880,
            3920, 3940, 3960, 3980, 4000, 4020, 4040, 4060, 4080, 4110
        };

        public static int VoltageToPercent(int mv)
        {
            if (mv <= 0) return -1;
            int last = OfficialVoltageTable.Length - 1;
            if (mv <= OfficialVoltageTable[0]) return 0;
            if (mv >= OfficialVoltageTable[last]) return 100;
            for (int i = 0; i < last; i++)
            {
                int vLow = OfficialVoltageTable[i];
                int vHigh = OfficialVoltageTable[i + 1];
                if (mv >= vLow && mv < vHigh)
                {
                    int pLow = i * 5;
                    int pHigh = (i + 1) * 5;
                    int num = (mv - vLow) * (pHigh - pLow);
                    int den = vHigh - vLow;
                    return pLow + (num + den / 2) / den;
                }
            }
            return 100;
        }

        // 当前电压附近每 1% 对应的毫伏数（用于把电压斜率换算成电量斜率）
        public static double MillivoltsPerPercent(int mv)
        {
            int last = OfficialVoltageTable.Length - 1;
            if (mv <= OfficialVoltageTable[0]) return (OfficialVoltageTable[1] - OfficialVoltageTable[0]) / 5.0;
            if (mv >= OfficialVoltageTable[last]) return (OfficialVoltageTable[last] - OfficialVoltageTable[last - 1]) / 5.0;
            for (int i = 0; i < last; i++)
            {
                if (mv >= OfficialVoltageTable[i] && mv < OfficialVoltageTable[i + 1])
                {
                    return (OfficialVoltageTable[i + 1] - OfficialVoltageTable[i]) / 5.0;
                }
            }
            return 8.0;
        }

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

            // 1. Drain pending reports（循环清空直到连续两次 15ms 无残留，杜绝响应错位）
            byte[] drain = new byte[inLen];
            int emptyRounds = 0;
            while (true)
            {
                OVERLAPPED dOl = new OVERLAPPED();
                dOl.hEvent = hEvent;
                uint readBytes;
                bool ok = ReadFile(handle, drain, (uint)drain.Length, out readBytes, ref dOl);
                if (!ok && Marshal.GetLastWin32Error() == 997) // ERROR_IO_PENDING
                {
                    if (WaitForSingleObject(hEvent, 15) == WAIT_OBJECT_0)
                    {
                        GetOverlappedResult(handle, ref dOl, out readBytes, false);
                        emptyRounds = 0;
                        continue;
                    }
                    CancelIo(handle);
                    emptyRounds++;
                    if (emptyRounds >= 2) break;
                    continue;
                }
                emptyRounds++;
                if (emptyRounds >= 2) break;
            }

            // 2. Write command
            OVERLAPPED wOl = new OVERLAPPED();
            wOl.hEvent = hEvent;
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

        // ====================================================================
        // Yjx 通道 (391D:1A03, VGN Dragonfly 3 Pro 等)
        // 端点: UsagePage 0xFF01 / Usage 0x0010 / 65 字节, Report ID 0x00
        // 帧:   [0]=0x00 [1]=0x55 [2]=cmd [3..8]=args [9..54]=46 字节 profileBuf
        // 回复: [0]=0x00 [1]=0xAA [2]=cmd [9..]=payload
        // 铁律: 必须 WriteFile(中断 OUT)；HidD_SetOutputReport(控制传输) 会静默丢包。
        // ====================================================================
        private const byte YjxCmdMouseOnline = 0xED;
        private const byte YjxCmdGetBattery = 0x30;
        private const byte YjxCmdGetPerformance = 0x0E;
        private const byte YjxCmdSetPerformance = 0x0F;
        private const int YjxReportLen = 65;
        private const int YjxProfileOffset = 9;
        private const int YjxProfileLen = 46;

        private class YjxSnapshot
        {
            public bool Online;
            public int Battery = -1;
            public bool Charging;
            public int RateCode = 4;
            public int StageCount = 5;
            public int CurrentStage = 1;
            public int HardwareStage;
            public int[] Stages;
            public byte[] Profile;
        }

        private static byte[] BuildYjxCmd(byte cmd)
        {
            byte[] buf = new byte[YjxReportLen];
            buf[0] = 0x00;
            buf[1] = 0x55;
            buf[2] = cmd;
            return buf;
        }

        private static byte[] BuildYjxSetProfile(byte[] profile)
        {
            byte[] buf = new byte[YjxReportLen];
            buf[0] = 0x00;
            buf[1] = 0x55;
            buf[2] = YjxCmdSetPerformance;
            buf[5] = 0x2E; // 46
            buf[6] = 0x01;
            buf[7] = 0x01;
            if (profile != null)
            {
                int n = profile.Length < YjxProfileLen ? profile.Length : YjxProfileLen;
                for (int i = 0; i < n; i++) buf[YjxProfileOffset + i] = profile[i];
            }
            return buf;
        }

        private static void YjxDrain(IntPtr handle, IntPtr hEvent, int inLen)
        {
            if (inLen <= 0) inLen = YjxReportLen;
            byte[] drain = new byte[inLen];
            while (true)
            {
                OVERLAPPED dOl = new OVERLAPPED();
                dOl.hEvent = hEvent;
                uint readBytes;
                bool ok = ReadFile(handle, drain, (uint)drain.Length, out readBytes, ref dOl);
                if (!ok && Marshal.GetLastWin32Error() == 997)
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
        }

        private static bool YjxWriteRaw(IntPtr handle, IntPtr hEvent, byte[] tx)
        {
            OVERLAPPED wOl = new OVERLAPPED();
            wOl.hEvent = hEvent;
            uint written;
            bool wOk = WriteFile(handle, tx, (uint)tx.Length, out written, ref wOl);
            if (!wOk && Marshal.GetLastWin32Error() == 997)
            {
                if (WaitForSingleObject(hEvent, 500) == WAIT_OBJECT_0)
                {
                    GetOverlappedResult(handle, ref wOl, out written, false);
                    return true;
                }
                CancelIo(handle);
                return false;
            }
            return wOk;
        }

        private static byte[] YjxReadRaw(IntPtr handle, IntPtr hEvent, int inLen, int timeoutMs)
        {
            if (inLen <= 0) inLen = YjxReportLen;
            byte[] rx = new byte[inLen];
            OVERLAPPED rOl = new OVERLAPPED();
            rOl.hEvent = hEvent;
            uint rxRead = 0;
            bool rOk = ReadFile(handle, rx, (uint)rx.Length, out rxRead, ref rOl);
            if (!rOk && Marshal.GetLastWin32Error() == 997)
            {
                if (WaitForSingleObject(hEvent, (uint)timeoutMs) == WAIT_OBJECT_0)
                {
                    GetOverlappedResult(handle, ref rOl, out rxRead, false);
                    return rx;
                }
                CancelIo(handle);
                return null;
            }
            if (rOk) return rx;
            return null;
        }

        private static byte[] YjxSendExpect(IntPtr handle, IntPtr hEvent, byte[] tx, int inLen, int timeoutMs, byte expectCmd)
        {
            YjxDrain(handle, hEvent, inLen);
            if (!YjxWriteRaw(handle, hEvent, tx)) return null;
            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < timeoutMs)
            {
                int remain = timeoutMs - (int)sw.ElapsedMilliseconds;
                if (remain < 30) remain = 30;
                byte[] rx = YjxReadRaw(handle, hEvent, inLen, remain);
                if (rx == null) return null;
                if (rx[1] != 0xAA) continue;
                if (rx[2] == expectCmd) return rx;
            }
            return null;
        }

        private static int YjxRateCodeToHz(int code)
        {
            switch (code)
            {
                case 1: return 125;
                case 2: return 250;
                case 3: return 500;
                case 5: return 2000;
                case 6: return 4000;
                case 7: return 8000;
                default: return 1000;
            }
        }

        private static int YjxHzToRateCode(int hz)
        {
            if (hz >= 8000) return 7;
            switch (hz)
            {
                case 125: return 1;
                case 250: return 2;
                case 500: return 3;
                case 1000: return 4;
                case 2000: return 5;
                case 4000: return 6;
                default: return 0;
            }
        }

        private static YjxSnapshot QueryYjx(IntPtr handle, IntPtr hEvent, int inLen)
        {
            YjxSnapshot snap = new YjxSnapshot();
            byte[] online = YjxSendExpect(handle, hEvent, BuildYjxCmd(YjxCmdMouseOnline), inLen, 400, YjxCmdMouseOnline);
            if (online != null && online.Length > 9)
            {
                snap.Online = (online[9] != 0 && online[9] != 1);
            }
            Thread.Sleep(30);

            byte[] bat = YjxSendExpect(handle, hEvent, BuildYjxCmd(YjxCmdGetBattery), inLen, 800, YjxCmdGetBattery);
            if (bat != null && bat.Length > 10)
            {
                int lvl = bat[9];
                if (lvl >= 0 && lvl <= 100)
                {
                    snap.Battery = lvl;
                    snap.Charging = (bat[10] == 1);
                    snap.Online = true;
                }
            }

            byte[] perf = YjxSendExpect(handle, hEvent, BuildYjxCmd(YjxCmdGetPerformance), inLen, 800, YjxCmdGetPerformance);
            if (perf != null && perf.Length >= YjxProfileOffset + YjxProfileLen)
            {
                snap.Online = true;
                snap.RateCode = perf[11];
                int cnt = perf[12];
                int cur = perf[13] - 1;
                if (cnt < 1 || cnt > 8) cnt = 5;
                if (cnt > 5) cnt = 5;
                if (cur < 0 || cur >= cnt) cur = 0;
                int[] stages = new int[cnt];
                for (int i = 0; i < cnt; i++)
                {
                    int o = 14 + i * 2;
                    int v = perf[o] | (perf[o + 1] << 8);
                    if (v < 50 || v > 36000) v = 0;
                    stages[i] = v;
                }
                snap.StageCount = cnt;
                snap.HardwareStage = perf[13];
                snap.CurrentStage = cur + 1;
                snap.Stages = stages;
                byte[] prof = new byte[YjxProfileLen];
                for (int i = 0; i < YjxProfileLen; i++) prof[i] = perf[YjxProfileOffset + i];
                snap.Profile = prof;
            }
            return snap;
        }

        private static bool FindYjxEndpoint(out IntPtr handle, out int inLen)
        {
            handle = IntPtr.Zero;
            inLen = YjxReportLen;
            Guid hidGuid;
            RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
            IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
            if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return false;
            try
            {
                RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(ifData);
                uint idx = 0;
                while (RazerDeviceHelper.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, idx++, ref ifData))
                {
                    uint reqSize;
                    RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                    if (reqSize == 0) continue;
                    IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                    Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);
                    try
                    {
                        if (!RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero)) continue;
                        string path = Marshal.PtrToStringAuto(new IntPtr(detailBuffer.ToInt64() + 4));
                        if (path == null || path.ToLower().IndexOf("vid_391d") < 0) continue;
                        IntPtr h = RazerDeviceHelper.CreateFile(
                            path,
                            GENERIC_READ | GENERIC_WRITE,
                            FILE_SHARE_READ | FILE_SHARE_WRITE,
                            IntPtr.Zero,
                            OPEN_EXISTING,
                            FILE_FLAG_OVERLAPPED,
                            IntPtr.Zero);
                        if (h == IntPtr.Zero || h.ToInt64() == -1) continue;
                        IntPtr preparsed;
                        if (!RazerDeviceHelper.HidD_GetPreparsedData(h, out preparsed))
                        {
                            RazerDeviceHelper.CloseHandle(h);
                            continue;
                        }
                        RazerDeviceHelper.HIDP_CAPS caps;
                        RazerDeviceHelper.HidP_GetCaps(preparsed, out caps);
                        RazerDeviceHelper.HidD_FreePreparsedData(preparsed);
                        if (caps.UsagePage == 0xFF01 && caps.Usage == 0x0010
                            && caps.InputReportByteLength >= YjxReportLen
                            && caps.OutputReportByteLength >= YjxReportLen)
                        {
                            handle = h;
                            inLen = caps.InputReportByteLength;
                            return true;
                        }
                        RazerDeviceHelper.CloseHandle(h);
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
            return false;
        }

        public static bool SetYjxProfile(int rateHz, int stage, int dpiValue)
        {
            lock (hidLock)
            {
                IntPtr handle;
                int inLen;
                if (!FindYjxEndpoint(out handle, out inLen)) return false;
                IntPtr hEvent = CreateEvent(IntPtr.Zero, true, false, null);
                bool ok = false;
                try
                {
                    if (hEvent == IntPtr.Zero) return false;
                    YjxSnapshot before = QueryYjx(handle, hEvent, inLen);
                    byte[] profile = before.Profile;
                    if (profile == null) profile = new byte[YjxProfileLen];

                    if (rateHz > 0)
                    {
                        int code = YjxHzToRateCode(rateHz);
                        if (code > 0) profile[2] = (byte)code;
                    }
                    if (stage > 0 && stage < 64)
                    {
                        profile[4] = (byte)stage;
                        if (dpiValue >= 50 && dpiValue <= 36000)
                        {
                            int off = 5 + (stage - 1) * 2;
                            if (off + 1 < YjxProfileLen)
                            {
                                profile[off] = (byte)(dpiValue & 0xFF);
                                profile[off + 1] = (byte)((dpiValue >> 8) & 0xFF);
                            }
                        }
                    }

                    YjxSendExpect(handle, hEvent, BuildYjxSetProfile(profile), inLen, 500, YjxCmdSetPerformance);
                    Thread.Sleep(80);

                    YjxSnapshot after = QueryYjx(handle, hEvent, inLen);
                    if (after.Profile == null)
                    {
                        ok = false;
                    }
                    else
                    {
                        ok = true;
                        if (stage > 0 && after.CurrentStage != stage) ok = false;
                        if (rateHz > 0 && YjxRateCodeToHz(after.RateCode) != rateHz) ok = false;
                    }
                }
                finally
                {
                    if (hEvent != IntPtr.Zero) RazerDeviceHelper.CloseHandle(hEvent);
                    RazerDeviceHelper.CloseHandle(handle);
                }
                return ok;
            }
        }

        public static bool SetYjxDpiStage(int stage)
        {
            return SetYjxProfile(0, stage, 0);
        }

        public static bool SetYjxPollingRate(int hz)
        {
            return SetYjxProfile(hz, 0, 0);
        }

        public static bool IsYjxDevice(string deviceId)
        {
            if (deviceId == null) return false;
            return deviceId.StartsWith("VGN:391D:", StringComparison.OrdinalIgnoreCase)
                || deviceId.StartsWith("HID:391D:", StringComparison.OrdinalIgnoreCase);
        }

        public static string GetModelName(int pid, string productString, int cid, int mid)
        {
            if (cid == 7 && mid == 2) return "Zaopin Z2";
            if (cid == 118 && mid == 11) return "VGN Dragonfly F2 Pro Max";
            if (cid == 118 && mid == 10) return "VGN Dragonfly F2 Pro";
            if (cid == 118 && mid == 9) return "VGN Dragonfly F2";
            if (pid == 0xF503) return "VGN Dragonfly F1 Pro";
            if (pid == 0xF526 || pid == 0xF527) return "Zaopin Z2";
            if (!string.IsNullOrEmpty(productString)) return productString;
            switch (pid)
            {
                case 0xF526: return "Zaopin Z2"; // Nano 1K 直插接收器
                case 0xF527: return "Zaopin Z2"; // 4K 底座式接收器 (NORDIC nRF52810)
                case 0xFB3E: return "VGN Dragonfly F2 Pro Max";
                case 0xFC03: return "VXE Dragonfly R1 Pro";
                case 0xFC04: return "VXE Dragonfly R1 Pro Max";
                case 0xFC05: return "VXE Dragonfly R1 SE";
                case 0xFB24: return "VGN Dragonfly F1";
                case 0xFB25: return "VGN Dragonfly F1 Pro";
                case 0xFB26: return "VGN Dragonfly F1 Pro Max";
                case 0xFB27: return "VGN Dragonfly F1 MOBA";
                case 0x1A03: return "VGN Dragonfly 3 Pro";
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
            if (pid == 0xFB3E || pid == 0x1A03 || name.Contains("f2") || name.Contains("8k") || name.Contains("dragonfly 3") || name.Contains("8000") || name.Contains("atk"))
            {
                // 8K models
                return new int[] { 125, 250, 500, 1000, 2000, 4000, 8000 };
            }
            // 4K models (F1 Pro/Pro Max/MOBA, R1 Pro/Pro Max)
            return new int[] { 125, 250, 500, 1000, 2000, 4000 };
        }

        // 构建配置读写报文（read=0x08 / write=0x07，含 16 位地址与数据）
        private static byte[] BuildConfigReport(byte cmd, int address, byte[] data)
        {
            byte[] buf16 = new byte[16];
            buf16[0] = cmd;
            buf16[2] = (byte)((address >> 8) & 0xFF);
            buf16[3] = (byte)(address & 0xFF);
            if (data != null)
            {
                buf16[4] = (byte)data.Length;
                for (int i = 0; i < data.Length && (5 + i) < 15; i++)
                {
                    buf16[5 + i] = data[i];
                }
            }
            byte crc = CalcChecksum(buf16);
            buf16[15] = (byte)(crc - 8);
            byte[] report17 = new byte[17];
            report17[0] = 0x08;
            Array.Copy(buf16, 0, report17, 1, 16);
            return report17;
        }

        // 打开 FF02 通道 → 握手 → 写配置（data 为配置值字节，含奇偶校验）
        private static bool ExecuteVgnWriteBytes(int address, byte[] data)
        {
            return ExecuteVgnWriteBytes(address, data, 0x07);
        }

        // cmd：0x07 通用写（回报率/切档位），0x08 DPI 值落盘（抓包实测官方用 0x08 提交 DPI）
        private static bool ExecuteVgnWriteBytes(int address, byte[] data, byte cmd)
        {
            lock (hidLock)
            {
                try
                {
                    Guid hidGuid;
                    RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
                    IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
                    if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return false;

                    RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                    uint memberIdx = 0;

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
                                if (devicePath != null && devicePath.ToLower().Contains("vid_3554"))
                                {
                                    IntPtr handle = RazerDeviceHelper.CreateFile(devicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                                    if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                    {
                                        try
                                        {
                                            IntPtr preparsed;
                                            if (RazerDeviceHelper.HidD_GetPreparsedData(handle, out preparsed))
                                            {
                                                RazerDeviceHelper.HIDP_CAPS caps;
                                                RazerDeviceHelper.HidP_GetCaps(preparsed, out caps);
                                                RazerDeviceHelper.HidD_FreePreparsedData(preparsed);

                                                if (caps.UsagePage == 0xFF02 && caps.Usage == 0x0002 && caps.InputReportByteLength >= 17 && caps.OutputReportByteLength >= 17)
                                                {
                                                    IntPtr hEvent = CreateEvent(IntPtr.Zero, true, false, null);
                                                    if (hEvent != IntPtr.Zero)
                                                    {
                                                        try
                                                        {
                                                            Random rnd = new Random();
                                                            byte[] randBytes = new byte[8] { (byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256), 0, 0, 0, 0 };
                                                            SendAndReceive(handle, hEvent, BuildReport17(1, randBytes), caps.InputReportByteLength, 300);
                                                            SendAndReceive(handle, hEvent, BuildReport17(2, new byte[1] { 1 }), caps.InputReportByteLength, 200);
                                                            SendAndReceive(handle, hEvent, BuildReport17(3, null), caps.InputReportByteLength, 300);

                                                            byte[] tx = BuildConfigReport(cmd, address, data);
                                                            byte[] rx = SendAndReceive(handle, hEvent, tx, caps.InputReportByteLength, 300);
                                                            return rx != null;
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
                                Marshal.FreeHGlobal(detailBuffer);
                            }
                        }
                    }
                    finally
                    {
                        RazerDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);
                    }
                }
                catch { }
            }
            return false;
        }

        // 打开 FF02 通道 → 握手一次 → 循环写多个配置（addresses[i] 写 datas[i]），避免每档都重新打开+握手
        // 返回全部写成功的布尔数组；任一项失败不影响其余项
        private static bool[] ExecuteVgnWriteBatch(int[] addresses, byte[][] datas)
        {
            bool[] results = new bool[addresses.Length];
            lock (hidLock)
            {
                try
                {
                    Guid hidGuid;
                    RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
                    IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
                    if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return results;

                    RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                    uint memberIdx = 0;

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
                                if (devicePath != null && devicePath.ToLower().Contains("vid_3554"))
                                {
                                    IntPtr handle = RazerDeviceHelper.CreateFile(devicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                                    if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                    {
                                        try
                                        {
                                            IntPtr preparsed;
                                            if (RazerDeviceHelper.HidD_GetPreparsedData(handle, out preparsed))
                                            {
                                                RazerDeviceHelper.HIDP_CAPS caps;
                                                RazerDeviceHelper.HidP_GetCaps(preparsed, out caps);
                                                RazerDeviceHelper.HidD_FreePreparsedData(preparsed);

                                                if (caps.UsagePage == 0xFF02 && caps.Usage == 0x0002 && caps.InputReportByteLength >= 17 && caps.OutputReportByteLength >= 17)
                                                {
                                                    IntPtr hEvent = CreateEvent(IntPtr.Zero, true, false, null);
                                                    if (hEvent != IntPtr.Zero)
                                                    {
                                                        try
                                                        {
                                                            // 一次性握手
                                                            Random rnd = new Random();
                                                            byte[] randBytes = new byte[8] { (byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256), 0, 0, 0, 0 };
                                                            SendAndReceive(handle, hEvent, BuildReport17(1, randBytes), caps.InputReportByteLength, 300);
                                                            SendAndReceive(handle, hEvent, BuildReport17(2, new byte[1] { 1 }), caps.InputReportByteLength, 200);
                                                            SendAndReceive(handle, hEvent, BuildReport17(3, null), caps.InputReportByteLength, 300);

                                                            // 循环写多项
                                                            for (int i = 0; i < addresses.Length; i++)
                                                            {
                                                                byte[] tx = BuildConfigReport(0x07, addresses[i], datas[i]);
                                                                byte[] rx = SendAndReceive(handle, hEvent, tx, caps.InputReportByteLength, 300);
                                                                results[i] = (rx != null);
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
                                Marshal.FreeHGlobal(detailBuffer);
                            }
                        }
                    }
                    finally
                    {
                        RazerDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);
                    }
                }
                catch { }
            }
            return results;
        }

        public static bool SetVgnDpiStage(int targetStage)
        {
            if (targetStage < 1) return false;
            byte index = (byte)(targetStage - 1);
            return ExecuteVgnWriteBytes(0x04, new byte[] { index, (byte)((0x55 - index) & 0xFF) });
        }

        // 设置指定档位的 DPI 值（dpi 50-26000，50 步进；stageIndex 为 0-based 档位序号）
        public static bool SetVgnDpiValue(int dpi, int stageIndex)
        {
            if (dpi < 50 || dpi > 26000) return false;
            if (stageIndex < 0 || stageIndex > 7) return false;
            int enc = dpi / 50 - 1;
            byte low = (byte)(enc & 0xFF);
            byte high = (byte)((enc >> 8) & 0x03);
            byte flags = (byte)((high << 2) | (high << 6));
            byte ck = (byte)((0x55 - low - low - flags) & 0xFF);
            // 官方 DPI 落盘命令为 0x08（0x07 仅是拖拽预览，不持久化）
            return ExecuteVgnWriteBytes(0x0c + stageIndex * 4, new byte[] { low, low, flags, ck }, 0x08);
        }

        // 回报率编码（CompX 方案地址 0x00）：125→8, 250→4, 500→2, 1000→1, 2000→16, 4000→32（皂品 Z2 4K 版上限 4000Hz）
        private static byte EncodeVgnPollingRate(int hz)
        {
            switch (hz)
            {
                case 125: return 8;
                case 250: return 4;
                case 500: return 2;
                case 1000: return 1;
                case 2000: return 16;
                case 4000: return 32;
                default: return 0;
            }
        }

        // 切换回报率（hz 仅支持 125/250/500/1000/2000/4000，上限 4000Hz）
        public static bool SetVgnPollingRate(int hz)
        {
            byte code = EncodeVgnPollingRate(hz);
            if (code == 0) return false;
            return ExecuteVgnWriteBytes(0x00, new byte[] { code, (byte)((0x55 - code) & 0xFF) });
        }

        // 极速读取当前 DPI 档位（返回 1-based 档位序号；失败返回 -1）
        // 供主界面后台监听调用：实体 DPI 键切换后，读地址 0x04 同步档位
        public static int FastQueryVgnDpiStage()
        {
            lock (hidLock)
            {
                try
                {
                    Guid hidGuid;
                    RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
                    IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
                    if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return -1;

                    RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                    uint memberIdx = 0;

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
                                if (devicePath != null && devicePath.ToLower().Contains("vid_3554"))
                                {
                                    IntPtr handle = RazerDeviceHelper.CreateFile(devicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                                    if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                    {
                                        try
                                        {
                                            IntPtr preparsed;
                                            if (RazerDeviceHelper.HidD_GetPreparsedData(handle, out preparsed))
                                            {
                                                RazerDeviceHelper.HIDP_CAPS caps;
                                                RazerDeviceHelper.HidP_GetCaps(preparsed, out caps);
                                                RazerDeviceHelper.HidD_FreePreparsedData(preparsed);

                                                if (caps.UsagePage == 0xFF02 && caps.Usage == 0x0002 && caps.InputReportByteLength >= 17 && caps.OutputReportByteLength >= 17)
                                                {
                                                    IntPtr hEvent = CreateEvent(IntPtr.Zero, true, false, null);
                                                    if (hEvent != IntPtr.Zero)
                                                    {
                                                        try
                                                        {
                                                            Random rnd = new Random();
                                                            byte[] randBytes = new byte[8] { (byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256), 0, 0, 0, 0 };
                                                            SendAndReceive(handle, hEvent, BuildReport17(1, randBytes), caps.InputReportByteLength, 300);
                                                            SendAndReceive(handle, hEvent, BuildReport17(2, new byte[1] { 1 }), caps.InputReportByteLength, 200);
                                                            SendAndReceive(handle, hEvent, BuildReport17(3, null), caps.InputReportByteLength, 300);

                                                            // 读当前档位（地址 0x04，读 2 字节：value + 奇偶校验 0x55-value）；校验响应命令号 rx[1]==0x08，最多重试 3 次
                                                            for (int retry = 0; retry < 3; retry++)
                                                            {
                                                                byte[] tx = BuildConfigReport(0x08, 0x04, new byte[2]);
                                                                byte[] rx = SendAndReceive(handle, hEvent, tx, caps.InputReportByteLength, 300);
                                                                if (rx != null && rx.Length > 7 && rx[1] == 0x08 && rx[5] == 2 && ((rx[6] + rx[7]) & 0xFF) == 0x55)
                                                                {
                                                                    return rx[6] + 1; // 转 1-based 档位序号
                                                                }
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
                                Marshal.FreeHGlobal(detailBuffer);
                            }
                        }
                    }
                    finally
                    {
                        RazerDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);
                    }
                }
                catch { }
            }
            return -1;
        }

        // 批量写入全部 DPI 档位并切换到激活档（activeStage 为 1-based）
        // 优化：一次打开+握手，循环写全部档位与激活档，避免每档重新握手
        public static bool UpdateVgnHardwareStages(int activeStage, int[] stages)
        {
            if (stages == null || stages.Length == 0) return false;

            int writeCount = stages.Length;
            if (activeStage >= 1) writeCount += 1;

            int[] addrs = new int[writeCount];
            byte[][] datas = new byte[writeCount][];
            int idx = 0;
            for (int i = 0; i < stages.Length && i < 8; i++)
            {
                int dpi = stages[i];
                if (dpi < 50 || dpi > 26000) dpi = 800;
                int enc = dpi / 50 - 1;
                byte low = (byte)(enc & 0xFF);
                byte high = (byte)((enc >> 8) & 0x03);
                byte flags = (byte)((high << 2) | (high << 6));
                byte ck = (byte)((0x55 - low - low - flags) & 0xFF);
                addrs[idx] = 0x0c + i * 4;
                datas[idx] = new byte[] { low, low, flags, ck };
                idx++;
            }
            if (activeStage >= 1)
            {
                byte stageIndex = (byte)(activeStage - 1);
                addrs[idx] = 0x04;
                datas[idx] = new byte[] { stageIndex, (byte)((0x55 - stageIndex) & 0xFF) };
            }

            bool[] results = ExecuteVgnWriteBatch(addrs, datas);
            bool allOk = true;
            for (int i = 0; i < results.Length; i++)
            {
                if (!results[i]) allOk = false;
            }
            return allOk;
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
                int foundStage = -1;
                int foundVoltageMv = -1;
                // ⚠️ 数组容量修复：下方取值循环上界为 st < 8，且硬件可回报档位数上限亦为 8，
                //    原本此处只分配 6 个槽位（索引 0..5），当设备回报 7 或 8 档时
                //    readStages[6] / readStages[7] 直接越界抛出 IndexOutOfRangeException，
                //    导致整台鼠标完全无法识别（玄熊猫 P1 4K 实测即命中此路径）。
                //    扩容至 8 槽，与 stageCount 上限严格对齐。
                int[] readStages = new int[] { -1, -1, -1, -1, -1, -1, -1, -1 };
                int foundPollingRate = -1;
                int foundStageCount = -1;

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
                            if (devicePath != null && (devicePath.ToLower().Contains("vid_3554") || devicePath.ToLower().Contains("vgn")))
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
                                        if (string.IsNullOrEmpty(foundName))
                                        {
                                            StringBuilder sb = new StringBuilder(256);
                                            if (RazerDeviceHelper.HidD_GetProductString(handle, sb, sb.Capacity))
                                            {
                                                string pStr = sb.ToString().Trim();
                                                if (!string.IsNullOrEmpty(pStr)) foundName = pStr;
                                            }
                                        }

                                        RazerDeviceHelper.HIDD_ATTRIBUTES attr = new RazerDeviceHelper.HIDD_ATTRIBUTES();
                                        attr.Size = Marshal.SizeOf(attr);
                                        if (RazerDeviceHelper.HidD_GetAttributes(handle, ref attr))
                                        {
                                            if (attr.ProductID != 0) foundPid = attr.ProductID;
                                        }

                                        IntPtr preparsed;
                                        if (RazerDeviceHelper.HidD_GetPreparsedData(handle, out preparsed))
                                        {
                                            RazerDeviceHelper.HIDP_CAPS caps;
                                            RazerDeviceHelper.HidP_GetCaps(preparsed, out caps);
                                            RazerDeviceHelper.HidD_FreePreparsedData(preparsed);

                                            // Target communication endpoint: UsagePage == 0xFF02 && Usage == 0x0002
                                            if (caps.UsagePage == 0xFF02 && caps.Usage == 0x0002 && caps.InputReportByteLength >= 17 && caps.OutputReportByteLength >= 17)
                                            {
                                                foundVgnDongle = true;
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

                                                            // 4. Query Battery (Command 4)：校验响应命令号 rx[1]==0x04，防止读到握手/在线探测的错位残留响应
                                                            byte[] rxBat = null;
                                                            for (int bRetry = 0; bRetry < 3; bRetry++)
                                                            {
                                                                rxBat = SendAndReceive(handle, hEvent, BuildReport17(4, null), caps.InputReportByteLength, 400);
                                                                if (rxBat != null && rxBat.Length > 8 && rxBat[1] == 0x04)
                                                                {
                                                                    break;
                                                                }
                                                                rxBat = null;
                                                            }
                                                            // ⚠️ 边界修复：下方会访问 rxBat[9]（电压高字节），要求 Length >= 10，
                                                            //    原判定 Length > 8 仅保证 >= 9，短包时会越界崩溃。
                                                            if (rxBat != null && rxBat.Length > 9)
                                                            {
                                                                int bVal = rxBat[6];
                                                                if (bVal >= 0 && bVal <= 100)
                                                                {
                                                                    foundBattery = bVal;
                                                                    foundCharging = (rxBat[7] == 1);
                                                                    // bytes[8..9] = 电池电压（mV，大端），如 10 61 → 4193mV
                                                                    int vMv = (rxBat[8] << 8) | rxBat[9];
                                                                    if (vMv >= 3000 && vMv <= 4500)
                                                                    {
                                                                        foundVoltageMv = vMv;
                                                                    }
                                                                }
                                                            }

                                                            // 5. Query Current DPI Stage (地址 0x04，读 2 字节 value + 奇偶校验 0x55-value)：校验响应命令号 rx[1]==0x08
                                                            byte[] rxStage = null;
                                                            for (int sRetry = 0; sRetry < 3; sRetry++)
                                                            {
                                                                rxStage = SendAndReceive(handle, hEvent, BuildConfigReport(0x08, 0x04, new byte[2]), caps.InputReportByteLength, 300);
                                                                if (rxStage != null && rxStage.Length > 7 && rxStage[1] == 0x08)
                                                                {
                                                                    break;
                                                                }
                                                                rxStage = null;
                                                            }
                                                            if (rxStage != null && rxStage.Length > 7 && rxStage[5] == 2 && ((rxStage[6] + rxStage[7]) & 0xFF) == 0x55)
                                                            {
                                                                foundStage = rxStage[6] + 1; // 转 1-based 档位序号
                                                            }

                                                            // 5.5 读取 DPI 档位数（地址 0x02，value + 奇偶校验 0x55-value），决定实际档位数量
                                                            byte[] rxCnt = null;
                                                            for (int cRetry = 0; cRetry < 2; cRetry++)
                                                            {
                                                                rxCnt = SendAndReceive(handle, hEvent, BuildConfigReport(0x08, 0x02, new byte[2]), caps.InputReportByteLength, 300);
                                                                if (rxCnt != null && rxCnt.Length > 7 && rxCnt[1] == 0x08)
                                                                {
                                                                    break;
                                                                }
                                                                rxCnt = null;
                                                            }
                                                            if (rxCnt != null && rxCnt.Length > 7 && rxCnt[5] == 2 && ((rxCnt[6] + rxCnt[7]) & 0xFF) == 0x55 && rxCnt[6] >= 1 && rxCnt[6] <= 8)
                                                            {
                                                                foundStageCount = rxCnt[6];
                                                            }

                                                            // 6. 读取各档位的真实 DPI 值（地址 0x0c 起每档 4 字节 [low, low, flags, ck]），档位数取自硬件而非硬编码
                                                            int hwStageCount = (foundStageCount > 0) ? foundStageCount : 6;
                                                            // 上界绑定数组容量而非硬编码 8，杜绝二者再次脱节
                                                            for (int st = 0; st < hwStageCount && st < readStages.Length; st++)
                                                            {
                                                                byte[] rxSt = null;
                                                                for (int stRetry = 0; stRetry < 2; stRetry++)
                                                                {
                                                                    rxSt = SendAndReceive(handle, hEvent, BuildConfigReport(0x08, 0x0c + st * 4, new byte[4]), caps.InputReportByteLength, 300);
                                                                    if (rxSt != null && rxSt.Length > 9 && rxSt[1] == 0x08)
                                                                    {
                                                                        break;
                                                                    }
                                                                    rxSt = null;
                                                                }
                                                                if (rxSt != null && rxSt.Length > 9 && rxSt[5] == 4 && ((rxSt[6] + rxSt[7] + rxSt[8] + rxSt[9]) & 0xFF) == 0x55)
                                                                {
                                                                    int low = rxSt[6];
                                                                    int high = ((rxSt[8] >> 2) & 0x03); // flags=(high<<2)|(high<<6)
                                                                    int enc = low | (high << 8);
                                                                    int dpiVal = (enc + 1) * 50;
                                                                    if (dpiVal >= 50 && dpiVal <= 26000)
                                                                    {
                                                                        readStages[st] = dpiVal;
                                                                    }
                                                                }
                                                            }

                                                            // 7. 读取当前回报率（地址 0x00，2 字节 value + 奇偶校验 0x55-value）
                                                            //    编码与写入一致：8=125/4=250/2=500/1=1000/16=2000/32=4000
                                                            byte[] rxRate = null;
                                                            for (int rRetry = 0; rRetry < 2; rRetry++)
                                                            {
                                                                rxRate = SendAndReceive(handle, hEvent, BuildConfigReport(0x08, 0x00, new byte[2]), caps.InputReportByteLength, 300);
                                                                if (rxRate != null && rxRate.Length > 7 && rxRate[1] == 0x08)
                                                                {
                                                                    break;
                                                                }
                                                                rxRate = null;
                                                            }
                                                            if (rxRate != null && rxRate.Length > 7 && rxRate[5] == 2 && ((rxRate[6] + rxRate[7]) & 0xFF) == 0x55)
                                                            {
                                                                switch (rxRate[6])
                                                                {
                                                                    case 8: foundPollingRate = 125; break;
                                                                    case 4: foundPollingRate = 250; break;
                                                                    case 2: foundPollingRate = 500; break;
                                                                    case 1: foundPollingRate = 1000; break;
                                                                    case 16: foundPollingRate = 2000; break;
                                                                    case 32: foundPollingRate = 4000; break;
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
                        if (foundVoltageMv > 0) CachedBatteryVoltageMv = foundVoltageMv;
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

                    // 实测官方驱动显示的是固件电量计（库仑计）值，而非电压换算值：
                    // 同一时刻固件 70%、官方 70%，而电压换算为 73%。故主显示统一采用固件电量计，
                    // 电压仅作参考在悬停提示中展示。
                    int displayVoltageMv = (foundVoltageMv > 0) ? foundVoltageMv : CachedBatteryVoltageMv;

                    // 品牌与能力动态派生：皂品 Z2 (cid=7 / pid 0xF526 / 0xF527，接收器多 PID 变体) 与 VGN 蜻蜓同属 CompX 方案
                    bool isZaopin = (detectedCid == 7) || (foundPid == 0xF526) || (foundPid == 0xF527);
                    string brandName = isZaopin ? "Zaopin" : "VGN";
                    int[] defaultStages = isZaopin
                        ? new int[] { 400, 800, 1600, 2400, 3200, 6400 }
                        : new int[] { 400, 800, 1600, 3200, 6400 };

                    // 优先使用从硬件读回的真实档位值（用户可能自定义修改过），读不到的档位回退默认值
                    int stageCount = (foundStageCount >= 1 && foundStageCount <= 8) ? foundStageCount : (isZaopin ? 6 : 5);
                    int[] dpiStages = new int[stageCount];
                    int readOkCount = 0;
                    for (int i = 0; i < stageCount; i++)
                    {
                        if (i < readStages.Length && readStages[i] >= 50 && readStages[i] <= 26000)
                        {
                            dpiStages[i] = readStages[i];
                            readOkCount++;
                        }
                        else if (i < defaultStages.Length)
                        {
                            dpiStages[i] = defaultStages[i];
                        }
                        else
                        {
                            dpiStages[i] = 800;
                        }
                    }
                    if (readOkCount == 0)
                    {
                        // 全部读取失败时整体回退默认档位
                        dpiStages = defaultStages;
                    }

                    // 当前 DPI = 激活档位的真实值
                    int currentDpi = (foundStage >= 1 && foundStage <= dpiStages.Length) ? dpiStages[foundStage - 1] : 1600;

                    int[] pollRates = isZaopin
                        ? new int[] { 125, 250, 500, 1000, 2000, 4000 }
                        : GetSupportedPollingRatesForModel(foundPid, devName);

                    // 连接形态判断：皂品 Z2 有线直连时必然在充电（foundCharging=true），2.4G 无线时不充电
                    string transportText = displayCharging ? "有线连接" : "2.4G 无线";

                    // 悬停提示：皂品 Z2 的固件电量计在中间区间会长时间停滞（实测 50% 卡住 100 分钟），
                    // 官方驱动用的是电压法，故放电时主显示与官方对齐用电压法，库仑计仅作参考
                    // 官方驱动查表前会先扣掉约 12mV 的负载压降补偿（实测：3858mV 官方显示 46%，
                    // 而 3858mV 直接查表为 47%；3858-12=3846 查表正好 46%，3863 同理）
                    int calibratedMv = displayVoltageMv > 0 ? Math.Max(0, displayVoltageMv - 12) : displayVoltageMv;
                    int vPct = displayVoltageMv > 0 ? VoltageToPercent(calibratedMv) : -1;
                    bool useVoltageAsMain = isZaopin && !displayCharging && vPct >= 0 && vPct <= 100;
                    int coulombPct = displayBattery;

                    string batteryTooltip = null;
                    if (displayVoltageMv > 0)
                    {
                        if (useVoltageAsMain)
                        {
                            displayBattery = vPct;
                            batteryTooltip = string.Format("电量: {0}% (电压法，与官方驱动一致)\r\n固件电量计: {1}% (中间区间会滞后)\r\n电池电压: {2}.{3:D3}V", displayBattery, coulombPct >= 0 ? coulombPct.ToString() : "--", displayVoltageMv / 1000, displayVoltageMv % 1000);
                        }
                        else if (foundBattery >= 0)
                        {
                            batteryTooltip = string.Format("电量: {0}% (固件电量计)\r\n电池电压: {1}.{2:D3}V · 电压换算参考 {3}%", displayBattery, displayVoltageMv / 1000, displayVoltageMv % 1000, vPct);
                        }
                        else
                        {
                            batteryTooltip = string.Format("电池电压: {0}.{1:D3}V (换算参考 {2}%)", displayVoltageMv / 1000, displayVoltageMv % 1000, vPct);
                        }
                    }

                    return new MouseBatteryInfo
                    {
                        DeviceId = brandName + ":3554:" + (foundPid > 0 ? foundPid.ToString("X4") : "3554"),
                        Brand = brandName,
                        DeviceName = devName,
                        Category = DeviceCategory.Mouse,
                        Transport = transportText,
                        IsConnected = isMouseOnline,
                        IsSleeping = !isMouseOnline,
                        IsDonglePresent = true,
                        BatteryPercent = displayBattery,
                        ExactBatteryPercent = displayBattery >= 0 ? displayBattery : 0,
                        IsCharging = displayCharging,
                        BatteryVoltageMv = displayVoltageMv,
                        CustomTooltip = batteryTooltip,
                        LastUpdated = (foundBattery >= 0) ? DateTime.Now : (CachedLastUpdated != DateTime.MinValue ? CachedLastUpdated : DateTime.Now),
                        Dpi = currentDpi,
                        DpiStage = (foundStage >= 1 && foundStage <= dpiStages.Length) ? foundStage : 2,
                        DpiStageCount = dpiStages.Length,
                        DpiStages = dpiStages,
                        PollingRate = (foundPollingRate > 0) ? foundPollingRate : 1000,
                        MaxPollingRate = isZaopin ? 4000 : 8000,
                        SupportedPollingRates = pollRates
                    };
                }

                return null;
            }
        }
    }
}