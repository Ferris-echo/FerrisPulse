# ============================================================================
# 测试脚本: test_nuphy_lighting_control.ps1
# 目标: 验证 NuPhy WH80 及全系键盘灯效控制、亮度记忆与状态回显契约
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = Resolve-Path "$ScriptDir\.."
Set-Location $ProjectDir

Write-Host "[TEST] Validating NuPhy Lighting Control & State Reflection..." -ForegroundColor Cyan

$exePath = "$ProjectDir\FerrisPulse.exe"
if (-not (Test-Path $exePath)) {
    throw "FerrisPulse.exe not found!"
}

$asmBytes = [System.IO.File]::ReadAllBytes($exePath)
$asm = [System.Reflection.Assembly]::Load($asmBytes)
$nuphyHelper = $asm.GetType("RazerBatteryTray.NuphyDeviceHelper")
if ($null -eq $nuphyHelper) {
    throw "NuphyDeviceHelper type not found in assembly!"
}

# 1. 验证基础类型与设置模型
$settingsProp = $nuphyHelper.GetField("CachedHardwareSettings", [System.Reflection.BindingFlags]"Public,Static")
if ($null -eq $settingsProp) {
    throw "CachedHardwareSettings field not found!"
}
$settings = $settingsProp.GetValue($null)
if ($null -eq $settings) {
    throw "CachedHardwareSettings is null!"
}

# 2. 验证关灯亮度记忆与复原机制 (Backlight & Sidelight Memory Contract)
# 设置测试基准亮度
$settings.BacklightBrightness = 75
$settings.BacklightEnabled = $true
$settings.SidelightBrightness = 60
$settings.SidelightEnabled = $true

# 验证属性字段或反射方法
$lastBackProp = $nuphyHelper.GetField("LastActiveBacklightBrightness", [System.Reflection.BindingFlags]"Public,Static")
$lastSideProp = $nuphyHelper.GetField("LastActiveSidelightBrightness", [System.Reflection.BindingFlags]"Public,Static")

if ($null -eq $lastBackProp -or $null -eq $lastSideProp) {
    throw "LastActiveBacklightBrightness / LastActiveSidelightBrightness not found on NuphyDeviceHelper! Red test confirmed."
}

$lastBackProp.SetValue($null, [int]75)
$lastSideProp.SetValue($null, [int]60)

# 测试关灯
$setBackEnMethod = $nuphyHelper.GetMethod("SetBacklightEnabled", [System.Reflection.BindingFlags]"Public,Static")
$setSideEnMethod = $nuphyHelper.GetMethod("SetSidelightEnabled", [System.Reflection.BindingFlags]"Public,Static")

# 模拟关灯逻辑断言
$setBackEnMethod.Invoke($null, @($false)) | Out-Null
if ($settings.BacklightEnabled) {
    throw "BacklightEnabled should be false after SetBacklightEnabled(false)!"
}
$storedLastBack = [int]$lastBackProp.GetValue($null)
if ($storedLastBack -ne 75) {
    throw "LastActiveBacklightBrightness should remember 75, but was $storedLastBack!"
}

# 模拟开灯逻辑断言（必须精准恢复到 75，绝非强制重置为 100）
$setBackEnMethod.Invoke($null, @($true)) | Out-Null
if (-not $settings.BacklightEnabled) {
    throw "BacklightEnabled should be true after SetBacklightEnabled(true)!"
}
if ($settings.BacklightBrightness -ne 75) {
    throw "BacklightBrightness should be restored to 75, but was $($settings.BacklightBrightness)!"
}

# 侧翼/前置灯带记忆断言
$setSideEnMethod.Invoke($null, @($false)) | Out-Null
if ($settings.SidelightEnabled) {
    throw "SidelightEnabled should be false after SetSidelightEnabled(false)!"
}
$storedLastSide = [int]$lastSideProp.GetValue($null)
if ($storedLastSide -ne 60) {
    throw "LastActiveSidelightBrightness should remember 60, but was $storedLastSide!"
}

$setSideEnMethod.Invoke($null, @($true)) | Out-Null
if (-not $settings.SidelightEnabled) {
    throw "SidelightEnabled should be true after SetSidelightEnabled(true)!"
}
if ($settings.SidelightBrightness -ne 60) {
    throw "SidelightBrightness should be restored to 60, but was $($settings.SidelightBrightness)!"
}

# 3. 验证 NuphyKeyboardDriver 的能力契约同步与回显
$driverType = $asm.GetType("RazerBatteryTray.NuphyKeyboardDriver")
if ($null -eq $driverType) {
    throw "NuphyKeyboardDriver type not found!"
}
$driver = [System.Activator]::CreateInstance($driverType)
$dev = [System.Activator]::CreateInstance($asm.GetType("RazerBatteryTray.MouseBatteryInfo"))
$dev.Brand = "NuPhy"
$dev.DeviceId = "HID:19F5:8F01"

$populateMethod = $driverType.GetMethod("PopulateCapabilities")
$populateMethod.Invoke($driver, @($dev))

if ($null -eq $dev.Capabilities -or $null -eq $dev.Capabilities.CustomActions) {
    throw "CustomActions not populated on NuPhy device!"
}

$actions = $dev.Capabilities.CustomActions
$backToggle = $actions | Where-Object { $_.ActionId -eq "backlight_toggle" }
$sideToggle = $actions | Where-Object { $_.ActionId -eq "sidelight_toggle" }
$backSlider = $actions | Where-Object { $_.ActionId -eq "backlight_brightness" }

if ($null -eq $backToggle -or $null -eq $sideToggle -or $null -eq $backSlider) {
    throw "Required custom actions (backlight_toggle, sidelight_toggle, backlight_brightness) missing!"
}

if ($backSlider.IntValue -ne 75) {
    throw "backlight_brightness slider value should be 75, but was $($backSlider.IntValue)!"
}
if ($backToggle.BoolValue -ne $true) {
    throw "backlight_toggle BoolValue should be true!"
}
if ($sideToggle.BoolValue -ne $true) {
    throw "sidelight_toggle BoolValue should be true!"
}

Write-Host "[PASS] NuPhy Lighting Control & State Reflection Verified Successfully!" -ForegroundColor Green
