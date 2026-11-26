// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️

// 1. 目标平台与编译规范：严格维持 C# 5.0 语法标准与 .NET Framework 4.0 兼容性。严禁引入任何第三方包或依赖。

// 2. 编码规范：所有文件严格保存为 UTF-8 带签名（BOM）格式，防止中文字符乱码。

// 3. 架构规范：本文件属于 MainForm 的分部类 (partial class) 之一。修改时请保持职责清晰，任何新增方法或逻辑请同步维护 ARCHITECTURE.md。

// 模块描述: MainForm 系统消息钩子 (WndProc)、任务栏重建响应、RawInput 鼠标唤醒与 USB 即插即用监听

using System;

using System.Collections.Generic;

using System.Diagnostics;

using System.Drawing;

using System.Drawing.Drawing2D;

using System.IO;

using System.Media;

using System.Runtime.InteropServices;

using System.Text;

using System.Threading;

using System.Windows.Forms;

using Microsoft.Win32;



namespace RazerBatteryTray

{

    public partial class MainForm : Form

    {



        private int lastSizingLayoutTick = 0;
        private float pendingDragScale = 0f;

        protected override void OnPaintBackground(PaintEventArgs e)

        {

            // 彻底杜绝 Windows 默认浅色/白色笔刷擦除闪烁，始终以暗黑底色填充背景

            using (SolidBrush b = new SolidBrush(this.BackColor))

            {

                e.Graphics.FillRectangle(b, e.ClipRectangle);

            }

        }



        protected override void WndProc(ref Message m)

