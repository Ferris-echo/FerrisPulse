# ============================================================================
# 测试脚本: test_hyperx_support.ps1
# 目标: 验证极度未知（HyperX）无线耳机驱动层代码规范、0x66报文解析、飓风3无线版识别与全系统融合
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = Resolve-Path "$ScriptDir\.."
Set-Location $ProjectDir

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 1/5] HyperXDeviceHelper.cs 存在性与 UTF-8 BOM 编码校验" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$helperPath = "$ProjectDir\src\Hardware\HyperXDeviceHelper.cs"
if (-not (Test-Path $helperPath)) {
    Write-Host "FAILED: src\Hardware\HyperXDeviceHelper.cs 尚未创建！" -ForegroundColor Red
    exit 1
}

$bytes = [System.IO.File]::ReadAllBytes($helperPath)
if ($bytes.Length -lt 3 -or $bytes[0] -ne 0xEF -or $bytes[1] -ne 0xBB -or $bytes[2] -ne 0xBF) {
    Write-Host "FAILED: HyperXDeviceHelper.cs 不是 UTF-8 with BOM 编码！" -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: HyperXDeviceHelper.cs 文件存在且符合 UTF-8 with BOM 规范" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 2/5] C# 5.0 语法铁律审查（严禁使用 C# 6.0+ 语法）" -ForegroundColor Cyan
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
Write-Host "PASSED: HyperXDeviceHelper.cs 语法严格遵循 C# 5.0 标准" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 3/5] HyperX 硬件标识与型号白名单校验" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 加载 FerrisPulse.exe 进行反射与契约测试
$binPath = "$ProjectDir\FerrisPulse.exe"
if (-not (Test-Path $binPath)) {
    Write-Host "编译二进制以供测试反射..." -ForegroundColor Yellow
    cmd /c "$ProjectDir\build.bat" | Out-Null
}

# ⚠️ 铁律 10：严禁 Add-Type -Path / LoadFrom 锁死 exe 文件句柄（会导致后续编译 CS0016）。
# 必须全内存反射装载。
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($binPath))
$helperType = $asm.GetType("RazerBatteryTray.HyperXDeviceHelper")
if ($null -eq $helperType) {
    Write-Host "FAILED: 未能通过反射获取 RazerBatteryTray.HyperXDeviceHelper 类型" -ForegroundColor Red
    exit 1
}

# 验证 VID/PID 支持判定
$isSupported = $helperType.GetMethod("IsSupportedVidPid", [System.Reflection.BindingFlags]"Public,Static")
$res1 = $isSupported.Invoke($null, @(0x03F0, 0x05B7)) # 飓风3无线
$res2 = $isSupported.Invoke($null, @(0x03F0, 0x0C9D)) # 飓风3无线 变体
$res3 = $isSupported.Invoke($null, @(0x03F0, 0x06BE)) # 飓风3S无线 (应显式排除)
$res4 = $isSupported.Invoke($null, @(0x1532, 0x05B7)) # 非 HP 厂商

if (-not $res1 -or -not $res2 -or $res3 -or $res4) {
    Write-Host "FAILED: IsSupportedVidPid 白名单过滤异常！(05B7: $res1, 0C9D: $res2, 06BE: $res3, 1532: $res4)" -ForegroundColor Red
    exit 1
}

