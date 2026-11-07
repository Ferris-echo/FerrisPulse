# ============================================================================
# Test Case: test_bluetooth_disconnect_and_dedup.ps1
# Verification:
#   1. Bluetooth offline status accuracy: NEVER report offline device as online!
#   2. Classic + BLE dual mode TWS scan dedup: exactly 1 card, zero ghost nodes.
#   3. Offline BLE device filtering: disconnected BLE controllers/buds skipped.
#   4. DeviceManager multi-endpoint aggregation and duplicate cleanup.
# ============================================================================

$repoRoot = Split-Path -Parent $PSScriptRoot
$tempDll = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "BtDedupTest_" + [System.Guid]::NewGuid().ToString("N") + ".dll")
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"

& $csc /target:library /optimize- /codepage:65001 /r:System.Drawing.dll /r:System.Windows.Forms.dll /out:$tempDll /recurse:src\*.cs
if ($LASTEXITCODE -ne 0) {
    Write-Error "FAIL: Compilation failed!"
    exit 1
}

$bytes = [System.IO.File]::ReadAllBytes($tempDll)
$assembly = [System.Reflection.Assembly]::Load($bytes)
Remove-Item $tempDll -Force -ErrorAction SilentlyContinue

$btHelper = $assembly.GetType("RazerBatteryTray.BluetoothDeviceHelper")
$devMgr = $assembly.GetType("RazerBatteryTray.DeviceManager")

$queryByMac = $btHelper.GetMethod("QueryByMac", [System.Reflection.BindingFlags]"Public,Static", $null, @([string], [string], [int].MakeByRefType(), [string].MakeByRefType()), $null)
if ($null -eq $queryByMac) {
    Write-Error "FAIL: BluetoothDeviceHelper.QueryByMac method not found!"
    exit 1
}

# 1. Offline disconnect verification
$p1 = [object[]]@("D056FB203888", "Buds3 Pro", 0, "")
$conn1 = [bool]$queryByMac.Invoke($null, $p1)
if ($conn1 -eq $true) {
    Write-Error "FAIL: Disconnected Buds3 Pro was reported as connected!"
    exit 1
}
Write-Host "PASS: Disconnected Buds3 Pro correctly reported as isConnected=False."

$p2 = [object[]]@("0F9E2D72934F", "Xbox Wireless Controller", 0, "")
$conn2 = [bool]$queryByMac.Invoke($null, $p2)
$batt2 = [int]$p2[2]
if ($conn2 -eq $true) {
    Write-Error "FAIL: Disconnected Xbox Wireless Controller was reported as connected!"
    exit 1
}
Write-Host "PASS: Disconnected Xbox Wireless Controller reported isConnected=False."

# 2. ScanAllBluetoothDevices verification
$scanMethod = $btHelper.GetMethod("ScanAllBluetoothDevices", [System.Reflection.BindingFlags]"Public,Static")
if ($null -eq $scanMethod) {
    Write-Error "FAIL: ScanAllBluetoothDevices method not found!"
    exit 1
}

$scannedList = $scanMethod.Invoke($null, $null)
Write-Host "ScanAllBluetoothDevices returned $($scannedList.Count) devices."

$nameCounts = @{}
foreach ($item in $scannedList) {
    $devId = [string]$item.GetType().GetProperty("DeviceId").GetValue($item, $null)
    $origName = [string]$item.GetType().GetProperty("OriginalName").GetValue($item, $null)
    $cat = [string]$item.GetType().GetProperty("Category").GetValue($item, $null)
    Write-Host "  Scanned Item: $devId | $origName | Category=$cat"

    if ($cat -eq "Earbuds" -or $cat -eq "Headset") {
        if ($nameCounts.ContainsKey($origName)) {
            Write-Error "FAIL: Duplicate TWS earphone card found in scan list: '$origName' (DeviceId: $devId)!"
            exit 1
        }
        $nameCounts[$origName] = 1
    }

    if ($devId -eq "BT_0F9E2D72934F") {
        Write-Error "FAIL: Offline Xbox Wireless Controller was wrongly scanned as connected device!"
        exit 1
    }
}
Write-Host "PASS: ScanAllBluetoothDevices verified with zero duplicate TWS cards and zero offline ghost devices."

# 3. DeviceManager.QueryAllDevices single card verification
$queryAll = $devMgr.GetMethod("QueryAllDevices", [System.Reflection.BindingFlags]"Public,Static")
$allDevices = $queryAll.Invoke($null, $null)

$dmAudioCounts = @{}
foreach ($d in $allDevices) {
    $devId = [string]$d.DeviceId
    $devName = [string]$d.DeviceName
    $cat = [string]$d.Category
    if ($cat -eq "Earbuds" -or $cat -eq "Headset") {
        $key = $devName.Trim()
        if ($dmAudioCounts.ContainsKey($key)) {
            Write-Error "FAIL: DeviceManager.QueryAllDevices returned duplicate cards for audio device '$key'!"
            exit 1
        }
        $dmAudioCounts[$key] = 1
    }
}
Write-Host "PASS: DeviceManager.QueryAllDevices strictly deduplicates TWS / Bluetooth audio cards."

Write-Host "ALL BLUETOOTH DISCONNECT AND DEDUP TESTS PASSED!"
