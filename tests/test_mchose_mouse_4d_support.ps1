# ============================================================================
# 测试脚本: test_mchose_mouse_4d_support.ps1
# 目标: 验证迈从 (MCHOSE) 新一代 4D 帧协议族鼠标驱动规范：
#       0x4D 帧结构与 XOR 校验、0x0900 身份/电量解析、0x0002 回报率索引编解码、
#       0x0003 六档 DPI 解析、PID 白名单隔离与 42,000 DPI 能力契约。
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = Resolve-Path "$ScriptDir\.."
Set-Location $ProjectDir

function Assert-Bom([string]$path) {
    if (-not (Test-Path $path)) {
        Write-Host "FAILED: 文件不存在: $path" -ForegroundColor Red
        exit 1
    }
    $bytes = [System.IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 3 -or $bytes[0] -ne 0xEF -or $bytes[1] -ne 0xBB -or $bytes[2] -ne 0xBF) {
        Write-Host "FAILED: 文件不是 UTF-8 with BOM: $path" -ForegroundColor Red
        exit 1
    }
}

function Assert-CSharp5([string]$path) {
    $content = [System.IO.File]::ReadAllText($path, [System.Text.Encoding]::UTF8)
    if ([regex]::IsMatch($content, '\$".*?"')) { Write-Host "FAILED: 字符串插值: $path" -ForegroundColor Red; exit 1 }
    if ([regex]::IsMatch($content, '\w+\?\.')) { Write-Host "FAILED: 空条件运算符: $path" -ForegroundColor Red; exit 1 }
    if ([regex]::IsMatch($content, 'nameof\s*\(')) { Write-Host "FAILED: nameof: $path" -ForegroundColor Red; exit 1 }
    if ([regex]::IsMatch($content, 'out\s+var\s+')) { Write-Host "FAILED: out var: $path" -ForegroundColor Red; exit 1 }
    if (-not $content.Contains([char]0x26A0)) { Write-Host "FAILED: 缺少 AI 维护准则声明: $path" -ForegroundColor Red; exit 1 }
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 1/6] 4D 驱动文件编码与 C# 5.0 规范" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

foreach ($f in @("$ProjectDir\src\Hardware\MchoseMouse4DHelper.cs", "$ProjectDir\src\Hardware\MchoseMouseHelper.cs")) {
    Assert-Bom $f
    Assert-CSharp5 $f
}
Write-Host "PASSED: 4D 驱动与旧协议驱动 BOM 完整、符合 C# 5.0 铁律" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 2/6] 全内存反射加载与类型契约验证" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$binPath = "$ProjectDir\FerrisPulse.exe"
if (-not (Test-Path $binPath)) {
    Write-Host "重新编译以供测试..." -ForegroundColor Yellow
    cmd /c "$ProjectDir\build.bat" | Out-Null
}

$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($binPath))
$t4d = $asm.GetType("RazerBatteryTray.MchoseMouse4DHelper")
if ($null -eq $t4d) {
    Write-Host "FAILED: 程序集中未找到 RazerBatteryTray.MchoseMouse4DHelper" -ForegroundColor Red
    exit 1
}

$methods = @("IsSupportedPid", "BuildFrame", "TryParseDeviceInfo", "TryParsePerformance", "TryParseDpi",
             "GetSupportedPollingRates", "GetMaxPollingRate", "GetTransportName", "GetRateHz",
             "DecodeRateIndex", "EncodeRateIndex", "GetRateIndexForHz", "HasControlEndpoint",
             "QueryMchose4DMouse", "SetDpiStage", "SetDpi", "SetDpiStages", "SetPollingRate")
foreach ($m in $methods) {
    $mi = $t4d.GetMethod($m, [System.Reflection.BindingFlags]"Public,Static")
    if ($null -eq $mi) {
        Write-Host "FAILED: 缺少公共静态方法 $m" -ForegroundColor Red
        exit 1
    }
}
Write-Host "PASSED: MchoseMouse4DHelper 18 项核心契约方法全部就绪" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 3/6] 0x4D 帧结构与 XOR 校验和" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$buildFrame = $t4d.GetMethod("BuildFrame", [System.Reflection.BindingFlags]"Public,Static")

[object[]]$bfArgs = New-Object object[] 2
$bfArgs[0] = 0x0900
$bfArgs[1] = $null
[byte[]]$frame = $buildFrame.Invoke($null, $bfArgs)

