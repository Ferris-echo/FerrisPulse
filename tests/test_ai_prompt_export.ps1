# ============================================================================
# 测试脚本: test_ai_prompt_export.ps1
# 覆盖范围:
#   1. AiPromptDeviceItem 数据结构定义与属性
#   2. AddDeviceWizardForm.GetMultiDeviceAiAdaptationPrompt 多设备批量提示词生成
#   3. 提示词中包含设备类型 (Mouse, Keyboard, Headset 等) 与手动指定设备标识
#   4. AiPromptExportDialog 对话框实例化与 DPI 缩放契约
#   5. AiPromptExportDialog 内部 DeviceItems 状态与数据映射
# ============================================================================

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$rootDir = Split-Path -Parent $scriptDir
$exePath = Join-Path $rootDir "FerrisPulse.exe"

if (-not (Test-Path $exePath)) {
    Write-Host "[FAIL] FerrisPulse.exe not found at $exePath" -ForegroundColor Red
    exit 1
}

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# 铁律 10: 全内存反射加载，严禁锁死二进制文件
$bytes = [System.IO.File]::ReadAllBytes($exePath)
$asm = [System.Reflection.Assembly]::Load($bytes)
$flags = [System.Reflection.BindingFlags]"Public, NonPublic, Instance, Static"

function Assert-True($cond, $msg) {
    if (-not $cond) {
        Write-Host "[FAIL] $msg" -ForegroundColor Red
        exit 1
    }
    Write-Host "[PASS] $msg" -ForegroundColor Green
}

# 1. 验证 AiPromptDeviceItem 类型存在
$itemType = $asm.GetType("RazerBatteryTray.AiPromptDeviceItem")
Assert-True ($null -ne $itemType) "AiPromptDeviceItem 类型存在"

$itemInst = [System.Activator]::CreateInstance($itemType)
Assert-True ($null -ne $itemInst) "能够实例化 AiPromptDeviceItem"

# 2. 验证 AddDeviceWizardForm 多设备提示词生成方法
$wizardType = $asm.GetType("RazerBatteryTray.AddDeviceWizardForm")
Assert-True ($null -ne $wizardType) "AddDeviceWizardForm 类型存在"

$method = $wizardType.GetMethod("GetMultiDeviceAiAdaptationPrompt", $flags)
Assert-True ($null -ne $method) "AddDeviceWizardForm.GetMultiDeviceAiAdaptationPrompt 方法存在"

# 构建测试设备列表
$listType = [System.Type]::GetType("System.Collections.Generic.List``1[[$($itemType.AssemblyQualifiedName)]]")
$testList = [System.Activator]::CreateInstance($listType)

# 设备 1: 扫描到的鼠标
$dev1 = [System.Activator]::CreateInstance($itemType)
$dev1.IsChecked = $true
$dev1.IsManual = $false
$dev1.CustomName = "迈从 A5 V2 Ultra"
$dev1.Category = [RazerBatteryTray.DeviceCategory]::Mouse
$dev1.Brand = "迈从 (Mchose)"
$testList.Add($dev1)

# 设备 2: 扫描到的键盘
$dev2 = [System.Activator]::CreateInstance($itemType)
$dev2.IsChecked = $true
$dev2.IsManual = $false
$dev2.CustomName = "狼蛛 F75 机械键盘"
$dev2.Category = [RazerBatteryTray.DeviceCategory]::Keyboard
$dev2.Brand = "狼蛛 (AULA)"
$testList.Add($dev2)

# 设备 3: 手动添加的未识别耳机
$dev3 = [System.Activator]::CreateInstance($itemType)
$dev3.IsChecked = $true
$dev3.IsManual = $true
$dev3.CustomName = "漫步者 G4 Pro 电竞耳机"
$dev3.Category = [RazerBatteryTray.DeviceCategory]::Headset
$dev3.Brand = "漫步者 (Edifier)"
$testList.Add($dev3)

# 生成多设备提示词 (传入 [object[]]@(,$testList) 避免 PowerShell 数组展平)
$prompt = [string]$method.Invoke($null, [object[]]@(,$testList))
Assert-True (-not [string]::IsNullOrEmpty($prompt)) "多设备提示词生成非空"
Assert-True ($prompt.Contains("迈从 A5 V2 Ultra")) "提示词包含设备 1 型号"
Assert-True ($prompt.Contains("狼蛛 F75 机械键盘")) "提示词包含设备 2 型号"
Assert-True ($prompt.Contains("漫步者 G4 Pro 电竞耳机")) "提示词包含设备 3 手动型号"
Assert-True ($prompt.Contains("Mouse (鼠标)")) "提示词包含鼠标分类标注"
Assert-True ($prompt.Contains("Keyboard (键盘)")) "提示词包含键盘分类标注"
Assert-True ($prompt.Contains("Headset (头戴式耳机)")) "提示词包含耳机分类标注"
Assert-True ($prompt.Contains("手动指定") -or $prompt.Contains("暂无物理探针")) "提示词对未识别的手动添加设备具备针对性引导标注"

# 3. 验证 AiPromptExportDialog 存在且继承自 DpiAwareForm
$dlgType = $asm.GetType("RazerBatteryTray.AiPromptExportDialog")
Assert-True ($null -ne $dlgType) "AiPromptExportDialog 类型存在"
$dpiFormType = $asm.GetType("RazerBatteryTray.DpiAwareForm")
Assert-True ($dpiFormType.IsAssignableFrom($dlgType)) "AiPromptExportDialog 继承自 DpiAwareForm"

# 4. 验证 AiPromptExportDialog 实例化与数据映射
$dlgInst = [System.Activator]::CreateInstance($dlgType)
Assert-True ($null -ne $dlgInst) "能够实例化 AiPromptExportDialog"
Assert-True ($null -ne $dlgInst.DeviceItems) "AiPromptExportDialog 包含 DeviceItems 属性"
Assert-True ($dlgInst.DeviceItems.Count -ge 1) "无扫描设备时自动初始化默认待配置项"
$dlgInst.Dispose()

Write-Host "`nAll AI Prompt Export Tests PASSED!" -ForegroundColor Cyan
exit 0
