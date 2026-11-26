# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# Consolidated Peripherals Suite: ASUS, Acer/XCTECH, Corsair M75 Air, Endgame Gear, GANSS, Ironcat, Keychron M6, LEOBOG, MOONDROP, NuPhy, Valkyrie, Zaopin
# Replaces 19 legacy scripts; zero live hardware writes or registry leaks.

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
. (Join-Path $PSScriptRoot "..\tools\TestHarness.ps1")

$asm = Load-FerrisPulseAssembly
$root = Get-FerrisPulseRoot
$bfPubStatic = [System.Reflection.BindingFlags]"Public,Static"
$bfAnyStatic = [System.Reflection.BindingFlags]"Public,NonPublic,Static"
$bfNonPubInst = [System.Reflection.BindingFlags]"NonPublic,Instance"

Invoke-TestCase "Periph.AcerXctechAndZaopin" {
    # Acer OMR331 / XCTECH VK M3 Lite DPI & polling + sandbox write isolation
    $tAcer = $asm.GetType("RazerBatteryTray.AcerDeviceHelper")
    $q = [byte[]]$tAcer.GetMethod("BuildInfoQueryFrame", $bfPubStatic).Invoke($null, @())
    Assert-True ($q.Length -eq 33 -and $q[1] -eq 0x10) "Acer 33B 0x10 query"

    [byte[]]$rxUnplugged = @(0,0x10,0,1,0x0B,0x52,0x33,0x33,0x32,0x85,1,0x19,1,0,0x3A,1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0xC5)
    $pArgs = [object[]]@($rxUnplugged.PSObject.BaseObject, 33, "", 0, $true, 0)
    Assert-True ([bool]$tAcer.GetMethod("ParseInfoResponse", $bfPubStatic).Invoke($null, $pArgs) -and $pArgs[2] -eq "R332" -and [int]$pArgs[3] -eq 58) "Acer R332 58%"

    $mEncDpi = $tAcer.GetMethod("EncodeDpiRaw", $bfPubStatic)
    $mDecDpi = $tAcer.GetMethod("DecodeDpiRaw", $bfPubStatic)
    foreach ($dpi in @(800, 1200, 2000, 5000, 8000, 12000)) {
        $raw = [int]$mEncDpi.Invoke($null, @([int]0x11, [int]$dpi))
        Assert-Equal $dpi ([int]$mDecDpi.Invoke($null, @([int]0x11, $raw))) ("Acer 0x11 DPI roundtrip $dpi")
    }
    foreach ($rp in @(@{Hz=125;F=8}, @{Hz=250;F=4}, @{Hz=500;F=2}, @{Hz=1000;F=1}, @{Hz=2000;F=0x10}, @{Hz=4000;F=0x20}, @{Hz=8000;F=0x40})) {
        Assert-Equal $rp.F ([int]$tAcer.GetMethod("RateFlagFromHz", $bfPubStatic).Invoke($null, @([int]$rp.Hz))) ("RateFlagFromHz " + $rp.Hz)
        Assert-Equal $rp.Hz ([int]$tAcer.GetMethod("RateHzFromFlag", $bfPubStatic).Invoke($null, @([int]$rp.F))) ("RateHzFromFlag " + $rp.F)
    }
    $txRate = [byte[]]$tAcer.GetMethod("BuildVendorFrame", $bfPubStatic).Invoke($null, @([byte]0x02, ([byte[]]@([byte]1)).PSObject.BaseObject, $true, 0))
    Assert-True ($txRate.Length -eq 33 -and $txRate[1] -eq 2 -and $txRate[3] -eq 1 -and $txRate[32] -eq 1) "Acer BuildVendorFrame"

    $acerDrv = [Activator]::CreateInstance($asm.GetType("RazerBatteryTray.AcerMouseDriver"))
    $tDev = [Activator]::CreateInstance($asm.GetType("RazerBatteryTray.MouseBatteryInfo"))
    $tDev.DeviceId = "TEST_ACER_VK_M3_LITE"; $tDev.Brand = "Acer"; $tDev.Dpi = 800; $tDev.DpiStageCount = 6
    Assert-True ([bool]$acerDrv.SetDpi($tDev, 1600, 1600, 2) -and $tDev.Dpi -eq 1600 -and $tDev.DpiStage -eq 2) "AcerMouseDriver TEST_ mock isolation"

    # Zaopin Z2 (VGN F526/F527 source contracts)
    $vgnSrc = [System.IO.File]::ReadAllText((Join-Path $root "src\Hardware\VgnDeviceHelper.cs"), [System.Text.Encoding]::UTF8)
    Assert-True ($vgnSrc.Contains("0xF526") -and $vgnSrc.Contains("0xF527") -and $vgnSrc.Contains("BuildReport17(4")) "Zaopin Z2 0xF526/0xF527 present"
}

