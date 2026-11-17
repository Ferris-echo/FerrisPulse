// ============================================================================
// 模块名称: VxeNearLinkDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   VXE 蜻蜓 R1 星闪版 (NearLink) 接收器 (373B:1216) 电量与充电状态驱动：
//   通过 UsagePage 0xFF05 / Usage 0x0001 (In=64B / Out=64B) 厂商端点收发 BITMOUSE
//   63 字节帧 (Report ID 0x08)，支持 cmd 7 电量/电压读取与 cmd 15 (0x0F) 充电检测，
//   接入 DriverStateStore 休眠持久化缓存，与 ATK 公版 0x373B 鼠标严格隔离。
//   （特别鸣谢社区群友“冻干咸鱼”逆向 ATK HUB 3.2.30 并贡献完整协议实现）
// 依赖关系: HidTransport.cs, DriverStateStore.cs, HardwareDriverRegistry.cs, MouseBatteryInfo.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 修改命令字、帧布局或端点约束时必须同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止字符串插值、空条件运算符、nameof）。
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
        public const int TargetUsagePage = 0xFF05;
        public const int TargetUsage = 0x0001;
        public const int ReportLength = 64;

        public const byte FrameReportId = 0x08;
        public const byte FrameMagic = 0x72;
        public const byte TargetMouse = 0x01;
        public const byte CmdGetBatteryLevel = 0x07;
        public const byte CmdGetBatteryChargingStatus = 0x0F;

        public const string ModelName = "VXE Dragonfly R1 星闪版 (NearLink)";
        public const string DriverIdName = "BuiltIn:VxeNearLink";
        private const int ReadWindowMs = 1200;
        private const int QueryThrottleMs = 5000;

        public static bool IsSupportedVidPid(int vid, int pid)
        {
            return vid == VendorId && pid == ProductId;
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

            int sum = 0;
            for (int i = 2; i < ReportLength; i++)
            {
                sum += outBuf[i];
            }
            outBuf[1] = (byte)(sum & 0xFF);
            return outBuf;
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

        private static bool SendCommand(IntPtr handle, byte cmdId, byte target, out byte[] response)
        {
            response = null;
            if (!HidTransport.IsValidHandle(handle)) return false;

            int sn = Interlocked.Increment(ref cmdSn) & 0xFF;
            byte[] tx = BuildReport64(cmdId, target, sn);
            if (!HidTransport.SetOutputReport(handle, tx)) return false;

            Stopwatch sw = Stopwatch.StartNew();
            while (sw.ElapsedMilliseconds < ReadWindowMs)
            {
                int remain = Math.Max(1, ReadWindowMs - (int)sw.ElapsedMilliseconds);
                int slice = Math.Min(remain, 250);
                byte[] buf;
                if (!HidTransport.ReadReport(handle, ReportLength, slice, out buf)) continue;
                if (buf == null || buf.Length < 8) continue;
                if (buf[1] == FrameMagic && buf[5] == cmdId)
                {
                    response = buf;
                    return true;
                }
            }

            return false;
        }

        private static bool QueryBattery(string devicePath, out int batteryPercent, out bool isCharging, out int voltageMv)
        {
            batteryPercent = -1;
            isCharging = false;
            voltageMv = 0;

            IntPtr handle = HidTransport.Open(devicePath);
            if (!HidTransport.IsValidHandle(handle)) return false;
            try
            {
                byte[] rxBat;
                if (!SendCommand(handle, CmdGetBatteryLevel, TargetMouse, out rxBat)) return false;
                if (!ParseBatteryResponse(rxBat, rxBat.Length, out batteryPercent, out voltageMv)) return false;

                byte[] rxChg;
                if (SendCommand(handle, CmdGetBatteryChargingStatus, TargetMouse, out rxChg))
                {
                    ParseChargingResponse(rxChg, rxChg.Length, out isCharging);
                }
                return true;
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

        public static List<MouseBatteryInfo> QueryAllDevices()
        {
            lock (hidLock)
            {
                if ((DateTime.Now - lastQuery).TotalMilliseconds < QueryThrottleMs && cachedList.Count > 0)
                {
                    return new List<MouseBatteryInfo>(cachedList);
                }

                List<MouseBatteryInfo> result = new List<MouseBatteryInfo>();
                List<HidDeviceDescriptor> descriptors = HidTransport.Enumerate(VendorId, ProductId);
                string deviceId = string.Format("VXE:{0:X4}:{1:X4}", VendorId, ProductId);
                string fingerprint = string.Format("HID:{0:X4}:{1:X4}", VendorId, ProductId);

                for (int i = 0; i < descriptors.Count; i++)
                {
                    HidDeviceDescriptor desc = descriptors[i];
                    if (!IsNearLinkEndpoint(desc)) continue;
                    if (HardwareDriverRegistry.IsExternallyOwnedPath(desc.DevicePath)) continue;

                    int batt;
                    bool chg;
                    int mv;
                    bool ok = QueryBattery(desc.DevicePath, out batt, out chg, out mv);

                    int cachedBatt;
                    bool cachedChg;
                    string cachedName;
                    DateTime cachedUpdated;
                    bool hasCache = DriverStateStore.TryLoadBatteryState(deviceId, out cachedBatt, out cachedChg, out cachedName, out cachedUpdated);
                    if (!hasCache)
                    {
                        hasCache = DriverStateStore.TryLoadBatteryState(string.Format("Gaming:{0:X4}:{1:X4}", VendorId, ProductId), out cachedBatt, out cachedChg, out cachedName, out cachedUpdated);
                    }

                    if (ok && batt >= 0 && batt <= 100)
                    {
                        DriverStateStore.SaveBatteryState(deviceId, batt, chg, ModelName);
                    }

                    MouseBatteryInfo info = new MouseBatteryInfo();
                    info.DeviceId = deviceId;
                    info.HardwareFingerprint = fingerprint;
                    info.HardwareId = desc.InstanceId;
                    info.DriverId = DriverIdName;
                    info.DeviceName = ModelName;
                    info.Brand = "VXE";
                    info.Category = DeviceCategory.Mouse;
                    info.Transport = "星闪 NearLink 2.4G";
                    info.IsDonglePresent = true;
                    info.IsConnected = ok;
                    info.IsSleeping = !ok;
                    info.IsCharging = ok ? chg : (hasCache ? cachedChg : false);
                    info.BatteryPercent = ok ? batt : (hasCache ? cachedBatt : -1);
                    info.ExactBatteryPercent = info.BatteryPercent >= 0 ? info.BatteryPercent : 0;
                    info.BatteryVoltageMv = ok ? mv : 0;
                    info.LastUpdated = ok ? DateTime.Now : (hasCache ? cachedUpdated : DateTime.Now);
                    info.MaxPollingRate = 8000;
                    info.SupportedPollingRates = new int[] { 1000, 2000, 4000, 8000 };
                    info.Capabilities.SupportsBattery = true;
                    info.Capabilities.SupportsDpi = false;
                    info.Capabilities.SupportsDpiStages = false;
                    info.Capabilities.SupportsPollingRate = false;
                    info.Capabilities.SupportsHardwareDpiMonitor = false;
                    info.Capabilities.SupportsXyIndependence = false;

                    result.Add(info);
                    break;
                }

                lastQuery = DateTime.Now;
                cachedList.Clear();
                cachedList.AddRange(result);
                return result;
            }
        }
    }
}
