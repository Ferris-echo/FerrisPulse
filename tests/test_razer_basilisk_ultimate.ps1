# ============================================================================
# 测试脚本: test_razer_basilisk_ultimate.ps1
# 覆盖契约 (PR #73):
#   1. NormalizeRazerPid(0x0088) 归一化到 0x0086 (Basilisk Ultimate Wired)
#   2. 0x0086 与 0x0088 的官方型号名称解析为 "Razer Basilisk Ultimate"
#   3. 0x0086 支持经典 125/250/500/1000Hz 回报率列表
# ============================================================================

$ErrorActionPreference = "Stop"
$exePath = Join-Path $PSScriptRoot "..\FerrisPulse.exe"
if (-not (Test-Path $exePath)) {
    Write-Host "[FAIL] FerrisPulse.exe not found" -ForegroundColor Red
    exit 1
}

$bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $exePath))
$asm = [System.Reflection.Assembly]::Load($bytes)
$rzType = $asm.GetType("RazerBatteryTray.RazerDeviceHelper")

$normPid = [int]$rzType.GetMethod("NormalizeRazerPid", [System.Reflection.BindingFlags]"Public,Static").Invoke($null, @(0x0088))
if ($normPid -ne 0x0086) {
    Write-Host ("[FAIL] NormalizeRazerPid(0x0088) expected 0x0086, got 0x{0:X4}" -f $normPid) -ForegroundColor Red
    exit 1
}
Write-Host "[PASS] 1. NormalizeRazerPid(0x0088) -> 0x0086" -ForegroundColor Green

$getNameMethod = $rzType.GetMethod("GetRazerOfficialModelName", [System.Reflection.BindingFlags]"Public,Static")
$name86 = [string]$getNameMethod.Invoke($null, @(0x0086, $null))
$name88 = [string]$getNameMethod.Invoke($null, @(0x0088, $null))
if ($name86 -ne "Razer Basilisk Ultimate" -or $name88 -ne "Razer Basilisk Ultimate") {
    Write-Host "[FAIL] Expected 'Razer Basilisk Ultimate', got 0086='$name86', 0088='$name88'" -ForegroundColor Red
    exit 1
}
Write-Host "[PASS] 2. PID 0x0086 & 0x0088 resolve to 'Razer Basilisk Ultimate'" -ForegroundColor Green

$getRatesMethod = $rzType.GetMethod("GetSupportedPollingRatesForModel", [System.Reflection.BindingFlags]"Public,Static")
$rates = [int[]]$getRatesMethod.Invoke($null, @(0x0086, "Razer Basilisk Ultimate"))
if ($null -eq $rates -or ($rates -notcontains 125) -or ($rates -notcontains 500) -or ($rates -notcontains 1000)) {
    Write-Host "[FAIL] Unexpected supported polling rates for 0x0086" -ForegroundColor Red
    exit 1
}
Write-Host "[PASS] 3. Basilisk Ultimate (0x0086) polling rate list verified" -ForegroundColor Green
exit 0
