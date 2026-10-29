// ============================================================================
// 模块名称: MchoseMouse4DHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   MCHOSE（迈从）新一代无线游戏鼠标「4D 帧」私有 HID 协议驱动。
//   面向 K7 V2 Ultra+ / A5 V3 Ultra+ / A7 V3 等 2.4G 接收器机型，协议族与旧版
//   A5 V2 Ultra 的 0x11/0x12 帧完全不同，必须走 64 字节 0x4D 帧。
//
//   帧结构（64 字节，首字节即 Report ID 0x4D）:
//     [0]=0x4D  [1]=0x01  [2]=flags(1)  [3]=data 长度
//     [4]=cmd 低字节  [5]=cmd 高字节  [6]=bizCode  [7]=sequence
//     [8..]=data  [8+len]=校验和（frame[2] 到 frame[7+len] 逐字节 XOR）
//   注意：payload 不做 ^0xFF 反转（旧 A5 协议才需要反转）。
//
//   命令字:
//     0x0900 读设备信息 → [8..9]=VID(LE) [10..11]=PID(LE) [12]=配置档数
//                          [13]=宏存储类型 [14..15]=共享宏区 [16..17]=宏单元
//                          [18]=连接状态 [19]=充电状态 [20]=电量% [21]=游戏模式
//     0x0002 读性能配置 → [8]=配置档 [9]=高4位USB回报率索引/低4位USB DPI档
//                          [10]=高4位2.4G回报率索引/低4位2.4G DPI档
//                          [11]=休眠分钟 [12]=休眠模式 [13]=传感器
//                          [14]=角度参数 [15]=左键消抖 [16]=右键消抖
//     0x0003 读 DPI     → [8]=配置档 [9]=方向 [10]=档位总数 [11]=当前档位
//                          [12]=是否XY独立 [13..24]=6×uint16 LE DPI
//     0x0103 写 DPI     ← data 17 字节 [档位, 方向, XY独立, 档位总数, 当前档位, 6×uint16]
//     0x0102 写性能配置 ← data 19 字节 [档位, 率+DPI字节, 2.4G率+DPI字节,
//                          休眠, 休眠模式, 传感器, 角度, 左消抖, 右消抖, 10×0]
//     0x0901 读固件     ← data [0]=鼠标 / [1]=接收器
//   传输要求：必须 CreateFile 带 GENERIC_READ|WRITE 打开，用 WriteFile/ReadFile
//   收发中断报告；HidD_SetFeature/HidD_GetFeature 对该协议族一律无效。
//   忙标志：设备刚唤醒或正在落盘配置时，回包数据区首字节为 0xFF（无有效数据），
//   必须退避重发（BusyRetryCount × BusyRetryDelayMs），不能当作有效应答。
// 依赖关系: HidTransport.cs (共享 HID P/Invoke), MouseBatteryInfo.cs, Microsoft.Win32
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 修改命令字、校验和或电量解析时必须同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止字符串插值、空条件运算符、nameof、表达式主体）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    /// <summary>
    /// MCHOSE 新一代鼠标（4D 帧协议族）驱动。所有入口在失败时返回 null / false，
    /// 由调用方回退到旧版 A5 协议路径，保证既有设备不受影响。
    /// </summary>
    public static class MchoseMouse4DHelper
    {
        private static readonly object hidLock = new object();
        private const string CacheRegistryKey = @"Software\RazerBatteryTray\MchoseMouse4DCache";

        public const int VendorIdMchose = 0x3837;

        // 官方网页驱动的 WebHID 白名单（USB 接口 PID）。
        private static readonly int[] SupportedPids = new int[]
        {
            0x1001, // K7 Ultra（旧款，同族协议）
            0x1014,
            0x1016,
            0x1018, // K7 V2 Ultra+（本机实测）
            0x101C,
            0x101D
        };

        // 蓝牙 BLE 形态的硬件身份：BLE HID over GATT 节点上 Windows 报告的
        // PID 与 USB 接口 PID 不同（本机实测 K7 V2 Ultra+ 走 BLE 时为 0x4026）。
        // 该形态只暴露标准 HID 集合（鼠标/键盘/多媒体），没有 0x4D 厂商通道，
        // 因此 DPI 与回报率一律不可读写，只有电量可经 Windows 原生 GATT 电池服务取得。
        public const int BleProductIdK7V2UltraPlus = 0x4026;

        private static readonly int[] SupportedBlePids = new int[]
        {
            BleProductIdK7V2UltraPlus
        };

        // [物理设备键] 同一台鼠标在不同连接形态下 PID 不同（USB 接口 PID vs BLE 节点 PID），
        // 但物理上仍是同一台设备。设备聚焦记忆若按原始 DeviceId 字符串比对，
        // 用户在 2.4G 形态下记住的卡片在切到蓝牙后会失配，启动时回落到键盘视图。
        // 这里把已知的「同一型号不同形态 PID」归并到同一个物理键。
        private static readonly int[] PhysicalGroupK7V2UltraPlus = new int[]
        {
            0x1018, // USB / 2.4G 接收器接口
            BleProductIdK7V2UltraPlus // 0x4026，蓝牙 BLE 节点
        };

        /// <summary>
        /// 将 MCHOSE 设备的 DeviceId 归一化为「物理设备键」：
        /// 同一台鼠标的 USB 形态与蓝牙形态得到相同键，便于跨形态匹配聚焦记忆。
        /// 无法识别或非本协议族时原样返回，绝不猜测其它型号的对应关系。
        /// </summary>
        public static string GetPhysicalDeviceKey(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return deviceId;

            string prefix = string.Format("MCHOSE:{0:X4}:", VendorIdMchose);
            if (!deviceId.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return deviceId;

            string tail = deviceId.Substring(prefix.Length);
            int pid;
            if (!int.TryParse(tail, System.Globalization.NumberStyles.HexNumber,
                    System.Globalization.CultureInfo.InvariantCulture, out pid))
            {
                return deviceId;
            }

            for (int i = 0; i < PhysicalGroupK7V2UltraPlus.Length; i++)
            {
                if (PhysicalGroupK7V2UltraPlus[i] == pid)
                {
                    // 组键取该型号 USB 形态 PID，作为与形态无关的稳定标识。
                    return string.Format("MCHOSE:{0:X4}:{1:X4}", VendorIdMchose, 0x1018);
                }
            }
            return deviceId;
        }

        private const uint DigcfPresent = 0x00000002;
        private const uint DigcfAllClasses = 0x00000004;

        private const byte FrameHead = 0x4D;
        private const byte FrameVersion = 0x01;
        private const byte FrameFlags = 0x01;
        private const int FrameLength = 64;
        private const int MaxDataLength = FrameLength - 8 - 1; // 55 字节，需留校验位

        private const int CmdDeviceInfo = 0x0900;
        private const int CmdPerfRead = 0x0002;
        private const int CmdDpiRead = 0x0003;
        private const int CmdDpiWrite = 0x0103;
        private const int CmdPerfWrite = 0x0102;
        private const int CmdFirmware = 0x0901;

        private const int ReadTimeoutMs = 500;
        private const int WriteTimeoutMs = 300;
        private const int CommandPaceMs = 20;
        private const int BusyRetryCount = 8;
        private const int BusyRetryDelayMs = 90;

        // 回报率索引表：索引 0..5，协议内传输时 +1（0 保留给“未设置”）。
        private static readonly int[] Rates = new int[] { 125, 500, 1000, 2000, 4000, 8000 };

        private static DateTime lastCommandUtc = DateTime.MinValue;
        private static DateTime lastQueryTime = DateTime.MinValue;
        private static MouseBatteryInfo cachedDevice = null;
        private const int QueryThrottleMs = 3000;

        public static int CachedBatteryPercent = -1;
        public static bool CachedIsCharging = false;
        public static string CachedDeviceName = "";
        public static DateTime CachedLastUpdated = DateTime.MinValue;

        // 板载档位与回报率的真实快照。
        // 休眠时只能用这份真实快照渲染卡片，
        // 绝不能用 400/800/1600/3000/6400 这类默认档位凭空补数。
        public static int[] CachedDpiStages = null;
        public static int[] CachedDpiStagesY = null;
        public static int CachedDpiStage = 0;
        public static int CachedDpiX = 0;
        public static int CachedDpiY = 0;
        public static int CachedPollingRate = 0;

        /// <summary>0x0900 设备信息应答。</summary>
        public class DeviceInfo
        {
            public int Vid;
            public int Pid;
            public int ConfigCount;
            public int ConnectStatus;
            public int ChargeStatus;
            public int Battery;
            public int GameMode;
        }

        /// <summary>0x0002 性能配置应答。</summary>
        public class PerformanceInfo
        {
            public int ProfileIndex;
            public int UsbRateRaw;
            public int UsbDpiIndex;
            public int WirelessRateRaw;
            public int WirelessDpiIndex;
            public int SleepMinutes;
            public int SleepMode;
            public int Sensor;
            public int RotateVal;
            public int LeftDebounce;
            public int RightDebounce;
        }

        /// <summary>0x0003 DPI 应答。</summary>
        public class DpiInfo
        {
            public int ProfileIndex;
            public int Direction;
            public int StageCount;
            public int ActiveStage;
            public int HasY;
            public int[] StagesX;
            public int[] StagesY;
        }

        static MchoseMouse4DHelper()
        {
            LoadCache();
        }

        public static bool IsSupportedPid(int pid)
        {
            for (int i = 0; i < SupportedPids.Length; i++)
            {
                if (SupportedPids[i] == pid) return true;
            }
            return false;
        }

        public static int[] GetSupportedPollingRates()
        {
            return new int[] { 125, 500, 1000, 2000, 4000, 8000 };
        }

        public static int GetMaxPollingRate()
        {
            return 8000;
        }

        public static string GetTransportName(int connectStatus)
        {
            // 官方固件里 0 表示有线直连；2.4G 接收器链路实测上报 2。
            if (connectStatus == 0) return "USB 有线";
            return "2.4G 无线";
        }

        /// <summary>把协议内的回报率原始索引（+1 存储）还原成 RATES 索引。</summary>
        public static int DecodeRateIndex(int raw)
        {
            if (raw <= 0) return 0;
            return raw - 1;
        }

        /// <summary>把 RATES 索引编码成协议内的原始值（索引 0 仍发 0）。</summary>
        public static int EncodeRateIndex(int index)
        {
            if (index <= 0) return 0;
            return index + 1;
        }

        public static int GetRateHz(int raw)
        {
            int idx = DecodeRateIndex(raw);
            if (idx < 0) idx = 0;
            if (idx >= Rates.Length) idx = Rates.Length - 1;
            return Rates[idx];
        }

        public static int GetRateIndexForHz(int hz)
        {
            int best = 2;
            int bestDiff = int.MaxValue;
            for (int i = 0; i < Rates.Length; i++)
            {
                int diff = Math.Abs(Rates[i] - hz);
                if (diff < bestDiff)
                {
                    bestDiff = diff;
                    best = i;
                }
            }
            return best;
        }

        // ------------------------------------------------------------------
        // 帧构建 / 收发
        // ------------------------------------------------------------------

        /// <summary>构建 64 字节 0x4D 帧（首字节即 Report ID）。</summary>
        public static byte[] BuildFrame(int commandId, byte[] data)
        {
            if (data == null) data = new byte[0];
            if (data.Length > MaxDataLength) return null;

            byte[] frame = new byte[FrameLength];
            frame[0] = FrameHead;
            frame[1] = FrameVersion;
            frame[2] = FrameFlags;
            frame[3] = (byte)data.Length;
            frame[4] = (byte)(commandId & 0xFF);
            frame[5] = (byte)((commandId >> 8) & 0xFF);
            frame[6] = 0x00;
            frame[7] = 0x00;
            for (int i = 0; i < data.Length; i++)
            {
                frame[8 + i] = data[i];
            }

            int checksum = 0;
            int end = 7 + data.Length;
            for (int i = 2; i <= end; i++)
            {
                checksum ^= frame[i];
            }
            frame[8 + data.Length] = (byte)(checksum & 0xFF);
            return frame;
        }

        private static void Pace()
        {
            if (lastCommandUtc != DateTime.MinValue)
            {
                int elapsed = (int)(DateTime.UtcNow - lastCommandUtc).TotalMilliseconds;
                if (elapsed >= 0 && elapsed < CommandPaceMs) Thread.Sleep(CommandPaceMs - elapsed);
            }
            lastCommandUtc = DateTime.UtcNow;
        }

        /// <summary>写一帧并读回对应命令的应答；失败返回 null。</summary>
        private static byte[] SendCommand(IntPtr handle, int commandId, byte[] data)
        {
            if (!HidTransport.IsValidHandle(handle)) return null;
            byte[] frame = BuildFrame(commandId, data);
            if (frame == null) return null;

            // 设备刚唤醒或正在落盘配置时会回 FF 忙标志，需退避重试。
            for (int attempt = 0; attempt < BusyRetryCount; attempt++)
            {
                HidTransport.DrainReports(handle, FrameLength, 4);
                Pace();
                if (!HidTransport.WriteReport(handle, frame, FrameLength, WriteTimeoutMs))
                {
                    Thread.Sleep(60);
                    continue;
                }

                for (int read = 0; read < 2; read++)
                {
                    byte[] rx;
                    if (!HidTransport.ReadReport(handle, FrameLength, ReadTimeoutMs, out rx)) break;
                    if (rx == null || rx.Length < 8) continue;
                    if (rx[0] != FrameHead) continue;
                    int cmd = rx[4] | (rx[5] << 8);
                    if (cmd != commandId) continue;
                    if (rx[8] == 0xFF) break; // 忙：本次回包不可用，退避后重发
                    return rx;
                }
                Thread.Sleep(BusyRetryDelayMs);
            }
            return null;
        }

        // ------------------------------------------------------------------
        // 端点发现
        // ------------------------------------------------------------------

        /// <summary>
        /// 枚举 VID 3837 下所有 4D 协议族的控制端点（UsagePage 0xFF01，64 字节报告）。
        /// </summary>
        public static List<HidDeviceDescriptor> EnumerateControlEndpoints()
        {
            List<HidDeviceDescriptor> result = new List<HidDeviceDescriptor>();
            List<HidDeviceDescriptor> all = HidTransport.Enumerate(VendorIdMchose, 0);
            for (int i = 0; i < all.Count; i++)
            {
                HidDeviceDescriptor d = all[i];
                if (!IsSupportedPid(d.ProductId)) continue;
                if (d.UsagePage != 0xFF01) continue;
                if (d.OutputReportLength < FrameLength) continue;
                if (d.InputReportLength < FrameLength) continue;
                result.Add(d);
            }
            return result;
        }

        public static bool HasControlEndpoint()
        {
            return EnumerateControlEndpoints().Count > 0;
        }

        private static HidDeviceDescriptor PickEndpoint()
        {
            List<HidDeviceDescriptor> list = EnumerateControlEndpoints();
            if (list.Count == 0) return null;
            // 优先选带产品字符串的接口，便于取到型号名。
            for (int i = 0; i < list.Count; i++)
            {
                if (!string.IsNullOrEmpty(list[i].ProductString)) return list[i];
            }
            return list[0];
        }

        // ------------------------------------------------------------------
        // 读命令
        // ------------------------------------------------------------------

        public static bool TryParseDeviceInfo(byte[] report, out DeviceInfo info)
        {
            info = null;
            if (report == null || report.Length < 22) return false;
            if (report[0] != FrameHead) return false;

            DeviceInfo d = new DeviceInfo();
            d.Vid = report[8] | (report[9] << 8);
            d.Pid = report[10] | (report[11] << 8);
            d.ConfigCount = report[12];
            d.ConnectStatus = report[18];
            d.ChargeStatus = report[19];
            d.Battery = report[20];
            d.GameMode = report[21];

            if (d.Vid != VendorIdMchose) return false;
            if (d.Battery < 0 || d.Battery > 100) return false;

            info = d;
            return true;
        }

        public static bool TryParsePerformance(byte[] report, out PerformanceInfo info)
        {
            info = null;
            if (report == null || report.Length < 17) return false;
            if (report[0] != FrameHead) return false;

            PerformanceInfo p = new PerformanceInfo();
            p.ProfileIndex = report[8];
            p.UsbRateRaw = (report[9] >> 4) & 0x0F;
            p.UsbDpiIndex = report[9] & 0x0F;
            p.WirelessRateRaw = (report[10] >> 4) & 0x0F;
            p.WirelessDpiIndex = report[10] & 0x0F;
            p.SleepMinutes = report[11];
            p.SleepMode = report[12] & 0x01;
            p.Sensor = report[13];
            p.RotateVal = report[14];
            p.LeftDebounce = report[15];
            p.RightDebounce = report[16];

            info = p;
            return true;
        }

        public static bool TryParseDpi(byte[] report, out DpiInfo info)
        {
            info = null;
            if (report == null || report.Length < 25) return false;
            if (report[0] != FrameHead) return false;

            DpiInfo d = new DpiInfo();
            d.ProfileIndex = report[8];
            d.Direction = report[9];
            d.StageCount = report[10];
            d.ActiveStage = report[11];
            d.HasY = report[12];
            d.StagesX = new int[6];
            d.StagesY = new int[6];
            for (int i = 0; i < 6; i++)
            {
                int x = report[13 + i * 2] | (report[14 + i * 2] << 8);
                d.StagesX[i] = x;
                // 单轴机型直接复用 X 轴数值。
                d.StagesY[i] = x;
            }

            if (d.StageCount < 1 || d.StageCount > 6) d.StageCount = 1;
            if (d.ActiveStage < 0 || d.ActiveStage > 5) d.ActiveStage = 0;
            info = d;
            return true;
        }

        private static DeviceInfo ReadDeviceInfo(IntPtr handle)
        {
            DeviceInfo info;
            byte[] rx = SendCommand(handle, CmdDeviceInfo, null);
            if (rx == null) return null;
            if (!TryParseDeviceInfo(rx, out info)) return null;
            return info;
        }

        private static PerformanceInfo ReadPerformance(IntPtr handle)
        {
            PerformanceInfo info;
            byte[] rx = SendCommand(handle, CmdPerfRead, null);
            if (rx == null) return null;
            if (!TryParsePerformance(rx, out info)) return null;
            return info;
        }

        private static DpiInfo ReadDpi(IntPtr handle)
        {
            DpiInfo info;
            byte[] rx = SendCommand(handle, CmdDpiRead, new byte[] { 0x00, 0x00 });
            if (rx == null) return null;
            if (!TryParseDpi(rx, out info)) return null;
            return info;
        }

        // ------------------------------------------------------------------
        // 写命令
        // ------------------------------------------------------------------

        /// <summary>
        /// 写回性能配置（0x0102）。只改动回报率相关半字节，其余字段原样回写，
        /// 避免误改休眠/传感器/消抖等用户配置。
        /// </summary>
        private static bool WritePerformance(IntPtr handle, PerformanceInfo current, int rateIndex)
        {
            if (current == null) return false;

            int usbRaw = EncodeRateIndex(rateIndex);
            int wirelessRaw = EncodeRateIndex(rateIndex);
            int dpiIndex = current.UsbDpiIndex & 0x0F;

            byte[] data = new byte[19];
            data[0] = (byte)(current.ProfileIndex & 0xFF);
            data[1] = (byte)(((usbRaw & 0x0F) << 4) | dpiIndex);
            data[2] = (byte)(((wirelessRaw & 0x0F) << 4) | dpiIndex);
            data[3] = (byte)(current.SleepMinutes & 0xFF);
            data[4] = (byte)(current.SleepMode & 0xFF);
            data[5] = (byte)(current.Sensor & 0xFF);
            data[6] = (byte)(current.RotateVal & 0xFF);
            data[7] = (byte)(current.LeftDebounce & 0xFF);
            data[8] = (byte)(current.RightDebounce & 0xFF);

            return SendCommand(handle, CmdPerfWrite, data) != null;
        }

        /// <summary>写 DPI（0x0103）。stage 为 0 基索引，stageCount 为启用档位总数。</summary>
        private static bool WriteDpi(IntPtr handle, DpiInfo current, int[] stages, int activeStage, int stageCount)
        {
            if (current == null || stages == null || stages.Length < 6) return false;
            if (activeStage < 0) activeStage = 0;
            if (activeStage > 5) activeStage = 5;
            if (stageCount < 1) stageCount = 1;
            if (stageCount > 6) stageCount = 6;
            if (activeStage >= stageCount) stageCount = activeStage + 1;

            byte[] data = new byte[17];
            data[0] = (byte)(current.ProfileIndex & 0xFF);
            data[1] = 0x00; // 方向固定 X
            data[2] = (byte)(current.HasY & 0xFF);
            data[3] = (byte)(stageCount & 0xFF);
            data[4] = (byte)(activeStage & 0xFF);
            for (int i = 0; i < 6; i++)
            {
                int value = stages[i];
                if (value < 0) value = 0;
                if (value > 0xFFFF) value = 0xFFFF;
                data[5 + i * 2] = (byte)(value & 0xFF);
                data[6 + i * 2] = (byte)((value >> 8) & 0xFF);
            }

            return SendCommand(handle, CmdDpiWrite, data) != null;
        }

        // ------------------------------------------------------------------
        // 对外入口
        // ------------------------------------------------------------------

        /// <summary>查询 4D 协议族鼠标；无匹配设备时返回 null。</summary>
        public static MouseBatteryInfo QueryMchose4DMouse()
        {
            lock (hidLock)
            {
                // 与键盘一致的节流，避免主界面高频刷新时反复占用 HID 管道。
                if ((DateTime.Now - lastQueryTime).TotalMilliseconds < QueryThrottleMs && cachedDevice != null)
                {
                    return cachedDevice;
                }
                lastQueryTime = DateTime.Now;

                HidDeviceDescriptor descriptor = PickEndpoint();
                if (descriptor == null)
                {
                    // 设备被拔出/端点消失时保留缓存卡片，交由上层判定离线。
                    return cachedDevice;
                }

                IntPtr handle = HidTransport.Open(descriptor.DevicePath);
                if (!HidTransport.IsValidHandle(handle))
                {
                    return BuildStaleCard(descriptor);
                }

                try
                {
                    DeviceInfo info = ReadDeviceInfo(handle);
                    if (info == null)
                    {
                        // 设备休眠或正在落盘配置：回包恒为 0xFF 忙标志。
                        // 此时必须保留在线卡片与缓存电量，严禁让设备从列表消失。
                        return BuildStaleCard(descriptor);
                    }

                    PerformanceInfo perf = ReadPerformance(handle);
                    DpiInfo dpi = ReadDpi(handle);

                    bool linked = info.Battery > 0;
                    int battery = linked ? info.Battery : CachedBatteryPercent;
                    bool charging = linked ? (info.ChargeStatus != 0) : CachedIsCharging;
                    DateTime lastUpdated = linked ? DateTime.Now : CachedLastUpdated;

                    string name = string.IsNullOrEmpty(descriptor.ProductString)
                        ? "MCHOSE 无线游戏鼠标"
                        : descriptor.ProductString.Trim();

                    if (linked)
                    {
                        CachedBatteryPercent = info.Battery;
                        CachedIsCharging = charging;
                        CachedDeviceName = name;
                        CachedLastUpdated = lastUpdated;
                        SaveCache();
                    }
                    else if (!string.IsNullOrEmpty(CachedDeviceName))
                    {
                        name = CachedDeviceName;
                    }

                    int stageCount = 1;
                    int activeStage = 0;
                    int[] stagesX = null;
                    int[] stagesY = null;
                    int dpiX = 0;
                    int dpiY = 0;
                    bool xyIndependent = false;

                    if (dpi != null)
                    {
                        stageCount = dpi.StageCount;
                        activeStage = dpi.ActiveStage;
                        if (activeStage >= stageCount) activeStage = stageCount - 1;
                        stagesX = new int[stageCount];
                        stagesY = new int[stageCount];
                        for (int i = 0; i < stageCount; i++)
                        {
                            stagesX[i] = dpi.StagesX[i];
                            stagesY[i] = dpi.StagesY[i];
                        }
                        dpiX = dpi.StagesX[activeStage];
                        dpiY = dpi.StagesY[activeStage];
                        xyIndependent = (dpi.HasY != 0);
                    }

                    int pollingRate = 1000;
                    if (perf != null) pollingRate = GetRateHz(perf.UsbRateRaw);

                    MouseBatteryInfo dev = new MouseBatteryInfo();
                    dev.DeviceId = string.Format("MCHOSE:{0:X4}:{1:X4}", VendorIdMchose, descriptor.ProductId);
                    dev.HardwareFingerprint = string.Format("HID:{0:X4}:{1:X4}", VendorIdMchose, descriptor.ProductId);
                    dev.HardwareId = descriptor.InstanceId;
                    dev.Brand = "MCHOSE";
                    dev.Category = DeviceCategory.Mouse;
                    dev.DeviceName = name;
                    dev.Transport = GetTransportName(info.ConnectStatus);
                    dev.IsCustom = false;
                    dev.IsConnected = linked;
                    dev.IsSleeping = !linked;
                    dev.IsDonglePresent = true;
                    dev.BatteryPercent = battery;
                    dev.ExactBatteryPercent = battery;
                    dev.IsCharging = charging;
                    dev.LastUpdated = lastUpdated;
                    dev.Dpi = dpiX;
                    dev.DpiY = dpiY;
                    dev.DpiStage = activeStage + 1;
                    dev.DpiStageCount = stageCount;
                    dev.DpiStages = stagesX;
                    dev.DpiStagesY = stagesY;
                    dev.IsXyIndependent = xyIndependent;
                    dev.XyRatio = (dpiY > 0) ? ((double)dpiX / (double)dpiY) : 1.0;
                    dev.PollingRate = pollingRate;
                    dev.SupportedPollingRates = GetSupportedPollingRates();
                    dev.MaxPollingRate = GetMaxPollingRate();

                    // 记下板载真实档位与回报率，供休眠时的卡片渲染。
                    if (stagesX != null && stagesX.Length > 0)
                    {
                        CachedDpiStages = (int[])stagesX.Clone();
                        CachedDpiStagesY = (stagesY != null) ? (int[])stagesY.Clone() : null;
                        CachedDpiStage = activeStage + 1;
                        CachedDpiX = dpiX;
                        CachedDpiY = dpiY;
                        SaveCache();
                    }
                    if (pollingRate > 0)
                    {
                        CachedPollingRate = pollingRate;
                        SaveCache();
                    }

                    cachedDevice = dev;
                    return dev;
                }
                catch
                {
                    return null;
                }
                finally
                {
                    HidTransport.Close(handle);
                }
            }
        }

        /// <summary>
        /// 构建设备休眠/忙时的占位卡片：保留在线身份与缓存电量，
        /// 只把 IsConnected 置 false 并标注原因，避免卡片从列表消失。
        /// </summary>
        private static MouseBatteryInfo BuildStaleCard(HidDeviceDescriptor descriptor)
        {
            MouseBatteryInfo dev = new MouseBatteryInfo();
            dev.DeviceId = string.Format("MCHOSE:{0:X4}:{1:X4}", VendorIdMchose, descriptor.ProductId);
            dev.HardwareFingerprint = string.Format("HID:{0:X4}:{1:X4}", VendorIdMchose, descriptor.ProductId);
            dev.HardwareId = descriptor.InstanceId;
            dev.Brand = "MCHOSE";
            dev.Category = DeviceCategory.Mouse;
            dev.DeviceName = !string.IsNullOrEmpty(descriptor.ProductString)
                ? descriptor.ProductString.Trim()
                : (string.IsNullOrEmpty(CachedDeviceName) ? "MCHOSE 无线游戏鼠标" : CachedDeviceName);
            dev.Transport = "2.4G 无线";
            dev.IsCustom = false;
            dev.IsConnected = false;
            dev.IsSleeping = true;
            dev.IsDonglePresent = true;
            dev.BatteryPercent = CachedBatteryPercent;
            dev.ExactBatteryPercent = CachedBatteryPercent;
            dev.IsCharging = CachedIsCharging;
            dev.LastUpdated = CachedLastUpdated;
            // 休眠时只能呈现上次读到的真实值；
            // 从未读到过就留 0（未知），不得伪造 1000 Hz。
            if (CachedPollingRate > 0)
            {
                dev.PollingRate = CachedPollingRate;
                dev.SupportedPollingRates = GetSupportedPollingRates();
                dev.MaxPollingRate = GetMaxPollingRate();
            }
            else
            {
                dev.PollingRate = 0;
                dev.SupportedPollingRates = new int[0];
                dev.MaxPollingRate = 0;
            }

            // 档位同理：只用真实快照，绝不凭空生成档位表。
            if (CachedDpiStages != null && CachedDpiStages.Length > 0)
            {
                dev.DpiStages = (int[])CachedDpiStages.Clone();
                dev.DpiStagesY = (CachedDpiStagesY != null) ? (int[])CachedDpiStagesY.Clone() : (int[])CachedDpiStages.Clone();
                dev.DpiStageCount = CachedDpiStages.Length;
                dev.DpiStage = (CachedDpiStage >= 1 && CachedDpiStage <= CachedDpiStages.Length) ? CachedDpiStage : 1;
                dev.Dpi = CachedDpiStages[dev.DpiStage - 1];
                dev.DpiY = dev.DpiStagesY[dev.DpiStage - 1];
            }
            else
            {
                dev.DpiStages = null;
                dev.DpiStagesY = null;
                dev.DpiStageCount = 0;
                dev.DpiStage = 0;
                dev.Dpi = 0;
                dev.DpiY = 0;
            }
            dev.IsXyIndependent = false;
            dev.XyRatio = 1.0;

            if (CachedBatteryPercent >= 0)
            {
                dev.CustomTooltip = "设备休眠中，显示上次读取的电量";
            }
            cachedDevice = dev;
            return dev;
        }

        /// <summary>切换当前生效的 DPI 档位（1 基）。</summary>
        public static bool SetDpiStage(int stage)
        {
            if (stage < 1) stage = 1;
            if (stage > 6) stage = 6;

            lock (hidLock)
            {
                HidDeviceDescriptor descriptor = PickEndpoint();
                if (descriptor == null) return false;

                IntPtr handle = HidTransport.Open(descriptor.DevicePath);
                if (!HidTransport.IsValidHandle(handle)) return false;

                try
                {
                    DpiInfo dpi = ReadDpi(handle);
                    if (dpi == null) return false;

                    int[] stages = new int[6];
                    for (int i = 0; i < 6; i++) stages[i] = dpi.StagesX[i];

                    int target = stage - 1;
                    int stageCount = dpi.StageCount;
                    // 严格保守：目标档位必须已经存在于板载配置中。
                    // 这里绝不能自动扩档：自动扩档会把设备上并不存在的档位
                    // 永久写进板载配置，导致 UI 凭空多出 1600/3200 这类选项。
                    // 增删档位只能走 SetDpiStages。
                    if (target < 0 || target >= stageCount) return false;

                    if (!WriteDpi(handle, dpi, stages, target, stageCount)) return false;
                    Thread.Sleep(120);

                    DpiInfo after = ReadDpi(handle);
                    if (after == null) return false;
                    if (after.ActiveStage == target)
                    {
                        InvalidateCache();
                        return true;
                    }
                    return false;
                }
                catch
                {
                    return false;
                }
                finally
                {
                    HidTransport.Close(handle);
                }
            }
        }

        /// <summary>写入某一档的 DPI 值（stage 为 1 基）。</summary>
        public static bool SetDpi(int dpiX, int dpiY, int stage)
        {
            if (dpiX < 100) dpiX = 100;
            if (dpiX > 42000) dpiX = 42000;
            if (dpiY < 100) dpiY = dpiX;
            if (dpiY > 42000) dpiY = 42000;
            if (stage < 1) stage = 1;
            if (stage > 6) stage = 6;

            lock (hidLock)
            {
                HidDeviceDescriptor descriptor = PickEndpoint();
                if (descriptor == null) return false;

                IntPtr handle = HidTransport.Open(descriptor.DevicePath);
                if (!HidTransport.IsValidHandle(handle)) return false;

                try
                {
                    DpiInfo dpi = ReadDpi(handle);
                    if (dpi == null) return false;

                    int[] stages = new int[6];
                    for (int i = 0; i < 6; i++) stages[i] = dpi.StagesX[i];

                    int target = stage - 1;
                    int stageCount = dpi.StageCount;
                    // 同上：不得自动扩档。越界直接拒绝，避免污染板载档位表。
                    if (target < 0 || target >= stageCount) return false;
                    stages[target] = dpiX;

                    if (!WriteDpi(handle, dpi, stages, target, stageCount)) return false;
                    Thread.Sleep(150);

                    DpiInfo after = ReadDpi(handle);
                    if (after == null) return false;
                    if (after.StagesX[target] == dpiX)
                    {
                        InvalidateCache();
                        return true;
                    }
                    return false;
                }
                catch
                {
                    return false;
                }
                finally
                {
                    HidTransport.Close(handle);
                }
            }
        }

        /// <summary>
        /// 一次性写入整套 DPI 档位表（1~5 档）并指定生效档位。
        /// 供 DPI 管理对话框的“保存并写入板载”使用：对话框允许用户增删档位、
        /// 修改每档数值，因此必须整体回写 0x0103，而不是只改单档。
        /// activeStage 为 1 基；返回是否真正落到硬件（写入后回读校验）。
        /// </summary>
        public static bool SetDpiStages(int[] stagesX, int[] stagesY, int activeStage)
        {
            if (stagesX == null || stagesX.Length == 0) return false;

            lock (hidLock)
            {
                HidDeviceDescriptor descriptor = PickEndpoint();
                if (descriptor == null) return false;

                IntPtr handle = HidTransport.Open(descriptor.DevicePath);
                if (!HidTransport.IsValidHandle(handle)) return false;

                try
                {
                    DpiInfo dpi = ReadDpi(handle);
                    if (dpi == null) return false;

                    int stageCount = stagesX.Length;
                    if (stageCount > 5) stageCount = 5;
                    if (stageCount < 1) stageCount = 1;

                    // 设备固定保留 6 个槽位，未启用的槽位用最后一档填充，
                    // 避免把历史残留值写回硬件。
                    int[] stages = new int[6];
                    for (int i = 0; i < 6; i++)
                    {
                        int src = i < stageCount ? i : stageCount - 1;
                        int value = stagesX[src];
                        if (value < 50) value = 50;
                        if (value > 42000) value = 42000;
                        stages[i] = value;
                    }

                    int target = activeStage - 1;
                    if (target < 0) target = 0;
                    if (target >= stageCount) target = stageCount - 1;

                    if (!WriteDpi(handle, dpi, stages, target, stageCount)) return false;
                    Thread.Sleep(180);

                    DpiInfo after = ReadDpi(handle);
                    if (after == null) return false;

                    bool stageCountOk = after.StageCount == stageCount;
                    bool activeOk = after.ActiveStage == target;
                    bool valuesOk = true;
                    for (int i = 0; i < stageCount; i++)
                    {
                        if (after.StagesX[i] != stages[i])
                        {
                            valuesOk = false;
                            break;
                        }
                    }

                    if (stageCountOk && activeOk && valuesOk)
                    {
                        InvalidateCache();
                        return true;
                    }
                    return false;
                }
                catch
                {
                    return false;
                }
                finally
                {
                    HidTransport.Close(handle);
                }
            }
        }

        /// <summary>写入回报率（Hz）。</summary>
        public static bool SetPollingRate(int hz)
        {
            int index = GetRateIndexForHz(hz);

            lock (hidLock)
            {
                HidDeviceDescriptor descriptor = PickEndpoint();
                if (descriptor == null) return false;

                IntPtr handle = HidTransport.Open(descriptor.DevicePath);
                if (!HidTransport.IsValidHandle(handle)) return false;

                try
                {
                    PerformanceInfo perf = ReadPerformance(handle);
                    if (perf == null) return false;

                    if (!WritePerformance(handle, perf, index)) return false;
                    Thread.Sleep(150);

                    PerformanceInfo after = ReadPerformance(handle);
                    if (after == null) return false;
                    if (DecodeRateIndex(after.UsbRateRaw) == index)
                    {
                        InvalidateCache();
                        return true;
                    }
                    return false;
                }
                catch
                {
                    return false;
                }
                finally
                {
                    HidTransport.Close(handle);
                }
            }
        }

        // ------------------------------------------------------------------
        // 缓存
        // ------------------------------------------------------------------

        /// <summary>写入成功后作废内存缓存，强制下一次查询重新读取设备。</summary>
        private static void InvalidateCache()
        {
            cachedDevice = null;
            lastQueryTime = DateTime.MinValue;
        }

        public static void LoadCache()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(CacheRegistryKey, false))
                {
                    if (key == null) return;
                    object batt = key.GetValue("BatteryPercent");
                    if (batt != null) CachedBatteryPercent = Convert.ToInt32(batt);
                    object chg = key.GetValue("IsCharging");
                    if (chg != null) CachedIsCharging = Convert.ToInt32(chg) == 1;
                    string name = key.GetValue("DeviceName") as string;
                    if (!string.IsNullOrEmpty(name)) CachedDeviceName = name;
                    string updated = key.GetValue("LastUpdated") as string;
                    if (!string.IsNullOrEmpty(updated))
                    {
                        DateTime dt;
                        if (DateTime.TryParse(updated, out dt)) CachedLastUpdated = dt;
                    }

                    string stages = key.GetValue("DpiStages") as string;
                    if (!string.IsNullOrEmpty(stages))
                    {
                        string[] parts = stages.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        List<int> parsed = new List<int>();
                        for (int i = 0; i < parts.Length; i++)
                        {
                            int v;
                            if (int.TryParse(parts[i].Trim(), out v) && v > 0) parsed.Add(v);
                        }
                        if (parsed.Count > 0) CachedDpiStages = parsed.ToArray();
                    }
                    string stagesY = key.GetValue("DpiStagesY") as string;
                    if (!string.IsNullOrEmpty(stagesY))
                    {
                        string[] partsY = stagesY.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                        List<int> parsedY = new List<int>();
                        for (int i = 0; i < partsY.Length; i++)
                        {
                            int v;
                            if (int.TryParse(partsY[i].Trim(), out v) && v > 0) parsedY.Add(v);
                        }
                        if (parsedY.Count > 0) CachedDpiStagesY = parsedY.ToArray();
                    }
                    object stg = key.GetValue("DpiStage");
                    if (stg != null) CachedDpiStage = Convert.ToInt32(stg);
                    object rate = key.GetValue("PollingRate");
                    if (rate != null) CachedPollingRate = Convert.ToInt32(rate);
                }
            }
            catch { }
        }

        public static void SaveCache()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(CacheRegistryKey))
                {
                    if (key == null) return;
                    key.SetValue("BatteryPercent", CachedBatteryPercent, RegistryValueKind.DWord);
                    key.SetValue("IsCharging", CachedIsCharging ? 1 : 0, RegistryValueKind.DWord);
                    if (!string.IsNullOrEmpty(CachedDeviceName)) key.SetValue("DeviceName", CachedDeviceName);
                    if (CachedLastUpdated != DateTime.MinValue) key.SetValue("LastUpdated", CachedLastUpdated.ToString("o"));
                    if (CachedDpiStages != null && CachedDpiStages.Length > 0)
                    {
                        List<string> sx = new List<string>();
                        for (int i = 0; i < CachedDpiStages.Length; i++) sx.Add(CachedDpiStages[i].ToString());
                        key.SetValue("DpiStages", string.Join(",", sx.ToArray()));
                    }
                    if (CachedDpiStagesY != null && CachedDpiStagesY.Length > 0)
                    {
                        List<string> sy = new List<string>();
                        for (int i = 0; i < CachedDpiStagesY.Length; i++) sy.Add(CachedDpiStagesY[i].ToString());
                        key.SetValue("DpiStagesY", string.Join(",", sy.ToArray()));
                    }
                    if (CachedDpiStage > 0) key.SetValue("DpiStage", CachedDpiStage, RegistryValueKind.DWord);
                    if (CachedPollingRate > 0) key.SetValue("PollingRate", CachedPollingRate, RegistryValueKind.DWord);
                }
            }
            catch { }
        }

        // ------------------------------------------------------------------
        // 蓝牙 BLE 形态支持
        // ------------------------------------------------------------------
        //
        // 该鼠标切到蓝牙后不再以 USB HID 厂商通道出现，而是成为 BLE HID over GATT
        // 节点，Windows 在该节点上报的 PID 为 0x4026（与 USB 接口 PID 0x1018 不同）。
        // BLE 形态只暴露标准鼠标/键盘/多媒体集合，**没有 0x4D 厂商通道**：
        //   - 电量：可读，来自 Windows 原生 GATT 电池服务 0x180F（SetupAPI 属性缓存）
        //   - 充电状态：不可读（标准电池服务只给百分比）
        //   - DPI / 回报率：不可读写（无厂商通道），必须如实降级为不支持
        // 因此这里只做“识别 + 电量”，绝不伪造 DPI 与回报率。

        /// <summary>该 PID 是否为 4D 协议族在蓝牙 BLE 形态下的硬件身份。</summary>
        public static bool IsSupportedBlePid(int pid)
        {
            for (int i = 0; i < SupportedBlePids.Length; i++)
            {
                if (SupportedBlePids[i] == pid) return true;
            }
            return false;
        }

        /// <summary>
        /// 从 BTHLE 实例 ID 中解析硬件 PID。
        /// 形如：BTHLEDEVICE\{00001812-0000-1000-8000-00805F9B34FB}_DEV_VID&amp;023837_PID&amp;4026_REV&amp;0100_C19A783235F4
        /// 注意 VID/PID 前面带 "0" 前缀（023837 / 04026），是 Windows 的两位前缀约定。
        /// </summary>
        public static bool TryParseBleVidPid(string instanceId, out int vid, out int pid)
        {
            vid = 0;
            pid = 0;
            if (string.IsNullOrEmpty(instanceId)) return false;

            string upper = instanceId.ToUpperInvariant();
            int vidIdx = upper.IndexOf("VID&");
            int pidIdx = upper.IndexOf("PID&");
            if (vidIdx < 0 || pidIdx < 0) return false;

            if (!TryParseBleHex(upper, vidIdx + 4, out vid)) return false;
            if (!TryParseBleHex(upper, pidIdx + 4, out pid)) return false;
            return vid > 0 && pid > 0;
        }

        /// <summary>解析 BLE 实例 ID 中 "VID&amp;" / "PID&amp;" 之后的十六进制字段，遇非十六进制字符即止。</summary>
        private static bool TryParseBleHex(string text, int start, out int value)
        {
            value = 0;
            if (start < 0 || start >= text.Length) return false;

            int end = start;
            while (end < text.Length && Uri.IsHexDigit(text[end])) end++;
            if (end <= start) return false;

            string hex = text.Substring(start, end - start);
            // Windows 会在 VID/PID 前补两位序号前缀（如 023837 → 3837），
            // 因此只取末 4 位作为真实 VID/PID。
            if (hex.Length > 4) hex = hex.Substring(hex.Length - 4, 4);

            try
            {
                value = int.Parse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
                return true;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>
        /// 枚举当前在线的迈从 4D 协议族 BLE 节点，返回 BTHLE 父节点的实例 ID。
        ///
        /// 关键事实（本机实测，务必保留）：Windows 把 VID/PID 与电量属性分别挂在
        /// 两个**不同**的节点上，任何一步走错都会读不到电量——
        ///   BTHLEDEVICE\{GATT 服务 GUID}_DEV_VID&amp;023837_PID&amp;4026_REV&amp;0100_&lt;MAC&gt;\...
        ///       这里才有 VID/PID，但**没有**电量属性；
        ///   BTHLE\DEV_&lt;MAC&gt;\...
        ///       这里才有 DEVPKEY_Device_BatteryLevel 与 FriendlyName，但实例 ID 里**不含** VID/PID。
        /// 因此必须先从 BTHLEDEVICE 拿到 MAC，再回到 BTHLE 取电量，两步缺一不可。
        /// </summary>
        public static List<string> EnumerateBleNodeInstanceIds()
        {
            List<string> result = new List<string>();

            List<string> macs;
            try
            {
                macs = EnumerateBleDeviceMacs();
            }
            catch
            {
                return result;
            }
            if (macs == null || macs.Count == 0) return result;

            for (int i = 0; i < macs.Count; i++)
            {
                string parent = FindBleParentNodeId(macs[i]);
                if (!string.IsNullOrEmpty(parent)) result.Add(parent);
            }

            return result;
        }

        /// <summary>
        /// 从 BTHLEDEVICE 服务节点枚举出属于迈从 4D 协议族的设备 MAC（12 位大写十六进制）。
        /// 同一个 MAC 会对应多个 GATT 服务节点（0x1800/0x1801/0x180A/0x180F/0x1812），
        /// 这里去重后返回。
        /// </summary>
        public static List<string> EnumerateBleDeviceMacs()
        {
            List<string> result = new List<string>();
            IntPtr devInfo = BluetoothDeviceHelper.SetupDiGetClassDevs(
                IntPtr.Zero, "BTHLEDEVICE", IntPtr.Zero, DigcfPresent | DigcfAllClasses);
            if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return result;

            try
            {
                BluetoothDeviceHelper.SP_DEVINFO_DATA data = new BluetoothDeviceHelper.SP_DEVINFO_DATA();
                data.cbSize = (uint)Marshal.SizeOf(typeof(BluetoothDeviceHelper.SP_DEVINFO_DATA));

                uint idx = 0;
                while (BluetoothDeviceHelper.SetupDiEnumDeviceInfo(devInfo, idx++, ref data))
                {
                    StringBuilder idSb = new StringBuilder(512);
                    uint req;
                    if (!BluetoothDeviceHelper.SetupDiGetDeviceInstanceIdW(devInfo, ref data, idSb, (uint)idSb.Capacity, out req))
                    {
                        continue;
                    }

                    string instId = idSb.ToString();
                    int nodeVid, nodePid;
                    if (!TryParseBleVidPid(instId, out nodeVid, out nodePid)) continue;
                    if (nodeVid != VendorIdMchose) continue;
                    if (!IsSupportedBlePid(nodePid)) continue;

                    string mac = TryParseBleMacFromServiceNode(instId);
                    if (string.IsNullOrEmpty(mac)) continue;
                    if (result.Contains(mac)) continue;
                    result.Add(mac);
                }
            }
            catch { }
            finally
            {
                BluetoothDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);
            }

            return result;
        }

        /// <summary>
        /// 从服务节点实例 ID 中取出设备 MAC。
        /// 形如：BTHLEDEVICE\{...GUID...}_DEV_VID&amp;023837_PID&amp;4026_REV&amp;0100_C19A783235F4\9&amp;27EA35C3&amp;0&amp;0010
        /// MAC 是第一个反斜杠前、最后一个下划线后的 12 位字段。
        /// 注意不能用 BluetoothDeviceHelper.ExtractMac：它按 "DEV_" 后取 12 位，
        /// 对这种带 VID&amp;/PID&amp; 前缀的 BLE 格式会截成 "VID&amp;023837_P"。
        /// </summary>
        public static string TryParseBleMacFromServiceNode(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return null;

            // MAC 位于**最后**一个反斜杠之前、且是该段中最后一个下划线之后的部分。
            // 不能取第一个反斜杠：实例 ID 形如 "BTHLEDEVICE\{GUID}_DEV_..._<MAC>\9&..."，
            // 首个反斜杠在 "BTHLEDEVICE" 之后就出现了，会把整段截成 "BTHLEDEVICE"。
            int slash = instanceId.LastIndexOf('\\');
            string head = (slash > 0) ? instanceId.Substring(0, slash) : instanceId;

            int us = head.LastIndexOf('_');
            if (us < 0 || head.Length - us - 1 < 12) return null;

            string mac = head.Substring(us + 1).Trim().ToUpperInvariant();
            if (mac.Length != 12) return null;

            for (int i = 0; i < mac.Length; i++)
            {
                if (!Uri.IsHexDigit(mac[i])) return null;
            }
            return mac;
        }

        /// <summary>
        /// 在 BTHLE 枚举器中按 MAC 找到父节点实例 ID（形如 BTHLE\DEV_C19A783235F4\8&amp;71E4D4F&amp;0&amp;C19A783235F4）。
        /// 该节点被标记为已断开时返回 null，避免把历史缓存当成在线设备。
        /// </summary>
        public static string FindBleParentNodeId(string mac)
        {
            if (string.IsNullOrEmpty(mac)) return null;

            IntPtr devInfo = BluetoothDeviceHelper.SetupDiGetClassDevs(
                IntPtr.Zero, "BTHLE", IntPtr.Zero, DigcfPresent | DigcfAllClasses);
            if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return null;

            string found = null;
            try
            {
                BluetoothDeviceHelper.SP_DEVINFO_DATA data = new BluetoothDeviceHelper.SP_DEVINFO_DATA();
                data.cbSize = (uint)Marshal.SizeOf(typeof(BluetoothDeviceHelper.SP_DEVINFO_DATA));

                string needle = "DEV_" + mac.ToUpperInvariant();

                uint idx = 0;
                while (BluetoothDeviceHelper.SetupDiEnumDeviceInfo(devInfo, idx++, ref data))
                {
                    StringBuilder idSb = new StringBuilder(512);
                    uint req;
                    if (!BluetoothDeviceHelper.SetupDiGetDeviceInstanceIdW(devInfo, ref data, idSb, (uint)idSb.Capacity, out req))
                    {
                        continue;
                    }

                    string instId = idSb.ToString();
                    if (instId.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (IsBleNodeDisconnected(devInfo, ref data)) continue;

                    found = instId;
                    break;
                }
            }
            catch { }
            finally
            {
                BluetoothDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);
            }

            return found;
        }

        /// <summary>读取 DEVPKEY_Device_IsConnected，判定该 BLE 节点当前是否真的连着。</summary>
        private static bool IsBleNodeConnected(IntPtr devInfo, ref BluetoothDeviceHelper.SP_DEVINFO_DATA data)
        {
            try
            {
                byte[] connBuf = new byte[16];
                uint pType;
                uint req;
                if (BluetoothDeviceHelper.SetupDiGetDevicePropertyW(
                        devInfo, ref data, ref BluetoothDeviceHelper.DEVPKEY_Device_IsConnected,
                        out pType, connBuf, (uint)connBuf.Length, out req, 0))
                {
                    return connBuf[0] != 0;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// 判定该 BLE 节点是否**明确**已断开。
        /// 与 IsBleNodeConnected 的区别：属性读不到时返回 false（视为"未知，先不排除"），
        /// 只有系统明确回了 IsConnected = 0 才算断开。这样在部分系统版本上
        /// 该属性不可读时，不会把在线设备误判成离线而整卡消失。
        /// </summary>
        private static bool IsBleNodeDisconnected(IntPtr devInfo, ref BluetoothDeviceHelper.SP_DEVINFO_DATA data)
        {
            try
            {
                byte[] connBuf = new byte[16];
                uint pType;
                uint req;
                if (BluetoothDeviceHelper.SetupDiGetDevicePropertyW(
                        devInfo, ref data, ref BluetoothDeviceHelper.DEVPKEY_Device_IsConnected,
                        out pType, connBuf, (uint)connBuf.Length, out req, 0))
                {
                    return connBuf[0] == 0;
                }
            }
            catch { }
            return false;
        }

        /// <summary>
        /// 从 BLE 节点的 Windows 属性缓存读取电量（GATT 电池服务 0x180F 的映射值）。
        /// 读不到时返回 -1，绝不用缓存值冒充实时值。
        /// </summary>
        public static int ReadBleBattery(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return -1;

            IntPtr devInfo = BluetoothDeviceHelper.SetupDiGetClassDevs(
                IntPtr.Zero, "BTHLE", IntPtr.Zero, DigcfPresent | DigcfAllClasses);
            if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return -1;

            int battery = -1;
            try
            {
                BluetoothDeviceHelper.SP_DEVINFO_DATA data = new BluetoothDeviceHelper.SP_DEVINFO_DATA();
                data.cbSize = (uint)Marshal.SizeOf(typeof(BluetoothDeviceHelper.SP_DEVINFO_DATA));

                uint idx = 0;
                while (BluetoothDeviceHelper.SetupDiEnumDeviceInfo(devInfo, idx++, ref data))
                {
                    StringBuilder idSb = new StringBuilder(512);
                    uint req;
                    if (!BluetoothDeviceHelper.SetupDiGetDeviceInstanceIdW(devInfo, ref data, idSb, (uint)idSb.Capacity, out req))
                    {
                        continue;
                    }
                    if (!string.Equals(idSb.ToString(), instanceId, StringComparison.OrdinalIgnoreCase)) continue;

                    byte[] battBuf = new byte[16];
                    uint pType;
                    if (BluetoothDeviceHelper.SetupDiGetDevicePropertyW(
                            devInfo, ref data, ref BluetoothDeviceHelper.DEVPKEY_Device_BatteryLevel,
                            out pType, battBuf, (uint)battBuf.Length, out req, 0))
                    {
                        // 该属性是 DEVPROP_TYPE_BYTE（单字节 0-100），
                        // 不能按 ToInt32 读——那会把相邻字节一并算进来得到荒谬数值。
                        int value = (int)battBuf[0];
                        if (value >= 0 && value <= 100) battery = value;
                    }
                    break;
                }
            }
            catch { }
            finally
            {
                BluetoothDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);
            }

            return battery;
        }

        /// <summary>
        /// 查询蓝牙形态下的迈从 4D 协议族鼠标。
        /// 只填电量与身份；DPI / 回报率保持 0，并由驱动层能力契约如实标记为不支持。
        /// 未检测到在线 BLE 节点时返回 null。
        /// </summary>
        public static MouseBatteryInfo QueryMchose4DMouseBle()
        {
            string mac;
            int nodePid;
            if (!TryFindOnlineBleDevice(out mac, out nodePid)) return null;

            // BTHLE 父节点（电量与 FriendlyName 都在它身上）。
            string parentId = FindBleParentNodeId(mac);
            if (string.IsNullOrEmpty(parentId)) return null;

            int battery = ReadBleBattery(parentId);

            string name = ReadBleFriendlyName(parentId);
            if (string.IsNullOrEmpty(name))
            {
                name = !string.IsNullOrEmpty(CachedDeviceName) ? CachedDeviceName : "MCHOSE 无线游戏鼠标";
            }

            MouseBatteryInfo dev = new MouseBatteryInfo();
            dev.DeviceId = string.Format("MCHOSE:{0:X4}:{1:X4}", VendorIdMchose, nodePid);
            dev.HardwareFingerprint = string.Format("BLE:{0:X4}:{1:X4}", VendorIdMchose, nodePid);
            dev.HardwareId = parentId;
            dev.Brand = "MCHOSE";
            dev.Category = DeviceCategory.Mouse;
            dev.DeviceName = name;
            dev.Transport = "蓝牙 BLE";
            dev.IsCustom = false;
            dev.IsConnected = battery >= 0;
            dev.IsSleeping = false;
            // BLE 节点在线即代表接收器侧不需要，标记为无接收器。
            dev.IsDonglePresent = false;
            dev.BatteryPercent = battery;
            dev.ExactBatteryPercent = battery;
            // 标准 GATT 电池服务只给百分比，没有充电标志位；
            // 这里恒为 false，绝不猜测充电状态。
            dev.IsCharging = false;
            dev.LastUpdated = (battery >= 0) ? DateTime.Now : CachedLastUpdated;

            // 蓝牙形态无厂商通道：DPI 与回报率必须留空，
            // 由 MchoseMouseDriver.PopulateCapabilities 依据 Transport 降级。
            dev.Dpi = 0;
            dev.DpiY = 0;
            dev.DpiStage = 0;
            dev.DpiStageCount = 0;
            dev.DpiStages = null;
            dev.DpiStagesY = null;
            dev.IsXyIndependent = false;
            dev.XyRatio = 1.0;
            dev.PollingRate = 0;
            dev.SupportedPollingRates = new int[0];
            dev.MaxPollingRate = 0;

            if (battery >= 0)
            {
                CachedBatteryPercent = battery;
                CachedDeviceName = name;
                CachedLastUpdated = dev.LastUpdated;
                SaveCache();
            }

            return dev;
        }

        /// <summary>
        /// 找到第一个在线的迈从 4D 协议族 BLE 设备，返回其 MAC 与服务节点上报的 PID。
        /// PID 只能从 BTHLEDEVICE 服务节点取得（BTHLE 父节点实例 ID 不含 VID/PID）。
        /// </summary>
        public static bool TryFindOnlineBleDevice(out string mac, out int pid)
        {
            mac = null;
            pid = 0;

            List<string> macs;
            try
            {
                macs = EnumerateBleDeviceMacs();
            }
            catch
            {
                return false;
            }
            if (macs == null) return false;

            for (int i = 0; i < macs.Count; i++)
            {
                int foundPid;
                if (!TryGetBlePidForMac(macs[i], out foundPid)) continue;
                if (!FindBleParentNodeIdOnline(macs[i])) continue;

                mac = macs[i];
                pid = foundPid;
                return true;
            }

            return false;
        }

        /// <summary>按 MAC 从 BTHLEDEVICE 服务节点取回 PID（同一设备的多个服务节点 PID 一致）。</summary>
        private static bool TryGetBlePidForMac(string mac, out int pid)
        {
            pid = 0;
            if (string.IsNullOrEmpty(mac)) return false;

            IntPtr devInfo = BluetoothDeviceHelper.SetupDiGetClassDevs(
                IntPtr.Zero, "BTHLEDEVICE", IntPtr.Zero, DigcfPresent | DigcfAllClasses);
            if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return false;

            bool ok = false;
            try
            {
                BluetoothDeviceHelper.SP_DEVINFO_DATA data = new BluetoothDeviceHelper.SP_DEVINFO_DATA();
                data.cbSize = (uint)Marshal.SizeOf(typeof(BluetoothDeviceHelper.SP_DEVINFO_DATA));

                uint idx = 0;
                while (BluetoothDeviceHelper.SetupDiEnumDeviceInfo(devInfo, idx++, ref data))
                {
                    StringBuilder idSb = new StringBuilder(512);
                    uint req;
                    if (!BluetoothDeviceHelper.SetupDiGetDeviceInstanceIdW(devInfo, ref data, idSb, (uint)idSb.Capacity, out req))
                    {
                        continue;
                    }

                    string instId = idSb.ToString();
                    string nodeMac = TryParseBleMacFromServiceNode(instId);
                    if (string.IsNullOrEmpty(nodeMac)) continue;
                    if (!string.Equals(nodeMac, mac, StringComparison.OrdinalIgnoreCase)) continue;

                    int nodeVid, nodePid;
                    if (!TryParseBleVidPid(instId, out nodeVid, out nodePid)) continue;
                    if (nodeVid != VendorIdMchose) continue;

                    pid = nodePid;
                    ok = true;
                    break;
                }
            }
            catch { }
            finally
            {
                BluetoothDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);
            }

            return ok;
        }

        /// <summary>该 MAC 的 BTHLE 父节点是否存在且未被明确标记为断开。</summary>
        private static bool FindBleParentNodeIdOnline(string mac)
        {
            return !string.IsNullOrEmpty(FindBleParentNodeId(mac));
        }

        /// <summary>读取 BLE 节点的 FriendlyName（设备在系统里显示的名字）。</summary>
        private static string ReadBleFriendlyName(string instanceId)
        {
            if (string.IsNullOrEmpty(instanceId)) return null;

            IntPtr devInfo = BluetoothDeviceHelper.SetupDiGetClassDevs(
                IntPtr.Zero, "BTHLE", IntPtr.Zero, DigcfPresent | DigcfAllClasses);
            if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return null;

            string name = null;
            try
            {
                BluetoothDeviceHelper.SP_DEVINFO_DATA data = new BluetoothDeviceHelper.SP_DEVINFO_DATA();
                data.cbSize = (uint)Marshal.SizeOf(typeof(BluetoothDeviceHelper.SP_DEVINFO_DATA));

                uint idx = 0;
                while (BluetoothDeviceHelper.SetupDiEnumDeviceInfo(devInfo, idx++, ref data))
                {
                    StringBuilder idSb = new StringBuilder(512);
                    uint req;
                    if (!BluetoothDeviceHelper.SetupDiGetDeviceInstanceIdW(devInfo, ref data, idSb, (uint)idSb.Capacity, out req))
                    {
                        continue;
                    }
                    if (!string.Equals(idSb.ToString(), instanceId, StringComparison.OrdinalIgnoreCase)) continue;

                    byte[] nameBuf = new byte[512];
                    uint pType;
                    if (BluetoothDeviceHelper.SetupDiGetDevicePropertyW(
                            devInfo, ref data, ref BluetoothDeviceHelper.DEVPKEY_Device_FriendlyName,
                            out pType, nameBuf, (uint)nameBuf.Length, out req, 0))
                    {
                        name = Encoding.Unicode.GetString(nameBuf, 0, (int)req).TrimEnd('\0', ' ', '\t');
                    }
                    break;
                }
            }
            catch { }
            finally
            {
                BluetoothDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);
            }

            return name;
        }
    }
}
