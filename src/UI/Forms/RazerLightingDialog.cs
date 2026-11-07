// ============================================================================
// 模块名称: RazerLightingDialog.cs
// 所属区域: UI (二级弹窗)
// 核心职责:
//   雷蛇 Chroma 灯效控制台（Basilisk V3 Pro 系列专属）。复刻官方 Synapse 的
//   「灯光」+「滚轮」两个能力面：
//     · 整只联动 / 滚轮 / Logo / 底光 四个独立目标
//     · 静态纯色、呼吸、光谱、波浪、响应 五类灯效
//     · 全局亮度 0~100%
//     · 智能滚轮 Smart Reel 开关、滚轮加速开关、段落↔自由滚动切换
//   所有硬件写入均通过 RazerChromaHelper（BUSY 自动重试），失败时如实显示原因。
// 依赖关系: RazerChromaHelper.cs, DpiAwareForm, ModernButton
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 修改灯效选项、分区目标或滚轮开关集合时必须同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（禁止字符串插值、空条件运算符、nameof）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public sealed class RazerLightingDialog : DpiAwareForm
    {
        private readonly string deviceId;

        private readonly Label titleLabel = new Label();
        private readonly Label subtitleLabel = new Label();

        private readonly Label targetLabel = new Label();
        private readonly ComboBox targetBox = new ComboBox();

        private readonly Label effectLabel = new Label();
        private readonly ComboBox effectBox = new ComboBox();

        private readonly Label colorLabel = new Label();
        private readonly ModernButton colorButton = new ModernButton();
        private readonly Label speedLabel = new Label();
        private readonly ComboBox speedBox = new ComboBox();

        private readonly Label brightnessLabel = new Label();
        private readonly TrackBar brightnessBar = new TrackBar();
        private readonly Label brightnessValue = new Label();

        private readonly Label wheelTitle = new Label();
        private readonly CheckBox smartReelBox = new CheckBox();
        private readonly CheckBox scrollAccelBox = new CheckBox();
        private readonly Label scrollModeLabel = new Label();
        private readonly ComboBox scrollModeBox = new ComboBox();

        private readonly Label statusLabel = new Label();
        private readonly ModernButton applyButton = new ModernButton();
        private readonly ModernButton offButton = new ModernButton();
        private readonly ModernButton closeButton = new ModernButton();

        private Color selectedColor = Color.White;
        private bool busy;
        private Font layoutFont;

        private static readonly byte[] TargetIds = new byte[]
        {
            RazerChromaHelper.LedWhole,
            RazerChromaHelper.LedScrollWheel,
            RazerChromaHelper.LedLogo,
            RazerChromaHelper.LedUnderglow
        };

        private static readonly byte[] EffectIds = new byte[]
        {
            RazerChromaHelper.EffectStatic,
            RazerChromaHelper.EffectBreathing,
            RazerChromaHelper.EffectSpectrum,
            RazerChromaHelper.EffectWave,
            RazerChromaHelper.EffectReactive
        };

        public RazerLightingDialog(string selectedDeviceId, float scale) : base(scale)
        {
            if (!RazerChromaHelper.IsSupportedDeviceId(selectedDeviceId))
            {
                throw new NotSupportedException("此雷蛇设备尚未实机验证 Chroma 灯效通道。");
            }
            deviceId = selectedDeviceId;

            Text = "雷蛇灯效 · Basilisk V3 Pro";
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.CenterParent;
            BackColor = Color.FromArgb(16, 18, 22);
            ForeColor = Color.FromArgb(220, 225, 235);
            DoubleBuffered = true;

            titleLabel.Text = "Chroma 灯效控制";
            subtitleLabel.Text = "扩展矩阵协议 · 分区独立调色 · 写入失败自动重试";
            subtitleLabel.ForeColor = Color.FromArgb(145, 155, 175);

            targetLabel.Text = "调节目标";
            targetBox.Items.AddRange(new object[] { "整只鼠标（联动）", "滚轮灯", "三头蛇 Logo", "底部环绕灯带" });

            effectLabel.Text = "灯效模式";
            effectBox.Items.AddRange(new object[] { "静态纯色", "呼吸", "光谱循环", "波浪", "响应（按键余辉）" });

            colorLabel.Text = "颜色";
            speedLabel.Text = "速度";
            speedBox.Items.AddRange(new object[] { "1 · 最慢", "2 · 较慢", "3 · 中等", "4 · 最快" });

            brightnessLabel.Text = "全局亮度";
            brightnessBar.Minimum = 0;
            brightnessBar.Maximum = 100;
            brightnessBar.TickFrequency = 25;
            brightnessBar.Value = 100;
            brightnessBar.SmallChange = 5;
            brightnessBar.LargeChange = 25;
            brightnessValue.Text = "--";
            brightnessValue.ForeColor = Color.FromArgb(160, 200, 235);

            wheelTitle.Text = "滚轮物理机构";
            wheelTitle.ForeColor = Color.FromArgb(160, 200, 235);
            smartReelBox.Text = "智能滚轮（按滚动速度自动切换阻尼）";
            scrollAccelBox.Text = "滚轮加速";
            scrollModeLabel.Text = "滚轮模式";
            scrollModeBox.Items.AddRange(new object[] { "段落阻尼（有段落感）", "无阻尼（自由滚动）" });

            statusLabel.Text = "打开时读取设备当前滚轮状态；灯效参数请自行选择后应用。";
            statusLabel.ForeColor = Color.FromArgb(145, 155, 175);

            ComboBox[] selectors = new ComboBox[] { targetBox, effectBox, speedBox, scrollModeBox };
            for (int i = 0; i < selectors.Length; i++)
            {
                selectors[i].DropDownStyle = ComboBoxStyle.DropDownList;
                selectors[i].BackColor = Color.FromArgb(28, 32, 42);
                selectors[i].ForeColor = ForeColor;
                selectors[i].FlatStyle = FlatStyle.Flat;
            }

            PrepareButton(colorButton, "#FFFFFF");
            PrepareButton(applyButton, "应用灯效");
            PrepareButton(offButton, "关闭灯光");
            PrepareButton(closeButton, "关闭窗口");
            applyButton.BorderColor = Color.FromArgb(80, 200, 140);
            applyButton.TextColor = Color.FromArgb(150, 235, 190);

            Control[] children = new Control[]
            {
                titleLabel, subtitleLabel,
                targetLabel, targetBox,
                effectLabel, effectBox,
                colorLabel, colorButton, speedLabel, speedBox,
                brightnessLabel, brightnessBar, brightnessValue,
                wheelTitle, smartReelBox, scrollAccelBox, scrollModeLabel, scrollModeBox,
                statusLabel, applyButton, offButton, closeButton
            };
            Controls.AddRange(children);

            targetBox.SelectedIndex = 0;
            effectBox.SelectedIndex = 0;
            speedBox.SelectedIndex = 1;
            scrollModeBox.SelectedIndex = 0;

            effectBox.SelectedIndexChanged += delegate { UpdateAvailability(); };
            targetBox.SelectedIndexChanged += delegate { UpdateAvailability(); };
            brightnessBar.Scroll += delegate
            {
                brightnessValue.Text = brightnessBar.Value.ToString() + "%";
            };
            brightnessBar.ValueChanged += delegate
            {
                brightnessValue.Text = brightnessBar.Value.ToString() + "%";
            };
            colorButton.Click += delegate
            {
                using (ColorDialog chooser = new ColorDialog())
                {
                    chooser.Color = selectedColor;
                    chooser.FullOpen = true;
                    if (chooser.ShowDialog(this) == DialogResult.OK) SetColor(chooser.Color);
                }
            };
            applyButton.Click += delegate { RunApplyLighting(); };
            offButton.Click += delegate { RunOff(); };
            closeButton.Click += delegate { Close(); };
            smartReelBox.CheckedChanged += delegate { RunWheelToggle(); };
            scrollAccelBox.CheckedChanged += delegate { RunWheelToggle(); };
            scrollModeBox.SelectedIndexChanged += delegate { RunWheelToggle(); };
            FormClosing += delegate(object sender, FormClosingEventArgs args)
            {
                if (busy && args.CloseReason == CloseReason.UserClosing) args.Cancel = true;
            };

            SetColor(selectedColor);
            OnApplyDpiScale(dpiScale);
            UpdateAvailability();
        }

        protected override void OnShown(EventArgs args)
        {
            base.OnShown(args);
            SyncWheelStateFromDevice();
        }

        /// <summary>
        /// 从设备真实回读滚轮三开关并刷新控件。
        /// 0x94 / 0x96 / 0x97 的回读载荷已实机验证精确跟随（写入 1 读 1、写 0 读 0），
        /// 因此这里展示的是设备当前状态，而不是本地默认值。
        /// </summary>
        private void SyncWheelStateFromDevice()
        {
            if (busy) return;
            SetBusy(true);
            statusLabel.ForeColor = Color.FromArgb(190, 200, 220);
            statusLabel.Text = "正在读取设备当前滚轮状态…";

            ThreadPool.QueueUserWorkItem(delegate
            {
                int mode = RazerChromaHelper.QueryScrollMode();
                int accel = RazerChromaHelper.QueryScrollAcceleration();
                int smart = RazerChromaHelper.QuerySmartReel();

                try
                {
                    if (IsDisposed || Disposing) return;
                    BeginInvoke((Action)delegate
                    {
                        if (IsDisposed || Disposing) return;
                        if (mode >= 0) scrollModeBox.SelectedIndex = mode == RazerChromaHelper.ScrollModeFreeSpin ? 1 : 0;
                        if (accel >= 0) scrollAccelBox.Checked = accel != 0;
                        if (smart >= 0) smartReelBox.Checked = smart != 0;

                        bool allOk = mode >= 0 && accel >= 0 && smart >= 0;
                        statusLabel.ForeColor = allOk
                            ? Color.FromArgb(145, 155, 175)
                            : Color.FromArgb(255, 200, 120);
                        statusLabel.Text = allOk
                            ? "已读取设备当前滚轮状态；选择灯效参数后点击「应用灯效」。"
                            : "滚轮状态读取不完整（设备可能休眠），仍可正常写入。";
                        SetBusy(false);
                    });
                }
                catch (InvalidOperationException) { }
            });
        }

        protected override void OnApplyDpiScale(float scale)
        {
            dpiScale = scale;
            if (layoutFont == null || Math.Abs(layoutFont.Size - 9F * scale) > 0.01F)
            {
                Font previous = layoutFont;
                layoutFont = DpiScaleHelper.CreateScaledFont("Microsoft YaHei UI", 9F, FontStyle.Regular, scale);
                Font = layoutFont;
                if (previous != null) previous.Dispose();
            }

            ClientSize = Scale(new Size(520, 596));

            titleLabel.Bounds = Scale(new Rectangle(20, 14, 480, 28));
            subtitleLabel.Bounds = Scale(new Rectangle(20, 42, 480, 22));

            targetLabel.Bounds = Scale(new Rectangle(20, 74, 76, 26));
            targetBox.Bounds = Scale(new Rectangle(102, 71, 398, 30));
            effectLabel.Bounds = Scale(new Rectangle(20, 112, 76, 26));
            effectBox.Bounds = Scale(new Rectangle(102, 109, 398, 30));

            colorLabel.Bounds = Scale(new Rectangle(20, 150, 76, 26));
            colorButton.Bounds = Scale(new Rectangle(102, 147, 150, 32));
            speedLabel.Bounds = Scale(new Rectangle(266, 150, 52, 26));
            speedBox.Bounds = Scale(new Rectangle(322, 147, 178, 30));

            brightnessLabel.Bounds = Scale(new Rectangle(20, 190, 76, 26));
            brightnessBar.Bounds = Scale(new Rectangle(96, 184, 340, 40));
            brightnessValue.Bounds = Scale(new Rectangle(442, 190, 60, 26));

            wheelTitle.Bounds = Scale(new Rectangle(20, 238, 480, 24));
            smartReelBox.Bounds = Scale(new Rectangle(20, 264, 480, 26));
            scrollAccelBox.Bounds = Scale(new Rectangle(20, 292, 480, 26));
            scrollModeLabel.Bounds = Scale(new Rectangle(20, 324, 76, 26));
            scrollModeBox.Bounds = Scale(new Rectangle(102, 321, 398, 30));

            statusLabel.Bounds = Scale(new Rectangle(20, 366, 480, 60));

            applyButton.Bounds = Scale(new Rectangle(20, 540, 150, 36));
            offButton.Bounds = Scale(new Rectangle(184, 540, 150, 36));
            closeButton.Bounds = Scale(new Rectangle(350, 540, 150, 36));

            ModernButton[] buttons = new ModernButton[] { colorButton, applyButton, offButton, closeButton };
            for (int i = 0; i < buttons.Length; i++) buttons[i].CornerRadius = Scale(6);
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
            button.NormalColor = Color.FromArgb(26, 30, 40);
            button.HoverColor = Color.FromArgb(40, 46, 60);
            button.PressedColor = Color.FromArgb(18, 22, 30);
            button.BorderColor = Color.FromArgb(58, 66, 84);
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

        private byte GetSelectedTarget()
        {
            int index = targetBox.SelectedIndex;
            if (index < 0 || index >= TargetIds.Length) return TargetIds[0];
            return TargetIds[index];
        }

        private byte GetSelectedEffect()
        {
            int index = effectBox.SelectedIndex;
            if (index < 0 || index >= EffectIds.Length) return EffectIds[0];
            return EffectIds[index];
        }

        private void SetBusy(bool value)
        {
            busy = value;
            UpdateAvailability();
        }

        private void UpdateAvailability()
        {
            bool editable = !busy;
            byte effect = GetSelectedEffect();
            bool needsColor = effect == RazerChromaHelper.EffectStatic ||
                              effect == RazerChromaHelper.EffectBreathing ||
                              effect == RazerChromaHelper.EffectReactive;
            bool needsSpeed = effect == RazerChromaHelper.EffectBreathing ||
                              effect == RazerChromaHelper.EffectWave ||
                              effect == RazerChromaHelper.EffectReactive;

            targetBox.Enabled = editable;
            effectBox.Enabled = editable;
            colorButton.Enabled = editable && needsColor;
            speedBox.Enabled = editable && needsSpeed;
            brightnessBar.Enabled = editable;
            applyButton.Enabled = editable;
            offButton.Enabled = editable;
            smartReelBox.Enabled = editable;
            scrollAccelBox.Enabled = editable;
            scrollModeBox.Enabled = editable;
            closeButton.Enabled = !busy;

            // 光谱不取颜色与速度；关闭/光谱等模式明确提示
            if (effect == RazerChromaHelper.EffectSpectrum)
            {
                colorButton.BackColor = Color.FromArgb(22, 25, 32);
                speedBox.BackColor = Color.FromArgb(22, 25, 32);
            }
            else
            {
                colorButton.BackColor = Color.Transparent;
                speedBox.BackColor = Color.FromArgb(28, 32, 42);
            }
        }

        private void RunApplyLighting()
        {
            if (busy) return;
            byte target = GetSelectedTarget();
            byte effect = GetSelectedEffect();
            Color color = selectedColor;
            int speed = speedBox.SelectedIndex + 1;
            int brightness = brightnessBar.Value * 255 / 100;

            SetBusy(true);
            statusLabel.ForeColor = Color.FromArgb(190, 200, 220);
            statusLabel.Text = "正在写入灯效…";

            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = false;
                string detail = "";
                try
                {
                    if (effect == RazerChromaHelper.EffectStatic)
                    {
                        ok = RazerChromaHelper.SetStatic(target, color);
                    }
                    else if (effect == RazerChromaHelper.EffectBreathing)
                    {
                        ok = RazerChromaHelper.SetBreathing(target, color, speed);
                    }
                    else if (effect == RazerChromaHelper.EffectSpectrum)
                    {
                        ok = RazerChromaHelper.SetSpectrum(target);
                    }
                    else if (effect == RazerChromaHelper.EffectWave)
                    {
                        ok = RazerChromaHelper.SetWave(target, speed > 1 ? 1 : 0);
                    }
                    else if (effect == RazerChromaHelper.EffectReactive)
                    {
                        ok = RazerChromaHelper.SetReactive(target, color, speed);
                    }

                    if (ok)
                    {
                        bool brightOk = RazerChromaHelper.SetBrightness(target, brightness);
                        if (!brightOk) detail = "（灯效已生效，但亮度写入未确认）";
                    }
                    else
                    {
                        detail = "设备应答: " + RazerChromaHelper.LastResponseHex;
                    }
                }
                catch (Exception failure)
                {
                    ok = false;
                    detail = failure.Message;
                }

                try
                {
                    if (IsDisposed || Disposing) return;
                    BeginInvoke((Action)delegate
                    {
                        if (IsDisposed || Disposing) return;
                        if (ok)
                        {
                            statusLabel.ForeColor = Color.FromArgb(110, 210, 165);
                            statusLabel.Text = "已应用到设备。" + detail;
                        }
                        else
                        {
                            statusLabel.ForeColor = Color.FromArgb(255, 160, 135);
                            statusLabel.Text = "写入失败：" + detail;
                        }
                        SetBusy(false);
                    });
                }
                catch (InvalidOperationException) { }
            });
        }

        private void RunOff()
        {
            if (busy) return;
            byte target = GetSelectedTarget();
            SetBusy(true);
            statusLabel.ForeColor = Color.FromArgb(190, 200, 220);
            statusLabel.Text = "正在关闭灯光…";

            ThreadPool.QueueUserWorkItem(delegate
            {
                bool ok = false;
                string detail = "";
                try
                {
                    ok = RazerChromaHelper.SetOff(target);
                    if (!ok) detail = RazerChromaHelper.LastResponseHex;
                }
                catch (Exception failure) { detail = failure.Message; }

                try
                {
                    if (IsDisposed || Disposing) return;
                    BeginInvoke((Action)delegate
                    {
                        if (IsDisposed || Disposing) return;
                        statusLabel.ForeColor = ok ? Color.FromArgb(110, 210, 165) : Color.FromArgb(255, 160, 135);
                        statusLabel.Text = ok ? "该分区灯光已关闭。" : ("关闭失败：" + detail);
                        SetBusy(false);
                    });
                }
                catch (InvalidOperationException) { }
            });
        }

        private void RunWheelToggle()
        {
            if (busy) return;
            bool smartReel = smartReelBox.Checked;
            bool accel = scrollAccelBox.Checked;
            int mode = scrollModeBox.SelectedIndex == 1 ? RazerChromaHelper.ScrollModeFreeSpin : RazerChromaHelper.ScrollModeTactile;

            SetBusy(true);
            statusLabel.ForeColor = Color.FromArgb(190, 200, 220);
            statusLabel.Text = "正在写入滚轮设置…";

            ThreadPool.QueueUserWorkItem(delegate
            {
                bool okMode = RazerChromaHelper.SetScrollMode(mode);
                bool okAccel = RazerChromaHelper.SetScrollAcceleration(accel);
                bool okSmart = RazerChromaHelper.SetSmartReel(smartReel);
                bool ok = okMode && okAccel && okSmart;
                string detail = RazerChromaHelper.LastResponseHex;

                try
                {
                    if (IsDisposed || Disposing) return;
                    BeginInvoke((Action)delegate
                    {
                        if (IsDisposed || Disposing) return;
                        if (ok)
                        {
                            statusLabel.ForeColor = Color.FromArgb(110, 210, 165);
                            statusLabel.Text = "滚轮设置已应用。";
                        }
                        else
                        {
                            statusLabel.ForeColor = Color.FromArgb(255, 160, 135);
                            statusLabel.Text = "滚轮设置部分失败，设备应答: " + detail;
                        }
                        SetBusy(false);
                    });
                }
                catch (InvalidOperationException) { }
            });
        }
    }
}
