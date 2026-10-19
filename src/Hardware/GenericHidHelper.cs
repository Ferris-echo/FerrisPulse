// ============================================================================
// 模块名称: GenericHidHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   通用标准 USB HID 电池报告规范 (HID Usage Page 0x59 / 0x85) 解析
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
    public static class GenericHidHelper
    {
        public static bool IsDeviceConnected(string hidFingerprint)
        {
            if (string.IsNullOrEmpty(hidFingerprint)) return false;
            try
            {
                Guid hidGuid;
                RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
                IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return false;

                RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(ifData);
                uint memberIdx = 0;

                string target = hidFingerprint.Replace("HID:", "").ToLower();
                string[] parts = target.Split(':');
                string pattern = (parts.Length >= 2) ? string.Format("vid_{0}&pid_{1}", parts[0], parts[1]) : target;

                try
                {
                    while (RazerDeviceHelper.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                    {
                        uint reqSize;
                        RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                        IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                        if (RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                        {
                            IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                            string devicePath = Marshal.PtrToStringAuto(pDevicePath);
                            if (devicePath != null && devicePath.ToLower().Contains(pattern))
                            {
                                Marshal.FreeHGlobal(detailBuffer);
                                return true;
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
            }
            catch { }
            return false;
        }

        public static bool QueryHidBattery(string hidFingerprint, out int batteryPercent, out bool isCharging)
        {
            batteryPercent = -1;
            isCharging = false;
            if (string.IsNullOrEmpty(hidFingerprint)) return false;
            try
            {
                Guid hidGuid;
                RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
                IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return false;

                RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(ifData);
                uint memberIdx = 0;

                string target = hidFingerprint.Replace("HID:", "").ToLower();
                string[] parts = target.Split(':');
                string pattern = (parts.Length >= 2) ? string.Format("vid_{0}&pid_{1}", parts[0], parts[1]) : target;

                try
                {
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
                            if (devicePath != null && devicePath.ToLower().Contains(pattern))
                            {
                                IntPtr handle = RazerDeviceHelper.CreateFile(devicePath, 0, RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE, IntPtr.Zero, RazerDeviceHelper.OPEN_EXISTING, 0, IntPtr.Zero);
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

                                            // 1. USB-IF Standard Battery System (UsagePage 0x85)
                                            if (caps.UsagePage == 0x85)
                                            {
                                                if (caps.FeatureReportByteLength > 1)
                                                {
                                                    byte[] fBuf = new byte[caps.FeatureReportByteLength];
                                                    if (RazerDeviceHelper.HidD_GetFeature(handle, fBuf, fBuf.Length))
                                                    {
                                                        for (int i = 1; i < fBuf.Length; i++)
                                                        {
                                                            if (fBuf[i] > 0 && fBuf[i] <= 100)
                                                            {
                                                                batteryPercent = fBuf[i];
                                                                Marshal.FreeHGlobal(detailBuffer);
                                                                return true;
                                                            }
                                                        }
                                                    }
                                                }
                                                if (caps.InputReportByteLength > 1)
                                                {
                                                    byte[] inBuf = new byte[caps.InputReportByteLength];
                                                    if (RazerDeviceHelper.HidD_GetInputReport(handle, inBuf, inBuf.Length))
                                                    {
                                                        for (int i = 1; i < inBuf.Length; i++)
                                                        {
                                                            if (inBuf[i] > 0 && inBuf[i] <= 100)
                                                            {
                                                                batteryPercent = inBuf[i];
                                                                Marshal.FreeHGlobal(detailBuffer);
                                                                return true;
                                                            }
                                                        }
                                                    }
                                                }
                                            }

                                            // 2. Actions ATS / Beken vendor reports (UsagePage 0xFF00, 0xFFA0, 0x0B)
                                            if (caps.UsagePage == 0xFF00 || caps.UsagePage == 0xFFA0 || caps.UsagePage == 0x0B)
                                            {
                                                if (caps.FeatureReportByteLength > 2)
                                                {
                                                    byte[] fBuf = new byte[caps.FeatureReportByteLength];
                                                    fBuf[0] = 0x04;
                                                    if (RazerDeviceHelper.HidD_GetFeature(handle, fBuf, fBuf.Length))
                                                    {
                                                        int candidate = fBuf[1] > 0 && fBuf[1] <= 100 ? fBuf[1] : (fBuf[2] > 0 && fBuf[2] <= 100 ? fBuf[2] : -1);
                                                        if (candidate > 0)
                                                        {
                                                            batteryPercent = candidate;
                                                            Marshal.FreeHGlobal(detailBuffer);
                                                            return true;
                                                        }
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
            }
            catch { }
            return false;
        }

        public static List<CustomDeviceConfig> ScanGenericHidMice(HashSet<string> existingIds)
        {
            var result = new List<CustomDeviceConfig>();
            try
            {
                Guid hidGuid;
                RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
                IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return result;

                RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(ifData);
                uint memberIdx = 0;
                var seenPnP = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                try
                {
                    while (RazerDeviceHelper.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                    {
                        uint reqSize;
                        RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                        IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);

                        if (RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                        {
                            IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                            string devicePath = Marshal.PtrToStringAuto(pDevicePath);
                            if (devicePath != null)
                            {
                                string lower = devicePath.ToLower();
                                // Skip known handled or internal non-mouse devices
                                if (lower.Contains("vid_046d") || lower.Contains("vid_1532") || lower.Contains("vid_24ae") || lower.Contains("vid_3554") ||
                                    lower.Contains("vid_06cb") || lower.Contains("vid_ffaa") || lower.Contains("vid_048d") ||
                                    lower.Contains("vid_17ef") || lower.Contains("syna") || lower.Contains("tencent") || lower.Contains("virtual") ||
                                    ValkyrieDeviceHelper.IsValkyrieDevice(lower))
                                {
                                    Marshal.FreeHGlobal(detailBuffer);
                                    ifData.cbSize = Marshal.SizeOf(ifData);
                                    continue;
                                }

                                IntPtr handle = RazerDeviceHelper.CreateFile(devicePath, 0, RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE, IntPtr.Zero, RazerDeviceHelper.OPEN_EXISTING, 0, IntPtr.Zero);
                                if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                {
                                    try
                                    {
                                        IntPtr preparsed;
                                        if (RazerDeviceHelper.HidD_GetPreparsedData(handle, out preparsed))
                                        {
                                            StringBuilder sbProd = new StringBuilder(256);
                                            string prod = RazerDeviceHelper.HidD_GetProductString(handle, sbProd, sbProd.Capacity) ? sbProd.ToString().Trim() : "";

                                            RazerDeviceHelper.HIDP_CAPS caps;
                                            RazerDeviceHelper.HidP_GetCaps(preparsed, out caps);
                                            RazerDeviceHelper.HidD_FreePreparsedData(preparsed);

                                            bool isGamepad = (caps.UsagePage == 0x01 && (caps.Usage == 0x04 || caps.Usage == 0x05)) ||
                                                             (!string.IsNullOrEmpty(prod) && (prod.ToLower().Contains("gamepad") || prod.ToLower().Contains("controller") || prod.ToLower().Contains("joystick") || prod.Contains("手柄")));

                                            bool isHeadset = (caps.UsagePage == 0x0C && !string.IsNullOrEmpty(prod) && (prod.ToLower().Contains("head") || prod.ToLower().Contains("audio") || prod.ToLower().Contains("ear") || prod.ToLower().Contains("sound") || prod.Contains("耳机"))) ||
                                                             (caps.UsagePage == 0x0B) || (caps.UsagePage == 0x85);

                                            bool isDongle = (!string.IsNullOrEmpty(prod) && (prod.ToLower().Contains("dongle") || prod.ToLower().Contains("receiver") || prod.Contains("接收器")));

                                            bool isKeyboard = (caps.UsagePage == 0x01 && caps.Usage == 0x06) ||
                                                              (!string.IsNullOrEmpty(prod) && (
                                                                  prod.ToLower().Contains("keyboard") ||
                                                                  prod.ToLower().Contains("nuphy") ||
                                                                  prod.ToLower().Contains("keychron") ||
                                                                  prod.ToLower().Contains("halo") ||
                                                                  prod.ToLower().Contains("air75") ||
                                                                  prod.ToLower().Contains("air96") ||
                                                                  prod.ToLower().Contains("air60") ||
                                                                  prod.ToLower().Contains("wh80") ||
                                                                  prod.Contains("键盘")
                                                              ));

                                            bool isMouse = (caps.UsagePage == 0x01 && caps.Usage == 0x02) ||
                                                           (!string.IsNullOrEmpty(prod) && (prod.ToLower().Contains("mouse") || prod.Contains("鼠标")));

                                            if (isGamepad || isHeadset || isDongle || isKeyboard || isMouse)
                                            {
                                                RazerDeviceHelper.HIDD_ATTRIBUTES attr = new RazerDeviceHelper.HIDD_ATTRIBUTES();
                                                attr.Size = Marshal.SizeOf(attr);
                                                RazerDeviceHelper.HidD_GetAttributes(handle, ref attr);

                                                string key = string.Format("{0:X4}:{1:X4}", attr.VendorID, attr.ProductID);
                                                string prodStr = !string.IsNullOrEmpty(prod) ? prod : "HID 兼容外设";

                                                DeviceCategory devCat = DeviceCategory.Generic;
                                                if (isGamepad) devCat = DeviceCategory.Gamepad;
                                                else if (isHeadset) devCat = DeviceCategory.Headset;
                                                else if (isDongle) devCat = DeviceCategory.Dongle;
                                                else if (isKeyboard) devCat = DeviceCategory.Keyboard;
                                                else if (isMouse) devCat = DeviceCategory.Mouse;

                                                var existing = result.Find(r => r.DeviceId == "HID:" + key);
                                                if (existing != null)
                                                {
                                                    if (devCat != DeviceCategory.Generic && existing.Category == DeviceCategory.Generic)
                                                    {
                                                        existing.Category = devCat;
                                                    }
                                                }
                                                else if (!seenPnP.Contains(key))
                                                {
                                                    seenPnP.Add(key);
                                                    if (string.IsNullOrEmpty(prodStr))
                                                    {
                                                        if (isGamepad) prodStr = string.Format("USB 游戏手柄 ({0})", key);
                                                        else if (isHeadset) prodStr = string.Format("USB/2.4G 音频外设 ({0})", key);
                                                        else if (isKeyboard) prodStr = string.Format("USB/2.4G 键盘 ({0})", key);
                                                        else if (isDongle) prodStr = string.Format("2.4G 接收器 ({0})", key);
                                                        else prodStr = string.Format("USB/2.4G 外设 ({0})", key);
                                                    }

                                                    int initialBattery = -1;
                                                    if (NuphyDeviceHelper.IsNuphyDevice(key) || NuphyDeviceHelper.IsNuphyDevice(prodStr))
                                                    {
                                                        devCat = DeviceCategory.Keyboard;
                                                        var nuphy = NuphyDeviceHelper.QueryNuphyDeviceInfo();
                                                        if (nuphy != null && nuphy.BatteryPercent >= 0)
                                                        {
                                                            initialBattery = nuphy.BatteryPercent;
                                                            prodStr = nuphy.DeviceName;
                                                        }
                                                        else if (NuphyDeviceHelper.CachedBatteryPercent > 0)
                                                        {
                                                            initialBattery = NuphyDeviceHelper.CachedBatteryPercent;
                                                        }
                                                    }

                                                    var cfg = new CustomDeviceConfig
                                                    {
                                                        DeviceId = "HID:" + key,
                                                        OriginalName = prodStr,
                                                        CustomName = prodStr,
                                                        Category = devCat,
                                                        Transport = "2.4G 无线",
                                                        CachedBattery = initialBattery,
                                                        HardwareFingerprint = "HID:" + key,
                                                        LastSeen = DateTime.Now
                                                    };
                                                    result.Add(cfg);
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
            }
            catch { }
            return result;
        }
    }
}