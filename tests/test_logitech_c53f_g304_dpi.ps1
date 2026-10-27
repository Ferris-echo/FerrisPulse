# tests/test_logitech_c53f_g304_dpi.ps1
# 回归验证：罗技 LIGHTSPEED 接收器 046D:C53F 之后的 G304/G305 鼠标
#   1. HID++ 2.0 Feature 0x2201 Func 2 (getSensorDpi) 应答必须解析出真实 DPI（3200）
#   2. 严禁再把 Func 1 (getSensorDpiList) 的列表首项 200 当作当前 DPI
#   3. 接收器 PID 0xC53F 必须有明确的型号文案，不得回落到通用兜底名称
# 说明：全程全内存反射装载，严禁 Add-Type -Path 造成可执行文件句柄锁死。

$ErrorActionPreference = "Stop"

$asmPath = Join-Path $PSScriptRoot "..\FerrisPulse.exe"
if (-not (Test-Path $asmPath)) { $asmPath = "FerrisPulse.exe" }
$bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $asmPath).Path)
$asm = [System.Reflection.Assembly]::Load($bytes)

$tLogi = $asm.GetType("RazerBatteryTray.LogitechDeviceHelper")
if ($null -eq $tLogi) {
    Write-Host "FAIL: Cannot find RazerBatteryTray.LogitechDeviceHelper" -ForegroundColor Red
    exit 1
}

$mParse = $tLogi.GetMethod("ParseAdjustableDpiReply", [System.Reflection.BindingFlags]"Public,Static")
if ($null -eq $mParse) {
    Write-Host "FAIL: ParseAdjustableDpiReply 不存在（0x2201 应答解析入口缺失）" -ForegroundColor Red
    exit 1
}
$mModel = $tLogi.GetMethod("GetModelName", [System.Reflection.BindingFlags]"Public,Static")

# ---------------------------------------------------------------
# 测试 1：真实抓包帧 -> 3200 DPI
# 实机（046D:C53F 接收器 / devIdx 1 / 0x2201 @ featIdx 0x1A）原始应答：
#   11 01 1A 28 00 0C 80 03 20 00 ...
#   [4]=sensor 0, [5..6]=0x0C80=3200 当前 DPI, [7..8]=0x0320=800 出厂默认
# ---------------------------------------------------------------
$frame = New-Object byte[] 20
$frame[0] = 0x11; $frame[1] = 0x01; $frame[2] = 0x1A; $frame[3] = 0x28
$frame[4] = 0x00; $frame[5] = 0x0C; $frame[6] = 0x80; $frame[7] = 0x03; $frame[8] = 0x20

function Invoke-ParseDpi([byte[]]$rx) {
    $a = New-Object object[] 1
    if ($null -ne $rx) { $a[0] = $rx }
    return [int]$mParse.Invoke($null, $a)
}

$dpi = Invoke-ParseDpi $frame
if ($dpi -ne 3200) {
    Write-Host ("FAIL: 0x2201 getSensorDpi 解析错误，期望 3200，实际 " + $dpi) -ForegroundColor Red
    exit 1
}
Write-Host "PASS: 0x2201 Func 2 当前 DPI 解析正确 (3200)" -ForegroundColor Green

# ---------------------------------------------------------------
# 测试 2：损坏帧 / 错误帧 / 越界帧 必须返回 -1（禁止伪造电量式假数据）
# ---------------------------------------------------------------
$errFrame = New-Object byte[] 20
$errFrame[0] = 0x11; $errFrame[1] = 0x01; $errFrame[2] = 0xFF; $errFrame[3] = 0x28
if ((Invoke-ParseDpi $errFrame) -ne -1) {
    Write-Host "FAIL: HID++ 错误帧未被拒绝" -ForegroundColor Red
    exit 1
}

$zeroFrame = New-Object byte[] 20
$zeroFrame[0] = 0x11; $zeroFrame[1] = 0x01; $zeroFrame[2] = 0x1A; $zeroFrame[3] = 0x28
if ((Invoke-ParseDpi $zeroFrame) -ne -1) {
    Write-Host "FAIL: DPI=0 的越界帧未被拒绝" -ForegroundColor Red
    exit 1
}

if ((Invoke-ParseDpi $null) -ne -1) {
    Write-Host "FAIL: 空应答未被拒绝" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: 错误帧 / 越界帧 / 空帧 一律返回 -1" -ForegroundColor Green

# ---------------------------------------------------------------
# 测试 3：接收器 0xC53F 与无线上位型号文案
# ---------------------------------------------------------------
$recvName = [string]$mModel.Invoke($null, @([int]0xC53F, [string]"USB Receiver"))
if ($recvName -notmatch "0xC53F") {
    Write-Host ("FAIL: 0xC53F 接收器文案未命中，实际: " + $recvName) -ForegroundColor Red
    exit 1
}
$g304Name = [string]$mModel.Invoke($null, @([int]0x4074, [string]"USB Receiver"))
if ($g304Name -notmatch "G304") {
    Write-Host ("FAIL: 0x4074 型号文案未命中，实际: " + $g304Name) -ForegroundColor Red
    exit 1
}
Write-Host "PASS: 0xC53F 接收器 / 0x4074 G304 型号文案齐备" -ForegroundColor Green

Write-Host "ALL PASS: G304 (046D:C53F) 适配回归通过" -ForegroundColor Green
exit 0
