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

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_TASKBARCREATED && NativeMethods.WM_TASKBARCREATED != 0)
            {
                ReinitializeTrayIcon();
            }
            else if (m.Msg == Program.WM_SHOWME && Program.WM_SHOWME != 0)
            {
                ShowWindow();
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
                    lock (mouseDeviceWakeMatchCache)
                    {
                        mouseDeviceWakeMatchCache.Clear();
                    }
                    OnDeviceHardwareChange();
                }
            }
            base.WndProc(ref m);
        }

        private void EnableMouseWakeDetector(bool enable)
        {
            try
            {
                RAWINPUTDEVICE[] rid = new RAWINPUTDEVICE[1];
                rid[0].usUsagePage = HID_USAGE_PAGE_GENERIC;
                rid[0].usUsage = HID_USAGE_GENERIC_MOUSE;
                if (enable)
                {
                    rid[0].dwFlags = RIDEV_INPUTSINK;
                    rid[0].hwndTarget = this.Handle;
                }
                else
                {
                    rid[0].dwFlags = NativeMethods.RIDEV_REMOVE;
                    rid[0].hwndTarget = IntPtr.Zero;
                }
                RegisterRawInputDevices(rid, 1, (uint)Marshal.SizeOf(typeof(RAWINPUTDEVICE)));
            }
            catch { }
        }

        private void UnregisterMouseWakeDetector()
        {
            EnableMouseWakeDetector(false);
        }

        private static bool IsTargetMouseHardwarePath(string path, MouseBatteryInfo targetMouse)
        {
            if (string.IsNullOrEmpty(path)) return false;

            // Immediately ignore laptop touchpads, trackpoints and motherboard devices
            if (path.IndexOf("SYNA", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("ELAN", StringComparison.OrdinalIgnoreCase) >= 0 ||
                path.IndexOf("ACPI", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                return false;
            }

            if (targetMouse == null) return false;

            // Match by mouse brand or vendor ID
            string brand = targetMouse.Brand ?? "";
            string devId = targetMouse.DeviceId ?? "";

            if (brand.Equals("Razer", StringComparison.OrdinalIgnoreCase) || devId.IndexOf("1532", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                if (path.IndexOf("VID_1532", StringComparison.OrdinalIgnoreCase) >= 0) return true;
            }
            else if (brand.Equals("Logitech", StringComparison.OrdinalIgnoreCase) || devId.IndexOf("046D", StringComparison.OrdinalIgnoreCase) >= 0)
            {
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

            // Fallback match: if DeviceId or HardwareId has matching PID
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

        private void HandleMouseWakeInput(IntPtr hRawInput)
        {
            try
            {
                var cur = lastInfo;
                if (cur == null || !cur.IsConnected || !cur.IsSleeping) return;

                int headerSize = Marshal.SizeOf(typeof(RAWINPUTHEADER));
                RAWINPUTHEADER header;
                if (GetRawInputData(hRawInput, RID_HEADER, out header, ref headerSize, headerSize) >= 0)
                {
                    if (header.dwType == 0) // RIM_TYPEMOUSE
                    {
                        IntPtr hDev = header.hDevice;
                        if (hDev == IntPtr.Zero) return;

                        bool isMatch;
                        bool found;
                        lock (mouseDeviceWakeMatchCache)
                        {
                            found = mouseDeviceWakeMatchCache.TryGetValue(hDev, out isMatch);
                        }

                        if (!found)
                        {
                            isMatch = false;
                            uint len = 0;
                            GetRawInputDeviceInfoW(hDev, RIDI_DEVICENAME, null, ref len);
                            if (len > 0)
                            {
                                StringBuilder sb = new StringBuilder((int)len);
                                if (GetRawInputDeviceInfoW(hDev, RIDI_DEVICENAME, sb, ref len) > 0)
                                {
                                    string devPath = sb.ToString();
                                    isMatch = IsTargetMouseHardwarePath(devPath, cur);
                                }
                            }
                            lock (mouseDeviceWakeMatchCache)
                            {
                                mouseDeviceWakeMatchCache[hDev] = isMatch;
                            }
                        }

                        if (isMatch)
                        {
                            cur.IsSleeping = false;
                            isMouseSleeping = false;
                            EnableMouseWakeDetector(false);
                            UpdateTrayIcon(cur.BatteryPercent, cur.IsCharging, cur.IsConnected, false);
                            try { this.BeginInvoke(new Action(() => UpdateUI(cur, false))); } catch { }

                            if (!isPassiveWakePollingQueued)
                            {
                                isPassiveWakePollingQueued = true;
                                ThreadPool.QueueUserWorkItem(_ =>
                                {
                                    Thread.Sleep(800);
                                    isPassiveWakePollingQueued = false;
                                    try
                                    {
                                        this.BeginInvoke(new Action(() => RefreshBatteryStatus(false)));
                                    }
                                    catch { }
                                });
                            }
                        }
                    }
                }
            }
            catch { }
        }

        private void RegisterUsbNotification()
        {
            try
            {
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
            lock (mouseDeviceWakeMatchCache)
            {
                mouseDeviceWakeMatchCache.Clear();
            }
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
