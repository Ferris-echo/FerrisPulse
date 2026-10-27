# run_all_tests.ps1
$testFiles = Get-ChildItem (Join-Path $PSScriptRoot "..\tests\test_*.ps1")
$allPassed = $true
$summary = @()

# 1. 生产环境注册表防污染快照，严格保护用户真实外设与电竞配置
$regPath = "HKCU:\Software\FerrisPulse"
$regBackup = @{}
if (Test-Path $regPath) {
    $props = Get-ItemProperty -Path $regPath
    foreach ($prop in $props.PSObject.Properties) {
        if ($prop.Name -notmatch "^__" -and $prop.Name -notmatch "^PS") {
            $regBackup[$prop.Name] = $prop.Value
        }
    }
}

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
        $finished = $p.WaitForExit(8000) # 8秒看门狗超时，防止任何测试脚本无休止卡死
        if (-not $finished) {
            try { $p.Kill() } catch { }
            $allPassed = $false
            $summary += "[TIMEOUT (>8s)] " + $tf.Name
            Write-Host " -> TIMEOUT (>8s): 强制终止防卡死" -ForegroundColor Red
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
    if (Test-Path $regPath) {
        foreach ($k in $regBackup.Keys) {
            Set-ItemProperty -Path $regPath -Name $k -Value $regBackup[$k] -ErrorAction SilentlyContinue
        }
        Write-Host "`n[SAFEGUARD] 已恢复用户生产注册表快照，保护电竞 DPI / 回报率开关记忆。" -ForegroundColor DarkGray
    }
}

if (-not $allPassed) {
    exit 1
} else {
    exit 0
}
