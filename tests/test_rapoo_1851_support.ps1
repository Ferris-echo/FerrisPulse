# ============================================================================
# 测试脚本: test_rapoo_1851_support.ps1
# 目标: 回放验证雷柏 24AE:1851 (VT 系列 2.4G 接收器) 的 0xBA 协议解析：
#       电量/充电状态帧、7 档 DPI 表、当前档位、回报率编码。
# 数据来源: 实机抓包 0xBB 周期帧与 0xBA EEPROM 回读。
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = Resolve-Path "$ScriptDir\.."
Set-Location $ProjectDir

Write-Host "[TEST] Validating Rapoo 24AE:1851 (VT 2.4G) protocol..." -ForegroundColor Cyan

$exePath = "$ProjectDir\FerrisPulse.exe"
if (-not (Test-Path $exePath)) {
    throw "FerrisPulse.exe not found!"
}

$asmBytes = [System.IO.File]::ReadAllBytes($exePath)
$asm = [System.Reflection.Assembly]::Load($asmBytes)
$type = $asm.GetType("RazerBatteryTray.RapooVt1851DeviceHelper")
if ($null -eq $type) {
    throw "RapooVt1851DeviceHelper type not found!"
}

$parseBattery = $type.GetMethod("TryParseBatteryResponse", [System.Reflection.BindingFlags]"Public,Static")
[byte[]]$batteryDischarging = @(0x01, 0x01, 0x5B)
[object[]]$batteryArgs = @($batteryDischarging, 0, 0)
$ok = $parseBattery.Invoke($null, $batteryArgs)
if (-not $ok -or [int]$batteryArgs[1] -ne 91 -or [int]$batteryArgs[2] -ne 1) {
    throw "Battery discharge frame failed: ok=$ok, percent=$($batteryArgs[1]), state=$($batteryArgs[2])"
}
Write-Host "  [PASS] 0xBB battery frame (01 01 5B) -> 91%, discharging" -ForegroundColor Green

[byte[]]$batteryCharging = @(0x01, 0x02, 0x64)
[object[]]$chargingArgs = @($batteryCharging, 0, 0)
$ok = $parseBattery.Invoke($null, $chargingArgs)
if (-not $ok -or [int]$chargingArgs[1] -ne 100 -or [int]$chargingArgs[2] -ne 2) {
    throw "Battery charging frame failed: ok=$ok, percent=$($chargingArgs[1]), state=$($chargingArgs[2])"
}
Write-Host "  [PASS] 0xBB battery frame (01 02 64) -> 100%, charging" -ForegroundColor Green

[byte[]]$busyFrame = @(0x02, 0x01, 0x5B)
[object[]]$busyArgs = @($busyFrame, 0, 0)
$ok = $parseBattery.Invoke($null, $busyArgs)
if ($ok) {
    throw "Busy response (status 0x02) must be rejected!"
}
Write-Host "  [PASS] status 0x02 busy response rejected" -ForegroundColor Green

$parseDpi = $type.GetMethod("TryParseDpiBlock", [System.Reflection.BindingFlags]"Public,Static")
[byte[]]$dpiBlock = @(0x10,0x00,0x18,0x00,0x20,0x00,0x28,0x00,0x40,0x00,0x80,0x00,0x08,0x02,0x06,0x00,0x03,0x00,0x02,0x03)
[object[]]$dpiArgs = @($dpiBlock, $null, 0, 0)
$ok = $parseDpi.Invoke($null, $dpiArgs)
[int[]]$expectedDpi = @(800,1200,1600,2000,3200,6400,26000)
$stages = [int[]]$dpiArgs[1]
$count = [int]$dpiArgs[2]
$active = [int]$dpiArgs[3]
if (-not $ok -or $count -ne 7 -or $active -ne 3) {
    throw "DPI block metadata failed: ok=$ok, count=$count, active=$active"
}
for ($i = 0; $i -lt $expectedDpi.Count; $i++) {
    if ($stages[$i] -ne $expectedDpi[$i]) {
        throw "DPI stage $i mismatch: expected $($expectedDpi[$i]), got $($stages[$i])"
    }
}
Write-Host "  [PASS] 7-stage DPI table -> 800/1200/1600/2000/3200/6400/26000, active index 3" -ForegroundColor Green

$encodePoll = $type.GetMethod("EncodePollingRate", [System.Reflection.BindingFlags]"Public,Static")
$decodePoll = $type.GetMethod("DecodePollingRate", [System.Reflection.BindingFlags]"Public,Static")
[byte]$encoded = $encodePoll.Invoke($null, @(8000))
if ($encoded -ne 0x83) {
    throw "EncodePollingRate(8000) expected 0x83, got 0x$($encoded.ToString('X2'))"
}
$decoded = [int]$decodePoll.Invoke($null, @([byte]0x83))
if ($decoded -ne 8000) {
    throw "DecodePollingRate(0x83) expected 8000, got $decoded"
}
$legacy8000 = [int]$decodePoll.Invoke($null, @([byte]0x81))
if ($legacy8000 -ne 8000) {
    throw "DecodePollingRate(0x81) expected 8000, got $legacy8000"
}
Write-Host "  [PASS] polling rate 8000Hz encodes to 0x83 and decodes (0x83/0x81)" -ForegroundColor Green

$supported = $type.GetMethod("IsSupportedDevice", [System.Reflection.BindingFlags]"Public,Static")
if (-not $supported.Invoke($null, @(0x24AE, 0x1851))) {
    throw "24AE:1851 must be supported"
}
if ($supported.Invoke($null, @(0x24AE, 0x1231))) {
    throw "24AE:1231 must not be claimed by the 1851 helper"
}
Write-Host "  [PASS] VID/PID isolation: 24AE:1851 accepted, 24AE:1231 rejected" -ForegroundColor Green

Write-Host "[ALL PASSED] Rapoo 24AE:1851 protocol test suite succeeded!" -ForegroundColor Green
exit 0
