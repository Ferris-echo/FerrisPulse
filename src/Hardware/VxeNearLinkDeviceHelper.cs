// ============================================================================
// 模块名称: VxeNearLinkDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   VXE 蜻蜓 R1 星闪版 (NearLink) 接收器 (373B:1216) 与有线模式 (373B:124C) 完整驱动：
//   通过 UsagePage 0xFF05 / Usage 0x0001 (In=64B / Out=64B) 厂商端点收发 BITMOUSE
//   63 字节帧 (Report ID 0x08)，支持：
//     - 单读通道会话复用 + 0x73 物理按键切档实时监听（消除 Windows HID 回包抢占）；
//     - cmd 0x07 电量/电压读取与 cmd 0x0F 充电检测 + DriverStateStore 休眠缓存；
//     - 373B:124C 有线供电态快速短路识别（消除插线态对静音接收器的长阻塞超时）；
//     - cmd 0x17 连续 6×10B 流拼包读取 DPI 表（保留每档 [B,G,R,A] 灯色）；
//     - cmd 0x02 单档/多档 XY DPI 写入（含 ACK 校验与原色保真）；
//     - cmd 0x01 降序回报率切换 (125->3 / 250->2 / 500->1 / 1000->0 Hz)；
//     - cmd 0x17(key 0x4A) / cmd 0x1F 读写「ATK 绝鲨竞技固件 / 基础模式」。
//   （特别鸣谢社区群友“冻干咸鱼”逆向 ATK HUB 3.2.30~3.2.34 并贡献完整协议与抓包实证）
// 依赖关系: HidTransport.cs, DriverStateStore.cs, HardwareDriverRegistry.cs,
//           HardwareCommandBus.cs, MouseBatteryInfo.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 修改命令字、帧布局或端点约束时必须同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止 $"..." 插值、禁止 ?. 空条件运算符、禁止 nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

namespace RazerBatteryTray
{
    public static class VxeNearLinkDeviceHelper
    {
        private static readonly object hidLock = new object();
        private static int cmdSn = 0x35;
        private static DateTime lastQuery = DateTime.MinValue;
        private static readonly List<MouseBatteryInfo> cachedList = new List<MouseBatteryInfo>();

        public const int VendorId = 0x373B;
        public const int ProductId = 0x1216;
        public const int WiredProductId = 0x124C;
        public const int TargetUsagePage = 0xFF05;
        public const int TargetUsage = 0x0001;
        public const int ReportLength = 64;
        public const int PayloadLength = 63;

        public const byte FrameReportId = 0x08;
        public const byte FrameMagic = 0x72;
        public const byte TargetMouse = 0x01;
        public const byte CmdSetPollingRate = 0x01;
        public const byte CmdSetDpi = 0x02;
        public const byte CmdGetBatteryLevel = 0x07;
        public const byte CmdGetBatteryChargingStatus = 0x0F;
        public const byte CmdTableRead = 0x17;
        public const byte CmdSensorMode = 0x1F;
        public const byte KeySensorMode = 0x4A;
        public const byte EventDpiSwitchMagic = 0x73;
        public const byte SensorModeBasic = 0x00;
        public const byte SensorModeCompetitive = 0x04;

        private const int ParamLenSwitch = 0x03;
        private const int CmdLenSwitch = 0x01;
        private const int ParamLenDpi = 0x0C;
        private const int CmdLenDpi = 0x0A;
        private const int ParamLenRead = 0x0A;
        private const int CmdLenRead = 0x0D;

        public const int DpiMinAllowed = 100;
        public const int DpiMaxAllowed = 32000;
        public const int MaxStageProbe = 6;
        public const string ActionIdCompetitiveMode = "competitive_mode";

        public static readonly int[] SupportedPollingRates = new int[] { 125, 250, 500, 1000 };
        private static readonly int[] RateCodeList = new int[] { 3, 2, 1, 0 };

        public const string DeviceIdName = "VXE:373B:1216";
        public const string FingerprintName = "HID:373B:1216";
        public const string ModelName = "VXE Dragonfly R1 星闪版 (NearLink)";
        public const string DriverIdName = "BuiltIn:VxeNearLink";

        private const int WriteTimeoutMs = 400;
        private const int AckTimeoutMs = 700;
        private const int ReadTimeoutMs = 700;
        private const int BatteryTimeoutMs = 600;
        private const int WiredProbeTimeoutMs = 400;
        private const int SessionReadTimeoutMs = 5000;
        private const int QueryThrottleMs = 5000;
        private const int MaxWriteAttempts = 3;

        // 单读通道会话与路径热切换状态
        private static readonly object sessionLock = new object();
        private static readonly object cmdLock = new object();
        private static readonly object replyLock = new object();
        private static readonly ManualResetEvent replyEvent = new ManualResetEvent(false);
        private static IntPtr sessionHandle = IntPtr.Zero;
        private static string sessionPath = "";
        private static bool sessionThreadRunning = false;
        private static bool sessionWriteFailed = false;
        private static int sessionGeneration = 0;
        private static byte[] replyBuffer = null;
        private static int replyCmd = -1;

        // 活跃路径与硬件状态缓存
        private static readonly object stateLock = new object();
        private static string activeDonglePath = "";
        private static string activeWiredPath = "";
        private static int[] cachedDpiX = new int[0];
        private static int[] cachedDpiY = new int[0];
        private static int cachedStageCount = 0;
        private static bool tableCacheValid = false;
        private static byte[] cachedColorsBgra = new byte[MaxStageProbe * 4];
        private static bool colorCacheValid = false;
        private static int activeStageHint = -1;
        private static int lastStageIdx = -1;
        private static int activeDpiX = 0;
        private static int activeDpiY = 0;
        private static int lastPollingRate = 1000;
        private static bool competitiveModeState = false;
        private static DateTime lastTableRefreshUtc = DateTime.MinValue;
        private static DateTime localTrustUntilUtc = DateTime.MinValue;

        public static bool IsSupportedVidPid(int vid, int pid)
        {
            return vid == VendorId && (pid == ProductId || pid == WiredProductId);
        }

