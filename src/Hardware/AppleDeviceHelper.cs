// ============================================================================

// 模块名称: AppleDeviceHelper.cs

// 所属区域: Hardware (硬件驱动层)

// 核心职责:

//   Apple 与 Beats 全系生态设备电量读取引擎

//   1. AirPods 1/2/3/4/Pro/Max 及 Beats 全系真无线/头戴式耳机:

//      - 纯原生零外部依赖 WinRT BluetoothLEAdvertisementWatcher (反射/动态代理)

//      - Apple Continuity 0x004C Proximity Pairing (0x07/0x10) 广播解码 (左耳/右耳/充电盒)

//      - Windows PnP 统一蓝牙电池遥测属性 ({104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2)

//   2. Apple Magic 系列外设 (Magic Mouse 1/2, Magic Trackpad 1/2, Magic Keyboard):

//      - Bluetooth HID Feature Report (0x47 / 0xD1) 电量直读

// 依赖关系: NativeMethods.cs, MouseBatteryInfo.cs, RazerDeviceHelper.cs

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

using System.Reflection;

using System.Runtime.InteropServices;

using System.Text;

using System.Threading;

using Microsoft.Win32;

using Microsoft.Win32.SafeHandles;



namespace RazerBatteryTray

{

    public static class AppleDeviceHelper

    {

        private static readonly object syncLock = new object();

        private const string CacheRegistryKey = @"Software\RazerBatteryTray\AppleCache";



        // Apple Inc. Bluetooth SIG 厂商标识

        public const ushort AppleCompanyId = 76; // 0x004C

        public const int AppleVendorId = 0x05AC;

        public const int AppleBluetoothSigVid = 0x004C;



        #region 数据模型



        public class AppleEarbudsState

        {

            public ushort ModelId;

            public string ModelName;

            public string DisplayName;

            public int LeftBattery;      // -1 表示不可用/未取出

            public bool LeftCharging;

            public int RightBattery;     // -1 表示不可用/未取出

            public bool RightCharging;

            public int CaseBattery;      // -1 表示不可用/未开盖

            public bool CaseCharging;

            public bool IsLidOpen;

            public DateTime LastUpdated;

            public string DeviceMac;

            public bool IsBeats;



            public int BestBattery

            {

                get

                {

                    if (LeftBattery >= 0 && RightBattery >= 0)

                    {

                        return Math.Min(LeftBattery, RightBattery);

                    }

                    if (LeftBattery >= 0) return LeftBattery;

                    if (RightBattery >= 0) return RightBattery;

                    if (CaseBattery >= 0) return CaseBattery;

                    return -1;

                }

            }



            public bool IsCharging

            {

                get

                {

                    return LeftCharging || RightCharging || CaseCharging;

                }

            }



            public string FormatBatterySummary()

            {

                List<string> parts = new List<string>();

                if (LeftBattery >= 0)

                {

                    parts.Add(string.Format("左耳: {0}%{1}", LeftBattery, LeftCharging ? "⚡" : ""));

                }

                if (RightBattery >= 0)

                {

                    parts.Add(string.Format("右耳: {0}%{1}", RightBattery, RightCharging ? "⚡" : ""));

                }

                if (CaseBattery >= 0)

                {

                    parts.Add(string.Format("仓: {0}%{1}", CaseBattery, CaseCharging ? "⚡" : ""));

                }

                if (parts.Count == 0) return "电量未知";

                return string.Join(" | ", parts.ToArray());

            }

        }



        #endregion



        #region 全球型号数据库 (AirPods, Beats, Magic Peripherals)



        public static string ResolveModelName(ushort modelId, out bool isBeats)

        {

            isBeats = false;

            switch (modelId)

            {

                case 0x2002: return "AirPods (第 1 代)";

                case 0x200F: return "AirPods (第 2 代)";

                case 0x2013: return "AirPods (第 3 代)";

                case 0x2019: return "AirPods 4";

                case 0x201B: return "AirPods 4 (主动降噪版)";

                case 0x2030:

                case 0x2036: return "AirPods (新代)";

                case 0x200E: return "AirPods Pro (第 1 代)";

                case 0x2014: return "AirPods Pro 2 (Lightning)";

                case 0x2024: return "AirPods Pro 2 (USB-C)";

                case 0x2027: return "AirPods Pro 3";

                case 0x200A: return "AirPods Max";

                case 0x201F: return "AirPods Max (USB-C)";



                // Beats 系列

                case 0x2011: isBeats = true; return "Beats Studio Buds";

                case 0x2017: isBeats = true; return "Beats Studio Buds +";

                case 0x2012: isBeats = true; return "Beats Fit Pro";

                case 0x2029:

                case 0x2032: isBeats = true; return "Beats Solo Buds";

                case 0x2026: isBeats = true; return "Beats Solo 4";

                case 0x2006: isBeats = true; return "Beats Solo3 Wireless";

                case 0x200C: isBeats = true; return "Beats Solo Pro";

                case 0x2018: isBeats = true; return "Beats Studio Pro";

                case 0x2009: isBeats = true; return "Beats Studio3 Wireless";

                case 0x200B: isBeats = true; return "Powerbeats Pro";

                case 0x2003: isBeats = true; return "Powerbeats 3 Wireless";

                case 0x200D: isBeats = true; return "Powerbeats 4";

                case 0x2010: isBeats = true; return "Beats Flex";

                case 0x2005: isBeats = true; return "BeatsX";



                default:

                    if (modelId != 0)

                    {

                        return string.Format("Apple 智能音频外设 (0x{0:X4})", modelId);

                    }

                    return "Apple / Beats 音频设备";

            }

        }



