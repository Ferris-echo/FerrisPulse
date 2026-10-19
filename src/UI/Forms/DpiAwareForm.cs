// ============================================================================
// 模块名称: DpiAwareForm.cs
// 所属区域: UI/Forms (统一窗体基类)
// 核心职责:
//   全软件统一的高 DPI 自适应感知窗体基类：
//   1. 强制关闭 WinForms 默认非线性字体拉伸 (AutoScaleMode = AutoScaleMode.None)
//   2. 初始化时基于物理屏幕硬件句柄自动探测高 DPI 缩放比 (100%~300%)
//   3. 原生拦截 Windows 跨屏拖拽 WM_DPICHANGED 动态响应消息并更新布局与重绘
//   4. 统一内置 Windows 11 暗黑 Mica / Acrylic 原生圆角与防眩光主题
//   5. 为所有子类提供统一的 OnApplyDpiScale 生命周期钩子与 Scale 等比几何工具
// 依赖关系: Helpers.DpiScaleHelper, Helpers.ModernThemeHelper, Core.NativeMethods
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改窗体基类逻辑时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public class DpiAwareForm : Form
    {
        protected float dpiScale = 1.0f;
        private bool isHandleCreatedDpiProbed = false;

        public float DpiScale
        {
            get { return dpiScale; }
            protected set
            {
                if (value < 1.0f) value = 1.0f;
                dpiScale = value;
            }
        }

        public DpiAwareForm() : this(1.0f)
        {
        }

        public DpiAwareForm(float initialScale)
        {
            this.AutoScaleMode = AutoScaleMode.None;
            if (initialScale >= 1.0f)
            {
                this.dpiScale = initialScale;
            }
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ModernThemeHelper.ApplyModernWin11Theme(this.Handle);

            if (!isHandleCreatedDpiProbed)
            {
                if (this.dpiScale <= 1.0f)
                {
                    float probedScale = DpiScaleHelper.GetDpiScaleForWindow(this.Handle);
                    if (probedScale >= 1.0f)
                    {
                        this.dpiScale = probedScale;
                    }
                }
                Screen scrn = Screen.FromHandle(this.Handle);
                if (scrn != null)
                {
                    this.dpiScale = DpiScaleHelper.ClampScaleToScreen(this.dpiScale, scrn, 700);
                }
                isHandleCreatedDpiProbed = true;
                OnApplyDpiScale(this.dpiScale);
            }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == NativeMethods.WM_DPICHANGED)
            {
                int newDpiX = m.WParam.ToInt32() & 0xFFFF;
                float newScale = newDpiX / 96.0f;
                if (newScale < 1.0f) newScale = 1.0f;

                NativeMethods.RECT suggestedRect = (NativeMethods.RECT)Marshal.PtrToStructure(m.LParam, typeof(NativeMethods.RECT));
                Rectangle suggestedBounds = Rectangle.FromLTRB(suggestedRect.Left, suggestedRect.Top, suggestedRect.Right, suggestedRect.Bottom);

                Screen currentScreen = Screen.FromRectangle(!suggestedBounds.IsEmpty ? suggestedBounds : this.Bounds);
                newScale = DpiScaleHelper.ClampScaleToScreen(newScale, currentScreen, 700);

                HandleDpiChangedInternal(newScale, suggestedBounds);
                m.Result = IntPtr.Zero;
                return;
            }

            base.WndProc(ref m);
        }

        private void HandleDpiChangedInternal(float newScale, Rectangle suggestedBounds)
        {
            float oldScale = this.dpiScale;
            this.dpiScale = newScale;

            if (!suggestedBounds.IsEmpty)
            {
                this.SetBounds(suggestedBounds.Left, suggestedBounds.Top, suggestedBounds.Width, suggestedBounds.Height, BoundsSpecified.All);
            }

            OnDpiScaleChanged(oldScale, newScale);
            OnApplyDpiScale(newScale);
            this.Invalidate(true);
        }

        /// <summary>
        /// 当屏幕 DPI 发生变更或首次载入时触发，子类重写以更新自定义布局与绘图。
        /// </summary>
        protected virtual void OnApplyDpiScale(float scale)
        {
        }

        /// <summary>
        /// 当跨显示器拖拽触发 WM_DPICHANGED 缩放比变更时触发。
        /// </summary>
        protected virtual void OnDpiScaleChanged(float oldScale, float newScale)
        {
        }

        protected int Scale(int value)
        {
            return DpiScaleHelper.Scale(value, this.dpiScale);
        }

        protected Size Scale(Size size)
        {
            return DpiScaleHelper.Scale(size, this.dpiScale);
        }

        protected Point Scale(Point pt)
        {
            return DpiScaleHelper.Scale(pt, this.dpiScale);
        }

        protected Rectangle Scale(Rectangle rect)
        {
            return DpiScaleHelper.Scale(rect, this.dpiScale);
        }
    }
}
