# ============================================================================
# 测试名称: test_razer_chroma_basilisk_v3_pro.ps1
# 所属区域: tests (自动化回归)
# 核心职责:
#   Basilisk V3 Pro (1532:00AA 有线 / 1532:00AB 2.4G) Chroma 扩展矩阵灯效与
#   滚轮物理机构协议的离线回放验证。全部断言只依赖纯函数与常量，不触碰硬件。
#
# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
# 1. 严禁 Add-Type -Path（会锁死 FerrisPulse.exe 导致后续 CS0016），必须全内存反射加载。
# 2. 本文件必须严格保存为【UTF-8 with BOM】编码。
# ============================================================================

$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '../FerrisPulse.exe')))
function Assert($condition, $message) { if (-not $condition) { throw $message } }
function AssertEq($actual, $expected, $message) { if ($actual -ne $expected) { throw ($message + " (expected=$expected actual=$actual)") } }

$chroma = $assembly.GetType('RazerBatteryTray.RazerChromaHelper', $true)

# ---------- 1. 设备支持矩阵 ----------
Assert ([RazerBatteryTray.RazerChromaHelper]::IsSupportedDeviceId('Razer:1532:00AB')) 'Basilisk V3 Pro 2.4G receiver must be supported'
Assert ([RazerBatteryTray.RazerChromaHelper]::IsSupportedDeviceId('Razer:1532:00AA')) 'Basilisk V3 Pro wired must be supported'
Assert ([RazerBatteryTray.RazerChromaHelper]::IsSupportedDeviceId('Razer:1532:00CD')) 'Basilisk V3 Pro 35K wireless must be supported'
Assert ([RazerBatteryTray.RazerChromaHelper]::IsSupportedDeviceId('Razer:1532:0099')) 'Basilisk V3 must be supported'
Assert (-not [RazerBatteryTray.RazerChromaHelper]::IsSupportedDeviceId('Razer:1532:00C1')) 'Viper V3 Pro must NOT be claimed as verified'
Assert (-not [RazerBatteryTray.RazerChromaHelper]::IsSupportedDeviceId('Logitech:046D:C52B')) 'Foreign vendor must be rejected'
Assert (-not [RazerBatteryTray.RazerChromaHelper]::IsSupportedDeviceId('')) 'Empty id must be rejected'
Assert (-not [RazerBatteryTray.RazerChromaHelper]::IsSupportedDeviceId('1532:00AB')) 'Missing vendor prefix must be rejected'
Assert ([RazerBatteryTray.RazerChromaHelper]::IsSupportedDeviceId('Razer:1532:00AB:MS')) 'Multi-channel suffix must still resolve'

# ---------- 2. LED 分区 ID（本机逐个 LED ID 实测结论） ----------
AssertEq ([RazerBatteryTray.RazerChromaHelper]::LedWhole) 0x00 'Whole-mouse LED id'
AssertEq ([RazerBatteryTray.RazerChromaHelper]::LedScrollWheel) 0x01 'Scroll wheel LED id'
AssertEq ([RazerBatteryTray.RazerChromaHelper]::LedLogo) 0x04 'Logo LED id'
AssertEq ([RazerBatteryTray.RazerChromaHelper]::LedUnderglow) 0x0A 'Underglow LED id is 0x0A on this model, NOT openrazer BACKLIGHT_LED 0x05'
Assert (-not ($chroma.GetField('LedBacklight', [Reflection.BindingFlags]'Public,Static'))) 'Deprecated LedBacklight 0x05 must not be exposed'

