# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# Reviewer regressions: device switch, generic receiver identity and failed-setting UI.
param([ValidateSet('All','UI','Identity','Stage')][string]$Section='All')
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$a=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '..\FerrisPulse.exe')))
$h=$a.GetType('RazerBatteryTray.RapooDeviceHelper')
$static=[Reflection.BindingFlags]'NonPublic,Static'
function Assert($ok,$message) { if (!$ok) { throw $message } }
# This breaks if a failed setter is once again published as a confirmed UI value.
if ($Section -eq 'All' -or $Section -eq 'UI') {
$t=$a.GetType('RazerBatteryTray.MainForm'); $flags=[Reflection.BindingFlags]'NonPublic,Instance'
$f=[Activator]::CreateInstance($t,@($false)); $null=$f.Handle
try {
    $info=[Activator]::CreateInstance($a.GetType('RazerBatteryTray.MouseBatteryInfo'))
    $info.Brand='Rapoo'; $info.DeviceId='Rapoo:24AE:1412'; $info.Dpi=800; $info.DpiY=600; $info.PollingRate=1000
    $info.DpiStage=1; $info.DpiStages=[int[]]@(800,1600); $info.DpiStagesY=[int[]]@(600,1200)
    $t.GetField('selectedDevice',$flags).SetValue($f,$info)
    $t.GetField('dpiStageValues',$flags).SetValue($f,[int[]]@(800,1600))
    $f.SetDpiFromUI(35) # invalid transport DPI, returns false before any HID operation
    $until=[DateTime]::UtcNow.AddMilliseconds(500)
    do { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 10 } while ([DateTime]::UtcNow -lt $until)
    Assert ($info.Dpi -eq 800 -and $info.DpiY -eq 600) 'Rejected DPI was falsely published to the UI/OSD'
    $t.GetField('selectedDevice',$flags).SetValue($f,$info)
    $f.SetRapooPollingRateManual(123) # invalid Hz, no HID operation
    Assert ($info.PollingRate -eq 1000) 'Rejected polling rate was falsely published'
    $t.GetField('selectedDevice',$flags).SetValue($f,$info)
    $h.GetField('CachedDpiStageCount').SetValue($null,0) # no known hardware table: reject before HID
    $stages=New-Object 'System.Collections.Generic.List[int]'
    $stages.Add(800); $stages.Add(1600)
    $t.GetField('dpiStageList',$flags).SetValue($f,$stages)
    [void]$t.GetMethod('SwitchDpiStageByIndex',$flags).Invoke($f,@(1))
    $until=[DateTime]::UtcNow.AddMilliseconds(500)
    do { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 10 } while ([DateTime]::UtcNow -lt $until)
    Assert ($info.Dpi -eq 800 -and $info.DpiStage -eq 1) 'Rejected stage button setting was falsely published'
    $configure=$t.GetMethod('ApplyRapooDpiConfigurationFromUI',$flags)
    Assert ($null -ne $configure) 'DPI management dialog must use confirmed configuration publication'
    [void]$configure.Invoke($f,@($info,1,[int[]]@(35),[int[]]@(35),$false,1.0))
    $until=[DateTime]::UtcNow.AddMilliseconds(500)
    do { [Windows.Forms.Application]::DoEvents(); Start-Sleep -Milliseconds 10 } while ([DateTime]::UtcNow -lt $until)
    Assert ($info.Dpi -eq 800 -and $info.DpiStages[0] -eq 800) 'Rejected DPI table was falsely published'

} finally { $f.Dispose() }
}
if ($Section -eq 'All' -or $Section -eq 'Identity') {
$r=$h.GetMethod('ResolveRapooProductId')
Assert ($null -ne $r) 'Generic receiver effective-PID resolution missing'
foreach ($receiver in @(0x14FF,0x1500,0x1501)) {
    Assert ($r.Invoke($null,@($receiver,0x1412)) -eq 0x1412) 'Generic receiver lost its paired mouse identity'
    Assert ($r.Invoke($null,@($receiver,0)) -eq $receiver) 'Invalid effective PID replaced physical identity'
}
Assert ($r.Invoke($null,@(0x1412,0x1417)) -eq 0x1412) 'Dedicated VT3 identity overwritten by unrelated register value'
}
if ($Section -eq 'All' -or $Section -eq 'Stage') {
# This breaks if a confirmed write uses arrays invalidated by an endpoint switch.
$apply=$h.GetMethod('ApplyConfirmedRapooDpiStage',$static)
Assert ($null -ne $apply) 'Safe confirmed-stage publication missing'
$group=$h.GetField('activeRapooGroup',$static); $group.SetValue($null,'groupA')
$h.GetField('CachedDpiStages').SetValue($null,[int[]]@(800,1600))
$h.GetField('CachedDpiStagesY').SetValue($null,[int[]]@(600,1200))
$h.GetField('CachedDpiStageCount').SetValue($null,2)
Assert ($apply.Invoke($null,@('groupA',2))) 'Matching confirmed stage rejected'
Assert ($h.GetField('CachedDpi').GetValue($null) -eq 1600) 'Matching stage not applied'
[void]$h.GetMethod('ResetRapooLiveState',$static).Invoke($null,$null)
$group.SetValue($null,'groupB')
Assert (!$apply.Invoke($null,@('groupA',2))) 'Old device stage accepted after a physical switch'
Assert (!$apply.Invoke($null,@('groupB',2))) 'Empty arrays accessed after a physical switch'
Assert ($h.GetField('CachedDpi').GetValue($null) -eq 0) 'New device inherited old DPI'
}
Write-Host 'PASS: generic receiver identity, physical-switch stage safety and failed-setting UI'