// ============================================================================
// 模块名称: ValkyrieDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   瓦尔基里 (Valkyrie) 全系列无线/有线机械键盘与电竞鼠标私有 HID 协议通信实现
// 依赖关系: NativeMethods.cs, RazerDeviceHelper.cs, MouseBatteryInfo.cs, Models/
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
    public static class ValkyrieDeviceHelper
    {
        private static readonly object hidLock = new object();
        private const string CacheRegistryKey = @"Software\RazerBatteryTray\ValkyrieCache";

        public const int PrimaryVid = 0x374A; // 瓦尔基里官方主要厂商代码 (14154)
        public const int OemVid1 = 0x3151;    // 融渊 / 代工方案 VID (12625)
        public const int OemVid2 = 0x347A;    // 代工方案 VID (13434)

        public static int CachedKbBattery = -1;
        public static bool CachedKbOnline = false;
        public static string CachedKbName = "VK 99 Max V2";

        public static int CachedMouseBattery = -1;
        public static bool CachedMouseOnline = false;
        public static string CachedMouseName = "VK M5 PRO";

        public static DateTime CachedLastUpdated = DateTime.MinValue;

        public static string LastMouseDevicePath = null;
        public static int CachedPollingRate = 1000;
        public static int CachedDpi = 800;
        public static int CachedDpiY = 800;
        public static int CachedDpiStage = 1;
        public static int CachedDpiCount = 5;
        public static int[] CachedDpiStages = null;
        public static int[] CachedDpiStagesY = null;
        public static bool CachedIsXyIndependent = false;
        public static double CachedXyRatio = 1.0;

        public static bool IsValkyrieDevice(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return false;
            string lower = identifier.ToLowerInvariant();
            if (lower.Contains("vid_374a") || lower.Contains("374a:") || lower.Contains("valkyrie") || lower.Contains("vkms"))
            {
                return true;
            }

            // 代工接收器与键盘专用识别
            if (lower.Contains("vid_3151") || lower.Contains("3151:"))
            {
                if (lower.Contains("pid_5007") || lower.Contains("pid_5020") || lower.Contains("pid_503f") ||
                    lower.Contains("pid_5040") || lower.Contains("pid_504b") || lower.Contains("5007") ||
                    lower.Contains("5020") || lower.Contains("503f") || lower.Contains("5040") || lower.Contains("504b"))
                {
                    return true;
                }
            }

            return false;
        }

        public static string GetModelName(int vid, int pid, string productString, DeviceCategory category)
        {
            if (!string.IsNullOrEmpty(productString))
            {
                string cleaned = productString.Replace("Dongle", "").Replace("Receiver", "").Trim();
                if (!string.IsNullOrEmpty(cleaned) && !cleaned.StartsWith("HID", StringComparison.OrdinalIgnoreCase))
                {
                    return cleaned;
                }
            }

            // 键盘系列官方型号映射
            switch (pid)
            {
                case 0xA280: return "VK 99 Max V2";
                case 0xA277: return "VK 99 V2";
                case 0xA260: return "VK 99 MAX";
                case 0xA262: return "VK99 Ultra";
                case 0xA219: return "VK99 Gaming";
                case 0xA270: return "VK 87 Max V2";
                case 0xA267: return "VK 87 V2";
                case 0xA274: return "VK MAG87 Max";
                case 0xA250: return "VK 75 PRO";
                case 0xA283: return "VK 75";
                case 0xA293: return "VK 108";
                case 0xA286: return "VK NB 80 Pro";
                case 0xA216: return "VK MAG75 Pro";
                case 0xA213: return "VK MAG75 Lite";
                case 0xA225: return "VK MAG68";
                case 0xA232: return "VK MAG 68";
                case 0xA233: return "VK MAG 68 Max";
                case 0xA264: return "VK Cyber 68";
                case 0xA242: return "Mecha 75";
                case 0xA228: return "NB68";
                case 0xA236: return "NB68 Max";
            }

            // 鼠标系列官方型号映射
            switch (pid)
            {
                case 0xA321: return "VK M5";
                case 0xA318: return "VK M5 Lite";
                case 0xA328: return "VK M5 PRO";
                case 0xA333: return "VK M5 EVO";
                case 0xA336: return "VK M5 Super";
                case 0xA351: return "VK M5 Ultra";
                case 0xA339: return "VK M5 MAX";
                case 0xA354: return "VK M7 PRO";
                case 0xA345: return "VK M4";
                case 0xA342: return "VK M4 Lite";
                case 0x0331: return "VK M3 Basic";
            }

            // 接收器型号映射
            switch (pid)
            {
                case 0xA329: return "VK M5 PRO";
                case 0xA352: return "VK M5 Ultra";
                case 0xA340: return "VK M5 MAX";
                case 0xA355: return "VK M7 PRO";
                case 0xA322: return "瓦尔基里 2.4G 接收器";
            }

            if (category == DeviceCategory.Keyboard) return "瓦尔基里无线键盘";
            if (category == DeviceCategory.Mouse) return "瓦尔基里无线鼠标";
            return "瓦尔基里外设";
        }

        public static void LoadHardwareCache()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(CacheRegistryKey, false))
                {
                    if (key != null)
                    {
                        object kbBatt = key.GetValue("KbBatteryPercent");
                        if (kbBatt != null) CachedKbBattery = Convert.ToInt32(kbBatt);

                        object mBatt = key.GetValue("MouseBatteryPercent");
                        if (mBatt != null) CachedMouseBattery = Convert.ToInt32(mBatt);

                        string kbName = key.GetValue("KbDeviceName") as string;
                        if (!string.IsNullOrEmpty(kbName)) CachedKbName = kbName;

                        string mName = key.GetValue("MouseDeviceName") as string;
                        if (!string.IsNullOrEmpty(mName)) CachedMouseName = mName;

                        string updatedStr = key.GetValue("LastUpdated") as string;
                        if (!string.IsNullOrEmpty(updatedStr))
                        {
                            DateTime dt;
                            if (DateTime.TryParse(updatedStr, out dt)) CachedLastUpdated = dt;
                        }

                        object dpi = key.GetValue("Dpi");
                        if (dpi != null) CachedDpi = Convert.ToInt32(dpi);

                        object dpiY = key.GetValue("DpiY");
                        if (dpiY != null) CachedDpiY = Convert.ToInt32(dpiY);

                        object st = key.GetValue("DpiStage");
                        if (st != null) CachedDpiStage = Convert.ToInt32(st);

                        object count = key.GetValue("DpiCount");
                        if (count != null) CachedDpiCount = Convert.ToInt32(count);

                        string stStr = key.GetValue("DpiStages") as string;
                        if (!string.IsNullOrEmpty(stStr))
                        {
                            string[] parts = stStr.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                            CachedDpiStages = new int[parts.Length];
                            for (int i = 0; i < parts.Length; i++)
                            {
                                int p;
                                if (int.TryParse(parts[i], out p)) CachedDpiStages[i] = p;
                            }
                        }

                        string stStrY = key.GetValue("DpiStagesY") as string;
                        if (!string.IsNullOrEmpty(stStrY))
                        {
                            string[] partsY = stStrY.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                            CachedDpiStagesY = new int[partsY.Length];
                            for (int i = 0; i < partsY.Length; i++)
                            {
                                int p;
                                if (int.TryParse(partsY[i], out p)) CachedDpiStagesY[i] = p;
                            }
                        }

                        object isXy = key.GetValue("IsXyIndependent");
                        if (isXy != null) CachedIsXyIndependent = (Convert.ToInt32(isXy) == 1);

                        object xyR = key.GetValue("XyRatio");
                        if (xyR != null)
                        {
                            double r;
                            if (double.TryParse(xyR.ToString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out r))
                                CachedXyRatio = r;
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
                        if (CachedKbBattery >= 0) key.SetValue("KbBatteryPercent", CachedKbBattery);
                        if (CachedMouseBattery >= 0) key.SetValue("MouseBatteryPercent", CachedMouseBattery);
                        if (!string.IsNullOrEmpty(CachedKbName)) key.SetValue("KbDeviceName", CachedKbName);
                        if (!string.IsNullOrEmpty(CachedMouseName)) key.SetValue("MouseDeviceName", CachedMouseName);
                        if (CachedDpi > 0) key.SetValue("Dpi", CachedDpi);
                        if (CachedDpiY > 0) key.SetValue("DpiY", CachedDpiY);
                        if (CachedDpiStage > 0) key.SetValue("DpiStage", CachedDpiStage);
                        if (CachedDpiCount > 0) key.SetValue("DpiCount", CachedDpiCount);
                        if (CachedDpiStages != null && CachedDpiStages.Length > 0)
                            key.SetValue("DpiStages", string.Join(",", Array.ConvertAll(CachedDpiStages, s => s.ToString())));
                        if (CachedDpiStagesY != null && CachedDpiStagesY.Length > 0)
                            key.SetValue("DpiStagesY", string.Join(",", Array.ConvertAll(CachedDpiStagesY, s => s.ToString())));
                        key.SetValue("IsXyIndependent", CachedIsXyIndependent ? 1 : 0);
                        key.SetValue("XyRatio", CachedXyRatio.ToString("0.000", System.Globalization.CultureInfo.InvariantCulture));
                        if (CachedLastUpdated != DateTime.MinValue) key.SetValue("LastUpdated", CachedLastUpdated.ToString("o"));
                    }
                }
            }
            catch { }
        }

        public static byte[] SendValkyrieCommand(IntPtr handle, byte cmd, int waitMs)
        {
            if (handle == IntPtr.Zero || handle.ToInt64() == -1) return null;

            try
            {
                byte[] tx = new byte[65];
                tx[0] = 0x00; // Report ID 0x00
                tx[1] = cmd;  // 0xF7: 2.4G 状态及电量查询

                bool setOk = RazerDeviceHelper.HidD_SetFeature(handle, tx, tx.Length);
                if (!setOk)
                {
                    // 若不支持 Report ID 0 前导，回退首字节 0xF7
                    byte[] txDirect = new byte[64];
                    txDirect[0] = cmd;
                    RazerDeviceHelper.HidD_SetFeature(handle, txDirect, txDirect.Length);
                }

                if (waitMs > 0)
                {
                    Thread.Sleep(waitMs);
                }

                byte[] rx = new byte[65];
                bool getOk = RazerDeviceHelper.HidD_GetFeature(handle, rx, rx.Length);
                if (getOk)
                {
                    return rx;
                }

                // 回退 64 字节读取
                byte[] rx64 = new byte[64];
                if (RazerDeviceHelper.HidD_GetFeature(handle, rx64, rx64.Length))
                {
                    byte[] res = new byte[65];
                    Array.Copy(rx64, 0, res, 1, 64);
                    return res;
                }
            }
            catch { }

            return null;
        }

        public static int DecodeReportRate(byte raw)
        {
            switch (raw)
            {
                case 129: return 8000;
                case 130: return 4000;
                case 132: return 2000;
                case 1:   return 1000;
                case 2:   return 500;
                case 4:   return 250;
                case 8:   return 125;
            }
            return 0;
        }

        public static byte EncodeReportRate(int hz)
        {
            switch (hz)
            {
                case 8000: return 129;
                case 4000: return 130;
                case 2000: return 132;
                case 1000: return 1;
                case 500:  return 2;
                case 250:  return 4;
                case 125:  return 8;
            }
            return 1;
        }

        public static int QueryPollingRateFromHandle(IntPtr handle)
        {
            try
            {
                byte[] tx = new byte[65];
                tx[0] = 0x00;
                tx[1] = 0xD3; // FEA_CMD_MOUSE_GET_OPTIONPARAM0
                if (!RazerDeviceHelper.HidD_SetFeature(handle, tx, tx.Length))
                {
                    byte[] tx64 = new byte[64];
                    tx64[0] = 0xD3;
                    RazerDeviceHelper.HidD_SetFeature(handle, tx64, tx64.Length);
                }

                Thread.Sleep(10);
                byte[] rx = new byte[65];
                if (RazerDeviceHelper.HidD_GetFeature(handle, rx, rx.Length))
                {
                    int offset = (rx[0] == 0x00) ? 1 : 0;
                    byte raw = rx[offset + 9];
                    int hz = DecodeReportRate(raw);
                    if (hz > 0) return hz;
                }
            }
            catch { }
            return 0;
        }

        public static bool FastQueryDpi(out int dpi, out int stage, out int count)
        {
            int dy;
            return FastQueryDpi(out dpi, out dy, out stage, out count);
        }

        public static bool FastQueryDpi(out int dpiX, out int dpiY, out int stage, out int count)
        {
            dpiX = CachedDpi;
            dpiY = CachedDpiY;
            stage = CachedDpiStage;
            count = CachedDpiCount;
            if (string.IsNullOrEmpty(LastMouseDevicePath)) return false;

            IntPtr handle = RazerDeviceHelper.CreateFile(
                LastMouseDevicePath,
                0,
                RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                IntPtr.Zero,
                RazerDeviceHelper.OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;

            try
            {
                byte[] tx = new byte[65];
                tx[0] = 0x00;
                tx[1] = 0xD4; // FEA_CMD_MOUSE_GET_OPTIONPARAM1
                tx[2] = 0x00; // Profile 0
                if (!RazerDeviceHelper.HidD_SetFeature(handle, tx, tx.Length))
                {
                    byte[] tx64 = new byte[64];
                    tx64[0] = 0xD4;
                    tx64[1] = 0x00;
                    RazerDeviceHelper.HidD_SetFeature(handle, tx64, tx64.Length);
                }

                Thread.Sleep(10);
                byte[] rx = new byte[65];
                if (RazerDeviceHelper.HidD_GetFeature(handle, rx, rx.Length))
                {
                    int offset = (rx[0] == 0x00) ? 1 : 0;
                    int curIdx = rx[offset + 2];
                    if (curIdx > 8) curIdx = 0;
                    int c = rx[offset + 3];
                    if (c > 0 && c <= 8)
                    {
                        int dX = rx[offset + 8 + curIdx * 2] | (rx[offset + 9 + curIdx * 2] << 8);
                        int dY = rx[offset + 24 + curIdx * 2] | (rx[offset + 25 + curIdx * 2] << 8);
                        if (dY <= 0) dY = dX;
                        if (dX >= 100 && dX <= 40000)
                        {
                            dpiX = dX;
                            dpiY = dY;
                            stage = curIdx + 1;
                            count = c;
                            CachedDpi = dpiX;
                            CachedDpiY = dpiY;
                            CachedDpiStage = stage;
                            CachedDpiCount = count;

                            int[] stX = new int[c];
                            int[] stY = new int[c];
                            bool anyDiff = false;
                            for (int i = 0; i < c; i++)
                            {
                                int sx = rx[offset + 8 + i * 2] | (rx[offset + 9 + i * 2] << 8);
                                int sy = rx[offset + 24 + i * 2] | (rx[offset + 25 + i * 2] << 8);
                                if (sy <= 0) sy = sx;
                                stX[i] = sx;
                                stY[i] = sy;
                                if (sx != sy) anyDiff = true;
                            }
                            CachedDpiStages = stX;
                            CachedDpiStagesY = stY;
                            CachedIsXyIndependent = (dpiX != dpiY) || anyDiff;
                            CachedXyRatio = (dpiX > 0) ? ((double)dpiY / dpiX) : 1.0;
                            return true;
                        }
                    }
                }
            }
            catch { }
            finally
            {
                RazerDeviceHelper.CloseHandle(handle);
            }

            return false;
        }

        public static byte[] BuildOption1Payload(int activeStage, int[] stagesX, int[] stagesY)
        {
            byte[] txSet = new byte[65];
            txSet[0] = 0x00;
            txSet[1] = 0x54; // FEA_CMD_MOUSE_SET_OPTIONPARAM1
            txSet[2] = 0x00; // Profile 0
            int curIdx = Math.Max(0, activeStage - 1);
            txSet[3] = (byte)curIdx;
            int count = (stagesX != null && stagesX.Length > 0) ? Math.Min(8, stagesX.Length) : 5;
            txSet[4] = (byte)count;

            for (int i = 0; i < count; i++)
            {
                int sx = (stagesX != null && i < stagesX.Length) ? stagesX[i] : 800;
                int sy = (stagesY != null && i < stagesY.Length) ? stagesY[i] : sx;
                txSet[1 + 8 + i * 2] = (byte)(sx & 0xFF);
                txSet[1 + 9 + i * 2] = (byte)((sx >> 8) & 0xFF);
                txSet[1 + 24 + i * 2] = (byte)(sy & 0xFF);
                txSet[1 + 25 + i * 2] = (byte)((sy >> 8) & 0xFF);
            }
            return txSet;
        }

        public static bool UpdateValkyrieHardwareStages(int activeStage, int[] stages)
        {
            return UpdateValkyrieHardwareStages(activeStage, stages, stages);
        }

        public static bool UpdateValkyrieHardwareStages(int activeStage, int[] stagesX, int[] stagesY)
        {
            if (stagesX == null || stagesX.Length == 0) return false;
            if (stagesY == null || stagesY.Length < stagesX.Length) stagesY = stagesX;
            if (string.IsNullOrEmpty(LastMouseDevicePath)) return false;

            IntPtr handle = RazerDeviceHelper.CreateFile(
                LastMouseDevicePath,
                0,
                RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                IntPtr.Zero,
                RazerDeviceHelper.OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;

            try
            {
                byte[] txSet = BuildOption1Payload(activeStage, stagesX, stagesY);
                bool ok = RazerDeviceHelper.HidD_SetFeature(handle, txSet, txSet.Length);
                if (!ok)
                {
                    byte[] tx64 = new byte[64];
                    Array.Copy(txSet, 1, tx64, 0, 64);
                    ok = RazerDeviceHelper.HidD_SetFeature(handle, tx64, tx64.Length);
                }

                if (ok)
                {
                    int idx = Math.Max(0, Math.Min(activeStage - 1, stagesX.Length - 1));
                    CachedDpi = stagesX[idx];
                    CachedDpiY = stagesY[idx];
                    CachedDpiStage = idx + 1;
                    CachedDpiCount = stagesX.Length;
                    CachedDpiStages = stagesX;
                    CachedDpiStagesY = stagesY;
                    CachedIsXyIndependent = (CachedDpi != CachedDpiY);
                    CachedXyRatio = (CachedDpi > 0) ? ((double)CachedDpiY / CachedDpi) : 1.0;
                    SaveHardwareCache();
                    return true;
                }
            }
            catch { }
            finally
            {
                RazerDeviceHelper.CloseHandle(handle);
            }

            return false;
        }

        public static bool SetValkyriePollingRate(int hz)
        {
            if (string.IsNullOrEmpty(LastMouseDevicePath)) return false;

            IntPtr handle = RazerDeviceHelper.CreateFile(
                LastMouseDevicePath,
                0,
                RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                IntPtr.Zero,
                RazerDeviceHelper.OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;

            try
            {
                byte code = EncodeReportRate(hz);
                byte[] tx = new byte[65];
                tx[0] = 0x00;
                tx[1] = 0x04; // FEA_CMD_SET_REPORT
                tx[2] = 0x00; // Profile 0
                tx[3] = code;

                bool ok = RazerDeviceHelper.HidD_SetFeature(handle, tx, tx.Length);
                if (!ok)
                {
                    byte[] tx64 = new byte[64];
                    tx64[0] = 0x04;
                    tx64[1] = 0x00;
                    tx64[2] = code;
                    ok = RazerDeviceHelper.HidD_SetFeature(handle, tx64, tx64.Length);
                }

                if (ok)
                {
                    CachedPollingRate = hz;
                    return true;
                }
            }
            catch { }
            finally
            {
                RazerDeviceHelper.CloseHandle(handle);
            }

            return false;
        }

        public static bool SetValkyrieDpi(int dpi)
        {
            return SetValkyrieDpi(dpi, dpi);
        }

        public static bool SetValkyrieDpi(int dpiX, int dpiY)
        {
            if (string.IsNullOrEmpty(LastMouseDevicePath)) return false;

            IntPtr handle = RazerDeviceHelper.CreateFile(
                LastMouseDevicePath,
                0,
                RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                IntPtr.Zero,
                RazerDeviceHelper.OPEN_EXISTING,
                0,
                IntPtr.Zero);

            if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;

            try
            {
                // First read Option1 to preserve other stages
                byte[] txGet = new byte[65];
                txGet[0] = 0x00;
                txGet[1] = 0xD4;
                txGet[2] = 0x00;
                RazerDeviceHelper.HidD_SetFeature(handle, txGet, txGet.Length);
                Thread.Sleep(10);
                byte[] rx = new byte[65];
                if (RazerDeviceHelper.HidD_GetFeature(handle, rx, rx.Length))
                {
                    int offset = (rx[0] == 0x00) ? 1 : 0;
                    byte[] txSet = new byte[65];
                    txSet[0] = 0x00;
                    txSet[1] = 0x54; // FEA_CMD_MOUSE_SET_OPTIONPARAM1
                    txSet[2] = 0x00; // Profile 0
                    int curIdx = rx[offset + 2];
                    if (curIdx > 8) curIdx = 0;
                    txSet[3] = (byte)curIdx;
                    int count = rx[offset + 3];
                    if (count <= 0 || count > 8) count = 5;
                    txSet[4] = (byte)count;

                    // Copy existing DPIs
                    for (int i = 0; i < count; i++)
                    {
                        int srcX = rx[offset + 8 + i * 2] | (rx[offset + 9 + i * 2] << 8);
                        int srcY = rx[offset + 24 + i * 2] | (rx[offset + 25 + i * 2] << 8);
                        if (srcY <= 0) srcY = srcX;
                        if (i == curIdx)
                        {
                            srcX = dpiX;
                            srcY = dpiY;
                        }
                        txSet[1 + 8 + i * 2] = (byte)(srcX & 0xFF);
                        txSet[1 + 9 + i * 2] = (byte)((srcX >> 8) & 0xFF);
                        txSet[1 + 24 + i * 2] = (byte)(srcY & 0xFF);
                        txSet[1 + 25 + i * 2] = (byte)((srcY >> 8) & 0xFF);
                    }

                    bool ok = RazerDeviceHelper.HidD_SetFeature(handle, txSet, txSet.Length);
                    if (!ok)
                    {
                        byte[] tx64 = new byte[64];
                        Array.Copy(txSet, 1, tx64, 0, 64);
                        ok = RazerDeviceHelper.HidD_SetFeature(handle, tx64, tx64.Length);
                    }
                    if (ok)
                    {
                        CachedDpi = dpiX;
                        CachedDpiY = dpiY;
                        CachedIsXyIndependent = (dpiX != dpiY);
                        CachedXyRatio = (dpiX > 0) ? ((double)dpiY / dpiX) : 1.0;
                        SaveHardwareCache();
                        return true;
                    }
                }
            }
            catch { }
            finally
            {
                RazerDeviceHelper.CloseHandle(handle);
            }

            return false;
        }

        public static List<MouseBatteryInfo> QueryAllValkyrieDevices()
        {
            List<MouseBatteryInfo> list = new List<MouseBatteryInfo>();

            lock (hidLock)
            {
                Guid hidGuid;
                RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
                IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return list;

                RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                uint memberIdx = 0;
                HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                            if (devicePath != null && !seenPaths.Contains(devicePath) && IsValkyrieDevice(devicePath))
                            {
                                seenPaths.Add(devicePath);

                                IntPtr handle = RazerDeviceHelper.CreateFile(
                                    devicePath,
                                    0, // 查询句柄，无需独占读写权限
                                    RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                                    IntPtr.Zero,
                                    RazerDeviceHelper.OPEN_EXISTING,
                                    0,
                                    IntPtr.Zero);

                                if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                {
                                    try
                                    {
                                        RazerDeviceHelper.HIDD_ATTRIBUTES attr = new RazerDeviceHelper.HIDD_ATTRIBUTES();
                                        attr.Size = Marshal.SizeOf(attr);
                                        RazerDeviceHelper.HidD_GetAttributes(handle, ref attr);

                                        StringBuilder sbProd = new StringBuilder(256);
                                        string prodStr = RazerDeviceHelper.HidD_GetProductString(handle, sbProd, sbProd.Capacity) ? sbProd.ToString().Trim() : "";

                                        // 发送瓦尔基里专属 0xF7 报文查询电量与状态
                                        byte[] rx = SendValkyrieCommand(handle, 0xF7, 10);
                                        if (rx != null && rx.Length >= 7)
                                        {
                                            int baseOffset = 0;
                                            if (rx[0] == 0x00 && rx[1] == 0x01)
                                            {
                                                baseOffset = 1; // Win32 前导包含 0x00
                                            }
                                            else if (rx[0] == 0x01)
                                            {
                                                baseOffset = 0; // 无前导字节
                                            }

                                            int isCanRead = rx[baseOffset + 0];
                                            int kbBatt = rx[baseOffset + 1];
                                            int mouseBatt = rx[baseOffset + 2];
                                            bool kbOnline = (rx[baseOffset + 3] == 0);
                                            bool mouseOnline = (rx[baseOffset + 4] == 0);
                                            int devType = rx[baseOffset + 6]; // 1: 键盘, 2: 鼠标, 3: 键鼠双模

                                            // 1. 处理键盘设备
                                            if (devType == 1 || devType == 3)
                                            {
                                                string kbModel = GetModelName(attr.VendorID, attr.ProductID, prodStr, DeviceCategory.Keyboard);
                                                CachedKbName = kbModel;

                                                MouseBatteryInfo kbInfo = new MouseBatteryInfo();
                                                kbInfo.DeviceId = string.Format("Valkyrie:{0:X4}:{1:X4}:KB", attr.VendorID, attr.ProductID);
                                                kbInfo.HardwareFingerprint = string.Format("HID:{0:X4}:{1:X4}", attr.VendorID, attr.ProductID);
                                                kbInfo.DeviceName = kbModel;
                                                kbInfo.Brand = "Valkyrie";
                                                kbInfo.Category = DeviceCategory.Keyboard;
                                                kbInfo.Transport = "2.4G 无线";
                                                kbInfo.IsDonglePresent = true;
                                                kbInfo.IsConnected = kbOnline;
                                                kbInfo.IsSleeping = !kbOnline;
                                                kbInfo.IsCharging = false;

                                                if (kbBatt > 0 && kbBatt <= 100)
                                                {
                                                    CachedKbBattery = kbBatt;
                                                    CachedLastUpdated = DateTime.Now;
                                                    kbInfo.BatteryPercent = kbBatt;
                                                }
                                                else if (CachedKbBattery > 0)
                                                {
                                                    kbInfo.BatteryPercent = CachedKbBattery;
                                                }

                                                list.Add(kbInfo);
                                            }

                                            // 2. 处理鼠标设备
                                            if (devType == 2 || devType == 3)
                                            {
                                                string mouseModel = GetModelName(attr.VendorID, attr.ProductID, prodStr, DeviceCategory.Mouse);
                                                CachedMouseName = mouseModel;

                                                LastMouseDevicePath = devicePath;
                                                MouseBatteryInfo mouseInfo = new MouseBatteryInfo();
                                                mouseInfo.DeviceId = string.Format("Valkyrie:{0:X4}:{1:X4}:MS", attr.VendorID, attr.ProductID);
                                                mouseInfo.HardwareFingerprint = string.Format("HID:{0:X4}:{1:X4}", attr.VendorID, attr.ProductID);
                                                mouseInfo.DeviceName = mouseModel;
                                                mouseInfo.Brand = "Valkyrie";
                                                mouseInfo.Category = DeviceCategory.Mouse;
                                                mouseInfo.Transport = "2.4G 无线";
                                                mouseInfo.IsDonglePresent = true;
                                                mouseInfo.IsConnected = mouseOnline;
                                                mouseInfo.IsSleeping = !mouseOnline;
                                                mouseInfo.IsCharging = false;
                                                mouseInfo.SupportedPollingRates = new int[] { 125, 250, 500, 1000, 2000, 4000, 8000 };

                                                if (mouseOnline)
                                                {
                                                    int hz = QueryPollingRateFromHandle(handle);
                                                    if (hz > 0)
                                                    {
                                                        mouseInfo.PollingRate = hz;
                                                        CachedPollingRate = hz;
                                                    }
                                                    else
                                                    {
                                                        mouseInfo.PollingRate = CachedPollingRate;
                                                    }

                                                    int qDpiX, qDpiY, qStage, qCount;
                                                    if (FastQueryDpi(out qDpiX, out qDpiY, out qStage, out qCount))
                                                    {
                                                        mouseInfo.Dpi = qDpiX;
                                                        mouseInfo.DpiY = qDpiY;
                                                        mouseInfo.DpiStage = qStage;
                                                        mouseInfo.DpiStageCount = qCount;
                                                        mouseInfo.DpiStages = CachedDpiStages;
                                                        mouseInfo.DpiStagesY = CachedDpiStagesY;
                                                        mouseInfo.IsXyIndependent = CachedIsXyIndependent;
                                                        mouseInfo.XyRatio = CachedXyRatio;
                                                    }
                                                    else
                                                    {
                                                        mouseInfo.Dpi = CachedDpi;
                                                        mouseInfo.DpiY = CachedDpiY > 0 ? CachedDpiY : CachedDpi;
                                                        mouseInfo.DpiStage = CachedDpiStage;
                                                        mouseInfo.DpiStageCount = CachedDpiCount;
                                                        mouseInfo.DpiStages = CachedDpiStages;
                                                        mouseInfo.DpiStagesY = CachedDpiStagesY;
                                                        mouseInfo.IsXyIndependent = CachedIsXyIndependent;
                                                        mouseInfo.XyRatio = CachedXyRatio;
                                                    }
                                                }
                                                else
                                                {
                                                    mouseInfo.PollingRate = CachedPollingRate;
                                                    mouseInfo.Dpi = CachedDpi;
                                                    mouseInfo.DpiY = CachedDpiY > 0 ? CachedDpiY : CachedDpi;
                                                    mouseInfo.DpiStage = CachedDpiStage;
                                                    mouseInfo.DpiStageCount = CachedDpiCount;
                                                    mouseInfo.DpiStages = CachedDpiStages;
                                                    mouseInfo.DpiStagesY = CachedDpiStagesY;
                                                    mouseInfo.IsXyIndependent = CachedIsXyIndependent;
                                                    mouseInfo.XyRatio = CachedXyRatio;
                                                }

                                                if (mouseBatt > 0 && mouseBatt <= 100)
                                                {
                                                    CachedMouseBattery = mouseBatt;
                                                    CachedLastUpdated = DateTime.Now;
                                                    mouseInfo.BatteryPercent = mouseBatt;
                                                }
                                                else if (CachedMouseBattery > 0)
                                                {
                                                    mouseInfo.BatteryPercent = CachedMouseBattery;
                                                }

                                                list.Add(mouseInfo);
                                            }

                                            SaveHardwareCache();
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
            }

            return list;
        }
    }
}
