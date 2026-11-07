# tests/test_in_game_dpi_osd_polling.ps1
# 验证：
# 1. CalculateAdaptiveDpiPollInterval 具备 isGamingActive 状态感知与变档脉冲感知，绝不被闲置降频至 1000ms
# 2. 在游戏模式或变档脉冲期间，保持 50ms 极速响应

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$asmPath = Join-Path $PSScriptRoot "..\FerrisPulse.exe"
if (-not (Test-Path $asmPath)) { $asmPath = "FerrisPulse.exe" }
$fullPath = (Resolve-Path $asmPath).Path
$bytes = [System.IO.File]::ReadAllBytes($fullPath)
$asm = [System.Reflection.Assembly]::Load($bytes)

$tMain = $asm.GetType("RazerBatteryTray.MainForm")
if ($tMain -eq $null) {
    Write-Host "FAIL: 未能找到 MainForm 类型！" -ForegroundColor Red
    exit 1
}

Write-Host "--- 测试 1: 验证 CalculateAdaptiveDpiPollInterval 在游戏模式下锁定 50ms ---" -ForegroundColor Cyan

$mCalc = $tMain.GetMethod("CalculateAdaptiveDpiPollInterval", [System.Reflection.BindingFlags]'Static,Public,NonPublic', $null, [Type[]]@([double], [bool], [string], [bool], [long]), $null)

if ($mCalc -eq $null) {
    Write-Host "RED LIGHT: 未找到支持 (double, bool, string, bool, long) 签名的 CalculateAdaptiveDpiPollInterval！" -ForegroundColor Yellow
    exit 1
}

# 1. 模拟闲置 60 秒、硬件未休眠、但在电竞游戏中 (isGamingActive = true)
$resGaming = $mCalc.Invoke($null, @([double]60.0, $false, "Razer", $true, [long]0))
if ($resGaming -ne 50) {
    Write-Host "FAIL: 游戏模式下闲置 60 秒应返回 50ms，实际返回: $resGaming" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: 游戏模式下即使闲置 60 秒，仍成功锁定 50ms 极速轮询！" -ForegroundColor Green

# 2. 模拟非游戏模式下闲置 60 秒，但处于变档脉冲活跃期 (burstUntilTicks 为未来时间)
$futureTicks = [DateTime]::UtcNow.Ticks + [TimeSpan]::FromSeconds(5).Ticks
$resBurst = $mCalc.Invoke($null, @([double]60.0, $false, "Razer", $false, $futureTicks))
if ($resBurst -ne 50) {
    Write-Host "FAIL: 变档脉冲活跃期应返回 50ms，实际返回: $resBurst" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: 变档脉冲活跃期成功返回 50ms 极速轮询！" -ForegroundColor Green

# 3. 模拟非游戏模式、无脉冲、闲置 60 秒 -> 应恢复 1000ms 节能
$resIdle = $mCalc.Invoke($null, @([double]60.0, $false, "Razer", $false, [long]0))
if ($resIdle -ne 1000) {
    Write-Host "FAIL: 非游戏模式闲置 60 秒应返回 1000ms，实际返回: $resIdle" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: 非游戏模式闲置 60 秒正常降级至 1000ms 节能模式" -ForegroundColor Green

# 4. 模拟雷柏设备在游戏模式下返回 200ms
$resRapoo = $mCalc.Invoke($null, @([double]60.0, $false, "Rapoo", $true, [long]0))
if ($resRapoo -ne 200) {
    Write-Host "FAIL: 雷柏在游戏模式下应返回 200ms，实际返回: $resRapoo" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: 雷柏在游戏模式下安全保持 200ms 通信保护！" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " ALL PASS: 游戏模式 DPI 轮询锁定与脉冲机制验证全部通过！" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
exit 0
