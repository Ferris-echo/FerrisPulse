# tests\test_core_driver_architecture.ps1
# 整合测试套件：核心驱动架构、能力契约、HardwareCommandBus 总线、FDAS v2.0 / .fp-driver.cs 双擎扩展包、
# 外部端点归属与缓存复用、UI 零硬编码解耦、VHF 虚拟 HID 过滤、HidTransport 异步寿命、AI 提示词与探针安全锁
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "..\tools\TestHarness.ps1")

$asm = Load-FerrisPulseAssembly
$root = Get-FerrisPulseRoot
$BF = [System.Reflection.BindingFlags]"Public,NonPublic,Instance,Static"

Invoke-TestCase "1. DeviceCapabilities 与 CustomDeviceAction 模型契约" {
    $tActType = $asm.GetType("RazerBatteryTray.CustomActionType", $true)
    $tAct = $asm.GetType("RazerBatteryTray.CustomDeviceAction", $true)
    $tDev = $asm.GetType("RazerBatteryTray.MouseBatteryInfo", $true)

    $act = [Activator]::CreateInstance($tAct)
    $act.ActionId = "auto_sleep"
    $act.Title = "自动休眠"
    $act.ActionType = [Enum]::Parse($tActType, "Toggle")
    $act.ActiveText = "休眠: 开启"; $act.InactiveText = "休眠: 关闭"
    $act.BoolValue = $true
    Assert-Equal "休眠: 开启" $act.GetFormattedButtonText() "Toggle true text"
    $act.BoolValue = $false
    Assert-Equal "休眠: 关闭" $act.GetFormattedButtonText() "Toggle false text"

    $dev = [Activator]::CreateInstance($tDev)
    Assert-True ($null -ne $dev.Capabilities) "Capabilities auto-initialized"
    $dev.DriverId = "ext.futurebrand.x1"
    $dev.Capabilities.CustomActions.Add($act)
    $dev.Capabilities.ThemeColorHex = "#FF55AA"
    Assert-Equal 255 $dev.BrandColor.R "ThemeColorHex R"
    Assert-Equal 85 $dev.BrandColor.G "ThemeColorHex G"
    Assert-Equal 170 $dev.BrandColor.B "ThemeColorHex B"
}

Invoke-TestCase "2. BuiltInDrivers 能力矩阵、通用池隔离、Merge 与 HardwareCommandBus 路由仲裁" {
    $tBuiltIn = $asm.GetType("RazerBatteryTray.BuiltInDrivers", $true)
    $tBuiltIn.GetMethod("EnsureRegistered").Invoke($null, @()) | Out-Null
    $tReg = $asm.GetType("RazerBatteryTray.HardwareDriverRegistry", $true)
    $tBus = $asm.GetType("RazerBatteryTray.HardwareCommandBus", $true)
    $tStore = $asm.GetType("RazerBatteryTray.DriverStateStore", $true)
    $tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo", $true)
    $tCat = $asm.GetType("RazerBatteryTray.DeviceCategory", $true)

    $mchose = [Activator]::CreateInstance($tInfo)
    $mchose.Brand = "Mchose"; $mchose.DeviceId = "Mchose:3837:0042"; $mchose.Category = [Enum]::Parse($tCat, "Mouse")
    $tReg.GetMethod("EnsureCapabilitiesInitialized").Invoke($null, @($mchose)) | Out-Null
    Assert-True ($mchose.Capabilities.SupportsDpi -and $mchose.Capabilities.SupportsDpiStages -and $mchose.Capabilities.SupportsPollingRate -and $mchose.Capabilities.SupportsXyIndependence) "MCHOSE full caps"

    $nuphy = [Activator]::CreateInstance($tInfo)
    $nuphy.Brand = "NuPhy"; $nuphy.DeviceId = "NuPhy:WH80"; $nuphy.Category = [Enum]::Parse($tCat, "Keyboard")
    $tReg.GetMethod("EnsureCapabilitiesInitialized").Invoke($null, @($nuphy)) | Out-Null
    Assert-Equal 5 $nuphy.Capabilities.CustomActions.Count "NuPhy 5 custom actions"

    $tUniv = $asm.GetType("RazerBatteryTray.UniversalGamingDeviceHelper", $true)
    $mIsSup = $tUniv.GetMethod("IsSupportedGamingDevice")
    Assert-True (-not [bool]$mIsSup.Invoke($null, @([int]0x3837, [string]"\\?\hid#vid_3837&pid_0042", [string]"MCHOSE A5"))) "Reject 0x3837"
    Assert-True (-not [bool]$mIsSup.Invoke($null, @([int]0x1038, [string]"\\?\hid#vid_1038&pid_2232", [string]"SteelSeries"))) "Reject 0x1038"
    Assert-True ([bool]$mIsSup.Invoke($null, @([int]0x37B0, [string]"\\?\hid#vid_37b0&pid_0010", [string]"LAMZU 8K"))) "Accept LAMZU"

    # DriverStateStore roundtrip
    $tk = "UNIT_TEST:9999:0001"
    try {
        $tStore.GetMethod("SaveBatteryState").Invoke($null, @($tk, 87, $true, "MockDevice")) | Out-Null
        $la = @($tk, [int]0, [bool]$false, [string]"", [DateTime]::MinValue)
        $ok = [bool]$tStore.GetMethod("TryLoadBatteryState").Invoke($null, $la)
        Assert-True ($ok -and $la[1] -eq 87 -and $la[2]) "DriverStateStore battery roundtrip"
        $tStore.GetMethod("SaveCustomActionValue").Invoke($null, @($tk, "win_lock", 1)) | Out-Null
        $aa = @($tk, "win_lock", [int]0)
        $ok2 = [bool]$tStore.GetMethod("TryLoadCustomActionValue").Invoke($null, $aa)
        Assert-True ($ok2 -and $aa[2] -eq 1) "DriverStateStore custom action roundtrip"
    } finally {
        $tStore.GetMethod("ClearDeviceState").Invoke($null, @($tk)) | Out-Null
    }

    # Dynamic priority override + HardwareCommandBus dispatch (pure memory script loader -> zero duplicate assembly load)
    $tLoader = $asm.GetType("RazerBatteryTray.CsScriptDriverLoader", $true)
    $testCs = @"
using RazerBatteryTray;
public class MockBuiltInDriver : HardwareDriverBase {
    public override string DriverId { get { return "builtin.mock.9999"; } }
    public override string Brand { get { return "MockBrand"; } }
    public override int Priority { get { return 100; } }
    public override bool IsExternalPackage { get { return false; } }
    public override bool MatchesVidPid(int vid, int pid) { return vid == 0x9999 && pid == 0x0001; }
}
public class MockExternalOverrideDriver : HardwareDriverBase {
    public static int LastDpiSet = 0, LastRateSet = 0, LastStagesCount = 0, LastActionVal = -1;
    public override string DriverId { get { return "ext.mock.9999"; } }
    public override string Brand { get { return "MockBrandPro"; } }
    public override int Priority { get { return 300; } }
    public override bool IsExternalPackage { get { return true; } }
    public override bool MatchesVidPid(int vid, int pid) { return vid == 0x9999 && pid == 0x0001; }
    public override bool SetDpi(MouseBatteryInfo dev, int dpiX, int dpiY, int stage) { LastDpiSet = dpiX; return true; }
    public override bool UpdateHardwareStages(MouseBatteryInfo dev, int activeStage, int[] stagesX, int[] stagesY) { LastStagesCount = stagesX != null ? stagesX.Length : 0; return true; }
    public override bool SetPollingRate(MouseBatteryInfo dev, int rateHz) { LastRateSet = rateHz; return true; }
    public override bool ExecuteCustomAction(MouseBatteryInfo dev, string actionId, int value) { if (actionId == "rgb_toggle") LastActionVal = value; return true; }
}
"@
    $mockArgs = [object[]]@($testCs, "mock_bus.fp-driver.cs", $null)
    $mockDrvs = $tLoader.GetMethod("CompileDriversFromSource").Invoke($null, $mockArgs)
    Assert-True ($mockArgs[2].Count -eq 0 -and $null -ne $mockDrvs -and $mockDrvs.Count -eq 2) ("Mock driver compile: " + ($mockArgs[2] -join "; "))
    $dBuiltIn = @($mockDrvs | Where-Object { $_.DriverId -eq "builtin.mock.9999" })[0].PSObject.BaseObject
    $dExt = @($mockDrvs | Where-Object { $_.DriverId -eq "ext.mock.9999" })[0].PSObject.BaseObject
    $mUnreg = $tReg.GetMethod("UnregisterDriverInstance")
    try {
        $tReg.GetMethod("RegisterDriver").Invoke($null, @($dBuiltIn)) | Out-Null
        $tReg.GetMethod("RegisterDriver").Invoke($null, @($dExt)) | Out-Null

        $dev = [Activator]::CreateInstance($tInfo)
        $dev.DeviceId = "HID:9999:0001"; $dev.HardwareFingerprint = "HID:9999:0001"
        $res = $tReg.GetMethod("ResolveDriver").Invoke($null, @($dev))
        Assert-Equal "ext.mock.9999" $res.DriverId "External priority 300 override"
        Assert-True ([bool]$tReg.GetMethod("IsKnownMouseHardwarePath").Invoke($null, @("\\?\hid#vid_9999&pid_0001&mi_00#7&12345"))) "IsKnownMouseHardwarePath match"

        $tBus.GetMethod("SetDpi").Invoke($null, @($dev, 3200, 3200, 3)) | Out-Null
        $tBus.GetMethod("UpdateHardwareStages").Invoke($null, @($dev, 2, [int[]]@(400, 800, 1600, 3200), [int[]]@(400, 800, 1600, 3200))) | Out-Null
        $tBus.GetMethod("SetPollingRate").Invoke($null, @($dev, 4000)) | Out-Null
        $tBus.GetMethod("ExecuteCustomAction").Invoke($null, @($dev, "rgb_toggle", 1)) | Out-Null

        $tExt = $dExt.GetType()
        Assert-Equal 3200 ([int]$tExt.GetField("LastDpiSet").GetValue($null)) "Bus SetDpi"
        Assert-Equal 4000 ([int]$tExt.GetField("LastRateSet").GetValue($null)) "Bus SetPollingRate"
        Assert-Equal 4 ([int]$tExt.GetField("LastStagesCount").GetValue($null)) "Bus UpdateStages"
        Assert-Equal 1 ([int]$tExt.GetField("LastActionVal").GetValue($null)) "Bus CustomAction"
    } finally {
        $mUnreg.Invoke($null, @($dExt)) | Out-Null
        $mUnreg.Invoke($null, @($dBuiltIn)) | Out-Null
    }
}

