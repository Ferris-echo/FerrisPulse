// ============================================================================
// 模块名称: HardwareDiagnosticService.cs
// 所属区域: Hardware (硬件诊断与只读快照服务层)
// 核心职责:
//   提供零写入、隐私脱敏的一键外设诊断快照采集与驱动探测日志环缓存：
//   1. 只读采集本机全部物理 HID 端点拓扑（VID:PID、产品名称、UsagePage/Usage、Report 长度）
//   2. 自动剥离系统路径中的设备序列号/GUID，仅保留脱敏硬件总线标识
//   3. 汇总各内置驱动的真实探测轨迹（PID 未适配、端点不匹配、BUSY/0x04/权限拒绝）
//   4. 识别所有“物理在线但未被任何驱动接管”的大厂与通用外设，方便用户一键导出定位老型号
// 依赖关系: RazerDeviceHelper.cs, VirtualHidFilter.cs, GenericHidHelper.cs, DeviceManager.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、签名或字段时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（❌ 严禁 $"..." 插值、?.、nameof、out var）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// 4. [只读零写入铁律]: 本文件严禁向外设发送任何写入或修改状态的控制指令。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;

namespace RazerBatteryTray
{
    public sealed class HidEndpointSnapshot
    {
        public ushort VendorId { get; set; }
        public ushort ProductId { get; set; }
        public string VidPidKey { get; set; }
        public string ProductString { get; set; }
        public string SanitizedPath { get; set; }
        public ushort UsagePage { get; set; }
        public ushort Usage { get; set; }
        public int InputLength { get; set; }
        public int OutputLength { get; set; }
        public int FeatureLength { get; set; }
        public bool IsVirtual { get; set; }
        public bool IsInternal { get; set; }
    }

    public static class HardwareDiagnosticService
    {
        private const int MaxTraceCount = 150;
        private static readonly object traceLock = new object();
        private static readonly List<string> driverTraces = new List<string>();

        private static readonly Regex VidPidRegex = new Regex("vid_([0-9a-f]{4})&pid_([0-9a-f]{4})", RegexOptions.IgnoreCase);
        private static readonly Regex MiRegex = new Regex("&mi_([0-9a-f]{2})", RegexOptions.IgnoreCase);
        private static readonly Regex ColRegex = new Regex("&col([0-9a-f]{2})", RegexOptions.IgnoreCase);
        private static readonly Regex HexVidPidRegex = new Regex("([0-9a-f]{4}):([0-9a-f]{4})", RegexOptions.IgnoreCase);

        public static void ClearDriverTraces()
        {
            lock (traceLock)
            {
                driverTraces.Clear();
            }
        }

        public static void RecordDriverTrace(string driver, string vidPid, string message)
        {
            string drv = string.IsNullOrEmpty(driver) ? "Unknown" : driver;
            string vp = string.IsNullOrEmpty(vidPid) ? "-" : vidPid;
            string msg = string.IsNullOrEmpty(message) ? "" : message;
            string line = string.Format("[{0}] [{1}] {2}", drv, vp, msg);

            lock (traceLock)
            {
                if (driverTraces.Count >= MaxTraceCount)
                {
                    driverTraces.RemoveAt(0);
                }
                driverTraces.Add(line);
            }
        }

        public static List<string> GetRecentDriverTraces()
        {
            lock (traceLock)
            {
                return new List<string>(driverTraces);
            }
        }

