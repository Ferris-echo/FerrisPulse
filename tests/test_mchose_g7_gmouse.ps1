# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# Real G7 receiver RX fixtures for the MCHOSE /gmouse WebHID protocol (0xA8A5:0x2255).
# Loads bytes only; no physical HID traffic and no user settings changes in this test.
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path "$PSScriptRoot\..").Path
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $project 'FerrisPulse.exe')))
$type = $asm.GetType('RazerBatteryTray.MchoseGMouseHelper', $true)
$script:assertions = 0
function Assert-True($value, [string]$message) {
    if (-not $value) { throw "FAIL: $message" }
    $script:assertions++
}

Assert-True ($type.GetMethod('IsSupportedVidPid').Invoke($null, @(0xA8A5, 0x2255))) 'G7 receiver must be supported'
Assert-True (-not $type.GetMethod('IsSupportedVidPid').Invoke($null, @(0xA8A5, 0x2254))) 'Unvalidated sibling PID must stay excluded'
Assert-True (-not $type.GetMethod('IsSupportedVidPid').Invoke($null, @(0x41E4, 0x2004))) 'G98V2 keyboard receiver must not be claimed by the mouse helper'
Assert-True ($type.GetMethod('IsMchoseGMouse').Invoke($null, @('HID\VID_A8A5&PID_2255&MI_02'))) 'Recognize the G7 receiver identifier'
Assert-True (-not $type.GetMethod('IsMchoseGMouse').Invoke($null, @('HID\VID_41E4&PID_2004&MI_01'))) 'Do not claim the keyboard receiver'

$batteryTx = $type.GetMethod('BuildBatteryFrame').Invoke($null, @())
$versionTx = $type.GetMethod('BuildVersionFrame').Invoke($null, @())
$onlineTx = $type.GetMethod('BuildOnlineFrame').Invoke($null, @())
$zeroTail = ('-00' * 56)
Assert-True ([BitConverter]::ToString($batteryTx) -eq '00-55-30-A5-0B-2E-01-01-01' + $zeroTail) 'Exact 65-byte battery query with report ID 0'
Assert-True ([BitConverter]::ToString($versionTx) -eq '00-55-03' + ('-00' * 62)) 'Exact 65-byte version query'
Assert-True ([BitConverter]::ToString($onlineTx) -eq '00-55-ED' + ('-00' * 62)) 'Exact 65-byte online query'

$batteryParser = $type.GetMethod('ParseBatteryResponse')
function Invoke-Battery([byte[]]$buffer, [int]$length) {
    $argsList = New-Object object[] 4
    $argsList[0]=$buffer; $argsList[1]=$length; $argsList[2]=-1; $argsList[3]=$false
    $ok = $batteryParser.Invoke($null, $argsList)
    return @{ Ok=$ok; Battery=$argsList[2]; Charging=$argsList[3] }
}

# Physical capture from the real receiver: 00 AA 30 A5 0B 0A 01 01 01 64 00 ...
$live = New-Object byte[] 65
$live[0]=0x00; $live[1]=0xAA; $live[2]=0x30; $live[3]=0xA5; $live[4]=0x0B; $live[5]=0x0A
$live[6]=0x01; $live[7]=0x01; $live[8]=0x01; $live[9]=100; $live[10]=0x00
$r = Invoke-Battery $live 65
Assert-True ($r.Ok -and $r.Battery -eq 100 -and -not $r.Charging) 'Live 65-byte capture parses to 100% discharging'

$charging = $live.Clone()
$charging[9]=42; $charging[10]=0x01
$r = Invoke-Battery $charging 65
Assert-True ($r.Ok -and $r.Battery -eq 42 -and $r.Charging) 'Charge flag byte 10 maps to IsCharging'

