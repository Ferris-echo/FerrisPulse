// ============================================================================
// 模块名称: KzziDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   KZZI（珂芝）K75 三模机械键盘的 2.4G 接收器识别。
//   设备指纹: VID 0x3554 / PID 0xFA09（Compx 公版 20 字节 0x13 协议）。
//   同 PID 被 MCHOSE G75 PRO / MARVO / AULA / YUNZII 等共用，不能用 PID 直接定品牌；
//   本机通过 0x05 状态指纹识别 KZZI：
//     TX 13-05-01-00-02-...-1B
//     RX 13-05-01-00-0A-02-00-00-00-00-37-00-01-...-5D
//   开源同方案实现（AULA F75 / MARVO Titan 98）已确认该 2.4G 公版协议不提供
//   可信电量；因此 2.4G 下只上报型号与在线状态，电量走蓝牙 BLE 标准服务。
// 依赖关系: HidTransport.cs, MouseBatteryInfo.cs, HardwareDriverBase.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 修改识别指纹、协议解析或缓存策略时必须同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止 $"..." 插值、禁止 ?. 空条件运算符、禁止 nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;

namespace RazerBatteryTray
{
    public static class KzziDeviceHelper
    {
        public const int VendorId = 0x3554;
        public const int ProductId = 0xFA09;
        public const string DriverIdValue = "BuiltIn:KzziKeyboard";

        private const int ReportLength = 20;
        private const byte ReportId = 0x13;
        private const byte StatusCommand = 0x05;
        private const int ReadTimeoutMs = 400;
        private const int QueryThrottleMs = 3000;

        private static readonly object hidLock = new object();
        private static DateTime lastQueryTime = DateTime.MinValue;
        private static List<MouseBatteryInfo> cachedDevices = new List<MouseBatteryInfo>();

        public static bool IsKzziEndpoint(HidDeviceDescriptor descriptor)
        {
            if (descriptor == null) return false;
            return descriptor.VendorId == VendorId &&
                   descriptor.ProductId == ProductId &&
                   descriptor.UsagePage == 0xFF02 &&
                   descriptor.Usage == 0x0002 &&
                   descriptor.InputReportLength == ReportLength &&
                   descriptor.OutputReportLength == ReportLength;
        }

        /// <summary>
        /// 构建 0x05 状态查询帧：Report ID 0x13 + 命令 0x05 + 子命令 0x01 + 0x02 载荷。
        /// 实机 TX: 13-05-01-00-02-...-1B
        /// </summary>
        public static byte[] BuildStatusQuery()
        {
            byte[] frame = new byte[ReportLength];
            frame[0] = ReportId;
            frame[1] = StatusCommand;
            frame[2] = 0x01;
            frame[4] = 0x02;
            frame[ReportLength - 1] = CalcChecksum(frame);
            return frame;
        }

        public static byte CalcChecksum(byte[] frame)
        {
            if (frame == null || frame.Length < ReportLength) return 0;
            int sum = 0;
            for (int i = 0; i < ReportLength - 1; i++) sum += frame[i];
            return (byte)(sum & 0xFF);
        }

        /// <summary>
        /// KZZI K75 实机状态指纹：
        /// 13 05 01 00 0A 02 ... 37 00 01 ... 5D（校验和为前 19 字节低 8 位）。
        /// 该帧不用于电量；仅用于把 KZZI 从共用 PID 的公版键盘中区分出来。
        /// </summary>
        public static bool IsKzziStatusFrame(byte[] report, int length)
        {
            if (report == null || length < ReportLength || report.Length < ReportLength) return false;
            if (report[0] != ReportId || report[1] != StatusCommand) return false;
            if (report[2] != 0x01 || report[4] != 0x0A || report[5] != 0x02) return false;

            int sum = 0;
            for (int i = 0; i < ReportLength - 1; i++) sum += report[i];
            return (byte)(sum & 0xFF) == report[ReportLength - 1];
        }

