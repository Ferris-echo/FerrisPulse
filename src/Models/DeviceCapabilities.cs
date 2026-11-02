// ============================================================================
// 模块名称: DeviceCapabilities.cs
// 所属区域: Models (数据契约层)
// 核心职责:
//   与具体品牌硬编码解耦的设备能力描述模型（涵盖 DPI 控制、多档位板载管理、XY 轴独立、
//   回报率频段调节、硬件切档实时监听、传感器参数、主题配色及声明式自定义动作列表）。
// 依赖关系: CustomDeviceAction.cs, System.Drawing
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑或属性时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止 $"..." 插值、禁止 ?. 空条件运算符、禁止 nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;

namespace RazerBatteryTray
{
    public class DeviceCapabilities
    {
        public bool SupportsBattery { get; set; }
        public bool SupportsDpi { get; set; }
        public bool SupportsDpiStages { get; set; }
        public bool SupportsXyIndependence { get; set; }
        public bool SupportsPollingRate { get; set; }
        public bool SupportsHardwareDpiMonitor { get; set; }
        public bool SupportsFastDpiMonitor
        {
            get { return SupportsHardwareDpiMonitor; }
            set { SupportsHardwareDpiMonitor = value; }
        }
        public int[] SupportedPollingRates { get; set; }

        public int MinDpi { get; set; }
        public int MaxDpi { get; set; }
        public int DpiStep { get; set; }

        public string SensorDescription { get; set; }
        public string ThemeColorHex { get; set; }
        public string PerformanceCardTitle { get; set; }

        // 充电位语义声明：true 表示该设备的「充电中」标志位只代表「USB 已插上并供电」，
        // 并不代表电池正在充电；UI 应据此显示「USB 有线」而非「充电中」。
        public bool ChargingFlagMeansUsbPoweredOnly { get; set; }

        private List<CustomDeviceAction> customActions;
        public List<CustomDeviceAction> CustomActions
        {
            get
            {
                if (customActions == null) customActions = new List<CustomDeviceAction>();
                return customActions;
            }
            set { customActions = value; }
        }

        public DeviceCapabilities()
        {
            SupportsBattery = true;
            SupportsDpi = false;
            SupportsDpiStages = false;
            SupportsXyIndependence = false;
            SupportsPollingRate = false;
            SupportsHardwareDpiMonitor = false;
            MinDpi = 100;
            MaxDpi = 30000;
            DpiStep = 50;
            SensorDescription = "原生步进: 50 DPI (100 ~ 30,000)";
            ThemeColorHex = "";
            PerformanceCardTitle = "";
            customActions = new List<CustomDeviceAction>();
        }

        public CustomDeviceAction FindAction(string actionId)
        {
            if (string.IsNullOrEmpty(actionId) || customActions == null) return null;
            for (int i = 0; i < customActions.Count; i++)
            {
                CustomDeviceAction act = customActions[i];
                if (act != null && string.Equals(act.ActionId, actionId, StringComparison.OrdinalIgnoreCase))
                {
                    return act;
                }
            }
            return null;
        }

        public static bool TryParseHexColor(string hex, out Color color)
        {
            color = Color.Empty;
            if (string.IsNullOrEmpty(hex)) return false;
            string s = hex.Trim();
            if (s.StartsWith("#")) s = s.Substring(1);
            else if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase)) s = s.Substring(2);

            if (s.Length == 6)
            {
                int rgb;
                if (int.TryParse(s, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out rgb))
                {
                    int r = (rgb >> 16) & 0xFF;
                    int g = (rgb >> 8) & 0xFF;
                    int b = rgb & 0xFF;
                    color = Color.FromArgb(r, g, b);
                    return true;
                }
            }
            return false;
        }
    }
}
