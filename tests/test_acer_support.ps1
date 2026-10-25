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
$rxLegacyStateA = [byte[]]@(
    0x00,0x10,0x00,0x01,0x0B,0x52,0x33,0x33,0x32,0x85,0x01,
    0x19,0x01,0x00,0x4E,0x01,0x00,0x00,0x00,0x00,0x00,0x00,
    0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xD9
)
# 同一设备另一时刻（[15] 由 0x01 -> 0x00，校验和同步变为 0xD8；同样未充电）
$rxLegacyStateB = [byte[]]@(
    0x00,0x10,0x00,0x01,0x0B,0x52,0x33,0x33,0x32,0x85,0x01,
    0x19,0x01,0x00,0x4E,0x00,0x00,0x00,0x00,0x00,0x00,0x00,
    0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xD8
)
# 2026-10-25 插线充电中（100%，[13]=0x01，[32] 校验和 0xF0）——连续 8 帧一致
$rxCharging = [byte[]]@(
    0x00,0x10,0x00,0x01,0x0B,0x52,0x33,0x33,0x32,0x85,0x01,
    0x19,0x01,0x01,0x64,0x01,0x00,0x00,0x00,0x00,0x00,0x00,
    0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xF0
)
# 2026-10-25 拔线后（58%，[13]=0x00，[32] 校验和 0xC5）——连续 8 帧一致
$rxUnplugged = [byte[]]@(
    0x00,0x10,0x00,0x01,0x0B,0x52,0x33,0x33,0x32,0x85,0x01,
    0x19,0x01,0x00,0x3A,0x01,0x00,0x00,0x00,0x00,0x00,0x00,
    0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0xC5
)

$parse = $type.GetMethod("ParseInfoResponse", [System.Reflection.BindingFlags]"Public,Static")

# 断言 1: 历史帧（[13]=0x00, [15]=0x01）必须仍然判定为未充电
$args = [object[]]@($rxLegacyStateA, 33, "", 0, $true, 0)
$ok = $parse.Invoke($null, $args)
if (-not $ok) { Write-Host "FAILED: 历史帧解析失败" -ForegroundColor Red; exit 1 }
if ($args[2] -ne "R332") { Write-Host ("FAILED: 设备 ID 错误: " + $args[2]) -ForegroundColor Red; exit 1 }
if ($args[3] -ne 78) { Write-Host ("FAILED: 电量错误: " + $args[3]) -ForegroundColor Red; exit 1 }
if ($args[4] -ne $false) { Write-Host "FAILED: [15]=0x01 时不得误报充电" -ForegroundColor Red; exit 1 }
if ($args[5] -ne 0x0185) { Write-Host ("FAILED: 固件版本错误: " + $args[5]) -ForegroundColor Red; exit 1 }

$args2 = [object[]]@($rxLegacyStateB, 33, "", 0, $true, 0)
$ok2 = $parse.Invoke($null, $args2)
if (-not $ok2) { Write-Host "FAILED: 历史帧 B 解析失败" -ForegroundColor Red; exit 1 }
if ($args2[3] -ne 78) { Write-Host "FAILED: 历史帧 B 电量错误" -ForegroundColor Red; exit 1 }
if ($args2[4] -ne $false) { Write-Host "FAILED: 历史帧 B 充电标志错误" -ForegroundColor Red; exit 1 }

# 断言 2: 插线充电帧（[13]=0x01, 100%）必须判定为充电中
$args3 = [object[]]@($rxCharging, 33, "", 0, $false, 0)
$ok3 = $parse.Invoke($null, $args3)
if (-not $ok3) { Write-Host "FAILED: 充电帧解析失败" -ForegroundColor Red; exit 1 }
if ($args3[2] -ne "R332") { Write-Host ("FAILED: 充电帧设备 ID 错误: " + $args3[2]) -ForegroundColor Red; exit 1 }
if ($args3[3] -ne 100) { Write-Host ("FAILED: 充电帧电量错误: " + $args3[3]) -ForegroundColor Red; exit 1 }
if ($args3[4] -ne $true) { Write-Host "FAILED: [13]=0x01 时必须判定为充电中" -ForegroundColor Red; exit 1 }

