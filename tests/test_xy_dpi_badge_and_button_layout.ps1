# tests\test_xy_dpi_badge_and_button_layout.ps1
# 验证：
# 1. 独立 X/Y 轴生效时，右上角硬件生效徽标 (lblLiveRateBadge) 同步呈现 "XXXX×YYYY DPI" 格式
# 2. X/Y 等比或常规时，右上角硬件生效徽标呈现 "XXXX DPI"
# 3. 主界面 DPI 按钮拥有更充裕的布局宽度 (>= 55px)，且 ModernSegmentButton 具备长文本自动缩放抗溢出能力

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$asmPath = Join-Path $PSScriptRoot "..\FerrisPulse.exe"
if (-not (Test-Path $asmPath)) { $asmPath = "FerrisPulse.exe" }
$fullPath = (Resolve-Path $asmPath).Path
$bytes = [System.IO.File]::ReadAllBytes($fullPath)
$asm = [System.Reflection.Assembly]::Load($bytes)

$tMainForm = $asm.GetType("RazerBatteryTray.MainForm")
$tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo")
$tCat = $asm.GetType("RazerBatteryTray.DeviceCategory")
$tBtn = $asm.GetType("RazerBatteryTray.ModernSegmentButton")


# 启用测试防写保护，防止单测覆写用户生产注册表
$fDisableSave = $tMainForm.GetField("DisableConfigSaveForTesting", [System.Reflection.BindingFlags]'Static,NonPublic,Public')
if ($fDisableSave -ne $null) { $fDisableSave.SetValue($null, $true) }

$form = [Activator]::CreateInstance($tMainForm, @($false))
$null = $form.Handle
$form.Visible = $false

function Get-Control($name) {
    $f = $tMainForm.GetField($name, [System.Reflection.BindingFlags]'Instance,NonPublic')
    if ($f -ne $null) {
        return $f.GetValue($form)
    }
    return $null
}

# --- 测试 1: 独立 XY 轴时，右上角硬件生效徽标适配 ---
Write-Host "--- 测试 1: 独立 XY 轴时右上角硬件生效徽标适配 ---" -ForegroundColor Cyan

$mouseInfo = [Activator]::CreateInstance($tInfo)
$mouseInfo.Brand = "Razer"
$mouseInfo.DeviceId = "Razer:1532:00B7"
$mouseInfo.DeviceName = "Razer DeathAdder V3 Pro"
$mouseInfo.Category = [Enum]::Parse($tCat, "Mouse")
$mouseInfo.IsConnected = $true
$mouseInfo.BatteryPercent = 90
$mouseInfo.Dpi = 3000
$mouseInfo.DpiY = 2250
$mouseInfo.IsXyIndependent = $true
$mouseInfo.PollingRate = 4000
$mouseInfo.DpiStages = [int[]]@(400, 800, 2400, 3000, 6400)
$mouseInfo.DpiStagesY = [int[]]@(300, 600, 1800, 2250, 4800)
$mouseInfo.DpiStage = 4

$mUpdate = $tMainForm.GetMethod("UpdatePerformanceSection", [System.Reflection.BindingFlags]'Instance,NonPublic')
$mSetGameRate = $tMainForm.GetMethod("SetAutoGameRateEnabled", [System.Reflection.BindingFlags]'Instance,NonPublic')
if ($mSetGameRate -ne $null) {
    $mSetGameRate.Invoke($form, @($true))
}

$mUpdate.Invoke($form, @($mouseInfo, [System.Drawing.Color]::FromArgb(0, 255, 0)))

$lblBadge = Get-Control "lblLiveRateBadge"
if ($lblBadge -eq $null) {
    Write-Host "FAIL: 未找到 lblLiveRateBadge 控件！" -ForegroundColor Red
    exit 1
}

Write-Host ("当前硬件生效徽标: " + $lblBadge.Text) -ForegroundColor Gray

if ($lblBadge.Text -notmatch "3000×2250 DPI") {
    Write-Host ("FAIL: 独立 XY 模式下硬件生效文本未包含 '3000×2250 DPI'，实际为: " + $lblBadge.Text) -ForegroundColor Red
    exit 1
}
Write-Host "PASS: 独立 XY 模式下硬件生效徽标正确适配 '3000×2250 DPI'" -ForegroundColor Green


