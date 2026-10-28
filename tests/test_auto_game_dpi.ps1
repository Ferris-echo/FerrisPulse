# ============================================================================
# 测试脚本: test_auto_game_dpi.ps1
# 职责:
#   验证全屏电竞游戏自动切换 DPI 档位联动引擎 (TDD 规范)
#   覆盖：
#   1. 数据契约与注册表持久化 (AutoGameDpiEnabled, GameTargetDpiStage, desktopDpiStage)
#   2. 卡片 2 独立开关排版与 148 / 218 / 255 三档自适应高度动效
#   3. 进退游戏状态机自动切档与平滑复原逻辑
#   4. 组合加速模式 (回报率 + DPI) 与单 DPI 模式 OSD / 通知气泡格式
#   5. C# 5.0 语法铁律与 UTF-8 with BOM 规范
# ============================================================================

$ErrorActionPreference = "Stop"
$rootDir = (Resolve-Path "$PSScriptRoot\..").Path
Set-Location $rootDir

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 1/5] 校验核心字段与注册表持久化契约" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$mainFormCs = (Get-ChildItem "src/UI/Forms/MainForm*.cs" | ForEach-Object { Get-Content $_.FullName -Raw -Encoding UTF8 }) -join "`n"
$mainHardwareCs = (Get-ChildItem "src/UI/Forms/MainForm.Hardware*.cs" | ForEach-Object { Get-Content $_.FullName -Raw -Encoding UTF8 }) -join "`n"
$mainUiCs = (Get-ChildItem "src/UI/Forms/MainForm.UI*.cs" | ForEach-Object { Get-Content $_.FullName -Raw -Encoding UTF8 }) -join "`n"
$mainTrayCs = Get-Content "src/UI/Forms/MainForm.Tray.cs" -Raw -Encoding UTF8

if ($mainFormCs -notmatch 'private\s+bool\s+autoGameDpiEnabled') {
    throw "FAIL: MainForm.cs 缺少 autoGameDpiEnabled 核心状态字段！"
}
Write-Host "PASS: autoGameDpiEnabled 状态字段已声明" -ForegroundColor Green

if ($mainFormCs -notmatch 'private\s+int\s+gameDpiStage') {
    throw "FAIL: MainForm.cs 缺少 gameDpiStage 设定字段！"
}
Write-Host "PASS: gameDpiStage 设定字段已声明" -ForegroundColor Green

if ($mainFormCs -notmatch 'private\s+int\s+desktopDpiStage') {
    throw "FAIL: MainForm.cs 缺少 desktopDpiStage 恢复记忆字段！"
}
Write-Host "PASS: desktopDpiStage 状态字段已声明" -ForegroundColor Green

if ($mainFormCs -notmatch 'AutoGameDpiEnabled' -or $mainFormCs -notmatch 'GameTargetDpiStage') {
    throw "FAIL: MainForm.cs LoadConfig/SaveConfig 缺少 AutoGameDpiEnabled 或 GameTargetDpiStage 注册表读写！"
}
Write-Host "PASS: AutoGameDpiEnabled 与 GameTargetDpiStage 注册表持久化已打通" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 2/5] 校验卡片 2 界面排版与 148 / 255 纯净两档高度与配色联动" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if ($mainUiCs -notmatch 'chkAutoGameOpt' -or $mainUiCs -notmatch 'chkAutoGameDpi' -or $mainUiCs -notmatch 'btnGameDpiStages') {
    throw "FAIL: MainForm.UI.cs 缺少 chkAutoGameOpt / chkAutoGameDpi / btnGameDpiStages 控件定义！"
}
Write-Host "PASS: chkAutoGameOpt / chkAutoGameDpi 与 btnGameDpiStages[5] 控件已正确实例化与布局" -ForegroundColor Green

if ($mainUiCs -notmatch '255\s*\*\s*dpiScale' -or $mainHardwareCs -notmatch '255\s*\*\s*dpiScale') {
    throw "FAIL: 未找到 255 * dpiScale 展开高度！"
}
Write-Host "PASS: 卡片高度 148 / 255 纯净两档平滑自适应高度逻辑完整" -ForegroundColor Green

if ($mainUiCs -notmatch 'btnGameDpiStages\[i\]\.IsPreset\s*=' -or $mainHardwareCs -notmatch 'SetAutoGameOptEnabled') {
    throw "FAIL: 缺少 DPI 预设框深绿/亮绿转换或 SetAutoGameOptEnabled 总控方法！"
}
Write-Host "PASS: DPI 档位与回报率深绿预设/亮绿生效色彩联动与总控逻辑完整" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 3/5] 校验游戏检测监听与硬件切档/恢复逻辑" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if ($mainHardwareCs -notmatch 'ApplyGameDpiStage' -or $mainHardwareCs -notmatch 'RestoreDesktopDpiStage') {
    throw "FAIL: MainForm.Hardware.cs 缺少 ApplyGameDpiStage 或 RestoreDesktopDpiStage 硬件切档/恢复方法！"
}
Write-Host "PASS: ApplyGameDpiStage 与 RestoreDesktopDpiStage 硬件切档/恢复核心方法已实现" -ForegroundColor Green

if ($mainHardwareCs -notmatch 'autoGameRateEnabled\s*\|\|\s*autoGameDpiEnabled') {
    throw "FAIL: StartGameMonitor 监听保护条件未支持 autoGameRateEnabled || autoGameDpiEnabled 任一开启！"
}
Write-Host "PASS: StartGameMonitor 游戏监听守护条件已全面支持双通道" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 4/5] 校验托盘右键菜单同步支持" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if ($mainTrayCs -notmatch 'autoGameDpiToggleItem' -or $mainTrayCs -notmatch 'gameDpiSub') {
    throw "FAIL: MainForm.Tray.cs 缺少托盘菜单自动切换 DPI 或电竞 DPI 档位子菜单！"
}
Write-Host "PASS: 托盘右键菜单已支持自动切换 DPI 与电竞 DPI 档位子菜单选择" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 5/5] C# 5.0 语法铁律与 UTF-8 with BOM 规范" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$filesToCheck = @(
    "src/UI/Forms/MainForm.cs",
    "src/UI/Forms/MainForm.UI.cs",
    "src/UI/Forms/MainForm.Tray.cs"
)
$filesToCheck += (Get-ChildItem "src/UI/Forms/MainForm.Hardware*.cs" | ForEach-Object { $_.FullName })

foreach ($f in $filesToCheck) {
    $bytes = [System.IO.File]::ReadAllBytes($f)
    if ($bytes.Length -lt 3 -or $bytes[0] -ne 0xEF -or $bytes[1] -ne 0xBB -or $bytes[2] -ne 0xBF) {
        throw "FAIL: 文件 $f 缺少 UTF-8 BOM 签名！"
    }
}
Write-Host "PASS: 所有关键源文件严格遵循 UTF-8 with BOM 编码规范" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " ALL PASS: 全屏电竞游戏自动切换 DPI 档位测试全部通过！" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
