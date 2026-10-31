// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// 1. 目标平台与编译规范：严格维持 C# 5.0 语法标准与 .NET Framework 4.0 兼容性。严禁引入任何第三方包或依赖。
// 2. 编码规范：所有文件严格保存为 UTF-8 带签名（BOM）格式，防止中文字符乱码。
// 3. 架构规范：本文件属于 Models 数据模型层。修改时请保持职责清晰，任何新增属性请同步维护 ARCHITECTURE.md。

using System;

namespace RazerBatteryTray
{
    public class AiPromptDeviceItem
    {
        public bool IsChecked { get; set; }
        public bool IsManual { get; set; }
        public string CustomName { get; set; }
        public DeviceCategory Category { get; set; }
        public string Brand { get; set; }
        public CustomDeviceConfig ScannedConfig { get; set; }

        public AiPromptDeviceItem()
        {
            this.IsChecked = true;
            this.IsManual = false;
            this.CustomName = "";
            this.Category = DeviceCategory.Mouse;
            this.Brand = "";
        }

        public AiPromptDeviceItem(CustomDeviceConfig config)
        {
            this.IsChecked = true;
            this.IsManual = false;
            this.ScannedConfig = config;
            if (config != null)
            {
                this.CustomName = !string.IsNullOrEmpty(config.CustomName) ? config.CustomName : config.OriginalName;
                this.Category = config.Category;
                this.Brand = "";
            }
            else
            {
                this.CustomName = "";
                this.Category = DeviceCategory.Mouse;
                this.Brand = "";
            }
        }

        public override string ToString()
        {
            string catIcon = MouseBatteryInfo.GetCategoryIcon(this.Category);
            string name = !string.IsNullOrEmpty(this.CustomName) ? this.CustomName : "未知设备";
            string manualTag = this.IsManual ? " (手动添加)" : "";
            return string.Format("{0} {1}{2}", catIcon, name, manualTag);
        }
    }
}
