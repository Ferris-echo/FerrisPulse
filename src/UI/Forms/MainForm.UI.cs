// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// 1. 目标平台与编译规范：严格维持 C# 5.0 语法标准与 .NET Framework 4.0 兼容性。严禁引入任何第三方包或依赖。
// 2. 编码规范：所有文件严格保存为 UTF-8 带签名（BOM）格式，防止中文字符乱码。
// 3. 架构规范：本文件属于 MainForm 的分部类 (partial class) 之一。修改时请保持职责清晰，任何新增方法或逻辑请同步维护 ARCHITECTURE.md。
// 模块描述: MainForm 界面树装配 (InitializeFormUI)、卡片动画折叠与二级对话框路由
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
        private void InitializeFormUI()
        {
            this.Text = "FerrisPulse · 灵脉";
            this.FormBorderStyle = FormBorderStyle.FixedSingle;
            this.MaximizeBox = false;
            this.MinimizeBox = true;
            this.StartPosition = FormStartPosition.Manual;
            this.BackColor = Color.FromArgb(18, 19, 23);
            this.ForeColor = Color.White;
            this.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.ShowInTaskbar = true;
            this.DoubleBuffered = true;
            this.LocationChanged += (s, e) => {
                if (switcherFlyout != null && !switcherFlyout.IsDisposed && switcherFlyout.Visible)
                {
                    switcherFlyout.Close();
                }
                if (!isRestoringPosition && this.WindowState == FormWindowState.Normal && this.Visible && this.Location.X > -3000 && this.Location.Y > -3000)
                {
                    savedWindowX = this.Location.X;
                    savedWindowY = this.Location.Y;
                }
            };
            this.ResizeEnd += (s, e) => {
                if (!isRestoringPosition && this.WindowState == FormWindowState.Normal && this.Visible && this.Location.X > -3000 && this.Location.Y > -3000)
                {
                    savedWindowX = this.Location.X;
                    savedWindowY = this.Location.Y;
                    SaveConfig();
                }
            };

            int padX = (int)(18 * dpiScale);
            int baseW = (int)(470 * dpiScale);
            int cardW = baseW - (padX * 2);

            // ================= TOP BAR: SCHEME B FOCUS SELECTOR + BUTTONS =================
            int barH = (int)(38 * dpiScale);
            int btnSize = barH;
            int topGap = (int)(6 * dpiScale);
            int selectorW = cardW - (btnSize * 3) - (topGap * 3);

            focusDeviceSelector = new FocusDeviceSelector();
            focusDeviceSelector.DpiScale = dpiScale;
            focusDeviceSelector.Location = new Point(padX, (int)(12 * dpiScale));
            focusDeviceSelector.Size = new Size(selectorW, barH);
            focusDeviceSelector.SelectorClicked += () => {
                ToggleDeviceSwitcherFlyout();
            };
            this.Controls.Add(focusDeviceSelector);

            btnTopPriority = new ModernButton();
            btnTopPriority.Text = "📶";
            btnTopPriority.Font = new Font("Segoe UI Emoji", 10F);
            btnTopPriority.CornerRadius = (int)(8 * dpiScale);
            btnTopPriority.NormalColor = Color.FromArgb(25, 27, 34);
            btnTopPriority.HoverColor = Color.FromArgb(36, 40, 52);
            btnTopPriority.PressedColor = Color.FromArgb(20, 22, 28);
            btnTopPriority.BorderColor = Color.FromArgb(45, 50, 65);
            btnTopPriority.ForeColor = Color.White;
            btnTopPriority.Location = new Point(padX + selectorW + topGap, (int)(12 * dpiScale));
            btnTopPriority.Size = new Size(btnSize, barH);
            btnTopPriority.Click += (s, e) => {
                if (switcherFlyout != null && !switcherFlyout.IsDisposed && switcherFlyout.Visible) switcherFlyout.Close();
                OpenPriorityManager();
            };
            this.Controls.Add(btnTopPriority);

            btnTopAddDevice = new ModernButton();
            btnTopAddDevice.Text = "➕";
            btnTopAddDevice.Font = new Font("Segoe UI Emoji", 10F);
            btnTopAddDevice.CornerRadius = (int)(8 * dpiScale);
            btnTopAddDevice.NormalColor = Color.FromArgb(25, 27, 34);
            btnTopAddDevice.HoverColor = Color.FromArgb(36, 40, 52);
            btnTopAddDevice.PressedColor = Color.FromArgb(20, 22, 28);
            btnTopAddDevice.BorderColor = Color.FromArgb(45, 50, 65);
            btnTopAddDevice.ForeColor = Color.White;
            btnTopAddDevice.Location = new Point(padX + selectorW + topGap + btnSize + topGap, (int)(12 * dpiScale));
            btnTopAddDevice.Size = new Size(btnSize, barH);
            btnTopAddDevice.Click += (s, e) => {
                if (switcherFlyout != null && !switcherFlyout.IsDisposed && switcherFlyout.Visible) switcherFlyout.Close();
                OpenAddDeviceWizard();
            };
            this.Controls.Add(btnTopAddDevice);

            btnTopAbout = new ModernButton();
            btnTopAbout.IsInfoButton = true;
            btnTopAbout.Text = "ⓘ";
            btnTopAbout.CornerRadius = (int)(8 * dpiScale);
            btnTopAbout.NormalColor = Color.FromArgb(25, 27, 34);
            btnTopAbout.HoverColor = Color.FromArgb(36, 40, 52);
            btnTopAbout.PressedColor = Color.FromArgb(20, 22, 28);
            btnTopAbout.BorderColor = Color.FromArgb(45, 50, 65);
            btnTopAbout.ForeColor = Color.White;
            btnTopAbout.Location = new Point(padX + selectorW + topGap + (btnSize + topGap) * 2, (int)(12 * dpiScale));
            btnTopAbout.Size = new Size(btnSize, barH);
            btnTopAbout.Click += (s, e) => {
                if (switcherFlyout != null && !switcherFlyout.IsDisposed && switcherFlyout.Visible) switcherFlyout.Close();
                OpenAboutForm();
            };
            this.Controls.Add(btnTopAbout);

            toolTipTop = new ToolTip();
            toolTipTop.SetToolTip(btnTopPriority, "设备优先级管理 (拖拽排序)");
            toolTipTop.SetToolTip(btnTopAddDevice, "手动添加新外设向导 (耳机/手柄/键鼠)");
            toolTipTop.SetToolTip(btnTopAbout, "关于 FerrisPulse & 赞赏支持");

            // ================= CARD 1: BATTERY & DEVICE STATUS =================
            int card1Y = (int)(12 * dpiScale) + barH + (int)(10 * dpiScale);
            int card1H = (int)(176 * dpiScale);

            cardBattery = new RoundedCard();
            cardBattery.Location = new Point(padX, card1Y);
            cardBattery.Size = new Size(cardW, card1H);
            cardBattery.CornerRadius = (int)(10 * dpiScale);
            cardBattery.CardColor = Color.FromArgb(25, 27, 34);
            cardBattery.BorderColor = Color.FromArgb(42, 46, 58);
            this.Controls.Add(cardBattery);

            int badgeW = (int)(108 * dpiScale);
            int badgeH = (int)(26 * dpiScale);
            int badgeX = cardW - badgeW - (int)(16 * dpiScale);
            int badgeY = (int)(14 * dpiScale);

            badgePrimary = new StatusPill();
            badgePrimary.Location = new Point(badgeX, badgeY);
            badgePrimary.Size = new Size(badgeW, badgeH);
            badgePrimary.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold, GraphicsUnit.Point);
            badgePrimary.SetStatus("● 任务栏常驻", Color.FromArgb(0, 230, 118));
            badgePrimary.Visible = false;
            cardBattery.Controls.Add(badgePrimary);

            int nameX = (int)(20 * dpiScale);
            int nameY = (int)(14 * dpiScale);
            int nameMaxW = badgeX - nameX - (int)(10 * dpiScale);

            lblDeviceName = new Label();
            lblDeviceName.Text = "正在检测设备...";
            lblDeviceName.Font = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Bold, GraphicsUnit.Point);
            lblDeviceName.ForeColor = Color.White;
            lblDeviceName.BackColor = Color.Transparent;
            lblDeviceName.Location = new Point(nameX, nameY);
            lblDeviceName.AutoSize = false;
            lblDeviceName.Size = new Size(nameMaxW, (int)(24 * dpiScale));
            lblDeviceName.AutoEllipsis = true;
            lblDeviceName.Cursor = Cursors.Hand;
            lblDeviceName.Click += (s, e) => RenameCurrentDevice();
            toolTipTop.SetToolTip(lblDeviceName, "点击即可重命名当前设备 (支持中文与个性化昵称)");
            cardBattery.Controls.Add(lblDeviceName);

            lblBatteryBig = new Label();
            lblBatteryBig.Text = "--%";
            lblBatteryBig.Font = new Font("Microsoft YaHei UI", 34F, FontStyle.Bold, GraphicsUnit.Point);
            lblBatteryBig.ForeColor = Color.FromArgb(0, 230, 118);
            lblBatteryBig.BackColor = Color.Transparent;
            lblBatteryBig.Location = new Point((int)(18 * dpiScale), (int)(46 * dpiScale));
            lblBatteryBig.AutoSize = true;
            cardBattery.Controls.Add(lblBatteryBig);

            // Status Pill: Power / Charging State (sole status badge in Card 1)
            pillStatus = new StatusPill();
            pillStatus.Location = new Point((int)(150 * dpiScale), (int)(56 * dpiScale));
            pillStatus.Size = new Size((int)(80 * dpiScale), (int)(32 * dpiScale));
            pillStatus.Font = new Font("Microsoft YaHei UI", 9.0F, FontStyle.Bold, GraphicsUnit.Point);
            pillStatus.SetStatus("● 正常运行", Color.FromArgb(0, 230, 118));
            cardBattery.Controls.Add(pillStatus);

            barBattery = new ModernProgressBar();
            barBattery.Location = new Point((int)(20 * dpiScale), (int)(118 * dpiScale));
            barBattery.Size = new Size(cardW - (int)(40 * dpiScale), (int)(12 * dpiScale));
            barBattery.Value = 0;
            barBattery.TrackColor = Color.FromArgb(38, 42, 53);
            barBattery.ProgressColor = Color.FromArgb(0, 230, 118);
            cardBattery.Controls.Add(barBattery);

            lblUpdateTime = new Label();
            lblUpdateTime.Text = "最后同步: --:--:-- · 自动侦测硬件插拔";
            lblUpdateTime.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
            lblUpdateTime.ForeColor = Color.FromArgb(135, 142, 156);
            lblUpdateTime.BackColor = Color.Transparent;
            lblUpdateTime.Location = new Point((int)(20 * dpiScale), (int)(142 * dpiScale));
            lblUpdateTime.AutoSize = true;
            cardBattery.Controls.Add(lblUpdateTime);

            // ================= CARD 2: PERFORMANCE & TUNING =================
            int card2Y = card1Y + card1H + (int)(12 * dpiScale);
            int card2H = autoGameRateEnabled ? (int)(218 * dpiScale) : (int)(148 * dpiScale);

            cardPerformance = new RoundedCard();
            cardPerformance.Location = new Point(padX, card2Y);
            cardPerformance.Size = new Size(cardW, card2H);
            cardPerformance.CornerRadius = (int)(10 * dpiScale);
            cardPerformance.CardColor = Color.FromArgb(25, 27, 34);
            cardPerformance.BorderColor = Color.FromArgb(42, 46, 58);
            this.Controls.Add(cardPerformance);

            int cPad = (int)(18 * dpiScale);
            int secW = cardW - (cPad * 2);

            lblPerfTitle = new Label();
            lblPerfTitle.Text = "鼠标性能与档位调节";
            lblPerfTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);
            lblPerfTitle.ForeColor = Color.FromArgb(240, 245, 255);
            lblPerfTitle.BackColor = Color.Transparent;
            lblPerfTitle.Location = new Point((int)(20 * dpiScale), (int)(12 * dpiScale));
            lblPerfTitle.AutoSize = true;
            cardPerformance.Controls.Add(lblPerfTitle);

            lblLiveRateBadge = new Label();
            lblLiveRateBadge.Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Regular, GraphicsUnit.Point);
            lblLiveRateBadge.ForeColor = Color.FromArgb(0, 230, 118);
            lblLiveRateBadge.BackColor = Color.Transparent;
            lblLiveRateBadge.TextAlign = ContentAlignment.MiddleRight;
            lblLiveRateBadge.Location = new Point(cardW - cPad - (int)(220 * dpiScale), (int)(14 * dpiScale));
            lblLiveRateBadge.Size = new Size((int)(220 * dpiScale), (int)(20 * dpiScale));
            lblLiveRateBadge.Visible = false;
            cardPerformance.Controls.Add(lblLiveRateBadge);

            // Row 1: DPI 档位
            int r1Y = (int)(38 * dpiScale);
            int lblW = (int)(86 * dpiScale);
            int segH = (int)(28 * dpiScale);

            lblDpiTitle = new Label();
            lblDpiTitle.Text = "DPI 档位";
            lblDpiTitle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            lblDpiTitle.ForeColor = Color.FromArgb(160, 168, 185);
            lblDpiTitle.BackColor = Color.Transparent;
            lblDpiTitle.Location = new Point(cPad, (int)(41 * dpiScale));
            lblDpiTitle.Size = new Size(lblW, (int)(22 * dpiScale));
            cardPerformance.Controls.Add(lblDpiTitle);

            btnDpiStages = new ModernSegmentButton[5];
            for (int i = 0; i < 5; i++)
            {
                int index = i;
                var btn = new ModernSegmentButton();
                btn.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
                btn.Click += (s, e) => {
                    SwitchDpiStageByIndex(index);
                };
                cardPerformance.Controls.Add(btn);
                btnDpiStages[i] = btn;
            }

            btnDpiManage = new ModernButton();
            btnDpiManage.IsGearButton = true;
            btnDpiManage.CornerRadius = (int)(6 * dpiScale);
            btnDpiManage.Cursor = Cursors.Hand;
            btnDpiManage.Click += (s, e) => OpenDpiManagementDialog();
            toolTipTop.SetToolTip(btnDpiManage, "自定义 DPI 档位与板载设置");
            cardPerformance.Controls.Add(btnDpiManage);

            RelayoutDpiButtons();

            // Row 2: 回报率
            int r2Y = (int)(75 * dpiScale);

            lblRateTitle = new Label();
            lblRateTitle.Text = "常规回报率";
            lblRateTitle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            lblRateTitle.ForeColor = Color.FromArgb(160, 168, 185);
            lblRateTitle.BackColor = Color.Transparent;
            lblRateTitle.Location = new Point(cPad, (int)(78 * dpiScale));
            lblRateTitle.Size = new Size(lblW, (int)(22 * dpiScale));
            cardPerformance.Controls.Add(lblRateTitle);

            int hzStartX = cPad + lblW;
            int hzGap = (int)(6 * dpiScale);
            int hzSegW = (secW - lblW - (hzGap * 3)) / 4;

            btnRates = new ModernSegmentButton[4];
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                int hzVal = pollingRateValues[i];
                var btn = new ModernSegmentButton();
                btn.Text = hzVal + " Hz";
                btn.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
                btn.Location = new Point(hzStartX + i * (hzSegW + hzGap), r2Y);
                btn.Size = new Size(hzSegW, segH);
                btn.Click += (s, e) => SetPollingRateFromUI(pollingRateValues[index]);
                cardPerformance.Controls.Add(btn);
                btnRates[i] = btn;
            }

            // Row 3: 全屏游戏自动切换回报率 (开关 + 游戏管理按钮)
            int gameSwY = (int)(112 * dpiScale);
            int gameChkW = (int)(180 * dpiScale);
            chkAutoGameRate = new ModernCheckBox();
            chkAutoGameRate.Text = "全屏游戏自动切换回报率";
            chkAutoGameRate.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            chkAutoGameRate.ForeColor = Color.FromArgb(210, 220, 240);
            chkAutoGameRate.Location = new Point(cPad, gameSwY);
            chkAutoGameRate.Size = new Size(gameChkW, (int)(26 * dpiScale));
            chkAutoGameRate.CheckedChanged += (s, e) => {
                if (!isUpdatingUI) SetAutoGameRateEnabled(chkAutoGameRate.Checked);
            };
            cardPerformance.Controls.Add(chkAutoGameRate);

            btnManageGames = new ModernButton();
            UpdateManageGamesButtonText();
            btnManageGames.Font = new Font("Microsoft YaHei UI", 8.2F);
            btnManageGames.NormalColor = Color.FromArgb(28, 32, 44);
            btnManageGames.HoverColor = Color.FromArgb(42, 48, 64);
            btnManageGames.PressedColor = Color.FromArgb(20, 24, 34);
            btnManageGames.BorderColor = Color.FromArgb(50, 58, 76);
            btnManageGames.TextColor = Color.FromArgb(0, 230, 118);
            btnManageGames.CornerRadius = (int)(5 * dpiScale);
            btnManageGames.Cursor = Cursors.Hand;
            int btnMgrX = cPad + gameChkW + (int)(6 * dpiScale);
            int btnMgrW = secW - gameChkW - (int)(6 * dpiScale);
            btnManageGames.Location = new Point(btnMgrX, gameSwY);
            btnManageGames.Size = new Size(btnMgrW, (int)(26 * dpiScale));
            btnManageGames.Click += (s, e) => OpenGameProcessManager();
            cardPerformance.Controls.Add(btnManageGames);

            // Row 4: 电竞回报率选择
            int gameRateY = (int)(147 * dpiScale);
            lblGameRateTitle = new Label();
            lblGameRateTitle.Text = "电竞回报率";
            lblGameRateTitle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            lblGameRateTitle.ForeColor = Color.FromArgb(160, 168, 185);
            lblGameRateTitle.BackColor = Color.Transparent;
            lblGameRateTitle.Location = new Point(cPad, (int)(150 * dpiScale));
            lblGameRateTitle.Size = new Size(lblW, (int)(22 * dpiScale));
            lblGameRateTitle.Visible = false;
            cardPerformance.Controls.Add(lblGameRateTitle);

            btnGameRates = new ModernSegmentButton[4];
            for (int i = 0; i < 4; i++)
            {
                int index = i;
                int hzVal = pollingRateValues[i];
                var btn = new ModernSegmentButton();
                btn.Text = hzVal + " Hz";
                btn.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
                btn.Location = new Point(hzStartX + i * (hzSegW + hzGap), gameRateY);
                btn.Size = new Size(hzSegW, segH);
                btn.Visible = false;
                btn.Click += (s, e) => SetGamePollingRateFromUI(pollingRateValues[index]);
                cardPerformance.Controls.Add(btn);
                btnGameRates[i] = btn;
            }

            // Row 5: 提醒方式设置
            int gameNotifY = (int)(184 * dpiScale);
            lblNotifyModeTitle = new Label();
            lblNotifyModeTitle.Text = "切换提醒:";
            lblNotifyModeTitle.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
            lblNotifyModeTitle.ForeColor = Color.FromArgb(140, 150, 170);
            lblNotifyModeTitle.BackColor = Color.Transparent;
            lblNotifyModeTitle.Location = new Point(cPad, (int)(185 * dpiScale));
            lblNotifyModeTitle.Size = new Size((int)(62 * dpiScale), (int)(20 * dpiScale));
            lblNotifyModeTitle.Visible = false;
            cardPerformance.Controls.Add(lblNotifyModeTitle);

            int notifStartX = cPad + (int)(64 * dpiScale);
            int notifW = (secW - (int)(64 * dpiScale)) / 3;

            chkNotifyOsd = new ModernCheckBox();
            chkNotifyOsd.Text = "OSD菜单";
            chkNotifyOsd.Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Regular, GraphicsUnit.Point);
            chkNotifyOsd.ForeColor = Color.FromArgb(170, 180, 200);
            chkNotifyOsd.Location = new Point(notifStartX, gameNotifY);
            chkNotifyOsd.Size = new Size(notifW, (int)(22 * dpiScale));
            chkNotifyOsd.Visible = false;
            chkNotifyOsd.CheckedChanged += (s, e) => {
                if (!isUpdatingUI) { gameRateNotifyOsd = chkNotifyOsd.Checked; SaveConfig(); }
            };
            cardPerformance.Controls.Add(chkNotifyOsd);

            chkNotifyToast = new ModernCheckBox();
            chkNotifyToast.Text = "系统通知";
            chkNotifyToast.Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Regular, GraphicsUnit.Point);
            chkNotifyToast.ForeColor = Color.FromArgb(170, 180, 200);
            chkNotifyToast.Location = new Point(notifStartX + notifW, gameNotifY);
            chkNotifyToast.Size = new Size(notifW, (int)(22 * dpiScale));
            chkNotifyToast.Visible = false;
            chkNotifyToast.CheckedChanged += (s, e) => {
                if (!isUpdatingUI) { gameRateNotifyToast = chkNotifyToast.Checked; SaveConfig(); }
            };
            cardPerformance.Controls.Add(chkNotifyToast);

            chkNotifySound = new ModernCheckBox();
            chkNotifySound.Text = "柔和提示音";
            chkNotifySound.Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Regular, GraphicsUnit.Point);
            chkNotifySound.ForeColor = Color.FromArgb(170, 180, 200);
            chkNotifySound.Location = new Point(notifStartX + notifW * 2, gameNotifY);
            chkNotifySound.Size = new Size(notifW, (int)(22 * dpiScale));
            chkNotifySound.Visible = false;
            chkNotifySound.CheckedChanged += (s, e) => {
                if (!isUpdatingUI) { gameRateNotifySound = chkNotifySound.Checked; SaveConfig(); }
            };
            cardPerformance.Controls.Add(chkNotifySound);

            // Rapoo Status Tile (Displayed when Rapoo device is selected - full width, DPI only)
            int tileH = (int)(74 * dpiScale);

            tileRapooDpi = new RapooStatusTile();
            tileRapooDpi.DpiScale = dpiScale;
            tileRapooDpi.Location = new Point(cPad, (int)(38 * dpiScale));
            tileRapooDpi.Size = new Size(secW, tileH);
            tileRapooDpi.Title = "当前实时 DPI";
            tileRapooDpi.Badge = "";
            tileRapooDpi.ValueText = "1600";
            tileRapooDpi.UnitText = "DPI";
            tileRapooDpi.Footnote = "按鼠标机身 DPI 键循环切档 · 硬件级实时监听";
            tileRapooDpi.Visible = false;
            cardPerformance.Controls.Add(tileRapooDpi);

            tileHeadsetAudio = new HeadsetStatusTile();
            tileHeadsetAudio.DpiScale = dpiScale;
            tileHeadsetAudio.Location = new Point(cPad, (int)(38 * dpiScale));
            tileHeadsetAudio.Size = new Size(secW, tileH);
            tileHeadsetAudio.Title = "蓝牙音频连接";
            tileHeadsetAudio.Footnote = "Windows 原生蓝牙音频协议 · 自动双模监听";
            tileHeadsetAudio.Visible = false;
            cardPerformance.Controls.Add(tileHeadsetAudio);

            // Gamepad Interactive Controls
            btnGamepadVibrate = new ModernButton();
            btnGamepadVibrate.Text = "🎮 触发双震区触觉测试";
            btnGamepadVibrate.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold);
            btnGamepadVibrate.NormalColor = Color.FromArgb(40, 48, 68);
            btnGamepadVibrate.HoverColor = Color.FromArgb(60, 72, 100);
            btnGamepadVibrate.PressedColor = Color.FromArgb(30, 36, 52);
            btnGamepadVibrate.BorderColor = Color.FromArgb(80, 100, 140);
            btnGamepadVibrate.TextColor = Color.FromArgb(235, 240, 255);
            btnGamepadVibrate.CornerRadius = (int)(6 * dpiScale);
            btnGamepadVibrate.Cursor = Cursors.Hand;
            btnGamepadVibrate.Visible = false;
            btnGamepadVibrate.Click += (s, e) => {
                XInputHelper.TriggerGamepadVibration(400);
            };
            cardPerformance.Controls.Add(btnGamepadVibrate);

            btnGamepadJoyCpl = new ModernButton();
            btnGamepadJoyCpl.Text = "🕹️ 打开游戏控制器校准 (joy.cpl)";
            btnGamepadJoyCpl.Font = new Font("Microsoft YaHei UI", 8.5F);
            btnGamepadJoyCpl.NormalColor = Color.FromArgb(28, 32, 44);
            btnGamepadJoyCpl.HoverColor = Color.FromArgb(42, 48, 64);
            btnGamepadJoyCpl.PressedColor = Color.FromArgb(20, 24, 34);
            btnGamepadJoyCpl.BorderColor = Color.FromArgb(50, 58, 76);
            btnGamepadJoyCpl.TextColor = Color.FromArgb(190, 200, 220);
            btnGamepadJoyCpl.CornerRadius = (int)(6 * dpiScale);
            btnGamepadJoyCpl.Cursor = Cursors.Hand;
            btnGamepadJoyCpl.Visible = false;
            btnGamepadJoyCpl.Click += (s, e) => {
                try { System.Diagnostics.Process.Start("joy.cpl"); } catch { }
            };
            cardPerformance.Controls.Add(btnGamepadJoyCpl);

            // Universal Keyboard Interactive Controls
            hudThreeLocks = new ThreeLocksHudControl();
            hudThreeLocks.DpiScale = dpiScale;
            hudThreeLocks.Visible = false;
            cardPerformance.Controls.Add(hudThreeLocks);

            btnUniversalWinLock = new ModernButton();
            btnUniversalWinLock.Text = "🛡️ Win 键防误触: 未锁定";
            btnUniversalWinLock.Font = new Font("Microsoft YaHei UI", 8.5F);
            btnUniversalWinLock.NormalColor = Color.FromArgb(28, 32, 44);
            btnUniversalWinLock.HoverColor = Color.FromArgb(42, 48, 64);
            btnUniversalWinLock.PressedColor = Color.FromArgb(20, 24, 34);
            btnUniversalWinLock.BorderColor = Color.FromArgb(50, 58, 76);
            btnUniversalWinLock.TextColor = Color.FromArgb(190, 200, 220);
            btnUniversalWinLock.CornerRadius = (int)(6 * dpiScale);
            btnUniversalWinLock.Cursor = Cursors.Hand;
            btnUniversalWinLock.Visible = false;
            btnUniversalWinLock.Click += (s, e) => {
                bool nextState = !UniversalWinLockHelper.IsLocked;
                UniversalWinLockHelper.SetLock(nextState);
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
            };
            cardPerformance.Controls.Add(btnUniversalWinLock);

            btnKeyTestSpeed = new ModernButton();
            btnKeyTestSpeed.Text = "⚡ 全键无冲与触发测速仪 (NKRO)";
            btnKeyTestSpeed.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            btnKeyTestSpeed.NormalColor = Color.FromArgb(36, 44, 62);
            btnKeyTestSpeed.HoverColor = Color.FromArgb(50, 62, 88);
            btnKeyTestSpeed.PressedColor = Color.FromArgb(26, 32, 46);
            btnKeyTestSpeed.BorderColor = Color.FromArgb(70, 90, 130);
            btnKeyTestSpeed.TextColor = Color.FromArgb(230, 240, 255);
            btnKeyTestSpeed.CornerRadius = (int)(6 * dpiScale);
            btnKeyTestSpeed.Cursor = Cursors.Hand;
            btnKeyTestSpeed.Visible = false;
            btnKeyTestSpeed.Click += (s, e) => {
                using (var dlg = new KeyRolloverSpeedForm(dpiScale))
                {
                    dlg.ShowDialog(this);
                }
            };
            cardPerformance.Controls.Add(btnKeyTestSpeed);

            btnKeyboardSettings = new ModernButton();
            btnKeyboardSettings.Text = "⌨️ 打开 Windows 键盘属性设置";
            btnKeyboardSettings.Font = new Font("Microsoft YaHei UI", 8.5F);
            btnKeyboardSettings.NormalColor = Color.FromArgb(28, 32, 44);
            btnKeyboardSettings.HoverColor = Color.FromArgb(42, 48, 64);
            btnKeyboardSettings.PressedColor = Color.FromArgb(20, 24, 34);
            btnKeyboardSettings.BorderColor = Color.FromArgb(50, 58, 76);
            btnKeyboardSettings.TextColor = Color.FromArgb(190, 200, 220);
            btnKeyboardSettings.CornerRadius = (int)(6 * dpiScale);
            btnKeyboardSettings.Cursor = Cursors.Hand;
            btnKeyboardSettings.Visible = false;
            btnKeyboardSettings.Click += (s, e) => {
                ThreadPool.QueueUserWorkItem(_ => {
                    try
                    {
                        var psi = new System.Diagnostics.ProcessStartInfo("control.exe", "main.cpl,@1")
                        {
                            UseShellExecute = true
                        };
                        System.Diagnostics.Process.Start(psi);
                    }
                    catch
                    {
                        try
                        {
                            var psi = new System.Diagnostics.ProcessStartInfo("rundll32.exe", "shell32.dll,Control_RunDLL main.cpl @1")
                            {
                                UseShellExecute = true
                            };
                            System.Diagnostics.Process.Start(psi);
                        }
                        catch
                        {
                            try
                            {
                                var psi = new System.Diagnostics.ProcessStartInfo("ms-settings:keyboard")
                                {
                                    UseShellExecute = true
                                };
                                System.Diagnostics.Process.Start(psi);
                            }
                            catch { }
                        }
                    }
                });
            };
            cardPerformance.Controls.Add(btnKeyboardSettings);

            // Audio Controls
            btnAudioSetDefault = new ModernButton();
            btnAudioSetDefault.Text = "🎯 设为系统默认音频输出";
            btnAudioSetDefault.Font = new Font("Microsoft YaHei UI", 8.5F);
            btnAudioSetDefault.NormalColor = Color.FromArgb(28, 32, 44);
            btnAudioSetDefault.HoverColor = Color.FromArgb(42, 48, 64);
            btnAudioSetDefault.PressedColor = Color.FromArgb(20, 24, 34);
            btnAudioSetDefault.BorderColor = Color.FromArgb(50, 58, 76);
            btnAudioSetDefault.TextColor = Color.FromArgb(190, 200, 220);
            btnAudioSetDefault.CornerRadius = (int)(6 * dpiScale);
            btnAudioSetDefault.Cursor = Cursors.Hand;
            btnAudioSetDefault.Visible = false;
            btnAudioSetDefault.Click += (s, e) => {
                AudioDeviceHelper.SetDefaultAudioDevice(selectedDevice != null ? (selectedDevice.CustomName ?? selectedDevice.DeviceName ?? "") : "");
            };
            cardPerformance.Controls.Add(btnAudioSetDefault);

            btnVolumeMixer = new ModernButton();
            btnVolumeMixer.Text = "🎚️ 独立音量合成器 (sndvol)";
            btnVolumeMixer.Font = new Font("Microsoft YaHei UI", 8.5F);
            btnVolumeMixer.NormalColor = Color.FromArgb(28, 32, 44);
            btnVolumeMixer.HoverColor = Color.FromArgb(42, 48, 64);
            btnVolumeMixer.PressedColor = Color.FromArgb(20, 24, 34);
            btnVolumeMixer.BorderColor = Color.FromArgb(50, 58, 76);
            btnVolumeMixer.TextColor = Color.FromArgb(190, 200, 220);
            btnVolumeMixer.CornerRadius = (int)(6 * dpiScale);
            btnVolumeMixer.Cursor = Cursors.Hand;
            btnVolumeMixer.Visible = false;
            btnVolumeMixer.Click += (s, e) => {
                AudioDeviceHelper.OpenVolumeMixer();
            };
            cardPerformance.Controls.Add(btnVolumeMixer);

            btnAudioSettings = new ModernButton();
            btnAudioSettings.Text = "🔊 打开系统声音与空间音效";
            btnAudioSettings.Font = new Font("Microsoft YaHei UI", 8.5F);
            btnAudioSettings.NormalColor = Color.FromArgb(28, 32, 44);
            btnAudioSettings.HoverColor = Color.FromArgb(42, 48, 64);
            btnAudioSettings.PressedColor = Color.FromArgb(20, 24, 34);
            btnAudioSettings.BorderColor = Color.FromArgb(50, 58, 76);
            btnAudioSettings.TextColor = Color.FromArgb(190, 200, 220);
            btnAudioSettings.CornerRadius = (int)(6 * dpiScale);
            btnAudioSettings.Cursor = Cursors.Hand;
            btnAudioSettings.Visible = false;
            btnAudioSettings.Click += (s, e) => {
                try { System.Diagnostics.Process.Start("ms-settings:sound"); }
                catch { try { System.Diagnostics.Process.Start("mmsys.cpl"); } catch { } }
            };
            cardPerformance.Controls.Add(btnAudioSettings);

            // Generic Mouse Controls
            btnMouseRateTest = new ModernButton();
            btnMouseRateTest.Text = "⚡ 鼠标回报率实测 (RawInput)";
            btnMouseRateTest.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            btnMouseRateTest.NormalColor = Color.FromArgb(36, 44, 62);
            btnMouseRateTest.HoverColor = Color.FromArgb(50, 62, 88);
            btnMouseRateTest.PressedColor = Color.FromArgb(26, 32, 46);
            btnMouseRateTest.BorderColor = Color.FromArgb(70, 90, 130);
            btnMouseRateTest.TextColor = Color.FromArgb(230, 240, 255);
            btnMouseRateTest.CornerRadius = (int)(6 * dpiScale);
            btnMouseRateTest.Cursor = Cursors.Hand;
            btnMouseRateTest.Visible = false;
            btnMouseRateTest.Click += (s, e) => {
                using (var dlg = new MouseRateTestDialog(dpiScale))
                {
                    dlg.ShowDialog(this);
                }
            };
            cardPerformance.Controls.Add(btnMouseRateTest);

            // NuPhy Hardware Console Controls

            // Row 1: 自动休眠 + Win键锁定
            int dualBtnGap = (int)(8 * dpiScale);
            int dualBtnW = (secW - dualBtnGap) / 2;

            btnNuphyAutoSleep = new ModernButton();
            btnNuphyAutoSleep.Text = "🌙 自动休眠: 开启";
            btnNuphyAutoSleep.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold, GraphicsUnit.Point);
            btnNuphyAutoSleep.Location = new Point(cPad, r1Y);
            btnNuphyAutoSleep.Size = new Size(dualBtnW, segH);
            btnNuphyAutoSleep.CornerRadius = (int)(6 * dpiScale);
            btnNuphyAutoSleep.Cursor = Cursors.Hand;
            btnNuphyAutoSleep.Visible = false;
            btnNuphyAutoSleep.Click += (s, e) => {
                var cur = NuphyDeviceHelper.CachedHardwareSettings ?? new NuphyDeviceHelper.NuphyHardwareSettings();
                bool newEn = !cur.SleepEnabled;
                cur.SleepEnabled = newEn;
                ApplyNuphySettingsToUI();
                UpdateTrayDeviceMenuItems();
                ThreadPool.QueueUserWorkItem(_ => {
                    NuphyDeviceHelper.SetAutoSleep(newEn);
                });
            };
            cardPerformance.Controls.Add(btnNuphyAutoSleep);

            btnNuphyWinLock = new ModernButton();
            btnNuphyWinLock.Text = "🔓 Win键锁定: 关闭";
            btnNuphyWinLock.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold, GraphicsUnit.Point);
            btnNuphyWinLock.Location = new Point(cPad + dualBtnW + dualBtnGap, r1Y);
            btnNuphyWinLock.Size = new Size(dualBtnW, segH);
            btnNuphyWinLock.CornerRadius = (int)(6 * dpiScale);
            btnNuphyWinLock.Cursor = Cursors.Hand;
            btnNuphyWinLock.Visible = false;
            btnNuphyWinLock.Click += (s, e) => {
                var cur = NuphyDeviceHelper.CachedHardwareSettings ?? new NuphyDeviceHelper.NuphyHardwareSettings();
                bool newLock = !cur.WinLock;
                cur.WinLock = newLock;
                ApplyNuphySettingsToUI();
                UpdateTrayDeviceMenuItems();
                ThreadPool.QueueUserWorkItem(_ => {
                    NuphyDeviceHelper.SetWinLock(newLock);
                });
            };
            cardPerformance.Controls.Add(btnNuphyWinLock);

            // Row 2: 键盘背光开关 + 前置灯带开关
            btnNuphyBacklight = new ModernButton();
            btnNuphyBacklight.Text = "💡 键盘背光: 开";
            btnNuphyBacklight.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold, GraphicsUnit.Point);
            btnNuphyBacklight.Location = new Point(cPad, r2Y);
            btnNuphyBacklight.Size = new Size(dualBtnW, segH);
            btnNuphyBacklight.CornerRadius = (int)(6 * dpiScale);
            btnNuphyBacklight.Cursor = Cursors.Hand;
            btnNuphyBacklight.Visible = false;
            btnNuphyBacklight.Click += (s, e) => {
                var cur = NuphyDeviceHelper.CachedHardwareSettings ?? new NuphyDeviceHelper.NuphyHardwareSettings();
                bool newBack = !(cur.BacklightEnabled && cur.BacklightBrightness > 0);
                cur.BacklightEnabled = newBack;
                if (newBack && cur.BacklightBrightness <= 0) cur.BacklightBrightness = 100;
                ApplyNuphySettingsToUI();
                ThreadPool.QueueUserWorkItem(_ => {
                    NuphyDeviceHelper.SetBacklightEnabled(newBack);
                });
            };
            cardPerformance.Controls.Add(btnNuphyBacklight);

            btnNuphySidelight = new ModernButton();
            btnNuphySidelight.Text = "✨ 前置灯带: 开";
            btnNuphySidelight.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold, GraphicsUnit.Point);
            btnNuphySidelight.Location = new Point(cPad + dualBtnW + dualBtnGap, r2Y);
            btnNuphySidelight.Size = new Size(dualBtnW, segH);
            btnNuphySidelight.CornerRadius = (int)(6 * dpiScale);
            btnNuphySidelight.Cursor = Cursors.Hand;
            btnNuphySidelight.Visible = false;
            btnNuphySidelight.Click += (s, e) => {
                var cur = NuphyDeviceHelper.CachedHardwareSettings ?? new NuphyDeviceHelper.NuphyHardwareSettings();
                bool newSide = !(cur.SidelightEnabled && cur.SidelightBrightness > 0);
                cur.SidelightEnabled = newSide;
                if (newSide && cur.SidelightBrightness <= 0) cur.SidelightBrightness = 100;
                ApplyNuphySettingsToUI();
                ThreadPool.QueueUserWorkItem(_ => {
                    NuphyDeviceHelper.SetSidelightEnabled(newSide);
                });
            };
            cardPerformance.Controls.Add(btnNuphySidelight);

            // Row 3: 背光亮度滑块 (TactileSmoothSliderControl)
            int r3Y = (int)(112 * dpiScale);
            lblNuphyBrightTitle = new Label();
            lblNuphyBrightTitle.Text = "背光亮度";
            lblNuphyBrightTitle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            lblNuphyBrightTitle.ForeColor = Color.FromArgb(160, 168, 185);
            lblNuphyBrightTitle.BackColor = Color.Transparent;
            lblNuphyBrightTitle.Location = new Point(cPad, r3Y + (int)(4 * dpiScale));
            lblNuphyBrightTitle.Size = new Size(lblW, (int)(22 * dpiScale));
            lblNuphyBrightTitle.Visible = false;
            cardPerformance.Controls.Add(lblNuphyBrightTitle);

            int sliderX = cPad + lblW;
            int valBadgeW = (int)(50 * dpiScale);
            int sliderW = secW - lblW - valBadgeW - (int)(6 * dpiScale);

            sliderNuphyBrightness = new TactileSmoothSliderControl();
            sliderNuphyBrightness.DpiScale = dpiScale;
            sliderNuphyBrightness.Location = new Point(sliderX, r3Y);
            sliderNuphyBrightness.Size = new Size(sliderW, segH);
            sliderNuphyBrightness.Visible = false;
            sliderNuphyBrightness.ValueChanged += (s, e) => {
                if (lblNuphyBrightVal != null) lblNuphyBrightVal.Text = sliderNuphyBrightness.Value + "%";
                QueueNuphyBrightnessThrottled(sliderNuphyBrightness.Value);
            };
            sliderNuphyBrightness.ValueCommitted += (s, e) => {
                int finalB = sliderNuphyBrightness.Value;
                var cur = NuphyDeviceHelper.CachedHardwareSettings ?? new NuphyDeviceHelper.NuphyHardwareSettings();
                cur.BacklightBrightness = finalB;
                cur.BacklightEnabled = (finalB > 0);
                NuphyDeviceHelper.CachedHardwareSettings = cur;
                NuphyDeviceHelper.SaveHardwareCache();
                ApplyNuphySettingsToUI();
                QueueNuphyBrightnessThrottled(finalB);
            };
            cardPerformance.Controls.Add(sliderNuphyBrightness);

            lblNuphyBrightVal = new Label();
            lblNuphyBrightVal.Text = "100%";
            lblNuphyBrightVal.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold, GraphicsUnit.Point);
            lblNuphyBrightVal.ForeColor = Color.FromArgb(0, 229, 255);
            lblNuphyBrightVal.BackColor = Color.Transparent;
            lblNuphyBrightVal.TextAlign = ContentAlignment.MiddleRight;
            lblNuphyBrightVal.Location = new Point(sliderX + sliderW + (int)(6 * dpiScale), r3Y + (int)(3 * dpiScale));
            lblNuphyBrightVal.Size = new Size(valBadgeW, (int)(22 * dpiScale));
            lblNuphyBrightVal.Visible = false;
            cardPerformance.Controls.Add(lblNuphyBrightVal);

            // ================= CARD 3: CONFIGURATION & PREFERENCES =================
            int card3Y = card2Y + card2H + (int)(12 * dpiScale);
            int card3H = (int)(244 * dpiScale);

            cardSettings = new RoundedCard();
            cardSettings.Location = new Point(padX, card3Y);
            cardSettings.Size = new Size(cardW, card3H);
            cardSettings.CornerRadius = (int)(10 * dpiScale);
            cardSettings.CardColor = Color.FromArgb(25, 27, 34);
            cardSettings.BorderColor = Color.FromArgb(42, 46, 58);
            this.Controls.Add(cardSettings);

            lblSettingsTitle = new Label();
            lblSettingsTitle.Text = "功能设置与偏好";
            lblSettingsTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);
            lblSettingsTitle.ForeColor = Color.FromArgb(240, 245, 255);
            lblSettingsTitle.BackColor = Color.Transparent;
            lblSettingsTitle.Location = new Point(cPad, (int)(14 * dpiScale));
            lblSettingsTitle.AutoSize = true;
            cardSettings.Controls.Add(lblSettingsTitle);

            // Row 1: 托盘图标样式 (Capsule / Badge)
            int row1Y = (int)(40 * dpiScale);
            int lblTitleW = (int)(95 * dpiScale);
            int card3SegH = (int)(28 * dpiScale);

            lblStyleTitle = new Label();
            lblStyleTitle.Text = "托盘图标样式";
            lblStyleTitle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            lblStyleTitle.ForeColor = Color.FromArgb(160, 168, 185);
            lblStyleTitle.BackColor = Color.Transparent;
            lblStyleTitle.Location = new Point(cPad, row1Y + (int)(2 * dpiScale));
            lblStyleTitle.Size = new Size(lblTitleW, (int)(22 * dpiScale));
            cardSettings.Controls.Add(lblStyleTitle);

            int seg1Gap = (int)(6 * dpiScale);
            int seg1W = (secW - lblTitleW - (seg1Gap * 2)) / 3;
            int seg1X = cPad + lblTitleW;

            btnStyleCapsule = new ModernSegmentButton();
            btnStyleCapsule.Text = "胶囊电池";
            btnStyleCapsule.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            btnStyleCapsule.Location = new Point(seg1X, row1Y);
            btnStyleCapsule.Size = new Size(seg1W, card3SegH);
            btnStyleCapsule.Click += (s, e) => SetTrayStyle(0, false);
            cardSettings.Controls.Add(btnStyleCapsule);

            btnStyleNum = new ModernSegmentButton();
            btnStyleNum.Text = "数字能量";
            btnStyleNum.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            btnStyleNum.Location = new Point(seg1X + seg1W + seg1Gap, row1Y);
            btnStyleNum.Size = new Size(seg1W, card3SegH);
            btnStyleNum.Click += (s, e) => SetTrayStyle(1, false);
            cardSettings.Controls.Add(btnStyleNum);

            btnStylePureNum = new ModernSegmentButton();
            btnStylePureNum.Text = "极简纯数字";
            btnStylePureNum.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            btnStylePureNum.Location = new Point(seg1X + (seg1W + seg1Gap) * 2, row1Y);
            btnStylePureNum.Size = new Size(seg1W, card3SegH);
            btnStylePureNum.Click += (s, e) => SetTrayStyle(2, false);
            cardSettings.Controls.Add(btnStylePureNum);

            // Row 2: 自动刷新频率 (30s / 1m / 5m)
            int row2Y = (int)(76 * dpiScale);

            lblIntervalTitle = new Label();
            lblIntervalTitle.Text = "自动刷新频率";
            lblIntervalTitle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            lblIntervalTitle.ForeColor = Color.FromArgb(160, 168, 185);
            lblIntervalTitle.BackColor = Color.Transparent;
            lblIntervalTitle.Location = new Point(cPad, row2Y + (int)(2 * dpiScale));
            lblIntervalTitle.Size = new Size(lblTitleW, (int)(22 * dpiScale));
            cardSettings.Controls.Add(lblIntervalTitle);

            int seg2Gap = (int)(6 * dpiScale);
            int seg2W = (secW - lblTitleW - (seg2Gap * 2)) / 3;

            btnInt30s = new ModernSegmentButton();
            btnInt30s.Text = "30 秒";
            btnInt30s.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            btnInt30s.Location = new Point(seg1X, row2Y);
            btnInt30s.Size = new Size(seg2W, card3SegH);
            btnInt30s.Click += (s, e) => SetInterval(30000, false);
            cardSettings.Controls.Add(btnInt30s);

            btnInt1m = new ModernSegmentButton();
            btnInt1m.Text = "1 分钟";
            btnInt1m.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            btnInt1m.Location = new Point(seg1X + seg2W + seg2Gap, row2Y);
            btnInt1m.Size = new Size(seg2W, card3SegH);
            btnInt1m.Click += (s, e) => SetInterval(60000, false);
            cardSettings.Controls.Add(btnInt1m);

            btnInt5m = new ModernSegmentButton();
            btnInt5m.Text = "5 分钟";
            btnInt5m.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            btnInt5m.Location = new Point(seg1X + (seg2W + seg2Gap) * 2, row2Y);
            btnInt5m.Size = new Size(seg2W, card3SegH);
            btnInt5m.Click += (s, e) => SetInterval(300000, false);
            cardSettings.Controls.Add(btnInt5m);

            // Row 3: DPI 浮窗样式 (居中胶囊 / 阶梯能量 / 顶置微标)
            int row3Y = (int)(112 * dpiScale);

            lblOsdStyleTitle = new Label();
            lblOsdStyleTitle.Text = "DPI 浮窗样式";
            lblOsdStyleTitle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            lblOsdStyleTitle.ForeColor = Color.FromArgb(160, 168, 185);
            lblOsdStyleTitle.BackColor = Color.Transparent;
            lblOsdStyleTitle.Location = new Point(cPad, row3Y + (int)(2 * dpiScale));
            lblOsdStyleTitle.Size = new Size(lblTitleW, (int)(22 * dpiScale));
            cardSettings.Controls.Add(lblOsdStyleTitle);

            btnOsdStyleCapsule = new ModernSegmentButton();
            btnOsdStyleCapsule.Text = "居中胶囊";
            btnOsdStyleCapsule.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            btnOsdStyleCapsule.Location = new Point(seg1X, row3Y);
            btnOsdStyleCapsule.Size = new Size(seg2W, card3SegH);
            btnOsdStyleCapsule.Click += (s, e) => SetOsdStyle(0, true);
            cardSettings.Controls.Add(btnOsdStyleCapsule);

            btnOsdStyleGauge = new ModernSegmentButton();
            btnOsdStyleGauge.Text = "阶梯能量";
            btnOsdStyleGauge.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            btnOsdStyleGauge.Location = new Point(seg1X + seg2W + seg2Gap, row3Y);
            btnOsdStyleGauge.Size = new Size(seg2W, card3SegH);
            btnOsdStyleGauge.Click += (s, e) => SetOsdStyle(1, true);
            cardSettings.Controls.Add(btnOsdStyleGauge);

            btnOsdStyleCompact = new ModernSegmentButton();
            btnOsdStyleCompact.Text = "顶置微标";
            btnOsdStyleCompact.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            btnOsdStyleCompact.Location = new Point(seg1X + (seg2W + seg2Gap) * 2, row3Y);
            btnOsdStyleCompact.Size = new Size(seg2W, card3SegH);
            btnOsdStyleCompact.Click += (s, e) => SetOsdStyle(2, true);
            cardSettings.Controls.Add(btnOsdStyleCompact);

            // Row 4: Modern subtle divider
            divSettings = new SubtleDivider();
            divSettings.Location = new Point(cPad, (int)(150 * dpiScale));
            divSettings.Size = new Size(secW, (int)(6 * dpiScale));
            cardSettings.Controls.Add(divSettings);

            // Row 5: Checkboxes in 2x2 grid
            int chkY1 = (int)(162 * dpiScale);
            int chkY2 = (int)(190 * dpiScale);
            int chkGap = (int)(14 * dpiScale);
            int chkW = (secW - chkGap) / 2;
            int chkH = (int)(24 * dpiScale);

            chkAutoStart = new ModernCheckBox();
            chkAutoStart.Text = "开机自动启动";
            chkAutoStart.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            chkAutoStart.Location = new Point(cPad, chkY1);
            chkAutoStart.Size = new Size(chkW, chkH);
            chkAutoStart.CheckedChanged += (s, e) => {
                if (isUpdatingUI) return;
                SetAutoStart(chkAutoStart.Checked);
            };
            cardSettings.Controls.Add(chkAutoStart);

            chkAutoStartShowUI = new ModernCheckBox();
            chkAutoStartShowUI.Text = "开机弹出主窗口";
            chkAutoStartShowUI.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            chkAutoStartShowUI.Location = new Point(cPad + chkW + chkGap, chkY1);
            chkAutoStartShowUI.Size = new Size(chkW, chkH);
            chkAutoStartShowUI.CheckedChanged += (s, e) => {
                if (isUpdatingUI) return;
                SetAutoStartShowUI(chkAutoStartShowUI.Checked, true);
            };
            cardSettings.Controls.Add(chkAutoStartShowUI);

            chkLowAlert = new ModernCheckBox();
            chkLowAlert.Text = "低电量提醒 (≤20%)";
            chkLowAlert.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            chkLowAlert.Location = new Point(cPad, chkY2);
            chkLowAlert.Size = new Size(chkW, chkH);
            chkLowAlert.CheckedChanged += (s, e) => {
                if (isUpdatingUI) return;
                SetLowBatteryAlert(chkLowAlert.Checked);
            };
            cardSettings.Controls.Add(chkLowAlert);

            chkDpiOsd = new ModernCheckBox();
            chkDpiOsd.Text = "DPI 切换屏幕提示 (OSD)";
            chkDpiOsd.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            chkDpiOsd.Location = new Point(cPad + chkW + chkGap, chkY2);
            chkDpiOsd.Size = new Size(chkW, chkH);
            chkDpiOsd.CheckedChanged += (s, e) => {
                if (isUpdatingUI) return;
                SetDpiOsdEnabled(chkDpiOsd.Checked);
            };
            cardSettings.Controls.Add(chkDpiOsd);

            // Note inside Card 3
            lblSettingsTip = new Label();
            lblSettingsTip.Text = "注：切换线缆/接收器或按键调 DPI 时将自动即时同步，无需等待计时周期";
            lblSettingsTip.Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Regular, GraphicsUnit.Point);
            lblSettingsTip.ForeColor = Color.FromArgb(120, 128, 142);
            lblSettingsTip.BackColor = cardSettings.CardColor;
            lblSettingsTip.Location = new Point(cPad, (int)(218 * dpiScale));
            lblSettingsTip.AutoSize = true;
            cardSettings.Controls.Add(lblSettingsTip);

            // ================= BOTTOM ROW: ACTIONS =================
            int card4Y = card3Y + card3H + (int)(14 * dpiScale);
            int btnH = (int)(38 * dpiScale);
            int btnGap = (int)(14 * dpiScale);
            int btnW = (cardW - btnGap) / 2;

            btnRefresh = new ModernButton();
            btnRefresh.Text = "立即刷新";
            btnRefresh.Location = new Point(padX, card4Y);
            btnRefresh.Size = new Size(btnW, btnH);
            btnRefresh.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
            btnRefresh.NormalColor = Color.FromArgb(0, 200, 83);
            btnRefresh.HoverColor = Color.FromArgb(0, 230, 118);
            btnRefresh.PressedColor = Color.FromArgb(0, 160, 65);
            btnRefresh.ForeColor = Color.FromArgb(10, 24, 15);
            btnRefresh.CornerRadius = (int)(8 * dpiScale);
            btnRefresh.Click += (s, e) => {
                RefreshBatteryStatus(true);
                SyncAllDevicesPhysicalState();
            };
            this.Controls.Add(btnRefresh);

            btnHideToTray = new ModernButton();
            btnHideToTray.Text = "最小化到托盘";
            btnHideToTray.Location = new Point(padX + btnW + btnGap, card4Y);
            btnHideToTray.Size = new Size(btnW, btnH);
            btnHideToTray.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
            btnHideToTray.NormalColor = Color.FromArgb(36, 39, 49);
            btnHideToTray.HoverColor = Color.FromArgb(48, 52, 65);
            btnHideToTray.PressedColor = Color.FromArgb(28, 30, 38);
            btnHideToTray.BorderColor = Color.FromArgb(58, 63, 78);
            btnHideToTray.ForeColor = Color.White;
            btnHideToTray.CornerRadius = (int)(8 * dpiScale);
            btnHideToTray.Click += (s, e) => {
                this.Hide();
            };
            this.Controls.Add(btnHideToTray);

            int clientH = card4Y + btnH + (int)(16 * dpiScale);
            UpdateMainFormClientSize(baseW, clientH);
        }

        private void RelayoutDpiButtons()
        {
            if (cardPerformance == null || btnDpiStages == null || btnDpiManage == null) return;

            int cPad = (int)(18 * dpiScale);
            int secW = cardPerformance.ClientSize.Width - (cPad * 2);
            int r1Y = (int)(38 * dpiScale);
            int lblW = (int)(86 * dpiScale);
            int segH = (int)(28 * dpiScale);

            int dpiStartX = cPad + lblW;
            int availableW = secW - lblW;
            int gap = (int)(6 * dpiScale);
            int manageBtnW = segH;

            int count = Math.Max(1, Math.Min(5, dpiStageList.Count));
            int remainingW = availableW - manageBtnW - gap;
            int stageW = (remainingW - (count - 1) * gap) / count;

            dpiStageValues = dpiStageList.ToArray();

            for (int i = 0; i < 5; i++)
            {
                if (i < count)
                {
                    btnDpiStages[i].Visible = true;
                    btnDpiStages[i].Text = dpiStageList[i].ToString();
                    btnDpiStages[i].Location = new Point(dpiStartX + i * (stageW + gap), r1Y);
                    btnDpiStages[i].Size = new Size(stageW, segH);
                    if (selectedDevice != null && selectedDevice.IsXyIndependent && selectedDevice.DpiStagesY != null && selectedDevice.DpiStagesY.Length > i)
                    {
                        toolTipTop.SetToolTip(btnDpiStages[i], string.Format("第 {0} 档: X={1}, Y={2} DPI", i + 1, dpiStageList[i], selectedDevice.DpiStagesY[i]));
                    }
                    else
                    {
                        toolTipTop.SetToolTip(btnDpiStages[i], string.Format("第 {0} 档: {1} DPI", i + 1, dpiStageList[i]));
                    }
                    btnDpiStages[i].Invalidate();
                }
                else
                {
                    btnDpiStages[i].Visible = false;
                }
            }

            btnDpiManage.Visible = true;
            btnDpiManage.Location = new Point(dpiStartX + count * (stageW + gap), r1Y);
            btnDpiManage.Size = new Size(manageBtnW, segH);

            if (dpiMenu != null && selectedDevice != null && (selectedDevice.Brand == "Razer" || selectedDevice.Brand == "Valkyrie"))
            {
                dpiMenu.DropDownItems.Clear();
                for (int i = 0; i < dpiStageList.Count; i++)
                {
                    int stageIdx = i;
                    int valX = dpiStageList[i];
                    int valY = (selectedDevice.DpiStagesY != null && selectedDevice.DpiStagesY.Length > i) ? selectedDevice.DpiStagesY[i] : valX;
                    string itemText = (selectedDevice.IsXyIndependent && valX != valY)
                        ? string.Format("档位 {0}: {1} × {2} DPI", i + 1, valX, valY)
                        : string.Format("档位 {0}: {1} DPI", i + 1, valX);
                    var sub = new ToolStripMenuItem(itemText, null, (s, e) => SwitchDpiStageByIndex(stageIdx));
                    sub.Tag = valX;
                    if (selectedDevice.Dpi == valX || (selectedDevice.DpiStage == (i + 1))) sub.Checked = true;
                    dpiMenu.DropDownItems.Add(sub);
                }
            }
        }

        private void RelayoutPerformanceControls()
        {
            if (cardPerformance == null) return;
            int cPad = (int)(18 * dpiScale);
            int secW = cardPerformance.ClientSize.Width - (cPad * 2);
            int lblW = (int)(86 * dpiScale);
            int segH = (int)(28 * dpiScale);

            int r1Y = (int)(38 * dpiScale);
            if (lblDpiTitle != null)
            {
                lblDpiTitle.Location = new Point(cPad, (int)(41 * dpiScale));
                lblDpiTitle.Size = new Size(lblW, (int)(22 * dpiScale));
            }
            RelayoutDpiButtons();

            int r2Y = (int)(75 * dpiScale);
            if (lblRateTitle != null)
            {
                lblRateTitle.Location = new Point(cPad, (int)(78 * dpiScale));
                lblRateTitle.Size = new Size(lblW, (int)(22 * dpiScale));
            }

            int hzStartX = cPad + lblW;
            int hzGap = (int)(6 * dpiScale);
            int hzSegW = (secW - lblW - (hzGap * 3)) / 4;
            if (btnRates != null)
            {
                for (int i = 0; i < btnRates.Length; i++)
                {
                    if (btnRates[i] != null)
                    {
                        btnRates[i].Location = new Point(hzStartX + i * (hzSegW + hzGap), r2Y);
                        btnRates[i].Size = new Size(hzSegW, segH);
                    }
                }
            }

            int gameSwY = (int)(112 * dpiScale);
            int gameChkW = (int)(180 * dpiScale);
            if (chkAutoGameRate != null)
            {
                chkAutoGameRate.Location = new Point(cPad, gameSwY);
                chkAutoGameRate.Size = new Size(gameChkW, (int)(26 * dpiScale));
            }
            if (btnManageGames != null)
            {
                int btnMgrX = cPad + gameChkW + (int)(6 * dpiScale);
                int btnMgrW = secW - gameChkW - (int)(6 * dpiScale);
                btnManageGames.Location = new Point(btnMgrX, gameSwY);
                btnManageGames.Size = new Size(btnMgrW, (int)(26 * dpiScale));
                btnManageGames.CornerRadius = (int)(5 * dpiScale);
            }

            int gameRateY = (int)(147 * dpiScale);
            if (lblGameRateTitle != null)
            {
                lblGameRateTitle.Location = new Point(cPad, (int)(150 * dpiScale));
                lblGameRateTitle.Size = new Size(lblW, (int)(22 * dpiScale));
            }
            if (btnGameRates != null)
            {
                for (int i = 0; i < btnGameRates.Length; i++)
                {
                    if (btnGameRates[i] != null)
                    {
                        btnGameRates[i].Location = new Point(hzStartX + i * (hzSegW + hzGap), gameRateY);
                        btnGameRates[i].Size = new Size(hzSegW, segH);
                    }
                }
            }

            int gameNotifY = (int)(184 * dpiScale);
            if (lblNotifyModeTitle != null)
            {
                lblNotifyModeTitle.Location = new Point(cPad, (int)(185 * dpiScale));
                lblNotifyModeTitle.Size = new Size((int)(62 * dpiScale), (int)(20 * dpiScale));
            }
            int notifStartX = cPad + (int)(64 * dpiScale);
            int notifW = (secW - (int)(64 * dpiScale)) / 3;
            if (chkNotifyOsd != null)
            {
                chkNotifyOsd.Location = new Point(notifStartX, gameNotifY);
                chkNotifyOsd.Size = new Size(notifW, (int)(22 * dpiScale));
            }
            if (chkNotifyToast != null)
            {
                chkNotifyToast.Location = new Point(notifStartX + notifW, gameNotifY);
                chkNotifyToast.Size = new Size(notifW, (int)(22 * dpiScale));
            }
            if (chkNotifySound != null)
            {
                chkNotifySound.Location = new Point(notifStartX + notifW * 2, gameNotifY);
                chkNotifySound.Size = new Size(notifW, (int)(22 * dpiScale));
            }
        }

        private void HighlightDpiStage(int stage)
        {
            if (btnDpiStages != null)
            {
                for (int i = 0; i < btnDpiStages.Length; i++)
                {
                    if (btnDpiStages[i] != null)
                    {
                        btnDpiStages[i].Selected = (i == (stage - 1));
                    }
                }
            }
        }

        private void SwitchDpiStageByIndex(int index)
        {
            if (index < 0 || index >= dpiStageList.Count) return;
            int targetStage = index + 1;
            int targetDpi = dpiStageList[index];

            var curDev = selectedDevice;
            if (curDev != null && curDev.Brand == "Razer")
            {
                string devId = curDev.DeviceId;
                int[] stagesX = (curDev.DpiStages != null && curDev.DpiStages.Length > 0) ? curDev.DpiStages : dpiStageList.ToArray();
                int[] stagesY = (curDev.DpiStagesY != null && curDev.DpiStagesY.Length == stagesX.Length) ? curDev.DpiStagesY : stagesX;
                int targetDpiY = (stagesY != null && stagesY.Length > index) ? stagesY[index] : targetDpi;
                new Thread(() => {
                    bool ok = RazerDeviceHelper.SetRazerDpiStage(targetStage, devId);
                    this.BeginInvoke(new Action(() => {
                        if (curDev != null)
                        {
                            curDev.Dpi = targetDpi;
                            curDev.DpiY = targetDpiY;
                            curDev.DpiStage = targetStage;
                        }
                        HighlightDpiStage(targetStage);
                        OnDpiChanged("Razer", targetDpi, targetStage, dpiStageList.Count, devId);
                    }));
                }) { IsBackground = true }.Start();
            }
            else if (curDev != null && curDev.Brand == "Valkyrie")
            {
                int[] stagesX = (curDev.DpiStages != null && curDev.DpiStages.Length > 0) ? curDev.DpiStages : dpiStageList.ToArray();
                int[] stagesY = (curDev.DpiStagesY != null && curDev.DpiStagesY.Length == stagesX.Length) ? curDev.DpiStagesY : stagesX;
                int targetDpiY = (stagesY != null && stagesY.Length > index) ? stagesY[index] : targetDpi;
                new Thread(() => {
                    bool ok = ValkyrieDeviceHelper.UpdateValkyrieHardwareStages(targetStage, stagesX, stagesY);
                    this.BeginInvoke(new Action(() => {
                        if (curDev != null)
                        {
                            curDev.Dpi = targetDpi;
                            curDev.DpiY = targetDpiY;
                            curDev.DpiStage = targetStage;
                        }
                        HighlightDpiStage(targetStage);
                        OnDpiChanged("Valkyrie", targetDpi, targetStage, dpiStageList.Count);
                    }));
                }) { IsBackground = true }.Start();
            }
            else
            {
                SetDpiFromUI(targetDpi);
            }
        }

        private void UpdateMainFormClientSize(int baseW, int targetClientH)
        {
            Screen screen = Screen.FromRectangle(this.Bounds);
            Rectangle workArea = (screen != null) ? screen.WorkingArea : Screen.PrimaryScreen.WorkingArea;

            // 彻底禁用原生滚动条，保持精致卡片视觉设计
            this.AutoScroll = false;
            this.AutoScrollMinSize = Size.Empty;
            this.ClientSize = new Size(baseW, targetClientH);

            int newTop = this.Top;
            if (newTop + this.Height > workArea.Bottom)
            {
                newTop = Math.Max(workArea.Top + (int)(8 * dpiScale), workArea.Bottom - this.Height);
            }
            if (newTop < workArea.Top)
            {
                newTop = workArea.Top + (int)(8 * dpiScale);
            }
            this.Top = newTop;
        }

        private void AdjustPerformanceCardHeight(int newCard2H)
        {
            if (cardPerformance == null || cardSettings == null || btnRefresh == null || btnHideToTray == null) return;
            if (cardPerformance.Height == newCard2H) return;

            int oldCard3Y = cardSettings.Top;
            int oldBtnBottom = btnRefresh.Bottom;

            this.SuspendLayout();
            try
            {
                cardPerformance.Height = newCard2H;
                int padX = (int)(18 * dpiScale);
                int baseW = (int)(470 * dpiScale);
                int cardW = baseW - (padX * 2);

                int card3Y = cardPerformance.Bottom + (int)(12 * dpiScale);
                cardSettings.Location = new Point(padX, card3Y);

                int card4Y = cardSettings.Bottom + (int)(14 * dpiScale);
                int btnH = (int)(38 * dpiScale);
                int btnGap = (int)(14 * dpiScale);
                int btnW = (cardW - btnGap) / 2;

                btnRefresh.Location = new Point(padX, card4Y);
                btnHideToTray.Location = new Point(padX + btnW + btnGap, card4Y);
                btnRefresh.Size = new Size(btnW, btnH);
                btnHideToTray.Size = new Size(btnW, btnH);

                int clientH = card4Y + btnH + (int)(16 * dpiScale);
                UpdateMainFormClientSize(baseW, clientH);
            }
            finally
            {
                this.ResumeLayout(false);
            }

            int minY = Math.Min(oldCard3Y, cardSettings.Top);
            int maxY = Math.Max(oldBtnBottom, btnRefresh.Bottom);
            int spanH = maxY - minY + (int)(16 * dpiScale);
            if (spanH > 0)
            {
                this.Invalidate(new Rectangle(0, minY, this.ClientSize.Width, spanH), true);
                if (cardSettings != null) cardSettings.Invalidate();
            }
        }

        private void UpdateGameRateButtonSelection(int hz)
        {
            if (btnGameRates != null)
            {
                for (int i = 0; i < btnGameRates.Length; i++)
                {
                    if (i < pollingRateValues.Length)
                    {
                        btnGameRates[i].Selected = (pollingRateValues[i] == hz);
                    }
                }
            }

            if (autoGameRateMenu != null && autoGameRateMenu.DropDownItems.Count > 2)
            {
                var gameRateSub = autoGameRateMenu.DropDownItems[2] as ToolStripMenuItem;
                if (gameRateSub != null)
                {
                    foreach (ToolStripItem item in gameRateSub.DropDownItems)
                    {
                        var mi = item as ToolStripMenuItem;
                        if (mi != null && mi.Tag != null)
                        {
                            mi.Checked = ((int)mi.Tag == hz);
                        }
                    }
                }
            }
            UpdatePollingRateDisplayStates();
        }

        private void UpdateDesktopRateButtonSelection(int hz)
        {
            if (btnRates != null)
            {
                for (int i = 0; i < btnRates.Length; i++)
                {
                    if (i < pollingRateValues.Length)
                    {
                        btnRates[i].Selected = (pollingRateValues[i] == hz);
                    }
                }
            }

            if (rateMenu != null)
            {
                foreach (ToolStripItem item in rateMenu.DropDownItems)
                {
                    var mi = item as ToolStripMenuItem;
                    if (mi != null && mi.Tag != null)
                    {
                        mi.Checked = ((int)mi.Tag == hz);
                    }
                }
            }
            UpdatePollingRateDisplayStates();
        }

        private void UpdatePollingRateDisplayStates()
        {
            if (btnRates == null) return;

            bool isRazerMouse = (selectedDevice != null &&
                                 selectedDevice.Category == DeviceCategory.Mouse &&
                                 (selectedDevice.Brand == "Razer" || (selectedDevice.DeviceId != null && selectedDevice.DeviceId.StartsWith("Razer:1532:"))));

            if (!autoGameRateEnabled || !isRazerMouse)
            {
                if (lblLiveRateBadge != null) lblLiveRateBadge.Visible = false;
                if (lblRateTitle != null)
                {
                    lblRateTitle.Text = "常规回报率";
                    lblRateTitle.ForeColor = Color.FromArgb(160, 168, 185);
                }
                for (int i = 0; i < btnRates.Length; i++)
                {
                    if (btnRates[i] != null && i < pollingRateValues.Length)
                    {
                        btnRates[i].Selected = (pollingRateValues[i] == desktopPollingRate);
                        btnRates[i].IsPreset = false;
                    }
                }
            }
            else
            {
                int currentActiveHz = isGamingActive ? gamePollingRate : desktopPollingRate;
                if (lblLiveRateBadge != null)
                {
                    lblLiveRateBadge.Visible = true;
                    if (isGamingActive)
                    {
                        lblLiveRateBadge.Text = "● 硬件生效: " + currentActiveHz + " Hz (电竞加速)";
                        lblLiveRateBadge.ForeColor = Color.FromArgb(0, 230, 118);
                    }
                    else
                    {
                        lblLiveRateBadge.Text = "● 硬件生效: " + currentActiveHz + " Hz (常规桌面)";
                        lblLiveRateBadge.ForeColor = Color.FromArgb(180, 210, 240);
                    }
                }

                if (lblRateTitle != null)
                {
                    if (!isGamingActive)
                    {
                        lblRateTitle.Text = "● 常规回报率";
                        lblRateTitle.ForeColor = Color.FromArgb(0, 230, 118);
                    }
                    else
                    {
                        lblRateTitle.Text = "常规回报率";
                        lblRateTitle.ForeColor = Color.FromArgb(160, 168, 185);
                    }
                }

                if (lblGameRateTitle != null)
                {
                    if (isGamingActive)
                    {
                        lblGameRateTitle.Text = "● 电竞回报率";
                        lblGameRateTitle.ForeColor = Color.FromArgb(0, 230, 118);
                    }
                    else
                    {
                        lblGameRateTitle.Text = "电竞回报率";
                        lblGameRateTitle.ForeColor = Color.FromArgb(160, 168, 185);
                    }
                }

                for (int i = 0; i < btnRates.Length; i++)
                {
                    if (btnRates[i] != null && i < pollingRateValues.Length)
                    {
                        bool isSel = (pollingRateValues[i] == desktopPollingRate);
                        btnRates[i].Selected = isSel;
                        btnRates[i].IsPreset = (isSel && isGamingActive);
                    }
                }

                if (btnGameRates != null)
                {
                    for (int i = 0; i < btnGameRates.Length; i++)
                    {
                        if (btnGameRates[i] != null && i < pollingRateValues.Length)
                        {
                            bool isSel = (pollingRateValues[i] == gamePollingRate);
                            btnGameRates[i].Selected = isSel;
                            btnGameRates[i].IsPreset = (isSel && !isGamingActive);
                        }
                    }
                }
            }
        }

        private void SetGameRateControlsVisibility(bool visible)
        {
            bool isRazerMouse = (selectedDevice != null &&
                                 selectedDevice.Category == DeviceCategory.Mouse &&
                                 (selectedDevice.Brand == "Razer" || (selectedDevice.DeviceId != null && selectedDevice.DeviceId.StartsWith("Razer:1532:"))));
            bool effective = visible && isRazerMouse;

            if (btnManageGames != null) btnManageGames.Visible = effective;
            if (lblGameRateTitle != null) lblGameRateTitle.Visible = effective;
            if (btnGameRates != null)
            {
                for (int i = 0; i < btnGameRates.Length; i++)
                {
                    if (btnGameRates[i] != null) btnGameRates[i].Visible = effective;
                }
            }
            if (lblNotifyModeTitle != null) lblNotifyModeTitle.Visible = effective;
            if (chkNotifyOsd != null) chkNotifyOsd.Visible = effective;
            if (chkNotifyToast != null) chkNotifyToast.Visible = effective;
            if (chkNotifySound != null) chkNotifySound.Visible = effective;
        }

        private void AnimatePerformanceCardHeight(int targetH)
        {
            if (cardPerformance == null) return;
            animTargetCardH = targetH;

            // 1. 若窗体未显示或处于最小化状态，直接原子同步重排，不消耗计时器
            if (!this.Visible || this.WindowState == FormWindowState.Minimized)
            {
                if (cardAnimTimer != null && cardAnimTimer.Enabled) cardAnimTimer.Stop();
                SetGameRateControlsVisibility(autoGameRateEnabled);
                AdjustPerformanceCardHeight(targetH);
                return;
            }

            // 2. 若高度已然吻合，直接校准可见性并退出
            if (cardPerformance.Height == targetH)
            {
                if (cardAnimTimer != null && cardAnimTimer.Enabled) cardAnimTimer.Stop();
                SetGameRateControlsVisibility(autoGameRateEnabled);
                return;
            }

            // 3. 卷帘门遮罩式动效生命周期：
            // 展开开始时立即点亮子控件，配合卡片容器硬件裁剪 (WS_CLIPCHILDREN) 像卷帘门一样随着底边拉伸自然向下露出；
            // 收回过程中保持子控件可见，随着底边向上收拢自然被裁切遮盖，待完全缩入卡片底边后才彻底设为 false。
            if (autoGameRateEnabled)
            {
                SetGameRateControlsVisibility(true);
            }

            animStartCardH = cardPerformance.Height;
            animStartTime = DateTime.UtcNow;

            if (cardAnimTimer == null)
            {
                cardAnimTimer = new System.Windows.Forms.Timer();
                cardAnimTimer.Interval = 15; // 约 66 FPS
                cardAnimTimer.Tick += (s, e) => {
                    if (cardPerformance == null) { cardAnimTimer.Stop(); return; }

                    double elapsed = (DateTime.UtcNow - animStartTime).TotalMilliseconds;
                    float progress = (float)(elapsed / ANIM_DURATION_MS);

                    if (progress >= 1.0f)
                    {
                        cardAnimTimer.Stop();
                        AdjustPerformanceCardHeight(animTargetCardH);

                        // 收起彻底完成时关闭可见性，避免折叠状态下的无效命中或焦点捕获
                        SetGameRateControlsVisibility(autoGameRateEnabled);

                        // 终点全量深度刷新：彻底抹除所有历史脏矩形残留
                        this.Invalidate(true);
                        this.Update();
                        return;
                    }

                    // Cubic Ease-Out 平滑减速缓动曲线: 启程轻快、收尾极度平滑
                    float rem = 1.0f - progress;
                    float ease = 1.0f - (rem * rem * rem);
                    int nextH = (int)Math.Round(animStartCardH + (animTargetCardH - animStartCardH) * ease);
                    AdjustPerformanceCardHeight(nextH);
                };
            }

            cardAnimTimer.Start();
        }

        private void UpdatePerformanceCardHeightForCurrentDevice()
        {
            var dev = selectedDevice;
            if (dev != null && (dev.Brand == "Razer" || dev.Brand == "Rapoo"))
            {
                int targetH = autoGameRateEnabled ? (int)(218 * dpiScale) : (int)(148 * dpiScale);
                AnimatePerformanceCardHeight(targetH);
            }
        }

        private void UpdateManageGamesButtonText()
        {
            if (btnManageGames != null)
            {
                btnManageGames.Text = "🎮 游戏管理 (" + monitoredGameProcesses.Count + "款)";
            }
        }

        private void OpenDpiManagementDialog()
        {
            var curDev = selectedDevice;
            if (curDev == null || (curDev.Brand != "Razer" && curDev.Brand != "Valkyrie")) return;

            int curStage = curDev.DpiStage > 0 ? curDev.DpiStage : 1;
            int[] stagesX = (curDev.DpiStages != null && curDev.DpiStages.Length > 0) ? curDev.DpiStages : dpiStageList.ToArray();
            int[] stagesY = (curDev.DpiStagesY != null && curDev.DpiStagesY.Length == stagesX.Length) ? curDev.DpiStagesY : stagesX;
            bool isXy = curDev.IsXyIndependent;
            double xyRatio = (curDev.XyRatio > 0) ? curDev.XyRatio : 1.0;
            string devName = !string.IsNullOrEmpty(curDev.DisplayName) ? curDev.DisplayName : (curDev.Brand == "Valkyrie" ? "Valkyrie Mouse" : "Razer Mouse");

            RazerDeviceHelper.RazerDpiCapability cap;
            if (curDev.Brand == "Valkyrie")
            {
                cap = new RazerDeviceHelper.RazerDpiCapability
                {
                    MinDpi = 100,
                    MaxDpi = 26000,
                    Step = 50,
                    Description = "瓦尔基里高精度电竞光学传感器 (PAW3395/PAW3950)"
                };
            }
            else
            {
                int pid = 0;
                if (!string.IsNullOrEmpty(curDev.HardwareId) && curDev.HardwareId.Contains(":"))
                {
                    string[] parts = curDev.HardwareId.Split(':');
                    if (parts.Length >= 3)
                    {
                        int.TryParse(parts[2], System.Globalization.NumberStyles.HexNumber, null, out pid);
                    }
                }
                cap = RazerDeviceHelper.GetDpiCapability(devName, pid);
            }

            using (var dlg = new DpiManagementDialog(devName, stagesX, stagesY, isXy, xyRatio, curStage, cap))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    int[] newStagesX = dlg.Stages;
                    int[] newStagesY = dlg.StagesY;
                    if (newStagesX == null || newStagesX.Length == 0) return;
                    if (newStagesY == null || newStagesY.Length != newStagesX.Length) newStagesY = newStagesX;

                    int activeStage = Math.Max(1, Math.Min(dlg.ActiveStage, newStagesX.Length));
                    int activeDpiX = newStagesX[activeStage - 1];
                    int activeDpiY = newStagesY[activeStage - 1];
                    bool newIsXy = dlg.IsXyIndependent;
                    double newRatio = dlg.XyRatio;

                    // 1. Immediately apply to in-memory state on UI thread
                    dpiStageList = new List<int>(newStagesX);
                    dpiStageValues = newStagesX;
                    SaveCustomDpiStages();

                    if (curDev != null)
                    {
                        curDev.DpiStages = newStagesX;
                        curDev.DpiStagesY = newStagesY;
                        curDev.IsXyIndependent = newIsXy;
                        curDev.XyRatio = newRatio;
                        curDev.DpiStageCount = newStagesX.Length;
                        curDev.DpiStage = activeStage;
                        curDev.Dpi = activeDpiX;
                        curDev.DpiY = activeDpiY;
                    }

                    // 2. Immediately relayout, re-render and highlight main form DPI buttons
                    RelayoutDpiButtons();
                    HighlightDpiStage(activeStage);
                    if (btnDpiStages != null)
                    {
                        for (int i = 0; i < btnDpiStages.Length; i++)
                        {
                            if (btnDpiStages[i] != null) btnDpiStages[i].Invalidate();
                        }
                    }
                    if (cardPerformance != null) cardPerformance.Invalidate();

                    string targetDevId = curDev != null ? curDev.DeviceId : null;
                    string devBrand = curDev != null ? curDev.Brand : "Razer";
                    OnDpiChanged(devBrand, activeDpiX, activeStage, newStagesX.Length, targetDevId);
                    UpdatePerformanceSection(curDev, curDev.BrandColor);
                    UpdateStatusDisplay();

                    // 3. Persist to hardware in background thread
                    new Thread(() => {
                        if (devBrand == "Valkyrie")
                        {
                            ValkyrieDeviceHelper.UpdateValkyrieHardwareStages(activeStage, newStagesX, newStagesY);
                        }
                        else
                        {
                            RazerDeviceHelper.UpdateRazerHardwareStages(activeStage, newStagesX, newStagesY, targetDevId);
                        }
                    }) { IsBackground = true }.Start();
                }
            }
        }

        private void SaveCustomDpiStages()
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(ConfigRegistryKey))
                {
                    if (key != null)
                    {
                        StringBuilder sb = new StringBuilder();
                        for (int i = 0; i < dpiStageList.Count; i++)
                        {
                            if (i > 0) sb.Append(",");
                            sb.Append(dpiStageList[i]);
                        }
                        key.SetValue("CustomDpiStages", sb.ToString());
                    }
                }
            }
            catch { }
        }

        private void OpenGameProcessManager()
        {
            var dlg = new GameProcessManagerForm(dpiScale, monitoredGameProcesses, fallbackFullscreenEnabled, (updatedList, fb) => {
                monitoredGameProcesses = new List<string>(updatedList);
                fallbackFullscreenEnabled = fb;
                SaveConfig();
                UpdateManageGamesButtonText();
            });
            dlg.ShowDialog(this);
        }

        private void OpenPriorityManager()
        {
            using (var form = new DevicePriorityForm(activeDevices, dpiScale))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    RefreshBatteryStatus(true);
                }
            }
        }

        private void OpenAddDeviceWizard()
        {
            using (var form = new AddDeviceWizardForm(activeDevices, dpiScale))
            {
                if (form.ShowDialog(this) == DialogResult.OK)
                {
                    RefreshBatteryStatus(true);
                    if (!string.IsNullOrEmpty(form.AddedDeviceId))
                    {
                        SelectDevice(form.AddedDeviceId);
                    }
                }
            }
        }

        private void OpenAboutForm()
        {
            using (AboutForm form = new AboutForm())
            {
                form.ShowDialog(this);
            }
        }

        private void TriggerManualUpdateCheck()
        {
            UpdateManager.CheckUpdateAsync(delegate(bool success, UpdateInfo info, string error)
            {
                if (this.IsDisposed) return;
                this.BeginInvoke((MethodInvoker)delegate
                {
                    if (!success)
                    {
                        if (trayIcon != null)
                        {
                            try { trayIcon.ShowBalloonTip(3000, "检查更新失败", "无法连接到更新服务器，请检查网络设置。", ToolTipIcon.Warning); } catch { }
                        }
                        return;
                    }

                    string curVer = UpdateManager.CurrentVersion;
                    if (info != null && UpdateManager.IsNewerVersion(info.LatestVersion, curVer))
                    {
                        UpdateNoticeForm.ShowUpdateDialog(this, info, curVer);
                    }
                    else
                    {
                        if (trayIcon != null)
                        {
                            try { trayIcon.ShowBalloonTip(3000, "已是最新版本", "当前 FerrisPulse 已是最新版本 (" + curVer + ")！", ToolTipIcon.Info); } catch { }
                        }
                    }
                });
            });
        }

        private void StartPeriodicUpdateCheck()
        {
            if (!ConfigManager.GetAutoCheckUpdate()) return;

            // 开机延迟 10 秒低优先级静默探测，若有更新直接弹出 UpdateNoticeForm 提示用户
            System.Windows.Forms.Timer startupCheckTimer = new System.Windows.Forms.Timer();
            startupCheckTimer.Interval = 10000;
            startupCheckTimer.Tick += delegate(object s, EventArgs e)
            {
                startupCheckTimer.Stop();
                startupCheckTimer.Dispose();

                if (!ConfigManager.GetAutoCheckUpdate()) return;

                UpdateManager.CheckUpdateAsync(delegate(bool success, UpdateInfo info, string error)
                {
                    if (this.IsDisposed) return;
                    string curVer = UpdateManager.CurrentVersion;
                    if (success && info != null && UpdateManager.IsNewerVersion(info.LatestVersion, curVer))
                    {
                        this.BeginInvoke((MethodInvoker)delegate
                        {
                            UpdateNoticeForm.ShowUpdateDialog(this, info, curVer);
                        });
                    }
                });
            };
            startupCheckTimer.Start();
        }

        private void ToggleDeviceSwitcherFlyout()
        {
            if (activeDevices == null || activeDevices.Count == 0 || focusDeviceSelector == null) return;

            // Debounce toggle if clicked while flyout was closing
            if ((DateTime.Now - lastFlyoutCloseTime).TotalMilliseconds < 250)
            {
                return;
            }

            if (switcherFlyout != null && !switcherFlyout.IsDisposed && switcherFlyout.Visible)
            {
                switcherFlyout.Close();
                switcherFlyout = null;
                return;
            }

            switcherFlyout = new ModernDeviceSwitcherFlyout(dpiScale);
            switcherFlyout.Devices = this.activeDevices;
            switcherFlyout.SelectedDevice = this.selectedDevice;
            switcherFlyout.OnSelectDevice = (d) => {
                SelectDeviceView(d);
            };

            int padX = (int)(18 * dpiScale);
            int cardW = this.ClientSize.Width - (padX * 2);
            switcherFlyout.CalcSize(cardW);

            Point pt = this.PointToScreen(new Point(padX, (int)(12 * dpiScale) + (int)(38 * dpiScale) + (int)(4 * dpiScale)));
            switcherFlyout.Location = pt;
            switcherFlyout.FormClosed += (s, e) => {
                lastFlyoutCloseTime = DateTime.Now;
                switcherFlyout = null;
            };
            switcherFlyout.Show(this);
        }

        private void ShowDeviceSwitcherMenu()
        {
            ToggleDeviceSwitcherFlyout();
        }

        private void RenameCurrentDevice()
        {
            var targetDev = selectedDevice ?? lastInfo;
            if (targetDev == null) return;

            string currentName = targetDev.DisplayName;
            string newName;
            if (PromptDialog.Show(this, "修改设备名称", "请输入外设的自定义名称 (支持中文或英文):", currentName, out newName))
            {
                newName = (newName != null) ? newName.Trim() : "";
                if (!string.IsNullOrEmpty(newName) && !string.Equals(newName, currentName, StringComparison.Ordinal))
                {
                    targetDev.CustomName = newName;
                    lblDeviceName.Text = targetDev.DisplayName;
                    UpdateDeviceTabsUI();
                    UpdateTrayDeviceMenuItems();

                    var existingConfigs = DeviceManager.LoadCustomDevices();
                    CustomDeviceConfig cfg = null;
                    if (existingConfigs != null)
                    {
                        cfg = existingConfigs.Find(c => 
                            c.DeviceId.Equals(targetDev.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                            (!string.IsNullOrEmpty(c.HardwareFingerprint) && !string.IsNullOrEmpty(targetDev.HardwareFingerprint) &&
                             c.HardwareFingerprint.Equals(targetDev.HardwareFingerprint, StringComparison.OrdinalIgnoreCase)));
                    }

                    if (cfg != null)
                    {
                        cfg.CustomName = newName;
                        DeviceManager.SaveCustomDevice(cfg);
                    }
                    else
                    {
                        cfg = new CustomDeviceConfig
                        {
                            DeviceId = targetDev.DeviceId,
                            OriginalName = targetDev.DeviceName,
                            CustomName = newName,
                            Category = targetDev.Category,
                            Transport = targetDev.Transport,
                            CachedBattery = targetDev.BatteryPercent,
                            HardwareFingerprint = targetDev.HardwareFingerprint
                        };
                        DeviceManager.SaveCustomDevice(cfg);
                    }

                    UpdateStatusDisplay();
                    UpdateDeviceTabsUI();
                    UpdateTrayDeviceMenuItems();
                }
            }
        }

        internal void ApplyDpiScaleToControls()
        {
            this.SuspendLayout();
            try
            {
                int padX = (int)(18 * dpiScale);
                int baseW = (int)(470 * dpiScale);
                int cardW = baseW - (padX * 2);

                // Top bar
                int barH = (int)(38 * dpiScale);
                int btnSize = barH;
                int topGap = (int)(6 * dpiScale);
                int selectorW = cardW - (btnSize * 3) - (topGap * 3);

                if (focusDeviceSelector != null)
                {
                    focusDeviceSelector.DpiScale = dpiScale;
                    focusDeviceSelector.Location = new Point(padX, (int)(12 * dpiScale));
                    focusDeviceSelector.Size = new Size(selectorW, barH);
                }
                if (btnTopPriority != null)
                {
                    btnTopPriority.CornerRadius = (int)(8 * dpiScale);
                    btnTopPriority.Location = new Point(padX + selectorW + topGap, (int)(12 * dpiScale));
                    btnTopPriority.Size = new Size(btnSize, barH);
                }
                if (btnTopAddDevice != null)
                {
                    btnTopAddDevice.CornerRadius = (int)(8 * dpiScale);
                    btnTopAddDevice.Location = new Point(padX + selectorW + topGap + btnSize + topGap, (int)(12 * dpiScale));
                    btnTopAddDevice.Size = new Size(btnSize, barH);
                }
                if (btnTopAbout != null)
                {
                    btnTopAbout.CornerRadius = (int)(8 * dpiScale);
                    btnTopAbout.Location = new Point(padX + selectorW + topGap + (btnSize + topGap) * 2, (int)(12 * dpiScale));
                    btnTopAbout.Size = new Size(btnSize, barH);
                }

                // Card 1
                int card1Y = (int)(12 * dpiScale) + barH + (int)(10 * dpiScale);
                int card1H = (int)(176 * dpiScale);
                if (cardBattery != null)
                {
                    cardBattery.Location = new Point(padX, card1Y);
                    cardBattery.Size = new Size(cardW, card1H);
                    cardBattery.CornerRadius = (int)(10 * dpiScale);

                    int badgeW = (int)(108 * dpiScale);
                    int badgeH = (int)(26 * dpiScale);
                    int badgeX = cardW - badgeW - (int)(16 * dpiScale);
                    int badgeY = (int)(14 * dpiScale);
                    if (badgePrimary != null)
                    {
                        badgePrimary.Location = new Point(badgeX, badgeY);
                        badgePrimary.Size = new Size(badgeW, badgeH);
                    }
                    int nameX = (int)(20 * dpiScale);
                    int nameY = (int)(14 * dpiScale);
                    int nameMaxW = badgeX - nameX - (int)(10 * dpiScale);
                    if (lblDeviceName != null)
                    {
                        lblDeviceName.Location = new Point(nameX, nameY);
                        lblDeviceName.Size = new Size(nameMaxW, (int)(24 * dpiScale));
                    }
                    if (lblBatteryBig != null)
                    {
                        lblBatteryBig.Location = new Point((int)(18 * dpiScale), (int)(46 * dpiScale));
                    }
                    if (pillStatus != null)
                    {
                        pillStatus.Location = new Point((int)(150 * dpiScale), (int)(56 * dpiScale));
                        pillStatus.Size = new Size((int)(80 * dpiScale), (int)(32 * dpiScale));
                    }
                    if (barBattery != null)
                    {
                        barBattery.Location = new Point((int)(20 * dpiScale), (int)(118 * dpiScale));
                        barBattery.Size = new Size(cardW - (int)(40 * dpiScale), (int)(12 * dpiScale));
                    }
                    if (lblUpdateTime != null)
                    {
                        lblUpdateTime.Location = new Point((int)(20 * dpiScale), (int)(142 * dpiScale));
                    }
                }

                // Card 2
                int card2Y = card1Y + card1H + (int)(12 * dpiScale);
                int card2H = autoGameRateEnabled ? (int)(218 * dpiScale) : (int)(148 * dpiScale);
                if (cardPerformance != null)
                {
                    cardPerformance.Location = new Point(padX, card2Y);
                    cardPerformance.Size = new Size(cardW, card2H);
                    cardPerformance.CornerRadius = (int)(10 * dpiScale);
                }

                // Card 3
                int cPad = (int)(18 * dpiScale);
                int secW = cardW - (cPad * 2);
                int card3H = (int)(244 * dpiScale);
                if (cardSettings != null)
                {
                    cardSettings.Width = cardW;
                    cardSettings.Height = card3H;
                    cardSettings.CornerRadius = (int)(10 * dpiScale);

                    int row1Y = (int)(40 * dpiScale);
                    int lblTitleW = (int)(95 * dpiScale);
                    int seg1Gap = (int)(6 * dpiScale);
                    int seg1W = (secW - lblTitleW - (seg1Gap * 2)) / 3;
                    int seg1X = cPad + lblTitleW;
                    int segH = (int)(28 * dpiScale);

                    if (lblSettingsTitle != null) lblSettingsTitle.Location = new Point(cPad, (int)(14 * dpiScale));
                    if (lblStyleTitle != null)
                    {
                        lblStyleTitle.Location = new Point(cPad, row1Y + (int)(2 * dpiScale));
                        lblStyleTitle.Size = new Size(lblTitleW, (int)(22 * dpiScale));
                    }
                    if (btnStyleCapsule != null)
                    {
                        btnStyleCapsule.Location = new Point(seg1X, row1Y);
                        btnStyleCapsule.Size = new Size(seg1W, segH);
                    }
                    if (btnStyleNum != null)
                    {
                        btnStyleNum.Location = new Point(seg1X + seg1W + seg1Gap, row1Y);
                        btnStyleNum.Size = new Size(seg1W, segH);
                    }
                    if (btnStylePureNum != null)
                    {
                        btnStylePureNum.Location = new Point(seg1X + (seg1W + seg1Gap) * 2, row1Y);
                        btnStylePureNum.Size = new Size(seg1W, segH);
                    }

                    int row2Y = (int)(76 * dpiScale);
                    int seg2Gap = (int)(6 * dpiScale);
                    int seg2W = (secW - lblTitleW - (seg2Gap * 2)) / 3;

                    if (lblIntervalTitle != null)
                    {
                        lblIntervalTitle.Location = new Point(cPad, row2Y + (int)(2 * dpiScale));
                        lblIntervalTitle.Size = new Size(lblTitleW, (int)(22 * dpiScale));
                    }
                    if (btnInt30s != null)
                    {
                        btnInt30s.Location = new Point(seg1X, row2Y);
                        btnInt30s.Size = new Size(seg2W, segH);
                    }
                    if (btnInt1m != null)
                    {
                        btnInt1m.Location = new Point(seg1X + seg2W + seg2Gap, row2Y);
                        btnInt1m.Size = new Size(seg2W, segH);
                    }
                    if (btnInt5m != null)
                    {
                        btnInt5m.Location = new Point(seg1X + (seg2W + seg2Gap) * 2, row2Y);
                        btnInt5m.Size = new Size(seg2W, segH);
                    }

                    int row3Y = (int)(112 * dpiScale);
                    if (lblOsdStyleTitle != null)
                    {
                        lblOsdStyleTitle.Location = new Point(cPad, row3Y + (int)(2 * dpiScale));
                        lblOsdStyleTitle.Size = new Size(lblTitleW, (int)(22 * dpiScale));
                    }
                    if (btnOsdStyleCapsule != null)
                    {
                        btnOsdStyleCapsule.Location = new Point(seg1X, row3Y);
                        btnOsdStyleCapsule.Size = new Size(seg2W, segH);
                    }
                    if (btnOsdStyleGauge != null)
                    {
                        btnOsdStyleGauge.Location = new Point(seg1X + seg2W + seg2Gap, row3Y);
                        btnOsdStyleGauge.Size = new Size(seg2W, segH);
                    }
                    if (btnOsdStyleCompact != null)
                    {
                        btnOsdStyleCompact.Location = new Point(seg1X + (seg2W + seg2Gap) * 2, row3Y);
                        btnOsdStyleCompact.Size = new Size(seg2W, segH);
                    }

                    if (divSettings != null)
                    {
                        divSettings.Location = new Point(cPad, (int)(150 * dpiScale));
                        divSettings.Size = new Size(secW, (int)(6 * dpiScale));
                    }

                    int chkY1 = (int)(162 * dpiScale);
                    int chkY2 = (int)(190 * dpiScale);
                    int chkGap = (int)(14 * dpiScale);
                    int chkW = (secW - chkGap) / 2;
                    int chkH = (int)(24 * dpiScale);

                    if (chkAutoStart != null)
                    {
                        chkAutoStart.Location = new Point(cPad, chkY1);
                        chkAutoStart.Size = new Size(chkW, chkH);
                    }
                    if (chkAutoStartShowUI != null)
                    {
                        chkAutoStartShowUI.Location = new Point(cPad + chkW + chkGap, chkY1);
                        chkAutoStartShowUI.Size = new Size(chkW, chkH);
                    }
                    if (chkLowAlert != null)
                    {
                        chkLowAlert.Location = new Point(cPad, chkY2);
                        chkLowAlert.Size = new Size(chkW, chkH);
                    }
                    if (chkDpiOsd != null)
                    {
                        chkDpiOsd.Location = new Point(cPad + chkW + chkGap, chkY2);
                        chkDpiOsd.Size = new Size(chkW, chkH);
                    }

                    if (lblSettingsTip != null)
                    {
                        lblSettingsTip.Location = new Point(cPad, (int)(218 * dpiScale));
                    }
                }

                // Relayout Performance controls in Card 2
                RelayoutPerformanceControls();

                // Bottom row: Actions
                int card3Y = (cardPerformance != null ? cardPerformance.Bottom : 0) + (int)(12 * dpiScale);
                if (cardSettings != null) cardSettings.Location = new Point(padX, card3Y);

                int card4Y = (cardSettings != null ? cardSettings.Bottom : 0) + (int)(14 * dpiScale);
                int btnH = (int)(38 * dpiScale);
                int btnGap = (int)(14 * dpiScale);
                int btnW = (cardW - btnGap) / 2;

                if (btnRefresh != null)
                {
                    btnRefresh.Location = new Point(padX, card4Y);
                    btnRefresh.Size = new Size(btnW, btnH);
                    btnRefresh.CornerRadius = (int)(8 * dpiScale);
                }
                if (btnHideToTray != null)
                {
                    btnHideToTray.Location = new Point(padX + btnW + btnGap, card4Y);
                    btnHideToTray.Size = new Size(btnW, btnH);
                    btnHideToTray.CornerRadius = (int)(8 * dpiScale);
                }

                int clientH = card4Y + btnH + (int)(16 * dpiScale);
                UpdateMainFormClientSize(baseW, clientH);
            }
            finally
            {
                this.ResumeLayout(true);
            }

            if (selectedDevice != null)
            {
                try { UpdateUI(selectedDevice, true); } catch { }
            }

            this.Invalidate(true);
            if (cardBattery != null) cardBattery.Invalidate(true);
            if (cardPerformance != null) cardPerformance.Invalidate(true);
            if (cardSettings != null) cardSettings.Invalidate(true);
            this.Update();
        }

    }
}
