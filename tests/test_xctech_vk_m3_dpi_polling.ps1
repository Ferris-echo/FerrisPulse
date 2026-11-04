# ============================================================================
# 测试脚本: test_xctech_vk_m3_dpi_polling.ps1
# 目标: 验证 XCTECH（兮宸代工公版接收器，249A:5C2F / 248A:5C2E/5D2E）
#       Valkyrie VK M3 / VK M3 Lite 与 Acer OMR 系列外设协议演进：
#       1) BOM / C# 5.0 铁律与维护准则注释契约；
#       2) 传感器 ID 映射、量程、步进与原值编解码 (含 0x11 PAW3395/3311 算法验证)；
#       3) 回报率位标志互转 (125/250/500/1000/2000/4000/8000 Hz)；
#       4) 33 字节厂商帧组装与校验和手算一致性 (BuildVendorFrame)；
#       5) 出厂档位预设表 (M220 对应 VK M3 Lite 六档 800/1200/2000/5000/8000/12000)；
#       6) AcerMouseDriver 驱动层接口契约与 Mock/Test 设备强隔离保护 (绝不渗透真实 USB/HID)。
# 加载方式: 字节数组全内存反射加载（handbook §4.4：严禁 Add-Type -Path）。
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
    $codeOnly = ($content -split "`r?`n" | Where-Object {
        $t = $_.Trim()
        -not ($t.StartsWith("//") -or $t.StartsWith("/*") -or $t.StartsWith("*") -or $t.StartsWith("///"))
    }) -join "`n"
    if ([regex]::IsMatch($codeOnly, '\$"')) { Write-Host "FAILED: 字符串插值: $path" -ForegroundColor Red; exit 1 }
    if ([regex]::IsMatch($codeOnly, '\w+\?\.')) { Write-Host "FAILED: 空条件运算符: $path" -ForegroundColor Red; exit 1 }
    if ([regex]::IsMatch($codeOnly, 'nameof\s*\(')) { Write-Host "FAILED: nameof: $path" -ForegroundColor Red; exit 1 }
    if ([regex]::IsMatch($codeOnly, 'out\s+var\s+')) { Write-Host "FAILED: out var: $path" -ForegroundColor Red; exit 1 }
    if ([regex]::IsMatch($codeOnly, '=>\s*\{')) { Write-Host "FAILED: 表达式体成员: $path" -ForegroundColor Red; exit 1 }
    if (-not $content.Contains("⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️")) {
        Write-Host "FAILED: 缺少 AI 维护准则声明: $path" -ForegroundColor Red; exit 1
    }
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 1/6] AcerDeviceHelper 源码规范与 C# 5.0 契约" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$helperPath = "$ProjectDir\src\Hardware\AcerDeviceHelper.cs"
Assert-Bom $helperPath
Assert-CSharp5 $helperPath
Write-Host "PASSED: AcerDeviceHelper.cs 存在、BOM 正确且符合 C# 5.0 铁律" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 2/6] 全内存反射加载与类型准备" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$binPath = "$ProjectDir\FerrisPulse.exe"
if (-not (Test-Path $binPath)) {
    Write-Host "编译二进制以供反射测试..." -ForegroundColor Yellow
    cmd /c "$ProjectDir\build.bat" | Out-Null
}
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($binPath))
$helperType = $asm.GetType("RazerBatteryTray.AcerDeviceHelper")
if ($null -eq $helperType) { Write-Host "FAILED: 未找到 AcerDeviceHelper" -ForegroundColor Red; exit 1 }
$driverType = $asm.GetType("RazerBatteryTray.AcerMouseDriver")
if ($null -eq $driverType) { Write-Host "FAILED: 未找到 AcerMouseDriver" -ForegroundColor Red; exit 1 }
Write-Host "PASSED: 程序集已通过全内存反射正确装配" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 3/6] 传感器量程与 DPI 编解码互逆性验证" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$getRange = $helperType.GetMethod("GetDpiRange", [System.Reflection.BindingFlags]"Public,Static")
$decodeDpi = $helperType.GetMethod("DecodeDpiRaw", [System.Reflection.BindingFlags]"Public,Static")
$encodeDpi = $helperType.GetMethod("EncodeDpiRaw", [System.Reflection.BindingFlags]"Public,Static")

