# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# Consolidated Rapoo Brand Test Suite (replaces 8 legacy test_rapoo_*.ps1 scripts)
# Pure in-memory reflection + offline VT3/1851/MT760 replay + sandbox UI tests; zero hardware I/O.

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
. (Join-Path $PSScriptRoot "..\tools\TestHarness.ps1")

function Assert-False([bool]$Condition, [string]$Message) { Assert-True (-not $Condition) $Message }

$asm = Load-FerrisPulseAssembly
$bfPubStatic = [System.Reflection.BindingFlags]"Public,Static"
$bfAnyStatic = [System.Reflection.BindingFlags]"Public,NonPublic,Static"
$bfNonPubInst = [System.Reflection.BindingFlags]"NonPublic,Instance"

$tRapoo = $asm.GetType("RazerBatteryTray.RapooDeviceHelper")
$tProto = $asm.GetType("RazerBatteryTray.RapooProtocol")
$t1851 = $asm.GetType("RazerBatteryTray.RapooVt1851DeviceHelper")
$tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo")
$tCat = $asm.GetType("RazerBatteryTray.DeviceCategory")

Invoke-TestCase "Rapoo.Vt1851BatteryDpiAndPollingProtocol" {
    $mBatt = $t1851.GetMethod("TryParseBatteryResponse", $bfPubStatic)
    $a1 = [object[]]@([byte[]]@(0x01, 0x01, 0x5B), 0, 0)
    Assert-True ($mBatt.Invoke($null, $a1) -and [int]$a1[1] -eq 91 -and [int]$a1[2] -eq 1) "1851 91% discharging"
    $a2 = [object[]]@([byte[]]@(0x01, 0x02, 0x64), 0, 0)
    Assert-True ($mBatt.Invoke($null, $a2) -and [int]$a2[1] -eq 100 -and [int]$a2[2] -eq 2) "1851 100% charging"
    Assert-False ($mBatt.Invoke($null, [object[]]@([byte[]]@(0x02, 0x01, 0x5B), 0, 0))) "1851 busy 0x02 rejected"

    $mDpi = $t1851.GetMethod("TryParseDpiBlock", $bfPubStatic)
    [byte[]]$dpiBlk = @(0x10,0x00,0x18,0x00,0x20,0x00,0x28,0x00,0x40,0x00,0x80,0x00,0x08,0x02,0x06,0x00,0x03,0x00,0x02,0x03)
    $dArgs = [object[]]@($dpiBlk, $null, 0, 0)
    Assert-True ($mDpi.Invoke($null, $dArgs) -and [int]$dArgs[2] -eq 7 -and [int]$dArgs[3] -eq 3) "1851 7-stage DPI metadata"
    Assert-Equal "800,1200,1600,2000,3200,6400,26000" (([int[]]$dArgs[1]) -join ",") "1851 7-stage DPI values"

    Assert-Equal ([byte]0x83) ([byte]$t1851.GetMethod("EncodePollingRate", $bfPubStatic).Invoke($null, @(8000))) "1851 encode 8000"
    Assert-Equal 8000 ([int]$t1851.GetMethod("DecodePollingRate", $bfPubStatic).Invoke($null, @([byte]0x83))) "1851 decode 0x83"
    Assert-Equal 8000 ([int]$t1851.GetMethod("DecodePollingRate", $bfPubStatic).Invoke($null, @([byte]0x81))) "1851 decode 0x81"

    $mSup = $t1851.GetMethod("IsSupportedDevice", $bfPubStatic)
    Assert-True ([bool]$mSup.Invoke($null, @(0x24AE, 0x1851))) "1851 accepted"
    Assert-False ([bool]$mSup.Invoke($null, @(0x24AE, 0x1231))) "1231 rejected"
}

