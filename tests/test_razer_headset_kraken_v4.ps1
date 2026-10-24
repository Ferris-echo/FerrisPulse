# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# Physical captures from 2026-10-24; boundary/corruption cases are synthetic.
$ErrorActionPreference = 'Stop'
[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '../FerrisPulse.exe'))) | Out-Null
function Check($ok, $message) { if (-not $ok) { throw $message } }
function Frame($cmd, $value) {
    $r = New-Object byte[] 64
    [byte[]]$prefix = @(2,2,0x60,0,0,0,5,0,0x80,0x80,$cmd,1,1,$value)
    [Array]::Copy($prefix,$r,$prefix.Length)
    return ,$r
}
$r = Frame 0x21 69
Check ([RazerBatteryTray.RazerHeadsetHelper]::ParseKrakenReplyValue($r,0x21) -eq 69) 'Captured battery'
foreach ($pair in @(@(0x2A,0),@(0x2A,1),@(0x20,1))) {
    Check ([RazerBatteryTray.RazerHeadsetHelper]::ParseKrakenReplyValue((Frame $pair[0] $pair[1]),$pair[0]) -eq $pair[1]) 'Captured state'
}
foreach ($value in @(0,100)) {
    Check ([RazerBatteryTray.RazerHeadsetHelper]::ParseKrakenReplyValue((Frame 0x21 $value),0x21) -eq $value) 'Battery boundary'
}
foreach ($value in @(101,255)) {
    Check ([RazerBatteryTray.RazerHeadsetHelper]::ParseKrakenReplyValue((Frame 0x21 $value),0x21) -eq -1) 'Invalid battery'
}
foreach ($idx in @(0,1,2,6,7,9,10,11,12)) {
    $bad = $r.Clone(); $bad[$idx] = 0xFF
    Check ([RazerBatteryTray.RazerHeadsetHelper]::ParseKrakenReplyValue($bad,0x21) -eq -1) "Reject corrupt field $idx"
}
Check ([RazerBatteryTray.RazerHeadsetHelper]::ParseKrakenReplyValue((New-Object byte[] 13),0x21) -eq -1) 'Truncated reply'
Check ([RazerBatteryTray.RazerHeadsetHelper]::ParseKrakenReplyValue($null,0x21) -eq -1) 'Null reply'
Check ([RazerBatteryTray.RazerHeadsetHelper]::ParseKrakenReplyValue((Frame 0x2A 2),0x2A) -eq -1) 'Unknown charging'
$tx = [RazerBatteryTray.RazerHeadsetHelper]::BuildKrakenFrame(0x21)
Check ($tx.Length -eq 64 -and [BitConverter]::ToString($tx,0,12) -eq '02-00-60-00-00-00-04-00-00-80-21-00') 'Captured request'
Check ([RazerBatteryTray.RazerHeadsetHelper]::GetHeadsetModelName(0x056C,'Razer Kraken V4 - Chat') -eq 'Razer Kraken V4') 'Audio interface suffix'
Check ([RazerBatteryTray.RazerHeadsetHelper]::IsRazerHeadset('HID\VID_1532&PID_056C&MI_05')) 'Identity'
Write-Host 'PASS: Kraken V4 capture replay and invalid-response rejection'
