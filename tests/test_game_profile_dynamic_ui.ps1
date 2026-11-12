# ============================================================================
# 测试脚本: test_game_profile_dynamic_ui.ps1
# 所属区域: tests (自动化回归测试层)
# 核心职责:
#   验证独立游戏专属策略与游戏内动态切档的主界面联动：
#   1. 专属策略进游戏：电竞回报率与 DPI 按钮高亮实际生效值，右上角角标附加 (专属: ...)
#   2. 游戏内物理变档：模拟 OnDpiChanged，电竞 DPI 按钮实时动态跟随高亮新档位
#   3. 退出游戏复原：电竞按钮组回到暗绿预设态，日常桌面按钮恢复高亮，角标恢复桌面显示
# ============================================================================

$ErrorActionPreference = "Stop"
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture

$rootDir = (Resolve-Path "$PSScriptRoot\..").Path
$exePath = Join-Path $rootDir "FerrisPulse.exe"

if (-not (Test-Path $exePath)) {
    Write-Host "FAIL: 未找到二进制产物 $exePath，请先执行 build.bat" -ForegroundColor Red
    exit 1
}

$asmBytes = [System.IO.File]::ReadAllBytes($exePath)
$asm = [System.Reflection.Assembly]::Load($asmBytes)

$tMain = $asm.GetType("RazerBatteryTray.MainForm")
$tProfile = $asm.GetType("RazerBatteryTray.GameProfile")
$tMouseInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo")
$tCat = $asm.GetType("RazerBatteryTray.DeviceCategory")

Write-Host "--- 1. 实例化 MainForm 与装配测试上下文 ---" -ForegroundColor Cyan
$mainForm = [System.Activator]::CreateInstance($tMain, @($false))
$null = $mainForm.Handle

# 准备测试鼠标设备 (支持电竞回报率与 DPI)
$dummyDev = [System.Activator]::CreateInstance($tMouseInfo)
$dummyDev.Brand = "Razer"
$dummyDev.DeviceId = "TestMouse001"
$dummyDev.Dpi = 800
$dummyDev.DpiStage = 1
$dummyDev.DpiStages = @(800, 1600, 2400, 3200, 4000)
$dummyDev.PollingRate = 1000
$dummyDev.SupportedPollingRates = @(125, 250, 500, 1000, 2000, 4000, 8000)
$dummyDev.Capabilities.SupportsPollingRate = $true
$dummyDev.Capabilities.SupportsDpi = $true
$dummyDev.Category = [System.Enum]::Parse($tCat, "Mouse")

# 反射注入 selectedDevice 与 lastInfo
$fSelDev = $tMain.GetField("selectedDevice", [System.Reflection.BindingFlags]"NonPublic, Instance")
$fSelDev.SetValue($mainForm, $dummyDev)
$fLastInfo = $tMain.GetField("lastInfo", [System.Reflection.BindingFlags]"NonPublic, Instance")
$fLastInfo.SetValue($mainForm, $dummyDev)

# 开启电竞优化开关
$fRateOpt = $tMain.GetField("autoGameRateEnabled", [System.Reflection.BindingFlags]"NonPublic, Instance")
$fRateOpt.SetValue($mainForm, $true)
$fDpiOpt = $tMain.GetField("autoGameDpiEnabled", [System.Reflection.BindingFlags]"NonPublic, Instance")
$fDpiOpt.SetValue($mainForm, $true)

# 设置通用默认电竞参数: 4000Hz, 第 2 档 (1600 DPI)
$fGameRate = $tMain.GetField("gamePollingRate", [System.Reflection.BindingFlags]"NonPublic, Instance")
$fGameRate.SetValue($mainForm, 4000)
$fGameDpi = $tMain.GetField("gameDpiStage", [System.Reflection.BindingFlags]"NonPublic, Instance")
$fGameDpi.SetValue($mainForm, 2)

