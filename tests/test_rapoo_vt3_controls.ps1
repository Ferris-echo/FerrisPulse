# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# UTF-8 BOM; test protocol validation without hardware writes.
$ErrorActionPreference='Stop'
$a=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '..\FerrisPulse.exe')))
$t=$a.GetType('RazerBatteryTray.RapooDeviceHelper')
$p=$a.GetType('RazerBatteryTray.RapooProtocol')
function Assert($ok,$message) { if (!$ok) { throw $message } }
Assert ($t.GetMethod('DecodeRapooPollingRate').Invoke($null,@([byte]0xFF)) -eq 0) 'Unknown polling code must not claim 1000Hz'
$rates=@{125=8;250=4;500=2;1000=1;2000=0x84;4000=0x82;8000=0x81}
foreach ($entry in $rates.GetEnumerator()) {
    $encoded=$t.GetMethod('EncodeRapooPollingRate').Invoke($null,@([int]$entry.Key))
    Assert ($encoded -eq $entry.Value) 'Polling encode mismatch'
    Assert ($t.GetMethod('DecodeRapooPollingRate').Invoke($null,@([byte]$encoded)) -eq $entry.Key) 'Polling decode mismatch'
}
$v=$p.GetMethod('IsValidDpiConfiguration')
Assert ($null -ne $v) 'DPI configuration validator missing'
Assert ($v.Invoke($null,@(1,[int[]]@(800,1600),[int[]]@(600,1200)))) 'Valid independent XY configuration rejected'
foreach ($case in @(
    [object[]]@(0,[int[]]@(800),[int[]]@(800)),
    [object[]]@(2,[int[]]@(800),[int[]]@(800)),
    [object[]]@(1,[int[]]@(49),[int[]]@(800)),
    [object[]]@(1,[int[]]@(800),[int[]]@(65536)),
    [object[]]@(1,[int[]]@(800,1600),[int[]]@(800)),
    [object[]]@(1,[int[]]@(800,800,800,800,800,800,800),[int[]]@(800,800,800,800,800,800,800))
)) { Assert (!$v.Invoke($null,$case)) 'Unsafe or truncated DPI configuration accepted' }
Assert (!$t.GetMethod('SetRapooPollingRate').Invoke($null,@(123))) 'Invalid polling rate accepted'
Assert (!$t.GetMethod('SetRapooDpiStage').Invoke($null,@(0))) 'Invalid stage accepted'
Assert (!$t.GetMethod('SetRapooXyDpi').Invoke($null,@(65536,800,1))) 'Overflow DPI accepted'
Write-Host 'PASS: VT3 polling mappings, unknown code and DPI/write validation'
