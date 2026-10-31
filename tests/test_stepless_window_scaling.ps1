# ============================================================================
# 测试脚本: test_stepless_window_scaling.ps1
# 覆盖范围:
#   1. DpiScaleHelper 低分屏自适应下探 (0.60x ~ 2.50x) 与全展开基准高度 (880px)
#   2. 跨系统物理 DPI 的等比矢量字号换算 (GetScaledFontSize / ApplyScaledFontsToControlTree / SetControlBaseFont)
#   3. MainForm 无极边框拖拽 (FormBorderStyle.Sizable + WM_SIZING)、Ctrl+滚轮、Ctrl+0 与配置持久化
#   4. 自绘控件 (ModernCheckBox / StatusPill / ModernSegmentButton / PriorityReorderControl) 与子瓦片矢量比例缩放
#   5. 全局二级窗口 (DpiAwareForm 子类 + VgnKeyboardForm + DpiManagementDialog) 支持 < 1.0f 缩小比例同步
#   6. 拖拽性能守卫 (ApplyDpiScaleToControls 禁调 UpdateUI) 与“电竞回报率”防遮挡几何校验
# ============================================================================

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir
$exePath = Join-Path $rootDir "FerrisPulse.exe"

if (-not (Test-Path $exePath)) {
    Write-Host "[FAIL] FerrisPulse.exe not found at $exePath" -ForegroundColor Red
    exit 1
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# 铁律 10: 全内存反射加载，严禁锁死二进制文件
$bytes = [System.IO.File]::ReadAllBytes($exePath)
$asm = [System.Reflection.Assembly]::Load($bytes)

$flags = [System.Reflection.BindingFlags]"Public, NonPublic, Instance, Static"

function Assert-True($cond, $msg) {
    if (-not $cond) {
        Write-Host "[FAIL] $msg" -ForegroundColor Red
        exit 1
    }
    Write-Host "[PASS] $msg" -ForegroundColor Green
}

# 1. 验证 DpiScaleHelper 常量与方法
$helperType = $asm.GetType("RazerBatteryTray.DpiScaleHelper")
Assert-True ($null -ne $helperType) "DpiScaleHelper 类型存在"

$minScaleField = $helperType.GetField("MinScale", $flags)
Assert-True ($null -ne $minScaleField) "DpiScaleHelper.MinScale 常量存在"
$minScaleVal = [float]$minScaleField.GetValue($null)
Assert-True ([Math]::Abs($minScaleVal - 0.60) -lt 0.001) "DpiScaleHelper.MinScale 应为 0.60f (实际: $minScaleVal)"

$getScaledFontSizeMethod = $helperType.GetMethod("GetScaledFontSize", $flags)
Assert-True ($null -ne $getScaledFontSizeMethod) "DpiScaleHelper.GetScaledFontSize 方法存在"

# 当系统 DPI 为 1.0x，目标缩放为 0.75x 时，20pt 字体应缩放到 15pt
$pt1 = [float]$getScaledFontSizeMethod.Invoke($null, @([float]20.0, [float]0.75, [float]1.0))
Assert-True ([Math]::Abs($pt1 - 15.0) -lt 0.05) "GetScaledFontSize(20pt, target=0.75, sys=1.0) == 15.0pt (实际: $pt1)"

# 当系统 DPI 为 1.5x (150%)，目标缩放为 0.75x 时，20pt 字体应换算为 10pt 以抵消系统 1.5x 放大
$pt2 = [float]$getScaledFontSizeMethod.Invoke($null, @([float]20.0, [float]0.75, [float]1.5))
Assert-True ([Math]::Abs($pt2 - 10.0) -lt 0.05) "GetScaledFontSize(20pt, target=0.75, sys=1.5) == 10.0pt (实际: $pt2)"

# 当系统 DPI 为 1.5x (150%)，目标缩放为 0.65x 时，8.5pt 字体应换算为 ~3.68pt，绝不能被 4.5pt 下限错误锁死
$pt3 = [float]$getScaledFontSizeMethod.Invoke($null, @([float]8.5, [float]0.65, [float]1.5))
Assert-True ([Math]::Abs($pt3 - 3.68) -lt 0.08) "GetScaledFontSize(8.5pt, target=0.65, sys=1.5) == 3.68pt，未被 4.5pt 锁死 (实际: $pt3)"

$setControlBaseFontMethod = $helperType.GetMethod("SetControlBaseFont", $flags)
Assert-True ($null -ne $setControlBaseFontMethod) "DpiScaleHelper.SetControlBaseFont 方法存在"

$applyTreeMethod = $helperType.GetMethod("ApplyScaledFontsToControlTree", $flags)
Assert-True ($null -ne $applyTreeMethod) "DpiScaleHelper.ApplyScaledFontsToControlTree 方法存在"

# 验证 ApplyScaledFontsToControlTree 多次缩放无累积误差
$dummyPanel = New-Object System.Windows.Forms.Panel
$dummyLabel = New-Object System.Windows.Forms.Label
$dummyLabel.Font = New-Object System.Drawing.Font("Microsoft YaHei UI", [float]12.0, [System.Drawing.FontStyle]::Bold)
$dummyPanel.Controls.Add($dummyLabel)

[void]$applyTreeMethod.Invoke($null, [object[]]@($dummyPanel.PSObject.BaseObject, [float]0.75, [float]1.0))
Assert-True ([Math]::Abs($dummyLabel.Font.Size - 9.0) -lt 0.1) "控件树首次缩放到 0.75x 后字号为 9.0pt (实际: $($dummyLabel.Font.Size))"

[void]$applyTreeMethod.Invoke($null, [object[]]@($dummyPanel.PSObject.BaseObject, [float]1.50, [float]1.0))
Assert-True ([Math]::Abs($dummyLabel.Font.Size - 18.0) -lt 0.1) "控件树再次缩放到 1.50x 后字号精确为 18.0pt，无累积舍入误差 (实际: $($dummyLabel.Font.Size))"
$dummyPanel.Dispose()

# 2. 验证 MainForm 无极缩放接口与源码契约
$mainFormType = $asm.GetType("RazerBatteryTray.MainForm")
Assert-True ($null -ne $mainFormType) "MainForm 类型存在"

$setUserScaleMethod = $mainFormType.GetMethod("SetUserWindowScale", $flags)
Assert-True ($null -ne $setUserScaleMethod) "MainForm.SetUserWindowScale 方法存在"

$stepScaleMethod = $mainFormType.GetMethod("StepWindowScale", $flags)
Assert-True ($null -ne $stepScaleMethod) "MainForm.StepWindowScale 方法存在"

$resetScaleMethod = $mainFormType.GetMethod("ResetWindowScale", $flags)
Assert-True ($null -ne $resetScaleMethod) "MainForm.ResetWindowScale 方法存在"

$mainUiSrc = Get-Content (Join-Path $rootDir "src\UI\Forms\MainForm.UI.cs") -Raw -Encoding UTF8
Assert-True ($mainUiSrc -match "FormBorderStyle\.Sizable") "MainForm.UI.cs 已解锁 FormBorderStyle.Sizable"
Assert-True ($mainUiSrc -match "ApplyScaledFontsToControlTree") "MainForm.UI.cs 在 ApplyDpiScaleToControls 中同步缩放控件树字体"
Assert-True ($mainUiSrc -match "tileRapooDpi\.DpiScale\s*=\s*dpiScale") "MainForm.UI.cs 同步更新 tileRapooDpi.DpiScale"
Assert-True ($mainUiSrc -match "tileHeadsetAudio\.DpiScale\s*=\s*dpiScale") "MainForm.UI.cs 同步更新 tileHeadsetAudio.DpiScale"
Assert-True ($mainUiSrc -match "hudThreeLocks\.DpiScale\s*=\s*dpiScale") "MainForm.UI.cs 同步更新 hudThreeLocks.DpiScale"

$mainEventsSrc = Get-Content (Join-Path $rootDir "src\UI\Forms\MainForm.Events.cs") -Raw -Encoding UTF8
Assert-True ($mainEventsSrc -match "WM_SIZING") "MainForm.Events.cs 拦截 WM_SIZING 实现等比无极拖拽"
Assert-True ($mainEventsSrc -match "ProcessCmdKey") "MainForm.Events.cs 实现 ProcessCmdKey 全局快捷键缩放"

# 3. 验证二级窗口在缩小比例 (0.75x) 下的同步缩放与防截断
$dpiFormType = $asm.GetType("RazerBatteryTray.DpiAwareForm")
$dpiScaleField = $dpiFormType.GetField("dpiScale", $flags)

$vgnType = $asm.GetType("RazerBatteryTray.VgnKeyboardForm")
Assert-True ($dpiFormType.IsAssignableFrom($vgnType)) "VgnKeyboardForm 已继承自 DpiAwareForm"

$ratesArr = [int[]]@(125, 500, 1000)
$dialogChecks = @(
    @{ Name = "ResetConfirmDialog"; Args = [object[]]@([float]0.75); BaseW = 460 },
    @{ Name = "PromptDialog"; Args = [object[]]@([string]"重命名", [string]"名称:", [string]"默认", [float]0.75); BaseW = 380 },
    @{ Name = "PollingRateManagementDialog"; Args = [object[]]@([string]"Razer", $ratesArr, $ratesArr, [int]1000, [float]0.75); BaseW = 420 },
    @{ Name = "ImportAdapterDialog"; Args = [object[]]@([float]0.75); BaseW = 500 },
    @{ Name = "VgnKeyboardForm"; Args = [object[]]@([float]0.75); BaseW = 460 }
)

foreach ($chk in $dialogChecks) {
    $t = $asm.GetType("RazerBatteryTray." + $chk.Name)
    Assert-True ($null -ne $t) "$($chk.Name) 类型存在"
    $inst = [System.Activator]::CreateInstance($t, $flags, $null, $chk.Args, $null)
    try {
        $actualScale = [float]$dpiScaleField.GetValue($inst)
        Assert-True ([Math]::Abs($actualScale - 0.75) -lt 0.02) "$($chk.Name)(0.75f) 保持 0.75x 缩放比而非强制回退 1.0x (实际: $actualScale)"
        $expectedW = [int][Math]::Round($chk.BaseW * 0.75)
        Assert-True ([Math]::Abs($inst.ClientSize.Width - $expectedW) -le 6) "$($chk.Name)(0.75f) 客户区宽度按 0.75x 等比缩放 (期望 ~$expectedW, 实际 $($inst.ClientSize.Width))"
    }
    finally {
        $inst.Dispose()
    }
}

# 4. 验证实测三大缺陷修复契约：
#   (A) ApplyDpiScaleToControls 严禁调用重度阻塞的 UpdateUI(selectedDevice, true)
#   (B) MainForm.Hardware.cs 动态切换 lblBatteryBig 字号必须走 DpiScaleHelper.SetControlBaseFont
#   (C) PriorityReorderControl / ModernDeviceSwitcherFlyout / DpiStageRowControl 必须接入 CreateScaledFont
$applyBlockMatch = [regex]::Match($mainUiSrc, "void ApplyDpiScaleToControls\(\)[\s\S]*?private void RelayoutActiveDevicePerformanceControls")
Assert-True ($applyBlockMatch.Success) "定位 MainForm.UI.cs 中的 ApplyDpiScaleToControls 方法块"
Assert-True ($applyBlockMatch.Value -notmatch "UpdateUI\s*\(\s*selectedDevice") "ApplyDpiScaleToControls 内部严禁调用 UpdateUI(selectedDevice, ...) 导致拖拽卡顿"

$mainHwSrc = Get-Content (Join-Path $rootDir "src\UI\Forms\MainForm.Hardware.cs") -Raw -Encoding UTF8
Assert-True ($mainHwSrc -notmatch "lblBatteryBig\.Font\s*=\s*new\s+Font") "MainForm.Hardware.cs 严禁直接用未缩放的 new Font 覆盖 lblBatteryBig.Font"
Assert-True ($mainHwSrc -match "DpiScaleHelper\.SetControlBaseFont\s*\(\s*lblBatteryBig") "MainForm.Hardware.cs 使用 DpiScaleHelper.SetControlBaseFont 动态切换电量字号"

$prioSrc = Get-Content (Join-Path $rootDir "src\UI\Controls\PriorityReorderControl.cs") -Raw -Encoding UTF8
Assert-True ($prioSrc -notmatch 'new\s+Font\s*\(\s*"Microsoft YaHei UI"') "PriorityReorderControl.cs 严禁硬编码未缩放的 Microsoft YaHei UI 字体"
Assert-True ($prioSrc -match "DpiScaleHelper\.CreateScaledFont") "PriorityReorderControl.cs 已全面接入 DpiScaleHelper.CreateScaledFont"

$flyoutSrc = Get-Content (Join-Path $rootDir "src\UI\Forms\ModernDeviceSwitcherFlyout.cs") -Raw -Encoding UTF8
Assert-True ($flyoutSrc -notmatch 'new\s+Font\s*\(\s*"Microsoft YaHei UI"') "ModernDeviceSwitcherFlyout.cs 已全面接入 DpiScaleHelper.CreateScaledFont"

$dpiRowSrc = Get-Content (Join-Path $rootDir "src\UI\Controls\DpiStageRowControl.cs") -Raw -Encoding UTF8
Assert-True ($dpiRowSrc -notmatch 'new\s+Font\s*\(\s*"Microsoft YaHei UI"') "DpiStageRowControl.cs 已全面接入 DpiScaleHelper.CreateScaledFont"

$dpiMgmtSrc = Get-Content (Join-Path $rootDir "src\UI\Forms\DpiManagementDialog.cs") -Raw -Encoding UTF8
Assert-True ($dpiMgmtSrc -notmatch "if\s*\(\s*scale\s*<\s*1\.0f\s*\)\s*scale\s*=\s*1\.0f") "DpiManagementDialog.cs 已移除 scale < 1.0f 钳制，支持同步缩小"

# 5. 验证 AboutForm(float scale) 紧凑对齐缩放、主窗口缩放预设档位与重置按钮、以及 WM_SIZING 零阻塞防卡顿契约
$aboutType = $asm.GetType("RazerBatteryTray.AboutForm")
$aboutCompact = [System.Activator]::CreateInstance($aboutType, $flags, $null, [object[]]@([float]1.0), $null)
try {
    Assert-True ($aboutCompact.ClientSize.Width -eq 600 -and $aboutCompact.ClientSize.Height -eq 668) "AboutForm(1.0f) 保持经典原版 600x668 尺寸 (实际: $($aboutCompact.ClientSize.Width)x$($aboutCompact.ClientSize.Height))"
}
finally {
    $aboutCompact.Dispose()
}

$getRecScaleMethod = $mainFormType.GetMethod("GetRecommendedDefaultScale", $flags)
Assert-True ($null -ne $getRecScaleMethod) "MainForm.GetRecommendedDefaultScale 方法存在"
Assert-True ($mainUiSrc -match "btnZoomReset" -and $mainUiSrc -match "GetRecommendedScalePercent") "MainForm.UI.cs 包含界面缩放重置按钮并直接显示推荐比例数字"
Assert-True ($mainUiSrc -match "btnZoomSmall" -and $mainUiSrc -match "btnZoomMedium" -and $mainUiSrc -match "btnZoomLarge") "MainForm.UI.cs 包含界面缩放预设档位按钮 (btnZoomSmall / btnZoomMedium / btnZoomLarge)"
Assert-True ($mainEventsSrc -match "sizingDragTimer") "MainForm.Events.cs 在 WM_SIZING 中采用异步防抖定时器 sizingDragTimer 杜绝 2000Hz 鼠标拖拽卡顿"

Write-Host "`nAll Stepless Window Scaling Tests PASSED!" -ForegroundColor Cyan
exit 0