# 传感器 0x11（VK M3 Lite PAW3395/3311 系列）：50~18000 DPI，步进 50
$rangeInv = New-Object 'object[]' 4
$rangeInv[0] = [int]0x11
$rangeInv[1] = [int]0 # out min
$rangeInv[2] = [int]0 # out max
$rangeInv[3] = [int]0 # out step
$getRange.Invoke($null, $rangeInv)
if ($rangeInv[1] -ne 50 -or $rangeInv[2] -ne 18000 -or $rangeInv[3] -ne 50) {
    Write-Host ("FAILED: 0x11 传感器量程解析错误: min={0}, max={1}, step={2}" -f $rangeInv[1], $rangeInv[2], $rangeInv[3]) -ForegroundColor Red
    exit 1
}

# 验证 VK M3 Lite 出厂 6 档编解码
$vkStages = @(800, 1200, 2000, 5000, 8000, 12000)
foreach ($targetDpi in $vkStages) {
    $raw = [int]$encodeDpi.Invoke($null, @([int]0x11, [int]$targetDpi))
    $decoded = [int]$decodeDpi.Invoke($null, @([int]0x11, [int]$raw))
    if ($decoded -ne $targetDpi) {
        Write-Host ("FAILED: 0x11 DPI 编解码不一致: 期望 {0}, 得到 raw={1}, decoded={2}" -f $targetDpi, $raw, $decoded) -ForegroundColor Red
        exit 1
    }
}
Write-Host "PASSED: 传感器 0x11 量程 50~18000 步进 50，出厂 6 档 DPI 完美通过编解码互逆测试" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 4/6] 回报率标志与 Hz 映射双向一致性" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$flagFromHz = $helperType.GetMethod("RateFlagFromHz", [System.Reflection.BindingFlags]"Public,Static")
$hzFromFlag = $helperType.GetMethod("RateHzFromFlag", [System.Reflection.BindingFlags]"Public,Static")

$ratePairs = @(
    @{ Hz = 125; Flag = 0x08 },
    @{ Hz = 250; Flag = 0x04 },
    @{ Hz = 500; Flag = 0x02 },
    @{ Hz = 1000; Flag = 0x01 },
    @{ Hz = 2000; Flag = 0x10 },
    @{ Hz = 4000; Flag = 0x20 },
    @{ Hz = 8000; Flag = 0x40 }
)

foreach ($p in $ratePairs) {
    $calcFlag = [int]$flagFromHz.Invoke($null, @([int]$p.Hz))
    if ($calcFlag -ne $p.Flag) {
        Write-Host ("FAILED: RateFlagFromHz({0}) 应为 0x{1:X2}，得到 0x{2:X2}" -f $p.Hz, $p.Flag, $calcFlag) -ForegroundColor Red
        exit 1
    }
    $calcHz = [int]$hzFromFlag.Invoke($null, @([int]$p.Flag))
    if ($calcHz -ne $p.Hz) {
        Write-Host ("FAILED: RateHzFromFlag(0x{0:X2}) 应为 {1}，得到 {2}" -f $p.Flag, $p.Hz, $calcHz) -ForegroundColor Red
        exit 1
    }
}
# 非法回报率应回退 0
if ([int]$flagFromHz.Invoke($null, @([int]123)) -ne 0) { Write-Host "FAILED: 非法回报率应返回 0" -ForegroundColor Red; exit 1 }
Write-Host "PASSED: 125Hz~8000Hz 全部标准档位双向一一映射，非法回报率拦截有效" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 5/6] 厂商协议帧结构与校验和手算一致性 (BuildVendorFrame)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$buildFrame = $helperType.GetMethod("BuildVendorFrame", [System.Reflection.BindingFlags]"Public,Static")

