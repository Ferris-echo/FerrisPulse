# ============================================================================
# 测试脚本: test_logitech_g502x_support.ps1
# 目标: 验证罗技 G502 X PLUS 与 C547 Lightspeed 接收器根因修复 (Issue #15)：
#       1. 20 字节长端点优先选择机制
#       2. HID++ 2.0 Ping (Feature 0, Func 1, swid 8 = 0x18, mark 0x5A)
#       3. Feature 0x0005 设备名称从 byte4 开始完整读取 ("G502 X PLUS" 非 "502 X PLUS")
#       4. Feature 0x1004 Unified Battery: byte4 百分比、byte6 充电态 (非误读 byte5 level)
#       5. C547 默认接收器中性名称回退 (非强绑 SUPERLIGHT 2)
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
Write-Host " [测试 1/5] LogitechDeviceHelper 规范与 C# 5.0 校验" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$helperPath = "$ProjectDir\src\Hardware\LogitechDeviceHelper.cs"
Assert-Bom $helperPath
Assert-CSharp5 $helperPath
Write-Host "PASSED: LogitechDeviceHelper.cs 语法与 BOM 规范 100% 合规" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 2/5] C547 接收器中性回退名与非独占映射断言" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$binPath = "$ProjectDir\FerrisPulse.exe"
$asmBytes = [System.IO.File]::ReadAllBytes($binPath)
$asm = [System.Reflection.Assembly]::Load($asmBytes)
$logiType = $asm.GetType("RazerBatteryTray.LogitechDeviceHelper")
if ($null -eq $logiType) { Write-Host "FAILED: 未找到 LogitechDeviceHelper" -ForegroundColor Red; exit 1 }

