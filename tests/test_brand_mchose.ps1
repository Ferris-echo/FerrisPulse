# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# Consolidated Mchose (+ Flydigi) Brand Test Suite
# Replaces 15 legacy scripts (test_flydigi_mchose_support + all 14 test_mchose_* scripts).
# Zero live hardware I/O (replaces live 0x4D probing with offline contract/sandbox checks + named pipe boundary test).

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

Invoke-TestCase "Mchose.FlydigiAndKeyboardG87G75K99V3" {
    # Flydigi APEX 6
    $flyType = $asm.GetType("RazerBatteryTray.FlydigiDeviceHelper")
    Assert-True ([bool]$flyType.GetMethod("IsSupportedVidPid", $bfPubStatic).Invoke($null, @(0x37D7, 0x2502))) "Flydigi 2502"
    Assert-True ([bool]$flyType.GetMethod("IsSupportedVidPid", $bfPubStatic).Invoke($null, @(0x37D7, 0x2401))) "Flydigi 2401"
    Assert-True (-not [bool]$flyType.GetMethod("IsSupportedVidPid", $bfPubStatic).Invoke($null, @(0x3837, 0x3033))) "Flydigi rejects Mchose"

    $mFlyParse = $flyType.GetMethod("ParseInfo", $bfPubStatic)
    function Invoke-Fly([int]$rawByte) {
        [byte[]]$buf = New-Object byte[] 33
        $buf[0] = 0x5A; $buf[1] = 0xA5; $buf[2] = 0x01; $buf[3] = 0x01; $buf[5] = 150; $buf[6] = 1; $buf[11] = [byte]$rawByte
        $a = [object[]]@($buf.PSObject.BaseObject, 33, 0, $false, 0, 0)
        $ok = [bool]$mFlyParse.Invoke($null, $a)
        return [pscustomobject]@{ Ok = $ok; Batt = [int]$a[2]; Chg = [bool]$a[3]; Type = [int]$a[4] }
    }
    $f1 = Invoke-Fly 0x1A
    Assert-True ($f1.Ok -and $f1.Batt -eq 100 -and $f1.Chg -and $f1.Type -eq 150) "Flydigi 100% charging"
    $f2 = Invoke-Fly 0x03
    Assert-True ($f2.Ok -and $f2.Batt -eq 60 -and !$f2.Chg) "Flydigi 60% discharging"

    # Mchose G87 V2 & K99 V3 wired/BLE + G75
    $mchType = $asm.GetType("RazerBatteryTray.MchoseDeviceHelper")
    Assert-True ([bool]$mchType.GetMethod("IsSupportedVidPid", $bfPubStatic).Invoke($null, @(0x3837, 0x3033))) "Mchose 3837:3033"
    Assert-Equal 0x07D7 ([int]$mchType.GetField("BleVendorIdK99V3", $bfAnyStatic).GetValue($null)) "K99V3 BLE VID 0x07D7"
    Assert-Equal 0xEFFF ([int]$mchType.GetField("BlePidK99V3", $bfAnyStatic).GetValue($null)) "K99V3 BLE PID 0xEFFF"
    Assert-Equal 0x2020 ([int]$mchType.GetField("PidK99V3Wired", $bfAnyStatic).GetValue($null)) "K99V3 Wired PID 0x2020"

    $mBleVp = $mchType.GetMethod("TryParseBleVidPid", $bfAnyStatic)
    $vp1 = [object[]]@("BTHLEDEVICE\{00001812-0000-1000-8000-00805F9B34FB}_DEV_VID&0107D7_PID&EFFF_REV&0001_DC80652107DF\8&1&0&0000", 0, 0)
    Assert-True ([bool]$mBleVp.Invoke($null, $vp1) -and [int]$vp1[1] -eq 0x07D7 -and [int]$vp1[2] -eq 0xEFFF) "BLE VID 0107D7 -> 0x07D7"
    $mBleMac = $mchType.GetMethod("TryParseBleMac", $bfAnyStatic)
    Assert-Equal "DC80652107DF" ([string]$mBleMac.Invoke($null, @("BTHLEDEVICE\{00001812-0000-1000-8000-00805F9B34FB}_DEV_VID&0107D7_PID&EFFF_REV&0001_DC80652107DF\8&1&0&0000"))) "BLE MAC extraction"
    Assert-True ($null -eq $mBleMac.Invoke($null, @("BTHLE\DEV_DC80652107DF\8&71E4D4F&0&DC80652107DF"))) "Parent BTHLE node rejected"

    $mPerf = $mchType.GetMethod("ParsePerformanceResponse", $bfPubStatic)
    foreach ($tc in @(
        @{ Raw = 0xC3; Batt = 67; Chg = $true },
        @{ Raw = 0x44; Batt = 68; Chg = $false },
        @{ Raw = 0xCA; Batt = 74; Chg = $true }
    )) {
        [byte[]]$pBuf = New-Object byte[] 64; $pBuf[0] = 0xAA; $pBuf[1] = 0x05; $pBuf[40] = [byte]$tc.Raw
        $pArgs = [object[]]@($pBuf.PSObject.BaseObject, 64, 0, $false)
        Assert-True ([bool]$mPerf.Invoke($null, $pArgs) -and [int]$pArgs[2] -eq $tc.Batt -and [bool]$pArgs[3] -eq $tc.Chg) ("K99V3 ParsePerformanceResponse 0x{0:X2}" -f $tc.Raw)
    }

    # G75 20-byte query & checksum validation + VGN F503 isolation + HIDI2C filter
    $g75Tx = [byte[]]$mchType.GetMethod("BuildG75BatteryFrame", $bfPubStatic).Invoke($null, @())
    Assert-True ($g75Tx.Length -eq 20 -and $g75Tx[0] -eq 0x13 -and $g75Tx[1] -eq 0x4A -and $g75Tx[19] -eq 0x5D) "G75 BuildG75BatteryFrame"
    $mG75Parse = $mchType.GetMethod("ParseG75BatteryResponse", $bfPubStatic)
    [byte[]]$g75Batt = 0x13,0x4A,0x01,0x00,0x02,0x4C,0x01,0,0,0,0,0,0,0,0,0,0,0,0,0xAD
    [byte[]]$g75Chg = 0x13,0x4A,0x01,0x00,0x02,0x5D,0x10,0,0,0,0,0,0,0,0,0,0,0,0,0xCD
    $g75Args1 = [object[]]@($g75Batt.PSObject.BaseObject, 20, 0, $false)
    Assert-True ([bool]$mG75Parse.Invoke($null, $g75Args1) -and [int]$g75Args1[2] -eq 76 -and ![bool]$g75Args1[3]) "G75 76% discharging"
    $g75Args2 = [object[]]@($g75Chg.PSObject.BaseObject, 20, 0, $false)
    Assert-True ([bool]$mG75Parse.Invoke($null, $g75Args2) -and [int]$g75Args2[2] -eq 93 -and [bool]$g75Args2[3]) "G75 93% charging"

    $tVgn = $asm.GetType("RazerBatteryTray.VgnDeviceHelper")
    Assert-True ([bool]$tVgn.GetMethod("IsVgnProtocolEndpoint", $bfPubStatic).Invoke($null, @(0x3554, 0xF503, 0xFF02, 0x0002, 17, 17))) "VGN F503 17B endpoint"
    Assert-True (-not [bool]$tVgn.GetMethod("IsVgnProtocolEndpoint", $bfPubStatic).Invoke($null, @(0x3554, 0xFA09, 0xFF02, 0x0002, 20, 20))) "G75 FA09 excluded from VGN"
    $tGen = $asm.GetType("RazerBatteryTray.GenericHidHelper")
    Assert-True ([bool]$tGen.GetMethod("IsInternalHidDevice", $bfPubStatic).Invoke($null, @("HIDI2C Device", "hid#vid_093a&pid_0005"))) "HIDI2C filtered"
}

