$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '../FerrisPulse.exe')))
function Assert($condition, $message) { if (-not $condition) { throw $message } }
$helper = $assembly.GetType('RazerBatteryTray.AtkDeviceHelper', $true)
$descriptor = New-Object RazerBatteryTray.HidDeviceDescriptor
$descriptor.VendorId=0x320F
$descriptor.ProductId=0x5088
$descriptor.ProductString='VGN S99 2.4G Dongle'
Assert (-not [RazerBatteryTray.AtkDeviceHelper]::IsSupportedDevice($descriptor)) 'Shared PID must not imply ATK model'
$descriptor.ProductString='ATK V75X 2.4G Dongle'
Assert ([RazerBatteryTray.AtkDeviceHelper]::IsSupportedDevice($descriptor)) 'Confirmed ATK product identity'
Assert ([RazerBatteryTray.DeviceManager]::GetDeviceIdVariants('ATK:320F:5088').Contains('HID:320F:5088')) 'Native/custom ID compatibility'
$packet = $helper.GetMethod('BuildBatteryQuery').Invoke($null, @([uint16]0x1234,64))
Assert ($packet.Length -eq 64 -and $packet[0] -eq 4 -and $packet[1] -eq 0x34 -and $packet[2] -eq 0x12 -and $packet[3] -eq 0x1A -and $packet[4] -eq 0) 'ATK request framing'
$parse = $helper.GetMethod('ParseBatteryResponse')
$sample = New-Object byte[] 64
[byte[]]$header = @(4,1,0,0x1A,0,0,0,0,0x57,0)
[Array]::Copy($header,$sample,$header.Length)
$state = [RazerBatteryTray.AtkDeviceHelper]::ParseBatteryResponse($sample,1)
Assert ($state.BatteryPercent -eq 87 -and -not $state.IsCharging -and $state.Category.ToString() -eq 'Keyboard') 'Recorded ATK 87% sample'
foreach ($battery in @(0,100)) {
    $sample[8] = $battery
    $sample[9] = 1
    $state = [RazerBatteryTray.AtkDeviceHelper]::ParseBatteryResponse($sample,1)
    Assert ($state.BatteryPercent -eq $battery -and $state.IsCharging) 'Battery boundaries / charging'
}
$sample[8] = 101
Assert ($null -eq [RazerBatteryTray.AtkDeviceHelper]::ParseBatteryResponse($sample,1)) 'Reject invalid percentage'
$sample[8] = 87
Assert ($null -eq [RazerBatteryTray.AtkDeviceHelper]::ParseBatteryResponse($sample,2)) 'Reject stale transaction'
$sample[0] = 0
Assert ($null -eq [RazerBatteryTray.AtkDeviceHelper]::ParseBatteryResponse($sample,1)) 'Reject wrong report'
Assert ($null -eq [RazerBatteryTray.AtkDeviceHelper]::ParseBatteryResponse([byte[]]@(4,1,0,26),1)) 'Reject truncated report'
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../src/Hardware/DeviceManager.cs'))
Assert ($source.Contains('AtkDeviceHelper.QueryAllAtkDevices()')) 'DeviceManager registration'
Assert ($source.Contains('"ATK"')) 'ATK device ID aliases'
Write-Host 'PASS: ATK V75X framing, real sample, charging, boundaries, routing'