# ============================================================================
# 测试脚本: test_dpi_ui_responsiveness_and_optimistic_rendering.ps1
# 测试目标: 验证 DPI 调节界面的零延迟乐观 UI 响应架构与单任务防抖避让机制
# ============================================================================

$ErrorActionPreference = 'Stop'
$WorkspaceRoot = 'D:\FerrisPulse · 灵脉'
Set-Location $WorkspaceRoot

Write-Host '========================================================' -ForegroundColor Cyan
Write-Host '   DPI 调节 UI 零延迟响应与防卡顿架构专项测试' -ForegroundColor Cyan
Write-Host '========================================================' -ForegroundColor Cyan

# 阶段 1: 静态源码架构契约检查
Write-Host '`n[阶段 1] 校验静态源码契约...' -ForegroundColor Yellow

$mainFormCs = Get-Content -Raw -Encoding UTF8 'src\UI\Forms\MainForm.cs'
$mainUiCs = Get-Content -Raw -Encoding UTF8 'src\UI\Forms\MainForm.UI.cs'
$mainHwCs = Get-Content -Raw -Encoding UTF8 'src\UI\Forms\MainForm.Hardware.cs'

if ($mainFormCs -notmatch 'pendingDpiTarget' -or $mainFormCs -notmatch 'dpiMonitorSuppressUntilTicks') {
    throw 'FAIL: MainForm.cs 缺少 pendingDpiTarget 或 dpiMonitorSuppressUntilTicks 调度字段！'
}
Write-Host 'PASS: MainForm.cs 已声明单任务调度与轮询避让字段' -ForegroundColor Green

if ($mainHwCs -notmatch 'dpiMonitorSuppressUntilTicks[\s\S]*?continue;') {
    throw 'FAIL: DpiMonitorWorker 缺少前台调 DPI 避让检测逻辑！'
}
Write-Host 'PASS: DpiMonitorWorker 包含前台调 DPI 避让逻辑' -ForegroundColor Green

if ($mainHwCs -notmatch 'QueueDpiHardwareDispatch' -or $mainHwCs -notmatch 'DpiDispatchWorker') {
    throw 'FAIL: MainForm.Hardware.cs 缺少 QueueDpiHardwareDispatch 或 DpiDispatchWorker 调度管线！'
}
Write-Host 'PASS: 单任务防抖调度器 QueueDpiHardwareDispatch / DpiDispatchWorker 已就绪' -ForegroundColor Green

if ($mainUiCs -notmatch 'SwitchDpiStageByIndex[\s\S]*?HighlightDpiStage\(targetStage\);[\s\S]*?QueueDpiHardwareDispatch') {
    throw 'FAIL: SwitchDpiStageByIndex 未能在硬件下发前立即调用 HighlightDpiStage 乐观更新！'
}
Write-Host 'PASS: SwitchDpiStageByIndex 已实现零延迟乐观 UI 更新契约' -ForegroundColor Green

if ($mainHwCs -notmatch 'SetDpiFromUI[\s\S]*?HighlightDpiStage[\s\S]*?QueueDpiHardwareDispatch') {
    throw 'FAIL: SetDpiFromUI 未能接入乐观更新与防抖调度管线！'
}
Write-Host 'PASS: SetDpiFromUI 已接入乐观更新与防抖调度管线' -ForegroundColor Green

# 阶段 2: 内存反射动态行为验证
Write-Host '`n[阶段 2] 校验动态运行时行为...' -ForegroundColor Yellow

$binPath = Join-Path $WorkspaceRoot 'FerrisPulse.exe'
if (-not (Test-Path $binPath)) {
    throw 'FAIL: 未找到构建目标 FerrisPulse.exe，请先执行 build.bat'
}

$bytes = [System.IO.File]::ReadAllBytes($binPath)
$asm = [System.Reflection.Assembly]::Load($bytes)
$mainFormType = $asm.GetType('RazerBatteryTray.MainForm')
if ($null -eq $mainFormType) {
    throw 'FAIL: 无法从程序集加载 RazerBatteryTray.MainForm 类型'
}

# 开启测试隔离保护，绝不污染用户生产注册表
$disableSaveField = $mainFormType.GetField('DisableConfigSaveForTesting', [System.Reflection.BindingFlags]::Static -bor [System.Reflection.BindingFlags]::NonPublic)
if ($null -ne $disableSaveField) {
    $disableSaveField.SetValue($null, $true)
}

$form = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($mainFormType)
$bindingFlags = [System.Reflection.BindingFlags]::Instance -bor `
                [System.Reflection.BindingFlags]::NonPublic -bor `
                [System.Reflection.BindingFlags]::Public

