# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# Offline MR0097 protocol replay & built-in driver routing test. Zero hardware I/O.
$ErrorActionPreference = 'Stop'
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Resolve-Path "$PSScriptRoot/../FerrisPulse.exe")))
[Environment]::CurrentDirectory = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))

[RazerBatteryTray.BuiltInDrivers]::EnsureRegistered()
$drivers = @([RazerBatteryTray.HardwareDriverRegistry]::GetAllDriversOrdered())
$driver = @($drivers | Where-Object DriverId -eq 'BuiltIn:LogitechMR0097')[0]
if ($null -eq $driver) { throw 'Missing BuiltIn:LogitechMR0097 driver' }
if ($driver.IsExternalPackage -or $driver.Priority -ne 100) { throw 'Incorrect MR0097 built-in metadata' }

$static = [Reflection.BindingFlags]'Static,Public,NonPublic'
$type = $asm.GetType('RazerBatteryTray.LogitechMr0097Helper')
if ($null -eq $type) { throw 'Missing LogitechMr0097Helper' }

function Call-Protocol([string]$name, [object[]]$values) {
    $method = $type.GetMethod($name, $static)
    if ($null -eq $method) { throw "Missing protocol function: $name" }
    for ($i = 0; $i -lt $values.Length; $i++) {
        if ($null -ne $values[$i]) { $values[$i] = $values[$i].PSObject.BaseObject }
    }
    return $method.Invoke($null, $values)
}
function Require([bool]$condition, [string]$message) { if (!$condition) { throw $message } }
function New-Endpoint([int]$vendorId, [int]$productId, [int]$page, [int]$usage, [int]$length) {
    $d = New-Object RazerBatteryTray.HidDeviceDescriptor
    $d.VendorId = $vendorId; $d.ProductId = $productId; $d.UsagePage = $page; $d.Usage = $usage
    $d.InputReportLength = $length; $d.OutputReportLength = $length; $d.FeatureReportLength = 0
    $d.DevicePath = 'offline-mr0097-endpoint'
    return $d
}

# 1. Endpoint and interface ancestry checks
$pathA = '\\?\hid#vid_046d&pid_c54d&mi_02&col02#7&1111111&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030}'
$pathB = '\\?\hid#vid_046d&pid_c54d&mi_02&col01#7&1111111&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}'
Require (Call-Protocol 'SameProtocolInterface' @($pathA, $pathB)) 'Sibling collections not recognized'
Require (!(Call-Protocol 'SameProtocolInterface' @($pathA, $pathB.Replace('1111111', '2222222')))) 'Different receiver claimed'
Require (!(Call-Protocol 'SameProtocolInterface' @($pathA, $pathB.Replace('mi_02', 'mi_01')))) 'Different USB interface claimed'
Require (!(Call-Protocol 'SameProtocolInterface' @('bad', 'bad'))) 'Malformed paths claimed'

$ld = New-Endpoint 0x046D 0xC54D 0xFF00 2 20
Require (Call-Protocol 'IsControlEndpoint' @($ld)) 'Verified MR0097 endpoint rejected'
foreach ($change in @(@('VendorId', 0x3554), @('ProductId', 0x1234), @('UsagePage', 1), @('Usage', 6), @('InputReportLength', 65), @('OutputReportLength', 7), @('FeatureReportLength', 64))) {
    $field = $ld.GetType().GetField($change[0]); $old = $field.GetValue($ld)
    $field.SetValue($ld, [int]$change[1])
    Require (!(Call-Protocol 'IsControlEndpoint' @($ld))) ('Unsafe endpoint accepted: ' + $change[0])
    $field.SetValue($ld, $old)
}
$ld.ProductId = 0xC09B
Require (Call-Protocol 'IsControlEndpoint' @($ld)) 'Verified MR0097 USB endpoint rejected'
$ld.ProductId = 0xC54D

# 2. Identity & real protocol frame replay
$li = New-Object byte[] 20; $li[0] = 0x11; $li[1] = 1; $li[2] = 2; $li[3] = 9
$li[11] = 0x40; $li[12] = 0xA9; $li[13] = 0xC0; $li[14] = 0x9B
Require (Call-Protocol 'IsModelIdentity' @($li, [byte]1, [byte]2)) 'MR0097 identity rejected'
foreach ($index in 0, 1, 2, 3, 11, 12, 13, 14, 15, 16) {
    $old = $li[$index]; $li[$index] = [byte]($old -bxor 1)
    Require (!(Call-Protocol 'IsModelIdentity' @($li, [byte]1, [byte]2))) ('Incorrect MR0097 identity accepted at ' + $index)
    $li[$index] = $old
}

