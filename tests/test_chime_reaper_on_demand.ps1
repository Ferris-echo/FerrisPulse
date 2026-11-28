# tests/test_chime_reaper_on_demand.ps1
# P5 验证：提示音声部回收器按需启停（类型构造不再常驻 250ms 定时器）
# 断言口径（docs/OPTIMIZATION_BACKLOG.md P5）：
#   1) 类型构造后不创建定时器；
#   2) EnsureVoiceReaperRunning 按需起表且不销毁实例，StopVoiceReaperIfIdle 在声部为空时停表；
#   3) 源码契约：定时器只有一处创建、入队处起表、回收结束判停。
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "..\tools\TestHarness.ps1")

$asm = Load-FerrisPulseAssembly
$root = Get-FerrisPulseRoot
$BF = [System.Reflection.BindingFlags]"Public,NonPublic,Instance,Static"
$tChime = $asm.GetType("RazerBatteryTray.ChimeSynthesizer", $true)
$fTimer = $tChime.GetField("voiceReaperTimer", $BF)
$fVoices = $tChime.GetField("activeVoices", $BF)
$mEnsure = $tChime.GetMethod("EnsureVoiceReaperRunning", $BF)
$mStop = $tChime.GetMethod("StopVoiceReaperIfIdle", $BF)

Invoke-TestCase "1. 类型构造后不常驻定时器" {
    Assert-True ($null -eq $fTimer.GetValue($null)) "no reaper timer created by the static constructor"
}

Invoke-TestCase "2. 按需起表且停表不销毁实例" {
    $mEnsure.Invoke($null, @()) | Out-Null
    $timer = $fTimer.GetValue($null)
    Assert-True ($null -ne $timer) "reaper timer created on demand"
    $mStop.Invoke($null, @()) | Out-Null
    Assert-True ([object]::ReferenceEquals($timer, $fTimer.GetValue($null))) "stop keeps the same timer instance"
    Assert-Equal 0 ($fVoices.GetValue($null)).Count "no active voices during the test"
}

Invoke-TestCase "3. 源码契约：单点创建 + 入队起表 + 回收判停" {
    $src = [System.IO.File]::ReadAllText((Join-Path $root "src\Services\ChimeSynthesizer.cs"), [System.Text.Encoding]::UTF8)
    Assert-Equal 1 (([regex]::Matches($src, [regex]::Escape("new Timer(OnVoiceReaperTick"))).Count) "timer created in exactly one place"
    Assert-True ($src.Contains("EnsureVoiceReaperRunning();")) "enqueue path starts the reaper"
    Assert-True ($src.Contains("StopVoiceReaperIfIdle();")) "reaper tick stops itself when idle"
    $ctorArmed = [regex]::Matches($src, [regex]::Escape("voiceReaperTimer = new Timer(OnVoiceReaperTick, null, 250, 250);")).Count
    Assert-Equal 1 $ctorArmed "only EnsureVoiceReaperRunning arms the timer with a due time"
}

Write-Host ""
Write-Host "ALL CHIME REAPER ON-DEMAND TESTS PASSED." -ForegroundColor Green
exit 0
