// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// 1. 目标平台与编译规范：严格维持 C# 5.0 语法标准与 .NET Framework 4.0 兼容性。严禁引入任何第三方包或依赖。
// 2. 编码规范：所有文件严格保存为 UTF-8 带签名（BOM）格式，防止中文字符乱码。
// 3. 架构规范：本文件属于 MainForm 的分部类 (partial class) 之一。修改时请保持职责清晰，任何新增方法或逻辑请同步维护 ARCHITECTURE.md。
// 模块描述: MainForm 性能卡片渲染（续航/温度/电量趋势单块大方法）
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

        private void UpdatePerformanceSection(MouseBatteryInfo info, Color brandAccent)
        {
            if (info == null) return;
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
            bool isZaopinMouse = ((info.Brand == "Zaopin" || info.Brand == "皂品") && info.Category == DeviceCategory.Mouse);

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
                            catch (Exception diagEx) { Diagnostics.Log("UI/Forms/MainForm.Hardware.Performance.cs: 已忽略异常", diagEx); }
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
                            string chgStatus = isKbWired ? "USB 持续供电" : (info.IsCharging ? "正在充电" : "电池供电");
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
            else if (isRazer || isValkyrieMouse || isLogitechMouse || isRapooMouse || isZaopinMouse || isFdasMouse)
            {
                if (lblPerfTitle != null)
                {
                    if (isValkyrieMouse) lblPerfTitle.Text = "瓦尔基里鼠标性能与档位调节";
                    else if (isLogitechMouse) lblPerfTitle.Text = "罗技鼠标性能与档位调节";
                    else if (isRapooMouse) lblPerfTitle.Text = "雷柏鼠标性能与档位调节";
                    else if (isZaopinMouse) lblPerfTitle.Text = "皂品鼠标性能与档位调节";
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

                // 档位集合变了就重排按钮（文案与数量都在 RelayoutPerformanceControls 里重建）。
                string rateSig = string.Join("/", Array.ConvertAll(pollingRateValues, x => x.ToString()));
                if (rateSig != lastRenderedRateSig)
                {
                    lastRenderedRateSig = rateSig;
                    try
                    {
                        this.BeginInvoke((MethodInvoker)delegate
                        {
                            RelayoutPerformanceControls();
                            UpdatePollingRateDisplayStates();
                        });
                    }
                    catch (Exception sigEx)
                    {
                        Diagnostics.Log("MainForm.Hardware.Performance.cs: 重排回报率按钮失败", sigEx);
                    }
                }


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

                // 按设备回报率上限裁剪，过滤超出上限的档位（如皂品 Z2 4K 版上限 4000Hz，杜绝 8K 档位出现）
                if (info.MaxPollingRate > 0 && pollingRateValues != null)
                {
                    List<int> cappedList = new List<int>();
                    for (int i = 0; i < pollingRateValues.Length; i++)
                    {
                        if (pollingRateValues[i] <= info.MaxPollingRate)
                        {
                            cappedList.Add(pollingRateValues[i]);
                        }
                    }
                    if (cappedList.Count > 0) pollingRateValues = cappedList.ToArray();
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
                                if (isValkyrieMouse || isLogitechMouse || isRapooMouse || isRazer || isZaopinMouse || isFdasMouse)
                                {
                                    btnRates[i].Selected = (pollingRateValues[i] == info.PollingRate);
                                }
                            }
                        }
                    }
                }

                RelayoutPerformanceControls();
                SetGameRateControlsVisibility(isOptExpanded);

                // 设备值优先：原来条件写成 desktopPollingRate == 0，只在首次初始化时同步，
                // 之后用官方驱动改了回报率，界面永远显示旧值（实测 2026-10-30）。
                // 改为「与设备读回值不一致就同步」，保证界面反映设备真实状态。
                // 电竞回报率激活时不同步——那期间设备是电竞档位，不是桌面档位。
                // 不再用 supportsAutoGameRate 作前置条件：该判定为 false 时，
                // 同步与后面的 UpdatePollingRateDisplayStates 会被一起跳过，
                // 界面就永远停在旧值（实测 2026-10-30 用官方驱动改回报率后界面不动）。
                if (!autoGameRateEnabled && info.PollingRate > 0 && desktopPollingRate != info.PollingRate)
                {
                    desktopPollingRate = info.PollingRate;
                    // 必须延后执行：本方法由 ApplyDeviceStatus 调用，紧接着还要更新托盘图标。
                    // 直接在这里刷 UI 会在托盘绘制之前插入一次重排，2026-10-30 实测触发过
                    // 「索引超出了数组界限」弹窗（栈顶在托盘数字绘制）。
                    try
                    {
                        this.BeginInvoke((MethodInvoker)delegate
                        {
                            // 只刷选中态不够：按钮的文案和数量是在 RelayoutPerformanceControls
                            // 里按 pollingRateValues 重建的。启动时先按默认值 {1000,2000,4000,8000}
                            // 排过一次版，数据到达后若不重排，8000 会一直留在界面上。
                            RelayoutPerformanceControls();
                            UpdatePollingRateDisplayStates();
                        });
                    }
                    catch (Exception rateEx)
                    {
                        Diagnostics.Log("MainForm.Hardware.Performance.cs: 延后刷新回报率失败", rateEx);
                    }
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

    }
}
