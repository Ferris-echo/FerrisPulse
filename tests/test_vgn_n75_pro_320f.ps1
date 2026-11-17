# ============================================================================
# 测试脚本: test_vgn_n75_pro_320f.ps1
# 核心职责:
#   验证 VGN N75 PRO 2.4G (320F:5088) 的实机回放解析、充电位、边界值、陈旧帧拒绝，
#   以及与同 PID 的 ATK V75X 之间的双向产品串/端点隔离和能力位不虚标。
# 运行环境: PowerShell 3.0+ / Windows（零硬件依赖，纯内存反射，严禁 Add-Type -Path）
# ============================================================================

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$asmPath = Join-Path $repoRoot "FerrisPulse.exe"
if (-not (Test-Path $asmPath)) {
    Write-Host "FAIL: FerrisPulse.exe not found at $asmPath" -ForegroundColor Red
    exit 1
}

# 铁律 10：全内存反射装配，避免独占 exe 句柄导致后续 build.bat CS0016
$bytes = [System.IO.File]::ReadAllBytes($asmPath)
$asm = [System.Reflection.Assembly]::Load($bytes)

function Assert($condition, $message) {
    if (-not $condition) {
        Write-Host ("FAIL: " + $message) -ForegroundColor Red
        exit 1
    }
}

# 1. 源码存在、UTF-8 BOM、C# 5.0 语法与维护准则头部
$helperPath = Join-Path $repoRoot "src\Hardware\VgnN75ProHelper.cs"
Assert (Test-Path $helperPath) "VgnN75ProHelper.cs missing!"
$srcBytes = [System.IO.File]::ReadAllBytes($helperPath)
Assert ($srcBytes.Length -ge 3 -and $srcBytes[0] -eq 0xEF -and $srcBytes[1] -eq 0xBB -and $srcBytes[2] -eq 0xBF) "VgnN75ProHelper.cs is not UTF-8 with BOM!"
$src = [System.IO.File]::ReadAllText($helperPath, [System.Text.Encoding]::UTF8)
Assert (-not [regex]::IsMatch($src, '\$".*?"')) "string interpolation is forbidden in C# 5.0"
Assert (-not [regex]::IsMatch($src, '\w+\?\.')) "null-conditional operator is forbidden in C# 5.0"
Assert (-not [regex]::IsMatch($src, 'nameof\s*\(')) "nameof is forbidden in C# 5.0"
Assert ($src.Contains("⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️")) "AI maintenance directive header missing"
Write-Host "PASS 1: source exists, BOM valid, C# 5.0 syntax gate passed."

# 2. 设备门禁：同 PID 公版必须靠产品串互斥
$vgn = New-Object RazerBatteryTray.HidDeviceDescriptor
$vgn.VendorId = 0x320F
$vgn.ProductId = 0x5088
$vgn.UsagePage = 0xFF1C
$vgn.Usage = 0x92
$vgn.InputReportLength = 64
$vgn.OutputReportLength = 64
$vgn.ProductString = 'VGN N75 PRO 2.4G Dongle'
Assert ([RazerBatteryTray.VgnN75ProHelper]::IsSupportedDevice($vgn)) "real VGN product string must be claimed"
Assert ([RazerBatteryTray.VgnN75ProHelper]::IsVendorEndpoint($vgn)) "VGN vendor endpoint must be recognized"

$atk = New-Object RazerBatteryTray.HidDeviceDescriptor
$atk.VendorId = 0x320F
$atk.ProductId = 0x5088
$atk.ProductString = 'ATK V75X 2.4G Dongle'
Assert (-not [RazerBatteryTray.VgnN75ProHelper]::IsSupportedDevice($atk)) "ATK V75X must not be claimed by VGN helper"
Assert ([RazerBatteryTray.AtkDeviceHelper]::IsSupportedDevice($atk)) "ATK helper keeps its own model"
Assert (-not [RazerBatteryTray.AtkDeviceHelper]::IsSupportedDevice($vgn)) "VGN must not be claimed by ATK helper"

$otherVgn = New-Object RazerBatteryTray.HidDeviceDescriptor
$otherVgn.VendorId = 0x320F
$otherVgn.ProductId = 0x5088
$otherVgn.ProductString = 'VGN S99 2.4G Dongle'
Assert (-not [RazerBatteryTray.VgnN75ProHelper]::IsSupportedDevice($otherVgn)) "other VGN keyboard models must stay unclaimed"

