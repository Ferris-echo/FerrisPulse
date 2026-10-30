# ============================================================================
# 测试脚本: test_endgamegear_xm2we.ps1
# 目标: 验证 Endgame Gear XM2we (3367:1961) 双端点 Beken 电量协议：
#       1. 电量查询帧构造与 0x55 校验和
#       2. 实机抓包应答帧回放 (09 04 00 00 00 02 41 00 ... 05 -> 65%, 未充电)
#       3. 充电 / 毫伏电压帧解析
#       4. 坏校验和 / 错误 Report ID / 截断帧必须被拒绝
#       5. 与通用 UniversalGaming 引擎的端点隔离，及 EGG 设备 ID 变体映射
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = Resolve-Path "$ScriptDir\.."
Set-Location $ProjectDir

Write-Host "[TEST] Validating Endgame Gear XM2we (3367:1961) dual-endpoint protocol..." -ForegroundColor Cyan

$exePath = "$ProjectDir\FerrisPulse.exe"
if (-not (Test-Path $exePath)) {
    throw "FerrisPulse.exe not found!"
}

# 铁律 10：严禁 Add-Type -Path，必须全内存反射加载，避免文件句柄锁死后续编译。
$asmBytes = [System.IO.File]::ReadAllBytes($exePath)
$asm = [System.Reflection.Assembly]::Load($asmBytes)

$eggType = $asm.GetType("RazerBatteryTray.EndgameGearDeviceHelper")
if ($null -eq $eggType) {
    throw "EndgameGearDeviceHelper type not found!"
}

$flags = [System.Reflection.BindingFlags]"Public,Static"

# --- 1. 查询帧构造 ---
$buildMethod = $eggType.GetMethod("BuildBatteryQuery", $flags)
[byte[]]$tx = $buildMethod.Invoke($null, @())
[string]$txHex = ($tx | ForEach-Object { $_.ToString("X2") }) -join " "
if ($tx.Length -ne 17 -or $txHex -ne "08 04 00 00 00 00 00 00 00 00 00 00 00 00 00 00 49") {
    throw "BuildBatteryQuery mismatch: len=$($tx.Length) hex=$txHex"
}
Write-Host "  [PASS] Query frame = 08 04 00 ... 49 (17B)" -ForegroundColor Green

# --- 2. 校验和算法 ---
$crcMethod = $eggType.GetMethod("ComputeChecksum", $flags)
[byte[]]$payload = @(0x04, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0)
[byte]$crc = $crcMethod.Invoke($null, @([byte]0x08, $payload, 0, 15))
if ($crc -ne 0x49) {
    throw "ComputeChecksum(0x08, 04 00 ...) expected 0x49, got 0x$($crc.ToString('X2'))"
}
Write-Host "  [PASS] Beken 0x55 checksum = 0x49" -ForegroundColor Green

$parseMethod = $eggType.GetMethod("ParseBatteryFrame", $flags)

# --- 3. 实机抓包回放（本机 XM2we 65% 未充电） ---
[byte[]]$rxReal = @(0x09, 0x04, 0x00, 0x00, 0x00, 0x02, 0x41, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x05)
[object[]]$args = @($rxReal, 17, 0, $false, 0)
$ok = $parseMethod.Invoke($null, $args)
$batt = [int]$args[2]
$chg = [bool]$args[3]
$mv = [int]$args[4]
if (-not $ok -or $batt -ne 65 -or $chg -ne $false -or $mv -ne 0) {
    throw "Real hardware frame parse failed: ok=$ok batt=$batt chg=$chg mv=$mv"
}
Write-Host "  [PASS] Real RX '09 04 00 00 00 02 41 00 ... 05' -> 65%, not charging" -ForegroundColor Green

# --- 4. 充电帧 + 毫伏电压 ---
[byte[]]$rxCharging = New-Object byte[] 17
$rxCharging[0] = 0x09
$rxCharging[1] = 0x04
$rxCharging[5] = 0x04
$rxCharging[6] = 0x41
$rxCharging[7] = 0x01
$rxCharging[8] = 0x0D
$rxCharging[9] = 0x48
[byte]$chgCrc = $crcMethod.Invoke($null, @([byte]0x09, $rxCharging, 1, 15))
$rxCharging[16] = $chgCrc

