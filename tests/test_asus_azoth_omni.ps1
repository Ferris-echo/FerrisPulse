# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# Captured Azoth pairing/power/NAK, with synthetic malformed and isolation cases.
$ErrorActionPreference = 'Stop'
[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '../FerrisPulse.exe'))) | Out-Null
function Check($ok,$message) { if (-not $ok) { throw $message } }
function Packet([byte[]]$prefix) { $r = New-Object byte[] 64; [Array]::Copy($prefix,$r,$prefix.Length); return ,$r }
$pair = Packet @(1,0xA0,0,1,0,0x85,0x1A,2,4)
Check ([RazerBatteryTray.AsusDeviceHelper]::ParseAzothPairing($pair) -eq 0x1A85) 'Captured paired Azoth'
$power = Packet @(2,0x12,1,0,0,0,0x5F,2,0,0,0x1E,0x5F,0x9B,0x10)
$b = -1; $c = $false
Check ([RazerBatteryTray.AsusDeviceHelper]::TryParseBattery($power,[ref]$b,[ref]$c) -and $b -eq 95 -and -not $c) 'Captured 95%'
foreach ($v in @(0,100)) { $r=$power.Clone(); $r[6]=$v; Check ([RazerBatteryTray.AsusDeviceHelper]::TryParseBattery($r,[ref]$b,[ref]$c) -and $b -eq $v) 'Boundary' }
foreach ($i in @(0,1,2,5,6,9)) { $r=$power.Clone(); $r[$i]=255; Check (-not [RazerBatteryTray.AsusDeviceHelper]::TryParseBattery($r,[ref]$b,[ref]$c)) "Reject field $i" }
Check (-not [RazerBatteryTray.AsusDeviceHelper]::TryParseBattery((Packet @(2,255,170)),[ref]$b,[ref]$c)) 'Captured sleeping NAK'
Check (-not [RazerBatteryTray.AsusDeviceHelper]::TryParseBattery((New-Object byte[] 9),[ref]$b,[ref]$c)) 'Short reply'
Check ([RazerBatteryTray.AsusDeviceHelper]::ParseAzothPairing((Packet @(1,0xA0,0,1,0,0x94,0x1A,2,4))) -eq 0) 'Mouse not Azoth'
$d = New-Object RazerBatteryTray.HidDeviceDescriptor
$d.VendorId=0x0B05; $d.ProductId=0x1ACE; $d.UsagePage=0xFF00; $d.Usage=1; $d.InputReportLength=64; $d.OutputReportLength=64
Check ([RazerBatteryTray.AsusDeviceHelper]::IsKeyboardEndpoint($d)) 'Keyboard channel'
$d.UsagePage=0xFF01
Check (-not [RazerBatteryTray.AsusDeviceHelper]::IsKeyboardEndpoint($d)) 'Mouse channel isolation'
$list = New-Object 'System.Collections.Generic.List[RazerBatteryTray.MouseBatteryInfo]'
$kb = New-Object RazerBatteryTray.MouseBatteryInfo
$kb.DeviceId='ASUS:0B05:1ACE:KB:instance'; $kb.Category=[RazerBatteryTray.DeviceCategory]::Keyboard
$list.Add($kb)
$cfg=New-Object RazerBatteryTray.CustomDeviceConfig
$cfg.DeviceId='HID:0B05:1ACE'; $cfg.Category=[RazerBatteryTray.DeviceCategory]::Dongle; $cfg.CustomName='ROG OMNI RECEIVER'
Check ([RazerBatteryTray.AsusDeviceHelper]::MergeLegacyKeyboardConfig($list,$cfg)) 'Receiver migration'
Check ($kb.Category -eq [RazerBatteryTray.DeviceCategory]::Keyboard -and -not $kb.CustomName) 'No stale receiver label/category'
$cfg.CustomName='工作键盘'
Check ([RazerBatteryTray.AsusDeviceHelper]::MergeLegacyKeyboardConfig($list,$cfg) -and $kb.CustomName -eq '工作键盘') 'Preserve user alias'
$cfg.Category=[RazerBatteryTray.DeviceCategory]::Mouse
Check (-not [RazerBatteryTray.AsusDeviceHelper]::MergeLegacyKeyboardConfig($list,$cfg)) 'Do not consume mouse config'
Check (-not [RazerBatteryTray.UniversalGamingDeviceHelper]::IsSupportedGamingDevice(0x0B05,'hid#vid_0b05&pid_1ace','ROG OMNI RECEIVER')) 'No generic chip probes'
Write-Host 'PASS: Azoth captures, parser bounds, channel isolation and legacy receiver migration'