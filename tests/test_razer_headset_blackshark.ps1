# ============================================================================
# 测试脚本: test_razer_headset_blackshark.ps1
# 目标: 验证雷蛇 BlackShark V2 Pro 2.4 (VID 1532 / PID 0555) 耳机适配层
#   1. 源文件存在性 / UTF-8 BOM / C# 5.0 语法铁律 / AI 维护准则
#   2. 编译真实驱动源码, 用真机抓包得到的 64 字节应答帧校验解析
#      (0x21 电量 / 0x2A 充电 / 0x20 连接状态)
#   3. 校验 64 字节命令帧构造 ("PA" 魔数 / 序列号 / 命令字)
#   4. 校验 DeviceManager 挂载与型号映射
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = Resolve-Path "$ScriptDir\.."
Set-Location $ProjectDir

$helperPath = "$ProjectDir\src\Hardware\RazerHeadsetHelper.cs"
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) { $csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe" }

function Fail([string]$msg) {
    Write-Host "FAILED: $msg" -ForegroundColor Red
    exit 1
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 1/4] 文件存在性与 UTF-8 with BOM 编码校验" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if (-not (Test-Path $helperPath)) { Fail "src\Hardware\RazerHeadsetHelper.cs 尚未创建！" }
$bytes = [System.IO.File]::ReadAllBytes($helperPath)
if ($bytes.Length -lt 3 -or $bytes[0] -ne 0xEF -or $bytes[1] -ne 0xBB -or $bytes[2] -ne 0xBF) {
    Fail "RazerHeadsetHelper.cs 不是 UTF-8 with BOM 编码！"
}
Write-Host "PASSED: 文件存在且为 UTF-8 with BOM" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 2/4] C# 5.0 语法铁律与协议常量审查" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$content = [System.IO.File]::ReadAllText($helperPath, [System.Text.Encoding]::UTF8)
if ([regex]::IsMatch($content, '\$".*?"')) { Fail "发现禁用的字符串插值语法" }
if ([regex]::IsMatch($content, '\w+\?\.')) { Fail "发现禁用的空条件运算符" }
if ([regex]::IsMatch($content, 'nameof\s*\(')) { Fail "发现禁用的 nameof 语法" }
if ([regex]::IsMatch($content, 'out\s+var\s+')) { Fail "发现禁用的 out var 内联声明" }
if (-not $content.Contains("⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️")) {
    Fail "缺少头部 AI 维护准则声明"
}
foreach ($token in @("0x1532", "0x0555", "0xFF00", "0x50", "0x41", "0x49", "CmdGetBattery", "CmdGetCharging")) {
    if (-not $content.Contains($token)) { Fail "驱动中缺少协议常量: $token" }
}
Write-Host "PASSED: 语法合规，协议常量齐备" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 3/4] 编译真实驱动并校验帧解析与构造" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$tempDir = Join-Path $env:TEMP ("rzhs_test_" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Force -Path $tempDir | Out-Null

$stub = @'
namespace RazerBatteryTray
{
    public static class RazerDeviceHelper
    {
        public static string GetRazerDisplayName(int pid, string deviceName) { return deviceName; }
    }
}
'@

# 真机抓包得到的应答帧 (已裁剪到 16 字节有效区)
$harness = @'
using System;
using RazerBatteryTray;

internal static class Harness
{
    private static int Main()
    {
        // 0x21 Get Battery Status -> 0x58 = 88%
        byte[] batteryRx = new byte[] { 0x02, 0x0E, 0x50, 0x49, 0x08, 0xD6, 0x54, 0x66,
                                        0x0F, 0x00, 0x04, 0x00, 0x21, 0x01, 0x01, 0x58 };
        // 0x2A Get charging status -> 0x00 = 未充电
        byte[] chargeRx = new byte[] { 0x02, 0x0E, 0x50, 0x49, 0x08, 0xD7, 0x5F, 0x66,
                                       0x0F, 0x00, 0x04, 0x00, 0x2A, 0x01, 0x01, 0x00 };
        // 0x20 Get wireless Connection Status -> 0x01 = 在线
        byte[] connRx = new byte[] { 0x02, 0x0E, 0x50, 0x49, 0x08, 0xD5, 0x4C, 0x66,
                                     0x0F, 0x00, 0x04, 0x00, 0x20, 0x01, 0x01, 0x01 };

        Console.WriteLine("battery=" + RazerHeadsetHelper.ParseReplyValue(batteryRx, RazerHeadsetHelper.CmdGetBattery));
        Console.WriteLine("charge=" + RazerHeadsetHelper.ParseReplyValue(chargeRx, RazerHeadsetHelper.CmdGetCharging));
        Console.WriteLine("conn=" + RazerHeadsetHelper.ParseReplyValue(connRx, RazerHeadsetHelper.CmdGetConnection));

        // 错误命令字不得被误认
        Console.WriteLine("wrongcmd=" + RazerHeadsetHelper.ParseReplyValue(batteryRx, RazerHeadsetHelper.CmdGetCharging));
        // 非法帧安全拒绝
        Console.WriteLine("badmagic=" + RazerHeadsetHelper.ParseReplyValue(new byte[] { 0x02, 0x0E, 0x00, 0x00, 0x08, 0, 0, 0, 0, 0, 4, 0, 0x21, 1, 1, 88 }, RazerHeadsetHelper.CmdGetBattery));
        Console.WriteLine("nullrx=" + RazerHeadsetHelper.ParseReplyValue(null, RazerHeadsetHelper.CmdGetBattery));
        Console.WriteLine("shortrx=" + RazerHeadsetHelper.ParseReplyValue(new byte[4], RazerHeadsetHelper.CmdGetBattery));

        byte[] tx = RazerHeadsetHelper.BuildFrame(RazerHeadsetHelper.CmdGetBattery);
        Console.WriteLine("txlen=" + tx.Length);
        Console.WriteLine("tx=" + BitConverter.ToString(tx, 0, 12));
        return 0;
    }
}
'@

