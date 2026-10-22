# tests\test_tray_low_battery_priority.ps1
# 验证任务栏常驻仲裁模式：
# 1) DeviceManager 暴露「低电量优先」开关 API，默认值为开启 (true)；
# 2) 开关可写入并读回注册表 TrayLowBatteryFirst（测试结束后还原原值，避免污染用户配置）；
# 3) MainForm 托盘链路已挂载 trayLowBatteryFirstMenuItem 菜单字段。

$ErrorActionPreference = "Stop"

$asmPath = Join-Path $PSScriptRoot "..\FerrisPulse.exe"
if (-not (Test-Path $asmPath)) { $asmPath = "FerrisPulse.exe" }
$fullPath = (Resolve-Path $asmPath).Path
$bytes = [System.IO.File]::ReadAllBytes($fullPath)
$asm = [System.Reflection.Assembly]::Load($bytes)

$tDM = $asm.GetType("RazerBatteryTray.DeviceManager")
$tMainForm = $asm.GetType("RazerBatteryTray.MainForm")

$flagsStatic = [System.Reflection.BindingFlags]'Static,Public'
$mGet = $tDM.GetMethod("IsTrayLowBatteryFirstEnabled", $flagsStatic)
$mSet = $tDM.GetMethod("SetTrayLowBatteryFirstEnabled", $flagsStatic)

if ($mGet -eq $null -or $mSet -eq $null) {
    Write-Host "FAIL: DeviceManager.IsTrayLowBatteryFirstEnabled / SetTrayLowBatteryFirstEnabled missing" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: DeviceManager 仲裁模式 API 存在" -ForegroundColor Green

$field = $tMainForm.GetField("trayLowBatteryFirstMenuItem", [System.Reflection.BindingFlags]'Instance,NonPublic')
if ($field -eq $null) {
    Write-Host "FAIL: MainForm.trayLowBatteryFirstMenuItem field missing" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: MainForm 托盘菜单字段已挂载" -ForegroundColor Green

# --- 注册表默认值与读写往返验证 ---
$regKey = "HKCU:\Software\RazerBatteryTray"
$valName = "TrayLowBatteryFirst"
$original = $null
if (Test-Path $regKey) {
    $prop = Get-ItemProperty -Path $regKey -Name $valName -ErrorAction SilentlyContinue
    if ($prop -ne $null) { $original = $prop.$valName }
}

if ($original -eq $null) {
    $def = $mGet.Invoke($null, @())
    if ($def -ne $true) {
        Write-Host "FAIL: 默认值应为 true (低电量优先)，实际为 $def" -ForegroundColor Red
        exit 1
    }
    Write-Host "PASS: 未配置时默认开启低电量优先" -ForegroundColor Green
}

$mSet.Invoke($null, @($false))
if ($mGet.Invoke($null, @()) -ne $false) {
    Write-Host "FAIL: 关闭后读回值应为 false" -ForegroundColor Red
    exit 1
}
$mSet.Invoke($null, @($true))
if ($mGet.Invoke($null, @()) -ne $true) {
    Write-Host "FAIL: 开启后读回值应为 true" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: 仲裁模式开关可持久化并正确读回" -ForegroundColor Green

# --- 还原用户原有配置 ---
if ($original -eq $null) {
    if (Test-Path $regKey) {
        Remove-ItemProperty -Path $regKey -Name $valName -ErrorAction SilentlyContinue
    }
} else {
    $mSet.Invoke($null, @(([int]$original -eq 1)))
}

Write-Host "ALL PASS: 任务栏常驻低电量优先仲裁模式契约校验完成" -ForegroundColor Green
exit 0
