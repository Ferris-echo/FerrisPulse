// ============================================================================
// 模块名称: RazerButtonMapping.cs
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   Razer Viper V3 HyperSpeed (VID 1532 / PID 00B8) 板载按键映射命令层：
//   1. 按键绑定协议事务 (0x02/0x8C 读, 0x02/0x0C 写; tid=0x1F, dataSize=10)
//      与重试策略 (BUSY/HID 失败重试, 0x03 读语境确定性拒绝不重试)
//   2. 设备发现与写路径白名单 (vid_1532 + FRBL>=90; 写路径加严 pid_00b8 或
//      产品串匹配, 多候选拒绝写入)
//   3. 批量绑定写入: 完整 pre-apply 快照 → 逐键写+回读语义校验 → 失败自动
//      回滚; 宏/未知功能类默认保护跳过 (需显式 force 才覆盖)
//   4. 出厂默认表 (0x05 为推断项) 与快照备份持久化
//      (HKCU\Software\FerrisPulse\ButtonMappingBackup)
//   线程契约: 入口仅供后台线程调用, 内部持有 RazerDeviceHelper.hidLock
//   (与电量轮询/DPI 写互斥; 批量最坏含回滚可达数十秒, 见 issue #7 讨论)。
// 依赖关系: RazerDeviceHelper.cs (hidLock 互斥锁, CreateRazerReport 报文
//   构造, HID P/Invoke 声明)
//
// 协议事实来自真机实测 (Viper V3 HyperSpeed 全键空间扫描与两层写闭环,
// 详见 FerrisPulse issue #7 附件报告); 数据模型移植自 geezmolycos/razerqdhid
// (MIT, https://github.com/geezmolycos/razerqdhid) 逆向文档并以实测修正。
// 代码注释中的 §x.y 编号指贡献者 fork 设计文档中的验证项。
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、报文结构或新增 PID 时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace RazerBatteryTray
{
    public static class RazerButtonMapping
    {
        /// <summary>Hardware button ids answered by this mouse (M0a full-range scan: exactly these 8).</summary>
        public enum ButtonId : byte
        {
            Left = 0x01,
            Right = 0x02,
            Middle = 0x03,
            Backward = 0x04,
            Forward = 0x05,
            WheelUp = 0x09,
            WheelDown = 0x0A,
            DpiButton = 0x60,          // the single button below the scroll wheel
        }

        /// <summary>"Mouse function" target values — value domain differs from ButtonId on the wheel-left/right pair.</summary>
        public enum FnMouse : byte
        {
            Left = 0x01,
            Right = 0x02,
            Middle = 0x03,
            Backward = 0x04,
            Forward = 0x05,
            WheelUp = 0x09,
            WheelDown = 0x0A,
            // Kept for protocol completeness; this mouse has no tilt wheel so the UI
            // never offers them (M0a-report §8-5, deferred to phase 2).
            WheelLeft = 0x68,
            WheelRight = 0x69,
        }

        [Flags]
        public enum FnKeyboardModifier : byte
        {
            None = 0x00,
            LeftCtrl = 0x01, LeftShift = 0x02, LeftAlt = 0x04, LeftWin = 0x08,
            RightCtrl = 0x10, RightShift = 0x20, RightAlt = 0x40, RightWin = 0x80,
        }

        /// <summary>Function category byte of the 7-byte ButtonFunction structure.</summary>
        public enum FnClass : byte
        {
            Disabled = 0x00, Mouse = 0x01, Keyboard = 0x02,
            MacroFixed = 0x03, MacroHold = 0x04, MacroToggle = 0x05,
            DpiSwitch = 0x06, ProfileSwitch = 0x07,
            System = 0x09, Consumer = 0x0A, DoubleClick = 0x0B,
            HypershiftToggle = 0x0C, KeyboardTurbo = 0x0D,
            MouseTurbo = 0x0E, MacroSequence = 0x0F, ScrollModeToggle = 0x12,
        }

        public enum FnDpiSwitchMode : byte
        {
            Next = 0x01, Prev = 0x02, Fixed = 0x03, Aim = 0x05, NextLoop = 0x06, PrevLoop = 0x07,
        }

        /// <summary>7-byte function structure [class][valueLength][value(5, zero padded)] of command 0x020C/0x028C.</summary>
        public sealed class ButtonFunction
        {
            public FnClass Class = FnClass.Disabled;
            public byte ValueLength;                       // 0..5
            public readonly byte[] Value = new byte[5];

            public byte[] ToBytes7()
            {
                byte[] b = new byte[7];
                b[0] = (byte)Class;
                b[1] = ValueLength;
                Array.Copy(Value, 0, b, 2, 5);
                return b;
            }

            public static ButtonFunction FromBytes7(byte[] src, int offset)
            {
                ButtonFunction fn = new ButtonFunction();
                fn.Class = (FnClass)src[offset];
                fn.ValueLength = Math.Min(src[offset + 1], (byte)5);
                for (int i = 0; i < fn.ValueLength; i++) fn.Value[i] = src[offset + 2 + i];
                return fn;
            }

            /// <summary>Semantic prefix equality (design §4.4): class + length + value[0..len-1]; tail padding zeros ignored.</summary>
            public bool SemanticEquals(ButtonFunction other)
            {
                if (other == null || Class != other.Class || ValueLength != other.ValueLength) return false;
                for (int i = 0; i < ValueLength; i++)
                    if (Value[i] != other.Value[i]) return false;
                return true;
            }

            // ---- factories for the phase-1 supported function families ----

            public static ButtonFunction MouseTarget(FnMouse target)
            {
                return Build(FnClass.Mouse, new byte[] { (byte)target });
            }

            public static ButtonFunction DoubleClick(FnMouse target)
            {
                return Build(FnClass.DoubleClick, new byte[] { (byte)target });
            }

            public static ButtonFunction Keyboard(FnKeyboardModifier modifiers, byte hidUsageId)
            {
                return Build(FnClass.Keyboard, new byte[] { (byte)modifiers, hidUsageId });
            }

            public static ButtonFunction KeyboardTurbo(FnKeyboardModifier modifiers, byte hidUsageId, ushort intervalMs)
            {
                return Build(FnClass.KeyboardTurbo, new byte[] { (byte)modifiers, hidUsageId, (byte)(intervalMs >> 8), (byte)(intervalMs & 0xFF) });
            }

            public static ButtonFunction MouseTurbo(FnMouse target, ushort intervalMs)
            {
                return Build(FnClass.MouseTurbo, new byte[] { (byte)target, (byte)(intervalMs >> 8), (byte)(intervalMs & 0xFF) });
            }

            public static ButtonFunction DpiCycle(FnDpiSwitchMode mode)
            {
                return Build(FnClass.DpiSwitch, new byte[] { (byte)mode });
            }

            /// <summary>Hypershift toggle carries one fixed byte 0x01 (cmd_button_en.md).</summary>
            public static ButtonFunction HypershiftToggle()
            {
                return Build(FnClass.HypershiftToggle, new byte[] { 0x01 });
            }

            public static ButtonFunction Disabled()
            {
                return Build(FnClass.Disabled, new byte[0]);
            }

            private static ButtonFunction Build(FnClass cls, byte[] value)
            {
                ButtonFunction fn = new ButtonFunction();
                fn.Class = cls;
                fn.ValueLength = (byte)Math.Min(value.Length, 5);
                Array.Copy(value, fn.Value, fn.ValueLength);
                return fn;
            }

            /// <summary>Classes the phase-1 writer may overwrite; macro/system/consumer etc. are protected (design §4.3/§5).</summary>
            public static bool IsSupportedClass(FnClass c)
            {
                switch (c)
                {
                    case FnClass.Disabled:
                    case FnClass.Mouse:
                    case FnClass.Keyboard:
                    case FnClass.DoubleClick:
                    case FnClass.KeyboardTurbo:
                    case FnClass.MouseTurbo:
                    case FnClass.DpiSwitch:
                    case FnClass.HypershiftToggle:
                        return true;
                    default:
                        return false;
                }
            }

            public override string ToString()
            {
                string cls = Enum.IsDefined(typeof(FnClass), Class) ? Class.ToString() : "Unknown(0x" + ((byte)Class).ToString("X2") + ")";
                StringBuilder sb = new StringBuilder();
                sb.Append(cls);
                sb.Append('(');
                for (int i = 0; i < ValueLength; i++)
                {
                    if (i > 0) sb.Append(' ');
                    sb.Append(Value[i].ToString("X2"));
                }
                sb.Append(')');
                return sb.ToString();
            }
        }

        /// <summary>Per-button read result. LayerFlag semantics per M0b hypothesis: 0x01 implicit/factory record, 0x00 explicit record — display hint only, never part of validation.</summary>
        public sealed class ButtonFunctionInfo
        {
            public ButtonId Button;
            public ButtonFunction Function;
            public byte LayerFlag;
            public bool IsExplicitRecord { get { return LayerFlag == 0x00; } }
        }

        /// <summary>Outcome of a batch mapping operation (design §6).</summary>
        public sealed class ButtonMappingBatchResult
        {
            public bool Success;
            public bool RolledBack;                            // true: failed batch was restored to pre-apply state
            public List<ButtonFunctionInfo> PreApplySnapshot;
            public List<ButtonFunctionInfo> ReadBack;          // full read-back after success (null if device stopped responding)
            public List<ButtonId> SkippedUnknown;              // keys skipped by the unknown/macro class protection
            public string ErrorMessage;
        }

        // ---- protocol constants (M0a verified: tid 0x1F, dataSize 10, function at args+3) ----
        private const byte BtnTid = 0x1F;
        private const byte BtnClass = 0x02;
        private const byte BtnGetId = 0x8C;
        private const byte BtnSetId = 0x0C;
        private const byte BtnDataSize = 10;
        private const byte BtnProfileOnboard = 0x01;           // this model has a single onboard profile
        private const byte BtnHypershiftOff = 0x00;            // phase 1: base layer only

        private static readonly ButtonId[] AllSupportedButtons = new ButtonId[]
        {
            ButtonId.Left, ButtonId.Right, ButtonId.Middle, ButtonId.Backward,
            ButtonId.Forward, ButtonId.WheelUp, ButtonId.WheelDown, ButtonId.DpiButton,
        };

        // Factory defaults captured on-device (M0b-report §5). Forward is the one inferred entry.
        private static readonly Dictionary<ButtonId, ButtonFunction> FactoryDefaultFunctions = BuildFactoryDefaults();

        private static Dictionary<ButtonId, ButtonFunction> BuildFactoryDefaults()
        {
            Dictionary<ButtonId, ButtonFunction> d = new Dictionary<ButtonId, ButtonFunction>();
            d[ButtonId.Left] = ButtonFunction.MouseTarget(FnMouse.Left);
            d[ButtonId.Right] = ButtonFunction.MouseTarget(FnMouse.Right);
            d[ButtonId.Middle] = ButtonFunction.MouseTarget(FnMouse.Middle);
            d[ButtonId.Backward] = ButtonFunction.MouseTarget(FnMouse.Backward);
            d[ButtonId.Forward] = ButtonFunction.MouseTarget(FnMouse.Forward);     // inferred (M0b-report §5)
            d[ButtonId.WheelUp] = ButtonFunction.MouseTarget(FnMouse.WheelUp);
            d[ButtonId.WheelDown] = ButtonFunction.MouseTarget(FnMouse.WheelDown);
            d[ButtonId.DpiButton] = ButtonFunction.DpiCycle(FnDpiSwitchMode.NextLoop);
            return d;
        }

        /// <summary>Read-only view of the factory table (UI: "reset single button").</summary>
        public static ButtonFunction GetFactoryDefault(ButtonId button)
        {
            ButtonFunction fn;
            return FactoryDefaultFunctions.TryGetValue(button, out fn) ? fn : null;
        }

        // =====================================================================
        // Transaction primitive with the retry policy of design §4.4/§4.6
        // (revised per M0a: 0x03 in read context is a deterministic rejection).
        // =====================================================================

        private sealed class BtnTransaction
        {
            public byte[] Response;            // last full feature report (null when every attempt failed at HID API level)
            public byte Status;                // last observed status code
            public bool NotSupported;          // status 0x05
            public bool DeterministicFailure;  // 0x03 (read context) / 0x05 / unknown status — retrying will not help
        }

        /// <summary>Set → Sleep(15) → Get with retries: NEW/BUSY/TIMEOUT and HID-level failure retry up to 3 times (backoff 50/150/300ms); FAIL (0x03) and unknown statuses retry once in write context only; NOT_SUPPORTED never retries.</summary>
        private static BtnTransaction BtnTransact(IntPtr handle, ushort frbl, bool prepended, byte cmdClass, byte cmdId, byte dataSize, byte[] args, bool writeContext)
        {
            int offset = prepended ? 1 : 0;
            int[] backoff = { 0, 50, 150, 300 };
            BtnTransaction t = new BtnTransaction();
            bool retriedFail = false;
            for (int attempt = 0; attempt < 4; attempt++)
            {
                if (attempt > 0) Thread.Sleep(backoff[attempt]);

                byte[] req = RazerDeviceHelper.CreateRazerReport(BtnTid, cmdClass, cmdId, dataSize, frbl, prepended, args);
                byte[] resp = new byte[frbl];
                if (prepended) resp[0] = 0x00;
                if (!RazerDeviceHelper.HidD_SetFeature(handle, req, req.Length)) { t.Status = 0x04; continue; }   // HID-level failure reported as TIMEOUT
                Thread.Sleep(15);
                if (!RazerDeviceHelper.HidD_GetFeature(handle, resp, resp.Length)) { t.Status = 0x04; continue; } // HID-level failure reported as TIMEOUT

                t.Response = resp;
                t.Status = resp[offset + 0];
                if (t.Status == 0x02) return t;
                if (t.Status == 0x05) { t.NotSupported = true; t.DeterministicFailure = true; return t; }
                if (t.Status == 0x03)
                {
                    if (!writeContext || retriedFail) { t.DeterministicFailure = true; return t; }
                    retriedFail = true;
                    continue;
                }
                if (t.Status == 0x00 || t.Status == 0x01 || t.Status == 0x04) continue;
                // unknown status: treat as FAIL
                if (!writeContext || retriedFail) { t.DeterministicFailure = true; return t; }
                retriedFail = true;
            }
            return t;
        }

        /// <summary>Read response predicate (M0a §8-1): status/class/id echo plus [profile][button] echo; args[2] is data, not an echo.</summary>
        private static bool BtnTryParse(ButtonId button, byte[] resp, bool prepended, out ButtonFunctionInfo info)
        {
            info = null;
            int offset = prepended ? 1 : 0;
            if (resp == null || resp[offset] != 0x02 || resp[offset + 6] != BtnClass || resp[offset + 7] != BtnGetId) return false;
            if (resp[offset + 8] != BtnProfileOnboard || resp[offset + 9] != (byte)button) return false;
            info = new ButtonFunctionInfo();
            info.Button = button;
            info.LayerFlag = resp[offset + 10];
            info.Function = ButtonFunction.FromBytes7(resp, offset + 11);        // function structure at args+3
            return true;
        }

        /// <summary>Reads one button binding; returns null on success or a human-readable error.
        /// Stale-echo retry (measured 2026-10-17, smoke on-device): GetFeature occasionally
        /// returns the PREVIOUS transaction's answer — status 0x02 but [profile][button] echo
        /// mismatch. One re-issued read then returns the fresh answer.</summary>
        private static string BtnReadOne(IntPtr handle, ushort frbl, bool prepended, ButtonId button, out ButtonFunctionInfo info)
        {
            for (int attempt = 0; ; attempt++)
            {
                byte[] args = new byte[BtnDataSize];
                args[0] = BtnProfileOnboard;
                args[1] = (byte)button;
                args[2] = BtnHypershiftOff;
                BtnTransaction t = BtnTransact(handle, frbl, prepended, BtnClass, BtnGetId, BtnDataSize, args, false);
                if (BtnTryParse(button, t.Response, prepended, out info)) return null;
                if (attempt == 0 && t.Status == 0x02) { Thread.Sleep(20); continue; }   // stale echo — retry once
                if (t.NotSupported) return string.Format("button 0x{0:X2} not supported by device", (byte)button);
                return string.Format("read failed for button 0x{0:X2} (status 0x{1:X2})", (byte)button, t.Status);
            }
        }

        /// <summary>Writes one binding and verifies by read-back; "written" means the read-back matches semantically (design §4.4). Returns null on success.
        /// Stale-echo retry mirrors BtnReadOne: a status-0x02 answer failing the echo predicate is a
        /// leftover buffer; re-issuing is safe because the write is idempotent (same args, same result).</summary>
        private static string BtnWriteOne(IntPtr handle, ushort frbl, bool prepended, ButtonId button, ButtonFunction fn)
        {
            for (int attempt = 0; ; attempt++)
            {
                byte[] args = new byte[BtnDataSize];
                args[0] = BtnProfileOnboard;
                args[1] = (byte)button;
                args[2] = BtnHypershiftOff;
                Array.Copy(fn.ToBytes7(), 0, args, 3, 7);

                BtnTransaction t = BtnTransact(handle, frbl, prepended, BtnClass, BtnSetId, BtnDataSize, args, true);
                int offset = prepended ? 1 : 0;
                // write predicate (M0b-report §8-2): st==02 + class/id echo + [profile][button] echo
                bool predicateOk = t.Response != null && t.Response[offset] == 0x02 && t.Response[offset + 6] == BtnClass && t.Response[offset + 7] == BtnSetId
                    && t.Response[offset + 8] == BtnProfileOnboard && t.Response[offset + 9] == (byte)button;
                if (predicateOk) break;
                if (attempt == 0 && t.Status == 0x02) { Thread.Sleep(20); continue; }   // stale echo — retry once
                return string.Format("write rejected for button 0x{0:X2} (status 0x{1:X2})", (byte)button, t.Status);
            }

            Thread.Sleep(20);
            ButtonFunctionInfo rb;
            string err = BtnReadOne(handle, frbl, prepended, button, out rb);
            if (err != null) return err + " during read-back";
            if (!rb.Function.SemanticEquals(fn)) return string.Format("read-back mismatch for button 0x{0:X2}", (byte)button);
            return null;
        }

        /// <summary>Liveness handshake (design §4.4): get_serial must answer before any write.</summary>
        private static bool BtnHandshake(IntPtr handle, ushort frbl, bool prepended)
        {
            BtnTransaction t = BtnTransact(handle, frbl, prepended, 0x00, 0x82, 16, null, false);
            int offset = prepended ? 1 : 0;
            return t.Response != null && t.Response[offset] == 0x02 && t.Response[offset + 6] == 0x00 && t.Response[offset + 7] == 0x82;
        }

        /// <summary>Handshake with retry (measured 2026-10-17, smoke on-device): a batch started
        /// immediately after a previous write can fail the first get_serial — the firmware appears
        /// briefly busy; a short backoff clears it.</summary>
        private static bool BtnHandshakeRetry(IntPtr handle, ushort frbl, bool prepended)
        {
            for (int i = 0; i < 3; i++)
            {
                if (BtnHandshake(handle, frbl, prepended)) return true;
                if (i < 2) Thread.Sleep(500);
            }
            return false;
        }

        // =====================================================================
        // Device discovery — 6th inline copy of the enumeration pattern with the
        // tightened write-path identity check (design §3): vid_1532 + FRBL>=90,
        // plus (path contains pid_00b8 or product string matches Viper V3
        // HyperSpeed); ambiguous multiple candidates are rejected.
        // =====================================================================

        /// <summary>Opens the Razer control interface; returns IntPtr.Zero and sets error on failure.</summary>
        private static IntPtr OpenButtonMappingDevice(out ushort frbl, out bool prepended, bool requireTargetDevice, out string error)
        {
            frbl = 0;
            prepended = false;
            error = null;

            Guid hidGuid;
            RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);
            IntPtr devInfo = RazerDeviceHelper.SetupDiGetClassDevs(ref hidGuid, null, IntPtr.Zero, RazerDeviceHelper.DIGCF_PRESENT | RazerDeviceHelper.DIGCF_DEVICEINTERFACE);
            if (devInfo == IntPtr.Zero || devInfo == new IntPtr(-1)) { error = "device enumeration failed"; return IntPtr.Zero; }

            RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA ifData = new RazerDeviceHelper.SP_DEVICE_INTERFACE_DATA();
            ifData.cbSize = Marshal.SizeOf(ifData);

            uint memberIdx = 0;
            IntPtr found = IntPtr.Zero;
            int candidates = 0;
            try
            {
                while (RazerDeviceHelper.SetupDiEnumDeviceInterfaces(devInfo, IntPtr.Zero, ref hidGuid, memberIdx++, ref ifData))
                {
                    uint reqSize;
                    RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, IntPtr.Zero, 0, out reqSize, IntPtr.Zero);
                    IntPtr detailBuffer = Marshal.AllocHGlobal((int)reqSize);
                    IntPtr opened = IntPtr.Zero;
                    try
                    {
                        Marshal.WriteInt32(detailBuffer, IntPtr.Size == 8 ? 8 : 5);
                        if (!RazerDeviceHelper.SetupDiGetDeviceInterfaceDetail(devInfo, ref ifData, detailBuffer, reqSize, out reqSize, IntPtr.Zero)) continue;
                        IntPtr pDevicePath = new IntPtr(detailBuffer.ToInt64() + 4);
                        string devicePath = Marshal.PtrToStringAuto(pDevicePath);
                        if (devicePath == null || devicePath.ToLower().IndexOf("vid_1532") < 0) continue;

                        opened = RazerDeviceHelper.CreateFile(devicePath, 0, RazerDeviceHelper.FILE_SHARE_READ | RazerDeviceHelper.FILE_SHARE_WRITE, IntPtr.Zero, RazerDeviceHelper.OPEN_EXISTING, 0, IntPtr.Zero);
                        if (opened == IntPtr.Zero || opened.ToInt64() == -1) { opened = IntPtr.Zero; continue; }

                        IntPtr preparsed;
                        if (!RazerDeviceHelper.HidD_GetPreparsedData(opened, out preparsed)) { RazerDeviceHelper.CloseHandle(opened); opened = IntPtr.Zero; continue; }
                        RazerDeviceHelper.HIDP_CAPS caps;
                        RazerDeviceHelper.HidP_GetCaps(preparsed, out caps);
                        RazerDeviceHelper.HidD_FreePreparsedData(preparsed);
                        if (caps.FeatureReportByteLength < 90) { RazerDeviceHelper.CloseHandle(opened); opened = IntPtr.Zero; continue; }

                        if (requireTargetDevice)
                        {
                            StringBuilder prod = new StringBuilder(256);
                            string prodName = RazerDeviceHelper.HidD_GetProductString(opened, prod, prod.Capacity) && prod.Length > 0 ? prod.ToString() : "";
                            bool match = devicePath.ToLower().IndexOf("pid_00b8") >= 0
                                         || prodName.IndexOf("Viper V3 HyperSpeed", StringComparison.OrdinalIgnoreCase) >= 0;
                            if (!match) { RazerDeviceHelper.CloseHandle(opened); opened = IntPtr.Zero; continue; }
                        }

                        candidates++;
                        if (found != IntPtr.Zero) { RazerDeviceHelper.CloseHandle(opened); opened = IntPtr.Zero; continue; }
                        found = opened;
                        opened = IntPtr.Zero;
                        frbl = caps.FeatureReportByteLength;
                        prepended = caps.FeatureReportByteLength == 91;
                    }
                    finally
                    {
                        if (opened != IntPtr.Zero) RazerDeviceHelper.CloseHandle(opened);
                        Marshal.FreeHGlobal(detailBuffer);
                    }
                }
            }
            finally
            {
                RazerDeviceHelper.SetupDiDestroyDeviceInfoList(devInfo);
            }

            if (found == IntPtr.Zero) { error = "no Razer control interface found"; return IntPtr.Zero; }
            if (candidates > 1)
            {
                RazerDeviceHelper.CloseHandle(found);
                error = "ambiguous: multiple Razer control interfaces";
                return IntPtr.Zero;
            }
            return found;
        }

        // =====================================================================
        // Batch entry points — one hidLock hold per batch (design §3-4), called
        // from background threads; UI marshalling is left to the caller (M2).
        // =====================================================================

        /// <summary>Reads the current binding of every supported button (~9 transactions). Null on device failure.</summary>
        public static List<ButtonFunctionInfo> ReadAllButtonFunctions()
        {
            lock (RazerDeviceHelper.hidLock)
            {
                ushort frbl;
                bool prepended;
                string err;
                IntPtr handle = OpenButtonMappingDevice(out frbl, out prepended, false, out err);
                if (handle == IntPtr.Zero) return null;
                try
                {
                    if (!BtnHandshakeRetry(handle, frbl, prepended)) return null;
                    List<ButtonFunctionInfo> list = new List<ButtonFunctionInfo>();
                    foreach (ButtonId b in AllSupportedButtons)
                    {
                        ButtonFunctionInfo info;
                        if (BtnReadOne(handle, frbl, prepended, b, out info) != null) return null;
                        list.Add(info);
                    }
                    return list;
                }
                finally
                {
                    RazerDeviceHelper.CloseHandle(handle);
                }
            }
        }

        /// <summary>Applies a batch of bindings under one lock: complete pre-apply snapshot → per-key write+verify → auto rollback on failure (design §4.4/§6, N5).</summary>
        public static ButtonMappingBatchResult ApplyButtonFunctions(Dictionary<ButtonId, ButtonFunction> changes, bool allowOverwriteUnknown)
        {
            lock (RazerDeviceHelper.hidLock)
            {
                ButtonMappingBatchResult result = new ButtonMappingBatchResult();
                ushort frbl;
                bool prepended;
                string err;
                IntPtr handle = OpenButtonMappingDevice(out frbl, out prepended, true, out err);
                if (handle == IntPtr.Zero)
                {
                    result.ErrorMessage = "device unavailable: " + err;
                    return result;
                }
                try
                {
                    if (!BtnHandshakeRetry(handle, frbl, prepended))
                    {
                        result.ErrorMessage = "device offline or asleep";
                        return result;
                    }

                    // 1. pre-apply snapshot — must be complete before any write (N5)
                    List<ButtonFunctionInfo> snapshot = new List<ButtonFunctionInfo>();
                    foreach (ButtonId b in AllSupportedButtons)
                    {
                        ButtonFunctionInfo info;
                        string rerr = BtnReadOne(handle, frbl, prepended, b, out info);
                        if (rerr != null)
                        {
                            result.ErrorMessage = "incomplete pre-apply snapshot, no writes performed: " + rerr;
                            return result;
                        }
                        snapshot.Add(info);
                    }
                    result.PreApplySnapshot = snapshot;

                    // 2. build the write plan with the unknown-class protection (design §4.3/§5)
                    List<KeyValuePair<ButtonId, ButtonFunction>> plan = new List<KeyValuePair<ButtonId, ButtonFunction>>();
                    foreach (ButtonId b in AllSupportedButtons)
                    {
                        ButtonFunction fn;
                        if (changes == null || !changes.TryGetValue(b, out fn)) continue;
                        if (!ButtonFunction.IsSupportedClass(snapshot.Find(delegate(ButtonFunctionInfo x) { return x.Button == b; }).Function.Class) && !allowOverwriteUnknown)
                        {
                            if (result.SkippedUnknown == null) result.SkippedUnknown = new List<ButtonId>();
                            result.SkippedUnknown.Add(b);
                            continue;
                        }
                        plan.Add(new KeyValuePair<ButtonId, ButtonFunction>(b, fn));
                    }
                    if (plan.Count == 0)
                    {
                        result.Success = true;
                        result.ReadBack = snapshot;
                        return result;
                    }

                    // 3. persist pending snapshot for crash recovery (design §6)
                    SavePendingButtonSnapshot(snapshot);

                    // 4. write each key; roll back to the snapshot on first failure
                    List<ButtonId> written = new List<ButtonId>();
                    foreach (KeyValuePair<ButtonId, ButtonFunction> kv in plan)
                    {
                        string werr = BtnWriteOne(handle, frbl, prepended, kv.Key, kv.Value);
                        if (werr != null)
                        {
                            // The failing key joins the rollback set too: "write accepted but
                            // read-back mismatched" leaves the key changed on-device, and even a
                            // rejected write cannot be proven harmless — restoring its snapshot
                            // value is always safe.
                            written.Add(kv.Key);
                            result.ErrorMessage = werr;
                            result.RolledBack = BtnRollback(handle, frbl, prepended, snapshot, written);
                            if (result.RolledBack) result.ErrorMessage += "; restored to pre-apply state";
                            else result.ErrorMessage += "; ROLLBACK FAILED — use 'restore from backup' or Razer Synapse";
                            return result;
                        }
                        written.Add(kv.Key);
                        Thread.Sleep(20);
                    }

                    // 5. success: full read-back, promote snapshot to backup, clear pending
                    List<ButtonFunctionInfo> readBack = new List<ButtonFunctionInfo>();
                    foreach (ButtonId b in AllSupportedButtons)
                    {
                        ButtonFunctionInfo info;
                        if (BtnReadOne(handle, frbl, prepended, b, out info) != null)
                        {
                            readBack = null;      // bindings did apply; the UI refresh will retry
                            break;
                        }
                        readBack.Add(info);
                    }
                    result.ReadBack = readBack;
                    SaveButtonBackup(snapshot);
                    ClearPendingButtonSnapshot();
                    result.Success = true;
                    return result;
                }
                finally
                {
                    RazerDeviceHelper.CloseHandle(handle);
                }
            }
        }

        /// <summary>Restores only the keys that were actually written, verifying each (design §6).</summary>
        private static bool BtnRollback(IntPtr handle, ushort frbl, bool prepended, List<ButtonFunctionInfo> snapshot, List<ButtonId> written)
        {
            bool ok = true;
            foreach (ButtonId b in written)
            {
                ButtonFunctionInfo snap = snapshot.Find(delegate(ButtonFunctionInfo x) { return x.Button == b; });
                if (BtnWriteOne(handle, frbl, prepended, b, snap.Function) != null) ok = false;
                Thread.Sleep(20);
            }
            if (ok) ClearPendingButtonSnapshot();      // pending residue is kept only when the rollback itself failed
            return ok;
        }

        /// <summary>"Restore all defaults": writes the factory table captured in M0b (docs/M0b-report.md §5). Buttons bound to macro/unknown classes are skipped unless forced.</summary>
        public static ButtonMappingBatchResult RestoreButtonDefaults(bool forceOverwriteUnknown)
        {
            Dictionary<ButtonId, ButtonFunction> changes = new Dictionary<ButtonId, ButtonFunction>();
            foreach (KeyValuePair<ButtonId, ButtonFunction> kv in FactoryDefaultFunctions) changes[kv.Key] = kv.Value;
            return ApplyButtonFunctions(changes, forceOverwriteUnknown);
        }

        /// <summary>"Restore from backup": applies a stored snapshot JSON (version 1 format, §6). Returns null when the JSON is not a valid backup.</summary>
        public static ButtonMappingBatchResult RestoreButtonFunctionsFromBackupJson(string json, bool forceOverwriteUnknown)
        {
            List<KeyValuePair<ButtonId, ButtonFunction>> entries = ParseButtonSnapshotJson(json);
            if (entries == null) return null;
            Dictionary<ButtonId, ButtonFunction> changes = new Dictionary<ButtonId, ButtonFunction>();
            foreach (KeyValuePair<ButtonId, ButtonFunction> kv in entries) changes[kv.Key] = kv.Value;
            return ApplyButtonFunctions(changes, forceOverwriteUnknown);
        }

        // =====================================================================
        // Backup / pending-snapshot persistence (design §6). Hand-rolled fixed-shape
        // JSON — all fields are hex, so no escaping is ever needed:
        //   {"version":1,"entries":[{"button":"04","fnClass":"01","value":"04"},...]}
        // =====================================================================

        private const string ButtonBackupRegistryKey = @"Software\FerrisPulse\ButtonMappingBackup";

        private static string ButtonSnapshotToJson(List<ButtonFunctionInfo> snapshot)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"version\":1,\"entries\":[");
            bool first = true;
            foreach (ButtonFunctionInfo info in snapshot)
            {
                if (!first) sb.Append(',');
                first = false;
                sb.Append(string.Format("{{\"button\":\"{0:X2}\",\"fnClass\":\"{1:X2}\",\"value\":\"",
                    (byte)info.Button, (byte)info.Function.Class));
                StringBuilder val = new StringBuilder();
                for (int i = 0; i < info.Function.ValueLength; i++) val.Append(info.Function.Value[i].ToString("X2"));
                sb.Append(val.ToString());
                sb.Append("\"}");
            }
            sb.Append("]}");
            return sb.ToString();
        }

        /// <summary>Parses the version-1 snapshot JSON; null on any deviation from the fixed shape.</summary>
        private static List<KeyValuePair<ButtonId, ButtonFunction>> ParseButtonSnapshotJson(string json)
        {
            if (string.IsNullOrEmpty(json)) return null;
            List<KeyValuePair<ButtonId, ButtonFunction>> list = new List<KeyValuePair<ButtonId, ButtonFunction>>();
            int pos = 0;
            if (!Expect(json, ref pos, "{\"version\":1,\"entries\":[")) return null;
            if (Peek(json, pos) == ']')
            {
                return Expect(json, ref pos, "]}") ? list : null;
            }
            while (true)
            {
                byte button, fnClass;
                if (!Expect(json, ref pos, "{\"button\":\"")) return null;
                if (!TryParseHexByteAt(json, ref pos, out button)) return null;
                if (!Expect(json, ref pos, "\",\"fnClass\":\"")) return null;
                if (!TryParseHexByteAt(json, ref pos, out fnClass)) return null;
                if (!Expect(json, ref pos, "\",\"value\":\"")) return null;
                ButtonFunction fn = new ButtonFunction();
                fn.Class = (FnClass)fnClass;
                while (Peek(json, pos) != '"')
                {
                    if (fn.ValueLength >= 5) return null;      // check before the store: Value is byte[5]
                    byte v;
                    if (!TryParseHexByteAt(json, ref pos, out v)) return null;
                    fn.Value[fn.ValueLength++] = v;
                }
                if (!Expect(json, ref pos, "\"}")) return null;
                list.Add(new KeyValuePair<ButtonId, ButtonFunction>((ButtonId)button, fn));

                char c = Peek(json, pos);
                if (c == ',') { pos++; continue; }
                if (c == ']') return Expect(json, ref pos, "]}") ? list : null;
                return null;
            }
        }

        private static bool Expect(string s, ref int pos, string literal)
        {
            if (pos + literal.Length > s.Length || s.Substring(pos, literal.Length) != literal) return false;
            pos += literal.Length;
            return true;
        }

        private static char Peek(string s, int pos)
        {
            return pos < s.Length ? s[pos] : '\0';
        }

        /// <summary>Non-throwing hex byte parse at a fixed position; advances pos only on success.</summary>
        private static bool TryParseHexByteAt(string s, ref int pos, out byte value)
        {
            value = 0;
            if (pos + 2 > s.Length) return false;
            int hi = HexDigit(s[pos]);
            int lo = HexDigit(s[pos + 1]);
            if (hi < 0 || lo < 0) return false;
            value = (byte)((hi << 4) | lo);
            pos += 2;
            return true;
        }

        private static int HexDigit(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            return -1;
        }

        private static void SavePendingButtonSnapshot(List<ButtonFunctionInfo> snapshot)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(ButtonBackupRegistryKey))
                {
                    if (key != null) key.SetValue("PendingSnapshot", ButtonSnapshotToJson(snapshot));
                }
            }
            catch { }
        }

        private static void ClearPendingButtonSnapshot()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(ButtonBackupRegistryKey, true))
                {
                    if (key != null) key.DeleteValue("PendingSnapshot", false);
                }
            }
            catch { }
        }

        private static void SaveButtonBackup(List<ButtonFunctionInfo> snapshot)
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.CreateSubKey(ButtonBackupRegistryKey))
                {
                    if (key != null) key.SetValue("Backup", ButtonSnapshotToJson(snapshot));
                }
            }
            catch { }
        }

        /// <summary>Last successful batch's pre-apply snapshot (the "restore from backup" source); null when absent.</summary>
        public static string LoadButtonBackupJson()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(ButtonBackupRegistryKey))
                {
                    object v = key != null ? key.GetValue("Backup") : null;
                    return v as string;
                }
            }
            catch { return null; }
        }

        /// <summary>Crash residue: a pending snapshot left behind by an interrupted batch (design §6). Null when absent.</summary>
        public static string LoadPendingButtonSnapshotJson()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(ButtonBackupRegistryKey))
                {
                    object v = key != null ? key.GetValue("PendingSnapshot") : null;
                    return v as string;
                }
            }
            catch { return null; }
        }
    }
}
