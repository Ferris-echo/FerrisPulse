# ============================================================================
# 测试脚本: test_flydigi_mchose_support.ps1
# 目标: 验证飞智 APEX 6 与迈从 G87 V2 驱动层规范、VID/PID 白名单、
#       GPA6 电量刻度解析、MCHOSE 0x4A/0x87 电池报文解析与 DeviceManager 接入
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
Write-Host " [测试 1/5] 新品牌驱动文件与 UTF-8 BOM / C# 5.0 规范" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$files = @(
    "$ProjectDir\src\Hardware\HidTransport.cs",
    "$ProjectDir\src\Hardware\FlydigiDeviceHelper.cs",
    "$ProjectDir\src\Hardware\MchoseDeviceHelper.cs"
)
foreach ($f in $files) {
    Assert-Bom $f
    Assert-CSharp5 $f
}
Write-Host "PASSED: 三个新增源文件均存在、BOM 正确且符合 C# 5.0 铁律" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 2/5] VID/PID 白名单与协议常量" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$flyContent = [System.IO.File]::ReadAllText("$ProjectDir\src\Hardware\FlydigiDeviceHelper.cs", [System.Text.Encoding]::UTF8)
$mchContent = [System.IO.File]::ReadAllText("$ProjectDir\src\Hardware\MchoseDeviceHelper.cs", [System.Text.Encoding]::UTF8)

foreach ($token in @("0x37D7", "0x2502", "0x2401", "0x5A", "0xA5")) {
    if (-not $flyContent.Contains($token)) { Write-Host "FAILED: Flydigi 缺少协议常量 $token" -ForegroundColor Red; exit 1 }
}
foreach ($token in @("0x3837", "0x3033", "0x4A", "0x13", "0x87")) {
    if (-not $mchContent.Contains($token)) { Write-Host "FAILED: MCHOSE 缺少协议常量 $token" -ForegroundColor Red; exit 1 }
}
Write-Host "PASSED: 飞智 37D7:2502/2401 与迈从 3837:3033 协议常量完整" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 3/5] GPA6 命令 0x01 电量/充电解析断言" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$binPath = "$ProjectDir\FerrisPulse.exe"
if (-not (Test-Path $binPath)) {
    Write-Host "编译二进制以供反射测试..." -ForegroundColor Yellow
    cmd /c "$ProjectDir\build.bat" | Out-Null
}
$asmBytes = [System.IO.File]::ReadAllBytes($binPath)
$asm = [System.Reflection.Assembly]::Load($asmBytes)
$flyType = $asm.GetType("RazerBatteryTray.FlydigiDeviceHelper")
if ($null -eq $flyType) { Write-Host "FAILED: 未找到 FlydigiDeviceHelper" -ForegroundColor Red; exit 1 }

$isSupported = $flyType.GetMethod("IsSupportedVidPid", [System.Reflection.BindingFlags]"Public,Static")
$r1 = $isSupported.Invoke($null, @(0x37D7, 0x2502))
$r2 = $isSupported.Invoke($null, @(0x37D7, 0x2401))
$r3 = $isSupported.Invoke($null, @(0x3837, 0x3033))
if (-not $r1 -or -not $r2 -or $r3) {
    Write-Host "FAILED: Flydigi VID/PID 白名单异常 ($r1/$r2/$r3)" -ForegroundColor Red; exit 1
}

$parseInfo = $flyType.GetMethod("ParseInfo", [System.Reflection.BindingFlags]"Public,Static")
function Invoke-ParseInfo([int]$raw) {
    [byte[]]$buf = New-Object byte[] 33
    $buf[0] = 0x5A; $buf[1] = 0xA5; $buf[2] = 0x01; $buf[3] = 0x01; $buf[4] = 0x00
    $buf[5] = 150; $buf[6] = 1; $buf[11] = [byte]$raw
    [object[]]$args = New-Object object[] 6
    $args[0] = $buf
    $args[1] = 33
    $args[2] = 0
    $args[3] = $false
    $args[4] = 0
    $args[5] = 0
    $ok = $parseInfo.Invoke($null, $args)
    return @{ Ok = $ok; Battery = [int]$args[2]; Charging = [bool]$args[3]; DeviceType = [int]$args[4] }
}

