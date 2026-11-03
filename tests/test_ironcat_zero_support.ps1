$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '../FerrisPulse.exe')))
function Assert($condition, $message) { if (-not $condition) { throw $message } }

[byte[]]$query = [RazerBatteryTray.IroncatDeviceHelper]::BuildBatteryQuery()
Assert ($query.Length -eq 17) 'Ironcat query must be 17 bytes'
Assert ($query[0] -eq 0x08 -and $query[1] -eq 0x04) 'Ironcat query header must be 08 04'
Assert ($query[16] -eq 0x49) 'Ironcat query checksum must be 0x49'
$sum = 0
for ($i = 0; $i -lt 16; $i++) { $sum += $query[$i] }
Assert ($query[16] -eq [byte](85 - ($sum -band 0xFF))) 'Ironcat checksum formula'

# Real capture while charging: 95%, status 1, checksum E6.
[byte[]]$chargingSample = @(0x09,0x04,0x00,0x00,0x00,0x02,0x5F,0x01,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xE6)
$battery = 0
$charging = $false
$voltage = 0
$ok = [RazerBatteryTray.IroncatDeviceHelper]::ParseBatteryResponse($chargingSample, 17, [ref]$battery, [ref]$charging, [ref]$voltage)
Assert ($ok -and $battery -eq 95 -and $charging -and $voltage -eq -1) 'Recorded 95% charging sample'

# Real capture after full charge: 100%, status 0, checksum E2.
[byte[]]$fullSample = @(0x09,0x04,0x00,0x00,0x00,0x02,0x64,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xE2)
$ok = [RazerBatteryTray.IroncatDeviceHelper]::ParseBatteryResponse($fullSample, 17, [ref]$battery, [ref]$charging, [ref]$voltage)
Assert ($ok -and $battery -eq 100 -and -not $charging) 'Recorded 100% full sample'

foreach ($level in @(0, 100)) {
    [byte[]]$sample = $fullSample.Clone()
    $sample[6] = [byte]$level
    $s = 0
    for ($i = 0; $i -lt 16; $i++) { $s += $sample[$i] }
    $sample[16] = [byte]((85 - ($s -band 0xFF)) -band 0xFF)
    $ok = [RazerBatteryTray.IroncatDeviceHelper]::ParseBatteryResponse($sample, 17, [ref]$battery, [ref]$charging, [ref]$voltage)
    Assert ($ok -and $battery -eq $level) "Battery boundary $level"
}

[byte[]]$invalid = $fullSample.Clone()
$invalid[6] = 101
$s = 0
for ($i = 0; $i -lt 16; $i++) { $s += $invalid[$i] }
$invalid[16] = [byte]((85 - ($s -band 0xFF)) -band 0xFF)
$ok = [RazerBatteryTray.IroncatDeviceHelper]::ParseBatteryResponse($invalid, 17, [ref]$battery, [ref]$charging, [ref]$voltage)
Assert (-not $ok) 'Reject percentage above 100'

[byte[]]$badCrc = $fullSample.Clone()
$badCrc[16] = 0x00
$ok = [RazerBatteryTray.IroncatDeviceHelper]::ParseBatteryResponse($badCrc, 17, [ref]$battery, [ref]$charging, [ref]$voltage)
Assert (-not $ok) 'Reject corrupted checksum'

[byte[]]$badId = $fullSample.Clone()
$badId[0] = 0x08
$ok = [RazerBatteryTray.IroncatDeviceHelper]::ParseBatteryResponse($badId, 17, [ref]$battery, [ref]$charging, [ref]$voltage)
Assert (-not $ok) 'Reject wrong response report id'

[byte[]]$badCmd = $fullSample.Clone()
$badCmd[1] = 0x03
$ok = [RazerBatteryTray.IroncatDeviceHelper]::ParseBatteryResponse($badCmd, 17, [ref]$battery, [ref]$charging, [ref]$voltage)
Assert (-not $ok) 'Reject wrong command echo'

$ok = [RazerBatteryTray.IroncatDeviceHelper]::ParseBatteryResponse($fullSample, 16, [ref]$battery, [ref]$charging, [ref]$voltage)
Assert (-not $ok) 'Reject truncated report'

Assert ([RazerBatteryTray.IroncatDeviceHelper]::IsIroncatConfigEndpoint(0x25A7, 0xFA7C, 0xFF02, 0x0002, 17)) 'Config endpoint match'
Assert ([RazerBatteryTray.IroncatDeviceHelper]::IsIroncatInputEndpoint(0x25A7, 0xFA7C, 0xFF01, 0x0000, 17)) 'Input endpoint match'
Assert (-not [RazerBatteryTray.IroncatDeviceHelper]::IsIroncatConfigEndpoint(0x3554, 0xFA09, 0xFF02, 0x0002, 20)) 'Reject MCHOSE G75 endpoint'
Assert (-not [RazerBatteryTray.IroncatDeviceHelper]::IsIroncatConfigEndpoint(0x25A7, 0xFA7C, 0xFF02, 0x0002, 20)) 'Reject wrong feature length'
Assert (-not [RazerBatteryTray.IroncatDeviceHelper]::IsIroncatConfigEndpoint(0x25A7, 0xFA07, 0xFF02, 0x0002, 17)) 'Reject sibling PID'

$dm = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../src/Hardware/DeviceManager.cs'))
Assert ($dm.Contains('IroncatDeviceHelper.QueryAllIroncatDevices()')) 'DeviceManager registration missing'
Assert ([RazerBatteryTray.DeviceManager]::GetDeviceIdVariants('Ironcat:25A7:FA7C').Contains('HID:25A7:FA7C')) 'Device ID aliases missing'

$dev = New-Object RazerBatteryTray.MouseBatteryInfo
$dev.Category = [RazerBatteryTray.DeviceCategory]::Mouse
$dev.Brand = 'Ironcat'
$driver = New-Object RazerBatteryTray.IroncatMouseDriver
Assert ($driver.CanHandle($dev)) 'Ironcat driver routing missing'
$driver.PopulateCapabilities($dev)
Assert ($dev.Capabilities.SupportsBattery -and -not $dev.Capabilities.SupportsDpi -and -not $dev.Capabilities.SupportsPollingRate) 'Ironcat capabilities must stay measured-only'

$bi = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../src/Hardware/BuiltInDrivers.cs'))
Assert ($bi.Contains('new IroncatMouseDriver()')) 'BuiltInDrivers registration missing'
$ug = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../src/Hardware/UniversalGamingDeviceHelper.cs'))
Assert ($ug.Contains('pid_fa7c')) 'Universal engine exclusion missing'

Write-Host 'PASS: Ironcat Zero 25A7:FA7C framing, real samples, charging, boundaries, endpoint isolation, routing'