Invoke-TestCase "Mchose.G7GMouseAndV9TurboPlusHeadset" {
    # G7 GMouse
    $tG7 = $asm.GetType("RazerBatteryTray.MchoseGMouseHelper", $true)
    Assert-True ([bool]$tG7.GetMethod("IsSupportedVidPid").Invoke($null, @(0xA8A5, 0x2255))) "G7 2255 accepted"
    Assert-True (-not [bool]$tG7.GetMethod("IsSupportedVidPid").Invoke($null, @(0xA8A5, 0x2254))) "Sibling 2254 rejected"
    Assert-True ([bool]$tG7.GetMethod("IsMchoseGMouse").Invoke($null, @("HID\VID_A8A5&PID_2255&MI_02"))) "IsMchoseGMouse"

    $battTx = [byte[]]$tG7.GetMethod("BuildBatteryFrame").Invoke($null, @())
    Assert-True ($battTx.Length -eq 65 -and $battTx[1] -eq 0x55 -and $battTx[2] -eq 0x30) "G7 65-byte battery frame"

    [byte[]]$live65 = New-Object byte[] 65
    $live65[1] = 0xAA; $live65[2] = 0x30; $live65[3] = 0xA5; $live65[4] = 0x0B; $live65[5] = 0x0A
    $live65[6] = 0x01; $live65[7] = 0x01; $live65[8] = 0x01; $live65[9] = 42; $live65[10] = 0x01
    $mG7Parse = $tG7.GetMethod("ParseBatteryResponse")
    $gArgs = [object[]]@($live65.PSObject.BaseObject, 65, 0, $false)
    Assert-True ([bool]$mG7Parse.Invoke($null, $gArgs) -and [int]$gArgs[2] -eq 42 -and [bool]$gArgs[3]) "G7 42% charging"

    # V9 Turbo+ Headset + FDAS JSON contract
    $adapterPath = Join-Path $root "examples\adapters\mchose_v9_turbo_plus.fp-adapter.json"
    Assert-True (Test-Path $adapterPath) "mchose_v9_turbo_plus.fp-adapter.json exists"
    $adapter = (Get-Content $adapterPath -Raw -Encoding UTF8) | ConvertFrom-Json
    Assert-True ($adapter.fdasVersion -eq "2.0" -and $adapter.metadata.category -eq "Headset" -and $adapter.hardware.vendorId -eq "0x3837" -and $adapter.hardware.productId -eq "0x600A") "V9 Turbo+ FDAS adapter metadata"

    $q = [RazerBatteryTray.MchoseHeadsetHelper]::BuildBatteryQuery()
    Assert-True ($q.Length -eq 64 -and $q[0] -eq 0x55 -and $q[1] -eq 0x65 -and $q[2] -eq 0x01) "V9 Turbo+ BuildBatteryQuery"
    $pct = -1
    $healthy = Parse-HexBytes $adapter.capabilities.battery.sampleRxHex
    Assert-True ([RazerBatteryTray.MchoseHeadsetHelper]::ParseBatteryResponse($healthy, 64, [ref]$pct) -and $pct -eq 100) "V9 Turbo+ healthy 100%"
    $degraded = Parse-HexBytes $adapter.capabilities.battery.invalidSampleRxHex
    Assert-True (-not [RazerBatteryTray.MchoseHeadsetHelper]::ParseBatteryResponse($degraded, 64, [ref]$pct) -and $pct -eq -1) "V9 Turbo+ degraded rejected"
}

