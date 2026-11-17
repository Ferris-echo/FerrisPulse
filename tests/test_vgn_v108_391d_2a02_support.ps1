# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# Real V108 (391D:2A02) RX fixtures captured on physical hardware over the 2.4G receiver.
# Load bytes only; no physical HID traffic or user settings changes in this test.
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path "$PSScriptRoot\..").Path
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $project 'FerrisPulse.exe')))
$type = $asm.GetType('RazerBatteryTray.VgnKeyboardHelper', $true)
$script:assertions = 0
function Assert-True($value, [string]$message) {
    if (-not $value) { throw "FAIL: $message" }
    $script:assertions++
}

# --- identity of the dongle must never leak into the Neon75 (3151) or Yjx (391D:1A03) paths ---
Assert-True ($type.GetMethod('IsVgnV108').Invoke($null, @('VGN:391D:2A02:KB'))) 'Recognize the built-in V108 DeviceId'
Assert-True ($type.GetMethod('IsVgnV108').Invoke($null, @('HID:391D:2A02'))) 'Recognize the V108 hardware fingerprint'
Assert-True ($type.GetMethod('IsVgnV108').Invoke($null, @('\\?\hid#vid_391d&pid_2a02&mi_01&col01#8&274d167f&0&0000'))) 'Recognize the receiver physical path'
Assert-True (-not $type.GetMethod('IsVgnV108').Invoke($null, @('VGN:3151:5038:KB'))) 'Neon75 receiver must not match V108'
Assert-True (-not $type.GetMethod('IsVgnV108').Invoke($null, @('VGN:391D:1A03:MS'))) 'Yjx 391D:1A03 family must not match V108'
Assert-True (-not $type.GetMethod('IsVgnV108').Invoke($null, @($null))) 'Null identifier is rejected'
Assert-True (-not $type.GetMethod('IsVgnV108').Invoke($null, @(''))) 'Empty identifier is rejected'

# --- exact TX frames: report ID 0x13 + command + 0x01 + 0x00 + payload length + checksum ---
$buildFrame = $type.GetMethod('BuildV108Frame')
$batteryTx = $buildFrame.Invoke($null, @([byte]0x4A, [byte]2, $null))
$identityTx = $buildFrame.Invoke($null, @([byte]0x05, [byte]6, $null))
Assert-True ([BitConverter]::ToString($batteryTx) -eq '13-4A-01-00-02-00-00-00-00-00-00-00-00-00-00-00-00-00-00-60') 'Exact 20-byte V108 battery query'
Assert-True ([BitConverter]::ToString($identityTx) -eq '13-05-01-00-06-00-00-00-00-00-00-00-00-00-00-00-00-00-00-1F') 'Exact 20-byte V108 model query'
$sumFn = $type.GetMethod('SumV108Frame')
Assert-True ($sumFn.Invoke($null, [object[]]@(,$batteryTx)) -eq 0x60) 'Checksum covers bytes 0..18 of the TX frame'

# --- physical fixtures (multi-polled, byte identical on every round) ---
[byte[]]$batteryRx = @(0x13,0x4A,0x01,0x00,0x02,0x5A,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xBA)
[byte[]]$identityRx = @(0x13,0x05,0x01,0x00,0x06,0x0A,0x00,0x00,0x00,0x00,0x30,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x59)
# Captured on the same unit after plugging the USB-C cable: only the status byte flipped 0x00 -> 0x10.
[byte[]]$chargingRx = @(0x13,0x4A,0x01,0x00,0x02,0x5A,0x10,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xCA)
$isResponse = $type.GetMethod('IsV108Response')
Assert-True ($isResponse.Invoke($null, @($batteryRx, 20, [byte]0x4A, 2))) 'Physical battery frame passes the strict response contract'
Assert-True ($isResponse.Invoke($null, @($identityRx, 20, [byte]0x05, 6))) 'Physical model frame passes the strict response contract'
Assert-True (-not $isResponse.Invoke($null, @($batteryRx, 20, [byte]0x05, 2))) 'Command echo is enforced'
Assert-True (-not $isResponse.Invoke($null, @($batteryRx, 20, [byte]0x4A, 6))) 'Payload length echo is enforced'

$batteryParser = $type.GetMethod('TryParseV108Battery')
function Invoke-V108Battery([byte[]]$buffer) {
    $argsList = New-Object object[] 4
    $argsList[0]=$buffer; $argsList[1]=-1; $argsList[2]=$false; $argsList[3]=$false
    $ok = $batteryParser.Invoke($null, $argsList)
    return @{ Ok=$ok; Battery=$argsList[1]; Charging=$argsList[2]; Unknown=$argsList[3] }
}
$live = Invoke-V108Battery $batteryRx
Assert-True ($live.Ok -and $live.Battery -eq 90 -and -not $live.Charging -and -not $live.Unknown) 'Physical frame yields 90% on battery power'
$liveCharging = Invoke-V108Battery $chargingRx
Assert-True ($isResponse.Invoke($null, @($chargingRx, 20, [byte]0x4A, 2))) 'Captured charging frame passes the strict response contract'
Assert-True ($liveCharging.Ok -and $liveCharging.Battery -eq 90 -and $liveCharging.Charging -and -not $liveCharging.Unknown) 'Captured charging frame yields 90% while charging'

