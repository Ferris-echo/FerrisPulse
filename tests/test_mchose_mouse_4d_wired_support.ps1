# ============================================================================
# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# 本文件受 AGENT_HANDBOOK.md 铁律约束：
# 1. 严格 C# 5.0 / .NET 4.0 语法基准
# 2. 零外部依赖
# 3. 所有文本文件必须 UTF-8 with BOM
# 10. 测试脚本严禁 Add-Type -Path / LoadFrom / LoadFile（会锁死 exe），
#     必须 [System.IO.File]::ReadAllBytes + [System.Reflection.Assembly]::Load($bytes)
# ============================================================================
# 迈从 4D 协议族「USB 有线形态」回归测试
#
# 背景（实机 K7 V2 Ultra+，2026-10-29 实测）：
#   同一台鼠标有三种硬件身份，必须按接口 PID 分别识别：
#     2.4G 接收器 → 3837:1018（0x4D 厂商通道，DPI/回报率可读写）
#     USB 有线直连 → 3837:4028（同一套 0x4D 协议，能力与 2.4G 完全一致）
#     蓝牙 BLE    → 3837:4026（无厂商通道，只有电量）
#
#   有线形态的坑：鼠标插上线后，2.4G 接收器往往仍插在机器上，
#   于是 0xFF01 集合会同时枚举出 0x1018（已断链）与 0x4028（在线）两个端点。
#   若端点挑选逻辑不优先选 0x4028，所有写帧都会打进断链的接收器，功能全哑。
#
# 本测试锁定四件事：
#   A. 源码层：0x4028 进白名单、常量存在、传输名按 PID 判定
#   B. 运行时：0x4028 被 IsSupportedPid 接受，且 0x4026 / 键盘 PID 被拒
#   C. 运行时：GetTransportName 在有线 PID 下必须返回「USB 有线」
#   D. 运行时：能力分流下有线形态能力与 2.4G 完全一致，且跨形态归并到同一物理键
# ============================================================================

$ErrorActionPreference = 'Stop'
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $scriptDir
Set-Location $root

$script:fail = 0
function Check([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) {
        Write-Host ('PASS: ' + $name)
    } else {
        Write-Host ('FAIL: ' + $name + ' -- ' + $detail)
        $script:fail++
    }
}

# ------------------------------------------------------- A. 文件编码与铁律
$helperPath = Join-Path $root 'src\Hardware\MchoseMouse4DHelper.cs'
$driversPath = Join-Path $root 'src\Hardware\BuiltInDrivers.cs'
$uiPath = Join-Path $root 'src\UI\Forms\MainForm.Hardware.cs'

foreach ($p in @($helperPath, $driversPath, $uiPath)) {
    Check ((Split-Path -Leaf $p) + ' exists') (Test-Path $p) $p
    $b = [System.IO.File]::ReadAllBytes($p)
    $bom = ($b.Length -ge 3) -and ($b[0] -eq 0xEF) -and ($b[1] -eq 0xBB) -and ($b[2] -eq 0xBF)
    Check ((Split-Path -Leaf $p) + ' is UTF-8 with BOM') $bom 'missing BOM'
    $t = [System.Text.Encoding]::UTF8.GetString($b)
    Check ((Split-Path -Leaf $p) + ' no CRCRLF corruption') (-not $t.Contains([string][char]13 + [string][char]13 + [string][char]10)) 'found CRCRLF'
    Check ((Split-Path -Leaf $p) + ' keeps AI directive') ($t.Contains([char]0x26A0)) 'missing directive'
    $codeOnly = (($t -split "`r`n") | Where-Object { $_ -notmatch '^\s*//' }) -join "`r`n"
    Check ((Split-Path -Leaf $p) + ' no C# 6 interpolation') (-not [regex]::IsMatch($codeOnly, '\$".*?"')) 'found $"..."'
    Check ((Split-Path -Leaf $p) + ' no null-conditional operator') (-not [regex]::IsMatch($codeOnly, '\w+\?\.')) 'found ?.'
    Check ((Split-Path -Leaf $p) + ' no nameof') (-not [regex]::IsMatch($codeOnly, 'nameof\s*\(')) 'found nameof'
}

$helperSrc = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($helperPath))

# ------------------------------------------------------- B. 源码层契约
Check 'wired PID constant 0x4028' `
    ($helperSrc.Contains('WiredProductIdK7V2UltraPlus = 0x4028')) 'missing constant'
Check 'wired PID in SupportedPids' `
    ($helperSrc.Contains('WiredProductIdK7V2UltraPlus // K7 V2 Ultra+')) 'not in whitelist'
