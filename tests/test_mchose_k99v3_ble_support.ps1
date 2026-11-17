# tests\test_mchose_k99v3_ble_support.ps1
# ============================================================================
# 迈从 MCHOSE K99 V3 蓝牙 BLE 形态（公版 VID 07D7 / PID EFFF）识别与电量契约
#
# 背景（实机抓包，2026-10-31）：
#   同一把 K99 V3 键盘有三个互不相同的硬件身份：
#     0x3033 = 2.4G 接收器通道（迈从自有 VID 3837）
#     0x2020 = USB 有线直连（迈从自有 VID 3837）
#     07D7:EFFF = 蓝牙 BLE（**公版** VID/PID，不是迈从自有 VID）
#   蓝牙形态的实例 ID：
#     BTHLEDEVICE\{0000180F-...}_DEV_VID&0107D7_PID&EFFF_REV&0120_<MAC>\9&338F4FA&0&001F
#   Windows 会把 VID/PID 前补两位序号（0107D7 → 07D7），必须只取末 4 位。
#
#   关键陷阱：07D7:EFFF 是公版 ID，可能被大量蓝牙键盘共用，
#   因此绝不能只凭 VID/PID 认设备，必须叠加 FriendlyName 的 "MCHOSE" 校验。
#
#   电量与身份分挂在两个节点上（两段式，缺一不可）：
#     BTHLEDEVICE\..._DEV_VID&PID_<MAC>\...  有 VID/PID，无电量
#     BTHLE\DEV_<MAC>\...                    有电量与名字，无 VID/PID
#
# 断言项：
#   1. 公版 VID/PID 解析：0107D7 → 0x07D7，EFFF → 0xEFFF（只取末 4 位）
#   2. MAC 解析：取最后一个反斜杠前、该段最后一个下划线后的 12 位
#   3. 名字归一化：末尾 "-N" 槽位后缀必须去掉（MCHOSE K99 V3-1 → MCHOSE K99 V3）
#   4. 蓝牙形态能力如实降级：DPI / 回报率全部为 false
#   5. DeviceId 跨三形态稳定（2.4G / 有线 / 蓝牙同一 ID）
#   6. 蓝牙无充电位，IsCharging 恒 false
#   7. 蓝牙在线时跳过 USB 探测（性能守卫）
#   8. 文件编码（BOM）与 C# 5.0 语法合规
# ============================================================================
$ErrorActionPreference = "Stop"
$ProjectDir = Split-Path -Parent $PSScriptRoot
if (-not $ProjectDir) { $ProjectDir = (Get-Location).Path }

$pass = 0
$fail = 0
function Check([string]$name, [bool]$ok, [string]$detail) {
    if ($ok) {
        $script:pass++
        Write-Host ("  [PASS] " + $name) -ForegroundColor Green
    } else {
        $script:fail++
        Write-Host ("  [FAIL] " + $name + "  " + $detail) -ForegroundColor Red
    }
}

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [1/7] C# 源文件 BOM 与 C# 5.0 语法合规" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$srcFiles = @("src\Hardware\MchoseDeviceHelper.cs")
foreach ($rel in $srcFiles) {
    $full = Join-Path $ProjectDir $rel
    $bytes = [System.IO.File]::ReadAllBytes($full)
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    Check ("BOM: " + $rel) $hasBom "缺少 UTF-8 BOM"
}

