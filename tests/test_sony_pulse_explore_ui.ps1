$ErrorActionPreference='Stop'
$assembly=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '../FerrisPulse.exe')))
function Assert($condition,$message) { if (-not $condition) { throw $message } }
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
$type=$assembly.GetType('RazerBatteryTray.MainForm',$true)
$form=[Activator]::CreateInstance($type,@($false))
try {
    [byte[]]$report=New-Object byte[] 64
    $report[0]=0xB0; $report[1]=0x50; $report[2]=1; $report[29]=0x11; $report[39]=3; $report[55]=0xDD; $report[56]=0xFF
    $sonyHelper=$assembly.GetType('RazerBatteryTray.SonyDeviceHelper',$true)
    $state=$sonyHelper.GetMethod('ParsePulseExploreReport').Invoke($null,[object[]]@(,$report))
    $info=$sonyHelper.GetMethod('CreatePulseExploreInfo').Invoke($null,@($state))
    $tile=$type.GetField('tileHeadsetAudio',$flags).GetValue($form)
    $update=$type.GetMethod('UpdatePerformanceSection',$flags)
    $null=$update.Invoke($form,@($info,[Drawing.Color]::Blue))
    Assert ($tile.Title.Contains('PULSE Explore') -and $tile.StatusText.Contains('L≈87%') -and $tile.StatusText.Contains('R≈87%')) 'Stereo battery reaches main card'
    Assert ($tile.Footnote.Contains('充电/盒电量未知')) 'Do not show unknown charging as discharging'
    $tip=$type.GetField('toolTipTop',$flags).GetValue($form)
    Assert ($tip.GetToolTip($tile).Contains('充电状态未知')) 'Detailed hover exposes precision and charging limits'
    $report[56]=0xAF
    $sonyHelper=$assembly.GetType('RazerBatteryTray.SonyDeviceHelper',$true)
    $state=$sonyHelper.GetMethod('ParsePulseExploreReport').Invoke($null,[object[]]@(,$report))
    $info=$sonyHelper.GetMethod('CreatePulseExploreInfo').Invoke($null,@($state))
    $null=$update.Invoke($form,@($info,[Drawing.Color]::Blue))
    Assert ($tile.StatusText.Contains('L≈--') -and $tile.StatusText.Contains('R≈87%')) 'Inactive left ear does not show cached live reading'
    $report[39]=2; $report[56]=0x0F
    $sonyHelper=$assembly.GetType('RazerBatteryTray.SonyDeviceHelper',$true)
    $state=$sonyHelper.GetMethod('ParsePulseExploreReport').Invoke($null,[object[]]@(,$report))
    $info=$sonyHelper.GetMethod('CreatePulseExploreInfo').Invoke($null,@($state))
    $null=$update.Invoke($form,@($info,[Drawing.Color]::Blue))
    Assert ($tile.StatusText.Contains('耳塞离线') -and -not $tile.StatusText.Contains('87%')) 'In-case state is not presented as active battery'
    $report[30]=1; $report[55]=0xBF; $report[56]=0x3F
    $usb=$sonyHelper.GetMethod('ParsePulseExploreUsbReport').Invoke($null,[object[]]@(,$report))
    $info=$sonyHelper.GetMethod('CreatePulseExploreInfo').Invoke($null,@($usb))
    $null=$update.Invoke($form,@($info,[Drawing.Color]::Blue))
    Assert ($tile.StatusText.Contains('L≈100%') -and $tile.StatusText.Contains('R≈73%') -and $tile.Footnote.Contains('左已满') -and $tile.Footnote.Contains('右充电')) 'USB in-case charging appears per ear'
    $generic=[Activator]::CreateInstance($assembly.GetType('RazerBatteryTray.MouseBatteryInfo',$true))
    $generic.Brand='Sony'; $generic.DeviceName='Other earbuds'; $generic.Category=[RazerBatteryTray.DeviceCategory]::Earbuds; $generic.IsConnected=$true; $generic.BatteryPercent=60
    $null=$update.Invoke($form,@($generic,[Drawing.Color]::Blue))
    Assert (-not $tile.Title.Contains('PULSE') -and $tile.StatusText.Contains('60%') -and $tip.GetToolTip($tile) -eq '') 'Device switch clears Explore text and tooltip'
} finally { $form.Dispose() }
Write-Host 'PASS: Explore main card stereo values, unknown charging, in-case state and device-switch cleanup'