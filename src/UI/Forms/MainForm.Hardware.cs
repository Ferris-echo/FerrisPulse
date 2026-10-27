// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// 1. 目标平台与编译规范：严格维持 C# 5.0 语法标准与 .NET Framework 4.0 兼容性。严禁引入任何第三方包或依赖。
// 2. 编码规范：所有文件严格保存为 UTF-8 带签名（BOM）格式，防止中文字符乱码。
// 3. 架构规范：本文件属于 MainForm 的分部类 (partial class) 之一。修改时请保持职责清晰，任何新增方法或逻辑请同步维护 ARCHITECTURE.md。
// 模块描述: MainForm 硬件轮询调度、DPI/回报率设置下发、电量梯度与 DPI 变化回调
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
        private void StartDpiMonitor()
        {
            if (dpiMonitorThread != null && dpiMonitorThread.IsAlive) return;
            isDpiMonitorRunning = true;
            dpiMonitorThread = new Thread(DpiMonitorWorker);
            dpiMonitorThread.IsBackground = true;
            dpiMonitorThread.Name = "RazerDpiMonitorWorker";
            dpiMonitorThread.Start();
        }

        private void StopDpiMonitor()
        {
            isDpiMonitorRunning = false;
            RapooDeviceHelper.CloseRapooStream();
        }

        // 仅在状态变化时落盘，避免每 50ms 刷日志
        private string lastDpiMonState = null;

        private void LogDpiMonState(string state)
        {
            if (state == lastDpiMonState) return;
            lastDpiMonState = state;
            Diagnostics.Log("DpiMonitor: " + state);
        }

        private void DpiMonitorWorker()
        {
            while (isDpiMonitorRunning)
            {
                try
                {
                    // 1. 空闲暂停：窗口隐藏时停止设备查询，**仅在检测到全屏游戏时保持监听**。
                    //    原因：OSD 开启时原逻辑完全不退避，以 50ms 周期轮询各类设备（VGN/Logitech/
                    //    Rapoo 全走一遍），持续占用 USB 总线 → 无线鼠标永远无法进入休眠（实测 LED 常亮）。
                    //    策略：游戏中（isGamingActive）保留监听以便弹 DPI OSD；非游戏且窗口隐藏时
                    //    完全不碰设备，让鼠标按自身 idle 计时休眠。
                    // 用 isBackgroundMode（含「最小化」）而非只看 Visible：
                    // 点 × 最小化到托盘时 Visible 仍为 true，旧条件会漏判 → DPI 监听继续以 50ms
                    // 轮询设备，导致鼠标无法休眠（实测：主轮询已静默 284s 但 LED 仍常亮）。
                    if (isBackgroundMode && !isGamingActive)
                    {
                        LogDpiMonState("已暂停 bg=1 gaming=0");
                        // 3s 一轮只做状态检查，不触碰任何设备
                        Thread.Sleep(3000);
                        continue;
                    }
                    LogDpiMonState(string.Format("运行中 bg={0} gaming={1}", isBackgroundMode ? 1 : 0, isGamingActive ? 1 : 0));

                    // 2. 收集当前需要监听 DPI 的雷蛇鼠标设备（仅针对鼠标类别，杜绝键盘等其他外设误入）
                    List<string> rzTargets = new List<string>();
                    if (selectedDevice != null && selectedDevice.Brand == "Razer" && selectedDevice.Category == DeviceCategory.Mouse)
                    {
                        rzTargets.Add(selectedDevice.DeviceId);
                    }
                    if (activeDevices != null)
                    {
                        for (int i = 0; i < activeDevices.Count; i++)
                        {
                            var d = activeDevices[i];
                            if (d != null && d.Brand == "Razer" && d.Category == DeviceCategory.Mouse && d.IsConnected && !d.IsSleeping)
                            {
                                if (!rzTargets.Contains(d.DeviceId)) rzTargets.Add(d.DeviceId);
                            }
                        }
                    }
                    if (rzTargets.Count == 0 && (selectedDevice == null || selectedDevice.Category == DeviceCategory.Mouse))
                    {
                        rzTargets.Add(null); // 回退至默认主鼠标设备
                    }

                    for (int rIdx = 0; rIdx < rzTargets.Count; rIdx++)
                    {
                        string devId = rzTargets[rIdx];
                        int dpi, dpiY, stage, count;
                        if (RazerDeviceHelper.FastQueryDpi(out dpi, out dpiY, out stage, out count, devId))
                        {
                            // 严格校验有效性：DPI在常规合法范围且档位在合理合法闭区间
                            int validCount = count > 0 ? count : 5;
                            if (dpi >= 100 && dpi <= 40000 && stage >= 1 && stage <= validCount)
                            {
                                string key = devId ?? "default";
                                int prevDpi = -1;
                                int prevStage = -1;
                                lastMonitoredRazerDpis.TryGetValue(key, out prevDpi);
                                lastMonitoredRazerStages.TryGetValue(key, out prevStage);

                                if (prevDpi != -1 && (dpi != prevDpi || stage != prevStage))
                                {
                                    // 连续两帧一致性确认（防抖）：消除单次瞬态电气或无线报文杂讯误触发
                                    int candDpi = -1;
                                    int candStage = -1;
                                    pendingRazerDpiCandidates.TryGetValue(key, out candDpi);
                                    pendingRazerStageCandidates.TryGetValue(key, out candStage);

                                    if (candDpi == dpi && candStage == stage)
                                    {
                                        // 连续两帧一致，确认为真实档位切换
                                        pendingRazerDpiCandidates.Remove(key);
                                        pendingRazerStageCandidates.Remove(key);

                                        int newDpi = dpi;
                                        int newStage = stage;
                                        int newCount = count;
                                        string notifyDevId = devId;
                                        try
                                        {
                                            this.BeginInvoke(new Action(() => {
                                                OnDpiChanged("Razer", newDpi, newStage, newCount, notifyDevId, dpiY);
                                            }));
                                        }
                                        catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.Hardware.cs: 已忽略异常", diagEx); }

                                        lastMonitoredRazerDpis[key] = dpi;
                                        lastMonitoredRazerStages[key] = stage;
                                        lastMonitoredDpi = dpi;
                                        lastMonitoredStage = stage;
                                    }
                                    else
                                    {
                                        // 记录初次采样候选值，等待下一帧确认
                                        pendingRazerDpiCandidates[key] = dpi;
                                        pendingRazerStageCandidates[key] = stage;
                                    }
                                }
                                else
                                {
                                    // 状态平稳，清理候选值
                                    pendingRazerDpiCandidates.Remove(key);
                                    pendingRazerStageCandidates.Remove(key);

                                    lastMonitoredRazerDpis[key] = dpi;
                                    lastMonitoredRazerStages[key] = stage;
                                    lastMonitoredDpi = dpi;
                                    lastMonitoredStage = stage;
                                }
                            }
                        }
                    }

                    // 3. 雷柏设备极速 DPI 与物理按键/档位/回报率监听（1200ms 舒缓轮询 + 光标移动避让 + 失败时序退避）
                    int rDpi, rDpiY, rStage, rCount;
                    if ((DateTime.Now - lastRapooDpiPollTime).TotalMilliseconds >= 200)
                    {
                        lastRapooDpiPollTime = DateTime.Now;
                        if (RapooDeviceHelper.FastQueryDpi(out rDpi, out rDpiY, out rStage, out rCount))
                        {
                            int validRCount = rCount > 0 ? rCount : 5;
                            if (rDpi >= 50 && rDpi <= 40000 && rStage >= 1 && rStage <= validRCount)
                            {
                                if (lastMonitoredRapooDpi != -1 && (rDpi != lastMonitoredRapooDpi || rStage != lastMonitoredRapooStage))
                                {
                                    int newDpi = rDpi;
                                    int newStage = rStage;
                                    int newCount = rCount;
                                    try
                                    {
                                        this.BeginInvoke(new Action(() => {
                                            OnDpiChanged("Rapoo", newDpi, newStage, newCount, null, rDpiY);
                                        }));
                                    }
                                    catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.Hardware.cs: 已忽略异常", diagEx); }
                                    lastMonitoredRapooDpi = rDpi;
                                    lastMonitoredRapooStage = rStage;
                                }
                                else
                                {
                                    lastMonitoredRapooDpi = rDpi;
                                    lastMonitoredRapooStage = rStage;
                                }
                            }
                        }
                    }

                    if (false) { /* [雷柏MCU保护]: 停用 50ms 高频回报率 EEPROM 轮询，杜绝通信死锁
                    if (RapooDeviceHelper.FastQueryPollingRate(out rRate))
                    {
                        if (lastMonitoredRapooRate != -1 && rRate != lastMonitoredRapooRate)
                        {
                            int newRate = rRate;
                            try
                            {
                                this.BeginInvoke(new Action(() => {
                                    if (selectedDevice != null && (selectedDevice.Brand == "Rapoo" || selectedDevice.Brand == "雷柏"))
                                    {
                                        selectedDevice.PollingRate = newRate;
                                        desktopPollingRate = newRate;
                                        UpdateDesktopRateButtonSelection(newRate);
                                        UpdatePollingRateDisplayStates();
                                        UpdateStatusDisplay();
                                    }
                                }));
                            }
                            catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.Hardware.cs: 已忽略异常", diagEx); }
                        }
                        lastMonitoredRapooRate = -1; */
                    }

                    // 4. 瓦尔基里设备极速 DPI 监听
                    int vDpi, vDpiY, vStage, vCount;
                    if (ValkyrieDeviceHelper.FastQueryDpi(out vDpi, out vDpiY, out vStage, out vCount))
                    {
                        int validVCount = vCount > 0 ? vCount : 5;
                        if (vDpi >= 100 && vDpi <= 40000 && vStage >= 1 && vStage <= validVCount)
                        {
                            if (lastMonitoredValkyrieDpi != -1 && (vDpi != lastMonitoredValkyrieDpi || vStage != lastMonitoredValkyrieStage))
                            {
                                if (pendingValkyrieDpiCandidate == vDpi && pendingValkyrieStageCandidate == vStage)
                                {
                                    pendingValkyrieDpiCandidate = -1;
                                    pendingValkyrieStageCandidate = -1;
                                    int newDpi = vDpi;
                                    int newStage = vStage;
                                    int newCount = vCount;
                                    try
                                    {
                                        this.BeginInvoke(new Action(() => {
                                            OnDpiChanged("Valkyrie", newDpi, newStage, newCount, null, vDpiY);
                                        }));
                                    }
                                    catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.Hardware.cs: 已忽略异常", diagEx); }
                                    lastMonitoredValkyrieDpi = vDpi;
                                    lastMonitoredValkyrieStage = vStage;
                                }
                                else
                                {
                                    pendingValkyrieDpiCandidate = vDpi;
                                    pendingValkyrieStageCandidate = vStage;
                                }
                            }
                            else
                            {
                                pendingValkyrieDpiCandidate = -1;
                                pendingValkyrieStageCandidate = -1;
                                lastMonitoredValkyrieDpi = vDpi;
                                lastMonitoredValkyrieStage = vStage;
                            }
                        }
                    }

                    // 5. 罗技设备极速 DPI 监听
                    int lDpiX, lDpiY, lStage, lCount;
                    if (LogitechDeviceHelper.FastQueryDpi(out lDpiX, out lDpiY, out lStage, out lCount))
                    {
                        int validLCount = lCount > 0 ? lCount : 5;
                        if (lDpiX >= 50 && lDpiX <= 50000 && lStage >= 1 && lStage <= validLCount)
                        {
                            if (lastMonitoredLogitechDpi != -1 && (lDpiX != lastMonitoredLogitechDpi || lStage != lastMonitoredLogitechStage))
                            {
                                if (pendingLogitechDpiCandidate == lDpiX && pendingLogitechStageCandidate == lStage)
                                {
                                    pendingLogitechDpiCandidate = -1;
                                    pendingLogitechStageCandidate = -1;
                                    int newDpi = lDpiX;
                                    int newStage = lStage;
                                    int newCount = lCount;
                                    try
                                    {
                                        this.BeginInvoke(new Action(() => {
                                            OnDpiChanged("Logitech", newDpi, newStage, newCount, null, lDpiY);
                                        }));
                                    }
                                    catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.Hardware.cs: 已忽略异常", diagEx); }
                                    lastMonitoredLogitechDpi = lDpiX;
                                    lastMonitoredLogitechStage = lStage;
                                }
                                else
                                {
                                    pendingLogitechDpiCandidate = lDpiX;
                                    pendingLogitechStageCandidate = lStage;
                                }
                            }
                            else
                            {
                                pendingLogitechDpiCandidate = -1;
                                pendingLogitechStageCandidate = -1;
                                lastMonitoredLogitechDpi = lDpiX;
                                lastMonitoredLogitechStage = lStage;
                            }
                        }
                    }

                    // 6. 皂品/VGN（CompX 方案）设备极速 DPI 档位监听：实体 DPI 键切换后读地址 0x04 同步（300ms 节流，避免与电量查询抢锁）
                    if (selectedDevice != null && (selectedDevice.Brand == "Zaopin" || selectedDevice.Brand == "皂品" || selectedDevice.Brand == "VGN") && selectedDevice.Category == DeviceCategory.Mouse)
                    {
                        if ((DateTime.Now - lastZaopinDpiPollTime).TotalMilliseconds >= 300)
                        {
                            lastZaopinDpiPollTime = DateTime.Now;
                            int zStage = VgnDeviceHelper.FastQueryVgnDpiStage();
                            if (zStage >= 1)
                            {
                                int[] zStages = (selectedDevice.DpiStages != null && selectedDevice.DpiStages.Length > 0)
                                    ? selectedDevice.DpiStages
                                    : (dpiStageList != null && dpiStageList.Count > 0 ? dpiStageList.ToArray() : new int[] { 400, 800, 1600, 2400, 3200, 6400 });
                                int zCount = zStages.Length;
                                int zDpi = (zStage >= 1 && zStage <= zStages.Length) ? zStages[zStage - 1] : 1600;

                                if (lastMonitoredZaopinStage != -1 && zStage != lastMonitoredZaopinStage)
                                {
                                    if (pendingZaopinStageCandidate == zStage)
                                    {
                                        pendingZaopinStageCandidate = -1;
                                        int newDpi = zDpi;
                                        int newStage = zStage;
                                        int newCount = zCount;
                                        try
                                        {
                                            this.BeginInvoke(new Action(() => {
                                                OnDpiChanged("Zaopin", newDpi, newStage, newCount, null, -1);
                                            }));
                                        }
                                        catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.Hardware.cs: 已忽略异常", diagEx); }
                                        lastMonitoredZaopinStage = zStage;
                                    }
                                    else
                                    {
                                        pendingZaopinStageCandidate = zStage;
                                    }
                                }
                                else
                                {
                                    pendingZaopinStageCandidate = -1;
                                    lastMonitoredZaopinStage = zStage;
                                }
                            }
                        }
                    }
                }
                catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.Hardware.cs: 已忽略异常", diagEx); }
                int pollSleep = isMouseSleeping ? 250 : 50; // 鼠标活跃时 50ms 极速响应，按键瞬间 OSD 丝滑弹出；待机时 250ms 静默
                Thread.Sleep(pollSleep);
            }
        }

        // 电量连续渐变颜色：100% 绿色 → 0% 红色（HSV 色相 120°→0°，饱和/明度固定）
        private static Color GetBatteryGradientColor(int batteryPercent)
        {
            int pct = batteryPercent < 0 ? 0 : (batteryPercent > 100 ? 100 : batteryPercent);
            double hue = 120.0 * pct / 100.0; // 120=绿, 60=黄, 0=红
            double s = 0.85;
            double v = 0.95;

            double c = v * s;
            double x = c * (1.0 - Math.Abs((hue / 60.0) % 2.0 - 1.0));
            double m = v - c;
            double r, g, b;
            if (hue < 60) { r = c; g = x; b = 0; }
            else if (hue < 120) { r = x; g = c; b = 0; }
            else if (hue < 180) { r = 0; g = c; b = x; }
            else if (hue < 240) { r = 0; g = x; b = c; }
            else if (hue < 300) { r = x; g = 0; b = c; }
            else { r = c; g = 0; b = x; }

            int R = (int)Math.Round((r + m) * 255.0);
            int G = (int)Math.Round((g + m) * 255.0);
            int B = (int)Math.Round((b + m) * 255.0);
            if (R < 0) R = 0; if (R > 255) R = 255;
            if (G < 0) G = 0; if (G > 255) G = 255;
            if (B < 0) B = 0; if (B > 255) B = 255;
            return Color.FromArgb(R, G, B);
        }

        private void OnDpiChanged(string brand, int newDpi, int newStage, int stageCount, string deviceId = null, int customDpiY = -1)
        {
            int curDpiY = (customDpiY > 0) ? customDpiY : newDpi;

            if (activeDevices != null)
            {
                MouseBatteryInfo matched = null;
                if (!string.IsNullOrEmpty(deviceId))
                {
                    string normTarget = RazerDeviceHelper.NormalizeRazerDeviceId(deviceId);
                    matched = activeDevices.Find(d => string.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase) ||
                                                      string.Equals(RazerDeviceHelper.NormalizeRazerDeviceId(d.DeviceId), normTarget, StringComparison.OrdinalIgnoreCase));
                }
                if (matched == null)
                {
                    matched = activeDevices.Find(d => d.Brand == brand);
                }
                if (matched != null)
                {
                    matched.Dpi = newDpi;
                    matched.DpiStage = newStage;
                    matched.DpiStageCount = stageCount;
                    if (customDpiY > 0) matched.DpiY = customDpiY; else if (matched.DpiStagesY != null && matched.DpiStagesY.Length >= newStage && newStage >= 1)
                    {
                        matched.DpiY = matched.DpiStagesY[newStage - 1];
                    }
                    else if (brand == "Valkyrie" && ValkyrieDeviceHelper.CachedDpiY > 0)
                    {
                        matched.DpiY = ValkyrieDeviceHelper.CachedDpiY;
                    }
                    else if (brand == "Razer" && RazerDeviceHelper.CachedDpiY > 0)
                    {
                        matched.DpiY = RazerDeviceHelper.CachedDpiY;
                    }
                    else if (brand == "Rapoo" && RapooDeviceHelper.CachedDpiY > 0)
                    {
                        matched.DpiY = RapooDeviceHelper.CachedDpiY;
                    }
                    else
                    {
                        matched.DpiY = newDpi;
                    }
                    curDpiY = matched.DpiY;
                }
            }

            if (customDpiY > 0) curDpiY = customDpiY; else if (curDpiY <= 0)
            {
                if (brand == "Valkyrie" && ValkyrieDeviceHelper.CachedDpiY > 0) curDpiY = ValkyrieDeviceHelper.CachedDpiY;
                else if (brand == "Razer" && RazerDeviceHelper.CachedDpiY > 0) curDpiY = RazerDeviceHelper.CachedDpiY;
                else if (brand == "Rapoo" && RapooDeviceHelper.CachedDpiY > 0) curDpiY = RapooDeviceHelper.CachedDpiY;
                else curDpiY = newDpi;
            }

            if (dpiOsdEnabled && osdForm != null)
            {
                if ((DateTime.Now - lastGameOptOsdTime).TotalMilliseconds >= 2500)
                {
                    osdForm.ShowDpi(newDpi, curDpiY, newStage, stageCount, brand);
                }
            }

            if (lastInfo != null && (string.IsNullOrEmpty(deviceId) ||
                                     string.Equals(lastInfo.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase) ||
                                     string.Equals(RazerDeviceHelper.NormalizeRazerDeviceId(lastInfo.DeviceId), RazerDeviceHelper.NormalizeRazerDeviceId(deviceId), StringComparison.OrdinalIgnoreCase)))
            {
                if (lastInfo.Brand == brand)
                {
                    lastInfo.Dpi = newDpi;
                    lastInfo.DpiY = curDpiY;
                    lastInfo.DpiStage = newStage;
                    lastInfo.DpiStageCount = stageCount;
                }
            }

            if (selectedDevice != null && (string.IsNullOrEmpty(deviceId) ||
                                           string.Equals(selectedDevice.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase) ||
                                           string.Equals(RazerDeviceHelper.NormalizeRazerDeviceId(selectedDevice.DeviceId), RazerDeviceHelper.NormalizeRazerDeviceId(deviceId), StringComparison.OrdinalIgnoreCase)))
            {
                if (selectedDevice.Brand == brand)
                {
                    selectedDevice.Dpi = newDpi;
                    selectedDevice.DpiY = curDpiY;
                    selectedDevice.DpiStage = newStage;
                    selectedDevice.DpiStageCount = stageCount;

                    if (brand == "Rapoo")
                    {
                        if (tileRapooDpi != null)
                        {
                            tileRapooDpi.ValueText = newDpi.ToString();
                            tileRapooDpi.Badge = "";
                            tileRapooDpi.Invalidate();
                        }
                    }

                    // Highlight corresponding segment button (for all mice: Razer, Valkyrie, Logitech, Rapoo)
                    if (btnDpiStages != null)
                    {
                        if (isGamingActive && autoGameDpiEnabled) { UpdateGameDpiButtonSelection(newStage); if (desktopDpiStage > 0) HighlightDpiStage(desktopDpiStage); } else { HighlightDpiStage(newStage); } UpdatePollingRateDisplayStates(); for (int i = 0; false && i < btnDpiStages.Length; i++)
                        {
                            bool isCur = (newStage > 0 && i < stageCount)
                                ? (i == (newStage - 1))
                                : (i < dpiStageValues.Length && dpiStageValues[i] == newDpi);
                            btnDpiStages[i].Selected = isCur;
                        }
                    }

                    // Update Tray DPI submenu checks
                    if (dpiMenu != null)
                    {
                        foreach (ToolStripItem item in dpiMenu.DropDownItems)
                        {
                            var mi = item as ToolStripMenuItem;
                            if (mi != null && mi.Tag != null)
                            {
                                mi.Checked = ((int)mi.Tag == newDpi);
                            }
                        }
                    }

                    // Update status text
                    UpdateStatusDisplay();
                }
            }
        }

        public void SetDpiFromUI(int dpi)
        {
            var curDev = selectedDevice;
            new Thread(() => {
                int targetStage = -1;
                for (int i = 0; i < dpiStageValues.Length; i++)
                {
                    if (dpiStageValues[i] == dpi)
                    {
                        targetStage = i + 1;
                        break;
                    }
                }

                if (curDev != null && FdasRuntimeHelper.IsFdasMouse(curDev))
                {
                    int dpiY = dpi;
                    if (targetStage > 0 && curDev.DpiStagesY != null && curDev.DpiStagesY.Length >= targetStage)
                    {
                        dpiY = curDev.DpiStagesY[targetStage - 1];
                    }
                    else if (curDev.DpiY > 0)
                    {
                        dpiY = curDev.DpiY;
                    }

                    FdasRuntimeHelper.SetDpi(curDev.DeviceId, dpi, dpiY, targetStage > 0 ? targetStage : 1);

                    this.BeginInvoke(new Action(() => {
                        if (curDev != null)
                        {
                            curDev.Dpi = dpi;
                            curDev.DpiY = dpiY;
                            if (targetStage > 0) curDev.DpiStage = targetStage;
                        }
                        HighlightDpiStage(targetStage > 0 ? targetStage : 1);
                        OnDpiChanged(curDev != null ? curDev.Brand : "FDAS", dpi, targetStage > 0 ? targetStage : 1, dpiStageValues.Length, curDev != null ? curDev.DeviceId : null, dpiY);
                    }));
                    return;
                }

                if (curDev != null && (curDev.Brand == "Rapoo" || curDev.Brand == "雷柏"))
                {
                    int dpiY = dpi;
                    if (targetStage > 0 && curDev.DpiStagesY != null && curDev.DpiStagesY.Length >= targetStage)
                    {
                        dpiY = curDev.DpiStagesY[targetStage - 1];
                    }
                    else if (curDev.DpiY > 0)
                    {
                        dpiY = curDev.DpiY;
                    }

                    if (targetStage > 0)
                    {
                        RapooDeviceHelper.SetRapooDpiStage(targetStage);
                    }
                    else
                    {
                        RapooDeviceHelper.SetRapooXyDpi(dpi, dpiY, curDev.DpiStage > 0 ? curDev.DpiStage : 1);
                    }

                    this.BeginInvoke(new Action(() => {
                        if (curDev != null)
                        {
                            curDev.Dpi = dpi;
                            curDev.DpiY = dpiY;
                            if (targetStage > 0) curDev.DpiStage = targetStage;
                        }
                        HighlightDpiStage(targetStage > 0 ? targetStage : 1);
                        OnDpiChanged("Rapoo", dpi, targetStage > 0 ? targetStage : 1, dpiStageValues.Length, null, dpiY);
                    }));
                }
                else if (curDev != null && (curDev.Brand == "Valkyrie" || curDev.Brand == "瓦尔基里"))
                {
                    int dpiY = dpi;
                    if (targetStage > 0 && curDev.DpiStagesY != null && curDev.DpiStagesY.Length >= targetStage)
                    {
                        dpiY = curDev.DpiStagesY[targetStage - 1];
                    }
                    else if (curDev.DpiY > 0)
                    {
                        dpiY = curDev.DpiY;
                    }

                    if (targetStage > 0 && curDev.DpiStages != null && curDev.DpiStagesY != null && curDev.DpiStages.Length == curDev.DpiStagesY.Length)
                    {
                        ValkyrieDeviceHelper.UpdateValkyrieHardwareStages(targetStage, curDev.DpiStages, curDev.DpiStagesY);
                    }
                    else
                    {
                        ValkyrieDeviceHelper.SetValkyrieDpi(dpi, dpiY);
                    }

                    this.BeginInvoke(new Action(() => {
                        if (curDev != null)
                        {
                            curDev.Dpi = dpi;
                            curDev.DpiY = dpiY;
                            if (targetStage > 0) curDev.DpiStage = targetStage;
                        }
                        OnDpiChanged("Valkyrie", dpi, targetStage > 0 ? targetStage : 1, dpiStageValues.Length, null, dpiY);
                    }));
                }
                else if (curDev != null && (curDev.Brand == "Logitech" || curDev.Brand == "罗技"))
                {
                    int dpiY = dpi;
                    if (targetStage > 0 && curDev.DpiStagesY != null && curDev.DpiStagesY.Length >= targetStage)
                    {
                        dpiY = curDev.DpiStagesY[targetStage - 1];
                    }
                    else if (curDev.DpiY > 0)
                    {
                        dpiY = curDev.DpiY;
                    }

                    LogitechDeviceHelper.SetLogitechDpi(dpi, dpiY, curDev.DeviceId);

                    this.BeginInvoke(new Action(() => {
                        if (curDev != null)
                        {
                            curDev.Dpi = dpi;
                            curDev.DpiY = dpiY;
                            if (targetStage > 0) curDev.DpiStage = targetStage;
                        }
                        OnDpiChanged("Logitech", dpi, targetStage > 0 ? targetStage : 1, dpiStageValues.Length, null, dpiY);
                    }));
                }
                else if (curDev != null && (curDev.Brand == "Mchose" || curDev.Brand == "MCHOSE" || curDev.Brand == "迈从") && curDev.Category == DeviceCategory.Mouse)
                {
                    int dpiY = dpi;
                    if (targetStage > 0 && curDev.DpiStagesY != null && curDev.DpiStagesY.Length >= targetStage)
                    {
                        dpiY = curDev.DpiStagesY[targetStage - 1];
                    }
                    else if (curDev.DpiY > 0)
                    {
                        dpiY = curDev.DpiY;
                    }

                    if (targetStage > 0)
                    {
                        MchoseMouseHelper.SetMchoseDpiStage(targetStage);
                    }
                    else
                    {
                        MchoseMouseHelper.SetMchoseDpi(dpi, dpiY, curDev.DpiStage > 0 ? curDev.DpiStage : 1);
                    }

                    this.BeginInvoke(new Action(() => {
                        if (curDev != null)
                        {
                            curDev.Dpi = dpi;
                            curDev.DpiY = dpiY;
                            if (targetStage > 0) curDev.DpiStage = targetStage;
                        }
                        HighlightDpiStage(targetStage > 0 ? targetStage : 1);
                        OnDpiChanged("MCHOSE", dpi, targetStage > 0 ? targetStage : 1, dpiStageValues.Length, null, dpiY);
                    }));
                }
                else if (curDev != null && (curDev.Brand == "Zaopin" || curDev.Brand == "皂品" || curDev.Brand == "VGN"))
                {
                    int dpiY = dpi;
                    if (targetStage > 0 && curDev.DpiStagesY != null && curDev.DpiStagesY.Length >= targetStage)
                    {
                        dpiY = curDev.DpiStagesY[targetStage - 1];
                    }
                    else if (curDev.DpiY > 0)
                    {
                        dpiY = curDev.DpiY;
                    }

                    if (targetStage > 0)
                    {
                        VgnDeviceHelper.SetVgnDpiStage(targetStage);
                    }
                    else
                    {
                        // 自定义 DPI：把当前激活档位的值改为自定义值
                        int activeStage = curDev.DpiStage > 0 ? curDev.DpiStage : 1;
                        VgnDeviceHelper.SetVgnDpiValue(dpi, activeStage - 1);
                    }

                    this.BeginInvoke(new Action(() => {
                        if (curDev != null)
                        {
                            curDev.Dpi = dpi;
                            curDev.DpiY = dpiY;
                            if (targetStage > 0) curDev.DpiStage = targetStage;
                        }
                        HighlightDpiStage(targetStage > 0 ? targetStage : 1);
                        OnDpiChanged("Zaopin", dpi, targetStage > 0 ? targetStage : 1, dpiStageValues.Length, null, dpiY);
                    }));
                }
                else
                {
                    string rzDevId = curDev != null ? curDev.DeviceId : null;
                    int rzDpiY = dpi;
                    if (targetStage > 0 && curDev != null && curDev.DpiStagesY != null && curDev.DpiStagesY.Length >= targetStage)
                    {
                        rzDpiY = curDev.DpiStagesY[targetStage - 1];
                    }
                    else if (curDev != null && curDev.DpiY > 0)
                    {
                        rzDpiY = curDev.DpiY;
                    }

                    if (targetStage > 0)
                    {
                        RazerDeviceHelper.SetRazerDpiStage(targetStage, rzDevId);
                    }
                    else
                    {
                        RazerDeviceHelper.SetRazerDpi(dpi, rzDpiY, rzDevId);
                    }

                    int queryDpi, stage, count;
                    if (RazerDeviceHelper.FastQueryDpi(out queryDpi, out stage, out count, rzDevId))
                    {
                        this.BeginInvoke(new Action(() => {
                            if (curDev != null)
                            {
                                curDev.Dpi = queryDpi;
                                curDev.DpiStage = stage;
                            }
                            OnDpiChanged("Razer", queryDpi, stage, count, rzDevId, rzDpiY);
                        }));
                    }
                    else
                    {
                        this.BeginInvoke(new Action(() => {
                            if (curDev != null)
                            {
                                curDev.Dpi = dpi;
                                if (targetStage > 0) curDev.DpiStage = targetStage;
                            }
                            OnDpiChanged("Razer", dpi, targetStage > 0 ? targetStage : 1, dpiStageValues.Length, rzDevId, rzDpiY);
                        }));
                    }
                }
            }) { IsBackground = true }.Start();
        }

        public void SetPollingRateFromUI(int hz)
        {
            desktopPollingRate = hz;
            SaveConfig();

            var curDev = selectedDevice;
            new Thread(() => {
                bool ok = false;
                if (!isGamingActive)
                {
                    if (curDev != null && FdasRuntimeHelper.IsFdasMouse(curDev))
                    {
                        ok = FdasRuntimeHelper.SetPollingRate(curDev.DeviceId, hz);
                    }
                    else if (curDev != null && (curDev.Brand == "Valkyrie" || curDev.Brand == "瓦尔基里"))
                    {
                        ok = ValkyrieDeviceHelper.SetValkyriePollingRate(hz);
                    }
                    else if (curDev != null && (curDev.Brand == "Logitech" || curDev.Brand == "罗技"))
                    {
                        ok = LogitechDeviceHelper.SetLogitechPollingRate(hz, curDev.DeviceId);
                    }
                    else if (curDev != null && (curDev.Brand == "Rapoo" || curDev.Brand == "雷柏"))
                    {
                        ok = RapooDeviceHelper.SetRapooPollingRate(hz);
                    }
                    else if (curDev != null && (curDev.Brand == "Mchose" || curDev.Brand == "MCHOSE" || curDev.Brand == "迈从") && curDev.Category == DeviceCategory.Mouse)
                    {
                        ok = MchoseMouseHelper.SetMchosePollingRate(hz);
                    }
                    else if (curDev != null && (curDev.Brand == "Zaopin" || curDev.Brand == "皂品" || curDev.Brand == "VGN"))
                    {
                        ok = VgnDeviceHelper.SetVgnPollingRate(hz);
                    }
                    else if (curDev != null && (curDev.Brand == "Razer" || curDev.Brand == "雷蛇" || (curDev.DeviceId != null && curDev.DeviceId.StartsWith("Razer:1532:"))))
                    {
                        ok = RazerDeviceHelper.SetRazerPollingRate(hz, curDev.DeviceId);
                    }
                    else
                    {
                        ok = RazerDeviceHelper.SetRazerPollingRate(hz, curDev != null ? curDev.DeviceId : null);
                    }
                }
                else
                {
                    ok = true;
                }

                if (ok)
                {
                    this.BeginInvoke(new Action(() => {
                        if (!isGamingActive && curDev != null) curDev.PollingRate = hz;
                        if (!isGamingActive && lastInfo != null && lastInfo.DeviceId == (curDev != null ? curDev.DeviceId : "")) lastInfo.PollingRate = hz;

                        UpdateDesktopRateButtonSelection(hz);
                        UpdatePollingRateDisplayStates();
                        UpdateStatusDisplay();
                    }));
                }
            }) { IsBackground = true }.Start();
        }

        public void SetGamePollingRateFromUI(int hz)
        {
            gamePollingRate = hz;
            UpdateGameRateButtonSelection(hz);
            UpdatePollingRateDisplayStates();
            SaveConfig();

            if (isGamingActive && autoGameRateEnabled)
            {
                ApplyGamePollingRate();
            }
        }

        public void SetGameDpiStageFromUI(int stage)
        {
            if (stage < 1 || stage > 5) stage = 1;
            gameDpiStage = stage;
            UpdateGameDpiButtonSelection(stage);
            UpdatePollingRateDisplayStates();
            SaveConfig();

            if (isGamingActive && autoGameDpiEnabled)
            {
                ApplyGameDpiStage();
            }
        }

        public void SetAutoGameOptEnabled(bool enabled)
        {
            isUpdatingUI = true;
            try
            {
                if (chkAutoGameOpt != null) chkAutoGameOpt.Checked = enabled;

                if (!enabled)
                {
                    autoGameRateEnabled = false;
                    autoGameDpiEnabled = false;
                    if (chkAutoGameRate != null) chkAutoGameRate.Checked = false;
                    if (chkAutoGameDpi != null) chkAutoGameDpi.Checked = false;
                    if (autoGameRateToggleItem != null) autoGameRateToggleItem.Checked = false;
                    if (autoGameDpiToggleItem != null) autoGameDpiToggleItem.Checked = false;
                    if (btnManageGames != null) btnManageGames.Visible = false;
                }
                else
                {
                    // 总开关开启时，默认启用回报率与 DPI 优化
                    autoGameRateEnabled = true;
                    autoGameDpiEnabled = true;
                    if (chkAutoGameRate != null) chkAutoGameRate.Checked = true;
                    if (chkAutoGameDpi != null) chkAutoGameDpi.Checked = true;
                    if (autoGameRateToggleItem != null) autoGameRateToggleItem.Checked = true;
                    if (autoGameDpiToggleItem != null) autoGameDpiToggleItem.Checked = true;
                }

                UpdatePerformanceCardHeightForCurrentDevice();
                UpdatePollingRateDisplayStates();
            }
            finally
            {
                isUpdatingUI = false;
            }
            SaveConfig();

            if (!enabled && isGamingActive)
            {
                RestoreDesktopPollingRate();
                RestoreDesktopDpiStage();
                isGamingActive = false;
            }
            else if (enabled && isGamingActive)
            {
                ApplyGamePollingRate();
                ApplyGameDpiStage();
            }
        }

        private void SetAutoGameDpiEnabled(bool enabled)
        {
            autoGameDpiEnabled = enabled;
            isUpdatingUI = true;
            try
            {
                if (chkAutoGameDpi != null) chkAutoGameDpi.Checked = enabled;
                if (autoGameDpiToggleItem != null) autoGameDpiToggleItem.Checked = enabled;

                bool anyEnabled = (autoGameRateEnabled || autoGameDpiEnabled);
                if (chkAutoGameOpt != null) chkAutoGameOpt.Checked = anyEnabled;

                UpdatePerformanceCardHeightForCurrentDevice();
                UpdatePollingRateDisplayStates();
            }
            finally
            {
                isUpdatingUI = false;
            }
            SaveConfig();

            // If toggled off while in game, restore desktop DPI immediately
            if (!enabled && isGamingActive)
            {
                if (desktopDpiStage > 0)
                {
                    RestoreDesktopDpiStage();
                }
                if (!autoGameRateEnabled)
                {
                    isGamingActive = false;
                }
            }
            else if (enabled && isGamingActive)
            {
                ApplyGameDpiStage();
            }
        }

        private void CycleRapooPollingRate()
        {
            var curDev = selectedDevice;
            int curRate = (curDev != null && curDev.PollingRate > 0) ? curDev.PollingRate : 1000;
            int[] rates = new int[] { 125, 250, 500, 1000, 2000, 4000, 8000 };
            int idx = Array.IndexOf(rates, curRate);
            int nextRate = rates[(idx + 1) % rates.Length];
            SetRapooPollingRateManual(nextRate);
        }

        public void SetRapooPollingRateManual(int hz)
        {
            RapooDeviceHelper.SetRapooPollingRate(hz);
            if (selectedDevice != null && selectedDevice.Brand == "Rapoo")
            {
                selectedDevice.PollingRate = hz;
            }
            if (lastInfo != null && lastInfo.Brand == "Rapoo")
            {
                lastInfo.PollingRate = hz;
            }

            if (rateMenu != null)
            {
                foreach (ToolStripItem item in rateMenu.DropDownItems)
                {
                    var mi = item as ToolStripMenuItem;
                    if (mi != null && mi.Tag != null && mi.Tag is int)
                    {
                        mi.Checked = ((int)mi.Tag == hz);
                    }
                }
            }
        }

        private void SetAutoGameRateEnabled(bool enabled)
        {
            autoGameRateEnabled = enabled;
            isUpdatingUI = true;
            try
            {
                if (chkAutoGameRate != null) chkAutoGameRate.Checked = enabled;
                if (autoGameRateToggleItem != null) autoGameRateToggleItem.Checked = enabled;

                bool anyEnabled = (autoGameRateEnabled || autoGameDpiEnabled);
                if (chkAutoGameOpt != null) chkAutoGameOpt.Checked = anyEnabled;

                UpdatePerformanceCardHeightForCurrentDevice();
                UpdatePollingRateDisplayStates();
            }
            finally
            {
                isUpdatingUI = false;
            }
            SaveConfig();

            // If toggled off while in game, restore desktop rate immediately
            if (!enabled && isGamingActive)
            {
                RestoreDesktopPollingRate();
                if (!autoGameDpiEnabled)
                {
                    isGamingActive = false;
                }
            }
            else if (enabled && isGamingActive)
            {
                ApplyGamePollingRate();
            }
        }

    }
}
