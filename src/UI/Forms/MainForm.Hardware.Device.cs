// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// 1. 目标平台与编译规范：严格维持 C# 5.0 语法标准与 .NET Framework 4.0 兼容性。严禁引入任何第三方包或依赖。
// 2. 编码规范：所有文件严格保存为 UTF-8 带签名（BOM）格式，防止中文字符乱码。
// 3. 架构规范：本文件属于 MainForm 的分部类 (partial class) 之一。修改时请保持职责清晰，任何新增方法或逻辑请同步维护 ARCHITECTURE.md。
// 模块描述: MainForm 设备切换、主设备仲裁、电量状态刷新与 NuPhy 设置同步
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
                    else if (dev.BatteryPercent > 0)
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
                    ? (dev.IsSleeping ? "休眠中" : ((dev.Brand == "NuPhy" && (dev.BatteryPercent >= 100 || dev.IsCharging)) ? "充电中" : (dev.Category == DeviceCategory.Keyboard && dev.BatteryPercent <= 0 ? "免驱在线" : "已连接"))) 
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
            // 省电模式：启动阶段完全不查询设备（缓存值由 ShowEcoCachedState 显示）。
            // 任何一次查询都会把鼠标从低功耗推到高性能并打断其休眠计时，
            // 因此这里直接不启动轮询，由用户「立即刷新」或打开窗口时主动触发。
            if (VgnDeviceHelper.EcoModeEnabled) return;

            bootPollCount = 0;
            if (bootPollTimer == null)
            {
                bootPollTimer = new System.Windows.Forms.Timer();
                // 省电模式：启动时不立刻查询（查询会把鼠标从 LP 推到 HP，导致其无法休眠），
                // 首屏沿用上次缓存值，90 秒后再取一次。
                bootPollTimer.Interval = VgnDeviceHelper.EcoModeEnabled ? 300000 : 1200;
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
                            catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.Hardware.Device.cs: 已忽略异常", diagEx); }
                        });
                    }
                    int maxBootPolls = VgnDeviceHelper.EcoModeEnabled ? 1 : 8;
                    if (bootPollCount >= maxBootPolls || (lastInfo != null && lastInfo.IsConnected && !lastInfo.IsSleeping && lastInfo.BatteryPercent > 0))
                    {
                        bootPollTimer.Stop();
                    }
                };
            }
            bootPollTimer.Start();
        }



        // 省电模式：启动后这段时间内忽略所有「自动」查询（用户主动刷新不受限），
        // 目的是不让程序在开机就把鼠标从 LP 低功耗推到 HP 高性能。
        private static readonly DateTime appStartUtc = DateTime.UtcNow;
        private static readonly TimeSpan ecoBootQuietWindow = TimeSpan.FromSeconds(90);

        private void RefreshBatteryStatus(bool showTipIfManual)
        {
            // 单一守口：所有自动查询路径（启动轮询/可见性刷新/物理态同步/定时器）都汇到这里，
            // 省电模式启动窗口内直接返回，避免逐处打补丁时漏掉某条路径。
            // 省电模式启动静默窗口内，**所有自动刷新一律拦截**（含唤醒刷新）。
            // 早期版本给唤醒刷新开了例外，结果启动时「点图标」的鼠标输入就被当成唤醒，
            // 立刻触发一次查询把鼠标推到 HP（实测灯随即亮起）。只有用户主动操作
            //（立即刷新 / 切设备等 showTipIfManual=true）才放行。
            if (!showTipIfManual && VgnDeviceHelper.EcoModeEnabled
                && (DateTime.UtcNow - appStartUtc) < ecoBootQuietWindow)
            {
                return;
            }
            if (wakeRefreshPending) wakeRefreshPending = false;

            ThreadPool.QueueUserWorkItem(_ =>
            {
                try
                {
                    // 注：不在此处写心跳日志——每次轮询都落盘会显著拖慢启动类测试（日志目录不可写时更甚）。
                    // 异常路径已由 Program.cs 的全局处理器落盘，足以定位崩溃。
                    var devices = DeviceManager.QueryAllDevices();
                    try
                    {
                        this.BeginInvoke(new Action(() =>
                        {
                            ApplyDeviceStatus(devices, showTipIfManual);
                        }));
                    }
                    catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.Hardware.Device.cs: 已忽略异常", diagEx); }
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
                    catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.Hardware.Device.cs: 已忽略异常", diagEx); }
                }
            });
        }

        // 充电 → 放电切换时，若电量大幅回落，弹一次说明气泡。
        // 原因：充电时端电压被抬高，拔线后按真实开路电压重算，数值必然下降（实测 42% → 31%）。
        // 用户看到会以为掉电，所以解释一次。5 分钟内不重复弹，避免反复插拔刷屏。
        private bool prevChargingState = false;
        private int prevShownPct = -1;
        private DateTime lastUnplugTipUtc = DateTime.MinValue;

        private void DetectUnplugDrop(MouseBatteryInfo info)
        {
            if (info == null || info.BatteryPercent < 0)
            {
                return;
            }
            if (prevChargingState && !info.IsCharging && prevShownPct > 0)
            {
                int drop = prevShownPct - info.BatteryPercent;
                if (drop >= 5 && (DateTime.UtcNow - lastUnplugTipUtc).TotalMinutes >= 5)
                {
                    lastUnplugTipUtc = DateTime.UtcNow;
                    try
                    {
                        SafeShowBalloonTip(5000, "已切换为电池供电",
                            string.Format("电量由 {0}% 更新为 {1}%：充电时端电压被抬高，拔线后按电池实际电压重新计算，属正常现象。", prevShownPct, info.BatteryPercent),
                            ToolTipIcon.Info);
                    }
                    catch (Exception diagEx) { Diagnostics.Log("MainForm.Hardware.Device.cs: 拔线提示失败", diagEx); }
                }
            }
            prevChargingState = info.IsCharging;
            prevShownPct = info.BatteryPercent;
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
                DetectUnplugDrop(primary);

                UpdateDeviceTabsUI();
                UpdateUI(selectedDevice, showTipIfManual);
                UpdatePrimaryPinButton(selectedDevice);

                UpdateTrayIcon(primary.BatteryPercent, primary.IsCharging, primary.IsConnected, primary.IsSleeping);
                UpdateStatusDisplay();
                UpdateTrayDeviceMenuItems();
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
#if CHG_DEBUG
            Diagnostics.Log(string.Format("[UI] transport={0} isCharging={1} sleeping={2} batt={3} devId={4}",
                lastInfo.Transport, lastInfo.IsCharging, lastInfo.IsSleeping, lastInfo.BatteryPercent, lastInfo.DeviceId));
#endif

            string brandTag = lastInfo.Brand == "Rapoo" ? "雷柏" : (lastInfo.Brand == "VGN" ? "VGN" : (lastInfo.Brand == "Razer" ? "雷蛇" : (lastInfo.Brand == "NuPhy" ? "NuPhy" : (lastInfo.Category == DeviceCategory.Headset ? "耳机" : (lastInfo.Category == DeviceCategory.Gamepad ? "手柄" : (lastInfo.Category == DeviceCategory.Keyboard ? "键盘" : "外设"))))));
            string dpiPart = (lastInfo.Category != DeviceCategory.Keyboard && lastInfo.Dpi > 0) ? (" · " + lastInfo.Dpi + " DPI") : "";
            string ratePart = (lastInfo.Category != DeviceCategory.Keyboard && lastInfo.PollingRate > 0) ? (" · " + lastInfo.PollingRate + "Hz") : "";
            string chgPart = lastInfo.IsSleeping ? "休眠待机" : (lastInfo.IsCharging ? "充电中" : "电池供电");
            string chgStr = lastInfo.IsSleeping ? "休眠待机 (移动唤醒)" : (lastInfo.IsCharging ? "正在充电" : "电池供电");

            if (lastInfo.Category == DeviceCategory.Keyboard && !lastInfo.IsCharging)
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

            if (lastInfo.Category == DeviceCategory.Keyboard && lastInfo.BatteryPercent <= 0)
            {
                menuStatus = string.Format("[{0}] {1} (免驱在线)", brandTag, devDisplayName);
                tipText = string.Format("FerrisPulse · 灵脉\n[{0}] {1}\n状态: 免驱在线\n最后同步: {2}", brandTag, devDisplayName, timeStr);
            }
            else if (lastInfo.Category == DeviceCategory.Keyboard && lastInfo.BatteryPercent >= 100)
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
                                    NuphyDeviceHelper.SetBacklightBrightness(target);
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
                        catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.Hardware.Device.cs: 已忽略异常", diagEx); }
                    }
                }
                catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.Hardware.Device.cs: 已忽略异常", diagEx); }
                finally
                {
                    isSyncingPhysicalState = false;
                }
            });
        }

        // 判断用户是否已闲置指定秒数（读系统最后输入时间，零 USB 开销）
        private bool IsUserIdle(int seconds)
        {
            try
            {
                LASTINPUTINFO info = new LASTINPUTINFO();
                info.cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf(typeof(LASTINPUTINFO));
                if (!GetLastInputInfo(ref info)) return false;
                uint idleMs = unchecked((uint)Environment.TickCount - info.dwTime);
                // TickCount 会在 49.7 天回绕，用差值比较即可
                if (idleMs > 0x7FFFFFFF) return false;
                return idleMs >= (uint)(seconds * 1000);
            }
            catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.Hardware.Device.cs: 闲置判断失败", diagEx); }
            return false;
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

            // IsConnected 与 IsSleeping 互补（IsSleeping = !IsConnected），
            // 必须先排除休眠，否则休眠态会命中本分支 → 误显示「设备已离线」且关掉唤醒检测
            if (!info.IsConnected && !info.IsSleeping)
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

            if (info.IsConnected && info.Category == DeviceCategory.Keyboard)
            {
                isMouseSleeping = false;
                EnableMouseWakeDetector(false);
                if (updateTimer != null) ApplyAdaptiveInterval(); // 必须走自适应：直接赋 userSelectedInterval 会覆盖后台 60s 退避

                lblDeviceName.Text = info.DisplayName;

                bool isPureWired = (info.Transport != null && 
                                   (info.Transport.IndexOf("有线", StringComparison.OrdinalIgnoreCase) >= 0 || info.Transport.IndexOf("USB", StringComparison.OrdinalIgnoreCase) >= 0) &&
                                   !info.Transport.Contains("无线") && !info.Transport.Contains("2.4G"))
                                   || (info.DeviceId != null && info.DeviceId.IndexOf("pid_a011", StringComparison.OrdinalIgnoreCase) >= 0);
                bool isBle = info.Transport != null && info.Transport.Contains("蓝牙");

                bool kbCharging = isPureWired;
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

            // 待机展示条件：设备真休眠，或用户已闲置一段时间。
            // 后者是因为实测设备被查询唤醒后不会自行回到低功耗，IsSleeping 长期为 false，
            // 导致「闲置待机」这个状态永远看不到；用系统输入时间兜底更贴近用户直觉。
            bool userIdle = IsUserIdle(120);
            if (info.IsSleeping || userIdle)
            {
                // 只有设备真的休眠时才启用 RawInput 唤醒检测（闲置不代表设备睡了）
                isMouseSleeping = info.IsSleeping;
                if (info.IsSleeping && info.Category == DeviceCategory.Mouse)
                {
                    EnableMouseWakeDetector(true);
                }
                if (updateTimer != null) ApplyAdaptiveInterval(); // 必须走自适应：直接赋 userSelectedInterval 会覆盖后台 60s 退避

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
            if (updateTimer != null) ApplyAdaptiveInterval(); // 必须走自适应：直接赋 userSelectedInterval 会覆盖后台 60s 退避

            // 连续渐变：电量 100% → 0% 由绿色平滑过渡到红色（HSV 色相 120°→0°），充电与否均按电量变色
            Color accentColor = GetBatteryGradientColor(info.BatteryPercent);

            bool mouseIsWired = info.Transport != null && 
                                (info.Transport.IndexOf("有线", StringComparison.OrdinalIgnoreCase) >= 0 || info.Transport.Equals("USB", StringComparison.OrdinalIgnoreCase)) &&
                                !info.Transport.Contains("无线") && 
                                !info.Transport.Contains("蓝牙");
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
                // 不再回落到 Transport 原文：该字段形态不统一（可能是 "2.4G"、"2.4G-WiFi" 等），
                // 会显示成非规范文案。统一用「2.4G 无线」。
                mouseConnPill = "2.4G 无线";
            }

            lblDeviceName.Text = info.DisplayName;

            lblBatteryBig.Font = new Font("Microsoft YaHei UI", 34F, FontStyle.Bold, GraphicsUnit.Point);
            string awakeBattDisplay = (info.BatteryPercent >= 0 ? info.BatteryPercent.ToString() : "--") + "%";

            lblBatteryBig.Text = awakeBattDisplay;
            lblBatteryBig.ForeColor = accentColor;

            // 双显示悬停提示：固件电量计 + 电池电压换算值
            if (toolTipTop != null)
            {
                if (!string.IsNullOrEmpty(info.CustomTooltip))
                {
                    toolTipTop.SetToolTip(lblBatteryBig, info.CustomTooltip);
                }
                else
                {
                    toolTipTop.SetToolTip(lblBatteryBig, null);
                }
            }

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
            string chgDetail = info.IsCharging ? " · 正在充电" : " · 自动侦测插拔";
            // 常驻显示电池电压（放电时主显示为电压法，充电/有线时回落固件库仑计）
            string voltDetail = (info.BatteryVoltageMv > 0) ? string.Format(" · {0}.{1:D3}V", info.BatteryVoltageMv / 1000, info.BatteryVoltageMv % 1000) : "";

            // 剩余续航估算：基于当前主显示电量的放电速率（Z2 放电时即电压法换算值）
            //   正式值：累计 ≥1.5 小时或下降 ≥3%    粗估：≥15 分钟且下降 ≥1%    都不满足：显示采集进度
            BatteryRuntimeEstimator.Record(info.BatteryPercent, info.IsCharging, DateTime.Now, info.BatteryVoltageMv);
            double remainHours = 0, pctPerHour = 0;
            string runtimeDetail = "";
            string runtimeTip = "";
            if (!info.IsCharging)
            {
                if (BatteryRuntimeEstimator.TryEstimate(out remainHours, out pctPerHour))
                {
                    runtimeDetail = string.Format(" · 预计 {0:0.#}h（空闲）", remainHours);
                    runtimeTip = string.Format("\r\n放电速率: {0:0.0}%/小时 · 预计剩余 {1:0.#} 小时", pctPerHour, remainHours);
                }
                else if (BatteryRuntimeEstimator.TryEstimateRough(out remainHours, out pctPerHour))
                {
                    runtimeDetail = string.Format(" · 粗估 {0:0.#}h", remainHours);
                    runtimeTip = string.Format("\r\n放电速率(粗估): {0:0.0}%/小时 · 预计剩余 {1:0.#} 小时\r\n数据不足 1.5 小时，误差较大", pctPerHour, remainHours);
                }
                else if (BatteryRuntimeEstimator.TryEstimateFromVoltage(info.BatteryVoltageMv, info.BatteryPercent, out remainHours, out pctPerHour))
                {
                    // 固件电量计长时间不刷新时，用电压趋势兜底（误差较大，单独标注）
                    runtimeDetail = string.Format(" · 电压估算 {0:0.#}h", remainHours);
                    runtimeTip = string.Format("\r\n电压趋势估算: {0:0.0}%/小时 · 预计剩余 {1:0.#} 小时\r\n依据电池电压下降速率（官方 21 点表），误差约 ±30%", pctPerHour, remainHours);
                }
                else
                {
                    double spanH; int dropPct;
                    if (BatteryRuntimeEstimator.TryGetProgress(out spanH, out dropPct))
                    {
                        runtimeDetail = string.Format(" · 续航测算中 {0:0}m", spanH * 60);
                        runtimeTip = string.Format("\r\n续航测算中: 已采集 {0:0} 分钟 · 电量变化 {1}%\r\n需累计 15 分钟且下降 1% 出粗估值", spanH * 60, -dropPct);
                        // 电压趋势进度：电量计不动时用户仍能看到在积累
                        double vSpanH; int vDropMv;
                        if (BatteryRuntimeEstimator.TryGetVoltageProgress(out vSpanH, out vDropMv))
                        {
                            runtimeTip += string.Format("\r\n电压趋势: 已采集 {0:0} 分钟 · 下降 {1}mV\r\n满 30 分钟且降 15mV 后出电压估算", vSpanH * 60, vDropMv);
                        }
                        // 电量计长时间不动：多半是鼠标空闲休眠/极低放电，避免用户误以为卡住
                        if (dropPct == 0 && spanH >= 0.5)
                        {
                            runtimeTip = string.Format("\r\n续航测算待机: 已采集 {0:0} 分钟，电量计未变化\r\n鼠标可能处于休眠/低功耗状态，活跃使用掉 1% 后即出估值", spanH * 60);
                            if (BatteryRuntimeEstimator.TryGetVoltageProgress(out vSpanH, out vDropMv))
                            {
                                runtimeTip += string.Format("\r\n电压趋势: 已采集 {0:0} 分钟 · 下降 {1}mV\r\n满 30 分钟且降 15mV 后出电压估算", vSpanH * 60, vDropMv);
                            }
                        }
                    }
                }
            }

            lblUpdateTime.Text = "最后同步: " + normalTimeStr + voltDetail + runtimeDetail + chgDetail;

            // 悬停提示补充续航信息（正式值 / 粗估 / 测算进度）
            if (!string.IsNullOrEmpty(runtimeTip) && toolTipTop != null && !string.IsNullOrEmpty(info.CustomTooltip))
            {
                toolTipTop.SetToolTip(lblBatteryBig, info.CustomTooltip + runtimeTip);
            }

            // Update Performance Card
            UpdatePerformanceSection(info, brandAccent);

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