foreach ($sample in @(
    @('11 01 06 19 59 08 00 00', 1, 89, $false),
    @('11 FF 06 19 3C 04 01 01', 255, 60, $true),
    @('11 01 06 19 3E 04 00 00', 1, 62, $false),
    @('11 01 06 19 00 01 00', 1, 0, $false),
    @('11 01 06 19 65 04 00', 1, -1, $false),
    @('11 02 06 19 3E 04 00', 1, -1, $false),
    @('11 01 FF 06 19 05 00', 1, -1, $false),
    @('11 01 06 19 3E', 1, -1, $false)
)) {
    $frame = [byte[]]($sample[0].Split(' ') | ForEach-Object { [Convert]::ToByte($_, 16) })
    $argsList = [object[]]@($frame, [byte]$sample[1], [byte]6, -1, $false)
    $valid = Call-Protocol 'TryReadUnifiedBattery' $argsList
    Require ($valid -eq ($sample[2] -ge 0) -and $argsList[3] -eq $sample[2] -and $argsList[4] -eq $sample[3]) "Battery parse mismatch: $($sample[0])"
}

$dpi = [byte[]](17, 1, 9, 89, 0, 6, 64, 3, 32, 6, 64, 3, 32, 2)
$argsList = [object[]]@($dpi, [byte]1, [byte]9, -1, -1)
Call-Protocol 'ReadExtendedDpi' $argsList
Require ($argsList[3] -eq 1600 -and $argsList[4] -eq 1600) 'Extended DPI symmetric read failed'
$dpi[10] = 114
Call-Protocol 'ReadExtendedDpi' $argsList
Require ($argsList[3] -eq 1600 -and $argsList[4] -eq 1650) 'Independent Y lost'
$dpi[1] = 2
Call-Protocol 'ReadExtendedDpi' $argsList
Require ($argsList[3] -eq -1 -and $argsList[4] -eq -1) 'Wrong-slot DPI accepted'

$profileBytes = [byte[]](3, 3, 0, 0, 32, 3, 32, 3, 2, 176, 4, 176, 4, 2, 64, 6, 64, 6, 2, 96, 9, 96, 9, 2, 128, 12, 128, 12, 2, 0, 0, 0)
$profile = Call-Protocol 'DecodeOnboardDpiProfile' @($profileBytes, 2, [byte]13)
$flags = [Reflection.BindingFlags]'Instance,Public'
foreach ($axis in 'X', 'Y') {
    $values = $profile.GetType().GetField($axis, $flags).GetValue($profile)
    Require (($values -join ',') -eq '800,1200,1600,2400,3200') "Invalid onboard $axis table"
}
Require ($profile.GetType().GetField('Stage', $flags).GetValue($profile) -eq 3) 'Incorrect active slot'
Require ($null -eq (Call-Protocol 'DecodeOnboardDpiProfile' @($profileBytes, 5, [byte]13))) 'Invalid slot accepted'

for ($code = 0; $code -lt 7; $code++) {
    Require ((Call-Protocol 'ReadReportRate' @([byte[]](17, 1, 10, 41, $code), [byte]1, [byte]10)) -eq (125 -shl $code)) 'Report rate parse failed'
}
Require ((Call-Protocol 'ReadReportRate' @([byte[]](17, 1, 10, 41, 7), [byte]1, [byte]10)) -eq 0) 'Invalid rate code accepted'

# 3. Verification, built-in routing & shared C54D fallback
$dev = New-Object RazerBatteryTray.MouseBatteryInfo
$dev.DeviceId = 'Logitech:046D:C54D:1'; $dev.Brand = 'Logitech'; $dev.Category = 'Mouse'
$dev.DeviceName = 'Logitech G PRO X SUPERLIGHT 2'; $dev.HardwareId = $ld.DevicePath
Require (!$driver.CanHandle($dev)) 'Unverified path claimed'
$resolvedUnverified = [RazerBatteryTray.HardwareDriverRegistry]::ResolveDriver($dev)
Require ($resolvedUnverified.DriverId -eq 'BuiltIn:LogitechMouse') 'Shared C54D unverified mouse did not fall back to LogitechMouse'

