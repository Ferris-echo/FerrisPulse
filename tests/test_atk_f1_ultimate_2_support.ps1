# ============================================================================
# 测试脚本: test_atk_f1_ultimate_2_support.ps1
# 目标: 验证社区贡献者 “好的很” 提供的 ATK F1 Ultimate 2.0 (373B:11D9)
#       型号目录映射、Beken 17 字节电量响应帧解析与校验和算法。
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = Resolve-Path "$ScriptDir\.."
Set-Location $ProjectDir

Write-Host "[TEST] Validating ATK F1 Ultimate 2.0 (373B:11D9) Support..." -ForegroundColor Cyan

$exePath = "$ProjectDir\FerrisPulse.exe"
if (-not (Test-Path $exePath)) {
    throw "FerrisPulse.exe not found!"
}

$asmBytes = [System.IO.File]::ReadAllBytes($exePath)
$asm = [System.Reflection.Assembly]::Load($asmBytes)
$ugType = $asm.GetType("RazerBatteryTray.UniversalGamingDeviceHelper")
if ($null -eq $ugType) {
    throw "UniversalGamingDeviceHelper type not found!"
}

$lookupMethod = $ugType.GetMethod("LookupProduct", [System.Reflection.BindingFlags]"Public,Static")
$product = $lookupMethod.Invoke($null, @(0x373B, 0x11D9, "8k dongle-L"))
if ($product.Brand -ne "ATK" -or $product.ModelName -ne "ATK F1 Ultimate 2.0" -or $product.MaxPollingRate -ne 8000) {
    throw "373B:11D9 LookupProduct mismatch: Brand=$($product.Brand), Model=$($product.ModelName), MaxPoll=$($product.MaxPollingRate)"
}
Write-Host "  [PASS] 373B:11D9 mapped to 'ATK F1 Ultimate 2.0' (8000Hz)" -ForegroundColor Green

$calcMethod = $ugType.GetMethod("CalculateWeChecksum", [System.Reflection.BindingFlags]"Public,Static")
[byte[]]$data15 = @(0x04,0,0,0,0,0,0,0,0,0,0,0,0,0,0)
$crc = $calcMethod.Invoke($null, @([byte]0x08, $data15))
if ($crc -ne 0x49) {
    throw "CalculateWeChecksum(0x08, [0x04...]) expected 0x49, got 0x$($crc.ToString('X2'))"
}
Write-Host "  [PASS] Beken 0x55 checksum validated (0x49)" -ForegroundColor Green

$parseMethod = $ugType.GetMethod("ParseBekenWeBatteryResponse", [System.Reflection.BindingFlags]"Public,Static")
[byte[]]$rxReal = @(0x08, 0x04, 0x00, 0x00, 0x00, 0x02, 0x4B, 0x00, 0x0F, 0x5C, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0xF2)
[object[]]$args = @($rxReal, 17, 0, $false, 0)
$ok = $parseMethod.Invoke($null, $args)
$batt = [int]$args[2]
$chg = [bool]$args[3]
if (-not $ok -or $batt -ne 75 -or $chg -ne $false) {
    throw "ParseBekenWeBatteryResponse real frame failed: ok=$ok, batt=$batt, chg=$chg"
}
Write-Host "  [PASS] Real hardware response (08 04 00 00 00 02 4B 00 0F 5C ...) parsed -> 75%, not charging" -ForegroundColor Green

Write-Host "[ALL PASSED] ATK F1 Ultimate 2.0 (373B:11D9) test suite succeeded!" -ForegroundColor Green
exit 0
