$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '../FerrisPulse.exe')))
function Assert($condition, $message) { if (-not $condition) { throw $message } }
$helper = $assembly.GetType('RazerBatteryTray.AtkDeviceHelper', $true)
Assert ($null -ne $helper.GetMethod('DecodeLightingConfig')) 'Lighting decoder must exist'
[byte[]]$config = @(0,20,2,3,0,0,255,129,155,3,0,0,0,180,0,255,255,255,0,0,1,0,0,0,0,0,1,0,0,1,4,1,0,0,1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0)
$settings = [RazerBatteryTray.AtkDeviceHelper]::DecodeLightingConfig($config)
Assert ($settings.Mode -eq 20 -and $settings.Enabled -and $settings.Brightness -eq 2 -and $settings.Speed -eq 1) 'Recorded configuration and inverse speed'
Assert ($settings.Red -eq 255 -and $settings.Green -eq 129 -and $settings.Blue -eq 155) 'Recorded RGB'
$settings.Mode=5; $settings.Enabled=$false; $settings.Brightness=1; $settings.Speed=4
$settings.Red=12; $settings.Green=34; $settings.Blue=56; $settings.Rainbow=$true
$updated=[RazerBatteryTray.AtkDeviceHelper]::PatchLightingConfig($config,$settings)
Assert ($updated[1] -eq 5 -and $updated[2] -eq 1 -and $updated[3] -eq 0 -and $updated[5] -eq 255 -and $updated[28] -eq 1) 'Lighting patch and global switch'
foreach ($index in 0..55) {
    if ($index -notin @(1,2,3,5,6,7,8,28)) { Assert ($config[$index] -eq $updated[$index]) ('Preserve unrelated byte '+$index) }
}
Assert ($config[1] -eq 20 -and $config[28] -eq 0) 'Never mutate caller snapshot'
$settings.Mode=6
$steady=[RazerBatteryTray.AtkDeviceHelper]::PatchLightingConfig($config,$settings)
Assert ($steady[3] -eq $config[3]) 'Steady effect has no speed setting'
$settings.Mode=11
$fixed=[RazerBatteryTray.AtkDeviceHelper]::PatchLightingConfig($config,$settings)
foreach ($index in 5..8) { Assert ($fixed[$index] -eq $config[$index]) 'Effect without color must preserve RGB and rainbow' }
$packet=[RazerBatteryTray.AtkDeviceHelper]::BuildLightingReport(0x1234,6,24,24,$updated)
Assert ($packet.Length -eq 64 -and $packet[0] -eq 4 -and $packet[1] -eq 0x34 -and $packet[2] -eq 0x12 -and $packet[3] -eq 6 -and $packet[4] -eq 24 -and $packet[5] -eq 24 -and $packet[12] -eq 1) 'Write packet offset and global switch'
Assert ([RazerBatteryTray.AtkDeviceHelper]::IsLightingResponse($packet,0x1234,6,24,24)) 'Matching response'
Assert (-not [RazerBatteryTray.AtkDeviceHelper]::IsLightingResponse($packet,0x1235,6,24,24)) 'Reject stale response'
Assert (-not [RazerBatteryTray.AtkDeviceHelper]::IsLightingResponse($packet,0x1234,6,0,24)) 'Reject wrong chunk'
$packet[7]=1
Assert (-not [RazerBatteryTray.AtkDeviceHelper]::IsLightingResponse($packet,0x1234,6,24,24)) 'Reject device error'
Assert (-not [RazerBatteryTray.AtkDeviceHelper]::IsLightingResponse([byte[]]@(4,52,18,5,24,0,0,0),0x1234,5,0,24)) 'Reject truncated read'
foreach ($mode in 1..20) {
    Assert ([RazerBatteryTray.AtkDeviceHelper]::SupportsLightingSpeed($mode) -eq ($mode -le 15 -and $mode -ne 6)) 'Official speed capability'
    Assert ([RazerBatteryTray.AtkDeviceHelper]::SupportsLightingColor($mode) -eq ($mode -in @(1,2,3,5,6,7,8,9,10,13,14,15,16))) 'Official color capability'
}
foreach ($property in @('Mode','Brightness','Speed')) {
    $invalid=[RazerBatteryTray.AtkDeviceHelper]::DecodeLightingConfig($config)
    $invalid.$property=255
    $rejected=$false
    try { [RazerBatteryTray.AtkDeviceHelper]::PatchLightingConfig($config,$invalid) | Out-Null } catch { $rejected=$true }
    Assert $rejected ('Reject invalid '+$property)
}
Assert ([RazerBatteryTray.AtkDeviceHelper]::SupportsLightingDevice('ATK:320F:5088')) 'Confirmed receiver'
Assert (-not [RazerBatteryTray.AtkDeviceHelper]::SupportsLightingDevice('ATK:320F:5055')) 'Untested wired writes excluded'
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot '../src/Hardware/AtkDeviceHelper.Lighting.cs'))
Assert ($source.Contains('offset < 56; offset += 24') -and $source.Contains('write(original)')) 'Flash erase requires complete 24/24/8-byte writes and rollback'
$commit=$helper.GetMethod('CommitLightingConfig',[Reflection.BindingFlags]'NonPublic,Static')
$store=[pscustomobject]@{Config=[byte[]]$config.Clone();Writes=0;FailOnce=$false;CorruptOnce=$false;FailAlways=$false}
$write=[Action[byte[]]]{
    param($value)
    $store.Writes++
    if ($store.FailAlways) { throw 'Disconnected device' }
    if ($store.FailOnce) { $store.FailOnce=$false; $store.Config=New-Object byte[] 56; throw 'Simulated partial page write' }
    $store.Config=[byte[]]$value.Clone()
}
$read=[Func[byte[]]]{
    if ($store.CorruptOnce) { $store.CorruptOnce=$false; return ,(New-Object byte[] 56) }
    return ,$store.Config
}
$null=$commit.Invoke($null,@($config,$updated,$write,$read))
Assert ($store.Writes -eq 1 -and [BitConverter]::ToString($store.Config) -eq [BitConverter]::ToString($updated)) 'Commit verified snapshot'
$store.Writes=0
$null=$commit.Invoke($null,@($updated,$updated,$write,$read))
Assert ($store.Writes -eq 0) 'Unchanged settings must not wear flash'
foreach ($failure in @('FailOnce','CorruptOnce')) {
    $store.Config=[byte[]]$config.Clone(); $store.Writes=0; $store.$failure=$true
    $rejected=$false
    try { $null=$commit.Invoke($null,@($config,$updated,$write,$read)) } catch { $rejected=$_.Exception.ToString().Contains('已恢复原配置') }
    Assert ($rejected -and $store.Writes -eq 2 -and [BitConverter]::ToString($store.Config) -eq [BitConverter]::ToString($config)) 'Partial write / readback mismatch must restore complete original'
}
$store.FailAlways=$true
$rejected=$false
try { $null=$commit.Invoke($null,@($config,$updated,$write,$read)) } catch { $rejected=$_.Exception.ToString().Contains('无法确认原配置已恢复') }
Assert $rejected 'Never report successful restoration after disconnect'
Write-Host 'PASS: ATK lighting framing, capabilities, RGB, speed, switch, preservation and validation'