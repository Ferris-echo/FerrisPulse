# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# Consolidated Audio, BLE, Gamepad & SinoWealth/OpenMouse Suite
# Replaces 19 legacy scripts (Apple/Beats, Earbuds, Edifier, Corsair HS80, HyperX, SteelSeries, Sony DualSense/INZONE/PulseExplore/DS5Dongle, Beitong, GameSir, KZZI, OpenMouse, SinoWealth).
# Zero live hardware I/O; runs inside hardware-write & registry sandbox.

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
. (Join-Path $PSScriptRoot "..\tools\TestHarness.ps1")

$asm = Load-FerrisPulseAssembly
$root = Get-FerrisPulseRoot
$bfPubStatic = [System.Reflection.BindingFlags]"Public,Static"
$bfNonPubStatic = [System.Reflection.BindingFlags]"NonPublic,Static"
$bfAnyStatic = [System.Reflection.BindingFlags]"Public,NonPublic,Static"
$bfNonPubInst = [System.Reflection.BindingFlags]"NonPublic,Instance"

function Parse-HexBytes([string]$hex) {
    $tokens = $hex.Trim().Split(" ", [System.StringSplitOptions]::RemoveEmptyEntries)
    $arr = New-Object byte[] $tokens.Length
    for ($i = 0; $i -lt $tokens.Length; $i++) { $arr[$i] = [Convert]::ToByte($tokens[$i], 16) }
    return ,$arr
}

Invoke-TestCase "Audio.AppleEdifierCorsairHyperXAndSteelSeries" {
    # Apple/Beats source contract (no hardcoded 85/90 + model matrix)
    $appleSrc = [System.IO.File]::ReadAllText((Join-Path $root "src\Hardware\AppleDeviceHelper.cs"), [System.Text.Encoding]::UTF8)
    Assert-True (-not $appleSrc.Contains("? battery : 85") -and $appleSrc.Contains("AirPods Pro 2") -and $appleSrc.Contains("Magic Mouse")) "AppleDeviceHelper honest & model matrix"

    # Edifier Halo Space (0x2D99:0xE049 XOR 0xA5 decrypt)
    $tEd = $asm.GetType("RazerBatteryTray.EdifierDeviceHelper")
    $edTx = [byte[]]$tEd.GetMethod("BuildEcFrame", $bfPubStatic).Invoke($null, @([byte]0xD0, $null))
    Assert-True ($edTx.Length -eq 64 -and $edTx[0] -eq 0x2E -and $edTx[1] -eq 0xAA -and $edTx[2] -eq 0xEC -and $edTx[3] -eq 0xD0 -and $edTx[6] -eq 0x66) "Edifier 0xD0 query"

    [byte[]]$rxBatt = New-Object byte[] 64
    $rxBatt[0]=0x2F; $rxBatt[1]=0xBB; $rxBatt[2]=0xEC; $rxBatt[3]=0xD0; $rxBatt[5]=1; $rxBatt[6]=0x86; $rxBatt[7]=0xFE; $rxBatt[8]=0x5A; $rxBatt[9]=0xA3
    $edRepArgs = [object[]]@($rxBatt.PSObject.BaseObject, [byte]0xD0, $null)
    Assert-True ([bool]$tEd.GetMethod("TryParseEcReport", $bfPubStatic).Invoke($null, $edRepArgs)) "Edifier TryParseEcReport 0xD0"
    $mEdBat = $tEd.GetMethod("ParseBatteryPayload", $bfPubStatic)
    Assert-Equal 35 ([int]$mEdBat.Invoke($null, [object[]]@(,([byte[]]$edRepArgs[2]).PSObject.BaseObject))) "Edifier 0x86 ^ 0xA5 = 35%"

    [byte[]]$rxName = New-Object byte[] 64
    [byte[]]$nameMasked = @(0x4D,0x2F,0x14,0x40,0x23,0x28,0x85,0xED,0xC4,0xC9,0xCA,0x85,0xF6,0xD5,0xC4,0xC6,0xC0)
    $rxName[0]=0x2F; $rxName[1]=0xBB; $rxName[2]=0xEC; $rxName[3]=0xC9; $rxName[5]=0x11
    [Array]::Copy($nameMasked, 0, $rxName, 6, 17)
    $sum = 0; for ($i = 1; $i -le 22; $i++) { $sum += $rxName[$i] }; $rxName[23] = [byte]($sum -band 0xFF)
    $edNArgs = [object[]]@($rxName.PSObject.BaseObject, [byte]0xC9, $null)
    Assert-True ([bool]$tEd.GetMethod("TryParseEcReport", $bfPubStatic).Invoke($null, $edNArgs)) "Edifier TryParseEcReport 0xC9"
    Assert-Equal "花再 Halo Space" ([string]$tEd.GetMethod("ParseNamePayload", $bfPubStatic).Invoke($null, [object[]]@(,([byte[]]$edNArgs[2]).PSObject.BaseObject))) "Edifier name decode"

    # Corsair HS80 MAX + HyperX Cloud III / III S + SteelSeries Nova 5
    $tCor = $asm.GetType("RazerBatteryTray.CorsairDeviceHelper")
    Assert-True ([bool]$tCor.GetMethod("IsSupportedVidPid", $bfPubStatic).Invoke($null, @(0x1B1C, 0x0A97))) "Corsair HS80 MAX 0A97"
    $tHx3s = $asm.GetType("RazerBatteryTray.HyperXCloud3SHelper")
    Assert-True ([bool]$tHx3s.GetMethod("IsSupportedVidPid", $bfAnyStatic).Invoke($null, @(0x03F0, 0x02CC))) "HyperX Cloud III S 02CC"
    Assert-True (-not [bool]$tHx3s.GetMethod("IsSupportedVidPid", $bfAnyStatic).Invoke($null, @(0x03F0, 0x05B7))) "Cloud III 05B7 excluded"
    $tHx = $asm.GetType("RazerBatteryTray.HyperXDeviceHelper")
    Assert-True ([bool]$tHx.GetMethod("IsSupportedVidPid", $bfPubStatic).Invoke($null, @(0x03F0, 0x05B7))) "HyperX Cloud III 05B7"
    Assert-True (-not [bool]$tHx.GetMethod("IsSupportedVidPid", $bfPubStatic).Invoke($null, @(0x03F0, 0x06BE))) "Cloud III S 06BE excluded"
    $tSs = $asm.GetType("RazerBatteryTray.SteelSeriesDeviceHelper")
    Assert-True ([bool]$tSs.GetMethod("IsSupportedVidPid", $bfPubStatic).Invoke($null, @(0x1038, 0x2232))) "SteelSeries Nova 5 2232"
}

