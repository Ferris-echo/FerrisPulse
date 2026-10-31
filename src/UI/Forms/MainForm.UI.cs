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
            int baseW = (int)(486 * dpiScale);
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
            int card2H;
            if (!autoGameRateEnabled && !autoGameDpiEnabled)
            {
                card2H = (int)(148 * dpiScale);
            }
            else if (autoGameRateEnabled && autoGameDpiEnabled)
            {
                card2H = (int)(255 * dpiScale);
            }
            else
            {
                card2H = (int)(218 * dpiScale);
            }

            cardPerformance = new RoundedCard();
            cardPerformance.Location = new Point(padX, card2Y);
            cardPerformance.Size = new Size(cardW, card2H);
            cardPerformance.CornerRadius = (int)(10 * dpiScale);
            cardPerformance.CardColor = Color.FromArgb(25, 27, 34);
            cardPerformance.BorderColor = Color.FromArgb(42, 46, 58);
            this.Controls.Add(cardPerformance);

            int cPad = (int)(14 * dpiScale);
            int secW = cardW - (cPad * 2);

            lblPerfTitle = new Label();
            lblPerfTitle.Text = "鼠标性能与档位调节";
            lblPerfTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold, GraphicsUnit.Point);
            lblPerfTitle.ForeColor = Color.FromArgb(240, 245, 255);
            lblPerfTitle.BackColor = Color.Transparent;
            lblPerfTitle.Location = new Point((int)(16 * dpiScale), (int)(12 * dpiScale));
            lblPerfTitle.AutoSize = true;
            cardPerformance.Controls.Add(lblPerfTitle);

            lblLiveRateBadge = new Label();
            lblLiveRateBadge.Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Regular, GraphicsUnit.Point);
            lblLiveRateBadge.ForeColor = Color.FromArgb(0, 230, 118);
            lblLiveRateBadge.BackColor = Color.Transparent;
            lblLiveRateBadge.TextAlign = ContentAlignment.MiddleRight;
            lblLiveRateBadge.Location = new Point(cardW - cPad - (int)(270 * dpiScale), (int)(14 * dpiScale));
            lblLiveRateBadge.Size = new Size((int)(270 * dpiScale), (int)(20 * dpiScale));
            lblLiveRateBadge.Visible = false;
            cardPerformance.Controls.Add(lblLiveRateBadge);

            // Row 1: DPI 档位
            int r1Y = (int)(38 * dpiScale);
            int lblW = MeasurePerfLabelColumnW();
            int segH = (int)(28 * dpiScale);

            lblDpiTitle = new Label();
            lblDpiTitle.Text = "DPI 档位";
            lblDpiTitle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            lblDpiTitle.ForeColor = Color.FromArgb(160, 168, 185);
            lblDpiTitle.BackColor = Color.Transparent;
            lblDpiTitle.Location = new Point(cPad, (int)(41 * dpiScale));
            lblDpiTitle.Size = new Size(lblW, (int)(22 * dpiScale));
            cardPerformance.Controls.Add(lblDpiTitle);

            btnDpiStages = new ModernSegmentButton[6];
            for (int i = 0; i < btnDpiStages.Length; i++)
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

            btnRates = new ModernSegmentButton[8];
            for (int i = 0; i < 8; i++)
            {
                int index = i;
                var btn = new ModernSegmentButton();
                btn.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
                btn.Click += (s, e) => {
                    if (pollingRateValues != null && index < pollingRateValues.Length)
                    {
                        SetPollingRateFromUI(pollingRateValues[index]);
                    }
                };
                cardPerformance.Controls.Add(btn);
                btnRates[i] = btn;
            }

            btnRateManage = new ModernButton();
            btnRateManage.IsGearButton = true;
            btnRateManage.CornerRadius = (int)(6 * dpiScale);
            btnRateManage.Cursor = Cursors.Hand;
            btnRateManage.Click += (s, e) => OpenPollingRateManagementDialog();
            toolTipTop.SetToolTip(btnRateManage, "自定义常用回报率档位与快捷设置");
            cardPerformance.Controls.Add(btnRateManage);

            RelayoutRateButtons();

            // Row 3: 全屏游戏自动优化 (总开关 + 游戏管理按钮)
            int gameSwY = (int)(112 * dpiScale);
            int gap = (int)(6 * dpiScale);
            int chkOptW = (int)(160 * dpiScale);
            int btnMgrW = secW - chkOptW - gap;

            chkAutoGameOpt = new ModernCheckBox();
            chkAutoGameOpt.Text = "全屏游戏自动优化";
            chkAutoGameOpt.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
            chkAutoGameOpt.ForeColor = Color.FromArgb(210, 220, 240);
            chkAutoGameOpt.Location = new Point(cPad, gameSwY);
            chkAutoGameOpt.Size = new Size(chkOptW, (int)(26 * dpiScale));
            chkAutoGameOpt.CheckedChanged += (s, e) => {
                if (!isUpdatingUI) SetAutoGameOptEnabled(chkAutoGameOpt.Checked);
            };
            cardPerformance.Controls.Add(chkAutoGameOpt);

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
            int btnMgrX = cPad + chkOptW + gap;
            btnManageGames.Location = new Point(btnMgrX, gameSwY);
            btnManageGames.Size = new Size(btnMgrW, (int)(26 * dpiScale));
            btnManageGames.Click += (s, e) => OpenGameProcessManager();
            cardPerformance.Controls.Add(btnManageGames);

            // Row 4: 电竞 DPI 档位选择 (独立开关 + 5 档选择)
            chkAutoGameDpi = new ModernCheckBox();
            chkAutoGameDpi.Text = "电竞 DPI";
            chkAutoGameDpi.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
            chkAutoGameDpi.ForeColor = Color.FromArgb(210, 220, 240);
            chkAutoGameDpi.Location = new Point(cPad, (int)(147 * dpiScale));
            chkAutoGameDpi.Size = new Size(lblW, (int)(28 * dpiScale));
            chkAutoGameDpi.Visible = false;
            chkAutoGameDpi.CheckedChanged += (s, e) => {
                if (!isUpdatingUI) SetAutoGameDpiEnabled(chkAutoGameDpi.Checked);
            };
            cardPerformance.Controls.Add(chkAutoGameDpi);

            lblGameDpiTitle = new Label();
            lblGameDpiTitle.Visible = false;

            btnGameDpiStages = new ModernSegmentButton[6];
            for (int i = 0; i < btnGameDpiStages.Length; i++)
            {
                int stageNum = i + 1;
                var btn = new ModernSegmentButton();
                btn.Text = "第" + stageNum + "档";
                btn.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
                btn.Visible = false;
                btn.Click += (s, e) => {
                    SetGameDpiStageFromUI(stageNum);
                };
                cardPerformance.Controls.Add(btn);
                btnGameDpiStages[i] = btn;
            }

            // Row 5: 电竞回报率选择 (独立开关 + 回报率档位)
            chkAutoGameRate = new ModernCheckBox();
            chkAutoGameRate.Text = "电竞回报率";
            chkAutoGameRate.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
            chkAutoGameRate.ForeColor = Color.FromArgb(210, 220, 240);
            chkAutoGameRate.Location = new Point(cPad, (int)(184 * dpiScale));
            chkAutoGameRate.Size = new Size(lblW, (int)(28 * dpiScale));
            chkAutoGameRate.Visible = false;
            chkAutoGameRate.CheckedChanged += (s, e) => {
                if (!isUpdatingUI) SetAutoGameRateEnabled(chkAutoGameRate.Checked);
            };
            cardPerformance.Controls.Add(chkAutoGameRate);

            lblGameRateTitle = new Label();
            lblGameRateTitle.Visible = false;

            btnGameRates = new ModernSegmentButton[8];
            for (int i = 0; i < 8; i++)
            {
                int index = i;
                var btn = new ModernSegmentButton();
                btn.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
                btn.Visible = false;
                btn.Click += (s, e) => {
                    if (pollingRateValues != null && index < pollingRateValues.Length)
                    {
                        SetGamePollingRateFromUI(pollingRateValues[index]);
                    }
                };
                cardPerformance.Controls.Add(btn);
                btnGameRates[i] = btn;
            }

            // Row 6: 提醒方式设置
            int gameNotifY = (int)(221 * dpiScale);
            lblNotifyModeTitle = new Label();
            lblNotifyModeTitle.Text = "切换提醒:";
            lblNotifyModeTitle.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
            lblNotifyModeTitle.ForeColor = Color.FromArgb(140, 150, 170);
            lblNotifyModeTitle.BackColor = Color.Transparent;
            lblNotifyModeTitle.Location = new Point(cPad, (int)(222 * dpiScale));
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
            RelayoutPerformanceControls();

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
                if (selectedDevice != null && selectedDevice.Brand == "VGN" &&
                    VgnKeyboardHelper.IsVgnNeon75(selectedDevice.DeviceId))
                {
                    using (var dlg = new VgnKeyboardForm())
                    {
                        dlg.ShowDialog(this);
                    }
                    return;
                }
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
            InitializeAtkLightingControl();

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
                var dev = selectedDevice ?? GetPrimaryDevice();
                var act = GetDeviceToggleAction(dev, 0, "auto_sleep");
                bool newEn = act != null ? !(act.BoolValue || act.IntValue != 0) : true;
                if (act != null) { act.BoolValue = newEn; act.IntValue = newEn ? 1 : 0; }
                string actId = act != null ? act.ActionId : "auto_sleep";
                ThreadPool.QueueUserWorkItem(_ => {
                    HardwareCommandBus.ExecuteCustomAction(dev, actId, newEn ? 1 : 0);
                    try
                    {
                        this.BeginInvoke(new Action(() => {
                            ApplyNuphySettingsToUI();
                            UpdateTrayDeviceMenuItems();
                        }));
                    }
                    catch { }
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
                var dev = selectedDevice ?? GetPrimaryDevice();
                var act = GetDeviceToggleAction(dev, 1, "win_lock");
                bool newLock = act != null ? !(act.BoolValue || act.IntValue != 0) : true;
                if (act != null) { act.BoolValue = newLock; act.IntValue = newLock ? 1 : 0; }
                string actId = act != null ? act.ActionId : "win_lock";
                ThreadPool.QueueUserWorkItem(_ => {
                    HardwareCommandBus.ExecuteCustomAction(dev, actId, newLock ? 1 : 0);
                    try
                    {
                        this.BeginInvoke(new Action(() => {
                            ApplyNuphySettingsToUI();
                            UpdateTrayDeviceMenuItems();
                        }));
                    }
                    catch { }
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
                var dev = selectedDevice ?? GetPrimaryDevice();
                var act = GetDeviceToggleAction(dev, 2, "backlight_toggle");
                bool newBack = act != null ? !(act.BoolValue || act.IntValue != 0) : true;
                if (act != null) { act.BoolValue = newBack; act.IntValue = newBack ? 1 : 0; }
                string actId = act != null ? act.ActionId : "backlight_toggle";
                ThreadPool.QueueUserWorkItem(_ => {
                    HardwareCommandBus.ExecuteCustomAction(dev, actId, newBack ? 1 : 0);
                    try
                    {
                        this.BeginInvoke(new Action(() => {
                            ApplyNuphySettingsToUI();
                            UpdateTrayDeviceMenuItems();
                        }));
                    }
                    catch { }
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
                var dev = selectedDevice ?? GetPrimaryDevice();
                var act = GetDeviceToggleAction(dev, 3, "sidelight_toggle");
                bool newSide = act != null ? !(act.BoolValue || act.IntValue != 0) : true;
                if (act != null) { act.BoolValue = newSide; act.IntValue = newSide ? 1 : 0; }
                string actId = act != null ? act.ActionId : "sidelight_toggle";
                ThreadPool.QueueUserWorkItem(_ => {
                    HardwareCommandBus.ExecuteCustomAction(dev, actId, newSide ? 1 : 0);
                    try
                    {
                        this.BeginInvoke(new Action(() => {
                            ApplyNuphySettingsToUI();
                            UpdateTrayDeviceMenuItems();
                        }));
                    }
                    catch { }
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
                var dev = selectedDevice ?? GetPrimaryDevice();
                var sliderAct = GetDeviceSliderAction(dev, "backlight_brightness");
                if (sliderAct != null) sliderAct.IntValue = finalB;
                QueueNuphyBrightnessThrottled(finalB);
                ApplyNuphySettingsToUI();
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

        // 左列「标签 / 开关」栏的宽度：按真实文字实测动态计算。
        // 固定 82px 在「电竞回报率」这类较长文案下会把最后一个字压到右侧
        // 档位按钮底下——ModernCheckBox 的文字从控件内 x=25 起绘（1px 留白
        // + 16px 方框 + 8px 间距），68px 宽文本实际需要 97px 控件宽度，
        // 而右侧第一个回报率按钮正好紧贴该栏右边缘起排。故此处按最宽文案实测取值。
        private int MeasurePerfLabelColumnW()
        {
            int boxAndGap = 25; // 与 ModernCheckBox.OnPaint 的 boxRect.Right + 8 保持一致
            int breathing = (int)Math.Ceiling(4 * dpiScale);
            int widest = 0;

            if (lblDpiTitle != null) { int w = TextRenderer.MeasureText(lblDpiTitle.Text, lblDpiTitle.Font).Width; if (w > widest) widest = w; }
            if (lblRateTitle != null) { int w = TextRenderer.MeasureText(lblRateTitle.Text, lblRateTitle.Font).Width; if (w > widest) widest = w; }
            if (lblNuphyBrightTitle != null) { int w = TextRenderer.MeasureText(lblNuphyBrightTitle.Text, lblNuphyBrightTitle.Font).Width; if (w > widest) widest = w; }
            if (chkAutoGameDpi != null) { int w = TextRenderer.MeasureText(chkAutoGameDpi.Text, chkAutoGameDpi.Font).Width; if (w > widest) widest = w; }
            if (chkAutoGameRate != null) { int w = TextRenderer.MeasureText(chkAutoGameRate.Text, chkAutoGameRate.Font).Width; if (w > widest) widest = w; }

            // 兜底：控件尚未创建时按最长内置文案「常规回报率 / 电竞回报率」的实测宽度。
            if (widest <= 0) widest = 68;

            int needed = widest + boxAndGap + breathing;
            int minW = (int)Math.Ceiling(82 * dpiScale);
            return (needed > minW) ? needed : minW;
        }

        private void RelayoutDpiButtons()
        {
            if (cardPerformance == null || btnDpiStages == null || btnDpiManage == null) return;

            int cPad = (int)(14 * dpiScale);
            int secW = cardPerformance.ClientSize.Width - (cPad * 2);
            int r1Y = (int)(38 * dpiScale);
            int lblW = MeasurePerfLabelColumnW();
            int segH = (int)(28 * dpiScale);

            int dpiStartX = cPad + lblW;
            int availableW = secW - lblW;
            int gap = (int)(5 * dpiScale);
            int manageBtnW = segH;

            // 严格按板载实际档位数量渲染：有多少档就显示多少，绝不用 Math.Max(1, ...) 伪造出档位。
            int count = Math.Min(btnDpiStages.Length, dpiStageList.Count);
            if (count < 0) count = 0;
            int remainingW = availableW - manageBtnW - gap;
            int stageW = (count > 0) ? ((remainingW - (count - 1) * gap) / count) : remainingW;

            dpiStageValues = dpiStageList.ToArray();

            for (int i = 0; i < btnDpiStages.Length; i++)
            {
                if (i < count)
                {
                    btnDpiStages[i].Visible = true;
                    int valX = dpiStageList[i];
                    int valY = (selectedDevice != null && selectedDevice.DpiStagesY != null && selectedDevice.DpiStagesY.Length > i) ? selectedDevice.DpiStagesY[i] : valX;
                    bool isDiffXy = (selectedDevice != null && selectedDevice.IsXyIndependent && valX != valY);

                    if (isDiffXy)
                    {
                        btnDpiStages[i].Text = string.Format("{0}×{1}", valX, valY);
                        int tLen = btnDpiStages[i].Text.Length;
                        float fSize = (tLen <= 7) ? 7.2F : ((tLen <= 9) ? 6.4F : 5.6F);
                        btnDpiStages[i].Font = new Font("Segoe UI", fSize, FontStyle.Bold);
                    }
                    else
                    {
                        btnDpiStages[i].Text = valX.ToString();
                        btnDpiStages[i].Font = new Font("Segoe UI", 8.8F, FontStyle.Bold);
                    }
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

            if (dpiMenu != null && selectedDevice != null && selectedDevice.Category == DeviceCategory.Mouse)
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

        // 电竞回报率档位按钮的统一可用性：设备锁死回报率时保持 Enabled=true + IsReadOnly=true，
        // 这样既能灰化不可编辑，又能继续悬停弹出原生气泡（Enabled=false 会让 ToolTip 失效）。
        private void ApplyGameRateButtonState(ModernSegmentButton btn)
        {
            if (btn == null) return;
            bool rateLocked = (selectedDevice != null && selectedDevice.SupportsPollingRateWrite == false);
            btn.IsReadOnly = rateLocked || !autoGameRateEnabled;
            btn.Enabled = autoGameRateEnabled || rateLocked;
        }

        private void RelayoutRateButtons()
        {
            if (cardPerformance == null || btnRates == null || btnRateManage == null) return;

            int cPad = (int)(14 * dpiScale);
            int secW = cardPerformance.ClientSize.Width - (cPad * 2);
            int r2Y = (int)(75 * dpiScale);
            int lblW = MeasurePerfLabelColumnW();
            int segH = (int)(28 * dpiScale);

            int hzStartX = cPad + lblW;
            int availableW = secW - lblW;
            int gap = (int)(5 * dpiScale);
            int manageBtnW = segH;

            // 回报率最多 5 档快捷显示
            // 严格按设备实际上报的回报率数量渲染：有多少档就显示多少，绝不补默认值。
            int count = (pollingRateValues != null) ? Math.Min(5, Math.Min(btnRates.Length, pollingRateValues.Length)) : 0;

            // 1. 常规桌面回报率档位（末尾空出齿轮按钮位置）
            int remainingW = availableW - manageBtnW - gap;
            int hzSegW = (count > 0) ? ((remainingW - (count - 1) * gap) / count) : remainingW;

            for (int i = 0; i < btnRates.Length; i++)
            {
                if (i < count && pollingRateValues != null && i < pollingRateValues.Length)
                {
                    int hzVal = pollingRateValues[i];
                    btnRates[i].Visible = true;
                    if (hzSegW < (int)(46 * dpiScale) && hzVal >= 1000)
                    {
                        btnRates[i].Text = (hzVal / 1000) + "K";
                    }
                    else
                    {
                        btnRates[i].Text = hzVal + " Hz";
                    }
                    btnRates[i].Location = new Point(hzStartX + i * (hzSegW + gap), r2Y);
                    btnRates[i].Size = new Size(hzSegW, segH);
                    bool rateWritable = !(selectedDevice != null && selectedDevice.SupportsPollingRateWrite == false);
                    btnRates[i].IsReadOnly = !rateWritable;
                    btnRates[i].Enabled = true;
                    btnRates[i].Cursor = rateWritable ? Cursors.Hand : Cursors.Default;
                    string tipText = string.Format("{0} Hz · 响应延迟 {1:F3} ms", hzVal, 1000.0 / hzVal);
                    if (!rateWritable) tipText = tipText + "\n该设备不支持修改回报率，当前档位由设备自身配置决定";
                    toolTipTop.SetToolTip(btnRates[i], tipText);
                    btnRates[i].Invalidate();
                }
                else
                {
                    btnRates[i].Visible = false;
                }
            }

            btnRateManage.Visible = true;
            btnRateManage.Location = new Point(hzStartX + count * (hzSegW + gap), r2Y);
            btnRateManage.Size = new Size(manageBtnW, segH);
            bool manageWritable = !(selectedDevice != null && selectedDevice.SupportsPollingRateWrite == false);
            btnRateManage.Enabled = manageWritable;
            btnRateManage.Cursor = manageWritable ? Cursors.Hand : Cursors.Default;
            toolTipTop.SetToolTip(btnRateManage, manageWritable ? "自定义回报率档位" : "该设备不支持修改回报率");
        }

        private void RelayoutPerformanceControls()
        {
            if (cardPerformance == null) return;
            int cPad = (int)(14 * dpiScale);
            int secW = cardPerformance.ClientSize.Width - (cPad * 2);
            int lblW = MeasurePerfLabelColumnW();
            int segH = (int)(28 * dpiScale);

            if (lblLiveRateBadge != null)
            {
                lblLiveRateBadge.Location = new Point(cardPerformance.ClientSize.Width - cPad - (int)(270 * dpiScale), (int)(14 * dpiScale));
                lblLiveRateBadge.Size = new Size((int)(270 * dpiScale), (int)(20 * dpiScale));
            }

            int r1Y = (int)(38 * dpiScale);
            if (lblDpiTitle != null)
            {
                lblDpiTitle.Location = new Point(cPad, (int)(41 * dpiScale));
                lblDpiTitle.Size = new Size(lblW, (int)(22 * dpiScale));
            }
            RelayoutDpiButtons();

            if (lblRateTitle != null)
            {
                lblRateTitle.Location = new Point(cPad, (int)(78 * dpiScale));
                lblRateTitle.Size = new Size(lblW, (int)(22 * dpiScale));
            }
            RelayoutRateButtons();

            bool supportsAutoGameRate = IsSelectedDeviceSupportingAutoGameRate();

            // Row 3: 全屏游戏自动优化总开关 + 游戏管理 (恒定 Y = 112)
            int gameSwY = (int)(112 * dpiScale);
            int gap = (int)(6 * dpiScale);
            bool isOptExpanded = (autoGameRateEnabled || autoGameDpiEnabled);
            bool showExpanded = supportsAutoGameRate && isOptExpanded;
            int chkOptW = showExpanded ? (int)(160 * dpiScale) : secW;
            int btnMgrW = secW - (int)(160 * dpiScale) - gap;

            if (chkAutoGameOpt != null)
            {
                chkAutoGameOpt.Location = new Point(cPad, gameSwY);
                chkAutoGameOpt.Size = new Size(chkOptW, (int)(26 * dpiScale));
                chkAutoGameOpt.Visible = supportsAutoGameRate;
            }
            if (btnManageGames != null)
            {
                int btnMgrX = cPad + (int)(160 * dpiScale) + gap;
                btnManageGames.Location = new Point(btnMgrX, gameSwY);
                btnManageGames.Size = new Size(btnMgrW, (int)(26 * dpiScale));
                btnManageGames.CornerRadius = (int)(5 * dpiScale);
                btnManageGames.Visible = showExpanded;
            }

            int availableW = secW - lblW;
            int hzStartX = cPad + lblW;

            // Row 4: 电竞 DPI 档位 (恒定 Y = 147)
            int r4Y = (int)(147 * dpiScale);
            if (chkAutoGameDpi != null)
            {
                chkAutoGameDpi.Location = new Point(cPad, r4Y);
                chkAutoGameDpi.Size = new Size(lblW, segH);
                chkAutoGameDpi.Visible = showExpanded;
            }
            if (btnGameDpiStages != null)
            {
                int dpiCount = Math.Min(btnGameDpiStages.Length, dpiStageList.Count);
                int stageW = (dpiCount > 0) ? ((availableW - (dpiCount - 1) * gap) / dpiCount) : availableW;
                for (int i = 0; i < btnGameDpiStages.Length; i++)
                {
                    if (btnGameDpiStages[i] != null)
                    {
                        if (i < dpiCount)
                        {
                            btnGameDpiStages[i].Location = new Point(hzStartX + i * (stageW + gap), r4Y);
                            btnGameDpiStages[i].Size = new Size(stageW, segH);
                            int valX = (dpiStageList != null && i < dpiStageList.Count) ? dpiStageList[i] : 0;
                            int valY = (selectedDevice != null && selectedDevice.DpiStagesY != null && selectedDevice.DpiStagesY.Length > i) ? selectedDevice.DpiStagesY[i] : valX;
                            bool isDiffXy = (selectedDevice != null && selectedDevice.IsXyIndependent && valX != valY);
                            if (isDiffXy)
                            {
                                btnGameDpiStages[i].Text = string.Format("{0}×{1}", valX, valY);
                                int tLen = btnGameDpiStages[i].Text.Length;
                                float fSize = (tLen <= 7) ? 7.2F : ((tLen <= 9) ? 6.4F : 5.6F);
                                btnGameDpiStages[i].Font = new Font("Segoe UI", fSize, FontStyle.Bold);
                                toolTipTop.SetToolTip(btnGameDpiStages[i], string.Format("全屏电竞第 {0} 档: X={1}, Y={2} DPI", i + 1, valX, valY));
                            }
                            else
                            {
                                btnGameDpiStages[i].Text = valX.ToString();
                                btnGameDpiStages[i].Font = new Font("Segoe UI", 8.8F, FontStyle.Bold);
                                toolTipTop.SetToolTip(btnGameDpiStages[i], string.Format("全屏电竞第 {0} 档: {1} DPI", i + 1, valX));
                            }


                            btnGameDpiStages[i].Enabled = autoGameDpiEnabled;
                            btnGameDpiStages[i].Visible = showExpanded;
                        }
                        else
                        {
                            btnGameDpiStages[i].Visible = false;
                        }
                    }
                }
                UpdateGameDpiButtonSelection(gameDpiStage);
            }

            // Row 5: 电竞回报率 (恒定 Y = 184)
            int r5Y = (int)(184 * dpiScale);
            if (chkAutoGameRate != null)
            {
                chkAutoGameRate.Location = new Point(cPad, r5Y);
                chkAutoGameRate.Size = new Size(lblW, segH);
                chkAutoGameRate.Visible = showExpanded;
                bool rateLocked = (selectedDevice != null && selectedDevice.SupportsPollingRateWrite == false);
                chkAutoGameRate.Enabled = !rateLocked;
                chkAutoGameRate.Cursor = rateLocked ? Cursors.Default : Cursors.Hand;
                toolTipTop.SetToolTip(chkAutoGameRate, rateLocked
                    ? "该设备不支持修改回报率，电竞回报率已自动关闭"
                    : "进入全屏游戏时自动切换到下方回报率档位");
            }
            if (btnGameRates != null)
            {
                int rateCount = (pollingRateValues != null) ? Math.Min(5, Math.Min(btnGameRates.Length, pollingRateValues.Length)) : 0;
                int gameSegW = (rateCount > 0) ? ((availableW - (rateCount - 1) * gap) / rateCount) : availableW;
                for (int i = 0; i < btnGameRates.Length; i++)
                {
                    if (btnGameRates[i] != null)
                    {
                        if (i < rateCount && pollingRateValues != null && i < pollingRateValues.Length)
                        {
                            int hzVal = pollingRateValues[i];
                            btnGameRates[i].Location = new Point(hzStartX + i * (gameSegW + gap), r5Y);
                            btnGameRates[i].Size = new Size(gameSegW, segH);
                            if (gameSegW < (int)(46 * dpiScale) && hzVal >= 1000)
                            {
                                btnGameRates[i].Text = (hzVal / 1000) + "K";
                            }
                            else
                            {
                                btnGameRates[i].Text = hzVal + " Hz";
                            }
                            bool rateLockedForGame = (selectedDevice != null && selectedDevice.SupportsPollingRateWrite == false);
                            string tip = string.Format("全屏电竞回报率: {0} Hz · 延迟 {1:F3} ms", hzVal, 1000.0 / hzVal);
                            if (rateLockedForGame) tip = tip + "\n该设备不支持修改回报率";
                            toolTipTop.SetToolTip(btnGameRates[i], tip);
                            bool gameRateEditable = autoGameRateEnabled && !rateLockedForGame;
                            ApplyGameRateButtonState(btnGameRates[i]);
                            btnGameRates[i].Cursor = gameRateEditable ? Cursors.Hand : Cursors.Default;
                            btnGameRates[i].Visible = showExpanded;
                        }
                        else
                        {
                            btnGameRates[i].Visible = false;
                        }
                    }
                }
            }

            // Row 6: 切换提醒方式 (恒定 Y = 221)
            int r6Y = (int)(221 * dpiScale);
            if (lblNotifyModeTitle != null)
            {
                lblNotifyModeTitle.Location = new Point(cPad, r6Y + (int)(1 * dpiScale));
                lblNotifyModeTitle.Size = new Size((int)(62 * dpiScale), (int)(20 * dpiScale));
                lblNotifyModeTitle.Visible = showExpanded;
            }
            int notifStartX = cPad + (int)(64 * dpiScale);
            int notifW = (secW - (int)(64 * dpiScale)) / 3;
            if (chkNotifyOsd != null)
            {
                chkNotifyOsd.Location = new Point(notifStartX, r6Y);
                chkNotifyOsd.Size = new Size(notifW, (int)(22 * dpiScale));
                chkNotifyOsd.Visible = showExpanded;
            }
            if (chkNotifyToast != null)
            {
                chkNotifyToast.Location = new Point(notifStartX + notifW, r6Y);
                chkNotifyToast.Size = new Size(notifW, (int)(22 * dpiScale));
                chkNotifyToast.Visible = showExpanded;
            }
            if (chkNotifySound != null)
            {
                chkNotifySound.Location = new Point(notifStartX + notifW * 2, r6Y);
                chkNotifySound.Size = new Size(notifW, (int)(22 * dpiScale));
                chkNotifySound.Visible = showExpanded;
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

        internal void SwitchDpiStageByIndex(int index)
        {
            if (index < 0 || index >= dpiStageList.Count) return;
            int targetStage = index + 1;
            int targetDpi = dpiStageList[index];

            if (isGamingActive && autoGameDpiEnabled)
            {
                // 处于游戏运行中：上方的日常 DPI 仅作为“退出游戏后的恢复预设目标”
                // 绝不篡改 gameDpiStage，绝不向正在游戏中的鼠标硬件发送 SetDpi 指令打扰游戏
                desktopDpiStage = targetStage;
                UpdatePollingRateDisplayStates();
                return;
            }

            desktopDpiStage = targetStage;
            var curDev = selectedDevice;
            if (curDev != null)
            {
                int[] stagesX = (curDev.DpiStages != null && curDev.DpiStages.Length > 0) ? curDev.DpiStages : dpiStageList.ToArray();
                int[] stagesY = (curDev.DpiStagesY != null && curDev.DpiStagesY.Length == stagesX.Length) ? curDev.DpiStagesY : stagesX;
                int targetDpiY = (stagesY != null && stagesY.Length > index) ? stagesY[index] : targetDpi;
                string devId = curDev.DeviceId;
                string devBrand = curDev.Brand ?? "Mouse";
                SyncMonitoredDpiState(targetDpi, targetStage, devId);
                new Thread(() => {
                    bool ok = HardwareCommandBus.SetDpi(curDev, targetDpi, targetDpiY, targetStage);
                    if (!ok) return;
                    this.BeginInvoke(new Action(() => {
                        if (curDev != null)
                        {
                            curDev.Dpi = targetDpi;
                            curDev.DpiY = targetDpiY;
                            curDev.DpiStage = targetStage;
                        }
                        HighlightDpiStage(targetStage);
                        UpdatePollingRateDisplayStates();
                        OnDpiChanged(devBrand, targetDpi, targetStage, dpiStageList.Count, devId, targetDpiY);
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
                int baseW = (int)(486 * dpiScale);
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
            UpdatePollingRateDisplayStates();
        }

        private void UpdateGameDpiButtonSelection(int stage)
        {
            if (btnGameDpiStages != null)
            {
                for (int i = 0; i < btnGameDpiStages.Length; i++)
                {
                    if (btnGameDpiStages[i] != null)
                    {
                        btnGameDpiStages[i].Selected = ((i + 1) == stage);
                    }
                }
            }

            if (gameDpiSub != null)
            {
                foreach (ToolStripItem item in gameDpiSub.DropDownItems)
                {
                    var mi = item as ToolStripMenuItem;
                    if (mi != null && mi.Tag != null)
                    {
                        mi.Checked = ((int)mi.Tag == stage);
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

            bool isAutoRateSupportedMouse = IsSelectedDeviceSupportingAutoGameRate();

            // 1. 标题文字保持恒定简洁，不加点也不改色（复选框与按钮颜色已清晰呈现当前生效状态）
            if (lblRateTitle != null)
            {
                lblRateTitle.Text = "常规回报率";
                lblRateTitle.ForeColor = Color.FromArgb(160, 168, 185);
            }
            if (lblDpiTitle != null)
            {
                lblDpiTitle.Text = "DPI 档位";
                lblDpiTitle.ForeColor = Color.FromArgb(160, 168, 185);
            }
            if (chkAutoGameRate != null)
            {
                chkAutoGameRate.Text = "电竞回报率";
                chkAutoGameRate.ForeColor = Color.Empty;
            }
            if (chkAutoGameDpi != null)
            {
                chkAutoGameDpi.Text = "电竞 DPI";
                chkAutoGameDpi.ForeColor = Color.Empty;
            }

            // 2. 右上角硬件生效徽标：保留并增加中间点显示 DPI，支持独立 X/Y 轴呈现
            int measuredDesktopHz = (selectedDevice != null && selectedDevice.PollingRate > 0)
                ? selectedDevice.PollingRate
                : 0;
            int effectiveDesktopHz = (measuredDesktopHz > 0) ? measuredDesktopHz : desktopPollingRate;
            int currentActiveHz = (isGamingActive && autoGameRateEnabled) ? gamePollingRate : effectiveDesktopHz;
            int desktopHighlightRate = (isGamingActive && autoGameRateEnabled) ? desktopPollingRate : effectiveDesktopHz;
            int currentActiveDpi = 0;
            int currentActiveDpiY = 0;
            int activeStage = (isGamingActive && autoGameDpiEnabled)
                ? gameDpiStage
                : ((desktopDpiStage > 0) ? desktopDpiStage : ((selectedDevice != null && selectedDevice.DpiStage > 0) ? selectedDevice.DpiStage : 0));

            if (dpiStageList != null && dpiStageList.Count > 0)
            {
                if (activeStage > 0 && activeStage <= dpiStageList.Count)
                {
                    currentActiveDpi = dpiStageList[activeStage - 1];
                    if (selectedDevice != null && selectedDevice.DpiStagesY != null && selectedDevice.DpiStagesY.Length >= activeStage)
                    {
                        currentActiveDpiY = selectedDevice.DpiStagesY[activeStage - 1];
                    }
                    else
                    {
                        currentActiveDpiY = currentActiveDpi;
                    }
                }
                else if (selectedDevice != null && selectedDevice.Dpi > 0)
                {
                    currentActiveDpi = selectedDevice.Dpi;
                    currentActiveDpiY = (selectedDevice.DpiY > 0) ? selectedDevice.DpiY : selectedDevice.Dpi;
                }
            }
            else if (selectedDevice != null && selectedDevice.Dpi > 0)
            {
                currentActiveDpi = selectedDevice.Dpi;
                currentActiveDpiY = (selectedDevice.DpiY > 0) ? selectedDevice.DpiY : selectedDevice.Dpi;
            }

            bool isDiffXy = (selectedDevice != null && selectedDevice.IsXyIndependent && currentActiveDpiY > 0 && currentActiveDpiY != currentActiveDpi);

            if (lblLiveRateBadge != null)
            {
                bool showBadge = isAutoRateSupportedMouse && (autoGameRateEnabled || autoGameDpiEnabled);
                lblLiveRateBadge.Visible = showBadge;
                if (showBadge)
                {
                    string badgeText = "● 硬件生效: " + currentActiveHz + " Hz";
                    if (currentActiveDpi > 0)
                    {
                        if (isDiffXy)
                        {
                            badgeText += " · " + currentActiveDpi + "×" + currentActiveDpiY + " DPI";
                        }
                        else
                        {
                            badgeText += " · " + currentActiveDpi + " DPI";
                        }
                    }
                    lblLiveRateBadge.Text = badgeText;
                    lblLiveRateBadge.ForeColor = isGamingActive ? Color.FromArgb(0, 230, 118) : Color.FromArgb(180, 210, 240);
                }
            }

            // 3. 回报率按钮分段高光联动：未使用时深绿预设 (IsPreset = true)，生效时亮绿激活 (IsPreset = false)
            for (int i = 0; i < btnRates.Length; i++)
            {
                if (btnRates[i] != null && pollingRateValues != null && i < pollingRateValues.Length)
                {
                    bool isSel = (pollingRateValues[i] == desktopHighlightRate);
                    btnRates[i].Selected = isSel;
                    btnRates[i].IsPreset = (isSel && isGamingActive && autoGameRateEnabled);
                }
            }

            if (btnGameRates != null)
            {
                for (int i = 0; i < btnGameRates.Length; i++)
                {
                    if (btnGameRates[i] != null && pollingRateValues != null && i < pollingRateValues.Length)
                    {
                        bool isSel = (pollingRateValues[i] == gamePollingRate);
                        btnGameRates[i].Selected = isSel;
                        btnGameRates[i].IsPreset = (isSel && !isGamingActive);
                        ApplyGameRateButtonState(btnGameRates[i]);
                    }
                }
            }

            // 4. DPI 档位按钮分段高光联动：未使用时深绿预设 (IsPreset = true)，生效时亮绿激活 (IsPreset = false)
            int effectiveDesktopStage = (desktopDpiStage > 0) ? desktopDpiStage : ((selectedDevice != null && selectedDevice.DpiStage > 0) ? selectedDevice.DpiStage : 1);
            if (btnDpiStages != null)
            {
                for (int i = 0; i < btnDpiStages.Length; i++)
                {
                    if (btnDpiStages[i] != null && dpiStageList != null && i < dpiStageList.Count)
                    {
                        bool isCur = (i == (effectiveDesktopStage - 1));
                        btnDpiStages[i].Selected = isCur;
                        btnDpiStages[i].IsPreset = (isCur && isGamingActive && autoGameDpiEnabled);
                    }
                }
            }

            if (btnGameDpiStages != null)
            {
                for (int i = 0; i < btnGameDpiStages.Length; i++)
                {
                    if (btnGameDpiStages[i] != null && dpiStageList != null && i < dpiStageList.Count)
                    {
                        bool isSel = ((i + 1) == gameDpiStage);
                        btnGameDpiStages[i].Selected = isSel;
                        btnGameDpiStages[i].IsPreset = (isSel && !isGamingActive);
                        btnGameDpiStages[i].Enabled = autoGameDpiEnabled;
                    }
                }
            }
        }

        private void SetGameRateControlsVisibility(bool visible)
        {
            bool supportsAutoGameRate = IsSelectedDeviceSupportingAutoGameRate();

            // Row 4, 5, 6 仅在卡片展开且功能开启时展示（两档高度，两档展示）
            bool isOptExpanded = (autoGameRateEnabled || autoGameDpiEnabled);
            bool showExpanded = visible && supportsAutoGameRate && isOptExpanded;

            // Row 3: 总开关始终伴随支持设备展示；游戏管理按钮仅在展开时展示
            if (chkAutoGameOpt != null) chkAutoGameOpt.Visible = supportsAutoGameRate;
            if (btnManageGames != null) btnManageGames.Visible = showExpanded;

            if (chkAutoGameDpi != null) chkAutoGameDpi.Visible = showExpanded;
            if (lblGameDpiTitle != null) lblGameDpiTitle.Visible = false;
            if (btnGameDpiStages != null)
            {
                // 严格按板载实际档位数量渲染：有多少档就显示多少，不得凭空造出一个档位。
                int count = (dpiStageList != null && dpiStageList.Count > 0)
                    ? Math.Min(5, dpiStageList.Count) : 0;
                for (int i = 0; i < btnGameDpiStages.Length; i++)
                {
                    if (btnGameDpiStages[i] != null)
                    {
                        btnGameDpiStages[i].Visible = showExpanded && (i < count);
                        btnGameDpiStages[i].Enabled = autoGameDpiEnabled;
                    }
                }
            }

            if (chkAutoGameRate != null) chkAutoGameRate.Visible = showExpanded;
            if (lblGameRateTitle != null) lblGameRateTitle.Visible = false;
            if (btnGameRates != null)
            {
                // 回报率同样按设备实际上报数量渲染，不补默认值。
                int count = (pollingRateValues != null && pollingRateValues.Length > 0)
                    ? Math.Min(5, Math.Min(btnGameRates.Length, pollingRateValues.Length)) : 0;
                for (int i = 0; i < btnGameRates.Length; i++)
                {
                    if (btnGameRates[i] != null)
                    {
                        btnGameRates[i].Visible = showExpanded && (i < count);
                        ApplyGameRateButtonState(btnGameRates[i]);
                    }
                }
            }

            if (lblNotifyModeTitle != null) lblNotifyModeTitle.Visible = showExpanded;
            if (chkNotifyOsd != null) chkNotifyOsd.Visible = showExpanded;
            if (chkNotifyToast != null) chkNotifyToast.Visible = showExpanded;
            if (chkNotifySound != null) chkNotifySound.Visible = showExpanded;
        }

        private void AnimatePerformanceCardHeight(int targetH)
        {
            if (cardPerformance == null) return;
            animTargetCardH = targetH;

            // 1. 若窗体未显示或处于最小化状态，直接原子同步重排，不消耗计时器
            if (!this.Visible || this.WindowState == FormWindowState.Minimized)
            {
                if (cardAnimTimer != null && cardAnimTimer.Enabled) cardAnimTimer.Stop();
                SetGameRateControlsVisibility(autoGameRateEnabled || autoGameDpiEnabled);
                AdjustPerformanceCardHeight(targetH);
                return;
            }

            // 2. 若高度已然吻合，直接校准可见性并退出
            if (cardPerformance.Height == targetH)
            {
                if (cardAnimTimer != null && cardAnimTimer.Enabled) cardAnimTimer.Stop();
                SetGameRateControlsVisibility(autoGameRateEnabled || autoGameDpiEnabled);
                return;
            }

            // 3. 卷帘门遮罩式动效生命周期：
            // 展开开始时立即点亮子控件，配合卡片容器硬件裁剪 (WS_CLIPCHILDREN) 像卷帘门一样随着底边拉伸自然向下露出；
            // 收回过程中保持子控件可见，随着底边向上收拢自然被裁切遮盖，待完全缩入卡片底边后才彻底设为 false。
            if (autoGameRateEnabled || autoGameDpiEnabled)
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
                        SetGameRateControlsVisibility(autoGameRateEnabled || autoGameDpiEnabled);

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

        public bool IsDeviceSupportingAutoGameRate(MouseBatteryInfo dev)
        {
            if (dev == null || dev.Category != DeviceCategory.Mouse) return false;
            HardwareDriverRegistry.EnsureCapabilitiesInitialized(dev);
            return dev.Capabilities != null &&
                   (dev.Capabilities.SupportsPollingRate || dev.Capabilities.SupportsDpiStages || dev.Capabilities.SupportsDpi);
        }

        public bool IsSelectedDeviceSupportingAutoGameRate()
        {
            return IsDeviceSupportingAutoGameRate(selectedDevice);
        }

        public MouseBatteryInfo GetActiveMouseDevice()
        {
            if (selectedDevice != null && selectedDevice.Category == DeviceCategory.Mouse && IsDeviceSupportingAutoGameRate(selectedDevice))
            {
                return selectedDevice;
            }
            if (activeDevices != null)
            {
                for (int i = 0; i < activeDevices.Count; i++)
                {
                    var d = activeDevices[i];
                    if (d != null && d.Category == DeviceCategory.Mouse && IsDeviceSupportingAutoGameRate(d))
                    {
                        return d;
                    }
                }
            }
            return selectedDevice;
        }

        private void UpdatePerformanceCardHeightForCurrentDevice()
        {
            var dev = selectedDevice;
            if (dev != null && IsSelectedDeviceSupportingAutoGameRate())
            {
                bool isOptExpanded = (autoGameRateEnabled || autoGameDpiEnabled);
                int targetH = isOptExpanded ? (int)(255 * dpiScale) : (int)(148 * dpiScale);
                RelayoutPerformanceControls();
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
            if (curDev == null) return;
            if (!IsDeviceSupportingAutoGameRate(curDev)) return;
            HardwareDriverRegistry.EnsureCapabilitiesInitialized(curDev);

            int curStage = curDev.DpiStage > 0 ? curDev.DpiStage : 1;
            int[] stagesX = (curDev.DpiStages != null && curDev.DpiStages.Length > 0) ? curDev.DpiStages : dpiStageList.ToArray();
            int[] stagesY = (curDev.DpiStagesY != null && curDev.DpiStagesY.Length == stagesX.Length) ? curDev.DpiStagesY : stagesX;
            bool isXy = curDev.IsXyIndependent;
            double xyRatio = (curDev.XyRatio > 0) ? curDev.XyRatio : 1.0;
            string devName = !string.IsNullOrEmpty(curDev.DisplayName) ? curDev.DisplayName : ((curDev.Brand ?? "Gaming") + " Mouse");

            int minDpi = (curDev.Capabilities != null && curDev.Capabilities.MinDpi > 0) ? curDev.Capabilities.MinDpi : 50;
            int maxDpi = (curDev.Capabilities != null && curDev.Capabilities.MaxDpi > 0) ? curDev.Capabilities.MaxDpi : 26000;
            int step = (curDev.Capabilities != null && curDev.Capabilities.DpiStep > 0) ? curDev.Capabilities.DpiStep : 50;
            string sensorDesc = (curDev.Capabilities != null && !string.IsNullOrEmpty(curDev.Capabilities.SensorDescription))
                ? curDev.Capabilities.SensorDescription
                : string.Format("{0} 高精度光学传感器 (最高 {1:N0} DPI)", curDev.Brand ?? "电竞", maxDpi);

            var cap = new RazerDeviceHelper.RazerDpiCapability
            {
                MinDpi = minDpi,
                MaxDpi = maxDpi,
                Step = step,
                Description = sensorDesc
            };

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
                    OnDpiChanged(devBrand, activeDpiX, activeStage, newStagesX.Length, targetDevId, activeDpiY);
                    UpdatePerformanceSection(curDev, curDev.BrandColor);
                    UpdateStatusDisplay();

                    // 3. Persist to hardware in background thread via unified HardwareCommandBus
                    new Thread(() => {
                        HardwareCommandBus.UpdateHardwareStages(curDev, activeStage, newStagesX, newStagesY);
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

        private void OpenPollingRateManagementDialog()
        {
            var curDev = selectedDevice;
            if (curDev == null) return;

            string devName = !string.IsNullOrEmpty(curDev.DisplayName) ? curDev.DisplayName : (curDev.Brand + " 鼠标");
            // 严格按设备实际上报的回报率创建选项，不补全表。
            int[] supported = (curDev.SupportedPollingRates != null && curDev.SupportedPollingRates.Length > 0)
                ? curDev.SupportedPollingRates
                : ((curDev.PollingRate > 0) ? new int[] { curDev.PollingRate } : new int[0]);
            if (supported.Length == 0) return;

            int[] activeSelected = (pollingRateValues != null && pollingRateValues.Length > 0)
                ? pollingRateValues
                : supported;

            // 读不到就不编造 1000 Hz。
            int curHz = curDev.PollingRate > 0 ? curDev.PollingRate : (supported.Length > 0 ? supported[0] : 0);
            using (var dlg = new PollingRateManagementDialog(devName, supported, activeSelected, curHz, dpiScale))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    int[] newSelected = dlg.SelectedRates;
                    if (newSelected != null && newSelected.Length > 0)
                    {
                        if (newSelected.Length > 5)
                        {
                            List<int> clamp5 = new List<int>(newSelected);
                            clamp5.Sort();
                            while (clamp5.Count > 5) clamp5.RemoveAt(0);
                            newSelected = clamp5.ToArray();
                        }
                        pollingRateValues = newSelected;
                        SaveCustomPollingRates(curDev.DeviceId, newSelected);

                        // 档位变更后电竞回报率同步联动：若当前电竞回报率不在新档位列表中，自动吸附到最高档
                        if (Array.IndexOf(newSelected, gamePollingRate) == -1)
                        {
                            gamePollingRate = newSelected[newSelected.Length - 1];
                            SaveConfig();
                        }
                        // 若当前桌面回报率不在新档位列表中，自动吸附到第一档
                        if (Array.IndexOf(newSelected, desktopPollingRate) == -1)
                        {
                            desktopPollingRate = newSelected[0];
                            SaveConfig();
                        }

                        RelayoutPerformanceControls();
                        SetGameRateControlsVisibility(autoGameRateEnabled);

                        curHz = curDev.PollingRate > 0 ? curDev.PollingRate : (newSelected.Length > 0 ? newSelected[0] : 0);
                        UpdateDesktopRateButtonSelection(curHz);
                        UpdateGameRateButtonSelection(gamePollingRate);
                        UpdatePollingRateDisplayStates();
                        UpdateStatusDisplay();
                    }
                }
            }
        }

        private int[] GetDefaultActivePollingRates(int[] supported)
        {
            if (supported == null || supported.Length == 0)
            {
                return new int[] { 1000 };
            }

            List<int> sorted = new List<int>();
            for (int i = 0; i < supported.Length; i++)
            {
                if (supported[i] > 0 && !sorted.Contains(supported[i]))
                {
                    sorted.Add(supported[i]);
                }
            }
            sorted.Sort();

            if (sorted.Count <= 4)
            {
                return sorted.ToArray();
            }

            // 如果超过4个档位，则将最高的4档显示出来，低档位则放在配置列表内，使用户可配置
            int skipCount = sorted.Count - 4;
            List<int> top4 = new List<int>();
            for (int i = skipCount; i < sorted.Count; i++)
            {
                top4.Add(sorted[i]);
            }
            return top4.ToArray();
        }

        private void SaveCustomPollingRates(string devId, int[] rates)
        {
            try
            {
                using (var key = Registry.CurrentUser.CreateSubKey(ConfigRegistryKey + @"\CustomPollingRates"))
                {
                    if (key != null && rates != null && rates.Length > 0)
                    {
                        List<string> strList = new List<string>();
                        for (int i = 0; i < rates.Length; i++) strList.Add(rates[i].ToString());
                        string s = string.Join(",", strList.ToArray());
                        string k = string.IsNullOrEmpty(devId) ? "Default" : devId.Replace("\\", "_").Replace("/", "_");
                        key.SetValue(k, s);
                    }
                }
            }
            catch { }
        }

        private int[] LoadCustomPollingRates(string devId, int[] fallbackRates)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(ConfigRegistryKey + @"\CustomPollingRates"))
                {
                    if (key != null)
                    {
                        string k = string.IsNullOrEmpty(devId) ? "Default" : devId.Replace("\\", "_").Replace("/", "_");
                        object val = key.GetValue(k);
                        if (val != null)
                        {
                            string str = val.ToString();
                            string[] parts = str.Split(new char[] { ',' }, StringSplitOptions.RemoveEmptyEntries);
                            List<int> list = new List<int>();
                            for (int i = 0; i < parts.Length; i++)
                            {
                                int v;
                                if (int.TryParse(parts[i].Trim(), out v) && v > 0)
                                {
                                    list.Add(v);
                                }
                            }
                            if (list.Count > 0)
                            {
                                list.Sort();
                                while (list.Count > 5)
                                {
                                    list.RemoveAt(0); // 最多 5 档快捷显示，裁剪超额低档位
                                }
                                return list.ToArray();
                            }
                        }
                    }
                }
            }
            catch { }
            return fallbackRates;
        }

        private void OpenGameProcessManager()
        {
            var dlg = new GameProcessManagerForm(dpiScale, monitoredGameProcesses, fallbackFullscreenEnabled, gameOptMode, (updatedList, fb, mode) => {
                monitoredGameProcesses = new List<string>(updatedList);
                fallbackFullscreenEnabled = fb;
                gameOptMode = mode;
                SaveConfig();
                UpdateManageGamesButtonText();
            });
            dlg.ShowDialog(this);
        }

        private void OpenPriorityManager()
        {
            using (var form = new DevicePriorityForm(activeDevices, dpiScale))
            {
                form.ShowDialog(this);
                activeDevices.RemoveAll(d => DeviceManager.IsDeviceHidden(d.DeviceId) || (!string.IsNullOrEmpty(d.HardwareFingerprint) && DeviceManager.IsDeviceHidden(d.HardwareFingerprint)));
                RefreshBatteryStatus(true);
            }
        }

        private void OpenAddDeviceWizard()
        {
            activeDevices.RemoveAll(d => DeviceManager.IsDeviceHidden(d.DeviceId) || (!string.IsNullOrEmpty(d.HardwareFingerprint) && DeviceManager.IsDeviceHidden(d.HardwareFingerprint)));
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
                int baseW = (int)(486 * dpiScale);
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
                int card2H;
                if (!autoGameRateEnabled && !autoGameDpiEnabled)
                {
                    card2H = (int)(148 * dpiScale);
                }
                else if (autoGameRateEnabled && autoGameDpiEnabled)
                {
                    card2H = (int)(255 * dpiScale);
                }
                else
                {
                    card2H = (int)(218 * dpiScale);
                }
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

        private void ApplyRapooDpiConfigurationFromUI(MouseBatteryInfo device, int stage, int[] stagesX, int[] stagesY, bool independent, double ratio)
        {
            new Thread(() => {
                if (!HardwareCommandBus.UpdateHardwareStages(device, stage, stagesX, stagesY)) return;
                try
                {
                    this.BeginInvoke(new Action(() => {
                        if (selectedDevice != device || IsDisposed) return;
                        dpiStageList = new List<int>(stagesX);
                        dpiStageValues = (int[])stagesX.Clone();
                        device.DpiStages = (int[])stagesX.Clone();
                        device.DpiStagesY = (int[])stagesY.Clone();
                        device.DpiStageCount = stagesX.Length;
                        device.DpiStage = stage;
                        device.Dpi = stagesX[stage - 1];
                        device.DpiY = stagesY[stage - 1];
                        device.IsXyIndependent = independent;
                        device.XyRatio = ratio;
                        SaveCustomDpiStages();
                        RelayoutDpiButtons();
                        HighlightDpiStage(stage);
                        OnDpiChanged("Rapoo", device.Dpi, stage, stagesX.Length, device.DeviceId, device.DpiY);
                        UpdatePerformanceSection(device, device.BrandColor);
                        UpdateStatusDisplay();
                    }));
                }
                catch (InvalidOperationException) { }
            }) { IsBackground = true }.Start();
        }

}
    }