# ---------- 3. 灯效 ID（与 openrazer razerchromacommon.c 对齐） ----------
AssertEq ([RazerBatteryTray.RazerChromaHelper]::EffectOff) 0x00 'Off effect id'
AssertEq ([RazerBatteryTray.RazerChromaHelper]::EffectStatic) 0x01 'Static effect id'
AssertEq ([RazerBatteryTray.RazerChromaHelper]::EffectBreathing) 0x02 'Breathing effect id'
AssertEq ([RazerBatteryTray.RazerChromaHelper]::EffectSpectrum) 0x03 'Spectrum effect id'
AssertEq ([RazerBatteryTray.RazerChromaHelper]::EffectWave) 0x04 'Wave effect id'
AssertEq ([RazerBatteryTray.RazerChromaHelper]::EffectReactive) 0x05 'Reactive effect id'
Assert (-not ($chroma.GetMethod('SetStarlight'))) 'Starlight must NOT be implemented: all three payload forms (0x06/0x09/0x0C) answer 0x05 NOT_SUPPORTED on both 00AA and 00AB'

# ---------- 4. 滚轮模式常量 ----------
AssertEq ([RazerBatteryTray.RazerChromaHelper]::ScrollModeTactile) 0 'Tactile scroll mode'
AssertEq ([RazerBatteryTray.RazerChromaHelper]::ScrollModeFreeSpin) 1 'Free spin scroll mode'

# ---------- 5. 报文构造契约（反射调用私有 BuildReport） ----------
$build = $chroma.GetMethod('BuildReport', [Reflection.BindingFlags]'NonPublic,Static')
Assert ($null -ne $build) 'BuildReport must exist'

# 静态红整只：args = 01 00 01 00 00 01 FF 00 00
$args = [byte[]]@(0x01, 0x00, 0x01, 0x00, 0x00, 0x01, 0xFF, 0x00, 0x00)
$report = $build.Invoke($null, @([byte]0x0F, [byte]0x02, $args))
AssertEq $report.Length 91 'Razer report must be 91 bytes'
AssertEq $report[0] 0x00 'Report[0] must be zero'
AssertEq $report[2] 0x1F 'Transaction id must be 0x1F for this family'
AssertEq $report[6] 0x09 'DataSize must equal args length'
AssertEq $report[7] 0x0F 'Command class must be 0x0F (extended matrix)'
AssertEq $report[8] 0x02 'Command id must be 0x02 (set effect)'
AssertEq $report[9] 0x01 'args[0] variable storage = VARSTORE'
AssertEq $report[10] 0x00 'args[1] led id = whole'
AssertEq $report[11] 0x01 'args[2] effect id = static'
AssertEq $report[14] 0x01 'args[5] colour count = 1'
AssertEq $report[15] 0xFF 'args[6] red'
AssertEq $report[16] 0x00 'args[7] green'
AssertEq $report[17] 0x00 'args[8] blue'
AssertEq $report[90] 0x00 'Reserved trailing byte must be zero'

# CRC = XOR of bytes[3..87]
$crc = 0
for ($i = 3; $i -lt 88; $i++) { $crc = $crc -bxor $report[$i] }
AssertEq $report[89] $crc 'CRC must be XOR of bytes 3..87'

# 亮度写：class 0x0F cmd 0x04, args = 01 led brightness
$bright = [byte[]]@(0x01, 0x0A, 0xC8)
$brightReport = $build.Invoke($null, @([byte]0x0F, [byte]0x04, $bright))
AssertEq $brightReport[6] 0x03 'Brightness data size'
AssertEq $brightReport[7] 0x0F 'Brightness command class'
AssertEq $brightReport[8] 0x04 'Brightness set command id'
AssertEq $brightReport[11] 0xC8 'Brightness payload byte'

# 滚轮模式：class 0x02 cmd 0x14, args = 01 mode
$mode = [byte[]]@(0x01, 0x01)
$modeReport = $build.Invoke($null, @([byte]0x02, [byte]0x14, $mode))
AssertEq $modeReport[6] 0x02 'Scroll mode data size'
AssertEq $modeReport[7] 0x02 'Scroll mode command class'
AssertEq $modeReport[8] 0x14 'Scroll mode set command id'
AssertEq $modeReport[10] 0x01 'Scroll mode value'

