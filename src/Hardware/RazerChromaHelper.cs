// ============================================================================
// 模块名称: RazerChromaHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   雷蛇 Chroma 扩展矩阵 (class=0x0F) 灯效与滚轮物理机构控制，面向
//   Basilisk V3 Pro 系列 (1532:00AA 有线 / 1532:00AB 2.4G 接收器) 实机验证：
//     · 整只 (LED_ID=0x00) 与物理分区 (0x01 滚轮 / 0x04 Logo / 0x0A 底光) 静态色
//     · 静态 / 呼吸 / 光谱 / 波浪 / 响应 五类灯效（星光在本机固件回 0x05 NOT_SUPPORTED，故不实现）
//     · 全局亮度 (0x0F 0x04)
//     · 滚轮模式 (0x02 0x14 段落阻尼 ↔ 无阻尼自由滚动)
//     · 滚轮加速 (0x02 0x16)、智能滚轮 Smart Reel (0x02 0x17)
// 依赖关系: HidTransport.cs (91 字节 Feature 端点枚举与 Set/GetFeature)
//
// 协议依据 (与 OpenRazer razerchromacommon.c 双向印证, 2026-11-07 本机实测):
//   razer_chroma_extended_matrix_effect_base() → class=0x0F, cmd=0x02,
//     args[0]=variable_storage(0x01 VARSTORE), args[1]=led_id, args[2]=effect_id
//   静态  : dataSize=0x09, args[5]=0x01(颜色数), args[6..8]=RGB
//   光谱  : dataSize=0x06, args[2]=0x03
//   波浪  : dataSize=0x06, args[2]=0x04, args[3]=方向, args[4]=0x28 速度
//   响应  : dataSize=0x09, args[2]=0x05, args[4]=速度, args[5]=0x01, args[6..8]=RGB
//   呼吸  : dataSize=0x09, args[2]=0x02, args[3]=0x01, args[5]=0x01, args[6..8]=RGB
//   关闭  : dataSize=0x06, args[2]=0x00
//   亮度写: class=0x0F cmd=0x04 dataSize=0x03, args[0]=VARSTORE, args[1]=led_id, args[2]=亮度
//   滚轮模: class=0x02 cmd=0x14 dataSize=0x02, args[0]=VARSTORE, args[1]=模式
//   滚轮加速: class=0x02 cmd=0x16 dataSize=0x02, args[0]=VARSTORE, args[1]=0/1
//   智能滚轮: class=0x02 cmd=0x17 dataSize=0x02, args[0]=VARSTORE, args[1]=0/1
//   事务 ID: 全部使用 0x1F (openrazer 中 Basilisk V3 Pro 系列固定 0x1f)
//
// 关键实测结论（务必保留，均为本机 1532:00AB 抓包确认）:
//   1. 该鼠标对每一次写入返回状态字节 status = response[1]:
//        0x01 BUSY（已受理但设备正忙，稍后重试必然成功，实测 1~3 次后转为 0x02）
//        0x02 SUCCESSFUL
//        0x03 FAILURE / 0x04 TIMEOUT / 0x05 NOT_SUPPORTED
//      因此所有写操作必须带 BUSY 重试，否则会把「已生效但回 0x01」误判为失败。
//   2. 必须复用同一个已打开句柄。每次调用都重新 CreateFile 会让固件持续回 BUSY，
//      实测表现为「同一命令直连脚本成功、而 helper 连续调用失败」。
//   3. 回读类命令 (0x0F 0x84 / 0x02 0x94 / 0x96 / 0x97) 在本机固件上【不可用】:
//      应答载荷恒为上一次查询的陈旧报文缓存（例如残留 26 04 86 的 DPI 应答），
//      并非当前值。因此本类不提供 Query* 方法，只在本地缓存最后写入值 ——
//      宁可无读数，也不虚报一个假读数。
//   4. Chroma 通道为 91 字节 Feature Report，必须走 HidD_SetFeature / HidD_GetFeature；
//      该设备不存在 Interrupt 通道，切勿改用 WriteFile 下发。
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 修改命令字、args 布局或 LED ID 映射时必须同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止字符串插值、空条件运算符、nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Threading;