Invoke-TestCase "Rapoo.Mt760OfficeProtocolAndDriver" {
    $req = [byte[]]$tRapoo.GetMethod("BuildOfficeBatteryRequest").Invoke($null, $null)
    Assert-True ($req.Length -eq 32 -and $req[0] -eq 0xBA -and $req[1] -eq 0xA5 -and $req[2] -eq 0xAA) "MT760 request frame"

    $mBatt = $tRapoo.GetMethod("TryParseOfficeBatteryReply")
    function Parse-OfficeBatt([byte[]]$b) {
        $p = [object[]]@($b, 0, $false, $false)
        $ok = [bool]$mBatt.Invoke($null, $p)
        return [pscustomobject]@{ Ok = $ok; Batt = $p[1]; Chg = $p[2]; Unknown = $p[3] }
    }
    $r89 = Parse-OfficeBatt ([byte[]]@(0x01, 0x01, 0x59))
    Assert-True ($r89.Ok -and $r89.Batt -eq 89 -and !$r89.Chg -and !$r89.Unknown) "MT760 89%"
    $rUnk = Parse-OfficeBatt ([byte[]]@(0x01, 0x02, 0x7F))
    Assert-True ($rUnk.Ok -and $rUnk.Batt -eq -1 -and $rUnk.Chg -and $rUnk.Unknown) "MT760 charging unknown level"
    Assert-False (Parse-OfficeBatt ([byte[]]@(0x02, 0x01, 0x59))).Ok "MT760 busy frame rejected"

    $mDpi = $tRapoo.GetMethod("TryParseOfficeDpiConfig")
    $dpi = New-Object byte[] 32; $dpi[0] = 0x01
    $levels = [byte[]]@(0x10, 0x14, 0x18, 0x20, 0x30, 0x40, 0x50)
    for ($i = 0; $i -lt 7; $i++) { $dpi[4 + $i * 2] = $levels[$i]; $dpi[5 + $i * 2] = 0 }
    $dpi[18] = 6; $dpi[19] = 0; $dpi[20] = 2; $dpi[21] = 0; $dpi[22] = 2; $dpi[23] = 3
    $dpi[24] = 0x10; $dpi[25] = 0; $dpi[26] = 0x14; $dpi[27] = 0
    $dpArgs = [object[]]@($dpi.PSObject.BaseObject, $null, 0, 0)
    Assert-True ([bool]$mDpi.Invoke($null, $dpArgs)) "MT760 DPI config"
    Assert-True ([int]$dpArgs[2] -eq 7 -and [int]$dpArgs[3] -eq 3) "MT760 7-stage count/current"
    Assert-Equal "800,1000,1200,1600,2400,3200,4000" (([int[]]$dpArgs[1]) -join ",") "MT760 7-stage DPI values"

    $mRate = $tRapoo.GetMethod("TryParseOfficePollingRate")
    $rate = New-Object byte[] 32
    $rate[0] = 0x01; $rate[4] = 0x02; $rate[5] = 0x01; $rate[6] = 0xFF; $rate[7] = 0xFF; $rate[8] = 0x01; $rate[9] = 0x02; $rate[10] = 0x00; $rate[11] = 0x03
    $rArgs = [object[]]@($rate.PSObject.BaseObject, 0)
    Assert-True ([bool]$mRate.Invoke($null, $rArgs) -and [int]$rArgs[1] -eq 1000) "MT760 wireless 1000Hz"

    $caps = $tRapoo.GetMethod("IsOfficeEndpointCaps")
    Assert-True ($caps.Invoke($null, @(0x24AE, 0x1870, 0xFF00, 0x0E, 32, 32, 0))) "MT760 32/32 FF00:000E accepted"
    Assert-False ($caps.Invoke($null, @(0x24AE, 0x1870, 0xFF00, 0x02, 7, 7, 0))) "MT760 7B telemetry isolated"
    Assert-False ($caps.Invoke($null, @(0x24AE, 0x1412, 0xFF00, 0x0E, 32, 32, 0))) "Gaming PID excluded"

    $drv = [Activator]::CreateInstance($asm.GetType("RazerBatteryTray.RapooOfficeMouseDriver"))
    Assert-Equal "BuiltIn:RapooOfficeMouse" $drv.DriverId "MT760 driver ID"
    $dev = [Activator]::CreateInstance($tInfo)
    $dev.Brand = "Rapoo"; $dev.DeviceId = "Rapoo:24AE:1870"; $dev.Category = [Enum]::Parse($tCat, "Mouse")
    $drv.PopulateCapabilities($dev)
    Assert-True ($dev.Capabilities.SupportsDpi -and !$dev.Capabilities.SupportsXyIndependence -and $dev.Capabilities.MaxDpi -eq 4000) "MT760 capabilities"
}

