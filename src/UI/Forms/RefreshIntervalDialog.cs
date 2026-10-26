// ============================================================================
// 模块名称: RefreshIntervalDialog.cs
// 所属区域: UI/Forms (业务对话框层)
// 核心职责:
//   自动刷新频率档位设置面板，结构与 DPI 档位配置弹窗一致：
//   逐行「第 N 档 + 秒数输入框 + 单位 + ✓ 生效中胶囊」、底部提示与取消/保存按钮。
// 依赖关系: RoundedCard, DpiAwareForm, DpiActionButton
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
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class RefreshIntervalDialog : DpiAwareForm
    {
        // 默认 6 档（秒），每档可改
        private static readonly int[] DefaultSeconds = new int[] { 3, 10, 30, 60, 300, 600 };

        private readonly List<IntervalRow> rows = new List<IntervalRow>();
        private Label lblError;
        private int selectedMs;

        public int SelectedIntervalMs { get { return selectedMs; } }

        public RefreshIntervalDialog(int currentMs)
            : base(1.0f)
        {
            selectedMs = currentMs;
            BuildUi();
        }

        private int S(int v)
        {
            return (int)Math.Round(v * dpiScale);
        }

        private void BuildUi()
        {
            this.Text = "FerrisPulse · 自动刷新频率";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MinimizeBox = false;
            this.MaximizeBox = false;
            this.ShowInTaskbar = false;
            this.BackColor = Color.FromArgb(14, 17, 24);
            this.ForeColor = Color.FromArgb(240, 244, 252);
            this.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            int padX = S(18);
            int rowW = S(404);
            int y = S(14);

            Label title = new Label();
            title.Text = "自动刷新频率 · 档位设置";
            title.Font = new Font("Microsoft YaHei UI", 12.5F, FontStyle.Bold, GraphicsUnit.Point);
            title.ForeColor = Color.FromArgb(245, 248, 255);
            title.AutoSize = true;
            title.Location = new Point(padX, y);
            this.Controls.Add(title);
            y += S(30);

            Label sub = new Label();
            sub.Text = "支持 6 档可改 · 范围 3 ~ 3600 秒";
            sub.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
            sub.ForeColor = Color.FromArgb(140, 150, 168);
            sub.AutoSize = true;
            sub.Location = new Point(padX, y);
            this.Controls.Add(sub);
            y += S(28);

            int activeSec = Math.Max(3, Math.Min(3600, selectedMs / 1000));
            bool matched = false;
            for (int i = 0; i < DefaultSeconds.Length; i++)
            {
                int sec = DefaultSeconds[i];
                IntervalRow row = new IntervalRow(i + 1, sec, dpiScale);
                row.Location = new Point(padX, y);
                row.Size = new Size(rowW, S(40));
                row.Click += OnRowClick;
                this.Controls.Add(row);
                rows.Add(row);
                if (!matched && sec == activeSec)
                {
                    row.IsActive = true;
                    matched = true;
                }
                y += S(46);
            }
            if (!matched && rows.Count > 0)
            {
                rows[0].SetSeconds(activeSec);
                rows[0].IsActive = true;
            }

            Label hint = new Label();
            hint.Text = "ⓘ  保存后立即生效，配置写入本地设置。";
            hint.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular, GraphicsUnit.Point);
            hint.ForeColor = Color.FromArgb(130, 140, 158);
            hint.AutoSize = true;
            hint.Location = new Point(padX, y);
            this.Controls.Add(hint);
            y += S(26);

            lblError = new Label();
            lblError.Text = "";
            lblError.ForeColor = Color.FromArgb(255, 96, 96);
            lblError.AutoSize = true;
            lblError.Location = new Point(padX, y);
            this.Controls.Add(lblError);
            y += S(26);

            DpiActionButton btnCancel = new DpiActionButton(dpiScale);
            btnCancel.Text = "取消";
            btnCancel.NormalColor = Color.FromArgb(38, 44, 56);
            btnCancel.HoverColor = Color.FromArgb(50, 57, 71);
            btnCancel.PressedColor = Color.FromArgb(30, 35, 45);
            btnCancel.Location = new Point(padX + S(196), y);
            btnCancel.Size = new Size(S(96), S(34));
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnCancel);

            DpiActionButton btnSave = new DpiActionButton(dpiScale);
            btnSave.Text = "保存并应用";
            btnSave.NormalColor = Color.FromArgb(0, 200, 83);
            btnSave.HoverColor = Color.FromArgb(0, 230, 118);
            btnSave.PressedColor = Color.FromArgb(0, 160, 65);
            btnSave.Location = new Point(padX + S(300), y);
            btnSave.Size = new Size(S(104), S(34));
            btnSave.Click += (s, e) => OnSave();
            this.Controls.Add(btnSave);

            y += S(50);
            this.ClientSize = new Size(S(440), y);
        }

        private void OnRowClick(object sender, EventArgs e)
        {
            IntervalRow row = sender as IntervalRow;
            if (row == null) return;
            for (int i = 0; i < rows.Count; i++)
            {
                rows[i].IsActive = (rows[i] == row);
            }
            if (lblError != null) lblError.Text = "";
        }

        private void OnSave()
        {
            for (int i = 0; i < rows.Count; i++)
            {
                if (!rows[i].IsActive) continue;
                int sec = rows[i].Seconds;
                if (sec < 3 || sec > 3600)
                {
                    lblError.Text = "间隔需在 3 ~ 3600 秒之间";
                    return;
                }
                selectedMs = sec * 1000;
                this.DialogResult = DialogResult.OK;
                this.Close();
                return;
            }
            lblError.Text = "请先选择一个档位";
        }

        // ====================================================================
        // 单行：第 N 档 + 秒数输入 + 单位 + 生效中胶囊（与 DPI 行同结构）
        // ====================================================================
        private class IntervalRow : Control
        {
            public readonly int Index;
            public int Seconds;
            private readonly float scale;
            private bool isActive;
            private bool hover;
            private TextBox txt;

            public bool IsActive
            {
                get { return isActive; }
                set
                {
                    if (isActive == value) return;
                    isActive = value;
                    Invalidate();
                }
            }

            public IntervalRow(int index, int seconds, float dpiScale)
            {
                Index = index;
                Seconds = seconds;
                scale = dpiScale;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw | ControlStyles.SupportsTransparentBackColor, true);
                BackColor = Color.FromArgb(14, 17, 24);
                Cursor = Cursors.Hand;

                txt = new TextBox();
                txt.BorderStyle = BorderStyle.None;
                txt.BackColor = Color.FromArgb(10, 13, 19);
                txt.ForeColor = Color.FromArgb(245, 248, 255);
                txt.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold, GraphicsUnit.Point);
                txt.TextAlign = HorizontalAlignment.Center;
                txt.MaxLength = 4;
                txt.Text = seconds.ToString();
                txt.TextChanged += (s, e) =>
                {
                    int v;
                    if (int.TryParse(txt.Text, out v)) Seconds = v;
                };
                this.Controls.Add(txt);
                LayoutInput();
            }

            public void SetSeconds(int sec)
            {
                Seconds = sec;
                if (txt != null) txt.Text = sec.ToString();
            }

            private int S(int v) { return (int)Math.Round(v * scale); }

            protected override void OnMouseEnter(EventArgs e) { base.OnMouseEnter(e); hover = true; Invalidate(); }
            protected override void OnMouseLeave(EventArgs e) { base.OnMouseLeave(e); hover = false; Invalidate(); }
            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                LayoutInput();
            }

            private void LayoutInput()
            {
                if (txt == null) return;
                int boxW = S(74);
                int boxH = S(28);
                int boxX = S(10) + S(64) + S(8);
                int boxY = (Height - boxH) / 2;
                txt.SetBounds(boxX + S(2), boxY + (boxH - txt.PreferredHeight) / 2, boxW - S(4), txt.PreferredHeight);
            }

            protected override void OnPaint(PaintEventArgs pevent)
            {
                Graphics g = pevent.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;
                g.Clear(this.BackColor);

                Color cardBg = IsActive ? Color.FromArgb(16, 36, 26) : (hover ? Color.FromArgb(22, 28, 38) : Color.FromArgb(17, 21, 30));
                Color cardBorder = IsActive ? Color.FromArgb(0, 230, 118) : (hover ? Color.FromArgb(44, 55, 76) : Color.FromArgb(28, 35, 50));

                Rectangle rect = new Rectangle(0, 0, Width - 1, Height - 1);
                using (GraphicsPath path = RoundedCard.GetRoundedRectangle(rect, S(6)))
                {
                    using (SolidBrush b = new SolidBrush(cardBg)) g.FillPath(b, path);
                    using (Pen p = new Pen(cardBorder, IsActive ? 1.4f : 1f)) g.DrawPath(p, path);
                }

                int elemH = S(28);
                int elemY = (Height - elemH) / 2;
                int x = S(10);

                string badge = string.Format("第 {0} 档", Index);
                Rectangle rectBadge = new Rectangle(x, elemY, S(64), elemH);
                using (GraphicsPath path = RoundedCard.GetRoundedRectangle(rectBadge, S(5)))
                {
                    using (SolidBrush b = new SolidBrush(IsActive ? Color.FromArgb(0, 200, 95) : Color.FromArgb(26, 33, 45))) g.FillPath(b, path);
                }
                TextRenderer.DrawText(g, badge, this.Font, rectBadge,
                    IsActive ? Color.FromArgb(10, 24, 16) : Color.FromArgb(200, 212, 228),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                x = rectBadge.Right + S(8);

                Rectangle rectInput = new Rectangle(x, elemY, S(74), elemH);
                using (GraphicsPath path = RoundedCard.GetRoundedRectangle(rectInput, S(5)))
                {
                    using (SolidBrush b = new SolidBrush(Color.FromArgb(10, 13, 19))) g.FillPath(b, path);
                    using (Pen p = new Pen(IsActive ? Color.FromArgb(0, 230, 118) : Color.FromArgb(36, 46, 64), 1f)) g.DrawPath(p, path);
                }
                x = rectInput.Right + S(6);

                Rectangle rectUnit = new Rectangle(x, elemY, S(24), elemH);
                TextRenderer.DrawText(g, "秒", this.Font, rectUnit, Color.FromArgb(150, 160, 178),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                x = rectUnit.Right + S(10);

                string pillText = IsActive ? "✓ 生效中" : "设为激活";
                int pillW = IsActive ? S(84) : S(72);
                Rectangle rectPill = new Rectangle(x, elemY, pillW, elemH);
                using (GraphicsPath path = RoundedCard.GetRoundedRectangle(rectPill, S(4)))
                {
                    if (IsActive)
                    {
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(0, 200, 95))) g.FillPath(b, path);
                    }
                    else
                    {
                        using (SolidBrush b = new SolidBrush(Color.FromArgb(26, 33, 45))) g.FillPath(b, path);
                        using (Pen p = new Pen(Color.FromArgb(45, 55, 72), 1f)) g.DrawPath(p, path);
                    }
                }
                TextRenderer.DrawText(g, pillText, this.Font, rectPill,
                    IsActive ? Color.FromArgb(10, 24, 16) : Color.FromArgb(200, 212, 228),
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            }
        }
    }
}