namespace RazerBatteryTray
{
    public static class RazerChromaHelper
    {
        private const int VendorId = 0x1532;
        private const int ReportLength = 91;
        private const byte TransactionId = 0x1F;
        private const byte ClassLighting = 0x0F;
        private const byte ClassMisc = 0x02;
        private const byte VarStore = 0x01;

        private const byte StatusPending = 0x00;
        private const byte StatusBusy = 0x01;
        private const byte StatusSuccessful = 0x02;

        private const int WriteRetryCount = 3;
        private const int MaxPollCount = 14;
        private const int PollIntervalMs = 26;
        private const int BusyRetryDelayMs = 60;
        private const int ResponseDelayMs = 34;

        // ---------- LED 物理分区 ID ----------
        // 本机 1532:00AB 逐个 LED ID 实测：仅 0x00 / 0x01 / 0x04 / 0x0A 回 SUCCESS，
        // 其余（0x02,0x03,0x05..0x09,0x0B..）一律回 0x03 FAILURE，即该型号不存在这些分区。
        public const byte LedWhole = 0x00;       // ZERO_LED：整只联动
        public const byte LedScrollWheel = 0x01; // SCROLL_WHEEL_LED：滚轮
        public const byte LedLogo = 0x04;        // LOGO_LED：三头蛇 Logo
        public const byte LedUnderglow = 0x0A;   // 底部环绕灯带（本型号实测为 0x0A，openrazer 的 BACKLIGHT_LED 0x05 不适用）

        // ---------- 灯效 ID ----------
        public const byte EffectOff = 0x00;
        public const byte EffectStatic = 0x01;
        public const byte EffectBreathing = 0x02;
        public const byte EffectSpectrum = 0x03;
        public const byte EffectWave = 0x04;
        public const byte EffectReactive = 0x05;

        // ---------- 滚轮物理机构 ----------
        /// <summary>滚轮模式：0 = 段落阻尼 (Tactile)，1 = 无阻尼自由滚动 (Free Spin)。</summary>
        public const int ScrollModeTactile = 0;
        public const int ScrollModeFreeSpin = 1;

        // ---------- 本地状态缓存（供 UI 与托盘菜单显示，不代表设备回读值） ----------
        public static byte LastLedId = LedWhole;
        public static byte LastEffect = 0xFF;
        public static Color LastColor = Color.White;
        public static int LastBrightness = -1;
        public static bool LastOperationSucceeded = false;
        public static string LastResponseHex = "";

        private static readonly object SyncRoot = new object();
        private static IntPtr cachedHandle = IntPtr.Zero;
        private static string cachedPath = null;

        /// <summary>该 PID 是否已实机验证支持 Chroma 扩展矩阵灯效。</summary>
        public static bool IsSupportedPid(int pid)
        {
            switch (pid)
            {
                case 0x00AA: // Basilisk V3 Pro (Wired)
                case 0x00AB: // Basilisk V3 Pro (Wireless / 2.4G 接收器)
                case 0x00CC: // Basilisk V3 Pro 35K (Wired)
                case 0x00CD: // Basilisk V3 Pro 35K (Wireless)
                case 0x00C4: // Basilisk V3 35K (Wired)
                case 0x00C5: // Basilisk V3 35K (Wireless)
                case 0x0099: // Basilisk V3
                    return true;
                default:
                    return false;
            }
        }

        /// <summary>按 DeviceId（形如 "Razer:1532:00AB"）判断是否支持。</summary>
        public static bool IsSupportedDeviceId(string deviceId)
        {
            if (string.IsNullOrEmpty(deviceId)) return false;
            int vid, pid;
            if (!TryParseVidPid(deviceId.Trim(), out vid, out pid)) return false;
            if (vid != VendorId) return false;
            return IsSupportedPid(pid);
        }

