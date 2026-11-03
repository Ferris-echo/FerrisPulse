# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# C# 5 / .NET 4.0; UTF-8 BOM; byte-array assembly loading only.
# Rapoo MT760 office series (24AE:1870) protocol fixtures, endpoint isolation and capability contract.
$ErrorActionPreference = 'Stop'
$a = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '..\FerrisPulse.exe')))
$t = $a.GetType('RazerBatteryTray.RapooDeviceHelper')
if ($null -eq $t) { throw 'Missing RapooDeviceHelper MT760 extension' }
function Assert($ok, $message) { if (!$ok) { throw $message } }

$req = $t.GetMethod('BuildOfficeBatteryRequest').Invoke($null, $null)
Assert ($req.Length -eq 32) 'Battery request must be 32 bytes'
Assert ($req[0] -eq 0xBA -and $req[1] -eq 0xA5 -and $req[2] -eq 0xAA) 'Battery request must be BA A5 AA'

function Parse-Batt([byte[]]$data) {
    $args = [object[]]@($data, 0, $false, $false)
    $ok = $t.GetMethod('TryParseOfficeBatteryReply').Invoke($null, $args)
    return @{ Ok=$ok; Batt=[int]$args[1]; Chg=[bool]$args[2]; Unknown=[bool]$args[3] }
}

# Live capture from the attached MT760: 01 01 59 => discharging 89%.
$r = Parse-Batt ([byte[]]@(0x01,0x01,0x59))
Assert ($r.Ok -and $r.Batt -eq 89 -and !$r.Chg -and !$r.Unknown) 'Live 89% discharging frame parsed incorrectly'
$r = Parse-Batt ([byte[]]@(0x01,0x02,0x64))
Assert ($r.Ok -and $r.Batt -eq 100 -and $r.Chg -and !$r.Unknown) 'Charging/full frame parsed incorrectly'
$r = Parse-Batt ([byte[]]@(0x01,0x02,0x7F))
Assert ($r.Ok -and $r.Batt -eq -1 -and $r.Chg -and $r.Unknown) 'Charging with unknown level must be explicit, not 100%'
foreach ($bad in @([byte[]]@(0x02,0x01,0x59),[byte[]]@(0x01,0x01,0x00),[byte[]]@(0x01,0x01,0x65),[byte[]]@(0x01,0x01))) {
    Assert (!(Parse-Batt $bad).Ok) 'Malformed or busy battery frame accepted'
}
Assert (!(Parse-Batt $null).Ok) 'Null battery frame accepted'

$dpi = New-Object byte[] 32
$dpi[0] = 0x01
$levels = @(0x10,0x14,0x18,0x20,0x30,0x40,0x50)
for ($i = 0; $i -lt 7; $i++) { $dpi[4 + $i*2] = $levels[$i]; $dpi[5 + $i*2] = 0 }
$dpi[18] = 6; $dpi[19] = 0; $dpi[20] = 2; $dpi[21] = 0; $dpi[22] = 2; $dpi[23] = 3; $dpi[24] = 0x10; $dpi[25] = 0; $dpi[26] = 0x14; $dpi[27] = 0

function Parse-Dpi([byte[]]$data) {
    $args = [object[]]@($data, $null, 0, 0)
    $ok = $t.GetMethod('TryParseOfficeDpiConfig').Invoke($null, $args)
    return @{ Ok=$ok; Stages=$args[1]; Count=[int]$args[2]; Current=[int]$args[3] }
}

$r = Parse-Dpi $dpi
Assert $r.Ok 'Live DPI configuration frame rejected'
Assert ($r.Count -eq 7 -and $r.Current -eq 3) 'DPI stage count/current stage mapped incorrectly'
$expected = @(800,1000,1200,1600,2400,3200,4000)
Assert ($r.Stages.Length -eq 7) 'DPI stage array length wrong'
for ($i = 0; $i -lt 7; $i++) { Assert ($r.Stages[$i] -eq $expected[$i]) 'DPI stage value wrong' }

$badDpi = [byte[]]$dpi.Clone(); $badDpi[18] = 7
Assert (!(Parse-Dpi $badDpi).Ok) 'Eight stage count accepted'
$badDpi = [byte[]]$dpi.Clone(); $badDpi[20] = 7
Assert (!(Parse-Dpi $badDpi).Ok) 'Current stage beyond count accepted'
$badDpi = [byte[]]$dpi.Clone(); $badDpi[4] = 0
Assert (!(Parse-Dpi $badDpi).Ok) 'Zero DPI stage accepted'
$badDpi = [byte[]]$dpi.Clone(); $badDpi[0] = 0x02
Assert (!(Parse-Dpi $badDpi).Ok) 'Busy DPI response accepted'
Assert (!(Parse-Dpi ([byte[]]@(0x01,0x00))).Ok) 'Truncated DPI response accepted'

