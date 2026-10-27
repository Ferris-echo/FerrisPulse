# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# Pure replay tests: no hardware writes, no user registry changes, no EXE file locks.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot\..").Path
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'FerrisPulse.exe')))
$t = $asm.GetType('RazerBatteryTray.AsusDeviceHelper')
function Assert($ok, $why) { if (-not $ok) { throw "FAIL: $why" } }
Assert ($null -ne $t) 'AsusDeviceHelper missing'
$flags = [Reflection.BindingFlags]'Public,NonPublic,Static'
function Invoke-Parser($method, [byte[]]$rx) {
    $argsArray = [object[]]@($rx, -1)
    $ok = $t.GetMethod($method, $flags).Invoke($null, $argsArray)
    return @($ok, $argsArray[1])
}
$sample = New-Object byte[] 64
[byte[]]$head = 0xCC,0x12,7,0,0,5,0x27,0x14,1
[Array]::Copy($head,$sample,$head.Length)
$r = Invoke-Parser 'TryParseBatteryReport' $sample
Assert ($r[0] -and $r[1] -eq 39) 'real PELTA replay must read 39 percent'
foreach ($value in @(0,100)) {
    $sample[6] = $value
    $r = Invoke-Parser 'TryParseBatteryReport' $sample
    Assert ($r[0] -and $r[1] -eq $value) '0 and 100 are valid battery values'
}
$sample[6] = 101
Assert (-not (Invoke-Parser 'TryParseBatteryReport' $sample)[0]) 'reject percent >100'
$sample[5]=255; $sample[6]=170
Assert (-not (Invoke-Parser 'TryParseBatteryReport' $sample)[0]) 'reject payload NAK'
foreach ($rx in @([byte[]]@(0xCC,0x12,7), [byte[]]@(0xCC,0x12,8,0,0,0,39), [byte[]]@(0xCC,0xFF,0xAA,0,0,0,39), [byte[]]@(0,0x12,7,0,0,0,39), [byte[]]@(0xCC,0x12,7,1,0,0,39))) {
    Assert (-not (Invoke-Parser 'TryParseBatteryReport' $rx)[0]) 'reject unrelated, truncated, malformed replies'
}
# Keep all 64 bytes, corrupt one header field at a time so length cannot mask matching bugs.
$sample[5]=5; $sample[6]=39
foreach ($field in @(0,1,2,3,4)) {
    $corrupt=[byte[]]$sample.Clone()
    $corrupt[$field]=$corrupt[$field] -bxor 1
    Assert (-not (Invoke-Parser 'TryParseBatteryReport' $corrupt)[0]) "reject full-length invalid header at offset $field"
}
$nak=[byte[]]$sample.Clone(); $nak[1]=255; $nak[2]=170
Assert (-not (Invoke-Parser 'TryParseBatteryReport' $nak)[0]) 'reject full-length header NAK'
Assert (-not (Invoke-Parser 'TryParseBatteryReport' $null)[0]) 'reject null'
foreach ($value in @(0,1)) {
    $rx = New-Object byte[] 64; $rx[0]=0xCC; $rx[1]=0x12; $rx[2]=8; $rx[5]=$value
    $r = Invoke-Parser 'TryParseChargingReport' $rx
    Assert ($r[0] -and $r[1] -eq $value) 'charging decoded from offset 5'
}
$rx[5]=2
Assert (-not (Invoke-Parser 'TryParseChargingReport' $rx)[0]) 'unknown charging code is not discharging'
$descType = $asm.GetType('RazerBatteryTray.HidDeviceDescriptor')
$desc = [Activator]::CreateInstance($descType)
$desc.VendorId=0x0B05; $desc.ProductId=0x1B84; $desc.UsagePage=0xFF00; $desc.Usage=1
$desc.InputReportLength=64; $desc.OutputReportLength=64
Assert ($t.GetMethod('IsSupportedEndpoint',$flags).Invoke($null,@($desc))) 'PE LTA vendor channel allowed'
$desc.UsagePage=0xFF07
Assert (-not $t.GetMethod('IsSupportedEndpoint',$flags).Invoke($null,@($desc))) 'no FF07 feature channel'
$desc.UsagePage=0xFF00; $desc.ProductId=0x1B82
Assert (-not $t.GetMethod('IsSupportedEndpoint',$flags).Invoke($null,@($desc))) 'untested PID not enabled'
$gaming=$asm.GetType('RazerBatteryTray.UniversalGamingDeviceHelper')
foreach ($pidText in @('1b84','1ab5')) {
    Assert (-not $gaming.GetMethod('IsSupportedGamingDevice').Invoke($null,@(0x0B05,"hid#vid_0b05&pid_$pidText",'ROG'))) 'known ASUS non-mouse excluded'
}
$dm=$asm.GetType('RazerBatteryTray.DeviceManager')
$variants=$dm.GetMethod('GetDeviceIdVariants').Invoke($null,@('Gaming:0B05:1B84'))
Assert ($variants.Contains('ASUS:0B05:1B84')) 'custom names / hidden aliases include ASUS'
$build=$t.GetMethod('BuildDeviceInfo',$flags)
$desc.ProductId=0x1B84
$info=$build.Invoke($null,@($desc,39,$false,$true,[DateTime]::UtcNow))
Assert ($info.Category.ToString() -eq 'Headset' -and $info.BatteryPercent -eq 39 -and $info.Brand -eq 'ASUS') 'headset model contract'
Assert ($info.Dpi -eq 0 -and $info.PollingRate -eq 0 -and $info.DisplayName -match 'PELTA') 'headset has no mouse features'
$unknown=$build.Invoke($null,@($desc,-1,$false,$false,[DateTime]::MinValue))
Assert ($unknown.BatteryPercent -eq -1 -and -not $unknown.IsConnected) 'unknown battery is never fabricated'
$desc.ProductId=0x1AB5
$info=$build.Invoke($null,@($desc,-1,$false,$true,[DateTime]::UtcNow))
Assert ($info.Category.ToString() -eq 'Keyboard' -and $info.BatteryPercent -eq -1 -and $info.Transport -eq 'USB 有线') 'wired keyboard without fake battery'
# Opening a vanished path must preserve only the last real sample, and clear live charging.
$stateType=$t.GetNestedType('DeviceState',[Reflection.BindingFlags]'NonPublic')
$state=[Activator]::CreateInstance($stateType,$true)
$stamp=[DateTime]::Parse('2026-10-24T09:00:00')
$state.Battery=39; $state.Charging=$true; $state.Ready=$true; $state.LastUpdated=$stamp
$desc.DevicePath='\\?\FerrisPulse-Nonexistent-Test-Device'
$t.GetMethod('ReadState',$flags).Invoke($null,@($desc,$state)) | Out-Null
Assert ($state.Battery -eq 39 -and $state.LastUpdated -eq $stamp) 'failed query preserves last real sample and timestamp'
Assert (-not $state.Ready -and -not $state.Charging) 'failed query never claims live charging'
$desc.ProductId=0x1B84
$sleep=$build.Invoke($null,@($desc,39,$false,$false,$stamp))
Assert ($sleep.IsSleeping -and $sleep.BatteryPercent -eq 39 -and -not $sleep.IsCharging) 'standby retains real battery'
Write-Host 'PASS: ASUS PELTA replay, bounds, NAK, endpoint isolation, categories and aliases'