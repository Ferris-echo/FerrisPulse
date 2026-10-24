# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# Verify existing UI contract for ASUS; memory-load only, dispose form in finally.
$ErrorActionPreference='Stop'
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
$root=(Resolve-Path "$PSScriptRoot\..").Path
$asm=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'FerrisPulse.exe')))
$t=$asm.GetType('RazerBatteryTray.MainForm')
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
$form=[Activator]::CreateInstance($t,@($false))
function Assert($ok,$why) { if (-not $ok) { throw "FAIL: $why" } }
function Control($name) { return $t.GetField($name,$flags).GetValue($form) }
try {
    $null=$form.Handle
    $desc=[Activator]::CreateInstance($asm.GetType('RazerBatteryTray.HidDeviceDescriptor'))
    $desc.VendorId=0x0B05; $desc.ProductId=0x1B84
    $helper=$asm.GetType('RazerBatteryTray.AsusDeviceHelper')
    $build=$helper.GetMethod('BuildDeviceInfo',[Reflection.BindingFlags]'Static,NonPublic')
    $update=$t.GetMethod('UpdateUI',$flags)
    foreach ($level in @(39,0,100)) {
        $info=$build.Invoke($null,@($desc,$level,$false,$true,[DateTime]::Now))
        $update.Invoke($form,@($info,$false)) | Out-Null
        Assert ((Control 'lblBatteryBig').Text -eq ($level.ToString()+'%')) 'main form shows exact headset percent including zero'
        Assert ((Control 'lblDeviceName').Text -match 'PELTA') 'main form uses headset name'
        Assert (-not $t.GetMethod('IsSelectedDeviceSupportingAutoGameRate').Invoke($form,@())) 'no mouse optimization on headset'
    }
    $info.IsCharging=$true
    $update.Invoke($form,@($info,$false)) | Out-Null
    Assert ((Control 'pillStatus').Text -match '充电') 'main form shows headset charging'
    Assert ($null -ne $info.PSObject.Properties['IsChargingUnknown']) 'model must distinguish unknown charging from discharging'
    $info.IsCharging=$false; $info.IsChargingUnknown=$true
    $info.IsPrimary=$true
    $t.GetField('lastInfo',$flags).SetValue($form,$info)
    $update.Invoke($form,@($info,$false)) | Out-Null
    Assert ((Control 'pillStatus').Text -match '充电状态未知') 'partial sample has visible unknown charging state'
    $t.GetMethod('UpdateStatusDisplay',$flags).Invoke($form,@()) | Out-Null
    Assert ((Control 'statusMenuItem').Text -match '充电状态未知') 'tray does not misreport unknown as discharging'
    $desc.ProductId=0x1AB5
    $info=$build.Invoke($null,@($desc,-1,$false,$true,[DateTime]::Now))
    $update.Invoke($form,@($info,$false)) | Out-Null
    Assert ((Control 'lblDeviceName').Text -match 'SCOPE II RX') 'wired keyboard identified'
    Assert ((Control 'lblBatteryBig').Text -notmatch '-1|100%') 'no fabricated wired battery'
    Write-Host 'PASS: ASUS main form battery, charging, keyboard and capability binding'
} finally { $form.Dispose() }