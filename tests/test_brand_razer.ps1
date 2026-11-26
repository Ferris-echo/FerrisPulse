# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# Consolidated Razer Brand Test Suite (replaces 14 legacy test_razer_*.ps1 scripts)
# Pure in-memory reflection + sandbox mode; zero hardware writes, zero registry writes.

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
. (Join-Path $PSScriptRoot "..\tools\TestHarness.ps1")

$asm = Load-FerrisPulseAssembly
$root = Get-FerrisPulseRoot
$bfPubStatic = [System.Reflection.BindingFlags]"Public,Static"
$bfAnyStatic = [System.Reflection.BindingFlags]"Public,NonPublic,Static"
$bfNonPubInst = [System.Reflection.BindingFlags]"NonPublic,Instance"

$rzType = $asm.GetType("RazerBatteryTray.RazerDeviceHelper")
$infoType = $asm.GetType("RazerBatteryTray.MouseBatteryInfo")
$catType = $asm.GetType("RazerBatteryTray.DeviceCategory")

Invoke-TestCase "Razer.BasiliskUltimateAndV3ProNormalization" {
    $mNorm = $rzType.GetMethod("NormalizeRazerPid", $bfPubStatic)
    $mNormId = $rzType.GetMethod("NormalizeRazerDeviceId", $bfPubStatic)
    $mDongle = $rzType.GetMethod("DetermineIsDongle", $bfPubStatic)
    $mName = $rzType.GetMethod("GetRazerOfficialModelName", $bfPubStatic, $null, @([int], [string]), $null)
    $mRates = $rzType.GetMethod("GetSupportedPollingRatesForModel", $bfPubStatic)
    $mCap = $rzType.GetMethod("GetDpiCapability", $bfPubStatic)

    Assert-Equal 0x0086 ([int]$mNorm.Invoke($null, @(0x0088))) "Basilisk Ultimate dongle 0x0088 -> 0x0086"
    Assert-Equal "Razer Basilisk Ultimate" ([string]$mName.Invoke($null, @(0x0086, $null))) "0x0086 name"
    Assert-Equal "Razer Basilisk Ultimate" ([string]$mName.Invoke($null, @(0x0088, $null))) "0x0088 name"
    $rates86 = [int[]]$mRates.Invoke($null, @(0x0086, "Razer Basilisk Ultimate"))
    Assert-True ($rates86 -contains 125 -and $rates86 -contains 500 -and $rates86 -contains 1000) "Basilisk Ultimate rates"

    $normCases = @(
        @{ In = 0x00CD; Out = 0x00CC }, @{ In = 0x00D7; Out = 0x00D6 }, @{ In = 0x00AB; Out = 0x00AA },
        @{ In = 0x00CC; Out = 0x00CC }, @{ In = 0x00D6; Out = 0x00D6 }, @{ In = 0x00DE; Out = 0x00DE },
        @{ In = 0x00DF; Out = 0x00DE }, @{ In = 0x00F0; Out = 0x00EF }, @{ In = 0x00BF; Out = 0x00BE },
        @{ In = 0x00E6; Out = 0x00E5 }
    )
    foreach ($c in $normCases) {
        Assert-Equal $c.Out ([int]$mNorm.Invoke($null, @([int]$c.In))) ("NormalizeRazerPid 0x{0:X4}" -f $c.In)
    }

    Assert-Equal "Razer:1532:00CC" ([string]$mNormId.Invoke($null, @("Razer:1532:00CD"))) "NormalizeRazerDeviceId 00CD"
    Assert-Equal "Razer:1532:00D6" ([string]$mNormId.Invoke($null, @("Razer:1532:00D7"))) "NormalizeRazerDeviceId 00D7"
    Assert-Equal "Razer:1532:00AA" ([string]$mNormId.Invoke($null, @("Razer:1532:00AB"))) "NormalizeRazerDeviceId 00AB"

    foreach ($dp in @(0x00CD, 0x00D7, 0x00AB, 0x00DF, 0x009C)) {
        Assert-True ([bool]$mDongle.Invoke($null, @([int]$dp, "", 0, $false))) ("DetermineIsDongle 0x{0:X4}" -f $dp)
    }

    $nameCases = @(
        @{ Pid = 0x00CC; Prod = ""; Exp = "Razer Basilisk V3 Pro 35K" },
        @{ Pid = 0x00D6; Prod = ""; Exp = "Razer Basilisk V3 Pro 35K (Phantom Green)" },
        @{ Pid = 0x00AA; Prod = ""; Exp = "Razer Basilisk V3 Pro" },
        @{ Pid = 0x00DE; Prod = ""; Exp = "Razer Viper V3 Pro SE" },
        @{ Pid = 0x00DF; Prod = ""; Exp = "Razer Viper V3 Pro SE" },
        @{ Pid = 0x00EF; Prod = "Razer DeathAdder V4 Pro Carbon"; Exp = "Razer DeathAdder V4 Pro Carbon" },
        @{ Pid = 0x00F0; Prod = "Razer HyperPolling Wireless Dongle"; Exp = "Razer DeathAdder V4 Pro Carbon" },
        @{ Pid = 0x00BE; Prod = "Razer DeathAdder V4 Pro"; Exp = "Razer DeathAdder V4 Pro" },
        @{ Pid = 0x00BF; Prod = "Razer HyperPolling Wireless Dongle"; Exp = "Razer DeathAdder V4 Pro" },
        @{ Pid = 0x00E5; Prod = "Razer Viper V4 Pro"; Exp = "Razer Viper V4 Pro" },
        @{ Pid = 0x00E6; Prod = "Razer HyperPolling Wireless Dongle"; Exp = "Razer Viper V4 Pro" },
        @{ Pid = 0x00BE; Prod = "Razer DeathAdder V4 Pro Carbon"; Exp = "Razer DeathAdder V4 Pro Carbon" }
    )
    foreach ($nc in $nameCases) {
        Assert-Equal $nc.Exp ([string]$mName.Invoke($null, @([int]$nc.Pid, $nc.Prod))) ("Model 0x{0:X4} ({1})" -f $nc.Pid, $nc.Prod)
    }

    foreach ($p35k in @(0x00CC, 0x00CD, 0x00D6, 0x00D7, 0x00DE, 0x00DF)) {
        $cap = $mCap.Invoke($null, @([string]$mName.Invoke($null, @([int]$p35k, "")), [int]$p35k))
        Assert-Equal 35000 $cap.MaxDpi ("35K sensor MaxDpi for 0x{0:X4}" -f $p35k)
    }
    foreach ($p8k in @(0x00CC, 0x00CD, 0x00D6, 0x00D7)) {
        $r = [int[]]$mRates.Invoke($null, @([int]$p8k, ""))
        Assert-True ($r -contains 8000 -and $r -contains 1000) ("8K polling rate for 0x{0:X4}" -f $p8k)
    }
}

