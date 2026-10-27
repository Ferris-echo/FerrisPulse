// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// 1. 目标平台与编译规范：严格维持 C# 5.0 语法标准与 .NET Framework 4.0 兼容性。严禁引入任何第三方包或依赖。
// 2. 编码规范：所有文件严格保存为 UTF-8 带签名（BOM）格式，防止中文字符乱码。
// 3. 架构规范：本文件属于 MainForm 的分部类 (partial class) 之一。修改时请保持职责清晰，任何新增方法或逻辑请同步维护 ARCHITECTURE.md。
// 模块描述: MainForm 硬件通信调度、后台状态轮询、DPI实时监听、游戏全屏感知与界面状态同步
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
            try { dpiMonitorWakeEvent.Set(); } catch { }
            RapooDeviceHelper.CloseRapooStream();
        }

        public static int CalculateAdaptiveDpiPollInterval(double idleSeconds, bool isSleeping, string brand)
        {
            if (isSleeping) return 2000;
            if (idleSeconds >= 30.0) return 1000;
            if (!string.IsNullOrEmpty(brand) && (brand.Equals("Rapoo", StringComparison.OrdinalIgnoreCase) || brand.Equals("雷柏", StringComparison.OrdinalIgnoreCase)))
            {
                return 200;
            }
            return 50;
        }

        public static bool EvaluateDpiCalibrationResult(int hwDpi, int hwStage, int recordedDpi, int recordedStage)
        {
            if (hwDpi <= 0 || hwStage <= 0) return false;
            if (recordedDpi <= 0) return true;
            return (hwDpi != recordedDpi || hwStage != recordedStage);
        }

        private void PerformInstantDpiCalibration(MouseBatteryInfo cur)
        {
            if (cur == null || cur.Category != DeviceCategory.Mouse) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    int qDpi, qDpiY, qStage, qCount;
                    if (HardwareCommandBus.FastQueryDpi(cur, out qDpi, out qDpiY, out qStage, out qCount))
                    {
                        if (EvaluateDpiCalibrationResult(qDpi, qStage, cur.Dpi, cur.DpiStage))
                        {
                            this.BeginInvoke(new Action(() =>
                            {
                                OnDpiChanged(cur.Brand ?? "Mouse", qDpi, qStage, qCount, cur.DeviceId, qDpiY);
                            }));
                        }
                    }
                }
                catch { }
            });
        }

        private void DpiMonitorWorker()
        {
            EnableMouseWakeDetector(true);
            while (isDpiMonitorRunning)
            {
                try
                {
                    // 1. 若用户未开启 DPI OSD 提示且主窗口未在前台显示，休眠省电零占用
                    if (!dpiOsdEnabled && !this.Visible)
                    {
                        dpiMonitorWakeEvent.WaitOne(800);
                        continue;
                    }

                    // 2. 收集当前支持极速硬件 DPI 监听的鼠标设备（通过统一 Capabilities 契约与 HardwareCommandBus 调度）
                    List<MouseBatteryInfo> monitorTargets = new List<MouseBatteryInfo>();
                    if (selectedDevice != null && selectedDevice.Category == DeviceCategory.Mouse)
                    {
                        HardwareDriverRegistry.EnsureCapabilitiesInitialized(selectedDevice);
                        if (selectedDevice.Capabilities != null && selectedDevice.Capabilities.SupportsFastDpiMonitor)
                        {
                            monitorTargets.Add(selectedDevice);
                        }
                    }
                    if (activeDevices != null)
                    {
                        for (int i = 0; i < activeDevices.Count; i++)
                        {
                            var d = activeDevices[i];
                            if (d != null && d.Category == DeviceCategory.Mouse && d.IsConnected && !d.IsSleeping)
                            {
                                HardwareDriverRegistry.EnsureCapabilitiesInitialized(d);
                                if (d.Capabilities != null && d.Capabilities.SupportsFastDpiMonitor)
                                {
                                    bool exists = false;
                                    for (int k = 0; k < monitorTargets.Count; k++)
                                    {
                                        if (string.Equals(monitorTargets[k].DeviceId, d.DeviceId, StringComparison.OrdinalIgnoreCase))
                                        {
                                            exists = true;
                                            break;
                                        }
                                    }
                                    if (!exists) monitorTargets.Add(d);
                                }
                            }
                        }
                    }

                    if (monitorTargets.Count == 0 && (selectedDevice == null || selectedDevice.Category == DeviceCategory.Mouse))
                    {
                        monitorTargets.Add(null);
                    }

                    long lastTicks = System.Threading.Interlocked.Read(ref lastTargetMouseActivityTicks);
                    double idleSec = (DateTime.UtcNow.Ticks - lastTicks) / (double)TimeSpan.TicksPerSecond;
                    if (idleSec < 0) idleSec = 0;

                    bool anySuccess = false;
                    bool anyTargetSleeping = isMouseSleeping;

                    for (int tIdx = 0; tIdx < monitorTargets.Count; tIdx++)
                    {
                        var targetDev = monitorTargets[tIdx];
                        if (targetDev != null && targetDev.IsSleeping)
                        {
                            anyTargetSleeping = true;
                            continue;
                        }

                        bool isRapooTarget = targetDev != null && (string.Equals(targetDev.Brand, "Rapoo", StringComparison.OrdinalIgnoreCase) ||
                                             string.Equals(targetDev.Brand, "雷柏", StringComparison.OrdinalIgnoreCase));
                        if (isRapooTarget)
                        {
                            if ((DateTime.Now - lastRapooDpiPollTime).TotalMilliseconds < 200)
                                continue;
                            lastRapooDpiPollTime = DateTime.Now;
                        }

                        int dpi = 0, dpiY = 0, stage = 0, count = 0;
                        bool queryOk = false;
                        if (targetDev != null)
                        {
                            queryOk = HardwareCommandBus.FastQueryDpi(targetDev, out dpi, out dpiY, out stage, out count);
                        }
                        else
                        {
                            queryOk = RazerDeviceHelper.FastQueryDpi(out dpi, out dpiY, out stage, out count, null);
                        }

                        if (queryOk)
                        {
                            anySuccess = true;
                            int validCount = count > 0 ? count : 5;
                            if (dpi >= 50 && dpi <= 50000 && stage >= 1 && stage <= validCount)
                            {
                                string key = (targetDev != null && !string.IsNullOrEmpty(targetDev.DeviceId)) ? targetDev.DeviceId : ((targetDev != null && !string.IsNullOrEmpty(targetDev.Brand)) ? targetDev.Brand : "default");
                                int prevDpi = -1;
                                int prevStage = -1;
                                lastMonitoredDpis.TryGetValue(key, out prevDpi);
                                lastMonitoredStages.TryGetValue(key, out prevStage);

                                if (prevDpi != -1 && (dpi != prevDpi || stage != prevStage))
                                {
                                    int candDpi = -1;
                                    int candStage = -1;
                                    pendingDpiCandidates.TryGetValue(key, out candDpi);
                                    pendingStageCandidates.TryGetValue(key, out candStage);

                                    if (isRapooTarget || (candDpi == dpi && candStage == stage))
                                    {
                                        pendingDpiCandidates.Remove(key);
                                        pendingStageCandidates.Remove(key);

                                        int newDpi = dpi;
                                        int newDpiY = dpiY;
                                        int newStage = stage;
                                        int newCount = count;
                                        string notifyBrand = targetDev != null ? (targetDev.Brand ?? "Mouse") : "Razer";
                                        string notifyDevId = targetDev != null ? targetDev.DeviceId : null;
                                        try
                                        {
                                            this.BeginInvoke(new Action(() => {
                                                OnDpiChanged(notifyBrand, newDpi, newStage, newCount, notifyDevId, newDpiY);
                                            }));
                                        }
                                        catch { }

                                        lastMonitoredDpis[key] = dpi;
                                        lastMonitoredStages[key] = stage;
                                        lastMonitoredDpi = dpi;
                                        lastMonitoredStage = stage;
                                    }
                                    else
                                    {
                                        pendingDpiCandidates[key] = dpi;
                                        pendingStageCandidates[key] = stage;
                                    }
                                }
                                else
                                {
                                    pendingDpiCandidates.Remove(key);
                                    pendingStageCandidates.Remove(key);

                                    lastMonitoredDpis[key] = dpi;
                                    lastMonitoredStages[key] = stage;
                                    lastMonitoredDpi = dpi;
                                    lastMonitoredStage = stage;
                                }
                            }
                        }
                        else
                        {
                            if (targetDev != null && targetDev.IsSleeping) anyTargetSleeping = true;
                        }
                    }

                    // 3. 动态自适应调度判定：
                    // - 活跃态 (0~30s): 50ms (Rapoo为200ms) 极速响应
                    // - 渐退态 (>=30s 且硬件未睡): 1000ms 轻量探询
                    // - 彻底休眠态 (问不到了 / 硬件已入睡): 彻底停发一切 2.4G 报文，进入 2000ms 事件挂起静默
                    bool isDeepSleeping = monitorTargets.Count == 0 || anyTargetSleeping || (!anySuccess && idleSec >= 30.0);
                    string primaryBrand = (selectedDevice != null && !string.IsNullOrEmpty(selectedDevice.Brand)) ? selectedDevice.Brand : "Razer";
                    int pollSleep = CalculateAdaptiveDpiPollInterval(idleSec, isDeepSleeping, primaryBrand);

                    if (isDeepSleeping)
                    {
                        isDpiMonitorSleeping = true;
                        dpiMonitorWakeEvent.WaitOne(pollSleep);
                    }
                    else
                    {
                        isDpiMonitorSleeping = false;
                        dpiMonitorWakeEvent.WaitOne(pollSleep);
                    }
                }
                catch { }
            }
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
            var curDev = selectedDevice ?? GetActiveMouseDevice();
            if (curDev == null) return;

            int targetStage = -1;
            for (int i = 0; i < dpiStageValues.Length; i++)
            {
                if (dpiStageValues[i] == dpi)
                {
                    targetStage = i + 1;
                    break;
                }
            }

            if (isGamingActive && autoGameDpiEnabled)
            {
                // 处于游戏运行中时：调节日常 DPI 仅更新退出恢复目标，不改变鼠标当前电竞硬件状态
                desktopDpiStage = (targetStage > 0) ? targetStage : 1;
                UpdatePollingRateDisplayStates();
                return;
            }

            desktopDpiStage = (targetStage > 0) ? targetStage : 1;

            int dpiY = dpi;
            if (targetStage > 0 && curDev.DpiStagesY != null && curDev.DpiStagesY.Length >= targetStage)
            {
                dpiY = curDev.DpiStagesY[targetStage - 1];
            }
            else if (curDev.DpiY > 0)
            {
                dpiY = curDev.DpiY;
            }

            SyncMonitoredDpiState(dpi, targetStage > 0 ? targetStage : 1, curDev.DeviceId);

            new Thread(() => {
                bool ok = HardwareCommandBus.SetDpi(curDev, dpi, dpiY, targetStage);
                int queryDpi, queryDpiY, stage, count;
                if (HardwareCommandBus.FastQueryDpi(curDev, out queryDpi, out queryDpiY, out stage, out count))
                {
                    this.BeginInvoke(new Action(() => {
                        curDev.Dpi = queryDpi;
                        curDev.DpiY = queryDpiY > 0 ? queryDpiY : queryDpi;
                        curDev.DpiStage = stage;
                        HighlightDpiStage(stage);
                        OnDpiChanged(curDev.Brand ?? "Mouse", queryDpi, stage, count, curDev.DeviceId, curDev.DpiY);
                    }));
                }
                else
                {
                    this.BeginInvoke(new Action(() => {
                        curDev.Dpi = dpi;
                        curDev.DpiY = dpiY;
                        if (targetStage > 0) curDev.DpiStage = targetStage;
                        HighlightDpiStage(targetStage > 0 ? targetStage : 1);
                        int validCount = (dpiStageValues != null && dpiStageValues.Length > 0) ? dpiStageValues.Length : 5;
                        OnDpiChanged(curDev.Brand ?? "Mouse", dpi, targetStage > 0 ? targetStage : 1, validCount, curDev.DeviceId, dpiY);
                    }));
                }
            }) { IsBackground = true }.Start();
        }

        public void SetPollingRateFromUI(int hz)
        {
            desktopPollingRate = hz;
            SaveConfig();

            var curDev = selectedDevice ?? GetActiveMouseDevice();
            if (curDev == null) return;

            new Thread(() => {
                bool ok = false;
                if (!isGamingActive || !autoGameRateEnabled)
                {
                    for (int attempt = 0; attempt < 3; attempt++)
                    {
                        ok = HardwareCommandBus.SetPollingRate(curDev, hz);
                        if (ok) break;
                        Thread.Sleep(60);
                    }
                }
                else
                {
                    ok = true;
                }

                if (ok)
                {
                    this.BeginInvoke(new Action(() => {
                        if ((!isGamingActive || !autoGameRateEnabled) && curDev != null) curDev.PollingRate = hz;
                        if ((!isGamingActive || !autoGameRateEnabled) && lastInfo != null && lastInfo.DeviceId == (curDev != null ? curDev.DeviceId : "")) lastInfo.PollingRate = hz;
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
            var curDev = selectedDevice ?? GetActiveMouseDevice();
            if (curDev != null)
            {
                HardwareCommandBus.SetPollingRate(curDev, hz);
                curDev.PollingRate = hz;
            }
            if (lastInfo != null)
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

        private bool ContainsGameProcess(string name)
        {
            if (string.IsNullOrEmpty(name)) return false;
            string target = name.ToLower();
            if (!target.EndsWith(".exe")) target += ".exe";
            for (int i = 0; i < monitoredGameProcesses.Count; i++)
            {
                string item = monitoredGameProcesses[i].ToLower();
                if (!item.EndsWith(".exe")) item += ".exe";
                if (item == target) return true;
            }
            return false;
        }

        private bool DetectCurrentGameRunning(out string procTitle)
        {
            procTitle = "";
            bool isGame = false;

            if (gameOptMode == 1) // 模式 1: 进程存活常驻模式 (推荐)
            {
                IntPtr fgHwnd = GetForegroundWindow();
                if (fgHwnd != IntPtr.Zero)
                {
                    uint pid = 0;
                    GetWindowThreadProcessId(fgHwnd, out pid);
                    if (pid != 0)
                    {
                        try
                        {
                            using (var proc = Process.GetProcessById((int)pid))
                            {
                                string pName = proc.ProcessName;
                                if (ContainsGameProcess(pName))
                                {
                                    isGame = true;
                                    procTitle = proc.MainWindowTitle;
                                    if (string.IsNullOrEmpty(procTitle)) procTitle = pName;
                                }
                            }
                        }
                        catch { }
                    }
                }

                if (!isGame && monitoredGameProcesses != null && monitoredGameProcesses.Count > 0)
                {
                    for (int i = 0; i < monitoredGameProcesses.Count; i++)
                    {
                        string g = monitoredGameProcesses[i];
                        if (string.IsNullOrEmpty(g)) continue;
                        string pNameOnly = g.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? g.Substring(0, g.Length - 4) : g;
                        try
                        {
                            Process[] matched = Process.GetProcessesByName(pNameOnly);
                            if (matched != null && matched.Length > 0)
                            {
                                bool hasLiveWindow = false;
                                for (int k = 0; k < matched.Length; k++)
                                {
                                    // 仅当进程拥有有效顶层窗体时视为真正运行中，彻底过滤游戏退出后残留的无窗体假死/防作弊僵尸进程
                                    if (matched[k].MainWindowHandle != IntPtr.Zero)
                                    {
                                        hasLiveWindow = true;
                                        procTitle = matched[k].MainWindowTitle;
                                        if (string.IsNullOrEmpty(procTitle)) procTitle = pNameOnly;
                                        break;
                                    }
                                }
                                for (int k = 0; k < matched.Length; k++) matched[k].Dispose();

                                if (hasLiveWindow)
                                {
                                    isGame = true;
                                    break;
                                }
                            }
                        }
                        catch { }
                    }
                }

                if (!isGame && fallbackFullscreenEnabled)
                {
                    string fsTitle;
                    if (GameDetector.IsGameRunning(out fsTitle))
                    {
                        isGame = true;
                        procTitle = fsTitle;
                    }
                }
            }
            else // 模式 0: 仅前台激活模式
            {
                IntPtr fgHwnd = GetForegroundWindow();
                if (fgHwnd != IntPtr.Zero)
                {
                    uint pid = 0;
                    GetWindowThreadProcessId(fgHwnd, out pid);
                    if (pid != 0)
                    {
                        try
                        {
                            using (var proc = Process.GetProcessById((int)pid))
                            {
                                string pName = proc.ProcessName;
                                if (ContainsGameProcess(pName))
                                {
                                    isGame = true;
                                    procTitle = proc.MainWindowTitle;
                                    if (string.IsNullOrEmpty(procTitle)) procTitle = pName;
                                }
                            }
                        }
                        catch { }
                    }
                }

                if (!isGame && fallbackFullscreenEnabled)
                {
                    string fsTitle;
                    if (GameDetector.IsGameRunning(out fsTitle))
                    {
                        isGame = true;
                        procTitle = fsTitle;
                    }
                }
            }

            return isGame;
        }

        private void PerformStartupDesktopCalibration(MouseBatteryInfo dev)
        {
            if (dev == null || dev.Category != DeviceCategory.Mouse) return;
            if (!autoGameRateEnabled && !autoGameDpiEnabled) return;

            string devKey = !string.IsNullOrEmpty(dev.DeviceId) ? dev.DeviceId : (dev.Brand ?? "default");
            bool alreadyCalibrated = false;
            calibratedStartupDevices.TryGetValue(devKey, out alreadyCalibrated);

            string gameTitle;
            bool isGame = DetectCurrentGameRunning(out gameTitle);

            if (isGame)
            {
                if (!isGamingActive)
                {
                    isGamingActive = true;
                    if (autoGameRateEnabled && autoGameDpiEnabled) ApplyGameOptimization(true);
                    else if (autoGameRateEnabled) ApplyGamePollingRate();
                    else if (autoGameDpiEnabled) ApplyGameDpiStage();
                }
                calibratedStartupDevices[devKey] = true;
            }
            else
            {
                if (isGamingActive)
                {
                    isGamingActive = false;
                    if (autoGameRateEnabled && autoGameDpiEnabled) ApplyGameOptimization(false);
                    else if (autoGameRateEnabled) RestoreDesktopPollingRate();
                    else if (autoGameDpiEnabled && desktopDpiStage > 0) RestoreDesktopDpiStage();
                    calibratedStartupDevices[devKey] = true;
                }
                else if (!alreadyCalibrated)
                {
                    // 仅在开机或设备初次识别就绪时执行一次桌面档位同步校准，彻底杜绝关机残留 8000Hz 幽灵回报率
                    RestoreDesktopSettingsSynchronous();
                    calibratedStartupDevices[devKey] = true;
                }
            }
        }

        private void RestoreDesktopSettingsSynchronous()
        {
            try
            {
                var curDev = GetActiveMouseDevice();
                if (curDev == null) return;

                if (autoGameRateEnabled && desktopPollingRate > 0)
                {
                    HardwareCommandBus.SetPollingRate(curDev, desktopPollingRate);
                }
                if (autoGameDpiEnabled && desktopDpiStage > 0)
                {
                    int targetStage = desktopDpiStage;
                    if (targetStage < 1 || targetStage > 5) targetStage = 1;
                    int targetDpi = (dpiStageValues != null && targetStage - 1 < dpiStageValues.Length) ? dpiStageValues[targetStage - 1] : 1600;
                    HardwareCommandBus.SetDpi(curDev, targetDpi, targetDpi, targetStage);
                }
            }
            catch { }
        }

        private void StartGameMonitor()
        {
            if (gameMonitorTimer != null) return;
            gameMonitorTimer = new System.Windows.Forms.Timer();
            gameMonitorTimer.Interval = 1000;
            gameMonitorTimer.Tick += (s, e) => {
                if (!autoGameRateEnabled && !autoGameDpiEnabled) return;
                var curDev = GetActiveMouseDevice();
                if (curDev == null || !IsDeviceSupportingAutoGameRate(curDev)) return;

                string procTitle;
                bool isGame = DetectCurrentGameRunning(out procTitle);
                if (isGame)
                {
                    outOfGameTickCount = 0;
                    inGameTickCount++;
                    if (inGameTickCount >= 2 && !isGamingActive)
                    {
                        isGamingActive = true; if (autoGameRateEnabled && autoGameDpiEnabled) { ApplyGameOptimization(true); } else
                        if (autoGameRateEnabled)
                        {
                            ApplyGamePollingRate();
                        }
                        else if (autoGameDpiEnabled)
                        {
                            ApplyGameDpiStage();
                        }
                    }
                }
                else
                {
                    inGameTickCount = 0;
                    outOfGameTickCount++;
                    if (outOfGameTickCount >= 2 && isGamingActive)
                    {
                        isGamingActive = false; if (autoGameRateEnabled && autoGameDpiEnabled) { ApplyGameOptimization(false); } else
                        if (autoGameRateEnabled)
                        {
                            RestoreDesktopPollingRate();
                        }
                        else if (autoGameDpiEnabled && desktopDpiStage > 0)
                        {
                            RestoreDesktopDpiStage();
                        }
                    }
                }
            };
            gameMonitorTimer.Start();
        }

        private void SyncMonitoredDpiState(int targetDpi, int targetStage, string deviceId = null)
        {
            lastGameOptOsdTime = DateTime.Now;
            string key = deviceId ?? (selectedDevice != null ? selectedDevice.DeviceId : "default");
            if (string.IsNullOrEmpty(key)) key = "default";
            lastMonitoredDpis[key] = targetDpi;
            lastMonitoredStages[key] = targetStage;
            pendingDpiCandidates.Remove(key);
            pendingStageCandidates.Remove(key);

            lastMonitoredDpi = targetDpi;
            lastMonitoredStage = targetStage;
            lastMonitoredRapooDpi = targetDpi;
            lastMonitoredRapooStage = targetStage;
            lastMonitoredValkyrieDpi = targetDpi;
            lastMonitoredValkyrieStage = targetStage;
            lastMonitoredLogitechDpi = targetDpi;
            lastMonitoredLogitechStage = targetStage;
        }

        private void ApplyGameOptimization(bool isEntering)
        {
            var curDev = GetActiveMouseDevice();
            if (curDev == null) return;

            string brand = curDev.Brand;
            int targetHz = isEntering ? gamePollingRate : desktopPollingRate;

            int targetStage;
            int prevStage = desktopDpiStage;
            if (isEntering)
            {
                if (desktopDpiStage <= 0)
                {
                    desktopDpiStage = (curDev.DpiStage > 0) ? curDev.DpiStage : 1;
                }
                targetStage = gameDpiStage;
            }
            else
            {
                targetStage = (desktopDpiStage > 0) ? desktopDpiStage : 1;
                desktopDpiStage = 0;
            }

            if (targetStage < 1 || targetStage > 5) targetStage = 1;
            int targetDpi = (dpiStageValues != null && targetStage - 1 < dpiStageValues.Length) ? dpiStageValues[targetStage - 1] : 800;
            int targetDpiY = targetDpi;
            if (curDev.DpiStagesY != null && curDev.DpiStagesY.Length >= targetStage)
            {
                targetDpiY = curDev.DpiStagesY[targetStage - 1];
            }
            SyncMonitoredDpiState(targetDpi, targetStage, curDev.DeviceId);

            new Thread(() => {
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    bool rOk = HardwareCommandBus.SetPollingRate(curDev, targetHz);
                    if (rOk) break;
                    Thread.Sleep(60);
                }

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    bool dOk = HardwareCommandBus.SetDpi(curDev, targetDpi, targetDpiY, targetStage);
                    if (dOk) break;
                    Thread.Sleep(60);
                }

                this.BeginInvoke(new Action(() => {
                    curDev.PollingRate = targetHz;
                    if (lastInfo != null && lastInfo.DeviceId == curDev.DeviceId) lastInfo.PollingRate = targetHz;
                    curDev.Dpi = targetDpi;
                    curDev.DpiY = targetDpiY;
                    curDev.DpiStage = targetStage;

                    if (isEntering)
                    {
                        if (desktopDpiStage > 0) HighlightDpiStage(desktopDpiStage);
                        UpdateGameDpiButtonSelection(targetStage);
                        UpdateGameRateButtonSelection(targetHz);
                    }
                    else
                    {
                        if (isGamingActive && autoGameDpiEnabled) { if (desktopDpiStage > 0) HighlightDpiStage(desktopDpiStage); UpdateGameDpiButtonSelection(targetStage); } else { HighlightDpiStage(targetStage); }
                        UpdateDesktopRateButtonSelection(targetHz);
                    }

                    UpdateStatusDisplay();
                    UpdatePollingRateDisplayStates();

                    if (gameRateNotifyOsd && osdForm != null)
                    {
                        int stCount = (dpiStageValues != null) ? dpiStageValues.Length : 5;
                        osdForm.ShowGameOptimization(isEntering, true, targetDpi, targetDpiY, targetStage, stCount, true, targetHz, brand);
                    }

                    if (gameRateNotifyToast)
                    {
                        if (isEntering)
                        {
                            SafeShowBalloonTip(3000, "FerrisPulse · 电竞加速已激活",
                                string.Format("检测到电竞游戏运行，回报率提升至 {0} Hz，DPI 切换至第 {1} 档 ({2} DPI)", targetHz, targetStage, targetDpi),
                                ToolTipIcon.Info);
                        }
                        else
                        {
                            SafeShowBalloonTip(3000, "FerrisPulse · 常规桌面状态",
                                string.Format("已切回桌面，回报率恢复至 {0} Hz，DPI 恢复至第 {1} 档 ({2} DPI)", targetHz, targetStage, targetDpi),
                                ToolTipIcon.Info);
                        }
                    }

                    if (gameRateNotifySound)
                    {
                        if (isEntering) PlayAscendingChime();
                        else PlayDescendingChime();
                    }
                }));
            }) { IsBackground = true }.Start();
        }

        private void ApplyGamePollingRate()
        {
            var curDev = GetActiveMouseDevice();
            if (curDev == null) return;
            int targetHz = gamePollingRate;
            string brand = curDev.Brand;

            new Thread(() => {
                bool ok = false;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    ok = HardwareCommandBus.SetPollingRate(curDev, targetHz);                    if (ok) break;
                    Thread.Sleep(80);
                }

                if (ok)
                {
                    this.BeginInvoke(new Action(() => {
                        curDev.PollingRate = targetHz;
                        if (lastInfo != null && lastInfo.DeviceId == curDev.DeviceId) lastInfo.PollingRate = targetHz;

                        UpdateStatusDisplay();
                        UpdatePollingRateDisplayStates();

                        if (gameRateNotifyOsd && osdForm != null)
                        {
                            if (autoGameDpiEnabled)
                            {
                                int gameDpiVal = (dpiStageValues != null && gameDpiStage - 1 < dpiStageValues.Length) ? dpiStageValues[gameDpiStage - 1] : 800;
                                osdForm.ShowGameOptimization(true, false, 0, 0, 0, 0, true, targetHz, brand);
                            }
                            else
                            {
                                osdForm.ShowGameOptimization(true, false, 0, 0, 0, 0, true, targetHz, brand);
                            }
                        }

                        if (gameRateNotifyToast)
                        {
                            if (autoGameDpiEnabled)
                            {
                                int gameDpiVal = (dpiStageValues != null && gameDpiStage - 1 < dpiStageValues.Length) ? dpiStageValues[gameDpiStage - 1] : 800;
                                SafeShowBalloonTip(3000, "FerrisPulse · 电竞加速已激活",
                                    string.Format("检测到电竞游戏运行，回报率提升至 {0} Hz，DPI 切换至第 {1} 档 ({2} DPI)", targetHz, gameDpiStage, gameDpiVal),
                                    ToolTipIcon.Info);
                            }
                            else
                            {
                                SafeShowBalloonTip(3000, "FerrisPulse · 电竞回报率",
                                    string.Format("检测到电竞游戏运行，已自动将鼠标回报率提升至 {0} Hz", targetHz),
                                    ToolTipIcon.Info);
                            }
                        }

                        if (gameRateNotifySound)
                        {
                            PlayAscendingChime();
                        }
                    }));
                }
            }) { IsBackground = true }.Start();
        }

        private void RestoreDesktopPollingRate()
        {
            var curDev = GetActiveMouseDevice();
            if (curDev == null) return;
            int targetHz = desktopPollingRate;
            string brand = curDev.Brand;

            new Thread(() => {
                bool ok = false;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    ok = HardwareCommandBus.SetPollingRate(curDev, targetHz);                    if (ok) break;
                    Thread.Sleep(80);
                }

                if (ok)
                {
                    this.BeginInvoke(new Action(() => {
                        curDev.PollingRate = targetHz;
                        if (lastInfo != null && lastInfo.DeviceId == curDev.DeviceId) lastInfo.PollingRate = targetHz;

                        UpdateStatusDisplay();
                        UpdatePollingRateDisplayStates();

                        if (gameRateNotifyOsd && osdForm != null)
                        {
                            if (autoGameDpiEnabled && curDev.DpiStage > 0)
                            {
                                osdForm.ShowGameOptimization(false, false, 0, 0, 0, 0, true, targetHz, brand);
                            }
                            else
                            {
                                osdForm.ShowGameOptimization(false, false, 0, 0, 0, 0, true, targetHz, brand);
                            }
                        }

                        if (gameRateNotifyToast)
                        {
                            if (autoGameDpiEnabled && curDev.DpiStage > 0)
                            {
                                SafeShowBalloonTip(3000, "FerrisPulse · 常规桌面",
                                    string.Format("已切回桌面，回报率恢复至 {0} Hz，DPI 恢复至第 {1} 档 ({2} DPI)", targetHz, curDev.DpiStage, curDev.Dpi),
                                    ToolTipIcon.Info);
                            }
                            else
                            {
                                SafeShowBalloonTip(3000, "FerrisPulse · 常规回报率",
                                    string.Format("已切回桌面，鼠标回报率已恢复至 {0} Hz 节能状态", targetHz),
                                    ToolTipIcon.Info);
                            }
                        }

                        if (gameRateNotifySound)
                        {
                            PlayDescendingChime();
                        }
                    }));
                }
            }) { IsBackground = true }.Start();
        }

        private void ApplyGameDpiStage()
        {
            var curDev = GetActiveMouseDevice();
            if (curDev == null) return;

            // Remember current desktop stage if not already remembered
            if (desktopDpiStage <= 0)
            {
                desktopDpiStage = (curDev.DpiStage > 0) ? curDev.DpiStage : 1;
            }

            int targetStage = gameDpiStage;
            if (targetStage < 1 || targetStage > 5) targetStage = 1;

            int targetDpi = (dpiStageValues != null && targetStage - 1 < dpiStageValues.Length) ? dpiStageValues[targetStage - 1] : 800;
            int targetDpiY = (curDev.DpiStagesY != null && curDev.DpiStagesY.Length >= targetStage) ? curDev.DpiStagesY[targetStage - 1] : targetDpi;
            string brand = curDev.Brand;
            SyncMonitoredDpiState(targetDpi, targetStage, curDev.DeviceId);

            new Thread(() => {
                bool ok = false;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    ok = HardwareCommandBus.SetDpi(curDev, targetDpi, targetDpiY, targetStage);                    if (ok) break;
                    Thread.Sleep(80);
                }

                if (ok)
                {
                    this.BeginInvoke(new Action(() => {
                        curDev.Dpi = targetDpi;
                        curDev.DpiStage = targetStage;
                        if (isGamingActive && autoGameDpiEnabled) { if (desktopDpiStage > 0) HighlightDpiStage(desktopDpiStage); UpdateGameDpiButtonSelection(targetStage); } else { HighlightDpiStage(targetStage); }
                        UpdateStatusDisplay();
                        UpdatePollingRateDisplayStates();

                        if (!autoGameRateEnabled)
                        {
                            if (gameRateNotifyOsd && osdForm != null)
                            {
                                int effTargetDpiY = (curDev != null && curDev.DpiStagesY != null && curDev.DpiStagesY.Length >= targetStage) ? curDev.DpiStagesY[targetStage - 1] : targetDpi; if (curDev != null) curDev.DpiY = effTargetDpiY; osdForm.ShowGameOptimization(isGamingActive, true, targetDpi, effTargetDpiY, targetStage, dpiStageValues != null ? dpiStageValues.Length : 5, false, 0, brand);
                            }

                            if (gameRateNotifyToast)
                            {
                                SafeShowBalloonTip(3000, "FerrisPulse · 电竞 DPI",
                                    string.Format("检测到电竞游戏运行，已自动将鼠标 DPI 切换至第 {0} 档 ({1} DPI)", targetStage, targetDpi),
                                    ToolTipIcon.Info);
                            }

                            if (gameRateNotifySound)
                            {
                                PlayAscendingChime();
                            }
                        }
                    }));
                }
            }) { IsBackground = true }.Start();
        }

        private void RestoreDesktopDpiStage()
        {
            var curDev = GetActiveMouseDevice();
            if (curDev == null || desktopDpiStage <= 0) return;

            int targetStage = desktopDpiStage;
            if (targetStage < 1 || targetStage > 5) targetStage = 1;

            int targetDpi = (dpiStageValues != null && targetStage - 1 < dpiStageValues.Length) ? dpiStageValues[targetStage - 1] : 1600;
            int targetDpiY = (curDev.DpiStagesY != null && curDev.DpiStagesY.Length >= targetStage) ? curDev.DpiStagesY[targetStage - 1] : targetDpi;
            string brand = curDev.Brand;
            int prevStage = desktopDpiStage;
            desktopDpiStage = 0; // reset memory
            SyncMonitoredDpiState(targetDpi, targetStage, curDev.DeviceId);

            new Thread(() => {
                bool ok = false;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    ok = HardwareCommandBus.SetDpi(curDev, targetDpi, targetDpiY, targetStage);                    if (ok) break;
                    Thread.Sleep(80);
                }

                if (ok)
                {
                    this.BeginInvoke(new Action(() => {
                        curDev.Dpi = targetDpi;
                        curDev.DpiStage = targetStage;
                        if (isGamingActive && autoGameDpiEnabled) { if (desktopDpiStage > 0) HighlightDpiStage(desktopDpiStage); UpdateGameDpiButtonSelection(targetStage); } else { HighlightDpiStage(targetStage); }
                        UpdateStatusDisplay();
                        UpdatePollingRateDisplayStates();

                        if (!autoGameRateEnabled)
                        {
                            if (gameRateNotifyOsd && osdForm != null)
                            {
                                int effTargetDpiY = (curDev != null && curDev.DpiStagesY != null && curDev.DpiStagesY.Length >= targetStage) ? curDev.DpiStagesY[targetStage - 1] : targetDpi; if (curDev != null) curDev.DpiY = effTargetDpiY; osdForm.ShowGameOptimization(isGamingActive, true, targetDpi, effTargetDpiY, targetStage, dpiStageValues != null ? dpiStageValues.Length : 5, false, 0, brand);
                            }

                            if (gameRateNotifyToast)
                            {
                                SafeShowBalloonTip(3000, "FerrisPulse · 常规 DPI",
                                    string.Format("已切回桌面，鼠标 DPI 已恢复至第 {0} 档 ({1} DPI) 办公状态", prevStage, targetDpi),
                                    ToolTipIcon.Info);
                            }

                            if (gameRateNotifySound)
                            {
                                PlayDescendingChime();
                            }
                        }
                    }));
                }
            }) { IsBackground = true }.Start();
        }

        private static SoundPlayer ascendingPlayer = null;
        private static SoundPlayer descendingPlayer = null;
        private static readonly object chimeSyncLock = new object();

        private static byte[] GenerateBellChimeWav(double[] freqs, double[] durations)
        {
            try
            {
                int sampleRate = 44100;
                int totalSamples = 0;
                for (int i = 0; i < durations.Length; i++) totalSamples += (int)(sampleRate * durations[i]);
                int silenceSamples = (int)(sampleRate * 0.15); // 150ms 尾部静音缓冲，彻底根除音频驱动提前截断
                totalSamples += silenceSamples;
                int dataChunkSize = totalSamples * 2;

                using (MemoryStream ms = new MemoryStream())
                {
                    using (BinaryWriter bw = new BinaryWriter(ms))
                    {
                        bw.Write(new char[] { 'R', 'I', 'F', 'F' });
                        bw.Write(36 + dataChunkSize);
                        bw.Write(new char[] { 'W', 'A', 'V', 'E' });
                        bw.Write(new char[] { 'f', 'm', 't', ' ' });
                        bw.Write(16);
                        bw.Write((short)1); // PCM
                        bw.Write((short)1); // Mono
                        bw.Write(sampleRate);
                        bw.Write(sampleRate * 2);
                        bw.Write((short)2);
                        bw.Write((short)16);
                        bw.Write(new char[] { 'd', 'a', 't', 'a' });
                        bw.Write(dataChunkSize);

                        for (int n = 0; n < freqs.Length; n++)
                        {
                            double f = freqs[n];
                            int nSamples = (int)(sampleRate * durations[n]);
                            double decayRate = (n == 0) ? 10.0 : 5.0;
                            for (int i = 0; i < nSamples; i++)
                            {
                                double t = (double)i / sampleRate;
                                double env = (t < 0.005) ? (t / 0.005) : Math.Exp(-(t - 0.005) * decayRate);
                                double remTime = durations[n] - t;
                                if (remTime < 0.025)
                                {
                                    env *= (remTime / 0.025);
                                }
                                double s = (Math.Sin(2.0 * Math.PI * f * t) * 0.75 +
                                            Math.Sin(4.0 * Math.PI * f * t) * 0.20 +
                                            Math.Sin(6.0 * Math.PI * f * t) * 0.05) * env;
                                short sample = (short)(s * 9600);
                                bw.Write(sample);
                            }
                        }

                        // 填充 150ms 纯零静音缓冲
                        for (int i = 0; i < silenceSamples; i++)
                        {
                            bw.Write((short)0);
                        }

                        bw.Flush();
                        return ms.ToArray();
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        private static void PlayAscendingChime()
        {
            ThreadPool.QueueUserWorkItem(_ => {
                try
                {
                    lock (chimeSyncLock)
                    {
                        if (ascendingPlayer == null)
                        {
                            byte[] wav = GenerateBellChimeWav(new double[] { 784.0, 1046.5 }, new double[] { 0.10, 0.26 });
                            if (wav != null)
                            {
                                var ms = new MemoryStream(wav);
                                ascendingPlayer = new SoundPlayer(ms);
                                ascendingPlayer.Load();
                            }
                        }
                        if (ascendingPlayer != null)
                        {
                            ascendingPlayer.PlaySync();
                        }
                        else
                        {
                            System.Media.SystemSounds.Asterisk.Play();
                        }
                    }
                }
                catch
                {
                    try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
                }
            });
        }

        private static void PlayDescendingChime()
        {
            ThreadPool.QueueUserWorkItem(_ => {
                try
                {
                    lock (chimeSyncLock)
                    {
                        if (descendingPlayer == null)
                        {
                            byte[] wav = GenerateBellChimeWav(new double[] { 1046.5, 784.0 }, new double[] { 0.10, 0.26 });
                            if (wav != null)
                            {
                                var ms = new MemoryStream(wav);
                                descendingPlayer = new SoundPlayer(ms);
                                descendingPlayer.Load();
                            }
                        }
                        if (descendingPlayer != null)
                        {
                            descendingPlayer.PlaySync();
                        }
                        else
                        {
                            System.Media.SystemSounds.Asterisk.Play();
                        }
                    }
                }
                catch
                {
                    try { System.Media.SystemSounds.Asterisk.Play(); } catch { }
                }
            });
        }

        private void SetAsPrimaryDevice(string deviceId)
        {
            DeviceManager.SetConfiguredPrimaryDeviceId(deviceId);
            var prio = DeviceManager.GetPriorityList();
            prio.Remove(deviceId);
            prio.Insert(0, deviceId);
            DeviceManager.SavePriorityList(prio);

            if (activeDevices != null)
            {
                foreach (var d in activeDevices)
                {
                    d.IsPrimary = string.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase);
                }
            }
            UpdateDeviceTabsUI();
            if (selectedDevice != null)
            {
                UpdatePrimaryPinButton(selectedDevice);
            }
            var primary = GetPrimaryDevice();
            if (primary != null)
            {
                lastInfo = primary;
                UpdateTrayIcon(primary.BatteryPercent, primary.IsCharging, primary.IsConnected, primary.IsSleeping);
                UpdateStatusDisplay();
            }
            UpdateTrayDeviceMenuItems();
        }

        private MouseBatteryInfo GetPrimaryDevice()
        {
            if (activeDevices == null || activeDevices.Count == 0) return lastInfo;
            var p = activeDevices.Find(d => d.IsPrimary);
            return p ?? activeDevices[0];
        }

        private void UpdatePrimaryPinButton(MouseBatteryInfo dev)
        {
            if (badgePrimary == null) return;
            if (dev == null)
            {
                badgePrimary.Visible = false;
                return;
            }

            badgePrimary.Visible = dev.IsPrimary;
            if (dev.IsPrimary)
            {
                badgePrimary.SetStatus("● 任务栏常驻", dev.BrandColor);
            }

            if (lblDeviceName != null)
            {
                int rightBound = badgePrimary.Visible ? badgePrimary.Left : (cardBattery.ClientSize.Width - (int)(16 * dpiScale));
                lblDeviceName.Size = new Size(Math.Max(50, rightBound - lblDeviceName.Left - (int)(10 * dpiScale)), lblDeviceName.Height);
            }
        }

        private void UpdateDeviceTabsUI()
        {
            if (focusDeviceSelector == null) return;
            var dev = selectedDevice ?? GetPrimaryDevice();
            if (dev != null)
            {
                focusDeviceSelector.DeviceIcon = dev.CategoryIcon;
                focusDeviceSelector.Category = dev.Category;
                focusDeviceSelector.DeviceName = dev.DisplayName;
                focusDeviceSelector.AccentColor = dev.BrandColor;
                string battText = "--%";
                if (dev.IsConnected)
                {
                    bool isWired = dev.Category == DeviceCategory.Keyboard && dev.Transport != null && 
                                   (dev.Transport.IndexOf("有线", StringComparison.OrdinalIgnoreCase) >= 0 || dev.Transport.IndexOf("USB", StringComparison.OrdinalIgnoreCase) >= 0);
                    if (isWired)
                    {
                        battText = "⚡ 供电中";
                    }
                    else if (dev.Brand == "NuPhy" && dev.Category == DeviceCategory.Keyboard && (dev.BatteryPercent >= 100 || dev.IsCharging))
                    {
                        battText = "⚡ 充电中";
                    }
                    else if (dev.BatteryPercent >= 0)
                    {
                        battText = dev.BatteryPercent + "%";
                    }
                    else if (dev.Category == DeviceCategory.Keyboard)
                    {
                        battText = "免驱";
                    }
                }
                focusDeviceSelector.BatteryText = battText;
                focusDeviceSelector.StatusText = dev.IsConnected 
                    ? (dev.IsSleeping ? "休眠中" : ((dev.IsCharging || (dev.Brand == "NuPhy" && dev.BatteryPercent >= 100)) ? "充电中" : (dev.Category == DeviceCategory.Keyboard && dev.BatteryPercent < 0 ? "免驱在线" : "已连接"))) 
                    : "已离线";
                focusDeviceSelector.Invalidate();
            }
        }

        public void SelectDevice(string deviceId)
        {
            if (activeDevices != null)
            {
                var target = activeDevices.Find(d => string.Equals(d.DeviceId, deviceId, StringComparison.OrdinalIgnoreCase));
                if (target != null)
                {
                    SelectDeviceView(target);
                }
            }
        }

        private void SelectDeviceView(MouseBatteryInfo dev)
        {
            if (dev == null) return;
            selectedDevice = dev;
            UpdateDeviceTabsUI();
            UpdateUI(dev, false);
            UpdatePrimaryPinButton(dev);
        }

        private void StartBootPolling()
        {
            bootPollCount = 0;
            if (bootPollTimer == null)
            {
                bootPollTimer = new System.Windows.Forms.Timer();
                bootPollTimer.Interval = 1200;
                bootPollTimer.Tick += (s, e) =>
                {
                    bootPollCount++;
                    if (lastInfo == null || lastInfo.IsSleeping || !lastInfo.IsConnected || lastInfo.BatteryPercent <= 0)
                    {
                        ThreadPool.QueueUserWorkItem(_ =>
                        {
                            try
                            {
                                this.BeginInvoke(new Action(() => RefreshBatteryStatus(false)));
                            }
                            catch { }
                        });
                    }
                    if (bootPollCount >= 8 || (lastInfo != null && lastInfo.IsConnected && !lastInfo.IsSleeping && lastInfo.BatteryPercent > 0))
                    {
                        bootPollTimer.Stop();
                    }
                };
            }
            bootPollTimer.Start();
        }

        public bool PauseBackgroundRefresh { get; set; }


        private void RefreshBatteryStatus(bool showTipIfManual)
        {
            if (PauseBackgroundRefresh) return;
            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    var devices = DeviceManager.QueryAllDevices();
                    try
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            if (PauseBackgroundRefresh) return;
                            ApplyDeviceStatus(devices, showTipIfManual);
                        }));
                    }
                    catch { }
                }
                catch (Exception ex)
                {
                    try
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            statusMenuItem.Text = "读取失败: " + ex.Message;
                            trayIcon.Text = "无线鼠标检测异常";
                            UpdateTrayIcon(-1, false, false);
                        }));
                    }
                    catch { }
                }
            });
        }

        private void ApplyDeviceStatus(List<MouseBatteryInfo> devices, bool showTipIfManual)
        {
            activeDevices = devices;

            if (devices != null && devices.Count > 0)
            {
                if (selectedDevice != null)
                {
                    var match = devices.Find(d => string.Equals(d.DeviceId, selectedDevice.DeviceId, StringComparison.OrdinalIgnoreCase));
                    selectedDevice = match ?? devices.Find(d => d.IsPrimary) ?? devices.Find(d => d.IsConnected) ?? devices[0];
                }
                else
                {
                    selectedDevice = devices.Find(d => d.IsPrimary) ?? devices.Find(d => d.IsConnected) ?? devices[0];
                }

                var primary = GetPrimaryDevice();
                lastInfo = primary;

                UpdateDeviceTabsUI();
                UpdateUI(selectedDevice, showTipIfManual);
                UpdatePrimaryPinButton(selectedDevice);

                UpdateTrayIcon(primary.BatteryPercent, primary.IsCharging, primary.IsConnected, primary.IsSleeping);
                UpdateStatusDisplay();
                UpdateTrayDeviceMenuItems();
                if (selectedDevice != null)
                {
                    PerformStartupDesktopCalibration(selectedDevice);
                }
            }
            else
            {
                selectedDevice = null;
                lastInfo = null;
                UpdateDeviceTabsUI();
                UpdateUI(null, showTipIfManual);
                UpdateTrayDeviceMenuItems();
            }
        }

        private void UpdateStatusDisplay()
        {
            if (lastInfo == null || !lastInfo.IsConnected) return;

            string brandTag = lastInfo.Brand == "Rapoo" ? "雷柏" : (lastInfo.Brand == "VGN" ? "VGN" : (lastInfo.Brand == "Razer" ? "雷蛇" : (lastInfo.Brand == "NuPhy" ? "NuPhy" : (lastInfo.Category == DeviceCategory.Headset ? "耳机" : (lastInfo.Category == DeviceCategory.Gamepad ? "手柄" : (lastInfo.Category == DeviceCategory.Keyboard ? "键盘" : "外设"))))));
            string dpiPart = (lastInfo.Category != DeviceCategory.Keyboard && lastInfo.Dpi > 0) ? (" · " + lastInfo.Dpi + " DPI") : "";
            string ratePart = (lastInfo.Category != DeviceCategory.Keyboard && lastInfo.PollingRate > 0) ? (" · " + lastInfo.PollingRate + "Hz") : "";
            string chgPart = lastInfo.IsSleeping ? "休眠待机" : (lastInfo.IsCharging ? "充电中" : "电池供电");
            string chgStr = lastInfo.IsSleeping ? "休眠待机 (移动唤醒)" : (lastInfo.IsCharging ? "正在充电" : "电池供电");

            if (lastInfo.Brand == "NuPhy" && lastInfo.Category == DeviceCategory.Keyboard && !lastInfo.IsCharging && !lastInfo.IsSleeping)
            {
                if (lastInfo.BatteryPercent >= 100)
                {
                    chgPart = "充电中 · 电量充足";
                    chgStr = "充电中 · 电量充足";
                }
                else
                {
                    chgPart = "无线状态";
                    chgStr = "无线供电";
                }
            }

            string menuStatus;
            string tipText;
            string timeStr = lastInfo.LastUpdated.ToString("HH:mm:ss");

            string devDisplayName = lastInfo.DisplayName;

            if (lastInfo.Category == DeviceCategory.Keyboard && lastInfo.BatteryPercent < 0)
            {
                menuStatus = string.Format("[{0}] {1} (免驱在线)", brandTag, devDisplayName);
                tipText = string.Format("FerrisPulse · 灵脉\n[{0}] {1}\n状态: 免驱在线\n最后同步: {2}", brandTag, devDisplayName, timeStr);
            }
            else if (lastInfo.Brand == "NuPhy" && lastInfo.Category == DeviceCategory.Keyboard && lastInfo.BatteryPercent >= 100 && !lastInfo.IsSleeping)
            {
                menuStatus = string.Format("[{0}] {1} (供电中)", brandTag, devDisplayName);
                tipText = string.Format("FerrisPulse · 灵脉\n[{0}] {1}\n状态: 2.4G 供电中\n最后同步: {2}", brandTag, devDisplayName, timeStr);
            }
            else
            {
                menuStatus = string.Format("[{0}] {1} ({2}% · {3})", brandTag, devDisplayName, lastInfo.BatteryPercent, chgPart);
                tipText = string.Format("FerrisPulse · 灵脉\n[{0}] {1} · {2}%\n状态: {3}{4}{5}\n最后同步: {6}",
                    brandTag, devDisplayName, lastInfo.BatteryPercent, chgStr, dpiPart, ratePart, timeStr);
            }
            statusMenuItem.Text = menuStatus;

            if (tipText.Length > 63)
            {
                tipText = string.Format("[{0}] {1}: {2}%\n{3}{4}", brandTag, devDisplayName, lastInfo.BatteryPercent, chgPart, dpiPart);
                if (tipText.Length > 63)
                {
                    tipText = string.Format("电量: {0}% ({1})", lastInfo.BatteryPercent, chgPart);
                }
            }
            trayIcon.Text = tipText;
        }

        private void HideNuphyControls()
        {
            if (btnNuphyAutoSleep != null) btnNuphyAutoSleep.Visible = false;
            if (btnNuphyWinLock != null) btnNuphyWinLock.Visible = false;
            if (btnNuphyBacklight != null) btnNuphyBacklight.Visible = false;
            if (btnNuphySidelight != null) btnNuphySidelight.Visible = false;
            if (lblNuphyBrightTitle != null) lblNuphyBrightTitle.Visible = false;
            if (sliderNuphyBrightness != null) sliderNuphyBrightness.Visible = false;
            if (lblNuphyBrightVal != null) lblNuphyBrightVal.Visible = false;
        }

        private void HideAllAutoGameControls()
        {
            if (chkAutoGameOpt != null) chkAutoGameOpt.Visible = false;
            if (chkAutoGameRate != null) chkAutoGameRate.Visible = false;
            if (chkAutoGameDpi != null) chkAutoGameDpi.Visible = false;
            if (btnManageGames != null) btnManageGames.Visible = false;
            if (lblLiveRateBadge != null) lblLiveRateBadge.Visible = false;
            if (lblGameRateTitle != null) lblGameRateTitle.Visible = false;
            if (lblGameDpiTitle != null) lblGameDpiTitle.Visible = false;
            if (btnGameRates != null)
            {
                for (int i = 0; i < btnGameRates.Length; i++)
                {
                    if (btnGameRates[i] != null) btnGameRates[i].Visible = false;
                }
            }
            if (btnGameDpiStages != null)
            {
                for (int i = 0; i < btnGameDpiStages.Length; i++)
                {
                    if (btnGameDpiStages[i] != null) btnGameDpiStages[i].Visible = false;
                }
            }
            if (lblNotifyModeTitle != null) lblNotifyModeTitle.Visible = false;
            if (chkNotifyOsd != null) chkNotifyOsd.Visible = false;
            if (chkNotifyToast != null) chkNotifyToast.Visible = false;
            if (chkNotifySound != null) chkNotifySound.Visible = false;
            if (autoGameRateMenu != null) autoGameRateMenu.Visible = false;
        }

        private void ApplyNuphySettingsToUI()
        {
            if (selectedDevice == null || selectedDevice.Brand != "NuPhy")
            {
                HideNuphyControls();
                return;
            }
            var s = NuphyDeviceHelper.CachedHardwareSettings;
            if (s == null) return;

            Color cyan = Color.FromArgb(0, 229, 255);
            Color textDim = Color.FromArgb(145, 155, 175);

            if (btnNuphyAutoSleep != null)
            {
                btnNuphyAutoSleep.Visible = true;
                if (s.SleepEnabled)
                {
                    btnNuphyAutoSleep.Text = "🌙 自动休眠: 开启";
                    btnNuphyAutoSleep.NormalColor = Color.FromArgb(40, 0, 229, 255);
                    btnNuphyAutoSleep.HoverColor = Color.FromArgb(55, 0, 229, 255);
                    btnNuphyAutoSleep.PressedColor = Color.FromArgb(65, 0, 229, 255);
                    btnNuphyAutoSleep.BorderColor = cyan;
                    btnNuphyAutoSleep.ForeColor = cyan;
                }
                else
                {
                    btnNuphyAutoSleep.Text = "🌙 自动休眠: 关闭";
                    btnNuphyAutoSleep.NormalColor = Color.FromArgb(28, 32, 42);
                    btnNuphyAutoSleep.HoverColor = Color.FromArgb(40, 46, 60);
                    btnNuphyAutoSleep.PressedColor = Color.FromArgb(22, 26, 34);
                    btnNuphyAutoSleep.BorderColor = Color.FromArgb(60, 68, 86);
                    btnNuphyAutoSleep.ForeColor = Color.FromArgb(175, 185, 205);
                }
            }

            if (btnNuphyWinLock != null)
            {
                btnNuphyWinLock.Visible = true;
                if (s.WinLock)
                {
                    btnNuphyWinLock.Text = "🔒 Win键锁定: 开启";
                    btnNuphyWinLock.NormalColor = Color.FromArgb(40, 0, 229, 255);
                    btnNuphyWinLock.HoverColor = Color.FromArgb(55, 0, 229, 255);
                    btnNuphyWinLock.PressedColor = Color.FromArgb(65, 0, 229, 255);
                    btnNuphyWinLock.BorderColor = cyan;
                    btnNuphyWinLock.ForeColor = cyan;
                }
                else
                {
                    btnNuphyWinLock.Text = "🔓 Win键锁定: 关闭";
                    btnNuphyWinLock.NormalColor = Color.FromArgb(28, 32, 42);
                    btnNuphyWinLock.HoverColor = Color.FromArgb(40, 46, 60);
                    btnNuphyWinLock.PressedColor = Color.FromArgb(22, 26, 34);
                    btnNuphyWinLock.BorderColor = Color.FromArgb(60, 68, 86);
                    btnNuphyWinLock.ForeColor = Color.FromArgb(175, 185, 205);
                }
            }

            if (btnNuphyBacklight != null)
            {
                btnNuphyBacklight.Visible = true;
                bool on = s.BacklightEnabled && s.BacklightBrightness > 0;
                if (on)
                {
                    btnNuphyBacklight.Text = "💡 键盘背光: 开";
                    btnNuphyBacklight.NormalColor = Color.FromArgb(40, 0, 229, 255);
                    btnNuphyBacklight.HoverColor = Color.FromArgb(55, 0, 229, 255);
                    btnNuphyBacklight.PressedColor = Color.FromArgb(65, 0, 229, 255);
                    btnNuphyBacklight.BorderColor = cyan;
                    btnNuphyBacklight.ForeColor = cyan;
                }
                else
                {
                    btnNuphyBacklight.Text = "💡 键盘背光: 关";
                    btnNuphyBacklight.NormalColor = Color.FromArgb(28, 32, 42);
                    btnNuphyBacklight.HoverColor = Color.FromArgb(40, 46, 60);
                    btnNuphyBacklight.PressedColor = Color.FromArgb(22, 26, 34);
                    btnNuphyBacklight.BorderColor = Color.FromArgb(60, 68, 86);
                    btnNuphyBacklight.ForeColor = Color.FromArgb(175, 185, 205);
                }
            }

            if (btnNuphySidelight != null)
            {
                btnNuphySidelight.Visible = true;
                bool on = s.SidelightEnabled && s.SidelightBrightness > 0;
                if (on)
                {
                    btnNuphySidelight.Text = "✨ 前置灯带: 开";
                    btnNuphySidelight.NormalColor = Color.FromArgb(40, 0, 229, 255);
                    btnNuphySidelight.HoverColor = Color.FromArgb(55, 0, 229, 255);
                    btnNuphySidelight.PressedColor = Color.FromArgb(65, 0, 229, 255);
                    btnNuphySidelight.BorderColor = cyan;
                    btnNuphySidelight.ForeColor = cyan;
                }
                else
                {
                    btnNuphySidelight.Text = "✨ 前置灯带: 关";
                    btnNuphySidelight.NormalColor = Color.FromArgb(28, 32, 42);
                    btnNuphySidelight.HoverColor = Color.FromArgb(40, 46, 60);
                    btnNuphySidelight.PressedColor = Color.FromArgb(22, 26, 34);
                    btnNuphySidelight.BorderColor = Color.FromArgb(60, 68, 86);
                    btnNuphySidelight.ForeColor = Color.FromArgb(175, 185, 205);
                }
            }

            if (lblNuphyBrightTitle != null) lblNuphyBrightTitle.Visible = true;
            int curB = s.BacklightEnabled ? s.BacklightBrightness : 0;
            if (sliderNuphyBrightness != null)
            {
                sliderNuphyBrightness.Visible = true;
                sliderNuphyBrightness.SetValueWithoutEvents(curB);
            }
            if (lblNuphyBrightVal != null)
            {
                lblNuphyBrightVal.Visible = true;
                lblNuphyBrightVal.Text = curB + "%";
            }
        }

        private void QueueNuphyBrightnessThrottled(int val)
        {
            nuphyTargetBrightness = val;
            lock (nuphyBrightnessSyncRoot)
            {
                if (!isNuphyBrightnessWorkerActive)
                {
                    isNuphyBrightnessWorkerActive = true;
                    ThreadPool.QueueUserWorkItem(_ =>
                    {
                        try
                        {
                            while (true)
                            {
                                int target;
                                lock (nuphyBrightnessSyncRoot)
                                {
                                    target = nuphyTargetBrightness;
                                    nuphyTargetBrightness = -1;
                                }

                                if (target >= 0)
                                {
                                    HardwareCommandBus.ExecuteCustomAction(selectedDevice ?? GetPrimaryDevice(), "backlight_brightness", target);
                                }

                                Thread.Sleep(90); // ~11Hz 黄金平滑节流：物理背光平滑渐变，同时彻底避免 2.4G 射频拥塞与 I/O 卡顿

                                lock (nuphyBrightnessSyncRoot)
                                {
                                    if (nuphyTargetBrightness < 0)
                                    {
                                        isNuphyBrightnessWorkerActive = false;
                                        break;
                                    }
                                }
                            }
                        }
                        catch
                        {
                            lock (nuphyBrightnessSyncRoot)
                            {
                                isNuphyBrightnessWorkerActive = false;
                            }
                        }
                    });
                }
            }
        }

        private bool isSyncingPhysicalState = false;

        private void SyncAllDevicesPhysicalState()
        {
            if (isSyncingPhysicalState) return;
            isSyncingPhysicalState = true;

            RefreshBatteryStatus(false);

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    NuphyDeviceHelper.NuphyHardwareSettings hwSettings;
                    bool ok = NuphyDeviceHelper.GetHardwareSettings(out hwSettings);
                    if (ok && hwSettings != null)
                    {
                        try
                        {
                            this.BeginInvoke((Action)(() =>
                            {
                                ApplyNuphySettingsToUI();
                            }));
                        }
                        catch { }
                    }
                }
                catch { }
                finally
                {
                    isSyncingPhysicalState = false;
                }
            });
        }

        private void UpdatePerformanceSection(MouseBatteryInfo info, Color brandAccent)
        {
            if (info == null) return;
            bool prevUpdatingUI = isUpdatingUI;
            isUpdatingUI = true;
            try
            {
                selectedDevice = info;

            HideNuphyControls();

            if (tileHeadsetAudio != null) tileHeadsetAudio.Visible = false;
            if (tileRapooDpi != null) tileRapooDpi.Visible = false;
            if (btnRateManage != null) btnRateManage.Visible = false;
            if (btnGamepadVibrate != null) btnGamepadVibrate.Visible = false;
            if (btnGamepadJoyCpl != null) btnGamepadJoyCpl.Visible = false;
            if (hudThreeLocks != null) hudThreeLocks.Visible = false;
            if (btnUniversalWinLock != null) btnUniversalWinLock.Visible = false;
            if (btnKeyTestSpeed != null) btnKeyTestSpeed.Visible = false;
            if (btnKeyboardSettings != null) btnKeyboardSettings.Visible = false;
            if (btnAudioSetDefault != null) btnAudioSetDefault.Visible = false;
            if (btnVolumeMixer != null) btnVolumeMixer.Visible = false;
            if (btnAudioSettings != null) btnAudioSettings.Visible = false;
            if (btnMouseRateTest != null) btnMouseRateTest.Visible = false;

            bool isGamepad = (info.Category == DeviceCategory.Gamepad);
            bool isHeadset = (info.Category == DeviceCategory.Headset || info.Category == DeviceCategory.Earbuds);
            bool isKeyboard = (info.Category == DeviceCategory.Keyboard);
            bool isDongle = (info.Category == DeviceCategory.Dongle);
            bool isRapoo = (info.Brand == "Rapoo" || info.Brand == "雷柏");

            bool isFdasDevice = (info != null && info.DeviceId != null && FdasRuntimeHelper.IsFdasDevice(info.DeviceId));
            bool isFdasMouse = (info != null && FdasRuntimeHelper.IsFdasMouse(info));
            bool isLogitech = (info.Brand == "Logitech" || info.Brand == "罗技");
            bool isVgn = (info.Brand == "VGN" && !isFdasDevice);
            bool isRazer = (info.Brand == "Razer" || (info.DeviceId != null && info.DeviceId.StartsWith("Razer:1532:")));
            bool isValkyrieMouse = ((info.Brand == "Valkyrie" || info.Brand == "瓦尔基里") && info.Category == DeviceCategory.Mouse);
            bool isLogitechMouse = (isLogitech && info.Category == DeviceCategory.Mouse);
            bool isRapooMouse = (isRapoo && info.Category == DeviceCategory.Mouse);

            int secW = cardPerformance.ClientSize.Width - (int)(32 * dpiScale);

            HideAllAutoGameControls();

            if (isGamepad)
            {
                AdjustPerformanceCardHeight((int)(168 * dpiScale));
                if (lblPerfTitle != null) lblPerfTitle.Text = "游戏手柄硬件与触觉控制台";

                // Hide Razer interactive controls
                if (lblDpiTitle != null) lblDpiTitle.Visible = false;
                if (btnDpiManage != null) btnDpiManage.Visible = false;
                if (btnDpiStages != null)
                {
                    for (int i = 0; i < btnDpiStages.Length; i++)
                    {
                        if (btnDpiStages[i] != null) btnDpiStages[i].Visible = false;
                    }
                }
                if (lblRateTitle != null) lblRateTitle.Visible = false;
                if (btnRates != null)
                {
                    for (int i = 0; i < btnRates.Length; i++)
                    {
                        if (btnRates[i] != null) btnRates[i].Visible = false;
                    }
                }

                if (tileRapooDpi != null)
                {
                    tileRapooDpi.Visible = true;
                    tileRapooDpi.Size = new Size(secW, (int)(68 * dpiScale));
                    tileRapooDpi.Location = new Point((int)(16 * dpiScale), (int)(38 * dpiScale));
                    tileRapooDpi.AccentColor = brandAccent;
                    tileRapooDpi.Title = "游戏手柄状态";
                    tileRapooDpi.Badge = (info.Transport != null && info.Transport.Contains("蓝牙")) ? "BLE" : "2.4G/USB";
                    if (info.BatteryPercent >= 0)
                    {
                        tileRapooDpi.ValueText = info.BatteryPercent.ToString();
                        tileRapooDpi.UnitText = "%";
                        tileRapooDpi.Footnote = string.Format("连接: {0} | 状态: 在线就绪 | 电池供电", info.Transport ?? "蓝牙");
                    }
                    else
                    {
                        tileRapooDpi.ValueText = info.IsConnected ? "在线" : "离线";
                        tileRapooDpi.UnitText = "";
                        tileRapooDpi.Footnote = string.Format("连接: {0} | 状态: {1}", info.Transport ?? "USB", info.IsConnected ? "在线就绪" : "离线");
                    }
                    tileRapooDpi.Invalidate();
                }

                int btnW = (secW - (int)(8 * dpiScale)) / 2;
                int btnH = (int)(32 * dpiScale);
                int btnY = (int)(116 * dpiScale);

                if (btnGamepadVibrate != null)
                {
                    btnGamepadVibrate.Visible = true;
                    btnGamepadVibrate.SetBounds((int)(16 * dpiScale), btnY, btnW, btnH);
                }
                if (btnGamepadJoyCpl != null)
                {
                    btnGamepadJoyCpl.Visible = true;
                    btnGamepadJoyCpl.SetBounds((int)(16 * dpiScale) + btnW + (int)(8 * dpiScale), btnY, btnW, btnH);
                }

                if (dpiMenu != null) dpiMenu.Visible = false;
                if (rateMenu != null) rateMenu.Visible = false;
            }
            else if (isHeadset)
            {
                AdjustPerformanceCardHeight((int)(196 * dpiScale));
                if (lblPerfTitle != null)
                {
                    lblPerfTitle.Text = (info.Category == DeviceCategory.Earbuds) ? "入耳式无线耳机 · 音频控制台" : "无线耳机 · 音频控制台";
                }

                // Hide Razer interactive controls
                if (lblDpiTitle != null) lblDpiTitle.Visible = false;
                if (btnDpiManage != null) btnDpiManage.Visible = false;
                if (btnDpiStages != null)
                {
                    for (int i = 0; i < btnDpiStages.Length; i++)
                    {
                        if (btnDpiStages[i] != null) btnDpiStages[i].Visible = false;
                    }
                }
                if (lblRateTitle != null) lblRateTitle.Visible = false;
                if (btnRates != null)
                {
                    for (int i = 0; i < btnRates.Length; i++)
                    {
                        if (btnRates[i] != null) btnRates[i].Visible = false;
                    }
                }

                // Show Headset Audio Tile
                if (tileHeadsetAudio != null)
                {
                    tileHeadsetAudio.Visible = true;
                    tileHeadsetAudio.Size = new Size(secW, (int)(64 * dpiScale));
                    tileHeadsetAudio.Location = new Point((int)(16 * dpiScale), (int)(36 * dpiScale));
                    tileHeadsetAudio.AccentColor = brandAccent;
                    tileHeadsetAudio.Title = (info.Category == DeviceCategory.Earbuds) ? "TWS 入耳式无线耳机" : "无线音频设备";
                    tileHeadsetAudio.StatusText = info.IsConnected ? (info.BatteryPercent >= 0 ? string.Format("已连接 · 剩余电量 {0}%", info.BatteryPercent) : "已连接就绪") : "已离线 / 未连接";
                    tileHeadsetAudio.Footnote = string.Format("通道: {0} · Windows 原生音频端点", info.Transport ?? "蓝牙");
                    tileHeadsetAudio.Invalidate();
                }

                int btnW = (secW - (int)(8 * dpiScale)) / 2;
                int btnH = (int)(32 * dpiScale);
                int row1Y = (int)(110 * dpiScale);
                int row2Y = (int)(150 * dpiScale);

                if (btnAudioSetDefault != null)
                {
                    btnAudioSetDefault.Visible = true;
                    btnAudioSetDefault.SetBounds((int)(16 * dpiScale), row1Y, btnW, btnH);
                }
                if (btnVolumeMixer != null)
                {
                    btnVolumeMixer.Visible = true;
                    btnVolumeMixer.SetBounds((int)(16 * dpiScale) + btnW + (int)(8 * dpiScale), row1Y, btnW, btnH);
                }
                if (btnAudioSettings != null)
                {
                    btnAudioSettings.Visible = true;
                    btnAudioSettings.SetBounds((int)(16 * dpiScale), row2Y, secW, btnH);
                }

                if (dpiMenu != null) dpiMenu.Visible = false;
                if (rateMenu != null) rateMenu.Visible = false;
            }
            else if (isKeyboard)
            {
                // Hide Razer interactive controls
                if (lblDpiTitle != null) lblDpiTitle.Visible = false;
                if (btnDpiManage != null) btnDpiManage.Visible = false;
                if (btnDpiStages != null)
                {
                    for (int i = 0; i < btnDpiStages.Length; i++)
                    {
                        if (btnDpiStages[i] != null) btnDpiStages[i].Visible = false;
                    }
                }
                if (lblRateTitle != null) lblRateTitle.Visible = false;
                if (btnRates != null)
                {
                    for (int i = 0; i < btnRates.Length; i++)
                    {
                        if (btnRates[i] != null) btnRates[i].Visible = false;
                    }
                }

                bool isNuphyHardware = (info.Brand == "NuPhy");
                if (isNuphyHardware)
                {
                    AdjustPerformanceCardHeight((int)(196 * dpiScale));
                    if (lblPerfTitle != null) lblPerfTitle.Text = "NuPhy 专属硬件控制台";

                    if (btnNuphyAutoSleep != null) btnNuphyAutoSleep.Visible = true;
                    if (btnNuphyWinLock != null) btnNuphyWinLock.Visible = true;
                    if (btnNuphyBacklight != null) btnNuphyBacklight.Visible = true;
                    if (btnNuphySidelight != null) btnNuphySidelight.Visible = true;
                    if (lblNuphyBrightTitle != null) lblNuphyBrightTitle.Visible = true;
                    if (sliderNuphyBrightness != null) sliderNuphyBrightness.Visible = true;
                    if (lblNuphyBrightVal != null) lblNuphyBrightVal.Visible = true;

                    // Row 4: 键盘全键无冲测速 + Windows 键盘设置
                    int btnW = (secW - (int)(8 * dpiScale)) / 2;
                    int btnH = (int)(32 * dpiScale);
                    int r4Y = (int)(150 * dpiScale);

                    if (btnKeyTestSpeed != null)
                    {
                        btnKeyTestSpeed.Visible = true;
                        btnKeyTestSpeed.SetBounds((int)(16 * dpiScale), r4Y, btnW, btnH);
                    }
                    if (btnKeyboardSettings != null)
                    {
                        btnKeyboardSettings.Visible = true;
                        btnKeyboardSettings.SetBounds((int)(16 * dpiScale) + btnW + (int)(8 * dpiScale), r4Y, btnW, btnH);
                    }

                    ApplyNuphySettingsToUI();

                    ThreadPool.QueueUserWorkItem(_ => {
                        NuphyDeviceHelper.NuphyHardwareSettings hwSettings;
                        if (NuphyDeviceHelper.GetHardwareSettings(out hwSettings))
                        {
                            try
                            {
                                this.BeginInvoke((Action)(() => {
                                    ApplyNuphySettingsToUI();
                                }));
                            }
                            catch { }
                        }
                    });
                }
                else
                {
                    AdjustPerformanceCardHeight((int)(228 * dpiScale));
                    if (lblPerfTitle != null) lblPerfTitle.Text = "机械键盘硬件状态与控制台";

                    if (tileRapooDpi != null)
                    {
                        tileRapooDpi.Visible = true;
                        tileRapooDpi.Size = new Size(secW, (int)(62 * dpiScale));
                        tileRapooDpi.Location = new Point((int)(16 * dpiScale), (int)(36 * dpiScale));
                        tileRapooDpi.AccentColor = brandAccent;
                        tileRapooDpi.Badge = (info.Transport != null && info.Transport.Contains("蓝牙")) ? "BLE" : ((info.Transport != null && info.Transport.Contains("有线")) ? "USB" : "2.4G");

                        if (info.BatteryPercent >= 0)
                        {
                            tileRapooDpi.Title = "无线机械键盘";
                            tileRapooDpi.ValueText = info.BatteryPercent.ToString();
                            tileRapooDpi.UnitText = "%";
                            bool isKbWired = info.Transport != null && (info.Transport.IndexOf("有线", StringComparison.OrdinalIgnoreCase) >= 0 || info.Transport.IndexOf("USB", StringComparison.OrdinalIgnoreCase) >= 0);
                            string chgStatus = info.IsSleeping ? "休眠缓存" : (isKbWired ? "USB 持续供电" : (info.IsCharging ? "正在充电" : "电池供电"));
                            tileRapooDpi.Footnote = string.Format("通道: {0} | 供电: {1}", info.Transport ?? "无线", chgStatus);
                        }
                        else
                        {
                            tileRapooDpi.Title = "机械键盘 · 免驱即插即用";
                            tileRapooDpi.ValueText = info.IsConnected ? "在线" : "离线";
                            tileRapooDpi.UnitText = "";
                            tileRapooDpi.Footnote = string.Format("通道: {0} | 供电: USB 持续供电", info.Transport ?? "USB 直连");
                        }
                        tileRapooDpi.Invalidate();
                    }

                    // Three Locks HUD
                    if (hudThreeLocks != null)
                    {
                        hudThreeLocks.Visible = true;
                        hudThreeLocks.SetBounds((int)(16 * dpiScale), (int)(104 * dpiScale), secW, (int)(28 * dpiScale));
                        hudThreeLocks.CheckStatus();
                    }

                    int btnW = (secW - (int)(8 * dpiScale)) / 2;
                    int btnH = (int)(32 * dpiScale);
                    int row1Y = (int)(140 * dpiScale);
                    int row2Y = (int)(180 * dpiScale);

                    if (btnUniversalWinLock != null)
                    {
                        btnUniversalWinLock.Visible = true;
                        btnUniversalWinLock.SetBounds((int)(16 * dpiScale), row1Y, btnW, btnH);
                        if (UniversalWinLockHelper.IsLocked)
                        {
                            btnUniversalWinLock.Text = "🔒 Win 键已锁定 (防误触)";
                            btnUniversalWinLock.NormalColor = Color.FromArgb(40, 20, 24);
                            btnUniversalWinLock.BorderColor = Color.FromArgb(255, 82, 82);
                            btnUniversalWinLock.TextColor = Color.FromArgb(255, 100, 100);
                        }
                        else
                        {
                            btnUniversalWinLock.Text = "🛡️ Win 键防误触: 未锁定";
                            btnUniversalWinLock.NormalColor = Color.FromArgb(28, 32, 44);
                            btnUniversalWinLock.BorderColor = Color.FromArgb(50, 58, 76);
                            btnUniversalWinLock.TextColor = Color.FromArgb(190, 200, 220);
                        }
                    }
                    if (btnKeyTestSpeed != null)
                    {
                        btnKeyTestSpeed.Visible = true;
                        btnKeyTestSpeed.SetBounds((int)(16 * dpiScale) + btnW + (int)(8 * dpiScale), row1Y, btnW, btnH);
                    }
                    if (btnKeyboardSettings != null)
                    {
                        btnKeyboardSettings.Visible = true;
                        btnKeyboardSettings.SetBounds((int)(16 * dpiScale), row2Y, secW, btnH);
                    }
                }

                if (dpiMenu != null) dpiMenu.Visible = false;
                if (rateMenu != null) rateMenu.Visible = false;
            }
            else if (isDongle)
            {
                AdjustPerformanceCardHeight((int)(120 * dpiScale));
                if (lblPerfTitle != null) lblPerfTitle.Text = "2.4G 接收器 / 拓展坞";

                // Hide Razer interactive controls
                if (lblDpiTitle != null) lblDpiTitle.Visible = false;
                if (btnDpiManage != null) btnDpiManage.Visible = false;
                if (btnDpiStages != null)
                {
                    for (int i = 0; i < btnDpiStages.Length; i++)
                    {
                        if (btnDpiStages[i] != null) btnDpiStages[i].Visible = false;
                    }
                }
                if (lblRateTitle != null) lblRateTitle.Visible = false;
                if (btnRates != null)
                {
                    for (int i = 0; i < btnRates.Length; i++)
                    {
                        if (btnRates[i] != null) btnRates[i].Visible = false;
                    }
                }

                if (tileRapooDpi != null)
                {
                    tileRapooDpi.Visible = true;
                    tileRapooDpi.Size = new Size(secW, (int)(74 * dpiScale));
                    tileRapooDpi.Location = new Point((int)(16 * dpiScale), (int)(38 * dpiScale));
                    tileRapooDpi.AccentColor = brandAccent;
                    tileRapooDpi.Title = "无线接收器工作状态";
                    tileRapooDpi.Badge = "2.4G";
                    tileRapooDpi.ValueText = info.IsConnected ? "在线" : "未就绪";
                    tileRapooDpi.UnitText = "";
                    tileRapooDpi.Footnote = string.Format("标识: {0} | 供电: USB 持续供电", info.HardwareFingerprint ?? info.DeviceId);
                    tileRapooDpi.Invalidate();
                }

                if (dpiMenu != null) dpiMenu.Visible = false;
                if (rateMenu != null) rateMenu.Visible = false;
            }
            else if (isRapoo && !isRapooMouse)
            {
                AdjustPerformanceCardHeight((int)(160 * dpiScale));
                if (lblPerfTitle != null) lblPerfTitle.Text = "雷柏鼠标状态与性能测速";

                // Hide Razer interactive controls
                if (lblDpiTitle != null) lblDpiTitle.Visible = false;
                if (btnDpiManage != null) btnDpiManage.Visible = false;
                if (btnDpiStages != null)
                {
                    for (int i = 0; i < btnDpiStages.Length; i++)
                    {
                        if (btnDpiStages[i] != null) btnDpiStages[i].Visible = false;
                    }
                }
                if (lblRateTitle != null) lblRateTitle.Visible = false;
                if (btnRates != null)
                {
                    for (int i = 0; i < btnRates.Length; i++)
                    {
                        if (btnRates[i] != null) btnRates[i].Visible = false;
                    }
                }

                // Show Rapoo Status Tile (Full width, DPI only)
                if (tileRapooDpi != null)
                {
                    tileRapooDpi.Visible = true;
                    tileRapooDpi.Size = new Size(secW, (int)(64 * dpiScale));
                    tileRapooDpi.Location = new Point((int)(16 * dpiScale), (int)(36 * dpiScale));
                    tileRapooDpi.AccentColor = brandAccent;
                    tileRapooDpi.Title = "当前实时 DPI 档位";
                    tileRapooDpi.ValueText = (info.Dpi > 0 ? info.Dpi.ToString() : "--");
                    tileRapooDpi.UnitText = info.Dpi > 0 ? "DPI" : "";
                    tileRapooDpi.Badge = (info.Transport != null && info.Transport.Contains("2.4G")) ? "2.4G" : "无线";
                    tileRapooDpi.Footnote = "机身物理 DPI 键循环切档 · 屏幕 OSD 联动感知";
                    tileRapooDpi.Invalidate();
                }

                if (btnMouseRateTest != null)
                {
                    btnMouseRateTest.Visible = true;
                    btnMouseRateTest.SetBounds((int)(16 * dpiScale), (int)(110 * dpiScale), secW, (int)(34 * dpiScale));
                }

                if (dpiMenu != null)
                {
                    dpiMenu.Visible = true;
                    dpiMenu.Text = "当前 DPI 状态 (&D)";
                    dpiMenu.DropDownItems.Clear();
                    int curDpi = info.Dpi > 0 ? info.Dpi : 1600;
                    var curItem = new ToolStripMenuItem(string.Format("当前: {0} DPI", curDpi));
                    curItem.Enabled = false;
                    dpiMenu.DropDownItems.Add(curItem);
                }

                if (rateMenu != null) rateMenu.Visible = false;
            }
            else if (isVgn && !isFdasMouse)
            {
                AdjustPerformanceCardHeight((int)(160 * dpiScale));
                if (lblPerfTitle != null) lblPerfTitle.Text = "VGN 鼠标状态与性能测速";

                // Hide Razer interactive controls
                if (lblDpiTitle != null) lblDpiTitle.Visible = false;
                if (btnDpiManage != null) btnDpiManage.Visible = false;
                if (btnDpiStages != null)
                {
                    for (int i = 0; i < btnDpiStages.Length; i++)
                    {
                        if (btnDpiStages[i] != null) btnDpiStages[i].Visible = false;
                    }
                }
                if (lblRateTitle != null) lblRateTitle.Visible = false;
                if (btnRates != null)
                {
                    for (int i = 0; i < btnRates.Length; i++)
                    {
                        if (btnRates[i] != null) btnRates[i].Visible = false;
                    }
                }

                // Show VGN Status Tile
                if (tileRapooDpi != null)
                {
                    tileRapooDpi.Visible = true;
                    tileRapooDpi.Size = new Size(secW, (int)(64 * dpiScale));
                    tileRapooDpi.Location = new Point((int)(16 * dpiScale), (int)(36 * dpiScale));
                    tileRapooDpi.AccentColor = brandAccent;
                    tileRapooDpi.Title = "无线传输与工作状态";
                    tileRapooDpi.Badge = "2.4G";
                    tileRapooDpi.ValueText = info.IsConnected ? "在线" : "离线";
                    tileRapooDpi.UnitText = "";
                    tileRapooDpi.Footnote = string.Format("通道: {0} | 供电: {1}", info.Transport ?? "2.4G 无线", info.BatteryPercent >= 0 ? (info.BatteryPercent + "%") : "电池供电");
                    tileRapooDpi.Invalidate();
                }

                if (btnMouseRateTest != null)
                {
                    btnMouseRateTest.Visible = true;
                    btnMouseRateTest.SetBounds((int)(16 * dpiScale), (int)(110 * dpiScale), secW, (int)(34 * dpiScale));
                }

                if (dpiMenu != null) dpiMenu.Visible = false;
                if (rateMenu != null) rateMenu.Visible = false;
            }

            else if (isLogitech && !isLogitechMouse)
            {
                AdjustPerformanceCardHeight((int)(160 * dpiScale));
                if (lblPerfTitle != null) lblPerfTitle.Text = "罗技外设状态与性能测速";

                // Hide Razer interactive controls
                if (lblDpiTitle != null) lblDpiTitle.Visible = false;
                if (btnDpiManage != null) btnDpiManage.Visible = false;
                if (btnDpiStages != null)
                {
                    for (int i = 0; i < btnDpiStages.Length; i++)
                    {
                        if (btnDpiStages[i] != null) btnDpiStages[i].Visible = false;
                    }
                }
                if (lblRateTitle != null) lblRateTitle.Visible = false;
                if (btnRates != null)
                {
                    for (int i = 0; i < btnRates.Length; i++)
                    {
                        if (btnRates[i] != null) btnRates[i].Visible = false;
                    }
                }

                // Show Logitech Status Tile
                if (tileRapooDpi != null)
                {
                    tileRapooDpi.Visible = true;
                    tileRapooDpi.Size = new Size(secW, (int)(64 * dpiScale));
                    tileRapooDpi.Location = new Point((int)(16 * dpiScale), (int)(36 * dpiScale));
                    tileRapooDpi.AccentColor = brandAccent;
                    tileRapooDpi.Title = "Lightspeed 无线与工作状态";
                    tileRapooDpi.Badge = (info.Transport != null && info.Transport.Contains("蓝牙")) ? "BLE" : "Lightspeed";
                    tileRapooDpi.ValueText = info.IsConnected ? (info.BatteryPercent >= 0 ? info.BatteryPercent.ToString() : "在线") : (info.IsDonglePresent ? "待机" : "离线");
                    tileRapooDpi.UnitText = (info.IsConnected && info.BatteryPercent >= 0) ? "%" : "";
                    string logiPower = info.IsCharging ? "正在充电 ⚡" : (info.BatteryPercent >= 0 ? string.Format("电池供电 ({0}%)", info.BatteryPercent) : "电池供电");
                    tileRapooDpi.Footnote = string.Format("通道: {0} | 状态: {1} | 供电: {2}", info.Transport ?? "Lightspeed 无线", info.IsConnected ? "在线就绪" : (info.IsDonglePresent ? "闲置待机" : "已断开"), logiPower);
                    tileRapooDpi.Invalidate();
                }

                if (btnMouseRateTest != null)
                {
                    btnMouseRateTest.Visible = true;
                    btnMouseRateTest.SetBounds((int)(16 * dpiScale), (int)(110 * dpiScale), secW, (int)(34 * dpiScale));
                }

                if (dpiMenu != null) dpiMenu.Visible = false;
                if (rateMenu != null) rateMenu.Visible = false;
            }
            else if (isRazer || isValkyrieMouse || isLogitechMouse || isRapooMouse || isFdasMouse)
            {
                if (lblPerfTitle != null)
                {
                    if (isValkyrieMouse) lblPerfTitle.Text = "瓦尔基里鼠标性能与档位调节";
                    else if (isLogitechMouse) lblPerfTitle.Text = "罗技鼠标性能与档位调节";
                    else if (isRapooMouse) lblPerfTitle.Text = "雷柏鼠标性能与档位调节";
                    else if (isFdasMouse) lblPerfTitle.Text = (info.Brand ?? "外设") + " 鼠标性能与档位调节";
                    else lblPerfTitle.Text = "鼠标性能与档位调节";
                }
                if (rateMenu != null) rateMenu.Visible = true;

                if (lblDpiTitle != null) lblDpiTitle.Visible = true;
                if (lblRateTitle != null) lblRateTitle.Visible = true;

                bool supportsAutoGameRate = IsSelectedDeviceSupportingAutoGameRate();
                bool isOptExpanded = (autoGameRateEnabled || autoGameDpiEnabled);
                int targetPerfH = (!supportsAutoGameRate || !isOptExpanded) ? (int)(148 * dpiScale) : (int)(255 * dpiScale);
                AdjustPerformanceCardHeight(targetPerfH);

                bool showExpanded = supportsAutoGameRate && isOptExpanded;
                if (chkAutoGameOpt != null)
                {
                    chkAutoGameOpt.Visible = supportsAutoGameRate;
                    chkAutoGameOpt.Checked = isOptExpanded;
                }
                if (chkAutoGameDpi != null)
                {
                    chkAutoGameDpi.Checked = autoGameDpiEnabled;
                }
                if (chkAutoGameRate != null)
                {
                    chkAutoGameRate.Checked = autoGameRateEnabled;
                }
                if (chkNotifyOsd != null) chkNotifyOsd.Checked = gameRateNotifyOsd;
                if (chkNotifyToast != null) chkNotifyToast.Checked = gameRateNotifyToast;
                if (chkNotifySound != null) chkNotifySound.Checked = gameRateNotifySound;
                if (autoGameRateMenu != null) autoGameRateMenu.Visible = supportsAutoGameRate;
                if (info.DpiStages != null && info.DpiStages.Length > 0)
                {
                    bool differs = false;
                    if (dpiStageList.Count != info.DpiStages.Length) differs = true;
                    else
                    {
                        for (int k = 0; k < info.DpiStages.Length; k++)
                        {
                            if (dpiStageList[k] != info.DpiStages[k]) { differs = true; break; }
                        }
                    }
                    if (differs)
                    {
                        dpiStageList = new List<int>(info.DpiStages);
                        dpiStageValues = info.DpiStages;
                    }
                }
                RelayoutDpiButtons();

                if (btnDpiStages != null)
                {
                    for (int i = 0; i < btnDpiStages.Length; i++)
                    {
                        if (btnDpiStages[i] != null)
                        {
                            btnDpiStages[i].AccentColor = brandAccent;
                            if (i < dpiStageList.Count)
                            {
                                bool isCur = (info.DpiStage > 0 && info.DpiStageCount > 0)
                                    ? (i == (info.DpiStage - 1))
                                    : (dpiStageList[i] == info.Dpi);
                                btnDpiStages[i].Selected = isCur;
                            }
                        }
                    }
                }
                if (btnDpiManage != null)
                {
                    btnDpiManage.Visible = true;
                    btnDpiManage.NormalColor = Color.FromArgb(28, 33, 45);
                    btnDpiManage.HoverColor = Color.FromArgb(40, 48, 65);
                    btnDpiManage.PressedColor = Color.FromArgb(20, 24, 33);
                    btnDpiManage.BorderColor = Color.FromArgb(65, 75, 95);
                    btnDpiManage.ForeColor = Color.FromArgb(210, 220, 235);
                }

                if (btnRateManage != null)
                {
                    btnRateManage.Visible = true;
                    btnRateManage.NormalColor = Color.FromArgb(28, 33, 45);
                    btnRateManage.HoverColor = Color.FromArgb(40, 48, 65);
                    btnRateManage.PressedColor = Color.FromArgb(20, 24, 33);
                    btnRateManage.BorderColor = Color.FromArgb(65, 75, 95);
                    btnRateManage.ForeColor = Color.FromArgb(210, 220, 235);
                }

                int[] baseSupported = (info.SupportedPollingRates != null && info.SupportedPollingRates.Length > 0)
                    ? info.SupportedPollingRates
                    : (info.MaxPollingRate == 1000 ? new int[] { 125, 250, 500, 1000 } : new int[] { 125, 250, 500, 1000, 2000, 4000, 8000 });

                int[] defaultActive = GetDefaultActivePollingRates(baseSupported);
                pollingRateValues = LoadCustomPollingRates(info.DeviceId, defaultActive);

                if (info.PollingRate > 0 && Array.IndexOf(pollingRateValues, info.PollingRate) == -1)
                {
                    List<int> list = new List<int>(pollingRateValues);
                    list.Add(info.PollingRate);
                    list.Sort();
                    while (list.Count > 5)
                    {
                        int removeIdx = -1;
                        for (int k = 0; k < list.Count; k++)
                        {
                            if (list[k] != info.PollingRate) { removeIdx = k; break; }
                        }
                        if (removeIdx >= 0) list.RemoveAt(removeIdx);
                        else list.RemoveAt(0);
                    }
                    pollingRateValues = list.ToArray();
                }

                if (pollingRateValues != null && pollingRateValues.Length > 5)
                {
                    List<int> clampList = new List<int>(pollingRateValues);
                    clampList.Sort();
                    while (clampList.Count > 5) clampList.RemoveAt(0);
                    pollingRateValues = clampList.ToArray();
                }

                RelayoutRateButtons();

                if (btnRates != null)
                {
                    for (int i = 0; i < btnRates.Length; i++)
                    {
                        if (btnRates[i] != null)
                        {
                            btnRates[i].AccentColor = brandAccent;
                            if (i < pollingRateValues.Length)
                            {
                                if (isValkyrieMouse || isLogitechMouse || isRapooMouse || isRazer || isFdasMouse)
                                {
                                    btnRates[i].Selected = (pollingRateValues[i] == info.PollingRate);
                                }
                            }
                        }
                    }
                }

                RelayoutPerformanceControls();
                SetGameRateControlsVisibility(isOptExpanded);

                if (supportsAutoGameRate && !autoGameRateEnabled && info.PollingRate > 0 && desktopPollingRate == 0)
                {
                    desktopPollingRate = info.PollingRate;
                }

                if (supportsAutoGameRate)
                {
                    UpdatePollingRateDisplayStates();
                }

                if (dpiMenu != null && dpiStageValues != null)
                {
                    dpiMenu.Text = "调节 DPI 档位 (&D)";
                    dpiMenu.DropDownItems.Clear();
                    for (int i = 0; i < dpiStageValues.Length; i++)
                    {
                        int valX = dpiStageValues[i];
                        int valY = (info.DpiStagesY != null && info.DpiStagesY.Length > i) ? info.DpiStagesY[i] : valX;
                        string itemText = (info.IsXyIndependent && valX != valY)
                            ? string.Format("档位 {0}: {1} × {2} DPI", i + 1, valX, valY)
                            : string.Format("{0} DPI", valX);
                        var sub = new ToolStripMenuItem(itemText, null, (s, e) => SetDpiFromUI(valX));
                        sub.Tag = valX;
                        sub.Checked = (valX == info.Dpi);
                        dpiMenu.DropDownItems.Add(sub);
                    }
                }

                if (rateMenu != null && pollingRateValues != null)
                {
                    rateMenu.Text = "调节回报率 (&P)";
                    rateMenu.DropDownItems.Clear();
                    int targetActive = (isValkyrieMouse || isFdasMouse) ? info.PollingRate : (autoGameRateEnabled ? (isGamingActive ? gamePollingRate : desktopPollingRate) : desktopPollingRate);
                    for (int i = 0; i < pollingRateValues.Length; i++)
                    {
                        int val = pollingRateValues[i];
                        var sub = new ToolStripMenuItem(val + " Hz", null, (s, e) => SetPollingRateFromUI(val));
                        sub.Tag = val;
                        sub.Checked = (val == targetActive);
                        rateMenu.DropDownItems.Add(sub);
                    }
                }
            }
            else // Generic Mouse or Generic Peripheral
            {
                bool isGenMouse = (info.Category == DeviceCategory.Mouse);
                AdjustPerformanceCardHeight((int)((isGenMouse ? 160 : 120) * dpiScale));
                if (lblPerfTitle != null) lblPerfTitle.Text = isGenMouse ? "鼠标工作状态与测速" : "外设连接与状态";

                // Hide Razer interactive controls
                if (lblDpiTitle != null) lblDpiTitle.Visible = false;
                if (btnDpiManage != null) btnDpiManage.Visible = false;
                if (btnDpiStages != null)
                {
                    for (int i = 0; i < btnDpiStages.Length; i++)
                    {
                        if (btnDpiStages[i] != null) btnDpiStages[i].Visible = false;
                    }
                }
                if (lblRateTitle != null) lblRateTitle.Visible = false;
                if (btnRates != null)
                {
                    for (int i = 0; i < btnRates.Length; i++)
                    {
                        if (btnRates[i] != null) btnRates[i].Visible = false;
                    }
                }

                if (tileRapooDpi != null)
                {
                    tileRapooDpi.Visible = true;
                    tileRapooDpi.Size = new Size(secW, (int)(64 * dpiScale));
                    tileRapooDpi.Location = new Point((int)(16 * dpiScale), (int)(36 * dpiScale));
                    tileRapooDpi.AccentColor = brandAccent;
                    tileRapooDpi.Title = isGenMouse ? "鼠标工作状态" : "外设工作状态";
                    tileRapooDpi.Badge = (info.Transport != null && info.Transport.Contains("蓝牙")) ? "BLE" : "2.4G/USB";
                    if (info.BatteryPercent >= 0)
                    {
                        tileRapooDpi.ValueText = info.BatteryPercent.ToString();
                        tileRapooDpi.UnitText = "%";
                        tileRapooDpi.Footnote = string.Format("通道: {0} | 供电: 电池供电", info.Transport ?? "无线");
                    }
                    else
                    {
                        tileRapooDpi.ValueText = info.IsConnected ? "在线" : "离线";
                        tileRapooDpi.UnitText = "";
                        tileRapooDpi.Footnote = string.Format("通道: {0} | 供电: USB 持续供电", info.Transport ?? "USB 直连");
                    }
                    tileRapooDpi.Invalidate();
                }

                if (isGenMouse && btnMouseRateTest != null)
                {
                    btnMouseRateTest.Visible = true;
                    btnMouseRateTest.SetBounds((int)(16 * dpiScale), (int)(110 * dpiScale), secW, (int)(34 * dpiScale));
                }

                if (dpiMenu != null) dpiMenu.Visible = false;
                if (rateMenu != null) rateMenu.Visible = false;
            }
            }
            finally
            {
                isUpdatingUI = prevUpdatingUI;
            }
        }

        private void UpdateUI(MouseBatteryInfo info, bool showTipIfManual)
        {
            if (info == null)
            {
                isMouseSleeping = false;
                if (updateTimer != null) updateTimer.Interval = 3000;

                if (badgePrimary != null) badgePrimary.Visible = false;
                HideNuphyControls();
                HideAllAutoGameControls();

                lblDeviceName.Text = "未检测到支持的无线设备";

                lblBatteryBig.Text = "--%";
                lblBatteryBig.ForeColor = Color.FromArgb(140, 145, 155);

                pillStatus.SetStatus("未连接", Color.FromArgb(140, 145, 155));
                pillStatus.Location = new Point(lblBatteryBig.Right + (int)(16 * dpiScale), (int)(56 * dpiScale));

                barBattery.StopFlow(0, Color.FromArgb(70, 80, 100));
                lblUpdateTime.Text = "最后同步: " + DateTime.Now.ToString("HH:mm:ss") + " · 未检测到设备";

                statusMenuItem.Text = "未检测到设备 (未连接)";
                trayIcon.Text = "未检测到设备 (未连接)";
                UpdateTrayIcon(-1, false, false);
                lastLowAlertFired = false;
                if (showTipIfManual)
                {
                    SafeShowBalloonTip(2000, "设备未连接", "未能找到已连接或唤醒的外设设备，请检查开关或蓝牙连接。", ToolTipIcon.Warning);
                }
                return;
            }

            UpdatePrimaryPinButton(info);

            Color brandAccent = info.BrandColor;

            if (!info.IsConnected)
            {
                isMouseSleeping = false;
                EnableMouseWakeDetector(false);
                lblDeviceName.Text = info.DisplayName;

                string battDisplay = (info.BatteryPercent > 0 ? info.BatteryPercent.ToString() : "--") + "%";
                lblBatteryBig.Text = battDisplay;
                lblBatteryBig.ForeColor = Color.FromArgb(150, 160, 175);

                pillStatus.SetStatus("设备已离线", Color.FromArgb(145, 155, 175));
                pillStatus.Location = new Point(lblBatteryBig.Right + (int)(16 * dpiScale), (int)(56 * dpiScale));

                barBattery.StopFlow(info.BatteryPercent > 0 ? info.BatteryPercent : 0, Color.FromArgb(70, 80, 100));

                lblUpdateTime.Text = "最后同步: " + info.LastUpdated.ToString("yyyy-MM-dd HH:mm:ss") + " · 离线待命中";

                UpdatePerformanceSection(info, brandAccent);

                if (info.IsPrimary)
                {
                    UpdateStatusDisplay();
                    UpdateTrayIcon(info.BatteryPercent, false, false, false);
                }
                return;
            }

            if (info.Category == DeviceCategory.Keyboard)
            {
                isMouseSleeping = false;
                EnableMouseWakeDetector(false);
                if (updateTimer != null) updateTimer.Interval = userSelectedInterval;

                lblDeviceName.Text = info.DisplayName;

                bool isPureWired = (info.Transport != null && 
                                   (info.Transport.IndexOf("有线", StringComparison.OrdinalIgnoreCase) >= 0 || info.Transport.IndexOf("USB", StringComparison.OrdinalIgnoreCase) >= 0) &&
                                   !info.Transport.Contains("无线") && !info.Transport.Contains("2.4G"))
                                   || (info.DeviceId != null && info.DeviceId.IndexOf("pid_a011", StringComparison.OrdinalIgnoreCase) >= 0);
                bool isBle = info.Transport != null && info.Transport.Contains("蓝牙");

                // 休眠缓存不是当前遥测：保留数值与采样时间，停止充电动画。
                if (info.IsSleeping)
                {
                    isMouseSleeping = true;
                    lblBatteryBig.Font = new Font("Microsoft YaHei UI", 34F, FontStyle.Bold, GraphicsUnit.Point);
                    lblBatteryBig.Text = info.BatteryPercent >= 0 ? info.BatteryPercent + "%" : "--";
                    lblBatteryBig.ForeColor = Color.FromArgb(220, 225, 235);
                    pillStatus.SetStatus("休眠待机", Color.FromArgb(255, 183, 77));
                    pillStatus.Location = new Point(lblBatteryBig.Right + (int)(16 * dpiScale), (int)(56 * dpiScale));
                    barBattery.StopFlow(Math.Max(0, info.BatteryPercent), brandAccent);
                    lblUpdateTime.Text = info.LastUpdated == DateTime.MinValue ? "尚未读取电量 · 等待键盘响应" :
                        "最后同步: " + info.LastUpdated.ToString("HH:mm:ss") + " · 休眠缓存";
                    UpdatePerformanceSection(info, brandAccent);
                    if (info.IsPrimary)
                    {
                        UpdateStatusDisplay();
                        UpdateTrayIcon(info.BatteryPercent, false, true, true, brandAccent);
                    }
                    return;
                }

                bool kbCharging = isPureWired || info.IsCharging;
                if (isPureWired)
                {
                    lblBatteryBig.Font = new Font("Microsoft YaHei UI", 24F, FontStyle.Bold, GraphicsUnit.Point);
                    lblBatteryBig.Text = "已连接 · 供电中";
                    lblBatteryBig.ForeColor = brandAccent;

                    pillStatus.SetStatus("⚡ USB 供电", brandAccent);
                    pillStatus.Location = new Point(lblBatteryBig.Right + (int)(14 * dpiScale), (int)(54 * dpiScale));

                    barBattery.StartChargingFill(100, brandAccent, Color.FromArgb(200, 255, 230));
                    lblUpdateTime.Text = "最后同步: " + info.LastUpdated.ToString("HH:mm:ss") + " · USB 持续供电";
                }
                else if (isBle)
                {
                    lblBatteryBig.Font = new Font("Microsoft YaHei UI", 34F, FontStyle.Bold, GraphicsUnit.Point);
                    lblBatteryBig.Text = (info.BatteryPercent > 0 ? info.BatteryPercent.ToString() : "--") + "%";
                    lblBatteryBig.ForeColor = brandAccent;

                    pillStatus.SetStatus("📶 蓝牙无线", brandAccent);
                    pillStatus.Location = new Point(lblBatteryBig.Right + (int)(16 * dpiScale), (int)(56 * dpiScale));

                    barBattery.StopFlow(info.BatteryPercent, brandAccent);
                    lblUpdateTime.Text = "最后同步: " + info.LastUpdated.ToString("HH:mm:ss") + " · 蓝牙无线连接";
                }
                else
                {
                    // 2.4G 无线模式
                    int realBatt = info.BatteryPercent;
                    if (realBatt < 0)
                    {
                        lblBatteryBig.Font = new Font("Microsoft YaHei UI", 34F, FontStyle.Bold, GraphicsUnit.Point);
                        lblBatteryBig.Text = "--";
                        lblBatteryBig.ForeColor = brandAccent;

                        pillStatus.SetStatus("2.4G 无线", brandAccent);
                        pillStatus.Location = new Point(lblBatteryBig.Right + (int)(16 * dpiScale), (int)(56 * dpiScale));

                        barBattery.StopFlow(0, brandAccent);
                        lblUpdateTime.Text = "最后同步: " + info.LastUpdated.ToString("HH:mm:ss") + " · 2.4G 接收器在线";
                    }
                    else
                    {
                        bool isNuphy = (info.Brand == "NuPhy");
                        if (isNuphy && realBatt >= 100)
                        {
                            info.IsCharging = true;
                            kbCharging = true;
                            lblBatteryBig.Font = new Font("Microsoft YaHei UI", 24F, FontStyle.Bold, GraphicsUnit.Point);
                            lblBatteryBig.Text = "已连接 · 充电中";
                            lblBatteryBig.ForeColor = brandAccent;

                            pillStatus.SetStatus("⚡ 充电中 (2.4G)", brandAccent);
                            pillStatus.Location = new Point(lblBatteryBig.Right + (int)(14 * dpiScale), (int)(54 * dpiScale));

                            barBattery.StartChargingFill(100, brandAccent, Color.FromArgb(200, 255, 230));
                            lblUpdateTime.Text = "最后同步: " + info.LastUpdated.ToString("HH:mm:ss") + " · 2.4G 充电中";
                        }
                        else
                        {
                            lblBatteryBig.Font = new Font("Microsoft YaHei UI", 34F, FontStyle.Bold, GraphicsUnit.Point);
                            lblBatteryBig.Text = realBatt + "%";
                            lblBatteryBig.ForeColor = brandAccent;

                            pillStatus.SetStatus(info.IsCharging ? "⚡ 充电中" : "2.4G 无线", brandAccent);
                            pillStatus.Location = new Point(lblBatteryBig.Right + (int)(16 * dpiScale), (int)(56 * dpiScale));

                            if (info.IsCharging)
                                barBattery.StartChargingFill(realBatt, brandAccent, Color.FromArgb(200, 255, 230));
                            else
                                barBattery.StopFlow(realBatt, brandAccent);

                            lblUpdateTime.Text = "最后同步: " + info.LastUpdated.ToString("HH:mm:ss") + " · 2.4G 无线连接";
                        }
                    }
                }

                UpdatePerformanceSection(info, brandAccent);

                if (info.IsPrimary)
                {
                    UpdateStatusDisplay();
                    UpdateTrayIcon(info.BatteryPercent, kbCharging, true, false, brandAccent);
                }
                return;
            }

            // Connected mouse with zero/unknown battery (only query cache if device is genuinely sleeping)
            if (info.BatteryPercent <= 0 && info.Category == DeviceCategory.Mouse && info.IsSleeping)
            {
                int cached = -1;
                if (FdasRuntimeHelper.IsFdasMouse(info))
                {
                    var fdasDef = FdasRuntimeHelper.FindAdapterByDeviceId(info.DeviceId);
                    if (fdasDef != null && fdasDef.LastBattery > 0) cached = fdasDef.LastBattery;
                }
                else if (info.Brand == "Rapoo")
                {
                    cached = RapooDeviceHelper.CachedBatteryPercent;
                }
                else if (info.Brand == "Razer")
                {
                    var st = RazerDeviceHelper.GetDeviceState(info.DeviceId);
                    cached = (st != null && st.BatteryPercent > 0) ? st.BatteryPercent : RazerDeviceHelper.CachedBatteryPercent;
                }
                info.BatteryPercent = cached > 0 ? cached : -1;
            }

            if (info.IsSleeping)
            {
                isMouseSleeping = true;
                if (info.Category == DeviceCategory.Mouse)
                {
                    EnableMouseWakeDetector(true);
                }
                if (updateTimer != null) updateTimer.Interval = userSelectedInterval;

                Color sleepColor = Color.FromArgb(255, 183, 77); // Warm amber
                lblDeviceName.Text = info.DisplayName;

                string battDisplay = (info.BatteryPercent > 0 ? (info.BatteryPercent.ToString() + "%") : "--");

                lblBatteryBig.Font = new Font("Microsoft YaHei UI", 34F, FontStyle.Bold, GraphicsUnit.Point);
                lblBatteryBig.Text = battDisplay;
                lblBatteryBig.ForeColor = Color.FromArgb(220, 225, 235);

                pillStatus.SetStatus("💤 闲置待机", sleepColor);
                pillStatus.Location = new Point(lblBatteryBig.Right + (int)(16 * dpiScale), (int)(56 * dpiScale));

                barBattery.StopFlow(info.BatteryPercent, sleepColor);

                string timeStr = info.LastUpdated.ToString("HH:mm:ss");
                string wakeTip = (info.Category == DeviceCategory.Mouse) ? " · 移动鼠标即刻唤醒" : " · 待机中";
                lblUpdateTime.Text = "最后同步: " + timeStr + wakeTip;

                // Update Performance Card
                UpdatePerformanceSection(info, brandAccent);

                if (info.IsPrimary)
                {
                    UpdateStatusDisplay();
                    UpdateTrayIcon(info.BatteryPercent, false, true, true);
                }

                if (showTipIfManual)
                {
                    string restoreTip = (info.Category == DeviceCategory.Mouse) ? "\n移动鼠标将即刻恢复工作" : "";
                    SafeShowBalloonTip(1500, info.DisplayName, string.Format("电量: {0}% (休眠待机){1}", info.BatteryPercent, restoreTip), ToolTipIcon.Info);
                }
                return;
            }

            // Normal awake state (Mouse / Gamepad / Headset / Generic)
            isMouseSleeping = false;
            EnableMouseWakeDetector(false);
            if (updateTimer != null) updateTimer.Interval = userSelectedInterval;

            Color accentColor;
            if (info.BatteryPercent > 20 || info.IsCharging)
                accentColor = brandAccent;
            else if (info.BatteryPercent > 10)
                accentColor = Color.FromArgb(255, 214, 0);
            else
                accentColor = Color.FromArgb(255, 45, 85);

            bool mouseIsWired = info.Transport != null && 
                                (info.Transport.Equals("USB 有线", StringComparison.OrdinalIgnoreCase) || info.Transport.Equals("USB", StringComparison.OrdinalIgnoreCase)) &&
                                !info.Transport.Contains("无线") && 
                                !info.Transport.Contains("2.4G") && 
                                !info.IsDonglePresent;
            bool mouseIsBle = info.Transport != null && info.Transport.Contains("蓝牙");

            string mouseConnPill;
            if (info.IsCharging)
            {
                mouseConnPill = mouseIsWired ? "⚡ USB 充电中" : "⚡ 2.4G 充电中";
            }
            else if (mouseIsWired)
            {
                mouseConnPill = "🔌 USB 有线";
            }
            else if (mouseIsBle)
            {
                mouseConnPill = "📶 蓝牙无线";
            }
            else
            {
                mouseConnPill = (info.Transport != null && info.Transport.Length > 0) ? info.Transport : "2.4G 无线";
            }

            lblDeviceName.Text = info.DisplayName;

            lblBatteryBig.Font = new Font("Microsoft YaHei UI", 34F, FontStyle.Bold, GraphicsUnit.Point);
            string awakeBattDisplay = (info.BatteryPercent >= 0 ? info.BatteryPercent.ToString() : "--") + "%";

            lblBatteryBig.Text = awakeBattDisplay;
            lblBatteryBig.ForeColor = accentColor;

            pillStatus.SetStatus(mouseConnPill, accentColor);
            pillStatus.Location = new Point(lblBatteryBig.Right + (int)(16 * dpiScale), (int)(56 * dpiScale));

            if (info.IsCharging)
            {
                barBattery.StartChargingFill(info.BatteryPercent, accentColor, Color.FromArgb(180, 255, 200));
            }
            else
            {
                barBattery.StopFlow(info.BatteryPercent, accentColor);
            }

            string normalTimeStr = info.LastUpdated.ToString("HH:mm:ss");
            string chgDetail = info.IsCharging ? " · 正在充电" : " · 自动侦测硬件插拔";
            lblUpdateTime.Text = "最后同步: " + normalTimeStr + chgDetail;

            // Update Performance Card
            UpdatePerformanceSection(info, brandAccent);
            if (autoGameDpiEnabled && gameDpiStage > 0)
            {
                UpdateGameDpiButtonSelection(gameDpiStage);
            }

            if (info.IsPrimary)
            {
                UpdateStatusDisplay();
                UpdateTrayIcon(info.BatteryPercent, info.IsCharging, true, false);

                if (lowBatteryAlertEnabled && !info.IsCharging)
                {
                    if (info.BatteryPercent <= 20)
                    {
                        if (!lastLowAlertFired)
                        {
                            SafeShowBalloonTip(4000, info.DisplayName + " 电量不足", string.Format("当前电量仅剩 {0}%，请及时连接充电器！", info.BatteryPercent), ToolTipIcon.Warning);
                            lastLowAlertFired = true;
                        }
                    }
                    else
                    {
                        lastLowAlertFired = false;
                    }
                }
                else
                {
                    lastLowAlertFired = false;
                }
            }

            if (showTipIfManual)
            {
                string perfTip = (info.Dpi > 0 && info.PollingRate > 0) ? string.Format("\n性能: {0} DPI · {1} Hz", info.Dpi, info.PollingRate) : "";
                SafeShowBalloonTip(1500, info.DisplayName, string.Format("电量: {0}% ({1}){2}\n更新时间: {3}", info.BatteryPercent, mouseConnPill, perfTip, normalTimeStr), ToolTipIcon.Info);
            }
        }

    }
}
