# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# Pure memory-load test for tray menu flicker fix, GDI Font reuse, and idempotent dropdown wiring.
$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$root = (Resolve-Path "$PSScriptRoot\..").Path
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes((Join-Path $root "FerrisPulse.exe")))
$tMain = $asm.GetType("RazerBatteryTray.MainForm")
$tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo")
$tCat = $asm.GetType("RazerBatteryTray.DeviceCategory")
$flags = [System.Reflection.BindingFlags]"Instance,NonPublic"

function Assert($ok, $why) { if (-not $ok) { throw "FAIL: $why" } }

$form = [Activator]::CreateInstance($tMain, @($false))
try {
    $null = $form.Handle
    $activeList = $tMain.GetField("activeDevices", $flags).GetValue($form)
    $activeList.Clear()

    $m1 = [Activator]::CreateInstance($tInfo)
    $m1.DeviceId = "Test:M1"; $m1.Brand = "Razer"; $m1.DeviceName = "Razer Viper V2 Pro"
    $m1.Category = [Enum]::Parse($tCat, "Mouse"); $m1.IsConnected = $true; $m1.IsPrimary = $true; $m1.BatteryPercent = 80
    $m1.DpiStages = [int[]]@(400, 800, 1600); $m1.SupportedPollingRates = [int[]]@(125, 500, 1000)

    $k1 = [Activator]::CreateInstance($tInfo)
    $k1.DeviceId = "Test:K1"; $k1.Brand = "ASUS"; $k1.DeviceName = "ROG AZOTH 96 HE"
    $k1.Category = [Enum]::Parse($tCat, "Keyboard"); $k1.IsConnected = $true; $k1.BatteryPercent = 100
    $activeList.Add($m1); $activeList.Add($k1)

    $updateMenu = $tMain.GetMethod("UpdateTrayDeviceMenuItems", $flags)
    $updateMenu.Invoke($form, @()) | Out-Null

    $dynItems = $tMain.GetField("dynamicDeviceMenuItems", $flags).GetValue($form)
    Assert ($dynItems.Count -eq 2) "Expected 2 dynamic device menu items"
    $f1 = $dynItems[0].Font
    $f2 = $dynItems[1].Font
    Assert ([object]::ReferenceEquals($f1, $f2)) "Device menu items must share reusable trayDeviceItemFont"

    $ctx = $tMain.GetField("contextMenu", $flags).GetValue($form)
    $hAfterFirst = $ctx.PreferredSize.Height
    $updateMenu.Invoke($form, @()) | Out-Null
    $dynItems2 = $tMain.GetField("dynamicDeviceMenuItems", $flags).GetValue($form)
    Assert ([object]::ReferenceEquals($f1, $dynItems2[0].Font)) "Font reference must be reused across menu openings"

    # Toggle primary to keyboard (hides DPI/Rate menus) and verify layout converged synchronously
    $m1.IsPrimary = $false; $k1.IsPrimary = $true
    $updateMenu.Invoke($form, @()) | Out-Null
    $hAfterSecond = $ctx.PreferredSize.Height
    Assert ($hAfterSecond -lt $hAfterFirst) "Hiding mouse submenus must shrink preferred height synchronously"
    Assert ($ctx.Height -eq $hAfterSecond) "contextMenu.Height must already match PreferredSize.Height after PerformLayout()"

    # Idempotent ConfigureSubMenuDropDown guard
    $cfgSet = $tMain.GetField("configuredSubMenuDropDowns", $flags).GetValue($form)
    $dpiMenu = $tMain.GetField("dpiMenu", $flags).GetValue($form)
    Assert ($cfgSet.Contains($dpiMenu.DropDown)) "dpiMenu.DropDown must be registered in configuredSubMenuDropDowns"

    Write-Host "PASS: tray menu PerformLayout convergence, Font reuse, and dropdown handler idempotency"
}
finally {
    $form.Dispose()
}
