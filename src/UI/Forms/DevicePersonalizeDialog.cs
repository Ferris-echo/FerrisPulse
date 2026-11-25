// ============================================================================
// 模块名称: DevicePersonalizeDialog.cs
// 所属区域: UI/Forms (业务对话框层)
// 核心职责:
//   单设备个性化设置模态弹窗，与主界面深色圆角卡片语言完全一致：
//   支持圆角 ModernInputBox 自定义名称、抗锯齿圆形调色盘（带选中微光环与胶囊色值预览）、
//   独立设置「主界面主题色」与「状态栏托盘色」、默认双向联动及一键恢复品牌默认。
// 依赖关系: Controls.RoundedCard, Controls.ModernInputBox, Controls.ModernButton, Controls.ModernCheckBox, Models.MouseBatteryInfo
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、字段或排版时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（❌ 严禁 $"..." 插值、?.、nameof、out var）。
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
    public class DevicePersonalizeDialog : DpiAwareForm
    {
        private static readonly Color[] PresetColors = new Color[]
        {
            Color.FromArgb(0, 230, 118),   // 雷蛇绿
            Color.FromArgb(0, 229, 255),   // 赛博青
            Color.FromArgb(0, 174, 239),   // 罗技蓝
            Color.FromArgb(148, 108, 230), // 幻影紫
            Color.FromArgb(255, 118, 24),  // 炽焰橙
            Color.FromArgb(255, 64, 82),   // 烈焰红
            Color.FromArgb(255, 214, 0),   // 耀眼金
            Color.FromArgb(255, 86, 150),  // 魅影粉
            Color.FromArgb(236, 242, 252)  // 纯净白
        };

        private readonly Color defaultBrandColor;
        private readonly string originalName;

        private ModernInputBox txtCustomName;
        private ModernCheckBox chkLinkTrayColor;
        private Label lblTrayTitle;
        private ColorPreviewPill pillThemePreview;
        private ColorPreviewPill pillTrayPreview;
        private ModernButton btnTrayCustom;
        private readonly List<ColorSwatchCircle> themeSwatches = new List<ColorSwatchCircle>();
        private readonly List<ColorSwatchCircle> traySwatches = new List<ColorSwatchCircle>();
        private ModernButton btnReset;
        private ModernButton btnCancel;
        private ModernButton btnOk;
        private bool suppressLinkEvent;

        public string ResultCustomName { get; private set; }
        public string ResultThemeColorHex { get; private set; }
        public string ResultTrayColorHex { get; private set; }
        public bool ResultLinkTrayColorToTheme { get; private set; }

        public DevicePersonalizeDialog(
            string originalName,
            string currentCustomName,
            Color defaultBrandColor,
            string initialThemeHex,
            string initialTrayHex,
            bool linkTrayToTheme,
            float scale) : base(scale)
        {
            this.originalName = originalName ?? "";
            this.defaultBrandColor = defaultBrandColor.IsEmpty ? Color.FromArgb(0, 230, 118) : defaultBrandColor;
            this.ResultCustomName = string.IsNullOrEmpty(currentCustomName) ? this.originalName : currentCustomName;
            this.ResultThemeColorHex = initialThemeHex ?? "";
            this.ResultLinkTrayColorToTheme = linkTrayToTheme;
            this.ResultTrayColorHex = linkTrayToTheme ? this.ResultThemeColorHex : (initialTrayHex ?? "");

            this.Text = "设备个性化设置";
            int clientW = (int)(436 * dpiScale);
            int clientH = (int)(426 * dpiScale);
            this.ClientSize = new Size(clientW, clientH);
            this.BackColor = Color.FromArgb(18, 19, 23);
            this.ForeColor = Color.White;
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            int pad = (int)(18 * dpiScale);
            int innerW = clientW - (pad * 2);

            var lblTitle = new Label();
            lblTitle.Text = "设备外观与名称设置";
            lblTitle.Font = new Font("Microsoft YaHei UI", 11.2F, FontStyle.Bold, GraphicsUnit.Point);
            lblTitle.ForeColor = Color.FromArgb(240, 245, 255);
            lblTitle.Location = new Point(pad, (int)(14 * dpiScale));
            lblTitle.AutoSize = true;
            this.Controls.Add(lblTitle);

            var lblSub = new Label();
            lblSub.Text = "原始型号: " + (string.IsNullOrEmpty(this.originalName) ? "未知设备" : this.originalName);
            lblSub.Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Regular, GraphicsUnit.Point);
            lblSub.ForeColor = Color.FromArgb(142, 152, 172);
            lblSub.Location = new Point(pad, (int)(40 * dpiScale));
            lblSub.Size = new Size(innerW, (int)(18 * dpiScale));
            this.Controls.Add(lblSub);

            // Card 1: 自定义显示名称
            var cardName = new RoundedCard();
            cardName.CardColor = Color.FromArgb(25, 27, 34);
            cardName.BorderColor = Color.FromArgb(42, 46, 58);
            cardName.CornerRadius = (int)(10 * dpiScale);
            cardName.Location = new Point(pad, (int)(66 * dpiScale));
            cardName.Size = new Size(innerW, (int)(78 * dpiScale));
            this.Controls.Add(cardName);

            int cPad = (int)(14 * dpiScale);
            int cInnerW = innerW - (cPad * 2);

            var lblNamePrompt = new Label();
            lblNamePrompt.Text = "显示名称（留空恢复默认）";
            lblNamePrompt.Font = new Font("Microsoft YaHei UI", 8.6F, FontStyle.Bold, GraphicsUnit.Point);
            lblNamePrompt.ForeColor = Color.FromArgb(212, 220, 236);
            lblNamePrompt.BackColor = cardName.CardColor;
            lblNamePrompt.Location = new Point(cPad, (int)(11 * dpiScale));
            lblNamePrompt.AutoSize = true;
            cardName.Controls.Add(lblNamePrompt);

            txtCustomName = new ModernInputBox();
            txtCustomName.DpiScale = dpiScale;
            txtCustomName.Font = new Font("Microsoft YaHei UI", 9.6F, FontStyle.Regular, GraphicsUnit.Point);
            txtCustomName.Text = this.ResultCustomName;
            txtCustomName.Location = new Point(cPad, (int)(35 * dpiScale));
            txtCustomName.Size = new Size(cInnerW, (int)(32 * dpiScale));
            txtCustomName.TextChanged += (s, e) => {
                this.ResultCustomName = txtCustomName.Text.Trim();
            };
            cardName.Controls.Add(txtCustomName);

            // Card 2: 双色个性化调色盘
            var cardColors = new RoundedCard();
            cardColors.CardColor = Color.FromArgb(25, 27, 34);
            cardColors.BorderColor = Color.FromArgb(42, 46, 58);
            cardColors.CornerRadius = (int)(10 * dpiScale);
            cardColors.Location = new Point(pad, cardName.Bottom + (int)(12 * dpiScale));
            cardColors.Size = new Size(innerW, (int)(206 * dpiScale));
            this.Controls.Add(cardColors);

            // 主界面主题色区块
            int themeTop = (int)(14 * dpiScale);
            Label unusedThemeTitle;
            pillThemePreview = CreateSectionHeader(cardColors, "主界面主题色", themeTop, cPad, cInnerW, out unusedThemeTitle);
            BuildColorRow(cardColors, themeTop + (int)(30 * dpiScale), cPad, cInnerW, true);

            // 分隔线
            var divider = new Panel();
            divider.BackColor = Color.FromArgb(40, 45, 58);
            divider.Location = new Point(cPad, (int)(86 * dpiScale));
            divider.Size = new Size(cInnerW, 1);
            cardColors.Controls.Add(divider);

            // 联动开关
            chkLinkTrayColor = new ModernCheckBox();
            chkLinkTrayColor.Text = "托盘图标色跟随主界面主题色联动";
            chkLinkTrayColor.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            chkLinkTrayColor.ForeColor = Color.FromArgb(215, 224, 240);
            chkLinkTrayColor.Checked = this.ResultLinkTrayColorToTheme;
            chkLinkTrayColor.Location = new Point(cPad, (int)(96 * dpiScale));
            chkLinkTrayColor.Size = new Size(cInnerW, (int)(24 * dpiScale));
            chkLinkTrayColor.CheckedChanged += (s, e) => {
                if (suppressLinkEvent) return;
                this.ResultLinkTrayColorToTheme = chkLinkTrayColor.Checked;
                if (chkLinkTrayColor.Checked)
                {
                    this.ResultTrayColorHex = this.ResultThemeColorHex;
                }
                else if (string.IsNullOrEmpty(this.ResultTrayColorHex))
                {
                    this.ResultTrayColorHex = string.IsNullOrEmpty(this.ResultThemeColorHex)
                        ? MouseBatteryInfo.FormatColorHex(this.defaultBrandColor)
                        : this.ResultThemeColorHex;
                }
                RefreshPreviews();
            };
            cardColors.Controls.Add(chkLinkTrayColor);

            // 状态栏托盘色区块
            int trayTop = (int)(130 * dpiScale);
            pillTrayPreview = CreateSectionHeader(cardColors, "状态栏托盘数字 / 胶囊色", trayTop, cPad, cInnerW, out lblTrayTitle);
            BuildColorRow(cardColors, trayTop + (int)(30 * dpiScale), cPad, cInnerW, false);

            // 底部按钮
            int btnH = (int)(34 * dpiScale);
            int btnY = clientH - (int)(16 * dpiScale) - btnH;

            btnReset = new ModernButton();
            btnReset.Text = "恢复品牌默认";
            btnReset.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            btnReset.NormalColor = Color.FromArgb(30, 34, 44);
            btnReset.HoverColor = Color.FromArgb(44, 50, 64);
            btnReset.PressedColor = Color.FromArgb(24, 27, 35);
            btnReset.BorderColor = Color.FromArgb(54, 60, 76);
            btnReset.ForeColor = Color.FromArgb(205, 214, 230);
            btnReset.CornerRadius = (int)(8 * dpiScale);
            btnReset.Location = new Point(pad, btnY);
            btnReset.Size = new Size((int)(112 * dpiScale), btnH);
            btnReset.Click += (s, e) => ResetToBrandDefaults();
            this.Controls.Add(btnReset);

            int actW = (int)(92 * dpiScale);
            btnCancel = new ModernButton();
            btnCancel.Text = "取消";
            btnCancel.Font = new Font("Microsoft YaHei UI", 9.0F, FontStyle.Regular, GraphicsUnit.Point);
            btnCancel.NormalColor = Color.FromArgb(32, 36, 46);
            btnCancel.HoverColor = Color.FromArgb(46, 52, 66);
            btnCancel.PressedColor = Color.FromArgb(24, 28, 36);
            btnCancel.BorderColor = Color.FromArgb(56, 62, 78);
            btnCancel.ForeColor = Color.FromArgb(215, 222, 236);
            btnCancel.CornerRadius = (int)(8 * dpiScale);
            btnCancel.Location = new Point(clientW - pad - (actW * 2) - (int)(10 * dpiScale), btnY);
            btnCancel.Size = new Size(actW, btnH);
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnCancel);

            btnOk = new ModernButton();
            btnOk.Text = "保存设置";
            btnOk.Font = new Font("Microsoft YaHei UI", 9.0F, FontStyle.Bold, GraphicsUnit.Point);
            btnOk.NormalColor = Color.FromArgb(0, 200, 83);
            btnOk.HoverColor = Color.FromArgb(0, 230, 118);
            btnOk.PressedColor = Color.FromArgb(0, 160, 65);
            btnOk.ForeColor = Color.FromArgb(10, 24, 15);
            btnOk.CornerRadius = (int)(8 * dpiScale);
            btnOk.Location = new Point(clientW - pad - actW, btnY);
            btnOk.Size = new Size(actW, btnH);
            btnOk.Click += (s, e) => CommitAndClose();
            this.Controls.Add(btnOk);

            this.KeyPreview = true;
            this.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Enter) { e.SuppressKeyPress = true; CommitAndClose(); }
                else if (e.KeyCode == Keys.Escape) { e.SuppressKeyPress = true; this.DialogResult = DialogResult.Cancel; this.Close(); }
            };
            RefreshPreviews();
        }

        private void CommitAndClose()
        {
            this.ResultCustomName = txtCustomName.Text.Trim();
            this.ResultLinkTrayColorToTheme = chkLinkTrayColor.Checked;
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        private ColorPreviewPill CreateSectionHeader(RoundedCard parent, string title, int top, int left, int width, out Label titleLabel)
        {
            var lbl = new Label();
            lbl.Text = title;
            lbl.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold, GraphicsUnit.Point);
            lbl.ForeColor = Color.FromArgb(228, 235, 248);
            lbl.BackColor = parent.CardColor;
            lbl.Location = new Point(left, top + (int)(2 * dpiScale));
            lbl.AutoSize = true;
            parent.Controls.Add(lbl);
            titleLabel = lbl;

            int pillW = (int)(108 * dpiScale);
            int pillH = (int)(22 * dpiScale);
            var pill = new ColorPreviewPill(dpiScale);
            pill.Location = new Point(left + width - pillW, top);
            pill.Size = new Size(pillW, pillH);
            pill.SwatchColor = this.defaultBrandColor;
            pill.LabelText = "品牌默认";
            parent.Controls.Add(pill);

            return pill;
        }

        private void BuildColorRow(RoundedCard parent, int top, int left, int innerW, bool isThemeRow)
        {
            int chipSize = (int)(26 * dpiScale);
            int customW = (int)(66 * dpiScale);
            int rightGap = (int)(8 * dpiScale);
            int availForSwatches = innerW - customW - rightGap;
            int step = Math.Max(chipSize + 2, availForSwatches / PresetColors.Length);
            var targetList = isThemeRow ? themeSwatches : traySwatches;

            for (int i = 0; i < PresetColors.Length; i++)
            {
                Color c = PresetColors[i];
                var chip = new ColorSwatchCircle(c, dpiScale);
                chip.Size = new Size(chipSize, chipSize);
                chip.Location = new Point(left + i * step, top);
                chip.Click += (s, e) => {
                    if (isThemeRow) SetThemeColor(c);
                    else SetTrayColor(c);
                };
                targetList.Add(chip);
                parent.Controls.Add(chip);
            }

            var btnCustom = new ModernButton();
            btnCustom.Text = "自定义";
            btnCustom.Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Regular, GraphicsUnit.Point);
            btnCustom.NormalColor = Color.FromArgb(32, 36, 48);
            btnCustom.HoverColor = Color.FromArgb(46, 52, 68);
            btnCustom.PressedColor = Color.FromArgb(24, 28, 38);
            btnCustom.BorderColor = Color.FromArgb(56, 62, 80);
            btnCustom.ForeColor = Color.FromArgb(215, 224, 240);
            btnCustom.CornerRadius = (int)(13 * dpiScale);
            btnCustom.Location = new Point(left + innerW - customW, top);
            btnCustom.Size = new Size(customW, chipSize);
            btnCustom.Click += (s, e) => {
                using (var cd = new ColorDialog())
                {
                    cd.FullOpen = true;
                    cd.Color = isThemeRow ? ResolveEffectiveThemeColor() : ResolveEffectiveTrayColor();
                    if (cd.ShowDialog(this) == DialogResult.OK)
                    {
                        if (isThemeRow) SetThemeColor(cd.Color);
                        else SetTrayColor(cd.Color);
                    }
                }
            };
            if (!isThemeRow) btnTrayCustom = btnCustom;
            parent.Controls.Add(btnCustom);
        }

        public void SetThemeColor(Color color)
        {
            this.ResultThemeColorHex = MouseBatteryInfo.FormatColorHex(color);
            if (chkLinkTrayColor != null && chkLinkTrayColor.Checked)
            {
                this.ResultTrayColorHex = this.ResultThemeColorHex;
            }
            RefreshPreviews();
        }

        public void SetTrayColor(Color color)
        {
            Color effectiveTheme = ResolveEffectiveThemeColor();
            if (ResultLinkTrayColorToTheme && color.ToArgb() == effectiveTheme.ToArgb())
            {
                return;
            }
            this.ResultTrayColorHex = MouseBatteryInfo.FormatColorHex(color);
            if (chkLinkTrayColor != null && chkLinkTrayColor.Checked)
            {
                suppressLinkEvent = true;
                chkLinkTrayColor.Checked = false;
                suppressLinkEvent = false;
            }
            this.ResultLinkTrayColorToTheme = false;
            RefreshPreviews();
        }

        public void ResetToBrandDefaults()
        {
            this.ResultThemeColorHex = "";
            this.ResultTrayColorHex = "";
            this.ResultLinkTrayColorToTheme = true;
            if (chkLinkTrayColor != null)
            {
                suppressLinkEvent = true;
                chkLinkTrayColor.Checked = true;
                suppressLinkEvent = false;
            }
            RefreshPreviews();
        }

        private Color ResolveEffectiveThemeColor()
        {
            Color parsed;
            if (!string.IsNullOrEmpty(ResultThemeColorHex) && DeviceCapabilities.TryParseHexColor(ResultThemeColorHex, out parsed))
            {
                return parsed;
            }
            return defaultBrandColor;
        }

        private Color ResolveEffectiveTrayColor()
        {
            if (ResultLinkTrayColorToTheme) return ResolveEffectiveThemeColor();
            Color parsed;
            if (!string.IsNullOrEmpty(ResultTrayColorHex) && DeviceCapabilities.TryParseHexColor(ResultTrayColorHex, out parsed))
            {
                return parsed;
            }
            return ResolveEffectiveThemeColor();
        }

        private void RefreshPreviews()
        {
            Color themeCol = ResolveEffectiveThemeColor();
            Color trayCol = ResolveEffectiveTrayColor();
            bool trayDimmed = ResultLinkTrayColorToTheme;

            if (txtCustomName != null)
            {
                txtCustomName.AccentBorderColor = themeCol;
                txtCustomName.Invalidate();
            }

            if (lblTrayTitle != null)
            {
                lblTrayTitle.ForeColor = trayDimmed ? Color.FromArgb(124, 132, 148) : Color.FromArgb(228, 235, 248);
            }

            if (pillThemePreview != null)
            {
                pillThemePreview.SwatchColor = themeCol;
                pillThemePreview.IsDimmed = false;
                pillThemePreview.LabelText = string.IsNullOrEmpty(ResultThemeColorHex) ? "品牌默认" : ResultThemeColorHex;
                pillThemePreview.Invalidate();
            }

            if (pillTrayPreview != null)
            {
                pillTrayPreview.SwatchColor = trayCol;
                pillTrayPreview.IsDimmed = trayDimmed;
                pillTrayPreview.LabelText = ResultLinkTrayColorToTheme
                    ? (string.IsNullOrEmpty(ResultThemeColorHex) ? "跟随主题" : "联动 " + ResultThemeColorHex)
                    : (string.IsNullOrEmpty(ResultTrayColorHex) ? "品牌默认" : ResultTrayColorHex);
                pillTrayPreview.Invalidate();
            }

            if (btnTrayCustom != null)
            {
                btnTrayCustom.NormalColor = trayDimmed ? Color.FromArgb(24, 27, 35) : Color.FromArgb(32, 36, 48);
                btnTrayCustom.HoverColor = Color.FromArgb(46, 52, 68);
                btnTrayCustom.BorderColor = trayDimmed ? Color.FromArgb(40, 44, 56) : Color.FromArgb(56, 62, 80);
                btnTrayCustom.ForeColor = trayDimmed ? Color.FromArgb(124, 132, 148) : Color.FromArgb(215, 224, 240);
                btnTrayCustom.Invalidate();
            }

            for (int i = 0; i < themeSwatches.Count; i++)
            {
                themeSwatches[i].IsDimmed = false;
                themeSwatches[i].IsSelected = !string.IsNullOrEmpty(ResultThemeColorHex) &&
                    themeSwatches[i].SwatchColor.ToArgb() == themeCol.ToArgb();
            }
            for (int i = 0; i < traySwatches.Count; i++)
            {
                traySwatches[i].IsDimmed = trayDimmed;
                traySwatches[i].IsSelected = !trayDimmed &&
                    traySwatches[i].SwatchColor.ToArgb() == trayCol.ToArgb();
            }
        }

        private sealed class ColorSwatchCircle : Control
        {
            private readonly float scale;
            private bool isHovered;
            private bool isSelected;
            private bool isDimmed;

            public Color SwatchColor { get; private set; }
            public bool IsSelected
            {
                get { return isSelected; }
                set { if (isSelected != value) { isSelected = value; Invalidate(); } }
            }
            public bool IsDimmed
            {
                get { return isDimmed; }
                set { if (isDimmed != value) { isDimmed = value; Invalidate(); } }
            }

            public ColorSwatchCircle(Color color, float dpiScale)
            {
                this.SwatchColor = color;
                this.scale = dpiScale > 0.1f ? dpiScale : 1.0f;
                this.Cursor = Cursors.Hand;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnMouseEnter(EventArgs e) { isHovered = true; Invalidate(); base.OnMouseEnter(e); }
            protected override void OnMouseLeave(EventArgs e) { isHovered = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Color parentBg = (Parent is RoundedCard) ? ((RoundedCard)Parent).CardColor : Color.FromArgb(25, 27, 34);
                g.Clear(parentBg);

                bool dimmedRest = isDimmed && !isHovered;
                float pad = isSelected ? Math.Max(3.5f, 3.8f * scale) : (isHovered ? Math.Max(2.2f, 2.5f * scale) : Math.Max(3.0f, 3.2f * scale));
                RectangleF circleRect = new RectangleF(pad, pad, Width - pad * 2f - 1f, Height - pad * 2f - 1f);

                if (isSelected)
                {
                    RectangleF ringRect = new RectangleF(1.2f, 1.2f, Width - 3.4f, Height - 3.4f);
                    using (var ringPen = new Pen(Color.FromArgb(235, SwatchColor.R, SwatchColor.G, SwatchColor.B), Math.Max(1.5f, 1.8f * scale)))
                    {
                        g.DrawEllipse(ringPen, ringRect);
                    }
                }
                else if (isHovered)
                {
                    RectangleF ringRect = new RectangleF(1.2f, 1.2f, Width - 3.4f, Height - 3.4f);
                    using (var ringPen = new Pen(Color.FromArgb(isDimmed ? 175 : 130, 225, 235, 255), Math.Max(1.1f, 1.3f * scale)))
                    {
                        g.DrawEllipse(ringPen, ringRect);
                    }
                }

                Color fillCol = SwatchColor;
                if (dimmedRest)
                {
                    // 灰色遮罩态：降饱和度 + 叠一层半透明深灰幕布，直观提示“当前跟随主题”且仍可点击
                    int gray = (int)(SwatchColor.R * 0.299 + SwatchColor.G * 0.587 + SwatchColor.B * 0.114);
                    int mr = (int)(SwatchColor.R * 0.35 + gray * 0.65);
                    int mg = (int)(SwatchColor.G * 0.35 + gray * 0.65);
                    int mb = (int)(SwatchColor.B * 0.35 + gray * 0.65);
                    fillCol = Color.FromArgb(
                        (int)(mr * 0.48 + parentBg.R * 0.52),
                        (int)(mg * 0.48 + parentBg.G * 0.52),
                        (int)(mb * 0.48 + parentBg.B * 0.52));
                }

                using (var fill = new SolidBrush(fillCol))
                {
                    g.FillEllipse(fill, circleRect);
                }

                using (var innerRim = new Pen(Color.FromArgb(dimmedRest ? 35 : 55, 255, 255, 255), 1f))
                {
                    g.DrawEllipse(innerRim, circleRect);
                }

                if (isSelected)
                {
                    double lum = SwatchColor.R * 0.299 + SwatchColor.G * 0.587 + SwatchColor.B * 0.114;
                    Color checkCol = lum > 155 ? Color.FromArgb(18, 22, 30) : Color.White;
                    float cx = circleRect.X + circleRect.Width / 2f;
                    float cy = circleRect.Y + circleRect.Height / 2f;
                    float r = circleRect.Width * 0.22f;
                    using (var pen = new Pen(checkCol, Math.Max(1.4f, 1.7f * scale)))
                    {
                        pen.StartCap = LineCap.Round;
                        pen.EndCap = LineCap.Round;
                        g.DrawLines(pen, new PointF[] {
                            new PointF(cx - r * 1.05f, cy + r * 0.05f),
                            new PointF(cx - r * 0.2f, cy + r * 0.9f),
                            new PointF(cx + r * 1.15f, cy - r * 0.75f)
                        });
                    }
                }
            }
        }

        private sealed class ColorPreviewPill : Control
        {
            private readonly float scale;
            public Color SwatchColor { get; set; }
            public string LabelText { get; set; }
            public bool IsDimmed { get; set; }

            public ColorPreviewPill(float dpiScale)
            {
                this.scale = dpiScale > 0.1f ? dpiScale : 1.0f;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                var g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                Color parentBg = (Parent is RoundedCard) ? ((RoundedCard)Parent).CardColor : Color.FromArgb(25, 27, 34);
                g.Clear(parentBg);

                Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
                int radius = Math.Max(6, Height / 2);
                Color borderCol = IsDimmed
                    ? Color.FromArgb(46, 52, 66)
                    : Color.FromArgb(75, SwatchColor.R, SwatchColor.G, SwatchColor.B);
                using (var path = RoundedCard.GetRoundedRectangle(rect, radius))
                {
                    using (var bg = new SolidBrush(Color.FromArgb(20, 23, 31))) g.FillPath(bg, path);
                    using (var border = new Pen(borderCol, 1f)) g.DrawPath(border, path);
                }

                float dotD = Math.Max(8f, 10f * scale);
                float dotX = Math.Max(6f, 7f * scale);
                float dotY = (Height - dotD) / 2f;
                Color dotCol = IsDimmed
                    ? Color.FromArgb(
                        (int)(SwatchColor.R * 0.45 + 25 * 0.55),
                        (int)(SwatchColor.G * 0.45 + 27 * 0.55),
                        (int)(SwatchColor.B * 0.45 + 34 * 0.55))
                    : SwatchColor;
                using (var b = new SolidBrush(dotCol))
                {
                    g.FillEllipse(b, dotX, dotY, dotD, dotD);
                }

                int textX = (int)(dotX + dotD + Math.Max(4f, 5f * scale));
                Rectangle textRect = new Rectangle(textX, 0, Math.Max(10, Width - textX - 4), Height);
                Color textCol = IsDimmed ? Color.FromArgb(124, 132, 148) : Color.FromArgb(192, 202, 222);
                using (var f = DpiScaleHelper.CreateScaledFont("Microsoft YaHei UI", 7.8F, FontStyle.Regular, scale))
                {
                    TextRenderer.DrawText(g, LabelText ?? "", f, textRect, textCol,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
                }
            }
        }
    }
}
