// ============================================================================
// 模块名称: GameProfileSettingForm.cs
// 所属区域: UI/Forms (业务对话框层)
// 核心职责:
//   现代化暗黑流体游戏专属硬件性能策略设置弹窗：
//   1. 支持指定游戏在“跟随主界面默认电竞预设”与“启用独立专属策略”之间一键切换
//   2. 专属策略下直观提供 1~5 档 DPI 快捷切换按钮，高度与回报率保持高度一致，内文仅标示精准 DPI 数值
//   3. 专属策略下直观提供 125..8000 Hz 回报率按键组，根据当前连接设备动态列出可用档位
//   4. 严格保持与 FerrisPulse 统一的现代化暗黑微光视觉语言与高分屏 DPI 自适应缩放，彻底消除黑底黑字
// 依赖关系: Controls.ModernButton, Controls.RoundedCard, Models.GameProfile, Core.DpiScaleHelper
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、属性或布局时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（❌ 严禁 $"..." 插值，必须使用 string.Format 或 + 拼接）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class GameProfileSettingForm : DpiAwareForm
    {
        private string processName;
        private string gameTitle;
        private Image gameIcon;
        private GameProfile currentProfile;
        private int[] availableDpiValues;
        private int[] availablePollingRates;
        private Action<GameProfile> onSaveCallback;

        private bool isCustomMode;
        private int selectedStage;
        private int selectedRate;

        private ModernButton btnModeDefault;
        private ModernButton btnModeCustom;
        private Panel pnlCustomArea;
        private Label lblDpiTitle;
        private Label lblRateTitle;
        private Label lblHint;
        private List<ModernButton> stageButtons = new List<ModernButton>();
        private List<ModernButton> rateButtons = new List<ModernButton>();

        private int S(int val) { return (int)Math.Round(val * dpiScale); }

        public GameProfileSettingForm(float scale, string processName, string gameTitle, Image icon,
                                      GameProfile initialProfile, int[] dpiValues, int[] pollingRates,
                                      Action<GameProfile> onSave)
            : base(scale)
        {
            this.processName = processName != null ? processName : "";
            this.gameTitle = !string.IsNullOrEmpty(gameTitle) ? gameTitle : this.processName;
            this.gameIcon = icon;
            this.currentProfile = initialProfile != null ? initialProfile : new GameProfile(this.processName);
            if (string.IsNullOrEmpty(this.currentProfile.FriendlyTitle) && !string.IsNullOrEmpty(this.gameTitle) && this.gameTitle != this.processName)
            {
                this.currentProfile.FriendlyTitle = this.gameTitle;
            }
            this.availableDpiValues = (dpiValues != null && dpiValues.Length > 0) ? dpiValues : new int[] { 800, 1600, 2400, 3200, 4000 };
            this.availablePollingRates = (pollingRates != null && pollingRates.Length > 0) ? pollingRates : new int[] { 125, 500, 1000, 2000, 4000, 8000 };
            this.onSaveCallback = onSave;

            this.isCustomMode = this.currentProfile.IsCustomConfigured;
            this.selectedStage = (this.currentProfile.CustomDpiStage >= 1 && this.currentProfile.CustomDpiStage <= 5) ? this.currentProfile.CustomDpiStage : 2;
            this.selectedRate = (this.currentProfile.CustomPollingRate > 0) ? this.currentProfile.CustomPollingRate : 1000;

            InitializeFormUI();
        }

        private void InitializeFormUI()
        {
            this.Text = "FerrisPulse · 专属游戏性能策略设置";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color.FromArgb(13, 16, 23);
            this.ForeColor = Color.FromArgb(240, 245, 255);
            this.KeyPreview = true;

            int clientW = S(450);
            int pad = S(18);

            // ================= 1. Top Header (Icon + Title) =================
            int headerH = S(38);
            PictureBox picIcon = new PictureBox();
            picIcon.Location = new Point(pad, pad);
            picIcon.Size = new Size(S(36), S(36));
            picIcon.SizeMode = PictureBoxSizeMode.Zoom;
            picIcon.Image = gameIcon;
            this.Controls.Add(picIcon);

            Label lblTitle = new Label();
            lblTitle.Text = gameTitle;
            lblTitle.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(240, 245, 255);
            lblTitle.Location = new Point(pad + S(44), pad);
            lblTitle.Size = new Size(clientW - pad * 2 - S(44), S(20));
            this.Controls.Add(lblTitle);

            Label lblSub = new Label();
            lblSub.Text = "为该游戏定制专属 DPI 档位与电竞回报率";
            lblSub.Font = new Font("Segoe UI", 7.8F, FontStyle.Regular);
            lblSub.ForeColor = Color.FromArgb(130, 145, 165);
            lblSub.Location = new Point(pad + S(44), pad + S(22));
            lblSub.Size = new Size(clientW - pad * 2 - S(44), S(16));
            this.Controls.Add(lblSub);

            // ================= 2. Mode Toggle (Default vs Custom) =================
            int modeY = pad + headerH + S(10);
            int btnW = (clientW - pad * 2 - S(10)) / 2;
            int btnH = S(30);

            btnModeDefault = new ModernButton();
            btnModeDefault.Text = "● 跟随默认电竞预设";
            btnModeDefault.Font = new Font("Microsoft YaHei UI", 8.4F, FontStyle.Bold);
            btnModeDefault.CornerRadius = S(6);
            btnModeDefault.Location = new Point(pad, modeY);
            btnModeDefault.Size = new Size(btnW, btnH);
            btnModeDefault.Cursor = Cursors.Hand;
            btnModeDefault.Click += (s, e) => { isCustomMode = false; UpdateModeDisplay(); };
            this.Controls.Add(btnModeDefault);

            btnModeCustom = new ModernButton();
            btnModeCustom.Text = "○ 启用游戏专属策略";
            btnModeCustom.Font = new Font("Microsoft YaHei UI", 8.4F, FontStyle.Bold);
            btnModeCustom.CornerRadius = S(6);
            btnModeCustom.Location = new Point(pad + btnW + S(10), modeY);
            btnModeCustom.Size = new Size(btnW, btnH);
            btnModeCustom.Cursor = Cursors.Hand;
            btnModeCustom.Click += (s, e) => { isCustomMode = true; UpdateModeDisplay(); };
            this.Controls.Add(btnModeCustom);

            // ================= 3. Custom Settings Container Card =================
            int cardY = modeY + btnH + S(10);
            int cardW = clientW - pad * 2;
            int cardH = S(172);

            pnlCustomArea = new Panel();
            pnlCustomArea.Location = new Point(pad, cardY);
            pnlCustomArea.Size = new Size(cardW, cardH);
            pnlCustomArea.BackColor = Color.FromArgb(18, 22, 32);
            pnlCustomArea.Paint += (s, e) =>
            {
                e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle r = new Rectangle(0, 0, pnlCustomArea.Width - 1, pnlCustomArea.Height - 1);
                using (GraphicsPath path = RoundedCard.GetRoundedRectangle(r, S(8)))
                {
                    using (Pen p = new Pen(Color.FromArgb(36, 44, 62), 1f))
                    {
                        e.Graphics.DrawPath(p, path);
                    }
                }
            };
            this.Controls.Add(pnlCustomArea);

            // Section 3.1: DPI Stage selection (高度与回报率按钮一致为 S(28)，仅标示纯数值)
            lblDpiTitle = new Label();
            lblDpiTitle.Text = "🎯 专属 DPI 档位 (硬件按键对应):";
            lblDpiTitle.Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Bold);
            lblDpiTitle.ForeColor = Color.FromArgb(220, 235, 255);
            lblDpiTitle.Location = new Point(S(12), S(10));
            lblDpiTitle.Size = new Size(cardW - S(24), S(18));
            pnlCustomArea.Controls.Add(lblDpiTitle);

            int stageCount = 5;
            int stageBtnGap = S(6);
            int stageBtnW = (cardW - S(24) - stageBtnGap * (stageCount - 1)) / stageCount;
            int commonBtnH = S(28); // DPI 按钮与回报率按钮完全等高
            int stageBtnY = S(30);

            stageButtons.Clear();
            for (int i = 0; i < stageCount; i++)
            {
                int stNum = i + 1;
                int dpiVal = (availableDpiValues != null && i < availableDpiValues.Length) ? availableDpiValues[i] : (800 * (i + 1));
                ModernButton b = new ModernButton();
                b.Text = dpiVal.ToString(); // 仅显示精准 DPI 数值，不写第几档
                b.Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Regular);
                b.CornerRadius = S(4);
                b.Location = new Point(S(12) + i * (stageBtnW + stageBtnGap), stageBtnY);
                b.Size = new Size(stageBtnW, commonBtnH);
                b.Cursor = Cursors.Hand;
                b.Click += (s, e) =>
                {
                    selectedStage = stNum;
                    if (!isCustomMode)
                    {
                        isCustomMode = true;
                        UpdateModeDisplay();
                    }
                    else
                    {
                        UpdateButtonSelections();
                    }
                };
                stageButtons.Add(b);
                pnlCustomArea.Controls.Add(b);
            }

            // Section 3.2: Polling Rate selection (等高 S(28))
            lblRateTitle = new Label();
            lblRateTitle.Text = "⚡ 专属电竞回报率 (Polling Rate):";
            lblRateTitle.Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Bold);
            lblRateTitle.ForeColor = Color.FromArgb(220, 235, 255);
            lblRateTitle.Location = new Point(S(12), S(66));
            lblRateTitle.Size = new Size(cardW - S(24), S(18));
            pnlCustomArea.Controls.Add(lblRateTitle);

            rateButtons.Clear();
            int rateCount = availablePollingRates != null ? availablePollingRates.Length : 0;
            if (rateCount > 0)
            {
                int rateBtnGap = S(6);
                int rateBtnW = Math.Min(S(64), (cardW - S(24) - rateBtnGap * (rateCount - 1)) / rateCount);
                int rateBtnY = S(86);

                for (int i = 0; i < rateCount; i++)
                {
                    int rHz = availablePollingRates[i];
                    ModernButton rb = new ModernButton();
                    rb.Text = rHz >= 1000 ? (rHz / 1000 + "K") : (rHz.ToString());
                    rb.Font = new Font("Microsoft YaHei UI", 8.0F, FontStyle.Regular);
                    rb.CornerRadius = S(4);
                    rb.Location = new Point(S(12) + i * (rateBtnW + rateBtnGap), rateBtnY);
                    rb.Size = new Size(rateBtnW, commonBtnH);
                    rb.Cursor = Cursors.Hand;
                    rb.Click += (s, e) =>
                    {
                        selectedRate = rHz;
                        if (!isCustomMode)
                        {
                            isCustomMode = true;
                            UpdateModeDisplay();
                        }
                        else
                        {
                            UpdateButtonSelections();
                        }
                    };
                    rateButtons.Add(rb);
                    pnlCustomArea.Controls.Add(rb);
                }
            }

            lblHint = new Label();
            lblHint.Text = "💡 说明: 启动该游戏时自动下发专属参数，退出后平滑恢复至桌面预设。";
            lblHint.Font = new Font("Microsoft YaHei UI", 7.4F, FontStyle.Regular);
            lblHint.ForeColor = Color.FromArgb(120, 140, 165);
            lblHint.Location = new Point(S(12), S(126));
            lblHint.Size = new Size(cardW - S(24), S(36));
            pnlCustomArea.Controls.Add(lblHint);

            // ================= 4. Bottom Action Buttons =================
            int bottomY = cardY + cardH + S(12);
            int actionBtnW = S(95);
            int actionBtnH = S(30);

            ModernButton btnCancel = new ModernButton();
            btnCancel.Text = "取消";
            btnCancel.Font = new Font("Microsoft YaHei UI", 8.4F, FontStyle.Regular);
            btnCancel.NormalColor = Color.FromArgb(24, 30, 42);
            btnCancel.HoverColor = Color.FromArgb(34, 44, 62);
            btnCancel.BorderColor = Color.FromArgb(44, 56, 78);
            btnCancel.TextColor = Color.FromArgb(170, 185, 205);
            btnCancel.CornerRadius = S(6);
            btnCancel.Cursor = Cursors.Hand;
            btnCancel.Location = new Point(clientW - pad - actionBtnW * 2 - S(10), bottomY);
            btnCancel.Size = new Size(actionBtnW, actionBtnH);
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnCancel);

            ModernButton btnSave = new ModernButton();
            btnSave.Text = "💾 确定保存";
            btnSave.Font = new Font("Microsoft YaHei UI", 8.4F, FontStyle.Bold);
            btnSave.NormalColor = Color.FromArgb(0, 180, 90);
            btnSave.HoverColor = Color.FromArgb(0, 210, 110);
            btnSave.PressedColor = Color.FromArgb(0, 150, 75);
            btnSave.BorderColor = Color.FromArgb(0, 230, 118);
            btnSave.TextColor = Color.FromArgb(10, 25, 18);
            btnSave.CornerRadius = S(6);
            btnSave.Cursor = Cursors.Hand;
            btnSave.Location = new Point(clientW - pad - actionBtnW, bottomY);
            btnSave.Size = new Size(actionBtnW, actionBtnH);
            btnSave.Click += (s, e) => SaveProfileAndClose();
            this.Controls.Add(btnSave);

            int clientH = bottomY + actionBtnH + pad;
            this.ClientSize = new Size(clientW, clientH);

            UpdateModeDisplay();
        }

        private void UpdateModeDisplay()
        {
            if (isCustomMode)
            {
                btnModeCustom.Text = "● 启用游戏专属策略";
                btnModeCustom.NormalColor = Color.FromArgb(18, 48, 36);
                btnModeCustom.HoverColor = Color.FromArgb(24, 62, 46);
                btnModeCustom.BorderColor = Color.FromArgb(0, 230, 118);
                btnModeCustom.TextColor = Color.FromArgb(0, 230, 118);

                btnModeDefault.Text = "○ 跟随默认电竞预设";
                btnModeDefault.NormalColor = Color.FromArgb(20, 26, 36);
                btnModeDefault.HoverColor = Color.FromArgb(28, 36, 50);
                btnModeDefault.BorderColor = Color.FromArgb(36, 46, 64);
                btnModeDefault.TextColor = Color.FromArgb(140, 155, 175);

                if (lblDpiTitle != null) lblDpiTitle.ForeColor = Color.FromArgb(240, 248, 255);
                if (lblRateTitle != null) lblRateTitle.ForeColor = Color.FromArgb(240, 248, 255);
                if (lblHint != null) lblHint.ForeColor = Color.FromArgb(160, 180, 205);
            }
            else
            {
                btnModeDefault.Text = "● 跟随默认电竞预设";
                btnModeDefault.NormalColor = Color.FromArgb(20, 44, 62);
                btnModeDefault.HoverColor = Color.FromArgb(28, 58, 80);
                btnModeDefault.BorderColor = Color.FromArgb(56, 189, 248);
                btnModeDefault.TextColor = Color.FromArgb(56, 189, 248);

                btnModeCustom.Text = "○ 启用游戏专属策略";
                btnModeCustom.NormalColor = Color.FromArgb(20, 26, 36);
                btnModeCustom.HoverColor = Color.FromArgb(28, 36, 50);
                btnModeCustom.BorderColor = Color.FromArgb(36, 46, 64);
                btnModeCustom.TextColor = Color.FromArgb(140, 155, 175);

                // 绝不使用系统 GrayText，采用高对比度明晰中灰与冷灰，彻底杜绝黑底黑字
                if (lblDpiTitle != null) lblDpiTitle.ForeColor = Color.FromArgb(175, 190, 210);
                if (lblRateTitle != null) lblRateTitle.ForeColor = Color.FromArgb(175, 190, 210);
                if (lblHint != null) lblHint.ForeColor = Color.FromArgb(140, 155, 175);
            }

            btnModeDefault.Invalidate();
            btnModeCustom.Invalidate();
            UpdateButtonSelections();
        }

        private void UpdateButtonSelections()
        {
            for (int i = 0; i < stageButtons.Count; i++)
            {
                bool isSel = ((i + 1) == selectedStage);
                var b = stageButtons[i];
                if (isCustomMode)
                {
                    if (isSel)
                    {
                        b.NormalColor = Color.FromArgb(20, 52, 38);
                        b.HoverColor = Color.FromArgb(28, 68, 48);
                        b.BorderColor = Color.FromArgb(0, 230, 118);
                        b.TextColor = Color.FromArgb(0, 230, 118);
                    }
                    else
                    {
                        b.NormalColor = Color.FromArgb(24, 30, 42);
                        b.HoverColor = Color.FromArgb(32, 40, 56);
                        b.BorderColor = Color.FromArgb(44, 56, 76);
                        b.TextColor = Color.FromArgb(190, 205, 225);
                    }
                }
                else
                {
                    b.NormalColor = Color.FromArgb(22, 28, 38);
                    b.HoverColor = Color.FromArgb(30, 38, 52);
                    b.BorderColor = Color.FromArgb(36, 46, 64);
                    b.TextColor = Color.FromArgb(160, 175, 195);
                }
                b.Invalidate();
            }

            for (int i = 0; i < rateButtons.Count; i++)
            {
                int rHz = availablePollingRates[i];
                bool isSel = (rHz == selectedRate);
                var rb = rateButtons[i];
                if (isCustomMode)
                {
                    if (isSel)
                    {
                        rb.NormalColor = Color.FromArgb(18, 48, 68);
                        rb.HoverColor = Color.FromArgb(24, 60, 84);
                        rb.BorderColor = Color.FromArgb(56, 189, 248);
                        rb.TextColor = Color.FromArgb(56, 189, 248);
                    }
                    else
                    {
                        rb.NormalColor = Color.FromArgb(24, 30, 42);
                        rb.HoverColor = Color.FromArgb(32, 40, 56);
                        rb.BorderColor = Color.FromArgb(44, 56, 76);
                        rb.TextColor = Color.FromArgb(190, 205, 225);
                    }
                }
                else
                {
                    rb.NormalColor = Color.FromArgb(22, 28, 38);
                    rb.HoverColor = Color.FromArgb(30, 38, 52);
                    rb.BorderColor = Color.FromArgb(36, 46, 64);
                    rb.TextColor = Color.FromArgb(160, 175, 195);
                }
                rb.Invalidate();
            }
        }

        private void SaveProfileAndClose()
        {
            GameProfile result = new GameProfile(processName);
            result.ExePath = currentProfile != null ? currentProfile.ExePath : "";
            result.FriendlyTitle = currentProfile != null ? currentProfile.FriendlyTitle : "";
            if (isCustomMode)
            {
                result.CustomDpiStage = selectedStage;
                result.CustomPollingRate = selectedRate;
            }
            else
            {
                result.CustomDpiStage = 0;
                result.CustomPollingRate = 0;
            }

            if (onSaveCallback != null)
            {
                onSaveCallback(result);
            }
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