Require (Call-Protocol 'AcceptIdentity' @($ld, [byte]1, [byte]2, $li)) 'Verified MR0097 identity not accepted'
Require ($driver.CanHandle($dev)) 'Verified MR0097 path state not handled'
Require ([RazerBatteryTray.HardwareDriverRegistry]::ResolveDriver($dev).DriverId -eq $driver.DriverId) 'MR0097 built-in routing failed'
$dev.HardwareId = 'different-unverified-logitech-path'
Require (!$driver.CanHandle($dev)) 'Verified model identity leaked to another path'
$dev.HardwareId = $ld.DevicePath

$dev.IsDonglePresent = $true; $dev.IsSleeping = $false; $dev.DpiStageCount = 5
$driver.PopulateCapabilities($dev)
Require (!$dev.Capabilities.SupportsDpi -and !$dev.Capabilities.SupportsPollingRate -and $dev.Capabilities.SupportsHardwareDpiMonitor -and $dev.Capabilities.SupportsDpiStages -and $dev.SupportsDpiWrite -eq $false -and $dev.SupportsPollingRateWrite -eq $false) 'MR0097 capability limits mismatch'
Require (!$driver.SetDpi($dev, 1234, 1234, 3) -and !$driver.SetPollingRate($dev, 8000) -and !$driver.UpdateHardwareStages($dev, 1, [int[]](800, 1600), [int[]](800, 1600))) 'Unsupported write accepted without live endpoint'
Require ($driver.MatchesVidPid(0x046D, 0xC54D) -and $driver.MatchesVidPid(0x046D, 0xC09B) -and !$driver.MatchesVidPid(0x046D, 0x1234)) 'VID/PID coverage too broad'

# 4. Cache slot isolation & multi-receiver endpoint routing
$stamp = [DateTime]'2026-11-08T10:00:00'
$logitechState = Call-Protocol 'CreateState' @($ld, [byte]1, 89, $true, $false, $stamp, 'Logitech:40A9:Unit:01-02-03-04')
$logitechMemory = $type.GetField('last', $static).GetValue($null)
$logitechMemory[('BuiltIn:LogitechMR0097:' + $ld.DevicePath + ':1')] = $logitechState
$cached = Call-Protocol 'CachedState' @($ld, [byte]1)
Require ($cached.BatteryPercent -eq 89 -and $cached.LastUpdated -eq $stamp -and $cached.IsSleeping -and $cached.IsChargingUnknown -and !$cached.IsCharging) 'MR0097 cache freshness changed'
Require ($null -eq (Call-Protocol 'CachedState' @($ld, [byte]2))) 'MR0097 cache leaked to a different receiver slot'
$logitechMemory.Clear()

$ld2 = New-Endpoint 0x046D 0xC54D 0xFF00 2 20
$ld2.DevicePath = 'second-logitech-receiver'
Require (Call-Protocol 'AcceptIdentity' @($ld2, [byte]1, [byte]2, $li)) 'Second Logitech identity rejected'
$logitechState2 = Call-Protocol 'CreateState' @($ld2, [byte]1, 80, $false, $false, $stamp, 'Logitech:40A9:Unit:05-06-07-08')
Call-Protocol 'RememberActiveEndpoint' @($ld, [byte]1, [byte]9, $profile)
Call-Protocol 'RememberActiveEndpoint' @($ld2, [byte]1, [byte]9, $profile)
$epFlags = [Reflection.BindingFlags]'Instance,Public,NonPublic'
foreach ($case in @(@($logitechState, $ld.DevicePath), @($logitechState2, $ld2.DevicePath))) {
    $endpoint = Call-Protocol 'FindEndpoint' @($case[0])
    Require ($null -ne $endpoint) 'One C54D receiver replaced the other active endpoint'
    $descriptor = $endpoint.GetType().GetField('Descriptor', $epFlags).GetValue($endpoint)
    Require ($descriptor.DevicePath -eq $case[1]) 'Performance route points to a different receiver'
}

