# ============================================================================
# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# 1. C# 5.0 / .NET 4.0，零外部依赖
# 2. 全内存反射加载 FerrisPulse.exe（严禁 LoadFrom / LoadFile / Add-Type -Path）
# ============================================================================
$ErrorActionPreference = "Stop"
$rootDir = Split-Path -Parent $PSScriptRoot
Set-Location $rootDir

$script:pass = 0
$script:fail = 0
function Chk([bool]$cond, [string]$msg) {
    if ($cond) {
        $script:pass++
        Write-Host ("  PASS: " + $msg) -ForegroundColor Green
    } else {
        $script:fail++
        Write-Host ("  FAIL: " + $msg) -ForegroundColor Red
    }
}

Write-Host "=== [1/5] 编码 BOM 与 C# 5.0 规范检查 ===" -ForegroundColor Cyan
$helperPath = Join-Path $rootDir "src\Hardware\VxeNearLinkDeviceHelper.cs"
Chk (Test-Path $helperPath) "VxeNearLinkDeviceHelper.cs exists"
$srcBytes = [System.IO.File]::ReadAllBytes($helperPath)
Chk ($srcBytes.Length -ge 3 -and $srcBytes[0] -eq 0xEF -and $srcBytes[1] -eq 0xBB -and $srcBytes[2] -eq 0xBF) "UTF-8 with BOM"
$srcText = [System.Text.Encoding]::UTF8.GetString($srcBytes)
Chk (-not $srcText.Contains([char]36 + [char]34)) "no C# 6 string interpolation"
Chk (-not $srcText.Contains("?.")) "no C# 6 null-conditional operator"
Chk (-not $srcText.Contains("nameof(")) "no C# 6 nameof"

Write-Host "=== [2/5] 全内存反射加载与类型检查 ===" -ForegroundColor Cyan
$exePath = Join-Path $rootDir "FerrisPulse.exe"
Chk (Test-Path $exePath) "FerrisPulse.exe exists"
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath))
$tVxe = $asm.GetType("RazerBatteryTray.VxeNearLinkDeviceHelper")
$tDesc = $asm.GetType("RazerBatteryTray.HidDeviceDescriptor")
$tDrv = $asm.GetType("RazerBatteryTray.VxeNearLinkDriver")
$tUniv = $asm.GetType("RazerBatteryTray.UniversalGamingDeviceHelper")
$tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo")
Chk ($tVxe -ne $null -and $tDrv -ne $null -and $tUniv -ne $null) "reflection types resolved"

$BF = [System.Reflection.BindingFlags]"Public,Static"

Write-Host "=== [3/5] VID/PID 隔离与四元组端点匹配 ===" -ForegroundColor Cyan
$mVidPid = $tVxe.GetMethod("IsSupportedVidPid", $BF)
Chk ([bool]$mVidPid.Invoke($null, @([int]0x373B, [int]0x1216))) "accepts 373B:1216 (VXE R1 NearLink)"
Chk (-not [bool]$mVidPid.Invoke($null, @([int]0x373B, [int]0x1085))) "rejects 373B:1085 (VXE R1 SE)"
Chk (-not [bool]$mVidPid.Invoke($null, @([int]0x373B, [int]0x111F))) "rejects 373B:111F (ATK A87 PRO)"

$mEp = $tVxe.GetMethod("IsNearLinkEndpoint", $BF)
$dOk = [Activator]::CreateInstance($tDesc)
$dOk.VendorId = 0x373B; $dOk.ProductId = 0x1216; $dOk.UsagePage = 0xFF05; $dOk.Usage = 0x0001
$dOk.InputReportLength = 64; $dOk.OutputReportLength = 64
$dOk.DevicePath = "\\?\hid#vid_373b&pid_1216&mi_02&col04#test"
Chk ([bool]$mEp.Invoke($null, @($dOk))) "matches 0xFF05/0x0001 64B NearLink endpoint"

$dWrongLen = [Activator]::CreateInstance($tDesc)
$dWrongLen.VendorId = 0x373B; $dWrongLen.ProductId = 0x1216; $dWrongLen.UsagePage = 0xFF05; $dWrongLen.Usage = 0x0001
$dWrongLen.InputReportLength = 17; $dWrongLen.OutputReportLength = 17
$dWrongLen.DevicePath = "\\?\hid#vid_373b&pid_1216&mi_02#test"
Chk (-not [bool]$mEp.Invoke($null, @($dWrongLen))) "rejects mismatched 17B report length"

$dKbd = [Activator]::CreateInstance($tDesc)
$dKbd.VendorId = 0x373B; $dKbd.ProductId = 0x1216; $dKbd.UsagePage = 0xFF05; $dKbd.Usage = 0x0001
$dKbd.InputReportLength = 64; $dKbd.OutputReportLength = 64
$dKbd.DevicePath = "\\?\hid#vid_373b&pid_1216&mi_00\kbd"
Chk (-not [bool]$mEp.Invoke($null, @($dKbd))) "rejects \kbd collection"

$mUniv = $tUniv.GetMethod("IsSupportedGamingDevice", $BF)
Chk (-not [bool]$mUniv.Invoke($null, @([int]0x373B, "\\?\hid#vid_373b&pid_1216&mi_02#test", "VXE R1 NearLink"))) "UniversalGamingDeviceHelper excludes 373B:1216"

