// ============================================================================
// 模块名称: RefreshRingControl.cs
// 所属区域: UI/Controls (高级功能控件层)
// 核心职责:
//   设备状态刷新态环形加载条：25FPS 自绘渐变彗尾圆弧，展示"正在获取最新设备状态"。
//   生命周期由外部驱动（BeginSpinning / CompleteSpinning / AbortSpinning），
//   控件内部自带【最短展示 600ms】与【200ms 淡出】，杜绝扫描快于一次视觉感知时
//   的"闪一下"观感；被抑制/超时时 AbortSpinning 立即收口，绝不留永转的环。
//   定时器严格按需启停：宿主窗体隐藏或最小化时自动停表并归零，绝不空转耗电。
// 依赖关系: 无
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、属性或绘图逻辑时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（❌ 严禁 $"..." 插值，必须使用 string.Format 或 + 拼接）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class RefreshRingControl : Control
    {
        // 25FPS：System.Windows.Forms.Timer 分辨率约 15.6ms，追求 60FPS 只会
        // 白烧 CPU 并和主窗体 15ms 的卡片动效定时器互相抖动，40ms 视觉已足够顺滑。
        internal const int AnimIntervalMs = 40;
        // 最短展示时长：实测单次扫描最快 344ms，短于此值的环会被当成"闪一下"故障。
        internal const int MinVisibleMs = 600;
        // 收口淡出时长：避免硬切造成的突兀。
        internal const int FadeOutMs = 200;
        // 彗尾分段数：20 段 × 每帧一次 DrawArc，成本仍在可忽略量级，
        // 但分段必须够密且用平头衔尾（round cap 会把每段画成圆珠，出串珠感）。
        private const int TailSegments = 20;
        // 单圈周期 1000ms（相位 0..1 对应 360 度）
        private const float RevolutionsPerSecond = 1.0f;

        private readonly Timer animTimer;
        private bool spinning;
        private bool fading;
        private bool stopRequested;
        private long spinStartedMs;
        private long fadeStartedMs;
        private long lastTickMs;
        private float phase;
        private float fadeAlpha = 1f;

        private Color accentColor = Color.FromArgb(0, 230, 118);
        private Color trackColor = Color.FromArgb(44, 50, 62);

        public Color AccentColor
        {
            get { return accentColor; }
            set { accentColor = value; Invalidate(); }
        }

        public Color TrackColor
        {
            get { return trackColor; }
            set { trackColor = value; Invalidate(); }
        }

        /// <summary>是否处于转动/淡出状态（测试探针亦读取该值）。</summary>
        internal bool IsAnimating { get { return spinning; } }

        /// <summary>是否已进入收口淡出阶段（测试探针读取）。</summary>
        internal bool IsFading { get { return fading; } }

        public RefreshRingControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint |
                     ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            DoubleBuffered = true;
            BackColor = Color.FromArgb(25, 27, 34);
            Visible = false;

            animTimer = new Timer();
            animTimer.Interval = AnimIntervalMs;
            animTimer.Tick += (s, e) => OnAnimTick();
        }

        #region 生命周期（全部由宿主在 UI 线程调用）

        /// <summary>开始（或维持）转动。已在转动时只撤销待停止标记，绝不重置相位——连续性靠这个保证。</summary>
        public void BeginSpinning()
        {
            if (IsHostHidden()) return;

            if (!spinning)
            {
                spinning = true;
                fading = false;
                stopRequested = false;
                fadeAlpha = 1f;
                spinStartedMs = NowMs();
                lastTickMs = spinStartedMs;
                if (!Visible) Visible = true;
            }
            else
            {
                stopRequested = false;
                if (fading)
                {
                    fading = false;
                    fadeAlpha = 1f;
                }
            }

            if (!animTimer.Enabled) animTimer.Start();
            Invalidate();
        }

        /// <summary>数据已落地：进入收口。未满最短展示时长时先继续转，到点再淡出。</summary>
        public void CompleteSpinning()
        {
            if (!spinning || fading) return;
            stopRequested = true;
            if (NowMs() - spinStartedMs >= MinVisibleMs)
            {
                BeginFade();
            }
        }

        /// <summary>强制性收口（刷新被抑制 / 超时兜底）：立即淡出，不等最短展示。</summary>
        public void AbortSpinning()
        {
            if (!spinning) return;
            stopRequested = true;
            BeginFade();
        }

        #endregion

        #region 动画推进

        private void OnAnimTick()
        {
            if (IsHostHidden())
            {
                StopAndHide();
                return;
            }

            long now = NowMs();
            int delta = (int)(now - lastTickMs);
            if (delta < 0) delta = 0;
            if (delta > 500) delta = 500; // 系统休眠/卡顿后不要一次性跳大角度
            lastTickMs = now;

            if (fading)
            {
                float p = (float)((double)(now - fadeStartedMs) / FadeOutMs);
                if (p >= 1f)
                {
                    StopAndHide();
                    return;
                }
                fadeAlpha = 1f - p;
                if (fadeAlpha < 0f) fadeAlpha = 0f;
                Invalidate();
                return;
            }

            phase += ((float)delta / 1000f) * RevolutionsPerSecond;
            if (phase >= 1f) phase -= (float)Math.Floor(phase);

            if (stopRequested && now - spinStartedMs >= MinVisibleMs)
            {
                BeginFade();
            }

            Invalidate();
        }

        private void BeginFade()
        {
            if (fading) return;
            fading = true;
            fadeStartedMs = NowMs();
            if (!animTimer.Enabled) animTimer.Start();
        }

        private void StopAndHide()
        {
            spinning = false;
            fading = false;
            stopRequested = false;
            fadeAlpha = 1f;
            animTimer.Stop();
            lastTickMs = NowMs();
            if (Visible) Visible = false;
        }

        /// <summary>宿主窗体不可见/最小化时视为不该占用定时器。</summary>
        private bool IsHostHidden()
        {
            Form host = this.FindForm();
            if (host == null) return false;
            return !host.Visible || host.WindowState == FormWindowState.Minimized;
        }

        private static long NowMs()
        {
            return DateTime.UtcNow.Ticks / 10000L;
        }

        /// <summary>测试探针：把内部时间轴整体前移，用于验证"最短展示/淡出"时间线，不改变生产路径。</summary>
        internal void ShiftClockForTest(int ms)
        {
            spinStartedMs -= ms;
            fadeStartedMs -= ms;
            lastTickMs -= ms;
        }

        #endregion

        #region 绘制

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;

            // 先铺底色：本控件挂在圆角卡片内部，实色底比 Transparent 少一条
            // 每帧"请求父级重绘"的链路，且卡片底色固定，不存在色差风险。
            using (SolidBrush bg = new SolidBrush(this.BackColor))
            {
                g.FillRectangle(bg, this.ClientRectangle);
            }

            int w = this.Width;
            int h = this.Height;
            if (w < 8 || h < 8) return;

            g.SmoothingMode = SmoothingMode.AntiAlias;

            float thickness = w * 0.13f;
            if (thickness < 2f) thickness = 2f;
            float diameter = Math.Min(w, h) - thickness - 2f;
            if (diameter <= 1f) return;
            RectangleF rect = new RectangleF((w - diameter) / 2f, (h - diameter) / 2f, diameter, diameter);

            int alpha = (int)(255f * fadeAlpha);
            if (alpha <= 0) return;

            using (Pen track = new Pen(Color.FromArgb(alpha * 35 / 100, trackColor), thickness))
            {
                g.DrawEllipse(track, rect);
            }

            // 彗尾：以头部为基准反向叠 TailSegments 段，透明度按 1.6 次方衰减。
            // 每段多画 1.2 度互相压边，配合平头线帽消除段间缝与串珠感。
            float headAngle = phase * 360f;
            float segSweep = 265f / TailSegments;
            for (int i = 0; i < TailSegments; i++)
            {
                float weight = 1f - ((float)i / TailSegments);
                int sa = (int)(alpha * (0.08f + 0.92f * (float)Math.Pow(weight, 1.6)));
                if (sa <= 0) continue;
                using (Pen seg = new Pen(Color.FromArgb(sa, accentColor), thickness))
                {
                    seg.StartCap = LineCap.Flat;
                    seg.EndCap = LineCap.Flat;
                    g.DrawArc(seg, rect, headAngle - (i + 1) * segSweep, segSweep + 1.2f);
                }
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && animTimer != null)
            {
                try { animTimer.Stop(); } catch { }
                animTimer.Dispose();
            }
            base.Dispose(disposing);
        }

        #endregion
    }
}
