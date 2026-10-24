# ============================================================================
# 测试脚本: test_fdas_system.ps1
# 目标: 验证 FDAS v1.0 规范体系、硬件探针现场注入及自动化融合校验闭环
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = (Resolve-Path "$ScriptDir\..").Path
Set-Location $ProjectDir

function Assert-Bom([string]$path) {
    if (-not (Test-Path $path)) { Write-Host "FAIL: 文件不存在: $path" -ForegroundColor Red; exit 1 }
    $bytes = [System.IO.File]::ReadAllBytes($path)
    if ($bytes.Length -lt 3 -or $bytes[0] -ne 0xEF -or $bytes[1] -ne 0xBB -or $bytes[2] -ne 0xBF) {
        Write-Host "FAIL: 不是 UTF-8 with BOM: $path" -ForegroundColor Red; exit 1
    }
}

Write-Host "--- 1. 验证规范文档与官方基准样例 BOM 与完整性 ---" -ForegroundColor Cyan
$specDoc = "$ProjectDir\docs\ADAPTER_SPECIFICATION.md"
Assert-Bom $specDoc
$specText = [System.IO.File]::ReadAllText($specDoc, [System.Text.Encoding]::UTF8)
if (-not $specText.Contains("FDAS v1.0") -or -not $specText.Contains("capabilities.mouse")) {
    Write-Host "FAIL: 规范文档缺少核心章节！" -ForegroundColor Red; exit 1
}
Write-Host "PASS: docs/ADAPTER_SPECIFICATION.md 完整就绪" -ForegroundColor Green

$mchoseSample = "$ProjectDir\examples\adapters\mchose_a5_v2_ultra.fp-adapter.json"
$sonySample = "$ProjectDir\examples\adapters\sony_inzone_buds.fp-adapter.json"
Assert-Bom $mchoseSample
Assert-Bom $sonySample
Write-Host "PASS: 官方样例 BOM 校验全部通过" -ForegroundColor Green

Write-Host "`n--- 2. 验证 tools/integrate_adaptation.ps1 对官方样例的校验闭环 ---" -ForegroundColor Cyan
& powershell -ExecutionPolicy Bypass -File "$ProjectDir\tools\integrate_adaptation.ps1" -AdapterFile $mchoseSample -ValidateOnly
if ($LASTEXITCODE -ne 0) {
    Write-Host "FAIL: 迈从 A5 规范包校验未通过！" -ForegroundColor Red; exit 1
}
Write-Host "PASS: 迈从 A5 V2 Ultra 规范包校验完全成立" -ForegroundColor Green

& powershell -ExecutionPolicy Bypass -File "$ProjectDir\tools\integrate_adaptation.ps1" -AdapterFile $sonySample -ValidateOnly
if ($LASTEXITCODE -ne 0) {
    Write-Host "FAIL: 索尼 INZONE Buds 规范包校验未通过！" -ForegroundColor Red; exit 1
}
Write-Host "PASS: 索尼 INZONE Buds 规范包校验完全成立" -ForegroundColor Green

$vgnAdapter = "$ProjectDir\adapters\VGN_Dragonfly_F2_Pro_Max.fp-adapter.json"
if (Test-Path $vgnAdapter) {
    & powershell -ExecutionPolicy Bypass -File "$ProjectDir\tools\integrate_adaptation.ps1" -AdapterFile $vgnAdapter -ValidateOnly
    if ($LASTEXITCODE -ne 0) {
        Write-Host "FAIL: VGN 蜻蜓 F2 Pro Max 规范包校验未通过！" -ForegroundColor Red; exit 1
    }
    Write-Host "PASS: VGN 蜻蜓 F2 Pro Max 规范包校验完全成立" -ForegroundColor Green
}

Write-Host "`n--- 3. 验证 AddDeviceWizardForm 硬件探针动态注入能力 ---" -ForegroundColor Cyan
$exePath = "$ProjectDir\FerrisPulse.exe"
$bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $exePath).Path)
$assembly = [System.Reflection.Assembly]::Load($bytes)
$tWizard = $assembly.GetType("RazerBatteryTray.AddDeviceWizardForm")
$tConfig = $assembly.GetType("RazerBatteryTray.CustomDeviceConfig")

$testConfig = [System.Activator]::CreateInstance($tConfig)
$testConfig.DeviceId = "HID:3837:1101"
$testConfig.OriginalName = "2.4G Wireless Mouse"
$testConfig.CustomName = "我的新鼠标"
$testConfig.Transport = "2.4G 无线"

$mPrompt = $tWizard.GetMethod("GetAiAdaptationPrompt", [System.Reflection.BindingFlags]'Public,Static', $null, @($tConfig, [string]), $null)
$generatedPrompt = [string]$mPrompt.Invoke($null, @($testConfig, "迈从 A5"))

if (-not $generatedPrompt.Contains("VID = 0x3837") -or -not $generatedPrompt.Contains("PID = 0x1101")) {
    Write-Host "FAIL: 生成的提示词未包含透视出的物理 VID/PID！" -ForegroundColor Red; exit 1
}
if (-not $generatedPrompt.Contains("透视厂商归属: 迈从 (MCHOSE)")) {
    Write-Host "FAIL: 生成的提示词未包含厂商透视！" -ForegroundColor Red; exit 1
}
if (-not $generatedPrompt.Contains("docs/ADAPTER_SPECIFICATION.md")) {
    Write-Host "FAIL: 生成的提示词未包含规范文档指引！" -ForegroundColor Red; exit 1
}
Write-Host "PASS: 向导硬件探针已精准注入 VID=0x3837, PID=0x1101 及透视品牌 MCHOSE" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " ALL PASS: FDAS v1.0 规范体系与自动化验证全线通过！" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
