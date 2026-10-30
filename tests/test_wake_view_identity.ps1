# Regression: reject foreign receiver callbacks while preserving original view/tray selection.
# Load the assembly from bytes to avoid locking the executable.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$a=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '..\FerrisPulse.exe')))
$t=$a.GetType('RazerBatteryTray.MainForm')
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
$t.GetField('DisableConfigSaveForTesting',[Reflection.BindingFlags]'Static,NonPublic,Public').SetValue($null,$true)
$f=[Activator]::CreateInstance($t,@($false))
try {
 $f.PauseBackgroundRefresh=$true
 $gpw=New-Object RazerBatteryTray.MouseBatteryInfo
 $gpw.DeviceId='Logitech:046D:C539:1';$gpw.DeviceName='G Pro Wireless Gaming Mouse';$gpw.Brand='Logitech';$gpw.Category=[RazerBatteryTray.DeviceCategory]::Mouse
 $gpw.BatteryPercent=26;$gpw.IsConnected=$true;$gpw.Transport='Lightspeed 无线';$gpw.Dpi=3200;$gpw.PollingRate=1000
 $other=New-Object RazerBatteryTray.MouseBatteryInfo
 $other.DeviceId='Logitech:046D:C53F:1';$other.DeviceName='LIGHTSPEED C53F';$other.Brand='Logitech';$other.IsConnected=$true;$other.BatteryPercent=-1
 $select=$t.GetMethod('SelectDeviceView',$flags)
 $update=$t.GetMethod('UpdateUI',$flags)
 $select.Invoke($f,@($gpw.psobject.BaseObject)) | Out-Null
 $label=$t.GetField('lblDeviceName',$flags).GetValue($f)
 $battery=$t.GetField('lblBatteryBig',$flags).GetValue($f)
 $beforeName=$label.Text;$beforeBattery=$battery.Text
 $update.Invoke($f,@($other.psobject.BaseObject,$false)) | Out-Null
 if($label.Text -ne $beforeName -or $battery.Text -ne $beforeBattery){throw 'Foreign wake callback overwrote main card'}
 if($t.GetField('selectedDevice',$flags).GetValue($f).DeviceId -ne $gpw.DeviceId){throw 'Renderer changed selected identity'}
 $match=$t.GetMethod('IsTargetMouseHardwarePath',[Reflection.BindingFlags]'Static,NonPublic')
 if($match.Invoke($null,@('HID\VID_046D&PID_C53F&MI_01',$gpw.psobject.BaseObject))){throw 'Different Logitech receiver matched GPW wake input'}
 if(-not $match.Invoke($null,@('HID\VID_046D&PID_C539&MI_01',$gpw.psobject.BaseObject))){throw 'GPW receiver did not match'}
 $select.Invoke($f,@($other.psobject.BaseObject)) | Out-Null
 $update.Invoke($f,@($gpw.psobject.BaseObject,$false)) | Out-Null
 if($label.Text -ne 'LIGHTSPEED C53F'){throw 'Late GPW callback overwrote new manual selection'}
 foreach($field in @('autoGameRateEnabled','autoGameDpiEnabled')) { $t.GetField($field,$flags).SetValue($f,$false) }
 $apply=$t.GetMethod('ApplyDeviceStatus',$flags)
 $list=New-Object 'System.Collections.Generic.List[RazerBatteryTray.MouseBatteryInfo]'
 $other.IsPrimary=$true
 $gpw.IsSleeping=$true
 $list.Add($other);$list.Add($gpw)
 $select.Invoke($f,@($gpw.psobject.BaseObject)) | Out-Null
 $apply.Invoke($f,@($list.psobject.BaseObject,$false)) | Out-Null
 if($t.GetField('selectedDevice',$flags).GetValue($f).DeviceId -ne $gpw.DeviceId){throw 'Original behavior: existing sleeping selection should remain selected'}
 if($t.GetField('lastInfo',$flags).GetValue($f).DeviceId -ne $other.DeviceId){throw 'Tray primary behavior changed'}
 $list.Remove($gpw) | Out-Null
 $apply.Invoke($f,@($list.psobject.BaseObject,$false)) | Out-Null
 if($t.GetField('selectedDevice',$flags).GetValue($f).DeviceId -ne $other.DeviceId){throw 'Original fallback on missing device changed'}
 $list.Clear()
 $apply.Invoke($f,@($list.psobject.BaseObject,$false)) | Out-Null
 if($null -ne $t.GetField('selectedDevice',$flags).GetValue($f)){throw 'Original empty list behavior changed'}
 Write-Host 'PASS: original sleeping selection, missing-device fallback, empty-list handling and independent tray primary'
 Write-Host 'PASS: foreign wake callback rejected; title/battery/selection coherent; exact receiver match; reverse switch protected'
} finally { $f.Dispose() }