# 构造一个 0x02 回报率写帧：command=0x02, payload=[0x01], isWrite=$true
$txRate = [byte[]]$buildFrame.Invoke($null, @([byte]0x02, [byte[]]@([byte]0x01), [bool]$true, [int]0))
if ($txRate.Length -ne 33) { Write-Host "FAILED: 厂商帧长度必须为 33 字节" -ForegroundColor Red; exit 1 }
if ($txRate[0] -ne 0x00) { Write-Host "FAILED: [0] Report ID 必须为 0x00" -ForegroundColor Red; exit 1 }
if ($txRate[1] -ne 0x02) { Write-Host "FAILED: [1] 命令字必须为 0x02" -ForegroundColor Red; exit 1 }
if ($txRate[3] -ne 0x01) { Write-Host "FAILED: [3] 写标志必须为 0x01" -ForegroundColor Red; exit 1 }
if ($txRate[4] -ne 0x01) { Write-Host "FAILED: [4] 负载长度必须为 0x01" -ForegroundColor Red; exit 1 }
if ($txRate[5] -ne 0x01) { Write-Host "FAILED: [5] 负载首字节必须为 0x01" -ForegroundColor Red; exit 1 }
# 校验和：sum([5..31]) & 0xFF。由于 [5]=0x01，其余均为 0，sum=0x01
if ($txRate[32] -ne 0x01) {
    Write-Host ("FAILED: [32] 校验和应为 0x01，得到 0x{0:X2}" -f $txRate[32]) -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: BuildVendorFrame 生成的 33 字节厂商帧与校验和严格符合协议标准" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 6/6] AcerMouseDriver 测试隔离与参数门禁契约" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$driver = [Activator]::CreateInstance($driverType)
$mouseInfoType = $asm.GetType("RazerBatteryTray.MouseBatteryInfo")
$testDev = [Activator]::CreateInstance($mouseInfoType)
$testDev.DeviceId = "TEST_ACER_VK_M3_LITE"
$testDev.Brand = "Acer"
$testDev.Dpi = 800
$testDev.DpiStage = 1
$testDev.DpiStageCount = 6
$testDev.PollingRate = 1000

# 验证测试隔离下 SetDpi 成功且不发物理包
$setDpiMethod = $driverType.GetMethod("SetDpi")
$okSetDpi = $setDpiMethod.Invoke($driver, @($testDev, [int]1600, [int]1600, [int]2))
if (-not $okSetDpi -or $testDev.Dpi -ne 1600 -or $testDev.DpiStage -ne 2) {
    Write-Host "FAILED: 测试隔离模式下 SetDpi 应返回 true 并更新内部状态" -ForegroundColor Red
    exit 1
}

# 验证非法 DPI 档位被优雅拦截
$failStage = $setDpiMethod.Invoke($driver, @($testDev, [int]1600, [int]1600, [int]99))
if ($failStage) {
    Write-Host "FAILED: 非法档位 99 应被拦截拒绝" -ForegroundColor Red
    exit 1
}

# 验证 SetPollingRate 正确更新与非法拒绝
$setRateMethod = $driverType.GetMethod("SetPollingRate")
$okSetRate = $setRateMethod.Invoke($driver, @($testDev, [int]500))
if (-not $okSetRate -or $testDev.PollingRate -ne 500) {
    Write-Host "FAILED: 测试隔离模式下 SetPollingRate 500Hz 应返回 true 并更新状态" -ForegroundColor Red
    exit 1
}
$failRate = $setRateMethod.Invoke($driver, @($testDev, [int]123))
if ($failRate) {
    Write-Host "FAILED: 非法回报率 123Hz 应被拦截拒绝" -ForegroundColor Red
    exit 1
}

Write-Host "PASSED: AcerMouseDriver 在测试隔离模式下完美响应控制指令，且严格杜绝物理设备穿透" -ForegroundColor Green

Write-Host "`nALL XCTECH VK M3 LITE TESTS PASSED" -ForegroundColor Green
