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

        private static byte[] SendAndReceive(IntPtr handle, IntPtr hEvent, byte[] tx, int inLen, int timeoutMs)
        {
            if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return null;

            // 1. Drain pending reports
            byte[] drain = new byte[inLen];
            while (true)
            {
                OVERLAPPED dOl = new OVERLAPPED();
                dOl.hEvent = hEvent;
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

        private static DateTime yjxStageFixTime = DateTime.MinValue;

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
                // 其余是 0xFA 通知帧，跳过继续等目标命令
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
            switch (hz)
            {
                case 125: return 1;
                case 250: return 2;
                case 500: return 3;
                case 1000: return 4;
                case 2000: return 5;
                case 4000: return 6;
                case 8000: return 7;
                default: return 0;
            }
        }

        private static YjxSnapshot QueryYjx(IntPtr handle, IntPtr hEvent, int inLen)
        {
            YjxSnapshot snap = new YjxSnapshot();
            byte[] online = YjxSendExpect(handle, hEvent, BuildYjxCmd(YjxCmdMouseOnline), inLen, 400, YjxCmdMouseOnline);
            if (online != null && online.Length > 9)
            {
                // receivedData[0] == 0 或 1 表示休眠/离线
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
                // 硬件 profile 出厂带第 6 档 DPI；本项目 UI 只维护 5 档，
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

        /// <summary>写入 Yjx profile：rateHz 大于 0 改回报率；stage 大于 0 切档，dpiValue 大于 0 同时改写该档 DPI。</summary>
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

        // 是否为 Yjx 通道设备（VID 391D，如 Dragonfly 3 Pro）。
        public static bool IsYjxDevice(string deviceId)
        {
            if (deviceId == null) return false;
            return deviceId.StartsWith("VGN:391D:", StringComparison.OrdinalIgnoreCase)
                || deviceId.StartsWith("HID:391D:", StringComparison.OrdinalIgnoreCase);
        }

        public static string GetModelName(int pid, string productString, int cid, int mid)
        {
            if (cid == 118 && mid == 11) return "VGN Dragonfly F2 Pro Max";
            if (cid == 118 && mid == 10) return "VGN Dragonfly F2 Pro";
            if (cid == 118 && mid == 9) return "VGN Dragonfly F2";
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
                case 0x1A03: return "VGN Dragonfly 3 Pro";
                default: return "VGN 无线游戏鼠标";
            }
        }

        public static int[] GetSupportedPollingRatesForModel(int pid, string modelName)
        {
            string name = !string.IsNullOrEmpty(modelName) ? modelName.ToLowerInvariant() : "";
            if (pid == 0xFC05 || pid == 0xFB24 || pid == 0x1A03 || name.Contains(" r1 se") || (name.Contains("dragonfly f1") && !name.Contains("pro") && !name.Contains("moba")))
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
                bool foundYjx = false;
                YjxSnapshot yjxSnap = null;

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
                            if (devicePath != null && (devicePath.ToLower().Contains("vid_3554") || devicePath.ToLower().Contains("vid_391d") || devicePath.ToLower().Contains("vgn")))
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

                                            // ---- Yjx 端点: UsagePage 0xFF01 / Usage 0x0010 / 65 字节 ----
                                            if (caps.UsagePage == 0xFF01 && caps.Usage == 0x0010
                                                && caps.InputReportByteLength >= YjxReportLen
                                                && caps.OutputReportByteLength >= YjxReportLen)
                                            {
                                                foundVgnDongle = true;
                                                foundYjx = true;
                                                IntPtr yxEvent = CreateEvent(IntPtr.Zero, true, false, null);
                                                if (yxEvent != IntPtr.Zero)
                                                {
                                                    try
                                                    {
                                                        yjxSnap = QueryYjx(handle, yxEvent, caps.InputReportByteLength);
                                                    }
                                                    finally
                                                    {
                                                        RazerDeviceHelper.CloseHandle(yxEvent);
                                                    }
                                                }
                                            }
                                            // ---- 旧 ATK 通道: UsagePage == 0xFF02 && Usage == 0x0002 ----
                                            else if (caps.UsagePage == 0xFF02 && caps.Usage == 0x0002 && caps.InputReportByteLength >= 17 && caps.OutputReportByteLength >= 17)
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

                                                            // 4. Query Battery (Command 4)
                                                            byte[] rxBat = SendAndReceive(handle, hEvent, BuildReport17(4, null), caps.InputReportByteLength, 400);
                                                            if (rxBat != null && rxBat.Length > 8)
                                                            {
                                                                int bVal = rxBat[6];
                                                                if (bVal >= 0 && bVal <= 100)
                                                                {
                                                                    foundBattery = bVal;
                                                                    foundCharging = (rxBat[7] == 1);
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

                if (foundYjx && yjxSnap != null)
                {
                    if (yjxSnap.Battery >= 0)
                    {
                        CachedBatteryPercent = yjxSnap.Battery;
                        CachedIsCharging = yjxSnap.Charging;
                        CachedLastUpdated = DateTime.Now;
                        HasValidHardwareReading = true;
                        SaveHardwareCache();
                    }

                    string yName = (foundPid == 0x1A03) ? "VGN Dragonfly 3 Pro" : GetModelName(foundPid, foundName, 0, 0);
                    if (string.IsNullOrEmpty(yName)) yName = CachedDeviceName;
                    if (string.IsNullOrEmpty(yName)) yName = "VGN 无线游戏鼠标";
                    CachedDeviceName = yName;

                    int yBat = yjxSnap.Battery;
                    if (yBat < 0 && HasValidHardwareReading && CachedBatteryPercent > 0) yBat = CachedBatteryPercent;
                    bool yChg = (yjxSnap.Battery >= 0) ? yjxSnap.Charging : CachedIsCharging;

                    int[] yStages = yjxSnap.Stages;
                    if (yStages == null || yStages.Length == 0) yStages = new int[] { 400, 800, 1600, 3200, 6400 };
                    int yCount = yStages.Length;
                    int yStage = yjxSnap.CurrentStage;
                    if (yjxSnap.HardwareStage > yCount)
                    {
                        // 硬件 profile 出厂带第 6 档 DPI，本项目只维护 5 档。
                        // 若鼠标正停在该已下线档位，自动切回最高支持档，保证 UI 显示 DPI 与物理 DPI 一致。
                        if ((DateTime.Now - yjxStageFixTime).TotalSeconds >= 60)
                        {
                            yjxStageFixTime = DateTime.Now;
                            SetYjxDpiStage(yCount);
                        }
                        yStage = yCount;
                    }
                    if (yStage < 1 || yStage > yCount) yStage = 1;
                    int yDpi = yStages[yStage - 1];
                    if (yDpi < 50) yDpi = 800;

                    return new MouseBatteryInfo
                    {
                        DeviceId = "VGN:391D:" + (foundPid > 0 ? foundPid.ToString("X4") : "1A03"),
                        Brand = "VGN",
                        DeviceName = yName,
                        Category = DeviceCategory.Mouse,
                        Transport = "2.4G 无线",
                        IsConnected = yjxSnap.Online,
                        IsSleeping = !yjxSnap.Online,
                        IsDonglePresent = true,
                        BatteryPercent = yBat,
                        ExactBatteryPercent = yBat >= 0 ? yBat : 0,
                        IsCharging = yChg,
                        LastUpdated = (yjxSnap.Battery >= 0) ? DateTime.Now : (CachedLastUpdated != DateTime.MinValue ? CachedLastUpdated : DateTime.Now),
                        Dpi = yDpi,
                        DpiY = yDpi,
                        DpiStage = yStage,
                        DpiStageCount = yCount,
                        DpiStages = yStages,
                        DpiStagesY = yStages,
                        IsXyIndependent = false,
                        PollingRate = YjxRateCodeToHz(yjxSnap.RateCode),
                        SupportedPollingRates = GetSupportedPollingRatesForModel(foundPid, yName)
                    };
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