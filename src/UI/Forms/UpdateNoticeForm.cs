// ============================================================================
// 模块名称: UpdateNoticeForm.cs
// 所属区域: UI/Forms (二级窗体层)
// 核心职责:
//   新版本升级提示弹窗：宽敞展示单版本或跨版本累积更新日志、暗黑双缓冲滚动卡片、
//   历代版本快捷入口、流光进度条与无误报原生自动重启置换
// 依赖关系: ModernThemeHelper, ModernButton, ModernProgressBar, StatusPill, RoundedCard, UpdateManager, ReleaseNotesService, ChangelogHistoryForm
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 2. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class UpdateNoticeForm : DpiAwareForm
    {
        private UpdateInfo updateInfo;
        private string currentVersion;

        private ModernButton btnClose;
        private StatusPill pillVersion;
        private Label lblReleaseDate;
        private Label lblHotReloadHint;
        private RoundedCard cardChangelog;
        private Label lblLogTitle;
        private ModernDarkScrollPanel logScrollPanel;
        private LinkLabel lnkHistory;
        private ModernButton btnLater;
        private ModernButton btnUpgrade;
        private ModernProgressBar progressBar;
        private Label lblProgressText;

        private bool isRechecking = false;
        private System.Windows.Forms.Timer toastTimer = null;

        private static UpdateNoticeForm _activeInstance = null;
        private static readonly object _dialogLock = new object();

        public static bool IsShowing
        {
            get
            {
                lock (_dialogLock)
                {
                    return _activeInstance != null && !_activeInstance.IsDisposed;
                }
            }
        }

        protected override bool UseNativeDwmFramelessAnimation
        {
            get { return true; }
        }

        public static void ShowUpdateDialog(IWin32Window owner, UpdateInfo info, string currentVersion, float dpiScale = 0f)
        {
            if (info == null) return;

            lock (_dialogLock)
            {
                if (_activeInstance != null && !_activeInstance.IsDisposed)
                {
                    try
                    {
                        if (_activeInstance.InvokeRequired)
                        {
                            _activeInstance.BeginInvoke((MethodInvoker)delegate
                            {
                                try
                                {
                                    _activeInstance.Activate();
                                    _activeInstance.BringToFront();
                                }
                                catch { }
                            });
                        }
                        else
                        {
                            _activeInstance.Activate();
                            _activeInstance.BringToFront();
                        }
                    }
                    catch { }
                    return;
                }
            }

            using (UpdateNoticeForm form = new UpdateNoticeForm(info, currentVersion, dpiScale))
            {
                lock (_dialogLock)
                {
                    _activeInstance = form;
                }
                try
                {
                    if (owner != null)
                    {
                        form.ShowDialog(owner);
                    }
                    else
                    {
                        form.ShowDialog();
                    }
                }
                finally
                {
                    lock (_dialogLock)
                    {
                        if (_activeInstance == form)
                        {
                            _activeInstance = null;
                        }
                    }
                }
            }
        }

        public UpdateNoticeForm() : this(new UpdateInfo(), UpdateManager.CurrentVersion, 0f)
        {
        }

        public UpdateNoticeForm(UpdateInfo info, string currentVersion) : this(info, currentVersion, 0f)
        {
        }

        public UpdateNoticeForm(UpdateInfo info, string currentVersion, float dpiScale) : base(dpiScale)
        {
            if (dpiScale <= 0f)
            {
                float sysDpi = DpiScaleHelper.GetDpiScaleForWindow(IntPtr.Zero);
                this.DpiScale = DpiScaleHelper.ClampScaleToScreen(sysDpi, Screen.FromPoint(Cursor.Position), 680);
            }
            this.updateInfo = info ?? new UpdateInfo();
            this.currentVersion = currentVersion ?? "";

            InitializeUpdateUI();
        }

        private void InitializeUpdateUI()
        {
            this.Text = "FerrisPulse · 灵脉 - 发现新版本";
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(18, 19, 23);
            this.ForeColor = Color.White;
            this.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            Rectangle workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            int formW = Scale(620);
            int formH = Scale(540);
            int maxW = (int)(workArea.Width * 0.90);
            int maxH = (int)(workArea.Height * 0.88);
            if (maxW > 0 && formW > maxW) formW = maxW;
            if (maxH > 0 && formH > maxH) formH = maxH;
            if (formW < Scale(520)) formW = Math.Min(Scale(520), workArea.Width > 0 ? workArea.Width : Scale(520));
            if (formH < Scale(440)) formH = Math.Min(Scale(440), workArea.Height > 0 ? workArea.Height : Scale(440));

            this.ClientSize = new Size(formW, formH);
            this.DoubleBuffered = true;
            this.ShowInTaskbar = false;
            this.TopMost = false;
            this.KeyPreview = true;
            this.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    this.Close();
                }
            };

            int padX = Scale(24);

            // 1. 顶部一体化大标题栏
            int heroBarH = Scale(52);
            int topInset = Scale(12);
            int rowH = Math.Max(Scale(24), heroBarH - topInset - Scale(2));
            Panel titleBar = new Panel();
            titleBar.Dock = DockStyle.Top;
            titleBar.Height = heroBarH;
            titleBar.BackColor = this.BackColor;
            titleBar.MouseDown += TitleBar_MouseDown;

            Label lblTitle = new Label();
            lblTitle.Text = "发现新版本";
            lblTitle.Font = DpiScaleHelper.GetCachedScaledFont("Microsoft YaHei UI", 13.0F, FontStyle.Bold, DpiScale);
            lblTitle.ForeColor = Color.FromArgb(242, 246, 255);
            lblTitle.AutoSize = false;
            lblTitle.TextAlign = ContentAlignment.MiddleLeft;
            lblTitle.MouseDown += TitleBar_MouseDown;
            titleBar.Controls.Add(lblTitle);

            int closeSize = Math.Max(22, Scale(28));
            btnClose = new ModernButton();
            btnClose.IsCloseButton = true;
            btnClose.Text = "";
            btnClose.Size = new Size(closeSize, closeSize);
            btnClose.Location = new Point(formW - padX - closeSize, topInset + Math.Max(0, (rowH - closeSize) / 2));
            btnClose.NormalColor = Color.FromArgb(28, 32, 42);
            btnClose.BorderColor = Color.FromArgb(44, 50, 64);
            btnClose.HoverColor = Color.FromArgb(215, 45, 58);
            btnClose.PressedColor = Color.FromArgb(175, 28, 42);
            btnClose.ForeColor = Color.FromArgb(182, 192, 212);
            btnClose.CornerRadius = Math.Max(5, Scale(7));
            btnClose.Click += delegate(object s, EventArgs e) { this.Close(); };
            titleBar.Controls.Add(btnClose);

            lblTitle.Bounds = new Rectangle(padX, topInset, Math.Max(40, btnClose.Left - padX - Scale(10)), rowH);

            this.Controls.Add(titleBar);

            // 2. 版本头部信息 (动态对齐，永不重叠)
            int headerY = Scale(58);
            string verText = FormatDisplayVersion(updateInfo.LatestVersion);

            pillVersion = new StatusPill();
            pillVersion.Location = new Point(padX, headerY);
            pillVersion.Height = Scale(24);
            pillVersion.SetStatus(verText, Color.FromArgb(0, 230, 118));
            this.Controls.Add(pillVersion);

            lblReleaseDate = new Label();
            lblReleaseDate.Text = string.IsNullOrEmpty(updateInfo.ReleaseDate) ? "最新发布" : ("发布日期: " + updateInfo.ReleaseDate);
            lblReleaseDate.Font = new Font("Microsoft YaHei UI", 8.8F);
            lblReleaseDate.ForeColor = Color.FromArgb(155, 164, 180);
            lblReleaseDate.AutoSize = true;
            lblReleaseDate.Location = new Point(pillVersion.Right + Scale(16), headerY + (pillVersion.Height - lblReleaseDate.PreferredHeight) / 2);
            lblReleaseDate.MouseDown += TitleBar_MouseDown;
            this.Controls.Add(lblReleaseDate);

            // 右侧微光热重载动态提示 (静默探测到更高版本时轻柔浮现)
            lblHotReloadHint = new Label();
            lblHotReloadHint.Text = "";
            lblHotReloadHint.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold);
            lblHotReloadHint.ForeColor = Color.FromArgb(0, 230, 118);
            lblHotReloadHint.AutoSize = true;
            lblHotReloadHint.Visible = false;
            lblHotReloadHint.Location = new Point(formW - padX - Scale(160), headerY + (pillVersion.Height - lblHotReloadHint.PreferredHeight) / 2);
            lblHotReloadHint.MouseDown += TitleBar_MouseDown;
            this.Controls.Add(lblHotReloadHint);

            // 3. 更新日志卡片容器
            int cardY = headerY + Scale(38);
            int cardW = formW - padX * 2;
            int actionY = formH - Scale(24) - Scale(36);
            int cardH = actionY - Scale(18) - cardY;
            cardChangelog = new RoundedCard();
            cardChangelog.Location = new Point(padX, cardY);
            cardChangelog.Size = new Size(cardW, cardH);
            cardChangelog.CornerRadius = Scale(8);
            cardChangelog.CardColor = Color.FromArgb(25, 27, 34);
            cardChangelog.BorderColor = Color.FromArgb(42, 46, 58);
            cardChangelog.MouseDown += TitleBar_MouseDown;

            lblLogTitle = new Label();
            lblLogTitle.Text = "更新日志与改进特性:";
            lblLogTitle.Font = new Font("Microsoft YaHei UI", 9.6F, FontStyle.Bold);
            lblLogTitle.ForeColor = Color.FromArgb(235, 240, 255);
            lblLogTitle.BackColor = cardChangelog.CardColor;
            lblLogTitle.Location = new Point(Scale(20), Scale(16));
            lblLogTitle.AutoSize = true;
            lblLogTitle.MouseDown += TitleBar_MouseDown;
            cardChangelog.Controls.Add(lblLogTitle);

            logScrollPanel = new ModernDarkScrollPanel();
            logScrollPanel.Location = new Point(Scale(16), Scale(44));
            logScrollPanel.Size = new Size(cardW - Scale(32), cardH - Scale(54));
            logScrollPanel.BackColor = cardChangelog.CardColor;
            cardChangelog.Controls.Add(logScrollPanel);

            RenderChangelog();

            this.Controls.Add(cardChangelog);

            // 4. 底部进度条、历代日志入口与控制按钮区域
            progressBar = new ModernProgressBar();
            progressBar.Location = new Point(padX, actionY + Scale(4));
            progressBar.Size = new Size(cardW, Scale(8));
            progressBar.Visible = false;
            progressBar.Value = 0;
            this.Controls.Add(progressBar);

            lblProgressText = new Label();
            lblProgressText.Font = new Font("Microsoft YaHei UI", 8.8F);
            lblProgressText.ForeColor = Color.FromArgb(0, 230, 118);
            lblProgressText.Location = new Point(padX, actionY + Scale(16));
            lblProgressText.Size = new Size(cardW, Scale(22));
            lblProgressText.TextAlign = ContentAlignment.MiddleCenter;
            lblProgressText.Visible = false;
            this.Controls.Add(lblProgressText);

            int btnLaterW = Scale(110);
            int btnUpgradeW = Scale(130);
            int btnH = Scale(36);

            lnkHistory = new LinkLabel();
            lnkHistory.Text = "查看历代版本";
            lnkHistory.Font = new Font("Microsoft YaHei UI", 8.8F);
            lnkHistory.LinkColor = Color.FromArgb(120, 195, 255);
            lnkHistory.ActiveLinkColor = Color.FromArgb(170, 220, 255);
            lnkHistory.VisitedLinkColor = Color.FromArgb(120, 195, 255);
            lnkHistory.LinkBehavior = LinkBehavior.NeverUnderline;
            lnkHistory.BackColor = Color.Transparent;
            lnkHistory.AutoSize = true;
            lnkHistory.Cursor = Cursors.Hand;
            lnkHistory.Location = new Point(padX, actionY + (btnH - lnkHistory.PreferredHeight) / 2);
            lnkHistory.MouseEnter += delegate { lnkHistory.LinkColor = Color.FromArgb(175, 222, 255); };
            lnkHistory.MouseLeave += delegate { lnkHistory.LinkColor = Color.FromArgb(120, 195, 255); };
            lnkHistory.LinkClicked += delegate(object s, LinkLabelLinkClickedEventArgs e)
            {
                ChangelogHistoryForm.ShowHistoryDialog(this, DpiScale);
            };
            this.Controls.Add(lnkHistory);

            btnLater = new ModernButton();
            btnLater.Text = "稍后提醒";
            btnLater.Font = new Font("Microsoft YaHei UI", 9.2F, FontStyle.Bold);
            btnLater.Location = new Point(formW - padX - btnUpgradeW - Scale(16) - btnLaterW, actionY);
            btnLater.Size = new Size(btnLaterW, btnH);
            btnLater.NormalColor = Color.FromArgb(36, 40, 52);
            btnLater.HoverColor = Color.FromArgb(48, 54, 70);
            btnLater.PressedColor = Color.FromArgb(28, 30, 40);
            btnLater.BorderColor = Color.FromArgb(58, 64, 82);
            btnLater.ForeColor = Color.White;
            btnLater.CornerRadius = Scale(6);
            btnLater.Click += delegate(object s, EventArgs e) { this.Close(); };
            this.Controls.Add(btnLater);

            btnUpgrade = new ModernButton();
            btnUpgrade.Text = "立即升级";
            btnUpgrade.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            btnUpgrade.Location = new Point(formW - padX - btnUpgradeW, actionY);
            btnUpgrade.Size = new Size(btnUpgradeW, btnH);
            btnUpgrade.NormalColor = Color.FromArgb(0, 200, 83);
            btnUpgrade.HoverColor = Color.FromArgb(0, 230, 118);
            btnUpgrade.PressedColor = Color.FromArgb(0, 160, 65);
            btnUpgrade.ForeColor = Color.FromArgb(10, 24, 15);
            btnUpgrade.CornerRadius = Scale(6);
            btnUpgrade.Click += BtnUpgrade_Click;
            this.Controls.Add(btnUpgrade);

            // 5. SWR (Stale-While-Revalidate) 模式：显示后静默发起后台重检
            this.Shown += delegate(object s, EventArgs e)
            {
                TriggerBackgroundRecheck();
            };
        }

        private static string FormatDisplayVersion(string rawVersion)
        {
            string rawVer = string.IsNullOrEmpty(rawVersion) ? "2.0.0" : rawVersion;
            string cleanVer = rawVer.Trim().TrimStart('v', 'V');
            int spaceIdx = cleanVer.IndexOf(' ');
            if (spaceIdx > 0) cleanVer = cleanVer.Substring(0, spaceIdx);
            return "v" + cleanVer;
        }

        protected override void OnApplyDpiScale(float scale)
        {
            base.OnApplyDpiScale(scale);
            if (logScrollPanel != null)
            {
                logScrollPanel.UpdateDpiScale(scale);
            }
            if (lblReleaseDate != null && pillVersion != null)
            {
                lblReleaseDate.Location = new Point(
                    pillVersion.Right + Scale(16),
                    pillVersion.Top + (pillVersion.Height - lblReleaseDate.PreferredHeight) / 2);
            }
        }

        private void RenderChangelog()
        {
            if (logScrollPanel == null) return;

            IList<ReleaseNoteEntry> remotePool = (updateInfo != null && updateInfo.ReleaseHistory != null)
                ? updateInfo.ReleaseHistory
                : null;
            List<ReleaseNoteEntry> mergedPool = ReleaseNotesService.MergeReleaseNotes(
                remotePool,
                ReleaseNotesService.LoadEmbeddedReleaseNotes());

            int spannedCount;
            List<string> lines = ReleaseNotesService.BuildCumulativeChangelogLines(
                mergedPool,
                this.currentVersion,
                this.updateInfo,
                out spannedCount);

            if (spannedCount > 1)
            {
                string cleanCur = (this.currentVersion ?? "").Trim().TrimStart('v', 'V');
                if (lblLogTitle != null)
                {
                    lblLogTitle.Text = string.Format("本次跨版本更新内容（共 {0} 个版本）:", spannedCount);
                }
                if (lblReleaseDate != null)
                {
                    string datePart = string.IsNullOrEmpty(updateInfo.ReleaseDate) ? "" : (" · " + updateInfo.ReleaseDate);
                    lblReleaseDate.Text = string.Format("从 v{0} 升级 · 共包含 {1} 个版本更新{2}", cleanCur, spannedCount, datePart);
                }
            }
            else
            {
                if (lblLogTitle != null)
                {
                    lblLogTitle.Text = "更新日志与改进特性:";
                }
                if (lblReleaseDate != null)
                {
                    lblReleaseDate.Text = string.IsNullOrEmpty(updateInfo.ReleaseDate) ? "最新发布" : ("发布日期: " + updateInfo.ReleaseDate);
                }
            }

            if (lblReleaseDate != null && pillVersion != null)
            {
                lblReleaseDate.Location = new Point(
                    pillVersion.Right + Scale(16),
                    pillVersion.Top + (pillVersion.Height - lblReleaseDate.PreferredHeight) / 2);
            }

            logScrollPanel.SetChangelog(lines, DpiScale);
        }

        public void TriggerBackgroundRecheck()
        {
            if (isRechecking) return;
            isRechecking = true;

            UpdateManager.CheckUpdateAsync(delegate(bool success, UpdateInfo info, string error)
            {
                if (this.IsDisposed || !this.IsHandleCreated) return;

                this.BeginInvoke((MethodInvoker)delegate
                {
                    isRechecking = false;
                    if (!success || info == null) return;

                    int cmp = UpdateManager.CompareVersions(info.LatestVersion, this.updateInfo.LatestVersion);
                    if (cmp > 0)
                    {
                        UpdateDisplay(info, true);
                    }
                    else if (cmp == 0 && info.ReleaseHistory != null && info.ReleaseHistory.Count > 0 &&
                             (this.updateInfo.ReleaseHistory == null || this.updateInfo.ReleaseHistory.Count == 0))
                    {
                        UpdateDisplay(info, false);
                    }
                });
            });
        }

        public void UpdateDisplay(UpdateInfo newInfo)
        {
            UpdateDisplay(newInfo, false);
        }

        public void UpdateDisplay(UpdateInfo newInfo, bool showToast)
        {
            if (newInfo == null) return;
            this.updateInfo = newInfo;

            string verText = FormatDisplayVersion(updateInfo.LatestVersion);

            if (pillVersion != null)
            {
                pillVersion.SetStatus(verText, Color.FromArgb(0, 230, 118));
            }

            RenderChangelog();

            if (showToast && lblHotReloadHint != null && pillVersion != null)
            {
                lblHotReloadHint.Text = "✨ 已同步最新版本 (" + verText + ")";
                lblHotReloadHint.Location = new Point(
                    this.ClientSize.Width - Scale(24) - lblHotReloadHint.PreferredWidth,
                    pillVersion.Top + (pillVersion.Height - lblHotReloadHint.PreferredHeight) / 2);
                lblHotReloadHint.Visible = true;

                if (toastTimer != null)
                {
                    try { toastTimer.Stop(); toastTimer.Dispose(); } catch { }
                    toastTimer = null;
                }
                toastTimer = new System.Windows.Forms.Timer();
                toastTimer.Interval = 2800;
                toastTimer.Tick += delegate(object s, EventArgs e)
                {
                    toastTimer.Stop();
                    toastTimer.Dispose();
                    toastTimer = null;
                    if (!this.IsDisposed && lblHotReloadHint != null)
                    {
                        lblHotReloadHint.Visible = false;
                    }
                };
                toastTimer.Start();
            }
        }

        private void ShowDownloadErrorLayout(string message, int padX, int actionY, int btnH)
        {
            progressBar.StopFlow();
            progressBar.Visible = false;
            btnLater.Visible = true;
            btnUpgrade.Visible = true;
            btnUpgrade.Text = "重试升级";
            if (lnkHistory != null) lnkHistory.Visible = true;

            int leftBound = (lnkHistory != null && lnkHistory.Visible) ? (lnkHistory.Right + Scale(14)) : padX;
            int availW = btnLater.Left - leftBound - Scale(12);
            lblProgressText.ForeColor = Color.FromArgb(255, 82, 82);
            lblProgressText.Text = message;
            lblProgressText.Location = new Point(leftBound, actionY + (btnH - Scale(22)) / 2);
            lblProgressText.Size = new Size(availW > 100 ? availW : Scale(260), Scale(22));
            lblProgressText.TextAlign = ContentAlignment.MiddleLeft;
            lblProgressText.AutoEllipsis = true;
            lblProgressText.Visible = true;
        }

        private void BtnUpgrade_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(updateInfo.DownloadUrl))
            {
                MessageBox.Show(this, "未找到有效的下载地址。", "更新错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            int padX = Scale(24);
            int actionY = btnUpgrade != null ? btnUpgrade.Top : (this.ClientSize.Height - Scale(60));
            int btnH = btnUpgrade != null ? btnUpgrade.Height : Scale(36);
            int cardW = this.ClientSize.Width - padX * 2;

            if (lnkHistory != null) lnkHistory.Visible = false;
            btnLater.Visible = false;
            btnUpgrade.Visible = false;
            progressBar.Visible = true;
            lblProgressText.Visible = true;
            lblProgressText.Location = new Point(padX, actionY + Scale(16));
            lblProgressText.Size = new Size(cardW, Scale(22));
            lblProgressText.TextAlign = ContentAlignment.MiddleCenter;
            lblProgressText.ForeColor = Color.FromArgb(0, 230, 118);
            progressBar.StartChargingFlow(Color.FromArgb(0, 200, 83), Color.FromArgb(0, 229, 255));
            lblProgressText.Text = "正在同步更新组件...";

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string tempExe = Path.Combine(baseDir, "FerrisPulse.exe.update");

            UpdateManager.DownloadFileAsync(
                updateInfo.DownloadUrl,
                tempExe,
                updateInfo.Sha256,
                delegate(long bytesRead, long totalBytes, double speedBytesPerSec)
                {
                    if (this.IsDisposed || !this.IsHandleCreated) return;

                    this.BeginInvoke((MethodInvoker)delegate
                    {
                        if (totalBytes > 0)
                        {
                            int pct = (int)((bytesRead * 100) / totalBytes);
                            progressBar.StartChargingFill(pct, Color.FromArgb(0, 215, 115), Color.FromArgb(0, 229, 255));

                            double readMb = bytesRead / (1024.0 * 1024.0);
                            double totalMb = totalBytes / (1024.0 * 1024.0);
                            double speedMb = speedBytesPerSec / (1024.0 * 1024.0);

                            lblProgressText.Text = string.Format("正在同步新版本组件: {0}% ({1:F1} MB / {2:F1} MB) · {3:F1} MB/s", pct, readMb, totalMb, speedMb);
                        }
                        else
                        {
                            double readMb = bytesRead / (1024.0 * 1024.0);
                            double speedMb = speedBytesPerSec / (1024.0 * 1024.0);
                            lblProgressText.Text = string.Format("已同步: {0:F1} MB · {1:F1} MB/s", readMb, speedMb);
                        }
                    });
                },
                delegate(bool success, string error)
                {
                    if (this.IsDisposed || !this.IsHandleCreated) return;

                    this.BeginInvoke((MethodInvoker)delegate
                    {
                        if (!success)
                        {
                            ShowDownloadErrorLayout(
                                string.IsNullOrEmpty(error) ? "更新包校验失败，文件损坏！" : ("更新失败: " + error),
                                padX, actionY, btnH);
                            try { File.Delete(tempExe); } catch { }
                            return;
                        }

                        string validateErr;
                        if (!ReleaseNotesService.ValidateDownloadedExecutable(tempExe, updateInfo.Sha256, out validateErr))
                        {
                            ShowDownloadErrorLayout(
                                string.IsNullOrEmpty(validateErr) ? "更新包校验失败，文件可能已损坏！" : validateErr,
                                padX, actionY, btnH);
                            try { File.Delete(tempExe); } catch { }
                            return;
                        }

                        progressBar.StopFlow(100, Color.FromArgb(0, 230, 118));
                        lblProgressText.ForeColor = Color.FromArgb(0, 230, 118);
                        lblProgressText.Location = new Point(padX, actionY + Scale(16));
                        lblProgressText.Size = new Size(cardW, Scale(22));
                        lblProgressText.TextAlign = ContentAlignment.MiddleCenter;
                        lblProgressText.Text = "更新组件就绪，正在准备装载并重启...";

                        Timer restartTimer = new Timer();
                        restartTimer.Interval = 1200;
                        restartTimer.Tick += delegate(object s, EventArgs ev)
                        {
                            restartTimer.Stop();
                            restartTimer.Dispose();
                            UpdateManager.LaunchSelfReplace(tempExe);
                        };
                        restartTimer.Start();
                    });
                }
            );
        }

        private void TitleBar_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                try
                {
                    NativeMethods.ReleaseCapture();
                    NativeMethods.SendMessage(this.Handle, NativeMethods.WM_NCLBUTTONDOWN, (IntPtr)NativeMethods.HT_CAPTION, IntPtr.Zero);
                }
                catch { }
            }
        }

        protected override void WndProc(ref Message m)
        {
            const int WM_NCHITTEST = 0x84;
            const int HTCLIENT = 1;
            const int HTCAPTION = 2;

            base.WndProc(ref m);

            if (m.Msg == WM_NCHITTEST && (int)m.Result == HTCLIENT)
            {
                Point pt = this.PointToClient(new Point(m.LParam.ToInt32()));
                if (pt.Y <= 36)
                {
                    m.Result = (IntPtr)HTCAPTION;
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (toastTimer != null)
                {
                    try { toastTimer.Stop(); toastTimer.Dispose(); } catch { }
                    toastTimer = null;
                }
            }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen borderPen = new Pen(Color.FromArgb(42, 46, 58), 1))
            {
                e.Graphics.DrawRectangle(borderPen, 0, 0, this.Width - 1, this.Height - 1);
            }
        }
    }
}
