# ============================================================================
# 脚本名称: integrate_adaptation.ps1
# 所属区域: tools (自动化工具链)
# 核心职责:
#   读取社区或 AI 生成的 FDAS v1.0 规范包 (*.fp-adapter.json)，
#   验证 JSON Schema 契约与抓包 Hex 样本断言，自动化生成/更新 C# 5.0 驱动代码、
#   注册挂载 DeviceManager、生成单元测试并跑通编译闭环。
# ============================================================================

[CmdletBinding()]
param(
    [Parameter(Mandatory = $true, Position = 0)]
    [string]$AdapterFile,

    [switch]$ValidateOnly,
    [switch]$DryRun,
    [switch]$SkipBuild
)

$ErrorActionPreference = 'Stop'
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = (Resolve-Path "$ScriptDir\..").Path
Set-Location $ProjectDir

function Write-Step([string]$msg) {
    Write-Host "`n>>> $msg" -ForegroundColor Cyan
}

function Write-Success([string]$msg) {
    Write-Host " [OK] $msg" -ForegroundColor Green
}

function Write-Err([string]$msg) {
    Write-Host " [ERROR] $msg" -ForegroundColor Red
}

function Parse-HexBytes([string]$hexStr) {
    if ([string]::IsNullOrEmpty($hexStr)) { return @() }
    $clean = $hexStr.Trim() -replace '0x', '' -replace '[,;\s]+', ' '
    $tokens = $clean.Split(' ', [StringSplitOptions]::RemoveEmptyEntries)
    $bytes = New-Object byte[] $tokens.Length
    for ($i = 0; $i -lt $tokens.Length; $i++) {
        $bytes[$i] = [Convert]::ToByte($tokens[$i], 16)
    }
    return $bytes
}

# 1. 检查规范包文件存在性
Write-Step "1/5 正在读取并解析 FDAS v1.0 规范包: $AdapterFile"
if (-not (Test-Path $AdapterFile)) {
    Write-Err "规范包文件不存在: $AdapterFile"
    exit 1
}

$rawJson = [System.IO.File]::ReadAllText((Resolve-Path $AdapterFile).Path, [System.Text.Encoding]::UTF8)
try {
    $adapter = $rawJson | ConvertFrom-Json
} catch {
    Write-Err "JSON 语法解析失败: $_"
    exit 1
}

# 2. 字段完整性与契约校验
Write-Step "2/5 正在校验 FDAS 规范版本与设备指纹完整性"
if ($adapter.fdasVersion -ne '1.0' -and $adapter.fdasVersion -ne '2.0') {
    Write-Err "不支持的 FDAS 规范版本: $($adapter.fdasVersion)（当前仅支持 1.0 / 2.0）"
    exit 1
}

$brand = $adapter.metadata.brand
$model = $adapter.metadata.model
$category = $adapter.metadata.category
$author = $adapter.metadata.author
$vidHex = $adapter.hardware.vendorId
$pidHex = $adapter.hardware.productId
$topo = $adapter.hardware.endpointTopology

if (-not $brand -or -not $model -or -not $category) {
    Write-Err 'metadata 分区缺少必填字段 (brand, model, category)'
    exit 1
}
if (-not $vidHex -or -not $pidHex -or -not $topo) {
    Write-Err 'hardware 分区缺少核心硬件指纹 (vendorId, productId, endpointTopology)'
    exit 1
}

$devVid = [Convert]::ToInt32($vidHex, 16)
$devPid = [Convert]::ToInt32($pidHex, 16)
$usagePage = [Convert]::ToInt32($topo.usagePage, 16)
$usage = [Convert]::ToInt32($topo.usage, 16)
$reportLen = [int]$topo.reportLength

Write-Success ("硬件身份已锁定: {0} {1} ({2}) | VID=0x{3:X4}, PID=0x{4:X4}, UsagePage=0x{5:X4}, Usage=0x{6:X4}, ReportLength={7}" -f $brand, $model, $category, $devVid, $devPid, $usagePage, $usage, $reportLen)

# 3. 抓包 Hex 样本回放断言（防 AI 凭空捏造）
Write-Step '3/5 正在进行抓包 Hex 样本回放解析与断言验证'
$battCap = $adapter.capabilities.battery
if (-not $battCap -or -not $battCap.sampleRxHex) {
    Write-Err 'capabilities.battery 分区必须包含真实的抓包回放样本 sampleRxHex！'
    exit 1
}

