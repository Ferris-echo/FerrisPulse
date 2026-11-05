// ============================================================================
// 模块名称: RapooVt1851DeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   雷柏 VT 系列 2.4G 无线鼠标（接收器 VID 0x24AE / PID 0x1851）的私有协议适配。
//   使用 0xBA 32 字节厂商报文读取电量/充电状态、7 档 DPI 表与回报率，
//   并通过同一报文写入 DPI 档位/数值和 125-8000Hz 回报率。
//   本型号没有 Usage 0x0F Feature 集合，因此不能复用 RapooDeviceHelper 的 Feature 通道。
// 依赖关系: 仅 .NET 4.0 核心程序集 + hid.dll / kernel32.dll / setupapi.dll (经 HidTransport)
//
// 协议来源与实测记录:
//   Linux hid-rapoo (AUR hid-rapoo-dkms) 0xBB 电量帧格式；
//   akvadrako "Rapoo Mice Device Notes" 与 mousectl rapoo_vt3pro 的 0xBA EEPROM 帧格式。
//   本文件中的地址/长度/编码均已在 24AE:1851 实机上读写回环验证。
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止字符串插值、空条件运算符、nameof）。
// 2. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// 3. [物理事实优先]: 只有实机验证过的能力才能置 true；本型号 Y 轴 DPI 表为 0xFF，禁止伪造双轴。
// 4. [报文时序]: 0x02 表示设备忙，其 payload 无意义；写入后必须留足 1.5s 再回读校验。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;

namespace RazerBatteryTray
{
    public static class RapooVt1851DeviceHelper
    {
        public const int VendorId = 0x24AE;
        public const int ProductId = 0x1851;
        public const string DeviceIdValue = "RapooVT:24AE:1851";
        public const string HardwareFingerprintValue = "HID:24AE:1851";
        public const string DriverIdValue = "BuiltIn:RapooVt1851Mouse";

        private const int ControlUsagePage = 0xFF00;
        private const int ControlUsage = 0x0E;
        private const int StatusUsagePage = 0xFF00;
        private const int StatusUsage = 0x02;
        private const int ReportLength = 32;

        private const byte FrameReportId = 0xBA;
        private const byte LinkDongle = 0xA5;
        private const byte CmdRead = 0xA4;
        private const byte CmdWrite = 0xA5;
        private const byte CmdBattery = 0xAA;

        private const int AddrPolling = 0x880;
        private const int AddrDpiBlock = 0x888;
        private const int DpiBlockLength = 20;

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_SHARE_READ = 0x00000001;
        private const uint FILE_SHARE_WRITE = 0x00000002;
        private const uint OPEN_EXISTING = 3;

        private static string controlPath = null;
        private static string statusPath = null;

        private static int lastBatteryPercent = -1;
        private static int lastChargeState = -1;
        private static int lastDpi = 0;
        private static int lastDpiCount = 0;
        private static int lastDpiStage = 0;
        private static int lastPollingRate = 0;
        private static int[] lastDpiStages = null;
        private static DateTime lastConfigRead = DateTime.MinValue;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr CreateFileW(string lpFileName, uint dwDesiredAccess, uint dwShareMode,
            IntPtr lpSecurityAttributes, uint dwCreationDisposition, uint dwFlagsAndAttributes, IntPtr hTemplateFile);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool WriteFile(IntPtr hFile, byte[] lpBuffer, uint nBytesToWrite,
            out uint lpNumberOfBytesWritten, IntPtr lpOverlapped);

        [DllImport("hid.dll", SetLastError = true)]
        private static extern bool HidD_GetInputReport(IntPtr HidDeviceObject, byte[] ReportBuffer, uint ReportBufferLength);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool CloseHandle(IntPtr hObject);

        private static bool IsValidHandle(IntPtr handle)
        {
            return handle != IntPtr.Zero && handle != new IntPtr(-1);
        }

        public static bool IsSupportedDevice(int vid, int pid)
        {
            return vid == VendorId && pid == ProductId;
        }

