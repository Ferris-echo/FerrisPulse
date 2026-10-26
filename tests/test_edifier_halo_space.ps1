# ============================================================================
# 测试脚本: test_edifier_halo_space.ps1
# 目标: 验证漫步者 Halo Space (2D99:E049) EC 私有协议驱动层契约、
#       BOM / C# 5.0 规范、64 字节报文建帧、XOR 0xA5 载荷掩码、
#       累加和校验（自下标 1 起算）、真实抓包电量/名称/版本/MAC 解析与容灾
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

function Invoke-WithPayload($method, $payload) {
    # PowerShell 会把单元素数组展开为标量, 必须用逗号运算符包住 byte[] 再转型
    return $method.Invoke($null, [object[]](,$payload))
}

function Get-Sum8([byte[]]$buf, [int]$offset, [int]$count) {
    $sum = 0
    for ($i = 0; $i -lt $count; $i++) { $sum += $buf[$offset + $i] }
    return ($sum -band 0xFF)
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 1/5] EdifierDeviceHelper 源码 BOM 与 C# 5.0 规范核验" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$helperPath = "$ProjectDir\src\Hardware\EdifierDeviceHelper.cs"
Assert-Bom $helperPath
Assert-CSharp5 $helperPath

$infoPath = "$ProjectDir\src\Models\MouseBatteryInfo.cs"
Assert-Bom $infoPath
$infoContent = [System.IO.File]::ReadAllText($infoPath, [System.Text.Encoding]::UTF8)
if (-not $infoContent.Contains('Brand == "Edifier"')) {
    Write-Host "FAILED: MouseBatteryInfo.BrandColor 缺少 Edifier 品牌色分支" -ForegroundColor Red; exit 1
}

$mgrPath = "$ProjectDir\src\Hardware\DeviceManager.cs"
Assert-Bom $mgrPath
$mgrContent = [System.IO.File]::ReadAllText($mgrPath, [System.Text.Encoding]::UTF8)
if (-not $mgrContent.Contains("EdifierDeviceHelper.QueryAllEdifierDevices")) {
    Write-Host "FAILED: DeviceManager 未挂载 EdifierDeviceHelper" -ForegroundColor Red; exit 1
}
Write-Host "PASSED: 驱动/BOM/C#5 规范、品牌色与 DeviceManager 挂载全部就位" -ForegroundColor Green

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 2/5] VID:PID 白名单与 EC 协议常量核验" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$helperContent = [System.IO.File]::ReadAllText($helperPath, [System.Text.Encoding]::UTF8)
foreach ($token in @("0x2D99", "0xE049", "0xFF14", "0x0001", "0x2E", "0xAA", "0x2F", "0xBB", "0xEC", "0xA5", "0xD0", "0xC9", "0xC6", "0xC8")) {
    if (-not $helperContent.Contains($token)) {
        Write-Host "FAILED: EdifierDeviceHelper 缺少协议常量 $token" -ForegroundColor Red
        exit 1
    }
}
Write-Host "PASSED: 漫步者 2D99:E049、UsagePage 0xFF14、帧头 2E/AA、掩码 0xA5 常量完整" -ForegroundColor Green

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 3/5] 64 字节查询报文建帧与累加和校验断言" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$binPath = "$ProjectDir\FerrisPulse.exe"
if (-not (Test-Path $binPath)) {
    Write-Host "编译二进制以供反射测试..." -ForegroundColor Yellow
    cmd /c "$ProjectDir\build.bat" | Out-Null
}
$asmBytes = [System.IO.File]::ReadAllBytes($binPath)
$asm = [System.Reflection.Assembly]::Load($asmBytes)
$edType = $asm.GetType("RazerBatteryTray.EdifierDeviceHelper")
if ($null -eq $edType) { Write-Host "FAILED: 未找到 EdifierDeviceHelper" -ForegroundColor Red; exit 1 }

$buildFrame = $edType.GetMethod("BuildEcFrame", [System.Reflection.BindingFlags]"Public,Static")
[byte[]]$frame = $buildFrame.Invoke($null, @([byte]0xD0, $null))

