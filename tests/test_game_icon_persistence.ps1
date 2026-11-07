# tests/test_game_icon_persistence.ps1
# 验证：
# 1. GameProcessManagerForm 具备本地图标持久化存储引擎 (SaveProcessIconToDisk & LoadProcessIconFromDisk)
# 2. 即使清空内存缓存或进程未运行，仍可从本地磁盘长期缓存完美还原游戏图标

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$asmPath = Join-Path $PSScriptRoot "..\FerrisPulse.exe"
if (-not (Test-Path $asmPath)) { $asmPath = "FerrisPulse.exe" }
$fullPath = (Resolve-Path $asmPath).Path
$bytes = [System.IO.File]::ReadAllBytes($fullPath)
$asm = [System.Reflection.Assembly]::Load($bytes)

$tGpm = $asm.GetType("RazerBatteryTray.GameProcessManagerForm")
if ($tGpm -eq $null) {
    Write-Host "FAIL: 未找到 GameProcessManagerForm 类型！" -ForegroundColor Red
    exit 1
}

Write-Host "--- 测试 1: 验证本地图标磁盘持久化接口存在 ---" -ForegroundColor Cyan

$mSave = $tGpm.GetMethod("SaveProcessIconToDisk", [System.Reflection.BindingFlags]'Static,Public,NonPublic', $null, [Type[]]@([string], [System.Drawing.Image]), $null)
$mLoad = $tGpm.GetMethod("LoadProcessIconFromDisk", [System.Reflection.BindingFlags]'Static,Public,NonPublic', $null, [Type[]]@([string]), $null)
$mGetCached = $tGpm.GetMethod("GetCachedProcessIcon", [System.Reflection.BindingFlags]'Static,Public,NonPublic', $null, [Type[]]@([string]), $null)

if ($mSave -eq $null -or $mLoad -eq $null -or $mGetCached -eq $null) {
    Write-Host "RED LIGHT: 未能找到 SaveProcessIconToDisk 或 LoadProcessIconFromDisk 接口！" -ForegroundColor Yellow
    exit 1
}

# 创建一个 22x22 的测试图标
$testBmp = New-Object System.Drawing.Bitmap 22, 22
$g = [System.Drawing.Graphics]::FromImage($testBmp)
$g.Clear([System.Drawing.Color]::FromArgb(0, 230, 118))
$g.Dispose()

$testExe = "ferris_test_game_" + [Guid]::NewGuid().ToString("N").Substring(0, 8) + ".exe"

# 1. 保存测试图标至磁盘 (显式 Object[] 数组转换避开 PowerShell PSObject 包装)
$params = [object[]]@([string]$testExe, [System.Drawing.Image]$testBmp)
$mSave.Invoke($null, $params)

# 2. 检查本地磁盘是否存在缓存文件
$localAppData = [Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)
$expectedPngName = [System.IO.Path]::GetFileNameWithoutExtension($testExe) + ".png"
$expectedPath = [System.IO.Path]::Combine($localAppData, "FerrisPulse", "GameIcons", $expectedPngName)

if (-not (Test-Path $expectedPath)) {
    Write-Host "FAIL: 图标未能成功写入本地缓存路径: $expectedPath" -ForegroundColor Red
    $testBmp.Dispose()
    exit 1
}
Write-Host "PASS: 成功生成本地持久化图标文件: $expectedPngName" -ForegroundColor Green

# 3. 清空内存缓存以模拟重启
$fCache = $tGpm.GetField("processIconCache", [System.Reflection.BindingFlags]'Static,NonPublic')
if ($fCache -ne $null) {
    $dict = $fCache.GetValue($null)
    if ($dict -ne $null) {
        $dict.Clear()
        Write-Host "已清空内存静态缓存字典 processIconCache，模拟软件重启" -ForegroundColor Gray
    }
}

# 4. 调用 GetCachedProcessIcon 读取，断言能从磁盘自动还原
$restored = $mGetCached.Invoke($null, [object[]]@([string]$testExe))
if ($restored -eq $null) {
    Write-Host "FAIL: 内存清空后未能从本地磁盘缓存还原图标！" -ForegroundColor Red
    if (Test-Path $expectedPath) { Remove-Item -Force $expectedPath }
    $testBmp.Dispose()
    exit 1
}

if ($restored.Width -ne 22 -or $restored.Height -ne 22) {
    Write-Host "FAIL: 还原的图标尺寸不符 (预期 22x22, 实际: $($restored.Width)x$($restored.Height))" -ForegroundColor Red
    if (Test-Path $expectedPath) { Remove-Item -Force $expectedPath }
    $testBmp.Dispose()
    $restored.Dispose()
    exit 1
}

Write-Host "PASS: 内存清空后成功从磁盘秒级还原 22x22 高清游戏图标！" -ForegroundColor Green

# 5. 清理测试产生的临时文件
if (Test-Path $expectedPath) { Remove-Item -Force $expectedPath }
$testBmp.Dispose()
$restored.Dispose()

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " ALL PASS: 守护列表游戏图标持久化引擎验证全部通过！" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
exit 0