Invoke-TestCase "3. FDAS v2.0 + CsScriptDriverLoader + BuiltInPackageExporter" {
    $tFdas = $asm.GetType("RazerBatteryTray.FdasRuntimeHelper", $true)
    $tJsonDrv = $asm.GetType("RazerBatteryTray.FdasJsonDriver", $true)
    $tLoader = $asm.GetType("RazerBatteryTray.CsScriptDriverLoader", $true)
    $tExport = $asm.GetType("RazerBatteryTray.BuiltInPackageExporter", $true)
    $tDev = $asm.GetType("RazerBatteryTray.MouseBatteryInfo", $true)

    $v2Json = @'
{
  "fdasVersion": "2.0",
  "metadata": { "brand": "ApexLab", "model": "Pro X8", "category": "Mouse", "brandColor": "#00E5FF", "author": "Test" },
  "hardware": {
    "vendorId": "0xABCD", "productId": "0x1001",
    "devices": [
      { "productId": "0x1001", "model": "Pro X8 (1K)", "transport": "2.4G 无线", "maxRate": 1000 },
      { "productId": "0x1002", "model": "Pro X8 (8K)", "transport": "2.4G 8K", "maxRate": 8000 }
    ],
    "endpointTopology": { "usagePage": "0xFF00", "usage": "0x0001", "reportType": "Feature", "reportLength": 64 }
  },
  "capabilities": {
    "battery": { "reportId": "0x08", "batteryPercentByteOffset": 6, "batteryFormula": "DirectPercent", "sampleRxHex": "08 04 00 00 00 02 58 01" },
    "mouse": { "dpi": { "minDpi": 50, "maxDpi": 42000, "step": 50, "sensorDescription": "PAW3950 Ultra" }, "pollingRate": { "supportedRates": [125, 1000, 8000] } },
    "customActions": [{ "actionId": "motion_sync", "title": "MotionSync", "type": "Toggle", "txTemplate": "08 09 {value}" }]
  },
  "protocol": { "queryBatteryTxHex": "08 04 00 00", "setDpiTxTemplate": "08 07 {stage0}", "setPollingRateTxTemplate": "08 06 {rateCode}", "checksumType": "None" }
}
'@
    $def = $tFdas.GetMethod("ParseAdapterJson").Invoke($null, @($v2Json, "virtual.fp-adapter.json"))
    Assert-True ($null -ne $def -and $def.BrandColorHex -eq "#00E5FF" -and $def.Devices.Count -eq 2 -and $def.CustomActionDefs.Count -eq 1) "ParseAdapterJson v2.0"
    $jdrv = [Activator]::CreateInstance($tJsonDrv, @($def))
    Assert-True ($jdrv.MatchesVidPid(0xABCD, 0x1001) -and $jdrv.MatchesVidPid(0xABCD, 0x1002) -and $jdrv.Priority -eq 200) "FdasJsonDriver multi-PID"
    $md = [Activator]::CreateInstance($tDev)
    $jdrv.PopulateCapabilities($md)
    Assert-True ($md.Capabilities.SupportsDpi -and $md.Capabilities.SupportsPollingRate -and $md.Capabilities.CustomActions.Count -eq 1) "FDAS capabilities populated"

    $validCs = @'
using System.Collections.Generic;
using RazerBatteryTray;
public class CustomScriptMouseDriver : HardwareDriverBase {
    public override string DriverId { get { return "script.custom.mouse"; } }
    public override string Brand { get { return "ScriptBrand"; } }
    public override bool IsExternalPackage { get { return true; } }
    public override bool MatchesVidPid(int vid, int pid) { return vid == 0xDCBA && pid == 0x2001; }
    public override List<MouseBatteryInfo> QueryDevices() {
        MouseBatteryInfo info = new MouseBatteryInfo();
        info.DeviceId = "SCRIPT:DCBA:2001"; info.DriverId = this.DriverId; info.IsConnected = true; info.BatteryPercent = 93;
        return new List<MouseBatteryInfo> { info };
    }
}
'@
    $errList = New-Object 'System.Collections.Generic.List[string]'
    $cArgs = [object[]]@($validCs, "virt.fp-driver.cs", $errList.PSObject.BaseObject)
    $cDrvs = $tLoader.GetMethod("CompileDriversFromSource").Invoke($null, $cArgs)
    Assert-True ($cArgs[2].Count -eq 0 -and $null -ne $cDrvs -and $cDrvs.Count -eq 1) ("CsScriptDriverLoader compile errors: " + ($cArgs[2] -join "; "))
    Assert-True ($cDrvs[0].Priority -eq 300 -and $cDrvs[0].QueryDevices()[0].BatteryPercent -eq 93) "CsScriptDriverLoader priority & query"

    $badArgs = [object[]]@("broken c# code !!!", "bad.fp-driver.cs", $null)
    $badDrvs = $tLoader.GetMethod("CompileDriversFromSource").Invoke($null, $badArgs)
    Assert-True ($badDrvs.Count -eq 0 -and $badArgs[2].Count -gt 0) "Broken script isolated cleanly"

    $tmpDir = Join-Path $env:TEMP ("FP_Export_" + [Guid]::NewGuid().ToString("N"))
    try {
        $cnt = [int]$tExport.GetMethod("ExportDefaultPackages").Invoke($null, @([string]$tmpDir, [bool]$true))
        Assert-True ($cnt -ge 4) "BuiltInPackageExporter exported >= 4 packages"
    } finally {
        if (Test-Path $tmpDir) { Remove-Item $tmpDir -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

Invoke-TestCase "4. 外部驱动精确端点排他归属与脚本缓存失效复用" {
    $tReg = $asm.GetType("RazerBatteryTray.HardwareDriverRegistry", $true)
    $tLoader = $asm.GetType("RazerBatteryTray.CsScriptDriverLoader", $true)
    $tDm = $asm.GetType("RazerBatteryTray.DeviceManager", $true)
    $tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo", $true)
    $tCat = $asm.GetType("RazerBatteryTray.DeviceCategory", $true)

    $source = @'
using System;
using System.Collections.Generic;
using System.Threading;
using RazerBatteryTray;
public sealed class OwnershipExample : HardwareDriverBase {
    public override string DriverId { get { return "Script:OwnershipExample"; } }
    public override string Brand { get { return "Fixture"; } }
    public override bool IsExternalPackage { get { return true; } }
    public int Calls;
    public bool Throw, EmptyClaims, ThrowClaims, NoBattery;
    public static int InFlight, MaxInFlight;
    public static void ConcurrentScans() {
        MaxInFlight = 0;
        Thread t1 = new Thread(delegate() { HardwareDriverRegistry.QueryExternalDevices(); });
        Thread t2 = new Thread(delegate() { HardwareDriverRegistry.QueryExternalDevices(); });
        t1.Start(); t2.Start(); t1.Join(); t2.Join();
    }
    public string[] ClaimedDevicePaths { get { if (ThrowClaims) throw new InvalidOperationException(); return EmptyClaims ? new string[0] : new string[] { "hid#owned20", "hid#owned7" }; } }
    public override bool MatchesVidPid(int v, int p) { return v == 0x046D && p == 0xC54D; }
    public override bool CanHandle(MouseBatteryInfo d) { return d != null && d.DeviceName == "Verified model"; }
    public override List<MouseBatteryInfo> QueryDevices() {
        int b = Interlocked.Increment(ref InFlight);
        if (b > MaxInFlight) MaxInFlight = b;
        Thread.Sleep(15); Interlocked.Decrement(ref InFlight);
        Calls++; if (Throw) throw new InvalidOperationException();
        if (NoBattery) return new List<MouseBatteryInfo>();
        return new List<MouseBatteryInfo> { new MouseBatteryInfo { DeviceId = "Logitech:046D:C54D", HardwareId = "hid#owned20", DeviceName = "Verified model", DriverId = DriverId, IsConnected = true, Category = DeviceCategory.Mouse } };
    }
}
public sealed class FallbackExample : HardwareDriverBase {
    public override string DriverId { get { return "BuiltIn:OwnershipFallback"; } }
    public override string Brand { get { return "Fixture"; } }
    public override bool MatchesVidPid(int v, int p) { return v == 0x046D && p == 0xC54D; }
}
'@
    $compArgs = [object[]]@($source, "ownership.fp-driver.cs", $null)
    $drivers = $tLoader.GetMethod("CompileDriversFromSource").Invoke($null, $compArgs)
    Assert-Equal 2 $drivers.Count "Ownership fixtures compiled"
    $ext = @($drivers | Where-Object { $_.IsExternalPackage })[0].PSObject.BaseObject
    $fb = @($drivers | Where-Object { -not $_.IsExternalPackage })[0].PSObject.BaseObject
    try {
        foreach ($d in $drivers) { $tReg.GetMethod("RegisterDriver").Invoke($null, @($d)) | Out-Null }
        $res = @($tReg.GetMethod("QueryExternalDevices").Invoke($null, @()))
        Assert-True ($ext.Calls -eq 1 -and $res.Count -eq 1) "Script queried once"
        Assert-True ([bool]$tReg.GetMethod("IsExternallyOwnedPath").Invoke($null, @("HID#OWNED7"))) "Exact sibling path owned"
        Assert-True (-not [bool]$tReg.GetMethod("IsExternallyOwnedPath").Invoke($null, @("hid#another-c54d"))) "Sibling path not escaped"

        $list = New-Object 'System.Collections.Generic.List[RazerBatteryTray.MouseBatteryInfo]'
        $mMerge = $tDm.GetMethod("MergeExternalDriverDevice")
        $rawList = $list.PSObject.BaseObject
        $rawRes0 = $res[0].PSObject.BaseObject
        $other = [Activator]::CreateInstance($tInfo)
        $other.DeviceId = "Logitech:046D:C54D"; $other.DeviceName = "Different paired model"; $other.HardwareId = "hid#another-c54d"; $other.Category = [Enum]::Parse($tCat, "Mouse")
        $list.Add($other)
        $mMerge.Invoke($null, [object[]]@($rawList, $rawRes0)) | Out-Null
        Assert-Equal 2 $list.Count "Two physical endpoints kept distinct"

        # Live USB beats sleeping RF in MergeExternalDriverDevice regardless of order
        $usb = [Activator]::CreateInstance($tInfo); $usb.DeviceId = "T:1"; $usb.PhysicalUnitId = "Unit"; $usb.IsConnected = $true; $usb.BatteryPercent = 89; $usb.IsCharging = $true
        $rf = [Activator]::CreateInstance($tInfo); $rf.DeviceId = "T:2"; $rf.PhysicalUnitId = "Unit"; $rf.IsConnected = $true; $rf.IsSleeping = $true; $rf.BatteryPercent = 70
        foreach ($rev in @($false, $true)) {
            $list.Clear()
            if ($rev) { $list.Add($rf); $mMerge.Invoke($null, [object[]]@($rawList, $usb.PSObject.BaseObject)) | Out-Null }
            else { $list.Add($usb); $mMerge.Invoke($null, [object[]]@($rawList, $rf.PSObject.BaseObject)) | Out-Null }
            Assert-True ($list.Count -eq 1 -and -not $list[0].IsSleeping -and $list[0].BatteryPercent -eq 89) "Live USB beats sleeping RF"
        }

        $ext.GetType().GetMethod("ConcurrentScans").Invoke($null, @()) | Out-Null
        Assert-Equal 1 ([int]$ext.GetType().GetField("MaxInFlight").GetValue($null)) "Concurrent scan serialized"

        # Cache reuse + invalidation by file content change even with identical timestamp
        $tmp = Join-Path $env:TEMP ("fp-cache-" + [Guid]::NewGuid().ToString("N"))
        [IO.Directory]::CreateDirectory($tmp) | Out-Null
        $cfile = Join-Path $tmp "cache.fp-driver.cs"
        $csrc = "using RazerBatteryTray; public sealed class CachedEx : HardwareDriverBase { public override string DriverId { get { return `"Script:CacheEx`"; } } public override string Brand { get { return `"CF`"; } } public override bool IsExternalPackage { get { return true; } } public int Version { get { return 1; } } }"
        $loadMethod = $tLoader.GetMethod("LoadAndRegisterScriptFiles", $BF)
        try {
            [IO.File]::WriteAllText($cfile, $csrc, (New-Object Text.UTF8Encoding($true)))
            $a1 = [object[]]@([string[]]@($cfile), $null); $r1 = $loadMethod.Invoke($null, $a1)
            $a2 = [object[]]@([string[]]@($cfile), $null); $r2 = $loadMethod.Invoke($null, $a2)
            Assert-True ([object]::ReferenceEquals($r1[0], $r2[0])) "Unchanged script reused from cache"
            $ts = [IO.File]::GetLastWriteTimeUtc($cfile)
            [IO.File]::WriteAllText($cfile, $csrc.Replace("return 1;", "return 2;"), (New-Object Text.UTF8Encoding($true)))
            [IO.File]::SetLastWriteTimeUtc($cfile, $ts)
            $a3 = [object[]]@([string[]]@($cfile), $null); $r3 = $loadMethod.Invoke($null, $a3)
            Assert-True ($r3[0].Version -eq 2 -and -not [object]::ReferenceEquals($r1[0], $r3[0])) "Same-timestamp content change invalidates cache"
            $loadMethod.Invoke($null, [object[]]@([string[]]@(), $null)) | Out-Null
        } finally {
            Remove-Item $tmp -Recurse -Force -ErrorAction SilentlyContinue
        }
    } finally {
        $tReg.GetMethod("UnregisterDriverInstance").Invoke($null, @($ext)) | Out-Null
        $tReg.GetMethod("UnregisterDriverInstance").Invoke($null, @($fb)) | Out-Null
    }
}

