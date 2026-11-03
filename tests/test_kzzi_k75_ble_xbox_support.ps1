$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '../FerrisPulse.exe')))
function Assert($condition, $message) { if (-not $condition) { throw $message } }
$tInfo = $assembly.GetType('RazerBatteryTray.MouseBatteryInfo', $true)
$tDesc = $assembly.GetType('RazerBatteryTray.HidDeviceDescriptor', $true)
$tKzzi = $assembly.GetType('RazerBatteryTray.KzziKeyboardDriver', $true)
$tBle = $assembly.GetType('RazerBatteryTray.BlePeripheralDriver', $true)

# KZZI K75 实机 0x05 状态帧
[byte[]]$status = @(0x13,0x05,0x01,0x00,0x0A,0x02,0x00,0x00,0x00,0x00,0x37,0x00,0x01,0x00,0x00,0x00,0x00,0x00,0x00,0x5D)
Assert ([RazerBatteryTray.KzziDeviceHelper]::IsKzziStatusFrame($status, $status.Length)) 'KZZI status fingerprint must match'
$badCrc = $status.Clone()
$badCrc[19] = 0x00
Assert (-not [RazerBatteryTray.KzziDeviceHelper]::IsKzziStatusFrame($badCrc, $badCrc.Length)) 'Reject bad checksum'
$badCmd = $status.Clone()
$badCmd[1] = 0x4A
Assert (-not [RazerBatteryTray.KzziDeviceHelper]::IsKzziStatusFrame($badCmd, $badCmd.Length)) 'Reject MCHOSE 0x4A command'

[byte[]]$query = [RazerBatteryTray.KzziDeviceHelper]::BuildStatusQuery()
Assert ($query.Length -eq 20) 'KZZI query length'
Assert ($query[0] -eq 0x13 -and $query[1] -eq 0x05 -and $query[2] -eq 0x01 -and $query[4] -eq 0x02) 'KZZI query header'
Assert ($query[19] -eq 0x1B) 'KZZI query checksum'

$d = New-Object -TypeName 'RazerBatteryTray.HidDeviceDescriptor'
$d.VendorId = 0x3554
$d.ProductId = 0xFA09
$d.UsagePage = 0xFF02
$d.Usage = 0x0002
$d.InputReportLength = 20
$d.OutputReportLength = 20
Assert ([RazerBatteryTray.KzziDeviceHelper]::IsKzziEndpoint($d)) 'KZZI endpoint match'
$d.VendorId = 0x25A7
Assert (-not [RazerBatteryTray.KzziDeviceHelper]::IsKzziEndpoint($d)) 'Reject Ironcat VID'
$d.VendorId = 0x3554
$d.InputReportLength = 17
Assert (-not [RazerBatteryTray.KzziDeviceHelper]::IsKzziEndpoint($d)) 'Reject 17-byte VGN endpoint'

# 品牌化卡片不能被历史通用指纹隐藏项连带隐藏
$dev = New-Object -TypeName 'RazerBatteryTray.MouseBatteryInfo'
$dev.DeviceId = 'KZZI:3554:FA09'
$dev.Brand = 'KZZI'
Assert (-not [RazerBatteryTray.DeviceManager]::ShouldHideDevice($dev, $false, $true)) 'Brand-scoped KZZI must survive legacy fingerprint hide'
$dev.DeviceId = 'HID:3554:FA09'
Assert ([RazerBatteryTray.DeviceManager]::ShouldHideDevice($dev, $false, $true)) 'Generic HID fingerprint must still honor hide'

$kzziDriver = New-Object -TypeName 'RazerBatteryTray.KzziKeyboardDriver'
$kzziDev = New-Object -TypeName 'RazerBatteryTray.MouseBatteryInfo'
$kzziDev.Category = [RazerBatteryTray.DeviceCategory]::Keyboard
$kzziDev.Brand = 'KZZI'
Assert ($kzziDriver.CanHandle($kzziDev)) 'KZZI keyboard driver routing'
$kzziDriver.PopulateCapabilities($kzziDev)
Assert ($kzziDev.Capabilities.SupportsBattery -and -not $kzziDev.Capabilities.SupportsDpi) 'KZZI capabilities'

$bleDriver = New-Object -TypeName 'RazerBatteryTray.BlePeripheralDriver'
$bleDev = New-Object -TypeName 'RazerBatteryTray.MouseBatteryInfo'
$bleDev.DriverId = 'BuiltIn:BlePeripheral'
Assert ($bleDriver.CanHandle($bleDev)) 'BLE driver routing'
$bleDriver.PopulateCapabilities($bleDev)
Assert ($bleDev.Capabilities.SupportsBattery -and -not $bleDev.Capabilities.SupportsDpi) 'BLE capabilities'

$dm = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../src/Hardware/DeviceManager.cs'))
Assert ($dm.Contains('KzziDeviceHelper.QueryKzziDevice()')) 'DeviceManager KZZI registration missing'
Assert ($dm.Contains('BleDeviceHelper.QueryAllBlePeripherals()')) 'DeviceManager BLE registration missing'
$bi = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../src/Hardware/BuiltInDrivers.cs'))
Assert ($bi.Contains('new KzziKeyboardDriver()')) 'BuiltInDrivers KZZI registration missing'
Assert ($bi.Contains('new BlePeripheralDriver()')) 'BuiltInDrivers BLE registration missing'
$mc = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../src/Hardware/MchoseDeviceHelper.cs'))
Assert ($mc.Contains('KzziDeviceHelper.TryIdentifyKzziK75(selected)')) 'MCHOSE shared-PID guard missing'
$bt = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../src/Hardware/BluetoothDeviceHelper.cs'))
Assert ($bt.Contains('kzzi')) 'Bluetooth KZZI category mapping missing'

Write-Host 'PASS: KZZI K75 fingerprint, shared-PID isolation, hidden-record exemption, BLE keyboard/gamepad routing'
