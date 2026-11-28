// ============================================================================
// 模块名称: MainForm.RefreshUi.cs
// 所属区域: UI/Forms (主窗体分片 - 刷新可观测性)
// 核心职责:
//   1) 【打开即刷新】控制面板呼出后经物理态串行化再发起一次全量状态刷新，
//      并带 3 秒节流窗口，杜绝托盘连点/反复开合把扫描连成 100% 占空；
//   2) 【环形加载条生命周期】把"刷新请求 → 数据落地"收口成一个状态机：
//      StartRefreshCore 的三种结果必须分开处理，被抑制时绝不点亮加载环；
//   3) 【兜底】15 秒守卫定时器保证任何异常路径都不会留下永转的环。
// 依赖关系: RefreshRingControl.cs, HardwareCommandBus.cs（仅读沙盒标记）
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 修改刷新状态机语义时，必须同步更新本说明与 docs/AGENT_HANDBOOK.md 对应段落。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（❌ 严禁 $"..." 插值，❌ 严禁 ?. 与 async/await）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// 4. [铁律 8 协同]: 本分片不触碰窗体句柄重修与 WM_ERASEBKGND，动效一律走子控件自绘。
// ============================================================================

using System;
using System.Drawing;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public partial class MainForm : Form
    {
        // 打开面板触发的刷新节流窗口：距上次"数据落地"小于该值直接跳过。
        // 依据：稳态单次扫描 344-889ms，3000ms 足以覆盖一次完整收口，
        // 又能在用户真正隔了一会儿再打开时给出新鲜数据。
        internal const int PanelOpenRefreshThrottleMs = 3000;

        // 加载环兜底超时：必须大于「冷启动有界等待 12s + 单次扫描最慢 3.4s」之外的
        // 常见路径，取 15s；超时后强制收口并降级提示，绝不让环永转。
        internal const int RefreshRingGuardTimeoutMs = 15000;

        private RefreshRingControl refreshRing;
        private System.Windows.Forms.Timer refreshRingGuardTimer;
        private bool refreshUiWaiting;
        private DateTime lastRefreshAppliedUtc = DateTime.MinValue;

        /// <summary>在 Card 1 内挂载环形加载条（UI 构建期调用一次）。</summary>
        private void InitRefreshRing()
        {
            if (cardBattery == null || refreshRing != null) return;

            refreshRing = new RefreshRingControl();
            refreshRing.AccentColor = Color.FromArgb(0, 230, 118);
            refreshRing.TrackColor = Color.FromArgb(44, 50, 62);
            refreshRing.BackColor = Color.FromArgb(25, 27, 34);
            refreshRing.Visible = false;
            cardBattery.Controls.Add(refreshRing);
            refreshRing.BringToFront();

            if (toolTipTop != null) toolTipTop.SetToolTip(refreshRing, "正在获取最新设备状态...");
            LayoutRefreshRing();
        }

        /// <summary>按 DPI 重排加载环。任何改动 Card 1 几何的地方都必须回调它。</summary>
        internal void LayoutRefreshRing()
        {
            if (refreshRing == null || cardBattery == null) return;

            int size = (int)Math.Round(44 * dpiScale);
            if (size < 24) size = 24;
            int x = cardBattery.ClientSize.Width - size - (int)Math.Round(16 * dpiScale);
            if (x < (int)Math.Round(232 * dpiScale)) x = (int)Math.Round(232 * dpiScale); // 避让状态胶囊
            refreshRing.Size = new Size(size, size);
            refreshRing.Location = new Point(x, (int)Math.Round(56 * dpiScale));
        }

        /// <summary>线程池回调：物理态读取结束后再回 UI 线程发起可观测刷新（避免两条 HID 路径并发抢端点）。</summary>
        private void RequestRefreshAfterPhysicalSync(bool showTipIfManual)
        {
            try
            {
                if (this.IsDisposed) return;
                this.BeginInvoke(new Action(() =>
                {
                    if (this.IsDisposed) return;
                    RequestVisibleRefresh(showTipIfManual);
                }));
            }
            catch { }
        }

        /// <summary>「打开控制面板即刷新」的唯一入口：线程池回调 → UI 线程 → 节流判定 → 发起。</summary>
        private void RequestRefreshOnPanelOpen()
        {
            try
            {
                if (this.IsDisposed) return;
                this.BeginInvoke(new Action(() =>
                {
                    if (this.IsDisposed) return;
                    if (PauseBackgroundRefresh) return;
                    // 沙盒态下刷新链路由测试桩接管，不发起真实扫描，也不点亮加载环
                    if (HardwareCommandBus.IsHardwareSandboxActive) return;
                    if (lastRefreshAppliedUtc != DateTime.MinValue &&
                        (DateTime.UtcNow - lastRefreshAppliedUtc).TotalMilliseconds < PanelOpenRefreshThrottleMs)
                    {
                        return; // 数据足够新：不重复扫描，也不闪环
                    }
                    RequestVisibleRefresh(false);
                }));
            }
            catch { }
        }

        /// <summary>发起一次"可观测"刷新：先判收口结果，被抑制时绝不点亮加载环。</summary>
        private void RequestVisibleRefresh(bool showTipIfManual)
        {
            if (this.IsDisposed || PauseBackgroundRefresh) return;

            if (refreshRing == null)
            {
                StartRefreshCore(showTipIfManual);
                return;
            }

            RefreshOutcome outcome = StartRefreshCore(showTipIfManual);
            if (outcome == RefreshOutcome.Suppressed) return; // 永远不会落地，环一旦点亮就是永转

            BeginRefreshUiWait();
        }

        private void BeginRefreshUiWait()
        {
            refreshUiWaiting = true;

            if (refreshRingGuardTimer == null)
            {
                refreshRingGuardTimer = new System.Windows.Forms.Timer();
                refreshRingGuardTimer.Interval = RefreshRingGuardTimeoutMs;
                refreshRingGuardTimer.Tick += (s, e) =>
                {
                    refreshRingGuardTimer.Stop();
                    AbortRefreshUiWait(true);
                };
            }
            refreshRingGuardTimer.Stop();
            refreshRingGuardTimer.Start();

            if (refreshRing != null) refreshRing.BeginSpinning();
        }

        /// <summary>数据落地收口：由 ApplyDeviceStatus 在 UI 线程首行调用。</summary>
        internal void MarkRefreshAppliedOnUi()
        {
            lastRefreshAppliedUtc = DateTime.UtcNow;

            if (!refreshUiWaiting) return;
            refreshUiWaiting = false;
            if (refreshRingGuardTimer != null) refreshRingGuardTimer.Stop();
            // 最短展示与淡出交给控件自己算，这里只表达"可以收口了"
            if (refreshRing != null) refreshRing.CompleteSpinning();
        }

        /// <summary>异常收口：刷新被抑制 / 读取异常 / 超时兜底。</summary>
        private void AbortRefreshUiWait(bool isTimeout)
        {
            refreshUiWaiting = false;
            if (refreshRingGuardTimer != null) refreshRingGuardTimer.Stop();
            if (refreshRing != null) refreshRing.AbortSpinning();
            if (isTimeout && lblUpdateTime != null)
            {
                lblUpdateTime.Text = "刷新超时 · 当前显示缓存数据";
            }
        }
    }
}
