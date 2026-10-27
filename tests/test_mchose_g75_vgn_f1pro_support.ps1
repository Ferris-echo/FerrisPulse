# ============================================================================
# 测试脚本: test_mchose_g75_vgn_f1pro_support.ps1
# 目标: 验证社区贡献者 “yaphets” 提供的 G75 PRO 与 VGN F1 Pro 电量/充电状态、
#       CompX 同 VID 17/20 字节端点隔离与 HIDI2C 内置触摸板过滤。
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = Resolve-Path "$ScriptDir\.."
Set-Location $ProjectDir

function Fail([string]$message) {
    Write-Host ("FAILED: " + $message) -ForegroundColor Red
    exit 1
}

function Assert-Bom([string]$path) {
    if (-not (Test-Path $path)) { Fail ("文件不存在: " + $path) }
    $bytes = [System.IO.File]::ReadAllBytes($path)
    $signature = ""
    if ($bytes.Length -ge 3) {
        $signature = [System.BitConverter]::ToString($bytes, 0, 3)
    }
    if ($signature -ne "EF-BB-BF") {
        Fail ("文件不是 UTF-8 with BOM: " + $path)
    }
}

function Assert-CSharp5([string]$path) {
    $rawLines = [System.IO.File]::ReadAllLines($path, [System.Text.Encoding]::UTF8)
    $codeLines = $rawLines | Where-Object { -not ($_.TrimStart().StartsWith("//")) }
    $content = $codeLines -join "`n"
    if ([regex]::IsMatch($content, '\$".*?"')) { Fail ("发现字符串插值: " + $path) }
    if ([regex]::IsMatch($content, '\w+\?\.')) { Fail ("发现空条件运算符: " + $path) }
    if ([regex]::IsMatch($content, 'nameof\s*\(')) { Fail ("发现 nameof: " + $path) }
    if ([regex]::IsMatch($content, 'out\s+var\s+')) { Fail ("发现 out var: " + $path) }
}

Write-Host "[1/5] 文件规范与接线检查" -ForegroundColor Cyan
$sourceFiles = @(
    "$ProjectDir\src\Hardware\MchoseDeviceHelper.cs",
    "$ProjectDir\src\Hardware\VgnDeviceHelper.cs",
    "$ProjectDir\src\Hardware\GenericHidHelper.cs",
    "$ProjectDir\src\Hardware\DeviceManager.cs",
    "$ProjectDir\src\UI\Forms\AddDeviceWizardForm.cs"
)
foreach ($file in $sourceFiles) {
    Assert-Bom $file
    Assert-CSharp5 $file
}

$wizardContent = [System.IO.File]::ReadAllText("$ProjectDir\src\UI\Forms\AddDeviceWizardForm.cs", [System.Text.Encoding]::UTF8)
if (-not $wizardContent.Contains("MchoseDeviceHelper.QueryAllMchoseDevices()")) {
    Fail "添加设备向导未接入 MCHOSE 专用扫描"
}
$genericContent = [System.IO.File]::ReadAllText("$ProjectDir\src\Hardware\GenericHidHelper.cs", [System.Text.Encoding]::UTF8)
if (-not $genericContent.Contains("IsInternalHidDevice")) {
    Fail "通用 HID 扫描未实现内置 HIDI2C 过滤"
}
$managerContent = [System.IO.File]::ReadAllText("$ProjectDir\src\Hardware\DeviceManager.cs", [System.Text.Encoding]::UTF8)
if (-not $managerContent.Contains("GenericHidHelper.IsInternalHidDevice(internalName, internalIdentity)")) {
    Fail "已误保存的 HIDI2C 自定义配置未在 DeviceManager 中抑制"
}

Write-Host "[2/5] 编译并全内存加载" -ForegroundColor Cyan
$binPath = "$ProjectDir\FerrisPulse.exe"
if (-not (Test-Path $binPath)) {
    cmd /c "$ProjectDir\build.bat"
    if ($LASTEXITCODE -ne 0 -or -not (Test-Path $binPath)) { Fail "build.bat 编译失败" }
}
$asmBytes = [System.IO.File]::ReadAllBytes($binPath)
$asm = [System.Reflection.Assembly]::Load($asmBytes)

$mchoseType = $asm.GetType("RazerBatteryTray.MchoseDeviceHelper")
$vgnType = $asm.GetType("RazerBatteryTray.VgnDeviceHelper")
$genericType = $asm.GetType("RazerBatteryTray.GenericHidHelper")
if ($null -eq $mchoseType -or $null -eq $vgnType -or $null -eq $genericType) {
    Fail "适配类型未进入程序集"
}

Write-Host "[3/5] G75 PRO 实机报文回放" -ForegroundColor Cyan
$isMchose = $mchoseType.GetMethod("IsSupportedVidPid", [System.Reflection.BindingFlags]"Public,Static")
if (-not $isMchose.Invoke($null, @(0x3554, 0xFA09))) { Fail "3554:FA09 未列入 MCHOSE 键盘白名单" }
if ($isMchose.Invoke($null, @(0x3554, 0xF503))) { Fail "VGN F503 被误列入 MCHOSE 键盘白名单" }

