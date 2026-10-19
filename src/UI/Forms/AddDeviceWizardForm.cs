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
        private WizardRadarBadge radarBadge;
        private ScanningBeamControl beamControl;
        private System.Windows.Forms.Timer autoScanDebounceTimer;
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
                    existingIds.Add(d.DeviceId);
                    if (!string.IsNullOrEmpty(d.HardwareFingerprint)) existingIds.Add(d.HardwareFingerprint);
                    if (d.DeviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase))
                    {
                        existingIds.Add(RazerDeviceHelper.NormalizeRazerDeviceId(d.DeviceId));
                    }
                }
            }

            this.Text = "添加与配置外设向导";
            this.ClientSize = new Size((int)(420 * dpiScale), (int)(510 * dpiScale));
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
            lstDevices.Location = new Point(pad, (int)(73 * dpiScale));
            lstDevices.Size = new Size(this.ClientSize.Width - (pad * 2), (int)(180 * dpiScale));
            lstDevices.DrawItem += LstDevices_DrawItem;
            lstDevices.SelectedIndexChanged += (s, e) => OnDeviceSelected();
            lstDevices.HandleCreated += (s, e) => {
                try { ModernThemeHelper.SetWindowTheme(lstDevices.Handle, "DarkMode_Explorer", null); } catch { }
            };
            this.Controls.Add(lstDevices);

            // Row 4: Custom Nickname
            int formY = (int)(264 * dpiScale);
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
            formY += (int)(38 * dpiScale);
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
            formY += (int)(42 * dpiScale);
            lblTestStatus = new Label();
            lblTestStatus.Text = "请从上方列表选择一个设备";
            lblTestStatus.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold);
            lblTestStatus.ForeColor = Color.FromArgb(0, 230, 118);
            lblTestStatus.Location = new Point(pad, formY);
            lblTestStatus.Size = new Size(this.ClientSize.Width - (pad * 2), (int)(46 * dpiScale));
            this.Controls.Add(lblTestStatus);

            // Row 7: Buttons
            int btnH = (int)(36 * dpiScale);
            int btnY = (int)(456 * dpiScale);
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
            bool already = existingIds.Contains(item.DeviceId) || 
                           (!string.IsNullOrEmpty(item.HardwareFingerprint) && existingIds.Contains(item.HardwareFingerprint)) ||
                           (item.DeviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase) && 
                            existingIds.Contains(RazerDeviceHelper.NormalizeRazerDeviceId(item.DeviceId)));

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



                // 2.6 Scan Generic HID Mice & Peripherals (2.4G / USB)
                try
                {
                    var genMice = GenericHidHelper.ScanGenericHidMice(existingIds);
                    foreach (var gm in genMice)
                    {
                        tempConfigs.Add(gm);
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
                bool already = existingIds.Contains(item.DeviceId) || 
                               (!string.IsNullOrEmpty(item.HardwareFingerprint) && existingIds.Contains(item.HardwareFingerprint)) ||
                               (item.DeviceId.StartsWith("Razer:1532:", StringComparison.OrdinalIgnoreCase) && 
                                existingIds.Contains(RazerDeviceHelper.NormalizeRazerDeviceId(item.DeviceId)));

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
    }
}