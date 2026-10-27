// ============================================================================
// 模块名称: VgnKeyboardHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   VGN Neon75 2.4G 键盘接收器与有线键盘的识别、电量/充电状态读取。
//   2.4G Dongle: VID 0x3151 / PID 0x5038，UsagePage 0xFFFF / Usage 0x0002
//   的 65 字节 Feature 端点，0xF7 状态帧 payload[1]=电量、payload[9]=充电标志。
//   有线 Neon75: PID 0x502D / 0x502F / 0x5054，读取 0x8F 身份帧用于型号识别。
// 依赖关系: HidTransport.cs, MouseBatteryInfo.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、协议解析或签名时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    public static class VgnKeyboardHelper
    {
        private static readonly object hidLock = new object();
        private const string CacheRegistryKey = @"Software\RazerBatteryTray\VgnKeyboardCache";

        public const int VgnVid = 0x3151;
        public const int Neon75DonglePid = 0x5038;
        public const int Neon75WiredPid1 = 0x502D;
        public const int Neon75WiredPid2 = 0x502F;
        public const int Neon75WiredPid3 = 0x5054;

        public const byte CmdReceiverStatus = 0xF7;
        public const byte CmdIdentify = 0x8F;
        public const byte CmdGetMainLight = 0x87;
        public const byte CmdSetMainLight = 0x07;
        public const int StatusReportLength = 65;

        public static int CachedBatteryPercent = -1;
        public static bool CachedIsOnline = false;
        public static bool CachedIsCharging = false;
        public static string CachedDeviceName = "VGN Neon75";
        public static DateTime CachedLastUpdated = DateTime.MinValue;
        public static bool CachedWiredMode = false;
        public static int CachedDeviceModelId = 0;

        static VgnKeyboardHelper()
        {
            LoadHardwareCache();
        }

        public static bool IsVgnNeon75(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return false;
            string lower = identifier.ToLowerInvariant();
            if (lower.Contains("vgnkeyboard:")) return true;
            if (lower.Contains("3151:5038") || lower.Contains("vid_3151&pid_5038")) return true;
            if (lower.Contains("3151:502d") || lower.Contains("vid_3151&pid_502d")) return true;
            if (lower.Contains("3151:502f") || lower.Contains("vid_3151&pid_502f")) return true;
            if (lower.Contains("3151:5054") || lower.Contains("vid_3151&pid_5054")) return true;
            return false;
        }

        public static bool IsNeon75Pid(int pid)
        {
            return pid == Neon75DonglePid || pid == Neon75WiredPid1 ||
                   pid == Neon75WiredPid2 || pid == Neon75WiredPid3;
        }

        public static string GetModelName(int pid, int modelId, bool wired)
        {
            if (modelId == 2859) return "VGN Neon75 Pro";
            if (modelId == 2905) return "VGN Neon75 Ultra";
            if (modelId == 2904) return "VGN Neon75 HE";
            if (modelId == 3237 || modelId == 3391) return "VGN Neon75 Extreme";
            if (modelId == 2781) return "VGN Neon75";
            if (pid == Neon75DonglePid) return wired ? "VGN Neon75 (有线)" : "VGN Neon75";
            if (IsNeon75Pid(pid)) return "VGN Neon75 (有线)";
            return "VGN Neon75";
        }

        public static byte[] BuildCommandPacket(byte command)
        {
            byte[] packet = new byte[StatusReportLength];
            packet[0] = 0x00;
            packet[1] = command;
            int checksumIndex = 8;
            int sum = 0;
            for (int i = 0; i < 7; i++)
            {
                sum += packet[1 + i];
            }
            packet[checksumIndex] = (byte)(0xFF - (sum & 0xFF));
            return packet;
        }

        public static byte[] BuildStatusPacket()
        {
            return BuildCommandPacket(CmdReceiverStatus);
        }

        /// <summary>
        /// 解析 0xF7 接收器状态回包。兼容 65 字节带 Report ID 与 64 字节裸帧。
        /// payload[1] 电量；payload[3]==0 在线；payload[6] 设备类型；payload[9] 充电标志。
        /// VGN VHUB 的 getDongleData 同样读取 payload[9] 判断充电，不能再用“满电=充电”。
        /// </summary>
        public static bool TryParseStatus(byte[] report, out int battery, out bool keyboardOnline,
            out bool charging, out bool hasKeyboard, out int deviceKind)
        {
            battery = -1;
            keyboardOnline = false;
            charging = false;
            hasKeyboard = false;
            deviceKind = 0;

            if (report == null || report.Length < 10) return false;

            int offset = (report.Length >= StatusReportLength) ? 1 : 0;
            int kind = report[offset + 6];
            if (kind < 1 || kind > 3)
            {
                offset = 1 - offset;
                kind = report[offset + 6];
            }
            if (kind < 1 || kind > 3) return false;

            int rawBattery = report[offset + 1];
            if (rawBattery > 100) return false;

            battery = rawBattery;
            keyboardOnline = (report[offset + 3] == 0);
            charging = (report[offset + 9] != 0);
            hasKeyboard = (kind == 1 || kind == 3);
            deviceKind = kind;
            return hasKeyboard;
        }

        public static bool TryParseIdentity(byte[] report, out int modelId)
        {
            modelId = 0;
            if (report == null || report.Length < 4) return false;
            int offset = (report.Length >= StatusReportLength) ? 1 : 0;
            modelId = report[offset + 1] | (report[offset + 2] << 8);
            return modelId > 0;
        }

        public static byte[] BuildLightReadPacket(byte command)
        {
            byte[] packet = new byte[StatusReportLength];
            packet[0] = 0x00;
            packet[1] = command;
            int sum = 0;
            for (int i = 1; i <= 7; i++) sum += packet[i];
            packet[8] = (byte)(0xFF - (sum & 0xFF));
            return packet;
        }

        public static byte[] BuildLightWritePacket(byte command, int mode, int speedWire,
            int luminance, int flags, int red, int green, int blue)
        {
            byte[] packet = new byte[StatusReportLength];
            packet[0] = 0x00;
            packet[1] = command;
            packet[2] = (byte)(mode & 0xFF);
            packet[3] = (byte)(speedWire & 0xFF);
            packet[4] = (byte)(luminance & 0xFF);
            packet[5] = (byte)(flags & 0xFF);
            packet[6] = (byte)(red & 0xFF);
            packet[7] = (byte)(green & 0xFF);
            packet[8] = (byte)(blue & 0xFF);
            int sum = 0;
            for (int i = 1; i <= 8; i++) sum += packet[i];
            packet[9] = (byte)(0xFF - (sum & 0xFF));
            return packet;
        }

        /// <summary>
        /// 解析 0x87 主灯状态。回包布局（含 Report ID）：
        /// [0]=0, [1]=0x87, [2]=mode, [3]=wire speed, [4]=luminance,
        /// [5]=flags/direction, [6..8]=R,G,B。
        /// </summary>
        public static bool TryParseMainLight(byte[] report, out int mode, out int wireSpeed,
            out int luminance, out int flags, out int red, out int green, out int blue)
        {
            mode = 0;
            wireSpeed = 0;
            luminance = 0;
            flags = 0;
            red = 0;
            green = 0;
            blue = 0;
            if (report == null || report.Length < 10) return false;

            int offset = (report.Length >= StatusReportLength) ? 1 : 0;
            mode = report[offset + 1];
            wireSpeed = report[offset + 2];
            luminance = report[offset + 3];
            flags = report[offset + 4];
            red = report[offset + 5];
            green = report[offset + 6];
            blue = report[offset + 7];
            return mode <= 25 && wireSpeed <= 7 && luminance <= 7;
        }

        private static HidDeviceDescriptor FindWiredEndpoint(out int pid)
        {
            pid = 0;
            int[] pids = new int[] { Neon75WiredPid1, Neon75WiredPid2, Neon75WiredPid3 };
            for (int p = 0; p < pids.Length; p++)
            {
                List<HidDeviceDescriptor> descriptors = HidTransport.Enumerate(VgnVid, pids[p]);
                if (descriptors == null || descriptors.Count == 0) continue;
                for (int i = 0; i < descriptors.Count; i++)
                {
                    HidDeviceDescriptor d = descriptors[i];
                    if (d.UsagePage == 0xFFFF && d.Usage == 0x0002 && d.FeatureReportLength >= 10)
                    {
                        pid = pids[p];
                        return d;
                    }
                }
            }
            return null;
        }

        public static bool IsWiredNeon75Present()
        {
            int pid;
            return FindWiredEndpoint(out pid) != null;
        }

        public static bool TryReadMainLight(out int mode, out int uiSpeed, out int luminance,
            out bool chroma, out int red, out int green, out int blue)
        {
            mode = 0;
            uiSpeed = 0;
            luminance = 0;
            chroma = false;
            red = 0;
            green = 0;
            blue = 0;

            int pid;
            HidDeviceDescriptor endpoint = FindWiredEndpoint(out pid);
            if (endpoint == null) return false;

            IntPtr handle = HidTransport.Open(endpoint.DevicePath);
            if (!HidTransport.IsValidHandle(handle)) return false;
            try
            {
                byte[] tx = BuildLightReadPacket(CmdGetMainLight);
                if (!HidTransport.SetFeature(handle, tx)) return false;
                Thread.Sleep(40);
                byte[] rx = new byte[StatusReportLength];
                if (!HidTransport.GetFeature(handle, rx)) return false;

                int wireSpeed;
                int flags;
                bool ok = TryParseMainLight(rx, out mode, out wireSpeed, out luminance,
                    out flags, out red, out green, out blue);
                if (!ok) return false;
                uiSpeed = 4 - Math.Max(0, Math.Min(4, wireSpeed));
                chroma = (flags == 8);
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                HidTransport.Close(handle);
            }
        }

        /// <summary>
        /// 写入主灯并回读校验。uiSpeed 0=最慢、4=最快；硬件 wire speed = 4 - uiSpeed。
        /// 实测：mode=1/flags=7/RGB 可回读；mode=0 可关闭；原状态可无损恢复。
        /// </summary>
        public static bool SetMainLight(int mode, int uiSpeed, int luminance, bool chroma,
            int red, int green, int blue, out string error)
        {
            int wireSpeed = 4 - Math.Max(0, Math.Min(4, uiSpeed));
            int lum = Math.Max(0, Math.Min(4, luminance));
            int flags = chroma ? 8 : 7;
            return SetMainLightRaw(mode, wireSpeed, lum, flags, red, green, blue, out error);
        }

        public static bool SetMainLightRaw(int mode, int wireSpeed, int luminance, int flags,
            int red, int green, int blue, out string error)
        {
            error = null;
            int pid;
            HidDeviceDescriptor endpoint = FindWiredEndpoint(out pid);
            if (endpoint == null)
            {
                error = "未检测到 VGN Neon75 有线端点（请把模式开关拨到 Wired 档）";
                return false;
            }

            int safeSpeed = Math.Max(0, Math.Min(7, wireSpeed));
            int lum = Math.Max(0, Math.Min(4, luminance));
            int safeFlags = flags & 0xFF;
            int targetMode = Math.Max(0, Math.Min(25, mode));

            IntPtr handle = HidTransport.Open(endpoint.DevicePath);
            if (!HidTransport.IsValidHandle(handle))
            {
                error = "无法打开键盘配置端点";
                return false;
            }
            try
            {
                byte[] tx = BuildLightWritePacket(CmdSetMainLight, targetMode, safeSpeed, lum,
                    safeFlags, red, green, blue);
                if (!HidTransport.SetFeature(handle, tx))
                {
                    error = "SetFeature 写入失败";
                    return false;
                }
                Thread.Sleep(80);

                byte[] rx = new byte[StatusReportLength];
                if (!HidTransport.GetFeature(handle, rx))
                {
                    error = "写入后回读失败";
                    return false;
                }

                int rMode, rSpeed, rLum, rFlags, rRed, rGreen, rBlue;
                if (!TryParseMainLight(rx, out rMode, out rSpeed, out rLum, out rFlags,
                    out rRed, out rGreen, out rBlue))
                {
                    error = "回读报文解析失败";
                    return false;
                }
                if (rMode != targetMode)
                {
                    error = string.Format("回读模式不一致：写入 {0}，回读 {1}", targetMode, rMode);
                    return false;
                }
                if (targetMode == 1 &&
                    (rLum != lum || rSpeed != safeSpeed || rRed != red || rGreen != green || rBlue != blue))
                {
                    error = string.Format("常亮回读不一致：lum={0}/{1}, speed={2}/{3}, RGB={4},{5},{6}",
                        rLum, lum, rSpeed, safeSpeed, rRed, rGreen, rBlue);
                    return false;
                }
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
            finally
            {
                HidTransport.Close(handle);
            }
        }

        public static MouseBatteryInfo QueryVgnKeyboardInfo()
        {
            lock (hidLock)
            {
                // 有线优先：设置通道只在有线端点可用；Dongle 仅用于无线电量。
                MouseBatteryInfo wiredInfo = QueryWiredInfo();
                if (wiredInfo != null) return wiredInfo;
                return QueryDongleInfo();
            }
        }

        private static MouseBatteryInfo QueryDongleInfo()
        {
            List<HidDeviceDescriptor> descriptors = HidTransport.Enumerate(VgnVid, Neon75DonglePid);
            if (descriptors == null || descriptors.Count == 0) return null;

            HidDeviceDescriptor endpoint = null;
            for (int i = 0; i < descriptors.Count; i++)
            {
                HidDeviceDescriptor d = descriptors[i];
                if (d.UsagePage == 0xFFFF && d.Usage == 0x0002 && d.FeatureReportLength >= 10)
                {
                    endpoint = d;
                    break;
                }
            }
            if (endpoint == null) return null;

            bool parsed = false;
            int battery = -1;
            bool online = false;
            bool charging = false;
            bool hasKeyboard = false;
            int deviceKind = 0;

            IntPtr handle = HidTransport.Open(endpoint.DevicePath);
            if (HidTransport.IsValidHandle(handle))
            {
                try
                {
                    byte[] tx = BuildStatusPacket();
                    if (HidTransport.SetFeature(handle, tx))
                    {
                        Thread.Sleep(35);
                        byte[] rx = new byte[StatusReportLength];
                        if (HidTransport.GetFeature(handle, rx))
                        {
                            parsed = TryParseStatus(rx, out battery, out online, out charging,
                                out hasKeyboard, out deviceKind);
                        }
                    }
                }
                catch
                {
                    parsed = false;
                }
                finally
                {
                    HidTransport.Close(handle);
                }
            }

            if (parsed)
            {
                CachedBatteryPercent = battery;
                CachedIsOnline = online;
                CachedIsCharging = charging;
                CachedWiredMode = false;
                CachedLastUpdated = DateTime.Now;
            }

            string modelName = GetModelName(Neon75DonglePid, 0, false);
            CachedDeviceName = modelName;

            MouseBatteryInfo info = new MouseBatteryInfo();
            info.DeviceId = "VGN:3151:5038:KB";
            info.HardwareFingerprint = "HID:3151:5038";
            info.DeviceName = modelName;
            info.Brand = "VGN";
            info.Category = DeviceCategory.Keyboard;
            info.Transport = "2.4G 无线";
            info.IsDonglePresent = true;
            info.IsConnected = parsed ? online : true;
            info.IsSleeping = parsed ? (!online) : false;
            info.IsCharging = parsed ? charging : CachedIsCharging;
            info.BatteryPercent = parsed ? battery : CachedBatteryPercent;
            info.LastUpdated = parsed ? DateTime.Now : CachedLastUpdated;
            info.CustomTooltip = "VGN Neon75 Dongle：0xF7 状态帧 payload[1]=电量，payload[9]=充电；2.4G 不转发灯光/按键设置";

            SaveHardwareCache();
            return info;
        }

        private static MouseBatteryInfo QueryWiredInfo()
        {
            int[] pids = new int[] { Neon75WiredPid1, Neon75WiredPid2, Neon75WiredPid3 };
            for (int p = 0; p < pids.Length; p++)
            {
                List<HidDeviceDescriptor> descriptors = HidTransport.Enumerate(VgnVid, pids[p]);
                if (descriptors == null || descriptors.Count == 0) continue;

                HidDeviceDescriptor endpoint = null;
                for (int i = 0; i < descriptors.Count; i++)
                {
                    HidDeviceDescriptor d = descriptors[i];
                    if (d.UsagePage == 0xFFFF && d.Usage == 0x0002 && d.FeatureReportLength >= 10)
                    {
                        endpoint = d;
                        break;
                    }
                }
                if (endpoint == null) continue;

                int modelId = 0;
                IntPtr handle = HidTransport.Open(endpoint.DevicePath);
                if (HidTransport.IsValidHandle(handle))
                {
                    try
                    {
                        byte[] tx = BuildCommandPacket(CmdIdentify);
                        if (HidTransport.SetFeature(handle, tx))
                        {
                            Thread.Sleep(35);
                            byte[] rx = new byte[StatusReportLength];
                            if (HidTransport.GetFeature(handle, rx))
                            {
                                TryParseIdentity(rx, out modelId);
                            }
                        }
                    }
                    catch { }
                    finally
                    {
                        HidTransport.Close(handle);
                    }
                }

                string modelName = GetModelName(pids[p], modelId, true);
                CachedDeviceName = modelName;
                CachedWiredMode = true;
                CachedDeviceModelId = modelId;
                CachedIsCharging = true;
                CachedLastUpdated = DateTime.Now;

                MouseBatteryInfo info = new MouseBatteryInfo();
                info.DeviceId = string.Format("VGN:3151:{0:X4}:KB", pids[p]);
                info.HardwareFingerprint = string.Format("HID:3151:{0:X4}", pids[p]);
                info.DeviceName = modelName;
                info.Brand = "VGN";
                info.Category = DeviceCategory.Keyboard;
                info.Transport = "USB 有线";
                info.IsDonglePresent = false;
                info.IsConnected = true;
                info.IsSleeping = false;
                info.IsCharging = true;
                info.BatteryPercent = CachedBatteryPercent;
                info.LastUpdated = DateTime.Now;
                info.CustomTooltip = "VGN Neon75 有线模式：键盘设置通道可用；FerrisPulse 当前先提供电量/型号识别";

                SaveHardwareCache();
                return info;
            }
            return null;
        }

        public static void LoadHardwareCache()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(CacheRegistryKey, false))
                {
                    if (key != null)
                    {
                        object bp = key.GetValue("BatteryPercent");
                        if (bp != null) CachedBatteryPercent = Convert.ToInt32(bp);

                        object online = key.GetValue("IsOnline");
                        if (online != null) CachedIsOnline = (Convert.ToInt32(online) == 1);

                        object charging = key.GetValue("IsCharging");
                        if (charging != null) CachedIsCharging = (Convert.ToInt32(charging) == 1);

                        object wired = key.GetValue("WiredMode");
                        if (wired != null) CachedWiredMode = (Convert.ToInt32(wired) == 1);

                        object model = key.GetValue("DeviceModelId");
                        if (model != null) CachedDeviceModelId = Convert.ToInt32(model);

                        string name = key.GetValue("DeviceName") as string;
                        if (!string.IsNullOrEmpty(name)) CachedDeviceName = name;

                        string updated = key.GetValue("LastUpdated") as string;
                        if (!string.IsNullOrEmpty(updated))
                        {
                            DateTime dt;
                            if (DateTime.TryParse(updated, out dt)) CachedLastUpdated = dt;
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
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(CacheRegistryKey))
                {
                    if (key != null)
                    {
                        if (CachedBatteryPercent >= 0) key.SetValue("BatteryPercent", CachedBatteryPercent);
                        key.SetValue("IsOnline", CachedIsOnline ? 1 : 0);
                        key.SetValue("IsCharging", CachedIsCharging ? 1 : 0);
                        key.SetValue("WiredMode", CachedWiredMode ? 1 : 0);
                        key.SetValue("DeviceModelId", CachedDeviceModelId);
                        if (!string.IsNullOrEmpty(CachedDeviceName)) key.SetValue("DeviceName", CachedDeviceName);
                        if (CachedLastUpdated != DateTime.MinValue) key.SetValue("LastUpdated", CachedLastUpdated.ToString("o"));
                    }
                }
            }
            catch { }
        }
    }
}