Invoke-TestCase "Mchose.Mouse3837And4DProtocolUsbBleWired" {
    # Legacy MchoseMouseHelper (XOR 0xFF 0x11)
    $tMouse = $asm.GetType("RazerBatteryTray.MchoseMouseHelper")
    [byte[]]$resp = New-Object byte[] 64
    $resp[0] = 0x11; $resp[1] = 0xF9
    $resp[2] = [byte](0x37 -bxor 0xFF); $resp[3] = [byte](0x38 -bxor 0xFF)
    $resp[4] = [byte](0x0C -bxor 0xFF); $resp[5] = [byte](0x10 -bxor 0xFF)
    $resp[10] = [byte](0x09 -bxor 0xFF); $resp[11] = [byte](76 -bxor 0xFF); $resp[12] = [byte](1 -bxor 0xFF)
    $idArgs = [object[]]@($resp.PSObject.BaseObject, 0, 0, 0, 0, 0)
    Assert-True ([bool]$tMouse.GetMethod("TryParseIdentityResponse", $bfPubStatic).Invoke($null, $idArgs)) "Mchose 0xF9 identity"
    Assert-True ([int]$idArgs[1] -eq 0x3837 -and [int]$idArgs[2] -eq 0x100C -and [int]$idArgs[4] -eq 76 -and [int]$idArgs[5] -eq 1) "Mchose 76% charging"

    # MchoseMouse4DHelper (0x4D frames + BLE 0x4026 + Wired 0x4028 + sandbox writes)
    $t4d = $asm.GetType("RazerBatteryTray.MchoseMouse4DHelper")
    $mIsPid = $t4d.GetMethod("IsSupportedPid", $bfPubStatic)
    $mIsBle = $t4d.GetMethod("IsSupportedBlePid", $bfPubStatic)
    $mTr = $t4d.GetMethod("GetTransportNameByPid", $bfPubStatic)

    foreach ($p in @(0x1001, 0x1014, 0x1016, 0x1018, 0x101C, 0x101D, 0x4028)) {
        Assert-True ([bool]$mIsPid.Invoke($null, @([int]$p))) ("4D USB/Dongle PID 0x{0:X4}" -f $p)
    }
    Assert-True (-not [bool]$mIsPid.Invoke($null, @(0x4026))) "4D USB excludes BLE 0x4026"
    Assert-True ([bool]$mIsBle.Invoke($null, @(0x4026))) "4D BLE accepts 0x4026"
    Assert-True (-not [bool]$mIsBle.Invoke($null, @(0x1018))) "4D BLE excludes 0x1018"
    Assert-Equal "USB 有线" ([string]$mTr.Invoke($null, @(1, 0x4028))) "0x4028 USB wired transport"
    Assert-Equal "2.4G 无线" ([string]$mTr.Invoke($null, @(0, 0x1018))) "0x1018 2.4G transport"

    $mBuild = $t4d.GetMethod("BuildFrame", $bfPubStatic)
    [byte[]]$f2 = $mBuild.Invoke($null, @([uint16]0x0103, ([byte[]]@(1, 2, 3, 4)).PSObject.BaseObject))
    $chk = 0; for ($i = 2; $i -le 11; $i++) { $chk = $chk -bxor $f2[$i] }
    Assert-True ($f2[3] -eq 4 -and $f2[12] -eq $chk) "0x4D BuildFrame XOR checksum"

    [byte[]]$rxDev = New-Object byte[] 64
    $rxDev[0] = 0x4D; $rxDev[1] = 0x01; $rxDev[8] = 0x37; $rxDev[9] = 0x38; $rxDev[10] = 0x18; $rxDev[11] = 0x10
    $rxDev[12] = 0x04; $rxDev[18] = 0x02; $rxDev[19] = 0x00; $rxDev[20] = 77; $rxDev[21] = 0x01
    $devArgs = [object[]]@($rxDev.PSObject.BaseObject, $null)
    Assert-True ([bool]$t4d.GetMethod("TryParseDeviceInfo", $bfPubStatic).Invoke($null, $devArgs)) "0x0900 TryParseDeviceInfo"
    Assert-True ($devArgs[1].Vid -eq 0x3837 -and $devArgs[1].Pid -eq 0x1018 -and $devArgs[1].Battery -eq 77) "0x0900 battery 77%"

    # Rate roundtrips (125..8000Hz)
    $mIdx = $t4d.GetMethod("GetRateIndexForHz", $bfPubStatic)
    $mEnc = $t4d.GetMethod("EncodeRateIndex", $bfPubStatic)
    $mDec = $t4d.GetMethod("DecodeRateIndex", $bfPubStatic)
    $mHz = $t4d.GetMethod("GetRateHz", $bfPubStatic)
    foreach ($hz in @(125, 500, 1000, 2000, 4000, 8000)) {
        $idx = $mIdx.Invoke($null, @([int]$hz))
        $raw = $mEnc.Invoke($null, @($idx))
        Assert-Equal $idx ($mDec.Invoke($null, @($raw))) "Rate idx $hz"
        Assert-Equal $hz ($mHz.Invoke($null, @($raw))) "Rate Hz $hz"
    }

    # 6-stage 42000 DPI parse
    [byte[]]$dp = New-Object byte[] 64; $dp[0] = 0x4D; $dp[10] = 0x06; $dp[11] = 0x02; $dp[12] = 0x01
    $dpiVals = @(400, 800, 2200, 6400, 16000, 42000)
    for ($i = 0; $i -lt 6; $i++) { $dp[13 + $i * 2] = [byte]($dpiVals[$i] -band 0xFF); $dp[14 + $i * 2] = [byte](($dpiVals[$i] -shr 8) -band 0xFF) }
    $dpArgs = [object[]]@($dp.PSObject.BaseObject, $null)
    Assert-True ([bool]$t4d.GetMethod("TryParseDpi", $bfPubStatic).Invoke($null, $dpArgs)) "TryParseDpi 6 stages"
    Assert-Equal 42000 $dpArgs[1].StagesX[5] "42000 DPI stage 6"

    # Offline hardware write sandbox contract (replaces live 0x4D hardware test)
    Assert-True (-not [RazerBatteryTray.MchoseMouse4DHelper]::SetDpi(1600, 1600, 1)) "SetDpi blocked by hardware sandbox"
    Assert-True (-not [RazerBatteryTray.MchoseMouse4DHelper]::SetPollingRate(1000)) "SetPollingRate blocked by hardware sandbox"
}