if ($frame.Length -ne 64) { Write-Host "FAILED: 帧长度应为 64，实际 $($frame.Length)" -ForegroundColor Red; exit 1 }
if ($frame[0] -ne 0x4D) { Write-Host "FAILED: 帧头应为 0x4D" -ForegroundColor Red; exit 1 }
if ($frame[1] -ne 0x01) { Write-Host "FAILED: 版本字节应为 0x01" -ForegroundColor Red; exit 1 }
if ($frame[2] -ne 0x01) { Write-Host "FAILED: flags 应为 0x01" -ForegroundColor Red; exit 1 }
if ($frame[3] -ne 0) { Write-Host "FAILED: 空 data 长度应为 0" -ForegroundColor Red; exit 1 }
if ($frame[4] -ne 0x00 -or $frame[5] -ne 0x09) { Write-Host "FAILED: 命令字 0x0900 小端编码错误: $($frame[4]):$($frame[5])" -ForegroundColor Red; exit 1 }

$expect = 0
for ($i = 2; $i -le 7; $i++) { $expect = $expect -bxor $frame[$i] }
if ($frame[8] -ne $expect) { Write-Host "FAILED: 校验和错误 期望 $expect 实际 $($frame[8])" -ForegroundColor Red; exit 1 }

[object[]]$bfArgs2 = New-Object object[] 2
$bfArgs2[0] = 0x0103
$bfArgs2[1] = [byte[]]@(0x01, 0x02, 0x03, 0x04)
[byte[]]$frame2 = $buildFrame.Invoke($null, $bfArgs2)
if ($frame2[3] -ne 4) { Write-Host "FAILED: data 长度字段错误" -ForegroundColor Red; exit 1 }
if ($frame2[8] -ne 0x01 -or $frame2[11] -ne 0x04) { Write-Host "FAILED: data 区不应做 ^0xFF 反转" -ForegroundColor Red; exit 1 }
$expect2 = 0
for ($i = 2; $i -le 11; $i++) { $expect2 = $expect2 -bxor $frame2[$i] }
if ($frame2[12] -ne $expect2) { Write-Host "FAILED: 带数据帧校验和错误" -ForegroundColor Red; exit 1 }

Write-Host "PASSED: 0x4D 帧头/长度/小端命令字/XOR 校验和全部精确，payload 未反转" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 4/6] 0x0900 身份与物理电量解析" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$tryDev = $t4d.GetMethod("TryParseDeviceInfo", [System.Reflection.BindingFlags]"Public,Static")

[byte[]]$rx = New-Object byte[] 64
$rx[0] = 0x4D
$rx[1] = 0x01
$rx[8] = 0x37; $rx[9] = 0x38
$rx[10] = 0x18; $rx[11] = 0x10
$rx[12] = 0x04
$rx[18] = 0x02
$rx[19] = 0x00
$rx[20] = 77
$rx[21] = 0x01

[object[]]$pdArgs = New-Object object[] 2
$pdArgs[0] = $rx
$pdArgs[1] = $null
$ok = $tryDev.Invoke($null, $pdArgs)
if (-not $ok) { Write-Host "FAILED: 合法 0x0900 应答未能解析" -ForegroundColor Red; exit 1 }
$info = $pdArgs[1]
if ($info.Vid -ne 0x3837 -or $info.Pid -ne 0x1018 -or $info.Battery -ne 77 -or $info.ChargeStatus -ne 0 -or $info.ConnectStatus -ne 2) {
    Write-Host "FAILED: 身份解析字段不匹配 VID=$($info.Vid) PID=$($info.Pid) Bat=$($info.Battery)" -ForegroundColor Red
    exit 1
}

$rx[19] = 0x01
$rx[20] = 100
$pdArgs[1] = $null
[void]$tryDev.Invoke($null, $pdArgs)
if ($pdArgs[1].ChargeStatus -ne 1 -or $pdArgs[1].Battery -ne 100) {
    Write-Host "FAILED: 充电状态 / 100% 电量解析错误" -ForegroundColor Red; exit 1
}

$rx[8] = 0x6A; $rx[9] = 0x04
$pdArgs[1] = $null
$badOk = $tryDev.Invoke($null, $pdArgs)
if ($badOk) { Write-Host "FAILED: 非 0x3837 VID 应返回 false" -ForegroundColor Red; exit 1 }

$rx[8] = 0x37; $rx[9] = 0x38
$rx[20] = 200
$pdArgs[1] = $null
$badOk2 = $tryDev.Invoke($null, $pdArgs)
if ($badOk2) { Write-Host "FAILED: 电量 >100 应返回 false" -ForegroundColor Red; exit 1 }

