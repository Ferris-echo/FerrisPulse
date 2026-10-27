// ============================================================================
// 模块名称: UniversalGamingDeviceHelper.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   OpenMouse 与 All-in-One-Mouse-Driver (Mouse Hub) 全量外设协议引擎
//   涵盖 20+ 个品牌、数百款主流电竞/客制化/原相方案鼠标及键盘：
//   1. CompX / PixArt 64字节 Feature 引擎 (Lamzu, Pulsar, WLmouse, Attack Shark, Ajazz, Darmoshark, Glorious, Ninjutso, RAWM, Incott, Delux, Vancer, Fantech, Finalmouse)
//   2. Beken / ATK / Endgame Gear 16字节 EEPROM 引擎 (ATK A9/F1/X1, VXE R1 全系, Endgame Gear OP1we/XM2we)
//   3. Vaxee 64字节专用控制引擎 (NP-01S, NP-01S v2, XE, AX, OUTSET AX, E1)
//   4. Asus ROG / TUF, SteelSeries, Corsair, Dareu, Keychron, Wooting 厂商协议引擎
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
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;
using Microsoft.Win32.SafeHandles;

namespace RazerBatteryTray
{
    public static class UniversalGamingDeviceHelper
    {
        private static readonly object hidLock = new object();
        private const string CacheRegistryKey = @"Software\RazerBatteryTray\UniversalGamingCache";

        private const uint GENERIC_READ = 0x80000000;
        private const uint GENERIC_WRITE = 0x40000000;
        private const uint FILE_FLAG_OVERLAPPED = 0x40000000;
        private const uint WAIT_OBJECT_0 = 0;
        private const int ERROR_IO_PENDING = 997;

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
        private static extern bool ResetEvent(IntPtr hEvent);

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

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern IntPtr CreateFileW(
            string lpFileName,
            uint dwDesiredAccess,
            uint dwShareMode,
            IntPtr lpSecurityAttributes,
            uint dwCreationDisposition,
            uint dwFlagsAndAttributes,
            IntPtr hTemplateFile);

