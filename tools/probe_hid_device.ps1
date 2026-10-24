# ============================================================================
# 脚本名称: probe_hid_device.ps1
# 所属区域: tools (自动化工具链 / 硬件现场探针)
# 核心职责:
#   零依赖 Win32 HID 现场探针工具。供智能体或工程师对任意外设的物理端点
#   进行直接探测、下发握手 Hex 报文、捕获真实 RX 回包并分析潜在电量/充电偏移。
#   支持 Interrupt/Feature 双模通信、任意 N 字节长度与权限三级降级保护。
#
# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
# 1. 严格使用 UTF-8 with BOM 编码保存。
# 2. 严禁使用 Add-Type -Path 锁死二进制，加载程序集必须采用 ReadAllBytes 全内存反射。
# ============================================================================

[CmdletBinding()]
param(
    [Parameter(Position = 0)]
    [string]$VendorId,

    [Parameter(Position = 1)]
    [string]$ProductId,

    [string]$UsagePage,
    [string]$Usage,
    [string]$TxHex,
    [ValidateSet("Interrupt", "Feature")]
    [string]$ReportType = "Interrupt",
    [int]$ReportLength = 0,
    [int]$TimeoutMs = 500,
    [switch]$AllowUnsafeWrite,
    [switch]$ListOnly,
    [switch]$ScanAll
)

$ErrorActionPreference = 'Stop'
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = (Resolve-Path "$ScriptDir\..").Path
Set-Location $ProjectDir

# --- 1. 全内存反射加载 FerrisPulse 核心程序集 ---
$exePath = Join-Path $ProjectDir "FerrisPulse.exe"
if (-not (Test-Path $exePath)) {
    Write-Host "[!] FerrisPulse.exe 未编译，正在通过 build.bat 快速编译..." -ForegroundColor Yellow
    cmd.exe /c "$ProjectDir\build.bat" | Out-Null
    if (-not (Test-Path $exePath)) {
        Write-Error "无法生成 FerrisPulse.exe，请先执行 build.bat 构建项目！"
        exit 1
    }
}

$rawBytes = [System.IO.File]::ReadAllBytes($exePath)
[System.Reflection.Assembly]::Load($rawBytes) | Out-Null

function Parse-HexValue([string]$val) {
    if ([string]::IsNullOrEmpty($val)) { return 0 }
    $clean = $val.Trim()
    if ($clean.StartsWith("0x", [System.StringComparison]::OrdinalIgnoreCase)) {
        $clean = $clean.Substring(2)
    }
    try {
        return [Convert]::ToInt32($clean, 16)
    } catch {
        return [int]$val
    }
}

function Parse-HexBuffer([string]$hexStr) {
    if ([string]::IsNullOrEmpty($hexStr)) { return @() }
    $clean = $hexStr.Trim() -replace '0x', '' -replace '[,;\s]+', ' '
    $tokens = $clean.Split(' ', [System.StringSplitOptions]::RemoveEmptyEntries)
    $bytes = New-Object byte[] $tokens.Length
    for ($i = 0; $i -lt $tokens.Length; $i++) {
        $bytes[$i] = [Convert]::ToByte($tokens[$i], 16)
    }
    return $bytes
}

function Format-HexDump([byte[]]$buffer) {
    if ($null -eq $buffer -or $buffer.Length -eq 0) { return "  (空缓冲区)" }
    $sb = New-Object System.Text.StringBuilder
    $hexLine = ""
    $ascLine = ""
    for ($i = 0; $i -lt $buffer.Length; $i++) {
        if ($i % 16 -eq 0) {
            if ($i -gt 0) {
                $sb.AppendLine(("{0,-50} | {1}" -f $hexLine, $ascLine)) | Out-Null
                $hexLine = ""
                $ascLine = ""
            }
            $hexLine += ("{0:X4}: " -f $i)
        }
        $hexLine += ("{0:X2} " -f $buffer[$i])
        $ch = [char]$buffer[$i]
        if ($buffer[$i] -ge 32 -and $buffer[$i] -le 126) {
            $ascLine += $ch
        } else {
            $ascLine += "."
        }
    }
    if ($hexLine.Length -gt 0) {
        $sb.AppendLine(("{0,-50} | {1}" -f $hexLine, $ascLine)) | Out-Null
    }
    return $sb.ToString().TrimEnd()
}

