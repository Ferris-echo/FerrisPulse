# ============================================================================
# 测试脚本: test_corsair_hs80max.ps1
# 目标: 验证海盗船 (Corsair) HS80 MAX Wireless 耳机 Bragi HID 协议驱动层：
#       文件规范 / C# 5.0 语法 / VID-PID 白名单 / 电量与充电状态报文解析 /
#       DeviceManager、BuiltInDrivers、MainForm 全生命周期接入。
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = Resolve-Path "$ScriptDir\.."
Set-Location $ProjectDir

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 1/6] CorsairDeviceHelper.cs 存在性与 UTF-8 BOM 编码校验" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$helperPath = "$ProjectDir\src\Hardware\CorsairDeviceHelper.cs"
if (-not (Test-Path $helperPath)) {
    Write-Host "FAILED: src\Hardware\CorsairDeviceHelper.cs 尚未创建！" -ForegroundColor Red
    exit 1
}

$bytes = [System.IO.File]::ReadAllBytes($helperPath)
if ($bytes.Length -lt 3 -or $bytes[0] -ne 0xEF -or $bytes[1] -ne 0xBB -or $bytes[2] -ne 0xBF) {
    Write-Host "FAILED: CorsairDeviceHelper.cs 不是 UTF-8 with BOM 编码！" -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: CorsairDeviceHelper.cs 文件存在且符合 UTF-8 with BOM 规范" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 2/6] C# 5.0 语法铁律审查（严禁使用 C# 6.0+ 语法）" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$content = [System.IO.File]::ReadAllText($helperPath, [System.Text.Encoding]::UTF8)

if ([regex]::IsMatch($content, '\$".*?"')) {
    Write-Host "FAILED: 发现禁用的字符串插值语法" -ForegroundColor Red
    exit 1
}
if ([regex]::IsMatch($content, '\w+\?\.')) {
    Write-Host "FAILED: 发现禁用的空条件运算符" -ForegroundColor Red
    exit 1
}
if ([regex]::IsMatch($content, 'nameof\s*\(')) {
    Write-Host "FAILED: 发现禁用的 nameof 语法" -ForegroundColor Red
    exit 1
}
if ([regex]::IsMatch($content, 'out\s+var\s+')) {
    Write-Host "FAILED: 发现禁用的 out var 内联声明" -ForegroundColor Red
    exit 1
}
if (-not $content.Contains("⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️")) {
    Write-Host "FAILED: 缺少头部 AI 维护准则声明" -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: CorsairDeviceHelper.cs 语法严格遵循 C# 5.0 标准" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 3/6] Corsair 硬件标识、型号与设备分类校验" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$binPath = "$ProjectDir\FerrisPulse.exe"
if (-not (Test-Path $binPath)) {
    Write-Host "编译二进制以供测试反射..." -ForegroundColor Yellow
    cmd /c "$ProjectDir\build.bat" | Out-Null
}

# 全内存反射加载，严禁 Add-Type / LoadFrom 产生文件句柄锁死
$asmBytes = [System.IO.File]::ReadAllBytes((Resolve-Path $binPath).Path)
$asm = [System.Reflection.Assembly]::Load($asmBytes)
$helperType = $asm.GetType("RazerBatteryTray.CorsairDeviceHelper")
if ($null -eq $helperType) {
    Write-Host "FAILED: 未能通过反射获取 RazerBatteryTray.CorsairDeviceHelper 类型" -ForegroundColor Red
    exit 1
}

$isSupported = $helperType.GetMethod("IsSupportedVidPid", [System.Reflection.BindingFlags]"Public,Static")
$r1 = $isSupported.Invoke($null, @(0x1B1C, 0x0A97))
$r2 = $isSupported.Invoke($null, @(0x1B1C, 0x0A96))
$r3 = $isSupported.Invoke($null, @(0x1532, 0x0A97))
$r4 = $isSupported.Invoke($null, @(0x1B1C, 0x2B0E))
if (-not $r1 -or -not $r2 -or $r3 -or $r4) {
    Write-Host "FAILED: IsSupportedVidPid 白名单过滤异常！(0A97:$r1 0A96:$r2 1532:$r3 2B0E:$r4)" -ForegroundColor Red
    exit 1
}

$getModelName = $helperType.GetMethod("GetModelName", [System.Reflection.BindingFlags]"Public,Static")
$name1 = $getModelName.Invoke($null, @(0x0A97, "CORSAIR HS80 MAX WIRELESS Gaming Receiver"))
$name2 = $getModelName.Invoke($null, @(0x0A96, ""))
if ($name1 -ne "HS80 MAX Wireless" -or -not $name2.Contains("HS80 MAX")) {
    Write-Host "FAILED: GetModelName 命名异常！name1: $name1, name2: $name2" -ForegroundColor Red
    exit 1
}

$isDevice = $helperType.GetMethod("IsCorsairDevice", [System.Reflection.BindingFlags]"Public,Static")
$idHit = $isDevice.Invoke($null, @("HID:1B1C:0A97"))
$idHit2 = $isDevice.Invoke($null, @("\\?\hid#vid_1b1c&pid_0a96&mi_04"))
$idMiss = $isDevice.Invoke($null, @("HID:046D:C08B"))
$idMiss2 = $isDevice.Invoke($null, @("HID:1B1C:2B0E"))
if (-not $idHit -or -not $idHit2 -or $idMiss -or $idMiss2) {
    Write-Host "FAILED: IsCorsairDevice 指纹识别异常！(hit:$idHit hit2:$idHit2 miss:$idMiss miss2:$idMiss2)" -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: Corsair VID/PID、型号与指纹识别逻辑全部正确" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 4/6] Bragi 电量 / 充电状态报文解析断言" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$parseBattery = $helperType.GetMethod("TryParseBattery", [System.Reflection.BindingFlags]"Public,Static")
$parseCharge = $helperType.GetMethod("TryParseChargeState", [System.Reflection.BindingFlags]"Public,Static")

function Invoke-Battery([byte[]]$buf) {
    $invokeArgs = [object[]]@($buf, $buf.Length, [double]0)
    $ok = $parseBattery.Invoke($null, $invokeArgs)
    return @{ Ok = [bool]$ok; Percent = [double]$invokeArgs[2] }
}

function Invoke-Charge([byte[]]$buf) {
    $invokeArgs = [object[]]@($buf, $buf.Length, [int]-1)
    $ok = $parseCharge.Invoke($null, $invokeArgs)
    return @{ Ok = [bool]$ok; State = [int]$invokeArgs[2] }
}

# 1. 实机抓包: 01 01 02 00 EA 01 -> 490 / 10 = 49.0%
$rx49 = [byte[]](0x01,0x01,0x02,0x00,0xEA,0x01)
$b1 = Invoke-Battery $rx49
if (-not $b1.Ok -or [Math]::Abs($b1.Percent - 49.0) -gt 0.001) {
    Write-Host "FAILED: 49.0% 电量帧解析失败！ok=$($b1.Ok) pct=$($b1.Percent)" -ForegroundColor Red
    exit 1
}

# 2. 实机抓包: 01 01 02 00 72 01 -> 370 / 10 = 37.0%
$rx37 = [byte[]](0x01,0x01,0x02,0x00,0x72,0x01)
$b2 = Invoke-Battery $rx37
if (-not $b2.Ok -or [Math]::Abs($b2.Percent - 37.0) -gt 0.001) {
    Write-Host "FAILED: 37.0% 电量帧解析失败！ok=$($b2.Ok) pct=$($b2.Percent)" -ForegroundColor Red
    exit 1
}

# 3. 剥离 Report ID 的 HID 后端布局: 01 02 00 72 01 -> 37.0%
$rxStripped = [byte[]](0x01,0x02,0x00,0x72,0x01)
$b3 = Invoke-Battery $rxStripped
if (-not $b3.Ok -or [Math]::Abs($b3.Percent - 37.0) -gt 0.001) {
    Write-Host "FAILED: 无 Report ID 布局解析失败！ok=$($b3.Ok) pct=$($b3.Percent)" -ForegroundColor Red
    exit 1
}

# 4. 错误帧 (err=0x05) 必须被拒绝，绝不把校验失败当成电量
$rxError = [byte[]](0x01,0x01,0x02,0x05,0x28,0x59,0x21,0x14)
$b4 = Invoke-Battery $rxError
if ($b4.Ok) {
    Write-Host "FAILED: 错误帧被误判为有效电量！pct=$($b4.Percent)" -ForegroundColor Red
    exit 1
}

# 5. 超短帧必须安全忽略
$b5 = Invoke-Battery ([byte[]](0x01,0x01,0x02))
if ($b5.Ok) {
    Write-Host "FAILED: 超短帧未被安全忽略！" -ForegroundColor Red
    exit 1
}

# 6. 充电状态帧: 1 = 充电中，0 = 放电，3 = 已接电源/充满，2 = 电池供电
$s1 = Invoke-Charge ([byte[]](0x01,0x01,0x02,0x00,0x01))
$s0 = Invoke-Charge ([byte[]](0x01,0x01,0x02,0x00,0x00))
$s3 = Invoke-Charge ([byte[]](0x01,0x01,0x02,0x00,0x03))
$s2 = Invoke-Charge ([byte[]](0x01,0x01,0x02,0x00,0x02))
if (-not $s1.Ok -or $s1.State -ne 1 -or -not $s0.Ok -or $s0.State -ne 0 -or -not $s3.Ok -or $s3.State -ne 3 -or -not $s2.Ok -or $s2.State -ne 2) {
    Write-Host "FAILED: 充电状态帧解析失败！s1=$($s1.State) s0=$($s0.State) s3=$($s3.State) s2=$($s2.State)" -ForegroundColor Red
    exit 1
}

$isCharging = $helperType.GetMethod("IsChargingState", [System.Reflection.BindingFlags]"Public,Static")
$c1 = $isCharging.Invoke($null, @(1))
$c2 = $isCharging.Invoke($null, @(2))
$c0 = $isCharging.Invoke($null, @(0))
$c3 = $isCharging.Invoke($null, @(3))
if (-not $c1 -or $c2 -or $c0 -or $c3) {
    Write-Host "FAILED: 充电判定异常！(1:$c1 2:$c2 0:$c0 3:$c3)" -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: Bragi 电量 / 充电状态 / 错误帧 / 短帧算法全部断言成功" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 5/6] DeviceManager / BuiltInDrivers / MainForm 生命周期融合校验" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$dmContent = [System.IO.File]::ReadAllText("$ProjectDir\src\Hardware\DeviceManager.cs", [System.Text.Encoding]::UTF8)
if (-not $dmContent.Contains("CorsairDeviceHelper.QueryCorsairDeviceInfo()")) {
    Write-Host "FAILED: DeviceManager 中缺少 QueryCorsairDeviceInfo 调用" -ForegroundColor Red
    exit 1
}
if (-not $dmContent.Contains("CorsairUserHidden")) {
    Write-Host "FAILED: DeviceManager 缺少尊重用户主动隐藏的自动解除隐藏逻辑" -ForegroundColor Red
    exit 1
}
if (-not $dmContent.Contains("CleanupDuplicateCorsairDevices")) {
    Write-Host "FAILED: DeviceManager 中缺少 CleanupDuplicateCorsairDevices 逻辑" -ForegroundColor Red
    exit 1
}

$mfContent = [System.IO.File]::ReadAllText("$ProjectDir\src\UI\Forms\MainForm.cs", [System.Text.Encoding]::UTF8)
if (-not $mfContent.Contains("CorsairDeviceHelper.LoadHardwareCache()")) {
    Write-Host "FAILED: MainForm 中缺少 Corsair 离线缓存预加载调用" -ForegroundColor Red
    exit 1
}

$bdContent = [System.IO.File]::ReadAllText("$ProjectDir\src\Hardware\BuiltInDrivers.cs", [System.Text.Encoding]::UTF8)
if (-not $bdContent.Contains("CorsairHeadsetDriver")) {
    Write-Host "FAILED: BuiltInDrivers 中缺少 CorsairHeadsetDriver 注册" -ForegroundColor Red
    exit 1
}

$ugContent = [System.IO.File]::ReadAllText("$ProjectDir\src\Hardware\UniversalGamingDeviceHelper.cs", [System.Text.Encoding]::UTF8)
if (-not $ugContent.Contains("pid_0a97")) {
    Write-Host "FAILED: UniversalGamingDeviceHelper 缺少 0A97 端点隔离，存在异构协议误扫风险" -ForegroundColor Red
    exit 1
}

$mbiContent = [System.IO.File]::ReadAllText("$ProjectDir\src\Models\MouseBatteryInfo.cs", [System.Text.Encoding]::UTF8)
if (-not $mbiContent.Contains("Corsair")) {
    Write-Host "FAILED: MouseBatteryInfo 中缺少 Corsair 品牌色彩配置" -ForegroundColor Red
    exit 1
}

$categoryType = $asm.GetType("RazerBatteryTray.DeviceCategory")
$headsetValue = [System.Enum]::Parse($categoryType, "Headset")
if ([int]$headsetValue -lt 0) {
    Write-Host "FAILED: DeviceCategory.Headset 枚举异常" -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: Corsair 驱动已接入硬件管理器、内置驱动注册表与 UI 生命周期" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 6/6] 实机在线探测（无设备时自动跳过）" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$queryMethod = $helperType.GetMethod("QueryCorsairDeviceInfo", [System.Reflection.BindingFlags]"Public,Static")
$loadCache = $helperType.GetMethod("LoadHardwareCache", [System.Reflection.BindingFlags]"Public,Static")
$loadCache.Invoke($null, @()) | Out-Null
$live = $queryMethod.Invoke($null, @())
if ($null -eq $live) {
    Write-Host "SKIPPED: 当前未检测到 HS80 MAX 接收器（离机回归测试通过）" -ForegroundColor Yellow
}
else {
    if ($live.DeviceId -ne "Corsair:1B1C:0A97" -and $live.DeviceId -ne "Corsair:1B1C:0A96") {
        Write-Host "FAILED: 实机 DeviceId 异常: $($live.DeviceId)" -ForegroundColor Red
        exit 1
    }
    if ($live.Brand -ne "Corsair") {
        Write-Host "FAILED: 实机 Brand 异常: $($live.Brand)" -ForegroundColor Red
        exit 1
    }
    if ($live.IsConnected) {
        if ($live.BatteryPercent -lt 0 -or $live.BatteryPercent -gt 100) {
            Write-Host "FAILED: 实机电量越界: $($live.BatteryPercent)" -ForegroundColor Red
            exit 1
        }
        Write-Host ("PASSED: 实机读取成功 -> " + $live.DeviceName + " / " + $live.BatteryPercent + "% / 充电:" + $live.IsCharging) -ForegroundColor Green
    }
    else {
        # 接收器在线但耳机已自动休眠：应保留缓存电量并标记 IsSleeping，绝不伪造连接
        if (-not $live.IsDonglePresent -or -not $live.IsSleeping) {
            Write-Host "FAILED: 耳机休眠态标记异常！dongle=$($live.IsDonglePresent) sleeping=$($live.IsSleeping)" -ForegroundColor Red
            exit 1
        }
        Write-Host ("PASSED: 接收器在线 / 耳机休眠，缓存电量 " + $live.BatteryPercent + "% 正确回显") -ForegroundColor Green
    }
}

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " 海盗船 (Corsair) HS80 MAX Wireless 耳机测试全部通过！" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
