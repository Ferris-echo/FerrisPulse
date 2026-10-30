// ============================================================================
// 模块名称: PollingRateManagementDialog.cs
// 所属区域: UI/Forms (业务对话框层)
// 核心职责:
//   鼠标回报率快捷档位增删查改管理面板，展示硬件支持的 125Hz~8000Hz 回报率列表、
//   毫秒/微秒级响应延迟参数、主面板快捷切换档位自定义勾选、即时生效与配置持久化。
// 依赖关系: Controls.ModernButton, Controls.RoundedCard, DpiAwareForm
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
    public class PollingRateManagementDialog : DpiAwareForm
    {
        public int[] SelectedRates { get; private set; }
        public int ActiveRate { get; private set; }

        private string deviceName;
        private List<int> allAvailableRates = new List<int>();
        private List<int> chosenRates = new List<int>();
        private int currentActiveRate;
        private int maxSupportedRate;

        private Panel pnlPresetBar;
        private Panel pnlRateList;
        private Label lblError;
        private ModernButton btnSave;
        private ModernButton btnCancel;

        private List<RateItemRow> itemRows = new List<RateItemRow>();

        private class RateItemRow
        {
            public int Rate;
            public Panel RowPanel;
            public CheckBox ChkEnable;
            public Label LblRateText;
            public Label LblDelayText;
        }

        public PollingRateManagementDialog(string devName, int[] availableRates, int[] currentShortcutRates, int activeRate, float scale = 1.0f)
            : base(scale)
        {
            this.deviceName = !string.IsNullOrEmpty(devName) ? devName : "鼠标外设";
            this.currentActiveRate = activeRate > 0 ? activeRate : 1000;
            this.ActiveRate = this.currentActiveRate;

            if (availableRates != null && availableRates.Length > 0)
            {
                for (int i = 0; i < availableRates.Length; i++)
                {
                    if (!allAvailableRates.Contains(availableRates[i]))
                    {
                        allAvailableRates.Add(availableRates[i]);
                    }
                }
                allAvailableRates.Sort();
            }
            else
            {
                allAvailableRates = new List<int>(new int[] { 125, 250, 500, 1000, 2000, 4000, 8000 });
            }

            // 记录设备支持的回报率上限（如皂品 Z2 4K 版上限 4000Hz），预设按钮据此裁剪，杜绝 8K
            maxSupportedRate = allAvailableRates[allAvailableRates.Count - 1];

            if (currentShortcutRates != null && currentShortcutRates.Length > 0)
            {
                for (int i = 0; i < currentShortcutRates.Length; i++)
                {
                    if (allAvailableRates.Contains(currentShortcutRates[i]) && !chosenRates.Contains(currentShortcutRates[i]))
                    {
                        chosenRates.Add(currentShortcutRates[i]);
                    }
                }
            }
            if (chosenRates.Count == 0)
            {
                if (allAvailableRates.Count > 4)
                {
                    int skipCount = allAvailableRates.Count - 4;
                    for (int i = skipCount; i < allAvailableRates.Count; i++)
                    {
                        chosenRates.Add(allAvailableRates[i]);
                    }
                }
                else
                {
                    chosenRates = new List<int>(allAvailableRates);
                }
            }
            else if (chosenRates.Count > 5)
            {
                chosenRates.Sort();
                while (chosenRates.Count > 5)
                {
                    chosenRates.RemoveAt(0);
                }
            }

            this.SelectedRates = chosenRates.ToArray();

            InitializeUI();
        }

        private int S(int val)
        {
            return (int)Math.Round(val * dpiScale);
        }

        private string GetDelayDescription(int hz)
        {
            switch (hz)
            {
                case 8000: return "0.125 ms · 顶级微秒响应 (极致电竞)";
                case 4000: return "0.25 ms · 竞技级超低延迟";
                case 2000: return "0.5 ms · 高刷电竞高灵敏";
                case 1000: return "1.0 ms · 标准电竞回报率";
                case 500:  return "2.0 ms · 平衡续航模式";
                case 250:  return "4.0 ms · 办公省电模式";
                case 125:  return "8.0 ms · 极致节能模式";
                default:
                    double ms = 1000.0 / Math.Max(1, hz);
                    return string.Format("{0:F2} ms · 自定义回报率", ms);
            }
        }

        private void InitializeUI()
        {
            this.Text = "FerrisPulse · 回报率快捷档位管理";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.BackColor = Color.FromArgb(13, 16, 23);
            this.ForeColor = Color.White;
            try { ModernThemeHelper.ApplyModernWin11Theme(this.Handle); } catch (Exception diagEx) { Diagnostics.Log("UI/Forms/PollingRateManagementDialog.cs: 已忽略异常", diagEx); }

            int clientW = S(420);
            int clientH = S(530);
            this.ClientSize = new Size(clientW, clientH);

            int pad = S(16);

            // 1. Title Header
            Label lblTitle = new Label();
            lblTitle.Text = deviceName + " · 回报率档位配置";
            lblTitle.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(245, 248, 252);
            lblTitle.Location = new Point(pad, S(12));
            lblTitle.Size = new Size(clientW - pad * 2, S(22));
            lblTitle.BackColor = Color.Transparent;
            this.Controls.Add(lblTitle);

            // 2. Subtitle Note
            Label lblSub = new Label();
            lblSub.Text = "选择需要在主面板快捷切换的回报率档位 (最多勾选 5 档)";
            lblSub.Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Regular);
            lblSub.ForeColor = Color.FromArgb(120, 135, 155);
            lblSub.Location = new Point(pad, S(36));
            lblSub.Size = new Size(clientW - pad * 2, S(18));
            lblSub.BackColor = Color.Transparent;
            this.Controls.Add(lblSub);

            // 3. Preset Quick Buttons Bar
            pnlPresetBar = new Panel();
            pnlPresetBar.Location = new Point(pad, S(58));
            pnlPresetBar.Size = new Size(clientW - pad * 2, S(32));
            pnlPresetBar.BackColor = Color.FromArgb(18, 23, 33);
            pnlPresetBar.Paint += (s, e) =>
            {
                Rectangle r = new Rectangle(0, 0, pnlPresetBar.Width - 1, pnlPresetBar.Height - 1);
                using (GraphicsPath path = RoundedCard.GetRoundedRectangle(r, S(6)))
                {
                    using (Pen p = new Pen(Color.FromArgb(36, 46, 64), 1f))
                    {
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        e.Graphics.DrawPath(p, path);
                    }
                }
            };
            this.Controls.Add(pnlPresetBar);

            int btnW = (pnlPresetBar.Width - S(16)) / 3;

            // 预设档位按设备回报率上限动态裁剪（如皂品 Z2 4K 版上限 4000Hz，杜绝 8K 出现）
            List<int> presetStd = new List<int>(new int[] { 125, 250, 500, 1000 });
            List<int> presetHigh = new List<int>(new int[] { 1000, 2000, 4000, 8000 });
            List<int> presetAll = new List<int>(new int[] { 500, 1000, 2000, 4000, 8000 });
            if (maxSupportedRate > 0)
            {
                presetStd.RemoveAll(delegate(int r) { return r > maxSupportedRate; });
                presetHigh.RemoveAll(delegate(int r) { return r > maxSupportedRate; });
                presetAll.RemoveAll(delegate(int r) { return r > maxSupportedRate; });
            }
            string stdText = FormatPresetText("常规 {0} 档", presetStd);
            string highText = FormatPresetText("电竞 {0} 档", presetHigh);

            var btnPresetStandard = CreatePresetButton(stdText, S(4), btnW, presetStd.ToArray());
            var btnPresetHigh = CreatePresetButton(highText, S(4) + btnW + S(4), btnW, presetHigh.ToArray());

            int[] preset5Rates;
            string preset5Text;
            if (allAvailableRates.Count <= 5)
            {
                preset5Rates = allAvailableRates.ToArray();
                preset5Text = "全档位启用";
            }
            else
            {
                preset5Rates = presetAll.ToArray();
                preset5Text = FormatPresetText("全能 {0} 档", presetAll);
            }
            var btnPresetAll = CreatePresetButton(preset5Text, S(4) + (btnW + S(4)) * 2, btnW, preset5Rates);

            pnlPresetBar.Controls.Add(btnPresetStandard);
            pnlPresetBar.Controls.Add(btnPresetHigh);
            pnlPresetBar.Controls.Add(btnPresetAll);

            // 4. Rate Rows Container Card
            pnlRateList = new Panel();
            pnlRateList.Location = new Point(pad, S(98));
            pnlRateList.Size = new Size(clientW - pad * 2, S(350));
            pnlRateList.BackColor = Color.FromArgb(16, 20, 29);
            pnlRateList.AutoScroll = true;
            pnlRateList.Paint += (s, e) =>
            {
                Rectangle r = new Rectangle(0, 0, pnlRateList.Width - 1, pnlRateList.Height - 1);
                using (GraphicsPath path = RoundedCard.GetRoundedRectangle(r, S(8)))
                {
                    using (Pen p = new Pen(Color.FromArgb(32, 40, 58), 1f))
                    {
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        e.Graphics.DrawPath(p, path);
                    }
                }
            };
            this.Controls.Add(pnlRateList);

            BuildRateRows();

            // 5. Error Label
            lblError = new Label();
            lblError.Text = "";
            lblError.ForeColor = Color.FromArgb(255, 90, 90);
            lblError.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular);
            lblError.Location = new Point(pad, S(456));
            lblError.Size = new Size(clientW - pad * 2, S(18));
            lblError.BackColor = Color.Transparent;
            this.Controls.Add(lblError);
            RefreshItemRowStates();

            // 6. Action Buttons
            int btnH = S(32);
            int actionW = S(85);

            btnCancel = new ModernButton();
            btnCancel.Text = "取消";
            btnCancel.Font = new Font("Microsoft YaHei UI", 8.8F);
            btnCancel.BackColor = Color.FromArgb(24, 30, 42);
            btnCancel.HoverColor = Color.FromArgb(34, 42, 60);
            btnCancel.PressedColor = Color.FromArgb(18, 24, 34);
            btnCancel.ForeColor = Color.FromArgb(180, 190, 205);
            btnCancel.BorderColor = Color.FromArgb(50, 62, 85);
            btnCancel.CornerRadius = S(6);
            btnCancel.Size = new Size(actionW, btnH);
            btnCancel.Location = new Point(clientW - pad - (actionW + S(15)) - actionW - S(10), S(480));
            btnCancel.Click += (s, e) => {
                this.DialogResult = DialogResult.Cancel;
                this.Close();
            };
            this.Controls.Add(btnCancel);

            btnSave = new ModernButton();
            btnSave.Text = "保存并应用";
            btnSave.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold);
            btnSave.BackColor = Color.FromArgb(0, 160, 100);
            btnSave.HoverColor = Color.FromArgb(0, 185, 118);
            btnSave.PressedColor = Color.FromArgb(0, 135, 82);
            btnSave.ForeColor = Color.White;
            btnSave.BorderColor = Color.Transparent;
            btnSave.CornerRadius = S(6);
            btnSave.Size = new Size(actionW + S(15), btnH);
            btnSave.Location = new Point(clientW - pad - (actionW + S(15)), S(480));
            btnSave.Click += (s, e) => SaveAndClose();
            this.Controls.Add(btnSave);
        }

        // 生成预设按钮文本：如 "电竞 4 档 (1K~4K)"（范围随设备上限动态变化）
        private string FormatPresetText(string prefix, List<int> rates)
        {
            if (rates == null || rates.Count == 0) return string.Format(prefix, 0);
            int lo = rates[0];
            int hi = rates[rates.Count - 1];
            string loText = (lo >= 1000) ? (lo / 1000) + "K" : lo.ToString();
            string hiText = (hi >= 1000) ? (hi / 1000) + "K" : hi.ToString();
            // 注意：prefix 里已经有一个 {0}（档位数量），区间起点必须用 {1}。
            // 原来写成 " ({0}~{1})"，导致「常规 4 档 (4~125)」——区间起点显示成了档位数量。
            return string.Format(prefix + " ({1}~{2})", rates.Count, loText, hiText);
        }

        private Button CreatePresetButton(string text, int x, int width, int[] rates)
        {
            Button btn = new Button();
            btn.Text = text;
            btn.Location = new Point(x, S(3));
            btn.Size = new Size(width, S(26));
            btn.FlatStyle = FlatStyle.Flat;
            btn.FlatAppearance.BorderSize = 0;
            btn.FlatAppearance.MouseOverBackColor = Color.FromArgb(32, 42, 60);
            btn.FlatAppearance.MouseDownBackColor = Color.FromArgb(18, 25, 36);
            btn.BackColor = Color.Transparent;
            btn.ForeColor = Color.FromArgb(180, 195, 215);
            btn.Font = new Font("Microsoft YaHei UI", 7.8F, FontStyle.Regular);
            btn.Cursor = Cursors.Hand;
            btn.Click += (s, e) => ApplyPreset(rates);
            return btn;
        }

        private void ApplyPreset(int[] rates)
        {
            if (rates == null || rates.Length == 0) return;
            for (int i = 0; i < itemRows.Count; i++)
            {
                var row = itemRows[i];
                bool match = false;
                for (int j = 0; j < rates.Length; j++)
                {
                    if (rates[j] == row.Rate) { match = true; break; }
                }
                row.ChkEnable.Checked = match;
            }
            RefreshItemRowStates();
        }

        private void RefreshItemRowStates()
        {
            int checkedCount = 0;
            for (int i = 0; i < itemRows.Count; i++)
            {
                if (itemRows[i].ChkEnable.Checked) checkedCount++;
            }

            bool isFull = (checkedCount >= 5);

            for (int i = 0; i < itemRows.Count; i++)
            {
                var row = itemRows[i];
                bool isCurrentActive = (row.Rate == currentActiveRate);
                if (row.ChkEnable.Checked)
                {
                    row.ChkEnable.Enabled = true;
                    row.RowPanel.BackColor = isCurrentActive ? Color.FromArgb(24, 34, 48) : Color.FromArgb(20, 25, 36);
                    row.LblRateText.ForeColor = isCurrentActive ? Color.FromArgb(0, 230, 140) : Color.FromArgb(235, 240, 250);
                    row.LblDelayText.ForeColor = isCurrentActive ? Color.FromArgb(160, 220, 190) : Color.FromArgb(135, 148, 170);
                }
                else
                {
                    if (isFull)
                    {
                        row.ChkEnable.Enabled = false;
                        row.RowPanel.BackColor = Color.FromArgb(15, 18, 25);
                        row.LblRateText.ForeColor = Color.FromArgb(85, 95, 110);
                        row.LblDelayText.ForeColor = Color.FromArgb(65, 75, 90);
                    }
                    else
                    {
                        row.ChkEnable.Enabled = true;
                        row.RowPanel.BackColor = isCurrentActive ? Color.FromArgb(24, 34, 48) : Color.FromArgb(20, 25, 36);
                        row.LblRateText.ForeColor = isCurrentActive ? Color.FromArgb(0, 230, 140) : Color.FromArgb(235, 240, 250);
                        row.LblDelayText.ForeColor = isCurrentActive ? Color.FromArgb(160, 220, 190) : Color.FromArgb(135, 148, 170);
                    }
                }
            }

            if (isFull)
            {
                lblError.Text = "已达到快捷档位上限 (最多 5 档)，请先取消勾选其他档位。";
                lblError.ForeColor = Color.FromArgb(240, 170, 50);
            }
            else
            {
                lblError.Text = "";
            }
        }

        private void BuildRateRows()
        {
            pnlRateList.Controls.Clear();
            itemRows.Clear();

            int rowH = S(44);
            int rowPad = S(4);

            for (int i = 0; i < allAvailableRates.Count; i++)
            {
                int hz = allAvailableRates[i];
                bool isChecked = chosenRates.Contains(hz);
                bool isCurrentActive = (hz == currentActiveRate);

                Panel rowPnl = new Panel();
                rowPnl.Location = new Point(S(6), S(6) + i * (rowH + rowPad));
                rowPnl.Size = new Size(pnlRateList.Width - S(16), rowH);
                rowPnl.BackColor = isCurrentActive ? Color.FromArgb(24, 34, 48) : Color.FromArgb(20, 25, 36);
                rowPnl.Cursor = Cursors.Hand;

                int capturedHz = hz;
                rowPnl.Paint += (s, e) =>
                {
                    Rectangle r = new Rectangle(0, 0, rowPnl.Width - 1, rowPnl.Height - 1);
                    using (GraphicsPath path = RoundedCard.GetRoundedRectangle(r, S(6)))
                    {
                        Color borderClr = (capturedHz == currentActiveRate) ? Color.FromArgb(0, 180, 110) : Color.FromArgb(36, 46, 66);
                        using (Pen p = new Pen(borderClr, (capturedHz == currentActiveRate) ? 1.5f : 1f))
                        {
                            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                            e.Graphics.DrawPath(p, path);
                        }
                    }
                };

                CheckBox chk = new CheckBox();
                chk.Checked = isChecked;
                chk.Location = new Point(S(12), S(13));
                chk.Size = new Size(S(18), S(18));
                chk.BackColor = Color.Transparent;
                chk.Cursor = Cursors.Hand;
                chk.CheckedChanged += (s, e) => {
                    RefreshItemRowStates();
                };
                rowPnl.Controls.Add(chk);

                Label lblHz = new Label();
                lblHz.Text = hz + " Hz";
                lblHz.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
                lblHz.ForeColor = (capturedHz == currentActiveRate) ? Color.FromArgb(0, 230, 140) : Color.FromArgb(235, 240, 250);
                lblHz.Location = new Point(S(36), S(6));
                lblHz.Size = new Size(S(80), S(20));
                lblHz.BackColor = Color.Transparent;
                lblHz.Click += (s, e) => { if (chk.Enabled) chk.Checked = !chk.Checked; };
                rowPnl.Controls.Add(lblHz);

                Label lblDelay = new Label();
                lblDelay.Text = GetDelayDescription(hz);
                lblDelay.Font = new Font("Microsoft YaHei UI", 7.8F, FontStyle.Regular);
                lblDelay.ForeColor = (capturedHz == currentActiveRate) ? Color.FromArgb(160, 220, 190) : Color.FromArgb(135, 148, 170);
                lblDelay.Location = new Point(S(36), S(25));
                lblDelay.Size = new Size(S(240), S(16));
                lblDelay.BackColor = Color.Transparent;
                lblDelay.Click += (s, e) => { if (chk.Enabled) chk.Checked = !chk.Checked; };
                rowPnl.Controls.Add(lblDelay);

                if (isCurrentActive)
                {
                    Label lblCurrentBadge = new Label();
                    lblCurrentBadge.Text = "当前生效";
                    lblCurrentBadge.Font = new Font("Microsoft YaHei UI", 7.5F, FontStyle.Bold);
                    lblCurrentBadge.ForeColor = Color.FromArgb(0, 230, 140);
                    lblCurrentBadge.BackColor = Color.FromArgb(12, 45, 30);
                    lblCurrentBadge.TextAlign = ContentAlignment.MiddleCenter;
                    lblCurrentBadge.Location = new Point(rowPnl.Width - S(72), S(12));
                    lblCurrentBadge.Size = new Size(S(60), S(20));
                    rowPnl.Controls.Add(lblCurrentBadge);
                }

                rowPnl.Click += (s, e) => { if (chk.Enabled) chk.Checked = !chk.Checked; };

                pnlRateList.Controls.Add(rowPnl);

                RateItemRow item = new RateItemRow();
                item.Rate = hz;
                item.RowPanel = rowPnl;
                item.ChkEnable = chk;
                item.LblRateText = lblHz;
                item.LblDelayText = lblDelay;
                itemRows.Add(item);
            }
        }

        private void SaveAndClose()
        {
            List<int> selected = new List<int>();
            for (int i = 0; i < itemRows.Count; i++)
            {
                if (itemRows[i].ChkEnable.Checked)
                {
                    selected.Add(itemRows[i].Rate);
                }
            }

            if (selected.Count == 0)
            {
                lblError.Text = "请至少选择 1 个有效回报率档位！";
                return;
            }

            if (selected.Count > 5)
            {
                lblError.Text = "主面板快捷切换最多支持勾选 5 个回报率档位！";
                return;
            }

            selected.Sort();
            this.SelectedRates = selected.ToArray();
            this.DialogResult = DialogResult.OK;
            this.Close();
        }
    }
}