Write-Host "=== [4/5] BITMOUSE 64B 构包与协议帧回放解析 ===" -ForegroundColor Cyan
$mBuild = $tVxe.GetMethod("BuildReport64", $BF)
$tx7 = [byte[]]$mBuild.Invoke($null, @([int]7, [int]1, [int]0x36))
Chk ($tx7.Length -eq 64) "BuildReport64 returns 64 bytes"
Chk ($tx7[0] -eq 0x08 -and $tx7[2] -eq 0x72 -and $tx7[3] -eq 2 -and $tx7[4] -eq 0x36 -and $tx7[5] -eq 1 -and $tx7[6] -eq 7 -and $tx7[7] -eq 1) "TX frame header/cmd fields exact"
$expectedSum = (0x72 + 2 + 0x36 + 1 + 7 + 1) -band 0xFF
Chk ($tx7[1] -eq $expectedSum) ("TX checksum byte equals sum([2..63])&0xFF (got " + $tx7[1] + ")")

$mParseBat = $tVxe.GetMethod("ParseBatteryResponse", $BF)
[byte[]]$rxBat = New-Object byte[] 64
$rxBat[1] = 0x72; $rxBat[5] = 0x07; $rxBat[6] = 4; $rxBat[7] = 100; $rxBat[8] = 0x68; $rxBat[9] = 0x10 # 4200 mV (0x1068)
$batArgs = New-Object object[] 4
$batArgs[0] = [byte[]]$rxBat; $batArgs[1] = [int]64; $batArgs[2] = [int]0; $batArgs[3] = [int]0
$batOk = [bool]$mParseBat.Invoke($null, $batArgs)
Chk ($batOk -and $batArgs[2] -eq 100 -and $batArgs[3] -eq 4200) "ParseBatteryResponse decodes 100% and 4200mV LE"

[byte[]]$rxZero = New-Object byte[] 64
$rxZero[1] = 0x72; $rxZero[5] = 0x07; $rxZero[7] = 0; $rxZero[8] = 0x84; $rxZero[9] = 0x0C
$zeroArgs = New-Object object[] 4
$zeroArgs[0] = [byte[]]$rxZero; $zeroArgs[1] = [int]64; $zeroArgs[2] = [int]0; $zeroArgs[3] = [int]0
Chk ([bool]$mParseBat.Invoke($null, $zeroArgs) -and $zeroArgs[2] -eq 0) "ParseBatteryResponse accepts honest 0%"

[byte[]]$rxBad = New-Object byte[] 64
$rxBad[1] = 0x72; $rxBad[5] = 0x07; $rxBad[7] = 101
$badArgs = New-Object object[] 4
$badArgs[0] = [byte[]]$rxBad; $badArgs[1] = [int]64; $badArgs[2] = [int]0; $badArgs[3] = [int]0
Chk (-not [bool]$mParseBat.Invoke($null, $badArgs)) "ParseBatteryResponse rejects >100%"

$mParseChg = $tVxe.GetMethod("ParseChargingResponse", $BF)
[byte[]]$rxChg = New-Object byte[] 64
$rxChg[1] = 0x72; $rxChg[5] = 0x0F; $rxChg[7] = 1
$chgArgs = New-Object object[] 3
$chgArgs[0] = [byte[]]$rxChg; $chgArgs[1] = [int]64; $chgArgs[2] = $false
Chk ([bool]$mParseChg.Invoke($null, $chgArgs) -and [bool]$chgArgs[2]) "ParseChargingResponse decodes charging=true"

[byte[]]$rxChgOff = New-Object byte[] 64
$rxChgOff[1] = 0x72; $rxChgOff[5] = 0x07; $rxChgOff[7] = 1
$chgWrongCmd = New-Object object[] 3
$chgWrongCmd[0] = [byte[]]$rxChgOff; $chgWrongCmd[1] = [int]64; $chgWrongCmd[2] = $false
Chk (-not [bool]$mParseChg.Invoke($null, $chgWrongCmd)) "ParseChargingResponse rejects mismatched cmd"

Write-Host "=== [5/5] 驱动能力契约（物理事实：只读电量，禁用未验证 DPI/回报率写入） ===" -ForegroundColor Cyan
$drv = [Activator]::CreateInstance($tDrv)
$devInfo = [Activator]::CreateInstance($tInfo)
$devInfo.DeviceId = "VXE:373B:1216"
Chk ([bool]$drv.CanHandle($devInfo)) "VxeNearLinkDriver.CanHandle accepts VXE:373B:1216"
$drv.PopulateCapabilities($devInfo)
Chk ($devInfo.Capabilities.SupportsBattery -and -not $devInfo.Capabilities.SupportsDpi -and -not $devInfo.Capabilities.SupportsPollingRate) "SupportsBattery=true, SupportsDpi=false, SupportsPollingRate=false"

Write-Host ("`nRESULT: pass=" + $script:pass + " fail=" + $script:fail)
if ($script:fail -gt 0) { exit 1 }
Write-Host "[ALL PASSED] VXE R1 星闪版 (373B:1216) BITMOUSE 协议与隔离测试通过！" -ForegroundColor Green
exit 0