Set-Content -Path (Join-Path $tempDir "stub.cs") -Value $stub -Encoding UTF8
Set-Content -Path (Join-Path $tempDir "harness.cs") -Value $harness -Encoding UTF8

$exe = Join-Path $tempDir "rzhs_test.exe"
$sources = @(
    (Join-Path $tempDir "harness.cs"),
    (Join-Path $tempDir "stub.cs"),
    $helperPath,
    "$ProjectDir\src\Models\MouseBatteryInfo.cs",
    "$ProjectDir\src\Models\DeviceCapabilities.cs",
    "$ProjectDir\src\Models\CustomDeviceAction.cs",
    "$ProjectDir\src\Models\SonyPulseExploreState.cs",
    "$ProjectDir\src\Models\DeviceCategory.cs"
)
& $csc /nologo /target:exe /codepage:65001 /r:System.Drawing.dll "/out:$exe" $sources | Out-String | Write-Host
if ($LASTEXITCODE -ne 0) { Fail "驱动源码编译失败 (csc exit=$LASTEXITCODE)" }

$output = & $exe
if ($LASTEXITCODE -ne 0) { Fail "测试宿主运行失败" }
$map = @{}
foreach ($line in $output) {
    $kv = $line -split '=', 2
    if ($kv.Count -eq 2) { $map[$kv[0]] = $kv[1] }
}

if ($map["battery"] -ne "88") { Fail "电量解析错误: $($map['battery']) (期望 88)" }
if ($map["charge"] -ne "0") { Fail "充电解析错误: $($map['charge']) (期望 0)" }
if ($map["conn"] -ne "1") { Fail "连接状态解析错误: $($map['conn']) (期望 1)" }
if ($map["wrongcmd"] -ne "-1") { Fail "命令字不匹配时应返回 -1，实际 $($map['wrongcmd'])" }
if ($map["badmagic"] -ne "-1") { Fail "魔数错误时应返回 -1，实际 $($map['badmagic'])" }
if ($map["nullrx"] -ne "-1") { Fail "空帧应返回 -1，实际 $($map['nullrx'])" }
if ($map["shortrx"] -ne "-1") { Fail "过短帧应返回 -1，实际 $($map['shortrx'])" }
if ($map["txlen"] -ne "64") { Fail "命令帧长度错误: $($map['txlen']) (期望 64)" }
if ($map["tx"] -ne "02-80-08-00-00-50-41-08-00-03-21-00") { Fail "命令帧内容错误: $($map['tx'])" }

Remove-Item -Recurse -Force $tempDir -ErrorAction SilentlyContinue
Write-Host "PASSED: 真机应答帧解析 (88% / 未充电 / 在线) 与命令帧构造全部正确" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 4/4] 调度接入与型号映射校验" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$deviceManager = [System.IO.File]::ReadAllText("$ProjectDir\src\Hardware\DeviceManager.cs", [System.Text.Encoding]::UTF8)
if (-not $deviceManager.Contains("RazerHeadsetHelper.QueryAllRazerHeadsets()")) {
    Fail "DeviceManager.QueryAllDevices() 未挂载雷蛇耳机轮询"
}

$model = "$ProjectDir\src\Hardware\RazerHeadsetHelper.cs"
$modelContent = [System.IO.File]::ReadAllText($model, [System.Text.Encoding]::UTF8)
if (-not $modelContent.Contains("BlackShark")) { Fail "缺少 BlackShark 型号映射" }

Write-Host "PASSED: 调度挂载与型号映射齐备" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " ALL PASS: BlackShark V2 Pro 2.4 适配测试全部通过！" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
exit 0
