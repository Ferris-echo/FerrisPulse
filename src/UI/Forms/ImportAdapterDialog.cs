// ============================================================================
// 模块名称: ImportAdapterDialog.cs
// 所属区域: UI/Forms (业务对话框层)
// 核心职责:
//   现代化双模硬件扩展包 (FDAS v1.0/v2.0 JSON 与 .fp-driver.cs C# 5.0 脚本驱动) 导入、
//   实时契约/编译校验、内置包模板一键导出与热重载生效窗口。
// 依赖关系: Controls.ModernButton, Controls.RoundedCard, Hardware.FdasRuntimeHelper,
//           Hardware.CsScriptDriverLoader, Hardware.BuiltInPackageExporter, Hardware.HardwareDriverRegistry
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
using System.IO;
using System.Text;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class ImportAdapterDialog : DpiAwareForm
    {
        private TextBox txtJson;
        private Label lblStatus;
        private ModernButton btnPaste;
        private ModernButton btnBrowse;
        private ModernButton btnExportBuiltIn;
        private ModernButton btnSaveAndApply;
        private ModernButton btnCancel;

        private FdasAdapterDefinition parsedDefinition;
        private IHardwareDriver compiledScriptDriver;

        public FdasAdapterDefinition ParsedDefinition
        {
            get { return parsedDefinition; }
        }

        public IHardwareDriver CompiledScriptDriver
        {
            get { return compiledScriptDriver; }
        }

        public ImportAdapterDialog(float scale) : base(scale)
        {
            this.Text = "导入 AI 外设扩展包 (FDAS v2.0 / .fp-driver.cs)";
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.StartPosition = FormStartPosition.CenterParent;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.ShowInTaskbar = false;
            this.BackColor = Color.FromArgb(18, 19, 23);
            this.ForeColor = Color.White;

            int clientW = Scale(500);
            int clientH = Scale(540);
            this.ClientSize = new Size(clientW, clientH);

            int pad = Scale(16);

            Label lblTitle = new Label();
            lblTitle.Text = "📥 导入 / 粘贴 AI 外设扩展包 (JSON / C# 脚本)";
            lblTitle.Font = new Font("Microsoft YaHei UI", 11F, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(240, 245, 255);
            lblTitle.Location = new Point(pad, Scale(12));
            lblTitle.AutoSize = true;
            this.Controls.Add(lblTitle);

            Label lblDesc = new Label();
            lblDesc.Text = "支持粘贴 FDAS v2.0 (.fp-adapter.json) 或 C# 5.0 驱动脚本 (.fp-driver.cs)：";
            lblDesc.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblDesc.ForeColor = Color.FromArgb(150, 160, 180);
            lblDesc.Location = new Point(pad, Scale(38));
            lblDesc.Size = new Size(clientW - pad * 2, Scale(20));
            this.Controls.Add(lblDesc);

            // Action Toolbar (Paste, Browse, Export Built-in Templates)
            int tbY = Scale(62);
            int tbBtnH = Scale(28);
            int pasteW = Scale(130);
            btnPaste = new ModernButton();
            btnPaste.Text = "📋 一键粘贴剪贴板";
            btnPaste.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            btnPaste.NormalColor = Color.FromArgb(32, 42, 60);
            btnPaste.HoverColor = Color.FromArgb(46, 60, 86);
            btnPaste.PressedColor = Color.FromArgb(22, 30, 44);
            btnPaste.BorderColor = Color.FromArgb(64, 82, 116);
            btnPaste.TextColor = Color.FromArgb(220, 235, 255);
            btnPaste.Location = new Point(pad, tbY);
            btnPaste.Size = new Size(pasteW, tbBtnH);
            btnPaste.Click += (s, e) => DoPasteFromClipboard();
            this.Controls.Add(btnPaste);

            int browseW = Scale(130);
            btnBrowse = new ModernButton();
            btnBrowse.Text = "📂 载入本地扩展包...";
            btnBrowse.Font = new Font("Microsoft YaHei UI", 8.5F);
            btnBrowse.NormalColor = Color.FromArgb(28, 34, 48);
            btnBrowse.HoverColor = Color.FromArgb(40, 50, 70);
            btnBrowse.PressedColor = Color.FromArgb(20, 24, 34);
            btnBrowse.BorderColor = Color.FromArgb(56, 70, 98);
            btnBrowse.TextColor = Color.FromArgb(200, 215, 240);
            btnBrowse.Location = new Point(pad + pasteW + Scale(8), tbY);
            btnBrowse.Size = new Size(browseW, tbBtnH);
            btnBrowse.Click += (s, e) =>
            {
                using (OpenFileDialog ofd = new OpenFileDialog())
                {
                    ofd.Filter = "硬件扩展包 (*.fp-adapter.json;*.fp-driver.cs;*.json;*.cs)|*.fp-adapter.json;*.fp-driver.cs;*.json;*.cs|所有文件 (*.*)|*.*";
                    ofd.Title = "选择 FDAS JSON 或 C# 脚本扩展包";
                    if (ofd.ShowDialog(this) == DialogResult.OK)
                    {
                        try
                        {
                            txtJson.Text = File.ReadAllText(ofd.FileName, Encoding.UTF8);
                            ValidateJsonInput();
                        }
                        catch (Exception ex)
                        {
                            MessageBox.Show("读取文件失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                        }
                    }
                }
            };
            this.Controls.Add(btnBrowse);

            int exportW = Scale(140);
            btnExportBuiltIn = new ModernButton();
            btnExportBuiltIn.Text = "📤 导出内置包模板";
            btnExportBuiltIn.Font = new Font("Microsoft YaHei UI", 8.5F);
            btnExportBuiltIn.NormalColor = Color.FromArgb(38, 30, 52);
            btnExportBuiltIn.HoverColor = Color.FromArgb(56, 44, 78);
            btnExportBuiltIn.PressedColor = Color.FromArgb(26, 20, 38);
            btnExportBuiltIn.BorderColor = Color.FromArgb(88, 68, 122);
            btnExportBuiltIn.TextColor = Color.FromArgb(225, 210, 255);
            btnExportBuiltIn.Location = new Point(pad + pasteW + browseW + Scale(16), tbY);
            btnExportBuiltIn.Size = new Size(exportW, tbBtnH);
            btnExportBuiltIn.Click += (s, e) => DoExportBuiltInTemplates();
            this.Controls.Add(btnExportBuiltIn);

            // Multiline Editor
            int txtY = Scale(96);
            int txtH = Scale(265);
            txtJson = new TextBox();
            txtJson.Multiline = true;
            txtJson.ScrollBars = ScrollBars.Vertical;
            txtJson.Font = new Font("Consolas", 9F);
            txtJson.BackColor = Color.FromArgb(24, 27, 36);
            txtJson.ForeColor = Color.FromArgb(230, 235, 245);
            txtJson.BorderStyle = BorderStyle.FixedSingle;
            txtJson.Location = new Point(pad, txtY);
            txtJson.Size = new Size(clientW - pad * 2, txtH);
            txtJson.TextChanged += (s, e) => ValidateJsonInput();
            this.Controls.Add(txtJson);

            // Status Card
            int statusY = txtY + txtH + Scale(10);
            int statusH = Scale(96);
            Panel pnlStatus = new Panel();
            pnlStatus.Location = new Point(pad, statusY);
            pnlStatus.Size = new Size(clientW - pad * 2, statusH);
            pnlStatus.BackColor = Color.FromArgb(22, 25, 34);
            pnlStatus.Paint += (s, e) =>
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                Rectangle rect = new Rectangle(0, 0, pnlStatus.Width - 1, pnlStatus.Height - 1);
                using (GraphicsPath path = RoundedCard.GetRoundedRectangle(rect, Scale(6)))
                {
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(22, 25, 34))) g.FillPath(bg, path);
                    bool isValid = (parsedDefinition != null || compiledScriptDriver != null);
                    using (Pen pen = new Pen(isValid ? Color.FromArgb(0, 200, 100) : Color.FromArgb(50, 60, 80), 1f))
                    {
                        g.DrawPath(pen, path);
                    }
                }
            };
            this.Controls.Add(pnlStatus);

            lblStatus = new Label();
            lblStatus.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblStatus.ForeColor = Color.FromArgb(160, 170, 190);
            lblStatus.Location = new Point(Scale(10), Scale(8));
            lblStatus.Size = new Size(clientW - pad * 2 - Scale(20), statusH - Scale(16));
            lblStatus.Text = "● 等待输入：请粘贴或载入 FDAS v2.0 JSON (.fp-adapter.json) 或 C# 脚本 (.fp-driver.cs)。";
            pnlStatus.Controls.Add(lblStatus);

            // Bottom Buttons
            int btnH = Scale(34);
            int btnY = clientH - btnH - Scale(14);
            int cancelW = Scale(100);
            int applyW = Scale(160);

            btnCancel = new ModernButton();
            btnCancel.Text = "取消";
            btnCancel.Font = new Font("Microsoft YaHei UI", 9F);
            btnCancel.NormalColor = Color.FromArgb(32, 36, 48);
            btnCancel.HoverColor = Color.FromArgb(46, 52, 68);
            btnCancel.PressedColor = Color.FromArgb(24, 28, 38);
            btnCancel.BorderColor = Color.FromArgb(60, 68, 88);
            btnCancel.TextColor = Color.FromArgb(220, 230, 250);
            btnCancel.Location = new Point(clientW - pad - cancelW - applyW - Scale(10), btnY);
            btnCancel.Size = new Size(cancelW, btnH);
            btnCancel.Click += (s, e) => { this.DialogResult = DialogResult.Cancel; this.Close(); };
            this.Controls.Add(btnCancel);

            btnSaveAndApply = new ModernButton();
            btnSaveAndApply.Text = "🚀 保存并立即启用";
            btnSaveAndApply.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            btnSaveAndApply.NormalColor = Color.FromArgb(0, 180, 80);
            btnSaveAndApply.HoverColor = Color.FromArgb(0, 210, 100);
            btnSaveAndApply.PressedColor = Color.FromArgb(0, 150, 65);
            btnSaveAndApply.BorderColor = Color.FromArgb(0, 220, 110);
            btnSaveAndApply.TextColor = Color.White;
            btnSaveAndApply.Enabled = false;
            btnSaveAndApply.Location = new Point(clientW - pad - applyW, btnY);
            btnSaveAndApply.Size = new Size(applyW, btnH);
            btnSaveAndApply.Click += (s, e) => SaveAndApplyAdapter();
            this.Controls.Add(btnSaveAndApply);

            // Auto-check clipboard on load
            this.Shown += (s, e) =>
            {
                try
                {
                    if (Clipboard.ContainsText())
                    {
                        string txt = Clipboard.GetText();
                        if (txt.Contains("fdasVersion") || (txt.Contains("metadata") && txt.Contains("hardware")) || txt.Contains("HardwareDriverBase"))
                        {
                            DoPasteFromClipboard();
                        }
                    }
                }
                catch { }
            };
        }

        private void DoExportBuiltInTemplates()
        {
            try
            {
                string dir = FdasRuntimeHelper.GetAdaptersDirectory();
                if (string.IsNullOrEmpty(dir))
                {
                    dir = FdasRuntimeHelper.GetAppDataAdaptersDirectory();
                }

                int count = BuiltInPackageExporter.ExportDefaultPackages(dir, false);
                MessageBox.Show(
                    string.Format("已成功导出 {0} 个内置扩展包参考模板至：\n{1}\n\n您可以直接用编辑器修改这些 .fp-adapter.json 或 .fp-driver.cs 文件来热覆盖默认驱动逻辑。", count, dir),
                    "内置扩展包模板导出完成",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show("导出内置模板失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void DoPasteFromClipboard()
        {
            try
            {
                if (Clipboard.ContainsText())
                {
                    string cb = Clipboard.GetText();
                    if (cb.Contains("```"))
                    {
                        int firstFence = cb.IndexOf("```");
                        int afterFirstLine = cb.IndexOf('\n', firstFence);
                        int lastFence = cb.LastIndexOf("```");
                        if (afterFirstLine > firstFence && lastFence > afterFirstLine)
                        {
                            cb = cb.Substring(afterFirstLine + 1, lastFence - afterFirstLine - 1).Trim();
                        }
                        else
                        {
                            int first = cb.IndexOf('{');
                            int last = cb.LastIndexOf('}');
                            if (first >= 0 && last > first)
                            {
                                cb = cb.Substring(first, last - first + 1);
                            }
                        }
                    }
                    txtJson.Text = cb;
                    ValidateJsonInput();
                }
            }
            catch { }
        }

        private static bool LooksLikeCSharpScript(string raw)
        {
            if (string.IsNullOrEmpty(raw)) return false;
            if (raw.Contains("HardwareDriverBase") || raw.Contains("IHardwareDriver")) return true;
            if (raw.StartsWith("using ") || raw.StartsWith("namespace ") || raw.Contains("class ")) return true;
            return false;
        }

        private void ValidateJsonInput()
        {
            string raw = txtJson.Text != null ? txtJson.Text.Trim() : "";
            if (string.IsNullOrEmpty(raw))
            {
                parsedDefinition = null;
                compiledScriptDriver = null;
                lblStatus.ForeColor = Color.FromArgb(160, 170, 190);
                lblStatus.Text = "● 等待输入：请粘贴或载入 FDAS v2.0 JSON (.fp-adapter.json) 或 C# 脚本 (.fp-driver.cs)。";
                btnSaveAndApply.Enabled = false;
                return;
            }

            if (LooksLikeCSharpScript(raw))
            {
                parsedDefinition = null;
                List<string> errors;
                IHardwareDriver drv = CsScriptDriverLoader.ValidateAndCompileScript(raw, out errors);
                if (drv != null)
                {
                    compiledScriptDriver = drv;
                    lblStatus.ForeColor = Color.FromArgb(0, 230, 118);
                    lblStatus.Text = string.Format(
                        "✓ C# 5.0 驱动脚本内存编译通过: {0}\n驱动ID: {1} | 优先级: {2} (可热覆盖同ID或同VID:PID内置驱动)",
                        drv.Brand, drv.DriverId, drv.Priority);
                    btnSaveAndApply.Enabled = true;
                }
                else
                {
                    compiledScriptDriver = null;
                    lblStatus.ForeColor = Color.FromArgb(255, 110, 110);
                    string errText = (errors != null && errors.Count > 0) ? errors[0] : "未找到实现 IHardwareDriver / HardwareDriverBase 的公开类。";
                    lblStatus.Text = "● C# 脚本编译失败: " + errText;
                    btnSaveAndApply.Enabled = false;
                }
                return;
            }

            compiledScriptDriver = null;
            try
            {
                FdasAdapterDefinition def = FdasRuntimeHelper.ParseAdapterJson(raw, null);
                if (def != null)
                {
                    parsedDefinition = def;
                    lblStatus.ForeColor = Color.FromArgb(0, 230, 118);
                    string battInfo = def.SampleBattery > 0 ? (def.SampleBattery.ToString() + "% (断言正常)") : "免驱读取";
                    string catCn = MouseBatteryInfo.GetCategoryDisplayName(def.Category);
                    string extInfo = "";
                    if (def.Category == DeviceCategory.Mouse)
                    {
                        extInfo = string.Format("\n特性: DPI {0}~{1} | XY独立: {2} | 回报率: {3}Hz | PID矩阵: {4}项",
                            def.MinDpi, def.MaxDpi, def.SupportsXyIndependence ? "支持" : "单轴", def.MaxRate,
                            def.Devices != null && def.Devices.Count > 0 ? def.Devices.Count : 1);
                    }
                    else if (def.CustomActionDefs != null && def.CustomActionDefs.Count > 0)
                    {
                        extInfo = string.Format("\n特性: 声明式自定义动作 {0} 项 | 传感器/固件: {1}",
                            def.CustomActionDefs.Count, string.IsNullOrEmpty(def.SensorDescription) ? "标准" : def.SensorDescription);
                    }
                    lblStatus.Text = string.Format("✓ FDAS v{0} 契约校验通过: {1} {2} [{3}]\n硬件: VID=0x{4:X4}, PID=0x{5:X4} | 电量样本: {6}{7}",
                        def.FdasVersion, def.Brand, def.Model, catCn, def.VendorId, def.ProductId, battInfo, extInfo);
                    btnSaveAndApply.Enabled = true;
                }
                else
                {
                    parsedDefinition = null;
                    lblStatus.ForeColor = Color.FromArgb(255, 170, 0);
                    lblStatus.Text = "● 校验失败：未找到有效 FDAS v1.0/v2.0 根契约 (需包含 fdasVersion, metadata, hardware 等核心分区) 或 HardwareDriverBase 脚本。";
                    btnSaveAndApply.Enabled = false;
                }
            }
            catch (Exception ex)
            {
                parsedDefinition = null;
                lblStatus.ForeColor = Color.FromArgb(255, 100, 100);
                lblStatus.Text = "● 解析异常: " + ex.Message;
                btnSaveAndApply.Enabled = false;
            }
        }

        private void SaveAndApplyAdapter()
        {
            if (compiledScriptDriver != null)
            {
                try
                {
                    string savedPath = CsScriptDriverLoader.SaveScriptDriverToDisk(txtJson.Text, compiledScriptDriver);
                    HardwareDriverRegistry.ReloadExternalPackages();

                    MessageBox.Show(
                        string.Format("🎉 C# 驱动扩展包 [{0} ({1})] 编译并导入成功！\n保存位置: {2}\n已即时热重载生效。",
                            compiledScriptDriver.Brand, compiledScriptDriver.DriverId, savedPath),
                        "脚本驱动导入成功", MessageBoxButtons.OK, MessageBoxIcon.Information);

                    this.DialogResult = DialogResult.OK;
                    this.Close();
                }
                catch (Exception ex)
                {
                    MessageBox.Show("保存脚本驱动扩展包失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                }
                return;
            }

            if (parsedDefinition == null) return;

            try
            {
                string dir = FdasRuntimeHelper.GetAdaptersDirectory();
                if (string.IsNullOrEmpty(dir))
                {
                    dir = FdasRuntimeHelper.GetAppDataAdaptersDirectory();
                }

                string safeBrand = parsedDefinition.Brand.Replace(" ", "");
                string safeModel = parsedDefinition.Model.Replace(" ", "_").Replace("/", "_").Replace("\\", "_");
                string fileName = string.Format("{0}_{1}.fp-adapter.json", safeBrand, safeModel);
                string fullPath = Path.Combine(dir, fileName);

                UTF8Encoding utf8Bom = new UTF8Encoding(true);
                File.WriteAllText(fullPath, txtJson.Text, utf8Bom);

                CustomDeviceConfig cfg = new CustomDeviceConfig();
                cfg.DeviceId = string.Format("FDAS:{0:X4}:{1:X4}", parsedDefinition.VendorId, parsedDefinition.ProductId);
                cfg.HardwareFingerprint = cfg.DeviceId;
                cfg.OriginalName = parsedDefinition.Brand + " " + parsedDefinition.Model;
                cfg.CustomName = cfg.OriginalName;
                cfg.Category = parsedDefinition.Category;
                cfg.Transport = "2.4G/USB";
                cfg.CachedBattery = parsedDefinition.SampleBattery > 0 ? parsedDefinition.SampleBattery : 80;
                cfg.LastSeen = DateTime.Now;

                DeviceManager.SaveCustomDevice(cfg);
                DeviceManager.UnhideDevice(cfg.DeviceId);

                FdasRuntimeHelper.ReloadAdapters();
                HardwareDriverRegistry.ReloadExternalPackages();

                MessageBox.Show(string.Format("🎉 适配包 [{0} {1}] 导入成功！\n已即时生效并在本地启用。", parsedDefinition.Brand, parsedDefinition.Model),
                    "适配成功", MessageBoxButtons.OK, MessageBoxIcon.Information);

                this.DialogResult = DialogResult.OK;
                this.Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show("保存适配包失败: " + ex.Message, "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