Check 'wired PID in physical group' `
    ($helperSrc.Contains('WiredProductIdK7V2UltraPlus, // 0x4028')) 'not in physical group'
Check 'endpoint picker prefers wired PID' `
    ($helperSrc.Contains('if (list[i].ProductId == WiredProductIdK7V2UltraPlus) return list[i];')) 'no priority rule'
Check 'transport name by PID method' `
    ($helperSrc.Contains('public static string GetTransportNameByPid(int connectStatus, int productId)')) 'missing overload'
Check 'wired transport maps to USB 有线' `
    ($helperSrc.Contains('if (productId == WiredProductIdK7V2UltraPlus) return "USB 有线";')) 'missing mapping'
Check 'stale card no longer hardcodes 2.4G' `
    (-not $helperSrc.Contains('dev.Transport = "2.4G 无线";')) 'still hardcoded'
Check 'wired card reports no dongle' `
    ($helperSrc.Contains('dev.IsDonglePresent = (descriptor.ProductId != WiredProductIdK7V2UltraPlus);')) 'dongle flag wrong'

# ------------------------------------------------------- C. 运行时契约
$binPath = Join-Path $root 'FerrisPulse.exe'
if (-not (Test-Path $binPath)) {
    Write-Host '重新编译以供测试...' -ForegroundColor Yellow
    cmd /c (Join-Path $root 'build.bat') | Out-Null
}
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($binPath))
$t4d = $asm.GetType('RazerBatteryTray.MchoseMouse4DHelper')
Check 'MchoseMouse4DHelper type loaded' ($t4d -ne $null) 'type missing'
if ($t4d -eq $null) {
    Write-Host ''
    Write-Host ('>>> 迈从 4D 有线形态测试失败 ' + $script:fail + ' 项 <<<') -ForegroundColor Red
    exit 1
}

$flags = [System.Reflection.BindingFlags]'Public,Static'

# C1. 白名单：0x4028 属于本协议族；0x4026（BLE）与 0x3033（键盘）不属于 USB 通道。
$isPid = $t4d.GetMethod('IsSupportedPid', $flags)
Check 'USB whitelist accepts 0x4028' ([bool]$isPid.Invoke($null, @(0x4028))) '4028 rejected'
Check 'USB whitelist still accepts 0x1018' ([bool]$isPid.Invoke($null, @(0x1018))) '1018 rejected'
Check 'USB whitelist rejects BLE 0x4026' (-not [bool]$isPid.Invoke($null, @(0x4026))) '4026 wrongly accepted'
Check 'USB whitelist rejects keyboard 0x3033' (-not [bool]$isPid.Invoke($null, @(0x3033))) '3033 wrongly accepted'

# C2. 传输形态：以接口 PID 为主判据，避免固件把 [18] 改回 0 就误判成有线。
$trName = $t4d.GetMethod('GetTransportNameByPid', $flags)
Check 'transport-by-pid method exists' ($trName -ne $null) 'missing method'
if ($trName -ne $null) {
    Check 'PID 0x4028 -> USB 有线' ([string]$trName.Invoke($null, @(1, 0x4028)) -eq 'USB 有线') ('actual ' + [string]$trName.Invoke($null, @(1, 0x4028)))
    Check 'PID 0x1018 -> 2.4G 无线' ([string]$trName.Invoke($null, @(0, 0x1018)) -eq '2.4G 无线') ('actual ' + [string]$trName.Invoke($null, @(0, 0x1018)))
    Check 'unknown PID falls back to status 0' ([string]$trName.Invoke($null, @(0, 0x9999)) -eq 'USB 有线') 'fallback broken'
    Check 'unknown PID falls back to status 2' ([string]$trName.Invoke($null, @(2, 0x9999)) -eq '2.4G 无线') 'fallback broken'
}

# C3. 解析器：用有线形态真实抓取的回包验证，防止字段偏移被改坏。
$dpiFrame = [byte[]]@(0x4D,0x01,0x01,0x11,0x03,0x00,0x00,0x00,0x00,0x00,0x01,0x00,0x00,0x98,0x08,0x40,0x06,0x80,0x0C,0x80,0x0C,0x00,0x19,0x50,0xC3,0x4E)
$parseDpi = $t4d.GetMethod('TryParseDpi', $flags)
[object[]]$pd = New-Object object[] 2
$pd[0] = $dpiFrame; $pd[1] = $null
Check 'wired DPI frame parses' ([bool]$parseDpi.Invoke($null, $pd)) 'parse failed'
if ([bool]$parseDpi.Invoke($null, $pd)) {
    $d = $pd[1]
    Check 'wired DPI stage count = 1' ($d.StageCount -eq 1) ('actual ' + $d.StageCount)
    Check 'wired DPI first stage = 2200' ($d.StagesX[0] -eq 2200) ('actual ' + $d.StagesX[0])
}