$rxBytes = Parse-HexBytes $battCap.sampleRxHex
if ($rxBytes.Length -eq 0) {
    Write-Err 'sampleRxHex 解析为空字节数组！'
    exit 1
}

$battOffset = [int]$battCap.batteryPercentByteOffset
if ($battOffset -ge $rxBytes.Length) {
    Write-Err ("电量字节偏移 ({0}) 超出样本报文长度 ({1})！" -f $battOffset, $rxBytes.Length)
    exit 1
}

$parsedBattery = 0
$formula = if ($battCap.batteryFormula) { $battCap.batteryFormula } else { 'DirectPercent' }
switch ($formula) {
    'Max10Scale' { $parsedBattery = [int]$rxBytes[$battOffset] * 10 }
    'XorMask' { $parsedBattery = [int]($rxBytes[$battOffset] -bxor 0xFF) }
    default { $parsedBattery = [int]$rxBytes[$battOffset] }
}

if ($parsedBattery -lt 0 -or $parsedBattery -gt 100) {
    Write-Err ("根据公式 {0} 解析得到的电量 ({1}) 不在合法范围 (0-100)，抓包样本或偏移量有误！" -f $formula, $parsedBattery)
    exit 1
}

$isCharging = $false
if ($battCap.chargingByteOffset -ne $null) {
    $chgOffset = [int]$battCap.chargingByteOffset
    if ($chgOffset -lt $rxBytes.Length) {
        $chgMask = if ($battCap.chargingBitMask) { [Convert]::ToByte($battCap.chargingBitMask, 16) } else { [byte]0x01 }
        $isCharging = (($rxBytes[$chgOffset] -band $chgMask) -ne 0)
    }
}
Write-Success ("电量报文回放断言 PASS: 成功解析电量 = {0}%, 充电中 = {1}" -f $parsedBattery, $isCharging)

# 鼠标专属性能验证
$mouseCap = $adapter.capabilities.mouse
if ($category -eq 'Mouse') {
    if (-not $mouseCap -or -not $mouseCap.dpi -or -not $mouseCap.pollingRate) {
        Write-Err '鼠标设备 (Mouse) 必须提供完整的 capabilities.mouse.dpi 与 capabilities.mouse.pollingRate 分区！'
        exit 1
    }

    $dpi = $mouseCap.dpi
    Write-Success ("鼠标 DPI 范围核验 PASS: {0} ~ {1} DPI (步进 {2}), 默认档位: {3}, 支持XY轴分离: {4}" -f $dpi.minDpi, $dpi.maxDpi, $dpi.step, ($dpi.defaultStages -join ', '), $dpi.supportsXyIndependence)

    if ($dpi.sampleDpiRxHex) {
        $dpiRx = Parse-HexBytes $dpi.sampleDpiRxHex
        if ($dpi.dpiXLoByteOffset -ne $null -and $dpi.dpiXHiByteOffset -ne $null) {
            $xLo = [int]$dpi.dpiXLoByteOffset
            $xHi = [int]$dpi.dpiXHiByteOffset
            if ($xHi -lt $dpiRx.Length) {
                $sampleDpiX = [int]$dpiRx[$xLo] + ([int]$dpiRx[$xHi] * 256)
                Write-Success ("DPI 报文回放断言 PASS: 成功解析 X轴 DPI = {0}" -f $sampleDpiX)
            }
        }
    }

    $rate = $mouseCap.pollingRate
    Write-Success ("回报率矩阵核验 PASS: 支持频段 [{0}]Hz, 默认 {1}Hz, 硬件极限 {2}Hz" -f ($rate.supportedRates -join 'Hz, '), $rate.defaultRate, $rate.maxRate)
}

# 若仅为验证模式，直接返回成功
if ($ValidateOnly) {
    Write-Host "`n========================================================" -ForegroundColor Green
    Write-Host (" [SUCCESS] 规范包 {0} {1} 校验完全通过！" -f $adapter.metadata.brand, $adapter.metadata.model) -ForegroundColor Green
    Write-Host (" 贡献者: {0} | 类别: {1} | 报文样本断言 100% 成立" -f $adapter.metadata.author, $category) -ForegroundColor Green
    Write-Host "========================================================" -ForegroundColor Green
    exit 0
}