[object[]]$args2 = @($rxCharging, 17, 0, $false, 0)
$ok2 = $parseMethod.Invoke($null, $args2)
$batt2 = [int]$args2[2]
$chg2 = [bool]$args2[3]
$mv2 = [int]$args2[4]
if (-not $ok2 -or $batt2 -ne 65 -or $chg2 -ne $true -or $mv2 -ne 3400) {
    throw "Charging frame parse failed: ok=$ok2 batt=$batt2 chg=$chg2 mv=$mv2"
}
Write-Host "  [PASS] Charging frame -> 65%, charging=true, 3400mV" -ForegroundColor Green

# --- 5. 异常帧必须拒绝 ---
[byte[]]$rxBadCrc = [byte[]]$rxReal.Clone()
$rxBadCrc[16] = 0x00
[object[]]$a3 = @($rxBadCrc, 17, 0, $false, 0)
if ($parseMethod.Invoke($null, $a3)) { throw "Bad checksum frame must be rejected!" }

[byte[]]$rxBadId = [byte[]]$rxReal.Clone()
$rxBadId[0] = 0x08
[byte[]]$p4 = @($rxBadId[1], $rxBadId[2], $rxBadId[3], $rxBadId[4], $rxBadId[5], $rxBadId[6], $rxBadId[7], $rxBadId[8], $rxBadId[9], $rxBadId[10], $rxBadId[11], $rxBadId[12], $rxBadId[13], $rxBadId[14], $rxBadId[15])
$rxBadId[16] = $crcMethod.Invoke($null, @([byte]0x08, $p4, 0, 15))
[object[]]$a5 = @($rxBadId, 17, 0, $false, 0)
if ($parseMethod.Invoke($null, $a5)) { throw "Wrong report id frame must be rejected!" }

if ($parseMethod.Invoke($null, @($rxReal, 6, 0, $false, 0))) { throw "Truncated frame must be rejected!" }
Write-Host "  [PASS] Bad checksum / wrong report id / truncated frame rejected" -ForegroundColor Green

# --- 6. 型号映射 ---
$modelMethod = $eggType.GetMethod("ResolveModelName", $flags)
$pidMethod = $eggType.GetMethod("IsSupportedPid", $flags)
if ($modelMethod.Invoke($null, @(0x1961)) -ne "XM2we Wireless") { throw "0x1961 model mapping wrong" }
if ($modelMethod.Invoke($null, @(0x1960)) -ne "OP1we Wireless") { throw "0x1960 model mapping wrong" }
if (-not $pidMethod.Invoke($null, @(0x1961))) { throw "0x1961 should be supported" }
if ($pidMethod.Invoke($null, @(0x1962))) { throw "0x1962 (8K family) must stay unclaimed" }
Write-Host "  [PASS] PID mapping: 1961=XM2we, 1960=OP1we, 1962 unclaimed" -ForegroundColor Green

# --- 7. 与通用引擎隔离 ---
$ugType = $asm.GetType("RazerBatteryTray.UniversalGamingDeviceHelper")
$isSupported = $ugType.GetMethod("IsSupportedGamingDevice", $flags)
if ($isSupported.Invoke($null, @(0x3367, "", ""))) {
    throw "UniversalGamingDeviceHelper must NOT claim Endgame Gear (3367) any more!"
}
Write-Host "  [PASS] UniversalGaming engine no longer claims 0x3367 (dual-endpoint isolation)" -ForegroundColor Green

# --- 8. 设备 ID 变体映射 ---
$dmType = $asm.GetType("RazerBatteryTray.DeviceManager")
$variants = $dmType.GetMethod("GetDeviceIdVariants", $flags).Invoke($null, @("EGG:3367:1961"))
if (-not ($variants -contains "HID:3367:1961")) {
    throw "GetDeviceIdVariants missing HID:3367:1961 for EGG device id"
}
Write-Host "  [PASS] DeviceId variants include HID:3367:1961" -ForegroundColor Green

# --- 9. 内置驱动注册 ---
$driverType = $asm.GetType("RazerBatteryTray.EndgameGearDriver")
if ($null -eq $driverType) { throw "EndgameGearDriver type not found!" }
$driver = [System.Activator]::CreateInstance($driverType)
if ($driver.DriverId -ne "BuiltIn:EndgameGear") { throw "EndgameGearDriver DriverId mismatch: $($driver.DriverId)" }
Write-Host "  [PASS] BuiltIn:EndgameGear driver registered class present" -ForegroundColor Green

Write-Host "[ALL PASSED] Endgame Gear XM2we (3367:1961) test suite succeeded!" -ForegroundColor Green
exit 0
