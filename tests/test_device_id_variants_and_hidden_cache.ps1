# tests/test_device_id_variants_and_hidden_cache.ps1
# P3 验证：设备 ID 变体生成（静态前缀表 + HashSet 去重）与隐藏集合进程内快照
# 断言口径（docs/OPTIMIZATION_BACKLOG.md P3）：
#   1) 变体集合无重复（忽略大小写）、覆盖自身/归一化/跨前缀候选；
#   2) 隐藏后 IsDeviceHidden 立刻为真，取消隐藏后立刻为假（写入口必须失效缓存）；
#   3) 绕过写入口直接改注册表时，显式 InvalidateHiddenDevicesCache 也能看到新状态。
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "..\tools\TestHarness.ps1")

$asm = Load-FerrisPulseAssembly
$BF = [System.Reflection.BindingFlags]"Public,NonPublic,Instance,Static"
$tDm = $asm.GetType("RazerBatteryTray.DeviceManager", $true)
$mVars = $tDm.GetMethod("GetDeviceIdVariants", [System.Reflection.BindingFlags]"Static,Public")
$mHide = $tDm.GetMethod("HideOrDeleteDevice", [System.Reflection.BindingFlags]"Static,Public")
$mUnhide = $tDm.GetMethod("UnhideDevice", [System.Reflection.BindingFlags]"Static,Public")
$mIsHidden = $tDm.GetMethod("IsDeviceHidden", [System.Reflection.BindingFlags]"Static,Public")
$mInvalidateHidden = $tDm.GetMethod("InvalidateHiddenDevicesCache", $BF)

Invoke-TestCase "1. 变体生成覆盖完整且无重复" {
    $v = $mVars.Invoke($null, @("Razer:1532:00C0"))
    Assert-True ($v.Contains("Razer:1532:00C0")) "contains self"
    Assert-True ($v.Contains("HID:1532:00C0")) "cross-prefix candidate present"
    Assert-True ($v.Contains("Logitech:1532:00C0")) "all common prefixes covered"
    $seen = New-Object System.Collections.Generic.HashSet[string] ([StringComparer]::OrdinalIgnoreCase)
    foreach ($x in $v) { Assert-True ($seen.Add($x)) ("duplicate variant: " + $x) }
    $single = $mVars.Invoke($null, @("NoColonId"))
    Assert-Equal 1 $single.Count "id without colon yields a single variant"
    Assert-Equal 0 ($mVars.Invoke($null, @(""))).Count "empty id yields no variants"
}

Invoke-TestCase "2. 隐藏/取消隐藏即时生效（缓存必须失效）" {
    $testId = "Test:P3HIDDEN:0001"
    try {
        $mUnhide.Invoke($null, @($testId)) | Out-Null
        Assert-True (-not [bool]$mIsHidden.Invoke($null, @($testId))) "not hidden initially"
        $mHide.Invoke($null, @($testId)) | Out-Null
        Assert-True ([bool]$mIsHidden.Invoke($null, @($testId))) "hidden takes effect immediately"
        $mUnhide.Invoke($null, @($testId)) | Out-Null
        Assert-True (-not [bool]$mIsHidden.Invoke($null, @($testId))) "unhide takes effect immediately"
    } finally {
        $mUnhide.Invoke($null, @($testId)) | Out-Null
    }
}

Invoke-TestCase "3. 外部直接改注册表后可通过失效函数看到新状态" {
    $wildId = "Test:P3EXTERNAL:0001"
    $key = "HKCU:\Software\RazerBatteryTray\HiddenDevices"
    try {
        if (-not (Test-Path $key)) { New-Item -Path $key -Force | Out-Null }
        Assert-True (-not [bool]$mIsHidden.Invoke($null, @($wildId))) "warm the cache with a negative lookup"
        Set-ItemProperty -Path $key -Name $wildId -Value 1 -Type DWord
        $mInvalidateHidden.Invoke($null, @()) | Out-Null
        Assert-True ([bool]$mIsHidden.Invoke($null, @($wildId))) "explicit invalidation sees the external write"
    } finally {
        Remove-ItemProperty -Path $key -Name $wildId -ErrorAction SilentlyContinue
        $mInvalidateHidden.Invoke($null, @()) | Out-Null
    }
}

Write-Host ""
Write-Host "ALL DEVICE-ID VARIANT & HIDDEN-CACHE TESTS PASSED." -ForegroundColor Green
exit 0
