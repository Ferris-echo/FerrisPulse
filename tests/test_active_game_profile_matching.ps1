# ============================================================================
# 测试脚本: test_active_game_profile_matching.ps1
# 所属区域: tests (自动化回归测试层)
# 核心职责:
#   验证 GetActiveGameProfile 的搜索优先级与多进程防截断机制：
#   1. 优先匹配前台窗口进程
#   2. 遇到未配置专属策略的运行中进程时不提前截断，继续匹配定制了专属策略的运行中游戏
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

$mainForm = [System.Activator]::CreateInstance($tMain, @($false))
$null = $mainForm.Handle

$mGetActive = $tMain.GetMethod("GetActiveGameProfile", [System.Reflection.BindingFlags]"NonPublic, Instance")
if ($mGetActive -eq $null) {
    Write-Host "FAIL: MainForm 缺少 GetActiveGameProfile 方法！" -ForegroundColor Red
    $mainForm.Dispose()
    exit 1
}

# 注入 monitoredGameProcesses: 包含一个当前机器必然在运行的系统进程 (如 explorer.exe 或 powershell.exe)
# 以及一个配置了专属策略的假想游戏
$currentProc = [System.Diagnostics.Process]::GetCurrentProcess().ProcessName + ".exe"
Write-Host "当前测试宿主进程: $currentProc" -ForegroundColor DarkGray

$monList = New-Object 'System.Collections.Generic.List[string]'
$monList.Add("dummy_unconfigured_running.exe") # 列表中排在第一，假设正在运行但无专属
$monList.Add($currentProc)                     # 列表中排在第二，定制了专属策略

$profilesDict = New-Object 'System.Collections.Generic.Dictionary[string, RazerBatteryTray.GameProfile]'( [StringComparer]::OrdinalIgnoreCase )
# dummy_unconfigured_running 即使有条目但无专属值
$dummyProf = [Activator]::CreateInstance($tProfile, @("dummy_unconfigured_running.exe", [int]0, [int]0, "", ""))
$profilesDict["dummy_unconfigured_running.exe"] = $dummyProf

# currentProc 配置专属参数
$activeProf = [Activator]::CreateInstance($tProfile, @($currentProc, [int]4, [int]4000, "", "TargetGame"))
$profilesDict[$currentProc] = $activeProf

$fProfiles = $tMain.GetField("gameProfiles", [System.Reflection.BindingFlags]"NonPublic, Instance")
$fProfiles.SetValue($mainForm, $profilesDict)

$fMonList = $tMain.GetField("monitoredGameProcesses", [System.Reflection.BindingFlags]"NonPublic, Instance")
$fMonList.SetValue($mainForm, $monList)

# 调用 GetActiveGameProfile
$result = $mGetActive.Invoke($mainForm, $null)

if ($result -eq $null) {
    Write-Host "FAIL: GetActiveGameProfile 未能匹配到正在运行且定制了专属策略的进程 $currentProc！" -ForegroundColor Red
    $mainForm.Dispose()
    exit 1
}

if ($result.CustomDpiStage -ne 4 -or $result.CustomPollingRate -ne 4000) {
    Write-Host "FAIL: 匹配到的 Profile 参数异常！DPI: $($result.CustomDpiStage), Rate: $($result.CustomPollingRate)" -ForegroundColor Red
    $mainForm.Dispose()
    exit 1
}

Write-Host "PASS: 专属策略成功穿透未定制项，精准匹配定制游戏！" -ForegroundColor Green

$mainForm.Dispose()
Write-Host "`n=== 专属策略匹配测试通过 (ALL PASS) ===" -ForegroundColor Green
exit 0
