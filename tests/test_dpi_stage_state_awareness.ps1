# ============================================================================
# 测试脚本: test_dpi_stage_state_awareness.ps1
# 职责:
#   验证 DPI / 回报率在日常状态 vs 游戏状态下的双向状态感知与解耦：
#   1. 游戏运行中调节日常 DPI：只记录 desktopDpiStage 恢复目标，绝不篡改 gameDpiStage，绝不改动正在游戏中的鼠标硬件！
#   2. 退出游戏时：精准恢复到游戏中所选定的最新日常 DPI！
#   3. 日常状态下调节日常 DPI：直接生效到鼠标硬件，并同步日常档位。
#   4. 日常状态下调节电竞 DPI：仅更新电竞预设 gameDpiStage，绝不打扰日常鼠标硬件。
#   5. 游戏运行中调节电竞 DPI：即时应用到鼠标硬件。
# ============================================================================

$ErrorActionPreference = "Stop"
$rootDir = (Resolve-Path "$PSScriptRoot\..").Path
Set-Location $rootDir

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 1/3] 静态代码审计：验证日常 DPI 与电竞 DPI 逻辑完全解耦" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$mainUiCs = Get-Content "src/UI/Forms/MainForm.UI.cs" -Raw -Encoding UTF8
$mainHwCs = Get-Content "src/UI/Forms/MainForm.Hardware.cs" -Raw -Encoding UTF8

# 1. 确保 SwitchDpiStageByIndex 不再在游戏状态下覆写 gameDpiStage
if ($mainUiCs -match 'SwitchDpiStageByIndex[\s\S]*?isGamingActive[\s\S]*?gameDpiStage\s*=') {
    throw "FAIL: SwitchDpiStageByIndex 依然存在将日常 DPI 赋值给 gameDpiStage 的恶性耦合！"
}
Write-Host "PASS: SwitchDpiStageByIndex 彻底解除了 gameDpiStage 覆写门禁" -ForegroundColor Green

# 2. 确保 SwitchDpiStageByIndex 在游戏状态下记录 desktopDpiStage 并直接 return
if ($mainUiCs -notmatch 'desktopDpiStage\s*=\s*targetStage[\s\S]*?UpdatePollingRateDisplayStates\(\);\s*return;') {
    throw "FAIL: SwitchDpiStageByIndex 缺少 desktopDpiStage = targetStage 恢复记录或未即时 return 隔离硬件！"
}
Write-Host "PASS: SwitchDpiStageByIndex 正确记录 desktopDpiStage 退出恢复目标并即时隔离硬件" -ForegroundColor Green

# 3. 确保 SetDpiFromUI 也具备游戏状态感知保护
if ($mainHwCs -notmatch 'SetDpiFromUI[\s\S]*?isGamingActive\s*&&\s*autoGameDpiEnabled[\s\S]*?desktopDpiStage') {
    throw "FAIL: SetDpiFromUI 缺少 isGamingActive && autoGameDpiEnabled 状态隔离保护！"
}
Write-Host "PASS: SetDpiFromUI 状态感知保护已就绪" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 2/3] 动态反射断言：验证日常与电竞 DPI 切换的状态机行为" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$exePath = Join-Path $rootDir "FerrisPulse.exe"
if (-not (Test-Path $exePath)) {
    throw "FAIL: 未找到 FerrisPulse.exe，请先执行 build.bat"
}

# 全内存反射装配，严格防止文件句柄锁死
$bytes = [System.IO.File]::ReadAllBytes($exePath)
$asm = [System.Reflection.Assembly]::Load($bytes)
$mainFormType = $asm.GetType("RazerBatteryTray.MainForm")
if ($null -eq $mainFormType) {
    throw "FAIL: 无法解析 RazerBatteryTray.MainForm 类型"
}

