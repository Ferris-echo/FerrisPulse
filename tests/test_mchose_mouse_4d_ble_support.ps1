# ============================================================================
# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# 本文件受 AGENT_HANDBOOK.md 铁律约束：
# 1. 严格 C# 5.0 / .NET 4.0 语法基准
# 2. 零外部依赖
# 3. 所有文本文件必须 UTF-8 with BOM
# 10. 测试脚本严禁 Add-Type -Path / LoadFrom / LoadFile（会锁死 exe），
#     必须 [System.IO.File]::ReadAllBytes + [System.Reflection.Assembly]::Load($bytes)
# ============================================================================
# 迈从 4D 协议族「蓝牙 BLE 形态」回归测试
#
# 背景（实机 K7 V2 Ultra+）：
#   同一台鼠标有三种硬件身份，能力完全不同，必须按形态如实分流：
#     2.4G 接收器 / 有线 USB → 3837:1018（有 0x4D 厂商通道，DPI 与回报率可读写）
#     蓝牙 BLE (HID over GATT) → 3837:4026（无厂商通道，只有电量）
#
# 本测试锁定四件事：
#   A. 源码层：BLE 身份解析、电量读取、能力降级、卡片去重全部就位
#   B. 运行时：BTHLE 实例 ID 的 VID/PID 与 MAC 解析必须精准
#   C. 运行时：PopulateCapabilities 必须按 Transport 分流，BLE 如实降级
#   D. 运行时：BLE 在线时不得再去轮询 0x4D 通道（否则每轮白卡 4.2 秒）
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
$legacyPath = Join-Path $root 'src\Hardware\MchoseMouseHelper.cs'
$driversPath = Join-Path $root 'src\Hardware\BuiltInDrivers.cs'
$mgrPath = Join-Path $root 'src\Hardware\DeviceManager.cs'
$uiPath = Join-Path $root 'src\UI\Forms\MainForm.Hardware.cs'

foreach ($p in @($helperPath, $legacyPath, $driversPath, $mgrPath, $uiPath)) {
    Check ((Split-Path -Leaf $p) + ' exists') (Test-Path $p) $p
    $b = [System.IO.File]::ReadAllBytes($p)
    $bom = ($b.Length -ge 3) -and ($b[0] -eq 0xEF) -and ($b[1] -eq 0xBB) -and ($b[2] -eq 0xBF)
    Check ((Split-Path -Leaf $p) + ' is UTF-8 with BOM') $bom 'missing BOM'
    $t = [System.Text.Encoding]::UTF8.GetString($b)
    Check ((Split-Path -Leaf $p) + ' no CRCRLF corruption') (-not $t.Contains([string][char]13 + [string][char]13 + [string][char]10)) 'found CRCRLF'
    Check ((Split-Path -Leaf $p) + ' keeps AI directive') ($t.Contains([char]0x26A0)) 'missing directive'
    # 注释行会合法地提到被禁止的语法（铁律说明本身），必须只校验可执行代码行。
    $codeOnly = (($t -split "`r`n") | Where-Object { $_ -notmatch '^\s*//' }) -join "`r`n"
    Check ((Split-Path -Leaf $p) + ' no C# 6 interpolation') (-not [regex]::IsMatch($codeOnly, '\$".*?"')) 'found $"..."'
    Check ((Split-Path -Leaf $p) + ' no null-conditional operator') (-not [regex]::IsMatch($codeOnly, '\w+\?\.')) 'found ?.'
    Check ((Split-Path -Leaf $p) + ' no nameof') (-not [regex]::IsMatch($codeOnly, 'nameof\s*\(')) 'found nameof'
}

$helperSrc = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($helperPath))
$legacySrc = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($legacyPath))
$driversSrc = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($driversPath))
$mgrSrc = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($mgrPath))
$uiSrc = [System.Text.Encoding]::UTF8.GetString([System.IO.File]::ReadAllBytes($uiPath))

# ------------------------------------------------------- B. 源码层契约
Check 'BLE PID constant 0x4026' ($helperSrc.Contains('BleProductIdK7V2UltraPlus = 0x4026')) 'missing constant'
Check 'BLE whitelist array declared' ($helperSrc.Contains('SupportedBlePids')) 'missing SupportedBlePids'
Check 'USB whitelist still intact' ($helperSrc.Contains('0x1018')) 'missing 0x1018'