$wiredForm = New-Object RazerBatteryTray.HidDeviceDescriptor
$wiredForm.VendorId = 0x320F
$wiredForm.ProductId = 0x5088
$wiredForm.ProductString = 'VGN N75 PRO'
Assert (-not [RazerBatteryTray.VgnN75ProHelper]::IsSupportedDevice($wiredForm)) "untested wired form must stay unclaimed"

$wrongPid = New-Object RazerBatteryTray.HidDeviceDescriptor
$wrongPid.VendorId = 0x320F
$wrongPid.ProductId = 0x5055
$wrongPid.ProductString = 'VGN N75 PRO 2.4G Dongle'
Assert (-not [RazerBatteryTray.VgnN75ProHelper]::IsSupportedDevice($wrongPid)) "untested PID must stay unclaimed"
Assert (-not [RazerBatteryTray.VgnN75ProHelper]::IsSupportedDevice($null)) "null descriptor must be rejected"

# 字段序错位（16/16 或 20/20）同样必须拒绝，避免跨设备误发报文
$badEndpoint = New-Object RazerBatteryTray.HidDeviceDescriptor
$badEndpoint.UsagePage = 0xFF1C
$badEndpoint.Usage = 0x92
$badEndpoint.InputReportLength = 32
$badEndpoint.OutputReportLength = 32
Assert (-not [RazerBatteryTray.VgnN75ProHelper]::IsVendorEndpoint($badEndpoint)) "non 64/64 endpoint must be rejected"
Write-Host "PASS 2: shared-PID isolation and endpoint quadruple gate verified."

# 3. 请求帧构造（Report 4 + 小端序号 + 0x1A）
$request = [RazerBatteryTray.VgnN75ProHelper]::BuildBatteryQuery([uint16]0x1234, 64)
Assert ($request.Length -eq 64) "request length"
Assert ($request[0] -eq 4 -and $request[1] -eq 0x34 -and $request[2] -eq 0x12 -and $request[3] -eq 0x1A) "request framing"
Assert ($request[4] -eq 0 -and $request[63] -eq 0) "payload after opcode must be zero"
$threw = $false
try { [void][RazerBatteryTray.VgnN75ProHelper]::BuildBatteryQuery([uint16]0, 64) } catch { $threw = $true }
Assert $threw "zero sequence must be rejected"
Write-Host "PASS 3: battery query framing verified."

# 4. 实机回放：04 01 00 1A 00 00 00 00 4F 00 -> 79% 未充电
$sample = New-Object byte[] 64
[byte[]]$header = @(4, 1, 0, 0x1A, 0, 0, 0, 0, 0x4F, 0)
[Array]::Copy($header, $sample, $header.Length)
$battery = -1
$charging = $true
$ok = [RazerBatteryTray.VgnN75ProHelper]::TryParseBattery($sample, [uint16]1, [ref]$battery, [ref]$charging)
Assert ($ok -and $battery -eq 79 -and -not $charging) "recorded 79% sample must parse"

# 5. 序号原样回显：0x1234 通道的回包不得被 0x0001 的解析器接受
$sampleEcho = New-Object byte[] 64
[byte[]]$echoHeader = @(4, 0x34, 0x12, 0x1A, 0, 0, 0, 0, 0x50, 0)
[Array]::Copy($echoHeader, $sampleEcho, $echoHeader.Length)
$b2 = -1
$c2 = $false
Assert ([RazerBatteryTray.VgnN75ProHelper]::TryParseBattery($sampleEcho, [uint16]0x1234, [ref]$b2, [ref]$c2)) "0x1234 echo must parse"
$b3 = -1
$c3 = $false
Assert (-not [RazerBatteryTray.VgnN75ProHelper]::TryParseBattery($sampleEcho, [uint16]1, [ref]$b3, [ref]$c3)) "stale transaction must be rejected"
# 4b. 充电态实机样本：插 USB-C 充电器时 04 07 00 1A ... 4F 01 -> 79% 充电中
$chargeSample = New-Object byte[] 64
[byte[]]$chargeHeader = @(4, 7, 0, 0x1A, 0, 0, 0, 0, 0x4F, 1)
[Array]::Copy($chargeHeader, $chargeSample, $chargeHeader.Length)
$bc = -1
$cc = $false
Assert ([RazerBatteryTray.VgnN75ProHelper]::TryParseBattery($chargeSample, [uint16]7, [ref]$bc, [ref]$cc)) "charging sample must parse"
Assert ($bc -eq 79 -and $cc) "charging sample must report 79% and charging"
Write-Host "PASS 4-5: real 79% replay, charging sample and sequence echo isolation verified."

