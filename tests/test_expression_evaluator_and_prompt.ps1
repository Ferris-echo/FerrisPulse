# ============================================================================
# 测试脚本: test_expression_evaluator_and_prompt.ps1
# 目标: 验证微表达式引擎动态求值、FDAS 模版动态渲染、安全锁与社区生态飞轮提示词
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = (Resolve-Path "$ScriptDir\..").Path
Set-Location $ProjectDir

$exePath = "$ProjectDir\FerrisPulse.exe"
$bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $exePath).Path)
$assembly = [System.Reflection.Assembly]::Load($bytes)

Write-Host "--- 1. 验证 ExpressionEvaluator 微表达式求值器 ---" -ForegroundColor Cyan
$tEval = $assembly.GetType("RazerBatteryTray.ExpressionEvaluator")
if ($null -eq $tEval) {
    Write-Host "FAIL: 未找到 ExpressionEvaluator 类！" -ForegroundColor Red
    exit 1
}

$mEval = $tEval.GetMethod("Evaluate", [System.Reflection.BindingFlags]'Public,Static')
$mExpand = $tEval.GetMethod("ExpandTemplate", [System.Reflection.BindingFlags]'Public,Static')

$dict = New-Object 'System.Collections.Generic.Dictionary[string, int64]'([System.StringComparer]::OrdinalIgnoreCase)
$dict["rateCode"] = [int64]0x01
$dict["dpi"] = [int64]1600
$dict["stage0"] = [int64]2
$rawDict = $dict.psobject.BaseObject

# 1.1 基础四则运算与位掩码
$val1 = $mEval.Invoke($null, @('(0x55 - $rateCode) & 0xFF', $rawDict))
if ($val1 -ne 0x54) {
    Write-Host "FAIL: (0x55 - 0x01) & 0xFF 预期 0x54 (84)，实际 $val1" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: 互补校验位算式求值通过: (0x55 - 0x01) & 0xFF = 0x$($val1.ToString('X2'))" -ForegroundColor Green

# 1.2 PAW3950 DPI 编码运算: ((1600 / 50) - 1) & 0xFF = 31 (0x1F)
$valDpi = $mEval.Invoke($null, @('(($dpi / 50) - 1) & 0xFF', $rawDict))
if ($valDpi -ne 31) {
    Write-Host "FAIL: ((1600 / 50) - 1) & 0xFF 预期 31 (0x1F)，实际 $valDpi" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: DPI 编码低位求值通过: ((1600 / 50) - 1) & 0xFF = $valDpi (0x$($valDpi.ToString('X2')))" -ForegroundColor Green

# 1.3 寄存器地址偏移: 0x000C + stage0 * 4 = 12 + 8 = 20 (0x14)
$valAddr = $mEval.Invoke($null, @('0x000C + $stage0 * 4', $rawDict))
if ($valAddr -ne 20) {
    Write-Host "FAIL: 0x000C + stage0 * 4 预期 20 (0x14)，实际 $valAddr" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: EEPROM 寄存器地址求值通过: 0x000C + 2 * 4 = $valAddr (0x$($valAddr.ToString('X2')))" -ForegroundColor Green

Write-Host "`n--- 2. 验证 ExpandTemplate 动态模版与派生变量展开 ---" -ForegroundColor Cyan
$exprVars = New-Object 'System.Collections.Generic.Dictionary[string, string]'([System.StringComparer]::OrdinalIgnoreCase)
$exprVars["rateParity"] = '(0x55 - $rateCode) & 0xFF'
$exprVars["stageParity"] = '(0x55 - $stage0) & 0xFF'
$rawExprVars = $exprVars.psobject.BaseObject

$tpl = "08 07 00 00 00 02 {rateCode} {rateParity} 00 00 00 00 00 00 00 00 00"
$resTpl = [string]$mExpand.Invoke($null, @($tpl, $rawDict, $rawExprVars))
$expectedTpl = "08 07 00 00 00 02 01 54 00 00 00 00 00 00 00 00 00"

if ($resTpl -ne $expectedTpl) {
    Write-Host "FAIL: ExpandTemplate 输出与预期不符！实际: $resTpl 预期: $expectedTpl" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: 动态模版成功将 {rateParity} 展开为派生计算值: $resTpl" -ForegroundColor Green

# 验证内联表达式
$tplInline = "08 07 00 00 00 02 {rateCode} {(0x55 - rateCode) & 0xFF} 00 00 00 00 00 00 00 00 00"
$resInline = [string]$mExpand.Invoke($null, @($tplInline, $rawDict, $null))
if ($resInline -ne $expectedTpl) {
    Write-Host "FAIL: 内联表达式模版展开不符！实际: $resInline" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: 内联表达式 {(0x55 - rateCode) & 0xFF} 成功秒算展开为 54" -ForegroundColor Green

Write-Host "`n--- 3. 验证向导提示词中的 WebHID 逆向优先、逃生舱与生态反哺 ---" -ForegroundColor Cyan
$tWiz = $assembly.GetType("RazerBatteryTray.AddDeviceWizardForm")
$mPrompt = $tWiz.GetMethod("GetAiAdaptationPrompt", [System.Reflection.BindingFlags]'Public,Static', $null, @(), $null)
$promptText = [string]$mPrompt.Invoke($null, @())

$requiredPhrases = @(
    "WebHID",
    "sendReport",
    "逃生舱机制",
    "开源生态飞轮",
    "2949225840@qq.com",
    "2949225840zpf@gmail.com",
    "https://github.com/Ferris-echo/FerrisPulse/issues",
    "https://gitee.com/Ferris-echo/FerrisPulse/issues"
)

foreach ($phrase in $requiredPhrases) {
    if (-not $promptText.Contains($phrase)) {
        Write-Host "FAIL: 提示词未包含必要信条: $phrase" -ForegroundColor Red
        exit 1
    }
}
Write-Host "PASS: 提示词已全面覆盖安全红线、WebHID 首选、代码级逃生舱与双邮箱直连反哺！" -ForegroundColor Green

Write-Host "`n--- 4. 验证探针脚本 probe_hid_device.ps1 的高危 EEPROM 写入安全拦截 ---" -ForegroundColor Cyan
$probeScript = "$ProjectDir\tools\probe_hid_device.ps1"
$psi = New-Object System.Diagnostics.ProcessStartInfo
$psi.FileName = "powershell.exe"
$psi.Arguments = "-ExecutionPolicy Bypass -NoProfile -Command `"`$null = & '$probeScript' -VendorId 0x3554 -ProductId 0xFB3E -TxHex '08 07 00 00 00 02 01 54 00 00 00 00 00 00 00 00 00' *>&1; exit `$LASTEXITCODE`""
$psi.UseShellExecute = $false
$psi.RedirectStandardOutput = $true
$psi.RedirectStandardError = $true
$psi.CreateNoWindow = $true

$proc = [System.Diagnostics.Process]::Start($psi)
$proc.WaitForExit(5000) | Out-Null

if ($proc.ExitCode -ne 2) {
    Write-Host "FAIL: 探针脚本面对 0x07 写指令未被安全拦截！退出码: $($proc.ExitCode)" -ForegroundColor Red
    exit 1
}
Write-Host "PASS: 探针脚本成功拦截未经授权的高危 EEPROM 0x07 写指令 (ExitCode 2)" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " ALL PASS: 微表达式引擎、动态模版、安全锁与社区飞轮验证通过！" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
