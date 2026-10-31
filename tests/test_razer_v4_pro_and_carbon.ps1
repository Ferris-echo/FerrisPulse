# ========================================================
# 单元测试：毒蝰 V4 Pro 与 炼狱蝰蛇 V4 Pro 标准版/碳纤维版 规格与报文解析
# ========================================================

$ErrorActionPreference = 'Stop'
$asmPath = Join-Path $PSScriptRoot "..\FerrisPulse.exe"
if (-not (Test-Path $asmPath)) {
    $asmPath = "FerrisPulse.exe"
}
if (-not (Test-Path $asmPath)) {
    Write-Error "[FAIL] 未找到 FerrisPulse.exe，请先执行 build.bat 进行编译！"
    exit 1
}

# 1. 内存反射加载二进制（严格遵守自动化测试防锁死铁律）
$fullPath = (Resolve-Path $asmPath).Path
[byte[]]$rawBytes = [System.IO.File]::ReadAllBytes($fullPath)
$assembly = [System.Reflection.Assembly]::Load($rawBytes)

$helperType = $assembly.GetType('RazerBatteryTray.RazerDeviceHelper')
if ($null -eq $helperType) {
    Write-Error "[FAIL] 未在程序集中找到 RazerBatteryTray.RazerDeviceHelper 类型！"
    exit 1
}

Write-Host "========================================================"
Write-Host "  测试套件：毒蝰 V4 Pro 与 炼狱蝰蛇 V4 Pro 标准版/碳纤维版"
Write-Host "========================================================"

# ----------------------------------------------------
# TEST 1: PID 归一化映射 (NormalizeRazerPid)
# ----------------------------------------------------
Write-Host "[TEST 1] 校验 PID 归一化映射 (NormalizeRazerPid)..."
$normMethod = $helperType.GetMethod('NormalizeRazerPid', [System.Reflection.BindingFlags]'Public,Static')

# 碳纤维版 Dongle (0x00F0 -> 0x00EF)
$res = $normMethod.Invoke($null, @([int]0x00F0))
if ($res -ne 0x00EF) {
    Write-Error ("[FAIL] 碳纤维版 Dongle 归一化错误，预期 0x00EF，实测 0x{0:X4}" -f $res)
    exit 1
}
Write-Host "  [PASS] DeathAdder V4 Pro Carbon Dongle -> Wired: 0x00F0 -> 0x00EF"

# 标准版 Dongle (0x00BF -> 0x00BE)
$res = $normMethod.Invoke($null, @([int]0x00BF))
if ($res -ne 0x00BE) {
    Write-Error ("[FAIL] 标准版 Dongle 归一化错误，预期 0x00BE，实测 0x{0:X4}" -f $res)
    exit 1
}
Write-Host "  [PASS] DeathAdder V4 Pro Standard Dongle -> Wired: 0x00BF -> 0x00BE"

# 毒蝰 V4 Pro Dongle (0x00E6 -> 0x00E5)
$res = $normMethod.Invoke($null, @([int]0x00E6))
if ($res -ne 0x00E5) {
    Write-Error ("[FAIL] 毒蝰 V4 Pro Dongle 归一化错误，预期 0x00E5，实测 0x{0:X4}" -f $res)
    exit 1
}
Write-Host "  [PASS] Viper V4 Pro Dongle -> Wired: 0x00E6 -> 0x00E5"

# ----------------------------------------------------
# TEST 2: 型号名称识别与碳纤维专属高精匹配 (GetRazerOfficialModelName)
# ----------------------------------------------------
Write-Host "`n[TEST 2] 校验型号名称识别与碳纤维专属高精匹配 (GetRazerOfficialModelName)..."
$nameMethod = $helperType.GetMethod('GetRazerOfficialModelName', [System.Reflection.BindingFlags]'Public,Static', $null, @([int], [string]), $null)

