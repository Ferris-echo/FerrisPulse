# ============================================================================
# 脚本名称: probe_vk_m3.ps1
# 所属区域: tools (自动化工具链 / VK M3 现场诊断)
# 核心职责:
#   验证 Valkyrie VK M3 系列（2.4G 接收器 249A:5C2F / 有线 248A:5D2E、5C2E）
#   是否使用 XCTECH（248A/249A 代工）同族协议。
#
#   ★ 依据（官方驱动 C:\Program Files (x86)\VK M3 Series\config.xml）：
#       VK M3 / VK M3 lite  2.4G 模式 -> vid=249A pid=5C2F  hid_interface=MI_02
#       VK M3 / VK M3 lite  USB  模式 -> vid=248A pid=5C2E/5D2E/5E2E
#     即本机用户实际在用的鼠标是 249A:5C2F（此前被误当作「宏碁 OMR331」）。
#
#   探测内容（全程只读，仅下发设备信息查询帧 0x10）：
#     1) 枚举全部集合，打印 UsagePage/Usage/各方向报告长度
#     2) 每个集合按 RID 0x00~0x0F 读 Feature，打印非零帧
#     3) XCTECH 设备信息查询 0x10 的四种下发通道：
#        F-Feature  SetFeature -> GetFeature
#        I-Feature  SetFeature -> 中断 IN 读取
#        O-Output   WriteFile   -> ReadFile
#     命中判据：回帧 [1] == 0x10（命令回显），此时 [5..8] 为设备 ID，
#               [13] 充电标志、[14] 电量百分比（灵脉 AcerDeviceHelper 已实测布局）。
#
#   用法:
#     powershell -ExecutionPolicy Bypass -File "绝对路径\tools\probe_vk_m3.ps1"
#
# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
# 1. 严格使用 UTF-8 with BOM 编码保存。
# 2. 严禁 Add-Type -Path 锁死二进制；加载程序集必须采用 ReadAllBytes 全内存反射。
# 3. 本脚本仅下发只读查询命令，任何写入配置/Flash 的行为一律禁止。
# 4. [C# 5 时代的 PowerShell 兼容] 不得使用 $变量::静态方法()，必须写完整类型名。
# ============================================================================

[CmdletBinding()]
param(
    [int]$VendorId = 0x249A,
    [int]$ProductId = 0x5C2F
)

$ErrorActionPreference = 'Stop'
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = (Resolve-Path "$ScriptDir\..").Path
Set-Location $ProjectDir

