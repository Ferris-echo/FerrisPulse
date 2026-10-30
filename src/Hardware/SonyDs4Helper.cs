// ============================================================================
// 模块名称: SonyDs4Helper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   索尼 PS4 手柄 (DualShock 4) 电量适配：VID_054C / PID_05C4 (v1)、PID_09CC (v2)。
//   协议经独立探针实测确认（2026-10-30，见 _ds4_probe/probe_out.txt）：
//   · 仅一个 HID 接口，usagePage 0x01 / usage 0x05，输入报告 64 字节
//   · 报告 ID 0x01 (USB) / 0x11 (蓝牙)
//   · 第 30 字节为电量：低 4 位 = 档位（实测出现 11，超出文档 0-10，按满电处理）
//     bit4 (0x10) = 正在充电（USB 连接时为 1）
// 依赖关系: HidTransport.cs, MouseBatteryInfo.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 修改帧格式、电量解析或设备识别时必须同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止字符串插值、空条件运算符、nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;

namespace RazerBatteryTray
{
    public static class SonyDs4Helper
    {
        private const int VendorIdSony = 0x054C;
        private const int ProductIdDs4V1 = 0x05C4;
        private const int ProductIdDs4V2 = 0x09CC;

        // 电量字节下标。USB (report 0x01) 与蓝牙 (report 0x11) 实测均为 30。
        private const int BatteryByteIndex = 30;

        public static bool IsDs4(int vid, int pid)
        {
            return vid == VendorIdSony && (pid == ProductIdDs4V1 || pid == ProductIdDs4V2);
        }

        public static List<MouseBatteryInfo> QueryAllDs4Devices()
        {
            List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();

            int[] pids = new int[] { ProductIdDs4V2, ProductIdDs4V1 };
            for (int i = 0; i < pids.Length; i++)
            {
                List<HidDeviceDescriptor> descs = null;
                try
                {
                    descs = HidTransport.Enumerate(VendorIdSony, pids[i]);
                }
                catch (Exception ex)
                {
                    Diagnostics.Log("Hardware/SonyDs4Helper.cs: 枚举 DS4 失败", ex);
                    continue;
                }
                if (descs == null || descs.Count == 0)
                {
                    continue;
                }

                HidDeviceDescriptor target = PickGamepadInterface(descs);
                if (target == null)
                {
                    continue;
                }

                MouseBatteryInfo info = QueryOne(target);
                if (info != null)
                {
                    result.Add(info);
                    break;   // 同一手柄只会有一个有效接口，取到即止
                }
            }

            return result;
        }

        // 优先取「通用桌面 / 游戏手柄」用途的接口（usagePage 0x01 / usage 0x05），
        // 只有这个接口会持续上报带摇杆与按键的输入报告。
        private static HidDeviceDescriptor PickGamepadInterface(List<HidDeviceDescriptor> descs)
        {
            HidDeviceDescriptor best = null;
            for (int i = 0; i < descs.Count; i++)
            {
                HidDeviceDescriptor d = descs[i];
                if (d == null)
                {
                    continue;
                }
                if (d.UsagePage == 0x01 && d.Usage == 0x05)
                {
                    return d;
                }
                if (best == null || d.InputReportLength > best.InputReportLength)
                {
                    best = d;
                }
            }
            return best;
        }

        private static MouseBatteryInfo QueryOne(HidDeviceDescriptor desc)
        {
            if (desc == null || string.IsNullOrEmpty(desc.DevicePath))
            {
                return null;
            }

            IntPtr handle = IntPtr.Zero;
            try
            {
                handle = HidTransport.Open(desc.DevicePath);
                if (!HidTransport.IsValidHandle(handle))
                {
                    return null;
                }

                // 蓝牙连接时 HID 描述符声明 547 字节输入报告，但实测只上报精简报告
                // （report 0x01，第 30 字节恒为 0），拿不到电量。USB 为 64 字节，正常。
                // 这里只支持 USB，避免蓝牙下显示假的 0%。
                if (desc.InputReportLength > 100)
                {
                    return null;
                }

                int readLen = desc.InputReportLength > 0 ? desc.InputReportLength : 64;
                if (readLen > 100)
                {
                    readLen = 100;
                }

                // 手柄持续上报，先清掉积压的旧报告，避免拿到很久以前的数据
                HidTransport.DrainReports(handle, readLen, 8);

                byte[] data = null;
                if (!HidTransport.ReadReport(handle, readLen, 800, out data))
                {
                    return null;
                }
                if (data == null || data.Length <= BatteryByteIndex)
                {
                    return null;
                }

                int raw = data[BatteryByteIndex];
                int level = raw & 0x0F;
                // 实测出现过 11，超出文档的 0-10；按满电处理，避免算出 110%
                if (level > 10)
                {
                    level = 10;
                }
                bool charging = (raw & 0x10) != 0;
                int percent = level * 10;

                MouseBatteryInfo info = new MouseBatteryInfo();
                info.DeviceId = "Sony:054C:" + desc.ProductId.ToString("X4");
                info.Brand = "Sony";
                info.Category = DeviceCategory.Gamepad;
                info.DeviceName = "PS4 手柄 (DualShock 4)";
                info.CustomName = "DS4 手柄";
                info.Transport = charging ? "USB 有线" : "蓝牙";
                info.IsConnected = true;
                info.IsSleeping = false;
                info.IsCharging = charging;
                info.BatteryPercent = percent;
                info.HardwareId = "HID\\VID_054C&PID_" + desc.ProductId.ToString("X4");
                info.HardwareFingerprint = "Sony:054C:" + desc.ProductId.ToString("X4") + ":DS4";
                info.LastUpdated = DateTime.Now;
                info.CustomTooltip = charging
                    ? string.Format("PS4 手柄 · 正在充电 · 电量 {0}%", percent)
                    : string.Format("PS4 手柄 · 电量 {0}%", percent);
                return info;
            }
            catch (Exception ex)
            {
                Diagnostics.Log("Hardware/SonyDs4Helper.cs: 读取 DS4 失败", ex);
                return null;
            }
            finally
            {
                if (handle != IntPtr.Zero && HidTransport.IsValidHandle(handle))
                {
                    try { HidTransport.Close(handle); }
                    catch (Exception closeEx) { Diagnostics.Log("Hardware/SonyDs4Helper.cs: 关闭句柄失败", closeEx); }
                }
            }
        }
    }
}
