// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️

// 模块描述: MainForm 分部类 —— 配置持久化与开机启动管理（LoadConfig/SaveConfig/自动启动/低电量提醒）

// 说明: 本文件由 MainForm.cs 拆分而来，与原文件同为 partial class MainForm 的一部分。

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

    public partial class MainForm
    {

        private void LoadConfig()

        {

            try

            {

                using (var key = Registry.CurrentUser.OpenSubKey(ConfigRegistryKey) ?? Registry.CurrentUser.OpenSubKey(@"Software\RazerBatteryTray"))

                {

                    if (key != null)

                    {

                        var val = key.GetValue("LowBatteryAlert");

                        if (val != null) lowBatteryAlertEnabled = (int)val == 1;



                        var dVal = key.GetValue("DpiOsdAlert");

                        if (dVal != null) dpiOsdEnabled = (int)dVal == 1;



                        var autoShowVal = key.GetValue("AutoStartShowUI");

                        if (autoShowVal != null) autoStartShowMainWindow = (int)autoShowVal == 1;



                        var sVal = key.GetValue("TrayIconStyle");

                        if (sVal != null)

                        {

                            trayStyle = (int)sVal;

                            if (trayStyle < 0 || trayStyle > 2) trayStyle = 0;

                        }



                        var oVal = key.GetValue("OsdStyle");

                        if (oVal != null)

                        {

                            osdStyle = (int)oVal;

                            if (osdStyle < 0 || osdStyle > 2) osdStyle = 0;

                        }



                        var rVal = key.GetValue("RefreshInterval");

                        if (rVal != null)

                        {

                            userSelectedInterval = (int)rVal;

                            // 允许自定义间隔（3 秒 ~ 60 分钟），越界则回落默认 10 秒
                            if (userSelectedInterval < 3000 || userSelectedInterval > 3600000)

                            {

                                userSelectedInterval = 10000;

                            }

                        }



                        var autoGameOptVal = key.GetValue("AutoGameOptEnabled");

                        var autoGameVal = key.GetValue("AutoGameRateEnabled");

                        if (autoGameVal != null) autoGameRateEnabled = (int)autoGameVal == 1;



                        var autoGameDpiVal = key.GetValue("AutoGameDpiEnabled");

                        if (autoGameDpiVal != null) autoGameDpiEnabled = (int)autoGameDpiVal == 1;



                        if (autoGameOptVal != null)

                        {

                            bool optMaster = (int)autoGameOptVal == 1;

                            if (!optMaster)

                            {

                                autoGameRateEnabled = false;

                                autoGameDpiEnabled = false;

                            }

                        }



                        var gameRateVal = key.GetValue("GameTargetPollingRate");

                        if (gameRateVal != null) gamePollingRate = (int)gameRateVal;



                        var gameDpiStageVal = key.GetValue("GameTargetDpiStage");

                        if (gameDpiStageVal != null)

                        {

                            gameDpiStage = (int)gameDpiStageVal;

                            if (gameDpiStage < 1 || gameDpiStage > 5) gameDpiStage = 2;

                        }



                        var deskRateVal = key.GetValue("DesktopPollingRate");

                        if (deskRateVal != null) desktopPollingRate = (int)deskRateVal;



                        var notifyOsdVal = key.GetValue("GameRateNotifyOsd");

                        if (notifyOsdVal != null) gameRateNotifyOsd = (int)notifyOsdVal == 1;



                        var notifyToastVal = key.GetValue("GameRateNotifyToast");

                        if (notifyToastVal != null) gameRateNotifyToast = (int)notifyToastVal == 1;



                        var notifySoundVal = key.GetValue("GameRateNotifySound");

                        if (notifySoundVal != null) gameRateNotifySound = (int)notifySoundVal == 1;



                        var procListVal = key.GetValue("MonitoredGameProcesses") as string;

                        if (!string.IsNullOrEmpty(procListVal))

                        {

                            monitoredGameProcesses.Clear();

                            string[] items = procListVal.Split(new char[] { ';', '|', ',' }, StringSplitOptions.RemoveEmptyEntries);

                            for (int i = 0; i < items.Length; i++)

                            {

                                string trimmed = items[i].Trim();

                                if (!string.IsNullOrEmpty(trimmed))

                                {

                                    monitoredGameProcesses.Add(trimmed);

                                }

                            }

                        }



                        var fallbackVal = key.GetValue("FallbackFullscreenEnabled");

                        if (fallbackVal != null) fallbackFullscreenEnabled = (int)fallbackVal == 1;

                        var gameOptModeVal = key.GetValue("GameOptMode");
                        if (gameOptModeVal != null) gameOptMode = (int)gameOptModeVal;
                        else gameOptMode = 1; // 默认进程存活常驻模式 (推荐)



                        var customDpiVal = key.GetValue("CustomDpiStages") as string;

                        if (!string.IsNullOrEmpty(customDpiVal))

                        {

                            string[] parts = customDpiVal.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);

                            List<int> loaded = new List<int>();

                            for (int i = 0; i < parts.Length && loaded.Count < 5; i++)

                            {

                                int pDpi;

                                if (int.TryParse(parts[i].Trim(), out pDpi) && pDpi >= 100 && pDpi <= 20000)

                                {

                                    loaded.Add(pDpi);

                                }

                            }

                            if (loaded.Count > 0)

                            {

                                dpiStageList = loaded;

                                dpiStageValues = dpiStageList.ToArray();

                            }

                        }



                        var wxVal = key.GetValue("WindowX");

                        if (wxVal != null) savedWindowX = (int)wxVal;

                        var wyVal = key.GetValue("WindowY");

                        if (wyVal != null) savedWindowY = (int)wyVal;

                    }

                }

            }

            catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.cs: 已忽略异常", diagEx); }



            CleanupLegacyAutoStart();

            bool autoStart = IsAutoStartEnabled();

            if (autoStart)

            {

                SyncAutoStartRegistry();

            }



            isUpdatingUI = true;

            try

            {

                UpdateMenuStatusTexts();

                if (chkLowAlert != null) chkLowAlert.Checked = lowBatteryAlertEnabled;

                if (chkDpiOsd != null) chkDpiOsd.Checked = dpiOsdEnabled;

                if (chkAutoStart != null) chkAutoStart.Checked = autoStart;

                if (chkAutoStartShowUI != null)

                {

                    chkAutoStartShowUI.Checked = autoStartShowMainWindow;

                    chkAutoStartShowUI.Enabled = autoStart;

                }

                if (autoStartShowUIMenuItem != null)

                {

                    autoStartShowUIMenuItem.Checked = autoStartShowMainWindow;

                    autoStartShowUIMenuItem.Enabled = autoStart;

                }



                if (styleCapsuleItem != null) styleCapsuleItem.Checked = (trayStyle == 0);

                if (styleNumItem != null) styleNumItem.Checked = (trayStyle == 1);

                if (stylePureNumItem != null) stylePureNumItem.Checked = (trayStyle == 2);

                if (btnStyleCapsule != null) btnStyleCapsule.Selected = (trayStyle == 0);

                if (btnStyleNum != null) btnStyleNum.Selected = (trayStyle == 1);

                if (btnStylePureNum != null) btnStylePureNum.Selected = (trayStyle == 2);



                if (osdForm != null) osdForm.OsdStyle = osdStyle;

                if (osdStyleCapsuleItem != null) osdStyleCapsuleItem.Checked = (osdStyle == 0);

                if (osdStyleGaugeItem != null) osdStyleGaugeItem.Checked = (osdStyle == 1);

                if (osdStyleCompactItem != null) osdStyleCompactItem.Checked = (osdStyle == 2);

                if (btnOsdStyleCapsule != null) btnOsdStyleCapsule.Selected = (osdStyle == 0);

                if (btnOsdStyleGauge != null) btnOsdStyleGauge.Selected = (osdStyle == 1);

                if (btnOsdStyleCompact != null) btnOsdStyleCompact.Selected = (osdStyle == 2);



                if (int30sMenuItem != null) int30sMenuItem.Checked = (userSelectedInterval == 30000);

                if (int10sMenuItem != null) int10sMenuItem.Checked = (userSelectedInterval == 10000);

                if (int1mMenuItem != null) int1mMenuItem.Checked = (userSelectedInterval == 60000);

                if (int5mMenuItem != null) int5mMenuItem.Checked = (userSelectedInterval == 300000);

                ApplyPresetSelection(userSelectedInterval);



                if (chkAutoGameRate != null) chkAutoGameRate.Checked = autoGameRateEnabled;

                if (autoGameRateToggleItem != null) autoGameRateToggleItem.Checked = autoGameRateEnabled;

                if (chkAutoGameDpi != null) chkAutoGameDpi.Checked = autoGameDpiEnabled;

                if (autoGameDpiToggleItem != null) autoGameDpiToggleItem.Checked = autoGameDpiEnabled;

                if (chkNotifyOsd != null) chkNotifyOsd.Checked = gameRateNotifyOsd;

                if (chkNotifyToast != null) chkNotifyToast.Checked = gameRateNotifyToast;

                if (chkNotifySound != null) chkNotifySound.Checked = gameRateNotifySound;

                UpdateGameRateButtonSelection(gamePollingRate);

                UpdateGameDpiButtonSelection(gameDpiStage);

                UpdateDesktopRateButtonSelection(desktopPollingRate);

                UpdateManageGamesButtonText();

                SetAutoGameRateEnabled(autoGameRateEnabled);

                SetAutoGameDpiEnabled(autoGameDpiEnabled);

            }

            finally

            {

                isUpdatingUI = false;

            }

            RelayoutDpiButtons();

        }



        private void SaveConfig()

        {

            try

            {

                using (var key = Registry.CurrentUser.CreateSubKey(ConfigRegistryKey))

                {

                    if (key != null)

                    {

                        key.SetValue("LowBatteryAlert", lowBatteryAlertEnabled ? 1 : 0);

                        key.SetValue("DpiOsdAlert", dpiOsdEnabled ? 1 : 0);

                        key.SetValue("AutoStartShowUI", autoStartShowMainWindow ? 1 : 0);

                        key.SetValue("TrayIconStyle", trayStyle);

                        key.SetValue("OsdStyle", osdStyle);

                        key.SetValue("RefreshInterval", userSelectedInterval);

                        key.SetValue("AutoGameOptEnabled", (autoGameRateEnabled || autoGameDpiEnabled) ? 1 : 0);

                        key.SetValue("AutoGameRateEnabled", autoGameRateEnabled ? 1 : 0);

                        key.SetValue("AutoGameDpiEnabled", autoGameDpiEnabled ? 1 : 0);

                        key.SetValue("GameTargetPollingRate", gamePollingRate);

                        key.SetValue("GameTargetDpiStage", gameDpiStage);

                        key.SetValue("DesktopPollingRate", desktopPollingRate);

                        key.SetValue("GameRateNotifyOsd", gameRateNotifyOsd ? 1 : 0);

                        key.SetValue("GameRateNotifyToast", gameRateNotifyToast ? 1 : 0);

                        key.SetValue("GameRateNotifySound", gameRateNotifySound ? 1 : 0);

                        if (monitoredGameProcesses != null)

                        {

                            string procListStr = string.Join(";", monitoredGameProcesses.ToArray());

                            key.SetValue("MonitoredGameProcesses", procListStr);

                        }

                        key.SetValue("FallbackFullscreenEnabled", fallbackFullscreenEnabled ? 1 : 0);
                        key.SetValue("GameOptMode", gameOptMode);

                        if (savedWindowX > -3000 && savedWindowY > -3000)

                        {

                            key.SetValue("WindowX", savedWindowX);

                            key.SetValue("WindowY", savedWindowY);

                        }

                    }

                }

            }

            catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.cs: 已忽略异常", diagEx); }

        }



        public static void CleanupLegacyAutoStart()
        {
            // 由 StartupManager 统一维护与清理历史启动项
        }

        private bool IsAutoStartEnabled()
        {
            return StartupManager.IsAutoStartConfigured();
        }

        private void SyncAutoStartRegistry()
        {
            StartupManager.SyncAutoStartRegistry();
        }

        private void SetAutoStart(bool enabled, bool showNotification = false)
        {
            string errMsg;
            bool ok = StartupManager.SetAutoStart(enabled, out errMsg);
            if (!ok && !string.IsNullOrEmpty(errMsg))
            {
                MessageBox.Show("设置开机启动失败: " + errMsg, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            isUpdatingUI = true;

            try

            {

                UpdateMenuStatusTexts();

                if (chkAutoStart != null && chkAutoStart.Checked != enabled) chkAutoStart.Checked = enabled;

                if (chkAutoStartShowUI != null) chkAutoStartShowUI.Enabled = enabled;

                if (autoStartShowUIMenuItem != null) autoStartShowUIMenuItem.Enabled = enabled;

            }

            finally

            {

                isUpdatingUI = false;

            }



        }



        private void SetAutoStartShowUI(bool showUI, bool showNotification = false)

        {

            autoStartShowMainWindow = showUI;

            SaveConfig();



            isUpdatingUI = true;

            try

            {

                if (chkAutoStartShowUI != null && chkAutoStartShowUI.Checked != showUI) chkAutoStartShowUI.Checked = showUI;

                if (autoStartShowUIMenuItem != null) autoStartShowUIMenuItem.Checked = showUI;

            }

            finally

            {

                isUpdatingUI = false;

            }

        }



        private void SetLowBatteryAlert(bool enabled, bool showNotification = false)

        {

            lowBatteryAlertEnabled = enabled;



            isUpdatingUI = true;

            try

            {

                UpdateMenuStatusTexts();

                if (chkLowAlert != null && chkLowAlert.Checked != enabled) chkLowAlert.Checked = enabled;

            }

            finally

            {

                isUpdatingUI = false;

            }



            SaveConfig();

        }




    }

}
