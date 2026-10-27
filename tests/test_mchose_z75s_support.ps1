# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# Real Z75S RX fixtures: identity, discharging and USB charging over the 2.4G receiver.
# Load bytes only; no physical HID traffic or user settings changes in this test.
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path "$PSScriptRoot\..").Path
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $project 'FerrisPulse.exe')))
$type = $asm.GetType('RazerBatteryTray.MchoseDeviceHelper', $true)
$script:assertions = 0
function Assert-True($value, [string]$message) {
    if (-not $value) { throw "FAIL: $message" }
    $script:assertions++
}

Assert-True ($type.GetMethod('IsSupportedVidPid').Invoke($null, @(0x258A, 0x01BB))) 'Z75S receiver must be supported'
Assert-True ($type.GetMethod('IsSupportedVidPid').Invoke($null, @(0x3837, 0x3033))) 'G87 V2 must remain supported'
Assert-True (-not $type.GetMethod('IsSupportedVidPid').Invoke($null, @(0x258A, 0x01B9))) 'Unvalidated wired PID must remain excluded'
Assert-True ($type.GetMethod('IsMchoseKeyboard').Invoke($null, @('HID\VID_258A&PID_01BB&MI_01'))) 'Recognize the Z75S receiver identifier'
Assert-True (-not $type.GetMethod('IsMchoseKeyboard').Invoke($null, @('HID\VID_258A&PID_010C'))) 'Do not claim other SinoWealth keyboards'

$identityTx = $type.GetMethod('BuildZ75sIdentityFrame').Invoke($null, @())
$batteryTx = $type.GetMethod('BuildZ75sBatteryFrame').Invoke($null, @())
Assert-True ([BitConverter]::ToString($identityTx) -eq '13-05-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-19') 'Exact model query, including report ID and checksum'
Assert-True ([BitConverter]::ToString($batteryTx) -eq '13-4A-01-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-00-5E') 'Exact 20-byte battery query'

$identityParser = $type.GetMethod('ParseZ75sIdentityResponse')
$batteryParser = $type.GetMethod('ParseZ75sBatteryResponse')
[byte[]]$identity = @(0x13,0x05,1,0,6,0x0A,0,0,0,0,9,0,0,0,0,0,0,0,0,0x32)
[byte[]]$discharging = @(0x13,0x4A,1,0,2,0x55,0,0,0,0,0,0,0,0,0,0,0,0,0,0xB5)
[byte[]]$charging = @(0x13,0x4A,1,0,2,0x32,0x10,0,0,0,0,0,0,0,0,0,0,0,0,0xA2)
[byte[]]$empty = @(0x13,0x4A,1,0,2,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0x60)
[byte[]]$full = @(0x13,0x4A,1,0,2,100,1,0,0,0,0,0,0,0,0,0,0,0,0,0xC5)
function Invoke-Battery([byte[]]$buffer, [int]$length = 20) {
    $argsList = New-Object object[] 4
    $argsList[0]=$buffer; $argsList[1]=$length; $argsList[2]=-1; $argsList[3]=$false
    $ok = $batteryParser.Invoke($null, $argsList)
    return @{ Ok=$ok; Battery=$argsList[2]; Charging=$argsList[3] }
}
function Update-Checksum([byte[]]$buffer) {
    $sum = 0
    for($i=0; $i -lt 19; $i++) { $sum += $buffer[$i] }
    $buffer[19] = [byte]($sum -band 255)
}
Assert-True ($identityParser.Invoke($null, @($identity,20))) 'Physical model ID is Z75S'
$otherModel = [byte[]]$identity.Clone(); $otherModel[10]=1; Update-Checksum $otherModel
Assert-True (-not $identityParser.Invoke($null, @($otherModel,20))) 'Shared VID/PID with Z75 identity must not become Z75S'
$badIdentity = [byte[]]$identity.Clone(); $badIdentity[19]=0
Assert-True (-not $identityParser.Invoke($null, @($badIdentity,20))) 'Corrupt identity must be rejected'
foreach($case in @(
    @{Rx=$discharging; Battery=85; Charging=$false},
    @{Rx=$charging; Battery=50; Charging=$true},
    @{Rx=$empty; Battery=0; Charging=$false},
    @{Rx=$full; Battery=100; Charging=$false}
)) {
    $r = Invoke-Battery $case.Rx
    Assert-True ($r.Ok -and $r.Battery -eq $case.Battery -and $r.Charging -eq $case.Charging) "Battery/charging contract for $($case.Battery)%"
}

