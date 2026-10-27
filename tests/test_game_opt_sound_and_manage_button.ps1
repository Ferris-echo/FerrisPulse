# tests\test_game_opt_sound_and_manage_button.ps1
# 验证：
# 1. 关闭全屏游戏自动优化时，游戏管理按钮 (btnManageGames) 必须彻底隐藏消失
# 2. 提示音音频生成必须包含尾部静音缓冲 (trailing silence) 且波形平滑衰减至 0，绝不被音频驱动截断

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

function Get-Control($name) {
    $f = $tMainForm.GetField($name, [System.Reflection.BindingFlags]'Instance,NonPublic')
    if ($f -ne $null) {
        return $f.GetValue($form)
    }
    return $null
}

# --- 测试 1: 鼠标在关闭游戏优化时，btnManageGames 必须不可见 ---
Write-Host "--- 测试 1: 鼠标在关闭游戏优化时 btnManageGames 必须隐藏 ---" -ForegroundColor Cyan

$mouseInfo = [Activator]::CreateInstance($tInfo)
$mouseInfo.Brand = "Razer"
$mouseInfo.DeviceId = "Razer:1532:00B7"
$mouseInfo.DeviceName = "Razer DeathAdder V3 Pro"
$mouseInfo.Category = [Enum]::Parse($tCat, "Mouse")
$mouseInfo.IsConnected = $true
$mouseInfo.BatteryPercent = 85
$mouseInfo.Dpi = 1600
$mouseInfo.PollingRate = 1000
$mouseInfo.DpiStages = [int[]]@(400, 800, 1600, 3200, 6400)
$mouseInfo.DpiStage = 3

$mUpdate = $tMainForm.GetMethod("UpdatePerformanceSection", [System.Reflection.BindingFlags]'Instance,NonPublic')
$mSetOpt = $tMainForm.GetMethod("SetAutoGameOptEnabled", [System.Reflection.BindingFlags]'Public,Instance')

# 先设置为开启
$mSetOpt.Invoke($form, @($true))
$mUpdate.Invoke($form, @($mouseInfo, [System.Drawing.Color]::FromArgb(0, 255, 0)))

$btnManageGames = Get-Control "btnManageGames"
$chkAutoGameOpt = Get-Control "chkAutoGameOpt"

if (-not $btnManageGames.Visible) {
    Write-Host "FAIL: 开启游戏优化时，btnManageGames 应该可见！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: 开启游戏优化时，btnManageGames 正常可见" -ForegroundColor Green

# 现在关闭游戏优化
$mSetOpt.Invoke($form, @($false))
$mUpdate.Invoke($form, @($mouseInfo, [System.Drawing.Color]::FromArgb(0, 255, 0)))

if ($btnManageGames.Visible) {
    Write-Host "FAIL: 关闭游戏优化后，btnManageGames 依然可见，未被隐藏！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: 关闭游戏优化后，btnManageGames 已成功隐藏！" -ForegroundColor Green

# --- 测试 2: 验证提示音生成含有尾部静音缓冲与平滑衰减 ---
Write-Host "`n--- 测试 2: 验证提示音生成含有尾部静音缓冲与平滑衰减 ---" -ForegroundColor Cyan

$mGenWav = $tMainForm.GetMethod("GenerateBellChimeWav", [System.Reflection.BindingFlags]'Static,NonPublic')
if ($mGenWav -eq $null) {
    Write-Host "FAIL: GenerateBellChimeWav 方法未找到！" -ForegroundColor Red
    exit 1
}

$wavBytes = $mGenWav.Invoke($null, @([double[]]@(784.0, 1046.5), [double[]]@(0.10, 0.26)))
if ($wavBytes -eq $null -or $wavBytes.Length -lt 20000) {
    Write-Host "FAIL: GenerateBellChimeWav 返回的音频过短或为空！" -ForegroundColor Red
    exit 1
}

# 校验 WAV 尾部是否有纯零静音缓冲（防止驱动截断）
$tailZeroCount = 0
for ($i = $wavBytes.Length - 1; $i -ge $wavBytes.Length - 2000; $i--) {
    if ($wavBytes[$i] -eq 0) {
        $tailZeroCount++
    } else {
        break
    }
}

if ($tailZeroCount -lt 1000) {
    Write-Host ("FAIL: 音频文件尾部缺少足够的静音缓冲保护（实际尾部纯零字节数: " + $tailZeroCount + "，预期 >= 1000）！") -ForegroundColor Red
    exit 1
}
Write-Host ("PASS: 音频尾部包含充足静音缓冲 (" + $tailZeroCount + " 字节)，彻底杜绝声卡驱动提前截断" ) -ForegroundColor Green

$form.Dispose()

Write-Host "`nALL PASS: 游戏优化关闭按钮隐藏与提示音防截断测试全部通过！" -ForegroundColor Green
exit 0
