# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# UI continuity for external M6 states. Protocol replay is in test_external_peripheral_packages.ps1.
$ErrorActionPreference='Stop'
$a=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Resolve-Path "$PSScriptRoot/../FerrisPulse.exe")))
[Environment]::CurrentDirectory=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$package=Join-Path $PSScriptRoot '../packages/keychron_m6.fp-driver.cs'
$compileArgs=[object[]]@([IO.File]::ReadAllText($package),$package,$null)
for($i=0;$i -lt 2;$i++) { $compileArgs[$i]=$compileArgs[$i].PSObject.BaseObject }
$drivers=$a.GetType('RazerBatteryTray.CsScriptDriverLoader').GetMethod('CompileDriversFromSource').Invoke($null,$compileArgs)
if ($compileArgs[2].Count -or $drivers.Count -ne 1) { throw ($compileArgs[2] -join '; ') }
$driver=$drivers[0]
[RazerBatteryTray.HardwareDriverRegistry]::RegisterDriver($driver)
$flags=[Reflection.BindingFlags]'NonPublic,Static'
$state=New-Object RazerBatteryTray.MouseBatteryInfo
$state.DeviceId='Keychron:3434:D026:MS'; $state.PhysicalUnitId='Keychron:ReceiverPath:offline-rf'
$state.DeviceName='Keychron M6'; $state.Category='Mouse'; $state.IsConnected=$true
$wired=New-Object RazerBatteryTray.MouseBatteryInfo
$wired.DeviceId='Keychron:3434:D060:MS'; $wired.PhysicalUnitId=$state.PhysicalUnitId
$wired.DeviceName='Keychron M6'; $wired.Category='Mouse'; $wired.IsConnected=$true
$wired.Transport='USB 有线'; $wired.IsCharging=$true; $wired.BatteryPercent=86
$wired.HardwareId='offline-wired-control'
$endpoint=New-Object RazerBatteryTray.HidDeviceDescriptor
$endpoint.VendorId=0x3434; $endpoint.ProductId=0xD060; $endpoint.UsagePage=0xFFC1; $endpoint.Usage=1
$endpoint.InputReportLength=64; $endpoint.OutputReportLength=64; $endpoint.FeatureReportLength=0; $endpoint.DevicePath=$wired.HardwareId
$identity=New-Object byte[] 64
$identity[0]=0xB6; $identity[1]=2; $identity[4]=0x34; $identity[5]=0x34; $identity[6]=0x60; $identity[7]=0xD0
$protocol=$driver.GetType().Assembly.GetType('FerrisPulse.Packages.KeychronM6.Protocol')
$null=$protocol.GetMethod('AcceptIdentity',$flags).Invoke($null,@($endpoint.PSObject.BaseObject,$identity.PSObject.BaseObject))
$driver.PopulateCapabilities($wired)
if (![object]::ReferenceEquals([RazerBatteryTray.HardwareDriverRegistry]::ResolveDriver($wired),$driver)) { throw 'M6 external routing failed' }
$mt=$a.GetType('RazerBatteryTray.MainForm')
$nf=[Reflection.BindingFlags]'NonPublic,Instance'
# Refreshing a selected M6 must follow its confirmed USB association.
$mt.GetField('DisableConfigSaveForTesting',[Reflection.BindingFlags]'NonPublic,Static').SetValue($null,$true)
$window=[Activator]::CreateInstance($mt,@($true))
try {
    $window.PauseBackgroundRefresh=$true
    $other=New-Object RazerBatteryTray.MouseBatteryInfo
    $other.DeviceId='Test:1234:5678'; $other.DeviceName='Other mouse'; $other.IsPrimary=$true
    $other.IsConnected=$true; $other.BatteryPercent=50; $other.Category='Mouse'
    $liveDevices=New-Object 'System.Collections.Generic.List[RazerBatteryTray.MouseBatteryInfo]'
    $liveDevices.Add($other); $liveDevices.Add($wired)
    $mt.GetField('selectedDevice',$nf).SetValue($window,$state)
    $mt.GetMethod('ApplyDeviceStatus',$nf).Invoke($window,@($liveDevices.PSObject.BaseObject,$false))
    $selected=$mt.GetField('selectedDevice',$nf).GetValue($window)
    if ($selected.DeviceId -ne $wired.DeviceId -or !$selected.IsCharging) {throw 'Selected M6 page changed to a different mouse on USB connection'}
} finally { $window.Dispose() }
Write-Host 'PASS external M6 driver routing and selected page continuity'