        public static void PopulateCapabilities(MouseBatteryInfo dev)
        {
            if (dev == null) return;
            if (dev.Capabilities == null) dev.Capabilities = new DeviceCapabilities();

            dev.Capabilities.SupportsBattery = true;
            dev.Capabilities.SupportsDpi = true;
            dev.Capabilities.SupportsDpiStages = true;
            dev.Capabilities.SupportsPollingRate = true;
            dev.Capabilities.SupportsXyIndependence = false;
            dev.Capabilities.SupportsHardwareDpiMonitor = false;
            dev.Capabilities.MinDpi = 50;
            dev.Capabilities.MaxDpi = 26000;
            dev.Capabilities.DpiStep = 50;
            dev.Capabilities.SupportedPollingRates = new int[] { 125, 250, 500, 1000, 2000, 4000, 8000 };
            dev.Capabilities.SensorDescription = "雷柏 VT 系列 2.4G 无线光学传感器（50 DPI 步进，最高 26,000 DPI）";
            dev.Capabilities.ThemeColorHex = "#00B9FF";
            dev.Capabilities.PerformanceCardTitle = "雷柏 VT 2.4G";
        }

        // --------------------------------------------------------------------
        // 设备发现与端点隔离
        // --------------------------------------------------------------------
        private static void RefreshEndpoints()
        {
            controlPath = null;
            statusPath = null;

            List<HidDeviceDescriptor> list = HidTransport.Enumerate(VendorId, ProductId);
            if (list == null) return;

            for (int i = 0; i < list.Count; i++)
            {
                HidDeviceDescriptor d = list[i];
                if (d == null) continue;

                if (d.UsagePage == ControlUsagePage && d.Usage == ControlUsage &&
                    d.OutputReportLength == ReportLength && d.InputReportLength == ReportLength)
                {
                    controlPath = d.DevicePath;
                }
                else if (d.UsagePage == StatusUsagePage && d.Usage == StatusUsage &&
                    d.InputReportLength == 7)
                {
                    statusPath = d.DevicePath;
                }
            }
        }

        private static bool EnsureEndpoints()
        {
            if (string.IsNullOrEmpty(controlPath)) RefreshEndpoints();
            return !string.IsNullOrEmpty(controlPath);
        }

        // --------------------------------------------------------------------
        // 0xBA 厂商报文收发
        // --------------------------------------------------------------------
        private static bool Transact(byte[] frame, int waitMs, out byte[] response)
        {
            response = null;
            if (frame == null || frame.Length > ReportLength) return false;
            if (string.IsNullOrEmpty(controlPath)) return false;

            IntPtr handle = CreateFileW(controlPath, GENERIC_READ | GENERIC_WRITE,
                FILE_SHARE_READ | FILE_SHARE_WRITE, IntPtr.Zero, OPEN_EXISTING, 0, IntPtr.Zero);
            if (!IsValidHandle(handle))
            {
                controlPath = null;
                return false;
            }

            try
            {
                byte[] output = new byte[ReportLength];
                Array.Copy(frame, output, frame.Length);

                uint written = 0;
                if (!WriteFile(handle, output, (uint)output.Length, out written, IntPtr.Zero))
                {
                    controlPath = null;
                    return false;
                }

                if (waitMs > 0) Thread.Sleep(waitMs);

                byte[] input = new byte[ReportLength];
                input[0] = FrameReportId;
                if (!HidD_GetInputReport(handle, input, (uint)input.Length)) return false;

                response = input;
                return true;
            }
            catch
            {
                return false;
            }
            finally
            {
                CloseHandle(handle);
            }
        }

        private static bool ReadRegister(int address, int length, out byte[] data)
        {
            data = null;
            if (length <= 0 || length > ReportLength - 8) return false;

            byte[] frame = new byte[8];
            frame[0] = FrameReportId;
            frame[1] = LinkDongle;
            frame[2] = CmdRead;
            frame[3] = (byte)length;
            frame[4] = (byte)(address & 0xFF);
            frame[5] = (byte)((address >> 8) & 0xFF);
            frame[6] = (byte)((address >> 16) & 0xFF);
            frame[7] = (byte)((address >> 24) & 0xFF);

            for (int attempt = 0; attempt < 4; attempt++)
            {
                byte[] response;
                if (Transact(frame, 250, out response) && response != null &&
                    response[0] == 0x01 && response.Length >= 4 + length)
                {
                    byte[] payload = new byte[length];
                    Array.Copy(response, 4, payload, 0, length);
                    data = payload;
                    return true;
                }
                Thread.Sleep(350);
            }
            return false;
        }

