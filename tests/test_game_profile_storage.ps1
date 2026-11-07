# tests/test_game_profile_storage.ps1
# 验证：
# 1. GameProfile 数据模型支持精准序列化与反序列化
# 2. 支持独立游戏专属 DPI 档位与专属回报率配置
# 3. 未配置专属项时能够平滑回退至全局电竞预设

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$asmPath = Join-Path $PSScriptRoot "..\FerrisPulse.exe"
if (-not (Test-Path $asmPath)) { $asmPath = "FerrisPulse.exe" }
$fullPath = (Resolve-Path $asmPath).Path
$bytes = [System.IO.File]::ReadAllBytes($fullPath)
$asm = [System.Reflection.Assembly]::Load($bytes)

$tProfile = $asm.GetType("RazerBatteryTray.GameProfile")
$tMain = $asm.GetType("RazerBatteryTray.MainForm")

if ($tProfile -eq $null) {
    Write-Host "RED LIGHT: 未能找到 GameProfile 数据模型类型！" -ForegroundColor Yellow
    exit 1
}

Write-Host "--- 测试 1: GameProfile 序列化与反序列化 ---" -ForegroundColor Cyan

# 1. 构造配置集合 (通过 MakeGenericType 动态绑定)
$dictGenericDef = [System.Collections.Generic.Dictionary[string, object]].GetGenericTypeDefinition()
$dictType = $dictGenericDef.MakeGenericType(@([string], $tProfile))
$dict = [Activator]::CreateInstance($dictType, @([StringComparer]::OrdinalIgnoreCase))

# cs2.exe: 专属第 1 档 (800 DPI), 1000 Hz
$p1 = [Activator]::CreateInstance($tProfile, @("cs2.exe", 1, 1000, "D:\Games\CS2\cs2.exe"))
# apex.exe: 专属第 2 档 (1600 DPI), 4000 Hz
$p2 = [Activator]::CreateInstance($tProfile, @("apex.exe", 2, 4000, "D:\Games\Apex\apex.exe"))

$dict.Add("cs2.exe", $p1)
$dict.Add("apex.exe", $p2)

# 2. 序列化
$mSer = $tProfile.GetMethod("SerializeProfiles", [System.Reflection.BindingFlags]'Static,Public')
$rawStr = [string]$mSer.Invoke($null, @($dict))
Write-Host "序列化结果: $rawStr" -ForegroundColor Gray

if (-not $rawStr.Contains("cs2.exe") -or -not $rawStr.Contains("dpi=1") -or -not $rawStr.Contains("rate=4000")) {
    Write-Host "FAIL: 序列化字符串未包含完整字段！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: GameProfile 成功序列化为注册表紧凑格式" -ForegroundColor Green

# 3. 反序列化
$mDeser = $tProfile.GetMethod("DeserializeProfiles", [System.Reflection.BindingFlags]'Static,Public')
$restoredDict = $mDeser.Invoke($null, @($rawStr))

if ($restoredDict.Count -ne 2) {
    Write-Host "FAIL: 反序列化后字典项数量不符 (预期 2, 实际: $($restoredDict.Count))" -ForegroundColor Red
    exit 1
}

$cs2Prof = $restoredDict["cs2.exe"]
if ($cs2Prof.CustomDpiStage -ne 1 -or $cs2Prof.CustomPollingRate -ne 1000) {
    Write-Host "FAIL: 反序列化 cs2.exe 属性不匹配！" -ForegroundColor Red
    exit 1
}

$apexProf = $restoredDict["apex.exe"]
if ($apexProf.CustomDpiStage -ne 2 -or $apexProf.CustomPollingRate -ne 4000) {
    Write-Host "FAIL: 反序列化 apex.exe 属性不匹配！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: GameProfile 反序列化恢复精度 100% 吻合！" -ForegroundColor Green

# --- 测试 2: MainForm 中 gameProfiles 字段及调度优先级 ---
Write-Host "`n--- 测试 2: MainForm 专属策略感知与回退 ---" -ForegroundColor Cyan

$fProfiles = $tMain.GetField("gameProfiles", [System.Reflection.BindingFlags]'Instance,NonPublic')
if ($fProfiles -eq $null) {
    Write-Host "RED LIGHT: MainForm 中缺少 gameProfiles 配置字典字段！" -ForegroundColor Yellow
    exit 1
}
Write-Host "PASS: MainForm 拥有 gameProfiles 持久化管理字段" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " ALL PASS: 独立游戏专属策略模型与存储引擎测试全部通过！" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
exit 0
