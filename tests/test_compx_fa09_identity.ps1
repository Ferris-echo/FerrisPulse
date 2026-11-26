# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# CompX 公版 0x3554:0xFA09 键盘族归属判定回归测试。
#
# 回归的 BUG：软件把 LEOBOG Hi98 Pro 识别成 MCHOSE G75 PRO。
# 根因：0xFA09 是 CompX 公版参考固件 PID，MCHOSE G75 PRO / KZZI K75 / LEOBOG Hi98 Pro
# 共用同一套接收器，USB 产品串恒为 "2.4G Wireless Receiver"、电量帧完全同质；
# 旧实现把该 PID 硬编码成 MCHOSE G75 PRO，导致整机型误标。
# 现由固件 0x05 状态帧的子型号字节 RX[5] 判定归属，未收录值一律中性名。
#
# 零实机 I/O：全部为离线回放断言（真实抓包样本 + 反射契约 + 前缀归一）。

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
. (Join-Path $PSScriptRoot "..\tools\TestHarness.ps1")

$asm = Load-FerrisPulseAssembly
$bfPubStatic = [System.Reflection.BindingFlags]"Public,Static"

function Parse-HexBytes([string]$hex) {
    $tokens = $hex.Trim().Split(" ", [System.StringSplitOptions]::RemoveEmptyEntries)
    $arr = New-Object byte[] $tokens.Length
    for ($i = 0; $i -lt $tokens.Length; $i++) { $arr[$i] = [Convert]::ToByte($tokens[$i], 16) }
    return ,$arr
}

Invoke-TestCase "CompXFa09.SubtypeFingerprintFromRealCapture" {
    $tFamily = $asm.GetType("RazerBatteryTray.CompxFa09KeyboardHelper")
    Assert-True ($null -ne $tFamily) "CompxFa09KeyboardHelper exists"

    $parseStatus = $tFamily.GetMethod("TryParseStatusSubtype", $bfPubStatic)

    # 真实抓包（本机 LEOBOG Hi98 Pro，2026-11-26）：
    #   TX 13 05 01 00 02 00 ... 1B
    #   RX 13 05 01 00 0A 03 00 00 00 01 21 01 ... 49
    [byte[]]$leobog = Parse-HexBytes "13 05 01 00 0A 03 00 00 00 01 21 01 00 00 00 00 00 00 00 49"
    $argsLeobog = [object[]]@($leobog.PSObject.BaseObject, 20, [byte]0)
    $okLeobog = [bool]$parseStatus.Invoke($null, $argsLeobog)
    Assert-True $okLeobog "LEOBOG status frame parsed"
    Assert-Equal 3 ([int]$argsLeobog[2]) "LEOBOG subtype = 0x03"

    # KZZI K75 历史样本：13 05 01 00 0A 02 ... 5D（子型号 0x02）
    [byte[]]$kzzi = Parse-HexBytes "13 05 01 00 0A 02 00 00 00 00 37 00 01 00 00 00 00 00 00 5D"
    $argsKzzi = [object[]]@($kzzi.PSObject.BaseObject, 20, [byte]0)
    Assert-True ([bool]$parseStatus.Invoke($null, $argsKzzi)) "KZZI status frame parsed"
    Assert-Equal 2 ([int]$argsKzzi[2]) "KZZI subtype = 0x02"

    # 结构字节缺失：把 [4] 的 0x0A 改成 0x0B 必须拒绝
    [byte[]]$badMagic = Parse-HexBytes "13 05 01 00 0B 03 00 00 00 01 21 01 00 00 00 00 00 00 00 4A"
    $argsBad = [object[]]@($badMagic.PSObject.BaseObject, 20, [byte]0)
    Assert-True (-not [bool]$parseStatus.Invoke($null, $argsBad)) "reject wrong magic [4]"

    # 校验和错误必须拒绝
    [byte[]]$badSum = Parse-HexBytes "13 05 01 00 0A 03 00 00 00 01 21 01 00 00 00 00 00 00 00 4A"
    $argsBadSum = [object[]]@($badSum.PSObject.BaseObject, 20, [byte]0)
    Assert-True (-not [bool]$parseStatus.Invoke($null, $argsBadSum)) "reject wrong checksum"

    # 20 字节以下的截断帧必须拒绝
    [byte[]]$shortFrame = Parse-HexBytes "13 05 01 00 0A 03 00 00 00 00"
    $argsShort = [object[]]@($shortFrame.PSObject.BaseObject, 10, [byte]0)
    Assert-True (-not [bool]$parseStatus.Invoke($null, $argsShort)) "reject truncated frame"
}

