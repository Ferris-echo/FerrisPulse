# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# Consolidated Logitech Brand Test Suite (replaces 8 legacy test_logitech_*.ps1 scripts)
# Pure in-memory reflection + offline protocol replay + sandbox UI tests; zero hardware I/O.

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
. (Join-Path $PSScriptRoot "..\tools\TestHarness.ps1")

$asm = Load-FerrisPulseAssembly
$root = Get-FerrisPulseRoot
$bfPubStatic = [System.Reflection.BindingFlags]"Public,Static"
$bfAnyStatic = [System.Reflection.BindingFlags]"Public,NonPublic,Static"
$bfNonPubInst = [System.Reflection.BindingFlags]"NonPublic,Instance"

$tLogi = $asm.GetType("RazerBatteryTray.LogitechDeviceHelper")
$tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo")
$tCat = $asm.GetType("RazerBatteryTray.DeviceCategory")

Invoke-TestCase "Logitech.G502XAndG304AndGpwNamingAndProtocol" {
    $mModel = $tLogi.GetMethod("GetModelName", $bfPubStatic)
    $mParseDpi = $tLogi.GetMethod("ParseAdjustableDpiReply", $bfPubStatic)

    $nameC547 = [string]$mModel.Invoke($null, @([int]0xC547, ""))
    Assert-True (-not $nameC547.Contains("SUPERLIGHT 2") -and $nameC547.Contains("接收器")) "C547 neutral fallback: $nameC547"
    Assert-True ([string]$mModel.Invoke($null, @([int]0xC53F, "USB Receiver")) -match "0xC53F") "0xC53F receiver name"
    Assert-True ([string]$mModel.Invoke($null, @([int]0x4074, "USB Receiver")) -match "G304") "0x4074 G304 name"
    Assert-True ([string]$mModel.Invoke($null, @([int]0xC095, [string]$null))).Contains("G502 X PLUS") "0xC095 G502 X PLUS"

    # FriendlyShortName differentiators
    foreach ($tc in @(
        @{ Name = "Logitech G502 X PLUS"; Exp = "G502 X PLUS" },
        @{ Name = "Logitech G502 X Wireless（无线版）"; Exp = "G502 X 无线" },
        @{ Name = "Logitech G502 X (有线版)"; Exp = "G502 X" }
    )) {
        $obj = [Activator]::CreateInstance($tInfo)
        $obj.Brand = "Logitech"; $obj.DeviceName = $tc.Name; $obj.Category = [Enum]::Parse($tCat, "Mouse")
        Assert-Equal $tc.Exp $obj.FriendlyShortName ("FriendlyShortName for " + $tc.Name)
    }

    # ParseAdjustableDpiReply (0x2201 Func 2)
    function Invoke-ParseLogiDpi([byte[]]$rx) {
        $arr = New-Object object[] 1
        if ($null -ne $rx) { $arr[0] = $rx.PSObject.BaseObject }
        return [int]$mParseDpi.Invoke($null, $arr)
    }
    $dpiRx = New-Object byte[] 20
    $dpiRx[0] = 0x11; $dpiRx[1] = 1; $dpiRx[2] = 0x1A; $dpiRx[3] = 0x28
    $dpiRx[5] = 0x0C; $dpiRx[6] = 0x80; $dpiRx[7] = 0x03; $dpiRx[8] = 0x20 # 3200 DPI
    Assert-Equal 3200 (Invoke-ParseLogiDpi $dpiRx) "0x2201 3200 DPI"
    $errRx = New-Object byte[] 20; $errRx[0] = 0x11; $errRx[1] = 1; $errRx[2] = 0xFF; $errRx[3] = 0x28
    Assert-Equal -1 (Invoke-ParseLogiDpi $errRx) "0xFF error frame"
    $zeroRx = New-Object byte[] 20; $zeroRx[0] = 0x11; $zeroRx[1] = 1; $zeroRx[2] = 0x1A; $zeroRx[3] = 0x28
    Assert-Equal -1 (Invoke-ParseLogiDpi $zeroRx) "0 DPI rejected"
    Assert-Equal -1 (Invoke-ParseLogiDpi $null) "null rejected"

    # Source contract invariants
    $logiSrc = [System.IO.File]::ReadAllText((Join-Path $root "src\Hardware\LogitechDeviceHelper.cs"), [System.Text.Encoding]::UTF8)
    Assert-True ($logiSrc.Contains("txPing[3] = 0x18;") -and $logiSrc.Contains("txPing[6] = 0x5A;")) "HID++ 2.0 ping frame 0x18/0x5A"
    foreach ($pidHex in @("0x40BD", "0x40A9", "0xC0A9", "0xC54F", "0x40B5", "0xC548")) {
        Assert-True ($logiSrc.Contains($pidHex)) "GPW family PID $pidHex present"
    }
}