if ($frame.Length -ne 64) { Write-Host "FAILED: 查询帧长度非 64 ($($frame.Length))" -ForegroundColor Red; exit 1 }
if ($frame[0] -ne 0x2E -or $frame[1] -ne 0xAA -or $frame[2] -ne 0xEC -or $frame[3] -ne 0xD0) {
    Write-Host "FAILED: 帧头/设备码/命令索引异常" -ForegroundColor Red; exit 1
}
if ($frame[4] -ne 0x00 -or $frame[5] -ne 0x00) {
    Write-Host "FAILED: 长度字段异常" -ForegroundColor Red; exit 1
}
if ($frame[6] -ne 0x66) {
    Write-Host "FAILED: 校验和错误 (期望 0x66, 实际 0x$($frame[6].ToString('X2')))" -ForegroundColor Red; exit 1
}
for ($i = 7; $i -lt 64; $i++) {
    if ($frame[$i] -ne 0x00) { Write-Host "FAILED: 填充区非零 (index $i)" -ForegroundColor Red; exit 1 }
}
Write-Host "PASSED: 0xD0 电量查询帧 = 2E AA EC D0 00 00 66 + 57x00, 与实机抓包逐字节一致" -ForegroundColor Green

[byte[]]$emptyPayload = New-Object byte[] 0
[byte[]]$frameEmpty = $buildFrame.Invoke($null, @([byte]0xC9, $emptyPayload))
if ($frameEmpty[3] -ne 0xC9 -or $frameEmpty[6] -ne (Get-Sum8 $frameEmpty 1 5)) {
    Write-Host "FAILED: 0xC9 建帧校验和异常" -ForegroundColor Red; exit 1
}
Write-Host "PASSED: 0xC9 建帧校验和自洽 (自下标 1 起算的累加和)" -ForegroundColor Green

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 4/5] 真实抓包解析: 电量/名称/固件/MAC + XOR 0xA5 解掩码" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$parseReport = $edType.GetMethod("TryParseEcReport", [System.Reflection.BindingFlags]"Public,Static")
$parseBatt = $edType.GetMethod("ParseBatteryPayload", [System.Reflection.BindingFlags]"Public,Static")
$parseName = $edType.GetMethod("ParseNamePayload", [System.Reflection.BindingFlags]"Public,Static")
$parseVer = $edType.GetMethod("ParseVersionPayload", [System.Reflection.BindingFlags]"Public,Static")
$parseMac = $edType.GetMethod("ParseMacPayload", [System.Reflection.BindingFlags]"Public,Static")
$tipM = $edType.GetMethod("BuildTooltip", [System.Reflection.BindingFlags]"Public,Static")

[byte[]]$rxBatt = New-Object byte[] 64
$rxBatt[0]=0x2F; $rxBatt[1]=0xBB; $rxBatt[2]=0xEC; $rxBatt[3]=0xD0
$rxBatt[4]=0x00; $rxBatt[5]=0x01; $rxBatt[6]=0x86; $rxBatt[7]=0xFE
$rxBatt[8]=0x5A; $rxBatt[9]=0xA3

[object[]]$a1 = @($rxBatt, [byte]0xD0, $null)
$ok1 = $parseReport.Invoke($null, $a1)
if (-not $ok1) { Write-Host "FAILED: 电量应答解析失败" -ForegroundColor Red; exit 1 }
$batt = Invoke-WithPayload $parseBatt $a1[2]
if ($batt -ne 35) { Write-Host "FAILED: 电量期望 35, 实际 $batt" -ForegroundColor Red; exit 1 }
Write-Host "PASSED: 案例 1 电量 0x86 ^ 0xA5 = 35%" -ForegroundColor Green

[byte[]]$rxName = New-Object byte[] 64
$nameMasked = @(0x4D,0x2F,0x14,0x40,0x23,0x28,0x85,0xED,0xC4,0xC9,0xCA,0x85,0xF6,0xD5,0xC4,0xC6,0xC0)
$rxName[0]=0x2F; $rxName[1]=0xBB; $rxName[2]=0xEC; $rxName[3]=0xC9
$rxName[4]=0x00; $rxName[5]=0x11
for ($i = 0; $i -lt 17; $i++) { $rxName[6 + $i] = $nameMasked[$i] }
$rxName[23] = Get-Sum8 $rxName 1 22

[object[]]$a2 = @($rxName, [byte]0xC9, $null)
$ok2 = $parseReport.Invoke($null, $a2)
if (-not $ok2) { Write-Host "FAILED: 名称应答解析失败" -ForegroundColor Red; exit 1 }
$name = Invoke-WithPayload $parseName $a2[2]
if ($name -ne "花再 Halo Space") { Write-Host "FAILED: 名称解析异常: $name" -ForegroundColor Red; exit 1 }
Write-Host "PASSED: 案例 2 设备名称解出 UTF-8 花再 Halo Space" -ForegroundColor Green

