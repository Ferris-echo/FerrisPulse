// ============================================================================
// 模块名称: DpiManagementDialog.cs
// 所属区域: UI/Forms (业务对话框层)
// 核心职责:
//   鼠标 DPI 档位增删查改管理面板，展示当前传感器型号规格能力、各档位 DPI 列表、
//   X/Y 轴独立灵敏度比例快捷调节、实时档位激活切换、输入合法性校验与硬件板载同步。
// 依赖关系: Controls.DpiStageRowControl, Controls.DpiActionButton, Controls.RoundedCard, Hardware.RazerDeviceHelper
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
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class DpiManagementDialog : DpiAwareForm
    {
        public int[] Stages { get; private set; }
        public int[] StagesY { get; private set; }
        public bool IsXyIndependent { get; private set; }
        public double XyRatio { get; private set; }
        public int ActiveStage { get; private set; }

        private string deviceName;
        private RazerDeviceHelper.RazerDpiCapability capability;
        private int minDpi;
        private int maxDpi;
        private string sensorDesc;
        private List<int> stageValuesX = new List<int>();
        private List<int> stageValuesY = new List<int>();
        private bool isXyMode = false;
        private double currentRatio = 1.0;
        private int currentActiveStage = 1;

        // 是否支持 X/Y 轴独立调节（CompX 方案如皂品/VGN 等硬件协议无双轴，置 false 隐藏 XY 栏）
        public static bool SupportsXy = true;

        // 板载最大档位数（皂品 Z2 等 CompX 方案硬件支持 6 档）
        private const int MaxStageCount = 6;

        private Panel pnlXyBar;
        private DpiActionButton btnToggleXy;
        private DpiActionButton btnRatio1_1;
        private DpiActionButton btnRatio4_3;
        private DpiActionButton btnRatio16_10;
        private DpiActionButton btnRatio133;
        private Panel pnlStages;
        private DpiActionButton btnAddStage;
        private Label lblError;
        private DpiActionButton btnSave;
        private DpiActionButton btnCancel;
        private List<DpiStageRowControl> rowControls = new List<DpiStageRowControl>();

        public DpiManagementDialog(string devName, int[] initialStages, int currentStage, RazerDeviceHelper.RazerDpiCapability cap)
            : this(devName, initialStages, null, false, currentStage, cap, 1.0f)
        {
        }

        public DpiManagementDialog(string devName, int[] initialStages, int currentStage, RazerDeviceHelper.RazerDpiCapability cap, float scale)
            : this(devName, initialStages, null, false, currentStage, cap, scale)
        {
        }

        public DpiManagementDialog(string devName, int[] initialStagesX, int[] initialStagesY, bool isXy, int currentStage, RazerDeviceHelper.RazerDpiCapability cap, float scale = 1.0f)
            : this(devName, initialStagesX, initialStagesY, isXy, 1.0, currentStage, cap, scale)
        {
        }

        public DpiManagementDialog(string devName, int[] initialStagesX, int[] initialStagesY, bool isXy, double xyRatio, int currentStage, RazerDeviceHelper.RazerDpiCapability cap, float scale = 1.0f)
            : base(scale)
        {
            this.deviceName = devName ?? "外设设备";
            this.capability = cap ?? new RazerDeviceHelper.RazerDpiCapability { MaxDpi = 35000, MinDpi = 100, Step = 1, Description = "高精度光学传感器" };
            this.minDpi = (this.capability.MinDpi > 0) ? this.capability.MinDpi : 100;
            this.maxDpi = (this.capability.MaxDpi > 0) ? this.capability.MaxDpi : 35000;
            this.sensorDesc = !string.IsNullOrEmpty(this.capability.Description) ? this.capability.Description : "高精度光学传感器";
            this.currentActiveStage = currentStage > 0 ? currentStage : 1;
            this.ActiveStage = this.currentActiveStage;
            this.isXyMode = isXy;
            this.IsXyIndependent = isXy;
            if (xyRatio > 0.1 && xyRatio < 5.0)
            {
                this.currentRatio = xyRatio;
                this.XyRatio = xyRatio;
            }

            if (scale <= 0f)
            {
                try
                {
                    using (Graphics g = this.CreateGraphics())
                    {
                        scale = g.DpiX / 96.0f;
                    }
                }
                catch { scale = 1.0f; }
            }
            if (scale < 1.0f) scale = 1.0f;
            this.dpiScale = scale;

            if (initialStagesX != null && initialStagesX.Length > 0)
            {
                for (int i = 0; i < initialStagesX.Length && i < MaxStageCount; i++)
                {
                    stageValuesX.Add(initialStagesX[i]);
                    int sy = (initialStagesY != null && i < initialStagesY.Length && initialStagesY[i] > 0) ? initialStagesY[i] : initialStagesX[i];
                    stageValuesY.Add(sy);
                }
            }
            if (stageValuesX.Count == 0)
            {
                stageValuesX.AddRange(new int[] { 400, 800, 1600, 3200, 6400 });
                stageValuesY.AddRange(new int[] { 400, 800, 1600, 3200, 6400 });
            }

            // Check if any stage has X != Y
            for (int i = 0; i < stageValuesX.Count; i++)
            {
                if (stageValuesX[i] != stageValuesY[i])
                {
                    this.isXyMode = true;
                    this.IsXyIndependent = true;
                    break;
                }
            }

            if (stageValuesX[0] > 0)
            {
                this.currentRatio = (double)stageValuesY[0] / stageValuesX[0];
            }

            InitializeUI();
        }

        private int S(int val) { return (int)Math.Round(val * dpiScale); }

        private void InitializeUI()
        {
            this.Text = "FerrisPulse · DPI 档位与板载设置";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.BackColor = Color.FromArgb(13, 16, 23);
            this.ForeColor = Color.White;
            try { ModernThemeHelper.ApplyModernWin11Theme(this.Handle); } catch { }

            int clientW = S(420);
            int clientH = S(500);
            this.ClientSize = new Size(clientW, clientH);

            int pad = S(16);

            // 1. Title Header
            Label lblTitle = new Label();
            lblTitle.Text = deviceName + " · DPI 档位配置";
            lblTitle.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(245, 248, 252);
            lblTitle.Location = new Point(pad, S(12));
            lblTitle.Size = new Size(clientW - pad * 2, S(22));
            lblTitle.BackColor = Color.Transparent;
            this.Controls.Add(lblTitle);

            // 2. Subtitle / Range Note
            Label lblSub = new Label();
            lblSub.Text = SupportsXy
                ? string.Format("支持 1~{0} 档 · 范围 {1} ~ {2} DPI (支持 X/Y 轴独立调节)", MaxStageCount, minDpi, maxDpi.ToString("N0"))
                : string.Format("支持 1~{0} 档 · 范围 {1} ~ {2} DPI", MaxStageCount, minDpi, maxDpi.ToString("N0"));
            lblSub.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular);
            lblSub.ForeColor = Color.FromArgb(120, 135, 155);
            lblSub.Location = new Point(pad, S(36));
            lblSub.Size = new Size(clientW - pad * 2, S(16));
            lblSub.BackColor = Color.Transparent;
            this.Controls.Add(lblSub);

            // 3. XY Mode & Ratio ToolBar Card（不支持 XY 时隐藏，布局上移）
            if (SupportsXy)
            {
                BuildXyBar(pad, clientW);
            }

            // 4. Stages List Container（无 XY 时上移并压缩高度）
            int stagesY = SupportsXy ? S(96) : S(56);
            int stagesH = SupportsXy ? S(245) : S(285);
            pnlStages = new Panel();
            pnlStages.Location = new Point(pad, stagesY);
            pnlStages.Size = new Size(clientW - pad * 2, stagesH);
            pnlStages.BackColor = Color.Transparent;
            pnlStages.AutoScroll = true;
            this.Controls.Add(pnlStages);

            // 5. Add Stage Button
            btnAddStage = new DpiActionButton(dpiScale);
            btnAddStage.Location = new Point(pad, S(348));
            btnAddStage.Size = new Size(clientW - pad * 2, S(30));
            btnAddStage.CornerRadius = S(6);
            btnAddStage.Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Regular);
            btnAddStage.Cursor = Cursors.Hand;
            btnAddStage.Click += (s, e) => AddStage();
            this.Controls.Add(btnAddStage);

            // 6. Error Label
            lblError = new Label();
            lblError.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular);
            lblError.ForeColor = Color.FromArgb(255, 90, 95);
            lblError.Location = new Point(pad, S(382));
            lblError.Size = new Size(clientW - pad * 2, S(16));
            lblError.BackColor = Color.Transparent;
            lblError.Text = "";
            this.Controls.Add(lblError);

            // 7. Hint Note（不支持 XY 时改为通用提示）
            Label lblHint = new Label();
            lblHint.Font = new Font("Microsoft YaHei UI", 7.8F, FontStyle.Regular);
            lblHint.ForeColor = Color.FromArgb(95, 110, 130);
            lblHint.Location = new Point(pad, S(402));
            lblHint.Size = new Size(clientW - pad * 2, S(20));
            lblHint.BackColor = Color.Transparent;
            lblHint.Text = SupportsXy
                ? "💡 XY轴独立: 专为CS2/瓦罗兰特等4:3拉伸屏设计，调低Y轴可拉平横纵手感偏差；保存即写板载。"
                : "💡 保存即写板载，实时生效。";
            this.Controls.Add(lblHint);

            // 8. Bottom Action Buttons
            int btnCancelW = S(88);
            int btnSaveW = S(148);
            int gap = S(12);
            int totalBtnW = btnCancelW + gap + btnSaveW;
            int startX = (clientW - totalBtnW) / 2;
            int btnY = S(436);

            btnCancel = new DpiActionButton(dpiScale);
            btnCancel.Text = "取消";
            btnCancel.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular);
            btnCancel.NormalColor = Color.FromArgb(24, 30, 40);
            btnCancel.HoverColor = Color.FromArgb(34, 42, 56);
            btnCancel.PressedColor = Color.FromArgb(18, 22, 30);
            btnCancel.BorderColor = Color.FromArgb(42, 52, 70);
            btnCancel.ForeColor = Color.FromArgb(200, 210, 225);
            btnCancel.CornerRadius = S(6);
            btnCancel.Location = new Point(startX, btnY);
            btnCancel.Size = new Size(btnCancelW, S(34));
            btnCancel.Cursor = Cursors.Hand;
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnCancel);

            btnSave = new DpiActionButton(dpiScale);
            btnSave.Text = "保存并写入板载";
            btnSave.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold);
            btnSave.NormalColor = Color.FromArgb(0, 200, 90);
            btnSave.HoverColor = Color.FromArgb(0, 230, 115);
            btnSave.PressedColor = Color.FromArgb(0, 160, 70);
            btnSave.BorderColor = Color.Transparent;
            btnSave.ForeColor = Color.FromArgb(10, 24, 16);
            btnSave.CornerRadius = S(6);
            btnSave.Location = new Point(startX + btnCancelW + gap, btnY);
            btnSave.Size = new Size(btnSaveW, S(34));
            btnSave.Cursor = Cursors.Hand;
            btnSave.Click += (s, e) => SaveAndClose();
            this.Controls.Add(btnSave);

            this.KeyPreview = true;
            this.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { this.DialogResult = DialogResult.Cancel; this.Close(); }
                else if (e.KeyCode == Keys.Enter) { SaveAndClose(); }
            };

            this.Shown += (s, e) =>
            {
                try { this.ActiveControl = btnSave; } catch { }
            };

            UpdateXyUiState();
            RebuildStageRows();
        }

        private void BuildXyBar(int pad, int clientW)
        {
            pnlXyBar = new Panel();
            pnlXyBar.Location = new Point(pad, S(56));
            pnlXyBar.Size = new Size(clientW - pad * 2, S(32));
            pnlXyBar.BackColor = Color.FromArgb(18, 23, 33);
            pnlXyBar.Paint += (s, e) =>
            {
                Rectangle r = new Rectangle(0, 0, pnlXyBar.Width - 1, pnlXyBar.Height - 1);
                using (GraphicsPath path = RoundedCard.GetRoundedRectangle(r, S(6)))
                {
                    using (Pen p = new Pen(Color.FromArgb(36, 46, 64), 1f))
                    {
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        e.Graphics.DrawPath(p, path);
                    }
                }
            };
            this.Controls.Add(pnlXyBar);

            btnToggleXy = new DpiActionButton(dpiScale);
            btnToggleXy.Location = new Point(S(4), S(3));
            btnToggleXy.Size = new Size(S(88), S(26));
            btnToggleXy.CornerRadius = S(4);
            btnToggleXy.Font = new Font("Microsoft YaHei UI", 7.8F, FontStyle.Bold);
            btnToggleXy.Cursor = Cursors.Hand;
            btnToggleXy.Click += (s, e) => ToggleXyMode();
            pnlXyBar.Controls.Add(btnToggleXy);

            btnRatio1_1 = CreateRatioButton("等比 1:1", S(96), S(60), 1.0);
            pnlXyBar.Controls.Add(btnRatio1_1);

            btnRatio4_3 = CreateRatioButton("4:3拉伸补偿", S(160), S(82), 0.75);
            pnlXyBar.Controls.Add(btnRatio4_3);

            btnRatio16_10 = CreateRatioButton("16:10补偿", S(246), S(74), 0.90);
            pnlXyBar.Controls.Add(btnRatio16_10);

            btnRatio133 = CreateRatioButton("反向1.33x", S(324), S(60), 1.333333);
            pnlXyBar.Controls.Add(btnRatio133);
        }

        private DpiActionButton CreateRatioButton(string text, int x, int width, double ratio)
        {
            DpiActionButton btn = new DpiActionButton(dpiScale);
            btn.Text = text;
            btn.Location = new Point(x, S(3));
            btn.Size = new Size(width, S(26));
            btn.CornerRadius = S(4);
            btn.Font = new Font("Segoe UI", 7.6F, FontStyle.Regular);
            btn.NormalColor = Color.FromArgb(24, 30, 42);
            btn.HoverColor = Color.FromArgb(34, 44, 62);
            btn.PressedColor = Color.FromArgb(18, 22, 32);
            btn.BorderColor = Color.FromArgb(40, 52, 72);
            btn.ForeColor = Color.FromArgb(160, 175, 195);
            btn.Cursor = Cursors.Hand;
            btn.Click += (s, e) => ApplyRatio(ratio);
            return btn;
        }

        private void ToggleXyMode()
        {
            SaveCurrentInputs();
            isXyMode = !isXyMode;
            this.IsXyIndependent = isXyMode;
            if (!isXyMode)
            {
                // Synchronize Y to X when locking back to 1:1
                for (int i = 0; i < stageValuesX.Count; i++)
                {
                    stageValuesY[i] = stageValuesX[i];
                }
                currentRatio = 1.0;
            }
            UpdateXyUiState();
            RebuildStageRows();
        }

        private void ApplyRatio(double ratio)
        {
            SaveCurrentInputs();
            isXyMode = true;
            this.IsXyIndependent = true;
            currentRatio = ratio;

            for (int i = 0; i < stageValuesX.Count; i++)
            {
                int valX = stageValuesX[i];
                int valY = (int)Math.Round(valX * ratio);
                if (valY < minDpi) valY = minDpi;
                if (valY > maxDpi) valY = maxDpi;
                stageValuesY[i] = valY;
            }
            UpdateXyUiState();
            RebuildStageRows();
        }

        private void UpdateXyUiState()
        {
            if (btnToggleXy == null) return;

            if (isXyMode)
            {
                btnToggleXy.Text = "🔓 独立自由";
                btnToggleXy.NormalColor = Color.FromArgb(35, 45, 65);
                btnToggleXy.HoverColor = Color.FromArgb(45, 58, 82);
                btnToggleXy.PressedColor = Color.FromArgb(28, 36, 52);
                btnToggleXy.BorderColor = Color.FromArgb(56, 189, 248);
                btnToggleXy.ForeColor = Color.FromArgb(56, 189, 248);
            }
            else
            {
                btnToggleXy.Text = "🔗 联动等比";
                btnToggleXy.NormalColor = Color.FromArgb(20, 36, 28);
                btnToggleXy.HoverColor = Color.FromArgb(26, 48, 36);
                btnToggleXy.PressedColor = Color.FromArgb(16, 28, 22);
                btnToggleXy.BorderColor = Color.FromArgb(0, 200, 95);
                btnToggleXy.ForeColor = Color.FromArgb(0, 230, 118);
            }
            btnToggleXy.Invalidate();
            if (btnRatio1_1 != null) HighlightRatioButton(btnRatio1_1, !isXyMode || Math.Abs(currentRatio - 1.0) < 0.01);
            if (btnRatio4_3 != null) HighlightRatioButton(btnRatio4_3, isXyMode && Math.Abs(currentRatio - 0.75) < 0.02);
            if (btnRatio16_10 != null) HighlightRatioButton(btnRatio16_10, isXyMode && Math.Abs(currentRatio - 0.90) < 0.02);
            if (btnRatio133 != null) HighlightRatioButton(btnRatio133, isXyMode && Math.Abs(currentRatio - 1.333333) < 0.02);
        }

        private void HighlightRatioButton(DpiActionButton btn, bool isSelected)
        {
            if (btn == null) return;
            if (isSelected)
            {
                btn.NormalColor = Color.FromArgb(20, 48, 36);
                btn.BorderColor = Color.FromArgb(0, 200, 95);
                btn.ForeColor = Color.FromArgb(0, 230, 120);
            }
            else
            {
                btn.NormalColor = Color.FromArgb(24, 30, 42);
                btn.BorderColor = Color.FromArgb(40, 52, 72);
                btn.ForeColor = Color.FromArgb(160, 175, 195);
            }
            btn.Invalidate();
        }

        private void RebuildStageRows()
        {
            pnlStages.SuspendLayout();
            pnlStages.Controls.Clear();
            rowControls.Clear();

            int rowH = S(43);
            int rowGap = S(5);
            int cardW = pnlStages.ClientSize.Width - S(2);

            for (int i = 0; i < stageValuesX.Count; i++)
            {
                int stageNum = i + 1;
                int y = i * (rowH + rowGap);
                bool isActive = (stageNum == currentActiveStage);

                int valX = stageValuesX[i];
                int valY = (i < stageValuesY.Count) ? stageValuesY[i] : valX;

                DpiStageRowControl row = new DpiStageRowControl(
                    stageNum,
                    valX,
                    valY,
                    isXyMode,
                    isActive,
                    (stageValuesX.Count > 1),
                    minDpi,
                    maxDpi,
                    dpiScale);

                row.Location = new Point(0, y);
                row.Size = new Size(cardW, rowH);

                int captureStageNum = stageNum;
                int captureIndex = i;

                row.OnActivateClicked += () =>
                {
                    SaveCurrentInputs();
                    currentActiveStage = captureStageNum;
                    this.ActiveStage = currentActiveStage;
                    RebuildStageRows();
                };

                row.OnDeleteClicked += () =>
                {
                    RemoveStage(captureIndex);
                };

                rowControls.Add(row);
                pnlStages.Controls.Add(row);
            }

            pnlStages.ResumeLayout();

            if (stageValuesX.Count < MaxStageCount)
            {
                btnAddStage.Enabled = true;
                btnAddStage.Text = string.Format("+ 添加新档位  ({0}/{1} 档)", stageValuesX.Count, MaxStageCount);
                btnAddStage.NormalColor = Color.FromArgb(22, 29, 42);
                btnAddStage.HoverColor = Color.FromArgb(30, 40, 58);
                btnAddStage.PressedColor = Color.FromArgb(18, 24, 35);
                btnAddStage.BorderColor = Color.FromArgb(44, 58, 82);
                btnAddStage.ForeColor = Color.FromArgb(56, 189, 248);
            }
            else
            {
                btnAddStage.Enabled = false;
                btnAddStage.Text = string.Format("已达到板载存储上限 ({0} 档)", MaxStageCount);
                btnAddStage.NormalColor = Color.FromArgb(18, 22, 30);
                btnAddStage.HoverColor = Color.FromArgb(18, 22, 30);
                btnAddStage.PressedColor = Color.FromArgb(18, 22, 30);
                btnAddStage.BorderColor = Color.FromArgb(32, 40, 54);
                btnAddStage.ForeColor = Color.FromArgb(90, 105, 125);
            }
            btnAddStage.Invalidate();
        }

        private void SaveCurrentInputs()
        {
            for (int i = 0; i < rowControls.Count && i < stageValuesX.Count; i++)
            {
                stageValuesX[i] = rowControls[i].DpiValue;
                stageValuesY[i] = isXyMode ? rowControls[i].DpiValueY : stageValuesX[i];
            }
        }

        private void AddStage()
        {
            if (stageValuesX.Count >= MaxStageCount) return;
            SaveCurrentInputs();
            int lastX = stageValuesX.Count > 0 ? stageValuesX[stageValuesX.Count - 1] : 800;
            int nextX = Math.Min(maxDpi, lastX + 400);
            int nextY = isXyMode ? (int)Math.Round(nextX * currentRatio) : nextX;
            if (nextY < minDpi) nextY = minDpi;
            if (nextY > maxDpi) nextY = maxDpi;
            stageValuesX.Add(nextX);
            stageValuesY.Add(nextY);
            RebuildStageRows();
        }

        private void RemoveStage(int index)
        {
            if (stageValuesX.Count <= 1) return;
            SaveCurrentInputs();
            if (index >= 0 && index < stageValuesX.Count)
            {
                int removedStageNum = index + 1;
                stageValuesX.RemoveAt(index);
                if (index < stageValuesY.Count) stageValuesY.RemoveAt(index);

                if (currentActiveStage == removedStageNum)
                {
                    currentActiveStage = Math.Min(currentActiveStage, stageValuesX.Count);
                }
                else if (currentActiveStage > removedStageNum)
                {
                    currentActiveStage--;
                }
                this.ActiveStage = currentActiveStage;
                RebuildStageRows();
            }
        }

        public void SaveAndClose()
        {
            SaveCurrentInputs();
            lblError.Text = "";

            if (stageValuesX.Count == 0)
            {
                lblError.Text = "至少需要保留 1 档 DPI。";
                return;
            }

            for (int i = 0; i < stageValuesX.Count; i++)
            {
                int vx = stageValuesX[i];
                if (vx < minDpi || vx > maxDpi)
                {
                    lblError.Text = string.Format("第 {0} 档 X 轴 DPI ({1}) 超出有效范围 ({2} ~ {3})",
                        i + 1, vx, minDpi, maxDpi.ToString("N0"));
                    return;
                }
                if (isXyMode)
                {
                    int vy = stageValuesY[i];
                    if (vy < minDpi || vy > maxDpi)
                    {
                        lblError.Text = string.Format("第 {0} 档 Y 轴 DPI ({1}) 超出有效范围 ({2} ~ {3})",
                            i + 1, vy, minDpi, maxDpi.ToString("N0"));
                        return;
                    }
                }
            }

            if (currentActiveStage < 1 || currentActiveStage > stageValuesX.Count)
            {
                currentActiveStage = 1;
            }

            this.ActiveStage = currentActiveStage;
            this.Stages = stageValuesX.ToArray();
            this.StagesY = isXyMode ? stageValuesY.ToArray() : stageValuesX.ToArray();
            this.IsXyIndependent = isXyMode;
            this.XyRatio = currentRatio;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}