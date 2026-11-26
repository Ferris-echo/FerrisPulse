// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
using System;

namespace RazerBatteryTray
{
    public partial class MainForm
    {
        private void UpdatePulseExploreTile(MouseBatteryInfo info)
        {
            if (toolTipTop != null) toolTipTop.SetToolTip(tileHeadsetAudio, info.CustomTooltip ?? "");
            SonyPulseExploreState state = info.PulseExploreState;
            if (state == null) return;
            tileHeadsetAudio.Title = "PULSE Explore · PS Link 双耳电量";
            tileHeadsetAudio.StatusText = state.IsConnected || state.IsUsbCaseConnected
                ? string.Format("L≈{0} · R≈{1}", state.IsLeftOnline || state.IsLeftDocked ? state.LeftBatteryPercent + "%" : "--", state.IsRightOnline || state.IsRightDocked ? state.RightBatteryPercent + "%" : "--")
                : "耳塞离线 · 接收器在线";
            tileHeadsetAudio.Footnote = state.IsUsbCaseConnected
                ? (state.IsLeftDocked ? (state.LeftCharging == true ? "左充电 " : "左已满 ") : "") +
                    (state.IsRightDocked ? (state.RightCharging == true ? "右充电 " : "右已满 ") : "") + "· 盒电量未知"
                : "0..15档位换算 · 充电/盒电量未知";
        }
    }
}