// ============================================================================
// 模块名称: CorsairDeviceHelper.Mouse.cs (CorsairDeviceHelper 分部类)
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   美商海盗船 M75 AIR 无线鼠标 (SLIPSTREAM 接收器 1B1C:1BDC / 子设备 PID 0x1BF2)
//   Bragi 65 字节控制协议：电量 0x0F、供电状态 0x10、DPI X/Y 0x21/0x22、
//   回报率 0x01；支持 DPI 双轴读写与回报率档位切换。
// 依赖关系: CorsairDeviceHelper.cs (HID 基座与 HS80 耳机协议), MouseBatteryInfo.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 修改本文件协议、缓存或签名时必须同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止字符串插值、空条件运算符、nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// 4. [协议来源]: ckb-next Bragi 实现 (bragi_proto.h / dpi_bragi.c) + 本机实机抓包：
//      TX 02 09 02 12 -> RX 00 01 02 00 F2 1B   (M75 AIR PID 0x1BF2)
//      TX 02 09 02 0F -> RX 00 01 02 00 76 02   (0x0276 = 630 -> 63.0%)
//      TX 02 09 02 21 -> RX 00 01 02 00 40 06   (DPI X = 1600)
//      TX 02 09 02 01 -> RX 00 01 02 00 04      (回报率码 4 = 1000Hz)
//      SET 0x21=800 / 0x22=1200 后回读一致，已验证 X/Y 独立写入。
// 5. [端点隔离]: 仅接管 MI_01 (65 字节) 上的 Bragi 子设备；鼠标输入帧 00 00 ... 必须过滤。
// ============================================================================

