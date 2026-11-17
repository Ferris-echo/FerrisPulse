# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# Offline Keychron M6 built-in driver, protocol replay & UI continuity test. Zero hardware I/O.
$ErrorActionPreference = 'Stop'
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Resolve-Path "$PSScriptRoot/../FerrisPulse.exe")))
[Environment]::CurrentDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

[RazerBatteryTray.BuiltInDrivers]::EnsureRegistered()
$drivers = @([RazerBatteryTray.HardwareDriverRegistry]::GetAllDriversOrdered())
$driver = @($drivers | Where-Object DriverId -eq 'BuiltIn:KeychronM6')[0]
if ($null -eq $driver) { throw 'Missing BuiltIn:KeychronM6 driver' }
if ($driver.IsExternalPackage -or $driver.Priority -ne 100) { throw 'Incorrect Keychron M6 built-in metadata' }

$flags = [Reflection.BindingFlags]'NonPublic,Public,Static'
$protocol = $asm.GetType('RazerBatteryTray.KeychronM6Helper')
if ($null -eq $protocol) { throw 'Missing KeychronM6Helper' }

function Call-Protocol([string]$name, [object[]]$values) {
    $method = $protocol.GetMethod($name, $flags)
    if ($null -eq $method) { throw "Missing protocol function: $name" }
    for ($i = 0; $i -lt $values.Length; $i++) {
        if ($null -ne $values[$i]) { $values[$i] = $values[$i].PSObject.BaseObject }
    }
    return $method.Invoke($null, $values)
}
function Require([bool]$condition, [string]$message) { if (!$condition) { throw $message } }
function New-Endpoint([int]$vendorId, [int]$productId, [int]$page, [int]$usage, [int]$length, [string]$path) {
    $d = New-Object RazerBatteryTray.HidDeviceDescriptor
    $d.VendorId = $vendorId; $d.ProductId = $productId; $d.UsagePage = $page; $d.Usage = $usage
    $d.InputReportLength = $length; $d.OutputReportLength = $length; $d.FeatureReportLength = 0
    $d.DevicePath = $path
    return $d
}

# 1. UniversalGamingDeviceHelper exclusion & endpoint validation
$univ = $asm.GetType('RazerBatteryTray.UniversalGamingDeviceHelper')
Require (!($univ.GetMethod('IsSupportedGamingDevice').Invoke($null, @([int]0x3434, '\\?\hid#vid_3434&pid_d026&mi_01&col02', 'Keychron Link-KM')))) 'UniversalGaming must exclude 3434:D026'
Require (!($univ.GetMethod('IsSupportedGamingDevice').Invoke($null, @([int]0x3434, '\\?\hid#vid_3434&pid_d060&mi_04', 'Keychron M6')))) 'UniversalGaming must exclude 3434:D060'

$kd = New-Endpoint 0x3434 0xD026 0xFFC1 1 64 'offline-test-endpoint'
Require (Call-Protocol 'IsControlEndpoint' @($kd)) 'Verified RF M6 endpoint rejected'
foreach ($change in @(@('VendorId', 0x3554), @('ProductId', 0x1234), @('UsagePage', 1), @('Usage', 6), @('InputReportLength', 65), @('OutputReportLength', 7), @('FeatureReportLength', 64))) {
    $field = $kd.GetType().GetField($change[0]); $old = $field.GetValue($kd)
    $field.SetValue($kd, [int]$change[1])
    Require (!(Call-Protocol 'IsControlEndpoint' @($kd))) ('Unsafe endpoint accepted: ' + $change[0])
    $field.SetValue($kd, $old)
}
Require ($driver.MatchesVidPid(0x3434, 0xD026) -and $driver.MatchesVidPid(0x3434, 0xD060) -and !$driver.MatchesVidPid(0x3434, 0x1234)) 'VID/PID coverage too broad'

# 2. Identity & battery frame replay (including charging bit 0x80)
$identity = New-Object byte[] 64
$identity[0] = 0xB6; $identity[1] = 2; $identity[4] = 0x34; $identity[5] = 0x34; $identity[6] = 0x60; $identity[7] = 0xD0
Require (Call-Protocol 'IsM6Identity' @(, $identity)) 'M6 identity rejected'
$m6Identity = $identity.Clone()
$identity[6] = 0x26
Require (!(Call-Protocol 'IsM6Identity' @(, $identity))) 'Receiver accepted as M6'

foreach ($sample in @(
    @([byte]0x56, 86, $false),
    @([byte]0xD0, 80, $true),
    @([byte]0xD6, 86, $true),
    @([byte]0x5C, 92, $false),
    @([byte]0xE5, -1, $false)
)) {
    $rx = New-Object byte[] 64; $rx[0] = 0xB4; $rx[1] = 6; $rx[20] = $sample[0]
    $argsList = [object[]]@($rx, -1, $false)
    $valid = Call-Protocol 'TryParseBattery' $argsList
    Require ($valid -eq ($sample[1] -ge 0) -and $argsList[1] -eq $sample[1] -and $argsList[2] -eq $sample[2]) 'M6 status parse failed'
}
foreach ($bad in @([byte[]](0xB4, 6), $null)) {
    $argsList = [object[]]@($bad, 86, $true)
    Require (!(Call-Protocol 'TryParseBattery' $argsList) -and $argsList[1] -eq -1 -and !$argsList[2]) 'Malformed M6 frame retained stale data'
}

