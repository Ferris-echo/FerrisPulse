// ============================================================================
// 模块名称: AboutForm.cs
// 所属区域: UI/Forms (二级窗体层)
// 核心职责:
//   专属黑金电竞美学关于与致谢卡片：呈现作者 Ferris-echo 署名、版本状态、
//   在线检查更新、自动更新配置以及微信/支付宝赞赏支持双艺术二维码（纯净无图标极客矩阵）。
//   支持原生 Win32 平滑拖动、ESC 快速退出、暗黑质感防眩光布局与严格像素级对齐居中。
//   极致视听与动效系统：
//   1. 八音阶纯内存即时并发水晶钢片琴（2.5s 空闲重置），急速连击零停顿、零掐音
//   2. 正面作者署名交互：星芒迸发与金辉涟漪直接在正面面板绘制呈现
//   3. 全屏极光漫融转场 + 黑曜石钛金镜面视差彩蛋卡（随鼠标扫光）与三层丝绸脉冲光波剧场
//   4. 精密矢量脉冲星环徽章 + 现代电影字幕级渐显排版（保持进入彩蛋隐藏标题栏的纯正沉浸体验）
//   5. 鼠标星尘拖尾流光（Stardust Trail）与水波涟漪动效（Water Ripples）
//   6. 《写在代码之外》克制本真排版，彻底消除廉价感与文字截断
// 依赖关系: ModernThemeHelper, ModernButton, ModernCheckBox, StatusPill, RoundedCard, UpdateManager, ConfigManager, NativeMethods, ChimeSynthesizer, DonateQrAssets
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 2. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class AboutForm : DpiAwareForm
    {
        private static string AppVersion { get { return UpdateManager.CurrentVersion; } }
        private const string AuthorName = "Ferris-echo";

        // 主容器与双面结构
        private Panel flipContainer;
        private Panel pnlFront;
        private Panel pnlBack;
        private Panel shatterCanvas;

        // 正面控件
        private Panel titleBar;
        private RoundedCard cardUpdate;
        private RoundedCard cardDonate;
        private Label lblCardUpdateTitle;
        private Label lblDonateTitle;
        private ModernCheckBox chkAutoUpdate;
        private ModernButton btnCheckUpdate;
        private Label lblUpdateStatus;
        private bool localCheckCompleted = false;
        private bool localCheckFailed = false;

        private float childDialogScale = 0f;
        public float ChildDialogScale
        {
            get { return childDialogScale; }
            set
            {
                if (Math.Abs(childDialogScale - value) > 0.001f)
                {
                    childDialogScale = value;
                    if (modernChromeTitleBar != null && !this.IsDisposed)
                    {
                        ApplyAboutLayout();
                    }
                }
            }
        }
        private Label lblLogo;
        private Label lblAuthorPrefix;
        private Label lblAuthorName;
        private Label lblAuthorSuffix;
        private Label lblDonateDesc;

        // 双二维码赞赏面板
        private Panel qrPanelWeChat;
        private Panel qrPanelAlipay;
        private LinkLabel lnkQQ;
        private Label lblFooterDot;
        private LinkLabel lnkGithub;
        private LinkLabel lnkResetApp;
        private LinkLabel lnkClearCache;
        private int footerRow1Y = 0;

        // 背面控件
        private RoundedCard cardQuote;
        private ModernButton btnFlipBack;

        // 基础粒子模型
        private class AmbientParticle
        {
            public float X, Y;
            public float Vx, Vy;
            public float Alpha;
            public float Size;
            public Color Color;
            public float Phase;
        }

        // 水波纹与转场粒子模型
        private class WaterRipple
        {
            public float X, Y;
            public float Radius;
            public float MaxRadius;
            public float Alpha;
            public float Speed;
            public float Wavelength;
            public int Age;
        }

        // 浪漫唯美爱心粒子模型 (Romantic Vector Heart Motes)
        private class HeartParticle
        {
            public float X, Y;
            public float Vx, Vy;
            public float Alpha;
            public float Size;
            public float Rotation;
            public float RotSpeed;
            public Color Color;
        }

        // 全屏极光漫融模型 (Full-Window Aurora Veil & Floating Vector Hearts)
        private class EtherealMote
        {
            public float X, Y;
            public float Vx, Vy;
            public float Alpha;
            public float Size;
            public Color Color;
            public bool IsHeart;
            public float Rotation;
            public float RotSpeed;
        }

        private class ShatterShard { } // 兼容旧接口引用

        private System.Collections.Generic.List<AmbientParticle> frontParticles = new System.Collections.Generic.List<AmbientParticle>();
        private System.Collections.Generic.List<WaterRipple> frontRipples = new System.Collections.Generic.List<WaterRipple>();
        private System.Collections.Generic.List<HeartParticle> heartParticles = new System.Collections.Generic.List<HeartParticle>();
        private bool wasFrontParticlesActive = false;
        private System.Collections.Generic.List<AmbientParticle> backParticles = new System.Collections.Generic.List<AmbientParticle>();
        private System.Collections.Generic.List<WaterRipple> waterRipples = new System.Collections.Generic.List<WaterRipple>();
        private System.Collections.Generic.List<EtherealMote> transitionMotes = new System.Collections.Generic.List<EtherealMote>();
        private System.Collections.Generic.List<ShatterShard> shatterShards = new System.Collections.Generic.List<ShatterShard>();

        private Bitmap snapFront = null;
        private Bitmap snapBack = null;

        private Timer animationTimer = null;
        private Random rnd = new Random();
        private DateTime backOpenTime = DateTime.MinValue;

        // 八音阶彩蛋与 2.5s 空闲重置计时器
        private int easterEggNoteIndex = 0;
        private Timer noteResetTimer = null;

        // 背面黑曜石流光、丝绸脉冲光波与画布交互状态
        private bool isChromaActive = false;
        private float chromaHue = 0f;
        private float pulsePhase = 0f;
        private float smoothMouseX = 280f;
        private float smoothMouseY = 260f;
        private float targetMouseX = 280f;
        private float targetMouseY = 260f;
        private bool hasPointerInBack = false;
        private float waveEnergy = 0f;
        private float targetWaveEnergy = 0f;
        private float wavePluckX = 0.5f;
        private float targetWavePluckX = 0.5f;
        private RectangleF backBtnRect = RectangleF.Empty;
        private bool isBackBtnHovered = false;
        private bool isBackBtnPressed = false;
        private float backBtnHoverProgress = 0f;
        private float clickBloomX = 0f;
        private float clickBloomY = 0f;
        private float clickBloomAlpha = 0f;
        private float clickBloomRadius = 0f;

        // 柔和微光漫射过渡动效计时器 (Ethereal Luminescent Transition)
        private Timer shatterTimer = null;
        private float shatterProgress = 0f;
        private bool isShatteringToBack = true;
        private const int TotalShatterFrames = 30;
        private int currentShatterFrame = 0;

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.Style |= 0x02000000; // WS_CLIPCHILDREN: 杜绝主窗体背景绘制涂抹子控件
                return cp;
            }
        }

        public float EffectiveScale { get { return dpiScale > 0f ? dpiScale : 1.0f; } }

        public AboutForm() : this(1.0f)
        {
        }


        public AboutForm(float scale) : base(scale)
        {

            InitializeAboutUI();
            UpdateManager.OnPendingUpdateChanged += OnPendingUpdateChangedHandler;
            UpdateAboutUpdateButtonState();
        }

        private void InitializeAboutUI()
        {
            this.Text = "关于 FerrisPulse";
            this.FormBorderStyle = FormBorderStyle.None;
            this.MaximizeBox = false;
            this.MinimizeBox = false;
            this.StartPosition = FormStartPosition.CenterScreen;
            this.BackColor = Color.FromArgb(18, 19, 23);
            this.ForeColor = Color.White;
            this.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Regular, GraphicsUnit.Point);
            this.DoubleBuffered = true;
            this.ShowInTaskbar = false;
            this.TopMost = false;
            this.KeyPreview = true;
            this.KeyDown += delegate(object s, KeyEventArgs e)
            {
                if (e.KeyCode == Keys.Escape)
                {
                    if (pnlBack != null && pnlBack.Visible)
                    {
                        ChimeSynthesizer.StopAmbientMelody();
                        TriggerQuantumShatter(false);
                    }
                    else
                    {
                        this.Close();
                    }
                }
            };

            this.FormClosing += delegate(object s, FormClosingEventArgs e)
            {
                ChimeSynthesizer.StopAmbientMelody();
            };

            // 1. 统一接入纯净暗黑无图标自绘标题栏（保留 AboutForm 外框流光/细边框绘制）
            titleBar = new Panel();
            titleBar.Visible = false;
            modernChromeDrawBorder = false;
            modernChromeTitlePadLeftBase = 24;
            AttachModernChrome("FerrisPulse · 灵脉", null, 0, false, false);
            if (modernChromeTitleBar != null)
            {
                EnableDoubleBuffer(modernChromeTitleBar);
                modernChromeTitleBar.Paint += delegate(object s, PaintEventArgs pe)
                {
                    if (pnlFront != null && pnlFront.Visible && flipContainer != null && (heartParticles.Count > 0 || frontParticles.Count > 0))
                    {
                        float ox = (flipContainer.Left + pnlFront.Left) - modernChromeTitleBar.Left;
                        float oy = (flipContainer.Top + pnlFront.Top) - modernChromeTitleBar.Top;
                        DrawFrontParticlesOverlay(pe.Graphics, ox, oy);
                    }
                };
            }
            if (modernChromeTitleLabel != null)
            {
                EnableDoubleBuffer(modernChromeTitleLabel);
                modernChromeTitleLabel.Paint += delegate(object s, PaintEventArgs pe)
                {
                    if (pnlFront != null && pnlFront.Visible && flipContainer != null && modernChromeTitleBar != null && (heartParticles.Count > 0 || frontParticles.Count > 0))
                    {
                        float ox = (flipContainer.Left + pnlFront.Left) - modernChromeTitleBar.Left - modernChromeTitleLabel.Left;
                        float oy = (flipContainer.Top + pnlFront.Top) - modernChromeTitleBar.Top - modernChromeTitleLabel.Top;
                        DrawFrontParticlesOverlay(pe.Graphics, ox, oy);
                    }
                };
            }

            // 2. 翻转主容器 (挂接正面面板、背面面板与粒子碎裂画布)
            flipContainer = new Panel();
            flipContainer.BackColor = Color.FromArgb(18, 19, 23);
            flipContainer.MouseDown += OnDragMouseDown;

            // 粒子碎裂重组动效专用画布 (置于顶层，过渡时激活)
            shatterCanvas = new Panel();
            shatterCanvas.Location = new Point(0, 0);
            shatterCanvas.BackColor = Color.FromArgb(18, 19, 23);
            shatterCanvas.Visible = false;
            shatterCanvas.Paint += ShatterCanvas_Paint;
            shatterCanvas.MouseDown += OnDragMouseDown;
            flipContainer.Controls.Add(shatterCanvas);

            // ==========================================
            // 正面面板 (pnlFront) 构建
            // ==========================================
            pnlFront = new Panel();
            pnlFront.Location = new Point(0, 0);
            pnlFront.BackColor = Color.FromArgb(18, 19, 23);
            pnlFront.MouseDown += OnDragMouseDown;
            pnlFront.Paint += PnlFront_Paint;

            lblLogo = new Label();
            lblLogo.Text = "FerrisPulse · 灵脉";
            lblLogo.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold);
            lblLogo.ForeColor = Color.FromArgb(245, 248, 255);
            lblLogo.BackColor = Color.Transparent;
            lblLogo.AutoSize = true;
            lblLogo.MouseDown += OnDragMouseDown;
            pnlFront.Controls.Add(lblLogo);

            // 作者署名热区行
            lblAuthorPrefix = new Label();
            lblAuthorPrefix.Text = "由 ";
            lblAuthorPrefix.Font = new Font("Microsoft YaHei UI", 9.2F, FontStyle.Regular);
            lblAuthorPrefix.ForeColor = Color.FromArgb(175, 182, 198);
            lblAuthorPrefix.BackColor = Color.Transparent;
            lblAuthorPrefix.AutoSize = true;
            lblAuthorPrefix.Cursor = Cursors.Hand;
            lblAuthorPrefix.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) HandleAuthorHit(); };
            pnlFront.Controls.Add(lblAuthorPrefix);

            lblAuthorName = new Label();
            lblAuthorName.Text = AuthorName;
            lblAuthorName.Font = new Font("Microsoft YaHei UI", 9.2F, FontStyle.Bold);
            lblAuthorName.ForeColor = Color.FromArgb(235, 240, 255);
            lblAuthorName.BackColor = Color.Transparent;
            lblAuthorName.AutoSize = true;
            lblAuthorName.Cursor = Cursors.Hand;
            lblAuthorName.MouseEnter += delegate(object s, EventArgs e) { lblAuthorName.ForeColor = Color.FromArgb(255, 255, 255); };
            lblAuthorName.MouseLeave += delegate(object s, EventArgs e) { lblAuthorName.ForeColor = Color.FromArgb(235, 240, 255); };
            lblAuthorName.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) HandleAuthorHit(); };
            pnlFront.Controls.Add(lblAuthorName);

            lblAuthorSuffix = new Label();
            lblAuthorSuffix.Text = " 独立设计与开发";
            lblAuthorSuffix.Font = new Font("Microsoft YaHei UI", 9.2F, FontStyle.Regular);
            lblAuthorSuffix.ForeColor = Color.FromArgb(175, 182, 198);
            lblAuthorSuffix.BackColor = Color.Transparent;
            lblAuthorSuffix.AutoSize = true;
            lblAuthorSuffix.Cursor = Cursors.Hand;
            lblAuthorSuffix.MouseDown += delegate(object s, MouseEventArgs e) { if (e.Button == MouseButtons.Left) HandleAuthorHit(); };
            pnlFront.Controls.Add(lblAuthorSuffix);


            // Card 1: 软件在线更新卡片
            cardUpdate = new RoundedCard();
            cardUpdate.CardColor = Color.FromArgb(25, 27, 34);
            cardUpdate.BorderColor = Color.FromArgb(42, 46, 58);
            cardUpdate.MouseDown += OnDragMouseDown;

            lblCardUpdateTitle = new Label();
            lblCardUpdateTitle.Text = "软件在线更新";
            lblCardUpdateTitle.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            lblCardUpdateTitle.ForeColor = Color.FromArgb(235, 240, 255);
            lblCardUpdateTitle.BackColor = cardUpdate.CardColor;
            lblCardUpdateTitle.AutoSize = true;
            lblCardUpdateTitle.MouseDown += OnDragMouseDown;
            cardUpdate.Controls.Add(lblCardUpdateTitle);

            btnCheckUpdate = new ModernButton();
            btnCheckUpdate.Text = "检查更新";
            btnCheckUpdate.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
            btnCheckUpdate.NormalColor = Color.FromArgb(36, 40, 52);
            btnCheckUpdate.HoverColor = Color.FromArgb(48, 54, 70);
            btnCheckUpdate.PressedColor = Color.FromArgb(28, 30, 40);
            btnCheckUpdate.BorderColor = Color.FromArgb(0, 200, 83);
            btnCheckUpdate.ForeColor = Color.White;
            btnCheckUpdate.Click += BtnCheckUpdate_Click;
            cardUpdate.Controls.Add(btnCheckUpdate);

            chkAutoUpdate = new ModernCheckBox();
            chkAutoUpdate.Text = "开机与后台自动检查更新";
            chkAutoUpdate.Font = new Font("Microsoft YaHei UI", 8.8F);
            chkAutoUpdate.Checked = ConfigManager.GetAutoCheckUpdate();
            chkAutoUpdate.CheckedChanged += delegate(object s, EventArgs e)
            {
                ConfigManager.SetAutoCheckUpdate(chkAutoUpdate.Checked);
            };
            cardUpdate.Controls.Add(chkAutoUpdate);

            lblUpdateStatus = new Label();
            lblUpdateStatus.Text = "当前版本: " + AppVersion;
            lblUpdateStatus.Font = new Font("Microsoft YaHei UI", 8.5F);
            lblUpdateStatus.ForeColor = Color.FromArgb(140, 148, 162);
            lblUpdateStatus.BackColor = cardUpdate.CardColor;
            lblUpdateStatus.AutoSize = true;
            lblUpdateStatus.MouseDown += OnDragMouseDown;
            cardUpdate.Controls.Add(lblUpdateStatus);

            pnlFront.Controls.Add(cardUpdate);

            // Card 2: 赞赏支持卡片
            cardDonate = new RoundedCard();
            cardDonate.CardColor = Color.FromArgb(25, 27, 34);
            cardDonate.BorderColor = Color.FromArgb(42, 46, 58);
            cardDonate.MouseDown += OnDragMouseDown;

            lblDonateTitle = new Label();
            lblDonateTitle.Text = "支持作者 / 赞赏开发";
            lblDonateTitle.Font = new Font("Microsoft YaHei UI", 9.8F, FontStyle.Bold);
            lblDonateTitle.ForeColor = Color.FromArgb(235, 240, 255);
            lblDonateTitle.BackColor = cardDonate.CardColor;
            lblDonateTitle.AutoSize = true;
            lblDonateTitle.MouseDown += OnDragMouseDown;
            cardDonate.Controls.Add(lblDonateTitle);

            lblDonateDesc = new Label();
            lblDonateDesc.Text = "如果这款软件为您带来了便利，欢迎赞赏支持作者持续更新！";
            lblDonateDesc.Font = new Font("Microsoft YaHei UI", 8.8F);
            lblDonateDesc.ForeColor = Color.FromArgb(160, 168, 185);
            lblDonateDesc.BackColor = Color.Transparent;
            lblDonateDesc.AutoSize = true;
            lblDonateDesc.MouseDown += OnDragMouseDown;
            cardDonate.Controls.Add(lblDonateDesc);

            // 双艺术二维码左右平铺面板
            qrPanelWeChat = new Panel();
            qrPanelWeChat.BackColor = Color.Transparent;
            qrPanelWeChat.Paint += delegate(object sender, PaintEventArgs e)
            {
                DrawArtisticQrCard(e.Graphics, qrPanelWeChat.Width, qrPanelWeChat.Height, true, DonateQrAssets.GetWeChatQr());
            };
            qrPanelWeChat.MouseDown += OnDragMouseDown;
            cardDonate.Controls.Add(qrPanelWeChat);
            qrPanelWeChat.BringToFront();

            qrPanelAlipay = new Panel();
            qrPanelAlipay.BackColor = Color.Transparent;
            qrPanelAlipay.Paint += delegate(object sender, PaintEventArgs e)
            {
                DrawArtisticQrCard(e.Graphics, qrPanelAlipay.Width, qrPanelAlipay.Height, false, DonateQrAssets.GetAlipayQr());
            };
            qrPanelAlipay.MouseDown += OnDragMouseDown;
            cardDonate.Controls.Add(qrPanelAlipay);
            qrPanelAlipay.BringToFront();

            // 底部社区交流群与开源仓库链接
            lnkQQ = new LinkLabel();
            lnkQQ.Text = "QQ 交流群: 580180452";
            lnkQQ.Font = new Font("Microsoft YaHei UI", 8.8F);
            lnkQQ.LinkColor = Color.FromArgb(77, 182, 252);
            lnkQQ.ActiveLinkColor = Color.FromArgb(129, 212, 250);
            lnkQQ.VisitedLinkColor = Color.FromArgb(77, 182, 252);
            lnkQQ.LinkBehavior = LinkBehavior.NeverUnderline;
            lnkQQ.BackColor = Color.Transparent;
            lnkQQ.AutoSize = true;
            lnkQQ.Cursor = Cursors.Hand;
            lnkQQ.MouseEnter += delegate { lnkQQ.LinkColor = Color.FromArgb(140, 215, 255); };
            lnkQQ.MouseLeave += delegate { lnkQQ.LinkColor = Color.FromArgb(77, 182, 252); };
            lnkQQ.LinkClicked += delegate(object s, LinkLabelLinkClickedEventArgs e)
            {
                try
                {
                    Clipboard.SetText("580180452");
                    lnkQQ.Text = "QQ 交流群: 580180452 (已复制)";
                    LayoutAboutFooterRow();

                    Timer copyTimer = new Timer();
                    copyTimer.Interval = 2000;
                    copyTimer.Tick += delegate(object s2, EventArgs e2)
                    {
                        copyTimer.Stop();
                        copyTimer.Dispose();
                        if (!lnkQQ.IsDisposed)
                        {
                            lnkQQ.Text = "QQ 交流群: 580180452";
                            LayoutAboutFooterRow();
                        }
                    };
                    copyTimer.Start();

                    string qqShareUrl = "https://qun.qq.com/universal-share/share?ac=1&authKey=jSTwQOun56pd3gi6ts34tN%2F6TDFrBouMRWJE%2BSsHaKS7%2FrrSlKHLu%2FXYJXfDZpl%2B&busi_data=eyJncm91cENvZGUiOiI1ODAxODA0NTIiLCJ0b2tlbiI6Ind2Y1ZocnhsekFnVi8xN3AzOXl1dTY5VlA2NWtCSFd6NFphcXR2dHhCTW10R3NwYWpyZmhGNXdNS3ZhS2owZzAiLCJ1aW4iOiIyOTQ5MjI1ODQwIn0%3D&data=8-Avoq-5DTwgCiC_xlKHxhhhnZp7d66IdyHPeha1XGGNxS3pp9s1uPoyaE-RtU_ZhIb74Px33-A1ZRe3oIw8vQ&svctype=4&tempid=h5_group_info";
                    try { Process.Start(qqShareUrl); } catch { }
                }
                catch { }
            };
            pnlFront.Controls.Add(lnkQQ);

            lblFooterDot = new Label();
            lblFooterDot.Text = "·";
            lblFooterDot.Font = new Font("Microsoft YaHei UI", 8.8F, FontStyle.Bold);
            lblFooterDot.ForeColor = Color.FromArgb(88, 98, 118);
            lblFooterDot.BackColor = Color.Transparent;
            lblFooterDot.AutoSize = true;
            lblFooterDot.MouseDown += OnDragMouseDown;
            pnlFront.Controls.Add(lblFooterDot);

            lnkGithub = new LinkLabel();
            lnkGithub.Text = "GitHub: Ferris-echo/FerrisPulse";
            lnkGithub.Font = new Font("Microsoft YaHei UI", 8.8F);
            lnkGithub.LinkColor = Color.FromArgb(0, 215, 115);
            lnkGithub.ActiveLinkColor = Color.FromArgb(0, 255, 130);
            lnkGithub.VisitedLinkColor = Color.FromArgb(0, 215, 115);
            lnkGithub.LinkBehavior = LinkBehavior.NeverUnderline;
            lnkGithub.BackColor = Color.Transparent;
            lnkGithub.AutoSize = true;
            lnkGithub.Cursor = Cursors.Hand;
            lnkGithub.MouseEnter += delegate { lnkGithub.LinkColor = Color.FromArgb(60, 255, 160); };
            lnkGithub.MouseLeave += delegate { lnkGithub.LinkColor = Color.FromArgb(0, 215, 115); };
            lnkGithub.LinkClicked += delegate(object s, LinkLabelLinkClickedEventArgs e)
            {
                try { Process.Start("https://github.com/Ferris-echo/FerrisPulse"); } catch { }
            };
            pnlFront.Controls.Add(lnkGithub);

            // 底部低调维护入口：深度重置（独立双行极简页脚第二行，保留 600x668 原生紧凑比例）
            lnkResetApp = new LinkLabel();
            lnkResetApp.Text = "故障排查与深度重置 (清空旧版残留)";
            lnkResetApp.Font = new Font("Microsoft YaHei UI", 8.2F);
            lnkResetApp.LinkColor = Color.FromArgb(125, 134, 150);
            lnkResetApp.ActiveLinkColor = Color.FromArgb(255, 130, 140);
            lnkResetApp.VisitedLinkColor = Color.FromArgb(125, 134, 150);
            lnkResetApp.LinkBehavior = LinkBehavior.NeverUnderline;
            lnkResetApp.BackColor = Color.Transparent;
            lnkResetApp.AutoSize = true;
            lnkResetApp.Cursor = Cursors.Hand;
            lnkResetApp.MouseEnter += delegate { lnkResetApp.LinkColor = Color.FromArgb(235, 150, 160); };
            lnkResetApp.MouseLeave += delegate { lnkResetApp.LinkColor = Color.FromArgb(125, 134, 150); };
            lnkResetApp.LinkClicked += delegate(object s, LinkLabelLinkClickedEventArgs e)
            {
                OpenResetDialog();
            };
            pnlFront.Controls.Add(lnkResetApp);

            // 日常排障入口：清除设备缓存与识别状态（不动自启动与自定义驱动脚本）
            lnkClearCache = new LinkLabel();
            lnkClearCache.Text = "清除缓存 (重新识别外设)";
            lnkClearCache.Font = new Font("Microsoft YaHei UI", 8.2F);
            lnkClearCache.LinkColor = Color.FromArgb(0, 190, 110);
            lnkClearCache.ActiveLinkColor = Color.FromArgb(0, 235, 130);
            lnkClearCache.VisitedLinkColor = Color.FromArgb(0, 190, 110);
            lnkClearCache.LinkBehavior = LinkBehavior.NeverUnderline;
            lnkClearCache.BackColor = Color.Transparent;
            lnkClearCache.AutoSize = true;
            lnkClearCache.Cursor = Cursors.Hand;
            lnkClearCache.MouseEnter += delegate { lnkClearCache.LinkColor = Color.FromArgb(90, 255, 175); };
            lnkClearCache.MouseLeave += delegate { lnkClearCache.LinkColor = Color.FromArgb(0, 190, 110); };
            lnkClearCache.LinkClicked += delegate(object s, LinkLabelLinkClickedEventArgs e)
            {
                OpenCacheClearDialog();
            };
            pnlFront.Controls.Add(lnkClearCache);

            pnlFront.Controls.Add(cardDonate);
            flipContainer.Controls.Add(pnlFront);

            // ==========================================
            // 背面面板 (pnlBack: 《写在代码之外》) 构建
            // ==========================================
            pnlBack = new Panel();
            pnlBack.Location = new Point(0, 0);
            pnlBack.BackColor = Color.FromArgb(18, 19, 23);
            pnlBack.Visible = false;
            pnlBack.MouseDown += OnDragMouseDown;

            cardQuote = new RoundedCard();
            cardQuote.CardColor = Color.FromArgb(11, 13, 19);
            cardQuote.BorderColor = Color.Transparent;
            cardQuote.MouseDown += CardQuote_MouseDown;
            cardQuote.MouseUp += CardQuote_MouseUp;
            cardQuote.MouseMove += CardQuote_MouseMove;
            cardQuote.MouseLeave += delegate
            {
                hasPointerInBack = false;
                isBackBtnHovered = false;
                isBackBtnPressed = false;
                if (cardQuote != null) cardQuote.Cursor = Cursors.Default;
            };
            cardQuote.Paint += CardQuote_Paint;

            // 返回交互已改为在背面黑曜石画布上直接绘制半透明玻璃胶囊，避免子控件切出四角黑边
            btnFlipBack = new ModernButton();
            btnFlipBack.Visible = false;

            pnlBack.Controls.Add(cardQuote);
            flipContainer.Controls.Add(pnlBack);

            this.Controls.Add(flipContainer);

            // 启用全层级无闪烁双缓冲
            EnableDoubleBuffer(flipContainer);
            EnableDoubleBuffer(shatterCanvas);
            EnableDoubleBuffer(pnlFront);
            EnableDoubleBuffer(pnlBack);
            EnableDoubleBuffer(cardQuote);

            // 初始化背景微光环境流光与星芒系统
            InitAmbientAnimations();

            // 挂接原生 Win11 边框与阴影
            this.HandleCreated += delegate(object s, EventArgs e)
            {
                ModernThemeHelper.ApplyModernWin11Theme(this.Handle);
            };

            this.MouseDown += OnDragMouseDown;

            // 应用高质感紧凑自适应布局
            ApplyAboutLayout();
        }

        private int S(int v) { return (int)Math.Round(v * EffectiveScale); }
        private void SetAboutFont(Control ctrl, float basePt, FontStyle style) { if (ctrl != null) ctrl.Font = DpiScaleHelper.GetCachedScaledFont("Microsoft YaHei UI", basePt, style, EffectiveScale, 1.0f); }

        private void ApplyAboutLayout()
        {
            int clientW = S(600);
            int padX = S(24);
            int cardWidth = clientW - (padX * 2);

            // 1. 顶部一体化大标题栏（与正文左边界 padX 严格同轨 + 统一 28×28 暗色胶囊矢量关闭按钮）
            if (titleBar != null)
            {
                titleBar.Visible = false;
            }
            if (modernChromeTitleBar != null)
            {
                float closeScale = Math.Max(EffectiveScale, ResolveChildDialogScale());
                int closeSize = Math.Min(S(46), Math.Max(22, (int)Math.Round(28 * closeScale)));
                int closeRadius = Math.Max(5, (int)Math.Round(7 * closeScale));
                int topInset = S(12);
                int rowH = Math.Max(S(38), closeSize);
                int barH = Math.Max(S(52), topInset + rowH + S(2));
                int barW = Math.Max(40, clientW - 2);
                int sidePad = Math.Max(8, padX - 1);
                modernChromeTitleBar.BackColor = this.BackColor;
                modernChromeTitleBar.Bounds = new Rectangle(1, 1, barW, barH);
                if (modernChromeCloseButton != null)
                {
                    modernChromeCloseButton.IsCloseButton = true;
                    modernChromeCloseButton.Text = "";
                    modernChromeCloseButton.Size = new Size(closeSize, closeSize);
                    modernChromeCloseButton.Location = new Point(Math.Max(0, barW - sidePad - closeSize), topInset + Math.Max(0, (rowH - closeSize) / 2));
                    modernChromeCloseButton.CornerRadius = closeRadius;
                    modernChromeCloseButton.NormalColor = Color.FromArgb(28, 32, 42);
                    modernChromeCloseButton.BorderColor = Color.FromArgb(44, 50, 64);
                    modernChromeCloseButton.HoverColor = Color.FromArgb(215, 45, 58);
                    modernChromeCloseButton.PressedColor = Color.FromArgb(175, 28, 42);
                    modernChromeCloseButton.ForeColor = Color.FromArgb(182, 192, 212);
                }
                if (modernChromeTitleLabel != null)
                {
                    modernChromeTitleLabel.Text = "FerrisPulse · 灵脉";
                    SetAboutFont(modernChromeTitleLabel, 14.5F, FontStyle.Bold);
                    modernChromeTitleLabel.ForeColor = Color.FromArgb(245, 248, 255);
                    int rightLim = (modernChromeCloseButton != null ? modernChromeCloseButton.Left : barW) - S(10);
                    modernChromeTitleLabel.Bounds = new Rectangle(sidePad, topInset, Math.Max(40, rightLim - sidePad), rowH);
                }
                modernChromeTitleBar.BringToFront();
            }

            // 2. 正面署名 (主标题已融合至顶部一体化大标题栏，保留 lblLogo 隐藏尺寸契约避免重叠测试破坏)
            int topY = S(12);
            int logoH = S(30);
            if (lblLogo != null)
            {
                SetAboutFont(lblLogo, 14.5F, FontStyle.Bold);
                lblLogo.Location = new Point(padX, topY);
                lblLogo.Visible = false;
                logoH = Math.Max(S(24), TextRenderer.MeasureText(lblLogo.Text, lblLogo.Font).Height);
            }

            int heroBottom = (modernChromeTitleBar != null) ? modernChromeTitleBar.Bottom : S(52);
            int authorY = Math.Max(heroBottom + S(2), topY + logoH + S(4));
            int authorH = S(20);
            if (lblAuthorPrefix != null)
            {
                SetAboutFont(lblAuthorPrefix, 9.2F, FontStyle.Regular);
                lblAuthorPrefix.Location = new Point(padX, authorY);
                Size prefixSize = TextRenderer.MeasureText(lblAuthorPrefix.Text, lblAuthorPrefix.Font);
                authorH = Math.Max(S(18), prefixSize.Height);
                if (lblAuthorName != null)
                {
                    SetAboutFont(lblAuthorName, 9.2F, FontStyle.Bold);
                    lblAuthorName.Location = new Point(padX + prefixSize.Width - S(6), authorY);
                    Size nameSize = TextRenderer.MeasureText(AuthorName, lblAuthorName.Font);
                    if (lblAuthorSuffix != null)
                    {
                        SetAboutFont(lblAuthorSuffix, 9.2F, FontStyle.Regular);
                        lblAuthorSuffix.Location = new Point(lblAuthorName.Left + nameSize.Width - S(4), authorY);
                        lblAuthorName.SizeChanged += delegate
                        {
                            if (lblAuthorSuffix != null && !lblAuthorSuffix.IsDisposed)
                            {
                                lblAuthorSuffix.Location = new Point(lblAuthorName.Right - S(4), authorY);
                            }
                        };
                    }
                }
            }

            // Card 1: 软件在线更新卡片 (基于实测字高绝对平分上下内边距与行间间距，任何 DPI 均严格等距)
            int card1Y = Math.Max(S(92), authorY + authorH + S(14));
            int card1H = S(116);
            if (cardUpdate != null)
            {
                if (lblCardUpdateTitle != null) SetAboutFont(lblCardUpdateTitle, 9.5F, FontStyle.Bold);
                if (chkAutoUpdate != null) SetAboutFont(chkAutoUpdate, 8.8F, FontStyle.Regular);
                if (lblUpdateStatus != null) SetAboutFont(lblUpdateStatus, 8.5F, FontStyle.Regular);

                int h1 = (lblCardUpdateTitle != null) ? TextRenderer.MeasureText(lblCardUpdateTitle.Text, lblCardUpdateTitle.Font).Height : S(18);
                int h2 = (chkAutoUpdate != null) ? Math.Max(S(22), TextRenderer.MeasureText(chkAutoUpdate.Text, chkAutoUpdate.Font).Height + S(2)) : S(24);
                int h3 = (lblUpdateStatus != null) ? TextRenderer.MeasureText(string.IsNullOrEmpty(lblUpdateStatus.Text) ? "当前版本" : lblUpdateStatus.Text, lblUpdateStatus.Font).Height : S(18);
                int padV = S(14);
                int minNeededH = (padV * 2) + h1 + h2 + h3 + S(12);
                if (card1H < minNeededH) card1H = minNeededH;

                cardUpdate.Location = new Point(padX, card1Y);
                cardUpdate.Size = new Size(cardWidth, card1H);
                cardUpdate.CornerRadius = 8;

                int rowGap = Math.Max(S(4), (card1H - (padV * 2) - h1 - h2 - h3) / 2);
                int y1 = padV;
                int y2 = y1 + h1 + rowGap;
                int y3 = y2 + h2 + rowGap;

                if (lblCardUpdateTitle != null)
                {
                    lblCardUpdateTitle.Location = new Point(S(20), y1);
                }
                if (btnCheckUpdate != null)
                {
                    SetAboutFont(btnCheckUpdate, 9F, FontStyle.Bold);
                    btnCheckUpdate.Size = new Size(S(112), S(34));
                    btnCheckUpdate.Location = new Point(cardWidth - S(20) - btnCheckUpdate.Width, (card1H - btnCheckUpdate.Height) / 2);
                    btnCheckUpdate.CornerRadius = 6;
                }
                if (chkAutoUpdate != null)
                {
                    int chkMaxW = (btnCheckUpdate != null ? btnCheckUpdate.Left : cardWidth) - S(40);
                    chkAutoUpdate.Location = new Point(S(20), y2);
                    chkAutoUpdate.Size = new Size(chkMaxW, h2);
                }
                if (lblUpdateStatus != null)
                {
                    lblUpdateStatus.Location = new Point(S(20), y3);
                }
            }

            // Card 2: 纯粹赞赏支持卡片 (14px 卡间间距，按 668 总高自适应留足底部双行页脚呼吸边距)
            int containerH = S(668);
            int card2Y = card1Y + card1H + S(14);
            int footerReserve = S(84);
            int card2H = Math.Min(S(314), Math.Max(S(260), containerH - card2Y - footerReserve));
            if (cardDonate != null)
            {
                cardDonate.Location = new Point(padX, card2Y);
                cardDonate.Size = new Size(cardWidth, card2H);
                cardDonate.CornerRadius = 8;
            }

            if (lblDonateTitle != null)
            {
                SetAboutFont(lblDonateTitle, 9.8F, FontStyle.Bold);
                lblDonateTitle.Location = new Point(S(20), S(14));
            }
            if (lblDonateDesc != null)
            {
                SetAboutFont(lblDonateDesc, 8.8F, FontStyle.Regular);
                int donateTitleBottom = (lblDonateTitle != null) ? (S(14) + TextRenderer.MeasureText(lblDonateTitle.Text, lblDonateTitle.Font).Height) : S(34);
                lblDonateDesc.Location = new Point(S(20), Math.Max(S(40), donateTitleBottom + S(6)));
                lblDonateDesc.MaximumSize = new Size(cardWidth - S(40), 0);
                lblDonateDesc.AutoSize = true;
                lblDonateDesc.BackColor = Color.Transparent;
            }

            int descBottom = (lblDonateDesc != null && lblDonateDesc.Height > 0) ? (lblDonateDesc.Top + lblDonateDesc.Height) : S(60);
            int qrY = Math.Max(descBottom + S(12), S(74));
            int qrCardW = S(172);
            int qrCardH = Math.Min(S(210), Math.Max(S(184), card2H - qrY - S(16)));
            int qrGap = S(40);
            int qrTotalW = qrCardW * 2 + qrGap;
            int qrStartX = (cardWidth - qrTotalW) / 2;

            if (qrPanelWeChat != null)
            {
                qrPanelWeChat.Location = new Point(qrStartX, qrY);
                qrPanelWeChat.Size = new Size(qrCardW, qrCardH);
                qrPanelWeChat.BringToFront();
            }
            if (qrPanelAlipay != null)
            {
                qrPanelAlipay.Location = new Point(qrStartX + qrCardW + qrGap, qrY);
                qrPanelAlipay.Size = new Size(qrCardW, qrCardH);
                qrPanelAlipay.BringToFront();
            }

            // 3. 计算窗体与主容器总高度 (严格保持 600x668，赞赏卡外独立双行极简页脚)

            this.ClientSize = new Size(clientW, containerH);
            this.StartPosition = FormStartPosition.CenterScreen;

            if (flipContainer != null)
            {
                flipContainer.Location = new Point(1, 1);
                flipContainer.Size = new Size(Math.Max(40, clientW - 2), Math.Max(40, containerH - 2));
            }
            if (shatterCanvas != null)
            {
                shatterCanvas.Size = new Size(Math.Max(40, clientW - 2), Math.Max(40, containerH - 2));
            }
            if (pnlFront != null)
            {
                pnlFront.Size = new Size(Math.Max(40, clientW - 2), Math.Max(40, containerH - 2));
            }
            footerRow1Y = card2Y + card2H + S(24);
            LayoutAboutFooterRow();
            if (pnlBack != null)
            {
                pnlBack.Size = new Size(Math.Max(40, clientW - 2), Math.Max(40, containerH - 2));
            }
            if (cardQuote != null)
            {
                int backW = (pnlBack != null && pnlBack.Width > 40) ? pnlBack.Width : Math.Max(40, clientW - 2);
                int backH = (pnlBack != null && pnlBack.Height > 40) ? pnlBack.Height : Math.Max(40, containerH - 2);
                cardQuote.Location = new Point(0, 0);
                cardQuote.Size = new Size(backW, backH);
                cardQuote.CornerRadius = 0;

                if (btnFlipBack != null)
                {
                    btnFlipBack.Visible = false;
                }
            }
            if (modernChromeTitleBar != null)
            {
                bool showFrontChrome = (pnlBack == null || !pnlBack.Visible) && (shatterCanvas == null || !shatterCanvas.Visible);
                modernChromeTitleBar.Visible = showFrontChrome;
                if (showFrontChrome)
                {
                    modernChromeTitleBar.BringToFront();
                }
            }
        }

        protected override void OnApplyDpiScale(float scale)
        {
            if (scale > 0f) this.dpiScale = scale;
            ApplyAboutLayout();
            this.Invalidate(true);
        }

        private static void EnableDoubleBuffer(Control c)
        {
            if (c == null) return;
            try
            {
                typeof(Control).GetProperty("DoubleBuffered", BindingFlags.NonPublic | BindingFlags.Instance)
                    .SetValue(c, true, null);
            }
            catch { }
        }

        private void LayoutAboutFooterRow()
        {
            int surfaceW = (pnlFront != null && pnlFront.Width > 0) ? pnlFront.Width : (S(600) - 2);
            int row1Y = footerRow1Y > 0 ? footerRow1Y : S(578);

            if (lnkQQ != null && lnkGithub != null)
            {
                SetAboutFont(lnkQQ, 8.8F, FontStyle.Regular);
                SetAboutFont(lnkGithub, 8.8F, FontStyle.Regular);
                Size qqSz = TextRenderer.MeasureText(lnkQQ.Text, lnkQQ.Font);
                Size gitSz = TextRenderer.MeasureText(lnkGithub.Text, lnkGithub.Font);
                int gap = S(10);
                int dotW = S(8);
                if (lblFooterDot != null)
                {
                    SetAboutFont(lblFooterDot, 8.8F, FontStyle.Bold);
                    dotW = TextRenderer.MeasureText(lblFooterDot.Text, lblFooterDot.Font).Width;
                }
                int totalW = qqSz.Width + gap + dotW + gap + gitSz.Width;
                int startX = Math.Max(S(16), (surfaceW - totalW) / 2);

                lnkQQ.Location = new Point(startX, row1Y);
                if (lblFooterDot != null)
                {
                    lblFooterDot.Location = new Point(startX + qqSz.Width + gap, row1Y);
                    lblFooterDot.BringToFront();
                }
                lnkGithub.Location = new Point(startX + qqSz.Width + gap + dotW + gap, row1Y);
                lnkQQ.BringToFront();
                lnkGithub.BringToFront();
            }

            if (lnkResetApp != null || lnkClearCache != null)
            {
                // 第二行维护入口：清除缓存（绿）+ 深度重置（灰），两者并排居中
                Size rstSz = Size.Empty;
                Size cacheSz = Size.Empty;
                if (lnkResetApp != null)
                {
                    SetAboutFont(lnkResetApp, 8.2F, FontStyle.Regular);
                    rstSz = TextRenderer.MeasureText(lnkResetApp.Text, lnkResetApp.Font);
                }
                if (lnkClearCache != null)
                {
                    SetAboutFont(lnkClearCache, 8.2F, FontStyle.Regular);
                    cacheSz = TextRenderer.MeasureText(lnkClearCache.Text, lnkClearCache.Font);
                }
                int row2Y = row1Y + S(28);
                int linkGap = S(16);
                int combinedW = cacheSz.Width + rstSz.Width + ((cacheSz.Width > 0 && rstSz.Width > 0) ? linkGap : 0);
                int startX = Math.Max(S(12), (surfaceW - combinedW) / 2);

                if (lnkClearCache != null)
                {
                    lnkClearCache.Top = row2Y;
                    lnkClearCache.Left = startX;
                    lnkClearCache.BringToFront();
                }
                if (lnkResetApp != null)
                {
                    lnkResetApp.Top = row2Y;
                    lnkResetApp.Left = startX + cacheSz.Width + ((cacheSz.Width > 0) ? linkGap : 0);
                    lnkResetApp.BringToFront();
                }
            }
        }

        private static void CenterControlHorizontally(Control ctrl, int containerWidth)
        {
            if (ctrl == null) return;
            Size sz = TextRenderer.MeasureText(ctrl.Text, ctrl.Font);
            ctrl.Left = (containerWidth - sz.Width) / 2;
        }

        private void DrawArtisticQrCard(Graphics g, int w, int h, bool isWeChat, Image qrImage)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Rectangle rect = new Rectangle(0, 0, w - 1, h - 1);
            int cornerRadius = 8;
            Color channelAccent = isWeChat ? Color.FromArgb(0, 200, 83) : Color.FromArgb(22, 119, 255);
            Color textAccent = isWeChat ? Color.FromArgb(0, 230, 118) : Color.FromArgb(0, 176, 255);

            // 1. 暗黑微晶浮岛卡底
            using (GraphicsPath outerPath = RoundedCard.GetRoundedRectangle(rect, cornerRadius))
            {
                using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(20, 23, 30)))
                {
                    g.FillPath(bgBrush, outerPath);
                }

                using (Pen borderPen = new Pen(Color.FromArgb(90, channelAccent.R, channelAccent.G, channelAccent.B), 1.2f))
                {
                    g.DrawPath(borderPen, outerPath);
                }
            }

            // 2. 内部扫码视窗 (152x152, 6px 微圆角, 浅色高对比度基底确保 100% 极速识别)
            int innerW = S(152);
            int innerH = S(152);
            int innerX = (w - innerW) / 2;
            int innerY = S(10);
            Rectangle innerRect = new Rectangle(innerX, innerY, innerW, innerH);

            Color qrBgColor = isWeChat ? Color.FromArgb(243, 250, 246) : Color.FromArgb(243, 246, 252);

            using (GraphicsPath innerPath = RoundedCard.GetRoundedRectangle(innerRect, 6))
            {
                using (SolidBrush plateBrush = new SolidBrush(qrBgColor))
                {
                    g.FillPath(plateBrush, innerPath);
                }

                GraphicsState state = g.Save();
                g.SetClip(innerPath);
                if (qrImage != null)
                {
                    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                    Rectangle qrDrawRect = new Rectangle(innerRect.Left + 4, innerRect.Top + 4, innerRect.Width - 8, innerRect.Height - 8);
                    g.DrawImage(qrImage, qrDrawRect);
                }
                g.Restore(state);

                // 统一精致圆角护边，彻底根除直角角标造成的几何冲突与不对齐
                using (Pen plateBorderPen = new Pen(Color.FromArgb(65, channelAccent.R, channelAccent.G, channelAccent.B), 1.0f))
                {
                    g.DrawPath(plateBorderPen, innerPath);
                }
            }

            // 3. 下方通道引导提示文案 (垂直居中与水平中轴线双向居中，舒展克制)
            string titleText = isWeChat ? "微信赞赏码" : "支付宝赞赏码";

            using (Font titleFont = DpiScaleHelper.CreateScaledFont("Microsoft YaHei UI", 9.0F, FontStyle.Bold, EffectiveScale, 1.0f))
            using (SolidBrush titleBrush = new SolidBrush(textAccent))
            {
                StringFormat sf = new StringFormat();
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                Rectangle titleRect = new Rectangle(0, innerY + innerH, w, h - (innerY + innerH));
                g.DrawString(titleText, titleFont, titleBrush, titleRect, sf);
            }
        }

        #region 背面 60fps 持续动画：星尘拖尾、水波纹、5 层物理内聚 Bloom 柔光

        private void InitAmbientAnimations()
        {
            // 初始化星芒微光背景粒子
            for (int i = 0; i < 28; i++)
            {
                AmbientParticle p = new AmbientParticle();
                p.X = rnd.Next(20, 532);
                p.Y = rnd.Next(20, 560);
                p.Vx = (float)((rnd.NextDouble() - 0.5) * 0.3);
                p.Vy = (float)(-rnd.NextDouble() * 0.45 - 0.1);
                p.Alpha = (float)(rnd.NextDouble() * 0.4 + 0.2);
                p.Size = (float)(rnd.NextDouble() * 3.0 + 1.5);
                p.Phase = (float)(rnd.NextDouble() * Math.PI * 2.0);
                Color[] cols = new Color[] { Color.FromArgb(200, 220, 245), Color.FromArgb(0, 230, 118), Color.FromArgb(100, 210, 255), Color.FromArgb(255, 255, 255) };
                p.Color = cols[rnd.Next(cols.Length)];
                backParticles.Add(p);
            }

            // 全局 60fps 动画主驱动定时器
            animationTimer = new Timer();
            animationTimer.Interval = 16;
            animationTimer.Tick += delegate(object s, EventArgs e)
            {
                // 1. 相位、丝绸光波二阶阻尼、按钮悬停插值与点击柔雾平滑演化
                chromaHue += 0.9f;
                if (chromaHue >= 360f) chromaHue -= 360f;
                pulsePhase += 0.024f;
                if (pulsePhase > 10000f) pulsePhase -= 10000f;

                float restPluck = hasPointerInBack ? targetWavePluckX : 0.5f;
                wavePluckX += (restPluck - wavePluckX) * 0.045f;
                waveEnergy += (targetWaveEnergy - waveEnergy) * 0.055f;
                targetWaveEnergy *= 0.972f;
                if (targetWaveEnergy < 0.002f) targetWaveEnergy = 0f;
                if (waveEnergy < 0.001f && targetWaveEnergy == 0f) waveEnergy = 0f;

                float targetBtnHover = isBackBtnHovered ? 1f : 0f;
                backBtnHoverProgress += (targetBtnHover - backBtnHoverProgress) * 0.16f;

                if (clickBloomAlpha > 0.005f)
                {
                    clickBloomRadius += 1.4f * EffectiveScale;
                    clickBloomAlpha *= 0.945f;
                }
                else
                {
                    clickBloomAlpha = 0f;
                }

                float cardW = (cardQuote != null && cardQuote.Width > 0) ? cardQuote.Width : 598f;
                float cardH = (cardQuote != null && cardQuote.Height > 0) ? cardQuote.Height : 666f;
                float goalX = hasPointerInBack ? targetMouseX : (cardW * 0.5f + (float)Math.Cos(pulsePhase * 0.42f) * cardW * 0.28f);
                float goalY = hasPointerInBack ? targetMouseY : (cardH * 0.38f + (float)Math.Sin(pulsePhase * 0.31f) * cardH * 0.20f);
                smoothMouseX += (goalX - smoothMouseX) * 0.08f;
                smoothMouseY += (goalY - smoothMouseY) * 0.08f;

                // 2. 背面浮动星芒粒子微动与微光呼吸
                for (int i = 0; i < backParticles.Count; i++)
                {
                    AmbientParticle p = backParticles[i];
                    p.X += p.Vx;
                    p.Y += p.Vy;
                    p.Phase += 0.035f;
                    p.Alpha = 0.25f + (float)Math.Sin(p.Phase) * 0.25f;

                    if (p.Y < 10)
                    {
                        p.Y = 560;
                        p.X = rnd.Next(20, 532);
                    }
                }

                // 3. 兼容旧水波列表清理
                for (int i = waterRipples.Count - 1; i >= 0; i--)
                {
                    WaterRipple wr = waterRipples[i];
                    wr.Radius += wr.Speed;
                    wr.Speed = Math.Max(0.7f, wr.Speed * 0.985f);
                    wr.Age++;
                    wr.Alpha = (float)Math.Pow(Math.Max(0f, 1.0f - (wr.Radius / wr.MaxRadius)), 1.35);
                    if (wr.Alpha <= 0.005f || wr.Radius >= wr.MaxRadius)
                    {
                        waterRipples.RemoveAt(i);
                    }
                }

                bool hasFrontRipples = false;
                for (int i = frontRipples.Count - 1; i >= 0; i--)
                {
                    WaterRipple fr = frontRipples[i];
                    fr.Radius += fr.Speed;
                    fr.Speed = Math.Max(0.7f, fr.Speed * 0.985f);
                    fr.Age++;
                    fr.Alpha = (float)Math.Pow(Math.Max(0f, 1.0f - (fr.Radius / fr.MaxRadius)), 1.35);
                    if (fr.Alpha <= 0.005f || fr.Radius >= fr.MaxRadius)
                    {
                        frontRipples.RemoveAt(i);
                    }
                    else
                    {
                        hasFrontRipples = true;
                    }
                }

                // 5. 正面点击微光微芒演进 (慢速上升微晕)
                bool hasFrontParticles = false;
                for (int i = frontParticles.Count - 1; i >= 0; i--)
                {
                    AmbientParticle p = frontParticles[i];
                    p.X += p.Vx;
                    p.Y += p.Vy;
                    p.Alpha -= 0.010f;
                    if (p.Alpha <= 0f)
                    {
                        frontParticles.RemoveAt(i);
                    }
                    else
                    {
                        hasFrontParticles = true;
                    }
                }

                // 6. 正面浪漫爱心粒子演进 (缓慢上浮，微转微芒，存活约 3.0 秒)
                bool hasHeartParticles = false;
                for (int i = heartParticles.Count - 1; i >= 0; i--)
                {
                    HeartParticle p = heartParticles[i];
                    p.X += p.Vx;
                    p.Y += p.Vy;
                    p.Vy = (p.Vy - 0.010f) * 0.985f; // 轻柔上升浮力阻尼
                    p.Vx *= 0.985f;
                    p.Rotation += p.RotSpeed * 0.6f;
                    p.Alpha -= 0.0055f; // 存活时间大幅延长至约 3.0 秒 (180帧)
                    if (p.Alpha <= 0f)
                    {
                        heartParticles.RemoveAt(i);
                    }
                    else
                    {
                        hasHeartParticles = true;
                    }
                }

                // 7. 刷新界面呈现（含标题栏图层叠加同步刷新与消散末帧清屏）
                bool isFrontOverlayActive = hasFrontParticles || hasHeartParticles;
                if (pnlFront.Visible && (hasFrontParticles || hasFrontRipples || hasHeartParticles || wasFrontParticlesActive))
                {
                    pnlFront.Invalidate();
                    if (isFrontOverlayActive || wasFrontParticlesActive)
                    {
                        InvalidateFrontOverlayControls();
                    }
                }
                wasFrontParticlesActive = pnlFront.Visible && isFrontOverlayActive;

                if (pnlBack.Visible && cardQuote != null)
                {
                    cardQuote.Invalidate();
                }
            };
            animationTimer.Start();
        }

        public void SimulateStardustTrail(int x, int y)
        {
            CardQuote_MouseMove(null, new MouseEventArgs(MouseButtons.None, 0, x, y, 0));
        }

        private void CardQuote_MouseMove(object sender, MouseEventArgs e)
        {
            targetMouseX = e.X;
            targetMouseY = e.Y;
            hasPointerInBack = true;

            bool hovered = backBtnRect != RectangleF.Empty && backBtnRect.Contains(e.X, e.Y);
            if (hovered != isBackBtnHovered)
            {
                isBackBtnHovered = hovered;
                if (cardQuote != null)
                {
                    cardQuote.Cursor = hovered ? Cursors.Hand : Cursors.Default;
                }
            }

            // 鼠标轻拂下方丝绸脉冲光波时施加极柔缓低通阻尼能量（无任何生硬位移）
            if (!hovered && cardQuote != null && cardQuote.Height > 0)
            {
                float waveCenterY = 534f * EffectiveScale;
                if (Math.Abs(e.Y - waveCenterY) < 60f * EffectiveScale)
                {
                    targetWavePluckX = Math.Max(0.20f, Math.Min(0.80f, e.X / (float)Math.Max(1, cardQuote.Width)));
                    targetWaveEnergy = Math.Min(0.32f, targetWaveEnergy + 0.008f);
                }
            }
        }

        private void CardQuote_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                if (backBtnRect != RectangleF.Empty && backBtnRect.Contains(e.X, e.Y))
                {
                    isBackBtnPressed = true;
                    return;
                }

                // 无边框模式下顶部留白区域支持直接拖拽移动窗口
                if (e.Y <= 44f * EffectiveScale)
                {
                    OnDragMouseDown(sender, e);
                    return;
                }

                // 点击正片区域激发极轻柔无硬边体积柔雾晕散，并轻微拨动丝绸波纹
                targetMouseX = e.X;
                targetMouseY = e.Y;
                hasPointerInBack = true;
                float curW = (cardQuote != null && cardQuote.Width > 0) ? cardQuote.Width : 598f;
                targetWavePluckX = Math.Max(0.20f, Math.Min(0.80f, e.X / curW));
                targetWaveEnergy = Math.Min(0.45f, targetWaveEnergy + 0.16f);

                clickBloomX = e.X;
                clickBloomY = e.Y;
                clickBloomRadius = 18f * EffectiveScale;
                clickBloomAlpha = 1.0f;
            }
        }

        private void CardQuote_MouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && isBackBtnPressed)
            {
                isBackBtnPressed = false;
                if (backBtnRect != RectangleF.Empty && backBtnRect.Contains(e.X, e.Y))
                {
                    ChimeSynthesizer.StopAmbientMelody();
                    TriggerQuantumShatter(false);
                }
            }
        }

        private void InvalidateFrontOverlayControls()
        {
            if (modernChromeTitleBar != null && !modernChromeTitleBar.IsDisposed && modernChromeTitleBar.Visible)
            {
                modernChromeTitleBar.Invalidate();
            }
            if (modernChromeTitleLabel != null && !modernChromeTitleLabel.IsDisposed && modernChromeTitleLabel.Visible)
            {
                modernChromeTitleLabel.Invalidate();
            }
        }

        private void PnlFront_Paint(object sender, PaintEventArgs e)
        {
            DrawFrontParticlesOverlay(e.Graphics, 0f, 0f);
        }

        private void DrawFrontParticlesOverlay(Graphics g, float offsetX, float offsetY)
        {
            if (heartParticles.Count == 0 && frontParticles.Count == 0) return;

            GraphicsState savedState = g.Save();
            g.SmoothingMode = SmoothingMode.AntiAlias;
            if (offsetX != 0f || offsetY != 0f)
            {
                g.TranslateTransform(offsetX, offsetY);
            }

            // 绘制正面漂浮的浪漫爱心粒子 (Real Vector Hearts with Soft Neon Bloom)
            for (int i = 0; i < heartParticles.Count; i++)
            {
                HeartParticle hp = heartParticles[i];
                int a = Math.Max(0, Math.Min(255, (int)(hp.Alpha * 255)));
                if (a > 0)
                {
                    // 柔和外发光微晕 (Soft Ambient Glow)
                    int haloA = (int)(a * 0.32f);
                    if (haloA > 0)
                    {
                        using (SolidBrush haloBrush = new SolidBrush(Color.FromArgb(haloA, hp.Color)))
                        {
                            DrawHeart(g, haloBrush, hp.X, hp.Y, hp.Size * 1.45f, hp.Rotation);
                        }
                    }
                    // 核心高饱和心形矢量
                    using (SolidBrush hBrush = new SolidBrush(Color.FromArgb(a, hp.Color)))
                    {
                        DrawHeart(g, hBrush, hp.X, hp.Y, hp.Size, hp.Rotation);
                    }
                }
            }

            // 绘制正面漂浮的心跳微光粒子
            if (frontParticles.Count > 0)
            {
                for (int i = 0; i < frontParticles.Count; i++)
                {
                    AmbientParticle p = frontParticles[i];
                    int a = Math.Max(0, Math.Min(255, (int)(p.Alpha * 255)));
                    if (a > 0)
                    {
                        using (SolidBrush pb = new SolidBrush(Color.FromArgb(a, p.Color)))
                        {
                            g.FillEllipse(pb, p.X - p.Size / 2f, p.Y - p.Size / 2f, p.Size, p.Size);
                        }
                    }
                }
            }

            g.Restore(savedState);
        }

        private void CardQuote_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int w = cardQuote.Width;
            int h = cardQuote.Height;
            float es = EffectiveScale;

            // Layer 1: 无边框全画幅黑曜石深空基底 + 天顶柔翡翠微光 + 跟随光标镜面扫光
            Rectangle cardRect = new Rectangle(0, 0, Math.Max(1, w), Math.Max(1, h));
            using (LinearGradientBrush bgBrush = new LinearGradientBrush(cardRect, Color.FromArgb(11, 13, 19), Color.FromArgb(9, 11, 16), 90f))
            {
                ColorBlend bgBlend = new ColorBlend();
                bgBlend.Colors = new Color[] { Color.FromArgb(11, 13, 19), Color.FromArgb(15, 19, 28), Color.FromArgb(9, 11, 16) };
                bgBlend.Positions = new float[] { 0f, 0.48f, 1f };
                bgBrush.InterpolationColors = bgBlend;
                g.FillRectangle(bgBrush, cardRect);
            }

            float zenithR = Math.Max(140f, w * 0.58f);
            using (GraphicsPath zPath = new GraphicsPath())
            {
                zPath.AddEllipse(w / 2f - zenithR, -zenithR * 0.58f, zenithR * 2f, zenithR * 1.40f);
                using (PathGradientBrush zBrush = new PathGradientBrush(zPath))
                {
                    zBrush.CenterColor = Color.FromArgb(24, 0, 220, 135);
                    zBrush.SurroundColors = new Color[] { Color.FromArgb(0, 11, 13, 19) };
                    g.FillPath(zBrush, zPath);
                }
            }

            float sheenR = Math.Max(150f, w * 0.44f);
            using (GraphicsPath sPath = new GraphicsPath())
            {
                sPath.AddEllipse(smoothMouseX - sheenR, smoothMouseY - sheenR, sheenR * 2f, sheenR * 2f);
                using (PathGradientBrush sBrush = new PathGradientBrush(sPath))
                {
                    sBrush.CenterColor = Color.FromArgb(hasPointerInBack ? 24 : 14, 110, 235, 195);
                    sBrush.SurroundColors = new Color[] { Color.FromArgb(0, 11, 13, 19) };
                    g.FillPath(sBrush, sPath);
                }
            }

            // Layer 2: 视差深空微星粒子 (柔和纵深微光，无鼠标拖尾噪点)
            float paraX = -(smoothMouseX - w / 2f) * 0.015f;
            float paraY = -(smoothMouseY - h / 2f) * 0.012f;
            for (int i = 0; i < backParticles.Count; i++)
            {
                AmbientParticle p = backParticles[i];
                float px = (p.X / 550f) * w + paraX * (0.6f + (i % 3) * 0.35f);
                float py = (p.Y / 580f) * h + paraY * (0.6f + (i % 3) * 0.35f);
                int a = Math.Max(0, Math.Min(160, (int)(p.Alpha * 145)));
                if (a > 0)
                {
                    using (SolidBrush pb = new SolidBrush(Color.FromArgb(a, p.Color)))
                    {
                        g.FillEllipse(pb, px, py, p.Size, p.Size);
                    }
                }
            }

            // Layer 3: 点击轻柔无硬边体积雾光晕 (取代廉价线框圆环)
            if (clickBloomAlpha > 0.01f && clickBloomRadius > 4f)
            {
                int bloomA = Math.Max(0, Math.Min(28, (int)(clickBloomAlpha * 28f)));
                if (bloomA > 0)
                {
                    using (GraphicsPath bPath = new GraphicsPath())
                    {
                        bPath.AddEllipse(clickBloomX - clickBloomRadius, clickBloomY - clickBloomRadius, clickBloomRadius * 2f, clickBloomRadius * 2f);
                        using (PathGradientBrush bBrush = new PathGradientBrush(bPath))
                        {
                            bBrush.CenterColor = Color.FromArgb(bloomA, 80, 235, 185);
                            bBrush.SurroundColors = new Color[] { Color.FromArgb(0, 11, 13, 19) };
                            g.FillPath(bBrush, bPath);
                        }
                    }
                }
            }

            // Layer 4: 舒展剧场级排版 + 三层渐隐丝绸流体光波 + 画布一体化半透明返回胶囊 + 贴边钛金镜面流光框
            DrawSilkPulseWaveform(g, w, 534f * es, es);
            DrawPoeticManifesto(g, w, h);
            DrawBackPillButton(g, w, h, es);

            float rimX = hasPointerInBack ? smoothMouseX : (w * 0.5f + (float)Math.Cos(pulsePhase * 0.65f) * w * 0.48f);
            float rimY = hasPointerInBack ? smoothMouseY : (h * 0.5f + (float)Math.Sin(pulsePhase * 0.65f) * h * 0.48f);
            Rectangle borderRect = new Rectangle(2, 2, Math.Max(10, w - 5), Math.Max(10, h - 5));
            DrawTitaniumSpecularBorder(g, borderRect, Math.Max(6, (int)Math.Round(8 * es)), rimX, rimY, 1.0f);
        }

        #region 浪漫唯美矢量爱心渲染 (Romantic Vector Heart Graphics)

        private static void DrawHeart(Graphics g, Brush brush, float cx, float cy, float size, float rotation)
        {
            GraphicsState state = g.Save();
            g.TranslateTransform(cx, cy);
            if (Math.Abs(rotation) > 0.01f)
            {
                g.RotateTransform(rotation);
            }

            using (GraphicsPath heartPath = new GraphicsPath())
            {
                float r = size / 2f;
                heartPath.AddBezier(0, -r * 0.4f, -r * 0.7f, -r * 1.1f, -r * 1.2f, -r * 0.3f, 0, r * 1.0f);
                heartPath.AddBezier(0, r * 1.0f, r * 1.2f, -r * 0.3f, r * 0.7f, -r * 1.1f, 0, -r * 0.4f);
                heartPath.CloseFigure();
                g.FillPath(brush, heartPath);
            }

            g.Restore(state);
        }

        #endregion

        #region 真实物理水波纹拟真渲染 (Caustic Wave Packet Optics)

        private static void DrawRealisticCausticRipple(Graphics g, float cx, float cy, float radius, float alpha, float wavelength, int age)
        {
            if (alpha <= 0.001f || radius <= 0.5f) return;

            // 1. 中心落点水滴回弹微光 (Initial Droplet Flash & Cavity)
            if (age < 12 && radius < 24f)
            {
                float cavT = 1.0f - (age / 12f);
                int cavAlpha = Math.Max(0, Math.Min(255, (int)(alpha * cavT * 220)));
                if (cavAlpha > 0)
                {
                    float cavR = Math.Max(1.0f, (12 - age) * 0.45f);
                    using (SolidBrush cavBrush = new SolidBrush(Color.FromArgb(cavAlpha, 240, 255, 255)))
                    {
                        g.FillEllipse(cavBrush, cx - cavR, cy - cavR, cavR * 2, cavR * 2);
                    }
                }
            }

            // 2. 主波峰 (Primary Crest: 极细高光 + 柔和外晕) 与 波谷折射阴影 (Trough Shadow: 凹陷光线发散暗带)
            float rShadow = radius - 1.8f;
            if (rShadow > 1.5f)
            {
                int shadowA = Math.Max(0, Math.Min(255, (int)(alpha * 85)));
                using (Pen shadowPen = new Pen(Color.FromArgb(shadowA, 4, 6, 12), 2.8f))
                {
                    g.DrawEllipse(shadowPen, cx - rShadow, cy - rShadow, rShadow * 2, rShadow * 2);
                }
            }

            int bloomA = Math.Max(0, Math.Min(255, (int)(alpha * 120)));
            using (Pen bloomPen = new Pen(Color.FromArgb(bloomA, 0, 230, 140), 2.2f))
            {
                g.DrawEllipse(bloomPen, cx - radius, cy - radius, radius * 2, radius * 2);
            }

            int crestA = Math.Max(0, Math.Min(255, (int)(alpha * 240)));
            using (Pen crestPen = new Pen(Color.FromArgb(crestA, 245, 255, 255), 1.0f))
            {
                g.DrawEllipse(crestPen, cx - radius, cy - radius, radius * 2, radius * 2);
            }

            // 3. 次级微谐波涟漪 (Secondary Harmonic Echo)
            float rSub = radius - wavelength;
            if (rSub > 2.0f)
            {
                float subAmp = 0.55f;
                int subShadowA = Math.Max(0, Math.Min(255, (int)(alpha * subAmp * 60)));
                float rSubShadow = rSub - 1.4f;
                if (rSubShadow > 1.0f && subShadowA > 0)
                {
                    using (Pen subShadowPen = new Pen(Color.FromArgb(subShadowA, 4, 6, 12), 2.0f))
                    {
                        g.DrawEllipse(subShadowPen, cx - rSubShadow, cy - rSubShadow, rSubShadow * 2, rSubShadow * 2);
                    }
                }

                int subBloomA = Math.Max(0, Math.Min(255, (int)(alpha * subAmp * 90)));
                using (Pen subBloomPen = new Pen(Color.FromArgb(subBloomA, 80, 215, 255), 1.6f))
                {
                    g.DrawEllipse(subBloomPen, cx - rSub, cy - rSub, rSub * 2, rSub * 2);
                }

                int subCrestA = Math.Max(0, Math.Min(255, (int)(alpha * subAmp * 200)));
                using (Pen subCrestPen = new Pen(Color.FromArgb(subCrestA, 230, 250, 255), 0.9f))
                {
                    g.DrawEllipse(subCrestPen, cx - rSub, cy - rSub, rSub * 2, rSub * 2);
                }
            }

            // 4. 第三重微弱余波 (Tertiary Harmonic Ripple)
            float rSub2 = radius - wavelength * 1.85f;
            if (rSub2 > 2.0f)
            {
                float sub2Amp = 0.22f;
                int sub2CrestA = Math.Max(0, Math.Min(255, (int)(alpha * sub2Amp * 150)));
                if (sub2CrestA > 0)
                {
                    using (Pen sub2Pen = new Pen(Color.FromArgb(sub2CrestA, 180, 235, 255), 0.75f))
                    {
                        g.DrawEllipse(sub2Pen, cx - rSub2, cy - rSub2, rSub2 * 2, rSub2 * 2);
                    }
                }
            }
        }

        private void SpawnRealisticWaterRipple(System.Collections.Generic.List<WaterRipple> list, float x, float y, float maxRadius, float initialSpeed)
        {
            WaterRipple wr = new WaterRipple();
            wr.X = x;
            wr.Y = y;
            wr.Radius = 3f;
            wr.MaxRadius = maxRadius;
            wr.Alpha = 1.0f;
            wr.Speed = initialSpeed;
            wr.Wavelength = 11.5f;
            wr.Age = 0;
            list.Add(wr);
        }

        #endregion

        #region 艺术字形与文字逐行外发光漫生渲染 (Artistic Glowing Typography)

        private static Font GetArtisticFont(float size, FontStyle style)
        {
            string[] preferred = new string[] { "Microsoft YaHei UI", "Segoe UI", "PingFang SC" };
            for (int i = 0; i < preferred.Length; i++)
            {
                try
                {
                    Font f = new Font(preferred[i], size, style);
                    if (string.Equals(f.Name, preferred[i], StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(f.OriginalFontName, preferred[i], StringComparison.OrdinalIgnoreCase))
                    {
                        return f;
                    }
                    f.Dispose();
                }
                catch { }
            }
            return new Font("Microsoft YaHei UI", size, style);
        }

        private static Font GetFittedArtisticFont(Graphics g, string text, float targetSize, float maxAllowedWidth, FontStyle style)
        {
            float sz = targetSize;
            while (sz >= 7.5f)
            {
                Font f = GetArtisticFont(sz, style);
                SizeF measured = g.MeasureString(text, f);
                if (measured.Width <= maxAllowedWidth)
                {
                    return f;
                }
                f.Dispose();
                sz -= 0.3f;
            }
            return GetArtisticFont(sz, style);
        }

        private static void DrawGlowString(Graphics g, string text, Font font, Color textColor, Color glowColor, float x, float y, StringFormat sf, int alpha, float floatOffset)
        {
            if (alpha <= 0) return;
            int safeAlpha = Math.Max(0, Math.Min(255, alpha));
            float drawY = y - floatOffset;

            // 柔和微光漫射层 (4 向极细柔晕，杜绝粗糙假粗体与重影)
            int glowAlpha = Math.Max(0, Math.Min(255, (int)(safeAlpha * 0.18f)));
            if (glowAlpha > 0 && glowColor.A > 0)
            {
                using (SolidBrush glowBrush = new SolidBrush(Color.FromArgb(glowAlpha, glowColor)))
                {
                    g.DrawString(text, font, glowBrush, x - 1f, drawY, sf);
                    g.DrawString(text, font, glowBrush, x + 1f, drawY, sf);
                    g.DrawString(text, font, glowBrush, x, drawY - 1f, sf);
                    g.DrawString(text, font, glowBrush, x, drawY + 1f, sf);
                }
            }

            // 核心原文字形 (Core Artistic Text Layer)
            using (SolidBrush textBrush = new SolidBrush(Color.FromArgb(safeAlpha, textColor)))
            {
                g.DrawString(text, font, textBrush, x, drawY, sf);
            }
        }

        private void DrawPoeticManifesto(Graphics g, int w, int h)
        {
            float elapsedSec = 999f;
            if (backOpenTime != DateTime.MinValue)
            {
                elapsedSec = (float)(DateTime.UtcNow - backOpenTime).TotalSeconds;
            }
            DrawPoeticManifestoWithAlpha(g, w, h, 0, 0, 255, elapsedSec);
        }

        private void DrawPoeticManifestoWithAlpha(Graphics g, int w, int h, int offsetX, int offsetY, int alpha, float elapsedSec)
        {
            if (alpha <= 0) return;
            alpha = Math.Max(0, Math.Min(255, alpha));

            float tTitle = Math.Max(0f, Math.Min(1f, elapsedSec / 0.45f));
            float tL1 = Math.Max(0f, Math.Min(1f, (elapsedSec - 0.25f) / 0.50f));
            float tL2 = Math.Max(0f, Math.Min(1f, (elapsedSec - 0.65f) / 0.50f));
            float tL3 = Math.Max(0f, Math.Min(1f, (elapsedSec - 1.05f) / 0.50f));
            float tSign = Math.Max(0f, Math.Min(1f, (elapsedSec - 1.45f) / 0.50f));

            float cx = offsetX + w / 2f;
            float es = EffectiveScale;
            float maxAllowedTextW = w - 96f * es;

            using (StringFormat sfCenter = new StringFormat())
            {
                sfCenter.Alignment = StringAlignment.Center;
                sfCenter.LineAlignment = StringAlignment.Center;
                sfCenter.FormatFlags |= StringFormatFlags.NoWrap;

                // 1. 现代排版主标题《写 在 代 码 之 外》（剔除多余英文 AI 眉标，留足天顶留白）
                int aTitle = (int)(alpha * (tTitle * tTitle * (3f - 2f * tTitle)));
                if (aTitle > 0)
                {
                    float floatTitle = (1f - tTitle) * 6f;
                    using (Font titleFont = GetFittedArtisticFont(g, "写 在 代 码 之 外", DpiScaleHelper.GetScaledFontSize(16.2F, es, 1.0f), maxAllowedTextW, FontStyle.Regular))
                    {
                        DrawGlowString(g, "写 在 代 码 之 外", titleFont, Color.FromArgb(aTitle, 244, 247, 255), Color.FromArgb(Math.Min(aTitle, 95), 110, 242, 176), cx, offsetY + 122f * es, sfCenter, aTitle, floatTitle);
                    }

                    // 极细 76px 银翠渐隐分割线 + 中心微钻
                    int divY = (int)(offsetY + 162 * es);
                    int divW = Math.Max(40, (int)(76 * es));
                    int divX = (int)(offsetX + (w - divW) / 2);
                    int aLine = Math.Min(aTitle, 110);
                    using (LinearGradientBrush lineBrush = new LinearGradientBrush(
                        new Point(divX, divY), new Point(divX + divW, divY),
                        Color.FromArgb(0, 110, 242, 176), Color.FromArgb(aLine, 110, 242, 176)))
                    {
                        ColorBlend cb = new ColorBlend();
                        cb.Colors = new Color[] { Color.FromArgb(0, 110, 242, 176), Color.FromArgb(aLine, 110, 242, 176), Color.FromArgb(0, 110, 242, 176) };
                        cb.Positions = new float[] { 0f, 0.5f, 1f };
                        lineBrush.InterpolationColors = cb;

                        using (Pen linePen = new Pen(lineBrush, 1.0f))
                        {
                            g.DrawLine(linePen, divX, divY, divX + divW, divY);
                        }
                    }
                    int aDot = Math.Min(aTitle, 190);
                    using (SolidBrush dotBrush = new SolidBrush(Color.FromArgb(aDot, 232, 248, 240)))
                    {
                        PointF[] diamond = new PointF[]
                        {
                            new PointF(offsetX + w / 2f, divY - 2.2f),
                            new PointF(offsetX + w / 2f + 2.2f, divY),
                            new PointF(offsetX + w / 2f, divY + 2.2f),
                            new PointF(offsetX + w / 2f - 2.2f, divY)
                        };
                        g.FillPolygon(dotBrush, diamond);
                    }
                }

                // 2. 诗篇正文 (舒展呼吸行距与段落留白)
                int aL1 = (int)(alpha * (tL1 * tL1 * (3f - 2f * tL1)));
                if (aL1 > 0)
                {
                    float floatL1 = (1f - tL1) * 5f;
                    using (Font f1 = GetFittedArtisticFont(g, "拂去所有冗余与喧嚣，", DpiScaleHelper.GetScaledFontSize(11.8F, es, 1.0f), maxAllowedTextW, FontStyle.Regular))
                    {
                        DrawGlowString(g, "拂去所有冗余与喧嚣，", f1, Color.FromArgb(aL1, 220, 230, 246), Color.FromArgb(Math.Min(aL1, 65), 100, 210, 255), cx, offsetY + 218f * es, sfCenter, aL1, floatL1);
                        DrawGlowString(g, "只将最纯净的秩序赠予你。", f1, Color.FromArgb(aL1, 220, 230, 246), Color.FromArgb(Math.Min(aL1, 65), 100, 210, 255), cx, offsetY + 250f * es, sfCenter, aL1, floatL1);
                    }
                }

                int aL2 = (int)(alpha * (tL2 * tL2 * (3f - 2f * tL2)));
                if (aL2 > 0)
                {
                    float floatL2 = (1f - tL2) * 5f;
                    using (Font f2 = GetFittedArtisticFont(g, "灵脉初醒，微光致意。", DpiScaleHelper.GetScaledFontSize(14.2F, es, 1.0f), maxAllowedTextW, FontStyle.Bold))
                    {
                        DrawGlowString(g, "灵脉初醒，微光致意。", f2, Color.FromArgb(aL2, 110, 242, 176), Color.FromArgb(Math.Min(aL2, 120), 0, 230, 118), cx, offsetY + 314f * es, sfCenter, aL2, floatL2);
                    }
                }

                int aL3 = (int)(alpha * (tL3 * tL3 * (3f - 2f * tL3)));
                if (aL3 > 0)
                {
                    float floatL3 = (1f - tL3) * 5f;
                    using (Font f3 = GetFittedArtisticFont(g, "愿你在属于自己的战场上，", DpiScaleHelper.GetScaledFontSize(11.8F, es, 1.0f), maxAllowedTextW, FontStyle.Regular))
                    {
                        DrawGlowString(g, "愿你在属于自己的战场上，", f3, Color.FromArgb(aL3, 220, 230, 246), Color.FromArgb(Math.Min(aL3, 65), 140, 210, 255), cx, offsetY + 378f * es, sfCenter, aL3, floatL3);
                        DrawGlowString(g, "永远专注、自由、无可阻挡。", f3, Color.FromArgb(aL3, 220, 230, 246), Color.FromArgb(Math.Min(aL3, 65), 140, 210, 255), cx, offsetY + 410f * es, sfCenter, aL3, floatL3);
                    }
                }

                // 3. 诗篇右下角经典落款署名
                int aSign = (int)(alpha * (tSign * tSign * (3f - 2f * tSign)));
                if (aSign > 0)
                {
                    float floatSign = (1f - tSign) * 4f;
                    using (StringFormat sfRight = new StringFormat())
                    {
                        sfRight.Alignment = StringAlignment.Far;
                        sfRight.LineAlignment = StringAlignment.Center;
                        sfRight.FormatFlags |= StringFormatFlags.NoWrap;
                        using (Font fSign = new Font("Georgia", Math.Max(9.0f, DpiScaleHelper.GetScaledFontSize(11.2F, es, 1.0f)), FontStyle.Italic))
                        {
                            DrawGlowString(g, "—  Ferris-echo", fSign, Color.FromArgb(aSign, 223, 194, 120), Color.FromArgb(Math.Min(aSign, 80), 255, 225, 145), offsetX + w - 92f * es, offsetY + 468f * es, sfRight, aSign, floatSign);
                        }
                    }
                }
            }
        }

        private void DrawBackPillButton(Graphics g, int w, int h, float es)
        {
            float btnW = 114f * es;
            float btnH = 34f * es;
            float btnX = (w - btnW) / 2f;
            float btnY = h - 62f * es;
            backBtnRect = new RectangleF(btnX, btnY, btnW, btnH);

            float t = Math.Max(0f, Math.Min(1f, backBtnHoverProgress));
            int fillA = isBackBtnPressed ? 190 : (int)Math.Round(110 + (165 - 110) * t);
            int fillR = isBackBtnPressed ? 18 : (int)Math.Round(22 + (26 - 22) * t);
            int fillG = isBackBtnPressed ? 28 : (int)Math.Round(27 + (40 - 27) * t);
            int fillB = isBackBtnPressed ? 36 : (int)Math.Round(38 + (50 - 38) * t);

            int bdrA = isBackBtnPressed ? 155 : (int)Math.Round(68 + (135 - 68) * t);
            int bdrR = (int)Math.Round(120 + (110 - 120) * t);
            int bdrG = (int)Math.Round(140 + (242 - 140) * t);
            int bdrB = (int)Math.Round(175 + (176 - 175) * t);

            int txtR = (int)Math.Round(172 + (236 - 172) * t);
            int txtG = (int)Math.Round(186 + (248 - 186) * t);
            int txtB = (int)Math.Round(212 + (242 - 212) * t);

            Rectangle pillRect = Rectangle.Round(backBtnRect);
            int radius = Math.Max(4, (int)Math.Round(pillRect.Height / 2f));
            using (GraphicsPath pillPath = RoundedCard.GetRoundedRectangle(pillRect, radius))
            {
                using (SolidBrush fillBrush = new SolidBrush(Color.FromArgb(fillA, fillR, fillG, fillB)))
                {
                    g.FillPath(fillBrush, pillPath);
                }
                using (Pen borderPen = new Pen(Color.FromArgb(bdrA, bdrR, bdrG, bdrB), 1.0f))
                {
                    g.DrawPath(borderPen, pillPath);
                }
            }

            Font pillFont = DpiScaleHelper.GetCachedScaledFont("Microsoft YaHei UI", 8.8F, FontStyle.Bold, es, 1.0f);
            using (SolidBrush txtBrush = new SolidBrush(Color.FromArgb(255, txtR, txtG, txtB)))
            using (StringFormat sf = new StringFormat())
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString("返  回", pillFont, txtBrush, backBtnRect, sf);
            }
        }

        private void DrawSilkPulseWaveform(Graphics g, int w, float baseY, float es)
        {
            if (w < 8) return;
            int samples = 64;
            float span = (float)w;

            float[] ampFactors = new float[] { 1.0f, 0.72f, 0.48f };
            float[] freqFactors = new float[] { 1.0f, 1.42f, 1.92f };
            float[] speedFactors = new float[] { 1.0f, -0.76f, 1.22f };
            Color[] colors = new Color[]
            {
                Color.FromArgb(0, 230, 118),
                Color.FromArgb(76, 216, 255),
                Color.FromArgb(223, 194, 120)
            };

            float gradH = Math.Max(40f, 44f * es);
            RectangleF gradRect = new RectangleF(0f, baseY - gradH / 2f, span, gradH);

            for (int layer = 0; layer < 3; layer++)
            {
                PointF[] pts = new PointF[samples];
                float baseAmp = (4.5f + waveEnergy * 6.0f) * ampFactors[layer] * es;
                for (int i = 0; i < samples; i++)
                {
                    float u = i / (float)(samples - 1);
                    float x = u * span;
                    float edgeWin = (float)Math.Exp(-Math.Pow((u - 0.5) / 0.36, 2.0));
                    float pluckDist = (u - wavePluckX) / 0.36f;
                    float pluckEnv = 0.86f + 0.26f * (float)Math.Exp(-pluckDist * pluckDist);
                    float carrier = (float)Math.Sin(u * Math.PI * 2.5 * freqFactors[layer] + pulsePhase * speedFactors[layer]);
                    float harmonic = (float)Math.Sin(u * Math.PI * 4.4 * freqFactors[layer] - pulsePhase * 1.15f * speedFactors[layer]) * 0.28f;
                    float y = baseY + (carrier + harmonic) * baseAmp * edgeWin * pluckEnv;
                    pts[i] = new PointF(x, y);
                }

                Color c = colors[layer];
                int bloomA = Math.Max(0, Math.Min(75, (int)((layer == 0 ? 28 : 18) + waveEnergy * 24f)));
                int coreA = Math.Max(0, Math.Min(195, (int)((layer == 0 ? 128 : (layer == 1 ? 96 : 68)) + waveEnergy * 42f)));

                Color bloomZero = Color.FromArgb(0, c);
                Color bloomFull = Color.FromArgb(bloomA, c);
                using (LinearGradientBrush bloomBrush = new LinearGradientBrush(gradRect, bloomZero, bloomFull, 0f))
                {
                    ColorBlend cbBloom = new ColorBlend();
                    cbBloom.Colors = new Color[] { bloomZero, bloomFull, bloomFull, bloomZero };
                    cbBloom.Positions = new float[] { 0f, 0.18f, 0.82f, 1f };
                    bloomBrush.InterpolationColors = cbBloom;
                    using (Pen bloomPen = new Pen(bloomBrush, Math.Max(2.2f, (layer == 0 ? 4.4f : 3.0f) * es)))
                    {
                        bloomPen.LineJoin = LineJoin.Round;
                        g.DrawCurve(bloomPen, pts, 0.5f);
                    }
                }

                Color coreZero = Color.FromArgb(0, c);
                Color coreFull = Color.FromArgb(coreA, c);
                using (LinearGradientBrush coreBrush = new LinearGradientBrush(gradRect, coreZero, coreFull, 0f))
                {
                    ColorBlend cbCore = new ColorBlend();
                    cbCore.Colors = new Color[] { coreZero, coreFull, coreFull, coreZero };
                    cbCore.Positions = new float[] { 0f, 0.18f, 0.82f, 1f };
                    coreBrush.InterpolationColors = cbCore;
                    using (Pen corePen = new Pen(coreBrush, Math.Max(1.0f, (layer == 0 ? 1.45f : 1.05f) * es)))
                    {
                        corePen.LineJoin = LineJoin.Round;
                        g.DrawCurve(corePen, pts, 0.5f);
                    }
                }
            }
        }

        private void DrawTitaniumSpecularBorder(Graphics g, Rectangle bounds, int radius, float lightX, float lightY, float alphaMultiplier)
        {
            int safeA = Math.Max(0, Math.Min(255, (int)(255 * alphaMultiplier)));
            if (safeA <= 0 || bounds.Width < 10 || bounds.Height < 10) return;

            using (GraphicsPath path = RoundedCard.GetRoundedRectangle(bounds, radius))
            {
                // 1. 精密拉丝钛金基础四边外框
                int topA = Math.Max(0, Math.Min(255, (int)(205 * alphaMultiplier)));
                int midA = Math.Max(0, Math.Min(255, (int)(170 * alphaMultiplier)));
                int botA = Math.Max(0, Math.Min(255, (int)(185 * alphaMultiplier)));
                using (LinearGradientBrush baseBrush = new LinearGradientBrush(bounds, Color.FromArgb(topA, 128, 148, 178), Color.FromArgb(botA, 96, 112, 140), 90f))
                {
                    ColorBlend baseBlend = new ColorBlend();
                    baseBlend.Colors = new Color[]
                    {
                        Color.FromArgb(topA, 128, 148, 178),
                        Color.FromArgb(midA, 78, 92, 118),
                        Color.FromArgb(botA, 96, 112, 140)
                    };
                    baseBlend.Positions = new float[] { 0f, 0.5f, 1f };
                    baseBrush.InterpolationColors = baseBlend;
                    using (Pen basePen = new Pen(baseBrush, 1.2f))
                    {
                        basePen.LineJoin = LineJoin.Round;
                        g.DrawPath(basePen, path);
                    }
                }

                // 2. 径向聚焦液态金属主掠光晕 + 高光脊线 (PathGradientBrush 真二维边缘高光)
                float specR = Math.Max(40f, Math.Max(bounds.Width, bounds.Height) * 0.52f);
                using (GraphicsPath specPath = new GraphicsPath())
                {
                    specPath.AddEllipse(lightX - specR, lightY - specR, specR * 2f, specR * 2f);

                    int auraCenterA = Math.Max(0, Math.Min(255, (int)(95 * alphaMultiplier)));
                    int auraMidA = Math.Max(0, Math.Min(255, (int)(35 * alphaMultiplier)));
                    using (PathGradientBrush auraBrush = new PathGradientBrush(specPath))
                    {
                        auraBrush.CenterPoint = new PointF(lightX, lightY);
                        ColorBlend cbAura = new ColorBlend();
                        cbAura.Colors = new Color[]
                        {
                            Color.FromArgb(0, 76, 210, 245),
                            Color.FromArgb(auraMidA, 76, 210, 245),
                            Color.FromArgb(auraCenterA, 0, 235, 148)
                        };
                        cbAura.Positions = new float[] { 0f, 0.55f, 1f };
                        auraBrush.InterpolationColors = cbAura;
                        using (Pen auraPen = new Pen(auraBrush, 4.0f))
                        {
                            auraPen.LineJoin = LineJoin.Round;
                            g.DrawPath(auraPen, path);
                        }
                    }

                    int crestCenterA = Math.Max(0, Math.Min(255, (int)(245 * alphaMultiplier)));
                    int crestMidA = Math.Max(0, Math.Min(255, (int)(125 * alphaMultiplier)));
                    using (PathGradientBrush crestBrush = new PathGradientBrush(specPath))
                    {
                        crestBrush.CenterPoint = new PointF(lightX, lightY);
                        ColorBlend cbCrest = new ColorBlend();
                        cbCrest.Colors = new Color[]
                        {
                            Color.FromArgb(0, 100, 228, 195),
                            Color.FromArgb(crestMidA, 100, 228, 195),
                            Color.FromArgb(crestCenterA, 224, 255, 242)
                        };
                        cbCrest.Positions = new float[] { 0f, 0.55f, 1f };
                        crestBrush.InterpolationColors = cbCrest;
                        using (Pen crestPen = new Pen(crestBrush, 1.6f))
                        {
                            crestPen.LineJoin = LineJoin.Round;
                            g.DrawPath(crestPen, path);
                        }
                    }
                }

                // 3. 对角冰银环境反光呼应
                float oppX = bounds.Right - (lightX - bounds.Left);
                float oppY = bounds.Bottom - (lightY - bounds.Top);
                float oppR = specR * 0.72f;
                using (GraphicsPath oppPath = new GraphicsPath())
                {
                    oppPath.AddEllipse(oppX - oppR, oppY - oppR, oppR * 2f, oppR * 2f);
                    int oppCenterA = Math.Max(0, Math.Min(255, (int)(115 * alphaMultiplier)));
                    using (PathGradientBrush oppBrush = new PathGradientBrush(oppPath))
                    {
                        oppBrush.CenterPoint = new PointF(oppX, oppY);
                        oppBrush.CenterColor = Color.FromArgb(oppCenterA, 165, 212, 255);
                        oppBrush.SurroundColors = new Color[] { Color.FromArgb(0, 165, 212, 255) };
                        using (Pen oppPen = new Pen(oppBrush, 1.3f))
                        {
                            oppPen.LineJoin = LineJoin.Round;
                            g.DrawPath(oppPen, path);
                        }
                    }
                }
            }

            // 4. 极细内嵌玻璃倒角微光 (1px inset)
            Rectangle innerR = bounds;
            innerR.Inflate(-1, -1);
            if (innerR.Width > 8 && innerR.Height > 8)
            {
                int innerRad = Math.Max(2, radius - 1);
                int bevelA = Math.Max(0, Math.Min(255, (int)(26 * alphaMultiplier)));
                using (GraphicsPath innerPath = RoundedCard.GetRoundedRectangle(innerR, innerRad))
                using (Pen bevelPen = new Pen(Color.FromArgb(bevelA, 255, 255, 255), 1.0f))
                {
                    g.DrawPath(bevelPen, innerPath);
                }
            }
        }

        #endregion

        private void DrawInwardBloomBorder(Graphics g, Rectangle bounds, int radius, float baseHue)
        {
            DrawInwardBloomBorderWithAlpha(g, bounds, radius, baseHue, 1.0f);
        }

        private void DrawInwardBloomBorderWithAlpha(Graphics g, Rectangle bounds, int radius, float baseHue, float alphaMultiplier)
        {
            if (alphaMultiplier <= 0f) return;

            // 真实物理级 5 层内聚羽化 Bloom 柔光（向内自然浸润扩散，饱满柔和，绝无生硬细条）
            Color cTop = ColorFromHsv(baseHue, 0.90f, 1.0f);
            Color cRight = ColorFromHsv((baseHue + 90f) % 360f, 0.90f, 1.0f);
            Color cBottom = ColorFromHsv((baseHue + 180f) % 360f, 0.90f, 1.0f);
            Color cLeft = ColorFromHsv((baseHue + 270f) % 360f, 0.90f, 1.0f);

            // Layer 5: 最内层深邃弥散柔晕 (Inset: 10px, Stroke: 10px, Alpha: 22)
            DrawInwardGlowLayer(g, bounds, radius, 10, 10f, (int)(22 * alphaMultiplier), cTop, cRight, cBottom, cLeft);

            // Layer 4: 中层漫反射光晕 (Inset: 6px, Stroke: 6px, Alpha: 48)
            DrawInwardGlowLayer(g, bounds, radius, 6, 6f, (int)(48 * alphaMultiplier), cTop, cRight, cBottom, cLeft);

            // Layer 3: 聚合能量柔光 (Inset: 3px, Stroke: 4px, Alpha: 90)
            DrawInwardGlowLayer(g, bounds, radius, 3, 4f, (int)(90 * alphaMultiplier), cTop, cRight, cBottom, cLeft);

            // Layer 2: 霓虹主光柱 (Inset: 1px, Stroke: 2.2f, Alpha: 185)
            DrawInwardGlowLayer(g, bounds, radius, 1, 2.2f, (int)(185 * alphaMultiplier), cTop, cRight, cBottom, cLeft);

            // Layer 1: 最外沿高光微晶边 (Inset: 0px, Stroke: 1.0f, Alpha: 235)
            int aCore = Math.Max(0, Math.Min(255, (int)(235 * alphaMultiplier)));
            if (aCore > 0)
            {
                Color acTop = Color.FromArgb(aCore, cTop.R, cTop.G, cTop.B);
                Color acRight = Color.FromArgb(aCore, cRight.R, cRight.G, cRight.B);
                Color acBottom = Color.FromArgb(aCore, cBottom.R, cBottom.G, cBottom.B);
                Color acLeft = Color.FromArgb(aCore, cLeft.R, cLeft.G, cLeft.B);

                using (GraphicsPath path = RoundedCard.GetRoundedRectangle(bounds, radius))
                {
                    using (LinearGradientBrush lgb = new LinearGradientBrush(bounds, acTop, acBottom, baseHue))
                    {
                        ColorBlend cb = new ColorBlend();
                        cb.Colors = new Color[] { acTop, acRight, acBottom, acLeft, acTop };
                        cb.Positions = new float[] { 0f, 0.25f, 0.5f, 0.75f, 1f };
                        lgb.InterpolationColors = cb;

                        using (Pen corePen = new Pen(lgb, 1.2f))
                        {
                            g.DrawPath(corePen, path);
                        }
                    }
                }
            }
        }

        private void DrawInwardGlowLayer(Graphics g, Rectangle bounds, int radius, int inset, float penWidth, int alpha, Color c1, Color c2, Color c3, Color c4)
        {
            Rectangle r = bounds;
            r.Inflate(-inset, -inset);
            if (r.Width <= 10 || r.Height <= 10) return;

            int rad = Math.Max(2, radius - inset);
            int safeAlpha = Math.Max(0, Math.Min(255, alpha));
            if (safeAlpha <= 0) return;

            Color a1 = Color.FromArgb(safeAlpha, c1.R, c1.G, c1.B);
            Color a2 = Color.FromArgb(safeAlpha, c2.R, c2.G, c2.B);
            Color a3 = Color.FromArgb(safeAlpha, c3.R, c3.G, c3.B);
            Color a4 = Color.FromArgb(safeAlpha, c4.R, c4.G, c4.B);

            using (GraphicsPath path = RoundedCard.GetRoundedRectangle(r, rad))
            {
                using (LinearGradientBrush lgb = new LinearGradientBrush(r, a1, a3, 45f))
                {
                    ColorBlend cb = new ColorBlend();
                    cb.Colors = new Color[] { a1, a2, a3, a4, a1 };
                    cb.Positions = new float[] { 0f, 0.25f, 0.5f, 0.75f, 1f };
                    lgb.InterpolationColors = cb;

                    using (Pen glowPen = new Pen(lgb, penWidth))
                    {
                        glowPen.LineJoin = LineJoin.Round;
                        g.DrawPath(glowPen, path);
                    }
                }
            }
        }

        #endregion

        #region 全屏极光破晓·双向帘幕漫融转场动效 (Full-Window Aurora Veil & Cross-Dissolve)

        private static void DrawBitmapWithAlphaAndScale(Graphics g, Bitmap bmp, float alpha, float scale, int targetW, int targetH)
        {
            if (bmp == null || alpha <= 0.001f) return;
            float safeAlpha = Math.Max(0f, Math.Min(1f, alpha));

            ColorMatrix cm = new ColorMatrix();
            cm.Matrix33 = safeAlpha;
            using (ImageAttributes ia = new ImageAttributes())
            {
                ia.SetColorMatrix(cm, ColorMatrixFlag.Default, ColorAdjustType.Bitmap);

                float drawW = targetW * scale;
                float drawH = targetH * scale;
                float drawX = (targetW - drawW) / 2f;
                float drawY = (targetH - drawH) / 2f;

                g.DrawImage(bmp, new Rectangle((int)drawX, (int)drawY, (int)drawW, (int)drawH), 0, 0, bmp.Width, bmp.Height, GraphicsUnit.Pixel, ia);
            }
        }

        private void TriggerQuantumShatter(bool toBack)
        {
            if (shatterTimer != null && shatterTimer.Enabled) return;

            isShatteringToBack = toBack;
            currentShatterFrame = 0;
            transitionMotes.Clear();

            int w = flipContainer.Width;
            int h = flipContainer.Height;

            // 无论正向还是反向转场，立即清空正面点击激发的爱心粒子、微光与水波纹，确保快照与返回绝对纯净
            heartParticles.Clear();
            frontParticles.Clear();
            frontRipples.Clear();
            wasFrontParticlesActive = false;
            if (pnlFront != null && !pnlFront.IsDisposed)
            {
                pnlFront.Invalidate();
            }
            InvalidateFrontOverlayControls();

            // 截取当前处于可见状态的面板快照以供全屏平滑渐变
            try
            {
                if (toBack)
                {
                    if (snapFront != null) snapFront.Dispose();
                    snapFront = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                    pnlFront.DrawToBitmap(snapFront, new Rectangle(0, 0, w, h));
                }
                else
                {
                    if (snapBack != null) snapBack.Dispose();
                    snapBack = new Bitmap(w, h, PixelFormat.Format32bppPArgb);
                    pnlBack.DrawToBitmap(snapBack, new Rectangle(0, 0, w, h));
                }
            }
            catch { }

            Color[] moteColors = new Color[]
            {
                Color.FromArgb(255, 105, 180), // 浪漫樱粉
                Color.FromArgb(0, 230, 118),   // 灵脉翡翠绿
                Color.FromArgb(100, 210, 255), // 晨曦水蓝
                Color.FromArgb(255, 215, 0),   // 温暖香槟金
                Color.FromArgb(190, 140, 255)  // 梦幻薰衣草紫
            };

            // 播撒 22 枚全屏飞扬的浪漫爱心与极光微晶浮粒
            for (int i = 0; i < 22; i++)
            {
                EtherealMote m = new EtherealMote();
                m.X = rnd.Next(20, w - 20);
                m.Y = rnd.Next(40, h - 30);
                m.Vx = (float)((rnd.NextDouble() - 0.5) * 0.8);
                m.Vy = (float)(-rnd.NextDouble() * 1.2 - 0.4); // 向上舒展飞扬
                m.Alpha = (float)(rnd.NextDouble() * 0.4 + 0.6);
                m.IsHeart = (i % 2 == 0); // 一半是心形矢量，一半是星芒微晶
                m.Size = m.IsHeart ? (float)(rnd.NextDouble() * 8.0 + 10.0) : (float)(rnd.NextDouble() * 2.5 + 1.5);
                m.Rotation = (float)(rnd.NextDouble() * 360.0);
                m.RotSpeed = (float)((rnd.NextDouble() - 0.5) * 2.5);
                m.Color = moteColors[rnd.Next(moteColors.Length)];
                transitionMotes.Add(m);
            }

            // 激活全屏转场画布并立即隐藏顶部标题栏，保证全屏彩蛋沉浸视觉
            if (modernChromeTitleBar != null)
            {
                modernChromeTitleBar.Visible = false;
            }
            pnlFront.Visible = false;
            pnlBack.Visible = false;
            shatterCanvas.Visible = true;
            shatterCanvas.BringToFront();

            if (shatterTimer == null)
            {
                shatterTimer = new Timer();
                shatterTimer.Interval = 16;
                shatterTimer.Tick += OnShatterTimerTick;
            }

            shatterTimer.Start();
        }

        private void OnShatterTimerTick(object sender, EventArgs e)
        {
            currentShatterFrame++;
            shatterProgress = (float)currentShatterFrame / (float)TotalShatterFrames;

            // 漫游爱心与极光微芒演进
            for (int i = 0; i < transitionMotes.Count; i++)
            {
                EtherealMote m = transitionMotes[i];
                m.X += m.Vx;
                m.Y += m.Vy;
                m.Rotation += m.RotSpeed;
            }

            if (shatterProgress >= 1.0f)
            {
                shatterTimer.Stop();

                // 动效完毕：隐藏全屏画布，恢复目标面板与对应标题栏状态
                shatterCanvas.Visible = false;
                if (isShatteringToBack)
                {
                    pnlBack.Visible = true;
                    pnlFront.Visible = false;
                    if (modernChromeTitleBar != null) modernChromeTitleBar.Visible = false;
                    backOpenTime = DateTime.UtcNow;
                    waveEnergy = 0.25f;
                    targetWaveEnergy = 0.25f;
                    wavePluckX = 0.5f;
                    targetWavePluckX = 0.5f;
                    clickBloomAlpha = 0f;
                    isBackBtnHovered = false;
                    isBackBtnPressed = false;
                    backBtnHoverProgress = 0f;
                    ChimeSynthesizer.StartAmbientMelody();
                }
                else
                {
                    isChromaActive = false;
                    isBackBtnHovered = false;
                    isBackBtnPressed = false;
                    backBtnHoverProgress = 0f;
                    pnlFront.Visible = true;
                    pnlBack.Visible = false;
                    if (modernChromeTitleBar != null)
                    {
                        modernChromeTitleBar.Visible = true;
                        modernChromeTitleBar.BringToFront();
                    }
                    ChimeSynthesizer.StopAmbientMelody();
                    this.Invalidate();
                }
                transitionMotes.Clear();
                return;
            }

            shatterCanvas.Invalidate();
        }

        private void ShatterCanvas_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            int w = shatterCanvas.Width;
            int h = shatterCanvas.Height;
            float cx = w / 2f;
            float cy = h / 2f;

            // 全屏无边框深空画布基底
            g.Clear(Color.FromArgb(11, 13, 19));

            float p = shatterProgress; // 0.0 -> 1.0

            if (isShatteringToBack)
            {
                // ==================== 正向转场: 正面 ➔ 背面 ====================
                // 1. 正面快照平滑淡出与轻微扩张 (Scale 1.0 -> 1.03, Alpha 1.0 -> 0.0)
                if (p < 0.65f && snapFront != null)
                {
                    float fadeFront = (float)Math.Cos((p / 0.65f) * (Math.PI / 2.0));
                    float scaleFront = 1.0f + p * 0.04f;
                    DrawBitmapWithAlphaAndScale(g, snapFront, fadeFront, scaleFront, w, h);
                }

                // 2. 全屏极光破晓漫幕 (Full-Window Aurora Veil: 半径 80px -> 800px 彻底漫溢全屏)
                float maxAuroraR = (float)Math.Sqrt(w * w + h * h) * 0.85f;
                float auroraR = 60f + p * maxAuroraR;
                float auroraBreath = (float)Math.Sin(p * Math.PI);
                int auroraAlpha = Math.Max(0, Math.Min(255, (int)(auroraBreath * 140)));
                if (auroraAlpha > 0)
                {
                    using (GraphicsPath aPath = new GraphicsPath())
                    {
                        aPath.AddEllipse(cx - auroraR, cy - auroraR, auroraR * 2, auroraR * 2);
                        using (PathGradientBrush pgb = new PathGradientBrush(aPath))
                        {
                            pgb.CenterColor = Color.FromArgb(auroraAlpha, 0, 230, 140);
                            pgb.SurroundColors = new Color[] { Color.FromArgb(0, 11, 13, 19) };
                            g.FillPath(pgb, aPath);
                        }
                    }

                    // 晨曦水蓝核心柔波
                    float subR = auroraR * 0.6f;
                    using (GraphicsPath subPath = new GraphicsPath())
                    {
                        subPath.AddEllipse(cx - subR, cy - subR, subR * 2, subR * 2);
                        using (PathGradientBrush subPgb = new PathGradientBrush(subPath))
                        {
                            subPgb.CenterColor = Color.FromArgb((int)(auroraAlpha * 0.7f), 80, 210, 255);
                            subPgb.SurroundColors = new Color[] { Color.FromArgb(0, 11, 13, 19) };
                            g.FillPath(subPgb, subPath);
                        }
                    }
                }

                // 3. 全画幅黑曜石背景与贴边钛金流光框平滑渐显 (Cross-Dissolve: 0.35 -> 1.0)
                if (p >= 0.35f)
                {
                    float appearT = (p - 0.35f) / 0.65f;
                    float smoothAlpha = appearT * appearT * (3f - 2f * appearT);
                    using (SolidBrush bgB = new SolidBrush(Color.FromArgb((int)(smoothAlpha * 245), 11, 13, 19)))
                    {
                        g.FillRectangle(bgB, 0, 0, w, h);
                    }
                    float rimX = hasPointerInBack ? smoothMouseX : (w * 0.5f + (float)Math.Cos(pulsePhase * 0.65f) * w * 0.48f);
                    float rimY = hasPointerInBack ? smoothMouseY : (h * 0.5f + (float)Math.Sin(pulsePhase * 0.65f) * h * 0.48f);
                    DrawTitaniumSpecularBorder(g, new Rectangle(2, 2, Math.Max(10, w - 5), Math.Max(10, h - 5)), Math.Max(6, (int)Math.Round(8 * EffectiveScale)), rimX, rimY, smoothAlpha);
                }

                // 4. 全屏飞扬的浪漫爱心与微晶浮粒 (Floating Vector Hearts & Stardust Motes)
                for (int i = 0; i < transitionMotes.Count; i++)
                {
                    EtherealMote m = transitionMotes[i];
                    int a = Math.Max(0, Math.Min(255, (int)(m.Alpha * Math.Sin(p * Math.PI) * 255)));
                    if (a > 0)
                    {
                        using (SolidBrush mb = new SolidBrush(Color.FromArgb(a, m.Color)))
                        {
                            if (m.IsHeart)
                            {
                                DrawHeart(g, mb, m.X, m.Y, m.Size, m.Rotation);
                            }
                            else
                            {
                                g.FillEllipse(mb, m.X - m.Size / 2f, m.Y - m.Size / 2f, m.Size, m.Size);
                            }
                        }
                    }
                }
            }
            else
            {
                // ==================== 反向转场: 背面 ➔ 正面 ====================
                // 1. 背面内容向内轻柔淡出 (Scale 1.0 -> 0.96, Alpha 1.0 -> 0.0)
                float pBack = Math.Max(0f, 1.0f - p / 0.65f);
                float smoothBack = (float)(0.5 - 0.5 * Math.Cos(pBack * Math.PI));
                int backAlpha = Math.Max(0, Math.Min(255, (int)(smoothBack * 255)));
                if (backAlpha > 0)
                {
                    if (snapBack != null)
                    {
                        float scaleBack = 1.0f - p * 0.04f;
                        DrawBitmapWithAlphaAndScale(g, snapBack, smoothBack, scaleBack, w, h);
                    }
                    else
                    {
                        using (SolidBrush bgB = new SolidBrush(Color.FromArgb((int)(smoothBack * 235), 11, 13, 19)))
                        {
                            g.FillRectangle(bgB, 0, 0, w, h);
                        }
                        DrawPoeticManifestoWithAlpha(g, w, h, 0, 0, backAlpha, 999f);
                    }
                }

                // 2. 全屏极光向内收敛 (Aurora Inward Veil)
                float rContract = (float)Math.Sqrt(w * w + h * h) * 0.8f * (1.0f - p);
                int contractAlpha = Math.Max(0, Math.Min(255, (int)(Math.Sin(p * Math.PI) * 110)));
                if (contractAlpha > 0 && rContract > 10f)
                {
                    using (GraphicsPath aPath = new GraphicsPath())
                    {
                        aPath.AddEllipse(cx - rContract, cy - rContract, rContract * 2, rContract * 2);
                        using (PathGradientBrush pgb = new PathGradientBrush(aPath))
                        {
                            pgb.CenterColor = Color.FromArgb(contractAlpha, 0, 230, 140);
                            pgb.SurroundColors = new Color[] { Color.FromArgb(0, 18, 19, 23) };
                            g.FillPath(pgb, aPath);
                        }
                    }
                }

                // 3. 正面快照全面复苏 (Front View Rebirth: 0.25 -> 1.0, Scale 1.03 -> 1.0, Alpha 0.0 -> 1.0)
                if (p >= 0.25f && snapFront != null)
                {
                    float appearFront = (p - 0.25f) / 0.75f;
                    float smoothFront = (float)(0.5 - 0.5 * Math.Cos(appearFront * Math.PI));
                    float scaleFront = 1.03f - (appearFront * 0.03f);
                    DrawBitmapWithAlphaAndScale(g, snapFront, smoothFront, scaleFront, w, h);
                }

                // 4. 柔和回归的微晶浮粒 (Gentle Return Motes)
                for (int i = 0; i < transitionMotes.Count; i++)
                {
                    EtherealMote m = transitionMotes[i];
                    int a = Math.Max(0, Math.Min(255, (int)(m.Alpha * Math.Sin(p * Math.PI) * 200)));
                    if (a > 0)
                    {
                        using (SolidBrush mb = new SolidBrush(Color.FromArgb(a, m.Color)))
                        {
                            if (m.IsHeart)
                            {
                                DrawHeart(g, mb, m.X, m.Y, m.Size, m.Rotation);
                            }
                            else
                            {
                                g.FillEllipse(mb, m.X - m.Size / 2f, m.Y - m.Size / 2f, m.Size, m.Size);
                            }
                        }
                    }
                }
            }
        }

        #endregion

        #region 轻叩 5 次触发、2.5s 空闲重置与唯美爱心粒子

        private void HandleAuthorHit()
        {
            // 1. 点击激发浪漫唯美爱心粒子 (Real Vector Heart Motes)
            Point center = new Point(lblAuthorName.Left + lblAuthorName.Width / 2, lblAuthorName.Top + lblAuthorName.Height / 2);
            SpawnHeartParticles(center.X, center.Y);
            if (pnlFront != null && pnlFront.Visible)
            {
                pnlFront.Invalidate();
            }

            // 2. 维护 2.5s 空闲重置计时器 (长时间不按自动重置计数)
            if (noteResetTimer == null)
            {
                noteResetTimer = new Timer();
                noteResetTimer.Interval = 2500;
                noteResetTimer.Tick += delegate(object s, EventArgs ev)
                {
                    noteResetTimer.Stop();
                    easterEggNoteIndex = 0;
                };
            }
            noteResetTimer.Stop();
            noteResetTimer.Start();

            easterEggNoteIndex++;

            // 3. 轻叩 5 次达成，启动【全屏极光破晓·帘幕漫融】转场进入《写在代码之外》心语信笺
            if (easterEggNoteIndex >= 5)
            {
                easterEggNoteIndex = 0;
                if (noteResetTimer != null) noteResetTimer.Stop();

                isChromaActive = true;
                TriggerQuantumShatter(true);
            }
        }

        private void OnAuthorNameClick(object sender, EventArgs e)
        {
            HandleAuthorHit();
        }

        private void SpawnHeartParticles(int originX, int originY)
        {
            Color[] heartColors = new Color[]
            {
                Color.FromArgb(255, 105, 180), // 浪漫樱粉
                Color.FromArgb(255, 64, 129),  // 玫瑰绯红
                Color.FromArgb(0, 230, 118),   // 灵脉翡翠绿
                Color.FromArgb(100, 210, 255), // 晨曦水蓝
                Color.FromArgb(255, 215, 0),   // 温暖香槟金
                Color.FromArgb(190, 140, 255)  // 梦幻薰衣草紫
            };

            for (int i = 0; i < 16; i++)
            {
                HeartParticle hp = new HeartParticle();
                hp.X = originX + (float)((rnd.NextDouble() - 0.5) * 36.0);
                hp.Y = originY + (float)((rnd.NextDouble() - 0.5) * 18.0);
                hp.Vx = (float)((rnd.NextDouble() - 0.5) * 1.5);
                hp.Vy = (float)(-rnd.NextDouble() * 1.6 - 0.6); // 柔美轻盈向上浮动
                hp.Alpha = 0.98f;
                hp.Size = (float)(rnd.NextDouble() * 8.0 + 11.0); // 11 ~ 19px 晶莹心形
                hp.Rotation = (float)(rnd.NextDouble() * 60.0 - 30.0);
                hp.RotSpeed = (float)((rnd.NextDouble() - 0.5) * 2.2);
                hp.Color = heartColors[rnd.Next(heartColors.Length)];
                heartParticles.Add(hp);
            }
        }

        #endregion

        #region 在线更新检测

        private void OnPendingUpdateChangedHandler()
        {
            if (this.IsDisposed) return;
            if (this.InvokeRequired)
            {
                this.BeginInvoke((MethodInvoker)UpdateAboutUpdateButtonState);
                return;
            }
            UpdateAboutUpdateButtonState();
        }

        private float ResolveChildDialogScale()
        {
            if (ChildDialogScale >= DpiScaleHelper.MinScale)
            {
                return ChildDialogScale;
            }
            IntPtr hWnd = this.IsHandleCreated ? this.Handle : IntPtr.Zero;
            float sysDpi = DpiScaleHelper.GetDpiScaleForWindow(hWnd);
            Screen scr = this.IsHandleCreated ? Screen.FromHandle(this.Handle) : Screen.FromPoint(Cursor.Position);
            return DpiScaleHelper.ClampScaleToScreen(sysDpi * EffectiveScale, scr, 680);
        }

        private void UpdateAboutUpdateButtonState()
        {
            if (this.IsDisposed || btnCheckUpdate == null || lblUpdateStatus == null) return;

            var pending = UpdateManager.PendingUpdate;
            if (pending != null && !string.IsNullOrEmpty(pending.LatestVersion) && UpdateManager.IsNewerVersion(pending.LatestVersion, AppVersion))
            {
                string cleanVer = pending.LatestVersion.Trim().TrimStart('v', 'V');
                int sp = cleanVer.IndexOf(' ');
                if (sp > 0) cleanVer = cleanVer.Substring(0, sp);

                btnCheckUpdate.Text = "查看更新";
                btnCheckUpdate.NormalColor = Color.FromArgb(0, 160, 70);
                btnCheckUpdate.HoverColor = Color.FromArgb(0, 200, 83);
                btnCheckUpdate.PressedColor = Color.FromArgb(0, 140, 58);
                btnCheckUpdate.BorderColor = Color.FromArgb(0, 230, 118);

                lblUpdateStatus.Text = "当前版本: " + AppVersion + "  ➔  发现新版本: v" + cleanVer;
                lblUpdateStatus.ForeColor = Color.FromArgb(0, 230, 118);
            }
            else if (localCheckCompleted || UpdateManager.HasCompletedCheck)
            {
                btnCheckUpdate.Text = "更新日志";
                btnCheckUpdate.NormalColor = Color.FromArgb(36, 40, 52);
                btnCheckUpdate.HoverColor = Color.FromArgb(48, 54, 70);
                btnCheckUpdate.PressedColor = Color.FromArgb(28, 30, 40);
                btnCheckUpdate.BorderColor = Color.FromArgb(0, 200, 83);

                if (localCheckFailed || (UpdateManager.HasCompletedCheck && !UpdateManager.LastCheckSucceeded))
                {
                    lblUpdateStatus.Text = "检查更新失败，可点击查看内置更新日志";
                    lblUpdateStatus.ForeColor = Color.FromArgb(255, 171, 0);
                }
                else
                {
                    lblUpdateStatus.Text = "当前已是最新版本 (" + AppVersion + ")";
                    lblUpdateStatus.ForeColor = Color.FromArgb(0, 230, 118);
                }
            }
            else
            {
                btnCheckUpdate.Text = "检查更新";
                btnCheckUpdate.NormalColor = Color.FromArgb(36, 40, 52);
                btnCheckUpdate.HoverColor = Color.FromArgb(48, 54, 70);
                btnCheckUpdate.PressedColor = Color.FromArgb(28, 30, 40);
                btnCheckUpdate.BorderColor = Color.FromArgb(0, 200, 83);

                lblUpdateStatus.Text = "当前版本: " + AppVersion;
                lblUpdateStatus.ForeColor = Color.FromArgb(140, 148, 162);
            }
        }

        private void OpenCacheClearDialog()
        {
            using (CacheClearConfirmDialog dlg = new CacheClearConfirmDialog(ResolveChildDialogScale()))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || !dlg.Confirmed)
                {
                    return;
                }

                CacheResetReport report = CacheResetManager.Execute();
                if (!report.Success)
                {
                    MessageBox.Show(
                        this,
                        "以下项目未能清理（其余已处理）：\n\n" + report.BuildSummary() + "\n\n程序将重启，可稍后重试或手动删除。",
                        "清除缓存未完全成功 - FerrisPulse · 灵脉",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }

                // 清理后必须重启：内存里的设备列表与缓存仍是旧状态，且配置回写会重新生成注册表树。
                AppResetManager.RestartApplicationAndExit();
            }
        }

        private void OpenResetDialog()
        {
            using (ResetConfirmDialog dlg = new ResetConfirmDialog(ResolveChildDialogScale()))
            {
                if (dlg.ShowDialog(this) != DialogResult.OK || dlg.SelectedMode == ResetActionMode.None)
                {
                    return;
                }

                bool removeShortcuts = (dlg.SelectedMode == ResetActionMode.CleanAndExit);
                string errMsg;
                AppResetManager.PerformFullReset(removeShortcuts, out errMsg);

                if (dlg.SelectedMode == ResetActionMode.ResetAndRestart)
                {
                    AppResetManager.RestartApplicationAndExit();
                }
                else if (dlg.SelectedMode == ResetActionMode.CleanAndExit)
                {
                    MessageBox.Show(
                        this,
                        "已彻底清空所有历史版本的注册表配置、开机启动项、设备缓存与快捷方式。\n\n点击“确定”后程序将立即退出，您现在可以直接删除软件文件或重新开始。",
                        "清理完成 - FerrisPulse · 灵脉",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);

                    try
                    {
                        Program.ReleaseSingleInstanceMutex();
                    }
                    catch { }

                    Environment.Exit(0);
                }
            }
        }

        private void BtnCheckUpdate_Click(object sender, EventArgs e)
        {
            var pending = UpdateManager.PendingUpdate;
            if (pending != null && UpdateManager.IsNewerVersion(pending.LatestVersion, AppVersion))
            {
                // 0ms 秒开：发现新版本直接从内存打开弹窗，弹窗内部 SWR 自行静默复检
                UpdateNoticeForm.ShowUpdateDialog(this, pending, AppVersion, ResolveChildDialogScale());
                return;
            }

            if (localCheckCompleted || UpdateManager.HasCompletedCheck)
            {
                ChangelogHistoryForm.ShowHistoryDialog(this, ResolveChildDialogScale());
                return;
            }

            btnCheckUpdate.Enabled = false;
            btnCheckUpdate.Text = "检查中...";
            lblUpdateStatus.Text = "正在连接更新服务器...";
            lblUpdateStatus.ForeColor = Color.FromArgb(0, 230, 118);

            UpdateManager.CheckUpdateAsync(delegate(bool success, UpdateInfo info, string error)
            {
                if (this.IsDisposed || !this.IsHandleCreated) return;

                this.BeginInvoke((MethodInvoker)delegate
                {
                    btnCheckUpdate.Enabled = true;

                    if (!success)
                    {
                        localCheckCompleted = true;
                        localCheckFailed = true;
                        UpdateAboutUpdateButtonState();
                        return;
                    }

                    if (info != null && UpdateManager.IsNewerVersion(info.LatestVersion, AppVersion))
                    {
                        localCheckCompleted = true;
                        localCheckFailed = false;
                        UpdateAboutUpdateButtonState();
                        // 弹出新版本提示与更新对话框
                        UpdateNoticeForm.ShowUpdateDialog(this, info, AppVersion, ResolveChildDialogScale());
                    }
                    else
                    {
                        localCheckCompleted = true;
                        localCheckFailed = false;
                        UpdateAboutUpdateButtonState();
                    }
                });
            });
        }

        #endregion

        #region 原生拖动与消息调度

        private void OnDragMouseDown(object sender, MouseEventArgs e)
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
                if (pt.Y <= 42)
                {
                    m.Result = (IntPtr)HTCAPTION;
                }
            }
        }

        #endregion

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            if (isChromaActive)
            {
                // 无边框沉浸模式：外缘 1px 与深空黑曜石背景无缝融为一体
                using (Pen seamlessPen = new Pen(Color.FromArgb(11, 13, 19), 1.0f))
                {
                    g.DrawRectangle(seamlessPen, 0, 0, this.Width - 1, this.Height - 1);
                }
            }
            else
            {
                // 窗体暗黑微光细微边框
                using (Pen borderPen = new Pen(Color.FromArgb(42, 46, 58), 1))
                {
                    g.DrawRectangle(borderPen, 0, 0, this.Width - 1, this.Height - 1);
                }
            }
        }

        private static Color ColorFromHsv(float hue, float saturation, float value)
        {
            int hi = Convert.ToInt32(Math.Floor(hue / 60)) % 6;
            double f = hue / 60 - Math.Floor(hue / 60);

            value = value * 255;
            int v = Convert.ToInt32(value);
            int p = Convert.ToInt32(value * (1 - saturation));
            int q = Convert.ToInt32(value * (1 - f * saturation));
            int t = Convert.ToInt32(value * (1 - (1 - f) * saturation));

            if (hi == 0) return Color.FromArgb(255, v, t, p);
            else if (hi == 1) return Color.FromArgb(255, q, v, p);
            else if (hi == 2) return Color.FromArgb(255, p, v, t);
            else if (hi == 3) return Color.FromArgb(255, p, q, v);
            else if (hi == 4) return Color.FromArgb(255, t, p, v);
            else return Color.FromArgb(255, v, p, q);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                UpdateManager.OnPendingUpdateChanged -= OnPendingUpdateChangedHandler;
                if (animationTimer != null)
                {
                    try { animationTimer.Stop(); animationTimer.Dispose(); } catch { }
                    animationTimer = null;
                }
                if (noteResetTimer != null)
                {
                    try { noteResetTimer.Stop(); noteResetTimer.Dispose(); } catch { }
                    noteResetTimer = null;
                }
                if (shatterTimer != null)
                {
                    try { shatterTimer.Stop(); shatterTimer.Dispose(); } catch { }
                    shatterTimer = null;
                }
                if (snapFront != null)
                {
                    try { snapFront.Dispose(); } catch { }
                    snapFront = null;
                }
                if (snapBack != null)
                {
                    try { snapBack.Dispose(); } catch { }
                    snapBack = null;
                }
            }
            base.Dispose(disposing);
        }
    }
}