        #endregion



        #region 动态 WinRT BLE Advertisement 监听引擎 (纯原生零第三方依赖)



        private static bool isWatcherInitialized = false;

        private static object bleWatcher = null;

        private static readonly Dictionary<string, AppleEarbudsState> liveBleStates = new Dictionary<string, AppleEarbudsState>(StringComparer.OrdinalIgnoreCase);



        public class PairedAppleDeviceRecord

        {

            public string FriendlyName;

            public string DeviceMac;

            public int Pid;

            public bool IsAudio;

            public bool IsMagicPeripheral;

        }



        /// <summary>

        /// 扫描 Windows 系统中已配对或已记住的 Apple / Beats 音频与外设

        /// 核心升级: 穿透底层 SetupAPI 读取硬件 VID (0x004C / 0x05AC) 与 PID

        /// 彻底免疫用户自定义修改蓝牙名称（如改名成"摸鱼神器"或"🎧"），始终精准锁定硬件

        /// </summary>

        public static List<PairedAppleDeviceRecord> GetPairedAppleDevices()

        {

            List<PairedAppleDeviceRecord> results = new List<PairedAppleDeviceRecord>();

            HashSet<string> seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);



            // 1. 扫描 Windows SetupAPI BTHENUM 与 BTHLE 驱动树中的硬件节点 (权威硬件 VID & PID 鉴定)

            string[] enums = new string[] { "BTHENUM", "BTHLE" };

            foreach (var en in enums)

            {

                IntPtr devInfo = BluetoothDeviceHelper.SetupDiGetClassDevs(IntPtr.Zero, en, IntPtr.Zero, 0x02 | 0x04);

                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) continue;



                try

