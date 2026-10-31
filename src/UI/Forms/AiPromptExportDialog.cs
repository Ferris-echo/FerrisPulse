// ============================================================================
// 模块名称: AiPromptExportDialog.cs
// 所属区域: UI/Forms (业务对话框层)
// 核心职责:
//   现代化暗黑风 AI 适配提示词导出中心，支持多选扫描到的设备、自定义商业型号与品类、
//   手动补充未识别设备（品牌、型号、品类），一键生成符合 FDAS v2.0 的批量逆向提示词。
// 依赖关系: Models.AiPromptDeviceItem, Forms.DpiAwareForm, Controls.ModernButton, Controls.ModernDarkDropdown
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、属性或绘图逻辑时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（❌ 严禁 $"..." 插值，必须使用 string.Format 或 + 拼接）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class AiPromptExportDialog : DpiAwareForm
    {
        private List<AiPromptDeviceItem> deviceItems;
        private AiPromptDeviceItem selectedItem;
        private int manualDeviceIndex = 1;

        // UI 控件
        private Label lblTitle;
        private Label lblSubtitle;
        private ModernButton btnSelectAll;
        private ModernButton btnDeselectAll;
        private ModernButton btnAddManual;
        private Panel pnlListContainer;
        private Panel pnlDetails;
        private Label lblDetailsTitle;
        private Label lblNameLabel;
        private TextBox txtCustomName;
        private Label lblCategoryLabel;
        private ModernDarkDropdown cboCategory;
        private Label lblBrandLabel;
        private TextBox txtBrand;
        private Label lblHardwareStatus;
        private ModernButton btnRemoveDevice;

        private Label lblCountStatus;
        private ModernButton btnCopyPrompt;
        private ModernButton btnCancel;

        private Dictionary<AiPromptDeviceItem, DeviceRowUi> rowUiMap;
        private bool isUpdatingUi = false;

        private readonly DeviceCategory[] availableCategories = new DeviceCategory[]
        {
            DeviceCategory.Mouse,
            DeviceCategory.Keyboard,
            DeviceCategory.Headset,
            DeviceCategory.Earbuds,
            DeviceCategory.Gamepad,
            DeviceCategory.Dongle,
            DeviceCategory.Generic
        };

        public string GeneratedPrompt { get; private set; }
        public List<AiPromptDeviceItem> DeviceItems { get { return deviceItems; } }

        private class DeviceRowUi
        {
            public Panel RowPanel;
            public ModernCheckBox CheckBox;
            public Label IconLabel;
            public Label NameLabel;
            public Label DescLabel;
        }

        public AiPromptExportDialog() : this(null, null, null, 1.0f)
        {
        }

        public AiPromptExportDialog(List<CustomDeviceConfig> scannedConfigs, float scale) : this(scannedConfigs, null, null, scale)
        {
        }

        public AiPromptExportDialog(List<CustomDeviceConfig> scannedConfigs, CustomDeviceConfig initialSelected, string initialUserTypedName, float scale) : base(scale)
        {
            this.deviceItems = new List<AiPromptDeviceItem>();
            this.rowUiMap = new Dictionary<AiPromptDeviceItem, DeviceRowUi>();
            this.GeneratedPrompt = "";

            InitializeDeviceList(scannedConfigs, initialSelected, initialUserTypedName);
            BuildFormUI();
            RebuildDeviceListUI();
            SelectDevice(this.deviceItems.Count > 0 ? this.deviceItems[0] : null);
        }

        private void InitializeDeviceList(List<CustomDeviceConfig> scannedConfigs, CustomDeviceConfig initialSelected, string initialUserTypedName)
        {
            if (scannedConfigs != null && scannedConfigs.Count > 0)
            {
                for (int i = 0; i < scannedConfigs.Count; i++)
                {
                    CustomDeviceConfig cfg = scannedConfigs[i];
                    if (cfg == null) continue;
                    AiPromptDeviceItem it = new AiPromptDeviceItem(cfg);
                    if (cfg == initialSelected && !string.IsNullOrEmpty(initialUserTypedName))
                    {
                        it.CustomName = initialUserTypedName.Trim();
                    }
                    deviceItems.Add(it);
                }
            }

            if (deviceItems.Count == 0)
            {
                AiPromptDeviceItem defaultManual = new AiPromptDeviceItem();
                defaultManual.IsChecked = true;
                defaultManual.IsManual = true;
                defaultManual.CustomName = !string.IsNullOrEmpty(initialUserTypedName) ? initialUserTypedName.Trim() : "我的外设 1";
                defaultManual.Category = DeviceCategory.Mouse;
                defaultManual.Brand = "";
                deviceItems.Add(defaultManual);
            }
        }

        private void BuildFormUI()
        {
            this.Text = "AI 适配提示词导出中心 (多设备)";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.BackColor = Color.FromArgb(18, 20, 26);
            this.ForeColor = Color.White;

            int clientW = (int)(680 * dpiScale);
            int clientH = (int)(540 * dpiScale);
            this.ClientSize = new Size(clientW, clientH);

            int pad = (int)(16 * dpiScale);

            // 1. 标题区
            lblTitle = new Label();
            lblTitle.Text = "⚡ AI 适配提示词导出中心";
            lblTitle.Font = new Font("Microsoft YaHei UI", 12F, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(240, 245, 255);
            lblTitle.Location = new Point(pad, (int)(12 * dpiScale));
            lblTitle.AutoSize = true;
            this.Controls.Add(lblTitle);

            lblSubtitle = new Label();
            lblSubtitle.Text = "勾选需要由外部 AI 批量适配的外设，可自由调整商业型号、分类并手动补充未识别设备。";
            lblSubtitle.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular);
            lblSubtitle.ForeColor = Color.FromArgb(145, 155, 175);
            lblSubtitle.Location = new Point(pad, (int)(38 * dpiScale));
            lblSubtitle.AutoSize = true;
            this.Controls.Add(lblSubtitle);

            // 2. 顶部操作栏
            int toolBtnH = (int)(26 * dpiScale);
            int toolBtnY = (int)(64 * dpiScale);

            btnSelectAll = new ModernButton();
            btnSelectAll.Text = "全选";
            btnSelectAll.Font = new Font("Microsoft YaHei UI", 8F);
            btnSelectAll.NormalColor = Color.FromArgb(32, 38, 50);
            btnSelectAll.HoverColor = Color.FromArgb(44, 52, 68);
            btnSelectAll.PressedColor = Color.FromArgb(26, 32, 42);
            btnSelectAll.CornerRadius = (int)(4 * dpiScale);
            btnSelectAll.Location = new Point(pad, toolBtnY);
            btnSelectAll.Size = new Size((int)(54 * dpiScale), toolBtnH);
            btnSelectAll.Click += (s, e) => { SetAllCheckedState(true); };
            this.Controls.Add(btnSelectAll);

            btnDeselectAll = new ModernButton();
            btnDeselectAll.Text = "清空";
            btnDeselectAll.Font = new Font("Microsoft YaHei UI", 8F);
            btnDeselectAll.NormalColor = Color.FromArgb(32, 38, 50);
            btnDeselectAll.HoverColor = Color.FromArgb(44, 52, 68);
            btnDeselectAll.PressedColor = Color.FromArgb(26, 32, 42);
            btnDeselectAll.CornerRadius = (int)(4 * dpiScale);
            btnDeselectAll.Location = new Point(pad + (int)(60 * dpiScale), toolBtnY);
            btnDeselectAll.Size = new Size((int)(54 * dpiScale), toolBtnH);
            btnDeselectAll.Click += (s, e) => { SetAllCheckedState(false); };
            this.Controls.Add(btnDeselectAll);

            btnAddManual = new ModernButton();
            btnAddManual.Text = "➕ 手动添加未识别外设";
            btnAddManual.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            btnAddManual.NormalColor = Color.FromArgb(28, 55, 80);
            btnAddManual.HoverColor = Color.FromArgb(38, 72, 106);
            btnAddManual.PressedColor = Color.FromArgb(22, 44, 66);
            btnAddManual.CornerRadius = (int)(4 * dpiScale);
            btnAddManual.Location = new Point(pad + (int)(122 * dpiScale), toolBtnY);
            btnAddManual.Size = new Size((int)(168 * dpiScale), toolBtnH);
            btnAddManual.Click += BtnAddManual_Click;
            this.Controls.Add(btnAddManual);

            // 3. 中部左侧：设备多选列表滚动容器
            int contentY = (int)(98 * dpiScale);
            int bottomH = (int)(54 * dpiScale);
            int contentH = clientH - contentY - bottomH;
            int listW = (int)(340 * dpiScale);

            pnlListContainer = new Panel();
            pnlListContainer.Location = new Point(pad, contentY);
            pnlListContainer.Size = new Size(listW, contentH);
            pnlListContainer.AutoScroll = true;
            pnlListContainer.BackColor = Color.FromArgb(14, 16, 21);
            this.Controls.Add(pnlListContainer);

            // 4. 中部右侧：设备属性编辑面板
            int detailsX = pad + listW + (int)(12 * dpiScale);
            int detailsW = clientW - detailsX - pad;

            pnlDetails = new Panel();
            pnlDetails.Location = new Point(detailsX, contentY);
            pnlDetails.Size = new Size(detailsW, contentH);
            pnlDetails.BackColor = Color.FromArgb(24, 27, 36);
            this.Controls.Add(pnlDetails);

            BuildDetailsPanel(detailsW, contentH);

            // 5. 底部操作栏
            int bottomY = clientH - bottomH + (int)(10 * dpiScale);

            lblCountStatus = new Label();
            lblCountStatus.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular);
            lblCountStatus.ForeColor = Color.FromArgb(160, 175, 200);
            lblCountStatus.Location = new Point(pad, bottomY + (int)(6 * dpiScale));
            lblCountStatus.AutoSize = true;
            this.Controls.Add(lblCountStatus);

            int btnH = (int)(34 * dpiScale);
            int btnCancelW = (int)(76 * dpiScale);
            int btnCopyW = (int)(224 * dpiScale);

            btnCancel = new ModernButton();
            btnCancel.Text = "取消";
            btnCancel.Font = new Font("Microsoft YaHei UI", 9F);
            btnCancel.NormalColor = Color.FromArgb(40, 46, 58);
            btnCancel.HoverColor = Color.FromArgb(50, 58, 72);
            btnCancel.PressedColor = Color.FromArgb(32, 38, 48);
            btnCancel.CornerRadius = (int)(6 * dpiScale);
            btnCancel.Location = new Point(clientW - pad - btnCopyW - (int)(10 * dpiScale) - btnCancelW, bottomY);
            btnCancel.Size = new Size(btnCancelW, btnH);
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnCancel);

            btnCopyPrompt = new ModernButton();
            btnCopyPrompt.Text = "📋 复制已选设备提示词";
            btnCopyPrompt.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            btnCopyPrompt.NormalColor = Color.FromArgb(0, 180, 85);
            btnCopyPrompt.HoverColor = Color.FromArgb(0, 210, 100);
            btnCopyPrompt.PressedColor = Color.FromArgb(0, 150, 70);
            btnCopyPrompt.CornerRadius = (int)(6 * dpiScale);
            btnCopyPrompt.Location = new Point(clientW - pad - btnCopyW, bottomY);
            btnCopyPrompt.Size = new Size(btnCopyW, btnH);
            btnCopyPrompt.Click += BtnCopyPrompt_Click;
            this.Controls.Add(btnCopyPrompt);
        }

        private void BuildDetailsPanel(int pnlW, int pnlH)
        {
            int dPad = (int)(14 * dpiScale);
            int curY = dPad;
            int innerW = pnlW - (dPad * 2);

            lblDetailsTitle = new Label();
            lblDetailsTitle.Text = "外设参数与属性配置";
            lblDetailsTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            lblDetailsTitle.ForeColor = Color.FromArgb(235, 240, 255);
            lblDetailsTitle.Location = new Point(dPad, curY);
            lblDetailsTitle.AutoSize = true;
            pnlDetails.Controls.Add(lblDetailsTitle);
            curY += (int)(28 * dpiScale);

            // 商业型号
            lblNameLabel = new Label();
            lblNameLabel.Text = "商业型号 (AI 识别基准):";
            lblNameLabel.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblNameLabel.ForeColor = Color.FromArgb(170, 180, 200);
            lblNameLabel.Location = new Point(dPad, curY);
            lblNameLabel.AutoSize = true;
            pnlDetails.Controls.Add(lblNameLabel);
            curY += (int)(20 * dpiScale);

            txtCustomName = new TextBox();
            txtCustomName.Font = new Font("Microsoft YaHei UI", 9.5F);
            txtCustomName.BackColor = Color.FromArgb(32, 36, 48);
            txtCustomName.ForeColor = Color.White;
            txtCustomName.BorderStyle = BorderStyle.FixedSingle;
            txtCustomName.Location = new Point(dPad, curY);
            txtCustomName.Size = new Size(innerW, (int)(24 * dpiScale));
            txtCustomName.TextChanged += (s, e) =>
            {
                if (isUpdatingUi || selectedItem == null) return;
                selectedItem.CustomName = txtCustomName.Text.Trim();
                UpdateRowDisplay(selectedItem);
            };
            pnlDetails.Controls.Add(txtCustomName);
            curY += (int)(32 * dpiScale);

            // 设备品类
            lblCategoryLabel = new Label();
            lblCategoryLabel.Text = "设备品类 (生成对应协议规范):";
            lblCategoryLabel.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblCategoryLabel.ForeColor = Color.FromArgb(170, 180, 200);
            lblCategoryLabel.Location = new Point(dPad, curY);
            lblCategoryLabel.AutoSize = true;
            pnlDetails.Controls.Add(lblCategoryLabel);
            curY += (int)(20 * dpiScale);

            cboCategory = new ModernDarkDropdown();
            cboCategory.DpiScale = dpiScale;
            cboCategory.Location = new Point(dPad, curY);
            cboCategory.Size = new Size(innerW, (int)(28 * dpiScale));
            for (int i = 0; i < availableCategories.Length; i++)
            {
                string icon = MouseBatteryInfo.GetCategoryIcon(availableCategories[i]);
                string name = MouseBatteryInfo.GetCategoryDisplayName(availableCategories[i]);
                cboCategory.Items.Add(string.Format("{0} {1}", icon, name));
            }
            cboCategory.SelectedIndexChanged += (s, e) =>
            {
                if (isUpdatingUi || selectedItem == null) return;
                if (cboCategory.SelectedIndex >= 0 && cboCategory.SelectedIndex < availableCategories.Length)
                {
                    selectedItem.Category = availableCategories[cboCategory.SelectedIndex];
                    UpdateRowDisplay(selectedItem);
                }
            };
            pnlDetails.Controls.Add(cboCategory);
            curY += (int)(36 * dpiScale);

            // 厂商 / 品牌
            lblBrandLabel = new Label();
            lblBrandLabel.Text = "厂商 / 品牌 (可选):";
            lblBrandLabel.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblBrandLabel.ForeColor = Color.FromArgb(170, 180, 200);
            lblBrandLabel.Location = new Point(dPad, curY);
            lblBrandLabel.AutoSize = true;
            pnlDetails.Controls.Add(lblBrandLabel);
            curY += (int)(20 * dpiScale);

            txtBrand = new TextBox();
            txtBrand.Font = new Font("Microsoft YaHei UI", 9.5F);
            txtBrand.BackColor = Color.FromArgb(32, 36, 48);
            txtBrand.ForeColor = Color.White;
            txtBrand.BorderStyle = BorderStyle.FixedSingle;
            txtBrand.Location = new Point(dPad, curY);
            txtBrand.Size = new Size(innerW, (int)(24 * dpiScale));
            txtBrand.TextChanged += (s, e) =>
            {
                if (isUpdatingUi || selectedItem == null) return;
                selectedItem.Brand = txtBrand.Text.Trim();
            };
            pnlDetails.Controls.Add(txtBrand);
            curY += (int)(34 * dpiScale);

            // 硬件拓扑与状态指引
            lblHardwareStatus = new Label();
            lblHardwareStatus.Font = new Font("Microsoft YaHei UI", 8F);
            lblHardwareStatus.ForeColor = Color.FromArgb(130, 145, 170);
            lblHardwareStatus.Location = new Point(dPad, curY);
            lblHardwareStatus.Size = new Size(innerW, (int)(68 * dpiScale));
            pnlDetails.Controls.Add(lblHardwareStatus);

            // 移除手动添加设备按钮
            btnRemoveDevice = new ModernButton();
            btnRemoveDevice.Text = "🗑️ 移除此设备";
            btnRemoveDevice.Font = new Font("Microsoft YaHei UI", 8.5F);
            btnRemoveDevice.NormalColor = Color.FromArgb(70, 30, 36);
            btnRemoveDevice.HoverColor = Color.FromArgb(92, 40, 48);
            btnRemoveDevice.PressedColor = Color.FromArgb(50, 20, 24);
            btnRemoveDevice.CornerRadius = (int)(4 * dpiScale);
            btnRemoveDevice.Location = new Point(dPad, pnlH - (int)(36 * dpiScale));
            btnRemoveDevice.Size = new Size(innerW, (int)(26 * dpiScale));
            btnRemoveDevice.Visible = false;
            btnRemoveDevice.Click += (s, e) =>
            {
                if (selectedItem != null && selectedItem.IsManual)
                {
                    deviceItems.Remove(selectedItem);
                    RebuildDeviceListUI();
                    SelectDevice(deviceItems.Count > 0 ? deviceItems[0] : null);
                }
            };
            pnlDetails.Controls.Add(btnRemoveDevice);
        }

        private void RebuildDeviceListUI()
        {
            pnlListContainer.SuspendLayout();
            pnlListContainer.Controls.Clear();
            rowUiMap.Clear();

            int rowH = (int)(48 * dpiScale);
            int rowSpacing = (int)(4 * dpiScale);
            int rowW = pnlListContainer.ClientSize.Width - (int)(6 * dpiScale);

            for (int i = 0; i < deviceItems.Count; i++)
            {
                AiPromptDeviceItem item = deviceItems[i];
                Panel rowPanel = new Panel();
                rowPanel.Location = new Point((int)(3 * dpiScale), i * (rowH + rowSpacing) + (int)(3 * dpiScale));
                rowPanel.Size = new Size(rowW, rowH);
                rowPanel.BackColor = (item == selectedItem) ? Color.FromArgb(36, 46, 68) : Color.FromArgb(24, 28, 38);
                rowPanel.Cursor = Cursors.Hand;

                // 勾选框
                ModernCheckBox chk = new ModernCheckBox();
                chk.Checked = item.IsChecked;
                chk.Location = new Point((int)(8 * dpiScale), (rowH - (int)(20 * dpiScale)) / 2);
                chk.Size = new Size((int)(20 * dpiScale), (int)(20 * dpiScale));
                chk.CheckedChanged += (s, e) =>
                {
                    item.IsChecked = chk.Checked;
                    UpdateSelectionCount();
                };
                rowPanel.Controls.Add(chk);

                // 图标
                Label lblIcon = new Label();
                lblIcon.Text = MouseBatteryInfo.GetCategoryIcon(item.Category);
                lblIcon.Font = new Font("Segoe UI Emoji", 11F);
                lblIcon.Location = new Point((int)(32 * dpiScale), (rowH - (int)(22 * dpiScale)) / 2);
                lblIcon.Size = new Size((int)(24 * dpiScale), (int)(22 * dpiScale));
                lblIcon.BackColor = Color.Transparent;
                lblIcon.Cursor = Cursors.Hand;
                rowPanel.Controls.Add(lblIcon);

                // 名称
                Label lblName = new Label();
                lblName.Text = !string.IsNullOrEmpty(item.CustomName) ? item.CustomName : "未知外设";
                lblName.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
                lblName.ForeColor = Color.White;
                lblName.Location = new Point((int)(60 * dpiScale), (int)(6 * dpiScale));
                lblName.Size = new Size(rowW - (int)(66 * dpiScale), (int)(18 * dpiScale));
                lblName.BackColor = Color.Transparent;
                lblName.Cursor = Cursors.Hand;
                rowPanel.Controls.Add(lblName);

                // 描述与分类
                Label lblDesc = new Label();
                string statusText = item.IsManual ? "手动添加" : "探针已捕获";
                lblDesc.Text = string.Format("{0} · {1}", MouseBatteryInfo.GetCategoryDisplayName(item.Category), statusText);
                lblDesc.Font = new Font("Microsoft YaHei UI", 7.5F);
                lblDesc.ForeColor = item.IsManual ? Color.FromArgb(255, 180, 80) : Color.FromArgb(120, 170, 220);
                lblDesc.Location = new Point((int)(60 * dpiScale), (int)(25 * dpiScale));
                lblDesc.Size = new Size(rowW - (int)(66 * dpiScale), (int)(16 * dpiScale));
                lblDesc.BackColor = Color.Transparent;
                lblDesc.Cursor = Cursors.Hand;
                rowPanel.Controls.Add(lblDesc);

                // 单击选中
                EventHandler onRowClick = (s, e) =>
                {
                    SelectDevice(item);
                };
                rowPanel.Click += onRowClick;
                lblIcon.Click += onRowClick;
                lblName.Click += onRowClick;
                lblDesc.Click += onRowClick;

                DeviceRowUi ui = new DeviceRowUi();
                ui.RowPanel = rowPanel;
                ui.CheckBox = chk;
                ui.IconLabel = lblIcon;
                ui.NameLabel = lblName;
                ui.DescLabel = lblDesc;
                rowUiMap[item] = ui;

                pnlListContainer.Controls.Add(rowPanel);
            }

            pnlListContainer.ResumeLayout(true);
            UpdateSelectionCount();
        }

        private void SelectDevice(AiPromptDeviceItem item)
        {
            this.selectedItem = item;

            // 更新行高亮
            foreach (KeyValuePair<AiPromptDeviceItem, DeviceRowUi> kv in rowUiMap)
            {
                if (kv.Value != null && kv.Value.RowPanel != null)
                {
                    kv.Value.RowPanel.BackColor = (kv.Key == selectedItem) ? Color.FromArgb(36, 46, 68) : Color.FromArgb(24, 28, 38);
                }
            }

            if (selectedItem == null)
            {
                pnlDetails.Enabled = false;
                return;
            }

            pnlDetails.Enabled = true;
            isUpdatingUi = true;
            try
            {
                txtCustomName.Text = selectedItem.CustomName ?? "";
                txtBrand.Text = selectedItem.Brand ?? "";

                int catIdx = 0;
                for (int i = 0; i < availableCategories.Length; i++)
                {
                    if (availableCategories[i] == selectedItem.Category)
                    {
                        catIdx = i;
                        break;
                    }
                }
                cboCategory.SelectedIndex = catIdx;

                if (selectedItem.IsManual)
                {
                    lblHardwareStatus.Text = "⚠️ 状态：手动补充设备\n当前无底层物理探针指纹。导出的提示词将显式标注该设备，指导外部 Agent 优先引导您打开官方网页驱动以反查报文。";
                    lblHardwareStatus.ForeColor = Color.FromArgb(255, 185, 80);
                    btnRemoveDevice.Visible = true;
                }
                else
                {
                    int vid = 0;
                    int pid = 0;
                    if (selectedItem.ScannedConfig != null)
                    {
                        string id = !string.IsNullOrEmpty(selectedItem.ScannedConfig.HardwareFingerprint) ? selectedItem.ScannedConfig.HardwareFingerprint : selectedItem.ScannedConfig.DeviceId;
                        AddDeviceWizardForm.TryExtractVidPid(id, out vid, out pid);
                    }
                    string tp = (selectedItem.ScannedConfig != null && !string.IsNullOrEmpty(selectedItem.ScannedConfig.Transport)) ? selectedItem.ScannedConfig.Transport : "USB/2.4G";
                    if (vid != 0 || pid != 0)
                    {
                        lblHardwareStatus.Text = string.Format("✅ 状态：硬件探针已捕获物理指纹\nVID: 0x{0:X4}, PID: 0x{1:X4}, 物理通道: {2}\n已捕获完整 HID 描述符端点，Agent 可精准匹配！", vid, pid, tp);
                    }
                    else
                    {
                        lblHardwareStatus.Text = string.Format("✅ 状态：硬件探针已捕获设备\n物理通道: {0}，Agent 可匹配通信拓扑！", tp);
                    }
                    lblHardwareStatus.ForeColor = Color.FromArgb(0, 220, 110);
                    btnRemoveDevice.Visible = false;
                }
            }
            finally
            {
                isUpdatingUi = false;
            }
        }

        private void UpdateRowDisplay(AiPromptDeviceItem item)
        {
            if (item == null || !rowUiMap.ContainsKey(item)) return;
            DeviceRowUi ui = rowUiMap[item];
            if (ui == null) return;

            if (ui.NameLabel != null)
            {
                ui.NameLabel.Text = !string.IsNullOrEmpty(item.CustomName) ? item.CustomName : "未知外设";
            }
            if (ui.IconLabel != null)
            {
                ui.IconLabel.Text = MouseBatteryInfo.GetCategoryIcon(item.Category);
            }
            if (ui.DescLabel != null)
            {
                string statusText = item.IsManual ? "手动添加" : "探针已捕获";
                ui.DescLabel.Text = string.Format("{0} · {1}", MouseBatteryInfo.GetCategoryDisplayName(item.Category), statusText);
                ui.DescLabel.ForeColor = item.IsManual ? Color.FromArgb(255, 180, 80) : Color.FromArgb(120, 170, 220);
            }
        }

        private void SetAllCheckedState(bool check)
        {
            for (int i = 0; i < deviceItems.Count; i++)
            {
                deviceItems[i].IsChecked = check;
                if (rowUiMap.ContainsKey(deviceItems[i]) && rowUiMap[deviceItems[i]].CheckBox != null)
                {
                    rowUiMap[deviceItems[i]].CheckBox.Checked = check;
                }
            }
            UpdateSelectionCount();
        }

        private void UpdateSelectionCount()
        {
            int checkedCount = 0;
            for (int i = 0; i < deviceItems.Count; i++)
            {
                if (deviceItems[i].IsChecked) checkedCount++;
            }

            lblCountStatus.Text = string.Format("已勾选 {0} / {1} 款外设", checkedCount, deviceItems.Count);

            if (checkedCount > 1)
            {
                btnCopyPrompt.Text = string.Format("📋 复制已选 ({0}) 款设备提示词", checkedCount);
                btnCopyPrompt.NormalColor = Color.FromArgb(0, 180, 85);
            }
            else if (checkedCount == 1)
            {
                btnCopyPrompt.Text = "📋 复制已选 (1) 款设备提示词";
                btnCopyPrompt.NormalColor = Color.FromArgb(0, 180, 85);
            }
            else
            {
                btnCopyPrompt.Text = "📋 请勾选至少一款设备";
                btnCopyPrompt.NormalColor = Color.FromArgb(60, 68, 80);
            }
        }

        private void BtnAddManual_Click(object sender, EventArgs e)
        {
            AiPromptDeviceItem newItem = new AiPromptDeviceItem();
            newItem.IsChecked = true;
            newItem.IsManual = true;
            newItem.CustomName = string.Format("未识别新外设 {0}", manualDeviceIndex++);
            newItem.Category = DeviceCategory.Mouse;
            newItem.Brand = "";
            deviceItems.Add(newItem);

            RebuildDeviceListUI();
            SelectDevice(newItem);

            if (txtCustomName != null)
            {
                txtCustomName.Focus();
                txtCustomName.SelectAll();
            }
        }

        private void BtnCopyPrompt_Click(object sender, EventArgs e)
        {
            List<AiPromptDeviceItem> checkedList = new List<AiPromptDeviceItem>();
            for (int i = 0; i < deviceItems.Count; i++)
            {
                if (deviceItems[i].IsChecked) checkedList.Add(deviceItems[i]);
            }

            if (checkedList.Count == 0)
            {
                MessageBox.Show("请至少勾选一款需要导出 AI 适配提示词的外设！", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            try
            {
                this.GeneratedPrompt = AddDeviceWizardForm.GetMultiDeviceAiAdaptationPrompt(this.deviceItems);
                if (!string.IsNullOrEmpty(this.GeneratedPrompt))
                {
                    Clipboard.SetText(this.GeneratedPrompt);
                }
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("生成或复制提示词时出错: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