# WebHID view without the leading report ID must resolve one byte earlier.
$webhid = New-Object byte[] 64
$webhid[0]=0xAA; $webhid[1]=0x30; $webhid[2]=0xA5; $webhid[3]=0x0B; $webhid[4]=0x0A
$webhid[7]=0x01; $webhid[8]=93; $webhid[9]=0x00
$r = Invoke-Battery $webhid 64
Assert-True ($r.Ok -and $r.Battery -eq 93 -and -not $r.Charging) '64-byte WebHID layout resolves battery at offset 8'

$bad = $live.Clone(); $bad[1]=0xAB
Assert-True (-not (Invoke-Battery $bad 65).Ok) 'Reject wrong response marker'
$bad = $live.Clone(); $bad[2]=0x31
Assert-True (-not (Invoke-Battery $bad 65).Ok) 'Reject wrong command echo'
$bad = $live.Clone(); $bad[9]=101
Assert-True (-not (Invoke-Battery $bad 65).Ok) 'Reject battery above 100'
$bad = $live.Clone(); $bad[9]=255
Assert-True (-not (Invoke-Battery $bad 65).Ok) 'Reject 0xFF sentinel as battery'
Assert-True (-not (Invoke-Battery $live 11).Ok) 'Reject truncated report length'
Assert-True (-not (Invoke-Battery $live 66).Ok) 'Reject claimed length beyond backing buffer'
Assert-True (-not (Invoke-Battery ([byte[]]@(0x00,0xAA,0x30)) 65).Ok) 'Reject short backing buffer'
Assert-True (-not (Invoke-Battery $null 65).Ok) 'Reject null buffer'

$versionParser = $type.GetMethod('ParseVersionResponse')
$versionRx = New-Object byte[] 65
$versionRx[0]=0x00; $versionRx[1]=0xAA; $versionRx[2]=0x03
[byte[]]$ascii = @(0x59,0x4A,0x58,0x2E,0x63,0x6F,0x2E,0x6D,0x6F,0x75,0x73,0x65)
for ($i=0; $i -lt $ascii.Length; $i++) { $versionRx[9+$i] = $ascii[$i] }
$versionRx[24]=0x32; $versionRx[25]=0x32; $versionRx[26]=0x33
$argsV = New-Object object[] 3
[byte[]]$versionRxBytes = $versionRx; $argsV[0]=$versionRxBytes; $argsV[1]=65; $argsV[2]=$null
Assert-True ($versionParser.Invoke($null, $argsV)) 'Version reply is recognized'
Assert-True ($argsV[2] -eq '223') 'Version ASCII 2.2.3 exposed for diagnostics'
$bad = $versionRx.Clone(); $bad[24]=0x2F
[byte[]]$badBytes = $bad; $argsV[0]=$badBytes; $argsV[1]=65; $argsV[2]=$null
Assert-True (-not $versionParser.Invoke($null, $argsV)) 'Non-digit version triples are rejected'

Assert-True ($type.GetMethod('GetTransport').Invoke($null, @(0xA8A5)) -eq '2.4G 无线') 'Receiver is reported as 2.4G'
Assert-True ($type.GetMethod('GetTransport').Invoke($null, @(0x40A6)) -eq 'USB 有线') 'Wired G3 V2 PID is reported as USB'

$gaming = $asm.GetType('RazerBatteryTray.UniversalGamingDeviceHelper')
Assert-True (-not $gaming.GetMethod('IsSupportedGamingDevice').Invoke($null, @(0xA8A5, 'hid#vid_a8a5&pid_2255&mi_02', 'MCHOSE G7'))) 'G7 must never receive generic mouse protocol probes'
$mchose = $asm.GetType('RazerBatteryTray.MchoseDeviceHelper')
Assert-True (-not $mchose.GetMethod('IsSupportedVidPid').Invoke($null, @(0xA8A5, 0x2255))) 'Keyboard helper must not claim the G7 receiver'
Write-Host "PASS: MCHOSE G7 gmouse protocol ($script:assertions assertions)"