Invoke-TestCase "Razer.SynapseWebV2XAndV4Capabilities" {
    $mName = $rzType.GetMethod("GetRazerOfficialModelName", $bfPubStatic, $null, @([int], [string]), $null)
    $mDisplay = $rzType.GetMethod("GetRazerDisplayName", $bfPubStatic)
    $mCap = $rzType.GetMethod("GetDpiCapability", $bfPubStatic)
    $mRates = $rzType.GetMethod("GetSupportedPollingRatesForModel", $bfPubStatic)
    $mMaxRate = $rzType.GetMethod("GetMaxPollingRateForModel", $bfPubStatic)
    $mCat = $rzType.GetMethod("GetRazerDeviceCategory", $bfPubStatic)

    $da2xCap = $mCap.Invoke($null, @("Razer DeathAdder V2 X HyperSpeed", 0x009C))
    Assert-True ($da2xCap.MinDpi -eq 100 -and $da2xCap.MaxDpi -eq 14000 -and $da2xCap.Step -eq 50) "DA V2 X cap"
    Assert-Equal "125,250,500,1000" (([int[]]$mRates.Invoke($null, @(0x009C, "Razer DeathAdder V2 X HyperSpeed"))) -join ",") "DA V2 X rates"
    Assert-Equal 1000 ([int]$mMaxRate.Invoke($null, @(0x009C, "Razer DeathAdder V2 X HyperSpeed"))) "DA V2 X max rate"

    Assert-True ([string]$mDisplay.Invoke($null, @(0x00A6, "Razer Viper V2 Pro"))).Contains("Viper V2 Pro") "Viper V2 display name"
    Assert-Equal 1000 ([int]$mMaxRate.Invoke($null, @(0x00A6, "Razer Viper V2 Pro"))) "Viper V2 standard dongle max rate"

    $v3Cap = $mCap.Invoke($null, @("Razer DeathAdder V3 Pro", 0x00B6))
    Assert-Equal 30000 $v3Cap.MaxDpi "DA V3 Pro MaxDpi"
    Assert-True (([int[]]$mRates.Invoke($null, @(0x00B6, "Razer DeathAdder V3 Pro"))) -contains 8000) "DA V3 Pro 8K"

    $capViper4 = $mCap.Invoke($null, @("Razer Viper V4 Pro", 0x00E5))
    $capDa4Carbon = $mCap.Invoke($null, @("Razer DeathAdder V4 Pro Carbon", 0x00EF))
    $capDa4Std = $mCap.Invoke($null, @("Razer DeathAdder V4 Pro", 0x00BE))
    $capDa4Hs = $mCap.Invoke($null, @("Razer DeathAdder V4 HyperSpeed", 0xB007))
    $capV4Hs = $mCap.Invoke($null, @("Razer Viper V4 HyperSpeed", 0xB005))
    Assert-True ($capViper4.MaxDpi -ge 45000 -and $capDa4Carbon.MaxDpi -ge 45000 -and $capDa4Std.MaxDpi -ge 45000) "V4 Pro MaxDpi >= 45000"
    Assert-True ($capDa4Hs.MaxDpi -ge 20000 -and $capV4Hs.MaxDpi -ge 20000) "V4 HyperSpeed MaxDpi"

    $synapseWebModels = @(
        @{ Pid = 0xB007; Name = "Razer DeathAdder V4 HyperSpeed"; Cat = "Mouse" },
        @{ Pid = 0xB005; Name = "Razer Viper V4 HyperSpeed"; Cat = "Mouse" },
        @{ Pid = 0x0257; Name = "Razer Huntsman Mini"; Cat = "Keyboard" },
        @{ Pid = 0x0269; Name = "Razer Huntsman Mini Japan"; Cat = "Keyboard" },
        @{ Pid = 0x02B0; Name = "Razer Huntsman V3 Pro Mini"; Cat = "Keyboard" },
        @{ Pid = 0x02CF; Name = "Razer Huntsman V3 Pro 8KHz"; Cat = "Keyboard" },
        @{ Pid = 0x02D0; Name = "Razer Huntsman V3 Pro Tenkeyless 8KHz"; Cat = "Keyboard" },
        @{ Pid = 0x02D1; Name = "Razer Huntsman V3 Pro Mini 8KHz"; Cat = "Keyboard" },
        @{ Pid = 0x02D8; Name = "Razer Huntsman Signature Edition"; Cat = "Keyboard" },
        @{ Pid = 0x02E4; Name = "Razer Huntsman V3 HE Magnetic Mini 65% 8KHz"; Cat = "Keyboard" },
        @{ Pid = 0x02E5; Name = "Razer Huntsman V3 Tenkeyless 8KHz"; Cat = "Keyboard" },
        @{ Pid = 0x02E6; Name = "Razer Huntsman V3 Pro Low-profile Tenkeyless 8KHz"; Cat = "Keyboard" },
        @{ Pid = 0x02EA; Name = "Razer Huntsman V3 HE Magnetic Tenkeyless 8KHz"; Cat = "Keyboard" },
        @{ Pid = 0x02EB; Name = "Razer Tartarus V2 Pro"; Cat = "Keyboard" },
        @{ Pid = 0x02E0; Name = "Razer Blade 16 (2026)"; Cat = "Keyboard" },
        @{ Pid = 0x02E1; Name = "Razer Blade 18 (2026)"; Cat = "Keyboard" }
    )
    foreach ($sw in $synapseWebModels) {
        Assert-Equal $sw.Name ([string]$mName.Invoke($null, @([int]$sw.Pid, ""))) ("Synapse Web name 0x{0:X4}" -f $sw.Pid)
        Assert-Equal $sw.Cat ($mCat.Invoke($null, @([int]$sw.Pid, $sw.Name)).ToString()) ("Synapse Web category 0x{0:X4}" -f $sw.Pid)
    }

    foreach ($kPid in @(0x02CF, 0x02D0, 0x02D1, 0x02D8, 0x02E4, 0x02E5, 0x02E6, 0x02EA)) {
        $kRates = [int[]]$mRates.Invoke($null, @([int]$kPid, ""))
        Assert-True ($kRates -contains 8000) ("8KHz keyboard 0x{0:X4} missing 8000Hz" -f $kPid)
    }

    $classCases = @(
        @{ Pid = 0x0216; Name = "Razer BlackWidow X Chroma"; Exp = "Keyboard" },
        @{ Pid = 0x0217; Name = "Razer BlackWidow X Ultimate"; Exp = "Keyboard" },
        @{ Pid = 0x021A; Name = "Razer BlackWidow X Chroma TE"; Exp = "Keyboard" },
        @{ Pid = 0x0203; Name = "Razer BlackWidow Chroma"; Exp = "Keyboard" },
        @{ Pid = 0x0226; Name = "Razer Huntsman Elite"; Exp = "Keyboard" },
        @{ Pid = 0x021E; Name = "Razer Ornata Chroma"; Exp = "Keyboard" },
        @{ Pid = 0x022A; Name = "Razer Cynosa Chroma"; Exp = "Keyboard" },
        @{ Pid = 0x008A; Name = "Razer Viper Mini"; Exp = "Mouse" },
        @{ Pid = 0x0078; Name = "Razer Viper Ultimate"; Exp = "Mouse" }
    )
    foreach ($cc in $classCases) {
        Assert-Equal $cc.Exp ($mCat.Invoke($null, @([int]$cc.Pid, $cc.Name)).ToString()) ("Class 0x{0:X4}" -f $cc.Pid)
    }
}

