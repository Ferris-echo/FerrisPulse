# ============================================================================
# 测试脚本: test_acer_support.ps1
# 目标: 验证 Acer（宏碁）OMR 系列 2.4G 无线鼠标适配：
#       XCTECH 接收器 249A:5C2F 白名单、0x10 设备信息查询帧、
#       R332 型号映射、电量/充电字段解析与 DeviceManager 接入。
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = Resolve-Path "$ScriptDir\.."
Set-Location $ProjectDir

function Assert-Bom([string]$path) {
    if (-not (Test-Path $path)) {
        Write-Host "FAILED: 文件不存在: $path" -ForegroundColor Red
        exit 1
    }
    $bytes = [System.IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 3 -or $bytes[0] -ne 0xEF -or $bytes[1] -ne 0xBB -or $bytes[2] -ne 0xBF) {
        Write-Host "FAILED: 文件不是 UTF-8 with BOM: $path" -ForegroundColor Red
        exit 1
    }
}

function Assert-CSharp5([string]$path) {
    $content = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    if ([regex]::IsMatch($content, '\$".*?"')) { Write-Host "FAILED: 字符串插值: $path" -ForegroundColor Red; exit 1 }
    if ([regex]::IsMatch($content, '\w+\?\.')) { Write-Host "FAILED: 空条件运算符: $path" -ForegroundColor Red; exit 1 }
    if ([regex]::IsMatch($content, 'nameof\s*\(')) { Write-Host "FAILED: nameof: $path" -ForegroundColor Red; exit 1 }
    if ([regex]::IsMatch($content, 'out\s+var\s+')) { Write-Host "FAILED: out var: $path" -ForegroundColor Red; exit 1 }
    if (-not $content.Contains("⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️")) {
        Write-Host "FAILED: 缺少 AI 维护准则声明: $path" -ForegroundColor Red; exit 1
    }
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 1/4] Acer 驱动文件 BOM / C# 5.0 规范" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$helperPath = "$ProjectDir\src\Hardware\AcerDeviceHelper.cs"
Assert-Bom $helperPath
Assert-CSharp5 $helperPath
Write-Host "PASSED: AcerDeviceHelper.cs 存在、BOM 正确且符合 C# 5.0 铁律" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 2/4] VID/PID 白名单与 0x10 查询帧" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$content = [System.IO.File]::ReadAllText($helperPath, [System.Text.Encoding]::UTF8)
foreach ($token in @("0x249A", "0x248A", "0x5C2F", "0x10", "R332")) {
    if (-not $content.Contains($token)) { Write-Host "FAILED: 缺少协议常量 $token" -ForegroundColor Red; exit 1 }
}

$binPath = "$ProjectDir\FerrisPulse.exe"
if (-not (Test-Path $binPath)) {
    Write-Host "编译二进制以供反射测试..." -ForegroundColor Yellow
    cmd /c "$ProjectDir\build.bat" | Out-Null
}
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($binPath))
$type = $asm.GetType("RazerBatteryTray.AcerDeviceHelper")
if ($null -eq $type) { Write-Host "FAILED: 未找到 AcerDeviceHelper" -ForegroundColor Red; exit 1 }

$buildFrame = $type.GetMethod("BuildInfoQueryFrame", [System.Reflection.BindingFlags]"Public,Static")
$frame = $buildFrame.Invoke($null, @())
if ($frame.Length -ne 33) { Write-Host "FAILED: 查询帧长度不是 33" -ForegroundColor Red; exit 1 }
if ($frame[1] -ne 0x10) { Write-Host "FAILED: 查询命令字不是 0x10" -ForegroundColor Red; exit 1 }
Write-Host "PASSED: 33 字节查询帧与 0x10 命令字正确" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 3/4] R332 设备信息/电量/充电解析" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 实机抓包（2026-10-22, Acer OMR331 旗舰版, dev_id R332, [15]=0x01 但设备并未充电）
$rxCharging = [byte[]]@(
    0x00,0x10,0x00,0x01,0x0B,0x52,0x33,0x33,0x32,0x85,0x01,
    0x19,0x01,0x00,0x4E,0x01,0x00,0x00,0x00,0x00,0x00,0x00,
    0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xD9
)
# 同一设备另一时刻（[15] 由 0x01 -> 0x00，校验和同步变为 0xD8；同样未充电）
$rxIdle = [byte[]]@(
    0x00,0x10,0x00,0x01,0x0B,0x52,0x33,0x33,0x32,0x85,0x01,
    0x19,0x01,0x00,0x4E,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
    0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xD8
)

$parse = $type.GetMethod("ParseInfoResponse", [System.Reflection.BindingFlags]"Public,Static")
$args = [object[]]@($rxCharging, 33, "", 0, $false, 0)
$ok = $parse.Invoke($null, $args)
if (-not $ok) { Write-Host "FAILED: 充电帧解析失败" -ForegroundColor Red; exit 1 }
if ($args[2] -ne "R332") { Write-Host ("FAILED: 设备 ID 错误: " + $args[2]) -ForegroundColor Red; exit 1 }
if ($args[3] -ne 78) { Write-Host ("FAILED: 电量错误: " + $args[3]) -ForegroundColor Red; exit 1 }
if ($args[4] -ne $false) { Write-Host "FAILED: [15]=0x01 时不得误报充电" -ForegroundColor Red; exit 1 }
if ($args[5] -ne 0x0185) { Write-Host ("FAILED: 固件版本错误: " + $args[5]) -ForegroundColor Red; exit 1 }

$args2 = [object[]]@($rxIdle, 33, "", 0, $true, 0)
$ok2 = $parse.Invoke($null, $args2)
if (-not $ok2) { Write-Host "FAILED: 放电帧解析失败" -ForegroundColor Red; exit 1 }
if ($args2[3] -ne 78) { Write-Host "FAILED: 放电帧电量错误" -ForegroundColor Red; exit 1 }
if ($args2[4] -ne $false) { Write-Host "FAILED: 未充电帧充电标志错误" -ForegroundColor Red; exit 1 }
Write-Host "PASSED: R332 电量 78%、固件 1.85 解析正确；[15] 漂移不再误报充电" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 4/4] 型号映射与 DeviceManager 接入" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$modelMethod = $type.GetMethod("GetModelNameFromDevId", [System.Reflection.BindingFlags]"Public,Static")
$model = $modelMethod.Invoke($null, @("R332"))
if ($model -notmatch "OMR331") { Write-Host ("FAILED: R332 型号映射错误: " + $model) -ForegroundColor Red; exit 1 }

$dmContent = [System.IO.File]::ReadAllText("$ProjectDir\src\Hardware\DeviceManager.cs", [System.Text.Encoding]::UTF8)
if (-not $dmContent.Contains("AcerDeviceHelper.QueryAllAcerDevices()")) {
    Write-Host "FAILED: DeviceManager 未接入 Acer 查询" -ForegroundColor Red; exit 1
}
Write-Host "PASSED: R332 -> OMR331 旗舰版，且已接入 DeviceManager" -ForegroundColor Green

Write-Host "`nALL ACER TESTS PASSED" -ForegroundColor Green