Invoke-TestCase "Logitech.CacheIsolationAudioFilterAndProXAdc" {
    $mGetCache = $tLogi.GetMethod("GetCachedBatteryForDevice", $bfPubStatic)
    $fGlobal = $tLogi.GetField("CachedBatteryPercent", $bfPubStatic)
    $fValid = $tLogi.GetField("HasValidHardwareReading", $bfPubStatic)
    $fGlobal.SetValue($null, 100); $fValid.SetValue($null, $true)
    Assert-Equal -1 ([int]$mGetCache.Invoke($null, @("Logitech:046D:0AB1"))) "Uncached device cannot borrow global 100%"
    Assert-Equal -1 ([int]$mGetCache.Invoke($null, @([string]$null))) "Null device ID cannot borrow cache"

    $mAudio = $tLogi.GetMethod("IsLogitechAudioOnlyPeripheral", $bfPubStatic)
    foreach ($ac in @(
        @{ Pid = 0x0AB1; Name = "Yeti Nano"; Exp = $true },
        @{ Pid = 0x0A00; Name = "Logitech Speaker"; Exp = $true },
        @{ Pid = 0x0ABA; Name = "Logitech G PRO X Wireless 游戏耳机"; Exp = $false },
        @{ Pid = 0xC095; Name = "G502 X PLUS"; Exp = $false }
    )) {
        Assert-Equal $ac.Exp ([bool]$mAudio.Invoke($null, @([int]$ac.Pid, [string]$ac.Name))) ("Audio filter " + $ac.Name)
    }

    $mParseAdc = $tLogi.GetMethod("ParseProXAdcPayload", $bfAnyStatic)
    $mVoltPct = $tLogi.GetMethod("ProXVoltageToPercent", $bfAnyStatic)
    $mDetCat = $tLogi.GetMethod("DetectCategory", $bfPubStatic)
    Assert-True ($null -ne $mParseAdc -and $null -ne $mVoltPct) "Pro X ADC helpers"

    $adc1 = [object[]]@(([byte[]]@(0x0F, 0xA0, 0x01)).PSObject.BaseObject, 0, 0) # 4000mV discharging
    Assert-True ([bool]$mParseAdc.Invoke($null, $adc1) -and [int]$adc1[1] -eq 4000 -and [int]$adc1[2] -eq 1) "Pro X 4000mV discharging"
    $adc2 = [object[]]@(([byte[]]@(0x0F, 0x0A, 0x03)).PSObject.BaseObject, 0, 0) # 3850mV charging
    Assert-True ([bool]$mParseAdc.Invoke($null, $adc2) -and [int]$adc2[1] -eq 3850 -and [int]$adc2[2] -eq 3) "Pro X 3850mV charging"
    Assert-True (-not [bool]$mParseAdc.Invoke($null, [object[]]@(([byte[]]@(0x05, 0x00, 0x01)).PSObject.BaseObject, 0, 0))) "Pro X under-voltage rejected"
    Assert-True (-not [bool]$mParseAdc.Invoke($null, [object[]]@(([byte[]]@(0x12, 0x00, 0x01)).PSObject.BaseObject, 0, 0))) "Pro X over-voltage rejected"
    Assert-Equal 80 ([int]$mVoltPct.Invoke($null, @(4000))) "ProXVoltageToPercent 4000mV"
    Assert-Equal 0 ([int]$mVoltPct.Invoke($null, @(3500))) "ProXVoltageToPercent 3500mV"
    Assert-Equal 100 ([int]$mVoltPct.Invoke($null, @(4200))) "ProXVoltageToPercent 4200mV"
    Assert-Equal "Headset" ($mDetCat.Invoke($null, @([int]0x0ABA, "Logitech G PRO X Wireless 游戏耳机")).ToString()) "0x0ABA Headset"
}