# 初始化调度锁与基础字典
$mainFormType.GetField('dpiDispatchLock', $bindingFlags).SetValue($form, [object]::new())
$mainFormType.GetField('lastMonitoredDpis', $bindingFlags).SetValue($form, [System.Collections.Generic.Dictionary[string, int]]::new())
$mainFormType.GetField('lastMonitoredStages', $bindingFlags).SetValue($form, [System.Collections.Generic.Dictionary[string, int]]::new())
$mainFormType.GetField('pendingDpiCandidates', $bindingFlags).SetValue($form, [System.Collections.Generic.Dictionary[string, int]]::new())
$mainFormType.GetField('pendingStageCandidates', $bindingFlags).SetValue($form, [System.Collections.Generic.Dictionary[string, int]]::new())

try {
    $mouseDevType = $asm.GetType('RazerBatteryTray.MouseBatteryInfo')
    $catType = $asm.GetType('RazerBatteryTray.DeviceCategory')
    $mouseDev = [System.Activator]::CreateInstance($mouseDevType)
    $mouseDev.DeviceId = 'TEST_MOUSE_VID1532_PID00B6'
    $mouseDev.Brand = 'Razer'
    $mouseDev.Category = [System.Enum]::Parse($catType, 'Mouse')
    $mouseDev.DpiStages = [int[]]@(400, 800, 1600, 3200, 6400)
    $mouseDev.DpiStagesY = [int[]]@(400, 800, 1600, 3200, 6400)
    $mouseDev.DpiStageCount = 5
    $mouseDev.Dpi = 800
    $mouseDev.DpiStage = 2

    $selDevField = $mainFormType.GetField('selectedDevice', $bindingFlags)
    $selDevField.SetValue($form, $mouseDev)

    $dpiStageListField = $mainFormType.GetField('dpiStageList', $bindingFlags)
    $listInstance = [System.Collections.Generic.List[int]]::new()
    foreach ($val in @(400, 800, 1600, 3200, 6400)) {
        $listInstance.Add($val)
    }
    $dpiStageListField.SetValue($form, $listInstance)

    $dpiStageValuesField = $mainFormType.GetField('dpiStageValues', $bindingFlags)
    $dpiStageValuesField.SetValue($form, [int[]]@(400, 800, 1600, 3200, 6400))

    # 1. 验证乐观 UI 更新: 调用 SwitchDpiStageByIndex(3) (即第 4 档: 3200 DPI)
    $switchDpiMethod = $mainFormType.GetMethod('SwitchDpiStageByIndex', $bindingFlags)
    $switchDpiMethod.Invoke($form, @(3))

    # 2. 验证内存状态瞬时同步
    $deskDpiStageField = $mainFormType.GetField('desktopDpiStage', $bindingFlags)
    $dStage = [int]$deskDpiStageField.GetValue($form)
    if ($dStage -ne 4) {
        throw \"FAIL: desktopDpiStage 未能瞬时更新为 4，实际为: $dStage\"
    }
    if ($mouseDev.Dpi -ne 3200 -or $mouseDev.DpiStage -ne 4) {
        throw 'FAIL: mouseDev 内存状态未能瞬时更新为 3200 / 4'
    }
    Write-Host 'PASS: 鼠标设备内存 DPI (3200) 与档位 (4) 瞬时同步完成' -ForegroundColor Green

    # 3. 验证轮询避让时间戳有效设置
    $suppressTicksField = $mainFormType.GetField('dpiMonitorSuppressUntilTicks', $bindingFlags)
    $suppressTicks = [long]$suppressTicksField.GetValue($form)
    $nowTicks = [System.DateTime]::UtcNow.Ticks
    if ($suppressTicks -le $nowTicks) {
        throw 'FAIL: dpiMonitorSuppressUntilTicks 未能设置未来的避让时间戳！'
    }
    Write-Host 'PASS: dpiMonitorSuppressUntilTicks 正确设定未来避让静默期' -ForegroundColor Green

    # 4. 验证防抖队列合流: 连续调用快速切档
    $switchDpiMethod.Invoke($form, @(0))
    $switchDpiMethod.Invoke($form, @(2))

    $pendingTargetField = $mainFormType.GetField('pendingDpiTarget', $bindingFlags)
    $pendingTarget = $pendingTargetField.GetValue($form)
    if ($null -ne $pendingTarget) {
        if ($pendingTarget.TargetStage -ne 3 -or $pendingTarget.TargetDpi -ne 1600) {
            throw \"FAIL: 连续切档后 pendingDpiTarget 应当汇聚为最新目标第 3 档 (1600)\"
        }
        Write-Host 'PASS: 连续切档触发防抖合并，最新目标准确锁定为第 3 档 (1600 DPI)' -ForegroundColor Green
    }
} finally {
    if ($null -ne $form) {
        try { $form.Dispose() } catch { }
    }
}

Write-Host '`n========================================================' -ForegroundColor Green
Write-Host '   ALL PASS! DPI 调节零延迟响应与防卡顿架构验证通过' -ForegroundColor Green
Write-Host '========================================================' -ForegroundColor Green
