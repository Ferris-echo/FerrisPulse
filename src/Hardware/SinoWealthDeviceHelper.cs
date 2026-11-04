// ============================================================================
// 模块名称: SinoWealthDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   赛诺韦尔 (Sino Wealth / SINOWEALTH, VID 0x258A) Transceiver 方案外设适配。
//   典型产品为 2.4G 键鼠套装（如 Valkyrie / 达摩鲨 / 多彩 等贴牌），本机实测
//   VID 258A / PID 010C：MI_00 = 键盘、MI_01 = 鼠标 + 厂商集合。
//
//   ★ 关键架构 (本机实测端点分布，厂商集合被拆成多个独立 HID 接口)：
//     col03 = Input 4 字节（无 Feature，切勿在其上发 Feature）
//     col05 = Feature 6 字节   → 0x05 短命令（读选择器）
//     col06 = Feature 520 字节 → 0x04 / 0x06 配置帧；Input 8 字节 → 0x07 通知
//   因此「命令」与「配置」必须分别打开对应集合，不能只取第一个 0xFF00 端点。
//
//   协议来源 (社区逆向，MouseFlash 项目，已与 HID 报告描述符交叉验证)：
//     0x04 Feature 报告 (read/write): 配置（长鼠标为 0x06）。
//     0x05 Feature 报告 (6 字节): 短命令，[1]=命令字。
//     0x07 Input 报告 (8 字节): 异步通知（如 DPI 切换）。
//     读配置：在命令集合(col05)发 0x05 命令 0x11，再在配置集合(col06)读 0x04（长鼠标 0x06）。
//     配置 (命令 0x11) 关键偏移（整帧含 Report ID，下标 0 = ID）:
//       [10] 高 4 位 = 标志位 (bit0x8 = X/Y 轴独立)，低 4 位 = 回报率档 (1/2/3/4 = 125/250/500/1000Hz)
//       [11] 高 4 位 = 当前激活 DPI 档，低 4 位 = 已启用档位数
//       [13..28] 16 个 DPI 档原始值（每档 1 字节）
//
//   电量 (核心目标): SinoWealth 配置帧 (命令 0x11) 【不包含】电量字段，电量由独立的
//   厂商私有查询命令返回。真值来源：开源项目 GloriousBatteryMonitor-Go (同为 258A 方案，
//   与多份 USB 抓包交叉确认)：
//     命令负载 6 字节 = 00 00 02 02 00 83  → [DevSel=0x02 无线] [CmdType=0x02 电量查询] [Func=0x83]
//     响应布局 [0]RID [1]状态(A1 正常/A2 唤醒/A4 休眠) [2..5]回显 [6]=0x83 [7]充电(非0) [8]电量%
//   本机 col05 的 Feature 报告恰为 6 字节且不含 Report ID，命令负载可直接整包下发；
//   响应在 col06 (Feature 520 字节) 上按 Report ID 0x00~0x0F 扫描读取。
//   标准 HID 电池页 (UsagePage 0x85) 作为兜底；两条都失败时 BatteryPercent = -1
//   ⇒ SupportsBattery = false，绝不虚标。
//
// 依赖关系: HidTransport.cs, MouseBatteryInfo.cs, DeviceCapabilities.cs, GenericHidHelper.cs, HardwareDriverBase.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 修改命令字、字段偏移或解析逻辑时必须同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止 $"..." 插值、空条件运算符、nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;

namespace RazerBatteryTray
{
    public static class SinoWealthDeviceHelper
    {
        private static readonly object hidLock = new object();

        public const int VendorId = 0x258A;
        public const int ProductId = 0x010C;

        // 厂商通道（与 G213Colors hidapitester 实测 258A 一致）
        public const int VendorUsagePage = 0xFF00;
        public const int VendorUsage = 0x0001;

        private const byte ReportCommand = 0x05;   // 短命令 Feature 报告
        private const byte ReportConfig = 0x04;     // 配置 Feature 报告 (read/write)
        private const byte ReportConfigLong = 0x06; // 长鼠标的配置 Feature 报告
        private const byte CmdConfig = 0x11;        // 读/写配置

        // 配置帧偏移（下标 0 = Report ID）
        private const int OffsetFlagsPolling = 10;  // 高4位: 标志(bit0x8=XY独立); 低4位: 回报率档
        private const int OffsetActiveStages = 11;  // 高4位: 激活档; 低4位: 已启用档数
        private const int OffsetDpiValues = 13;     // 16 个 DPI 档原始值（每档 1 字节）
        private const int MaxStages = 16;

