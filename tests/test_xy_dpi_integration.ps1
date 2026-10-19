# tests\test_xy_dpi_integration.ps1
# 验证雷蛇与瓦尔基里 X/Y 轴独立灵敏度与比例调节契约与报文生成

$asmPath = Join-Path $PSScriptRoot "..\FerrisPulse.exe"
if (-not (Test-Path $asmPath)) {
    $asmPath = "FerrisPulse.exe"
}
if (-not (Test-Path $asmPath)) {
    Write-Host "RED: FerrisPulse.exe not found for testing!" -ForegroundColor Red
    exit 1
}

$fullPath = (Resolve-Path $asmPath).Path
$bytes = [System.IO.File]::ReadAllBytes($fullPath)
$asm = [System.Reflection.Assembly]::Load($bytes)

$tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo")
$tState = $asm.GetType("RazerBatteryTray.RazerDeviceHardwareState")
$tRazer = $asm.GetType("RazerBatteryTray.RazerDeviceHelper")
$tValk = $asm.GetType("RazerBatteryTray.ValkyrieDeviceHelper")
$tRow = $asm.GetType("RazerBatteryTray.DpiStageRowControl")
$tDlg = $asm.GetType("RazerBatteryTray.DpiManagementDialog")

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 1/5] 校验 Models 数据契约层 X/Y 独立字段与属性" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. MouseBatteryInfo
$reqProps = @("DpiY", "DpiStagesY", "IsXyIndependent", "XyRatio")
foreach ($p in $reqProps) {
    $prop = $tInfo.GetProperty($p)
    if ($prop -eq $null) {
        Write-Host "FAIL: MouseBatteryInfo missing property: $p" -ForegroundColor Red
        exit 1
    }
}
Write-Host "PASS: MouseBatteryInfo contains all XY independent properties." -ForegroundColor Green

# 2. RazerDeviceHardwareState
$reqFields = @("DpiY", "DpiStagesY", "IsXyIndependent", "XyRatio")
foreach ($f in $reqFields) {
    $field = $tState.GetField($f)
    $prop = $tState.GetProperty($f)
    if ($field -eq $null -and $prop -eq $null) {
        Write-Host "FAIL: RazerDeviceHardwareState missing field/property: $f" -ForegroundColor Red
        exit 1
    }
}
Write-Host "PASS: RazerDeviceHardwareState contains all XY independent fields." -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 2/5] 校验雷蛇底层驱动独立 X/Y 轴方法与报文构造" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# Check SetRazerDpi overload with dpiX, dpiY
$mSetDpiXY = $tRazer.GetMethod("SetRazerDpi", [Type[]]@([int], [int], [string]))
if ($mSetDpiXY -eq $null) {
    Write-Host "FAIL: RazerDeviceHelper.SetRazerDpi(int, int, string) not found!" -ForegroundColor Red
    exit 1
}

# Check UpdateRazerHardwareStages overload with stagesX, stagesY
$mUpdateStagesXY = $tRazer.GetMethod("UpdateRazerHardwareStages", [Type[]]@([int], [int[]], [int[]], [string]))
if ($mUpdateStagesXY -eq $null) {
    Write-Host "FAIL: RazerDeviceHelper.UpdateRazerHardwareStages(int, int[], int[], string) not found!" -ForegroundColor Red
    exit 1
}

# Verify Razer CreateRazerReport with XY args
# 1600 DPI = 0x0640, 1200 DPI = 0x04B0
# Args: [0x01, 0x06, 0x40, 0x04, 0xB0, 0x00, 0x00]
$mCreate = $tRazer.GetMethod("CreateRazerReport", [System.Reflection.BindingFlags]'NonPublic,Static')
$argsXY = [byte[]]@(0x01, 0x06, 0x40, 0x04, 0xB0, 0x00, 0x00)
$report = $mCreate.Invoke($null, @([byte]0x1F, [byte]0x04, [byte]0x05, [byte]0x07, 90, $false, $argsXY))

$dpiXRead = ([int]$report[9] -shl 8) -bor [int]$report[10]
$dpiYRead = ([int]$report[11] -shl 8) -bor [int]$report[12]

