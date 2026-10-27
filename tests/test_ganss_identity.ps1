# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
$ErrorActionPreference='Stop'
$a=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '..\FerrisPulse.exe')))
$t=$a.GetType('RazerBatteryTray.GanssDeviceHelper')
if (!$t) { throw 'Missing GS3108T-PRO identity correction' }
$cType=$a.GetType('RazerBatteryTray.CustomDeviceConfig')
$c=[Activator]::CreateInstance($cType)
$c.OriginalName='GS3108T-PRO 2.4G'; $c.DeviceId='HID:05AC:024F'; $c.HardwareFingerprint='05AC:024F'
$c.Category=[Enum]::Parse($a.GetType('RazerBatteryTray.DeviceCategory'),'Mouse'); $c.Transport='蓝牙 (BLE)'; $c.CachedBattery=-1
[void]$t.GetMethod('NormalizeConfig').Invoke($null,@($c))
if ($c.Category.ToString() -ne 'Keyboard' -or $c.Transport -ne '2.4G 无线' -or $c.CachedBattery -ne -1) { throw 'Composite keyboard classified as mouse/BLE or fake battery added' }
$c.OriginalName='GS3108T-PRO'; $c.DeviceId='BT_123456789ABC'; $c.HardwareFingerprint='123456789ABC'; $c.Transport='蓝牙 (BLE)'
$ganssDevice=[Activator]::CreateInstance($a.GetType('RazerBatteryTray.MouseBatteryInfo'))
$ganssDevice.Brand='GANSS'; $ganssDevice.DeviceId='HID:05AC:024F'; $ganssDevice.BatteryPercent=100
if (!$t.GetMethod('MatchesDeviceModel').Invoke($null,@($c,$ganssDevice))) { throw 'Legacy Bluetooth config without 2.4G suffix was not merged into the battery-bearing GANSS row' }
$ganssDevice.Brand='Apple'
if ($t.GetMethod('MatchesDeviceModel').Invoke($null,@($c,$ganssDevice))) { throw 'Legacy GANSS config incorrectly merged into a non-GANSS device' }
$c.OriginalName='GS3108T-PRO'; $c.DeviceId='BT_123456789ABC'; $c.HardwareFingerprint='123456789ABC'; $c.Transport='蓝牙 (BLE)'
[void]$t.GetMethod('NormalizeConfig').Invoke($null,@($c))
if ($c.Transport -ne '蓝牙 (BLE)') { throw 'Real Bluetooth transport overwritten' }
$c.OriginalName='Apple Keyboard'; $c.CustomName=''; $c.DeviceId='HID:05AC:024F'; $c.Transport='USB'
if ($t.GetMethod('NormalizeConfig').Invoke($null,@($c))) { throw 'Shared Apple VID/PID incorrectly identified as GANSS' }
if (!$t.GetMethod('IsKnownKeyboardName').Invoke($null,@('GS3108T-PRO 2.4G'))) { throw 'Known product not recognized' }
Write-Host 'PASS: GANSS composite keyboard identity, physical transport and shared VID/PID isolation'

$q=$t.GetMethod('BuildBatteryQuery').Invoke($null,$null)
if ($q.Length -ne 33 -or $q[0] -ne 0 -or $q[1] -ne 0x20 -or $q[2] -ne 1 -or $q[32] -ne 0x21) { throw 'Official battery command/checksum mismatch' }
foreach ($b in @(0,1,75,100)) {
    $raw=New-Object byte[] 33; $raw[1]=0x20; $raw[2]=1; $raw[4]=$b
    $args=[object[]]@(([byte[]]$raw),[int]-1)
    if (!$t.GetMethod('TryParseBattery').Invoke($null,$args) -or $args[1] -ne $b) { throw 'Battery response offset/boundary mismatch' }
}
$bad=[byte[]]@(0,0x20,1,0,255); $args=[object[]]@($bad,-1)
if ($t.GetMethod('TryParseBattery').Invoke($null,$args)) { throw 'Malformed battery accepted' }
Write-Host 'PASS: official battery command and response boundaries'

# Actual host capture, 2026-10-23, official driver exited: 00 20 01 00 64 ... 85.
[byte[]]$captured = @(0)*33; $captured[1]=0x20; $captured[2]=1; $captured[4]=0x64; $captured[32]=0x85
$args=[object[]]@($captured,-1)
if (!$t.GetMethod('TryParseBattery').Invoke($null,$args) -or $args[1] -ne 100) { throw 'Captured 100% frame rejected' }

$apple=$a.GetType('RazerBatteryTray.AppleDeviceHelper').GetMethod('IsAppleMagicDevice')
if ($apple.Invoke($null,@('hid#vid_05ac&pid_024f','GS3108T-PRO 2.4G',0x05AC,0x024F))) { throw 'GANSS shared VID misdetected as Apple: duplicate mouse row' }
if (!$apple.Invoke($null,@('hid#vid_05ac&pid_0267','Magic Keyboard',0x05AC,0x0267))) { throw 'Real Apple Magic Keyboard rejected' }
$appleVendor=$a.GetType('RazerBatteryTray.AppleDeviceHelper').GetMethod('IsAppleVendorIdentity')
if ($appleVendor.Invoke($null,@('BTHENUM\{00000000-0000-0000-0000-000000000000}_VID&000205AC_PID&024F','GS3108T-PRO 2.4G'))) { throw 'GANSS shared VID misdetected as paired Apple device: duplicate keyboard row' }
if (!$appleVendor.Invoke($null,@('BTHENUM\{00000000-0000-0000-0000-000000000000}_VID&000205AC_PID&0267','Magic Keyboard'))) { throw 'Real paired Apple Magic Keyboard rejected' }
Write-Host 'PASS: GANSS is not duplicated by Apple discovery; genuine Magic Keyboard retained'