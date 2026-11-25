// ============================================================================
// 模块名称: MultiTrayIconHost.cs
// 所属区域: UI/Tray (Win32 原生多托盘宿主层)
// 核心职责:
//   基于 Win32 Shell_NotifyIconW + 静态确定性 NIF_GUID 管理多个独立状态栏托盘图标。
//   支持：按槽位固定 GUID 防止重连/重启缩回溢出区、差分化图标刷新与 GDI 句柄释放、
//   独立槽位左键/双击/右键事件路由、Explorer WM_TASKBARCREATED 全量重建及沙盒无头隔离。
// 依赖关系: TraySlotPlanner, HardwareCommandBus
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、结构体或 P/Invoke 时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（❌ 严禁 $"..." 插值、?.、nameof、out var）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public sealed class MultiTrayIconHost : IDisposable
    {
        private const int WM_APP = 0x8000;
        private const int CallbackMsgBase = WM_APP + 0x100;
        private const int MaxSupportedSlots = 16;

        private const int WM_LBUTTONUP = 0x0202;
        private const int WM_LBUTTONDBLCLK = 0x0203;
        private const int WM_RBUTTONUP = 0x0205;
        private const int WM_CONTEXTMENU = 0x007B;

        private const uint NIM_ADD = 0x00000000;
        private const uint NIM_MODIFY = 0x00000001;
        private const uint NIM_DELETE = 0x00000002;

        private const uint NIF_MESSAGE = 0x00000001;
        private const uint NIF_ICON = 0x00000002;
        private const uint NIF_TIP = 0x00000004;
        private const uint NIF_GUID = 0x00000020;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
        private struct NOTIFYICONDATAW
        {
            public int cbSize;
            public IntPtr hWnd;
            public uint uID;
            public uint uFlags;
            public uint uCallbackMessage;
            public IntPtr hIcon;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 128)]
            public string szTip;
            public uint dwState;
            public uint dwStateMask;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string szInfo;
            public uint uTimeoutOrVersion;
            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
            public string szInfoTitle;
            public uint dwInfoFlags;
            public Guid guidItem;
            public IntPtr hBalloonIcon;
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern bool Shell_NotifyIconW(uint dwMessage, ref NOTIFYICONDATAW lpData);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint RegisterWindowMessage(string lpString);

        private sealed class SlotState
        {
            public int SlotIndex;
            public string DeviceId;
            public string Signature;
            public string Tooltip;
            public Icon IconHandle;
            public bool IsActive;
            public bool NativeAdded;
            public bool UseGuidFlag = true;
            public TraySlotPlanItem LastPlan;
        }

        private sealed class MessageSinkWindow : NativeWindow
        {
            private readonly MultiTrayIconHost owner;

            public MessageSinkWindow(MultiTrayIconHost owner)
            {
                this.owner = owner;
                CreateParams cp = new CreateParams();
                cp.Caption = "FerrisPulse_MultiTraySink";
                CreateHandle(cp);
            }

            protected override void WndProc(ref Message m)
            {
                if (owner != null && owner.OnWindowMessage(ref m))
                {
                    return;
                }
                base.WndProc(ref m);
            }
        }

        private readonly Dictionary<int, SlotState> slots = new Dictionary<int, SlotState>();
        private MessageSinkWindow sinkWindow;
        private readonly uint wmTaskbarCreated;
        private int lastClickTick;
        private int lastDoubleClickTick;
        private bool disposed;

        public event Action<int, string> SlotClicked;
        public event Action<int, string> SlotDoubleClicked;
        public event Action<int, string, Point> SlotContextMenuRequested;

        public MultiTrayIconHost()
        {
            try
            {
                wmTaskbarCreated = RegisterWindowMessage("TaskbarCreated");
            }
            catch
            {
                wmTaskbarCreated = 0;
            }
        }

        public int ActiveSlotCount
        {
            get
            {
                int count = 0;
                foreach (var kv in slots)
                {
                    if (kv.Value != null && kv.Value.IsActive) count++;
                }
                return count;
            }
        }

        public string GetSlotDeviceId(int slotIndex)
        {
            SlotState st;
            if (slots.TryGetValue(slotIndex, out st) && st != null && st.IsActive)
            {
                return st.DeviceId;
            }
            return null;
        }

        public static Guid GetSlotGuid(int slotIndex)
        {
            if (slotIndex < 0) slotIndex = 0;
            if (slotIndex >= MaxSupportedSlots) slotIndex = MaxSupportedSlots - 1;
            return new Guid(
                unchecked((int)0x8F2C0100),
                0x4E3A,
                0x4B9D,
                0xA0, 0x11, 0x00, 0x00, 0x00, 0x00, 0x00, (byte)slotIndex);
        }

        private void EnsureSinkWindow()
        {
            if (disposed || HardwareCommandBus.IsHardwareSandboxActive) return;
            if (sinkWindow == null || sinkWindow.Handle == IntPtr.Zero)
            {
                sinkWindow = new MessageSinkWindow(this);
            }
        }

        public void SyncSlots(IList<TraySlotPlanItem> plans, Func<TraySlotPlanItem, Icon> iconRenderer)
        {
            if (disposed) return;
            EnsureSinkWindow();

            int targetCount = (plans != null) ? Math.Min(plans.Count, MaxSupportedSlots) : 0;

            for (int i = 0; i < targetCount; i++)
            {
                var plan = plans[i];
                if (plan == null) continue;

                SlotState st;
                if (!slots.TryGetValue(i, out st))
                {
                    st = new SlotState { SlotIndex = i };
                    slots[i] = st;
                }

                string sig = BuildVisualSignature(plan);
                string tip = NormalizeTooltip(plan.TooltipText);
                bool needIconRedraw = (st.IconHandle == null) || !string.Equals(st.Signature, sig, StringComparison.Ordinal);
                bool needTipUpdate = !string.Equals(st.Tooltip, tip, StringComparison.Ordinal);
                bool wasActive = st.IsActive;

                if (needIconRedraw && iconRenderer != null)
                {
                    Icon nextIcon = iconRenderer(plan);
                    if (nextIcon != null)
                    {
                        if (st.IconHandle != null)
                        {
                            try { st.IconHandle.Dispose(); } catch { }
                        }
                        st.IconHandle = nextIcon;
                    }
                }

                st.DeviceId = plan.DeviceId ?? "";
                st.Signature = sig;
                st.Tooltip = tip;
                st.LastPlan = plan;
                st.IsActive = true;

                if (!wasActive || !st.NativeAdded)
                {
                    AddOrModifyNativeIcon(st, true);
                }
                else if (needIconRedraw || needTipUpdate)
                {
                    AddOrModifyNativeIcon(st, false);
                }
            }

            // 撤回尾部多余槽位
            var existingKeys = new List<int>(slots.Keys);
            for (int i = 0; i < existingKeys.Count; i++)
            {
                int slotIdx = existingKeys[i];
                if (slotIdx >= targetCount)
                {
                    RemoveNativeSlot(slots[slotIdx]);
                }
            }
        }

        public void RecreateAll()
        {
            if (disposed || HardwareCommandBus.IsHardwareSandboxActive) return;
            EnsureSinkWindow();

            var ordered = new List<int>(slots.Keys);
            ordered.Sort();
            for (int i = 0; i < ordered.Count; i++)
            {
                SlotState st = slots[ordered[i]];
                if (st != null && st.IsActive && st.IconHandle != null)
                {
                    st.NativeAdded = false;
                    AddOrModifyNativeIcon(st, true);
                }
            }
        }

        public void HideAll()
        {
            var ordered = new List<int>(slots.Keys);
            for (int i = 0; i < ordered.Count; i++)
            {
                RemoveNativeSlot(slots[ordered[i]]);
            }
        }

        public void HandleSlotMouseMessage(int slotIndex, int mouseMsg)
        {
            SlotState st;
            if (!slots.TryGetValue(slotIndex, out st) || st == null || !st.IsActive) return;

            string devId = st.DeviceId ?? "";
            if (mouseMsg == WM_LBUTTONDBLCLK)
            {
                lastDoubleClickTick = Environment.TickCount;
                var h = SlotDoubleClicked;
                if (h != null) h(slotIndex, devId);
            }
            else if (mouseMsg == WM_LBUTTONUP)
            {
                int now = Environment.TickCount;
                int dblThreshold = Math.Max(400, SystemInformation.DoubleClickTime + 100);
                if (unchecked(now - lastDoubleClickTick) >= 0 && unchecked(now - lastDoubleClickTick) < dblThreshold)
                {
                    return;
                }
                if (unchecked(now - lastClickTick) >= 0 && unchecked(now - lastClickTick) < 200)
                {
                    return;
                }
                lastClickTick = now;

                var h = SlotClicked;
                if (h != null) h(slotIndex, devId);
            }
            else if (mouseMsg == WM_RBUTTONUP || mouseMsg == WM_CONTEXTMENU)
            {
                Point cursor = Control.MousePosition;
                var h = SlotContextMenuRequested;
                if (h != null) h(slotIndex, devId, cursor);
            }
        }

        private bool OnWindowMessage(ref Message m)
        {
            if (wmTaskbarCreated != 0 && (uint)m.Msg == wmTaskbarCreated)
            {
                RecreateAll();
                return false;
            }

            if (m.Msg >= CallbackMsgBase && m.Msg < CallbackMsgBase + MaxSupportedSlots)
            {
                int slotIdx = m.Msg - CallbackMsgBase;
                int mouseMsg = m.LParam.ToInt32() & 0xFFFF;
                if (mouseMsg == WM_LBUTTONUP || mouseMsg == WM_LBUTTONDBLCLK || mouseMsg == WM_RBUTTONUP || mouseMsg == WM_CONTEXTMENU)
                {
                    HandleSlotMouseMessage(slotIdx, mouseMsg);
                    return true;
                }
            }
            return false;
        }

        private void AddOrModifyNativeIcon(SlotState st, bool isAdd)
        {
            if (HardwareCommandBus.IsHardwareSandboxActive) return;
            if (sinkWindow == null || sinkWindow.Handle == IntPtr.Zero) return;

            NOTIFYICONDATAW nid = BuildNotifyIconData(st);
            if (isAdd || !st.NativeAdded)
            {
                // 若先前同 GUID 残留于 Explorer（如异常退出），先静默清理再添加
                if (st.UseGuidFlag && !Shell_NotifyIconW(NIM_ADD, ref nid))
                {
                    Shell_NotifyIconW(NIM_DELETE, ref nid);
                    if (!Shell_NotifyIconW(NIM_ADD, ref nid))
                    {
                        // 某些精简环境或无签名路径可能拒绝 NIF_GUID：优雅回退标准 hWnd+uID 通道
                        st.UseGuidFlag = false;
                        nid = BuildNotifyIconData(st);
                        Shell_NotifyIconW(NIM_ADD, ref nid);
                    }
                }
                st.NativeAdded = true;
            }
            else
            {
                if (!Shell_NotifyIconW(NIM_MODIFY, ref nid))
                {
                    Shell_NotifyIconW(NIM_ADD, ref nid);
                }
            }
        }

        private void RemoveNativeSlot(SlotState st)
        {
            if (st == null) return;
            if (!HardwareCommandBus.IsHardwareSandboxActive && st.NativeAdded && sinkWindow != null && sinkWindow.Handle != IntPtr.Zero)
            {
                try
                {
                    NOTIFYICONDATAW nid = BuildNotifyIconData(st);
                    Shell_NotifyIconW(NIM_DELETE, ref nid);
                }
                catch { }
            }
            st.IsActive = false;
            st.NativeAdded = false;
            st.Signature = null;
            st.Tooltip = null;
            if (st.IconHandle != null)
            {
                try { st.IconHandle.Dispose(); } catch { }
                st.IconHandle = null;
            }
        }

        private NOTIFYICONDATAW BuildNotifyIconData(SlotState st)
        {
            NOTIFYICONDATAW nid = new NOTIFYICONDATAW();
            nid.cbSize = Marshal.SizeOf(typeof(NOTIFYICONDATAW));
            nid.hWnd = (sinkWindow != null) ? sinkWindow.Handle : IntPtr.Zero;
            nid.uID = (uint)(st.SlotIndex + 100);
            nid.uFlags = NIF_MESSAGE | NIF_ICON | NIF_TIP | (st.UseGuidFlag ? NIF_GUID : 0u);
            nid.uCallbackMessage = (uint)(CallbackMsgBase + st.SlotIndex);
            nid.hIcon = (st.IconHandle != null) ? st.IconHandle.Handle : IntPtr.Zero;
            nid.szTip = st.Tooltip ?? "";
            nid.guidItem = GetSlotGuid(st.SlotIndex);
            return nid;
        }

        private static string BuildVisualSignature(TraySlotPlanItem plan)
        {
            return string.Format("{0}|{1}|{2}|{3}|{4}|{5}|{6}|{7}",
                plan.Percent,
                plan.IsCharging ? 1 : 0,
                plan.VisualConnected ? 1 : 0,
                plan.IsSleeping ? 1 : 0,
                plan.IsFallback ? 1 : 0,
                plan.AccentColor.ToArgb(),
                plan.TrayStyle,
                plan.AllowLowBatteryTrayColor ? 1 : 0);
        }

        private static string NormalizeTooltip(string tip)
        {
            if (string.IsNullOrEmpty(tip)) return "FerrisPulse · 灵脉";
            return tip.Length > 127 ? tip.Substring(0, 127) : tip;
        }

        public void Dispose()
        {
            if (disposed) return;
            disposed = true;
            HideAll();
            slots.Clear();
            if (sinkWindow != null)
            {
                try { sinkWindow.DestroyHandle(); } catch { }
                sinkWindow = null;
            }
        }
    }
}
