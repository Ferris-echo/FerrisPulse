# ============================================================================
# 测试脚本: test_game_profile_ui.ps1
# 所属区域: tests (自动化回归测试层)
# 核心职责:
#   测试独立游戏专属策略在 GameProcessManagerForm 上的 UI 呈现：
#   1. ModernProcessListControl 具备 OnSettingsClicked 事件与微型设置按钮
#   2. GameProfileSettingForm 实例化与参数交互
#   3. GameProcessManagerForm 回显专属策略副标题（精准数值如 800DPI · 1000Hz 与“跟随默认预设”）
#   4. 列表标题改为“📋 已生效的游戏列表”
#   5. 后台静默自适应自动刷新计时器正常装配
# ============================================================================

$ErrorActionPreference = "Stop"
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture

$rootDir = (Resolve-Path "$PSScriptRoot\..").Path
$exePath = Join-Path $rootDir "FerrisPulse.exe"

if (-not (Test-Path $exePath)) {
    Write-Host "FAIL: 未找到二进制产物 $exePath，请先执行 build.bat" -ForegroundColor Red
    exit 1
}

# 采用内存装配机制避免文件占用句柄导致 CS0016 编译冲突
$asmBytes = [System.IO.File]::ReadAllBytes($exePath)
$asm = [System.Reflection.Assembly]::Load($asmBytes)

$tList = $asm.GetType("RazerBatteryTray.ModernProcessListControl")
$tForm = $asm.GetType("RazerBatteryTray.GameProfileSettingForm")
$tGpm = $asm.GetType("RazerBatteryTray.GameProcessManagerForm")
$tProfile = $asm.GetType("RazerBatteryTray.GameProfile")

# --- 测试 1: ModernProcessListControl 具备 OnSettingsClicked 事件 ---
Write-Host "--- 测试 1: ModernProcessListControl 具备 OnSettingsClicked 事件 ---" -ForegroundColor Cyan
$evt = $tList.GetEvent("OnSettingsClicked")
if ($evt -eq $null) {
    Write-Host "FAIL: ModernProcessListControl 未声明 OnSettingsClicked 事件！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: ModernProcessListControl 具备 OnSettingsClicked 事件" -ForegroundColor Green

# --- 测试 2: GameProfileSettingForm 实例化与数据交互 ---
Write-Host "`n--- 测试 2: GameProfileSettingForm 实例化与数据交互 ---" -ForegroundColor Cyan
$dummyProfile = [Activator]::CreateInstance($tProfile, @("cs2.exe"))
$ctorForm = $tForm.GetConstructors()[0]
$dlg = $ctorForm.Invoke(@([float]1.0, "cs2.exe", "Counter-Strike 2", $null, $dummyProfile, [int[]]@(800, 1600, 2400, 3200, 4000), [int[]]@(1000, 2000, 4000, 8000), $null))
$null = $dlg.Handle
if ($dlg.Controls.Count -lt 4) {
    Write-Host "FAIL: GameProfileSettingForm 控件树装配异常！" -ForegroundColor Red
    $dlg.Dispose()
    exit 1
}
Write-Host "PASS: GameProfileSettingForm 句柄创建成功，控件树装配完成" -ForegroundColor Green
$dlg.Dispose()

# --- 测试 3: GameProcessManagerForm 专属策略回显、默认策略与新标题 ---
Write-Host "`n--- 测试 3: GameProcessManagerForm 专属策略回显、默认策略与新标题 ---" -ForegroundColor Cyan
$dictType = [System.Collections.Generic.Dictionary[string, object]].GetGenericTypeDefinition().MakeGenericType(@([string], $tProfile))
$dict = [Activator]::CreateInstance($dictType, @([System.StringComparer]::OrdinalIgnoreCase))

$profile = [Activator]::CreateInstance($tProfile, @("cs2.exe"))
$profile.CustomDpiStage = 1
$profile.CustomPollingRate = 1000
$dict["cs2.exe"] = $profile

$gpmForm = [Activator]::CreateInstance($tGpm, @([float]1.0, [System.Collections.Generic.List[string]]@("cs2.exe", "dota2.exe"), $false, 1, $dict, $null))
$null = $gpmForm.Handle

$fCtlMon = $tGpm.GetField("ctlMonitoredList", [System.Reflection.BindingFlags]'Instance,NonPublic')
$ctlMon = $fCtlMon.GetValue($gpmForm)
$mGetItems = $tList.GetMethod("GetItems")
$monItems = $mGetItems.Invoke($ctlMon, $null)

if ($monItems.Count -lt 2) {
    Write-Host "FAIL: 守护列表未成功装载游戏！" -ForegroundColor Red
    $gpmForm.Dispose()
    exit 1
}

# 验证专属策略实际生效数值显示
$cs2Item = $monItems[0]
Write-Host "cs2.exe 副标题回显: $($cs2Item.Subtitle)" -ForegroundColor Gray

if (-not $cs2Item.Subtitle.Contains("专属策略") -or -not $cs2Item.Subtitle.Contains("800DPI") -or -not $cs2Item.Subtitle.Contains("1000Hz")) {
    Write-Host "FAIL: 守护列表中 cs2.exe 未能正确回显专属策略实际数值（应含 800DPI · 1000Hz）！" -ForegroundColor Red
    $gpmForm.Dispose()
    exit 1
}
Write-Host "PASS: 守护列表成功精准回显独立游戏专属策略实际数值（800DPI · 1000Hz）！" -ForegroundColor Green

# 验证默认策略精简副标题显示
$dotaItem = $monItems[1]
Write-Host "dota2.exe 副标题回显: $($dotaItem.Subtitle)" -ForegroundColor Gray
if ($dotaItem.Subtitle -ne "跟随默认预设") {
    Write-Host "FAIL: 默认策略副标题未显示为'跟随默认预设'，当前为: $($dotaItem.Subtitle)" -ForegroundColor Red
    $gpmForm.Dispose()
    exit 1
}
Write-Host "PASS: 默认策略副标题成功简化为'跟随默认预设'！" -ForegroundColor Green

# 验证标题文案更新
$fTitle = $tGpm.GetField("lblMonitoredTitle", [System.Reflection.BindingFlags]'Instance,NonPublic')
$lblTitle = $fTitle.GetValue($gpmForm)
Write-Host "右栏标题回显: $($lblTitle.Text)" -ForegroundColor Gray
if (-not $lblTitle.Text.Contains("已生效的游戏列表")) {
    Write-Host "FAIL: 标题未成功修改为'已生效的游戏列表'！" -ForegroundColor Red
    $gpmForm.Dispose()
    exit 1
}
Write-Host "PASS: 右栏标题成功展示为'已生效的游戏列表'！" -ForegroundColor Green

# 验证后台自愈刷新定时器已创建
$fTimer = $tGpm.GetField("autoRefreshTimer", [System.Reflection.BindingFlags]'Instance,NonPublic')
$timer = $fTimer.GetValue($gpmForm)
if ($timer -eq $null -or -not $timer.Enabled) {
    Write-Host "FAIL: autoRefreshTimer 未成功激活！" -ForegroundColor Red
    $gpmForm.Dispose()
    exit 1
}
Write-Host "PASS: 后台运行进程自动刷新定时器正常启动！" -ForegroundColor Green

$gpmForm.Dispose()

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " ALL PASS: 独立游戏专属策略 UI 交互与卡片组件验证全部通过！" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
exit 0
