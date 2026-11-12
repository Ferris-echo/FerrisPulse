// ============================================================================
// 模块名称: GameProcessManagerForm.cs
// 所属区域: UI/Forms (业务对话框层)
// 核心职责:
//   现代化左右双栏电竞游戏进程控制台：
//   1. 宽屏左右双栏布局 (左栏运行应用库 + 实时搜索过滤 + 后台静默自适应自动刷新；右栏生效游戏列表 + 专属策略控制)
//   2. Windows 原生 API 进程高清图标提取、内存高速缓存与本地磁盘永久化存储
//   3. 与设备优先级管理页完全统一的矢量级微型添加 [+] 与删除 [✕] 按键设计，删除与清空具备二次确认框
//   4. 列表精准展示实际生效数值（如“3000DPI · 8000Hz”与“跟随默认预设”），进程存活常驻 vs 仅前台窗口激活双策略切换
// 依赖关系: Controls.ModernProcessListControl, Controls.ModernButton, Controls.ModernCheckBox,
//           Controls.RoundedCard, Models.RunningProcessItem, Core.NativeMethods
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
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class GameProcessManagerForm : DpiAwareForm
    {
        private List<string> monitoredList;
        private bool fallbackEnabled;
        private int currentMode; // 0 = 仅前台激活模式, 1 = 进程存活常驻模式 (推荐)
        private Dictionary<string, GameProfile> gameProfiles;
        private int[] availableDpis;
        private int[] availableRates;
        private Action<List<string>, bool, int, Dictionary<string, GameProfile>> onSaveProfileCallback;

        // In-memory icon and title cache for process executables (prevents disk I/O lag and memory leaks)
        private static readonly Dictionary<string, Image> processIconCache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);
        private static readonly Dictionary<string, string> processTitleCache = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        private List<RunningProcessItem> rawRunningProcesses = new List<RunningProcessItem>();

        private Label lblRunningTitle;
        private Panel pnlSearch;
        private TextBox txtSearch;
        private ModernProcessListControl ctlRunningList;
        private ModernButton btnRefreshRunning;
        private ModernButton btnBrowseExe;

        private Label lblMonitoredTitle;
        private ModernProcessListControl ctlMonitoredList;
        private ModernButton btnClearAll;

        private Label lblModeTitle;
        private ModernButton btnModePersistent;
        private ModernButton btnModeForeground;
        private Panel pnlModeCard;
        private ModernCheckBox chkFallback;
        private ModernButton btnClose;
        private Timer autoRefreshTimer;
        private bool isRefreshing = false;

        public GameProcessManagerForm(float scale, List<string> initialList, bool initialFallback, Action<List<string>, bool> callback)
            : this(scale, initialList, initialFallback, 1, null, (list, fb, mode, profs) => { if (callback != null) callback(list, fb); })
        {
        }

        public GameProcessManagerForm(float scale, List<string> initialList, bool initialFallback, int initialMode, Action<List<string>, bool, int> callback)
            : this(scale, initialList, initialFallback, initialMode, null, (list, fb, mode, profs) => { if (callback != null) callback(list, fb, mode); })
        {
        }

        public GameProcessManagerForm(float scale, List<string> initialList, bool initialFallback, int initialMode,
                                      Dictionary<string, GameProfile> initialProfiles,
                                      Action<List<string>, bool, int, Dictionary<string, GameProfile>> callback)
            : this(scale, initialList, initialFallback, initialMode, initialProfiles, null, null, callback)
        {
        }

        public GameProcessManagerForm(float scale, List<string> initialList, bool initialFallback, int initialMode,
                                      Dictionary<string, GameProfile> initialProfiles,
                                      int[] dpiValues, int[] pollingRates,
                                      Action<List<string>, bool, int, Dictionary<string, GameProfile>> callback)
            : base(scale)
        {
            this.monitoredList = initialList != null ? new List<string>(initialList) : new List<string>();
            this.fallbackEnabled = initialFallback;
            this.currentMode = (initialMode == 0) ? 0 : 1;
            this.gameProfiles = initialProfiles != null ? new Dictionary<string, GameProfile>(initialProfiles, StringComparer.OrdinalIgnoreCase) : new Dictionary<string, GameProfile>(StringComparer.OrdinalIgnoreCase);
            this.availableDpis = dpiValues;
            this.availableRates = pollingRates;
            this.onSaveProfileCallback = callback;

            InitializeFormUI();
        }

        private int S(int val) { return (int)Math.Round(val * dpiScale); }

        private void InitializeFormUI()
        {
            this.Text = "FerrisPulse · 电竞游戏进程管理与自动捕获";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color.FromArgb(13, 16, 23);
            this.ForeColor = Color.FromArgb(240, 245, 255);
            this.KeyPreview = true;

            int clientW = S(760);
            int clientH = S(575);
            this.ClientSize = new Size(clientW, clientH);

            int pad = S(16);

            // ================= 1. Top Header =================
            Label lblTitle = new Label();
            lblTitle.Text = "🎮 电竞游戏进程管理与自动捕获";
            lblTitle.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(0, 230, 118);
            lblTitle.Location = new Point(pad, S(12));
            lblTitle.Size = new Size(clientW - pad * 2, S(22));
            lblTitle.BackColor = Color.Transparent;
            this.Controls.Add(lblTitle);

            Label lblSub = new Label();
            lblSub.Text = "将游戏添加至守护列表后，启动游戏即自动升频与切换 DPI 档位；退出游戏自动恢复常规桌面参数。";
            lblSub.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular);
            lblSub.ForeColor = Color.FromArgb(120, 135, 155);
            lblSub.Location = new Point(pad, S(34));
            lblSub.Size = new Size(clientW - pad * 2, S(16));
            lblSub.BackColor = Color.Transparent;
            this.Controls.Add(lblSub);

            // ================= 2. Column Geometry =================
            int colGap = S(16);
            int leftColW = S(390);
            int rightColW = clientW - pad * 2 - leftColW - colGap; // 322px
            int rightColX = pad + leftColW + colGap;

            // ================= 3. Left Column: Running Processes Library =================
            lblRunningTitle = new Label();
            lblRunningTitle.Text = "⚡ 当前正在运行的应用与游戏";
            lblRunningTitle.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold);
            lblRunningTitle.ForeColor = Color.FromArgb(220, 232, 248);
            lblRunningTitle.Location = new Point(pad, S(54));
            lblRunningTitle.Size = new Size(leftColW, S(18));
            lblRunningTitle.BackColor = Color.Transparent;
            this.Controls.Add(lblRunningTitle);

            // Modern Rounded Search Input Box
            pnlSearch = new Panel();
            pnlSearch.Location = new Point(pad, S(76));
            pnlSearch.Size = new Size(leftColW, S(30));
            pnlSearch.BackColor = Color.FromArgb(20, 25, 36);
            pnlSearch.Paint += (s, e) =>
            {
                Rectangle r = new Rectangle(0, 0, pnlSearch.Width - 1, pnlSearch.Height - 1);
                using (GraphicsPath path = RoundedCard.GetRoundedRectangle(r, S(6)))
                {
                    using (Pen p = new Pen(Color.FromArgb(38, 48, 68), 1f))
                    {
                        e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        e.Graphics.DrawPath(p, path);
                    }
                }
                // Draw Search Icon 🔍
                using (Font fontIco = DpiScaleHelper.CreateScaledFont("Segoe UI Emoji", 8.2F, FontStyle.Regular, dpiScale))
                {
                    TextRenderer.DrawText(e.Graphics, "🔍", fontIco, new Rectangle(S(6), 0, S(20), pnlSearch.Height), Color.FromArgb(120, 138, 160),
                        TextFormatFlags.VerticalCenter | TextFormatFlags.HorizontalCenter);
                }
            };
            this.Controls.Add(pnlSearch);

            txtSearch = new TextBox();
            txtSearch.BorderStyle = BorderStyle.None;
            txtSearch.BackColor = Color.FromArgb(20, 25, 36);
            txtSearch.ForeColor = Color.FromArgb(240, 245, 255);
            txtSearch.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Regular);
            txtSearch.Location = new Point(S(28), S(6));
            txtSearch.Size = new Size(leftColW - S(36), S(18));
            txtSearch.TextChanged += (s, e) => ApplySearchFilter();
            pnlSearch.Controls.Add(txtSearch);

            // Left Process List Control
            ctlRunningList = new ModernProcessListControl();
            ctlRunningList.DpiScale = dpiScale;
            ctlRunningList.ActionType = ProcessListActionType.Add;
            ctlRunningList.EmptyPlaceholderText = "未找到正在运行的用户应用或游戏进程";
            ctlRunningList.Location = new Point(pad, S(112));
            ctlRunningList.Size = new Size(leftColW, S(364));
            ctlRunningList.OnActionClicked += (item) => AddGameProcess(item.ProcessName);
            ctlRunningList.OnItemDoubleClicked += (item) => AddGameProcess(item.ProcessName);
            this.Controls.Add(ctlRunningList);

            // Left Action Buttons Row
            int leftBtnY = S(484);
            int btnH = S(28);

            btnRefreshRunning = new ModernButton();
            btnRefreshRunning.Text = "🔄 刷新运行列表";
            btnRefreshRunning.Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Regular);
            btnRefreshRunning.NormalColor = Color.FromArgb(24, 30, 42);
            btnRefreshRunning.HoverColor = Color.FromArgb(34, 44, 62);
            btnRefreshRunning.BorderColor = Color.FromArgb(42, 54, 76);
            btnRefreshRunning.TextColor = Color.FromArgb(190, 205, 225);
            btnRefreshRunning.CornerRadius = S(6);
            btnRefreshRunning.Cursor = Cursors.Hand;
            btnRefreshRunning.Location = new Point(pad, leftBtnY);
            btnRefreshRunning.Size = new Size(S(125), btnH);
            btnRefreshRunning.Click += (s, e) => RefreshRunningProcesses();
            this.Controls.Add(btnRefreshRunning);

            btnBrowseExe = new ModernButton();
            btnBrowseExe.Text = "📁 浏览本地 .exe...";
            btnBrowseExe.Font = new Font("Microsoft YaHei UI", 8.2F, FontStyle.Regular);
            btnBrowseExe.NormalColor = Color.FromArgb(24, 30, 42);
            btnBrowseExe.HoverColor = Color.FromArgb(34, 44, 62);
            btnBrowseExe.BorderColor = Color.FromArgb(42, 54, 76);
            btnBrowseExe.TextColor = Color.FromArgb(190, 205, 225);
            btnBrowseExe.CornerRadius = S(6);
            btnBrowseExe.Cursor = Cursors.Hand;
            btnBrowseExe.Location = new Point(pad + S(135), leftBtnY);
            btnBrowseExe.Size = new Size(S(145), btnH);
            btnBrowseExe.Click += (s, e) => BrowseLocalExe();
            this.Controls.Add(btnBrowseExe);

            // ================= 4. Right Column: Monitored Games & Strategy =================
            lblMonitoredTitle = new Label();
            lblMonitoredTitle.Text = "📋 已生效的游戏列表";
            lblMonitoredTitle.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold);
            lblMonitoredTitle.ForeColor = Color.FromArgb(0, 230, 118);
            lblMonitoredTitle.Location = new Point(rightColX, S(54));
            lblMonitoredTitle.Size = new Size(S(220), S(18));
            lblMonitoredTitle.BackColor = Color.Transparent;
            this.Controls.Add(lblMonitoredTitle);

            btnClearAll = new ModernButton();
            btnClearAll.Text = "清空全部";
            btnClearAll.Font = new Font("Microsoft YaHei UI", 7.8F, FontStyle.Regular);
            btnClearAll.NormalColor = Color.FromArgb(26, 20, 24);
            btnClearAll.HoverColor = Color.FromArgb(42, 28, 34);
            btnClearAll.BorderColor = Color.FromArgb(64, 36, 44);
            btnClearAll.TextColor = Color.FromArgb(240, 140, 150);
            btnClearAll.CornerRadius = S(4);
            btnClearAll.Cursor = Cursors.Hand;
            btnClearAll.Location = new Point(rightColX + rightColW - S(75), S(50));
            btnClearAll.Size = new Size(S(75), S(22));
            btnClearAll.Click += (s, e) =>
            {
                if (monitoredList.Count > 0)
                {
                    DialogResult dr = MessageBox.Show(this,
                        "确定要清空全部已生效的游戏列表吗？\n清空后将同步移除所有关联的游戏专属策略配置。",
                        "清空确认 · FerrisPulse",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Warning);

                    if (dr == DialogResult.Yes)
                    {
                        monitoredList.Clear();
                        if (gameProfiles != null)
                        {
                            gameProfiles.Clear();
                        }
                        RefreshMonitoredList();
                    }
                }
            };
            this.Controls.Add(btnClearAll);

            // Right Monitored Process List Control (expanded height to S(246) for comfortable view)
            ctlMonitoredList = new ModernProcessListControl();
            ctlMonitoredList.DpiScale = dpiScale;
            ctlMonitoredList.ActionType = ProcessListActionType.Delete;
            ctlMonitoredList.EmptyPlaceholderText = "暂无守护游戏 (双击右侧列表定制专属策略)";
            ctlMonitoredList.Location = new Point(rightColX, S(76));
            ctlMonitoredList.Size = new Size(rightColW, S(246));
            ctlMonitoredList.OnActionClicked += (item) => RemoveGameProcess(item.ProcessName);
            ctlMonitoredList.OnItemDoubleClicked += (item) => OpenGameProfileSetting(item.ProcessName);
            ctlMonitoredList.OnSettingsClicked += (item) => OpenGameProfileSetting(item.ProcessName);
            this.Controls.Add(ctlMonitoredList);

            // Section: Strategy Mode (shifted down to S(330))
            int stratY = S(330);
            lblModeTitle = new Label();
            lblModeTitle.Text = "⚙️ 切换守护模式 (避免切屏频繁跳变)";
            lblModeTitle.Font = new Font("Microsoft YaHei UI", 8.6F, FontStyle.Bold);
            lblModeTitle.ForeColor = Color.FromArgb(220, 232, 248);
            lblModeTitle.Location = new Point(rightColX, stratY);
            lblModeTitle.Size = new Size(rightColW, S(18));
            lblModeTitle.BackColor = Color.Transparent;
            this.Controls.Add(lblModeTitle);

            int modeBtnW = (rightColW - S(8)) / 2;
            int modeBtnH = S(28);
            int modeBtnY = stratY + S(22);

            btnModePersistent = new ModernButton();
            btnModePersistent.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Bold);
            btnModePersistent.CornerRadius = S(5);
            btnModePersistent.Cursor = Cursors.Hand;
            btnModePersistent.Location = new Point(rightColX, modeBtnY);
            btnModePersistent.Size = new Size(modeBtnW, modeBtnH);
            btnModePersistent.Click += (s, e) => SetGameOptMode(1);
            this.Controls.Add(btnModePersistent);

            btnModeForeground = new ModernButton();
            btnModeForeground.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular);
            btnModeForeground.CornerRadius = S(5);
            btnModeForeground.Cursor = Cursors.Hand;
            btnModeForeground.Location = new Point(rightColX + modeBtnW + S(8), modeBtnY);
            btnModeForeground.Size = new Size(modeBtnW, modeBtnH);
            btnModeForeground.Click += (s, e) => SetGameOptMode(0);
            this.Controls.Add(btnModeForeground);

            // Mode Explanation Micro-Card (height expanded to S(68) to prevent text clipping)
            int modeCardH = S(68);
            pnlModeCard = new Panel();
            pnlModeCard.Location = new Point(rightColX, modeBtnY + modeBtnH + S(6));
            pnlModeCard.Size = new Size(rightColW, modeCardH);
            pnlModeCard.BackColor = Color.FromArgb(17, 22, 31);
            pnlModeCard.Paint += (s, e) =>
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                Rectangle r = new Rectangle(0, 0, pnlModeCard.Width - 1, pnlModeCard.Height - 1);
                using (GraphicsPath path = RoundedCard.GetRoundedRectangle(r, S(6)))
                {
                    using (SolidBrush br = new SolidBrush(Color.FromArgb(17, 22, 31))) g.FillPath(br, path);
                    using (Pen p = new Pen(Color.FromArgb(34, 44, 62), 1f)) g.DrawPath(p, path);
                }

                // Left Accent bar
                Color accColor = (currentMode == 1) ? Color.FromArgb(0, 200, 95) : Color.FromArgb(56, 189, 248);
                Rectangle accRect = new Rectangle(S(5), S(8), S(3), pnlModeCard.Height - S(16));
                using (GraphicsPath accPath = RoundedCard.GetRoundedRectangle(accRect, S(1)))
                using (SolidBrush accBr = new SolidBrush(accColor))
                {
                    g.FillPath(accBr, accPath);
                }

                int textX = S(14);
                int textW = pnlModeCard.Width - textX - S(8);

                string title = (currentMode == 1) ? "💡 进程存活常驻模式 (官方推荐)" : "💡 仅前台窗口激活模式 (传统)";
                Color titleCol = (currentMode == 1) ? Color.FromArgb(0, 230, 118) : Color.FromArgb(56, 189, 248);

                string desc = (currentMode == 1)
                    ? "只要游戏在后台存活，切屏查攻略/看时间绝不反复切鼠标；彻底退出游戏才恢复常规。"
                    : "仅当游戏位于最顶层活动窗口时生效；切换到其他应用时经 2 秒防抖立即恢复桌面参数。";

                using (Font ft = DpiScaleHelper.CreateScaledFont("Microsoft YaHei UI", 7.8F, FontStyle.Bold, dpiScale))
                using (Font fd = DpiScaleHelper.CreateScaledFont("Microsoft YaHei UI", 7.2F, FontStyle.Regular, dpiScale))
                {
                    TextRenderer.DrawText(g, title, ft, new Rectangle(textX, S(6), textW, S(18)), titleCol,
                        TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.WordEllipsis);

                    TextRenderer.DrawText(g, desc, fd, new Rectangle(textX, S(25), textW, S(38)), Color.FromArgb(120, 138, 160),
                        TextFormatFlags.Left | TextFormatFlags.Top | TextFormatFlags.WordBreak);
                }
            };
            this.Controls.Add(pnlModeCard);

            // Fallback Checkbox (aligned right under mode micro-card)
            chkFallback = new ModernCheckBox();
            chkFallback.Text = "开启未在列表中的全屏游戏通用自动探测 (兜底模式)";
            chkFallback.Font = new Font("Microsoft YaHei UI", 8F, FontStyle.Regular);
            chkFallback.ForeColor = Color.FromArgb(160, 175, 195);
            chkFallback.Location = new Point(rightColX, S(460));
            chkFallback.Size = new Size(rightColW, S(22));
            chkFallback.Checked = fallbackEnabled;
            this.Controls.Add(chkFallback);

            SetGameOptMode(this.currentMode);

            // ================= 5. Bottom Control Bar =================
            int botY = S(524);

            Label lblHintBottom = new Label();
            lblHintBottom.Text = "💡 提示：双击左侧应用或点击 ➕ 可快速加入守护；配置修改后点击保存立即写入生效。";
            lblHintBottom.Font = new Font("Microsoft YaHei UI", 7.8F, FontStyle.Regular);
            lblHintBottom.ForeColor = Color.FromArgb(95, 110, 130);
            lblHintBottom.Location = new Point(pad, botY + S(6));
            lblHintBottom.Size = new Size(clientW - pad * 2 - S(140), S(20));
            lblHintBottom.BackColor = Color.Transparent;
            this.Controls.Add(lblHintBottom);

            btnClose = new ModernButton();
            btnClose.Text = "完成并保存";
            btnClose.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold);
            btnClose.NormalColor = Color.FromArgb(0, 200, 90);
            btnClose.HoverColor = Color.FromArgb(0, 230, 115);
            btnClose.PressedColor = Color.FromArgb(0, 160, 70);
            btnClose.BorderColor = Color.Transparent;
            btnClose.TextColor = Color.FromArgb(10, 24, 16);
            btnClose.CornerRadius = S(6);
            btnClose.Cursor = Cursors.Hand;
            btnClose.Location = new Point(clientW - pad - S(130), botY);
            btnClose.Size = new Size(S(130), S(34));
            btnClose.Click += (s, e) => SaveAndClose();
            this.Controls.Add(btnClose);

            this.KeyDown += (s, e) =>
            {
                if (e.KeyCode == Keys.Escape) { SaveAndClose(); }
            };

            // Apply DPI scaled fonts across all controls
            DpiScaleHelper.ApplyScaledFontsToControlTree(this, this.dpiScale);

            RefreshRunningProcesses();
            RefreshMonitoredList();

            // Setup Background Auto-Refresh Timer for Running Applications (2 seconds)
            autoRefreshTimer = new Timer();
            autoRefreshTimer.Interval = 2000;
            autoRefreshTimer.Tick += (s, e) => AutoRefreshRunningProcesses();
            autoRefreshTimer.Start();
        }

        protected override void OnApplyDpiScale(float scale)
        {
            base.OnApplyDpiScale(scale);
            if (ctlRunningList != null) ctlRunningList.DpiScale = scale;
            if (ctlMonitoredList != null) ctlMonitoredList.DpiScale = scale;
            if (ctlRunningList != null) ctlRunningList.Invalidate();
            if (ctlMonitoredList != null) ctlMonitoredList.Invalidate();
            if (pnlModeCard != null) pnlModeCard.Invalidate();
            if (pnlSearch != null) pnlSearch.Invalidate();
        }

        private void SetGameOptMode(int mode)
        {
            currentMode = mode;
            if (mode == 1)
            {
                btnModePersistent.Text = "● 进程存活常驻 (推荐)";
                btnModePersistent.NormalColor = Color.FromArgb(20, 48, 32);
                btnModePersistent.HoverColor = Color.FromArgb(28, 64, 42);
                btnModePersistent.PressedColor = Color.FromArgb(16, 38, 26);
                btnModePersistent.BorderColor = Color.FromArgb(0, 230, 118);
                btnModePersistent.TextColor = Color.FromArgb(0, 230, 118);

                btnModeForeground.Text = "○ 仅前台窗口激活";
                btnModeForeground.NormalColor = Color.FromArgb(24, 30, 42);
                btnModeForeground.HoverColor = Color.FromArgb(34, 44, 62);
                btnModeForeground.PressedColor = Color.FromArgb(18, 22, 32);
                btnModeForeground.BorderColor = Color.FromArgb(44, 56, 78);
                btnModeForeground.TextColor = Color.FromArgb(150, 165, 185);
            }
            else
            {
                btnModePersistent.Text = "○ 进程存活常驻 (推荐)";
                btnModePersistent.NormalColor = Color.FromArgb(24, 30, 42);
                btnModePersistent.HoverColor = Color.FromArgb(34, 44, 62);
                btnModePersistent.PressedColor = Color.FromArgb(18, 22, 32);
                btnModePersistent.BorderColor = Color.FromArgb(44, 56, 78);
                btnModePersistent.TextColor = Color.FromArgb(150, 165, 185);

                btnModeForeground.Text = "● 仅前台窗口激活";
                btnModeForeground.NormalColor = Color.FromArgb(20, 44, 62);
                btnModeForeground.HoverColor = Color.FromArgb(28, 58, 80);
                btnModeForeground.PressedColor = Color.FromArgb(16, 34, 48);
                btnModeForeground.BorderColor = Color.FromArgb(56, 189, 248);
                btnModeForeground.TextColor = Color.FromArgb(56, 189, 248);
            }
            btnModePersistent.Invalidate();
            btnModeForeground.Invalidate();
            if (pnlModeCard != null) pnlModeCard.Invalidate();
        }

        private void RefreshRunningProcesses()
        {
            rawRunningProcesses = GetRunningUserApplications();
            ApplySearchFilter();
        }

        private void ApplySearchFilter()
        {
            string query = (txtSearch != null && txtSearch.Text != null) ? txtSearch.Text.Trim() : "";
            List<ProcessListItem> displayItems = new List<ProcessListItem>();

            for (int i = 0; i < rawRunningProcesses.Count; i++)
            {
                RunningProcessItem p = rawRunningProcesses[i];
                if (string.IsNullOrEmpty(query) ||
                    (p.WindowTitle != null && p.WindowTitle.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0) ||
                    (p.ProcessName != null && p.ProcessName.IndexOf(query, StringComparison.OrdinalIgnoreCase) >= 0))
                {
                    string sub = string.Format("{0} · PID: {1}", p.ProcessName, p.Pid);
                    displayItems.Add(new ProcessListItem(p.ProcessName, p.WindowTitle, sub, p.IconImage, p));
                }
            }

            ctlRunningList.SetItems(displayItems);

            if (lblRunningTitle != null)
            {
                if (string.IsNullOrEmpty(query))
                    lblRunningTitle.Text = string.Format("⚡ 当前正在运行的应用与游戏 ({0} 款)", rawRunningProcesses.Count);
                else
                    lblRunningTitle.Text = string.Format("⚡ 运行列表 (匹配 {0}/{1} 款)", displayItems.Count, rawRunningProcesses.Count);
            }
        }

        private string GetDpiDisplayString(int stage)
        {
            if (stage < 1 || stage > 5) return "默认DPI";
            int dpiVal = 0;
            if (availableDpis != null && stage - 1 < availableDpis.Length && availableDpis[stage - 1] > 0)
            {
                dpiVal = availableDpis[stage - 1];
            }
            else
            {
                int[] fallback = new int[] { 800, 1600, 2400, 3200, 4000 };
                dpiVal = fallback[stage - 1];
            }
            return dpiVal.ToString() + "DPI";
        }

        private void RefreshMonitoredList()
        {
            List<ProcessListItem> items = new List<ProcessListItem>();
            for (int i = 0; i < monitoredList.Count; i++)
            {
                string exeName = monitoredList[i];
                Image ico = GetCachedProcessIcon(exeName);
                GameProfile prof = null;
                if (gameProfiles != null) gameProfiles.TryGetValue(exeName, out prof);

                string title = exeName;
                if (prof != null && !string.IsNullOrEmpty(prof.FriendlyTitle))
                {
                    title = prof.FriendlyTitle;
                }
                else
                {
                    string ct = GetCachedProcessTitle(exeName);
                    if (!string.IsNullOrEmpty(ct)) title = ct;
                }

                string sub;
                if (prof != null && prof.IsCustomConfigured)
                {
                    string dpiPart = GetDpiDisplayString(prof.CustomDpiStage);
                    string ratePart = (prof.CustomPollingRate > 0) ? (prof.CustomPollingRate + "Hz") : "默认回报率";
                    sub = "专属策略: " + dpiPart + " · " + ratePart;
                }
                else
                {
                    sub = "跟随默认预设";
                }

                // Check if matching window title exists from running processes
                for (int j = 0; j < rawRunningProcesses.Count; j++)
                {
                    if (string.Equals(rawRunningProcesses[j].ProcessName, exeName, StringComparison.OrdinalIgnoreCase))
                    {
                        string liveTitle = rawRunningProcesses[j].WindowTitle;
                        if (!string.IsNullOrEmpty(liveTitle))
                        {
                            title = liveTitle;
                            processTitleCache[exeName] = liveTitle;
                            SaveProcessTitleToDisk(exeName, liveTitle);

                            if (prof == null)
                            {
                                prof = new GameProfile(exeName);
                                if (gameProfiles == null) gameProfiles = new Dictionary<string, GameProfile>(StringComparer.OrdinalIgnoreCase);
                                gameProfiles[exeName] = prof;
                            }
                            prof.FriendlyTitle = liveTitle;
                        }

                        if (prof != null && prof.IsCustomConfigured)
                        {
                            string dpiPart = GetDpiDisplayString(prof.CustomDpiStage);
                            string ratePart = (prof.CustomPollingRate > 0) ? (prof.CustomPollingRate + "Hz") : "默认回报率";
                            sub = "专属策略: " + dpiPart + " · " + ratePart + " · 实时运行中";
                        }
                        else
                        {
                            sub = "跟随默认预设 · 实时运行中";
                        }
                        if (rawRunningProcesses[j].IconImage != null)
                        {
                            if (ico == null) ico = rawRunningProcesses[j].IconImage;
                            SaveProcessIconToDisk(exeName, rawRunningProcesses[j].IconImage);
                        }
                        break;
                    }
                }

                items.Add(new ProcessListItem(exeName, title, sub, ico));
            }

            ctlMonitoredList.SetItems(items);

            if (lblMonitoredTitle != null)
            {
                lblMonitoredTitle.Text = string.Format("📋 已生效的游戏列表 ({0} 款)", monitoredList.Count);
            }
        }

        private void AddGameProcess(string exeName)
        {
            if (string.IsNullOrEmpty(exeName)) return;
            exeName = exeName.Trim();
            string lower = exeName.ToLowerInvariant();
            if (!lower.EndsWith(".exe")) lower += ".exe";

            for (int i = 0; i < monitoredList.Count; i++)
            {
                string cur = monitoredList[i].ToLowerInvariant();
                if (!cur.EndsWith(".exe")) cur += ".exe";
                if (cur == lower) return;
            }
            string capturedTitle = null;
            for (int rIdx = 0; rIdx < rawRunningProcesses.Count; rIdx++)
            {
                if (string.Equals(rawRunningProcesses[rIdx].ProcessName, lower, StringComparison.OrdinalIgnoreCase) ||
                    string.Equals(rawRunningProcesses[rIdx].ProcessName, exeName, StringComparison.OrdinalIgnoreCase))
                {
                    capturedTitle = rawRunningProcesses[rIdx].WindowTitle;
                    if (rawRunningProcesses[rIdx].IconImage != null)
                    {
                        SaveProcessIconToDisk(lower, rawRunningProcesses[rIdx].IconImage);
                    }
                    break;
                }
            }

            if (!string.IsNullOrEmpty(capturedTitle))
            {
                processTitleCache[lower] = capturedTitle;
                SaveProcessTitleToDisk(lower, capturedTitle);

                GameProfile prof;
                if (gameProfiles == null) gameProfiles = new Dictionary<string, GameProfile>(StringComparer.OrdinalIgnoreCase);
                if (!gameProfiles.TryGetValue(lower, out prof) || prof == null)
                {
                    prof = new GameProfile(lower);
                    gameProfiles[lower] = prof;
                }
                prof.FriendlyTitle = capturedTitle;
            }

            monitoredList.Add(lower);
            RefreshMonitoredList();
        }

        public void RemoveGameProcess(string exeName)
        {
            RemoveGameProcess(exeName, true);
        }

        public void RemoveGameProcess(string exeName, bool promptConfirm)
        {
            if (string.IsNullOrEmpty(exeName)) return;
            string lower = exeName.ToLowerInvariant();
            if (!lower.EndsWith(".exe")) lower += ".exe";

            if (promptConfirm)
            {
                DialogResult dr = MessageBox.Show(this,
                    string.Format("确定要将 \"{0}\" 从生效的游戏列表中移除吗？\n移除后该游戏将不再自动触发电竞性能优化，其专属配置也将同步清除。", exeName),
                    "移除确认 · FerrisPulse",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Question);

                if (dr != DialogResult.Yes) return;
            }

            for (int i = 0; i < monitoredList.Count; i++)
            {
                string cur = monitoredList[i].ToLowerInvariant();
                if (!cur.EndsWith(".exe")) cur += ".exe";
                if (cur == lower)
                {
                    monitoredList.RemoveAt(i);
                    break;
                }
            }

            // 同步清理该游戏的专属硬件配置策略 (GameProfile)，杜绝配置残留泄漏
            if (gameProfiles != null && gameProfiles.ContainsKey(lower))
            {
                gameProfiles.Remove(lower);
            }
            RefreshMonitoredList();
        }

        private void BrowseLocalExe()
        {
            using (OpenFileDialog ofd = new OpenFileDialog())
            {
                ofd.Title = "选择游戏可执行文件";
                ofd.Filter = "游戏程序 (*.exe)|*.exe|所有文件 (*.*)|*.*";
                ofd.Multiselect = true;
                if (ofd.ShowDialog(this) == DialogResult.OK)
                {
                    for (int i = 0; i < ofd.FileNames.Length; i++)
                    {
                        string fullPath = ofd.FileNames[i];
                        string fn = Path.GetFileName(fullPath);
                        if (File.Exists(fullPath))
                        {
                            try
                            {
                                using (Icon ico = Icon.ExtractAssociatedIcon(fullPath))
                                {
                                    if (ico != null)
                                    {
                                        Image bmp = ico.ToBitmap();
                                        processIconCache[fn] = bmp;
                                        SaveProcessIconToDisk(fn, bmp);
                                    }
                                }
                            }
                            catch { }

                            try
                            {
                                var vi = FileVersionInfo.GetVersionInfo(fullPath);
                                if (!string.IsNullOrEmpty(vi.FileDescription))
                                {
                                    string desc = vi.FileDescription.Trim();
                                    processTitleCache[fn] = desc;
                                    SaveProcessTitleToDisk(fn, desc);
                                }
                            }
                            catch { }
                        }
                        AddGameProcess(fn);
                    }
                }
            }
        }

        private void OpenGameProfileSetting(string exeName)
        {
            if (string.IsNullOrEmpty(exeName)) return;
            string lower = exeName.ToLowerInvariant();
            if (!lower.EndsWith(".exe")) lower += ".exe";

            GameProfile prof = null;
            if (gameProfiles != null) gameProfiles.TryGetValue(lower, out prof);
            if (prof == null) prof = new GameProfile(lower);

            Image icon = GetCachedProcessIcon(lower);
            string title = !string.IsNullOrEmpty(prof.FriendlyTitle) ? prof.FriendlyTitle : lower;
            if (title == lower)
            {
                string ct = GetCachedProcessTitle(lower);
                if (!string.IsNullOrEmpty(ct)) title = ct;
            }

            for (int i = 0; i < rawRunningProcesses.Count; i++)
            {
                if (string.Equals(rawRunningProcesses[i].ProcessName, lower, StringComparison.OrdinalIgnoreCase))
                {
                    title = rawRunningProcesses[i].WindowTitle;
                    if (icon == null) icon = rawRunningProcesses[i].IconImage;
                    break;
                }
            }

            int[] dpis = (availableDpis != null && availableDpis.Length > 0) ? availableDpis : new int[] { 800, 1600, 2400, 3200, 4000 };
            int[] rates = (availableRates != null && availableRates.Length > 0) ? availableRates : new int[] { 125, 500, 1000, 2000, 4000, 8000 };

            using (var dlg = new GameProfileSettingForm(dpiScale, lower, title, icon, prof, dpis, rates, (updatedProf) =>
            {
                if (gameProfiles == null) gameProfiles = new Dictionary<string, GameProfile>(StringComparer.OrdinalIgnoreCase);
                gameProfiles[lower] = updatedProf;
                RefreshMonitoredList();
            }))
            {
                dlg.ShowDialog(this);
            }
        }

        private void AutoRefreshRunningProcesses()
        {
            if (this.IsDisposed || !this.Visible || isRefreshing) return;

            try
            {
                isRefreshing = true;
                List<RunningProcessItem> latest = GetRunningUserApplications();
                if (HasProcessListChanged(rawRunningProcesses, latest))
                {
                    rawRunningProcesses = latest;
                    ApplySearchFilter();
                    RefreshMonitoredList();
                }
            }
            catch { }
            finally
            {
                isRefreshing = false;
            }
        }

        private static bool HasProcessListChanged(List<RunningProcessItem> oldList, List<RunningProcessItem> newList)
        {
            if (oldList == null || newList == null) return true;
            if (oldList.Count != newList.Count) return true;
            for (int i = 0; i < oldList.Count; i++)
            {
                if (oldList[i].Pid != newList[i].Pid ||
                    !string.Equals(oldList[i].ProcessName, newList[i].ProcessName, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(oldList[i].WindowTitle, newList[i].WindowTitle, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            return false;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            base.OnFormClosing(e);
            if (autoRefreshTimer != null)
            {
                autoRefreshTimer.Stop();
                autoRefreshTimer.Dispose();
                autoRefreshTimer = null;
            }
        }

        private void SaveAndClose()
        {
            if (onSaveProfileCallback != null)
            {
                onSaveProfileCallback(monitoredList, chkFallback != null ? chkFallback.Checked : fallbackEnabled, currentMode, gameProfiles);
            }
            this.DialogResult = DialogResult.OK;
            this.Close();
        }

        // ====================================================================
        // High-Precision Process & Icon Enumeration Engine
        // ====================================================================
        public static List<RunningProcessItem> GetRunningUserApplications()
        {
            var list = new List<RunningProcessItem>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            Process[] all = Process.GetProcesses();
            for (int i = 0; i < all.Length; i++)
            {
                Process p = all[i];
                try
                {
                    if (p.MainWindowHandle == IntPtr.Zero) continue;
                    string title = p.MainWindowTitle;
                    if (string.IsNullOrWhiteSpace(title)) continue;

                    string name = p.ProcessName;
                    if (seen.Contains(name)) continue;

                    string nameLower = name.ToLowerInvariant();
                    if (nameLower == "explorer" || nameLower == "taskmgr" || nameLower == "devenv" ||
                        nameLower == "ferrispulse" || nameLower == "razerbatterytray" ||
                        nameLower == "textinputhost" || nameLower == "applicationframehost" ||
                        nameLower == "searchhost" || nameLower == "shellexperiencehost" ||
                        nameLower == "startmenuexperiencehost" || nameLower == "lockapp" ||
                        nameLower == "systemsettings" || nameLower == "cmd" || nameLower == "powershell")
                    {
                        continue;
                    }

                    string exeName = nameLower.EndsWith(".exe") ? nameLower : (nameLower + ".exe");
                    string fullPath = ResolveProcessImagePath(p.Id);
                    Image icon = GetOrExtractIcon(exeName, fullPath);

                    list.Add(new RunningProcessItem
                    {
                        ProcessName = exeName,
                        WindowTitle = title,
                        Pid = p.Id,
                        ExePath = fullPath,
                        IconImage = icon
                    });
                    seen.Add(name);
                }
                catch { }
                finally
                {
                    p.Dispose();
                }
            }

            list.Sort((a, b) => string.Compare(a.WindowTitle, b.WindowTitle, StringComparison.CurrentCultureIgnoreCase));
            return list;
        }

        private static string ResolveProcessImagePath(int pid)
        {
            try
            {
                IntPtr hProcess = NativeMethods.OpenProcess(NativeMethods.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
                if (hProcess != IntPtr.Zero)
                {
                    try
                    {
                        StringBuilder sb = new StringBuilder(1024);
                        int capacity = sb.Capacity;
                        if (NativeMethods.QueryFullProcessImageName(hProcess, 0, sb, ref capacity))
                        {
                            return sb.ToString();
                        }
                    }
                    finally
                    {
                        NativeMethods.CloseHandle(hProcess);
                    }
                }
            }
            catch { }

            // Secondary fallback via Process.MainModule
            try
            {
                using (Process proc = Process.GetProcessById(pid))
                {
                    return proc.MainModule.FileName;
                }
            }
            catch { }

            return null;
        }

        private static Image GetOrExtractIcon(string exeName, string fullPath)
        {
            if (!string.IsNullOrEmpty(exeName))
            {
                Image cached;
                if (processIconCache.TryGetValue(exeName, out cached)) return cached;
            }

            if (!string.IsNullOrEmpty(fullPath) && File.Exists(fullPath))
            {
                try
                {
                    using (Icon ico = Icon.ExtractAssociatedIcon(fullPath))
                    {
                        if (ico != null)
                        {
                            Image bmp = ico.ToBitmap();
                            if (!string.IsNullOrEmpty(exeName))
                            {
                                processIconCache[exeName] = bmp;
                                SaveProcessIconToDisk(exeName, bmp);
                            }
                            return bmp;
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        public static string GetIconCacheDirectory()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FerrisPulse", "GameIcons");
            if (!Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); } catch { }
            }
            return dir;
        }

        public static void SaveProcessIconToDisk(string exeName, Image icon)
        {
            if (string.IsNullOrEmpty(exeName) || icon == null) return;
            try
            {
                string cacheDir = GetIconCacheDirectory();
                string safeName = Path.GetFileName(exeName).ToLowerInvariant();
                if (safeName.EndsWith(".exe")) safeName = safeName.Substring(0, safeName.Length - 4);
                string filePath = Path.Combine(cacheDir, safeName + ".png");

                using (MemoryStream ms = new MemoryStream())
                {
                    icon.Save(ms, System.Drawing.Imaging.ImageFormat.Png);
                    File.WriteAllBytes(filePath, ms.ToArray());
                }
            }
            catch { }
        }

        public static Image LoadProcessIconFromDisk(string exeName)
        {
            if (string.IsNullOrEmpty(exeName)) return null;
            try
            {
                string cacheDir = GetIconCacheDirectory();
                string safeName = Path.GetFileName(exeName).ToLowerInvariant();
                if (safeName.EndsWith(".exe")) safeName = safeName.Substring(0, safeName.Length - 4);
                string filePath = Path.Combine(cacheDir, safeName + ".png");
                if (File.Exists(filePath))
                {
                    byte[] bytes = File.ReadAllBytes(filePath);
                    using (MemoryStream ms = new MemoryStream(bytes))
                    {
                        using (Image img = Image.FromStream(ms))
                        {
                            return new Bitmap(img);
                        }
                    }
                }
            }
            catch { }
            return null;
        }

        public static Image GetCachedProcessIcon(string exeName)
        {
            if (string.IsNullOrEmpty(exeName)) return null;
            Image img;
            if (processIconCache.TryGetValue(exeName, out img)) return img;

            Image diskImg = LoadProcessIconFromDisk(exeName);
            if (diskImg != null)
            {
                processIconCache[exeName] = diskImg;
                return diskImg;
            }
            return null;
        }

        public static string GetTitleCacheDirectory()
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FerrisPulse", "GameTitles");
            if (!Directory.Exists(dir))
            {
                try { Directory.CreateDirectory(dir); } catch { }
            }
            return dir;
        }

        public static void SaveProcessTitleToDisk(string exeName, string title)
        {
            if (string.IsNullOrEmpty(exeName) || string.IsNullOrEmpty(title)) return;
            try
            {
                string cacheDir = GetTitleCacheDirectory();
                string safeName = Path.GetFileName(exeName).ToLowerInvariant();
                if (safeName.EndsWith(".exe")) safeName = safeName.Substring(0, safeName.Length - 4);
                string filePath = Path.Combine(cacheDir, safeName + ".txt");
                File.WriteAllText(filePath, title, Encoding.UTF8);
            }
            catch { }
        }

        public static string LoadProcessTitleFromDisk(string exeName)
        {
            if (string.IsNullOrEmpty(exeName)) return null;
            try
            {
                string cacheDir = GetTitleCacheDirectory();
                string safeName = Path.GetFileName(exeName).ToLowerInvariant();
                if (safeName.EndsWith(".exe")) safeName = safeName.Substring(0, safeName.Length - 4);
                string filePath = Path.Combine(cacheDir, safeName + ".txt");
                if (File.Exists(filePath))
                {
                    string t = File.ReadAllText(filePath, Encoding.UTF8);
                    if (!string.IsNullOrEmpty(t)) return t.Trim();
                }
            }
            catch { }
            return null;
        }

        public static string GetCachedProcessTitle(string exeName)
        {
            if (string.IsNullOrEmpty(exeName)) return null;
            string t;
            if (processTitleCache.TryGetValue(exeName, out t) && !string.IsNullOrEmpty(t)) return t;

            string diskT = LoadProcessTitleFromDisk(exeName);
            if (!string.IsNullOrEmpty(diskT))
            {
                processTitleCache[exeName] = diskT;
                return diskT;
            }
            return null;
        }
    }
}
