// ============================================================================
// 模块名称: GameProcessManagerForm.cs
// 所属区域: UI/Forms (业务对话框层)
// 核心职责:
//   游戏进程名单管理窗口，支持实时枚举前台可见窗口与后台进程、添加/删除白名单、全屏自动检测开关与进程防抖持久化保存。
// 依赖关系: Controls.*, Core.ConfigManager, Models.RunningProcessItem, Services.GameDetector
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
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class GameProcessManagerForm : DpiAwareForm
    {
        private List<string> monitoredList;
        private bool fallbackEnabled;
        private Action<List<string>, bool> onSaveCallback;

        private ListBox lstRunning;
        private ListBox lstMonitored;
        private ModernButton btnAddRunning;
        private ModernButton btnRefreshRunning;
        private ModernButton btnBrowseExe;
        private ModernButton btnRemove;
        private ModernButton btnClearAll;
        private ModernCheckBox chkFallback;
        private ModernButton btnClose;

        public GameProcessManagerForm(float scale, List<string> initialList, bool initialFallback, Action<List<string>, bool> callback) : base(scale)
        {
            this.monitoredList = initialList != null ? new List<string>(initialList) : new List<string>();
            this.fallbackEnabled = initialFallback;
            this.onSaveCallback = callback;

            this.Text = "FerrisPulse · 电竞游戏进程管理与自动捕获";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterParent;
            this.BackColor = Color.FromArgb(20, 23, 30);
            this.ForeColor = Color.FromArgb(240, 245, 255);
            this.ClientSize = new Size((int)(540 * dpiScale), (int)(520 * dpiScale));
            this.KeyPreview = true;

            int pad = (int)(16 * dpiScale);
            int clientW = this.ClientSize.Width;

            // Title
            Label lblTitle = new Label();
            lblTitle.Text = "🎮 电竞游戏进程管理与自动捕获";
            lblTitle.Font = new Font("Microsoft YaHei UI", 10.5F, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(0, 230, 118);
            lblTitle.Location = new Point(pad, (int)(12 * dpiScale));
            lblTitle.Size = new Size(clientW - (pad * 2), (int)(22 * dpiScale));
            this.Controls.Add(lblTitle);

            // Subtitle
            Label lblSub = new Label();
            lblSub.Text = "只需将游戏添加至守护列表，进入游戏时自动生效电竞回报率，切回桌面自动恢复节能。";
            lblSub.Font = new Font("Microsoft YaHei UI", 8.2F);
            lblSub.ForeColor = Color.FromArgb(150, 160, 180);
            lblSub.Location = new Point(pad, (int)(34 * dpiScale));
            lblSub.Size = new Size(clientW - (pad * 2), (int)(18 * dpiScale));
            this.Controls.Add(lblSub);

            // Section 1 Header
            Label lblSec1 = new Label();
            lblSec1.Text = "⚡ 当前正在运行的应用与游戏 (双击或点击下方按钮添加):";
            lblSec1.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold);
            lblSec1.ForeColor = Color.FromArgb(220, 230, 245);
            lblSec1.Location = new Point(pad, (int)(58 * dpiScale));
            lblSec1.Size = new Size(clientW - (pad * 2), (int)(20 * dpiScale));
            this.Controls.Add(lblSec1);

            // ListBox Running
            int listW = clientW - (pad * 2);
            lstRunning = new ListBox();
            lstRunning.BackColor = Color.FromArgb(28, 32, 42);
            lstRunning.ForeColor = Color.FromArgb(225, 235, 250);
            lstRunning.BorderStyle = BorderStyle.FixedSingle;
            lstRunning.Font = new Font("Microsoft YaHei UI", 8.8F);
            lstRunning.ItemHeight = (int)(20 * dpiScale);
            lstRunning.Location = new Point(pad, (int)(80 * dpiScale));
            lstRunning.Size = new Size(listW, (int)(125 * dpiScale));
            lstRunning.DoubleClick += (s, e) => AddSelectedRunning();
            this.Controls.Add(lstRunning);

            // Action buttons below Running
            int actY1 = (int)(212 * dpiScale);
            int btnH = (int)(26 * dpiScale);

            btnAddRunning = new ModernButton();
            btnAddRunning.Text = "➕ 添加选中的应用";
            btnAddRunning.Font = new Font("Microsoft YaHei UI", 8.2F);
            btnAddRunning.NormalColor = Color.FromArgb(26, 42, 32);
            btnAddRunning.HoverColor = Color.FromArgb(36, 60, 44);
            btnAddRunning.BorderColor = Color.FromArgb(0, 180, 80);
            btnAddRunning.TextColor = Color.FromArgb(0, 230, 118);
            btnAddRunning.CornerRadius = (int)(5 * dpiScale);
            btnAddRunning.Cursor = Cursors.Hand;
            btnAddRunning.Location = new Point(pad, actY1);
            btnAddRunning.Size = new Size((int)(140 * dpiScale), btnH);
            btnAddRunning.Click += (s, e) => AddSelectedRunning();
            this.Controls.Add(btnAddRunning);

            btnRefreshRunning = new ModernButton();
            btnRefreshRunning.Text = "🔄 刷新列表";
            btnRefreshRunning.Font = new Font("Microsoft YaHei UI", 8.2F);
            btnRefreshRunning.NormalColor = Color.FromArgb(32, 36, 48);
            btnRefreshRunning.HoverColor = Color.FromArgb(46, 52, 68);
            btnRefreshRunning.BorderColor = Color.FromArgb(56, 64, 84);
            btnRefreshRunning.TextColor = Color.FromArgb(190, 200, 220);
            btnRefreshRunning.CornerRadius = (int)(5 * dpiScale);
            btnRefreshRunning.Cursor = Cursors.Hand;
            btnRefreshRunning.Location = new Point(pad + (int)(148 * dpiScale), actY1);
            btnRefreshRunning.Size = new Size((int)(100 * dpiScale), btnH);
            btnRefreshRunning.Click += (s, e) => RefreshRunningProcesses();
            this.Controls.Add(btnRefreshRunning);

            btnBrowseExe = new ModernButton();
            btnBrowseExe.Text = "📁 浏览本地 .exe 文件...";
            btnBrowseExe.Font = new Font("Microsoft YaHei UI", 8.2F);
            btnBrowseExe.NormalColor = Color.FromArgb(32, 36, 48);
            btnBrowseExe.HoverColor = Color.FromArgb(46, 52, 68);
            btnBrowseExe.BorderColor = Color.FromArgb(56, 64, 84);
            btnBrowseExe.TextColor = Color.FromArgb(190, 200, 220);
            btnBrowseExe.CornerRadius = (int)(5 * dpiScale);
            btnBrowseExe.Cursor = Cursors.Hand;
            btnBrowseExe.Location = new Point(pad + (int)(256 * dpiScale), actY1);
            btnBrowseExe.Size = new Size((int)(180 * dpiScale), btnH);
            btnBrowseExe.Click += (s, e) => BrowseLocalExe();
            this.Controls.Add(btnBrowseExe);

            // Section 2 Header
            Label lblSec2 = new Label();
            lblSec2.Text = "📋 已生效的电竞游戏守护列表 (命中时自动切入电竞回报率):";
            lblSec2.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold);
            lblSec2.ForeColor = Color.FromArgb(220, 230, 245);
            lblSec2.Location = new Point(pad, (int)(248 * dpiScale));
            lblSec2.Size = new Size(clientW - (pad * 2), (int)(20 * dpiScale));
            this.Controls.Add(lblSec2);

            // ListBox Monitored
            lstMonitored = new ListBox();
            lstMonitored.BackColor = Color.FromArgb(24, 28, 36);
            lstMonitored.ForeColor = Color.FromArgb(0, 230, 118);
            lstMonitored.BorderStyle = BorderStyle.FixedSingle;
            lstMonitored.Font = new Font("Microsoft YaHei UI", 8.8F);
            lstMonitored.ItemHeight = (int)(20 * dpiScale);
            lstMonitored.Location = new Point(pad, (int)(270 * dpiScale));
            lstMonitored.Size = new Size(listW, (int)(115 * dpiScale));
            lstMonitored.DoubleClick += (s, e) => RemoveSelectedMonitored();
            this.Controls.Add(lstMonitored);

            // Action buttons below Monitored
            int actY2 = (int)(392 * dpiScale);
            btnRemove = new ModernButton();
            btnRemove.Text = "✕ 移除选中的游戏";
            btnRemove.Font = new Font("Microsoft YaHei UI", 8.2F);
            btnRemove.NormalColor = Color.FromArgb(42, 28, 32);
            btnRemove.HoverColor = Color.FromArgb(60, 36, 42);
            btnRemove.BorderColor = Color.FromArgb(160, 60, 70);
            btnRemove.TextColor = Color.FromArgb(255, 120, 130);
            btnRemove.CornerRadius = (int)(5 * dpiScale);
            btnRemove.Cursor = Cursors.Hand;
            btnRemove.Location = new Point(pad, actY2);
            btnRemove.Size = new Size((int)(140 * dpiScale), btnH);
            btnRemove.Click += (s, e) => RemoveSelectedMonitored();
            this.Controls.Add(btnRemove);

            btnClearAll = new ModernButton();
            btnClearAll.Text = "清空全部";
            btnClearAll.Font = new Font("Microsoft YaHei UI", 8.2F);
            btnClearAll.NormalColor = Color.FromArgb(32, 36, 48);
            btnClearAll.HoverColor = Color.FromArgb(46, 52, 68);
            btnClearAll.BorderColor = Color.FromArgb(56, 64, 84);
            btnClearAll.TextColor = Color.FromArgb(160, 170, 190);
            btnClearAll.CornerRadius = (int)(5 * dpiScale);
            btnClearAll.Cursor = Cursors.Hand;
            btnClearAll.Location = new Point(pad + (int)(148 * dpiScale), actY2);
            btnClearAll.Size = new Size((int)(80 * dpiScale), btnH);
            btnClearAll.Click += (s, e) => {
                monitoredList.Clear();
                RefreshMonitoredList();
            };
            this.Controls.Add(btnClearAll);

            // Fallback checkbox & Close button
            int botY = (int)(430 * dpiScale);
            chkFallback = new ModernCheckBox();
            chkFallback.Text = "开启未在列表中的全屏游戏通用自动探测 (兜底模式)";
            chkFallback.Font = new Font("Microsoft YaHei UI", 8.2F);
            chkFallback.ForeColor = Color.FromArgb(170, 180, 200);
            chkFallback.Location = new Point(pad, botY);
            chkFallback.Size = new Size((int)(360 * dpiScale), (int)(22 * dpiScale));
            chkFallback.Checked = fallbackEnabled;
            this.Controls.Add(chkFallback);

            btnClose = new ModernButton();
            btnClose.Text = "完成并保存";
            btnClose.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold);
            btnClose.NormalColor = Color.FromArgb(0, 200, 83);
            btnClose.HoverColor = Color.FromArgb(0, 230, 118);
            btnClose.PressedColor = Color.FromArgb(0, 170, 70);
            btnClose.BorderColor = Color.FromArgb(0, 230, 118);
            btnClose.TextColor = Color.FromArgb(10, 24, 15);
            btnClose.CornerRadius = (int)(6 * dpiScale);
            btnClose.Cursor = Cursors.Hand;
            btnClose.Location = new Point(clientW - pad - (int)(110 * dpiScale), (int)(465 * dpiScale));
            btnClose.Size = new Size((int)(110 * dpiScale), (int)(34 * dpiScale));
            btnClose.Click += (s, e) => {
                if (onSaveCallback != null) onSaveCallback(monitoredList, chkFallback.Checked);
                this.DialogResult = DialogResult.OK;
                this.Close();
            };
            this.Controls.Add(btnClose);

            this.KeyDown += (s, e) => {
                if (e.KeyCode == Keys.Escape)
                {
                    if (onSaveCallback != null) onSaveCallback(monitoredList, chkFallback.Checked);
                    this.Close();
                }
            };

            RefreshRunningProcesses();
            RefreshMonitoredList();
        }

        private void RefreshRunningProcesses()
        {
            lstRunning.Items.Clear();
            var procs = GetRunningUserApplications();
            for (int i = 0; i < procs.Count; i++)
            {
                lstRunning.Items.Add(procs[i]);
            }
            if (lstRunning.Items.Count > 0) lstRunning.SelectedIndex = 0;
        }

        private void RefreshMonitoredList()
        {
            lstMonitored.Items.Clear();
            for (int i = 0; i < monitoredList.Count; i++)
            {
                lstMonitored.Items.Add(monitoredList[i]);
            }
            if (lstMonitored.Items.Count > 0) lstMonitored.SelectedIndex = 0;
        }

        private void AddSelectedRunning()
        {
            var item = lstRunning.SelectedItem as RunningProcessItem;
            if (item != null)
            {
                AddGameProcess(item.ProcessName);
            }
        }

        private void AddGameProcess(string exeName)
        {
            if (string.IsNullOrEmpty(exeName)) return;
            exeName = exeName.Trim();
            string lower = exeName.ToLower();
            if (!lower.EndsWith(".exe")) lower += ".exe";

            for (int i = 0; i < monitoredList.Count; i++)
            {
                string cur = monitoredList[i].ToLower();
                if (!cur.EndsWith(".exe")) cur += ".exe";
                if (cur == lower) return;
            }
            monitoredList.Add(lower);
            RefreshMonitoredList();
        }

        private void RemoveSelectedMonitored()
        {
            if (lstMonitored.SelectedIndex >= 0 && lstMonitored.SelectedIndex < monitoredList.Count)
            {
                monitoredList.RemoveAt(lstMonitored.SelectedIndex);
                RefreshMonitoredList();
            }
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
                        string fn = Path.GetFileName(ofd.FileNames[i]);
                        AddGameProcess(fn);
                    }
                }
            }
        }

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

                    string nameLower = name.ToLower();
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
                    list.Add(new RunningProcessItem {
                        ProcessName = exeName,
                        WindowTitle = title,
                        Pid = p.Id
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
    }
}