Invoke-TestCase "Razer.DpiStagesAndReportProtocol" {
    $tState = $asm.GetType("RazerBatteryTray.RazerDeviceHardwareState")
    Assert-True ($null -ne $tState.GetProperty("ActiveTransactionId") -or $null -ne $tState.GetField("ActiveTransactionId")) "ActiveTransactionId missing"
    $mAdaptive = $rzType.GetMethod("GetAdaptiveTransactionIds", $bfAnyStatic)
    foreach ($tid in @([byte]0x3F, [byte]0xFF, [byte]0x1F)) {
        $arr = [byte[]]$mAdaptive.Invoke($null, @($tid))
        Assert-Equal $tid $arr[0] ("Adaptive TID {0:X2}" -f $tid)
    }

    $mCreate = $rzType.GetMethod("CreateRazerReport", [System.Reflection.BindingFlags]"NonPublic,Static")
    $argsDpi = [byte[]]@(0x01, 0x06, 0x40, 0x06, 0x40, 0x00, 0x00)
    foreach ($tid in @([byte]0x1F, [byte]0x3F, [byte]0xFF)) {
        $rep = [byte[]]$mCreate.Invoke($null, @($tid, [byte]0x04, [byte]0x05, [byte]0x07, 90, $false, $argsDpi))
        Assert-True ($rep[1] -eq $tid -and $rep[6] -eq 0x04 -and $rep[7] -eq 0x05) "CreateRazerReport header"
    }

    # Independent XY report
    $xyReport = New-Object byte[] 91
    $xyReport[1] = 2; $xyReport[7] = 4; $xyReport[8] = 0x86; $xyReport[10] = 2; $xyReport[11] = 2
    [byte[]]$xyStages = @(1, 3, 32, 3, 32, 0, 0, 2, 7, 8, 3, 132, 0, 0)
    [Array]::Copy($xyStages, 0, $xyReport, 12, $xyStages.Length)
    $xyState = [RazerBatteryTray.RazerDeviceHelper]::ParseDpiStagesReport($xyReport, 1)
    Assert-True ($xyState.Dpi -eq 1800 -and $xyState.DpiY -eq 900 -and $xyState.DpiStagesY[0] -eq 800) "Independent XY stages"
    $xyReport[11] = 6
    Assert-True ($null -eq [RazerBatteryTray.RazerDeviceHelper]::ParseDpiStagesReport($xyReport, 1)) "Invalid stage count rejected"

    # High DPI 45k/50k report + 91-byte prepended frame
    $hi = New-Object byte[] 90
    $hi[0] = 0x02; $hi[1] = 0x1F; $hi[5] = 0x26; $hi[6] = 0x04; $hi[7] = 0x86
    $hi[8] = 0x01; $hi[9] = 0x05; $hi[10] = 0x05
    $dpis = @(800, 1600, 3200, 45000, 50000)
    for ($idx = 0; $idx -lt 5; $idx++) {
        $off = 11 + $idx * 7
        $v = [int]$dpis[$idx]
        $hi[$off] = [byte]($idx + 1)
        $hi[$off + 1] = [byte](($v -shr 8) -band 0xFF); $hi[$off + 2] = [byte]($v -band 0xFF)
        $hi[$off + 3] = $hi[$off + 1]; $hi[$off + 4] = $hi[$off + 2]
    }
    $hiState = [RazerBatteryTray.RazerDeviceHelper]::ParseDpiStagesReport($hi, 0)
    Assert-True ($null -ne $hiState -and $hiState.DpiStage -eq 5 -and $hiState.Dpi -eq 50000 -and $hiState.DpiStages[3] -eq 45000 -and $hiState.DpiStages[4] -eq 50000) "50k DPI stages"
    $hi91 = New-Object byte[] 91
    [Array]::Copy($hi, 0, $hi91, 1, 90)
    $hiState91 = [RazerBatteryTray.RazerDeviceHelper]::ParseDpiStagesReport($hi91, 1)
    Assert-True ($null -ne $hiState91 -and $hiState91.Dpi -eq 50000 -and $hiState91.DpiStages[3] -eq 45000) "91-byte prepended 50k report"

    # Ghost-OSD regression guard: 4-arg FastQueryDpi(out int, out int, out int, string) exists & safe in sandbox
    $mFast = $rzType.GetMethod("FastQueryDpi", $bfPubStatic, $null, [Type[]]@([int].MakeByRefType(), [int].MakeByRefType(), [int].MakeByRefType(), [string]), $null)
    Assert-True ($null -ne $mFast) "FastQueryDpi(out, out, out, string) overload"
}

