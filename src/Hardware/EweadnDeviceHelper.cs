// ============================================================================
// 模块名称: EweadnDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   前行者 (EWEADN) X87 系列三模机械键盘适配。该键盘与配套鼠标共用一个松翰 (Sonix)
//   方案的 2.4G 复合接收器 VID 0C45 / PID FEFE（USB BusReportedDeviceDesc = "2.4G Dongle"），
//   接收器同时枚举出键盘与鼠标两套 HID 集合，本 Helper 负责：
//     · 按精确四元组（VID + PID + UsagePage/Usage + 精确报告长度）识别 X87 的键盘通道，
//       与同一接收器下的鼠标/多媒体通道物理隔离，不互相污染状态缓存；
//     · 判定连接形态（2.4G 无线 / USB 有线）；
//     · 向设备列表提供可展示的型号与分类信息。
// 依赖关系: HidTransport.cs, MouseBatteryInfo.cs, DriverStateStore.cs
//
// ⚠️ 电量功能说明（2026-11-07 本机实测，务必保留此结论）:
//   该接收器上不存在「可读电量寄存器」。下列全部通道经穷举验证，均无电量字段：
//     1. 0xFF60 / 0x61 / 33 字节（VIA 风格配置通道）：
//        命令字 0x00..0xFF 全扫，只应答两类 Chunked 数据块 ——
//          type 0x12 = 键位映射表（负载为 29/3A/3B/3C... 等 HID usage 码，即 ESC/F1/F2/F3）；
//          type 0x33 = RGB 灯效矩阵。
//        块头 00 55 <type> <len> <offLo> <offMid> <offHi> 00 00 <payload>，按 offset 可完整重组，
//        整个 blob 内不存在随电量变化的百分比字段；
//     2. 0xFF59 / 0x61 / 65 字节：固定 7 字节信标 00 0F 01 01 11 00 00，不发数据即自发，
//        与输入/按键无关，无电量语义；
//     3. 0xFFFF / 0x0001 / 4 字节（report id 5）：12~20 秒被动监听零帧，无输出；
//     4. 键盘标准集合 0x0001/0x0006、鼠标集合 0x0001/0x0002、多媒体 0x000C/0x0001：
//        均为标准输入描述符，无 Battery Strength (UsagePage 0x06 / Usage 0x20) 集合；
//     5. 接收器未通过 HID 暴露任何 Feature Report（全部集合 FeatureReportLength = 0）。
//   进一步的完整排查（2026-11-07，逐项实测/静态验证）：
//     7. 标准 HID 电量用法：对全部 9 个集合调用 HidP_GetUsageValue(0/2, 0x06, 0, 0x20)
//        均返回 0xC0110003（HIDP_STATUS_USAGE_NOT_FOUND），固件未声明 Battery Strength。
//     8. 官方网页驱动 A（https://eweadn.i-game.tech/，React + WebHID）：设备表只含
//        vendorId 0x28E9 / 0x056D 与 0x31xx 系列 PID；全 bundle 检索 65278(0xFEFE)、3141(0x0C45)
//        零命中 —— 不覆盖本接收器。其 getPoorNum 电量协议（帧 [cmd,00,00,0x38,crcLo,crcHi,00,00,00,payload]，
//        sum 累加校验；应答 n[7]=百分比、n[8]=0 未充/1 充电/2 充满、n[9]=低电标志）仅适用于那些型号。
//     9. 官方网页驱动 B（https://eweadn.driveall.cn/）：设备表确实含
//        {vendorId:3141, productId:65278, usagePage:65376(0xFF60), usage:97}，
//        但其 HID 组帧为 buildPkt_Init → [0xAA, cmd, len, offLo, offHi]、reportId 固定 0、
//        主用于 0xFF68 家族；其初始化命令 16/17/19/20/23 解析的字段只有版本、宏上限、
//        N 键、扫描率、稳定度、组合键、MAC、分区、回报率、自动校准、灯效 —— **全程无电量字段**。
//    10. 官方桌面驱动 EHUB 的 iot_manager 二进制静态扫描：percent/battery 等关键字全部命中
//        Rust 依赖库噪声，无电量业务代码。
//    11. 本接收器 FF60/61 通道对**任意写入都只做逐字节回环回显**：
//        TX 00 55 8F 00 E4 ... => RX 00 55 8F 00 E4 ...；TX AA 10 00 00 ... => RX 00 55 00 00 ...
//        换用 driveall 的 0xAA 协议、getPoorNum 的 0x8F/0x1F、以及 25~33/250~255 等候选命令码，
//        无一例产生真实数据应答。此前偶发捕获的非零数据块属设备主动上报的键位表(type 0x12)/
//        灯效矩阵(type 0x33)，且不随电量变化。
//
//   ★★ 官方网页驱动 hub.eweadn.cn 的判定逻辑（决定性证据）：
//     该驱动区分两种形态：
//       · 键盘本体 USB 直连 = VID 0x0C45 / PID 0x8006 / productName "X87"，
//         且必须在端点列表中找到 **UsagePage 0xFF68 / Usage 0x0061** 才认为「支持驱动」；
//       · 若找到 0x8006 但**没有** 0xFF68/0x61，则直接弹窗
//         「键盘和接收器的固件不支持驱动，请点击确定升级固件，升级后可支持驱动」（函数 kSe）。
//     另有 RSe 判定：同一个 0xFF60/0x61 端点出现 **≥2 次**时也会触发升级提示。
//     本机实测（2026-11-07）：
//       · 接收器 0C45:FEFE 的全部 9 个端点里只有 0xFF59/0x61 与 0xFF60/0x61，
//         **不存在 0xFF68/0x61**；
//       · 键盘本体 0C45:8006 当前不在线（正使用 2.4G）。
//     结论：当前固件形态下，官方驱动自己也会判定为「不支持・需升级」，
//     这与本 Helper 在 2.4G 上探不到电量的实测完全吻合。
//     即：2.4G 电量不可读是**固件能力**问题，不是本实现的缺口。
//
//   ★ 电量确实存在，但只在**蓝牙 (BLE) 模式**下可得：
//     注册表 HKLM\SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Devices\cc415e28335e
//     记录该键盘蓝牙名 "X87-5.0-BT2"，VID=3141(0x0C45) / PID=65278(0xFEFE) / REV=0138
//     与 USB 接收器完全一致，并暴露标准 GATT Battery Service (0x180F) 与 Battery Level (0x2A19)。
//     即蓝牙模式下电量由标准 GATT 提供，与厂商私有 HID 无关；由 EweadnBleBatteryHelper 读取。
//     2.4G 接收器未把电量转发到 USB 侧，故 2.4G 下不可读 —— 这是设备固件行为，不是本实现的缺口。
//
//   因此本 Helper **在 2.4G 形态下不提供电量读取**，也**不注册任何伪造电量**。
//   禁止为了让界面「有数字」而填入常量或估算值 —— 那会误导用户。
//   若需电量：让键盘切换到蓝牙模式，由 BLE 标准 Battery Service 提供（待实现 BLE 通道适配）。
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 修改识别条件、通道判定或分类逻辑时必须同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止字符串插值、空条件运算符、nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;

