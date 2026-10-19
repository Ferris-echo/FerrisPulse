// ============================================================================
// 模块名称: MouseRateTestDialog.cs
// 所属区域: UI/Forms (业务对话框层)
// 核心职责:
//   鼠标回报率与运动轨迹实时测试窗口，采用 Win32 Raw Input 原始输入技术，实时统计并可视化当前回报率、峰值回报率、平均回报率及轨迹画布。
// 依赖关系: Controls.ModernButton, Controls.RoundedCard, NativeMethods
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、属性或绘图逻辑时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（❌ 严禁 $"..." 插值，必须使用 string.Format 或 + 拼接）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class MouseRateTestDialog : DpiAwareForm
    {
        private Label lblLiveRate;
        private Label lblPeakRate;
        private Label lblAvgRate;
        private Panel motionCanvas;
        private ModernButton btnClose;
        private ModernButton btnClear;

        private long lastMotionTick = 0;
        private int peakHz = 0;
        private double avgHz = 0;
        private int sampleCount = 0;
        private Queue<double> recentHz = new Queue<double>();
        private List<Point> trail = new List<Point>();

        public MouseRateTestDialog() : this(0f)
        {
        }

        public MouseRateTestDialog(float scale) : base(scale)
        {
            this.Text = "FerrisPulse · 鼠标微秒级实时回报率 (Polling Rate) 测速台";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(16, 20, 28);
            this.ForeColor = Color.FromArgb(240, 245, 255);
            this.ClientSize = new Size(Scale(460), Scale(340));
            this.KeyPreview = true;

            int pad = Scale(16);

            Label lblTitle = new Label();
            lblTitle.Text = "⚡ 鼠标硬件物理回报率实测 (在方框内划动鼠标即可测量)";
            lblTitle.Font = new Font("Microsoft YaHei UI", 10F, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(0, 230, 118);
            lblTitle.SetBounds(pad, Scale(12), ClientSize.Width - (pad * 2), Scale(24));
            this.Controls.Add(lblTitle);

            int cardW = (ClientSize.Width - (pad * 2) - Scale(16)) / 3;
            int cardY = Scale(44);
            int cardH = Scale(62);

            Panel p1 = CreateMetricPanel("当前实时回报率", "0 Hz", out lblLiveRate, pad, cardY, cardW, cardH, Color.FromArgb(0, 230, 118));
            Panel p2 = CreateMetricPanel("峰值回报率", "0 Hz", out lblPeakRate, pad + cardW + Scale(8), cardY, cardW, cardH, Color.FromArgb(0, 229, 255));
            Panel p3 = CreateMetricPanel("平均回报率", "0 Hz", out lblAvgRate, pad + (cardW + Scale(8)) * 2, cardY, cardW, cardH, Color.FromArgb(255, 183, 77));
            this.Controls.AddRange(new Control[] { p1, p2, p3 });

            motionCanvas = new Panel();
            motionCanvas.SetBounds(pad, Scale(116), ClientSize.Width - (pad * 2), Scale(160));
            motionCanvas.BackColor = Color.FromArgb(22, 26, 36);
            motionCanvas.MouseMove += MotionCanvas_MouseMove;
            motionCanvas.Paint += MotionCanvas_Paint;
            this.Controls.Add(motionCanvas);

            int btnW = Scale(100);
            int btnH = Scale(32);
            int btnY = ClientSize.Height - btnH - Scale(12);

            btnClear = new ModernButton();
            btnClear.Text = "清空数据";
            btnClear.Font = new Font("Microsoft YaHei UI", 8.5F);
            btnClear.NormalColor = Color.FromArgb(32, 38, 52);
            btnClear.HoverColor = Color.FromArgb(48, 56, 76);
            btnClear.BorderColor = Color.FromArgb(60, 70, 96);
            btnClear.TextColor = Color.FromArgb(190, 200, 220);
            btnClear.CornerRadius = Scale(6);
            btnClear.SetBounds(pad, btnY, btnW, btnH);
            btnClear.Click += (s, e) => {
                peakHz = 0;
                avgHz = 0;
                sampleCount = 0;
                recentHz.Clear();
                trail.Clear();
                lblLiveRate.Text = "0 Hz";
                lblPeakRate.Text = "0 Hz";
                lblAvgRate.Text = "0 Hz";
                motionCanvas.Invalidate();
            };
            this.Controls.Add(btnClear);

            btnClose = new ModernButton();
            btnClose.Text = "完成退出";
            btnClose.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            btnClose.NormalColor = Color.FromArgb(0, 180, 90);
            btnClose.HoverColor = Color.FromArgb(0, 210, 105);
            btnClose.BorderColor = Color.FromArgb(0, 230, 118);
            btnClose.TextColor = Color.White;
            btnClose.CornerRadius = Scale(6);
            btnClose.SetBounds(ClientSize.Width - btnW - pad, btnY, btnW, btnH);
            btnClose.Click += (s, e) => this.Close();
            this.Controls.Add(btnClose);
        }

        private Panel CreateMetricPanel(string title, string defVal, out Label valLabel, int x, int y, int w, int h, Color accent)
        {
            Panel p = new Panel();
            p.SetBounds(x, y, w, h);
            p.BackColor = Color.FromArgb(24, 28, 40);

            Label lt = new Label();
            lt.Text = title;
            lt.Font = new Font("Microsoft YaHei UI", 7.8F);
            lt.ForeColor = Color.FromArgb(140, 150, 175);
            lt.SetBounds((int)(6 * dpiScale), (int)(4 * dpiScale), w - (int)(12 * dpiScale), (int)(16 * dpiScale));
            p.Controls.Add(lt);

            valLabel = new Label();
            valLabel.Text = defVal;
            valLabel.Font = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Bold);
            valLabel.ForeColor = accent;
            valLabel.SetBounds((int)(6 * dpiScale), (int)(22 * dpiScale), w - (int)(12 * dpiScale), (int)(32 * dpiScale));
            p.Controls.Add(valLabel);

            return p;
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape) { this.Close(); }
            base.OnKeyDown(e);
        }

        private void MotionCanvas_MouseMove(object sender, MouseEventArgs e)
        {
            long now = Stopwatch.GetTimestamp();
            if (lastMotionTick > 0)
            {
                long diff = now - lastMotionTick;
                if (diff > 0)
                {
                    double hz = (double)Stopwatch.Frequency / diff;
                    if (hz >= 50 && hz <= 10000)
                    {
                        sampleCount++;
                        int curHz = (int)Math.Round(hz);
                        if (curHz > peakHz) peakHz = curHz;

                        recentHz.Enqueue(hz);
                        if (recentHz.Count > 64) recentHz.Dequeue();

                        double sum = 0;
                        foreach (var v in recentHz) sum += v;
                        avgHz = sum / recentHz.Count;

                        lblLiveRate.Text = curHz + " Hz";
                        lblPeakRate.Text = peakHz + " Hz";
                        lblAvgRate.Text = string.Format("{0:0} Hz", avgHz);
                    }
                }
            }
            lastMotionTick = now;

            trail.Add(e.Location);
            if (trail.Count > 30) trail.RemoveAt(0);
            motionCanvas.Invalidate();
        }

        private void MotionCanvas_Paint(object sender, PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (sampleCount == 0)
            {
                using (Font f = new Font("Microsoft YaHei UI", 9F))
                {
                    TextRenderer.DrawText(g, "请在方框内持续划动鼠标进行实时微秒回报率测速...", f,
                        new Rectangle(0, 0, motionCanvas.Width, motionCanvas.Height), Color.FromArgb(100, 110, 135),
                        TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
                }
                return;
            }

            if (trail.Count > 1)
            {
                for (int i = 1; i < trail.Count; i++)
                {
                    int alpha = (int)(255 * ((float)i / trail.Count));
                    using (Pen p = new Pen(Color.FromArgb(alpha, 0, 230, 118), 2f * dpiScale))
                    {
                        g.DrawLine(p, trail[i - 1], trail[i]);
                    }
                }
            }
        }
    }
}