Invoke-TestCase "Periph.AsusAzothPeltaAndRog" {
    $tAsus = $asm.GetType("RazerBatteryTray.AsusDeviceHelper")
    function New-Pkt([byte[]]$hdr) {
        $arr = New-Object byte[] 64; [Array]::Copy($hdr, 0, $arr, 0, $hdr.Length); return ,$arr
    }
    Assert-Equal 0x1A85 ([RazerBatteryTray.AsusDeviceHelper]::ParseAzothPairing((New-Pkt @(1,0xA0,0,1,0,0x85,0x1A,2,4)))) "Azoth pairing 0x1A85"
    Assert-Equal 0 ([RazerBatteryTray.AsusDeviceHelper]::ParseAzothPairing((New-Pkt @(1,0xA0,0,1,0,0x94,0x1A,2,4)))) "Non-Azoth rejected"

    $b = 0; $c = $false
    Assert-True ([RazerBatteryTray.AsusDeviceHelper]::TryParseBattery((New-Pkt @(2,0x12,1,0,0,0,0x5F,2,0,0,0x1E,0x5F,0x9B,0x10)), [ref]$b, [ref]$c) -and $b -eq 95 -and !$c) "Azoth 95%"
    Assert-True (-not [RazerBatteryTray.AsusDeviceHelper]::TryParseBattery((New-Pkt @(2,255,170)), [ref]$b, [ref]$c)) "Azoth sleep NAK rejected"

    # PELTA Headset + UI
    $peltaSample = New-Pkt @(0xCC,0x12,7,0,0,5,0x27,0x14,1)
    $pArgs = [object[]]@($peltaSample.PSObject.BaseObject, -1)
    Assert-True ([bool]$tAsus.GetMethod("TryParseBatteryReport", $bfAnyStatic).Invoke($null, $pArgs) -and [int]$pArgs[1] -eq 39) "PELTA 39%"

    # ROG Azoth 96 (1AD0)
    $rogPkt = New-Pkt @(0x02,0x12,1,0,0,0,0x64,0x03,1,0,0x14,0x64,0x32,0x11)
    $rArgs = [object[]]@($rogPkt.PSObject.BaseObject, [byte]0x02, 64, 0, $false)
    Assert-True ([bool]$tAsus.GetMethod("TryParseRogAzoth96Battery", $bfAnyStatic).Invoke($null, $rArgs) -and [int]$rArgs[3] -eq 100 -and ![bool]$rArgs[4]) "ROG 1AD0 100%"

    # PELTA UI check
    $form = New-SandboxMainForm $false
    $tMain = $form.GetType()
    try {
        $desc = [Activator]::CreateInstance($asm.GetType("RazerBatteryTray.HidDeviceDescriptor"))
        $desc.VendorId = 0x0B05; $desc.ProductId = 0x1B84
        $build = $tAsus.GetMethod("BuildPeltaDeviceInfo", $bfAnyStatic)
        if ($null -ne $build) {
            $pInfo = $build.Invoke($null, @($desc.PSObject.BaseObject, 0, $false, $true, [DateTime]::Now))
            [void]$tMain.GetMethod("UpdateUI", $bfNonPubInst).Invoke($form, @($pInfo, $false))
            Assert-Equal "0%" $tMain.GetField("lblBatteryBig", $bfNonPubInst).GetValue($form).Text "PELTA 0% rendered"
            Assert-True (-not [bool]$tMain.GetMethod("IsSelectedDeviceSupportingAutoGameRate", $bfNonPubInst).Invoke($form, @())) "Headset has no game rate opt"
        }
    } finally {
        $form.Dispose()
    }
}

