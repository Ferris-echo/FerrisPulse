# run_all_tests.ps1
$testFiles = Get-ChildItem (Join-Path $PSScriptRoot "..\tests\test_*.ps1")
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

# 1. 生产环境注册表防污染深快照，同时保护 FerrisPulse 与 RazerBatteryTray（含外设优先级与状态记忆）
. (Join-Path $PSScriptRoot "TestRegistryGuard.ps1")
$regSnapshot = Save-TestRegistrySnapshot -Roots @("HKCU:\Software\FerrisPulse", "HKCU:\Software\RazerBatteryTray")

try {
    foreach ($tf in $testFiles) {
        Write-Host ("[TESTING] " + $tf.Name) -ForegroundColor Cyan
        $psi = New-Object System.Diagnostics.ProcessStartInfo
        $psi.FileName = "powershell.exe"
        $psi.Arguments = "-ExecutionPolicy Bypass -NoProfile -File `"$($tf.FullName)`""
        $psi.UseShellExecute = $false
        $psi.RedirectStandardOutput = $true
        $psi.RedirectStandardError = $true
        $psi.CreateNoWindow = $true
        
        $p = [System.Diagnostics.Process]::Start($psi)
        $stdoutTask = $p.StandardOutput.ReadToEndAsync()
        $stderrTask = $p.StandardError.ReadToEndAsync()
        $budgetMs = $defaultTimeoutMs
        if ($testTimeouts.ContainsKey($tf.Name)) { $budgetMs = $testTimeouts[$tf.Name] }
        $finished = $p.WaitForExit($budgetMs) # 看门狗超时，防止任何测试脚本无休止卡死
        if (-not $finished) {
            try { $p.Kill() } catch { }
            $allPassed = $false
            $summary += "[TIMEOUT (>$([int]($budgetMs/1000))s)] " + $tf.Name
            Write-Host " -> TIMEOUT (>$([int]($budgetMs/1000))s): 强制终止防卡死" -ForegroundColor Red
            continue
        }

        if ($p.ExitCode -eq 0) {
            $summary += "[PASS] " + $tf.Name
            Write-Host " -> OK" -ForegroundColor Green
        } else {
            $allPassed = $false
            $summary += "[FAIL (" + $p.ExitCode + ")] " + $tf.Name
            Write-Host " -> FAILED with code " + $p.ExitCode -ForegroundColor Red
            Write-Host ($stdoutTask.Result) -ForegroundColor Gray
            Write-Host ($stderrTask.Result) -ForegroundColor Magenta
        }
    }

    Write-Host "`n================ TEST SUITE SUMMARY ================" -ForegroundColor Yellow
    $summary | ForEach-Object { Write-Host $_ }
}
finally {
    # 彻底恢复用户生产环境注册表，杜绝自动化测试篡改用户的电竞优化与外设记忆
    Restore-TestRegistrySnapshot -Snapshot $regSnapshot
    Write-Host "`n[SAFEGUARD] 已回滚用户生产注册表快照（应用设置 + 外设优先级与状态记忆）。" -ForegroundColor DarkGray
}

if (-not $allPassed) {
    exit 1
} else {
    exit 0
}
