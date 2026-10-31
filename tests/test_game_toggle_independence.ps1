# ============================================================================
# 测试脚本: test_game_toggle_independence.ps1
# 职责:
#   验证「电竞回报率」与「电竞 DPI」两个开关彼此独立生效。
#   核心约定（用户需求）：
#     1. 未勾选电竞回报率 -> 进入/退出游戏都绝不改写回报率；
#     2. 未勾选电竞 DPI   -> 进入/退出游戏都绝不改写 DPI；
#     3. 两个都未勾选     -> 完全不触碰硬件；
#     4. 勾选后才在进入游戏时写入电竞值、退出游戏时恢复日用值。
#   本测试为静态源码契约测试，不接触真实硬件。
# ============================================================================

$ErrorActionPreference = "Stop"
$rootDir = (Resolve-Path "$PSScriptRoot\..").Path
Set-Location $rootDir

$script:fail = 0
$script:pass = 0

function Ok([string]$msg) {
    $script:pass++
    Write-Host ("PASS: " + $msg) -ForegroundColor Green
}
function Ng([string]$msg) {
    $script:fail++
    Write-Host ("FAIL: " + $msg) -ForegroundColor Red
}
function AssertHas([string]$text, [string]$needle, [string]$msg) {
    if ($text.Contains($needle)) { Ok $msg } else { Ng ($msg + " (missing: " + $needle + ")") }
}
function AssertNot([string]$text, [string]$needle, [string]$msg) {
    if (-not $text.Contains($needle)) { Ok $msg } else { Ng ($msg + " (unexpected: " + $needle + ")") }
}

function Read-Cs([string]$rel) {
    $full = Join-Path $rootDir $rel
    if (-not (Test-Path $full)) { throw ("missing file: " + $rel) }
    return [System.IO.File]::ReadAllText($full)
}

function Get-Body([string]$text, [string]$head, [string]$nextHead) {
    $i = $text.IndexOf($head)
    if ($i -lt 0) { return "" }
    $j = $text.IndexOf($nextHead, $i)
    if ($j -lt 0) { return $text.Substring($i) }
    return $text.Substring($i, $j - $i)
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 1/4] ApplyGameOptimization 自带双开关闸门" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$hw = Read-Cs "src/UI/Forms/MainForm.Hardware.cs"

$opt = Get-Body $hw "private void ApplyGameOptimization(bool isEntering)" "private void ApplyGamePollingRate()"
if ($opt.Length -eq 0) { throw "FAIL: 找不到 ApplyGameOptimization 方法体" }

AssertHas $opt "bool applyRate = autoGameRateEnabled;" "ApplyGameOptimization 读取电竞回报率开关作为独立闸门"
AssertHas $opt "bool applyDpi = autoGameDpiEnabled;" "ApplyGameOptimization 读取电竞 DPI 开关作为独立闸门"
AssertHas $opt "if (!applyRate && !applyDpi) return;" "两个开关都未勾选时完全不动硬件"

$rateWrites = ([regex]::Matches($opt, [regex]::Escape("HardwareCommandBus.SetPollingRate"))).Count
$dpiWrites = ([regex]::Matches($opt, [regex]::Escape("HardwareCommandBus.SetDpi("))).Count
if ($rateWrites -eq 1) { Ok "ApplyGameOptimization 内回报率写入点唯一" } else { Ng ("ApplyGameOptimization 内回报率写入点应为 1，实际 " + $rateWrites) }
if ($dpiWrites -eq 1) { Ok "ApplyGameOptimization 内 DPI 写入点唯一" } else { Ng ("ApplyGameOptimization 内 DPI 写入点应为 1，实际 " + $dpiWrites) }

$gateRate = "if (applyRate)" + [char]13 + [char]10 + "                {" + [char]13 + [char]10 + "                    for (int attempt = 0; attempt < 3; attempt++)" + [char]13 + [char]10 + "                    {" + [char]13 + [char]10 + "                        if (HardwareCommandBus.SetPollingRate"
if ($opt.Contains($gateRate)) { Ok "回报率写入被 if (applyRate) 包裹" } else { Ng "回报率写入未被 if (applyRate) 包裹" }

$gateDpi = "if (applyDpi)" + [char]13 + [char]10 + "                {" + [char]13 + [char]10 + "                    for (int attempt = 0; attempt < 3; attempt++)" + [char]13 + [char]10 + "                    {" + [char]13 + [char]10 + "                        if (HardwareCommandBus.SetDpi("
if ($opt.Contains($gateDpi)) { Ok "DPI 写入被 if (applyDpi) 包裹" } else { Ng "DPI 写入未被 if (applyDpi) 包裹" }

