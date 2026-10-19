// ============================================================================
// 模块名称: MouseBatteryInfo.cs
// 所属区域: Models (数据契约层)
// 核心职责:
//   全系统统一的归一化外设数据实体，包含连接状态、电池百分比、DPI与XY轴独立灵敏度、回报率与智能命名推导
// 依赖关系: DeviceCategory.cs, RazerDeviceHelper.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、属性或添加命名映射时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（❌ 严禁 $"..." 插值，必须使用 string.Format 或 + 拼接）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Drawing;

namespace RazerBatteryTray
{
    public class MouseBatteryInfo
    {
        public string DeviceId { get; set; }
        public string Brand { get; set; } // "Razer", "Rapoo", "VGN", "Logitech", "NuPhy", "Custom", etc.
        public DeviceCategory Category { get; set; }
        public string CustomName { get; set; }
        public int PriorityIndex { get; set; }
        public string HardwareFingerprint { get; set; }
        public string HardwareId { get; set; }
        public string Transport { get; set; }
        public bool IsCustom { get; set; }

        public bool IsConnected { get; set; }
        public bool IsSleeping { get; set; }
        public bool IsDonglePresent { get; set; }
        public string DeviceName { get; set; }
        public int BatteryPercent { get; set; }
        public double ExactBatteryPercent { get; set; }
        public bool IsCharging { get; set; }
        public DateTime LastUpdated { get; set; }

        public int Dpi { get; set; }
        public int DpiY { get; set; }
        public int DpiStage { get; set; }
        public int DpiStageCount { get; set; }
        public int[] DpiStages { get; set; }
        public int[] DpiStagesY { get; set; }
        public bool IsXyIndependent { get; set; }
        public double XyRatio { get; set; }
        public int PollingRate { get; set; }
        public int[] SupportedPollingRates { get; set; }
        public bool IsPrimary { get; set; }