Invoke-TestCase "Periph.CorsairEndgameGearIroncatLeobogMoondrop" {
    # Corsair M75 AIR
    $tCor = $asm.GetType("RazerBatteryTray.CorsairDeviceHelper")
    Assert-True ([bool]$tCor.GetMethod("IsSupportedMouseVidPid", $bfAnyStatic).Invoke($null, @(0x1B1C, 0x1BF2))) "M75 Air 1BF2"
    Assert-True ([bool]$tCor.GetMethod("IsSupportedMouseVidPid", $bfAnyStatic).Invoke($null, @(0x1B1C, 0x1BDC))) "M75 Air dongle 1BDC"
    Assert-True (-not [bool]$tCor.GetMethod("IsSupportedMouseVidPid", $bfAnyStatic).Invoke($null, @(0x1532, 0x1BF2))) "Corsair foreign VID rejected"

    # Endgame Gear XM2we
    $tEgg = $asm.GetType("RazerBatteryTray.EndgameGearDeviceHelper")
    $eggTx = [byte[]]$tEgg.GetMethod("BuildBatteryQuery", $bfAnyStatic).Invoke($null, @())
    Assert-True ($eggTx.Length -ge 16) "XM2we query"
    [byte[]]$eggPay = @(0x04,0,0,0,0,0,0,0,0,0,0,0,0,0,0)
    Assert-Equal ([byte]0x49) ([byte]$tEgg.GetMethod("ComputeChecksum", $bfAnyStatic).Invoke($null, @([byte]0x08, $eggPay.PSObject.BaseObject, 0, 15))) "XM2we checksum 0x49"

    # Ironcat ZERO
    $icTx = [RazerBatteryTray.IroncatDeviceHelper]::BuildBatteryQuery()
    Assert-True ($icTx.Length -eq 17 -and $icTx[0] -eq 8 -and $icTx[1] -eq 4 -and $icTx[16] -eq 0x49) "Ironcat 17B query"
    $icBat = 0; $icChg = $false; $icVolt = 0
    [byte[]]$icRx = @(0x09,0x04,0,0,0,2,0x5F,1,0,0,0,0,0,0,0,0,0xE6)
    Assert-True ([RazerBatteryTray.IroncatDeviceHelper]::ParseBatteryResponse($icRx, 17, [ref]$icBat, [ref]$icChg, [ref]$icVolt) -and $icBat -eq 95 -and $icChg) "Ironcat 95% charging"

    # LEOBOG GM5 Pro
    $lb = New-Object RazerBatteryTray.HidDeviceDescriptor
    $lb.VendorId = 0x04F3; $lb.ProductId = 0x026F; $lb.UsagePage = 0x000A; $lb.Usage = 0
    $lb.InputReportLength = 32; $lb.OutputReportLength = 32; $lb.DevicePath = "\\?\hid#vid_04f3&pid_026f#test"
    Assert-True ([RazerBatteryTray.LeobogDeviceHelper]::IsSupportedDevice($lb)) "LEOBOG GM5 Pro 04F3:026F"
    Assert-True ([RazerBatteryTray.LeobogDeviceHelper]::IsVendorEndpoint($lb)) "LEOBOG 0x000A endpoint"

    # MOONDROP GM-01 PRO
    $tMoon = $asm.GetType("RazerBatteryTray.MoondropDeviceHelper", $true)
    Assert-True ([bool]$tMoon.GetMethod("IsSupportedVidPid").Invoke($null, @(0x35D8, 0x0133))) "MOONDROP 35D8:0133"
    Assert-True (-not [bool]$tMoon.GetMethod("IsSupportedVidPid").Invoke($null, @(0x35D8, 0x0134))) "Other MOONDROP rejected"
    $mTx = [byte[]]$tMoon.GetMethod("BuildReadFrame").Invoke($null, @([byte]0x27))
    Assert-True ($mTx.Length -eq 64 -and $mTx[0] -eq 0x4B -and $mTx[1] -eq 0x80 -and $mTx[2] -eq 0x27) "MOONDROP BuildReadFrame"
    [byte[]]$mRx = New-Object byte[] 64; $mRx[0] = 0x4B; $mRx[1] = 0x80; $mRx[2] = 0x27; $mRx[3] = 6; $mRx[8] = 1; $mRx[9] = 0x5A
    $mArgs = [object[]]@($mRx.PSObject.BaseObject, 64, 0)
    Assert-True ([bool]$tMoon.GetMethod("TryParseBatteryResponse").Invoke($null, $mArgs) -and [int]$mArgs[2] -eq 90) "MOONDROP 90%"
}