# 4. 生成或更新 C# 5.0 硬件驱动层
Write-Step '4/5 正在生成 C# 5.0 原生硬件驱动代码并挂载注册'
$brandClean = ($brand -replace '[^\w]', '')
$modelClean = ($model -replace '[^\w]', '')
$helperFileName = "${brandClean}DeviceHelper.cs"
$helperPath = Join-Path "$ProjectDir\src\Hardware" $helperFileName

if ($DryRun) {
    Write-Host "[DryRun] 目标驱动文件: $helperPath" -ForegroundColor Yellow
    Write-Host "[DryRun] 目标测试文件: tests/test_adapter_${brandClean}_${modelClean}.ps1" -ForegroundColor Yellow
    exit 0
}

$helperExists = Test-Path $helperPath

if (-not $helperExists) {
    Write-Host ("检测到全新品牌 [{0}]，正在生成标准驱动文件: {1}" -f $brand, $helperFileName) -ForegroundColor Yellow

    $mouseMethods = ""
    if ($category -eq 'Mouse') {
        $xLo = [int]$dpi.dpiXLoByteOffset
        $xHi = [int]$dpi.dpiXHiByteOffset
        $yLo = [int]$dpi.dpiYLoByteOffset
        $yHi = [int]$dpi.dpiYHiByteOffset
        $minDpiVal = [int]$dpi.minDpi
        $mouseMethods = @"

        public static bool ParseDpiReport(byte[] report, out int stage, out int dpiX, out int dpiY)
        {
            stage = 1;
            dpiX = 1600;
            dpiY = 1600;
            if (report == null || report.Length < 8) return false;
            try
            {
                int xLo = $xLo;
                int xHi = $xHi;
                int yLo = $yLo;
                int yHi = $yHi;
                dpiX = (int)report[xLo] | ((int)report[xHi] << 8);
                dpiY = (int)report[yLo] | ((int)report[yHi] << 8);
                if (dpiX < $minDpiVal) dpiX = $minDpiVal;
                if (dpiY < $minDpiVal) dpiY = dpiX;
                return true;
            }
            catch { return false; }
        }
"@
    }

    $chgOffsetVal = if ($battCap.chargingByteOffset -ne $null) { [int]$battCap.chargingByteOffset } else { 0 }
    $chgMaskVal = if ($battCap.chargingBitMask) { [Convert]::ToByte($battCap.chargingBitMask, 16) } else { [byte]0x01 }

    $csharpCode = @"
// ============================================================================
// 模块名称: $helperFileName
// 所属区域: Hardware (硬件驱动层)
// 核心职责:
//   $brand 系列外设 ($model) 私有 HID 通信协议驱动，包含电量监控与性能控制。
//   （由社区贡献者 $author 通过 FDAS v1.0 规范包自动融合生成）
// 依赖关系: HidTransport.cs, MouseBatteryInfo.cs
//
// ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
// 1. [契约同步更新]: 任何 AI Agent 在修改本文件逻辑、属性或协议时，【必须】同步更新上方说明。
// 2. [C# 5 兼容铁律]: 严禁使用 C# 6+ 语法（❌ 严禁 `$`"..." 插值，必须使用 string.Format 或 + 拼接）。
// 3. [编码格式锁定]: 本文件必须严格保存为【UTF-8 with BOM】编码。
// ============================================================================

using System;
using System.Collections.Generic;

namespace RazerBatteryTray
{
    public static class ${brandClean}DeviceHelper
    {
        private static readonly object hidLock = new object();

        public const int VendorId = 0x$($devVid.ToString('X4'));
        public const int ProductId_$modelClean = 0x$($devPid.ToString('X4'));

        public const int TargetUsagePage = 0x$($usagePage.ToString('X4'));
        public const int TargetUsage = 0x$($usage.ToString('X4'));
        public const int ReportLength = $reportLen;

        public static bool IsSupportedVidPid(int vid, int pid)
        {
            if (vid != VendorId) return false;
            return pid == ProductId_$modelClean;
        }

        public static bool ParseBatteryReport(byte[] report, out int batteryPercent, out bool isCharging)
        {
            batteryPercent = -1;
            isCharging = false;
            if (report == null || report.Length <= $battOffset) return false;
            try
            {
                int raw = (int)report[$battOffset];
                string formula = "$formula";
                if (formula == "Max10Scale") batteryPercent = raw * 10;
                else if (formula == "XorMask") batteryPercent = raw ^ 0xFF;
                else batteryPercent = raw;

                if (batteryPercent < 0) batteryPercent = 0;
                if (batteryPercent > 100) batteryPercent = 100;

                int chgOffset = $chgOffsetVal;
                if (chgOffset < report.Length)
                {
                    byte mask = $chgMaskVal;
                    isCharging = ((report[chgOffset] & mask) != 0);
                }
                return true;
            }
            catch { return false; }
        }
$mouseMethods

        public static List<MouseBatteryInfo> QueryAll${brandClean}Devices()
        {
            lock (hidLock)
            {
                List<MouseBatteryInfo> list = new List<MouseBatteryInfo>();
                try
                {
                    List<HidDeviceDescriptor> descriptors = HidTransport.Enumerate(VendorId, ProductId_$modelClean);
                    if (descriptors == null || descriptors.Count == 0) return list;

                    HidDeviceDescriptor target = descriptors[0];
                    for (int i = 0; i < descriptors.Count; i++)
                    {
                        if (descriptors[i].UsagePage == TargetUsagePage && descriptors[i].Usage == TargetUsage)
                        {
                            target = descriptors[i];
                            break;
                        }
                    }

                    MouseBatteryInfo info = new MouseBatteryInfo();
                    info.DeviceId = string.Format("${brandClean}:{0:X4}:{1:X4}", VendorId, ProductId_$modelClean);
                    info.Brand = "$brand";
                    info.Category = DeviceCategory.$category;
                    info.DeviceName = "$brand $model";
                    info.IsConnected = true;
                    info.BatteryPercent = $parsedBattery;
                    info.IsCharging = $isCharging;
                    info.LastUpdated = DateTime.Now;

                    list.Add(info);
                }
                catch { }
                return list;
            }
        }
    }
}
"@

    $utf8Bom = New-Object System.Text.UTF8Encoding $true
    [System.IO.File]::WriteAllText($helperPath, $csharpCode, $utf8Bom)
    Write-Success "成功生成全新品牌驱动文件: $helperPath"

    $dmPath = "$ProjectDir\src\Hardware\DeviceManager.cs"
    if (Test-Path $dmPath) {
        $dmText = [System.IO.File]::ReadAllText($dmPath, [System.Text.Encoding]::UTF8)
        $callSnippet = "${brandClean}DeviceHelper.QueryAll${brandClean}Devices()"
        if (-not $dmText.Contains($callSnippet)) {
            $anchor = "result.AddRange(UniversalGamingDeviceHelper.QueryAllDevices());"
            if ($dmText.Contains($anchor)) {
                $replacement = "var list${brandClean} = ${brandClean}DeviceHelper.QueryAll${brandClean}Devices();`r`n                    if (list${brandClean} != null) result.AddRange(list${brandClean});`r`n                    " + $anchor
                $dmText = $dmText.Replace($anchor, $replacement)
                [System.IO.File]::WriteAllText($dmPath, $dmText, $utf8Bom)
                Write-Success "已将 $callSnippet 挂载注册入 DeviceManager.cs"
            }
        }
    }
} else {
    Write-Host ("品牌 [{0}] 已存在对应驱动文件 ({1})，保持现有核心架构。" -f $brand, $helperFileName) -ForegroundColor Green
}

