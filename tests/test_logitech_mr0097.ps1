# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# MR0097 HID++ frames from a physical mouse; no hardware I/O in this test.
$ErrorActionPreference = 'Stop'
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Resolve-Path "$PSScriptRoot/../FerrisPulse.exe")))
[Environment]::CurrentDirectory=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$package=Join-Path $PSScriptRoot '../packages/logitech_mr0097.fp-driver.cs'
$compileArgs=[object[]]@([IO.File]::ReadAllText($package),$package,$null)
for($i=0;$i -lt 2;$i++) { $compileArgs[$i]=$compileArgs[$i].PSObject.BaseObject }
$drivers=$asm.GetType('RazerBatteryTray.CsScriptDriverLoader').GetMethod('CompileDriversFromSource').Invoke($null,$compileArgs)
if ($compileArgs[2].Count -or $drivers.Count -ne 1) { throw ($compileArgs[2] -join '; ') }
$type=$drivers[0].GetType().Assembly.GetType('FerrisPulse.Packages.LogitechMr0097.Protocol')
$battery = $type.GetMethod('TryReadUnifiedBattery', [Reflection.BindingFlags]'Static,NonPublic')
function Check-Battery([string]$hex, [byte]$slot, [int]$expected, [bool]$charging) {
    [byte[]]$frame = $hex.Split(' ') | ForEach-Object { [Convert]::ToByte($_,16) }
    $argsList = [object[]]@($frame,$slot,[byte]6,0,$false)
    $valid = $battery.Invoke($null,$argsList)
    if ($valid -ne ($expected -ge 0) -or $argsList[3] -ne $expected -or $argsList[4] -ne $charging) { throw "Battery: $hex" }
}
Check-Battery '11 01 06 19 59 08 00 00' 1 89 $false
Check-Battery '11 FF 06 19 3C 04 01 01' 255 60 $true
Check-Battery '11 01 06 19 3E 04 00 00' 1 62 $false
Check-Battery '11 01 06 19 00 01 00' 1 0 $false
Check-Battery '11 01 06 19 65 04 00' 1 -1 $false
Check-Battery '11 01 06 19 3E' 1 -1 $false
Check-Battery '11 02 06 19 3E 04 00' 1 -1 $false
Check-Battery '11 01 FF 06 19 05 00' 1 -1 $false
$dpi = $type.GetMethod('ReadExtendedDpi', [Reflection.BindingFlags]'Static,NonPublic')
$argsList = [object[]]@([byte[]](17,1,9,89,0,6,64,3,32,6,64,3,32,2),[byte]1,[byte]9,0,0)
$dpi.Invoke($null,$argsList)
if ($argsList[3] -ne 1600 -or $argsList[4] -ne 1600) { throw 'Default X was decoded as current Y' }
$argsList[0][10] = 114
$dpi.Invoke($null,$argsList)
if ($argsList[3] -ne 1600 -or $argsList[4] -ne 1650) { throw 'Independent Y lost' }
Write-Host 'PASS MR0097 wireless, USB charging, reconnect, invalid frames and X/Y DPI'
$profileParser = $type.GetMethod('DecodeOnboardDpiProfile', [Reflection.BindingFlags]'Static,NonPublic')
$bytes = [byte[]](3,3,0,0,32,3,32,3,2,176,4,176,4,2,64,6,64,6,2,96,9,96,9,2,128,12,128,12,2,0,0,0)
$parameters = [object[]]@($bytes, 2, [byte]13)
$profile = $profileParser.Invoke($null,$parameters)
$flags = [Reflection.BindingFlags]'Instance,Public'
foreach ($axis in 'X','Y') {
    $values = $profile.GetType().GetField($axis,$flags).GetValue($profile)
    if (($values -join ',') -ne '800,1200,1600,2400,3200') { throw "Invalid onboard $axis table" }
}
if ($profile.GetType().GetField('Stage',$flags).GetValue($profile) -ne 3) { throw 'Incorrect active slot' }
$parameters[1] = 5
if ($null -ne $profileParser.Invoke($null,$parameters)) { throw 'Invalid slot accepted' }
Write-Host 'PASS MR0097 onboard profile format 7'
