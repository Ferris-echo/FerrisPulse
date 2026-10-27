# ============================================================================
# 测试脚本: test_adaptive_dpi_monitor.ps1
# 目标: 验证全品牌通用鼠标 DPI 渐进式自适应心跳机制 (50ms -> 30s放缓 -> 彻底休眠0发包 -> 移动即时校准)
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = Resolve-Path "$ScriptDir\.."
Set-Location $ProjectDir

Write-Host "[TEST] Validating Universal Adaptive Mouse DPI Heartbeat Contract..." -ForegroundColor Cyan

$exePath = "$ProjectDir\FerrisPulse.exe"
if (-not (Test-Path $exePath)) {
    throw "FerrisPulse.exe not found!"
}

$asmBytes = [System.IO.File]::ReadAllBytes($exePath)
$asm = [System.Reflection.Assembly]::Load($asmBytes)
$mainFormType = $asm.GetType("RazerBatteryTray.MainForm")
if ($null -eq $mainFormType) {
    throw "MainForm type not found in assembly!"
}

# 1. 验证自适应调度辅助方法或状态机字段 (Adaptive Interval Calculation)
# 契约规则：
# - 活跃态 (0 ~ 30s): 50ms (Rapoo为200ms)
# - 渐退态 (>= 30s 且 !isSleeping): 1000ms
# - 彻底休眠态 (isSleeping): 2000ms (完全静默挂起等待)
$calcIntervalMethod = $mainFormType.GetMethod("CalculateAdaptiveDpiPollInterval", [System.Reflection.BindingFlags]"NonPublic,Static,Public")
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

# 测试瓦尔基里等其他品牌渐退态 (验证多品牌全解耦)
$intervalValIdle = [int]$calcIntervalMethod.Invoke($null, @([double]40.0, [bool]$false, [string]"Valkyrie"))
if ($intervalValIdle -ne 1000) {
    throw "Idle backoff interval for Valkyrie should be 1000ms, but was $intervalValIdle!"
}

# 测试彻底休眠态 (isSleeping == true: 问不到了，彻底静默长心跳)
$intervalSleep = [int]$calcIntervalMethod.Invoke($null, @([double]60.0, [bool]$true, [string]"Razer"))
if ($intervalSleep -lt 2000) {
    throw "Deep sleep interval should be >= 2000ms, but was $intervalSleep!"
}

# 2. 验证移动即时校准判定逻辑契约 (Instant Calibration: 变则存盘，不变则静)
$shouldNotifyMethod = $mainFormType.GetMethod("EvaluateDpiCalibrationResult", [System.Reflection.BindingFlags]"NonPublic,Static,Public")
if ($null -eq $shouldNotifyMethod) {
    throw "EvaluateDpiCalibrationResult method not found on MainForm! Red test confirmed."
}

# Case A: 硬件读出新 DPI (1600) 与软件当前记录 (800) 不一致 -> 必须返回 true (触发保存与OSD)
$needUpdate1 = [bool]$shouldNotifyMethod.Invoke($null, @([int]1600, [int]2, [int]800, [int]1))
if (-not $needUpdate1) {
    throw "EvaluateDpiCalibrationResult should return true when DPI changed (800 -> 1600)!"
}

# Case B: 硬件读出 DPI (800) 与软件当前记录 (800) 一致 -> 必须返回 false (静默放行，零额外操作)
$needUpdate2 = [bool]$shouldNotifyMethod.Invoke($null, @([int]800, [int]1, [int]800, [int]1))
if ($needUpdate2) {
    throw "EvaluateDpiCalibrationResult should return false when DPI unchanged (800 == 800)!"
}

# Case C: 硬件读出异常值 (<= 0) -> 必须返回 false (容错保护)
$needUpdate3 = [bool]$shouldNotifyMethod.Invoke($null, @([int]-1, [int]0, [int]800, [int]1))
if ($needUpdate3) {
    throw "EvaluateDpiCalibrationResult should return false for invalid hardware reading!"
}

Write-Host "[PASS] Universal Adaptive Mouse DPI Heartbeat Contract Verified Successfully!" -ForegroundColor Green
