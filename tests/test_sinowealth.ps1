# ============================================================================
# 测试脚本: test_sinowealth.ps1
# 目标: 验证 SinoWealth（赛诺韦尔，芯片厂商）VID 258A / PID 010C 多模键鼠接收器适配：
#       厂商集合通道 (0xFF00/0x0001)、0x04/0x05/0x11 配置帧、回报率/XY 独立/DPI 档解析、
#       VID:PID 隔离（绝不误吞 Acer/Razer/Logitech）、DeviceManager 与 BuiltInDrivers 接入、
#       以及保守电量探测不虚标。
# 注意: 258A:010C 是 SinoWealth 多模键鼠接收器，与本项目另有实测支持的
#       Valkyrie VK M3 系列（249A:5C2F，XCTECH 方案）不是同一设备，勿混。
# 加载方式: 字节数组反射加载 FerrisPulse.exe（handbook §4.4：绝不用 Add-Type -Path）。
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
    # 只扫描「真正的代码行」：文件头的 AI 维护准则里本身写着被禁止的语法字面量（如 $"..."），
    # 直接对全文匹配会把注释误判成违规代码。故先剔除整行注释再断言。
    $codeOnly = ($content -split "`r?`n" | Where-Object {
        $t = $_.Trim()
        -not ($t.StartsWith("//") -or $t.StartsWith("/*") -or $t.StartsWith("*") -or $t.StartsWith("///"))
    }) -join "`n"
    if ([regex]::IsMatch($codeOnly, '\$"')) { Write-Host "FAILED: 字符串插值: $path" -ForegroundColor Red; exit 1 }
    if ([regex]::IsMatch($codeOnly, '\w+\?\.')) { Write-Host "FAILED: 空条件运算符: $path" -ForegroundColor Red; exit 1 }
    if ([regex]::IsMatch($codeOnly, 'nameof\s*\(')) { Write-Host "FAILED: nameof: $path" -ForegroundColor Red; exit 1 }
    if ([regex]::IsMatch($codeOnly, 'out\s+var\s+')) { Write-Host "FAILED: out var: $path" -ForegroundColor Red; exit 1 }
    if ([regex]::IsMatch($codeOnly, '=>\s*\{')) { Write-Host "FAILED: 表达式体成员: $path" -ForegroundColor Red; exit 1 }
    if (-not $content.Contains("⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️")) {
        Write-Host "FAILED: 缺少 AI 维护准则声明: $path" -ForegroundColor Red; exit 1
    }
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 1/5] SinoWealth 驱动文件 BOM / C# 5.0 规范" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$helperPath = "$ProjectDir\src\Hardware\SinoWealthDeviceHelper.cs"
Assert-Bom $helperPath
Assert-CSharp5 $helperPath
Write-Host "PASSED: SinoWealthDeviceHelper.cs 存在、BOM 正确且符合 C# 5.0 铁律" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 2/5] VID/PID 白名单与协议常量 / 接入点" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$content = [System.IO.File]::ReadAllText($helperPath, [System.Text.Encoding]::UTF8)
foreach ($token in @("0x258A", "0x010C", "0xFF00", "0x0001", "0x04", "0x05", "0x11")) {
    if (-not $content.Contains($token)) { Write-Host "FAILED: 缺少协议常量 $token" -ForegroundColor Red; exit 1 }
}

$dmContent = [System.IO.File]::ReadAllText("$ProjectDir\src\Hardware\DeviceManager.cs", [System.Text.Encoding]::UTF8)
if (-not $dmContent.Contains("SinoWealthDeviceHelper.QueryAllSinoWealthDevices()")) {
    Write-Host "FAILED: DeviceManager 未接入 SinoWealth 查询" -ForegroundColor Red; exit 1
}

$bdContent = [System.IO.File]::ReadAllText("$ProjectDir\src\Hardware\BuiltInDrivers.cs", [System.Text.Encoding]::UTF8)
if (-not $bdContent.Contains("new SinoWealthDriver()")) {
    Write-Host "FAILED: BuiltInDrivers 未注册 SinoWealthDriver" -ForegroundColor Red; exit 1
}

