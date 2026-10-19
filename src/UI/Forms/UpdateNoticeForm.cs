// ============================================================================
// 模块名称: UpdateNoticeForm.cs
// 所属区域: UI/Forms (二级窗体层)
// 核心职责:
//   新版本升级提示弹窗：展示新版本特性、暗黑日志滚动卡片、流光进度条与自动重启置换
// 依赖关系: ModernThemeHelper, ModernButton, ModernProgressBar, StatusPill, RoundedCard, UpdateManager
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 2. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
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
        private ModernDarkScrollPanel logScrollPanel;
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
            this.updateInfo = info ?? new UpdateInfo();
            this.currentVersion = currentVersion ?? "";

            InitializeUpdateUI();
        }

        private void InitializeUpdateUI()
        {
            this.Text = "发现新版本";
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(18, 19, 23);
            this.ForeColor = Color.White;
            this.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            int formW = Scale(580);
            int formH = Scale(520);
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

            // 1. 顶部标题栏
            Panel titleBar = new Panel();
            titleBar.Dock = DockStyle.Top;
            titleBar.Height = Scale(38);
            titleBar.BackColor = Color.FromArgb(22, 24, 30);
            titleBar.MouseDown += TitleBar_MouseDown;

            Label lblTitle = new Label();
            lblTitle.Text = "发现新版本";
            lblTitle.Font = new Font("Microsoft YaHei UI", 9.2F, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(225, 230, 242);
            lblTitle.Location = new Point(padX, Scale(9));
            lblTitle.AutoSize = true;
            lblTitle.MouseDown += TitleBar_MouseDown;
            titleBar.Controls.Add(lblTitle);

            btnClose = new ModernButton();
            btnClose.Text = "✕";
            btnClose.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            btnClose.Size = new Size(Scale(34), Scale(28));
            btnClose.Location = new Point(formW - padX - Scale(18), Scale(5));
            btnClose.NormalColor = Color.Transparent;
            btnClose.HoverColor = Color.FromArgb(232, 17, 35);
            btnClose.PressedColor = Color.FromArgb(180, 10, 25);
            btnClose.ForeColor = Color.FromArgb(180, 180, 180);
            btnClose.CornerRadius = Scale(4);
            btnClose.Click += delegate(object s, EventArgs e) { this.Close(); };
            titleBar.Controls.Add(btnClose);

            this.Controls.Add(titleBar);

            // 2. 版本头部信息 (动态对齐，永不重叠)
            int headerY = Scale(54);

            string rawVer = string.IsNullOrEmpty(updateInfo.LatestVersion) ? "2.0.0" : updateInfo.LatestVersion;
            // 剥离可能存在的冗余字样，统一纯净规范为 vX.X.X
            string cleanVer = rawVer.Trim().TrimStart('v', 'V');
            int spaceIdx = cleanVer.IndexOf(' ');
            if (spaceIdx > 0) cleanVer = cleanVer.Substring(0, spaceIdx);
            string verText = "v" + cleanVer;

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
            // 严格根据 pillVersion 实际渲染右侧边缘动态推算坐标，留出舒适间距，永不重叠
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

            Label lblLogTitle = new Label();
            lblLogTitle.Text = "更新日志与改进特性:";
            lblLogTitle.Font = new Font("Microsoft YaHei UI", 9.2F, FontStyle.Bold);
            lblLogTitle.ForeColor = Color.FromArgb(235, 240, 255);
            lblLogTitle.BackColor = cardChangelog.CardColor;
            lblLogTitle.Location = new Point(Scale(20), Scale(16));
            lblLogTitle.AutoSize = true;
            lblLogTitle.MouseDown += TitleBar_MouseDown;
            cardChangelog.Controls.Add(lblLogTitle);

            logScrollPanel = new ModernDarkScrollPanel();
            logScrollPanel.Location = new Point(Scale(16), Scale(44));
            logScrollPanel.Size = new Size(cardW - Scale(32), cardH - Scale(54));
            logScrollPanel.BackColor = Color.Transparent;
            RenderChangelog();
            cardChangelog.Controls.Add(logScrollPanel);

            this.Controls.Add(cardChangelog);

            // 4. 底部进度条与控制按钮区域 (沉底排布，下留白舒适 24px)
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

            // 5. 挂接原生圆角与阴影已由 DpiAwareForm 自动处理

            // 6. SWR (Stale-While-Revalidate) 模式：显示后静默发起后台重检
            this.Shown += delegate(object s, EventArgs e)
            {
                TriggerBackgroundRecheck();
            };
        }

        private void RenderChangelog()
        {
            if (logScrollPanel == null) return;
            logScrollPanel.Content.Controls.Clear();

            int logItemY = 4;
            int itemWidth = logScrollPanel.Content.Width > 0 ? logScrollPanel.Content.Width - 8 : (logScrollPanel.Width - 20);

            if (updateInfo.Changelog != null && updateInfo.Changelog.Count > 0)
            {
                System.Collections.Generic.List<string> displayItems = new System.Collections.Generic.List<string>();
                for (int i = 0; i < updateInfo.Changelog.Count; i++)
                {
                    string entry = updateInfo.Changelog[i];
                    if (string.IsNullOrEmpty(entry)) continue;

                    if (entry.Contains("【") && entry.IndexOf("【") != entry.LastIndexOf("【"))
                    {
                        string[] parts = System.Text.RegularExpressions.Regex.Split(entry, @"(?=【)");
                        foreach (string p in parts)
                        {
                            string cp = p.Trim().TrimEnd(',', '，', ';', '；');
                            if (!string.IsNullOrEmpty(cp)) displayItems.Add(cp);
                        }
                    }
                    else
                    {
                        displayItems.Add(entry);
                    }
                }

                for (int i = 0; i < displayItems.Count; i++)
                {
                    string itemText = "•  " + displayItems[i];
                    Label lblItem = new Label();
                    lblItem.Text = itemText;
                    lblItem.Font = new Font("Microsoft YaHei UI", 9F);
                    lblItem.ForeColor = Color.FromArgb(190, 198, 215);
                    lblItem.BackColor = Color.Transparent;
                    lblItem.Location = new Point(4, logItemY);
                    lblItem.Width = itemWidth;

                    // 计算文本实际换行所需高度，自适应排版绝不出现横向白滚动条
                    Size measured = TextRenderer.MeasureText(itemText, lblItem.Font, new Size(itemWidth, 0),
                        TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl);
                    int itemH = Math.Max(26, measured.Height + 6);
                    lblItem.Height = itemH;

                    logScrollPanel.Content.Controls.Add(lblItem);
                    logItemY += itemH + 8;
                }
            }
            else
            {
                Label lblItem = new Label();
                lblItem.Text = "•  常规性能提升与稳定性修复";
                lblItem.Font = new Font("Microsoft YaHei UI", 9F);
                lblItem.ForeColor = Color.FromArgb(190, 198, 215);
                lblItem.BackColor = Color.Transparent;
                lblItem.Location = new Point(4, logItemY);
                lblItem.Width = itemWidth;
                lblItem.Height = 28;
                logScrollPanel.Content.Controls.Add(lblItem);
            }

            logScrollPanel.UpdateContentLayout();
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

                    // 比对当前展示版本与最新探测版本
                    int cmp = UpdateManager.CompareVersions(info.LatestVersion, this.updateInfo.LatestVersion);
                    if (cmp > 0)
                    {
                        // 探测到更高的增量/补丁版本，就地平滑热替换并浮现翡翠绿微光提示
                        UpdateDisplay(info, true);
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

            string rawVer = string.IsNullOrEmpty(updateInfo.LatestVersion) ? "2.0.0" : updateInfo.LatestVersion;
            string cleanVer = rawVer.Trim().TrimStart('v', 'V');
            int spaceIdx = cleanVer.IndexOf(' ');
            if (spaceIdx > 0) cleanVer = cleanVer.Substring(0, spaceIdx);
            string verText = "v" + cleanVer;

            if (pillVersion != null)
            {
                pillVersion.SetStatus(verText, Color.FromArgb(0, 230, 118));
            }
            if (lblReleaseDate != null && pillVersion != null)
            {
                lblReleaseDate.Text = string.IsNullOrEmpty(updateInfo.ReleaseDate) ? "最新发布" : ("发布日期: " + updateInfo.ReleaseDate);
                lblReleaseDate.Location = new Point(pillVersion.Right + 16, pillVersion.Top + (pillVersion.Height - lblReleaseDate.PreferredHeight) / 2);
            }

            RenderChangelog();

            if (showToast && lblHotReloadHint != null && pillVersion != null)
            {
                lblHotReloadHint.Text = "✨ 已同步最新版本 (" + verText + ")";
                lblHotReloadHint.Location = new Point(580 - 24 - lblHotReloadHint.PreferredWidth, pillVersion.Top + (pillVersion.Height - lblHotReloadHint.PreferredHeight) / 2);
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

        private void BtnUpgrade_Click(object sender, EventArgs e)
        {
            if (string.IsNullOrEmpty(updateInfo.DownloadUrl))
            {
                MessageBox.Show(this, "未找到有效的下载地址。", "更新错误", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            btnLater.Visible = false;
            btnUpgrade.Visible = false;
            progressBar.Visible = true;
            lblProgressText.Visible = true;
            progressBar.StartChargingFlow(Color.FromArgb(0, 200, 83), Color.FromArgb(0, 229, 255));
            lblProgressText.Text = "正在同步更新组件...";

            string baseDir = AppDomain.CurrentDomain.BaseDirectory;
            string tempExe = Path.Combine(baseDir, "FerrisPulse.exe.update");

            UpdateManager.DownloadFileAsync(
                updateInfo.DownloadUrl,
                tempExe,
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
                            progressBar.StopFlow();
                            progressBar.Visible = false;
                            lblProgressText.ForeColor = Color.FromArgb(255, 171, 0);
                            lblProgressText.Text = "同步失败: " + error;
                            btnLater.Visible = true;
                            btnUpgrade.Visible = true;
                            btnUpgrade.Text = "重试升级";
                            return;
                        }

                        // SHA256 完整性校验（若远端提供有效校验值）
                        if (!string.IsNullOrEmpty(updateInfo.Sha256) && updateInfo.Sha256.Length == 64 && !updateInfo.Sha256.StartsWith("dummy"))
                        {
                            string localSha = UpdateManager.ComputeFileSha256(tempExe);
                            if (!string.Equals(localSha, updateInfo.Sha256, StringComparison.OrdinalIgnoreCase))
                            {
                                progressBar.StopFlow();
                                progressBar.Visible = false;
                                lblProgressText.ForeColor = Color.FromArgb(255, 82, 82);
                                lblProgressText.Text = "更新包校验失败，文件可能已损坏！";
                                btnLater.Visible = true;
                                btnUpgrade.Visible = true;
                                try { File.Delete(tempExe); } catch { }
                                return;
                            }
                        }

                        progressBar.StopFlow(100, Color.FromArgb(0, 230, 118));
                        lblProgressText.ForeColor = Color.FromArgb(0, 230, 118);
                        lblProgressText.Text = "更新组件就绪，正在准备装载并重启...";

                        // 延迟 1.2 秒给用户反馈感，然后唤醒原子置换命令并退出
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
