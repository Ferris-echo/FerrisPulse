// ============================================================================
// 模块名称: HyperXCloud3SHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   极度未知 (HyperX) 飓风3S 无线耳机 (Cloud III S Wireless) 2.4G 接收器专属 HID 协议实现：
//   1. 按 UsagePage 0x01C0 / Usage 0x0001 / 64 字节端点精确隔离出电量通道
//      （同一接收器的 0xFF13/62 字节为固件通道，0x170F/8 字节为按键通道，均不可用于电量）
//   2. 下发 0x0C 查询帧 (0C 02 03 01 00 06 00) 读取 1% 精度电量
//   3. 解析 0x0D 充电事件帧 (0D 02 03 00 0A <state>)，事件驱动 + 时效窗口，绝不臆造充电位
//   4. 离线电量缓存与注册表持久化，拔线/关机后仍可显示上次读数
// 依赖关系: HidTransport.cs, MouseBatteryInfo.cs
//
// 协议来源:
//   - 本机实机抓包验证: 03F0:02CC (RS 63 Air 同机环境) 回帧 0C 02 03 01 00 06 2D 0E C4 -> 45%
//   - 社区逆向: navrozashvili/HyperXBatteryHID (HyperXCloud3SBatteryProtocol.cs), 06BE 同代协议
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、报文结构或新增 PID 时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止 $"..." 插值、?. 空条件、nameof、表达式主体成员）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    public static class HyperXCloud3SHelper
    {
        private static readonly object hidLock = new object();
        private const string CacheRegistryKey = @"Software\RazerBatteryTray\HyperXCloud3SCache";

        // ---- 硬件标识 ----
        public const int VendorIdHp = 0x03F0;                 // HP Inc.（HyperX 现属 HP）
        public const int PidCloud3SWireless = 0x02CC;         // 飓风3S 无线接收器（本机实测）
        public const int PidCloud3STio = 0x06BE;              // 飓风3S 无线接收器（同代变体，社区实机验证）

        // ---- 端点签名（四元组隔离，严禁只凭 PID 匹配） ----
        private const int BatteryUsagePage = 0x01C0;          // 电量/事件厂商集合
        private const int BatteryUsage = 0x0001;
        private const int BatteryReportLength = 64;           // 含 Report ID 的固定报文长度

        // ---- 协议常量 ----
        private const byte ReportIdBattery = 0x0C;            // 电量查询与回包
        private const byte CmdBattery = 0x06;
        private const byte ReportIdCharging = 0x0D;           // 充电事件帧
        private const byte TagCharging = 0x0A;
        private const byte HeaderA = 0x02;
        private const byte HeaderB = 0x03;

        // ---- 时序常量 ----
        private const int QueryThrottleMs = 700;              // 硬件查询节流窗口
        private const int ReadBudgetMs = 600;                 // 单轮读取总预算
        private const int SingleReadTimeoutMs = 120;          // 单次读取等待上限
        private const int ChargingStaleMs = 600000;           // 充电事件时效 10 分钟，超时降级为“未知”

        // ---- 运行态缓存 ----
        public static int CachedBatteryPercent = -1;
        public static bool CachedIsCharging = false;
        public static bool CachedChargingKnown = false;
        public static bool CachedIsConnected = false;
        public static int CachedFlags1 = 0;
        public static int CachedFlags2 = 0;
        public static string CachedDeviceName = "HyperX 飓风3S 无线";
        public static DateTime CachedLastUpdated = DateTime.MinValue;
        public static string LastResponseHex = "";

        private static bool cachedDonglePresent = false;
        private static DateTime lastChargingEventAt = DateTime.MinValue;
        private static int lastQueryTick = 0;
        private static bool hasQueried = false;
        private static int detectedPid = PidCloud3SWireless;

        // ====================================================================
        // 设备识别
        // ====================================================================
        public static bool IsSupportedVidPid(int vid, int pid)
        {
            if (vid != VendorIdHp) return false;
            return pid == PidCloud3SWireless || pid == PidCloud3STio;
        }

        // 支持 "HID:03F0:02CC"、"HyperX:03F0:02CC" 与原始接口路径等任意包含 VID/PID 的标识
        public static bool IsCloud3SIdentifier(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return false;
            string lower = identifier.ToLowerInvariant();
            bool isVid = lower.Contains("vid_03f0") || lower.Contains("03f0:") || lower.Contains("03f0&");
            if (!isVid) return false;
            return lower.Contains("pid_02cc") || lower.Contains("pid_06be") ||
                   lower.Contains(":02cc") || lower.Contains(":06be");
        }

        public static string GetModelName(int pid)
        {
            if (pid == PidCloud3STio) return "HyperX 飓风3S 无线";
            return "HyperX 飓风3S 无线";
        }

        // ====================================================================
        // 端点选择：仅接受 0x01C0 / 0x0001 / 64 字节的电量集合
        // 同一接收器还存在 0xFF13/62、0x170F/8、0xFFC0/32 等异构集合，必须物理隔离
        // ====================================================================
        public static bool IsBatteryEndpoint(HidDeviceDescriptor device)
        {
            if (device == null) return false;
            if (!IsSupportedVidPid(device.VendorId, device.ProductId)) return false;
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
        public static byte[] BuildBatteryQuery(int length)
        {
            if (length < 9 || length > 512) throw new ArgumentOutOfRangeException("length");
            byte[] report = new byte[length];
            report[0] = ReportIdBattery;
            report[1] = HeaderA;
            report[2] = HeaderB;
            report[3] = 0x01;
            report[4] = 0x00;
            report[5] = CmdBattery;
            report[6] = 0x00;
            return report;
        }

        // 回包: 0C 02 03 01 00 06 <PCT> <FLAGS1> <FLAGS2> ...
        public static bool TryParseBattery(byte[] report, out int percent, out byte flags1, out byte flags2)
        {
            percent = -1;
            flags1 = 0;
            flags2 = 0;
            if (report == null || report.Length < 9) return false;
            if (report[0] != ReportIdBattery) return false;
            if (report[1] != HeaderA || report[2] != HeaderB) return false;
            if (report[5] != CmdBattery) return false;
            int pct = report[6];
            if (pct < 0 || pct > 100) return false;
            percent = pct;
            flags1 = report[7];
            flags2 = report[8];
            return true;
        }

        // 充电事件: 0D 02 03 00 0A <STATE>，STATE=1 充电 / 0 未充电
        public static bool TryParseCharging(byte[] report, out bool charging)
        {
            charging = false;
            if (report == null || report.Length < 6) return false;
            if (report[0] != ReportIdCharging) return false;
            if (report[1] != HeaderA || report[2] != HeaderB || report[3] != 0x00) return false;
            if (report[4] != TagCharging) return false;
            if (report[5] == 1) { charging = true; return true; }
            if (report[5] == 0) { charging = false; return true; }
            return false;
        }

        // ====================================================================
        // 离线缓存持久化
        // ====================================================================
        public static void LoadHardwareCache()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(CacheRegistryKey, false))
                {
                    if (key == null) return;

                    object batt = key.GetValue("BatteryPercent");
                    if (batt != null) CachedBatteryPercent = Convert.ToInt32(batt);

                    object chg = key.GetValue("IsCharging");
                    if (chg != null) CachedIsCharging = (Convert.ToInt32(chg) == 1);

                    object known = key.GetValue("ChargingKnown");
                    if (known != null) CachedChargingKnown = (Convert.ToInt32(known) == 1);

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

        public static void SaveHardwareCache()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(CacheRegistryKey))
                {
                    if (key == null) return;
                    if (CachedBatteryPercent >= 0) key.SetValue("BatteryPercent", CachedBatteryPercent);
                    key.SetValue("IsCharging", CachedIsCharging ? 1 : 0);
                    key.SetValue("ChargingKnown", CachedChargingKnown ? 1 : 0);
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
                if (hasQueried)
                {
                    int elapsed = Environment.TickCount - lastQueryTick;
                    if (elapsed >= 0 && elapsed < QueryThrottleMs)
                    {
                        return cachedDonglePresent ? BuildDeviceInfo() : null;
                    }
                }

                List<HidDeviceDescriptor> descriptors = HidTransport.Enumerate(VendorIdHp, 0);
                HidDeviceDescriptor endpoint = FindBatteryEndpoint(descriptors);
                lastQueryTick = Environment.TickCount;
                hasQueried = true;

                if (endpoint == null)
                {
                    cachedDonglePresent = false;
                    CachedIsConnected = false;
                    CachedIsCharging = false;
                    CachedChargingKnown = false;
                    return null;
                }

                cachedDonglePresent = true;
                detectedPid = endpoint.ProductId;
                ReadDeviceState(endpoint);

                // 充电态时效判定：事件驱动的状态位超过时效窗口即降级为“未知”，绝不长期伪装
                bool chargingKnown = false;
                if (lastChargingEventAt != DateTime.MinValue)
                {
                    double ageMs = (DateTime.Now - lastChargingEventAt).TotalMilliseconds;
                    if (ageMs >= 0 && ageMs <= ChargingStaleMs) chargingKnown = true;
                }
                CachedChargingKnown = chargingKnown;

                return BuildDeviceInfo();
            }
        }

        private static MouseBatteryInfo BuildDeviceInfo()
        {
            MouseBatteryInfo info = new MouseBatteryInfo();
            info.DeviceId = string.Format("HyperX:{0:X4}:{1:X4}", VendorIdHp, detectedPid);
            info.HardwareFingerprint = string.Format("HID:{0:X4}:{1:X4}", VendorIdHp, detectedPid);
            info.Brand = "HyperX";
            info.Category = DeviceCategory.Headset;
            info.DeviceName = CachedDeviceName;
            info.Transport = "2.4G 无线";
            info.IsCustom = false;
            info.IsDonglePresent = cachedDonglePresent;
            info.IsConnected = CachedIsConnected;
            info.IsSleeping = false;
            info.IsCharging = CachedIsConnected && CachedChargingKnown && CachedIsCharging;
            info.IsChargingUnknown = CachedIsConnected && !CachedChargingKnown;
            info.BatteryPercent = CachedIsConnected ? CachedBatteryPercent : -1;
            info.ExactBatteryPercent = CachedBatteryPercent >= 0 ? CachedBatteryPercent : 0;
            info.LastUpdated = (CachedLastUpdated == DateTime.MinValue) ? DateTime.Now : CachedLastUpdated;
            return info;
        }

        // ====================================================================
        // 硬件会话：单帧查询 + 限时读取（同时捕获 0x0D 充电事件）
        // ====================================================================
        private static bool ReadDeviceState(HidDeviceDescriptor endpoint)
        {
            IntPtr handle = HidTransport.Open(endpoint.DevicePath);
            if (!HidTransport.IsValidHandle(handle))
            {
                CachedIsConnected = false;
                return false;
            }

            try
            {
                if (!string.IsNullOrEmpty(endpoint.ProductString))
                {
                    CachedDeviceName = GetModelName(endpoint.ProductId);
                }

                // 丢弃接收器积压的历史状态报文（本机型为 Interrupt 端点，预排空安全）
                HidTransport.DrainReports(handle, BatteryReportLength, 8);

                byte[] tx = BuildBatteryQuery(BatteryReportLength);
                if (!HidTransport.WriteReport(handle, tx, tx.Length, 200))
                {
                    CachedIsConnected = false;
                    return false;
                }

                int battery = -1;
                byte flags1 = 0;
                byte flags2 = 0;
                bool sawResponse = false;
                bool sawCharging = false;
                bool chargingState = false;

                long deadline = Environment.TickCount + ReadBudgetMs;
                while (true)
                {
                    int remain = (int)(deadline - Environment.TickCount);
                    if (remain <= 0) break;

                    int timeout = (remain < SingleReadTimeoutMs) ? remain : SingleReadTimeoutMs;
                    byte[] rx;
                    if (!HidTransport.ReadReport(handle, BatteryReportLength, timeout, out rx)) continue;
                    if (rx == null || rx.Length < 6) continue;

                    if (rx[0] == ReportIdBattery || rx[0] == ReportIdCharging)
                    {
                        sawResponse = true;
                        LastResponseHex = BitConverter.ToString(rx);
                    }

                    byte f1;
                    byte f2;
                    int pct;
                    if (TryParseBattery(rx, out pct, out f1, out f2))
                    {
                        battery = pct;
                        flags1 = f1;
                        flags2 = f2;
                        break;
                    }

                    bool chg;
                    if (TryParseCharging(rx, out chg))
                    {
                        sawCharging = true;
                        chargingState = chg;
                    }
                }

                if (battery >= 0)
                {
                    int prevBatt = CachedBatteryPercent;
                    bool prevChg = CachedIsCharging;
                    bool prevKnown = CachedChargingKnown;

                    CachedBatteryPercent = battery;
                    CachedFlags1 = flags1;
                    CachedFlags2 = flags2;
                    CachedIsConnected = true;
                    CachedLastUpdated = DateTime.Now;

                    if (sawCharging)
                    {
                        CachedIsCharging = chargingState;
                        lastChargingEventAt = DateTime.Now;
                    }

                    if (prevBatt != CachedBatteryPercent || prevChg != CachedIsCharging || prevKnown != CachedChargingKnown)
                    {
                        SaveHardwareCache();
                    }
                    return true;
                }

                // 有帧但无电量内容，或完全无响应：耳机未开机 / 已休眠 / 超出射频范围
                CachedIsConnected = sawResponse && battery >= 0;
                if (sawCharging && sawResponse)
                {
                    CachedIsCharging = chargingState;
                    lastChargingEventAt = DateTime.Now;
                }
                return false;
            }
            catch
            {
                CachedIsConnected = false;
                return false;
            }
            finally
            {
                HidTransport.Close(handle);
            }
        }
    }
}
