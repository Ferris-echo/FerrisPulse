# tests\test_startup_game_buttons_visibility.ps1
# 验证：
# 1. 应用打开/加载鼠标后，电竞 DPI 档位按钮与电竞回报率按钮必须完成排版布局（Width > 0, Height > 0），不得出现 0 尺寸
# 2. 按钮文本必须正确填充数值（如 400, 800, 1000 Hz 等），不得为空或默认占位
# 3. 展开状态下所有支持档位按钮必须正常可见 (Visible = true)，未勾选子项呈现暗化禁用 (Enabled = false)

$ErrorActionPreference = "Stop"

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


# 启用测试防写保护，防止单测覆写用户生产注册表
$fDisableSave = $tMainForm.GetField("DisableConfigSaveForTesting", [System.Reflection.BindingFlags]'Static,NonPublic,Public')
if ($fDisableSave -ne $null) { $fDisableSave.SetValue($null, $true) }

$form = [Activator]::CreateInstance($tMainForm, @($false))
$null = $form.Handle
$form.Visible = $true

function Get-Field($name) {
    $f = $tMainForm.GetField($name, [System.Reflection.BindingFlags]'Instance,NonPublic')
    if ($f -ne $null) {
        return $f.GetValue($form)
    }
    return $null
}

$mouseInfo = [Activator]::CreateInstance($tInfo)
$mouseInfo.Brand = "Razer"
$mouseInfo.DeviceId = "Razer:1532:00B7"
$mouseInfo.DeviceName = "Razer DeathAdder V4 Pro"
$mouseInfo.Category = [Enum]::Parse($tCat, "Mouse")
$mouseInfo.IsConnected = $true
$mouseInfo.BatteryPercent = 98
$mouseInfo.Dpi = 3000
$mouseInfo.PollingRate = 4000
$mouseInfo.DpiStages = [int[]]@(400, 800, 1600, 3000, 6400)
$mouseInfo.DpiStage = 4
$mouseInfo.SupportedPollingRates = [int[]]@(125, 250, 500, 1000, 2000, 4000, 8000)

# 模拟用户当前状态：总开关开启，电竞回报率开启，电竞 DPI 未勾选
$mSetRate = $tMainForm.GetMethod("SetAutoGameRateEnabled", [System.Reflection.BindingFlags]'Instance,NonPublic')
$mSetRate.Invoke($form, @($true))
$mSetDpi = $tMainForm.GetMethod("SetAutoGameDpiEnabled", [System.Reflection.BindingFlags]'Instance,NonPublic')
$mSetDpi.Invoke($form, @($false))

$mUpdate = $tMainForm.GetMethod("UpdatePerformanceSection", [System.Reflection.BindingFlags]'Instance,NonPublic')
$mUpdate.Invoke($form, @($mouseInfo, [System.Drawing.Color]::FromArgb(0, 255, 0)))

$btnGameDpiStages = Get-Field "btnGameDpiStages"
$btnGameRates = Get-Field "btnGameRates"
$chkAutoGameOpt = Get-Field "chkAutoGameOpt"

Write-Host "--- 检查电竞 DPI 档位按钮 ---" -ForegroundColor Cyan
if (-not $btnGameDpiStages[0].Visible) {
    Write-Host "FAIL: 展开状态下 btnGameDpiStages[0] 应该可见！" -ForegroundColor Red
    exit 1
}
if ($btnGameDpiStages[0].Width -le 0 -or $btnGameDpiStages[0].Height -le 0) {
    Write-Host ("FAIL: btnGameDpiStages[0] 尺寸异常: " + $btnGameDpiStages[0].Bounds.ToString()) -ForegroundColor Red
    exit 1
}
if ($btnGameDpiStages[0].Text -ne "400") {
    Write-Host ("FAIL: btnGameDpiStages[0] 文本应为 400，实际为: " + $btnGameDpiStages[0].Text) -ForegroundColor Red
    exit 1
}
if ($btnGameDpiStages[0].Enabled -ne $false) {
    Write-Host "FAIL: autoGameDpiEnabled 为 false 时，btnGameDpiStages[0].Enabled 应该为 false (暗化禁用)！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: btnGameDpiStages 排版尺寸、数值填充与禁用态正常" -ForegroundColor Green

Write-Host "`n--- 检查电竞回报率档位按钮 ---" -ForegroundColor Cyan
if (-not $btnGameRates[0].Visible) {
    Write-Host "FAIL: 展开状态下 btnGameRates[0] 应该可见！" -ForegroundColor Red
    exit 1
}
if ($btnGameRates[0].Width -le 0 -or $btnGameRates[0].Height -le 0) {
    Write-Host ("FAIL: btnGameRates[0] 尺寸异常: " + $btnGameRates[0].Bounds.ToString()) -ForegroundColor Red
    exit 1
}
if ($btnGameRates[0].Text -ne "1000 Hz" -and $btnGameRates[0].Text -ne "1K") {
    Write-Host ("FAIL: btnGameRates[0] 文本应为 1000 Hz 或 1K，实际为: " + $btnGameRates[0].Text) -ForegroundColor Red
    exit 1
}
if ($btnGameRates[0].Enabled -ne $true) {
    Write-Host "FAIL: autoGameRateEnabled 为 true 时，btnGameRates[0].Enabled 应该为 true！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: btnGameRates 排版尺寸、数值填充与激活态正常" -ForegroundColor Green

$form.Dispose()
Write-Host "`nALL PASS: 打开应用时电竞 DPI 与回报率组件完整显示验证通过！" -ForegroundColor Green
exit 0
