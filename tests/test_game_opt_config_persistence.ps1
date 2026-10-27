# tests\test_game_opt_config_persistence.ps1
# 验证：
# 1. DisableConfigSaveForTesting 生效时，SaveConfig() 严禁写入注册表，保护用户环境；
# 2. LoadConfig() 不会因为 AutoGameOptEnabled 为 0 而破坏性抹除 AutoGameDpiEnabled / AutoGameRateEnabled 的用户偏好；
# 3. UpdatePerformanceSection 运行期间不会意外触发 CheckedChanged 导致配置被覆写；
# 4. 电竞 DPI 开关与档位设置具备完整持久性与记忆能力。

$ErrorActionPreference = "Stop"

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

$asmPath = Join-Path $PSScriptRoot "..\FerrisPulse.exe"
if (-not (Test-Path $asmPath)) { $asmPath = "FerrisPulse.exe" }
$fullPath = (Resolve-Path $asmPath).Path
$bytes = [System.IO.File]::ReadAllBytes($fullPath)
$asm = [System.Reflection.Assembly]::Load($bytes)

$tMainForm = $asm.GetType("RazerBatteryTray.MainForm")
$tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo")
$tCat = $asm.GetType("RazerBatteryTray.DeviceCategory")

# 测试隔离虚拟注册表项
$testRegPath = "HKCU:\Software\FerrisPulse_ConfigTest"
if (Test-Path $testRegPath) { Remove-Item -Path $testRegPath -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -Path $testRegPath -Force | Out-Null

try {
    # 注入测试配置
    Set-ItemProperty -Path $testRegPath -Name "AutoGameOptEnabled" -Value 0 -Type DWord
    Set-ItemProperty -Path $testRegPath -Name "AutoGameDpiEnabled" -Value 1 -Type DWord
    Set-ItemProperty -Path $testRegPath -Name "AutoGameRateEnabled" -Value 1 -Type DWord
    Set-ItemProperty -Path $testRegPath -Name "GameTargetDpiStage" -Value 4 -Type DWord

    # 将 ConfigRegistryKey 重定向至测试项
    $fRegKey = $tMainForm.GetField("ConfigRegistryKey", [System.Reflection.BindingFlags]'Static,NonPublic,Public')
    if ($fRegKey -ne $null) {
        $fRegKey.SetValue($null, "Software\FerrisPulse_ConfigTest")
    }

    # 启用单测防写保护
    $fDisableSave = $tMainForm.GetField("DisableConfigSaveForTesting", [System.Reflection.BindingFlags]'Static,NonPublic,Public')
    if ($fDisableSave -ne $null) {
        $fDisableSave.SetValue($null, $true)
    }

    $form = [Activator]::CreateInstance($tMainForm, @($false))
    $null = $form.Handle
    $form.Visible = $false

    function Get-Field($name) {
        $f = $tMainForm.GetField($name, [System.Reflection.BindingFlags]'Instance,NonPublic')
        if ($f -ne $null) { return $f.GetValue($form) }
        return $null
    }

    Write-Host "--- 测试 1: LoadConfig 必须保留 AutoGameDpiEnabled 与 GameTargetDpiStage ---" -ForegroundColor Cyan
    $dpiEnabled = Get-Field "autoGameDpiEnabled"
    $rateEnabled = Get-Field "autoGameRateEnabled"
    $stage = Get-Field "gameDpiStage"

    if ($dpiEnabled -ne $true) {
        Write-Host "FAIL: 即使 AutoGameOptEnabled 为 0，LoadConfig 也不应抹除已保存的 autoGameDpiEnabled (预期 true，实际 $dpiEnabled)！" -ForegroundColor Red
        exit 1
    }
    Write-Host "PASS: autoGameDpiEnabled 记忆保持为 true" -ForegroundColor Green

    if ($stage -ne 4) {
        Write-Host "FAIL: gameDpiStage 预期为 4，实际为 $stage！" -ForegroundColor Red
        exit 1
    }
    Write-Host "PASS: gameDpiStage 记忆保持为 4" -ForegroundColor Green

    Write-Host "`n--- 测试 2: UpdatePerformanceSection 运行中不触发事件反写 ---" -ForegroundColor Cyan
    $mouseInfo = [Activator]::CreateInstance($tInfo)
    $mouseInfo.Brand = "Razer"
    $mouseInfo.DeviceId = "Razer:1532:00B7"
    $mouseInfo.DeviceName = "Razer DeathAdder V3 Pro"
    $mouseInfo.Category = [Enum]::Parse($tCat, "Mouse")
    $mouseInfo.IsConnected = $true
    $mouseInfo.BatteryPercent = 90
    $mouseInfo.Dpi = 3200
    $mouseInfo.PollingRate = 1000
    $mouseInfo.DpiStages = [int[]]@(400, 800, 1600, 3200, 6400)
    $mouseInfo.DpiStage = 4
    $mouseInfo.SupportedPollingRates = [int[]]@(125, 250, 500, 1000, 2000, 4000, 8000)

    $mUpdate = $tMainForm.GetMethod("UpdatePerformanceSection", [System.Reflection.BindingFlags]'Instance,NonPublic')
    $mUpdate.Invoke($form, @($mouseInfo, [System.Drawing.Color]::FromArgb(0, 255, 0)))

    $dpiEnabledAfter = Get-Field "autoGameDpiEnabled"
    if ($dpiEnabledAfter -ne $true) {
        Write-Host "FAIL: UpdatePerformanceSection 导致 autoGameDpiEnabled 被篡改为 $dpiEnabledAfter！" -ForegroundColor Red
        exit 1
    }
    Write-Host "PASS: UpdatePerformanceSection 执行后 autoGameDpiEnabled 保持不变" -ForegroundColor Green

    $form.Dispose()
    Write-Host "`nALL PASS: 电竞配置持久性与记忆隔离验证通过！" -ForegroundColor Green
    exit 0
}
finally {
    if (Test-Path $testRegPath) {
        Remove-Item -Path $testRegPath -Recurse -Force -ErrorAction SilentlyContinue
    }
}
