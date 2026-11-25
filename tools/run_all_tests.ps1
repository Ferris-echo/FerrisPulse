# run_all_tests.ps1
param(
    [string]$Filter = "test_*.ps1"
)
$testFiles = Get-ChildItem (Join-Path $PSScriptRoot "..\tests\$Filter") | Sort-Object Name
$allPassed = $true
$summary = @()

# 0. 个别测试需要真实硬件往返（每次 0x4D 查询在设备未应答时会把
#    BusyRetryCount(8) × BusyRetryDelayMs(90) 的重试全部耗完，单次约 4.2 秒），
#    12 秒全局看门狗对它们必然误杀。这里给它们单独的宽限预算，
#    其余测试仍保持 12 秒严格看门狗。
$testTimeouts = @{
    "test_mchose_onboard_truthfulness.ps1" = 60000
}
$defaultTimeoutMs = 12000

# 1. 强制开启测试硬件写入沙盒与生产注册表快照保护（严禁测试改写实机 DPI / 回报率 / 键盘灯光及注册表）
$prevTestMode = $env:FERRISPULSE_TEST_MODE
$env:FERRISPULSE_TEST_MODE = "1"
. (Join-Path $PSScriptRoot "TestRegistryGuard.ps1")
$regSnapshot = Save-TestRegistrySnapshot -Roots @("HKCU:\Software\FerrisPulse", "HKCU:\Software\RazerBatteryTray")
$suiteWatch = [System.Diagnostics.Stopwatch]::StartNew()

try {
    foreach ($tf in $testFiles) {
        Write-Host ("[TESTING] " + $tf.Name) -ForegroundColor Cyan
        $caseWatch = [System.Diagnostics.Stopwatch]::StartNew()
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = "powershell.exe"
        $psi.Arguments = "-ExecutionPolicy Bypass -NoProfile -File `"$($tf.FullName)`""
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.StandardOutputEncoding = [System.Text.Encoding]::UTF8
        $psi.StandardErrorEncoding = [System.Text.Encoding]::UTF8
        $psi.CreateNoWindow = $true
        
        $p = [System.Diagnostics.Process]::Start($psi)
        $stdoutTask = $p.StandardOutput.ReadToEndAsync()
        $stderrTask = $p.StandardError.ReadToEndAsync()
        $budgetMs = $defaultTimeoutMs
        if ($testTimeouts.ContainsKey($tf.Name)) { $budgetMs = $testTimeouts[$tf.Name] }
        $finished = $p.WaitForExit($budgetMs) # 看门狗超时，防止任何测试脚本无休止卡死
        $caseWatch.Stop()
        if (-not $finished) {
            try { $p.Kill() } catch { }
            $allPassed = $false
            $summary += "[TIMEOUT (>$([int]($budgetMs/1000))s)] " + $tf.Name
            Write-Host " -> TIMEOUT (>$([int]($budgetMs/1000))s): 强制终止防卡死" -ForegroundColor Red
            continue
        }

        if ($p.ExitCode -eq 0) {
            $summary += ("[PASS {0}ms] {1}" -f $caseWatch.ElapsedMilliseconds, $tf.Name)
            Write-Host (" -> OK ({0}ms)" -f $caseWatch.ElapsedMilliseconds) -ForegroundColor Green
        } else {
            $allPassed = $false
            $summary += ("[FAIL ({0})] {1}" -f $p.ExitCode, $tf.Name)
            Write-Host " -> FAILED with code " + $p.ExitCode -ForegroundColor Red
            Write-Host ($stdoutTask.Result) -ForegroundColor Gray
            Write-Host ($stderrTask.Result) -ForegroundColor Magenta
        }
    }

    $suiteWatch.Stop()
    Write-Host ("`n================ TEST SUITE SUMMARY ({0} files, {1:F1}s) ================" -f $testFiles.Count, $suiteWatch.Elapsed.TotalSeconds) -ForegroundColor Yellow
    $summary | ForEach-Object { Write-Host $_ }
}
finally {
    if ($null -eq $prevTestMode) {
        Remove-Item Env:FERRISPULSE_TEST_MODE -ErrorAction SilentlyContinue
    } else {
        $env:FERRISPULSE_TEST_MODE = $prevTestMode
    }
    # 彻底恢复用户生产环境注册表，杜绝自动化测试篡改用户的电竞优化与外设记忆
    Restore-TestRegistrySnapshot -Snapshot $regSnapshot
    Write-Host "`n[SAFEGUARD] 已退出测试硬件沙盒并回滚用户生产注册表快照（应用设置 + 外设优先级与状态记忆）。" -ForegroundColor DarkGray
}

if (-not $allPassed) {
    exit 1
} else {
    exit 0
}
