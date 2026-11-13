$ErrorActionPreference = 'Stop'
# ============================================================================
# 测试脚本: test_mchose_v9_turbo_plus.ps1
# 目标: 验证 迈从 V9 Turbo+ 耳机 (3837:600A) 的 FDAS v2.0 包 + 核心驱动
#       MchoseHeadsetHelper，锁定 "55 65 00 1A 为降级帧、不是 0% 也不是充电位"。
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

$jsonPath = Join-Path $ProjectDir 'examples/adapters/mchose_v9_turbo_plus.fp-adapter.json'
Assert (Test-Path $jsonPath) 'MCHOSE V9 Turbo+ adapter package missing'
$adapter = [IO.File]::ReadAllText($jsonPath) | ConvertFrom-Json
Assert ($adapter.fdasVersion -eq '2.0') 'FDAS version must be 2.0'
Assert ($adapter.metadata.category -eq 'Headset') 'Category must be Headset'
Assert ($adapter.hardware.vendorId -eq '0x3837' -and $adapter.hardware.productId -eq '0x600A') 'Hardware fingerprint'
Assert ([int]$adapter.hardware.endpointTopology.reportLength -eq 64) 'Exact 64-byte report length'
Assert ($null -eq $adapter.capabilities.battery.chargingByteOffset) 'No charging offset declared'

# --- 核心驱动 ---
$helper = $assembly.GetType('RazerBatteryTray.MchoseHeadsetHelper', $true)
Assert ([RazerBatteryTray.MchoseHeadsetHelper]::ReportLength -eq 64) 'Core report length'
Assert ([RazerBatteryTray.MchoseHeadsetHelper]::BatteryByteOffset -eq 2) 'Core percent offset'
$query = [RazerBatteryTray.MchoseHeadsetHelper]::BuildBatteryQuery()
Assert ($query.Length -eq 64 -and $query[0] -eq 0x55 -and $query[1] -eq 0x65 -and $query[2] -eq 0x01) 'Query framing 55 65 01'

# --- 健康帧回放 ---
$healthy = Parse-Hex $adapter.capabilities.battery.sampleRxHex
$pct = [int]0
$ok = [RazerBatteryTray.MchoseHeadsetHelper]::ParseBatteryResponse($healthy, 64, [ref]$pct)
Assert ($ok -and $pct -eq 100) 'Recorded 100% healthy sample'

# --- 降级帧必须整帧丢弃，绝不能变成 0% ---
$degraded = Parse-Hex $adapter.capabilities.battery.invalidSampleRxHex
Assert ($degraded[2] -eq 0 -and $degraded[3] -eq 0x1A) 'Degraded sample is 55 65 00 1A'
$pct = [int]123
$ok = [RazerBatteryTray.MchoseHeadsetHelper]::ParseBatteryResponse($degraded, 64, [ref]$pct)
Assert (-not $ok) 'Degraded frame must be rejected'
Assert ($pct -eq -1) 'Degraded frame must not yield 0%'

# --- 帧头不符与越界拒绝 ---
$badHeader = New-Object byte[] 64
$badHeader[0] = 0x00; $badHeader[1] = 0x02; $badHeader[2] = 100; $badHeader[3] = 0
$pct = [int]0
Assert (-not [RazerBatteryTray.MchoseHeadsetHelper]::ParseBatteryResponse($badHeader, 64, [ref]$pct)) 'Wrong header rejected'
$over = New-Object byte[] 64
$over[0] = 0x55; $over[1] = 0x65; $over[2] = 200; $over[3] = 0
$pct = [int]0
Assert (-not [RazerBatteryTray.MchoseHeadsetHelper]::ParseBatteryResponse($over, 64, [ref]$pct)) 'Out-of-range level rejected'

# --- 引擎隔离与挂载注册 ---
$headsetPath = '\\?\hid#vid_3837&pid_600a&mi_00#8&1f2e3d4c&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}'
Assert (-not [RazerBatteryTray.UniversalGamingDeviceHelper]::IsSupportedGamingDevice(0x3837, $headsetPath, 'MCHOSE V9 Turbo+')) 'Universal engine must not claim headset'
$dm = [IO.File]::ReadAllText((Join-Path $ProjectDir 'src/Hardware/DeviceManager.cs'))
Assert ($dm.Contains('MchoseHeadsetHelper.QueryAllMchoseHeadsetDevices()')) 'DeviceManager registration'

Write-Host 'PASS: MCHOSE V9 Turbo+ 55 65 framing, 100% sample, 0x1A degraded-frame discard, no fabricated charging, DeviceManager mount'