        private static bool TryParseVidPid(string deviceId, out int vid, out int pid)
        {
            vid = 0;
            pid = 0;
            int first = deviceId.IndexOf(':');
            if (first < 0) return false;
            int second = deviceId.IndexOf(':', first + 1);
            if (second < 0) return false;

            int end = deviceId.IndexOf(':', second + 1);
            string pidText = end > second ? deviceId.Substring(second + 1, end - second - 1) : deviceId.Substring(second + 1);
            string vidText = deviceId.Substring(first + 1, second - first - 1);

            int v, p;
            if (!int.TryParse(vidText, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out v)) return false;
            if (!int.TryParse(pidText, System.Globalization.NumberStyles.HexNumber, System.Globalization.CultureInfo.InvariantCulture, out p)) return false;
            vid = v;
            pid = p;
            return true;
        }

        // ---------- 句柄管理 ----------

        /// <summary>
        /// 枚举 91 字节 Feature Report 的鼠标控制集合并复用句柄。
        /// Basilisk V3 Pro 的控制集合 UsagePage=0x01 / Usage=0x02，FeatureReportLength=91。
        /// </summary>
        private static IntPtr AcquireHandle()
        {
            if (cachedHandle != IntPtr.Zero && HidTransport.IsValidHandle(cachedHandle)) return cachedHandle;

            cachedHandle = IntPtr.Zero;
            if (string.IsNullOrEmpty(cachedPath))
            {
                List<HidDeviceDescriptor> all = HidTransport.Enumerate(VendorId, 0);
                if (all == null) return IntPtr.Zero;
                for (int i = 0; i < all.Count; i++)
                {
                    HidDeviceDescriptor d = all[i];
                    if (d == null) continue;
                    if (d.FeatureReportLength != ReportLength) continue;
                    if (d.UsagePage != 0x01 || d.Usage != 0x02) continue;
                    if (d.DevicePath == null) continue;
                    if (d.DevicePath.IndexOf("\\kbd", StringComparison.OrdinalIgnoreCase) >= 0) continue;
                    cachedPath = d.DevicePath;
                    break;
                }
            }
            if (string.IsNullOrEmpty(cachedPath)) return IntPtr.Zero;

            IntPtr handle = HidTransport.OpenFeature(cachedPath);
            if (!HidTransport.IsValidHandle(handle))
            {
                cachedPath = null;
                return IntPtr.Zero;
            }
            cachedHandle = handle;
            return cachedHandle;
        }

        private static void ReleaseHandle()
        {
            if (cachedHandle != IntPtr.Zero)
            {
                try { HidTransport.Close(cachedHandle); }
                catch { }
                cachedHandle = IntPtr.Zero;
            }
            cachedPath = null;
        }

        /// <summary>清空缓存的端点与句柄（USB 热插拔、休眠唤醒后必须调用）。</summary>
        public static void InvalidateCache()
        {
            lock (SyncRoot)
            {
                ReleaseHandle();
            }
        }

        // ---------- 报文构造与收发 ----------

        private static byte ComputeCrc(byte[] report)
        {
            byte crc = 0;
            for (int i = 3; i < 88; i++) crc ^= report[i];
            return crc;
        }

        private static byte[] BuildReport(byte commandClass, byte commandId, byte[] args)
        {
            byte[] report = new byte[ReportLength];
            report[0] = 0x00;
            report[2] = TransactionId;
            int dataSize = args == null ? 0 : args.Length;
            report[6] = (byte)dataSize;
            report[7] = commandClass;
            report[8] = commandId;
            if (args != null && dataSize > 0)
            {
                int copy = dataSize;
                if (copy > 80) copy = 80;
                Buffer.BlockCopy(args, 0, report, 9, copy);
            }
            report[89] = ComputeCrc(report);
            return report;
        }

