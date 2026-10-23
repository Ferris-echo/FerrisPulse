// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// 模块: 雷柏 VT3 状态输入、采样缓存与可取消 Win32 重叠 I/O。
// C# 5.0 / .NET 4.0; UTF-8 BOM; 无第三方依赖。
// 报文布局参考 rapoo-tray (MIT, Copyright (c) 2026 Iris)。
// 完整许可见 docs/rapoo-tray-MIT.md。
using System;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    public static partial class RapooDeviceHelper
    {
        public static bool CachedIsCharging = false;
        public static DateTime StatusLastUpdated = DateTime.MinValue;
        public static string LastStatusReportHex = "";
        public static string LastStatusError = "";
        private static string activeRapooGroup;
        private static int activeRapooModelPid;
        private static RapooEndpointSet activeRapooEndpoints;
        private static StatusSession statusSession;

        private sealed class StatusSession
        {
            public string Path;
            public int Length;
            public ManualResetEvent Stop = new ManualResetEvent(false);
            public volatile bool Failed;
        }

        private static void ResetRapooLiveState()
        {
            activeRapooModelPid = 0;
            CachedBatteryPercent = -1;
            CachedIsCharging = false;
            CachedLastUpdated = DateTime.MinValue;
            StatusLastUpdated = DateTime.MinValue;
            LastStatusReportHex = "";
            CachedDpi = CachedDpiY = 0;
            CachedDpiStage = 1;
            CachedDpiStageCount = 0;
            CachedDpiStages = new int[0];
            CachedDpiStagesY = new int[0];
            CachedPollingRate = 0;
            CachedIsXyIndependent = false;
            CachedXyRatio = 1.0;
        }

        public static bool ApplyStatusReport(byte[] report)
        {
            RapooStatusReport status;
            if (!RapooProtocol.TryParseStatus(report, out status)) return false;
            lock (hidLock)
            {
                CachedDpi = status.DpiX;
                CachedDpiY = status.DpiY;
                CachedDpiStage = status.DpiStage;
                CachedIsXyIndependent = status.DpiX != status.DpiY;
                CachedXyRatio = (double)status.DpiY / status.DpiX;
                CachedIsCharging = status.IsCharging;
                StatusLastUpdated = DateTime.UtcNow;
                LastStatusReportHex = BitConverter.ToString(report).Replace('-', ' ');
                if (status.BatteryPercent >= 0)
                {
                    CachedBatteryPercent = status.BatteryPercent;
                    CachedLastUpdated = DateTime.Now;
                }
                return true;
            }
        }

        private static void StopStatusReader()
        {
            // Do not join while holding hidLock: the worker may be publishing a frame.
            StatusSession session = statusSession;
            statusSession = null;
            if (session != null) session.Stop.Set();
        }

        private static void EnsureStatusReader(HidDeviceDescriptor descriptor)
        {
            if (descriptor == null) { StopStatusReader(); return; }
            if (statusSession != null && !statusSession.Failed &&
                string.Equals(statusSession.Path, descriptor.DevicePath, StringComparison.OrdinalIgnoreCase)) return;
            StopStatusReader();
            StatusSession session = new StatusSession();
            session.Path = descriptor.DevicePath;
            session.Length = descriptor.InputReportLength;
            statusSession = session;
            Thread worker = new Thread(delegate() { ReadStatusLoop(session); });
            worker.IsBackground = true;
            worker.Name = "RapooStatusReader";
            worker.Start();
        }

        private static void ReadStatusLoop(StatusSession session)
        {
            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = RazerDeviceHelper.CreateFile(session.Path, GENERIC_READ,
                    FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                if (!HidTransport.IsValidHandle(handle))
                {
                    LastStatusError = "Open status: " + Marshal.GetLastWin32Error();
                    return;
                }
                while (!session.Stop.WaitOne(0))
                {
                    byte[] report;
                    if (!RapooNativeIo.Read(handle, session.Length, session.Stop, out report)) break;
                    lock (hidLock)
                    {
                        // An old reader must never publish after a physical endpoint switch.
                        if (statusSession != session || session.Stop.WaitOne(0)) break;
                        ApplyStatusReport(report);
                    }
                }
            }
            catch (Exception ex) { LastStatusError = ex.GetType().Name + ": " + ex.Message; }
            finally
            {
                if (HidTransport.IsValidHandle(handle)) RazerDeviceHelper.CloseHandle(handle);
                // Stop is owned by this session. It remains valid for StopStatusReader,
                // which may still hold a reference until the next enumeration.
                lock (hidLock)
                {
                    session.Failed = true;
                    if (statusSession == session) statusSession = null;
                    session.Stop.Dispose();
                }
            }
        }

        private static void LoadBatteryCacheForGroup(string group)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(CacheRegistryKey, false))
                {
                    if (key == null || !string.Equals(key.GetValue("PhysicalGroup") as string, group, StringComparison.OrdinalIgnoreCase) ||
                        (key.GetValue("BatterySource") as string) != "HidStatus07") return;
                    int battery = Convert.ToInt32(key.GetValue("BatteryPercent", -1));
                    DateTime sampled;
                    if (battery >= 0 && battery <= 100 && DateTime.TryParse(key.GetValue("LastUpdated") as string, out sampled))
                    {
                        CachedBatteryPercent = battery;
                        CachedLastUpdated = sampled;
                    }
                }
            }
            catch { }
        }

        private static MouseBatteryInfo BuildRapooDeviceInfo(int pid, bool online, bool wired)
        {
            string name = GetModelName(pid);
            return new MouseBatteryInfo
            {
                DeviceId = "Rapoo:24AE:" + pid.ToString("X4"),
                HardwareFingerprint = "HID:24AE:" + pid.ToString("X4"),
                Brand = "Rapoo", Category = DeviceCategory.Mouse, DeviceName = name,
                IsConnected = true, IsSleeping = !online, IsDonglePresent = !wired,
                Transport = wired ? "USB 有线" : "2.4G 无线",
                BatteryPercent = CachedBatteryPercent,
                IsCharging = online && CachedIsCharging,
                LastUpdated = CachedLastUpdated,
                Dpi = CachedDpi, DpiY = CachedDpiY, DpiStage = CachedDpiStage,
                DpiStageCount = CachedDpiStageCount,
                DpiStages = (int[])CachedDpiStages.Clone(), DpiStagesY = (int[])CachedDpiStagesY.Clone(),
                IsXyIndependent = CachedIsXyIndependent, XyRatio = CachedXyRatio,
                PollingRate = CachedPollingRate,
                SupportedPollingRates = GetSupportedPollingRatesForModel(pid, name)
            };
        }
    }

    // Native buffers stay allocated until cancellation has actually completed.
    // The shared HidTransport's managed OVERLAPPED timeout path is intentionally
    // not used here: CancelIo alone does not complete pending kernel accesses.
    internal static class RapooNativeIo
    {
        [StructLayout(LayoutKind.Sequential)]
        private struct Overlapped
        {
            public IntPtr Internal, InternalHigh;
            public uint Offset, OffsetHigh;
            public IntPtr Event;
        }
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(IntPtr handle, IntPtr buffer, uint length, out uint count, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(IntPtr handle, IntPtr buffer, uint length, out uint count, IntPtr overlapped);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetOverlappedResult(IntPtr handle, IntPtr overlapped, out uint count, bool wait);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CancelIo(IntPtr handle);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateEvent(IntPtr attributes, bool manual, bool initial, string name);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr handle, uint milliseconds);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr handle);

        public static bool Read(IntPtr handle, int length, ManualResetEvent stop, out byte[] report)
        {
            report = null;
            return Transfer(handle, null, length, stop, 0, out report);
        }

        public static bool Read(IntPtr handle, int length, int timeout, out byte[] report)
        {
            return Transfer(handle, null, length, null, timeout, out report);
        }

        public static bool Write(IntPtr handle, byte[] report, int timeout)
        {
            byte[] ignored;
            return Transfer(handle, report, report.Length, null, timeout, out ignored);
        }

        private static bool Transfer(IntPtr handle, byte[] output, int length, ManualResetEvent stop, int timeout, out byte[] input)
        {
            input = null;
            IntPtr evt = CreateEvent(IntPtr.Zero, true, false, null);
            if (evt == IntPtr.Zero) return false;
            IntPtr buffer = IntPtr.Zero;
            IntPtr overlapped = IntPtr.Zero;
            bool pending = false;
            try
            {
                buffer = Marshal.AllocHGlobal(length);
                overlapped = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(Overlapped)));
                Overlapped value = new Overlapped();
                value.Event = evt;
                Marshal.StructureToPtr(value, overlapped, false);
                if (output != null) Marshal.Copy(output, 0, buffer, length);
                uint count;
                bool ok = output == null ? ReadFile(handle, buffer, (uint)length, out count, overlapped) :
                    WriteFile(handle, buffer, (uint)length, out count, overlapped);
                if (!ok)
                {
                    if (Marshal.GetLastWin32Error() != 997) return false;
                    pending = true;
                    if (output == null)
                    {
                        if (stop == null)
                        {
                            if (WaitForSingleObject(evt, (uint)timeout) != 0) return false;
                        }
                        else while (true)
                        {
                            if (stop.WaitOne(0)) return false;
                            uint wait = WaitForSingleObject(evt, 250);
                            if (wait == 0) break;
                            if (wait != 258) return false;
                        }
                    }
                    else if (WaitForSingleObject(evt, (uint)timeout) != 0) return false;
                    ok = GetOverlappedResult(handle, overlapped, out count, false);
                    pending = false;
                }
                if (!ok || count == 0 || count > length) return false;
                if (output != null) return count == length;
                input = new byte[count];
                Marshal.Copy(buffer, input, 0, (int)count);
                return true;
            }
            finally
            {
                if (pending)
                {
                    CancelIo(handle);
                    uint ignored;
                    GetOverlappedResult(handle, overlapped, out ignored, true);
                }
                if (buffer != IntPtr.Zero) Marshal.FreeHGlobal(buffer);
                if (overlapped != IntPtr.Zero) Marshal.FreeHGlobal(overlapped);
                CloseHandle(evt);
            }
        }
    }
}