$helperPath = Join-Path $ProjectDir "src\Hardware\MchoseDeviceHelper.cs"
$helperSrc = [System.IO.File]::ReadAllText($helperPath, [System.Text.Encoding]::UTF8)
$codeLines = @()
foreach ($line in ($helperSrc -split "`r?`n")) {
    $trimmed = $line.Trim()
    if ($trimmed.StartsWith("//")) { continue }
    $codeLines += $line
}
$codeOnly = $codeLines -join "`n"
$dq = [string][char]34
Check 'C#5: no string interpolation' (-not $codeOnly.Contains('$' + $dq)) 'found C#6 interpolation'
Check 'C#5: no null-conditional' (-not ($codeOnly -match [regex]::Escape('?.') + '[A-Za-z_(]')) 'found C#6 null-conditional'
Check 'C#5: no nameof' (-not $codeOnly.Contains('nameof(')) 'found C#6 nameof'
Check 'C#5: no expression-bodied member' (-not $codeOnly.Contains(') =>')) 'found C#6 expression body'

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [2/7] 加载程序集（全内存反射，严禁 LoadFrom/LoadFile）" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$binPath = Join-Path $ProjectDir "FerrisPulse.exe"
if (-not (Test-Path $binPath)) {
    Write-Host "  FAILED: FerrisPulse.exe 不存在，请先运行 build.bat" -ForegroundColor Red
    exit 1
}
$asmBytes = [System.IO.File]::ReadAllBytes($binPath)
$asm = [System.Reflection.Assembly]::Load($asmBytes)
$helperType = $asm.GetType("RazerBatteryTray.MchoseDeviceHelper")
Check "反射加载 MchoseDeviceHelper" ($null -ne $helperType) "类型未找到"
if ($null -eq $helperType) { exit 1 }

$flags = [System.Reflection.BindingFlags]::Public -bor [System.Reflection.BindingFlags]::Static

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [3/7] BLE 公版 VID/PID 常量与解析" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$fVid = $helperType.GetField("BleVendorIdK99V3", $flags)
$fPid = $helperType.GetField("BlePidK99V3", $flags)
Check "常量 BleVendorIdK99V3 存在" ($null -ne $fVid) "字段缺失"
Check "常量 BlePidK99V3 存在" ($null -ne $fPid) "字段缺失"
if ($null -ne $fVid) {
    Check "BLE VID = 0x07D7（公版，非迈从 0x3837）" ([int]$fVid.GetValue($null) -eq 0x07D7) ("实际=" + $fVid.GetValue($null))
}
if ($null -ne $fPid) {
    Check "BLE PID = 0xEFFF" ([int]$fPid.GetValue($null) -eq 0xEFFF) ("实际=" + $fPid.GetValue($null))
}