Invoke-TestCase "5. 微表达式引擎、FDAS 文档规范与 probe_hid_device 写保护锁" {
    $tEval = $asm.GetType("RazerBatteryTray.ExpressionEvaluator", $true)
    $d = New-Object 'System.Collections.Generic.Dictionary[string, int64]'([System.StringComparer]::OrdinalIgnoreCase)
    $d["rateCode"] = [int64]1; $d["dpi"] = [int64]1600; $d["stage0"] = [int64]2
    Assert-Equal 0x54 ([int64]$tEval.GetMethod("Evaluate").Invoke($null, @('(0x55 - $rateCode) & 0xFF', $d.PSObject.BaseObject))) "Eval parity"
    Assert-Equal 31 ([int64]$tEval.GetMethod("Evaluate").Invoke($null, @('(($dpi / 50) - 1) & 0xFF', $d.PSObject.BaseObject))) "Eval dpi"
    Assert-Equal 20 ([int64]$tEval.GetMethod("Evaluate").Invoke($null, @('0x000C + $stage0 * 4', $d.PSObject.BaseObject))) "Eval addr"

    $tpl = "08 07 {rateCode} {(0x55 - rateCode) & 0xFF}"
    Assert-Equal "08 07 01 54" ([string]$tEval.GetMethod("ExpandTemplate").Invoke($null, @($tpl, $d.PSObject.BaseObject, $null))) "ExpandTemplate"

    $specDoc = Join-Path $root "docs\ADAPTER_SPECIFICATION.md"
    Assert-True (Test-Path $specDoc) "ADAPTER_SPECIFICATION.md exists"

    # Probe safety lock blocks 0x07 write without authorization
    $probe = Join-Path $root "tools\probe_hid_device.ps1"
    $psi = New-Object System.Diagnostics.ProcessStartInfo
    $psi.FileName = "powershell.exe"
    $psi.Arguments = "-ExecutionPolicy Bypass -NoProfile -Command `"`$null = & '$probe' -VendorId 0x3554 -ProductId 0xFB3E -TxHex '08 07 00 00 00 02 01 54 00 00 00 00 00 00 00 00 00' *>&1; exit `$LASTEXITCODE`""
    $psi.UseShellExecute = $false; $psi.CreateNoWindow = $true
    $proc = [System.Diagnostics.Process]::Start($psi); $proc.WaitForExit(5000) | Out-Null
    Assert-Equal 2 $proc.ExitCode "Probe blocks 0x07 EEPROM write"
}

Invoke-TestCase "6. AI 提示词导出、向导卡片与 UI 零硬编码解耦契约" {
    $tWiz = $asm.GetType("RazerBatteryTray.AddDeviceWizardForm", $true)
    $tAiDlg = $asm.GetType("RazerBatteryTray.AiToolsDialog", $true)
    $tExportDlg = $asm.GetType("RazerBatteryTray.AiPromptExportDialog", $true)
    $tMain = $asm.GetType("RazerBatteryTray.MainForm", $true)
    $tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo", $true)
    $tCat = $asm.GetType("RazerBatteryTray.DeviceCategory", $true)

    $prompt = [string]$tWiz.GetMethod("GetAiAdaptationPrompt", $BF, $null, [Type]::EmptyTypes, $null).Invoke($null, @())
    Assert-True ($prompt.Contains("FDAS v2.0") -and $prompt.Contains(".fp-driver.cs") -and $prompt.Contains("WebHID") -and $prompt.Contains("AGENT_HANDBOOK.md")) "AI prompt contents"
    Assert-True ($prompt.Contains("ferris.echo@gmail.com") -and -not $prompt.Contains("2949225840")) "AI prompt unified author email"
    Assert-True (-not $prompt.Contains("Razer DeathAdder V4 Pro (2.4G)")) "Buggy DeathAdder V4 Pro sample removed"

    $mJson = [regex]::Match($prompt, '(?s)```json\s*(\{.*?\})\s*```')
    Assert-True $mJson.Success "Prompt Template A JSON block exists"
    $tFdas = $asm.GetType("RazerBatteryTray.FdasRuntimeHelper", $true)
    $def = $tFdas.GetMethod("ParseAdapterJson", $BF).Invoke($null, @($mJson.Groups[1].Value, "prompt_template_a.fp-adapter.json"))
    Assert-True ($null -ne $def) "Prompt Template A parses"
    Assert-Equal "2.0" $def.FdasVersion "Prompt Template A FdasVersion"
    Assert-Equal 0x373B $def.VendorId "Prompt Template A VendorId"
    Assert-Equal 0x111F $def.ProductId "Prompt Template A ProductId"
    Assert-Equal 100 $def.SampleBattery "Prompt Template A SampleBattery"

    $mCs = [regex]::Match($prompt, '(?s)```csharp\s*(.*?)\s*```')
    Assert-True $mCs.Success "Prompt Template B C# block exists"
    $tCs = $asm.GetType("RazerBatteryTray.CsScriptDriverLoader", $true)
    $csArgs = [object[]]@($mCs.Groups[1].Value, "prompt_template_b.fp-driver.cs", $null)
    $compiledDrivers = $tCs.GetMethod("CompileDriversFromSource", $BF).Invoke($null, $csArgs)
    Assert-Equal 0 $csArgs[2].Count "Prompt Template B compiles cleanly without errors"
    Assert-Equal 1 $compiledDrivers.Count "Prompt Template B emits 1 driver"

    $tDiag = $asm.GetType("RazerBatteryTray.HardwareDiagnosticService", $true)
    $tDiag.GetMethod("ClearDriverTraces", $BF).Invoke($null, @()) | Out-Null
    $tDiag.GetMethod("RecordDriverTrace", $BF).Invoke($null, @("RazerHeadset", "1532:0528", "SKIPPED: PID 未适配")) | Out-Null
    $tCustomCfg = $asm.GetType("RazerBatteryTray.CustomDeviceConfig", $true)
    $cfgObj = [Activator]::CreateInstance($tCustomCfg)
    $cfgObj.DeviceId = "HID\VID_1532&PID_0528\test"
    $cfgObj.HardwareFingerprint = "VID_1532&PID_0528"
    $cfgObj.Transport = "2.4G 无线"
    $mPromptArg = $tWiz.GetMethod("GetAiAdaptationPrompt", $BF, $null, @($tCustomCfg, [string]), $null)
    $devPrompt = [string]$mPromptArg.Invoke($null, @($cfgObj, "BlackShark V2 Pro 2020"))
    Assert-True ($devPrompt.Contains("[RazerHeadset] [1532:0528] SKIPPED: PID 未适配")) "AI prompt injects matched Driver Trace"

    $wiz = [Activator]::CreateInstance($tWiz, @($null, [float]1.0))
    try { Assert-True ($wiz.ClientSize.Height -ge 450) "Wizard height" } finally { $wiz.Dispose() }
    $ai = [Activator]::CreateInstance($tAiDlg, @([float]1.0))
    try { Assert-True ($ai.Controls.Count -ge 4) "AiToolsDialog controls" } finally { $ai.Dispose() }
    $exp = [Activator]::CreateInstance($tExportDlg)
    try { Assert-True ($exp.DeviceItems.Count -ge 1) "AiPromptExportDialog items" } finally { $exp.Dispose() }

    # UI zero-hardcoding decoupling checks
    $combinedUi = [IO.File]::ReadAllText((Join-Path $root "src\UI\Forms\MainForm.Hardware.cs"), [Text.Encoding]::UTF8) +
                  [IO.File]::ReadAllText((Join-Path $root "src\UI\Forms\MainForm.UI.cs"), [Text.Encoding]::UTF8) +
                  [IO.File]::ReadAllText((Join-Path $root "src\UI\Forms\MainForm.Tray.cs"), [Text.Encoding]::UTF8)
    foreach ($forbidden in @("RazerDeviceHelper.SetRazer", "LogitechDeviceHelper.SetLogitech", "RapooDeviceHelper.SetRapoo", "ValkyrieDeviceHelper.SetValkyrie", "MchoseMouseHelper.SetMchose")) {
        Assert-True (-not $combinedUi.Contains($forbidden)) "UI zero hardcoding: $forbidden"
    }

    $uninitForm = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($tMain)
    $mSupp = $tMain.GetMethod("IsDeviceSupportingAutoGameRate", $BF)
    $customMouse = [Activator]::CreateInstance($tInfo)
    $customMouse.Brand = "FutureBrand"; $customMouse.Category = [Enum]::Parse($tCat, "Mouse"); $customMouse.Capabilities.SupportsPollingRate = $true
    Assert-True ([bool]$mSupp.Invoke($uninitForm, @($customMouse))) "Custom mouse auto-game rate supported"
    $roMouse = [Activator]::CreateInstance($tInfo)
    $roMouse.Brand = "ReadOnlyVendor"; $roMouse.Category = [Enum]::Parse($tCat, "Mouse")
    $roMouse.Capabilities.SupportsPollingRate = $false; $roMouse.Capabilities.SupportsDpi = $false; $roMouse.Capabilities.SupportsDpiStages = $false
    Assert-True (-not [bool]$mSupp.Invoke($uninitForm, @($roMouse))) "Read-only mouse rejected"
}

Invoke-TestCase "8. 只读外设诊断快照引擎、隐私脱敏、大厂白名单识别与诊断弹窗 UI" {
    $tDiagSvc = $asm.GetType("RazerBatteryTray.HardwareDiagnosticService", $true)
    $tSnap = $asm.GetType("RazerBatteryTray.HidEndpointSnapshot", $true)
    $tGen = $asm.GetType("RazerBatteryTray.GenericHidHelper", $true)
    $tDiagDlg = $asm.GetType("RazerBatteryTray.DiagnosticReportDialog", $true)
    $tAbout = $asm.GetType("RazerBatteryTray.AboutForm", $true)

    $rawPath = '\\?\hid#vid_1532&pid_0528&mi_03&col02#8&serial123456&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030}'
    $san = [string]$tDiagSvc.GetMethod("SanitizeDevicePath").Invoke($null, @($rawPath))
    Assert-True ($san.Contains("vid_1532") -and $san.Contains("pid_0528") -and $san.Contains("mi_03") -and $san.Contains("col02") -and (-not $san.Contains("serial123456")) -and (-not $san.Contains("4d1e55b2"))) "SanitizeDevicePath removes serial/GUID"

    $tDiagSvc.GetMethod("ClearDriverTraces").Invoke($null, @()) | Out-Null
    $tDiagSvc.GetMethod("RecordDriverTrace").Invoke($null, @("RazerHeadset", "1532:0528", "SKIPPED: PID 未适配")) | Out-Null
    $traces = $tDiagSvc.GetMethod("GetRecentDriverTraces").Invoke($null, @())
    Assert-True ($traces.Count -eq 1 -and $traces[0].Contains("1532:0528") -and $traces[0].Contains("SKIPPED")) "Driver trace ring buffer"

    $epListType = [System.Collections.Generic.List`1].MakeGenericType($tSnap)
    $eps = [Activator]::CreateInstance($epListType)
    $ep = [Activator]::CreateInstance($tSnap)
    $ep.VendorId = [uint16]0x1532; $ep.ProductId = [uint16]0x0528; $ep.VidPidKey = "1532:0528"
    $ep.ProductString = "Razer BlackShark V2 Pro"; $ep.SanitizedPath = $san
    $ep.UsagePage = [uint16]0xFF00; $ep.Usage = [uint16]0x0001; $ep.InputLength = 64; $ep.OutputLength = 64; $ep.FeatureLength = 0
    $eps.Add($ep)

    $tDevInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo", $true)
    $activeListType = [System.Collections.Generic.List`1].MakeGenericType($tDevInfo)
    $act = [Activator]::CreateInstance($activeListType)
    $mFmt = $tDiagSvc.GetMethod("FormatSnapshotText", $BF)
    $txt = [string]$mFmt.Invoke($null, @("v2.3.2", "Win11", $act, $eps, $traces, 1, 2))
    Assert-True ($txt.Contains("[未匹配内置驱动]") -and $txt.Contains("1532:0528") -and $txt.Contains("过滤虚拟 HID: 1") -and $txt.Contains("过滤内置触控板: 2")) "Diagnostic report format"

    $mMajor = $tGen.GetMethod("IsMajorPeripheralVendorId", $BF)
    Assert-True ([bool]$mMajor.Invoke($null, @([uint16]0x1532)) -and [bool]$mMajor.Invoke($null, @([uint16]0x046D)) -and (-not [bool]$mMajor.Invoke($null, @([uint16]0x1234)))) "IsMajorPeripheralVendorId"

    $dlg = [Activator]::CreateInstance($tDiagDlg, @([float]1.0))
    try {
        Assert-True ($dlg.ClientSize.Width -eq 620 -and $dlg.ClientSize.Height -eq 520) "DiagnosticReportDialog size"
        $cardRep = $tDiagDlg.GetField("cardReport", $BF).GetValue($dlg)
        $scrollRep = $tDiagDlg.GetField("scrollReport", $BF).GetValue($dlg)
        $lblSt = $tDiagDlg.GetField("lblStatus", $BF).GetValue($dlg)
        $btnCp = $tDiagDlg.GetField("btnCopy", $BF).GetValue($dlg)
        Assert-True ($null -ne $scrollRep -and $scrollRep.GetType().Name -eq "ModernDarkScrollPanel") "DiagnosticReportDialog uses ModernDarkScrollPanel"
        Assert-True ($cardRep.Bottom -le $lblSt.Top -and $lblSt.Bottom -le $btnCp.Top) "DiagnosticReportDialog zero bottom overlap"
        $scrollRep.SetDiagnosticText($txt, [float]1.0)
        Add-Type -AssemblyName System.Drawing
        $bmp = New-Object System.Drawing.Bitmap(560, 320)
        try { $scrollRep.DrawToBitmap($bmp, (New-Object System.Drawing.Rectangle(0, 0, 560, 320))) } finally { $bmp.Dispose() }
    } finally { $dlg.Dispose() }

    $about = [Activator]::CreateInstance($tAbout, @([float]1.0))
    try {
        Assert-True ($null -eq $tAbout.GetField("lnkDiagnostic", $BF)) "AboutForm removed lnkDiagnostic"
        Assert-True ($null -eq $tAbout.GetField("lnkChangelogHistory", $BF)) "AboutForm removed lnkChangelogHistory"
        $btnCheck = $tAbout.GetField("btnCheckUpdate", $BF).GetValue($about)
        $cardUpd = $tAbout.GetField("cardUpdate", $BF).GetValue($about)
        $lnkGit = $tAbout.GetField("lnkGithub", $BF).GetValue($about)
        $lnkReset = $tAbout.GetField("lnkResetApp", $BF).GetValue($about)
        Assert-True ($null -ne $btnCheck -and $null -ne $cardUpd -and $cardUpd.Controls.Contains($btnCheck)) "AboutForm unified btnCheckUpdate inside cardUpdate"
        Assert-True ($about.ClientSize.Width -eq 600 -and $about.ClientSize.Height -eq 668) "AboutForm keeps invariant 600x668 size"
        Assert-True ($lnkReset.Top -gt $lnkGit.Top) "AboutForm footer reset link below GitHub"
    } finally { $about.Dispose() }
}

Invoke-TestCase "7. VHF 虚拟 HID 过滤与 HidTransport 重叠 I/O 取消寿命" {
    $tVhf = $asm.GetType("RazerBatteryTray.VirtualHidFilter", $true)
    Assert-True ([bool]$tVhf.GetMethod("IsVirtualHidPath").Invoke($null, @('HID\HID_DEVICE_SYSTEM_VHF&COL01\2&88D67F4&0&0000'))) "VHF path rejected"
    Assert-True (-not [bool]$tVhf.GetMethod("IsVirtualHidPath").Invoke($null, @('HID\VID_249A&PID_5C2F&MI_02\7&2955FD3&0&0000'))) "Physical Acer path allowed"

    Add-Type -TypeDefinition @"
using System;
using System.Runtime.InteropServices;
public static class FpPipeHelper {
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr CreateNamedPipe(string lpName, uint dwOpenMode, uint dwPipeMode, uint nMaxInstances, uint nOutBufferSize, uint nInBufferSize, uint nDefaultTimeOut, IntPtr lpSecurityAttributes);
    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);
}
"@
    $hid = $asm.GetType("RazerBatteryTray.HidTransport", $true)
    $readR = $hid.GetMethod("ReadReport", [System.Reflection.BindingFlags]"Public,Static", $null, @([IntPtr], [int], [int], [byte[]].MakeByRefType()), $null)
    $writeR = $hid.GetMethod("WriteReport", [System.Reflection.BindingFlags]"Public,Static")
    $pipe = "\\.\pipe\FerrisPulse-test-" + [Guid]::NewGuid().ToString("N")
    $h = [FpPipeHelper]::CreateNamedPipe($pipe, 0x40000003, 0, 1, 4096, 4096, 1000, [IntPtr]::Zero)
    $cli = New-Object IO.Pipes.NamedPipeClientStream('.', $pipe.Replace("\\.\pipe\", ""), [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::Asynchronous)
    try {
        $cli.Connect(1000)
        for ($i = 0; $i -lt 5; $i++) {
            $p = [object[]]@($h, 64, 5, $null)
            Assert-True (-not [bool]$readR.Invoke($null, $p)) "Empty pipe unreadable"
            [GC]::Collect()
        }
        [byte[]]$pay = @(4, 1, 26)
        $wt = $cli.WriteAsync($pay, 0, 3)
        $p = [object[]]@($h, 64, 200, $null)
        Assert-True ([bool]$readR.Invoke($null, $p)) "Read after cancel succeeds"
        Assert-True ($wt.Wait(500) -and $p[3].Length -eq 3 -and $p[3][2] -eq 26) "Exact unpadded length"
    } finally {
        try { $cli.Dispose() } catch {}
        [FpPipeHelper]::CloseHandle($h) | Out-Null
    }
}

Invoke-TestCase "8. AddDeviceWizardForm 跨驱动前缀去重与高优先级包覆盖" {
    $tWiz = $asm.GetType("RazerBatteryTray.AddDeviceWizardForm", $true)
    $tCfg = $asm.GetType("RazerBatteryTray.CustomDeviceConfig", $true)
    $tList = [System.Collections.Generic.List`1].MakeGenericType($tCfg)
    $list = [Activator]::CreateInstance($tList)
    $mMerge = $tWiz.GetMethod("MergeScannedConfig", $BF)

    $vgn = [Activator]::CreateInstance($tCfg)
    $vgn.DeviceId = "VGN:3554:FB3E"
    $vgn.OriginalName = "VGN Dragonfly F2 Pro Max"
    $vgn.CustomName = "蜻蜓 F2 Pro Max"
    $vgn.HardwareFingerprint = "VGN:3554:FB3E"

    $fdas = [Activator]::CreateInstance($tCfg)
    $fdas.DeviceId = "FDAS:3554:FB3E"
    $fdas.OriginalName = "VGN Dragonfly F2 Pro Max"
    $fdas.CustomName = ""
    $fdas.HardwareFingerprint = "HID:3554:FB3E"

    $hidDup = [Activator]::CreateInstance($tCfg)
    $hidDup.DeviceId = "HID:3554:FB3E"
    $hidDup.OriginalName = "VGN Dragonfly F2 Pro Max"
    $hidDup.HardwareFingerprint = "HID:3554:FB3E"

    $mMerge.Invoke($null, @($list, $vgn, $false)) | Out-Null
    $mMerge.Invoke($null, @($list, $fdas, $true)) | Out-Null
    $mMerge.Invoke($null, @($list, $hidDup, $false)) | Out-Null

    Assert-Equal 1 $list.Count "Built-in VGN + FDAS + HID same VID:PID deduplicated into 1 wizard item"
    Assert-Equal "FDAS:3554:FB3E" $list[0].DeviceId "Higher-priority FDAS package overrides built-in entry"
    Assert-Equal "蜻蜓 F2 Pro Max" $list[0].CustomName "Nickname preserved across priority override"

    $logi1 = [Activator]::CreateInstance($tCfg)
    $logi1.DeviceId = "Logitech:046D:C52B:1"
    $logi1.OriginalName = "Logitech Slot 1"
    $logi2 = [Activator]::CreateInstance($tCfg)
    $logi2.DeviceId = "Logitech:046D:C52B:2"
    $logi2.OriginalName = "Logitech Slot 2"

    $listLogi = [Activator]::CreateInstance($tList)
    $mMerge.Invoke($null, @($listLogi, $logi1, $false)) | Out-Null
    $mMerge.Invoke($null, @($listLogi, $logi2, $false)) | Out-Null
    Assert-Equal 2 $listLogi.Count "Distinct multi-slot receiver channels remain separate"
}