        private static bool WriteRegister(int address, byte[] data)
        {
            if (data == null || data.Length == 0 || data.Length > ReportLength - 8) return false;

            byte[] frame = new byte[8 + data.Length];
            frame[0] = FrameReportId;
            frame[1] = LinkDongle;
            frame[2] = CmdWrite;
            frame[3] = (byte)data.Length;
            frame[4] = (byte)(address & 0xFF);
            frame[5] = (byte)((address >> 8) & 0xFF);
            frame[6] = (byte)((address >> 16) & 0xFF);
            frame[7] = (byte)((address >> 24) & 0xFF);
            Array.Copy(data, 0, frame, 8, data.Length);

            byte[] response;
            // 写入提交到 EEPROM 需要时间；这里只负责发帧，调用方必须回读校验。
            return Transact(frame, 1600, out response);
        }

        private static bool ReadBattery(out int percent, out int chargeState)
        {
            percent = -1;
            chargeState = -1;

            byte[] frame = new byte[] { FrameReportId, LinkDongle, CmdBattery, 0x00 };
            for (int attempt = 0; attempt < 4; attempt++)
            {
                byte[] response;
                if (Transact(frame, 250, out response))
                {
                    int p;
                    int s;
                    if (TryParseBatteryResponse(response, out p, out s))
                    {
                        percent = p;
                        chargeState = s;
                        return true;
                    }
                }
                Thread.Sleep(350);
            }
            return false;
        }

        // --------------------------------------------------------------------
        // 公开解析器（供自动化回放测试）
        // --------------------------------------------------------------------
        public static bool TryParseBatteryResponse(byte[] response, out int percent, out int chargeState)
        {
            percent = -1;
            chargeState = -1;
            if (response == null || response.Length < 3) return false;
            if (response[0] != 0x01) return false;

            int p = response[2];
            int s = response[1];
            if (p < 0 || p > 100) return false;

            percent = p;
            chargeState = s;
            return true;
        }

        public static bool TryParseDpiBlock(byte[] block, out int[] stages, out int count, out int activeIndex)
        {
            stages = null;
            count = 0;
            activeIndex = 0;
            if (block == null || block.Length < DpiBlockLength) return false;

            int c = block[14] + 1;
            if (c < 1 || c > 7) return false;

            int[] parsed = new int[c];
            for (int i = 0; i < c; i++)
            {
                int raw = block[i * 2] | (block[i * 2 + 1] << 8);
                if (raw <= 0) return false;
                int dpi = raw * 50;
                if (dpi < 50 || dpi > 26000) return false;
                parsed[i] = dpi;
            }

            int idx = block[16];
            if (idx >= c) idx = c - 1;
            if (idx < 0) idx = 0;

            stages = parsed;
            count = c;
            activeIndex = idx;
            return true;
        }

        public static byte EncodePollingRate(int hz)
        {
            switch (hz)
            {
                case 125: return 0x08;
                case 250: return 0x04;
                case 500: return 0x02;
                case 1000: return 0x01;
                case 2000: return 0x84;
                case 4000: return 0x82;
                case 8000: return 0x83;
                default: return 0;
            }
        }

        public static int DecodePollingRate(byte code)
        {
            switch (code)
            {
                case 0x08: return 125;
                case 0x04: return 250;
                case 0x02: return 500;
                case 0x01: return 1000;
                case 0x84: return 2000;
                case 0x82: return 4000;
                case 0x83: return 8000;
                case 0x81: return 8000;
                default: return 0;
            }
        }