Invoke-TestCase "Razer.BasiliskLightingAndTrayIsolation" {
    $lightType = $asm.GetType("RazerBatteryTray.LightingHelper")
    Assert-Equal ([byte]0x00) ([byte]$lightType.GetField("ZoneAll", $bfPubStatic).GetValue($null)) "ZoneAll"
    Assert-Equal ([byte]0x01) ([byte]$lightType.GetField("ZoneWheel", $bfPubStatic).GetValue($null)) "ZoneWheel"
    Assert-Equal ([byte]0x04) ([byte]$lightType.GetField("ZoneLogo", $bfPubStatic).GetValue($null)) "ZoneLogo"
    Assert-Equal 14 ([int]$lightType.GetField("MatrixLeds", $bfPubStatic).GetValue($null)) "MatrixLeds"
    Assert-Equal 4 ([int]$lightType.GetField("LeftSegmentCount", $bfPubStatic).GetValue($null)) "LeftSegmentCount"

    $setAll = $lightType.GetMethod("SetAllColor", $bfPubStatic)
    Assert-Equal $false ([bool]$setAll.Invoke($null, @([System.Drawing.Color]::LimeGreen))) "LightingHelper blocked in test sandbox"

    $mainFormType = $asm.GetType("RazerBatteryTray.MainForm")
    $form = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($mainFormType)
    $listType = [System.Collections.Generic.List`1].MakeGenericType(@($infoType))
    $contextMenu = New-Object System.Windows.Forms.ContextMenuStrip
    $lightingMenu = New-Object System.Windows.Forms.ToolStripMenuItem("Lighting")
    [void]$contextMenu.Items.Add($lightingMenu)
    $mainFormType.GetField("contextMenu", $bfNonPubInst).SetValue($form, $contextMenu)
    $mainFormType.GetField("lightingMenu", $bfNonPubInst).SetValue($form, $lightingMenu)
    $mainFormType.GetField("dynamicDeviceMenuItems", $bfNonPubInst).SetValue($form, (New-Object System.Collections.Generic.List[System.Windows.Forms.ToolStripMenuItem]))
    $mUpdateTray = $mainFormType.GetMethod("UpdateTrayDeviceMenuItems", $bfNonPubInst)

    function Test-LightingVisible([string]$id, [string]$name, [string]$cat, [bool]$expected) {
        $d = [Activator]::CreateInstance($infoType)
        $d.DeviceId = $id; $d.DeviceName = $name; $d.Category = [Enum]::Parse($catType, $cat)
        $d.IsConnected = $true; $d.IsPrimary = $true
        $devs = [Activator]::CreateInstance($listType); $devs.Add($d)
        $mainFormType.GetField("activeDevices", $bfNonPubInst).SetValue($form, $devs)
        $mainFormType.GetField("lastInfo", $bfNonPubInst).SetValue($form, $d)
        [void]$mUpdateTray.Invoke($form, $null)
        Assert-Equal $expected $lightingMenu.Available ("lightingMenu visibility for $name")
    }
    Test-LightingVisible "Razer:1532:00BE" "Razer DeathAdder V4 Pro" "Mouse" $false
    Test-LightingVisible "Razer:1532:0088" "Razer Basilisk Ultimate" "Mouse" $true
    Test-LightingVisible "NuPhy:19F5:3245" "NuPhy Air75" "Keyboard" $false
    $contextMenu.Dispose()
}

Invoke-TestCase "Razer.HeadsetsBlackSharkAndKrakenV4" {
    $hsType = $asm.GetType("RazerBatteryTray.RazerHeadsetHelper")
    Assert-True ($null -ne $hsType) "RazerHeadsetHelper missing"
    $cmdBatt = [byte]$hsType.GetField("CmdGetBattery", $bfPubStatic).GetValue($null)
    $cmdChg = [byte]$hsType.GetField("CmdGetCharging", $bfPubStatic).GetValue($null)
    $cmdConn = [byte]$hsType.GetField("CmdGetConnection", $bfPubStatic).GetValue($null)

    [byte[]]$bsBattRx = @(0x02, 0x0E, 0x50, 0x49, 0x08, 0xD6, 0x54, 0x66, 0x0F, 0x00, 0x04, 0x00, 0x21, 0x01, 0x01, 0x58)
    [byte[]]$bsChgRx  = @(0x02, 0x0E, 0x50, 0x49, 0x08, 0xD7, 0x5F, 0x66, 0x0F, 0x00, 0x04, 0x00, 0x2A, 0x01, 0x01, 0x00)
    [byte[]]$bsConnRx = @(0x02, 0x0E, 0x50, 0x49, 0x08, 0xD5, 0x4C, 0x66, 0x0F, 0x00, 0x04, 0x00, 0x20, 0x01, 0x01, 0x01)
    [byte[]]$bsBadMagic = @(0x02, 0x0E, 0x00, 0x00, 0x08, 0, 0, 0, 0, 0, 4, 0, 0x21, 1, 1, 88)

    Assert-Equal 88 ([RazerBatteryTray.RazerHeadsetHelper]::ParseReplyValue($bsBattRx, $cmdBatt)) "BlackShark 88% battery"
    Assert-Equal 0  ([RazerBatteryTray.RazerHeadsetHelper]::ParseReplyValue($bsChgRx, $cmdChg)) "BlackShark discharging"
    Assert-Equal 1  ([RazerBatteryTray.RazerHeadsetHelper]::ParseReplyValue($bsConnRx, $cmdConn)) "BlackShark connected"
    Assert-Equal -1 ([RazerBatteryTray.RazerHeadsetHelper]::ParseReplyValue($bsBattRx, $cmdChg)) "BlackShark wrong cmd rejected"
    Assert-Equal -1 ([RazerBatteryTray.RazerHeadsetHelper]::ParseReplyValue($bsBadMagic, $cmdBatt)) "BlackShark bad magic rejected"
    Assert-Equal -1 ([RazerBatteryTray.RazerHeadsetHelper]::ParseReplyValue($null, $cmdBatt)) "BlackShark null rejected"
    Assert-Equal -1 ([RazerBatteryTray.RazerHeadsetHelper]::ParseReplyValue((New-Object byte[] 4), $cmdBatt)) "BlackShark short rejected"
    $bsTx = [RazerBatteryTray.RazerHeadsetHelper]::BuildFrame($cmdBatt)
    Assert-Equal 64 $bsTx.Length "BlackShark BuildFrame len"
    Assert-Equal "02-80-08-00-00-50-41-08-00-03-21-00" ([BitConverter]::ToString($bsTx, 0, 12)) "BlackShark BuildFrame header"

    $dmSrc = [System.IO.File]::ReadAllText((Join-Path $root "src\Hardware\DeviceManager.cs"), [System.Text.Encoding]::UTF8)
    Assert-True ($dmSrc.Contains("RazerHeadsetHelper.QueryAllRazerHeadsets()")) "DeviceManager mounts RazerHeadsetHelper"

    function New-KrakenFrame([byte]$cmd, [byte]$val) {
        $f = New-Object byte[] 64
        [byte[]]$p = @(2, 2, 0x60, 0, 0, 0, 5, 0, 0x80, 0x80, $cmd, 1, 1, $val)
        [Array]::Copy($p, 0, $f, 0, $p.Length)
        return ,$f
    }
    Assert-Equal 69 ([RazerBatteryTray.RazerHeadsetHelper]::ParseKrakenReplyValue((New-KrakenFrame 0x21 69), 0x21)) "Kraken V4 battery 69"
    Assert-Equal 0  ([RazerBatteryTray.RazerHeadsetHelper]::ParseKrakenReplyValue((New-KrakenFrame 0x2A 0), 0x2A)) "Kraken V4 discharging"
    Assert-Equal 1  ([RazerBatteryTray.RazerHeadsetHelper]::ParseKrakenReplyValue((New-KrakenFrame 0x2A 1), 0x2A)) "Kraken V4 charging"
    Assert-Equal 1  ([RazerBatteryTray.RazerHeadsetHelper]::ParseKrakenReplyValue((New-KrakenFrame 0x20 1), 0x20)) "Kraken V4 online"
    Assert-Equal 0  ([RazerBatteryTray.RazerHeadsetHelper]::ParseKrakenReplyValue((New-KrakenFrame 0x21 0), 0x21)) "Kraken V4 0%"
    Assert-Equal 100 ([RazerBatteryTray.RazerHeadsetHelper]::ParseKrakenReplyValue((New-KrakenFrame 0x21 100), 0x21)) "Kraken V4 100%"
    Assert-Equal -1 ([RazerBatteryTray.RazerHeadsetHelper]::ParseKrakenReplyValue((New-KrakenFrame 0x21 101), 0x21)) "Kraken V4 >100 rejected"
    Assert-Equal -1 ([RazerBatteryTray.RazerHeadsetHelper]::ParseKrakenReplyValue((New-KrakenFrame 0x2A 2), 0x2A)) "Kraken V4 unknown charging rejected"
    Assert-Equal -1 ([RazerBatteryTray.RazerHeadsetHelper]::ParseKrakenReplyValue((New-Object byte[] 13), 0x21)) "Kraken V4 truncated"
    Assert-Equal -1 ([RazerBatteryTray.RazerHeadsetHelper]::ParseKrakenReplyValue($null, 0x21)) "Kraken V4 null"
    $kTx = [RazerBatteryTray.RazerHeadsetHelper]::BuildKrakenFrame(0x21)
    Assert-Equal "02-00-60-00-00-00-04-00-00-80-21-00" ([BitConverter]::ToString($kTx, 0, 12)) "Kraken BuildFrame"
    Assert-Equal "Razer Kraken V4" ([RazerBatteryTray.RazerHeadsetHelper]::GetHeadsetModelName(0x056C, "Razer Kraken V4 - Chat")) "Kraken chat suffix stripped"
    Assert-True ([RazerBatteryTray.RazerHeadsetHelper]::IsRazerHeadset("HID\VID_1532&PID_056C&MI_05")) "IsRazerHeadset 056C"
}

Invoke-TestCase "Razer.ViperV3ButtonMappingAndJsonRoundTrip" {
    $bmType = $asm.GetType("RazerBatteryTray.RazerButtonMapping")
    $btnEnum = $asm.GetType("RazerBatteryTray.RazerButtonMapping+ButtonId")
    $expectedButtons = @{ Left=0x01; Right=0x02; Middle=0x03; Backward=0x04; Forward=0x05; WheelUp=0x09; WheelDown=0x0A; DpiButton=0x60 }
    foreach ($n in $expectedButtons.Keys) {
        Assert-Equal ([byte]$expectedButtons[$n]) ([byte][Enum]::Parse($btnEnum, $n)) ("ButtonId $n")
    }

    $fnType = $asm.GetType("RazerBatteryTray.RazerButtonMapping+ButtonFunction")
    $fnClass = $asm.GetType("RazerBatteryTray.RazerButtonMapping+FnClass")
    $fn = [Activator]::CreateInstance($fnType)
    $fn.Class = [Enum]::Parse($fnClass, "Mouse"); $fn.ValueLength = 1; $fn.Value[0] = 0x01
    $b7 = [byte[]]$fn.ToBytes7()
    Assert-True ($b7.Length -eq 7 -and $b7[0] -eq 1 -and $b7[1] -eq 1 -and $b7[2] -eq 1) "ToBytes7"
    $restoredFn = $fnType.GetMethod("FromBytes7", $bfPubStatic).Invoke($null, @($b7, [int]0))
    Assert-True ($restoredFn.Class -eq $fn.Class -and $restoredFn.ValueLength -eq 1 -and $restoredFn.Value[0] -eq 1) "FromBytes7"

    $mDef = $bmType.GetMethod("GetFactoryDefault", $bfPubStatic)
    foreach ($n in $expectedButtons.Keys) {
        Assert-True ($null -ne $mDef.Invoke($null, @([Enum]::Parse($btnEnum, $n)))) ("Factory default for $n")
    }

    $bfiType = $asm.GetType("RazerBatteryTray.RazerButtonMapping+ButtonFunctionInfo")
    $listType = [System.Collections.Generic.List`1].MakeGenericType($bfiType)
    $list = [Activator]::CreateInstance($listType)
    $item = [Activator]::CreateInstance($bfiType)
    $item.Button = [Enum]::Parse($btnEnum, "Backward"); $item.Function = $fn
    $list.Add($item)
    $json = [string]$bmType.GetMethod("ButtonSnapshotToJson", [System.Reflection.BindingFlags]"NonPublic,Static").Invoke($null, [object[]]@(,$list))
    Assert-True ($json.Contains('"button":"04"')) "ButtonSnapshotToJson output"
    $parsed = $bmType.GetMethod("ParseButtonSnapshotJson", [System.Reflection.BindingFlags]"NonPublic,Static").Invoke($null, [object[]]@(,$json))
    Assert-True ($parsed.Count -eq 1 -and $parsed[0].Key -eq [Enum]::Parse($btnEnum, "Backward")) "ParseButtonSnapshotJson roundtrip"
}

