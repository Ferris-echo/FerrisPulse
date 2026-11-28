# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# Consolidated VGN / ATK / VXE / CompX Brand Test Suite
# Replaces 13 legacy scripts: test_atk_* (7), test_vgn_* (4), test_compx_* (1), test_vxe_nearlink_r1 (1).
# Pure offline protocol replay + FDAS JSON verification + sandbox UI tests; zero hardware writes.

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

Invoke-TestCase "Atk.A87ProA9SharedReceiverAndUniversalCatalog" {
    # ATK A87 PRO FDAS JSON + helper
    $adapterPath = Join-Path $root "examples\adapters\atk_a87pro.fp-adapter.json"
    Assert-True (Test-Path $adapterPath) "atk_a87pro.fp-adapter.json exists"
    $adapter = (Get-Content $adapterPath -Raw -Encoding UTF8) | ConvertFrom-Json
    Assert-True ($adapter.fdasVersion -eq "2.0" -and $adapter.hardware.vendorId -eq "0x373B" -and $adapter.hardware.productId -eq "0x111F") "ATK A87 PRO adapter"

    $q = [RazerBatteryTray.AtkA87ProHelper]::BuildBatteryQuery()
    Assert-True ($q.Length -eq 33 -and $q[1] -eq 0x02 -and $q[2] -eq 0x96 -and $q[3] -eq 0x24) "A87 query framing"
    $pct = -1; $chg = $false
    Assert-True ([RazerBatteryTray.AtkA87ProHelper]::ParseBatteryResponse((Parse-HexBytes $adapter.capabilities.battery.sampleRxHex), 33, [ref]$pct, [ref]$chg) -and $pct -eq 100 -and !$chg) "A87 100% full"
    Assert-True ([RazerBatteryTray.AtkA87ProHelper]::ParseBatteryResponse((Parse-HexBytes $adapter.capabilities.battery.chargingSampleRxHex), 33, [ref]$pct, [ref]$chg) -and $pct -eq 91 -and $chg) "A87 91% charging"

    # Shared receiver A9 Ultimate identity + WE checksum + product catalog
    $a9Tx = [RazerBatteryTray.UniversalGamingDeviceHelper]::BuildAtkReceiverIdentityRequest()
    Assert-True ($a9Tx.Length -eq 17 -and $a9Tx[0] -eq 8 -and $a9Tx[1] -eq 0x10 -and $a9Tx[16] -eq 0x3D) "A9 identity frame"
    [byte[]]$a9Id = @(0x08, 0x10, 0, 0, 0, 2, 1, 3, 0, 0, 0, 0, 0, 0, 0, 0, 0x37)
    Assert-True ([RazerBatteryTray.UniversalGamingDeviceHelper]::IsAtkA9UltimateIdentity($a9Id)) "A9 identity accepted"
    $a9Id[16] = 0x38
    Assert-True (-not [RazerBatteryTray.UniversalGamingDeviceHelper]::IsAtkA9UltimateIdentity($a9Id)) "A9 bad checksum rejected"

    $tUg = $asm.GetType("RazerBatteryTray.UniversalGamingDeviceHelper")
    $mLookup = $tUg.GetMethod("LookupProduct", $bfPubStatic)
    Assert-Equal "ATK A9 AIR 8K 接收器" ($mLookup.Invoke($null, @(0x373B, 0x1279, ""))).ModelName "ATK A9 AIR"
    Assert-Equal "ATK X1 V2 8K 接收器" ($mLookup.Invoke($null, @(0x373B, 0x12E9, ""))).ModelName "ATK X1 V2"
    Assert-Equal "ATK F1 Ultimate 2.0" ($mLookup.Invoke($null, @(0x373B, 0x11D9, "8k dongle-L"))).ModelName "ATK F1 Ultimate 2.0"
    Assert-Equal "怒喵 AM Infinity .97 (2.4G)" ($mLookup.Invoke($null, @(0x0E8D, 0x0703, ""))).ModelName "Angry Miao AM Infinity"

    [byte[]]$we15 = New-Object byte[] 15; $we15[0] = 0x04
    Assert-Equal ([byte]0x49) ([byte]$tUg.GetMethod("CalculateWeChecksum", $bfPubStatic).Invoke($null, @([byte]0x08, $we15.PSObject.BaseObject))) "WE checksum 0x49"

    [byte[]]$rxWe = @(0x08, 0x04, 0, 0, 0, 0x02, 0x4B, 0, 0x0F, 0x5C, 0, 0, 0, 0, 0, 0, 0xF2)
    $bArgs = [object[]]@($rxWe.PSObject.BaseObject, 17, 0, $false, 0)
    Assert-True ([bool]$tUg.GetMethod("ParseBekenWeBatteryResponse", $bfPubStatic).Invoke($null, $bArgs) -and [int]$bArgs[2] -eq 75 -and -not [bool]$bArgs[3]) "Beken WE 75% discharging"
}