# 3. Link resolution, multi-receiver separation, cache freshness
$stamp = [DateTime]'2026-11-08T10:00:00'
$state = Call-Protocol 'CreateState' @($kd, 86, $true, $true, $stamp)
Require ($state.IsSleeping -and $state.IsChargingUnknown -and !$state.IsCharging -and $state.LastUpdated -eq $stamp -and $state.HardwareId -eq $kd.DevicePath) 'M6 cached freshness or endpoint identity changed'
Require (!$driver.CanHandle($state)) 'Logical M6 ID claimed without verified path'
Require (Call-Protocol 'AcceptIdentity' @($kd, $m6Identity)) 'Verified M6 identity not accepted'
Require ($driver.CanHandle($state)) 'Verified M6 endpoint not routed'

$rf2 = New-Endpoint 0x3434 0xD026 0xFFC1 1 64 'second-verified-m6-receiver'
$state2 = Call-Protocol 'CreateState' @($rf2, 50, $false, $false, $stamp)
Require ($null -eq (Call-Protocol 'ResolveReceiverLink' @('Keychron:ReceiverPath:second-verified-m6-receiver'))) 'Unverified receiver path linked'
Require ((Call-Protocol 'ResolveReceiverLink' @('Keychron:3434:D026:MS')) -eq (Call-Protocol 'PhysicalUnit' @($state))) 'Unambiguous legacy receiver link lost'
Require (Call-Protocol 'AcceptIdentity' @($rf2, $m6Identity)) 'Second M6 identity not accepted'
$twoReceivers = New-Object 'System.Collections.Generic.List[RazerBatteryTray.MouseBatteryInfo]'
Call-Protocol 'MergeTransportState' @($twoReceivers, $state)
Call-Protocol 'MergeTransportState' @($twoReceivers, $state2)
Require ($twoReceivers.Count -eq 2) 'Distinct verified M6 receivers collapsed'
Require ($null -eq (Call-Protocol 'ResolveReceiverLink' @('Keychron:3434:D026:MS'))) 'Ambiguous legacy link silently selected one mouse'
Require ((Call-Protocol 'ResolveReceiverLink' @('Keychron:ReceiverPath:second-verified-m6-receiver')) -eq (Call-Protocol 'PhysicalUnit' @($state2))) 'Explicit verified receiver path did not resolve'

$memory = $protocol.GetField('lastReadings', $flags).GetValue($null)
$memory[$kd.DevicePath] = $state
$cached = Call-Protocol 'CachedState' @($kd)
Require ($cached.BatteryPercent -eq 86 -and $cached.LastUpdated -eq $stamp -and $cached.IsSleeping -and $cached.IsChargingUnknown -and !$cached.IsCharging) 'Same-path M6 cache failed'
$kd.DevicePath = 'unrelated-offline-test-endpoint'
Require ($null -eq (Call-Protocol 'CachedState' @($kd))) 'M6 cache leaked to different HID path'
$kd.DevicePath = 'offline-test-endpoint'
$memory.Clear()

# 4. Wired M6 verification, routing, capability limits & UI PhysicalUnitId continuity
$state = New-Object RazerBatteryTray.MouseBatteryInfo
$state.DeviceId = 'Keychron:3434:D026:MS'; $state.PhysicalUnitId = 'Keychron:3434:D026:MS:Path:OFFLINE-TEST-ENDPOINT'
$state.DeviceName = 'Keychron M6'; $state.Category = 'Mouse'; $state.IsConnected = $true

$wiredEndpoint = New-Endpoint 0x3434 0xD060 0xFFC1 1 64 'offline-wired-control'
$wired = Call-Protocol 'CreateState' @($wiredEndpoint, 86, $true, $false, $stamp)
$wired.PhysicalUnitId = $state.PhysicalUnitId
Require (Call-Protocol 'AcceptIdentity' @($wiredEndpoint, $m6Identity)) 'USB M6 identity not accepted'
$driver.PopulateCapabilities($wired)
Require ($wired.Capabilities.SupportsBattery -and !$wired.Capabilities.SupportsDpi -and !$wired.Capabilities.SupportsPollingRate -and $wired.SupportsDpiWrite -eq $false -and $wired.SupportsPollingRateWrite -eq $false) 'M6 capability limits mismatch'
Require ([object]::ReferenceEquals([RazerBatteryTray.HardwareDriverRegistry]::ResolveDriver($wired), $driver)) 'M6 built-in routing failed'

$mt = $asm.GetType('RazerBatteryTray.MainForm')
$nf = [Reflection.BindingFlags]'NonPublic,Instance'
$mt.GetField('DisableConfigSaveForTesting', [Reflection.BindingFlags]'NonPublic,Static').SetValue($null, $true)
$window = [Activator]::CreateInstance($mt, @($true))
try {
    $window.PauseBackgroundRefresh = $true
    $other = New-Object RazerBatteryTray.MouseBatteryInfo
    $other.DeviceId = 'Test:1234:5678'; $other.DeviceName = 'Other mouse'; $other.IsPrimary = $true
    $other.IsConnected = $true; $other.BatteryPercent = 50; $other.Category = 'Mouse'
    $liveDevices = New-Object 'System.Collections.Generic.List[RazerBatteryTray.MouseBatteryInfo]'
    $liveDevices.Add($other); $liveDevices.Add($wired)
    $mt.GetField('selectedDevice', $nf).SetValue($window, $state)
    $mt.GetMethod('ApplyDeviceStatus', $nf).Invoke($window, @($liveDevices.PSObject.BaseObject, $false))
    $selected = $mt.GetField('selectedDevice', $nf).GetValue($window)
    Require ($selected.DeviceId -eq $wired.DeviceId -and $selected.IsCharging) 'Selected M6 page changed on USB connection'
}
finally {
    $window.Dispose()
}

Write-Host 'PASS built-in Keychron M6 protocol, receiver links, capability limits and UI continuity'