# 物理事实：BLE 形态没有厂商通道，绝不声明 DPI / 回报率。
Check 'BLE transport degrades DPI' ($driversSrc.Contains('dev.Capabilities.SupportsDpi = false;')) 'missing degrade'
Check 'BLE branch keys on 蓝牙 transport' ($driversSrc.Contains('dev.Transport.IndexOf("蓝牙"')) 'missing transport gate'

# 去重：同一台鼠标不得同时出现 2.4G 与 BLE 两张卡。
Check 'duplicate MCHOSE cleanup declared' ($mgrSrc.Contains('public static void CleanupDuplicateMchoseDevices()')) 'missing method'
Check 'duplicate MCHOSE cleanup invoked' ($mgrSrc.Contains('CleanupDuplicateMchoseDevices();')) 'missing call'
Check 'cleanup only removes HID: prefixed ids' ($mgrSrc.Contains('StartsWith("HID:3837:"')) 'missing prefix guard'
Check 'cleanup keeps native MCHOSE: records' (-not $mgrSrc.Contains('StartsWith("MCHOSE:3837:"')) 'must not delete native records'

# 性能：BLE 在线时必须先返回，不再去撞 0x4D 通道的 8 次忙重试。
$bleIdx = $legacySrc.IndexOf('MchoseMouse4DHelper.QueryMchose4DMouseBle()')
$usbIdx = $legacySrc.IndexOf('MchoseMouse4DHelper.QueryMchose4DMouse()')
Check 'BLE probe precedes USB 4D probe' (($bleIdx -ge 0) -and ($usbIdx -gt $bleIdx)) ('ble=' + $bleIdx + ' usb=' + $usbIdx)
Check 'USB 4D probe guarded by !bleOnline' ($legacySrc.Contains('if (!bleOnline)')) 'missing guard'

# UI：BLE 形态必须落入 Generic Mouse 分支（与上游对能力缺失鼠标的既有处理一致）。
# 铁律 12：UI 层零品牌硬编码，门禁必须读取能力契约，不得写 Brand == "xxx"。
Check 'UI gates console by capability contract' ($uiSrc.Contains('supportsMouseTuning')) 'missing capability gate'
Check 'UI capability gate reads DPI/stage/rate flags' ($uiSrc.Contains('SupportsDpi || info.Capabilities.SupportsDpiStages || info.Capabilities.SupportsPollingRate')) 'gate must read capability flags'
Check 'UI has no MCHOSE brand hardcode' (-not $uiSrc.Contains('isMchoseBrandMouse')) 'brand hardcode should be removed'
Check 'UI has no BLE transport string gate' (-not $uiSrc.Contains('isMchoseMouseBle')) 'transport-string gate should be removed'

# ------------------------------------------------------- C. 运行时契约
$binPath = Join-Path $root 'FerrisPulse.exe'
if (-not (Test-Path $binPath)) {
    Write-Host '重新编译以供测试...' -ForegroundColor Yellow
    cmd /c (Join-Path $root 'build.bat') | Out-Null
}
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($binPath))
$t4d = $asm.GetType('RazerBatteryTray.MchoseMouse4DHelper')
Check 'MchoseMouse4DHelper type loaded' ($t4d -ne $null) 'type missing'

$flags = [System.Reflection.BindingFlags]'Public,Static'
foreach ($m in @('IsSupportedBlePid', 'TryParseBleVidPid', 'TryParseBleMacFromServiceNode',
                 'EnumerateBleDeviceMacs', 'TryFindOnlineBleDevice', 'ReadBleBattery',
                 'QueryMchose4DMouseBle')) {
    Check ('method ' + $m + ' present') ($t4d.GetMethod($m, $flags) -ne $null) 'missing method'
}

