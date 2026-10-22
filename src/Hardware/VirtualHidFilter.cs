// ============================================================================
// 模块名称: VirtualHidFilter.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   识别并过滤 Windows VHF（Virtual HID Framework，虚拟 HID 框架）设备。
//   远程控制/串流/按键映射类软件会通过内核驱动调用 VHF，向系统注册一整套
//   “虚拟键盘 + 虚拟鼠标”HID 集合；它们没有任何物理电池与硬件实体，
//   必须在设备扫描与向导中排除，避免被当成“2.4G 无线外设 / 可监控电量”。
//
// 典型实例（本机实测，2026-10）:
//   - 设备：VHF\HID_DEVICE_SYSTEM_VHF\...\{6225EBE9-...}
//   - HardwareIds：HID_DEVICE_SYSTEM_VHF / VHF\VID_5E2A&PID_71B4
//   - 子集合：COL01 键盘（kbdhid）、COL02/COL03 鼠标
//   - 创建者：网易 UU 远程（UU Remote）的 uuycinput.sys，LowerFilters=vhf
//   注意：VID 0x5E2A / PID 0x71B4 只是虚拟标识，不是 USB-IF 注册厂商，
//   不同软件可能使用不同虚拟 VID/PID，因此这里按 VHF 总线结构识别，
//   而不是写死某个厂商 ID，避免漏掉其它远程工具的虚拟键鼠。
//
// 依赖关系: 无（纯字符串结构识别，可被 GenericHidHelper / DeviceManager 复用）
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 修改识别规则时必须同步更新 docs/AGENT_HANDBOOK.md 3.10 节说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止字符串插值、空条件运算符、nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;

namespace RazerBatteryTray
{
    public static class VirtualHidFilter
    {
        /// <summary>
        /// 按设备接口路径识别 VHF 虚拟 HID。
        /// 典型路径：
        /// \\?\hid#hid_device_system_vhf&col01#7&...&0&0000#{4d1e55b2-...}
        /// </summary>
        public static bool IsVirtualHidPath(string devicePath)
        {
            if (string.IsNullOrEmpty(devicePath)) return false;
            return IsVirtualHidToken(devicePath.ToLowerInvariant());
        }

        /// <summary>
        /// 按 PnP InstanceId / 硬件 ID 识别 VHF 虚拟 HID。
        /// 典型：
        /// HID\HID_DEVICE_SYSTEM_VHF&COL01\...
        /// VHF\HID_DEVICE_SYSTEM_VHF\...\{GUID}
        /// VHF\VID_5E2A&PID_71B4
        /// </summary>
        public static bool IsVirtualHidInstance(string instanceOrHardwareId)
        {
            if (string.IsNullOrEmpty(instanceOrHardwareId)) return false;
            string lower = instanceOrHardwareId.ToLowerInvariant();
            if (lower.StartsWith("vhf\\", StringComparison.Ordinal)) return true;
            return IsVirtualHidToken(lower);
        }

        /// <summary>
        /// 供 DeviceManager 最终列表兜底过滤使用：
        /// DeviceId / HardwareFingerprint / HardwareId 任一命中 VHF 即视为虚拟设备。
        /// </summary>
        public static bool IsVirtualHidIdentity(string deviceId, string hardwareFingerprint, string hardwareId)
        {
            if (IsVirtualHidInstance(deviceId)) return true;
            if (IsVirtualHidInstance(hardwareFingerprint)) return true;
            if (IsVirtualHidInstance(hardwareId)) return true;
            return false;
        }

        private static bool IsVirtualHidToken(string lower)
        {
            // 微软 VHF 总线固定字符串：所有 VHF Source Driver 生成的 HID 都包含它。
            if (lower.Contains("hid_device_system_vhf")) return true;
            // 兼容部分驱动直接以 VHF\VID_xxxx&PID_xxxx 作为硬件 ID 的形态。
            if (lower.Contains("vhf\\vid_")) return true;
            if (lower.Contains("vhf#")) return true;
            return false;
        }
    }
}
