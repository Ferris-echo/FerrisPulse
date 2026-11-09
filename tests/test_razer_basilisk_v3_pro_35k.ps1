# tests\test_razer_basilisk_v3_pro_35k.ps1
# 验证雷蛇巴蛇 V3 专业版 35K (Razer Basilisk V3 Pro 35K) 设备识别与硬件能力契约

# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
$ErrorActionPreference = 'Stop'
$asmPath = Join-Path $PSScriptRoot "..\FerrisPulse.exe"
if (-not (Test-Path $asmPath)) {
    $asmPath = "FerrisPulse.exe"
}
if (-not (Test-Path $asmPath)) {
    Write-Host "RED: FerrisPulse.exe not found for testing!" -ForegroundColor Red
    exit 1
}

# 严格执行 Rule 10：全内存反射加载，杜绝文件句柄占用
$fullPath = (Resolve-Path $asmPath).Path
$bytes = [System.IO.File]::ReadAllBytes($fullPath)
$asm = [System.Reflection.Assembly]::Load($bytes)

$tRazer = $asm.GetType("RazerBatteryTray.RazerDeviceHelper")
$tCat = $asm.GetType("RazerBatteryTray.DeviceCategory")

if ($tRazer -eq $null) {
    Write-Host "FAIL: RazerDeviceHelper type not found in assembly!" -ForegroundColor Red
    exit 1
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 1/6] 校验 NormalizeRazerPid 归一化映射" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$testNormCases = @(
    @{ Input = 0x00CD; Expected = 0x00CC; Desc = "Basilisk V3 Pro 35K 2.4G 无线接收器 -> 有线" },
    @{ Input = 0x00D7; Expected = 0x00D6; Desc = "Basilisk V3 Pro 35K 幻影绿 2.4G 接收器 -> 有线" },
    @{ Input = 0x00AB; Expected = 0x00AA; Desc = "Basilisk V3 Pro 备用 2.4G 接收器 -> 有线" },
    @{ Input = 0x00CC; Expected = 0x00CC; Desc = "Basilisk V3 Pro 35K 有线保持不变" },
    @{ Input = 0x00D6; Expected = 0x00D6; Desc = "Basilisk V3 Pro 35K 幻影绿有线保持不变" }
)

foreach ($tc in $testNormCases) {
    $res = $tRazer.GetMethod("NormalizeRazerPid").Invoke($null, @([int]$tc.Input))
    if ($res -ne $tc.Expected) {
        Write-Host ("FAIL: NormalizeRazerPid(0x{0:X4}) returned 0x{1:X4}, expected 0x{2:X4} ({3})" -f $tc.Input, $res, $tc.Expected, $tc.Desc) -ForegroundColor Red
        exit 1
    }
}
Write-Host "PASS: NormalizeRazerPid 成功归一化全部巴蛇 V3 专业版 35K 设备 PID。" -ForegroundColor Green

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 2/6] 校验 NormalizeRazerDeviceId 字符串对齐" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$testIdCases = @(
    @{ Input = "Razer:1532:00CD"; Expected = "Razer:1532:00CC" },
    @{ Input = "Razer:1532:00D7"; Expected = "Razer:1532:00D6" },
    @{ Input = "Razer:1532:00AB"; Expected = "Razer:1532:00AA" }
)