# 6. 充电位与电量边界
foreach ($level in @(0, 1, 50, 99, 100)) {
    $sample[8] = $level
    $sample[9] = 1
    $b = -1
    $c = $false
    Assert ([RazerBatteryTray.VgnN75ProHelper]::TryParseBattery($sample, [uint16]1, [ref]$b, [ref]$c)) "boundary $level must parse"
    Assert ($b -eq $level -and $c) "charging flag / level $level mismatch"
}
$sample[8] = 101
$b = -1
$c = $false
Assert (-not [RazerBatteryTray.VgnN75ProHelper]::TryParseBattery($sample, [uint16]1, [ref]$b, [ref]$c)) "invalid percentage must be rejected"
$sample[8] = 79
$sample[9] = 2
Assert (-not [RazerBatteryTray.VgnN75ProHelper]::TryParseBattery($sample, [uint16]1, [ref]$b, [ref]$c)) "invalid charging byte must be rejected"
$sample[9] = 0
$sample[0] = 0
Assert (-not [RazerBatteryTray.VgnN75ProHelper]::TryParseBattery($sample, [uint16]1, [ref]$b, [ref]$c)) "wrong report id must be rejected"
$sample[0] = 4
$sample[3] = 0x1B
Assert (-not [RazerBatteryTray.VgnN75ProHelper]::TryParseBattery($sample, [uint16]1, [ref]$b, [ref]$c)) "wrong opcode must be rejected"
$truncated = New-Object byte[] 9
[Array]::Copy($sample, $truncated, 9)
Assert (-not [RazerBatteryTray.VgnN75ProHelper]::TryParseBattery($truncated, [uint16]1, [ref]$b, [ref]$c)) "truncated report must be rejected"
Write-Host "PASS 6: charging bit, boundaries and malformed frame rejection verified."

# 7. 型号推导
Assert ([RazerBatteryTray.VgnN75ProHelper]::GetModelName('VGN N75 PRO 2.4G Dongle') -eq 'VGN N75 PRO') "model name derivation"
Assert ([RazerBatteryTray.VgnN75ProHelper]::GetModelName('') -eq 'VGN N75 PRO') "empty product string fallback"
Write-Host "PASS 7: model name derivation verified."

# 8. 能力位不虚标：VGN 键盘不得被 VgnMouseDriver 接管
$kb = New-Object RazerBatteryTray.MouseBatteryInfo
$kb.Brand = 'VGN'
$kb.Category = [RazerBatteryTray.DeviceCategory]::Keyboard
$kb.DeviceId = 'VGN:320F:5088:KB'
$kb.HardwareFingerprint = 'HID:320F:5088:KB'
$mouse = New-Object RazerBatteryTray.MouseBatteryInfo
$mouse.Brand = 'VGN'
$mouse.Category = [RazerBatteryTray.DeviceCategory]::Mouse
$mouse.DeviceId = 'VGN:3554:F503'
$driver = New-Object RazerBatteryTray.VgnMouseDriver
Assert (-not $driver.CanHandle($kb)) "VGN keyboard must not be handled by the mouse driver"
Assert ($driver.CanHandle($mouse)) "VGN mouse must still be handled"
Assert ([RazerBatteryTray.VgnN75ProHelper]::IsNativeDevice($kb)) "native keyboard identity"
$kb.Category = [RazerBatteryTray.DeviceCategory]::Dongle
Assert (-not [RazerBatteryTray.VgnN75ProHelper]::IsNativeDevice($kb)) "dongle category must not count as native keyboard"
Write-Host "PASS 8: capability contract (battery-only) verified."

# 9. 注册与变体池
$variants = [RazerBatteryTray.DeviceManager]::GetDeviceIdVariants('VGN:320F:5088:KB')
Assert ($variants.Contains('HID:320F:5088')) "DeviceManager variant pool must bridge legacy HID id"
$dmSource = [System.IO.File]::ReadAllText((Join-Path $repoRoot "src\Hardware\DeviceManager.cs"), [System.Text.Encoding]::UTF8)
Assert ($dmSource.Contains('VgnN75ProHelper.QueryDevices()')) "DeviceManager registration missing"
Assert ($dmSource.Contains('VgnN75ProHelper.IsNativeDevice(existDev)')) "native keyboard category guard missing"
$genSource = [System.IO.File]::ReadAllText((Join-Path $repoRoot "src\Hardware\GenericHidHelper.cs"), [System.Text.Encoding]::UTF8)
Assert ($genSource.Contains('vid_320f')) "generic wizard scan must skip natively supported 0x320F devices"
Write-Host "PASS 9: DeviceManager registration, variant bridging and wizard de-duplication verified."

Write-Host "ALL TESTS PASSED for VGN N75 PRO (320F:5088) adaptation!" -ForegroundColor Green
exit 0