Invoke-TestCase "Rapoo.NonBlockingQuietPeriodAndTelemetry0x07" {
    [RazerBatteryTray.RapooDeviceHelper]::SetEepromQuietPeriod(150)
    Assert-True ([RazerBatteryTray.RapooDeviceHelper]::IsEepromQuietActive()) "EEPROM quiet period active"
    $dx = 0; $dy = 0; $st = 0; $sc = 0
    Assert-False ([RazerBatteryTray.RapooDeviceHelper]::FastQueryDpi([ref]$dx, [ref]$dy, [ref]$st, [ref]$sc)) "FastQueryDpi short-circuits during quiet period"
    Start-Sleep -Milliseconds 200
    Assert-False ([RazerBatteryTray.RapooDeviceHelper]::IsEepromQuietActive()) "EEPROM quiet period expires"

    [RazerBatteryTray.RapooDeviceHelper]::CloseRapooStream()
    [RazerBatteryTray.RapooDeviceHelper]::CloseRapooStream()

    $rep = New-Object byte[] 19
    $rep[0] = 0x07; $rep[2] = 0x02; $rep[3] = 0x40; $rep[4] = 0x06; $rep[5] = 0x40; $rep[6] = 0x06
    $rep[7] = 0x02; $rep[8] = 0x33; $rep[17] = 0x05
    Assert-True ([RazerBatteryTray.RapooDeviceHelper]::ProcessTelemetryReport($rep)) "Report 0x07 parsed"
    Assert-Equal 51 ([RazerBatteryTray.RapooDeviceHelper]::CachedBatteryPercent) "Report 0x07 51%"
    Assert-Equal $true ([RazerBatteryTray.RapooDeviceHelper]::CachedIsCharging) "Report 0x07 charging"
    Assert-Equal 1600 ([RazerBatteryTray.RapooDeviceHelper]::CachedDpi) "Report 0x07 1600 DPI"
    Assert-Equal 3 ([RazerBatteryTray.RapooDeviceHelper]::CachedDpiStage) "Report 0x07 stage 3"
}