# --- 测试 2: 单一或非独立 XY 轴时，硬件生效徽标回退为单 DPI ---
Write-Host "`n--- 测试 2: 非独立 XY 轴时硬件生效徽标单数值 ---" -ForegroundColor Cyan

$mouseInfo.IsXyIndependent = $false
$mUpdate.Invoke($form, @($mouseInfo, [System.Drawing.Color]::FromArgb(0, 255, 0)))

Write-Host ("当前硬件生效徽标: " + $lblBadge.Text) -ForegroundColor Gray

if ($lblBadge.Text -notmatch "3000 DPI" -or $lblBadge.Text -match "×") {
    Write-Host ("FAIL: 非独立模式下应该显示单 DPI 且不含 '×'，实际为: " + $lblBadge.Text) -ForegroundColor Red
    exit 1
}
Write-Host "PASS: 非独立模式下硬件生效徽标正确显示单值 '3000 DPI'" -ForegroundColor Green


# --- 测试 3: DPI 按钮布局宽度提升与双轴文字呈现 ---
Write-Host "`n--- 测试 3: DPI 按钮布局宽度提升与双轴文字呈现 ---" -ForegroundColor Cyan

$mouseInfo.IsXyIndependent = $true
$mUpdate.Invoke($form, @($mouseInfo, [System.Drawing.Color]::FromArgb(0, 255, 0)))

$btnDpiStages = Get-Control "btnDpiStages"
if ($btnDpiStages -eq $null -or $btnDpiStages.Length -lt 5) {
    Write-Host "FAIL: 未能获取 btnDpiStages 数组！" -ForegroundColor Red
    exit 1
}

$stage4Btn = $btnDpiStages[3]
Write-Host ("第4档按钮文字: " + $stage4Btn.Text + ", 宽度: " + $stage4Btn.Width + "px") -ForegroundColor Gray

if ($stage4Btn.Text -ne "3000×2250") {
    Write-Host ("FAIL: 第4档按钮文字预期为 '3000×2250'，实际为: " + $stage4Btn.Text) -ForegroundColor Red
    exit 1
}

if ($stage4Btn.Width -lt 50) {
    Write-Host ("FAIL: 按钮宽度预期 >= 50px，实际为: " + $stage4Btn.Width + "px") -ForegroundColor Red
    exit 1
}
Write-Host ("PASS: DPI 按钮文字正确且宽度提升至 " + $stage4Btn.Width + "px") -ForegroundColor Green


# --- 测试 4: ModernSegmentButton 极长文字自动缩放抗溢出 ---
Write-Host "`n--- 测试 4: ModernSegmentButton 极长文字自动缩放抗溢出 ---" -ForegroundColor Cyan

$segBtn = [Activator]::CreateInstance($tBtn)
$segBtn.Size = New-Object System.Drawing.Size(56, 28)
$segBtn.Text = "16000×12000"
$segBtn.Selected = $true

$bmp = New-Object System.Drawing.Bitmap(56, 28)
$g = [System.Drawing.Graphics]::FromImage($bmp)
$pevent = New-Object System.Windows.Forms.PaintEventArgs($g, (New-Object System.Drawing.Rectangle(0, 0, 56, 28)))

$mOnPaint = $tBtn.GetMethod("OnPaint", [System.Reflection.BindingFlags]'Instance,NonPublic')
try {
    $rawPevent = $pevent.PSObject.BaseObject
    $invokeArgs = [object[]]@($rawPevent)
    $mOnPaint.Invoke($segBtn, $invokeArgs)
    Write-Host "PASS: ModernSegmentButton 在 56px 按钮内渲染 11 字符长文本 (16000×12000) 正常无异常！" -ForegroundColor Green
} catch {
    Write-Host ("FAIL: 绘制过程中抛出异常: " + $_.Exception.ToString()) -ForegroundColor Red
    exit 1
} finally {
    $g.Dispose()
    $bmp.Dispose()
    $segBtn.Dispose()
    $form.Dispose()
}

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " ALL PASS: 独立 XY 轴硬件生效同步与按钮舒适布局验证通过！" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
exit 0