        // --------------------------------------------------------------------
        // 设备信息与实时查询
        // --------------------------------------------------------------------
        public static MouseBatteryInfo QueryDevice()
        {
            RefreshEndpoints();
            if (string.IsNullOrEmpty(controlPath)) return null;

            MouseBatteryInfo info = new MouseBatteryInfo();
            info.DeviceId = DeviceIdValue;
            info.DriverId = DriverIdValue;
            info.Brand = "Rapoo";
            info.Category = DeviceCategory.Mouse;
            info.DeviceName = "雷柏 VT 系列 2.4G 无线鼠标 (24AE:1851)";
            info.Transport = "2.4G 无线";
            info.HardwareFingerprint = HardwareFingerprintValue;
            info.HardwareId = "HID\\VID_24AE&PID_1851";
            info.PriorityIndex = 20;
            info.IsCustom = false;
            info.IsConnected = true;
            info.IsDonglePresent = true;
            info.IsPrimary = false;
            info.BatteryVoltageMv = 0;

            PopulateCapabilities(info);
            info.SupportedPollingRates = new int[] { 125, 250, 500, 1000, 2000, 4000, 8000 };
            info.MaxPollingRate = 8000;
            info.SupportsPollingRateWrite = true;

            int percent;
            int chargeState;
            bool batteryOk = ReadBattery(out percent, out chargeState);

            if (batteryOk)
            {
                lastBatteryPercent = percent;
                lastChargeState = chargeState;
                info.BatteryPercent = percent;
                info.ExactBatteryPercent = percent;
                info.IsCharging = (chargeState == 2);
                info.IsChargingUnknown = (chargeState != 1 && chargeState != 2);
                info.IsSleeping = false;
            }
            else
            {
                info.BatteryPercent = lastBatteryPercent;
                info.ExactBatteryPercent = lastBatteryPercent;
                info.IsCharging = (lastChargeState == 2);
                info.IsChargingUnknown = (lastChargeState != 1 && lastChargeState != 2);
                info.IsSleeping = true;
            }

            if (batteryOk)
            {
                RefreshConfiguration(info);
            }
            else
            {
                ApplyCachedConfiguration(info);
            }

            info.LastUpdated = DateTime.Now;
            return info;
        }

        private static void RefreshConfiguration(MouseBatteryInfo info)
        {
            byte[] polling;
            if (ReadRegister(AddrPolling, 4, out polling) && polling != null)
            {
                int hz = DecodePollingRate(polling[0]);
                if (hz > 0)
                {
                    lastPollingRate = hz;
                    if (info != null)
                    {
                        info.PollingRate = hz;
                        info.MaxPollingRate = 8000;
                    }
                }
            }

            byte[] block;
            if (ReadRegister(AddrDpiBlock, DpiBlockLength, out block) && block != null)
            {
                int[] stages;
                int count;
                int activeIndex;
                if (TryParseDpiBlock(block, out stages, out count, out activeIndex))
                {
                    lastDpiStages = stages;
                    lastDpiCount = count;
                    lastDpiStage = activeIndex + 1;
                    lastDpi = stages[activeIndex];
                    lastConfigRead = DateTime.Now;

                    if (info != null)
                    {
                        info.Dpi = stages[activeIndex];
                        info.DpiY = stages[activeIndex];
                        info.DpiStage = activeIndex + 1;
                        info.DpiStageCount = count;
                        info.DpiStages = stages;
                        info.DpiStagesY = stages;
                        info.IsXyIndependent = false;
                        info.XyRatio = 1.0;
                    }
                }
            }
        }

        private static void ApplyCachedConfiguration(MouseBatteryInfo info)
        {
            if (info == null) return;
            if (lastDpiStages != null && lastDpiCount > 0)
            {
                int idx = lastDpiStage - 1;
                if (idx < 0) idx = 0;
                if (idx >= lastDpiStages.Length) idx = lastDpiStages.Length - 1;
                info.Dpi = lastDpiStages[idx];
                info.DpiY = lastDpiStages[idx];
                info.DpiStage = idx + 1;
                info.DpiStageCount = lastDpiCount;
                info.DpiStages = lastDpiStages;
                info.DpiStagesY = lastDpiStages;
                info.IsXyIndependent = false;
                info.XyRatio = 1.0;
            }
            info.PollingRate = lastPollingRate;
            info.MaxPollingRate = 8000;
        }

