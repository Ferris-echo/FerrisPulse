// ============================================================================
// 模块名称: BleDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   通用蓝牙低功耗 (BLE) 外设电量接入：
//   复用 BluetoothDeviceHelper 的 Windows BLE Battery Service (0x180F) 读取能力，
//   将已连接的 BLE 手柄 / 键盘 / 鼠标转换为统一 MouseBatteryInfo 卡片。
//   当前实机验证：Xbox Wireless Controller (045E:0B13, BLE) 电量 74%。
// 依赖关系: BluetoothDeviceHelper.cs, MouseBatteryInfo.cs, HardwareDriverBase.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 修改 BLE 分类、品牌映射或缓存策略时必须同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止 $"..." 插值、禁止 ?. 空条件运算符、禁止 nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;

namespace RazerBatteryTray
{
    public static class BleDeviceHelper
    {
        public const string DriverIdValue = "BuiltIn:BlePeripheral";
        private const int QueryThrottleMs = 10000;

        private static readonly object bleLock = new object();
        private static DateTime lastQueryTime = DateTime.MinValue;
        private static List<MouseBatteryInfo> cachedDevices = new List<MouseBatteryInfo>();

        public static List<MouseBatteryInfo> QueryAllBlePeripherals()
        {
            lock (bleLock)
            {
                if ((DateTime.Now - lastQueryTime).TotalMilliseconds < QueryThrottleMs && cachedDevices != null)
                {
                    return new List<MouseBatteryInfo>(cachedDevices);
                }
                lastQueryTime = DateTime.Now;

                List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
                try
                {
                    List<CustomDeviceConfig> configs = BluetoothDeviceHelper.ScanAllBluetoothDevices();
                    if (configs != null)
                    {
                        for (int i = 0; i < configs.Count; i++)
                        {
                            CustomDeviceConfig cfg = configs[i];
                            if (cfg == null) continue;
                            DeviceCategory category = cfg.Category;
                            if (category == DeviceCategory.Generic)
                            {
                                category = InferCategory(cfg.OriginalName);
                            }
                            if (category != DeviceCategory.Mouse &&
                                category != DeviceCategory.Keyboard &&
                                category != DeviceCategory.Gamepad) continue;
                            if (string.IsNullOrEmpty(cfg.HardwareFingerprint)) continue;

                            MouseBatteryInfo info = new MouseBatteryInfo();
                            info.DeviceId = "BLE:" + cfg.HardwareFingerprint;
                            info.HardwareFingerprint = cfg.HardwareFingerprint;
                            info.HardwareId = cfg.HardwareFingerprint;
                            info.DriverId = DriverIdValue;
                            info.Brand = InferBrand(cfg.OriginalName);
                            info.Category = category;
                            info.Transport = "蓝牙 (BLE)";
                            info.DeviceName = string.IsNullOrEmpty(cfg.OriginalName) ? "Bluetooth Device" : cfg.OriginalName;
                            info.IsCustom = false;
                            info.IsConnected = true;
                            info.IsDonglePresent = true;
                            info.IsSleeping = false;
                            info.IsCharging = false;
                            info.IsChargingUnknown = true;
                            info.BatteryPercent = cfg.CachedBattery >= 0 ? cfg.CachedBattery : -1;
                            info.ExactBatteryPercent = cfg.CachedBattery >= 0 ? cfg.CachedBattery : -1;
                            info.LastUpdated = DateTime.Now;
                            info.Capabilities.SupportsBattery = true;
                            info.Capabilities.SupportsDpi = false;
                            info.Capabilities.SupportsDpiStages = false;
                            info.Capabilities.SupportsPollingRate = false;
                            info.Capabilities.SupportsXyIndependence = false;
                            info.Capabilities.SupportsHardwareDpiMonitor = false;
                            info.Capabilities.ThemeColorHex = "#7E57C2";

                            result.Add(info);
                        }
                    }
                }
                catch
                {
                }

                cachedDevices = new List<MouseBatteryInfo>(result);
                return result;
            }
        }

        private static DeviceCategory InferCategory(string name)
        {
            if (string.IsNullOrEmpty(name)) return DeviceCategory.Generic;
            string lower = name.ToLowerInvariant();
            if (lower.Contains("xbox") || lower.Contains("controller") || lower.Contains("gamepad") ||
                lower.Contains("dualsense") || lower.Contains("dualshock") || lower.Contains("手柄"))
            {
                return DeviceCategory.Gamepad;
            }
            if (lower.Contains("keyboard") || lower.Contains("键盘") || lower.Contains("kzzi") ||
                lower.Contains("k75") || lower.Contains("keychron"))
            {
                return DeviceCategory.Keyboard;
            }
            if (lower.Contains("mouse") || lower.Contains("鼠标"))
            {
                return DeviceCategory.Mouse;
            }
            return DeviceCategory.Generic;
        }

        private static string InferBrand(string name)
        {
            if (string.IsNullOrEmpty(name)) return "Bluetooth";
            string lower = name.ToLowerInvariant();
            if (lower.Contains("xbox")) return "Microsoft";
            if (lower.Contains("kzzi") || lower.Contains("k75")) return "KZZI";
            if (lower.Contains("ironcat")) return "Ironcat";
            if (lower.Contains("dualsense") || lower.Contains("dualshock")) return "Sony";
            if (lower.Contains("logitech") || lower.Contains("mx ")) return "Logitech";
            if (lower.Contains("keychron")) return "Keychron";
            return "Bluetooth";
        }
    }

    public sealed class BlePeripheralDriver : HardwareDriverBase
    {
        public override string DriverId { get { return BleDeviceHelper.DriverIdValue; } }
        public override string Brand { get { return "Bluetooth"; } }
        public override int Priority { get { return 100; } }
        public override bool IsExternalPackage { get { return false; } }

        public override bool CanHandle(MouseBatteryInfo dev)
        {
            if (dev == null) return false;
            return string.Equals(dev.DriverId, DriverId, StringComparison.OrdinalIgnoreCase);
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
            dev.Capabilities.ThemeColorHex = "#7E57C2";
        }

        public override List<MouseBatteryInfo> QueryDevices()
        {
            List<MouseBatteryInfo> list = BleDeviceHelper.QueryAllBlePeripherals();
            if (list != null)
            {
                for (int i = 0; i < list.Count; i++) PopulateCapabilities(list[i]);
            }
            return list ?? new List<MouseBatteryInfo>();
        }
    }
}
