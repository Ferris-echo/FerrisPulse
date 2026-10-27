# ============================================================================
# 测试脚本: test_dual_extension_loader.ps1
# 目标: 验证 FDAS v2.0 声明式扩展包引擎（多 PID devices 矩阵、brandColor、
#       sensorDescription、customActions、FdasJsonDriver 封装）与
#       CsScriptDriverLoader (.fp-driver.cs 纯内存动态编译引擎) 及 BuiltInPackageExporter
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = (Resolve-Path "$ScriptDir\..").Path
Set-Location $ProjectDir

$exePath = "$ProjectDir\FerrisPulse.exe"
$bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $exePath).Path)
$assembly = [System.Reflection.Assembly]::Load($bytes)

Write-Host "--- 1. 验证 FDAS v2.0 多 PID 矩阵、主题色与 CustomActions 解析 ---" -ForegroundColor Cyan
$tFdas = $assembly.GetType("RazerBatteryTray.FdasRuntimeHelper")
$tJsonDriver = $assembly.GetType("RazerBatteryTray.FdasJsonDriver")
$tScriptLoader = $assembly.GetType("RazerBatteryTray.CsScriptDriverLoader")
$tExporter = $assembly.GetType("RazerBatteryTray.BuiltInPackageExporter")

if ($null -eq $tFdas -or $null -eq $tJsonDriver -or $null -eq $tScriptLoader -or $null -eq $tExporter) {
    Write-Host "FAIL: 未找到 FdasJsonDriver / CsScriptDriverLoader / BuiltInPackageExporter 类型！" -ForegroundColor Red
    exit 1
}

$v2Json = @"
{
  "fdasVersion": "2.0",
  "metadata": {
    "brand": "ApexLab",
    "model": "Pro X8",
    "category": "Mouse",
    "brandColor": "#00E5FF",
    "author": "FerrisPulse Test"
  },
  "hardware": {
    "vendorId": "0xABCD",
    "productId": "0x1001",
    "devices": [
      { "productId": "0x1001", "model": "Pro X8 (1K 接收器)", "transport": "2.4G 无线", "maxRate": 1000 },
      { "productId": "0x1002", "model": "Pro X8 (8K 接收器)", "transport": "2.4G 8K", "maxRate": 8000 },
      { "productId": "0x1003", "model": "Pro X8 (有线模式)", "transport": "USB 有线", "maxRate": 8000 }
    ],
    "endpointTopology": {
      "usagePage": "0xFF00",
      "usage": "0x0001",
      "reportType": "Feature",
      "reportLength": 64
    }
  },
  "capabilities": {
    "battery": {
      "reportId": "0x08",
      "batteryPercentByteOffset": 6,
      "chargingByteOffset": 7,
      "chargingBitMask": "0x01",
      "batteryFormula": "DirectPercent",
      "sampleRxHex": "08 04 00 00 00 02 58 01"
    },
    "mouse": {
      "dpi": {
        "minDpi": 50,
        "maxDpi": 42000,
        "step": 50,
        "sensorDescription": "PAW3950 Ultra (50~42,000 DPI)",
        "supportsHardwareDpiMonitor": true,
        "defaultStages": [400, 800, 1600, 3200],
        "supportsXyIndependence": true
      },
      "pollingRate": {
        "supportedRates": [125, 250, 500, 1000, 2000, 4000, 8000],
        "defaultRate": 1000,
        "maxRate": 8000
      }
    },
    "customActions": [
      {
        "actionId": "motion_sync",
        "title": "MotionSync 同步",
        "type": "Toggle",
        "activeText": "⚡ MotionSync: 开启",
        "inactiveText": "⚡ MotionSync: 关闭",
        "defaultValue": 1,
        "showInTrayMenu": true,
        "txTemplate": "08 09 00 00 00 01 {value} 00"
      },
      {
        "actionId": "lod_height",
        "title": "静默高度 LOD",
        "type": "Slider",
        "minValue": 1,
        "maxValue": 3,
        "defaultValue": 1,
        "valueSuffix": "mm",
        "showInTrayMenu": false,
        "txTemplate": "08 0A 00 00 00 01 {value} 00"
      }
    ]
  },
  "protocol": {
    "queryBatteryTxHex": "08 04 00 00 00 00",
    "setDpiTxTemplate": "08 07 00 00 00 02 {stage0} 00",
    "setStagesTxTemplate": "08 0C 00 00 00 04 {dpiLow} {dpiHigh}",
    "setPollingRateTxTemplate": "08 06 00 00 00 01 {rateCode} 00",
    "checksumType": "None"
  }
}
"@