Invoke-TestCase "CompXFa09.ModelMappingAndNeutralFallback" {
    $tFamily = $asm.GetType("RazerBatteryTray.CompxFa09KeyboardHelper")
    $classify = $tFamily.GetMethod("ClassifySubtype", $bfPubStatic)
    $getName = $tFamily.GetMethod("GetModelName", $bfPubStatic)
    $getBrand = $tFamily.GetMethod("GetBrand", $bfPubStatic)

    $modelKzzi = $classify.Invoke($null, @([byte]0x02, $true))
    $modelLeobog = $classify.Invoke($null, @([byte]0x03, $true))
    $modelUnknown = $classify.Invoke($null, @([byte]0x01, $true))
    $modelKnownMissing = $classify.Invoke($null, @([byte]0x03, $false))

    Assert-Equal "KzziK75" ([string]$modelKzzi) "0x02 -> KzziK75"
    Assert-Equal "LeobogHi98Pro" ([string]$modelLeobog) "0x03 -> LeobogHi98Pro"
    Assert-Equal "Unknown" ([string]$modelUnknown) "0x01 -> Unknown"
    Assert-Equal "Unknown" ([string]$modelKnownMissing) "probe failure -> Unknown"

    Assert-Equal "LEOBOG Hi98 Pro" ([string]$getName.Invoke($null, @($modelLeobog))) "LEOBOG model name"
    Assert-Equal "LEOBOG" ([string]$getBrand.Invoke($null, @($modelLeobog))) "LEOBOG brand"
    Assert-Equal "CompX 2.4G 无线键盘" ([string]$getName.Invoke($null, @($modelUnknown))) "neutral model name"
    Assert-Equal "CompX" ([string]$getBrand.Invoke($null, @($modelUnknown))) "neutral brand"

    # 回归断言：MchoseDeviceHelper.GetModelName(0xFA09, ...) 不得再返回 MCHOSE G75 PRO
    $tMchose = $asm.GetType("RazerBatteryTray.MchoseDeviceHelper")
    $mchoseName = [string]$tMchose.GetMethod("GetModelName", [System.Reflection.BindingFlags]"Public,Static", $null, [type[]]@([int], [string]), $null).Invoke($null, @(0xFA09, "2.4G Wireless Receiver"))
    Assert-Equal "CompX 2.4G 无线键盘" $mchoseName "FA09 no longer hardcoded to MCHOSE G75 PRO"
}

