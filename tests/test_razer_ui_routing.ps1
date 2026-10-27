# tests\test_razer_ui_routing.ps1
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$asmPath = Join-Path $PSScriptRoot "..\FerrisPulse.exe"
if (-not (Test-Path $asmPath)) { $asmPath = "FerrisPulse.exe" }
$fullPath = (Resolve-Path $asmPath).Path
$bytes = [System.IO.File]::ReadAllBytes($fullPath)
$asm = [System.Reflection.Assembly]::Load($bytes)

$tMainForm = $asm.GetType("RazerBatteryTray.MainForm")
$tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo")
$tCat = $asm.GetType("RazerBatteryTray.DeviceCategory")


# 启用测试防写保护，防止单测覆写用户生产注册表
$fDisableSave = $tMainForm.GetField("DisableConfigSaveForTesting", [System.Reflection.BindingFlags]'Static,NonPublic,Public')
if ($fDisableSave -ne $null) { $fDisableSave.SetValue($null, $true) }

$form = [Activator]::CreateInstance($tMainForm, @($false))
$null = $form.Handle
$form.Visible = $true

$mUpdate = $tMainForm.GetMethod("UpdatePerformanceSection", [System.Reflection.BindingFlags]'Instance,NonPublic')
if ($mUpdate -eq $null) {
    Write-Host "RED: UpdatePerformanceSection method not found on MainForm!" -ForegroundColor Red
    exit 1
}

# Helpers to extract private controls from MainForm
function Get-Control($fieldName) {
    $f = $tMainForm.GetField($fieldName, [System.Reflection.BindingFlags]'Instance,NonPublic')
    if ($f -ne $null) {
        return $f.GetValue($form)
    }
    return $null
}

# --- TEST 1: Razer Keyboard (BlackWidow X Chroma) Routing ---
Write-Host "--- TEST 1: Razer Keyboard Routing ---" -ForegroundColor Cyan
$kbInfo = [Activator]::CreateInstance($tInfo)
$kbInfo.Brand = "Razer"
$kbInfo.DeviceId = "Razer:1532:0216"
$kbInfo.DeviceName = "Razer BlackWidow X Chroma"
$kbInfo.Category = [Enum]::Parse($tCat, "Keyboard")
$kbInfo.IsConnected = $true
$kbInfo.BatteryPercent = -1
$kbInfo.Transport = "USB"

$mUpdate.Invoke($form, @($kbInfo, [System.Drawing.Color]::FromArgb(0, 229, 255)))

$lblPerfTitle = Get-Control "lblPerfTitle"
$lblDpiTitle = Get-Control "lblDpiTitle"
$btnDpiManage = Get-Control "btnDpiManage"
$lblRateTitle = Get-Control "lblRateTitle"
$tileRapooDpi = Get-Control "tileRapooDpi"
$hudThreeLocks = Get-Control "hudThreeLocks"
$btnUniversalWinLock = Get-Control "btnUniversalWinLock"
$btnKeyTestSpeed = Get-Control "btnKeyTestSpeed"
$btnKeyboardSettings = Get-Control "btnKeyboardSettings"
$dpiMenu = Get-Control "dpiMenu"
$rateMenu = Get-Control "rateMenu"

if ($lblDpiTitle.Visible -or $btnDpiManage.Visible -or $lblRateTitle.Visible) {
    Write-Host "FAIL: DPI controls must be HIDDEN for Razer Keyboard!" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: DPI and Polling Rate controls are properly hidden for keyboard." -ForegroundColor Green

if (-not $tileRapooDpi.Visible) {
    Write-Host "FAIL: Keyboard status tile must be VISIBLE!" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: Keyboard status tile is visible." -ForegroundColor Green

if (-not $lblPerfTitle.Text.Contains("键盘")) {
    Write-Host ("FAIL: Performance title expected keyboard, got: " + $lblPerfTitle.Text) -ForegroundColor Red
    exit 1
}
Write-Host ("PASS: Performance title is: " + $lblPerfTitle.Text) -ForegroundColor Green

if (-not $hudThreeLocks.Visible -or -not $btnUniversalWinLock.Visible -or -not $btnKeyTestSpeed.Visible -or -not $btnKeyboardSettings.Visible) {
    Write-Host "FAIL: Keyboard dedicated HUD / controls not visible!" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: ThreeLocks HUD, UniversalWinLock, KeyTestSpeed, and KeyboardSettings are all visible!" -ForegroundColor Green

if ($dpiMenu.Visible -or $rateMenu.Visible) {
    Write-Host "FAIL: Tray menus for DPI/Rate should be hidden for keyboard!" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: Tray DPI and Rate menus are hidden for keyboard." -ForegroundColor Green


# --- TEST 2: Razer Mouse (DeathAdder V3 Pro) Routing ---
Write-Host "`n--- TEST 2: Razer Mouse Routing ---" -ForegroundColor Cyan
$mouseInfo = [Activator]::CreateInstance($tInfo)
$mouseInfo.Brand = "Razer"
$mouseInfo.DeviceId = "Razer:1532:00B7"
$mouseInfo.DeviceName = "Razer DeathAdder V3 Pro"
$mouseInfo.Category = [Enum]::Parse($tCat, "Mouse")
$mouseInfo.IsConnected = $true
$mouseInfo.BatteryPercent = 85
$mouseInfo.Dpi = 1600
$mouseInfo.PollingRate = 1000
$mouseInfo.DpiStages = [int[]]@(400, 800, 1600, 3200, 6400)
$mouseInfo.DpiStage = 3

$mUpdate.Invoke($form, @($mouseInfo, [System.Drawing.Color]::FromArgb(0, 255, 0)))

if (-not $lblDpiTitle.Visible -or -not $lblRateTitle.Visible) {
    Write-Host "FAIL: DPI and Rate titles must be VISIBLE for Razer Mouse!" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: DPI and Polling Rate titles are visible for Razer Mouse." -ForegroundColor Green

if (-not $lblPerfTitle.Text.Contains("鼠标")) {
    Write-Host ("FAIL: Performance title expected mouse, got: " + $lblPerfTitle.Text) -ForegroundColor Red
    exit 1
}
Write-Host ("PASS: Performance title is: " + $lblPerfTitle.Text) -ForegroundColor Green

if ($hudThreeLocks.Visible -or $btnUniversalWinLock.Visible) {
    Write-Host "FAIL: Keyboard-specific ThreeLocks/WinLock must be HIDDEN for Razer Mouse!" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: Keyboard-specific HUD/WinLock controls are properly hidden for Razer Mouse." -ForegroundColor Green

Write-Host "`nALL RAZER UI ROUTING TESTS PASSED!" -ForegroundColor Green
exit 0
