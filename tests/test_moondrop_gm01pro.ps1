# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# MOONDROP GM-01 PRO (0x35D8:0x0133) battery protocol contract.
# 协议来源：官方网页驱动 https://hub.moondroplab.tech/（WebHID）。
# 命令通道 = UsagePage 0x0C / Usage 0x0001 / In=Out=64；帧 = 4B 80 <cmd> 00 + 补零；
# 电量命令 0x27，应答 [8]=有效标志、[9]=电量百分比。
# 本测试只做纯解析/常量断言，唯一触碰硬件的是 QueryAllMoondropDevices（并容忍 -1）。
$ErrorActionPreference = 'Stop'
$project = (Resolve-Path "$PSScriptRoot\..").Path
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $project 'FerrisPulse.exe')))
$type = $asm.GetType('RazerBatteryTray.MoondropDeviceHelper', $true)
$script:assertions = 0
function Assert-True($value, [string]$message) {
    if (-not $value) { throw "FAIL: $message" }
    $script:assertions++
}
function Field([string]$name) { return $type.GetField($name).GetValue($null) }

Assert-True ($type.GetMethod('IsSupportedVidPid').Invoke($null, @(0x35D8, 0x0133))) 'GM-01 PRO must be identified'
Assert-True (-not $type.GetMethod('IsSupportedVidPid').Invoke($null, @(0x35D8, 0x0134))) 'Other MOONDROP PIDs remain untouched'
Assert-True (-not $type.GetMethod('IsSupportedVidPid').Invoke($null, @(0x41E4, 0x2004))) 'MCHOSE receivers must not be claimed'
Assert-True ($type.GetMethod('IsManagedDevice').Invoke($null, @(0x35D8, 0x0133))) 'Generic scanner must skip this device'

# --- 协议常量锁定（官方 config.reportId = 75 / st.BATTERY_LEVEL = 39）---
Assert-True ((Field 'ProtocolReportId') -eq 0x4B) 'Report ID must stay 0x4B'
Assert-True ((Field 'OpRead') -eq 0x80) 'Read opcode must stay 0x80'
Assert-True ((Field 'CmdBatteryLevel') -eq 0x27) 'Battery command must stay 0x27'
Assert-True ((Field 'ProtocolUsagePage') -eq 0x0C) 'Command channel lives on usage page 0x0C'
Assert-True ((Field 'ProtocolUsage') -eq 0x0001) 'Command channel lives on usage 0x0001'
Assert-True ((Field 'ProtocolReportLength') -eq 64) 'Command channel is 64-byte in/out'

# --- 帧构造 ---
$buildFrame = $type.GetMethod('BuildReadFrame')
[byte]$batteryCmd = 0x27
$frame = $buildFrame.Invoke($null, @($batteryCmd))
Assert-True ($frame.Length -eq 64) 'Frame must be padded to the full 64-byte report'
Assert-True ($frame[0] -eq 0x4B) 'Frame starts with Report ID'
Assert-True ($frame[1] -eq 0x80) 'Frame carries the read opcode'
Assert-True ($frame[2] -eq 0x27) 'Frame carries the battery command'
Assert-True ($frame[3] -eq 0) 'Read commands declare a zero-length payload'
for ($i = 4; $i -lt 64; $i++) { if ($frame[$i] -ne 0) { throw "FAIL: frame byte $i must be zero" } }

# --- 应答解析（实机样本：4B 80 27 06 00 00 00 00 01 5A → 90%）---
$parse = $type.GetMethod('TryParseBatteryResponse')
function Parse([byte[]]$report) {
    $args = New-Object object[] 3
    $args[0] = $report
    $args[1] = $report.Length
    $args[2] = 0
    $ok = $parse.Invoke($null, $args)
    return @{ Ok = $ok; Percent = [int]$args[2] }
}
[byte[]]$good = New-Object byte[] 64
$good[0] = 0x4B; $good[1] = 0x80; $good[2] = 0x27; $good[3] = 0x06; $good[8] = 0x01; $good[9] = 0x5A
$r = Parse $good
Assert-True ($r.Ok -and $r.Percent -eq 90) 'Real 90% sample must decode to 90'
$good[9] = 0
Assert-True ((Parse $good).Ok -and (Parse $good).Percent -eq 0) 'Zero percent is a legal reading'
$good[9] = 100
Assert-True ((Parse $good).Percent -eq 100) 'Full battery decodes to 100'
$good[9] = 101
Assert-True (-not (Parse $good).Ok) 'Out-of-range 101% must be rejected'
$good[9] = 0x5A; $good[8] = 0
Assert-True (-not (Parse $good).Ok) 'Invalid flag (0) must be rejected'
$good[8] = 0x01; $good[2] = 0x31
Assert-True (-not (Parse $good).Ok) 'A different command echo must be rejected'
$good[2] = 0x27; $good[0] = 0x4C
Assert-True (-not (Parse $good).Ok) 'A different report id must be rejected'
$good[0] = 0x4B; $good[3] = 0x03
Assert-True (-not (Parse $good).Ok) 'Short payload length must be rejected'
Assert-True (-not (Parse ([byte[]](0x4B, 0x80, 0x27, 0x06))).Ok) 'Truncated report must be rejected'
$argsNull = New-Object object[] 3
$argsNull[0] = $null; $argsNull[1] = 64; $argsNull[2] = 0
Assert-True (-not $parse.Invoke($null, $argsNull)) 'Null report must be rejected'