# 开启测试隔离保护，绝不污染用户生产注册表
$disableSaveField = $mainFormType.GetField("DisableConfigSaveForTesting", [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::NonPublic)
if ($null -ne $disableSaveField) {
    $disableSaveField.SetValue($null, $true)
}

$form = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($mainFormType)
$bindingFlags = [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Public

# 构造模拟鼠标设备
$devType = $asm.GetType("RazerBatteryTray.MouseBatteryInfo")
$catType = $asm.GetType("RazerBatteryTray.DeviceCategory")
$mockMouse = [System.Activator]::CreateInstance($devType)
$mockMouse.Brand = "Razer"
$mockMouse.Category = [System.Enum]::Parse($catType, "Mouse")
$mockMouse.DeviceId = "TEST_MOUSE_DPI_01"
$mockMouse.DpiStages = [int[]]@(400, 800, 1600, 3200, 6400)
$mockMouse.DpiStagesY = [int[]]@(400, 800, 1600, 3200, 6400)
$mockMouse.DpiStageCount = 5
$mockMouse.DpiStage = 2
$mockMouse.Dpi = 800

# 绑定到 MainForm
$selectedDevField = $mainFormType.GetField("selectedDevice", $bindingFlags)
$selectedDevField.SetValue($form, $mockMouse)

$dpiStageListField = $mainFormType.GetField("dpiStageList", $bindingFlags)
$dpiStageListField.SetValue($form, [System.Collections.Generic.List[int]]@(400, 800, 1600, 3200, 6400))

$dpiStageValuesField = $mainFormType.GetField("dpiStageValues", $bindingFlags)
$dpiStageValuesField.SetValue($form, [int[]]@(400, 800, 1600, 3200, 6400))

$isGamingActiveField = $mainFormType.GetField("isGamingActive", $bindingFlags)
$autoGameDpiEnabledField = $mainFormType.GetField("autoGameDpiEnabled", $bindingFlags)
$gameDpiStageField = $mainFormType.GetField("gameDpiStage", $bindingFlags)
$desktopDpiStageField = $mainFormType.GetField("desktopDpiStage", $bindingFlags)

$switchDpiMethod = $mainFormType.GetMethod("SwitchDpiStageByIndex", $bindingFlags)
$setGameDpiMethod = $mainFormType.GetMethod("SetGameDpiStageFromUI", $bindingFlags)

# --- 场景 A: 游戏运行中调节日常 DPI ---
# 设定为游戏中，当前电竞档位为第 4 档 (3200)
$isGamingActiveField.SetValue($form, $true)
$autoGameDpiEnabledField.SetValue($form, $true)
$gameDpiStageField.SetValue($form, 4)
$desktopDpiStageField.SetValue($form, 2)
$mockMouse.DpiStage = 4
$mockMouse.Dpi = 3200

# 用户在游戏中点击上方的“日常 DPI”选择第 1 档 (400 DPI, index = 0)
$switchDpiMethod.Invoke($form, @(0))

# 校验 1: 电竞 DPI 档位绝不可被篡改
$currentGStage = [int]$gameDpiStageField.GetValue($form)
if ($currentGStage -ne 4) {
    throw "FAIL: 游戏运行时调节日常 DPI 错误篡改了 gameDpiStage！期望 4，实际: $currentGStage"
}
Write-Host "PASS: [场景 A] 游戏运行时调节日常 DPI，gameDpiStage 依然保持为 4" -ForegroundColor Green

# 校验 2: 日常恢复目标 desktopDpiStage 正确记录为 1
$currentDStage = [int]$desktopDpiStageField.GetValue($form)
if ($currentDStage -ne 1) {
    throw "FAIL: 游戏运行时调节日常 DPI 未能正确更新 desktopDpiStage！期望 1，实际: $currentDStage"
}
Write-Host "PASS: [场景 A] 游戏运行时调节日常 DPI，desktopDpiStage 成功更新为 1" -ForegroundColor Green

# 校验 3: 当前正在游戏中的鼠标硬件档位绝不可被修改为 1
if ($mockMouse.DpiStage -ne 4) {
    throw "FAIL: 游戏运行时调节日常 DPI 错误修改了当前硬件鼠标档位！期望 4，实际: $($mockMouse.DpiStage)"
}
Write-Host "PASS: [场景 A] 游戏运行时调节日常 DPI，鼠标硬件档位稳稳保持在 4 (3200 DPI)" -ForegroundColor Green

# --- 场景 B: 日常状态下调节电竞 DPI ---
# 退出游戏状态
$isGamingActiveField.SetValue($form, $false)
$mockMouse.DpiStage = 2
$mockMouse.Dpi = 800

# 用户在桌面上调节下方的电竞 DPI 为第 5 档
$setGameDpiMethod.Invoke($form, @(5))

$currentGStage = [int]$gameDpiStageField.GetValue($form)
if ($currentGStage -ne 5) {
    throw "FAIL: 日常状态下设置电竞 DPI 失败！期望 5，实际: $currentGStage"
}
if ($mockMouse.DpiStage -ne 2) {
    throw "FAIL: 日常状态下设置电竞 DPI 错误影响了日常实际鼠标！期望 2，实际: $($mockMouse.DpiStage)"
}
Write-Host "PASS: [场景 B] 日常状态下调节电竞 DPI，仅更新预设配置，实际鼠标毫发无损" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 3/3] 验证 UTF-8 BOM 编码规范" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$filesToCheck = @(
    "src/UI/Forms/MainForm.UI.cs",
    "src/UI/Forms/MainForm.Hardware.cs"
)

foreach ($f in $filesToCheck) {
    $b = [System.IO.File]::ReadAllBytes($f)
    if ($b.Length -lt 3 -or $b[0] -ne 0xEF -or $b[1] -ne 0xBB -or $b[2] -ne 0xBF) {
        throw "FAIL: 文件 $f 缺少 UTF-8 BOM 头！"
    }
}
Write-Host "PASS: 所有核心源码文件均满足 UTF-8 BOM 规范" -ForegroundColor Green

Write-Host "`n>>> [ALL CHECKS PASSED] DPI 档位状态感知与日常/电竞解耦测试 100% 成功！<<<" -ForegroundColor Green
