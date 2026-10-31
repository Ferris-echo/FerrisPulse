# tests\test_mchose_k99v3_wired_charging.ps1
# ============================================================================
# 迈从 MCHOSE K99 V3 双形态（2.4G 0x3033 / USB 有线 0x2020）电量与充电状态契约
#
# 背景（实机抓包，2026-10-30）：
#   同一把键盘有两个 USB 硬件身份：
#     0x3033 = 2.4G 接收器通道（开关拨 2.4G 时转发性能帧）
#     0x2020 = USB 有线直连（开关拨 OFF 并插线时才有）
#   两者可同时枚举。0x55 性能帧的关键字节：
#     [40] bit7 = 充电标志，低 7 位 = 电量百分比（复合字节，不是纯百分比）
#     [42] = 传输形态：0x01 = USB 有线，0x02 = 2.4G
#   插着 USB 线给 2.4G 键盘充电时，只有有线端点 (0x2020) 的 bit7 置位；
#   旧实现把 [40] 整字节当百分比（0xC3=195 超出 0-100）→ 整帧判无效 →
#   电量与充电状态同时丢失，卡片退回缓存值并显示"设备已离线"。
#
# 断言项：
#   1. 位域解析：bit7 充电 + 低7位电量；0xC3 → 67% + 充电中
#   2. 形态字节：0x01 有线 / 0x02 2.4G / 其他值判为未知
#   3. 双形态同时存在时，充电状态取两端点并集
#   4. 0x2020 与 0x3033 都进白名单，且 DeviceId 跨形态保持稳定
#   5. 充电中但电量为 0 时不得显示 0%
#   6. 文件编码（BOM）与 C# 5.0 语法合规
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
Write-Host " [1/6] C# 源文件 BOM 与 C# 5.0 语法合规" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$srcFiles = @("src\Hardware\MchoseDeviceHelper.cs", "src\UI\Forms\MainForm.Hardware.cs")
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
# 语法合规检查：用单引号 + Contains，避免正则反斜杠与反引号转义陷阱
$dq = [string][char]34
Check 'C#5: no string interpolation' (-not $codeOnly.Contains('$' + $dq)) 'found C#6 interpolation'
Check 'C#5: no null-conditional' (-not ($codeOnly -match [regex]::Escape('?.') + '[A-Za-z_(]')) 'found C#6 null-conditional'
Check 'C#5: no nameof' (-not $codeOnly.Contains('nameof(')) 'found C#6 nameof'
Check 'C#5: no expression-bodied member' (-not $codeOnly.Contains(') =>')) 'found C#6 expression body'

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [2/6] 加载程序集（全内存反射，严禁 LoadFrom/LoadFile）" -ForegroundColor Cyan
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

$pubStatic = [System.Reflection.BindingFlags]"Public,Static"
$nonPubStatic = [System.Reflection.BindingFlags]"NonPublic,Static"

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [3/6] 复合字节位域解析：bit7 充电 + 低 7 位电量" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$pidWiredField = $helperType.GetField("PidK99V3Wired", $pubStatic)
Check "常量 PidK99V3Wired 存在" ($null -ne $pidWiredField) "字段缺失"
if ($null -ne $pidWiredField) {
    $pidWiredVal = [int]$pidWiredField.GetValue($null)
    Check "PidK99V3Wired == 0x2020" ($pidWiredVal -eq 0x2020) ("实际 0x" + $pidWiredVal.ToString("X4"))
}

$parsePerf = $helperType.GetMethod("ParsePerformanceResponse", $pubStatic)
Check "ParsePerformanceResponse 存在" ($null -ne $parsePerf) "方法缺失"

function Invoke-ParsePerf([int]$combined) {
    [byte[]]$buf = New-Object byte[] 64
    $buf[0] = 0xAA
    $buf[1] = 0x05
    $buf[40] = [byte]$combined
    [object[]]$argv = New-Object object[] 4
    $argv[0] = $buf
    $argv[1] = 64
    $argv[2] = 0
    $argv[3] = $false
    $ok = $parsePerf.Invoke($null, $argv)
    return @{ Ok = [bool]$ok; Battery = [int]$argv[2]; Charging = [bool]$argv[3] }
}

