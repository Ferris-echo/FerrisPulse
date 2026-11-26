// ============================================================================
// 模块名称: ChangelogHistoryForm.cs
// 所属区域: UI/Forms (二级窗体层)
// 核心职责:
//   软件内历代更新日志浏览器与历史版本一键切换弹窗：
//   左侧展示历代版本时间轴（含“全部版本”与当前版本徽标），右侧通过纯 GDI+
//   双缓冲滚动面板呈现通俗易懂的纯功能更新说明，支持从 Gitee/GitHub 多源
//   下载指定历史或最新版本并执行原生原子替换重启
// 依赖关系: DpiAwareForm, DpiScaleHelper, ModernThemeHelper, ModernButton, ModernProgressBar, StatusPill, RoundedCard, ModernDarkScrollPanel, ReleaseNotesService, UpdateManager
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
    public class ChangelogHistoryForm : DpiAwareForm
    {
        private List<ReleaseNoteEntry> releases = new List<ReleaseNoteEntry>();
        private ReleaseNoteEntry selectedEntry = null; // null 表示“全部版本”
        private bool isDownloading = false;

        private ModernButton btnCloseTop;
        private RoundedCard cardTimeline;
        private VersionTimelineControl timelineList;
        private RoundedCard cardDetail;
        private StatusPill pillSelectedVersion;
        private Label lblSelectedDate;
        private Label lblSelectedSummary;
        private ModernButton btnSwitchVersion;
        private Panel dividerLine;
        private ModernDarkScrollPanel logScrollPanel;

        private ModernProgressBar progressBar;
        private Label lblDownloadStatus;
        private ModernButton btnCloseBottom;

        private static ChangelogHistoryForm _activeInstance = null;
        private static readonly object _dialogLock = new object();

        protected override bool UseNativeDwmFramelessAnimation
        {
            get { return true; }
        }

        public static void ShowHistoryDialog(IWin32Window owner, float dpiScale = 0f)
        {
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

            using (ChangelogHistoryForm form = new ChangelogHistoryForm(dpiScale))
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

        public ChangelogHistoryForm() : this(0f)
        {
        }

        public ChangelogHistoryForm(float dpiScale) : base(dpiScale)
        {
            if (dpiScale <= 0f)
            {
                float sysDpi = DpiScaleHelper.GetDpiScaleForWindow(IntPtr.Zero);
                this.DpiScale = DpiScaleHelper.ClampScaleToScreen(sysDpi, Screen.FromPoint(Cursor.Position), 680);
            }
            this.releases = ReleaseNotesService.LoadEmbeddedReleaseNotes();
            InitializeHistoryUI();
            BindReleases(this.releases, true);
        }

        private void InitializeHistoryUI()
        {
            this.Text = "FerrisPulse · 灵脉 - 更新日志与历史版本";
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(18, 19, 23);
            this.ForeColor = Color.White;
            this.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            Rectangle workArea = Screen.FromPoint(Cursor.Position).WorkingArea;
            int formW = Scale(680);
            int formH = Scale(550);
            int maxW = (int)(workArea.Width * 0.90);
            int maxH = (int)(workArea.Height * 0.88);
            if (maxW > 0 && formW > maxW) formW = maxW;
            if (maxH > 0 && formH > maxH) formH = maxH;
            if (formW < Scale(580)) formW = Math.Min(Scale(580), workArea.Width > 0 ? workArea.Width : Scale(580));
            if (formH < Scale(460)) formH = Math.Min(Scale(460), workArea.Height > 0 ? workArea.Height : Scale(460));

            this.ClientSize = new Size(formW, formH);
            this.DoubleBuffered = true;
            this.ShowInTaskbar = false;
            this.TopMost = false;
            this.KeyPreview = true;
            this.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape && !isDownloading)
                {
                    this.Close();
                }
            };

            int padX = Scale(20);

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
            lblTitle.Text = "更新日志与历史版本";
            lblTitle.Font = DpiScaleHelper.GetCachedScaledFont("Microsoft YaHei UI", 13.0F, FontStyle.Bold, DpiScale);
            lblTitle.ForeColor = Color.FromArgb(242, 246, 255);
            lblTitle.AutoSize = false;
            lblTitle.TextAlign = ContentAlignment.MiddleLeft;
            lblTitle.MouseDown += TitleBar_MouseDown;
            titleBar.Controls.Add(lblTitle);

            int closeSize = Math.Max(22, Scale(28));
            btnCloseTop = new ModernButton();
            btnCloseTop.IsCloseButton = true;
            btnCloseTop.Text = "";
            btnCloseTop.Size = new Size(closeSize, closeSize);
            btnCloseTop.Location = new Point(formW - padX - closeSize, topInset + Math.Max(0, (rowH - closeSize) / 2));
            btnCloseTop.NormalColor = Color.FromArgb(28, 32, 42);
            btnCloseTop.BorderColor = Color.FromArgb(44, 50, 64);
            btnCloseTop.HoverColor = Color.FromArgb(215, 45, 58);
            btnCloseTop.PressedColor = Color.FromArgb(175, 28, 42);
            btnCloseTop.ForeColor = Color.FromArgb(182, 192, 212);
            btnCloseTop.CornerRadius = Math.Max(5, Scale(7));
            btnCloseTop.Click += delegate(object s, EventArgs e) { if (!isDownloading) this.Close(); };
            titleBar.Controls.Add(btnCloseTop);

            lblTitle.Bounds = new Rectangle(padX, topInset, Math.Max(40, btnCloseTop.Left - padX - Scale(10)), rowH);

            this.Controls.Add(titleBar);

            // 2. 主体双栏区
            int contentY = Scale(60);
            int actionY = formH - Scale(20) - Scale(34);
            int contentH = actionY - Scale(14) - contentY;

            int timelineW = Scale(184);
            cardTimeline = new RoundedCard();
            cardTimeline.Location = new Point(padX, contentY);
            cardTimeline.Size = new Size(timelineW, contentH);
            cardTimeline.CornerRadius = Scale(8);
            cardTimeline.CardColor = Color.FromArgb(25, 27, 34);
            cardTimeline.BorderColor = Color.FromArgb(42, 46, 58);

            timelineList = new VersionTimelineControl(DpiScale);
            timelineList.Location = new Point(Scale(6), Scale(8));
            timelineList.Size = new Size(timelineW - Scale(12), contentH - Scale(16));
            timelineList.BackColor = cardTimeline.CardColor;
            timelineList.SelectionChanged += delegate(object s, EventArgs e)
            {
                if (!isDownloading)
                {
                    ApplySelectedEntry(timelineList.SelectedEntry);
                }
            };
            cardTimeline.Controls.Add(timelineList);
            this.Controls.Add(cardTimeline);

            int detailX = cardTimeline.Right + Scale(14);
            int detailW = formW - padX - detailX;
            cardDetail = new RoundedCard();
            cardDetail.Location = new Point(detailX, contentY);
            cardDetail.Size = new Size(detailW, contentH);
            cardDetail.CornerRadius = Scale(8);
            cardDetail.CardColor = Color.FromArgb(25, 27, 34);
            cardDetail.BorderColor = Color.FromArgb(42, 46, 58);

            pillSelectedVersion = new StatusPill();
            pillSelectedVersion.Location = new Point(Scale(20), Scale(15));
            pillSelectedVersion.Height = Scale(26);
            pillSelectedVersion.SetStatus("全部版本", Color.FromArgb(0, 230, 118));
            cardDetail.Controls.Add(pillSelectedVersion);

            lblSelectedDate = new Label();
            lblSelectedDate.Text = "";
            lblSelectedDate.Font = new Font("Microsoft YaHei UI", 8.8F);
            lblSelectedDate.ForeColor = Color.FromArgb(148, 158, 176);
            lblSelectedDate.BackColor = cardDetail.CardColor;
            lblSelectedDate.AutoSize = true;
            lblSelectedDate.Location = new Point(pillSelectedVersion.Right + Scale(12), Scale(19));
            cardDetail.Controls.Add(lblSelectedDate);

            btnSwitchVersion = new ModernButton();
            btnSwitchVersion.Text = "当前运行中";
            btnSwitchVersion.Font = new Font("Microsoft YaHei UI", 9.0F, FontStyle.Bold);
            btnSwitchVersion.Size = new Size(Scale(124), Scale(32));
            btnSwitchVersion.Location = new Point(detailW - Scale(20) - btnSwitchVersion.Width, Scale(12));
            btnSwitchVersion.CornerRadius = Scale(6);
            btnSwitchVersion.Visible = false;
            btnSwitchVersion.Click += BtnSwitchVersion_Click;
            cardDetail.Controls.Add(btnSwitchVersion);

            lblSelectedSummary = new Label();
            lblSelectedSummary.Text = "";
            lblSelectedSummary.Font = new Font("Microsoft YaHei UI", 9.6F, FontStyle.Bold);
            lblSelectedSummary.ForeColor = Color.FromArgb(225, 232, 245);
            lblSelectedSummary.BackColor = cardDetail.CardColor;
            lblSelectedSummary.AutoEllipsis = true;
            lblSelectedSummary.Location = new Point(Scale(20), Scale(48));
            lblSelectedSummary.Size = new Size(detailW - Scale(40), Scale(24));
            cardDetail.Controls.Add(lblSelectedSummary);

            dividerLine = new Panel();
            dividerLine.Location = new Point(Scale(18), Scale(78));
            dividerLine.Size = new Size(detailW - Scale(36), 1);
            dividerLine.BackColor = Color.FromArgb(38, 42, 54);
            cardDetail.Controls.Add(dividerLine);

            logScrollPanel = new ModernDarkScrollPanel();
            logScrollPanel.Location = new Point(Scale(16), Scale(86));
            logScrollPanel.Size = new Size(detailW - Scale(32), contentH - Scale(96));
            logScrollPanel.BackColor = cardDetail.CardColor;
            cardDetail.Controls.Add(logScrollPanel);

            this.Controls.Add(cardDetail);

            // 3. 底部下载进度区域 + 关闭按钮（常态下右侧仅留干净关闭按钮，不堆冗余提示）
            int fullW = formW - padX * 2;
            int btnCloseW = Scale(98);
            int btnCloseH = Scale(34);

            progressBar = new ModernProgressBar();
            progressBar.Location = new Point(padX, actionY + Scale(2));
            progressBar.Size = new Size(fullW, Scale(8));
            progressBar.Visible = false;
            progressBar.Value = 0;
            this.Controls.Add(progressBar);

            lblDownloadStatus = new Label();
            lblDownloadStatus.Font = new Font("Microsoft YaHei UI", 8.8F);
            lblDownloadStatus.ForeColor = Color.FromArgb(0, 230, 118);
            lblDownloadStatus.Location = new Point(padX, actionY + Scale(14));
            lblDownloadStatus.Size = new Size(fullW, Scale(20));
            lblDownloadStatus.TextAlign = ContentAlignment.MiddleCenter;
            lblDownloadStatus.AutoEllipsis = true;
            lblDownloadStatus.Visible = false;
            this.Controls.Add(lblDownloadStatus);

            btnCloseBottom = new ModernButton();
            btnCloseBottom.Text = "关闭";
            btnCloseBottom.Font = new Font("Microsoft YaHei UI", 9.2F, FontStyle.Bold);
            btnCloseBottom.Size = new Size(btnCloseW, btnCloseH);
            btnCloseBottom.Location = new Point(formW - padX - btnCloseW, actionY);
            btnCloseBottom.NormalColor = Color.FromArgb(36, 40, 52);
            btnCloseBottom.HoverColor = Color.FromArgb(48, 54, 70);
            btnCloseBottom.PressedColor = Color.FromArgb(28, 30, 40);
            btnCloseBottom.BorderColor = Color.FromArgb(58, 64, 82);
            btnCloseBottom.ForeColor = Color.White;
            btnCloseBottom.CornerRadius = Scale(6);
            btnCloseBottom.Click += delegate(object s, EventArgs e) { if (!isDownloading) this.Close(); };
            this.Controls.Add(btnCloseBottom);

            // 打开后后台静默同步云端历史（如远端发布了更高版本则无感刷新列表）
            this.Shown += delegate(object s, EventArgs e)
            {
                ReleaseNotesService.FetchRemoteReleaseNotesAsync(delegate(List<ReleaseNoteEntry> merged)
                {
                    if (this.IsDisposed || !this.IsHandleCreated || merged == null || merged.Count == 0) return;
                    this.BeginInvoke((MethodInvoker)delegate
                    {
                        if (this.IsDisposed || isDownloading) return;
                        BindReleases(merged, false);
                    });
                });
            };
        }

        protected override void OnApplyDpiScale(float scale)
        {
            base.OnApplyDpiScale(scale);
            if (timelineList != null)
            {
                timelineList.SetDpiScale(scale);
            }
            if (logScrollPanel != null)
            {
                logScrollPanel.UpdateDpiScale(scale);
            }
            if (lblSelectedDate != null && pillSelectedVersion != null)
            {
                lblSelectedDate.Location = new Point(
                    pillSelectedVersion.Right + Scale(12),
                    pillSelectedVersion.Top + (pillSelectedVersion.Height - lblSelectedDate.PreferredHeight) / 2);
            }
        }

        private void BindReleases(List<ReleaseNoteEntry> list, bool pickDefault)
        {
            this.releases = list ?? new List<ReleaseNoteEntry>();
            string currentVer = UpdateManager.CurrentVersion;
            if (timelineList != null)
            {
                timelineList.SetReleases(this.releases, currentVer);
            }

            if (pickDefault)
            {
                ReleaseNoteEntry defaultEntry = FindEntryByVersion(currentVer);
                if (defaultEntry == null && this.releases.Count > 0)
                {
                    defaultEntry = this.releases[0];
                }
                if (defaultEntry != null)
                {
                    SelectVersion(defaultEntry.Version);
                }
                else
                {
                    SelectAllVersions();
                }
            }
            else if (this.selectedEntry != null)
            {
                ReleaseNoteEntry updatedMatch = FindEntryByVersion(this.selectedEntry.Version);
                ApplySelectedEntry(updatedMatch);
            }
            else
            {
                ApplySelectedEntry(null);
            }
        }

        public bool SelectVersion(string version)
        {
            ReleaseNoteEntry match = FindEntryByVersion(version);
            if (match == null) return false;

            if (timelineList != null)
            {
                timelineList.SelectEntry(match);
            }
            ApplySelectedEntry(match);
            return true;
        }

        public void SelectAllVersions()
        {
            if (timelineList != null)
            {
                timelineList.SelectEntry(null);
            }
            ApplySelectedEntry(null);
        }

        private ReleaseNoteEntry FindEntryByVersion(string version)
        {
            if (string.IsNullOrEmpty(version) || releases == null) return null;
            for (int i = 0; i < releases.Count; i++)
            {
                if (UpdateManager.CompareVersions(releases[i].Version, version) == 0)
                {
                    return releases[i];
                }
            }
            return null;
        }

        private void ApplySelectedEntry(ReleaseNoteEntry entry)
        {
            this.selectedEntry = entry;
            string currentVer = UpdateManager.CurrentVersion;

            if (entry == null)
            {
                pillSelectedVersion.SetStatus("全部历史版本", Color.FromArgb(0, 230, 118));
                lblSelectedDate.Text = string.Format("共 {0} 个已发布版本", releases.Count);
                lblSelectedDate.Location = new Point(
                    pillSelectedVersion.Right + Scale(12),
                    pillSelectedVersion.Top + (pillSelectedVersion.Height - lblSelectedDate.PreferredHeight) / 2);
                lblSelectedSummary.Text = "按发布时间倒序展示历代核心功能更新";
                btnSwitchVersion.Visible = false;

                List<string> allLines = new List<string>();
                for (int i = 0; i < releases.Count; i++)
                {
                    ReleaseNoteEntry r = releases[i];
                    bool isCur = UpdateManager.CompareVersions(r.Version, currentVer) == 0;
                    allLines.Add(ReleaseNotesService.FormatVersionBanner(r.Version, r.ReleaseDate, isCur ? "当前版本" : ""));
                    if (r.Changelog != null && r.Changelog.Count > 0)
                    {
                        allLines.AddRange(r.Changelog);
                    }
                    else if (!string.IsNullOrEmpty(r.Summary))
                    {
                        allLines.Add(r.Summary);
                    }
                }
                logScrollPanel.SetChangelog(allLines, DpiScale);
                return;
            }

            string cleanVer = entry.GetCleanVersion();
            int cmp = UpdateManager.CompareVersions(entry.Version, currentVer);

            Color pillColor = Color.FromArgb(100, 185, 255);
            if (cmp == 0) pillColor = Color.FromArgb(0, 230, 118);
            else if (cmp > 0) pillColor = Color.FromArgb(0, 229, 255);

            pillSelectedVersion.SetStatus("v" + cleanVer, pillColor);
            lblSelectedDate.Text = string.IsNullOrEmpty(entry.ReleaseDate) ? "历史版本" : ("发布日期: " + entry.ReleaseDate);
            lblSelectedDate.Location = new Point(
                pillSelectedVersion.Right + Scale(12),
                pillSelectedVersion.Top + (pillSelectedVersion.Height - lblSelectedDate.PreferredHeight) / 2);
            lblSelectedSummary.Text = string.IsNullOrEmpty(entry.Summary) ? "功能优化与问题修复" : entry.Summary;

            btnSwitchVersion.Visible = true;
            if (cmp == 0)
            {
                btnSwitchVersion.Enabled = false;
                btnSwitchVersion.Text = "当前运行中";
                btnSwitchVersion.NormalColor = Color.FromArgb(32, 36, 46);
                btnSwitchVersion.HoverColor = Color.FromArgb(32, 36, 46);
                btnSwitchVersion.PressedColor = Color.FromArgb(32, 36, 46);
                btnSwitchVersion.BorderColor = Color.FromArgb(56, 62, 78);
                btnSwitchVersion.ForeColor = Color.FromArgb(140, 150, 170);
            }
            else if (cmp > 0)
            {
                btnSwitchVersion.Enabled = true;
                btnSwitchVersion.Text = "升级到此版本";
                btnSwitchVersion.NormalColor = Color.FromArgb(0, 200, 83);
                btnSwitchVersion.HoverColor = Color.FromArgb(0, 230, 118);
                btnSwitchVersion.PressedColor = Color.FromArgb(0, 160, 65);
                btnSwitchVersion.BorderColor = Color.FromArgb(0, 230, 118);
                btnSwitchVersion.ForeColor = Color.FromArgb(10, 24, 15);
            }
            else
            {
                btnSwitchVersion.Enabled = true;
                btnSwitchVersion.Text = "回退到此版本";
                btnSwitchVersion.NormalColor = Color.FromArgb(40, 44, 58);
                btnSwitchVersion.HoverColor = Color.FromArgb(54, 60, 78);
                btnSwitchVersion.PressedColor = Color.FromArgb(30, 33, 44);
                btnSwitchVersion.BorderColor = Color.FromArgb(255, 179, 0);
                btnSwitchVersion.ForeColor = Color.FromArgb(255, 224, 140);
            }

            List<string> singleLines = (entry.Changelog != null && entry.Changelog.Count > 0)
                ? new List<string>(entry.Changelog)
                : new List<string>() { string.IsNullOrEmpty(entry.Summary) ? "常规性能优化与问题修复" : entry.Summary };
            logScrollPanel.SetChangelog(singleLines, DpiScale);
        }

        private void BtnSwitchVersion_Click(object sender, EventArgs e)
        {
            if (isDownloading || selectedEntry == null) return;

            string cleanTarget = selectedEntry.GetCleanVersion();
            int cmp = UpdateManager.CompareVersions(selectedEntry.Version, UpdateManager.CurrentVersion);
            if (cmp == 0) return;

            string promptMsg;
            string promptTitle;
            if (cmp < 0)
            {
                promptTitle = "确认回退版本";
                promptMsg = string.Format(
                    "确定要下载并切换回 v{0} 吗？\r\n\r\n回退过程中软件将自动重启；旧版本可能不包含近期新增的功能。\r\n如希望长期使用该版本，可在「关于与更新」中取消勾选“自动检查更新”。",
                    cleanTarget);
            }
            else
            {
                promptTitle = "确认升级版本";
                promptMsg = string.Format("确定要下载并升级到 v{0} 吗？\r\n\r\n下载完成后软件将自动完成替换并重启。", cleanTarget);
            }

            DialogResult confirm = MessageBox.Show(this, promptMsg, promptTitle, MessageBoxButtons.YesNo, MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes) return;

            string downloadUrl = string.IsNullOrEmpty(selectedEntry.DownloadUrl)
                ? ReleaseNotesService.GetReleaseDownloadUrl(cleanTarget)
                : selectedEntry.DownloadUrl;
            if (string.IsNullOrEmpty(downloadUrl))
            {
                MessageBox.Show(this, "未找到该版本的有效下载地址。", "切换失败", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            StartVersionDownload(selectedEntry, downloadUrl, cleanTarget);
        }

        private void StartVersionDownload(ReleaseNoteEntry targetEntry, string downloadUrl, string cleanTarget)
        {
            isDownloading = true;
            if (timelineList != null) timelineList.Enabled = false;
            btnSwitchVersion.Enabled = false;
            btnCloseTop.Enabled = false;
            btnCloseBottom.Visible = false;

            int padX = Scale(20);
            int actionY = btnCloseBottom.Top;
            int fullW = this.ClientSize.Width - padX * 2;
            progressBar.Visible = true;
            lblDownloadStatus.Location = new Point(padX, actionY + Scale(14));
            lblDownloadStatus.Size = new Size(fullW, Scale(20));
            lblDownloadStatus.TextAlign = ContentAlignment.MiddleCenter;
            lblDownloadStatus.Visible = true;
            lblDownloadStatus.ForeColor = Color.FromArgb(0, 230, 118);
            lblDownloadStatus.Text = string.Format("正在从云端获取 v{0} ...", cleanTarget);
            progressBar.StartChargingFlow(Color.FromArgb(0, 200, 83), Color.FromArgb(0, 229, 255));

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string tempExe = Path.Combine(baseDir, "FerrisPulse.exe.update");

            UpdateManager.DownloadFileAsync(
                downloadUrl,
                tempExe,
                targetEntry.Sha256,
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
                            lblDownloadStatus.Text = string.Format("正在下载 v{0}: {1}% ({2:F1} MB / {3:F1} MB) · {4:F1} MB/s", cleanTarget, pct, readMb, totalMb, speedMb);
                        }
                        else
                        {
                            double readMb = bytesRead / (1024.0 * 1024.0);
                            double speedMb = speedBytesPerSec / (1024.0 * 1024.0);
                            lblDownloadStatus.Text = string.Format("正在下载 v{0}: {1:F1} MB · {2:F1} MB/s", cleanTarget, readMb, speedMb);
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
                            RestoreAfterDownloadFailure(string.IsNullOrEmpty(error) ? "下载失败，请检查网络连接" : ("下载失败: " + error), tempExe);
                            return;
                        }

                        string validateErr;
                        if (!ReleaseNotesService.ValidateDownloadedExecutable(tempExe, targetEntry.Sha256, out validateErr))
                        {
                            RestoreAfterDownloadFailure(string.IsNullOrEmpty(validateErr) ? "安装包校验失败" : validateErr, tempExe);
                            return;
                        }

                        progressBar.StopFlow(100, Color.FromArgb(0, 230, 118));
                        lblDownloadStatus.ForeColor = Color.FromArgb(0, 230, 118);
                        lblDownloadStatus.Text = string.Format("v{0} 准备就绪，正在替换并重启...", cleanTarget);

                        Timer restartTimer = new Timer();
                        restartTimer.Interval = 1000;
                        restartTimer.Tick += delegate(object s, EventArgs ev)
                        {
                            restartTimer.Stop();
                            restartTimer.Dispose();
                            UpdateManager.LaunchSelfReplace(tempExe);
                        };
                        restartTimer.Start();
                    });
                });
        }

        private void RestoreAfterDownloadFailure(string errorText, string tempExe)
        {
            isDownloading = false;
            try { File.Delete(tempExe); } catch { }

            progressBar.StopFlow();
            progressBar.Visible = false;

            if (timelineList != null) timelineList.Enabled = true;
            btnCloseTop.Enabled = true;
            btnCloseBottom.Visible = true;

            int padX = Scale(20);
            int availW = Math.Max(Scale(200), btnCloseBottom.Left - padX - Scale(14));
            lblDownloadStatus.Location = new Point(padX, btnCloseBottom.Top + (btnCloseBottom.Height - Scale(20)) / 2);
            lblDownloadStatus.Size = new Size(availW, Scale(20));
            lblDownloadStatus.TextAlign = ContentAlignment.MiddleLeft;
            lblDownloadStatus.ForeColor = Color.FromArgb(255, 110, 115);
            lblDownloadStatus.Text = errorText;
            lblDownloadStatus.Visible = true;

            ApplySelectedEntry(this.selectedEntry);
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

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using (Pen borderPen = new Pen(Color.FromArgb(42, 46, 58), 1))
            {
                e.Graphics.DrawRectangle(borderPen, 0, 0, this.Width - 1, this.Height - 1);
            }
        }

        private class VersionTimelineControl : Control
        {
            private class RowItem
            {
                public ReleaseNoteEntry Entry; // null 表示“全部版本”
                public string Title;
                public string Subtitle;
                public bool IsCurrent;
                public Rectangle Bounds;
            }

            private float scale;
            private readonly List<RowItem> rows = new List<RowItem>();
            private int selectedIndex = 0;
            private int hoverIndex = -1;
            private int scrollOffset = 0;
            private int totalHeight = 0;

            public event EventHandler SelectionChanged;

            public ReleaseNoteEntry SelectedEntry
            {
                get
                {
                    if (selectedIndex >= 0 && selectedIndex < rows.Count)
                    {
                        return rows[selectedIndex].Entry;
                    }
                    return null;
                }
            }

            public VersionTimelineControl(float dpiScale)
            {
                this.scale = (dpiScale > 0.1f) ? dpiScale : 1.0f;
                this.DoubleBuffered = true;
                this.SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                this.Cursor = Cursors.Hand;
            }

            public void SetDpiScale(float dpiScale)
            {
                float normalized = (dpiScale > 0.1f) ? dpiScale : 1.0f;
                if (Math.Abs(this.scale - normalized) < 0.001f) return;
                this.scale = normalized;
                RecalcBounds();
                Invalidate();
            }

            private int S(int v) { return (int)Math.Round(v * scale); }

            public void SetReleases(IList<ReleaseNoteEntry> list, string currentVersion)
            {
                ReleaseNoteEntry prevSelected = SelectedEntry;
                rows.Clear();

                RowItem allRow = new RowItem();
                allRow.Entry = null;
                allRow.Title = "全部版本";
                allRow.Subtitle = string.Format("共 {0} 个版本", list != null ? list.Count : 0);
                allRow.IsCurrent = false;
                rows.Add(allRow);

                if (list != null)
                {
                    for (int i = 0; i < list.Count; i++)
                    {
                        ReleaseNoteEntry e = list[i];
                        if (e == null) continue;
                        RowItem row = new RowItem();
                        row.Entry = e;
                        row.Title = "v" + e.GetCleanVersion();
                        row.Subtitle = string.IsNullOrEmpty(e.ReleaseDate) ? "历史发布" : e.ReleaseDate;
                        row.IsCurrent = UpdateManager.CompareVersions(e.Version, currentVersion) == 0;
                        rows.Add(row);
                    }
                }

                if (prevSelected != null)
                {
                    SelectEntry(prevSelected);
                }
                else if (selectedIndex >= rows.Count)
                {
                    selectedIndex = 0;
                }

                RecalcBounds();
                Invalidate();
            }

            public void SelectEntry(ReleaseNoteEntry entry)
            {
                if (entry == null)
                {
                    selectedIndex = 0;
                }
                else
                {
                    for (int i = 1; i < rows.Count; i++)
                    {
                        if (UpdateManager.CompareVersions(rows[i].Entry.Version, entry.Version) == 0)
                        {
                            selectedIndex = i;
                            break;
                        }
                    }
                }
                EnsureSelectedVisible();
                Invalidate();
            }

            private void RecalcBounds()
            {
                int rowH = S(56);
                int gap = S(6);
                int w = Math.Max(40, this.ClientSize.Width - S(12));
                int y = S(2);
                for (int i = 0; i < rows.Count; i++)
                {
                    rows[i].Bounds = new Rectangle(S(2), y, w, rowH);
                    y += rowH + gap;
                }
                totalHeight = y + S(4);
                ClampScroll();
            }

            private void ClampScroll()
            {
                int maxScroll = Math.Max(0, totalHeight - this.ClientSize.Height);
                if (scrollOffset > maxScroll) scrollOffset = maxScroll;
                if (scrollOffset < 0) scrollOffset = 0;
            }

            private void EnsureSelectedVisible()
            {
                if (selectedIndex < 0 || selectedIndex >= rows.Count) return;
                Rectangle b = rows[selectedIndex].Bounds;
                if (b.Top - scrollOffset < 0)
                {
                    scrollOffset = Math.Max(0, b.Top - S(4));
                }
                else if (b.Bottom - scrollOffset > this.ClientSize.Height)
                {
                    scrollOffset = Math.Max(0, b.Bottom - this.ClientSize.Height + S(4));
                }
                ClampScroll();
            }

            protected override void OnResize(EventArgs e)
            {
                base.OnResize(e);
                RecalcBounds();
            }

            protected override void OnMouseWheel(MouseEventArgs e)
            {
                base.OnMouseWheel(e);
                int maxScroll = Math.Max(0, totalHeight - this.ClientSize.Height);
                if (maxScroll <= 0) return;
                scrollOffset += (e.Delta > 0 ? -S(38) : S(38));
                ClampScroll();
                Invalidate();
            }

            protected override void OnMouseMove(MouseEventArgs e)
            {
                base.OnMouseMove(e);
                int hit = HitTest(e.Location);
                if (hit != hoverIndex)
                {
                    hoverIndex = hit;
                    Invalidate();
                }
            }

            protected override void OnMouseLeave(EventArgs e)
            {
                base.OnMouseLeave(e);
                if (hoverIndex != -1)
                {
                    hoverIndex = -1;
                    Invalidate();
                }
            }

            protected override void OnMouseDown(MouseEventArgs e)
            {
                base.OnMouseDown(e);
                if (e.Button != MouseButtons.Left) return;
                int hit = HitTest(e.Location);
                if (hit >= 0 && hit < rows.Count && hit != selectedIndex)
                {
                    selectedIndex = hit;
                    Invalidate();
                    if (SelectionChanged != null) SelectionChanged(this, EventArgs.Empty);
                }
            }

            private int HitTest(Point pt)
            {
                for (int i = 0; i < rows.Count; i++)
                {
                    Rectangle r = rows[i].Bounds;
                    r.Offset(0, -scrollOffset);
                    if (r.Contains(pt)) return i;
                }
                return -1;
            }

            protected override void OnPaint(PaintEventArgs e)
            {
                base.OnPaint(e);
                Graphics g = e.Graphics;
                g.Clear(this.BackColor);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

                Font titleFont = DpiScaleHelper.GetCachedScaledFont("Microsoft YaHei UI", 9.5F, FontStyle.Bold, scale);
                Font subFont = DpiScaleHelper.GetCachedScaledFont("Microsoft YaHei UI", 8.2F, FontStyle.Regular, scale);
                Font badgeFont = DpiScaleHelper.GetCachedScaledFont("Microsoft YaHei UI", 7.5F, FontStyle.Bold, scale);

                TextFormatFlags measureFlags = TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

                for (int i = 0; i < rows.Count; i++)
                {
                    RowItem row = rows[i];
                    Rectangle r = row.Bounds;
                    r.Offset(0, -scrollOffset);
                    if (r.Bottom < 0 || r.Top > this.ClientSize.Height) continue;

                    bool isSel = (i == selectedIndex);
                    bool isHov = (i == hoverIndex);

                    if (isSel || isHov)
                    {
                        Color fill = isSel ? Color.FromArgb(30, 44, 42) : Color.FromArgb(32, 36, 46);
                        using (GraphicsPath path = ModernThemeHelper.GetRoundedRectangle(r, S(6)))
                        using (SolidBrush br = new SolidBrush(fill))
                        {
                            g.FillPath(br, path);
                            if (isSel)
                            {
                                using (Pen borderPen = new Pen(Color.FromArgb(46, 86, 72), 1))
                                {
                                    g.DrawPath(borderPen, path);
                                }
                            }
                        }
                    }

                    if (isSel)
                    {
                        using (SolidBrush bar = new SolidBrush(Color.FromArgb(0, 226, 120)))
                        {
                            g.FillRectangle(bar, r.X + S(2), r.Y + S(10), S(3), r.Height - S(20));
                        }
                    }

                    int textX = r.X + S(12);
                    Color titleColor = isSel ? Color.FromArgb(245, 250, 255) : Color.FromArgb(215, 223, 236);
                    Size tSize = TextRenderer.MeasureText(g, row.Title, titleFont, new Size(r.Width, r.Height), measureFlags);
                    Rectangle titleRect = new Rectangle(textX, r.Y + S(7), r.Width - S(16), S(22));
                    TextRenderer.DrawText(g, row.Title, titleFont, titleRect, titleColor,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

                    if (row.IsCurrent)
                    {
                        Size bSize = TextRenderer.MeasureText(g, "当前", badgeFont, new Size(100, 40), measureFlags);
                        int pillW = bSize.Width + S(10);
                        int pillH = S(18);
                        int pillX = Math.Min(r.Right - pillW - S(8), textX + tSize.Width + S(8));
                        int pillY = titleRect.Y + (titleRect.Height - pillH) / 2;
                        Rectangle pillRect = new Rectangle(pillX, pillY, pillW, pillH);
                        using (GraphicsPath pillPath = ModernThemeHelper.GetRoundedRectangle(pillRect, pillH / 2))
                        using (SolidBrush pillBr = new SolidBrush(Color.FromArgb(0, 190, 90)))
                        {
                            g.FillPath(pillBr, pillPath);
                        }
                        TextRenderer.DrawText(g, "当前", badgeFont, pillRect, Color.FromArgb(12, 24, 16),
                            TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                    }

                    Rectangle subRect = new Rectangle(textX, r.Y + S(30), r.Width - S(16), S(20));
                    TextRenderer.DrawText(g, row.Subtitle, subFont, subRect, Color.FromArgb(140, 150, 170),
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix | TextFormatFlags.EndEllipsis);

                    // 在相邻条目之间绘制细腻微光分割线（选中/悬停行周边自动避让）
                    if (i < rows.Count - 1)
                    {
                        bool nextActive = (i + 1 == selectedIndex) || (i + 1 == hoverIndex);
                        if (!isSel && !isHov && !nextActive)
                        {
                            int divY = r.Bottom + S(3);
                            Color divColor = (i == 0) ? Color.FromArgb(48, 54, 68) : Color.FromArgb(35, 39, 52);
                            using (Pen divPen = new Pen(divColor, 1))
                            {
                                g.DrawLine(divPen, r.Left + S(12), divY, r.Right - S(12), divY);
                            }
                        }
                    }
                }

                // 绘制左侧纤细暗黑滚动条
                int maxScroll = totalHeight - this.ClientSize.Height;
                if (maxScroll > 0 && this.ClientSize.Height > S(16))
                {
                    int barW = Math.Max(3, S(4));
                    int barX = this.ClientSize.Width - barW - S(1);
                    int clientH = this.ClientSize.Height;
                    float ratio = (float)clientH / (float)totalHeight;
                    int thumbH = Math.Max(S(24), (int)(clientH * ratio));
                    if (thumbH > clientH - S(8)) thumbH = clientH - S(8);
                    int trackH = clientH - thumbH;
                    int thumbY = (int)((float)scrollOffset / (float)maxScroll * trackH);
                    Rectangle thumbRect = new Rectangle(barX, thumbY, barW, thumbH);
                    using (GraphicsPath thumbPath = ModernThemeHelper.GetRoundedRectangle(thumbRect, barW / 2))
                    using (SolidBrush thumbBr = new SolidBrush(Color.FromArgb(62, 70, 88)))
                    {
                        g.FillPath(thumbBr, thumbPath);
                    }
                }
            }
        }
    }
}
