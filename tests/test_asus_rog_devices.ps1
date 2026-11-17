# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# Pure replay tests: no hardware writes, no user registry changes, no EXE file locks.
$ErrorActionPreference = 'Stop'
$root = (Resolve-Path "$PSScriptRoot\..").Path
$asm = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'FerrisPulse.exe')))
$t = $asm.GetType('RazerBatteryTray.AsusDeviceHelper')
$dm = $asm.GetType('RazerBatteryTray.DeviceManager')
$gaming = $asm.GetType('RazerBatteryTray.UniversalGamingDeviceHelper')
function Assert($ok, $why) { if (-not $ok) { throw "FAIL: $why" } }
Assert ($null -ne $t) 'AsusDeviceHelper missing'

$flags = [Reflection.BindingFlags]'Public,NonPublic,Static'

function New-Packet([int]$len, [byte[]]$prefix) {
    $buf = New-Object byte[] $len
    if ($null -ne $prefix) { [Array]::Copy($prefix, $buf, $prefix.Length) }
    return [byte[]]$buf
}

function Invoke-RogBatt([byte[]]$pkt, [byte]$rid, [int]$expectedLen) {
    $a = [object[]]@([byte[]]$pkt, [byte]$rid, [int]$expectedLen, -1, $false)
    $ok = $parseBatt.Invoke($null, $a)
    return @($ok, $a[3], $a[4])
}

# 1. 1AD0 (ROG AZOTH 96 HE Wireless, 64B, reportId 0x02) captured replay
$parseBatt = $t.GetMethod('TryParseRogAzoth96Battery', $flags)
Assert ($null -ne $parseBatt) 'TryParseRogAzoth96Battery missing'

[byte[]]$rxWireless = New-Packet 64 @(0x02, 0x12, 0x01, 0x00, 0x00, 0x00, 0x64, 0x03, 0x01, 0x00, 0x14, 0x64, 0x32, 0x11)
$rW = Invoke-RogBatt $rxWireless 0x02 64
Assert ($rW[0] -and $rW[1] -eq 100 -and -not $rW[2]) '1AD0 captured 100% discharging replay'

# Boundary 0 and 100, and charging = 1
foreach ($pct in @(0, 45, 100)) {
    [byte[]]$p = $rxWireless.Clone()
    $p[6] = $pct
    $p[9] = 1
    $rB = Invoke-RogBatt $p 0x02 64
    Assert ($rB[0] -and $rB[1] -eq $pct -and $rB[2]) "1AD0 boundary $pct% charging"
}

# Reject >100, charging >1, wrong header, sleep NAK 02 FF AA, truncated
[byte[]]$badPct = $rxWireless.Clone(); $badPct[6] = 101
Assert (-not (Invoke-RogBatt $badPct 0x02 64)[0]) 'reject percent > 100'
[byte[]]$badChg = $rxWireless.Clone(); $badChg[9] = 2
Assert (-not (Invoke-RogBatt $badChg 0x02 64)[0]) 'reject charging > 1'
[byte[]]$nak = New-Packet 64 @(0x02, 0xFF, 0xAA)
Assert (-not (Invoke-RogBatt $nak 0x02 64)[0]) 'reject sleep NAK 02 FF AA'
[byte[]]$trunc = New-Packet 12 @(0x02, 0x12, 0x01, 0, 0, 0, 50, 0, 0, 0)
Assert (-not (Invoke-RogBatt $trunc 0x02 64)[0]) 'reject truncated packet'
Assert (-not (Invoke-RogBatt $rxWireless 0x00 64)[0]) 'reject mismatched reportId'

# 2. 1C10 (ROG AZOTH 96 HE Wired, 65B, reportId 0x00) captured replay
[byte[]]$rxWired = New-Packet 65 @(0x00, 0x12, 0x01, 0x00, 0x00, 0x00, 0x64, 0x03, 0x01, 0x01, 0x14, 0x64, 0x3C, 0x11)
$rWired = Invoke-RogBatt $rxWired 0x00 65
Assert ($rWired[0] -and $rWired[1] -eq 100 -and $rWired[2]) '1C10 captured 100% charging replay'

# 3. Ready probe replay (0x01 ready vs 0xFA sleep)
$parseReady = $t.GetMethod('TryParseRogReady', $flags)
Assert ($null -ne $parseReady) 'TryParseRogReady missing'
[byte[]]$readyOk = New-Packet 64 @(0x02, 0x12, 0x00, 0x00, 0x00, 0x01)
[byte[]]$readySleep = New-Packet 64 @(0x02, 0x12, 0x00, 0x00, 0x00, 0xFA)
Assert ($parseReady.Invoke($null, [object[]]@([byte[]]$readyOk, [byte]0x02, 64))) 'ready packet RX[5]=0x01'
Assert (-not $parseReady.Invoke($null, [object[]]@([byte[]]$readySleep, [byte]0x02, 64))) 'sleep packet RX[5]=0xFA'