Invoke-TestCase "Atk.LightingProtocolRollbackAndDialogUI" {
    $tAtk = $asm.GetType("RazerBatteryTray.AtkDeviceHelper", $true)
    [byte[]]$cfg = @(0,20,2,3,0,0,255,129,155,3,0,0,0,180,0,255,255,255,0,0,1,0,0,0,0,0,1,0,0,1,4,1,0,0,1,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0)
    $settings = [RazerBatteryTray.AtkDeviceHelper]::DecodeLightingConfig($cfg)
    Assert-True ($settings.Mode -eq 20 -and $settings.Enabled -and $settings.Brightness -eq 2 -and $settings.Speed -eq 1) "DecodeLightingConfig"
    Assert-True ($settings.Red -eq 255 -and $settings.Green -eq 129 -and $settings.Blue -eq 155) "DecodeLightingConfig RGB"

    $settings.Mode = 5; $settings.Enabled = $false; $settings.Brightness = 1; $settings.Speed = 4
    $settings.Red = 12; $settings.Green = 34; $settings.Blue = 56; $settings.Rainbow = $true
    $upd = [RazerBatteryTray.AtkDeviceHelper]::PatchLightingConfig($cfg, $settings)
    Assert-True ($upd[1] -eq 5 -and $upd[2] -eq 1 -and $upd[3] -eq 0 -and $upd[5] -eq 255 -and $upd[28] -eq 1) "PatchLightingConfig"

    # Sandbox guard: ApplyLightingSettings must throw InvalidOperationException in test mode
    $threwSandbox = $false
    try { [void][RazerBatteryTray.AtkDeviceHelper]::ApplyLightingSettings("ATK:320F:5088", $settings) } catch { $threwSandbox = $true }
    Assert-True $threwSandbox "ApplyLightingSettings blocked in hardware sandbox"

    # Rollback test via CommitLightingConfig
    $commit = $tAtk.GetMethod("CommitLightingConfig", $bfNonPubStatic)
    $store = [pscustomobject]@{ Config = [byte[]]$cfg.Clone(); Writes = 0; FailOnce = $true }
    $write = [Action[byte[]]] { param($v); $store.Writes++; if ($store.FailOnce) { $store.FailOnce = $false; $store.Config = New-Object byte[] 56; throw "partial" }; $store.Config = [byte[]]$v.Clone() }
    $read = [Func[byte[]]] { return ,$store.Config }
    $rolledBack = $false
    try { [void]$commit.Invoke($null, @($cfg.PSObject.BaseObject, $upd.PSObject.BaseObject, $write, $read)) } catch { $rolledBack = $_.Exception.ToString().Contains("已恢复原配置") }
    Assert-True ($rolledBack -and $store.Writes -eq 2 -and [BitConverter]::ToString($store.Config) -eq [BitConverter]::ToString($cfg)) "Partial write triggers rollback"

    # AtkLightingDialog + MainForm button gating
    $dlgType = $asm.GetType("RazerBatteryTray.AtkLightingDialog", $true)
    $dlg = [Activator]::CreateInstance($dlgType, @("ATK:320F:5088", [single]1))
    try {
        $dlgType.GetMethod("DisplaySettings", $bfNonPubInst).Invoke($dlg, @($settings))
        $modeBox = $dlgType.GetField("modeBox", $bfNonPubInst).GetValue($dlg)
        Assert-Equal 20 $modeBox.Items.Count "20 lighting modes"
    } finally {
        $dlg.Dispose()
    }

    $main = New-SandboxMainForm $false
    try {
        $tMain = $main.GetType()
        $btn = $tMain.GetField("btnAtkLighting", $bfNonPubInst).GetValue($main)
        $mUpdate = $tMain.GetMethod("UpdatePerformanceSection", $bfNonPubInst)
        $mGetState = [System.Windows.Forms.Control].GetMethod("GetState", $bfNonPubInst)
        $info = [Activator]::CreateInstance($asm.GetType("RazerBatteryTray.MouseBatteryInfo"))
        $info.Brand = "ATK"; $info.DeviceId = "ATK:320F:5088"; $info.Category = [RazerBatteryTray.DeviceCategory]::Keyboard
        $info.DeviceName = "ATK V75X"; $info.BatteryPercent = 87; $info.IsConnected = $true; $info.Transport = "2.4G 无线"
        [void]$mUpdate.Invoke($main, @($info, [System.Drawing.Color]::Gold))
        Assert-True ([bool]$mGetState.Invoke($btn, @(2)) -and $btn.Enabled) "ATK V75X exposes btnAtkLighting"
        $info.DeviceId = "ATK:320F:5055"
        [void]$mUpdate.Invoke($main, @($info, [System.Drawing.Color]::Gold))
        Assert-True (-not [bool]$mGetState.Invoke($btn, @(2))) "Untested ATK hides btnAtkLighting"
    } finally {
        $main.Dispose()
    }
}

