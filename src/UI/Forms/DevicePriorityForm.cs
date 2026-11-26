// ============================================================================
// 模块名称: DevicePriorityForm.cs
// 所属区域: UI/Forms (业务对话框层)
// 核心职责:
//   多外设托盘图标展示优先级调整窗口，承载 PriorityReorderControl，支持多托盘开关平滑折叠/展开、平滑滑动分段胶囊选数动画、保存排序以及外观双色持久化。
// 依赖关系: Controls.PriorityReorderControl, Controls.SlidingSegmentTrack, Controls.ModernButton, Core.ConfigManager
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、属性或绘图逻辑时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（❌ 严禁 $"..." 插值，必须使用 string.Format 或 + 拼接）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class DevicePriorityForm : DpiAwareForm
    {
        private PriorityReorderControl reorderControl;
        private RoundedCard cardSettings;
        private ModernCheckBox chkLowBatteryFirst;
        private ModernCheckBox chkMultiTray;
        private Panel pnlTrayCountBar;
        private SlidingSegmentTrack pnlSegmentTrack;
        private readonly List<ModernButton> presetCountButtons = new List<ModernButton>();
        private Label lblListSection;
        private Label lblListHint;
        private Panel pnlFooter;
        private Label lblDeviceCount;
        private ModernButton btnSave;
        private ModernButton btnCancel;
        private int selectedTrayCount = 3;
        private int trayBarExpandedW;
        private int trayBarHeight;
        private System.Windows.Forms.Timer trayBarAnimTimer;
        private int trayBarAnimStartW;
        private int trayBarAnimTargetW;
        private DateTime trayBarAnimStartTime;
        private const int TrayBarAnimDurationMs = 160;
        private readonly HashSet<string> editedAppearanceIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public bool ResultMultiTrayEnabled { get; private set; }
        public int ResultMultiTrayMaxCount { get; private set; }

        public DevicePriorityForm(List<MouseBatteryInfo> devices) : this(devices, 1.0f) { }

        public DevicePriorityForm(List<MouseBatteryInfo> devices, float scale)
            : this(BuildItemsFromDevices(devices),
                   DeviceManager.IsTrayLowBatteryFirstEnabled(),
                   ConfigManager.GetMultiTrayEnabled(),
                   ConfigManager.GetMultiTrayMaxCount(),
                   scale)
        {
        }

        public DevicePriorityForm(List<PriorityItem> items, bool lowBatteryFirst, bool multiTrayEnabled, int multiTrayMaxCount, float scale)
            : base(scale)
        {
            int itemCount = (items != null) ? items.Count : 0;
            this.selectedTrayCount = Math.Max(2, Math.Min(16, multiTrayMaxCount));
            this.ResultMultiTrayEnabled = multiTrayEnabled;
            this.ResultMultiTrayMaxCount = this.selectedTrayCount;

            this.Text = "设备优先级管理 (拖拽排序)";
            int initClientW = (int)(480 * dpiScale);
            int initClientH = (int)(560 * dpiScale);
            this.ClientSize = new Size(initClientW, initClientH);
            this.BackColor = Color.FromArgb(16, 18, 23);
            this.ForeColor = Color.White;
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            int pad = (int)(22 * dpiScale);
            int contentW = initClientW - (pad * 2);

            var lblTip = new Label();
            lblTip.Text = "调整状态栏展示顺位，或为每台外设定制专属名称与配色";
            lblTip.Font = new Font("Microsoft YaHei UI", 8.6F);
            lblTip.ForeColor = Color.FromArgb(136, 148, 170);
            lblTip.Location = new Point(pad, (int)(54 * dpiScale));
            lblTip.Size = new Size(contentW, (int)(20 * dpiScale));
            lblTip.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            this.Controls.Add(lblTip);

            // 1. 列表分区标题行与优先拖拽列表（第一视觉焦点置顶）
            int sectionY = (int)(84 * dpiScale);
            lblListSection = new Label();
            lblListSection.Text = "设备展示顺序";
            lblListSection.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold);
            lblListSection.ForeColor = Color.FromArgb(152, 164, 188);
            lblListSection.AutoSize = true;
            lblListSection.Location = new Point(pad, sectionY);
            this.Controls.Add(lblListSection);

            lblListHint = new Label();
            lblListHint.Text = "拖拽排序 · 点击色点定制";
            lblListHint.Font = new Font("Microsoft YaHei UI", 8.2F);
            lblListHint.ForeColor = Color.FromArgb(110, 122, 146);
            lblListHint.AutoSize = true;
            lblListHint.Location = new Point(initClientW - pad - lblListHint.PreferredWidth, sectionY + (int)(1 * dpiScale));
            lblListHint.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            this.Controls.Add(lblListHint);

            // 2. 稳重底部操作栏 (Footer Bar)
            int footerH = (int)(62 * dpiScale);
            pnlFooter = new Panel();
            pnlFooter.Location = new Point(1, initClientH - footerH - 1);
            pnlFooter.Size = new Size(initClientW - 2, footerH);
            pnlFooter.BackColor = Color.FromArgb(20, 22, 29);
            pnlFooter.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            pnlFooter.Paint += (s, e) => {
                using (Pen topLine = new Pen(Color.FromArgb(35, 40, 54), 1))
                {
                    e.Graphics.DrawLine(topLine, 0, 0, pnlFooter.Width, 0);
                }
            };
            this.Controls.Add(pnlFooter);

            lblDeviceCount = new Label();
            lblDeviceCount.Text = string.Format("共 {0} 台外设", itemCount);
            lblDeviceCount.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblDeviceCount.ForeColor = Color.FromArgb(122, 134, 158);
            lblDeviceCount.AutoSize = true;
            lblDeviceCount.Location = new Point(pad - 1, (footerH - (int)(18 * dpiScale)) / 2);
            pnlFooter.Controls.Add(lblDeviceCount);

            int btnCancelW = (int)(92 * dpiScale);
            int btnSaveW = (int)(118 * dpiScale);
            int btnH = (int)(34 * dpiScale);
            int btnY = (footerH - btnH) / 2;
            int rightMargin = pad - 1;

            btnCancel = new ModernButton();
            btnCancel.Text = "取消";
            btnCancel.Font = new Font("Microsoft YaHei UI", 9.2F, FontStyle.Bold, GraphicsUnit.Point);
            btnCancel.NormalColor = Color.FromArgb(29, 33, 43);
            btnCancel.HoverColor = Color.FromArgb(41, 47, 62);
            btnCancel.PressedColor = Color.FromArgb(22, 25, 34);
            btnCancel.BorderColor = Color.FromArgb(46, 53, 72);
            btnCancel.ForeColor = Color.FromArgb(203, 212, 230);
            btnCancel.CornerRadius = (int)(7 * dpiScale);
            btnCancel.Location = new Point(pnlFooter.Width - rightMargin - btnSaveW - (int)(10 * dpiScale) - btnCancelW, btnY);
            btnCancel.Size = new Size(btnCancelW, btnH);
            btnCancel.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            pnlFooter.Controls.Add(btnCancel);

            btnSave = new ModernButton();
            btnSave.Text = "确定并保存";
            btnSave.Font = new Font("Microsoft YaHei UI", 9.2F, FontStyle.Bold, GraphicsUnit.Point);
            btnSave.NormalColor = Color.FromArgb(0, 200, 83);
            btnSave.HoverColor = Color.FromArgb(0, 224, 104);
            btnSave.PressedColor = Color.FromArgb(0, 168, 70);
            btnSave.ForeColor = Color.FromArgb(7, 26, 15);
            btnSave.CornerRadius = (int)(7 * dpiScale);
            btnSave.Location = new Point(pnlFooter.Width - rightMargin - btnSaveW, btnY);
            btnSave.Size = new Size(btnSaveW, btnH);
            btnSave.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            btnSave.Click += (s, e) => {
                SaveChanges();
                this.DialogResult = DialogResult.OK;
                this.Close();
            };
            pnlFooter.Controls.Add(btnSave);

            // 3. 抬升式深空圆角设置分组卡片（恒定双行紧凑高度，多托盘数量胶囊同行右侧平滑水平显隐）
            int innerPad = (int)(16 * dpiScale);
            int innerW = contentW - (innerPad * 2);
            int rowH = (int)(44 * dpiScale);
            int initSettingsH = rowH * 2;

            cardSettings = new RoundedCard();
            cardSettings.Location = new Point(pad, pnlFooter.Top - initSettingsH - (int)(16 * dpiScale));
            cardSettings.Size = new Size(contentW, initSettingsH);
            cardSettings.CornerRadius = (int)(10 * dpiScale);
            cardSettings.CardColor = Color.FromArgb(24, 27, 35);
            cardSettings.BorderColor = Color.FromArgb(40, 46, 60);
            cardSettings.Anchor = AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            cardSettings.Paint += (s, e) => {
                int divY = (int)(44 * dpiScale);
                int padIn = (int)(16 * dpiScale);
                if (cardSettings.Width > padIn * 2 + 4)
                {
                    using (Pen divPen = new Pen(Color.FromArgb(35, 40, 54), 1))
                    {
                        e.Graphics.DrawLine(divPen, padIn, divY, cardSettings.Width - padIn, divY);
                    }
                }
            };
            this.Controls.Add(cardSettings);

            chkLowBatteryFirst = new ModernCheckBox();
            chkLowBatteryFirst.Text = "低电量设备优先显示（电量 ≤ 30%）";
            chkLowBatteryFirst.Font = new Font("Microsoft YaHei UI", 9.0F, FontStyle.Regular, GraphicsUnit.Point);
            chkLowBatteryFirst.ForeColor = Color.FromArgb(224, 232, 246);
            chkLowBatteryFirst.BackColor = cardSettings.CardColor;
            chkLowBatteryFirst.Checked = lowBatteryFirst;
            chkLowBatteryFirst.Location = new Point(innerPad, (int)(10 * dpiScale));
            chkLowBatteryFirst.Size = new Size(innerW, (int)(24 * dpiScale));
            chkLowBatteryFirst.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            cardSettings.Controls.Add(chkLowBatteryFirst);

            chkMultiTray = new ModernCheckBox();
            chkMultiTray.Text = "状态栏多设备独立图标";
            chkMultiTray.Font = new Font("Microsoft YaHei UI", 9.0F, FontStyle.Regular, GraphicsUnit.Point);
            chkMultiTray.ForeColor = Color.FromArgb(224, 232, 246);
            chkMultiTray.BackColor = cardSettings.CardColor;
            chkMultiTray.Checked = multiTrayEnabled;
            chkMultiTray.Location = new Point(innerPad, rowH + (rowH - (int)(24 * dpiScale)) / 2);
            chkMultiTray.Size = new Size((int)(180 * dpiScale), (int)(24 * dpiScale));
            chkMultiTray.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            chkMultiTray.CheckedChanged += (s, e) => UpdateTrayCountBarState();
            cardSettings.Controls.Add(chkMultiTray);

            // 同行右侧自适应滑动数量选择条：已知设备数 <= 5 仅显示分段胶囊；> 5 时附带自定义输入
            trayBarHeight = (int)(30 * dpiScale);
            pnlTrayCountBar = new Panel();
            pnlTrayCountBar.BackColor = cardSettings.CardColor;
            pnlTrayCountBar.Size = new Size(0, trayBarHeight);
            pnlTrayCountBar.Location = new Point(cardSettings.Width - innerPad, rowH + (rowH - trayBarHeight) / 2);
            pnlTrayCountBar.Anchor = AnchorStyles.Top | AnchorStyles.Right;
            BuildTrayCountSelector(itemCount);
            cardSettings.Controls.Add(pnlTrayCountBar);

            int listTop = (int)(108 * dpiScale);
            int listH = Math.Max((int)(120 * dpiScale), cardSettings.Top - listTop - (int)(14 * dpiScale));
            reorderControl = new PriorityReorderControl();
            reorderControl.BackColor = this.BackColor;
            reorderControl.DpiScale = dpiScale;
            reorderControl.ActiveTraySlotCount = multiTrayEnabled ? this.selectedTrayCount : 1;
            reorderControl.Location = new Point(pad, listTop);
            reorderControl.Size = new Size(contentW, listH);
            reorderControl.Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right;
            reorderControl.SwatchClicked += OnCardSwatchClicked;

            if (items != null)
            {
                for (int i = 0; i < items.Count; i++)
                {
                    if (items[i] != null) reorderControl.Items.Add(items[i]);
                }
                reorderControl.ResetPositions();
            }
            this.Controls.Add(reorderControl);
            this.modernChromeTitlePadLeftBase = 22;
            this.modernChromeUseIntegratedHeroHeader = true;
            AttachModernChrome("设备优先级与多托盘管理", null, 0, false, true);
            this.MinimumSize = new Size((int)(420 * dpiScale), (int)(480 * dpiScale));
            UpdateTrayCountBarState(false);
        }

        private static List<PriorityItem> BuildItemsFromDevices(List<MouseBatteryInfo> devices)
        {
            var result = new List<PriorityItem>();
            if (devices == null) return result;
            for (int i = 0; i < devices.Count; i++)
            {
                var d = devices[i];
                if (d == null) continue;
                result.Add(new PriorityItem
                {
                    DeviceId = d.DeviceId,
                    Name = d.DisplayName,
                    OriginalName = d.DeviceName,
                    Icon = d.CategoryIcon,
                    Category = d.Category,
                    Battery = d.BatteryPercent,
                    IsConnected = d.IsConnected,
                    IsSleeping = d.IsSleeping,
                    AccentColor = d.BrandColor,
                    DefaultBrandColor = d.DefaultBrandColor,
                    ThemeColorHex = d.CustomThemeColorHex ?? "",
                    TrayColorHex = d.CustomTrayColorHex ?? "",
                    LinkTrayColorToTheme = d.LinkTrayColorToTheme,
                    TrayAccentColor = d.TrayAccentColor,
                    PriorityIndex = i + 1,
                    IsCustom = d.IsCustom,
                    HardwareFingerprint = d.HardwareFingerprint
                });
            }
            return result;
        }

        private void BuildTrayCountSelector(int deviceCount)
        {
            pnlTrayCountBar.Controls.Clear();
            presetCountButtons.Clear();

            int maxPreset = Math.Min(Math.Max(deviceCount, 2), 5);
            if (this.selectedTrayCount > maxPreset && deviceCount <= 5)
            {
                this.selectedTrayCount = maxPreset;
            }

            bool showManualInput = (deviceCount > 5);
            int presetCount = maxPreset - 1;
            int trackPad = (int)(3 * dpiScale);
            int pillH = (int)(24 * dpiScale);
            int trackH = pillH + trackPad * 2;
            int trackW = showManualInput
                ? (trackPad * 2 + presetCount * (int)(36 * dpiScale) + (int)(50 * dpiScale) + presetCount * (int)(2 * dpiScale))
                : (trackPad * 2 + presetCount * (int)(42 * dpiScale) + Math.Max(0, presetCount - 1) * (int)(3 * dpiScale));
            trayBarHeight = trackH;
            trayBarExpandedW = trackW;

            var values = new int[presetCount];
            var labels = new string[presetCount];
            for (int i = 0; i < presetCount; i++)
            {
                int count = 2 + i;
                values[i] = count;
                labels[i] = count.ToString() + " 个";

                var shadowBtn = new ModernButton();
                shadowBtn.Text = labels[i];
                shadowBtn.Tag = count;
                presetCountButtons.Add(shadowBtn);
            }

            pnlSegmentTrack = new SlidingSegmentTrack();
            pnlSegmentTrack.DpiScale = dpiScale;
            pnlSegmentTrack.BackColor = cardSettings.CardColor;
            pnlSegmentTrack.Location = new Point(0, 0);
            pnlSegmentTrack.Size = new Size(trackW, trackH);
            pnlSegmentTrack.Anchor = AnchorStyles.Top | AnchorStyles.Left;
            pnlSegmentTrack.SetOptions(values, labels, this.selectedTrayCount, showManualInput, 2, 16);
            pnlSegmentTrack.SelectedValueChanged += (val) => {
                this.selectedTrayCount = val;
                SyncActiveTraySlotsToList();
            };
            pnlTrayCountBar.Controls.Add(pnlSegmentTrack);

            RefreshPresetButtonHighlights();
        }

        private void SyncActiveTraySlotsToList()
        {
            if (reorderControl == null) return;
            reorderControl.ActiveTraySlotCount = (chkMultiTray != null && chkMultiTray.Checked) ? this.selectedTrayCount : 1;
            reorderControl.Invalidate();
        }

        private void RefreshPresetButtonHighlights()
        {
            if (pnlSegmentTrack != null)
            {
                pnlSegmentTrack.SelectValue(this.selectedTrayCount, true);
            }
            SyncActiveTraySlotsToList();
        }

        private void ApplyTrayCountBarWidth(int curBarW)
        {
            if (pnlTrayCountBar == null || cardSettings == null) return;
            int innerPad = (int)(16 * dpiScale);
            int rowH = (int)(44 * dpiScale);
            int minLeft = innerPad + (int)(178 * dpiScale);
            int clampedW = Math.Max(0, Math.Min(trayBarExpandedW, curBarW));
            pnlTrayCountBar.SetBounds(
                Math.Max(minLeft, cardSettings.Width - innerPad - clampedW),
                rowH + Math.Max(2, (rowH - trayBarHeight) / 2),
                clampedW,
                trayBarHeight);
        }

        private void UpdateTrayCountBarState()
        {
            UpdateTrayCountBarState(true);
        }

        private void UpdateTrayCountBarState(bool animate)
        {
            bool enabled = (chkMultiTray != null && chkMultiTray.Checked);
            this.ResultMultiTrayEnabled = enabled;
            SyncActiveTraySlotsToList();
            if (pnlTrayCountBar == null) return;

            int targetW = enabled ? trayBarExpandedW : 0;
            if (!animate || !this.Visible || !this.IsHandleCreated)
            {
                if (trayBarAnimTimer != null && trayBarAnimTimer.Enabled) trayBarAnimTimer.Stop();
                pnlTrayCountBar.Visible = enabled;
                ApplyTrayCountBarWidth(targetW);
                return;
            }

            if (pnlTrayCountBar.Width == targetW && pnlTrayCountBar.Visible == enabled)
            {
                if (trayBarAnimTimer != null && trayBarAnimTimer.Enabled) trayBarAnimTimer.Stop();
                return;
            }

            if (enabled)
            {
                pnlTrayCountBar.Visible = true;
            }

            trayBarAnimStartW = pnlTrayCountBar.Width;
            trayBarAnimTargetW = targetW;
            trayBarAnimStartTime = DateTime.UtcNow;

            if (trayBarAnimTimer == null)
            {
                trayBarAnimTimer = new System.Windows.Forms.Timer();
                trayBarAnimTimer.Interval = 15;
                trayBarAnimTimer.Tick += (s, e) => {
                    if (pnlTrayCountBar == null || this.IsDisposed)
                    {
                        trayBarAnimTimer.Stop();
                        return;
                    }

                    double elapsed = (DateTime.UtcNow - trayBarAnimStartTime).TotalMilliseconds;
                    float progress = (float)(elapsed / TrayBarAnimDurationMs);
                    if (progress >= 1.0f)
                    {
                        trayBarAnimTimer.Stop();
                        ApplyTrayCountBarWidth(trayBarAnimTargetW);
                        if (trayBarAnimTargetW == 0)
                        {
                            pnlTrayCountBar.Visible = false;
                        }
                        if (cardSettings != null) cardSettings.Invalidate();
                        return;
                    }

                    float rem = 1.0f - progress;
                    float ease = 1.0f - (rem * rem * rem);
                    int curW = trayBarAnimStartW + (int)Math.Round((trayBarAnimTargetW - trayBarAnimStartW) * ease);
                    ApplyTrayCountBarWidth(curW);
                };
            }

            if (!trayBarAnimTimer.Enabled) trayBarAnimTimer.Start();
        }

        private void OnCardSwatchClicked(int itemIndex)
        {
            if (reorderControl == null || itemIndex < 0 || itemIndex >= reorderControl.Items.Count) return;
            var it = reorderControl.Items[itemIndex];
            if (it == null) return;

            Color defCol = it.DefaultBrandColor.IsEmpty ? it.AccentColor : it.DefaultBrandColor;
            using (var dlg = new DevicePersonalizeDialog(
                it.OriginalName ?? it.Name,
                it.Name,
                defCol,
                it.ThemeColorHex ?? "",
                it.TrayColorHex ?? "",
                it.LinkTrayColorToTheme,
                this.dpiScale))
            {
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    if (!string.IsNullOrEmpty(dlg.ResultCustomName))
                    {
                        it.Name = dlg.ResultCustomName;
                    }
                    else if (!string.IsNullOrEmpty(it.OriginalName))
                    {
                        it.Name = it.OriginalName;
                    }
                    it.ThemeColorHex = dlg.ResultThemeColorHex ?? "";
                    it.TrayColorHex = dlg.ResultTrayColorHex ?? "";
                    it.LinkTrayColorToTheme = dlg.ResultLinkTrayColorToTheme;

                    Color newAccent = defCol;
                    Color parsedTheme;
                    if (!string.IsNullOrEmpty(it.ThemeColorHex) && DeviceCapabilities.TryParseHexColor(it.ThemeColorHex, out parsedTheme))
                    {
                        newAccent = parsedTheme;
                    }
                    it.AccentColor = newAccent;

                    Color newTray = newAccent;
                    Color parsedTray;
                    if (!it.LinkTrayColorToTheme && !string.IsNullOrEmpty(it.TrayColorHex) && DeviceCapabilities.TryParseHexColor(it.TrayColorHex, out parsedTray))
                    {
                        newTray = parsedTray;
                    }
                    it.TrayAccentColor = newTray;

                    if (!string.IsNullOrEmpty(it.DeviceId))
                    {
                        editedAppearanceIds.Add(it.DeviceId);
                    }
                    reorderControl.Invalidate();
                }
            }
        }

        private void SaveChanges()
        {
            if (pnlSegmentTrack != null)
            {
                pnlSegmentTrack.CommitPendingCustomInput();
            }
            var ids = new List<string>();
            foreach (var it in reorderControl.Items)
            {
                ids.Add(it.DeviceId);
                if (!string.IsNullOrEmpty(it.DeviceId) && editedAppearanceIds.Contains(it.DeviceId))
                {
                    DeviceManager.SaveDeviceAppearance(it.DeviceId, it.Name, it.ThemeColorHex, it.TrayColorHex, it.LinkTrayColorToTheme);
                }
            }
            DeviceManager.SavePriorityList(ids);
            if (ids.Count > 0) DeviceManager.SetConfiguredPrimaryDeviceId(ids[0]);
            DeviceManager.SetTrayLowBatteryFirstEnabled(chkLowBatteryFirst != null && chkLowBatteryFirst.Checked);

            bool multiOn = (chkMultiTray != null && chkMultiTray.Checked);
            int maxCount = Math.Max(2, Math.Min(16, this.selectedTrayCount));
            this.ResultMultiTrayEnabled = multiOn;
            this.ResultMultiTrayMaxCount = maxCount;
            ConfigManager.SetMultiTrayEnabled(multiOn);
            ConfigManager.SetMultiTrayMaxCount(maxCount);
        }

        protected override void OnApplyDpiScale(float scale)
        {
            base.OnApplyDpiScale(scale);
            trayBarHeight = (int)(30 * scale);
            int pad = (int)(22 * scale);
            int footerTop = (pnlFooter != null) ? pnlFooter.Top : (this.ClientSize.Height - (int)(62 * scale));
            int settingsH = (int)(88 * scale);
            if (cardSettings != null)
            {
                cardSettings.Top = footerTop - settingsH - (int)(16 * scale);
                cardSettings.Height = settingsH;
            }
            if (reorderControl != null)
            {
                int listTop = (int)(108 * scale);
                int listBottomBound = (cardSettings != null) ? cardSettings.Top : (footerTop - (int)(16 * scale));
                reorderControl.Location = new Point(pad, listTop);
                reorderControl.Height = Math.Max((int)(120 * scale), listBottomBound - listTop - (int)(14 * scale));
            }
            if (pnlSegmentTrack != null)
            {
                pnlSegmentTrack.DpiScale = scale;
                trayBarExpandedW = pnlSegmentTrack.Width;
                pnlSegmentTrack.SnapThumbToSelected();
            }
            bool enabled = (chkMultiTray != null && chkMultiTray.Checked);
            ApplyTrayCountBarWidth(enabled ? trayBarExpandedW : 0);
            if (reorderControl != null)
            {
                reorderControl.DpiScale = scale;
                reorderControl.ResetPositions();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && trayBarAnimTimer != null)
            {
                try
                {
                    trayBarAnimTimer.Stop();
                    trayBarAnimTimer.Dispose();
                }
                catch { }
                trayBarAnimTimer = null;
            }
            base.Dispose(disposing);
        }
    }
}
