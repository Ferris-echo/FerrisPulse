# ============================================================================
# 测试脚本: test_sony_inzone_h5_support.ps1
# 目标: 验证索尼 INZONE H5 (WH-G500, 054C:0EBF) 头戴式 2.4GHz 接收器适配:
#       型号白名单、头戴两字节电量槽 [charger, percent] 解析、真实抓包帧、
#       按载荷宽度自适应分派、充电标志、校验和篡改拒绝与旧入耳契约不回归
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

function New-Frame([int[]]$head, [int]$status, [int]$percent) {
    # 组装头戴式电池应答帧（64 字节），校验和 = sum(buffer[5..14]) & 0xFF 写入 buffer[15]
    [byte[]]$buf = New-Object byte[] 64
    for ($i = 0; $i -lt $head.Count; $i++) { $buf[$i] = [byte]$head[$i] }
    $buf[13] = [byte]$status
    $buf[14] = [byte]$percent
    $sum = 0
    for ($i = 5; $i -le 14; $i++) { $sum += $buf[$i] }
    $buf[15] = [byte]($sum -band 0xFF)
    return $buf
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 1/6] SonyInzoneHelper 源码 BOM 与 C# 5.0 规范核验" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$helperPath = "$ProjectDir\src\Hardware\SonyInzoneHelper.cs"
Assert-Bom $helperPath
Assert-CSharp5 $helperPath
Write-Host "PASSED: SonyInzoneHelper.cs BOM 与 C# 5.0 铁律合规" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 2/6] H5 与 Buds 双型号白名单常量核验" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$content = [System.IO.File]::ReadAllText($helperPath, [System.Text.Encoding]::UTF8)
foreach ($token in @("0x054C", "0x0EBF", "0x0EC2", "0xFF04", "WH-G500", "WF-G700N", "ParseHeadsetBatteryResponse", "ParseBatteryResponseAuto")) {
    if (-not $content.Contains($token)) {
        Write-Host "FAILED: 缺少型号/协议声明 $token" -ForegroundColor Red
        exit 1
    }
}
Write-Host "PASSED: 054C:0EBF (H5) 与 054C:0EC2 (Buds) 双型号常量完整" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 3/6] 反射加载与真实抓包帧解析（未充电 100%）" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$binPath = "$ProjectDir\FerrisPulse.exe"
if (-not (Test-Path $binPath)) {
    Write-Host "编译二进制以供反射测试..." -ForegroundColor Yellow
    cmd /c "$ProjectDir\build.bat" | Out-Null
}
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($binPath))
$sonyType = $asm.GetType("RazerBatteryTray.SonyInzoneHelper")
if ($null -eq $sonyType) { Write-Host "FAILED: 未找到 SonyInzoneHelper" -ForegroundColor Red; exit 1 }

$parseHeadset = $sonyType.GetMethod("ParseHeadsetBatteryResponse", [System.Reflection.BindingFlags]"Public,Static")
$parseAuto    = $sonyType.GetMethod("ParseBatteryResponseAuto", [System.Reflection.BindingFlags]"Public,Static")
$parseBuds    = $sonyType.GetMethod("ParseBatteryResponse", [System.Reflection.BindingFlags]"Public,Static")
$resolveBatt  = $sonyType.GetMethod("ResolvePrimaryBatteryPercent", [System.Reflection.BindingFlags]"Public,Static")
$buildTip     = $sonyType.GetMethod("BuildTooltip", [System.Reflection.BindingFlags]"Public,Static")
$isSupported  = $sonyType.GetMethod("IsSupportedVidPid", [System.Reflection.BindingFlags]"Public,Static")

# 实测抓包（本机 INZONE H5 接收器返回）：02 0E 04 FF 0B 00 96 C3 14 04 10 01 00 00 64 E6
[byte[]]$capture = New-Object byte[] 64
$capture[0] = 0x02; $capture[1] = 0x0E; $capture[2] = 0x04; $capture[3] = 0xFF
$capture[4] = 0x0B; $capture[5] = 0x00; $capture[6] = 0x96; $capture[7] = 0xC3
$capture[8] = 0x14; $capture[9] = 0x04; $capture[10] = 0x10; $capture[11] = 0x01
$capture[12] = 0x00; $capture[13] = 0x00; $capture[14] = 0x64; $capture[15] = 0xE6

[object[]]$a1 = @($capture, $null)
$ok1 = $parseHeadset.Invoke($null, $a1)
$s1 = $a1[1]
if (-not $ok1 -or $null -eq $s1) { Write-Host "FAILED: 真实抓包帧解析失败" -ForegroundColor Red; exit 1 }
if (-not $s1.IsHeadset -or $s1.LeftBattery -ne 100 -or $s1.LeftCharging -or
    $s1.RightBattery -ne -1 -or $s1.CaseBattery -ne -1 -or -not $s1.IsConnected) {
    Write-Host "FAILED: 头戴式状态解析异常 (L=$($s1.LeftBattery) R=$($s1.RightBattery) Case=$($s1.CaseBattery))" -ForegroundColor Red; exit 1
}
if (($resolveBatt.Invoke($null, @($s1))) -ne 100) { Write-Host "FAILED: 主电量非 100" -ForegroundColor Red; exit 1 }
$tip1 = $buildTip.Invoke($null, @($s1))
if ($tip1 -ne "电量: 100%") { Write-Host "FAILED: 头戴式 Tooltip 异常: $tip1" -ForegroundColor Red; exit 1 }
Write-Host "PASSED: 真实抓包帧 02 0E ... 64 E6 解析为 电量 100% / 未充电" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 4/6] 充电状态与低电量、Auto 宽度分派" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 充电中（来自 sony-h5-tray 公开单测帧）：02 0E 04 FF 0B 00 96 C3 14 04 A0 01 00 01 64 77
[byte[]]$chargerFrame = New-Frame @(0x02,0x0E,0x04,0xFF,0x0B,0x00,0x96,0xC3,0x14,0x04,0xA0,0x01,0x00) 1 100
if ($chargerFrame[15] -ne 0x77) {
    Write-Host ("FAILED: 充电帧校验和应为 0x77, 实际 0x" + $chargerFrame[15].ToString('X2')) -ForegroundColor Red; exit 1
}
[object[]]$a2 = @($chargerFrame, $null)
$ok2 = $parseAuto.Invoke($null, $a2)
$s2 = $a2[1]
if (-not $ok2 -or -not $s2.IsHeadset -or -not $s2.LeftCharging -or $s2.LeftBattery -ne 100) {
    Write-Host "FAILED: 充电帧解析异常" -ForegroundColor Red; exit 1
}
$tip2 = $buildTip.Invoke($null, @($s2))
if ($tip2 -ne "电量: 100%⚡") { Write-Host "FAILED: 充电 Tooltip 缺闪电: $tip2" -ForegroundColor Red; exit 1 }
Write-Host "PASSED: status=0x01 识别为充电中并输出闪电标记" -ForegroundColor Green

