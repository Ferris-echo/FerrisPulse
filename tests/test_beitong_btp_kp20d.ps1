$ErrorActionPreference = 'Stop'
# ============================================================================
# 测试脚本: test_beitong_btp_kp20d.ps1
# 目标: 验证 北通 BTP-KP20D 手柄 (20BC:5159) 的 FDAS v2.0 包 + 核心驱动
#       BeitongDeviceHelper 的推送式帧回放与充电位双向语义。
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

$jsonPath = Join-Path $ProjectDir 'examples/adapters/beitong_btp_kp20d.fp-adapter.json'
Assert (Test-Path $jsonPath) 'BEITONG KP20D adapter package missing'
$adapter = [IO.File]::ReadAllText($jsonPath) | ConvertFrom-Json
Assert ($adapter.fdasVersion -eq '2.0') 'FDAS version must be 2.0'
Assert ($adapter.metadata.category -eq 'Gamepad') 'Category must be Gamepad'
Assert ($adapter.hardware.vendorId -eq '0x20BC' -and $adapter.hardware.productId -eq '0x5159') 'Hardware fingerprint'
Assert ([int]$adapter.hardware.endpointTopology.reportLength -eq 64) 'Exact 64-byte report length'
Assert ([string]::IsNullOrEmpty($adapter.protocol.queryBatteryTxHex)) 'Push-only must not declare a query'

$helper = $assembly.GetType('RazerBatteryTray.BeitongDeviceHelper', $true)
Assert ([RazerBatteryTray.BeitongDeviceHelper]::ReportLength -eq 64) 'Core report length'
Assert ([RazerBatteryTray.BeitongDeviceHelper]::BatteryByteOffset -eq 2) 'Core percent offset'
Assert ([RazerBatteryTray.BeitongDeviceHelper]::BuildBatteryQuery().Length -eq 0) 'Push-only core must have empty query'

# --- 充电样本 ---
$chargingFrame = Parse-Hex $adapter.capabilities.battery.sampleRxHex
$pct = [int]0; $chg = [bool]$false
$ok = [RazerBatteryTray.BeitongDeviceHelper]::ParseBatteryResponse($chargingFrame, 64, [ref]$pct, [ref]$chg)
Assert ($ok -and $pct -eq 100 -and $chg) 'Recorded 100% charging sample'

# --- 放电样本 ---
$idleFrame = Parse-Hex $adapter.capabilities.battery.chargingSampleRxHex
$pct = [int]0; $chg = [bool]$true
$ok = [RazerBatteryTray.BeitongDeviceHelper]::ParseBatteryResponse($idleFrame, 64, [ref]$pct, [ref]$chg)
Assert ($ok -and $pct -eq 86 -and -not $chg) 'Recorded 86% idle sample'

# --- 帧头与越界拒绝 ---
$bad = New-Object byte[] 64
$bad[0] = 0x00; $bad[1] = 0x15; $bad[2] = 50
$pct = [int]0; $chg = [bool]$false
Assert (-not [RazerBatteryTray.BeitongDeviceHelper]::ParseBatteryResponse($bad, 64, [ref]$pct, [ref]$chg)) 'Wrong header rejected'
$over = New-Object byte[] 64
$over[0] = 0x02; $over[1] = 0x15; $over[2] = 101
$pct = [int]0; $chg = [bool]$false
Assert (-not [RazerBatteryTray.BeitongDeviceHelper]::ParseBatteryResponse($over, 64, [ref]$pct, [ref]$chg)) 'Out-of-range level rejected'

$padPath = '\\?\hid#vid_20bc&pid_5159&mi_01&col02#9&2b7f4c11&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030}'
Assert (-not [RazerBatteryTray.UniversalGamingDeviceHelper]::IsSupportedGamingDevice(0x20BC, $padPath, 'BTP-KP20D')) 'Universal engine must not claim gamepad'
$dm = [IO.File]::ReadAllText((Join-Path $ProjectDir 'src/Hardware/DeviceManager.cs'))
Assert ($dm.Contains('BeitongDeviceHelper.QueryAllBeitongDevices()')) 'DeviceManager registration'

Write-Host 'PASS: BEITONG KP20D push-only 64B frames, 100%/86% samples, bidirectional charging bit, DeviceManager mount'