Invoke-TestCase "CompXFa09.BatteryFrameReplay" {
    $tFamily = $asm.GetType("RazerBatteryTray.CompxFa09KeyboardHelper")
    $build = $tFamily.GetMethod("BuildBatteryQueryFrame", $bfPubStatic)
    $parse = $tFamily.GetMethod("TryParseBatteryResponse", $bfPubStatic)

    [byte[]]$tx = $build.Invoke($null, @())
    Assert-Equal 20 $tx.Length "battery query length 20"
    Assert-Equal 0x13 ([int]$tx[0]) "battery query report id 0x13"
    Assert-Equal 0x4A ([int]$tx[1]) "battery query command 0x4A"
    Assert-Equal 0x5D ([int]$tx[19]) "battery query checksum 0x5D"

    # 本机实测回包（LEOBOG Hi98 Pro，94% 电池供电）
    [byte[]]$rx = Parse-HexBytes "13 4A 01 00 02 5E 01 00 00 00 00 00 00 00 00 00 00 00 00 BF"
    $argsRx = [object[]]@($rx.PSObject.BaseObject, 20, 0, $false)
    Assert-True ([bool]$parse.Invoke($null, $argsRx)) "battery response parsed"
    Assert-Equal 94 ([int]$argsRx[2]) "battery 94%"
    Assert-True (-not [bool]$argsRx[3]) "battery discharging (0x01)"

    # 充电位 0x10：仅解析协议，不作为 UI 充电声明依据
    [byte[]]$chg = Parse-HexBytes "13 4A 01 00 02 5D 10 00 00 00 00 00 00 00 00 00 00 00 00 CD"
    $argsChg = [object[]]@($chg.PSObject.BaseObject, 20, 0, $true)
    Assert-True ([bool]$parse.Invoke($null, $argsChg)) "charging frame parsed"
    Assert-Equal 93 ([int]$argsChg[2]) "charging frame 93%"
    Assert-True ([bool]$argsChg[3]) "charging flag 0x10"

    # 越界电量必须拒绝（防 0xE4 之类位域值被当成百分比）
    [byte[]]$overflow = Parse-HexBytes "13 4A 01 00 02 E4 01 00 00 00 00 00 00 00 00 00 00 00 00 45"
    $argsOf = [object[]]@($overflow.PSObject.BaseObject, 20, 0, $false)
    Assert-True (-not [bool]$parse.Invoke($null, $argsOf)) "reject battery > 100"

    # 兼容别名契约（旧测试与外部脚本仍在用）
    $tMchose = $asm.GetType("RazerBatteryTray.MchoseDeviceHelper")
    [byte[]]$aliasTx = $tMchose.GetMethod("BuildG75BatteryFrame", $bfPubStatic).Invoke($null, @())
    Assert-Equal 0x5D ([int]$aliasTx[19]) "legacy BuildG75BatteryFrame still valid"
}

Invoke-TestCase "CompXFa09.DriverIdentityIsolation" {
    $tFamily = $asm.GetType("RazerBatteryTray.CompxFa09KeyboardHelper")
    $tDriver = $asm.GetType("RazerBatteryTray.CompxFa09KeyboardDriver")
    $tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo")
    $tCat = $asm.GetType("RazerBatteryTray.DeviceCategory")
    Assert-True ($null -ne $tDriver) "CompxFa09KeyboardDriver exists"

    $driver = [Activator]::CreateInstance($tDriver)

    function New-KeyboardCard([string]$id, [string]$brand) {
        $info = [Activator]::CreateInstance($tInfo)
        $info.DeviceId = $id
        $info.Brand = $brand
        $info.Category = [Enum]::Parse($tCat, "Keyboard")
        return $info
    }

    $leobogCard = New-KeyboardCard "LEOBOG:3554:FA09" "LEOBOG"
    $neutralCard = New-KeyboardCard "CompX:3554:FA09" "CompX"
    $kzziCard = New-KeyboardCard "KZZI:3554:FA09" "KZZI"
    $mchoseG87Card = New-KeyboardCard "MCHOSE:3837:3033" "MCHOSE"
    # 另一套公版键盘：05AC:024F（CompxWirelessKeyboardHelper），报告长度与协议完全不同
    $compXAppleCard = New-KeyboardCard "CompX:05AC:024F" "CompX"

    Assert-True ($driver.CanHandle($leobogCard)) "driver handles LEOBOG FA09"
    Assert-True ($driver.CanHandle($neutralCard)) "driver handles neutral FA09"
    Assert-True (-not $driver.CanHandle($kzziCard)) "driver rejects KZZI card"
    Assert-True (-not $driver.CanHandle($mchoseG87Card)) "driver rejects MCHOSE 3837:3033"
    Assert-True (-not $driver.CanHandle($compXAppleCard)) "driver rejects CompX 05AC:024F"

    # 驱动解析：LEOBOG 卡必须落到公版族驱动，KZZI 卡必须仍由 KZZI 驱动接管
    [RazerBatteryTray.BuiltInDrivers]::EnsureRegistered() | Out-Null
    $resolvedLeobog = [RazerBatteryTray.HardwareDriverRegistry]::ResolveDriver($leobogCard)
    $resolvedKzzi = [RazerBatteryTray.HardwareDriverRegistry]::ResolveDriver($kzziCard)
    Assert-Equal "BuiltIn:CompxFa09Keyboard" ([string]$resolvedLeobog.DriverId) "LEOBOG resolves to CompX family driver"
    Assert-Equal "BuiltIn:KzziKeyboard" ([string]$resolvedKzzi.DriverId) "KZZI still resolves to KZZI driver"

    # 能力降级：只声明电量，绝不暴露 DPI / 回报率
    $driver.PopulateCapabilities($leobogCard)
    Assert-True ($leobogCard.Capabilities.SupportsBattery) "battery supported"
    Assert-True (-not $leobogCard.Capabilities.SupportsDpi) "DPI not claimed"
    Assert-True (-not $leobogCard.Capabilities.SupportsPollingRate) "polling rate not claimed"
    Assert-True ($leobogCard.Capabilities.ChargingFlagMeansUsbPoweredOnly) "charging flag is usb-powered-only"
}