# 5. Deterministic ReceiveReply & cold-start identity budget replay
function Replay-Reply([int]$budget, [object[]]$events) {
    $script:replyElapsed = [long]0; $script:replyIndex = 0; $script:replyEvents = $events
    $script:replyTimeouts = New-Object 'System.Collections.Generic.List[int]'
    $reader = [Func[int, byte[]]] {
        param([int]$remaining)
        $script:replyTimeouts.Add($remaining)
        if ($script:replyIndex -ge $script:replyEvents.Count) { return $null }
        $event = $script:replyEvents[$script:replyIndex]; $script:replyIndex++
        if ($event.Delay -ge $remaining) { $script:replyElapsed += $remaining; return $null }
        $script:replyElapsed += $event.Delay
        return $event.Frame
    }
    $elapsed = [Func[long]] { return $script:replyElapsed }
    $tx = New-Object byte[] 20; $tx[0] = 0x11; $tx[1] = 1; $tx[3] = 9; $tx[5] = 3
    $reply = Call-Protocol 'ReceiveReply' @($tx, $budget, $reader, $elapsed)
    return [pscustomobject]@{ Reply = $reply; Elapsed = $script:replyElapsed; Reads = $script:replyIndex; Budgets = $script:replyTimeouts.ToArray() }
}
$rootReply = New-Object byte[] 20; $rootReply[0] = 0x11; $rootReply[1] = 1; $rootReply[3] = 9; $rootReply[4] = 2
$cold = Replay-Reply 2000 @([pscustomobject]@{ Delay = 1059; Frame = $rootReply })
Require ($null -ne $cold.Reply -and $cold.Elapsed -eq 1059 -and $cold.Reads -eq 1) 'Measured 1059ms cold identity reply rejected'
$ordinary = Replay-Reply 500 @([pscustomobject]@{ Delay = 1059; Frame = $rootReply })
Require ($null -eq $ordinary.Reply -and $ordinary.Elapsed -eq 500) 'Ordinary command exceeded its original deadline'
$wrongSlot = $rootReply.Clone(); $wrongSlot[1] = 2
$wrongFunction = $rootReply.Clone(); $wrongFunction[3] = 0x19
$filtered = Replay-Reply 2000 @([pscustomobject]@{ Delay = 500; Frame = $wrongSlot }, [pscustomobject]@{ Delay = 500; Frame = $wrongFunction }, [pscustomobject]@{ Delay = 59; Frame = $rootReply })
Require ($null -ne $filtered.Reply -and $filtered.Elapsed -eq 1059 -and ($filtered.Budgets -join ',') -eq '2000,1500,1000') 'Unrelated traffic reset deadline'

$script:identityBudgets = New-Object 'System.Collections.Generic.List[int]'
$script:identityInfo = $li.Clone()
$identityExchange = [Func[byte, int, byte[]]] {
    param([byte]$feature, [int]$timeoutMs)
    $script:identityBudgets.Add($timeoutMs)
    if ($feature -eq 0) { return (Replay-Reply $timeoutMs @([pscustomobject]@{ Delay = 1059; Frame = $rootReply })).Reply }
    return $script:identityInfo
}
$identityArgs = [object[]]@([byte]1, $false, $identityExchange)
$identified = Call-Protocol 'ReadIdentityCore' $identityArgs
Require ($null -ne $identified -and !$identityArgs[1] -and ($script:identityBudgets -join ',') -eq '2000,500') 'ReadIdentity did not apply cold-only budget'
$script:identityInfo[12] = 0xB5
$identityArgs = [object[]]@([byte]1, $false, $identityExchange)
Require ($null -eq (Call-Protocol 'ReadIdentityCore' $identityArgs) -and $identityArgs[1]) 'Cold query bypassed exact paired-model validation'

$wrongLogitech = $li.Clone(); $wrongLogitech[12] = 0xB5
Require (!(Call-Protocol 'AcceptIdentity' @($ld, [byte]1, [byte]2, $wrongLogitech)) -and !$driver.CanHandle($logitechState)) 'Changed paired identity retained MR0097 ownership'
$driver.PopulateCapabilities($logitechState)
Require (!$logitechState.Capabilities.SupportsBattery -and !$logitechState.Capabilities.SupportsDpiStages -and !$logitechState.Capabilities.SupportsHardwareDpiMonitor) 'Unverified MR0097 state retained capabilities'

Write-Host 'PASS built-in MR0097 protocol, onboard format 7, routing, cache and isolation'