        /// <summary>
        /// 对 Windows HID 设备路径脱敏：仅保留总线前缀与 vid/pid/mi/col，剔除设备序列号与实例 GUID。
        /// </summary>
        public static string SanitizeDevicePath(string devicePath)
        {
            if (string.IsNullOrEmpty(devicePath)) return "";

            string lower = devicePath.ToLowerInvariant();
            string bus = "hid";
            if (lower.Contains("\\hid#")) bus = "hid";
            else if (lower.Contains("\\usb#")) bus = "usb";
            else if (lower.Contains("bthenum#") || lower.Contains("{00001812") || lower.Contains("bthle")) bus = "bt";

            StringBuilder sb = new StringBuilder();
            sb.Append(bus);

            Match vpMatch = VidPidRegex.Match(lower);
            if (vpMatch.Success)
            {
                sb.Append("#vid_").Append(vpMatch.Groups[1].Value.ToLowerInvariant());
                sb.Append("&pid_").Append(vpMatch.Groups[2].Value.ToLowerInvariant());
            }

            Match miMatch = MiRegex.Match(lower);
            if (miMatch.Success)
            {
                sb.Append("&mi_").Append(miMatch.Groups[1].Value.ToLowerInvariant());
            }

            Match colMatch = ColRegex.Match(lower);
            if (colMatch.Success)
            {
                sb.Append("&col").Append(colMatch.Groups[1].Value.ToLowerInvariant());
            }

            if (lower.Contains("\\kbd"))
            {
                sb.Append("\\kbd");
            }
            else if (lower.Contains("\\mou"))
            {
                sb.Append("\\mou");
            }

            return sb.ToString();
        }

        public static List<HidEndpointSnapshot> EnumeratePhysicalHidEndpoints(out int skippedVirtualCount, out int skippedInternalCount)
        {
            skippedVirtualCount = 0;
            skippedInternalCount = 0;
            List<HidEndpointSnapshot> list = new List<HidEndpointSnapshot>();

            try
            {
                Guid hidGuid;
                RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
                IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, RazerDeviceHelper.DIGCF_PRESENT | RazerDeviceHelper.DIGCF_DEVICEINTERFACE);
                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1))
                {
                    return list;
                }

                RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                ifData.cbSize = Marshal.SizeOf(ifData);
                uint memberIdx = 0;