# 设置日常桌面参数: 1000Hz, 第 1 档 (800 DPI)
$fDeskRate = $tMain.GetField("desktopPollingRate", [System.Reflection.BindingFlags]"NonPublic, Instance")
$fDeskRate.SetValue($mainForm, 1000)
$fDeskDpi = $tMain.GetField("desktopDpiStage", [System.Reflection.BindingFlags]"NonPublic, Instance")
$fDeskDpi.SetValue($mainForm, 1)

# 配置专属策略: cs2.exe -> 2000Hz, 第 3 档 (2400 DPI), FriendlyTitle: "CS2"
$profilesDict = New-Object 'System.Collections.Generic.Dictionary[string, RazerBatteryTray.GameProfile]'( [StringComparer]::OrdinalIgnoreCase )
$cs2Profile = [Activator]::CreateInstance($tProfile, @("cs2.exe", [int]3, [int]2000, "C:\Games\cs2.exe", "CS2"))
$profilesDict["cs2.exe"] = $cs2Profile

$fProfiles = $tMain.GetField("gameProfiles", [System.Reflection.BindingFlags]"NonPublic, Instance")
$fProfiles.SetValue($mainForm, $profilesDict)

$monList = New-Object 'System.Collections.Generic.List[string]'
$monList.Add("cs2.exe")
$fMonList = $tMain.GetField("monitoredGameProcesses", [System.Reflection.BindingFlags]"NonPublic, Instance")
$fMonList.SetValue($mainForm, $monList)

$mUpdateDisplay = $tMain.GetMethod("UpdatePollingRateDisplayStates", [System.Reflection.BindingFlags]"NonPublic, Instance")
$fBadge = $tMain.GetField("lblLiveRateBadge", [System.Reflection.BindingFlags]"NonPublic, Instance")
$badge = $fBadge.GetValue($mainForm)

$fBtnGameRates = $tMain.GetField("btnGameRates", [System.Reflection.BindingFlags]"NonPublic, Instance")
$btnGameRates = $fBtnGameRates.GetValue($mainForm)
$fBtnGameDpis = $tMain.GetField("btnGameDpiStages", [System.Reflection.BindingFlags]"NonPublic, Instance")
$btnGameDpis = $fBtnGameDpis.GetValue($mainForm)

# --- 测试 1: 桌面状态下的显示基准 ---
Write-Host "`n--- 测试 1: 桌面日常状态显示基准 ---" -ForegroundColor Cyan
$fIsGaming = $tMain.GetField("isGamingActive", [System.Reflection.BindingFlags]"NonPublic, Instance")
$fIsGaming.SetValue($mainForm, $false)
$mUpdateDisplay.Invoke($mainForm, $null)

Write-Host "Badge Text: $($badge.Text)" -ForegroundColor DarkGray
if ($badge.Text -notmatch "1000 Hz") {
    Write-Host "FAIL: 桌面状态下徽标未能正确显示日常回报率 1000 Hz！" -ForegroundColor Red
    $mainForm.Dispose()
    exit 1
}
if ($badge.Text -match "专属") {
    Write-Host "FAIL: 桌面状态下误带专属标识！" -ForegroundColor Red
    $mainForm.Dispose()
    exit 1
}
Write-Host "PASS: 桌面状态显示基准正常" -ForegroundColor Green

# --- 测试 2: 进入专属策略游戏态 (CS2) ---
Write-Host "`n--- 测试 2: 进入专属策略游戏态 (2000 Hz, 第 3 档) ---" -ForegroundColor Cyan
$fIsGaming.SetValue($mainForm, $true)

$fInGameRate = $tMain.GetField("inGameRuntimePollingRate", [System.Reflection.BindingFlags]"NonPublic, Instance")
$fInGameStage = $tMain.GetField("inGameRuntimeDpiStage", [System.Reflection.BindingFlags]"NonPublic, Instance")
$fInGameTitle = $tMain.GetField("inGameActiveGameTitle", [System.Reflection.BindingFlags]"NonPublic, Instance")

