// ============================================================================
// 模块名称: VgnDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   VGN / VXE / 蜻蜓 / ATK 系列鼠标私有 HID 协议通信实现
// 依赖关系: NativeMethods.cs, MouseBatteryInfo.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、协议解析或签名时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace RazerBatteryTray
{
    public static class VgnDeviceHelper
    {
        private static readonly object hidLock = new object();

        // 长期存在的 OVERLAPPED：所有 HID 访问都在 lock(hidLock) 内串行，故可安全复用同一组实例。
        // 放在静态字段而非栈上——超时放弃时内核稍后仍会写入这块内存，若在栈上就会变成悬垂指针
        // （原生访问违例、进程静默消失）；静态字段永远有效，因此可以放心用有界等待。
        private static OVERLAPPED drainOl;
        private static OVERLAPPED writeOl;
        private static OVERLAPPED readOl;

        private static void PrepareOverlapped(ref OVERLAPPED ol, IntPtr hEvent)
        {
            // 复用前必须清零：Internal/InternalHigh 残留上次的 NTSTATUS，会让 GetOverlappedResult 误判
            ol.Internal = IntPtr.Zero;
            ol.InternalHigh = IntPtr.Zero;
            ol.Offset = 0;
            ol.OffsetHigh = 0;
            ol.hEvent = hEvent;
        }
        private const string CacheRegistryKey = @"Software\RazerBatteryTray\VgnCache";

        [StructLayout(LayoutKind.Sequential)]
        private struct OVERLAPPED
        {
            public IntPtr Internal;
            public IntPtr InternalHigh;
            public uint Offset;
            public uint OffsetHigh;
            public IntPtr hEvent;
        }

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr CreateEvent(IntPtr lpEventAttributes, bool bManualReset, bool bInitialState, string lpName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern uint WaitForSingleObject(IntPtr hHandle, uint dwMilliseconds);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ReadFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToRead, out uint lpNumberOfBytesRead, ref OVERLAPPED lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, uint nNumberOfBytesToWrite, out uint lpNumberOfBytesWritten, ref OVERLAPPED lpOverlapped);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool GetOverlappedResult(IntPtr hFile, ref OVERLAPPED lpOverlapped, out uint lpNumberOfBytesTransferred, bool bWait);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CancelIo(IntPtr hFile);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool ResetEvent(IntPtr hEvent);

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;
        private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        private const uint WAIT_OBJECT_0 = 0;

        public static int CachedBatteryPercent = -1;
        public static bool CachedIsCharging = false;
        public static string CachedDeviceName = "";
        public static DateTime CachedLastUpdated = DateTime.MinValue;
        public static bool HasValidHardwareReading = false;
        public static int CachedBatteryVoltageMv = -1;

        // 锂电池电压（mV）→ 电量百分比：复刻官方驱动 Config.ini 的 BatteryParam 表
        // （21 个阈值对应 0%~100%，每档 5%），档间线性插值，保证与官方读数口径一致
        private static readonly int[] OfficialVoltageTable = new int[]
        {
            // 本表逐档照抄官方驱动 Config.ini 的 BatteryParam（2026-10-29 已核对原文）：
            //   D://Software//皂品驱动//Zaopin Wireless Mouse\Config.ini
            //   [Device2] DeviceName=Z2
            //   BatteryParam=1,3050,3420,...,4080,4110
            // 不要凭实测显示值去"校准"这张表——曾按官方驱动低电量显示反推把最低档
            // 改成 3200/3150，后来核对 Config.ini 原文发现 3050 是对的，
            // 低端 1% 的差异来自官方驱动的刷新延迟，不是表格。
            3050, 3420, 3480, 3540, 3600, 3660, 3720, 3760, 3800, 3840, 3880,
            3920, 3940, 3960, 3980, 4000, 4020, 4040, 4060, 4080, 4110
        };

        // 电压→百分比滞回：原始电压存在 ±2~4mV 抖动，直接查表会让百分比在取整边界上来回跳
        // （实测 3796/3800mV 严格交替 → 显示 38%/39% 每 10 秒闪一次）。
        // 中位数滤波对「两值严格交替」无效（只会跟着多数值摆相位），必须用滞回：
        // 只有电压相对「上次采纳值」移动超过 6mV 才允许改变百分比，否则保持上一次显示。
        private static int lastAcceptedPct = -1;
        private static int lastAcceptedMv = -1;
        // 「连续拒绝」计数：突变保护/噪声抑制会保持上次值，若长期保持同一值说明
        // 滞回可能已被锁死（历史故障：一次脏读数把基准置为 100%，之后电压恢复正常
        // 也永远返回 100%）。累计超过阈值即强制采纳当前读数，保证最坏情况能自愈。
        private static int stuckCount = 0;

        private static int PercentWithHysteresis(int mv)
        {
            // 电压合理性闸门：单节锂电池不可能低于 2.8V 或高于 4.3V。
            // 越界值只可能来自响应错位/半包，必须丢弃——否则一次垃圾读数（如 65535mV）
            // 会算出 100% 并被滞回逻辑锁死：之后电压正常了，因 |mv 变化| < 6 仍永远返回 100%。
            if (mv < 2800 || mv > 4300)
            {
                return lastAcceptedPct >= 0 ? lastAcceptedPct : -1;
            }

            int pct = VoltageToPercent(mv);
            if (pct < 0) return pct;

            // 轨值合理性：100% 只可能出现在 >=4050mV，0% 只可能出现在 <=3100mV。
            // 落在中间却算出轨值，说明电压读数不可信，丢弃而不是采纳——
            // 否则一次错误读数会把滞回基准锁在 100%，之后再也降不下来。
            if (pct >= 100 && mv < 4050) return lastAcceptedPct >= 0 ? lastAcceptedPct : -1;
            if (pct <= 0 && mv > 3100) return lastAcceptedPct >= 0 ? lastAcceptedPct : -1;
            if (lastAcceptedPct < 0)
            {
                lastAcceptedPct = pct;
                lastAcceptedMv = mv;
                return pct;
            }
            if (pct == lastAcceptedPct)
            {
                lastAcceptedMv = mv;
                stuckCount = 0;
                return pct;
            }

            // 突变保护：百分比跳变 >10% 时，必须有 >=60mV 的电压变化支撑才采纳
            // （60mV 约对应 10%），否则判为异常读数，丢弃。
            // 噪声抑制：变化幅度不足 6mV 视为抖动，维持原显示。
            bool rejected =
                (Math.Abs(pct - lastAcceptedPct) > 10 && Math.Abs(mv - lastAcceptedMv) < 60)
                || (Math.Abs(mv - lastAcceptedMv) < 6);

            if (rejected)
            {
                // 自愈闸门：连续 20 次（约 200 秒）都被拒绝，说明基准可能已经错误，
                // 此时强制采纳当前读数，避免滞回被永久锁死。
                if (++stuckCount >= 20)
                {
                    lastAcceptedPct = pct;
                    lastAcceptedMv = mv;
                    stuckCount = 0;
                    return pct;
                }
                return lastAcceptedPct;
            }

            lastAcceptedPct = pct;
            lastAcceptedMv = mv;
            stuckCount = 0;
            return pct;
        }

        public static int VoltageToPercent(int mv)
        {
            if (mv <= 0) return -1;
            int last = OfficialVoltageTable.Length - 1;
            if (mv <= OfficialVoltageTable[0]) return 0;
            if (mv >= OfficialVoltageTable[last]) return 100;
            for (int i = 0; i < last; i++)
            {
                int vLow = OfficialVoltageTable[i];
                int vHigh = OfficialVoltageTable[i + 1];
                if (mv >= vLow && mv < vHigh)
                {
                    int pLow = i * 5;
                    int pHigh = (i + 1) * 5;
                    int num = (mv - vLow) * (pHigh - pLow);
                    int den = vHigh - vLow;
                    return pLow + (num + den / 2) / den;
                }
            }
            return 100;
        }

        // 当前电压附近每 1% 对应的毫伏数（用于把电压斜率换算成电量斜率）
        public static double MillivoltsPerPercent(int mv)
        {
            int last = OfficialVoltageTable.Length - 1;
            if (mv <= OfficialVoltageTable[0]) return (OfficialVoltageTable[1] - OfficialVoltageTable[0]) / 5.0;
            if (mv >= OfficialVoltageTable[last]) return (OfficialVoltageTable[last] - OfficialVoltageTable[last - 1]) / 5.0;
            for (int i = 0; i < last; i++)
            {
                if (mv >= OfficialVoltageTable[i] && mv < OfficialVoltageTable[i + 1])
                {
                    return (OfficialVoltageTable[i + 1] - OfficialVoltageTable[i]) / 5.0;
                }
            }
            return 8.0;
        }

        // 省电模式：启动后不回立即查询设备（避免把鼠标从 LP 低功耗推到 HP 高性能），
        // 首屏显示上次缓存值，延迟一段时间才首次真正读取。
        private const string EcoModeValueName = "EcoMode";

        public static bool EcoModeEnabled
        {
            get
            {
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(CacheRegistryKey, false))
                    {
                        if (key == null) return false;
                        var v = key.GetValue(EcoModeValueName);
                        return v != null && Convert.ToInt32(v) == 1;
                    }
                }
                catch (Exception diagEx) { Diagnostics.Log("Hardware/VgnDeviceHelper.cs: 读取省电模式失败", diagEx); }
                return false;
            }
            set
            {
                try
                {
                    using (var key = Registry.CurrentUser.CreateSubKey(CacheRegistryKey))
                    {
                        if (key != null) key.SetValue(EcoModeValueName, value ? 1 : 0, RegistryValueKind.DWord);
                    }
                }
                catch (Exception diagEx) { Diagnostics.Log("Hardware/VgnDeviceHelper.cs: 写入省电模式失败", diagEx); }
            }
        }

        public static void LoadHardwareCache()
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(CacheRegistryKey, false))
                {
                    if (key != null)
                    {
                        var bp = key.GetValue("BatteryPercent");
                        var isValidObj = key.GetValue("IsValid");
                        bool isValid = (isValidObj != null && Convert.ToInt32(isValidObj) == 1);

                        if (bp != null)
                        {
                            int val = Convert.ToInt32(bp);
                            if (val > 0 && val <= 100 && isValid)
                            {
                                CachedBatteryPercent = val;
                                HasValidHardwareReading = true;
                            }
                            else
                            {
                                CachedBatteryPercent = -1;
                                HasValidHardwareReading = false;
                            }
                        }

                        var dn = key.GetValue("DeviceName") as string;
                        if (!string.IsNullOrEmpty(dn)) CachedDeviceName = dn;

                        var chg = key.GetValue("IsCharging");
                        if (chg != null) CachedIsCharging = (Convert.ToInt32(chg) != 0);

                        var updatedStr = key.GetValue("LastUpdated") as string;
                        if (!string.IsNullOrEmpty(updatedStr))
                        {
                            DateTime dt;
                            if (DateTime.TryParse(updatedStr, out dt)) CachedLastUpdated = dt;
                        }
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/VgnDeviceHelper.cs: 已忽略异常", diagEx); }
        }

        public static void SaveHardwareCache()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(CacheRegistryKey))
                {
                    if (key != null)
                    {
                        if (CachedBatteryPercent > 0)
                        {
                            key.SetValue("BatteryPercent", CachedBatteryPercent);
                            key.SetValue("IsValid", HasValidHardwareReading ? 1 : 0);
                        }
                        if (!string.IsNullOrEmpty(CachedDeviceName)) key.SetValue("DeviceName", CachedDeviceName);
                        key.SetValue("IsCharging", CachedIsCharging ? 1 : 0);
                        if (CachedLastUpdated != DateTime.MinValue) key.SetValue("LastUpdated", CachedLastUpdated.ToString("o"));
                    }
                }
            }
            catch (Exception diagEx) { Diagnostics.Log("Hardware/VgnDeviceHelper.cs: 已忽略异常", diagEx); }
        }

        private static byte CalcChecksum(byte[] buf16)
        {
            int sum = 0;
            for (int i = 0; i < buf16.Length - 1; i++) sum += buf16[i];
            sum = sum & 0xFF;
            return (byte)(85 - sum);
        }

        private static byte[] BuildReport17(byte cmd, byte[] extraData)
        {
            byte[] buf16 = new byte[16];
            buf16[0] = cmd;
            if (extraData != null)
            {
                buf16[4] = (byte)extraData.Length;
                for (int i = 0; i < extraData.Length && (5 + i) < 15; i++)
                {
                    buf16[5 + i] = extraData[i];
                }
            }
            byte crc = CalcChecksum(buf16);
            buf16[15] = (byte)(crc - 8);

            byte[] report17 = new byte[17];
            report17[0] = 0x08; // Report ID
            Array.Copy(buf16, 0, report17, 1, 16);
            return report17;
        }

        private static byte[] SendAndReceive(IntPtr handle, IntPtr hEvent, byte[] tx, int inLen, int timeoutMs)
        {
            if (handle == IntPtr.Zero || handle == new IntPtr(-1)) return null;

            // 1. Drain pending reports（循环清空直到连续两次 15ms 无残留，杜绝响应错位）
            byte[] drain = new byte[inLen];
            int emptyRounds = 0;
            while (true)
            {
                PrepareOverlapped(ref drainOl, hEvent);
                uint readBytes;
                ResetEvent(hEvent); // 手动重置事件必须每次 I/O 前清零，否则后续 Wait 会被上次的信号立即唤醒
                bool ok = ReadFile(handle, drain, (uint)drain.Length, out readBytes, ref drainOl);
                if (!ok && Marshal.GetLastWin32Error() == 997) // ERROR_IO_PENDING
                {
                    if (WaitForSingleObject(hEvent, 15) == WAIT_OBJECT_0)
                    {
                        GetOverlappedResult(handle, ref drainOl, out readBytes, false);
                        emptyRounds = 0;
                        continue;
                    }
                    CancelIo(handle);
                    // OVERLAPPED 是长期存在的静态字段，超时放弃也不会悬垂；再等 500ms 让内核收尾，
                    // 但绝不做无界等待——无界等待会在设备无响应时永久持有 hidLock，导致整个程序假死。
                    WaitForSingleObject(hEvent, 500);
                    GetOverlappedResult(handle, ref drainOl, out readBytes, false);
                    emptyRounds++;
                    if (emptyRounds >= 2) break;
                    continue;
                }
                emptyRounds++;
                if (emptyRounds >= 2) break;
            }

            // 2. Write command
            PrepareOverlapped(ref writeOl, hEvent);
            uint written;
            ResetEvent(hEvent);
            bool wOk = WriteFile(handle, tx, (uint)tx.Length, out written, ref writeOl);
            if (!wOk && Marshal.GetLastWin32Error() == 997)
            {
                if (WaitForSingleObject(hEvent, 500) == WAIT_OBJECT_0)
                {
                    GetOverlappedResult(handle, ref writeOl, out written, false);
                }
                else
                {
                    CancelIo(handle);
                    // OVERLAPPED 是长期存在的静态字段，超时放弃也不会悬垂；再等 500ms 让内核收尾，
                    // 但绝不做无界等待——无界等待会在设备无响应时永久持有 hidLock，导致整个程序假死。
                    WaitForSingleObject(hEvent, 500);
                    GetOverlappedResult(handle, ref writeOl, out written, false);
                    return null;
                }
            }

            // 3. Read response
            byte[] rx = new byte[inLen];
            PrepareOverlapped(ref readOl, hEvent);
            uint rxRead = 0;
            ResetEvent(hEvent);
            bool rOk = ReadFile(handle, rx, (uint)rx.Length, out rxRead, ref readOl);
            if (!rOk && Marshal.GetLastWin32Error() == 997)
            {
                if (WaitForSingleObject(hEvent, (uint)timeoutMs) == WAIT_OBJECT_0)
                {
                    GetOverlappedResult(handle, ref readOl, out rxRead, false);
                    return rx;
                }
                else
                {
                    CancelIo(handle);
                    // OVERLAPPED 是长期存在的静态字段，超时放弃也不会悬垂；再等 500ms 让内核收尾，
                    // 但绝不做无界等待——无界等待会在设备无响应时永久持有 hidLock，导致整个程序假死。
                    WaitForSingleObject(hEvent, 500);
                    GetOverlappedResult(handle, ref readOl, out rxRead, false);
                    return null;
                }
            }
            else if (rOk)
            {
                return rx;
            }

            return null;
        }

        public static string GetModelName(int pid, string productString, int cid, int mid)
        {
            if (cid == 7 && mid == 2) return "Zaopin Z2";
            if (cid == 118 && mid == 11) return "VGN Dragonfly F2 Pro Max";
            if (cid == 118 && mid == 10) return "VGN Dragonfly F2 Pro";
            if (cid == 118 && mid == 9) return "VGN Dragonfly F2";
            if (!string.IsNullOrEmpty(productString)) return productString;
            switch (pid)
            {
                // 实测（2026-10-29）：F526 = Type-C 线直插鼠标（接收器拔掉）时出现；
                //                    F527 = 4K 底座式接收器（2.4G）时出现。
                case 0xF526: return "Zaopin Z2"; // 有线直连
                case 0xF527: return "Zaopin Z2"; // 4K 底座式接收器 (NORDIC nRF52810)
                case 0xFB3E: return "VGN Dragonfly F2 Pro Max";
                case 0xFC03: return "VXE Dragonfly R1 Pro";
                case 0xFC04: return "VXE Dragonfly R1 Pro Max";
                case 0xFC05: return "VXE Dragonfly R1 SE";
                case 0xFB24: return "VGN Dragonfly F1";
                case 0xFB25: return "VGN Dragonfly F1 Pro";
                case 0xFB26: return "VGN Dragonfly F1 Pro Max";
                case 0xFB27: return "VGN Dragonfly F1 MOBA";
                default: return "VGN 无线游戏鼠标";
            }
        }

        public static int[] GetSupportedPollingRatesForModel(int pid, string modelName)
        {
            string name = !string.IsNullOrEmpty(modelName) ? modelName.ToLowerInvariant() : "";
            if (pid == 0xFC05 || pid == 0xFB24 || name.Contains(" r1 se") || (name.Contains("dragonfly f1") && !name.Contains("pro") && !name.Contains("moba")))
            {
                // SE / Standard 1K models
                return new int[] { 125, 250, 500, 1000 };
            }
            if (pid == 0xFB3E || name.Contains("f2") || name.Contains("8k") || name.Contains("8000") || name.Contains("atk"))
            {
                // 8K models
                return new int[] { 125, 250, 500, 1000, 2000, 4000, 8000 };
            }
            // 4K models (F1 Pro/Pro Max/MOBA, R1 Pro/Pro Max)
            return new int[] { 125, 250, 500, 1000, 2000, 4000 };
        }

        // 构建配置读写报文（read=0x08 / write=0x07，含 16 位地址与数据）
        private static byte[] BuildConfigReport(byte cmd, int address, byte[] data)
        {
            byte[] buf16 = new byte[16];
            buf16[0] = cmd;
            buf16[2] = (byte)((address >> 8) & 0xFF);
            buf16[3] = (byte)(address & 0xFF);
            if (data != null)
            {
                buf16[4] = (byte)data.Length;
                for (int i = 0; i < data.Length && (5 + i) < 15; i++)
                {
                    buf16[5 + i] = data[i];
                }
            }
            byte crc = CalcChecksum(buf16);
            buf16[15] = (byte)(crc - 8);
            byte[] report17 = new byte[17];
            report17[0] = 0x08;
            Array.Copy(buf16, 0, report17, 1, 16);
            return report17;
        }

        // 打开 FF02 通道 → 握手 → 写配置（data 为配置值字节，含奇偶校验）
        private static bool ExecuteVgnWriteBytes(int address, byte[] data)
        {
            return ExecuteVgnWriteBytes(address, data, 0x07);
        }

        // cmd：0x07 通用写（回报率/切档位），0x08 DPI 值落盘（抓包实测官方用 0x08 提交 DPI）
        private static bool ExecuteVgnWriteBytes(int address, byte[] data, byte cmd)
        {
            lock (hidLock)
            {
                try
                {
                    Guid hidGuid;
                    RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
                    IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
                    if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return false;

                    RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                    uint memberIdx = 0;

                    try
                    {
                        ifData.cbSize = Marshal.SizeOf(ifData);
                        while (RazerDeviceHelper.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                        {
                            uint reqSize;
                            RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                            if (reqSize == 0) continue;

                            IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                            Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                            if (RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                            {
                                IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                                string devicePath = Marshal.PtrToStringAuto(pDevicePath);
                                if (devicePath != null && devicePath.ToLower().Contains("vid_3554"))
                                {
                                    IntPtr handle = RazerDeviceHelper.CreateFile(devicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                                    if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                    {
                                        try
                                        {
                                            IntPtr preparsed;
                                            if (RazerDeviceHelper.HidD_GetPreparsedData(handle, out preparsed))
                                            {
                                                RazerDeviceHelper.HIDP_CAPS caps;
                                                RazerDeviceHelper.HidP_GetCaps(preparsed, out caps);
                                                RazerDeviceHelper.HidD_FreePreparsedData(preparsed);

                                                if (caps.UsagePage == 0xFF02 && caps.Usage == 0x0002 && caps.InputReportByteLength >= 17 && caps.OutputReportByteLength >= 17)
                                                {
                                                    IntPtr hEvent = CreateEvent(IntPtr.Zero, true, false, null);
                                                    if (hEvent != IntPtr.Zero)
                                                    {
                                                        try
                                                        {
                                                            Random rnd = new Random();
                                                            byte[] randBytes = new byte[8] { (byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256), 0, 0, 0, 0 };
                                                            SendAndReceive(handle, hEvent, BuildReport17(1, randBytes), caps.InputReportByteLength, 300);
                                                            SendAndReceive(handle, hEvent, BuildReport17(2, new byte[1] { 1 }), caps.InputReportByteLength, 200);
                                                            SendAndReceive(handle, hEvent, BuildReport17(3, null), caps.InputReportByteLength, 300);

                                                            byte[] tx = BuildConfigReport(cmd, address, data);
                                                            byte[] rx = SendAndReceive(handle, hEvent, tx, caps.InputReportByteLength, 300);
                                                            return rx != null;
                                                        }
                                                        finally
                                                        {
                                                            RazerDeviceHelper.CloseHandle(hEvent);
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        finally
                                        {
                                            RazerDeviceHelper.CloseHandle(handle);
                                        }
                                    }
                                }
                                Marshal.FreeHGlobal(detailBuffer);
                            }
                        }
                    }
                    finally
                    {
                        RazerDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);
                    }
                }
                catch (Exception diagEx) { Diagnostics.Log("Hardware/VgnDeviceHelper.cs: 已忽略异常", diagEx); }
            }
            return false;
        }

        // 打开 FF02 通道 → 握手一次 → 循环写多个配置（addresses[i] 写 datas[i]），避免每档都重新打开+握手
        // 返回全部写成功的布尔数组；任一项失败不影响其余项
        private static bool[] ExecuteVgnWriteBatch(int[] addresses, byte[][] datas)
        {
            bool[] results = new bool[addresses.Length];
            lock (hidLock)
            {
                try
                {
                    Guid hidGuid;
                    RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
                    IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
                    if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return results;

                    RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                    uint memberIdx = 0;

                    try
                    {
                        ifData.cbSize = Marshal.SizeOf(ifData);
                        while (RazerDeviceHelper.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                        {
                            uint reqSize;
                            RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                            if (reqSize == 0) continue;

                            IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                            Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                            if (RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                            {
                                IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                                string devicePath = Marshal.PtrToStringAuto(pDevicePath);
                                if (devicePath != null && devicePath.ToLower().Contains("vid_3554"))
                                {
                                    IntPtr handle = RazerDeviceHelper.CreateFile(devicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                                    if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                    {
                                        try
                                        {
                                            IntPtr preparsed;
                                            if (RazerDeviceHelper.HidD_GetPreparsedData(handle, out preparsed))
                                            {
                                                RazerDeviceHelper.HIDP_CAPS caps;
                                                RazerDeviceHelper.HidP_GetCaps(preparsed, out caps);
                                                RazerDeviceHelper.HidD_FreePreparsedData(preparsed);

                                                if (caps.UsagePage == 0xFF02 && caps.Usage == 0x0002 && caps.InputReportByteLength >= 17 && caps.OutputReportByteLength >= 17)
                                                {
                                                    IntPtr hEvent = CreateEvent(IntPtr.Zero, true, false, null);
                                                    if (hEvent != IntPtr.Zero)
                                                    {
                                                        try
                                                        {
                                                            // 一次性握手
                                                            Random rnd = new Random();
                                                            byte[] randBytes = new byte[8] { (byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256), 0, 0, 0, 0 };
                                                            SendAndReceive(handle, hEvent, BuildReport17(1, randBytes), caps.InputReportByteLength, 300);
                                                            SendAndReceive(handle, hEvent, BuildReport17(2, new byte[1] { 1 }), caps.InputReportByteLength, 200);
                                                            SendAndReceive(handle, hEvent, BuildReport17(3, null), caps.InputReportByteLength, 300);

                                                            // 循环写多项
                                                            for (int i = 0; i < addresses.Length; i++)
                                                            {
                                                                byte[] tx = BuildConfigReport(0x07, addresses[i], datas[i]);
                                                                byte[] rx = SendAndReceive(handle, hEvent, tx, caps.InputReportByteLength, 300);
                                                                results[i] = (rx != null);
                                                            }
                                                        }
                                                        finally
                                                        {
                                                            RazerDeviceHelper.CloseHandle(hEvent);
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        finally
                                        {
                                            RazerDeviceHelper.CloseHandle(handle);
                                        }
                                    }
                                }
                                Marshal.FreeHGlobal(detailBuffer);
                            }
                        }
                    }
                    finally
                    {
                        RazerDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);
                    }
                }
                catch (Exception diagEx) { Diagnostics.Log("Hardware/VgnDeviceHelper.cs: 已忽略异常", diagEx); }
            }
            return results;
        }

        public static bool SetVgnDpiStage(int targetStage)
        {
            if (targetStage < 1) return false;
            byte index = (byte)(targetStage - 1);
            return ExecuteVgnWriteBytes(0x04, new byte[] { index, (byte)((0x55 - index) & 0xFF) });
        }

        // 设置指定档位的 DPI 值（dpi 50-26000，50 步进；stageIndex 为 0-based 档位序号）
        public static bool SetVgnDpiValue(int dpi, int stageIndex)
        {
            if (dpi < 50 || dpi > 26000) return false;
            if (stageIndex < 0 || stageIndex > 7) return false;
            int enc = dpi / 50 - 1;
            byte low = (byte)(enc & 0xFF);
            byte high = (byte)((enc >> 8) & 0x03);
            byte flags = (byte)((high << 2) | (high << 6));
            byte ck = (byte)((0x55 - low - low - flags) & 0xFF);
            // 官方 DPI 落盘命令为 0x08（0x07 仅是拖拽预览，不持久化）
            return ExecuteVgnWriteBytes(0x0c + stageIndex * 4, new byte[] { low, low, flags, ck }, 0x08);
        }

        // 回报率编码（CompX 方案地址 0x00）：125→8, 250→4, 500→2, 1000→1, 2000→16, 4000→32（皂品 Z2 4K 版上限 4000Hz）
        private static byte EncodeVgnPollingRate(int hz)
        {
            switch (hz)
            {
                case 125: return 8;
                case 250: return 4;
                case 500: return 2;
                case 1000: return 1;
                case 2000: return 16;
                case 4000: return 32;
                default: return 0;
            }
        }

        // 休眠时间写入（抓包实测：地址 0xAD，code = 秒数 / 10，校验 0x55 - code）
        // 官方档位：10秒(1)、30秒(3)、1分钟(6)、5分钟(30)、10分钟(60)、15分钟(90)、
        //           20分钟(120)、25分钟(150)、30分钟(180，上限 0xB4)
        public static bool SetVgnSleepTime(int seconds)
        {
            if (seconds < 10 || seconds > 1800) return false;
            int code = seconds / 10;
            if (code < 1 || code > 180) return false;
            return ExecuteVgnWriteBytes(0xAD, new byte[] { (byte)code, (byte)((0x55 - code) & 0xFF) });
        }

        // 读取休眠时间（返回秒数；失败返回 -1）
        public static int GetVgnSleepTime()
        {
            lock (hidLock)
            {
                try
                {
                    Guid hidGuid;
                    RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
                    IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
                    if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return -1;

                    RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                    uint memberIdx = 0;
                    try
                    {
                        ifData.cbSize = Marshal.SizeOf(ifData);
                        while (RazerDeviceHelper.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                        {
                            uint reqSize;
                            RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                            if (reqSize == 0) continue;

                            IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                            Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);
                            if (RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                            {
                                IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                                string devicePath = Marshal.PtrToStringAuto(pDevicePath);
                                if (devicePath != null && devicePath.ToLower().Contains("vid_3554"))
                                {
                                    IntPtr handle = RazerDeviceHelper.CreateFile(devicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                                    if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                    {
                                        try
                                        {
                                            IntPtr preparsed;
                                            if (RazerDeviceHelper.HidD_GetPreparsedData(handle, out preparsed))
                                            {
                                                RazerDeviceHelper.HIDP_CAPS caps;
                                                RazerDeviceHelper.HidP_GetCaps(preparsed, out caps);
                                                RazerDeviceHelper.HidD_FreePreparsedData(preparsed);

                                                if (caps.UsagePage == 0xFF02 && caps.Usage == 0x0002 && caps.InputReportByteLength >= 17 && caps.OutputReportByteLength >= 17)
                                                {
                                                    IntPtr hEvent = CreateEvent(IntPtr.Zero, true, false, null);
                                                    if (hEvent != IntPtr.Zero)
                                                    {
                                                        try
                                                        {
                                                            Random rnd = new Random();
                                                            byte[] randBytes = new byte[8] { (byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256), 0, 0, 0, 0 };
                                                            SendAndReceive(handle, hEvent, BuildReport17(1, randBytes), caps.InputReportByteLength, 300);
                                                            SendAndReceive(handle, hEvent, BuildReport17(2, new byte[1] { 1 }), caps.InputReportByteLength, 200);
                                                            SendAndReceive(handle, hEvent, BuildReport17(3, null), caps.InputReportByteLength, 300);

                                                            for (int retry = 0; retry < 3; retry++)
                                                            {
                                                                byte[] tx = BuildConfigReport(0x08, 0xAD, new byte[2]);
                                                                byte[] rx = SendAndReceive(handle, hEvent, tx, caps.InputReportByteLength, 300);
                                                                if (rx != null && rx.Length > 7 && rx[1] == 0x08 && rx[5] == 2 && ((rx[6] + rx[7]) & 0xFF) == 0x55)
                                                                {
                                                                    return rx[6] * 10;
                                                                }
                                                            }
                                                        }
                                                        finally
                                                        {
                                                            RazerDeviceHelper.CloseHandle(hEvent);
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        finally
                                        {
                                            RazerDeviceHelper.CloseHandle(handle);
                                        }
                                    }
                                }
                                Marshal.FreeHGlobal(detailBuffer);
                            }
                        }
                    }
                    finally
                    {
                        RazerDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);
                    }
                }
                catch (Exception diagEx) { Diagnostics.Log("Hardware/VgnDeviceHelper.cs: 读取休眠时间失败", diagEx); }
                return -1;
            }
        }

        // 切换回报率（hz 仅支持 125/250/500/1000/2000/4000，上限 4000Hz）
        public static bool SetVgnPollingRate(int hz)
        {
            byte code = EncodeVgnPollingRate(hz);
            if (code == 0) return false;
            return ExecuteVgnWriteBytes(0x00, new byte[] { code, (byte)((0x55 - code) & 0xFF) });
        }

        // 极速读取当前 DPI 档位（返回 1-based 档位序号；失败返回 -1）
        // 供主界面后台监听调用：实体 DPI 键切换后，读地址 0x04 同步档位
        public static int FastQueryVgnDpiStage()
        {
            lock (hidLock)
            {
                try
                {
                    Guid hidGuid;
                    RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
                    IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
                    if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return -1;

                    RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                    uint memberIdx = 0;

                    try
                    {
                        ifData.cbSize = Marshal.SizeOf(ifData);
                        while (RazerDeviceHelper.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                        {
                            uint reqSize;
                            RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                            if (reqSize == 0) continue;

                            IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                            Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                            if (RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                            {
                                IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                                string devicePath = Marshal.PtrToStringAuto(pDevicePath);
                                if (devicePath != null && devicePath.ToLower().Contains("vid_3554"))
                                {
                                    IntPtr handle = RazerDeviceHelper.CreateFile(devicePath, GENERIC_READ | GENERIC_WRITE, FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, FILE_FLAG_OVERLAPPED, IntPtr.Zero);
                                    if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                    {
                                        try
                                        {
                                            IntPtr preparsed;
                                            if (RazerDeviceHelper.HidD_GetPreparsedData(handle, out preparsed))
                                            {
                                                RazerDeviceHelper.HIDP_CAPS caps;
                                                RazerDeviceHelper.HidP_GetCaps(preparsed, out caps);
                                                RazerDeviceHelper.HidD_FreePreparsedData(preparsed);

                                                if (caps.UsagePage == 0xFF02 && caps.Usage == 0x0002 && caps.InputReportByteLength >= 17 && caps.OutputReportByteLength >= 17)
                                                {
                                                    IntPtr hEvent = CreateEvent(IntPtr.Zero, true, false, null);
                                                    if (hEvent != IntPtr.Zero)
                                                    {
                                                        try
                                                        {
                                                            Random rnd = new Random();
                                                            byte[] randBytes = new byte[8] { (byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256), 0, 0, 0, 0 };
                                                            SendAndReceive(handle, hEvent, BuildReport17(1, randBytes), caps.InputReportByteLength, 300);
                                                            SendAndReceive(handle, hEvent, BuildReport17(2, new byte[1] { 1 }), caps.InputReportByteLength, 200);
                                                            SendAndReceive(handle, hEvent, BuildReport17(3, null), caps.InputReportByteLength, 300);

                                                            // 读当前档位（地址 0x04，读 2 字节：value + 奇偶校验 0x55-value）；校验响应命令号 rx[1]==0x08，最多重试 3 次
                                                            for (int retry = 0; retry < 3; retry++)
                                                            {
                                                                byte[] tx = BuildConfigReport(0x08, 0x04, new byte[2]);
                                                                byte[] rx = SendAndReceive(handle, hEvent, tx, caps.InputReportByteLength, 300);
                                                                if (rx != null && rx.Length > 7 && rx[1] == 0x08 && rx[5] == 2 && ((rx[6] + rx[7]) & 0xFF) == 0x55)
                                                                {
                                                                    return rx[6] + 1; // 转 1-based 档位序号
                                                                }
                                                            }
                                                        }
                                                        finally
                                                        {
                                                            RazerDeviceHelper.CloseHandle(hEvent);
                                                        }
                                                    }
                                                }
                                            }
                                        }
                                        finally
                                        {
                                            RazerDeviceHelper.CloseHandle(handle);
                                        }
                                    }
                                }
                                Marshal.FreeHGlobal(detailBuffer);
                            }
                        }
                    }
                    finally
                    {
                        RazerDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);
                    }
                }
                catch (Exception diagEx) { Diagnostics.Log("Hardware/VgnDeviceHelper.cs: 已忽略异常", diagEx); }
            }
            return -1;
        }

        // 批量写入全部 DPI 档位并切换到激活档（activeStage 为 1-based）
        // 优化：一次打开+握手，循环写全部档位与激活档，避免每档重新握手
        public static bool UpdateVgnHardwareStages(int activeStage, int[] stages)
        {
            if (stages == null || stages.Length == 0) return false;

            int writeCount = stages.Length;
            if (activeStage >= 1) writeCount += 1;

            int[] addrs = new int[writeCount];
            byte[][] datas = new byte[writeCount][];
            int idx = 0;
            for (int i = 0; i < stages.Length && i < 8; i++)
            {
                int dpi = stages[i];
                if (dpi < 50 || dpi > 26000) dpi = 800;
                int enc = dpi / 50 - 1;
                byte low = (byte)(enc & 0xFF);
                byte high = (byte)((enc >> 8) & 0x03);
                byte flags = (byte)((high << 2) | (high << 6));
                byte ck = (byte)((0x55 - low - low - flags) & 0xFF);
                addrs[idx] = 0x0c + i * 4;
                datas[idx] = new byte[] { low, low, flags, ck };
                idx++;
            }
            if (activeStage >= 1)
            {
                byte stageIndex = (byte)(activeStage - 1);
                addrs[idx] = 0x04;
                datas[idx] = new byte[] { stageIndex, (byte)((0x55 - stageIndex) & 0xFF) };
            }

            bool[] results = ExecuteVgnWriteBatch(addrs, datas);
            bool allOk = true;
            for (int i = 0; i < results.Length; i++)
            {
                if (!results[i]) allOk = false;
            }
            return allOk;
        }

        public static MouseBatteryInfo QueryVgnDeviceInfo()
        {
            lock (hidLock)
            {
                Guid hidGuid;
                RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
                IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return null;

                RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                uint memberIdx = 0;
                bool foundVgnDongle = false;
                string foundName = null;
                int foundBattery = -1;
                bool foundCharging = false;
                bool isMouseOnline = false;
                int foundPid = 0;
                int detectedCid = 0;
                int detectedMid = 0;
                int foundStage = -1;
                int foundVoltageMv = -1;
                int[] readStages = new int[] { -1, -1, -1, -1, -1, -1 };
                int foundPollingRate = -1;
                int foundStageCount = -1;

                try
                {
                    ifData.cbSize = Marshal.SizeOf(ifData);
                    while (RazerDeviceHelper.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                    {
                        uint reqSize;
                        RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                        if (reqSize == 0) continue;

                        IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                        if (RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                        {
                            IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                            string devicePath = Marshal.PtrToStringAuto(pDevicePath);
                            if (devicePath != null && (devicePath.ToLower().Contains("vid_3554") || devicePath.ToLower().Contains("vgn")))
                            {
                                IntPtr handle = RazerDeviceHelper.CreateFile(
                                    devicePath,
                                    GENERIC_READ | GENERIC_WRITE,
                                    FILE_SHARE_READ | FILE_SHARE_WRITE,
                                    IntPtr.Zero,
                                    OPEN_EXISTING,
                                    FILE_FLAG_OVERLAPPED,
                                    IntPtr.Zero);

                                if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                {
                                    try
                                    {
                                        if (string.IsNullOrEmpty(foundName))
                                        {
                                            StringBuilder sb = new StringBuilder(256);
                                            if (RazerDeviceHelper.HidD_GetProductString(handle, sb, sb.Capacity))
                                            {
                                                string pStr = sb.ToString().Trim();
                                                if (!string.IsNullOrEmpty(pStr)) foundName = pStr;
                                            }
                                        }

                                        RazerDeviceHelper.HIDD_ATTRIBUTES attr = new RazerDeviceHelper.HIDD_ATTRIBUTES();
                                        attr.Size = Marshal.SizeOf(attr);
                                        if (RazerDeviceHelper.HidD_GetAttributes(handle, ref attr))
                                        {
                                            if (attr.ProductID != 0) foundPid = attr.ProductID;
                                        }

                                        IntPtr preparsed;
                                        if (RazerDeviceHelper.HidD_GetPreparsedData(handle, out preparsed))
                                        {
                                            RazerDeviceHelper.HIDP_CAPS caps;
                                            RazerDeviceHelper.HidP_GetCaps(preparsed, out caps);
                                            RazerDeviceHelper.HidD_FreePreparsedData(preparsed);

                                            // Target communication endpoint: UsagePage == 0xFF02 && Usage == 0x0002
                                            if (caps.UsagePage == 0xFF02 && caps.Usage == 0x0002 && caps.InputReportByteLength >= 17 && caps.OutputReportByteLength >= 17)
                                            {
                                                foundVgnDongle = true;
                                                IntPtr hEvent = CreateEvent(IntPtr.Zero, true, false, null);
                                                if (hEvent != IntPtr.Zero)
                                                {
                                                    try
                                                    {
                                                        // 1. Encryption handshake (Command 1)
                                                        Random rnd = new Random();
                                                        byte[] randBytes = new byte[8] { (byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256), (byte)rnd.Next(256), 0, 0, 0, 0 };
                                                        byte[] rxEnc = SendAndReceive(handle, hEvent, BuildReport17(1, randBytes), caps.InputReportByteLength, 300);
                                                        if (rxEnc != null && rxEnc.Length > 12)
                                                        {
                                                            detectedCid = rxEnc[10];
                                                            detectedMid = rxEnc[11];
                                                        }

                                                        // 2. PC Driver Status (Command 2)
                                                        SendAndReceive(handle, hEvent, BuildReport17(2, new byte[1] { 1 }), caps.InputReportByteLength, 200);

                                                        // 3. Online Check (Command 3)
                                                        byte[] rxOnline = SendAndReceive(handle, hEvent, BuildReport17(3, null), caps.InputReportByteLength, 300);
                                                        if (rxOnline != null && rxOnline.Length > 6 && rxOnline[6] > 0)
                                                        {
                                                            isMouseOnline = true;

                                                            // 4. Query Battery (Command 4)：校验响应命令号 rx[1]==0x04，防止读到握手/在线探测的错位残留响应
                                                            byte[] rxBat = null;
                                                            for (int bRetry = 0; bRetry < 3; bRetry++)
                                                            {
                                                                rxBat = SendAndReceive(handle, hEvent, BuildReport17(4, null), caps.InputReportByteLength, 400);
                                                                if (rxBat != null && rxBat.Length > 8 && rxBat[1] == 0x04)
                                                                {
                                                                    break;
                                                                }
                                                                rxBat = null;
                                                            }
                                                            if (rxBat != null && rxBat.Length > 8)
                                                            {
#if CHG_DEBUG
                                                                // 临时诊断：确认充电标志位到底在第几字节、取值是什么
                                                                Diagnostics.Log("[CHG] len=" + rxBat.Length + " raw=" + BitConverter.ToString(rxBat));
#endif
                                                                int bVal = rxBat[6];
                                                                if (bVal >= 0 && bVal <= 100)
                                                                {
                                                                    foundBattery = bVal;
                                                                    foundCharging = (rxBat[7] == 1);
                                                                    // bytes[8..9] = 电池电压（mV，大端），如 10 61 → 4193mV
                                                                    int vMv = (rxBat[8] << 8) | rxBat[9];
                                                                    if (vMv >= 3000 && vMv <= 4500)
                                                                    {
                                                                        foundVoltageMv = vMv;
                                                                    }
                                                                }
                                                            }

                                                            // 5. Query Current DPI Stage (地址 0x04，读 2 字节 value + 奇偶校验 0x55-value)：校验响应命令号 rx[1]==0x08
                                                            byte[] rxStage = null;
                                                            for (int sRetry = 0; sRetry < 3; sRetry++)
                                                            {
                                                                rxStage = SendAndReceive(handle, hEvent, BuildConfigReport(0x08, 0x04, new byte[2]), caps.InputReportByteLength, 300);
                                                                if (rxStage != null && rxStage.Length > 7 && rxStage[1] == 0x08)
                                                                {
                                                                    break;
                                                                }
                                                                rxStage = null;
                                                            }
                                                            if (rxStage != null && rxStage.Length > 7 && rxStage[5] == 2 && ((rxStage[6] + rxStage[7]) & 0xFF) == 0x55)
                                                            {
                                                                foundStage = rxStage[6] + 1; // 转 1-based 档位序号
                                                            }

                                                            // 5.5 读取 DPI 档位数（地址 0x02，value + 奇偶校验 0x55-value），决定实际档位数量
                                                            byte[] rxCnt = null;
                                                            for (int cRetry = 0; cRetry < 2; cRetry++)
                                                            {
                                                                rxCnt = SendAndReceive(handle, hEvent, BuildConfigReport(0x08, 0x02, new byte[2]), caps.InputReportByteLength, 300);
                                                                if (rxCnt != null && rxCnt.Length > 7 && rxCnt[1] == 0x08)
                                                                {
                                                                    break;
                                                                }
                                                                rxCnt = null;
                                                            }
                                                            if (rxCnt != null && rxCnt.Length > 7 && rxCnt[5] == 2 && ((rxCnt[6] + rxCnt[7]) & 0xFF) == 0x55 && rxCnt[6] >= 1 && rxCnt[6] <= 8)
                                                            {
                                                                foundStageCount = rxCnt[6];
                                                            }

                                                            // 6. 读取各档位的真实 DPI 值（地址 0x0c 起每档 4 字节 [low, low, flags, ck]），档位数取自硬件而非硬编码
                                                            int hwStageCount = (foundStageCount > 0) ? foundStageCount : 6;
                                                            for (int st = 0; st < hwStageCount && st < 8; st++)
                                                            {
                                                                byte[] rxSt = null;
                                                                for (int stRetry = 0; stRetry < 2; stRetry++)
                                                                {
                                                                    rxSt = SendAndReceive(handle, hEvent, BuildConfigReport(0x08, 0x0c + st * 4, new byte[4]), caps.InputReportByteLength, 300);
                                                                    if (rxSt != null && rxSt.Length > 9 && rxSt[1] == 0x08)
                                                                    {
                                                                        break;
                                                                    }
                                                                    rxSt = null;
                                                                }
                                                                if (rxSt != null && rxSt.Length > 9 && rxSt[5] == 4 && ((rxSt[6] + rxSt[7] + rxSt[8] + rxSt[9]) & 0xFF) == 0x55)
                                                                {
                                                                    int low = rxSt[6];
                                                                    int high = ((rxSt[8] >> 2) & 0x03); // flags=(high<<2)|(high<<6)
                                                                    int enc = low | (high << 8);
                                                                    int dpiVal = (enc + 1) * 50;
                                                                    if (dpiVal >= 50 && dpiVal <= 26000)
                                                                    {
                                                                        readStages[st] = dpiVal;
                                                                    }
                                                                }
                                                            }

                                                            // 7. 读取当前回报率（地址 0x00，2 字节 value + 奇偶校验 0x55-value）
                                                            //    编码与写入一致：8=125/4=250/2=500/1=1000/16=2000/32=4000
                                                            byte[] rxRate = null;
                                                            for (int rRetry = 0; rRetry < 2; rRetry++)
                                                            {
                                                                rxRate = SendAndReceive(handle, hEvent, BuildConfigReport(0x08, 0x00, new byte[2]), caps.InputReportByteLength, 300);
                                                                if (rxRate != null && rxRate.Length > 7 && rxRate[1] == 0x08)
                                                                {
                                                                    break;
                                                                }
                                                                rxRate = null;
                                                            }
                                                            if (rxRate != null && rxRate.Length > 7 && rxRate[5] == 2 && ((rxRate[6] + rxRate[7]) & 0xFF) == 0x55)
                                                            {
                                                                switch (rxRate[6])
                                                                {
                                                                    case 8: foundPollingRate = 125; break;
                                                                    case 4: foundPollingRate = 250; break;
                                                                    case 2: foundPollingRate = 500; break;
                                                                    case 1: foundPollingRate = 1000; break;
                                                                    case 16: foundPollingRate = 2000; break;
                                                                    case 32: foundPollingRate = 4000; break;
                                                                }
                                                            }
                                                        }
                                                        else
                                                        {
                                                            isMouseOnline = false;
                                                        }
                                                    }
                                                    finally
                                                    {
                                                        RazerDeviceHelper.CloseHandle(hEvent);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                    finally
                                    {
                                        RazerDeviceHelper.CloseHandle(handle);
                                    }
                                }
                            }
                        }
                        Marshal.FreeHGlobal(detailBuffer);
                        ifData.cbSize = Marshal.SizeOf(ifData);
                    }
                }
                finally
                {
                    RazerDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);
                }

                if (foundVgnDongle)
                {
                    if (foundBattery >= 0)
                    {
                        CachedBatteryPercent = foundBattery;
                        CachedIsCharging = foundCharging;
                        CachedLastUpdated = DateTime.Now;
                        HasValidHardwareReading = true;
                        if (foundVoltageMv > 0) CachedBatteryVoltageMv = foundVoltageMv;
                        // 缓存写入移到 displayBattery 算完之后（见文件末尾），保证写入的是界面显示值
                    }

                    string devName = GetModelName(foundPid, foundName, detectedCid, detectedMid);
                    if (string.IsNullOrEmpty(devName)) devName = !string.IsNullOrEmpty(CachedDeviceName) ? CachedDeviceName : ("VGN 游戏鼠标 (PID: " + (foundPid > 0 ? foundPid.ToString("X4") : "3554") + ")");
                    CachedDeviceName = devName;

                    int displayBattery;
                    if (foundBattery >= 0)
                    {
                        displayBattery = foundBattery;
                    }
                    else if (HasValidHardwareReading && CachedBatteryPercent > 0)
                    {
                        displayBattery = CachedBatteryPercent;
                    }
                    else
                    {
                        displayBattery = -1;
                    }

                    bool displayCharging = (foundBattery >= 0) ? foundCharging : CachedIsCharging;

                    // 电量主显示口径（已按抓包复核修订，勿再改回旧结论）：
                    //   · 放电（非充电）：主显示 = 电压法（官方 21 点表 + 12mV 压降补偿）。固件库仑计在
                    //     中间区间会长时间停滞（实测 50% 卡住 100 分钟），无法反映真实掉电；
                    //   · 充电 / 有线：主显示 = 固件库仑计。充电时电压被抬高，电压法会虚高；
                    //   · 两种口径都写进悬停提示，便于对照排查。
                    // 历史注记：早期曾按「官方 = 库仑计」实现（当时观测固件 70% / 官方 70% / 电压 73%），
                    // 后续抓包推翻了该结论（官方查表前会先扣约 12mV），故改为电压法为主。
                    int displayVoltageMv = (foundVoltageMv > 0) ? foundVoltageMv : CachedBatteryVoltageMv;

                    // 品牌与能力动态派生：皂品 Z2 (cid=7 / pid 0xF526 / 0xF527，接收器多 PID 变体) 与 VGN 蜻蜓同属 CompX 方案
                    bool isZaopin = (detectedCid == 7) || (foundPid == 0xF526) || (foundPid == 0xF527);
                    string brandName = isZaopin ? "Zaopin" : "VGN";
                    int[] defaultStages = isZaopin
                        ? new int[] { 400, 800, 1600, 2400, 3200, 6400 }
                        : new int[] { 400, 800, 1600, 3200, 6400 };

                    // 优先使用从硬件读回的真实档位值（用户可能自定义修改过），读不到的档位回退默认值
                    int stageCount = (foundStageCount >= 1 && foundStageCount <= 8) ? foundStageCount : (isZaopin ? 6 : 5);
                    int[] dpiStages = new int[stageCount];
                    int readOkCount = 0;
                    for (int i = 0; i < stageCount; i++)
                    {
                        if (i < readStages.Length && readStages[i] >= 50 && readStages[i] <= 26000)
                        {
                            dpiStages[i] = readStages[i];
                            readOkCount++;
                        }
                        else if (i < defaultStages.Length)
                        {
                            dpiStages[i] = defaultStages[i];
                        }
                        else
                        {
                            dpiStages[i] = 800;
                        }
                    }
                    if (readOkCount == 0)
                    {
                        // 全部读取失败时整体回退默认档位
                        dpiStages = defaultStages;
                    }

                    // 当前 DPI = 激活档位的真实值
                    int currentDpi = (foundStage >= 1 && foundStage <= dpiStages.Length) ? dpiStages[foundStage - 1] : 1600;

                    int[] pollRates = isZaopin
                        ? new int[] { 125, 250, 500, 1000, 2000, 4000 }
                        : GetSupportedPollingRatesForModel(foundPid, devName);

                    // 连接形态判断：**不能只看是否充电**。
                    // 2.4G 模式下把充电线插到鼠标上同样会 charging=true（实测 2026-10-29），
                    // 原来按 charging 判断会把 2.4G 误标成「有线连接」。
                    // 正确的判据是 USB PID：F526 = 有线直连，F527 = 2.4G 底座接收器。
                    // 保留 charging 作为与条件，是为了兼容「Nano 接收器也用 F526」的传闻——
                    // 那种情况下不会充电，仍应显示 2.4G。
                    string transportText = (foundPid == 0xF526 && displayCharging) ? "有线连接" : "2.4G 无线";
#if CHG_DEBUG
                    Diagnostics.Log(string.Format("[STATE] pid=0x{0:X4} foundChg={1} dispChg={2} transport={3} batt={4} online={5}",
                        foundPid, foundCharging, displayCharging, transportText, foundBattery, isMouseOnline));
#endif

                    // 悬停提示：皂品 Z2 的固件电量计在中间区间会长时间停滞（实测 50% 卡住 100 分钟），
                    // 官方驱动用的是电压法，故放电时主显示与官方对齐用电压法，库仑计仅作参考
                    // 官方驱动查表前会先扣掉约 12mV 的负载压降补偿（实测：3858mV 官方显示 46%，
                    // 而 3858mV 直接查表为 47%；3858-12=3846 查表正好 46%，3863 同理）
                    // 百分比换算带滞回防抖，悬停提示仍展示原始电压（用户要看真实值）
                    int calibratedMv = displayVoltageMv > 0 ? Math.Max(0, displayVoltageMv - 12) : displayVoltageMv;
                    int vPct = displayVoltageMv > 0 ? PercentWithHysteresis(calibratedMv) : -1;
                    bool useVoltageAsMain = isZaopin && !displayCharging && vPct >= 0 && vPct <= 100;
                    int coulombPct = displayBattery;

                    string batteryTooltip = null;
                    if (displayVoltageMv > 0)
                    {
                        if (useVoltageAsMain)
                        {
                            displayBattery = vPct;
                            batteryTooltip = string.Format("电量: {0}% (电压法，与官方驱动一致)\r\n固件电量计: {1}% (中间区间会滞后)\r\n电池电压: {2}.{3:D3}V", displayBattery, coulombPct >= 0 ? coulombPct.ToString() : "--", displayVoltageMv / 1000, displayVoltageMv % 1000);
                        }
                        else if (foundBattery >= 0)
                        {
                            batteryTooltip = string.Format("电量: {0}% (固件电量计)\r\n电池电压: {1}.{2:D3}V · 电压换算参考 {3}%", displayBattery, displayVoltageMv / 1000, displayVoltageMv % 1000, vPct);
                        }
                        else
                        {
                            batteryTooltip = string.Format("电池电压: {0}.{1:D3}V (换算参考 {2}%)", displayVoltageMv / 1000, displayVoltageMv % 1000, vPct);
                        }
                    }

                    // A1 低电量校准采样：放电时每轮记录 电压/电压法/固件电量计，供事后对比 12mV 压降补偿是否够用。
                    // 只记放电（充电时电压被抬高，样本无意义），不写盘之外的任何副作用。
                    if (isZaopin && displayVoltageMv > 0 && !displayCharging)
                    {
                        #if A1_SAMPLE
                    // 低电量校准采样（默认关闭）：编译时加 /define:A1_SAMPLE 才输出。
                    // 常开会每轮查询写一行（10 秒一次），一天近万行，日志 1MB 轮转会把
                    // 真正的错误日志冲掉。2026-10-29 已完成校准，结论见 HANDOVER.md。
                    Diagnostics.Log(string.Format("[A1] mv={0} vPct={1} gauge={2} cal={3} raw={4}", displayVoltageMv, vPct, coulombPct, calibratedMv, VoltageToPercent(calibratedMv)));
#endif
                    }

                    // 缓存必须写「界面显示值」而不是固件电量计：两者在低电量段会分叉
                    // （实测固件计卡在 30% 长达 80 分钟，而电压法已降到 26%）。
                    // 否则冷启动首屏、外部读取注册表的程序都会拿到旧口径，与界面差数个百分点。
                    // 允许缓存 0%：原来条件写 displayBattery > 0，导致电量耗尽关机前
                    // 那几轮（0%）不写缓存，缓存永远停在最后一个正值（实测停在 1%）。
                    // 设备关机后查询失败会回退显示缓存值，于是界面上一直挂着 1%，
                    // 用户以为还有电，实际已经关机。
                    if (foundBattery >= 0 && displayBattery >= 0)
                    {
                        CachedBatteryPercent = displayBattery;
                        SaveHardwareCache();
                    }

                    return new MouseBatteryInfo
                    {
                        // 皂品 Z2 的 DeviceId 必须**不含 PID**：同一只鼠标在「有线直连(F526)」
                        // 与「2.4G 接收器(F527)」之间切换时 PID 会变，若 DeviceId 带 PID 就会被
                        // 当成两台设备——持久化的「选中设备」仍指向旧 PID，界面于是显示那条
                        // 幽灵条目（实测 2026-10-29：有线连接却显示 2.4G 无线、电量停在缓存值）。
                        DeviceId = isZaopin
                            ? (brandName + ":3554:Z2")
                            : (brandName + ":3554:" + (foundPid > 0 ? foundPid.ToString("X4") : "3554")),
                        Brand = brandName,
                        DeviceName = devName,
                        Category = DeviceCategory.Mouse,
                        Transport = transportText,
                        // 语义约定：IsConnected = 本次在线探测（cmd3）有应答；IsSleeping = 未应答（两者互补）。
                        // 需要判断「设备是否在位」请用 IsDonglePresent（2.4G 接收器在位时恒为 true）。
                        IsConnected = isMouseOnline,
                        IsSleeping = !isMouseOnline,
                        IsDonglePresent = true,
                        BatteryPercent = displayBattery,
                        ExactBatteryPercent = displayBattery >= 0 ? displayBattery : -1,
                        IsCharging = displayCharging,
                        BatteryVoltageMv = displayVoltageMv,
                        CustomTooltip = batteryTooltip,
                        LastUpdated = (foundBattery >= 0) ? DateTime.Now : (CachedLastUpdated != DateTime.MinValue ? CachedLastUpdated : DateTime.Now),
                        Dpi = currentDpi,
                        DpiStage = (foundStage >= 1 && foundStage <= dpiStages.Length) ? foundStage : 2,
                        DpiStageCount = dpiStages.Length,
                        DpiStages = dpiStages,
                        PollingRate = (foundPollingRate > 0) ? foundPollingRate : 1000,
                        MaxPollingRate = isZaopin ? 4000 : 8000,
                        SupportedPollingRates = pollRates
                    };
                }

                return null;
            }
        }
    }
}