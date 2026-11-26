// ============================================================================
// 模块名称: ThreeLocksHudControl.cs
// 所属区域: UI/Controls (高级功能控件层)
// 核心职责:
//   键盘三锁（Caps Lock 大写锁定、Num Lock 数字小键盘、Scroll Lock 滚屏锁定）状态 HUD 监控指示条，
//   支持 150ms 轮询感知物理键盘锁定状态并在界面高亮激活，支持无极等比缩放。
// 依赖关系: RoundedCard, Helpers.DpiScaleHelper
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、属性或绘图逻辑时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（❌ 严禁 $"..." 插值，必须使用 string.Format 或 + 拼接）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class ThreeLocksHudControl : Control
    {
        public float DpiScale = 1.0f;
        private Timer pollTimer;
        private bool capsOn, numOn, scrollOn;

        public ThreeLocksHudControl()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.SupportsTransparentBackColor, true);
            BackColor = Color.Transparent;
            DoubleBuffered = true;
            pollTimer = new Timer();
            pollTimer.Interval = 150;
            pollTimer.Tick += (s, e) => CheckStatus();
            pollTimer.Start();
            CheckStatus();
        }

        public void CheckStatus()
        {
            bool c = Control.IsKeyLocked(Keys.CapsLock);
            bool n = Control.IsKeyLocked(Keys.NumLock);
            bool sc = Control.IsKeyLocked(Keys.Scroll);
            if (c != capsOn || n != numOn || sc != scrollOn)
            {
                capsOn = c;
                numOn = n;
                scrollOn = sc;
                Invalidate();
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && pollTimer != null)
            {
                pollTimer.Stop();
                pollTimer.Dispose();
                pollTimer = null;
            }
            base.Dispose(disposing);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.ClearTypeGridFit;

            int pad = (int)(2 * DpiScale);
            int gap = (int)(6 * DpiScale);
            int w = (Width - (pad * 2) - (gap * 2)) / 3;
            int h = Height - (pad * 2);

            DrawPill(g, new Rectangle(pad, pad, w, h), "🔤 大写锁定", capsOn);
            DrawPill(g, new Rectangle(pad + w + gap, pad, w, h), "🔢 数字小键盘", numOn);
            DrawPill(g, new Rectangle(pad + (w + gap) * 2, pad, w, h), "📜 滚屏锁定", scrollOn);
        }

        private void DrawPill(Graphics g, Rectangle rect, string text, bool isOn)
        {
            int r = Math.Max(2, (int)(5 * DpiScale));
            Color bg = isOn ? Color.FromArgb(40, 0, 230, 118) : Color.FromArgb(24, 28, 38);
            Color border = isOn ? Color.FromArgb(0, 230, 118) : Color.FromArgb(45, 52, 68);
            Color fg = isOn ? Color.FromArgb(0, 230, 118) : Color.FromArgb(120, 130, 150);

            using (var path = RoundedCard.GetRoundedRectangle(rect, r))
            {
                using (var b = new SolidBrush(bg)) g.FillPath(b, path);
                using (var p = new Pen(border, 1f)) g.DrawPath(p, path);
            }

            string fullText = text + (isOn ? " ● 开启" : " ○ 关闭");
            using (Font f = DpiScaleHelper.CreateScaledFont("Microsoft YaHei UI", 8.0F, isOn ? FontStyle.Bold : FontStyle.Regular, DpiScale))
            {
                TextRenderer.DrawText(g, fullText, f, rect, fg,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.NoPadding);
            }
        }
    }
}
