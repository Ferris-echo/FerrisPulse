// ============================================================================
// 模块名称: MoondropDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   水月雨（MOONDROP）头戴式耳机 GM-01 PRO（USB 0x35D8:0x0133）的识别与电量读取。
//   协议来源：官方网页驱动 https://hub.moondroplab.tech/（WebHID，Vue SPA）。
//   设备枚举出 5 个 HID 集合，只有 【UsagePage 0x0C / Usage 0x0001 / In=Out=64】 这一个
//   集合承载厂商私有命令通道（不是 FF02 私有页！）；其它集合均为音频控制或 65 字节镜像端点。
//   帧格式（Report ID 固定 0x4B = 官方 config.reportId 75）：
//     [0]=0x4B Report ID, [1]=OP, [2]=CMD, [3]=payload 长度, [4..] payload
//     读命令的 OP=0x80，payload 长度为 0，因此只需 4 个有效字节，其余补零到 64。
//   电量命令 CMD=0x27（39）：应答 [3]=6（payload 长度），[8]=有效标志(1=有效)，
//     [9]=电量百分比 0..100。官方解码器等价于：battery = (e[8]!=0 ? e[9] : 0)。
//   其它常用命令（本驱动暂未使用，仅记录）：0x0C=固件版本（ASCII "1.5.1"）、
//     0x31=在线/接收器状态（[4]=1 表示在线）、0x2C=SKU/SN。
//   注意：官方协议没有"充电中"字段，USB 直连即持续供电，故不臆造充电状态。
// 依赖关系: HidTransport.cs, MouseBatteryInfo.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 一旦协议变化，必须同时更新上方说明、常量与 CustomTooltip 文案。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止字符串插值、空条件运算符、nameof、表达式主体）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// 4. [端点铁律]: 必须按 UsagePage/Usage + In/Out 报告长度四元组精确匹配，
//    严禁宽松地"取报告长度最大的集合"，那会命中 FF02 的 65 字节镜像端点而读不到数据。
// ============================================================================

using System;
using System.Collections.Generic;

namespace RazerBatteryTray
{
    public static class MoondropDeviceHelper
    {
        public const int VendorIdMoondrop = 0x35D8;
        public const int PidGm01Pro = 0x0133;

        // ---- 官方 WebHID 协议常量（hub.moondroplab.tech 的 GM01 Pro config）----
        public const byte ProtocolReportId = 0x4B;      // config.reportId = 75
        public const byte OpRead = 0x80;                // st.OP_READ
        public const byte OpWrite = 0x01;               // st.OP_WRITE
        public const byte CmdBatteryLevel = 0x27;       // st.BATTERY_LEVEL = 39
        public const byte CmdFirmwareVersion = 0x0C;    // st.FIRMWARE_VERSION = 12
        public const byte CmdDongleStatus = 0x31;       // st.DONGLE_STATUS = 49
        public const byte CmdSkuSn = 0x2C;              // st.SKU_SN = 44

        // 命令通道所在集合：Consumer 页 0x0C / Usage 0x0001，64 字节双向报告。
        public const int ProtocolUsagePage = 0x0C;
        public const int ProtocolUsage = 0x0001;
        public const int ProtocolReportLength = 64;

        // 应答里 payload 的最小长度与关键偏移。
        private const int BatteryPayloadLength = 6;
        private const int OffsetCommand = 2;
        private const int OffsetPayloadLength = 3;
        private const int OffsetBatteryValid = 8;
        private const int OffsetBatteryValue = 9;
        private const int QueryTimeoutMs = 1200;
        private const int MaxBatteryValue = 100;

        private const string ModuleName = "MOONDROP GM-01 PRO";

        // 读取失败时给用户看的解释（仍不臆造百分比）。
        public const string QueryFailedTooltip =
            "MOONDROP GM-01 PRO：本次未能从耳机读到电量（设备可能休眠、被音频通路占用或刚开机未就绪）。" +
            "驱动使用官方 WebHID 命令（Report ID 0x4B / CMD 0x27）查询，稍后会自动重试。";

        public static bool IsSupportedVidPid(int vid, int pid)
        {
            return vid == VendorIdMoondrop && pid == PidGm01Pro;
        }

        /// <summary>
        /// 供 GenericHidHelper 等通用扫描器跳过本机型，避免同一条耳机出现两条设备记录。
        /// </summary>
        public static bool IsManagedDevice(int vid, int pid)
        {
            return IsSupportedVidPid(vid, pid);
        }

        public static string GetTransport()
        {
            return "USB 有线";
        }

        /// <summary>
        /// 判断某个 HID 集合是否是厂商命令通道（0C:0001 + 64/64）。
        /// </summary>
        public static bool IsProtocolEndpoint(HidDeviceDescriptor descriptor)
        {
            if (descriptor == null) return false;
            return descriptor.UsagePage == ProtocolUsagePage
                && descriptor.Usage == ProtocolUsage
                && descriptor.InputReportLength == ProtocolReportLength
                && descriptor.OutputReportLength == ProtocolReportLength;
        }

