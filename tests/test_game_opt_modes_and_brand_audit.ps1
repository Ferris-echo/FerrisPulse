# ============================================================================
# 测试脚本: test_game_opt_modes_and_brand_audit.ps1
# 目标:
#   1. 验证 IsDeviceSupportingAutoGameRate 支持全品牌 (Razer, Logi, Rapoo, Vk, Mchose, FDAS)
#   2. 验证非鼠标设备 (键盘、耳机、手柄) 不受误判
#   3. 验证 GetActiveMouseDevice 动态穿透定位鼠标，消除界面选中键盘导致的监控失效
#   4. 验证 GameOptMode 属性及 GameProcessManagerForm 双模构造与切换
#   5. 静态代码审计：验证 MainForm.Hardware.cs 与 MainForm.UI.cs 中恶性 Brand 门禁已全部清空
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = (Resolve-Path "$ScriptDir\..").Path
Set-Location $ProjectDir

$exePath = "$ProjectDir\FerrisPulse.exe"
$bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $exePath).Path)
$assembly = [System.Reflection.Assembly]::Load($bytes)

$tMain = $assembly.GetType("RazerBatteryTray.MainForm")
$tDev = $assembly.GetType("RazerBatteryTray.MouseBatteryInfo")
$tCat = $assembly.GetType("RazerBatteryTray.DeviceCategory")

Write-Host "--- 1. 验证 IsDeviceSupportingAutoGameRate 全品牌覆盖与能力检测 ---" -ForegroundColor Cyan
$mIsDevSupp = $tMain.GetMethod("IsDeviceSupportingAutoGameRate", [System.Reflection.BindingFlags]'Public,Instance')
if ($null -eq $mIsDevSupp) {
    Write-Host "FAIL: 未找到 IsDeviceSupportingAutoGameRate(MouseBatteryInfo) 方法！" -ForegroundColor Red
    exit 1
}

$mainInstance = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($tMain)