        {

            const int WM_QUERYENDSESSION = 0x0011;

            const int WM_ENDSESSION = 0x0016;

            if (m.Msg == WM_QUERYENDSESSION || m.Msg == WM_ENDSESSION)

            {

                // 系统关机/注销或重启拦截：毫秒级同步下发指令将硬件恢复至桌面回报率与 DPI，杜绝开机残留 8000Hz 电竞档位

                try

                {

                    RestoreDesktopSettingsSynchronous();

                }

                catch { }

                m.Result = (IntPtr)1;

                return;

            }



            const int WM_ERASEBKGND = 0x0014;

            if (m.Msg == WM_ERASEBKGND)

            {

                // 拦截 WM_ERASEBKGND，返回 1 (已由系统擦除)，移交双缓冲绘制，彻底杜绝白框闪烁

                m.Result = (IntPtr)1;

                return;

            }



            if (m.Msg == NativeMethods.WM_TASKBARCREATED && NativeMethods.WM_TASKBARCREATED != 0)

            {

                ReinitializeTrayIcon();

            }

            else if (m.Msg == Program.WM_SHOWME && Program.WM_SHOWME != 0)

            {

                ShowWindow();

            }

            else if (m.Msg == NativeMethods.WM_SIZING && m.LParam != IntPtr.Zero)
            {
                isUserSizingDrag = true;
                NativeMethods.RECT rc = (NativeMethods.RECT)Marshal.PtrToStructure(m.LParam, typeof(NativeMethods.RECT));
                int edge = m.WParam.ToInt32();

                int frameW = Math.Max(0, this.Width - this.ClientSize.Width);
                int frameH = Math.Max(0, this.Height - this.ClientSize.Height);
                int baseClientW = 486;
                int baseClientH = GetCurrentBaseClientHeight();

                int reqClientW = (rc.Right - rc.Left) - frameW;
                int reqClientH = (rc.Bottom - rc.Top) - frameH;

                float scaleW = (float)reqClientW / (float)baseClientW;
                float scaleH = (float)reqClientH / (float)baseClientH;
                float targetScale;

                if (edge == NativeMethods.WMSZ_LEFT || edge == NativeMethods.WMSZ_RIGHT)
                {
                    targetScale = scaleW;
                }
                else if (edge == NativeMethods.WMSZ_TOP || edge == NativeMethods.WMSZ_BOTTOM)
                {
                    targetScale = scaleH;
                }
                else
                {
                    targetScale = (scaleW + scaleH) * 0.5f;
                }

                Screen scr = Screen.FromHandle(this.Handle);
                targetScale = DpiScaleHelper.ClampScaleToScreen(targetScale, scr, baseClientH + frameH);

                int lockedW = (int)Math.Round(baseClientW * targetScale) + frameW;
                int lockedH = (int)Math.Round(baseClientH * targetScale) + frameH;

                if (edge == NativeMethods.WMSZ_LEFT || edge == NativeMethods.WMSZ_TOPLEFT || edge == NativeMethods.WMSZ_BOTTOMLEFT)
                {
                    rc.Left = rc.Right - lockedW;
                }
                else
                {
                    rc.Right = rc.Left + lockedW;
                }

                if (edge == NativeMethods.WMSZ_TOP || edge == NativeMethods.WMSZ_TOPLEFT || edge == NativeMethods.WMSZ_TOPRIGHT)
                {
                    rc.Top = rc.Bottom - lockedH;
                }
                else
                {
                    rc.Bottom = rc.Top + lockedH;
                }

                Marshal.StructureToPtr(rc, m.LParam, false);
                pendingDragScale = targetScale;
                int nowTick = Environment.TickCount;

                if (Math.Abs(targetScale - this.dpiScale) >= 0.006f)
                {
                    ShowZoomTitleHint(targetScale);
                    if (sizingDragTimer == null)
                    {
                        sizingDragTimer = new System.Windows.Forms.Timer();
                        sizingDragTimer.Interval = 45;
                        sizingDragTimer.Tick += (s, ev) =>
                        {
                            sizingDragTimer.Stop();
                            if (isUserSizingDrag && pendingDragScale >= DpiScaleHelper.MinScale && Math.Abs(pendingDragScale - this.dpiScale) >= 0.006f)
                            {
                                SetUserWindowScale(pendingDragScale, false);
                            }
                        };
                    }
                    sizingDragTimer.Stop();
                    sizingDragTimer.Start();
                }
                lastSizingLayoutTick = nowTick;
                {
                    lastSizingLayoutTick = nowTick;
                {
                }
                }

                m.Result = (IntPtr)1;
                return;
            }
            else if (m.Msg == NativeMethods.WM_EXITSIZEMOVE)
            {
                if (isUserSizingDrag)
                {
                    if (sizingDragTimer != null) sizingDragTimer.Stop();
                    isUserSizingDrag = false;
                    float finalScale = pendingDragScale >= DpiScaleHelper.MinScale ? pendingDragScale : this.dpiScale;
                    pendingDragScale = 0f;
                    SetUserWindowScale(finalScale, true);
                }
            }
            else if (m.Msg == 0x020A && (Control.ModifierKeys & Keys.Control) == Keys.Control)
            {
                short wheelDelta = (short)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                if (wheelDelta != 0)
                {
                    StepWindowScale(wheelDelta > 0 ? 0.05f : -0.05f);
                    m.Result = IntPtr.Zero;
                    return;
                }
            }
            else if (m.Msg == NativeMethods.WM_DPICHANGED)

            {

                int newDpiX = m.WParam.ToInt32() & 0xFFFF;

                float newScale = newDpiX / 96.0f;

                if (newScale < 1.0f) newScale = 1.0f;



                NativeMethods.RECT suggestedRect = (NativeMethods.RECT)Marshal.PtrToStructure(m.LParam, typeof(NativeMethods.RECT));

                Rectangle suggestedBounds = Rectangle.FromLTRB(suggestedRect.Left, suggestedRect.Top, suggestedRect.Right, suggestedRect.Bottom);



                HandleDpiChanged(newScale, suggestedBounds);

                m.Result = IntPtr.Zero;

                return;

            }

            else if (m.Msg == WM_INPUT)

            {

                HandleMouseWakeInput(m.LParam);

            }

            else if (m.Msg == WM_DEVICECHANGE)

            {

                int wp = m.WParam.ToInt32();

                if (wp == DBT_DEVICEARRIVAL || wp == DBT_DEVICEREMOVECOMPLETE || wp == DBT_DEVNODES_CHANGED)

                {

                    InvalidateMouseWakeCache();

                    OnDeviceHardwareChange();

                }

            }

            base.WndProc(ref m);

        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if ((Control.ModifierKeys & Keys.Control) == Keys.Control && e.Delta != 0)
            {
                StepWindowScale(e.Delta > 0 ? 0.05f : -0.05f);
                return;
            }
            base.OnMouseWheel(e);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (keyData == (Keys.Control | Keys.D0) || keyData == (Keys.Control | Keys.NumPad0))
            {
                ResetWindowScale();
                return true;
            }
            if (keyData == (Keys.Control | Keys.Oemplus) || keyData == (Keys.Control | Keys.Add))
            {
                StepWindowScale(0.05f);
                return true;
            }
            if (keyData == (Keys.Control | Keys.OemMinus) || keyData == (Keys.Control | Keys.Subtract))
            {
                StepWindowScale(-0.05f);
                return true;
            }
            return base.ProcessCmdKey(ref msg, keyData);
        }



        private IntPtr lastWakeDetectorHwnd = IntPtr.Zero;
        private bool lastWakeDetectorEnabled = false;

        private bool HasAnySleepingMouse()
        {
            if (lastInfo != null && lastInfo.Category == DeviceCategory.Mouse && lastInfo.IsSleeping) return true;
            if (selectedDevice != null && selectedDevice.Category == DeviceCategory.Mouse && selectedDevice.IsSleeping) return true;
            if (activeDevices != null)
            {
                for (int i = 0; i < activeDevices.Count; i++)
                {
                    var d = activeDevices[i];
                    if (d != null && d.Category == DeviceCategory.Mouse && d.IsSleeping) return true;
                }
            }
            return false;
        }

