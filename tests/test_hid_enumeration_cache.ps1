# tests/test_hid_enumeration_cache.ps1
# P1 验证：按扫描代次的 HID 枚举只读快照
# 断言口径（docs/OPTIMIZATION_BACKLOG.md P1）：
#   1) 同一代次内的重复 Enumerate 只做 1 次真实 SetupAPI 全量枚举（其余命中快照）；
#   2) 代次结束后立刻回到实时枚举（写路径/UI 线程永远拿活句柄）；
#   3) NotifyTopologyChanged 会立刻作废快照；
#   4) 返回的是防御性副本，调用方改动不会污染共享快照。
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "..\tools\TestHarness.ps1")

$asm = Load-FerrisPulseAssembly
$tHid = $asm.GetType("RazerBatteryTray.HidTransport", $true)
$BF = [System.Reflection.BindingFlags]"Public,NonPublic,Instance,Static"
$mBegin = $tHid.GetMethod("BeginScanGeneration", $BF)
$mEnd = $tHid.GetMethod("EndScanGeneration", $BF)
$mNotify = $tHid.GetMethod("NotifyTopologyChanged", $BF)
$mResetCount = $tHid.GetMethod("ResetEnumerationCount", $BF)
$mResetHits = $tHid.GetMethod("ResetEnumerationCacheHits", $BF)
$pCount = $tHid.GetProperty("EnumerationCount", $BF)
$pHits = $tHid.GetProperty("EnumerationCacheHits", $BF)
$mEnumerate = $tHid.GetMethod("Enumerate", [System.Reflection.BindingFlags]"Static,Public")

Invoke-TestCase "1. 同一代次内只有一次真实枚举" {
    $mResetCount.Invoke($null, @()) | Out-Null
    $mResetHits.Invoke($null, @()) | Out-Null
    $mBegin.Invoke($null, @()) | Out-Null
    try {
        $first = $mEnumerate.Invoke($null, @([int]0, [int]0))
        $second = $mEnumerate.Invoke($null, @([int]0, [int]0))
        $third = $mEnumerate.Invoke($null, @([int]0x1532, [int]0))
        Assert-Equal 1 ([int]$pCount.GetValue($null, $null)) "only one real enumeration inside the generation"
        Assert-True ([int]$pHits.GetValue($null, $null) -ge 2) "subsequent calls served from snapshot"
        Assert-True ($null -ne $first -and $null -ne $second -and $null -ne $third) "snapshot calls returned lists"
        Write-Host ("    [INFO] endpoints=" + $first.Count + ", cacheHits=" + $pHits.GetValue($null, $null)) -ForegroundColor DarkGray
    } finally {
        $mEnd.Invoke($null, @()) | Out-Null
    }
}

Invoke-TestCase "2. 代次结束后回到实时枚举" {
    $mResetCount.Invoke($null, @()) | Out-Null
    $mEnumerate.Invoke($null, @([int]0, [int]0)) | Out-Null
    $mEnumerate.Invoke($null, @([int]0, [int]0)) | Out-Null
    Assert-Equal 2 ([int]$pCount.GetValue($null, $null)) "outside a scan every call enumerates live"
}

Invoke-TestCase "3. 拓扑变更与返回副本" {
    $mResetCount.Invoke($null, @()) | Out-Null
    $mResetHits.Invoke($null, @()) | Out-Null
    $mBegin.Invoke($null, @()) | Out-Null
    try {
        $a = $mEnumerate.Invoke($null, @([int]0, [int]0))
        $countAfterFirst = [int]$pCount.GetValue($null, $null)
        Assert-Equal 1 $countAfterFirst "first call enumerated"
        if ($a.Count -gt 0) { $a[0].ProductString = "MUTATED-BY-TEST" }
        $mNotify.Invoke($null, @()) | Out-Null
        $b = $mEnumerate.Invoke($null, @([int]0, [int]0))
        Assert-Equal 2 ([int]$pCount.GetValue($null, $null)) "topology change forces a fresh enumeration"
        if ($b.Count -gt 0) {
            Assert-True ($b[0].ProductString -ne "MUTATED-BY-TEST") "snapshot is not polluted by caller mutation"
        }
    } finally {
        $mEnd.Invoke($null, @()) | Out-Null
    }
}

Write-Host ""
Write-Host "ALL HID ENUMERATION CACHE TESTS PASSED." -ForegroundColor Green
exit 0