Write-Host "PASSED: 0x0900 身份报文解析精准，77%/100% 电量与充电状态、异常报文拒绝全部正确" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 5/6] 0x0002 回报率索引与 0x0003 六档 DPI 解析" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$tryPerf = $t4d.GetMethod("TryParsePerformance", [System.Reflection.BindingFlags]"Public,Static")
$getHz = $t4d.GetMethod("GetRateHz", [System.Reflection.BindingFlags]"Public,Static")
$getIdx = $t4d.GetMethod("GetRateIndexForHz", [System.Reflection.BindingFlags]"Public,Static")
$enc = $t4d.GetMethod("EncodeRateIndex", [System.Reflection.BindingFlags]"Public,Static")
$dec = $t4d.GetMethod("DecodeRateIndex", [System.Reflection.BindingFlags]"Public,Static")

[byte[]]$pf = New-Object byte[] 64
$pf[0] = 0x4D
$pf[8] = 0x00
$pf[9] = [byte]((0x03 -shl 4) -bor 0x00)
$pf[10] = [byte]((0x05 -shl 4) -bor 0x01)
$pf[11] = 10
$pf[13] = 0x01

[object[]]$ppArgs = New-Object object[] 2
$ppArgs[0] = $pf
$ppArgs[1] = $null
if (-not $tryPerf.Invoke($null, $ppArgs)) { Write-Host "FAILED: 0x0002 应答解析失败" -ForegroundColor Red; exit 1 }
$perf = $ppArgs[1]

[object[]]$hzArgs = New-Object object[] 1
$hzArgs[0] = $perf.UsbRateRaw
$usbHz = $getHz.Invoke($null, $hzArgs)
$hzArgs[0] = $perf.WirelessRateRaw
$wlHz = $getHz.Invoke($null, $hzArgs)

# 协议内回报率索引为「RATES 索引 + 1」存储：raw 3 -> 索引 2 -> 1000Hz，raw 5 -> 索引 4 -> 4000Hz。
if ($usbHz -ne 1000 -or $wlHz -ne 4000) {
    Write-Host "FAILED: 回报率解析错误 USB=$usbHz WL=$wlHz" -ForegroundColor Red; exit 1
}

foreach ($hz in @(125, 500, 1000, 2000, 4000, 8000)) {
    [object[]]$iArgs = New-Object object[] 1
    $iArgs[0] = $hz
    $idx = $getIdx.Invoke($null, $iArgs)
    [object[]]$eArgs = New-Object object[] 1
    $eArgs[0] = $idx
    $raw = $enc.Invoke($null, $eArgs)
    [object[]]$dArgs = New-Object object[] 1
    $dArgs[0] = $raw
    $back = $dec.Invoke($null, $dArgs)
    if ($back -ne $idx) { Write-Host "FAILED: 索引编解码往返失败 hz=$hz" -ForegroundColor Red; exit 1 }
    [object[]]$hArgs2 = New-Object object[] 1
    $hArgs2[0] = $raw
    $roundHz = $getHz.Invoke($null, $hArgs2)
    if ($roundHz -ne $hz) { Write-Host "FAILED: $hz Hz 往返变 $roundHz Hz" -ForegroundColor Red; exit 1 }
}

$rates = $t4d.GetMethod("GetSupportedPollingRates").Invoke($null, @())
$maxRate = $t4d.GetMethod("GetMaxPollingRate").Invoke($null, @())
if ($rates.Length -ne 6 -or $rates[5] -ne 8000 -or $maxRate -ne 8000) {
    Write-Host "FAILED: 支持回报率档位应为 6 档且上限 8000Hz" -ForegroundColor Red; exit 1
}

$tryDpi = $t4d.GetMethod("TryParseDpi", [System.Reflection.BindingFlags]"Public,Static")
[byte[]]$dp = New-Object byte[] 64
$dp[0] = 0x4D
$dp[8] = 0x00
$dp[9] = 0x00
$dp[10] = 0x06
$dp[11] = 0x02
$dp[12] = 0x01
$dpiVals = @(400, 800, 2200, 6400, 16000, 42000)
for ($i = 0; $i -lt 6; $i++) {
    $dp[13 + $i * 2] = [byte]($dpiVals[$i] -band 0xFF)
    $dp[14 + $i * 2] = [byte](($dpiVals[$i] -shr 8) -band 0xFF)
}

