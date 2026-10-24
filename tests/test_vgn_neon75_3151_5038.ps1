# ============================================================================
# 测试脚本: test_vgn_neon75_3151_5038.ps1
# 核心职责:
#   验证 VGN Neon75 2.4G Dongle (3151:5038) 的 0xF7 状态解析、payload[9]
#   充电位、100% 电量、有线 PID 隔离，以及 Valkyrie 误识别隔离。
# 运行环境: PowerShell 3.0+ / Windows
# ============================================================================

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
Set-Location $repoRoot

$asmPath = Join-Path $repoRoot "FerrisPulse.exe"
if (-not (Test-Path $asmPath)) {
    Write-Host "FAIL: FerrisPulse.exe not found at $asmPath" -ForegroundColor Red
    exit 1
}

$bytes = [System.IO.File]::ReadAllBytes($asmPath)
$asm = [System.Reflection.Assembly]::Load($bytes)

# 1. 源码存在、UTF-8 BOM 与 C# 5.0 语法铁律
$helperPath = Join-Path $repoRoot "src\Hardware\VgnKeyboardHelper.cs"
if (-not (Test-Path $helperPath)) {
    Write-Host "FAIL: VgnKeyboardHelper.cs missing!" -ForegroundColor Red
    exit 1
}
$srcBytes = [System.IO.File]::ReadAllBytes($helperPath)
if ($srcBytes.Length -lt 3 -or $srcBytes[0] -ne 0xEF -or $srcBytes[1] -ne 0xBB -or $srcBytes[2] -ne 0xBF) {
    Write-Host "FAIL: VgnKeyboardHelper.cs is not UTF-8 with BOM!" -ForegroundColor Red
    exit 1
}
$src = [System.IO.File]::ReadAllText($helperPath, [System.Text.Encoding]::UTF8)
if ([regex]::IsMatch($src, '\$".*?"')) {
    Write-Host "FAIL: string interpolation is forbidden in C# 5.0!" -ForegroundColor Red
    exit 1
}
if ([regex]::IsMatch($src, '\w+\?\.')) {
    Write-Host "FAIL: null-conditional operator is forbidden in C# 5.0!" -ForegroundColor Red
    exit 1
}
if ([regex]::IsMatch($src, 'nameof\s*\(')) {
    Write-Host "FAIL: nameof is forbidden in C# 5.0!" -ForegroundColor Red
    exit 1
}
if (-not $src.Contains("⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️")) {
    Write-Host "FAIL: AI maintenance directive header missing!" -ForegroundColor Red
    exit 1
}
Write-Host "PASS 1: VgnKeyboardHelper.cs exists, BOM is valid, C# 5.0 syntax gate passed." -ForegroundColor Green

# 2. 0xF7 查询帧构造与 Bit7 校验和
$packet = [RazerBatteryTray.VgnKeyboardHelper]::BuildStatusPacket()
if ($packet.Length -ne 65 -or $packet[0] -ne 0x00 -or $packet[1] -ne 0xF7 -or $packet[8] -ne 0x08) {
    Write-Host ("FAIL: status packet mismatch: len=" + $packet.Length + " [0]=" + $packet[0] + " [1]=" + $packet[1] + " [8]=" + $packet[8]) -ForegroundColor Red
    exit 1
}
Write-Host "PASS 2: 65-byte 0xF7 status packet and Bit7 checksum verified." -ForegroundColor Green

# 3. 实机样本：Report ID 0x00 + payload (100%, 在线, 键盘, 未充电)
$rx = New-Object byte[] 65
$rx[0] = 0x00
$rx[1] = 0x00
$rx[2] = 0x64
$rx[3] = 0x00
$rx[4] = 0x00
$rx[5] = 0x01
$rx[6] = 0x01
$rx[7] = 0x01
$rx[8] = 0x00
$rx[9] = 0x00
$rx[10] = 0x00

$batt = -1
$online = $false
$charging = $true
$hasKeyboard = $false
$kind = 0
$ok = [RazerBatteryTray.VgnKeyboardHelper]::TryParseStatus($rx, [ref]$batt, [ref]$online, [ref]$charging, [ref]$hasKeyboard, [ref]$kind)
if (-not $ok -or $batt -ne 100 -or -not $online -or $charging -or -not $hasKeyboard -or $kind -ne 1) {
    Write-Host ("FAIL: 65-byte sample parse mismatch: ok=" + $ok + " batt=" + $batt + " online=" + $online + " charging=" + $charging + " hasKb=" + $hasKeyboard + " kind=" + $kind) -ForegroundColor Red
    exit 1
}
Write-Host "PASS 3: real 100% sample parsed correctly; payload[9]=0 means not charging." -ForegroundColor Green