# 4. 1AFA (ROG DELTA II, 64B, reportId 0xCC) captured replay via existing TryParseBatteryReport/Charging
[byte[]]$rxHeadset = New-Packet 64 @(0xCC, 0x12, 0x07, 0x00, 0x00, 0x05, 0x25, 0x14, 0x00)
$hArgs = [object[]]@([byte[]]$rxHeadset, -1)
Assert ($t.GetMethod('TryParseBatteryReport', $flags).Invoke($null, $hArgs) -and $hArgs[1] -eq 37) '1AFA captured 37% battery replay'

# 5. Four-tuple endpoint isolation & UniversalGaming exclusion
$isRogEp = $t.GetMethod('IsRogEndpoint', $flags)
Assert ($null -ne $isRogEp) 'IsRogEndpoint missing'
$descType = $asm.GetType('RazerBatteryTray.HidDeviceDescriptor')
$desc = [Activator]::CreateInstance($descType)
$desc.VendorId = 0x0B05; $desc.ProductId = 0x1AD0; $desc.UsagePage = 0xFF00; $desc.Usage = 1; $desc.InputReportLength = 64; $desc.OutputReportLength = 64
Assert ($isRogEp.Invoke($null, @($desc, 0x1AD0, 64))) '1AD0 FF00/0001 64/64 allowed'
$desc.UsagePage = 0xFF01
Assert (-not $isRogEp.Invoke($null, @($desc, 0x1AD0, 64))) '1AD0 mouse slot FF01 rejected'
$desc.UsagePage = 0xFF00; $desc.ProductId = 0x1C10; $desc.InputReportLength = 65; $desc.OutputReportLength = 65
Assert ($isRogEp.Invoke($null, @($desc, 0x1C10, 65))) '1C10 FF00/0001 65/65 allowed'
$desc.InputReportLength = 64
Assert (-not $isRogEp.Invoke($null, @($desc, 0x1C10, 65))) '1C10 wrong packet length rejected'

foreach ($pidText in @('1ad0', '1afa', '1c10')) {
    Assert (-not $gaming.GetMethod('IsSupportedGamingDevice').Invoke($null, @(0x0B05, "hid#vid_0b05&pid_$pidText", 'ROG'))) "UniversalGaming must exclude pid_$pidText"
}

# 6. Capability discipline & vanished-path preservation
$buildRog = $t.GetMethod('BuildRogDeviceInfo', $flags)
Assert ($null -ne $buildRog) 'BuildRogDeviceInfo missing'
$desc.ProductId = 0x1AD0; $desc.InputReportLength = 64; $desc.OutputReportLength = 64
$kbW = $buildRog.Invoke($null, @($desc, 100, $false, $true, [DateTime]::Now))
Assert ($kbW.Category.ToString() -eq 'Keyboard' -and -not $kbW.Capabilities.SupportsDpi -and -not $kbW.Capabilities.SupportsPollingRate -and -not $kbW.Capabilities.SupportsXyIndependence) '1AD0 has no mouse write capabilities'

$desc.ProductId = 0x1AFA
$hs = $buildRog.Invoke($null, @($desc, 37, $false, $true, [DateTime]::Now))
Assert ($hs.Category.ToString() -eq 'Headset' -and $hs.BatteryPercent -eq 37 -and -not $hs.Capabilities.SupportsDpi -and -not $hs.Capabilities.SupportsPollingRate) '1AFA headset capability contract'

$desc.ProductId = 0x1C10
$kbWired = $buildRog.Invoke($null, @($desc, 100, $true, $true, [DateTime]::Now))
Assert ($kbWired.Category.ToString() -eq 'Keyboard' -and $kbWired.Transport -eq 'USB 有线' -and -not $kbWired.Capabilities.SupportsDpi) '1C10 wired capability contract'

$stateType = $t.GetNestedType('RogDevState', [Reflection.BindingFlags]'NonPublic')
Assert ($null -ne $stateType) 'RogDevState missing'
$st = [Activator]::CreateInstance($stateType, $true)
$stamp = [DateTime]::Parse('2026-11-10T10:00:00')
$st.Battery = 88; $st.Charging = $true; $st.Ready = $true; $st.LastUpdated = $stamp
$desc.ProductId = 0x1AD0
$desc.DevicePath = '\\?\FerrisPulse-Nonexistent-ROG-Test'
$t.GetMethod('ReadWirelessKeyboardState', $flags).Invoke($null, @($desc, $st)) | Out-Null
Assert ($st.Battery -eq 88 -and $st.LastUpdated -eq $stamp) 'failed read preserves last real battery and timestamp'
Assert (-not $st.Ready -and -not $st.Charging) 'failed read clears live ready and charging'

# 7. Three-channel online arbitration (Wired > 2.4G, Bluetooth > 2.4G, no false suppression when only charging via cable)
$arb = $dm.GetMethod('ApplyCrossTransportKeyboardArbitration', $flags)
Assert ($null -ne $arb) 'ApplyCrossTransportKeyboardArbitration missing'
$infoType = $asm.GetType('RazerBatteryTray.MouseBatteryInfo')
$listType = [System.Collections.Generic.List``1].MakeGenericType($infoType)