$targetVid = Parse-HexValue $VendorId
$targetPid = Parse-HexValue $ProductId
$targetUp  = Parse-HexValue $UsagePage
$targetU   = Parse-HexValue $Usage

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host " 🚀 灵脉 (FerrisPulse) 通用 HID 硬件现场探针工具" -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan

# 硬件安全锁：检测到高危 EEPROM / Flash 内存写入指令 (如 0x07)，严禁盲目下发
if (-not [string]::IsNullOrEmpty($TxHex)) {
    $preBytes = Parse-HexBuffer $TxHex
    if ($preBytes.Length -ge 2 -and ($preBytes[1] -eq 0x07 -or $preBytes[0] -eq 0x07) -and -not $AllowUnsafeWrite) {
        Write-Host "[!] 硬件安全锁：检测到疑似高危 EEPROM / Flash 内存写入指令 (0x07)！" -ForegroundColor Red
        Write-Host "    盲目下发该指令极易冲垮 2.4G 接收机 RF 配对表或硬件校验，导致固件损坏变砖！" -ForegroundColor Red
        Write-Host "    若该指令通过官方 WebHID 源码反编译严格核对，确实需要测试，请显式追加参数: -AllowUnsafeWrite" -ForegroundColor Yellow
        exit 2
    }
}

