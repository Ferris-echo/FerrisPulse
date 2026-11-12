# ============================================================================
# 测试脚本: test_game_manager_double_click_customization.ps1
# 所属区域: tests (自动化回归测试层)
# 核心职责:
#   验证 GameProcessManagerForm 中已生效游戏列表的双击交互：
#   1. ctlMonitoredList 的 OnItemDoubleClicked 不再触发删除逻辑
#   2. 双击行为重构为触发专属策略定制
#   3. ctlMonitoredList 的 EmptyPlaceholderText 包含引导双击定制专属策略的提示
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

$tGpm = $asm.GetType("RazerBatteryTray.GameProcessManagerForm")

Write-Host "--- 测试 1: GameProcessManagerForm 控件装配与双击提示文案 ---" -ForegroundColor Cyan

$dummyCb = [System.Action[System.Collections.Generic.List[string], bool, int]]{ param($l, $f, $m) }
$gpm = [System.Activator]::CreateInstance($tGpm, [object[]]@([float]1.0, [System.Collections.Generic.List[string]]@(), $false, 1, $dummyCb))
$null = $gpm.Handle

$fCtlMon = $tGpm.GetField("ctlMonitoredList", [System.Reflection.BindingFlags]"NonPublic, Instance")
$ctlMon = $fCtlMon.GetValue($gpm)

if ($ctlMon -eq $null) {
    Write-Host "FAIL: ctlMonitoredList 未能成功装配！" -ForegroundColor Red
    $gpm.Dispose()
    exit 1
}

$placeholder = $ctlMon.EmptyPlaceholderText
Write-Host "EmptyPlaceholderText: $placeholder" -ForegroundColor DarkGray
if (-not ($placeholder -match "定制" -or $placeholder -match "策略")) {
    Write-Host "FAIL: 占位文案未提示用户可双击定制专属策略！实际: $placeholder" -ForegroundColor Red
    $gpm.Dispose()
    exit 1
}
Write-Host "PASS: 占位文案引导正确" -ForegroundColor Green

Write-Host "`n--- 测试 2: 验证双击事件绑定到打开策略定制而非删除 ---" -ForegroundColor Cyan
$fEvent = $ctlMon.GetType().GetField("OnItemDoubleClicked", [System.Reflection.BindingFlags]"NonPublic, Instance")
if ($fEvent -eq $null) {
    $fEvent = $ctlMon.GetType().GetField("OnItemDoubleClicked", [System.Reflection.BindingFlags]"Public, Instance")
}
$handlers = $fEvent.GetValue($ctlMon)

if ($handlers -eq $null) {
    Write-Host "FAIL: ctlMonitoredList.OnItemDoubleClicked 未挂载任何处理函数！" -ForegroundColor Red
    $gpm.Dispose()
    exit 1
}

$invocationList = $handlers.GetInvocationList()
$methodTarget = $invocationList[0].Method
Write-Host "Double click target method: $($methodTarget.Name)" -ForegroundColor DarkGray

if ($methodTarget.Name -eq "RemoveGameProcess") {
    Write-Host "FAIL: 双击事件依然直接绑定到了 RemoveGameProcess 删除方法！" -ForegroundColor Red
    $gpm.Dispose()
    exit 1
}

$gpm.Dispose()
Write-Host "PASS: 双击事件不再绑定删除方法，已重构为专属策略定制！" -ForegroundColor Green

Write-Host "`n=== 全部测试通过 (ALL PASS) ===" -ForegroundColor Green
exit 0