# Each mutation has a valid checksum, so header/range guards are tested independently.
foreach($change in @(@(0,0),@(1,0x41),@(2,2),@(3,1),@(4,1),@(5,101))) {
    $bad = [byte[]]$discharging.Clone(); $bad[$change[0]]=[byte]$change[1]; Update-Checksum $bad
    $r=Invoke-Battery $bad
    Assert-True (-not $r.Ok -and $r.Battery -eq -1 -and -not $r.Charging) "Reject invalid field at $($change[0])"
}
$bad = [byte[]]$discharging.Clone(); $bad[19]=0
Assert-True (-not (Invoke-Battery $bad).Ok) 'Reject invalid battery checksum'
Assert-True (-not (Invoke-Battery $discharging 19).Ok) 'Reject truncated report length'
Assert-True (-not (Invoke-Battery $discharging 21).Ok) 'Reject claimed length beyond backing buffer'
Assert-True (-not (Invoke-Battery ([byte[]]@(0x13,0x4A)) 20).Ok) 'Reject short backing buffer'
Assert-True (-not (Invoke-Battery $null).Ok) 'Reject null buffer'

$descriptor = [Activator]::CreateInstance($asm.GetType('RazerBatteryTray.HidDeviceDescriptor'))
$descriptor.VendorId=0x258A; $descriptor.ProductId=0x01BB
$descriptor.InstanceId='HID\VID_258A&PID_01BB\TEST'
$descriptor.DevicePath='test-z75s'
$makeInfo=$type.GetMethod('CreateZ75sInfo',[Reflection.BindingFlags]'NonPublic,Static')
$fresh=$makeInfo.Invoke($null,@($descriptor,$null,85,$false,$true))
Assert-True ($fresh.Category.ToString() -eq 'Keyboard' -and $fresh.DeviceName -eq 'MCHOSE Z75S') 'Native keyboard classification and verified model name'
Assert-True ($fresh.DeviceId -eq 'MCHOSE:258A:01BB' -and $fresh.HardwareFingerprint -eq 'HID:258A:01BB') 'Existing DeviceManager identity/merge contract'
Assert-True ($fresh.IsConnected -and $fresh.IsDonglePresent -and -not $fresh.IsSleeping -and $fresh.BatteryPercent -eq 85) 'Fresh real telemetry remains online'
Assert-True ($fresh.Dpi -eq 0 -and $null -eq $fresh.SupportedPollingRates) 'Keyboard does not advertise mouse performance controls'
$sampleTime=[DateTime]::Parse('2026-10-26T00:00:00Z').ToUniversalTime()
$fresh.LastUpdated=$sampleTime
$sleep=$makeInfo.Invoke($null,@($descriptor,$fresh,-1,$false,$false))
Assert-True ($sleep.IsConnected -and $sleep.IsSleeping -and $sleep.BatteryPercent -eq 85 -and $sleep.ExactBatteryPercent -eq 85 -and $sleep.LastUpdated -eq $sampleTime) 'Sleep preserves last physical battery and acquisition time'
$legacyBattery=$type.GetField('cachedBattery',[Reflection.BindingFlags]'NonPublic,Static')
$savedLegacy=$legacyBattery.GetValue($null)
try {
    $legacyBattery.SetValue($null,99)
    $unknown=$makeInfo.Invoke($null,@($descriptor,$null,-1,$false,$false))
    Assert-True ($unknown.BatteryPercent -eq -1 -and $unknown.ExactBatteryPercent -eq -1) 'No G87 cache or fabricated battery on first sleeping query'
} finally { $legacyBattery.SetValue($null,$savedLegacy) }
$gaming=$asm.GetType('RazerBatteryTray.UniversalGamingDeviceHelper')
Assert-True (-not $gaming.GetMethod('IsSupportedGamingDevice').Invoke($null,@(0x258A,'hid#vid_258a&pid_01bb&mi_01','Gaming Keyboard'))) 'Z75S must never receive generic mouse protocol probes'
Write-Host "PASS: MCHOSE Z75S ($script:assertions assertions)"
