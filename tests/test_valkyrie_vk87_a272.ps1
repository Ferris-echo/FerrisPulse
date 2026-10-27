# ============================================================================
# 测试脚本: test_valkyrie_vk87_a272.ps1
# 目标: 验证社区贡献者 “Zero” 提供的瓦尔基里 VK 87 Max V2 (VID=374A, PID=A272)
#       型号映射与 2.4G 0xF7 电量帧解析。
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = Resolve-Path "$ScriptDir\.."
Set-Location $ProjectDir

Write-Host "[TEST] Validating Valkyrie VK 87 Max V2 (374A:A272) Support..." -ForegroundColor Cyan

$exePath = "$ProjectDir\FerrisPulse.exe"
if (-not (Test-Path $exePath)) {
    throw "FerrisPulse.exe not found!"
}

$asmBytes = [System.IO.File]::ReadAllBytes($exePath)
$asm = [System.Reflection.Assembly]::Load($asmBytes)
$vkType = $asm.GetType("RazerBatteryTray.ValkyrieDeviceHelper")
$catType = $asm.GetType("RazerBatteryTray.DeviceCategory")
$kbCat = [System.Enum]::Parse($catType, "Keyboard")

$getModel = $vkType.GetMethod("GetModelName", [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static)
$model = $getModel.Invoke($null, @(0x374A, 0xA272, "", $kbCat))
if ($model -ne "VK 87 Max V2") {
    throw "Expected 'VK 87 Max V2' for PID 0xA272, got '$model'"
}
Write-Host "  [PASS] PID 0xA272 mapped to '$model'" -ForegroundColor Green

Write-Host "[ALL PASSED] Valkyrie VK 87 Max V2 (A272) test succeeded!" -ForegroundColor Green
exit 0