        private const int QueryThrottleMs = 3000;
        private const int ReadTimeoutMs = 400;

        private static DateTime lastQueryTime = DateTime.MinValue;
        private static List<MouseBatteryInfo> cachedDevices = new List<MouseBatteryInfo>();

        // 离线/休眠电量缓存（真实读数拿不到时回显，绝不虚构）
        private static int cachedBattery = -1;
        private static bool cachedCharging = false;

        public static bool IsSinoWealthDevice(string identifier)
        {
            if (string.IsNullOrEmpty(identifier)) return false;
            string lower = identifier.ToLowerInvariant();
            return (lower.Contains("vid_258a") || lower.Contains("258a:")) &&
                   (lower.Contains("pid_010c") || lower.Contains(":010c"));
        }

        public static bool IsSupportedVidPid(int vid, int pid)
        {
            return vid == VendorId && pid == ProductId;
        }

        private static int MapPollingNibble(int nibble)
        {
            switch (nibble)
            {
                case 1: return 125;
                case 2: return 250;
                case 3: return 500;
                case 4: return 1000;
                default: return 0;
            }
        }

        // 该收发器把厂商私有集合拆成多个独立 HID 接口（本机实测，VID 258A:PID 010C）：
        //   col05 = Feature 6 字节   → 0x05 短命令（读选择器）
        //   col06 = Feature 520 字节 → 0x04 / 0x06 配置帧；Input 8 字节 → 0x07 通知
        //   col03 = Input 4 字节（无 Feature，切勿在其上发 Feature）
        // 因此必须分别锁定「命令集合」与「配置集合」，绝不能只取第一个 0xFF00 端点。
        private static HidDeviceDescriptor SelectCommandChannel(List<HidDeviceDescriptor> descriptors)
        {
            if (descriptors == null) return null;
            HidDeviceDescriptor best = null;
            for (int i = 0; i < descriptors.Count; i++)
            {
                HidDeviceDescriptor d = descriptors[i];
                if (d.UsagePage != VendorUsagePage) continue;
                if (d.FeatureReportLength >= 6 && d.FeatureReportLength <= 64)
                {
                    if (best == null || d.FeatureReportLength < best.FeatureReportLength) best = d;
                }
            }
            return best;
        }

        private static HidDeviceDescriptor SelectConfigChannel(List<HidDeviceDescriptor> descriptors)
        {
            if (descriptors == null) return null;
            HidDeviceDescriptor best = null;
            for (int i = 0; i < descriptors.Count; i++)
            {
                HidDeviceDescriptor d = descriptors[i];
                if (d.UsagePage != VendorUsagePage) continue;
                if (d.FeatureReportLength >= 100)
                {
                    if (best == null || d.FeatureReportLength > best.FeatureReportLength) best = d;
                }
            }
            return best;
        }