Invoke-TestCase "VgnAndCompx.CrossBrandIsolationAndFrames" {
    # ATK V75X vs VGN V98 Pro vs VGN N75 Pro shared 320F:5088 isolation
    function New-HidDesc([int]$vid, [int]$pidVal, [string]$prod, [int]$page = 0xFF1C, [int]$usage = 0x92) {
        $d = New-Object RazerBatteryTray.HidDeviceDescriptor
        $d.VendorId = $vid; $d.ProductId = $pidVal; $d.ProductString = $prod
        $d.UsagePage = $page; $d.Usage = $usage
        return $d
    }
    Assert-True ([RazerBatteryTray.AtkDeviceHelper]::IsSupportedDevice((New-HidDesc 0x320F 0x5088 "VGN V98 PRO 2.4G Dongle"))) "V98 PRO accepted"
    Assert-True ([RazerBatteryTray.AtkDeviceHelper]::IsSupportedDevice((New-HidDesc 0x320F 0x5088 "ATK V75X 2.4G Dongle"))) "ATK V75X accepted"
    Assert-True (-not [RazerBatteryTray.AtkDeviceHelper]::IsSupportedDevice((New-HidDesc 0x320F 0x5088 "Generic USB Keyboard"))) "Generic rejected"
    Assert-True ([RazerBatteryTray.VgnN75ProHelper]::IsSupportedDevice((New-HidDesc 0x320F 0x5088 "VGN N75 PRO 2.4G Dongle"))) "VGN N75 PRO accepted"
    Assert-True (-not [RazerBatteryTray.VgnN75ProHelper]::IsSupportedDevice((New-HidDesc 0x320F 0x5088 "ATK V75X 2.4G Dongle"))) "ATK V75X excluded from VgnN75ProHelper"

    # CompX 05AC:024F isolation against GS3108T-PRO and Magic Keyboard
    $tCx = $asm.GetType("RazerBatteryTray.CompxWirelessKeyboardHelper")
    Assert-True ([bool]$tCx.GetMethod("IsRecognizedProduct", $bfAnyStatic).Invoke($null, @("2.4G Wireless Receiver", 0x05AC, 0x024F))) "CompX 05AC:024F"
    Assert-True (-not [bool]$tCx.GetMethod("IsRecognizedProduct", $bfAnyStatic).Invoke($null, @("GS3108T-PRO 2.4G", 0x05AC, 0x024F))) "GS3108T-PRO stays Ganss"
    Assert-True (-not [bool]$tCx.GetMethod("IsRecognizedProduct", $bfAnyStatic).Invoke($null, @("Magic Keyboard", 0x05AC, 0x024F))) "Apple Magic excluded"
    $cxOk = New-HidDesc 0x05AC 0x024F "2.4G Wireless Receiver" 0xFF60 0x0061; $cxOk.InputReportLength = 33; $cxOk.OutputReportLength = 33
    Assert-True ([bool]$tCx.GetMethod("IsBatteryEndpoint", $bfAnyStatic).Invoke($null, @($cxOk.PSObject.BaseObject))) "CompX 33B endpoint"
    $cx65 = New-HidDesc 0x05AC 0x024F "2.4G Wireless Receiver" 0xFF60 0x0061; $cx65.InputReportLength = 65; $cx65.OutputReportLength = 65
    Assert-True (-not [bool]$tCx.GetMethod("IsBatteryEndpoint", $bfAnyStatic).Invoke($null, @($cx65.PSObject.BaseObject))) "CompX 65B config excluded"

    # VGN Neon75 & V108
    $rxN = New-Object byte[] 65; $rxN[2] = 0x64; $rxN[5] = 1; $rxN[6] = 1; $rxN[7] = 1
    $bN = 0; $onN = $false; $chN = $false; $kbN = $false; $msN = $false
    Assert-True ([RazerBatteryTray.VgnKeyboardHelper]::TryParseStatus($rxN, [ref]$bN, [ref]$onN, [ref]$chN, [ref]$kbN, [ref]$msN) -and $bN -eq 100 -and $onN -and !$chN) "Neon75 100%"

    $tVgnKb = $asm.GetType("RazerBatteryTray.VgnKeyboardHelper", $true)
    Assert-True ([bool]$tVgnKb.GetMethod("IsVgnV108").Invoke($null, @("VGN:391D:2A02:KB"))) "V108 DeviceId"
    Assert-True (-not [bool]$tVgnKb.GetMethod("IsVgnV108").Invoke($null, @("VGN:391D:1A03:MS"))) "Yjx excluded from V108"
    $v108Tx = [byte[]]$tVgnKb.GetMethod("BuildV108Frame").Invoke($null, @([byte]0x4A, [byte]2, $null))
    Assert-Equal "13-4A-01-00-02-00-00-00-00-00-00-00-00-00-00-00-00-00-00-60" ([BitConverter]::ToString($v108Tx)) "V108 battery TX"
    [byte[]]$v108Rx = @(0x13, 0x4A, 0x01, 0x00, 0x02, 0x5A, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xBA)
    Assert-True ([bool]$tVgnKb.GetMethod("IsV108Response").Invoke($null, @($v108Rx.PSObject.BaseObject, 20, [byte]0x4A, 2))) "V108 battery RX"
}

