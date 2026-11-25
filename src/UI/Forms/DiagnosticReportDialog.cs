// ============================================================================
// 模块名称: DiagnosticReportDialog.cs
// 所属区域: UI/Forms (业务对话框层)
// 核心职责:
//   一键外设硬件诊断快照导出弹窗：后台线程只读采集全部物理 HID 外设端点拓扑、
//   未适配设备列表与驱动探测日志，使用 ModernDarkScrollPanel 纤细圆角暗黑滚动画布与
//   多色语义高亮渲染，支持一键复制或保存 TXT 发给开发者快速定位老型号兼容问题。
// 依赖关系: HardwareDiagnosticService, Forms.DpiAwareForm, Controls.RoundedCard, Controls.ModernDarkScrollPanel, Controls.ModernButton
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、字段或排版时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（❌ 严禁 $"..." 插值、?.、nameof、out var）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Text;
using System.Threading;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class DiagnosticReportDialog : DpiAwareForm
    {
        private readonly List<MouseBatteryInfo> initialDevices;
        private RoundedCard cardReport;
        private ModernDarkScrollPanel scrollReport;
        private string rawReportText = "";
        private ModernButton btnRefresh;
        private ModernButton btnSaveFile;
        private ModernButton btnCopy;
        private ModernButton btnClose;
        private Label lblStatus;
        private System.Windows.Forms.Timer copyFeedbackTimer;
        private bool isCollecting;

        public DiagnosticReportDialog() : this(null, 1.0f)
        {
        }

        public DiagnosticReportDialog(float scale) : this(null, scale)
        {
        }

        public DiagnosticReportDialog(List<MouseBatteryInfo> devices, float scale) : base(scale)
        {
            this.initialDevices = devices;
            InitializeUi();
        }

        private void InitializeUi()
        {
            this.Text = "外设硬件诊断快照 (只读零写入)";
            int clientW = (int)(620 * dpiScale);
            int clientH = (int)(520 * dpiScale);
            this.ClientSize = new Size(clientW, clientH);
            this.BackColor = Color.FromArgb(18, 19, 23);
            this.ForeColor = Color.White;
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.KeyPreview = true;
            this.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape) this.Close();
            };

            this.HandleCreated += delegate(object s, EventArgs e)
            {
                ModernThemeHelper.ApplyModernWin11Theme(this.Handle);
            };

            int pad = (int)(18 * dpiScale);
            int innerW = clientW - (pad * 2);

            Label lblSub = new Label();
            lblSub.Text = "仅读取 HID 硬件信息（已隐藏序列号），复制后可发给开发者排查兼容问题。";
            lblSub.Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Regular, GraphicsUnit.Point);
            lblSub.ForeColor = Color.FromArgb(145, 155, 175);
            lblSub.Location = new Point(pad, (int)(56 * dpiScale));
            lblSub.Size = new Size(innerW, (int)(32 * dpiScale));
            this.Controls.Add(lblSub);

            // 自底向上严格分层定高：底部按钮行 -> 状态栏 -> 中间滚动卡片，彻底杜绝控件重叠遮挡
            int btnH = (int)(34 * dpiScale);
            int btnY = clientH - (int)(16 * dpiScale) - btnH;
            int statusH = (int)(20 * dpiScale);
            int statusY = btnY - (int)(8 * dpiScale) - statusH;
            int cardY = (int)(94 * dpiScale);
            int cardH = Math.Max((int)(120 * dpiScale), statusY - (int)(8 * dpiScale) - cardY);

            cardReport = new RoundedCard();
            cardReport.CardColor = Color.FromArgb(20, 22, 29);
            cardReport.BorderColor = Color.FromArgb(44, 50, 66);
            cardReport.CornerRadius = (int)(10 * dpiScale);
            cardReport.Location = new Point(pad, cardY);
            cardReport.Size = new Size(innerW, cardH);
            this.Controls.Add(cardReport);

            int cPad = (int)(10 * dpiScale);
            scrollReport = new ModernDarkScrollPanel();
            scrollReport.BackColor = Color.FromArgb(20, 22, 29);
            scrollReport.Location = new Point(cPad, cPad);
            scrollReport.Size = new Size(innerW - (cPad * 2), cardH - (cPad * 2));
            rawReportText = "点击「重新采集」或打开窗口后将自动开始只读硬件快照采集...";
            scrollReport.SetDiagnosticText(rawReportText, dpiScale);
            cardReport.Controls.Add(scrollReport);

            lblStatus = new Label();
            lblStatus.Text = "准备采集本机外设拓扑...";
            lblStatus.Font = new Font("Microsoft YaHei UI", 8.0F, FontStyle.Regular, GraphicsUnit.Point);
            lblStatus.ForeColor = Color.FromArgb(130, 142, 165);
            lblStatus.BackColor = Color.Transparent;
            lblStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblStatus.Location = new Point(pad, statusY);
            lblStatus.Size = new Size(innerW, statusH);
            this.Controls.Add(lblStatus);

            int gap = (int)(8 * dpiScale);
            int refreshW = (int)(98 * dpiScale);
            int saveW = (int)(108 * dpiScale);
            int copyW = (int)(134 * dpiScale);
            int closeW = (int)(76 * dpiScale);

            btnRefresh = new ModernButton();
            btnRefresh.Text = "🔄 重新采集";
            btnRefresh.Font = new Font("Microsoft YaHei UI", 8.6F, FontStyle.Bold, GraphicsUnit.Point);
            btnRefresh.NormalColor = Color.FromArgb(32, 36, 48);
            btnRefresh.HoverColor = Color.FromArgb(46, 52, 68);
            btnRefresh.PressedColor = Color.FromArgb(24, 28, 38);
            btnRefresh.BorderColor = Color.FromArgb(60, 68, 88);
            btnRefresh.TextColor = Color.FromArgb(220, 230, 250);
            btnRefresh.Location = new Point(pad, btnY);
            btnRefresh.Size = new Size(refreshW, btnH);
            btnRefresh.Click += delegate(object s, EventArgs e) { StartCaptureAsync(null); };
            this.Controls.Add(btnRefresh);

            btnSaveFile = new ModernButton();
            btnSaveFile.Text = "💾 保存 TXT";
            btnSaveFile.Font = new Font("Microsoft YaHei UI", 8.6F, FontStyle.Bold, GraphicsUnit.Point);
            btnSaveFile.NormalColor = Color.FromArgb(30, 38, 54);
            btnSaveFile.HoverColor = Color.FromArgb(44, 56, 78);
            btnSaveFile.PressedColor = Color.FromArgb(22, 28, 40);
            btnSaveFile.BorderColor = Color.FromArgb(62, 82, 118);
            btnSaveFile.TextColor = Color.FromArgb(160, 210, 255);
            btnSaveFile.Location = new Point(btnRefresh.Right + gap, btnY);
            btnSaveFile.Size = new Size(saveW, btnH);
            btnSaveFile.Click += BtnSaveFile_Click;
            this.Controls.Add(btnSaveFile);

            btnClose = new ModernButton();
            btnClose.Text = "关闭";
            btnClose.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Regular, GraphicsUnit.Point);
            btnClose.NormalColor = Color.FromArgb(32, 36, 48);
            btnClose.HoverColor = Color.FromArgb(46, 52, 68);
            btnClose.PressedColor = Color.FromArgb(24, 28, 38);
            btnClose.BorderColor = Color.FromArgb(60, 68, 88);
            btnClose.TextColor = Color.FromArgb(210, 220, 238);
            btnClose.Size = new Size(closeW, btnH);
            btnClose.Location = new Point(clientW - pad - closeW, btnY);
            btnClose.Click += delegate(object s, EventArgs e) { this.Close(); };
            this.Controls.Add(btnClose);

            btnCopy = new ModernButton();
            btnCopy.Text = "复制报告";
            btnCopy.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold, GraphicsUnit.Point);
            btnCopy.NormalColor = Color.FromArgb(0, 195, 82);
            btnCopy.HoverColor = Color.FromArgb(0, 228, 114);
            btnCopy.PressedColor = Color.FromArgb(0, 165, 68);
            btnCopy.BorderColor = Color.FromArgb(0, 230, 118);
            btnCopy.TextColor = Color.Black;
            btnCopy.Size = new Size(copyW, btnH);
            btnCopy.Location = new Point(btnClose.Left - gap - copyW, btnY);
            btnCopy.Click += BtnCopy_Click;
            this.Controls.Add(btnCopy);

            this.MouseWheel += delegate(object s, MouseEventArgs e)
            {
                if (scrollReport != null) scrollReport.HandleWheelDelta(e.Delta);
            };
            cardReport.MouseWheel += delegate(object s, MouseEventArgs e)
            {
                if (scrollReport != null) scrollReport.HandleWheelDelta(e.Delta);
            };

            this.Shown += delegate(object s, EventArgs e)
            {
                StartCaptureAsync(initialDevices);
            };

            this.modernChromeTitlePadLeftBase = 18;
            AttachModernChrome("外设硬件诊断快照 (只读零写入)", null, 0, false, false);
        }

        private void StartCaptureAsync(List<MouseBatteryInfo> preloadedDevices)
        {
            if (isCollecting) return;
            isCollecting = true;
            btnRefresh.Enabled = false;
            lblStatus.ForeColor = Color.FromArgb(95, 195, 255);
            lblStatus.Text = "正在只读采集本机 HID 外设拓扑与驱动日志...";

            ThreadPool.QueueUserWorkItem(delegate(object state)
            {
                string reportText;
                try
                {
                    reportText = HardwareDiagnosticService.GenerateDiagnosticReport(preloadedDevices);
                }
                catch (Exception ex)
                {
                    reportText = "诊断采集失败: " + ex.Message;
                }

                try
                {
                    if (!this.IsDisposed && this.IsHandleCreated)
                    {
                        this.BeginInvoke((Action)delegate()
                        {
                            if (this.IsDisposed) return;
                            isCollecting = false;
                            btnRefresh.Enabled = true;
                            rawReportText = reportText ?? "";
                            scrollReport.SetDiagnosticText(rawReportText, dpiScale);
                            lblStatus.ForeColor = Color.FromArgb(0, 225, 115);
                            lblStatus.Text = "采集完成！点击「复制报告」发给开发者即可排查。";
                        });
                    }
                }
                catch { }
            });
        }

        private void BtnCopy_Click(object sender, EventArgs e)
        {
            try
            {
                string content = rawReportText ?? "";
                if (string.IsNullOrEmpty(content)) return;
                Clipboard.SetText(content);

                btnCopy.Text = "✓ 已复制到剪贴板";
                if (copyFeedbackTimer == null)
                {
                    copyFeedbackTimer = new System.Windows.Forms.Timer();
                    copyFeedbackTimer.Interval = 2000;
                    copyFeedbackTimer.Tick += delegate(object s2, EventArgs e2)
                    {
                        copyFeedbackTimer.Stop();
                        if (!btnCopy.IsDisposed) btnCopy.Text = "复制报告";
                    };
                }
                copyFeedbackTimer.Stop();
                copyFeedbackTimer.Start();
            }
            catch { }
        }

        private void BtnSaveFile_Click(object sender, EventArgs e)
        {
            try
            {
                using (SaveFileDialog sfd = new SaveFileDialog())
                {
                    sfd.Filter = "文本文件 (*.txt)|*.txt";
                    sfd.FileName = "FerrisPulse_Diagnostic_" + DateTime.Now.ToString("yyyyMMdd_HHmmss") + ".txt";
                    if (sfd.ShowDialog(this) == DialogResult.OK)
                    {
                        File.WriteAllText(sfd.FileName, rawReportText ?? "", new UTF8Encoding(true));
                        lblStatus.ForeColor = Color.FromArgb(0, 225, 115);
                        lblStatus.Text = "已保存诊断报告: " + Path.GetFileName(sfd.FileName);
                    }
                }
            }
            catch (Exception ex)
            {
                lblStatus.ForeColor = Color.FromArgb(255, 110, 120);
                lblStatus.Text = "保存失败: " + ex.Message;
            }
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            base.OnFormClosed(e);
            if (copyFeedbackTimer != null)
            {
                copyFeedbackTimer.Stop();
                copyFeedbackTimer.Dispose();
                copyFeedbackTimer = null;
            }
        }
    }
}
