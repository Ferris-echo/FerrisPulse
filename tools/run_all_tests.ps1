# run_all_tests.ps1
param(
    [string]$Filter = "test_*.ps1"
)
$testFiles = Get-ChildItem (Join-Path $PSScriptRoot "..\tests\$Filter") | Sort-Object Name
$allPassed = $true
$summary = @()
$passCount = 0
$expectedFailCount = 0
$regressionCount = 0

# 0. 个别测试需要真实硬件往返（每次 0x4D 查询在设备未应答时会把
#    BusyRetryCount(8) × BusyRetryDelayMs(90) 的重试全部耗完，单次约 4.2 秒），
#    12 秒全局看门狗对它们必然误杀。这里给它们单独的宽限预算，
#    其余测试仍保持 12 秒严格看门狗。
$testTimeouts = @{
    # M1 合流用例需要真实的"在途扫描 + 补跑"，单次扫描实测约 5s，故给 45s 宽限
    "test_scan_coalescing.ps1" = 45000
}
$defaultTimeoutMs = 12000

# 预期失败识别：用例文件头 8 行内出现 "# EXPECTED-FAIL: 原因" 即视为已登记的预期失败，
# 不计入回归失败；但若这类用例反而通过，会在汇总里标注（提示可以摘掉标记）。
function Test-ExpectedFail([string]$path) {
    try {
        $head = Get-Content -Path $path -TotalCount 8 -ErrorAction Stop
        foreach ($line in $head) {
            if ($line -match "^\s*#\s*EXPECTED-FAIL\s*:") { return $true }
        }
    } catch { }
    return $false
}

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
            $regressionCount++
            $summary += "[TIMEOUT (>$([int]($budgetMs/1000))s)] " + $tf.Name
            Write-Host " -> TIMEOUT (>$([int]($budgetMs/1000))s): 强制终止防卡死" -ForegroundColor Red
            continue
        }

        $declaredExpected = Test-ExpectedFail $tf.FullName
        if ($p.ExitCode -eq 0) {
            $passCount++
            if ($declaredExpected) {
                $summary += ("[PASS-BUT-EXPECTED-FAIL {0}ms] {1}（可摘掉文件头的 EXPECTED-FAIL 标记）" -f $caseWatch.ElapsedMilliseconds, $tf.Name)
                Write-Host (" -> OK ({0}ms) 但该用例登记为预期失败，建议摘标记" -f $caseWatch.ElapsedMilliseconds) -ForegroundColor Yellow
            } else {
                $summary += ("[PASS {0}ms] {1}" -f $caseWatch.ElapsedMilliseconds, $tf.Name)
                Write-Host (" -> OK ({0}ms)" -f $caseWatch.ElapsedMilliseconds) -ForegroundColor Green
            }
        } elseif ($declaredExpected) {
            $expectedFailCount++
            $summary += ("[EXPECTED-FAIL ({0})] {1}" -f $p.ExitCode, $tf.Name)
            Write-Host (" -> EXPECTED-FAIL (code {0})：既登记失败的已知问题，不计回归" -f $p.ExitCode) -ForegroundColor DarkYellow
        } else {
            $allPassed = $false
            $regressionCount++
            $summary += ("[FAIL ({0})] {1}" -f $p.ExitCode, $tf.Name)
            Write-Host " -> FAILED with code " + $p.ExitCode -ForegroundColor Red
            Write-Host ($stdoutTask.Result) -ForegroundColor Gray
            Write-Host ($stderrTask.Result) -ForegroundColor Magenta
        }
    }

    $suiteWatch.Stop()
    Write-Host ("`n================ TEST SUITE SUMMARY ({0} files, {1:F1}s) ================" -f $testFiles.Count, $suiteWatch.Elapsed.TotalSeconds) -ForegroundColor Yellow
    $summary | ForEach-Object { Write-Host $_ }
    Write-Host ("`n{0} 用例 / {1} PASS / {2} EXPECTED-FAIL / {3} REGRESSION" -f $testFiles.Count, $passCount, $expectedFailCount, $regressionCount) -ForegroundColor Cyan
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