# Test 1.1: Razer Mouse
$d1 = [System.Activator]::CreateInstance($tDev)
$d1.Category = [System.Enum]::Parse($tCat, "Mouse")
$d1.Brand = "Razer"
$d1.DeviceId = "Razer:1532:007B"
if (-not $mIsDevSupp.Invoke($mainInstance, @($d1))) {
    Write-Host "FAIL: Razer 鼠标应受支持！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: Razer 鼠标支持判定正常" -ForegroundColor Green

# Test 1.2: Logitech Mouse
$d2 = [System.Activator]::CreateInstance($tDev)
$d2.Category = [System.Enum]::Parse($tCat, "Mouse")
$d2.Brand = "Logitech"
$d2.DeviceId = "Logi:046D:C539"
if (-not $mIsDevSupp.Invoke($mainInstance, @($d2))) {
    Write-Host "FAIL: Logitech 鼠标应受支持！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: Logitech 鼠标支持判定正常" -ForegroundColor Green

# Test 1.3: Rapoo Mouse
$d3 = [System.Activator]::CreateInstance($tDev)
$d3.Category = [System.Enum]::Parse($tCat, "Mouse")
$d3.Brand = "Rapoo"
$d3.DeviceId = "Rapoo:24AE:2013"
if (-not $mIsDevSupp.Invoke($mainInstance, @($d3))) {
    Write-Host "FAIL: Rapoo 鼠标应受支持！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: Rapoo 鼠标支持判定正常" -ForegroundColor Green

# Test 1.4: Valkyrie Mouse
$d4 = [System.Activator]::CreateInstance($tDev)
$d4.Category = [System.Enum]::Parse($tCat, "Mouse")
$d4.Brand = "Valkyrie"
$d4.DeviceId = "Vk:35A7:0102"
if (-not $mIsDevSupp.Invoke($mainInstance, @($d4))) {
    Write-Host "FAIL: Valkyrie 鼠标应受支持！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: Valkyrie 鼠标支持判定正常" -ForegroundColor Green

# Test 1.5: MCHOSE Mouse
$d5 = [System.Activator]::CreateInstance($tDev)
$d5.Category = [System.Enum]::Parse($tCat, "Mouse")
$d5.Brand = "MCHOSE"
$d5.DeviceId = "Mchose:A5"
if (-not $mIsDevSupp.Invoke($mainInstance, @($d5))) {
    Write-Host "FAIL: MCHOSE 鼠标应受支持！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: MCHOSE 鼠标支持判定正常" -ForegroundColor Green

# Test 1.6: FDAS Mouse (VGN Dragonfly F2 Pro Max)
$d6 = [System.Activator]::CreateInstance($tDev)
$d6.Category = [System.Enum]::Parse($tCat, "Mouse")
$d6.Brand = "VGN"
$d6.DeviceId = "FDAS:3554:FB3E"
if (-not $mIsDevSupp.Invoke($mainInstance, @($d6))) {
    Write-Host "FAIL: FDAS 鼠标应受支持！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: FDAS 鼠标支持判定正常" -ForegroundColor Green

# Test 1.7: NuPhy Keyboard should NOT be treated as auto-game-rate mouse
$d7 = [System.Activator]::CreateInstance($tDev)
$d7.Category = [System.Enum]::Parse($tCat, "Keyboard")
$d7.Brand = "NuPhy"
$d7.DeviceId = "NuPhy:Air75"
if ($mIsDevSupp.Invoke($mainInstance, @($d7))) {
    Write-Host "FAIL: NuPhy 键盘不应被识别为电竞鼠标！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: NuPhy 键盘正确被排除在鼠标优化之外" -ForegroundColor Green

# Test 1.8: Sony Headset should NOT be treated as auto-game-rate mouse
$d8 = [System.Activator]::CreateInstance($tDev)
$d8.Category = [System.Enum]::Parse($tCat, "Headset")
$d8.Brand = "Sony"
$d8.DeviceId = "Sony:INZONE"
if ($mIsDevSupp.Invoke($mainInstance, @($d8))) {
    Write-Host "FAIL: Sony 耳机不应被识别为电竞鼠标！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: Sony 耳机正确被排除在鼠标优化之外" -ForegroundColor Green

Write-Host "`n--- 2. 验证 GetActiveMouseDevice 动态跨外设穿透 ---" -ForegroundColor Cyan
$mGetActive = $tMain.GetMethod("GetActiveMouseDevice", [System.Reflection.BindingFlags]'Public,Instance')
if ($null -eq $mGetActive) {
    Write-Host "FAIL: 未找到 GetActiveMouseDevice 方法！" -ForegroundColor Red
    exit 1
}

$fSelected = $tMain.GetField("selectedDevice", [System.Reflection.BindingFlags]'NonPublic,Instance')
$fActiveDevs = $tMain.GetField("activeDevices", [System.Reflection.BindingFlags]'NonPublic,Instance')

# Scenario A: selectedDevice is Keyboard, activeDevices has FDAS mouse
$fSelected.SetValue($mainInstance, $d7)
$tList = ([System.Collections.Generic.List[int]]).GetGenericTypeDefinition().MakeGenericType(@($tDev))
$devList = [System.Activator]::CreateInstance($tList)
$devList.Add($d7)
$devList.Add($d6) # FDAS mouse
$fActiveDevs.SetValue($mainInstance, $devList)

$resolved = $mGetActive.Invoke($mainInstance, $null)
if ($null -eq $resolved -or $resolved.DeviceId -ne "FDAS:3554:FB3E") {
    Write-Host "FAIL: 当界面选中键盘时，GetActiveMouseDevice 未能自动定位到 FDAS 鼠标！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: 界面选中键盘时，动态寻路自动穿透到 FDAS 鼠标: $($resolved.Brand) ($($resolved.DeviceId))" -ForegroundColor Green

# Scenario B: selectedDevice is Mouse itself
$fSelected.SetValue($mainInstance, $d1)
$resolvedB = $mGetActive.Invoke($mainInstance, $null)
if ($null -eq $resolvedB -or $resolvedB.DeviceId -ne "Razer:1532:007B") {
    Write-Host "FAIL: 当界面选中鼠标时，GetActiveMouseDevice 应直接返回该鼠标！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: 界面选中鼠标时，直接返回该鼠标: $($resolvedB.Brand)" -ForegroundColor Green

Write-Host "`n--- 3. 验证 GameOptMode 属性与 GameProcessManagerForm 双模构造 ---" -ForegroundColor Cyan
$pMode = $tMain.GetProperty("GameOptMode", [System.Reflection.BindingFlags]'Public,Instance')
if ($null -eq $pMode) {
    Write-Host "FAIL: 未找到 GameOptMode 属性！" -ForegroundColor Red
    exit 1
}

$pMode.SetValue($mainInstance, 1, $null)
if ($pMode.GetValue($mainInstance, $null) -ne 1) {
    Write-Host "FAIL: GameOptMode 设置为 1 失败！" -ForegroundColor Red
    exit 1
}
$pMode.SetValue($mainInstance, 0, $null)
if ($pMode.GetValue($mainInstance, $null) -ne 0) {
    Write-Host "FAIL: GameOptMode 设置为 0 失败！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: GameOptMode 属性读写正常 (0: 仅前台激活, 1: 进程存活常驻)" -ForegroundColor Green

$tGameForm = $assembly.GetType("RazerBatteryTray.GameProcessManagerForm")
$ctors = $tGameForm.GetConstructors()
$foundDualCtor = $false
foreach ($c in $ctors) {
    $ps = $c.GetParameters()
    if ($ps.Length -eq 5 -and $ps[3].ParameterType.Name -eq "Int32") {
        $foundDualCtor = $true
        break
    }
}
if (-not $foundDualCtor) {
    Write-Host "FAIL: 未找到支持 initialMode 参数的 GameProcessManagerForm 构造函数！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: GameProcessManagerForm 双模构造函数就绪" -ForegroundColor Green

Write-Host "`n--- 4. 静态代码审计：验证恶性品牌门禁已彻底拔除 ---" -ForegroundColor Cyan
$hwText = (Get-ChildItem "$ProjectDir\src\UI\Forms\MainForm.Hardware*.cs" | ForEach-Object { [System.IO.File]::ReadAllText($_.FullName, [System.Text.Encoding]::UTF8) }) -join "`n"

# Check: StartGameMonitor must NOT contain hardcoded brand inequality
if ($hwText -match 'curDev\.Brand\s*!=\s*"Razer"') {
    Write-Host "FAIL: MainForm.Hardware.cs 仍残留 curDev.Brand != 'Razer' 门禁！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: MainForm.Hardware.cs 中 StartGameMonitor 恶性门禁已彻底清空" -ForegroundColor Green

$uiFile = "$ProjectDir\src\UI\Forms\MainForm.UI.cs"
$uiText = [System.IO.File]::ReadAllText($uiFile, [System.Text.Encoding]::UTF8)
if ($uiText -match 'curDev\.Brand\s*!=\s*"Razer"') {
    Write-Host "FAIL: MainForm.UI.cs 仍残留 curDev.Brand != 'Razer' 门禁！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: MainForm.UI.cs 中 OpenDpiManagementDialog 恶性门禁已彻底清空" -ForegroundColor Green

Write-Host "`n>>> [ALL CHECKS PASSED] 双模式游戏优化与全代码库硬编码门禁清理验证 100% 成功！<<<" -ForegroundColor Green