Invoke-TestCase "9. 全品牌高刷鼠标防卡死契约 (零越界强开/零盲发 EP0/有界 Drain/启动避让)" {
    # 1. UniversalGamingDeviceHelper: excludes Razer(0x1532) & dedicated VIDs, checks FeatureReportByteLength before ProductString, ATK filters vid_373b first
    $ugSrc = [IO.File]::ReadAllText((Join-Path $root "src\Hardware\UniversalGamingDeviceHelper.cs"), [Text.Encoding]::UTF8)
    Assert-True ($ugSrc.Contains("IsDedicatedOtherBrandOrInternalVid")) "UniversalGaming defines dedicated VID filter"
    Assert-True ($ugSrc.Contains("case 0x1532:")) "UniversalGaming excludes Razer VID 0x1532"
    $qGamingIdx = $ugSrc.IndexOf("public static List<MouseBatteryInfo> QueryAllGamingDevices()")
    $qGamingBody = $ugSrc.Substring($qGamingIdx, $ugSrc.IndexOf("private static List<string> EnumerateHidInterfacePaths()") - $qGamingIdx)
    Assert-True (-not $qGamingBody.Contains("GENERIC_READ | GENERIC_WRITE")) "UniversalGaming probes with FILE_READ_ATTRIBUTES (0) instead of GENERIC_READ|WRITE"
    Assert-True ($qGamingBody.IndexOf("caps.FeatureReportByteLength == 0") -lt $qGamingBody.IndexOf("HidD_GetProductString")) "UniversalGaming filters empty FeatureReport before ProductString"
    $qAtkIdx = $ugSrc.IndexOf("private static void QueryAtkOutputEndpointDevices")
    $qAtkBody = $ugSrc.Substring($qAtkIdx)
    Assert-True ($qAtkBody.IndexOf("vid_373b") -gt 0 -and $qAtkBody.IndexOf("vid_373b") -lt $qAtkBody.IndexOf("CreateFileW")) "ATK 0xFF02 scan pre-filters vid_373b before CreateFileW"

    # 2. AppleDeviceHelper: pre-filters Apple/05AC/004C paths before CreateFile and opens with 0 access (not GENERIC_WRITE)
    $apSrc = [IO.File]::ReadAllText((Join-Path $root "src\Hardware\AppleDeviceHelper.cs"), [Text.Encoding]::UTF8)
    $qApIdx = $apSrc.IndexOf("// 4. 扫描 USB 或蓝牙 HID 直连的 Apple Magic 键鼠设备")
    $qApBody = $apSrc.Substring($qApIdx)
    Assert-True ($qApBody.IndexOf("vid_05ac") -gt 0 -and $qApBody.IndexOf("vid_05ac") -lt $qApBody.IndexOf("CreateFile(")) "Apple HID scan pre-filters vid_05ac/vid_004c before CreateFile"
    Assert-True (-not $qApBody.Contains("0x40000000, // GENERIC_WRITE")) "Apple HID scan does not open all interfaces with GENERIC_WRITE"

    # 3. HidTransport: GetCaps before ProductString, keeps GANSS identity, skips other pure input collections
    $htSrc = [IO.File]::ReadAllText((Join-Path $root "src\Hardware\HidTransport.cs"), [Text.Encoding]::UTF8)
    $htEnumIdx = $htSrc.IndexOf("public static List<HidDeviceDescriptor> Enumerate")
    $htEnumBody = $htSrc.Substring($htEnumIdx, $htSrc.IndexOf("public static HIDP_CAPS GetCaps") - $htEnumIdx)
    Assert-True ($htEnumBody.IndexOf("GetCaps(handle)") -lt $htEnumBody.IndexOf("HidD_GetProductString")) "HidTransport calls GetCaps before ProductString"
    Assert-True ($htEnumBody.Contains("attributes.VendorID == 0x05AC && attributes.ProductID == 0x024F") -and $htEnumBody.Contains("!isGanssKeyboardIdentity &&")) "HidTransport preserves GANSS keyboard product identity while skipping other pure input collections"
    Assert-True (-not $htEnumBody.Contains("HidD_GetManufacturerString") -and -not $htEnumBody.Contains("HidD_GetSerialNumberString")) "HidTransport removes unused Manufacturer/Serial EP0 transfers"

    # 4. VgnDeviceHelper: bounded drain loops + 0-access 0xFF02 check before GENERIC_READ|WRITE
    $vgnSrc = [IO.File]::ReadAllText((Join-Path $root "src\Hardware\VgnDeviceHelper.cs"), [Text.Encoding]::UTF8)
    Assert-True (-not $vgnSrc.Contains("while (true)")) "VgnDeviceHelper has no unbounded while(true) drain loops"
    Assert-True ($vgnSrc.Contains("probeHandle")) "VgnDeviceHelper probes 0xFF02 endpoint with 0 access before GENERIC_READ|WRITE"

    # 5. LogitechDeviceHelper: pid_0aba pre-filter before GENERIC_READ|WRITE & isHidppEndpoint before ProductString
    $lgSrc = [IO.File]::ReadAllText((Join-Path $root "src\Hardware\LogitechDeviceHelper.cs"), [Text.Encoding]::UTF8)
    $qProXIdx = $lgSrc.IndexOf("public static MouseBatteryInfo QueryLogitechProXHeadset()")
    $qProXBody = $lgSrc.Substring($qProXIdx, $lgSrc.IndexOf("public static List<MouseBatteryInfo> QueryAllLogitechDevices()") - $qProXIdx)
    Assert-True ($qProXBody.IndexOf("pid_0aba") -gt 0 -and $qProXBody.IndexOf("pid_0aba") -lt $qProXBody.IndexOf("CreateFile(")) "Logitech ProX scan filters pid_0aba before opening RW handle"
    $qLgIdx = $lgSrc.IndexOf("bool isHidppEndpoint =")
    $qLgProdIdx = $lgSrc.IndexOf("HidD_GetProductString(handle, sb, sb.Capacity)", $lgSrc.IndexOf("QueryAllLogitechDevices"))
    Assert-True ($qLgIdx -gt 0 -and $qLgProdIdx -gt $qLgIdx) "Logitech QueryAllLogitechDevices calls ProductString only after confirming HID++ endpoint"

    # 6. RazerDeviceHelper FastQueryDpi cooldown guard + MainForm WM_INPUT zero-alloc fast path
    $rzSrc = [IO.File]::ReadAllText((Join-Path $root "src\Hardware\RazerDeviceHelper.cs"), [Text.Encoding]::UTF8)
    Assert-True ($rzSrc.Contains("fastDpiCooldownUntilUtc")) "RazerDeviceHelper FastQueryDpi implements failure cooldown backoff"
    $evSrc = [IO.File]::ReadAllText((Join-Path $root "src\UI\Forms\MainForm.Events.cs"), [Text.Encoding]::UTF8)
    Assert-True ($evSrc.IndexOf("TryGetValue(hDev, out anyTargetMatched)") -lt $evSrc.IndexOf("() => ResolveRawInputDeviceName")) "HandleMouseWakeInput checks cache before allocating closure delegate"
}