if ($null -ne $parsePerf) {
    # 实机帧值：0xC3 = 67% + 充电中（插着 USB 线时的真实读数）
    $r1 = Invoke-ParsePerf 0xC3
    Check "0xC3 -> 解析成功" $r1.Ok "解析失败"
    Check "0xC3 -> 电量 67%" ($r1.Battery -eq 67) ("实际 " + $r1.Battery)
    Check "0xC3 -> 充电中" ($r1.Charging -eq $true) "充电标志丢失"

    # 0x44 = 68% 纯放电（2.4G 未插线）
    $r2 = Invoke-ParsePerf 0x44
    Check "0x44 -> 电量 68%" ($r2.Battery -eq 68) ("实际 " + $r2.Battery)
    Check "0x44 -> 未充电" ($r2.Charging -eq $false) "误判为充电"

    # 0x4A = 74% 纯放电（实测过得电值）
    $r3 = Invoke-ParsePerf 0x4A
    Check "0x4A -> 电量 74%" ($r3.Battery -eq 74) ("实际 " + $r3.Battery)

    # 0xCA = 74% + 充电（实测过：插回 USB 线瞬间）
    $r4 = Invoke-ParsePerf 0xCA
    Check "0xCA -> 电量 74%" ($r4.Battery -eq 74) ("实际 " + $r4.Battery)
    Check "0xCA -> 充电中" ($r4.Charging -eq $true) "充电标志丢失"

    # 0x80 = 充电中但电量未初始化 -> 低 7 位为 0，必须能解析（不得整帧作废）
    $r5 = Invoke-ParsePerf 0x80
    Check "0x80 -> 解析成功（不得整帧作废）" $r5.Ok "解析失败"
    Check "0x80 -> 充电中" ($r5.Charging -eq $true) "充电标志丢失"

    # 边界：低 7 位 100 是合法上限，101 非法
    $r6 = Invoke-ParsePerf 100
    Check "0x64 -> 电量 100%" ($r6.Ok -and $r6.Battery -eq 100) "边界 100% 解析异常"
    $r7 = Invoke-ParsePerf 101
    Check "101 -> 判为非法帧" ($r7.Ok -eq $false) "超范围电量未拒绝"
}

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [4/6] 传输形态字节 [42]：0x01 有线 / 0x02 2.4G" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$parseTransport = $helperType.GetMethod("TryParsePerformanceTransport", $pubStatic)
Check "TryParsePerformanceTransport 存在" ($null -ne $parseTransport) "方法缺失"

function Invoke-ParseTransport([int]$transportByte) {
    [byte[]]$buf = New-Object byte[] 64
    $buf[0] = 0xAA
    $buf[1] = 0x05
    $buf[40] = 0x43
    $buf[42] = [byte]$transportByte
    [object[]]$argv = New-Object object[] 3
    $argv[0] = $buf
    $argv[1] = 64
    $argv[2] = $false
    $ok = $parseTransport.Invoke($null, $argv)
    return @{ Ok = [bool]$ok; Wired = [bool]$argv[2] }
}

if ($null -ne $parseTransport) {
    $t1 = Invoke-ParseTransport 0x01
    Check "0x01 -> USB 有线" ($t1.Ok -and $t1.Wired) "形态判定错误"
    $t2 = Invoke-ParseTransport 0x02
    Check "0x02 -> 2.4G 无线" ($t2.Ok -and (-not $t2.Wired)) "形态判定错误"
    $t3 = Invoke-ParseTransport 0x00
    Check "0x00 -> 形态未知" ($t3.Ok -eq $false) "未定义值被当作已知形态"
    $t4 = Invoke-ParseTransport 0x03
    Check "0x03 -> 形态未知" ($t4.Ok -eq $false) "未定义值被当作已知形态"
}

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [5/6] PID 白名单与跨形态 DeviceId 稳定性" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$isSupported = $helperType.GetMethod("IsSupportedVidPid", $pubStatic)
Check "IsSupportedVidPid 存在" ($null -ne $isSupported) "方法缺失"
if ($null -ne $isSupported) {
    Check "3837:3033 2.4G 受支持" ([bool]$isSupported.Invoke($null, @([int]0x3837, [int]0x3033))) "白名单缺 3033"
    Check "3837:2020 有线受支持" ([bool]$isSupported.Invoke($null, @([int]0x3837, [int]0x2020))) "白名单缺 2020"
    Check "3837:1018 鼠标未被键盘误认" (-not [bool]$isSupported.Invoke($null, @([int]0x3837, [int]0x1018))) "键盘白名单误纳鼠标 PID"
    Check "3837:4028 鼠标有线未被键盘误认" (-not [bool]$isSupported.Invoke($null, @([int]0x3837, [int]0x4028))) "键盘白名单误纳鼠标 PID"
}

