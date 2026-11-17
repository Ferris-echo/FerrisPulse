# ============================================================================
# 测试脚本: test_compx_wireless_keyboard_05ac024f.ps1
# 目标: 验证公版 CompX 2.4G 无线键盘 (05AC:024F "2.4G Wireless Receiver") 适配：
#   1. 与 GS3108T-PRO 共用 Apple VID/PID，必须靠产品串隔离，且绝不被 Apple 逻辑吞掉
#   2. 33 字节 0xFF60/0x61 才是电量端点，65 字节同 UsagePage 通道必须拒绝
#   3. 查询帧 00 20 01 00 ... 21 与实机回包解析 (38 -> 56%, 36 -> 54%)
# 铁律: 全内存反射加载，严禁 Add-Type -Path（防止 exe 句柄锁死后续 build.bat）。
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = Resolve-Path "$ScriptDir\.."
Set-Location $ProjectDir

Write-Host "[TEST] Validating CompX 2.4G Wireless Keyboard (05AC:024F)..." -ForegroundColor Cyan

$exePath = "$ProjectDir\FerrisPulse.exe"
if (-not (Test-Path $exePath)) { throw "FerrisPulse.exe not found! Run build.bat first." }

$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath))
$t = $asm.GetType("RazerBatteryTray.CompxWirelessKeyboardHelper")
if ($null -eq $t) { throw "CompxWirelessKeyboardHelper type not found!" }
$flags = [System.Reflection.BindingFlags]"Public,Static"

# --- 1. VID/PID 与产品串隔离 ---
$isSup = $t.GetMethod("IsSupportedVidPid", $flags)
if (-not $isSup.Invoke($null, @(0x05AC, 0x024F))) { throw "05AC:024F should be supported" }
if ($isSup.Invoke($null, @(0x05AC, 0x026C))) { throw "Other Apple PIDs must not match" }
Write-Host "  [PASS] VID/PID gate = 05AC:024F only" -ForegroundColor Green

$isRec = $t.GetMethod("IsRecognizedProduct", $flags)
if (-not $isRec.Invoke($null, @("2.4G Wireless Receiver", 0x05AC, 0x024F))) { throw "Real product string should be recognized" }
if ($isRec.Invoke($null, @("GS3108T-PRO 2.4G", 0x05AC, 0x024F))) { throw "GS3108T-PRO must stay with GanssDeviceHelper" }
if ($isRec.Invoke($null, @("Magic Keyboard", 0x05AC, 0x024F))) { throw "Apple Magic Keyboard must not be claimed" }
if ($isRec.Invoke($null, @("2.4G Wireless Receiver", 0x1532, 0x024F))) { throw "Foreign VID must not be claimed" }
Write-Host "  [PASS] Product-string isolation (GS3108T-PRO / Apple Magic excluded)" -ForegroundColor Green

# --- 2. 端点四元组隔离：33B 命中，65B 拒绝 ---
$descType = $asm.GetType("RazerBatteryTray.HidDeviceDescriptor")
$isEp = $t.GetMethod("IsBatteryEndpoint", $flags)

$ok = [System.Activator]::CreateInstance($descType)
$ok.VendorId = 0x05AC; $ok.ProductId = 0x024F; $ok.ProductString = "2.4G Wireless Receiver"
$ok.UsagePage = 0xFF60; $ok.Usage = 0x0061; $ok.InputReportLength = 33; $ok.OutputReportLength = 33
if (-not $isEp.Invoke($null, @($ok))) { throw "33B 0xFF60/0x61 endpoint rejected" }

$cfg = [System.Activator]::CreateInstance($descType)
$cfg.VendorId = 0x05AC; $cfg.ProductId = 0x024F; $cfg.ProductString = "2.4G Wireless Receiver"
$cfg.UsagePage = 0xFF60; $cfg.Usage = 0x0061; $cfg.InputReportLength = 65; $cfg.OutputReportLength = 65
if ($isEp.Invoke($null, @($cfg))) { throw "65B config channel must never be treated as battery endpoint" }

$noName = [System.Activator]::CreateInstance($descType)
$noName.VendorId = 0x05AC; $noName.ProductId = 0x024F; $noName.ProductString = "GS3108T-PRO"
$noName.UsagePage = 0xFF60; $noName.Usage = 0x0061; $noName.InputReportLength = 33; $noName.OutputReportLength = 33
if ($isEp.Invoke($null, @($noName))) { throw "GS3108T-PRO endpoint must not be claimed by CompX helper" }
Write-Host "  [PASS] 33B accepted; 65B config channel and GS3108T-PRO rejected" -ForegroundColor Green