Invoke-TestCase "Sony.DualSenseInzonePulseExploreAndDs5Dongle" {
    # DualSense Edge USB
    [byte[]]$dsUsb = New-Object byte[] 64; $dsUsb[0] = 0x01; $dsUsb[53] = 0x10
    $dsSt = [RazerBatteryTray.SonyDeviceHelper]::ParseDualSenseUsbReport($dsUsb)
    Assert-True ($null -ne $dsSt -and $dsSt.BatteryPercent -eq 5 -and $dsSt.IsCharging -and $dsSt.Category.ToString() -eq "Gamepad") "DualSense Edge charging 5%"
    $dsUsb[53] = 0x20
    Assert-True ([RazerBatteryTray.SonyDeviceHelper]::ParseDualSenseUsbReport($dsUsb).BatteryPercent -eq 100) "DualSense Edge full 100%"
    Assert-True ($null -eq [RazerBatteryTray.SonyDeviceHelper]::ParseDualSenseUsbReport([byte[]]@(1, 0))) "DualSense rejects short frame"

    # DS5 Dongle 0xF9 (128-byte feature report)
    [byte[]]$ds5Rx = New-Object byte[] 128; $ds5Rx[0] = 0xF9; $ds5Rx[2] = 0x82; $ds5Rx[12] = 0x14
    $ds5St = [RazerBatteryTray.Ds5DongleDeviceHelper]::ParseBatteryReport($ds5Rx)
    Assert-True ($null -ne $ds5St -and $ds5St.BatteryPercent -eq 20 -and !$ds5St.IsCharging) "DS5 dongle 20% discharging"
    $ds5Rx[13] = 1
    Assert-True ([RazerBatteryTray.Ds5DongleDeviceHelper]::ParseBatteryReport($ds5Rx).IsCharging) "DS5 dongle charging"

    # Sony INZONE H5 & Buds
    $tInz = $asm.GetType("RazerBatteryTray.SonyInzoneHelper")
    [byte[]]$qInz = $tInz.GetMethod("BuildQueryPacket", $bfPubStatic).Invoke($null, @([uint16]1))
    Assert-True ($qInz[0] -eq 0x02 -and $qInz[1] -eq 0x0C -and $qInz[6] -eq 0x96 -and $qInz[7] -eq 0xC3) "INZONE query header"
    $capture64 = New-Object byte[] 64
    [byte[]]$h5Cap = @(0x02,0x0E,0x04,0xFF,0x0B,0x00,0x96,0xC3,0x14,0x04,0x10,0x01,0x00,0x00,0x64,0xE6)
    [Array]::Copy($h5Cap, 0, $capture64, 0, $h5Cap.Length)
    $h5Args = [object[]]@($capture64.PSObject.BaseObject, $null)
    Assert-True ([bool]$tInz.GetMethod("ParseHeadsetBatteryResponse", $bfPubStatic).Invoke($null, $h5Args) -and $h5Args[1].LeftBattery -eq 100) "INZONE H5 100%"

    # Sony PULSE Explore + UI
    [byte[]]$peRx = New-Object byte[] 64
    $peRx[0]=0xB0; $peRx[1]=0x50; $peRx[2]=1; $peRx[29]=0x11; $peRx[39]=3; $peRx[55]=0xDD; $peRx[56]=0xFF
    $peSt = [RazerBatteryTray.SonyDeviceHelper]::ParsePulseExploreReport($peRx)
    Assert-True ($null -ne $peSt -and $peSt.LeftBatteryPercent -eq 87 -and $peSt.RightBatteryPercent -eq 87) "PULSE Explore 87%/87%"
    $peInfo = [RazerBatteryTray.SonyDeviceHelper]::CreatePulseExploreInfo($peSt)
    Assert-True ($peInfo.BatteryPercent -eq 87 -and $peInfo.Category.ToString() -eq "Earbuds") "PULSE Explore Earbuds info"

    $form = New-SandboxMainForm $false
    $tMain = $form.GetType()
    try {
        [void]$tMain.GetMethod("UpdatePerformanceSection", $bfNonPubInst).Invoke($form, @($peInfo, [System.Drawing.Color]::Blue))
        $tile = $tMain.GetField("tileHeadsetAudio", $bfNonPubInst).GetValue($form)
        Assert-True ($tile.Title.Contains("PULSE Explore") -and $tile.StatusText.Contains("87%")) "PULSE Explore UI tile"
    } finally {
        $form.Dispose()
    }
}