        public static bool IsNearLinkEndpoint(HidDeviceDescriptor device)
        {
            if (device == null) return false;
            if (!IsSupportedVidPid(device.VendorId, device.ProductId)) return false;
            if (device.UsagePage != TargetUsagePage || device.Usage != TargetUsage) return false;
            if (device.InputReportLength != ReportLength || device.OutputReportLength != ReportLength) return false;
            if (!string.IsNullOrEmpty(device.DevicePath) &&
                device.DevicePath.EndsWith("\\kbd", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }
            return true;
        }

        /// <summary>
        /// 构建 64B BITMOUSE 输出报告：
        /// [0]=ReportID(0x08), [1]=checksum(sum([2..63])&0xFF), [2]=0x72, [3]=2, [4]=sn, [5]=target, [6]=cmdId, [7]=1。
        /// </summary>
        public static byte[] BuildReport64(int cmdId, int target, int sn)
        {
            byte[] outBuf = new byte[ReportLength];
            outBuf[0] = FrameReportId;
            outBuf[2] = FrameMagic;
            outBuf[3] = 2;
            outBuf[4] = (byte)(sn & 0xFF);
            outBuf[5] = (byte)(target & 0xFF);
            outBuf[6] = (byte)(cmdId & 0xFF);
            outBuf[7] = 1;

            ApplyChecksum(outBuf);
            return outBuf;
        }

        /// <summary>
        /// 构建完整参数版 64B BITMOUSE 输出报告：
        /// [0]=0x08, [1]=sum([2..63])&0xFF, [2]=0x72, [3]=paramLen, [4]=sn, [5]=target(0x01), [6]=cmdId, [7]=cmdLen, [8..]=body。
        /// </summary>
        public static byte[] BuildFrame64(int cmdId, int paramLen, int cmdLen, byte[] body, int sn)
        {
            byte[] outBuf = new byte[ReportLength];
            outBuf[0] = FrameReportId;
            outBuf[2] = FrameMagic;
            outBuf[3] = (byte)(paramLen & 0xFF);
            outBuf[4] = (byte)(sn & 0xFF);
            outBuf[5] = TargetMouse;
            outBuf[6] = (byte)(cmdId & 0xFF);
            outBuf[7] = (byte)(cmdLen & 0xFF);

            if (body != null)
            {
                for (int i = 0; i < body.Length && (8 + i) < ReportLength; i++)
                {
                    outBuf[8 + i] = body[i];
                }
            }

            ApplyChecksum(outBuf);
            return outBuf;
        }

        public static byte[] BuildDpiWriteFrame64(int stageZeroBased, int dpiX, int dpiY, byte[] colorBgra)
        {
            if (stageZeroBased < 0 || stageZeroBased >= MaxStageProbe) return null;
            if (dpiX < DpiMinAllowed || dpiX > DpiMaxAllowed) return null;
            if (dpiY < DpiMinAllowed || dpiY > DpiMaxAllowed) return null;
            if (colorBgra == null || colorBgra.Length < 4) return null;

            byte[] body = new byte[10];
            body[0] = (byte)(stageZeroBased & 0xFF);
            body[1] = (byte)(dpiX & 0xFF);
            body[2] = (byte)((dpiX >> 8) & 0xFF);
            body[3] = (byte)(dpiY & 0xFF);
            body[4] = (byte)((dpiY >> 8) & 0xFF);
            body[5] = colorBgra[0];
            body[6] = colorBgra[1];
            body[7] = colorBgra[2];
            body[8] = colorBgra[3];
            body[9] = 0x01;
            return BuildFrame64(CmdSetDpi, ParamLenDpi, CmdLenDpi, body, 0);
        }

        public static int FindPollingRateCode(int rateHz)
        {
            for (int i = 0; i < SupportedPollingRates.Length; i++)
            {
                if (SupportedPollingRates[i] == rateHz) return RateCodeList[i];
            }
            return -1;
        }

        public static byte[] BuildPollingRateFrame64(int rateHz)
        {
            int code = FindPollingRateCode(rateHz);
            if (code < 0) return null;
            byte[] body = new byte[1];
            body[0] = (byte)(code & 0xFF);
            return BuildFrame64(CmdSetPollingRate, ParamLenSwitch, CmdLenSwitch, body, 0);
        }

        public static byte[] BuildSensorModeWriteFrame64(bool competitive)
        {
            byte[] body = new byte[1];
            body[0] = competitive ? SensorModeCompetitive : SensorModeBasic;
            return BuildFrame64(CmdSensorMode, ParamLenSwitch, CmdLenSwitch, body, 0);
        }

        public static byte[] BuildTableReadFrame64(int seq)
        {
            byte[] body = new byte[3];
            body[0] = (byte)(seq & 0xFF);
            body[1] = 0x00;
            body[2] = 0x0A;
            return BuildFrame64(CmdTableRead, ParamLenRead, CmdLenRead, body, 0);
        }

        public static byte[] BuildKeyReadFrame64(int key, int len)
        {
            byte[] body = new byte[3];
            body[0] = (byte)(key & 0xFF);
            body[1] = 0x00;
            body[2] = (byte)(len & 0xFF);
            return BuildFrame64(CmdTableRead, ParamLenRead, CmdLenRead, body, 0);
        }

        private static void ApplyChecksum(byte[] report)
        {
            if (report == null || report.Length < ReportLength) return;
            int sum = 0;
            for (int i = 2; i < ReportLength; i++)
            {
                sum += report[i];
            }
            report[1] = (byte)(sum & 0xFF);
        }

        public static bool ParseBatteryResponse(byte[] rx, int length, out int batteryPercent, out int voltageMv)
        {
            batteryPercent = -1;
            voltageMv = 0;
            if (rx == null || length < 10 || rx.Length < 10) return false;
            if (rx[1] != FrameMagic || rx[5] != CmdGetBatteryLevel) return false;

            int pct = rx[7];
            if (pct < 0 || pct > 100) return false;

            batteryPercent = pct;
            voltageMv = (rx[9] << 8) | rx[8];
            return true;
        }

        public static bool ParseChargingResponse(byte[] rx, int length, out bool isCharging)
        {
            isCharging = false;
            if (rx == null || length < 8 || rx.Length < 8) return false;
            if (rx[1] != FrameMagic || rx[5] != CmdGetBatteryChargingStatus) return false;

            isCharging = (rx[7] != 0);
            return true;
        }

        public static bool ParseSensorModeResponse(byte[] rx, int length, out bool competitive)
        {
            competitive = false;
            if (rx == null || length < 11 || rx.Length < 11) return false;
            if (rx[1] != FrameMagic || rx[5] != CmdTableRead || rx[7] != KeySensorMode) return false;
            if (rx[9] == 0) return false;

            competitive = (rx[10] != 0);
            return true;
        }

        public static bool ParseDpiSwitchEventFrame(byte[] frame, int length, out int dpiX, out int dpiY)
        {
            dpiX = 0;
            dpiY = 0;
            if (frame == null || length < 10 || frame.Length < 10) return false;
            if (frame[0] != FrameReportId || frame[2] != EventDpiSwitchMagic) return false;

            int x = frame[6] | (frame[7] << 8);
            int y = frame[8] | (frame[9] << 8);
            if (x < DpiMinAllowed || y < DpiMinAllowed || x > 100000 || y > 100000) return false;

            dpiX = x;
            dpiY = y;
            return true;
        }

        public static bool ParseDpiTableStream(byte[] stream, int length, out int[] xs, out int[] ys, out byte[] colorsBgra, out int parsedStageHint)
        {
            xs = new int[0];
            ys = new int[0];
            colorsBgra = new byte[0];
            parsedStageHint = -1;

            if (stream == null || length < 10 || stream.Length < length) return false;
            if (stream[1] != 0x08) return false;

            int[] tmpX = new int[MaxStageProbe];
            int[] tmpY = new int[MaxStageProbe];
            byte[] tmpC = new byte[MaxStageProbe * 4];
            int found = 0;

            for (int k = 0; k < MaxStageProbe; k++)
            {
                int idx = 2 + 8 * k;
                if (idx + 7 >= length) break;
                int x = stream[idx] | (stream[idx + 1] << 8);
                int y = stream[idx + 2] | (stream[idx + 3] << 8);
                if (x == 0 || x == 0xFF || x == 0xFFFF) break;
                if (x < DpiMinAllowed || x > DpiMaxAllowed || y < DpiMinAllowed || y > DpiMaxAllowed)
                {
                    return false;
                }

                tmpX[found] = x;
                tmpY[found] = y;
                tmpC[found * 4] = stream[idx + 4];
                tmpC[found * 4 + 1] = stream[idx + 5];
                tmpC[found * 4 + 2] = stream[idx + 6];
                tmpC[found * 4 + 3] = stream[idx + 7];
                found++;
            }

            if (found <= 0) return false;

            xs = new int[found];
            ys = new int[found];
            colorsBgra = new byte[found * 4];
            for (int i = 0; i < found; i++)
            {
                xs[i] = tmpX[i];
                ys[i] = tmpY[i];
            }
            Array.Copy(tmpC, 0, colorsBgra, 0, found * 4);
            parsedStageHint = (stream[0] < found) ? (int)stream[0] : -1;
            return true;
        }

        public static int ResolveHintIndex(int[] xs, int count, int hint, int eventDpiX)
        {
            if (xs == null || count <= 0 || hint < 0) return -1;
            int limit = Math.Min(count, xs.Length);
            if (eventDpiX <= 0) return (hint < limit) ? hint : -1;
            if (hint < limit && xs[hint] == eventDpiX) return hint;
            if (hint - 1 >= 0 && hint - 1 < limit && xs[hint - 1] == eventDpiX) return hint - 1;
            return -1;
        }

        public static void PopulateCapabilities(MouseBatteryInfo dev)
        {
            if (dev == null) return;
            if (dev.Capabilities == null) dev.Capabilities = new DeviceCapabilities();
            if (string.IsNullOrEmpty(dev.DriverId)) dev.DriverId = DriverIdName;

            DeviceCapabilities cap = dev.Capabilities;
            cap.SupportsBattery = true;
            cap.SupportsDpi = true;
            cap.SupportsDpiStages = true;
            cap.SupportsXyIndependence = true;
            cap.SupportsPollingRate = true;
            cap.SupportsHardwareDpiMonitor = true;
            cap.MinDpi = DpiMinAllowed;
            cap.MaxDpi = DpiMaxAllowed;
            cap.DpiStep = 1;
            cap.SupportedPollingRates = SupportedPollingRates;
            cap.SensorDescription = "NK 星闪 · BITMOUSE 私有协议 · 4 档板载 DPI";
            cap.PerformanceCardTitle = "星闪链路";

            bool compMode;
            int polRate;
            lock (stateLock)
            {
                compMode = competitiveModeState;
                polRate = lastPollingRate;
            }

            dev.SupportsDpiWrite = true;
            dev.SupportsPollingRateWrite = true;
            dev.SupportedPollingRates = SupportedPollingRates;
            dev.MaxPollingRate = 1000;
            if (dev.PollingRate <= 0) dev.PollingRate = polRate;
            dev.IsXyIndependent = true;

            CustomDeviceAction found = null;
            for (int i = 0; i < cap.CustomActions.Count; i++)
            {
                CustomDeviceAction a = cap.CustomActions[i];
                if (a != null && string.Equals(a.ActionId, ActionIdCompetitiveMode, StringComparison.OrdinalIgnoreCase))
                {
                    found = a;
                    break;
                }
            }
            if (found == null)
            {
                found = new CustomDeviceAction();
                found.ActionId = ActionIdCompetitiveMode;
                found.Title = "绝鲨竞技固件";
                found.ActionType = CustomActionType.Toggle;
                found.ActiveText = "ATK绝鲨竞技固件";
                found.InactiveText = "基础模式";
                found.ShowInTrayMenu = true;
                cap.CustomActions.Add(found);
            }
            found.SyncWithGameOptimization = true;
            found.BoolValue = compMode;
            found.IntValue = compMode ? 1 : 0;
        }

        // ------------------------------------------------------------------
        // 单读通道会话：一条读线程收 0x72 命令回包 & 0x73 物理切档事件
        // ------------------------------------------------------------------
        private static void EnsureSession(string path)
        {
            if (string.IsNullOrEmpty(path)) return;
            bool needStart = false;
            IntPtr staleHandle = IntPtr.Zero;

            lock (sessionLock)
            {
                if (!string.Equals(sessionPath, path, StringComparison.OrdinalIgnoreCase))
                {
                    sessionPath = path;
                    sessionWriteFailed = false;
                    sessionGeneration++;
                    if (HidTransport.IsValidHandle(sessionHandle))
                    {
                        staleHandle = sessionHandle;
                        sessionHandle = IntPtr.Zero;
                    }
                }
                if (!sessionThreadRunning)
                {
                    sessionThreadRunning = true;
                    needStart = true;
                }
            }

            if (HidTransport.IsValidHandle(staleHandle))
            {
                HidTransport.Close(staleHandle);
            }

            if (needStart)
            {
                Thread t = new Thread(SessionLoop);
                t.IsBackground = true;
                t.Name = "VxeNearLinkSession";
                t.Start();
            }
        }

        private static void SessionLoop()
        {
            int openFails = 0;
            int ioFastFails = 0;

            try
            {
                while (true)
                {
                    IntPtr curHandle;
                    string curPath;
                    int curGen;

                    lock (sessionLock)
                    {
                        curHandle = sessionHandle;
                        curPath = sessionPath;
                        curGen = sessionGeneration;
                    }

                    if (string.IsNullOrEmpty(curPath))
                    {
                        lock (sessionLock) { sessionThreadRunning = false; }
                        return;
                    }

                    if (!HidTransport.IsValidHandle(curHandle))
                    {
                        IntPtr h = HidTransport.Open(curPath);
                        if (!HidTransport.IsValidHandle(h))
                        {
                            openFails++;
                            if (openFails >= 5)
                            {
                                lock (sessionLock)
                                {
                                    if (sessionGeneration == curGen)
                                    {
                                        sessionPath = "";
                                        sessionThreadRunning = false;
                                        return;
                                    }
                                }
                                openFails = 0;
                            }
                            Thread.Sleep(300);
                            continue;
                        }

                        bool adopted = false;
                        lock (sessionLock)
                        {
                            if (sessionGeneration == curGen &&
                                string.Equals(sessionPath, curPath, StringComparison.OrdinalIgnoreCase) &&
                                !HidTransport.IsValidHandle(sessionHandle))
                            {
                                sessionHandle = h;
                                sessionWriteFailed = false;
                                adopted = true;
                            }
                        }
                        if (!adopted)
                        {
                            HidTransport.Close(h);
                        }
                        openFails = 0;
                        ioFastFails = 0;
                        continue;
                    }

                    Stopwatch ioWatch = Stopwatch.StartNew();
                    byte[] data;
                    if (HidTransport.ReadReportExact(curHandle, ReportLength, SessionReadTimeoutMs, out data))
                    {
                        openFails = 0;
                        ioFastFails = 0;
                        DispatchFrame(data);
                        continue;
                    }

                    long elapsedMs = ioWatch.ElapsedMilliseconds;
                    bool shouldResetHandle = false;
                    lock (sessionLock)
                    {
                        if (sessionGeneration != curGen ||
                            !string.Equals(sessionPath, curPath, StringComparison.OrdinalIgnoreCase) ||
                            sessionWriteFailed)
                        {
                            shouldResetHandle = true;
                        }
                        else if (elapsedMs < 200)
                        {
                            ioFastFails++;
                            if (ioFastFails >= 2)
                            {
                                shouldResetHandle = true;
                            }
                        }
                        else
                        {
                            ioFastFails = 0;
                        }

                        if (shouldResetHandle && sessionHandle == curHandle)
                        {
                            sessionHandle = IntPtr.Zero;
                            sessionWriteFailed = false;
                        }
                        else
                        {
                            curHandle = IntPtr.Zero;
                        }
                    }

                    if (shouldResetHandle && HidTransport.IsValidHandle(curHandle))
                    {
                        HidTransport.Close(curHandle);
                        ioFastFails = 0;
                        Thread.Sleep(50);
                    }
                }
            }
            catch
            {
                lock (sessionLock)
                {
                    if (HidTransport.IsValidHandle(sessionHandle))
                    {
                        HidTransport.Close(sessionHandle);
                        sessionHandle = IntPtr.Zero;
                    }
                    sessionThreadRunning = false;
                }
            }
        }

        private static void DispatchFrame(byte[] frame)
        {
            if (frame == null || frame.Length < 6 || frame[0] != FrameReportId) return;

            if (frame[1] == FrameMagic)
            {
                lock (replyLock)
                {
                    if (replyCmd >= 0 && frame[5] == (byte)replyCmd)
                    {
                        replyBuffer = frame;
                        replyEvent.Set();
                    }
                }
                return;
            }

            int dpiX, dpiY;
            if (ParseDpiSwitchEventFrame(frame, frame.Length, out dpiX, out dpiY))
            {
                lock (stateLock)
                {
                    activeDpiX = dpiX;
                    activeDpiY = dpiY;
                }
            }
        }

        private static bool SessionSend(string path, byte[] report64, int expectedCmdId, int timeoutMs, out byte[] rx)
        {
            rx = null;
            if (string.IsNullOrEmpty(path) || report64 == null || report64.Length != ReportLength) return false;

            EnsureSession(path);

            lock (cmdLock)
            {
                Stopwatch waitOpen = Stopwatch.StartNew();
                IntPtr h = IntPtr.Zero;
                while (waitOpen.ElapsedMilliseconds < 2500)
                {
                    lock (sessionLock)
                    {
                        if (string.Equals(sessionPath, path, StringComparison.OrdinalIgnoreCase) &&
                            HidTransport.IsValidHandle(sessionHandle))
                        {
                            h = sessionHandle;
                            break;
                        }
                    }
                    Thread.Sleep(30);
                }
                if (!HidTransport.IsValidHandle(h)) return false;

                lock (replyLock)
                {
                    replyCmd = expectedCmdId;
                    replyBuffer = null;
                    replyEvent.Reset();
                }

                bool wrote = HidTransport.WriteReport(h, report64, ReportLength, WriteTimeoutMs);
                if (!wrote)
                {
                    lock (replyLock)
                    {
                        replyCmd = -1;
                        replyBuffer = null;
                    }
                    lock (sessionLock)
                    {
                        if (sessionHandle == h) sessionWriteFailed = true;
                    }
                    return false;
                }

                bool signaled = replyEvent.WaitOne(timeoutMs);
                lock (replyLock)
                {
                    rx = replyBuffer;
                    replyBuffer = null;
                    replyCmd = -1;
                }
                return signaled && rx != null;
            }
        }

        private static bool DirectQueryBattery(string devicePath, out int batteryPercent, out bool isCharging, out int voltageMv)
        {
            batteryPercent = -1;
            isCharging = false;
            voltageMv = 0;

            IntPtr handle = HidTransport.Open(devicePath);
            if (!HidTransport.IsValidHandle(handle)) return false;
            try
            {
                int sn = Interlocked.Increment(ref cmdSn) & 0xFF;
                byte[] tx = BuildReport64(CmdGetBatteryLevel, TargetMouse, sn);
                if (!HidTransport.WriteReport(handle, tx, ReportLength, WriteTimeoutMs)) return false;

                Stopwatch sw = Stopwatch.StartNew();
                while (sw.ElapsedMilliseconds < WiredProbeTimeoutMs)
                {
                    int remain = Math.Max(1, WiredProbeTimeoutMs - (int)sw.ElapsedMilliseconds);
                    byte[] buf;
                    if (!HidTransport.ReadReportExact(handle, ReportLength, Math.Min(remain, 200), out buf)) break;
                    if (ParseBatteryResponse(buf, buf.Length, out batteryPercent, out voltageMv))
                    {
                        int snChg = Interlocked.Increment(ref cmdSn) & 0xFF;
                        byte[] txChg = BuildReport64(CmdGetBatteryChargingStatus, TargetMouse, snChg);
                        if (HidTransport.WriteReport(handle, txChg, ReportLength, WriteTimeoutMs))
                        {
                            Stopwatch chgWatch = Stopwatch.StartNew();
                            while (chgWatch.ElapsedMilliseconds < WiredProbeTimeoutMs)
                            {
                                int chgRem = Math.Max(1, WiredProbeTimeoutMs - (int)chgWatch.ElapsedMilliseconds);
                                byte[] rxChg;
                                if (!HidTransport.ReadReportExact(handle, ReportLength, Math.Min(chgRem, 200), out rxChg)) break;
                                if (ParseChargingResponse(rxChg, rxChg.Length, out isCharging)) break;
                            }
                        }
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
                HidTransport.Close(handle);
            }
        }

        private static bool QueryBatteryViaSession(string devicePath, out int batteryPercent, out bool isCharging, out int voltageMv)
        {
            batteryPercent = -1;
            isCharging = false;
            voltageMv = 0;
            if (string.IsNullOrEmpty(devicePath)) return false;

            byte[] rxBat = null;
            bool gotBat = false;
            for (int attempt = 0; attempt < 2; attempt++)
            {
                int sn = Interlocked.Increment(ref cmdSn) & 0xFF;
                byte[] tx = BuildReport64(CmdGetBatteryLevel, TargetMouse, sn);
                byte[] r;
                if (SessionSend(devicePath, tx, CmdGetBatteryLevel, BatteryTimeoutMs, out r) &&
                    ParseBatteryResponse(r, r.Length, out batteryPercent, out voltageMv))
                {
                    rxBat = r;
                    gotBat = true;
                    break;
                }
                if (attempt == 0) Thread.Sleep(80);
            }
            if (!gotBat || rxBat == null) return false;

            for (int attempt = 0; attempt < 2; attempt++)
            {
                int snChg = Interlocked.Increment(ref cmdSn) & 0xFF;
                byte[] txChg = BuildReport64(CmdGetBatteryChargingStatus, TargetMouse, snChg);
                byte[] rxChg;
                if (SessionSend(devicePath, txChg, CmdGetBatteryChargingStatus, BatteryTimeoutMs, out rxChg) &&
                    ParseChargingResponse(rxChg, rxChg.Length, out isCharging))
                {
                    break;
                }
                if (attempt == 0) Thread.Sleep(80);
            }

            return true;
        }

        private static bool TryReadSensorMode(string path, out bool competitive)
        {
            competitive = false;
            if (string.IsNullOrEmpty(path)) return false;
            byte[] tx = BuildKeyReadFrame64(KeySensorMode, 1);
            byte[] rx;
            if (!SessionSend(path, tx, CmdTableRead, AckTimeoutMs, out rx)) return false;
            return ParseSensorModeResponse(rx, rx != null ? rx.Length : 0, out competitive);
        }

        private static bool ReadTableOnce(string path, out int[] xs, out int[] ys, out byte[] colorsBgra, out int count, out int hint)
        {
            xs = new int[0];
            ys = new int[0];
            colorsBgra = new byte[0];
            count = 0;
            hint = -1;
            if (string.IsNullOrEmpty(path)) return false;

            const int ChunkCount = 6;
            const int ChunkData = 10;
            byte[] stream = new byte[ChunkCount * ChunkData];
            int filled = 0;

            for (int n = 0; n < ChunkCount; n++)
            {
                int seq = 0x01 + 0x0A * n;
                byte[] tx = BuildTableReadFrame64(seq);
                byte[] frame = null;
                bool ok = false;
                for (int attempt = 0; attempt < 2; attempt++)
                {
                    byte[] r;
                    if (SessionSend(path, tx, CmdTableRead, ReadTimeoutMs, out r) &&
                        r != null && r.Length >= ReportLength && r[7] == (byte)seq)
                    {
                        frame = r;
                        ok = true;
                        break;
                    }
                    if (attempt == 0) Thread.Sleep(50);
                }
                if (!ok || frame == null || frame.Length < 10 + ChunkData) break;

                for (int j = 0; j < ChunkData; j++)
                {
                    stream[n * ChunkData + j] = frame[10 + j];
                }
                filled = (n + 1) * ChunkData;
            }

            if (filled < ChunkCount * ChunkData) return false;
            if (!ParseDpiTableStream(stream, filled, out xs, out ys, out colorsBgra, out hint)) return false;
            count = xs.Length;
            return count > 0;
        }

        private static bool ReadAndCacheTable(string path, int maxPasses)
        {
            for (int pass = 0; pass < maxPasses; pass++)
            {
                int[] xs;
                int[] ys;
                byte[] cols;
                int cnt;
                int hint;
                if (ReadTableOnce(path, out xs, out ys, out cols, out cnt, out hint) && cnt > 0)
                {
                    lock (stateLock)
                    {
                        cachedDpiX = xs;
                        cachedDpiY = ys;
                        cachedStageCount = cnt;
                        tableCacheValid = true;
                        cachedColorsBgra = cols;
                        colorCacheValid = (cols != null && cols.Length >= cnt * 4);
                        if (hint >= 0) activeStageHint = hint;
                        lastTableRefreshUtc = DateTime.UtcNow;
                    }
                    return true;
                }
                if (pass + 1 < maxPasses) Thread.Sleep(100);
            }
            return false;
        }

        private static void RefreshTableIfDue(string path, int intervalMs)
        {
            DateTime lastUtc;
            lock (stateLock) { lastUtc = lastTableRefreshUtc; }
            if (lastUtc != DateTime.MinValue && (DateTime.UtcNow - lastUtc).TotalMilliseconds < intervalMs) return;

            lock (stateLock) { lastTableRefreshUtc = DateTime.UtcNow; }
            ReadAndCacheTable(path, 1);
        }

        private static byte[] GetStageColorBgra(int stageZeroBased)
        {
            lock (stateLock)
            {
                if (!colorCacheValid || cachedColorsBgra == null || stageZeroBased < 0) return null;
                int offset = stageZeroBased * 4;
                if (offset + 3 >= cachedColorsBgra.Length) return null;
                byte[] c = new byte[4];
                c[0] = cachedColorsBgra[offset];
                c[1] = cachedColorsBgra[offset + 1];
                c[2] = cachedColorsBgra[offset + 2];
                c[3] = cachedColorsBgra[offset + 3];
                return c;
            }
        }

        private static bool WriteDpiStageWithAck(string path, int stageZeroBased, int dpiX, int dpiY)
        {
            byte[] color = GetStageColorBgra(stageZeroBased);
            if (color == null)
            {
                RefreshTableIfDue(path, 1000);
                color = GetStageColorBgra(stageZeroBased);
            }
            if (color == null)
            {
                if (!ReadAndCacheTable(path, 2)) return false;
                color = GetStageColorBgra(stageZeroBased);
            }
            if (color == null) return false;

            byte[] tx = BuildDpiWriteFrame64(stageZeroBased, dpiX, dpiY, color);
            if (tx == null) return false;

            for (int attempt = 0; attempt < MaxWriteAttempts; attempt++)
            {
                byte[] rx;
                if (SessionSend(path, tx, CmdSetDpi, AckTimeoutMs, out rx) && rx != null && rx.Length >= 13)
                {
                    if (rx[7] == (byte)stageZeroBased &&
                        rx[8] == (byte)(dpiX & 0xFF) && rx[9] == (byte)((dpiX >> 8) & 0xFF) &&
                        rx[10] == (byte)(dpiY & 0xFF) && rx[11] == (byte)((dpiY >> 8) & 0xFF))
                    {
                        return true;
                    }
                }
                if (attempt + 1 < MaxWriteAttempts) Thread.Sleep(60);
            }
            return false;
        }

        private static void ApplyLocalStageCache(int stageZeroBased, int dpiX, int dpiY)
        {
            lock (stateLock)
            {
                if (cachedStageCount <= 0 || stageZeroBased < 0 || stageZeroBased >= cachedStageCount) return;
                int[] nx = new int[cachedStageCount];
                int[] ny = new int[cachedStageCount];
                for (int i = 0; i < cachedStageCount; i++)
                {
                    nx[i] = cachedDpiX[i];
                    ny[i] = cachedDpiY[i];
                }
                nx[stageZeroBased] = dpiX;
                ny[stageZeroBased] = dpiY;
                cachedDpiX = nx;
                cachedDpiY = ny;
                tableCacheValid = true;
            }
        }

        private static string ResolveActiveDonglePath(MouseBatteryInfo dev)
        {
            lock (stateLock)
            {
                if (!string.IsNullOrEmpty(activeDonglePath)) return activeDonglePath;
            }
            List<HidDeviceDescriptor> eps = HidTransport.Enumerate(VendorId, ProductId);
            for (int i = 0; i < eps.Count; i++)
            {
                HidDeviceDescriptor d = eps[i];
                if (!IsNearLinkEndpoint(d)) continue;
                if (HardwareDriverRegistry.IsExternallyOwnedPath(d.DevicePath)) continue;
                lock (stateLock) { activeDonglePath = d.DevicePath; }
                return d.DevicePath;
            }
            return null;
        }

        private static void ApplyCachedDpiSnapshotToDevice(MouseBatteryInfo info)
        {
            if (info == null) return;
            lock (stateLock)
            {
                if (!tableCacheValid || cachedStageCount <= 0) return;

                int[] xsCopy = new int[cachedStageCount];
                int[] ysCopy = new int[cachedStageCount];
                Array.Copy(cachedDpiX, 0, xsCopy, 0, cachedStageCount);
                Array.Copy(cachedDpiY, 0, ysCopy, 0, cachedStageCount);

                info.DpiStageCount = cachedStageCount;
                info.DpiStages = xsCopy;
                info.DpiStagesY = ysCopy;

                int st = lastStageIdx;
                if (st < 0 || st >= cachedStageCount)
                {
                    st = info.DpiStage - 1;
                    if (st < 0 || st >= cachedStageCount) st = 0;
                }

                if (activeDpiX > 0)
                {
                    int hintIdx = ResolveHintIndex(xsCopy, cachedStageCount, activeStageHint, activeDpiX);
                    if (hintIdx >= 0)
                    {
                        st = hintIdx;
                    }
                    else
                    {
                        int match = -1;
                        int hits = 0;
                        for (int k = 0; k < cachedStageCount; k++)
                        {
                            if (xsCopy[k] == activeDpiX && ysCopy[k] == activeDpiY)
                            {
                                hits++;
                                if (match < 0) match = k;
                            }
                        }
                        if (hits == 1)
                        {
                            st = match;
                        }
                        else if (hits > 1)
                        {
                            lastStageIdx = st;
                            info.DpiStage = st + 1;
                            info.Dpi = activeDpiX;
                            info.DpiY = activeDpiY;
                            info.IsXyIndependent = true;
                            return;
                        }
                    }
                }

                lastStageIdx = st;
                info.DpiStage = st + 1;
                info.Dpi = xsCopy[st];
                info.DpiY = ysCopy[st];
                info.IsXyIndependent = true;
            }
        }

        public static List<MouseBatteryInfo> QueryAllDevices()
        {
            lock (hidLock)
            {
                if ((DateTime.Now - lastQuery).TotalMilliseconds < QueryThrottleMs && cachedList.Count > 0)
                {
                    return new List<MouseBatteryInfo>(cachedList);
                }

                List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
                List<HidDeviceDescriptor> wiredDescriptors = HidTransport.Enumerate(VendorId, WiredProductId);
                List<HidDeviceDescriptor> dongleDescriptors = HidTransport.Enumerate(VendorId, ProductId);

                HidDeviceDescriptor wiredEp = null;
                for (int i = 0; i < wiredDescriptors.Count; i++)
                {
                    HidDeviceDescriptor d = wiredDescriptors[i];
                    if (!IsNearLinkEndpoint(d)) continue;
                    if (HardwareDriverRegistry.IsExternallyOwnedPath(d.DevicePath)) continue;
                    wiredEp = d;
                    break;
                }

                HidDeviceDescriptor dongleEp = null;
                for (int i = 0; i < dongleDescriptors.Count; i++)
                {
                    HidDeviceDescriptor d = dongleDescriptors[i];
                    if (!IsNearLinkEndpoint(d)) continue;
                    if (HardwareDriverRegistry.IsExternallyOwnedPath(d.DevicePath)) continue;
                    dongleEp = d;
                    break;
                }

                lock (stateLock)
                {
                    activeWiredPath = (wiredEp != null) ? wiredEp.DevicePath : "";
                    activeDonglePath = (dongleEp != null) ? dongleEp.DevicePath : "";
                }

                if (wiredEp != null)
                {
                    int batt;
                    bool chg;
                    int mv;
                    bool ok = DirectQueryBattery(wiredEp.DevicePath, out batt, out chg, out mv);

                    if (ok && batt >= 0 && batt <= 100)
                    {
                        DriverStateStore.SaveBatteryState(DeviceIdName, batt, chg, ModelName);
                    }

                    MouseBatteryInfo info = new MouseBatteryInfo();
                    info.DeviceId = DeviceIdName;
                    info.HardwareFingerprint = FingerprintName;
                    info.HardwareId = wiredEp.InstanceId;
                    info.DriverId = DriverIdName;
                    info.DeviceName = ModelName;
                    info.Brand = "VXE";
                    info.Category = DeviceCategory.Mouse;
                    info.Transport = "有线 USB";
                    info.IsDonglePresent = false;
                    info.IsConnected = true;
                    info.IsSleeping = false;
                    info.IsCharging = ok ? chg : false;
                    info.BatteryPercent = ok ? batt : -1;
                    info.ExactBatteryPercent = ok ? batt : 0;
                    info.BatteryVoltageMv = ok ? mv : 0;
                    info.CustomTooltip = ok ? "有线 USB" : "有线模式 · 供电中（有线形态下不显示电量）";
                    info.LastUpdated = DateTime.Now;

                    PopulateCapabilities(info);
                    ApplyCachedDpiSnapshotToDevice(info);
                    result.Add(info);
                }
                else if (dongleEp != null)
                {
                    EnsureSession(dongleEp.DevicePath);

                    int batt;
                    bool chg;
                    int mv;
                    bool ok = QueryBatteryViaSession(dongleEp.DevicePath, out batt, out chg, out mv);

                    int cachedBatt;
                    bool cachedChg;
                    string cachedName;
                    DateTime cachedUpdated;
                    bool hasCache = DriverStateStore.TryLoadBatteryState(DeviceIdName, out cachedBatt, out cachedChg, out cachedName, out cachedUpdated);
                    if (!hasCache)
                    {
                        hasCache = DriverStateStore.TryLoadBatteryState(string.Format("Gaming:{0:X4}:{1:X4}", VendorId, ProductId), out cachedBatt, out cachedChg, out cachedName, out cachedUpdated);
                    }

                    if (ok && batt >= 0 && batt <= 100)
                    {
                        DriverStateStore.SaveBatteryState(DeviceIdName, batt, chg, ModelName);

                        bool modeRead;
                        if (TryReadSensorMode(dongleEp.DevicePath, out modeRead))
                        {
                            lock (stateLock) { competitiveModeState = modeRead; }
                        }

                        bool needTable;
                        lock (stateLock) { needTable = !tableCacheValid; }
                        if (needTable)
                        {
                            ReadAndCacheTable(dongleEp.DevicePath, 2);
                        }
                    }

                    MouseBatteryInfo info = new MouseBatteryInfo();
                    info.DeviceId = DeviceIdName;
                    info.HardwareFingerprint = FingerprintName;
                    info.HardwareId = dongleEp.InstanceId;
                    info.DriverId = DriverIdName;
                    info.DeviceName = ModelName;
                    info.Brand = "VXE";
                    info.Category = DeviceCategory.Mouse;
                    info.Transport = "星闪 NearLink 2.4G";
                    info.CustomTooltip = "星闪 NearLink 2.4G";
                    info.IsDonglePresent = true;
                    info.IsConnected = ok;
                    info.IsSleeping = !ok;
                    info.IsCharging = ok ? chg : (hasCache ? cachedChg : false);
                    info.IsChargingUnknown = !ok && !hasCache;
                    info.BatteryPercent = ok ? batt : (hasCache ? cachedBatt : -1);
                    info.ExactBatteryPercent = info.BatteryPercent >= 0 ? info.BatteryPercent : 0;
                    info.BatteryVoltageMv = ok ? mv : 0;
                    info.LastUpdated = ok ? DateTime.Now : (hasCache ? cachedUpdated : DateTime.Now);

                    PopulateCapabilities(info);
                    ApplyCachedDpiSnapshotToDevice(info);
                    result.Add(info);
                }

                lastQuery = DateTime.Now;
                cachedList.Clear();
                cachedList.AddRange(result);
                return result;
            }
        }

        public static bool FastQueryDpi(MouseBatteryInfo dev, out int dpiX, out int dpiY, out int stage, out int count)
        {
            dpiX = 0;
            dpiY = 0;
            stage = 0;
            count = 0;

            string path = ResolveActiveDonglePath(dev);
            if (string.IsNullOrEmpty(path)) return false;

            bool trusting;
            lock (stateLock)
            {
                trusting = (localTrustUntilUtc != DateTime.MinValue && DateTime.UtcNow < localTrustUntilUtc);
            }
            if (!trusting)
            {
                RefreshTableIfDue(path, 5000);
            }

            bool hasValidTable;
            lock (stateLock) { hasValidTable = tableCacheValid && cachedStageCount > 0; }
            if (!hasValidTable)
            {
                if (!ReadAndCacheTable(path, 1)) return false;
            }

            lock (stateLock)
            {
                if (!tableCacheValid || cachedStageCount <= 0) return false;
                count = cachedStageCount;

                int idx = ResolveHintIndex(cachedDpiX, count, activeStageHint, activeDpiX);
                if (idx < 0 && activeDpiX > 0)
                {
                    for (int k = 0; k < count; k++)
                    {
                        if (cachedDpiX[k] == activeDpiX && cachedDpiY[k] == activeDpiY)
                        {
                            idx = k;
                            break;
                        }
                    }
                    if (idx < 0)
                    {
                        lastTableRefreshUtc = DateTime.MinValue;
                    }
                }

                if (idx < 0)
                {
                    idx = lastStageIdx;
                    if (idx < 0 || idx >= count)
                    {
                        idx = (dev != null) ? (dev.DpiStage - 1) : 0;
                        if (idx < 0 || idx >= count) idx = 0;
                    }
                }

                lastStageIdx = idx;
                dpiX = cachedDpiX[idx];
                dpiY = cachedDpiY[idx];
                stage = idx + 1;
            }

            if (dev != null)
            {
                dev.Dpi = dpiX;
                dev.DpiY = dpiY;
            }
            return dpiX > 0;
        }

        public static bool SetDpi(MouseBatteryInfo dev, int dpiX, int dpiY, int stage)
        {
            if (HardwareCommandBus.IsHardwareSandboxActive) return false;
            string path = ResolveActiveDonglePath(dev);
            if (string.IsNullOrEmpty(path)) return false;

            int idx = stage - 1;
            if (idx < 0 || idx >= MaxStageProbe) return false;
            if (dpiX < DpiMinAllowed || dpiX > DpiMaxAllowed) return false;
            if (dpiY < DpiMinAllowed || dpiY > DpiMaxAllowed) return false;

            RefreshTableIfDue(path, 1000);

            if (!WriteDpiStageWithAck(path, idx, dpiX, dpiY)) return false;

            ApplyLocalStageCache(idx, dpiX, dpiY);
            lock (stateLock)
            {
                lastStageIdx = idx;
                activeStageHint = idx;
                activeDpiX = dpiX;
                activeDpiY = dpiY;
                localTrustUntilUtc = DateTime.UtcNow.AddMilliseconds(1500);
                lastTableRefreshUtc = DateTime.UtcNow;
            }

            if (dev != null)
            {
                dev.DpiStage = idx + 1;
                dev.Dpi = dpiX;
                dev.DpiY = dpiY;
                if (dev.DpiStages != null && idx < dev.DpiStages.Length) dev.DpiStages[idx] = dpiX;
                if (dev.DpiStagesY != null && idx < dev.DpiStagesY.Length) dev.DpiStagesY[idx] = dpiY;
            }
            return true;
        }

        public static bool UpdateHardwareStages(MouseBatteryInfo dev, int activeStage, int[] stagesX, int[] stagesY)
        {
            if (HardwareCommandBus.IsHardwareSandboxActive) return false;
            string path = ResolveActiveDonglePath(dev);
            if (string.IsNullOrEmpty(path)) return false;
            if (stagesX == null || stagesY == null) return false;
            int n = stagesX.Length;
            if (n <= 0 || n > MaxStageProbe || stagesY.Length < n) return false;

            RefreshTableIfDue(path, 1000);

            for (int k = 0; k < n; k++)
            {
                int x = stagesX[k];
                int y = stagesY[k];
                if (x < DpiMinAllowed || x > DpiMaxAllowed) return false;
                if (y < DpiMinAllowed || y > DpiMaxAllowed) return false;

                bool unchanged;
                lock (stateLock)
                {
                    unchanged = (tableCacheValid && k < cachedStageCount &&
                                 k < cachedDpiX.Length && k < cachedDpiY.Length &&
                                 cachedDpiX[k] == x && cachedDpiY[k] == y);
                }
                if (unchanged) continue;

                if (!WriteDpiStageWithAck(path, k, x, y)) return false;
            }

            int aidx = activeStage - 1;
            lock (stateLock)
            {
                if (aidx >= 0 && aidx < n) lastStageIdx = aidx;
                localTrustUntilUtc = DateTime.UtcNow.AddMilliseconds(1500);
                lastTableRefreshUtc = DateTime.UtcNow;
            }
            for (int k = 0; k < n; k++)
            {
                ApplyLocalStageCache(k, stagesX[k], stagesY[k]);
            }
            return true;
        }

        public static bool SetPollingRate(MouseBatteryInfo dev, int rateHz)
        {
            if (HardwareCommandBus.IsHardwareSandboxActive) return false;
            string path = ResolveActiveDonglePath(dev);
            if (string.IsNullOrEmpty(path)) return false;

            int code = FindPollingRateCode(rateHz);
            byte[] payload = BuildPollingRateFrame64(rateHz);
            if (code < 0 || payload == null) return false;

            for (int attempt = 0; attempt < MaxWriteAttempts; attempt++)
            {
                byte[] rx;
                if (SessionSend(path, payload, CmdSetPollingRate, AckTimeoutMs, out rx) &&
                    rx != null && rx.Length >= 8 &&
                    rx[5] == CmdSetPollingRate && rx[7] == (byte)code)
                {
                    lock (stateLock) { lastPollingRate = rateHz; }
                    if (dev != null) dev.PollingRate = rateHz;
                    return true;
                }
                if (attempt + 1 < MaxWriteAttempts) Thread.Sleep(60);
            }
            return false;
        }

        public static bool SyncCustomActions(MouseBatteryInfo dev)
        {
            if (dev == null || dev.Capabilities == null || dev.Capabilities.CustomActions == null) return true;
            bool comp;
            lock (stateLock) { comp = competitiveModeState; }
            for (int i = 0; i < dev.Capabilities.CustomActions.Count; i++)
            {
                CustomDeviceAction a = dev.Capabilities.CustomActions[i];
                if (a != null && string.Equals(a.ActionId, ActionIdCompetitiveMode, StringComparison.OrdinalIgnoreCase))
                {
                    a.BoolValue = comp;
                    a.IntValue = comp ? 1 : 0;
                    break;
                }
            }
            return true;
        }

        public static bool ExecuteCustomAction(MouseBatteryInfo dev, string actionId, int value)
        {
            if (HardwareCommandBus.IsHardwareSandboxActive) return false;
            if (string.IsNullOrEmpty(actionId)) return false;
            if (!string.Equals(actionId, ActionIdCompetitiveMode, StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(actionId, "sensor_mode", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(actionId, "auto_sleep", StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            string path = ResolveActiveDonglePath(dev);
            if (string.IsNullOrEmpty(path)) return false;

            bool comp = (value != 0);
            byte expectedByte = comp ? SensorModeCompetitive : SensorModeBasic;
            byte[] payload = BuildSensorModeWriteFrame64(comp);

            for (int attempt = 0; attempt < MaxWriteAttempts; attempt++)
            {
                byte[] rx;
                if (SessionSend(path, payload, CmdSensorMode, AckTimeoutMs, out rx) &&
                    rx != null && rx.Length >= 8 &&
                    rx[5] == CmdSensorMode && rx[7] == expectedByte)
                {
                    lock (stateLock) { competitiveModeState = comp; }
                    SyncCustomActions(dev);
                    return true;
                }
                if (attempt + 1 < MaxWriteAttempts) Thread.Sleep(60);
            }
            return false;
        }
    }
}
