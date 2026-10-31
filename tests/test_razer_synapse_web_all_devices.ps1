# ========================================================
# 单元测试：雷云 Web 端全量 19 款设备深度适配断言套件
# 覆盖 5 款鼠标、11 款 Huntsman 键盘、1 款 Tartarus、2 款 Blade 笔记本
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

$catType = $assembly.GetType('RazerBatteryTray.DeviceCategory')

Write-Host "========================================================"
Write-Host "  测试套件：雷云 Web 端全量 19 款设备深度适配验证"
Write-Host "========================================================"

$nameMethod = $helperType.GetMethod('GetRazerOfficialModelName', [System.Reflection.BindingFlags]'Public,Static', $null, @([int], [string]), $null)
$catMethod = $helperType.GetMethod('GetRazerDeviceCategory', [System.Reflection.BindingFlags]'Public,Static', $null, @([int], [string]), $null)
$capMethod = $helperType.GetMethod('GetDpiCapability', [System.Reflection.BindingFlags]'Public,Static', $null, @([string], [int]), $null)
$rateMethod = $helperType.GetMethod('GetSupportedPollingRatesForModel', [System.Reflection.BindingFlags]'Public,Static', $null, @([int], [string]), $null)

# ----------------------------------------------------
# TEST 1: 全量 19 款设备型号名称识别 (GetRazerOfficialModelName)
# ----------------------------------------------------
Write-Host "`n[TEST 1] 校验全量 19 款设备 PID 官方名称映射..."

$allDevices = @(
    # 5 Mice
    @{ Pid = 0x00E5; Name = "Razer Viper V4 Pro" },
    @{ Pid = 0x00E6; Name = "Razer Viper V4 Pro" },
    @{ Pid = 0x00EF; Name = "Razer DeathAdder V4 Pro Carbon" },
    @{ Pid = 0x00F0; Name = "Razer DeathAdder V4 Pro Carbon" },
    @{ Pid = 0x00BE; Name = "Razer DeathAdder V4 Pro" },
    @{ Pid = 0x00BF; Name = "Razer DeathAdder V4 Pro" },
    @{ Pid = 0xB007; Name = "Razer DeathAdder V4 HyperSpeed" },
    @{ Pid = 0xB005; Name = "Razer Viper V4 HyperSpeed" },

    # 11 Keyboards
    @{ Pid = 0x0257; Name = "Razer Huntsman Mini" },
    @{ Pid = 0x0269; Name = "Razer Huntsman Mini Japan" },
    @{ Pid = 0x02B0; Name = "Razer Huntsman V3 Pro Mini" },
    @{ Pid = 0x02CF; Name = "Razer Huntsman V3 Pro 8KHz" },
    @{ Pid = 0x02D0; Name = "Razer Huntsman V3 Pro Tenkeyless 8KHz" },
    @{ Pid = 0x02D1; Name = "Razer Huntsman V3 Pro Mini 8KHz" },
    @{ Pid = 0x02D8; Name = "Razer Huntsman Signature Edition" },
    @{ Pid = 0x02E4; Name = "Razer Huntsman V3 HE Magnetic Mini 65% 8KHz" },
    @{ Pid = 0x02E5; Name = "Razer Huntsman V3 Tenkeyless 8KHz" },
    @{ Pid = 0x02E6; Name = "Razer Huntsman V3 Pro Low-profile Tenkeyless 8KHz" },
    @{ Pid = 0x02EA; Name = "Razer Huntsman V3 HE Magnetic Tenkeyless 8KHz" },

    # 1 Keypad
    @{ Pid = 0x02EB; Name = "Razer Tartarus V2 Pro" },

    # 2 Blade Laptops
    @{ Pid = 0x02E0; Name = "Razer Blade 16 (2026)" },
    @{ Pid = 0x02E1; Name = "Razer Blade 18 (2026)" }
)

foreach ($dev in $allDevices) {
    $resName = $nameMethod.Invoke($null, @([int]$dev.Pid, [string]""))
    if ($resName -ne $dev.Name) {
        Write-Error ("[FAIL] PID 0x{0:X4} 名称识别错误，预期 '{1}'，实测 '{2}'" -f $dev.Pid, $dev.Name, $resName)
        exit 1
    }
    Write-Host ("  [PASS] 0x{0:X4} -> {1}" -f $dev.Pid, $resName)
}

# ----------------------------------------------------
# TEST 2: 设备类别路由校验 (GetRazerDeviceCategory)
# ----------------------------------------------------
Write-Host "`n[TEST 2] 校验设备类别自动路由（键盘 vs 鼠标）..."

