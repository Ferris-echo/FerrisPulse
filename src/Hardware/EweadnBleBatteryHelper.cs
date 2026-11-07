// ============================================================================
// 模块名称: EweadnBleBatteryHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   前行者 X87 键盘的**蓝牙 (BLE) 电量读取**。X87 是三模键盘（2.4G / 蓝牙 / 有线），
//   电量只在蓝牙模式下由标准 GATT Battery Service 提供：
//     · Service 0x180F (Battery Service)
//     · Characteristic 0x2A19 (Battery Level，1 字节 0~100)
//   2.4G 接收器 (0C45:FEFE) 不把电量转发到 USB 侧，因此 2.4G 下不可读（见 EweadnDeviceHelper 说明）。
//
// 实现方式（零外部依赖、严格 C# 5.0 / .NET 4.0）:
//   通过 WinRT (Windows.Devices.Bluetooth / GenericAttributeProfile) 读取 GATT。
//   WinRT 投影在 .NET Framework 4.0 下需按名称反射调用，不能直接引用类型，
//   故此处统一用 Type.GetType(...).GetMethod(...).Invoke 的方式，避免引入任何新引用。
//   任何一步失败都安全返回 -1，绝不上抛异常影响主轮询。
//
// 实测依据 (2026-11-07 本机):
//   注册表 HKLM\SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Devices\cc415e28335e
//     LEName       = "X87-5.0-BT2"
//     VID / PID    = 3141 (0x0C45) / 65278 (0xFEFE)，REV 0138 —— 与 USB 接收器完全一致
//     LmpVersion   = 12 (蓝牙 5.x)
//   Windows 为该设备枚举出 Battery Service 节点
//     BTHLEDEVICE\{0000180F-...}_DEV_VID&010C45_PID&FEFE_REV&0138_CC415E28335E
//   但设备当前使用 2.4G 时该节点状态为 Unknown（未连接），此时读取返回 -1 —— 属预期行为。
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 修改 GATT UUID、反射目标或缓存策略时必须同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止字符串插值、空条件运算符、nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Reflection;
using System.Threading;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    public static class EweadnBleBatteryHelper
    {
        // ---------- GATT UUID ----------
        public const string BatteryServiceUuid = "0000180f-0000-1000-8000-00805f9b34fb";
        public const string BatteryLevelCharacteristicUuid = "00002a19-0000-1000-8000-00805f9b34fb";

        private const string BthDevicesKey = @"SYSTEM\CurrentControlSet\Services\BTHPORT\Parameters\Devices";
        private const int ReadTimeoutMs = 6000;

        private static readonly object SyncRoot = new object();
        private static int cachedPercent = -1;
        private static DateTime cachedAt = DateTime.MinValue;
        private static string cachedName = "";
        private const int CacheSeconds = 60;

        /// <summary>最近一次读到的电量百分比；-1 表示不可用（未在蓝牙模式或读取失败）。</summary>
        public static int LastBatteryPercent { get { return cachedPercent; } }

        /// <summary>最近一次成功读取的时间。</summary>
        public static DateTime LastSampleTime { get { return cachedAt; } }

        /// <summary>已配对的蓝牙设备名（如 "X87-5.0-BT2"），未找到返回空串。</summary>
        public static string PairedDeviceName { get { return cachedName; } }

        /// <summary>
        /// 从注册表枚举已配对的 BLE 设备，找出 VID=0x0C45 且 PID=0xFEFE 的键盘，
        /// 返回其蓝牙 MAC（格式 "AA:BB:CC:DD:EE:FF"）；未找到返回 null。
        /// 仅读注册表，不需要设备处于连接态，故可用来判断「该键盘是否曾以蓝牙配对过」。
        /// </summary>
        public static string FindPairedKeyboardMac(out string friendlyName)
        {
            friendlyName = "";
            try
            {
                using (RegistryKey root = Registry.LocalMachine.OpenSubKey(BthDevicesKey, false))
                {
                    if (root == null) return null;
                    string[] names = root.GetSubKeyNames();
                    for (int i = 0; i < names.Length; i++)
                    {
                        string mac = names[i];
                        if (string.IsNullOrEmpty(mac) || mac.Length != 12) continue;
                        using (RegistryKey dev = root.OpenSubKey(mac, false))
                        {
                            if (dev == null) continue;
                            object vidObj = dev.GetValue("VID");
                            object pidObj = dev.GetValue("PID");
                            if (vidObj == null || pidObj == null) continue;
                            int vid = Convert.ToInt32(vidObj, CultureInfo.InvariantCulture);
                            int pid = Convert.ToInt32(pidObj, CultureInfo.InvariantCulture);
                            if (vid != EweadnDeviceHelper.VendorId) continue;
                            if (pid != EweadnDeviceHelper.ProductIdDongle) continue;

                            byte[] leName = dev.GetValue("LEName") as byte[];
                            string display = "";
                            if (leName != null && leName.Length > 0)
                            {
                                display = System.Text.Encoding.UTF8.GetString(leName).TrimEnd('\0');
                            }
                            friendlyName = display;
                            cachedName = display;
                            return FormatMac(mac);
                        }
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private static string FormatMac(string hex12)
        {
            if (string.IsNullOrEmpty(hex12) || hex12.Length != 12) return hex12;
            return hex12.Substring(0, 2) + ":" + hex12.Substring(2, 2) + ":" +
                   hex12.Substring(4, 2) + ":" + hex12.Substring(6, 2) + ":" +
                   hex12.Substring(8, 2) + ":" + hex12.Substring(10, 2);
        }

        /// <summary>
        /// 读取蓝牙电量。成功返回 0~100，失败返回 -1。
        /// 带 60 秒缓存，避免高频轮询反复打开 GATT 影响键盘响应。
        /// </summary>
        public static int QueryBattery()
        {
            lock (SyncRoot)
            {
                if (cachedPercent >= 0 && (DateTime.Now - cachedAt).TotalSeconds < CacheSeconds)
                {
                    return cachedPercent;
                }

                string name;
                string mac = FindPairedKeyboardMac(out name);
                if (string.IsNullOrEmpty(mac)) return -1;

                int pct = ReadGattBattery(mac);
                if (pct >= 0)
                {
                    cachedPercent = pct;
                    cachedAt = DateTime.Now;
                }
                return pct;
            }
        }

        /// <summary>
        /// 用 WinRT 反射读取 GATT Battery Level。
        /// 全程 try/catch，任何失败返回 -1。
        /// </summary>
        private static int ReadGattBattery(string mac)
        {
            try
            {
                ulong address = ParseMacToUInt64(mac);
                if (address == 0) return -1;

                Type tDevice = FindWinRtType("Windows.Devices.Bluetooth.BluetoothLEDevice");
                Type tService = FindWinRtType("Windows.Devices.Bluetooth.GenericAttributeProfile.GattDeviceService");
                Type tChar = FindWinRtType("Windows.Devices.Bluetooth.GenericAttributeProfile.GattCharacteristic");
                if (tDevice == null || tService == null || tChar == null) return -1;

                // BluetoothLEDevice.FromBluetoothAddressAsync(ulong)
                MethodInfo fromAddr = FindMethod(tDevice, "FromBluetoothAddressAsync", 1);
                if (fromAddr == null) return -1;
                object devOp = fromAddr.Invoke(null, new object[] { address });
                object device = AwaitWinRt(devOp, tDevice);
                if (device == null) return -1;   // 设备不在 BLE 连接态（多为正在使用 2.4G/有线）

                // GetGattServicesForUuidAsync(Guid)
                MethodInfo getSvc = FindMethod(tDevice, "GetGattServicesForUuidAsync", 1);
                if (getSvc == null) return -1;
                Guid svcGuid = new Guid(BatteryServiceUuid);
                object svcOp = getSvc.Invoke(device, new object[] { svcGuid });
                object svcResult = AwaitWinRt(svcOp, null);
                if (svcResult == null) return -1;

                object services = GetPropertyOrField(svcResult, "Services");
                object svc = FirstItem(services);
                if (svc == null) return -1;

                // GetCharacteristicsForUuidAsync(Guid)
                MethodInfo getChar = FindMethod(tService, "GetCharacteristicsForUuidAsync", 1);
                if (getChar == null) return -1;
                Guid charGuid = new Guid(BatteryLevelCharacteristicUuid);
                object charOp = getChar.Invoke(svc, new object[] { charGuid });
                object charResult = AwaitWinRt(charOp, null);
                if (charResult == null) return -1;

                object chars = GetPropertyOrField(charResult, "Characteristics");
                object ch = FirstItem(chars);
                if (ch == null) return -1;

                // ReadValueAsync(BluetoothCacheMode.Uncached = 1)
                MethodInfo readValue = FindMethod(tChar, "ReadValueAsync", 1);
                if (readValue == null) return -1;
                Type tCacheMode = FindWinRtType("Windows.Devices.Bluetooth.BluetoothCacheMode");
                object cacheMode = tCacheMode != null ? Enum.ToObject(tCacheMode, 1) : 1;
                object readOp;
                try { readOp = readValue.Invoke(ch, new object[] { cacheMode }); }
                catch { readOp = readValue.Invoke(ch, new object[] { 1 }); }
                object readResult = AwaitWinRt(readOp, null);
                if (readResult == null) return -1;

                object status = GetPropertyOrField(readResult, "Status");
                if (status != null && status.ToString() != "Success") return -1;

                object buffer = GetPropertyOrField(readResult, "Value");
                byte[] bytes = BufferToBytes(buffer);
                if (bytes == null || bytes.Length < 1) return -1;
                int pct = bytes[0];
                if (pct < 0 || pct > 100) return -1;
                return pct;
            }
            catch
            {
                return -1;
            }
        }

        private static Type FindWinRtType(string fullName)
        {
            try
            {
                Type t = Type.GetType(fullName + ", Windows.Devices.Bluetooth, ContentType=WindowsRuntime", false);
                if (t != null) return t;
            }
            catch { }
            try
            {
                // 逐程序集探测（部分系统上投影程序集名不同）
                Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < asms.Length; i++)
                {
                    try
                    {
                        Type t = asms[i].GetType(fullName, false);
                        if (t != null) return t;
                    }
                    catch { }
                }
            }
            catch { }
            return null;
        }

        private static MethodInfo FindMethod(Type type, string name, int paramCount)
        {
            try
            {
                MethodInfo[] ms = type.GetMethods(BindingFlags.Public | BindingFlags.Static | BindingFlags.Instance);
                for (int i = 0; i < ms.Length; i++)
                {
                    if (ms[i].Name != name) continue;
                    if (ms[i].GetParameters().Length == paramCount) return ms[i];
                }
            }
            catch { }
            return null;
        }

        private static object GetPropertyOrField(object target, string name)
        {
            if (target == null) return null;
            try
            {
                PropertyInfo p = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
                if (p != null) return p.GetValue(target, null);
            }
            catch { }
            try
            {
                FieldInfo f = target.GetType().GetField(name, BindingFlags.Public | BindingFlags.Instance);
                if (f != null) return f.GetValue(target);
            }
            catch { }
            return null;
        }

        private static object FirstItem(object collection)
        {
            if (collection == null) return null;
            try
            {
                PropertyInfo countProp = collection.GetType().GetProperty("Count");
                if (countProp != null)
                {
                    int count = Convert.ToInt32(countProp.GetValue(collection, null), CultureInfo.InvariantCulture);
                    if (count <= 0) return null;
                }
                PropertyInfo indexer = collection.GetType().GetProperty("Item");
                if (indexer != null) return indexer.GetValue(collection, new object[] { 0 });
            }
            catch { }
            try
            {
                System.Collections.IEnumerable e = collection as System.Collections.IEnumerable;
                if (e != null)
                {
                    foreach (object o in e) return o;
                }
            }
            catch { }
            return null;
        }

        private static byte[] BufferToBytes(object buffer)
        {
            if (buffer == null) return null;
            try
            {
                // IBuffer -> byte[] 通过扩展方法 CopyToByteArray
                Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < asms.Length; i++)
                {
                    Type ext = null;
                    try { ext = asms[i].GetType("System.Runtime.InteropServices.WindowsRuntime.WindowsRuntimeBufferExtensions", false); }
                    catch { }
                    if (ext == null) continue;
                    MethodInfo[] ms = ext.GetMethods(BindingFlags.Public | BindingFlags.Static);
                    for (int k = 0; k < ms.Length; k++)
                    {
                        if (ms[k].Name != "ToArray") continue;
                        ParameterInfo[] ps = ms[k].GetParameters();
                        if (ps.Length != 1) continue;
                        try
                        {
                            object r = ms[k].Invoke(null, new object[] { buffer });
                            byte[] arr = r as byte[];
                            if (arr != null) return arr;
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return null;
        }

        private static ulong ParseMacToUInt64(string mac)
        {
            if (string.IsNullOrEmpty(mac)) return 0;
            string hex = mac.Replace(":", "").Replace("-", "");
            if (hex.Length != 12) return 0;
            ulong v;
            if (ulong.TryParse(hex, NumberStyles.HexNumber, CultureInfo.InvariantCulture, out v)) return v;
            return 0;
        }

        /// <summary>
        /// 同步等待 WinRT IAsyncOperation。通过 AsTask 扩展反射实现，带超时。
        /// </summary>
        private static object AwaitWinRt(object asyncOp, Type resultTypeHint)
        {
            if (asyncOp == null) return null;
            try
            {
                Type extType = null;
                Assembly[] asms = AppDomain.CurrentDomain.GetAssemblies();
                for (int i = 0; i < asms.Length; i++)
                {
                    try { extType = asms[i].GetType("System.WindowsRuntimeSystemExtensions", false); }
                    catch { }
                    if (extType != null) break;
                }
                if (extType == null) return null;

                MethodInfo asTask = null;
                MethodInfo[] ms = extType.GetMethods(BindingFlags.Public | BindingFlags.Static);
                for (int i = 0; i < ms.Length; i++)
                {
                    if (ms[i].Name != "AsTask") continue;
                    ParameterInfo[] ps = ms[i].GetParameters();
                    if (ps.Length != 1) continue;
                    string pn = ps[0].ParameterType.Name;
                    if (pn.StartsWith("IAsyncOperation", StringComparison.Ordinal))
                    {
                        asTask = ms[i];
                        break;
                    }
                }
                if (asTask == null) return null;

                // 泛型参数取 IAsyncOperation<T> 的 T
                Type iop = asyncOp.GetType();
                Type argType = null;
                Type[] ifaces = iop.GetInterfaces();
                for (int i = 0; i < ifaces.Length; i++)
                {
                    if (ifaces[i].IsGenericType &&
                        ifaces[i].GetGenericTypeDefinition().Name.StartsWith("IAsyncOperation", StringComparison.Ordinal))
                    {
                        argType = ifaces[i].GetGenericArguments()[0];
                        break;
                    }
                }
                if (argType == null) argType = resultTypeHint;
                if (argType == null) return null;

                MethodInfo closed = asTask.MakeGenericMethod(argType);
                object task = closed.Invoke(null, new object[] { asyncOp });
                System.Threading.Tasks.Task t = task as System.Threading.Tasks.Task;
                if (t == null) return null;
                if (!t.Wait(ReadTimeoutMs)) return null;
                if (t.IsFaulted || t.IsCanceled) return null;

                PropertyInfo resProp = t.GetType().GetProperty("Result");
                if (resProp != null) return resProp.GetValue(t, null);
            }
            catch { }
            return null;
        }
    }
}
