# tests/test_adaptive_dpi_monitor.ps1
# 验证：通用自适应鼠标硬件 DPI 监听与心跳降频调度契约

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$asmPath = Join-Path $PSScriptRoot "..\FerrisPulse.exe"
if (-not (Test-Path $asmPath)) { $asmPath = "FerrisPulse.exe" }
$fullPath = (Resolve-Path $asmPath).Path
$bytes = [System.IO.File]::ReadAllBytes($fullPath)
$asm = [System.Reflection.Assembly]::Load($bytes)

$mainFormType = $asm.GetType("RazerBatteryTray.MainForm")
if ($null -eq $mainFormType) {
    throw "MainForm type not found in assembly!"
}

# 1. 验证自适应调度辅助方法或状态机字段 (Adaptive Interval Calculation)
# 契约规则：
# - 活跃态 (0 ~ 30s): 50ms (Rapoo为200ms)
# - 渐退态 (>= 30s 且 !isSleeping): 1000ms
# - 彻底休眠态 (isSleeping): 2000ms (完全静默挂起等待)
$calcIntervalMethod = $mainFormType.GetMethod("CalculateAdaptiveDpiPollInterval", [System.Reflection.BindingFlags]"NonPublic,Static,Public", $null, [Type[]]@([double], [bool], [string]), $null)
if ($null -eq $calcIntervalMethod) {
    throw "CalculateAdaptiveDpiPollInterval method not found on MainForm! Red test confirmed."
}

# 测试活跃态 (闲置 5 秒)
$intervalActive = [int]$calcIntervalMethod.Invoke($null, @([double]5.0, [bool]$false, [string]"Razer"))
if ($intervalActive -ne 50) {
    throw "Active interval for Razer mouse should be 50ms, but was $intervalActive!"
}

# 测试 Rapoo 活跃态 (200ms 保护流)
$intervalRapooActive = [int]$calcIntervalMethod.Invoke($null, @([double]10.0, [bool]$false, [string]"Rapoo"))
if ($intervalRapooActive -ne 200) {
    throw "Active interval for Rapoo mouse should be 200ms, but was $intervalRapooActive!"
}

# 测试渐退态 (闲置 35 秒，硬件尚未休眠)
$intervalIdle = [int]$calcIntervalMethod.Invoke($null, @([double]35.0, [bool]$false, [string]"Razer"))
if ($intervalIdle -ne 1000) {
    throw "Idle backoff interval (>=30s) should be 1000ms, but was $intervalIdle!"
}

# 测试彻底休眠态 (硬件报告休眠)
$intervalSleep = [int]$calcIntervalMethod.Invoke($null, @([double]10.0, [bool]$true, [string]"Razer"))
if ($intervalSleep -ne 2000) {
    throw "Sleeping interval should be 2000ms, but was $intervalSleep!"
}

Write-Host "PASS: 自适应 DPI 调度区间状态机算法校验 100% 正确！" -ForegroundColor Green
exit 0