# --- 1. 全内存反射加载（缺失则自动编译）---
$exePath = Join-Path $ProjectDir "FerrisPulse.exe"
if (-not (Test-Path $exePath)) {
    Write-Host "[!] 未找到 FerrisPulse.exe，正在自动编译..." -ForegroundColor Yellow
    $buildBat = Join-Path $ProjectDir "build.bat"
    if (Test-Path $buildBat) { cmd.exe /c "`"$buildBat`"" | Out-Null }
    if (-not (Test-Path $exePath)) { Write-Host "[-] 自动编译失败，请手动执行 build.bat。" -ForegroundColor Red; exit 1 }
}
[System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath)) | Out-Null

function Hex([byte[]]$buf, [int]$n) {
    if ($null -eq $buf) { return "" }
    $m = [Math]::Min($n, $buf.Length)
    if ($m -le 0) { return "" }
    return (($buf[0..($m-1)] | ForEach-Object { "{0:X2}" -f $_ }) -join ' ')
}

function AllZero([byte[]]$buf) {
    foreach ($b in $buf) { if ($b -ne 0) { return $false } }
    return $true
}

function AsciiStr([byte[]]$buf, [int]$start, [int]$len) {
    $s = ""
    for ($i = $start; $i -lt ($start + $len); $i++) {
        if ($i -ge $buf.Length) { break }
        $c = $buf[$i]
        if ($c -ge 0x20 -and $c -le 0x7E) { $s += [char]$c } else { $s += "." }
    }
    return $s
}

Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host (" 🔍 VK M3 协议验证探针 (VID {0:X4} / PID {1:X4})" -f $VendorId, $ProductId) -ForegroundColor Cyan
Write-Host "======================================================================" -ForegroundColor Cyan
Write-Host "[依据] 官方 config.xml：VK M3 2.4G = 249A:5C2F，USB = 248A:5C2E/5D2E/5E2E" -ForegroundColor DarkGray

$descs = [RazerBatteryTray.HidTransport]::Enumerate($VendorId, $ProductId)
if ($null -eq $descs -or $descs.Count -eq 0) {
    Write-Host "[-] 未找到该 VID/PID 设备。请确认鼠标开关在 2.4G 档且接收器已插好。" -ForegroundColor Red
    exit 1
}

# --- 2. 打印全部集合 ---
Write-Host "`n[1] 端点集合清单" -ForegroundColor Cyan
for ($i = 0; $i -lt $descs.Count; $i++) {
    $d = $descs[$i]
    $tag = ""
    if ($d.UsagePage -ge 0xFF00) { $tag = " [厂商私有]" }
    Write-Host ("    [{0}] UP=0x{1:X4} U=0x{2:X4} In={3} Out={4} Feat={5}{6}" -f ($i+1), $d.UsagePage, $d.Usage, $d.InputReportLength, $d.OutputReportLength, $d.FeatureReportLength, $tag) -ForegroundColor Gray
}

# --- 3. Feature 全 RID 读取 ---
Write-Host "`n[2] Feature 报告 RID 0x00~0x0F 读取" -ForegroundColor Cyan
$anyFeature = $false
foreach ($d in $descs) {
    if ($d.FeatureReportLength -le 0) { continue }
    $h = [RazerBatteryTray.HidTransport]::Open($d.DevicePath)
    if (-not [RazerBatteryTray.HidTransport]::IsValidHandle($h)) { continue }
    try {
        for ($rid = 0; $rid -le 0x0F; $rid++) {
            $buf = New-Object byte[] $d.FeatureReportLength
            $buf[0] = [byte]$rid
            if (-not [RazerBatteryTray.HidTransport]::GetFeature($h, $buf)) { continue }
            if (AllZero $buf) { continue }
            $anyFeature = $true
            Write-Host ("    UP=0x{0:X4} RID=0x{1:X2} -> [{2}]" -f $d.UsagePage, $rid, (Hex $buf 24)) -ForegroundColor Yellow
        }
    } finally { [RazerBatteryTray.HidTransport]::Close($h) }
}
if (-not $anyFeature) { Write-Host "    (全部为空)" -ForegroundColor DarkGray }

# --- 4. XCTECH 0x10 查询帧：四通道试打 ---
Write-Host "`n[3] XCTECH 设备信息查询 0x10（命中判据：回帧 [1]==0x10）" -ForegroundColor Cyan
$hit = $false

foreach ($d in $descs) {
    $path = $d.DevicePath
    $up = $d.UsagePage

    # 4.1 Feature 通道
    if ($d.FeatureReportLength -ge 8) {
        $len = $d.FeatureReportLength
        if ($len -gt 64) { $len = 64 }
        $tx = New-Object byte[] $len
        $tx[0] = 0x00
        $tx[1] = 0x10
        $h = [RazerBatteryTray.HidTransport]::Open($path)
        if ([RazerBatteryTray.HidTransport]::IsValidHandle($h)) {
            try {
                $okSet = [RazerBatteryTray.HidTransport]::SetFeature($h, $tx)
                [System.Threading.Thread]::Sleep(120)
                for ($rid = 0; $rid -le 0x0F; $rid++) {
                    $rx = New-Object byte[] $len
                    $rx[0] = [byte]$rid
                    if (-not [RazerBatteryTray.HidTransport]::GetFeature($h, $rx)) { continue }
                    if (AllZero $rx) { continue }
                    if ($rx.Length -ge 2 -and $rx[1] -eq 0x10) {
                        $hit = $true
                        Write-Host ("    [F] UP=0x{0:X4} Set={1} RID=0x{2:X2} 命中! [{3}]" -f $up, $okSet, $rid, (Hex $rx 20)) -ForegroundColor Green
                        Write-Host ("        设备ID='{0}' [13]={1} [14]={2}" -f (AsciiStr $rx 5 4), $rx[13], $rx[14]) -ForegroundColor Green
                    } else {
                        Write-Host ("    [F] UP=0x{0:X4} RID=0x{1:X2} -> [{2}]" -f $up, $rid, (Hex $rx 20)) -ForegroundColor DarkGray
                    }
                }
            } finally { [RazerBatteryTray.HidTransport]::Close($h) }
        }
    }

    # 4.2 Output -> Input 通道（AcerDeviceHelper 实际使用的方式）
    if ($d.OutputReportLength -ge 8) {
        $olen = $d.OutputReportLength
        if ($olen -gt 64) { $olen = 64 }
        $ilen = $d.InputReportLength
        if ($ilen -le 0) { $ilen = $olen }
        if ($ilen -gt 64) { $ilen = 64 }
        $tx = New-Object byte[] $olen
        $tx[0] = 0x00
        $tx[1] = 0x10
        $h = [RazerBatteryTray.HidTransport]::Open($path)
        if ([RazerBatteryTray.HidTransport]::IsValidHandle($h)) {
            try {
                [void][RazerBatteryTray.HidTransport]::DrainReports($h, $ilen, 8)
                $okW = [RazerBatteryTray.HidTransport]::WriteReport($h, $tx, $olen, 500)
                $rx = $null
                $okR = [RazerBatteryTray.HidTransport]::ReadReport($h, $ilen, 900, [ref]$rx)
                if ($okR -and ($null -ne $rx)) {
                    if ($rx.Length -ge 2 -and $rx[1] -eq 0x10) { $hit = $true }
                    $col = if ($rx.Length -ge 2 -and $rx[1] -eq 0x10) { "Green" } else { "Yellow" }
                    Write-Host ("    [O] UP=0x{0:X4} Write={1} Read={2} -> [{3}]" -f $up, $okW, $okR, (Hex $rx 20)) -ForegroundColor $col
                    if ($rx.Length -gt 13) {
                        Write-Host ("        设备ID='{0}' [13]={1} [14]={2}" -f (AsciiStr $rx 5 4), $rx[13], $rx[14]) -ForegroundColor $col
                    }
                } else {
                    Write-Host ("    [O] UP=0x{0:X4} Write={1} Read=无回帧" -f $up, $okW) -ForegroundColor DarkGray
                }
            } finally { [RazerBatteryTray.HidTransport]::Close($h) }
        }
    }
}

Write-Host "`n======================================================================" -ForegroundColor Cyan
if ($hit) {
    Write-Host " ✅ 命中 XCTECH 0x10 协议 —— 电量就在 [14]、充电在 [13]" -ForegroundColor Green
    Write-Host "    请把上面带『命中!』的整行贴回，我据此接入驱动。" -ForegroundColor Green
} else {
    Write-Host " ⚠ 未命中 0x10 协议回显。请把本屏完整输出贴回。" -ForegroundColor Yellow
    Write-Host "    （若 [2] 段有非空 Feature 帧，那串字节同样极有价值）" -ForegroundColor Yellow
}
Write-Host "======================================================================" -ForegroundColor Cyan
