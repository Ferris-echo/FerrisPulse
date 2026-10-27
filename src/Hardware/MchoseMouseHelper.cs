// ============================================================================
// 模块名称: MchoseMouseHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   MCHOSE（迈从）A5 V2 Ultra 等 2.4G / USB / 蓝牙三模鼠标私有 HID 协议驱动。
//   （特别鸣谢社区群友“冰城”逆向贡献，原生支持精确物理电量、充电感知、XY 轴独立 DPI 与 8K 回报率）
// 依赖关系: RazerDeviceHelper.cs (共享 HID P/Invoke), MouseBatteryInfo.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、协议解析或签名时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（字符串插值、空条件运算符、nameof、表达式主体等）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    public static class MchoseMouseHelper
    {
        private static readonly object hidLock = new object();
        private const string CacheRegistryKey = @"Software\RazerBatteryTray\MchoseMouseCache";

        public const int VID_MCHOSE = 0x3837;
        public const int VID_MCHOSE_ALT = 0x5253;
        public const int PID_A5_V2_ULTRA_RX = 0x100C;
        public const int PID_A5_V2_ULTRA = 0x1101;

        private const byte REPORT_SHORT = 0x11; // 20 字节 payload
        private const byte REPORT_LONG = 0x12;  // 64 字节 payload

        private const byte CMD_IDENTITY = 0x06;
        private const byte CMD_CONFIG_READ = 0x67;
        private const byte CMD_CONFIG_WRITE = 0x57;
        private const byte CMD_RATE = 0x41;
        private const byte CMD_PROFILE_SELECT = 0x58;

        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;

        // 官方 8K 固件的回报率索引表；无线链路最高 4000Hz，USB 有线最高 8000Hz。
        private static readonly int[] RATES = new int[] { 125, 500, 1000, 2000, 4000, 8000 };

        private static DateTime lastCommandUtc = DateTime.MinValue;

        public static int CachedBatteryPercent = -1;
        public static bool CachedIsCharging = false;
        public static string CachedDeviceName = "";
        public static DateTime CachedLastUpdated = DateTime.MinValue;
        public static bool HasValidHardwareReading = false;

        public class MchoseConfig
        {
            public byte[] Raw;
            public int WiredRateIndex;
            public int WiredStage;
            public int WirelessRateIndex;
            public int WirelessStage;
            public int EnabledStages;
            public int Sensor;
            public int DebounceMs;
            public int SleepMinutes;
            public int[] DpiX;
            public int[] DpiY;
        }

        private struct IdentityReading
        {
            public int Vid;
            public int Pid;
            public int Flags;
            public int Battery;
            public int Charge;
            public int Firmware;
        }

        static MchoseMouseHelper()
        {
            LoadHardwareCache();
        }

        public static void LoadHardwareCache()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(CacheRegistryKey, false))
                {
                    if (key == null) return;

                    object bp = key.GetValue("BatteryPercent");
                    object validObj = key.GetValue("IsValid");
                    bool valid = (validObj != null && Convert.ToInt32(validObj) == 1);

                    if (bp != null)
                    {
                        int val = Convert.ToInt32(bp);
                        if (val >= 0 && val <= 100 && valid)
                        {
                            CachedBatteryPercent = val;
                            HasValidHardwareReading = true;
                        }
                    }

                    object chg = key.GetValue("IsCharging");
                    if (chg != null) CachedIsCharging = (Convert.ToInt32(chg) != 0);

                    string dn = key.GetValue("DeviceName") as string;
                    if (!string.IsNullOrEmpty(dn)) CachedDeviceName = dn;

                    string updated = key.GetValue("LastUpdated") as string;
                    if (!string.IsNullOrEmpty(updated))
                    {
                        DateTime dt;
                        if (DateTime.TryParse(updated, out dt)) CachedLastUpdated = dt;
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/MchoseMouseHelper.cs: 已忽略异常", diagEx); }
        }

        public static void SaveHardwareCache()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(CacheRegistryKey))
                {
                    if (key == null) return;

                    if (CachedBatteryPercent >= 0)
                    {
                        key.SetValue("BatteryPercent", CachedBatteryPercent, RegistryValueKind.DWord);
                        key.SetValue("IsValid", HasValidHardwareReading ? 1 : 0, RegistryValueKind.DWord);
                    }
                    key.SetValue("IsCharging", CachedIsCharging ? 1 : 0, RegistryValueKind.DWord);
                    if (!string.IsNullOrEmpty(CachedDeviceName)) key.SetValue("DeviceName", CachedDeviceName);
                    if (CachedLastUpdated != DateTime.MinValue) key.SetValue("LastUpdated", CachedLastUpdated.ToString("o"));
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/MchoseMouseHelper.cs: 已忽略异常", diagEx); }
        }

        public static bool IsMchoseDevice(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;
            string upper = value.ToUpperInvariant();
            return upper.IndexOf("MCHOSE", StringComparison.Ordinal) >= 0 ||
                   upper.IndexOf("3837", StringComparison.Ordinal) >= 0 ||
                   upper.IndexOf("5253", StringComparison.Ordinal) >= 0;
        }

        public static string GetModelName(int pid, string productString)
        {
            if (!string.IsNullOrEmpty(productString))
            {
                string p = productString.Trim();
                if (p.Length > 0 && p.IndexOf("MCHOSE", StringComparison.OrdinalIgnoreCase) >= 0) return p;
            }

            switch (pid)
            {
                case PID_A5_V2_ULTRA: return "MCHOSE A5 V2 Ultra";
                case PID_A5_V2_ULTRA_RX: return "MCHOSE A5 V2 Ultra (2.4G 接收器)";
                default: return !string.IsNullOrEmpty(productString) ? productString.Trim() : "MCHOSE 无线游戏鼠标";
            }
        }

        public static int[] GetSupportedPollingRatesForMode(int mode)
        {
            if (mode == 0)
            {
                // USB 有线：官方 8K
                return new int[] { 500, 1000, 2000, 4000, 8000 };
            }
            // 2.4G 无线：官方 4K
            return new int[] { 125, 500, 1000, 2000, 4000 };
        }

        public static int GetMaxPollingRateForMode(int mode)
        {
            return (mode == 0) ? 8000 : 4000;
        }

        public static string GetTransportName(int mode)
        {
            if (mode == 0) return "USB 有线";
            if (mode == 1) return "2.4G 无线";
            if (mode == 2) return "蓝牙";
            return "无线";
        }

        public static bool TryParseIdentityResponse(byte[] response, out int vid, out int pid, out int flags, out int battery, out int charge)
        {
            vid = 0;
            pid = 0;
            flags = 0;
            battery = -1;
            charge = 0;

            if (response == null || response.Length < 13) return false;
            if (response[0] != REPORT_SHORT || response[1] != 0xF9) return false;

            vid = (response[2] ^ 0xFF) | ((response[3] ^ 0xFF) << 8);
            pid = (response[4] ^ 0xFF) | ((response[5] ^ 0xFF) << 8);
            flags = (response[10] ^ 0xFF);
            battery = (response[11] ^ 0xFF);
            charge = (response[12] ^ 0xFF);

            if (vid == 0 && pid == 0) return false;
            if (battery < 0 || battery > 100) return false;
            return true;
        }

        public static MchoseConfig ParseConfigPayload(byte[] payload)
        {
            if (payload == null || payload.Length < 63) return null;

            MchoseConfig cfg = new MchoseConfig();
            cfg.Raw = new byte[63];
            Array.Copy(payload, 0, cfg.Raw, 0, 63);
            cfg.WiredRateIndex = (payload[1] >> 4) & 0x0F;
            cfg.WiredStage = payload[1] & 0x0F;
            cfg.WirelessRateIndex = (payload[2] >> 4) & 0x0F;
            cfg.WirelessStage = payload[2] & 0x0F;
            cfg.EnabledStages = payload[16];
            cfg.Sensor = payload[17];
            cfg.DebounceMs = payload[18];
            cfg.SleepMinutes = payload[19];
            cfg.DpiX = new int[6];
            cfg.DpiY = new int[6];

            for (int i = 0; i < 6; i++)
            {
                cfg.DpiX[i] = payload[4 + i * 2] | (payload[5 + i * 2] << 8);
                cfg.DpiY[i] = payload[51 + i * 2] | (payload[52 + i * 2] << 8);
            }
            return cfg;
        }

        private static void Pace()
        {
            if (lastCommandUtc != DateTime.MinValue)
            {
                int elapsed = (int)(DateTime.UtcNow - lastCommandUtc).TotalMilliseconds;
                if (elapsed >= 0 && elapsed < 25) Thread.Sleep(25 - elapsed);
            }
            lastCommandUtc = DateTime.UtcNow;
        }

        private static bool SendOnly(IntPtr handle, byte reportId, byte[] body, int payloadLength)
        {
            if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return false;
            if (body == null || body.Length == 0 || body.Length > payloadLength) return false;

            byte[] request = new byte[payloadLength + 1];
            for (int i = 0; i < request.Length; i++) request[i] = 0xFF;
            request[0] = reportId;
            for (int i = 0; i < body.Length; i++) request[i + 1] = (byte)(body[i] ^ 0xFF);

            Pace();
            return RazerDeviceHelper.HidD_SetFeature(handle, request, request.Length);
        }

        private static byte[] SendCommand(IntPtr handle, byte reportId, byte[] body, int payloadLength)
        {
            return SendCommand(handle, reportId, body, payloadLength, 5);
        }

        private static byte[] SendCommand(IntPtr handle, byte reportId, byte[] body, int payloadLength, int maxAttempts)
        {
            if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return null;
            if (body == null || body.Length == 0 || body.Length > payloadLength) return null;

            for (int attempt = 0; attempt < maxAttempts; attempt++)
            {
                if (!SendOnly(handle, reportId, body, payloadLength))
                {
                    Thread.Sleep(80);
                    continue;
                }

                Thread.Sleep(95 + attempt * 40);

                byte[] response = new byte[payloadLength + 1];
                response[0] = reportId;
                if (!RazerDeviceHelper.HidD_GetFeature(handle, response, response.Length))
                {
                    Thread.Sleep(70);
                    continue;
                }

                if (response[1] != (byte)(body[0] ^ 0xFF))
                {
                    Thread.Sleep(70);
                    continue;
                }

                byte[] payload = new byte[payloadLength - 1];
                bool any = false;
                for (int i = 0; i < payload.Length; i++)
                {
                    payload[i] = (byte)(response[i + 2] ^ 0xFF);
                    if (payload[i] != 0) any = true;
                }
                if (any) return payload;

                Thread.Sleep(90);
            }
            return null;
        }

        private static string FindControlPath(out int usbPid)
        {
            usbPid = 0;
            string preferred = null;
            int preferredPid = 0;
            string fallback = null;
            int fallbackPid = 0;

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
                    try
                    {
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);
                        if (!RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero)) continue;

                        string devicePath = Marshal.PtrToStringUni(new IntPtr(detailBuffer.ToInt64() + 4));
                        if (string.IsNullOrEmpty(devicePath)) continue;
                        if (devicePath.EndsWith("\\kbd", StringComparison.OrdinalIgnoreCase)) continue;

                        IntPtr handle = RazerDeviceHelper.CreateFile(
                            devicePath,
                            0,
                            FILE_SHARE_READ | FILE_SHARE_WRITE,
                            IntPtr.Zero,
                            OPEN_EXISTING,
                            0,
                            IntPtr.Zero);
                        if (handle == IntPtr.Zero || handle == new IntPtr(-1)) continue;

                        try
                        {
                            RazerDeviceHelper.HIDD_ATTRIBUTES attr = new RazerDeviceHelper.HIDD_ATTRIBUTES();
                            attr.Size = Marshal.SizeOf(attr);
                            if (!RazerDeviceHelper.HidD_GetAttributes(handle, ref attr)) continue;
                            if (attr.VendorID != VID_MCHOSE && attr.VendorID != VID_MCHOSE_ALT) continue;

                            IntPtr preparsed;
                            if (!RazerDeviceHelper.HidD_GetPreparsedData(handle, out preparsed)) continue;
                            RazerDeviceHelper.HIDP_CAPS caps;
                            int capsStatus = RazerDeviceHelper.HidP_GetCaps(preparsed, out caps);
                            RazerDeviceHelper.HidD_FreePreparsedData(preparsed);
                            if (capsStatus != 0x00110000) continue;

                            bool isPreferred = (caps.UsagePage == 0xFF01 && caps.FeatureReportByteLength >= 21);
                            bool isFallback = (caps.UsagePage == 0xFF0B && caps.FeatureReportByteLength >= 21);

                            if (isPreferred)
                            {
                                preferred = devicePath;
                                preferredPid = attr.ProductID;
                            }
                            else if (isFallback && fallback == null)
                            {
                                fallback = devicePath;
                                fallbackPid = attr.ProductID;
                            }

                            if (preferred != null) break;
                        }
                        finally
                        {
                            RazerDeviceHelper.CloseHandle(handle);
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
                RazerDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);
            }

            if (preferred != null)
            {
                usbPid = preferredPid;
                return preferred;
            }
            usbPid = fallbackPid;
            return fallback;
        }

        private static IntPtr OpenControlHandle(out int usbPid)
        {
            string path = FindControlPath(out usbPid);
            if (string.IsNullOrEmpty(path)) return new IntPtr(-1);

            return RazerDeviceHelper.CreateFile(
                path,
                0,
                FILE_SHARE_READ | FILE_SHARE_WRITE,
                IntPtr.Zero,
                OPEN_EXISTING,
                0,
                IntPtr.Zero);
        }

        private static string GetProductString(IntPtr handle)
        {
            StringBuilder sb = new StringBuilder(256);
            if (RazerDeviceHelper.HidD_GetProductString(handle, sb, sb.Capacity * 2))
            {
                return sb.ToString().Trim();
            }
            return "";
        }

        private static bool ReadIdentity(IntPtr handle, out IdentityReading reading)
        {
            reading = new IdentityReading();
            byte[] payload = SendCommand(handle, REPORT_SHORT, new byte[] { CMD_IDENTITY }, 20, 3);
            if (payload == null || payload.Length < 11) return false;

            int vid = payload[0] | (payload[1] << 8);
            int pid = payload[2] | (payload[3] << 8);
            int flags = payload[8];
            int battery = payload[9];
            int charge = payload[10];

            if (vid == 0 && pid == 0) return false;
            if (battery < 0 || battery > 100) return false;

            reading.Vid = vid;
            reading.Pid = pid;
            reading.Flags = flags;
            reading.Battery = battery;
            reading.Charge = charge;
            reading.Firmware = payload[4] | (payload[5] << 8) | (payload[6] << 16) | (payload[7] << 24);
            return true;
        }

        private static byte[] ReadConfigPayload(IntPtr handle)
        {
            for (int attempt = 0; attempt < 3; attempt++)
            {
                byte[] payload = SendCommand(handle, REPORT_LONG, new byte[] { CMD_CONFIG_READ }, 64, 3);
                if (payload != null && payload.Length >= 63)
                {
                    bool any = false;
                    for (int i = 0; i < 63; i++)
                    {
                        if (payload[i] != 0) { any = true; break; }
                    }
                    if (any) return payload;
                }

                // 0x12 长报文通道可能暂时休眠，重新选择 0 号配置唤醒
                SendOnly(handle, REPORT_SHORT, new byte[] { CMD_PROFILE_SELECT, 0x00 }, 20);
                Thread.Sleep(130);
            }
            return null;
        }

        private static bool WriteConfig(IntPtr handle, byte[] config63)
        {
            if (config63 == null || config63.Length < 63) return false;
            byte[] body = new byte[64];
            body[0] = CMD_CONFIG_WRITE;
            Array.Copy(config63, 0, body, 1, 63);
            return SendOnly(handle, REPORT_LONG, body, 64);
        }

        public static List<MouseBatteryInfo> QueryAllMchoseMice()
        {
            List<MouseBatteryInfo> list = new List<MouseBatteryInfo>();
            try
            {
                MouseBatteryInfo dev = QueryMchoseDeviceInfo();
                if (dev != null && (dev.IsConnected || dev.IsDonglePresent))
                {
                    list.Add(dev);
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/MchoseMouseHelper.cs: 已忽略异常", diagEx); }
            return list;
        }

        public static MouseBatteryInfo QueryMchoseDeviceInfo()
        {
            lock (hidLock)
            {
                int usbPid;
                IntPtr handle = OpenControlHandle(out usbPid);
                if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return null;

                try
                {
                    string product = GetProductString(handle);
                    IdentityReading identity;
                    bool hasIdentity = ReadIdentity(handle, out identity);

                    MchoseConfig config = null;
                    if (hasIdentity)
                    {
                        byte[] configPayload = ReadConfigPayload(handle);
                        if (configPayload != null) config = ParseConfigPayload(configPayload);
                    }

                    // 接收器在线但鼠标休眠时仍保留设备卡片，电量回退到最近一次硬件缓存。
                    int mode = hasIdentity ? (identity.Flags & 0x07) : 1;
                    bool linked = hasIdentity && ((identity.Flags & 0x08) != 0);

                    int battery;
                    bool charging;
                    DateTime lastUpdated;
                    if (hasIdentity)
                    {
                        battery = identity.Battery;
                        charging = (identity.Charge == 1);
                        lastUpdated = DateTime.Now;

                        CachedBatteryPercent = battery;
                        CachedIsCharging = charging;
                        CachedLastUpdated = lastUpdated;
                        HasValidHardwareReading = true;
                        string model = GetModelName(identity.Pid, product);
                        if (!string.IsNullOrEmpty(model)) CachedDeviceName = model;
                        SaveHardwareCache();
                    }
                    else
                    {
                        battery = CachedBatteryPercent;
                        charging = CachedIsCharging;
                        lastUpdated = (CachedLastUpdated != DateTime.MinValue) ? CachedLastUpdated : DateTime.Now;
                    }

                    string deviceName = GetModelName(hasIdentity ? identity.Pid : 0, product);
                    if (string.IsNullOrEmpty(deviceName)) deviceName = !string.IsNullOrEmpty(CachedDeviceName) ? CachedDeviceName : "MCHOSE 无线游戏鼠标";

                    int stageIndex = 0;
                    int rateIndex = 2;
                    if (config != null)
                    {
                        stageIndex = (mode == 0) ? config.WiredStage : config.WirelessStage;
                        rateIndex = (mode == 0) ? config.WiredRateIndex : config.WirelessRateIndex;
                    }
                    if (stageIndex < 0 || stageIndex > 5) stageIndex = 0;

                    int dpiX = 0;
                    int dpiY = 0;
                    int[] stagesX = null;
                    int[] stagesY = null;
                    bool xyIndependent = false;
                    if (config != null)
                    {
                        for (int i = 0; i < 6; i++)
                        {
                            if (config.DpiX[i] != config.DpiY[i]) xyIndependent = true;
                        }
                        dpiX = config.DpiX[stageIndex];
                        dpiY = config.DpiY[stageIndex];
                        stagesX = new int[5];
                        stagesY = new int[5];
                        for (int i = 0; i < 5; i++)
                        {
                            stagesX[i] = config.DpiX[i];
                            stagesY[i] = config.DpiY[i];
                        }
                    }

                    int pollingRate = 1000;
                    if (rateIndex >= 0 && rateIndex < RATES.Length) pollingRate = RATES[rateIndex];

                    string fingerprint = string.Format("HID:{0:X4}:{1:X4}", VID_MCHOSE, usbPid);
                    MouseBatteryInfo info = new MouseBatteryInfo();
                    info.DeviceId = string.Format("MCHOSE:{0:X4}:{1:X4}", VID_MCHOSE, usbPid);
                    info.HardwareFingerprint = fingerprint;
                    info.HardwareId = fingerprint;
                    info.Brand = "MCHOSE";
                    info.Category = DeviceCategory.Mouse;
                    info.DeviceName = deviceName;
                    info.Transport = GetTransportName(mode);
                    info.IsCustom = false;
                    info.IsConnected = linked;
                    info.IsSleeping = !linked;
                    info.IsDonglePresent = true;
                    info.BatteryPercent = battery;
                    info.ExactBatteryPercent = battery;
                    info.IsCharging = charging;
                    info.LastUpdated = lastUpdated;
                    info.Dpi = dpiX;
                    info.DpiY = dpiY;
                    info.DpiStage = Math.Min(stageIndex + 1, 5);
                    info.DpiStageCount = 5;
                    info.DpiStages = stagesX;
                    info.DpiStagesY = stagesY;
                    info.IsXyIndependent = xyIndependent;
                    info.XyRatio = (dpiY > 0) ? ((double)dpiX / (double)dpiY) : 1.0;
                    info.PollingRate = pollingRate;
                    info.SupportedPollingRates = GetSupportedPollingRatesForMode(mode);
                    info.MaxPollingRate = GetMaxPollingRateForMode(mode);
                    return info;
                }
                catch
                {
                    return null;
                }
                finally
                {
                    RazerDeviceHelper.CloseHandle(handle);
                }
            }
        }

        private static bool OpenForWrite(out IntPtr handle, out int usbPid)
        {
            handle = OpenControlHandle(out usbPid);
            return (handle != IntPtr.Zero && handle != new IntPtr(-1));
        }

        public static bool SetMchoseDpiStage(int stage)
        {
            if (stage < 1) stage = 1;
            if (stage > 5) stage = 5;

            lock (hidLock)
            {
                int usbPid;
                IntPtr handle;
                if (!OpenForWrite(out handle, out usbPid)) return false;

                try
                {
                    byte[] config = ReadConfigPayload(handle);
                    if (config == null) return false;

                    int idx = stage - 1;
                    config[1] = (byte)((config[1] & 0xF0) | idx);
                    config[2] = (byte)((config[2] & 0xF0) | idx);
                    config[16] = (byte)(config[16] | (1 << idx));

                    if (!WriteConfig(handle, config)) return false;
                    Thread.Sleep(160);

                    byte[] after = ReadConfigPayload(handle);
                    if (after == null) return false;
                    return ((after[1] & 0x0F) == idx) || ((after[2] & 0x0F) == idx);
                }
                catch
                {
                    return false;
                }
                finally
                {
                    RazerDeviceHelper.CloseHandle(handle);
                }
            }
        }

        public static bool SetMchoseDpi(int dpiX, int dpiY, int stage)
        {
            if (dpiX < 100) dpiX = 100;
            if (dpiX > 26000) dpiX = 26000;
            if (dpiY < 100) dpiY = dpiX;
            if (dpiY > 26000) dpiY = 26000;
            if (stage < 1) stage = 1;
            if (stage > 5) stage = 5;

            lock (hidLock)
            {
                int usbPid;
                IntPtr handle;
                if (!OpenForWrite(out handle, out usbPid)) return false;

                try
                {
                    byte[] config = ReadConfigPayload(handle);
                    if (config == null) return false;

                    int idx = stage - 1;
                    config[1] = (byte)((config[1] & 0xF0) | idx);
                    config[2] = (byte)((config[2] & 0xF0) | idx);
                    config[16] = (byte)(config[16] | (1 << idx));

                    config[4 + idx * 2] = (byte)(dpiX & 0xFF);
                    config[5 + idx * 2] = (byte)((dpiX >> 8) & 0xFF);
                    config[51 + idx * 2] = (byte)(dpiY & 0xFF);
                    config[52 + idx * 2] = (byte)((dpiY >> 8) & 0xFF);

                    if (!WriteConfig(handle, config)) return false;
                    Thread.Sleep(160);

                    byte[] after = ReadConfigPayload(handle);
                    if (after == null) return false;

                    int afterX = after[4 + idx * 2] | (after[5 + idx * 2] << 8);
                    int afterY = after[51 + idx * 2] | (after[52 + idx * 2] << 8);
                    return (afterX == dpiX && afterY == dpiY);
                }
                catch
                {
                    return false;
                }
                finally
                {
                    RazerDeviceHelper.CloseHandle(handle);
                }
            }
        }

        private static int GetRateIndex(int hz)
        {
            for (int i = 0; i < RATES.Length; i++)
            {
                if (RATES[i] == hz) return i;
            }

            int best = 2;
            int bestDiff = int.MaxValue;
            for (int i = 0; i < RATES.Length; i++)
            {
                int diff = Math.Abs(RATES[i] - hz);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    best = i;
                }
            }
            return best;
        }

        public static bool SetMchosePollingRate(int hz)
        {
            int idx = GetRateIndex(hz);

            lock (hidLock)
            {
                int usbPid;
                IntPtr handle;
                if (!OpenForWrite(out handle, out usbPid)) return false;

                try
                {
                    byte[] config = ReadConfigPayload(handle);
                    if (config == null) return false;

                    config[1] = (byte)((config[1] & 0x0F) | (idx << 4));
                    config[2] = (byte)((config[2] & 0x0F) | (idx << 4));

                    if (!SendOnly(handle, REPORT_SHORT, new byte[] { CMD_RATE, (byte)idx, (byte)idx }, 20)) return false;
                    Thread.Sleep(150);

                    byte[] after = ReadConfigPayload(handle);
                    if (after == null) return false;
                    return (((after[1] >> 4) & 0x0F) == idx) || (((after[2] >> 4) & 0x0F) == idx);
                }
                catch
                {
                    return false;
                }
                finally
                {
                    RazerDeviceHelper.CloseHandle(handle);
                }
            }
        }
    }
}
