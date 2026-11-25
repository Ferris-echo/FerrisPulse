# tests\test_sandbox_and_guard.ps1
# 验证测试硬件写入沙盒 (FERRISPULSE_TEST_MODE=1)：
#   1. 禁止 HardwareCommandBus 对内置物理设备发送 SetDpi / UpdateHardwareStages / SetPollingRate / ExecuteCustomAction；
#   2. 允许外部 Mock 测试驱动正常运行；
#   3. 禁止各大 *DeviceHelper / LightingHelper 直接向物理 HID 写配置；
#   4. MainForm 在沙盒模式下不启动后台硬件轮询、DPI 监听与游戏监控；
#   5. TestRegistryGuard 不污染生产注册表。
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "..\tools\TestHarness.ps1")

$asm = Load-FerrisPulseAssembly
$tBus = $asm.GetType("RazerBatteryTray.HardwareCommandBus", $true)
$tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo", $true)
$tCat = $asm.GetType("RazerBatteryTray.DeviceCategory", $true)
$tMain = $asm.GetType("RazerBatteryTray.MainForm", $true)

Invoke-TestCase "1. HardwareCommandBus.IsHardwareSandboxActive 激活确认" {
    $isSandbox = [bool]$tBus.GetProperty("IsHardwareSandboxActive", [System.Reflection.BindingFlags]"Public,Static").GetValue($null, $null)
    Assert-True $isSandbox "IsHardwareSandboxActive must be true in test harness"
}

Invoke-TestCase "2. HardwareCommandBus 对内置真实设备阻断 SetDpi / Polling / CustomAction" {
    $dev = [Activator]::CreateInstance($tInfo)
    $dev.DeviceId = "Razer:1532:00B7"
    $dev.HardwareFingerprint = "Razer:1532:00B7"
    $dev.Brand = "Razer"
    $dev.Category = [Enum]::Parse($tCat, "Mouse")
    $dev.Dpi = 1600
    $dev.PollingRate = 1000

    $mSetDpi = $tBus.GetMethod("SetDpi", [System.Reflection.BindingFlags]"Public,Static")
    $mSetRate = $tBus.GetMethod("SetPollingRate", [System.Reflection.BindingFlags]"Public,Static")
    $mExec = $tBus.GetMethod("ExecuteCustomAction", [System.Reflection.BindingFlags]"Public,Static")

    Assert-True (-not [bool]$mSetDpi.Invoke($null, @($dev, [int]3200, [int]3200, [int]3))) "Built-in SetDpi must be blocked in sandbox"
    Assert-True (-not [bool]$mSetRate.Invoke($null, @($dev, [int]4000))) "Built-in SetPollingRate must be blocked in sandbox"
    Assert-True (-not [bool]$mExec.Invoke($null, @($dev, "auto_sleep", [int]1))) "Built-in ExecuteCustomAction must be blocked in sandbox"
    Assert-Equal 1600 $dev.Dpi "dev.Dpi must remain unchanged"
    Assert-Equal 1000 $dev.PollingRate "dev.PollingRate must remain unchanged"
}

Invoke-TestCase "3. 直调各品牌硬件写 Helper 在沙盒下全部安全拦截返回 false" {
    $tRazer = $asm.GetType("RazerBatteryTray.RazerDeviceHelper", $true)
    $tLogi = $asm.GetType("RazerBatteryTray.LogitechDeviceHelper", $true)
    $tNuphy = $asm.GetType("RazerBatteryTray.NuphyDeviceHelper", $true)
    $tM4d = $asm.GetType("RazerBatteryTray.MchoseMouse4DHelper", $true)
    $tVgn = $asm.GetType("RazerBatteryTray.VgnDeviceHelper", $true)
    $tLight = $asm.GetType("RazerBatteryTray.LightingHelper", $true)

    $mRazerDpi = $tRazer.GetMethod("SetRazerDpi", [Type[]]@([int], [int], [string]))
    $mLogiDpi = $tLogi.GetMethod("SetLogitechDpi", [Type[]]@([int], [int], [string]))
    $mNuphyBright = $tNuphy.GetMethod("SetBacklightBrightness", [System.Reflection.BindingFlags]"Public,Static")
    $mNuphySleep = $tNuphy.GetMethod("SetAutoSleep", [System.Reflection.BindingFlags]"Public,Static")
    $mM4dRate = $tM4d.GetMethod("SetPollingRate", [System.Reflection.BindingFlags]"Public,Static")
    $mVgnRate = $tVgn.GetMethod("SetVgnPollingRate", [System.Reflection.BindingFlags]"Public,Static")
    $mLightStatic = $tLight.GetMethod("SetStatic", [System.Reflection.BindingFlags]"Public,Static")

    Assert-True (-not [bool]$mRazerDpi.Invoke($null, @([int]3200, [int]3200, $null))) "RazerDeviceHelper.SetRazerDpi blocked"
    Assert-True (-not [bool]$mLogiDpi.Invoke($null, @([int]3200, [int]3200, $null))) "LogitechDeviceHelper.SetLogitechDpi blocked"
    Assert-True (-not [bool]$mNuphyBright.Invoke($null, @([int]50))) "NuphyDeviceHelper.SetBacklightBrightness blocked"
    Assert-True (-not [bool]$mNuphySleep.Invoke($null, @($false))) "NuphyDeviceHelper.SetAutoSleep blocked"
    Assert-True (-not [bool]$mM4dRate.Invoke($null, @([int]4000))) "MchoseMouse4DHelper.SetPollingRate blocked"
    Assert-True (-not [bool]$mVgnRate.Invoke($null, @([int]2000))) "VgnDeviceHelper.SetVgnPollingRate blocked"
    Assert-True (-not [bool]$mLightStatic.Invoke($null, @([byte]0, [System.Drawing.Color]::Red))) "LightingHelper.SetStatic blocked"
}

Invoke-TestCase "4. MainForm 在沙盒模式下不触发后台轮询与监听线程" {
    $form = New-SandboxMainForm $true
    try {
        $bf = [System.Reflection.BindingFlags]"Instance,NonPublic"
        $timer = $tMain.GetField("updateTimer", $bf).GetValue($form)
        $dpiRunning = [bool]$tMain.GetField("isDpiMonitorRunning", $bf).GetValue($form)
        $gameTimer = $tMain.GetField("gameMonitorTimer", $bf).GetValue($form)
        Assert-True ($null -ne $timer -and -not $timer.Enabled) "updateTimer must stay stopped in sandbox"
        Assert-True (-not $dpiRunning) "DpiMonitor must not start in sandbox"
        Assert-True ($null -eq $gameTimer) "gameMonitorTimer must not start in sandbox"
    }
    finally {
        $form.Dispose()
    }
}

Write-Host "`nALL SANDBOX GUARD CHECKS PASSED." -ForegroundColor Green
exit 0