Invoke-TestCase "Periph.GanssKeychronM6NuphyAndValkyrie" {
    # GANSS identity & USB wired vs 2.4G + UI
    $tGanss = $asm.GetType("RazerBatteryTray.GanssDeviceHelper")
    $cfg = [Activator]::CreateInstance($asm.GetType("RazerBatteryTray.CustomDeviceConfig"))
    $cfg.DeviceId = "HID:05AC:024F"; $cfg.OriginalName = "GS3108T-PRO 2.4G"
    $cfg.Category = [Enum]::Parse($asm.GetType("RazerBatteryTray.DeviceCategory"), "Mouse")
    $cfg.Transport = "蓝牙 (BLE)"; $cfg.CachedBattery = -1
    [void]$tGanss.GetMethod("NormalizeConfig").Invoke($null, @($cfg))
    Assert-True ($cfg.Category.ToString() -eq "Keyboard" -and $cfg.Transport -eq "2.4G 无线") "GANSS NormalizeConfig"
    $gQ = [byte[]]$tGanss.GetMethod("BuildBatteryQuery").Invoke($null, $null)
    Assert-True ($gQ.Length -eq 33 -and $gQ[1] -eq 0x20 -and $gQ[32] -eq 0x21) "GANSS BuildBatteryQuery"

    $form = New-SandboxMainForm $false
    $tMain = $form.GetType()
    try {
        $gDev = [Activator]::CreateInstance($asm.GetType("RazerBatteryTray.MouseBatteryInfo"))
        $gDev.Brand = "GANSS"; $gDev.DeviceId = "HID:05AC:024F"; $gDev.DeviceName = "GS3108T-PRO"
        $gDev.Category = [Enum]::Parse($asm.GetType("RazerBatteryTray.DeviceCategory"), "Keyboard")
        $gDev.Transport = "USB 有线"; $gDev.IsConnected = $true; $gDev.IsDonglePresent = $true; $gDev.BatteryPercent = -1
        [void]$tMain.GetMethod("UpdateUI", $bfNonPubInst).Invoke($form, @($gDev, $false))
        Assert-Equal "已连接 · 供电中" $tMain.GetField("lblBatteryBig", $bfNonPubInst).GetValue($form).Text "Wired GANSS powered display"
    } finally {
        $form.Dispose()
    }

    # Keychron M6 built-in offline protocol & isolation
    [RazerBatteryTray.BuiltInDrivers]::EnsureRegistered()
    $m6Drv = @([RazerBatteryTray.HardwareDriverRegistry]::GetAllDriversOrdered() | Where-Object DriverId -eq "BuiltIn:KeychronM6")[0]
    Assert-True ($null -ne $m6Drv -and !$m6Drv.IsExternalPackage -and $m6Drv.Priority -eq 100) "BuiltIn:KeychronM6 metadata"
    $tM6 = $asm.GetType("RazerBatteryTray.KeychronM6Helper")
    function Call-M6([string]$name, [object[]]$vals) {
        $m = $tM6.GetMethod($name, $bfAnyStatic)
        for ($i = 0; $i -lt $vals.Length; $i++) { if ($null -ne $vals[$i]) { $vals[$i] = $vals[$i].PSObject.BaseObject } }
        return $m.Invoke($null, $vals)
    }
    $kd = New-Object RazerBatteryTray.HidDeviceDescriptor
    $kd.VendorId = 0x3434; $kd.ProductId = 0xD026; $kd.UsagePage = 0xFFC1; $kd.Usage = 1; $kd.InputReportLength = 64; $kd.OutputReportLength = 64; $kd.DevicePath = "offline-m6"
    Assert-True (Call-M6 "IsControlEndpoint" @($kd)) "Keychron M6 0xFFC1/64 endpoint"
    $m6Id = New-Object byte[] 64; $m6Id[0] = 0xB6; $m6Id[1] = 2; $m6Id[4] = 0x34; $m6Id[5] = 0x34; $m6Id[6] = 0x60; $m6Id[7] = 0xD0
    Assert-True (Call-M6 "IsM6Identity" @(,$m6Id)) "M6 identity byte pattern"
    $rxM6 = New-Object byte[] 64; $rxM6[0] = 0xB4; $rxM6[1] = 6; $rxM6[20] = 0xD6
    $m6bArgs = [object[]]@($rxM6.PSObject.BaseObject, -1, $false)
    Assert-True ((Call-M6 "TryParseBattery" $m6bArgs) -and $m6bArgs[1] -eq 86 -and $m6bArgs[2]) "M6 86% charging"

    # NuPhy lighting cache & sandbox protection
    $tNuphy = $asm.GetType("RazerBatteryTray.NuphyDeviceHelper")
    $cachedLight = $tNuphy.GetField("CachedHardwareSettings", $bfPubStatic).GetValue($null)
    Assert-True ($null -ne $cachedLight) "NuPhy CachedHardwareSettings present"
    $cachedLight.BacklightBrightness = 75
    $cachedLight.BacklightEnabled = $true
    Assert-True (-not [bool]$tNuphy.GetMethod("SetBacklightEnabled", $bfPubStatic).Invoke($null, @($false))) "NuPhy write blocked in hardware sandbox"
    Assert-Equal 75 ([int]$tNuphy.GetField("LastActiveBacklightBrightness", $bfPubStatic).GetValue($null)) "NuPhy remembers 75 brightness"
    Assert-True ($cachedLight.BacklightEnabled) "Sandbox did not overwrite cached state"

    # Valkyrie VK 87 Max V2
    $tVk = $asm.GetType("RazerBatteryTray.ValkyrieDeviceHelper")
    $kbCat = [Enum]::Parse($asm.GetType("RazerBatteryTray.DeviceCategory"), "Keyboard")
    Assert-Equal "VK 87 Max V2" ([string]$tVk.GetMethod("GetModelName", $bfAnyStatic).Invoke($null, @(0x374A, 0xA272, "", $kbCat))) "Valkyrie 0xA272 model name"
}