        [DllImport("setupapi.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool SetupDiGetDeviceInterfaceDetailW(
            IntPtr DeviceInfoSet,
            ref RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA DeviceInterfaceData,
            IntPtr DeviceInterfaceDetailData,
            uint DeviceInterfaceDetailDataSize,
            out uint RequiredSize,
            IntPtr DeviceInfoData);

        #region 品牌 VID 常量定义 (汇总自 OpenMouse 与 All-in-One)

        public const int VID_LAMZU_OFFICIAL   = 0x37B0; // Lamzu 官方 (Inca 8K, Paro 等)
        public const int VID_COMPX_LAMZU      = 0x373E; // CompX / Lamzu / Attack Shark / Vancer / LunaFury
        public const int VID_PULSAR           = 0x3710; // Pulsar 官方
        public const int VID_WLMOUSE          = 0x36A7; // WLmouse 官方 (Beast X 全系, Sword X)
        public const int VID_ENDGAME_GEAR     = 0x3367; // Endgame Gear (OP1we, XM2we)
        public const int VID_ATK              = 0x373B; // ATK / 烈空 (A9, F1, X1, VXE R1)
        public const int VID_VGN_COMPX        = 0x3554; // VGN / 蜻蜓 F1, Darmoshark M3, Ninjutso, RAWM, Teevolution
        public const int VID_ATTACK_SHARK_1   = 0x25A7; // Attack Shark (X3, R1 2.4G)
        public const int VID_ATTACK_SHARK_2   = 0x1D57; // Attack Shark (R1 / X11 OEM)
        public const int VID_MICLINK_OEM      = 0x3151; // MicLink / mlzn OEM (GearHub, Attack Shark R2, etc.)
        public const int VID_AJAZZ_USB        = 0x1A2C; // Ajazz (AJ199, AJ139 Pro, AJ159, AK820)
        public const int VID_MOTOSPEED        = 0x258A; // Motospeed / Darmoshark / SinoWealth / Glorious Classic
        public const int VID_GLORIOUS_PIXART  = 0x093A; // Glorious Model O2 / Incott GHero
        public const int VID_GLORIOUS_I       = 0x22D4; // Glorious Model I
        public const int VID_GLORIOUS_WIRED   = 0x320F; // Glorious Model O V2 / I 2 Wired
        public const int VID_GLORIOUS_O3      = 0x3794; // Glorious Model O3 Wireless
        public const int VID_NINJUTSO_LEGACY  = 0x248A; // Ninjutso Origin One X
        public const int VID_FINALMOUSE       = 0x361D; // Finalmouse (UltralightX, Starlight-12)
        public const int VID_KEYCHRON         = 0x3434; // Keychron (M1, M2, M3, M6)
        public const int VID_WOOTING          = 0x31E3; // Wooting (60HE, 80HE, Two HE)
        public const int VID_ASUS             = 0x0B05; // ASUS ROG / TUF (Gladius, Keris, Harpe Ace)
        public const int VID_STEELSERIES      = 0x1038; // SteelSeries (Aerox, Rival, Prime)
        public const int VID_CORSAIR          = 0x1B1C; // Corsair (Sabre, M65, Dark Core)
        public const int VID_DAREU            = 0x260D; // Dareu (A950 等)
        public const int VID_ANGRY_MIAO_1     = 0x0E8D; // 怒喵 AM Infinity 2.4G / 有线
        public const int VID_ANGRY_MIAO_2     = 0x35A1; // 怒喵 AM Infinity 有线变体
        public const int VID_MCHOSE           = 0x3837; // MCHOSE 迈从 (A5, G3, AX5)
        public const int VID_REDRAGON         = 0x258A; // Redragon / SinoWealth (M690 Pro)
        public const int VID_GWOLVES          = 0x33E4; // G-Wolves (HTX, HTS, HSK)
        public const int VID_ZAUNKOENIG       = 0x3404; // Zaunkoenig (M2K, M3K)
        public const int VID_FATER            = 0x04D9; // Holtek / Fater
        public const int VID_MICROSOFT        = 0x045E; // Microsoft (Pro IntelliMouse)

        #endregion

        #region 产品信息与映射结构

        public class ProductEntry
        {
            public int Vid;
            public int Pid;
            public string Brand;
            public string ModelName;
            public DeviceCategory Category;
            public int MaxPollingRate;

            public ProductEntry(int vid, int pid, string brand, string model, DeviceCategory cat, int maxPoll)
            {
                Vid = vid;
                Pid = pid;
                Brand = brand;
                ModelName = model;
                Category = cat;
                MaxPollingRate = maxPoll;
            }
        }

        private static readonly Dictionary<string, ProductEntry> catalog = new Dictionary<string, ProductEntry>(StringComparer.OrdinalIgnoreCase);

        public static int[] GetSupportedPollingRatesByMax(int maxPoll)
        {
            if (maxPoll >= 8000) return new int[] { 125, 250, 500, 1000, 2000, 4000, 8000 };
            if (maxPoll >= 4000) return new int[] { 125, 250, 500, 1000, 2000, 4000 };
            if (maxPoll >= 2000) return new int[] { 125, 250, 500, 1000, 2000 };
            return new int[] { 125, 250, 500, 1000 };
        }

        static UniversalGamingDeviceHelper()
        {
            RegisterCatalog();
        }

        private static void AddProduct(int vid, int pid, string brand, string model, DeviceCategory cat, int maxPoll)
        {
            string key = string.Format("{0:X4}:{1:X4}", vid, pid);
            catalog[key] = new ProductEntry(vid, pid, brand, model, cat, maxPoll);
        }

        private static void RegisterCatalog()
        {
            // --- LAMZU ---
            AddProduct(VID_LAMZU_OFFICIAL, 0x0010, "LAMZU", "LAMZU 8K Receiver (Inca / Paro)", DeviceCategory.Mouse, 8000);
            AddProduct(VID_LAMZU_OFFICIAL, 0x0007, "LAMZU", "LAMZU PARO (有线模式)", DeviceCategory.Mouse, 1000);
            AddProduct(VID_LAMZU_OFFICIAL, 0x0009, "LAMZU", "LAMZU INCA (有线模式)", DeviceCategory.Mouse, 1000);
            AddProduct(VID_COMPX_LAMZU,    0x0001, "LAMZU", "LAMZU Atlantis Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_COMPX_LAMZU,    0x0002, "LAMZU", "LAMZU Atlantis 4K", DeviceCategory.Mouse, 4000);
            AddProduct(VID_COMPX_LAMZU,    0x0003, "LAMZU", "LAMZU Thorn Wireless", DeviceCategory.Mouse, 4000);
            AddProduct(VID_COMPX_LAMZU,    0x0004, "LAMZU", "LAMZU Maya Wireless", DeviceCategory.Mouse, 4000);
            AddProduct(VID_COMPX_LAMZU,    0x0005, "LAMZU", "LAMZU Maya X 8K", DeviceCategory.Mouse, 8000);

            // --- VAXEE ---
            AddProduct(0x3554, 0x1010, "VAXEE", "VAXEE E1 Wireless", DeviceCategory.Mouse, 4000);
            AddProduct(0x3554, 0x1011, "VAXEE", "VAXEE NP-01S Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(0x3554, 0x1012, "VAXEE", "VAXEE NP-01S v2 4K", DeviceCategory.Mouse, 4000);
            AddProduct(0x3554, 0x1013, "VAXEE", "VAXEE XE Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(0x3554, 0x1014, "VAXEE", "VAXEE AX Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(0x3554, 0x1015, "VAXEE", "VAXEE OUTSET AX Wireless", DeviceCategory.Mouse, 1000);

            // --- WLMOUSE ---
            AddProduct(VID_WLMOUSE, 0xA860, "WLmouse", "Beast G Wireless", DeviceCategory.Mouse, 8000);
            AddProduct(VID_WLMOUSE, 0xA861, "WLmouse", "Beast G 有线", DeviceCategory.Mouse, 8000);
            AddProduct(VID_WLMOUSE, 0xA863, "WLmouse", "Huan Wireless", DeviceCategory.Mouse, 8000);
            AddProduct(VID_WLMOUSE, 0xA866, "WLmouse", "Beast Miao Wireless", DeviceCategory.Mouse, 8000);
            AddProduct(VID_WLMOUSE, 0xA868, "WLmouse", "Beast Mini Pro", DeviceCategory.Mouse, 8000);
            AddProduct(VID_WLMOUSE, 0xA870, "WLmouse", "Beast X Pro", DeviceCategory.Mouse, 8000);
            AddProduct(VID_WLMOUSE, 0xA872, "WLmouse", "Strider Wireless", DeviceCategory.Mouse, 8000);
            AddProduct(VID_WLMOUSE, 0xA874, "WLmouse", "Ying Wireless", DeviceCategory.Mouse, 8000);
            AddProduct(VID_WLMOUSE, 0xA878, "WLmouse", "Sword X Wireless", DeviceCategory.Mouse, 8000);
            AddProduct(VID_WLMOUSE, 0xA880, "WLmouse", "Beast Max Wireless", DeviceCategory.Mouse, 8000);
            AddProduct(VID_WLMOUSE, 0xA882, "WLmouse", "WLmouse 1K Receiver", DeviceCategory.Mouse, 1000);
            AddProduct(VID_WLMOUSE, 0xA883, "WLmouse", "Beast X Wireless", DeviceCategory.Mouse, 4000);
            AddProduct(VID_WLMOUSE, 0xA884, "WLmouse", "Beast X 有线", DeviceCategory.Mouse, 4000);
            AddProduct(VID_WLMOUSE, 0xA885, "WLmouse", "Beast Mini Wireless", DeviceCategory.Mouse, 4000);

            // --- PULSAR ---
            AddProduct(VID_PULSAR, 0x0001, "Pulsar", "X2 Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_PULSAR, 0x0002, "Pulsar", "X2 Mini Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_PULSAR, 0x0003, "Pulsar", "X2V2 Wireless", DeviceCategory.Mouse, 4000);
            AddProduct(VID_PULSAR, 0x0004, "Pulsar", "X2V2 Mini Wireless", DeviceCategory.Mouse, 4000);
            AddProduct(VID_PULSAR, 0x0005, "Pulsar", "X2H Wireless", DeviceCategory.Mouse, 4000);
            AddProduct(VID_PULSAR, 0x0006, "Pulsar", "X2A Wireless", DeviceCategory.Mouse, 4000);
            AddProduct(VID_PULSAR, 0x0007, "Pulsar", "Xlite V2 Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_PULSAR, 0x0008, "Pulsar", "Xlite V3 Wireless", DeviceCategory.Mouse, 4000);
            AddProduct(VID_PULSAR, 0x0009, "Pulsar", "Xlite V3 eS 8K", DeviceCategory.Mouse, 8000);

            // --- ENDGAME GEAR ---
            AddProduct(VID_ENDGAME_GEAR, 0x0001, "Endgame Gear", "OP1we Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_ENDGAME_GEAR, 0x0002, "Endgame Gear", "XM2we Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_ENDGAME_GEAR, 0x0003, "Endgame Gear", "OP1 8K 有线", DeviceCategory.Mouse, 8000);

            // --- ATK / VXE ---
            AddProduct(VID_ATK, 0x1085, "VXE", "VXE Dragonfly R1 SE / SE+", DeviceCategory.Mouse, 2000);
            AddProduct(VID_VGN_COMPX, 0xF58A, "VXE", "VXE Dragonfly R1 Pro Max 接收器", DeviceCategory.Mouse, 4000);
            AddProduct(VID_VGN_COMPX, 0xF58C, "VXE", "VXE Dragonfly R1 Pro Max 有线", DeviceCategory.Mouse, 4000);
            AddProduct(VID_VGN_COMPX, 0xF58E, "VXE", "VXE Dragonfly R1 2.4G 接收器", DeviceCategory.Mouse, 4000);
            AddProduct(VID_VGN_COMPX, 0xF58F, "VXE", "VXE Dragonfly R1 鼠标", DeviceCategory.Mouse, 4000);
            AddProduct(VID_ATK, 0x10C9, "ATK", "ATK Nearlink 接收器 (A9 / 烈空)", DeviceCategory.Mouse, 8000);
            AddProduct(VID_ATK, 0x1086, "ATK", "ATK F1 Pro Max Wireless", DeviceCategory.Mouse, 8000);
            AddProduct(VID_ATK, 0x1087, "ATK", "ATK F1 Ultimate 8K", DeviceCategory.Mouse, 8000);
            AddProduct(VID_ATK, 0x1088, "ATK", "ATK X1 Pro Wireless", DeviceCategory.Mouse, 8000);
            AddProduct(VID_ATK, 0x1089, "ATK", "ATK X1 Pro Max Wireless", DeviceCategory.Mouse, 8000);
            AddProduct(VID_ATK, 0x11D9, "ATK", "ATK F1 Ultimate 2.0", DeviceCategory.Mouse, 8000);

            // --- ATTACK SHARK (攻击鲨) ---
            AddProduct(VID_ATTACK_SHARK_1, 0x0001, "Attack Shark", "Attack Shark X3 Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_ATTACK_SHARK_1, 0x0002, "Attack Shark", "Attack Shark X3 Pro 4K", DeviceCategory.Mouse, 4000);
            AddProduct(VID_ATTACK_SHARK_1, 0x0003, "Attack Shark", "Attack Shark X6 Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_ATTACK_SHARK_2, 0x0001, "Attack Shark", "Attack Shark R1 Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_ATTACK_SHARK_2, 0x0002, "Attack Shark", "Attack Shark X11 有线/无线", DeviceCategory.Mouse, 1000);
            AddProduct(VID_MICLINK_OEM,    0x5001, "Attack Shark", "Attack Shark R2 8K", DeviceCategory.Mouse, 8000);

            // --- AJAZZ (黑爵) ---
            AddProduct(VID_AJAZZ_USB, 0x0001, "Ajazz", "Ajazz AJ199 Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_AJAZZ_USB, 0x0002, "Ajazz", "Ajazz AJ199 4K", DeviceCategory.Mouse, 4000);
            AddProduct(VID_AJAZZ_USB, 0x0003, "Ajazz", "Ajazz AJ139 Pro", DeviceCategory.Mouse, 1000);
            AddProduct(VID_AJAZZ_USB, 0x0004, "Ajazz", "Ajazz AJ139 Pro 4K", DeviceCategory.Mouse, 4000);
            AddProduct(VID_AJAZZ_USB, 0x0005, "Ajazz", "Ajazz AJ159 Apex 8K", DeviceCategory.Mouse, 8000);
            AddProduct(VID_AJAZZ_USB, 0x0006, "Ajazz", "Ajazz AJ179 Apex 8K", DeviceCategory.Mouse, 8000);

            // --- DARMOSHARK (摩豹达摩鲨) ---
            AddProduct(VID_VGN_COMPX, 0xFB24, "Darmoshark", "Darmoshark M3 Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_VGN_COMPX, 0xFB25, "Darmoshark", "Darmoshark M3s Wireless", DeviceCategory.Mouse, 2000);
            AddProduct(VID_VGN_COMPX, 0xFB26, "Darmoshark", "Darmoshark M3-4K", DeviceCategory.Mouse, 4000);
            AddProduct(VID_VGN_COMPX, 0xFB27, "Darmoshark", "Darmoshark M3 Pro", DeviceCategory.Mouse, 4000);
            AddProduct(VID_VGN_COMPX, 0xFB28, "Darmoshark", "Darmoshark N3 Wireless", DeviceCategory.Mouse, 1000);

            // --- GLORIOUS ---
            AddProduct(VID_MOTOSPEED,       0x2011, "Glorious", "Model O Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_MOTOSPEED,       0x2022, "Glorious", "Model O Wireless 接收器", DeviceCategory.Mouse, 1000);
            AddProduct(VID_MOTOSPEED,       0x2012, "Glorious", "Model D Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_MOTOSPEED,       0x2023, "Glorious", "Model D Wireless 接收器", DeviceCategory.Mouse, 1000);
            AddProduct(VID_MOTOSPEED,       0x2013, "Glorious", "Model O- Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_MOTOSPEED,       0x2024, "Glorious", "Model O- Wireless 接收器", DeviceCategory.Mouse, 1000);
            AddProduct(VID_MOTOSPEED,       0x2014, "Glorious", "Model D- Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_MOTOSPEED,       0x2025, "Glorious", "Model D- Wireless 接收器", DeviceCategory.Mouse, 1000);
            AddProduct(VID_MOTOSPEED,       0x2015, "Glorious", "Model O Pro", DeviceCategory.Mouse, 1000);
            AddProduct(VID_MOTOSPEED,       0x2018, "Glorious", "Series One Pro", DeviceCategory.Mouse, 1000);
            AddProduct(VID_GLORIOUS_PIXART, 0x822A, "Glorious", "Model O 2 Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_GLORIOUS_PIXART, 0x824A, "Glorious", "Model D 2 Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_GLORIOUS_PIXART, 0x821D, "Glorious", "Model I 2 Wireless 接收器", DeviceCategory.Mouse, 1000);

            // --- NINJUTSO ---
            AddProduct(VID_VGN_COMPX,       0xF520, "Ninjutso", "Ninjutso Sora Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_VGN_COMPX,       0xF522, "Ninjutso", "Ninjutso Sora 4K", DeviceCategory.Mouse, 4000);
            AddProduct(VID_VGN_COMPX,       0xF523, "Ninjutso", "Ninjutso Sora V2 8K", DeviceCategory.Mouse, 8000);
            AddProduct(VID_NINJUTSO_LEGACY, 0x0001, "Ninjutso", "Ninjutso Origin One X", DeviceCategory.Mouse, 1000);

            // --- RAWM ---
            AddProduct(VID_VGN_COMPX, 0xF530, "RAWM", "RAWM SA-MH01 4K", DeviceCategory.Mouse, 4000);
            AddProduct(VID_VGN_COMPX, 0xF531, "RAWM", "RAWM SA-SH01 4K", DeviceCategory.Mouse, 4000);

            // --- INCOTT ---
            AddProduct(VID_GLORIOUS_PIXART, 0x0001, "Incott", "Incott GHero 8K", DeviceCategory.Mouse, 8000);
            AddProduct(VID_GLORIOUS_PIXART, 0x0002, "Incott", "Incott HPC01M Pro", DeviceCategory.Mouse, 4000);

            // --- FINALMOUSE ---
            AddProduct(VID_FINALMOUSE, 0x0100, "Finalmouse", "UltralightX (ULX) Wireless", DeviceCategory.Mouse, 8000);
            AddProduct(VID_FINALMOUSE, 0x0101, "Finalmouse", "Starlight-12 Wireless", DeviceCategory.Mouse, 1000);

            // --- KEYCHRON ---
            AddProduct(VID_KEYCHRON, 0x0440, "Keychron", "Keychron Nape Pro (有线)", DeviceCategory.Mouse, 1000);
            AddProduct(VID_KEYCHRON, 0x0001, "Keychron", "Keychron M1 Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_KEYCHRON, 0x0002, "Keychron", "Keychron M3 Wireless", DeviceCategory.Mouse, 1000);
            AddProduct(VID_KEYCHRON, 0x0003, "Keychron", "Keychron M3 Mini 4K", DeviceCategory.Mouse, 4000);
            AddProduct(VID_KEYCHRON, 0x0004, "Keychron", "Keychron M6 Wireless", DeviceCategory.Mouse, 1000);

            // --- WOOTING ---
            AddProduct(VID_WOOTING, 0x0001, "Wooting", "Wooting 60HE 磁轴键盘", DeviceCategory.Keyboard, 1000);
            AddProduct(VID_WOOTING, 0x0002, "Wooting", "Wooting 80HE 8K 磁轴键盘", DeviceCategory.Keyboard, 8000);
            AddProduct(VID_WOOTING, 0x0003, "Wooting", "Wooting Two HE 磁轴键盘", DeviceCategory.Keyboard, 1000);

            // --- MCHOSE (迈从) ---
            AddProduct(VID_MCHOSE, 0x0001, "MCHOSE", "MCHOSE A5 Wireless", DeviceCategory.Mouse, 4000);
            AddProduct(VID_MCHOSE, 0x0002, "MCHOSE", "MCHOSE AX5 Wireless", DeviceCategory.Mouse, 8000);
            AddProduct(VID_MCHOSE, 0x0003, "MCHOSE", "MCHOSE G3 Wireless", DeviceCategory.Mouse, 8000);

            // --- DAREU (达尔优) ---
            AddProduct(VID_DAREU, 0x0001, "Dareu", "达尔优 A950 4K", DeviceCategory.Mouse, 4000);
            AddProduct(VID_DAREU, 0x0002, "Dareu", "达尔优 A980 Pro Max 8K", DeviceCategory.Mouse, 8000);

            // --- ATK / VXE ---
            AddProduct(VID_ATK, 0x1279, "ATK", "ATK A9 AIR 8K 接收器", DeviceCategory.Mouse, 8000);
            AddProduct(VID_ATK, 0x12E9, "ATK", "ATK X1 V2 8K 接收器", DeviceCategory.Mouse, 8000);

            // --- 玄派玄熊猫 (Metapanda) ---
            AddProduct(VID_VGN_COMPX, 0xF5A9, "玄熊猫", "玄熊猫 P1 游戏鼠标", DeviceCategory.Mouse, 1000);
            AddProduct(VID_VGN_COMPX, 0xF5AB, "玄熊猫", "玄熊猫 P1 游戏鼠标 (有线模式)", DeviceCategory.Mouse, 1000);
            AddProduct(VID_VGN_COMPX, 0xF5AC, "玄熊猫", "玄熊猫 P1 4K 游戏鼠标", DeviceCategory.Mouse, 4000);

            // --- 怒喵 (Angry Miao) ---
            AddProduct(VID_ANGRY_MIAO_1, 0x0703, "Angry Miao", "怒喵 AM Infinity .97 (2.4G)", DeviceCategory.Mouse, 8000);
            AddProduct(VID_ANGRY_MIAO_1, 0x0880, "Angry Miao", "怒喵 AM Infinity .97 (有线模式)", DeviceCategory.Mouse, 8000);
            AddProduct(VID_ANGRY_MIAO_2, 0x0035, "Angry Miao", "怒喵 AM Infinity .97 (有线模式)", DeviceCategory.Mouse, 8000);

            // --- DELUX (多彩) ---
            AddProduct(0x258A, 0x1001, "Delux", "多彩 M800 Pro", DeviceCategory.Mouse, 1000);
            AddProduct(0x258A, 0x1002, "Delux", "多彩 M800 Ultra 4K", DeviceCategory.Mouse, 4000);

            // --- WALLHACK ---
            AddProduct(0x3554, 0x1122, "WALLHACK", "WALLHACK SP-004 Esports Mouse", DeviceCategory.Mouse, 4000);

            // --- G-WOLVES ---
            AddProduct(0x3554, 0x2233, "G-Wolves", "G-Wolves HTS Plus 4K", DeviceCategory.Mouse, 4000);

            // --- FINALMOUSE ---
            AddProduct(0x3554, 0x3344, "Finalmouse", "Finalmouse UltralightX", DeviceCategory.Mouse, 8000);

            // --- ZAUNKOENIG ---
            AddProduct(0x3554, 0x4455, "Zaunkoenig", "Zaunkoenig M2K 8K", DeviceCategory.Mouse, 8000);
        }

        public static ProductEntry LookupProduct(int vid, int pid, string productString)
        {
            string key = string.Format("{0:X4}:{1:X4}", vid, pid);
            ProductEntry entry;
            if (catalog.TryGetValue(key, out entry))
            {
                return entry;
            }

            // 依据厂商 VID 推断品牌
            string brand = "通用电竞外设";
            DeviceCategory cat = DeviceCategory.Mouse;
            int poll = 1000;

            switch (vid)
            {
                case VID_LAMZU_OFFICIAL:
                case VID_COMPX_LAMZU:
                    brand = "LAMZU";
                    poll = 4000;
                    break;
                case VID_PULSAR:
                    brand = "Pulsar";
                    poll = 4000;
                    break;
                case VID_WLMOUSE:
                    brand = "WLmouse";
                    poll = 8000;
                    break;
                case VID_ENDGAME_GEAR:
                    brand = "Endgame Gear";
                    break;
                case VID_ATK:
                    brand = "ATK";
                    poll = 8000;
                    break;
                case VID_ANGRY_MIAO_1:
                case VID_ANGRY_MIAO_2:
                    brand = "Angry Miao";
                    poll = 8000;
                    break;
                case VID_VGN_COMPX:
                    brand = "VGN / 达摩鲨";
                    poll = 4000;
                    break;
                case VID_ATTACK_SHARK_1:
                case VID_ATTACK_SHARK_2:
                    brand = "Attack Shark";
                    break;
                case VID_AJAZZ_USB:
                    brand = "Ajazz";
                    break;
                case VID_FINALMOUSE:
                    brand = "Finalmouse";
                    poll = 4000;
                    break;
                case VID_WOOTING:
                    brand = "Wooting";
                    cat = DeviceCategory.Keyboard;
                    poll = 8000;
                    break;
                case VID_KEYCHRON:
                    brand = "Keychron";
                    break;
                case VID_DAREU:
                    brand = "Dareu";
                    poll = 4000;
                    break;
                case VID_ASUS:
                    brand = "ASUS ROG";
                    break;
                case VID_STEELSERIES:
                    brand = "SteelSeries";
                    break;
                case VID_CORSAIR:
                    brand = "Corsair";
                    break;
            }

            string model = !string.IsNullOrEmpty(productString) ? productString : string.Format("{0} 无线设备 ({1:X4})", brand, pid);
            return new ProductEntry(vid, pid, brand, model, cat, poll);
        }

        #endregion

        #region 协议引擎一：CompX / PixArt 64字节 Feature 引擎 (Lamzu, Pulsar, WLmouse 等)

        public static bool QueryCompxBattery(IntPtr handle, out int percent, out bool isCharging)
        {
            percent = -1;
            isCharging = false;
            if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;

            try
            {
                // 下发 Feature Report 0x83 查询命令 (前导 Report ID 0x00 + 64 字节)
                byte[] tx = new byte[65];
                tx[0] = 0x00;
                tx[1] = 0x00;
                tx[2] = 0x00;
                tx[3] = 0x02;
                tx[4] = 0x02;
                tx[5] = 0x00;
                tx[6] = 0x83;

                bool setOk = RazerDeviceHelper.HidD_SetFeature(handle, tx, tx.Length);
                if (!setOk)
                {
                    // 尝试无前导 64 字节
                    byte[] tx64 = new byte[64];
                    Array.Copy(tx, 1, tx64, 0, 64);
                    setOk = RazerDeviceHelper.HidD_SetFeature(handle, tx64, tx64.Length);
                }

                if (!setOk) return false;

                // 轮询 GetFeature 直至取得 A1 00 02 02 00 83 帧 (跳过 A0 等待帧)
                for (int attempt = 0; attempt < 12; attempt++)
                {
                    Thread.Sleep(8);
                    byte[] rx = new byte[65];
                    rx[0] = 0x00;
                    if (RazerDeviceHelper.HidD_GetFeature(handle, rx, rx.Length))
                    {
                        byte[] data = rx;
                        if (rx.Length >= 2 && rx[0] == 0x00)
                        {
                            data = new byte[rx.Length - 1];
                            Array.Copy(rx, 1, data, 0, data.Length);
                        }

                        // 跳过 A0 确认包
                        if (data.Length > 0 && data[0] == 0xA0) continue;

                        // 查找特征帧: A1 00 02 02 00 83 [chg] [pct]
                        for (int i = 0; i + 8 <= data.Length; i++)
                        {
                            if (data[i] == 0xA1 && data[i + 1] == 0x00 &&
                                data[i + 2] == 0x02 && data[i + 3] == 0x02 &&
                                data[i + 4] == 0x00 && data[i + 5] == 0x83)
                            {
                                int chg = data[i + 6];
                                int pct = data[i + 7];
                                if ((chg == 0 || chg == 1) && pct > 0 && pct <= 100)
                                {
                                    percent = pct;
                                    isCharging = (chg == 1);
                                    return true;
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        #endregion

        #region 协议引擎二：Beken / ATK / Endgame Gear 16字节 EEPROM 引擎

        public static byte CalculateWeChecksum(byte reportId, byte[] data15)
        {
            int sum = reportId;
            for (int i = 0; i < 15 && i < data15.Length; i++)
            {
                sum += (data15[i] & 0xFF);
            }
            return (byte)((0x55 - (sum & 0xFF)) & 0xFF);
        }

        public static bool ParseBekenWeBatteryResponse(byte[] rx, int length, out int percent, out bool isCharging, out int millivolts)
        {
            percent = -1;
            isCharging = false;
            millivolts = 0;
            if (rx == null || length < 10 || rx.Length < length) return false;

            int offset = (rx[0] == 0x08) ? 1 : 0;
            if (offset + 8 >= length) return false;
            if (rx[offset] != 0x04) return false;

            int len = rx[offset + 4];
            if (len >= 1)
            {
                percent = Math.Min((int)rx[offset + 5], 100);
            }
            if (len >= 2)
            {
                isCharging = (rx[offset + 6] != 0);
            }
            if (len >= 4)
            {
                millivolts = (rx[offset + 7] << 8) | rx[offset + 8];
            }
            return (percent > 0 && percent <= 100);
        }

        public static bool QueryBekenWeBattery(IntPtr handle, out int percent, out bool isCharging, out int millivolts)
        {
            percent = -1;
            isCharging = false;
            millivolts = 0;
            if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;

            try
            {
                // Report ID 0x08, 16 字节
                byte[] tx = new byte[17];
                tx[0] = 0x08; // Report ID
                tx[1] = 0x04; // CMD: BATTERY_COMMAND (0x04)

                byte[] data15 = new byte[15];
                Array.Copy(tx, 1, data15, 0, 15);
                tx[16] = CalculateWeChecksum(0x08, data15);

                bool setOk = RazerDeviceHelper.HidD_SetFeature(handle, tx, tx.Length);
                if (!setOk)
                {
                    // 尝试无前导 16 字节
                    byte[] tx16 = new byte[16];
                    Array.Copy(tx, 1, tx16, 0, 16);
                    setOk = RazerDeviceHelper.HidD_SetFeature(handle, tx16, tx16.Length);
                }

                if (!setOk) return false;

                Thread.Sleep(10);
                byte[] rx = new byte[17];
                rx[0] = 0x08;
                if (RazerDeviceHelper.HidD_GetFeature(handle, rx, rx.Length))
                {
                    return ParseBekenWeBatteryResponse(rx, rx.Length, out percent, out isCharging, out millivolts);
                }
            }
            catch { }

            return false;
        }

        /// <summary>
        /// 针对 ATK F1 Ultimate 2.0 (373B:11D9, 8k dongle-L) 等仅暴露 UsagePage 0xFF02 / Usage 0x0002
        /// 17 字节 Interrupt Out/In 端点（无 Feature 报告）的 Beken 方案接收器查询电量。
        /// 重要：严禁在 WriteFile 前执行 DrainReports，否则会破坏接收器与鼠标的无线握手状态。
        /// 鸣谢社区贡献者：“好的很”。
        /// </summary>
        public static bool QueryAtkBekenOutputBattery(string devicePath, out int percent, out bool isCharging, out int millivolts)
        {
            percent = -1;
            isCharging = false;
            millivolts = 0;
            if (string.IsNullOrEmpty(devicePath)) return false;

            IntPtr handle = CreateFileW(
                devicePath,
                GENERIC_READ | GENERIC_WRITE,
                RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                IntPtr.Zero,
                RazerDeviceHelper.OPEN_EXISTING,
                FILE_FLAG_OVERLAPPED,
                IntPtr.Zero);
            if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;

            IntPtr hEvent = CreateEvent(IntPtr.Zero, true, false, null);
            if (hEvent == IntPtr.Zero)
            {
                RazerDeviceHelper.CloseHandle(handle);
                return false;
            }

            try
            {
                byte[] tx = new byte[17];
                tx[0] = 0x08;
                tx[1] = 0x04;

                byte[] data15 = new byte[15];
                Array.Copy(tx, 1, data15, 0, 15);
                tx[16] = CalculateWeChecksum(0x08, data15);

                OVERLAPPED wOl = new OVERLAPPED();
                wOl.hEvent = hEvent;
                ResetEvent(hEvent);
                uint written;
                bool wOk = WriteFile(handle, tx, (uint)tx.Length, out written, ref wOl);
                if (!wOk)
                {
                    int wErr = Marshal.GetLastWin32Error();
                    if (wErr != ERROR_IO_PENDING) return false;
                    if (WaitForSingleObject(hEvent, 500) != WAIT_OBJECT_0)
                    {
                        CancelIo(handle);
                        return false;
                    }
                    if (!GetOverlappedResult(handle, ref wOl, out written, false)) return false;
                }

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    byte[] rx = new byte[17];
                    OVERLAPPED rOl = new OVERLAPPED();
                    rOl.hEvent = hEvent;
                    ResetEvent(hEvent);
                    uint readBytes = 0;
                    bool rOk = ReadFile(handle, rx, (uint)rx.Length, out readBytes, ref rOl);
                    if (!rOk)
                    {
                        int rErr = Marshal.GetLastWin32Error();
                        if (rErr != ERROR_IO_PENDING) continue;
                        if (WaitForSingleObject(hEvent, 350) != WAIT_OBJECT_0)
                        {
                            CancelIo(handle);
                            continue;
                        }
                        if (!GetOverlappedResult(handle, ref rOl, out readBytes, false)) continue;
                    }

                    if (ParseBekenWeBatteryResponse(rx, (int)readBytes, out percent, out isCharging, out millivolts))
                    {
                        return true;
                    }
                }
            }
            catch { }
            finally
            {
                RazerDeviceHelper.CloseHandle(hEvent);
                RazerDeviceHelper.CloseHandle(handle);
            }

            return false;
        }

        #endregion

        #region 协议引擎三：Vaxee 64字节专用控制引擎

        public static bool QueryVaxeeBattery(IntPtr handle, out int percent, out bool isCharging)
        {
            percent = -1;
            isCharging = false;
            if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;

            try
            {
                // 1. 查询 Page 0x0B (电量)
                byte[] tx0b = new byte[64];
                tx0b[0] = 0x0E;
                tx0b[1] = 0xA5;
                tx0b[2] = 0x0B;
                tx0b[3] = 0x01;
                tx0b[4] = 0x01;
                tx0b[5] = 0x00;

                if (RazerDeviceHelper.HidD_SetFeature(handle, tx0b, tx0b.Length))
                {
                    Thread.Sleep(10);
                    byte[] rx0b = new byte[64];
                    rx0b[0] = 0x0E;
                    if (RazerDeviceHelper.HidD_GetFeature(handle, rx0b, rx0b.Length))
                    {
                        if (rx0b[0] == 0x0E && rx0b[1] == 0xA5 && rx0b[2] == 0x0B)
                        {
                            int raw = (int)rx0b[5];
                            int pct = raw * 5; // 0..20 段值按 5% 离散计算
                            if (pct >= 0 && pct <= 100)
                            {
                                percent = pct;
                            }
                        }
                    }
                }

                // 2. 查询 Page 0x10 (充电状态)
                byte[] tx10 = new byte[64];
                tx10[0] = 0x0E;
                tx10[1] = 0xA5;
                tx10[2] = 0x10;
                tx10[3] = 0x01;
                tx10[4] = 0x01;
                tx10[5] = 0x00;

                if (RazerDeviceHelper.HidD_SetFeature(handle, tx10, tx10.Length))
                {
                    Thread.Sleep(10);
                    byte[] rx10 = new byte[64];
                    rx10[0] = 0x0E;
                    if (RazerDeviceHelper.HidD_GetFeature(handle, rx10, rx10.Length))
                    {
                        if (rx10[0] == 0x0E && rx10[1] == 0xA5 && rx10[2] == 0x10)
                        {
                            isCharging = (rx10[5] == 0x01);
                        }
                    }
                }

                return (percent >= 0);
            }
            catch { }

            return false;
        }

        #endregion

        #region 协议引擎四：怒喵 (Angry Miao) 62字节 RACE Feature 引擎

        public static bool QueryAngryMiaoBattery(IntPtr handle, int pid, out int percent, out bool isCharging)
        {
            percent = -1;
            isCharging = false;
            if (handle == IntPtr.Zero || handle.ToInt64() == -1) return false;

            try
            {
                byte route = (pid == 0x0703) ? (byte)0x80 : (byte)0x00;
                byte[] tx = new byte[62];
                tx[0] = 0x14; // Report ID
                tx[1] = 0x0A; // Payload size
                tx[2] = route;
                tx[3] = (byte)'R';
                tx[4] = (byte)'A';
                tx[5] = (byte)'C';
                tx[6] = (byte)'E';
                tx[7] = 0x05;
                tx[8] = 0x5A;
                tx[9] = 0x02; // len = 2
                tx[10] = 0x00;
                tx[11] = 0xCF; // cmd = 0x30CF (Battery query)
                tx[12] = 0x30;

                bool setOk = RazerDeviceHelper.HidD_SetFeature(handle, tx, tx.Length);
                if (!setOk)
                {
                    byte[] tx63 = new byte[63];
                    Array.Copy(tx, 0, tx63, 1, 62);
                    setOk = RazerDeviceHelper.HidD_SetFeature(handle, tx63, tx63.Length);
                }

                if (!setOk) return false;

                for (int i = 0; i < 8; i++)
                {
                    Thread.Sleep(10);
                    byte[] rx = new byte[62];
                    rx[0] = 0x14;
                    if (RazerDeviceHelper.HidD_GetFeature(handle, rx, rx.Length))
                    {
                        int offset = 0;
                        if (rx.Length >= 63 && rx[0] == 0x00 && rx[1] == 0x14) offset = 1;

                        if (rx[offset] == 0x14 && rx[offset + 3] == (byte)'R' && rx[offset + 4] == (byte)'A' && rx[offset + 5] == (byte)'C' && rx[offset + 6] == (byte)'E')
                        {
                            for (int j = offset + 7; j + 6 < rx.Length; j++)
                            {
                                if (rx[j] == 0x05 && (rx[j + 1] == 0x5B || rx[j + 1] == 0x5D))
                                {
                                    int cmd = rx[j + 4] | (rx[j + 5] << 8);
                                    if (cmd == 0x30CF && j + 7 < rx.Length)
                                    {
                                        int pct = rx[j + 6];
                                        int chg = (j + 7 < rx.Length) ? rx[j + 7] : 0;
                                        if (pct > 0 && pct <= 100)
                                        {
                                            percent = pct;
                                            isCharging = (chg != 0);
                                            return true;
                                        }
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch { }

            return false;
        }

        #endregion

        #region 全设备综合枚举与发现

        public static bool IsSupportedGamingDevice(int vid, string path, string prod)
        {
            // ASUS PELTA, wired Scope II RX, and ROG OMNI Receiver have dedicated classification. Never send mouse probes.
            if (vid == AsusDeviceHelper.VendorId && !string.IsNullOrEmpty(path))
            {
                string asusPath = path.ToLowerInvariant();
                if (asusPath.Contains("pid_1b84") || asusPath.Contains("pid_1ab5") || asusPath.Contains("pid_1ace")) return false;
            }
            // 258A:01BB 的 Z75/Z75S 为键盘接收器；由 MchoseDeviceHelper 握手确认型号。
            if (vid == MchoseDeviceHelper.VendorIdZ75s && !string.IsNullOrEmpty(path) &&
                path.IndexOf("pid_01bb", StringComparison.OrdinalIgnoreCase) >= 0) return false;

            // MCHOSE 全系键鼠 (含 G87 V2 pid_3033 与 G75 PRO pid_fa09) 与 SteelSeries 耳机由专属独立驱动接管，禁止通用引擎越界拦截
            if (vid == VID_MCHOSE || vid == VID_STEELSERIES)
            {
                return false;
            }
            if (!string.IsNullOrEmpty(path) && (path.IndexOf("pid_3033", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("pid_fa09", StringComparison.OrdinalIgnoreCase) >= 0))
            {
                return false;
            }

            switch (vid)
            {
                case VID_LAMZU_OFFICIAL:
                case VID_COMPX_LAMZU:
                case VID_PULSAR:
                case VID_WLMOUSE:
                case VID_ENDGAME_GEAR:
                case VID_ATK:
                case VID_ANGRY_MIAO_1:
                case VID_ANGRY_MIAO_2:
                case VID_VGN_COMPX:
                case VID_ATTACK_SHARK_1:
                case VID_ATTACK_SHARK_2:
                case VID_MICLINK_OEM:
                case VID_AJAZZ_USB:
                case VID_MOTOSPEED:
                case VID_GLORIOUS_PIXART:
                case VID_GLORIOUS_I:
                case VID_GLORIOUS_WIRED:
                case VID_GLORIOUS_O3:
                case VID_NINJUTSO_LEGACY:
                case VID_FINALMOUSE:
                case VID_KEYCHRON:
                case VID_WOOTING:
                case VID_DAREU:
                case VID_ASUS:
                case VID_CORSAIR:
                case VID_GWOLVES:
                case VID_ZAUNKOENIG:
                case VID_FATER:
                case VID_MICROSOFT:
                    return true;
            }

            if (!string.IsNullOrEmpty(path))
            {
                string lower = path.ToLowerInvariant();
                if (lower.Contains("lamzu") || lower.Contains("pulsar") || lower.Contains("wlmouse") ||
                    lower.Contains("atk") || lower.Contains("vxe") || lower.Contains("vgn") ||
                    lower.Contains("darmoshark") || lower.Contains("attackshark") || lower.Contains("ajazz") ||
                    lower.Contains("glorious") || lower.Contains("ninjutso") || lower.Contains("vaxee"))
                {
                    return true;
                }
            }

            if (!string.IsNullOrEmpty(prod))
            {
                string pLower = prod.ToLowerInvariant();
                if (pLower.Contains("lamzu") || pLower.Contains("pulsar") || pLower.Contains("wlmouse") ||
                    pLower.Contains("vxe") || pLower.Contains("atk") || pLower.Contains("beast") ||
                    pLower.Contains("atlantis") || pLower.Contains("thorn") || pLower.Contains("maya") ||
                    pLower.Contains("darmoshark") || pLower.Contains("glorious") || pLower.Contains("sora") ||
                    pLower.Contains("vaxee"))
                {
                    return true;
                }
            }

            return false;
        }

        public static List<MouseBatteryInfo> QueryAllGamingDevices()
        {
            List<MouseBatteryInfo> list = new List<MouseBatteryInfo>();

            lock (hidLock)
            {
                Guid hidGuid;
                RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
                IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
                if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return list;

                RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
                uint memberIdx = 0;
                HashSet<string> seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

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
                            if (devicePath != null)
                            {
                                // 过滤键盘按键集合，保留控制/Vendor集合 (mi_02 或非普通 kbd)
                                if (devicePath.EndsWith("\\kbd", StringComparison.OrdinalIgnoreCase))
                                {
                                    Marshal.FreeHGlobal(detailBuffer);
                                    continue;
                                }

                                IntPtr handle = RazerDeviceHelper.CreateFile(
                                    devicePath,
                                    GENERIC_READ | GENERIC_WRITE,
                                    RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                                    IntPtr.Zero,
                                    RazerDeviceHelper.OPEN_EXISTING,
                                    0,
                                    IntPtr.Zero);

                                if (handle == IntPtr.Zero || handle.ToInt64() == -1)
                                {
                                    handle = RazerDeviceHelper.CreateFile(
                                        devicePath,
                                        GENERIC_WRITE,
                                        RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                                        IntPtr.Zero,
                                        RazerDeviceHelper.OPEN_EXISTING,
                                        0,
                                        IntPtr.Zero);
                                }

                                if (handle == IntPtr.Zero || handle.ToInt64() == -1)
                                {
                                    handle = RazerDeviceHelper.CreateFile(
                                        devicePath,
                                        0,
                                        RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                                        IntPtr.Zero,
                                        RazerDeviceHelper.OPEN_EXISTING,
                                        0,
                                        IntPtr.Zero);
                                }

                                if (handle != IntPtr.Zero && handle.ToInt64() != -1)
                                {
                                    try
                                    {
                                        RazerDeviceHelper.HIDD_ATTRIBUTES attr = new RazerDeviceHelper.HIDD_ATTRIBUTES();
                                        attr.Size = Marshal.SizeOf(attr);
                                        RazerDeviceHelper.HidD_GetAttributes(handle, ref attr);

                                        StringBuilder sbProd = new StringBuilder(256);
                                        string prodStr = RazerDeviceHelper.HidD_GetProductString(handle, sbProd, sbProd.Capacity) ? sbProd.ToString().Trim() : "";

                                        if (IsSupportedGamingDevice(attr.VendorID, devicePath, prodStr))
                                        {
                                            string deviceKey = string.Format("{0:X4}:{1:X4}", attr.VendorID, attr.ProductID);
                                            if (!seenKeys.Contains(deviceKey))
                                            {
                                                ProductEntry product = LookupProduct(attr.VendorID, attr.ProductID, prodStr);

                                                int batt = -1;
                                                bool isChg = false;
                                                int mv = 0;
                                                bool ok = false;

                                                // 按协议族白名单精准分发探测，严禁向同一设备盲扫 4 套异构 Feature Report
                                                if (attr.VendorID == VID_ANGRY_MIAO_1 || attr.VendorID == VID_ANGRY_MIAO_2)
                                                {
                                                    ok = QueryAngryMiaoBattery(handle, attr.ProductID, out batt, out isChg);
                                                }
                                                else if (attr.VendorID == 0x3554 && attr.ProductID >= 0x1010 && attr.ProductID <= 0x101F)
                                                {
                                                    ok = QueryVaxeeBattery(handle, out batt, out isChg);
                                                }
                                                else if (attr.VendorID == VID_ATK || attr.VendorID == VID_ENDGAME_GEAR)
                                                {
                                                    ok = QueryBekenWeBattery(handle, out batt, out isChg, out mv);
                                                }
                                                else
                                                {
                                                    ok = QueryCompxBattery(handle, out batt, out isChg);
                                                }

                                                if (ok || batt > 0)
                                                {
                                                    seenKeys.Add(deviceKey);

                                                    MouseBatteryInfo info = new MouseBatteryInfo();
                                                    info.DeviceId = string.Format("Gaming:{0:X4}:{1:X4}", attr.VendorID, attr.ProductID);
                                                    info.HardwareFingerprint = string.Format("HID:{0:X4}:{1:X4}", attr.VendorID, attr.ProductID);
                                                    info.DriverId = "BuiltIn:UniversalGaming";
                                                    info.DeviceName = product.ModelName;
                                                    info.Brand = product.Brand;
                                                    info.Category = product.Category;
                                                    info.Transport = "2.4G 无线 / 极速回报";
                                                    info.IsDonglePresent = true;
                                                    info.IsConnected = true;
                                                    info.IsSleeping = false;
                                                    info.IsCharging = isChg;
                                                    info.BatteryPercent = batt;
                                                    info.MaxPollingRate = product.MaxPollingRate;
                                                    info.SupportedPollingRates = GetSupportedPollingRatesByMax(product.MaxPollingRate);
                                                    info.Capabilities.SupportsBattery = true;
                                                    info.Capabilities.SupportsDpi = false;
                                                    info.Capabilities.SupportsDpiStages = false;
                                                    info.Capabilities.SupportsPollingRate = false;

                                                    list.Add(info);
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

                QueryAtkOutputEndpointDevices(list, seenKeys);
            }

            return list;
        }

        private static List<string> EnumerateHidInterfacePaths()
        {
            List<string> paths = new List<string>();
            Guid hidGuid;
            RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
            IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, 0x12);
            if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) return paths;

            RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
            uint memberIdx = 0;
            try
            {
                ifData.cbSize = Marshal.SizeOf(ifData);
                while (RazerDeviceHelper.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                {
                    uint reqSize;
                    SetupDiGetDeviceInterfaceDetailW(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                    if (reqSize == 0) continue;

                    IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                    Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 6);
                    try
                    {
                        if (SetupDiGetDeviceInterfaceDetailW(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero))
                        {
                            IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                            string devicePath = Marshal.PtrToStringUni(pDevicePath);
                            if (!string.IsNullOrEmpty(devicePath))
                            {
                                paths.Add(devicePath);
                            }
                        }
                    }
                    finally
                    {
                        Marshal.FreeHGlobal(detailBuffer);
                    }
                }
            }
            finally
            {
                RazerDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);
            }

            return paths;
        }

        private static void QueryAtkOutputEndpointDevices(List<MouseBatteryInfo> list, HashSet<string> seenKeys)
        {
            List<string> paths = EnumerateHidInterfacePaths();
            for (int i = 0; i < paths.Count; i++)
            {
                string devicePath = paths[i];
                if (string.IsNullOrEmpty(devicePath) || devicePath.EndsWith("\\kbd", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                IntPtr handle = CreateFileW(
                    devicePath,
                    0,
                    RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE,
                    IntPtr.Zero,
                    RazerDeviceHelper.OPEN_EXISTING,
                    0,
                    IntPtr.Zero);
                if (handle == IntPtr.Zero || handle.ToInt64() == -1) continue;

                int vid = 0;
                int pid = 0;
                string prodStr = "";
                ushort usagePage = 0;
                ushort usage = 0;
                ushort inLen = 0;
                ushort outLen = 0;
                ushort featLen = 0;
                try
                {
                    RazerDeviceHelper.HIDD_ATTRIBUTES attr = new RazerDeviceHelper.HIDD_ATTRIBUTES();
                    attr.Size = Marshal.SizeOf(attr);
                    if (!RazerDeviceHelper.HidD_GetAttributes(handle, ref attr)) continue;
                    vid = attr.VendorID;
                    pid = attr.ProductID;

                    IntPtr preparsed;
                    if (!RazerDeviceHelper.HidD_GetPreparsedData(handle, out preparsed)) continue;
                    try
                    {
                        RazerDeviceHelper.HIDP_CAPS caps;
                        RazerDeviceHelper.HidP_GetCaps(preparsed, out caps);
                        usagePage = (ushort)caps.UsagePage;
                        usage = (ushort)caps.Usage;
                        inLen = (ushort)caps.InputReportByteLength;
                        outLen = (ushort)caps.OutputReportByteLength;
                        featLen = (ushort)caps.FeatureReportByteLength;
                    }
                    finally
                    {
                        RazerDeviceHelper.HidD_FreePreparsedData(preparsed);
                    }

                    StringBuilder sbProd = new StringBuilder(256);
                    if (RazerDeviceHelper.HidD_GetProductString(handle, sbProd, sbProd.Capacity))
                    {
                        prodStr = sbProd.ToString().Trim();
                    }
                }
                finally
                {
                    RazerDeviceHelper.CloseHandle(handle);
                }

                if (vid != VID_ATK || usagePage != 0xFF02 || usage != 0x0002 || inLen != 17 || outLen != 17 || featLen != 0)
                {
                    continue;
                }

                string deviceKey = string.Format("{0:X4}:{1:X4}", vid, pid);
                if (seenKeys.Contains(deviceKey)) continue;

                ProductEntry product = LookupProduct(vid, pid, prodStr);
                int batt;
                bool isChg;
                int mv;
                bool ok = QueryAtkBekenOutputBattery(devicePath, out batt, out isChg, out mv);
                seenKeys.Add(deviceKey);

                int cachedBatt;
                bool cachedChg;
                string cachedName;
                DateTime cachedUpdated;
                bool hasCache = DriverStateStore.TryLoadBatteryState("Gaming:" + deviceKey, out cachedBatt, out cachedChg, out cachedName, out cachedUpdated);

                if (ok && batt > 0 && batt <= 100)
                {
                    DriverStateStore.SaveBatteryState("Gaming:" + deviceKey, batt, isChg, product.ModelName);
                }

                MouseBatteryInfo info = new MouseBatteryInfo();
                info.DeviceId = string.Format("Gaming:{0:X4}:{1:X4}", vid, pid);
                info.HardwareFingerprint = string.Format("HID:{0:X4}:{1:X4}", vid, pid);
                info.DriverId = "BuiltIn:UniversalGaming";
                info.DeviceName = product.ModelName;
                info.Brand = product.Brand;
                info.Category = product.Category;
                info.Transport = "2.4G 无线 / 极速回报";
                info.IsDonglePresent = true;
                info.IsConnected = ok;
                info.IsSleeping = !ok;
                info.IsCharging = ok ? isChg : (hasCache ? cachedChg : false);
                info.BatteryPercent = ok ? batt : (hasCache ? cachedBatt : -1);
                info.ExactBatteryPercent = info.BatteryPercent >= 0 ? info.BatteryPercent : 0;
                info.LastUpdated = ok ? DateTime.Now : (hasCache ? cachedUpdated : DateTime.Now);
                info.MaxPollingRate = product.MaxPollingRate;
                info.SupportedPollingRates = GetSupportedPollingRatesByMax(product.MaxPollingRate);
                info.Capabilities.SupportsBattery = true;
                info.Capabilities.SupportsDpi = false;
                info.Capabilities.SupportsDpiStages = false;
                info.Capabilities.SupportsPollingRate = false;

                list.Add(info);
            }
        }

        #endregion
    }
}