# --- 2. 设备端点枚举 ---
if ($ScanAll -or ($targetVid -eq 0 -and $targetPid -eq 0)) {
    Write-Host "`n[*] 正在扫描系统当前连接的所有 HID 设备..." -ForegroundColor Yellow
    $devices = [RazerBatteryTray.HidTransport]::Enumerate(0, 0)
    if ($null -eq $devices -or $devices.Count -eq 0) {
        Write-Host "[-] 未探测到任何 HID 设备！" -ForegroundColor Red
        exit 0
    }

    Write-Host ("[+] 发现 {0} 个 HID 设备/端点集合:" -f $devices.Count) -ForegroundColor Green
    $grouped = $devices | Group-Object { "{0:X4}:{1:X4}" -f $_.VendorId, $_.ProductId }
    foreach ($grp in $grouped) {
        $first = $grp.Group[0]
        $prod = if ($first.ProductString) { $first.ProductString } else { "未知产品" }
        $mfg  = if ($first.ManufacturerString) { $first.ManufacturerString } else { "未知厂商" }
        Write-Host ("  ► VID:PID = {0} | {1} - {2} ({3} 个端点)" -f $grp.Name, $mfg, $prod, $grp.Group.Count) -ForegroundColor White
        foreach ($d in $grp.Group) {
            Write-Host ("     ├─ UsagePage=0x{0:X4}, Usage=0x{1:X4} | InLen={2}, OutLen={3}, FeatLen={4}" -f `
                $d.UsagePage, $d.Usage, $d.InputReportLength, $d.OutputReportLength, $d.FeatureReportLength) -ForegroundColor Gray
        }
    }
    Write-Host "`n[提示] 使用 `-VendorId 0xXXXX -ProductId 0xXXXX` 深入探测具体设备并下发握手指令。" -ForegroundColor Cyan
    exit 0
}

Write-Host ("[*] 目标设备: VID=0x{0:X4}, PID=0x{1:X4}" -f $targetVid, $targetPid) -ForegroundColor White
$descriptors = [RazerBatteryTray.HidTransport]::Enumerate($targetVid, $targetPid)
if ($null -eq $descriptors -or $descriptors.Count -eq 0) {
    Write-Host ("[-] 未找到匹配 VID=0x{0:X4}, PID=0x{1:X4} 的在线设备！请检查连接状态。" -f $targetVid, $targetPid) -ForegroundColor Red
    exit 1
}

Write-Host ("[+] 成功匹配到 {0} 个端点集合:" -f $descriptors.Count) -ForegroundColor Green
for ($i = 0; $i -lt $descriptors.Count; $i++) {
    $d = $descriptors[$i]
    $prod = if ($d.ProductString) { $d.ProductString } else { "未知" }
    $mfg  = if ($d.ManufacturerString) { $d.ManufacturerString } else { "未知" }
    $isVendor = ($d.UsagePage -ge 0xFF00)
    $tag = if ($isVendor) { "[推荐私有控制通道]" } else { "" }
    Write-Host ("  [{0}] UsagePage=0x{1:X4}, Usage=0x{2:X4} | InLen={3}, OutLen={4}, FeatLen={5} {6}" -f `
        ($i + 1), $d.UsagePage, $d.Usage, $d.InputReportLength, $d.OutputReportLength, $d.FeatureReportLength, $tag) -ForegroundColor $(if ($isVendor) { "Magenta" } else { "Gray" })
    Write-Host ("      路径: {0}" -f $d.DevicePath) -ForegroundColor DarkGray
    if ($i -eq 0) {
        Write-Host ("      固件原名: {0} | 制造商: {1}" -f $prod, $mfg) -ForegroundColor DarkGray
    }
}

if ($ListOnly -or [string]::IsNullOrEmpty($TxHex)) {
    Write-Host "`n[提示] 如需下发测试指令，请追加 `-TxHex \"08 02 ...\"` 参数。" -ForegroundColor Cyan
    exit 0
}

# --- 3. 选择通信端点 ---
$targetEndpoint = $null
if ($targetUp -ne 0 -and $targetU -ne 0) {
    foreach ($d in $descriptors) {
        if ($d.UsagePage -eq $targetUp -and $d.Usage -eq $targetU) {
            $targetEndpoint = $d
            break
        }
    }
}

if ($null -eq $targetEndpoint) {
    # 优先选择厂商私有端点 (UsagePage >= 0xFF00) 且支持实际写操作的端点
    foreach ($d in $descriptors) {
        if ($d.UsagePage -ge 0xFF00) {
            if ($ReportType -eq "Feature" -and $d.FeatureReportLength -gt 0) {
                $targetEndpoint = $d
                break
            }
            if ($ReportType -eq "Interrupt" -and $d.OutputReportLength -gt 0) {
                $targetEndpoint = $d
                break
            }
        }
    }
}

if ($null -eq $targetEndpoint) {
    $targetEndpoint = $descriptors[0]
}

Write-Host ("`n[*] 选定通信端点: UsagePage=0x{0:X4}, Usage=0x{1:X4} (InLen={2}, OutLen={3}, FeatLen={4})" -f `
    $targetEndpoint.UsagePage, $targetEndpoint.Usage, $targetEndpoint.InputReportLength, $targetEndpoint.OutputReportLength, $targetEndpoint.FeatureReportLength) -ForegroundColor White

# --- 4. 组装 TX 报文 ---
$txBytes = Parse-HexBuffer $TxHex
$expectedLen = $ReportLength
if ($expectedLen -le 0) {
    if ($ReportType -eq "Feature" -and $targetEndpoint.FeatureReportLength -gt 0) {
        $expectedLen = $targetEndpoint.FeatureReportLength
    } elseif ($ReportType -eq "Interrupt" -and $targetEndpoint.OutputReportLength -gt 0) {
        $expectedLen = $targetEndpoint.OutputReportLength
    } else {
        $expectedLen = $txBytes.Length
    }
}

if ($txBytes.Length -lt $expectedLen) {
    $padded = New-Object byte[] $expectedLen
    [Array]::Copy($txBytes, $padded, $txBytes.Length)
    $txBytes = $padded
} elseif ($txBytes.Length -gt $expectedLen -and $expectedLen -gt 0) {
    $trimmed = New-Object byte[] $expectedLen
    [Array]::Copy($txBytes, $trimmed, $expectedLen)
    $txBytes = $trimmed
}

Write-Host ("[*] 准备发送 TX 报文 ({0} 字节, 模式: {1}):" -f $txBytes.Length, $ReportType) -ForegroundColor Yellow
Write-Host (Format-HexDump $txBytes) -ForegroundColor Gray

# --- 5. 打开句柄并执行物理交互 ---
$handle = [RazerBatteryTray.HidTransport]::Open($targetEndpoint.DevicePath)
if (-not [RazerBatteryTray.HidTransport]::IsValidHandle($handle)) {
    Write-Host "[-] 无法打开设备句柄！可能端点被系统独占或无访问权限。" -ForegroundColor Red
    exit 1
}

$readOk = $false
$inBuffer = $null

try {
    # 排空残留缓冲区
    $drainLen = if ($targetEndpoint.InputReportLength -gt 0) { $targetEndpoint.InputReportLength } else { 64 }
    [RazerBatteryTray.HidTransport]::DrainReports($handle, $drainLen, 3) | Out-Null

    if ($ReportType -eq "Feature") {
        Write-Host "[*] 正在执行 HidD_SetFeature..." -ForegroundColor Yellow
        $writeOk = [RazerBatteryTray.HidTransport]::SetFeature($handle, $txBytes)
        if (-not $writeOk) {
            Write-Host "[-] SetFeature 失败 (Win32 LastError: $([System.Runtime.InteropServices.Marshal]::GetLastWin32Error()))" -ForegroundColor Red
        } else {
            Write-Host "[+] SetFeature 发送成功！" -ForegroundColor Green
        }

        [System.Threading.Thread]::Sleep(35)

        $featLen = if ($targetEndpoint.FeatureReportLength -gt 0) { $targetEndpoint.FeatureReportLength } else { $expectedLen }
        $inBuffer = New-Object byte[] $featLen
        Write-Host "[*] 正在执行 HidD_GetFeature..." -ForegroundColor Yellow
        $readOk = [RazerBatteryTray.HidTransport]::GetFeature($handle, $inBuffer)
    } else {
        Write-Host "[*] 正在执行 Interrupt WriteReport..." -ForegroundColor Yellow
        $writeOk = [RazerBatteryTray.HidTransport]::WriteReport($handle, $txBytes, $txBytes.Length, $TimeoutMs)
        if (-not $writeOk) {
            Write-Host "[-] WriteReport 失败！可能设备休眠或拒绝接收。" -ForegroundColor Red
        } else {
            Write-Host "[+] WriteReport 发送成功！" -ForegroundColor Green
        }

        [System.Threading.Thread]::Sleep(35)

        $inLen = if ($targetEndpoint.InputReportLength -gt 0) { $targetEndpoint.InputReportLength } else { $expectedLen }
        Write-Host "[*] 正在执行 Interrupt ReadReport (超时: ${TimeoutMs}ms)..." -ForegroundColor Yellow
        $readOk = [RazerBatteryTray.HidTransport]::ReadReport($handle, $inLen, $TimeoutMs, [ref]$inBuffer)
    }
} finally {
    [RazerBatteryTray.HidTransport]::Close($handle)
}

# --- 6. 捕获与分析响应结果 ---
if ($readOk -and $null -ne $inBuffer -and $inBuffer.Length -gt 0) {
    Write-Host "`n======================================================================" -ForegroundColor Green
    Write-Host ("🎉 物理回包成功捕获！(共 {0} 字节)" -f $inBuffer.Length) -ForegroundColor Green
    Write-Host "======================================================================" -ForegroundColor Green
    Write-Host (Format-HexDump $inBuffer) -ForegroundColor White

    # 简易启发式电量与状态分析
    Write-Host "`n[*] 潜在电量字节候选索引 (值介于 1~100 之间):" -ForegroundColor Cyan
    $candidateFound = $false
    for ($idx = 0; $idx -lt $inBuffer.Length; $idx++) {
        $val = $inBuffer[$idx]
        if ($val -ge 1 -and $val -le 100) {
            Write-Host ("   ► 索引 [{0}] (0x{0:X2}): 原始值 = 0x{1:X2} (十进制: {1})" -f $idx, $val) -ForegroundColor Yellow
            $candidateFound = $true
        }
    }
    if (-not $candidateFound) {
        Write-Host "   (未发现明显 1~100 的十进制百分比数值，可能需特定公式或倍率换算)" -ForegroundColor Gray
    }

    $rawRxHex = ($inBuffer | ForEach-Object { "{0:X2}" -f $_ }) -join ' '
    $rawTxHex = ($txBytes | ForEach-Object { "{0:X2}" -f $_ }) -join ' '

    Write-Host "`n[*] 可直接用于 FDAS 规范包的实测证据数据:" -ForegroundColor Magenta
    Write-Host ("  sampleTxHex: `"$rawTxHex`"" -f $rawTxHex) -ForegroundColor Gray
    Write-Host ("  sampleRxHex: `"$rawRxHex`"" -f $rawRxHex) -ForegroundColor Green
} else {
    Write-Host "`n[-] 未能收到设备有效物理回包 (读取超时或设备无响应)。" -ForegroundColor Red
    Write-Host "[建议排查方向]:" -ForegroundColor Yellow
    Write-Host " 1. 尝试使用 `-ReportType Feature` 切换通信模式。" -ForegroundColor Gray
    Write-Host " 2. 尝试切换其他 UsagePage/Usage 候选端点。" -ForegroundColor Gray
    Write-Host " 3. 检查 TX 指令尾部是否需要特定校验和（如 VgnCompxSum55、SumMod256、XorSum）。" -ForegroundColor Gray
    Write-Host " 4. 确保无线外设处于唤醒活动状态（晃动鼠标或敲击按键）。" -ForegroundColor Gray
}