# 低电量 7% 未充电
[byte[]]$lowFrame = New-Frame @(0x02,0x0E,0x04,0xFF,0x0B,0x00,0x96,0xC3,0x14,0x04,0x10,0x02,0x00) 0 7
[object[]]$a3 = @($lowFrame, $null)
if (-not $parseAuto.Invoke($null, $a3)) { Write-Host "FAILED: 低电量帧解析失败" -ForegroundColor Red; exit 1 }
if ($a3[1].LeftBattery -ne 7 -or $a3[1].LeftCharging) { Write-Host "FAILED: 低电量解析异常" -ForegroundColor Red; exit 1 }
Write-Host "PASSED: 低电量 7% / 未充电 解析正确" -ForegroundColor Green

# Auto 分派：入耳 6 字节槽（hci_len=0x12）必须走 Buds 路径
[byte[]]$buds = New-Object byte[] 64
$buds[0] = 0x02; $buds[1] = 0x12; $buds[2] = 0x04; $buds[3] = 0xFF; $buds[4] = 0x0F; $buds[5] = 0x00
$buds[6] = 0x96; $buds[7] = 0xC3; $buds[8] = 0x14; $buds[9] = 0x04; $buds[10] = 0x10
$buds[11] = 0x01; $buds[12] = 0x00
$buds[13] = 0x00; $buds[14] = 0x64; $buds[15] = 0x00; $buds[16] = 0x64; $buds[17] = 0xFF; $buds[18] = 0x61
$buds[19] = 0xAA
[object[]]$a4 = @($buds, $null)
if (-not $parseAuto.Invoke($null, $a4)) { Write-Host "FAILED: Auto 未分派入耳 6 字节槽" -ForegroundColor Red; exit 1 }
if ($a4[1].IsHeadset -or $a4[1].LeftBattery -ne 100 -or $a4[1].CaseBattery -ne 97) {
    Write-Host "FAILED: Auto 分派后入耳解析异常" -ForegroundColor Red; exit 1
}
Write-Host "PASSED: Auto 按载荷宽度分派（2 字节->头戴 / 6 字节->入耳）" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 5/6] 校验和篡改与非法 HCI 长度拒绝" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

[byte[]]$tampered = New-Object byte[] 64
[System.Array]::Copy($capture, $tampered, 64)
$tampered[15] = 0x00
[object[]]$a5 = @($tampered, $null)
if ($parseHeadset.Invoke($null, $a5)) {
    Write-Host "FAILED: 篡改校验和后仍被接受" -ForegroundColor Red; exit 1
}

# 头戴通道收到 6 字节载荷（hci_len=0x12）必须拒绝，避免把入耳帧误当单耳
[byte[]]$wrongWidth = New-Object byte[] 64
[System.Array]::Copy($buds, $wrongWidth, 64)
[object[]]$a6 = @($wrongWidth, $null)
if ($parseHeadset.Invoke($null, $a6)) {
    Write-Host "FAILED: 头戴解析器接受了入耳宽度的帧" -ForegroundColor Red; exit 1
}
Write-Host "PASSED: 校验和篡改与帧宽不符均被拒绝，杜绝脏数据" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 6/6] 双型号白名单与入耳旧契约不回归" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if (-not $isSupported.Invoke($null, @(0x054C, 0x0EBF))) { Write-Host "FAILED: 054C:0EBF 未通过白名单" -ForegroundColor Red; exit 1 }
if (-not $isSupported.Invoke($null, @(0x054C, 0x0EC2))) { Write-Host "FAILED: 054C:0EC2 未通过白名单" -ForegroundColor Red; exit 1 }
if ($isSupported.Invoke($null, @(0x054C, 0x0EBE))) { Write-Host "FAILED: 未知 PID 被误纳入白名单" -ForegroundColor Red; exit 1 }

[object[]]$a7 = @($buds, $null)
$ok7 = $parseBuds.Invoke($null, $a7)
if (-not $ok7 -or $a7[1].LeftBattery -ne 100 -or $a7[1].RightBattery -ne 100 -or $a7[1].CaseBattery -ne 97 -or $a7[1].IsHeadset) {
    Write-Host "FAILED: 入耳六字节解析出现回归" -ForegroundColor Red; exit 1
}
Write-Host "PASSED: 白名单精确匹配，入耳 Buds 契约零回归" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " ALL 6/6 SONY INZONE H5 TESTS PASSED! (头戴式接收器适配测试全量通过)" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
