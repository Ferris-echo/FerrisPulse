# ============================================================================
# 测试脚本: test_game_process_manager_ui_improvements.ps1
# 验证内容:
#   1. 默认出厂自带游戏列表已彻底清空 (全新环境 monitoredGameProcesses 初始 Count == 0)
#   2. 右侧守护列表高度由 184px 扩展至 246px，消除压抑局促感
#   3. 模式说明卡片高度由 52px 提升至 68px，两行文字完整显示杜绝截断
#   4. 进程列表控件内置悬停 ToolTip 机制，支持长标题与进程路径无损查看
#   5. 界面全面接入 DpiScaleHelper 缩放体系，自绘制字体与控件树等比缩放
# ============================================================================

$ErrorActionPreference = "Stop"

$workspace = Split-Path -Parent $PSScriptRoot
$exePath = Join-Path $workspace "FerrisPulse.exe"

if (-not (Test-Path $exePath)) {
    Write-Error "FerrisPulse.exe 不存在，请先执行 build.bat"
}

# 铁律 10: 内存反射加载二进制，严禁文件句柄锁死
$bytes = [System.IO.File]::ReadAllBytes($exePath)
$assembly = [System.Reflection.Assembly]::Load($bytes)

Write-Host "--- 1. 验证默认游戏列表清空 (注册表隔离保护) ---" -ForegroundColor Cyan
$regKeyPath = "HKCU:\Software\FerrisPulse"
$savedRegVal = $null
$hasRegKey = Test-Path $regKeyPath
if ($hasRegKey) {
    $itemProps = Get-ItemProperty -Path $regKeyPath -ErrorAction SilentlyContinue
    if ($null -ne $itemProps -and $null -ne $itemProps.MonitoredGameProcesses) {
        $savedRegVal = $itemProps.MonitoredGameProcesses
        Remove-ItemProperty -Path $regKeyPath -Name "MonitoredGameProcesses" -ErrorAction SilentlyContinue
    }
}