Invoke-TestCase "Vxe.NearLinkR1ProtocolAndTrayCustomActions" {
    $tVxe = $asm.GetType("RazerBatteryTray.VxeNearLinkDeviceHelper", $true)
    Assert-True ([bool]$tVxe.GetMethod("IsSupportedVidPid", $bfPubStatic).Invoke($null, @([int]0x373B, [int]0x1216))) "VXE R1 NearLink dongle accepted"
    Assert-True ([bool]$tVxe.GetMethod("IsSupportedVidPid", $bfPubStatic).Invoke($null, @([int]0x373B, [int]0x124C))) "VXE R1 NearLink wired PID accepted"
    Assert-True (-not [bool]$tVxe.GetMethod("IsSupportedVidPid", $bfPubStatic).Invoke($null, @([int]0x373B, [int]0x1085))) "VXE R1 SE rejected"

    $epOk = New-Object RazerBatteryTray.HidDeviceDescriptor
    $epOk.VendorId = 0x373B; $epOk.ProductId = 0x1216; $epOk.UsagePage = 0xFF05; $epOk.Usage = 1
    $epOk.InputReportLength = 64; $epOk.OutputReportLength = 64; $epOk.DevicePath = "\\?\hid#vid_373b&pid_1216&mi_02&col04#test"
    Assert-True ([bool]$tVxe.GetMethod("IsNearLinkEndpoint", $bfPubStatic).Invoke($null, @($epOk.PSObject.BaseObject))) "NearLink 64B endpoint"

    # UniversalGamingDeviceHelper isolates both 373B:1216 and wired 373B:124C
    $tUg = $asm.GetType("RazerBatteryTray.UniversalGamingDeviceHelper", $true)
    $mUgCheck = $tUg.GetMethod("IsSupportedMouseCandidate", $bfAnyStatic)
    if ($null -ne $mUgCheck) {
        $wiredDesc = New-Object RazerBatteryTray.HidDeviceDescriptor
        $wiredDesc.VendorId = 0x373B; $wiredDesc.ProductId = 0x124C; $wiredDesc.DevicePath = "\\?\hid#vid_373b&pid_124c&mi_02#test"
        Assert-True (-not [bool]$mUgCheck.Invoke($null, @($wiredDesc.PSObject.BaseObject))) "UniversalGaming excludes 373B:124C"
    }

    $tx7 = [byte[]]$tVxe.GetMethod("BuildReport64", $bfPubStatic).Invoke($null, @([int]7, [int]1, [int]0x36))
    Assert-True ($tx7.Length -eq 64 -and $tx7[0] -eq 8 -and $tx7[2] -eq 0x72 -and $tx7[1] -eq ((0x72 + 2 + 0x36 + 1 + 7 + 1) -band 0xFF)) "NearLink BuildReport64"

    $rxBat65 = New-Object byte[] 64; $rxBat65[1] = 0x72; $rxBat65[5] = 0x07; $rxBat65[6] = 1; $rxBat65[7] = 65; $rxBat65[8] = 0x41; $rxBat65[9] = 0x0F
    $bArgs65 = [object[]]@($rxBat65.PSObject.BaseObject, 64, 0, 0)
    Assert-True ([bool]$tVxe.GetMethod("ParseBatteryResponse", $bfPubStatic).Invoke($null, $bArgs65) -and [int]$bArgs65[2] -eq 65 -and [int]$bArgs65[3] -eq 3905) "NearLink 65% 3905mV"

    $rxBat = New-Object byte[] 64; $rxBat[1] = 0x72; $rxBat[5] = 0x07; $rxBat[6] = 4; $rxBat[7] = 100; $rxBat[8] = 0x68; $rxBat[9] = 0x10
    $bArgs = [object[]]@($rxBat.PSObject.BaseObject, 64, 0, 0)
    Assert-True ([bool]$tVxe.GetMethod("ParseBatteryResponse", $bfPubStatic).Invoke($null, $bArgs) -and [int]$bArgs[2] -eq 100 -and [int]$bArgs[3] -eq 4200) "NearLink 100% 4200mV"

    $rxChg = New-Object byte[] 64; $rxChg[1] = 0x72; $rxChg[5] = 0x0F; $rxChg[7] = 1
    $cArgs = [object[]]@($rxChg.PSObject.BaseObject, 64, $false)
    Assert-True ([bool]$tVxe.GetMethod("ParseChargingResponse", $bfPubStatic).Invoke($null, $cArgs) -and [bool]$cArgs[2]) "NearLink charging"

    # VgnDeviceHelper TryExtractVgnBatteryFromReply (supports 9B legacy and 10B+ voltage frames)
    $tVgn = $asm.GetType("RazerBatteryTray.VgnDeviceHelper", $true)
    $mVgnBat = $tVgn.GetMethod("TryExtractVgnBatteryFromReply", $bfPubStatic)
    Assert-True ($null -ne $mVgnBat) "TryExtractVgnBatteryFromReply exists"
    [byte[]]$rxLegacyVgn = @(0x08, 0x00, 0, 0, 0, 0, 82, 1, 0)
    $vArgs9 = [object[]]@($rxLegacyVgn.PSObject.BaseObject, 0, $false, 0)
    Assert-True ([bool]$mVgnBat.Invoke($null, $vArgs9) -and [int]$vArgs9[1] -eq 82 -and [bool]$vArgs9[2] -and [int]$vArgs9[3] -eq 0) "9B legacy CompX battery reply accepted"
    [byte[]]$rxFullVgn = @(0x08, 0x04, 0, 0, 0, 0, 64, 0, 0x10, 0x61, 0)
    $vArgs11 = [object[]]@($rxFullVgn.PSObject.BaseObject, 0, $false, 0)
    Assert-True ([bool]$mVgnBat.Invoke($null, $vArgs11) -and [int]$vArgs11[1] -eq 64 -and -not [bool]$vArgs11[2] -and [int]$vArgs11[3] -eq 4193) "10B+ CompX battery + voltage parsed"

    # DPI table 6x10B stream parsing (preserving per-stage [B,G,R,A] colors)
    [byte[]]$stream = New-Object byte[] 60
    $stream[0] = 1; $stream[1] = 0x08
    # Stage 0: 400x400 (0x0190), color 00 00 FF 00
    $stream[2] = 0x90; $stream[3] = 0x01; $stream[4] = 0x90; $stream[5] = 0x01; $stream[8] = 0xFF
    # Stage 1: 800x800 (0x0320), color EA 00 FF 00
    $stream[10] = 0x20; $stream[11] = 0x03; $stream[12] = 0x20; $stream[13] = 0x03; $stream[14] = 0xEA; $stream[16] = 0xFF
    # Stage 2 (crosses 20B chunk boundary): 2750x2750 (0x0ABE), color FF 11 00 00
    $stream[18] = 0xBE; $stream[19] = 0x0A; $stream[20] = 0xBE; $stream[21] = 0x0A; $stream[22] = 0xFF; $stream[23] = 0x11
    # Stage 3: 3000x3000 (0x0BB8), color FF CC 00 00
    $stream[26] = 0xB8; $stream[27] = 0x0B; $stream[28] = 0xB8; $stream[29] = 0x0B; $stream[30] = 0xFF; $stream[31] = 0xCC
    $tArgs = [object[]]@($stream.PSObject.BaseObject, 60, [int[]]@(), [int[]]@(), [byte[]]@(), -1)
    $tOk = [bool]$tVxe.GetMethod("ParseDpiTableStream", $bfPubStatic).Invoke($null, $tArgs)
    Assert-True ($tOk -and ([int[]]$tArgs[2]).Length -eq 4 -and ([int[]]$tArgs[2])[0] -eq 400 -and ([int[]]$tArgs[2])[2] -eq 2750 -and ([int[]]$tArgs[3])[2] -eq 2750 -and ([int[]]$tArgs[2])[3] -eq 3000) "ParseDpiTableStream 4 stages"
    Assert-True (([byte[]]$tArgs[4]).Length -eq 16 -and ([byte[]]$tArgs[4])[4] -eq 0xEA -and ([byte[]]$tArgs[4])[8] -eq 0xFF -and [int]$tArgs[5] -eq 1) "ParseDpiTableStream preserves per-stage BGRA & hint"
    $badStream = [byte[]]$stream.Clone(); $badStream[1] = 0x00
    Assert-True (-not [bool]$tVxe.GetMethod("ParseDpiTableStream", $bfPubStatic).Invoke($null, [object[]]@($badStream.PSObject.BaseObject, 60, [int[]]@(), [int[]]@(), [byte[]]@(), -1))) "Reject invalid DPI table header"

    # DPI write frame preserves color & refuses missing color
    [byte[]]$col = @(0xDD, 0x00, 0xFF, 0x00)
    $dpiTx = [byte[]]$tVxe.GetMethod("BuildDpiWriteFrame64", $bfPubStatic).Invoke($null, @([int]1, [int]1450, [int]1450, $col.PSObject.BaseObject))
    $dpiSum = 0; for ($i = 2; $i -lt 64; $i++) { $dpiSum = ($dpiSum + $dpiTx[$i]) -band 0xFF }
    Assert-True ($null -ne $dpiTx -and $dpiTx.Length -eq 64 -and $dpiTx[1] -eq $dpiSum -and $dpiTx[6] -eq 0x02 -and $dpiTx[8] -eq 1 -and $dpiTx[9] -eq 0xAA -and $dpiTx[10] -eq 0x05 -and $dpiTx[13] -eq 0xDD -and $dpiTx[15] -eq 0xFF) "BuildDpiWriteFrame64 1450 DPI + color"
    Assert-True ($null -eq $tVxe.GetMethod("BuildDpiWriteFrame64", $bfPubStatic).Invoke($null, @([int]0, [int]800, [int]800, $null))) "BuildDpiWriteFrame64 refuses null color"

    # Polling rate descending codes (125->3, 250->2, 500->1, 1000->0)
    $r250 = [byte[]]$tVxe.GetMethod("BuildPollingRateFrame64", $bfPubStatic).Invoke($null, @([int]250))
    $r1000 = [byte[]]$tVxe.GetMethod("BuildPollingRateFrame64", $bfPubStatic).Invoke($null, @([int]1000))
    Assert-True ($r250[6] -eq 0x01 -and $r250[8] -eq 2 -and $r1000[8] -eq 0) "Polling rate descending codes"
    Assert-True ($null -eq $tVxe.GetMethod("BuildPollingRateFrame64", $bfPubStatic).Invoke($null, @([int]8000))) "Unsupported 8000Hz rejected"

    # Competitive firmware read/write + 0x73 physical switch event
    $smTx = [byte[]]$tVxe.GetMethod("BuildSensorModeWriteFrame64", $bfPubStatic).Invoke($null, @($true))
    Assert-True ($smTx[6] -eq 0x1F -and $smTx[8] -eq 0x04) "BuildSensorModeWriteFrame64 competitive"
    [byte[]]$rxModeOn = @(0x08, 0x72, 0x00, 0x11, 0x00, 0x17, 0x0D, 0x4A, 0x00, 0x01, 0x04)
    $mArgs = [object[]]@($rxModeOn.PSObject.BaseObject, 11, $false)
    Assert-True ([bool]$tVxe.GetMethod("ParseSensorModeResponse", $bfPubStatic).Invoke($null, $mArgs) -and [bool]$mArgs[2]) "ParseSensorModeResponse competitive ON"
    $rxModeOff = [byte[]]$rxModeOn.Clone(); $rxModeOff[10] = 0x00
    $mArgs2 = [object[]]@($rxModeOff.PSObject.BaseObject, 11, $true)
    Assert-True ([bool]$tVxe.GetMethod("ParseSensorModeResponse", $bfPubStatic).Invoke($null, $mArgs2) -and -not [bool]$mArgs2[2]) "ParseSensorModeResponse basic OFF"

    [byte[]]$evt73 = @(0x08, 0x9B, 0x73, 0x01, 0x05, 0x00, 0x90, 0x01, 0x90, 0x01)
    $eArgs = [object[]]@($evt73.PSObject.BaseObject, 10, 0, 0)
    Assert-True ([bool]$tVxe.GetMethod("ParseDpiSwitchEventFrame", $bfPubStatic).Invoke($null, $eArgs) -and [int]$eArgs[2] -eq 400 -and [int]$eArgs[3] -eq 400) "ParseDpiSwitchEventFrame 400x400"

    # Driver capability contract
    $drv = [Activator]::CreateInstance($asm.GetType("RazerBatteryTray.VxeNearLinkDriver", $true))
    $dev = [Activator]::CreateInstance($asm.GetType("RazerBatteryTray.MouseBatteryInfo"))
    $dev.DeviceId = "VXE:373B:1216"; $dev.Brand = "VXE"
    $dev.Category = [RazerBatteryTray.DeviceCategory]::Mouse
    $dev.IsConnected = $true; $dev.IsPrimary = $true; $dev.BatteryPercent = 82
    $dev.DpiStages = [int[]]@(400, 800, 2750, 3000); $dev.DpiStage = 2; $dev.Dpi = 800
    Assert-True ($drv.CanHandle($dev)) "VxeNearLinkDriver CanHandle"
    $drv.PopulateCapabilities($dev)
    Assert-True ($dev.Capabilities.SupportsBattery -and $dev.Capabilities.SupportsDpi -and $dev.Capabilities.SupportsDpiStages -and $dev.Capabilities.SupportsPollingRate -and $dev.Capabilities.SupportsHardwareDpiMonitor) "VXE NearLink full capability contract"
    Assert-True ($dev.Capabilities.CustomActions.Count -ge 1 -and $dev.Capabilities.CustomActions[0].ActionId -eq "competitive_mode" -and $dev.Capabilities.CustomActions[0].SyncWithGameOptimization) "competitive_mode custom action registered with SyncWithGameOptimization"
    $dev.Capabilities.CustomActions[0].BoolValue = $true
    $dev.Capabilities.CustomActions[0].IntValue = 1

    # Sandbox MainForm tray CustomActions generic checked rendering (Mouse keeps DPI+Rate + dynamic Toggle)
    $form = New-SandboxMainForm $false
    try {
        $tMain = $form.GetType()
        $activeList = $tMain.GetField("activeDevices", $bfNonPubInst).GetValue($form)
        $activeList.Clear(); $activeList.Add($dev)
        # Keep competitiveModeState synchronized so PopulateCapabilities preserves true in tray refresh
        $tVxe.GetField("competitiveModeState", $bfNonPubStatic).SetValue($null, $true)
        $tMain.GetMethod("UpdateTrayDeviceMenuItems", $bfNonPubInst).Invoke($form, @()) | Out-Null

        $dpiMenu = $tMain.GetField("dpiMenu", $bfNonPubInst).GetValue($form)
        $rateMenu = $tMain.GetField("rateMenu", $bfNonPubInst).GetValue($form)
        $nuphySleep = $tMain.GetField("nuphySleepMenuItem", $bfNonPubInst).GetValue($form)
        $dynCustom = $tMain.GetField("dynamicCustomActionTrayItems", $bfNonPubInst).GetValue($form)
        Assert-True ($dpiMenu.Available -and $rateMenu.Available) "Mouse with CustomActions retains DPI and PollingRate tray menus"
        Assert-True (-not $nuphySleep.Available) "VXE competitive_mode does not hijack nuphySleepMenuItem"
        Assert-Equal 1 $dynCustom.Count "1 dynamic CustomAction tray item rendered"
        Assert-True ($dynCustom[0].Available -and $dynCustom[0].Checked -and (-not $dynCustom[0].CheckOnClick) -and $dynCustom[0].Tag -eq "competitive_mode") "Dynamic tray CustomAction checked & CheckOnClick=false"

        # In-place tray item refresh while context menu stays open (no menu reopen required)
        $mInPlace = $tMain.GetMethod("RefreshDynamicCustomActionTrayItemInPlace", $bfNonPubInst)
        Assert-True ($null -ne $mInPlace) "RefreshDynamicCustomActionTrayItemInPlace exists"
        $compItem = $dynCustom[0]
        $compAct = $dev.Capabilities.CustomActions[0]
        [void]$mInPlace.Invoke($form, @($compItem, $dev, "competitive_mode", $compAct, $true, $false))
        Assert-True ((-not $compItem.Checked) -and ($compItem.Text -like "*基础模式*") -and (-not $compAct.BoolValue)) "In-place refresh toggles competitive_mode OFF"
        [void]$mInPlace.Invoke($form, @($compItem, $dev, "competitive_mode", $compAct, $false, $true))
        Assert-True ((-not $compItem.Checked) -and ($compItem.Text -like "*基础模式*")) "Failed execute does not flip Checked state"
        [void]$mInPlace.Invoke($form, @($compItem, $dev, "competitive_mode", $compAct, $true, $true))
        Assert-True ($compItem.Checked -and ($compItem.Text -like "*ATK绝鲨竞技固件*") -and $compAct.BoolValue) "In-place refresh toggles competitive_mode ON"

        # Game-optimization firmware sync gating (VXE R1 exposes it; ordinary mice do not)
        $bfAnyInst = [System.Reflection.BindingFlags]"Public,NonPublic,Instance"
        $mSetOpt = $tMain.GetMethod("SetAutoGameOptEnabled", $bfAnyInst)
        $mSetCustomOpt = $tMain.GetMethod("SetAutoGameCustomActionEnabled", $bfAnyInst)
        $mUpdPerf = $tMain.GetMethod("UpdatePerformanceSection", $bfNonPubInst)
        $mExpH = $tMain.GetMethod("GetExpandedGameOptCardBaseHeight", $bfNonPubInst)
        $mIsAnyOpt = $tMain.GetMethod("IsAnyAutoGameOptEnabledForDevice", $bfNonPubInst)
        $chkCustom = $tMain.GetField("chkAutoGameCustomAction", $bfNonPubInst).GetValue($form)
        $menuCustom = $tMain.GetField("autoGameCustomActionToggleItem", $bfNonPubInst).GetValue($form)

        $form.Visible = $true
        $activeList.Clear(); $activeList.Add($dev)
        $tMain.GetField("selectedDevice", $bfNonPubInst).SetValue($form, $dev)
        $mSetOpt.Invoke($form, @($true)) | Out-Null
        $mSetCustomOpt.Invoke($form, @($true)) | Out-Null
        $mUpdPerf.Invoke($form, @($dev, [System.Drawing.Color]::Lime)) | Out-Null
        $tMain.GetMethod("UpdateTrayDeviceMenuItems", $bfNonPubInst).Invoke($form, @()) | Out-Null
        Assert-True ($chkCustom.Visible -and ($chkCustom.Text -like "*ATK绝鲨竞技固件*") -and $chkCustom.Checked) "VXE mouse displays GameOpt firmware sync checkbox"
        Assert-True ($menuCustom.Available -and $menuCustom.Checked -and ($menuCustom.Text -like "*ATK绝鲨竞技固件*")) "Tray autoGameRateMenu displays VXE firmware sync toggle"
        Assert-Equal 283 ([int]$mExpH.Invoke($form, @($dev))) "VXE expanded GameOpt card base height is 283"

        # Non-VXE mouse (e.g. Razer) must NOT see GameOpt firmware sync checkbox or menu item
        $razer = [Activator]::CreateInstance($asm.GetType("RazerBatteryTray.MouseBatteryInfo"))
        $razer.DeviceId = "Razer:1532:00C0"; $razer.Brand = "Razer"; $razer.DeviceName = "Razer Viper V3 Pro"
        $razer.Category = [RazerBatteryTray.DeviceCategory]::Mouse; $razer.IsConnected = $true; $razer.IsPrimary = $true
        $razer.Capabilities.SupportsPollingRate = $true; $razer.SupportedPollingRates = [int[]]@(1000, 2000, 4000, 8000)
        $activeList.Clear(); $activeList.Add($razer)
        $tMain.GetField("selectedDevice", $bfNonPubInst).SetValue($form, $razer)
        $mUpdPerf.Invoke($form, @($razer, [System.Drawing.Color]::Lime)) | Out-Null
        $tMain.GetMethod("UpdateTrayDeviceMenuItems", $bfNonPubInst).Invoke($form, @()) | Out-Null
        Assert-True (-not $chkCustom.Visible) "Ordinary Razer mouse hides GameOpt firmware sync checkbox"
        Assert-True (-not $menuCustom.Available) "Ordinary Razer mouse hides GameOpt firmware sync tray toggle"
        Assert-Equal 255 ([int]$mExpH.Invoke($form, @($razer))) "Ordinary mouse keeps 255 GameOpt card base height"

        # CustomAction-only expansion works for VXE, not for Razer; SetAutoGameOptEnabled(false) clears it
        $mSetOpt.Invoke($form, @($false)) | Out-Null
        $mSetCustomOpt.Invoke($form, @($true)) | Out-Null
        Assert-True ([bool]$mIsAnyOpt.Invoke($form, @($dev))) "VXE expands when only CustomAction sync is enabled"
        Assert-True (-not [bool]$mIsAnyOpt.Invoke($form, @($razer))) "Razer does not expand on CustomAction sync alone"
        $mSetOpt.Invoke($form, @($false)) | Out-Null
        Assert-True (-not $chkCustom.Checked -and -not $menuCustom.Checked) "SetAutoGameOptEnabled(false) turns off firmware sync"

        # Switching to NuPhy keyboard cleans dynamicCustomActionTrayItems and shows NuPhy auto_sleep with Checked state
        $tVxe.GetField("competitiveModeState", $bfNonPubStatic).SetValue($null, $false)
        $kb = [Activator]::CreateInstance($asm.GetType("RazerBatteryTray.MouseBatteryInfo"))
        $kb.DeviceId = "NuPhy:19F5:3245"; $kb.Brand = "NuPhy"; $kb.DriverId = "BuiltIn:NuPhy"
        $kb.Category = [RazerBatteryTray.DeviceCategory]::Keyboard
        $kb.IsConnected = $true; $kb.IsPrimary = $true; $kb.BatteryPercent = 90
        $activeList.Clear(); $activeList.Add($kb)
        $tMain.GetMethod("UpdateTrayDeviceMenuItems", $bfNonPubInst).Invoke($form, @()) | Out-Null
        Assert-Equal 0 $dynCustom.Count "Dynamic CustomActions cleared on keyboard"
        Assert-True ((-not $dpiMenu.Available) -and (-not $rateMenu.Available) -and $nuphySleep.Available) "NuPhy keyboard hides mouse menus and shows auto_sleep"
    } finally {
        $form.Dispose()
    }
}
