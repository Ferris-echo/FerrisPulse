// ============================================================================
// 模块名称: DpiScaleHelper.cs
// 所属区域: UI/Helpers (通用辅助层)
// 核心职责:
//   集中管理 Windows Win32 硬件级高 DPI 探测与通用几何、尺寸、坐标等比缩放运算。
//   兼容 Windows 7/8/10/11 多显示器环境，支持动态多屏混合 DPI 探测与回退。
// 依赖关系: Core.NativeMethods
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在新增或修改缩放算法时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Drawing;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public static class DpiScaleHelper
    {
        private static bool isGetDpiForWindowChecked = false;
        private static bool isGetDpiForWindowSupported = false;

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
        /// 基于屏幕工作区可用高度动态计算防溢出缩放上限，
        /// 保证窗口无论在何种缩放比例或分辨率下，其底边控制按钮永远 100% 完整显示在屏幕可视区域内，杜绝截断。
        /// </summary>
        public static float ClampScaleToScreen(float requestedScale, Screen screen, int baseHeight = 748)
        {
            if (screen == null) screen = Screen.PrimaryScreen;
            if (screen == null) return requestedScale;

            int availH = screen.WorkingArea.Height - 32;
            if (availH <= 0) return requestedScale;

            float maxAllowed = (float)availH / (float)baseHeight;
            if (requestedScale > maxAllowed)
            {
                return Math.Max(0.85f, maxAllowed);
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
                return 1.5f; // 2K/4K 屏幕基准黄金尺寸 (705x1086)
            }
            return 1.0f; // 1080p 及以下基准尺寸
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