        public static bool FastQueryDpi(MouseBatteryInfo dev, out int dpiX, out int dpiY, out int stage, out int count)
        {
            dpiX = 0;
            dpiY = 0;
            stage = 0;
            count = 0;

            if (lastDpiStages != null && lastDpiCount > 0 &&
                (DateTime.Now - lastConfigRead).TotalSeconds < 3.0)
            {
                dpiX = lastDpi;
                dpiY = lastDpi;
                stage = lastDpiStage;
                count = lastDpiCount;
                return true;
            }

            if (!EnsureEndpoints()) return false;

            byte[] block;
            if (!ReadRegister(AddrDpiBlock, DpiBlockLength, out block) || block == null) return false;

            int[] stages;
            int c;
            int activeIndex;
            if (!TryParseDpiBlock(block, out stages, out c, out activeIndex)) return false;

            lastDpiStages = stages;
            lastDpiCount = c;
            lastDpiStage = activeIndex + 1;
            lastDpi = stages[activeIndex];
            lastConfigRead = DateTime.Now;

            dpiX = lastDpi;
            dpiY = lastDpi;
            stage = lastDpiStage;
            count = c;

            if (dev != null)
            {
                dev.Dpi = dpiX;
                dev.DpiY = dpiY;
                dev.DpiStage = stage;
                dev.DpiStageCount = count;
                dev.DpiStages = stages;
                dev.DpiStagesY = stages;
            }
            return true;
        }

        // --------------------------------------------------------------------
        // 写入：DPI / 档位表 / 回报率
        // --------------------------------------------------------------------
        public static bool SetDpi(MouseBatteryInfo dev, int dpiX, int dpiY, int stage)
        {
            if (!EnsureEndpoints()) return false;
            if (dpiY > 0 && dpiY != dpiX) return false; // 本型号无 Y 轴 DPI 表

            byte[] original;
            if (!ReadRegister(AddrDpiBlock, DpiBlockLength, out original) || original == null) return false;

            int[] stages;
            int count;
            int activeIndex;
            if (!TryParseDpiBlock(original, out stages, out count, out activeIndex)) return false;

            int targetStage = stage;
            if (targetStage < 1) targetStage = activeIndex + 1;
            if (targetStage < 1 || targetStage > count) return false;

            int targetDpi = ClampDpi(dpiX);
            if (targetDpi <= 0) return false;

            byte[] changed = (byte[])original.Clone();
            int raw = targetDpi / 50;
            int offset = (targetStage - 1) * 2;
            changed[offset] = (byte)(raw & 0xFF);
            changed[offset + 1] = (byte)((raw >> 8) & 0xFF);
            changed[16] = (byte)(targetStage - 1);

            if (!WriteRegister(AddrDpiBlock, changed)) return false;
            Thread.Sleep(300);

            byte[] verify;
            if (!ReadRegister(AddrDpiBlock, DpiBlockLength, out verify) || verify == null)
            {
                WriteRegister(AddrDpiBlock, original);
                return false;
            }

            int[] verifyStages;
            int verifyCount;
            int verifyActive;
            if (!TryParseDpiBlock(verify, out verifyStages, out verifyCount, out verifyActive) ||
                verifyStages[targetStage - 1] != targetDpi || verifyActive != targetStage - 1)
            {
                WriteRegister(AddrDpiBlock, original);
                return false;
            }

            lastDpiStages = verifyStages;
            lastDpiCount = verifyCount;
            lastDpiStage = verifyActive + 1;
            lastDpi = verifyStages[verifyActive];
            lastConfigRead = DateTime.Now;

            if (dev != null)
            {
                dev.Dpi = lastDpi;
                dev.DpiY = lastDpi;
                dev.DpiStage = lastDpiStage;
                dev.DpiStageCount = verifyCount;
                dev.DpiStages = verifyStages;
                dev.DpiStagesY = verifyStages;
                dev.IsXyIndependent = false;
                dev.XyRatio = 1.0;
            }
            return true;
        }

