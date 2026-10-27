# tests/test_logitech_stage_from_dpi.ps1
# 回归验证：点「立即刷新」后 DPI 档位高亮必须跟随实测 DPI，不得跳回 800 / 固定第 2 档
#   1. MatchDpiStage(dpi, stages) 必须由实测 DPI 反推档位（精确命中优先，未命中取最近档位）
#   2. 空槽（<=0）必须跳过，且返回值恒 >= 1（游戏切换还原链路依赖 >0）
#   3. UI 集成：Dpi=3200 时必须高亮第 4 档（3200），严禁高亮第 2 档（800）
# 说明：全内存反射装载，严禁 Add-Type -Path 锁死可执行文件句柄。

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$asmPath = Join-Path $PSScriptRoot "..\FerrisPulse.exe"
if (-not (Test-Path $asmPath)) { $asmPath = "FerrisPulse.exe" }
$bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $asmPath).Path)
$asm = [System.Reflection.Assembly]::Load($bytes)

$tLogi = $asm.GetType("RazerBatteryTray.LogitechDeviceHelper")
if ($null -eq $tLogi) {
    Write-Host "FAIL: Cannot find RazerBatteryTray.LogitechDeviceHelper" -ForegroundColor Red
    exit 1
}

# ---------------------------------------------------------------
# 测试 1：DPI -> 档位反推
# ---------------------------------------------------------------
$mMatch = $tLogi.GetMethod("MatchDpiStage", [System.Reflection.BindingFlags]"Public,Static", $null, @([int], [int[]]), $null)
if ($null -eq $mMatch) {
    Write-Host "FAIL: MatchDpiStage(int,int[]) 不存在" -ForegroundColor Red
    exit 1
}

$cases = @(
    @{ Dpi = 400;  Stages = [int[]]@(400, 800, 1600, 3200, 6400); Expect = 1 },
    @{ Dpi = 800;  Stages = [int[]]@(400, 800, 1600, 3200, 6400); Expect = 2 },
    @{ Dpi = 3200; Stages = [int[]]@(400, 800, 1600, 3200, 6400); Expect = 4 },
    @{ Dpi = 6400; Stages = [int[]]@(400, 800, 1600, 3200, 6400); Expect = 5 },
    @{ Dpi = 3190; Stages = [int[]]@(400, 800, 1600, 3200, 6400); Expect = 4 },
    @{ Dpi = 1200; Stages = [int[]]@(400, 800, 1600, 3200, 6400); Expect = 2 },
    @{ Dpi = 800;  Stages = [int[]]@(400, 0, 1600, 3200, 6400);   Expect = 1 },
    @{ Dpi = 0;    Stages = [int[]]@(400, 800, 1600, 3200, 6400); Expect = 1 }
)

foreach ($c in $cases) {
    $actual = [int]$mMatch.Invoke($null, @([int]$c.Dpi, $c.Stages))
    if ($actual -ne $c.Expect) {
        Write-Host ("FAIL: MatchDpiStage({0}) 期望第 {1} 档，实际第 {2} 档" -f $c.Dpi, $c.Expect, $actual) -ForegroundColor Red
        exit 1
    }
}
Write-Host "PASS: DPI -> 档位反推全量通过（含空槽跳过、0 DPI 不返回 0）" -ForegroundColor Green

# ---------------------------------------------------------------
# 测试 2：UI 集成 —— 刷新后高亮必须是 3200，不是 800
# ---------------------------------------------------------------
$tMain = $asm.GetType("RazerBatteryTray.MainForm")
$tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo")
$tCat = $asm.GetType("RazerBatteryTray.DeviceCategory")
if ($null -eq $tMain -or $null -eq $tInfo) {
    Write-Host "FAIL: MainForm / MouseBatteryInfo 类型缺失" -ForegroundColor Red
    exit 1
}

$form = [Activator]::CreateInstance($tMain, @($false))
$null = $form.Handle

$stages = [int[]]@(400, 800, 1600, 3200, 6400)
$info = [Activator]::CreateInstance($tInfo)
$info.Brand = "Logitech"
$info.DeviceId = "Logitech:046D:C53F:1"
$info.DeviceName = "G304 Lightspeed Wireless Gaming Mouse"
$info.Category = [Enum]::Parse($tCat, "Mouse")
$info.Transport = "Lightspeed 无线"
$info.IsConnected = $true
$info.IsSleeping = $false
$info.BatteryPercent = 90
$info.Dpi = 3200
$info.DpiY = 3200
$info.DpiStage = [int]$mMatch.Invoke($null, @([int]3200, $stages))
$info.DpiStageCount = 5
$info.DpiStages = $stages
$info.DpiStagesY = $stages
$info.PollingRate = 1000
$info.SupportedPollingRates = [int[]]@(125, 250, 500, 1000)

$mUpdate = $null
foreach ($m in $tMain.GetMethods([System.Reflection.BindingFlags]"Instance,NonPublic")) {
    if ($m.Name -eq "UpdatePerformanceSection" -and $m.GetParameters().Count -eq 2) { $mUpdate = $m; break }
}
if ($null -eq $mUpdate) {
    Write-Host "FAIL: UpdatePerformanceSection(info, accent) 不存在" -ForegroundColor Red
    $form.Dispose()
    exit 1
}
$null = $mUpdate.Invoke($form, @($info, $info.BrandColor))

$fld = $tMain.GetField("btnDpiStages", [System.Reflection.BindingFlags]"Instance,NonPublic")
$btns = $fld.GetValue($form)
if ($null -eq $btns) {
    Write-Host "FAIL: btnDpiStages 未初始化" -ForegroundColor Red
    $form.Dispose()
    exit 1
}

$selectedIdx = -1
for ($i = 0; $i -lt $btns.Length; $i++) {
    if ($null -ne $btns[$i] -and $btns[$i].Selected) { $selectedIdx = $i }
}
if ($selectedIdx -ne 3) {
    Write-Host ("FAIL: DPI=3200 时高亮索引应为 3(3200)，实际为 {0}" -f $selectedIdx) -ForegroundColor Red
    $form.Dispose()
    exit 1
}
if ($btns[1].Selected) {
    Write-Host "FAIL: 800 档仍被选中（刷新跳回 800 的旧缺陷复现）" -ForegroundColor Red
    $form.Dispose()
    exit 1
}
Write-Host "PASS: 刷新后档位高亮跟随实测 DPI（3200 → 第 4 档）" -ForegroundColor Green

$form.Dispose()
Write-Host "ALL PASS: 刷新跳回 800 的缺陷已被回归测试锁定" -ForegroundColor Green
exit 0