namespace RazerBatteryTray
{
    public static class EweadnDeviceHelper
    {
        // ---------- 松翰 (Sonix) 2.4G 复合接收器 ----------
        public const int VendorId = 0x0C45;
        public const int ProductIdDongle = 0xFEFE;
        // 键盘本体 USB 直连形态（官方驱动 productName "X87"）
        public const int ProductIdKeyboardUsb = 0x8006;

        // 官方驱动认定「支持驱动」的关键端点
        private const int DriverUsagePage = 0xFF68;
        private const int DriverUsage = 0x0061;

        public const string BrandName = "EWEADN";
        public const string BrandDisplayName = "前行者";
        public const string DefaultModelName = "前行者 X87";

        public const string TransportWireless = "2.4G 无线";
        public const string TransportWired = "USB 有线";
        public const string TransportBluetooth = "蓝牙";

        // ---------- 精确四元组（AGENT_HANDBOOK 3.4-3：共享公版 VID 必须联合校验） ----------
        // 键盘业务通道：MI_00，UsagePage 0x0001 / Usage 0x0006，InputReportLength 9 / OutputReportLength 2
        private const int KeyboardUsagePage = 0x0001;
        private const int KeyboardUsage = 0x0006;
        private const int KeyboardInputLength = 9;
        private const int KeyboardOutputLength = 2;