# 断言 3: 拔线帧（[13]=0x00, 58%）必须判定为未充电
$args4 = [object[]]@($rxUnplugged, 33, "", 0, $true, 0)
$ok4 = $parse.Invoke($null, $args4)
if (-not $ok4) { Write-Host "FAILED: 拔线帧解析失败" -ForegroundColor Red; exit 1 }
if ($args4[3] -ne 58) { Write-Host ("FAILED: 拔线帧电量错误: " + $args4[3]) -ForegroundColor Red; exit 1 }
if ($args4[4] -ne $false) { Write-Host "FAILED: 拔线后必须判定为未充电" -ForegroundColor Red; exit 1 }
Write-Host "PASSED: [13] 充电标志 0x01/0x00 判定正确；[15] 状态位不再误报；电量 100%/58%/78% 解析正确" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 4/5] 型号映射与 DeviceManager 接入" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$modelMethod = $type.GetMethod("GetModelNameFromDevId", [System.Reflection.BindingFlags]"Public,Static")
$model = $modelMethod.Invoke($null, @("R332"))
if ($model -notmatch "OMR331") { Write-Host ("FAILED: R332 型号映射错误: " + $model) -ForegroundColor Red; exit 1 }

$dmContent = [System.IO.File]::ReadAllText("$ProjectDir\src\Hardware\DeviceManager.cs", [System.Text.Encoding]::UTF8)
if (-not $dmContent.Contains("AcerDeviceHelper.QueryAllAcerDevices()")) {
    Write-Host "FAILED: DeviceManager 未接入 Acer 查询" -ForegroundColor Red; exit 1
}
Write-Host "PASSED: R332 -> OMR331 旗舰版，且已接入 DeviceManager" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 5/6] 状态栏供电文案跟随充电状态（防自相矛盾）" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 顶部卡片显示「充电中」、状态栏却写死「电池供电」会造成同屏自相矛盾
# （2026-10-25 Acer OMR331 插线时实测出现），此处锁死该文案不得写死。
$hwContent = [System.IO.File]::ReadAllText("$ProjectDir\src\UI\Forms\MainForm.Hardware.cs", [System.Text.Encoding]::UTF8)
if ($hwContent.Contains("| 供电: 电池供电")) {
    Write-Host "FAILED: 状态栏供电文案仍被写死为「电池供电」" -ForegroundColor Red; exit 1
}
if (-not $hwContent.Contains("供电: {1}")) {
    Write-Host "FAILED: 状态栏供电文案未改为动态占位" -ForegroundColor Red; exit 1
}
if (-not $hwContent.Contains('info.IsCharging ? "正在充电" : "电池供电"')) {
    Write-Host "FAILED: 状态栏供电文案未跟随 info.IsCharging" -ForegroundColor Red; exit 1
}
Write-Host "PASSED: 状态栏供电文案由 info.IsCharging 驱动，充电时显示「正在充电」" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 6/6] 充电哨兵值不得当作真实电量展示" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 设备在外部供电时把电量字段填 100 当哨兵（实测：拔线 58%，插线瞬间 100%），
# 展示时必须退回最近一次非充电实测值，否则用户看到“一插线就满电”。
$resolve = $type.GetMethod("ResolveDisplayedBattery", [System.Reflection.BindingFlags]"Public,Static")
if ($null -eq $resolve) { Write-Host "FAILED: 缺少 ResolveDisplayedBattery" -ForegroundColor Red; exit 1 }

$r1 = $resolve.Invoke($null, @([int]100, [bool]$true, [int]58))
if ($r1 -ne 58) { Write-Host ("FAILED: 充电中哨兵 100 未回退到实测值，得到 " + $r1) -ForegroundColor Red; exit 1 }

$r2 = $resolve.Invoke($null, @([int]100, [bool]$false, [int]58))
if ($r2 -ne 100) { Write-Host ("FAILED: 未充电时必须保留设备上报值，得到 " + $r2) -ForegroundColor Red; exit 1 }

$r3 = $resolve.Invoke($null, @([int]100, [bool]$true, [int]-1))
if ($r3 -ne 100) { Write-Host ("FAILED: 无历史实测值时应原样返回，得到 " + $r3) -ForegroundColor Red; exit 1 }

$r4 = $resolve.Invoke($null, @([int]58, [bool]$true, [int]58))
if ($r4 -ne 58) { Write-Host ("FAILED: 充电中非哨兵电量应原样返回，得到 " + $r4) -ForegroundColor Red; exit 1 }
Write-Host "PASSED: 充电哨兵 100 回退到实测 58；未充电/非哨兵/无缓存三种情况均正确" -ForegroundColor Green

Write-Host "`nALL ACER TESTS PASSED" -ForegroundColor Green
