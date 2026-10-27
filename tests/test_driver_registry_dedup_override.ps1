# ============================================================================
# 模块名称: test_driver_registry_dedup_override.ps1
# 所属区域: Tests (自动化回归测试)
# 核心职责:
#   验证 BuiltInDrivers 内置驱动插件化封装、UniversalGamingDeviceHelper 去重防污染、
#   各品牌真实能力契约 (Capabilities) 初始化，以及外部扩展包按 VID:PID 热覆盖内置驱动。
#
# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
# 1. [防锁死铁律]: 严禁使用 Add-Type -Path 加载 FerrisPulse.exe，必须采用 ReadAllBytes 全内存反射装配。
# 2. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
# ============================================================================

$ErrorActionPreference = "Stop"
$exePath = Join-Path $PSScriptRoot "..\FerrisPulse.exe"
if (-not (Test-Path $exePath)) {
    throw "FerrisPulse.exe not found at $exePath"
}

$bytes = [System.IO.File]::ReadAllBytes($exePath)
$asm = [System.Reflection.Assembly]::Load($bytes)

function Assert-True($cond, $msg) {
    if (-not $cond) { throw "ASSERT FAILED: $msg" }
    Write-Host "  [PASS] $msg" -ForegroundColor Green
}

Write-Host "=== Running Driver Registry Dedup & Override Tests ===" -ForegroundColor Cyan

$tBuiltIn = $asm.GetType("RazerBatteryTray.BuiltInDrivers")
Assert-True ($null -ne $tBuiltIn) "BuiltInDrivers class should exist"

$mEnsure = $tBuiltIn.GetMethod("EnsureRegistered")
Assert-True ($null -ne $mEnsure) "BuiltInDrivers.EnsureRegistered() should exist"
$null = $mEnsure.Invoke($null, @())

$tRegistry = $asm.GetType("RazerBatteryTray.HardwareDriverRegistry")
$tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo")
$tCat = $asm.GetType("RazerBatteryTray.DeviceCategory")

# 1. 验证 MCHOSE 鼠标能力契约（补全 SetDpiStages / SetPollingRate / XY 独立轴）
$mchoseMouse = [Activator]::CreateInstance($tInfo)
$mchoseMouse.DeviceId = "MCHOSE:3837:0042:MS"
$mchoseMouse.HardwareFingerprint = "HID:3837:0042:MS"
$mchoseMouse.Brand = "MCHOSE"
$mchoseMouse.Category = [Enum]::Parse($tCat, "Mouse")

$mInitCap = $tRegistry.GetMethod("EnsureCapabilitiesInitialized")
$null = $mInitCap.Invoke($null, @($mchoseMouse))

Assert-True ($mchoseMouse.Capabilities.SupportsDpi -eq $true) "MCHOSE Mouse SupportsDpi should be true"
Assert-True ($mchoseMouse.Capabilities.SupportsDpiStages -eq $true) "MCHOSE Mouse SupportsDpiStages should be true"
Assert-True ($mchoseMouse.Capabilities.SupportsPollingRate -eq $true) "MCHOSE Mouse SupportsPollingRate should be true"
Assert-True ($mchoseMouse.Capabilities.SupportsXyIndependence -eq $true) "MCHOSE Mouse SupportsXyIndependence should be true"

# 2. 验证 NuPhy 键盘能力契约（5 项 CustomActions 完整注入）
$nuphyKb = [Activator]::CreateInstance($tInfo)
$nuphyKb.DeviceId = "HID:19F5:8F01"
$nuphyKb.Brand = "NuPhy"
$nuphyKb.Category = [Enum]::Parse($tCat, "Keyboard")
$null = $mInitCap.Invoke($null, @($nuphyKb))

Assert-True ($nuphyKb.Capabilities.CustomActions.Count -eq 5) "NuPhy Keyboard should declare 5 CustomActions"

# 3. 验证 UniversalGaming 只读设备诚实能力声明（SupportsDpi = false, SupportsPollingRate = false）
$lamzuMouse = [Activator]::CreateInstance($tInfo)
$lamzuMouse.DeviceId = "Gaming:37B0:0010"
$lamzuMouse.HardwareFingerprint = "HID:37B0:0010"
$lamzuMouse.Brand = "LAMZU"
$lamzuMouse.Category = [Enum]::Parse($tCat, "Mouse")
$null = $mInitCap.Invoke($null, @($lamzuMouse))