Invoke-TestCase "Gamepads.BeitongKp20dAndGameSirNova2AndUI" {
    # Beitong KP20D FDAS + Helper
    $adapterPath = Join-Path $root "examples\adapters\beitong_btp_kp20d.fp-adapter.json"
    Assert-True (Test-Path $adapterPath) "beitong_btp_kp20d.fp-adapter.json exists"
    $adapter = (Get-Content $adapterPath -Raw -Encoding UTF8) | ConvertFrom-Json
    Assert-True ($adapter.fdasVersion -eq "2.0" -and $adapter.metadata.category -eq "Gamepad" -and [RazerBatteryTray.BeitongDeviceHelper]::BuildBatteryQuery().Length -eq 0) "Beitong push-only contract"
    $pct = -1; $chg = $false
    Assert-True ([RazerBatteryTray.BeitongDeviceHelper]::ParseBatteryResponse((Parse-HexBytes $adapter.capabilities.battery.sampleRxHex), 64, [ref]$pct, [ref]$chg) -and $pct -eq 100 -and $chg) "Beitong 100% charging"
    Assert-True ([RazerBatteryTray.BeitongDeviceHelper]::ParseBatteryResponse((Parse-HexBytes $adapter.capabilities.battery.chargingSampleRxHex), 64, [ref]$pct, [ref]$chg) -and $pct -eq 86 -and !$chg) "Beitong 86% idle"

    # GameSir Nova 2 Lite
    $tGs = $asm.GetType("RazerBatteryTray.GameSirDeviceHelper")
    $gsQ = [byte[]]$tGs.GetMethod("BuildInfoQuery").Invoke($null, $null)
    Assert-True ($gsQ.Length -eq 65 -and $gsQ[1] -eq 1 -and $gsQ[2] -eq 1) "GameSir read-only query"
    [byte[]]$gsFrame = New-Object byte[] 65; $gsFrame[1]=1; $gsFrame[2]=0xA9; $gsFrame[3]=0x63; $gsFrame[4]=0x45; $gsFrame[5]=0xD8; $gsFrame[6]=0xCE; $gsFrame[7]=1; $gsFrame[19]=87
    $mCreateGs = $tGs.GetMethod("CreateDeviceInfo")
    $wl = $mCreateGs.Invoke($null, @($gsFrame.PSObject.BaseObject, 0x1098, $null))
    Assert-True ($wl.BatteryPercent -eq 87 -and !$wl.IsCharging -and $wl.Category.ToString() -eq "Gamepad") "GameSir 87% wireless"
    $gsFrame[19] = 255
    $firstWired = $mCreateGs.Invoke($null, @($gsFrame.PSObject.BaseObject, 0x100F, $null))
    Assert-True ($firstWired.IsCharging -and $firstWired.BatteryPercent -eq -1) "GameSir first wired unknown %"

    $form = New-SandboxMainForm $false
    $tMain = $form.GetType()
    try {
        [void]$tMain.GetMethod("UpdateUI", $bfNonPubInst).Invoke($form, @($firstWired, $false))
        Assert-Equal "充电中" $tMain.GetField("lblBatteryBig", $bfNonPubInst).GetValue($form).Text "GameSir sentinel renders charging state"
    } finally {
        $form.Dispose()
    }
}

