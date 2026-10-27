// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
using System;

namespace RazerBatteryTray
{
    public sealed class AtkLightingSettings
    {
        public bool Enabled { get; set; }
        public int Mode { get; set; }
        public int Brightness { get; set; }
        public int Speed { get; set; }
        public bool Rainbow { get; set; }
        public byte Red { get; set; }
        public byte Green { get; set; }
        public byte Blue { get; set; }
        public string DevicePath { get; internal set; }
        internal byte Profile { get; set; }
    }
}