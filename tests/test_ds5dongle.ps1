# Recorded user RX from LCT616-DS5 3.22H; mutations below are synthetic negative tests.
$ErrorActionPreference = 'Stop'
[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '../FerrisPulse.exe'))) | Out-Null
function Assert($value, $message) { if (-not $value) { throw $message } }
[byte[]]$rx = New-Object byte[] 128
$rx[0]=0xF9; $rx[2]=0x82; $rx[12]=0x14
$state = [RazerBatteryTray.Ds5DongleDeviceHelper]::ParseBatteryReport($rx)
Assert ($state.BatteryPercent -eq 20 -and -not $state.IsCharging) 'Recorded 20 percent discharging RX'
Assert ($state.Category.ToString() -eq 'Gamepad' -and $state.IsDonglePresent) 'Device classification'
$rx[13]=1
Assert ([RazerBatteryTray.Ds5DongleDeviceHelper]::ParseBatteryReport($rx).IsCharging) 'Synthetic charging state'
$rx[13]=2; $rx[12]=100
Assert (-not [RazerBatteryTray.Ds5DongleDeviceHelper]::ParseBatteryReport($rx).IsCharging) 'Full is not charging'
foreach ($bad in @(101,255)) {
    $rx[12]=$bad
    Assert ($null -eq [RazerBatteryTray.Ds5DongleDeviceHelper]::ParseBatteryReport($rx)) 'Reject unknown or invalid capacity'
}
$rx[12]=20; $rx[13]=10
Assert ($null -eq [RazerBatteryTray.Ds5DongleDeviceHelper]::ParseBatteryReport($rx)) 'Reject charging fault'
$rx[13]=0; $rx[2]=0
Assert ($null -eq [RazerBatteryTray.Ds5DongleDeviceHelper]::ParseBatteryReport($rx)) 'Reject invalid telemetry flags'
$rx[2]=0x82; $rx[0]=1
Assert ($null -eq [RazerBatteryTray.Ds5DongleDeviceHelper]::ParseBatteryReport($rx)) 'Reject wrong Report ID'
Assert ($null -eq [RazerBatteryTray.Ds5DongleDeviceHelper]::ParseBatteryReport([byte[]]@(249,0))) 'Reject short RX'
[byte[]]$version = New-Object byte[] 128
$version[0]=0xF8
[Text.Encoding]::ASCII.GetBytes('LCT616-DS5 3.22H').CopyTo($version,1)
Assert ([RazerBatteryTray.Ds5DongleDeviceHelper]::IsSupportedFirmware($version)) 'Recorded version identity'
$version[1]=0
Assert (-not [RazerBatteryTray.Ds5DongleDeviceHelper]::IsSupportedFirmware($version)) 'Reject unrelated firmware'
$ep=New-Object RazerBatteryTray.HidDeviceDescriptor
$ep.VendorId=0x054C; $ep.ProductId=0x0CE6; $ep.UsagePage=1; $ep.Usage=5
$ep.InputReportLength=64; $ep.OutputReportLength=48; $ep.FeatureReportLength=128
Assert ([RazerBatteryTray.Ds5DongleDeviceHelper]::IsSupportedEndpoint($ep)) 'Recorded endpoint'
foreach ($field in @('VendorId','ProductId','UsagePage','Usage','InputReportLength','OutputReportLength','FeatureReportLength')) {
    $original=$ep.$field; $ep.$field=0
    Assert (-not [RazerBatteryTray.Ds5DongleDeviceHelper]::IsSupportedEndpoint($ep)) ('Reject mismatch '+$field)
    $ep.$field=$original
}
$driver=New-Object RazerBatteryTray.Ds5DongleDriver
$driver.PopulateCapabilities($state)
Assert ($state.Capabilities.SupportsBattery -and -not $state.Capabilities.SupportsDpi -and -not $state.Capabilities.SupportsPollingRate) 'Read-only capabilities'
$unrelated=New-Object RazerBatteryTray.MouseBatteryInfo
$unrelated.Brand='Sony'
Assert (-not $driver.CanHandle($unrelated)) 'Do not claim every Sony device'
Assert ([RazerBatteryTray.HardwareDriverRegistry]::ResolveDriver($state).DriverId -eq 'BuiltIn:Ds5Dongle') 'Registry binds the dedicated battery driver'
Write-Host 'PASS: DS5Dongle recorded RX, firmware identity, topology, unknown/fault states, capabilities'