$p1 = Invoke-ParseInfo 0x1A   # 低 4 位 10 -> 100%，高 4 位 1 -> 充电中
$p2 = Invoke-ParseInfo 0x03   # 低 4 位 3 -> 60%，未充电
$p3 = Invoke-ParseInfo 0x09   # 低 4 位 9 -> 90%
if (-not $p1.Ok -or $p1.Battery -ne 100 -or -not $p1.Charging -or $p1.DeviceType -ne 150) {
    Write-Host "FAILED: APEX 6 满电/充电解析异常 (batt=$($p1.Battery), chg=$($p1.Charging), type=$($p1.DeviceType))" -ForegroundColor Red; exit 1
}
if (-not $p2.Ok -or $p2.Battery -ne 60 -or $p2.Charging) {
    Write-Host "FAILED: APEX 6 60% 解析异常 (batt=$($p2.Battery), chg=$($p2.Charging))" -ForegroundColor Red; exit 1
}
if (-not $p3.Ok -or $p3.Battery -ne 90 -or $p3.Charging) {
    Write-Host "FAILED: APEX 6 90% 解析异常 (batt=$($p3.Battery), chg=$($p3.Charging))" -ForegroundColor Red; exit 1
}
Write-Host "PASSED: 飞智 APEX 6 的 0..10 档电量、充电标志与型号解析全部正确" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 4/5] MCHOSE 0x4A 无线 / 0x87 有线电池报文解析断言" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$mchType = $asm.GetType("RazerBatteryTray.MchoseDeviceHelper")
if ($null -eq $mchType) { Write-Host "FAILED: 未找到 MchoseDeviceHelper" -ForegroundColor Red; exit 1 }
$isMch = $mchType.GetMethod("IsSupportedVidPid", [System.Reflection.BindingFlags]"Public,Static")
$m1 = $isMch.Invoke($null, @(0x3837, 0x3033))
$m2 = $isMch.Invoke($null, @(0x3837, 0x2015))
if (-not $m1 -or $m2) { Write-Host "FAILED: MCHOSE VID/PID 白名单异常 ($m1/$m2)" -ForegroundColor Red; exit 1 }

$parseBatt = $mchType.GetMethod("ParseBatteryResponse", [System.Reflection.BindingFlags]"Public,Static")
function Invoke-ParseBatt([byte]$cmd, [int]$battery, [int]$bits) {
    [byte[]]$buf = New-Object byte[] 65
    if ($cmd -eq 0x87) {
        $buf[0] = 0x09; $buf[1] = $cmd; $buf[2] = 1; $buf[3] = 0; $buf[4] = 0
        $buf[11] = [byte]$battery; $buf[12] = [byte]$bits
    } else {
        $buf[0] = 0x13; $buf[1] = $cmd; $buf[2] = 1; $buf[3] = 0; $buf[4] = 0
        $buf[5] = [byte]$battery; $buf[6] = [byte]$bits
    }
    [object[]]$args = New-Object object[] 5
    $args[0] = $buf
    $args[1] = 65
    $args[2] = 0
    $args[3] = $false
    $args[4] = $false
    $ok = $parseBatt.Invoke($null, $args)
    return @{ Ok = $ok; Battery = [int]$args[2]; Charging = [bool]$args[3]; Full = [bool]$args[4] }
}

$q1 = Invoke-ParseBatt 0x4A 87 0x11
$q2 = Invoke-ParseBatt 0x4A 42 0x00
$q3 = Invoke-ParseBatt 0x87 76 0x10
if (-not $q1.Ok -or $q1.Battery -ne 87 -or -not $q1.Charging) {
    Write-Host "FAILED: MCHOSE 无线 87%/充电解析异常 (ok=$($q1.Ok), batt=$($q1.Battery), chg=$($q1.Charging))" -ForegroundColor Red; exit 1
}
if (-not $q2.Ok -or $q2.Battery -ne 42 -or $q2.Charging) {
    Write-Host "FAILED: MCHOSE 无线 42%/未充电解析异常 (ok=$($q2.Ok), batt=$($q2.Battery), chg=$($q2.Charging))" -ForegroundColor Red; exit 1
}
if (-not $q3.Ok -or $q3.Battery -ne 76 -or -not $q3.Charging) {
    Write-Host "FAILED: MCHOSE 有线 76%/充电解析异常 (ok=$($q3.Ok), batt=$($q3.Battery), chg=$($q3.Charging))" -ForegroundColor Red; exit 1
}
$buildPerf = $mchType.GetMethod("BuildPerformanceFrame", [System.Reflection.BindingFlags]"Public,Static")
$pf = $buildPerf.Invoke($null, @())
if ($pf[0] -ne 0x55 -or $pf[1] -ne 0x05 -or $pf[3] -ne 0x38 -or $pf[4] -ne 0x38) {
    Write-Host "FAILED: MCHOSE 性能查询帧头异常" -ForegroundColor Red; exit 1
}