        public static bool UpdateHardwareStages(MouseBatteryInfo dev, int activeStage, int[] stagesX, int[] stagesY)
        {
            if (!EnsureEndpoints()) return false;
            if (stagesX == null || stagesX.Length == 0 || stagesX.Length > 7) return false;

            if (stagesY != null)
            {
                int check = Math.Min(stagesY.Length, stagesX.Length);
                for (int i = 0; i < check; i++)
                {
                    if (stagesY[i] != stagesX[i]) return false; // 不支持独立 Y 轴
                }
            }

            byte[] original;
            if (!ReadRegister(AddrDpiBlock, DpiBlockLength, out original) || original == null) return false;

            int count = stagesX.Length;
            int[] encoded = new int[count];
            for (int i = 0; i < count; i++)
            {
                int dpi = ClampDpi(stagesX[i]);
                if (dpi <= 0) return false;
                encoded[i] = dpi;
            }

            byte[] changed = (byte[])original.Clone();
            for (int i = 0; i < count; i++)
            {
                int raw = encoded[i] / 50;
                changed[i * 2] = (byte)(raw & 0xFF);
                changed[i * 2 + 1] = (byte)((raw >> 8) & 0xFF);
            }
            changed[14] = (byte)(count - 1);
            changed[15] = 0x00;

            int targetIndex = activeStage - 1;
            if (targetIndex < 0) targetIndex = 0;
            if (targetIndex >= count) targetIndex = count - 1;
            changed[16] = (byte)targetIndex;

            if (!WriteRegister(AddrDpiBlock, changed)) return false;
            Thread.Sleep(300);

            byte[] verify;
            if (!ReadRegister(AddrDpiBlock, DpiBlockLength, out verify) || verify == null)
            {
                WriteRegister(AddrDpiBlock, original);
                return false;
            }

            int[] verifyStages;
            int verifyCount;
            int verifyActive;
            if (!TryParseDpiBlock(verify, out verifyStages, out verifyCount, out verifyActive) ||
                verifyCount != count || verifyActive != targetIndex)
            {
                WriteRegister(AddrDpiBlock, original);
                return false;
            }
            for (int i = 0; i < count; i++)
            {
                if (verifyStages[i] != encoded[i])
                {
                    WriteRegister(AddrDpiBlock, original);
                    return false;
                }
            }

            lastDpiStages = verifyStages;
            lastDpiCount = verifyCount;
            lastDpiStage = verifyActive + 1;
            lastDpi = verifyStages[verifyActive];
            lastConfigRead = DateTime.Now;

            if (dev != null)
            {
                dev.Dpi = lastDpi;
                dev.DpiY = lastDpi;
                dev.DpiStage = lastDpiStage;
                dev.DpiStageCount = verifyCount;
                dev.DpiStages = verifyStages;
                dev.DpiStagesY = verifyStages;
                dev.IsXyIndependent = false;
                dev.XyRatio = 1.0;
            }
            return true;
        }

        public static bool SetPollingRate(MouseBatteryInfo dev, int rateHz)
        {
            if (!EnsureEndpoints()) return false;
            byte code = EncodePollingRate(rateHz);
            if (code == 0) return false;

            byte[] original;
            if (!ReadRegister(AddrPolling, 4, out original) || original == null) return false;

            byte[] changed = (byte[])original.Clone();
            changed[0] = code;
            if (!WriteRegister(AddrPolling, changed)) return false;
            Thread.Sleep(300);

            byte[] verify;
            if (!ReadRegister(AddrPolling, 4, out verify) || verify == null || verify[0] != code)
            {
                WriteRegister(AddrPolling, original);
                return false;
            }

            lastPollingRate = rateHz;
            if (dev != null)
            {
                dev.PollingRate = rateHz;
                dev.MaxPollingRate = 8000;
            }
            return true;
        }

        private static int ClampDpi(int dpi)
        {
            if (dpi < 50) return 0;
            int rounded = ((dpi + 25) / 50) * 50;
            if (rounded < 50) rounded = 50;
            if (rounded > 26000) rounded = 26000;
            return rounded;
        }
    }
}