$cases = @(
    @{ Desc = '碳纤维专属有线 PID'; Pid = 0x00EF; Prod = 'Razer DeathAdder V4 Pro Carbon'; Exp = 'Razer DeathAdder V4 Pro Carbon' },
    @{ Desc = '碳纤维专属无线 Dongle PID'; Pid = 0x00F0; Prod = 'Razer HyperPolling Wireless Dongle'; Exp = 'Razer DeathAdder V4 Pro Carbon' },
    @{ Desc = '标准版有线 PID'; Pid = 0x00BE; Prod = 'Razer DeathAdder V4 Pro'; Exp = 'Razer DeathAdder V4 Pro' },
    @{ Desc = '标准版无线 Dongle PID'; Pid = 0x00BF; Prod = 'Razer HyperPolling Wireless Dongle'; Exp = 'Razer DeathAdder V4 Pro' },
    @{ Desc = '毒蝰 V4 Pro 有线 PID'; Pid = 0x00E5; Prod = 'Razer Viper V4 Pro'; Exp = 'Razer Viper V4 Pro' },
    @{ Desc = '毒蝰 V4 Pro 无线 Dongle PID'; Pid = 0x00E6; Prod = 'Razer HyperPolling Wireless Dongle'; Exp = 'Razer Viper V4 Pro' },
    @{ Desc = '标准 PID 搭载 Carbon 描述符升级'; Pid = 0x00BE; Prod = 'Razer DeathAdder V4 Pro Carbon'; Exp = 'Razer DeathAdder V4 Pro Carbon' }
)

foreach ($c in $cases) {
    $name = $nameMethod.Invoke($null, @([int]$c.Pid, [string]$c.Prod))
    if ($name -ne $c.Exp) {
        Write-Error "[FAIL] $($c.Desc) 名称识别错误，预期 '$($c.Exp)'，实测 '$name'"
        exit 1
    }
    Write-Host "  [PASS] $($c.Desc): '$name'"
}

# ----------------------------------------------------
# TEST 3: 传感器规格与最大 DPI 精确门禁 (GetDpiCapability)
# ----------------------------------------------------
Write-Host "`n[TEST 3] 校验传感器规格与最大 DPI 精确门禁 (GetDpiCapability)..."
$capMethod = $helperType.GetMethod('GetDpiCapability', [System.Reflection.BindingFlags]'Public,Static', $null, @([string], [int]), $null)

# 1. 毒蝰 V4 Pro -> 50,000 DPI (Focus Pro 50K Gen-3)
$capViper = $capMethod.Invoke($null, @('Razer Viper V4 Pro', [int]0x00E5))
if ($capViper.MaxDpi -ne 50000 -or $capViper.Step -ne 1 -or -not $capViper.Description.Contains('50K')) {
    Write-Error "[FAIL] 毒蝰 V4 Pro 传感器规格错误！MaxDpi=$($capViper.MaxDpi), Step=$($capViper.Step), Desc=$($capViper.Description)"
    exit 1
}
Write-Host "  [PASS] 毒蝰 V4 Pro 50K 传感器: MaxDpi=$($capViper.MaxDpi), Step=$($capViper.Step), Desc='$($capViper.Description)'"

# 2. 炼狱蝰蛇 V4 Pro 碳纤维版 -> 50,000 DPI (Focus Pro 50K Gen-3)
$capCarbon = $capMethod.Invoke($null, @('Razer DeathAdder V4 Pro Carbon', [int]0x00EF))
if ($capCarbon.MaxDpi -ne 50000 -or $capCarbon.Step -ne 1 -or -not $capCarbon.Description.Contains('50K')) {
    Write-Error "[FAIL] 炼狱蝰蛇 V4 Pro 碳纤维版 传感器规格错误！MaxDpi=$($capCarbon.MaxDpi), Step=$($capCarbon.Step), Desc=$($capCarbon.Description)"
    exit 1
}
Write-Host "  [PASS] 炼狱蝰蛇 V4 Pro 碳纤维版 50K: MaxDpi=$($capCarbon.MaxDpi), Step=$($capCarbon.Step), Desc='$($capCarbon.Description)'"

# 3. 炼狱蝰蛇 V4 Pro 标准版 -> 45,000 DPI (第二代 Focus Pro 45K)
$capStd = $capMethod.Invoke($null, @('Razer DeathAdder V4 Pro', [int]0x00BE))
if ($capStd.MaxDpi -ne 45000 -or $capStd.Step -ne 1 -or -not $capStd.Description.Contains('45K')) {
    Write-Error "[FAIL] 炼狱蝰蛇 V4 Pro 标准版 传感器规格错误！MaxDpi=$($capStd.MaxDpi), Step=$($capStd.Step), Desc=$($capStd.Description)"
    exit 1
}
Write-Host "  [PASS] 炼狱蝰蛇 V4 Pro 标准版 45K: MaxDpi=$($capStd.MaxDpi), Step=$($capStd.Step), Desc='$($capStd.Description)'"

