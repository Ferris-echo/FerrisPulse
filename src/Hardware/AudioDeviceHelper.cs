// ============================================================================
// 模块名称: AudioDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   系统音频控制面板快速唤起与音量混音器跳转
// 依赖关系: 无
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Diagnostics;

namespace RazerBatteryTray
{
    public static class AudioDeviceHelper
    {
        public static void SetDefaultAudioDevice(string friendlyName)
        {
            try
            {
                Process.Start("ms-settings:sound");
            }
            catch
            {
                try { Process.Start("mmsys.cpl"); } catch (Exception diagEx) { Diagnostics.Log("Hardware/AudioDeviceHelper.cs: 已忽略异常", diagEx); }
            }
        }

        public static void OpenVolumeMixer()
        {
            try
            {
                Process.Start("sndvol.exe");
            }
            catch
            {
                try { Process.Start("ms-settings:sound"); } catch (Exception diagEx) { Diagnostics.Log("Hardware/AudioDeviceHelper.cs: 已忽略异常", diagEx); }
            }
        }
    }
}
