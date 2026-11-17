# ============================================================================
# 测试脚本: test_corsair_m75air.ps1
# 目标: 验证海盗船 M75 AIR 无线鼠标 (SLIPSTREAM 1B1C:1BDC / 子设备 1BF2)：
#       65 字节 Bragi 协议、电量/供电/DPI/回报率解析、鼠标输入帧过滤、
#       DeviceManager / BuiltInDrivers / MainForm 生命周期接入。
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = Resolve-Path "$ScriptDir\.."
Set-Location $ProjectDir

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 1/6] CorsairDeviceHelper.Mouse.cs 存在性与 UTF-8 BOM 校验" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$helperPath = "$ProjectDir\src\Hardware\CorsairDeviceHelper.Mouse.cs"
if (-not (Test-Path $helperPath)) {
    Write-Host "FAILED: src\Hardware\CorsairDeviceHelper.Mouse.cs 尚未创建！" -ForegroundColor Red
    exit 1
}
$bytes = [System.IO.File]::ReadAllBytes($helperPath)
if ($bytes.Length -lt 3 -or $bytes[0] -ne 0xEF -or $bytes[1] -ne 0xBB -or $bytes[2] -ne 0xBF) {
    Write-Host "FAILED: CorsairDeviceHelper.Mouse.cs 不是 UTF-8 with BOM 编码！" -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: 文件存在且符合 UTF-8 with BOM 规范" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 2/6] C# 5.0 语法铁律审查" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$content = [System.IO.File]::ReadAllText($helperPath, [System.Text.Encoding]::UTF8)
if ([regex]::IsMatch($content, '\$".*?"')) { Write-Host "FAILED: 字符串插值" -ForegroundColor Red; exit 1 }
if ([regex]::IsMatch($content, '\w+\?\.')) { Write-Host "FAILED: 空条件运算符" -ForegroundColor Red; exit 1 }
if ([regex]::IsMatch($content, 'nameof\s*\(')) { Write-Host "FAILED: nameof" -ForegroundColor Red; exit 1 }
if ([regex]::IsMatch($content, 'out\s+var\s+')) { Write-Host "FAILED: out var" -ForegroundColor Red; exit 1 }
if (-not $content.Contains("⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️")) {
    Write-Host "FAILED: 缺少头部 AI 维护准则声明" -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: 严格遵循 C# 5.0 标准" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 3/6] M75 AIR VID/PID、型号与设备分类校验" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$binPath = "$ProjectDir\FerrisPulse.exe"
if (-not (Test-Path $binPath)) { cmd /c "$ProjectDir\build.bat" | Out-Null }
$asmBytes = [System.IO.File]::ReadAllBytes((Resolve-Path $binPath).Path)
$asm = [System.Reflection.Assembly]::Load($asmBytes)
$helperType = $asm.GetType("RazerBatteryTray.CorsairDeviceHelper")
if ($null -eq $helperType) { Write-Host "FAILED: 反射获取 CorsairDeviceHelper 失败" -ForegroundColor Red; exit 1 }

$isMouse = $helperType.GetMethod("IsSupportedMouseVidPid", [System.Reflection.BindingFlags]"Public,Static")
$m1 = $isMouse.Invoke($null, @(0x1B1C, 0x1BF2))
$m2 = $isMouse.Invoke($null, @(0x1B1C, 0x1BDC))
$m3 = $isMouse.Invoke($null, @(0x1B1C, 0x0A97))
$m4 = $isMouse.Invoke($null, @(0x1532, 0x1BF2))
if (-not $m1 -or -not $m2 -or $m3 -or $m4) {
    Write-Host "FAILED: IsSupportedMouseVidPid 异常 (1BF2:$m1 1BDC:$m2 0A97:$m3 1532:$m4)" -ForegroundColor Red
    exit 1
}

$getName = $helperType.GetMethod("GetMouseModelName", [System.Reflection.BindingFlags]"Public,Static")
$n1 = $getName.Invoke($null, @(0x1BF2, "CORSAIR M75 AIR WIRELESS"))
$n2 = $getName.Invoke($null, @(0x1BF2, ""))
if ($n1 -ne "M75 AIR Wireless" -or $n2 -ne "M75 AIR Wireless") {
    Write-Host "FAILED: GetMouseModelName 异常: $n1 / $n2" -ForegroundColor Red
    exit 1
}

$isId = $helperType.GetMethod("IsM75AirIdentifier", [System.Reflection.BindingFlags]"Public,Static")
$i1 = $isId.Invoke($null, @("HID:1B1C:1BF2"))
$i2 = $isId.Invoke($null, @("\\?\hid#vid_1b1c&pid_1bdc&mi_01"))
$i3 = $isId.Invoke($null, @("HID:1B1C:0A97"))
if (-not $i1 -or -not $i2 -or $i3) {
    Write-Host "FAILED: IsM75AirIdentifier 异常 ($i1/$i2/$i3)" -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: M75 AIR 标识与型号逻辑正确" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 4/6] 65 字节 Bragi 报文解析断言" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$parseValue = $helperType.GetMethod("TryParseMouseValue", [System.Reflection.BindingFlags]"Public,Static")
$parseBatt = $helperType.GetMethod("TryParseMouseBattery", [System.Reflection.BindingFlags]"Public,Static")
$parseChg = $helperType.GetMethod("TryParseMouseChargeState", [System.Reflection.BindingFlags]"Public,Static")
$parseDpi = $helperType.GetMethod("TryParseMouseDpi", [System.Reflection.BindingFlags]"Public,Static")
$parsePoll = $helperType.GetMethod("TryParseMousePolling", [System.Reflection.BindingFlags]"Public,Static")

function New-Frame([byte[]]$head) {
    $f = New-Object byte[] 65
    for ($i = 0; $i -lt $head.Length; $i++) { $f[$i] = $head[$i] }
    return ,$f
}

# PID: 00 01 02 00 F2 1B
[byte[]]$pidFrame = New-Frame ([byte[]](0x00,0x01,0x02,0x00,0xF2,0x1B))
$invokeArgs = [object[]]@($pidFrame, 65, [byte]0x02, [int]-1, [int]0)
$ok = $parseValue.Invoke($null, $invokeArgs)
if (-not $ok -or [int]$invokeArgs[3] -ne 0 -or [int]$invokeArgs[4] -ne 0x1BF2) {
    Write-Host "FAILED: PID 帧解析失败 ok=$ok err=$($invokeArgs[3]) val=$($invokeArgs[4])" -ForegroundColor Red
    exit 1
}

# 电量: 00 01 02 00 76 02 -> 630 -> 63.0%
[byte[]]$battFrame = New-Frame ([byte[]](0x00,0x01,0x02,0x00,0x76,0x02))
$invokeArgs = [object[]]@($battFrame, 65, [double]0)
$ok = $parseBatt.Invoke($null, $invokeArgs)
if (-not $ok -or [Math]::Abs([double]$invokeArgs[2] - 63.0) -gt 0.001) {
    Write-Host "FAILED: 电量帧解析失败 ok=$ok pct=$($invokeArgs[2])" -ForegroundColor Red
    exit 1
}

# 供电状态: 00 01 02 00 02
[byte[]]$chgFrame = New-Frame ([byte[]](0x00,0x01,0x02,0x00,0x02))
$invokeArgs = [object[]]@($chgFrame, 65, [int]-1)
$ok = $parseChg.Invoke($null, $invokeArgs)
if (-not $ok -or [int]$invokeArgs[2] -ne 2) {
    Write-Host "FAILED: 供电状态解析失败 ok=$ok state=$($invokeArgs[2])" -ForegroundColor Red
    exit 1
}

# DPI: 00 01 02 00 40 06 -> 1600
[byte[]]$dpiFrame = New-Frame ([byte[]](0x00,0x01,0x02,0x00,0x40,0x06))
$invokeArgs = [object[]]@($dpiFrame, 65, [int]0)
$ok = $parseDpi.Invoke($null, $invokeArgs)
if (-not $ok -or [int]$invokeArgs[2] -ne 1600) {
    Write-Host "FAILED: DPI 帧解析失败 ok=$ok dpi=$($invokeArgs[2])" -ForegroundColor Red
    exit 1
}

# 回报率: 00 01 02 00 04 -> 1000Hz
[byte[]]$pollFrame = New-Frame ([byte[]](0x00,0x01,0x02,0x00,0x04))
$invokeArgs = [object[]]@($pollFrame, 65, [int]0)
$ok = $parsePoll.Invoke($null, $invokeArgs)
if (-not $ok -or [int]$invokeArgs[2] -ne 1000) {
    Write-Host "FAILED: 回报率帧解析失败 ok=$ok hz=$($invokeArgs[2])" -ForegroundColor Red
    exit 1
}

# 错误帧必须拒绝: 00 01 02 05 ...
[byte[]]$errFrame = New-Frame ([byte[]](0x00,0x01,0x02,0x05,0x01,0x02))
$invokeArgs = [object[]]@($errFrame, 65, [double]0)
if ($parseBatt.Invoke($null, $invokeArgs)) { Write-Host "FAILED: 错误帧被接受" -ForegroundColor Red; exit 1 }

# 鼠标输入帧必须拒绝: 00 00 0D ...
[byte[]]$inputFrame = New-Frame ([byte[]](0x00,0x00,0x0D,0x00,0x00,0x02))
$invokeArgs = [object[]]@($inputFrame, 65, [double]0)
if ($parseBatt.Invoke($null, $invokeArgs)) { Write-Host "FAILED: 鼠标输入帧未被过滤" -ForegroundColor Red; exit 1 }

# 过短帧必须拒绝
$invokeArgs = [object[]]@([byte[]](New-Frame ([byte[]](0x00,0x01,0x02))), 3, [double]0)
if ($parseBatt.Invoke($null, $invokeArgs)) { Write-Host "FAILED: 过短帧未被拒绝" -ForegroundColor Red; exit 1 }

# 码表
$codeToHz = $helperType.GetMethod("PollCodeToHz", [System.Reflection.BindingFlags]"Public,Static")
$hzToCode = $helperType.GetMethod("HzToPollCode", [System.Reflection.BindingFlags]"Public,Static")
$pairs = @(@(1,125), @(2,250), @(3,500), @(4,1000))
foreach ($p in $pairs) {
    if ([int]$codeToHz.Invoke($null, @($p[0])) -ne $p[1]) { Write-Host "FAILED: PollCodeToHz $($p[0])" -ForegroundColor Red; exit 1 }
    if ([int]$hzToCode.Invoke($null, @($p[1])) -ne $p[0]) { Write-Host "FAILED: HzToPollCode $($p[1])" -ForegroundColor Red; exit 1 }
}
Write-Host "PASSED: 65 字节 Bragi 电量 / 状态 / DPI / 回报率 / 输入帧过滤全部正确" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 5/6] DeviceManager / BuiltInDrivers / MainForm 接入校验" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$dm = [System.IO.File]::ReadAllText("$ProjectDir\src\Hardware\DeviceManager.cs", [System.Text.Encoding]::UTF8)
if (-not $dm.Contains("CorsairDeviceHelper.QueryCorsairMouseInfo()")) { Write-Host "FAILED: DeviceManager 缺少鼠标查询" -ForegroundColor Red; exit 1 }
if (-not $dm.Contains("UnhideDevice(`"Corsair:1B1C:1BF2`")")) { Write-Host "FAILED: DeviceManager 缺少 1BF2 隐藏迁移" -ForegroundColor Red; exit 1 }

$bd = [System.IO.File]::ReadAllText("$ProjectDir\src\Hardware\BuiltInDrivers.cs", [System.Text.Encoding]::UTF8)
if (-not $bd.Contains("CorsairMouseDriver")) { Write-Host "FAILED: BuiltInDrivers 缺少 CorsairMouseDriver" -ForegroundColor Red; exit 1 }

$mf = [System.IO.File]::ReadAllText("$ProjectDir\src\UI\Forms\MainForm.cs", [System.Text.Encoding]::UTF8)
if (-not $mf.Contains("CorsairDeviceHelper.LoadMouseHardwareCache()")) { Write-Host "FAILED: MainForm 缺少鼠标缓存预加载" -ForegroundColor Red; exit 1 }

$ug = [System.IO.File]::ReadAllText("$ProjectDir\src\Hardware\UniversalGamingDeviceHelper.cs", [System.Text.Encoding]::UTF8)
if (-not $ug.Contains("pid_1bdc")) { Write-Host "FAILED: 缺少 SLIPSTREAM 1BDC 端点隔离" -ForegroundColor Red; exit 1 }
Write-Host "PASSED: M75 AIR 已接入设备管理器、驱动注册表与 UI 生命周期" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 6/6] 实机在线探测（无设备时自动跳过）" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$query = $helperType.GetMethod("QueryCorsairMouseInfo", [System.Reflection.BindingFlags]"Public,Static")
$live = $query.Invoke($null, @())
if ($null -eq $live) {
    Write-Host "SKIPPED: 当前未检测到 SLIPSTREAM 接收器" -ForegroundColor Yellow
}
else {
    if ($live.DeviceId -ne "Corsair:1B1C:1BF2") { Write-Host "FAILED: DeviceId=$($live.DeviceId)" -ForegroundColor Red; exit 1 }
    if ($live.Brand -ne "Corsair" -or $live.Category.ToString() -ne "Mouse") { Write-Host "FAILED: 品牌/分类异常" -ForegroundColor Red; exit 1 }
    if ($live.IsConnected) {
        if ($live.BatteryPercent -lt 0 -or $live.BatteryPercent -gt 100) { Write-Host "FAILED: 电量越界" -ForegroundColor Red; exit 1 }
        if ($live.Dpi -lt 100 -or $live.Dpi -gt 26000) { Write-Host "FAILED: DPI 越界 $($live.Dpi)" -ForegroundColor Red; exit 1 }
        Write-Host ("PASSED: 实机读取 -> " + $live.DeviceName + " / " + $live.BatteryPercent + "% / DPI " + $live.Dpi + "x" + $live.DpiY + " / " + $live.PollingRate + "Hz") -ForegroundColor Green
    }
    else {
        Write-Host ("PASSED: 接收器在线、鼠标休眠，缓存 " + $live.BatteryPercent + "% 正确回显") -ForegroundColor Green
    }
}

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " 海盗船 (Corsair) M75 AIR Wireless 鼠标测试全部通过！" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