$parsePerf = $mchType.GetMethod("ParsePerformanceResponse", [System.Reflection.BindingFlags]"Public,Static")
# [40] 是电量与充电的复合字节：bit7 = 充电标志，低 7 位 = 百分比。
# 实机插着 USB 线充电时该字节为 0xC3（67% + 充电），若把整字节当百分比会超出 0-100
# 而导致整帧被判无效，电量与充电状态同时丢失 —— 这正是 K99 V3 有线形态读不到电量的根因。
function Invoke-ParsePerf([int]$battery, [bool]$charging) {
    [byte[]]$buf = New-Object byte[] 64
    $buf[0] = 0xAA; $buf[1] = 0x05
    $combined = $battery
    if ($charging) { $combined = $battery -bor 0x80 }
    $buf[40] = [byte]$combined
    [object[]]$args = New-Object object[] 4
    $args[0] = $buf
    $args[1] = 64
    $args[2] = 0
    $args[3] = $false
    $ok = $parsePerf.Invoke($null, $args)
    return @{ Ok = $ok; Battery = [int]$args[2]; Charging = [bool]$args[3] }
}
$p1 = Invoke-ParsePerf 21 $false
$p2 = Invoke-ParsePerf 88 $true
$p3 = Invoke-ParsePerf 67 $true
if (-not $p1.Ok -or $p1.Battery -ne 21 -or $p1.Charging) {
    Write-Host "FAILED: MCHOSE 性能帧 21%/未充电解析异常" -ForegroundColor Red; exit 1
}
if (-not $p2.Ok -or $p2.Battery -ne 88 -or -not $p2.Charging) {
    Write-Host "FAILED: MCHOSE 性能帧 88%/充电解析异常（bit7 充电标志未生效）" -ForegroundColor Red; exit 1
}
if (-not $p3.Ok -or $p3.Battery -ne 67 -or -not $p3.Charging) {
    Write-Host "FAILED: MCHOSE 性能帧 0xC3(67%+充电) 解析异常" -ForegroundColor Red; exit 1
}

# 传输形态字节：0x01 = USB 有线，0x02 = 2.4G，其余值必须判为未知
$parseTransport = $mchType.GetMethod("TryParsePerformanceTransport", [System.Reflection.BindingFlags]"Public,Static")
function Invoke-ParseTransport([int]$transportByte) {
    [byte[]]$buf = New-Object byte[] 64
    $buf[0] = 0xAA; $buf[1] = 0x05
    $buf[40] = 0x43
    $buf[42] = [byte]$transportByte
    [object[]]$args = New-Object object[] 3
    $args[0] = $buf
    $args[1] = 64
    $args[2] = $false
    $ok = $parseTransport.Invoke($null, $args)
    return @{ Ok = $ok; Wired = [bool]$args[2] }
}
$t1 = Invoke-ParseTransport 0x01
$t2 = Invoke-ParseTransport 0x02
$t3 = Invoke-ParseTransport 0x00
$t4 = Invoke-ParseTransport 0x03
if (-not $t1.Ok -or -not $t1.Wired) {
    Write-Host "FAILED: 形态字节 0x01 未判为 USB 有线" -ForegroundColor Red; exit 1
}
if (-not $t2.Ok -or $t2.Wired) {
    Write-Host "FAILED: 形态字节 0x02 未判为 2.4G" -ForegroundColor Red; exit 1
}
if ($t3.Ok -or $t4.Ok) {
    Write-Host "FAILED: 未定义的形态字节（0x00/0x03）不应被当作已知形态" -ForegroundColor Red; exit 1
}
Write-Host "PASSED: MCHOSE G87 V2 官方 0x55 性能帧（bit7充电+低7位电量、[42]形态位）与 0x4A/0x87 合成报文解析正确" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 5/5] DeviceManager 统一注册与通用鼠标协议隔离" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$dmContent = [System.IO.File]::ReadAllText("$ProjectDir\src\Hardware\DeviceManager.cs", [System.Text.Encoding]::UTF8)
if (-not $dmContent.Contains("FlydigiDeviceHelper.QueryAllFlydigiDevices()")) {
    Write-Host "FAILED: DeviceManager 缺少 Flydigi 查询" -ForegroundColor Red; exit 1
}
if (-not $dmContent.Contains("MchoseDeviceHelper.QueryAllMchoseDevices()")) {
    Write-Host "FAILED: DeviceManager 缺少 MCHOSE 查询" -ForegroundColor Red; exit 1
}

$ugContent = [System.IO.File]::ReadAllText("$ProjectDir\src\Hardware\UniversalGamingDeviceHelper.cs", [System.Text.Encoding]::UTF8)
if (-not $ugContent.Contains("pid_3033")) {
    Write-Host "FAILED: 通用电竞鼠标协议未隔离 MCHOSE G87 V2" -ForegroundColor Red; exit 1
}

Write-Host "PASSED: 两大新品牌已统一注册，且键盘不会被通用鼠标协议误探测" -ForegroundColor Green
Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " 飞智 APEX 6 + 迈从 G87 V2 实机适配与合成报文测试全部通过！" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
exit 0
