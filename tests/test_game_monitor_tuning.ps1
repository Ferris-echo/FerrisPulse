# tests/test_game_monitor_tuning.ps1
# P4b 验证：游戏侦测提速（500ms 周期 + 线程池探测在途闸门 + 第 1 拍预取票）
# 断言口径：
#   1) 侦测周期常量为 500ms，且定时器使用该常量；
#   2) 探测跑线程池并带在途闸门，结果回 UI 线程推进状态机（状态变更仍只在 UI 线程）；
#   3) 预取票（GameOptTicket）一次性消费、设备/开关/时效三项校验；
#   4) 负向探测不触发下发，计数归零。
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "..\tools\TestHarness.ps1")

$asm = Load-FerrisPulseAssembly
$root = Get-FerrisPulseRoot
$BF = [System.Reflection.BindingFlags]"Public,NonPublic,Instance,Static"
$tMain = $asm.GetType("RazerBatteryTray.MainForm", $true)
$tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo", $true)

Invoke-TestCase "1. 侦测周期常量与预取票类型契约" {
    $fInterval = $tMain.GetField("GameMonitorIntervalMs", [System.Reflection.BindingFlags]"Public,Static")
    Assert-True ($null -ne $fInterval) "GameMonitorIntervalMs const exists"
    Assert-Equal 500 ([int]$fInterval.GetRawConstantValue()) "game monitor interval is 500ms"

    $tTicket = $tMain.GetNestedType("GameOptTicket", [System.Reflection.BindingFlags]"NonPublic")
    Assert-True ($null -ne $tTicket) "GameOptTicket nested type exists"
    foreach ($f in @("DeviceId","ApplyRate","ApplyDpi","TargetHz","TargetDpi","TargetDpiY","TargetStage","ActiveProfile","PreparedUtc")) {
        Assert-True ($null -ne $tTicket.GetField($f, $BF)) ("GameOptTicket." + $f + " present")
    }
}

Invoke-TestCase "2. 预取票一次性消费与三项校验" {
    $form = New-SandboxMainForm -AutoStart $false
    try {
        $tTicket = $tMain.GetNestedType("GameOptTicket", [System.Reflection.BindingFlags]"NonPublic")
        $mStore = $tMain.GetMethod("StorePendingGameOptTicket", $BF)
        $mTake = $tMain.GetMethod("TakePendingGameOptTicket", $BF)
        $mDiscard = $tMain.GetMethod("DiscardPendingGameOptTicket", $BF)
        $rateOn = [bool]$tMain.GetField("autoGameRateEnabled", $BF).GetValue($form)
        $dpiOn = [bool]$tMain.GetField("autoGameDpiEnabled", $BF).GetValue($form)

        $devA = [Activator]::CreateInstance($tInfo); $devA.DeviceId = "Test:P4B:A"
        $devB = [Activator]::CreateInstance($tInfo); $devB.DeviceId = "Test:P4B:B"

        $mk = {
            param($deviceId, $ageSec)
            $tk = [Activator]::CreateInstance($tTicket)
            $tk.DeviceId = $deviceId
            $tk.ApplyRate = $rateOn
            $tk.ApplyDpi = $dpiOn
            $tk.TargetHz = 4000; $tk.TargetDpi = 3200; $tk.TargetDpiY = 3200; $tk.TargetStage = 3
            $tk.PreparedUtc = [DateTime]::UtcNow.AddSeconds(-1 * $ageSec)
            return $tk
        }

        $mStore.Invoke($form, @((& $mk "Test:P4B:A" 0))) | Out-Null
        $hit = $mTake.Invoke($form, @($devA))
        Assert-True ($null -ne $hit -and [int]$hit.TargetHz -eq 4000) "fresh matching ticket handed out"
        Assert-True ($null -eq ($mTake.Invoke($form, @($devA)))) "ticket is consumed exactly once"

        $mStore.Invoke($form, @((& $mk "Test:P4B:B" 0))) | Out-Null
        Assert-True ($null -eq ($mTake.Invoke($form, @($devA)))) "device mismatch invalidates ticket"
        $mDiscard.Invoke($form, @()) | Out-Null

        $mStore.Invoke($form, @((& $mk "Test:P4B:A" 10))) | Out-Null
        Assert-True ($null -eq ($mTake.Invoke($form, @($devA)))) "stale ticket invalidated"
        $mDiscard.Invoke($form, @()) | Out-Null

        $stale = & $mk "Test:P4B:A" 0
        $stale.ApplyRate = -not $rateOn
        $stale.ApplyDpi = -not $dpiOn
        $mStore.Invoke($form, @($stale)) | Out-Null
        Assert-True ($null -eq ($mTake.Invoke($form, @($devA)))) "switch change invalidates ticket"
        $mDiscard.Invoke($form, @()) | Out-Null
    } finally { $form.Dispose() }
}

Invoke-TestCase "3. 负向探测不触发下发且计数归零" {
    $form = New-SandboxMainForm -AutoStart $false
    try {
        $mProbe = $tMain.GetMethod("OnGameProbeCompleted", $BF)
        $tMain.GetField("inGameTickCount", $BF).SetValue($form, 1)
        $tMain.GetField("outOfGameTickCount", $BF).SetValue($form, 1)
        $mProbe.Invoke($form, @([bool]$false, $null, "Test:P4B:NONE")) | Out-Null
        Assert-Equal 0 ([int]$tMain.GetField("inGameTickCount", $BF).GetValue($form)) "in-game counter reset"
        Assert-Equal 0 ([int]$tMain.GetField("outOfGameTickCount", $BF).GetValue($form)) "out-of-game counter reset"
        Assert-True (-not [bool]$tMain.GetField("isGamingActive", $BF).GetValue($form)) "isGamingActive stays false"
    } finally { $form.Dispose() }
}

Invoke-TestCase "4. 源码契约：线程池探测 + 在途闸门 + 预取票接入" {
    $src = [System.IO.File]::ReadAllText((Join-Path $root "src\UI\Forms\MainForm.Hardware.cs"), [System.Text.Encoding]::UTF8)
    Assert-True ($src.Contains("gameMonitorTimer.Interval = GameMonitorIntervalMs;")) "timer uses the 500ms constant"
    Assert-True ($src.Contains("ThreadPool.QueueUserWorkItem")) "detection moved off the UI thread"
    Assert-True ($src.Contains("CompareExchange(ref gameDetectInFlight, 1, 0)")) "in-flight gate prevents probe pileup"
    Assert-True ($src.Contains("PrepareGameOptimization()")) "first-hit prefetch wired"
    Assert-True ($src.Contains("ApplyGameOptimizationWithPrefetch(true, TakePendingGameOptTicket(curDev))")) "confirmation consumes the ticket"
    Assert-True ($src.Contains("DiscardPendingGameOptTicket();")) "negative ticks discard the ticket"
}

Write-Host ""
Write-Host "ALL GAME MONITOR TUNING TESTS PASSED." -ForegroundColor Green
exit 0
