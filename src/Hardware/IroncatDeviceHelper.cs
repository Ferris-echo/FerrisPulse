// ============================================================================
// 模块名称: IroncatDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   因科特 (Ironcat) Zero 系列 2.4G 无线鼠标电量协议适配。
//   设备指纹: VID 0x25A7 / PID 0xFA7C，产品串 "ironcat zero wireless"（Compx 方案）。
//   查询通道: UsagePage 0xFF02 / Usage 0x0002，17 字节 Feature Report (Report ID 0x08)。
//   应答通道: UsagePage 0xFF01 / Usage 0x0000，17 字节 Vendor Input Report (Report ID 0x09)。
//   实机捕获: TX 08-04-00-00-00-00-00-00-00-00-00-00-00-00-00-00-49
//             RX 09-04-00-00-00-02-5F-01-00-00-00-00-00-00-00-00-E6  (95%, 充电位=1)
//   校验和: report[16] == (byte)(85 - sum(report[0..15]))
// 依赖关系: HidTransport.cs, MouseBatteryInfo.cs, HardwareDriverBase.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、协议解析或签名时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止 $"..." 插值、禁止 ?. 空条件运算符、禁止 nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Text;

namespace RazerBatteryTray
{
    public static class IroncatDeviceHelper
    {
        public const int VendorId = 0x25A7;
        public const int ProductId = 0xFA7C;
        public const string DriverIdValue = "BuiltIn:IroncatMouse";

        private const int ReportLength = 17;
        private const byte QueryReportId = 0x08;
        private const byte ResponseReportId = 0x09;
        private const byte CmdBattery = 0x04;
        private const int ResponseTimeoutMs = 600;
        private const int QueryThrottleMs = 800;

        private static readonly object hidLock = new object();
        private static DateTime lastQueryTime = DateTime.MinValue;
        private static List<MouseBatteryInfo> cachedDevices = new List<MouseBatteryInfo>();
        private static int cachedBattery = -1;
        private static bool cachedCharging = false;
        private static int cachedVoltageMv = -1;
        private static DateTime cachedLastUpdated = DateTime.MinValue;

        /// <summary>
        /// 精确校验配置端点：25A7:FA7C 的 17 字节 FF02/0002 Feature 集合。
        /// 严禁放宽为 VID 通配，避免与同 VID 的 Attack Shark 公版方案互相污染。
        /// </summary>
        public static bool IsIroncatConfigEndpoint(int vid, int pid, int usagePage, int usage, int featureLength)
        {
            return vid == VendorId && pid == ProductId &&
                   usagePage == 0xFF02 && usage == 0x0002 &&
                   featureLength == ReportLength;
        }

        /// <summary>
        /// 精确校验应答端点：25A7:FA7C 的 17 字节 FF01/0000 Vendor Input 集合。
        /// </summary>
        public static bool IsIroncatInputEndpoint(int vid, int pid, int usagePage, int usage, int inputLength)
        {
            return vid == VendorId && pid == ProductId &&
                   usagePage == 0xFF01 && usage == 0x0000 &&
                   inputLength == ReportLength;
        }

        /// <summary>
        /// 构建电量查询帧：Report ID 0x08 + 命令 0x04 + 16 字节载荷，末字节为校验和。
        /// 实机帧: 08 04 00 ... 00 49
        /// </summary>
        public static byte[] BuildBatteryQuery()
        {
            byte[] frame = new byte[ReportLength];
            frame[0] = QueryReportId;
            frame[1] = CmdBattery;
            frame[ReportLength - 1] = CalcChecksum(frame);
            return frame;
        }

        public static byte CalcChecksum(byte[] frame)
        {
            if (frame == null || frame.Length < ReportLength) return 0;
            int sum = 0;
            for (int i = 0; i < ReportLength - 1; i++) sum += frame[i];
            return (byte)(85 - (sum & 0xFF));
        }

        /// <summary>
        /// 解析实机应答帧。仅接受 Report ID 0x09 + 命令 0x04 + 合法累加和，
        /// 电量必须落在 0..100；充电位沿用 CompX 约定 report[7] == 1。
        /// </summary>
        public static bool ParseBatteryResponse(byte[] report, int length, out int batteryPercent, out bool isCharging)
        {
            int voltageMv;
            return ParseBatteryResponse(report, length, out batteryPercent, out isCharging, out voltageMv);
        }

