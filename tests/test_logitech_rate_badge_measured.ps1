# tests/test_logitech_rate_badge_measured.ps1
# 回归验证：右上角「● 硬件生效: X Hz」必须取设备实测回报率，不得取配置里的期望值
#   1. 设备实测 1000Hz + 配置残留 500Hz 时，徽标必须显示 1000 Hz
#   2. 设备实测值缺失（0）时才回退到配置值
#   3. 桌面回报率按钮高亮同样跟随实测值（不得停在 500Hz）
# 说明：全内存反射装载，严禁 Add-Type -Path 锁死可执行文件句柄。

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$asmPath = Join-Path $PSScriptRoot "..\FerrisPulse.exe"
if (-not (Test-Path $asmPath)) { $asmPath = "FerrisPulse.exe" }
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes((Resolve-Path $asmPath).Path))

$tMain = $asm.GetType("RazerBatteryTray.MainForm")
$tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo")
$tCat = $asm.GetType("RazerBatteryTray.DeviceCategory")
if ($null -eq $tMain -or $null -eq $tInfo) {
    Write-Host "FAIL: MainForm / MouseBatteryInfo 缺失" -ForegroundColor Red
    exit 1
}

function New-LogiInfo([int]$pollingRate) {
    $info = [Activator]::CreateInstance($tInfo)
    $info.Brand = "Logitech"
    $info.DeviceId = "Logitech:TEST:RATE01"
    $info.DeviceName = "G304 Lightspeed Wireless Gaming Mouse"
    $info.Category = [Enum]::Parse($tCat, "Mouse")
    $info.Transport = "Lightspeed 无线"
    $info.IsConnected = $true
    $info.IsSleeping = $false
    $info.BatteryPercent = 90
    $info.Dpi = 3200
    $info.DpiY = 3200
    $info.DpiStage = 4
    $info.DpiStageCount = 5
    $info.DpiStages = [int[]]@(400, 800, 1600, 3200, 6400)
    $info.DpiStagesY = [int[]]@(400, 800, 1600, 3200, 6400)
    $info.PollingRate = $pollingRate
    $info.SupportedPollingRates = [int[]]@(125, 250, 500, 1000)
    return $info
}

$form = [Activator]::CreateInstance($tMain, @($false))
$null = $form.Handle

# 模拟“历史失败写入留在配置里的期望值”
$fldDesktop = $tMain.GetField("desktopPollingRate", [System.Reflection.BindingFlags]"Instance,NonPublic")
$fldDesktop.SetValue($form, 500)
# 打开电竞回报率开关（徽标显示条件之一）
$fldAutoRate = $tMain.GetField("autoGameRateEnabled", [System.Reflection.BindingFlags]"Instance,NonPublic")
$fldAutoRate.SetValue($form, $true)

$mUpdate = $null
foreach ($m in $tMain.GetMethods([System.Reflection.BindingFlags]"Instance,NonPublic")) {
    if ($m.Name -eq "UpdatePerformanceSection" -and $m.GetParameters().Count -eq 2) { $mUpdate = $m; break }
}
if ($null -eq $mUpdate) {
    Write-Host "FAIL: UpdatePerformanceSection(info, accent) 不存在" -ForegroundColor Red
    $form.Dispose(); exit 1
}

$fldBadge = $tMain.GetField("lblLiveRateBadge", [System.Reflection.BindingFlags]"Instance,NonPublic")
$lbl = $fldBadge.GetValue($form)

# ---------------------------------------------------------------
# 测试 1：实测 1000Hz，配置残留 500Hz → 徽标必须是 1000 Hz
# ---------------------------------------------------------------
$info = New-LogiInfo 1000
$null = $mUpdate.Invoke($form, @($info, $info.BrandColor))
Write-Host ("徽标文本: " + $lbl.Text) -ForegroundColor Gray
if ($lbl.Text -notmatch "1000 Hz") {
    Write-Host ("FAIL: 徽标未采用实测回报率 1000Hz，实际: " + $lbl.Text) -ForegroundColor Red
    $form.Dispose(); exit 1
}
if ($lbl.Text -match "500 Hz") {
    Write-Host ("FAIL: 徽标仍显示配置残留的 500Hz，实际: " + $lbl.Text) -ForegroundColor Red
    $form.Dispose(); exit 1
}
Write-Host "PASS: 徽标跟随实测回报率（1000 Hz，未采用配置残留 500 Hz）" -ForegroundColor Green

# 桌面回报率按钮高亮也必须落在 1000Hz
$fldRates = $tMain.GetField("btnRates", [System.Reflection.BindingFlags]"Instance,NonPublic")
$fldVals = $tMain.GetField("pollingRateValues", [System.Reflection.BindingFlags]"Instance,NonPublic")
$rateBtns = $fldRates.GetValue($form)
$rateVals = $fldVals.GetValue($form)
$selHz = 0
for ($i = 0; $i -lt $rateBtns.Length; $i++) {
    if ($null -ne $rateBtns[$i] -and $rateBtns[$i].Selected -and $i -lt $rateVals.Length) { $selHz = $rateVals[$i] }
}
if ($selHz -ne 1000) {
    Write-Host ("FAIL: 桌面回报率按钮高亮应为 1000Hz，实际 {0}Hz" -f $selHz) -ForegroundColor Red
    $form.Dispose(); exit 1
}
Write-Host "PASS: 桌面回报率按钮高亮同步为 1000 Hz" -ForegroundColor Green

# ---------------------------------------------------------------
# 测试 2：实测值缺失时回退配置值（不得显示 0 Hz）
# ---------------------------------------------------------------
$info2 = New-LogiInfo 0
$null = $mUpdate.Invoke($form, @($info2, $info2.BrandColor))
Write-Host ("徽标文本(回退): " + $lbl.Text) -ForegroundColor Gray
if ($lbl.Text -notmatch "500 Hz") {
    Write-Host ("FAIL: 实测缺失时应回退配置值 500Hz，实际: " + $lbl.Text) -ForegroundColor Red
    $form.Dispose(); exit 1
}
if ([regex]::IsMatch($lbl.Text, '(^|[^\d])0 Hz')) {
    Write-Host "FAIL: 徽标出现 0 Hz 非法值" -ForegroundColor Red
    $form.Dispose(); exit 1
}
Write-Host "PASS: 实测缺失时安全回退配置值，无 0 Hz" -ForegroundColor Green

$form.Dispose()
Write-Host "ALL PASS: 回报率徽标已改为「实测优先、配置兜底」" -ForegroundColor Green
exit 0
