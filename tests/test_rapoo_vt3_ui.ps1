# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# UTF-8 BOM; in-memory assembly and synthetic state, no settings writes.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$assemblyPath=$env:FERRISPULSE_TEST_ASSEMBLY_PATH
if ([string]::IsNullOrEmpty($assemblyPath)) { $assemblyPath=Join-Path $PSScriptRoot '..\FerrisPulse.exe' }
$a=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes($assemblyPath))
$t=$a.GetType('RazerBatteryTray.MainForm')
$helper=$a.GetType('RazerBatteryTray.RapooDeviceHelper')
$f=[Activator]::CreateInstance($t,@($false))
$flags=[Reflection.BindingFlags]'NonPublic,Instance'
function Control($name) { return $t.GetField($name,$flags).GetValue($f) }
function Assert($ok,$message) { if (!$ok) { throw $message } }
try {
    $info=[Activator]::CreateInstance($a.GetType('RazerBatteryTray.MouseBatteryInfo'))
    $info.Brand='Rapoo'; $info.DeviceId='Rapoo:24AE:1412'; $info.DeviceName='雷柏 VT3'
    $info.Transport='2.4G 无线'; $info.IsConnected=$true; $info.IsSleeping=$true
    $info.BatteryPercent=0; $info.LastUpdated=[DateTime]::Now; $info.Dpi=800; $info.DpiY=600
    $info.DpiStage=1; $info.DpiStageCount=1; $info.DpiStages=[int[]]@(800); $info.DpiStagesY=[int[]]@(600)
    $info.Category=[Enum]::Parse($a.GetType('RazerBatteryTray.DeviceCategory'),'Mouse')
    $helper.GetField('CachedBatteryPercent').SetValue($null,0)
    [void]$t.GetMethod('UpdateUI',$flags).Invoke($f,@($info,$false))
    Assert ((Control 'lblBatteryBig').Text -eq '0%') 'Sleeping VT3 lost real zero battery'
    $info.BatteryPercent=-1; $info.LastUpdated=[DateTime]::MinValue
    $helper.GetField('CachedBatteryPercent').SetValue($null,-1)
    [void]$t.GetMethod('UpdateUI',$flags).Invoke($f,@($info,$false))
    Assert ((Control 'lblBatteryBig').Text -eq '--') 'Unknown VT3 battery fabricated'
    Assert ((Control 'lblUpdateTime').Text.Contains('尚未取得电量')) 'Unknown VT3 sampling time shown as a real timestamp'
    $info.Dpi=1600; $info.DpiY=1600; $info.DpiStage=4; $info.DpiStageCount=6
    $info.DpiStages=[int[]]@(400,800,1200,1600,3200,6400); $info.DpiStagesY=[int[]]@(400,800,1200,1600,3200,6400)
    [void]$t.GetMethod('UpdateUI',$flags).Invoke($f,@($info,$false))
    $buttons=Control 'btnDpiStages'
    Assert ($buttons.Length -eq 6 -and $buttons[5].Text -eq '6400') 'Sixth hardware DPI stage missing from main UI'
    Assert ($buttons[3].Selected -and !$buttons[2].Selected) '1600 DPI status highlighted the wrong hardware stage'
    $dialogType=$a.GetType('RazerBatteryTray.DpiManagementDialog')
    $capType=$a.GetType('RazerBatteryTray.RazerDeviceHelper+RazerDpiCapability')
    $cap=[Activator]::CreateInstance($capType)
    $ctor=$dialogType.GetConstructor([Type[]]@([string],[int[]],[int[]],[bool],[double],[int],$capType,[single],[int]))
    $dialog=$ctor.Invoke([object[]]@('VT3',[int[]]@(400,800,1200,1600,3200,6400),[int[]]@(400,800,1200,1600,3200,6400),$false,1.0,3,$cap,[single]1.0,6))
    try {
        $stageValues=$dialogType.GetField('stageValuesX',[Reflection.BindingFlags]'NonPublic,Instance').GetValue($dialog)
        Assert ($stageValues.Count -eq 6) 'DPI management dialog truncated the VT3 hardware profile'
    } finally { $dialog.Dispose() }
    $t.GetField('isGamingActive',$flags).SetValue($f,$false)
    $t.GetField('autoGameRateEnabled',$flags).SetValue($f,$false)
    $t.GetField('desktopPollingRate',$flags).SetValue($f,1000)
    $info.PollingRate=2000
    $info.SupportedPollingRates=[int[]]@(125,250,500,1000,2000,4000,8000)
    [void]$t.GetMethod('UpdateUI',$flags).Invoke($f,@($info,$false))
    $rateValues=$t.GetField('pollingRateValues',$flags).GetValue($f)
    $rateButtons=Control 'btnRates'
    $activeRateIndex=[Array]::IndexOf($rateValues,2000)
    Assert ($activeRateIndex -ge 0 -and $t.GetField('desktopPollingRate',$flags).GetValue($f) -eq 2000) 'VT3 hardware polling rate did not synchronize desktop selection'
    Assert ($rateButtons[$activeRateIndex].Selected) 'VT3 UI did not highlight the hardware-reported polling rate'
    $t.GetField('desktopPollingRate',$flags).SetValue($f,1000)
    $info.PollingRate=1000
    [void]$t.GetMethod('ApplyRapooPollingRateFromHardware',$flags).Invoke($f,@(2000))
    Assert ($info.PollingRate -eq 2000 -and $t.GetField('desktopPollingRate',$flags).GetValue($f) -eq 2000) 'Live VT3 polling-rate change did not immediately update the selected device and desktop profile'
    $t.GetField('isGamingActive',$flags).SetValue($f,$true)
    $t.GetField('autoGameRateEnabled',$flags).SetValue($f,$true)
    $t.GetField('desktopPollingRate',$flags).SetValue($f,1000)
    [void]$t.GetMethod('ApplyRapooPollingRateFromHardware',$flags).Invoke($f,@(4000))
    Assert ($info.PollingRate -eq 4000 -and $t.GetField('desktopPollingRate',$flags).GetValue($f) -eq 1000) 'Live hardware rate overwrote the desktop profile during automatic game-rate control'
    Write-Host 'PASS: VT3 zero/unknown battery and sampling time UI'
} finally { $f.Dispose() }