        public static bool IsDefaultGenericName(string name)
        {
            if (string.IsNullOrEmpty(name)) return true;
            string s = name.Trim();
            if (s.Equals("Nuphy WH80 Dongle", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("2.4G Wireless Keyboard", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("Razer Mouse", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("Razer Wireless Mouse", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("HyperPolling Wireless Dongle", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("Razer HyperPolling Wireless Dongle", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("雷蛇无线鼠标", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("雷蛇电竞鼠标", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("雷蛇无线设备", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("未知设备", StringComparison.OrdinalIgnoreCase) ||
                s.Equals("免驱鼠标设备", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("Bluetooth Device", StringComparison.OrdinalIgnoreCase) ||
                s.StartsWith("USB/2.4G 接收器", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        public string DisplayName
        {
            get
            {
                if (!string.IsNullOrEmpty(CustomName) && !IsDefaultGenericName(CustomName))
                {
                    return CustomName;
                }
                return FriendlyShortName;
            }
        }

        public string CategoryIcon
        {
            get
            {
                switch (Category)
                {
                    case DeviceCategory.Headset: return "🎧";
                    case DeviceCategory.Earbuds: return "🦻";
                    case DeviceCategory.Gamepad: return "🎮";
                    case DeviceCategory.Keyboard: return "⌨️";
                    case DeviceCategory.Dongle: return "📡";
                    case DeviceCategory.Generic: return "🔌";
                    default: return "🖱️";
                }
            }
        }

        public Color BrandColor
        {
            get
            {
                if (Category == DeviceCategory.Headset) return Color.FromArgb(90, 160, 255);
                if (Category == DeviceCategory.Earbuds) return Color.FromArgb(179, 136, 255);
                if (Category == DeviceCategory.Gamepad) return Color.FromArgb(192, 132, 252);
                if (Category == DeviceCategory.Dongle) return Color.FromArgb(255, 183, 77);
                if (Category == DeviceCategory.Generic) return Color.FromArgb(144, 164, 174);
                if (Brand == "NuPhy" || Category == DeviceCategory.Keyboard) return Color.FromArgb(0, 229, 255);
                if (Brand == "Logitech" || Brand == "罗技") return Color.FromArgb(0, 174, 239);
                if (Brand == "Rapoo") return Color.FromArgb(0, 185, 255);
                if (Brand == "VGN") return Color.FromArgb(255, 120, 0);
                return Color.FromArgb(0, 230, 118);
            }
        }

        public static string GetCategoryDisplayName(DeviceCategory cat)
        {
            switch (cat)
            {
                case DeviceCategory.Mouse: return "鼠标";
                case DeviceCategory.Keyboard: return "键盘";
                case DeviceCategory.Headset: return "头戴式耳机";
                case DeviceCategory.Earbuds: return "入耳式耳机";
                case DeviceCategory.Gamepad: return "游戏手柄";
                case DeviceCategory.Dongle: return "接收器 / 拓展坞";
                case DeviceCategory.Generic: return "其它通用外设";
                default: return "外设";
            }
        }

        public string FriendlyShortName
        {
            get
            {
                if (!string.IsNullOrEmpty(CustomName) && !IsDefaultGenericName(CustomName))
                {
                    return CustomName;
                }

                if (Category == DeviceCategory.Headset || Category == DeviceCategory.Earbuds)
                {
                    if (DeviceName != null)
                    {
                        string dn = DeviceName.Replace(" ", "");
                        if (dn.IndexOf("Buds4Pro", StringComparison.OrdinalIgnoreCase) >= 0) return "Buds4 Pro";
                        if (dn.IndexOf("Buds3Pro", StringComparison.OrdinalIgnoreCase) >= 0) return "Buds3 Pro";
                        if (dn.IndexOf("Buds", StringComparison.OrdinalIgnoreCase) >= 0) return "无线耳机";
                    }
                    return DeviceName ?? (Category == DeviceCategory.Earbuds ? "入耳式耳机" : "头戴式耳机");
                }

                if (Category == DeviceCategory.Gamepad)
                {
                    if (DeviceName != null)
                    {
                        if (DeviceName.Contains("Xbox")) return "Xbox 手柄";
                        if (DeviceName.Contains("DualSense")) return "PS5 手柄";
                        if (DeviceName.Contains("DualShock")) return "PS4 手柄";
                    }
                    return DeviceName ?? "无线手柄";
                }

                if (Brand == "NuPhy" || Category == DeviceCategory.Keyboard)
                {
                    string target = !string.IsNullOrEmpty(DeviceName) ? DeviceName : CustomName;
                    if (target != null)
                    {
                        if (target.IndexOf("WH80", StringComparison.OrdinalIgnoreCase) >= 0) return "NuPhy WH80";
                        if (target.IndexOf("Air75", StringComparison.OrdinalIgnoreCase) >= 0) return "NuPhy Air75";
                        if (target.IndexOf("Air96", StringComparison.OrdinalIgnoreCase) >= 0) return "NuPhy Air96";
                        if (target.IndexOf("Air60", StringComparison.OrdinalIgnoreCase) >= 0) return "NuPhy Air60";
                        if (target.IndexOf("Halo", StringComparison.OrdinalIgnoreCase) >= 0) return "NuPhy Halo";
                        if (target.IndexOf("Field75", StringComparison.OrdinalIgnoreCase) >= 0) return "NuPhy Field75";
                        if (target.IndexOf("NuPhy", StringComparison.OrdinalIgnoreCase) >= 0)
                        {
                            string s = target.Replace("Dongle", "").Trim();
                            if (s.Length > 0) return s;
                        }
                    }
                    return target ?? "无线键盘";
                }

                if (Brand == "VGN")
                {
                    if (DeviceName != null)
                    {
                        if (DeviceName.Contains("DragonFly F2 Pro Max") || DeviceName.Contains("F2 Pro Max")) return "蜻蜓 F2 Pro Max";
                        if (DeviceName.Contains("DragonFly F1 Pro Max") || DeviceName.Contains("F1 Pro Max")) return "蜻蜓 F1 Pro Max";
                        if (DeviceName.Contains("DragonFly F1 Pro")) return "蜻蜓 F1 Pro";
                        if (DeviceName.Contains("DragonFly F1")) return "蜻蜓 F1";
                        if (DeviceName.Contains("DragonFly F2")) return "蜻蜓 F2";
                        if (DeviceName.Contains("R1 Pro Max")) return "VXE R1 Pro Max";
                        if (DeviceName.Contains("R1 Pro")) return "VXE R1 Pro";
                        if (DeviceName.Contains("R1")) return "VXE R1";
                        if (DeviceName.Contains("DragonFly")) return "蜻蜓 无线鼠标";
                        if (DeviceName.Contains("VGN") || DeviceName.Contains("VXE") || DeviceName.Contains("ATK")) return DeviceName;
                    }
                    return "VGN 蜻蜓鼠标";
                }

                if (Brand == "Logitech" || Brand == "罗技")
                {
                    if (DeviceName != null)
                    {
                        if (DeviceName.Contains("SUPERLIGHT 2")) return "GPX 2";
                        if (DeviceName.Contains("SUPERLIGHT")) return "GPX";
                        if (DeviceName.Contains("PRO Wireless") || DeviceName.Contains("GPW")) return "GPW";
                        if (DeviceName.Contains("G502 X")) return "G502 X";
                        if (DeviceName.Contains("G502")) return "G502";
                        if (DeviceName.Contains("G304") || DeviceName.Contains("G305")) return "G304";
                        if (DeviceName.Contains("G703")) return "G703";
                        if (DeviceName.Contains("G903")) return "G903";
                        if (DeviceName.Contains("G913") || DeviceName.Contains("G915")) return "G913 TKL";
                        if (DeviceName.Contains("MX Master 3S")) return "MX Master 3S";
                        if (DeviceName.Contains("MX Master 3")) return "MX Master 3";
                        if (DeviceName.Contains("MX Anywhere")) return "MX Anywhere";
                        if (DeviceName.Length > 0) return DeviceName;
                    }
                    return "罗技无线鼠标";
                }

                if (Brand == "Rapoo")
                {
                    if (DeviceName != null)
                    {
                        if (DeviceName.Contains("VT7")) return "VT7 双模";
                        if (DeviceName.Contains("VT3 Pro Max")) return "VT3 Max";
                        if (DeviceName.Contains("VT3 Pro")) return "VT3 Pro";
                        if (DeviceName.Contains("VT3")) return "VT3 双模";
                        if (DeviceName.Contains("VT9 Pro Mini")) return "VT9 Mini";
                        if (DeviceName.Contains("VT9 Pro")) return "VT9 Pro";
                        if (DeviceName.Contains("VT9")) return "VT9 双模";
                        if (DeviceName.Contains("VT1 Pro Max")) return "VT1 Max";
                        if (DeviceName.Contains("VT1 Pro")) return "VT1 Pro";
                        if (DeviceName.Contains("VT1")) return "VT1 双模";
                        if (DeviceName.Contains("V300 Pro")) return "V300 Pro";
                        if (DeviceName.Contains("V300SE")) return "V300SE";
                        string s = DeviceName.Replace("雷柏", "").Trim();
                        if (s.Length > 0) return s;
                    }
                    return "VT 无线鼠标";
                }
                else if (Brand == "Razer")
                {
                    int pid = 0;
                    if (!string.IsNullOrEmpty(DeviceId) && DeviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase))
                    {
                        string pidHex = DeviceId.Substring("Razer:1532:".Length).Trim();
                        int.TryParse(pidHex, System.Globalization.NumberStyles.HexNumber, null, out pid);
                    }
                    if (pid == 0 && !string.IsNullOrEmpty(HardwareId) && HardwareId.StartsWith("HID:1532:", StringComparison.OrdinalIgnoreCase))
                    {
                        string pidHex = HardwareId.Substring("HID:1532:".Length).Trim();
                        int.TryParse(pidHex, System.Globalization.NumberStyles.HexNumber, null, out pid);
                    }
                    return RazerDeviceHelper.GetRazerDisplayName(pid, DeviceName);
                }
                return DeviceName ?? "未知外设";
            }
        }
    }
}