        // 鼠标业务通道：MI_01，UsagePage 0x0001 / Usage 0x0002，InputReportLength 6
        private const int MouseUsagePage = 0x0001;
        private const int MouseUsage = 0x0002;
        private const int MouseInputLength = 6;

        /// <summary>该 VID:PID 是否为前行者所用的松翰 2.4G 复合接收器。</summary>
        public static bool IsSupportedVidPid(int vid, int pid)
        {
            return vid == VendorId && pid == ProductIdDongle;
        }

        /// <summary>按设备标识（DeviceId / InstanceId / 物理路径）判断是否属于本接收器。</summary>
        public static bool IsEweadnReceiver(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return false;
            string lower = identifier.ToLowerInvariant();
            bool vidOk = lower.IndexOf("vid_0c45", StringComparison.Ordinal) >= 0 ||
                         lower.IndexOf("0c45:", StringComparison.Ordinal) >= 0;
            bool pidOk = lower.IndexOf("pid_fefe", StringComparison.Ordinal) >= 0 ||
                         lower.IndexOf(":fefe", StringComparison.Ordinal) >= 0;
            return vidOk && pidOk;
        }

        /// <summary>
        /// 判定某个 HID 集合是否为 X87 的键盘业务通道。
        /// 必须联合校验 VID + PID + UsagePage/Usage + 精确 Input/Output 报告长度，
        /// 避免把同一接收器下的鼠标集合或同级键盘子集合误判为键盘主通道。
        /// </summary>
        public static bool IsKeyboardChannel(HidDeviceDescriptor device)
        {
            if (device == null) return false;
            if (!IsSupportedVidPid(device.VendorId, device.ProductId)) return false;
            if (device.UsagePage != KeyboardUsagePage) return false;
            if (device.Usage != KeyboardUsage) return false;
            if (device.InputReportLength != KeyboardInputLength) return false;
            if (device.OutputReportLength != KeyboardOutputLength) return false;
            if (device.DevicePath != null &&
                device.DevicePath.IndexOf("\\kbd", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return true;
            }
            // MI_00 的键盘集合在部分固件上不带 \kbd 后缀，按 InstanceId 兜底
            return device.InstanceId != null &&
                   device.InstanceId.IndexOf("MI_00", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>判定某个 HID 集合是否为同一接收器下的鼠标业务通道。</summary>
        public static bool IsMouseChannel(HidDeviceDescriptor device)
        {
            if (device == null) return false;
            if (!IsSupportedVidPid(device.VendorId, device.ProductId)) return false;
            return device.UsagePage == MouseUsagePage &&
                   device.Usage == MouseUsage &&
                   device.InputReportLength == MouseInputLength;
        }

        /// <summary>
        /// 扫描当前在线的前行者接收器，返回键盘业务通道描述符；未连接返回 null。
        /// </summary>
        public static HidDeviceDescriptor FindKeyboardChannel()
        {
            List<HidDeviceDescriptor> all = HidTransport.Enumerate(VendorId, ProductIdDongle);
            if (all == null) return null;
            for (int i = 0; i < all.Count; i++)
            {
                if (IsKeyboardChannel(all[i])) return all[i];
            }
            return null;
        }

        /// <summary>
        /// 判定连接形态：能枚举到键盘业务通道即视为接收器在线（2.4G 无线）。
        /// 这款接收器没有独立的「有线键盘」枚举路径，故不虚报 USB 有线。
        /// </summary>
        public static string ResolveTransport()
        {
            return FindKeyboardChannel() != null ? TransportWireless : "";
        }

        /// <summary>接收器是否在线。</summary>
        public static bool IsReceiverPresent()
        {
            return FindKeyboardChannel() != null;
        }

        /// <summary>
        /// 键盘本体是否以 USB 直连形态在线（0C45:8006），且带官方驱动要求的 0xFF68/0x61 端点。
        /// 官方网页驱动的 kSe() 正是用「0x8006 + 0xFF68/0x61」判定「支持驱动」；
        /// 只有 0x8006 而缺该端点时会提示升级固件。本方法据此如实反映设备能力。
        /// </summary>
        public static bool HasDriverCapableInterface()
        {
            List<HidDeviceDescriptor> all = HidTransport.Enumerate(VendorId, ProductIdKeyboardUsb);
            if (all == null) return false;
            for (int i = 0; i < all.Count; i++)
            {
                HidDeviceDescriptor d = all[i];
                if (d == null) continue;
                if (d.UsagePage == DriverUsagePage && d.Usage == DriverUsage) return true;
            }
            return false;
        }

        /// <summary>键盘本体（0C45:8006）是否在线。</summary>
        public static bool IsKeyboardBodyPresent()
        {
            List<HidDeviceDescriptor> all = HidTransport.Enumerate(VendorId, ProductIdKeyboardUsb);
            return all != null && all.Count > 0;
        }

        /// <summary>
        /// 构造键盘设备条目。
        /// 注意：SupportsBattery 明确为 false —— 本接收器不提供可读电量，绝不虚标。
        /// </summary>
        public static MouseBatteryInfo CreateKeyboardDeviceInfo()
        {
            HidDeviceDescriptor channel = FindKeyboardChannel();
            if (channel == null) return null;

            string deviceId = string.Format("EWEADN:{0:X4}:{1:X4}:KB", VendorId, ProductIdDongle);
            MouseBatteryInfo info = new MouseBatteryInfo();
            info.DeviceId = deviceId;
            info.DriverId = "BuiltIn:EweadnKeyboard";
            info.Brand = BrandName;
            info.Category = DeviceCategory.Keyboard;
            info.DeviceName = DefaultModelName;
            info.HardwareId = string.Format("HID:{0:X4}:{1:X4}", VendorId, ProductIdDongle);
            info.HardwareFingerprint = channel.InstanceId;
            info.Transport = TransportWireless;
            info.IsConnected = true;
            info.IsSleeping = false;
            info.IsDonglePresent = true;
            info.LastUpdated = DateTime.Now;

            // 电量只在蓝牙 (BLE) 模式下由标准 GATT Battery Service 提供；
            // 2.4G 接收器不转发电量，此时 QueryBattery 返回 -1，界面显示为「不可读」。
            int bleBattery = EweadnBleBatteryHelper.QueryBattery();
            bool hasBleBattery = bleBattery >= 0;

            if (hasBleBattery)
            {
                info.BatteryPercent = bleBattery;
                info.ExactBatteryPercent = bleBattery;
                info.IsCharging = false;
                info.Transport = TransportBluetooth;
            }
            else
            {
                info.BatteryPercent = -1;
                info.ExactBatteryPercent = -1;
            }

            DeviceCapabilities caps = new DeviceCapabilities();
            caps.SupportsBattery = hasBleBattery;
            caps.SupportsDpi = false;
            caps.SupportsDpiStages = false;
            caps.SupportsXyIndependence = false;
            caps.SupportsPollingRate = false;
            caps.SupportsHardwareDpiMonitor = false;
            caps.ThemeColorHex = "#4FA8D8";
            caps.PerformanceCardTitle = "键盘信息";
            info.Capabilities = caps;

            return info;
        }
    }
}