try {
    $mainFormType = $assembly.GetType("RazerBatteryTray.MainForm")
    $mainForm = [System.Activator]::CreateInstance($mainFormType, [object[]]@($false))

    $monitoredField = $mainFormType.GetField("monitoredGameProcesses", [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::NonPublic)
    if ($null -eq $monitoredField) {
        Write-Error "未能反射获取 monitoredGameProcesses 字段"
    }

    $defaultList = $monitoredField.GetValue($mainForm)
    Write-Host "全新出厂默认游戏列表数量: $($defaultList.Count)"
    if ($defaultList.Count -ne 0) {
        Write-Error "默认出厂游戏列表应为空列表，但实际包含 $($defaultList.Count) 项: $($defaultList -join ', ')"
    }
    Write-Host "[PASS] 默认出厂预设游戏已成功清空为 0 款" -ForegroundColor Green
    $mainForm.Dispose()
}
finally {
    if ($hasRegKey -and $null -ne $savedRegVal) {
        Set-ItemProperty -Path $regKeyPath -Name "MonitoredGameProcesses" -Value $savedRegVal
    }
}

Write-Host "`n--- 2. 验证右栏守护列表扩容与模式卡片扩高 (1.0x 缩放基准) ---" -ForegroundColor Cyan
$formType = $assembly.GetType("RazerBatteryTray.GameProcessManagerForm")
$dummyCb = [System.Action[System.Collections.Generic.List[string], bool, int]]{ param($l, $f, $m) }
$form10 = [System.Activator]::CreateInstance($formType, [object[]]@([float]1.0, [System.Collections.Generic.List[string]]@(), $false, 1, $dummyCb))

# 反射获取 ctlMonitoredList 和 pnlModeCard
$ctlMonitoredField = $formType.GetField("ctlMonitoredList", [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::NonPublic)
$pnlModeCardField = $formType.GetField("pnlModeCard", [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::NonPublic)
$lblModeTitleField = $formType.GetField("lblModeTitle", [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::NonPublic)

$ctlMonitored = $ctlMonitoredField.GetValue($form10)
$pnlModeCard = $pnlModeCardField.GetValue($form10)
$lblModeTitle = $lblModeTitleField.GetValue($form10)

Write-Host "ctlMonitoredList 高度: $($ctlMonitored.Height) px (预期: 246px)"
if ($ctlMonitored.Height -ne 246) {
    Write-Error "ctlMonitoredList 高度不符合预期 246px，实测为 $($ctlMonitored.Height)"
}
Write-Host "[PASS] ctlMonitoredList 舒展高度 246px 验证通过" -ForegroundColor Green

Write-Host "pnlModeCard 高度: $($pnlModeCard.Height) px (预期: 68px，防文字裁切)"
if ($pnlModeCard.Height -ne 68) {
    Write-Error "pnlModeCard 高度不符合预期 68px，实测为 $($pnlModeCard.Height)"
}
Write-Host "[PASS] pnlModeCard 充裕高度 68px 验证通过" -ForegroundColor Green

Write-Host "lblModeTitle Y 坐标: $($lblModeTitle.Location.Y) px (预期: 330px，整体下移)"
if ($lblModeTitle.Location.Y -ne 330) {
    Write-Error "lblModeTitle 位置未下移，实测 Y 为 $($lblModeTitle.Location.Y)"
}
Write-Host "[PASS] 策略控制模块下移布局对齐验证通过" -ForegroundColor Green

Write-Host "`n--- 3. 验证 ModernProcessListControl 内置 ToolTip 机制 ---" -ForegroundColor Cyan
$listCtrlType = $assembly.GetType("RazerBatteryTray.ModernProcessListControl")
$tipField = $listCtrlType.GetField("itemToolTip", [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::NonPublic)
if ($null -eq $tipField) {
    Write-Error "未能反射获取 itemToolTip 字段"
}

$tipInst = $tipField.GetValue($ctlMonitored)
if ($null -eq $tipInst) {
    Write-Error "ModernProcessListControl 内部 itemToolTip 未正确初始化"
}
Write-Host "[PASS] ModernProcessListControl 悬停 ToolTip 组件初始化正常" -ForegroundColor Green

# 验证 ToolTip 文本格式化方法 BuildItemTooltip
$buildMethod = $listCtrlType.GetMethod("BuildItemTooltip", [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::NonPublic)
if ($null -ne $buildMethod) {
    $itemType = $assembly.GetType("RazerBatteryTray.ProcessListItem")
    $itemInst = [System.Activator]::CreateInstance($itemType, [object[]]@("long_game.exe", "Very Long Window Title - Gaming Session", "已纳入电竞性能守护名单", $null, $null))
    $tipText = $buildMethod.Invoke($ctlMonitored, @($itemInst))
    Write-Host "格式化后的 ToolTip 文本:`n$tipText"
    if (-not $tipText.Contains("Very Long Window Title") -or -not $tipText.Contains("long_game.exe")) {
        Write-Error "ToolTip 未能完整呈现长标题与进程名称"
    }
    Write-Host "[PASS] ToolTip 文本格式化完整度验证通过" -ForegroundColor Green
}
$form10.Dispose()

Write-Host "`n--- 4. 验证全局无级缩放体系传递 (1.25x 缩放) ---" -ForegroundColor Cyan
$form125 = [System.Activator]::CreateInstance($formType, [object[]]@([float]1.25, [System.Collections.Generic.List[string]]@(), $false, 1, $dummyCb))
$ctlRunningField = $formType.GetField("ctlRunningList", [System.Reflection.BindingFlags]::Instance -bor [System.Reflection.BindingFlags]::NonPublic)
$ctlRunning125 = $ctlRunningField.GetValue($form125)
$ctlMonitored125 = $ctlMonitoredField.GetValue($form125)

Write-Host "1.25x 缩放下 ctlRunningList.DpiScale: $($ctlRunning125.DpiScale)"
Write-Host "1.25x 缩放下 ctlMonitoredList.DpiScale: $($ctlMonitored125.DpiScale)"

if ([Math]::Abs($ctlRunning125.DpiScale - 1.25) -gt 0.01 -or [Math]::Abs($ctlMonitored125.DpiScale - 1.25) -gt 0.01) {
    Write-Error "DpiScale 属性未正确传递至列表控件"
}
Write-Host "[PASS] DpiScale 属性正确传递至子控件与自绘制引擎" -ForegroundColor Green
$form125.Dispose()

Write-Host "`n=======================================================" -ForegroundColor Green
Write-Host "ALL 5 UI IMPROVEMENT TEST CASES PASSED SUCCESSFULLY!" -ForegroundColor Green
Write-Host "=======================================================" -ForegroundColor Green
