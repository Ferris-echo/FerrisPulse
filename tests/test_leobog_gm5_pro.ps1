# ============================================================================
# 测试脚本: test_leobog_gm5_pro.ps1
# 核心职责:
#   验证 LEOBOG GM5 PRO (04F3:026F) 接收器状态帧回放：在线位、电量字节、
#   关机/开机实测样本、越界帧拒绝，以及 DeviceManager 注册与历史分类自愈。
# 运行环境: PowerShell 3.0+ / Windows（零硬件依赖，纯内存反射）
# ============================================================================

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
$asmPath = Join-Path $repoRoot "FerrisPulse.exe"
if (-not (Test-Path $asmPath)) {
    Write-Host "FAIL: FerrisPulse.exe not found at $asmPath" -ForegroundColor Red
    exit 1
}

# 铁律 10：全内存反射装配，避免独占 exe 句柄
$bytes = [System.IO.File]::ReadAllBytes($asmPath)
$asm = [System.Reflection.Assembly]::Load($bytes)

function Assert($condition, $message) {
    if (-not $condition) {
        Write-Host ("FAIL: " + $message) -ForegroundColor Red
        exit 1
    }
}

# 1. 源码规范门禁
$helperPath = Join-Path $repoRoot "src\Hardware\LeobogDeviceHelper.cs"
Assert (Test-Path $helperPath) "LeobogDeviceHelper.cs missing!"
$srcBytes = [System.IO.File]::ReadAllBytes($helperPath)
Assert ($srcBytes.Length -ge 3 -and $srcBytes[0] -eq 0xEF -and $srcBytes[1] -eq 0xBB -and $srcBytes[2] -eq 0xBF) "LeobogDeviceHelper.cs is not UTF-8 with BOM!"
$src = [System.IO.File]::ReadAllText($helperPath, [System.Text.Encoding]::UTF8)
Assert (-not [regex]::IsMatch($src, '\$".*?"')) "string interpolation is forbidden"
Assert (-not [regex]::IsMatch($src, '\w+\?\.')) "null-conditional operator is forbidden"
Assert ($src.Contains("⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️")) "maintenance header missing"
Write-Host "PASS 1: source/BOM/C#5 gate passed."

# 2. 设备与端点门禁
$dev = New-Object RazerBatteryTray.HidDeviceDescriptor
$dev.VendorId = 0x04F3
$dev.ProductId = 0x026F
$dev.ProductString = '2.4G Wireless Receiver'
$dev.UsagePage = 0x000A
$dev.Usage = 0x0000
$dev.InputReportLength = 32
Assert ([RazerBatteryTray.LeobogDeviceHelper]::IsSupportedDevice($dev)) "real receiver must be claimed"
Assert ([RazerBatteryTray.LeobogDeviceHelper]::IsVendorEndpoint($dev)) "0x000A/32B endpoint must be recognized"

$wrongPid = New-Object RazerBatteryTray.HidDeviceDescriptor
$wrongPid.VendorId = 0x04F3
$wrongPid.ProductId = 0x026E
Assert (-not [RazerBatteryTray.LeobogDeviceHelper]::IsSupportedDevice($wrongPid)) "other 04F3 PIDs must stay unclaimed"

$kbdCollection = New-Object RazerBatteryTray.HidDeviceDescriptor
$kbdCollection.UsagePage = 0x0001
$kbdCollection.Usage = 0x0006
$kbdCollection.InputReportLength = 8
Assert (-not [RazerBatteryTray.LeobogDeviceHelper]::IsVendorEndpoint($kbdCollection)) "keyboard collection must not be used"
Write-Host "PASS 2: device/endpoint gate verified."

# 3. 实机状态帧回放
$onlineFrame = New-Object byte[] 32
[byte[]]$h1 = @(4, 1, 1, 1, 0x5A, 0, 0, 0, 1, 0x60)
[Array]::Copy($h1, $onlineFrame, $h1.Length)
$online = $false
$battery = -1
Assert ([RazerBatteryTray.LeobogDeviceHelper]::TryParseStatus($onlineFrame, [ref]$online, [ref]$battery)) "online frame must parse"
Assert ($online -and $battery -eq 90) "online frame must report online + 90%"

$offFrame = New-Object byte[] 32
[byte[]]$h2 = @(4, 0, 1, 1, 0x5A, 0, 0, 0, 1, 0x60)
[Array]::Copy($h2, $offFrame, $h2.Length)
$online2 = $true
$battery2 = -1
Assert ([RazerBatteryTray.LeobogDeviceHelper]::TryParseStatus($offFrame, [ref]$online2, [ref]$battery2)) "offline frame must parse"
Assert ((-not $online2) -and $battery2 -eq 90) "power-off frame must report offline while keeping last level"

$bootFrame = New-Object byte[] 32
[byte[]]$h3 = @(4, 1, 1, 1, 0x64, 0, 0, 0, 1, 0x60)
[Array]::Copy($h3, $bootFrame, $h3.Length)
$online3 = $false
$battery3 = -1
Assert ([RazerBatteryTray.LeobogDeviceHelper]::TryParseStatus($bootFrame, [ref]$online3, [ref]$battery3)) "post-power-on frame must parse"
Assert ($battery3 -eq 100) "post-power-on frame must report 100"
Write-Host "PASS 3: recorded online/offline/boot frames verified."

# 4. 越界与畸形帧
$bad = New-Object byte[] 32
[Array]::Copy($onlineFrame, $bad, 32)
$o = $false; $b = -1
$bad[0] = 0
Assert (-not [RazerBatteryTray.LeobogDeviceHelper]::TryParseStatus($bad, [ref]$o, [ref]$b)) "wrong report id rejected"
$bad[0] = 4
$bad[1] = 2
Assert (-not [RazerBatteryTray.LeobogDeviceHelper]::TryParseStatus($bad, [ref]$o, [ref]$b)) "invalid online flag rejected"
$bad[1] = 1
$bad[4] = 101
Assert (-not [RazerBatteryTray.LeobogDeviceHelper]::TryParseStatus($bad, [ref]$o, [ref]$b)) "invalid percentage rejected"
Assert (-not [RazerBatteryTray.LeobogDeviceHelper]::TryParseStatus((New-Object byte[] 9), [ref]$o, [ref]$b)) "short frame rejected"
Write-Host "PASS 4: malformed frame rejection verified."

# 5. 注册与分类自愈
$dm = [System.IO.File]::ReadAllText((Join-Path $repoRoot "src\Hardware\DeviceManager.cs"), [System.Text.Encoding]::UTF8)
Assert ($dm.Contains('LeobogDeviceHelper.QueryDevices()')) "DeviceManager registration missing"
Assert ($dm.Contains('LeobogDeviceHelper.IsNativeDevice(existDev)')) "native category self-heal missing"
$gen = [System.IO.File]::ReadAllText((Join-Path $repoRoot "src\Hardware\GenericHidHelper.cs"), [System.Text.Encoding]::UTF8)
Assert ($gen.Contains('vid_04f3&pid_026f')) "generic wizard scan must skip this receiver"
$variants = [RazerBatteryTray.DeviceManager]::GetDeviceIdVariants('LEOBOG:04F3:026F')
Assert ($variants.Contains('HID:04F3:026F')) "legacy HID id must bridge to the native id"
Write-Host "PASS 5: registration, variant bridging and wizard de-duplication verified."

Write-Host "ALL TESTS PASSED for LEOBOG GM5 PRO (04F3:026F)!" -ForegroundColor Green
exit 0
