// ============================================================================
// 模块名称: ModernThemeHelper.cs
// 所属区域: UI/Theme (现代设计系统与主题渲染层)
// 核心职责:
//   提供 Windows 11 云母/沉浸式暗黑样式 DWM 设置、设备类型专属现代矢量图形绘制、
//   抗锯齿圆角几何生成以及矢量充电闪电图标渲染。
// 依赖关系: Models.DeviceCategory, NativeMethods
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、颜色值或矢量坐标时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（❌ 严禁 $"..." 插值，必须使用 string.Format 或 + 拼接）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Runtime.InteropServices;

namespace RazerBatteryTray
{
    public static class ModernThemeHelper
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_TRANSITIONS_FORCEDISABLED = 3;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1 = 19;
        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_BORDER_COLOR = 34;
        private const int DWMWA_CAPTION_COLOR = 35;
        private const int DWMWA_TEXT_COLOR = 36;

        [DllImport("uxtheme.dll", ExactSpelling = true, CharSet = CharSet.Unicode)]
        public static extern int SetWindowTheme(IntPtr hWnd, string pszSubAppName, string pszSubIdList);

        public static void ApplyModernWin11Theme(IntPtr hWnd)
        {
            try
            {
                int enableTransitions = 0; // 0 = 允许 Windows 原生 DWM 窗口动画 (不禁用)
                DwmSetWindowAttribute(hWnd, DWMWA_TRANSITIONS_FORCEDISABLED, ref enableTransitions, sizeof(int));

                int trueVal = 1;
                DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE, ref trueVal, sizeof(int));
                DwmSetWindowAttribute(hWnd, DWMWA_USE_IMMERSIVE_DARK_MODE_BEFORE_20H1, ref trueVal, sizeof(int));

                int roundVal = 2;
                DwmSetWindowAttribute(hWnd, DWMWA_WINDOW_CORNER_PREFERENCE, ref roundVal, sizeof(int));

                int captionBg = 0x00171312;
                DwmSetWindowAttribute(hWnd, DWMWA_CAPTION_COLOR, ref captionBg, sizeof(int));

                int captionText = 0x00FFFFFF;
                DwmSetWindowAttribute(hWnd, DWMWA_TEXT_COLOR, ref captionText, sizeof(int));

                int borderCol = 0x003A2E2A;
                DwmSetWindowAttribute(hWnd, DWMWA_BORDER_COLOR, ref borderCol, sizeof(int));
            }
            catch (Exception diagEx) { Diagnostics.Log("UI/Theme/ModernThemeHelper.cs: 已忽略异常", diagEx); }
        }

        public static GraphicsPath GetRoundedRectangle(Rectangle bounds, int radius)
        {
            GraphicsPath path = new GraphicsPath();
            int d = radius * 2;
            if (d > bounds.Width) d = bounds.Width;
            if (d > bounds.Height) d = bounds.Height;
            if (d <= 0) d = 1;

            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static GraphicsPath GetRoundRectF(RectangleF bounds, float radius)
        {
            GraphicsPath path = new GraphicsPath();
            float d = radius * 2;
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }

        public static void DrawVectorLightning(Graphics g, float cx, float cy, float height, Color color)
        {
            float hh = height / 2f;
            float hw = (height * 0.55f) / 2f;
            PointF[] pts = new PointF[]
            {
                new PointF(cx + 0.15f * hw, cy - hh),
                new PointF(cx - hw, cy + 0.05f * hh),
                new PointF(cx - 0.05f * hw, cy + 0.05f * hh),
                new PointF(cx - 0.4f * hw, cy + hh),
                new PointF(cx + hw, cy - 0.05f * hh),
                new PointF(cx + 0.05f * hw, cy - 0.05f * hh)
            };
            var prevSm = g.SmoothingMode;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (SolidBrush b = new SolidBrush(color))
            {
                g.FillPolygon(b, pts);
            }
            g.SmoothingMode = prevSm;
        }

        public static void DrawVectorLightning(Graphics g, RectangleF bounds, Color color)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float w = bounds.Width;
            float h = bounds.Height;
            float x = bounds.X;
            float y = bounds.Y;

            PointF[] pts = new PointF[]
            {
                new PointF(x + w * 0.58f, y),
                new PointF(x + w * 0.18f, y + h * 0.52f),
                new PointF(x + w * 0.48f, y + h * 0.52f),
                new PointF(x + w * 0.38f, y + h),
                new PointF(x + w * 0.82f, y + h * 0.44f),
                new PointF(x + w * 0.52f, y + h * 0.44f),
                new PointF(x + w * 0.62f, y)
            };

            using (GraphicsPath path = new GraphicsPath())
            {
                path.AddPolygon(pts);
                using (SolidBrush brush = new SolidBrush(color))
                {
                    g.FillPath(brush, path);
                }
            }
        }

        public static Color GetBatteryLevelColor(int percent, bool isCharging)
        {
            if (isCharging) return Color.FromArgb(0, 230, 118);
            if (percent > 50) return Color.FromArgb(0, 230, 118);
            if (percent > 20) return Color.FromArgb(255, 214, 0);
            return Color.FromArgb(255, 61, 0);
        }

        public static void DrawModernDeviceIcon(Graphics g, Rectangle bounds, DeviceCategory category, Color accent)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            float cx = bounds.X + bounds.Width / 2f;
            float cy = bounds.Y + bounds.Height / 2f;
            float dim = Math.Min(bounds.Width, bounds.Height);
            float s = dim / 24.0f;

            if (category == DeviceCategory.Headset)
            {
                using (Pen headPen = new Pen(Color.FromArgb(235, 240, 252), 1.8f * s))
                {
                    headPen.StartCap = LineCap.Round;
                    headPen.EndCap = LineCap.Round;
                    g.DrawArc(headPen, cx - 8.5f * s, cy - 8.5f * s, 17f * s, 17f * s, 180f, 180f);
                }
                using (SolidBrush cupBrush = new SolidBrush(accent))
                using (Pen cupPen = new Pen(Color.FromArgb(220, 240, 255), 1.1f * s))
                {
                    RectangleF leftCup = new RectangleF(cx - 10.5f * s, cy - 1f * s, 4f * s, 8.5f * s);
                    RectangleF rightCup = new RectangleF(cx + 6.5f * s, cy - 1f * s, 4f * s, 8.5f * s);
                    int cupR = Math.Max(1, (int)(2 * s));
                    using (GraphicsPath lp = GetRoundedRectangle(Rectangle.Round(leftCup), cupR))
                    using (GraphicsPath rp = GetRoundedRectangle(Rectangle.Round(rightCup), cupR))
                    {
                        g.FillPath(cupBrush, lp);
                        g.DrawPath(cupPen, lp);
                        g.FillPath(cupBrush, rp);
                        g.DrawPath(cupPen, rp);
                    }
                }
                using (SolidBrush dotBrush = new SolidBrush(Color.FromArgb(220, 255, 255, 255)))
                {
                    g.FillEllipse(dotBrush, cx - 1.2f * s, cy + 2f * s, 2.4f * s, 2.4f * s);
                }
            }
            else if (category == DeviceCategory.Earbuds)
            {
                using (SolidBrush earBg = new SolidBrush(Color.FromArgb(38, 44, 60)))
                using (Pen earPen = new Pen(Color.FromArgb(210, 225, 250), 1.2f * s))
                using (SolidBrush accBrush = new SolidBrush(accent))
                {
                    g.FillEllipse(earBg, cx - 8.5f * s, cy - 7f * s, 6.5f * s, 6.5f * s);
                    g.DrawEllipse(earPen, cx - 8.5f * s, cy - 7f * s, 6.5f * s, 6.5f * s);
                    using (GraphicsPath leftStem = GetRoundedRectangle(new Rectangle((int)(cx - 6.5f * s), (int)(cy - 2f * s), (int)(2.8f * s), (int)(8.5f * s)), (int)(1.2f * s)))
                    {
                        g.FillPath(earBg, leftStem);
                        g.DrawPath(earPen, leftStem);
                    }
                    g.FillEllipse(accBrush, cx - 5.5f * s, cy + 4.5f * s, 1.8f * s, 1.8f * s);

                    g.FillEllipse(earBg, cx + 2f * s, cy - 7f * s, 6.5f * s, 6.5f * s);
                    g.DrawEllipse(earPen, cx + 2f * s, cy - 7f * s, 6.5f * s, 6.5f * s);
                    using (GraphicsPath rightStem = GetRoundedRectangle(new Rectangle((int)(cx + 3.7f * s), (int)(cy - 2f * s), (int)(2.8f * s), (int)(8.5f * s)), (int)(1.2f * s)))
                    {
                        g.FillPath(earBg, rightStem);
                        g.DrawPath(earPen, rightStem);
                    }
                    g.FillEllipse(accBrush, cx + 4.7f * s, cy + 4.5f * s, 1.8f * s, 1.8f * s);
                }
            }
            else if (category == DeviceCategory.Keyboard)
            {
                Rectangle kbRect = new Rectangle((int)(cx - 9.5f * s), (int)(cy - 6.5f * s), (int)(19 * s), (int)(13 * s));
                int kbR = Math.Max(1, (int)(3 * s));
                using (GraphicsPath kbPath = GetRoundedRectangle(kbRect, kbR))
                {
                    using (SolidBrush kbBg = new SolidBrush(Color.FromArgb(32, 38, 52)))
                    {
                        g.FillPath(kbBg, kbPath);
                    }
                    using (Pen kbBorder = new Pen(Color.FromArgb(170, 185, 210), 1.2f * s))
                    {
                        g.DrawPath(kbBorder, kbPath);
                    }
                }
                using (SolidBrush keyBrush = new SolidBrush(Color.FromArgb(220, 230, 245)))
                {
                    g.FillRectangle(keyBrush, cx - 7f * s, cy - 4f * s, 2.5f * s, 2f * s);
                    g.FillRectangle(keyBrush, cx - 3f * s, cy - 4f * s, 2.5f * s, 2f * s);
                    g.FillRectangle(keyBrush, cx + 1f * s, cy - 4f * s, 2.5f * s, 2f * s);
                    g.FillRectangle(keyBrush, cx + 4.5f * s, cy - 4f * s, 2.5f * s, 2f * s);
                }
                using (SolidBrush keyBrush2 = new SolidBrush(Color.FromArgb(180, 195, 215)))
                {
                    g.FillRectangle(keyBrush2, cx - 7f * s, cy - 0.5f * s, 2.5f * s, 2f * s);
                    g.FillRectangle(keyBrush2, cx - 3f * s, cy - 0.5f * s, 2.5f * s, 2f * s);
                    g.FillRectangle(keyBrush2, cx + 1f * s, cy - 0.5f * s, 2.5f * s, 2f * s);
                    g.FillRectangle(keyBrush2, cx + 4.5f * s, cy - 0.5f * s, 2.5f * s, 2f * s);
                }
                using (SolidBrush spaceBrush = new SolidBrush(accent))
                {
                    g.FillRectangle(spaceBrush, cx - 5f * s, cy + 2.5f * s, 10f * s, 1.8f * s);
                }
            }
            else if (category == DeviceCategory.Gamepad)
            {
                Rectangle gpRect = new Rectangle((int)(cx - 9.5f * s), (int)(cy - 6f * s), (int)(19 * s), (int)(12 * s));
                int gpR = Math.Max(2, (int)(4 * s));
                using (GraphicsPath gpPath = GetRoundedRectangle(gpRect, gpR))
                {
                    using (SolidBrush gpBg = new SolidBrush(Color.FromArgb(32, 38, 52)))
                    {
                        g.FillPath(gpBg, gpPath);
                    }
                    using (Pen gpBorder = new Pen(Color.FromArgb(170, 185, 210), 1.2f * s))
                    {
                        g.DrawPath(gpBorder, gpPath);
                    }
                }
                using (SolidBrush dpad = new SolidBrush(Color.White))
                {
                    g.FillRectangle(dpad, cx - 6.2f * s, cy - 2.5f * s, 2f * s, 5.2f * s);
                    g.FillRectangle(dpad, cx - 7.8f * s, cy - 0.9f * s, 5.2f * s, 2f * s);
                }
                using (SolidBrush btnBrush = new SolidBrush(accent))
                {
                    float bRad = 1.1f * s;
                    g.FillEllipse(btnBrush, cx + 4.5f * s - bRad, cy - 2.5f * s - bRad, bRad * 2, bRad * 2);
                    g.FillEllipse(btnBrush, cx + 6.7f * s - bRad, cy - 0.3f * s - bRad, bRad * 2, bRad * 2);
                    g.FillEllipse(btnBrush, cx + 4.5f * s - bRad, cy + 1.9f * s - bRad, bRad * 2, bRad * 2);
                    g.FillEllipse(btnBrush, cx + 2.3f * s - bRad, cy - 0.3f * s - bRad, bRad * 2, bRad * 2);
                }
                using (SolidBrush homeBrush = new SolidBrush(Color.FromArgb(235, 240, 255)))
                {
                    g.FillEllipse(homeBrush, cx - 1f * s, cy - 1f * s, 2f * s, 2f * s);
                }
            }
            else if (category == DeviceCategory.Dongle)
            {
                RectangleF plugRect = new RectangleF(cx - 3.5f * s, cy - 8f * s, 7f * s, 4.5f * s);
                using (SolidBrush plugBg = new SolidBrush(Color.FromArgb(180, 195, 215)))
                using (Pen plugPen = new Pen(Color.FromArgb(230, 240, 255), 1.0f * s))
                {
                    g.FillRectangle(plugBg, plugRect);
                    g.DrawRectangle(plugPen, plugRect.X, plugRect.Y, plugRect.Width, plugRect.Height);
                }
                Rectangle bodyRect = new Rectangle((int)(cx - 6f * s), (int)(cy - 3.5f * s), (int)(12 * s), (int)(11.5f * s));
                int dongleR = Math.Max(1, (int)(3 * s));
                using (GraphicsPath bodyPath = GetRoundedRectangle(bodyRect, dongleR))
                {
                    using (SolidBrush bodyBg = new SolidBrush(Color.FromArgb(32, 38, 52)))
                    {
                        g.FillPath(bodyBg, bodyPath);
                    }
                    using (Pen bodyBorder = new Pen(Color.FromArgb(170, 185, 210), 1.2f * s))
                    {
                        g.DrawPath(bodyBorder, bodyPath);
                    }
                }
                using (SolidBrush ledBrush = new SolidBrush(accent))
                {
                    g.FillEllipse(ledBrush, cx - 1.5f * s, cy + 2f * s, 3f * s, 3f * s);
                }
                using (Pen wavePen = new Pen(Color.FromArgb(180, accent.R, accent.G, accent.B), 1.0f * s))
                {
                    g.DrawArc(wavePen, cx - 4f * s, cy - 0.5f * s, 8f * s, 8f * s, 200, 140);
                }
            }
            else if (category == DeviceCategory.Generic)
            {
                Rectangle genRect = new Rectangle((int)(cx - 7f * s), (int)(cy - 7f * s), (int)(14 * s), (int)(14 * s));
                int genR = Math.Max(1, (int)(3 * s));
                using (GraphicsPath genPath = GetRoundedRectangle(genRect, genR))
                {
                    using (SolidBrush genBg = new SolidBrush(Color.FromArgb(32, 38, 52)))
                    {
                        g.FillPath(genBg, genPath);
                    }
                    using (Pen genBorder = new Pen(Color.FromArgb(170, 185, 210), 1.2f * s))
                    {
                        g.DrawPath(genBorder, genPath);
                    }
                }
                using (SolidBrush coreBrush = new SolidBrush(accent))
                {
                    g.FillRectangle(coreBrush, cx - 2.5f * s, cy - 2.5f * s, 5f * s, 5f * s);
                }
                using (Pen pinPen = new Pen(Color.FromArgb(200, 220, 245), 1.1f * s))
                {
                    g.DrawLine(pinPen, cx - 4.5f * s, cy - 9f * s, cx - 4.5f * s, cy - 7f * s);
                    g.DrawLine(pinPen, cx + 4.5f * s, cy - 9f * s, cx + 4.5f * s, cy - 7f * s);
                    g.DrawLine(pinPen, cx - 4.5f * s, cy + 7f * s, cx - 4.5f * s, cy + 9f * s);
                    g.DrawLine(pinPen, cx + 4.5f * s, cy + 7f * s, cx + 4.5f * s, cy + 9f * s);
                }
            }
            else // Mouse or default
            {
                Rectangle mRect = new Rectangle((int)(cx - 5.5f * s), (int)(cy - 8.5f * s), (int)(11 * s), (int)(17 * s));
                int mR = Math.Max(2, (int)(5 * s));
                using (GraphicsPath mPath = GetRoundedRectangle(mRect, mR))
                {
                    using (SolidBrush mBg = new SolidBrush(Color.FromArgb(32, 38, 52)))
                    {
                        g.FillPath(mBg, mPath);
                    }
                    using (Pen mBorder = new Pen(Color.FromArgb(170, 185, 210), 1.2f * s))
                    {
                        g.DrawPath(mBorder, mPath);
                    }
                }
                using (Pen slitPen = new Pen(Color.FromArgb(110, 125, 150), 1f * s))
                {
                    g.DrawLine(slitPen, cx, cy - 8f * s, cx, cy - 3f * s);
                }
                RectangleF wheelRect = new RectangleF(cx - 1f * s, cy - 6.5f * s, 2f * s, 4.5f * s);
                using (SolidBrush wheelBrush = new SolidBrush(accent))
                {
                    g.FillRectangle(wheelBrush, wheelRect);
                }
            }
        }
    }
}
