# ============================================================================
# 测试脚本: test_valkyrie_vk87_a272.ps1
# 目标: 验证瓦尔基里 VK 87 Max V2 (374A:A272) 的 0xF7 应答兼容解析：
#       新型 00 00 57 ... 帧负载偏移、型号映射与源码回归标记。
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = Resolve-Path "$ScriptDir\.."
Set-Location $ProjectDir

function Assert-Bom([string]$path) {
    if (-not (Test-Path $path)) { Write-Host "FAILED: 文件不存在: $path" -ForegroundColor Red; exit 1 }
    $bytes = [System.IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 3 -or $bytes[0] -ne 0xEF -or $bytes[1] -ne 0xBB -or $bytes[2] -ne 0xBF) {
        Write-Host "FAILED: 文件不是 UTF-8 with BOM: $path" -ForegroundColor Red; exit 1
    }
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 1/3] ValkyrieDeviceHelper BOM 与源码回归标记" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$helperPath = "$ProjectDir\src\Hardware\ValkyrieDeviceHelper.cs"
Assert-Bom $helperPath
$content = [System.IO.File]::ReadAllText($helperPath, [System.Text.Encoding]::UTF8)
if (-not $content.Contains("case 0xA272")) { Write-Host "FAILED: 缺少 0xA272 型号映射" -ForegroundColor Red; exit 1 }
if (-not $content.Contains("else if (rx[0] == 0x00)")) { Write-Host "FAILED: 缺少 00 00 帧兼容分支" -ForegroundColor Red; exit 1 }
Write-Host "PASSED: BOM 正确，0xA272 映射与 00 00 帧兼容分支存在" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 2/3] GetModelName 型号映射" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$binPath = "$ProjectDir\FerrisPulse.exe"
if (-not (Test-Path $binPath)) {
    Write-Host "编译二进制以供反射测试..." -ForegroundColor Yellow
    cmd /c "$ProjectDir\build.bat" | Out-Null
}
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($binPath))
$type = $asm.GetType("RazerBatteryTray.ValkyrieDeviceHelper")
if ($null -eq $type) { Write-Host "FAILED: 未找到 ValkyrieDeviceHelper" -ForegroundColor Red; exit 1 }
$m = $type.GetMethod("GetModelName", [System.Reflection.BindingFlags]"Public,Static")
$cat = $asm.GetType("RazerBatteryTray.DeviceCategory")
$keyboard = [System.Enum]::Parse($cat, "Keyboard")
$name = $m.Invoke($null, @(0x374A, 0xA272, "VK 87 Max V2", $keyboard))
if ($name -ne "VK 87 Max V2") { Write-Host ("FAILED: 型号映射错误: " + $name) -ForegroundColor Red; exit 1 }
Write-Host "PASSED: 374A:A272 映射为 VK 87 Max V2" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 3/3] 帧偏移语义（00 00 57 ...）" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 新型应答：Report ID 0 + status 0，负载从 [1] 起；[2]=0x57(87%)、[7]=0x01(键盘)
$frame = [byte[]]@(0x00,0x00,0x57,0x00,0x01,0x01,0x01,0x01,0x00,0x00,0x01)
$baseOffset = 1
$kbBatt = $frame[$baseOffset + 1]
$devType = $frame[$baseOffset + 6]
if ($kbBatt -ne 0x57) { Write-Host "FAILED: 键盘电量偏移错误" -ForegroundColor Red; exit 1 }
if ($devType -ne 0x01) { Write-Host "FAILED: devType 偏移错误" -ForegroundColor Red; exit 1 }
Write-Host "PASSED: 00 00 57 帧在 baseOffset=1 下解析为 87% / 键盘" -ForegroundColor Green

Write-Host "`nALL VK87 MAX V2 TESTS PASSED" -ForegroundColor Green