$isKb = $helperType.GetMethod("IsMchoseKeyboard", $pubStatic)
if ($null -ne $isKb) {
    Check "识别 pid_3033 标识" ([bool]$isKb.Invoke($null, @("HID\VID_3837&PID_3033&MI_00"))) "未识别 2.4G"
    Check "识别 pid_2020 标识" ([bool]$isKb.Invoke($null, @("HID\VID_3837&PID_2020&MI_00"))) "未识别有线"
    Check "拒绝 pid_1018 标识" (-not [bool]$isKb.Invoke($null, @("HID\VID_3837&PID_1018&MI_00"))) "误纳鼠标"
}

# DeviceId 必须跨形态稳定，否则切换连接方式时聚焦记忆失效
$helperFull = [System.IO.File]::ReadAllText($helperPath, [System.Text.Encoding]::UTF8)
$deviceIdNeedle = "info.DeviceId = string.Format(" + [char]34 + "MCHOSE:{0:X4}:{1:X4}" + [char]34 + ", VendorIdMchose, PidG87V2)"
$deviceIdHits = 0
if ($helperFull.Contains($deviceIdNeedle)) { $deviceIdHits = 1 }
Check "键盘 DeviceId 固定用 PidG87V2（不随形态变化）" ($deviceIdHits.Count -ge 1) "DeviceId 可能随形态变化"
Check "存在双端点并集充电逻辑" ($helperFull.Contains("wireless.Charging") -and $helperFull.Contains("wired.Charging")) "缺少充电并集"
Check "存在 0x2020 端点查询" ($helperFull.Contains("CollectCandidates(PidK99V3Wired)")) "未查询有线端点"
Check "设备层充电时不采用冻结电量" ($helperFull.Contains("if (charging) battery = -1;")) "充电时未丢弃冻结电量"
Check "设备层充电冻结有实测注释" ($helperFull.Contains("拔掉 USB 线的瞬间立刻跳到")) "缺少充电冻结实测依据注释"
Check "设备层不把冻结值写入硬件缓存" ($helperFull.Contains("if (battery >= 0)")) "未对缓存写入做可信电量守卫"

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [6/6] UI 层形态与充电文案" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$uiPath = Join-Path $ProjectDir "src\UI\Forms\MainForm.Hardware.cs"
$uiSrc = [System.IO.File]::ReadAllText($uiPath, [System.Text.Encoding]::UTF8)
Check "设备层产出 2.4G+USB 充电传输态" ($helperFull.Contains("2.4G 无线 + USB 充电")) "缺少 2.4G+USB 充电传输态"
Check "UI 支持 2.4G+USB 充电状态标" ($uiSrc.Contains("2.4G · USB 充电中")) "缺少 2.4G+USB 充电状态标"
Check "设备层产出 USB 有线+充电传输态" ($helperFull.Contains("USB 有线 + 充电")) "缺少有线充电传输态"
Check "UI 支持 USB 有线充电状态标" ($uiSrc.Contains("USB 有线 · 充电中")) "缺少有线充电状态标"
Check "UI 有线带电量时不藏百分比" ($uiSrc.Contains("wiredWithBattery")) "缺少 wiredWithBattery 分支"
Check "UI 2.4G 充电无电量时区分 USB 充电" ($uiSrc.Contains("usbChargingNoBattery")) "缺少 usbChargingNoBattery 分支"
Check "UI 迈从键盘有线用官方口径" ($uiSrc.Contains("USB 有线充电中")) "缺少迈从键盘有线官方口径文案"
Check "UI 迈从键盘区分纯有线与充电" ($uiSrc.Contains("usbPoweredOnlyCharging")) "缺少 usbPoweredOnlyCharging 能力分支"
Check "UI 读取充电语义能力契约" ($uiSrc.Contains("ChargingFlagMeansUsbPoweredOnly")) "缺少 ChargingFlagMeansUsbPoweredOnly 能力契约读取"
Check "UI 层已移除品牌硬编码" (-not $uiSrc.Contains("isMchoseKb")) "仍残留 isMchoseKb 品牌硬编码"

Write-Host "`n==========================================================" -ForegroundColor Cyan
if ($fail -eq 0) {
    Write-Host (" 全部 " + $pass + " 项 K99 V3 双形态契约测试通过！") -ForegroundColor Green
} else {
    Write-Host (" " + $fail + " 项失败 / 共 " + ($pass + $fail) + " 项") -ForegroundColor Red
}
Write-Host "==========================================================" -ForegroundColor Cyan

if ($fail -gt 0) { exit 1 }
exit 0