        private static bool TrySendConfigCommand(string devicePath)
        {
            IntPtr handle = HidTransport.Open(devicePath);
            if (!HidTransport.IsValidHandle(handle)) return false;
            try
            {
                byte[] cmd = new byte[6];
                cmd[0] = ReportCommand;
                cmd[1] = CmdConfig;
                return HidTransport.SetFeature(handle, cmd);
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

        // 真配置帧的判据：报告 ID 匹配，且第 2 字节回显命令 0x11。
        // 本机实测：该设备读取 0x06 报告时若未下发 0x11 命令，会返回「USB/HID 描述符」
        // 原始数据（[1] 多为 0x04，含 "BYTech" 字符串与键盘 Report Descriptor），
        // 必须排除，否则会把描述符误当配置解析出错误的 DPI / 回报率。
        private static bool IsConfigFrame(byte[] rx, byte reportId)
        {
            return rx != null && rx.Length >= 2 && rx[0] == reportId && rx[1] == CmdConfig;
        }

        private static byte[] TryReadConfigOnHandle(string devicePath, int length, bool sendCommandFirst)
        {
            IntPtr handle = HidTransport.Open(devicePath);
            if (!HidTransport.IsValidHandle(handle)) return null;
            try
            {
                if (sendCommandFirst)
                {
                    byte[] cmd = new byte[6];
                    cmd[0] = ReportCommand;
                    cmd[1] = CmdConfig;
                    HidTransport.SetFeature(handle, cmd);
                    System.Threading.Thread.Sleep(45);
                }
                byte[] rx;
                if (HidTransport.ReadFeatureReport(handle, ReportConfig, length, out rx) && IsConfigFrame(rx, ReportConfig))
                {
                    return rx;
                }
                byte[] rx2;
                if (HidTransport.ReadFeatureReport(handle, ReportConfigLong, length, out rx2) && IsConfigFrame(rx2, ReportConfigLong))
                {
                    return rx2;
                }
                return null;
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

        // 读配置：兼容「单集合」与「跨集合」两种固件布局。
        private static byte[] ReadConfig(HidDeviceDescriptor configCh, HidDeviceDescriptor cmdCh)
        {
            if (configCh == null) return null;
            int len = configCh.FeatureReportLength;
            if (len < 16) len = 16;
            if (len > 520) len = 520;

            // 策略 A：在配置集合自身上发命令并读取
            byte[] rx = TryReadConfigOnHandle(configCh.DevicePath, len, true);
            if (rx != null) return rx;

            // 策略 B：命令走独立短命令集合 (col05)，配置读取走配置集合 (col06)
            if (cmdCh != null && !string.Equals(cmdCh.DevicePath, configCh.DevicePath, StringComparison.OrdinalIgnoreCase))
            {
                TrySendConfigCommand(cmdCh.DevicePath);
                System.Threading.Thread.Sleep(45);
                rx = TryReadConfigOnHandle(configCh.DevicePath, len, false);
                if (rx != null) return rx;
            }

            // 兜底：不发命令直接读取
            return TryReadConfigOnHandle(configCh.DevicePath, len, false);
        }

        private static bool ParseConfig(byte[] rx, int length, out int pollingRate, out bool xyIndependent,
            out int activeStage, out int stageCount, out int[] dpiStages)
        {
            pollingRate = 0;
            xyIndependent = false;
            activeStage = 0;
            stageCount = 0;
            dpiStages = null;
            if (rx == null || length < OffsetDpiValues + 1) return false;
            if (rx[0] != ReportConfig && rx[0] != ReportConfigLong) return false;
            if (rx[1] != CmdConfig) return false;

            byte flags = rx[OffsetFlagsPolling];
            xyIndependent = (flags & 0x80) != 0;
            pollingRate = MapPollingNibble(flags & 0x0F);

            byte stageByte = rx[OffsetActiveStages];
            activeStage = (stageByte >> 4) & 0x0F;
            stageCount = stageByte & 0x0F;

            dpiStages = new int[MaxStages];
            for (int i = 0; i < MaxStages; i++)
            {
                int idx = OffsetDpiValues + i;
                dpiStages[i] = (idx < length) ? (int)rx[idx] : 0;
            }
            return true;
        }

        // ==================== 厂商私有电量命令 (Func 0x83) ====================
        // 真值来源：GloriousBatteryMonitor-Go（同为 258A 方案），其命令字节为 00 00 02 02 00 83，
        // 语义为 [DevSel=0x02 无线] [CmdType=0x02 电量查询] [Func=0x83]。
        //
        // 本机实测修正（探针 v6）：该接收器的命令集合【只接受 Report ID = 0x05 的 Feature 写入】——
        //   SetFeature(05 11 00 00 00 00) = True   （MouseFlash 配置命令，带 RID 0x05）
        //   SetFeature(00 00 02 02 00 83) = False  （官方载荷首字节 0x00 被拒）
        // 故把官方载荷首字节替换为 RID 0x05 作为首选候选，原样载荷作为次选。
        private static readonly byte[][] BatteryQueryCommands = new byte[][]
        {
            new byte[] { 0x05, 0x00, 0x02, 0x02, 0x00, 0x83 },  // 首选：官方载荷 + 本机可接受的 RID 0x05
            new byte[] { 0x00, 0x00, 0x02, 0x02, 0x00, 0x83 },  // 次选：Glorious 原样载荷
            new byte[] { 0x05, 0x02, 0x02, 0x00, 0x83, 0x00 }   // 兜底：载荷前移一字节
        };

        private const int RfRoundTripDelayMs = 120;  // USB 下发 -> 2.4G 查询 -> 鼠标应答 -> USB 回填
        private const int OffsetBatteryFunc = 6;     // 响应 [6] 必须为 0x83
        private const int OffsetCharging = 7;        // 响应 [7] 非 0 = 在充电/已充满
        private const int OffsetBatteryLevel = 8;    // 响应 [8] = 电量百分比
        private const byte FuncBattery = 0x83;

        // 下发厂商电量命令并解析响应。成功才返回 true，绝不返回推测值。
        public static bool QueryVendorBattery(HidDeviceDescriptor cmdCh, HidDeviceDescriptor dataCh,
            out int percent, out bool isCharging)
        {
            percent = -1;
            isCharging = false;
            if (cmdCh == null) return false;

            IntPtr cmdHandle = IntPtr.Zero;
            IntPtr readHandle = IntPtr.Zero;
            bool sharedHandle = false;
            try
            {
                cmdHandle = HidTransport.Open(cmdCh.DevicePath);
                if (!HidTransport.IsValidHandle(cmdHandle)) return false;

                HidDeviceDescriptor readCh = dataCh;
                if (readCh == null || readCh.FeatureReportLength <= OffsetBatteryLevel) readCh = cmdCh;
                int readLen = readCh.FeatureReportLength;
                if (readLen > 520) readLen = 520;
                if (readLen <= OffsetBatteryLevel) return false;

                if (string.Equals(readCh.DevicePath, cmdCh.DevicePath, StringComparison.OrdinalIgnoreCase))
                {
                    readHandle = cmdHandle;
                    sharedHandle = true;
                }
                else
                {
                    readHandle = HidTransport.Open(readCh.DevicePath);
                    if (!HidTransport.IsValidHandle(readHandle)) return false;
                }

                // 逐个候选命令尝试；任一候选被固件接受（SetFeature 返回 true）后立即扫描响应。
                for (int c = 0; c < BatteryQueryCommands.Length; c++)
                {
                    if (!HidTransport.SetFeature(cmdHandle, BatteryQueryCommands[c])) continue;
                    System.Threading.Thread.Sleep(RfRoundTripDelayMs);

                    // 响应可能挂在任意 Report ID 上（该固件未公开文档），故全量扫描 0x00~0x0F。
                    for (int rid = 0; rid <= 0x0F; rid++)
                    {
                        byte[] buf = new byte[readLen];
                        buf[0] = (byte)rid;
                        if (!HidTransport.GetFeature(readHandle, buf)) continue;
                        // 严格校验：必须回显功能字 0x83，且电量为 1..100 的有效值。
                        // 该固件的 Feature 回读会复述上一次写入的载荷，此校验可避免把回显误判为电量。
                        if (buf[OffsetBatteryFunc] != FuncBattery) continue;
                        int level = buf[OffsetBatteryLevel];
                        if (level < 1 || level > 100) continue;
                        percent = level;
                        isCharging = (buf[OffsetCharging] != 0x00);
                        return true;
                    }
                }
                return false;
            }
            catch
            {
                return false;
            }
            finally
            {
                if (!sharedHandle && HidTransport.IsValidHandle(readHandle)) HidTransport.Close(readHandle);
                if (HidTransport.IsValidHandle(cmdHandle)) HidTransport.Close(cmdHandle);
            }
        }

        // 保守电量探测：仅标准 HID 电池页 (UsagePage 0x85) 的 Feature 报告，绝不盲扫厂商帧或 Input 管道。
        // 标准电池页 (0x85) 字段布局：f[1] = Battery State (2=Charging), f[2] = Battery Capacity (0-100)。
        // 退化扫描仅在首字节疑似容量值时生效，且严格限制在 1..100，避免把鼠标移动字节误判为电量。
        // 兼容性修正：部分固件的标准电池页使用非零 Report ID（多为 1），逐 ID 尝试，避免把真实电池页误判为「无电量」。
        public static bool QueryStandardHidBattery(out int percent, out bool isCharging)
        {
            percent = -1;
            isCharging = false;
            try
            {
                List<HidDeviceDescriptor> descs = HidTransport.Enumerate(VendorId, ProductId);
                int[] candidateIds = new int[] { 0, 1, 2 };
                for (int i = 0; i < descs.Count; i++)
                {
                    HidDeviceDescriptor d = descs[i];
                    if (d.UsagePage != 0x85) continue;
                    if (d.FeatureReportLength <= 1) continue;
                    IntPtr handle = HidTransport.Open(d.DevicePath);
                    if (!HidTransport.IsValidHandle(handle)) continue;
                    try
                    {
                        for (int c = 0; c < candidateIds.Length; c++)
                        {
                            byte[] f = new byte[d.FeatureReportLength];
                            f[0] = (byte)candidateIds[c];
                            if (!HidTransport.GetFeature(handle, f)) continue;
                            if (f.Length >= 3 && f[2] >= 0 && f[2] <= 100)
                            {
                                percent = (int)f[2];
                                isCharging = (f[1] == 2);
                                return true;
                            }
                            for (int k = 1; k < f.Length; k++)
                            {
                                if (f[k] > 0 && f[k] <= 100)
                                {
                                    percent = (int)f[k];
                                    return true;
                                }
                            }
                        }
                    }
                    finally
                    {
                        HidTransport.Close(handle);
                    }
                }
            }
            catch { }
            return false;
        }

        public static List<MouseBatteryInfo> QueryAllSinoWealthDevices()
        {
            lock (hidLock)
            {
                if ((DateTime.Now - lastQueryTime).TotalMilliseconds < QueryThrottleMs)
                {
                    return new List<MouseBatteryInfo>(cachedDevices);
                }
                lastQueryTime = DateTime.Now;

                List<HidDeviceDescriptor> descriptors = HidTransport.Enumerate(VendorId, ProductId);
                HidDeviceDescriptor cmdCh = SelectCommandChannel(descriptors);
                HidDeviceDescriptor cfgCh = SelectConfigChannel(descriptors);
                if (cfgCh == null) return new List<MouseBatteryInfo>();

                byte[] config = ReadConfig(cfgCh, cmdCh);
                int pollingRate, activeStage, stageCount;
                bool xyIndependent;
                int[] dpiStages;
                bool parsed = ParseConfig(config, (config != null ? config.Length : 0),
                    out pollingRate, out xyIndependent, out activeStage, out stageCount, out dpiStages);

                MouseBatteryInfo info = new MouseBatteryInfo();
                info.DeviceId = string.Format("SinoWealth:{0:X4}:{1:X4}", VendorId, ProductId);
                info.HardwareFingerprint = string.Format("HID:{0:X4}:{1:X4}", VendorId, ProductId);
                info.HardwareId = cfgCh.InstanceId;
                info.Brand = "SinoWealth";
                info.Category = DeviceCategory.Mouse;
                info.Transport = "2.4G 无线";
                info.IsCustom = false;
                info.IsDonglePresent = true;
                info.IsConnected = true;
                // 命名纠错：258A:010C 是多模键鼠接收器（MI_00 键盘 / MI_01 鼠标），
                // 并非 Valkyrie VK M3（后者 2.4G 为 249A:5C2F，见官方 config.xml），
                // 早期误标为「VALKYRIE 贴牌」会让用户把它当成自己的鼠标，故改为按芯片与型号命名。
                info.DeviceName = "SinoWealth 2.4G 无线鼠标 (258A:010C)";

                List<string> notes = new List<string>();
                if (parsed)
                {
                    info.PollingRate = pollingRate;
                    info.SupportedPollingRates = new int[] { 125, 250, 500, 1000 };
                    info.MaxPollingRate = 1000;
                    info.IsXyIndependent = xyIndependent;
                    info.DpiStage = activeStage;
                    info.DpiStageCount = stageCount;
                    info.DpiStages = dpiStages;
                    info.Dpi = (activeStage < MaxStages && dpiStages != null) ? dpiStages[activeStage] : 0;
                    info.DpiY = info.Dpi;
                    notes.Add("DPI 为固件原始单位值（精确比例待 HID 抓包确认）");
                }

                // 电量：优先厂商私有命令 (Func 0x83)，失败再退回标准 HID 电池页；
                // 两条都拿不到真实读数则不虚标（BatteryPercent=-1 ⇒ SupportsBattery=false）。
                int batt = -1;
                bool chg = false;
                bool gotVendor = QueryVendorBattery(cmdCh, cfgCh, out batt, out chg);
                bool gotBattery = gotVendor || QueryStandardHidBattery(out batt, out chg);
                if (gotBattery && batt >= 0)
                {
                    info.BatteryPercent = batt;
                    info.ExactBatteryPercent = batt;
                    info.IsCharging = chg;
                    cachedBattery = batt;
                    cachedCharging = chg;
                    if (gotVendor) notes.Add("电量来源：厂商私有命令 0x83");
                }
                else if (cachedBattery >= 0)
                {
                    // 本轮未拿到读数（设备休眠/被官方软件占用通道），回显缓存电量，不虚构新值。
                    info.BatteryPercent = cachedBattery;
                    info.ExactBatteryPercent = cachedBattery;
                    info.IsCharging = cachedCharging;
                }
                else
                {
                    info.BatteryPercent = -1;
                    info.ExactBatteryPercent = -1.0;
                    notes.Add("厂商命令 0x83 与本机响应通道尚未打通，电量暂不可读（详见 tools/find_sinowealth_battery.ps1 输出）");
                }
                info.CustomTooltip = string.Join("；", notes.ToArray());
                info.LastUpdated = DateTime.Now;

                List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
                result.Add(info);
                cachedDevices = new List<MouseBatteryInfo>(result);
                return result;
            }
        }
    }
}