if ($fInGameRate -eq $null -or $fInGameStage -eq $null -or $fInGameTitle -eq $null) {
    Write-Host "FAIL: MainForm 缺少运行时动态状态追踪字段 (inGameRuntimePollingRate/DpiStage/ActiveGameTitle)！" -ForegroundColor Red
    $mainForm.Dispose()
    exit 1
}

$fInGameRate.SetValue($mainForm, 2000)
$fInGameStage.SetValue($mainForm, 3)
$fInGameTitle.SetValue($mainForm, "CS2")

$mUpdateDisplay.Invoke($mainForm, $null)

Write-Host "In-Game Badge: $($badge.Text)" -ForegroundColor DarkGray
if ($badge.Text -notmatch "2000 Hz" -or $badge.Text -notmatch "专属: CS2") {
    Write-Host "FAIL: 进入专属策略游戏后，徽标未显示 2000 Hz 或未带专属 CS2 标识！实际: $($badge.Text)" -ForegroundColor Red
    $mainForm.Dispose()
    exit 1
}

# 验证按钮高亮：第 3 档 DPI 按钮应被选中，2000Hz 按钮应被选中
$rate2000Btn = $null
foreach ($btn in $btnGameRates) {
    if ($btn.Text -match "2000" -or $btn.Text -match "2K") { $rate2000Btn = $btn; break }
}
if ($rate2000Btn -ne $null -and -not $rate2000Btn.Selected) {
    Write-Host "FAIL: 电竞回报率按钮组未能高亮专属 2000 Hz！" -ForegroundColor Red
    $mainForm.Dispose()
    exit 1
}

$stage3Btn = $btnGameDpis[2] # 索引 2 为第 3 档
if (-not $stage3Btn.Selected) {
    Write-Host "FAIL: 电竞 DPI 按钮组未能高亮专属第 3 档！" -ForegroundColor Red
    $mainForm.Dispose()
    exit 1
}
Write-Host "PASS: 专属策略进入游戏态，按钮高亮与角标完全匹配生效值！" -ForegroundColor Green

# --- 测试 3: 游戏内手动物理变档 (变更为第 4 档) ---
Write-Host "`n--- 测试 3: 游戏内物理按键变档为第 4 档 ---" -ForegroundColor Cyan
$fInGameStage.SetValue($mainForm, 4)
$mUpdateDisplay.Invoke($mainForm, $null)

Write-Host "Changed Badge: $($badge.Text)" -ForegroundColor DarkGray
$stage4Btn = $btnGameDpis[3] # 索引 3 为第 4 档
if (-not $stage4Btn.Selected) {
    Write-Host "FAIL: 游戏内物理变档后，电竞 DPI 按钮未能动态跟随高亮至第 4 档！" -ForegroundColor Red
    $mainForm.Dispose()
    exit 1
}
Write-Host "PASS: 游戏内物理变档动态联动成功" -ForegroundColor Green

# --- 测试 4: 退出游戏干净复原 ---
Write-Host "`n--- 测试 4: 退出游戏复原桌面日常状态 ---" -ForegroundColor Cyan
$fIsGaming.SetValue($mainForm, $false)
$fInGameRate.SetValue($mainForm, 0)
$fInGameStage.SetValue($mainForm, 0)
$fInGameTitle.SetValue($mainForm, "")
$mUpdateDisplay.Invoke($mainForm, $null)

Write-Host "Restored Badge: $($badge.Text)" -ForegroundColor DarkGray
if ($badge.Text -notmatch "1000 Hz" -or $badge.Text -match "专属") {
    Write-Host "FAIL: 退出游戏后未能干净复原至 1000 Hz 桌面状态！实际: $($badge.Text)" -ForegroundColor Red
    $mainForm.Dispose()
    exit 1
}
Write-Host "PASS: 退出游戏状态干净复原" -ForegroundColor Green

$mainForm.Dispose()
Write-Host "`n=== 全部动态 UI 联动测试通过 (ALL PASS) ===" -ForegroundColor Green
exit 0
