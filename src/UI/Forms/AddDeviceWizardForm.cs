// ============================================================================
// 模块名称: AddDeviceWizardForm.cs
// 所属区域: UI/Forms (业务对话框层)
// 核心职责:
//   新外设即插即用添加向导窗口，包含全系统 HID 原始设备嗅探探测、实时读写报文连通性校验、自定义设备别名与品类归类（DPI 自适应）。
// 依赖关系: Controls.*, Hardware.DeviceManager, Core.ConfigManager, Models.CustomDeviceConfig
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
using System.Drawing.Text;
using System.Threading;
using System.Text;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class AddDeviceWizardForm : DpiAwareForm
    {
        public string AddedDeviceId { get; private set; }

        private ListBox lstDevices;
        private ModernInputBox txtCustomName;
        private ModernDarkDropdown cmbCategory;
        private Label lblTestStatus;
        private ModernButton btnScan;
        private ModernButton btnSave;
        private ModernButton btnCancel;
        private ModernButton btnCopyPrompt;
        private ModernButton btnImportAdapter;
        private ModernButton btnAiTools;
        private WizardRadarBadge radarBadge;
        private ScanningBeamControl beamControl;
        private System.Windows.Forms.Timer autoScanDebounceTimer;
        private System.Windows.Forms.Timer copyFeedbackTimer;
        private bool isScanning = false;
        private const int WM_DEVICECHANGE = 0x0219;
        private List<CustomDeviceConfig> scannedList = new List<CustomDeviceConfig>();
        private HashSet<string> existingIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);
            if (m.Msg == WM_DEVICECHANGE)
            {
                if (autoScanDebounceTimer == null)
                {
                    autoScanDebounceTimer = new System.Windows.Forms.Timer();
                    autoScanDebounceTimer.Interval = 600;
                    autoScanDebounceTimer.Tick += (s, ev) => {
                        autoScanDebounceTimer.Stop();
                        PerformScanAsync();
                    };
                }
                autoScanDebounceTimer.Stop();
                autoScanDebounceTimer.Start();
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (autoScanDebounceTimer != null)
            {
                autoScanDebounceTimer.Stop();
                autoScanDebounceTimer.Dispose();
                autoScanDebounceTimer = null;
            }
            if (copyFeedbackTimer != null)
            {
                copyFeedbackTimer.Stop();
                copyFeedbackTimer.Dispose();
                copyFeedbackTimer = null;
            }
        }

        public AddDeviceWizardForm() : this(null, 1.0f) { }

        public AddDeviceWizardForm(List<MouseBatteryInfo> existing) : this(existing, 1.0f) { }

        public AddDeviceWizardForm(float scale) : this(null, scale) { }

        public AddDeviceWizardForm(List<MouseBatteryInfo> existing, float scale) : base(scale)
        {
            if (existing != null)
            {
                foreach (var d in existing)
                {
                    if (DeviceManager.IsDeviceHidden(d.DeviceId)) continue;
                    if (!string.IsNullOrEmpty(d.HardwareFingerprint) && DeviceManager.IsDeviceHidden(d.HardwareFingerprint)) continue;

                    existingIds.Add(d.DeviceId);
                    var v1 = DeviceManager.GetDeviceIdVariants(d.DeviceId);
                    for (int i = 0; i < v1.Count; i++) existingIds.Add(v1[i]);

                    if (!string.IsNullOrEmpty(d.HardwareFingerprint))
                    {
                        existingIds.Add(d.HardwareFingerprint);
                        var v2 = DeviceManager.GetDeviceIdVariants(d.HardwareFingerprint);
                        for (int i = 0; i < v2.Count; i++) existingIds.Add(v2[i]);
                    }
                }
            }

            this.Text = "添加与配置外设向导";
            this.ClientSize = new Size((int)(430 * dpiScale), (int)(600 * dpiScale));
            this.BackColor = Color.FromArgb(18, 19, 23);
            this.ForeColor = Color.White;
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            int pad = (int)(16 * dpiScale);

            // Header Radar Badge
            radarBadge = new WizardRadarBadge();
            radarBadge.DpiScale = dpiScale;
            radarBadge.Size = new Size((int)(24 * dpiScale), (int)(24 * dpiScale));
            radarBadge.Location = new Point(pad, (int)(13 * dpiScale));
            this.Controls.Add(radarBadge);

            // Row 1: Title on left, Scan button on right
            var lblTitle = new Label();
            lblTitle.Text = "添加与配置外设向导";
            lblTitle.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(240, 245, 255);
            lblTitle.Location = new Point(pad + (int)(28 * dpiScale), (int)(15 * dpiScale));
            lblTitle.AutoSize = true;
            this.Controls.Add(lblTitle);

            btnScan = new ModernButton();
            btnScan.Text = "🔄 重新扫描";
            btnScan.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold);
            btnScan.NormalColor = Color.FromArgb(32, 36, 48);
            btnScan.HoverColor = Color.FromArgb(46, 52, 68);
            btnScan.PressedColor = Color.FromArgb(24, 28, 38);
            btnScan.BorderColor = Color.FromArgb(60, 68, 88);
            btnScan.TextColor = Color.FromArgb(220, 230, 250);
            int scanW = (int)(100 * dpiScale);
            btnScan.Location = new Point(this.ClientSize.Width - pad - scanW, (int)(12 * dpiScale));
            btnScan.Size = new Size(scanW, (int)(28 * dpiScale));
            btnScan.Click += (s, e) => PerformScanAsync();
            this.Controls.Add(btnScan);

            // Row 2: Subtitle
            var lblSub = new Label();
            lblSub.Text = "自动扫描当前在线外设，支持修改昵称与设备分类：";
            lblSub.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblSub.ForeColor = Color.FromArgb(145, 155, 175);
            lblSub.Location = new Point(pad, (int)(46 * dpiScale));
            lblSub.Size = new Size(this.ClientSize.Width - (pad * 2), (int)(20 * dpiScale));
            this.Controls.Add(lblSub);

            // Scanning Laser Beam Animation Bar
            beamControl = new ScanningBeamControl();
            beamControl.Location = new Point(pad, (int)(68 * dpiScale));
            beamControl.Size = new Size(this.ClientSize.Width - (pad * 2), (int)(3 * dpiScale));
            beamControl.Visible = false;
            this.Controls.Add(beamControl);

            // Row 3: Scanned Devices List (Owner Drawn Dark Cards)
            lstDevices = new ListBox();
            lstDevices.BackColor = Color.FromArgb(18, 19, 23);
            lstDevices.ForeColor = Color.FromArgb(235, 240, 250);
            lstDevices.BorderStyle = BorderStyle.None;
            lstDevices.Font = new Font("Microsoft YaHei UI", 9.2F);
            lstDevices.DrawMode = DrawMode.OwnerDrawFixed;
            lstDevices.ItemHeight = (int)(52 * dpiScale);
            lstDevices.Location = new Point(pad, (int)(72 * dpiScale));
            lstDevices.Size = new Size(this.ClientSize.Width - (pad * 2), (int)(212 * dpiScale));
            lstDevices.DrawItem += LstDevices_DrawItem;
            lstDevices.SelectedIndexChanged += (s, e) => OnDeviceSelected();
            lstDevices.HandleCreated += (s, e) => {
                try { ModernThemeHelper.SetWindowTheme(lstDevices.Handle, "DarkMode_Explorer", null); } catch { }
            };
            this.Controls.Add(lstDevices);

            // Row 4: Custom Nickname (整体下移，为上方列表腾出展示 4 个设备的完整空间)
            int formY = (int)(294 * dpiScale);
            var lblName = new Label();
            lblName.Text = "自定义昵称:";
            lblName.Font = new Font("Microsoft YaHei UI", 8.8F);
            lblName.ForeColor = Color.FromArgb(170, 180, 200);
            lblName.Location = new Point(pad, formY + 5);
            lblName.AutoSize = true;
            this.Controls.Add(lblName);

            txtCustomName = new ModernInputBox();
            txtCustomName.DpiScale = dpiScale;
            txtCustomName.Location = new Point(pad + (int)(95 * dpiScale), formY);
            txtCustomName.Size = new Size(this.ClientSize.Width - pad * 2 - (int)(95 * dpiScale), (int)(30 * dpiScale));
            this.Controls.Add(txtCustomName);

            // Row 5: Category Dropdown (ModernDarkDropdown)
            formY += (int)(35 * dpiScale);
            var lblCat = new Label();
            lblCat.Text = "外设类别:";
            lblCat.Font = new Font("Microsoft YaHei UI", 8.8F);
            lblCat.ForeColor = Color.FromArgb(170, 180, 200);
            lblCat.Location = new Point(pad, formY + 5);
            lblCat.AutoSize = true;
            this.Controls.Add(lblCat);

            cmbCategory = new ModernDarkDropdown();
            cmbCategory.DpiScale = dpiScale;
            cmbCategory.Items.Add("🖱️ 鼠标 (Mouse)");
            cmbCategory.Items.Add("⌨️ 键盘 (Keyboard)");
            cmbCategory.Items.Add("🎧 头戴式耳机 (Headset)");
            cmbCategory.Items.Add("🦻 入耳式耳机 (Earbuds)");
            cmbCategory.Items.Add("🎮 游戏手柄 (Gamepad)");
            cmbCategory.Items.Add("📡 接收器 / 拓展坞 (Dongle)");
            cmbCategory.Items.Add("🔌 通用外设 (Generic)");
            cmbCategory.SelectedIndex = 0;
            cmbCategory.Location = new Point(pad + (int)(95 * dpiScale), formY);
            cmbCategory.Size = new Size(this.ClientSize.Width - pad * 2 - (int)(95 * dpiScale), (int)(30 * dpiScale));
            this.Controls.Add(cmbCategory);

            // Row 6: Live Status Box
            formY += (int)(37 * dpiScale);
            lblTestStatus = new Label();
            lblTestStatus.Text = "请从上方列表选择一个设备";
            lblTestStatus.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold);
            lblTestStatus.ForeColor = Color.FromArgb(0, 230, 118);
            lblTestStatus.Location = new Point(pad, formY);
            lblTestStatus.Size = new Size(this.ClientSize.Width - (pad * 2), (int)(36 * dpiScale));
            this.Controls.Add(lblTestStatus);

            // Row 7: AI Dedicated Card (Self Developer Hub - 紧凑双行布局)
            int cardW = this.ClientSize.Width - (pad * 2);
            int cardH = (int)(122 * dpiScale);
            Panel pnlAiAdapter = new Panel();
            pnlAiAdapter.Location = new Point(pad, (int)(412 * dpiScale));
            pnlAiAdapter.Size = new Size(cardW, cardH);
            pnlAiAdapter.BackColor = Color.FromArgb(22, 25, 34);
            pnlAiAdapter.Paint += (s, e) =>
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                Rectangle rect = new Rectangle(0, 0, pnlAiAdapter.Width - 1, pnlAiAdapter.Height - 1);
                using (GraphicsPath path = RoundedCard.GetRoundedRectangle(rect, (int)(7 * dpiScale)))
                {
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(22, 25, 34)))
                    {
                        g.FillPath(bg, path);
                    }
                    using (Pen borderPen = new Pen(Color.FromArgb(46, 54, 72), 1f))
                    {
                        g.DrawPath(borderPen, path);
                    }
                }
            };
            this.Controls.Add(pnlAiAdapter);

            int cPad = (int)(12 * dpiScale);

            Label lblAiTitle = new Label();
            lblAiTitle.Text = "💡 找不到你的设备？试试自己做自己的开发者！";
            lblAiTitle.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            lblAiTitle.ForeColor = Color.FromArgb(240, 245, 255);
            lblAiTitle.Location = new Point(cPad, (int)(10 * dpiScale));
            lblAiTitle.AutoSize = true;
            pnlAiAdapter.Controls.Add(lblAiTitle);

            Label lblAiSub = new Label();
            lblAiSub.Text = "借助本地 AI Agent 工具自动探测外设通信与电量指令。";
            lblAiSub.Font = new Font("Microsoft YaHei UI", 8.2F);
            lblAiSub.ForeColor = Color.FromArgb(145, 155, 175);
            lblAiSub.Location = new Point(cPad, (int)(32 * dpiScale));
            lblAiSub.Size = new Size(cardW - cPad * 2, (int)(18 * dpiScale));
            pnlAiAdapter.Controls.Add(lblAiSub);

            int btnAiH = (int)(30 * dpiScale);
            int btnFullW = cardW - cPad * 2;
            int halfW = (cardW - cPad * 2 - (int)(10 * dpiScale)) / 2;

            // Row 7.1: 复制提示词 (整行醒目展示，点击后先弹出名称确认框)
            btnCopyPrompt = new ModernButton();
            btnCopyPrompt.Text = "📋 复制 AI 适配提示词 (含硬件指纹)";
            btnCopyPrompt.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            btnCopyPrompt.NormalColor = Color.FromArgb(32, 42, 60);
            btnCopyPrompt.HoverColor = Color.FromArgb(46, 60, 86);
            btnCopyPrompt.PressedColor = Color.FromArgb(22, 30, 44);
            btnCopyPrompt.BorderColor = Color.FromArgb(64, 82, 116);
            btnCopyPrompt.TextColor = Color.FromArgb(220, 235, 255);
            btnCopyPrompt.Location = new Point(cPad, (int)(52 * dpiScale));
            btnCopyPrompt.Size = new Size(btnFullW, btnAiH);
            btnCopyPrompt.Click += (s, e) =>
            {
                try
                {
                    int selIdx = lstDevices.SelectedIndex;
                    CustomDeviceConfig selItem = (selIdx >= 0 && selIdx < scannedList.Count) ? scannedList[selIdx] : null;
                    string userTxt = txtCustomName != null ? txtCustomName.Text : null;
                    string defaultModel = !string.IsNullOrEmpty(userTxt) ? userTxt.Trim() : 
                        (selItem != null ? (!string.IsNullOrEmpty(selItem.CustomName) ? selItem.CustomName : selItem.OriginalName) : "");

                    using (PromptDialog dlg = new PromptDialog("确认外设商业型号", 
                        "为了让外部 AI 能够精准逆向协议，请确认该外设的真实商业型号：\n（例如：迈从 A5 V2 Ultra、黑爵 AJ199 4K、狼蛛 F75）", 
                        defaultModel, dpiScale))
                    {
                        if (dlg.ShowDialog(this) == DialogResult.OK)
                        {
                            string confirmedName = dlg.InputText.Trim();
                            if (!string.IsNullOrEmpty(confirmedName))
                            {
                                txtCustomName.Text = confirmedName;
                                if (selItem != null) selItem.CustomName = confirmedName;
                            }
                            string prompt = (selItem != null) ? GetAiAdaptationPrompt(selItem, confirmedName) : GetAiAdaptationPrompt();
                            Clipboard.SetText(prompt);
                            btnCopyPrompt.Text = (selItem != null) ? "✓ 已复制含设备指纹的提示词！" : "✓ 提示词已复制！";
                            btnCopyPrompt.NormalColor = Color.FromArgb(0, 160, 80);
                            btnCopyPrompt.HoverColor = Color.FromArgb(0, 180, 90);
                            btnCopyPrompt.BorderColor = Color.FromArgb(0, 200, 100);
                            if (copyFeedbackTimer == null)
                            {
                                copyFeedbackTimer = new System.Windows.Forms.Timer();
                                copyFeedbackTimer.Interval = 2500;
                                copyFeedbackTimer.Tick += (s2, e2) =>
                                {
                                    copyFeedbackTimer.Stop();
                                    btnCopyPrompt.Text = "📋 复制 AI 适配提示词 (含硬件指纹)";
                                    btnCopyPrompt.NormalColor = Color.FromArgb(32, 42, 60);
                                    btnCopyPrompt.HoverColor = Color.FromArgb(46, 60, 86);
                                    btnCopyPrompt.BorderColor = Color.FromArgb(64, 82, 116);
                                };
                            }
                            copyFeedbackTimer.Stop();
                            copyFeedbackTimer.Start();
                        }
                    }
                }
                catch { }
            };
            pnlAiAdapter.Controls.Add(btnCopyPrompt);

            // Row 7.2: 导入适配包与推荐 AI 工具 (同行并列排版)
            int row2Y = (int)(86 * dpiScale);
            btnImportAdapter = new ModernButton();
            btnImportAdapter.Text = "📥 导入/粘贴 AI 适配包";
            btnImportAdapter.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            btnImportAdapter.NormalColor = Color.FromArgb(28, 48, 40);
            btnImportAdapter.HoverColor = Color.FromArgb(38, 66, 55);
            btnImportAdapter.PressedColor = Color.FromArgb(20, 36, 30);
            btnImportAdapter.BorderColor = Color.FromArgb(52, 96, 76);
            btnImportAdapter.TextColor = Color.FromArgb(0, 230, 118);
            btnImportAdapter.Location = new Point(cPad, row2Y);
            btnImportAdapter.Size = new Size(halfW, btnAiH);
            btnImportAdapter.Click += (s, e) =>
            {
                using (ImportAdapterDialog dlg = new ImportAdapterDialog(dpiScale))
                {
                    if (dlg.ShowDialog(this) == DialogResult.OK)
                    {
                        PerformScanAsync();
                    }
                }
            };
            pnlAiAdapter.Controls.Add(btnImportAdapter);

            btnAiTools = new ModernButton();
            btnAiTools.Text = "🚀 推荐 AI 工具 ↗";
            btnAiTools.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            btnAiTools.NormalColor = Color.FromArgb(28, 34, 48);
            btnAiTools.HoverColor = Color.FromArgb(40, 50, 70);
            btnAiTools.PressedColor = Color.FromArgb(20, 24, 34);
            btnAiTools.BorderColor = Color.FromArgb(56, 70, 98);
            btnAiTools.TextColor = Color.FromArgb(0, 210, 255);
            btnAiTools.Location = new Point(cPad + halfW + (int)(10 * dpiScale), row2Y);
            btnAiTools.Size = new Size(halfW, btnAiH);
            btnAiTools.Click += (s, e) =>
            {
                using (AiToolsDialog dlg = new AiToolsDialog(dpiScale))
                {
                    dlg.ShowDialog(this);
                }
            };
            pnlAiAdapter.Controls.Add(btnAiTools);

            // Row 8: Buttons
            int btnH = (int)(36 * dpiScale);
            int btnY = (int)(548 * dpiScale);
            int cancelW = (int)(110 * dpiScale);
            int saveW = (int)(150 * dpiScale);

            btnCancel = new ModernButton();
            btnCancel.Text = "取消";
            btnCancel.Font = new Font("Microsoft YaHei UI", 9F);
            btnCancel.NormalColor = Color.FromArgb(32, 36, 48);
            btnCancel.HoverColor = Color.FromArgb(46, 52, 68);
            btnCancel.PressedColor = Color.FromArgb(24, 28, 38);
            btnCancel.BorderColor = Color.FromArgb(60, 68, 88);
            btnCancel.TextColor = Color.FromArgb(220, 230, 250);
            btnCancel.Location = new Point(this.ClientSize.Width - pad - cancelW - saveW - (int)(10 * dpiScale), btnY);
            btnCancel.Size = new Size(cancelW, btnH);
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnCancel);

            btnSave = new ModernButton();
            btnSave.Text = "➕ 加入监控";
            btnSave.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            btnSave.NormalColor = Color.FromArgb(0, 200, 83);
            btnSave.HoverColor = Color.FromArgb(0, 230, 118);
            btnSave.PressedColor = Color.FromArgb(0, 170, 70);
            btnSave.BorderColor = Color.FromArgb(0, 230, 118);
            btnSave.TextColor = Color.Black;
            btnSave.Location = new Point(this.ClientSize.Width - pad - saveW, btnY);
            btnSave.Size = new Size(saveW, btnH);
            btnSave.Enabled = false;
            btnSave.Click += (s, e) => SaveSelectedDevice();
            this.Controls.Add(btnSave);

            PerformScanAsync();
        }

        private void LstDevices_DrawItem(object sender, DrawItemEventArgs e)
        {
            if (e.Index < 0 || e.Index >= scannedList.Count) return;
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            var item = scannedList[e.Index];
            bool isSel = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            bool isHidden = DeviceManager.IsDeviceHidden(item.DeviceId) || 
                            (!string.IsNullOrEmpty(item.HardwareFingerprint) && DeviceManager.IsDeviceHidden(item.HardwareFingerprint));
            bool already = false;
            if (!isHidden)
            {
                var vList = DeviceManager.GetDeviceIdVariants(item.DeviceId);
                if (!string.IsNullOrEmpty(item.HardwareFingerprint))
                {
                    vList.AddRange(DeviceManager.GetDeviceIdVariants(item.HardwareFingerprint));
                }
                for (int vi = 0; vi < vList.Count; vi++)
                {
                    if (existingIds.Contains(vList[vi]))
                    {
                        already = true;
                        break;
                    }
                }
            }

            // Background fill
            using (var bgBrush = new SolidBrush(Color.FromArgb(18, 19, 23)))
            {
                g.FillRectangle(bgBrush, e.Bounds);
            }

            // Card rectangle
            Rectangle cardRect = new Rectangle(e.Bounds.X + 2, e.Bounds.Y + 2, e.Bounds.Width - 4, e.Bounds.Height - 4);
            Color cardBg = isSel ? Color.FromArgb(32, 42, 58) : Color.FromArgb(25, 29, 40);
            Color cardBorder = isSel ? Color.FromArgb(0, 230, 118) : Color.FromArgb(42, 48, 65);

            using (var path = RoundedCard.GetRoundedRectangle(cardRect, (int)(7 * dpiScale)))
            {
                using (var cb = new SolidBrush(cardBg)) g.FillPath(cb, path);
                using (var cp = new Pen(cardBorder, isSel ? 1.6f : 1.0f)) g.DrawPath(cp, path);
            }

            // Left circle badge for icon
            int iconBoxSize = (int)(32 * dpiScale);
            int iconX = cardRect.X + (int)(10 * dpiScale);
            int iconY = cardRect.Y + (cardRect.Height - iconBoxSize) / 2;
            Rectangle iconRect = new Rectangle(iconX, iconY, iconBoxSize, iconBoxSize);

            Color brandCol = item.Category == DeviceCategory.Headset || item.Category == DeviceCategory.Earbuds ? Color.FromArgb(90, 160, 255) :
                             (item.Category == DeviceCategory.Keyboard ? Color.FromArgb(255, 170, 0) :
                             (item.Category == DeviceCategory.Gamepad ? Color.FromArgb(180, 100, 255) : Color.FromArgb(0, 230, 118)));

            using (var iconCircle = RoundedCard.GetRoundedRectangle(iconRect, iconBoxSize / 2))
            {
                using (var ib = new SolidBrush(Color.FromArgb(35, brandCol.R, brandCol.G, brandCol.B))) g.FillPath(ib, iconCircle);
                using (var ip = new Pen(Color.FromArgb(90, brandCol.R, brandCol.G, brandCol.B), 1f)) g.DrawPath(ip, iconCircle);
            }

            ModernThemeHelper.DrawModernDeviceIcon(g, iconRect, item.Category, brandCol);

            // Text info
            int textX = iconX + iconBoxSize + (int)(10 * dpiScale);
            string dispName = !string.IsNullOrEmpty(item.CustomName) ? item.CustomName : item.OriginalName;
            using (var nameFont = new Font("Microsoft YaHei UI", 9.2F, FontStyle.Bold))
            using (var nameBrush = new SolidBrush(Color.FromArgb(242, 246, 255)))
            {
                g.DrawString(dispName, nameFont, nameBrush, textX, cardRect.Y + (int)(7 * dpiScale));
            }

            string sub = item.Transport ?? "2.4G/USB";
            using (var subFont = new Font("Microsoft YaHei UI", 8F))
            using (var subBrush = new SolidBrush(Color.FromArgb(135, 145, 165)))
            {
                g.DrawString(sub, subFont, subBrush, textX, cardRect.Y + (int)(27 * dpiScale));
            }

            // Right tags: [已在设备库中] or [电量: X%]
            int rightX = cardRect.Right - (int)(10 * dpiScale);
            if (already)
            {
                string tag = "已在设备库中";
                using (var tagFont = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold))
                {
                    var tSz = TextRenderer.MeasureText(tag, tagFont);
                    int tW = tSz.Width + (int)(10 * dpiScale);
                    int tH = (int)(20 * dpiScale);
                    int tX = rightX - tW;
                    int tY = cardRect.Y + (cardRect.Height - tH) / 2;
                    Rectangle tagRect = new Rectangle(tX, tY, tW, tH);

                    using (var tp = RoundedCard.GetRoundedRectangle(tagRect, (int)(4 * dpiScale)))
                    {
                        using (var tb = new SolidBrush(Color.FromArgb(40, 255, 183, 77))) g.FillPath(tb, tp);
                        using (var tpen = new Pen(Color.FromArgb(180, 255, 183, 77), 1f)) g.DrawPath(tpen, tp);
                    }
                    using (var tfb = new SolidBrush(Color.FromArgb(255, 195, 100)))
                    {
                        g.DrawString(tag, tagFont, tfb, tX + (int)(5 * dpiScale), tY + (int)(2 * dpiScale));
                    }
                    rightX = tX - (int)(8 * dpiScale);
                }
            }

            if (item.CachedBattery > 0)
            {
                string bStr = item.CachedBattery + "%";
                using (var bFont = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold))
                {
                    var bSz = TextRenderer.MeasureText(bStr, bFont);
                    int bW = bSz.Width + (int)(10 * dpiScale);
                    int bH = (int)(20 * dpiScale);
                    int bX = rightX - bW;
                    int bY = cardRect.Y + (cardRect.Height - bH) / 2;
                    Rectangle bRect = new Rectangle(bX, bY, bW, bH);

                    using (var bp = RoundedCard.GetRoundedRectangle(bRect, (int)(4 * dpiScale)))
                    {
                        using (var bb = new SolidBrush(Color.FromArgb(30, 0, 230, 118))) g.FillPath(bb, bp);
                        using (var bpen = new Pen(Color.FromArgb(80, 0, 230, 118), 1f)) g.DrawPath(bpen, bp);
                    }
                    using (var bfb = new SolidBrush(Color.FromArgb(0, 230, 118)))
                    {
                        g.DrawString(bStr, bFont, bfb, bX + (int)(5 * dpiScale), bY + (int)(2 * dpiScale));
                    }
                }
            }
        }

        private void PerformScanAsync()
        {
            if (isScanning) return;
            isScanning = true;

            btnScan.Enabled = false;
            btnScan.Text = "⏳ 扫描中...";
            beamControl.Start();
            lblTestStatus.ForeColor = Color.FromArgb(0, 230, 118);
            lblTestStatus.Text = "正在检索在线 2.4G 接收器、HID 外设与蓝牙设备...";

            ThreadPool.QueueUserWorkItem((state) => {
                var tempConfigs = new List<CustomDeviceConfig>();
                var existingConfigs = DeviceManager.LoadCustomDevices();

                Func<string, string, string, string> resolveCustomName = delegate(string devId, string fallbackDisp, string origName) {
                    if (existingConfigs != null)
                    {
                        var match = existingConfigs.Find(c => c.DeviceId == devId || (!string.IsNullOrEmpty(c.HardwareFingerprint) && c.HardwareFingerprint == devId));
                        if (match != null && !string.IsNullOrEmpty(match.CustomName))
                            return match.CustomName;
                    }
                    return !string.IsNullOrEmpty(fallbackDisp) ? fallbackDisp : origName;
                };

                Func<string, DeviceCategory, DeviceCategory> resolveCategory = delegate(string devId, DeviceCategory defCat) {
                    if (existingConfigs != null)
                    {
                        var match = existingConfigs.Find(c => c.DeviceId == devId || (!string.IsNullOrEmpty(c.HardwareFingerprint) && c.HardwareFingerprint == devId));
                        if (match != null)
                            return match.Category;
                    }
                    return defCat;
                };

                // 1. Scan Razer Devices (2.4G Hyperspeed / USB)
                try
                {
                    var rzList = RazerDeviceHelper.QueryAllRazerDevices();
                    if (rzList != null)
                    {
                        foreach (var rz in rzList)
                        {
                            if (rz != null && (rz.IsConnected || rz.IsDonglePresent))
                            {
                                string normId = RazerDeviceHelper.NormalizeRazerDeviceId(rz.DeviceId);
                                if (!tempConfigs.Exists(c => c.DeviceId == normId))
                                {
                                    var cfg = new CustomDeviceConfig
                                    {
                                        DeviceId = normId,
                                        OriginalName = rz.DeviceName,
                                        CustomName = resolveCustomName(normId, rz.DisplayName, rz.DeviceName),
                                        Category = resolveCategory(normId, rz.Category),
                                        Transport = "2.4G 无线",
                                        CachedBattery = rz.BatteryPercent,
                                        HardwareFingerprint = rz.DeviceId
                                    };
                                    tempConfigs.Add(cfg);
                                }
                            }
                        }
                    }
                }
                catch { }

                // 2. Scan Rapoo Devices (2.4G / USB)
                try
                {
                    var rp = RapooDeviceHelper.QueryRapooDeviceInfo();
                    if (rp != null && (rp.IsConnected || rp.IsDonglePresent))
                    {
                        var cfg = new CustomDeviceConfig
                        {
                            DeviceId = rp.DeviceId,
                            OriginalName = rp.DeviceName,
                            CustomName = resolveCustomName(rp.DeviceId, rp.DisplayName, rp.DeviceName),
                            Category = resolveCategory(rp.DeviceId, DeviceCategory.Mouse),
                            Transport = "2.4G 无线",
                            CachedBattery = rp.BatteryPercent,
                            HardwareFingerprint = rp.DeviceId
                        };
                        tempConfigs.Add(cfg);
                    }
                }
                catch { }

                // 2.5 Scan VGN Devices (2.4G / USB)
                try
                {
                    var vgn = VgnDeviceHelper.QueryVgnDeviceInfo();
                    if (vgn != null && (vgn.IsConnected || vgn.IsDonglePresent))
                    {
                        var cfg = new CustomDeviceConfig
                        {
                            DeviceId = vgn.DeviceId,
                            OriginalName = vgn.DeviceName,
                            CustomName = resolveCustomName(vgn.DeviceId, vgn.DisplayName, vgn.DeviceName),
                            Category = resolveCategory(vgn.DeviceId, DeviceCategory.Mouse),
                            Transport = "2.4G 无线",
                            CachedBattery = vgn.BatteryPercent,
                            HardwareFingerprint = vgn.DeviceId
                        };
                        tempConfigs.Add(cfg);
                    }
                }
                catch { }

                // 2.55 Scan Logitech Devices (Lightspeed / Unifying / Nano / Wired)
                try
                {
                    var logiList = LogitechDeviceHelper.QueryAllLogitechDevices();
                    if (logiList != null)
                    {
                        foreach (var dev in logiList)
                        {
                            if (dev != null && (dev.IsConnected || dev.IsDonglePresent))
                            {
                                if (!tempConfigs.Exists(c => c.DeviceId == dev.DeviceId))
                                {
                                    var cfg = new CustomDeviceConfig
                                    {
                                        DeviceId = dev.DeviceId,
                                        OriginalName = dev.DeviceName,
                                        CustomName = resolveCustomName(dev.DeviceId, dev.DisplayName, dev.DeviceName),
                                        Category = resolveCategory(dev.DeviceId, dev.Category),
                                        Transport = dev.Transport ?? "Lightspeed 无线",
                                        CachedBattery = dev.BatteryPercent,
                                        HardwareFingerprint = dev.DeviceId
                                    };
                                    tempConfigs.Add(cfg);
                                }
                            }
                        }
                    }
                }
                catch { }



                // 2.56 Scan Valkyrie Devices (2.4G / USB)
                try
                {
                    var vkList = ValkyrieDeviceHelper.QueryAllValkyrieDevices();
                    if (vkList != null)
                    {
                        foreach (var vk in vkList)
                        {
                            if (vk != null && (vk.IsConnected || vk.IsDonglePresent))
                            {
                                if (!tempConfigs.Exists(c => c.DeviceId == vk.DeviceId || (!string.IsNullOrEmpty(c.HardwareFingerprint) && c.HardwareFingerprint == vk.HardwareFingerprint)))
                                {
                                    var cfg = new CustomDeviceConfig
                                    {
                                        DeviceId = vk.DeviceId,
                                        OriginalName = vk.DeviceName,
                                        CustomName = resolveCustomName(vk.DeviceId, vk.DisplayName, vk.DeviceName),
                                        Category = resolveCategory(vk.DeviceId, vk.Category),
                                        Transport = vk.Transport ?? "2.4G 无线",
                                        CachedBattery = vk.BatteryPercent,
                                        HardwareFingerprint = vk.HardwareFingerprint ?? vk.DeviceId
                                    };
                                    tempConfigs.Add(cfg);
                                }
                            }
                        }
                    }
                }
                catch { }

                // 2.57 Scan Universal Gaming Devices (OpenMouse / All-in-One: Lamzu, Pulsar, ATK, Attack Shark, etc.)
                try
                {
                    var gamingList = UniversalGamingDeviceHelper.QueryAllGamingDevices();
                    if (gamingList != null)
                    {
                        foreach (var dev in gamingList)
                        {
                            if (dev != null && (dev.IsConnected || dev.IsDonglePresent))
                            {
                                if (!tempConfigs.Exists(c => c.DeviceId == dev.DeviceId || (!string.IsNullOrEmpty(c.HardwareFingerprint) && c.HardwareFingerprint == dev.HardwareFingerprint)))
                                {
                                    var cfg = new CustomDeviceConfig
                                    {
                                        DeviceId = dev.DeviceId,
                                        OriginalName = dev.DeviceName,
                                        CustomName = resolveCustomName(dev.DeviceId, dev.DisplayName, dev.DeviceName),
                                        Category = resolveCategory(dev.DeviceId, dev.Category),
                                        Transport = dev.Transport ?? "2.4G 无线",
                                        CachedBattery = dev.BatteryPercent,
                                        HardwareFingerprint = dev.HardwareFingerprint ?? dev.DeviceId
                                    };
                                    tempConfigs.Add(cfg);
                                }
                            }
                        }
                    }
                }
                catch { }

                // 2.58 Scan Apple & Beats Devices
                try
                {
                    var appleList = AppleDeviceHelper.QueryAllAppleDevices();
                    if (appleList != null)
                    {
                        foreach (var dev in appleList)
                        {
                            if (dev != null && (dev.IsConnected || dev.IsDonglePresent))
                            {
                                if (!tempConfigs.Exists(c => c.DeviceId == dev.DeviceId || (!string.IsNullOrEmpty(c.HardwareFingerprint) && c.HardwareFingerprint == dev.HardwareFingerprint)))
                                {
                                    var cfg = new CustomDeviceConfig
                                    {
                                        DeviceId = dev.DeviceId,
                                        OriginalName = dev.DeviceName,
                                        CustomName = resolveCustomName(dev.DeviceId, dev.DisplayName, dev.DeviceName),
                                        Category = resolveCategory(dev.DeviceId, dev.Category),
                                        Transport = dev.Transport ?? "蓝牙",
                                        CachedBattery = dev.BatteryPercent,
                                        HardwareFingerprint = dev.HardwareFingerprint ?? dev.DeviceId
                                    };
                                    tempConfigs.Add(cfg);
                                }
                            }
                        }
                    }
                }
                catch { }

                // 2.6 Scan Generic HID Mice & Peripherals (2.4G / USB)
                try
                {
                    var genMice = GenericHidHelper.ScanGenericHidMice(existingIds);
                    foreach (var gm in genMice)
                    {
                        if (gm != null)
                        {
                            string gmKey = gm.DeviceId != null && gm.DeviceId.StartsWith("HID:") ? gm.DeviceId.Substring(4) : gm.DeviceId;
                            bool dup = tempConfigs.Exists(c => (c.DeviceId != null && c.DeviceId.IndexOf(gmKey, StringComparison.OrdinalIgnoreCase) >= 0) ||
                                                               (c.HardwareFingerprint != null && c.HardwareFingerprint.IndexOf(gmKey, StringComparison.OrdinalIgnoreCase) >= 0));
                            if (!dup)
                            {
                                tempConfigs.Add(gm);
                            }
                        }
                    }
                }
                catch { }

                // 3. Scan Bluetooth Devices (BLE & Classic)
                try
                {
                    var btList = BluetoothDeviceHelper.ScanAllBluetoothDevices();
                    foreach (var b in btList)
                    {
                        b.Transport = "蓝牙";
                        b.CustomName = resolveCustomName(b.DeviceId, b.CustomName, b.OriginalName);
                        b.Category = resolveCategory(b.DeviceId, b.Category);
                        tempConfigs.Add(b);
                    }
                }
                catch { }

                // Safely update UI atomically
                try
                {
                    if (this.IsDisposed) return;
                    this.BeginInvoke((Action)(() => {
                        if (this.IsDisposed) return;
                        isScanning = false;
                        beamControl.Stop();
                        btnScan.Enabled = true;
                        btnScan.Text = "🔄 重新扫描";

                        lstDevices.BeginUpdate();
                        try
                        {
                            lstDevices.Items.Clear();
                            scannedList.Clear();
                            scannedList.AddRange(tempConfigs);
                            for (int i = 0; i < scannedList.Count; i++)
                            {
                                string label = (!string.IsNullOrEmpty(scannedList[i].CustomName) && !string.Equals(scannedList[i].CustomName, scannedList[i].OriginalName, StringComparison.OrdinalIgnoreCase))
                                    ? scannedList[i].CustomName + " (" + scannedList[i].OriginalName + ")"
                                    : (!string.IsNullOrEmpty(scannedList[i].CustomName) ? scannedList[i].CustomName : scannedList[i].OriginalName);
                                lstDevices.Items.Add(label);
                            }
                        }
                        finally
                        {
                            lstDevices.EndUpdate();
                        }

                        if (lstDevices.Items.Count > 0)
                        {
                            lstDevices.SelectedIndex = 0;
                        }
                        else
                        {
                            lblTestStatus.ForeColor = Color.FromArgb(255, 183, 77);
                            lblTestStatus.Text = "● 未发现新的在线外设，请检查外设电源与配对状态。";
                        }
                    }));
                }
                catch { }
            });
        }

        private void OnDeviceSelected()
        {
            int idx = lstDevices.SelectedIndex;
            if (idx >= 0 && idx < scannedList.Count)
            {
                var item = scannedList[idx];
                bool isHidden = DeviceManager.IsDeviceHidden(item.DeviceId) || 
                                (!string.IsNullOrEmpty(item.HardwareFingerprint) && DeviceManager.IsDeviceHidden(item.HardwareFingerprint));
                bool already = false;
                if (!isHidden)
                {
                    var vList = DeviceManager.GetDeviceIdVariants(item.DeviceId);
                    if (!string.IsNullOrEmpty(item.HardwareFingerprint))
                    {
                        vList.AddRange(DeviceManager.GetDeviceIdVariants(item.HardwareFingerprint));
                    }
                    for (int vi = 0; vi < vList.Count; vi++)
                    {
                        if (existingIds.Contains(vList[vi]))
                        {
                            already = true;
                            break;
                        }
                    }
                }

                txtCustomName.Text = !string.IsNullOrEmpty(item.CustomName) ? item.CustomName : item.OriginalName;
                if (item.Category == DeviceCategory.Mouse) cmbCategory.SelectedIndex = 0;
                else if (item.Category == DeviceCategory.Keyboard) cmbCategory.SelectedIndex = 1;
                else if (item.Category == DeviceCategory.Headset) cmbCategory.SelectedIndex = 2;
                else if (item.Category == DeviceCategory.Earbuds) cmbCategory.SelectedIndex = 3;
                else if (item.Category == DeviceCategory.Gamepad) cmbCategory.SelectedIndex = 4;
                else if (item.Category == DeviceCategory.Dongle) cmbCategory.SelectedIndex = 5;
                else cmbCategory.SelectedIndex = 6;

                if (already)
                {
                    lblTestStatus.ForeColor = Color.FromArgb(0, 230, 118);
                    lblTestStatus.Text = "● 该设备已在设备库中，您可以直接修改自定义昵称或分类并保存更新。";
                    btnSave.Text = "💾 保存修改";
                    btnSave.NormalColor = Color.FromArgb(0, 180, 90);
                    btnSave.HoverColor = Color.FromArgb(0, 210, 110);
                    btnSave.Enabled = true;
                }
                else
                {
                    lblTestStatus.ForeColor = Color.FromArgb(0, 230, 118);
                    string battStr = item.CachedBattery > 0 ? string.Format("实时电量: {0}%", item.CachedBattery) : "供电: USB/免驱";
                    lblTestStatus.Text = string.Format("● 设备在线就绪！{0}\n连接通道: {1} | 标识: {2}", 
                        battStr, item.Transport ?? "2.4G/USB", item.HardwareFingerprint ?? item.DeviceId);
                    btnSave.Text = "➕ 加入监控";
                    btnSave.NormalColor = Color.FromArgb(0, 200, 83);
                    btnSave.HoverColor = Color.FromArgb(0, 230, 118);
                    btnSave.Enabled = true;
                }
            }
            else
            {
                btnSave.Enabled = false;
            }
        }

        private void SaveSelectedDevice()
        {
            int idx = lstDevices.SelectedIndex;
            if (idx >= 0 && idx < scannedList.Count)
            {
                var item = scannedList[idx];
                string name = txtCustomName.Text.Trim();
                if (string.IsNullOrEmpty(name)) name = !string.IsNullOrEmpty(item.CustomName) ? item.CustomName : item.OriginalName;
                item.CustomName = name;

                switch (cmbCategory.SelectedIndex)
                {
                    case 0: item.Category = DeviceCategory.Mouse; break;
                    case 1: item.Category = DeviceCategory.Keyboard; break;
                    case 2: item.Category = DeviceCategory.Headset; break;
                    case 3: item.Category = DeviceCategory.Earbuds; break;
                    case 4: item.Category = DeviceCategory.Gamepad; break;
                    case 5: item.Category = DeviceCategory.Dongle; break;
                    default: item.Category = DeviceCategory.Generic; break;
                }

                // Ensure canonical ID for Razer devices
                if (item.DeviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase))
                {
                    item.DeviceId = RazerDeviceHelper.NormalizeRazerDeviceId(item.DeviceId);
                }

                DeviceManager.SaveCustomDevice(item);
                DeviceManager.UnhideDevice(item.DeviceId);
                if (!string.IsNullOrEmpty(item.HardwareFingerprint)) DeviceManager.UnhideDevice(item.HardwareFingerprint);
                if (item.DeviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase))
                {
                    string norm = RazerDeviceHelper.NormalizeRazerDeviceId(item.DeviceId);
                    DeviceManager.UnhideDevice(norm);
                }

                // Also append to priority list if not in it
                var prio = DeviceManager.GetPriorityList();
                if (!prio.Contains(item.DeviceId))
                {
                    prio.Add(item.DeviceId);
                    DeviceManager.SavePriorityList(prio);
                }

                this.AddedDeviceId = item.DeviceId;
                this.DialogResult = DialogResult.OK;
                this.Close();
            }
        }

        private static bool TryExtractVidPid(string identifier, out int vid, out int pid)
        {
            vid = 0;
            pid = 0;
            if (string.IsNullOrEmpty(identifier)) return false;
            string s = identifier.ToLowerInvariant();

            int vidIdx = s.IndexOf("vid_");
            int pidIdx = s.IndexOf("pid_");
            if (vidIdx >= 0 && pidIdx >= 0)
            {
                try
                {
                    string vidStr = s.Substring(vidIdx + 4, 4);
                    string pidStr = s.Substring(pidIdx + 4, 4);
                    vid = Convert.ToInt32(vidStr, 16);
                    pid = Convert.ToInt32(pidStr, 16);
                    return true;
                }
                catch { }
            }

            string[] parts = s.Split(':');
            for (int i = 0; i < parts.Length - 1; i++)
            {
                if (parts[i].Length == 4 && parts[i + 1].Length == 4)
                {
                    try
                    {
                        vid = Convert.ToInt32(parts[i], 16);
                        pid = Convert.ToInt32(parts[i + 1], 16);
                        return true;
                    }
                    catch { }
                }
            }

            return false;
        }

        private static string LookupVendorBrand(int vid, string prodStr, string mfgStr)
        {
            switch (vid)
            {
                case 0x1532: return "雷蛇 (Razer)";
                case 0x046D: return "罗技 (Logitech)";
                case 0x3837: return "迈从 (MCHOSE)";
                case 0x5253: return "迈从 (MCHOSE)";
                case 0x3419: return "VGN / VXE / ATK (倍思 / 拓步 OEM)";
                case 0x373B: return "ATK / VXE (Beken / Compx 方案)";
                case 0x37D7: return "飞智 (Flydigi)";
                case 0x320F: return "蓝图 (LAMZU)";
                case 0x361D: return "狼蛛 (AULA)";
                case 0x0C45: return "松翰 / 黑爵 (AJAZZ)";
                case 0x258A: return "赛诺伟思 / 达摩鲨 (Darmoshark) / 多彩 (Delux)";
                case 0x24AE: return "雷柏 (Rapoo)";
                case 0x3554:
                    if (!string.IsNullOrEmpty(prodStr) || !string.IsNullOrEmpty(mfgStr))
                    {
                        string combined = ((prodStr ?? "") + " " + (mfgStr ?? "")).ToLowerInvariant();
                        if (combined.Contains("vgn") || combined.Contains("dragonfly") || combined.Contains("f1") || combined.Contains("f2") || combined.Contains("atk") || combined.Contains("vxe"))
                            return "VGN / VXE / ATK (康普讯 Compx 方案)";
                        if (combined.Contains("vk") || combined.Contains("valkyrie") || combined.Contains("m5") || combined.Contains("m7"))
                            return "瓦尔基里 (Valkyrie / 康普讯 Compx 方案)";
                        if (combined.Contains("zaopin"))
                            return "造品 (Zaopin / 康普讯 Compx 方案)";
                        if (combined.Contains("scyrox"))
                            return "Scyrox (康普讯 Compx 方案)";
                        if (combined.Contains("incott"))
                            return "英菲克 (Incott / 康普讯 Compx 方案)";
                        if (combined.Contains("darmoshark"))
                            return "达摩鲨 (Darmoshark / 康普讯 Compx 方案)";
                        if (combined.Contains("waizowl"))
                            return "枭鲨 (Waizowl / 康普讯 Compx 方案)";
                        if (combined.Contains("wlmouse"))
                            return "盈石 (WLMOUSE / 康普讯 Compx 方案)";
                    }
                    return "康普讯公版方案 (Compx OEM - 广泛代工国产 2.4G/8K 电竞外设)";
                case 0x054C: return "索尼 (Sony)";
                case 0x1038: return "赛睿 (SteelSeries)";
                case 0x1B1C: return "美商海盗船 (Corsair)";
                case 0x0B05: return "华硕 ROG (ASUS ROG)";
                case 0x045E: return "微软 (Microsoft / Xbox)";
                case 0x03EB: return "怒喵 (Angry Miao)";
                case 0x0951: return "极度未知 (HyperX)";
                case 0x03F0: return "惠普 (HP / HyperX)";
                case 0x17EF: return "联想拯救者 (Lenovo Legion)";
                case 0x2E8A: return "NuPhy (矩阵科技)";
                case 0x3434: return "Keychron (渴创)";
                default: return "未登记厂商 (通用公版芯片方案)";
            }
        }

        private static string ProbeDeviceDetails(CustomDeviceConfig item, out int vid, out int pid, out string romProd, out string romMfg)
        {
            vid = 0;
            pid = 0;
            romProd = "未知";
            romMfg = "未知";
            if (item == null) return "  * （未选中任何特定设备）";

            string idToProbe = !string.IsNullOrEmpty(item.HardwareFingerprint) ? item.HardwareFingerprint : item.DeviceId;
            TryExtractVidPid(idToProbe, out vid, out pid);
            if (vid == 0 && pid == 0)
            {
                TryExtractVidPid(item.DeviceId, out vid, out pid);
            }

            if (vid == 0 && pid == 0)
            {
                return "  * （无法从设备标识中提取有效 VID:PID）";
            }

            StringBuilder sb = new StringBuilder();
            try
            {
                List<HidDeviceDescriptor> descriptors = HidTransport.Enumerate(vid, pid);
                if (descriptors != null && descriptors.Count > 0)
                {
                    for (int i = 0; i < descriptors.Count; i++)
                    {
                        var d = descriptors[i];
                        if (!string.IsNullOrEmpty(d.ProductString) && romProd == "未知") romProd = d.ProductString;
                        if (!string.IsNullOrEmpty(d.ManufacturerString) && romMfg == "未知") romMfg = d.ManufacturerString;

                        string note = (d.UsagePage >= 0xFF00) ? " [推荐私有控制通道]" : "";
                        sb.Append(string.Format("  * 端点 {0}: UsagePage=0x{1:X4}, Usage=0x{2:X4}, InLen={3}, OutLen={4}, FeatureLen={5}{6}\r\n",
                            i + 1, d.UsagePage, d.Usage, d.InputReportLength, d.OutputReportLength, d.FeatureReportLength, note));
                    }
                }
                else
                {
                    sb.Append("  * （未探测到活跃的 HID 端点描述符，可能当前处于休眠或仅作为免驱外设枚举）\r\n");
                }
            }
            catch (Exception ex)
            {
                sb.Append(string.Format("  * 探测发生异常: {0}\r\n", ex.Message));
            }

            return sb.ToString().TrimEnd();
        }

        public static string GetAiAdaptationPrompt()
        {
            return GetAiAdaptationPrompt(null, null);
        }

        public static string GetAiAdaptationPrompt(CustomDeviceConfig item, string userTypedName)
        {
            int vid = 0;
            int pid = 0;
            string romProd = "未知";
            string romMfg = "未知";
            string endpoints = (item != null) ? ProbeDeviceDetails(item, out vid, out pid, out romProd, out romMfg) : "  * （未在列表中指定特定设备）";
            string brand = (vid != 0) ? LookupVendorBrand(vid, romProd, romMfg) : "待反查厂商";
            
            string userRefName = !string.IsNullOrEmpty(userTypedName) ? userTypedName.Trim() : 
                ((item != null && !string.IsNullOrEmpty(item.CustomName)) ? item.CustomName : (item != null ? item.OriginalName : "未指定"));
            if (string.IsNullOrEmpty(userRefName)) userRefName = "未知设备";

            string catStr = "Mouse";
            string catCn = "鼠标";
            if (item != null)
            {
                catCn = MouseBatteryInfo.GetCategoryDisplayName(item.Category);
                switch (item.Category)
                {
                    case DeviceCategory.Keyboard: catStr = "Keyboard"; break;
                    case DeviceCategory.Headset: catStr = "Headset"; break;
                    case DeviceCategory.Earbuds: catStr = "Earbuds"; break;
                    case DeviceCategory.Gamepad: catStr = "Gamepad"; break;
                    case DeviceCategory.Dongle: catStr = "Dongle"; break;
                    case DeviceCategory.Generic: catStr = "Generic"; break;
                    default: catStr = "Mouse"; break;
                }
            }

            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# 任务：为「FerrisPulse (灵脉)」开源外设系统生成 FDAS v1.0 硬件适配规范包");
            sb.AppendLine();
            sb.AppendLine("你好！我现在使用的外设设备暂未被「FerrisPulse (灵脉)」原生识别。请作为资深系统级硬件逆向与固件协议工程师，根据以下从当前物理硬件中实时捕获的硬件现场指纹与通信拓扑，为我输出一份严格符合 **FDAS v1.0 (FerrisPulse Device Adapter Specification)** 标准的适配规范包（JSON）。");
            sb.AppendLine();
            sb.AppendLine("## ⚠️ 核心工作流：两阶段严谨逆向法（物理事实优先，严禁幻觉）");
            sb.AppendLine("你必须严格按以下两阶段开展工作：");
            sb.AppendLine();
            sb.AppendLine("### 阶段一：硬件物理安全第一与官方 WebHID 源码逆向 (Safety First)");
            sb.AppendLine("1. **严厉安全红线：严禁在真机上下发盲测性质的写指令 (如 Cmd 0x07、写未知内存地址)**！");
            sb.AppendLine("   - 现代国产无线外设（如 CompX、PixArt 等主控）多将单片机板载 EEPROM / Flash 直接映射为写指令。");
            sb.AppendLine("   - 盲猜写指令或写错地址/长度，会瞬间冲毁 2.4G RF 配对表、校验扇区或引导记录，导致接收器硬件彻底死锁变砖！");
            sb.AppendLine("2. **官方 WebHID 源码逆向法（首选且唯一推荐的写指令来源，100% 零风险）**：");
            sb.AppendLine("   - 现代外设厂商（VGN, ATK, VXE, 迈从, 达摩鲨, 狼蛛, 雷神, 机械师等）90% 均提供官方 Web 驱动（Chrome 浏览器网页端，如 hub.vgn.cn、hub.atk.pro 等）。");
            sb.AppendLine("   - 请优先让用户或你在浏览器打开该网页驱动，按 F12 打开开发者工具：在 Sources / Network 面板中检索 `sendReport`、`write`、`0x07`、`0x55`、`rate`、`dpi` 等关键词；");
            sb.AppendLine("   - 直接将官方前端工程师编写的报文生成逻辑与公式提取出来！这套公式是原厂 100% 验证过的安全逻辑，绝不会损害硬件！");
            sb.AppendLine("3. **探针安全只读原则**：本工程根目录提供了 `tools/probe_hid_device.ps1`，默认仅允许执行只读探针或白名单安全电量查询。");
            sb.AppendLine();
            sb.AppendLine("### 阶段二：FDAS v1.0 动态微表达式与规范包封装 (Dynamic Calculation)");
            sb.AppendLine("FerrisPulse 底层已内置轻量微表达式求值器，无需改动 C# 代码即可动态处理任意厂商的奇葩算法：");
            sb.AppendLine("FerrisPulse 底层已内置轻量微表达式求值器，无需改动 C# 代码即可动态处理任意厂商的奇葩算法：");
            sb.AppendLine("1. **动态派生变量 (`protocol.variables`)**：支持基础四则运算、移位、位掩码与互补校验，例如：");
            sb.AppendLine("   - `\"rateParity\": \"(0x55 - $rateCode) & 0xFF\"` (CompX 互补校验位)");
            sb.AppendLine("   - `\"dpiEncoded\": \"($dpi / 50) - 1\"` (PAW3950/3395 编码)");
            sb.AppendLine("   - `\"dpiLow\": \"$dpiEncoded & 0xFF\"`");
            sb.AppendLine("   - `\"dpiHigh\": \"($dpiEncoded >> 8) & 0x03\"`");
            sb.AppendLine("   - `\"dpiFlags\": \"($dpiHigh << 2) | ($dpiHigh << 6)\"`");
            sb.AppendLine("   - `\"dpiParity\": \"(0x55 - $dpiLow - $dpiLow - $dpiFlags) & 0xFF\"`");
            sb.AppendLine("   - `\"dpiAddrLo\": \"(0x000C + $stage0 * 4) & 0xFF\"`");
            sb.AppendLine("2. **私有回报率编码映射 (`protocol.rateCodeMap`)**：如 `\"rateCodeMap\": { \"125\": \"0x08\", \"1000\": \"0x01\", \"8000\": \"0x40\" }`");
            sb.AppendLine("3. **多步时序握手管线 (`protocol.queryBatteryPipeline`)**：若设备查询电量需先唤醒接收器再查询，可声明执行序列：`[\"08 02 ...\", \"08 04 ...\"]`");
            sb.AppendLine("4. **内联表达式支持**：模板中直接支持 `{ (0x55 - rateCode) & 0xFF }` 动态求值。");
            sb.AppendLine();
            sb.AppendLine("## 🛡️ 逃生舱机制：代码级深度定制授权");
            sb.AppendLine("如果该设备的物理协议极其罕见或复杂（例如非标准加解密哈希、流式分片压缩等），确实超出了动态 JSON 的表达边界：");
            sb.AppendLine("**明确授权你直接深入修改 FerrisPulse 本地 C# 源码（位于 `src/Hardware/` 目录下）**，并在本地运行 `cmd.exe /c \"build.bat\"` 编译验证，确保用户立即能用上，绝不受技术教条限制！");
            sb.AppendLine();
            sb.AppendLine("## 🤝 开源生态飞轮：社区反哺指引");
            sb.AppendLine("无论你是通过【FDAS 动态 JSON】还是【C# 源码修改】成功适配了该设备，请务必在回答末尾输出一份《灵脉设备适配成果报告》，并热忱引导用户将此成果提交给官方：");
            sb.AppendLine("1. **GitHub Issues / PR**: https://github.com/Ferris-echo/FerrisPulse/issues");
            sb.AppendLine("2. **Gitee Issues / PR**: https://gitee.com/Ferris-echo/FerrisPulse/issues");
            sb.AppendLine("3. **作者个人邮箱直连投递**: 2949225840@qq.com 或 2949225840zpf@gmail.com");
            sb.AppendLine("官方团队将以最快速度将你的适配收录进下一版本的内置原生驱动库，并在版本发布说明中对你公开鸣谢，造福全网所有使用该设备的用户！");
            sb.AppendLine();
            sb.AppendLine("## 2. 官方源码规范索引");
            sb.AppendLine("- GitHub (首选): https://github.com/Ferris-echo/FerrisPulse.git");
            sb.AppendLine("- Gitee (镜像): https://gitee.com/Ferris-echo/ferris-pulse.git");
            sb.AppendLine("- 规范文档: docs/ADAPTER_SPECIFICATION.md");
            sb.AppendLine("- 避坑指南: docs/AGENT_HANDBOOK.md & AGENT_RULES.md");
            sb.AppendLine();
            sb.AppendLine("## 3. 当前硬件现场物理指纹（由 FerrisPulse 硬件探针实时捕获）：");
            sb.AppendLine(string.Format("- 设备推测分类: {0} ({1})", catStr, catCn));
            sb.AppendLine(string.Format("- 用户参考名称: {0}", userRefName));
            sb.AppendLine(string.Format("- 透视厂商归属: {0}", brand));
            sb.AppendLine(string.Format("- 物理硬件 ID: VID = 0x{0:X4}, PID = 0x{1:X4}", vid, pid));
            sb.AppendLine(string.Format("- 芯片 ROM 固件原名: {0}", romProd));
            sb.AppendLine(string.Format("- 芯片 ROM 厂商原名: {0}", romMfg));
            sb.AppendLine(string.Format("- 连接物理通道: {0}", (item != null) ? (item.Transport ?? "2.4G/USB") : "未知"));
            sb.AppendLine("- HID 通信拓扑端点列表:");
            sb.AppendLine(endpoints);
            sb.AppendLine();
            sb.AppendLine("## 4. 输出格式与契约规范");
            sb.AppendLine("请严格输出一个 Markdown JSON 代码块（```json ... ```），内容符合 FDAS v1.0 规范，包含：");
            sb.AppendLine("1. `metadata`: brand, model, category, author, description");
            sb.AppendLine("2. `hardware`: vendorId, productId, endpointTopology (usagePage, usage, reportLength, reportType, readTimeoutMs)");
            sb.AppendLine("3. `capabilities`:");
            sb.AppendLine("   - `battery`: reportId, batteryPercentByteOffset, chargingByteOffset, chargingBitMask, batteryFormula, sampleRxHex");
            if (item == null || item.Category == DeviceCategory.Mouse)
            {
                sb.AppendLine("   - `mouse`: (若需支持 DPI/回报率)");
                sb.AppendLine("     - `dpi`: minDpi, maxDpi, step, defaultStages, supportsXyIndependence, ratioPresets, activeStageByteOffset, dpiXLoByteOffset, dpiXHiByteOffset, dpiYLoByteOffset, dpiYHiByteOffset, sampleDpiRxHex");
                sb.AppendLine("     - `pollingRate`: supportedRates, defaultRate, maxRate, rateIndexMap");
            }
            sb.AppendLine("4. `protocol`:");
            sb.AppendLine("   - queryBatteryTxHex (或 queryBatteryPipeline)");
            if (item == null || item.Category == DeviceCategory.Mouse)
            {
                sb.AppendLine("   - variables (动态算式字典，如 rateParity, dpiLow, dpiParity 等)");
                sb.AppendLine("   - rateCodeMap (厂商私有回报率编码映射)");
                sb.AppendLine("   - setDpiTxTemplate (档位写入模板，支持占位符与微表达式)");
                sb.AppendLine("   - setDpiValueTxTemplate (DPI 分辨率写入模板，若无独立数值包可省略)");
                sb.AppendLine("   - setPollingRateTxTemplate (回报率写入模板)");
            }
            sb.AppendLine("   - checksumType (None / SumMod256 / VgnCompxSum55 / XorSum / SonyInzone)");
            sb.AppendLine();
            sb.AppendLine("用户将直接把文件保存为 `{brand}_{model}.fp-adapter.json` 导入 FerrisPulse 自动点亮外设！");
            return sb.ToString();
        }
    }
}