# Case A: Wired active with real battery suppresses 2.4G
$listA = [Activator]::CreateInstance($listType)
$w24 = $buildRog.Invoke($null, @($desc, 100, $false, $true, [DateTime]::Now))
$descWired = [Activator]::CreateInstance($descType); $descWired.VendorId = 0x0B05; $descWired.ProductId = 0x1C10
$wWired = $buildRog.Invoke($null, @($descWired, 100, $true, $true, [DateTime]::Now))
$listA.Add($w24); $listA.Add($wWired)
$arb.Invoke($null, [object[]]@(,$listA)) | Out-Null
Assert ((-not $w24.IsConnected) -and $w24.IsSleeping -and (-not $w24.IsCharging)) 'wired active suppresses same-model 2.4G'
Assert ($wWired.IsConnected) 'wired stays connected'

# Case B: Wired plugged in without response (switch still on 2.4G) does NOT suppress 2.4G
$listB = [Activator]::CreateInstance($listType)
$w24b = $buildRog.Invoke($null, @($desc, 75, $false, $true, [DateTime]::Now))
$wWiredIdle = $buildRog.Invoke($null, @($descWired, -1, $false, $false, [DateTime]::MinValue))
$listB.Add($w24b); $listB.Add($wWiredIdle)
$arb.Invoke($null, [object[]]@(,$listB)) | Out-Null
Assert ($w24b.IsConnected -and -not $w24b.IsSleeping) '2.4G stays online when wired has no valid battery/charging response'

# Case C: Bluetooth same-model suppresses 2.4G, unrelated BT does NOT
$listC = [Activator]::CreateInstance($listType)
$w24c = $buildRog.Invoke($null, @($desc, -1, $false, $true, [DateTime]::Now))
$btSame = [Activator]::CreateInstance($infoType)
$btSame.DeviceId = 'BT_AABBCCDDEEFF'; $btSame.DeviceName = 'ROG AZOTH 96 HE'; $btSame.Category = [RazerBatteryTray.DeviceCategory]::Keyboard; $btSame.IsConnected = $true; $btSame.BatteryPercent = 92; $btSame.Transport = '蓝牙'
$listC.Add($w24c); $listC.Add($btSame)
$arb.Invoke($null, [object[]]@(,$listC)) | Out-Null
Assert ((-not $w24c.IsConnected) -and $w24c.IsSleeping) 'same-model Bluetooth online suppresses 2.4G'

$listD = [Activator]::CreateInstance($listType)
$w24d = $buildRog.Invoke($null, @($desc, 80, $false, $true, [DateTime]::Now))
$btOther = [Activator]::CreateInstance($infoType)
$btOther.DeviceId = 'BT_112233445566'; $btOther.DeviceName = 'Keychron K2'; $btOther.Category = [RazerBatteryTray.DeviceCategory]::Keyboard; $btOther.IsConnected = $true; $btOther.BatteryPercent = 70; $btOther.Transport = '蓝牙'
$listD.Add($w24d); $listD.Add($btOther)
$arb.Invoke($null, [object[]]@(,$listD)) | Out-Null
Assert ($w24d.IsConnected -and -not $w24d.IsSleeping) 'unrelated Bluetooth keyboard must not suppress 2.4G AZOTH 96 HE'

# 8. 3-segment tray battery summary & keyboard preference (active/wired beats sleeping)
$listSum = [Activator]::CreateInstance($listType)
$m = [Activator]::CreateInstance($infoType); $m.DeviceId = 'Test:M'; $m.Category = [RazerBatteryTray.DeviceCategory]::Mouse; $m.IsConnected = $true; $m.BatteryPercent = 51; $m.Transport = '2.4G 无线'
$kSleep = [Activator]::CreateInstance($infoType); $kSleep.DeviceId = 'Test:KS'; $kSleep.Category = [RazerBatteryTray.DeviceCategory]::Keyboard; $kSleep.IsConnected = $true; $kSleep.IsSleeping = $true; $kSleep.BatteryPercent = 80; $kSleep.Transport = '2.4G 无线'
$kWired = [Activator]::CreateInstance($infoType); $kWired.DeviceId = 'Test:KW'; $kWired.Category = [RazerBatteryTray.DeviceCategory]::Keyboard; $kWired.IsConnected = $true; $kWired.BatteryPercent = 100; $kWired.IsCharging = $true; $kWired.Transport = 'USB 有线'
$h = [Activator]::CreateInstance($infoType); $h.DeviceId = 'Test:H'; $h.Category = [RazerBatteryTray.DeviceCategory]::Headset; $h.IsConnected = $true; $h.BatteryPercent = 37; $h.Transport = '2.4G 无线'
$listSum.Add($m); $listSum.Add($kSleep); $listSum.Add($kWired); $listSum.Add($h)

$summary = $dm.GetMethod('BuildTrayBatterySummary', $flags).Invoke($null, [object[]]@(,$listSum))
Assert ($summary -match '鼠标 51%' -and $summary -match '键盘 供电中' -and $summary -match '耳机 37%') "3-segment summary mismatch: $summary"

Write-Host 'PASS: ASUS ROG (1AD0/1AFA/1C10) replay, endpoint isolation, 3-channel arbitration, and 3-segment tray summary'
