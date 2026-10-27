// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// 1. 目标平台与编译规范：严格维持 C# 5.0 语法标准与 .NET Framework 4.0 兼容性。严禁引入任何第三方包或依赖。
// 2. 编码规范：所有文件严格保存为 UTF-8 带签名（BOM）格式，防止中文字符乱码。
// 3. 架构规范：本文件属于 MainForm 的分部类 (partial class) 之一。修改时请保持职责清晰，任何新增方法或逻辑请同步维护 ARCHITECTURE.md。
// 模块描述: MainForm 全屏游戏感知、进出游戏 DPI/回报率切档与桌面状态恢复
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

        private void StartGameMonitor()
        {
            if (gameMonitorTimer != null) return;
            gameMonitorTimer = new System.Windows.Forms.Timer();
            gameMonitorTimer.Interval = 1000;
            gameMonitorTimer.Tick += (s, e) => {
                if (!autoGameRateEnabled && !autoGameDpiEnabled) return;
                var curDev = GetActiveMouseDevice();
                if (curDev == null || !IsDeviceSupportingAutoGameRate(curDev)) return;

                string procTitle = "";
                bool isGame = false;

                if (gameOptMode == 1) // 模式 1: 进程存活常驻模式 (推荐)
                {
                    // Priority 1: Check foreground window first (fast path)
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
                            catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.Hardware.GameOpt.cs: 已忽略异常", diagEx); }
                        }
                    }

                    // Priority 2: If foreground is not game (user Alt-Tabbed to check time/chat/web), check if any monitored game process is running
                    if (!isGame && monitoredGameProcesses != null && monitoredGameProcesses.Count > 0)
                    {
                        // 性能：原实现对白名单里每个游戏各调一次 Process.GetProcessesByName，
                        // 每次都会枚举整张系统进程表（N 个游戏 = N 次全表遍历/秒）。
                        // 改为「一次快照 + 名字匹配」，白名单再长也只遍历一遍。
                        Process[] snapshot = null;
                        try { snapshot = Process.GetProcesses(); }
                        catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.Hardware.GameOpt.cs: 已忽略异常", diagEx); }
                        if (snapshot != null)
                        {
                            try
                            {
                                for (int i = 0; i < monitoredGameProcesses.Count && !isGame; i++)
                                {
                                    string g = monitoredGameProcesses[i];
                                    if (string.IsNullOrEmpty(g)) continue;
                                    string pNameOnly = g.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) ? g.Substring(0, g.Length - 4) : g;
                                    for (int k = 0; k < snapshot.Length; k++)
                                    {
                                        Process p = snapshot[k];
                                        if (p == null) continue;
                                        string pn;
                                        try { pn = p.ProcessName; } catch { continue; }
                                        if (!string.Equals(pn, pNameOnly, StringComparison.OrdinalIgnoreCase)) continue;
                                        isGame = true;
                                        try { procTitle = p.MainWindowTitle; } catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.Hardware.GameOpt.cs: 已忽略异常", diagEx); }
                                        if (string.IsNullOrEmpty(procTitle)) procTitle = pNameOnly;
                                        break;
                                    }
                                }
                            }
                            finally
                            {
                                // 统一释放快照内的 Process 对象，避免句柄滞留到 GC 终结器
                                for (int k = 0; k < snapshot.Length; k++)
                                {
                                    try { if (snapshot[k] != null) snapshot[k].Dispose(); } catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.Hardware.GameOpt.cs: 已忽略异常", diagEx); }
                                }
                            }
                        }
                    }

                    // Priority 3: Fallback to full-screen heuristic if user enabled fallback
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
                            catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.Hardware.GameOpt.cs: 已忽略异常", diagEx); }
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
            lastMonitoredRazerDpis[key] = targetDpi;
            lastMonitoredRazerStages[key] = targetStage;
            pendingRazerDpiCandidates.Remove(key);
            pendingRazerStageCandidates.Remove(key);

            lastMonitoredDpi = targetDpi;
            lastMonitoredStage = targetStage;
            lastMonitoredRapooDpi = targetDpi;
            lastMonitoredRapooStage = targetStage;
            lastMonitoredValkyrieDpi = targetDpi;
            lastMonitoredValkyrieStage = targetStage;
            pendingValkyrieDpiCandidate = -1;
            pendingValkyrieStageCandidate = -1;
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
                    bool rOk = false;
                    if (FdasRuntimeHelper.IsFdasMouse(curDev)) rOk = FdasRuntimeHelper.SetPollingRate(curDev.DeviceId, targetHz);
                    else if (brand == "Valkyrie" || brand == "瓦尔基里") rOk = ValkyrieDeviceHelper.SetValkyriePollingRate(targetHz);
                    else if (brand == "Logitech" || brand == "罗技") rOk = LogitechDeviceHelper.SetLogitechPollingRate(targetHz, curDev.DeviceId);
                    else if (brand == "Rapoo" || brand == "雷柏") rOk = RapooDeviceHelper.SetRapooPollingRate(targetHz);
                    else if ((brand == "Mchose" || brand == "MCHOSE" || brand == "迈从") && curDev.Category == DeviceCategory.Mouse) rOk = MchoseMouseHelper.SetMchosePollingRate(targetHz);
                    else if (brand == "Zaopin" || brand == "皂品" || brand == "VGN") rOk = VgnDeviceHelper.SetVgnPollingRate(targetHz);
                    else if (brand == "Razer" || brand == "雷蛇" || (curDev.DeviceId != null && curDev.DeviceId.StartsWith("Razer:1532:"))) rOk = RazerDeviceHelper.SetRazerPollingRate(targetHz, curDev.DeviceId);
                    else rOk = RazerDeviceHelper.SetRazerPollingRate(targetHz, curDev.DeviceId);
                    if (rOk) break;
                    Thread.Sleep(60);
                }

                for (int attempt = 0; attempt < 3; attempt++)
                {
                    bool dOk = false;
                    if (FdasRuntimeHelper.IsFdasMouse(curDev))
                    {
                        dOk = FdasRuntimeHelper.SetDpi(curDev.DeviceId, targetDpi, targetDpiY, targetStage);
                    }
                    else if (brand == "Rapoo" || brand == "雷柏")
                    {
                        dOk = RapooDeviceHelper.SetRapooDpiStage(targetStage);
                    }
                    else if (brand == "Valkyrie" || brand == "瓦尔基里")
                    {
                        int[] stagesX = (curDev.DpiStages != null && curDev.DpiStages.Length > 0) ? curDev.DpiStages : dpiStageList.ToArray();
                        int[] stagesY = (curDev.DpiStagesY != null && curDev.DpiStagesY.Length == stagesX.Length) ? curDev.DpiStagesY : stagesX;
                        dOk = ValkyrieDeviceHelper.UpdateValkyrieHardwareStages(targetStage, stagesX, stagesY);
                    }
                    else if (brand == "Logitech" || brand == "罗技")
                    {
                        dOk = LogitechDeviceHelper.SetLogitechDpi(targetDpi, targetDpiY, curDev.DeviceId);
                    }
                    else if (brand == "Zaopin" || brand == "皂品" || brand == "VGN")
                    {
                        dOk = VgnDeviceHelper.SetVgnDpiStage(targetStage);
                    }
                    else
                    {
                        dOk = RazerDeviceHelper.SetRazerDpiStage(targetStage, curDev.DeviceId);
                    }
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
                    if (FdasRuntimeHelper.IsFdasMouse(curDev)) ok = FdasRuntimeHelper.SetPollingRate(curDev.DeviceId, targetHz);
                    else if (brand == "Valkyrie" || brand == "瓦尔基里") ok = ValkyrieDeviceHelper.SetValkyriePollingRate(targetHz);
                    else if (brand == "Logitech" || brand == "罗技") ok = LogitechDeviceHelper.SetLogitechPollingRate(targetHz, curDev.DeviceId);
                    else if (brand == "Rapoo" || brand == "雷柏") ok = RapooDeviceHelper.SetRapooPollingRate(targetHz);
                    else if ((brand == "Mchose" || brand == "MCHOSE" || brand == "迈从") && curDev.Category == DeviceCategory.Mouse) ok = MchoseMouseHelper.SetMchosePollingRate(targetHz);
                    else if (brand == "Zaopin" || brand == "皂品" || brand == "VGN") ok = VgnDeviceHelper.SetVgnPollingRate(targetHz);
                    else if (brand == "Razer" || brand == "雷蛇" || (curDev.DeviceId != null && curDev.DeviceId.StartsWith("Razer:1532:"))) ok = RazerDeviceHelper.SetRazerPollingRate(targetHz, curDev.DeviceId);
                    else ok = RazerDeviceHelper.SetRazerPollingRate(targetHz, curDev.DeviceId);
                    if (ok) break;
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
                    if (FdasRuntimeHelper.IsFdasMouse(curDev)) ok = FdasRuntimeHelper.SetPollingRate(curDev.DeviceId, targetHz);
                    else if (brand == "Valkyrie" || brand == "瓦尔基里") ok = ValkyrieDeviceHelper.SetValkyriePollingRate(targetHz);
                    else if (brand == "Logitech" || brand == "罗技") ok = LogitechDeviceHelper.SetLogitechPollingRate(targetHz, curDev.DeviceId);
                    else if (brand == "Rapoo" || brand == "雷柏") ok = RapooDeviceHelper.SetRapooPollingRate(targetHz);
                    else if (brand == "Zaopin" || brand == "皂品" || brand == "VGN") ok = VgnDeviceHelper.SetVgnPollingRate(targetHz);
                    else if (brand == "Razer" || brand == "雷蛇" || (curDev.DeviceId != null && curDev.DeviceId.StartsWith("Razer:1532:"))) ok = RazerDeviceHelper.SetRazerPollingRate(targetHz, curDev.DeviceId);
                    else ok = RazerDeviceHelper.SetRazerPollingRate(targetHz, curDev.DeviceId);
                    if (ok) break;
                    Thread.Sleep(80);
                }

                if (ok)
                {
                    this.BeginInvoke(new Action(() => {
                        curDev.PollingRate = targetHz;
                        if (lastInfo != null && lastInfo.DeviceId == curDev.DeviceId) lastInfo.PollingRate = targetHz;

                        UpdateStatusDisplay();
                        UpdatePollingRateDisplayStates();

                        // 桌面回报率与电竞回报率相同时，切换前后并无变化，静默处理避免误报「已恢复」
                        bool unchanged = (gamePollingRate == targetHz);

                        if (!unchanged && gameRateNotifyOsd && osdForm != null)
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

                        if (!unchanged && gameRateNotifyToast)
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

                        if (!unchanged && gameRateNotifySound)
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
            string brand = curDev.Brand;
            SyncMonitoredDpiState(targetDpi, targetStage, curDev.DeviceId);

            new Thread(() => {
                bool ok = false;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if (FdasRuntimeHelper.IsFdasMouse(curDev))
                    {
                        int targetDpiY = targetDpi;
                        if (curDev.DpiStagesY != null && curDev.DpiStagesY.Length >= targetStage)
                        {
                            targetDpiY = curDev.DpiStagesY[targetStage - 1];
                        }
                        ok = FdasRuntimeHelper.SetDpi(curDev.DeviceId, targetDpi, targetDpiY, targetStage);
                    }
                    else if (brand == "Rapoo" || brand == "雷柏")
                    {
                        ok = RapooDeviceHelper.SetRapooDpiStage(targetStage);
                    }
                    else if (brand == "Valkyrie" || brand == "瓦尔基里")
                    {
                        int[] stagesX = (curDev.DpiStages != null && curDev.DpiStages.Length > 0) ? curDev.DpiStages : dpiStageList.ToArray();
                        int[] stagesY = (curDev.DpiStagesY != null && curDev.DpiStagesY.Length == stagesX.Length) ? curDev.DpiStagesY : stagesX;
                        ok = ValkyrieDeviceHelper.UpdateValkyrieHardwareStages(targetStage, stagesX, stagesY);
                    }
                    else if (brand == "Logitech" || brand == "罗技")
                    {
                        int targetDpiY = targetDpi;
                        if (curDev.DpiStagesY != null && curDev.DpiStagesY.Length >= targetStage)
                        {
                            targetDpiY = curDev.DpiStagesY[targetStage - 1];
                        }
                        ok = LogitechDeviceHelper.SetLogitechDpi(targetDpi, targetDpiY, curDev.DeviceId);
                    }
                    else if ((brand == "Mchose" || brand == "MCHOSE" || brand == "迈从") && curDev.Category == DeviceCategory.Mouse)
                    {
                        ok = (targetStage > 0) ? MchoseMouseHelper.SetMchoseDpiStage(targetStage) : MchoseMouseHelper.SetMchoseDpi(targetDpi, targetDpi, 1);
                    }
                    else if (brand == "Zaopin" || brand == "皂品" || brand == "VGN")
                    {
                        ok = VgnDeviceHelper.SetVgnDpiStage(targetStage);
                    }
                    else // Razer or other
                    {
                        string rzDevId = curDev.DeviceId;
                        ok = RazerDeviceHelper.SetRazerDpiStage(targetStage, rzDevId);
                    }

                    if (ok) break;
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
            string brand = curDev.Brand;
            int prevStage = desktopDpiStage;
            desktopDpiStage = 0; // reset memory
            SyncMonitoredDpiState(targetDpi, targetStage, curDev.DeviceId);

            new Thread(() => {
                bool ok = false;
                for (int attempt = 0; attempt < 3; attempt++)
                {
                    if (FdasRuntimeHelper.IsFdasMouse(curDev))
                    {
                        int targetDpiY = targetDpi;
                        if (curDev.DpiStagesY != null && curDev.DpiStagesY.Length >= targetStage)
                        {
                            targetDpiY = curDev.DpiStagesY[targetStage - 1];
                        }
                        ok = FdasRuntimeHelper.SetDpi(curDev.DeviceId, targetDpi, targetDpiY, targetStage);
                    }
                    else if (brand == "Rapoo" || brand == "雷柏")
                    {
                        ok = RapooDeviceHelper.SetRapooDpiStage(targetStage);
                    }
                    else if (brand == "Valkyrie" || brand == "瓦尔基里")
                    {
                        int[] stagesX = (curDev.DpiStages != null && curDev.DpiStages.Length > 0) ? curDev.DpiStages : dpiStageList.ToArray();
                        int[] stagesY = (curDev.DpiStagesY != null && curDev.DpiStagesY.Length == stagesX.Length) ? curDev.DpiStagesY : stagesX;
                        ok = ValkyrieDeviceHelper.UpdateValkyrieHardwareStages(targetStage, stagesX, stagesY);
                    }
                    else if (brand == "Logitech" || brand == "罗技")
                    {
                        int targetDpiY = targetDpi;
                        if (curDev.DpiStagesY != null && curDev.DpiStagesY.Length >= targetStage)
                        {
                            targetDpiY = curDev.DpiStagesY[targetStage - 1];
                        }
                        ok = LogitechDeviceHelper.SetLogitechDpi(targetDpi, targetDpiY, curDev.DeviceId);
                    }
                    else if ((brand == "Mchose" || brand == "MCHOSE" || brand == "迈从") && curDev.Category == DeviceCategory.Mouse)
                    {
                        ok = (targetStage > 0) ? MchoseMouseHelper.SetMchoseDpiStage(targetStage) : MchoseMouseHelper.SetMchoseDpi(targetDpi, targetDpi, 1);
                    }
                    else if (brand == "Zaopin" || brand == "皂品" || brand == "VGN")
                    {
                        ok = VgnDeviceHelper.SetVgnDpiStage(targetStage);
                    }
                    else // Razer or other
                    {
                        string rzDevId = curDev.DeviceId;
                        ok = RazerDeviceHelper.SetRazerDpiStage(targetStage, rzDevId);
                    }

                    if (ok) break;
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

                        // 桌面档位与电竞档位相同时，切换前后并无变化，静默处理避免误报「已恢复」
                        bool dpiUnchanged = (gameDpiStage == targetStage);

                        if (!autoGameRateEnabled && !dpiUnchanged)
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

    }
}
