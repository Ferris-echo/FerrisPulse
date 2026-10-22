# ============================================================================
# 测试脚本: test_virtual_hid_filter.ps1
# 目标: 验证 VHF 虚拟 HID 过滤（网易 UU 远程等软件创建的虚拟键鼠），
#       确保向导与设备列表不会把虚拟设备当成可监控的 2.4G 外设。
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
Write-Host " [测试 1/3] VirtualHidFilter BOM / C#5 与源码接入标记" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$filterPath = "$ProjectDir\src\Hardware\VirtualHidFilter.cs"
Assert-Bom $filterPath
$content = [System.IO.File]::ReadAllText($filterPath, [System.Text.Encoding]::UTF8)
if ($content -match '\$"') { Write-Host "FAILED: 字符串插值" -ForegroundColor Red; exit 1 }
if ($content -match 'nameof\s*\(') { Write-Host "FAILED: nameof" -ForegroundColor Red; exit 1 }
if (-not $content.Contains("HID_DEVICE_SYSTEM_VHF")) { Write-Host "FAILED: 缺少 VHF 识别关键字" -ForegroundColor Red; exit 1 }

$genContent = [System.IO.File]::ReadAllText("$ProjectDir\src\Hardware\GenericHidHelper.cs", [System.Text.Encoding]::UTF8)
if (-not $genContent.Contains("VirtualHidFilter.IsVirtualHidPath")) { Write-Host "FAILED: GenericHidHelper 未接入过滤" -ForegroundColor Red; exit 1 }
$dmContent = [System.IO.File]::ReadAllText("$ProjectDir\src\Hardware\DeviceManager.cs", [System.Text.Encoding]::UTF8)
if (-not $dmContent.Contains("VirtualHidFilter.IsVirtualHidIdentity")) { Write-Host "FAILED: DeviceManager 未接入兜底过滤" -ForegroundColor Red; exit 1 }
Write-Host "PASSED: 过滤模块存在，向导与 DeviceManager 均已接入" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 2/3] VHF 路径/实例识别断言" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$binPath = "$ProjectDir\FerrisPulse.exe"
if (-not (Test-Path $binPath)) {
    Write-Host "编译二进制以供反射测试..." -ForegroundColor Yellow
    cmd /c "$ProjectDir\build.bat" | Out-Null
}
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($binPath))
$type = $asm.GetType("RazerBatteryTray.VirtualHidFilter")
if ($null -eq $type) { Write-Host "FAILED: 未找到 VirtualHidFilter" -ForegroundColor Red; exit 1 }
$flags = [System.Reflection.BindingFlags]"Public,Static"
$isPath = $type.GetMethod("IsVirtualHidPath", $flags)
$isInstance = $type.GetMethod("IsVirtualHidInstance", $flags)
$isIdentity = $type.GetMethod("IsVirtualHidIdentity", $flags)

$vhfPath = '\\?\hid#hid_device_system_vhf&col01#7&376a25da&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}'
$acerPath = '\\?\hid#vid_249a&pid_5c2f&mi_02#7&2955fd3&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}'
$vkPath = '\\?\hid#vid_374a&pid_a272&mi_02#7&2e7b54cc&0&0000#{4d1e55b2-f16f-11cf-88cb-001111000030}'

if (-not $isPath.Invoke($null, @($vhfPath))) { Write-Host "FAILED: VHF 路径未识别" -ForegroundColor Red; exit 1 }
if ($isPath.Invoke($null, @($acerPath))) { Write-Host "FAILED: Acer 路径被误判为虚拟" -ForegroundColor Red; exit 1 }
if ($isPath.Invoke($null, @($vkPath))) { Write-Host "FAILED: Valkyrie 路径被误判为虚拟" -ForegroundColor Red; exit 1 }
if (-not $isInstance.Invoke($null, @('VHF\VID_5E2A&PID_71B4'))) { Write-Host "FAILED: VHF 硬件 ID 未识别" -ForegroundColor Red; exit 1 }
if ($isInstance.Invoke($null, @('HID\VID_249A&PID_5C2F&MI_02\7&2955FD3&0&0000'))) { Write-Host "FAILED: Acer 实例被误判" -ForegroundColor Red; exit 1 }
Write-Host "PASSED: 仅 VHF 路径/实例命中，Acer 与 Valkyrie 实体设备不受影响" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 3/3] 设备身份兜底过滤断言" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$vhfOk = $isIdentity.Invoke($null, @('HID:5E2A:71B4', 'HID:5E2A:71B4', 'HID\HID_DEVICE_SYSTEM_VHF&COL01\2&88D67F4&0&0000'))
$acerOk = $isIdentity.Invoke($null, @('ACER:249A:5C2F', 'HID:249A:5C2F', 'HID\VID_249A&PID_5C2F&MI_02\7&2955FD3&0&0000'))
$vkOk = $isIdentity.Invoke($null, @('Valkyrie:374A:A272:KB', 'HID:374A:A272', 'HID\VID_374A&PID_A272&MI_02\7&2E7B54CC&0&0000'))
if (-not $vhfOk) { Write-Host "FAILED: 虚拟设备身份未被过滤" -ForegroundColor Red; exit 1 }
if ($acerOk) { Write-Host "FAILED: Acer 身份被误过滤" -ForegroundColor Red; exit 1 }
if ($vkOk) { Write-Host "FAILED: Valkyrie 身份被误过滤" -ForegroundColor Red; exit 1 }
Write-Host "PASSED: HID:5E2A:71B4 被过滤，Acer / Valkyrie 实体设备保留" -ForegroundColor Green

Write-Host "`nALL VIRTUAL HID FILTER TESTS PASSED" -ForegroundColor Green
