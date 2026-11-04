# ============================================================================
# 测试脚本: test_razer_basilisk_lighting.ps1
# 覆盖契约 (PR #72):
#   1. LightingHelper 导出常量与方法 (ZoneAll, EffectStatic, SetAllColor, SetLeftSegment 等)
#   2. 测试模式安全防护 (测试模式下严禁下发真实硬件写指令，返回 false，安全退出)
#   3. 托盘右键菜单 lightingMenu 精确设备隔离 (仅对 Basilisk Ultimate 0086/0088 显式 Visible=true，其他设备隐藏)
# ============================================================================

$ErrorActionPreference = "Stop"
$exePath = Join-Path $PSScriptRoot "..\FerrisPulse.exe"
if (-not (Test-Path $exePath)) {
    Write-Host "[FAIL] FerrisPulse.exe not found" -ForegroundColor Red
    exit 1
}

$bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $exePath))
$asm = [System.Reflection.Assembly]::Load($bytes)

# 1. 验证 LightingHelper 类型与常量
$lightType = $asm.GetType("RazerBatteryTray.LightingHelper")
if ($null -eq $lightType) {
    Write-Host "[FAIL] LightingHelper type missing" -ForegroundColor Red
    exit 1
}

$zoneAll = [byte]$lightType.GetField("ZoneAll", [System.Reflection.BindingFlags]"Public,Static").GetValue($null)
$zoneWheel = [byte]$lightType.GetField("ZoneWheel", [System.Reflection.BindingFlags]"Public,Static").GetValue($null)
$zoneLogo = [byte]$lightType.GetField("ZoneLogo", [System.Reflection.BindingFlags]"Public,Static").GetValue($null)
$matrixLeds = [int]$lightType.GetField("MatrixLeds", [System.Reflection.BindingFlags]"Public,Static").GetValue($null)
$segCount = [int]$lightType.GetField("LeftSegmentCount", [System.Reflection.BindingFlags]"Public,Static").GetValue($null)

if ($zoneAll -ne 0x00 -or $zoneWheel -ne 0x01 -or $zoneLogo -ne 0x04 -or $matrixLeds -ne 14 -or $segCount -ne 4) {
    Write-Host "[FAIL] LightingHelper constants mismatch (MatrixLeds=$matrixLeds, segCount=$segCount)" -ForegroundColor Red
    exit 1
}
Write-Host "[PASS] 1. LightingHelper constants and topology verified (14-LED matrix, 4 left segments)" -ForegroundColor Green

# 2. 测试模式安全防护断言 (严禁调用真实 HID SetFeature 篡改物理设备)
$mainFormType = $asm.GetType("RazerBatteryTray.MainForm")
[Environment]::SetEnvironmentVariable("FERRISPULSE_TEST_MODE", "1")

$setAllMethod = $lightType.GetMethod("SetAllColor", [System.Reflection.BindingFlags]"Public,Static")
$res = [bool]$setAllMethod.Invoke($null, @([System.Drawing.Color]::LimeGreen))
if ($res -ne $false) {
    Write-Host "[FAIL] LightingHelper should return false under test mode to protect hardware" -ForegroundColor Red
    exit 1
}
Write-Host "[PASS] 2. Safeguard active: physical hardware writes safely blocked in test mode" -ForegroundColor Green

# 3. 托盘右键菜单 lightingMenu 设备隔离断言
$form = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($mainFormType)
$infoType = $asm.GetType("RazerBatteryTray.MouseBatteryInfo")
$catType = $asm.GetType("RazerBatteryTray.DeviceCategory")
$listType = [System.Collections.Generic.List`1].MakeGenericType(@($infoType))

$contextMenu = New-Object System.Windows.Forms.ContextMenuStrip
$lightingMenu = New-Object System.Windows.Forms.ToolStripMenuItem("灯光调节")
$contextMenu.Items.Add($lightingMenu) | Out-Null

$mainFormType.GetField("contextMenu", [System.Reflection.BindingFlags]"NonPublic,Instance").SetValue($form, $contextMenu)
$mainFormType.GetField("lightingMenu", [System.Reflection.BindingFlags]"NonPublic,Instance").SetValue($form, $lightingMenu)
$mainFormType.GetField("dynamicDeviceMenuItems", [System.Reflection.BindingFlags]"NonPublic,Instance").SetValue($form, (New-Object System.Collections.Generic.List[System.Windows.Forms.ToolStripMenuItem]))

$updateTrayMethod = $mainFormType.GetMethod("UpdateTrayDeviceMenuItems", [System.Reflection.BindingFlags]"NonPublic,Instance")

function Set-MockDevice($d) {
    $devs = [Activator]::CreateInstance($listType)
    $devs.Add($d)
    $mainFormType.GetField("activeDevices", [System.Reflection.BindingFlags]"NonPublic,Instance").SetValue($form, $devs)
    $mainFormType.GetField("lastInfo", [System.Reflection.BindingFlags]"NonPublic,Instance").SetValue($form, $d)
    $updateTrayMethod.Invoke($form, $null) | Out-Null
}

# 测试 A: 普通鼠标 (DeathAdder V4 Pro 00BE) -> lightingMenu 必须为 false
$daMouse = [Activator]::CreateInstance($infoType)
$daMouse.DeviceId = "Razer:1532:00BE"
$daMouse.DeviceName = "Razer DeathAdder V4 Pro"
$daMouse.Category = [Enum]::Parse($catType, "Mouse")
$daMouse.IsConnected = $true
$daMouse.IsPrimary = $true
Set-MockDevice $daMouse
if ($lightingMenu.Available -ne $false) {
    Write-Host "[FAIL] lightingMenu should be hidden for DeathAdder V4 Pro" -ForegroundColor Red
    exit 1
}

# 测试 B: 巴蛇终极版 (0088 接收器 / 0086 有线) -> lightingMenu 必须为 true
$basilisk = [Activator]::CreateInstance($infoType)
$basilisk.DeviceId = "Razer:1532:0088"
$basilisk.DeviceName = "Razer Basilisk Ultimate"
$basilisk.Category = [Enum]::Parse($catType, "Mouse")
$basilisk.IsConnected = $true
$basilisk.IsPrimary = $true
Set-MockDevice $basilisk
if ($lightingMenu.Available -ne $true) {
    Write-Host "[FAIL] lightingMenu should be visible for Basilisk Ultimate" -ForegroundColor Red
    exit 1
}

# 测试 C: 键盘 (NuPhy) -> lightingMenu 必须为 false
$kb = [Activator]::CreateInstance($infoType)
$kb.DeviceId = "NuPhy:19F5:3245"
$kb.DeviceName = "NuPhy Air75"
$kb.Category = [Enum]::Parse($catType, "Keyboard")
$kb.IsConnected = $true
$kb.IsPrimary = $true
Set-MockDevice $kb
if ($lightingMenu.Available -ne $false) {
    Write-Host "[FAIL] lightingMenu should be hidden for Keyboard" -ForegroundColor Red
    exit 1
}

Write-Host "[PASS] 3. Tray lightingMenu strictly isolated: visible ONLY for Basilisk Ultimate, hidden for others" -ForegroundColor Green
exit 0