Invoke-TestCase "Logitech.Mr0097OfflineProtocolAndRouting" {
    [RazerBatteryTray.BuiltInDrivers]::EnsureRegistered()
    $driver = @([RazerBatteryTray.HardwareDriverRegistry]::GetAllDriversOrdered() | Where-Object DriverId -eq "BuiltIn:LogitechMR0097")[0]
    Assert-True ($null -ne $driver -and !$driver.IsExternalPackage -and $driver.Priority -eq 100) "BuiltIn:LogitechMR0097 metadata"

    $tMr = $asm.GetType("RazerBatteryTray.LogitechMr0097Helper")
    function Call-Mr([string]$name, [object[]]$vals) {
        $m = $tMr.GetMethod($name, $bfAnyStatic)
        Assert-True ($null -ne $m) "Missing MR0097 method $name"
        for ($i = 0; $i -lt $vals.Length; $i++) { if ($null -ne $vals[$i]) { $vals[$i] = $vals[$i].PSObject.BaseObject } }
        return $m.Invoke($null, $vals)
    }
    function New-Ep([int]$vid, [int]$pidVal, [int]$page, [int]$usage, [int]$len) {
        $d = New-Object RazerBatteryTray.HidDeviceDescriptor
        $d.VendorId = $vid; $d.ProductId = $pidVal; $d.UsagePage = $page; $d.Usage = $usage
        $d.InputReportLength = $len; $d.OutputReportLength = $len; $d.FeatureReportLength = 0
        $d.DevicePath = "offline-mr0097-endpoint"
        return $d
    }

    $pathA = '\\?\hid#vid_046d&pid_c54d&mi_02&col02#7&1111111&0&0001#{4d1e55b2-f16f-11cf-88cb-001111000030}'
    $pathB = '\\?\hid#vid_046d&pid_c54d&mi_02&col01#7&1111111&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}'
    Assert-True (Call-Mr "SameProtocolInterface" @($pathA, $pathB)) "Sibling collections"
    Assert-True (!(Call-Mr "SameProtocolInterface" @($pathA, $pathB.Replace("1111111", "2222222")))) "Different receiver rejected"
    Assert-True (!(Call-Mr "SameProtocolInterface" @($pathA, $pathB.Replace("mi_02", "mi_01")))) "Different interface rejected"

    $ld = New-Ep 0x046D 0xC54D 0xFF00 2 20
    Assert-True (Call-Mr "IsControlEndpoint" @($ld)) "MR0097 C54D endpoint"
    $ld.ProductId = 0xC09B
    Assert-True (Call-Mr "IsControlEndpoint" @($ld)) "MR0097 C09B USB endpoint"
    $ld.ProductId = 0xC54D

    $li = New-Object byte[] 20; $li[0] = 0x11; $li[1] = 1; $li[2] = 2; $li[3] = 9
    $li[11] = 0x40; $li[12] = 0xA9; $li[13] = 0xC0; $li[14] = 0x9B
    Assert-True (Call-Mr "IsModelIdentity" @($li, [byte]1, [byte]2)) "MR0097 identity accepted"

    foreach ($s in @(
        @("11 01 06 19 59 08 00 00", 1, 89, $false),
        @("11 FF 06 19 3C 04 01 01", 255, 60, $true),
        @("11 01 06 19 00 01 00", 1, 0, $false),
        @("11 01 06 19 65 04 00", 1, -1, $false)
    )) {
        $fr = [byte[]]($s[0].Split(" ") | ForEach-Object { [Convert]::ToByte($_, 16) })
        $a = [object[]]@($fr, [byte]$s[1], [byte]6, -1, $false)
        $ok = Call-Mr "TryReadUnifiedBattery" $a
        Assert-True ($ok -eq ($s[2] -ge 0) -and $a[3] -eq $s[2] -and $a[4] -eq $s[3]) ("UnifiedBattery " + $s[0])
    }

    $dpi = [byte[]](17, 1, 9, 89, 0, 6, 64, 3, 32, 6, 64, 3, 32, 2)
    $dArgs = [object[]]@($dpi, [byte]1, [byte]9, -1, -1)
    Call-Mr "ReadExtendedDpi" $dArgs
    Assert-True ($dArgs[3] -eq 1600 -and $dArgs[4] -eq 1600) "Extended DPI symmetric"
    $dpi[10] = 114
    Call-Mr "ReadExtendedDpi" $dArgs
    Assert-True ($dArgs[3] -eq 1600 -and $dArgs[4] -eq 1650) "Extended DPI independent Y"

    $profBytes = [byte[]](3, 3, 0, 0, 32, 3, 32, 3, 2, 176, 4, 176, 4, 2, 64, 6, 64, 6, 2, 96, 9, 96, 9, 2, 128, 12, 128, 12, 2, 0, 0, 0)
    $prof = Call-Mr "DecodeOnboardDpiProfile" @($profBytes, 2, [byte]13)
    Assert-Equal "800,1200,1600,2400,3200" (($prof.GetType().GetField("X").GetValue($prof)) -join ",") "Onboard DPI table"
    Assert-Equal 3 ($prof.GetType().GetField("Stage").GetValue($prof)) "Onboard active stage"

    for ($code = 0; $code -lt 7; $code++) {
        Assert-Equal (125 -shl $code) (Call-Mr "ReadReportRate" @([byte[]](17, 1, 10, 41, $code), [byte]1, [byte]10)) ("Report rate code $code")
    }
    Assert-Equal 0 (Call-Mr "ReadReportRate" @([byte[]](17, 1, 10, 41, 7), [byte]1, [byte]10)) "Invalid rate code"

    $dev = [Activator]::CreateInstance($tInfo)
    $dev.DeviceId = "Logitech:046D:C54D:1"; $dev.Brand = "Logitech"; $dev.Category = [Enum]::Parse($tCat, "Mouse")
    $dev.DeviceName = "Logitech G PRO X SUPERLIGHT 2"; $dev.HardwareId = $ld.DevicePath
    Assert-True (!$driver.CanHandle($dev)) "Unverified path rejected"
    Assert-Equal "BuiltIn:LogitechMouse" ([RazerBatteryTray.HardwareDriverRegistry]::ResolveDriver($dev).DriverId) "Shared C54D fallback"

    Assert-True (Call-Mr "AcceptIdentity" @($ld, [byte]1, [byte]2, $li)) "AcceptIdentity"
    Assert-Equal $driver.DriverId ([RazerBatteryTray.HardwareDriverRegistry]::ResolveDriver($dev).DriverId) "Verified MR0097 routing"
    $dev.IsDonglePresent = $true; $dev.IsSleeping = $false; $dev.DpiStageCount = 5
    $driver.PopulateCapabilities($dev)
    Assert-True (!$dev.Capabilities.SupportsDpi -and !$dev.Capabilities.SupportsPollingRate -and $dev.Capabilities.SupportsHardwareDpiMonitor) "MR0097 read-only capability contract"
    Assert-True (!$driver.SetDpi($dev, 1234, 1234, 3) -and !$driver.SetPollingRate($dev, 8000)) "MR0097 rejects unsupported writes"

    $stamp = [DateTime]"2026-11-08T10:00:00"
    $st1 = Call-Mr "CreateState" @($ld, [byte]1, 89, $true, $false, $stamp, "Logitech:40A9:Unit:01-02-03-04")
    $lastField = $tMr.GetField("last", $bfAnyStatic).GetValue($null)
    $lastField[("BuiltIn:LogitechMR0097:" + $ld.DevicePath + ":1")] = $st1
    $cached = Call-Mr "CachedState" @($ld, [byte]1)
    Assert-True ($cached.BatteryPercent -eq 89 -and $cached.IsSleeping -and $null -eq (Call-Mr "CachedState" @($ld, [byte]2))) "MR0097 cache isolation"
    $lastField.Clear()

    # Deterministic ReceiveReply cold-start deadline replay (1059ms)
    $rootReply = New-Object byte[] 20; $rootReply[0] = 0x11; $rootReply[1] = 1; $rootReply[3] = 9; $rootReply[4] = 2
    $script:mrIdx = 0; $script:mrElapsed = [long]0
    $reader = [Func[int, byte[]]] {
        param([int]$rem)
        if ($script:mrIdx -gt 0 -or 1059 -ge $rem) { $script:mrElapsed += $rem; return $null }
        $script:mrIdx++; $script:mrElapsed += 1059; return $rootReply
    }
    $elapsedFn = [Func[long]] { return $script:mrElapsed }
    $tx = New-Object byte[] 20; $tx[0] = 0x11; $tx[1] = 1; $tx[3] = 9; $tx[5] = 3
    $cold = Call-Mr "ReceiveReply" @($tx, 2000, $reader, $elapsedFn)
    Assert-True ($null -ne $cold -and $script:mrElapsed -eq 1059) "MR0097 1059ms cold identity reply"
}

