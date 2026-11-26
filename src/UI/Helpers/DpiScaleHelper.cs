// ============================================================================
// 模块名称: DpiScaleHelper.cs
// 所属区域: UI/Helpers (通用辅助层)
// 核心职责:
//   集中管理 Windows Win32 硬件级高 DPI 探测、无极窗口缩放边界钳制、
//   跨系统 DPI 等比矢量字号换算、GDI 字体缓存池及控件树递归字体缩放运算。
//   兼容 Windows 7/8/10/11 多显示器环境，支持动态多屏混合 DPI 探测与回退。
// 依赖关系: Core.NativeMethods
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在新增或修改缩放算法时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public static class DpiScaleHelper
    {
        public const float MinScale = 0.60f;
        public const float MaxScale = 2.50f;
        public const int DefaultExpandedBaseHeight = 880;

        private static bool isGetDpiForWindowChecked = false;
        private static bool isGetDpiForWindowSupported = false;

        private sealed class BaseFontMetadata
        {
            public string FamilyName;
            public float BasePointSize;
            public FontStyle Style;
        }

        private static readonly ConditionalWeakTable<Control, BaseFontMetadata> baseFontTable =
            new ConditionalWeakTable<Control, BaseFontMetadata>();

        private static readonly Dictionary<string, Font> cachedControlFonts =
            new Dictionary<string, Font>();

        public static float GetDpiScaleForWindow(IntPtr hWnd)
        {
            try
            {
                if (!isGetDpiForWindowChecked)
                {
                    IntPtr hUser32 = NativeMethods.GetModuleHandle("user32.dll");
                    if (hUser32 != IntPtr.Zero)
                    {
                        IntPtr pProc = NativeMethods.GetProcAddress(hUser32, "GetDpiForWindow");
                        isGetDpiForWindowSupported = (pProc != IntPtr.Zero);
                    }
                    isGetDpiForWindowChecked = true;
                }

                if (isGetDpiForWindowSupported && hWnd != IntPtr.Zero)
                {
                    uint dpi = NativeMethods.GetDpiForWindow(hWnd);
                    if (dpi > 0)
                    {
                        float scale = dpi / 96.0f;
                        if (scale < 1.0f) scale = 1.0f;
                        return scale;
                    }
                }
            }
            catch { }

            // 回退到 GDI+ 屏幕设备上下文探测
            try
            {
                using (Graphics g = Graphics.FromHwnd(hWnd != IntPtr.Zero ? hWnd : IntPtr.Zero))
                {
                    float scale = g.DpiX / 96.0f;
                    if (scale < 1.0f) scale = 1.0f;
                    return scale;
                }
            }
            catch
            {
                return 1.0f;
            }
        }

        /// <summary>
        /// 基于屏幕工作区可用高度动态计算防溢出缩放上限（支持低分屏自适应下探至 0.60x），
        /// 保证窗口无论在何种缩放比例或分辨率（含 720p/768p 及 1080p@150%）下，
        /// 即使全屏游戏优化卡片展开，底边控制按钮也永远 100% 完整显示在屏幕可视区域内，杜绝截断。
        /// </summary>
        public static float ClampScaleToScreen(float requestedScale, Screen screen, int baseHeight = DefaultExpandedBaseHeight)
        {
            if (requestedScale < MinScale) requestedScale = MinScale;
            if (requestedScale > MaxScale) requestedScale = MaxScale;

            if (screen == null) screen = Screen.PrimaryScreen;
            if (screen == null) return requestedScale;

            if (baseHeight <= 0) baseHeight = DefaultExpandedBaseHeight;

            int availH = screen.WorkingArea.Height - 28;
            if (availH <= 0) return requestedScale;

            float maxAllowed = (float)availH / (float)baseHeight;
            int availW = screen.WorkingArea.Width - 24;
            if (availW > 0)
            {
                float maxAllowedW = (float)availW / 506.0f;
                if (maxAllowedW < maxAllowed) maxAllowed = maxAllowedW;
            }

            if (requestedScale > maxAllowed)
            {
                float clamped = Math.Max(MinScale, Math.Min(MaxScale, maxAllowed));
                return (float)Math.Round(clamped, 3);
            }
            return requestedScale;
        }

        /// <summary>
        /// 基于显示器物理分辨率获取默认基准缩放比。
        /// 针对 2K/4K 屏幕默认统一锚定为 1.5x 黄金尺寸，绝不强制膨胀为 2.0x。
        /// </summary>
        public static float GetOptimalScaleForScreen(Screen screen)
        {
            if (screen == null) screen = Screen.PrimaryScreen;
            if (screen == null) return 1.5f;

            int minDim = Math.Min(screen.Bounds.Width, screen.Bounds.Height);
            if (minDim >= 1300)
            {
                return 1.5f; // 2K/4K 屏幕基准黄金尺寸
            }
            return 1.0f; // 1080p 及以下基准尺寸
        }

        /// <summary>
        /// 计算跨系统物理 DPI 的等效矢量字号：
        /// Windows GDI+ 会将 Point 单位字体自动乘以 systemDpiScale 渲染，
        /// 因此通过 basePointSize * (targetScale / systemDpiScale) 换算，
        /// 可确保无论系统 DPI 如何、用户无极缩放比例如何，文字像素大小始终与窗口几何尺寸严格 1:1 等比缩放。
        /// </summary>
        public static float GetScaledFontSize(float basePointSize, float targetScale, float systemDpiScale = 0f)
        {
            if (systemDpiScale <= 0.01f)
            {
                systemDpiScale = GetDpiScaleForWindow(IntPtr.Zero);
            }
            if (targetScale < MinScale) targetScale = MinScale;
            if (targetScale > MaxScale) targetScale = MaxScale;

            float ratio = targetScale / systemDpiScale;
            float scaledPt = basePointSize * ratio;
            float minPt = Math.Max(2.0f, 3.6f / systemDpiScale);
            if (scaledPt < minPt) scaledPt = minPt;
            return (float)Math.Round(scaledPt, 2);
        }

        public static Font CreateScaledFont(string familyName, float basePointSize, FontStyle style, float targetScale, float systemDpiScale = 0f)
        {
            if (string.IsNullOrEmpty(familyName)) familyName = "Microsoft YaHei UI";
            float pt = GetScaledFontSize(basePointSize, targetScale, systemDpiScale);
            return new Font(familyName, pt, style, GraphicsUnit.Point);
        }

        public static Font GetCachedScaledFont(string familyName, float basePointSize, FontStyle style, float targetScale, float systemDpiScale = 0f)
        {
            if (string.IsNullOrEmpty(familyName)) familyName = "Microsoft YaHei UI";
            float pt = GetScaledFontSize(basePointSize, targetScale, systemDpiScale);
            float qPt = (float)(Math.Round(pt * 20.0f) / 20.0f);
            if (qPt < 2.0f) qPt = 2.0f;

            string key = familyName + "|" + qPt.ToString("F2", CultureInfo.InvariantCulture) + "|" + ((int)style).ToString(CultureInfo.InvariantCulture);
            lock (cachedControlFonts)
            {
                Font f;
                if (cachedControlFonts.TryGetValue(key, out f) && f != null)
                {
                    try
                    {
                        float check = f.SizeInPoints;
                        if (check > 0f) return f;
                    }
                    catch
                    {
                        cachedControlFonts.Remove(key);
                    }
                }
                f = new Font(familyName, qPt, style, GraphicsUnit.Point);
                cachedControlFonts[key] = f;
                return f;
            }
        }

        /// <summary>
        /// 显式更新某控件的 1.0x 基准字号元数据并立即应用当前缩放比例字号（用于动态切换大字号/中字号的标签如 lblBatteryBig）。
        /// </summary>
        public static void SetControlBaseFont(Control ctrl, string familyName, float basePointSize, FontStyle style, float targetScale, float systemDpiScale = 0f)
        {
            if (ctrl == null) return;
            if (string.IsNullOrEmpty(familyName)) familyName = "Microsoft YaHei UI";
            if (systemDpiScale <= 0.01f)
            {
                IntPtr hWnd = ctrl.IsHandleCreated ? ctrl.Handle : IntPtr.Zero;
                systemDpiScale = GetDpiScaleForWindow(hWnd);
            }

            try
            {
                BaseFontMetadata meta;
                if (!baseFontTable.TryGetValue(ctrl, out meta))
                {
                    meta = new BaseFontMetadata();
                    baseFontTable.Add(ctrl, meta);
                }
                meta.FamilyName = familyName;
                meta.BasePointSize = basePointSize;
                meta.Style = style;

                Font targetFont = GetCachedScaledFont(familyName, basePointSize, style, targetScale, systemDpiScale);
                if (!object.ReferenceEquals(ctrl.Font, targetFont))
                {
                    ctrl.Font = targetFont;
                }
            }
            catch { }
        }

        /// <summary>
        /// 递归遍历控件树，首次遇到控件时缓存其初始 1.0x 基准字号，
        /// 随后根据 targetScale 与 systemDpiScale 无损重设控件字号，消除多次缩放累积舍入误差。
        /// </summary>
        public static void ApplyScaledFontsToControlTree(Control root, float targetScale, float systemDpiScale = 0f)
        {
            if (root == null) return;
            if (systemDpiScale <= 0.01f)
            {
                IntPtr hWnd = root.IsHandleCreated ? root.Handle : IntPtr.Zero;
                systemDpiScale = GetDpiScaleForWindow(hWnd);
            }

            ApplyScaledFontRecursive(root, targetScale, systemDpiScale);
        }

        private static void ApplyScaledFontRecursive(Control ctrl, float targetScale, float systemDpiScale)
        {
            if (ctrl == null) return;

            try
            {
                bool isContainerWithChildren = (ctrl is Form || ctrl is Panel) && ctrl.Controls != null && ctrl.Controls.Count > 0;
                Font curFont = isContainerWithChildren ? null : ctrl.Font;
                if (curFont != null)
                {
                    BaseFontMetadata meta;
                    if (!baseFontTable.TryGetValue(ctrl, out meta))
                    {
                        meta = new BaseFontMetadata();
                        meta.FamilyName = curFont.FontFamily != null ? curFont.FontFamily.Name : "Microsoft YaHei UI";
                        meta.BasePointSize = curFont.SizeInPoints;
                        meta.Style = curFont.Style;
                        baseFontTable.Add(ctrl, meta);
                    }

                    Font targetFont = GetCachedScaledFont(meta.FamilyName, meta.BasePointSize, meta.Style, targetScale, systemDpiScale);
                    if (!object.ReferenceEquals(ctrl, null) && !object.ReferenceEquals(curFont, targetFont) && Math.Abs(curFont.SizeInPoints - targetFont.SizeInPoints) > 0.04f)
                    {
                        ctrl.Font = targetFont;
                    }
                }
            }
            catch { }

            if (ctrl.Controls != null && ctrl.Controls.Count > 0)
            {
                for (int i = 0; i < ctrl.Controls.Count; i++)
                {
                    ApplyScaledFontRecursive(ctrl.Controls[i], targetScale, systemDpiScale);
                }
            }
        }

        public static int Scale(int value, float scale)
        {
            return (int)Math.Round(value * scale);
        }

        public static Size Scale(Size size, float scale)
        {
            return new Size((int)Math.Round(size.Width * scale), (int)Math.Round(size.Height * scale));
        }

        public static Point Scale(Point pt, float scale)
        {
            return new Point((int)Math.Round(pt.X * scale), (int)Math.Round(pt.Y * scale));
        }

        public static Rectangle Scale(Rectangle rect, float scale)
        {
            return new Rectangle(
                (int)Math.Round(rect.X * scale),
                (int)Math.Round(rect.Y * scale),
                (int)Math.Round(rect.Width * scale),
                (int)Math.Round(rect.Height * scale));
        }
    }
}
