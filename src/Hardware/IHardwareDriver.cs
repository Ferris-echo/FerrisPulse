// ============================================================================
// 模块名称: IHardwareDriver.cs
// 所属区域: Hardware (硬件驱动插件契约层)
// 核心职责:
//   定义统一的外设驱动扩展包标准契约接口。无论是内置厂商驱动、FDAS v2.0 JSON 声明式扩展包，
//   还是动态内存编译的 .fp-driver.cs 脚本扩展包，均统一实现本接口。
// 依赖关系: MouseBatteryInfo.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本接口签名时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止 $"..." 插值、禁止 ?. 空条件运算符、禁止 nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;

namespace RazerBatteryTray
{
    public interface IHardwareDriver
    {
        string DriverId { get; }
        string Brand { get; }
        int Priority { get; }
        bool IsExternalPackage { get; }
        string SourceFilePath { get; }

        bool CanHandle(MouseBatteryInfo dev);
        bool MatchesVidPid(int vid, int pid);
        void PopulateCapabilities(MouseBatteryInfo dev);

        List<MouseBatteryInfo> QueryDevices();
        bool FastQueryDpi(MouseBatteryInfo dev, out int dpiX, out int dpiY, out int stage, out int count);
        bool SetDpi(MouseBatteryInfo dev, int dpiX, int dpiY, int stage);
        bool UpdateHardwareStages(MouseBatteryInfo dev, int activeStage, int[] stagesX, int[] stagesY);
        bool SetPollingRate(MouseBatteryInfo dev, int rateHz);
        bool SyncCustomActions(MouseBatteryInfo dev);
        bool ExecuteCustomAction(MouseBatteryInfo dev, string actionId, int value);
    }
}