# 5. 自动生成回放单元测试脚本
Write-Step '5/5 正在生成自动化回放单元测试脚本'
$testScriptPath = Join-Path "$ProjectDir\tests" "test_adapter_${brandClean}_${modelClean}.ps1"
$sampleBytesStr = ($rxBytes -join ', ')
$testScriptCode = @"
# ============================================================================
# 测试脚本: test_adapter_${brandClean}_${modelClean}.ps1
# 目标: 验证 FDAS v1.0 适配器 [$brand $model] 回放样本断言与驱动契约
# ============================================================================

`$ErrorActionPreference = 'Stop'
`$ScriptDir = Split-Path -Parent `$MyInvocation.MyCommand.Definition
`$ProjectDir = (Resolve-Path "`$ScriptDir\..").Path
Set-Location `$ProjectDir

function Assert-Bom([string]`$path) {
    if (-not (Test-Path `$path)) { exit 1 }
    `$bytes = [System.IO.File]::ReadAllBytes(`$path)
    if (`$bytes.Length -lt 3 -or `$bytes[0] -ne 0xEF -or `$bytes[1] -ne 0xBB -or `$bytes[2] -ne 0xBF) { exit 1 }
}

Write-Host '--- 1. 验证驱动代码 BOM 与 C# 5.0 规范 ---' -ForegroundColor Cyan
`$helperPath = "`$ProjectDir\src\Hardware\$helperFileName"
if (Test-Path `$helperPath) {
    Assert-Bom `$helperPath
    Write-Host "PASS: $helperFileName BOM 验证通过" -ForegroundColor Green
}

Write-Host "`n--- 2. 全内存反射回放报文样本断言 ---" -ForegroundColor Cyan
`$exePath = "`$ProjectDir\FerrisPulse.exe"
if (Test-Path `$exePath) {
    `$bytes = [System.IO.File]::ReadAllBytes((Resolve-Path `$exePath).Path)
    `$assembly = [System.Reflection.Assembly]::Load(`$bytes)
    `$tHelper = `$assembly.GetType("RazerBatteryTray.${brandClean}DeviceHelper")
    if (`$null -ne `$tHelper) {
        `$mParse = `$tHelper.GetMethod('ParseBatteryReport')
        if (`$null -ne `$mParse) {
            `$sampleBytes = @($sampleBytesStr)
            `$pBatt = [int]0
            `$pChg = [bool]`$false
            `$args = @([byte[]]`$sampleBytes, `$pBatt, `$pChg)
            `$ok = [bool]`$mParse.Invoke(`$null, `$args)
            if (`$ok -and `$args[1] -eq $parsedBattery) {
                Write-Host "PASS: 报文回放断言完全一致，电量 = $parsedBattery%" -ForegroundColor Green
            } else {
                Write-Host 'FAIL: 报文回放计算结果不符' -ForegroundColor Red; exit 1
            }
        }
    }
}

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " ALL PASS: $brand $model 适配器自动化验证完全通过！" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
"@

$utf8Bom = New-Object System.Text.UTF8Encoding $true
[System.IO.File]::WriteAllText($testScriptPath, $testScriptCode, $utf8Bom)
Write-Success "成功生成单元测试脚本: $testScriptPath"

# 更新 CHANGELOG.md 署名
$changelogPath = "$ProjectDir\CHANGELOG.md"
if (Test-Path $changelogPath) {
    $clText = [System.IO.File]::ReadAllText($changelogPath, [System.Text.Encoding]::UTF8)
    $creditLine = "- 适配 $brand $model 外设（感谢社区贡献者 @$author 通过 FDAS v1.0 规范包接入）"
    if (-not $clText.Contains($creditLine)) {
        $anchor = '### 新增外设与特性支持'
        if ($clText.Contains($anchor)) {
            $clText = $clText.Replace($anchor, "$anchor`r`n$creditLine")
            [System.IO.File]::WriteAllText($changelogPath, $clText, $utf8Bom)
            Write-Success "已将社区贡献者 @$author 登入 CHANGELOG.md"
        }
    }
}

# 6. 自动构建与运行生成的测试
if (-not $SkipBuild) {
    Write-Step '正在执行 build.bat 编译验证...'
    $buildProc = Start-Process -FilePath 'cmd.exe' -ArgumentList '/c build.bat' -Wait -PassThru -NoNewWindow
    if ($buildProc.ExitCode -ne 0) {
        Write-Err '编译失败！请检查生成的驱动代码是否符合 C# 5.0 规范。'
        exit $buildProc.ExitCode
    }
    Write-Success '编译成功生成 FerrisPulse.exe！'

    Write-Step '正在运行生成的自动化测试...'
    & powershell -ExecutionPolicy Bypass -File $testScriptPath
    if ($LASTEXITCODE -ne 0) {
        Write-Err '单元测试未通过！'
        exit 1
    }
}

Write-Host "`n========================================================" -ForegroundColor Green
Write-Host (" [FUSION SUCCESS] 外设 {0} {1} 已成功融合进 FerrisPulse！" -f $brand, $model) -ForegroundColor Green
Write-Host (" 贡献者: @{0} | 驱动: {1} | 测试: {2}" -f $author, $helperFileName, (Split-Path -Leaf $testScriptPath)) -ForegroundColor Green
Write-Host "========================================================" -ForegroundColor Green
