# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# Exercise actual endpoint filtering and persistent cache; no HID I/O.
# Use a unique test-only registry subkey and remove only the keys this test created.
$ErrorActionPreference='Stop'
$project=(Resolve-Path "$PSScriptRoot\..").Path
$asm=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $project 'FerrisPulse.exe')))
$type=$asm.GetType('RazerBatteryTray.MchoseDeviceHelper',$true)
$flags=[Reflection.BindingFlags]'NonPublic,Static'
$script:count=0
function Check($condition,[string]$message){if(-not $condition){throw "FAIL: $message"}; $script:count++}
function Call([string]$method,$arguments){return $type.GetMethod($method,$flags).Invoke($null,$arguments)}
$descriptor=[Activator]::CreateInstance($asm.GetType('RazerBatteryTray.HidDeviceDescriptor'))
$descriptor.VendorId=0x258A; $descriptor.ProductId=0x01BB
$descriptor.UsagePage=0xFF02; $descriptor.Usage=2
$descriptor.InputReportLength=20; $descriptor.OutputReportLength=20
$descriptor.InstanceId='FerrisPulse.Tests.Z75S.'+[Guid]::NewGuid().ToString('N')
$descriptor.DevicePath='test-only-no-hid'
Check (Call 'IsZ75sEndpoint' @($descriptor)) 'Select only the exact validated 20-byte vendor endpoint'
foreach($case in @(@('VendorId',0x3837),@('ProductId',0x01B9),@('UsagePage',1),@('Usage',6),@('InputReportLength',65),@('OutputReportLength',0))) {
    $field=$descriptor.GetType().GetField($case[0]); $before=$field.GetValue($descriptor)
    try { $field.SetValue($descriptor,[int]$case[1]); Check (-not (Call 'IsZ75sEndpoint' @($descriptor))) "Exclude wrong endpoint field $($case[0])" }
    finally { $field.SetValue($descriptor,$before) }
}
$parents=@('Software\RazerBatteryTray\MchoseCache\Z75S','Software\RazerBatteryTray\MchoseCache','Software\RazerBatteryTray')
$existed=@{}
foreach($name in $parents){$k=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($name); $existed[$name]=$null -ne $k; if($k){$k.Dispose()}}
$cachePath=Call 'GetZ75sCachePath' @($descriptor)
$key=$null
try {
    $info=Call 'CreateZ75sInfo' @($descriptor,$null,85,$true,$true)
    $sampleTime=[DateTime]::Parse('2026-10-26T00:00:00Z').ToUniversalTime()
    $info.LastUpdated=$sampleTime
    Call 'SaveZ75sCache' @($descriptor,$info)
    $cached=Call 'LoadZ75sCache' @($descriptor)
    Check ($cached.BatteryPercent -eq 85 -and $cached.IsCharging -and $cached.LastUpdated -eq $sampleTime -and $cached.IsSleeping) 'Persist physical battery, charging and original sample time for cold-start sleep'
    $originalId=$descriptor.InstanceId
    try {
        $descriptor.InstanceId+='-another-receiver'
        Check ($null -eq (Call 'LoadZ75sCache' @($descriptor))) 'Another receiver must never inherit battery or verified identity'
    } finally { $descriptor.InstanceId=$originalId }
    $key=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($cachePath,$true)
    $key.SetValue('ModelToken','0A0000000001')
    Check ($null -eq (Call 'LoadZ75sCache' @($descriptor))) 'Reject cached Z75 identity under shared VID/PID'
    $key.SetValue('ModelToken','0A0000000009')
    $key.SetValue('BatteryPercent',101,[Microsoft.Win32.RegistryValueKind]::DWord)
    $unknown=Call 'LoadZ75sCache' @($descriptor)
    Check ($unknown.BatteryPercent -eq -1 -and $unknown.ExactBatteryPercent -eq -1) 'Out-of-range persisted values remain unknown'
    $key.SetValue('BatteryPercent',85,[Microsoft.Win32.RegistryValueKind]::DWord)
    $key.SetValue('LastUpdated','invalid')
    Check ((Call 'LoadZ75sCache' @($descriptor)).BatteryPercent -eq -1) 'Reject cached values without a valid physical sample time'
    $key.Dispose(); $key=$null
    foreach($percent in @(0,100)) {
        $sample=Call 'CreateZ75sInfo' @($descriptor,$null,$percent,$false,$true)
        Call 'SaveZ75sCache' @($descriptor,$sample)
        $loaded=Call 'LoadZ75sCache' @($descriptor)
        Check ($loaded.BatteryPercent -eq $percent -and -not $loaded.IsCharging) "Persist exact boundary $percent%"
    }
} finally {
    if($key){$key.Dispose()}
    [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($cachePath,$false)
    foreach($name in $parents) {
        if($existed[$name]){continue}
        $k=[Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($name)
        if($k) {
            try{$empty=$k.SubKeyCount -eq 0 -and $k.ValueCount -eq 0}finally{$k.Dispose()}
            if($empty){[Microsoft.Win32.Registry]::CurrentUser.DeleteSubKey($name,$false)}
        }
    }
}
Write-Host "PASS: Z75S endpoint and cache ($script:count assertions)"
