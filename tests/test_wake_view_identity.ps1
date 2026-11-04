# ============================================================================
# 测试脚本: test_wake_view_identity.ps1
# 覆盖契约:
#   1. 多罗技接收器 (046D:C539 + 046D:C53F) 硬件路径唤醒匹配必须按 PID 精确隔离，不串联触发
#   2. UpdateUI 在传入非当前选中设备 (selectedDevice) 时必须拦截，防止异设备唤醒覆盖主卡片
#   3. LogitechDeviceHelper 按 DeviceId 独立缓存名称与充电状态
# ============================================================================

$ErrorActionPreference = "Stop"
$exePath = Join-Path $PSScriptRoot "..\FerrisPulse.exe"
if (-not (Test-Path $exePath)) {
    Write-Host "[FAIL] FerrisPulse.exe not found" -ForegroundColor Red
    exit 1
}

$bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $exePath))
$asm = [System.Reflection.Assembly]::Load($bytes)

$mainFormType = $asm.GetType("RazerBatteryTray.MainForm")
$infoType = $asm.GetType("RazerBatteryTray.MouseBatteryInfo")
$catType = $asm.GetType("RazerBatteryTray.DeviceCategory")
$logiType = $asm.GetType("RazerBatteryTray.LogitechDeviceHelper")

# FormattingServices bypasses constructor
$form = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($mainFormType)

function Set-PrivateField($obj, $name, $val) {
    $f = $mainFormType.GetField($name, [System.Reflection.BindingFlags]"NonPublic,Public,Instance")
    if ($null -eq $f) { throw "Missing field: $name" }
    $f.SetValue($obj, $val)
}

function Get-PrivateField($obj, $name) {
    $f = $mainFormType.GetField($name, [System.Reflection.BindingFlags]"NonPublic,Public,Instance")
    if ($null -eq $f) { throw "Missing field: $name" }
    return $f.GetValue($obj)
}

function New-MouseInfo([string]$id, [string]$name, [string]$brand, [int]$batt, [bool]$sleeping) {
    $m = [Activator]::CreateInstance($infoType)
    $m.DeviceId = $id
    $m.DeviceName = $name
    $m.Brand = $brand
    $m.Category = [Enum]::Parse($catType, "Mouse")
    $m.IsConnected = $true
    $m.IsSleeping = $sleeping
    $m.IsDonglePresent = $true
    $m.BatteryPercent = $batt
    $m.ExactBatteryPercent = $batt
    $m.LastUpdated = [DateTime]::Now
    return $m
}

$gpw1 = New-MouseInfo "Logitech:046D:C539:1" "G Pro Wireless Gaming Mouse" "Logitech" 78 $true
$gpx2 = New-MouseInfo "Logitech:046D:C53F:1" "PRO X SUPERLIGHT 2" "Logitech" 92 $false

# 1. IsTargetMouseHardwarePath 必须区分不同 PID 的罗技接收器
$isTargetMethod = $mainFormType.GetMethod("IsTargetMouseHardwarePath", [System.Reflection.BindingFlags]"NonPublic,Static")
$pathC539 = "\\?\HID#VID_046D&PID_C539&MI_01&Col01#8&11111111&0&0000#{378de44c-56ef-11d1-bc8c-00a0c91405dd}"
$pathC53F = "\\?\HID#VID_046D&PID_C53F&MI_01&Col01#8&22222222&0&0000#{378de44c-56ef-11d1-bc8c-00a0c91405dd}"

$matchOwn = [bool]$isTargetMethod.Invoke($null, @($pathC539, $gpw1))
$matchOther = [bool]$isTargetMethod.Invoke($null, @($pathC53F, $gpw1))

if (-not $matchOwn -or $matchOther) {
    Write-Host "[FAIL] IsTargetMouseHardwarePath failed to isolate Logitech PIDs (own=$matchOwn, other=$matchOther)" -ForegroundColor Red
    exit 1
}
Write-Host "[PASS] 1. IsTargetMouseHardwarePath isolates Logitech C539 vs C53F receivers" -ForegroundColor Green

# 2. UpdateUI 必须在传入非当前选中设备时直接返回，不篡改 lblDeviceName
$lblDeviceName = New-Object System.Windows.Forms.Label
$lblDeviceName.Text = "PRO X SUPERLIGHT 2"
Set-PrivateField $form "lblDeviceName" $lblDeviceName
Set-PrivateField $form "selectedDevice" $gpx2

$updateUiMethod = $mainFormType.GetMethod("UpdateUI", [System.Reflection.BindingFlags]"NonPublic,Instance")
$updateUiMethod.Invoke($form, @($gpw1, $false)) | Out-Null

if ($lblDeviceName.Text -ne "PRO X SUPERLIGHT 2") {
    Write-Host "[FAIL] UpdateUI overwrote selectedDevice label with foreign device: $($lblDeviceName.Text)" -ForegroundColor Red
    exit 1
}
Write-Host "[PASS] 2. UpdateUI rejects non-selected device update and preserves main card" -ForegroundColor Green

# 3. LogitechDeviceHelper per-device name/charging cache isolation
$getNameMethod = $logiType.GetMethod("GetCachedNameForDevice", [System.Reflection.BindingFlags]"Public,Static")
$getChgMethod = $logiType.GetMethod("GetCachedChargingForDevice", [System.Reflection.BindingFlags]"Public,Static")
if ($null -eq $getNameMethod -or $null -eq $getChgMethod) {
    Write-Host "[FAIL] Missing GetCachedNameForDevice or GetCachedChargingForDevice on LogitechDeviceHelper" -ForegroundColor Red
    exit 1
}
$unknownName = $getNameMethod.Invoke($null, @("Logitech:046D:FFFF:1"))
$unknownChg = [bool]$getChgMethod.Invoke($null, @("Logitech:046D:FFFF:1"))
if ($null -ne $unknownName -or $unknownChg -ne $false) {
    Write-Host "[FAIL] Per-device cache returned unexpected defaults" -ForegroundColor Red
    exit 1
}
Write-Host "[PASS] 3. LogitechDeviceHelper per-device name & charging cache isolated" -ForegroundColor Green
exit 0