Assert-True ($lamzuMouse.Capabilities.SupportsBattery -eq $true) "UniversalGaming LAMZU SupportsBattery should be true"
Assert-True ($lamzuMouse.Capabilities.SupportsDpi -eq $false) "UniversalGaming LAMZU SupportsDpi should be false without external package"
Assert-True ($lamzuMouse.Capabilities.SupportsPollingRate -eq $false) "UniversalGaming LAMZU SupportsPollingRate should be false without external package"

# 4. 验证 UniversalGamingDeviceHelper.IsSupportedGamingDevice 剔除 VID_MCHOSE (0x3837) 与 VID_STEELSERIES (0x1038)
$tUniv = $asm.GetType("RazerBatteryTray.UniversalGamingDeviceHelper")
$mIsSup = $tUniv.GetMethod("IsSupportedGamingDevice")
$supMchose = $mIsSup.Invoke($null, @([int]0x3837, [string]"\\?\hid#vid_3837&pid_0042", [string]"MCHOSE A5"))
$supSteel = $mIsSup.Invoke($null, @([int]0x1038, [string]"\\?\hid#vid_1038&pid_2232", [string]"SteelSeries Arctis"))
$supLamzu = $mIsSup.Invoke($null, @([int]0x37B0, [string]"\\?\hid#vid_37b0&pid_0010", [string]"LAMZU 8K"))

Assert-True ($supMchose -eq $false) "UniversalGamingDeviceHelper should NOT intercept VID_MCHOSE (0x3837)"
Assert-True ($supSteel -eq $false) "UniversalGamingDeviceHelper should NOT intercept VID_STEELSERIES (0x1038)"
Assert-True ($supLamzu -eq $true) "UniversalGamingDeviceHelper should still support VID_LAMZU_OFFICIAL (0x37B0)"

# 5. 验证 DeviceManager.MergeExternalDriverDevice 按 VID:PID 覆盖内置设备并保留 CustomName
$tDevMgr = $asm.GetType("RazerBatteryTray.DeviceManager")
$mMerge = $tDevMgr.GetMethod("MergeExternalDriverDevice")
Assert-True ($null -ne $mMerge) "DeviceManager.MergeExternalDriverDevice should exist"

$listType = [System.Collections.Generic.List`1].MakeGenericType($tInfo)
$devList = [Activator]::CreateInstance($listType)

$builtInDev = [Activator]::CreateInstance($tInfo)
$builtInDev.DeviceId = "Gaming:37B0:0010"
$builtInDev.HardwareFingerprint = "HID:37B0:0010"
$builtInDev.Brand = "LAMZU"
$builtInDev.DeviceName = "LAMZU 8K Receiver"
$builtInDev.CustomName = "我的主力鼠标"
$builtInDev.Category = [Enum]::Parse($tCat, "Mouse")
$builtInDev.IsConnected = $true
$devList.Add($builtInDev)

$extDev = [Activator]::CreateInstance($tInfo)
$extDev.DeviceId = "FDAS:37B0:0010:MS"
$extDev.HardwareFingerprint = "HID:37B0:0010:MS"
$extDev.DriverId = "FDAS:37B0:0010"
$extDev.Brand = "LAMZU"
$extDev.DeviceName = "LAMZU Maya X 8K (External V2)"
$extDev.Category = [Enum]::Parse($tCat, "Mouse")
$extDev.IsConnected = $true
$extDev.Capabilities.SupportsDpi = $true
$extDev.Capabilities.SupportsPollingRate = $true

$null = $mMerge.Invoke($null, @($devList, $extDev))

Assert-True ($devList.Count -eq 1) "External device with same VID:PID should replace built-in device in-place without duplication"
Assert-True ($devList[0].DeviceId -eq "FDAS:37B0:0010:MS") "Replaced device should have external DeviceId"
Assert-True ($devList[0].CustomName -eq "我的主力鼠标") "Replaced device should preserve user CustomName"
Assert-True ($devList[0].Capabilities.SupportsDpi -eq $true) "Replaced device should have external driver Capabilities"

Write-Host "ALL DRIVER REGISTRY DEDUP & OVERRIDE TESTS PASSED!" -ForegroundColor Green
