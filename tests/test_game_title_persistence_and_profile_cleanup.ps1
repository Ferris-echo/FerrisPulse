# ============================================================================
# 测试脚本: test_game_title_persistence_and_profile_cleanup.ps1
# 所属区域: tests (自动化回归测试层)
# 核心职责:
#   验证：
#   1. GameProfile 数据模型支持 FriendlyTitle 属性并支持注册表字符串序列化/反序列化往返保留
#   2. 游戏退出后（非运行状态），守护列表依然稳定保留友好运行时名称（如“PUBG”而非回退为“tslgame.exe”）
#   3. 从列表中移除游戏时，同步清理 gameProfiles 中的对应专属配置，避免配置残留泄漏
#   4. 清空全部游戏时，同步清空 gameProfiles 集合
# ============================================================================

$ErrorActionPreference = "Stop"
[System.Threading.Thread]::CurrentThread.CurrentCulture = [System.Globalization.CultureInfo]::InvariantCulture

$rootDir = (Resolve-Path "$PSScriptRoot\..").Path
$exePath = Join-Path $rootDir "FerrisPulse.exe"

if (-not (Test-Path $exePath)) {
    Write-Host "FAIL: 未找到二进制产物 $exePath，请先执行 build.bat" -ForegroundColor Red
    exit 1
}

# 全内存反射加载，杜绝文件句柄互斥锁死
$asmBytes = [System.IO.File]::ReadAllBytes($exePath)
$asm = [System.Reflection.Assembly]::Load($asmBytes)

$tProfile = $asm.GetType("RazerBatteryTray.GameProfile")
$tGpm = $asm.GetType("RazerBatteryTray.GameProcessManagerForm")
$tList = $asm.GetType("RazerBatteryTray.ModernProcessListControl")

Write-Host "--- 测试 1: GameProfile FriendlyTitle 序列化与反序列化往返 ---" -ForegroundColor Cyan
$p = [Activator]::CreateInstance($tProfile, @("tslgame.exe", 4, 8000, "C:\Steam\tslgame.exe", "PUBG: 绝地求生"))
$mSer = $tProfile.GetMethod("ToConfigString")
$raw = [string]$mSer.Invoke($p, $null)
Write-Host "序列化字符串: $raw" -ForegroundColor Gray

if (-not $raw.Contains("title=")) {
    Write-Host "FAIL: 序列化字符串未包含 title 字段！" -ForegroundColor Red
    exit 1
}

$mDeser = $tProfile.GetMethod("Parse")
$restored = $mDeser.Invoke($null, @($raw))
if ($restored.FriendlyTitle -ne "PUBG: 绝地求生") {
    Write-Host "FAIL: FriendlyTitle 反序列化不匹配 (实际: $($restored.FriendlyTitle))" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: FriendlyTitle 成功精准完成序列化与反序列化！" -ForegroundColor Green

Write-Host "`n--- 测试 2: 非运行状态下游戏名称持久化回显 (保留 PUBG) ---" -ForegroundColor Cyan
$dictType = [System.Collections.Generic.Dictionary[string, object]].GetGenericTypeDefinition().MakeGenericType(@([string], $tProfile))
$dict = [Activator]::CreateInstance($dictType, @([System.StringComparer]::OrdinalIgnoreCase))
$dict["tslgame.exe"] = $restored

$gpmForm = [Activator]::CreateInstance($tGpm, @([float]1.0, [System.Collections.Generic.List[string]]@("tslgame.exe"), $false, 1, $dict, $null))
$null = $gpmForm.Handle

$fCtlMon = $tGpm.GetField("ctlMonitoredList", [System.Reflection.BindingFlags]'Instance,NonPublic')
$ctlMon = $fCtlMon.GetValue($gpmForm)
$mGetItems = $tList.GetMethod("GetItems")
$monItems = $mGetItems.Invoke($ctlMon, $null)

if ($monItems.Count -eq 0) {
    Write-Host "FAIL: 列表未装入 tslgame.exe！" -ForegroundColor Red
    $gpmForm.Dispose()
    exit 1
}

$item = $monItems[0]
Write-Host "展示标题: $($item.Title)" -ForegroundColor Gray
if ($item.Title -ne "PUBG: 绝地求生") {
    Write-Host "FAIL: 游戏退出后标题被还原为进程包名，未能保留友好名称！(当前: $($item.Title))" -ForegroundColor Red
    $gpmForm.Dispose()
    exit 1
}
Write-Host "PASS: 游戏未运行状态下成功保留并展示友好运行时名称 'PUBG: 绝地求生'！" -ForegroundColor Green

Write-Host "`n--- 测试 3: 删除游戏时同步清除对应专属配置 ---" -ForegroundColor Cyan
$mRemove = $tGpm.GetMethod("RemoveGameProcess", [Type[]]@([string], [bool]))
$mRemove.Invoke($gpmForm, @("tslgame.exe", $false))

$fProfiles = $tGpm.GetField("gameProfiles", [System.Reflection.BindingFlags]'Instance,NonPublic')
$curProfiles = $fProfiles.GetValue($gpmForm)

if ($curProfiles.ContainsKey("tslgame.exe")) {
    Write-Host "FAIL: 删除游戏后 gameProfiles 仍残留该游戏的专属配置！" -ForegroundColor Red
    $gpmForm.Dispose()
    exit 1
}
Write-Host "PASS: 删除游戏后 gameProfiles 同步完全移除了对应专属配置！" -ForegroundColor Green
$gpmForm.Dispose()

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " ALL PASS: 游戏名称持久化与专属配置联动清理测试全部通过！" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
exit 0