Invoke-TestCase "BleAndMatrix.KzziOpenMouseAndSinoWealth" {
    # KZZI K75 + BlePeripheralDriver
    [byte[]]$kzSt = @(0x13,0x05,0x01,0x00,0x0A,0x02,0x00,0x00,0x00,0x00,0x37,0x00,0x01,0x00,0x00,0x00,0x00,0x00,0x00,0x5D)
    Assert-True ([RazerBatteryTray.KzziDeviceHelper]::IsKzziStatusFrame($kzSt, $kzSt.Length)) "KZZI status frame"
    $kzQ = [RazerBatteryTray.KzziDeviceHelper]::BuildStatusQuery()
    Assert-True ($kzQ.Length -eq 20 -and $kzQ[19] -eq 0x1B) "KZZI query"
    $kzDrv = New-Object RazerBatteryTray.KzziKeyboardDriver
    $kzDev = [Activator]::CreateInstance($asm.GetType("RazerBatteryTray.MouseBatteryInfo"))
    $kzDev.Category = [RazerBatteryTray.DeviceCategory]::Keyboard
    $kzDev.Brand = "KZZI"
    $kzDev.DeviceId = "KZZI:3554:FA09"
    Assert-True ($kzDrv.CanHandle($kzDev)) "KzziKeyboardDriver"
    Assert-True (-not [RazerBatteryTray.DeviceManager]::ShouldHideDevice($kzDev, $false, $true)) "Brand KZZI survives generic hide"
    $bleDrv = New-Object RazerBatteryTray.BlePeripheralDriver
    $bleDev = [Activator]::CreateInstance($asm.GetType("RazerBatteryTray.MouseBatteryInfo"))
    $bleDev.DriverId = "BuiltIn:BlePeripheral"
    Assert-True ($bleDrv.CanHandle($bleDev)) "BlePeripheralDriver"

    # OpenMouse 24+ brands + Earbuds ConfigManager sandbox test
    $ugSrc = [System.IO.File]::ReadAllText((Join-Path $root "src\Hardware\UniversalGamingDeviceHelper.cs"), [System.Text.Encoding]::UTF8)
    foreach ($brand in @("LAMZU", "VAXEE", "WLmouse", "Pulsar", "Endgame Gear", "Keychron", "Mchose", "Finalmouse")) {
        Assert-True ($ugSrc.IndexOf($brand, [System.StringComparison]::OrdinalIgnoreCase) -ge 0) "OpenMouse matrix $brand"
    }

    # SinoWealth 258A:010C
    $tSw = $asm.GetType("RazerBatteryTray.SinoWealthDeviceHelper")
    Assert-True ([bool]$tSw.GetMethod("IsSinoWealthDevice", $bfPubStatic).Invoke($null, @("HID\VID_258A&PID_010C&MI_01"))) "SinoWealth 258A:010C"
    Assert-True (-not [bool]$tSw.GetMethod("IsSinoWealthDevice", $bfPubStatic).Invoke($null, @("HID\VID_249A&PID_5C2F&MI_00"))) "SinoWealth excludes Acer"
    $fSw = New-Object byte[] 29; $fSw[0] = 4; $fSw[1] = 0x11; $fSw[10] = 0x81; $fSw[11] = 0x23
    for ($i = 0; $i -lt 16; $i++) { $fSw[13 + $i] = [byte](0x10 + $i * 4) }
    $swArgs = [object[]]@($fSw.PSObject.BaseObject, 29, 0, $false, 0, 0, $null)
    Assert-True ([bool]$tSw.GetMethod("ParseConfig", $bfNonPubStatic).Invoke($null, $swArgs) -and [int]$swArgs[2] -eq 125 -and [bool]$swArgs[3] -and [int]$swArgs[4] -eq 2 -and [int]$swArgs[5] -eq 3) "SinoWealth ParseConfig 125Hz XY stage 2/3"
}
