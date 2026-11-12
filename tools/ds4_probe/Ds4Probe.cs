// ============================================================================
// DS4 协议探针（独立工具，不参与 FerrisPulse 主程序编译）
//
// 用途：只读方式枚举 PS4 手柄的 HID 接口并打印原始输入报文，
//       用于确认电量字节、充电位、摇杆/按键布局。不写任何数据到设备。
//
// 编译：probe_build.bat（复用 FerrisPulse/src/Hardware/HidTransport.cs）
// 运行：Ds4Probe.exe [次数]
// ============================================================================

using System;
using System.Collections.Generic;
using System.Threading;
using RazerBatteryTray;

namespace Ds4Probe
{
    internal static class Program
    {
        private const int VidSony = 0x054C;
        private static readonly int[] Pids = new int[] { 0x09CC, 0x05C4 };

        private static int Main(string[] args)
        {
            int rounds = 5;
            if (args != null && args.Length > 0)
            {
                int.TryParse(args[0], out rounds);
                if (rounds <= 0) rounds = 5;
            }

            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine("=== DS4 协议探针 ===");
            Console.WriteLine();

            HidDeviceDescriptor target = null;

            for (int p = 0; p < Pids.Length; p++)
            {
                List<HidDeviceDescriptor> list = null;
                try { list = HidTransport.Enumerate(VidSony, Pids[p]); }
                catch (Exception ex) { Console.WriteLine("枚举失败 PID 0x" + Pids[p].ToString("X4") + ": " + ex.Message); continue; }

                if (list == null || list.Count == 0) continue;

                Console.WriteLine("PID 0x" + Pids[p].ToString("X4") + " 共 " + list.Count + " 个接口:");
                for (int i = 0; i < list.Count; i++)
                {
                    HidDeviceDescriptor d = list[i];
                    Console.WriteLine(string.Format("  [{0}] usagePage=0x{1:X2} usage=0x{2:X2} in={3} out={4} feature={5}",
                        i, d.UsagePage, d.Usage, d.InputReportLength, d.OutputReportLength, d.FeatureReportLength));
                    if (d.UsagePage == 0x01 && d.Usage == 0x05) target = d;
                }
                if (target == null)
                {
                    HidDeviceDescriptor best = null;
                    for (int i = 0; i < list.Count; i++)
                    {
                        if (best == null || list[i].InputReportLength > best.InputReportLength) best = list[i];
                    }
                    target = best;
                }
                Console.WriteLine();
                if (target != null) break;
            }

            if (target == null)
            {
                Console.WriteLine("未找到 DS4 手柄（VID_054C / PID_05C4 或 09CC）。");
                return 1;
            }

            Console.WriteLine("选中接口: usagePage=0x" + target.UsagePage.ToString("X2") + " usage=0x" + target.Usage.ToString("X2") + " in=" + target.InputReportLength);
            Console.WriteLine();

            IntPtr handle = HidTransport.Open(target.DevicePath);
            if (!HidTransport.IsValidHandle(handle))
            {
                Console.WriteLine("打开设备失败。");
                return 2;
            }

            try
            {
                int readLen = target.InputReportLength > 0 ? target.InputReportLength : 64;
                if (readLen > 1024) readLen = 1024;   // 蓝牙连接实测 547 字节，上限必须够大

                HidTransport.DrainReports(handle, readLen, 8);

                for (int r = 0; r < rounds; r++)
                {
                    byte[] data = null;
                    if (!HidTransport.ReadReport(handle, readLen, 1500, out data))
                    {
                        Console.WriteLine("第 " + (r + 1) + " 次读取超时。");
                        continue;
                    }

                    Console.WriteLine("--- 第 " + (r + 1) + " 次 len=" + data.Length + " ---");
                    Console.WriteLine(Hex(data));

                    if (data.Length > 4)
                    {
                        Console.WriteLine(string.Format("  摇杆 LX={0,3} LY={1,3} RX={2,3} RY={3,3}", data[1], data[2], data[3], data[4]));
                    }
                    if (data.Length > 7)
                    {
                        Console.WriteLine(string.Format("  按键 b5=0x{0:X2} b6=0x{1:X2} b7=0x{2:X2}", data[5], data[6], data[7]));
                    }
                    // 电量候选区：不同固件/连接方式下标略有差异，28..33 全打出来比对
                    if (data.Length > 33)
                    {
                        Console.Write("  候选字节 28..33:");
                        for (int k = 28; k <= 33; k++) Console.Write(string.Format(" [{0}]=0x{1:X2}", k, data[k]));
                        Console.WriteLine();
                        int b = data[30];
                        Console.WriteLine(string.Format("  data[30]=0x{0:X2} -> 档位={1} (0-10) 充电位={2} 推算电量={3}%",
                            b, (b & 0x0F), ((b & 0x10) != 0), (b & 0x0F) * 10));
                    }
                    Console.WriteLine();
                    Thread.Sleep(1200);
                }
            }
            finally
            {
                if (HidTransport.IsValidHandle(handle)) HidTransport.Close(handle);
            }

            Console.WriteLine("完成。");
            return 0;
        }

        private static string Hex(byte[] d)
        {
            if (d == null) return "(null)";
            System.Text.StringBuilder sb = new System.Text.StringBuilder(d.Length * 3);
            for (int i = 0; i < d.Length; i++)
            {
                if (i > 0) sb.Append(i % 16 == 0 ? "\n  " : " ");
                sb.Append(d[i].ToString("X2"));
            }
            return "  " + sb.ToString();
        }
    }
}
