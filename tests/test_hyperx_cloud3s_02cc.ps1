# ============================================================================
# 测试脚本: test_hyperx_cloud3s_02cc.ps1
# 目标: 验证 HyperX 飓风3S 无线 (Cloud III S Wireless, 03F0:02CC / 06BE) 适配：
#   1. VID/PID 白名单与 0x01C0/64 字节端点四元组隔离（严禁误吞 0xFF13/62 固件集合）
#   2. 0x0C 电量查询帧构造与本机实机回帧解析 (0C 02 03 01 00 06 2D 0E C4 -> 45%)
#   3. 0x0D 充电事件帧三态解析，且不得用旧版 0x66 报文混淆
#   4. 实机在线时电量落在 0..100 的物理合理区间
# 铁律: 全内存反射加载，严禁 Add-Type -Path（防止 exe 句柄锁死后续 build.bat）。
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = Resolve-Path "$ScriptDir\.."
Set-Location $ProjectDir

Write-Host "[TEST] Validating HyperX Cloud III S Wireless (03F0:02CC) Support..." -ForegroundColor Cyan

$exePath = "$ProjectDir\FerrisPulse.exe"
if (-not (Test-Path $exePath)) {
    throw "FerrisPulse.exe not found! Run build.bat first."
}

$asmBytes = [System.IO.File]::ReadAllBytes($exePath)
$asm = [System.Reflection.Assembly]::Load($asmBytes)
$helperType = $asm.GetType("RazerBatteryTray.HyperXCloud3SHelper")
if ($null -eq $helperType) { throw "HyperXCloud3SHelper type not found!" }

$flags = [System.Reflection.BindingFlags]"Public,Static"

# --- 1. VID/PID 白名单 ---
$isSupported = $helperType.GetMethod("IsSupportedVidPid", $flags)
if (-not $isSupported.Invoke($null, @(0x03F0, 0x02CC))) { throw "03F0:02CC should be supported" }
if (-not $isSupported.Invoke($null, @(0x03F0, 0x06BE))) { throw "03F0:06BE should be supported" }
if ($isSupported.Invoke($null, @(0x03F0, 0x05B7))) { throw "03F0:05B7 belongs to Cloud III (0x66 protocol), must NOT match" }
if ($isSupported.Invoke($null, @(0x1532, 0x02CC))) { throw "Foreign VID must NOT match" }
Write-Host "  [PASS] VID/PID whitelist isolated (02CC/06BE in, 05B7/foreign out)" -ForegroundColor Green

# --- 2. 端点四元组隔离 ---
$descType = $asm.GetType("RazerBatteryTray.HidDeviceDescriptor")
$isEndpoint = $helperType.GetMethod("IsBatteryEndpoint", $flags)

$okDesc = [System.Activator]::CreateInstance($descType)
$okDesc.VendorId = 0x03F0
$okDesc.ProductId = 0x02CC
$okDesc.UsagePage = 0x01C0
$okDesc.Usage = 0x0001
$okDesc.InputReportLength = 64
$okDesc.OutputReportLength = 64
if (-not $isEndpoint.Invoke($null, @($okDesc))) { throw "Valid 0x01C0/64 endpoint rejected" }

$fwDesc = [System.Activator]::CreateInstance($descType)
$fwDesc.VendorId = 0x03F0
$fwDesc.ProductId = 0x02CC
$fwDesc.UsagePage = 0xFF13
$fwDesc.Usage = 0x0001
$fwDesc.InputReportLength = 62
$fwDesc.OutputReportLength = 62
if ($isEndpoint.Invoke($null, @($fwDesc))) { throw "0xFF13/62 firmware channel must never be treated as battery endpoint" }

$keyDesc = [System.Activator]::CreateInstance($descType)
$keyDesc.VendorId = 0x03F0
$keyDesc.ProductId = 0x02CC
$keyDesc.UsagePage = 0x170F
$keyDesc.Usage = 0x0202
$keyDesc.InputReportLength = 8
$keyDesc.OutputReportLength = 8
if ($isEndpoint.Invoke($null, @($keyDesc))) { throw "0x170F/8 key channel must never be treated as battery endpoint" }
Write-Host "  [PASS] 0x01C0/64 accepted; 0xFF13/62 and 0x170F/8 rejected" -ForegroundColor Green

# --- 3. 查询帧构造 ---
$buildQuery = $helperType.GetMethod("BuildBatteryQuery", $flags)
[byte[]]$tx = $buildQuery.Invoke($null, @(64))
if ($tx.Length -ne 64) { throw "Query frame length must be 64, got $($tx.Length)" }
if ($tx[0] -ne 0x0C -or $tx[1] -ne 0x02 -or $tx[2] -ne 0x03 -or $tx[3] -ne 0x01 -or $tx[4] -ne 0x00 -or $tx[5] -ne 0x06) {
    throw "Query frame header mismatch: $($tx[0..5] -join ' ')"
}
Write-Host "  [PASS] Query frame = 0C 02 03 01 00 06 00 ..." -ForegroundColor Green

