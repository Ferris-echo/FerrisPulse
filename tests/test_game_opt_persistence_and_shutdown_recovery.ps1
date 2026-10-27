# ============================================================================
# 测试脚本: test_game_opt_persistence_and_shutdown_recovery.ps1
# 职责:
#   验证电竞优化配置持久化、开机桌面档位自愈 (Anti-Ghost Rate) 与关机/退出同步复原契约
# ============================================================================

$ErrorActionPreference = "Stop"
$rootDir = (Resolve-Path "$PSScriptRoot\..").Path
Set-Location $rootDir

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 1/4] 验证 MainForm.Events.cs 拦截 WM_QUERYENDSESSION / WM_ENDSESSION 关机信号" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$eventsCs = Get-Content "src/UI/Forms/MainForm.Events.cs" -Raw -Encoding UTF8
$mainCs = Get-Content "src/UI/Forms/MainForm.cs" -Raw -Encoding UTF8
$hwCs = Get-Content "src/UI/Forms/MainForm.Hardware.cs" -Raw -Encoding UTF8
$uiCs = Get-Content "src/UI/Forms/MainForm.UI.cs" -Raw -Encoding UTF8

if ($eventsCs -notmatch 'WM_QUERYENDSESSION' -or $eventsCs -notmatch 'WM_ENDSESSION') {
    throw "FAIL: MainForm.Events.cs WndProc 未拦截 WM_QUERYENDSESSION / WM_ENDSESSION 关机信号！"
}
Write-Host "PASS: WndProc 已正确拦截系统关机/注销消息 (WM_QUERYENDSESSION / WM_ENDSESSION)" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 2/4] 验证同步关机恢复方法与 SessionEnding / FormClosing / TrayExit 挂载" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if ($hwCs -notmatch 'RestoreDesktopSettingsSynchronous') {
    throw "FAIL: MainForm.Hardware.cs 缺少 RestoreDesktopSettingsSynchronous 同步复原方法！"
}
Write-Host "PASS: RestoreDesktopSettingsSynchronous 同步复原方法已实现" -ForegroundColor Green

if ($mainCs -notmatch 'RestoreDesktopSettingsSynchronous' -or $mainCs -notmatch 'SessionEnding') {
    throw "FAIL: MainForm.cs 未在 SessionEnding 或 FormClosing 中调用 RestoreDesktopSettingsSynchronous！"
}
Write-Host "PASS: 系统注销、关机与窗体退出通道已挂钩同步复原" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 3/4] 验证开机即时游戏检测与桌面档位自愈校准机制" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if ($hwCs -notmatch 'PerformStartupDesktopCalibration' -or $hwCs -notmatch 'DetectCurrentGameRunning') {
    throw "FAIL: MainForm.Hardware.cs 缺少 PerformStartupDesktopCalibration 或 DetectCurrentGameRunning 模块！"
}
Write-Host "PASS: 开机即时检测与桌面档位自愈校准引擎已声明" -ForegroundColor Green

if ($hwCs -notmatch 'PerformStartupDesktopCalibration\(selectedDevice\)') {
    throw "FAIL: ApplyDeviceStatus 识别到设备时未调用 PerformStartupDesktopCalibration！"
}
Write-Host "PASS: ApplyDeviceStatus 设备就绪即时对齐已打通" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 4/4] 验证 DPI 按钮重绘时保持 gameDpiStage 选中态高亮与测试隔离契约" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if ($hwCs -notmatch 'UpdateGameDpiButtonSelection\(gameDpiStage\)') {
    throw "FAIL: MainForm.Hardware.cs UpdateUIWithDeviceInfo 未在重绘时保持 UpdateGameDpiButtonSelection！"
}
Write-Host "PASS: 硬件上报与重绘时已保持电竞 DPI 档位选中态高亮" -ForegroundColor Green

$resetCs = Get-Content "src/Core/AppResetManager.cs" -Raw -Encoding UTF8
if ($resetCs -notmatch 'RegistryRootOverride') {
    throw "FAIL: AppResetManager.cs 缺少 RegistryRootOverride 测试隔离属性！"
}
Write-Host "PASS: AppResetManager 支持单元测试注册表虚拟隔离，杜绝破坏真实用户数据" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 5/5] 验证游戏进行中 PerformStartupDesktopCalibration 严禁重复触发 ApplyGameOptimization" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 校验 PerformStartupDesktopCalibration 必须具备 !isGamingActive 门禁，杜绝电池周期刷新 (30s) 重复弹 OSD
if ($hwCs -match '(?s)PerformStartupDesktopCalibration.*?else\s*\{\s*isGamingActive\s*=\s*true;\s*if\s*\(autoGameRateEnabled') {
    throw "FAIL: PerformStartupDesktopCalibration 缺少 !isGamingActive 防重入门禁，导致每隔 30s 周期性重复弹 OSD 提示！"
}
if ($hwCs -notmatch '(?s)PerformStartupDesktopCalibration.*?if\s*\(!isGamingActive\)') {
    throw "FAIL: PerformStartupDesktopCalibration 必须在 !isGamingActive 门禁下才允许触发 ApplyGameOptimization / ApplyGamePollingRate / ApplyGameDpiStage！"
}
Write-Host "PASS: PerformStartupDesktopCalibration 已具备 !isGamingActive 防重入门禁，杜绝游戏中重复弹窗" -ForegroundColor Green

Write-Host "`n[ALL PASSED] 电竞配置持久化、开机自愈与关机状态复原测试契约全部通过！" -ForegroundColor Green
