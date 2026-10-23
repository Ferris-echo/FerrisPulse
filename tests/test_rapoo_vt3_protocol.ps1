# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# C# 5 / .NET 4.0; UTF-8 BOM; byte-array assembly loading only.
$ErrorActionPreference = 'Stop'
$a = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '..\FerrisPulse.exe')))
$t = $a.GetType('RazerBatteryTray.RapooProtocol')
if ($null -eq $t) { throw 'Missing RapooProtocol: live VT3 protocol is not implemented' }
function Assert($ok, $message) { if (!$ok) { throw $message } }
function Parse-Status([byte[]]$data) {
    $args = [object[]]@($data, $null)
    $ok = $t.GetMethod('TryParseStatus').Invoke($null, $args)
    return @{ Ok=$ok; State=$args[1] }
}
# Synthetic protocol fixtures based on rapoo-tray, not claimed as captured hardware frames.
$r = Parse-Status ([byte[]]@(7,0x21,3,0x40,6,0x40,6,0,73,0))
Assert $r.Ok 'Valid wireless frame rejected'
Assert ($r.State.DpiStage -eq 4 -and $r.State.DpiX -eq 1600 -and $r.State.DpiY -eq 1600) '1600 DPI / zero-based stage index mapped incorrectly'
Assert ($r.State.BatteryPercent -eq 73 -and !$r.State.IsCharging) 'Discharging frame parsed incorrectly'
Assert ((Parse-Status ([byte[]]@(7,0x10,5,0x20,3,0x20,3,0,70,0))).State.DpiStage -eq 6) 'Sixth hardware DPI stage rejected'
foreach ($bat in @(0,1,100)) {
    $r = Parse-Status ([byte[]]@(7,0x10,0,0x20,3,0x20,3,2,$bat,0))
    Assert ($r.Ok -and $r.State.BatteryPercent -eq $bat -and $r.State.IsCharging) 'Valid low/full battery lost'
}
$r = Parse-Status ([byte[]]@(7,0x10,0,0x20,3,0x20,3,0,77,0))
Assert ($r.Ok -and !$r.State.IsCharging) 'Wired USB must not imply charging'
$r = Parse-Status ([byte[]]@(7,0x20,0,0x20,3,0x20,3,0,255,1))
Assert ($r.Ok -and $r.State.BatteryPercent -eq -1 -and $r.State.IsCharging) 'Invalid battery must be unknown, not 100%'
foreach ($bad in @([byte[]]@(7,32,0),[byte[]]@(7,32,6,32,3,32,3,0,70),[byte[]]@(7,32,255,32,3,32,3,0,70),[byte[]]@(8,32,0,32,3,32,3,0,70),[byte[]]@(7,48,1,32,3,32,3,0,70))) {
    Assert (!(Parse-Status $bad).Ok) 'Malformed report accepted'
}
Assert (!(Parse-Status $null).Ok) 'Null frame accepted'
foreach ($fixture in @([byte[]]@(1,0x80,8,1,0x82),[byte[]]@(8,1,0x80,8,1,0x82))) {
    $args = [object[]]@($fixture,1,$null)
    Assert ($t.GetMethod('TryParseFeature').Invoke($null,$args)) 'Valid Feature ACK layout rejected'
    Assert ($args[2][0] -eq 0x82) 'Feature payload offset wrong'
}
foreach ($fixture in @([byte[]]@(8,2,0,0,0,0x82),[byte[]]@(8,1,0,0,1),[byte[]]@(1,0,0,1))) {
    $args = [object[]]@($fixture,1,$null)
    Assert (!$t.GetMethod('TryParseFeature').Invoke($null,$args)) 'Busy/truncated ACK accepted'
}
$dType = $a.GetType('RazerBatteryTray.HidDeviceDescriptor')
$listType = [System.Collections.Generic.List``1].MakeGenericType($dType)
function Endpoint($group,$productId,$usage,$inputLen,$outputLen,$featureLen) {
    $d = [Activator]::CreateInstance($dType)
    $d.VendorId=0x24AE; $d.ProductId=$productId; $d.UsagePage=0xFF00; $d.Usage=$usage
    $d.InputReportLength=$inputLen; $d.OutputReportLength=$outputLen; $d.FeatureReportLength=$featureLen
    $d.InstanceId=('HID\VID_24AE&PID_{0:X4}&MI_01&COL{1:X2}\8&{2}&0&{1:X4}' -f $productId,$usage,$group)
    $d.DevicePath=$d.InstanceId
    return $d
}
$list = [Activator]::CreateInstance($listType)
$list.Add((Endpoint 'A' 0x1412 14 33 33 0))
$list.Add((Endpoint 'B' 0x1412 15 0 0 33))
Assert ($null -eq $t.GetMethod('SelectEndpoints').Invoke($null,[object[]](,$list))) 'Endpoints mixed between physical devices'
$list.Add((Endpoint 'A' 0x1412 15 0 0 33))
$list.Add((Endpoint 'A' 0x1412 2 19 19 0))
$list.Add((Endpoint 'A' 0x1412 16 0 0 33))
$set = $t.GetMethod('SelectEndpoints').Invoke($null,[object[]](,$list))
Assert ($set.Feature.Usage -eq 15 -and $set.Status.Usage -eq 2) 'Wrong Feature or status collection selected'
$list.Add((Endpoint 'C' 0x4612 14 33 33 0))
$list.Add((Endpoint 'C' 0x4612 15 0 0 33))
$list.Add((Endpoint 'C' 0x4612 2 19 19 0))
$set = $t.GetMethod('SelectEndpoints').Invoke($null,[object[]](,$list))
Assert ($set.Control.ProductId -eq 0x4612) 'Complete wired endpoint group must take precedence'
Write-Host 'PASS: VT3 status, XY, battery boundaries, Feature ACKs and physical endpoint isolation'