# ----------------------------------------------------
# TEST 4: 45,000 与 50,000 DPI 高规格多档位报文解析 (ParseDpiStagesReport)
# ----------------------------------------------------
Write-Host "`n[TEST 4] 校验 45,000 与 50,000 DPI 高规格多档位报文解析 (ParseDpiStagesReport)..."
$parseMethod = $helperType.GetMethod('ParseDpiStagesReport', [System.Reflection.BindingFlags]'Public,Static')

# 构造 90 字节原生报文 (offset = 0)
[byte[]]$mockReport = New-Object byte[] 90
$mockReport[0] = 0x02  # Status: OK (0x02)
$mockReport[1] = 0x1F  # Remaining
$mockReport[2] = 0x00
$mockReport[3] = 0x00
$mockReport[4] = 0x00
$mockReport[5] = 0x26  # Payload size = 38
$mockReport[6] = 0x04  # Class: DPI
$mockReport[7] = 0x86  # Cmd: Get DPI Stages
$mockReport[8] = 0x01  # VARSTORE
$mockReport[9] = 0x05  # Active stage: 5 (50,000 DPI)
$mockReport[10] = 0x05 # Stage count: 5

$testStages = @(400, 800, 1600, 45000, 50000)
for ($s = 0; $s -lt 5; $s++) {
    $stOffset = 11 + ($s * 7)
    $mockReport[$stOffset] = [byte]($s + 1)
    $val = $testStages[$s]
    $mockReport[$stOffset + 1] = [byte](($val -shr 8) -band 0xFF)
    $mockReport[$stOffset + 2] = [byte]($val -band 0xFF)
    $mockReport[$stOffset + 3] = [byte](($val -shr 8) -band 0xFF)
    $mockReport[$stOffset + 4] = [byte]($val -band 0xFF)
    $mockReport[$stOffset + 5] = 0x00
    $mockReport[$stOffset + 6] = 0x00
}

$invokeArgs = [object[]]@([byte[]]$mockReport, [int]0)
$state = $parseMethod.Invoke($null, $invokeArgs)
if ($null -eq $state) {
    Write-Error "[FAIL] ParseDpiStagesReport 对包含 45000 与 50000 DPI 的报文解析返回 null（旧版被 40000 上限截断）！"
    exit 1
}

if ($state.DpiStage -ne 5) {
    Write-Error "[FAIL] Active Stage 预期 5，实测 $($state.DpiStage)"
    exit 1
}
if ($state.Dpi -ne 50000) {
    Write-Error "[FAIL] Active DPI 预期 50000，实测 $($state.Dpi)"
    exit 1
}
if ($state.DpiStages[3] -ne 45000) {
    Write-Error "[FAIL] 第 4 档 DPI 预期 45000，实测 $($state.DpiStages[3])"
    exit 1
}
if ($state.DpiStages[4] -ne 50000) {
    Write-Error "[FAIL] 第 5 档 DPI 预期 50000，实测 $($state.DpiStages[4])"
    exit 1
}

Write-Host "  [PASS] 成功解析 5 档 DPI：$($state.DpiStages -join ', ')，当前活跃档位: 第 $($state.DpiStage) 档 ($($state.Dpi) DPI)"

# ----------------------------------------------------
# TEST 5: 91 字节前导报文 (offset = 1) 兼容性
# ----------------------------------------------------
Write-Host "`n[TEST 5] 校验 91 字节前导报文 (offset = 1) 兼容性..."
[byte[]]$mockPrepended = New-Object byte[] 91
$mockPrepended[0] = 0x00 # Prepend byte
[System.Array]::Copy($mockReport, 0, $mockPrepended, 1, 90)

$invokeArgsPrepended = [object[]]@([byte[]]$mockPrepended, [int]1)
$statePrepended = $parseMethod.Invoke($null, $invokeArgsPrepended)
if ($null -eq $statePrepended) {
    Write-Error "[FAIL] ParseDpiStagesReport 解析 91 字节前导报文失败！"
    exit 1
}
if ($statePrepended.Dpi -ne 50000 -or $statePrepended.DpiStage -ne 5) {
    Write-Error "[FAIL] 91 字节前导报文解析值错误！"
    exit 1
}
Write-Host "  [PASS] 91 字节前导报文成功解析: 第 $($statePrepended.DpiStage) 档 ($($statePrepended.Dpi) DPI)"

Write-Host "`n[SUCCESS] 雷蛇毒蝰 V4 Pro 与 炼狱蝰蛇 V4 Pro 全链路 5 项核心测试全部 PASS 通过！"