Invoke-TestCase "Razer.UiKeyboardVsMouseRouting" {
    $form = New-SandboxMainForm $false
    $tMain = $form.GetType()
    $mUpdate = $tMain.GetMethod("UpdatePerformanceSection", $bfNonPubInst)
    $form.Visible = $true
    try {
        $kb = [Activator]::CreateInstance($infoType)
        $kb.Brand = "Razer"; $kb.DeviceId = "Razer:1532:0216"; $kb.DeviceName = "Razer BlackWidow X Chroma"
        $kb.Category = [Enum]::Parse($catType, "Keyboard"); $kb.IsConnected = $true; $kb.BatteryPercent = -1; $kb.Transport = "USB"
        [void]$mUpdate.Invoke($form, @($kb, [System.Drawing.Color]::FromArgb(0, 229, 255)))

        Assert-True (-not $tMain.GetField("lblDpiTitle", $bfNonPubInst).GetValue($form).Visible) "Keyboard hides lblDpiTitle"
        Assert-True (-not $tMain.GetField("btnDpiManage", $bfNonPubInst).GetValue($form).Visible) "Keyboard hides btnDpiManage"
        Assert-True ($tMain.GetField("tileRapooDpi", $bfNonPubInst).GetValue($form).Visible) "Keyboard shows status tile"
        Assert-True ($tMain.GetField("hudThreeLocks", $bfNonPubInst).GetValue($form).Visible) "Keyboard shows ThreeLocks"
        Assert-True ($tMain.GetField("btnUniversalWinLock", $bfNonPubInst).GetValue($form).Visible) "Keyboard shows WinLock"
        Assert-True ($tMain.GetField("btnKeyTestSpeed", $bfNonPubInst).GetValue($form).Visible) "Keyboard shows KeyTestSpeed"
        Assert-True ($tMain.GetField("btnKeyboardSettings", $bfNonPubInst).GetValue($form).Visible) "Keyboard shows KeyboardSettings"
        Assert-True (-not $tMain.GetField("dpiMenu", $bfNonPubInst).GetValue($form).Visible) "Keyboard hides tray dpiMenu"

        $ms = [Activator]::CreateInstance($infoType)
        $ms.Brand = "Razer"; $ms.DeviceId = "Razer:1532:00B7"; $ms.DeviceName = "Razer DeathAdder V3 Pro"
        $ms.Category = [Enum]::Parse($catType, "Mouse"); $ms.IsConnected = $true; $ms.BatteryPercent = 80
        [void]$mUpdate.Invoke($form, @($ms, [System.Drawing.Color]::FromArgb(0, 255, 0)))

        Assert-True ($tMain.GetField("lblDpiTitle", $bfNonPubInst).GetValue($form).Visible) "Mouse shows lblDpiTitle"
        Assert-True ($tMain.GetField("lblRateTitle", $bfNonPubInst).GetValue($form).Visible) "Mouse shows lblRateTitle"
        Assert-True (-not $tMain.GetField("hudThreeLocks", $bfNonPubInst).GetValue($form).Visible) "Mouse hides ThreeLocks"
    } finally {
        $form.Dispose()
    }
}