# 4. payload[9] 充电位为 1 时必须识别为充电中
$rxChg = New-Object byte[] 65
[Array]::Copy($rx, $rxChg, 65)
$rxChg[10] = 0x01
$battC = -1
$onlineC = $false
$chargingC = $false
$hasKbC = $false
$kindC = 0
$okC = [RazerBatteryTray.VgnKeyboardHelper]::TryParseStatus($rxChg, [ref]$battC, [ref]$onlineC, [ref]$chargingC, [ref]$hasKbC, [ref]$kindC)
if (-not $okC -or -not $chargingC -or $battC -ne 100) {
    Write-Host ("FAIL: charging bit not parsed: ok=" + $okC + " charging=" + $chargingC + " batt=" + $battC) -ForegroundColor Red
    exit 1
}
Write-Host "PASS 4: payload[9]=1 is recognized as charging." -ForegroundColor Green

# 5. 64 字节裸帧兼容（payload[9] 位于 index 9）
$rx64 = New-Object byte[] 64
$rx64[0] = 0x01
$rx64[1] = 0x4B
$rx64[3] = 0x00
$rx64[5] = 0x01
$rx64[6] = 0x01
$rx64[9] = 0x01
$batt64 = -1
$online64 = $false
$chg64 = $false
$hasKb64 = $false
$kind64 = 0
$ok64 = [RazerBatteryTray.VgnKeyboardHelper]::TryParseStatus($rx64, [ref]$batt64, [ref]$online64, [ref]$chg64, [ref]$hasKb64, [ref]$kind64)
if (-not $ok64 -or $batt64 -ne 75 -or -not $online64 -or -not $chg64 -or -not $hasKb64 -or $kind64 -ne 1) {
    Write-Host ("FAIL: 64-byte sample parse mismatch: ok=" + $ok64 + " batt=" + $batt64 + " chg=" + $chg64) -ForegroundColor Red
    exit 1
}
Write-Host "PASS 5: 64-byte raw frame without Report ID parsed correctly." -ForegroundColor Green

# 6. 非法设备类型必须拒绝
$bad = New-Object byte[] 65
$bad[0] = 0x00
$bad[6] = 0x7F
$bad[7] = 0x7F
$badBatt = -1
$badOnline = $false
$badChg = $false
$badHasKb = $false
$badKind = 0
$badOk = [RazerBatteryTray.VgnKeyboardHelper]::TryParseStatus($bad, [ref]$badBatt, [ref]$badOnline, [ref]$badChg, [ref]$badHasKb, [ref]$badKind)
if ($badOk) {
    Write-Host "FAIL: invalid device kind was accepted!" -ForegroundColor Red
    exit 1
}
Write-Host "PASS 6: invalid device kind rejected." -ForegroundColor Green

# 7. 品牌隔离与 DeviceManager 变体池
if (-not [RazerBatteryTray.VgnKeyboardHelper]::IsVgnNeon75("HID:3151:5038")) {
    Write-Host "FAIL: VgnKeyboardHelper did not recognize 3151:5038!" -ForegroundColor Red
    exit 1
}
if (-not [RazerBatteryTray.VgnKeyboardHelper]::IsVgnNeon75("HID:3151:502F")) {
    Write-Host "FAIL: VgnKeyboardHelper did not recognize wired 3151:502F!" -ForegroundColor Red
    exit 1
}
if ([RazerBatteryTray.VgnKeyboardHelper]::IsVgnNeon75("HID:3151:5030")) {
    Write-Host "FAIL: VgnKeyboardHelper over-matched another 3151 PID!" -ForegroundColor Red
    exit 1
}
if ([RazerBatteryTray.ValkyrieDeviceHelper]::IsValkyrieDevice("HID:3151:5038")) {
    Write-Host "FAIL: ValkyrieDeviceHelper still claims 3151:5038!" -ForegroundColor Red
    exit 1
}
if ([RazerBatteryTray.ValkyrieDeviceHelper]::IsValkyrieDevice("HID:3151:502F")) {
    Write-Host "FAIL: ValkyrieDeviceHelper still claims 3151:502F!" -ForegroundColor Red
    exit 1
}
$variants = [RazerBatteryTray.DeviceManager]::GetDeviceIdVariants("HID:3151:5038")
if (-not $variants.Contains("VGN:3151:5038")) {
    Write-Host "FAIL: DeviceManager variant pool missing VGN prefix!" -ForegroundColor Red
    exit 1
}
Write-Host "PASS 7: VGN brand isolation and DeviceManager variant registration verified." -ForegroundColor Green

# 8. 型号映射
if (-not [RazerBatteryTray.VgnKeyboardHelper]::GetModelName(0x5038, 0, $false).Contains("Neon75")) {
    Write-Host "FAIL: dongle model name is not VGN Neon75!" -ForegroundColor Red
    exit 1
}
if (-not [RazerBatteryTray.VgnKeyboardHelper]::GetModelName(0x502F, 2859, $true).Contains("Neon75 Pro")) {
    Write-Host "FAIL: wired model mapping failed!" -ForegroundColor Red
    exit 1
}
Write-Host "PASS 8: VGN Neon75 model mapping verified." -ForegroundColor Green