        internal bool ShouldMonitorMouseWakeInput()
        {
            if (HasAnySleepingMouse()) return true;
            if (lastInfo != null && lastInfo.Category == DeviceCategory.Mouse && !lastInfo.IsConnected) return true;
            if (selectedDevice != null && selectedDevice.Category == DeviceCategory.Mouse && !selectedDevice.IsConnected) return true;
            if (activeDevices != null)
            {
                for (int i = 0; i < activeDevices.Count; i++)
                {
                    var d = activeDevices[i];
                    if (d != null && d.Category == DeviceCategory.Mouse && !d.IsConnected) return true;
                }
            }
            return false;
        }

        internal void InvalidateMouseWakeCache()
        {
            System.Threading.Interlocked.Increment(ref mouseWakeCacheVersion);
            System.Threading.Interlocked.Exchange(ref lastWakeProcessTicks, 0);
            lock (mouseDeviceWakeMatchCache)
            {
                mouseDeviceWakeMatchCache.Clear();
                mouseDevicePathCache.Clear();
            }
        }

        internal void SyncMouseWakeCacheForCandidates()
        {
            var candidates = CollectWakeCandidateMice();
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < candidates.Count; i++)
            {
                if (i > 0) sb.Append('|');
                sb.Append(candidates[i].DeviceId ?? "");
            }
            string cacheKey = sb.ToString();
            int curVer = System.Threading.Interlocked.CompareExchange(ref mouseWakeCacheVersion, 0, 0);
            if (!string.Equals(mouseWakeCacheDeviceId, cacheKey, StringComparison.OrdinalIgnoreCase) || appliedMouseWakeCacheVersion != curVer)
            {
                InvalidateMouseWakeCache();
                mouseWakeCacheDeviceId = cacheKey;
                appliedMouseWakeCacheVersion = System.Threading.Interlocked.CompareExchange(ref mouseWakeCacheVersion, 0, 0);
            }
        }