Invoke-TestCase "Razer.OldModelsMultiInterfaceBusyRetryAndClassicPolling" {
    # 1. ShouldReplaceCandidateDevice: live always beats fallback; wired beats dongle
    $mReplace = $rzType.GetMethod("ShouldReplaceCandidateDevice", $bfPubStatic)
    Assert-True ($null -ne $mReplace) "ShouldReplaceCandidateDevice missing"

    $fallback = [Activator]::CreateInstance($infoType)
    $fallback.DeviceId = "Razer:1532:00A5"; $fallback.IsConnected = $true; $fallback.IsSleeping = $false; $fallback.IsDonglePresent = $true; $fallback.BatteryPercent = 50
    $liveDongle = [Activator]::CreateInstance($infoType)
    $liveDongle.DeviceId = "Razer:1532:00A5"; $liveDongle.IsConnected = $true; $liveDongle.IsSleeping = $false; $liveDongle.IsDonglePresent = $true; $liveDongle.BatteryPercent = 82
    $liveWired = [Activator]::CreateInstance($infoType)
    $liveWired.DeviceId = "Razer:1532:00A5"; $liveWired.IsConnected = $true; $liveWired.IsSleeping = $false; $liveWired.IsDonglePresent = $false; $liveWired.BatteryPercent = 84

    Assert-True ([bool]$mReplace.Invoke($null, @($fallback, $false, $liveDongle, $true))) "Live dongle must replace fallback"
    Assert-True (-not [bool]$mReplace.Invoke($null, @($liveDongle, $true, $fallback, $false))) "Fallback must NOT overwrite live"
    Assert-True ([bool]$mReplace.Invoke($null, @($liveDongle, $true, $liveWired, $true))) "Live wired must beat live dongle"

    # 2. ReadRazerResponseWithBusyRetry: BUSY (0x01) -> BUSY (0x01) -> OK (0x02); NO_RESPONSE (0x04) stops fast
    $mBusy = $rzType.GetMethod("ReadRazerResponseWithBusyRetry", $bfPubStatic)
    Assert-True ($null -ne $mBusy) "ReadRazerResponseWithBusyRetry missing"

    $script:busyCallCount = 0
    $script:sleepDelays = New-Object System.Collections.Generic.List[int]
    $busyReader = [Func[byte[]]] {
        $script:busyCallCount++
        $r = New-Object byte[] 90
        if ($script:busyCallCount -le 2) {
            $r[0] = 0x01
        } else {
            $r[0] = 0x02; $r[6] = 0x07; $r[7] = 0x80; $r[9] = 0xCC
        }
        return $r
    }
    $sleepRec = [Action[int]] { param([int]$ms) $script:sleepDelays.Add($ms) }
    $busyArgs = [object[]]@($busyReader, [int]0, [byte]0x07, [byte]0x80, [int]4, $sleepRec, [byte]0)
    $busyRes = [byte[]]$mBusy.Invoke($null, $busyArgs)
    Assert-True ($null -ne $busyRes -and $busyRes[9] -eq 0xCC -and $script:busyCallCount -eq 3 -and $busyArgs[6] -eq 0x02) "BUSY retry recovers on 3rd attempt"

    $script:failCalls = 0
    $failReader = [Func[byte[]]] {
        $script:failCalls++
        $r = New-Object byte[] 90
        $r[0] = 0x04
        return $r
    }
    $failArgs = [object[]]@($failReader, [int]0, [byte]0x07, [byte]0x80, [int]4, $sleepRec, [byte]0)
    $failRes = $mBusy.Invoke($null, $failArgs)
    Assert-True ($null -eq $failRes -and $script:failCalls -eq 1 -and $failArgs[6] -eq 0x04) "0x04 NO_RESPONSE stops on 1st attempt"

    # 3. Old-model PID dictionary, dongle flags & classic polling detection
    $mName = $rzType.GetMethod("GetRazerOfficialModelName", $bfPubStatic, $null, @([int], [string]), $null)
    $mDongle = $rzType.GetMethod("DetermineIsDongle", $bfPubStatic)
    $mClassicPid = $rzType.GetMethod("IsClassicPollingProtocolPid", $bfPubStatic)
    $mDecClassic = $rzType.GetMethod("DecodeClassicPollingRateByte", $bfPubStatic)
    $mDecModern = $rzType.GetMethod("DecodeModernPollingRateByte", $bfPubStatic)

    foreach ($oldCase in @(
        @{ Pid = 0x007A; Exp = "Razer Viper Ultimate" },
        @{ Pid = 0x007B; Exp = "Razer Viper Ultimate" },
        @{ Pid = 0x007C; Exp = "Razer DeathAdder V2 Pro" },
        @{ Pid = 0x007D; Exp = "Razer DeathAdder V2 Pro" },
        @{ Pid = 0x008C; Exp = "Razer DeathAdder V2 Mini" },
        @{ Pid = 0x0084; Exp = "Razer DeathAdder V2" },
        @{ Pid = 0x0086; Exp = "Razer Basilisk Ultimate" },
        @{ Pid = 0x0088; Exp = "Razer Basilisk Ultimate" },
        @{ Pid = 0x0083; Exp = "Razer Basilisk X HyperSpeed" },
        @{ Pid = 0x0094; Exp = "Razer Orochi V2" }
    )) {
        Assert-Equal $oldCase.Exp ([string]$mName.Invoke($null, @([int]$oldCase.Pid, ""))) ("Old Razer PID 0x{0:X4}" -f $oldCase.Pid)
    }

    Assert-True (-not [bool]$mDongle.Invoke($null, @([int]0x0086, "", 0, $false))) "0x0086 Basilisk Ultimate wired is not dongle"
    Assert-True ([bool]$mDongle.Invoke($null, @([int]0x0088, "", 0, $false))) "0x0088 Basilisk Ultimate dongle"
    Assert-True ([bool]$mDongle.Invoke($null, @([int]0x0094, "", 0, $false))) "0x0094 Orochi V2 wireless"

    foreach ($cp in @(0x00A5, 0x00A6, 0x007B, 0x007D, 0x0086, 0x0094)) {
        Assert-True ([bool]$mClassicPid.Invoke($null, @([int]$cp))) ("Classic polling PID 0x{0:X4}" -f $cp)
    }
    foreach ($mp in @(0x00C1, 0x00BF, 0x00E6)) {
        Assert-True (-not [bool]$mClassicPid.Invoke($null, @([int]$mp))) ("Modern polling PID 0x{0:X4}" -f $mp)
    }

    Assert-Equal 1000 ([int]$mDecClassic.Invoke($null, @([byte]0x01))) "Classic 0x01 -> 1000Hz"
    Assert-Equal 500  ([int]$mDecClassic.Invoke($null, @([byte]0x02))) "Classic 0x02 -> 500Hz"
    Assert-Equal 125  ([int]$mDecClassic.Invoke($null, @([byte]0x08))) "Classic 0x08 -> 125Hz"
    Assert-Equal 8000 ([int]$mDecModern.Invoke($null, @([byte]0x01))) "Modern 0x01 -> 8000Hz"
    Assert-Equal 1000 ([int]$mDecModern.Invoke($null, @([byte]0x08))) "Modern 0x08 -> 1000Hz"

    # 4. RazerHeadsetHelper.PickBetterHeadsetCandidate prefers live connected headset over dead endpoint placeholder
    $hsType = $asm.GetType("RazerBatteryTray.RazerHeadsetHelper")
    $mPickHs = $hsType.GetMethod("PickBetterHeadsetCandidate", $bfPubStatic)
    Assert-True ($null -ne $mPickHs) "PickBetterHeadsetCandidate missing"
    $hsDead = [Activator]::CreateInstance($infoType)
    $hsDead.DeviceId = "RazerHeadset:1532:0555"; $hsDead.IsConnected = $false; $hsDead.IsSleeping = $true; $hsDead.BatteryPercent = -1
    $hsLive = [Activator]::CreateInstance($infoType)
    $hsLive.DeviceId = "RazerHeadset:1532:0555"; $hsLive.IsConnected = $true; $hsLive.IsSleeping = $false; $hsLive.BatteryPercent = 82
    $picked = $mPickHs.Invoke($null, @($hsDead, $hsLive))
    Assert-True ($picked.IsConnected -and $picked.BatteryPercent -eq 82) "Live headset endpoint beats dead endpoint"
    $pickedKeep = $mPickHs.Invoke($null, @($hsLive, $hsDead))
    Assert-True ($pickedKeep.IsConnected -and $pickedKeep.BatteryPercent -eq 82) "Dead headset endpoint cannot overwrite live"
}