Invoke-TestCase "10. 异步 Overlapped P0 安全契约 / UI 刷新合并 / GenericHid 内存安全" {
    foreach ($rel in @(
        "src\Hardware\VgnDeviceHelper.cs",
        "src\Hardware\LogitechDeviceHelper.cs",
        "src\Hardware\EndgameGearDeviceHelper.cs",
        "src\Hardware\UniversalGamingDeviceHelper.cs",
        "src\Hardware\CorsairDeviceHelper.cs",
        "src\Hardware\HyperXDeviceHelper.cs",
        "src\Hardware\SteelSeriesDeviceHelper.cs"
    )) {
        $src = [IO.File]::ReadAllText((Join-Path $root $rel), [Text.Encoding]::UTF8)
        Assert-True ($src.Contains("GCHandleType.Pinned")) ("$rel pins overlapped buffers via GCHandleType.Pinned")
        $cancelIdx = $src.LastIndexOf("CancelIo(handle);")
        Assert-True ($cancelIdx -gt 0) ("$rel calls CancelIo(handle)")
        $afterCancel = $src.Substring($cancelIdx, [Math]::Min(220, $src.Length - $cancelIdx))
        Assert-True ($afterCancel.Contains("GetOverlappedResult") -and $afterCancel.Contains("true)")) ("$rel waits GetOverlappedResult(..., true) after CancelIo")
    }

    $mhSrc = [IO.File]::ReadAllText((Join-Path $root "src\UI\Forms\MainForm.Hardware.cs"), [Text.Encoding]::UTF8)
    Assert-True ($mhSrc.Contains("refreshBatteryInFlight") -and $mhSrc.Contains("refreshBatteryPending")) "RefreshBatteryStatus coalesces overlapping scans"
    Assert-True ($mhSrc.Contains("var devicesSnapshot = activeDevices;")) "DpiMonitorWorker snapshots activeDevices"
    $kbSleepIdx = $mhSrc.IndexOf("// 休眠缓存不是当前遥测：保留数值与采样时间，停止充电动画。")
    Assert-True ($kbSleepIdx -gt 0) "Keyboard sleeping block marker present"
    $kbSleepSnippet = $mhSrc.Substring($kbSleepIdx, 260)
    Assert-True (-not $kbSleepSnippet.Contains("isMouseSleeping = true;")) "Keyboard sleeping path does not pollute isMouseSleeping"

    $ghSrc = [IO.File]::ReadAllText((Join-Path $root "src\Hardware\GenericHidHelper.cs"), [Text.Encoding]::UTF8)
    Assert-True ($ghSrc.Contains("if (reqSize == 0)")) "GenericHidHelper guards reqSize == 0"
    Assert-True ($ghSrc.IndexOf("MoondropDeviceHelper.IsManagedDevice") -lt $ghSrc.IndexOf("HidD_GetProductString(handle, sbProd")) "ScanGenericHidMice skips ProductString for already-managed devices"
}

