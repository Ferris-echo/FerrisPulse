// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
using System;
using System.Drawing;
using System.Windows.Forms;

namespace RazerBatteryTray
{
    public partial class MainForm
    {
        private ModernButton btnAtkLighting;

        private void InitializeAtkLightingControl()
        {
            btnAtkLighting = new ModernButton();
            btnAtkLighting.Text = "灯光控制";
            btnAtkLighting.Font = new Font("Microsoft YaHei UI", 8.5F);
            btnAtkLighting.NormalColor = Color.FromArgb(35, 31, 26);
            btnAtkLighting.HoverColor = Color.FromArgb(55, 47, 35);
            btnAtkLighting.PressedColor = Color.FromArgb(25, 23, 20);
            btnAtkLighting.BorderColor = Color.FromArgb(150, 120, 70);
            btnAtkLighting.TextColor = Color.FromArgb(235, 200, 130);
            btnAtkLighting.CornerRadius = (int)(6 * dpiScale);
            btnAtkLighting.Cursor = Cursors.Hand;
            btnAtkLighting.Visible = false;
            btnAtkLighting.Click += delegate
            {
                MouseBatteryInfo target = selectedDevice;
                if (target == null || target.Brand != "ATK" || target.Category != DeviceCategory.Keyboard ||
                    !AtkDeviceHelper.SupportsLightingDevice(target.DeviceId)) return;
                using (AtkLightingDialog dialog = new AtkLightingDialog(target.DeviceId, dpiScale)) dialog.ShowDialog(this);
            };
            cardPerformance.Controls.Add(btnAtkLighting);
        }

        private void ConfigureAtkLightingControl(MouseBatteryInfo info, int sectionWidth, int buttonWidth, int rowY, int buttonHeight)
        {
            if (btnKeyboardSettings != null) btnKeyboardSettings.Text = "⌨️ 打开 Windows 键盘属性设置";
            if (btnAtkLighting == null || info.Brand != "ATK" || info.Category != DeviceCategory.Keyboard ||
                !AtkDeviceHelper.SupportsLightingDevice(info.DeviceId)) return;
            if (btnKeyboardSettings != null)
            {
                btnKeyboardSettings.Text = "Windows 键盘属性";
                btnKeyboardSettings.Width = buttonWidth;
            }
            btnAtkLighting.SetBounds((int)(16 * dpiScale) + sectionWidth - buttonWidth, rowY, buttonWidth, buttonHeight);
            btnAtkLighting.Enabled = info.IsConnected && !info.IsSleeping;
            btnAtkLighting.Visible = true;
        }
    }
}