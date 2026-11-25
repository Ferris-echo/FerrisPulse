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

Invoke-TestCase "Vxe.NearLinkR1ProtocolAndReadOnlyContract" {
    $tVxe = $asm.GetType("RazerBatteryTray.VxeNearLinkDeviceHelper", $true)
    Assert-True ([bool]$tVxe.GetMethod("IsSupportedVidPid", $bfPubStatic).Invoke($null, @([int]0x373B, [int]0x1216))) "VXE R1 NearLink accepted"
    Assert-True (-not [bool]$tVxe.GetMethod("IsSupportedVidPid", $bfPubStatic).Invoke($null, @([int]0x373B, [int]0x1085))) "VXE R1 SE rejected"

    $epOk = New-Object RazerBatteryTray.HidDeviceDescriptor
    $epOk.VendorId = 0x373B; $epOk.ProductId = 0x1216; $epOk.UsagePage = 0xFF05; $epOk.Usage = 1
    $epOk.InputReportLength = 64; $epOk.OutputReportLength = 64; $epOk.DevicePath = "\\?\hid#vid_373b&pid_1216&mi_02&col04#test"
    Assert-True ([bool]$tVxe.GetMethod("IsNearLinkEndpoint", $bfPubStatic).Invoke($null, @($epOk.PSObject.BaseObject))) "NearLink 64B endpoint"

    $tx7 = [byte[]]$tVxe.GetMethod("BuildReport64", $bfPubStatic).Invoke($null, @([int]7, [int]1, [int]0x36))
    Assert-True ($tx7.Length -eq 64 -and $tx7[0] -eq 8 -and $tx7[2] -eq 0x72 -and $tx7[1] -eq ((0x72 + 2 + 0x36 + 1 + 7 + 1) -band 0xFF)) "NearLink BuildReport64"

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

    $drv = [Activator]::CreateInstance($asm.GetType("RazerBatteryTray.VxeNearLinkDriver", $true))
    $dev = [Activator]::CreateInstance($asm.GetType("RazerBatteryTray.MouseBatteryInfo"))
    $dev.DeviceId = "VXE:373B:1216"
    Assert-True ($drv.CanHandle($dev)) "VxeNearLinkDriver CanHandle"
    $drv.PopulateCapabilities($dev)
    Assert-True ($dev.Capabilities.SupportsBattery -and !$dev.Capabilities.SupportsDpi -and !$dev.Capabilities.SupportsPollingRate) "VXE NearLink read-only capability contract"
}
