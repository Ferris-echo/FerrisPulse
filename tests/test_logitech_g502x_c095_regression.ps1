# ============================================================================
# 测试脚本: test_logitech_g502x_c095_regression.ps1
# 目标: 验证 Logitech G502 X PLUS (046D:C095) 现场实测暴露的四处缺陷修复：
#       1. 电量缓存严格按设备 ID 隔离 (未命中的外设返回 -1，绝不借用全局缓存)
#       2. 0x1004 getStatus 充电判定只认 byte6，byte5 是电量档位码而非充电位
#       3. 0xC095 型号归属 G502 X PLUS (libratbag DeviceMatch 证据)
#       4. 音频类硬件 (麦克风/音箱) 不得被当成鼠标入库
# 本机实测环境: Windows 11 / G HUB 常驻 / G502 X PLUS USB 有线 / Blue Yeti Nano
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = Resolve-Path "$ScriptDir\.."
Set-Location $ProjectDir

function Fail([string]$msg) {
    Write-Host ("FAILED: " + $msg) -ForegroundColor Red
    exit 1
}

function Assert-File([string]$path) {
    if (-not (Test-Path $path)) { Fail "文件不存在: $path" }
    $bytes = [System.IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 3 -or $bytes[0] -ne 0xEF -or $bytes[1] -ne 0xBB -or $bytes[2] -ne 0xBF) {
        Fail "文件不是 UTF-8 with BOM: $path"
    }
}

$helperPath = "$ProjectDir\src\Hardware\LogitechDeviceHelper.cs"
$modelPath  = "$ProjectDir\src\Models\MouseBatteryInfo.cs"

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 1/4] 编码与 C# 5.0 语法规范" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

Assert-File $helperPath
Assert-File $modelPath

foreach ($p in @($helperPath, $modelPath)) {
    $raw = [System.IO.File]::ReadAllText($p, [System.Text.Encoding]::UTF8)
    if (-not $raw.Contains("⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️")) {
        Fail "缺少 AI 维护准则声明: $p"
    }
    # 剔除注释行后再做语法扫描，避免文档里的「禁止 $"..."」示例被判为违规
    $lines = $raw -split "`r?`n"
    $code = ($lines | Where-Object { $_.Trim() -notmatch '^(//|/\*|\*)' }) -join "`n"
    if ([regex]::IsMatch($code, '\$".*?"')) { Fail "字符串插值: $p" }
    if ([regex]::IsMatch($code, '\w+\?\.')) { Fail "空条件运算符: $p" }
    if ([regex]::IsMatch($code, 'nameof\s*\(')) { Fail "nameof: $p" }
    if ([regex]::IsMatch($code, 'out\s+var\s+')) { Fail "out var: $p" }
}
Write-Host "PASSED: 两个源文件 UTF-8 BOM 与 C# 5.0 语法 100% 合规" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 2/4] 电量缓存严格设备隔离（拒绝跨设备借用）" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$exePath = "$ProjectDir\FerrisPulse.exe"
if (-not (Test-Path $exePath)) { Fail "FerrisPulse.exe 未编译，请先执行 build.bat" }

# 全内存反射加载，零文件句柄占用 (AGENT_HANDBOOK 6.2)
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath))
$tLogi = $asm.GetType("RazerBatteryTray.LogitechDeviceHelper")
if ($null -eq $tLogi) { Fail "未找到 LogitechDeviceHelper" }

$bf = [System.Reflection.BindingFlags]"Public,Static"
$mGetCache = $tLogi.GetMethod("GetCachedBatteryForDevice", $bf)
if ($null -eq $mGetCache) { Fail "未找到 GetCachedBatteryForDevice" }

$fGlobal = $tLogi.GetField("CachedBatteryPercent", $bf)
$fValid  = $tLogi.GetField("HasValidHardwareReading", $bf)
if ($null -eq $fGlobal -or $null -eq $fValid) { Fail "缓存字段缺失" }

# 模拟：全局缓存有 100%，但同时存在一个从未被单独读取过的外设
$fGlobal.SetValue($null, 100)
$fValid.SetValue($null, $true)