        private void EnableMouseWakeDetector(bool enable)
        {
            if (HardwareCommandBus.IsHardwareSandboxActive) return;
            try
            {
                if (this.IsDisposed) return;
                if (this.InvokeRequired)
                {
                    try
                    {
                        if (this.IsHandleCreated && !this.IsDisposed)
                        {
                            this.BeginInvoke(new Action(() => EnableMouseWakeDetector(enable)));
                        }
                    }
                    catch { }
                    return;
                }

                bool shouldStayEnabled = isDpiMonitorRunning && dpiOsdEnabled;
                bool effectiveEnable = enable || shouldStayEnabled || ShouldMonitorMouseWakeInput();
                IntPtr curHwnd = effectiveEnable ? this.Handle : IntPtr.Zero;
                if (effectiveEnable == lastWakeDetectorEnabled && curHwnd == lastWakeDetectorHwnd)
                {
                    return;
                }

                RAWINPUTDEVICE[] rid = new RAWINPUTDEVICE[1];
                rid[0].usUsagePage = HID_USAGE_PAGE_GENERIC;
                rid[0].usUsage = HID_USAGE_GENERIC_MOUSE;
                if (effectiveEnable)
                {
                    rid[0].dwFlags = RIDEV_INPUTSINK;
                    rid[0].hwndTarget = curHwnd;
                }
                else
                {
                    rid[0].dwFlags = NativeMethods.RIDEV_REMOVE;
                    rid[0].hwndTarget = IntPtr.Zero;
                }

                if (RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE))))
                {
                    lastWakeDetectorEnabled = effectiveEnable;
                    lastWakeDetectorHwnd = curHwnd;
                }
            }
            catch { }
        }



        private void UnregisterMouseWakeDetector()

        {

            EnableMouseWakeDetector(false);

        }



        private static bool IsLogitechReceiverPid(int pid)
        {
            return pid == 0xC52B || pid == 0xC534 || pid == 0xC539 || pid == 0xC53A ||
                   pid == 0xC53F || pid == 0xC541 || pid == 0xC545 || pid == 0xC547 ||
                   pid == 0xC548 || pid == 0xC54B || pid == 0xC54D || pid == 0xC54F;
        }

        private static bool IsRapooGenericReceiverPid(int pid)
        {
            return pid == 0x14FF || pid == 0x1500 || pid == 0x1501;
        }

        private static bool DoesMousePidMatch(int devVid, int devPid, int pathVid, int pathPid)
        {
            if (devVid != pathVid) return false;
            if (devPid == pathPid) return true;

            if (devVid == 0x1532 &&
                RazerDeviceHelper.NormalizeRazerPid(devPid) == RazerDeviceHelper.NormalizeRazerPid(pathPid))
            {
                return true;
            }

            if (devVid == 0x046D)
            {
                // 当设备记录为 paired PID (0x40xx / 0xB0xx) 而 RawInput 路径上报接收器 PID (0xC5xx) 时允许匹配，
                // 但两个不同的接收器 PID (例如 GPW1 0xC539 与 GPW2 0xC53F) 依然严格隔离。
                bool devIsReceiver = IsLogitechReceiverPid(devPid);
                bool pathIsReceiver = IsLogitechReceiverPid(pathPid);
                if (devIsReceiver != pathIsReceiver)
                {
                    bool otherIsLogiMouse = ((devIsReceiver ? pathPid : devPid) & 0xF000) == 0x4000 ||
                                            ((devIsReceiver ? pathPid : devPid) & 0xF000) == 0xB000 ||
                                            ((devIsReceiver ? pathPid : devPid) & 0xF000) == 0xC000;
                    if (otherIsLogiMouse) return true;
                }
            }

            if (devVid == 0x24AE && (IsRapooGenericReceiverPid(devPid) || IsRapooGenericReceiverPid(pathPid)))
            {
                return true;
            }

            if (devVid == 0x3837 &&
                (MchoseMouse4DHelper.IsSupportedPid(devPid) || MchoseMouse4DHelper.IsSupportedBlePid(devPid)) &&
                (MchoseMouse4DHelper.IsSupportedPid(pathPid) || MchoseMouse4DHelper.IsSupportedBlePid(pathPid)))
            {
                return true;
            }

            if (devVid == 0x3434 && (devPid == 0xD026 || devPid == 0xD060) && (pathPid == 0xD026 || pathPid == 0xD060))
            {
                return true;
            }

            return false;
        }

        private static bool IsTargetMouseHardwarePath(string path, MouseBatteryInfo targetMouse)
        {
            if (string.IsNullOrEmpty(path)) return false;

            // Immediately ignore laptop touchpads, trackpoints and motherboard devices
            if (path.IndexOf("SYNA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("ELAN", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("ACPI", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("HIDI2C", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            if (targetMouse == null) return false;

            string brand = targetMouse.Brand ?? "";
            string devId = targetMouse.DeviceId ?? "";

            // 精确到型号：同时检查 DeviceId / HardwareId / HardwareFingerprint 中的 VID/PID 候选，
            // 防止「设备卡片 ID 为配对模型 PID (如 40A9) 而 RawInput 路径为接收器 PID (如 C54D)」被误杀，
            // 同时绝不允许不同厂商或不同接收器跨设备唤醒。
            int pathVid, pathPid;
            bool hasPathIds = HardwareDriverBase.TryExtractVidPidFromString(path, out pathVid, out pathPid);
            if (hasPathIds)
            {
                bool hadAnyDevIds = false;
                string[] idSources = new string[] { devId, targetMouse.HardwareId, targetMouse.HardwareFingerprint };
                for (int si = 0; si < idSources.Length; si++)
                {
                    int devVid, devPid;
                    if (HardwareDriverBase.TryExtractVidPidFromString(idSources[si], out devVid, out devPid))
                    {
                        hadAnyDevIds = true;
                        if (DoesMousePidMatch(devVid, devPid, pathVid, pathPid))
                        {
                            return true;
                        }
                    }
                }
                if (hadAnyDevIds) return false;
            }

            if (brand.Equals("Razer", StringComparison.OrdinalIgnoreCase) || devId.IndexOf("1532", StringComparison.OrdinalIgnoreCase) >= 0)

            {

                if (path.IndexOf("VID_1532", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            }

            else if (brand.Equals("Logitech", StringComparison.OrdinalIgnoreCase) || devId.IndexOf("046D", StringComparison.OrdinalIgnoreCase) >= 0)

            {

                if (devId.StartsWith("Logitech:", StringComparison.OrdinalIgnoreCase))

                {

                    string[] lgParts = devId.Split(':');

                    if (lgParts.Length >= 3 && lgParts[1].Length == 4 && lgParts[2].Length == 4)

                    {

                        string vidTok = "VID_" + lgParts[1];

                        string pidTok = "PID_" + lgParts[2];

                        return path.IndexOf(vidTok, StringComparison.OrdinalIgnoreCase) >= 0 &&

                               path.IndexOf(pidTok, StringComparison.OrdinalIgnoreCase) >= 0;

                    }

                }

                if (path.IndexOf("VID_046D", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            }

            else if (brand.Equals("Rapoo", StringComparison.OrdinalIgnoreCase) || devId.IndexOf("24AE", StringComparison.OrdinalIgnoreCase) >= 0)

            {

                if (path.IndexOf("VID_24AE", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            }

            else if (brand.Equals("VGN", StringComparison.OrdinalIgnoreCase) || brand.Equals("ATK", StringComparison.OrdinalIgnoreCase) || devId.IndexOf("3554", StringComparison.OrdinalIgnoreCase) >= 0)

            {

                if (path.IndexOf("VID_3554", StringComparison.OrdinalIgnoreCase) >= 0 || path.IndexOf("VID_258A", StringComparison.OrdinalIgnoreCase) >= 0) return true;

            }

            else if (devId.Contains(":"))

            {

                string[] parts = devId.Split(':');

                for (int pi = 0; pi < parts.Length - 1; pi++)

                {

                    if (parts[pi].Length == 4 && parts[pi + 1].Length == 4)

                    {

                        string vidToken = "VID_" + parts[pi];

                        if (path.IndexOf(vidToken, StringComparison.OrdinalIgnoreCase) >= 0) return true;

                    }

                }

            }



            // Fallback match: if DeviceId or HardwareId has matching PID, or registered in HardwareDriverRegistry

            if (HardwareDriverRegistry.IsKnownMouseHardwarePath(path)) return true;



            string devPathOrId = targetMouse.DeviceId ?? targetMouse.HardwareId ?? "";

            if (!string.IsNullOrEmpty(devPathOrId))

            {

                int pidIdx = devPathOrId.IndexOf("PID_", StringComparison.OrdinalIgnoreCase);

                if (pidIdx >= 0 && devPathOrId.Length >= pidIdx + 8)

                {

                    string pidToken = devPathOrId.Substring(pidIdx, 8);

                    if (path.IndexOf(pidToken, StringComparison.OrdinalIgnoreCase) >= 0) return true;

                }

            }



            return false;

        }



        private void QueuePassiveWakeRefresh(int delayMs)
        {
            if (isPassiveWakePollingQueued) return;
            isPassiveWakePollingQueued = true;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                Thread.Sleep(delayMs);
                isPassiveWakePollingQueued = false;
                try
                {
                    this.BeginInvoke(new Action(() => RefreshBatteryStatus(false)));
                }
                catch { }
            });
        }

        private static bool AreSameWakeMouse(MouseBatteryInfo a, MouseBatteryInfo b)
        {
            if (a == null || b == null) return false;
            if (object.ReferenceEquals(a, b)) return true;
            if (!string.IsNullOrEmpty(a.DeviceId) && !string.IsNullOrEmpty(b.DeviceId))
            {
                if (string.Equals(a.DeviceId, b.DeviceId, StringComparison.OrdinalIgnoreCase)) return true;
                if (string.Equals(RazerDeviceHelper.NormalizeRazerDeviceId(a.DeviceId), RazerDeviceHelper.NormalizeRazerDeviceId(b.DeviceId), StringComparison.OrdinalIgnoreCase)) return true;
            }
            if (!string.IsNullOrEmpty(a.PhysicalUnitId) && !string.IsNullOrEmpty(b.PhysicalUnitId) &&
                string.Equals(a.PhysicalUnitId, b.PhysicalUnitId, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
            return false;
        }

        private List<MouseBatteryInfo> CollectWakeCandidateMice()
        {
            var candidates = new List<MouseBatteryInfo>();
            var pool = new List<MouseBatteryInfo>();
            if (lastInfo != null) pool.Add(lastInfo);
            if (selectedDevice != null) pool.Add(selectedDevice);
            if (activeDevices != null) pool.AddRange(activeDevices);

            for (int i = 0; i < pool.Count; i++)
            {
                var d = pool[i];
                if (d == null || d.Category != DeviceCategory.Mouse) continue;
                bool dup = false;
                for (int j = 0; j < candidates.Count; j++)
                {
                    if (AreSameWakeMouse(candidates[j], d)) { dup = true; break; }
                }
                if (!dup) candidates.Add(d);
            }
            return candidates;
        }

        private string lastWakeMouseDeviceId = null;

        private long lastWakeProcessTicks = 0;

        // 原始输入 (WM_INPUT) 重入闸门：8K 回报率鼠标每秒可产生数千个报文，
        // 处理链内一旦触发嵌套消息派发必须立即退出，避免同一次输入被反复重入。
        private bool isHandlingMouseWakeInput = false;

        // 唤醒后的界面重建合并位：0 = 空闲，1 = 已排队。
        // 唤醒瞬间只允许存在一个待执行的 UpdateUI，后续报文全部折叠进这一次刷新。
        private int pendingWakeUiRefresh = 0;
        private MouseBatteryInfo pendingWakeUiTarget = null;

        /// <summary>
        /// 记录目标鼠标最近一次活动时间戳，并顺带唤醒休眠中的 DPI 监听线程。
        /// 仅包含 O(1) 内存操作，可安全地在 8K WM_INPUT 热路径执行。
        /// </summary>
        private void MarkTargetMouseActivity()
        {
            System.Threading.Interlocked.Exchange(ref lastTargetMouseActivityTicks, DateTime.UtcNow.Ticks);
            if (isDpiMonitorSleeping)
            {
                isDpiMonitorSleeping = false;
                try
                {
                    if (dpiMonitorWakeEvent != null) dpiMonitorWakeEvent.Set();
                }
                catch { }
            }
        }

        /// <summary>
        /// 零分配的廉价闸门：整个桌面没有任何休眠/离线鼠标候选时，WM_INPUT 直接返回。
        /// 在线且清醒时每次报文只做若干字段判读，绝不进入 P/Invoke、字符串匹配与硬件查询。
        /// </summary>
        private bool HasSleepingOrOfflineMouseCandidate()
        {
            if (lastInfo != null && lastInfo.Category == DeviceCategory.Mouse && (lastInfo.IsSleeping || !lastInfo.IsConnected)) return true;
            if (selectedDevice != null && selectedDevice.Category == DeviceCategory.Mouse && (selectedDevice.IsSleeping || !selectedDevice.IsConnected)) return true;
            if (activeDevices != null)
            {
                for (int i = 0; i < activeDevices.Count; i++)
                {
                    var d = activeDevices[i];
                    if (d != null && d.Category == DeviceCategory.Mouse && (d.IsSleeping || !d.IsConnected)) return true;
                }
            }
            return false;
        }

        /// <summary>
        /// 该原始输入设备路径是否真的存在待唤醒目标。
        /// 返回 false 表示目标鼠标已在线且清醒——此时只刷新活动时间戳，绝不触发界面重建。
        /// </summary>
        private bool IsWakeNeededForPath(string devPath, List<MouseBatteryInfo> candidates)
        {
            if (string.IsNullOrEmpty(devPath) || candidates == null) return false;
            for (int i = 0; i < candidates.Count; i++)
            {
                var c = candidates[i];
                if (c == null || c.Category != DeviceCategory.Mouse) continue;
                if (!c.IsSleeping && c.IsConnected) continue;
                if (IsTargetMouseHardwarePath(devPath, c)) return true;
            }
            return false;
        }

        /// <summary>
        /// 把唤醒后的全量界面重建合并成一次异步刷新。
        /// UpdateUI 会执行较多控件与状态更新，绝不能在 WM_INPUT 回调里按报文频次同步执行。
        /// </summary>
        private void QueueWakeUiRefresh(MouseBatteryInfo target)
        {
            if (target == null) return;
            pendingWakeUiTarget = target;
            if (System.Threading.Interlocked.CompareExchange(ref pendingWakeUiRefresh, 1, 0) != 0) return;
            try
            {
                this.BeginInvoke(new Action(delegate
                {
                    System.Threading.Interlocked.Exchange(ref pendingWakeUiRefresh, 0);
                    MouseBatteryInfo t = pendingWakeUiTarget;
                    if (t == null || this.IsDisposed) return;
                    try { UpdateUI(t, false); } catch { }
                }));
            }
            catch
            {
                System.Threading.Interlocked.Exchange(ref pendingWakeUiRefresh, 0);
            }
        }

        internal bool TryProcessMouseWakePath(string devPath)
        {
            if (string.IsNullOrEmpty(devPath)) return false;

            var candidates = CollectWakeCandidateMice();
            if (candidates.Count == 0) return false;

            bool anyWoken = false;
            bool anyPrimaryWoken = false;
            bool anySelectedWoken = false;
            bool anyNeededFastRefresh = false;
            MouseBatteryInfo primaryToRender = null;
            MouseBatteryInfo selectedToRender = null;

            for (int ci = 0; ci < candidates.Count; ci++)
            {
                var cand = candidates[ci];
                if (!IsTargetMouseHardwarePath(devPath, cand)) continue;

                System.Threading.Interlocked.Exchange(ref lastTargetMouseActivityTicks, DateTime.UtcNow.Ticks);
                lastWakeMouseDeviceId = cand.DeviceId;

                bool isPrimaryCand = cand.IsPrimary || (lastInfo != null && AreSameWakeMouse(cand, lastInfo));
                bool wasOnline = cand.IsConnected;

                // 【唤醒判定铁律】必须以「该物理鼠标自身的全部镜像」为唯一依据，
                // 严禁用窗体级聚合标记 isMouseSleeping 代表当前设备，避免副鼠标休眠时引发 8K 伪唤醒风暴。
                var mirrors = new List<MouseBatteryInfo>();
                if (lastInfo != null) mirrors.Add(lastInfo);
                if (selectedDevice != null) mirrors.Add(selectedDevice);
                if (activeDevices != null) mirrors.AddRange(activeDevices);

                bool anyMirrorSleeping = cand.IsSleeping;
                if (!anyMirrorSleeping)
                {
                    for (int mi = 0; mi < mirrors.Count; mi++)
                    {
                        var m = mirrors[mi];
                        if (m != null && m.IsSleeping && AreSameWakeMouse(m, cand)) { anyMirrorSleeping = true; break; }
                    }
                }

                bool wasSleeping = anyMirrorSleeping || (!wasOnline && cand.IsDonglePresent);

                if (wasSleeping || !wasOnline)
                {
                    // 同步清零同设备在 lastInfo / selectedDevice / activeDevices 上的所有副本，避免切换窗口或状态栏刷新时读到旧睡眠态
                    for (int mi = 0; mi < mirrors.Count; mi++)
                    {
                        var m = mirrors[mi];
                        if (m != null && AreSameWakeMouse(m, cand))
                        {
                            m.IsSleeping = false;
                            if (!m.IsConnected && m.IsDonglePresent) m.IsConnected = true;
                        }
                    }
                    cand.IsSleeping = false;
                    if (!cand.IsConnected && cand.IsDonglePresent) cand.IsConnected = true;

                    if (isPrimaryCand)
                    {
                        anyPrimaryWoken = true;
                        primaryToRender = lastInfo ?? cand;
                    }
                    if (selectedDevice != null && AreSameWakeMouse(cand, selectedDevice))
                    {
                        anySelectedWoken = true;
                        selectedToRender = selectedDevice;
                    }

                    anyWoken = true;
                    if (!wasOnline) anyNeededFastRefresh = true;

                    if (!HardwareCommandBus.IsHardwareSandboxActive)
                    {
                        PerformInstantDpiCalibration(cand);
                    }
                }
            }

            if (isDpiMonitorSleeping && anyWoken)
            {
                isDpiMonitorSleeping = false;
                try { dpiMonitorWakeEvent.Set(); } catch { }
            }

            if (!anyWoken) return false;

            isMouseSleeping = HasAnySleepingMouse();
            EnableMouseWakeDetector(ShouldMonitorMouseWakeInput());

            if (anyPrimaryWoken && primaryToRender != null)
            {
                UpdateStatusDisplay();
                UpdateTrayIcon(primaryToRender.BatteryPercent, primaryToRender.IsCharging, primaryToRender.IsConnected, false, primaryToRender.TrayAccentColor);
            }
            else if (multiTrayEnabled)
            {
                RefreshMultiTrayHost();
            }
            if (anySelectedWoken && selectedToRender != null && !this.IsDisposed)
            {
                // 合并异步刷新：唤醒瞬间只排队一次全量界面重建，杜绝按报文频次同步重建
                QueueWakeUiRefresh(selectedToRender);
            }

            if (!HardwareCommandBus.IsHardwareSandboxActive)
            {
                QueuePassiveWakeRefresh(anyNeededFastRefresh ? 120 : 800);
            }

            return true;
        }

        internal bool TryHandleCachedMouseRawDevice(IntPtr hDev, Func<string> resolveDevicePath, out bool didResolvePath, out bool didRunWakeProcess)
        {
            didResolvePath = false;
            didRunWakeProcess = false;
            if (hDev == IntPtr.Zero) return false;

            string cachedPath = null;
            bool anyTargetMatched = false;
            bool found = false;
            lock (mouseDeviceWakeMatchCache)
            {
                found = mouseDeviceWakeMatchCache.TryGetValue(hDev, out anyTargetMatched);
                if (found)
                {
                    mouseDevicePathCache.TryGetValue(hDev, out cachedPath);
                }
            }

            if (!found)
            {
                didResolvePath = true;
                cachedPath = (resolveDevicePath != null) ? (resolveDevicePath() ?? "") : "";
                var candidates = CollectWakeCandidateMice();
                if (!string.IsNullOrEmpty(cachedPath) && candidates.Count > 0)
                {
                    for (int ci = 0; ci < candidates.Count; ci++)
                    {
                        if (IsTargetMouseHardwarePath(cachedPath, candidates[ci]))
                        {
                            anyTargetMatched = true;
                            break;
                        }
                    }
                }
                lock (mouseDeviceWakeMatchCache)
                {
                    mouseDeviceWakeMatchCache[hDev] = anyTargetMatched;
                    mouseDevicePathCache[hDev] = cachedPath ?? "";
                }
            }

            if (!anyTargetMatched) return false;

            MarkTargetMouseActivity();

            bool needsWakeCheck = HasSleepingOrOfflineMouseCandidate();
            if (needsWakeCheck && !string.IsNullOrEmpty(cachedPath))
            {
                long nowTicks = DateTime.UtcNow.Ticks;
                long prevWakeTicks = System.Threading.Interlocked.Read(ref lastWakeProcessTicks);
                if (nowTicks - prevWakeTicks >= TimeSpan.FromMilliseconds(200).Ticks)
                {
                    System.Threading.Interlocked.Exchange(ref lastWakeProcessTicks, nowTicks);
                    var wakeCandidates = CollectWakeCandidateMice();
                    if (IsWakeNeededForPath(cachedPath, wakeCandidates))
                    {
                        didRunWakeProcess = TryProcessMouseWakePath(cachedPath);
                    }
                }
            }
            return true;
        }

        private string ResolveRawInputDeviceName(IntPtr hDev)
        {
            uint len = 0;
            GetRawInputDeviceInfoW(hDev, RIDI_DEVICENAME, null, ref len);
            if (len > 0)
            {
                StringBuilder sb = new StringBuilder((int)len);
                if (GetRawInputDeviceInfoW(hDev, RIDI_DEVICENAME, sb, ref len) > 0)
                {
                    return sb.ToString();
                }
            }
            return "";
        }

        private void HandleMouseWakeInput(IntPtr hRawInput)
        {
            if (isHandlingMouseWakeInput) return;
            isHandlingMouseWakeInput = true;
            try
            {
                // 零分配廉价闸门：没有任何休眠/离线鼠标候选时直接跳过 GetRawInputData 与字符串匹配
                if (!HasSleepingOrOfflineMouseCandidate())
                {
                    MarkTargetMouseActivity();
                    return;
                }

                int headerSize = Marshal.SizeOf(typeof(RAWINPUTHEADER));
                RAWINPUTHEADER header;
                if (GetRawInputData(hRawInput, RID_HEADER, out header, ref headerSize, headerSize) < 0) return;
                if (header.dwType != 0 || header.hDevice == IntPtr.Zero) return; // RIM_TYPEMOUSE only

                IntPtr hDev = header.hDevice;
                bool anyTargetMatched = false;
                bool found = false;
                lock (mouseDeviceWakeMatchCache)
                {
                    found = mouseDeviceWakeMatchCache.TryGetValue(hDev, out anyTargetMatched);
                }
                if (found)
                {
                    if (!anyTargetMatched) return;
                    long nowTicks = DateTime.UtcNow.Ticks;
                    long prevWakeTicks = System.Threading.Interlocked.Read(ref lastWakeProcessTicks);
                    if (nowTicks - prevWakeTicks < TimeSpan.FromMilliseconds(200).Ticks)
                    {
                        MarkTargetMouseActivity();
                        return; // 零堆分配极速热路径：即使另有其它离线鼠标，也按 200ms 节流，绝不在 8K WM_INPUT 内构造闭包委托
                    }
                }

                bool didResolvePath;
                bool didRunWakeProcess;
                TryHandleCachedMouseRawDevice(hDev, () => ResolveRawInputDeviceName(hDev), out didResolvePath, out didRunWakeProcess);
            }
            catch { }
            finally
            {
                isHandlingMouseWakeInput = false;
            }
        }



        private void RegisterUsbNotification()

        {

            try

            {

                if (hDevNotify != IntPtr.Zero)

                {

                    try { UnregisterDeviceNotification(hDevNotify); } catch { }

                    hDevNotify = IntPtr.Zero;

                }

                Guid hidGuid;

                RazerDeviceHelper.HidD_GetHidGuid(out hidGuid);



                DEV_BROADCAST_DEVICEINTERFACE dbi = new DEV_BROADCAST_DEVICEINTERFACE();

                dbi.dbcc_size = Marshal.SizeOf(dbi);

                dbi.dbcc_devicetype = DBT_DEVTYP_DEVICEINTERFACE;

                dbi.dbcc_reserved = 0;

                dbi.dbcc_classguid = hidGuid;



                IntPtr buffer = Marshal.AllocHGlobal(dbi.dbcc_size);

                try

                {

                    Marshal.StructureToPtr(dbi, buffer, true);

                    hDevNotify = RegisterDeviceNotification(this.Handle, buffer, DEVICE_NOTIFY_WINDOW_HANDLE | NativeMethods.DEVICE_NOTIFY_ALL_INTERFACE_CLASSES);

                }

                finally

                {

                    Marshal.FreeHGlobal(buffer);

                }

            }

            catch { }

        }



        private void OnDeviceHardwareChange()

        {

            InvalidateMouseWakeCache();

            NuphyDeviceHelper.CachedCommandPath = null;

            if (deviceChangeTimer1 == null)

            {

                deviceChangeTimer1 = new System.Windows.Forms.Timer();

                deviceChangeTimer1.Tick += (s, e) =>

                {

                    deviceChangeTimer1.Stop();

                    RefreshBatteryStatus(false);

                };

            }

            deviceChangeTimer1.Stop();

            deviceChangeTimer1.Interval = 250;

            deviceChangeTimer1.Start();



            if (deviceChangeTimer2 == null)

            {

                deviceChangeTimer2 = new System.Windows.Forms.Timer();

                deviceChangeTimer2.Tick += (s, e) =>

                {

                    deviceChangeTimer2.Stop();

                    RefreshBatteryStatus(false);

                };

            }

            deviceChangeTimer2.Stop();

            deviceChangeTimer2.Interval = 1000;

            deviceChangeTimer2.Start();

        }



        private void ApplyModernWin11Theme()

        {

            ModernThemeHelper.ApplyModernWin11Theme(this.Handle);

        }



    }

}

