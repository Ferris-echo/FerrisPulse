// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
// VT3 unsolicited battery reports -> existing UI snapshots; C#5/.NET4, no HID queries.
using System;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public partial class MainForm
    {
        private Timer rapooStatusUiTimer;

        private void InitializeRapooStatusUpdates()
        {
            rapooStatusUiTimer = new Timer();
            rapooStatusUiTimer.Interval = 250;
            rapooStatusUiTimer.Tick += delegate { SynchronizeRapooStatus(); };
            Disposed += delegate { rapooStatusUiTimer.Stop(); rapooStatusUiTimer.Dispose(); };
            rapooStatusUiTimer.Start();
        }

        private void SynchronizeRapooStatus()
        {
            if (PauseBackgroundRefresh || activeDevices == null || IsDisposed) return;
            MouseBatteryInfo status = RapooDeviceHelper.TryGetStatusSnapshot();
            if (status == null) return;
            bool changed = false;
            bool selectedChanged = false;
            foreach (MouseBatteryInfo device in activeDevices)
            {
                if (device == null || device.Brand != "Rapoo" || !device.IsConnected ||
                    !string.Equals(device.DeviceId, status.DeviceId, StringComparison.OrdinalIgnoreCase) ||
                    !string.Equals(device.HardwareFingerprint, status.HardwareFingerprint, StringComparison.OrdinalIgnoreCase)) continue;
                if (device.BatteryPercent == status.BatteryPercent && device.IsCharging == status.IsCharging &&
                    device.LastUpdated == status.LastUpdated) continue;
                device.BatteryPercent = status.BatteryPercent;
                device.IsCharging = status.IsCharging;
                device.LastUpdated = status.LastUpdated;
                changed = true;
                if (object.ReferenceEquals(device, selectedDevice)) selectedChanged = true;
            }
            if (!changed) return;
            if (selectedChanged) UpdateUI(selectedDevice, false);
            UpdateDeviceTabsUI();
            MouseBatteryInfo primary = GetPrimaryDevice();
            if (primary != null)
            {
                lastInfo = primary;
                UpdateTrayIcon(primary.BatteryPercent, primary.IsCharging, primary.IsConnected, primary.IsSleeping);
                UpdateStatusDisplay();
            }
            UpdateTrayDeviceMenuItems();
        }
    }
}