# 5 款鼠标必须被识别为 Mouse
$micePids = @(0x00E5, 0x00E6, 0x00EF, 0x00F0, 0x00BE, 0x00BF, 0xB007, 0xB005)
foreach ($p in $micePids) {
    $cat = $catMethod.Invoke($null, @([int]$p, [string]""))
    if ($cat.ToString() -ne "Mouse") {
        Write-Error ("[FAIL] 鼠标 PID 0x{0:X4} 未被路由为 Mouse，实测: {1}" -f $p, $cat)
        exit 1
    }
}
Write-Host "  [PASS] 全部 5 款鼠标 PID 100% 正确路由为 DeviceCategory.Mouse"

# 11 款键盘 + 1 款 Tartarus + 2 款 Blade 必须被识别为 Keyboard
$kbPids = @(0x0257, 0x0269, 0x02B0, 0x02CF, 0x02D0, 0x02D1, 0x02D8, 0x02E4, 0x02E5, 0x02E6, 0x02EA, 0x02EB, 0x02E0, 0x02E1)
foreach ($p in $kbPids) {
    $cat = $catMethod.Invoke($null, @([int]$p, [string]""))
    if ($cat.ToString() -ne "Keyboard") {
        Write-Error ("[FAIL] 键盘/系统 PID 0x{0:X4} 未被路由为 Keyboard，实测: {1}" -f $p, $cat)
        exit 1
    }
}
Write-Host "  [PASS] 全部 11 款键盘 + 1 款 Tartarus + 2 款 Blade 100% 正确路由为 DeviceCategory.Keyboard"

# ----------------------------------------------------
# TEST 3: 传感器与 DPI 规格精确门禁 (GetDpiCapability)
# ----------------------------------------------------
Write-Host "`n[TEST 3] 校验各系列鼠标传感器规格与最大 DPI..."

# 1. 毒蝰 V4 Pro (50,000 DPI)
$capV4 = $capMethod.Invoke($null, @("Razer Viper V4 Pro", [int]0x00E5))
if ($capV4.MaxDpi -ne 50000) { Write-Error "[FAIL] 毒蝰 V4 Pro 最大 DPI 错误！"; exit 1 }

# 2. 炼狱蝰蛇 V4 Pro 碳纤维版 (50,000 DPI)
$capCarbon = $capMethod.Invoke($null, @("Razer DeathAdder V4 Pro Carbon", [int]0x00EF))
if ($capCarbon.MaxDpi -ne 50000) { Write-Error "[FAIL] 碳纤维版 最大 DPI 错误！"; exit 1 }

# 3. 炼狱蝰蛇 V4 Pro 标准版 (45,000 DPI)
$capDA4 = $capMethod.Invoke($null, @("Razer DeathAdder V4 Pro", [int]0x00BE))
if ($capDA4.MaxDpi -ne 45000) { Write-Error "[FAIL] 炼狱蝰蛇 V4 Pro 标准版 最大 DPI 错误！"; exit 1 }

# 4. 炼狱蝰蛇 V4 HyperSpeed (33,000 DPI)
$capDA4HS = $capMethod.Invoke($null, @("Razer DeathAdder V4 HyperSpeed", [int]0xB007))
if ($capDA4HS.MaxDpi -ne 33000) { Write-Error "[FAIL] 炼狱蝰蛇 V4 HyperSpeed 最大 DPI 错误，实测 $($capDA4HS.MaxDpi)"; exit 1 }

# 5. 毒蝰 V4 HyperSpeed (33,000 DPI)
$capV4HS = $capMethod.Invoke($null, @("Razer Viper V4 HyperSpeed", [int]0xB005))
if ($capV4HS.MaxDpi -ne 33000) { Write-Error "[FAIL] 毒蝰 V4 HyperSpeed 最大 DPI 错误，实测 $($capV4HS.MaxDpi)"; exit 1 }

Write-Host "  [PASS] 50K (毒蝰V4P/碳纤维版), 45K (炼蛇V4P标准版), 33K (极速版系列) 门禁全部匹配！"

# ----------------------------------------------------
# TEST 4: 8000Hz 档位支持校验 (GetSupportedPollingRatesForModel)
# ----------------------------------------------------
Write-Host "`n[TEST 4] 校验 8000Hz 旗舰档位支持..."
$huntsman8k = @(0x02CF, 0x02D0, 0x02D1, 0x02D8, 0x02E4, 0x02E5, 0x02E6, 0x02EA)
foreach ($k in $huntsman8k) {
    $rates = $rateMethod.Invoke($null, @([int]$k, [string]""))
    if ($rates -notcontains 8000) {
        Write-Error ("[FAIL] 8KHz 键盘 0x{0:X4} 未包含 8000Hz 回报率！" -f $k)
        exit 1
    }
}
Write-Host "  [PASS] 全部 Huntsman 8KHz 键盘 100% 支持 8000Hz 回报率"

Write-Host "`n[SUCCESS] 雷云 Web 端全量 19 款设备 4 项严苛断言全部 PASS 通过！"
