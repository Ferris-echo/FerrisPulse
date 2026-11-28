# tests/test_update_tls_and_rollback.ps1
# U1/U2 验证：自更新通道 TLS 校验收紧 + 原位替换的 pending 标记与自动回滚
# 断言口径（docs/OPTIMIZATION_BACKLOG.md U1/U2）：
#   1) 进程级证书校验不再被整体放行，且只显式启用 TLS 1.2；
#   2) 替换前后用 UpdatePending 标记支撑回滚；标记存在时不得删除 .old；
#   3) 无 .old 退路时回滚不误动作，仅清标记；新版本确认健康后清标记。
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "..\tools\TestHarness.ps1")

$asm = Load-FerrisPulseAssembly
$root = Get-FerrisPulseRoot
$BF = [System.Reflection.BindingFlags]"Public,NonPublic,Instance,Static"
$tUpd = $asm.GetType("RazerBatteryTray.UpdateManager", $true)
$mHas = $tUpd.GetMethod("HasPendingUpdate", [System.Reflection.BindingFlags]"Static,Public")
$mMark = $tUpd.GetMethod("MarkUpdatePending", [System.Reflection.BindingFlags]"Static,Public")
$mConfirm = $tUpd.GetMethod("ConfirmUpdateHealthy", [System.Reflection.BindingFlags]"Static,Public")
$mRollback = $tUpd.GetMethod("TryRollbackPendingUpdate", [System.Reflection.BindingFlags]"Static,Public")
$regPath = "HKCU:\Software\FerrisPulse"

Invoke-TestCase "1. TLS：不再全局放行证书，只显式启用 TLS 1.2" {
    $src = [IO.File]::ReadAllText((Join-Path $root "src\Services\UpdateManager.cs"), [Text.Encoding]::UTF8)
    Assert-True (-not ($src -match "ServerCertificateValidationCallback\s*=")) "process-wide certificate bypass assignment removed"
    Assert-True (-not $src.Contains("delegate { return true; }")) "no blanket certificate acceptance delegate"
    Assert-True (-not $src.Contains("SecurityProtocolType.Tls")) "legacy TLS enum no longer referenced"
    Assert-True ($src.Contains("(SecurityProtocolType)3072")) "TLS 1.2 enforced explicitly"
}

Invoke-TestCase "2. pending 标记生命周期" {
    try {
        Remove-ItemProperty -Path $regPath -Name "UpdatePending" -ErrorAction SilentlyContinue
        Assert-True (-not [bool]$mHas.Invoke($null, @())) "no pending update by default"
        $mMark.Invoke($null, @("v9.9.9")) | Out-Null
        Assert-True ([bool]$mHas.Invoke($null, @())) "pending marker recorded"
        $val = (Get-ItemProperty -Path $regPath -Name "UpdatePending").UpdatePending
        Assert-True ($val -like "v9.9.9|*") "marker records target version and time"
        $mConfirm.Invoke($null, @()) | Out-Null
        Assert-True (-not [bool]$mHas.Invoke($null, @())) "ConfirmUpdateHealthy clears the marker"
        $mMark.Invoke($null, @($null)) | Out-Null
        Assert-True ([bool]$mHas.Invoke($null, @())) "marker can be written without a version"
    } finally {
        Remove-ItemProperty -Path $regPath -Name "UpdatePending" -ErrorAction SilentlyContinue
    }
}

Invoke-TestCase "3. 无 .old 退路时回滚不误动作，只清标记" {
    $hostExe = [System.Diagnostics.Process]::GetCurrentProcess().MainModule.FileName
    $hostOld = $hostExe + ".old"
    Assert-True (-not (Test-Path $hostOld)) "test host has no .old backup (precondition)"
    try {
        $mMark.Invoke($null, @("v9.9.9")) | Out-Null
        $rolled = [bool]$mRollback.Invoke($null, @())
        Assert-True (-not $rolled) "rollback reports false when there is no .old"
        Assert-True (-not [bool]$mHas.Invoke($null, @())) "marker cleared after a no-op rollback"
        Assert-True (Test-Path $hostExe) "test host executable untouched"
    } finally {
        Remove-ItemProperty -Path $regPath -Name "UpdatePending" -ErrorAction SilentlyContinue
    }
}

Invoke-TestCase "4. 源码契约：替换前落标记、启动早期回滚、健康后才删 .old" {
    $updSrc = [IO.File]::ReadAllText((Join-Path $root "src\Services\UpdateManager.cs"), [Text.Encoding]::UTF8)
    Assert-True ($updSrc.Contains("MarkUpdatePending(null);")) "self-replace marks pending before renaming"
    $progSrc = [IO.File]::ReadAllText((Join-Path $root "src\Core\Program.cs"), [Text.Encoding]::UTF8)
    Assert-True ($progSrc.Contains("TryRollbackPendingUpdate()")) "startup performs rollback check"
    Assert-True ($progSrc.Contains("!UpdateManager.HasPendingUpdate()")) ".old cleanup is gated on the pending marker"
}

Write-Host ""
Write-Host "ALL UPDATE TLS & ROLLBACK TESTS PASSED." -ForegroundColor Green
exit 0