# C1. BLE PID 白名单：4026 属于蓝牙形态；USB 侧的 1018 / 3033 不属于。
$isBlePid = $t4d.GetMethod('IsSupportedBlePid', $flags)
Check 'BLE whitelist accepts 0x4026' ([bool]$isBlePid.Invoke($null, @(0x4026))) '4026 rejected'
Check 'BLE whitelist rejects USB 0x1018' (-not [bool]$isBlePid.Invoke($null, @(0x1018))) '1018 wrongly accepted'
Check 'BLE whitelist rejects keyboard 0x3033' (-not [bool]$isBlePid.Invoke($null, @(0x3033))) '3033 wrongly accepted'

# C2. 实例 ID 解析：真实抓取的 BTHLEDEVICE 格式（VID/PID 带两位序号前缀）。
$parseVidPid = $t4d.GetMethod('TryParseBleVidPid', $flags)
$bleId = 'BTHLEDEVICE\{00001812-0000-1000-8000-00805F9B34FB}_DEV_VID&023837_PID&4026_REV&0100_C19A783235F4\9&27EA35C3&0&0010'
[object[]]$a1 = New-Object object[] 3
$a1[0] = $bleId; $a1[1] = 0; $a1[2] = 0
$okParse = [bool]$parseVidPid.Invoke($null, $a1)
Check 'parse BLE instance id succeeds' $okParse 'parse failed'
Check 'parsed VID = 0x3837' ($a1[1] -eq 0x3837) ('actual 0x' + ([int]$a1[1]).ToString('X4'))
Check 'parsed PID = 0x4026' ($a1[2] -eq 0x4026) ('actual 0x' + ([int]$a1[2]).ToString('X4'))

[object[]]$a2 = New-Object object[] 3
$a2[0] = 'BTHLE\DEV_C19A783235F4\8&71E4D4F&0&C19A783235F4'; $a2[1] = 0; $a2[2] = 0
Check 'plain BTHLE parent id rejected (no VID/PID)' (-not [bool]$parseVidPid.Invoke($null, $a2)) 'should reject'

[object[]]$a3 = New-Object object[] 3
$a3[0] = ''; $a3[1] = 0; $a3[2] = 0
Check 'empty instance id rejected' (-not [bool]$parseVidPid.Invoke($null, $a3)) 'should reject'

# C3. MAC 解析：必须取最后一个下划线之后、最后一个反斜杠之前的 12 位。
$parseMac = $t4d.GetMethod('TryParseBleMacFromServiceNode', $flags)
$mac = [string]$parseMac.Invoke($null, @($bleId))
Check 'MAC parsed from service node' ($mac -eq 'C19A783235F4') ('actual ' + $mac)
Check 'MAC is 12 hex chars' (($mac -ne $null) -and ($mac.Length -eq 12)) 'bad length'

$badMacId = 'BTHLEDEVICE\{00001812-0000-1000-8000-00805F9B34FB}_DEV_VID&023837_PID&4026_REV&0100_SHORT\9&27EA35C3&0&0010'
Check 'short MAC rejected' ([string]::IsNullOrEmpty([string]$parseMac.Invoke($null, @($badMacId)))) 'should reject'

# C4. 读不到电量时必须返回 -1，绝不拿缓存值冒充实时值。
$readBatt = $t4d.GetMethod('ReadBleBattery', $flags)
Check 'unknown node returns -1 battery' ([int]$readBatt.Invoke($null, @('BTHLE\DEV_000000000000\0&0&0&000000000000')) -eq -1) 'must be -1'
Check 'empty node returns -1 battery' ([int]$readBatt.Invoke($null, @('')) -eq -1) 'must be -1'

# C5. 能力分流：BLE 形态如实降级，2.4G / 有线形态能力齐全。
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

$devBle = New-Dev 'MCHOSE:3837:4026' '蓝牙 BLE'
$drv.PopulateCapabilities($devBle)
Check 'BLE keeps battery capability' ($devBle.Capabilities.SupportsBattery) 'battery lost'
Check 'BLE reports no DPI' (-not $devBle.Capabilities.SupportsDpi) 'DPI wrongly enabled'
Check 'BLE reports no DPI stages' (-not $devBle.Capabilities.SupportsDpiStages) 'stages wrongly enabled'
Check 'BLE reports no polling rate' (-not $devBle.Capabilities.SupportsPollingRate) 'rate wrongly enabled'
Check 'BLE reports no XY independence' (-not $devBle.Capabilities.SupportsXyIndependence) 'XY wrongly enabled'
Check 'BLE reports no hardware DPI monitor' (-not $devBle.Capabilities.SupportsHardwareDpiMonitor) 'monitor wrongly enabled'
Check 'BLE polling rate table empty' ($devBle.Capabilities.SupportedPollingRates.Length -eq 0) 'table not empty'
Check 'BLE sensor note mentions no vendor channel' ($devBle.Capabilities.SensorDescription.Contains('蓝牙')) 'note missing'

