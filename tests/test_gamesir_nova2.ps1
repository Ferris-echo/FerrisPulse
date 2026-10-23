# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
$ErrorActionPreference='Stop'
$a=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '..\FerrisPulse.exe')))
$t=$a.GetType('RazerBatteryTray.GameSirDeviceHelper')
if (!$t) { throw 'Missing Nova2 adapter' }
function Assert($ok,$message){ if (!$ok){throw $message} }
$q=$t.GetMethod('BuildInfoQuery').Invoke($null,$null)
Assert ($q.Length -eq 65 -and $q[0] -eq 0 -and $q[1] -eq 1 -and $q[2] -eq 1) 'Wrong read-only query'
[byte[]]$frame=@(0)*65;$frame[1]=1;$frame[2]=0xA9;$frame[3]=0x63;$frame[4]=0x45;$frame[5]=0xD8;$frame[6]=0xCE;$frame[7]=1;$frame[19]=87
$m=$t.GetMethod('CreateDeviceInfo')
$wireless=$m.Invoke($null,@($frame,0x1098,$null))
Assert ($wireless.BatteryPercent -eq 87 -and !$wireless.IsCharging -and $wireless.Category.ToString() -eq 'Gamepad') 'Wireless capture parsed incorrectly'
$frame[19]=255
$wired=$m.Invoke($null,@($frame,0x100F,$wireless))
Assert ($wired.IsCharging -and $wired.BatteryPercent -eq 87 -and $wired.LastUpdated -eq $wireless.LastUpdated) 'Charging sentinel fabricated full battery or timestamp'
Assert ($wired.DeviceId -eq $wireless.DeviceId -and $wired.Transport -eq 'USB 有线') 'USB/wireless identity duplicated'
$first=$m.Invoke($null,@($frame,0x100F,$null))
Assert ($first.IsCharging -and $first.BatteryPercent -eq -1) 'First charging sample must have unknown percent'
foreach($b in @(0,1,100)){$frame[19]=$b;$state=$m.Invoke($null,@($frame,0x100F,$null));Assert ($state.BatteryPercent -eq $b -and !$state.IsCharging) 'Boundary battery lost'}
$frame[1]=0xF4;Assert ($null -eq $m.Invoke($null,@($frame,0x100F,$null))) 'Gamepad input misread as device information'
$frame[1]=1;$frame[19]=101;Assert ($null -eq $m.Invoke($null,@($frame,0x100F,$null))) 'Invalid battery accepted'
$frame[19]=87;Assert ($null -eq $m.Invoke($null,@($frame,0x2106,$null))) 'Unverified product accepted'
Assert ($null -eq $m.Invoke($null,@([byte[]]@(0,1),0x100F,$null))) 'Truncated frame accepted'
$frame[19]=255;$other=$m.Invoke($null,@($frame,0x100F,$null));$frame[2]=0xAA
Assert ($m.Invoke($null,@($frame,0x100F,$wireless)).BatteryPercent -eq -1) 'Different controller inherited battery'
Write-Host 'PASS: Nova2 live samples, charging sentinel, stable identity, boundaries and malformed frames'

$resolve=$t.GetMethod('FindConfiguredDevice')
Assert ($null -ne $resolve) 'Missing unambiguous custom record resolution'
$cfg=New-Object ($a.GetType('RazerBatteryTray.CustomDeviceConfig'))
$cfg.DeviceId='HID:3537:1098';$cfg.HardwareFingerprint='HID:3537:1098'
$items=New-Object ('System.Collections.Generic.List['+$a.GetType('RazerBatteryTray.MouseBatteryInfo').FullName+']')
$items.Add($wireless)
$frame[19]=87;$second=$m.Invoke($null,@($frame,0x1098,$null));$items.Add($second)
Assert ($null -eq $resolve.Invoke($null,@($cfg.PSObject.BaseObject,$items.PSObject.BaseObject))) 'Ambiguous fingerprint renamed first controller'
$cfg.DeviceId=$second.DeviceId
Assert ($resolve.Invoke($null,@($cfg.PSObject.BaseObject,$items.PSObject.BaseObject)).DeviceId -eq $second.DeviceId) 'Exact identity did not win'
$cfg.DeviceId='GameSir:Nova2Lite:FFFFFFFFFFFF'
Assert ($null -eq $resolve.Invoke($null,@($cfg.PSObject.BaseObject,$items.PSObject.BaseObject))) 'Absent exact identity was assigned to another controller'
$cfg.DeviceId='HID:3537:1098';$items.Remove($second)|Out-Null
Assert ($resolve.Invoke($null,@($cfg.PSObject.BaseObject,$items.PSObject.BaseObject)).DeviceId -eq $wireless.DeviceId) 'Unique legacy fingerprint did not merge'
Write-Host 'PASS: GameSir custom record identity and ambiguous multi-controller matching'

# 旧通用记录 HID:3537:1098 的隐藏标记不得连带藏掉 MAC 身份的新品牌卡片
$dm=$a.GetType('RazerBatteryTray.DeviceManager')
$hide=$dm.GetMethod('ShouldHideDevice')
Assert ($null -ne $hide) 'Missing shared-fingerprint hide policy'
Assert (!$hide.Invoke($null,@($wireless,$false,$true))) 'Legacy generic fingerprint still suppresses MAC-identified controller'
Assert ($hide.Invoke($null,@($wireless,$true,$true))) 'Explicit MAC hide was ignored'
$generic=[Activator]::CreateInstance($a.GetType('RazerBatteryTray.MouseBatteryInfo'))
$generic.Brand='Custom';$generic.DeviceId='HID:3537:1098';$generic.HardwareFingerprint='HID:3537:1098'
Assert ($hide.Invoke($null,@($generic,$false,$true))) 'Generic device lost fingerprint-based hiding'
Assert (!$hide.Invoke($null,@($generic,$false,$false))) 'Unhidden generic device was filtered'
Write-Host 'PASS: legacy HID:3537 fingerprint no longer hides the MAC-identified Nova2'