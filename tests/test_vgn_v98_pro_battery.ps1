# ============================================================================
# 测试脚本: test_vgn_v98_pro_battery.ps1
# 目标: 验证 VGN V98 PRO (320F:5088，与 ATK V75X 共用 Telink 接收器)：
#       1. 产品串精确隔离（VGN S99 等仍拒绝）
#       2. 型号 / 品牌 / DeviceId 前缀解析
#       3. 实机抓包回放 04 03 00 1A 00 00 00 00 33 00 -> 51%
#       4. 充电位、边界与坏帧拒绝
#       5. VGN 键盘不得被 VgnMouseDriver 误挂 DPI / 回报率能力
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = Resolve-Path "$ScriptDir\.."
Set-Location $ProjectDir

Write-Host "[TEST] Validating VGN V98 PRO (320F:5088) battery support..." -ForegroundColor Cyan

$exePath = "$ProjectDir\FerrisPulse.exe"
if (-not (Test-Path $exePath)) { throw "FerrisPulse.exe not found!" }
$asmBytes = [System.IO.File]::ReadAllBytes($exePath)
$asm = [System.Reflection.Assembly]::Load($asmBytes)

$atk = $asm.GetType("RazerBatteryTray.AtkDeviceHelper")
if ($null -eq $atk) { throw "AtkDeviceHelper not found" }

function New-Desc([int]$vid, [int]$productId, [string]$product) {
    $d = New-Object RazerBatteryTray.HidDeviceDescriptor
    $d.VendorId = $vid
    $d.ProductId = $productId
    $d.ProductString = $product
    return $d
}

# --- 1. 产品串隔离 ---
if (-not [RazerBatteryTray.AtkDeviceHelper]::IsSupportedDevice((New-Desc 0x320F 0x5088 "VGN V98 PRO 2.4G Dongle"))) {
    throw "VGN V98 PRO should be supported"
}
if ([RazerBatteryTray.AtkDeviceHelper]::IsSupportedDevice((New-Desc 0x320F 0x5088 "VGN S99 2.4G Dongle"))) {
    throw "VGN S99 must stay unsupported (shared PID isolation)"
}
if (-not [RazerBatteryTray.AtkDeviceHelper]::IsSupportedDevice((New-Desc 0x320F 0x5088 "ATK V75X 2.4G Dongle"))) {
    throw "ATK V75X regression: still supported"
}
if ([RazerBatteryTray.AtkDeviceHelper]::IsSupportedDevice((New-Desc 0x320F 0x5088 "Generic USB Keyboard"))) {
    throw "Unknown product string must be rejected"
}
Write-Host "  [PASS] Product-string isolation: V98 PRO yes / S99 no / V75X yes" -ForegroundColor Green

# --- 2. 型号解析 ---
if ([RazerBatteryTray.AtkDeviceHelper]::ResolveModelName("VGN V98 PRO 2.4G Dongle") -ne "VGN V98 PRO") { throw "VGN model name" }
if ([RazerBatteryTray.AtkDeviceHelper]::ResolveBrand("VGN V98 PRO 2.4G Dongle") -ne "VGN") { throw "VGN brand" }
if ([RazerBatteryTray.AtkDeviceHelper]::ResolveDeviceIdPrefix("VGN V98 PRO 2.4G Dongle") -ne "VGN") { throw "VGN id prefix" }
if ([RazerBatteryTray.AtkDeviceHelper]::ResolveModelName("ATK V75X 2.4G Dongle") -ne "ATK V75X") { throw "ATK model name" }
if ([RazerBatteryTray.AtkDeviceHelper]::ResolveBrand("ATK V75X 2.4G Dongle") -ne "ATK") { throw "ATK brand" }
Write-Host "  [PASS] Model/brand/id resolution" -ForegroundColor Green

