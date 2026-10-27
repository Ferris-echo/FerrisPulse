# ============================================================================
# 测试脚本: test_device_capabilities_model.ps1
# 目标: 验证 DeviceCapabilities 与 CustomDeviceAction 能力契约模型、
#       MouseBatteryInfo.Capabilities / DriverId 属性及 ThemeColorHex 自定义配色解析
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = (Resolve-Path "$ScriptDir\..").Path
Set-Location $ProjectDir

$exePath = "$ProjectDir\FerrisPulse.exe"
if (-not (Test-Path $exePath)) {
    Write-Host "FAIL: FerrisPulse.exe not found." -ForegroundColor Red
    exit 1
}

$bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $exePath).Path)
$assembly = [System.Reflection.Assembly]::Load($bytes)

Write-Host "--- 1. 验证 CustomActionType 与 CustomDeviceAction 模型 ---" -ForegroundColor Cyan
$tActionType = $assembly.GetType("RazerBatteryTray.CustomActionType")
$tAction = $assembly.GetType("RazerBatteryTray.CustomDeviceAction")
$tCap = $assembly.GetType("RazerBatteryTray.DeviceCapabilities")

if ($null -eq $tActionType -or $null -eq $tAction -or $null -eq $tCap) {
    Write-Host "FAIL: 未找到 CustomActionType / CustomDeviceAction / DeviceCapabilities 类型！" -ForegroundColor Red
    exit 1
}

$toggleAct = [System.Activator]::CreateInstance($tAction)
$toggleAct.ActionId = "auto_sleep"
$toggleAct.Title = "自动休眠"
$toggleAct.ActionType = [System.Enum]::Parse($tActionType, "Toggle")
$toggleAct.ActiveText = "🌙 自动休眠: 已开启"
$toggleAct.InactiveText = "☀️ 自动休眠: 已关闭"
$toggleAct.BoolValue = $true
$toggleAct.ShowInTrayMenu = $true

if ($toggleAct.GetFormattedButtonText() -ne "🌙 自动休眠: 已开启") {
    Write-Host "FAIL: Toggle 按钮激活态文本不匹配: $($toggleAct.GetFormattedButtonText())" -ForegroundColor Red
    exit 1
}
$toggleAct.BoolValue = $false
if ($toggleAct.GetFormattedButtonText() -ne "☀️ 自动休眠: 已关闭") {
    Write-Host "FAIL: Toggle 按钮关闭态文本不匹配: $($toggleAct.GetFormattedButtonText())" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: CustomDeviceAction Toggle 文本动态推导验证通过" -ForegroundColor Green

Write-Host "`n--- 2. 验证 MouseBatteryInfo.Capabilities 与 DriverId 属性 ---" -ForegroundColor Cyan
$tDev = $assembly.GetType("RazerBatteryTray.MouseBatteryInfo")
$dev = [System.Activator]::CreateInstance($tDev)

if ($null -eq $dev.Capabilities) {
    Write-Host "FAIL: MouseBatteryInfo.Capabilities 应默认自动初始化非空实例！" -ForegroundColor Red
    exit 1
}

$dev.DriverId = "ext.futurebrand.x1"
$dev.Brand = "FutureBrandX"
$dev.Capabilities.SupportsDpi = $true
$dev.Capabilities.SupportsDpiStages = $true
$dev.Capabilities.SupportsXyIndependence = $true
$dev.Capabilities.SupportsPollingRate = $true
$dev.Capabilities.SupportsHardwareDpiMonitor = $true
$dev.Capabilities.MinDpi = 50
$dev.Capabilities.MaxDpi = 42000
$dev.Capabilities.DpiStep = 50
$dev.Capabilities.SensorDescription = "PAW3950 Ultra (50~42,000 DPI)"
$dev.Capabilities.ThemeColorHex = "#FF55AA"
$dev.Capabilities.CustomActions.Add($toggleAct)

if ($dev.DriverId -ne "ext.futurebrand.x1" -or $dev.Capabilities.CustomActions.Count -ne 1) {
    Write-Host "FAIL: DriverId 或 CustomActions 存储异常！" -ForegroundColor Red
    exit 1
}

$c = $dev.BrandColor
if ($c.R -ne 255 -or $c.G -ne 85 -or $c.B -ne 170) {
    Write-Host "FAIL: ThemeColorHex (#FF55AA) 未被 BrandColor 优先解析！实际 RGB=($($c.R), $($c.G), $($c.B))" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: MouseBatteryInfo.Capabilities、DriverId 与自定义 ThemeColorHex (#FF55AA) 解析全部通过" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " ALL PASS: DeviceCapabilities & CustomDeviceAction 契约测试通过！" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
exit 0
