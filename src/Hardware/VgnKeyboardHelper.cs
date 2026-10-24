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
