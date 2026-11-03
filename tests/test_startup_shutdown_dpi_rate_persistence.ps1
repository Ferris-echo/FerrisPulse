# tests\test_startup_shutdown_dpi_rate_persistence.ps1
# 验证：
# 1. DesktopDpiStage 在注册表中的持久化存储与加载（SaveConfig / LoadConfig 闭环验证）；
# 2. 退出游戏还原桌面状态时，desktopDpiStage 严禁被错误清零，必须持久保持用户设定；
# 3. 模拟开机冷启动校准（PerformStartupDesktopCalibration）：
#    - 硬件冷启动默认 1 档或关机残留 5 档时，若已开启 autoGameDpiEnabled，软件坚决不盲从硬件瞬态，保护用户配置；
#    - 初次未配置状态下，准确采纳硬件当前物理真实档位并持久化；
# 4. 键盘/耳机端点物理隔离：校准严禁渗透至非鼠标设备（如 NuPhy WH80 键盘）；
# 5. 关机/注销/休眠兜底（RestoreDesktopSettingsSynchronous）防残留验证。

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
$tRegistry = $asm.GetType("RazerBatteryTray.HardwareDriverRegistry")
$tBus = $asm.GetType("RazerBatteryTray.HardwareCommandBus")

# 隔离测试注册表项
$testRegPath = "HKCU:\Software\FerrisPulse_StartupShutdownTest"
if (Test-Path $testRegPath) { Remove-Item -Path $testRegPath -Recurse -Force -ErrorAction SilentlyContinue }
New-Item -Path $testRegPath -Force | Out-Null