using System;
using System.Runtime.InteropServices;
using System.Text;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    public static partial class CorsairDeviceHelper
    {
        public const int PidM75Air = 0x1BF2;
        public const int PidSlipstreamReceiver = 0x1BDC;

        private const int SlipPacketLength = 65;
        private const string MouseCacheRegistryKey = @"Software\RazerBatteryTray\CorsairMouseCache";
        private const int MouseQueryThrottleMs = 700;
        private const int MouseReadBudgetMs = 520;
        private const int MouseIdentityBudgetMs = 240;
        private const int MinDpi = 100;
        private const int MaxDpi = 26000;
        private const int DpiStep = 50;

        private static readonly object mouseLock = new object();
        private static bool mouseHasQueried = false;
        private static int mouseLastQueryTick = 0;
        private static bool mouseDonglePresent = false;
        private static int mouseDetectedPid = PidM75Air;

        public static int CachedMouseBatteryPercent = -1;
        public static bool CachedMouseIsCharging = false;
        public static bool CachedMouseIsConnected = false;
        public static int CachedMouseDpiX = 0;
        public static int CachedMouseDpiY = 0;
        public static int CachedMousePollingHz = 0;
        public static string CachedMouseDeviceName = "M75 AIR Wireless";
        public static DateTime CachedMouseLastUpdated = DateTime.MinValue;

        public static bool IsSupportedMouseVidPid(int vid, int pid)
        {
            if (vid != VendorIdCorsair) return false;
            return (pid == PidM75Air || pid == PidSlipstreamReceiver);
        }

        public static bool IsM75AirIdentifier(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return false;
            string lower = identifier.ToLowerInvariant();
            if (lower.IndexOf("1b1c", StringComparison.Ordinal) < 0) return false;
            return lower.IndexOf("1bf2", StringComparison.Ordinal) >= 0 ||
                   lower.IndexOf("1bdc", StringComparison.Ordinal) >= 0;
        }

        public static string GetMouseModelName(int pid, string productString)
        {
            if (!string.IsNullOrEmpty(productString))
            {
                string prod = productString.Trim();
                if (prod.IndexOf("M75", StringComparison.OrdinalIgnoreCase) >= 0) return "M75 AIR Wireless";
            }
            return pid == PidM75Air ? "M75 AIR Wireless" : "Corsair SLIPSTREAM 无线鼠标";
        }

        public static int PollCodeToHz(int code)
        {
            switch (code)
            {
                case 1: return 125;
                case 2: return 250;
                case 3: return 500;
                case 4: return 1000;
                default: return 0;
            }
        }

        public static int HzToPollCode(int hz)
        {
            if (hz <= 125) return 1;
            if (hz <= 250) return 2;
            if (hz <= 500) return 3;
            return 4;
        }

        public static int[] GetSupportedPollingRates()
        {
            return new int[] { 125, 250, 500, 1000 };
        }

        public static bool TryParseMouseValue(byte[] rx, int length, byte expectedCmd, out int err, out int value)
        {
            err = -1;
            value = 0;
            if (rx == null || length < 7) return false;
            if (rx[0] != 0x00 || rx[1] != 0x01) return false;   // 仅接受子设备同步响应，过滤鼠标输入帧
            if (rx[2] != expectedCmd) return false;
            err = rx[3];
            value = rx[4] | (rx[5] << 8) | (rx[6] << 16);
            return true;
        }

        public static bool TryParseMouseBattery(byte[] rx, int length, out double percent)
        {
            percent = -1;
            int err;
            int value;
            if (!TryParseMouseValue(rx, length, 0x02, out err, out value)) return false;
            if (err != 0) return false;
            if (value < 0 || value > 1000) return false;
            percent = value / 10.0;
            return true;
        }

        public static bool TryParseMouseChargeState(byte[] rx, int length, out int state)
        {
            state = -1;
            int err;
            int value;
            if (!TryParseMouseValue(rx, length, 0x02, out err, out value)) return false;
            if (err != 0) return false;
            if (value < 0 || value > 3) return false;
            state = value;
            return true;
        }

        public static bool TryParseMouseDpi(byte[] rx, int length, out int dpi)
        {
            dpi = 0;
            int err;
            int value;
            if (!TryParseMouseValue(rx, length, 0x02, out err, out value)) return false;
            if (err != 0) return false;
            if (value < MinDpi || value > MaxDpi) return false;
            dpi = value;
            return true;
        }

        public static bool TryParseMousePolling(byte[] rx, int length, out int hz)
        {
            hz = 0;
            int err;
            int code;
            if (!TryParseMouseValue(rx, length, 0x02, out err, out code)) return false;
            if (err != 0) return false;
            int value = PollCodeToHz(code);
            if (value <= 0) return false;
            hz = value;
            return true;
        }

        public static void LoadMouseHardwareCache()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(MouseCacheRegistryKey, false))
                {
                    if (key == null) return;
                    object batt = key.GetValue("BatteryPercent");
                    if (batt != null) CachedMouseBatteryPercent = Convert.ToInt32(batt);
                    object chg = key.GetValue("IsCharging");
                    if (chg != null) CachedMouseIsCharging = (Convert.ToInt32(chg) == 1);
                    object dx = key.GetValue("DpiX");
                    if (dx != null) CachedMouseDpiX = Convert.ToInt32(dx);
                    object dy = key.GetValue("DpiY");
                    if (dy != null) CachedMouseDpiY = Convert.ToInt32(dy);
                    object poll = key.GetValue("PollingHz");
                    if (poll != null) CachedMousePollingHz = Convert.ToInt32(poll);
                    string name = key.GetValue("DeviceName") as string;
                    if (!string.IsNullOrEmpty(name)) CachedMouseDeviceName = name;
                    string updated = key.GetValue("LastUpdated") as string;
                    if (!string.IsNullOrEmpty(updated))
                    {
                        DateTime dt;
                        if (DateTime.TryParse(updated, out dt)) CachedMouseLastUpdated = dt;
                    }
                }
            }
            catch { }
        }

        public static void SaveMouseHardwareCache()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(MouseCacheRegistryKey))
                {
                    if (key == null) return;
                    if (CachedMouseBatteryPercent >= 0) key.SetValue("BatteryPercent", CachedMouseBatteryPercent);
                    key.SetValue("IsCharging", CachedMouseIsCharging ? 1 : 0);
                    if (CachedMouseDpiX > 0) key.SetValue("DpiX", CachedMouseDpiX);
                    if (CachedMouseDpiY > 0) key.SetValue("DpiY", CachedMouseDpiY);
                    if (CachedMousePollingHz > 0) key.SetValue("PollingHz", CachedMousePollingHz);
                    if (!string.IsNullOrEmpty(CachedMouseDeviceName)) key.SetValue("DeviceName", CachedMouseDeviceName);
                    if (CachedMouseLastUpdated != DateTime.MinValue) key.SetValue("LastUpdated", CachedMouseLastUpdated.ToString("o"));
                }
            }
            catch { }
        }

        public static MouseBatteryInfo QueryCorsairMouseInfo()
        {
            lock (mouseLock)
            {
                if (mouseHasQueried)
                {
                    int elapsed = Environment.TickCount - mouseLastQueryTick;
                    if (elapsed >= 0 && elapsed < MouseQueryThrottleMs)
                    {
                        return mouseDonglePresent ? BuildMouseInfo() : null;
                    }
                }

                string path = FindSlipstreamControlPath();
                if (string.IsNullOrEmpty(path))
                {
                    mouseDonglePresent = false;
                    CachedMouseIsConnected = false;
                    CachedMouseIsCharging = false;
                    mouseLastQueryTick = Environment.TickCount;
                    mouseHasQueried = true;
                    return null;
                }

                mouseDonglePresent = true;
                ReadMouseState(path);
                mouseLastQueryTick = Environment.TickCount;
                mouseHasQueried = true;
                return BuildMouseInfo();
            }
        }

        private static MouseBatteryInfo BuildMouseInfo()
        {
            MouseBatteryInfo info = new MouseBatteryInfo();
            info.DeviceId = string.Format("Corsair:{0:X4}:{1:X4}", VendorIdCorsair, mouseDetectedPid);
            info.HardwareFingerprint = string.Format("HID:{0:X4}:{1:X4}", VendorIdCorsair, mouseDetectedPid);
            info.HardwareId = string.Format("USB\\VID_{0:X4}&PID_{1:X4}", VendorIdCorsair, PidSlipstreamReceiver);
            info.DriverId = "BuiltIn:CorsairMouse";
            info.Brand = "Corsair";
            info.Category = DeviceCategory.Mouse;
            info.DeviceName = CachedMouseDeviceName;
            info.Transport = "2.4G 无线 (SLIPSTREAM)";
            info.IsCustom = false;
            info.IsDonglePresent = mouseDonglePresent;
            info.IsConnected = CachedMouseIsConnected;
            info.IsSleeping = mouseDonglePresent && !CachedMouseIsConnected;
            info.IsCharging = CachedMouseIsConnected && CachedMouseIsCharging;
            info.BatteryPercent = CachedMouseBatteryPercent;
            info.ExactBatteryPercent = CachedMouseBatteryPercent >= 0 ? CachedMouseBatteryPercent : 0;
            info.LastUpdated = (CachedMouseLastUpdated == DateTime.MinValue) ? DateTime.Now : CachedMouseLastUpdated;
            info.Dpi = CachedMouseDpiX;
            info.DpiY = CachedMouseDpiY;
            info.DpiStage = 0;
            info.DpiStageCount = 1;
            info.IsXyIndependent = true;
            info.PollingRate = CachedMousePollingHz;
            info.MaxPollingRate = 1000;
            info.SupportedPollingRates = GetSupportedPollingRates();
            info.Capabilities.SupportsBattery = true;
            info.Capabilities.SupportsDpi = true;
            info.Capabilities.SupportsDpiStages = false;
            info.Capabilities.SupportsPollingRate = true;
            info.Capabilities.SupportsXyIndependence = true;
            info.Capabilities.SupportsFastDpiMonitor = false;
            info.Capabilities.SupportsHardwareDpiMonitor = false;
            info.Capabilities.MinDpi = MinDpi;
            info.Capabilities.MaxDpi = MaxDpi;
            info.Capabilities.DpiStep = DpiStep;
            info.Capabilities.SupportedPollingRates = info.SupportedPollingRates;
            info.Capabilities.SensorDescription = "Corsair MARKSMAN S 光学传感器";
            return info;
        }

        private static string FindSlipstreamControlPath()
        {
            try
            {
                Guid hidGuid;
                RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
                IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return null;

                RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(ifData);
                uint memberIdx = 0;
                try
                {
                    while (RazerDeviceHelper.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                    {
                        uint reqSize;
                        RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                        if (reqSize == 0) continue;
                        IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);
                        try
                        {
                            if (RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                            {
                                IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                                string devicePath = Marshal.PtrToStringAuto(pDevicePath);
                                if (string.IsNullOrEmpty(devicePath)) continue;
                                string lower = devicePath.ToLowerInvariant();
                                if (lower.IndexOf("vid_1b1c", StringComparison.Ordinal) < 0) continue;
                                if (lower.IndexOf("pid_1bdc", StringComparison.Ordinal) < 0) continue;
                                if (lower.IndexOf("mi_01", StringComparison.Ordinal) < 0) continue;
                                return devicePath;
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(detailBuffer);
                        }
                        ifData.cbSize = Marshal.SizeOf(ifData);
                    }
                }
                finally
                {
                    RazerDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);
                }
            }
            catch { }
            return null;
        }

        private static void ReadMouseState(string path)
        {
            IntPtr handle = RazerDeviceHelper.CreateFile(path, GenericRead | GenericWrite,
                RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                IntPtr.Zero, RazerDeviceHelper.OPEN_EXISTING, FileFlagOverlapped, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle.ToInt64() == -1)
            {
                CachedMouseIsConnected = false;
                CachedMouseIsCharging = false;
                return;
            }

            IntPtr evt = CreateEvent(IntPtr.Zero, true, false, null);
            if (evt == IntPtr.Zero)
            {
                RazerDeviceHelper.CloseHandle(handle);
                CachedMouseIsConnected = false;
                return;
            }

            try
            {
                RazerDeviceHelper.HIDD_ATTRIBUTES attr = new RazerDeviceHelper.HIDD_ATTRIBUTES();
                attr.Size = Marshal.SizeOf(attr);
                if (RazerDeviceHelper.HidD_GetAttributes(handle, ref attr))
                {
                    mouseDetectedPid = PidM75Air;
                    StringBuilder sbProd = new StringBuilder(256);
                    string prod = RazerDeviceHelper.HidD_GetProductString(handle, sbProd, sbProd.Capacity) ? sbProd.ToString().Trim() : null;
                    CachedMouseDeviceName = GetMouseModelName(mouseDetectedPid, prod);
                }

                int pidErr;
                int childPid;
                byte[] pidRx = MouseXfer(handle, evt, 0x02, 0x12, 0, MouseIdentityBudgetMs);
                if (pidRx == null || !TryParseMouseValue(pidRx, pidRx.Length, 0x02, out pidErr, out childPid) ||
                    pidErr != 0 || childPid != PidM75Air)
                {
                    CachedMouseIsConnected = false;
                    CachedMouseIsCharging = false;
                    return;
                }
                mouseDetectedPid = PidM75Air;

                int batteryErr;
                int batteryRaw;
                byte[] battRx = MouseXfer(handle, evt, 0x02, 0x0F, 0, MouseReadBudgetMs);
                bool batteryOk = false;
                int newBattery = -1;
                if (battRx != null && TryParseMouseValue(battRx, battRx.Length, 0x02, out batteryErr, out batteryRaw))
                {
                    if (batteryErr == 0 && batteryRaw >= 0 && batteryRaw <= 1000)
                    {
                        newBattery = (int)Math.Round(batteryRaw / 10.0);
                        batteryOk = true;
                    }
                }

                int chargeState = -1;
                byte[] stRx = MouseXfer(handle, evt, 0x02, 0x10, 0, MouseReadBudgetMs);
                if (stRx != null)
                {
                    int stErr;
                    int stVal;
                    if (TryParseMouseValue(stRx, stRx.Length, 0x02, out stErr, out stVal) && stErr == 0 && stVal >= 0 && stVal <= 3)
                    {
                        chargeState = stVal;
                    }
                }

                int dpiX = 0, dpiY = 0, pollHz = 0;
                byte[] dxRx = MouseXfer(handle, evt, 0x02, 0x21, 0, MouseReadBudgetMs);
                if (dxRx != null)
                {
                    int e, v;
                    if (TryParseMouseValue(dxRx, dxRx.Length, 0x02, out e, out v) && e == 0 && v >= MinDpi && v <= MaxDpi) dpiX = v;
                }
                byte[] dyRx = MouseXfer(handle, evt, 0x02, 0x22, 0, MouseReadBudgetMs);
                if (dyRx != null)
                {
                    int e, v;
                    if (TryParseMouseValue(dyRx, dyRx.Length, 0x02, out e, out v) && e == 0 && v >= MinDpi && v <= MaxDpi) dpiY = v;
                }
                byte[] pRx = MouseXfer(handle, evt, 0x02, 0x01, 0, MouseReadBudgetMs);
                if (pRx != null)
                {
                    int e, v;
                    if (TryParseMouseValue(pRx, pRx.Length, 0x02, out e, out v) && e == 0) pollHz = PollCodeToHz(v);
                }

                if (batteryOk)
                {
                    int prevBatt = CachedMouseBatteryPercent;
                    int prevDpiX = CachedMouseDpiX;
                    int prevDpiY = CachedMouseDpiY;
                    int prevPoll = CachedMousePollingHz;

                    CachedMouseBatteryPercent = newBattery;
                    CachedMouseIsCharging = (chargeState >= 0) ? IsChargingState(chargeState) : CachedMouseIsCharging;
                    CachedMouseIsConnected = true;
                    if (dpiX > 0) CachedMouseDpiX = dpiX;
                    if (dpiY > 0) CachedMouseDpiY = dpiY;
                    if (pollHz > 0) CachedMousePollingHz = pollHz;
                    CachedMouseLastUpdated = DateTime.Now;

                    if (prevBatt != CachedMouseBatteryPercent || prevDpiX != CachedMouseDpiX ||
                        prevDpiY != CachedMouseDpiY || prevPoll != CachedMousePollingHz)
                    {
                        SaveMouseHardwareCache();
                    }
                }
                else
                {
                    CachedMouseIsConnected = false;
                    CachedMouseIsCharging = false;
                }
            }
            catch
            {
                CachedMouseIsConnected = false;
            }
            finally
            {
                CloseHandle(evt);
                RazerDeviceHelper.CloseHandle(handle);
            }
        }

        public static bool QueryMouseDpi(out int dpiX, out int dpiY)
        {
            dpiX = CachedMouseDpiX;
            dpiY = CachedMouseDpiY;
            lock (mouseLock)
            {
                string path = FindSlipstreamControlPath();
                if (string.IsNullOrEmpty(path)) return false;
                IntPtr handle = RazerDeviceHelper.CreateFile(path, GenericRead | GenericWrite,
                    RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                    IntPtr.Zero, RazerDeviceHelper.OPEN_EXISTING, FileFlagOverlapped, IntPtr.Zero);
                if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;
                IntPtr evt = CreateEvent(IntPtr.Zero, true, false, null);
                if (evt == IntPtr.Zero) { RazerDeviceHelper.CloseHandle(handle); return false; }
                try
                {
                    int x = 0, y = 0;
                    int e, v;
                    byte[] rx = MouseXfer(handle, evt, 0x02, 0x21, 0, MouseReadBudgetMs);
                    if (rx == null || !TryParseMouseValue(rx, rx.Length, 0x02, out e, out v) || e != 0) return false;
                    x = v;
                    rx = MouseXfer(handle, evt, 0x02, 0x22, 0, MouseReadBudgetMs);
                    if (rx == null || !TryParseMouseValue(rx, rx.Length, 0x02, out e, out v) || e != 0) return false;
                    y = v;
                    if (x < MinDpi || x > MaxDpi || y < MinDpi || y > MaxDpi) return false;
                    CachedMouseDpiX = x;
                    CachedMouseDpiY = y;
                    dpiX = x;
                    dpiY = y;
                    return true;
                }
                finally
                {
                    CloseHandle(evt);
                    RazerDeviceHelper.CloseHandle(handle);
                }
            }
        }

        public static bool SetMouseDpi(int dpiX, int dpiY)
        {
            if (dpiX <= 0) dpiX = dpiY;
            if (dpiY <= 0) dpiY = dpiX;
            if (dpiX < MinDpi) dpiX = MinDpi;
            if (dpiX > MaxDpi) dpiX = MaxDpi;
            if (dpiY < MinDpi) dpiY = MinDpi;
            if (dpiY > MaxDpi) dpiY = MaxDpi;

            lock (mouseLock)
            {
                string path = FindSlipstreamControlPath();
                if (string.IsNullOrEmpty(path)) return false;
                IntPtr handle = RazerDeviceHelper.CreateFile(path, GenericRead | GenericWrite,
                    RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                    IntPtr.Zero, RazerDeviceHelper.OPEN_EXISTING, FileFlagOverlapped, IntPtr.Zero);
                if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;
                IntPtr evt = CreateEvent(IntPtr.Zero, true, false, null);
                if (evt == IntPtr.Zero) { RazerDeviceHelper.CloseHandle(handle); return false; }
                try
                {
                    byte[] rx = MouseXfer(handle, evt, 0x01, 0x21, dpiX, MouseReadBudgetMs);
                    int e, v;
                    if (rx == null || !TryParseMouseValue(rx, rx.Length, 0x01, out e, out v) || e != 0) return false;
                    rx = MouseXfer(handle, evt, 0x01, 0x22, dpiY, MouseReadBudgetMs);
                    if (rx == null || !TryParseMouseValue(rx, rx.Length, 0x01, out e, out v) || e != 0) return false;
                    CachedMouseDpiX = dpiX;
                    CachedMouseDpiY = dpiY;
                    SaveMouseHardwareCache();
                    return true;
                }
                finally
                {
                    CloseHandle(evt);
                    RazerDeviceHelper.CloseHandle(handle);
                }
            }
        }

        public static bool SetMousePollingRate(int hz)
        {
            int code = HzToPollCode(hz);
            lock (mouseLock)
            {
                string path = FindSlipstreamControlPath();
                if (string.IsNullOrEmpty(path)) return false;
                IntPtr handle = RazerDeviceHelper.CreateFile(path, GenericRead | GenericWrite,
                    RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                    IntPtr.Zero, RazerDeviceHelper.OPEN_EXISTING, FileFlagOverlapped, IntPtr.Zero);
                if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;
                IntPtr evt = CreateEvent(IntPtr.Zero, true, false, null);
                if (evt == IntPtr.Zero) { RazerDeviceHelper.CloseHandle(handle); return false; }
                try
                {
                    byte[] rx = MouseXfer(handle, evt, 0x01, 0x01, code, MouseReadBudgetMs);
                    int e, v;
                    if (rx == null || !TryParseMouseValue(rx, rx.Length, 0x01, out e, out v) || e != 0) return false;
                    CachedMousePollingHz = PollCodeToHz(code);
                    SaveMouseHardwareCache();
                    return true;
                }
                finally
                {
                    CloseHandle(evt);
                    RazerDeviceHelper.CloseHandle(handle);
                }
            }
        }

        private static void DrainMouseReports(IntPtr handle, IntPtr evt)
        {
            byte[] tmp = new byte[SlipPacketLength];
            for (int i = 0; i < 24; i++)
            {
                if (ReadReport(handle, evt, tmp, 70) <= 0) break;
            }
        }

        private static byte[] MouseXfer(IntPtr handle, IntPtr evt, byte cmd, byte prop, int value, int budgetMs)
        {
            try
            {
                DrainMouseReports(handle, evt);

                byte[] tx = new byte[SlipPacketLength];
                tx[0] = 0x02;
                tx[1] = 0x09;
                tx[2] = cmd;
                tx[3] = prop;
                tx[4] = 0x00;
                tx[5] = (byte)(value & 0xFF);
                tx[6] = (byte)((value >> 8) & 0xFF);

                if (!WriteReport(handle, evt, tx)) return null;

                byte[] rx = new byte[SlipPacketLength];
                long deadline = Environment.TickCount + budgetMs;
                while (true)
                {
                    int remain = (int)(deadline - Environment.TickCount);
                    if (remain <= 0) break;
                    int timeout = (remain < 180) ? remain : 180;
                    int len = ReadReport(handle, evt, rx, timeout);
                    if (len <= 0) continue;
                    if (rx[0] == 0x00 && rx[1] == 0x01 && rx[2] == cmd)
                    {
                        byte[] copy = new byte[len];
                        Array.Copy(rx, copy, len);
                        return copy;
                    }
                }
            }
            catch { }
            return null;
        }
    }
}