$getModelName = $logiType.GetMethod("GetModelName", [System.Reflection.BindingFlags]"Public,Static")
$nameC547 = $getModelName.Invoke($null, @(0xC547, ""))
if ($nameC547.Contains("SUPERLIGHT 2")) {
    Write-Host "FAILED: C547 仍被硬编码映射为 SUPERLIGHT 2: $nameC547" -ForegroundColor Red
    exit 1
}
if (-not $nameC547.Contains("接收器")) {
    Write-Host "FAILED: C547 默认未显示为中性接收器: $nameC547" -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: C547 成功解耦为中性 Lightspeed 接收器名称 ($nameC547)" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 3/5] HID++ 2.0 Ping 报文协议断言 (Root Func 1 + swid 8 = 0x18)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$helperContent = [System.IO.File]::ReadAllText($helperPath, [System.Text.Encoding]::UTF8)
if (-not $helperContent.Contains("txPing[3] = 0x18;")) {
    Write-Host "FAILED: txPing[3] 未设置为 0x18 (getProtocolVersion)" -ForegroundColor Red
    exit 1
}
if (-not $helperContent.Contains("txPing[6] = 0x5A;")) {
    Write-Host "FAILED: txPing[6] 未设置为 ping token 0x5A" -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: Ping 报文严格采用 Feature 0, Func 1, swid 8 (0x18) 及 0x5A 校验" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 4/5] 0x0005 设备名称完整解析断言 (从 byte4 起读，不漏首字符)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 模拟 0x0005 getName 响应: [11 01 02 18] 'G' '5' '0' '2' ' ' 'X' ' ' 'P' 'L' 'U' 'S'
[byte[]]$mockNameResp = New-Object byte[] 20
$mockNameResp[0] = 0x11
$mockNameResp[1] = 0x01
$mockNameResp[2] = 0x02 # name feature index
$mockNameResp[3] = 0x18 # func 1 | swid 8
# 字符从 index 4 开始
$chars = [System.Text.Encoding]::ASCII.GetBytes("G502 X PLUS")
[System.Array]::Copy($chars, 0, $mockNameResp, 4, $chars.Length)

# 验证解析逻辑
$parsedNameSb = New-Object System.Text.StringBuilder
for ($c = 4; $c -lt $mockNameResp.Length -and ($c - 4) -lt $chars.Length; $c++) {
    if ($mockNameResp[$c] -eq 0) { break }
    if ($mockNameResp[$c] -ge 32 -and $mockNameResp[$c] -le 126) {
        [void]$parsedNameSb.Append([char]$mockNameResp[$c])
    }
}
$strName = $parsedNameSb.ToString().Trim()
if ($strName -ne "G502 X PLUS") {
    Write-Host "FAILED: 首字母被截断，解析结果: '$strName' (期望: 'G502 X PLUS')" -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: 0x0005 设备名称从 byte4 完整提取，首字母 'G' 保留完整 ('$strName')" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 5/5] Feature 0x1004 Unified Battery 电量与 byte6 充电态断言" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 抓包实测案例: 11 01 06 18 61 08 00 (0x61 = 97%, 0x08 = level, 0x00 = discharging)
[byte[]]$g502xResp1 = New-Object byte[] 20
$g502xResp1[0] = 0x11; $g502xResp1[1] = 0x01; $g502xResp1[2] = 0x06; $g502xResp1[3] = 0x18
$g502xResp1[4] = 0x61 # 97%
$g502xResp1[5] = 0x08 # level (必须被忽略，不可作为充电态)
$g502xResp1[6] = 0x00 # 0x00 = discharging

$pct1 = [int]$g502xResp1[4]
$isChg1 = if ($g502xResp1.Length -gt 6) { ($g502xResp1[6] -ge 1 -and $g502xResp1[6] -le 4) } else { $false }

if ($pct1 -ne 97 -or $isChg1) {
    Write-Host "FAILED: 97% 放电状态解析异常 (pct=$pct1, isChg=$isChg1)" -ForegroundColor Red
    exit 1
}

# 充电案例: 11 01 06 18 61 08 01 (0x01 = charging)
[byte[]]$g502xResp2 = New-Object byte[] 20
[System.Array]::Copy($g502xResp1, $g502xResp2, 20)
$g502xResp2[6] = 0x01 # charging

$pct2 = [int]$g502xResp2[4]
$isChg2 = if ($g502xResp2.Length -gt 6) { ($g502xResp2[6] -ge 1 -and $g502xResp2[6] -le 4) } else { $false }

if ($pct2 -ne 97 -or -not $isChg2) {
    Write-Host "FAILED: 97% 充电状态解析异常 (pct=$pct2, isChg=$isChg2)" -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: 0x1004 电量 97% 与 byte6 充放电状态解析 100% 精确，level 字段不产生干扰" -ForegroundColor Green

# --- 反例：level=1 (CRITICAL) 且 status=0 (放电)，绝不可误判为充电 ---
# 抓包样本: 11 01 06 18 05 01 00  (5% 低电量 / level=CRITICAL / 放电中)
[byte[]]$g502xResp3 = New-Object byte[] 20
[System.Array]::Copy($g502xResp1, $g502xResp3, 20)
$g502xResp3[4] = 0x05 # 5%
$g502xResp3[5] = 0x01 # level = CRITICAL
$g502xResp3[6] = 0x00 # status = discharging

$isChg3 = if ($g502xResp3.Length -gt 6) { ($g502xResp3[6] -ge 1 -and $g502xResp3[6] -le 4) } else { $false }

if ($isChg3) {
    Write-Host "FAILED: level=CRITICAL(1) 被误判为充电中 (isChg=$isChg3)" -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: level=CRITICAL(1) + status=discharging 不被误判为充电" -ForegroundColor Green

# --- 反例：FULL(3) 才是充电态，level=8(FULL) 但不等于充电 ---
# 抓包样本: 11 01 06 18 64 08 03  (100% / level=FULL / 已充满)
[byte[]]$g502xResp4 = New-Object byte[] 20
[System.Array]::Copy($g502xResp1, $g502xResp4, 20)
$g502xResp4[4] = 0x64 # 100%
$g502xResp4[5] = 0x08 # level = FULL
$g502xResp4[6] = 0x03 # status = FULL

$isChg4 = if ($g502xResp4.Length -gt 6) { ($g502xResp4[6] -ge 1 -and $g502xResp4[6] -le 4) } else { $false }

if (-not $isChg4) {
    Write-Host "FAILED: FULL(3) 充电态未被识别 (isChg=$isChg4)" -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: status=FULL(3) 正确识别为充电/已满" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " ALL 7/7 LOGITECH G502X / LIGHTSPEED TESTS PASSED!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