        /// <summary>
        /// 下发一条 Chroma 报文并等到最终应答。状态机（openrazer 注释 + 本机实测）：
        ///   0x00 New Command   —— 命令已收下但尚未处理完，【必须继续 GetFeature 轮询】；
        ///                         此时重复 SetFeature 只会把同一份工作再排一次队，永远轮不到执行；
        ///   0x01 Command Busy  —— 仍在处理，同样只轮询不重发；
        ///   0x02 Successful    —— 成功；
        ///   0x03 Failure / 0x04 Timeout / 0x05 Not Supported —— 确定失败，立即返回。
        /// 只有整轮轮询耗尽仍未出结果时，才重新 SetFeature，最多 WriteRetryCount 轮。
        /// </summary>
        private static bool SendRaw(byte commandClass, byte commandId, byte[] args)
        {
            // 与 DPI 读写共用同一把设备锁：Chroma 与 DPI 走同一条 91 字节 Feature 端点，
            // 并发会在响应队列里互相串包（实测会把 DPI 的 26 04 86 应答读成 Chroma 的成功）。
            lock (HardwareCommandBus.GetDeviceLock(VendorId, 0x00AB))
            {
                IntPtr handle = AcquireHandle();
                if (!HidTransport.IsValidHandle(handle)) return false;

                byte[] request = BuildReport(commandClass, commandId, args);
                for (int round = 0; round < WriteRetryCount; round++)
                {
                    if (!HidTransport.SetFeature(handle, request))
                    {
                        ReleaseHandle();
                        return false;
                    }

                    Thread.Sleep(ResponseDelayMs);

                    for (int poll = 0; poll < MaxPollCount; poll++)
                    {
                        byte[] response = new byte[ReportLength];
                        if (!HidTransport.GetFeature(handle, response))
                        {
                            ReleaseHandle();
                            return false;
                        }

                        LastResponseHex = ToHex(response, 16);

                        // 应答必须回显本次请求的 class/cmd，否则是别的并发命令（如 DPI 轮询）
                        // 留在队列里的陈旧应答，丢弃并继续轮询，不能当作本次结果。
                        if (response[7] != commandClass || response[8] != commandId)
                        {
                            Thread.Sleep(PollIntervalMs);
                            continue;
                        }

                        byte status = response[1];
                        if (status == StatusSuccessful) return true;
                        if (status == StatusPending || status == StatusBusy)
                        {
                            // 尚未处理完：继续轮询即可，切勿重发命令
                            Thread.Sleep(PollIntervalMs);
                            continue;
                        }
                        // 0x03 FAILURE / 0x04 TIMEOUT / 0x05 NOT_SUPPORTED：确定失败
                        return false;
                    }
                }
                return false;
            }
        }

        private static string ToHex(byte[] data, int count)
        {
            if (data == null) return "";
            int n = count < data.Length ? count : data.Length;
            System.Text.StringBuilder sb = new System.Text.StringBuilder(n * 3);
            for (int i = 0; i < n; i++)
            {
                if (i > 0) sb.Append(' ');
                sb.Append(data[i].ToString("X2"));
            }
            return sb.ToString();
        }

        private static bool SendEffect(byte[] args)
        {
            bool ok = SendRaw(ClassLighting, 0x02, args);
            LastOperationSucceeded = ok;
            return ok;
        }

        private static byte ClampSpeed(int value, int min, int max)
        {
            if (value < min) return (byte)min;
            if (value > max) return (byte)max;
            return (byte)value;
        }

        // ---------- 灯效：静态 ----------

        /// <summary>指定分区设为静态纯色。</summary>
        public static bool SetStatic(byte ledId, Color color)
        {
            byte[] args = new byte[] { VarStore, ledId, EffectStatic, 0x00, 0x00, 0x01, color.R, color.G, color.B };
            if (!SendEffect(args)) return false;
            LastLedId = ledId;
            LastEffect = EffectStatic;
            LastColor = color;
            return true;
        }

        /// <summary>整只鼠标设为静态纯色。</summary>
        public static bool SetAllStatic(Color color)
        {
            return SetStatic(LedWhole, color);
        }

        // ---------- 灯效：关闭 ----------

        public static bool SetOff(byte ledId)
        {
            byte[] args = new byte[] { VarStore, ledId, EffectOff, 0x00, 0x00, 0x00 };
            if (!SendEffect(args)) return false;
            LastLedId = ledId;
            LastEffect = EffectOff;
            return true;
        }

        public static bool SetAllOff()
        {
            return SetOff(LedWhole);
        }

        // ---------- 灯效：光谱 ----------

        public static bool SetSpectrum(byte ledId)
        {
            byte[] args = new byte[] { VarStore, ledId, EffectSpectrum, 0x00, 0x00, 0x00 };
            if (!SendEffect(args)) return false;
            LastLedId = ledId;
            LastEffect = EffectSpectrum;
            return true;
        }

