# tests\test_device_switch_ui_cleanup.ps1
# 验证设备切换时，全屏游戏自动优化相关组件（chkAutoGameOpt, chkAutoGameDpi, btnGameDpiStages, btnGameRates, btnManageGames）
# 能够被彻底隐藏与清理，严禁残留到键盘、耳机、手柄等非支持设备界面中

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
$mUpdate = $tMainForm.GetMethod("UpdatePerformanceSection", [System.Reflection.BindingFlags]'Instance,NonPublic')
if ($mUpdate -eq $null) {
    Write-Host "FAIL: UpdatePerformanceSection not found" -ForegroundColor Red
    exit 1
}

function Get-Control($name) {
    $f = $tMainForm.GetField($name, [System.Reflection.BindingFlags]'Instance,NonPublic')
    if ($f -ne $null) {
        return $f.GetValue($form)
    }
    return $null
}

# 1. 模拟鼠标设备（激活全屏电竞优化与电竞 DPI）
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
$form.Visible = $true
$fAutoDpi = $tMainForm.GetField("autoGameDpiEnabled", [System.Reflection.BindingFlags]'Instance,NonPublic')
$fAutoDpi.SetValue($form, $true)
$fAutoRate = $tMainForm.GetField("autoGameRateEnabled", [System.Reflection.BindingFlags]'Instance,NonPublic')
$fAutoRate.SetValue($form, $true)

$mUpdate.Invoke($form, @($mouseInfo, [System.Drawing.Color]::FromArgb(0, 255, 0)))

$chkAutoGameOpt = Get-Control "chkAutoGameOpt"
$chkAutoGameDpi = Get-Control "chkAutoGameDpi"
$btnGameDpiStages = Get-Control "btnGameDpiStages"
$btnManageGames = Get-Control "btnManageGames"

Write-Host "--- 状态 1: 切换到电竞鼠标 ---" -ForegroundColor Cyan
Write-Host "chkAutoGameOpt.Visible =" $chkAutoGameOpt.Visible
Write-Host "chkAutoGameDpi.Visible =" $chkAutoGameDpi.Visible
Write-Host "btnGameDpiStages[0].Visible =" $btnGameDpiStages[0].Visible

# 2. 模拟切换到键盘设备 (NuPhy WH80)
$nuphyInfo = [Activator]::CreateInstance($tInfo)
$nuphyInfo.Brand = "NuPhy"
$nuphyInfo.DeviceId = "NuPhy:WH80"
$nuphyInfo.DeviceName = "NuPhy WH80"
$nuphyInfo.Category = [Enum]::Parse($tCat, "Keyboard")
$nuphyInfo.IsConnected = $true
$nuphyInfo.BatteryPercent = 97
$nuphyInfo.Transport = "2.4G 无线"

$mUpdate.Invoke($form, @($nuphyInfo, [System.Drawing.Color]::FromArgb(0, 229, 255)))

Write-Host "`n--- 状态 2: 切换到 NuPhy 键盘 (必须彻底隐藏电竞优化与 DPI 组件) ---" -ForegroundColor Cyan
Write-Host "chkAutoGameOpt.Visible =" $chkAutoGameOpt.Visible
Write-Host "chkAutoGameDpi.Visible =" $chkAutoGameDpi.Visible
Write-Host "btnGameDpiStages[0].Visible =" $btnGameDpiStages[0].Visible

$failed = $false

if ($chkAutoGameOpt.Visible) {
    Write-Host "FAIL: chkAutoGameOpt 依然可见！未被隐藏！" -ForegroundColor Red
    $failed = $true
} else {
    Write-Host "PASS: chkAutoGameOpt 已被隐藏" -ForegroundColor Green
}

if ($chkAutoGameDpi.Visible) {
    Write-Host "FAIL: chkAutoGameDpi 依然可见！未被隐藏！" -ForegroundColor Red
    $failed = $true
} else {
    Write-Host "PASS: chkAutoGameDpi 已被隐藏" -ForegroundColor Green
}

if ($btnGameDpiStages[0].Visible) {
    Write-Host "FAIL: btnGameDpiStages 依然可见！未被隐藏！" -ForegroundColor Red
    $failed = $true
} else {
    Write-Host "PASS: btnGameDpiStages 已被隐藏" -ForegroundColor Green
}

if ($btnManageGames.Visible) {
    Write-Host "FAIL: btnManageGames 依然可见！未被隐藏！" -ForegroundColor Red
    $failed = $true
} else {
    Write-Host "PASS: btnManageGames 已被隐藏" -ForegroundColor Green
}

$form.Dispose()

if ($failed) {
    exit 1
}

Write-Host "`nALL PASS: 设备切换 UI 清理测试全部通过！" -ForegroundColor Green
exit 0