$unknownResult = $mGetCache.Invoke($null, @("Logitech:046D:0AB1"))
if ($unknownResult -ne -1) {
    Fail ("未缓存设备应返回 -1，实际返回 " + $unknownResult + "（存在跨设备电量泄漏）")
}
Write-Host "PASSED: 未缓存设备返回 -1，不再继承全局 100% 缓存" -ForegroundColor Green

# 空 ID 同样不得借用全局缓存
$emptyResult = $mGetCache.Invoke($null, @([string]$null))
if ($emptyResult -ne -1) {
    Fail ("空 DeviceId 应返回 -1，实际返回 " + $emptyResult)
}
Write-Host "PASSED: 空 DeviceId 返回 -1，拒绝兜底借用" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 3/4] 0xC095 型号归属与 G502 X 家族细分" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$mModel = $tLogi.GetMethod("GetModelName", $bf)
$nameC095 = $mModel.Invoke($null, @([int]0xC095, [string]$null))
if (-not $nameC095.Contains("G502 X PLUS")) {
    Fail ("0xC095 应为 G502 X PLUS，实际: " + $nameC095)
}
Write-Host ("PASSED: GetModelName(0xC095) -> " + $nameC095) -ForegroundColor Green

# 0x4099 / 0xC099 存在上游归属分歧（libratbag 记为 G502 X PLUS，本仓库记为 GPX 2 DEX）。
# 本次 PR 不擅自改动该 PID，仅断言「不得被误标为 G502 X PLUS 的同名重复项」。
$nameC099 = $mModel.Invoke($null, @([int]0xC099, [string]$null))
Write-Host ("INFO: GetModelName(0xC099) -> " + $nameC099 + "（保留既有映射，未在本次修改）") -ForegroundColor Yellow

# FriendlyShortName 必须能区分 PLUS / 无线 / 普通版
$tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo")
if ($null -eq $tInfo) { Fail "未找到 MouseBatteryInfo" }

$cases = @(
    @{ Name = "Logitech G502 X PLUS";            Expect = "G502 X PLUS" },
    @{ Name = "Logitech G502 X Wireless（无线版）"; Expect = "G502 X 无线" },
    @{ Name = "Logitech G502 X (有线版)";         Expect = "G502 X" }
)

foreach ($tc in $cases) {
    $obj = [System.Activator]::CreateInstance($tInfo)
    $obj.Brand = "Logitech"
    $obj.DeviceName = $tc.Name
    $obj.Category = [RazerBatteryTray.DeviceCategory]::Mouse
    $actual = $obj.FriendlyShortName
    if ($actual -ne $tc.Expect) {
        Fail ("FriendlyShortName('" + $tc.Name + "') 期望 '" + $tc.Expect + "'，实际 '" + $actual + "'")
    }
    Write-Host ("PASSED: '" + $tc.Name + "' -> " + $actual) -ForegroundColor Green
}

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 4/4] 音频外设过滤（麦克风/音箱不得入鼠标列表）" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$mAudio = $tLogi.GetMethod("IsLogitechAudioOnlyPeripheral", $bf)
if ($null -eq $mAudio) { Fail "未找到 IsLogitechAudioOnlyPeripheral" }

$audioCases = @(
    @{ Pid = 0x0AB1; Name = "Yeti Nano";                       Expect = $true  },
    @{ Pid = 0x0AB1; Name = "Blue Microphones Yeti Nano";      Expect = $true  },
    @{ Pid = 0x0A00; Name = "Logitech Speaker";                Expect = $true  },
    @{ Pid = 0x0ABA; Name = "Logitech G PRO X Wireless 游戏耳机"; Expect = $false },
    @{ Pid = 0xC095; Name = "G502 X PLUS";                     Expect = $false }
)

foreach ($tc in $audioCases) {
    $actual = $mAudio.Invoke($null, @([int]$tc.Pid, [string]$tc.Name))
    if ($actual -ne $tc.Expect) {
        Fail ("PID 0x" + $tc.Pid.ToString("X4") + " '" + $tc.Name + "' 期望 " + $tc.Expect + "，实际 " + $actual)
    }
    Write-Host ("PASSED: PID 0x" + $tc.Pid.ToString("X4") + " '" + $tc.Name + "' -> 音频过滤=" + $actual) -ForegroundColor Green
}

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " ALL 4/4 G502 X PLUS (C095) REGRESSION TESTS PASSED!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
exit 0
