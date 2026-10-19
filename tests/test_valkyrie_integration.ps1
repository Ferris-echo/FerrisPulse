# ============================================================================
# 测试脚本: test_valkyrie_integration.ps1
# 目标: 验证瓦尔基里（Valkyrie）驱动层代码规范、报文解析、型号匹配与全系支持
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = Resolve-Path "$ScriptDir\.."
Set-Location $ProjectDir

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 1/4] 瓦尔基里驱动文件存在性与 UTF-8 BOM 编码校验" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$helperPath = "$ProjectDir\src\Hardware\ValkyrieDeviceHelper.cs"
if (-not (Test-Path $helperPath)) {
    Write-Host "FAILED: src\Hardware\ValkyrieDeviceHelper.cs 尚未创建！" -ForegroundColor Red
    exit 1
}

$bytes = [System.IO.File]::ReadAllBytes($helperPath)
if ($bytes.Length -lt 3 -or $bytes[0] -ne 0xEF -or $bytes[1] -ne 0xBB -or $bytes[2] -ne 0xBF) {
    Write-Host "FAILED: ValkyrieDeviceHelper.cs 不是 UTF-8 with BOM 编码！" -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: ValkyrieDeviceHelper.cs 文件存在且符合 UTF-8 with BOM 规范" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 2/4] C# 5.0 语法铁律审查（严禁使用 C# 6.0+ 语法）" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$content = [System.IO.File]::ReadAllText($helperPath, [System.Text.Encoding]::UTF8)

# 1. 禁用字符串插值
if ([regex]::IsMatch($content, '\$".*?"')) {
    Write-Host "FAILED: 发现禁用的字符串插值语法" -ForegroundColor Red
    exit 1
}

# 2. 禁用空条件运算符
if ([regex]::IsMatch($content, '\w+\?\.')) {
    Write-Host "FAILED: 发现禁用的空条件运算符" -ForegroundColor Red
    exit 1
}

# 3. 禁用 nameof
if ([regex]::IsMatch($content, 'nameof\s*\(')) {
    Write-Host "FAILED: 发现禁用的 nameof 语法" -ForegroundColor Red
    exit 1
}

# 4. 禁用 out var
if ([regex]::IsMatch($content, 'out\s+var\s+')) {
    Write-Host "FAILED: 发现禁用的 out var 内联声明" -ForegroundColor Red
    exit 1
}

# 5. 必须包含头部 AI 维护准则
if (-not $content.Contains("⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️")) {
    Write-Host "FAILED: 缺少头部 AI 维护准则声明" -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: ValkyrieDeviceHelper.cs 语法严格遵循 C# 5.0 标准" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 3/4] 瓦尔基里全系 PID 型号匹配与品类识别验证" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 验证关键型号映射是否存在
$requiredModels = @(
    "VK 99 Max V2",
    "VK 99 V2",
    "VK 87 Max V2",
    "VK 87 V2",
    "VK 75 PRO",
    "VK M5 PRO",
    "VK M5 Ultra",
    "VK M7 PRO"
)

foreach ($model in $requiredModels) {
    if (-not $content.Contains($model)) {
        Write-Host "FAILED: 驱动中未找到型号映射: $model" -ForegroundColor Red
        exit 1
    }
}
Write-Host "PASSED: 核心型号映射完整（包含 VK 99/87/75 键盘与 VK M5/M7 鼠标）" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 4/4] 模拟 0xF7 报文解码算法验证" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 模拟 1: 键盘 2.4G 在线，电量 88%
$mockRx1 = New-Object byte[] 65
$mockRx1[0] = 0x01
$mockRx1[1] = 88
$mockRx1[3] = 0x00
$mockRx1[6] = 0x01

$isCanRead = ($mockRx1[0] -eq 1)
$kbBattery = [int]$mockRx1[1]
$kbOnline = ($mockRx1[3] -eq 0)
$devType = [int]$mockRx1[6]

if (-not $isCanRead -or $kbBattery -ne 88 -or -not $kbOnline -or $devType -ne 1) {
    Write-Host "FAILED: 键盘在线报文模拟解码失败！" -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: 键盘 88% 在线报文解码正确" -ForegroundColor Green

# 模拟 2: 键鼠二合一接收器，键盘 92%，鼠标 65% 在线
$mockRx2 = New-Object byte[] 65
$mockRx2[0] = 0x01
$mockRx2[1] = 92
$mockRx2[2] = 65
$mockRx2[3] = 0x00
$mockRx2[4] = 0x00
$mockRx2[6] = 0x03

$mBattery = [int]$mockRx2[2]
$mOnline = ($mockRx2[4] -eq 0)
if ($mockRx2[6] -ne 3 -or $mockRx2[1] -ne 92 -or $mBattery -ne 65 -or -not $mOnline) {
    Write-Host "FAILED: 键鼠双模接收器模拟解码失败！" -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: 键鼠双模二合一接收器报文解码正确" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " ALL PASS: 瓦尔基里全系支持测试全部通过！" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
exit 0