$rate = New-Object byte[] 32
$rate[0] = 0x01; $rate[4] = 0x02; $rate[5] = 0x01; $rate[6] = 0xFF; $rate[7] = 0xFF; $rate[8] = 0x01; $rate[9] = 0x02; $rate[10] = 0x00; $rate[11] = 0x03

function Parse-Rate([byte[]]$data) {
    $args = [object[]]@($data, 0)
    $ok = $t.GetMethod('TryParseOfficePollingRate').Invoke($null, $args)
    return @{ Ok=$ok; Hz=[int]$args[1] }
}

$r = Parse-Rate $rate
Assert ($r.Ok -and $r.Hz -eq 1000) 'Wireless 1000Hz block parsed incorrectly'
$wired = [byte[]]$rate.Clone(); $wired[5] = 0x00
$r = Parse-Rate $wired
Assert ($r.Ok -and $r.Hz -eq 500) 'Wired-only 500Hz block parsed incorrectly'
$unsupported = [byte[]]$rate.Clone(); $unsupported[4] = 0x84; $unsupported[5] = 0x84
Assert (!(Parse-Rate $unsupported).Ok) 'Unverified 2000Hz block must not be exposed'
$busyRate = [byte[]]$rate.Clone(); $busyRate[0] = 0x02
Assert (!(Parse-Rate $busyRate).Ok) 'Busy rate response accepted'

$caps = $t.GetMethod('IsOfficeEndpointCaps')
Assert ($caps.Invoke($null, @(0x24AE,0x1870,0xFF00,0x0E,32,32,0))) 'FF00:000E 32/32 endpoint rejected'
Assert (!($caps.Invoke($null, @(0x24AE,0x1870,0xFF00,0x02,7,7,0)))) 'FF00:0002 7B telemetry must be isolated'
Assert (!($caps.Invoke($null, @(0x24AE,0x1870,0xFF01,0x00,29,29,29)))) 'FF01:0000 29B keyboard channel must be isolated'
Assert (!($caps.Invoke($null, @(0x24AE,0x1412,0xFF00,0x0E,32,32,0)))) 'Gaming PID must not match office endpoint'

$drvType = $a.GetType('RazerBatteryTray.RapooOfficeMouseDriver')
Assert ($null -ne $drvType) 'RapooOfficeMouseDriver is not registered'
$drv = [Activator]::CreateInstance($drvType)
Assert ($drv.DriverId -eq 'BuiltIn:RapooOfficeMouse') 'Wrong driver id'
Assert ($drv.MatchesVidPid(0x24AE,0x1870)) 'Office VID:PID not matched'
Assert (!($drv.MatchesVidPid(0x24AE,0x1412))) 'Gaming VID:PID must not match office driver'

$dev = [Activator]::CreateInstance($a.GetType('RazerBatteryTray.MouseBatteryInfo'))
$dev.Brand = 'Rapoo'
$dev.DeviceId = 'Rapoo:24AE:1870'
$dev.Category = [Enum]::Parse($a.GetType('RazerBatteryTray.DeviceCategory'), 'Mouse')
$drv.PopulateCapabilities($dev)
Assert ($dev.Capabilities.SupportsBattery) 'Battery capability missing'
Assert ($dev.Capabilities.SupportsDpi) 'DPI read/write capability missing'
Assert ($dev.Capabilities.SupportsDpiStages) 'DPI stage capability missing'
Assert ($dev.Capabilities.SupportsPollingRate) 'Polling rate capability missing'
Assert ($dev.Capabilities.SupportsHardwareDpiMonitor) 'Hardware DPI monitor capability missing'
Assert (!($dev.Capabilities.SupportsXyIndependence)) 'MT760 has no XY independence; capability must stay false'
Assert ($dev.Capabilities.MinDpi -eq 50 -and $dev.Capabilities.MaxDpi -eq 4000 -and $dev.Capabilities.DpiStep -eq 50) 'DPI range contract wrong'
Assert ($dev.Capabilities.SupportedPollingRates.Length -eq 4) 'Polling rate contract wrong'
Assert ($dev.Capabilities.SupportedPollingRates[0] -eq 125 -and $dev.Capabilities.SupportedPollingRates[3] -eq 1000) 'Polling rate values wrong'
Assert ($drv.CanHandle($dev)) 'Office device rejected by driver'

$dev2 = [Activator]::CreateInstance($a.GetType('RazerBatteryTray.MouseBatteryInfo'))
$dev2.Brand = 'Rapoo'
$dev2.DeviceId = 'Rapoo:24AE:1412'
$dev2.Category = [Enum]::Parse($a.GetType('RazerBatteryTray.DeviceCategory'), 'Mouse')
Assert (!($drv.CanHandle($dev2))) 'Gaming device must remain on the existing Rapoo driver'

Write-Host 'PASS: Rapoo MT760 battery/DPI/rate parsing, endpoint isolation and capability contract'
