# ============================================================================
# 测试脚本: test_hardware_command_bus.ps1
# 目标: 验证 IHardwareDriver、HardwareDriverBase、DriverStateStore、
#       HardwareDriverRegistry 优先级热覆盖仲裁 (300>200>100)、
#       IsKnownMouseHardwarePath 与 HardwareCommandBus 统一指令分发
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = (Resolve-Path "$ScriptDir\..").Path
Set-Location $ProjectDir

$exePath = "$ProjectDir\FerrisPulse.exe"
$bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $exePath).Path)
$assembly = [System.Reflection.Assembly]::Load($bytes)

Write-Host "--- 1. 验证核心接口与总线类存在性 ---" -ForegroundColor Cyan
$tDriver = $assembly.GetType("RazerBatteryTray.IHardwareDriver")
$tBase = $assembly.GetType("RazerBatteryTray.HardwareDriverBase")
$tStore = $assembly.GetType("RazerBatteryTray.DriverStateStore")
$tReg = $assembly.GetType("RazerBatteryTray.HardwareDriverRegistry")
$tBus = $assembly.GetType("RazerBatteryTray.HardwareCommandBus")

if ($null -eq $tDriver -or $null -eq $tBase -or $null -eq $tStore -or $null -eq $tReg -or $null -eq $tBus) {
    Write-Host "FAIL: 未找到 IHardwareDriver / HardwareDriverBase / DriverStateStore / HardwareDriverRegistry / HardwareCommandBus！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: 驱动总线与状态存储五大核心类型已定义" -ForegroundColor Green

Write-Host "`n--- 2. 验证 DriverStateStore 冷启动/休眠状态持久化往返 ---" -ForegroundColor Cyan
$testKey = "UnitTest_MockDev_9999_8888"
$mSaveBatt = $tStore.GetMethod("SaveBatteryState", [System.Reflection.BindingFlags]'Public,Static')
$mLoadBatt = $tStore.GetMethod("TryLoadBatteryState", [System.Reflection.BindingFlags]'Public,Static')
$mSaveAct = $tStore.GetMethod("SaveCustomActionValue", [System.Reflection.BindingFlags]'Public,Static')
$mLoadAct = $tStore.GetMethod("TryLoadCustomActionValue", [System.Reflection.BindingFlags]'Public,Static')
$mClear = $tStore.GetMethod("ClearDeviceState", [System.Reflection.BindingFlags]'Public,Static')

$mSaveBatt.Invoke($null, @($testKey, 87, $true, "MockDevice X")) | Out-Null

$loadArgs = @($testKey, [int]0, [bool]$false, [string]"", [DateTime]::MinValue)
$ok = [bool]$mLoadBatt.Invoke($null, $loadArgs)
if (-not $ok -or $loadArgs[1] -ne 87 -or $loadArgs[2] -ne $true -or $loadArgs[3] -ne "MockDevice X") {
    Write-Host "FAIL: DriverStateStore 电池状态往返不一致！ok=$ok, batt=$($loadArgs[1])" -ForegroundColor Red
    exit 1
}

$mSaveAct.Invoke($null, @($testKey, "win_lock", 1)) | Out-Null
$actArgs = @($testKey, "win_lock", [int]0)
$actOk = [bool]$mLoadAct.Invoke($null, $actArgs)
if (-not $actOk -or $actArgs[2] -ne 1) {
    Write-Host "FAIL: DriverStateStore 自定义动作状态往返不一致！" -ForegroundColor Red
    exit 1
}
$mClear.Invoke($null, @($testKey)) | Out-Null
Write-Host "PASS: DriverStateStore 电池与自定义动作注册表缓存往返验证通过" -ForegroundColor Green

Write-Host "`n--- 3. 验证 HardwareDriverRegistry 优先级仲裁与 HardwareCommandBus 指令路由 ---" -ForegroundColor Cyan
# 动态编译两个继承自 HardwareDriverBase 的测试驱动 (Priority 100 vs Priority 300)
$provider = New-Object Microsoft.CSharp.CSharpCodeProvider
$cp = New-Object System.CodeDom.Compiler.CompilerParameters
$cp.GenerateInMemory = $true
$cp.ReferencedAssemblies.Add("System.dll") | Out-Null
$cp.ReferencedAssemblies.Add("System.Drawing.dll") | Out-Null
$cp.ReferencedAssemblies.Add((Resolve-Path $exePath).Path) | Out-Null

$testCs = @"
using System;
using System.Collections.Generic;
using RazerBatteryTray;

public class MockBuiltInDriver : HardwareDriverBase
{
    public static int LastDpiSet = 0;
    public override string DriverId { get { return "builtin.mock.9999"; } }
    public override string Brand { get { return "MockBrand"; } }
    public override int Priority { get { return 100; } }
    public override bool IsExternalPackage { get { return false; } }
    public override bool MatchesVidPid(int vid, int pid) { return vid == 0x9999 && pid == 0x0001; }
    public override bool SetDpi(MouseBatteryInfo dev, int dpiX, int dpiY, int stage)
    {
        LastDpiSet = dpiX;
        return true;
    }
}

public class MockExternalOverrideDriver : HardwareDriverBase
{
    public static int LastDpiSet = 0;
    public static int LastRateSet = 0;
    public static int LastStagesCount = 0;
    public static int LastActionVal = -1;
    public override string DriverId { get { return "ext.mock.9999"; } }
    public override string Brand { get { return "MockBrandPro"; } }
    public override int Priority { get { return 300; } }
    public override bool IsExternalPackage { get { return true; } }
    public override bool MatchesVidPid(int vid, int pid) { return vid == 0x9999 && pid == 0x0001; }
    public override bool SetDpi(MouseBatteryInfo dev, int dpiX, int dpiY, int stage)
    {
        LastDpiSet = dpiX;
        return true;
    }
    public override bool UpdateHardwareStages(MouseBatteryInfo dev, int activeStage, int[] stagesX, int[] stagesY)
    {
        LastStagesCount = stagesX != null ? stagesX.Length : 0;
        return true;
    }
    public override bool SetPollingRate(MouseBatteryInfo dev, int rateHz)
    {
        LastRateSet = rateHz;
        return true;
    }
    public override bool ExecuteCustomAction(MouseBatteryInfo dev, string actionId, int value)
    {
        if (actionId == "rgb_toggle") LastActionVal = value;
        return true;
    }
}
"@

$cr = $provider.CompileAssemblyFromSource($cp, $testCs)
if ($cr.Errors.HasErrors) {
    Write-Host "FAIL: 测试驱动编译失败: $($cr.Errors[0].ErrorText)" -ForegroundColor Red
    exit 1
}

$dBuiltIn = $cr.CompiledAssembly.CreateInstance("MockBuiltInDriver")
$dExt = $cr.CompiledAssembly.CreateInstance("MockExternalOverrideDriver")

$mReg = $tReg.GetMethod("RegisterDriver", [System.Reflection.BindingFlags]'Public,Static')
$mUnregExt = $tReg.GetMethod("UnregisterExternalDrivers", [System.Reflection.BindingFlags]'Public,Static')
$mResolve = $tReg.GetMethod("ResolveDriver", [System.Reflection.BindingFlags]'Public,Static')
$mIsMousePath = $tReg.GetMethod("IsKnownMouseHardwarePath", [System.Reflection.BindingFlags]'Public,Static')

$mReg.Invoke($null, @($dBuiltIn)) | Out-Null
$mReg.Invoke($null, @($dExt)) | Out-Null

$tDev = $assembly.GetType("RazerBatteryTray.MouseBatteryInfo")
$dev = [System.Activator]::CreateInstance($tDev)
$dev.DeviceId = "HID:9999:0001"
$dev.HardwareFingerprint = "HID:9999:0001"

$resolved = $mResolve.Invoke($null, @($dev))
if ($null -eq $resolved -or $resolved.DriverId -ne "ext.mock.9999") {
    Write-Host "FAIL: Priority 300 外部扩展包未能热覆盖 Priority 100 内置驱动！实际: $($resolved.DriverId)" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: Priority 300 外部扩展包成功热覆盖同 VID:PID 的内置驱动" -ForegroundColor Green

# 验证 RawInput 鼠标路径匹配
$pathMatch = [bool]$mIsMousePath.Invoke($null, @("\\?\hid#vid_9999&pid_0001&mi_00#7&12345"))
if (-not $pathMatch) {
    Write-Host "FAIL: IsKnownMouseHardwarePath 未识别已注册扩展包的 VID_9999&PID_0001！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: IsKnownMouseHardwarePath 成功动态识别外部扩展包硬件路径" -ForegroundColor Green

# 验证 HardwareCommandBus 分发
$mSetDpi = $tBus.GetMethod("SetDpi", [System.Reflection.BindingFlags]'Public,Static')
$mSetStages = $tBus.GetMethod("UpdateHardwareStages", [System.Reflection.BindingFlags]'Public,Static')
$mSetRate = $tBus.GetMethod("SetPollingRate", [System.Reflection.BindingFlags]'Public,Static')
$mExecAct = $tBus.GetMethod("ExecuteCustomAction", [System.Reflection.BindingFlags]'Public,Static')

$null = $mSetDpi.Invoke($null, @($dev, 3200, 3200, 3))
$null = $mSetStages.Invoke($null, @($dev, 2, [int[]]@(400, 800, 1600, 3200), [int[]]@(400, 800, 1600, 3200)))
$null = $mSetRate.Invoke($null, @($dev, 4000))
$null = $mExecAct.Invoke($null, @($dev, "rgb_toggle", 1))

$tExtType = $cr.CompiledAssembly.GetType("MockExternalOverrideDriver")
$lastDpi = [int]$tExtType.GetField("LastDpiSet").GetValue($null)
$lastRate = [int]$tExtType.GetField("LastRateSet").GetValue($null)
$lastStages = [int]$tExtType.GetField("LastStagesCount").GetValue($null)
$lastAct = [int]$tExtType.GetField("LastActionVal").GetValue($null)

if ($lastDpi -ne 3200 -or $lastRate -ne 4000 -or $lastStages -ne 4 -or $lastAct -ne 1) {
    Write-Host "FAIL: HardwareCommandBus 指令分发结果不符: DPI=$lastDpi, Rate=$lastRate, Stages=$lastStages, Act=$lastAct" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: HardwareCommandBus 成功将 SetDpi / UpdateHardwareStages / SetPollingRate / ExecuteCustomAction 路由至高优扩展包" -ForegroundColor Green

# 验证卸载外部扩展包后自动回退到内置驱动
$mUnregExt.Invoke($null, @()) | Out-Null
$resolvedFallback = $mResolve.Invoke($null, @($dev))
if ($null -eq $resolvedFallback -or $resolvedFallback.DriverId -ne "builtin.mock.9999") {
    Write-Host "FAIL: 卸载外部扩展包后未能无损回退至内置驱动！" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: 卸载外部扩展包后立即无损回退至 Priority 100 内置驱动" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " ALL PASS: HardwareCommandBus & HardwareDriverRegistry 测试通过！" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
exit 0
