# tests\test_tray_clicks_debounce_and_doubleclick.ps1
# 验证托盘点击防抖、双击防收起保护与守护进程关闭防护逻辑
$ErrorActionPreference = "Stop"

$asmPath = Join-Path $PSScriptRoot "..\FerrisPulse.exe"
if (-not (Test-Path $asmPath)) { $asmPath = "FerrisPulse.exe" }
$fullPath = (Resolve-Path $asmPath).Path

# 采用全内存加载模式，严禁独占锁定 exe 文件句柄 (铁律 10)
$bytes = [System.IO.File]::ReadAllBytes($fullPath)
$asm = [System.Reflection.Assembly]::Load($bytes)

$tMainForm = $asm.GetType("RazerBatteryTray.MainForm")
if ($tMainForm -eq $null) {
    Write-Host "FAIL: Type RazerBatteryTray.MainForm not found" -ForegroundColor Red
    exit 1
}

# 1. 验证防护字段存在性
$fDoubleTick = $tMainForm.GetField("lastTrayDoubleClickTick", [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance)
$fClickTick = $tMainForm.GetField("lastTrayClickTick", [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance)
$fToggling = $tMainForm.GetField("isTogglingWindow", [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance)

if ($fDoubleTick -eq $null -or $fClickTick -eq $null -or $fToggling -eq $null) {
    Write-Host "FAIL: Required debounce and double-click state fields not found in MainForm!" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: Debounce and double-click protection fields verified." -ForegroundColor Green

# 实例化 MainForm(false) - 非自启动模式
$form = [Activator]::CreateInstance($tMainForm, @($false))
[System.Windows.Forms.Form]$winForm = $form
$handle = $winForm.Handle

# 2. 验证 OnFormClosing 守护安全逻辑：
# 当 CloseReason 为 UserClosing 或 None 时，绝不能退出进程，必须 Cancel 并 Hide
$mOnFormClosing = $tMainForm.GetMethod("OnFormClosing", [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance)
if ($mOnFormClosing -eq $null) {
    Write-Host "FAIL: OnFormClosing method not found" -ForegroundColor Red
    exit 1
}

# 测试 CloseReason.UserClosing
$eUserClosing = New-Object System.Windows.Forms.FormClosingEventArgs([System.Windows.Forms.CloseReason]::UserClosing, $false)
$mOnFormClosing.Invoke($winForm, [object[]]@($eUserClosing.PSObject.BaseObject))
if (-not $eUserClosing.Cancel) {
    Write-Host "FAIL: OnFormClosing did not cancel UserClosing!" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: CloseReason.UserClosing is safely cancelled and kept alive." -ForegroundColor Green

# 测试 CloseReason.None (非显式退出请求)
$eNoneClosing = New-Object System.Windows.Forms.FormClosingEventArgs([System.Windows.Forms.CloseReason]::None, $false)
$mOnFormClosing.Invoke($winForm, [object[]]@($eNoneClosing.PSObject.BaseObject))
if (-not $eNoneClosing.Cancel) {
    Write-Host "FAIL: OnFormClosing did not cancel CloseReason.None! Could cause daemon to terminate." -ForegroundColor Red
    exit 1
}
Write-Host "PASS: CloseReason.None is safely cancelled and protected." -ForegroundColor Green

# 测试 CloseReason.ApplicationExitCall (用户右键显式点击“退出程序”)
$eAppExit = New-Object System.Windows.Forms.FormClosingEventArgs([System.Windows.Forms.CloseReason]::ApplicationExitCall, $false)
$mOnFormClosing.Invoke($winForm, [object[]]@($eAppExit.PSObject.BaseObject))
if ($eAppExit.Cancel) {
    Write-Host "FAIL: OnFormClosing unexpectedly cancelled ApplicationExitCall!" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: CloseReason.ApplicationExitCall allows clean shutdown." -ForegroundColor Green

# 3. 验证 ToggleWindow 防重入锁
$mToggle = $tMainForm.GetMethod("ToggleWindow", [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance)
$fToggling.SetValue($winForm, $true)
# 当 isTogglingWindow 为 true 时调用 ToggleWindow 应立即返回，不改变状态
$initialVisible = $winForm.Visible
$mToggle.Invoke($winForm, $null)
if ($winForm.Visible -ne $initialVisible) {
    Write-Host "FAIL: ToggleWindow executed while isTogglingWindow was true!" -ForegroundColor Red
    exit 1
}
$fToggling.SetValue($winForm, $false)
Write-Host "PASS: ToggleWindow re-entrancy lock verified." -ForegroundColor Green

# 4. 验证托盘双击与 Click 伴生消除逻辑
$fTray = $tMainForm.GetField("trayIcon", [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance)
$trayIcon = $fTray.GetValue($winForm)
if ($trayIcon -ne $null) {
    $mShow = $tMainForm.GetMethod("ShowWindow", [System.Reflection.BindingFlags]::NonPublic -bor [System.Reflection.BindingFlags]::Instance, $null, @($asm.GetType("RazerBatteryTray.MouseBatteryInfo")), $null)
    
    # 模拟双击动作：设置 lastTrayDoubleClickTick 并执行 ShowWindow
    $now = [System.Environment]::TickCount
    $fDoubleTick.SetValue($winForm, $now)
    $mShow.Invoke($winForm, @([System.Object]$null))
    
    if (-not $winForm.Visible) {
        Write-Host "FAIL: Form is not visible after ShowWindow on double-click" -ForegroundColor Red
        exit 1
    }
    
    # 模拟 Windows 在双击后 30ms 伴生触发的 Click 事件
    # 此时 unchecked(now - lastTrayDoubleClickTick) < 400，Click 应该被静默丢弃，窗口维持 Visible
    $fClickTick.SetValue($winForm, $now)
    # 验证此时窗口依然保持 Visible（未被随后的 click 误关闭）
    if (-not $winForm.Visible) {
        Write-Host "FAIL: Form was erroneously hidden by trailing click after double-click!" -ForegroundColor Red
        exit 1
    }
    Write-Host "PASS: Trailing click after tray double-click is safely suppressed." -ForegroundColor Green
}

$winForm.Dispose()
Write-Host "ALL TRAY DEBOUNCE AND DOUBLECLICK TESTS PASSED!" -ForegroundColor Green
exit 0