Invoke-TestCase "Rapoo.Vt3ProtocolControlsStateAndEndpoints" {
    [void]$tRapoo.GetMethod("ResetRapooLiveState", $bfAnyStatic).Invoke($null, $null)
    Assert-Equal -1 ($tRapoo.GetField("CachedBatteryPercent").GetValue($null)) "Initial battery -1"

    Assert-Equal 0 ([int]$tRapoo.GetMethod("DecodeRapooPollingRate").Invoke($null, @([byte]0xFF))) "Unknown rate code returns 0"
    foreach ($entry in (@{125=8;250=4;500=2;1000=1;2000=0x84;4000=0x82;8000=0x81}).GetEnumerator()) {
        $enc = [byte]$tRapoo.GetMethod("EncodeRapooPollingRate").Invoke($null, @([int]$entry.Key))
        Assert-Equal ([byte]$entry.Value) $enc ("Encode " + $entry.Key)
        Assert-Equal ([int]$entry.Key) ([int]$tRapoo.GetMethod("DecodeRapooPollingRate").Invoke($null, @($enc))) ("Decode " + $entry.Key)
    }

    $mValDpi = $tProto.GetMethod("IsValidDpiConfiguration")
    Assert-True ($mValDpi.Invoke($null, @(1, [int[]]@(800, 1600), [int[]]@(600, 1200)))) "Valid XY config"
    Assert-False ($mValDpi.Invoke($null, [object[]]@(0, [int[]]@(800), [int[]]@(800)))) "0 stage invalid"
    Assert-False ($mValDpi.Invoke($null, [object[]]@(1, [int[]]@(49), [int[]]@(800)))) "49 DPI invalid"
    Assert-False ([RazerBatteryTray.RapooDeviceHelper]::SetRapooPollingRate(123)) "Invalid Hz rejected"
    Assert-False ([RazerBatteryTray.RapooDeviceHelper]::SetRapooDpiStage(0)) "Stage 0 rejected"
    Assert-False ([RazerBatteryTray.RapooDeviceHelper]::SetRapooXyDpi(65536, 800, 1)) "Overflow DPI rejected"

    # TryParseStatus & ApplyStatusReport & Snapshot & Physical Reset
    $mApply = $tRapoo.GetMethod("ApplyStatusReport")
    Assert-True ([bool]$mApply.Invoke($null, [object[]]@(,[byte[]]@(7, 0x20, 3, 0x40, 6, 0x40, 6, 2, 73, 0)))) "73% charging status"
    Assert-Equal 73 ($tRapoo.GetField("CachedBatteryPercent").GetValue($null)) "Cached battery 73"
    Assert-True ([bool]$mApply.Invoke($null, [object[]]@(,[byte[]]@(7, 0x20, 3, 0x40, 6, 0x40, 6, 0, 255, 0)))) "255 battery keeps cached 73"
    Assert-Equal 73 ($tRapoo.GetField("CachedBatteryPercent").GetValue($null)) "Valid cache preserved"
    Assert-False ($tRapoo.GetField("CachedIsCharging").GetValue($null)) "Charging cleared"

    $mSnap = $tRapoo.GetMethod("BuildRapooDeviceInfo", $bfAnyStatic)
    $snap = $mSnap.Invoke($null, @([int]0x1412, $true, $false))
    Assert-True ($snap.Category.ToString() -eq "Mouse" -and $snap.Transport -eq "2.4G 无线") "VT3 snapshot"

    # SelectEndpoints preference (wired 0x4612 over 0x1412, no mixed VID/PID)
    $dType = $asm.GetType("RazerBatteryTray.HidDeviceDescriptor")
    $listType = [System.Collections.Generic.List`1].MakeGenericType($dType)
    function Add-Ep($lst, [string]$group, [int]$prodId, [int]$u, [int]$inL, [int]$outL, [int]$featL) {
        $d = [Activator]::CreateInstance($dType)
        $d.VendorId = 0x24AE; $d.ProductId = $prodId; $d.UsagePage = 0xFF00; $d.Usage = $u
        $d.InputReportLength = $inL; $d.OutputReportLength = $outL; $d.FeatureReportLength = $featL
        $d.InstanceId = ("HID\VID_24AE&PID_{0:X4}&MI_01&COL{1:X2}\8&{2}&0&{1:X4}" -f $prodId, $u, $group)
        $d.DevicePath = $d.InstanceId
        $lst.Add($d)
    }
    $eps = [Activator]::CreateInstance($listType)
    Add-Ep $eps "A" 0x1412 14 33 33 0; Add-Ep $eps "B" 0x1412 15 0 0 33
    Assert-True ($null -eq $tProto.GetMethod("SelectEndpoints").Invoke($null, [object[]]@(,$eps))) "Mixed groups rejected"
    Add-Ep $eps "A" 0x1412 15 0 0 33; Add-Ep $eps "A" 0x1412 2 19 19 0
    Add-Ep $eps "C" 0x4612 14 33 33 0; Add-Ep $eps "C" 0x4612 15 0 0 33; Add-Ep $eps "C" 0x4612 2 19 19 0
    $epSet = $tProto.GetMethod("SelectEndpoints").Invoke($null, [object[]]@(,$eps))
    Assert-True ($null -ne $epSet -and $epSet.Control.ProductId -eq 0x4612) "Wired 0x4612 takes precedence"

    # ResolveRapooProductId & ApplyConfirmedRapooDpiStage isolation
    $mResPid = $tRapoo.GetMethod("ResolveRapooProductId")
    Assert-Equal 0x1412 ($mResPid.Invoke($null, @(0x14FF, 0x1412))) "Generic receiver resolved effective PID"

    # Legacy 19-byte Report 0x07 fallback parser (v2.0.1 parity)
    $mLegacy19 = $tRapoo.GetMethod("TryParseLegacy19ByteReport", $bfAnyStatic)
    Assert-True ($null -ne $mLegacy19) "TryParseLegacy19ByteReport exists"
    $legacyBuf = New-Object byte[] 19
    $legacyBuf[0] = 0x07; $legacyBuf[3] = 0x20; $legacyBuf[4] = 0x03; $legacyBuf[7] = 2; $legacyBuf[8] = 68; $legacyBuf[9] = 1
    $legacyBuf[13] = 0x19; $legacyBuf[14] = 0x14; $legacyBuf[17] = 5
    $lArgs = [object[]]@($legacyBuf.PSObject.BaseObject, 19, 0, $false, 0, 0, 0, 0)
    $lOk = [bool]$mLegacy19.Invoke($null, $lArgs)
    Assert-True ($lOk -and $lArgs[2] -eq 68 -and $lArgs[3] -eq $true -and $lArgs[4] -eq 800 -and $lArgs[5] -eq 2 -and $lArgs[6] -eq 0x1419 -and $lArgs[7] -eq 5) "Legacy 19B 0x07 report parsed accurately"
    Assert-Equal 0x1412 ($mResPid.Invoke($null, @(0x1412, 0x1417))) "Dedicated PID kept"
}

Invoke-TestCase "Rapoo.Vt3UiRegressionsAndStageDialog" {
    $form = New-SandboxMainForm $false
    $tMain = $form.GetType()
    try {
        $info = [Activator]::CreateInstance($tInfo)
        $info.Brand = "Rapoo"; $info.DeviceId = "Rapoo:24AE:1412"; $info.DeviceName = "雷柏 VT3"
        $info.Transport = "2.4G 无线"; $info.IsConnected = $true; $info.IsSleeping = $true
        $info.BatteryPercent = 0; $info.LastUpdated = [DateTime]::Now
        $info.Dpi = 800; $info.DpiY = 600; $info.DpiStage = 1; $info.DpiStageCount = 1
        $info.DpiStages = [int[]]@(800); $info.DpiStagesY = [int[]]@(600)
        $info.Category = [Enum]::Parse($tCat, "Mouse")

        $tRapoo.GetField("CachedBatteryPercent").SetValue($null, 0)
        [void]$tMain.GetMethod("UpdateUI", $bfNonPubInst).Invoke($form, @($info, $false))
        Assert-Equal "0%" $tMain.GetField("lblBatteryBig", $bfNonPubInst).GetValue($form).Text "0% shown on sleep"

        $info.Dpi = 1600; $info.DpiY = 1600; $info.DpiStage = 4; $info.DpiStageCount = 6
        $info.DpiStages = [int[]]@(400, 800, 1200, 1600, 3200, 6400)
        $info.DpiStagesY = [int[]]@(400, 800, 1200, 1600, 3200, 6400)
        $info.PollingRate = 2000; $info.SupportedPollingRates = [int[]]@(125, 250, 500, 1000, 2000, 4000, 8000)
        $tMain.GetField("isGamingActive", $bfNonPubInst).SetValue($form, $false)
        $tMain.GetField("autoGameRateEnabled", $bfNonPubInst).SetValue($form, $false)
        $tMain.GetField("desktopPollingRate", $bfNonPubInst).SetValue($form, 1000)
        [void]$tMain.GetMethod("UpdateUI", $bfNonPubInst).Invoke($form, @($info, $false))
        $btns = $tMain.GetField("btnDpiStages", $bfNonPubInst).GetValue($form)
        Assert-True ($btns.Length -ge 6 -and $btns[5].Text -eq "6400" -and $btns[3].Selected) "6-stage VT3 DPI UI"
        Assert-Equal 2000 $tMain.GetField("desktopPollingRate", $bfNonPubInst).GetValue($form) "VT3 polling synced to desktop"

        # Rejected setter does not pollute UI state
        $tMain.GetField("selectedDevice", $bfNonPubInst).SetValue($form, $info)
        [void]$form.SetDpiFromUI(35)
        Assert-Equal 1600 $info.Dpi "Invalid DPI did not overwrite selectedDevice"
        $form.SetRapooPollingRateManual(123)
        Assert-Equal 2000 $info.PollingRate "Invalid polling rate did not overwrite selectedDevice"

        # 6-stage DpiManagementDialog preserves all 6 VT3 stages
        $dlgType = $asm.GetType("RazerBatteryTray.DpiManagementDialog")
        $capType = $asm.GetType("RazerBatteryTray.RazerDeviceHelper+RazerDpiCapability")
        $cap = [Activator]::CreateInstance($capType)
        $ctor = $dlgType.GetConstructor([Type[]]@([string], [int[]], [int[]], [bool], [double], [int], $capType, [single], [int]))
        $dlg = $ctor.Invoke([object[]]@("VT3", $info.DpiStages, $info.DpiStagesY, $false, 1.0, 3, $cap, [single]1.0, 6))
        try {
            Assert-Equal 6 $dlgType.GetField("stageValuesX", $bfNonPubInst).GetValue($dlg).Count "6-stage dialog"
        } finally {
            $dlg.Dispose()
        }
    } finally {
        $form.Dispose()
    }
}
