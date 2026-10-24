# ============================================================================
# 测试脚本: test_fdas_device_merge_and_game_opt.ps1
# 目标: 验证 FDAS 设备与自定义配置自动去重合并、全屏游戏优化及 DPI/回报率联动
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = (Resolve-Path "$ScriptDir\..").Path
Set-Location $ProjectDir

$exePath = "$ProjectDir\FerrisPulse.exe"
$bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $exePath).Path)
$assembly = [System.Reflection.Assembly]::Load($bytes)

Write-Host "--- 1. 验证 GetDeviceIdVariants 包含 FDAS 前缀互通 ---" -ForegroundColor Cyan
$tDM = $assembly.GetType("RazerBatteryTray.DeviceManager")
$mVariants = $tDM.GetMethod("GetDeviceIdVariants", [System.Reflection.BindingFlags]'Public,Static')
$variants = [System.Collections.Generic.List[string]]$mVariants.Invoke($null, @("FDAS:3554:FB3E"))

if (-not $variants.Contains("HID:3554:FB3E")) {
    Write-Host "FAIL: GetDeviceIdVariants 未包含 HID:3554:FB3E 互通变体！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: FDAS 与 HID 变体互通成立: $($variants -join ', ')" -ForegroundColor Green

Write-Host "`n--- 2. 验证 FdasRuntimeHelper.IsFdasDevice & IsFdasMouse ---" -ForegroundColor Cyan
$tFdas = $assembly.GetType("RazerBatteryTray.FdasRuntimeHelper")
$mIsDev = $tFdas.GetMethod("IsFdasDevice", [System.Reflection.BindingFlags]'Public,Static')
$mIsMouse = $tFdas.GetMethod("IsFdasMouse", [System.Reflection.BindingFlags]'Public,Static')

if ($null -eq $mIsDev -or $null -eq $mIsMouse) {
    Write-Host "FAIL: 未找到 IsFdasDevice 或 IsFdasMouse 方法！" -ForegroundColor Red
    exit 1
}

$isDevRes = $mIsDev.Invoke($null, @("FDAS:3554:FB3E"))
if (-not $isDevRes) {
    Write-Host "FAIL: IsFdasDevice('FDAS:3554:FB3E') 应返回 true！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: IsFdasDevice 判定正常" -ForegroundColor Green

$tDev = $assembly.GetType("RazerBatteryTray.MouseBatteryInfo")
$tCat = $assembly.GetType("RazerBatteryTray.DeviceCategory")

$testDev = [System.Activator]::CreateInstance($tDev)
$testDev.DeviceId = "FDAS:3554:FB3E"
$testDev.Brand = "VGN"
$testDev.Category = [System.Enum]::Parse($tCat, "Mouse")
$testDev.DpiStages = @(400, 800, 1600, 3200, 6400)
$testDev.PollingRate = 1000
$testDev.SupportedPollingRates = @(125, 250, 500, 1000, 2000, 4000, 8000)

$isMouseRes = $mIsMouse.Invoke($null, @($testDev))
if (-not $isMouseRes) {
    Write-Host "FAIL: IsFdasMouse 应返回 true！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: IsFdasMouse 判定正常" -ForegroundColor Green

Write-Host "`n--- 3. 验证两阶段防幻觉提示词已生效 ---" -ForegroundColor Cyan
$tWiz = $assembly.GetType("RazerBatteryTray.AddDeviceWizardForm")
$mPrompt = $tWiz.GetMethod("GetAiAdaptationPrompt", [System.Reflection.BindingFlags]'Public,Static', $null, @(), $null)
$promptText = [string]$mPrompt.Invoke($null, @())

if (-not $promptText.Contains("两阶段严谨逆向法")) {
    Write-Host "FAIL: 提示词未包含两阶段严谨逆向法！" -ForegroundColor Red
    exit 1
}
if (-not $promptText.Contains("probe_hid_device.ps1")) {
    Write-Host "FAIL: 提示词未包含 probe_hid_device.ps1 现场探针命令！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: 两阶段防幻觉与现场探针提示词已完美装配" -ForegroundColor Green

Write-Host "`n--- 4. 验证 FDAS 鼠标在 MainForm 下全屏游戏优化拉伸/收回动效与可见性联动 ---" -ForegroundColor Cyan
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$tMainForm = $assembly.GetType("RazerBatteryTray.MainForm")
$form = [System.Activator]::CreateInstance($tMainForm, @($false))
$null = $form.Handle # 确保句柄与内部控件初始化
$form.Visible = $true

$fSelected = $tMainForm.GetField("selectedDevice", [System.Reflection.BindingFlags]'Instance,NonPublic')
$fSelected.SetValue($form, $testDev)

$mIsSupp = $tMainForm.GetMethod("IsSelectedDeviceSupportingAutoGameRate", [System.Reflection.BindingFlags]'Public,Instance')
if ($null -eq $mIsSupp) {
    Write-Host "FAIL: 未找到 IsSelectedDeviceSupportingAutoGameRate 公共方法！" -ForegroundColor Red
    exit 1
}

$isSuppRes = $mIsSupp.Invoke($form, @())
if (-not $isSuppRes) {
    Write-Host "FAIL: FDAS 鼠标调用 IsSelectedDeviceSupportingAutoGameRate 必须返回 true！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: FDAS 鼠标成功被识别为支持全屏游戏优化 (IsSelectedDeviceSupportingAutoGameRate = true)" -ForegroundColor Green

function Get-FieldVal($fName) {
    $f = $tMainForm.GetField($fName, [System.Reflection.BindingFlags]'Instance,NonPublic')
    if ($f -ne $null) { return $f.GetValue($form) }
    return $null
}

$mUpdate = $tMainForm.GetMethod("UpdatePerformanceSection", [System.Reflection.BindingFlags]'Instance,NonPublic')
$mSetOpt = $tMainForm.GetMethod("SetAutoGameOptEnabled", [System.Reflection.BindingFlags]'Public,Instance')

# 1. 测试开启优化 -> 卡片拉伸至展开高度 (255)，子控件可见
$mSetOpt.Invoke($form, @($true))
$mUpdate.Invoke($form, @($testDev, [System.Drawing.Color]::FromArgb(0, 255, 0)))

$cardPerf = Get-FieldVal "cardPerformance"
$btnManageGames = Get-FieldVal "btnManageGames"
$chkAutoGameDpi = Get-FieldVal "chkAutoGameDpi"
$chkAutoGameRate = Get-FieldVal "chkAutoGameRate"

$fDpiScale = $tMainForm.GetField("dpiScale", [System.Reflection.BindingFlags]'Instance,NonPublic')
$scale = if ($fDpiScale) { [float]$fDpiScale.GetValue($form) } else { 1.0 }
$expectedExpandedH = [int](255 * $scale)
$expectedRetractedH = [int](148 * $scale)

$sw = [System.Diagnostics.Stopwatch]::StartNew()
while ($cardPerf.Height -lt ($expectedExpandedH - 5) -and $sw.ElapsedMilliseconds -lt 2000) {
    [System.Threading.Thread]::Sleep(30)
    [System.Windows.Forms.Application]::DoEvents()
}

if ($null -eq $cardPerf) {
    Write-Host "FAIL: cardPerformance 未初始化！" -ForegroundColor Red
    exit 1
}

Write-Host ("   开启优化时卡片高度: " + $cardPerf.Height + " (预期约 " + $expectedExpandedH + ")") -ForegroundColor Gray
if ($cardPerf.Height -lt ($expectedExpandedH - 5)) {
    Write-Host ("FAIL: 开启优化后卡片高度未拉伸至展开高度 (实际: " + $cardPerf.Height + ", 期望: " + $expectedExpandedH + ")！") -ForegroundColor Red
    exit 1
}
if (-not $btnManageGames.Visible -or -not $chkAutoGameDpi.Visible -or -not $chkAutoGameRate.Visible) {
    Write-Host "FAIL: 开启优化后子控件必须全部可见！" -ForegroundColor Red
    exit 1
}
Write-Host ("PASS: 开启优化后卡片正常拉伸 (Height=" + $cardPerf.Height + ") 且子控件全部点亮") -ForegroundColor Green

# 2. 测试关闭优化 -> 卡片收缩至收起高度 (148 * scale)，子控件彻底隐藏
$mSetOpt.Invoke($form, @($false))
$mUpdate.Invoke($form, @($testDev, [System.Drawing.Color]::FromArgb(0, 255, 0)))
$swRetract = [System.Diagnostics.Stopwatch]::StartNew()
while ($cardPerf.Height -gt ($expectedRetractedH + 10) -and $swRetract.ElapsedMilliseconds -lt 2000) {
    [System.Threading.Thread]::Sleep(30)
    [System.Windows.Forms.Application]::DoEvents()
}

Write-Host ("   关闭优化时卡片高度: " + $cardPerf.Height + " (预期约 " + $expectedRetractedH + ")") -ForegroundColor Gray
if ($cardPerf.Height -gt ($expectedRetractedH + 10)) {
    Write-Host ("FAIL: 关闭优化后卡片高度未收回 (实际: " + $cardPerf.Height + ", 期望: " + $expectedRetractedH + ")！") -ForegroundColor Red
    exit 1
}
if ($btnManageGames.Visible -or $chkAutoGameDpi.Visible -or $chkAutoGameRate.Visible) {
    Write-Host "FAIL: 关闭优化后子控件必须全部隐藏！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: 关闭优化后卡片正常收起 (Height=$($cardPerf.Height)) 且子控件彻底隐藏" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " ALL PASS: FDAS 全局去重、动态第一公民与两阶段提示词验证通过！" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