                try
                {
                    while (RazerDeviceHelper.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                    {
                        uint reqSize;
                        RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                        if (reqSize == 0)
                        {
                            ifData.cbSize = Marshal.SizeOf(ifData);
                            continue;
                        }

                        IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                        try
                        {
                            Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);
                            if (!RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                            {
                                ifData.cbSize = Marshal.SizeOf(ifData);
                                continue;
                            }

                            IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                            string devicePath = Marshal.PtrToStringAuto(pDevicePath);
                            if (string.IsNullOrEmpty(devicePath))
                            {
                                ifData.cbSize = Marshal.SizeOf(ifData);
                                continue;
                            }

                            if (VirtualHidFilter.IsVirtualHidPath(devicePath))
                            {
                                skippedVirtualCount++;
                                ifData.cbSize = Marshal.SizeOf(ifData);
                                continue;
                            }

                            // 诊断采集严格使用 0 权限（只读属性/描述符，不触发写入、不受键盘独占锁阻塞）
                            IntPtr handle = RazerDeviceHelper.CreateFile(
                                devicePath,
                                0,
                                RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                                IntPtr.Zero,
                                RazerDeviceHelper.OPEN_EXISTING,
                                0,
                                IntPtr.Zero);

                            if (handle == IntPtr.Zero || handle.ToInt64() == -1)
                            {
                                ifData.cbSize = Marshal.SizeOf(ifData);
                                continue;
                            }

                            try
                            {
                                RazerDeviceHelper.HIDD_ATTRIBUTES attr = new RazerDeviceHelper.HIDD_ATTRIBUTES();
                                attr.Size = Marshal.SizeOf(attr);
                                bool hasAttr = RazerDeviceHelper.HidD_GetAttributes(handle, ref attr);

                                string prod = "";
                                StringBuilder sbProd = new StringBuilder(256);
                                if (RazerDeviceHelper.HidD_GetProductString(handle, sbProd, sbProd.Capacity))
                                {
                                    prod = sbProd.ToString().Trim();
                                }

                                if (GenericHidHelper.IsInternalHidDevice(prod, devicePath))
                                {
                                    skippedInternalCount++;
                                    continue;
                                }

                                ushort usagePage = 0;
                                ushort usage = 0;
                                int inLen = 0;
                                int outLen = 0;
                                int featLen = 0;

                                IntPtr preparsed;
                                if (RazerDeviceHelper.HidD_GetPreparsedData(handle, out preparsed) && preparsed != IntPtr.Zero)
                                {
                                    try
                                    {
                                        RazerDeviceHelper.HIDP_CAPS caps;
                                        if (RazerDeviceHelper.HidP_GetCaps(preparsed, out caps) == 0x00110000)
                                        {
                                            usagePage = caps.UsagePage;
                                            usage = caps.Usage;
                                            inLen = caps.InputReportByteLength;
                                            outLen = caps.OutputReportByteLength;
                                            featLen = caps.FeatureReportByteLength;
                                        }
                                    }
                                    finally
                                    {
                                        RazerDeviceHelper.HidD_FreePreparsedData(preparsed);
                                    }
                                }

                                ushort vid = hasAttr ? attr.VendorID : (ushort)0;
                                ushort pid = hasAttr ? attr.ProductID : (ushort)0;
                                if (vid == 0 && pid == 0)
                                {
                                    Match m = VidPidRegex.Match(devicePath);
                                    if (m.Success)
                                    {
                                        ushort.TryParse(m.Groups[1].Value, System.Globalization.NumberStyles.HexNumber, null, out vid);
                                        ushort.TryParse(m.Groups[2].Value, System.Globalization.NumberStyles.HexNumber, null, out pid);
                                    }
                                }

                                if (vid == 0 && pid == 0) continue;

                                HidEndpointSnapshot snap = new HidEndpointSnapshot();
                                snap.VendorId = vid;
                                snap.ProductId = pid;
                                snap.VidPidKey = string.Format("{0:X4}:{1:X4}", vid, pid);
                                snap.ProductString = prod;
                                snap.SanitizedPath = SanitizeDevicePath(devicePath);
                                snap.UsagePage = usagePage;
                                snap.Usage = usage;
                                snap.InputLength = inLen;
                                snap.OutputLength = outLen;
                                snap.FeatureLength = featLen;
                                snap.IsVirtual = false;
                                snap.IsInternal = false;
                                list.Add(snap);
                            }
                            finally
                            {
                                RazerDeviceHelper.CloseHandle(handle);
                            }
                        }
                        finally
                        {
                            Marshal.FreeHGlobal(detailBuffer);
                        }

                        ifData.cbSize = Marshal.SizeOf(ifData);
                    }
                }
                finally
                {
                    RazerDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);
                }
            }
            catch (Exception ex)
            {
                RecordDriverTrace("Diagnostic", "-", "EnumerateError: " + ex.Message);
            }

            return list;
        }

        public static void AddVidPidKeysToSet(HashSet<string> set, string text)
        {
            if (set == null || string.IsNullOrEmpty(text)) return;
            AddVidPidTokensFromString(set, text);
        }

        private static HashSet<string> BuildRecognizedVidPidSet(List<MouseBatteryInfo> activeDevices)
        {
            HashSet<string> set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (activeDevices == null) return set;

            for (int i = 0; i < activeDevices.Count; i++)
            {
                MouseBatteryInfo dev = activeDevices[i];
                if (dev == null) continue;

                AddVidPidTokensFromString(set, dev.DeviceId);
                AddVidPidTokensFromString(set, dev.HardwareFingerprint);
                AddVidPidTokensFromString(set, dev.HardwareId);

                if (!string.IsNullOrEmpty(dev.DeviceId))
                {
                    List<string> variants = DeviceManager.GetDeviceIdVariants(dev.DeviceId);
                    for (int v = 0; v < variants.Count; v++)
                    {
                        AddVidPidTokensFromString(set, variants[v]);
                    }
                }
            }
            return set;
        }

        private static void AddVidPidTokensFromString(HashSet<string> set, string text)
        {
            if (string.IsNullOrEmpty(text)) return;

            Match m = HexVidPidRegex.Match(text);
            if (m.Success)
            {
                string vidHex = m.Groups[1].Value.ToUpperInvariant();
                string pidHex = m.Groups[2].Value.ToUpperInvariant();
                set.Add(vidHex + ":" + pidHex);

                if (string.Equals(vidHex, "1532", StringComparison.OrdinalIgnoreCase))
                {
                    int pidVal;
                    if (int.TryParse(pidHex, System.Globalization.NumberStyles.HexNumber, null, out pidVal))
                    {
                        int norm = RazerDeviceHelper.NormalizeRazerPid(pidVal);
                        set.Add("1532:" + norm.ToString("X4"));
                    }
                }
            }

            Match vp = VidPidRegex.Match(text);
            if (vp.Success)
            {
                string vidHex = vp.Groups[1].Value.ToUpperInvariant();
                string pidHex = vp.Groups[2].Value.ToUpperInvariant();
                set.Add(vidHex + ":" + pidHex);
            }
        }

        private static bool IsEndpointRecognized(HidEndpointSnapshot ep, HashSet<string> recognizedKeys)
        {
            if (ep == null || recognizedKeys == null) return false;
            if (!string.IsNullOrEmpty(ep.VidPidKey) && recognizedKeys.Contains(ep.VidPidKey)) return true;

            if (ep.VendorId == 0x1532)
            {
                int normPid = RazerDeviceHelper.NormalizeRazerPid(ep.ProductId);
                string normKey = string.Format("1532:{0:X4}", normPid);
                if (recognizedKeys.Contains(normKey)) return true;
            }
            return false;
        }

        public static string FormatSnapshotText(
            string appVersion,
            string osVersion,
            List<MouseBatteryInfo> activeDevices,
            List<HidEndpointSnapshot> endpoints,
            List<string> traces,
            int skippedVirtualCount,
            int skippedInternalCount)
        {
            StringBuilder sb = new StringBuilder();
            int activeCount = activeDevices != null ? activeDevices.Count : 0;

            sb.AppendLine("=== FerrisPulse 外设诊断快照 ===");
            sb.AppendLine(string.Format("软件版本: {0}", string.IsNullOrEmpty(appVersion) ? UpdateManager.CurrentVersion : appVersion));
            sb.AppendLine(string.Format("操作系统: {0}", string.IsNullOrEmpty(osVersion) ? Environment.OSVersion.ToString() : osVersion));
            sb.AppendLine(string.Format("采集时间: {0}", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")));
            sb.AppendLine(string.Format("已识别设备数: {0} | 过滤虚拟 HID: {1} | 过滤内置触控板: {2}", activeCount, skippedVirtualCount, skippedInternalCount));
            sb.AppendLine();

            sb.AppendLine("[1. 当前已识别设备]");
            if (activeDevices == null || activeDevices.Count == 0)
            {
                sb.AppendLine("  (无已识别设备)");
            }
            else
            {
                for (int i = 0; i < activeDevices.Count; i++)
                {
                    MouseBatteryInfo d = activeDevices[i];
                    if (d == null) continue;
                    string stateText = d.IsSleeping ? "Sleeping" : (d.IsConnected ? "Online" : "Offline");
                    string battText = d.BatteryPercent >= 0 ? (d.BatteryPercent + "%") : "--%";
                    if (d.IsCharging) battText += "(Charging)";
                    sb.AppendLine(string.Format(
                        "  - {0} | {1} | {2} | {3} | {4} | {5} | {6} | Driver={7}",
                        d.DeviceId ?? "-",
                        d.Brand ?? "-",
                        d.DeviceName ?? "-",
                        d.Category.ToString(),
                        stateText,
                        battText,
                        d.Transport ?? "-",
                        d.DriverId ?? "-"));
                }
            }
            sb.AppendLine();

            sb.AppendLine("[2. 物理 HID 外设拓扑 (只读零写入)]");
            if (endpoints == null || endpoints.Count == 0)
            {
                sb.AppendLine("  (未发现物理 HID 接口)");
            }
            else
            {
                HashSet<string> recognizedKeys = BuildRecognizedVidPidSet(activeDevices);
                Dictionary<string, List<HidEndpointSnapshot>> grouped = new Dictionary<string, List<HidEndpointSnapshot>>(StringComparer.OrdinalIgnoreCase);
                List<string> keyOrder = new List<string>();

                for (int i = 0; i < endpoints.Count; i++)
                {
                    HidEndpointSnapshot ep = endpoints[i];
                    if (ep == null || string.IsNullOrEmpty(ep.VidPidKey)) continue;
                    if (!grouped.ContainsKey(ep.VidPidKey))
                    {
                        grouped[ep.VidPidKey] = new List<HidEndpointSnapshot>();
                        keyOrder.Add(ep.VidPidKey);
                    }
                    grouped[ep.VidPidKey].Add(ep);
                }

                for (int k = 0; k < keyOrder.Count; k++)
                {
                    string key = keyOrder[k];
                    List<HidEndpointSnapshot> group = grouped[key];
                    HidEndpointSnapshot first = group[0];

                    string prod = "";
                    bool recognized = false;
                    for (int j = 0; j < group.Count; j++)
                    {
                        if (string.IsNullOrEmpty(prod) && !string.IsNullOrEmpty(group[j].ProductString))
                        {
                            prod = group[j].ProductString;
                        }
                        if (IsEndpointRecognized(group[j], recognizedKeys))
                        {
                            recognized = true;
                        }
                    }

                    string tag = recognized ? "[已识别]" : "[未匹配内置驱动]";
                    string prodDisp = string.IsNullOrEmpty(prod) ? "(无产品描述)" : prod;
                    sb.AppendLine(string.Format("  * {0} {1} \"{2}\" (共 {3} 个接口)", tag, key, prodDisp, group.Count));

                    for (int j = 0; j < group.Count; j++)
                    {
                        HidEndpointSnapshot ep = group[j];
                        sb.AppendLine(string.Format(
                            "      - {0} | UP=0x{1:X4} U=0x{2:X4} | In={3} Out={4} Feat={5}",
                            ep.SanitizedPath ?? "-",
                            ep.UsagePage,
                            ep.Usage,
                            ep.InputLength,
                            ep.OutputLength,
                            ep.FeatureLength));
                    }
                }
            }
            sb.AppendLine();

            sb.AppendLine("[3. 驱动探测日志 (Driver Trace)]");
            if (traces == null || traces.Count == 0)
            {
                sb.AppendLine("  (暂无探测日志)");
            }
            else
            {
                for (int i = 0; i < traces.Count; i++)
                {
                    sb.AppendLine("  " + traces[i]);
                }
            }

            return sb.ToString();
        }

        public static string GenerateDiagnosticReport(List<MouseBatteryInfo> activeDevices)
        {
            List<MouseBatteryInfo> devices = activeDevices;
            if (devices == null)
            {
                ClearDriverTraces();
                devices = DeviceManager.QueryAllDevices();
            }

            int skippedVirtual;
            int skippedInternal;
            List<HidEndpointSnapshot> endpoints = EnumeratePhysicalHidEndpoints(out skippedVirtual, out skippedInternal);
            List<string> traces = GetRecentDriverTraces();

            return FormatSnapshotText(
                UpdateManager.CurrentVersion,
                Environment.OSVersion.ToString(),
                devices,
                endpoints,
                traces,
                skippedVirtual,
                skippedInternal);
        }
    }
}