$mParse = $tFdas.GetMethod("ParseAdapterJson", [System.Reflection.BindingFlags]'Public,Static')
$def = $mParse.Invoke($null, @($v2Json, "virtual_apexlab.fp-adapter.json"))
if ($null -eq $def) {
    Write-Host "FAIL: ParseAdapterJson 解析 FDAS v2.0 失败！" -ForegroundColor Red
    exit 1
}

if ($def.BrandColorHex -ne "#00E5FF" -or $def.SensorDescription -ne "PAW3950 Ultra (50~42,000 DPI)") {
    Write-Host "FAIL: BrandColorHex 或 SensorDescription 解析异常！" -ForegroundColor Red
    exit 1
}
if ($def.Devices.Count -ne 3) {
    Write-Host "FAIL: 一对多 devices 矩阵未正确解析！实际数量=$($def.Devices.Count)" -ForegroundColor Red
    exit 1
}
if ($def.CustomActionDefs.Count -ne 2) {
    Write-Host "FAIL: customActions 未正确解析！实际数量=$($def.CustomActionDefs.Count)" -ForegroundColor Red
    exit 1
}

$jsonDrv = [System.Activator]::CreateInstance($tJsonDriver, @($def))
if (-not $jsonDrv.MatchesVidPid(0xABCD, 0x1001) -or -not $jsonDrv.MatchesVidPid(0xABCD, 0x1002) -or -not $jsonDrv.MatchesVidPid(0xABCD, 0x1003)) {
    Write-Host "FAIL: FdasJsonDriver.MatchesVidPid 未能匹配 devices 矩阵中的全部 3 个 PID！" -ForegroundColor Red
    exit 1
}
if ($jsonDrv.Priority -ne 200) {
    Write-Host "FAIL: FdasJsonDriver.Priority 预期为 200，实际=$($jsonDrv.Priority)" -ForegroundColor Red
    exit 1
}

$tDev = $assembly.GetType("RazerBatteryTray.MouseBatteryInfo")
$mockDev = [System.Activator]::CreateInstance($tDev)
$mockDev.DeviceId = "FDAS:ABCD:1002"
$jsonDrv.PopulateCapabilities($mockDev)

