// ============================================================================
// 模块名称: RapooDeviceHelper.MT760.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   雷柏 MT760 / MT760L / MT760 Mini 系列 2.4G 办公鼠标 (24AE:1870) 私有协议扩展：
//   通过 UsagePage 0xFF00 / Usage 0x000E 的 32 字节 Out/Input 集合发送 BA A5 A4/A5/AA
//   内存指令，实现精准电量与充电状态读取、7 档 DPI 读写、125~1000Hz 回报率读写及休眠缓存。
// 依赖关系: RapooDeviceHelper.cs, RazerDeviceHelper.cs (HID P/Invoke), MouseBatteryInfo.cs
//
// 协议来源与致谢:
//   - PyFlat/Device-Battery-Info #36 (@xicor22): BA A5 AA 主动查询与 [01 state level] 解析。
//   - Revisory676/rapoo-mt760-linux: FF00:000E 端点、7 字节 0xBB 遥测与 0xBA 命令通道。
//   - LTbinglingfeng/MT760-driver: 0x0638 DPI 配置块、0x0648 辅助块、0x0630 回报率块布局。
//   - 本机实机验证: 电量 89%、DPI 1200 (3/7)、1000Hz，并完成 +50 DPI / 500Hz 可逆写入回读。
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、协议解析或签名时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止 $"..." 插值、?. 空条件运算符、nameof、out var）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Threading;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    public static partial class RapooDeviceHelper
    {
        private const int OfficeVid = 0x24AE;
        private const int OfficePid = 0x1870;
        private const int OfficeUsagePage = 0xFF00;
        private const int OfficeUsage = 0x000E;
        private const int OfficeReportLength = 32;

        private const byte OfficeReportId = 0xBA;
        private const byte OfficeConnDongle = 0xA5;
        private const byte OfficeOpRead = 0xA4;
        private const byte OfficeOpWrite = 0xA5;
        private const byte OfficeCmdBattery = 0xAA;

        private const int OfficeDpiAddress = 0x0638;
        private const int OfficeDpiAuxAddress = 0x0648;
        private const int OfficeRateAddress = 0x0630;
        private const int OfficeDpiBlockLength = 20;
        private const int OfficeDpiReadLength = 24;
        private const int OfficeAuxReadLength = 8;
        private const int OfficeRateBlockLength = 8;

        private const string OfficeCacheRegistryKey = @"Software\RazerBatteryTray\RapooMt760Cache";
        private const string OfficeDeviceName = "雷柏 MT760 系列多模无线鼠标";

        private static readonly object officeLock = new object();
        private static IntPtr officeHandle = IntPtr.Zero;
        private static string officePath = null;
        private static DateTime officeLastEnum = DateTime.MinValue;
        private static DateTime officeLastExchange = DateTime.MinValue;
        private static DateTime officeQuietUntil = DateTime.MinValue;
        private static DateTime officePerfUpdated = DateTime.MinValue;
        private static bool officeCacheLoaded = false;

        public static int CachedOfficeBatteryPercent = -1;
        public static bool CachedOfficeIsCharging = false;
        public static bool CachedOfficeChargingUnknown = false;
        public static DateTime CachedOfficeLastUpdated = DateTime.MinValue;
        public static int CachedOfficeDpi = 0;
        public static int CachedOfficeDpiY = 0;
        public static int CachedOfficeDpiStage = 0;
        public static int CachedOfficeDpiStageCount = 0;
        public static int[] CachedOfficeDpiStages = null;
        public static int[] CachedOfficeDpiStagesY = null;
        public static int CachedOfficePollingRate = 0;

        public static readonly int[] OfficeSupportedPollingRates = new int[] { 125, 250, 500, 1000 };

        /// <summary>
        /// 端点隔离断言：24AE:1870 的 FF00:000E 32/32 集合才允许接入 MT760 办公协议。
        /// 严禁仅凭 VID/PID 或宽松的 &gt;= 32 匹配（同厂 FF00:0002 7B 遥测与 FF01:0000 29B 键盘通道必须排除）。
        /// </summary>
        public static bool IsOfficeEndpointCaps(int vendorId, int productId, int usagePage, int usage, int inputLength, int outputLength, int featureLength)
        {
            return vendorId == OfficeVid &&
                   productId == OfficePid &&
                   usagePage == OfficeUsagePage &&
                   usage == OfficeUsage &&
                   inputLength == OfficeReportLength &&
                   outputLength == OfficeReportLength;
        }

        public static byte[] BuildOfficeBatteryRequest()
        {
            byte[] request = new byte[OfficeReportLength];
            request[0] = OfficeReportId;
            request[1] = OfficeConnDongle;
            request[2] = OfficeCmdBattery;
            return request;
        }

        public static bool TryParseOfficeBatteryReply(byte[] response, out int batteryPercent, out bool isCharging, out bool chargingUnknown)
        {
            batteryPercent = -1;
            isCharging = false;
            chargingUnknown = false;

            if (response == null || response.Length < 3) return false;
            if (response[0] != 0x01) return false;

            int state = response[1];
            int level = response[2];

            // 充电固件在电量尚未稳定时会回 0x7F；此时诚实标记未知，绝不伪造 100%。
            if (state == 0x02 && level == 0x7F)
            {
                isCharging = true;
                chargingUnknown = true;
                return true;
            }

            if (level < 1 || level > 100) return false;

            batteryPercent = level;
            isCharging = (state == 0x02);
            return true;
        }

        public static bool TryParseOfficeDpiConfig(byte[] response, out int[] stages, out int stageCount, out int currentStage)
        {
            stages = null;
            stageCount = 0;
            currentStage = 0;

            if (response == null || response.Length < 28) return false;
            if (response[0] != 0x01) return false;

            int count = response[18] + 1;
            int current = response[20] + 1;
            if (count < 1 || count > 7) return false;
            if (current < 1 || current > count) return false;

            int[] levels = new int[7];
            for (int i = 0; i < 7; i++)
            {
                int units = response[4 + i * 2] | (response[5 + i * 2] << 8);
                levels[i] = units * 50;
            }

            int[] active = new int[count];
            for (int i = 0; i < count; i++)
            {
                if (levels[i] < 50 || levels[i] > 4000 || (levels[i] % 50) != 0) return false;
                active[i] = levels[i];
            }

            stages = active;
            stageCount = count;
            currentStage = current;
            return true;
        }

        public static bool TryParseOfficePollingRate(byte[] response, out int hz)
        {
            hz = 0;
            if (response == null || response.Length < 12) return false;
            if (response[0] != 0x01) return false;

            int wired = DecodeOfficeRateCode(response[4]);
            int wireless = DecodeOfficeRateCode(response[5]);

            int selected = wireless > 0 ? wireless : wired;
            if (!IsSupportedOfficeRate(selected)) return false;

            hz = selected;
            return true;
        }

        public static MouseBatteryInfo QueryRapooOfficeDeviceInfo()
        {
            LoadOfficeCache();

            lock (officeLock)
            {
                // 没有接收器端点时返回 null；有端点但鼠标休眠时保留缓存占位。
                if (!EnsureOfficeHandle()) return null;

                bool online = false;
                int battery = -1;
                bool charging = false;
                bool chargingUnknown = false;

                byte[] batteryReply;
                if (OfficeExchangeRead(BuildOfficeBatteryRequest(), out batteryReply))
                {
                    if (TryParseOfficeBatteryReply(batteryReply, out battery, out charging, out chargingUnknown))
                    {
                        online = true;
                        CachedOfficeBatteryPercent = battery;
                        CachedOfficeIsCharging = charging;
                        CachedOfficeChargingUnknown = chargingUnknown;
                        CachedOfficeLastUpdated = DateTime.Now;
                    }
                }

                if (!online)
                {
                    battery = CachedOfficeBatteryPercent;
                    charging = CachedOfficeIsCharging;
                    chargingUnknown = CachedOfficeChargingUnknown;
                }

                // DPI/回报率属于慢变量：每 5 秒刷新一次，避免与电池轮询抢占 2.4G 通道。
                if ((DateTime.Now - officePerfUpdated).TotalSeconds >= 5.0 && !IsOfficeQuiet())
                {
                    int[] stages;
                    int stageCount;
                    int currentStage;
                    byte[] dpiReply;
                    if (OfficeReadDpiRaw(out dpiReply) && TryParseOfficeDpiConfig(dpiReply, out stages, out stageCount, out currentStage))
                    {
                        CachedOfficeDpiStages = stages;
                        CachedOfficeDpiStagesY = CloneIntArray(stages);
                        CachedOfficeDpiStageCount = stageCount;
                        CachedOfficeDpiStage = currentStage;
                        CachedOfficeDpi = stages[currentStage - 1];
                        CachedOfficeDpiY = CachedOfficeDpi;
                    }

                    int hz;
                    byte[] rateReply;
                    if (OfficeReadRateRaw(out rateReply) && TryParseOfficePollingRate(rateReply, out hz))
                    {
                        CachedOfficePollingRate = hz;
                    }

                    officePerfUpdated = DateTime.Now;
                }

                if (online) SaveOfficeCache();

                int dpi = CachedOfficeDpi > 0 ? CachedOfficeDpi : 0;
                int[] cachedStages = CachedOfficeDpiStages != null ? CloneIntArray(CachedOfficeDpiStages) : new int[0];
                int[] cachedStagesY = CachedOfficeDpiStagesY != null ? CloneIntArray(CachedOfficeDpiStagesY) : new int[0];
                int stageCountValue = CachedOfficeDpiStageCount > 0 ? CachedOfficeDpiStageCount : cachedStages.Length;
                int stageValue = CachedOfficeDpiStage > 0 ? CachedOfficeDpiStage : 1;

                MouseBatteryInfo info = new MouseBatteryInfo();
                info.DeviceId = "Rapoo:24AE:1870";
                info.HardwareFingerprint = "HID:24AE:1870";
                info.HardwareId = "HID:24AE:1870";
                info.DriverId = "BuiltIn:RapooOfficeMouse";
                info.Brand = "Rapoo";
                info.Category = DeviceCategory.Mouse;
                info.DeviceName = OfficeDeviceName;
                info.Transport = "2.4G 无线";
                info.IsDonglePresent = true;
                info.IsConnected = online;
                info.IsSleeping = !online;
                info.IsCustom = false;
                info.BatteryPercent = battery;
                info.ExactBatteryPercent = battery;
                info.IsCharging = charging;
                info.IsChargingUnknown = chargingUnknown;
                info.LastUpdated = CachedOfficeLastUpdated == DateTime.MinValue ? DateTime.Now : CachedOfficeLastUpdated;

                info.Dpi = dpi;
                info.DpiY = dpi;
                info.DpiStage = stageValue;
                info.DpiStageCount = stageCountValue;
                info.DpiStages = cachedStages;
                info.DpiStagesY = cachedStagesY;
                info.IsXyIndependent = false;
                info.XyRatio = 1.0;
                info.PollingRate = CachedOfficePollingRate;
                info.SupportedPollingRates = (int[])OfficeSupportedPollingRates.Clone();
                info.MaxPollingRate = 1000;
                info.SupportsPollingRateWrite = true;

                DeviceCapabilities caps = new DeviceCapabilities();
                caps.SupportsBattery = true;
                caps.SupportsDpi = true;
                caps.SupportsDpiStages = true;
                caps.SupportsPollingRate = true;
                caps.SupportsXyIndependence = false;
                caps.SupportsHardwareDpiMonitor = true;
                caps.SupportedPollingRates = (int[])OfficeSupportedPollingRates.Clone();
                caps.MinDpi = 50;
                caps.MaxDpi = 4000;
                caps.DpiStep = 50;
                caps.SensorDescription = "雷柏 MT760 系列办公光学传感器 (50–4,000 DPI，7 档)";
                info.Capabilities = caps;

                return info;
            }
        }

        public static bool FastQueryOfficeDpi(out int curDpiX, out int curDpiY, out int activeStage, out int stageCount)
        {
            curDpiX = 0;
            curDpiY = 0;
            activeStage = 0;
            stageCount = 0;

            lock (officeLock)
            {
                if (IsOfficeQuiet() || (DateTime.Now - officePerfUpdated).TotalMilliseconds < 250)
                {
                    return TryUseOfficeDpiCache(out curDpiX, out curDpiY, out activeStage, out stageCount);
                }

                int[] stages;
                int count;
                int stage;
                byte[] response;
                if (OfficeReadDpiRaw(out response) && TryParseOfficeDpiConfig(response, out stages, out count, out stage))
                {
                    CachedOfficeDpiStages = stages;
                    CachedOfficeDpiStagesY = CloneIntArray(stages);
                    CachedOfficeDpiStageCount = count;
                    CachedOfficeDpiStage = stage;
                    CachedOfficeDpi = stages[stage - 1];
                    CachedOfficeDpiY = CachedOfficeDpi;
                    officePerfUpdated = DateTime.Now;

                    curDpiX = CachedOfficeDpi;
                    curDpiY = CachedOfficeDpi;
                    activeStage = stage;
                    stageCount = count;
                    return true;
                }

                return TryUseOfficeDpiCache(out curDpiX, out curDpiY, out activeStage, out stageCount);
            }
        }

        public static bool FastQueryOfficePollingRate(out int hz)
        {
            hz = 0;

            lock (officeLock)
            {
                if (IsOfficeQuiet() || (DateTime.Now - officePerfUpdated).TotalMilliseconds < 500)
                {
                    if (CachedOfficePollingRate > 0)
                    {
                        hz = CachedOfficePollingRate;
                        return true;
                    }
                    return false;
                }

                int value;
                byte[] response;
                if (OfficeReadRateRaw(out response) && TryParseOfficePollingRate(response, out value))
                {
                    CachedOfficePollingRate = value;
                    officePerfUpdated = DateTime.Now;
                    hz = value;
                    return true;
                }

                if (CachedOfficePollingRate > 0)
                {
                    hz = CachedOfficePollingRate;
                    return true;
                }
                return false;
            }
        }

        public static bool SetOfficeDpi(int dpiX, int dpiY, int stage)
        {
            // MT760 办公系列每档只有一个 DPI 值，没有 XY 独立能力。
            if (dpiY > 0 && dpiY != dpiX) return false;

            lock (officeLock)
            {
                int[] stages;
                int count;
                int currentStage;
                byte[] response;
                if (!OfficeReadDpiRaw(out response)) return false;
                if (!TryParseOfficeDpiConfig(response, out stages, out count, out currentStage)) return false;

                int target = stage > 0 ? stage : currentStage;
                if (target < 1 || target > count) return false;

                if (dpiX <= 0)
                {
                    return UpdateOfficeHardwareStages(target, stages, stages);
                }

                int normalized = NormalizeOfficeDpi(dpiX);
                if (stage > 0 && stages[target - 1] == normalized)
                {
                    // 点选已有档位：只切换当前档，不重写数值。
                    return UpdateOfficeHardwareStages(target, stages, stages);
                }

                int[] next = CloneIntArray(stages);
                next[target - 1] = normalized;
                return UpdateOfficeHardwareStages(target, next, next);
            }
        }

        public static bool SetOfficeDpiStage(int stage)
        {
            lock (officeLock)
            {
                int[] stages;
                int count;
                int currentStage;
                byte[] response;
                if (!OfficeReadDpiRaw(out response)) return false;
                if (!TryParseOfficeDpiConfig(response, out stages, out count, out currentStage)) return false;
                return UpdateOfficeHardwareStages(stage, stages, stages);
            }
        }

        public static bool UpdateOfficeHardwareStages(int activeStage, int[] stagesX, int[] stagesY)
        {
            if (stagesX == null || stagesX.Length < 1 || stagesX.Length > 7) return false;
            if (stagesY == null) stagesY = stagesX;
            if (stagesY.Length != stagesX.Length) return false;

            for (int i = 0; i < stagesX.Length; i++)
            {
                if (!IsValidOfficeDpi(stagesX[i])) return false;
                if (stagesY[i] != stagesX[i]) return false;
            }

            lock (officeLock)
            {
                byte[] configReply;
                if (!OfficeReadDpiRaw(out configReply)) return false;
                if (configReply == null || configReply.Length < 28 || configReply[0] != 0x01) return false;

                int[] baselineStages;
                int baselineCount;
                int baselineCurrent;
                if (!TryParseOfficeDpiConfig(configReply, out baselineStages, out baselineCount, out baselineCurrent)) return false;

                byte[] auxReply;
                if (!OfficeReadAuxRaw(out auxReply)) return false;
                if (auxReply == null || auxReply.Length < 8 || auxReply[0] != 0x01) return false;

                // 官方 setter 的安全前提：0x0648 辅助块与 0x0638 配置块第 16..19 字节重叠一致。
                if (!BytesEqual(auxReply, 4, configReply, 20, 4)) return false;
                if (configReply[19] != 0) return false;

                int count = stagesX.Length;
                int target = activeStage > 0 ? activeStage : baselineCurrent;
                if (target < 1 || target > count) return false;

                byte[] data = new byte[OfficeDpiBlockLength];
                Array.Copy(configReply, 4, data, 0, OfficeDpiBlockLength);

                for (int i = 0; i < count; i++)
                {
                    int units = stagesX[i] / 50;
                    data[i * 2] = (byte)(units & 0xFF);
                    data[i * 2 + 1] = (byte)((units >> 8) & 0xFF);
                }

                data[14] = (byte)(count - 1);
                data[15] = 0;
                data[16] = (byte)(target - 1);
                data[17] = auxReply[5];
                data[18] = auxReply[6];
                data[19] = auxReply[7];

                byte[] writeRequest = BuildOfficeWriteRequest(OfficeDpiAddress, data);
                byte[] writeReply;
                if (!OfficeExchange(writeRequest, out writeReply)) return false;
                if (writeReply == null || writeReply[0] != 0x01) return false;

                // 单次写入后必须回读校验，失败即视为状态不确定，绝不自动重写。
                byte[] readback;
                if (!OfficeReadDpiRaw(out readback)) return false;
                if (readback == null || readback.Length < 28 || readback[0] != 0x01) return false;
                if (!BytesEqual(readback, 4, data, 0, OfficeDpiBlockLength)) return false;

                int[] verifiedStages;
                int verifiedCount;
                int verifiedStage;
                if (!TryParseOfficeDpiConfig(readback, out verifiedStages, out verifiedCount, out verifiedStage)) return false;
                if (verifiedCount != count || verifiedStage != target) return false;

                CachedOfficeDpiStages = verifiedStages;
                CachedOfficeDpiStagesY = CloneIntArray(verifiedStages);
                CachedOfficeDpiStageCount = verifiedCount;
                CachedOfficeDpiStage = verifiedStage;
                CachedOfficeDpi = verifiedStages[verifiedStage - 1];
                CachedOfficeDpiY = CachedOfficeDpi;
                officePerfUpdated = DateTime.Now;
                SetOfficeQuietPeriod(600);
                SaveOfficeCache();
                return true;
            }
        }

        public static bool SetOfficePollingRate(int rateHz)
        {
            if (!IsSupportedOfficeRate(rateHz)) return false;
            byte code = EncodeOfficeRateCode(rateHz);
            if (code == 0) return false;

            lock (officeLock)
            {
                byte[] readReply;
                if (!OfficeReadRateRaw(out readReply)) return false;
                if (readReply == null || readReply.Length < 12 || readReply[0] != 0x01) return false;

                byte[] baseline = new byte[OfficeRateBlockLength];
                Array.Copy(readReply, 4, baseline, 0, OfficeRateBlockLength);

                if (DecodeOfficeRateCode(baseline[0]) <= 0) return false;
                if (DecodeOfficeRateCode(baseline[1]) <= 0) return false;

                // 官方 setter 会把有线/无线两个字节写成同一编码，并保留后续未知字节。
                byte[] data = new byte[] { code, code, baseline[2], baseline[3] };
                byte[] writeRequest = BuildOfficeWriteRequest(OfficeRateAddress, data);
                byte[] writeReply;
                if (!OfficeExchange(writeRequest, out writeReply)) return false;
                if (writeReply == null || writeReply[0] != 0x01) return false;

                // 回报率切换可能引起接收器 USB 链路重初始化：只重试只读回读，绝不重发写入。
                Thread.Sleep(300);
                byte[] readback;
                if (!OfficeReadRateRaw(out readback))
                {
                    CloseOfficeHandle();
                    Thread.Sleep(300);
                    if (!OfficeReadRateRaw(out readback)) return false;
                }
                if (readback == null || readback.Length < 12 || readback[0] != 0x01) return false;

                byte[] verified = new byte[OfficeRateBlockLength];
                Array.Copy(readback, 4, verified, 0, OfficeRateBlockLength);
                if (verified[0] != code || verified[1] != code) return false;
                for (int i = 2; i < OfficeRateBlockLength; i++)
                {
                    if (verified[i] != baseline[i]) return false;
                }

                CachedOfficePollingRate = rateHz;
                officePerfUpdated = DateTime.Now;
                SetOfficeQuietPeriod(600);
                SaveOfficeCache();
                return true;
            }
        }

        private static bool TryUseOfficeDpiCache(out int curDpiX, out int curDpiY, out int activeStage, out int stageCount)
        {
            curDpiX = 0;
            curDpiY = 0;
            activeStage = 0;
            stageCount = 0;

            if (CachedOfficeDpiStages == null || CachedOfficeDpiStages.Length == 0) return false;
            if (CachedOfficeDpiStage < 1 || CachedOfficeDpiStage > CachedOfficeDpiStages.Length) return false;

            curDpiX = CachedOfficeDpiStages[CachedOfficeDpiStage - 1];
            curDpiY = curDpiX;
            activeStage = CachedOfficeDpiStage;
            stageCount = CachedOfficeDpiStages.Length;
            return true;
        }

        private static bool IsValidOfficeDpi(int dpi)
        {
            return dpi >= 50 && dpi <= 4000 && (dpi % 50) == 0;
        }

        private static int NormalizeOfficeDpi(int dpi)
        {
            if (dpi < 50) return 50;
            if (dpi > 4000) return 4000;
            return ((dpi + 25) / 50) * 50;
        }

        private static bool IsSupportedOfficeRate(int hz)
        {
            return hz == 125 || hz == 250 || hz == 500 || hz == 1000;
        }

        private static byte EncodeOfficeRateCode(int hz)
        {
            switch (hz)
            {
                case 125: return 0x08;
                case 250: return 0x04;
                case 500: return 0x02;
                case 1000: return 0x01;
                default: return 0;
            }
        }

        private static int DecodeOfficeRateCode(byte code)
        {
            switch (code)
            {
                case 0x08: return 125;
                case 0x04: return 250;
                case 0x02: return 500;
                case 0x01: return 1000;
                case 0x84: return 2000;
                case 0x82: return 4000;
                case 0x81: return 8000;
                default: return 0;
            }
        }

        private static byte[] BuildOfficeReadRequest(int address, int length)
        {
            byte[] request = new byte[OfficeReportLength];
            request[0] = OfficeReportId;
            request[1] = OfficeConnDongle;
            request[2] = OfficeOpRead;
            request[3] = (byte)length;
            request[4] = (byte)(address & 0xFF);
            request[5] = (byte)((address >> 8) & 0xFF);
            return request;
        }

        private static byte[] BuildOfficeWriteRequest(int address, byte[] data)
        {
            byte[] request = new byte[OfficeReportLength];
            request[0] = OfficeReportId;
            request[1] = OfficeConnDongle;
            request[2] = OfficeOpWrite;
            request[3] = (byte)(data != null ? data.Length : 0);
            request[4] = (byte)(address & 0xFF);
            request[5] = (byte)((address >> 8) & 0xFF);
            if (data != null && data.Length > 0)
            {
                Array.Copy(data, 0, request, 8, Math.Min(data.Length, OfficeReportLength - 8));
            }
            return request;
        }

        private static bool OfficeReadDpiRaw(out byte[] response)
        {
            return OfficeExchangeRead(BuildOfficeReadRequest(OfficeDpiAddress, OfficeDpiReadLength), out response)
                && response != null && response[0] == 0x01;
        }

        private static bool OfficeReadAuxRaw(out byte[] response)
        {
            return OfficeExchangeRead(BuildOfficeReadRequest(OfficeDpiAuxAddress, OfficeAuxReadLength), out response)
                && response != null && response[0] == 0x01;
        }

        private static bool OfficeReadRateRaw(out byte[] response)
        {
            return OfficeExchangeRead(BuildOfficeReadRequest(OfficeRateAddress, OfficeRateBlockLength), out response)
                && response != null && response[0] == 0x01;
        }

        /// <summary>
        /// 只读指令允许一次整帧重试（读操作幂等）；写指令严禁走此路径，必须保持单次写入。
        /// </summary>
        private static bool OfficeExchangeRead(byte[] request, out byte[] response)
        {
            if (OfficeExchange(request, out response)) return true;
            Thread.Sleep(120);
            return OfficeExchange(request, out response);
        }

        private static bool OfficeExchange(byte[] request, out byte[] response)
        {
            response = null;

            lock (officeLock)
            {
                if (!EnsureOfficeHandle()) return false;

                byte[] outBuffer = new byte[OfficeReportLength];
                if (request != null && request.Length > 0)
                {
                    Array.Copy(request, 0, outBuffer, 0, Math.Min(request.Length, OfficeReportLength));
                }

                // 2.4G 链路需要给上一条指令留出处理时间，避免背靠背 SetOutputReport 被固件丢弃。
                int sinceLast = (int)(DateTime.Now - officeLastExchange).TotalMilliseconds;
                if (sinceLast >= 0 && sinceLast < 30) Thread.Sleep(30 - sinceLast);

                officeLastExchange = DateTime.Now;
                if (!RazerDeviceHelper.HidD_SetOutputReport(officeHandle, outBuffer, OfficeReportLength))
                {
                    CloseOfficeHandle();
                    return false;
                }

                // 0x02 = 设备忙：只轮询 GetInputReport，绝不重发指令。
                for (int attempt = 0; attempt < 24; attempt++)
                {
                    Thread.Sleep(attempt == 0 ? 20 : 18);

                    byte[] inBuffer = new byte[OfficeReportLength];
                    inBuffer[0] = OfficeReportId;
                    if (!RazerDeviceHelper.HidD_GetInputReport(officeHandle, inBuffer, OfficeReportLength))
                    {
                        // 瞬时读失败不立即关闭句柄，避免触发 1 秒枚举限流；若设备已拔出，
                        // 下一次 SetOutputReport 会失败并统一回收句柄。
                        return false;
                    }

                    if (inBuffer[0] == 0x02) continue;

                    response = inBuffer;
                    return true;
                }

                return false;
            }
        }

        private static bool EnsureOfficeHandle()
        {
            if (officeHandle != IntPtr.Zero && officeHandle.ToInt64() != -1) return true;

            if ((DateTime.Now - officeLastEnum).TotalMilliseconds < 1000) return false;
            officeLastEnum = DateTime.Now;

            string path = FindOfficeEndpointPath();
            if (string.IsNullOrEmpty(path)) return false;

            IntPtr handle = RazerDeviceHelper.CreateFile(path, GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;

            officeHandle = handle;
            officePath = path;
            return true;
        }

        private static void CloseOfficeHandle()
        {
            if (officeHandle != IntPtr.Zero)
            {
                try { RazerDeviceHelper.CloseHandle(officeHandle); }
                catch { }
                officeHandle = IntPtr.Zero;
            }
            officePath = null;
        }

        private static string FindOfficeEndpointPath()
        {
            Guid hidGuid;
            RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
            IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
            if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return null;

            RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
            uint memberIdx = 0;

            try
            {
                while (true)
                {
                    ifData.cbSize = Marshal.SizeOf(ifData);
                    if (!RazerDeviceHelper.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx, ref ifData)) break;
                    memberIdx++;

                    uint requiredSize;
                    RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out requiredSize, IntPtr.Zero);
                    if (requiredSize == 0) continue;

                    IntPtr detailBuffer = Marshal.AllocHGlobal((int)requiredSize);
                    try
                    {
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);
                        if (!RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, requiredSize, out requiredSize, IntPtr.Zero)) continue;

                        IntPtr pPath = new IntPtr(detailBuffer.ToInt64() + 4);
                        string path = Marshal.PtrToStringAuto(pPath);
                        if (string.IsNullOrEmpty(path)) continue;

                        string lower = path.ToLowerInvariant();
                        if (lower.IndexOf("vid_24ae", StringComparison.Ordinal) < 0) continue;
                        if (lower.IndexOf("pid_1870", StringComparison.Ordinal) < 0) continue;

                        IntPtr probe = RazerDeviceHelper.CreateFile(path, 0,
                            RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                            IntPtr.Zero, RazerDeviceHelper.OPEN_EXISTING, 0, IntPtr.Zero);
                        if (probe == IntPtr.Zero || probe.ToInt64() == -1) continue;

                        try
                        {
                            RazerDeviceHelper.HIDD_ATTRIBUTES attributes = new RazerDeviceHelper.HIDD_ATTRIBUTES();
                            attributes.Size = Marshal.SizeOf(attributes);
                            if (!RazerDeviceHelper.HidD_GetAttributes(probe, ref attributes)) continue;

                            RazerDeviceHelper.HIDP_CAPS caps = new RazerDeviceHelper.HIDP_CAPS();
                            IntPtr preparsed;
                            if (!RazerDeviceHelper.HidD_GetPreparsedData(probe, out preparsed)) continue;
                            try
                            {
                                RazerDeviceHelper.HidP_GetCaps(preparsed, out caps);
                            }
                            finally
                            {
                                RazerDeviceHelper.HidD_FreePreparsedData(preparsed);
                            }

                            if (IsOfficeEndpointCaps(attributes.VendorID, attributes.ProductID,
                                caps.UsagePage, caps.Usage,
                                caps.InputReportByteLength, caps.OutputReportByteLength, caps.FeatureReportByteLength))
                            {
                                return path;
                            }
                        }
                        finally
                        {
                            RazerDeviceHelper.CloseHandle(probe);
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

            return null;
        }

        private static void SetOfficeQuietPeriod(int milliseconds)
        {
            officeQuietUntil = DateTime.Now.AddMilliseconds(milliseconds);
        }

        private static bool IsOfficeQuiet()
        {
            return DateTime.Now < officeQuietUntil;
        }

        private static bool BytesEqual(byte[] left, int leftOffset, byte[] right, int rightOffset, int length)
        {
            if (left == null || right == null) return false;
            if (leftOffset < 0 || rightOffset < 0 || length < 0) return false;
            if (left.Length < leftOffset + length) return false;
            if (right.Length < rightOffset + length) return false;
            for (int i = 0; i < length; i++)
            {
                if (left[leftOffset + i] != right[rightOffset + i]) return false;
            }
            return true;
        }

        private static int[] CloneIntArray(int[] source)
        {
            if (source == null) return null;
            int[] clone = new int[source.Length];
            Array.Copy(source, clone, source.Length);
            return clone;
        }

        public static void LoadOfficeCache()
        {
            if (officeCacheLoaded) return;
            officeCacheLoaded = true;

            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(OfficeCacheRegistryKey, false))
                {
                    if (key == null) return;

                    object value = key.GetValue("BatteryPercent");
                    if (value != null) CachedOfficeBatteryPercent = Convert.ToInt32(value);

                    value = key.GetValue("IsCharging");
                    if (value != null) CachedOfficeIsCharging = (Convert.ToInt32(value) == 1);

                    value = key.GetValue("ChargingUnknown");
                    if (value != null) CachedOfficeChargingUnknown = (Convert.ToInt32(value) == 1);

                    value = key.GetValue("Dpi");
                    if (value != null) CachedOfficeDpi = Convert.ToInt32(value);

                    value = key.GetValue("DpiStage");
                    if (value != null) CachedOfficeDpiStage = Convert.ToInt32(value);

                    value = key.GetValue("DpiStageCount");
                    if (value != null) CachedOfficeDpiStageCount = Convert.ToInt32(value);

                    value = key.GetValue("PollingRate");
                    if (value != null) CachedOfficePollingRate = Convert.ToInt32(value);

                    string stages = key.GetValue("DpiStages") as string;
                    if (!string.IsNullOrEmpty(stages))
                    {
                        string[] parts = stages.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        List<int> parsed = new List<int>();
                        for (int i = 0; i < parts.Length; i++)
                        {
                            int dpi;
                            if (int.TryParse(parts[i].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out dpi))
                            {
                                parsed.Add(dpi);
                            }
                        }
                        if (parsed.Count > 0)
                        {
                            CachedOfficeDpiStages = parsed.ToArray();
                            CachedOfficeDpiStagesY = CloneIntArray(CachedOfficeDpiStages);
                        }
                    }

                    string updated = key.GetValue("LastUpdated") as string;
                    if (!string.IsNullOrEmpty(updated))
                    {
                        DateTime parsedTime;
                        if (DateTime.TryParse(updated, out parsedTime)) CachedOfficeLastUpdated = parsedTime;
                    }
                }
            }
            catch { }
        }

        public static void SaveOfficeCache()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(OfficeCacheRegistryKey))
                {
                    if (key == null) return;

                    if (CachedOfficeBatteryPercent >= 0) key.SetValue("BatteryPercent", CachedOfficeBatteryPercent);
                    key.SetValue("IsCharging", CachedOfficeIsCharging ? 1 : 0);
                    key.SetValue("ChargingUnknown", CachedOfficeChargingUnknown ? 1 : 0);
                    if (CachedOfficeDpi > 0) key.SetValue("Dpi", CachedOfficeDpi);
                    if (CachedOfficeDpiStage > 0) key.SetValue("DpiStage", CachedOfficeDpiStage);
                    if (CachedOfficeDpiStageCount > 0) key.SetValue("DpiStageCount", CachedOfficeDpiStageCount);
                    if (CachedOfficePollingRate > 0) key.SetValue("PollingRate", CachedOfficePollingRate);
                    if (CachedOfficeDpiStages != null && CachedOfficeDpiStages.Length > 0)
                    {
                        string text = string.Join(",", Array.ConvertAll(CachedOfficeDpiStages, delegate(int value) { return value.ToString(CultureInfo.InvariantCulture); }));
                        key.SetValue("DpiStages", text);
                    }
                    if (CachedOfficeLastUpdated != DateTime.MinValue)
                    {
                        key.SetValue("LastUpdated", CachedOfficeLastUpdated.ToString("o"));
                    }
                }
            }
            catch { }
        }
    }
}