        public static bool SetAllSpectrum()
        {
            return SetSpectrum(LedWhole);
        }

        // ---------- 灯效：波浪 ----------

        /// <summary>波浪方向：0 = 向左 / 1 = 向右。</summary>
        public static bool SetWave(byte ledId, int direction)
        {
            byte dir = (byte)(direction <= 0 ? 0x00 : (direction >= 2 ? 0x02 : 0x01));
            byte[] args = new byte[] { VarStore, ledId, EffectWave, dir, 0x28, 0x00 };
            if (!SendEffect(args)) return false;
            LastLedId = ledId;
            LastEffect = EffectWave;
            return true;
        }

        public static bool SetAllWave(int direction)
        {
            return SetWave(LedWhole, direction);
        }

        // ---------- 灯效：呼吸 ----------

        /// <summary>单色呼吸。速度与官方一致：0x01 最慢 ~ 0x04 最快。</summary>
        public static bool SetBreathing(byte ledId, Color color, int speed)
        {
            byte sp = ClampSpeed(speed, 1, 4);
            byte[] args = new byte[] { VarStore, ledId, EffectBreathing, 0x01, 0x00, sp, color.R, color.G, color.B };
            if (!SendEffect(args)) return false;
            LastLedId = ledId;
            LastEffect = EffectBreathing;
            LastColor = color;
            return true;
        }

        public static bool SetAllBreathing(Color color, int speed)
        {
            return SetBreathing(LedWhole, color, speed);
        }

        // ---------- 灯效：响应（按键触发后余辉） ----------

        public static bool SetReactive(byte ledId, Color color, int speed)
        {
            byte sp = ClampSpeed(speed, 1, 4);
            byte[] args = new byte[] { VarStore, ledId, EffectReactive, 0x00, sp, 0x01, color.R, color.G, color.B };
            if (!SendEffect(args)) return false;
            LastLedId = ledId;
            LastEffect = EffectReactive;
            LastColor = color;
            return true;
        }

        public static bool SetAllReactive(Color color, int speed)
        {
            return SetReactive(LedWhole, color, speed);
        }

        // ---------- 亮度 ----------

        /// <summary>设置分区亮度 (0~255)。</summary>
        public static bool SetBrightness(byte ledId, int value)
        {
            int b = value;
            if (b < 0) b = 0;
            if (b > 255) b = 255;
            byte[] args = new byte[] { VarStore, ledId, (byte)b };
            bool ok = SendRaw(ClassLighting, 0x04, args);
            LastOperationSucceeded = ok;
            if (ok) LastBrightness = b;
            return ok;
        }

        public static bool SetAllBrightness(int value)
        {
            return SetBrightness(LedWhole, value);
        }

        // ---------- 滚轮物理机构：模式 / 加速 / 智能滚轮 ----------

        /// <summary>
        /// 设置滚轮模式：ScrollModeTactile (段落阻尼) / ScrollModeFreeSpin (无阻尼自由滚动)。
        /// </summary>
        public static bool SetScrollMode(int mode)
        {
            int m = mode == ScrollModeFreeSpin ? ScrollModeFreeSpin : ScrollModeTactile;
            byte[] args = new byte[] { VarStore, (byte)m };
            bool ok = SendRaw(ClassMisc, 0x14, args);
            LastOperationSucceeded = ok;
            if (ok) LastScrollMode = m;
            return ok;
        }

        /// <summary>滚轮加速开关（对应官方 Synapse「滚轮加速」）。</summary>
        public static bool SetScrollAcceleration(bool enabled)
        {
            byte[] args = new byte[] { VarStore, (byte)(enabled ? 1 : 0) };
            bool ok = SendRaw(ClassMisc, 0x16, args);
            LastOperationSucceeded = ok;
            if (ok) LastScrollAcceleration = enabled;
            return ok;
        }

        /// <summary>
        /// 智能滚轮 Smart Reel 开关：随滚动速度在段落/自由模式间自动切换。
        /// </summary>
        public static bool SetSmartReel(bool enabled)
        {
            byte[] args = new byte[] { VarStore, (byte)(enabled ? 1 : 0) };
            bool ok = SendRaw(ClassMisc, 0x17, args);
            LastOperationSucceeded = ok;
            if (ok) LastSmartReel = enabled;
            return ok;
        }