$parseVidPid = $helperType.GetMethod("TryParseBleVidPid", $flags)
Check "方法 TryParseBleVidPid 存在" ($null -ne $parseVidPid) "方法缺失"
if ($null -ne $parseVidPid) {
    $bleId = 'BTHLEDEVICE\{0000180F-0000-1000-8000-00805F9B34FB}_DEV_VID&0107D7_PID&EFFF_REV&0120_DC80652107DF\9&338F4FA&0&001F'
    $a = @($bleId, 0, 0)
    $ok = $parseVidPid.Invoke($null, $a)
    Check "解析 K99 BLE 实例 ID 成功" ([bool]$ok) "返回 false"
    Check "VID 只取末 4 位（0107D7 → 0x07D7）" ([int]$a[1] -eq 0x07D7) ("实际=0x" + ('{0:X4}' -f [int]$a[1]))
    Check "PID = 0xEFFF" ([int]$a[2] -eq 0xEFFF) ("实际=0x" + ('{0:X4}' -f [int]$a[2]))

    $k7Id = 'BTHLEDEVICE\{00001812-0000-1000-8000-00805F9B34FB}_DEV_VID&023837_PID&4026_REV&0100_C19A783235F4\9&27EA35C3&0&001C'
    $b = @($k7Id, 0, 0)
    $ok2 = $parseVidPid.Invoke($null, $b)
    Check "解析 K7 BLE 实例 ID 成功（回归）" ([bool]$ok2) "返回 false"
    Check "K7 VID = 0x3837（末 4 位）" ([int]$b[1] -eq 0x3837) ("实际=0x" + ('{0:X4}' -f [int]$b[1]))
    Check "K7 PID = 0x4026" ([int]$b[2] -eq 0x4026) ("实际=0x" + ('{0:X4}' -f [int]$b[2]))

    $noVid = @('BTHLE\DEV_DC80652107DF\8&71E4D4F&0&DC80652107DF', 0, 0)
    Check "无 VID&PID 的父节点被拒绝" (-not [bool]$parseVidPid.Invoke($null, $noVid)) "本应返回 false"
    $empty = @('', 0, 0)
    Check "空字符串被拒绝" (-not [bool]$parseVidPid.Invoke($null, $empty)) "本应返回 false"
}

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [4/7] BLE MAC 解析与名字归一化" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$parseMac = $helperType.GetMethod("TryParseBleMac", $flags)
Check "方法 TryParseBleMac 存在" ($null -ne $parseMac) "方法缺失"
if ($null -ne $parseMac) {
    $bleId = 'BTHLEDEVICE\{0000180F-0000-1000-8000-00805F9B34FB}_DEV_VID&0107D7_PID&EFFF_REV&0120_DC80652107DF\9&338F4FA&0&001F'
    Check "从服务节点取出 K99 MAC" ([string]$parseMac.Invoke($null, @($bleId)) -eq 'DC80652107DF') ("实际=" + $parseMac.Invoke($null, @($bleId)))
    $k7Id = 'BTHLEDEVICE\{00001812-0000-1000-8000-00805F9B34FB}_DEV_VID&023837_PID&4026_REV&0100_C19A783235F4\9&27EA35C3&0&001C'
    Check "从服务节点取出 K7 MAC" ([string]$parseMac.Invoke($null, @($k7Id)) -eq 'C19A783235F4') ("实际=" + $parseMac.Invoke($null, @($k7Id)))
    Check "父节点实例 ID 不得被当成服务节点" ($null -eq $parseMac.Invoke($null, @('BTHLE\DEV_DC80652107DF\8&71E4D4F&0&DC80652107DF'))) "本应返回 null"
    Check "空字符串返回 null" ($null -eq $parseMac.Invoke($null, @(''))) "本应返回 null"
}

$normName = $helperType.GetMethod("NormalizeBleKeyboardName", $flags)
Check "方法 NormalizeBleKeyboardName 存在" ($null -ne $normName) "方法缺失"
if ($null -ne $normName) {
    Check "去掉 -1 槽位后缀" ([string]$normName.Invoke($null, @('MCHOSE K99 V3-1')) -eq 'MCHOSE K99 V3') ("实际=" + $normName.Invoke($null, @('MCHOSE K99 V3-1')))
    Check "无后缀时保持原样" ([string]$normName.Invoke($null, @('MCHOSE K99 V3')) -eq 'MCHOSE K99 V3') ("实际=" + $normName.Invoke($null, @('MCHOSE K99 V3')))
    Check "带 + 的型号不受影响" ([string]$normName.Invoke($null, @('MCHOSE K7 V2 Ultra+')) -eq 'MCHOSE K7 V2 Ultra+') ("实际=" + $normName.Invoke($null, @('MCHOSE K7 V2 Ultra+')))
    Check "空输入返回 null" ($null -eq $normName.Invoke($null, @(''))) "本应返回 null"
}

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [5/7] 蓝牙查询真机契约（能力降级 / 充电位 / DeviceId）" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$queryBle = $helperType.GetMethod("QueryMchoseK99V3Ble", $flags)
Check "方法 QueryMchoseK99V3Ble 存在" ($null -ne $queryBle) "方法缺失"
if ($null -ne $queryBle) {
    $dev = $queryBle.Invoke($null, @())
    if ($null -eq $dev) {
        Write-Host "  [SKIP] 当前无在线 BLE 键盘，跳过真机断言" -ForegroundColor Yellow
    } else {
        Check "BLE 卡片 Category = Keyboard" ([string]$dev.Category -eq 'Keyboard') ("实际=" + $dev.Category)
        Check "BLE 卡片 Brand = MCHOSE" ([string]$dev.Brand -eq 'MCHOSE') ("实际=" + $dev.Brand)
        Check "BLE 传输形态标注蓝牙" ([string]$dev.Transport -like '*蓝牙*') ("实际=" + $dev.Transport)
        Check "BLE 电量在 0-100 内" ([int]$dev.BatteryPercent -ge 0 -and [int]$dev.BatteryPercent -le 100) ("实际=" + $dev.BatteryPercent)
        Check "BLE 无充电位时 IsCharging 恒 false" ([bool]$dev.IsCharging -eq $false) "BLE 无充电标志，不得猜测"
        Check "BLE DeviceId 用 2.4G 型号身份（跨形态稳定）" ([string]$dev.DeviceId -eq 'MCHOSE:3837:3033') ("实际=" + $dev.DeviceId)
        Check "BLE 能力：SupportsDpi = false" ([bool]$dev.Capabilities.SupportsDpi -eq $false) "蓝牙无厂商通道"
        Check "BLE 能力：SupportsPollingRate = false" ([bool]$dev.Capabilities.SupportsPollingRate -eq $false) "蓝牙无厂商通道"
        Check "BLE 能力：SupportsBattery = true" ([bool]$dev.Capabilities.SupportsBattery -eq $true) "电量可读"
        Check "BLE DPI 值为 0（无厂商通道）" ([int]$dev.Dpi -eq 0) ("实际=" + $dev.Dpi)
        Check "BLE 回报率值为 0（无厂商通道）" ([int]$dev.PollingRate -eq 0) ("实际=" + $dev.PollingRate)
        Check "BLE 无接收器标记" ([bool]$dev.IsDonglePresent -eq $false) "BLE 不经接收器"
    }
}

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [6/7] 蓝牙优先守卫与去重（源码契约）" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

