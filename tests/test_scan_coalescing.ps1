# tests/test_scan_coalescing.ps1
# M1 验证：全量扫描在途合流器（DeviceManager.QueryAllDevices 的 scanInFlight / pendingRescan 闸门）
# 断言口径（docs/OPTIMIZATION_BACKLOG.md M1）：
#   1) 并发/在途期间到达的刷新只登记补跑，绝不再进扫描核心；
#   2) 一次在途扫描 + 至多一次补跑，核心调用次数 <= 2；
#   3) 每个调用都能拿到非空 List（在途期间返回最近一次完成扫描的快照，不返回 null）。
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "..\tools\TestHarness.ps1")

$asm = Load-FerrisPulseAssembly
$BF = [System.Reflection.BindingFlags]"Public,NonPublic,Instance,Static"
$tDm = $asm.GetType("RazerBatteryTray.DeviceManager", $true)
$mQuery = $tDm.GetMethod("QueryAllDevices", [System.Reflection.BindingFlags]"Static,Public")
$mReset = $tDm.GetMethod("ResetScanCoalescingProbe", $BF)
$pCount = $tDm.GetProperty("CoreScanInvocationCount", $BF)

$probeSrc = @"
using System;
using System.Reflection;
using System.Threading;
public static class ScanCoalesceProbe {
    public static volatile bool Running;
    public static void SetStaticInt(Type t, string name, int value) {
        FieldInfo f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
        if (f == null) throw new InvalidOperationException("field not found: " + name);
        f.SetValue(null, value);
    }
    public static int GetStaticInt(Type t, string name) {
        FieldInfo f = t.GetField(name, BindingFlags.NonPublic | BindingFlags.Static);
        if (f == null) throw new InvalidOperationException("field not found: " + name);
        return (int)f.GetValue(null);
    }
    public static void RunScanInBackground(Type t) {
        MethodInfo m = t.GetMethod("QueryAllDevices", BindingFlags.Public | BindingFlags.Static);
        Running = true;
        Thread th = new Thread(delegate() {
            try { m.Invoke(null, null); }
            catch { }
            finally { Running = false; }
        });
        th.IsBackground = true;
        th.Start();
    }
}
"@
if (-not ([System.Management.Automation.PSTypeName]"ScanCoalesceProbe").Type) {
    Add-Type -TypeDefinition $probeSrc
}

Invoke-TestCase "1. 在途扫描期间到达的 5 次刷新不堆新扫描，且一次补跑收口" {
    $mReset.Invoke($null, @()) | Out-Null
    [ScanCoalesceProbe]::Running = $false
    [ScanCoalesceProbe]::RunScanInBackground($tDm)
    Assert-True ([ScanCoalesceProbe]::Running) "background scan started"

    # 等 1.2s 确认已深入扫描核心内部（单次扫描实测约 5s）
    [Threading.Thread]::Sleep(1200)
    $results = New-Object "System.Collections.Generic.List[object]"
    $watch = [System.Diagnostics.Stopwatch]::StartNew()
    for ($i = 0; $i -lt 5; $i++) {
        $results.Add($mQuery.Invoke($null, @()))
    }
    $watch.Stop()

    Assert-Equal 5 $results.Count "all 5 coalesced callers returned"
    foreach ($r in $results) { Assert-True ($null -ne $r) "coalesced caller received a list, not null" }

    # 等在途扫描 + 补跑全部结束
    $deadline = [DateTime]::UtcNow.AddSeconds(40)
    while ([ScanCoalesceProbe]::Running -and [DateTime]::UtcNow -lt $deadline) {
        [Threading.Thread]::Sleep(100)
    }
    Assert-True (-not [ScanCoalesceProbe]::Running) "background scan finished"

    $coreRuns = $pCount.GetValue($null, $null)
    Assert-True ($coreRuns -ge 1) ("at least one real scan happened, got " + $coreRuns)
    Assert-True ($coreRuns -le 2) ("coalesced core scans <= 2, got " + $coreRuns)
    Write-Host ("    [INFO] core scans=" + $coreRuns + ", coalesced-call elapsed=" + $watch.ElapsedMilliseconds + "ms") -ForegroundColor DarkGray
}

Invoke-TestCase "2. 在途令牌被占用时刷新入口零核心调用，仅置补跑位" {
    $mReset.Invoke($null, @()) | Out-Null
    [ScanCoalesceProbe]::SetStaticInt($tDm, "scanInFlight", 1)
    [ScanCoalesceProbe]::SetStaticInt($tDm, "pendingRescan", 0)
    try {
        $watch = [System.Diagnostics.Stopwatch]::StartNew()
        for ($i = 0; $i -lt 10; $i++) {
            $list = $mQuery.Invoke($null, @())
            Assert-True ($null -ne $list) "caller received a list while scan in flight"
        }
        $watch.Stop()
        Assert-Equal 0 ($pCount.GetValue($null, $null)) "no core scan entered while token is held"
        Assert-Equal 1 ([ScanCoalesceProbe]::GetStaticInt($tDm, "pendingRescan")) "pendingRescan registered"
        Assert-True ($watch.ElapsedMilliseconds -lt 3000) ("token-held calls returned immediately, elapsed=" + $watch.ElapsedMilliseconds + "ms")
    } finally {
        [ScanCoalesceProbe]::SetStaticInt($tDm, "scanInFlight", 0)
        [ScanCoalesceProbe]::SetStaticInt($tDm, "pendingRescan", 0)
    }
}

Write-Host ""
Write-Host "ALL SCAN COALESCING TESTS PASSED." -ForegroundColor Green
exit 0