$buildG75 = $mchoseType.GetMethod("BuildG75BatteryFrame", [System.Reflection.BindingFlags]"Public,Static")
$g75Tx = $buildG75.Invoke($null, @())
if ($g75Tx.Length -ne 20 -or $g75Tx[0] -ne 0x13 -or $g75Tx[1] -ne 0x4A -or $g75Tx[2] -ne 0x00 -or $g75Tx[19] -ne 0x5D) {
    Fail "G75 0x4A 查询帧与实机帧不一致"
}

$parseG75 = $mchoseType.GetMethod("ParseG75BatteryResponse", [System.Reflection.BindingFlags]"Public,Static")
function Invoke-G75Parse([byte[]]$frame) {
    [object[]]$args = New-Object object[] 4
    $args[0] = $frame; $args[1] = $frame.Length; $args[2] = 0; $args[3] = $false
    $ok = $parseG75.Invoke($null, $args)
    return @{ Ok = $ok; Battery = [int]$args[2]; Charging = [bool]$args[3] }
}

[byte[]]$g75Battery = 0x13,0x4A,0x01,0x00,0x02,0x4C,0x01,0,0,0,0,0,0,0,0,0,0,0,0,0xAD
[byte[]]$g75Charging = 0x13,0x4A,0x01,0x00,0x02,0x5D,0x10,0,0,0,0,0,0,0,0,0,0,0,0,0xCD
$g75A = Invoke-G75Parse $g75Battery
$g75B = Invoke-G75Parse $g75Charging
if (-not $g75A.Ok -or $g75A.Battery -ne 76 -or $g75A.Charging) { Fail "G75 76% 电池状态解析失败" }
if (-not $g75B.Ok -or $g75B.Battery -ne 93 -or -not $g75B.Charging) { Fail "G75 93% 充电状态解析失败" }
$g75Charging[19] = 0x00
if ((Invoke-G75Parse $g75Charging).Ok) { Fail "G75 错误校验和未被拒绝" }

Write-Host "[4/5] VGN F1 Pro 实机报文回放与端点隔离" -ForegroundColor Cyan
$isVgnEndpoint = $vgnType.GetMethod("IsVgnProtocolEndpoint", [System.Reflection.BindingFlags]"Public,Static")
if (-not $isVgnEndpoint.Invoke($null, @(0x3554, 0xF503, 0xFF02, 0x0002, 17, 17))) {
    Fail "VGN F503 17/17 协议端点未被识别"
}
if ($isVgnEndpoint.Invoke($null, @(0x3554, 0xFA09, 0xFF02, 0x0002, 20, 20))) {
    Fail "G75 FA09 20/20 端点仍会被 VGN 驱动误探测"
}

$parseVgn = $vgnType.GetMethod("ParseBatteryResponse", [System.Reflection.BindingFlags]"Public,Static")
function Invoke-VgnParse([byte[]]$frame) {
    [object[]]$args = New-Object object[] 4
    $args[0] = $frame; $args[1] = $frame.Length; $args[2] = 0; $args[3] = $false
    $ok = $parseVgn.Invoke($null, $args)
    return @{ Ok = $ok; Battery = [int]$args[2]; Charging = [bool]$args[3] }
}

[byte[]]$vgnBattery = 0x08,0x04,0,0,0,0x02,0x64,0x00,0x10,0x2A,0,0,0,0,0,0,0xA9
[byte[]]$vgnCharging = 0x08,0x04,0,0,0,0x02,0x5F,0x01,0x10,0x69,0,0,0,0,0,0,0x6E
$vgnA = Invoke-VgnParse $vgnBattery
$vgnB = Invoke-VgnParse $vgnCharging
if (-not $vgnA.Ok -or $vgnA.Battery -ne 100 -or $vgnA.Charging) { Fail "VGN 100% 未充电报文解析失败" }
if (-not $vgnB.Ok -or $vgnB.Battery -ne 95 -or -not $vgnB.Charging) { Fail "VGN 95% 充电报文解析失败" }
$vgnCharging[16] = 0x00
if ((Invoke-VgnParse $vgnCharging).Ok) { Fail "VGN 错误校验和未被拒绝" }

$getVgnName = $vgnType.GetMethod("GetModelName", [System.Reflection.BindingFlags]"Public,Static")
$vgnName = $getVgnName.Invoke($null, @(0xF503, "VGN Mouse 2.4G Receiver", 2, 1))
if ($vgnName -ne "VGN Dragonfly F1 Pro") { Fail ("F503 型号映射错误: " + $vgnName) }

Write-Host "[5/5] HIDI2C 内置触摸板过滤" -ForegroundColor Cyan
$isInternal = $genericType.GetMethod("IsInternalHidDevice", [System.Reflection.BindingFlags]"Public,Static")
if (-not $isInternal.Invoke($null, @("HIDI2C Device", "hid#vid_093a&pid_0005"))) {
    Fail "HIDI2C Device 未被识别为内置 HID 设备"
}
if ($isInternal.Invoke($null, @("VGN Mouse 2.4G Receiver", "hid#vid_3554&pid_f503"))) {
    Fail "VGN 接收器被错误过滤"
}

Write-Host "PASSED: G75 PRO 与 VGN F1 Pro 电量、充电状态、端点隔离及 HIDI2C 过滤全部通过。" -ForegroundColor Green
exit 0