Check "主查询先探 BLE" ($helperSrc.Contains('MouseBatteryInfo bleDevice = QueryMchoseK99V3Ble();')) "未接入主查询流程"
Check "BLE 在线时直接返回（跳过 USB 探测）" ($helperSrc.Contains('if (bleDevice != null && bleDevice.IsConnected)')) "缺少 BLE 优先守卫"
Check "公版 VID 需叠加 MCHOSE 名字校验" ($helperSrc.Contains('BleNameToken')) "缺少公版 VID 名字校验"
Check "两段式：先取 MAC 再读电量" ($helperSrc.Contains('TryFindMchoseBleKeyboardMac') -and $helperSrc.Contains('FindBleParentNodeId')) "缺少两段式读取"
Check "BLE 电量按单字节读取" ($helperSrc.Contains('int value = (int)battBuf[0];')) "未按 DEVPROP_TYPE_BYTE 读取"

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [7/7] 跨形态 DeviceId 归并（三形态同一 ID）" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$allQuery = $helperType.GetMethod("QueryAllMchoseDevices", $flags)
Check "方法 QueryAllMchoseDevices 存在" ($null -ne $allQuery) "方法缺失"
if ($null -ne $allQuery) {
    $list = $allQuery.Invoke($null, @())
    Check "键盘卡数量不超过 1（无重复卡片）" ($list.Count -le 1) ("实际=" + $list.Count)
    foreach ($d in $list) {
        Check ("键盘 DeviceId 固定为型号身份: " + $d.DeviceId) ([string]$d.DeviceId -eq 'MCHOSE:3837:3033') "跨形态 DeviceId 必须稳定"
    }
}

Write-Host "`n==========================================================" -ForegroundColor Cyan
if ($fail -eq 0) {
    Write-Host (" 全部 " + $pass + " 项 K99 V3 蓝牙 BLE 契约测试通过！") -ForegroundColor Green
} else {
    Write-Host (" " + $fail + " 项失败 / 共 " + ($pass + $fail) + " 项") -ForegroundColor Red
}
Write-Host "==========================================================" -ForegroundColor Cyan

if ($fail -gt 0) { exit 1 }
exit 0