        public static bool TryIdentifyKzziK75(HidDeviceDescriptor descriptor)
        {
            if (!IsKzziEndpoint(descriptor)) return false;

            IntPtr handle = HidTransport.Open(descriptor.DevicePath);
            if (!HidTransport.IsValidHandle(handle)) return false;
            try
            {
                HidTransport.DrainReports(handle, ReportLength, 6);
                byte[] tx = BuildStatusQuery();
                if (!HidTransport.WriteReport(handle, tx, ReportLength, ReadTimeoutMs)) return false;

                byte[] rx;
                if (!HidTransport.ReadReport(handle, ReportLength, ReadTimeoutMs, out rx)) return false;
                return IsKzziStatusFrame(rx, rx.Length);
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

        public static MouseBatteryInfo QueryKzziDevice()
        {
            lock (hidLock)
            {
                if ((DateTime.Now - lastQueryTime).TotalMilliseconds < QueryThrottleMs && cachedDevices != null)
                {
                    List<MouseBatteryInfo> cachedCopy = new List<MouseBatteryInfo>(cachedDevices);
                    return cachedCopy.Count > 0 ? cachedCopy[0] : null;
                }
                lastQueryTime = DateTime.Now;

                List<HidDeviceDescriptor> descriptors = HidTransport.Enumerate(VendorId, ProductId);
                HidDeviceDescriptor selected = null;
                for (int i = 0; i < descriptors.Count; i++)
                {
                    if (IsKzziEndpoint(descriptors[i]))
                    {
                        selected = descriptors[i];
                        break;
                    }
                }
                if (selected == null || !TryIdentifyKzziK75(selected))
                {
                    cachedDevices = new List<MouseBatteryInfo>();
                    return null;
                }

                MouseBatteryInfo info = new MouseBatteryInfo();
                info.DeviceId = string.Format("KZZI:{0:X4}:{1:X4}", VendorId, ProductId);
                info.HardwareFingerprint = string.Format("HID:{0:X4}:{1:X4}", VendorId, ProductId);
                info.HardwareId = selected.InstanceId;
                info.DriverId = DriverIdValue;
                info.Brand = "KZZI";
                info.Category = DeviceCategory.Keyboard;
                info.Transport = "2.4G 无线 (Compx 公版)";
                info.DeviceName = "KZZI K75";
                info.IsCustom = false;
                info.IsConnected = true;
                info.IsDonglePresent = true;
                info.IsSleeping = false;
                info.IsCharging = false;
                info.IsChargingUnknown = true;
                info.BatteryPercent = -1;
                info.ExactBatteryPercent = 0;
                info.LastUpdated = DateTime.Now;
                info.CustomTooltip = "KZZI K75 2.4G 接收器在线；该 CompX 2.4G 公版协议不提供可信电量。切换到蓝牙模式后，FerrisPulse 会自动读取 BLE 标准电量。";
                info.Capabilities.SupportsBattery = true;
                info.Capabilities.SupportsDpi = false;
                info.Capabilities.SupportsDpiStages = false;
                info.Capabilities.SupportsPollingRate = false;
                info.Capabilities.SupportsXyIndependence = false;
                info.Capabilities.SupportsHardwareDpiMonitor = false;
                info.Capabilities.ThemeColorHex = "#00BCD4";
                info.Capabilities.PerformanceCardTitle = "KZZI K75";

                List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
                result.Add(info);
                cachedDevices = new List<MouseBatteryInfo>(result);
                return info;
            }
        }
    }

    public sealed class KzziKeyboardDriver : HardwareDriverBase
    {
        public override string DriverId { get { return KzziDeviceHelper.DriverIdValue; } }
        public override string Brand { get { return "KZZI"; } }
        public override int Priority { get { return 100; } }
        public override bool IsExternalPackage { get { return false; } }

        public override bool MatchesVidPid(int vid, int pid)
        {
            return vid == KzziDeviceHelper.VendorId && pid == KzziDeviceHelper.ProductId;
        }

        public override bool CanHandle(MouseBatteryInfo dev)
        {
            if (dev == null || dev.Category != DeviceCategory.Keyboard) return false;
            if (string.Equals(dev.DriverId, DriverId, StringComparison.OrdinalIgnoreCase)) return true;
            if (string.Equals(dev.Brand, "KZZI", StringComparison.OrdinalIgnoreCase)) return true;
            return dev.DeviceId != null && dev.DeviceId.StartsWith("KZZI:3554:", StringComparison.OrdinalIgnoreCase);
        }

        public override void PopulateCapabilities(MouseBatteryInfo dev)
        {
            if (dev == null) return;
            if (dev.Capabilities == null) dev.Capabilities = new DeviceCapabilities();
            if (string.IsNullOrEmpty(dev.DriverId)) dev.DriverId = DriverId;
            dev.Capabilities.SupportsBattery = true;
            dev.Capabilities.SupportsDpi = false;
            dev.Capabilities.SupportsDpiStages = false;
            dev.Capabilities.SupportsPollingRate = false;
            dev.Capabilities.SupportsXyIndependence = false;
            dev.Capabilities.SupportsHardwareDpiMonitor = false;
            dev.Capabilities.ThemeColorHex = "#00BCD4";
            dev.Capabilities.PerformanceCardTitle = "KZZI K75";
        }

        public override List<MouseBatteryInfo> QueryDevices()
        {
            List<MouseBatteryInfo> list = new List<MouseBatteryInfo>();
            MouseBatteryInfo info = KzziDeviceHelper.QueryKzziDevice();
            if (info != null)
            {
                PopulateCapabilities(info);
                list.Add(info);
            }
            return list;
        }
    }
}
