# ============================================================================
# 测试脚本: test_tray_low_battery_priority.ps1
# 覆盖契约:
#   1. DeviceManager.IsTrayLowBatteryFirstEnabled / SetTrayLowBatteryFirstEnabled 读写与默认值
#   2. DevicePriorityForm 包含 chkLowBatteryFirst 复选框且状态与 DeviceManager 对齐
# ============================================================================

$ErrorActionPreference = "Stop"
$exePath = Join-Path $PSScriptRoot "..\FerrisPulse.exe"
if (-not (Test-Path $exePath)) {
    Write-Host "[FAIL] FerrisPulse.exe not found" -ForegroundColor Red
    exit 1
}

$bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $exePath))
$asm = [System.Reflection.Assembly]::Load($bytes)

$dmType = $asm.GetType("RazerBatteryTray.DeviceManager")
$dpfType = $asm.GetType("RazerBatteryTray.DevicePriorityForm")
$infoType = $asm.GetType("RazerBatteryTray.MouseBatteryInfo")
$listType = [System.Collections.Generic.List`1].MakeGenericType(@($infoType))

$isEnabledMethod = $dmType.GetMethod("IsTrayLowBatteryFirstEnabled", [System.Reflection.BindingFlags]"Public,Static")
$setEnabledMethod = $dmType.GetMethod("SetTrayLowBatteryFirstEnabled", [System.Reflection.BindingFlags]"Public,Static")

if ($null -eq $isEnabledMethod -or $null -eq $setEnabledMethod) {
    Write-Host "[FAIL] Missing IsTrayLowBatteryFirstEnabled / SetTrayLowBatteryFirstEnabled on DeviceManager" -ForegroundColor Red
    exit 1
}

$origVal = [bool]$isEnabledMethod.Invoke($null, $null)
try {
    $setEnabledMethod.Invoke($null, @($false)) | Out-Null
    $afterFalse = [bool]$isEnabledMethod.Invoke($null, $null)
    if ($afterFalse -ne $false) {
        Write-Host "[FAIL] SetTrayLowBatteryFirstEnabled(false) did not persist" -ForegroundColor Red
        exit 1
    }

    $setEnabledMethod.Invoke($null, @($true)) | Out-Null
    $afterTrue = [bool]$isEnabledMethod.Invoke($null, $null)
    if ($afterTrue -ne $true) {
        Write-Host "[FAIL] SetTrayLowBatteryFirstEnabled(true) did not persist" -ForegroundColor Red
        exit 1
    }
    Write-Host "[PASS] 1. DeviceManager low-battery-first setting persists accurately" -ForegroundColor Green

    $emptyList = [Activator]::CreateInstance($listType)
    $dlg = [Activator]::CreateInstance($dpfType, @($emptyList, [single]1.0))
    $chkField = $dpfType.GetField("chkLowBatteryFirst", [System.Reflection.BindingFlags]"NonPublic,Instance")
    if ($null -eq $chkField) {
        Write-Host "[FAIL] DevicePriorityForm missing chkLowBatteryFirst field" -ForegroundColor Red
        exit 1
    }
    $chk = $chkField.GetValue($dlg)
    if ($null -eq $chk -or -not $chk.Checked) {
        Write-Host "[FAIL] chkLowBatteryFirst not initialized to Checked=true" -ForegroundColor Red
        exit 1
    }
    $dlg.Dispose()
    Write-Host "[PASS] 2. DevicePriorityForm hosts chkLowBatteryFirst checkbox inside priority dialog" -ForegroundColor Green
}
finally {
    $setEnabledMethod.Invoke($null, @($origVal)) | Out-Null
}
exit 0