Invoke-TestCase "Razer.WiredBatteryLessMouseNoFakeBatteryOrSleep" {
    $mCreateFallback = $rzType.GetMethod("CreateFallbackDeviceInfo", $bfPubStatic)
    $mIsWiredPid = $rzType.GetMethod("IsWiredOnlyRazerMousePid", $bfPubStatic)
    Assert-True ($null -ne $mCreateFallback) "CreateFallbackDeviceInfo missing"
    Assert-True ($null -ne $mIsWiredPid) "IsWiredOnlyRazerMousePid missing"
    Assert-True ([bool]$mIsWiredPid.Invoke($null, @([int]0x008C))) "0x008C DeathAdder V2 Mini is wired-only PID"

    $tState = $asm.GetType("RazerBatteryTray.RazerDeviceHardwareState")

    # 1. Known wired-only mouse (DeathAdder V2 Mini 1532:008C) with ConsecutiveFailures=5 & BatteryNotSupported=true
    $stV2Mini = [Activator]::CreateInstance($tState)
    $stV2Mini.ConsecutiveFailures = 5
    $stV2Mini.BatteryNotSupported = $true
    $stV2Mini.Dpi = 1600
    $stV2Mini.PollingRate = 1000
    $v2MiniInfo = $mCreateFallback.Invoke($null, @("Razer:1532:008C", [int]0x008C, "Razer DeathAdder V2 Mini", $false, $stV2Mini))
    Assert-Equal -1 $v2MiniInfo.BatteryPercent "V2 Mini must NOT fabricate 50% battery"
    Assert-True $v2MiniInfo.IsConnected "V2 Mini must stay IsConnected=true"
    Assert-True (-not $v2MiniInfo.IsSleeping) "V2 Mini must NEVER enter IsSleeping=true"
    Assert-Equal "USB 有线" $v2MiniInfo.Transport "V2 Mini transport"
    Assert-Equal 1600 $v2MiniInfo.Dpi "V2 Mini keeps cached DPI"

    # 2. Unknown wired battery-less mouse PID where firmware returned status=0x05 NOT_SUPPORTED
    $stUnknownWired = [Activator]::CreateInstance($tState)
    $stUnknownWired.ConsecutiveFailures = 5
    $stUnknownWired.BatteryNotSupported = $true
    $unknownWiredInfo = $mCreateFallback.Invoke($null, @("Razer:1532:00FF", [int]0x00FF, "Razer Wired Mouse", $false, $stUnknownWired))
    Assert-Equal -1 $unknownWiredInfo.BatteryPercent "BatteryNotSupported wired mouse must NOT fabricate 50% battery"
    Assert-True (-not $unknownWiredInfo.IsSleeping) "BatteryNotSupported wired mouse must NEVER sleep"
    Assert-True $unknownWiredInfo.IsConnected "BatteryNotSupported wired mouse remains connected"

    # 3. Wireless-capable mouse connected over USB wired channel (isDongle=false) must never sleep
    $stWiredCable = [Activator]::CreateInstance($tState)
    $stWiredCable.ConsecutiveFailures = 5
    $stWiredCable.BatteryPercent = 88
    $stWiredCable.BatteryNotSupported = $false
    $wiredCableInfo = $mCreateFallback.Invoke($null, @("Razer:1532:00BE", [int]0x00BE, "Razer DeathAdder V4 Pro", $false, $stWiredCable))
    Assert-Equal 88 $wiredCableInfo.BatteryPercent "Wired cable keeps cached battery"
    Assert-True (-not $wiredCableInfo.IsSleeping) "USB wired channel must NEVER enter IsSleeping=true"
}

