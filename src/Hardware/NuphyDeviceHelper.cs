// ============================================================================
// 模块名称: NuphyDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   NuPhy 矮轴机械键盘 (Air/Halo/Field/WH80) HID 电量与灯效控制实现
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
    public static class NuphyDeviceHelper
    {
        private static readonly object hidLock = new object();
        private const string CacheRegistryKey = @"Software\RazerBatteryTray\NuphyCache";

        public static int CachedBatteryPercent = -1;
        public static bool CachedIsCharging = false;
        public static string CachedDeviceName = "";
        public static DateTime CachedLastUpdated = DateTime.MinValue;
        public static string CachedCommandPath = null;

        public class NuphyHardwareSettings
        {
            public int CurrentMode; // internal slot index
            public int PollingRateCode; // 1=125, 2=250, 3=500, 4=1000
            public int SleepMinutes;
            public bool SleepEnabled;
            public bool WinLock;
            public bool BacklightEnabled;
            public int BacklightBrightness;
            public bool SidelightEnabled;
            public int SidelightBrightness;
            public byte RawRate;
            public byte RawSleep;
            public byte RawWin;
            public byte RawOpt;

            public NuphyHardwareSettings()
            {
                CurrentMode = 1;
                PollingRateCode = 4;
                SleepMinutes = 6;
                SleepEnabled = true;
                WinLock = false;
                BacklightEnabled = true;
                BacklightBrightness = 100;
                SidelightEnabled = true;
                SidelightBrightness = 100;
                RawRate = 0x01;
                RawSleep = 0x86;
                RawWin = 0x00;
                RawOpt = 0x04;
            }
        }

        public static NuphyHardwareSettings CachedHardwareSettings = new NuphyHardwareSettings();

        public static void LoadHardwareCache()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(CacheRegistryKey, false))
                {
                    if (key != null)
                    {
                        var bp = key.GetValue("BatteryPercent");
                        if (bp != null) CachedBatteryPercent = Convert.ToInt32(bp);

                        var chg = key.GetValue("IsCharging");
                        if (chg != null) CachedIsCharging = Convert.ToInt32(chg) != 0;

                        var dn = key.GetValue("DeviceName") as string;
                        if (!string.IsNullOrEmpty(dn)) CachedDeviceName = dn;

                        var updatedStr = key.GetValue("LastUpdated") as string;
                        if (!string.IsNullOrEmpty(updatedStr))
                        {
                            DateTime dt;
                            if (DateTime.TryParse(updatedStr, out dt)) CachedLastUpdated = dt;
                        }

                        var sMode = key.GetValue("CurrentMode");
                        if (sMode != null) CachedHardwareSettings.CurrentMode = Convert.ToInt32(sMode);

                        var sRate = key.GetValue("PollingRateCode");
                        if (sRate != null) CachedHardwareSettings.PollingRateCode = Convert.ToInt32(sRate);

                        var sMin = key.GetValue("SleepMinutes");
                        if (sMin != null) CachedHardwareSettings.SleepMinutes = Convert.ToInt32(sMin);

                        var sEn = key.GetValue("SleepEnabled");
                        if (sEn != null) CachedHardwareSettings.SleepEnabled = Convert.ToInt32(sEn) != 0;

                        var sWin = key.GetValue("WinLock");
                        if (sWin != null) CachedHardwareSettings.WinLock = Convert.ToInt32(sWin) != 0;

                        var sBackEn = key.GetValue("BacklightEnabled");
                        if (sBackEn != null) CachedHardwareSettings.BacklightEnabled = Convert.ToInt32(sBackEn) != 0;

                        var sBackB = key.GetValue("BacklightBrightness");
                        if (sBackB != null) CachedHardwareSettings.BacklightBrightness = Convert.ToInt32(sBackB);

                        var sSideEn = key.GetValue("SidelightEnabled");
                        if (sSideEn != null) CachedHardwareSettings.SidelightEnabled = Convert.ToInt32(sSideEn) != 0;

                        var sSideB = key.GetValue("SidelightBrightness");
                        if (sSideB != null) CachedHardwareSettings.SidelightBrightness = Convert.ToInt32(sSideB);
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/NuphyDeviceHelper.cs: 已忽略异常", diagEx); }
        }

        public static void SaveHardwareCache()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(CacheRegistryKey))
                {
                    if (key != null)
                    {
                        if (CachedBatteryPercent > 0) key.SetValue("BatteryPercent", CachedBatteryPercent);
                        key.SetValue("IsCharging", CachedIsCharging ? 1 : 0);
                        if (!string.IsNullOrEmpty(CachedDeviceName)) key.SetValue("DeviceName", CachedDeviceName);
                        if (CachedLastUpdated != DateTime.MinValue) key.SetValue("LastUpdated", CachedLastUpdated.ToString("o"));

                        if (CachedHardwareSettings != null)
                        {
                            key.SetValue("CurrentMode", CachedHardwareSettings.CurrentMode);
                            key.SetValue("PollingRateCode", CachedHardwareSettings.PollingRateCode);
                            key.SetValue("SleepMinutes", CachedHardwareSettings.SleepMinutes);
                            key.SetValue("SleepEnabled", CachedHardwareSettings.SleepEnabled ? 1 : 0);
                            key.SetValue("WinLock", CachedHardwareSettings.WinLock ? 1 : 0);
                            key.SetValue("BacklightEnabled", CachedHardwareSettings.BacklightEnabled ? 1 : 0);
                            key.SetValue("BacklightBrightness", CachedHardwareSettings.BacklightBrightness);
                            key.SetValue("SidelightEnabled", CachedHardwareSettings.SidelightEnabled ? 1 : 0);
                            key.SetValue("SidelightBrightness", CachedHardwareSettings.SidelightBrightness);
                        }
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/NuphyDeviceHelper.cs: 已忽略异常", diagEx); }
        }

        public static bool IsNuphyDevice(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return false;
            string idLower = identifier.ToLowerInvariant();
            return idLower.Contains("19f5") || idLower.Contains("nuphy") || idLower.Contains("wh80");
        }

        public static string GetModelName(int pid, string productString)
        {
            if (!string.IsNullOrEmpty(productString))
            {
                string cleaned = productString.Replace("Dongle", "").Trim();
                if (!string.IsNullOrEmpty(cleaned)) return cleaned;
            }
            switch (pid)
            {
                case 0x8F01: return "NuPhy WH80";
                case 0xA011: return "NuPhy WH80";
                case 0x8F02: return "NuPhy Air75 V2";
                case 0x8F03: return "NuPhy Air96 V2";
                case 0x8F04: return "NuPhy Air60 V2";
                case 0x8F05: return "NuPhy Halo75 V2";
                case 0x8F06: return "NuPhy Halo96 V2";
                case 0x8F07: return "NuPhy Field75";
                default: return "NuPhy 无线键盘";
            }
        }

        public static byte[] SendNuphyCommand(string path, byte cmd, ushort offset, byte len, byte[] payload, int waitMs)
        {
            if (string.IsNullOrEmpty(path)) return null;

            lock (hidLock)
            {
                const uint GENERIC_READ = 0x80000000;
                const uint GENERIC_WRITE = 0x40000000;
                const uint FILE_FLAG_OVERLAPPED = 0x40000000;

                IntPtr devHandle = RazerDeviceHelper.CreateFile(
                    path,
                    GENERIC_READ | GENERIC_WRITE,
                    RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    RazerDeviceHelper.OPEN_EXISTING,
                    FILE_FLAG_OVERLAPPED,
                    IntPtr.Zero);

                if (devHandle == IntPtr.Zero || devHandle.ToInt64() == -1) return null;

                try
                {
                    using (SafeFileHandle sfh = new SafeFileHandle(devHandle, true))
                    using (FileStream fs = new FileStream(sfh, FileAccess.ReadWrite, 65, true))
                    {
                        byte[] pkt = new byte[65];
                        pkt[0] = 0x00;
                        pkt[1] = 0x55;
                        pkt[2] = cmd;
                        pkt[3] = 0x00;
                        pkt[5] = len;
                        pkt[6] = (byte)(offset & 0xFF);
                        pkt[7] = (byte)((offset >> 8) & 0xFF);
                        pkt[8] = 0x00;

                        if (payload != null)
                        {
                            for (int i = 0; i < payload.Length && (9 + i) < 65; i++)
                            {
                                pkt[9 + i] = payload[i];
                            }
                        }

                        byte sum = 0;
                        for (int i = 5; i < 65; i++) sum = (byte)(sum + pkt[i]);
                        pkt[4] = sum;

                        if (waitMs <= 0)
                        {
                            fs.Write(pkt, 0, 65);
                            fs.Flush();
                            return new byte[65];
                        }

                        byte[] inBuf = new byte[65];
                        IAsyncResult ar = fs.BeginRead(inBuf, 0, 65, null, null);

                        fs.Write(pkt, 0, 65);
                        fs.Flush();

                        if (ar.AsyncWaitHandle.WaitOne(waitMs))
                        {
                            int bytesRead = fs.EndRead(ar);
                            if (bytesRead >= 10)
                            {
                                return inBuf;
                            }
                        }
                    }
                }
                catch (Exception diagEx) { Diagnostics.Log("Hardware/NuphyDeviceHelper.cs: 已忽略异常", diagEx); }
                return null;
            }
        }

        public static string FindNuphyCommandEndpoint()
        {
            if (!string.IsNullOrEmpty(CachedCommandPath)) return CachedCommandPath;

            lock (hidLock)
            {
                Guid hidGuid;
                RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
                IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
                if (devInfo == IntPtr.Zero || devInfo.ToInt64() == -1) return null;

                RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                uint memberIdx = 0;
                string candidateWired = null;
                string candidateDongle = null;

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
                            if (devicePath != null && (devicePath.ToLower().Contains("vid_19f5") || devicePath.ToLower().Contains("nuphy")))
                            {
                                IntPtr handle = RazerDeviceHelper.CreateFile(devicePath, 0, RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE, IntPtr.Zero, RazerDeviceHelper.OPEN_EXISTING, 0, IntPtr.Zero);
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

                                            if (caps.InputReportByteLength == 65 && caps.OutputReportByteLength == 65)
                                            {
                                                bool isWired = devicePath.IndexOf("pid_a011", StringComparison.OrdinalIgnoreCase) >= 0;
                                                bool isMi01 = devicePath.IndexOf("mi_01", StringComparison.OrdinalIgnoreCase) >= 0;
                                                if (isWired)
                                                {
                                                    if (candidateWired == null || isMi01) candidateWired = devicePath;
                                                }
                                                else
                                                {
                                                    if (candidateDongle == null || isMi01) candidateDongle = devicePath;
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

                string candidate = candidateWired ?? candidateDongle;
                if (candidate != null) CachedCommandPath = candidate;
                return candidate;
            }
        }

        public static bool GetHardwareSettings(out NuphyHardwareSettings settings)
        {
            settings = CachedHardwareSettings;
            try
            {
                string path = FindNuphyCommandEndpoint();
                if (string.IsNullOrEmpty(path)) return false;

                // 1. Query mode
                byte[] baseResp = SendNuphyCommand(path, 0x04, 0, 16, null, 300);
                int mode = (baseResp != null && baseResp.Length >= 10) ? baseResp[9] : (CachedHardwareSettings != null ? CachedHardwareSettings.CurrentMode : 1);
                if (mode < 0 || mode > 3) mode = 1;

                ushort modeOffset = (ushort)(mode * 64);
                byte[] fResp = SendNuphyCommand(path, 0x05, modeOffset, 54, null, 300);
                if (fResp != null && fResp.Length >= 65 && (fResp[1] == 0xAA || fResp[1] == 0x55))
                {
                    var s = new NuphyHardwareSettings();
                    s.CurrentMode = mode;

                    s.RawRate = fResp[9 + 4];
                    s.RawSleep = fResp[9 + 5];
                    s.RawWin = fResp[9 + 6];
                    s.RawOpt = fResp[9 + 7];

                    s.PollingRateCode = s.RawRate & 0x0F;
                    s.SleepEnabled = (s.RawSleep & 0x80) != 0 && fResp[9 + 39] == 0;
                    s.SleepMinutes = s.RawSleep & 0x7F;

                    // If in wired mode (mode == 0) or active mode returns 0 for RawSleep,
                    // query Mode 1 (2.4G wireless mode, offset 64) because sleep is managed on wireless profiles
                    if (mode == 0 || s.RawSleep == 0)
                    {
                        byte[] wResp = SendNuphyCommand(path, 0x05, 64, 54, null, 300);
                        if (wResp != null && wResp.Length >= 65 && (wResp[1] == 0xAA || wResp[1] == 0x55))
                        {
                            byte wSleep = wResp[9 + 5];
                            s.RawSleep = wSleep;
                            s.SleepEnabled = (wSleep & 0x80) != 0 && wResp[9 + 39] == 0;
                            s.SleepMinutes = wSleep & 0x7F;
                        }
                        else if (CachedHardwareSettings != null)
                        {
                            s.SleepEnabled = CachedHardwareSettings.SleepEnabled;
                            s.SleepMinutes = CachedHardwareSettings.SleepMinutes > 0 ? CachedHardwareSettings.SleepMinutes : 6;
                            s.RawSleep = CachedHardwareSettings.RawSleep != 0 ? CachedHardwareSettings.RawSleep : (byte)((s.SleepEnabled ? 0x80 : 0x00) | (s.SleepMinutes & 0x7F));
                        }
                    }

                    if (s.SleepMinutes == 0) s.SleepMinutes = 6;

                    s.WinLock = (s.RawWin & 0x01) != 0;

                    s.BacklightBrightness = fResp[9 + 9];
                    s.BacklightEnabled = (s.BacklightBrightness > 0);

                    s.SidelightBrightness = fResp[9 + 25];
                    s.SidelightEnabled = (s.SidelightBrightness > 0);

                    CachedHardwareSettings = s;
                    settings = s;
                    SaveHardwareCache();
                    return true;
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/NuphyDeviceHelper.cs: 已忽略异常", diagEx); }
            return false;
        }

        public static bool SetWinLock(bool lockWin)
        {
            try
            {
                string path = FindNuphyCommandEndpoint();
                if (string.IsNullOrEmpty(path)) return false;

                var s = CachedHardwareSettings ?? new NuphyHardwareSettings();
                s.WinLock = lockWin;

                byte newWin = (byte)((s.RawWin & ~0x01) | (lockWin ? 0x01 : 0x00));
                s.RawWin = newWin;

                bool anyOk = false;
                int[] modesToUpdate = new int[] { 0, 1, 2, 3 };
                for (int i = 0; i < modesToUpdate.Length; i++)
                {
                    int m = modesToUpdate[i];
                    ushort baseOffset = (ushort)(m * 64);
                    byte[] payload = new byte[] { s.RawRate, s.RawSleep, newWin, s.RawOpt };
                    byte[] resp = SendNuphyCommand(path, 0x06, (ushort)(baseOffset + 4), 4, payload, 50);
                    if (resp != null) anyOk = true;
                }

                CachedHardwareSettings = s;
                SaveHardwareCache();
                return anyOk;
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/NuphyDeviceHelper.cs: 已忽略异常", diagEx); }
            return false;
        }

        public static bool SetAutoSleep(bool enable)
        {
            try
            {
                string path = FindNuphyCommandEndpoint();
                if (string.IsNullOrEmpty(path)) return false;

                var s = CachedHardwareSettings ?? new NuphyHardwareSettings();
                s.SleepEnabled = enable;

                int mins = s.SleepMinutes > 0 ? s.SleepMinutes : 6;
                s.SleepMinutes = mins;
                byte newSleep = (byte)((enable ? 0x80 : 0x00) | (mins & 0x7F));
                s.RawSleep = newSleep;

                bool anyOk = false;
                // Update wireless profiles (Mode 1 = 2.4G, Mode 2 = BT1, Mode 3 = BT2) and wired (Mode 0)
                int[] modesToUpdate = new int[] { 1, 2, 3, 0 };
                for (int i = 0; i < modesToUpdate.Length; i++)
                {
                    int m = modesToUpdate[i];
                    ushort baseOffset = (ushort)(m * 64);
                    byte[] payload = new byte[] { s.RawRate, newSleep, s.RawWin, s.RawOpt };
                    byte[] resp1 = SendNuphyCommand(path, 0x06, (ushort)(baseOffset + 4), 4, payload, 50);
                    byte[] resp2 = SendNuphyCommand(path, 0x06, (ushort)(baseOffset + 39), 1, new byte[] { (byte)(enable ? 0 : 1) }, 50);
                    if (resp1 != null || resp2 != null) anyOk = true;
                }

                CachedHardwareSettings = s;
                SaveHardwareCache();
                return anyOk;
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/NuphyDeviceHelper.cs: 已忽略异常", diagEx); }
            return false;
        }

        public static bool SetBacklightBrightness(int percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            try
            {
                string path = FindNuphyCommandEndpoint();
                if (string.IsNullOrEmpty(path)) return false;

                var s = CachedHardwareSettings ?? new NuphyHardwareSettings();
                s.BacklightBrightness = percent;
                s.BacklightEnabled = (percent > 0);

                bool anyOk = false;
                int[] modesToUpdate = new int[] { 0, 1, 2, 3 };
                for (int i = 0; i < modesToUpdate.Length; i++)
                {
                    int m = modesToUpdate[i];
                    ushort baseOffset = (ushort)(m * 64);
                    byte[] resp = SendNuphyCommand(path, 0x06, (ushort)(baseOffset + 9), 1, new byte[] { (byte)percent }, 0);
                    if (resp != null) anyOk = true;
                }

                CachedHardwareSettings = s;
                SaveHardwareCache();
                return anyOk;
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/NuphyDeviceHelper.cs: 已忽略异常", diagEx); }
            return false;
        }

        public static bool SetBacklightEnabled(bool enable)
        {
            var s = CachedHardwareSettings ?? new NuphyHardwareSettings();
            int target = enable ? (s.BacklightBrightness > 0 ? s.BacklightBrightness : 100) : 0;
            return SetBacklightBrightness(target);
        }

        public static bool SetSidelightBrightness(int percent)
        {
            if (percent < 0) percent = 0;
            if (percent > 100) percent = 100;
            try
            {
                string path = FindNuphyCommandEndpoint();
                if (string.IsNullOrEmpty(path)) return false;

                var s = CachedHardwareSettings ?? new NuphyHardwareSettings();
                s.SidelightBrightness = percent;
                s.SidelightEnabled = (percent > 0);

                bool anyOk = false;
                int[] modesToUpdate = new int[] { 0, 1, 2, 3 };
                for (int i = 0; i < modesToUpdate.Length; i++)
                {
                    int m = modesToUpdate[i];
                    ushort baseOffset = (ushort)(m * 64);
                    byte[] resp = SendNuphyCommand(path, 0x06, (ushort)(baseOffset + 25), 1, new byte[] { (byte)percent }, 0);
                    if (resp != null) anyOk = true;
                }

                CachedHardwareSettings = s;
                SaveHardwareCache();
                return anyOk;
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/NuphyDeviceHelper.cs: 已忽略异常", diagEx); }
            return false;
        }

        public static bool SetSidelightEnabled(bool enable)
        {
            var s = CachedHardwareSettings ?? new NuphyHardwareSettings();
            int target = enable ? (s.SidelightBrightness > 0 ? s.SidelightBrightness : 100) : 0;
            return SetSidelightBrightness(target);
        }

        public static MouseBatteryInfo QueryNuphyDeviceInfo()
        {
            lock (hidLock)
            {
                Guid hidGuid;
                RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
                IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
                if (devInfo == IntPtr.Zero || devInfo.ToInt64() == -1) return null;

                RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                uint memberIdx = 0;
                string candidateWired = null;
                string candidateDongle = null;
                int wiredPid = 0;
                int donglePid = 0;
                string wiredName = null;
                string dongleName = null;
                bool foundAnyNuphy = false;

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
                            if (devicePath != null && (devicePath.ToLower().Contains("vid_19f5") || devicePath.ToLower().Contains("nuphy")))
                            {
                                foundAnyNuphy = true;
                                IntPtr handle = RazerDeviceHelper.CreateFile(devicePath, 0, RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE, IntPtr.Zero, RazerDeviceHelper.OPEN_EXISTING, 0, IntPtr.Zero);
                                if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                {
                                    try
                                    {
                                        string curName = null;
                                        StringBuilder sb = new StringBuilder(256);
                                        if (RazerDeviceHelper.HidD_GetProductString(handle, sb, sb.Capacity))
                                        {
                                            string pStr = sb.ToString().Trim();
                                            if (!string.IsNullOrEmpty(pStr)) curName = pStr;
                                        }

                                        int curPid = 0;
                                        RazerDeviceHelper.HIDD_ATTRIBUTES attr = new RazerDeviceHelper.HIDD_ATTRIBUTES();
                                        attr.Size = Marshal.SizeOf(attr);
                                        if (RazerDeviceHelper.HidD_GetAttributes(handle, ref attr))
                                        {
                                            curPid = attr.ProductID;
                                        }

                                        IntPtr preparsed;
                                        if (RazerDeviceHelper.HidD_GetPreparsedData(handle, out preparsed))
                                        {
                                            RazerDeviceHelper.HIDP_CAPS caps;
                                            RazerDeviceHelper.HidP_GetCaps(preparsed, out caps);
                                            RazerDeviceHelper.HidD_FreePreparsedData(preparsed);

                                            if (caps.InputReportByteLength == 65 && caps.OutputReportByteLength == 65)
                                            {
                                                bool isWired = devicePath.IndexOf("pid_a011", StringComparison.OrdinalIgnoreCase) >= 0 || curPid == 0xA011;
                                                bool isMi01 = devicePath.IndexOf("mi_01", StringComparison.OrdinalIgnoreCase) >= 0;

                                                if (isWired)
                                                {
                                                    if (candidateWired == null || isMi01)
                                                    {
                                                        candidateWired = devicePath;
                                                        wiredPid = curPid != 0 ? curPid : 0xA011;
                                                        if (!string.IsNullOrEmpty(curName)) wiredName = curName;
                                                    }
                                                }
                                                else
                                                {
                                                    if (candidateDongle == null || isMi01)
                                                    {
                                                        candidateDongle = devicePath;
                                                        donglePid = curPid != 0 ? curPid : 0x8F01;
                                                        if (!string.IsNullOrEmpty(curName)) dongleName = curName;
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

                if (!foundAnyNuphy) return null;

                string candidatePath = candidateWired ?? candidateDongle;
                int foundPid = (candidateWired != null) ? (wiredPid != 0 ? wiredPid : 0xA011) : (donglePid != 0 ? donglePid : 0x8F01);
                string foundName = (candidateWired != null) ? wiredName : dongleName;

                int foundBattery = -1;
                bool foundCharging = (foundPid == 0xA011);

                if (candidatePath != null)
                {
                    CachedCommandPath = candidatePath;
                    int curMode = CachedHardwareSettings.CurrentMode;
                    byte[] baseResp = SendNuphyCommand(candidatePath, 0x04, 0, 16, null, 300);
                    if (baseResp != null && baseResp.Length >= 10)
                    {
                        curMode = baseResp[9];
                        if (curMode < 0 || curMode > 3) curMode = 1;
                        CachedHardwareSettings.CurrentMode = curMode;
                    }

                    ushort battOffset = (ushort)(curMode * 64 + 32);
                    byte[] inBuf = SendNuphyCommand(candidatePath, 0x05, battOffset, 1, null, 300);
                    if (inBuf != null && (inBuf[1] == 0xAA || inBuf[1] == 0x55) && inBuf[2] == 0x05)
                    {
                        byte raw = inBuf[9];
                        int pct = raw & 0x7F;
                        if (pct >= 0 && pct <= 100)
                        {
                            foundBattery = pct;
                        }
                    }
                }

                bool isPureWired = (foundPid == 0xA011);
                if (foundBattery >= 0)
                {
                    CachedBatteryPercent = foundBattery;
                    CachedIsCharging = isPureWired || (foundBattery >= 100);
                    CachedLastUpdated = DateTime.Now;
                    SaveHardwareCache();
                }

                string devName = GetModelName(foundPid, foundName);
                if (string.IsNullOrEmpty(devName)) devName = !string.IsNullOrEmpty(CachedDeviceName) ? CachedDeviceName : ("NuPhy 键盘 (PID: " + foundPid.ToString("X4") + ")");
                CachedDeviceName = devName;

                int displayBattery = (foundBattery >= 0) ? foundBattery : (CachedBatteryPercent > 0 ? CachedBatteryPercent : -1);
                bool isCharging = isPureWired || (displayBattery >= 100);
                string transport = isPureWired ? "USB 有线" : "2.4G 无线";

                return new MouseBatteryInfo
                {
                    DeviceId = "HID:19F5:" + foundPid.ToString("X4"),
                    Brand = "NuPhy",
                    DeviceName = devName,
                    Category = DeviceCategory.Keyboard,
                    Transport = transport,
                    IsConnected = true,
                    IsSleeping = (foundBattery < 0 && candidatePath == null),
                    IsDonglePresent = (candidateDongle != null),
                    BatteryPercent = displayBattery,
                    ExactBatteryPercent = displayBattery,
                    IsCharging = isCharging,
                    LastUpdated = (foundBattery >= 0) ? DateTime.Now : (CachedLastUpdated != DateTime.MinValue ? CachedLastUpdated : DateTime.Now)
                };
            }
        }
    }
}