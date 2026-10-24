# ============================================================================
# 测试脚本: test_rapoo_nonblocking_and_permissions.ps1
# 核心职责:
#   验证雷柏 (Rapoo) 设备驱动层的 EEPROM 写入静默期、非阻塞直连与极速档位探测机制，
#   以及 Report ID 0x07 (col09) 19 字节遥测报文解析与电量实时同步 (51% 电量真实修复断言)
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
[System.Reflection.Assembly]::Load($bytes) | Out-Null

# 1. 验证新增的非阻塞防御与遥测方法存在
$tRapoo = [RazerBatteryTray.RapooDeviceHelper]
if ($null -eq $tRapoo) {
    Write-Host "FAIL: Type RazerBatteryTray.RapooDeviceHelper not found!" -ForegroundColor Red
    exit 1
}

Write-Host "PASS 1: RapooDeviceHelper anti-freeze guard APIs confirmed." -ForegroundColor Green

# 2. 验证静默避让期机制
[RazerBatteryTray.RapooDeviceHelper]::SetEepromQuietPeriod(1000)
$isQuiet = [RazerBatteryTray.RapooDeviceHelper]::IsEepromQuietActive()
if (-not $isQuiet) {
    Write-Host "FAIL: IsEepromQuietActive was false immediately after SetEepromQuietPeriod!" -ForegroundColor Red
    exit 1
}

# 验证处于静默期时，FastQueryDpi 自动短路避让返回 False，杜绝撞击硬件 EEPROM
$dpiX = 0
$dpiY = 0
$stage = 0
$stageCount = 0
$fastRes = [RazerBatteryTray.RapooDeviceHelper]::FastQueryDpi([ref]$dpiX, [ref]$dpiY, [ref]$stage, [ref]$stageCount)
if ($fastRes) {
    Write-Host "FAIL: FastQueryDpi returned true while IsEepromQuietActive is true!" -ForegroundColor Red
    exit 1
}
Write-Host "PASS 2: FastQueryDpi cleanly short-circuits during EEPROM quiet period." -ForegroundColor Green

# 等待静默期结束
Start-Sleep -Milliseconds 1100
$isQuietAfter = [RazerBatteryTray.RapooDeviceHelper]::IsEepromQuietActive()
if ($isQuietAfter) {
    Write-Host "FAIL: IsEepromQuietActive did not expire after sleep!" -ForegroundColor Red
    exit 1
}
Write-Host "PASS 3: EEPROM quiet period correctly expires." -ForegroundColor Green

# 3. 验证 CloseRapooStream 幂等安全执行
[RazerBatteryTray.RapooDeviceHelper]::CloseRapooStream()
[RazerBatteryTray.RapooDeviceHelper]::CloseRapooStream()
Write-Host "PASS 4: CloseRapooStream executes idempotently without errors." -ForegroundColor Green

# 4. 验证 Report ID 0x07 (col09) 19 字节遥测报文解析与精准电量同步 (51% 电量真实修复断言)
$mockReport = New-Object byte[] 19
$mockReport[0] = 0x07  # Report ID
$mockReport[1] = 0x10  # Valid connected USB transport flags
$mockReport[2] = 0x02  # Stage index 2 -> Stage 3
$mockReport[3] = 0x40  # DPI X low byte (1600 = 0x0640)
$mockReport[4] = 0x06  # DPI X high byte
$mockReport[5] = 0x40  # DPI Y low byte
$mockReport[6] = 0x06  # DPI Y high byte
$mockReport[7] = 0x02  # IsCharging = true (0x02)
$mockReport[8] = 0x33  # BatteryPercent = 51% (0x33)
$mockReport[17] = 0x05 # Stage count = 5

$parsed = [RazerBatteryTray.RapooDeviceHelper]::ProcessTelemetryReport($mockReport)
if (-not $parsed) {
    Write-Host "FAIL: ProcessTelemetryReport returned false for valid Report 0x07!" -ForegroundColor Red
    exit 1
}

$batt = [RazerBatteryTray.RapooDeviceHelper]::CachedBatteryPercent
$charging = [RazerBatteryTray.RapooDeviceHelper]::CachedIsCharging
$dpi = [RazerBatteryTray.RapooDeviceHelper]::CachedDpi
$stage = [RazerBatteryTray.RapooDeviceHelper]::CachedDpiStage

if ($batt -ne 51 -or $charging -ne $true -or $dpi -ne 1600 -or $stage -ne 3) {
    Write-Host ("FAIL: Telemetry fields mismatch: Battery=" + $batt + " (expected 51), Charging=" + $charging + ", DPI=" + $dpi + ", Stage=" + $stage) -ForegroundColor Red
    exit 1
}
Write-Host "PASS 5: Report 0x07 parsed successfully, 51% battery and charging state verified!" -ForegroundColor Green

# Both public entry points must retain strict transport validation and real 0%.
$mockReport[1] = 0
if ([RazerBatteryTray.RapooDeviceHelper]::ProcessTelemetryReport($mockReport)) { throw 'Invalid transport accepted by compatibility API' }
$mockReport[1] = 0x10
$mockReport[8] = 0
if (-not [RazerBatteryTray.RapooDeviceHelper]::ProcessTelemetryReport($mockReport) -or [RazerBatteryTray.RapooDeviceHelper]::CachedBatteryPercent -ne 0) { throw 'Real zero battery lost by compatibility API' }

Write-Host "ALL TESTS PASSED for Rapoo Non-blocking, Report 0x07 Telemetry, and Simplified Logic!" -ForegroundColor Green
exit 0
