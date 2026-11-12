// ============================================================================
// 模块名称: CompxWirelessKeyboardHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   公版 CompX 方案 2.4G 无线键盘（产品串 "2.4G Wireless Receiver"，VID/PID 05AC:024F）
//   专属协议实现：
//   1. 该接收器借用 Apple VID/PID，会被 AppleDeviceHelper 误判为 Magic Mouse，
//      必须以产品串 + 33 字节厂商端点做精确四元组隔离
//   2. 同一接收器暴露 0xFF60/0x61 的两个厂商集合（65 字节为配置通道，33 字节为电量通道），
//      仅 33 字节集合承载电量；65 字节通道对任何查询都返回固定状态帧，严禁混用
//   3. 电量查询帧 00 20 01 00 ... 21，回包 00 20 01 00 <pct> ... <checksum>
//   4. 键盘休眠时射频不应答：接收器在线即保留条目并展示最后一次读数（休眠态），
//      绝不因为一次轮询落在休眠窗口就让整个设备从列表里消失
// 依赖关系: HidTransport.cs, MouseBatteryInfo.cs
//
// 实机证据 (本机 05AC:024F "2.4G Wireless Receiver"):
//   TX: 00 20 01 00 00 ... 21
//   RX: 00 20 01 00 38 00 ... 59  -> 56%
//   RX: 00 20 01 00 36 00 ... 57  -> 54%
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、报文结构或新增 PID 时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止 $"..." 插值、?. 空条件、nameof、表达式主体成员）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    public static class CompxWirelessKeyboardHelper
    {
        private static readonly object hidLock = new object();
        private const string CacheRegistryKey = @"Software\RazerBatteryTray\CompXKeyboardCache";

        // ---- 硬件标识 ----
        public const int VendorIdApple = 0x05AC;         // 该接收器借用 Apple VID
        public const int PidComboReceiver = 0x024F;

        // ---- 端点签名（与 GANSS GS3108T-PRO 共用 VID/PID，必须用产品串隔离） ----
        public const string ProductString = "2.4G Wireless Receiver";
        private const int BatteryUsagePage = 0xFF60;
        private const int BatteryUsage = 0x0061;
        private const int BatteryReportLength = 33;

        // ---- 协议常量 ----
        private const int OffsetCmd = 1;                 // 0x20 固定命令字
        private const int OffsetSub = 2;                 // 0x01 电量子命令
        private const int OffsetPercent = 4;             // 电量百分比
        private const byte CmdBattery = 0x20;
        private const byte SubBattery = 0x01;
        private const byte FrameTrailer = 0x21;

        // ---- 时序 ----
        private const int QueryThrottleMs = 5000;        // 硬件查询节流
        private const int AttemptBudgetMs = 700;         // 单次唤醒窗口（休眠键盘响应偶发丢帧）
        private const int SingleReadTimeoutMs = 120;
        private const int AttemptsPerPoll = 2;

        // ---- 运行态缓存 ----
        public static int CachedBatteryPercent = -1;
        public static bool CachedIsConnected = false;
        public static bool CachedIsSleeping = false;
        public static bool CachedDonglePresent = false;
        public static string CachedDeviceName = "2.4G 无线键盘";
        public static DateTime CachedLastUpdated = DateTime.MinValue;
        public static string LastResponseHex = "";

        private static int lastQueryTick = 0;
        private static bool hasQueried = false;
        private static bool cacheLoaded = false;
        private static string detectedProduct = "";

        // ====================================================================
        // 设备识别
        // ====================================================================
        public static bool IsSupportedVidPid(int vid, int pid)
        {
            return vid == VendorIdApple && pid == PidComboReceiver;
        }

        // AppleDeviceHelper 的统一入口守卫：命中本公版键盘时，禁止按 Apple 设备继续解析
        public static bool IsRecognizedProduct(string productString, int vid, int pid)
        {
            if (!IsSupportedVidPid(vid, pid)) return false;
            if (string.IsNullOrEmpty(productString)) return false;
            if (GanssDeviceHelper.IsKnownKeyboardName(productString)) return false;
            return productString.IndexOf(ProductString, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static bool IsBatteryEndpoint(HidDeviceDescriptor device)
        {
            if (device == null) return false;
            if (!IsSupportedVidPid(device.VendorId, device.ProductId)) return false;
            if (string.IsNullOrEmpty(device.ProductString)) return false;
            if (device.ProductString.IndexOf(ProductString, StringComparison.OrdinalIgnoreCase) < 0) return false;
            if (device.UsagePage != BatteryUsagePage || device.Usage != BatteryUsage) return false;
            return device.InputReportLength == BatteryReportLength &&
                   device.OutputReportLength == BatteryReportLength;
        }

        public static HidDeviceDescriptor FindBatteryEndpoint(List<HidDeviceDescriptor> descriptors)
        {
            if (descriptors == null) return null;
            for (int i = 0; i < descriptors.Count; i++)
            {
                if (IsBatteryEndpoint(descriptors[i])) return descriptors[i];
            }
            return null;
        }

        // ====================================================================
        // 报文构造与解析（纯函数，便于自动化断言）
        // ====================================================================
        public static byte[] BuildBatteryQuery()
        {
            byte[] report = new byte[BatteryReportLength];
            report[0] = 0x00;
            report[OffsetCmd] = CmdBattery;
            report[OffsetSub] = SubBattery;
            report[BatteryReportLength - 1] = FrameTrailer;
            return report;
        }

        public static bool TryParseBattery(byte[] report, out int percent)
        {
            percent = -1;
            if (report == null) return false;
            int offset = (report.Length == BatteryReportLength && report[0] == 0) ? 1 : 0;
            if (report.Length != 32 + offset) return false;
            if (report[offset] != CmdBattery || report[offset + 1] != SubBattery) return false;
            if (report[offset + 2] != 0) return false;
            if (report[offset + 3] > 100) return false;
            percent = report[offset + 3];
            return true;
        }

        // ====================================================================
        // 离线缓存持久化：休眠/拔线后仍保留最后一次读数
        // ====================================================================
        private static void EnsureCacheLoaded()
        {
            if (cacheLoaded) return;
            cacheLoaded = true;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(CacheRegistryKey, false))
                {
                    if (key == null) return;
                    object batt = key.GetValue("BatteryPercent");
                    if (batt != null) CachedBatteryPercent = Convert.ToInt32(batt);
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
            catch { }
        }

        private static void SaveCache()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(CacheRegistryKey))
                {
                    if (key == null) return;
                    if (CachedBatteryPercent >= 0) key.SetValue("BatteryPercent", CachedBatteryPercent);
                    if (!string.IsNullOrEmpty(CachedDeviceName)) key.SetValue("DeviceName", CachedDeviceName);
                    if (CachedLastUpdated != DateTime.MinValue) key.SetValue("LastUpdated", CachedLastUpdated.ToString("o"));
                }
            }
            catch { }
        }

        // ====================================================================
        // 对外查询入口
        // ====================================================================
        public static MouseBatteryInfo QueryDevice()
        {
            lock (hidLock)
            {
                EnsureCacheLoaded();

                if (hasQueried)
                {
                    int elapsed = Environment.TickCount - lastQueryTick;
                    if (elapsed >= 0 && elapsed < QueryThrottleMs)
                    {
                        return CachedDonglePresent ? BuildDeviceInfo() : null;
                    }
                }

                hasQueried = true;
                lastQueryTick = Environment.TickCount;

                List<HidDeviceDescriptor> descriptors = HidTransport.Enumerate(VendorIdApple, PidComboReceiver);
                HidDeviceDescriptor endpoint = FindBatteryEndpoint(descriptors);
                if (endpoint == null)
                {
                    CachedDonglePresent = false;
                    CachedIsConnected = false;
                    CachedIsSleeping = false;
                    return null;
                }

                CachedDonglePresent = true;
                if (!string.IsNullOrEmpty(endpoint.ProductString)) detectedProduct = endpoint.ProductString;

                ReadDeviceState(endpoint);
                CachedIsSleeping = !CachedIsConnected;
                return BuildDeviceInfo();
            }
        }

        private static MouseBatteryInfo BuildDeviceInfo()
        {
            MouseBatteryInfo info = new MouseBatteryInfo();
            info.DeviceId = string.Format("CompX:{0:X4}:{1:X4}", VendorIdApple, PidComboReceiver);
            // 指纹必须使用 CompX: 命名空间：通用 HID 自动注册会写入 "HID:05AC:024F" 的自定义配置
            // （Category=Dongle），若指纹同为 HID: 前缀会被其覆盖，导致键盘被改写成接收器。
            info.HardwareFingerprint = string.Format("CompX:{0:X4}:{1:X4}", VendorIdApple, PidComboReceiver);
            info.Brand = "2.4G 键盘";
            info.Category = DeviceCategory.Keyboard;
            info.DeviceName = CachedDeviceName;
            info.Transport = "2.4G 无线";
            info.IsCustom = false;
            info.IsDonglePresent = CachedDonglePresent;
            info.IsConnected = CachedIsConnected;
            info.IsSleeping = CachedIsSleeping;
            info.IsCharging = false; // 该帧只承载百分比，不臆造充电位
            info.BatteryPercent = CachedBatteryPercent;
            info.ExactBatteryPercent = CachedBatteryPercent >= 0 ? CachedBatteryPercent : 0;
            info.LastUpdated = (CachedLastUpdated == DateTime.MinValue) ? DateTime.Now : CachedLastUpdated;
            return info;
        }

        private static void ReadDeviceState(HidDeviceDescriptor endpoint)
        {
            IntPtr handle = HidTransport.Open(endpoint.DevicePath);
            if (!HidTransport.IsValidHandle(handle))
            {
                CachedIsConnected = false;
                return;
            }

            try
            {
                for (int attempt = 0; attempt < AttemptsPerPoll; attempt++)
                {
                    HidTransport.DrainReports(handle, BatteryReportLength, 4);

                    byte[] tx = BuildBatteryQuery();
                    if (!HidTransport.WriteReport(handle, tx, tx.Length, 200)) continue;

                    long deadline = Environment.TickCount + AttemptBudgetMs;
                    while (true)
                    {
                        int remain = (int)(deadline - Environment.TickCount);
                        if (remain <= 0) break;

                        int timeout = (remain < SingleReadTimeoutMs) ? remain : SingleReadTimeoutMs;
                        byte[] rx;
                        if (!HidTransport.ReadReport(handle, BatteryReportLength, timeout, out rx)) continue;
                        if (rx == null || rx.Length < 5) continue;

                        LastResponseHex = BitConverter.ToString(rx);
                        int percent;
                        if (TryParseBattery(rx, out percent))
                        {
                            int prev = CachedBatteryPercent;
                            CachedBatteryPercent = percent;
                            CachedIsConnected = true;
                            CachedLastUpdated = DateTime.Now;
                            if (prev != percent) SaveCache();
                            return;
                        }
                    }
                }

                // 接收器在、键盘射频静默（休眠）：保留上次读数，条目继续展示
                CachedIsConnected = false;
            }
            catch
            {
                CachedIsConnected = false;
            }
            finally
            {
                HidTransport.Close(handle);
            }
        }
    }
}
