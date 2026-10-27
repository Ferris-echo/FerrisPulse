# tests\test_window_position_persistence.ps1
# 验证主窗体启动时位置记忆能够准确从注册表恢复，且不会被居中坐标覆盖

$ErrorActionPreference = "Stop"

$regPath = "HKCU:\Software\FerrisPulse"
if (-not (Test-Path $regPath)) {
    New-Item -Path $regPath -Force | Out-Null
}

$expectedX = 500
$expectedY = 260

# 1. 预先向注册表写入特定坐标
Set-ItemProperty -Path $regPath -Name "WindowX" -Value $expectedX -Type DWord
Set-ItemProperty -Path $regPath -Name "WindowY" -Value $expectedY -Type DWord

Write-Host "Injected test coordinates into registry: ($expectedX, $expectedY)"

# 2. 反射加载并实例化 MainForm
$asmPath = Join-Path $PSScriptRoot "..\FerrisPulse.exe"
if (-not (Test-Path $asmPath)) { $asmPath = "FerrisPulse.exe" }
$fullPath = (Resolve-Path $asmPath).Path
$bytes = [System.IO.File]::ReadAllBytes($fullPath)
$asm = [System.Reflection.Assembly]::Load($bytes)

$tMainForm = $asm.GetType("RazerBatteryTray.MainForm")
if ($tMainForm -eq $null) {
    Write-Host "FAIL: Type RazerBatteryTray.MainForm not found" -ForegroundColor Red
    exit 1
}

# 实例化 MainForm(false) - 非自启动模式
$form = [Activator]::CreateInstance($tMainForm, @($false))
[System.Windows.Forms.Form]$winForm = $form

# 触发句柄与窗口加载
$handle = $winForm.Handle
$winForm.Visible = $true

# 3. 计算在当前屏幕工作区（含 Headless CI 虚拟低分辨率屏）下的合法安全坐标
$screen = [System.Windows.Forms.Screen]::PrimaryScreen
$wa = $screen.WorkingArea
$expectedClampedX = [Math]::Max($wa.Left, [Math]::Min($expectedX, [Math]::Max($wa.Left, $wa.Right - $winForm.Width)))
$expectedClampedY = [Math]::Max($wa.Top, [Math]::Min($expectedY, [Math]::Max($wa.Top, $wa.Bottom - $winForm.Height)))

# 4. 校验窗口坐标与注册表坐标
$actualX = $winForm.Location.X
$actualY = $winForm.Location.Y

$regX = (Get-ItemProperty -Path $regPath).WindowX
$regY = (Get-ItemProperty -Path $regPath).WindowY

Write-Host "Form Location: ($actualX, $actualY)"
Write-Host "Expected Clamped Location: ($expectedClampedX, $expectedClampedY)"
Write-Host "Registry Location: ($regX, $regY)"

$failed = $false

# 检查窗口坐标是否准确恢复（允许屏幕防溢出安全限制）
if ($actualX -ne $expectedClampedX -or $actualY -ne $expectedClampedY) {
    Write-Host "FAIL: Form location ($actualX, $actualY) does not match expected clamped ($expectedClampedX, $expectedClampedY)" -ForegroundColor Red
    $failed = $true
} else {
    Write-Host "PASS: Form location matches expected ($actualX, $actualY)" -ForegroundColor Green
}

# 检查注册表中的坐标是否被错误覆写
if ($regX -ne $expectedX -or $regY -ne $expectedY) {
    Write-Host "FAIL: Registry coordinates ($regX, $regY) were overwritten during form startup!" -ForegroundColor Red
    $failed = $true
} else {
    Write-Host "PASS: Registry coordinates were preserved" -ForegroundColor Green
}

# 5. (PR #17 断言) 模拟极端越界坐标必须被夹紧回工作区内，杜绝窗口底部被任务栏遮挡
$flags = [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance
$tMainForm.GetField("savedWindowX", $flags).SetValue($form, $wa.Right + 500)
$tMainForm.GetField("savedWindowY", $flags).SetValue($form, $wa.Bottom + 500)
$tMainForm.GetMethod("RestoreWindowPosition", $flags).Invoke($form, $null)

$clampedX = $winForm.Location.X
$clampedY = $winForm.Location.Y
$clampedRight = $clampedX + $winForm.Width
$clampedBottom = $clampedY + $winForm.Height

Write-Host "Simulated Clamped Location: ($clampedX, $clampedY), Bottom=$clampedBottom, WorkAreaBottom=$($wa.Bottom)"

if ($winForm.Width -le $wa.Width -and $winForm.Height -le $wa.Height) {
    if ($clampedX -lt $wa.Left -or $clampedY -lt $wa.Top -or $clampedRight -gt $wa.Right -or $clampedBottom -gt $wa.Bottom) {
        Write-Host "FAIL: Out-of-bounds position was not clamped into the work area" -ForegroundColor Red
        $failed = $true
    } else {
        Write-Host "PASS: Out-of-bounds position clamped into the work area" -ForegroundColor Green
    }
} else {
    Write-Host "SKIP: Window larger than work area, clamp assertion skipped" -ForegroundColor Yellow
}

# 现场清理与关闭
$winForm.Dispose()

if ($failed) {
    exit 1
}

Write-Host "ALL TESTS PASSED for test_window_position_persistence.ps1" -ForegroundColor Green
exit 0