                {

                    BluetoothDeviceHelper.SP_DEVINFO_DATA data = new BluetoothDeviceHelper.SP_DEVINFO_DATA();

                    data.cbSize = (uint)Marshal.SizeOf(typeof(BluetoothDeviceHelper.SP_DEVINFO_DATA));

                    uint idx = 0;

                    while (BluetoothDeviceHelper.SetupDiEnumDeviceInfo(devInfo, idx++, ref data))

                    {

                        StringBuilder idSb = new StringBuilder(512);

                        uint req;

                        BluetoothDeviceHelper.SetupDiGetDeviceInstanceIdW(devInfo, ref data, idSb, (uint)idSb.Capacity, out req);

                        string instId = idSb.ToString();



                        byte[] nameBuf = new byte[512];

                        string fn = null;

                        uint propType;

                        if (BluetoothDeviceHelper.SetupDiGetDevicePropertyW(devInfo, ref data, ref BluetoothDeviceHelper.DEVPKEY_Device_FriendlyName, out propType, nameBuf, (uint)nameBuf.Length, out req, 0))

                        {

                            fn = Encoding.Unicode.GetString(nameBuf, 0, (int)req).TrimEnd('\0', ' ', '\t');

                        }



                        if (string.IsNullOrEmpty(fn))

                        {

                            byte[] descBuf = new byte[512];

                            if (BluetoothDeviceHelper.SetupDiGetDevicePropertyW(devInfo, ref data, ref BluetoothDeviceHelper.DEVPKEY_Device_DeviceDesc, out propType, descBuf, (uint)descBuf.Length, out req, 0))

                            {

                                fn = Encoding.Unicode.GetString(descBuf, 0, (int)req).TrimEnd('\0', ' ', '\t');

                            }

                        }



                        // 过滤纯驱动或协议总线虚拟节点

                        if (!BluetoothDeviceHelper.IsValidDeviceFriendlyName(fn))

                        {

                            continue;

                        }



                        // --- 核心防线 1: 硬件 VID 穿透判断 (无论用户改名叫什么，硬件 VID 永生不变) ---

                        bool isAppleHardware = false;

                        if (!string.IsNullOrEmpty(instId))

                        {

                            string upperId = instId.ToUpperInvariant();

                            if (upperId.Contains("VID&0001004C") || upperId.Contains("VID&000205AC") || 

                                upperId.Contains("VID_05AC") || upperId.Contains("VID_004C") ||

                                (upperId.Contains("004C") && upperId.Contains("APPLE")))

                            {

                                isAppleHardware = true;

                            }

                        }



                        // --- 核心防线 2: 名字启发式辅助判定 (未捕获到 DID 节点时的兜底) ---

                        if (!isAppleHardware && !string.IsNullOrEmpty(fn))

                        {

                            string lower = fn.ToLowerInvariant();

                            if (lower.Contains("airpods") || lower.Contains("beats") || lower.Contains("powerbeats") || lower.Contains("pill") ||

                                lower.Contains("magic mouse") || lower.Contains("magic keyboard") || lower.Contains("magic trackpad"))

                            {

                                isAppleHardware = true;

                            }

                        }



                        if (isAppleHardware)

                        {

                            // 提取硬件 PID (如 PID&200E -> 0x200E)

                            int foundPid = 0;

                            if (!string.IsNullOrEmpty(instId))

                            {

                                int pidPos = instId.IndexOf("PID&", StringComparison.OrdinalIgnoreCase);

                                if (pidPos >= 0 && pidPos + 8 <= instId.Length)

                                {

                                    string hexPart = instId.Substring(pidPos + 4, 4);

                                    int.TryParse(hexPart, System.Globalization.NumberStyles.HexNumber, null, out foundPid);

                                }

                            }



                            string mac = BluetoothDeviceHelper.ExtractMac(instId);

                            string key = (!string.IsNullOrEmpty(mac) ? mac : (!string.IsNullOrEmpty(fn) ? fn : instId)).ToLowerInvariant();

                            if (!seenKeys.Contains(key))

                            {

                                seenKeys.Add(key);

                                bool isMagic = (!string.IsNullOrEmpty(fn) && (fn.ToLower().Contains("magic") || fn.ToLower().Contains("keyboard") || fn.ToLower().Contains("mouse")));

                                var rec = new PairedAppleDeviceRecord

                                {

                                    FriendlyName = (!string.IsNullOrEmpty(fn) && BluetoothDeviceHelper.IsValidDeviceFriendlyName(fn)) ? fn : (foundPid != 0 ? ResolveModelName((ushort)foundPid, out isMagic) : "Apple / Beats 音频设备"),

                                    DeviceMac = mac,

                                    Pid = foundPid,

                                    IsAudio = !isMagic,

                                    IsMagicPeripheral = isMagic

                                };

                                results.Add(rec);

                            }

                        }

                    }

                }

                finally