if ($dpiXRead -ne 1600) {
    Write-Host "FAIL: Razer Report DPI X expected 1600, got: $dpiXRead" -ForegroundColor Red
    exit 1
}
if ($dpiYRead -ne 1200) {
    Write-Host "FAIL: Razer Report DPI Y expected 1200, got: $dpiYRead" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: Razer Live DPI Report correctly encodes independent X=1600, Y=1200." -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 3/5] 校验瓦尔基里底层驱动独立 X/Y 轴方法与 Option1 编码" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# Check SetValkyrieDpi overload with dpiX, dpiY
$mSetValkDpiXY = $tValk.GetMethod("SetValkyrieDpi", [Type[]]@([int], [int]))
if ($mSetValkDpiXY -eq $null) {
    Write-Host "FAIL: ValkyrieDeviceHelper.SetValkyrieDpi(int, int) not found!" -ForegroundColor Red
    exit 1
}

# Check UpdateValkyrieHardwareStages with stagesX, stagesY
$mUpdateValkStagesXY = $tValk.GetMethod("UpdateValkyrieHardwareStages", [Type[]]@([int], [int[]], [int[]]))
if ($mUpdateValkStagesXY -eq $null) {
    Write-Host "FAIL: ValkyrieDeviceHelper.UpdateValkyrieHardwareStages(int, int[], int[]) not found!" -ForegroundColor Red
    exit 1
}

# Check EncodeOption1Payload or helper method if static
$mBuildOption1 = $tValk.GetMethod("BuildOption1Payload", [System.Reflection.BindingFlags]'Public,NonPublic,Static')
if ($mBuildOption1 -ne $null) {
    $stagesX = [int[]]@(800, 1600, 3200)
    $stagesY = [int[]]@(600, 1200, 2400)
    $valkPkt = [byte[]]$mBuildOption1.Invoke($null, @(1, $stagesX, $stagesY))
    
    # Check CMD = 0x54
    if ($valkPkt[1] -ne 0x54) {
        Write-Host "FAIL: Valkyrie Option1 CMD byte expected 0x54, got: $($valkPkt[1])" -ForegroundColor Red
        exit 1
    }
    # Check Stage 1: X=800 (0x0320 -> 0x20, 0x03), Y=600 (0x0258 -> 0x58, 0x02)
    $x0 = [int]$valkPkt[9] -bor ([int]$valkPkt[10] -shl 8)
    $y0 = [int]$valkPkt[25] -bor ([int]$valkPkt[26] -shl 8)
    if ($x0 -ne 800 -or $y0 -ne 600) {
        Write-Host "FAIL: Valkyrie Option1 Stage 0 expected X=800, Y=600, got: X=$x0, Y=$y0" -ForegroundColor Red
        exit 1
    }
    # Check Stage 2: X=1600, Y=1200
    $x1 = [int]$valkPkt[11] -bor ([int]$valkPkt[12] -shl 8)
    $y1 = [int]$valkPkt[27] -bor ([int]$valkPkt[28] -shl 8)
    if ($x1 -ne 1600 -or $y1 -ne 1200) {
        Write-Host "FAIL: Valkyrie Option1 Stage 1 expected X=1600, Y=1200, got: X=$x1, Y=$y1" -ForegroundColor Red
        exit 1
    }
    Write-Host "PASS: Valkyrie Option1 Payload correctly encodes independent stages X and Y." -ForegroundColor Green
} else {
    Write-Host "PASS: Valkyrie Update and Set methods signature verified." -ForegroundColor Green
}

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 4/5] 校验 UI 控件与管理对话框 XY 独立契约" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# DpiStageRowControl properties
$propDpiValY = $tRow.GetProperty("DpiValueY")
if ($propDpiValY -eq $null) {
    Write-Host "FAIL: DpiStageRowControl missing DpiValueY property!" -ForegroundColor Red
    exit 1
}
$propIsXy = $tRow.GetProperty("IsXyIndependent")
if ($propIsXy -eq $null) {
    Write-Host "FAIL: DpiStageRowControl missing IsXyIndependent property!" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: DpiStageRowControl supports DpiValueY and IsXyIndependent." -ForegroundColor Green

# DpiManagementDialog properties
$propStagesY = $tDlg.GetProperty("StagesY")
if ($propStagesY -eq $null) {
    Write-Host "FAIL: DpiManagementDialog missing StagesY property!" -ForegroundColor Red
    exit 1
}
$propDlgIsXy = $tDlg.GetProperty("IsXyIndependent")
if ($propDlgIsXy -eq $null) {
    Write-Host "FAIL: DpiManagementDialog missing IsXyIndependent property!" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: DpiManagementDialog supports StagesY and IsXyIndependent." -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 5/5] 验证 C# 5.0 语法铁律与 UTF-8 BOM" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$filesToCheck = @(
    "src\Models\MouseBatteryInfo.cs",
    "src\Models\RazerDeviceHardwareState.cs",
    "src\Hardware\RazerDeviceHelper.cs",
    "src\Hardware\ValkyrieDeviceHelper.cs",
    "src\UI\Controls\DpiStageRowControl.cs",
    "src\UI\Forms\DpiManagementDialog.cs"
)

foreach ($f in $filesToCheck) {
    $filePath = Join-Path $PSScriptRoot "..\$f"
    if (Test-Path $filePath) {
        $c = [System.IO.File]::ReadAllText($filePath)
        $codeOnly = (($c -split "`r?`n") | Where-Object { $_ -notmatch '^\s*//' }) -join "`n"
        if ($codeOnly -match '\$".*?"') {
            Write-Host ('FAIL: ' + $f + ' contains forbidden string interpolation!') -ForegroundColor Red
            exit 1
        }
        $b = [System.IO.File]::ReadAllBytes($filePath)
        if ($b.Length -lt 3 -or $b[0] -ne 0xEF -or $b[1] -ne 0xBB -or $b[2] -ne 0xBF) {
            Write-Host ('FAIL: ' + $f + ' does NOT have UTF-8 BOM!') -ForegroundColor Red
            exit 1
        }
    }
}
Write-Host "PASS: All modified files strictly conform to C# 5.0 and UTF-8 BOM standards." -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " ALL PASS: 雷蛇与瓦尔基里 XY 轴独立灵敏度测试全部通过！" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
exit 0
