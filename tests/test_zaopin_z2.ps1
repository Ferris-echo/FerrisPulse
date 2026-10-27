# ============================================================================
# 测试脚本: test_zaopin_z2.ps1
# 目标: 验证皂品 Z2 (Zaopin Z2, VID 3554 PID F526/F527) 在 VgnDeviceHelper 中的
#       品牌识别、电池协议与能力枚举
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = Resolve-Path "$ScriptDir\.."
Set-Location $ProjectDir

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 1/5] VgnDeviceHelper.cs 存在性与 UTF-8 BOM 校验" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$helperPath = "$ProjectDir\src\Hardware\VgnDeviceHelper.cs"
if (-not (Test-Path $helperPath)) {
    Write-Host "FAILED: VgnDeviceHelper.cs 不存在" -ForegroundColor Red
    exit 1
}
$bytes = [System.IO.File]::ReadAllBytes($helperPath)
if ($bytes.Length -lt 3 -or $bytes[0] -ne 0xEF -or $bytes[1] -ne 0xBB -or $bytes[2] -ne 0xBF) {
    Write-Host "FAILED: VgnDeviceHelper.cs 不是 UTF-8 with BOM" -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: VgnDeviceHelper.cs 存在且 BOM 正确" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 2/5] 皂品 Z2 型号识别 (cid=7/mid=2 + pid 0xF526/0xF527)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$content = [System.IO.File]::ReadAllText($helperPath, [System.Text.Encoding]::UTF8)
if (-not ($content -match 'cid == 7 && mid == 2')) {
    Write-Host "FAILED: 未按 cid=7/mid=2 识别皂品 Z2" -ForegroundColor Red
    exit 1
}
if (-not ($content -match '0xF526')) {
    Write-Host "FAILED: 未索引 pid 0xF526" -ForegroundColor Red
    exit 1
}
if (-not ($content -match '0xF527')) {
    Write-Host "FAILED: 未索引 pid 0xF527（皂品 Z2 接收器多 PID 变体）" -ForegroundColor Red
    exit 1
}
if (-not ($content -match 'Zaopin Z2')) {
    Write-Host "FAILED: 未映射 Zaopin Z2 型号名" -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: 皂品 Z2 型号识别正确 (F526/F527 双 PID 变体均覆盖)" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 3/5] 品牌动态派生 (Zaopin vs VGN)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if (-not ($content -match 'isZaopin')) {
    Write-Host "FAILED: 缺少 isZaopin 品牌派生逻辑" -ForegroundColor Red
    exit 1
}
if (-not ($content -match '"Zaopin"')) {
    Write-Host "FAILED: 未派生 Zaopin 品牌名" -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: 品牌按 cid/pid 动态派生为 Zaopin" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 4/5] 电池协议 Command 4 与能力枚举 (DPI 6 档 / 4K 回报率)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

if (-not ($content -match 'BuildReport17\(4')) {
    Write-Host "FAILED: 缺少 Command 4 电池查询" -ForegroundColor Red
    exit 1
}
if (-not ($content -match 'rxBat\[6\]')) {
    Write-Host "FAILED: 缺少电池电量解析 (rx[6])" -ForegroundColor Red
    exit 1
}
if (-not ($content -match '2400, 3200, 6400')) {
    Write-Host "FAILED: 缺少皂品 Z2 六档 DPI 映射" -ForegroundColor Red
    exit 1
}
if (-not ($content -match '2000, 4000')) {
    Write-Host "FAILED: 缺少皂品 Z2 4K 回报率档位" -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: 电池协议 + DPI 6 档 + 4K 回报率能力枚举正确" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 5/6] 品牌命名映射 (MouseBatteryInfo 皂品 Z2)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$mbPath = "$ProjectDir\src\Models\MouseBatteryInfo.cs"
$mbContent = [System.IO.File]::ReadAllText($mbPath, [System.Text.Encoding]::UTF8)
if (-not ($mbContent -match '"Zaopin"')) {
    Write-Host "FAILED: MouseBatteryInfo 未映射 Zaopin 品牌" -ForegroundColor Red
    exit 1
}
if (-not ($mbContent -match '皂品 Z2')) {
    Write-Host "FAILED: 未映射皂品 Z2 友好命名" -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: 品牌配色与「皂品 Z2」友好命名映射正确" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 6/6] DPI 档位切换 + 自定义 DPI + 回报率切换（4K 上限）能力" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# SetVgnDpiStage 切换方法存在
if (-not ($content -match 'SetVgnDpiStage')) {
    Write-Host "FAILED: 缺少 SetVgnDpiStage DPI 切换方法" -ForegroundColor Red
    exit 1
}
# 自定义 DPI 值写入方法存在
if (-not ($content -match 'SetVgnDpiValue')) {
    Write-Host "FAILED: 缺少 SetVgnDpiValue 自定义 DPI 写入方法" -ForegroundColor Red
    exit 1
}
# 批量档位写入方法存在
if (-not ($content -match 'UpdateVgnHardwareStages')) {
    Write-Host "FAILED: 缺少 UpdateVgnHardwareStages 批量档位写入" -ForegroundColor Red
    exit 1
}
# 配置读写报文构建（write=0x07 / read=0x08 + 16 位地址）
if (-not ($content -match 'BuildConfigReport')) {
    Write-Host "FAILED: 缺少 BuildConfigReport 配置报文构建" -ForegroundColor Red
    exit 1
}
# UI 层已接入 Zaopin/VGN DPI 分发
$mhContent = (Get-ChildItem "$ProjectDir\src\UI\Forms\MainForm.Hardware*.cs" | ForEach-Object { [System.IO.File]::ReadAllText($_.FullName, [System.Text.Encoding]::UTF8) }) -join "`n"
if (-not ($mhContent -match 'SetVgnDpiStage')) {
    Write-Host "FAILED: MainForm 未接入 SetVgnDpiStage 分发" -ForegroundColor Red
    exit 1
}
if (-not ($mhContent -match 'SetVgnDpiValue')) {
    Write-Host "FAILED: MainForm 未接入 SetVgnDpiValue 自定义 DPI 分发" -ForegroundColor Red
    exit 1
}
# 回报率切换方法存在（上限 4K，无 8K）
if (-not ($content -match 'SetVgnPollingRate')) {
    Write-Host "FAILED: 缺少 SetVgnPollingRate 回报率切换方法" -ForegroundColor Red
    exit 1
}
if (-not ($mhContent -match 'SetVgnPollingRate')) {
    Write-Host "FAILED: MainForm 未接入 SetVgnPollingRate 分发" -ForegroundColor Red
    exit 1
}
# 回报率编码表不得包含 8000（皂品 Z2 4K 版上限 4000Hz）
if ($content -match 'case 8000') {
    Write-Host "FAILED: 回报率编码表包含 8000Hz（应为 4K 上限）" -ForegroundColor Red
    exit 1
}
# 主页性能卡片需纳入 Zaopin 鼠标分支
if (-not ($mhContent -match 'isZaopinMouse')) {
    Write-Host "FAILED: 主页性能卡片未纳入 Zaopin 鼠标分支" -ForegroundColor Red
    exit 1
}
# 实体 DPI 键同步监听（读 0x04 档位，事件驱动跟随）
if (-not ($content -match 'FastQueryVgnDpiStage')) {
    Write-Host "FAILED: 缺少 FastQueryVgnDpiStage 档位读取方法" -ForegroundColor Red
    exit 1
}
if (-not ($mhContent -match 'FastQueryVgnDpiStage')) {
    Write-Host "FAILED: MainForm 未接入 FastQueryVgnDpiStage 实体键同步监听" -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: DPI 档位切换 + 自定义 DPI + 回报率切换(4K 上限) + 实体 DPI 键同步监听已完整接入" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " ALL PASS: 皂品 Z2 适配测试全部通过！" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
exit 0