# 9. 灯光命令帧与回读解析（实机 0x87 样本）
$lightRead = [RazerBatteryTray.VgnKeyboardHelper]::BuildLightReadPacket(0x87)
if ($lightRead.Length -ne 65 -or $lightRead[1] -ne 0x87 -or $lightRead[8] -ne 0x78) {
    Write-Host ("FAIL: light read packet mismatch: len=" + $lightRead.Length + " [1]=" + $lightRead[1] + " [8]=" + $lightRead[8]) -ForegroundColor Red
    exit 1
}
$lightWrite = [RazerBatteryTray.VgnKeyboardHelper]::BuildLightWritePacket(0x07, 1, 4, 3, 7, 255, 0, 0)
if ($lightWrite[1] -ne 0x07 -or $lightWrite[2] -ne 1 -or $lightWrite[3] -ne 4 -or $lightWrite[4] -ne 3 -or $lightWrite[5] -ne 7 -or $lightWrite[6] -ne 255 -or $lightWrite[9] -ne 0xEA) {
    Write-Host ("FAIL: light write packet mismatch: " + (($lightWrite[0..10] | ForEach-Object { '{0:X2}' -f $_ }) -join ' ')) -ForegroundColor Red
    exit 1
}

$lightRx = New-Object byte[] 65
$lightRx[0] = 0x00
$lightRx[1] = 0x87
$lightRx[2] = 0x06
$lightRx[3] = 0x01
$lightRx[4] = 0x03
$lightRx[5] = 0x01
$lightRx[6] = 0xFF
$lightRx[7] = 0xFF
$lightRx[8] = 0xFF
$lm = 0
$lw = 0
$ll = 0
$lf = 0
$lr = 0
$lg = 0
$lb = 0
$lok = [RazerBatteryTray.VgnKeyboardHelper]::TryParseMainLight($lightRx, [ref]$lm, [ref]$lw, [ref]$ll, [ref]$lf, [ref]$lr, [ref]$lg, [ref]$lb)
if (-not $lok -or $lm -ne 6 -or $lw -ne 1 -or $ll -ne 3 -or $lf -ne 1 -or $lr -ne 255 -or $lg -ne 255 -or $lb -ne 255) {
    Write-Host ("FAIL: 0x87 sample parse mismatch: ok=" + $lok + " mode=" + $lm + " speed=" + $lw + " lum=" + $ll + " flags=" + $lf + " rgb=" + $lr + "/" + $lg + "/" + $lb) -ForegroundColor Red
    exit 1
}
Write-Host "PASS 9: 0x87/0x07 light packets and real readback sample verified." -ForegroundColor Green

# 10. 灯光调节窗体已编译存在
if ($null -eq $asm.GetType("RazerBatteryTray.VgnKeyboardForm")) {
    Write-Host "FAIL: VgnKeyboardForm type missing!" -ForegroundColor Red
    exit 1
}
Write-Host "PASS 10: VgnKeyboardForm compiled and available." -ForegroundColor Green

# 11. 状态栏双设备摘要
$list = New-Object 'System.Collections.Generic.List[RazerBatteryTray.MouseBatteryInfo]'
$m = New-Object RazerBatteryTray.MouseBatteryInfo
$m.Category = [RazerBatteryTray.DeviceCategory]::Mouse
$m.IsConnected = $true
$m.BatteryPercent = 51
$m.Transport = "2.4G 无线"
$m.DeviceId = "Test:Mouse"
$list.Add($m)
$k = New-Object RazerBatteryTray.MouseBatteryInfo
$k.Category = [RazerBatteryTray.DeviceCategory]::Keyboard
$k.IsConnected = $true
$k.BatteryPercent = 100
$k.Transport = "2.4G 无线"
$k.DeviceId = "Test:Keyboard"
$list.Add($k)
$summary = [RazerBatteryTray.DeviceManager]::BuildTrayBatterySummary($list)
if ($summary -notmatch '鼠标 51%' -or $summary -notmatch '键盘 100%') {
    Write-Host ("FAIL: dual battery summary mismatch: " + $summary) -ForegroundColor Red
    exit 1
}
$k.Transport = "USB 有线"
$k.IsCharging = $true
$summaryWired = [RazerBatteryTray.DeviceManager]::BuildTrayBatterySummary($list)
if ($summaryWired -notmatch '键盘 供电中') {
    Write-Host ("FAIL: wired keyboard summary mismatch: " + $summaryWired) -ForegroundColor Red
    exit 1
}
Write-Host ("PASS 11: dual battery summary verified: " + $summary + " / " + $summaryWired) -ForegroundColor Green

Write-Host "ALL TESTS PASSED for VGN Neon75 3151:5038 adaptation, lighting and dual battery summary!" -ForegroundColor Green
exit 0