[object[]]$dpArgs2 = New-Object object[] 2
$dpArgs2[0] = $dp
$dpArgs2[1] = $null
if (-not $tryDpi.Invoke($null, $dpArgs2)) { Write-Host "FAILED: 0x0003 DPI 应答解析失败" -ForegroundColor Red; exit 1 }
$dinfo = $dpArgs2[1]
if ($dinfo.StageCount -ne 6 -or $dinfo.ActiveStage -ne 2 -or $dinfo.HasY -ne 1) {
    Write-Host "FAILED: DPI 头字段解析错误 stages=$($dinfo.StageCount) active=$($dinfo.ActiveStage)" -ForegroundColor Red; exit 1
}
for ($i = 0; $i -lt 6; $i++) {
    if ($dinfo.StagesX[$i] -ne $dpiVals[$i]) {
        Write-Host "FAILED: 第 $($i+1) 档 DPI 解析错误 期望 $($dpiVals[$i]) 实际 $($dinfo.StagesX[$i])" -ForegroundColor Red
        exit 1
    }
}
if ($dinfo.StagesX[5] -ne 42000) { Write-Host "FAILED: 42,000 DPI 上限解析错误" -ForegroundColor Red; exit 1 }

Write-Host "PASSED: 回报率 125~8000Hz 六档往返一致，六档 DPI 400~42000 与 XY 独立标志解析正确" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 6/6] PID 白名单隔离与 42,000 DPI 能力契约" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$isPid = $t4d.GetMethod("IsSupportedPid", [System.Reflection.BindingFlags]"Public,Static")
foreach ($devPid in @(0x1001, 0x1014, 0x1016, 0x1018, 0x101C, 0x101D)) {
    [object[]]$a = New-Object object[] 1
    $a[0] = $devPid
    if (-not $isPid.Invoke($null, $a)) { Write-Host "FAILED: PID 0x$($devPid.ToString('X4')) 应在 4D 白名单内" -ForegroundColor Red; exit 1 }
}
foreach ($devPid in @(0x100C, 0x1010, 0x3033)) {
    [object[]]$a = New-Object object[] 1
    $a[0] = $devPid
    if ($isPid.Invoke($null, $a)) { Write-Host "FAILED: PID 0x$($devPid.ToString('X4')) 不应走 4D 协议" -ForegroundColor Red; exit 1 }
}

$drvType = $asm.GetType("RazerBatteryTray.MchoseMouseDriver")
$drv = [System.Activator]::CreateInstance($drvType)
$infoType = $asm.GetType("RazerBatteryTray.MouseBatteryInfo")
$catType = $asm.GetType("RazerBatteryTray.DeviceCategory")
$mouseCat = $catType.GetField("Mouse").GetValue($null)

$devNew = [System.Activator]::CreateInstance($infoType)
$devNew.Brand = "MCHOSE"
$devNew.Category = $mouseCat
$devNew.DeviceId = "MCHOSE:3837:1018"
$drv.PopulateCapabilities($devNew)
if ($devNew.Capabilities.MaxDpi -ne 42000) {
    Write-Host "FAILED: 4D 机型上限应为 42000，实际 $($devNew.Capabilities.MaxDpi)" -ForegroundColor Red; exit 1
}
if (-not $devNew.Capabilities.SupportsPollingRate) {
    Write-Host "FAILED: 4D 机型应支持回报率调节" -ForegroundColor Red; exit 1
}

# XY 独立必须跟随设备实际回包（0x0003 的 [12] HasY 标志），不能一概而论。
# 实机 PAW3950 单传感器机型该位恒为 0，写入 1200x800 后回读为 1200x1200。
$devNew.IsXyIndependent = $false
$drv.PopulateCapabilities($devNew)
if ($devNew.Capabilities.SupportsXyIndependence) {
    Write-Host "FAILED: HasY=0 的 4D 机型不应声明 XY 独立" -ForegroundColor Red; exit 1
}

$devNew.IsXyIndependent = $true
$drv.PopulateCapabilities($devNew)
if (-not $devNew.Capabilities.SupportsXyIndependence) {
    Write-Host "FAILED: HasY=1 的 4D 机型应声明 XY 独立" -ForegroundColor Red; exit 1
}

$devOld = [System.Activator]::CreateInstance($infoType)
$devOld.Brand = "MCHOSE"
$devOld.Category = $mouseCat
$devOld.DeviceId = "MCHOSE:3837:100C"
$drv.PopulateCapabilities($devOld)
if ($devOld.Capabilities.MaxDpi -ne 26000) {
    Write-Host "FAILED: 旧 A5 V2 上限应保持 26000，实际 $($devOld.Capabilities.MaxDpi)" -ForegroundColor Red; exit 1
}

Write-Host "PASSED: 4D 白名单与旧协议物理隔离，42,000 / 26,000 DPI 双能力档正确分流" -ForegroundColor Green
Write-Host "`n>>> 全部 6 项迈从 4D 帧协议族鼠标自动化契约测试 100% 通过！ <<<" -ForegroundColor Green