Invoke-TestCase "11. FDAS v2.0 社区扩展示例契约完整性 (京造 JZ M5)" {
    $jzAdapter = Join-Path $root "examples\adapters\jz_m5.fp-adapter.json"
    Assert-True ([IO.File]::Exists($jzAdapter)) "jz_m5.fp-adapter.json exists"
    $jzRaw = [IO.File]::ReadAllBytes($jzAdapter)
    Assert-True ($jzRaw.Length -ge 3 -and $jzRaw[0] -eq 0xEF -and $jzRaw[1] -eq 0xBB -and $jzRaw[2] -eq 0xBF) "jz_m5.fp-adapter.json has UTF-8 BOM"
    $jzObj = [IO.File]::ReadAllText($jzAdapter, [Text.Encoding]::UTF8) | ConvertFrom-Json
    Assert-True ($jzObj.fdasVersion -eq "2.0" -and $jzObj.hardware.vendorId -eq "0x362D" -and $jzObj.hardware.productId -eq "0xD107") "JZ M5 FDAS v2.0 contract metadata intact"
    Assert-Equal 19 ([int]$jzObj.capabilities.battery.batteryPercentByteOffset) "JZ M5 battery offset is 19"
}

Write-Host "`nALL CORE DRIVER ARCHITECTURE TESTS PASSED." -ForegroundColor Green
exit 0