$ghContent = [System.IO.File]::ReadAllText("$ProjectDir\src\Hardware\GenericHidHelper.cs", [System.Text.Encoding]::UTF8)
if (-not $ghContent.Contains("vid_258a&pid_010c")) {
    Write-Host "FAILED: GenericHidHelper 跳频名单未包含 258A:010C" -ForegroundColor Red; exit 1
}
Write-Host "PASSED: 协议常量齐全，且已接入 DeviceManager / BuiltInDrivers / GenericHidHelper 跳频名单" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 3/5] VID:PID 隔离（绝不误吞其它品牌设备）" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$binPath = "$ProjectDir\FerrisPulse.exe"
if (-not (Test-Path $binPath)) {
    Write-Host "编译二进制以供反射测试..." -ForegroundColor Yellow
    cmd /c "$ProjectDir\build.bat" | Out-Null
}
if (-not (Test-Path $binPath)) {
    Write-Host "FAILED: 未能生成 FerrisPulse.exe，请先本地执行 build.bat" -ForegroundColor Red; exit 1
}
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($binPath))
$type = $asm.GetType("RazerBatteryTray.SinoWealthDeviceHelper")
if ($null -eq $type) { Write-Host "FAILED: 未找到 SinoWealthDeviceHelper" -ForegroundColor Red; exit 1 }

$isDev = $type.GetMethod("IsSinoWealthDevice", [System.Reflection.BindingFlags]"Public,Static")
$isVp = $type.GetMethod("IsSupportedVidPid", [System.Reflection.BindingFlags]"Public,Static")

# 正向：本机实测路径形式
if (-not $isDev.Invoke($null, @("HID\VID_258A&PID_010C&MI_01"))) { Write-Host "FAILED: 应识别 vid_258a&pid_010c" -ForegroundColor Red; exit 1 }
if (-not $isDev.Invoke($null, @("258A:010C"))) { Write-Host "FAILED: 应识别 258A:010C" -ForegroundColor Red; exit 1 }

# 反向隔离：Acer / Razer / Logitech 不得被误判
if ($isDev.Invoke($null, @("HID\VID_249A&PID_5C2F&MI_00"))) { Write-Host "FAILED: 不得误吞 Acer 249A:5C2F" -ForegroundColor Red; exit 1 }
if ($isDev.Invoke($null, @("HID\VID_1532&PID_0078&MI_00"))) { Write-Host "FAILED: 不得误吞 Razer 1532:0078" -ForegroundColor Red; exit 1 }
if ($isDev.Invoke($null, @("HID\VID_046D&PID_C539&MI_00"))) { Write-Host "FAILED: 不得误吞 Logitech 046D:C539" -ForegroundColor Red; exit 1 }

# VID/PID 组合严格匹配
if (-not $isVp.Invoke($null, @([int]0x258A, [int]0x010C))) { Write-Host "FAILED: 258A:010C 应命中" -ForegroundColor Red; exit 1 }
if ($isVp.Invoke($null, @([int]0x258A, [int]0x9999))) { Write-Host "FAILED: 错误 PID 不应命中" -ForegroundColor Red; exit 1 }
if ($isVp.Invoke($null, @([int]0x249A, [int]0x010C))) { Write-Host "FAILED: 错误 VID 不应命中" -ForegroundColor Red; exit 1 }
Write-Host "PASSED: 258A:010C 正向命中；Acer/Razer/Logitech 全部隔离" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 4/5] 配置帧解析（回报率 / XY 独立 / DPI 档）" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$parse = $type.GetMethod("ParseConfig", [System.Reflection.BindingFlags]"NonPublic,Static")

