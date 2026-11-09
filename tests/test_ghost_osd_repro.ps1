# Regression test for Ghost OSD Popup bug
# Root cause: Missing packet class/id validation and unvalidated stage values in Razer FastQueryDpi & DpiMonitorWorker
# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
$ErrorActionPreference = 'Stop'
$currentDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $currentDir
$exePath = Join-Path $root "FerrisPulse.exe"
$rawBytes = [System.IO.File]::ReadAllBytes($exePath)
$asm = [System.Reflection.Assembly]::Load($rawBytes)
$rdh = $asm.GetType('RazerBatteryTray.RazerDeviceHelper')

$mQuery = $rdh.GetMethod('QueryAllRazerDevices', [System.Reflection.BindingFlags]'Static,Public')
$devs = $mQuery.Invoke($null, @())

$parameterTypes = [Type[]]@([int].MakeByRefType(), [int].MakeByRefType(), [int].MakeByRefType(), [string])
$mFast = $rdh.GetMethod('FastQueryDpi', [System.Reflection.BindingFlags]'Static,Public', $null, $parameterTypes, $null)
if ($null -eq $mFast) { throw 'Missing FastQueryDpi(out dpi, out stage, out count, deviceId) overload' }

$lastMonitoredRazerDpis = @{}
$lastMonitoredRazerStages = @{}
$ghostTriggers = 0

Write-Host "Running 10 fast iterations of FastQueryDpi while idle..."
for ($i = 0; $i -lt 10; $i++) {
    $invokeArgs = [object[]]@([int]0, [int]0, [int]0, "Razer:1532:00BE")
    $ok = $mFast.Invoke($null, $invokeArgs)
    if ($ok) {
        $dpi = $invokeArgs[0]
        $stage = $invokeArgs[1]
        $count = $invokeArgs[2]

        if ($dpi -gt 0) {
            $key = "Razer:1532:00BE"
            $prevDpi = if ($lastMonitoredRazerDpis.ContainsKey($key)) { $lastMonitoredRazerDpis[$key] } else { -1 }
            $prevStage = if ($lastMonitoredRazerStages.ContainsKey($key)) { $lastMonitoredRazerStages[$key] } else { -1 }

            if ($prevDpi -ne -1 -and ($dpi -ne $prevDpi -or $stage -ne $prevStage)) {
                $ghostTriggers++
                Write-Host "[$([DateTime]::Now.ToString('HH:mm:ss.fff'))] Iter $i - GHOST TRIGGER: dpi=$dpi (prev $prevDpi), stage=$stage (prev $prevStage)"
            }

            $lastMonitoredRazerDpis[$key] = $dpi
            $lastMonitoredRazerStages[$key] = $stage
        }
    }
    Start-Sleep -Milliseconds 50
}

Write-Host "Total ghost triggers: $ghostTriggers"
if ($ghostTriggers -gt 0) {
    Write-Host "FAIL: Ghost OSD triggered $ghostTriggers times during idle!" -ForegroundColor Red
    exit 1
} else {
    Write-Host "PASS: Zero ghost OSD triggers detected." -ForegroundColor Green
    exit 0
}
