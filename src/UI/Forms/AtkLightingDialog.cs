// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public sealed class AtkLightingDialog : DpiAwareForm
    {
        private readonly string deviceId;
        private readonly Label titleLabel = new Label();
        private readonly Label subtitleLabel = new Label();
        private readonly CheckBox enabledBox = new CheckBox();
        private readonly Label modeLabel = new Label();
        private readonly ComboBox modeBox = new ComboBox();
        private readonly Label brightnessLabel = new Label();
        private readonly ComboBox brightnessBox = new ComboBox();
        private readonly Label speedLabel = new Label();
        private readonly ComboBox speedBox = new ComboBox();
        private readonly Label colorLabel = new Label();
        private readonly ModernButton colorButton = new ModernButton();
        private readonly CheckBox rainbowBox = new CheckBox();
        private readonly Label hintLabel = new Label();
        private readonly Label statusLabel = new Label();
        private readonly ModernButton reloadButton = new ModernButton();
        private readonly ModernButton applyButton = new ModernButton();
        private AtkLightingSettings settings;
        private Color selectedColor = Color.White;
        private bool busy;
        private Font layoutFont;

        public AtkLightingDialog(string selectedDeviceId, float scale) : base(scale)
        {
            if (!AtkDeviceHelper.SupportsLightingDevice(selectedDeviceId)) throw new NotSupportedException("尚未实测此键盘的灯光通道。");
            deviceId = selectedDeviceId;
            Text = "键盘灯光 · ATK V75X";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(18, 19, 23);
            ForeColor = Color.FromArgb(220, 225, 235);
            DoubleBuffered = true;
            titleLabel.Text = "ATK V75X 灯光控制";
            subtitleLabel.Text = "2.4G 接收器 · 完整配置备份 · 写入后回读校验";
            subtitleLabel.ForeColor = Color.FromArgb(145, 155, 175);
            enabledBox.Text = "启用键盘灯光（总开关）";
            modeLabel.Text = "灯效模式";
            brightnessLabel.Text = "亮度";
            speedLabel.Text = "速度";
            colorLabel.Text = "单色颜色";
            rainbowBox.Text = "多彩 / 彩虹";
            hintLabel.Text = "只有点击“应用”才写入键盘；不支持的选项自动禁用。\r\n“自定义”使用键盘已有方案；“全灭”模式即使打开总开关也不会亮。";
            hintLabel.ForeColor = Color.FromArgb(145, 155, 175);
            statusLabel.Text = "等待读取键盘配置…";
            string[] effects = { "随波逐流", "彩云纷飞", "峰回路转", "光之审判", "呼吸", "常亮", "踏雪无痕", "泛起涟漪", "奔逸绝尘", "暗流涌动", "百花争艳", "流星赶月", "大鹏展翅", "川流不息", "落雨纷纷", "左右逢源", "众志成城", "速度激情", "自定义（已有方案）", "全灭" };
            for (int index = 0; index < effects.Length; index++) modeBox.Items.Add((index + 1).ToString("00") + " · " + effects[index]);
            brightnessBox.Items.AddRange(new object[] { "0%", "25%", "50%", "75%", "100%" });
            speedBox.Items.AddRange(new object[] { "1 · 最慢", "2 · 较慢", "3 · 中等", "4 · 较快", "5 · 最快" });
            ComboBox[] selectors = { modeBox, brightnessBox, speedBox };
            foreach (ComboBox selector in selectors)
            {
                selector.DropDownStyle = ComboBoxStyle.DropDownList;
                selector.BackColor = Color.FromArgb(30, 34, 44);
                selector.ForeColor = ForeColor;
                selector.FlatStyle = FlatStyle.Flat;
            }
            PrepareButton(colorButton, "#FFFFFF");
            PrepareButton(reloadButton, "重新读取");
            PrepareButton(applyButton, "应用并校验");
            applyButton.BorderColor = Color.FromArgb(195, 155, 85);
            applyButton.TextColor = Color.FromArgb(235, 200, 130);
            Control[] children = { titleLabel, subtitleLabel, enabledBox, modeLabel, modeBox, brightnessLabel, brightnessBox, speedLabel, speedBox, colorLabel, colorButton, rainbowBox, hintLabel, statusLabel, reloadButton, applyButton };
            Controls.AddRange(children);
            modeBox.SelectedIndexChanged += delegate { UpdateAvailability(); };
            rainbowBox.CheckedChanged += delegate { UpdateAvailability(); };
            colorButton.Click += delegate
            {
                using (ColorDialog chooser = new ColorDialog())
                {
                    chooser.Color = selectedColor;
                    chooser.FullOpen = true;
                    if (chooser.ShowDialog(this) == DialogResult.OK) SetColor(chooser.Color);
                }
            };
            reloadButton.Click += delegate { RunOperation(false); };
            applyButton.Click += delegate { RunOperation(true); };
            FormClosing += delegate(object sender, FormClosingEventArgs args)
            {
                if (busy && args.CloseReason == CloseReason.UserClosing) args.Cancel = true;
            };
            OnApplyDpiScale(dpiScale);
            UpdateAvailability();
        }

        protected override void OnShown(EventArgs args)
        {
            base.OnShown(args);
            RunOperation(false);
        }

        protected override void OnApplyDpiScale(float scale)
        {
            dpiScale = scale;
            if (layoutFont == null || Math.Abs(layoutFont.Size - 9F * scale) > 0.01F)
            {
                Font previous = layoutFont;
                layoutFont = new Font("Microsoft YaHei UI", 9F * scale);
                Font = layoutFont;
                if (previous != null) previous.Dispose();
            }
            ClientSize = Scale(new Size(500, 444));
            titleLabel.Bounds = Scale(new Rectangle(20, 16, 460, 28));
            subtitleLabel.Bounds = Scale(new Rectangle(20, 46, 460, 24));
            enabledBox.Bounds = Scale(new Rectangle(20, 82, 460, 28));
            modeLabel.Bounds = Scale(new Rectangle(20, 126, 90, 28));
            modeBox.Bounds = Scale(new Rectangle(126, 122, 354, 30));
            brightnessLabel.Bounds = Scale(new Rectangle(20, 170, 90, 28));
            brightnessBox.Bounds = Scale(new Rectangle(126, 166, 354, 30));
            speedLabel.Bounds = Scale(new Rectangle(20, 214, 90, 28));
            speedBox.Bounds = Scale(new Rectangle(126, 210, 354, 30));
            colorLabel.Bounds = Scale(new Rectangle(20, 258, 90, 28));
            colorButton.Bounds = Scale(new Rectangle(126, 254, 160, 32));
            rainbowBox.Bounds = Scale(new Rectangle(310, 254, 170, 32));
            hintLabel.Bounds = Scale(new Rectangle(20, 302, 460, 46));
            statusLabel.Bounds = Scale(new Rectangle(20, 350, 460, 40));
            reloadButton.Bounds = Scale(new Rectangle(20, 398, 130, 32));
            applyButton.Bounds = Scale(new Rectangle(330, 398, 150, 32));
            foreach (ModernButton button in new ModernButton[] { colorButton, reloadButton, applyButton }) button.CornerRadius = Scale(6);
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);
            if (disposing && layoutFont != null)
            {
                layoutFont.Dispose();
                layoutFont = null;
            }
        }

        private static void PrepareButton(ModernButton button, string text)
        {
            button.Text = text;
            button.NormalColor = Color.FromArgb(28, 32, 44);
            button.HoverColor = Color.FromArgb(42, 48, 64);
            button.PressedColor = Color.FromArgb(20, 24, 34);
            button.BorderColor = Color.FromArgb(60, 68, 86);
            button.TextColor = Color.FromArgb(200, 210, 230);
            button.Cursor = Cursors.Hand;
        }

        private void SetColor(Color color)
        {
            selectedColor = color;
            colorButton.Text = "#" + color.R.ToString("X2") + color.G.ToString("X2") + color.B.ToString("X2");
            colorButton.BorderColor = color;
            colorButton.Invalidate();
        }

        private void DisplaySettings(AtkLightingSettings value)
        {
            settings = value;
            enabledBox.Checked = value.Enabled;
            modeBox.SelectedIndex = value.Mode - 1;
            brightnessBox.SelectedIndex = value.Brightness;
            speedBox.SelectedIndex = value.Speed;
            rainbowBox.Checked = value.Rainbow;
            SetColor(Color.FromArgb(value.Red, value.Green, value.Blue));
            UpdateAvailability();
        }

        private void SetBusy(bool value)
        {
            busy = value;
            UpdateAvailability();
        }

        private void UpdateAvailability()
        {
            bool editable = !busy && settings != null;
            int mode = modeBox.SelectedIndex + 1;
            enabledBox.Enabled = editable;
            modeBox.Enabled = editable;
            brightnessBox.Enabled = editable && mode != 20;
            speedBox.Enabled = editable && AtkDeviceHelper.SupportsLightingSpeed(mode);
            rainbowBox.Enabled = editable && AtkDeviceHelper.SupportsLightingColor(mode);
            colorButton.Enabled = rainbowBox.Enabled && !rainbowBox.Checked;
            applyButton.Enabled = editable;
            reloadButton.Enabled = !busy;
        }

        private void RunOperation(bool apply)
        {
            if (busy || (apply && settings == null)) return;
            AtkLightingSettings target = settings;
            if (apply)
            {
                target.Enabled = enabledBox.Checked;
                target.Mode = modeBox.SelectedIndex + 1;
                target.Brightness = brightnessBox.SelectedIndex;
                target.Speed = speedBox.SelectedIndex;
                target.Rainbow = rainbowBox.Checked;
                target.Red = selectedColor.R;
                target.Green = selectedColor.G;
                target.Blue = selectedColor.B;
            }
            SetBusy(true);
            statusLabel.ForeColor = Color.FromArgb(190, 200, 220);
            statusLabel.Text = apply ? "正在写入并回读校验，请勿拔出接收器…" : "正在读取键盘配置…";
            ThreadPool.QueueUserWorkItem(delegate
            {
                AtkLightingSettings result = null;
                string error = null;
                try { result = apply ? AtkDeviceHelper.ApplyLightingSettings(deviceId, target) : AtkDeviceHelper.ReadLightingSettings(deviceId); }
                catch (Exception failure) { error = failure.Message; }
                try
                {
                    if (IsDisposed || Disposing) return;
                    BeginInvoke((Action)delegate
                    {
                        if (IsDisposed || Disposing) return;
                        if (result != null)
                        {
                            DisplaySettings(result);
                            statusLabel.ForeColor = Color.FromArgb(110, 210, 165);
                            statusLabel.Text = apply ? "已应用，完整配置回读校验通过。" : "已读取当前配置；选择参数后点击“应用”。";
                        }
                        else
                        {
                            settings = null;
                            statusLabel.ForeColor = Color.FromArgb(255, 160, 135);
                            statusLabel.Text = error;
                        }
                        SetBusy(false);
                    });
                }
                catch (InvalidOperationException) { }
            });
        }
    }
}