if (-not $mockDev.Capabilities.SupportsDpi -or -not $mockDev.Capabilities.SupportsPollingRate -or $mockDev.Capabilities.CustomActions.Count -ne 2) {
    Write-Host "FAIL: FdasJsonDriver.PopulateCapabilities 未能正确注入设备能力与自定义动作！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: FDAS v2.0 多 PID 矩阵、主题色、传感器描述与 CustomActions 契约全部验证通过" -ForegroundColor Green

Write-Host "`n--- 2. 验证 CsScriptDriverLoader (.fp-driver.cs) 纯内存编译与语法错误容灾 ---" -ForegroundColor Cyan
$validCs = @"
using System;
using System.Collections.Generic;
using RazerBatteryTray;

public class CustomScriptMouseDriver : HardwareDriverBase
{
    public override string DriverId { get { return "script.custom.mouse"; } }
    public override string Brand { get { return "ScriptMouseBrand"; } }
    public override bool IsExternalPackage { get { return true; } }
    public override bool MatchesVidPid(int vid, int pid) { return vid == 0xDCBA && pid == 0x2001; }

    public override void PopulateCapabilities(MouseBatteryInfo dev)
    {
        base.PopulateCapabilities(dev);
        dev.Capabilities.SupportsDpi = true;
        dev.Capabilities.SupportsPollingRate = true;
        dev.Capabilities.ThemeColorHex = "#33FF88";
    }

    public override List<MouseBatteryInfo> QueryDevices()
    {
        List<MouseBatteryInfo> list = new List<MouseBatteryInfo>();
        MouseBatteryInfo info = new MouseBatteryInfo();
        info.DeviceId = "SCRIPT:DCBA:2001";
        info.DriverId = this.DriverId;
        info.Brand = this.Brand;
        info.DeviceName = "Script Pro Mouse";
        info.Category = DeviceCategory.Mouse;
        info.IsConnected = true;
        info.BatteryPercent = 93;
        PopulateCapabilities(info);
        list.Add(info);
        return list;
    }
}
"@

$mCompile = $tScriptLoader.GetMethod("CompileDriversFromSource", [System.Reflection.BindingFlags]'Public,Static')
$errList = New-Object 'System.Collections.Generic.List[string]'
$compileArgs = @($validCs, "virtual_script.fp-driver.cs", $errList.psobject.BaseObject)
$compiledDrivers = $mCompile.Invoke($null, $compileArgs)

if ($null -eq $compiledDrivers -or $compiledDrivers.Count -ne 1 -or $compileArgs[2].Count -gt 0) {
    Write-Host "FAIL: CsScriptDriverLoader 未能成功内存编译有效脚本！errors=$($compileArgs[2] -join '; ')" -ForegroundColor Red
    exit 1
}

$scriptDrv = $compiledDrivers[0]
if ($scriptDrv.DriverId -ne "script.custom.mouse" -or $scriptDrv.Priority -ne 300) {
    Write-Host "FAIL: 编译出的脚本驱动属性不符: Id=$($scriptDrv.DriverId), Priority=$($scriptDrv.Priority)" -ForegroundColor Red
    exit 1
}

$queried = $scriptDrv.QueryDevices()
if ($queried.Count -ne 1 -or $queried[0].BatteryPercent -ne 93 -or $queried[0].BrandColor.G -ne 255) {
    Write-Host "FAIL: 脚本驱动 QueryDevices 执行结果不符！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: CsScriptDriverLoader 纯内存动态编译与执行通过 (Priority=300)" -ForegroundColor Green

# 验证错误语法的容灾隔离（不抛未捕获异常）
$brokenCs = "using System; public class BrokenDriver : HardwareDriverBase { syntax_error_here !!! }"
$errList2 = New-Object 'System.Collections.Generic.List[string]'
$brokenArgs = @($brokenCs, "broken.fp-driver.cs", $errList2.psobject.BaseObject)
$brokenDrivers = $mCompile.Invoke($null, $brokenArgs)
if ($brokenDrivers.Count -ne 0 -or $brokenArgs[2].Count -eq 0) {
    Write-Host "FAIL: 损坏脚本应返回空驱动列表并记录编译错误信息！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: 损坏脚本容灾隔离验证通过 (捕获诊断: $($brokenArgs[2][0]))" -ForegroundColor Green

Write-Host "`n--- 3. 验证 BuiltInPackageExporter 一键导出模板 ---" -ForegroundColor Cyan
$tempExportDir = Join-Path $env:TEMP ("FerrisPulse_TestExport_" + [Guid]::NewGuid().ToString("N"))
try {
    $mExport = $tExporter.GetMethod("ExportDefaultPackages", [System.Reflection.BindingFlags]'Public,Static')
    $exportedCount = [int]$mExport.Invoke($null, @([string]$tempExportDir, [bool]$true))
    if ($exportedCount -lt 2) {
        Write-Host "FAIL: ExportDefaultPackages 导出文件数量不足: $exportedCount" -ForegroundColor Red
        exit 1
    }
    $files = Get-ChildItem $tempExportDir
    Write-Host "PASS: BuiltInPackageExporter 成功导出 $exportedCount 个扩展包模板 ($($files.Name -join ', '))" -ForegroundColor Green
}
finally {
    if (Test-Path $tempExportDir) { Remove-Item $tempExportDir -Recurse -Force -ErrorAction SilentlyContinue }
}

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " ALL PASS: 双擎扩展包加载器 (FDAS v2.0 + C# 动态脚本) 测试通过！" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
exit 0