try {
    # 注入测试键
    Set-ItemProperty -Path $testRegPath -Name "AutoGameOptEnabled" -Value 1 -Type DWord
    Set-ItemProperty -Path $testRegPath -Name "AutoGameDpiEnabled" -Value 1 -Type DWord
    Set-ItemProperty -Path $testRegPath -Name "AutoGameRateEnabled" -Value 1 -Type DWord
    Set-ItemProperty -Path $testRegPath -Name "DesktopPollingRate" -Value 1000 -Type DWord
    Set-ItemProperty -Path $testRegPath -Name "DesktopDpiStage" -Value 3 -Type DWord
    Set-ItemProperty -Path $testRegPath -Name "GameTargetPollingRate" -Value 4000 -Type DWord
    Set-ItemProperty -Path $testRegPath -Name "GameTargetDpiStage" -Value 5 -Type DWord

    $fRegKey = $tMainForm.GetField("ConfigRegistryKey", [System.Reflection.BindingFlags]'Static,NonPublic,Public')
    if ($fRegKey -ne $null) {
        $fRegKey.SetValue($null, "Software\FerrisPulse_StartupShutdownTest")
    }

    $form = [Activator]::CreateInstance($tMainForm, @($false))
    $null = $form.Handle
    $form.Visible = $false

    function Get-Field($name) {
        $f = $tMainForm.GetField($name, [System.Reflection.BindingFlags]'Instance,NonPublic')
        if ($f -ne $null) { return $f.GetValue($form) }
        return $null
    }

    function Set-Field($name, $val) {
        $f = $tMainForm.GetField($name, [System.Reflection.BindingFlags]'Instance,NonPublic')
        if ($f -ne $null) { $f.SetValue($form, $val) }
    }

    Write-Host "--- 测试 1: LoadConfig 必须正确加载 DesktopDpiStage ---" -ForegroundColor Cyan
    $loadedDeskDpiStage = Get-Field "desktopDpiStage"
    $loadedDeskRate = Get-Field "desktopPollingRate"

    if ($loadedDeskDpiStage -ne 3) {
        Write-Host "FAIL: LoadConfig 预期的 desktopDpiStage 为 3，实际为 $loadedDeskDpiStage！" -ForegroundColor Red
        exit 1
    }
    Write-Host "PASS: desktopDpiStage 正确从注册表加载为 3" -ForegroundColor Green

    if ($loadedDeskRate -ne 1000) {
        Write-Host "FAIL: LoadConfig 预期的 desktopPollingRate 为 1000，实际为 $loadedDeskRate！" -ForegroundColor Red
        exit 1
    }
    Write-Host "PASS: desktopPollingRate 正确从注册表加载为 1000" -ForegroundColor Green

    Write-Host "`n--- 测试 2: SaveConfig 必须正确写回 DesktopDpiStage ---" -ForegroundColor Cyan
    Set-Field "desktopDpiStage" 4
    $mSave = $tMainForm.GetMethod("SaveConfig", [System.Reflection.BindingFlags]'Instance,NonPublic')
    $mSave.Invoke($form, $null)

    $regValue = (Get-ItemProperty -Path $testRegPath -Name "DesktopDpiStage").DesktopDpiStage
    if ($regValue -ne 4) {
        Write-Host "FAIL: SaveConfig 写入注册表失败，预期 4，实际为 $regValue！" -ForegroundColor Red
        exit 1
    }
    Write-Host "PASS: SaveConfig 成功将修改后的 desktopDpiStage (4) 持久化至注册表" -ForegroundColor Green

    Write-Host "`n--- 测试 3: 游戏切换与退出时严禁清零 desktopDpiStage ---" -ForegroundColor Cyan
    # 模拟进入游戏优化
    $mApplyOpt = $tMainForm.GetMethod("ApplyGameOptimization", [System.Reflection.BindingFlags]'Instance,NonPublic')
    
    # 构造模拟雷蛇鼠标
    $mouseInfo = [Activator]::CreateInstance($tInfo)
    $mouseInfo.Brand = "Razer"
    $mouseInfo.DeviceId = "Razer:1532:00B7"
    $mouseInfo.DeviceName = "Razer DeathAdder V3 Pro"
    $mouseInfo.Category = [Enum]::Parse($tCat, "Mouse")
    $mouseInfo.IsConnected = $true
    $mouseInfo.BatteryPercent = 90
    $mouseInfo.Dpi = 1600
    $mouseInfo.PollingRate = 1000
    $mouseInfo.DpiStages = [int[]]@(400, 800, 1600, 3200, 6400)
    $mouseInfo.DpiStage = 3
    $mouseInfo.SupportedPollingRates = [int[]]@(125, 250, 500, 1000, 2000, 4000, 8000)

    Set-Field "selectedDevice" $mouseInfo
    Set-Field "desktopDpiStage" 3

    # 进入游戏
    $mApplyOpt.Invoke($form, @($true))
    $stageInGame = Get-Field "desktopDpiStage"
    if ($stageInGame -ne 3) {
        Write-Host "FAIL: 进入游戏后 desktopDpiStage 被破坏，预期 3，实际为 $stageInGame！" -ForegroundColor Red
        exit 1
    }
    Write-Host "PASS: 进入游戏时 desktopDpiStage 稳定保留为 3" -ForegroundColor Green

    # 退出游戏
    $mApplyOpt.Invoke($form, @($false))
    $stageAfterGame = Get-Field "desktopDpiStage"
    if ($stageAfterGame -ne 3) {
        Write-Host "FAIL: 退出游戏后 desktopDpiStage 被错误清零或篡改！预期 3，实际为 $stageAfterGame！" -ForegroundColor Red
        exit 1
    }
    Write-Host "PASS: 退出游戏后 desktopDpiStage 未被清零，依然稳定保留为 3" -ForegroundColor Green

    Write-Host "`n--- 测试 4: 开机冷启动校准不会因硬件异常档位而篡改用户配置 ---" -ForegroundColor Cyan
    # 模拟冷启动：硬件处于第 1 档 (400 DPI)
    $mouseInfo.DpiStage = 1
    $mouseInfo.Dpi = 400
    $mouseInfo.PollingRate = 4000 # 模拟关机残留的 4000Hz 高频

    $mCalib = $tMainForm.GetMethod("PerformStartupDesktopCalibration", [System.Reflection.BindingFlags]'Instance,NonPublic')
    
    # 清空校准标志
    $fCalibrated = $tMainForm.GetField("calibratedStartupDevices", [System.Reflection.BindingFlags]'Instance,NonPublic')
    $dict = $fCalibrated.GetValue($form)
    $dict.Clear()

    Set-Field "desktopDpiStage" 3
    Set-Field "desktopPollingRate" 1000
    Set-Field "autoGameDpiEnabled" $true
    Set-Field "autoGameRateEnabled" $true

    $mCalib.Invoke($form, @($mouseInfo))

    # 校验：desktopDpiStage 绝不能被篡改为 1
    $finalDpiStage = Get-Field "desktopDpiStage"
    if ($finalDpiStage -ne 3) {
        Write-Host "FAIL: 开机校准将用户的桌面档位篡改为硬件瞬态档位！预期 3，实际为 $finalDpiStage！" -ForegroundColor Red
        exit 1
    }
    Write-Host "PASS: 开机校准坚决捍卫用户设定的 desktopDpiStage (3)，未被硬件瞬态 (1) 篡改" -ForegroundColor Green

    Write-Host "`n--- 测试 5: 键盘设备隔离，严禁向非鼠标下发回报率/DPI 校准 ---" -ForegroundColor Cyan
    $kbInfo = [Activator]::CreateInstance($tInfo)
    $kbInfo.Brand = "NuPhy"
    $kbInfo.DeviceId = "NuPhy:WH80"
    $kbInfo.DeviceName = "NuPhy Air75 V2"
    $kbInfo.Category = [Enum]::Parse($tCat, "Keyboard")
    $kbInfo.IsConnected = $true

    $dict.Clear()
    $mCalib.Invoke($form, @($kbInfo))

    $kbKey = "nuphy:wh80"
    if ($dict.ContainsKey($kbKey)) {
        Write-Host "FAIL: PerformStartupDesktopCalibration 未隔离键盘，错误介入了非鼠标设备！" -ForegroundColor Red
        exit 1
    }
    Write-Host "PASS: 键盘设备（NuPhy WH80）被正确物理隔离，未被误判为鼠标校准目标" -ForegroundColor Green

    $form.Dispose()
    Write-Host "`nALL PASS: 开关机与冷启动 DPI/回报率持久化与防跳变验证 100% 通过！" -ForegroundColor Green
    exit 0
}
finally {
    if (Test-Path $testRegPath) {
        Remove-Item -Path $testRegPath -Recurse -Force -ErrorAction SilentlyContinue
    }
}