                {

                    BluetoothDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);

                }

            }



            // 2. 扫描 bthprops.cpl 记住的经典蓝牙配对设备 (双重覆盖)

            try

            {

                var sp = new BluetoothDeviceHelper.BLUETOOTH_DEVICE_SEARCH_PARAMS

                {

                    dwSize = Marshal.SizeOf(typeof(BluetoothDeviceHelper.BLUETOOTH_DEVICE_SEARCH_PARAMS)),

                    fReturnAuthenticated = true,

                    fReturnRemembered = true,

                    fReturnUnknown = false,

                    fReturnConnected = false,

                    fIssueInquiry = false,

                    cTimeoutMultiplier = 2,

                    hRadio = IntPtr.Zero

                };



                var dev = new BluetoothDeviceHelper.BLUETOOTH_DEVICE_INFO();

                dev.dwSize = Marshal.SizeOf(typeof(BluetoothDeviceHelper.BLUETOOTH_DEVICE_INFO));



                IntPtr hFind = BluetoothDeviceHelper.BluetoothFindFirstDevice(ref sp, ref dev);

                if (hFind != IntPtr.Zero)

                {

                    try

                    {

                        do

                        {

                            if (!string.IsNullOrEmpty(dev.szName))

                            {

                                string lower = dev.szName.ToLowerInvariant();

                                if (lower.Contains("airpods") || lower.Contains("beats") || lower.Contains("powerbeats") || lower.Contains("pill") || lower.Contains("magic"))

                                {

                                    string mac = string.Format("{0:X12}", dev.Address);

                                    bool isMagic = lower.Contains("magic");

                                    var existing = results.Find(r => string.Equals(r.DeviceMac, mac, StringComparison.OrdinalIgnoreCase));

                                    if (existing != null)

                                    {

                                        if (!BluetoothDeviceHelper.IsValidDeviceFriendlyName(existing.FriendlyName) || existing.FriendlyName.Contains("Apple") || existing.FriendlyName.Contains("Beats"))

                                        {

                                            existing.FriendlyName = dev.szName;

                                        }

                                    }

                                    else if (!seenKeys.Contains(mac.ToLowerInvariant()))

                                    {

                                        seenKeys.Add(mac.ToLowerInvariant());

                                        var rec = new PairedAppleDeviceRecord

                                        {

                                            FriendlyName = dev.szName,

                                            DeviceMac = mac,

                                            Pid = 0,

                                            IsAudio = !isMagic,

                                            IsMagicPeripheral = isMagic

                                        };

                                        results.Add(rec);

                                    }

                                }

                            }

                        } while (BluetoothDeviceHelper.BluetoothFindNextDevice(hFind, ref dev));

                    }

                    finally

                    {

                        BluetoothDeviceHelper.BluetoothFindDeviceClose(hFind);

                    }

                }

            }

            catch (Exception diagEx) { Diagnostics.Log("Hardware/AppleDeviceHelper.cs: 已忽略异常", diagEx); }



            return results;

        }



        public static List<string> GetPairedAppleAudioDevices()

        {

            var records = GetPairedAppleDevices();

            List<string> names = new List<string>();

            for (int i = 0; i < records.Count; i++)

            {

                if (records[i].IsAudio && !string.IsNullOrEmpty(records[i].FriendlyName))

                {

                    names.Add(records[i].FriendlyName);

                }

            }

            return names;

        }



        public static void EnsureBleWatcherStarted()

        {

            var paired = GetPairedAppleAudioDevices();

            if (paired == null || paired.Count == 0) return; // 本机未配对任何苹果音频设备，绝不启动 BLE 监听器



            if (isWatcherInitialized) return;

            lock (syncLock)

            {

                if (isWatcherInitialized) return;

                isWatcherInitialized = true;



                try

                {

                    Type watcherType = Type.GetType("Windows.Devices.Bluetooth.Advertisement.BluetoothLEAdvertisementWatcher, Windows.Devices, ContentType=WindowsRuntime");

                    if (watcherType == null)

                    {

                        Assembly asm = Assembly.Load("Windows.Devices, Version=255.255.255.255, Culture=neutral, PublicKeyToken=null, ContentType=WindowsRuntime");

                        if (asm != null)

                        {

                            watcherType = asm.GetType("Windows.Devices.Bluetooth.Advertisement.BluetoothLEAdvertisementWatcher");

                        }

                    }



                    if (watcherType == null) return;



                    bleWatcher = Activator.CreateInstance(watcherType);

                    EventInfo ev = watcherType.GetEvent("Received");

                    if (ev != null && bleWatcher != null)

                    {

                        MethodInfo handlerMethod = typeof(AppleDeviceHelper).GetMethod("OnBleAdvertisementReceived", BindingFlags.Static | BindingFlags.NonPublic);

                        Delegate del = Delegate.CreateDelegate(ev.EventHandlerType, handlerMethod);



                        MethodInfo addMethod = watcherType.GetMethod("add_Received");

                        if (addMethod != null)

                        {

                            addMethod.Invoke(bleWatcher, new object[] { del });

                        }



                        MethodInfo startMethod = watcherType.GetMethod("Start");

                        if (startMethod != null)

                        {

                            startMethod.Invoke(bleWatcher, null);

                        }

                    }

                }

                catch (Exception ex)

                {

                    Debug.WriteLine("AppleDeviceHelper WinRT BLE watcher failed: " + ex.Message);

                }

            }

        }



        private static void OnBleAdvertisementReceived(object sender, object args)

        {

            try

            {

                if (args == null) return;

                var paired = GetPairedAppleAudioDevices();

                if (paired == null || paired.Count == 0) return; // 严格门禁：本机无配对设备时直接抛弃所有空中抓包

                Type argsType = args.GetType();



                PropertyInfo propAddr = argsType.GetProperty("BluetoothAddress");

                ulong addr = (propAddr != null) ? Convert.ToUInt64(propAddr.GetValue(args, null)) : 0;

                string mac = (addr != 0) ? string.Format("{0:X12}", addr) : "UNKNOWN";



                PropertyInfo propAdv = argsType.GetProperty("Advertisement");

                if (propAdv == null) return;

                object adv = propAdv.GetValue(args, null);

                if (adv == null) return;



                PropertyInfo propMfr = adv.GetType().GetProperty("ManufacturerData");

                if (propMfr == null) return;

                var mfrList = propMfr.GetValue(adv, null) as System.Collections.IEnumerable;

                if (mfrList == null) return;



                foreach (var item in mfrList)

                {

                    if (item == null) continue;

                    Type itemType = item.GetType();

                    PropertyInfo propCompanyId = itemType.GetProperty("CompanyId");

                    if (propCompanyId == null) continue;

                    ushort companyId = Convert.ToUInt16(propCompanyId.GetValue(item, null));



                    if (companyId != AppleCompanyId) continue;



                    PropertyInfo propData = itemType.GetProperty("Data");

                    if (propData == null) continue;

                    object dataObj = propData.GetValue(item, null);

                    if (dataObj == null) continue;



                    byte[] payload = ReadIBufferData(dataObj);

                    if (payload != null && payload.Length >= 27)

                    {

                        ParseAppleContinuityAdv(mac, payload);

                    }

                }

            }

            catch (Exception diagEx) { Diagnostics.Log("Hardware/AppleDeviceHelper.cs: 已忽略异常", diagEx); }

        }



        private static byte[] ReadIBufferData(object ibuffer)

        {

            try

            {

                Type readerType = Type.GetType("Windows.Storage.Streams.DataReader, Windows.Storage.Streams, ContentType=WindowsRuntime");

                if (readerType == null) return null;



                MethodInfo fromBuffer = readerType.GetMethod("FromBuffer", BindingFlags.Static | BindingFlags.Public);

                if (fromBuffer == null) return null;



                object reader = fromBuffer.Invoke(null, new object[] { ibuffer });

                if (reader == null) return null;



                PropertyInfo propLen = reader.GetType().GetProperty("UnconsumedBufferLength");

                uint len = Convert.ToUInt32(propLen.GetValue(reader, null));

                if (len == 0 || len > 256) return null;



                byte[] bytes = new byte[len];

                MethodInfo readBytes = reader.GetType().GetMethod("ReadBytes");

                readBytes.Invoke(reader, new object[] { bytes });

                return bytes;

            }

            catch

            {

                return null;

            }

        }



        public static AppleEarbudsState ParseAppleContinuityAdv(string mac, byte[] data)

        {

            if (data == null || data.Length < 27) return null;



            byte packetType = data[0];

            byte remainingLength = data[1];



            // 0x07 为 Apple Proximity Pairing (近场配对广播)

            if (packetType != 0x07) return null;



            ushort modelId = (ushort)(data[3] | (data[4] << 8));

            bool isBeats = false;

            string modelName = ResolveModelName(modelId, out isBeats);



            byte status = data[5];

            int broadcastFrom = (status >> 5) & 0x01; // 1 = Left, 0 = Right



            byte batt0 = data[6];

            byte batt1 = data[7];



            int curr = batt0 & 0x0F;

            int anot = (batt0 >> 4) & 0x0F;

            int caseBox = batt1 & 0x0F;



            bool currCharging = (batt1 & 0x10) != 0;

            bool anotCharging = (batt1 & 0x20) != 0;

            bool caseCharging = (batt1 & 0x40) != 0;



            byte lidByte = data[8];

            bool isLidClosed = ((lidByte >> 3) & 0x01) == 1;



            int leftBatt = -1;

            bool leftChg = false;

            int rightBatt = -1;

            bool rightChg = false;



            if (broadcastFrom == 1) // 当前发射侧为左耳

            {

                leftBatt = (curr <= 10) ? curr * 10 : -1;

                leftChg = currCharging;

                rightBatt = (anot <= 10) ? anot * 10 : -1;

                rightChg = anotCharging;

            }

            else // 当前发射侧为右耳

            {

                leftBatt = (anot <= 10) ? anot * 10 : -1;

                leftChg = anotCharging;

                rightBatt = (curr <= 10) ? curr * 10 : -1;

                rightChg = currCharging;

            }



            int caseBatt = (caseBox <= 10) ? caseBox * 10 : -1;



            AppleEarbudsState state = new AppleEarbudsState

            {

                ModelId = modelId,

                ModelName = modelName,

                DisplayName = modelName,

                LeftBattery = leftBatt,

                LeftCharging = leftChg,

                RightBattery = rightBatt,

                RightCharging = rightChg,

                CaseBattery = caseBatt,

                CaseCharging = caseCharging,

                IsLidOpen = !isLidClosed,

                LastUpdated = DateTime.Now,

                DeviceMac = mac,

                IsBeats = isBeats

            };



            lock (liveBleStates)

            {

                liveBleStates[modelName] = state;

            }



            return state;

        }



        #endregion



        #region Apple Magic 系列键鼠 HID Feature Report (0x47 / 0xD1) 查询



        public static bool IsAppleMagicDevice(string path, string name, int vid, int pid)

        {

            if (vid == AppleVendorId || vid == AppleBluetoothSigVid) return true;

            if (!string.IsNullOrEmpty(path))

            {

                string lower = path.ToLowerInvariant();

                if (lower.Contains("vid_05ac") || lower.Contains("vid_004c") || lower.Contains("apple")) return true;

            }

            if (!string.IsNullOrEmpty(name))

            {

                string lower = name.ToLowerInvariant();

                if (lower.Contains("magic mouse") || lower.Contains("magic keyboard") || lower.Contains("magic trackpad") || lower.Contains("apple"))

                {

                    return true;

                }

            }

            return false;

        }



        public static int QueryAppleMagicBattery(IntPtr handle)

        {

            if (handle == IntPtr.Zero || handle.ToInt64() == -1) return -1;



            try

            {

                // Apple Magic Mouse / Keyboard 常用 Feature Report 0x47 (71)

                byte[] buf47 = new byte[65];

                buf47[0] = 0x47;

                if (RazerDeviceHelper.HidD_GetFeature(handle, buf47, buf47.Length))

                {

                    int b = (int)buf47[1];

                    if (b > 0 && b <= 100) return b;

                }



                // 部分固件使用 0xD1 报文

                byte[] bufD1 = new byte[65];

                bufD1[0] = 0xD1;

                if (RazerDeviceHelper.HidD_GetFeature(handle, bufD1, bufD1.Length))

                {

                    int b = (int)bufD1[1];

                    if (b > 0 && b <= 100) return b;

                }

            }

            catch (Exception diagEx) { Diagnostics.Log("Hardware/AppleDeviceHelper.cs: 已忽略异常", diagEx); }



            return -1;

        }



        #endregion



        #region 全设备综合枚举与遥测调度



        public static List<MouseBatteryInfo> QueryAllAppleDevices()

        {

            List<MouseBatteryInfo> list = new List<MouseBatteryInfo>();



            // 1. 获取本机已配对的 Apple / Beats 硬件记录列表 (穿透硬件 VID/PID，免疫用户改名)

            var pairedRecords = GetPairedAppleDevices();

            bool hasPairedApple = (pairedRecords != null && pairedRecords.Count > 0);



            // 2. 只有当本机确实配对了苹果外设时，才启动 BLE 广播监听器并注入分耳/仓电量遥测

            if (hasPairedApple)

            {

                EnsureBleWatcherStarted();



                lock (liveBleStates)

                {

                    foreach (var kvp in liveBleStates)

                    {

                        var st = kvp.Value;

                        if (st == null) continue;



                        // 剔除超过 15 秒未收到心跳的失效状态

                        if ((DateTime.Now - st.LastUpdated).TotalSeconds > 15) continue;



                        // --- 核心多级匹配矩阵 (优先硬件 PID 对齐 -> 其次单设备自动归属 -> 再次名称语义比对) ---

                        bool isMatchLocalPaired = false;

                        string matchedLocalName = st.DisplayName;



                        // 规则 1: 硬件级 PID 精准对齐 (最权威: 无论用户改成什么蓝牙名字，固件 PID 永恒不变)

                        if (st.ModelId != 0)

                        {

                            for (int i = 0; i < pairedRecords.Count; i++)

                            {

                                if (pairedRecords[i].Pid == st.ModelId && pairedRecords[i].IsAudio)

                                {

                                    isMatchLocalPaired = true;

                                    matchedLocalName = pairedRecords[i].FriendlyName;

                                    break;

                                }

                            }

                        }



                        // 规则 2: 单设备归属自动绑定 (如果用户电脑只配对了 1 付苹果耳机，空中的 Apple Continuity 广播必然归属于它)

                        if (!isMatchLocalPaired && pairedRecords.Count == 1 && pairedRecords[0].IsAudio)

                        {

                            isMatchLocalPaired = true;

                            matchedLocalName = pairedRecords[0].FriendlyName;

                        }



                        // 规则 3: 名称语义模糊比对 (未读取到 DID PID 时的兜底)

                        if (!isMatchLocalPaired)

                        {

                            for (int i = 0; i < pairedRecords.Count; i++)

                            {

                                if (!pairedRecords[i].IsAudio) continue;

                                string pName = pairedRecords[i].FriendlyName.ToLowerInvariant();

                                string mName = st.ModelName.ToLowerInvariant();

                                if (pName.Contains(mName) || mName.Contains(pName) ||

                                    (pName.Contains("airpods pro") && mName.Contains("airpods pro")) ||

                                    (pName.Contains("airpods max") && mName.Contains("airpods max")) ||

                                    (pName.Contains("airpods") && mName.Contains("airpods")) ||

                                    (pName.Contains("beats") && mName.Contains("beats")))

                                {

                                    isMatchLocalPaired = true;

                                    matchedLocalName = pairedRecords[i].FriendlyName;

                                    break;

                                }

                            }

                        }



                        if (!isMatchLocalPaired) continue; // 非本机配对设备，绝对不加入列表！



                        MouseBatteryInfo info = new MouseBatteryInfo();

                        info.DeviceId = string.Format("Apple:{0:X4}:{1}", st.ModelId, st.ModelName);

                        info.HardwareFingerprint = string.Format("BLE:004C:{0:X4}", st.ModelId);

                        info.DeviceName = BluetoothDeviceHelper.IsValidDeviceFriendlyName(matchedLocalName) ? matchedLocalName : st.ModelName;

                        info.Brand = st.IsBeats ? "Beats" : "Apple";

                        info.Category = DeviceCategory.Earbuds;

                        info.Transport = "蓝牙 BLE (Continuity)";

                        info.IsDonglePresent = false;

                        info.IsConnected = true;

                        info.IsSleeping = false;

                        info.IsCharging = st.IsCharging;

                        info.BatteryPercent = st.BestBattery;

                        info.CustomTooltip = string.Format("{0}\r\n{1}", matchedLocalName, st.FormatBatterySummary());



                        list.Add(info);

                    }

                }

            }

            else

            {

                // 本机无配对苹果设备，清空任何残留的实时状态字典

                lock (liveBleStates)

                {

                    liveBleStates.Clear();

                }

            }



            // 3. 扫描已连接的 Apple / Beats 经典蓝牙音频与头戴耳机 (通过 Windows PnP 遥测)

            try

            {

                var connectedBt = BluetoothDeviceHelper.GetConnectedBluetoothDevices();

                if (connectedBt != null && connectedBt.Count > 0)

                {

                    foreach (var kvp in connectedBt)

                    {

                        string mac = kvp.Key;

                        string name = kvp.Value;

                        if (string.IsNullOrEmpty(name)) continue;



                        string lower = name.ToLowerInvariant();

                        bool isAppleAudio = lower.Contains("airpods") || lower.Contains("beats") || lower.Contains("powerbeats") || lower.Contains("pill");

                        bool isAppleMagic = lower.Contains("magic mouse") || lower.Contains("magic keyboard") || lower.Contains("magic trackpad");



                        // 穿透支持: 检查该设备是否在 pairedRecords 中 (硬件 VID 属于 Apple，即便被用户改名如"摸鱼神器")

                        if (!isAppleAudio && !isAppleMagic)

                        {

                            for (int p = 0; p < pairedRecords.Count; p++)

                            {

                                if ((!string.IsNullOrEmpty(mac) && string.Equals(pairedRecords[p].DeviceMac, mac, StringComparison.OrdinalIgnoreCase)) ||

                                    string.Equals(pairedRecords[p].FriendlyName, name, StringComparison.OrdinalIgnoreCase))

                                {

                                    if (pairedRecords[p].IsMagicPeripheral) isAppleMagic = true;

                                    else isAppleAudio = true;

                                    break;

                                }

                            }

                        }



                        if (!isAppleAudio && !isAppleMagic) continue;



                        int battery = -1;

                        string fn = null;

                        bool connected = BluetoothDeviceHelper.QueryByMac(mac, name, out battery, out fn);



                        if (connected)

                        {

                            // 检查是否已经在 BLE 实时列表中

                            bool alreadyInList = false;

                            foreach (var exist in list)

                            {

                                if (exist.DeviceName != null && exist.DeviceName.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0)

                                {

                                    alreadyInList = true;

                                    break;

                                }

                            }



                            if (!alreadyInList)

                            {

                                MouseBatteryInfo bInfo = new MouseBatteryInfo();

                                bInfo.DeviceId = string.Format("AppleBT:{0}", mac);

                                bInfo.HardwareFingerprint = string.Format("BT:{0}", mac);

                                string validFn = BluetoothDeviceHelper.IsValidDeviceFriendlyName(fn) ? fn : null;

                                string validName = BluetoothDeviceHelper.IsValidDeviceFriendlyName(name) ? name : null;

                                bInfo.DeviceName = !string.IsNullOrEmpty(validFn) ? validFn : (!string.IsNullOrEmpty(validName) ? validName : "Beats 蓝牙音频设备");

                                bInfo.Brand = (lower.Contains("beats") || lower.Contains("powerbeats")) ? "Beats" : "Apple";



                                if (isAppleMagic)

                                {

                                    if (lower.Contains("mouse")) bInfo.Category = DeviceCategory.Mouse;

                                    else if (lower.Contains("keyboard")) bInfo.Category = DeviceCategory.Keyboard;

                                    else bInfo.Category = DeviceCategory.Mouse;

                                    bInfo.Transport = "蓝牙 HID";

                                }

                                else

                                {

                                    bInfo.Category = (lower.Contains("solo") || lower.Contains("studio") || lower.Contains("max") || lower.Contains("pill")) ? DeviceCategory.Headset : DeviceCategory.Earbuds;

                                    bInfo.Transport = lower.Contains("pill") ? "蓝牙 Classic (便携音箱)" : "蓝牙 Classic / AAC";

                                }



                                bInfo.IsDonglePresent = false;

                                bInfo.IsConnected = true;

                                bInfo.IsSleeping = false;

                                bInfo.IsCharging = false;



                                int effectiveBattery = battery;

                                if (effectiveBattery < 0)

                                {

                                    try

                                    {

                                        var cachedList = DeviceManager.LoadCustomDevices();

                                        for (int c = 0; c < cachedList.Count; c++)

                                        {

                                            if (cachedList[c].DeviceId.Equals(bInfo.DeviceId, StringComparison.OrdinalIgnoreCase) ||

                                                (!string.IsNullOrEmpty(cachedList[c].HardwareFingerprint) && cachedList[c].HardwareFingerprint.Equals(bInfo.HardwareFingerprint, StringComparison.OrdinalIgnoreCase)))

                                            {

                                                if (cachedList[c].CachedBattery > 0)

                                                {

                                                    effectiveBattery = cachedList[c].CachedBattery;

                                                    break;

                                                }

                                            }

                                        }

                                    }

                                    catch (Exception diagEx) { Diagnostics.Log("Hardware/AppleDeviceHelper.cs: 已忽略异常", diagEx); }

                                }



                                bInfo.BatteryPercent = effectiveBattery;



                                list.Add(bInfo);

                            }

                        }

                    }

                }

            }

            catch (Exception diagEx) { Diagnostics.Log("Hardware/AppleDeviceHelper.cs: 已忽略异常", diagEx); }



            // 4. 扫描 USB 或蓝牙 HID 直连的 Apple Magic 键鼠设备

            try

            {

                Guid hidGuid;

                RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);

                IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);

                if (devInfo != IntPtr.Zero && devInfo != new IntPtr(-1))

                {

                    RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();

                    uint memberIdx = 0;

                    HashSet<string> seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);



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

                                if (devicePath != null && !seenPaths.Contains(devicePath))

                                {

                                    seenPaths.Add(devicePath);



                                    IntPtr handle = RazerDeviceHelper.CreateFile(

                                        devicePath,

                                        0x40000000, // GENERIC_WRITE

                                        RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,

                                        IntPtr.Zero,

                                        RazerDeviceHelper.OPEN_EXISTING,

                                        0,

                                        IntPtr.Zero);



                                    if (handle != IntPtr.Zero && handle.ToInt64() != -1)

                                    {

                                        try

                                        {

                                            RazerDeviceHelper.HIDD_ATTRIBUTES attr = new RazerDeviceHelper.HIDD_ATTRIBUTES();

                                            attr.Size = Marshal.SizeOf(attr);

                                            RazerDeviceHelper.HidD_GetAttributes(handle, ref attr);



                                            StringBuilder sbProd = new StringBuilder(256);

                                            string prodStr = RazerDeviceHelper.HidD_GetProductString(handle, sbProd, sbProd.Capacity) ? sbProd.ToString().Trim() : "";



                                            if (IsAppleMagicDevice(devicePath, prodStr, attr.VendorID, attr.ProductID))

                                            {

                                                int batt = QueryAppleMagicBattery(handle);

                                                string magicName = !string.IsNullOrEmpty(prodStr) ? prodStr : "Apple Magic Device";



                                                MouseBatteryInfo mInfo = new MouseBatteryInfo();

                                                mInfo.DeviceId = string.Format("AppleHID:{0:X4}:{1:X4}", attr.VendorID, attr.ProductID);

                                                mInfo.HardwareFingerprint = string.Format("HID:{0:X4}:{1:X4}", attr.VendorID, attr.ProductID);

                                                mInfo.DeviceName = magicName;

                                                mInfo.Brand = "Apple";

                                                mInfo.Category = magicName.ToLower().Contains("keyboard") ? DeviceCategory.Keyboard : DeviceCategory.Mouse;

                                                mInfo.Transport = "蓝牙 / 雷雳接口";

                                                mInfo.IsDonglePresent = false;

                                                mInfo.IsConnected = true;

                                                mInfo.IsSleeping = false;

                                                mInfo.IsCharging = false;

                                                mInfo.BatteryPercent = batt >= 0 ? batt : -1;



                                                list.Add(mInfo);

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

                        }

                    }

                    finally

                    {

                        RazerDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);

                    }

                }

            }

            catch (Exception diagEx) { Diagnostics.Log("Hardware/AppleDeviceHelper.cs: 已忽略异常", diagEx); }



            return list;

        }



        #endregion

    }

}

