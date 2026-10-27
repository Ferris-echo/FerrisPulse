// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// 1. 目标平台与编译规范：严格维持 C# 5.0 语法标准与 .NET Framework 4.0 兼容性。严禁引入任何第三方包或依赖。
// 2. 编码规范：所有文件严格保存为 UTF-8 带签名（BOM）格式，防止中文字符乱码。
// 3. 架构规范：本文件属于 MainForm 的分部类 (partial class) 之一。修改时请保持职责清晰，任何新增方法或逻辑请同步维护 ARCHITECTURE.md。
// 模块描述: MainForm 自适应重排层——DPI/回报率档位按钮重排、性能卡片高度动画、档位高亮与选中态刷新
//
// ⚠️ 双处布局说明：控件「初始创建」在 MainForm.UI.cs 的 InitializeFormUI()，
//    「自适应重排」全部集中在本文件。新增控件时必须两处同步：先创建，再在此处定位。
using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Threading;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public partial class MainForm : Form
    {

        private void RelayoutDpiButtons()
        {
            if (cardPerformance == null || btnDpiStages == null || btnDpiManage == null) return;

            int cPad = (int)(14 * dpiScale);
            int secW = cardPerformance.ClientSize.Width - (cPad * 2);
            int r1Y = (int)(38 * dpiScale);
            int lblW = (int)(82 * dpiScale);
            int segH = (int)(28 * dpiScale);

            int dpiStartX = cPad + lblW;
            int availableW = secW - lblW;
            int gap = (int)(5 * dpiScale);
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

        private void RelayoutRateButtons()
        {
            if (cardPerformance == null || btnRates == null || btnRateManage == null) return;

            int cPad = (int)(14 * dpiScale);
            int secW = cardPerformance.ClientSize.Width - (cPad * 2);
            int r2Y = (int)(75 * dpiScale);
            int lblW = (int)(82 * dpiScale);
            int segH = (int)(28 * dpiScale);

            int hzStartX = cPad + lblW;
            int availableW = secW - lblW;
            int gap = (int)(5 * dpiScale);
            int manageBtnW = segH;

            // 回报率最多 5 档快捷显示
            int count = Math.Max(1, Math.Min(5, Math.Min(btnRates.Length, (pollingRateValues != null) ? pollingRateValues.Length : 4)));

            // 1. 常规桌面回报率档位（末尾空出齿轮按钮位置）
            int remainingW = availableW - manageBtnW - gap;
            int hzSegW = (remainingW - (count - 1) * gap) / count;

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
                    string tipText = string.Format("{0} Hz · 响应延迟 {1:F3} ms", hzVal, 1000.0 / hzVal);
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
        }

        private void RelayoutPerformanceControls()
        {
            if (cardPerformance == null) return;
            int cPad = (int)(14 * dpiScale);
            int secW = cardPerformance.ClientSize.Width - (cPad * 2);
            int lblW = (int)(82 * dpiScale);
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
                int dpiCount = Math.Max(1, Math.Min(5, dpiStageList.Count));
                int gameDpiManageW = segH;
                int gameDpiRemainingW = availableW - gameDpiManageW - gap;
                int stageW = (gameDpiRemainingW - (dpiCount - 1) * gap) / dpiCount;
                for (int i = 0; i < btnGameDpiStages.Length; i++)
                {
                    if (btnGameDpiStages[i] != null)
                    {
                        if (i < dpiCount)
                        {
                            btnGameDpiStages[i].Location = new Point(hzStartX + i * (stageW + gap), r4Y);
                            btnGameDpiStages[i].Size = new Size(stageW, segH);
                            int valX = (dpiStageList != null && i < dpiStageList.Count) ? dpiStageList[i] : 800;
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
                if (btnGameDpiManage != null)
                {
                    btnGameDpiManage.Location = new Point(hzStartX + dpiCount * (stageW + gap), r4Y);
                    btnGameDpiManage.Size = new Size(gameDpiManageW, segH);
                    btnGameDpiManage.Visible = showExpanded;
                }
            }

            // Row 5: 电竞回报率 (恒定 Y = 184) —— 标签比 Row 4 多一个字，单独加宽，避免「率」被档位按钮遮挡
            int r5Y = (int)(184 * dpiScale);
            int gameLblW = lblW + (int)(12 * dpiScale);
            int gameHzStartX = cPad + gameLblW;
            int gameAvailableW = secW - gameLblW;
            if (chkAutoGameRate != null)
            {
                chkAutoGameRate.Location = new Point(cPad, r5Y);
                chkAutoGameRate.Size = new Size(gameLblW, segH);
                chkAutoGameRate.Visible = showExpanded;
            }
            if (btnGameRates != null)
            {
                int rateCount = Math.Max(1, Math.Min(5, Math.Min(btnGameRates.Length, (pollingRateValues != null) ? pollingRateValues.Length : 4)));
                int gameSegW = (gameAvailableW - (rateCount - 1) * gap) / rateCount;
                for (int i = 0; i < btnGameRates.Length; i++)
                {
                    if (btnGameRates[i] != null)
                    {
                        if (i < rateCount && pollingRateValues != null && i < pollingRateValues.Length)
                        {
                            int hzVal = pollingRateValues[i];
                            btnGameRates[i].Location = new Point(gameHzStartX + i * (gameSegW + gap), r5Y);
                            btnGameRates[i].Size = new Size(gameSegW, segH);
                            if (gameSegW < (int)(46 * dpiScale) && hzVal >= 1000)
                            {
                                btnGameRates[i].Text = (hzVal / 1000) + "K";
                            }
                            else
                            {
                                btnGameRates[i].Text = hzVal + " Hz";
                            }
                            string tip = string.Format("全屏电竞回报率: {0} Hz · 延迟 {1:F3} ms", hzVal, 1000.0 / hzVal);
                            toolTipTop.SetToolTip(btnGameRates[i], tip);
                            btnGameRates[i].Enabled = autoGameRateEnabled;
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

        private void SwitchDpiStageByIndex(int index)
        {
            if (index < 0 || index >= dpiStageList.Count) return;
            int targetStage = index + 1;
            int targetDpi = dpiStageList[index];

            if (isGamingActive && autoGameDpiEnabled)
            {
                gameDpiStage = targetStage;
                UpdateGameDpiButtonSelection(targetStage);
                SaveConfig();
            }

            var curDev = selectedDevice;
            if (curDev != null && FdasRuntimeHelper.IsFdasMouse(curDev))
            {
                int[] stagesX = (curDev.DpiStages != null && curDev.DpiStages.Length > 0) ? curDev.DpiStages : dpiStageList.ToArray();
                int[] stagesY = (curDev.DpiStagesY != null && curDev.DpiStagesY.Length == stagesX.Length) ? curDev.DpiStagesY : stagesX;
                int targetDpiY = (stagesY != null && stagesY.Length > index) ? stagesY[index] : targetDpi;
                string devId = curDev.DeviceId;
                new Thread(() => {
                    bool ok = FdasRuntimeHelper.SetDpi(devId, targetDpi, targetDpiY, targetStage);
                    this.BeginInvoke(new Action(() => {
                        if (curDev != null)
                        {
                            curDev.Dpi = targetDpi;
                            curDev.DpiY = targetDpiY;
                            curDev.DpiStage = targetStage;
                        }
                        HighlightDpiStage(targetStage);
                        OnDpiChanged(curDev != null ? curDev.Brand : "FDAS", targetDpi, targetStage, dpiStageList.Count, devId, targetDpiY);
                    }));
                }) { IsBackground = true }.Start();
            }
            else if (curDev != null && curDev.Brand == "Razer")
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
                        OnDpiChanged("Razer", targetDpi, targetStage, dpiStageList.Count, devId, targetDpiY);
                    }));
                }) { IsBackground = true }.Start();
            }
            else if (curDev != null && (curDev.Brand == "Valkyrie" || curDev.Brand == "瓦尔基里"))
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
                        OnDpiChanged("Valkyrie", targetDpi, targetStage, dpiStageList.Count, null, targetDpiY);
                    }));
                }) { IsBackground = true }.Start();
            }
            else if (curDev != null && (curDev.Brand == "Logitech" || curDev.Brand == "罗技"))
            {
                int[] stagesX = (curDev.DpiStages != null && curDev.DpiStages.Length > 0) ? curDev.DpiStages : dpiStageList.ToArray();
                int[] stagesY = (curDev.DpiStagesY != null && curDev.DpiStagesY.Length == stagesX.Length) ? curDev.DpiStagesY : stagesX;
                int targetDpiY = (stagesY != null && stagesY.Length > index) ? stagesY[index] : targetDpi;
                string devId = curDev.DeviceId;
                new Thread(() => {
                    bool ok = LogitechDeviceHelper.SetLogitechDpi(targetDpi, targetDpiY, devId);
                    this.BeginInvoke(new Action(() => {
                        if (curDev != null)
                        {
                            curDev.Dpi = targetDpi;
                            curDev.DpiY = targetDpiY;
                            curDev.DpiStage = targetStage;
                        }
                        HighlightDpiStage(targetStage);
                        OnDpiChanged("Logitech", targetDpi, targetStage, dpiStageList.Count, devId, targetDpiY);
                    }));
                }) { IsBackground = true }.Start();
            }
            else if (curDev != null && (curDev.Brand == "Rapoo" || curDev.Brand == "雷柏"))
            {
                int[] stagesX = (curDev.DpiStages != null && curDev.DpiStages.Length > 0) ? curDev.DpiStages : dpiStageList.ToArray();
                int[] stagesY = (curDev.DpiStagesY != null && curDev.DpiStagesY.Length == stagesX.Length) ? curDev.DpiStagesY : stagesX;
                int targetDpiY = (stagesY != null && stagesY.Length > index) ? stagesY[index] : targetDpi;
                new Thread(() => {
                    bool ok = RapooDeviceHelper.SetRapooDpiStage(targetStage);
                    this.BeginInvoke(new Action(() => {
                        if (curDev != null)
                        {
                            curDev.Dpi = targetDpi;
                            curDev.DpiY = targetDpiY;
                            curDev.DpiStage = targetStage;
                        }
                        HighlightDpiStage(targetStage);
                        OnDpiChanged("Rapoo", targetDpi, targetStage, dpiStageList.Count, null, targetDpiY);
                    }));
                }) { IsBackground = true }.Start();
            }
            else if (curDev != null && (curDev.Brand == "Mchose" || curDev.Brand == "MCHOSE" || curDev.Brand == "迈从") && curDev.Category == DeviceCategory.Mouse)
            {
                int[] stagesX = (curDev.DpiStages != null && curDev.DpiStages.Length > 0) ? curDev.DpiStages : dpiStageList.ToArray();
                int[] stagesY = (curDev.DpiStagesY != null && curDev.DpiStagesY.Length == stagesX.Length) ? curDev.DpiStagesY : stagesX;
                int targetDpiY = (stagesY != null && stagesY.Length > index) ? stagesY[index] : targetDpi;
                new Thread(() => {
                    bool ok = MchoseMouseHelper.SetMchoseDpiStage(targetStage);
                    this.BeginInvoke(new Action(() => {
                        if (curDev != null)
                        {
                            curDev.Dpi = targetDpi;
                            curDev.DpiY = targetDpiY;
                            curDev.DpiStage = targetStage;
                        }
                        HighlightDpiStage(targetStage);
                        OnDpiChanged("MCHOSE", targetDpi, targetStage, dpiStageList.Count, curDev != null ? curDev.DeviceId : null, targetDpiY);
                    }));
                }) { IsBackground = true }.Start();
            }
            else if (curDev != null && curDev.DeviceId != null && curDev.DeviceId.StartsWith("FDAS:"))
            {
                int[] stagesX = (curDev.DpiStages != null && curDev.DpiStages.Length > 0) ? curDev.DpiStages : dpiStageList.ToArray();
                int[] stagesY = (curDev.DpiStagesY != null && curDev.DpiStagesY.Length == stagesX.Length) ? curDev.DpiStagesY : stagesX;
                int targetDpiY = (stagesY != null && stagesY.Length > index) ? stagesY[index] : targetDpi;
                string devId = curDev.DeviceId;
                new Thread(() => {
                    bool ok = FdasRuntimeHelper.SetDpi(devId, targetDpi, targetDpiY, targetStage);
                    this.BeginInvoke(new Action(() => {
                        if (curDev != null)
                        {
                            curDev.Dpi = targetDpi;
                            curDev.DpiY = targetDpiY;
                            curDev.DpiStage = targetStage;
                        }
                        HighlightDpiStage(targetStage);
                        OnDpiChanged(curDev != null ? curDev.Brand : "FDAS", targetDpi, targetStage, dpiStageList.Count, devId, targetDpiY);
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
            int currentActiveHz = (isGamingActive && autoGameRateEnabled) ? gamePollingRate : desktopPollingRate;
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
                    bool isSel = (pollingRateValues[i] == desktopPollingRate);
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
                        btnGameRates[i].Enabled = autoGameRateEnabled;
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
                int count = Math.Max(1, Math.Min(5, dpiStageList.Count));
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
                int count = Math.Max(1, Math.Min(5, Math.Min(btnGameRates.Length, (pollingRateValues != null) ? pollingRateValues.Length : 4)));
                for (int i = 0; i < btnGameRates.Length; i++)
                {
                    if (btnGameRates[i] != null)
                    {
                        btnGameRates[i].Visible = showExpanded && (i < count);
                        btnGameRates[i].Enabled = autoGameRateEnabled;
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
            bool isRazer = (dev.Brand == "Razer" || (dev.DeviceId != null && dev.DeviceId.StartsWith("Razer:1532:")));
            bool isLogitech = (dev.Brand == "Logitech" || dev.Brand == "罗技");
            bool isRapoo = (dev.Brand == "Rapoo" || dev.Brand == "雷柏");
            bool isValkyrie = (dev.Brand == "Valkyrie" || dev.Brand == "瓦尔基里");
            bool isMchose = ((dev.Brand == "Mchose" || dev.Brand == "MCHOSE" || dev.Brand == "迈从") && dev.Category == DeviceCategory.Mouse);
            bool isZaopin = (dev.Brand == "Zaopin" || dev.Brand == "皂品" || dev.Brand == "VGN");
            bool isFdas = FdasRuntimeHelper.IsFdasMouse(dev);
            return isRazer || isLogitech || isRapoo || isValkyrie || isMchose || isZaopin || isFdas;
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
    }
}
