// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
namespace RazerBatteryTray
{
    public sealed class SonyPulseExploreState
    {
        public int LeftBatteryPercent { get; internal set; }
        public int RightBatteryPercent { get; internal set; }
        public bool IsLeftOnline { get; internal set; }
        public bool IsRightOnline { get; internal set; }
        public bool IsConnected { get; internal set; }
        public bool? LeftCharging { get; internal set; }
        public bool? RightCharging { get; internal set; }
        public bool IsUsbCaseConnected { get; internal set; }
        public bool IsLeftDocked { get; internal set; }
        public bool IsRightDocked { get; internal set; }
        internal string PairingIdentity { get; set; }
    }
}