// ============================================================================
// 模块名称: DevicePriorityForm.cs
// 所属区域: UI/Forms (业务对话框层)
// 核心职责:
//   多外设托盘图标展示优先级调整窗口，承载 PriorityReorderControl，提供保存排序、重置、以及持久化至 ConfigManager 的完整交互。
// 依赖关系: Controls.PriorityReorderControl, Controls.ModernButton, Core.ConfigManager
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
        private ModernCheckBox chkLowBatteryFirst;
        private ModernCheckBox chkMultiTray;
        private Panel pnlTrayCountBar;
        private readonly List<ModernButton> presetCountButtons = new List<ModernButton>();
        private NumericUpDown numCustomCount;
        private Label lblCustomCountHint;
        private ModernButton btnSave;
        private ModernButton btnCancel;
        private int selectedTrayCount = 3;
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
            int initClientW = (int)(456 * dpiScale);
            int initClientH = (int)(558 * dpiScale);
            this.ClientSize = new Size(initClientW, initClientH);
            this.BackColor = Color.FromArgb(18, 19, 23);
            this.ForeColor = Color.White;
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.Sizable;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            int pad = (int)(16 * dpiScale);

            var lblTitle = new Label();
            lblTitle.Text = "设备优先级与多托盘管理";
            lblTitle.Font = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(240, 245, 255);
            lblTitle.Location = new Point(pad, (int)(12 * dpiScale));
            lblTitle.AutoSize = true;
            this.Controls.Add(lblTitle);

            var lblTip = new Label();
            lblTip.Text = "拖动右侧把手调整顺位；\n点击卡片右侧色点可单独定制设备名称、界面色与托盘图标色。";
            lblTip.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblTip.ForeColor = Color.FromArgb(145, 155, 175);
            lblTip.Location = new Point(pad, (int)(38 * dpiScale));
            lblTip.Size = new Size(initClientW - (pad * 2), (int)(34 * dpiScale));
            lblTip.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            this.Controls.Add(lblTip);

            int chkX = pad + (int)(6 * dpiScale);
            chkLowBatteryFirst = new ModernCheckBox();
            chkLowBatteryFirst.Text = "低电量设备优先显示（电量≤30%）";
            chkLowBatteryFirst.Font = new Font("Microsoft YaHei UI", 9.0F, FontStyle.Regular, GraphicsUnit.Point);
            chkLowBatteryFirst.ForeColor = Color.FromArgb(220, 230, 245);
            chkLowBatteryFirst.Checked = lowBatteryFirst;
            chkLowBatteryFirst.Location = new Point(chkX, (int)(74 * dpiScale));
            chkLowBatteryFirst.Size = new Size(initClientW - pad - chkX, (int)(24 * dpiScale));
            chkLowBatteryFirst.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            this.Controls.Add(chkLowBatteryFirst);

            chkMultiTray = new ModernCheckBox();
            chkMultiTray.Text = "启用状态栏多设备独立图标（每台设备单独占位，重连/休眠不跳位）";
            chkMultiTray.Font = new Font("Microsoft YaHei UI", 9.0F, FontStyle.Regular, GraphicsUnit.Point);
            chkMultiTray.ForeColor = Color.FromArgb(220, 230, 245);
            chkMultiTray.Checked = multiTrayEnabled;
            chkMultiTray.Location = new Point(chkX, (int)(100 * dpiScale));
            chkMultiTray.Size = new Size(initClientW - pad - chkX, (int)(24 * dpiScale));
            chkMultiTray.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            chkMultiTray.CheckedChanged += (s, e) => UpdateTrayCountBarState();
            this.Controls.Add(chkMultiTray);

            // 自适应图标数量选择条：已知设备数 <= 5 仅显示预设档位（无输入框）；> 5 时出现自定义输入
            pnlTrayCountBar = new Panel();
            pnlTrayCountBar.Location = new Point(pad, (int)(128 * dpiScale));
            pnlTrayCountBar.Size = new Size(initClientW - (pad * 2), (int)(30 * dpiScale));
            pnlTrayCountBar.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            BuildTrayCountSelector(itemCount);
            this.Controls.Add(pnlTrayCountBar);

            int btnW = (int)(110 * dpiScale);
            int btnH = (int)(34 * dpiScale);
            int bottomPad = (int)(16 * dpiScale);
            int btnY = initClientH - bottomPad - btnH;

            btnCancel = new ModernButton();
            btnCancel.Text = "取消";
            btnCancel.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
            btnCancel.NormalColor = Color.FromArgb(36, 39, 49);
            btnCancel.HoverColor = Color.FromArgb(48, 52, 65);
            btnCancel.PressedColor = Color.FromArgb(28, 30, 38);
            btnCancel.BorderColor = Color.FromArgb(58, 63, 78);
            btnCancel.ForeColor = Color.FromArgb(230, 235, 245);
            btnCancel.CornerRadius = (int)(8 * dpiScale);
            btnCancel.Location = new Point(initClientW - pad - (btnW * 2) - (int)(10 * dpiScale), btnY);
            btnCancel.Size = new Size(btnW, btnH);
            btnCancel.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnCancel);

            btnSave = new ModernButton();
            btnSave.Text = "确定并保存";
            btnSave.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
            btnSave.NormalColor = Color.FromArgb(0, 200, 83);
            btnSave.HoverColor = Color.FromArgb(0, 230, 118);
            btnSave.PressedColor = Color.FromArgb(0, 160, 65);
            btnSave.ForeColor = Color.FromArgb(10, 24, 15);
            btnSave.CornerRadius = (int)(8 * dpiScale);
            btnSave.Location = new Point(initClientW - pad - btnW, btnY);
            btnSave.Size = new Size(btnW, btnH);
            btnSave.Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            btnSave.Click += (s, e) => {
                SaveChanges();
                this.DialogResult = DialogResult.OK;
                this.Close();
            };
            this.Controls.Add(btnSave);

            int listTop = (int)(164 * dpiScale);
            int listH = btnY - listTop - (int)(14 * dpiScale);
            reorderControl = new PriorityReorderControl();
            reorderControl.DpiScale = dpiScale;
            reorderControl.Location = new Point(pad, listTop);
            reorderControl.Size = new Size(initClientW - (pad * 2), listH);
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
            this.MinimumSize = new Size((int)(400 * dpiScale), (int)(440 * dpiScale));
            UpdateTrayCountBarState();
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

            var lblCount = new Label();
            lblCount.Text = "同时显示数量:";
            lblCount.Font = new Font("Microsoft YaHei UI", 8.8F);
            lblCount.ForeColor = Color.FromArgb(180, 190, 210);
            lblCount.Location = new Point((int)(6 * dpiScale), (int)(5 * dpiScale));
            lblCount.AutoSize = true;
            pnlTrayCountBar.Controls.Add(lblCount);

            int maxPreset = Math.Min(Math.Max(deviceCount, 2), 5);
            if (this.selectedTrayCount > maxPreset && deviceCount <= 5)
            {
                this.selectedTrayCount = maxPreset;
            }

            int x = (int)(100 * dpiScale);
            int btnW = (int)(36 * dpiScale);
            int btnH = (int)(24 * dpiScale);
            int gap = (int)(6 * dpiScale);

            for (int count = 2; count <= maxPreset; count++)
            {
                int capCount = count;
                var btn = new ModernButton();
                btn.Text = count.ToString() + " 个";
                btn.Tag = count;
                btn.Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Bold);
                btn.CornerRadius = (int)(6 * dpiScale);
                btn.Location = new Point(x, (int)(2 * dpiScale));
                btn.Size = new Size(btnW + (int)(6 * dpiScale), btnH);
                btn.Click += (s, e) => {
                    this.selectedTrayCount = capCount;
                    if (numCustomCount != null && numCustomCount.Visible)
                    {
                        numCustomCount.Value = Math.Max(numCustomCount.Minimum, Math.Min(numCustomCount.Maximum, capCount));
                    }
                    RefreshPresetButtonHighlights();
                };
                presetCountButtons.Add(btn);
                pnlTrayCountBar.Controls.Add(btn);
                x += btn.Width + gap;
            }

            // 仅当设备总数 > 5 时才显示手动数字输入（<=5 设备绝不多添输入步骤）
            bool showManualInput = (deviceCount > 5);
            lblCustomCountHint = new Label();
            lblCustomCountHint.Text = "自定义:";
            lblCustomCountHint.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblCustomCountHint.ForeColor = Color.FromArgb(170, 180, 200);
            lblCustomCountHint.Location = new Point(x + (int)(4 * dpiScale), (int)(5 * dpiScale));
            lblCustomCountHint.AutoSize = true;
            lblCustomCountHint.Visible = showManualInput;
            pnlTrayCountBar.Controls.Add(lblCustomCountHint);

            numCustomCount = new NumericUpDown();
            numCustomCount.Minimum = 2;
            numCustomCount.Maximum = 16;
            numCustomCount.Value = Math.Max(2, Math.Min(16, this.selectedTrayCount));
            numCustomCount.BackColor = Color.FromArgb(30, 33, 42);
            numCustomCount.ForeColor = Color.White;
            numCustomCount.BorderStyle = BorderStyle.FixedSingle;
            numCustomCount.Font = new Font("Microsoft YaHei UI", 9.0F, FontStyle.Bold);
            numCustomCount.Location = new Point(x + (int)(54 * dpiScale), (int)(2 * dpiScale));
            numCustomCount.Size = new Size((int)(52 * dpiScale), btnH);
            numCustomCount.Visible = showManualInput;
            numCustomCount.ValueChanged += (s, e) => {
                if (numCustomCount.Visible)
                {
                    this.selectedTrayCount = (int)numCustomCount.Value;
                    RefreshPresetButtonHighlights();
                }
            };
            pnlTrayCountBar.Controls.Add(numCustomCount);

            RefreshPresetButtonHighlights();
        }

        private void RefreshPresetButtonHighlights()
        {
            for (int i = 0; i < presetCountButtons.Count; i++)
            {
                var btn = presetCountButtons[i];
                int val = (btn.Tag is int) ? (int)btn.Tag : 0;
                bool active = (val == this.selectedTrayCount);
                btn.NormalColor = active ? Color.FromArgb(0, 200, 83) : Color.FromArgb(34, 37, 47);
                btn.HoverColor = active ? Color.FromArgb(0, 230, 118) : Color.FromArgb(46, 50, 62);
                btn.PressedColor = active ? Color.FromArgb(0, 160, 65) : Color.FromArgb(26, 28, 36);
                btn.ForeColor = active ? Color.FromArgb(10, 24, 15) : Color.FromArgb(215, 225, 240);
                btn.BorderColor = active ? Color.FromArgb(0, 230, 118) : Color.FromArgb(56, 62, 76);
                btn.Invalidate();
            }
        }

        private void UpdateTrayCountBarState()
        {
            bool enabled = (chkMultiTray != null && chkMultiTray.Checked);
            this.ResultMultiTrayEnabled = enabled;
            if (pnlTrayCountBar != null)
            {
                pnlTrayCountBar.Enabled = enabled;
            }
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
            if (reorderControl != null)
            {
                reorderControl.DpiScale = scale;
                reorderControl.ResetPositions();
            }
        }
    }
}