Invoke-TestCase "Razer.V3ProPollingProtocolLockAndZeroStatusCap" {
    $mModern = $rzType.GetMethod("IsKnownModernPollingProtocolPid", $bfPubStatic)
    $mFallback = $rzType.GetMethod("ShouldAllowPollingProtocolFallback", $bfPubStatic)
    $mBusy = $rzType.GetMethod("ReadRazerResponseWithBusyRetry", $bfPubStatic)
    Assert-True ($null -ne $mModern -and $null -ne $mFallback) "Protocol lock helpers missing"

    $modernPids = @(0x00C0, 0x00C1, 0x00C2, 0x00C3, 0x00B6, 0x00B7, 0x009A, 0x009B, 0x00AA, 0x00AB, 0x00CC, 0x00CD, 0x00BE, 0x00BF, 0x00B9)
    foreach ($mp in $modernPids) {
        Assert-True ([bool]$mModern.Invoke($null, @([int]$mp))) ("IsKnownModernPollingProtocolPid true for 0x{0:X4}" -f $mp)
        Assert-True (-not [bool]$mFallback.Invoke($null, @([int]$mp))) ("Modern 8K PID 0x{0:X4} must lock protocol (no classic fallback)" -f $mp)
    }

    $classicPids = @(0x00A5, 0x00A6, 0x007A, 0x007B, 0x0086, 0x0088)
    foreach ($cp in $classicPids) {
        Assert-True (-not [bool]$mModern.Invoke($null, @([int]$cp))) ("IsKnownModernPollingProtocolPid false for classic 0x{0:X4}" -f $cp)
        Assert-True (-not [bool]$mFallback.Invoke($null, @([int]$cp))) ("Classic 1K PID 0x{0:X4} must lock protocol" -f $cp)
    }

    Assert-True ([bool]$mFallback.Invoke($null, @([int]0x00FE))) "Unknown PID 0x00FE allows fallback"
    Assert-True ([bool]$mFallback.Invoke($null, @([int]0x0000))) "Zero PID allows fallback"

    # 0x00 NEW/unprocessed must stop after at most 2 reads (not 4-read storm)
    $script:zeroCalls = 0
    $zeroReader = [Func[byte[]]] {
        $script:zeroCalls++
        return (New-Object byte[] 90)
    }
    $sleepNoop = [Action[int]] { param([int]$ms) }
    $zeroArgs = [object[]]@($zeroReader, [int]0, [byte]0x07, [byte]0x80, [int]4, $sleepNoop, [byte]0)
    $zeroRes = $mBusy.Invoke($null, $zeroArgs)
    Assert-True ($null -eq $zeroRes -and $script:zeroCalls -le 2) ("0x00 status must stop within <= 2 attempts, got " + $script:zeroCalls)

    # 0x01 BUSY still recovers on 3rd attempt
    $script:busyCalls2 = 0
    $busyReader2 = [Func[byte[]]] {
        $script:busyCalls2++
        $r = New-Object byte[] 90
        if ($script:busyCalls2 -le 2) { $r[0] = 0x01 }
        else { $r[0] = 0x02; $r[6] = 0x07; $r[7] = 0x80; $r[9] = 0xAA }
        return $r
    }
    $busyArgs2 = [object[]]@($busyReader2, [int]0, [byte]0x07, [byte]0x80, [int]4, $sleepNoop, [byte]0)
    $busyRes2 = [byte[]]$mBusy.Invoke($null, $busyArgs2)
    Assert-True ($null -ne $busyRes2 -and $script:busyCalls2 -eq 3) "0x01 BUSY retry recovers on 3rd attempt"

    # EP0 isolation: QueryAllRazerDevices must not invoke HidD_GetProductString before FeatureReportByteLength < 90 check
    $rzSrc = [System.IO.File]::ReadAllText((Join-Path $root "src\Hardware\RazerDeviceHelper.cs"), [System.Text.Encoding]::UTF8)
    $qStart = $rzSrc.IndexOf("public static List<MouseBatteryInfo> QueryAllRazerDevices()")
    $qEnd = $rzSrc.IndexOf("public static MouseBatteryInfo QueryRazerDeviceInfo()")
    $qBody = $rzSrc.Substring($qStart, $qEnd - $qStart)
    $idxFeatCheck = $qBody.IndexOf("if (caps.FeatureReportByteLength < 90)")
    $idxProdStr = $qBody.IndexOf("HidD_GetProductString(")
    Assert-True ($idxFeatCheck -gt 0 -and $idxProdStr -gt $idxFeatCheck) "HidD_GetProductString must stay strictly after FeatureReportByteLength < 90 guard"

    # FastQueryDpiCore must check !pathOpenFailed before falling through to 10s SetupDi enumeration
    $fStart = $rzSrc.IndexOf("private static bool FastQueryDpiCore(")
    $fEnd = $rzSrc.IndexOf("private static bool TryQueryRazerDpiByPath(")
    $fBody = $rzSrc.Substring($fStart, $fEnd - $fStart)
    $idxOpenFail = $fBody.IndexOf("if (!pathOpenFailed)")
    $idxSetupDi = $fBody.IndexOf("SetupDiGetClassDevs(")
    Assert-True ($idxOpenFail -gt 0 -and $idxSetupDi -gt $idxOpenFail) "FastQueryDpiCore returns on !pathOpenFailed before SetupDiGetClassDevs"
}
