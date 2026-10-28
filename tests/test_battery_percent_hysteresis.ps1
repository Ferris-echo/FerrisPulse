# tests\test_battery_percent_hysteresis.ps1
# 验证电量换算的滞回防抖不会把错误值永久锁存
#
# 历史 bug：一次异常电压读数算出 pct=100 后，lastAcceptedPct 被置为 100；
# 之后电压回到 3633mV，pct=23 与 100 不等，但 |3633-3633| < 6 命中
# 「噪声维持原值」分支 → 永远返回 100，界面在 3.645V 时显示 100% 且无法自愈。
#
# 本测试用反射直接驱动 VoltageToPercent / PercentWithHysteresis，
# 覆盖：查表正确性、越界读数丢弃、正常序列、越界后不锁死。

$ErrorActionPreference = "Stop"

$fullPath = (Resolve-Path "FerrisPulse.exe").Path
$bytes = [System.IO.File]::ReadAllBytes($fullPath)
$asm = [System.Reflection.Assembly]::Load($bytes)

$tVgn = $asm.GetType("RazerBatteryTray.VgnDeviceHelper")
if ($tVgn -eq $null) {
    Write-Host "FAIL: VgnDeviceHelper type not found" -ForegroundColor Red
    exit 1
}

$flagsStatic = [System.Reflection.BindingFlags]"Static, Public, NonPublic"
$mTable = $tVgn.GetMethod("VoltageToPercent", $flagsStatic)
$mHyst = $tVgn.GetMethod("PercentWithHysteresis", $flagsStatic)
$fPct = $tVgn.GetField("lastAcceptedPct", $flagsStatic)
$fMv = $tVgn.GetField("lastAcceptedMv", $flagsStatic)

if ($mTable -eq $null -or $mHyst -eq $null -or $fPct -eq $null -or $fMv -eq $null) {
    Write-Host "FAIL: 缺少 VoltageToPercent / PercentWithHysteresis / 滞回状态字段" -ForegroundColor Red
    exit 1
}

$failures = 0

function Reset-Hysteresis {
    $fPct.SetValue($null, -1)
    $fMv.SetValue($null, -1)
}

function Assert-Eq($actual, $expected, $label) {
    if ($actual -eq $expected) {
        Write-Host ("PASS: {0} (={1})" -f $label, $actual) -ForegroundColor Green
    } else {
        Write-Host ("FAIL: {0} 期望 {1} 实际 {2}" -f $label, $expected, $actual) -ForegroundColor Red
        $script:failures++
    }
}

Write-Host ""
Write-Host "===== 1. 查表正确性 =====" -ForegroundColor Cyan
Assert-Eq ($mTable.Invoke($null, @(3633))) 23 "3633mV 应查表得 23%"
Assert-Eq ($mTable.Invoke($null, @(4110))) 100 "4110mV 应查表得 100%"
Assert-Eq ($mTable.Invoke($null, @(3050))) 0 "3050mV 应查表得 0%"

Write-Host ""
Write-Host "===== 2. 越界读数必须丢弃 =====" -ForegroundColor Cyan
Reset-Hysteresis
$r = $mHyst.Invoke($null, @(65523))
Assert-Eq ($r -eq 100) $false "首次遇到 65523mV 不得返回 100"
Assert-Eq ($r -eq -1) $true "首次遇到 65523mV 应返回 -1（无可用基准）"

Write-Host ""
Write-Host "===== 3. 正常序列 =====" -ForegroundColor Cyan
Reset-Hysteresis
Assert-Eq ($mHyst.Invoke($null, @(3633))) 23 "首次 3633mV -> 23%"
Assert-Eq ($mHyst.Invoke($null, @(3635))) 23 "3635mV（<6mV 抖动）-> 保持 23%"
Assert-Eq ($mHyst.Invoke($null, @(3663))) 25 "3663mV（+30mV）-> 25%（3660 档起）"

Write-Host ""
Write-Host "===== 4. 关键回归：越界读数不得把滞回锁死在 100% =====" -ForegroundColor Cyan
Reset-Hysteresis
$null = $mHyst.Invoke($null, @(3633))
Assert-Eq ($mHyst.Invoke($null, @(3633))) 23 "基准建立 23%"
$afterGarbage = $mHyst.Invoke($null, @(65523))
Assert-Eq ($afterGarbage -eq 100) $false "注入 65523mV 后不得变成 100%"
Assert-Eq ($mHyst.Invoke($null, @(3633))) 23 "电压回到 3633mV 仍为 23%（未被锁死）"
Assert-Eq ($mHyst.Invoke($null, @(3637))) 23 "再次小幅抖动仍为 23%"

Write-Host ""
if ($failures -eq 0) {
    Write-Host "ALL PASS" -ForegroundColor Green
    exit 0
} else {
    Write-Host ("FAILED: {0} 项" -f $failures) -ForegroundColor Red
    exit 1
}