# 合成帧：下标 0 = Report ID (0x04)，[1] = 命令回显 0x11，[10]=标志/回报率，[11]=激活档/档数，[13..28]=16 个 DPI 原始值
# 说明：[1] 必须回显 0x11 —— 该固件上 0x06 报告是「导出 HID 描述符」而非配置，
# 只能用「[1]==0x11」把真配置和描述符垃圾区分开（见 SinoWealthDeviceHelper.IsConfigFrame）。
# 注意：`return ,$f` 用一元逗号阻止 PowerShell 把 byte[] 逐元素展开。
function Make-Frame([byte]$flags, [byte]$stage, [byte[]]$dpi) {
    $f = New-Object byte[] 29
    $f[0] = 0x04
    $f[1] = 0x11
    $f[10] = $flags
    $f[11] = $stage
    for ($i = 0; $i -lt 16; $i++) { $f[13 + $i] = $dpi[$i] }
    return ,$f
}

# 反射调用带 out 参数的方法：必须传入类型正确的 object[7]。
# 直接把 @($bytes, 29, ...) 交给 Invoke 会被 PowerShell 包成 PSObject，
# 反射层报「System.Management.Automation.PSObject 无法转换为 System.Byte[]」。
function Invoke-ParseFrame([byte[]]$frame, [int]$len) {
    $inv = New-Object 'object[]' 7
    $inv[0] = $frame
    $inv[1] = $len
    $inv[2] = [int]0        # out pollingRate
    $inv[3] = [bool]$false  # out xyIndependent
    $inv[4] = [int]0        # out activeStage
    $inv[5] = [int]0        # out stageCount
    $inv[6] = $null         # out dpiStages
    $ok = $parse.Invoke($null, $inv)
    return [pscustomobject]@{ Ok = $ok; Inv = $inv }
}

# 帧 A：0x81 = XY 独立(0x80) + 回报率档1(125Hz)；0x23 = 激活档2 / 档数3；DPI[2]=0x50(80)
$ra = Invoke-ParseFrame ([byte[]](Make-Frame 0x81 0x23 @(0x10,0x20,0x50,0x40,0x30,0x60,0x70,0x80,0x90,0xA0,0xB0,0xC0,0xD0,0xE0,0xF0,0x05))) 29
if (-not $ra.Ok) { Write-Host "FAILED: 帧 A 解析失败" -ForegroundColor Red; exit 1 }
if ($ra.Inv[2] -ne 125) { Write-Host ("FAILED: 帧 A 回报率错误: " + $ra.Inv[2]) -ForegroundColor Red; exit 1 }
if ($ra.Inv[3] -ne $true) { Write-Host "FAILED: 帧 A 应判定 XY 独立" -ForegroundColor Red; exit 1 }
if ($ra.Inv[4] -ne 2) { Write-Host ("FAILED: 帧 A 激活档错误: " + $ra.Inv[4]) -ForegroundColor Red; exit 1 }
if ($ra.Inv[5] -ne 3) { Write-Host ("FAILED: 帧 A 档数错误: " + $ra.Inv[5]) -ForegroundColor Red; exit 1 }
if ($ra.Inv[6].Length -ne 16) { Write-Host "FAILED: 帧 A DPI 档数组长度应为 16" -ForegroundColor Red; exit 1 }
if ($ra.Inv[6][2] -ne 80) { Write-Host ("FAILED: 帧 A DPI[2] 应为 80，得到 " + $ra.Inv[6][2]) -ForegroundColor Red; exit 1 }

# 帧 B：0x04 = XY 不独立 + 回报率档4(1000Hz)；0x11 = 激活档1 / 档数1
$rb = Invoke-ParseFrame ([byte[]](Make-Frame 0x04 0x11 @(0x64,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00,0x00))) 29
if (-not $rb.Ok) { Write-Host "FAILED: 帧 B 解析失败" -ForegroundColor Red; exit 1 }
if ($rb.Inv[2] -ne 1000) { Write-Host ("FAILED: 帧 B 回报率错误: " + $rb.Inv[2]) -ForegroundColor Red; exit 1 }
if ($rb.Inv[3] -ne $false) { Write-Host "FAILED: 帧 B 应判定 XY 不独立" -ForegroundColor Red; exit 1 }
if ($rb.Inv[4] -ne 1 -or $rb.Inv[5] -ne 1) { Write-Host "FAILED: 帧 B 激活档/档数错误" -ForegroundColor Red; exit 1 }