foreach ($tc in $testIdCases) {
    $res = $tRazer.GetMethod("NormalizeRazerDeviceId").Invoke($null, @([string]$tc.Input))
    if ($res -ne $tc.Expected) {
        Write-Host ("FAIL: NormalizeRazerDeviceId('{0}') returned '{1}', expected '{2}'" -f $tc.Input, $res, $tc.Expected) -ForegroundColor Red
        exit 1
    }
}
Write-Host "PASS: NormalizeRazerDeviceId 成功将无线 Dongle ID 映射为标准有线统一 ID。" -ForegroundColor Green

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 3/6] 校验 DetermineIsDongle 离线与待机识别" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$donglePids = @(0x00CD, 0x00D7, 0x00AB)
foreach ($devicePid in $donglePids) {
    # 模拟电量为0且未充电（接收器插入但鼠标关机或初次轮询）
    $isDongle = $tRazer.GetMethod("DetermineIsDongle").Invoke($null, @([int]$devicePid, "Razer Mouse", 0, $false))
    if (-not $isDongle) {
        Write-Host ("FAIL: DetermineIsDongle(0x{0:X4}) returned false, expected true!" -f $devicePid) -ForegroundColor Red
        exit 1
    }
}
Write-Host "PASS: DetermineIsDongle 能够 100% 识别巴蛇 V3 专业版 35K 2.4G 无线接收器！" -ForegroundColor Green

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 4/6] 校验 GetRazerOfficialModelName 官方尊贵型号命名" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$nameCases = @(
    @{ Pid = 0x00CC; Expected = "Razer Basilisk V3 Pro 35K" },
    @{ Pid = 0x00CD; Expected = "Razer Basilisk V3 Pro 35K" },
    @{ Pid = 0x00D6; Expected = "Razer Basilisk V3 Pro 35K (Phantom Green)" },
    @{ Pid = 0x00D7; Expected = "Razer Basilisk V3 Pro 35K (Phantom Green)" },
    @{ Pid = 0x00AA; Expected = "Razer Basilisk V3 Pro" },
    @{ Pid = 0x00AB; Expected = "Razer Basilisk V3 Pro" }
)

foreach ($nc in $nameCases) {
    $name = $tRazer.GetMethod("GetRazerOfficialModelName", [type[]]@([int], [string])).Invoke($null, @([int]$nc.Pid, [string]"Razer Mouse"))
    if ($name -ne $nc.Expected) {
        Write-Host ("FAIL: GetRazerOfficialModelName(0x{0:X4}) returned '{1}', expected '{2}'" -f $nc.Pid, $name, $nc.Expected) -ForegroundColor Red
        exit 1
    }
}
Write-Host "PASS: 官方型号名称精准命中 'Razer Basilisk V3 Pro 35K'，告别模糊泛称。" -ForegroundColor Green

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 5/6] 校验 8000Hz (8K) 超高回报率矩阵支持" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$rateCases = @(0x00CC, 0x00CD, 0x00D6, 0x00D7)
foreach ($devicePid in $rateCases) {
    $rates = $tRazer.GetMethod("GetSupportedPollingRatesForModel").Invoke($null, @([int]$devicePid, "Razer Basilisk V3 Pro 35K"))
    if ($rates -eq $null -or -not ($rates -contains 8000)) {
        Write-Host ("FAIL: GetSupportedPollingRatesForModel(0x{0:X4}) does not contain 8000Hz!" -f $devicePid) -ForegroundColor Red
        exit 1
    }
    if (-not ($rates -contains 125) -or -not ($rates -contains 1000) -or -not ($rates -contains 4000)) {
        Write-Host ("FAIL: GetSupportedPollingRatesForModel(0x{0:X4}) missing standard rate gears!" -f $devicePid) -ForegroundColor Red
        exit 1
    }
}
Write-Host "PASS: 巴蛇 V3 专业版 35K 成功激活 125Hz ~ 8000Hz 全阶超高轮询率矩阵！" -ForegroundColor Green

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 6/6] 校验 Focus Pro 35K Gen-2 顶级传感器规格" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

foreach ($devicePid in @(0x00CC, 0x00CD, 0x00D6, 0x00D7)) {
    $cap = $tRazer.GetMethod("GetDpiCapability").Invoke($null, @("Razer Basilisk V3 Pro 35K", [int]$devicePid))
    if ($cap -eq $null) {
        Write-Host ("FAIL: GetDpiCapability(0x{0:X4}) returned null!" -f $devicePid) -ForegroundColor Red
        exit 1
    }
    if ($cap.MaxDpi -ne 35000) {
        Write-Host ("FAIL: MaxDpi is {0}, expected 35000!" -f $cap.MaxDpi) -ForegroundColor Red
        exit 1
    }
    if ($cap.Step -ne 1) {
        Write-Host ("FAIL: Step is {0}, expected 1!" -f $cap.Step) -ForegroundColor Red
        exit 1
    }
}
Write-Host "PASS: 传感器规格精准识别为 Focus Pro 35K Gen-2 (35,000 DPI, 1-DPI步进)！" -ForegroundColor Green

Write-Host "`n>>> 全部 6 大项针对巴蛇 V3 专业版 35K 的测试用例 100% 验证通过！<<<" -ForegroundColor Green
exit 0
