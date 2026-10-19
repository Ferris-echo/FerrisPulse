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
//   3. 粒子破碎瓦解 ➔ 灵动汇聚重组（Quantum Shatter & Vortex Reassembly）终极过渡动效
//   4. 真实物理级内聚霓虹柔光（5 层 Inward Bloom Aura）与 60fps 动态全彩流光
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
        private Label lblLogo;
        private Label lblAuthorPrefix;
        private Label lblAuthorName;
        private Label lblAuthorSuffix;
        private Label lblDonateDesc;

        // 双二维码赞赏面板
        private Panel qrPanelWeChat;
        private Panel qrPanelAlipay;
        private LinkLabel lnkQQ;
        private LinkLabel lnkGithub;

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

        // 鼠标交互模型：星尘拖尾与水波纹
        private class StardustParticle
        {
            public float X, Y;
            public float Vx, Vy;
            public float Alpha;
            public float Size;
            public Color Color = Color.White;
        }

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
        private System.Collections.Generic.List<AmbientParticle> backParticles = new System.Collections.Generic.List<AmbientParticle>();
        private System.Collections.Generic.List<StardustParticle> stardustTrail = new System.Collections.Generic.List<StardustParticle>();
        private System.Collections.Generic.List<WaterRipple> waterRipples = new System.Collections.Generic.List<WaterRipple>();
        private System.Collections.Generic.List<EtherealMote> transitionMotes = new System.Collections.Generic.List<EtherealMote>();
        private System.Collections.Generic.List<ShatterShard> shatterShards = new System.Collections.Generic.List<ShatterShard>();

        private Bitmap snapFront = null;
        private Bitmap snapBack = null;

        private Point lastMouseTrailPos = Point.Empty;
        private Timer animationTimer = null;
        private Random rnd = new Random();
        private DateTime backOpenTime = DateTime.MinValue;

        // 八音阶彩蛋与 2.5s 空闲重置计时器
        private int easterEggNoteIndex = 0;
        private Timer noteResetTimer = null;

        // 全彩霓虹旋转流光循环系统 (Rotating Chroma with Inward Bloom)
        private bool isChromaActive = false;
        private float chromaHue = 0f;

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
            this.FormBorderStyle = FormBorderStyle.FixedDialog;
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

            // 1. 顶部标题栏由 Windows 11 原生沉浸式暗黑标题栏接管 (触发 Windows 自带弹性弹出与收回动画)
            titleBar = new Panel();
            titleBar.Visible = false;

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
            lblDonateDesc.Text = "如果这款软件为您的游戏体验带来了便利，欢迎赞赏支持作者！";
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
            lnkQQ.BackColor = Color.Transparent;
            lnkQQ.AutoSize = true;
            lnkQQ.LinkClicked += delegate(object s, LinkLabelLinkClickedEventArgs e)
            {
                try
                {
                    Clipboard.SetText("580180452");
                    lnkQQ.Text = "QQ 交流群: 580180452 (已复制)";
                    CenterControlHorizontally(lnkQQ, cardDonate.Width);

                    Timer copyTimer = new Timer();
                    copyTimer.Interval = 2000;
                    copyTimer.Tick += delegate(object s2, EventArgs e2)
                    {
                        copyTimer.Stop();
                        copyTimer.Dispose();
                        if (!lnkQQ.IsDisposed)
                        {
                            lnkQQ.Text = "QQ 交流群: 580180452";
                            CenterControlHorizontally(lnkQQ, cardDonate.Width);
                        }
                    };
                    copyTimer.Start();

                    string qqShareUrl = "https://qun.qq.com/universal-share/share?ac=1&authKey=jSTwQOun56pd3gi6ts34tN%2F6TDFrBouMRWJE%2BSsHaKS7%2FrrSlKHLu%2FXYJXfDZpl%2B&busi_data=eyJncm91cENvZGUiOiI1ODAxODA0NTIiLCJ0b2tlbiI6Ind2Y1ZocnhsekFnVi8xN3AzOXl1dTY5VlA2NWtCSFd6NFphcXR2dHhCTW10R3NwYWpyZmhGNXdNS3ZhS2owZzAiLCJ1aW4iOiIyOTQ5MjI1ODQwIn0%3D&data=8-Avoq-5DTwgCiC_xlKHxhhhnZp7d66IdyHPeha1XGGNxS3pp9s1uPoyaE-RtU_ZhIb74Px33-A1ZRe3oIw8vQ&svctype=4&tempid=h5_group_info";
                    try { Process.Start(qqShareUrl); } catch { }
                }
                catch { }
            };
            cardDonate.Controls.Add(lnkQQ);

            lnkGithub = new LinkLabel();
            lnkGithub.Text = "GitHub: Ferris-echo/FerrisPulse";
            lnkGithub.Font = new Font("Microsoft YaHei UI", 8.8F);
            lnkGithub.LinkColor = Color.FromArgb(0, 215, 115);
            lnkGithub.ActiveLinkColor = Color.FromArgb(0, 255, 130);
            lnkGithub.VisitedLinkColor = Color.FromArgb(0, 215, 115);
            lnkGithub.BackColor = Color.Transparent;
            lnkGithub.AutoSize = true;
            lnkGithub.LinkClicked += delegate(object s, LinkLabelLinkClickedEventArgs e)
            {
                try { Process.Start("https://github.com/Ferris-echo/FerrisPulse"); } catch { }
            };
            cardDonate.Controls.Add(lnkGithub);

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
            cardQuote.CardColor = Color.FromArgb(20, 23, 32);
            cardQuote.BorderColor = Color.Transparent;
            cardQuote.MouseDown += CardQuote_MouseDown;
            cardQuote.MouseMove += CardQuote_MouseMove;
            cardQuote.Paint += CardQuote_Paint;

            btnFlipBack = new ModernButton();
            btnFlipBack.Text = "返  回";
            btnFlipBack.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
            btnFlipBack.NormalColor = Color.FromArgb(28, 34, 46);
            btnFlipBack.HoverColor = Color.FromArgb(42, 52, 72);
            btnFlipBack.PressedColor = Color.FromArgb(20, 24, 32);
            btnFlipBack.BorderColor = Color.FromArgb(60, 75, 100);
            btnFlipBack.ForeColor = Color.FromArgb(200, 215, 235);
            btnFlipBack.Click += delegate(object s, EventArgs e)
            {
                ChimeSynthesizer.StopAmbientMelody();
                TriggerQuantumShatter(false);
            };
            cardQuote.Controls.Add(btnFlipBack);

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

        private void ApplyAboutLayout()
        {
            const int clientW = 600;
            const int padX = 24;
            const int cardWidth = clientW - (padX * 2); // 552

            // 1. 顶部标题栏由 Windows 11 原生沉浸式暗黑标题栏接管
            if (titleBar != null)
            {
                titleBar.Visible = false;
            }

            // 2. 正面品牌与署名
            const int topY = 14;
            if (lblLogo != null)
            {
                lblLogo.Font = new Font("Microsoft YaHei UI", 14F, FontStyle.Bold);
                lblLogo.Location = new Point(padX, topY);
            }

            const int authorY = topY + 36; // 50
            if (lblAuthorPrefix != null)
            {
                lblAuthorPrefix.Font = new Font("Microsoft YaHei UI", 9.2F, FontStyle.Regular);
                lblAuthorPrefix.Location = new Point(padX, authorY);
                Size prefixSize = TextRenderer.MeasureText(lblAuthorPrefix.Text, lblAuthorPrefix.Font);
                if (lblAuthorName != null)
                {
                    lblAuthorName.Font = new Font("Microsoft YaHei UI", 9.2F, FontStyle.Bold);
                    lblAuthorName.Location = new Point(padX + prefixSize.Width - 6, authorY);
                    Size nameSize = TextRenderer.MeasureText(AuthorName, lblAuthorName.Font);
                    if (lblAuthorSuffix != null)
                    {
                        lblAuthorSuffix.Font = new Font("Microsoft YaHei UI", 9.2F, FontStyle.Regular);
                        lblAuthorSuffix.Location = new Point(lblAuthorName.Left + nameSize.Width - 4, authorY);
                        lblAuthorName.SizeChanged += delegate
                        {
                            if (lblAuthorSuffix != null && !lblAuthorSuffix.IsDisposed)
                            {
                                lblAuthorSuffix.Location = new Point(lblAuthorName.Right - 4, authorY);
                            }
                        };
                    }
                }
            }

            // Card 1: 软件在线更新卡片 (原始设计高度 114)
            const int card1Y = topY + 70; // 84
            const int card1H = 114;
            if (cardUpdate != null)
            {
                cardUpdate.Location = new Point(padX, card1Y);
                cardUpdate.Size = new Size(cardWidth, card1H);
                cardUpdate.CornerRadius = 8;

                if (lblCardUpdateTitle != null)
                {
                    lblCardUpdateTitle.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
                    lblCardUpdateTitle.Location = new Point(20, 15);
                }
                if (btnCheckUpdate != null)
                {
                    btnCheckUpdate.Font = new Font("Microsoft YaHei UI", 9F, FontStyle.Bold);
                    btnCheckUpdate.Size = new Size(110, 32);
                    btnCheckUpdate.Location = new Point(cardWidth - 20 - 110, 44);
                    btnCheckUpdate.CornerRadius = 6;
                }
                if (chkAutoUpdate != null)
                {
                    chkAutoUpdate.Font = new Font("Microsoft YaHei UI", 8.8F);
                    int chkMaxW = (btnCheckUpdate != null ? btnCheckUpdate.Left : cardWidth) - 40;
                    chkAutoUpdate.Location = new Point(20, 47);
                    chkAutoUpdate.Size = new Size(chkMaxW, 26);
                }
                if (lblUpdateStatus != null)
                {
                    lblUpdateStatus.Font = new Font("Microsoft YaHei UI", 8.5F);
                    lblUpdateStatus.Location = new Point(20, 81);
                }
            }

            // Card 2: 赞赏支持卡片 (原始设计高度 432)
            const int card2Y = card1Y + card1H + 14; // 212
            const int card2H = 432;
            if (cardDonate != null)
            {
                cardDonate.Location = new Point(padX, card2Y);
                cardDonate.Size = new Size(cardWidth, card2H);
                cardDonate.CornerRadius = 8;
            }

            const int qrCardW = 176;
            const int qrCardH = 216;
            const int qrGap = 36;
            const int qrTotalW = qrCardW * 2 + qrGap; // 388
            const int qrStartX = (cardWidth - qrTotalW) / 2; // 82

            if (lblDonateTitle != null)
            {
                lblDonateTitle.Font = new Font("Microsoft YaHei UI", 9.8F, FontStyle.Bold);
                lblDonateTitle.Location = new Point(20, 16);
            }
            if (lblDonateDesc != null)
            {
                lblDonateDesc.Font = new Font("Microsoft YaHei UI", 8.8F);
                lblDonateDesc.Location = new Point(20, 42);
                lblDonateDesc.MaximumSize = new Size(cardWidth - 40, 0);
                lblDonateDesc.AutoSize = true;
                lblDonateDesc.BackColor = Color.Transparent;
            }

            // 动态流式布局：严格基于 lblDonateDesc 实际文本渲染底边界计算二维码卡片起始 Y 轴，彻底根绝重叠与截断
            int descBottom = (lblDonateDesc != null && lblDonateDesc.Height > 0) ? lblDonateDesc.Bottom : 64;
            int qrY = Math.Max(descBottom + 14, 76);

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

            int qrBottom = qrY + qrCardH;
            if (lnkQQ != null)
            {
                lnkQQ.Font = new Font("Microsoft YaHei UI", 8.8F);
                lnkQQ.Top = qrBottom + 16;
                CenterControlHorizontally(lnkQQ, cardWidth);
                lnkQQ.BringToFront();
            }
            if (lnkGithub != null)
            {
                lnkGithub.Font = new Font("Microsoft YaHei UI", 8.8F);
                lnkGithub.Top = (lnkQQ != null ? lnkQQ.Bottom : qrBottom + 16) + 8;
                CenterControlHorizontally(lnkGithub, cardWidth);
                lnkGithub.BringToFront();
            }

            // 3. 计算窗体与主容器总高度 (固定 600x668 客户端尺寸)
            const int containerH = 668;

            this.ClientSize = new Size(clientW, containerH);
            this.StartPosition = FormStartPosition.CenterScreen;

            if (flipContainer != null)
            {
                flipContainer.Location = new Point(0, 0);
                flipContainer.Size = new Size(clientW, containerH);
            }
            if (shatterCanvas != null)
            {
                shatterCanvas.Size = new Size(clientW, containerH);
            }
            if (pnlFront != null)
            {
                pnlFront.Size = new Size(clientW, containerH);
            }
            if (pnlBack != null)
            {
                pnlBack.Size = new Size(clientW, containerH);
            }
            if (cardQuote != null)
            {
                const int backCardY = 16;
                const int backCardH = containerH - (backCardY * 2) - 8; // 628
                cardQuote.Location = new Point(padX, backCardY);
                cardQuote.Size = new Size(cardWidth, backCardH);
                cardQuote.CornerRadius = 12;

                if (btnFlipBack != null)
                {
                    btnFlipBack.Font = new Font("Microsoft YaHei UI", 9.5F, FontStyle.Bold);
                    btnFlipBack.Size = new Size(160, 38);
                    btnFlipBack.CornerRadius = 6;
                    btnFlipBack.Location = new Point((cardWidth - btnFlipBack.Width) / 2, backCardH - 62);
                }
            }
        }

        protected override void OnApplyDpiScale(float scale)
        {
            base.OnApplyDpiScale(scale);
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
            int innerW = 152;
            int innerH = 152;
            int innerX = (w - innerW) / 2;
            int innerY = 10;
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

            using (Font titleFont = new Font("Microsoft YaHei UI", 9.0F, FontStyle.Bold))
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
                // 1. 旋转 RGB 色相递增 (真·全彩霓虹流光，每帧旋转)
                chromaHue += 1.6f;
                if (chromaHue >= 360f) chromaHue -= 360f;

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

                // 3. 鼠标星尘拖尾微粒演化 (空灵优雅消融，留存约 1.1 秒，绝无堆叠与毛毛虫感)
                for (int i = stardustTrail.Count - 1; i >= 0; i--)
                {
                    StardustParticle sp = stardustTrail[i];
                    sp.X += sp.Vx;
                    sp.Y += sp.Vy;
                    sp.Vx *= 0.985f;
                    sp.Vy = (sp.Vy - 0.008f) * 0.985f; // 轻柔如星尘缓慢微升
                    sp.Alpha -= 0.015f; // 优雅轻柔消散，存活约 1.1 秒 (46帧)
                    sp.Size *= 0.995f;
                    if (sp.Alpha <= 0f || sp.Size <= 0.4f)
                    {
                        stardustTrail.RemoveAt(i);
                    }
                }

                // 4. 水波纹扩散与衰减 (物理流体波包演进)
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

                // 7. 刷新界面呈现
                if (pnlFront.Visible && (hasFrontParticles || hasFrontRipples || hasHeartParticles))
                {
                    pnlFront.Invalidate();
                }


                if (pnlBack.Visible && cardQuote != null)
                {
                    cardQuote.Invalidate();
                }

                if (isChromaActive)
                {
                    this.Invalidate();
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
            // 空灵稀疏微光星尘拖尾：位移节流 22px、上限 12 颗、微小细腻 (1.8~2.8px)，绝不堆积成毛毛虫
            if (lastMouseTrailPos != Point.Empty)
            {
                float dx = e.X - lastMouseTrailPos.X;
                float dy = e.Y - lastMouseTrailPos.Y;
                if (dx * dx + dy * dy < 484f) // 22px 节流
                {
                    return;
                }
            }
            lastMouseTrailPos = e.Location;

            if (stardustTrail.Count >= 12)
            {
                stardustTrail.RemoveAt(0);
            }

            Color[] stardustColors = new Color[]
            {
                Color.FromArgb(100, 220, 255), // 晨曦极光蓝
                Color.FromArgb(120, 255, 200), // 灵脉浅翡翠
                Color.FromArgb(255, 235, 170), // 香槟星芒金
                Color.FromArgb(200, 180, 255)  // 梦幻薰衣草
            };

            StardustParticle sp = new StardustParticle();
            sp.X = e.X;
            sp.Y = e.Y;
            sp.Vx = (float)((rnd.NextDouble() - 0.5) * 0.3);
            sp.Vy = (float)(-rnd.NextDouble() * 0.35 - 0.1); // 轻柔如星尘缓慢微升
            sp.Size = (float)(rnd.NextDouble() * 1.0 + 1.8);  // 1.8 ~ 2.8px 极细星尘
            sp.Alpha = 0.70f;
            sp.Color = stardustColors[rnd.Next(stardustColors.Length)];
            stardustTrail.Add(sp);
        }

        private void CardQuote_MouseDown(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                // 鼠标在背面点击时，激发出静谧拟真水光微折射波纹 (Realistic Caustic Water Ripples)
                SpawnRealisticWaterRipple(waterRipples, e.X, e.Y, 130f, 2.5f);
            }
        }

        private void PnlFront_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;

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

        }

        private void CardQuote_Paint(object sender, PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.ClearTypeGridFit;

            int w = cardQuote.Width;
            int h = cardQuote.Height;

            Rectangle cardRect = new Rectangle(0, 0, w - 1, h - 1);
            using (GraphicsPath cardPath = RoundedCard.GetRoundedRectangle(cardRect, 12))
            {
                // 1. 深邃暗黑微晶底色
                using (LinearGradientBrush bgBrush = new LinearGradientBrush(cardRect, Color.FromArgb(16, 18, 24), Color.FromArgb(10, 12, 16), 90f))
                {
                    g.FillPath(bgBrush, cardPath);
                }

                // 2. 绘制背部浮动微光星河背景粒子
                for (int i = 0; i < backParticles.Count; i++)
                {
                    AmbientParticle p = backParticles[i];
                    int a = Math.Max(0, Math.Min(255, (int)(p.Alpha * 255)));
                    if (a > 0)
                    {
                        using (SolidBrush pb = new SolidBrush(Color.FromArgb(a, p.Color)))
                        {
                            g.FillEllipse(pb, p.X, p.Y, p.Size, p.Size);
                        }
                    }
                }

                // 3. 绘制鼠标交互拟真水波纹涟漪 (Realistic Caustic Water Ripples)
                for (int i = 0; i < waterRipples.Count; i++)
                {
                    WaterRipple wr = waterRipples[i];
                    DrawRealisticCausticRipple(g, wr.X, wr.Y, wr.Radius, wr.Alpha, wr.Wavelength, wr.Age);
                }

                // 4. 绘制鼠标移动星尘拖尾 (Luminous Stardust Trail: 柔和外晕 + 鲜艳星核 + 中心白光)
                for (int i = 0; i < stardustTrail.Count; i++)
                {
                    StardustParticle sp = stardustTrail[i];
                    int a = Math.Max(0, Math.Min(255, (int)(sp.Alpha * 255)));
                    if (a > 0)
                    {
                        // Tier 1: 外部弥散柔光晕 (Diffuse Aura)
                        int haloA = (int)(a * 0.35f);
                        if (haloA > 0)
                        {
                            float haloSize = sp.Size * 2.5f;
                            using (SolidBrush hb = new SolidBrush(Color.FromArgb(haloA, sp.Color)))
                            {
                                g.FillEllipse(hb, sp.X - haloSize / 2f, sp.Y - haloSize / 2f, haloSize, haloSize);
                            }
                        }

                        // Tier 2: 主体鲜艳星芒 (Core Stardust)
                        using (SolidBrush tb = new SolidBrush(Color.FromArgb(a, sp.Color)))
                        {
                            g.FillEllipse(tb, sp.X - sp.Size / 2f, sp.Y - sp.Size / 2f, sp.Size, sp.Size);
                        }

                        // Tier 3: 中心高亮纯白星辉 (White Hot Center)
                        int coreA = (int)(a * 0.90f);
                        if (coreA > 0 && sp.Size > 4.5f)
                        {
                            float coreSize = sp.Size * 0.42f;
                            using (SolidBrush cb = new SolidBrush(Color.FromArgb(coreA, 255, 255, 255)))
                            {
                                g.FillEllipse(cb, sp.X - coreSize / 2f, sp.Y - coreSize / 2f, coreSize, coreSize);
                            }
                        }
                    }
                }

                // 5. 《写在代码之外》克制本真排版自主渲染
                DrawPoeticManifesto(g, w, h);

                // 6. 绘制【真·物理级内聚霓虹柔光 (Inward Bloom Aura)】(向内深度羽化，绝无被控件裁剪缺陷)
                DrawInwardBloomBorder(g, cardRect, 12, chromaHue);
            }
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
            string[] preferred = new string[] { "STKaiti", "KaiTi", "华文楷体", "楷体", "STFangsong", "FangSong", "仿宋", "Microsoft YaHei UI Light", "Microsoft YaHei UI" };
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

            // 计算逐行漫生动效进度 (0.0 -> 1.0)
            float tTitle = Math.Max(0f, Math.Min(1f, elapsedSec / 0.45f));
            float tL1 = Math.Max(0f, Math.Min(1f, (elapsedSec - 0.25f) / 0.50f));
            float tL2 = Math.Max(0f, Math.Min(1f, (elapsedSec - 0.65f) / 0.50f));
            float tL3 = Math.Max(0f, Math.Min(1f, (elapsedSec - 1.05f) / 0.50f));
            float tSign = Math.Max(0f, Math.Min(1f, (elapsedSec - 1.45f) / 0.50f));

            float cx = offsetX + w / 2f;
            float maxAllowedTextW = w - 120f;

            using (StringFormat sfCenter = new StringFormat())
            using (StringFormat sfRight = new StringFormat())
            {
                sfCenter.Alignment = StringAlignment.Center;
                sfCenter.LineAlignment = StringAlignment.Center;
                sfCenter.FormatFlags |= StringFormatFlags.NoWrap;

                sfRight.Alignment = StringAlignment.Far;
                sfRight.LineAlignment = StringAlignment.Center;
                sfRight.FormatFlags |= StringFormatFlags.NoWrap;

                // 1. 主标题: 《写  在  代  码  之  外》
                int aTitle = (int)(alpha * (tTitle * tTitle * (3f - 2f * tTitle)));
                if (aTitle > 0)
                {
                    float floatTitle = (1f - tTitle) * 6f;
                    using (Font titleFont = GetFittedArtisticFont(g, "写  在  代  码  之  外", 14.5F, maxAllowedTextW, FontStyle.Regular))
                    {
                        DrawGlowString(g, "写  在  代  码  之  外", titleFont, Color.FromArgb(aTitle, 245, 250, 255), Color.FromArgb(Math.Min(aTitle, 140), 0, 230, 118), cx, offsetY + 60f, sfCenter, aTitle, floatTitle);
                    }

                    // 极细微光渐变分割线与中心晶体锚点
                    int divY = (int)(offsetY + 110);
                    int divW = 160;
                    int divX = (int)(offsetX + (w - divW) / 2);
                    int aLine = Math.Min(aTitle, 130);
                    using (LinearGradientBrush lineBrush = new LinearGradientBrush(
                        new Point(divX, divY), new Point(divX + divW, divY),
                        Color.FromArgb(0, 0, 230, 118), Color.FromArgb(aLine, 0, 230, 118)))
                    {
                        ColorBlend cb = new ColorBlend();
                        cb.Colors = new Color[] { Color.FromArgb(0, 0, 230, 118), Color.FromArgb(aLine, 0, 230, 118), Color.FromArgb(0, 0, 230, 118) };
                        cb.Positions = new float[] { 0f, 0.5f, 1f };
                        lineBrush.InterpolationColors = cb;

                        using (Pen linePen = new Pen(lineBrush, 1.0f))
                        {
                            g.DrawLine(linePen, divX, divY, divX + divW, divY);
                        }
                    }
                    int aDot = Math.Min(aTitle, 200);
                    using (SolidBrush dotBrush = new SolidBrush(Color.FromArgb(aDot, 240, 250, 255)))
                    {
                        PointF[] diamond = new PointF[]
                        {
                            new PointF(offsetX + w / 2f, divY - 3f),
                            new PointF(offsetX + w / 2f + 3f, divY),
                            new PointF(offsetX + w / 2f, divY + 3f),
                            new PointF(offsetX + w / 2f - 3f, divY)
                        };
                        g.FillPolygon(dotBrush, diamond);
                    }
                }

                // 2. 诗篇正文 (原版经典三句文案，采用现代诗篇句读分行，大字号清晰舒展排版，兼具张力与艺术呼吸感)
                // 第一段 (序章): 拂去所有冗余与喧嚣，\n只将最纯净的秩序赠予你。
                int aL1 = (int)(alpha * (tL1 * tL1 * (3f - 2f * tL1)));
                if (aL1 > 0)
                {
                    float floatL1 = (1f - tL1) * 5f;
                    using (Font f1 = GetFittedArtisticFont(g, "拂去所有冗余与喧嚣，", 13.0F, maxAllowedTextW, FontStyle.Regular))
                    {
                        DrawGlowString(g, "拂去所有冗余与喧嚣，", f1, Color.FromArgb(aL1, 235, 242, 255), Color.FromArgb(Math.Min(aL1, 90), 100, 210, 255), cx, offsetY + 140f, sfCenter, aL1, floatL1);
                        DrawGlowString(g, "只将最纯净的秩序赠予你。", f1, Color.FromArgb(aL1, 235, 242, 255), Color.FromArgb(Math.Min(aL1, 90), 100, 210, 255), cx, offsetY + 172f, sfCenter, aL1, floatL1);
                    }
                }

                // 第二段 (核心): 灵脉初醒，微光致意。(翡翠灵动脉冲高亮)
                int aL2 = (int)(alpha * (tL2 * tL2 * (3f - 2f * tL2)));
                if (aL2 > 0)
                {
                    float floatL2 = (1f - tL2) * 5f;
                    using (Font f2 = GetFittedArtisticFont(g, "灵脉初醒，微光致意。", 14.5F, maxAllowedTextW, FontStyle.Regular))
                    {
                        DrawGlowString(g, "灵脉初醒，微光致意。", f2, Color.FromArgb(aL2, 74, 222, 128), Color.FromArgb(Math.Min(aL2, 160), 0, 230, 118), cx, offsetY + 235f, sfCenter, aL2, floatL2);
                    }
                }

                // 第三段 (祝愿): 愿你在属于自己的战场上，\n永远专注、自由、无可阻挡。
                int aL3 = (int)(alpha * (tL3 * tL3 * (3f - 2f * tL3)));
                if (aL3 > 0)
                {
                    float floatL3 = (1f - tL3) * 5f;
                    using (Font f3 = GetFittedArtisticFont(g, "永远专注、自由、无可阻挡。", 13.0F, maxAllowedTextW, FontStyle.Regular))
                    {
                        DrawGlowString(g, "愿你在属于自己的战场上，", f3, Color.FromArgb(aL3, 235, 242, 255), Color.FromArgb(Math.Min(aL3, 90), 180, 215, 255), cx, offsetY + 298f, sfCenter, aL3, floatL3);
                        DrawGlowString(g, "永远专注、自由、无可阻挡。", f3, Color.FromArgb(aL3, 235, 242, 255), Color.FromArgb(Math.Min(aL3, 90), 180, 215, 255), cx, offsetY + 330f, sfCenter, aL3, floatL3);
                    }
                }

                // 3. 独立开发者署名 (置于文案右下方，大号香槟金斜体，右对齐锚定)
                int aSign = (int)(alpha * (tSign * tSign * (3f - 2f * tSign)));
                if (aSign > 0)
                {
                    float floatSign = (1f - tSign) * 5f;
                    using (Font fSign = GetFittedArtisticFont(g, "——   Ferris-echo", 12.0F, 220f, FontStyle.Italic))
                    {
                        DrawGlowString(g, "——   Ferris-echo", fSign, Color.FromArgb(aSign, 212, 175, 55), Color.FromArgb(Math.Min(aSign, 100), 255, 230, 150), offsetX + w - 68f, offsetY + 382f, sfRight, aSign, floatSign);
                    }
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
            if (pnlFront != null && !pnlFront.IsDisposed)
            {
                pnlFront.Invalidate();
            }

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

            // 激活全屏转场画布
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

                // 动效完毕：隐藏全屏画布，恢复目标面板
                shatterCanvas.Visible = false;
                if (isShatteringToBack)
                {
                    pnlBack.Visible = true;
                    pnlFront.Visible = false;
                    backOpenTime = DateTime.UtcNow;
                    ChimeSynthesizer.StartAmbientMelody();
                }
                else
                {
                    pnlFront.Visible = true;
                    pnlBack.Visible = false;
                    ChimeSynthesizer.StopAmbientMelody();
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

            // 全屏深空画布基底
            g.Clear(Color.FromArgb(18, 19, 23));

            int padX = 24;
            int cardW = w - padX * 2;
            int cardY = 16;
            int cardH = h - (cardY * 2) - 8;
            Rectangle bRect = new Rectangle(padX, cardY, cardW, cardH);

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
                            pgb.SurroundColors = new Color[] { Color.FromArgb(0, 18, 19, 23) };
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
                            subPgb.SurroundColors = new Color[] { Color.FromArgb(0, 18, 19, 23) };
                            g.FillPath(subPgb, subPath);
                        }
                    }
                }

                // 3. 背面诗篇信笺平滑渐显 (Cross-Dissolve: 0.35 -> 1.0)
                if (p >= 0.35f)
                {
                    float appearT = (p - 0.35f) / 0.65f;
                    float smoothAlpha = appearT * appearT * (3f - 2f * appearT);
                    int targetAlpha = Math.Max(0, Math.Min(255, (int)(smoothAlpha * 255)));

                    // 背面卡底微晶填充
                    using (GraphicsPath cardPath = RoundedCard.GetRoundedRectangle(bRect, 12))
                    {
                        using (SolidBrush bgB = new SolidBrush(Color.FromArgb((int)(smoothAlpha * 230), 16, 18, 24)))
                        {
                            g.FillPath(bgB, cardPath);
                        }
                    }

                    // 5 层物理内聚 Bloom 柔光 (文字不在此处提前全显，待转场结束由卡片生命周期优雅逐行展开)
                    DrawInwardBloomBorderWithAlpha(g, bRect, 12, chromaHue, smoothAlpha);
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
                        using (GraphicsPath cardPath = RoundedCard.GetRoundedRectangle(bRect, 12))
                        {
                            using (SolidBrush bgB = new SolidBrush(Color.FromArgb((int)(smoothBack * 230), 16, 18, 24)))
                            {
                                g.FillPath(bgB, cardPath);
                            }
                        }
                        DrawInwardBloomBorderWithAlpha(g, bRect, 12, chromaHue, smoothBack);
                        DrawPoeticManifestoWithAlpha(g, cardW, cardH, padX, cardY, backAlpha, 999f);
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

        private void BtnCheckUpdate_Click(object sender, EventArgs e)
        {
            var pending = UpdateManager.PendingUpdate;
            if (pending != null && UpdateManager.IsNewerVersion(pending.LatestVersion, AppVersion))
            {
                // 0ms 秒开：发现新版本直接从内存打开弹窗，弹窗内部 SWR 自行静默复检
                UpdateNoticeForm.ShowUpdateDialog(this, pending, AppVersion);
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
                        btnCheckUpdate.Text = "检查更新";
                        lblUpdateStatus.Text = "连接更新服务器失败，请检查网络后重试";
                        lblUpdateStatus.ForeColor = Color.FromArgb(255, 171, 0);
                        return;
                    }

                    if (info != null && UpdateManager.IsNewerVersion(info.LatestVersion, AppVersion))
                    {
                        UpdateAboutUpdateButtonState();
                        // 弹出新版本提示与更新对话框
                        UpdateNoticeForm.ShowUpdateDialog(this, info, AppVersion);
                    }
                    else
                    {
                        btnCheckUpdate.Text = "检查更新";
                        lblUpdateStatus.Text = "当前已是最新版本 (" + AppVersion + ")";
                        lblUpdateStatus.ForeColor = Color.FromArgb(0, 230, 118);
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
                // 窗体外框全彩流光
                Color c1 = ColorFromHsv(chromaHue, 0.85f, 1.0f);
                Color c2 = ColorFromHsv((chromaHue + 120f) % 360f, 0.85f, 1.0f);
                Color c3 = ColorFromHsv((chromaHue + 240f) % 360f, 0.85f, 1.0f);

                Rectangle borderRect = new Rectangle(0, 0, this.Width - 1, this.Height - 1);
                using (LinearGradientBrush brush = new LinearGradientBrush(borderRect, c1, c2, chromaHue))
                {
                    ColorBlend blend = new ColorBlend();
                    blend.Colors = new Color[] { c1, c2, c3, c1 };
                    blend.Positions = new float[] { 0f, 0.33f, 0.67f, 1.0f };
                    brush.InterpolationColors = blend;

                    using (Pen chromaPen = new Pen(brush, 2.0f))
                    {
                        g.DrawRectangle(chromaPen, borderRect);
                    }
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
