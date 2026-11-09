# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# Offline protocol replay. Assemblies load from bytes; this script never queries hardware.
param([string]$HostExe = (Join-Path $PSScriptRoot '../FerrisPulse.exe'))
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$assemblyPath = (Resolve-Path -LiteralPath $HostExe).Path
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($assemblyPath))
[Environment]::CurrentDirectory = [IO.Path]::GetDirectoryName($assemblyPath)
$drivers = @{}
foreach ($name in 'keychron_m6','logitech_mr0097') {
    $path = Join-Path $root ('packages/' + $name + '.fp-driver.cs')
    if (!(Test-Path -LiteralPath $path)) { throw "Missing standalone package: $name" }
    $source = [IO.File]::ReadAllText($path)
    $packageBytes=[IO.File]::ReadAllBytes($path)
    if ($packageBytes[0] -ne 0xEF -or $packageBytes[1] -ne 0xBB -or $packageBytes[2] -ne 0xBF) { throw 'Package must retain UTF-8 BOM' }
    if ($source -match '\b(?:LogitechDeviceHelper|KeychronDeviceHelper)\s*\.') { throw 'Package forwards to a host helper' }
    $argsList = [object[]]@([string]$source,[string]$path,$null)
    $loaded = $asm.GetType('RazerBatteryTray.CsScriptDriverLoader').GetMethod('CompileDriversFromSource').Invoke($null,$argsList)
    if ($argsList[2].Count -or $loaded.Count -ne 1) { throw ('Compile failed: ' + ($argsList[2] -join '; ')) }
    $drivers[$name] = $loaded[0]
    if (!$loaded[0].IsExternalPackage -or $loaded[0].Priority -ne 300) { throw 'Incorrect package priority' }
}
Write-Host 'PASS standalone package compilation'
$static = [Reflection.BindingFlags]'Static,Public,NonPublic'
function Call-Protocol($type,[string]$name,[object[]]$values) {
    $method = $type.GetMethod($name,$static)
    if (!$method) { throw "Missing protocol function: $name" }
    for ($i=0; $i -lt $values.Length; $i++) {
        if ($null -ne $values[$i]) { $values[$i]=$values[$i].PSObject.BaseObject }
    }
    return $method.Invoke($null,$values)
}
function Require([bool]$condition,[string]$message) { if (!$condition) { throw $message } }
function New-Endpoint([int]$vendorId,[int]$productId,[int]$page,[int]$usage,[int]$length) {
    $d = New-Object RazerBatteryTray.HidDeviceDescriptor
    $d.VendorId=$vendorId; $d.ProductId=$productId; $d.UsagePage=$page; $d.Usage=$usage
    $d.InputReportLength=$length; $d.OutputReportLength=$length; $d.FeatureReportLength=0
    $d.DevicePath='offline-test-endpoint'
    return $d
}
$kt = $drivers.keychron_m6.GetType().Assembly.GetType('FerrisPulse.Packages.KeychronM6.Protocol')
$lt = $drivers.logitech_mr0097.GetType().Assembly.GetType('FerrisPulse.Packages.LogitechMr0097.Protocol')
$pathA='\\?\hid#vid_046d&pid_c54d&mi_02&col02#7&1111111&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030}'
$pathB='\\?\hid#vid_046d&pid_c54d&mi_02&col01#7&1111111&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}'
Require (Call-Protocol $lt 'SameProtocolInterface' @($pathA,$pathB)) 'Sibling collections not recognized'
Require (!(Call-Protocol $lt 'SameProtocolInterface' @($pathA,$pathB.Replace('1111111','2222222')))) 'Different receiver claimed'
Require (!(Call-Protocol $lt 'SameProtocolInterface' @($pathA,$pathB.Replace('mi_02','mi_01')))) 'Different USB interface claimed'
Require (!(Call-Protocol $lt 'SameProtocolInterface' @('bad','bad'))) 'Malformed paths claimed'
$kd = New-Endpoint 0x3434 0xD026 0xFFC1 1 64
$ld = New-Endpoint 0x046D 0xC54D 0xFF00 2 20
foreach ($case in @(@($kt,$kd,0xD060),@($lt,$ld,0xC09B))) {
    $type=$case[0]; $endpoint=$case[1]
    Require (Call-Protocol $type 'IsControlEndpoint' @($endpoint)) 'Verified endpoint rejected'
    foreach ($change in @(@('VendorId',0x3554),@('ProductId',0x1234),@('UsagePage',1),@('Usage',6),@('InputReportLength',65),@('OutputReportLength',7),@('FeatureReportLength',64))) {
        $field=$endpoint.GetType().GetField($change[0]); $old=$field.GetValue($endpoint)
        $field.SetValue($endpoint,[int]$change[1])
        Require (!(Call-Protocol $type 'IsControlEndpoint' @($endpoint))) ('Unsafe endpoint accepted: '+$change[0])
        $field.SetValue($endpoint,$old)
    }
    $old=$endpoint.ProductId; $endpoint.ProductId=$case[2]
    Require (Call-Protocol $type 'IsControlEndpoint' @($endpoint)) 'Verified USB endpoint rejected'
    $endpoint.ProductId=$old
}
$identity=New-Object byte[] 64
$identity[0]=0xB6; $identity[1]=2; $identity[4]=0x34; $identity[5]=0x34; $identity[6]=0x60; $identity[7]=0xD0
Require (Call-Protocol $kt 'IsM6Identity' @(,$identity)) 'M6 identity rejected'
$m6Identity=$identity.Clone()
$identity[6]=0x26
Require (!(Call-Protocol $kt 'IsM6Identity' @(,$identity))) 'Receiver accepted as M6'
Require (!(Call-Protocol $kt 'IsM6Identity' @($null))) 'Null identity accepted'
$rx=New-Object byte[] 64; $rx[0]=0xB4; $rx[1]=6
foreach ($sample in @(@(0x56,86,$false),@(0xD6,86,$true),@(0x5C,92,$false),@(0,0,$false),@(0xE4,100,$true),@(0x65,-1,$false),@(0xFF,-1,$false))) {
    $rx[20]=$sample[0]; $argsList=[object[]]@($rx,-1,$false)
    $valid=Call-Protocol $kt 'TryParseBattery' $argsList
    Require ($valid -eq ($sample[1] -ge 0) -and $argsList[1] -eq $sample[1] -and $argsList[2] -eq $sample[2]) 'M6 status parse failed'
}
foreach ($bad in @([byte[]](0xB4,6),$null)) {
    $argsList=[object[]]@($bad,86,$true)
    Require (!(Call-Protocol $kt 'TryParseBattery' $argsList) -and $argsList[1] -eq -1 -and !$argsList[2]) 'Malformed M6 frame retained stale data'
}
$stamp=[DateTime]'2026-11-08T10:00:00'
$state=Call-Protocol $kt 'CreateState' @($kd,86,$true,$true,$stamp)
Require ($state.IsSleeping -and $state.IsChargingUnknown -and !$state.IsCharging -and $state.LastUpdated -eq $stamp -and $state.HardwareId -eq $kd.DevicePath) 'M6 cached freshness or endpoint identity changed'
Require (!$drivers.keychron_m6.CanHandle($state)) 'Logical M6 ID claimed without verified path identity'
Require (Call-Protocol $kt 'AcceptIdentity' @($kd,$m6Identity)) 'Verified M6 identity not accepted'
Require ($drivers.keychron_m6.CanHandle($state)) 'Verified M6 endpoint not routed'
$rf2=New-Endpoint 0x3434 0xD026 0xFFC1 1 64
$rf2.DevicePath='second-verified-m6-receiver'
$state2=Call-Protocol $kt 'CreateState' @($rf2,50,$false,$false,$stamp)
Require (!(Call-Protocol $kt 'ResolveReceiverLink' @('Keychron:ReceiverPath:second-verified-m6-receiver'))) 'Unverified receiver path linked'
Require ((Call-Protocol $kt 'ResolveReceiverLink' @('Keychron:3434:D026:MS')) -eq (Call-Protocol $kt 'PhysicalUnit' @($state))) 'Unambiguous legacy receiver link lost'
Require (Call-Protocol $kt 'AcceptIdentity' @($rf2,$m6Identity)) 'Second M6 identity not accepted'
$twoReceivers=New-Object 'System.Collections.Generic.List[RazerBatteryTray.MouseBatteryInfo]'
Call-Protocol $kt 'MergeTransportState' @($twoReceivers,$state)
Call-Protocol $kt 'MergeTransportState' @($twoReceivers,$state2)
Require ($twoReceivers.Count -eq 2) 'Distinct verified M6 receivers collapsed by logical ID'
Require (!(Call-Protocol $kt 'ResolveReceiverLink' @('Keychron:3434:D026:MS'))) 'Ambiguous legacy receiver link silently selected one mouse'
Require ((Call-Protocol $kt 'ResolveReceiverLink' @('Keychron:ReceiverPath:second-verified-m6-receiver')) -eq (Call-Protocol $kt 'PhysicalUnit' @($state2))) 'Explicit verified receiver path did not resolve'
$memory=$kt.GetField('lastReadings',$static).GetValue($null)
$memory[$kd.DevicePath]=$state
$cached=Call-Protocol $kt 'CachedState' @($kd)
Require ($cached.BatteryPercent -eq 86 -and $cached.LastUpdated -eq $stamp -and $cached.IsSleeping -and $cached.IsChargingUnknown -and !$cached.IsCharging) 'Same-path M6 cache not retained correctly'
$kd.DevicePath='unrelated-offline-test-endpoint'
Require (!(Call-Protocol $kt 'CachedState' @($kd))) 'M6 cache leaked to a different HID path'
$kd.DevicePath='offline-test-endpoint'
$memory.Clear()
$wiredEndpoint=New-Endpoint 0x3434 0xD060 0xFFC1 1 64
$wiredEndpoint.DevicePath='offline-test-wired-endpoint'
$wired=Call-Protocol $kt 'CreateState' @($wiredEndpoint,86,$true,$false,$stamp)
Require ($wired.DeviceId -eq 'Keychron:3434:D060:MS' -and !$wired.IsDonglePresent -and $wired.IsCharging) 'M6 wired identity/charging lost'
if ($wired.PSObject.Properties.Name -contains 'PhysicalUnitId') { Require (!$wired.PhysicalUnitId) 'Unconfirmed USB silently linked' }
Require (Call-Protocol $kt 'AcceptIdentity' @($wiredEndpoint,$m6Identity)) 'USB M6 identity not accepted'
foreach ($d in @($state,$wired)) {
    $drivers.keychron_m6.PopulateCapabilities($d)
    Require ($drivers.keychron_m6.CanHandle($d) -and $d.Capabilities.SupportsBattery -and !$d.Capabilities.SupportsDpi -and !$d.Capabilities.SupportsPollingRate) 'M6 capability/routing mismatch'
    Require (!$drivers.keychron_m6.SetDpi($d,800,800,1) -and !$drivers.keychron_m6.SetPollingRate($d,1000)) 'M6 unverified write enabled'
}
$li=New-Object byte[] 20; $li[0]=0x11; $li[1]=1; $li[2]=2; $li[3]=9
$li[11]=0x40; $li[12]=0xA9; $li[13]=0xC0; $li[14]=0x9B
Require (Call-Protocol $lt 'IsModelIdentity' @($li,[byte]1,[byte]2)) 'MR0097 identity rejected'
foreach ($index in 0,1,2,3,11,12,13,14,15,16) {
    $old=$li[$index]; $li[$index]=[byte]($old -bxor 1)
    Require (!(Call-Protocol $lt 'IsModelIdentity' @($li,[byte]1,[byte]2))) ('Incorrect MR0097 identity accepted at '+$index)
    $li[$index]=$old
}
foreach ($sample in @(@('11 01 06 19 59 08 00 00',1,89,$false),@('11 FF 06 19 3C 04 01 01',255,60,$true),@('11 01 06 19 00 01 00',1,0,$false),@('11 01 06 19 65 04 00',1,-1,$false),@('11 02 06 19 3E 04 00',1,-1,$false),@('11 01 FF 06 19 05 00',1,-1,$false),@('11 01 06 19 3E',1,-1,$false))) {
    $frame=[byte[]]($sample[0].Split(' ') | ForEach-Object {[Convert]::ToByte($_,16)})
    $argsList=[object[]]@($frame,[byte]$sample[1],[byte]6,-1,$false)
    $valid=Call-Protocol $lt 'TryReadUnifiedBattery' $argsList
    Require ($valid -eq ($sample[2] -ge 0) -and $argsList[3] -eq $sample[2] -and $argsList[4] -eq $sample[3]) 'MR0097 battery parse failed'
}
$dpi=[byte[]](17,1,9,89,0,6,64,3,32,6,114,3,32,2)
$argsList=[object[]]@($dpi,[byte]1,[byte]9,-1,-1)
Call-Protocol $lt 'ReadExtendedDpi' $argsList
Require ($argsList[3] -eq 1600 -and $argsList[4] -eq 1650) 'MR0097 independent current Y parsed as default X'
$dpi[1]=2
Call-Protocol $lt 'ReadExtendedDpi' $argsList
Require ($argsList[3] -eq -1 -and $argsList[4] -eq -1) 'Wrong-slot DPI accepted'
$profileBytes=[byte[]](3,3,0,0,32,3,32,3,2,176,4,176,4,2,64,6,64,6,2,96,9,96,9,2,128,12,128,12,2,0,0,0)
$profile=Call-Protocol $lt 'DecodeOnboardDpiProfile' @($profileBytes,2,[byte]13)
Require (($profile.X -join ',') -eq '800,1200,1600,2400,3200' -and ($profile.Y -join ',') -eq '800,1200,1600,2400,3200' -and $profile.Stage -eq 3) 'MR0097 onboard profile incorrect'
Require (!(Call-Protocol $lt 'DecodeOnboardDpiProfile' @($profileBytes,5,[byte]13))) 'Invalid onboard stage accepted'
for ($code=0; $code -lt 7; $code++) {
    Require ((Call-Protocol $lt 'ReadReportRate' @([byte[]](17,1,10,41,$code),[byte]1,[byte]10)) -eq (125 -shl $code)) 'MR0097 report rate parse failed'
}
Require ((Call-Protocol $lt 'ReadReportRate' @([byte[]](17,1,10,41,7),[byte]1,[byte]10)) -eq 0) 'Invalid rate code accepted'
$dev=New-Object RazerBatteryTray.MouseBatteryInfo
$dev.DeviceId='Logitech:046D:C54D:1'; $dev.Brand='Logitech'; $dev.Category='Mouse'
Require (!$drivers.logitech_mr0097.CanHandle($dev)) 'Unverified paired model claimed'
$dev.DriverId=$drivers.logitech_mr0097.DriverId
Require (!$drivers.logitech_mr0097.CanHandle($dev)) 'Package DriverId alone claimed an unverified model'
$dev.DeviceName='Logitech G PRO X SUPERLIGHT 2'; $dev.HardwareId=$ld.DevicePath
Require (!$drivers.logitech_mr0097.CanHandle($dev)) 'Model name and logical ID alone claimed an unverified path'
Require (Call-Protocol $lt 'AcceptIdentity' @($ld,[byte]1,[byte]2,$li)) 'Verified MR0097 identity not accepted'
Require ($drivers.logitech_mr0097.CanHandle($dev)) 'Verified package path state not routed'
$dev.HardwareId='different-unverified-logitech-path'
Require (!$drivers.logitech_mr0097.CanHandle($dev)) 'Verified model identity leaked to another path'
$dev.HardwareId=$ld.DevicePath
$drivers.logitech_mr0097.PopulateCapabilities($dev)
Require (!$dev.Capabilities.SupportsDpi -and !$dev.Capabilities.SupportsPollingRate -and $dev.Capabilities.SupportsHardwareDpiMonitor) 'Unverified arbitrary write capability enabled'
[RazerBatteryTray.HardwareDriverRegistry]::RegisterDriver($drivers.logitech_mr0097)
[RazerBatteryTray.HardwareDriverRegistry]::RegisterDriver($drivers.keychron_m6)
Require ([RazerBatteryTray.HardwareDriverRegistry]::ResolveDriver($dev).DriverId -eq $drivers.logitech_mr0097.DriverId) 'MR0097 external driver routing failed'
Require ([RazerBatteryTray.HardwareDriverRegistry]::ResolveDriver($wired).DriverId -eq $drivers.keychron_m6.DriverId) 'M6 external driver routing failed'
Require (!$drivers.logitech_mr0097.SetDpi($dev,1234,1234,3) -and !$drivers.logitech_mr0097.SetPollingRate($dev,8000) -and !$drivers.logitech_mr0097.UpdateHardwareStages($dev,1,[int[]](800,1600),[int[]](800,1600))) 'Unsupported write accepted without verified live endpoint'
$logitechState=Call-Protocol $lt 'CreateState' @($ld,[byte]1,89,$true,$false,$stamp,'Logitech:40A9:Unit:01-02-03-04')
$logitechMemory=$lt.GetField('last',$static).GetValue($null)
$logitechMemory[('External:LogitechMR0097:'+$ld.DevicePath+':1')]=$logitechState
$cached=Call-Protocol $lt 'CachedState' @($ld,[byte]1)
Require ($cached.BatteryPercent -eq 89 -and $cached.LastUpdated -eq $stamp -and $cached.IsSleeping -and $cached.IsChargingUnknown -and !$cached.IsCharging) 'MR0097 cache freshness changed'
Require (!(Call-Protocol $lt 'CachedState' @($ld,[byte]2))) 'MR0097 cache leaked to a different receiver slot'
$logitechMemory.Clear()
$dev.DeviceId='Logitech:046D:C539:1'
Require (!$drivers.logitech_mr0097.CanHandle($dev)) 'Different Logitech receiver claimed'
foreach ($case in @(@($drivers.keychron_m6,0x3434,0xD026,0xD060),@($drivers.logitech_mr0097,0x046D,0xC54D,0xC09B))) {
    Require ($case[0].MatchesVidPid($case[1],$case[2]) -and $case[0].MatchesVidPid($case[1],$case[3]) -and !$case[0].MatchesVidPid($case[1],0x1234)) 'Package VID/PID coverage too broad'
}
# Two same-model receivers have the same legacy DeviceId but distinct performance endpoints.
$ld2=New-Endpoint 0x046D 0xC54D 0xFF00 2 20
$ld2.DevicePath='second-logitech-receiver'
Require (Call-Protocol $lt 'AcceptIdentity' @($ld2,[byte]1,[byte]2,$li)) 'Second Logitech identity rejected'
$logitechState2=Call-Protocol $lt 'CreateState' @($ld2,[byte]1,80,$false,$false,$stamp,'Logitech:40A9:Unit:05-06-07-08')
Call-Protocol $lt 'RememberActiveEndpoint' @($ld,[byte]1,[byte]9,$profile)
Call-Protocol $lt 'RememberActiveEndpoint' @($ld2,[byte]1,[byte]9,$profile)
$endpointFlags=[Reflection.BindingFlags]'Instance,Public,NonPublic'
foreach ($case in @(@($logitechState,$ld.DevicePath),@($logitechState2,$ld2.DevicePath))) {
    $endpoint=Call-Protocol $lt 'FindEndpoint' @($case[0])
    Require ($null -ne $endpoint) 'One C54D receiver replaced the other active endpoint'
    $descriptor=$endpoint.GetType().GetField('Descriptor',$endpointFlags).GetValue($endpoint)
    Require ($descriptor.DevicePath -eq $case[1]) 'Performance route points to a different receiver'
}
$logitechState2.HardwareId='unseen-logitech-receiver'
Require (!(Call-Protocol $lt 'FindEndpoint' @($logitechState2))) 'Unknown receiver path borrowed an active endpoint'
$logitechState2.HardwareId=$ld2.DevicePath
# Replay the read loop with a deterministic elapsed clock; no wall-clock sleep or HID calls.
$ordinaryBudget=$lt.GetField('CommandReplyTimeoutMs',$static)
$identityBudget=$lt.GetField('IdentityRootReplyTimeoutMs',$static)
Require ($null -ne $ordinaryBudget -and $null -ne $identityBudget) 'Missing bounded command timeouts'
Require ($ordinaryBudget.GetRawConstantValue() -eq 500 -and $identityBudget.GetRawConstantValue() -eq 2000) 'Cold identity budget changed ordinary command timeout'
function Replay-Reply([int]$budget,[object[]]$events) {
    $script:replyElapsed=[long]0; $script:replyIndex=0; $script:replyEvents=$events
    $script:replyTimeouts=New-Object 'System.Collections.Generic.List[int]'
    $reader=[Func[int,byte[]]] {
        param([int]$remaining)
        $script:replyTimeouts.Add($remaining)
        if ($script:replyIndex -ge $script:replyEvents.Count) { return $null }
        $event=$script:replyEvents[$script:replyIndex]; $script:replyIndex++
        if ($event.Delay -ge $remaining) { $script:replyElapsed += $remaining; return $null }
        $script:replyElapsed += $event.Delay
        return $event.Frame
    }
    $elapsed=[Func[long]] { return $script:replyElapsed }
    $tx=New-Object byte[] 20; $tx[0]=0x11; $tx[1]=1; $tx[3]=9; $tx[5]=3
    $reply=Call-Protocol $lt 'ReceiveReply' @($tx,$budget,$reader,$elapsed)
    return [pscustomobject]@{ Reply=$reply; Elapsed=$script:replyElapsed; Reads=$script:replyIndex; Budgets=$script:replyTimeouts.ToArray() }
}
$rootReply=New-Object byte[] 20; $rootReply[0]=0x11; $rootReply[1]=1; $rootReply[3]=9; $rootReply[4]=2
$cold=Replay-Reply 2000 @([pscustomobject]@{Delay=1059;Frame=$rootReply})
Require ($null -ne $cold.Reply -and $cold.Elapsed -eq 1059 -and $cold.Reads -eq 1) 'Measured 1059ms cold identity reply rejected'
$ordinary=Replay-Reply 500 @([pscustomobject]@{Delay=1059;Frame=$rootReply})
Require ($null -eq $ordinary.Reply -and $ordinary.Elapsed -eq 500) 'Ordinary command exceeded its original deadline'
$late=Replay-Reply 2000 @([pscustomobject]@{Delay=2001;Frame=$rootReply})
Require ($null -eq $late.Reply -and $late.Elapsed -eq 2000) 'Identity read exceeded bounded cold-start budget'
$wrongSlot=$rootReply.Clone(); $wrongSlot[1]=2
$wrongFunction=$rootReply.Clone(); $wrongFunction[3]=0x19
$filtered=Replay-Reply 2000 @([pscustomobject]@{Delay=500;Frame=$wrongSlot},[pscustomobject]@{Delay=500;Frame=$wrongFunction},[pscustomobject]@{Delay=59;Frame=$rootReply})
Require ($null -ne $filtered.Reply -and $filtered.Elapsed -eq 1059 -and ($filtered.Budgets -join ',') -eq '2000,1500,1000') 'Unrelated traffic reset deadline or escaped filtering'
$errorReply=$rootReply.Clone(); $errorReply[2]=0xFF; $errorReply[3]=0; $errorReply[4]=9; $errorReply[5]=2
$errorResult=Replay-Reply 2000 @([pscustomobject]@{Delay=10;Frame=$errorReply},[pscustomobject]@{Delay=10;Frame=$rootReply})
Require ($null -eq $errorResult.Reply -and $errorResult.Reads -eq 1) 'Matching HID++ error retried or accepted as identity'
$short=Replay-Reply 2000 @([pscustomobject]@{Delay=10;Frame=[byte[]](17,1,0,9)},[pscustomobject]@{Delay=10;Frame=$rootReply})
Require ($null -ne $short.Reply -and $short.Reads -eq 2) 'Truncated response was accepted or broke filtering'
# Exercise actual identity orchestration: the cold root uses 2000ms, then identity info uses 500ms.
$script:identityBudgets=New-Object 'System.Collections.Generic.List[int]'
$script:identityInfo=$li.Clone()
$identityExchange=[Func[byte,int,byte[]]] {
    param([byte]$feature,[int]$timeoutMs)
    $script:identityBudgets.Add($timeoutMs)
    if ($feature -eq 0) {
        $delayed=Replay-Reply $timeoutMs @([pscustomobject]@{Delay=1059;Frame=$rootReply})
        return $delayed.Reply
    }
    if ($feature -ne 2) { throw 'Identity information feature changed' }
    return $script:identityInfo
}
$identityArgs=[object[]]@([byte]1,$false,$identityExchange)
$identified=Call-Protocol $lt 'ReadIdentityCore' $identityArgs
Require ($null -ne $identified -and !$identityArgs[1] -and ($script:identityBudgets -join ',') -eq '2000,500') 'ReadIdentity did not apply the cold-only budget'
$script:identityInfo[12]=0xB5
$identityArgs=[object[]]@([byte]1,$false,$identityExchange)
Require (!(Call-Protocol $lt 'ReadIdentityCore' $identityArgs) -and $identityArgs[1]) 'Cold query bypassed exact paired-model validation'
$wrongM6=$m6Identity.Clone(); $wrongM6[6]=0x26
Require (!(Call-Protocol $kt 'AcceptIdentity' @($kd,$wrongM6)) -and !$drivers.keychron_m6.CanHandle($state)) 'Changed paired identity retained M6 path ownership'
$drivers.keychron_m6.PopulateCapabilities($state)
Require (!$state.Capabilities.SupportsBattery -and !$state.Capabilities.SupportsDpi) 'Unverified M6 state retained capabilities'
$wrongLogitech=$li.Clone(); $wrongLogitech[12]=0xB5
Require (!(Call-Protocol $lt 'AcceptIdentity' @($ld,[byte]1,[byte]2,$wrongLogitech)) -and !$drivers.logitech_mr0097.CanHandle($logitechState)) 'Changed paired identity retained MR0097 path ownership'
$drivers.logitech_mr0097.PopulateCapabilities($logitechState)
Require (!$logitechState.Capabilities.SupportsBattery -and !$logitechState.Capabilities.SupportsDpiStages -and !$logitechState.Capabilities.SupportsHardwareDpiMonitor) 'Unverified MR0097 state retained capabilities'
Write-Host 'PASS endpoint isolation, real protocol frames, cache freshness, capability limits and unsupported writes'