if ($opt.Contains("osdForm.ShowGameOptimization(isEntering, applyDpi, targetDpi, targetDpiY, targetStage, stCount, applyRate, targetHz, brand)")) {
    Ok "OSD 按实际生效的通道展示，不虚报未勾选的项目"
} else { Ng "OSD 未按 applyRate/applyDpi 实际生效情况展示" }

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 2/4] 单通道入口各自带闸门" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$apr = Get-Body $hw "private void ApplyGamePollingRate()" "private void RestoreDesktopPollingRate()"
if ($apr.Length -eq 0) { throw "FAIL: 找不到 ApplyGamePollingRate 方法体" }
AssertHas $apr "if (!autoGameRateEnabled) return;" "ApplyGamePollingRate 未勾选电竞回报率时直接返回"

$agd = Get-Body $hw "private void ApplyGameDpiStage()" "private void RestoreDesktopDpiStage()"
if ($agd.Length -eq 0) { throw "FAIL: 找不到 ApplyGameDpiStage 方法体" }
AssertHas $agd "if (!autoGameDpiEnabled) return;" "ApplyGameDpiStage 未勾选电竞 DPI 时直接返回"

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 3/4] 调用点不得绕过闸门直写硬件" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$monitor = Get-Body $hw "private void StartGameMonitor()" "private void SyncMonitoredDpiState("
if ($monitor.Length -eq 0) { throw "FAIL: 找不到 StartGameMonitor 方法体" }
AssertNot $monitor "HardwareCommandBus.SetPollingRate" "游戏监听轮询内不直接写回报率"
AssertNot $monitor "HardwareCommandBus.SetDpi(" "游戏监听轮询内不直接写 DPI"
AssertHas $monitor "if (!autoGameRateEnabled && !autoGameDpiEnabled) return;" "游戏监听在双开关全关时直接退出"

$cal = Get-Body $hw "private void PerformStartupDesktopCalibration(MouseBatteryInfo dev)" "private void RestoreDesktopSettingsSynchronous()"
if ($cal.Length -eq 0) { throw "FAIL: 找不到 PerformStartupDesktopCalibration 方法体" }
AssertNot $cal "HardwareCommandBus.SetPollingRate" "启动校准不直接写回报率"
AssertNot $cal "HardwareCommandBus.SetDpi(" "启动校准不直接写 DPI"

$sync = Get-Body $hw "private void RestoreDesktopSettingsSynchronous()" "private void StartGameMonitor()"
if ($sync.Length -eq 0) { throw "FAIL: 找不到 RestoreDesktopSettingsSynchronous 方法体" }
AssertHas $sync "if (autoGameRateEnabled && desktopPollingRate > 0)" "同步校准写回报率前检查电竞回报率开关"
AssertHas $sync "if (autoGameDpiEnabled && desktopDpiStage > 0)" "同步校准写 DPI 前检查电竞 DPI 开关"

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 4/4] 文件规范：UTF-8 with BOM 与无 CRCRLF 腐败" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$files = @(
    "src/UI/Forms/MainForm.Hardware.cs",
    "src/UI/Forms/MainForm.cs",
    "src/UI/Forms/MainForm.UI.cs"
)
foreach ($rel in $files) {
    $full = Join-Path $rootDir $rel
    $bytes = [System.IO.File]::ReadAllBytes($full)
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    if ($hasBom) { Ok ($rel + " 保持 UTF-8 with BOM") } else { Ng ($rel + " 丢失 UTF-8 BOM") }
    $txt = [System.Text.Encoding]::UTF8.GetString($bytes)
    $crCount = ([regex]::Matches($txt, [regex]::Escape(([char]13).ToString() + ([char]13).ToString() + ([char]10).ToString()))).Count
    if ($crCount -eq 0) { Ok ($rel + " 无 CRCRLF 换行腐败") } else { Ng ($rel + " 出现 " + $crCount + " 处 CRCRLF 腐败") }
}

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host (" 通过: " + $script:pass + "   失败: " + $script:fail) -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if ($script:fail -gt 0) {
    Write-Host "[FAILED] 电竞开关独立性测试未通过" -ForegroundColor Red
    exit 1
}
Write-Host "[ALL PASSED] 电竞开关独立性测试通过：未勾选的电竞项在游戏场景绝不改写硬件" -ForegroundColor Green
exit 0
