// ============================================================================
// 模块名称: VgnKeyboardForm.cs
// 所属区域: UI (Forms)
// 核心职责:
//   VGN Neon75 Ultra 有线模式灯光调节面板。读取/写入主灯 mode、亮度、速度、
//   幻彩标志与 RGB，并在写入后回读校验；2.4G Dongle 不提供设置通道。支持全局无极缩放同步。
// 依赖关系: VgnKeyboardHelper.cs, MouseBatteryInfo.cs, ModernButton.cs, DpiAwareForm.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑或新增控件时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Drawing;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class VgnKeyboardForm : DpiAwareForm
    {
        private class ModeItem
        {
            public string Name;
            public int Value;

            public ModeItem(string name, int value)
            {
                Name = name;
                Value = value;
            }

            public override string ToString()
            {
                return Name;
            }
        }

        private ComboBox cboMode;
        private TrackBar trkBrightness;
        private TrackBar trkSpeed;
        private CheckBox chkChroma;
        private ModernButton btnColor;
        private ModernButton btnApply;
        private ModernButton btnOff;
        private ModernButton btnRefresh;
        private ModernButton btnClose;
        private Label lblStatus;
        private Label lblBrightnessVal;
        private Label lblSpeedVal;
        private Label lblHint;
        private Color currentColor = Color.White;
        private int currentMode = 1;
        private bool loading;

        public VgnKeyboardForm() : this(0f)
        {
        }

        public VgnKeyboardForm(float scale) : base(scale)
        {
            BuildUI();
            DpiScaleHelper.ApplyScaledFontsToControlTree(this, this.dpiScale);
            RefreshFromDevice();
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                ModernThemeHelper.ApplyModernWin11Theme(this.Handle);
            }
            catch { }
        }

        private void BuildUI()
        {
            this.Text = "VGN Neon75 灯光调节";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color.FromArgb(18, 22, 30);
            this.ForeColor = Color.FromArgb(225, 232, 245);
            this.ClientSize = new Size(Scale(460), Scale(500));
            this.Font = new Font("Microsoft YaHei UI", 9F);

            Label lblTitle = new Label();
            lblTitle.Text = "VGN Neon75 Ultra";
            lblTitle.Font = new Font("Microsoft YaHei UI", 16F, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(0, 229, 255);
            lblTitle.SetBounds(Scale(20), Scale(16), Scale(420), Scale(32));
            this.Controls.Add(lblTitle);

            Label lblSub = new Label();
            lblSub.Text = "主灯效 · USB 有线设置通道";
            lblSub.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblSub.ForeColor = Color.FromArgb(145, 158, 178);
            lblSub.SetBounds(Scale(22), Scale(48), Scale(420), Scale(20));
            this.Controls.Add(lblSub);

            lblStatus = new Label();
            lblStatus.Text = "正在读取键盘状态…";
            lblStatus.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblStatus.ForeColor = Color.FromArgb(160, 175, 195);
            lblStatus.SetBounds(Scale(22), Scale(76), Scale(420), Scale(22));
            this.Controls.Add(lblStatus);

            Label lblMode = new Label();
            lblMode.Text = "灯效模式";
            lblMode.SetBounds(Scale(22), Scale(118), Scale(80), Scale(24));
            this.Controls.Add(lblMode);

            cboMode = new ComboBox();
            cboMode.DropDownStyle = ComboBoxStyle.DropDownList;
            cboMode.FlatStyle = FlatStyle.Flat;
            cboMode.BackColor = Color.FromArgb(28, 32, 44);
            cboMode.ForeColor = Color.FromArgb(225, 232, 245);
            cboMode.SetBounds(Scale(110), Scale(114), Scale(320), Scale(28));
            AddMode("关闭", 0);
            AddMode("常亮", 1);
            AddMode("呼吸", 2);
            AddMode("光谱", 3);
            AddMode("波浪", 4);
            AddMode("涟漪", 5);
            AddMode("星点", 6);
            AddMode("流光", 7);
            AddMode("按键阴影", 8);
            AddMode("层叠", 9);
            AddMode("正弦", 10);
            AddMode("弹簧", 11);
            AddMode("霓虹", 12);
            AddMode("辐射", 14);
            AddMode("循环", 15);
            AddMode("色彩网格", 16);
            AddMode("飘雪", 17);
            AddMode("流星", 18);
            AddMode("静雪", 19);
            cboMode.SelectedIndex = 1;
            cboMode.SelectedIndexChanged += (s, e) => OnModeChanged();
            this.Controls.Add(cboMode);

            Label lblBrightness = new Label();
            lblBrightness.Text = "亮度";
            lblBrightness.SetBounds(Scale(22), Scale(164), Scale(80), Scale(24));
            this.Controls.Add(lblBrightness);

            trkBrightness = new TrackBar();
            trkBrightness.Minimum = 0;
            trkBrightness.Maximum = 4;
            trkBrightness.TickFrequency = 1;
            trkBrightness.SmallChange = 1;
            trkBrightness.LargeChange = 1;
            trkBrightness.SetBounds(Scale(104), Scale(156), Scale(250), Scale(45));
            trkBrightness.ValueChanged += (s, e) =>
            {
                if (lblBrightnessVal != null)
                {
                    lblBrightnessVal.Text = (trkBrightness.Value * 25) + "%";
                }
            };
            this.Controls.Add(trkBrightness);

            lblBrightnessVal = new Label();
            lblBrightnessVal.Text = "75%";
            lblBrightnessVal.SetBounds(Scale(364), Scale(164), Scale(64), Scale(24));
            lblBrightnessVal.ForeColor = Color.FromArgb(0, 229, 255);
            this.Controls.Add(lblBrightnessVal);

            Label lblSpeed = new Label();
            lblSpeed.Text = "速度";
            lblSpeed.SetBounds(Scale(22), Scale(214), Scale(80), Scale(24));
            this.Controls.Add(lblSpeed);

            trkSpeed = new TrackBar();
            trkSpeed.Minimum = 0;
            trkSpeed.Maximum = 4;
            trkSpeed.TickFrequency = 1;
            trkSpeed.SmallChange = 1;
            trkSpeed.LargeChange = 1;
            trkSpeed.SetBounds(Scale(104), Scale(206), Scale(250), Scale(45));
            trkSpeed.ValueChanged += (s, e) =>
            {
                if (lblSpeedVal != null)
                {
                    lblSpeedVal.Text = SpeedText(trkSpeed.Value);
                }
            };
            this.Controls.Add(trkSpeed);

            lblSpeedVal = new Label();
            lblSpeedVal.Text = "中";
            lblSpeedVal.SetBounds(Scale(364), Scale(214), Scale(64), Scale(24));
            lblSpeedVal.ForeColor = Color.FromArgb(0, 229, 255);
            this.Controls.Add(lblSpeedVal);

            chkChroma = new CheckBox();
            chkChroma.Text = "幻彩模式（颜色由灯效自动循环，忽略下方颜色）";
            chkChroma.ForeColor = Color.FromArgb(200, 210, 225);
            chkChroma.SetBounds(Scale(110), Scale(252), Scale(320), Scale(24));
            chkChroma.CheckedChanged += (s, e) => UpdateColorEnabled();
            this.Controls.Add(chkChroma);

            Label lblColor = new Label();
            lblColor.Text = "颜色";
            lblColor.SetBounds(Scale(22), Scale(292), Scale(80), Scale(24));
            this.Controls.Add(lblColor);

            btnColor = new ModernButton();
            btnColor.Text = "#FFFFFF";
            btnColor.Font = new Font("Consolas", 9F, FontStyle.Bold);
            btnColor.NormalColor = Color.FromArgb(28, 32, 44);
            btnColor.HoverColor = Color.FromArgb(42, 48, 64);
            btnColor.PressedColor = Color.FromArgb(20, 24, 34);
            btnColor.BorderColor = Color.FromArgb(70, 90, 130);
            btnColor.TextColor = Color.White;
            btnColor.CornerRadius = Math.Max(3, Scale(6));
            btnColor.Cursor = Cursors.Hand;
            btnColor.SetBounds(Scale(110), Scale(286), Scale(130), Scale(34));
            btnColor.Click += (s, e) => PickColor();
            this.Controls.Add(btnColor);

            lblHint = new Label();
            lblHint.Text = "写入后会立即回读设备校验。灯光设置保存在键盘板载配置中，\r\n2.4G 接收器不转发设置，必须保持 Wired 档。";
            lblHint.Font = new Font("Microsoft YaHei UI", 8F);
            lblHint.ForeColor = Color.FromArgb(130, 145, 165);
            lblHint.SetBounds(Scale(22), Scale(332), Scale(420), Scale(40));
            this.Controls.Add(lblHint);

            btnApply = MakeButton("应用并校验", 22, 390, 132, Color.FromArgb(0, 190, 140));
            btnApply.Click += (s, e) => ApplyLighting(false);
            this.Controls.Add(btnApply);

            btnOff = MakeButton("关闭灯光", 164, 390, 108, Color.FromArgb(180, 70, 90));
            btnOff.Click += (s, e) => ApplyLighting(true);
            this.Controls.Add(btnOff);

            btnRefresh = MakeButton("重新读取", 282, 390, 88, Color.FromArgb(50, 120, 190));
            btnRefresh.Click += (s, e) => RefreshFromDevice();
            this.Controls.Add(btnRefresh);

            btnClose = MakeButton("关闭", 380, 390, 58, Color.FromArgb(70, 78, 92));
            btnClose.Click += (s, e) => this.Close();
            this.Controls.Add(btnClose);
        }

        private ModernButton MakeButton(string text, int x, int y, int w, Color accent)
        {
            ModernButton btn = new ModernButton();
            btn.Text = text;
            btn.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            btn.NormalColor = Color.FromArgb(28, 32, 44);
            btn.HoverColor = Color.FromArgb(42, 48, 64);
            btn.PressedColor = Color.FromArgb(20, 24, 34);
            btn.BorderColor = accent;
            btn.TextColor = accent;
            btn.CornerRadius = Math.Max(3, Scale(6));
            btn.Cursor = Cursors.Hand;
            btn.SetBounds(Scale(x), Scale(y), Scale(w), Scale(34));
            return btn;
        }

        private void AddMode(string name, int value)
        {
            cboMode.Items.Add(new ModeItem(name, value));
        }

        private static string SpeedText(int value)
        {
            if (value <= 0) return "最慢";
            if (value == 1) return "较慢";
            if (value == 2) return "中";
            if (value == 3) return "较快";
            return "最快";
        }

        private void OnModeChanged()
        {
            if (loading) return;
            ModeItem item = cboMode.SelectedItem as ModeItem;
            if (item == null) return;
            currentMode = item.Value;
            UpdateColorEnabled();
        }

        private void UpdateColorEnabled()
        {
            ModeItem item = cboMode.SelectedItem as ModeItem;
            bool off = item != null && item.Value == 0;
            bool chroma = chkChroma.Checked && !off;
            if (btnColor != null) btnColor.Enabled = !off && !chroma;
            if (chkChroma != null) chkChroma.Enabled = !off;
        }

        private void PickColor()
        {
            using (ColorDialog dlg = new ColorDialog())
            {
                dlg.Color = currentColor;
                dlg.FullOpen = true;
                if (dlg.ShowDialog(this) == DialogResult.OK)
                {
                    currentColor = dlg.Color;
                    UpdateColorButton();
                }
            }
        }

        private void UpdateColorButton()
        {
            btnColor.Text = string.Format("#{0:X2}{1:X2}{2:X2}", currentColor.R, currentColor.G, currentColor.B);
            btnColor.NormalColor = currentColor;
            btnColor.HoverColor = currentColor;
            btnColor.TextColor = (currentColor.R + currentColor.G + currentColor.B > 380) ? Color.Black : Color.White;
            btnColor.Invalidate();
        }

        private void RefreshFromDevice()
        {
            int mode;
            int uiSpeed;
            int luminance;
            bool chroma;
            int red;
            int green;
            int blue;
            if (!VgnKeyboardHelper.TryReadMainLight(out mode, out uiSpeed, out luminance,
                out chroma, out red, out green, out blue))
            {
                lblStatus.Text = "未检测到 VGN Neon75 有线端点：请把模式开关拨到 Wired 档。";
                lblStatus.ForeColor = Color.FromArgb(255, 160, 90);
                if (btnApply != null) btnApply.Enabled = false;
                if (btnOff != null) btnOff.Enabled = false;
                return;
            }

            loading = true;
            currentMode = mode;
            SelectMode(mode);
            trkBrightness.Value = Math.Max(0, Math.Min(4, luminance));
            trkSpeed.Value = Math.Max(0, Math.Min(4, uiSpeed));
            chkChroma.Checked = chroma;
            currentColor = Color.FromArgb(red, green, blue);
            UpdateColorButton();
            loading = false;

            if (btnApply != null) btnApply.Enabled = true;
            if (btnOff != null) btnOff.Enabled = true;
            lblStatus.Text = "USB 有线 · 已读取当前主灯状态 · 写入后自动回读";
            lblStatus.ForeColor = Color.FromArgb(120, 220, 190);
            UpdateColorEnabled();
        }

        private void SelectMode(int mode)
        {
            for (int i = 0; i < cboMode.Items.Count; i++)
            {
                ModeItem item = cboMode.Items[i] as ModeItem;
                if (item != null && item.Value == mode)
                {
                    cboMode.SelectedIndex = i;
                    return;
                }
            }
            ModeItem unknown = new ModeItem("未知模式 " + mode, mode);
            int index = cboMode.Items.Add(unknown);
            cboMode.SelectedIndex = index;
        }

        private void ApplyLighting(bool off)
        {
            ModeItem item = cboMode.SelectedItem as ModeItem;
            int mode = off ? 0 : (item != null ? item.Value : 1);
            int lum = trkBrightness.Value;
            int speed = trkSpeed.Value;
            bool chroma = !off && chkChroma.Checked;

            string error;
            bool ok = VgnKeyboardHelper.SetMainLight(mode, speed, lum, chroma,
                currentColor.R, currentColor.G, currentColor.B, out error);

            if (ok)
            {
                RefreshFromDevice();
                lblStatus.Text = off ? "已关闭主灯（回读校验通过）" : "已应用主灯设置（回读校验通过）";
                lblStatus.ForeColor = Color.FromArgb(120, 220, 190);
            }
            else
            {
                lblStatus.Text = "写入失败：" + (error != null ? error : "未知错误");
                lblStatus.ForeColor = Color.FromArgb(255, 120, 120);
            }
        }
    }
}
