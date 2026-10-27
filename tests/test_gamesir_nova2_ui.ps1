# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$a=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '..\FerrisPulse.exe')))
$t=$a.GetType('RazerBatteryTray.MainForm');$flags=[Reflection.BindingFlags]'NonPublic,Instance'
$f=[Activator]::CreateInstance($t,@($false))
function C($name){return $t.GetField($name,$flags).GetValue($f)}
try {
 $d=[Activator]::CreateInstance($a.GetType('RazerBatteryTray.MouseBatteryInfo'))
 $d.Brand='GameSir';$d.DeviceId='GameSir:Nova2Lite:A96345D8CE01';$d.DeviceName='盖世小鸡 启明星2'
 $d.Category=[Enum]::Parse($a.GetType('RazerBatteryTray.DeviceCategory'),'Gamepad');$d.Transport='USB 有线'
 $d.IsConnected=$true;$d.IsCharging=$true;$d.BatteryPercent=-1
 [void]$t.GetMethod('UpdateUI',$flags).Invoke($f,@($d,$false))
 if ((C 'lblBatteryBig').Text -ne '充电中') {throw 'Charging sentinel rendered as a real percent instead of charging state'}
 if (!(C 'tileRapooDpi').Footnote.Contains('充电')) {throw 'Gamepad hardware card lost charging state'}
 $t.GetField('lastInfo',$flags).SetValue($f,$d)
 [void]$t.GetMethod('UpdateStatusDisplay',$flags).Invoke($f,$null)
 if ((C 'statusMenuItem').Text.Contains('-1%')) {throw 'Tray rendered unknown battery as negative percent'}
 $d.BatteryPercent=87
 [void]$t.GetMethod('UpdateUI',$flags).Invoke($f,@($d,$false))
 if ((C 'lblBatteryBig').Text -ne '87%') {throw 'Last known battery lost while charging'}
 Write-Host 'PASS: Nova2 unknown charging UI/tray and last known percent'
} finally {$f.Dispose()}