# --- 端点判定必须是四元组精确匹配（FF02 的 65 字节镜像端点不得被选中）---
$descType = $asm.GetType('RazerBatteryTray.HidDeviceDescriptor', $true)
function MakeDesc([int]$up, [int]$usage, [int]$inLen, [int]$outLen) {
    $d = [Activator]::CreateInstance($descType)
    $descType.GetField('UsagePage').SetValue($d, [uint16]$up)
    $descType.GetField('Usage').SetValue($d, [uint16]$usage)
    $descType.GetField('InputReportLength').SetValue($d, [int]$inLen)
    $descType.GetField('OutputReportLength').SetValue($d, [int]$outLen)
    return $d
}
$isEndpoint = $type.GetMethod('IsProtocolEndpoint')
Assert-True ($isEndpoint.Invoke($null, @((MakeDesc 0x0C 0x0001 64 64)))) '0C:0001 64/64 is the command channel'
Assert-True (-not $isEndpoint.Invoke($null, @((MakeDesc 0xFF02 0x0002 65 65)))) 'FF02 65-byte mirror endpoint must be rejected'
Assert-True (-not $isEndpoint.Invoke($null, @((MakeDesc 0xFF02 0x0002 64 64)))) 'FF02 64-byte endpoint must be rejected'
Assert-True (-not $isEndpoint.Invoke($null, @((MakeDesc 0x0C 0x0001 3 0)))) 'Consumer control collection must be rejected'
Assert-True (-not $isEndpoint.Invoke($null, @($null))) 'Null descriptor must be rejected'

# --- 通用扫描器必须真的排除该机型，否则同一条耳机会出现两条记录 ---
$genericSource = [IO.File]::ReadAllText((Join-Path $project 'src\Hardware\GenericHidHelper.cs'))
Assert-True ($genericSource.Contains('MoondropDeviceHelper.IsManagedDevice')) 'GenericHidHelper must consult the dedicated driver'
$managerSource = [IO.File]::ReadAllText((Join-Path $project 'src\Hardware\DeviceManager.cs'))
Assert-True ($managerSource.Contains('MoondropDeviceHelper.QueryAllMoondropDevices')) 'DeviceManager must register the dedicated driver'

# --- 真实设备（在位时）契约 ---
$devices = $type.GetMethod('QueryAllMoondropDevices').Invoke($null, @())
Assert-True ($null -ne $devices) 'Query must always return a list'
Assert-True ($devices.Count -le 1) 'One physical headset must map to at most one entry'
if ($devices.Count -eq 1) {
    $dev = $devices[0]
    Assert-True ($dev.DeviceId -eq 'MOONDROP:35D8:0133') 'Stable device identity'
    Assert-True ($dev.HardwareFingerprint -eq 'HID:35D8:0133') 'Fingerprint contract used by DeviceManager dedup'
    Assert-True ($dev.Category.ToString() -eq 'Headset') 'Classified as a headset'
    Assert-True ($dev.Brand -eq 'MOONDROP') 'Brand is MOONDROP, never a generic placeholder'
    Assert-True ($dev.IsConnected) 'Present hardware is reported as connected'
    Assert-True ($dev.Capabilities.SupportsBattery) 'Official WebHID protocol exposes a real battery level'
    Assert-True ($dev.BatteryPercent -eq -1 -or ($dev.BatteryPercent -ge 0 -and $dev.BatteryPercent -le 100)) 'Battery is either unknown or a sane percentage'
    Assert-True (-not $dev.IsCharging) 'No charging flag exists in the official protocol'
    if ($dev.BatteryPercent -lt 0) {
        Assert-True (-not [string]::IsNullOrEmpty($dev.CustomTooltip)) 'A failed read must explain itself instead of fabricating a value'
    }
}

# 已知的私有协议帧绝不能被这套 helper 宣称支持。
$gMouse = $asm.GetType('RazerBatteryTray.MchoseGMouseHelper')
Assert-True (-not $gMouse.GetMethod('IsSupportedVidPid').Invoke($null, @(0x35D8, 0x0133))) 'G-series mouse helper must not claim the headset'
Write-Host "PASS: MOONDROP GM-01 PRO battery protocol ($script:assertions assertions)"