Invoke-TestCase "Logitech.MatchDpiStageAndMeasuredRateBadgeUI" {
    $mMatch = $tLogi.GetMethod("MatchDpiStage", $bfPubStatic, $null, @([int], [int[]]), $null)
    $stages = [int[]]@(400, 800, 1600, 3200, 6400)
    Assert-Equal 1 ([int]$mMatch.Invoke($null, @(400, $stages))) "MatchDpiStage 400"
    Assert-Equal 4 ([int]$mMatch.Invoke($null, @(3200, $stages))) "MatchDpiStage 3200"
    Assert-Equal 4 ([int]$mMatch.Invoke($null, @(3190, $stages))) "MatchDpiStage 3190"
    Assert-Equal 1 ([int]$mMatch.Invoke($null, @(800, [int[]]@(400, 0, 1600, 3200, 6400)))) "MatchDpiStage skips zero slot"
    Assert-Equal 1 ([int]$mMatch.Invoke($null, @(0, $stages))) "MatchDpiStage 0 >= 1"

    $form = New-SandboxMainForm $false
    $tMain = $form.GetType()
    try {
        $tMain.GetField("desktopPollingRate", $bfNonPubInst).SetValue($form, 500)
        $tMain.GetField("autoGameRateEnabled", $bfNonPubInst).SetValue($form, $true)
        $mUpdate = $tMain.GetMethod("UpdatePerformanceSection", $bfNonPubInst)

        $info = [Activator]::CreateInstance($tInfo)
        $info.Brand = "Logitech"; $info.DeviceId = "Logitech:046D:C53F:1"; $info.DeviceName = "G304 Lightspeed Wireless Gaming Mouse"
        $info.Category = [Enum]::Parse($tCat, "Mouse"); $info.Transport = "Lightspeed 无线"
        $info.IsConnected = $true; $info.IsSleeping = $false; $info.BatteryPercent = 90
        $info.Dpi = 3200; $info.DpiY = 3200; $info.DpiStage = 4; $info.DpiStageCount = 5
        $info.DpiStages = $stages; $info.DpiStagesY = $stages
        $info.PollingRate = 1000; $info.SupportedPollingRates = [int[]]@(125, 250, 500, 1000)

        [void]$mUpdate.Invoke($form, @($info, $info.BrandColor))
        $lblBadge = $tMain.GetField("lblLiveRateBadge", $bfNonPubInst).GetValue($form)
        Assert-True ($lblBadge.Text -match "1000 Hz" -and $lblBadge.Text -notmatch "500 Hz") "Live rate badge uses measured 1000Hz"

        $btns = $tMain.GetField("btnDpiStages", $bfNonPubInst).GetValue($form)
        Assert-True ($btns[3].Selected -and !$btns[1].Selected) "DPI stage highlight 3200 (index 3), not 800"

        $info.PollingRate = 0
        [void]$mUpdate.Invoke($form, @($info, $info.BrandColor))
        Assert-True ($lblBadge.Text -match "500 Hz" -and $lblBadge.Text -notmatch "(^|[^\d])0 Hz") "Rate badge falls back when measured=0"
    } finally {
        $form.Dispose()
    }
}