[byte[]]$rxVer = New-Object byte[] 64
$rxVer[0]=0x2F; $rxVer[1]=0xBB; $rxVer[2]=0xEC; $rxVer[3]=0xC6
$rxVer[4]=0x00; $rxVer[5]=0x03; $rxVer[6]=0xA4; $rxVer[7]=0xA5; $rxVer[8]=0xA7
$rxVer[9] = Get-Sum8 $rxVer 1 8
[object[]]$a3 = @($rxVer, [byte]0xC6, $null)
$ok3 = $parseReport.Invoke($null, $a3)
if (-not $ok3) { Write-Host "FAILED: 固件应答解析失败" -ForegroundColor Red; exit 1 }
$ver = Invoke-WithPayload $parseVer $a3[2]
if ($ver -ne "1.0.2") { Write-Host "FAILED: 固件期望 1.0.2, 实际 $ver" -ForegroundColor Red; exit 1 }
Write-Host "PASSED: 案例 3 固件版本解出 1.0.2" -ForegroundColor Green

[byte[]]$rxMac = New-Object byte[] 64
$rxMac[0]=0x2F; $rxMac[1]=0xBB; $rxMac[2]=0xEC; $rxMac[3]=0xC8
$rxMac[4]=0x00; $rxMac[5]=0x06
# 线上字节为掩码态 (11 42 16 94 F7 76), 解掩码后才是 B4:E7:B3:31:52:D3
$rxMac[6]=0x11; $rxMac[7]=0x42; $rxMac[8]=0x16; $rxMac[9]=0x94; $rxMac[10]=0xF7; $rxMac[11]=0x76
$rxMac[12] = Get-Sum8 $rxMac 1 11
[object[]]$a4 = @($rxMac, [byte]0xC8, $null)
$ok4 = $parseReport.Invoke($null, $a4)
if (-not $ok4) { Write-Host "FAILED: MAC 应答解析失败" -ForegroundColor Red; exit 1 }
$mac = Invoke-WithPayload $parseMac $a4[2]
if ($mac -ne "B4:E7:B3:31:52:D3") { Write-Host "FAILED: MAC 期望 B4:E7:B3:31:52:D3, 实际 $mac" -ForegroundColor Red; exit 1 }
Write-Host "PASSED: 案例 4 MAC 解出 B4:E7:B3:31:52:D3" -ForegroundColor Green

$stateType = $asm.GetType("RazerBatteryTray.EdifierEcState")
$st = [System.Activator]::CreateInstance($stateType)
$st.BatteryPercent = 35
$st.DeviceName = $name
$st.FirmwareVersion = $ver
$tip = $tipM.Invoke($null, @($st))
if ($tip -ne "花再 Halo Space · 电量 35% · 固件 1.0.2") {
    Write-Host "FAILED: Tooltip 不符: $tip" -ForegroundColor Red; exit 1
}
Write-Host "PASSED: 案例 5 悬浮提示组装正确" -ForegroundColor Green

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 5/5] 校验和篡改 / 索引不匹配 / 非法首字节容灾" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

[byte[]]$badCrc = New-Object byte[] 64
[System.Array]::Copy($rxBatt, $badCrc, 64)
$badCrc[7] = 0x00
$okBad = $parseReport.Invoke($null, @($badCrc, [byte]0xD0, $null))
if ($okBad) { Write-Host "FAILED: 篡改校验和后仍被判定合法" -ForegroundColor Red; exit 1 }

$okIdx = $parseReport.Invoke($null, @($rxBatt, [byte]0xC9, $null))
if ($okIdx) { Write-Host "FAILED: 命令索引不匹配仍被接受" -ForegroundColor Red; exit 1 }

[byte[]]$garbage = New-Object byte[] 64
$garbage[0] = 0x12
$okGarbage = $parseReport.Invoke($null, @($garbage, [byte]0xD0, $null))
if ($okGarbage) { Write-Host "FAILED: 非法首字节报文被接受" -ForegroundColor Red; exit 1 }

$okNull = $parseReport.Invoke($null, @($null, [byte]0xD0, $null))
if ($okNull) { Write-Host "FAILED: null 报文被接受" -ForegroundColor Red; exit 1 }

$battBad = Invoke-WithPayload $parseBatt ([byte[]]@(0xFF))
if ($battBad -ne -1) { Write-Host "FAILED: 超范围电量未被判为未知, 实际 $battBad" -ForegroundColor Red; exit 1 }

Write-Host "PASSED: 校验和/索引/首字节/null/越界电量五项容灾全部严格拒绝" -ForegroundColor Green

Write-Host ""
Write-Host "==========================================================" -ForegroundColor Green
Write-Host " ALL 5/5 EDIFIER HALO SPACE TESTS PASSED! (漫步者 Halo Space 驱动层契约测试全量通过)" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
