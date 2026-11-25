// ============================================================================
// 模块名称: CustomDeviceConfig.cs
// 所属区域: Models (数据契约层)
// 核心职责:
//   用户手动配置和注册的自定义外设持久化数据模型
// 依赖关系: DeviceCategory.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑或字段时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;

namespace RazerBatteryTray
{
    public class CustomDeviceConfig
    {
        public string DeviceId { get; set; }
        public string HardwareFingerprint { get; set; }
        public string CustomName { get; set; }
        public string OriginalName { get; set; }
        public DeviceCategory Category { get; set; }
        public string Transport { get; set; }
        public int CachedBattery { get; set; }
        public DateTime LastSeen { get; set; }
        public string ThemeColorHex { get; set; }
        public string TrayColorHex { get; set; }
        private bool linkTrayColorToTheme = true;
        public bool LinkTrayColorToTheme
        {
            get { return linkTrayColorToTheme; }
            set { linkTrayColorToTheme = value; }
        }
    }
}
