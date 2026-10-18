# tests\test_window_position_persistence.ps1
# 验证主窗体启动时位置记忆能够准确从注册表恢复，且越界坐标会被夹紧到屏幕工作区内

$ErrorActionPreference = "Stop"

$regPath = "HKCU:\Software\FerrisPulse"
if (-not (Test-Path $regPath)) {
    New-Item -Path $regPath -Force | Out-Null
}

# 记录测试前的坐标，测试结束后恢复，避免污染真实配置
$originalRegX = (Get-ItemProperty -Path $regPath -ErrorAction SilentlyContinue).WindowX
$originalRegY = (Get-ItemProperty -Path $regPath -ErrorAction SilentlyContinue).WindowY

[void][System.Reflection.Assembly]::LoadWithPartialName("System.Windows.Forms")
$workArea = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea

# 1. 预先向注册表写入一个完整可见、且明显不是居中位置的坐标
$expectedX = $workArea.Left + 40
$expectedY = $workArea.Top + 40

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

# 3. 校验窗口坐标与注册表坐标
$actualX = $winForm.Location.X
$actualY = $winForm.Location.Y

$regX = (Get-ItemProperty -Path $regPath).WindowX
$regY = (Get-ItemProperty -Path $regPath).WindowY

Write-Host "Form Location: ($actualX, $actualY)"
Write-Host "Registry Location: ($regX, $regY)"

$failed = $false

# 检查窗口坐标是否准确恢复
if ($actualX -ne $expectedX -or $actualY -ne $expectedY) {
    Write-Host "FAIL: Form location ($actualX, $actualY) does not match expected ($expectedX, $expectedY)" -ForegroundColor Red
    $failed = $true
} else {
    Write-Host "PASS: Form location matches expected ($expectedX, $expectedY)" -ForegroundColor Green
}

# 检查注册表中的坐标是否被错误覆写
if ($regX -ne $expectedX -or $regY -ne $expectedY) {
    Write-Host "FAIL: Registry coordinates ($regX, $regY) were overwritten during form startup!" -ForegroundColor Red
    $failed = $true
} else {
    Write-Host "PASS: Registry coordinates were preserved" -ForegroundColor Green
}

# 4. 越界坐标必须被夹紧回工作区内，杜绝窗口底部被任务栏遮挡
$flags = [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance
$tMainForm.GetField("savedWindowX", $flags).SetValue($form, $workArea.Right + 500)
$tMainForm.GetField("savedWindowY", $flags).SetValue($form, $workArea.Bottom + 500)
$tMainForm.GetMethod("RestoreWindowPosition", $flags).Invoke($form, $null)

$clampedX = $winForm.Location.X
$clampedY = $winForm.Location.Y
$clampedRight = $clampedX + $winForm.Width
$clampedBottom = $clampedY + $winForm.Height

Write-Host "Clamped Location: ($clampedX, $clampedY), Bottom=$clampedBottom, WorkAreaBottom=$($workArea.Bottom)"

if ($winForm.Width -le $workArea.Width -and $winForm.Height -le $workArea.Height) {
    if ($clampedX -lt $workArea.Left -or $clampedY -lt $workArea.Top -or $clampedRight -gt $workArea.Right -or $clampedBottom -gt $workArea.Bottom) {
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

# 恢复测试前的注册表坐标
if ($null -ne $originalRegX -and $null -ne $originalRegY) {
    Set-ItemProperty -Path $regPath -Name "WindowX" -Value $originalRegX -Type DWord
    Set-ItemProperty -Path $regPath -Name "WindowY" -Value $originalRegY -Type DWord
}

if ($failed) {
    Write-Host "Test failed." -ForegroundColor Red
    exit 1
}

Write-Host "ALL TESTS PASSED for test_window_position_persistence.ps1" -ForegroundColor Green
exit 0
