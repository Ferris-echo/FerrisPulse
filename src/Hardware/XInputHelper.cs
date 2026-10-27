// ============================================================================
// 模块名称: XInputHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   XInput 手柄震动触觉测试与手柄在线状态探测
// 依赖关系: 无
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Runtime.InteropServices;
using System.Threading;

namespace RazerBatteryTray
{
    public static class XInputHelper
    {
        [StructLayout(LayoutKind.Sequential)]
        public struct XINPUT_VIBRATION
        {
            public ushort wLeftMotorSpeed;
            public ushort wRightMotorSpeed;
        }

        [DllImport("xinput1_4.dll", EntryPoint = "XInputSetState")]
        private static extern int XInput14_SetState(int dwUserIndex, ref XINPUT_VIBRATION pVibration);

        [DllImport("xinput9_1_0.dll", EntryPoint = "XInputSetState")]
        private static extern int XInput910_SetState(int dwUserIndex, ref XINPUT_VIBRATION pVibration);

        public static bool TriggerGamepadVibration(int durationMs)
        {
            ThreadPool.QueueUserWorkItem(new WaitCallback(delegate(object state)
            {
                try
                {
                    XINPUT_VIBRATION vibOn = new XINPUT_VIBRATION();
                    vibOn.wLeftMotorSpeed = 36000;
                    vibOn.wRightMotorSpeed = 52000;

                    XINPUT_VIBRATION vibOff = new XINPUT_VIBRATION();
                    vibOff.wLeftMotorSpeed = 0;
                    vibOff.wRightMotorSpeed = 0;

                    int matchedIndex = -1;
                    for (int i = 0; i < 4; i++)
                    {
                        try
                        {
                            if (XInput14_SetState(i, ref vibOn) == 0) { matchedIndex = i; break; }
                        }
                        catch
                        {
                            try
                            {
                                if (XInput910_SetState(i, ref vibOn) == 0) { matchedIndex = i; break; }
                            }
                            catch (Exception diagEx) { Diagnostics.Log("Hardware/XInputHelper.cs: 已忽略异常", diagEx); }
                        }
                    }

                    if (matchedIndex >= 0)
                    {
                        Thread.Sleep(durationMs);
                        try { XInput14_SetState(matchedIndex, ref vibOff); }
                        catch
                        {
                            try { XInput910_SetState(matchedIndex, ref vibOff); } catch (Exception diagEx) { Diagnostics.Log("Hardware/XInputHelper.cs: 已忽略异常", diagEx); }
                        }
                    }
                }
                catch (Exception diagEx) { Diagnostics.Log("Hardware/XInputHelper.cs: 已忽略异常", diagEx); }
            }));
            return true;
        }
    }
}