$identityParser = $type.GetMethod('TryParseV108Identity')
$modelArgs = New-Object object[] 2
$modelArgs[0]=$identityRx; $modelArgs[1]=0
Assert-True ($identityParser.Invoke($null, $modelArgs) -and $modelArgs[1] -eq 0x30) 'Physical model frame identifies code 0x30'
Assert-True ($type.GetMethod('GetV108ModelName').Invoke($null, @(0x30)) -eq 'VGN V108') 'Model code 0x30 maps to VGN V108'
Assert-True ($type.GetMethod('GetV108ModelName').Invoke($null, @(0x09)) -ne 'VGN V108') 'Unknown model code must not be reported as V108'

# --- charging encoding: 0x10 family = charging, 0x00 = on battery, unobserved = unknown ---
function New-BatteryFrame([byte]$percent, [byte]$status) {
    $frame = New-Object byte[] 20
    $frame[0]=0x13; $frame[1]=0x4A; $frame[2]=0x01; $frame[3]=0x00; $frame[4]=0x02
    $frame[5]=$percent; $frame[6]=$status
    $sum = 0
    for($i=0; $i -lt 19; $i++) { $sum += $frame[$i] }
    $frame[19] = [byte]($sum -band 255)
    return $frame
}
$charging = Invoke-V108Battery (New-BatteryFrame 55 0x10)
Assert-True ($charging.Ok -and $charging.Battery -eq 55 -and $charging.Charging -and -not $charging.Unknown) 'High nibble 0x10 is reported as charging'
$g75Low = Invoke-V108Battery (New-BatteryFrame 55 0x01)
Assert-True ($g75Low.Ok -and -not $g75Low.Charging -and -not $g75Low.Unknown) '0x01 stays on battery (shared 0x13 family encoding)'
$odd = Invoke-V108Battery (New-BatteryFrame 55 0x03)
Assert-True ($odd.Ok -and -not $odd.Charging -and $odd.Unknown) 'Unobserved status is flagged unknown, never guessed'
$fullCharged = Invoke-V108Battery (New-BatteryFrame 100 0x10)
Assert-True ($fullCharged.Ok -and $fullCharged.Battery -eq 100 -and $fullCharged.Charging) '100% while plugged still reports charging'

# --- corrupt frames must be rejected instead of surfacing a bogus percentage ---
$badChecksum = [byte[]]$batteryRx.Clone(); $badChecksum[19]=0
Assert-True (-not (Invoke-V108Battery $badChecksum).Ok) 'Reject wrong checksum'
$badHeader = [byte[]]$batteryRx.Clone(); $badHeader[0]=0x12
Assert-True (-not (Invoke-V108Battery $badHeader).Ok) 'Reject foreign frame header'
$badFlag = [byte[]]$batteryRx.Clone(); $badFlag[2]=0x02
Assert-True (-not (Invoke-V108Battery $badFlag).Ok) 'Reject frame without the 0x01 direction flag'
$overRange = [byte[]]$batteryRx.Clone(); $overRange[5]=101; $overRange[19]=[byte]((190-90+101) -band 255)
Assert-True (-not (Invoke-V108Battery $overRange).Ok) 'Reject battery above 100%'
$truncated = New-Object byte[] 12
[Array]::Copy($batteryRx, $truncated, 12)
Assert-True (-not (Invoke-V108Battery $truncated).Ok) 'Reject truncated response'
Assert-True (-not (Invoke-V108Battery $null).Ok) 'Reject null response'

# --- capability honesty: a VGN keyboard must not inherit the VGN mouse driver ---
$driverType = $asm.GetType('RazerBatteryTray.VgnMouseDriver', $true)
$driver = [Activator]::CreateInstance($driverType)
$categoryType = $asm.GetType('RazerBatteryTray.DeviceCategory', $true)
$infoType = $asm.GetType('RazerBatteryTray.MouseBatteryInfo', $true)
$kbInfo = [Activator]::CreateInstance($infoType)
$kbInfo.DeviceId = 'VGN:391D:2A02:KB'; $kbInfo.Brand = 'VGN'
$kbInfo.Category = [Enum]::Parse($categoryType, 'Keyboard')
Assert-True (-not $driverType.GetMethod('CanHandle').Invoke($driver, @($kbInfo))) 'VGN keyboard must not be claimed by the mouse driver'
$mouseInfo = [Activator]::CreateInstance($infoType)
$mouseInfo.DeviceId = 'VGN:391D:1A03:MS'; $mouseInfo.Brand = 'VGN'
$mouseInfo.Category = [Enum]::Parse($categoryType, 'Mouse')
Assert-True ($driverType.GetMethod('CanHandle').Invoke($driver, @($mouseInfo))) 'VGN mouse keeps the existing driver path'

$registry = $asm.GetType('RazerBatteryTray.HardwareDriverRegistry', $true)
$registry.GetMethod('EnsureCapabilitiesInitialized').Invoke($null, @($kbInfo))
Assert-True (-not $kbInfo.Capabilities.SupportsDpi) 'Keyboard keeps SupportsDpi=false after capability init'
Assert-True (-not $kbInfo.Capabilities.SupportsPollingRate) 'Keyboard keeps SupportsPollingRate=false after capability init'
Assert-True ($kbInfo.Capabilities.SupportsBattery) 'Keyboard still reports battery capability'

Write-Host "PASS: VGN V108 (391D:2A02) ($script:assertions assertions)"