$dev24 = New-Dev 'MCHOSE:3837:1018' '2.4G 无线'
$drv.PopulateCapabilities($dev24)
Check '2.4G keeps DPI capability' ($dev24.Capabilities.SupportsDpi) 'DPI lost'
Check '2.4G keeps polling rate capability' ($dev24.Capabilities.SupportsPollingRate) 'rate lost'
Check '2.4G keeps DPI stages capability' ($dev24.Capabilities.SupportsDpiStages) 'stages lost'
Check '2.4G max DPI is 42000' ($dev24.Capabilities.MaxDpi -eq 42000) ('actual ' + $dev24.Capabilities.MaxDpi)

$devWired = New-Dev 'MCHOSE:3837:1018' 'USB 有线'
$drv.PopulateCapabilities($devWired)
Check 'wired keeps DPI capability' ($devWired.Capabilities.SupportsDpi) 'DPI lost'
Check 'wired keeps polling rate capability' ($devWired.Capabilities.SupportsPollingRate) 'rate lost'

# C6. 跨形态聚焦记忆：同一台鼠标的 2.4G 与蓝牙形态必须归并到同一物理设备键，
#     否则用户在 2.4G 下记住的卡片切到蓝牙后会失配，启动时回落到键盘视图。
$getKey = $t4d.GetMethod('GetPhysicalDeviceKey', $flags)
Check 'physical key: USB 1018 stable' `
    ([string]$getKey.Invoke($null, @('MCHOSE:3837:1018')) -eq 'MCHOSE:3837:1018') 'key drifted'
Check 'physical key: BLE 4026 folds to 1018' `
    ([string]$getKey.Invoke($null, @('MCHOSE:3837:4026')) -eq 'MCHOSE:3837:1018') 'not folded'
Check 'physical key: 2.4G and BLE identical' `
    ([string]$getKey.Invoke($null, @('MCHOSE:3837:4026')) -eq [string]$getKey.Invoke($null, @('MCHOSE:3837:1018'))) 'keys differ'
Check 'physical key: unrelated PID untouched' `
    ([string]$getKey.Invoke($null, @('MCHOSE:3837:3033')) -eq 'MCHOSE:3837:3033') 'over-folded'
Check 'physical key: other brand untouched' `
    ([string]$getKey.Invoke($null, @('Razer:1532:007E')) -eq 'Razer:1532:007E') 'cross-brand folded'
Check 'physical key: null safe' `
    ([string]::IsNullOrEmpty([string]$getKey.Invoke($null, @($null)))) 'null crashed'

# 聚焦记忆必须真的走跨形态匹配，而不是只加了个没人调用的工具函数。
$hwSrc = [System.IO.File]::ReadAllText((Join-Path $root 'src\UI\Forms\MainForm.Hardware.cs'))
Check 'FindRememberedDevice uses physical key' `
    ($hwSrc.Contains('GetPhysicalDeviceKey(lastSelectedDeviceId)')) 'not wired into focus memory'
Check 'focus memory still requires online device' `
    ($hwSrc.Contains('(d.IsConnected || d.IsDonglePresent)')) 'online guard lost'

# ------------------------------------------------------- 汇总
Write-Host ''
if ($script:fail -gt 0) {
    Write-Host ('>>> 迈从 4D 蓝牙 BLE 形态测试失败 ' + $script:fail + ' 项 <<<') -ForegroundColor Red
    exit 1
}
Write-Host '>>> 迈从 4D 协议族蓝牙 BLE 形态自动化契约测试 100% 通过！ <<<' -ForegroundColor Green
exit 0