$perfFrame = [byte[]]@(0x4D,0x01,0x01,0x13,0x02,0x00,0x00,0x00,0x00,0x30,0x30,0x03,0x00,0x5C,0xFF,0x08,0x08,0x08,0x08,0x08,0x08,0x08,0x08,0x08,0x08,0x08,0x08,0xB0)
$parsePerf = $t4d.GetMethod('TryParsePerformance', $flags)
[object[]]$pp = New-Object object[] 2
$pp[0] = $perfFrame; $pp[1] = $null
Check 'wired performance frame parses' ([bool]$parsePerf.Invoke($null, $pp)) 'parse failed'
if ([bool]$parsePerf.Invoke($null, $pp)) {
    $getRate = $t4d.GetMethod('GetRateHz', $flags)
    Check 'wired polling rate = 1000 Hz' ([int]$getRate.Invoke($null, @($pp[1].UsbRateRaw)) -eq 1000) ('actual ' + [int]$getRate.Invoke($null, @($pp[1].UsbRateRaw)))
}

# C4. 能力分流：有线形态与 2.4G 必须完全一致（用户明确要求「别的功能都与 2.4G 下一致」）。
$drvType = $asm.GetType('RazerBatteryTray.MchoseMouseDriver')
$drv = [System.Activator]::CreateInstance($drvType)
$infoType = $asm.GetType('RazerBatteryTray.MouseBatteryInfo')
$catType = $asm.GetType('RazerBatteryTray.DeviceCategory')
$mouseCat = $catType.GetField('Mouse').GetValue($null)

function New-Dev([string]$deviceId, [string]$transport) {
    $d = [System.Activator]::CreateInstance($infoType)
    $d.Brand = 'MCHOSE'
    $d.Category = $mouseCat
    $d.DeviceId = $deviceId
    $d.Transport = $transport
    return $d
}

$devWired = New-Dev 'MCHOSE:3837:4028' 'USB 有线'
$drv.PopulateCapabilities($devWired)
$dev24 = New-Dev 'MCHOSE:3837:1018' '2.4G 无线'
$drv.PopulateCapabilities($dev24)

Check 'wired keeps battery capability' ($devWired.Capabilities.SupportsBattery) 'battery lost'
Check 'wired keeps DPI capability' ($devWired.Capabilities.SupportsDpi) 'DPI lost'
Check 'wired keeps DPI stages capability' ($devWired.Capabilities.SupportsDpiStages) 'stages lost'
Check 'wired keeps polling rate capability' ($devWired.Capabilities.SupportsPollingRate) 'rate lost'
Check 'wired max DPI is 42000' ($devWired.Capabilities.MaxDpi -eq 42000) ('actual ' + $devWired.Capabilities.MaxDpi)
Check 'wired rate table matches 2.4G' `
    (($devWired.Capabilities.SupportedPollingRates -join ',') -eq ($dev24.Capabilities.SupportedPollingRates -join ',')) 'rate table differs'
Check 'wired DPI range matches 2.4G' `
    (($devWired.Capabilities.MinDpi -eq $dev24.Capabilities.MinDpi) -and ($devWired.Capabilities.MaxDpi -eq $dev24.Capabilities.MaxDpi)) 'DPI range differs'
Check 'wired card title not BLE-degraded' `
    (-not $devWired.Capabilities.SensorDescription.Contains('蓝牙')) 'wrongly degraded'

# C5. 跨形态聚焦记忆：4028 与 1018 必须归并到同一物理设备键。
$getKey = $t4d.GetMethod('GetPhysicalDeviceKey', $flags)
Check 'physical key: wired 4028 folds to 1018' `
    ([string]$getKey.Invoke($null, @('MCHOSE:3837:4028')) -eq 'MCHOSE:3837:1018') 'not folded'
Check 'physical key: wired equals 2.4G' `
    ([string]$getKey.Invoke($null, @('MCHOSE:3837:4028')) -eq [string]$getKey.Invoke($null, @('MCHOSE:3837:1018'))) 'keys differ'
Check 'physical key: wired equals BLE' `
    ([string]$getKey.Invoke($null, @('MCHOSE:3837:4028')) -eq [string]$getKey.Invoke($null, @('MCHOSE:3837:4026'))) 'keys differ'

# ------------------------------------------------------- 汇总
Write-Host ''
if ($script:fail -gt 0) {
    Write-Host ('>>> 迈从 4D 有线形态测试失败 ' + $script:fail + ' 项 <<<') -ForegroundColor Red
    exit 1
}
Write-Host '>>> 迈从 4D 协议族 USB 有线形态自动化契约测试 100% 通过！ <<<' -ForegroundColor Green
exit 0