# 滚轮加速 / 智能滚轮命令字
$accelReport = $build.Invoke($null, @([byte]0x02, [byte]0x16, [byte[]]@(0x01, 0x01)))
AssertEq $accelReport[8] 0x16 'Scroll acceleration set command id'
$smartReport = $build.Invoke($null, @([byte]0x02, [byte]0x17, [byte[]]@(0x01, 0x01)))
AssertEq $smartReport[8] 0x17 'Smart reel set command id'

# args 超过 80 字节必须截断而不是越界
$overflow = [byte[]](New-Object byte[] 100)
$overflowReport = $build.Invoke($null, @([byte]0x0F, [byte]0x02, $overflow))
AssertEq $overflowReport.Length 91 'Overflowing args must not enlarge the report'
$overflowCrc = 0
for ($i = 3; $i -lt 88; $i++) { $overflowCrc = $overflowCrc -bxor $overflowReport[$i] }
AssertEq $overflowReport[89] $overflowCrc 'Overflow CRC must stay consistent'

# ---------- 6. 源码级铁律审计 ----------
$helperSource = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../src/Hardware/RazerChromaHelper.cs'))
Assert ($helperSource.Contains('AI MAINTENANCE DIRECTIVE')) 'Maintenance directive header must be preserved'
Assert (-not ($helperSource -match '\$"')) 'C# 6 string interpolation is forbidden'
Assert (-not ($helperSource.Contains('?.'))) 'C# 6 null-conditional operator is forbidden'
Assert (-not ($helperSource.Contains('nameof('))) 'nameof is forbidden'
Assert ($helperSource.Contains('StatusPending')) 'New Command (0x00) pending state must be handled'
Assert ($helperSource.Contains('response[7] != commandClass')) 'Stale response echo validation is mandatory'
Assert ($helperSource.Contains('HardwareCommandBus.GetDeviceLock')) 'Chroma must share the per-device lock with DPI polling'
Assert (-not ($helperSource.Contains('QueryBrightness'))) 'Brightness read-back must stay unexposed: 0x84 answers FAILURE on this model'
# 滚轮三开关的回读载荷已实机验证精确跟随（写 1 读 1 / 写 0 读 0），必须提供 Query 入口
Assert ($helperSource.Contains('QueryScrollMode')) 'Scroll mode read-back must be exposed (payload verified to track writes)'
Assert ($helperSource.Contains('QueryScrollAcceleration')) 'Scroll acceleration read-back must be exposed'
Assert ($helperSource.Contains('QuerySmartReel')) 'Smart reel read-back must be exposed'
Assert ($helperSource.Contains('SyncScrollState')) 'Bulk wheel-state sync entry point must exist'
Assert ($helperSource.Contains('SendRawForResponse')) 'Read commands need an echo-validated response path'

# 对话框同样必须遵守 C# 5 与 BOM 约束
$dialogSource = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../src/UI/Forms/RazerLightingDialog.cs'))
Assert ($dialogSource.Contains('AI MAINTENANCE DIRECTIVE')) 'Dialog header must be preserved'
Assert (-not ($dialogSource -match '\$"')) 'Dialog must not use C# 6 interpolation'
Assert ($dialogSource.Contains('智能滚轮')) 'Smart reel toggle must be present in the dialog'
Assert ($dialogSource.Contains('滚轮加速')) 'Scroll acceleration toggle must be present in the dialog'
Assert ($dialogSource.Contains('LedUnderglow')) 'Underglow target must be wired'
Assert ($dialogSource.Contains('SyncWheelStateFromDevice')) 'Dialog must sync real wheel state on open, not show hardcoded defaults'

# 两个 UI 文件必须为 UTF-8 with BOM
foreach ($relative in @('../src/Hardware/RazerChromaHelper.cs', '../src/UI/Forms/RazerLightingDialog.cs', '../src/UI/Forms/MainForm.RazerLighting.cs', 'test_razer_chroma_basilisk_v3_pro.ps1')) {
    $bytes = [IO.File]::ReadAllBytes((Join-Path $PSScriptRoot $relative))
    Assert ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) ('UTF-8 BOM required: ' + $relative)
}

Write-Host 'PASS: Basilisk V3 Pro Chroma + scroll wheel adaptation'