        public static bool ParseBatteryResponse(byte[] report, int length, out int batteryPercent, out bool isCharging, out int voltageMv)
        {
            batteryPercent = -1;
            isCharging = false;
            voltageMv = -1;

            if (report == null || length < ReportLength || report.Length < ReportLength) return false;
            if (report[0] != ResponseReportId) return false;
            if (report[1] != CmdBattery) return false;
            if (report[16] != CalcChecksum(report)) return false;

            int battery = report[6];
            if (battery < 0 || battery > 100) return false;

            int mv = (report[8] << 8) | report[9];
            if (mv >= 3000 && mv <= 4500) voltageMv = mv;

            batteryPercent = battery;
            isCharging = (report[7] == 0x01);
            return true;
        }

        public static List<MouseBatteryInfo> QueryAllIroncatDevices()
        {
            lock (hidLock)
            {
                if ((DateTime.Now - lastQueryTime).TotalMilliseconds < QueryThrottleMs && cachedDevices != null)
                {
                    return new List<MouseBatteryInfo>(cachedDevices);
                }
                lastQueryTime = DateTime.Now;

                List<HidDeviceDescriptor> descriptors = HidTransport.Enumerate(VendorId, ProductId);
                if (descriptors == null || descriptors.Count == 0)
                {
                    cachedDevices = new List<MouseBatteryInfo>();
                    return new List<MouseBatteryInfo>();
                }

                HidDeviceDescriptor config = null;
                HidDeviceDescriptor input = null;
                for (int i = 0; i < descriptors.Count; i++)
                {
                    HidDeviceDescriptor d = descriptors[i];
                    if (d == null) continue;
                    if (config == null && IsIroncatConfigEndpoint(d.VendorId, d.ProductId, d.UsagePage, d.Usage, d.FeatureReportLength))
                    {
                        config = d;
                    }
                    if (input == null && IsIroncatInputEndpoint(d.VendorId, d.ProductId, d.UsagePage, d.Usage, d.InputReportLength))
                    {
                        input = d;
                    }
                }

                if (config == null) return new List<MouseBatteryInfo>();

                string deviceName = "Ironcat Zero Wireless";
                if (!string.IsNullOrEmpty(config.ProductString)) deviceName = NormalizeProductName(config.ProductString);

                int battery = -1;
                bool charging = false;
                int voltageMv = -1;
                bool ok = false;

                if (input != null)
                {
                    IntPtr hConfig = HidTransport.Open(config.DevicePath);
                    IntPtr hInput = HidTransport.OpenRead(input.DevicePath);
                    try
                    {
                        if (HidTransport.IsValidHandle(hConfig) && HidTransport.IsValidHandle(hInput))
                        {
                            byte[] tx = BuildBatteryQuery();
                            if (HidTransport.SetFeature(hConfig, tx))
                            {
                                byte[] rx;
                                if (HidTransport.ReadReportExact(hInput, ReportLength, ResponseTimeoutMs, out rx))
                                {
                                    int parsedBattery;
                                    bool parsedCharging;
                                    int parsedVoltage;
                                    if (ParseBatteryResponse(rx, rx.Length, out parsedBattery, out parsedCharging, out parsedVoltage))
                                    {
                                        battery = parsedBattery;
                                        charging = parsedCharging;
                                        voltageMv = parsedVoltage;
                                        ok = true;
                                    }
                                }
                            }
                        }
                    }
                    catch
                    {
                        ok = false;
                    }
                    finally
                    {
                        HidTransport.Close(hConfig);
                        HidTransport.Close(hInput);
                    }
                }

                MouseBatteryInfo info = new MouseBatteryInfo();
                info.DeviceId = string.Format("Ironcat:{0:X4}:{1:X4}", VendorId, ProductId);
                info.HardwareFingerprint = string.Format("HID:{0:X4}:{1:X4}", VendorId, ProductId);
                info.HardwareId = config.InstanceId;
                info.DriverId = DriverIdValue;
                info.Brand = "Ironcat";
                info.Category = DeviceCategory.Mouse;
                info.Transport = "2.4G 无线 (USB 接收器)";
                info.DeviceName = deviceName;
                info.IsCustom = false;
                info.IsConnected = true;
                info.IsDonglePresent = true;
                info.Capabilities.SupportsBattery = true;
                info.Capabilities.SupportsDpi = false;
                info.Capabilities.SupportsDpiStages = false;
                info.Capabilities.SupportsPollingRate = false;
                info.Capabilities.SupportsXyIndependence = false;
                info.Capabilities.SupportsHardwareDpiMonitor = false;
                info.Capabilities.ThemeColorHex = "#FF5722";
                info.Capabilities.PerformanceCardTitle = "IRONCAT ZERO";

                if (ok)
                {
                    cachedBattery = battery;
                    cachedCharging = charging;
                    cachedVoltageMv = voltageMv;
                    cachedLastUpdated = DateTime.Now;
                    info.BatteryPercent = battery;
                    info.ExactBatteryPercent = battery;
                    info.IsCharging = charging;
                    info.IsSleeping = false;
                    info.BatteryVoltageMv = voltageMv > 0 ? voltageMv : 0;
                    info.LastUpdated = DateTime.Now;
                }
                else
                {
                    // 接收器在线但查询未应答：保留卡片，回显最近一次实测值，绝不伪造 0%。
                    info.BatteryPercent = cachedBattery;
                    info.ExactBatteryPercent = cachedBattery >= 0 ? cachedBattery : 0;
                    info.IsCharging = cachedBattery >= 0 ? cachedCharging : false;
                    info.IsSleeping = true;
                    info.BatteryVoltageMv = cachedVoltageMv > 0 ? cachedVoltageMv : 0;
                    info.LastUpdated = cachedBattery >= 0 ? cachedLastUpdated : DateTime.Now;
                    info.CustomTooltip = cachedBattery >= 0
                        ? "Ironcat 2.4G 接收器在线；鼠标休眠或未唤醒，当前显示上次实测电量"
                        : "Ironcat 2.4G 接收器在线；0x04 电量查询暂未返回应答";
                }

                List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
                result.Add(info);
                cachedDevices = new List<MouseBatteryInfo>(result);
                return result;
            }
        }

