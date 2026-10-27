# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# UTF-8 BOM; byte-array assembly loading; no live hardware or registry writes.
$ErrorActionPreference='Stop'
$a=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '..\FerrisPulse.exe')))
$t=$a.GetType('RazerBatteryTray.RapooDeviceHelper')
function Assert($ok,$message) { if (!$ok) { throw $message } }
$apply=$t.GetMethod('ApplyStatusReport')
Assert ($null -ne $apply) 'Missing live VT3 status application'
function Field($name) { return $t.GetField($name) }
function Apply([byte[]]$data) { return $apply.Invoke($null,[object[]](,$data)) }
Assert ((Field 'CachedBatteryPercent').GetValue($null) -eq -1) 'Initial battery must be unknown'
Assert (Apply ([byte[]]@(7,0x20,3,0x40,6,0x40,6,2,73,0))) 'Status update rejected'
Assert ((Field 'CachedBatteryPercent').GetValue($null) -eq 73) 'Live battery not stored'
Assert ((Field 'CachedIsCharging').GetValue($null)) 'Live charging flag not stored'
Assert ((Field 'CachedDpi').GetValue($null) -eq 1600 -and (Field 'CachedDpiY').GetValue($null) -eq 1600) 'Live XY DPI not stored'
Assert ((Field 'CachedDpiStage').GetValue($null) -eq 4) '1600 DPI / zero-based stage index mapped incorrectly'
$sampleTime=(Field 'CachedLastUpdated').GetValue($null)
Assert ($sampleTime -ne [DateTime]::MinValue) 'Battery timestamp missing'
Assert (Apply ([byte[]]@(7,0x20,3,0x40,6,0x40,6,0,255,0))) 'Valid frame with unknown battery should still update DPI/charging'
Assert ((Field 'CachedBatteryPercent').GetValue($null) -eq 73) 'Invalid battery erased valid cache'
Assert ((Field 'CachedLastUpdated').GetValue($null) -eq $sampleTime) 'Invalid battery refreshed sampling time'
Assert (!(Field 'CachedIsCharging').GetValue($null)) 'Charging flag not cleared'
foreach ($bat in @(0,1,100)) {
    [void](Apply ([byte[]]@(7,0x10,1,0x20,3,0x20,3,0,$bat,0)))
    Assert ((Field 'CachedBatteryPercent').GetValue($null) -eq $bat) 'Low/full battery replaced by cache or 100%'
}
$sixth=Apply ([byte[]]@(7,0x10,5,0x00,25,0x00,25,0,100,0))
Assert ($sixth -and (Field 'CachedDpiStage').GetValue($null) -eq 6) 'Sixth stage live report was not retained'
$snapshot=$t.GetMethod('BuildRapooDeviceInfo',[Reflection.BindingFlags]'NonPublic,Static')
Assert ($null -ne $snapshot) 'Missing normalized device snapshot'
$info=$snapshot.Invoke($null,@([int]0x1412,$true,$false))
Assert ($info.Category.ToString() -eq 'Mouse' -and $info.Transport -eq '2.4G 无线') 'VT3 classification or transport wrong'
Assert ($info.BatteryPercent -eq 100 -and !$info.IsCharging) 'Snapshot lost status values'
$time=$info.LastUpdated
$info=$snapshot.Invoke($null,@([int]0x4612,$true,$true))
Assert ($info.Transport -eq 'USB 有线' -and !$info.IsCharging -and $info.LastUpdated -eq $time) 'USB or control polling must not invent charging/fresh battery'
$reset=$t.GetMethod('ResetRapooLiveState',[Reflection.BindingFlags]'NonPublic,Static')
Assert ($null -ne $reset) 'Missing physical-device cache reset'
[void]$reset.Invoke($null,$null)
Assert ((Field 'CachedBatteryPercent').GetValue($null) -eq -1) 'New physical device inherited battery'
Assert ((Field 'CachedLastUpdated').GetValue($null) -eq [DateTime]::MinValue) 'New physical device inherited battery time'
$info=$snapshot.Invoke($null,@([int]0x1412,$false,$false))
Assert ($info.BatteryPercent -eq -1 -and $info.IsSleeping -and !$info.IsCharging) 'Unknown sleeping battery fabricated'
Write-Host 'PASS: VT3 live battery/charging/XY state, timestamps, unknown battery and physical reset'