# --- 4. 实机回帧解析 (本机 03F0:02CC 实测: 2D -> 45%) ---
$parseBatt = $helperType.GetMethod("TryParseBattery", $flags)
[byte[]]$rxReal = New-Object byte[] 64
$rxReal[0] = 0x0C; $rxReal[1] = 0x02; $rxReal[2] = 0x03; $rxReal[3] = 0x01
$rxReal[4] = 0x00; $rxReal[5] = 0x06; $rxReal[6] = 0x2D; $rxReal[7] = 0x0E; $rxReal[8] = 0xC4
[object[]]$args = @($rxReal, 0, [byte]0, [byte]0)
$ok = $parseBatt.Invoke($null, $args)
if (-not $ok) { throw "Real battery frame rejected" }
if ([int]$args[1] -ne 45) { throw "Battery percent expected 45, got $($args[1])" }
if ([byte]$args[2] -ne 0x0E -or [byte]$args[3] -ne 0xC4) { throw "Flag bytes mismatch" }
Write-Host "  [PASS] Real frame 0C 02 03 01 00 06 2D 0E C4 -> 45% (flags 0E C4)" -ForegroundColor Green

# --- 5. 异常帧必须拒绝 ---
[byte[]]$rxBad = New-Object byte[] 64
$rxBad[0] = 0x0C; $rxBad[1] = 0x02; $rxBad[2] = 0x03; $rxBad[5] = 0x06; $rxBad[6] = 0xFF
[object[]]$argsBad = @($rxBad, 0, [byte]0, [byte]0)
if ($parseBatt.Invoke($null, $argsBad)) { throw "Out-of-range percent (0xFF) must be rejected" }

[byte[]]$rxOld = New-Object byte[] 62
$rxOld[0] = 0x66; $rxOld[1] = 0x89; $rxOld[2] = 0x01; $rxOld[3] = 0x01; $rxOld[4] = 0x37
[object[]]$argsOld = @($rxOld, 0, [byte]0, [byte]0)
if ($parseBatt.Invoke($null, $argsOld)) { throw "Legacy Cloud III 0x66 frame must NOT parse on Cloud III S" }

[byte[]]$rxShort = New-Object byte[] 5
[object[]]$argsShort = @($rxShort, 0, [byte]0, [byte]0)
if ($parseBatt.Invoke($null, $argsShort)) { throw "Truncated frame must be rejected" }
Write-Host "  [PASS] 0xFF percent / legacy 0x66 frame / truncated frame all rejected" -ForegroundColor Green

# --- 6. 充电事件三态 ---
$parseChg = $helperType.GetMethod("TryParseCharging", $flags)
[byte[]]$rxChgOn = [byte[]]@(0x0D, 0x02, 0x03, 0x00, 0x0A, 0x01)
[object[]]$a1 = @($rxChgOn, $false)
if (-not $parseChg.Invoke($null, $a1) -or -not $a1[1]) { throw "Charging frame state=1 should yield true" }

[byte[]]$rxChgOff = [byte[]]@(0x0D, 0x02, 0x03, 0x00, 0x0A, 0x00)
[object[]]$a2 = @($rxChgOff, $true)
if (-not $parseChg.Invoke($null, $a2) -or $a2[1]) { throw "Charging frame state=0 should yield false" }

[object[]]$a3 = @($rxReal, $false)
if ($parseChg.Invoke($null, $a3)) { throw "0x0C battery frame must not be parsed as charging event" }
Write-Host "  [PASS] Charging event 0D 02 03 00 0A 01/00 -> true/false (3-state safe)" -ForegroundColor Green

# --- 7. 实机在线校验（无设备时跳过，不作为失败） ---
$live = $helperType.GetMethod("QueryDevice", $flags).Invoke($null, $null)
if ($null -eq $live) {
    Write-Host "  [SKIP] No Cloud III S dongle present - live assertion skipped" -ForegroundColor Yellow
} else {
    if ($live.BatteryPercent -lt 0 -or $live.BatteryPercent -gt 100) {
        throw "Live battery percent out of range: $($live.BatteryPercent)"
    }
    if ($live.Category.ToString() -ne "Headset") { throw "Category must be Headset, got $($live.Category)" }
    Write-Host ("  [PASS] Live device: " + $live.DeviceName + " " + $live.BatteryPercent + "% connected=" + $live.IsConnected + " chargingUnknown=" + $live.IsChargingUnknown) -ForegroundColor Green
}

Write-Host "[ALL PASSED] HyperX Cloud III S Wireless (03F0:02CC) test suite succeeded!" -ForegroundColor Green
exit 0
