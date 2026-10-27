# ============================================================================
# 测试脚本: test_acer_support.ps1
# 目标: 验证社区贡献者 “Zero” 提供的宏碁 (Acer) OMR331 旗舰版无线鼠标及全系 OMR 鼠标
#       33 字节 0x10 协议构建、实机响应帧解析（含防充电误跳变）与型号映射。
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = Resolve-Path "$ScriptDir\.."
Set-Location $ProjectDir

Write-Host "[TEST] Validating Acer OMR331 & OMR Series Mouse Protocol..." -ForegroundColor Cyan

$exePath = "$ProjectDir\FerrisPulse.exe"
if (-not (Test-Path $exePath)) {
    throw "FerrisPulse.exe not found!"
}

$asmBytes = [System.IO.File]::ReadAllBytes($exePath)
$asm = [System.Reflection.Assembly]::Load($asmBytes)
$acerType = $asm.GetType("RazerBatteryTray.AcerDeviceHelper")
if ($null -eq $acerType) {
    throw "AcerDeviceHelper type not found in assembly!"
}

# 1. Test BuildDeviceInfoQuery
$buildMethod = $acerType.GetMethod("BuildDeviceInfoQuery", [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static)
[byte[]]$query = $buildMethod.Invoke($null, $null)
if ($query.Length -ne 33 -or $query[0] -ne 0x00 -or $query[1] -ne 0x10) {
    throw "BuildDeviceInfoQuery produced invalid frame!"
}
Write-Host "  [PASS] BuildDeviceInfoQuery: 33-byte 0x00 0x10 frame verified." -ForegroundColor Green

# 2. Test ParseDeviceInfoResponse with real captured hardware packet from OMR331 Flagship (dev_id = R332, battery = 0x55 = 85%)
# Real packet: 00-10-00-01-0B-52-33-33-32-01-00-00-01-01-55-01-00...
[byte[]]$realRx = New-Object byte[] 33
$realRx[0] = 0x00
$realRx[1] = 0x10
$realRx[2] = 0x00
$realRx[3] = 0x01
$realRx[4] = 0x0B
$realRx[5] = 0x52 # 'R'
$realRx[6] = 0x33 # '3'
$realRx[7] = 0x33 # '3'
$realRx[8] = 0x32 # '2'
$realRx[9] = 0x01
$realRx[10] = 0x00
$realRx[11] = 0x00
$realRx[12] = 0x01
$realRx[13] = 0x01
$realRx[14] = 0x55 # 85%
$realRx[15] = 0x01 # Wireless link status bit (must NOT trigger false charging)

$parseMethod = $acerType.GetMethod("ParseDeviceInfoResponse", [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static)
$args = @($realRx, 33, 0, $false, $null)
$parsedOk = $parseMethod.Invoke($null, $args)
$batt = $args[2]
$chg = $args[3]
$devId = $args[4]

if (-not $parsedOk) { throw "ParseDeviceInfoResponse returned false on valid real packet!" }
if ($batt -ne 85) { throw "Expected battery 85%, got $batt%" }
if ($chg -ne $false) { throw "Expected isCharging=false on wireless dongle packet (even when byte[15]==1), got $chg" }
if ($devId -ne "R332") { throw "Expected devId 'R332', got '$devId'" }
Write-Host "  [PASS] ParseDeviceInfoResponse: Real packet -> devId='R332', Battery=85%, IsCharging=False." -ForegroundColor Green

# 3. Test GetModelNameByDevId
$nameMethod = $acerType.GetMethod("GetModelNameByDevId", [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static)
$modelName = $nameMethod.Invoke($null, @("R332", "Wireless Gaming Mouse"))
if ($modelName -ne "Acer OMR331 旗舰版无线鼠标") {
    throw "Expected 'Acer OMR331 旗舰版无线鼠标', got '$modelName'"
}
Write-Host "  [PASS] GetModelNameByDevId('R332') -> '$modelName'." -ForegroundColor Green

Write-Host "[ALL PASSED] Acer OMR331 support test suite succeeded!" -ForegroundColor Green
exit 0
