$ErrorActionPreference = 'Stop'
# ============================================================================
# 测试脚本: test_atk_a87pro.ps1
# 目标: 验证 ATK A87 PRO 键盘 (373B:111F) 的 FDAS v2.0 包 + 核心驱动
#       AtkA87ProHelper 的真实抓包回放、休眠帧丢弃、公版 VID 隔离与挂载注册。
# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# 严禁使用 Add-Type -Path，必须全内存反射装配，避免文件句柄锁死。
# ============================================================================
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = (Resolve-Path "$ScriptDir/..").Path
$assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $ProjectDir 'FerrisPulse.exe')))
function Assert($condition, $message) { if (-not $condition) { throw $message } }
function Parse-Hex([string]$hex) {
    $tokens = ($hex.Trim() -replace '\s+', ' ').Split(' ', [StringSplitOptions]::RemoveEmptyEntries)
    $bytes = New-Object byte[] $tokens.Length
    for ($i = 0; $i -lt $tokens.Length; $i++) { $bytes[$i] = [Convert]::ToByte($tokens[$i], 16) }
    return $bytes
}

# --- 1. FDAS 包契约 ---
$jsonPath = Join-Path $ProjectDir 'examples/adapters/atk_a87pro.fp-adapter.json'
Assert (Test-Path $jsonPath) 'ATK A87 PRO adapter package missing'
$adapter = [IO.File]::ReadAllText($jsonPath) | ConvertFrom-Json
Assert ($adapter.fdasVersion -eq '2.0') 'FDAS version must be 2.0'
Assert ($adapter.hardware.vendorId -eq '0x373B' -and $adapter.hardware.productId -eq '0x111F') 'Hardware fingerprint'
Assert ($adapter.hardware.endpointTopology.usagePage -eq '0xFF60') 'Vendor collection usage page'
Assert ([int]$adapter.hardware.endpointTopology.reportLength -eq 33) 'Exact 33-byte report length'
Assert ([int]$adapter.protocol.writeTimeoutMs -eq 400) 'Write timeout must be 400ms'

# --- 2. 核心驱动常量与查询帧 ---
$helper = $assembly.GetType('RazerBatteryTray.AtkA87ProHelper', $true)
Assert ([RazerBatteryTray.AtkA87ProHelper]::ReportLength -eq 33) 'Core report length'
Assert ([RazerBatteryTray.AtkA87ProHelper]::BatteryByteOffset -eq 4) 'Core percent offset'
$query = [RazerBatteryTray.AtkA87ProHelper]::BuildBatteryQuery()
Assert ($query.Length -eq 33) 'Query must be 33 bytes'
Assert ($query[0] -eq 0 -and $query[1] -eq 0x02 -and $query[2] -eq 0x96 -and $query[3] -eq 0x24) 'Query framing 00 02 96 24'

# --- 3. 真机样本回放 ---
$full = Parse-Hex $adapter.capabilities.battery.sampleRxHex
$pct = [int]0; $chg = [bool]$true
$ok = [RazerBatteryTray.AtkA87ProHelper]::ParseBatteryResponse($full, 33, [ref]$pct, [ref]$chg)
Assert ($ok -and $pct -eq 100 -and -not $chg) 'Recorded 100% full-charge sample'

$charging = Parse-Hex $adapter.capabilities.battery.chargingSampleRxHex
$pct = [int]0; $chg = [bool]$false
$ok = [RazerBatteryTray.AtkA87ProHelper]::ParseBatteryResponse($charging, 33, [ref]$pct, [ref]$chg)
Assert ($ok -and $pct -eq 91 -and $chg) 'Recorded 91% charging sample'

# --- 4. 休眠帧必须丢弃（0% + 未充电） ---
$sleep = New-Object byte[] 33
$sleep[1] = 0x02; $sleep[2] = 0x96; $sleep[3] = 0x24; $sleep[4] = 0; $sleep[5] = 0
$pct = [int]123; $chg = [bool]$true
$ok = [RazerBatteryTray.AtkA87ProHelper]::ParseBatteryResponse($sleep, 33, [ref]$pct, [ref]$chg)
Assert (-not $ok) 'Sleep frame (0% + not charging) must be rejected'
Assert ($pct -eq -1) 'Rejected frame must not expose a level'

# --- 5. 无关帧与越界值必须拒绝 ---
$noise = New-Object byte[] 33
$noise[1] = 0x02; $noise[2] = 0x96; $noise[3] = 0x26; $noise[4] = 0; $noise[7] = 0x19
$pct = [int]0; $chg = [bool]$false
Assert (-not [RazerBatteryTray.AtkA87ProHelper]::ParseBatteryResponse($noise, 33, [ref]$pct, [ref]$chg)) 'Unrelated sub-command frame rejected'
$over = New-Object byte[] 33
$over[1] = 0x02; $over[2] = 0x96; $over[3] = 0x24; $over[4] = 101
$pct = [int]0; $chg = [bool]$false
Assert (-not [RazerBatteryTray.AtkA87ProHelper]::ParseBatteryResponse($over, 33, [ref]$pct, [ref]$chg)) 'Out-of-range level rejected'

# --- 6. 公版 VID 隔离 + 鼠标回归 + 挂载注册 ---
$keyboardPath = '\\?\hid#vid_373b&pid_111f&mi_03#9&255ddaa5&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}'
Assert (-not [RazerBatteryTray.UniversalGamingDeviceHelper]::IsSupportedGamingDevice(0x373B, $keyboardPath, 'ATK A87Pro')) 'Keyboard must not be claimed by universal engine'
$mousePath = '\\?\hid#vid_373b&pid_1089&mi_02#7&1a2b3c4d&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}'
Assert ([RazerBatteryTray.UniversalGamingDeviceHelper]::IsSupportedGamingDevice(0x373B, $mousePath, 'ATK X1 Pro Max Wireless')) 'ATK mouse regression'
$dm = [IO.File]::ReadAllText((Join-Path $ProjectDir 'src/Hardware/DeviceManager.cs'))
Assert ($dm.Contains('AtkA87ProHelper.QueryAllA87ProDevices()')) 'DeviceManager registration'

Write-Host 'PASS: ATK A87 PRO 33B framing, 100%/91% samples, sleep-frame discard, noise/range rejection, VID isolation, DeviceManager mount'
