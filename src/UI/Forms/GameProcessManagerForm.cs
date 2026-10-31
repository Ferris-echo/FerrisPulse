// ============================================================================
// 模块名称: GameProcessManagerForm.cs
// 所属区域: UI/Forms (业务对话框层)
// 核心职责:
//   现代化左右双栏电竞游戏进程控制台：
//   1. 宽屏左右双栏布局 (左栏运行应用库 + 实时搜索过滤；右栏守护名单 + 策略控制)
//   2. Windows 原生 API 进程高清图标提取与内存高速缓存 (22x22 高清展示)
//   3. 与设备优先级管理页完全统一的矢量级微型添加 [+] 与删除 [✕] 按键设计
//   4. 进程存活常驻 vs 仅前台窗口激活双策略切换、全屏兜底探测与数据解耦回调
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
        private Action<List<string>, bool, int> onSaveCallback;

        // In-memory icon cache for process executables (prevents disk I/O lag and memory leaks)
        private static readonly Dictionary<string, Image> processIconCache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

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

        public GameProcessManagerForm(float scale, List<string> initialList, bool initialFallback, Action<List<string>, bool> callback)
            : this(scale, initialList, initialFallback, 1, (list, fb, mode) => { if (callback != null) callback(list, fb); })
        {
        }

        public GameProcessManagerForm(float scale, List<string> initialList, bool initialFallback, int initialMode, Action<List<string>, bool, int> callback)
            : base(scale)
        {
            this.monitoredList = initialList != null ? new List<string>(initialList) : new List<string>();
            this.fallbackEnabled = initialFallback;
            this.currentMode = (initialMode == 0) ? 0 : 1;
            this.onSaveCallback = callback;

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
            lblMonitoredTitle.Text = "📋 已生效的电竞守护列表";
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
                    monitoredList.Clear();
                    RefreshMonitoredList();
                }
            };
            this.Controls.Add(btnClearAll);

            // Right Monitored Process List Control (expanded height to S(246) for comfortable view)
            ctlMonitoredList = new ModernProcessListControl();
            ctlMonitoredList.DpiScale = dpiScale;
            ctlMonitoredList.ActionType = ProcessListActionType.Delete;
            ctlMonitoredList.EmptyPlaceholderText = "暂无守护游戏 (双击左侧应用或点击 ➕ 添加)";
            ctlMonitoredList.Location = new Point(rightColX, S(76));
            ctlMonitoredList.Size = new Size(rightColW, S(246));
            ctlMonitoredList.OnActionClicked += (item) => RemoveGameProcess(item.ProcessName);
            ctlMonitoredList.OnItemDoubleClicked += (item) => RemoveGameProcess(item.ProcessName);
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

        private void RefreshMonitoredList()
        {
            List<ProcessListItem> items = new List<ProcessListItem>();
            for (int i = 0; i < monitoredList.Count; i++)
            {
                string exeName = monitoredList[i];
                Image ico = GetCachedProcessIcon(exeName);
                string title = exeName;
                string sub = "已纳入电竞性能守护名单";

                // Check if matching window title exists from running processes
                for (int j = 0; j < rawRunningProcesses.Count; j++)
                {
                    if (string.Equals(rawRunningProcesses[j].ProcessName, exeName, StringComparison.OrdinalIgnoreCase))
                    {
                        title = rawRunningProcesses[j].WindowTitle;
                        sub = exeName + " · 实时运行中";
                        if (ico == null && rawRunningProcesses[j].IconImage != null)
                        {
                            ico = rawRunningProcesses[j].IconImage;
                        }
                        break;
                    }
                }

                items.Add(new ProcessListItem(exeName, title, sub, ico));
            }

            ctlMonitoredList.SetItems(items);

            if (lblMonitoredTitle != null)
            {
                lblMonitoredTitle.Text = string.Format("📋 已生效的电竞守护列表 ({0} 款)", monitoredList.Count);
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
            monitoredList.Add(lower);
            RefreshMonitoredList();
        }

        private void RemoveGameProcess(string exeName)
        {
            if (string.IsNullOrEmpty(exeName)) return;
            string lower = exeName.ToLowerInvariant();
            if (!lower.EndsWith(".exe")) lower += ".exe";

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
                                    if (ico != null && !processIconCache.ContainsKey(fn))
                                    {
                                        processIconCache[fn] = ico.ToBitmap();
                                    }
                                }
                            }
                            catch { }
                        }
                        AddGameProcess(fn);
                    }
                }
            }
        }

        private void SaveAndClose()
        {
            if (onSaveCallback != null)
            {
                onSaveCallback(monitoredList, chkFallback != null ? chkFallback.Checked : fallbackEnabled, currentMode);
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
                            if (!string.IsNullOrEmpty(exeName)) processIconCache[exeName] = bmp;
                            return bmp;
                        }
                    }
                }
                catch { }
            }

            return null;
        }

        private static Image GetCachedProcessIcon(string exeName)
        {
            if (string.IsNullOrEmpty(exeName)) return null;
            Image img;
            if (processIconCache.TryGetValue(exeName, out img)) return img;
            return null;
        }
    }
}
