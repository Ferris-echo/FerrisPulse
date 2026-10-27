// ============================================================================

// 模块名称: BluetoothDeviceHelper.cs

// 所属区域: Hardware (硬件驱动层)

// 核心职责:

//   Windows 原生低功耗蓝牙 (BLE Battery Service GATT 0x180F) 设备电量解析

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

    public static class BluetoothDeviceHelper

    {

        [StructLayout(LayoutKind.Sequential)]

        public struct SP_DEVINFO_DATA

        {

            public uint cbSize;

            public Guid ClassGuid;

            public uint DevInst;

            public IntPtr Reserved;

        }



        [StructLayout(LayoutKind.Sequential)]

        public struct DEVPROPKEY

        {

            public Guid fmtid;

            public uint pid;

        }



        [DllImport("setupapi.dll", SetLastError = true)]

        public static extern IntPtr SetupDiGetClassDevs(IntPtr ClassGuid, string Enumerator, IntPtr hwndParent, uint Flags);



        [DllImport("setupapi.dll", SetLastError = true)]

        public static extern bool SetupDiEnumDeviceInfo(IntPtr DeviceInfoSet, uint MemberIndex, ref SP_DEVINFO_DATA DeviceInfoData);



        [DllImport("setupapi.dll", SetLastError = true)]

        public static extern bool SetupDiDestroyDeviceInfoList(IntPtr DeviceInfoSet);



        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]

        public static extern bool SetupDiGetDevicePropertyW(

            IntPtr DeviceInfoSet,

            ref SP_DEVINFO_DATA DeviceInfoData,

            ref DEVPROPKEY PropertyKey,

            out uint PropertyType,

            byte[] PropertyBuffer,

            uint PropertyBufferSize,

            out uint RequiredSize,

            uint Flags);



        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]

        public static extern bool SetupDiGetDeviceInstanceIdW(

            IntPtr DeviceInfoSet,

            ref SP_DEVINFO_DATA DeviceInfoData,

            StringBuilder DeviceInstanceId,

            uint DeviceInstanceIdSize,

            out uint RequiredSize);



        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]

        public struct BLUETOOTH_DEVICE_INFO

        {

            public int dwSize;

            public ulong Address;

            public uint ulClassofDevice;

            public bool fConnected;

            public bool fRemembered;

            public bool fAuthenticated;

            public ushort wYear, wMonth, wDayOfWeek, wDay, wHour, wMinute, wSecond, wMilliseconds;

            public ushort wYear2, wMonth2, wDayOfWeek2, wDay2, wHour2, wMinute2, wSecond2, wMilliseconds2;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 248)]

            public string szName;

        }



        [StructLayout(LayoutKind.Sequential)]

        public struct BLUETOOTH_DEVICE_SEARCH_PARAMS

        {

            public int dwSize;

            public bool fReturnAuthenticated;

            public bool fReturnRemembered;

            public bool fReturnUnknown;

            public bool fReturnConnected;

            public bool fIssueInquiry;

            public byte cTimeoutMultiplier;

            public IntPtr hRadio;

        }



        [DllImport("bthprops.cpl", SetLastError = true)]

        public static extern IntPtr BluetoothFindFirstDevice(ref BLUETOOTH_DEVICE_SEARCH_PARAMS searchParams, ref BLUETOOTH_DEVICE_INFO deviceInfo);



        [DllImport("bthprops.cpl", SetLastError = true)]

        public static extern bool BluetoothFindNextDevice(IntPtr hFind, ref BLUETOOTH_DEVICE_INFO deviceInfo);



        [DllImport("bthprops.cpl", SetLastError = true)]

        public static extern bool BluetoothFindDeviceClose(IntPtr hFind);



        public static DEVPROPKEY DEVPKEY_Device_BatteryLevel = new DEVPROPKEY

        {

            fmtid = new Guid("104ea319-6ee2-4701-bd47-8ddbf425bbe5"),

            pid = 2

        };



        public static DEVPROPKEY DEVPKEY_Device_BatteryTimestamp = new DEVPROPKEY

        {

            fmtid = new Guid("104ea319-6ee2-4701-bd47-8ddbf425bbe5"),

            pid = 7

        };



        public static DEVPROPKEY DEVPKEY_Device_FriendlyName = new DEVPROPKEY

        {

            fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),

            pid = 14

        };



        public static DEVPROPKEY DEVPKEY_Device_DeviceDesc = new DEVPROPKEY

        {

            fmtid = new Guid("a45c254e-df1c-4efd-8020-67d146a850e0"),

            pid = 2

        };



        public static DEVPROPKEY DEVPKEY_Device_IsConnected = new DEVPROPKEY

        {

            fmtid = new Guid("83da6326-97a6-4088-9453-a1923f573b29"),

            pid = 15

        };



        const uint DIGCF_PRESENT = 0x00000002;

        const uint DIGCF_ALLCLASSES = 0x00000004;



        public static Dictionary<string, string> GetConnectedBluetoothDevices()

        {

            var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);



            // 1. Classic Bluetooth devices from bthprops.cpl (strictly verified via fConnected == true)

            try

            {

                var sp = new BLUETOOTH_DEVICE_SEARCH_PARAMS

                {

                    dwSize = Marshal.SizeOf(typeof(BLUETOOTH_DEVICE_SEARCH_PARAMS)),

                    fReturnAuthenticated = true,

                    fReturnRemembered = true,

                    fReturnUnknown = false,

                    fReturnConnected = true,

                    fIssueInquiry = false,

                    cTimeoutMultiplier = 2,

                    hRadio = IntPtr.Zero

                };



                var dev = new BLUETOOTH_DEVICE_INFO();

                dev.dwSize = Marshal.SizeOf(typeof(BLUETOOTH_DEVICE_INFO));



                IntPtr hFind = BluetoothFindFirstDevice(ref sp, ref dev);

                if (hFind != IntPtr.Zero)

                {

                    try

                    {

                        do

                        {

                            if (dev.fConnected)

                            {

                                string mac = string.Format("{0:X12}", dev.Address);

                                if (!dict.ContainsKey(mac) && !string.IsNullOrEmpty(dev.szName))

                                {

                                    dict[mac] = dev.szName;

                                }

                            }

                        } while (BluetoothFindNextDevice(hFind, ref dev));

                    }

                    finally

                    {

                        BluetoothFindDeviceClose(hFind);

                    }

                }

            }

            catch (Exception diagEx) { Diagnostics.Log("Hardware/BluetoothDeviceHelper.cs: 已忽略异常", diagEx); }



            // 2. BLE devices from SetupAPI BTHLE (strictly verified via DEVPKEY_Device_IsConnected == 255)

            IntPtr devInfo = SetupDiGetClassDevs(IntPtr.Zero, "BTHLE", IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);

            if (devInfo != IntPtr.Zero && devInfo != new IntPtr(-1))

            {

                try

                {

                    SP_DEVINFO_DATA data = new SP_DEVINFO_DATA();

                    data.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));

                    uint index = 0;

                    while (SetupDiEnumDeviceInfo(devInfo, index++, ref data))

                    {

                        byte[] connBuf = new byte[16];

                        uint propType, req;

                        if (!SetupDiGetDevicePropertyW(devInfo, ref data, ref DEVPKEY_Device_IsConnected, out propType, connBuf, (uint)connBuf.Length, out req, 0) || connBuf[0] == 0)

                        {

                            continue;

                        }



                        StringBuilder idSb = new StringBuilder(512);

                        SetupDiGetDeviceInstanceIdW(devInfo, ref data, idSb, (uint)idSb.Capacity, out req);

                        string instId = idSb.ToString();

                        string mac = ExtractMac(instId);



                        if (!string.IsNullOrEmpty(mac) && !dict.ContainsKey(mac))

                        {

                            byte[] nameBuf = new byte[512];

                            string name = null;

                            if (SetupDiGetDevicePropertyW(devInfo, ref data, ref DEVPKEY_Device_FriendlyName, out propType, nameBuf, (uint)nameBuf.Length, out req, 0))

                            {

                                string fn = Encoding.Unicode.GetString(nameBuf, 0, (int)req).TrimEnd('\0', ' ', '\t');

                                if (!string.IsNullOrEmpty(fn) && !fn.StartsWith("Bluetooth", StringComparison.OrdinalIgnoreCase))

                                {

                                    name = fn;

                                }

                            }

                            if (string.IsNullOrEmpty(name))

                            {

                                byte[] descBuf = new byte[512];

                                if (SetupDiGetDevicePropertyW(devInfo, ref data, ref DEVPKEY_Device_DeviceDesc, out propType, descBuf, (uint)descBuf.Length, out req, 0))

                                {

                                    string dn = Encoding.Unicode.GetString(descBuf, 0, (int)req).TrimEnd('\0', ' ', '\t');

                                    if (!string.IsNullOrEmpty(dn) && !dn.StartsWith("Bluetooth", StringComparison.OrdinalIgnoreCase))

                                    {

                                        name = dn;

                                    }

                                }

                            }

                            if (!string.IsNullOrEmpty(name))

                            {

                                dict[mac] = name;

                            }

                        }

                    }

                }

                finally

                {

                    SetupDiDestroyDeviceInfoList(devInfo);

                }

            }



            return dict;

        }



        public static bool IsValidDeviceFriendlyName(string name)

        {

            if (string.IsNullOrEmpty(name)) return false;

            string s = name.Trim();

            if (s.Length == 0) return false;

            string lower = s.ToLowerInvariant();

            if (lower.Contains("iap") || 

                lower.Contains("wireless iap") ||

                lower.Contains("avrcp") || 

                lower.Contains("hands-free") || 

                lower.Contains("generic attribute") || 

                lower.Contains("generic access") || 

                lower.Contains("rfcomm") || 

                lower.Contains("tdi") || 

                lower.Contains("传输") || 

                lower.Contains("串行") || 

                lower.Contains("com") || 

                lower.StartsWith("bluetooth") ||

                lower.Contains("audio/video remote") ||

                lower.Contains("enumerator") ||

                lower.Contains("personal area network"))

            {

                return false;

            }

            return true;

        }



        public static bool QueryByMac(string mac, out int battery, out string friendlyName)

        {

            return QueryByMac(mac, null, out battery, out friendlyName);

        }



        public static bool QueryByMac(string mac, string hintName, out int battery, out string friendlyName)

        {

            battery = -1;

            friendlyName = null;

            if (string.IsNullOrEmpty(mac)) return false;



            // 1. Authoritative check: Is device connected via Classic Bluetooth (bthprops) or BLE / PnP (SetupAPI)?

            bool isClassicConnected = false;

            string classicName = null;

            try

            {

                var sp = new BLUETOOTH_DEVICE_SEARCH_PARAMS

                {

                    dwSize = Marshal.SizeOf(typeof(BLUETOOTH_DEVICE_SEARCH_PARAMS)),

                    fReturnAuthenticated = true,

                    fReturnRemembered = true,

                    fReturnUnknown = false,

                    fReturnConnected = true,

                    fIssueInquiry = false,

                    cTimeoutMultiplier = 2,

                    hRadio = IntPtr.Zero

                };



                var dev = new BLUETOOTH_DEVICE_INFO();

                dev.dwSize = Marshal.SizeOf(typeof(BLUETOOTH_DEVICE_INFO));



                IntPtr hFind = BluetoothFindFirstDevice(ref sp, ref dev);

                if (hFind != IntPtr.Zero)

                {

                    try

                    {

                        do

                        {

                            if (dev.fConnected)

                            {

                                string devMac = string.Format("{0:X12}", dev.Address);

                                if (string.Equals(devMac, mac, StringComparison.OrdinalIgnoreCase))

                                {

                                    isClassicConnected = true;

                                    classicName = dev.szName;

                                    break;

                                }

                            }

                        } while (BluetoothFindNextDevice(hFind, ref dev));

                    }

                    finally

                    {

                        BluetoothFindDeviceClose(hFind);

                    }

                }

            }

            catch (Exception diagEx) { Diagnostics.Log("Hardware/BluetoothDeviceHelper.cs: 已忽略异常", diagEx); }



            bool isPnpConnected = false;

            string pnpName = null;

            string[] probeEnums = new string[] { "BTHLE", "BTHENUM" };

            foreach (var en in probeEnums)

            {

                IntPtr devInfo = SetupDiGetClassDevs(IntPtr.Zero, en, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);

                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) continue;



                try

                {

                    SP_DEVINFO_DATA data = new SP_DEVINFO_DATA();

                    data.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));

                    uint idx = 0;

                    while (SetupDiEnumDeviceInfo(devInfo, idx++, ref data))

                    {

                        StringBuilder idSb = new StringBuilder(512);

                        uint req;

                        SetupDiGetDeviceInstanceIdW(devInfo, ref data, idSb, (uint)idSb.Capacity, out req);

                        string instId = idSb.ToString();

                        bool isTarget = (instId.IndexOf(mac, StringComparison.OrdinalIgnoreCase) >= 0);

                        string nodeFn = null;

                        if (!isTarget && !string.IsNullOrEmpty(hintName))

                        {

                            byte[] nameBuf = new byte[512];

                            uint pType;

                            if (SetupDiGetDevicePropertyW(devInfo, ref data, ref DEVPKEY_Device_FriendlyName, out pType, nameBuf, (uint)nameBuf.Length, out req, 0))

                            {

                                string fn = Encoding.Unicode.GetString(nameBuf, 0, (int)req).TrimEnd('\0', ' ', '\t');

                                if (!string.IsNullOrEmpty(fn) && (string.Equals(fn, hintName, StringComparison.OrdinalIgnoreCase) || fn.StartsWith(hintName, StringComparison.OrdinalIgnoreCase) || hintName.StartsWith(fn, StringComparison.OrdinalIgnoreCase)))

                                {

                                    isTarget = true;

                                    nodeFn = fn;

                                }

                            }

                        }



                        if (isTarget)

                        {

                            byte[] connBuf = new byte[16];

                            uint pType;

                            if (SetupDiGetDevicePropertyW(devInfo, ref data, ref DEVPKEY_Device_IsConnected, out pType, connBuf, (uint)connBuf.Length, out req, 0))

                            {

                                if (connBuf[0] != 0)

                                {

                                    isPnpConnected = true;



                                    if (string.IsNullOrEmpty(pnpName))

                                    {

                                        if (string.IsNullOrEmpty(nodeFn))

                                        {

                                            byte[] nameBuf = new byte[512];

                                            if (SetupDiGetDevicePropertyW(devInfo, ref data, ref DEVPKEY_Device_FriendlyName, out pType, nameBuf, (uint)nameBuf.Length, out req, 0))

                                            {

                                                nodeFn = Encoding.Unicode.GetString(nameBuf, 0, (int)req).TrimEnd('\0', ' ', '\t');

                                            }

                                        }

                                        if (!string.IsNullOrEmpty(nodeFn) && !nodeFn.StartsWith("Bluetooth", StringComparison.OrdinalIgnoreCase) && !nodeFn.Contains("传输") && !nodeFn.Contains("Avrcp"))

                                        {

                                            pnpName = nodeFn;

                                        }

                                    }

                                    break;

                                }

                            }

                        }

                    }

                }

                finally

                {

                    SetupDiDestroyDeviceInfoList(devInfo);

                }

                if (isPnpConnected) break;

            }



            bool isConnected = (isClassicConnected || isPnpConnected);



            if (!string.IsNullOrEmpty(classicName))

            {

                friendlyName = classicName;

            }

            else if (!string.IsNullOrEmpty(pnpName))

            {

                friendlyName = pnpName;

            }

            else if (!string.IsNullOrEmpty(hintName))

            {

                friendlyName = hintName;

            }



            // 2. Scan telemetry under BTHLE and BTHENUM

            // 支持蓝牙双模设备 (Classic A2DP/HFP 音频 + BLE GATT 0x180F 电池服务):

            // 匹配原则: 匹配当前 MAC，或匹配该设备的 FriendlyName，并择取最新时间戳 (BatteryTimestamp) 的有效电量，杜绝旧 HFP 节点卡死

            // 无论当前是否已连接，均深入查询 Windows SetupAPI 驱动属性缓存，使离线电量与 Windows 蓝牙设置实时保持一致

            long latestTime = 0;

            int bestBattery = -1;



            string cleanMac = !string.IsNullOrEmpty(mac) ? mac.Replace(":", "").Replace("-", "").Replace("_", "").Trim().ToUpperInvariant() : null;

            string macWithColons = (cleanMac != null && cleanMac.Length == 12) ?

                string.Format("{0}:{1}:{2}:{3}:{4}:{5}", cleanMac.Substring(0,2), cleanMac.Substring(2,2), cleanMac.Substring(4,2), cleanMac.Substring(6,2), cleanMac.Substring(8,2), cleanMac.Substring(10,2)) : null;

            string macWithUnderscores = (cleanMac != null && cleanMac.Length == 12) ?

                string.Format("{0}_{1}_{2}_{3}_{4}_{5}", cleanMac.Substring(0,2), cleanMac.Substring(2,2), cleanMac.Substring(4,2), cleanMac.Substring(6,2), cleanMac.Substring(8,2), cleanMac.Substring(10,2)) : null;



            string[] enumerators = new string[] { "BTHLE", "BTHENUM" };

            foreach (var en in enumerators)

            {

                IntPtr devInfo = SetupDiGetClassDevs(IntPtr.Zero, en, IntPtr.Zero, DIGCF_PRESENT | DIGCF_ALLCLASSES);

                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) continue;



                try

                {

                    SP_DEVINFO_DATA data = new SP_DEVINFO_DATA();

                    data.cbSize = (uint)Marshal.SizeOf(typeof(SP_DEVINFO_DATA));



                    uint index = 0;

                    while (SetupDiEnumDeviceInfo(devInfo, index++, ref data))

                    {

                        StringBuilder idSb = new StringBuilder(512);

                        uint req;

                        SetupDiGetDeviceInstanceIdW(devInfo, ref data, idSb, (uint)idSb.Capacity, out req);

                        string instId = idSb.ToString();



                        string rawFn = null;

                        byte[] nameBuf = new byte[512];

                        uint propType;

                        if (SetupDiGetDevicePropertyW(devInfo, ref data, ref DEVPKEY_Device_FriendlyName, out propType, nameBuf, (uint)nameBuf.Length, out req, 0))

                        {

                            rawFn = Encoding.Unicode.GetString(nameBuf, 0, (int)req).TrimEnd('\0', ' ', '\t');

                        }



                        string rawDesc = null;

                        byte[] descBuf = new byte[512];

                        if (SetupDiGetDevicePropertyW(devInfo, ref data, ref DEVPKEY_Device_DeviceDesc, out propType, descBuf, (uint)descBuf.Length, out req, 0))

                        {

                            rawDesc = Encoding.Unicode.GetString(descBuf, 0, (int)req).TrimEnd('\0', ' ', '\t');

                        }



                        // 提取纯净设备名称（用于展示，过滤驱动端点后缀）

                        string cleanCandidate = null;
                        if (IsValidDeviceFriendlyName(rawFn))
                        {
                            cleanCandidate = rawFn;
                        }
                        else if (IsValidDeviceFriendlyName(rawDesc))
                        {
                            cleanCandidate = rawDesc;
                        }

                        if ((string.IsNullOrEmpty(friendlyName) || !IsValidDeviceFriendlyName(friendlyName)) && !string.IsNullOrEmpty(cleanCandidate))
                        {
                            bool instHasMac = (!string.IsNullOrEmpty(cleanMac) && instId.IndexOf(cleanMac, StringComparison.OrdinalIgnoreCase) >= 0);
                            if (instHasMac)
                            {
                                friendlyName = cleanCandidate;
                            }
                        }



                        // 核心判断: 该驱动节点是否属于当前查询的蓝牙耳机/设备 (MAC 命中 或 命名关联合适)

                        bool matchesMac = false;

                        if (!string.IsNullOrEmpty(cleanMac) && instId.IndexOf(cleanMac, StringComparison.OrdinalIgnoreCase) >= 0) matchesMac = true;

                        if (!matchesMac && !string.IsNullOrEmpty(macWithColons) && instId.IndexOf(macWithColons, StringComparison.OrdinalIgnoreCase) >= 0) matchesMac = true;

                        if (!matchesMac && !string.IsNullOrEmpty(macWithUnderscores) && instId.IndexOf(macWithUnderscores, StringComparison.OrdinalIgnoreCase) >= 0) matchesMac = true;



                        bool matchesName = false;

                        string searchName = !string.IsNullOrEmpty(friendlyName) ? friendlyName : hintName;

                        if (!string.IsNullOrEmpty(searchName))

                        {

                            if (!string.IsNullOrEmpty(rawFn))

                            {

                                if (rawFn.IndexOf(searchName, StringComparison.OrdinalIgnoreCase) >= 0 || searchName.IndexOf(rawFn, StringComparison.OrdinalIgnoreCase) >= 0)

                                {

                                    matchesName = true;

                                }

                            }

                            if (!matchesName && !string.IsNullOrEmpty(rawDesc))

                            {

                                if (rawDesc.IndexOf(searchName, StringComparison.OrdinalIgnoreCase) >= 0 || searchName.IndexOf(rawDesc, StringComparison.OrdinalIgnoreCase) >= 0)

                                {

                                    matchesName = true;

                                }

                            }

                        }



                        if (matchesMac || matchesName)

                        {

                            byte[] battBuf = new byte[16];

                            uint pType;

                            if (SetupDiGetDevicePropertyW(devInfo, ref data, ref DEVPKEY_Device_BatteryLevel, out pType, battBuf, (uint)battBuf.Length, out req, 0))

                            {

                                int b = (int)battBuf[0];

                                if (b >= 0 && b <= 100)

                                {

                                    long ft = 0;

                                    byte[] timeBuf = new byte[16];

                                    if (SetupDiGetDevicePropertyW(devInfo, ref data, ref DEVPKEY_Device_BatteryTimestamp, out pType, timeBuf, (uint)timeBuf.Length, out req, 0))

                                    {

                                        ft = BitConverter.ToInt64(timeBuf, 0);

                                    }



                                    if (ft > latestTime)

                                    {

                                        latestTime = ft;

                                        bestBattery = b;

                                    }

                                    else if (latestTime == 0 && bestBattery < 0)

                                    {

                                        bestBattery = b;

                                    }

                                }

                            }

                        }

                    }

                }

                finally

                {

                    SetupDiDestroyDeviceInfoList(devInfo);

                }

            }



            if (!IsValidDeviceFriendlyName(friendlyName))

            {

                if (IsValidDeviceFriendlyName(hintName)) friendlyName = hintName;

                else if (IsValidDeviceFriendlyName(classicName)) friendlyName = classicName;

                else friendlyName = null;

            }



            if (bestBattery >= 0)

            {

                battery = bestBattery;

            }



            return isConnected;

        }



        public static List<CustomDeviceConfig> ScanAllBluetoothDevices()

        {

            var result = new List<CustomDeviceConfig>();

            var connectedDevices = GetConnectedBluetoothDevices();

            if (connectedDevices == null || connectedDevices.Count == 0)

            {

                return result;

            }



            foreach (var kvp in connectedDevices)

            {

                string mac = kvp.Key;

                string name = kvp.Value;



                if (string.IsNullOrEmpty(name)) name = "Bluetooth Device (" + mac + ")";



                // Filter out non-peripheral internal Bluetooth nodes

                if (!IsValidDeviceFriendlyName(name))

                {

                    continue;

                }



                int batt;

                string friendlyName;

                bool isConn = QueryByMac(mac, name, out batt, out friendlyName);

                if (!isConn) continue; // Only return verified connected devices



                if (!string.IsNullOrEmpty(friendlyName) && IsValidDeviceFriendlyName(friendlyName))

                {

                    name = friendlyName;

                }



                DeviceCategory cat = DeviceCategory.Generic;

                string lower = name.ToLowerInvariant();

                if (lower.Contains("xbox") || lower.Contains("controller") || lower.Contains("gamepad") || lower.Contains("dualsense") || lower.Contains("joy-con") || lower.Contains("手柄"))

                    cat = DeviceCategory.Gamepad;

                else if (lower.Contains("buds") || lower.Contains("airpod") || lower.Contains("freebuds") || lower.Contains("earbud") || lower.Contains("入耳"))

                    cat = DeviceCategory.Earbuds;

                else if (lower.Contains("head") || lower.Contains("ear") || lower.Contains("audio") || lower.Contains("free") || lower.Contains("耳机") || lower.Contains("头戴"))

                    cat = DeviceCategory.Headset;

                else if (lower.Contains("mouse") || lower.Contains("鼠标"))

                    cat = DeviceCategory.Mouse;

                else if (lower.Contains("keyboard") || lower.Contains("键盘") || lower.Contains("nuphy") || lower.Contains("keychron") || lower.Contains("halo") || lower.Contains("air75") || lower.Contains("air96") || lower.Contains("air60") || lower.Contains("wh80") || lower.Contains("kb"))

                    cat = DeviceCategory.Keyboard;



                result.Add(new CustomDeviceConfig

                {

                    DeviceId = "BT_" + mac,

                    HardwareFingerprint = mac,

                    OriginalName = name,

                    CustomName = name,

                    Category = cat,

                    Transport = "蓝牙 (BLE)",

                    CachedBattery = batt >= 0 ? batt : 0,

                    LastSeen = DateTime.Now

                });

            }



            return result;

        }



        public static string ExtractMac(string id)

        {

            int idx = id.IndexOf("DEV_");

            if (idx >= 0 && id.Length >= idx + 16)

            {

                return id.Substring(idx + 4, 12).ToUpperInvariant();

            }

            return null;

        }

    }

}