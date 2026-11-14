# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# Late VT3 reports must update existing UI snapshots without querying hardware.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$a=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '..\FerrisPulse.exe')))
$t=$a.GetType('RazerBatteryTray.MainForm');$h=$a.GetType('RazerBatteryTray.RapooDeviceHelper')
$flags=[Reflection.BindingFlags]'NonPublic,Instance';$sf=[Reflection.BindingFlags]'NonPublic,Static'
function Assert($ok,$message){if(!$ok){throw $message}}
$f=[Activator]::CreateInstance($t,@($false))
try {
 $info=[Activator]::CreateInstance($a.GetType('RazerBatteryTray.MouseBatteryInfo'))
 $info.Brand='Rapoo';$info.DeviceId='Rapoo:24AE:4612';$info.HardwareFingerprint='HID:24AE:4612';$info.DeviceName='VT3'
 $info.Category=[Enum]::Parse($a.GetType('RazerBatteryTray.DeviceCategory'),'Mouse');$info.Transport='USB 有线'
 $info.IsConnected=$true;$info.IsPrimary=$true;$info.BatteryPercent=-1;$info.LastUpdated=[DateTime]::MinValue
 $info.Dpi=1200;$info.DpiY=1200;$info.DpiStage=3;$info.DpiStageCount=6
 $info.DpiStages=[int[]]@(400,800,1200,1600,3200,6400);$info.DpiStagesY=$info.DpiStages
 $list=[Activator]::CreateInstance($t.GetField('activeDevices',$flags).FieldType);$list.Add($info)
 [void]$t.GetMethod('ApplyDeviceStatus',$flags).Invoke($f,@($list.PSObject.BaseObject,$false))
 $sync=$t.GetMethod('SynchronizeRapooStatus',$flags)
 Assert ($null -ne $sync) 'Late telemetry has no independent UI synchronization'
 $endpoint=[Activator]::CreateInstance($a.GetType('RazerBatteryTray.HidDeviceDescriptor'));$endpoint.ProductId=0x4612
 $set=[Activator]::CreateInstance($a.GetType('RazerBatteryTray.RapooEndpointSet'));$set.Control=$endpoint;$set.GroupKey='test-wire'
 $h.GetField('activeRapooEndpoints',$sf).SetValue($null,$set)
 $h.GetField('activeRapooGroup',$sf).SetValue($null,'test-wire');$h.GetField('activeRapooModelPid',$sf).SetValue($null,0x4612)
 [byte[]]$frame=@(7,0x12,2,0xB0,4,0xB0,4,2,67,0,3,0x81,0,0x12,0x46,1,0,5,0)
 [void]$h.GetMethod('ApplyStatusReport').Invoke($null,[object[]](,$frame))
 [void]$sync.Invoke($f,$null)
 Assert ($info.BatteryPercent -eq 67 -and $info.IsCharging) 'Late report did not update existing device snapshot'
 Assert ($t.GetField('lblBatteryBig',$flags).GetValue($f).Text -eq '67%') 'Late report did not update battery label'
 $sample=$info.LastUpdated
 # A stale full-scan completion must be repaired without a second hardware query.
 $info.BatteryPercent=-1;$info.IsCharging=$false;$info.LastUpdated=[DateTime]::MinValue
 [void]$sync.Invoke($f,$null)
 Assert ($info.BatteryPercent -eq 67 -and $info.LastUpdated -eq $sample) 'Stale scan overwrote telemetry or fabricated a sample time'
 # Never send the active wired endpoint battery to another receiver/mouse row.
 $info.HardwareFingerprint='HID:24AE:1412';$info.BatteryPercent=-1
 [void]$sync.Invoke($f,$null)
 Assert ($info.BatteryPercent -eq -1) 'Telemetry crossed physical transport identity'
 $timer=$t.GetField('rapooStatusUiTimer',$flags).GetValue($f)
 Assert ($timer.Enabled -and $timer.Interval -le 500) 'No independent bounded UI synchronization timer'
 # A report for another active row must not rerender the currently selected mouse.
 $info.HardwareFingerprint='HID:24AE:4612'
 $other=[Activator]::CreateInstance($a.GetType('RazerBatteryTray.MouseBatteryInfo'))
 $other.Brand='Rapoo';$other.DeviceId='Rapoo:24AE:1412';$other.PollingRate=1000
 $t.GetField('selectedDevice',$flags).SetValue($f,$other)
 $h.GetField('CachedPollingRate').SetValue($null,8000)
 $beforeLabel=$t.GetField('lblBatteryBig',$flags).GetValue($f).Text
 $frame[8]=68;[void]$h.GetMethod('ApplyStatusReport').Invoke($null,[object[]](,$frame))
 [void]$sync.Invoke($f,$null)
 Assert ($t.GetField('lblBatteryBig',$flags).GetValue($f).Text -eq $beforeLabel) 'Unrelated selected device was rerendered'
 Assert ($other.PollingRate -eq 1000) 'Another endpoint report rerendered and changed the selected device'
 Write-Host 'PASS: VT3 late report, stale scan, timestamp and physical isolation'
} finally {$f.Dispose()}