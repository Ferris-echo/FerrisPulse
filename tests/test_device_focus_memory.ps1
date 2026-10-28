# ============================================================================
# 测试脚本: test_device_focus_memory.ps1
# 目标: 验证主窗口“设备聚焦记忆”与启动优选规则：
#       1. 冷启动无记忆时，优先展示支持 DPI/回报率调节的鼠标，
#          即便该鼠标处于休眠占位状态（IsConnected=false 但接收器在场）；
#       2. 用户主动选中的设备被持久化，重启后原样恢复；
#       3. 记忆指向已不存在的设备时安全回退；
#       4. 托盘主设备（GetPrimaryDevice）行为不受影响。
# 说明: 脚本仅做内存反射校验，不读写真实硬件，可安全重复执行。
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = Resolve-Path "$ScriptDir\.."
Set-Location $ProjectDir

Write-Host "[TEST] Validating device focus memory and startup preference..." -ForegroundColor Cyan

$exePath = "$ProjectDir\FerrisPulse.exe"
if (-not (Test-Path $exePath)) {
    throw "FAIL: FerrisPulse.exe not found!"
}

$asmBytes = [System.IO.File]::ReadAllBytes($exePath)
$asm = [System.Reflection.Assembly]::Load($asmBytes)
$tMain = $asm.GetType("RazerBatteryTray.MainForm")
$tDev = $asm.GetType("RazerBatteryTray.MouseBatteryInfo")
$tCaps = $asm.GetType("RazerBatteryTray.DeviceCapabilities")
if ($tMain -eq $null -or $tDev -eq $null -or $tCaps -eq $null) {
    throw "FAIL: unable to resolve core types from assembly."
}

$bf = [System.Reflection.BindingFlags]'Instance,Public,NonPublic'

$fRemember = $tMain.GetField("lastSelectedDeviceId", $bf)
$fPinned = $tMain.GetField("deviceSelectionPinnedByUser", $bf)
$fActive = $tMain.GetField("activeDevices", $bf)
$fLastInfo = $tMain.GetField("lastInfo", $bf)
$mPref = $tMain.GetMethod("GetPreferredStartupDevice", $bf)
$mFind = $tMain.GetMethod("FindRememberedDevice", $bf)
$mPrimary = $tMain.GetMethod("GetPrimaryDevice", $bf)

if ($fRemember -eq $null) { throw "FAIL: field lastSelectedDeviceId missing (focus memory not wired)." }
if ($fPinned -eq $null) { throw "FAIL: field deviceSelectionPinnedByUser missing (auto-correction guard not wired)." }
if ($mPref -eq $null) { throw "FAIL: method GetPreferredStartupDevice missing." }
if ($mFind -eq $null) { throw "FAIL: method FindRememberedDevice missing." }
Write-Host "  [PASS] focus-memory fields and methods are present" -ForegroundColor Green

# ---- 构造设备列表：键盘为托盘主设备，鼠标支持性能调节但处于休眠占位 ----
$tList = ([System.Collections.Generic.List[int]]).GetGenericTypeDefinition().MakeGenericType(@($tDev))
$devList = [System.Activator]::CreateInstance($tList)

$kb = [System.Activator]::CreateInstance($tDev)
$kb.DeviceId = "MCHOSE:3837:3033"
$kb.Brand = "MCHOSE"
$kb.DeviceName = "MCHOSE K99 V3"
$kb.Category = [RazerBatteryTray.DeviceCategory]::Keyboard
$kb.IsConnected = $true
$kb.IsPrimary = $true
$kb.BatteryPercent = 69
$kb.Capabilities = [System.Activator]::CreateInstance($tCaps)

$ms = [System.Activator]::CreateInstance($tDev)
$ms.DeviceId = "MCHOSE:3837:1018"
$ms.Brand = "MCHOSE"
$ms.DeviceName = "MCHOSE K7 V2 Ultra+"
$ms.Category = [RazerBatteryTray.DeviceCategory]::Mouse
$ms.IsConnected = $false          # 2.4G 鼠标刚启动时通常处于休眠占位
$ms.IsDonglePresent = $true       # 但接收器已经在场
$ms.IsPrimary = $false
$ms.BatteryPercent = 77
$ms.Capabilities = [System.Activator]::CreateInstance($tCaps)
$ms.Capabilities.SupportsDpi = $true
$ms.Capabilities.SupportsDpiStages = $true
$ms.Capabilities.SupportsPollingRate = $true

$devList.Add($kb)
$devList.Add($ms)

$form = [System.Activator]::CreateInstance($tMain, @([bool]$false))
$fActive.SetValue($form, $devList)
$fLastInfo.SetValue($form, $kb)

# ---- 1. 无记忆冷启动：应优选可调节鼠标（即便休眠占位） ----
$fRemember.SetValue($form, $null)
$pick = $mPref.Invoke($form, $null)
if ($pick -eq $null) { throw "FAIL: startup preference returned null." }
if ($pick.DeviceId -ne "MCHOSE:3837:1018") {
    throw ("FAIL: cold start without memory should prefer the DPI-capable mouse, got " + $pick.DeviceId)
}
Write-Host "  [PASS] cold start (no memory) prefers sleep-standing mouse 3837:1018" -ForegroundColor Green

# ---- 2. 记忆指向休眠鼠标：仍应被识别（接收器在场即可选） ----
$fRemember.SetValue($form, "MCHOSE:3837:1018")
$found = $mFind.Invoke($form, (,$devList))
if ($found -eq $null) {
    throw "FAIL: remembered asleep device with dongle present should still be found."
}
Write-Host "  [PASS] remembered asleep device is found while its dongle is present" -ForegroundColor Green

# ---- 3. 记忆指向键盘：应恢复键盘 ----
$fRemember.SetValue($form, "MCHOSE:3837:3033")
$r2 = $mPref.Invoke($form, $null)
if ($r2 -eq $null -or $r2.DeviceId -ne "MCHOSE:3837:3033") {
    throw "FAIL: remembered keyboard should be restored."
}
Write-Host "  [PASS] remembered keyboard is restored" -ForegroundColor Green

# ---- 4. 记忆指向不存在的设备：安全回退到可用优选 ----
$fRemember.SetValue($form, "GHOST:0000:0000")
$found2 = $mFind.Invoke($form, (,$devList))
if ($found2 -ne $null) {
    throw "FAIL: stale device id must not resolve to any real device."
}
$r4 = $mPref.Invoke($form, $null)
if ($r4 -eq $null -or $r4.DeviceId -eq "GHOST:0000:0000") {
    throw "FAIL: stale memory must fall back to a real device."
}
Write-Host "  [PASS] stale memory falls back safely without inventing a device" -ForegroundColor Green

# ---- 5. 托盘主设备不受影响 ----
$p1 = $mPrimary.Invoke($form, $null)
if ($p1 -eq $null -or $p1.DeviceId -ne "MCHOSE:3837:3033") {
    throw "FAIL: GetPrimaryDevice must keep returning the tray primary (keyboard)."
}
Write-Host "  [PASS] tray primary device stays unchanged (keyboard)" -ForegroundColor Green

$form.Dispose()

Write-Host "[ALL PASSED] Device focus memory test succeeded!" -ForegroundColor Green
exit 0
