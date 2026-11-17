# tests/test_registry_guard_no_production_pollution.ps1
# 验证 TestRegistryGuard 深快照与回滚对 HKCU:\Software\RazerBatteryTray 无损还原

$ErrorActionPreference = "Stop"
$root = (Resolve-Path "$PSScriptRoot\..").Path
. (Join-Path $root "tools\TestRegistryGuard.ps1")

$rbt = "HKCU:\Software\RazerBatteryTray"
$snap = Save-TestRegistrySnapshot -Roots @($rbt)
$beforePrio = if (Test-Path $rbt) { (Get-ItemProperty -Path $rbt -Name "PriorityOrder" -ErrorAction SilentlyContinue).PriorityOrder } else { $null }

try {
    if (-not (Test-Path $rbt)) { New-Item -Path $rbt -Force | Out-Null }
    Set-ItemProperty -Path $rbt -Name "PriorityOrder" -Value "FAKE:0000:0001,FAKE:0000:0002"
    Set-ItemProperty -Path $rbt -Name "GuardProbeJunk" -Value "junk"
    New-Item -Path "$rbt\DeviceState\GuardProbe_0000_0001" -Force | Out-Null
    Set-ItemProperty -Path "$rbt\DeviceState\GuardProbe_0000_0001" -Name "BatteryPercent" -Value 99 -Type DWord
}
finally {
    Restore-TestRegistrySnapshot -Snapshot $snap
}

$afterPrio = if (Test-Path $rbt) { (Get-ItemProperty -Path $rbt -Name "PriorityOrder" -ErrorAction SilentlyContinue).PriorityOrder } else { $null }
if ($afterPrio -ne $beforePrio) {
    throw "FAIL: PriorityOrder not restored! before='$beforePrio', after='$afterPrio'"
}
if (Test-Path "$rbt\DeviceState\GuardProbe_0000_0001") {
    throw "FAIL: GuardProbe_0000_0001 subkey was not removed!"
}
if (Test-Path $rbt) {
    $junk = Get-ItemProperty -Path $rbt -Name "GuardProbeJunk" -ErrorAction SilentlyContinue
    if ($null -ne $junk) {
        throw "FAIL: GuardProbeJunk value was not removed!"
    }
}

Write-Host "PASS: TestRegistryGuard restored RazerBatteryTray PriorityOrder and cleaned subkeys" -ForegroundColor Green
exit 0