        private static string NormalizeProductName(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return "Ironcat Zero Wireless";
            string[] parts = raw.Trim().Split(new char[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0) return "Ironcat Zero Wireless";

            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (part.Length == 0) continue;
                if (sb.Length > 0) sb.Append(' ');
                sb.Append(char.ToUpperInvariant(part[0]));
                if (part.Length > 1) sb.Append(part.Substring(1).ToLowerInvariant());
            }
            return sb.Length > 0 ? sb.ToString() : "Ironcat Zero Wireless";
        }
    }

    public sealed class IroncatMouseDriver : HardwareDriverBase
    {
        public override string DriverId { get { return IroncatDeviceHelper.DriverIdValue; } }
        public override string Brand { get { return "Ironcat"; } }
        public override int Priority { get { return 100; } }
        public override bool IsExternalPackage { get { return false; } }

        public override bool MatchesVidPid(int vid, int pid)
        {
            return vid == IroncatDeviceHelper.VendorId && pid == IroncatDeviceHelper.ProductId;
        }

        public override bool CanHandle(MouseBatteryInfo dev)
        {
            if (dev == null || dev.Category != DeviceCategory.Mouse) return false;
            if (string.Equals(dev.DriverId, DriverId, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(dev.Brand, "Ironcat", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(dev.Brand, "因科特", StringComparison.OrdinalIgnoreCase)) return true;
            return dev.DeviceId != null && dev.DeviceId.StartsWith("Ironcat:25A7:", StringComparison.OrdinalIgnoreCase);
        }

        public override void PopulateCapabilities(MouseBatteryInfo dev)
        {
            if (dev == null) return;
            if (dev.Capabilities == null) dev.Capabilities = new DeviceCapabilities();
            if (string.IsNullOrEmpty(dev.DriverId)) dev.DriverId = DriverId;

            dev.Capabilities.SupportsBattery = true;
            // 物理事实优先：电量/充电位已实机验证；DPI 与回报率写指令尚未实机验证，一律不虚标。
            dev.Capabilities.SupportsDpi = false;
            dev.Capabilities.SupportsDpiStages = false;
            dev.Capabilities.SupportsPollingRate = false;
            dev.Capabilities.SupportsXyIndependence = false;
            dev.Capabilities.SupportsHardwareDpiMonitor = false;
            dev.Capabilities.ThemeColorHex = "#FF5722";
            dev.Capabilities.PerformanceCardTitle = "IRONCAT ZERO";
        }

        public override List<MouseBatteryInfo> QueryDevices()
        {
            List<MouseBatteryInfo> list = IroncatDeviceHelper.QueryAllIroncatDevices();
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++) PopulateCapabilities(list[i]);
            }
            return list ?? new List<MouseBatteryInfo>();
        }
    }
}
