# tests\test_razer_v3pro_se_identification.ps1
# 验证：Razer Viper V3 Pro SE (0x00DE Wired / 0x00DF Dongle) 硬件识别与归一化
$ErrorActionPreference = "Stop"

$asmPath = Join-Path $PSScriptRoot "..\FerrisPulse.exe"
if (-not (Test-Path $asmPath)) { $asmPath = "FerrisPulse.exe" }
$fullPath = (Resolve-Path $asmPath).Path
# 铁律：严禁用 LoadFile 加载被测 exe —— 会建立文件锁，让后续 build.bat 报 CS0016。
# 读成字节后全内存反射，进程退出即释放，不碰磁盘句柄。
$asmBytes = [System.IO.File]::ReadAllBytes($fullPath)
$asm = [System.Reflection.Assembly]::Load($asmBytes)

$tRazer = $asm.GetType("RazerBatteryTray.RazerDeviceHelper")
if ($tRazer -eq $null) {
    Write-Host "FAIL: Type RazerBatteryTray.RazerDeviceHelper not found" -ForegroundColor Red
    exit 1
}

# 1. 验证 NormalizeRazerPid：0x00DF (Dongle) 必须归一化为 0x00DE (Wired)
$mNorm = ($tRazer.GetMethods([System.Reflection.BindingFlags]"Public,Static") | Where-Object { $_.Name -eq "NormalizeRazerPid" })[0]
if ($mNorm -eq $null) {
    Write-Host "FAIL: Method RazerDeviceHelper.NormalizeRazerPid not found" -ForegroundColor Red
    exit 1
}
$normDe = [int]$mNorm.Invoke($null, @(0x00DE))
$normDf = [int]$mNorm.Invoke($null, @(0x00DF))

if ($normDe -ne 0x00DE) {
    Write-Host "FAIL: NormalizeRazerPid(0x00DE) expected 0x00DE, got $normDe" -ForegroundColor Red
    exit 1
}
if ($normDf -ne 0x00DE) {
    Write-Host "FAIL: NormalizeRazerPid(0x00DF) expected 0x00DE, got $normDf" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: NormalizeRazerPid correctly maps 0x00DF -> 0x00DE." -ForegroundColor Green

# 2. 验证 DetermineIsDongle：0x00DF 必须被判定为无线接收器
$mDongle = ($tRazer.GetMethods([System.Reflection.BindingFlags]"Public,Static") | Where-Object { $_.Name -eq "DetermineIsDongle" })[0]
if ($mDongle -eq $null) {
    Write-Host "FAIL: Method RazerDeviceHelper.DetermineIsDongle not found" -ForegroundColor Red
    exit 1
}
$isDongleDf = [bool]$mDongle.Invoke($null, @(0x00DF, "", 0, $false))
if (-not $isDongleDf) {
    Write-Host "FAIL: DetermineIsDongle(0x00DF) expected true, got false" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: DetermineIsDongle correctly identifies 0x00DF as Dongle." -ForegroundColor Green

# 3. 验证官方名称匹配：GetRazerOfficialModelName
$mName = ($tRazer.GetMethods([System.Reflection.BindingFlags]"Public,Static") | Where-Object { $_.Name -eq "GetRazerOfficialModelName" })[0]
if ($mName -eq $null) {
    Write-Host "FAIL: Method RazerDeviceHelper.GetRazerOfficialModelName not found" -ForegroundColor Red
    exit 1
}
$nameDe = [string]$mName.Invoke($null, @(0x00DE, ""))
$nameDf = [string]$mName.Invoke($null, @(0x00DF, ""))

if ($nameDe -notlike "*Viper V3 Pro SE*") {
    Write-Host "FAIL: GetRazerOfficialModelName(0x00DE) expected Viper V3 Pro SE, got: '$nameDe'" -ForegroundColor Red
    exit 1
}
if ($nameDf -notlike "*Viper V3 Pro SE*") {
    Write-Host "FAIL: GetRazerOfficialModelName(0x00DF) expected Viper V3 Pro SE, got: '$nameDf'" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: GetRazerOfficialModelName correctly resolves Viper V3 Pro SE for 0x00DE & 0x00DF." -ForegroundColor Green

# 4. 验证 Focus Pro 35K 传感器能力匹配
$mDpi = ($tRazer.GetMethods([System.Reflection.BindingFlags]"Public,Static") | Where-Object { $_.Name -eq "GetDpiCapability" })[0]
if ($mDpi -ne $null) {
    $capDe = $mDpi.Invoke($null, @($nameDe, 0x00DE))
    $capDf = $mDpi.Invoke($null, @($nameDf, 0x00DF))
    if ($capDe.MaxDpi -ne 35000 -or $capDf.MaxDpi -ne 35000) {
        Write-Host "FAIL: DPI capability expected 35000, got: $($capDe.MaxDpi)" -ForegroundColor Red
        exit 1
    }
    Write-Host "PASS: DPI capability correctly recognized Focus Pro 35K (35000 DPI)." -ForegroundColor Green
}

Write-Host "ALL TESTS PASSED for test_razer_v3pro_se_identification.ps1" -ForegroundColor Green
exit 0