Invoke-TestCase "Logitech.EndpointScoringAndHidpp10Fallback" {
    $mScore = $tLogi.GetMethod("ScoreLogitechEndpoint", $bfPubStatic)
    $mHidpp10 = $tLogi.GetMethod("TryParseHidpp10BatteryReply", $bfPubStatic)
    Assert-True ($null -ne $mScore -and $null -ne $mHidpp10) "Logitech helper methods missing"

    $s20Exact = [int]$mScore.Invoke($null, @([uint16]0xFF00, [uint16]0x0002, [int]20, [int]20))
    $s32Dj    = [int]$mScore.Invoke($null, @([uint16]0xFF00, [uint16]0x0004, [int]32, [int]32))
    $s7Short  = [int]$mScore.Invoke($null, @([uint16]0xFF00, [uint16]0x0001, [int]7, [int]7))
    Assert-True ($s20Exact -gt $s32Dj -and $s32Dj -gt $s7Short) "20B FF00/0002 beats 32B DJ beats 7B short"

    # HID++ 1.0 Register 0x0D Mileage (75%, charging 0x30)
    $rx0D = [byte[]]@(0x10, 0x01, 0x81, 0x0D, 75, 0x00, 0x30)
    $args0D = [object[]]@($rx0D, [int]0, $false)
    $ok0D = [bool]$mHidpp10.Invoke($null, $args0D)
    Assert-True ($ok0D -and $args0D[1] -eq 75 -and $args0D[2] -eq $true) "HID++ 1.0 reg 0x0D mileage charging"

    # HID++ 1.0 Register 0x07 Status (level 5 -> 50%, discharging 0x00)
    $rx07 = [byte[]]@(0x10, 0x02, 0x81, 0x07, 0x05, 0x00, 0x00)
    $args07 = [object[]]@($rx07, [int]0, $false)
    $ok07 = [bool]$mHidpp10.Invoke($null, $args07)
    Assert-True ($ok07 -and $args07[1] -eq 50 -and $args07[2] -eq $false) "HID++ 1.0 reg 0x07 status"
}