        // ---------- 滚轮回读（0x94 / 0x96 / 0x97） ----------
        // 本机有线模式 1532:00AA 实测：写入 1 后回读为 1、写入 0 后回读为 0，载荷精确跟随，
        // 说明这三个读命令的应答字节可信。args[1] 即当前值。
        // 亮度回读 (0x84) 在有线模式下稳定回 0x03 FAILURE，故仍不提供亮度 Query。

        /// <summary>回读滚轮模式：0=Tactile 1=FreeSpin；读不到返回 -1。</summary>
        public static int QueryScrollMode()
        {
            int value;
            if (!TryReadMiscValue(0x94, out value)) return -1;
            LastScrollMode = value;
            return value;
        }

        /// <summary>回读滚轮加速开关：1=开 0=关；读不到返回 -1。</summary>
        public static int QueryScrollAcceleration()
        {
            int value;
            if (!TryReadMiscValue(0x96, out value)) return -1;
            LastScrollAcceleration = value != 0;
            return value;
        }

        /// <summary>回读智能滚轮开关：1=开 0=关；读不到返回 -1。</summary>
        public static int QuerySmartReel()
        {
            int value;
            if (!TryReadMiscValue(0x97, out value)) return -1;
            LastSmartReel = value != 0;
            return value;
        }

        /// <summary>
        /// 通用 misc 读命令：下发 VARSTORE + 占位字节，返回应答 args[1]（即 response[10]）。
        /// 必须回显校验 class=0x02 且 cmd 匹配，否则是被并发的 DPI 轮询串包。
        /// </summary>
        private static bool TryReadMiscValue(byte commandId, out int value)
        {
            value = -1;
            byte[] response;
            if (!SendRawForResponse(ClassMisc, commandId, new byte[] { VarStore, 0x00 }, out response)) return false;
            if (response == null || response.Length < 11) return false;
            value = response[10];
            return true;
        }

        /// <summary>下发命令并返回已完成回显校验的应答帧。</summary>
        private static bool SendRawForResponse(byte commandClass, byte commandId, byte[] args, out byte[] response)
        {
            response = null;
            lock (HardwareCommandBus.GetDeviceLock(VendorId, 0x00AB))
            {
                IntPtr handle = AcquireHandle();
                if (!HidTransport.IsValidHandle(handle)) return false;

                byte[] request = BuildReport(commandClass, commandId, args);
                for (int round = 0; round < WriteRetryCount; round++)
                {
                    if (!HidTransport.SetFeature(handle, request))
                    {
                        ReleaseHandle();
                        return false;
                    }

                    Thread.Sleep(ResponseDelayMs);

                    for (int poll = 0; poll < MaxPollCount; poll++)
                    {
                        byte[] frame = new byte[ReportLength];
                        if (!HidTransport.GetFeature(handle, frame))
                        {
                            ReleaseHandle();
                            return false;
                        }

                        LastResponseHex = ToHex(frame, 16);
                        if (frame[7] != commandClass || frame[8] != commandId)
                        {
                            Thread.Sleep(PollIntervalMs);
                            continue;
                        }

                        byte status = frame[1];
                        if (status == StatusSuccessful)
                        {
                            response = frame;
                            return true;
                        }
                        if (status == StatusPending || status == StatusBusy)
                        {
                            Thread.Sleep(PollIntervalMs);
                            continue;
                        }
                        return false;
                    }
                }
                return false;
            }
        }

        // ---------- 本地状态（写入成功或回读成功后缓存，供 UI 显示当前选择） ----------
        public static int LastScrollMode = -1;
        public static bool LastScrollAcceleration = false;
        public static bool LastSmartReel = false;

        /// <summary>一次性同步三个滚轮开关到设备真实值；任一失败返回 false。</summary>
        public static bool SyncScrollState()
        {
            bool okMode = QueryScrollMode() >= 0;
            bool okAccel = QueryScrollAcceleration() >= 0;
            bool okSmart = QuerySmartReel() >= 0;
            return okMode && okAccel && okSmart;
        }
    }
}