# --- 3. 查询帧 ---
[byte[]]$tx = $t.GetMethod("BuildBatteryQuery", $flags).Invoke($null, $null)
if ($tx.Length -ne 33) { throw "Query frame length must be 33, got $($tx.Length)" }
if ($tx[0] -ne 0x00 -or $tx[1] -ne 0x20 -or $tx[2] -ne 0x01 -or $tx[32] -ne 0x21) {
    throw "Query frame mismatch: $($tx[0..2] -join ' ') / trailer=$($tx[32])"
}
Write-Host "  [PASS] Query frame = 00 20 01 00 ... 21" -ForegroundColor Green

# --- 4. 实机回包解析（本机两次实测：38 -> 56%，36 -> 54%） ---
$parse = $t.GetMethod("TryParseBattery", $flags)
[byte[]]$rx56 = [byte[]]@(0,0x20,0x01,0x00,0x38) + (New-Object byte[] 27) + [byte[]]@(0x59)
[object[]]$a1 = @($rx56, 0)
if (-not $parse.Invoke($null, $a1) -or [int]$a1[1] -ne 56) { throw "Real frame 0x38 should parse as 56%" }

[byte[]]$rx54 = [byte[]]@(0,0x20,0x01,0x00,0x36) + (New-Object byte[] 27) + [byte[]]@(0x57)
[object[]]$a2 = @($rx54, 0)
if (-not $parse.Invoke($null, $a2) -or [int]$a2[1] -ne 54) { throw "Real frame 0x36 should parse as 54%" }
Write-Host "  [PASS] Real frames parsed: 0x38 -> 56%, 0x36 -> 54%" -ForegroundColor Green

# --- 5. 异常帧拒绝 ---
[byte[]]$rxBad = [byte[]]@(0,0x20,0x01,0x00,0xFF) + (New-Object byte[] 28)
[object[]]$a3 = @($rxBad, 0)
if ($parse.Invoke($null, $a3)) { throw "Percent 0xFF must be rejected" }

[byte[]]$rxWrongCmd = [byte[]]@(0,0x21,0x01,0x00,0x38) + (New-Object byte[] 27) + [byte[]]@(0x59)
[object[]]$a4 = @($rxWrongCmd, 0)
if ($parse.Invoke($null, $a4)) { throw "Wrong command byte must be rejected" }

[byte[]]$rxShort = New-Object byte[] 12
[object[]]$a5 = @($rxShort, 0)
if ($parse.Invoke($null, $a5)) { throw "Truncated frame must be rejected" }
Write-Host "  [PASS] 0xFF percent / wrong command / truncated frame all rejected" -ForegroundColor Green

# --- 6. Apple 逻辑不得再吞掉该设备 ---
$apple = $asm.GetType("RazerBatteryTray.AppleDeviceHelper")
$isMagic = $apple.GetMethod("IsAppleMagicDevice", $flags)
if ($isMagic.Invoke($null, @("\\?\hid#vid_05ac&pid_024f&mi_03#x", "2.4G Wireless Receiver", 0x05AC, 0x024F))) {
    throw "AppleDeviceHelper must NOT claim the CompX keyboard"
}
Write-Host "  [PASS] AppleDeviceHelper no longer claims 05AC:024F keyboard" -ForegroundColor Green

# --- 7. 实机校验 ---
# 契约: null = 接收器不在(设备真正移除)；非 null 且 IsConnected=false = 接收器在线但键盘休眠(条目必须保留)
$live = $t.GetMethod("QueryDevice", $flags).Invoke($null, $null)
if ($null -eq $live) {
    Write-Host "  [SKIP] Dongle absent - live assertion skipped" -ForegroundColor Yellow
} else {
    if ($live.Category.ToString() -ne "Keyboard") { throw "Category must be Keyboard, got $($live.Category)" }
    if (-not $live.IsDonglePresent) { throw "IsDonglePresent must be true when entry exists" }
    if ($live.IsConnected) {
        if ($live.BatteryPercent -lt 0 -or $live.BatteryPercent -gt 100) { throw "Live percent out of range: $($live.BatteryPercent)" }
        if ($live.IsSleeping) { throw "IsSleeping must be false when connected" }
        Write-Host ("  [PASS] Live connected: " + $live.DeviceName + " " + $live.BatteryPercent + "%") -ForegroundColor Green
    } else {
        # 休眠态: 必须标记 IsSleeping，百分比为缓存值(可为 -1 表示从未读到)
        if (-not $live.IsSleeping) { throw "IsSleeping must be true when dongle present but radio silent" }
        if ($live.BatteryPercent -lt -1 -or $live.BatteryPercent -gt 100) { throw "Cached percent out of range: $($live.BatteryPercent)" }
        Write-Host ("  [PASS] Live sleeping: entry retained, cached " + $live.BatteryPercent + "%") -ForegroundColor Green
    }
}

Write-Host "[ALL PASSED] CompX 2.4G Wireless Keyboard (05AC:024F) test suite succeeded!" -ForegroundColor Green
exit 0
