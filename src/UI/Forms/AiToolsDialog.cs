// ============================================================================
// 模块名称: AiToolsDialog.cs
// 所属区域: UI/Forms (业务对话框层)
// 核心职责:
//   展示推荐的本地 AI Agent 工具（DeepSeek Harness、Workbuddy、Antigravity、Codex），
//   引导用户自主使用智能体逆向适配未识别硬件。
// 依赖关系: Controls.*, Core.*
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、属性或绘图逻辑时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（❌ 严禁 $"..." 插值，必须使用 string.Format 或 + 拼接）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class AiToolsDialog : DpiAwareForm
    {
        private class ToolItem
        {
            public string Name { get; set; }
            public string Description { get; set; }
            public string Url { get; set; }
        }

        private ToolItem[] tools = new ToolItem[]
        {
            new ToolItem
            {
                Name = "DeepSeek Harness",
                Description = "开源智能体运行时，支持本地命令与工具链自动化调度",
                Url = "https://www.deepseek.com/harness/"
            },
            new ToolItem
            {
                Name = "Tencent Workbuddy",
                Description = "腾讯桌面级自主智能体工作台，内置编码环境与本地自主规划",
                Url = "https://workbuddy.ai/"
            },
            new ToolItem
            {
                Name = "Google Antigravity",
                Description = "Google DeepMind 新一代自主编程与硬件系统级调试智能体（需科学上网）",
                Url = "https://antigravity.google/"
            },
            new ToolItem
            {
                Name = "OpenAI Codex",
                Description = "深度集成的代码智能体开发环境与自动化工具链（需科学上网）",
                Url = "https://openai.com/zh-Hans-CN/codex/"
            }
        };

        public AiToolsDialog() : this(1.0f) { }

        public AiToolsDialog(float scale) : base(scale)
        {
            this.Text = "推荐 AI Agent 工具";
            this.ClientSize = new Size((int)(460 * dpiScale), (int)(460 * dpiScale));
            this.BackColor = Color.FromArgb(18, 19, 23);
            this.ForeColor = Color.White;
            this.StartPosition = FormStartPosition.CenterParent;
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
            this.MaximizeBox = false;
            this.MinimizeBox = false;

            int pad = (int)(18 * dpiScale);

            // Title
            Label lblTitle = new Label();
            lblTitle.Text = "🚀 推荐 AI Agent 智能体工具";
            lblTitle.Font = new Font("Microsoft YaHei UI", 11.5F, FontStyle.Bold);
            lblTitle.ForeColor = Color.FromArgb(240, 245, 255);
            lblTitle.Location = new Point(pad, (int)(16 * dpiScale));
            lblTitle.AutoSize = true;
            this.Controls.Add(lblTitle);

            // Subtitle
            Label lblSub = new Label();
            lblSub.Text = "推荐以下具备系统探测与自主编码能力的本地智能体：";
            lblSub.Font = new Font("Microsoft YaHei UI", 8.8F);
            lblSub.ForeColor = Color.FromArgb(145, 155, 175);
            lblSub.Location = new Point(pad, (int)(42 * dpiScale));
            lblSub.Size = new Size(this.ClientSize.Width - pad * 2, (int)(20 * dpiScale));
            this.Controls.Add(lblSub);

            // Cards Container
            int cardY = (int)(68 * dpiScale);
            int cardH = (int)(74 * dpiScale);
            int cardGap = (int)(10 * dpiScale);

            for (int i = 0; i < tools.Length; i++)
            {
                ToolItem item = tools[i];
                Panel card = CreateToolCard(item, pad, cardY, this.ClientSize.Width - pad * 2, cardH);
                this.Controls.Add(card);
                cardY += cardH + cardGap;
            }

            // Bottom Close Button
            ModernButton btnClose = new ModernButton();
            btnClose.Text = "关闭";
            btnClose.Font = new Font("Microsoft YaHei UI", 9F);
            btnClose.NormalColor = Color.FromArgb(32, 36, 48);
            btnClose.HoverColor = Color.FromArgb(46, 52, 68);
            btnClose.PressedColor = Color.FromArgb(24, 28, 38);
            btnClose.BorderColor = Color.FromArgb(60, 68, 88);
            btnClose.TextColor = Color.FromArgb(220, 230, 250);
            int closeW = (int)(100 * dpiScale);
            int closeH = (int)(32 * dpiScale);
            btnClose.Location = new Point(this.ClientSize.Width - pad - closeW, this.ClientSize.Height - (int)(16 * dpiScale) - closeH);
            btnClose.Size = new Size(closeW, closeH);
            btnClose.Click += (s, e) => this.Close();
            this.Controls.Add(btnClose);
        }

        private Panel CreateToolCard(ToolItem item, int x, int y, int w, int h)
        {
            Panel p = new Panel();
            p.Location = new Point(x, y);
            p.Size = new Size(w, h);
            p.BackColor = Color.FromArgb(24, 27, 36);

            p.Paint += (s, e) =>
            {
                Graphics g = e.Graphics;
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

                Rectangle rect = new Rectangle(0, 0, p.Width - 1, p.Height - 1);
                using (GraphicsPath path = RoundedCard.GetRoundedRectangle(rect, (int)(6 * dpiScale)))
                {
                    using (SolidBrush bg = new SolidBrush(Color.FromArgb(24, 27, 36)))
                    {
                        g.FillPath(bg, path);
                    }
                    using (Pen borderPen = new Pen(Color.FromArgb(42, 48, 64), 1f))
                    {
                        g.DrawPath(borderPen, path);
                    }
                }
            };

            int pad = (int)(12 * dpiScale);

            // Name
            Label lblName = new Label();
            lblName.Text = item.Name;
            lblName.Font = new Font("Microsoft YaHei UI", 9.8F, FontStyle.Bold);
            lblName.ForeColor = Color.White;
            lblName.Location = new Point(pad, (int)(12 * dpiScale));
            lblName.AutoSize = true;
            p.Controls.Add(lblName);

            // Description
            Label lblDesc = new Label();
            lblDesc.Text = item.Description;
            lblDesc.Font = new Font("Microsoft YaHei UI", 8.2F);
            lblDesc.ForeColor = Color.FromArgb(150, 160, 180);
            lblDesc.Location = new Point(pad, (int)(38 * dpiScale));
            lblDesc.Size = new Size(w - pad * 2 - (int)(100 * dpiScale), (int)(30 * dpiScale));
            p.Controls.Add(lblDesc);

            // Open Link Button
            ModernButton btnLink = new ModernButton();
            btnLink.Text = "访问官网 ↗";
            btnLink.Font = new Font("Microsoft YaHei UI", 8.5F, FontStyle.Bold);
            btnLink.NormalColor = Color.FromArgb(32, 40, 56);
            btnLink.HoverColor = Color.FromArgb(46, 58, 82);
            btnLink.PressedColor = Color.FromArgb(22, 28, 40);
            btnLink.BorderColor = Color.FromArgb(64, 80, 110);
            btnLink.TextColor = Color.FromArgb(0, 210, 255);
            int btnW = (int)(92 * dpiScale);
            int btnH = (int)(30 * dpiScale);
            btnLink.Location = new Point(w - pad - btnW, (int)(22 * dpiScale));
            btnLink.Size = new Size(btnW, btnH);
            btnLink.Click += (s, e) =>
            {
                try
                {
                    Process.Start(item.Url);
                }
                catch (Exception diagEx) { Diagnostics.Log("UI/Forms/AiToolsDialog.cs: 已忽略异常", diagEx); }
            };
            p.Controls.Add(btnLink);

            return p;
        }
    }
}
