# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# 1. 自动化测试铁律：必须采用 [System.IO.File]::ReadAllBytes 全内存反射加载二进制，严禁 Add-Type -Path 锁死文件句柄。
# 2. 编码规范：保存为 UTF-8 带签名 (BOM)。
# 模块描述: 验证 Task 5 UI 零硬编码能力契约解耦、HardwareCommandBus 统一调度、向导双引擎导入与 AI 提示词 v2.0 升级

$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exePath = Join-Path $root "FerrisPulse.exe"

if (-not (Test-Path $exePath)) {
    throw "FerrisPulse.exe not found at $exePath"
}

$bytes = [System.IO.File]::ReadAllBytes($exePath)
$asm = [System.Reflection.Assembly]::Load($bytes)

function Assert-True($cond, $msg) {
    if (-not $cond) {
        Write-Host "[FAIL] $msg" -ForegroundColor Red
        exit 1
    }
    Write-Host "[PASS] $msg" -ForegroundColor Green
}

# 1. Source Audit: MainForm.Hardware.cs, MainForm.UI.cs, MainForm.Tray.cs must NOT directly invoke vendor Set*/Update* methods
$hwFile = [System.IO.File]::ReadAllText((Join-Path $root "src\UI\Forms\MainForm.Hardware.cs"), [System.Text.Encoding]::UTF8)
$uiFile = [System.IO.File]::ReadAllText((Join-Path $root "src\UI\Forms\MainForm.UI.cs"), [System.Text.Encoding]::UTF8)
$trayFile = [System.IO.File]::ReadAllText((Join-Path $root "src\UI\Forms\MainForm.Tray.cs"), [System.Text.Encoding]::UTF8)
$combinedUi = $hwFile + "`n" + $uiFile + "`n" + $trayFile

$forbiddenPatterns = @(
    "RazerDeviceHelper.SetRazer",
    "RazerDeviceHelper.UpdateRazer",
    "LogitechDeviceHelper.SetLogitech",
    "RapooDeviceHelper.SetRapoo",
    "RapooDeviceHelper.UpdateRapoo",
    "ValkyrieDeviceHelper.SetValkyrie",
    "ValkyrieDeviceHelper.UpdateValkyrie",
    "MchoseMouseHelper.SetMchose",
    "NuphyDeviceHelper.Set",
    "FdasRuntimeHelper.Set"
)

foreach ($pat in $forbiddenPatterns) {
    Assert-True (-not $combinedUi.Contains($pat)) "UI layer must not contain direct vendor call: $pat"
}

Assert-True ($hwFile.Contains("HardwareCommandBus.")) "MainForm.Hardware.cs routes hardware commands via HardwareCommandBus"
Assert-True ($uiFile.Contains("HardwareCommandBus.")) "MainForm.UI.cs routes hardware commands via HardwareCommandBus"
Assert-True ($trayFile.Contains("HardwareCommandBus.")) "MainForm.Tray.cs routes hardware commands via HardwareCommandBus"

# 2. Capability-driven IsDeviceSupportingAutoGameRate
$formType = $asm.GetType("RazerBatteryTray.MainForm")
$infoType = $asm.GetType("RazerBatteryTray.MouseBatteryInfo")
$capType  = $asm.GetType("RazerBatteryTray.DeviceCapabilities")
$catType  = $asm.GetType("RazerBatteryTray.DeviceCategory")

$uninitForm = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($formType)
$methodSupport = $formType.GetMethod("IsDeviceSupportingAutoGameRate", [System.Reflection.BindingFlags]"Public,Instance")
Assert-True ($null -ne $methodSupport) "MainForm.IsDeviceSupportingAutoGameRate exists"

# 2a. Custom unknown brand mouse with SupportsPollingRate = true -> should return true!
$customMouse = [Activator]::CreateInstance($infoType)
$customMouse.Brand = "FutureBrand2099"
$customMouse.Category = [Enum]::Parse($catType, "Mouse")
$customCap = [Activator]::CreateInstance($capType)
$customCap.SupportsPollingRate = $true
$customCap.SupportsDpi = $false
$customMouse.Capabilities = $customCap

$resCustom = $methodSupport.Invoke($uninitForm, @($customMouse))
Assert-True ($resCustom -eq $true) "IsDeviceSupportingAutoGameRate returns true for unknown brand mouse when Capabilities.SupportsPollingRate is true"

# 2b. Read-only mouse with SupportsPollingRate = false and SupportsDpi = false -> should return false!
$readOnlyMouse = [Activator]::CreateInstance($infoType)
$readOnlyMouse.Brand = "ReadOnlyVendor"
$readOnlyMouse.Category = [Enum]::Parse($catType, "Mouse")
$roCap = [Activator]::CreateInstance($capType)
$roCap.SupportsPollingRate = $false
$roCap.SupportsDpi = $false
$roCap.SupportsDpiStages = $false
$readOnlyMouse.Capabilities = $roCap

$resRo = $methodSupport.Invoke($uninitForm, @($readOnlyMouse))
Assert-True ($resRo -eq $false) "IsDeviceSupportingAutoGameRate returns false for read-only mouse with SupportsPollingRate=false and SupportsDpi=false"

# 3. AI Adaptation Prompt v2.0 & Dual-Track support
$wizType = $asm.GetType("RazerBatteryTray.AddDeviceWizardForm")
$promptMethod = $wizType.GetMethod("GetAiAdaptationPrompt", [Type[]]@())
$promptText = $promptMethod.Invoke($null, @())

Assert-True ($promptText.Contains("FDAS v2.0")) "AI Prompt includes FDAS v2.0 specification"
Assert-True ($promptText.Contains("customActions")) "AI Prompt includes customActions schema"
Assert-True ($promptText.Contains(".fp-driver.cs") -and $promptText.Contains("HardwareDriverBase")) "AI Prompt includes .fp-driver.cs C# 5.0 script plugin specification"
Assert-True ($promptText.Contains("两阶段严谨逆向法") -and $promptText.Contains("probe_hid_device.ps1")) "AI Prompt retains safety-first two-stage reverse engineering workflow"

# 4. ImportAdapterDialog supports both .fp-adapter.json and .fp-driver.cs + BuiltInPackageExporter
$importFile = [System.IO.File]::ReadAllText((Join-Path $root "src\UI\Forms\ImportAdapterDialog.cs"), [System.Text.Encoding]::UTF8)
Assert-True ($importFile.Contains(".fp-driver.cs") -and $importFile.Contains("BuiltInPackageExporter.ExportDefaultPackages")) "ImportAdapterDialog supports .fp-driver.cs and BuiltInPackageExporter"

Write-Host "ALL UI ZERO-HARDCODING DECOUPLING TESTS PASSED!" -ForegroundColor Cyan
