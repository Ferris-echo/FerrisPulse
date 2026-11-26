// ============================================================================
// 模块名称: CompxFa09KeyboardHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   CompX（康普讯）公版 `0x3554:0xFA09` 2.4G 键盘接收器的「品牌/型号归属判定」与电量读取。
//
//   【为什么必须单独成模块】
//   0xFA09 是 CompX 公版参考固件的 PID，MCHOSE G75 PRO / KZZI K75 / LEOBOG Hi98 Pro /
//   MARVO Titan 98 / AULA F75 / YUNZII 等厂商共用同一套公版接收器：
//     · USB 描述符完全同质：产品串恒为 "2.4G Wireless Receiver"，制造商 "CX"；
//     · 电量协议完全同质：TX `13 4A 00 ... <前19字节和&0xFF>`，
//       RX `[5]=电量(0..100)`、`[6]=状态(0x01 电池供电 / 0x10 充电)`。
//   因此「VID + PID + UsagePage + 20/20 报告长度 + 电量帧」这一整套指纹**都无法区分品牌**，
//   绝不能把 0xFA09 直接等同某个品牌。历史实现在 MchoseDeviceHelper 里把 0xFA09 硬编码为
//   MCHOSE G75 PRO，导致 LEOBOG Hi98 Pro 被整机型误标（BUG 已确证，见 CHANGELOG）。
//
//   【唯一可用的物理事实：0x05 状态帧的子型号字节】
//   固件在同一家族的 0x05 状态查询帧回包里携带一个子型号码 `RX[5]`：
//     TX 13 05 01 00 02 00 ... 1B
//     RX 13 05 01 00 0A <子型号> ... <前19字节和&0xFF>
//   已用真实抓包确证（本机实测 2026-11-26，LEOBOG Hi98 Pro）：
//     0x02 = KZZI K75（沿用 KzziDeviceHelper 的历史判定，行为不变）
//     0x03 = LEOBOG Hi98 Pro
//   未收录的子型号**一律按中性名** "CompX 2.4G 无线键盘" 展示，并允许用户通过
//   「添加与配置外设」改成真实型号——公版硬件下不猜测品牌才是诚实的做法。
//
//   【离线记忆与缓存隔离】
//   键盘休眠时接收器可能不再转发电量帧，此时用缓存兜底；但缓存必须绑定**接收器实例路径**，
//   否则把 A 键盘的记忆电量/型号套到同 PID 的 B 键盘上，等于制造第二次误标。
// 依赖关系: HidTransport.cs, MouseBatteryInfo.cs, HardwareDriverBase.cs, Microsoft.Win32
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 修改子型号表、帧格式或缓存策略时必须同步更新上方说明与
//    docs/HARDWARE_PROTOCOLS.md 1.5 节，并为新增子型号补充真实回放样本测试。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止 $"..." 插值、禁止 ?. 空条件运算符、禁止 nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// 4. [物理事实优先]: 未实测的子型号宁可留空走中性名，严禁凭猜测把 PID 指向某个品牌。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    /// <summary>
    /// CompX 公版 0xFA09 键盘族中「已由真实抓包确证」的子型号。
    /// 取值刻意与固件 0x05 状态帧的 RX[5] 一致，便于追查。
    /// </summary>
    public enum CompxFa09Model
    {
        /// <summary>子型号未收录或固件未上报 → 中性名展示，不猜测品牌。</summary>
        Unknown = 0,
        /// <summary>KZZI K75（RX[5] = 0x02），由 KzziDeviceHelper 独立接管。</summary>
        KzziK75 = 2,
        /// <summary>LEOBOG Hi98 Pro（RX[5] = 0x03），本机实测确证。</summary>
        LeobogHi98Pro = 3
    }

    public static class CompxFa09KeyboardHelper
    {
        public const int VendorId = 0x3554;
        public const int ProductId = 0xFA09;

        public const int ReportLength = 20;
        public const byte ReportId = 0x13;
        public const byte StatusCommand = 0x05;
        public const byte BatteryCommand = 0x4A;

        /// <summary>0x05 状态回包里子型号码所在的偏移。</summary>
        public const int StatusSubtypeOffset = 5;
        /// <summary>0x05 状态回包中必须命中的结构字节：`[2]=0x01`、`[4]=0x0A`。</summary>
        private const byte StatusReplyMagic2 = 0x01;
        private const byte StatusReplyMagic4 = 0x0A;

        public const byte SubtypeKzziK75 = 0x02;
        public const byte SubtypeLeobogHi98Pro = 0x03;

        public const string NeutralBrand = "CompX";
        public const string NeutralModelName = "CompX 2.4G 无线键盘";
        public const string LeobogBrand = "LEOBOG";
        public const string LeobogModelName = "LEOBOG Hi98 Pro";
        public const string KzziBrand = "KZZI";
        public const string KzziModelName = "KZZI K75";

        public const string DriverIdValue = "BuiltIn:CompxFa09Keyboard";
        public const string TransportName = "2.4G 无线 (CompX 公版)";

        private const string CacheRegistryKey = @"Software\RazerBatteryTray\CompXFa09Cache";
        private const int ReadTimeoutMs = 320;
        private const int WriteTimeoutMs = 650;

        private static readonly object hidLock = new object();

        /// <summary>缓存的子型号；-1 表示从未成功取证。</summary>
        private static int cachedSubtype = -1;
        private static int cachedBattery = -1;
        private static bool cachedCharging = false;
        private static string cachedDeviceName = null;
        private static string cachedInstanceId = null;
        private static DateTime cachedLastUpdated = DateTime.MinValue;

        static CompxFa09KeyboardHelper()
        {
            LoadCache();
        }

        /// <summary>
        /// 公版 0xFA09 的私有控制端点：`UsagePage 0xFF02 / Usage 0x0002`，输入输出严格 20/20 字节。
        /// 与 VGN 鼠标（17/17）必须互斥，严禁使用 &gt;= 判定。
        /// </summary>
        public static bool IsVendorEndpoint(HidDeviceDescriptor descriptor)
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
        /// 构建 0x05 状态查询帧（与 KZZI K75 历史抓包一致）：
        /// `13 05 01 00 02 00 ... <前19字节和&0xFF>`，实测 TX 尾部校验为 0x1B。
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
        /// 解析 0x05 状态回包，取出子型号码。
        /// 结构断言：`[0]=0x13`、`[1]=0x05`、`[2]=0x01`、`[4]=0x0A`、前 19 字节和 = `[19]`。
        /// 实测样本：`13 05 01 00 0A 03 00 00 00 01 21 01 ... 49` → 子型号 0x03（LEOBOG Hi98 Pro）。
        /// </summary>
        public static bool TryParseStatusSubtype(byte[] report, int length, out byte subtype)
        {
            subtype = 0;
            if (report == null || length < ReportLength || report.Length < ReportLength) return false;
            if (report[0] != ReportId || report[1] != StatusCommand) return false;
            if (report[2] != StatusReplyMagic2 || report[4] != StatusReplyMagic4) return false;

            int sum = 0;
            for (int i = 0; i < ReportLength - 1; i++) sum += report[i];
            if ((byte)(sum & 0xFF) != report[ReportLength - 1]) return false;

            subtype = report[StatusSubtypeOffset];
            return true;
        }

        /// <summary>
        /// 现场读取子型号码。接收器在线但键盘休眠时该帧可能无应答，返回 false。
        /// </summary>
        public static bool TryReadSubtype(HidDeviceDescriptor descriptor, out byte subtype)
        {
            subtype = 0;
            if (!IsVendorEndpoint(descriptor)) return false;

            IntPtr handle = HidTransport.Open(descriptor.DevicePath);
            if (!HidTransport.IsValidHandle(handle)) return false;
            try
            {
                HidTransport.DrainReports(handle, ReportLength, 6);
                byte[] tx = BuildStatusQuery();
                if (!HidTransport.WriteReport(handle, tx, ReportLength, ReadTimeoutMs)) return false;

                byte[] rx;
                if (!HidTransport.ReadReport(handle, ReportLength, ReadTimeoutMs, out rx)) return false;
                return TryParseStatusSubtype(rx, rx.Length, out subtype);
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

        public static CompxFa09Model ClassifySubtype(byte subtype, bool subtypeKnown)
        {
            if (!subtypeKnown) return CompxFa09Model.Unknown;
            if (subtype == SubtypeKzziK75) return CompxFa09Model.KzziK75;
            if (subtype == SubtypeLeobogHi98Pro) return CompxFa09Model.LeobogHi98Pro;
            return CompxFa09Model.Unknown;
        }

        public static string GetBrand(CompxFa09Model model)
        {
            if (model == CompxFa09Model.KzziK75) return KzziBrand;
            if (model == CompxFa09Model.LeobogHi98Pro) return LeobogBrand;
            return NeutralBrand;
        }

        public static string GetModelName(CompxFa09Model model)
        {
            if (model == CompxFa09Model.KzziK75) return KzziModelName;
            if (model == CompxFa09Model.LeobogHi98Pro) return LeobogModelName;
            return NeutralModelName;
        }

        /// <summary>
        /// 构建 0x4A 电量查询帧：Report ID 0x13 + 命令 0x4A，校验和为前 19 字节无符号和的低 8 位。
        /// 实测 TX：`13 4A 00 ... 5D`。
        /// </summary>
        public static byte[] BuildBatteryQueryFrame()
        {
            byte[] frame = new byte[ReportLength];
            frame[0] = ReportId;
            frame[1] = BatteryCommand;
            int sum = 0;
            for (int i = 0; i < ReportLength - 1; i++) sum += frame[i];
            frame[ReportLength - 1] = (byte)(sum & 0xFF);
            return frame;
        }

        /// <summary>
        /// 解析 0x4A 电量回包：`[5]=电量(0..100)`、`[6]=状态(0x01 电池 / 0x10 充电)`。
        /// 实测样本：`13 4A 01 00 02 5E 01 ... BF` → 94% 电池供电（LEOBOG Hi98 Pro 实机）。
        /// </summary>
        public static bool TryParseBatteryResponse(byte[] report, int length, out int batteryPercent, out bool isCharging)
        {
            batteryPercent = -1;
            isCharging = false;
            if (report == null || length < ReportLength || report.Length < ReportLength) return false;
            if (report[0] != ReportId || (report[1] & 0x7F) != BatteryCommand) return false;

            int sum = 0;
            for (int i = 0; i < ReportLength - 1; i++) sum += report[i];
            if ((byte)(sum & 0xFF) != report[ReportLength - 1]) return false;

            int battery = report[5];
            byte state = report[6];
            if (battery < 0 || battery > 100) return false;
            if (state != 0x01 && state != 0x10) return false;

            batteryPercent = battery;
            isCharging = state == 0x10;
            return true;
        }

        private static bool TryQueryBattery(HidDeviceDescriptor descriptor, out int batteryPercent, out bool isCharging)
        {
            batteryPercent = -1;
            isCharging = false;
            if (!IsVendorEndpoint(descriptor)) return false;

            IntPtr handle = HidTransport.Open(descriptor.DevicePath);
            if (!HidTransport.IsValidHandle(handle)) return false;
            try
            {
                HidTransport.DrainReports(handle, ReportLength, 8);
                byte[] query = BuildBatteryQueryFrame();
                if (!HidTransport.WriteReport(handle, query, query.Length, WriteTimeoutMs)) return false;

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    byte[] response;
                    if (!HidTransport.ReadReport(handle, ReportLength, ReadTimeoutMs, out response)) continue;
                    int battery;
                    bool charging;
                    if (!TryParseBatteryResponse(response, response.Length, out battery, out charging)) continue;
                    batteryPercent = battery;
                    isCharging = charging;
                    return true;
                }
            }
            catch
            {
            }
            finally
            {
                HidTransport.Close(handle);
            }
            return false;
        }

        /// <summary>
        /// 查询当前接入的 CompX 公版 0xFA09 键盘接收器。
        /// 返回 null 的情形：接收器未接入，或子型号为 KZZI K75（该机型由 KzziDeviceHelper 独立接管，
        /// 保持历史卡片标题与「2.4G 公版不提供可信电量」的既有口径不变）。
        /// </summary>
        public static MouseBatteryInfo QueryDevice()
        {
            lock (hidLock)
            {
                List<HidDeviceDescriptor> descriptors = HidTransport.Enumerate(VendorId, ProductId);
                HidDeviceDescriptor selected = null;
                for (int i = 0; i < descriptors.Count; i++)
                {
                    if (IsVendorEndpoint(descriptors[i]))
                    {
                        selected = descriptors[i];
                        break;
                    }
                }
                if (selected == null) return null;

                byte subtype = 0;
                bool subtypeKnown = TryReadSubtype(selected, out subtype);
                CompxFa09Model model = ClassifySubtype(subtype, subtypeKnown);

                // KZZI K75：交给 KzziDeviceHelper，避免出现两张同物理设备的卡片。
                if (model == CompxFa09Model.KzziK75) return null;

                bool sameDevice = !string.IsNullOrEmpty(cachedInstanceId) &&
                                  string.Equals(cachedInstanceId, selected.InstanceId, StringComparison.OrdinalIgnoreCase);

                // 实时取证失败（键盘休眠等）时，只有当接收器实例路径与缓存一致，才允许沿用上次的子型号。
                CompxFa09Model effectiveModel = model;
                if (!subtypeKnown && sameDevice && cachedSubtype >= 0)
                {
                    effectiveModel = ClassifySubtype((byte)cachedSubtype, true);
                    subtype = (byte)cachedSubtype;
                }

                int battery;
                bool charging;
                bool ok = TryQueryBattery(selected, out battery, out charging);

                MouseBatteryInfo info = new MouseBatteryInfo();
                info.DeviceId = string.Format(CultureInfo.InvariantCulture, "{0}:{1:X4}:{2:X4}",
                    GetBrand(effectiveModel), VendorId, ProductId);
                info.HardwareFingerprint = string.Format(CultureInfo.InvariantCulture, "HID:{0:X4}:{1:X4}", VendorId, ProductId);
                info.HardwareId = selected.InstanceId;
                info.DriverId = DriverIdValue;
                info.DeviceName = GetModelName(effectiveModel);
                info.Brand = GetBrand(effectiveModel);
                info.Category = DeviceCategory.Keyboard;
                info.Transport = TransportName;
                info.IsCustom = false;
                info.IsDonglePresent = true;
                // 公版固件的 0x10「充电」位语义未实测，只上报电量、不宣称充电中。
                info.IsChargingUnknown = true;
                info.Capabilities.SupportsBattery = true;
                info.Capabilities.SupportsDpi = false;
                info.Capabilities.SupportsDpiStages = false;
                info.Capabilities.SupportsPollingRate = false;
                info.Capabilities.SupportsXyIndependence = false;
                info.Capabilities.SupportsHardwareDpiMonitor = false;
                info.Capabilities.ChargingFlagMeansUsbPoweredOnly = true;
                info.Capabilities.ThemeColorHex = (effectiveModel == CompxFa09Model.LeobogHi98Pro) ? "#FF6D00" : "#00BCD4";
                info.Capabilities.PerformanceCardTitle = info.DeviceName;

                if (ok)
                {
                    info.IsConnected = true;
                    info.IsSleeping = false;
                    info.IsCharging = false;
                    info.BatteryPercent = battery;
                    info.ExactBatteryPercent = battery;
                    info.LastUpdated = DateTime.Now;
                    info.CustomTooltip = BuildTooltip(effectiveModel, subtypeKnown, subtype);
                    SaveCache(subtypeKnown, subtype, battery, charging, info.DeviceName, selected.InstanceId);
                }
                else
                {
                    info.IsConnected = false;
                    info.IsSleeping = true;
                    bool hasMemory = sameDevice && cachedBattery >= 0;
                    info.IsCharging = false;
                    info.BatteryPercent = hasMemory ? cachedBattery : -1;
                    info.ExactBatteryPercent = hasMemory ? cachedBattery : 0;
                    info.LastUpdated = hasMemory ? cachedLastUpdated : DateTime.Now;
                    info.CustomTooltip = hasMemory
                        ? (info.DeviceName + " 接收器在线；键盘可能休眠，正在保留上次电量（0x4A 电量帧本轮无应答）")
                        : BuildTooltip(effectiveModel, subtypeKnown, subtype);
                }

                return info;
            }
        }

        private static string BuildTooltip(CompxFa09Model model, bool subtypeKnown, byte subtype)
        {
            if (model == CompxFa09Model.LeobogHi98Pro)
            {
                return "LEOBOG Hi98 Pro 2.4G 接收器（CompX 公版 0x3554:0xFA09，固件子型号 0x03）。电量取自 0x4A 状态帧 [5]；公版固件的充电位未实测，不显示充电状态。";
            }
            if (!subtypeKnown)
            {
                return "该 2.4G 接收器为 CompX 公版方案（0x3554:0xFA09），固件本轮未返回 0x05 子型号（键盘可能休眠），故按中性型号显示。电量取自 0x4A 状态帧 [5]。";
            }
            return string.Format(CultureInfo.InvariantCulture,
                "该 2.4G 接收器为 CompX 公版方案（0x3554:0xFA09），固件子型号 0x{0:X2} 尚未收录，故按中性型号显示，不猜测品牌。电量取自 0x4A 状态帧 [5]。可在「添加与配置外设」里把它改成真实型号。",
                subtype);
        }

        private static void LoadCache()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(CacheRegistryKey, false))
                {
                    if (key == null) return;
                    object subtype = key.GetValue("Subtype");
                    if (subtype != null) cachedSubtype = Convert.ToInt32(subtype, CultureInfo.InvariantCulture);
                    object batt = key.GetValue("BatteryPercent");
                    if (batt != null) cachedBattery = Convert.ToInt32(batt, CultureInfo.InvariantCulture);
                    object chg = key.GetValue("IsCharging");
                    if (chg != null) cachedCharging = Convert.ToInt32(chg, CultureInfo.InvariantCulture) == 1;
                    string name = key.GetValue("DeviceName") as string;
                    if (!string.IsNullOrEmpty(name)) cachedDeviceName = name;
                    string instance = key.GetValue("InstanceId") as string;
                    if (!string.IsNullOrEmpty(instance)) cachedInstanceId = instance;
                    string updated = key.GetValue("LastUpdated") as string;
                    if (!string.IsNullOrEmpty(updated))
                    {
                        DateTime dt;
                        if (DateTime.TryParse(updated, CultureInfo.InvariantCulture, DateTimeStyles.None, out dt)) cachedLastUpdated = dt;
                    }
                }
            }
            catch { }
        }

        private static void SaveCache(bool subtypeKnown, byte subtype, int battery, bool charging, string deviceName, string instanceId)
        {
            if (subtypeKnown) cachedSubtype = subtype;
            cachedBattery = battery;
            cachedCharging = charging;
            cachedDeviceName = deviceName;
            cachedInstanceId = instanceId;
            cachedLastUpdated = DateTime.Now;
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(CacheRegistryKey))
                {
                    if (key == null) return;
                    if (subtypeKnown) key.SetValue("Subtype", (int)subtype, RegistryValueKind.DWord);
                    key.SetValue("BatteryPercent", battery, RegistryValueKind.DWord);
                    key.SetValue("IsCharging", charging ? 1 : 0, RegistryValueKind.DWord);
                    if (!string.IsNullOrEmpty(deviceName)) key.SetValue("DeviceName", deviceName, RegistryValueKind.String);
                    if (!string.IsNullOrEmpty(instanceId)) key.SetValue("InstanceId", instanceId, RegistryValueKind.String);
                    key.SetValue("LastUpdated", DateTime.Now.ToString("o", CultureInfo.InvariantCulture), RegistryValueKind.String);
                }
            }
            catch { }
        }
    }

    public sealed class CompxFa09KeyboardDriver : HardwareDriverBase
    {
        public override string DriverId { get { return CompxFa09KeyboardHelper.DriverIdValue; } }
        public override string Brand { get { return CompxFa09KeyboardHelper.NeutralBrand; } }
        public override int Priority { get { return 100; } }
        public override bool IsExternalPackage { get { return false; } }

        /// <summary>
        /// 只接管 `0x3554:0xFA09` 的键盘卡片，且必须是本助手产出的中性名或 LEOBOG 身份。
        /// 严禁凭品牌名 "CompX" 泛化：`CompX:05AC:024F` 是另一套公版键盘（CompxWirelessKeyboardHelper），
        /// 两者协议与报告长度都不同。
        /// </summary>
        public override bool CanHandle(MouseBatteryInfo dev)
        {
            if (dev == null || dev.Category != DeviceCategory.Keyboard) return false;
            if (!string.IsNullOrEmpty(dev.DriverId) &&
                string.Equals(dev.DriverId, DriverId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            int vid;
            int pid;
            if (!HardwareDriverBase.TryExtractDeviceVidPid(dev, out vid, out pid)) return false;
            if (vid != CompxFa09KeyboardHelper.VendorId || pid != CompxFa09KeyboardHelper.ProductId) return false;

            return string.Equals(dev.Brand, CompxFa09KeyboardHelper.LeobogBrand, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(dev.Brand, CompxFa09KeyboardHelper.NeutralBrand, StringComparison.OrdinalIgnoreCase);
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
            // 公版固件 0x10 位只说明 USB 已供电，不等同电池正在充电。
            dev.Capabilities.ChargingFlagMeansUsbPoweredOnly = true;
            if (string.IsNullOrEmpty(dev.CustomTooltip))
            {
                dev.CustomTooltip = "CompX 公版 2.4G 键盘接收器（0x3554:0xFA09）：仅提供电量，不提供 DPI/回报率/灯效能力。";
            }
        }

        public override List<MouseBatteryInfo> QueryDevices()
        {
            List<MouseBatteryInfo> list = new List<MouseBatteryInfo>();
            MouseBatteryInfo info = CompxFa09KeyboardHelper.QueryDevice();
            if (info != null)
            {
                PopulateCapabilities(info);
                list.Add(info);
            }
            return list;
        }
    }
}
