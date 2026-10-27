$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '../FerrisPulse.exe')))
function Assert($condition,$message) { if (-not $condition) { throw $message } }
$sample = [byte[]]('01-7E-7B-83-7B-00-00-73-08-00-00-00-05-E1-9A-B8-01-00-04-00-FD-FF-C3-FF-8D-20-60-06-E8-AE-7C-49-19-80-00-00-00-80-00-00-00-00-09-09-00-00-00-00-00-1C-00-08-04-10-30-00-9D-34-51-81-D4-65-8F-0A'.Split('-') | ForEach-Object { [Convert]::ToByte($_,16) })
$state = [RazerBatteryTray.SonyDeviceHelper]::ParseDualSenseUsbReport($sample)
Assert ($state.BatteryPercent -eq 5 -and $state.IsCharging -and $state.Category.ToString() -eq 'Gamepad') 'Recorded charging report and battery resolution'
$sample[53]=0x20
$state = [RazerBatteryTray.SonyDeviceHelper]::ParseDualSenseUsbReport($sample)
Assert ($state.BatteryPercent -eq 100 -and -not $state.IsCharging) 'Fully charged status'
$sample[53]=0x0A
Assert ([RazerBatteryTray.SonyDeviceHelper]::ParseDualSenseUsbReport($sample).BatteryPercent -eq 100) 'Discharge full level'
foreach ($invalid in @(0x0B,0xA0,0xB0,0xF0)) {
    $sample[53]=$invalid
    Assert ($null -eq [RazerBatteryTray.SonyDeviceHelper]::ParseDualSenseUsbReport($sample)) 'Reject invalid capacity and charging fault'
}
Assert ($null -eq [RazerBatteryTray.SonyDeviceHelper]::ParseDualSenseUsbReport([byte[]]@(1,0))) 'Reject short input'
Write-Host 'PASS: DualSense Edge USB battery, charging, resolution, fault states'