Invoke-TestCase "Mchose.Z75sIdentityTransportPipeCacheAndUI" {
    $mType = $asm.GetType("RazerBatteryTray.MchoseDeviceHelper", $true)
    Assert-True ([bool]$mType.GetMethod("IsMchoseKeyboard").Invoke($null, @("HID\VID_258A&PID_01BB&MI_01"))) "Z75S receiver matched"
    Assert-True (-not [bool]$mType.GetMethod("IsMchoseKeyboard").Invoke($null, @("HID\VID_258A&PID_010C"))) "Other SinoWealth excluded"

    [byte[]]$idFrame = @(0x13, 0x05, 1, 0, 6, 0x0A, 0, 0, 0, 0, 9, 0, 0, 0, 0, 0, 0, 0, 0, 0x32)
    Assert-True ([bool]$mType.GetMethod("ParseZ75sIdentityResponse").Invoke($null, @($idFrame.PSObject.BaseObject, 20))) "Z75S identity frame"

    # Real ExchangeZ75sReport over in-process anonymous named pipe
    $exchange = $mType.GetMethod("ExchangeZ75sReport", $bfNonPubStatic)
    $tx = [byte[]]$mType.GetMethod("BuildZ75sBatteryFrame").Invoke($null, @())
    [byte[]]$validRx = @(0x13, 0x4A, 1, 0, 2, 0x32, 0x10, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0xA2)
    $pipeName = "FerrisPulse-Z75S-" + [Guid]::NewGuid().ToString("N")
    $server = New-Object IO.Pipes.NamedPipeServerStream($pipeName, [IO.Pipes.PipeDirection]::InOut, 1, [IO.Pipes.PipeTransmissionMode]::Byte, [IO.Pipes.PipeOptions]::Asynchronous)
    $client = New-Object IO.Pipes.NamedPipeClientStream(".", $pipeName, [IO.Pipes.PipeDirection]::InOut, [IO.Pipes.PipeOptions]::Asynchronous)
    try {
        $pending = $server.BeginWaitForConnection($null, $null)
        $client.Connect(1000)
        $server.EndWaitForConnection($pending)
        $cmdBuf = New-Object byte[] 20
        $pRead = $client.BeginRead($cmdBuf, 0, 20, $null, $null)
        $pWrite = $client.BeginWrite($validRx, 0, 20, $null, $null)
        $exArgs = [object[]]@($server.SafePipeHandle.DangerousGetHandle(), $tx.PSObject.BaseObject, [Diagnostics.Stopwatch]::StartNew(), $null)
        Assert-True ([bool]$exchange.Invoke($null, $exArgs)) "Named pipe Z75S exchange"
        Assert-True ($pRead.AsyncWaitHandle.WaitOne(1000) -and $pWrite.AsyncWaitHandle.WaitOne(1000)) "Pipe completion"
        [void]$client.EndRead($pRead); $client.EndWrite($pWrite)
        Assert-Equal ([BitConverter]::ToString($validRx)) ([BitConverter]::ToString([byte[]]$exArgs[3])) "Pipe RX exact match"
    } finally {
        $client.Dispose(); $server.Dispose()
    }

    # Z75S UI boundary (0/100%, charging, sleeping preserves cached battery)
    $form = New-SandboxMainForm $false
    $tMain = $form.GetType()
    try {
        $info = [Activator]::CreateInstance($asm.GetType("RazerBatteryTray.MouseBatteryInfo"))
        $info.Category = [Enum]::Parse($asm.GetType("RazerBatteryTray.DeviceCategory"), "Keyboard")
        $info.Brand = "MCHOSE"; $info.DeviceId = "MCHOSE:Z75S:TEST"; $info.DeviceName = "MCHOSE Z75S"
        $info.Transport = "2.4G 无线"; $info.IsConnected = $true; $info.IsPrimary = $true; $info.LastUpdated = [DateTime]::Now
        $info.BatteryPercent = 85; $info.ExactBatteryPercent = 85; $info.IsCharging = $true; $info.IsSleeping = $false
        [void]$tMain.GetMethod("UpdateUI", $bfNonPubInst).Invoke($form, @($info, $false))
        Assert-True ($tMain.GetField("barBattery", $bfNonPubInst).GetValue($form).IsChargingFill) "Z75S charging bar fill"

        $info.IsCharging = $false; $info.IsSleeping = $true
        [void]$tMain.GetMethod("UpdateUI", $bfNonPubInst).Invoke($form, @($info, $false))
        Assert-Equal "85%" $tMain.GetField("lblBatteryBig", $bfNonPubInst).GetValue($form).Text "Z75S keeps cached 85% while sleeping"
    } finally {
        $form.Dispose()
    }
}
