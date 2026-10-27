$ErrorActionPreference='Stop'
$assembly=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '../FerrisPulse.exe')))
function Assert($condition,$message) { if (-not $condition) { throw $message } }
$helper=$assembly.GetType('RazerBatteryTray.SonyDeviceHelper',$true)
Assert ($null -ne $helper.GetMethod('ParsePulseExploreReport')) 'Explore decoder must exist'
function Sample($link,$levels,$flags) {
    [byte[]]$report=New-Object byte[] 64
    $report[0]=0xB0; $report[1]=0x50; $report[2]=1; $report[29]=0x11
    $report[39]=$link; $report[55]=$levels; $report[56]=$flags
    return ,$report
}
$parse=$helper.GetMethod('ParsePulseExploreReport')
$record=Sample 3 0xDD 0xFF
$state=[RazerBatteryTray.SonyDeviceHelper]::ParsePulseExploreReport($record)
Assert ($state.LeftBatteryPercent -eq 87 -and $state.RightBatteryPercent -eq 87 -and $state.IsLeftOnline -and $state.IsRightOnline) 'Real 13/15 sample agrees with official 87% display'
Assert ($state.LeftCharging -eq $null -and $state.RightCharging -eq $null) 'Do not invent charging flags for model 0011'
$info=[RazerBatteryTray.SonyDeviceHelper]::CreatePulseExploreInfo($state)
Assert ($info.BatteryPercent -eq 87 -and $info.Brand -eq 'Sony' -and $info.Category.ToString() -eq 'Earbuds' -and $info.IsConnected) 'Unified earbuds state'
Assert ($info.CustomTooltip.Contains('左耳') -and $info.CustomTooltip.Contains('右耳') -and $info.CustomTooltip.Contains('充电状态未知')) 'Per-ear tooltip and explicit unknown charging'
$state=[RazerBatteryTray.SonyDeviceHelper]::ParsePulseExploreReport((Sample 0x27 0xDF 0xAD))
Assert (-not $state.IsLeftOnline -and $state.IsRightOnline -and $state.LeftBatteryPercent -eq 100 -and $state.RightBatteryPercent -eq 87) 'Left-in-case / right-online recorded sample'
$info=[RazerBatteryTray.SonyDeviceHelper]::CreatePulseExploreInfo($state)
Assert ($info.BatteryPercent -eq 87 -and $info.CustomTooltip.Contains('入盒/离线')) 'Only active ear participates in displayed battery'
$state=[RazerBatteryTray.SonyDeviceHelper]::ParsePulseExploreReport((Sample 3 0xDF 0x5F))
Assert ($state.IsLeftOnline -and -not $state.IsRightOnline) 'Right-in-case / left-online recorded sample'
$info=[RazerBatteryTray.SonyDeviceHelper]::CreatePulseExploreInfo($state)
Assert ($info.BatteryPercent -eq 100) 'Do not use inactive right ear to lower active-left battery'
$state=[RazerBatteryTray.SonyDeviceHelper]::ParsePulseExploreReport((Sample 2 0xDE 0x0F))
$info=[RazerBatteryTray.SonyDeviceHelper]::CreatePulseExploreInfo($state)
Assert (-not $info.IsConnected -and $info.IsSleeping -and $info.IsDonglePresent -and $info.BatteryPercent -eq -1) 'Receiver present does not mean earbuds connected; no stale live percentage'
foreach ($level in 0..15) {
    $state=[RazerBatteryTray.SonyDeviceHelper]::ParsePulseExploreReport((Sample 3 ($level*17) 0xFF))
    $expected=[int][Math]::Floor(($level*100+7)/15)
    Assert ($state.LeftBatteryPercent -eq $expected -and $state.RightBatteryPercent -eq $expected) 'Official 0..15 quantization / rounding'
}
$record=Sample 3 0xDE 0xFF
$record[0]=0x82
Assert ($null -eq [RazerBatteryTray.SonyDeviceHelper]::ParsePulseExploreReport($record)) 'Reject Elite-style battery report as Explore stereo status'
$record[0]=0xB0; $record[29]=0x10
Assert ($null -eq [RazerBatteryTray.SonyDeviceHelper]::ParsePulseExploreReport($record)) 'Shared adapter must not imply Explore model'
$record[29]=0x11; $record[30]=1
Assert ($null -eq [RazerBatteryTray.SonyDeviceHelper]::ParsePulseExploreReport($record)) 'Untested 0111 model is not silently treated as 0011'
Assert ($null -eq [RazerBatteryTray.SonyDeviceHelper]::ParsePulseExploreReport((New-Object byte[] 64))) 'Reject disconnected all-zero invalid status'
Assert ($null -eq [RazerBatteryTray.SonyDeviceHelper]::ParsePulseExploreReport([byte[]]@(0xB0,0x50,1))) 'Reject truncated status'
$record=Sample 2 0xDD 0xFF
$state=[RazerBatteryTray.SonyDeviceHelper]::ParsePulseExploreReport($record)
Assert (-not $state.IsLeftOnline -and -not $state.IsRightOnline) 'Global link-off overrides stale per-ear flags'
$wirelessReport=Sample 3 0xDD 0xFF
$wirelessReport[3]=0x42
$wireless=[RazerBatteryTray.SonyDeviceHelper]::ParsePulseExploreReport($wirelessReport)
$usbReport=Sample 3 0xBF 0x3F
$usbReport[3]=0x42; $usbReport[30]=1; $usbReport[61]=0x0F
$usb=[RazerBatteryTray.SonyDeviceHelper]::ParsePulseExploreUsbReport($usbReport)
Assert ($usb.IsUsbCaseConnected -and $usb.IsLeftDocked -and $usb.IsRightDocked -and $usb.LeftBatteryPercent -eq 100 -and $usb.RightBatteryPercent -eq 73) 'Recorded USB case state is earbud battery, not case battery'
Assert ($usb.LeftCharging -eq $false -and $usb.RightCharging -eq $true) 'USB docked full-left and charging-right state'
$merged=[RazerBatteryTray.SonyDeviceHelper]::MergePulseExploreUsbState($wireless,$usb)
Assert ($merged.IsUsbCaseConnected -and -not $merged.IsLeftOnline -and -not $merged.IsRightOnline) 'Matching USB identity supersedes stale wireless ear flags'
$info=[RazerBatteryTray.SonyDeviceHelper]::CreatePulseExploreInfo($merged)
Assert ($info.BatteryPercent -eq 73 -and $info.IsCharging -and $info.CustomTooltip.Contains('USB在盒充电') -and $info.CustomTooltip.Contains('充电盒自身电量未知')) 'USB charging integrates without inventing case percentage'
Assert ($wireless.IsLeftOnline -and -not $wireless.IsUsbCaseConnected) 'Merge must not mutate cached wireless snapshot'
$usbReport[3]=0x43
$other=[RazerBatteryTray.SonyDeviceHelper]::ParsePulseExploreUsbReport($usbReport)
Assert (-not [RazerBatteryTray.SonyDeviceHelper]::MergePulseExploreUsbState($wireless,$other).IsUsbCaseConnected) 'Never merge a different physical headset'
$usbReport[3]=0x42; $usbReport[55]=15; $usbReport[56]=0x1F
$usb=[RazerBatteryTray.SonyDeviceHelper]::ParsePulseExploreUsbReport($usbReport)
$info=[RazerBatteryTray.SonyDeviceHelper]::CreatePulseExploreInfo($usb)
Assert ($usb.RightBatteryPercent -eq -1 -and $usb.RightCharging -eq $null -and $info.CustomTooltip.Contains('右耳：未知')) 'USB absent ear is unknown, never false 0%'
$usbReport[29]=0x10
Assert ($null -eq [RazerBatteryTray.SonyDeviceHelper]::ParsePulseExploreUsbReport($usbReport)) 'USB case parser also validates model'
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot '../src/Hardware/SonyDeviceHelper.PulseExplore.cs'))
Assert ($source.Contains('OpenFeature') -and $source.Contains('ReadFeatureReport') -and -not $source.Contains('ReadReport(') -and -not $source.Contains('WriteReport(') -and -not $source.Contains('SetFeature')) 'Read-only control reports, no interrupt input or configuration writes'
Write-Host 'PASS: Explore stereo/USB samples, official 87%, quantization, charging/full, identity isolation, sleep and unknown case battery'