Invoke-TestCase "CompXFa09.DuplicateCardNamespaceNormalization" {
    # 同一台物理接收器可能被记成 CompX: / HID: / LEOBOG: 三种前缀，
    # 前缀归一必须能互相命中，否则主界面裂成两张卡。
    $variants = [RazerBatteryTray.DeviceManager]::GetDeviceIdVariants("LEOBOG:3554:FA09")
    Assert-True ($variants -contains "CompX:3554:FA09") "LEOBOG variant covers CompX namespace"
    Assert-True ($variants -contains "HID:3554:FA09") "LEOBOG variant covers legacy HID namespace"
    Assert-True ($variants -contains "LEOBOG:3554:FA09") "LEOBOG variant keeps itself"

    $variants2 = [RazerBatteryTray.DeviceManager]::GetDeviceIdVariants("CompX:3554:FA09")
    Assert-True ($variants2 -contains "LEOBOG:3554:FA09") "CompX variant covers LEOBOG namespace"
    Assert-True ($variants2 -contains "HID:3554:FA09") "CompX variant covers HID namespace"
}

Invoke-TestCase "CompXFa09.StaleHiddenMarkerDoesNotHideRenamedIdentity" {
    # 用户隐藏过历史上那张「错标为 MCHOSE G75 PRO」的卡片时，HideOrDeleteDevice 会把
    # 30 个命名空间变体一并写成隐藏标记；身份修正后这些旧变体不得再连带隐藏新卡片。
    $regPath = "HKCU:\Software\RazerBatteryTray\HiddenDevices"
    if (-not (Test-Path $regPath)) { New-Item -Path $regPath -Force | Out-Null }
    $markerKey = "MCHOSE:3554:FA09"
    $original = $null
    try { $original = (Get-ItemProperty -Path $regPath -Name $markerKey -ErrorAction Stop).$markerKey } catch { $original = $null }
    try {
        New-ItemProperty -Path $regPath -Name $markerKey -Value 1 -PropertyType DWord -Force | Out-Null
        Assert-True ([RazerBatteryTray.DeviceManager]::IsDeviceHidden("LEOBOG:3554:FA09")) "variant lookup still sees legacy marker"
        Assert-True (-not [RazerBatteryTray.DeviceManager]::IsHiddenExplicitly("LEOBOG:3554:FA09")) "exact lookup ignores stale cross-namespace marker"
    } finally {
        if ($null -ne $original) {
            Set-ItemProperty -Path $regPath -Name $markerKey -Value ([int]$original)
        } else {
            Remove-ItemProperty -Path $regPath -Name $markerKey -ErrorAction SilentlyContinue
        }
    }
}