# 验证型号命名获取
$getModelName = $helperType.GetMethod("GetModelName", [System.Reflection.BindingFlags]"Public,Static")
$name1 = $getModelName.Invoke($null, @(0x05B7, ""))
$name2 = $getModelName.Invoke($null, @(0x0C9D, ""))
if ($name1 -ne "HyperX 飓风3 无线" -or $name2 -ne "HyperX 飓风3 无线") {
    Write-Host "FAILED: GetModelName 命名异常！name1: $name1, name2: $name2" -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: HyperX 硬件识别与型号白名单逻辑全部正确" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 4/5] 0x66 状态上报与电量/充电报文解析算法断言" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$parseMethod = $helperType.GetMethod("ParseStatusReport", [System.Reflection.BindingFlags]"Public,Static")

function Invoke-ParseReport([byte[]]$buf, [int]$len, [int]$inConn, [int]$inChg, [int]$inBatt) {
    $arr = [System.Array]::CreateInstance([object], 5)
    $arr.SetValue($buf, 0)
    $arr.SetValue($len, 1)
    $arr.SetValue($inConn, 2)
    $arr.SetValue($inChg, 3)
    $arr.SetValue($inBatt, 4)
    $parseMethod.Invoke($null, $arr)
    return @{
        Connected = [int]$arr[2]
        Charging = [int]$arr[3]
        Battery = [int]$arr[4]
    }
}

# 1. 测试连接状态报文: ReportId=0x66, Cmd=0x82, State=1
$rxConn = New-Object byte[] 62
$rxConn[0] = 0x66
$rxConn[1] = 0x82
$rxConn[2] = 1
$r1 = Invoke-ParseReport $rxConn 62 -1 -1 -1
if ($r1.Connected -ne 1) {
    Write-Host "FAILED: 连接报文解析失败，期望 connected=1，实际: $($r1.Connected)" -ForegroundColor Red
    exit 1
}

# 2. 测试电量报文(双0未开机过滤): ReportId=0x66, Cmd=0x89, State=0, State2=0, Level=0
$rxBattZero = New-Object byte[] 62
$rxBattZero[0] = 0x66
$rxBattZero[1] = 0x89
$rxBattZero[2] = 0
$rxBattZero[3] = 0
$rxBattZero[4] = 0
$r2 = Invoke-ParseReport $rxBattZero 62 -1 -1 50
if ($r2.Battery -ne 50) {
    Write-Host "FAILED: 未开机双0状态未被忽略，导致电量被误改！期望 battery=50，实际: $($r2.Battery)" -ForegroundColor Red
    exit 1
}

# 3. 测试正常电量报文: ReportId=0x66, Cmd=0x89, State=1, State2=2, Level=88
$rxBattOk = New-Object byte[] 62
$rxBattOk[0] = 0x66
$rxBattOk[1] = 0x89
$rxBattOk[2] = 1
$rxBattOk[3] = 2
$rxBattOk[4] = 88
$r3 = Invoke-ParseReport $rxBattOk 62 -1 -1 -1
if ($r3.Battery -ne 88) {
    Write-Host "FAILED: 正常电量解析失败，期望 battery=88，实际: $($r3.Battery)" -ForegroundColor Red
    exit 1
}

# 4. 测试充电中报文: ReportId=0x66, Cmd=0x8A, State=1
$rxChg = New-Object byte[] 62
$rxChg[0] = 0x66
$rxChg[1] = 0x8A
$rxChg[2] = 1
$r4 = Invoke-ParseReport $rxChg 62 -1 -1 -1
if ($r4.Charging -ne 1) {
    Write-Host "FAILED: 充电中报文解析失败，期望 charging=1，实际: $($r4.Charging)" -ForegroundColor Red
    exit 1
}

# 5. 测试充满报文: ReportId=0x66, Cmd=0x8A, State=2 -> 充满且电量置为100%
$rxFull = New-Object byte[] 62
$rxFull[0] = 0x66
$rxFull[1] = 0x8A
$rxFull[2] = 2
$r5 = Invoke-ParseReport $rxFull 62 -1 -1 95
if ($r5.Charging -ne 0 -or $r5.Battery -ne 100) {
    Write-Host "FAILED: 充满报文解析失败，期望 charging=0, battery=100，实际: charging=$($r5.Charging), battery=$($r5.Battery)" -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: 0x66 状态上报与电量/充电报文算法全部断言成功" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 5/5] DeviceManager 与系统生命周期融合校验" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$dmContent = [System.IO.File]::ReadAllText("$ProjectDir\src\Hardware\DeviceManager.cs", [System.Text.Encoding]::UTF8)
if (-not $dmContent.Contains("HyperXDeviceHelper.QueryHyperXDeviceInfo()")) {
    Write-Host "FAILED: DeviceManager 中缺少 QueryHyperXDeviceInfo 调用" -ForegroundColor Red
    exit 1
}
if (-not $dmContent.Contains("CleanupDuplicateHyperXDevices")) {
    Write-Host "FAILED: DeviceManager 中缺少 CleanupDuplicateHyperXDevices 逻辑" -ForegroundColor Red
    exit 1
}

$mfContent = [System.IO.File]::ReadAllText("$ProjectDir\src\UI\Forms\MainForm.cs", [System.Text.Encoding]::UTF8)
if (-not $mfContent.Contains("HyperXDeviceHelper.LoadHardwareCache()")) {
    Write-Host "FAILED: MainForm 中缺少 HyperX 离线缓存预加载调用" -ForegroundColor Red
    exit 1
}

$mbiContent = [System.IO.File]::ReadAllText("$ProjectDir\src\Models\MouseBatteryInfo.cs", [System.Text.Encoding]::UTF8)
if (-not $mbiContent.Contains("HyperX")) {
    Write-Host "FAILED: MouseBatteryInfo 中缺少 HyperX 品牌色彩配置" -ForegroundColor Red
    exit 1
}

Write-Host "PASSED: HyperX 驱动已深度无缝融入灵脉硬件管理器与 UI 生命周期" -ForegroundColor Green
Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " 极度未知 (HyperX) 飓风3 无线耳机测试全部通过！" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