# 帧 C：回报率档3(500Hz) + XY 不独立
$rc = Invoke-ParseFrame ([byte[]](Make-Frame 0x03 0x05 @(0x32,0x32,0x32,0x32,0x32,0x32,0x32,0x32,0x32,0x32,0x32,0x32,0x32,0x32,0x32,0x32))) 29
if (-not $rc.Ok) { Write-Host "FAILED: 帧 C 解析失败" -ForegroundColor Red; exit 1 }
if ($rc.Inv[2] -ne 500) { Write-Host ("FAILED: 帧 C 回报率错误: " + $rc.Inv[2]) -ForegroundColor Red; exit 1 }

# 帧 D（防守）：Report ID 非法（既非 0x04 也非 0x06）必须返回 false
$frameD = [byte[]](Make-Frame 0x81 0x23 @(0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0))
$frameD[0] = 0x02
$rd = Invoke-ParseFrame $frameD 29
if ($rd.Ok) { Write-Host "FAILED: Report ID 非法时不应解析成功" -ForegroundColor Red; exit 1 }

# 帧 G（防守）：0x06 报告但 [1] 不回显 0x11 —— 这是「导出 HID 描述符」的回包，
# 绝不能被当成配置解析（否则界面会显示错误的 DPI / 回报率）。
$frameG = [byte[]](Make-Frame 0x81 0x23 @(0,0,0,0,0,0,0,0,0,0,0,0,0,0,0,0))
$frameG[0] = 0x06
$frameG[1] = 0x00
$rg = Invoke-ParseFrame $frameG 29
if ($rg.Ok) { Write-Host "FAILED: 描述符导出回包（[1]!=0x11）不应解析成功" -ForegroundColor Red; exit 1 }

# 帧 E（防守）：过短帧必须优雅返回 false（不抛异常）
$re = Invoke-ParseFrame (New-Object byte[] 10) 10
if ($re.Ok) { Write-Host "FAILED: 过短帧不应解析成功" -ForegroundColor Red; exit 1 }

# 帧 F（防守）：null 帧必须优雅返回 false
$rf = Invoke-ParseFrame $null 0
if ($rf.Ok) { Write-Host "FAILED: null 帧不应解析成功" -ForegroundColor Red; exit 1 }
Write-Host "PASSED: 125/500/1000Hz 回报率、XY 独立开关、激活档/档数、16 档 DPI 解析正确；非法帧全部优雅拒绝" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 5/5] 保守电量探测不虚标（无标准电池页则 SupportsBattery=false）" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 仅做静态契约校验：SinoWealthDeviceHelper 的 QueryStandardHidBattery 只扫描 UsagePage 0x85，
# 且 BatteryPercent=-1 时驱动层 PopulateCapabilities 会把 SupportsBattery 置 false（见 BuiltInDrivers）。
# 这里通过源码契约确保「绝不盲扫厂商帧 / 绝不把移动字节误判为电量」的设计被锁死。
if (-not $content.Contains("0x85")) { Write-Host "FAILED: 保守电量探测未锁定标准电池页 0x85" -ForegroundColor Red; exit 1 }
if ($content.Contains("HidD_GetInputReport")) { Write-Host "FAILED: 不得扫描 Input 管道（会把移动字节误判为电量）" -ForegroundColor Red; exit 1 }
$bdContent2 = [System.IO.File]::ReadAllText("$ProjectDir\src\Hardware\BuiltInDrivers.cs", [System.Text.Encoding]::UTF8)
if (-not $bdContent2.Contains("dev.Capabilities.SupportsBattery = (dev.BatteryPercent >= 0)")) {
    Write-Host "FAILED: 驱动未实现「电量未知则不虚标」契约" -ForegroundColor Red; exit 1
}
Write-Host "PASSED: 电量仅探测标准电池页(0x85)，不扫 Input 管道；BatteryPercent<0 时 SupportsBattery=false（不虚标）" -ForegroundColor Green

Write-Host "`nALL SINOWEALTH TESTS PASSED" -ForegroundColor Green