# --- 3. 实机帧回放（51% 未充电） ---
[byte[]]$frame = New-Object byte[] 64
[byte[]]$head = @(4,3,0,0x1A,0,0,0,0,0x33,0)
[Array]::Copy($head,$frame,$head.Length)
$state = [RazerBatteryTray.AtkDeviceHelper]::ParseBatteryResponseEx($frame, 3, "VGN V98 PRO", "VGN")
if ($null -eq $state -or $state.BatteryPercent -ne 51 -or $state.IsCharging -or
    $state.DeviceName -ne "VGN V98 PRO" -or $state.Brand -ne "VGN" -or
    $state.Category.ToString() -ne "Keyboard") {
    throw "Real VGN frame parse failed: batt=$($state.BatteryPercent) name=$($state.DeviceName)"
}
Write-Host "  [PASS] Real frame 04 03 00 1A ... 33 00 -> 51%, VGN V98 PRO" -ForegroundColor Green

# --- 4. 充电位与边界 ---
$frame[9] = 1
$chg = [RazerBatteryTray.AtkDeviceHelper]::ParseBatteryResponseEx($frame, 3, "VGN V98 PRO", "VGN")
if (-not $chg.IsCharging) { throw "Charging bit not detected" }
$frame[8] = 100; $frame[9] = 0
$full = [RazerBatteryTray.AtkDeviceHelper]::ParseBatteryResponseEx($frame, 3, "VGN V98 PRO", "VGN")
if ($full.BatteryPercent -ne 100) { throw "100% boundary" }
$frame[8] = 101
if ($null -ne [RazerBatteryTray.AtkDeviceHelper]::ParseBatteryResponseEx($frame, 3, "VGN V98 PRO", "VGN")) {
    throw "Invalid 101% must be rejected"
}
$frame[8] = 51
if ($null -ne [RazerBatteryTray.AtkDeviceHelper]::ParseBatteryResponseEx($frame, 4, "VGN V98 PRO", "VGN")) {
    throw "Stale sequence must be rejected"
}
Write-Host "  [PASS] Charging bit / 100% / 101% / stale sequence" -ForegroundColor Green

# --- 5. 向后兼容 ATK 两参重载 ---
$legacy = [RazerBatteryTray.AtkDeviceHelper]::ParseBatteryResponse($frame, 3)
if ($null -eq $legacy -or $legacy.Brand -ne "ATK" -or $legacy.DeviceName -ne "ATK V75X") {
    throw "Legacy two-arg overload must keep ATK branding"
}
Write-Host "  [PASS] Legacy ATK overload unchanged" -ForegroundColor Green

# --- 6. VGN 键盘不得被鼠标驱动接管 ---
$driverType = $asm.GetType("RazerBatteryTray.VgnMouseDriver")
if ($null -eq $driverType) { throw "VgnMouseDriver missing" }
$driver = [System.Activator]::CreateInstance($driverType)
$kb = New-Object RazerBatteryTray.MouseBatteryInfo
$kb.Brand = "VGN"
$kb.Category = [RazerBatteryTray.DeviceCategory]::Keyboard
if ($driver.CanHandle($kb)) { throw "VGN keyboard must NOT match VgnMouseDriver" }
$kb.Category = [RazerBatteryTray.DeviceCategory]::Mouse
if (-not $driver.CanHandle($kb)) { throw "VGN mouse must still match VgnMouseDriver" }
Write-Host "  [PASS] VgnMouseDriver matches mice only" -ForegroundColor Green

# --- 7. ID 变体与设备管理接入 ---
$dm = $asm.GetType("RazerBatteryTray.DeviceManager")
$variants = $dm.GetMethod("GetDeviceIdVariants").Invoke($null, @("VGN:320F:5088"))
if (-not ($variants -contains "HID:320F:5088")) {
    throw "VGN:320F:5088 variants must include HID:320F:5088"
}
Write-Host "  [PASS] DeviceId variants bridge VGN:320F:5088 <-> HID:320F:5088" -ForegroundColor Green

Write-Host "[ALL PASSED] VGN V98 PRO (320F:5088) test suite succeeded!" -ForegroundColor Green
exit 0