        /// <summary>
        /// 构造一条 64 字节读命令帧：[0]=0x4B, [1]=0x80, [2]=cmd, [3]=payload 长度(0)，其余补零。
        /// </summary>
        public static byte[] BuildReadFrame(byte command)
        {
            byte[] frame = new byte[ProtocolReportLength];
            frame[0] = ProtocolReportId;
            frame[1] = OpRead;
            frame[2] = command;
            frame[OffsetPayloadLength] = 0;
            return frame;
        }

        /// <summary>
        /// 解析电量应答。返回 true 表示拿到了可信百分比。
        /// </summary>
        public static bool TryParseBatteryResponse(byte[] report, int length, out int percent)
        {
            percent = -1;
            if (report == null || length < OffsetBatteryValue + 1) return false;
            if (report[0] != ProtocolReportId) return false;
            if (report[1] != OpRead) return false;
            if (report[OffsetCommand] != CmdBatteryLevel) return false;
            if (report[OffsetPayloadLength] < BatteryPayloadLength) return false;
            if (report[OffsetBatteryValid] == 0) return false;
            int value = report[OffsetBatteryValue];
            if (value < 0 || value > MaxBatteryValue) return false;
            percent = value;
            return true;
        }

        /// <summary>
        /// 在指定设备路径上执行一次电量查询（写读同一句柄）。
        /// </summary>
        public static bool TryQueryBattery(string devicePath, out int percent)
        {
            percent = -1;
            if (string.IsNullOrEmpty(devicePath)) return false;

            IntPtr handle = HidTransport.Open(devicePath);
            if (!HidTransport.IsValidHandle(handle)) return false;

            try
            {
                byte[] frame = BuildReadFrame(CmdBatteryLevel);
                if (!HidTransport.WriteReport(handle, frame, frame.Length, QueryTimeoutMs)) return false;

                byte[] response = null;
                if (!HidTransport.ReadReport(handle, ProtocolReportLength, QueryTimeoutMs, out response)) return false;

                int responseLength = (response == null) ? 0 : response.Length;
                return TryParseBatteryResponse(response, responseLength, out percent);
            }
            catch (Exception)
            {
                return false;
            }
            finally
            {
                HidTransport.Close(handle);
            }
        }

        private static string GetModelName(string productString)
        {
            if (!string.IsNullOrEmpty(productString))
            {
                string name = productString.Trim();
                if (name.Length > 0) return name.ToUpperInvariant().StartsWith("MOONDROP", StringComparison.Ordinal) ? name : "MOONDROP " + name;
            }
            return ModuleName;
        }

        /// <summary>
        /// 枚举当前在位的 MOONDROP GM-01 PRO，并尝试读取真实电量。
        /// </summary>
        public static List<MouseBatteryInfo> QueryAllMoondropDevices()
        {
            List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
            List<HidDeviceDescriptor> descriptors = HidTransport.Enumerate(VendorIdMoondrop, PidGm01Pro);
            if (descriptors == null || descriptors.Count == 0) return result;

            // 精确锚定厂商命令通道；找不到说明设备处在异常状态，仍按身份输出一条（电量未知）。
            HidDeviceDescriptor anchor = null;
            HidDeviceDescriptor protocolEndpoint = null;
            for (int i = 0; i < descriptors.Count; i++)
            {
                HidDeviceDescriptor d = descriptors[i];
                if (anchor == null) anchor = d;
                if (protocolEndpoint == null && IsProtocolEndpoint(d)) protocolEndpoint = d;
            }
            if (protocolEndpoint != null) anchor = protocolEndpoint;
            if (anchor == null) return result;

            int battery = -1;
            bool queried = false;
            if (protocolEndpoint != null)
            {
                queried = TryQueryBattery(protocolEndpoint.DevicePath, out battery);
            }

            MouseBatteryInfo info = new MouseBatteryInfo();
            info.DeviceId = string.Format("MOONDROP:{0:X4}:{1:X4}", VendorIdMoondrop, PidGm01Pro);
            info.HardwareFingerprint = string.Format("HID:{0:X4}:{1:X4}", VendorIdMoondrop, PidGm01Pro);
            info.HardwareId = anchor.InstanceId;
            info.DriverId = "BuiltIn:MoondropHeadset";
            info.DeviceName = GetModelName(anchor.ProductString);
            info.Brand = "MOONDROP";
            info.Category = DeviceCategory.Headset;
            info.Transport = GetTransport();
            info.IsCustom = false;
            info.IsConnected = true;
            info.IsDonglePresent = false;
            info.IsSleeping = false;

            // 官方网页驱动确实能读到电量，因此能力为 true；单次读取失败只降级为"未知"。
            info.Capabilities.SupportsBattery = true;
            if (queried && battery >= 0)
            {
                info.BatteryPercent = battery;
                info.ExactBatteryPercent = battery;
                info.LastUpdated = DateTime.Now;
                info.CustomTooltip = null;
            }
            else
            {
                info.BatteryPercent = -1;
                info.ExactBatteryPercent = -1;
                info.LastUpdated = DateTime.MinValue;
                info.CustomTooltip = QueryFailedTooltip;
            }

            // 官方协议不含充电字段：USB 直连即持续供电，不臆造充电图标。
            info.IsCharging = false;
            info.IsChargingUnknown = true;

            result.Add(info);
            return result;
        }
    }
}
