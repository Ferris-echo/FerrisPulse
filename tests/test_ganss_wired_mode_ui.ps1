# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# GANSS 有线模式 UI 回归：有线时显示“供电中”并隐藏过期电量，通道药丸必须是 USB 有线。
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$a=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '..\FerrisPulse.exe')))
$t=$a.GetType('RazerBatteryTray.MainForm');$flags=[Reflection.BindingFlags]'NonPublic,Instance'
$f=[Activator]::CreateInstance($t,@($false))
function C($name){return $t.GetField($name,$flags).GetValue($f)}
try {
  $d=[Activator]::CreateInstance($a.GetType('RazerBatteryTray.MouseBatteryInfo'))
  $d.Brand='GANSS';$d.DeviceId='HID:05AC:024F';$d.HardwareFingerprint='05AC:024F';$d.DeviceName='GS3108T-PRO'
  $d.CustomName='GS3108T-PRO 2.4G'  # 接收器时代自动写入的名字，不能在有线时继续冒充型号
  $d.Category=[Enum]::Parse($a.GetType('RazerBatteryTray.DeviceCategory'),'Keyboard')
  # 有线模式下接收器不再回报电量，界面必须显示供电中，不能把旧 100% 冒充当前电量。
  $d.Transport='USB 有线';$d.IsConnected=$true;$d.IsDonglePresent=$true;$d.BatteryPercent=-1;$d.LastUpdated=(Get-Date).AddMinutes(-5)
  [void]$t.GetMethod('UpdateUI',$flags).Invoke($f,@($d,$false))
  if ((C 'lblBatteryBig').Text -ne '已连接 · 供电中') {throw ('Wired GANSS still presents a stale percentage: ' + (C 'lblBatteryBig').Text)}
  if ((C 'lblDeviceName').Text -ne 'GS3108T-PRO') {throw ('Wired GANSS still shows the 2.4G model suffix: ' + (C 'lblDeviceName').Text)}
  if ((C 'pillStatus').Text -ne '🔌 USB 有线') {throw ('Wired GANSS still displays channel pill: ' + (C 'pillStatus').Text)}
  if (!(C 'lblUpdateTime').Text.Contains('USB 有线') -or !(C 'lblUpdateTime').Text.Contains('不可读')) {throw ('Wired GANSS footnote does not explain missing battery: ' + (C 'lblUpdateTime').Text)}
  $t.GetField('lastInfo',$flags).SetValue($f,$d)
  [void]$t.GetMethod('UpdateStatusDisplay',$flags).Invoke($f,$null)
  if (!(C 'statusMenuItem').Text.Contains('USB 有线')) {throw ('Tray status still hides wired transport: ' + (C 'statusMenuItem').Text)}
  if ((C 'statusMenuItem').Text.Contains('2.4G')) {throw ('Tray status still claims 2.4G while wired: ' + (C 'statusMenuItem').Text)}
  Write-Host 'PASS: GANSS wired shows powered state, hides stale battery and no longer claims 2.4G'
} finally {$f.Dispose()}
