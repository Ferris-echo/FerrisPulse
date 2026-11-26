# tests\test_dpi_and_polling_pipeline.ps1
# 整合测试套件：DPI/回报率自适应轮询频率、多品牌回报率支持表、只读回报率能力位拦截、
# 桌面 vs 游戏 DPI 状态隔离、乐观高亮与避让防抖、冷启动校准防跳变、X/Y 轴独立报文编码及 DpiAwareForm 缩放
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "..\tools\TestHarness.ps1")

$asm = Load-FerrisPulseAssembly
$root = Get-FerrisPulseRoot
$BF = [System.Reflection.BindingFlags]"Public,NonPublic,Instance,Static"

$tMain = $asm.GetType("RazerBatteryTray.MainForm", $true)
$tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo", $true)
$tCat = $asm.GetType("RazerBatteryTray.DeviceCategory", $true)
$tRazer = $asm.GetType("RazerBatteryTray.RazerDeviceHelper", $true)
$tRapoo = $asm.GetType("RazerBatteryTray.RapooDeviceHelper", $true)
$tVgn = $asm.GetType("RazerBatteryTray.VgnDeviceHelper", $true)
$tLogi = $asm.GetType("RazerBatteryTray.LogitechDeviceHelper", $true)
$tValk = $asm.GetType("RazerBatteryTray.ValkyrieDeviceHelper", $true)
$tUniv = $asm.GetType("RazerBatteryTray.UniversalGamingDeviceHelper", $true)

Invoke-TestCase "1. 自适应 DPI 心跳降频与游戏/变档脉冲感知" {
    $mCalc = $tMain.GetMethod("CalculateAdaptiveDpiPollInterval", $BF, $null, [Type[]]@([double], [bool], [string], [bool], [long]), $null)
    if ($null -eq $mCalc) { $mCalc = $tMain.GetMethod("CalculateAdaptiveDpiPollInterval", $BF) }
    Assert-Equal 50 ([int]$mCalc.Invoke($null, @([double]5.0, [bool]$false, "Razer", [bool]$false, [long]0))) "Razer active 50ms"
    Assert-Equal 200 ([int]$mCalc.Invoke($null, @([double]10.0, [bool]$false, "Rapoo", [bool]$true, [long]0))) "Rapoo gaming 200ms"
    Assert-Equal 1000 ([int]$mCalc.Invoke($null, @([double]60.0, [bool]$false, "Razer", [bool]$false, [long]0))) "Idle 60s backoff 1000ms"
    Assert-Equal 2000 ([int]$mCalc.Invoke($null, @([double]10.0, [bool]$true, "Razer", [bool]$false, [long]0))) "Sleep backoff 2000ms"
    $futureTicks = [DateTime]::UtcNow.Ticks + [TimeSpan]::FromSeconds(5).Ticks
    Assert-Equal 50 ([int]$mCalc.Invoke($null, @([double]60.0, [bool]$false, "Razer", [bool]$false, $futureTicks))) "Pulse burst stays 50ms"
    Assert-Equal 50 ([int]$mCalc.Invoke($null, @([double]60.0, [bool]$false, "Razer", [bool]$true, [long]0))) "Gaming idle stays 50ms"
}

Invoke-TestCase "2. 多品牌自适应回报率表与 Razer/Valkyrie XY 轴报文契约" {
    foreach ($tc in @(
        @{ T = $tRazer; Pid = 0x00BF; Name = "Razer DeathAdder V4 Pro"; Exp = @(125,250,500,1000,2000,4000,8000) },
        @{ T = $tRazer; Pid = 0x0084; Name = "Razer DeathAdder V2"; Exp = @(125,250,500,1000) },
        @{ T = $tRapoo; Pid = 0x4617; Name = "雷柏 VT9 Pro"; Exp = @(125,250,500,1000,2000,4000,8000) },
        @{ T = $tRapoo; Pid = 0x1A00; Name = "雷柏 VT7 SE"; Exp = @(125,250,500,1000) },
        @{ T = $tVgn;   Pid = 0xFB3E; Name = "VGN Dragonfly F2 Pro Max"; Exp = @(125,250,500,1000,2000,4000,8000) },
        @{ T = $tVgn;   Pid = 0xFC03; Name = "VXE Dragonfly R1 Pro"; Exp = @(125,250,500,1000,2000,4000) }
    )) {
        $res = [int[]]$tc.T.InvokeMember("GetSupportedPollingRatesForModel", [System.Reflection.BindingFlags]"InvokeMethod,Public,Static", $null, $null, @([int]$tc.Pid, $tc.Name))
        Assert-Equal ($tc.Exp -join ",") ($res -join ",") "Polling rates PID 0x$($tc.Pid.ToString('X4'))"
    }
    Assert-Equal "125,250,500,1000,2000,4000" (([int[]]$tUniv.GetMethod("GetSupportedPollingRatesByMax").Invoke($null, @([int]4000))) -join ",") "Universal 4K rates"

    # Razer & Valkyrie XY protocol frames + UI controls
    $mCreate = $tRazer.GetMethod("CreateRazerReport", [System.Reflection.BindingFlags]"NonPublic,Static")
    $rep = [byte[]]$mCreate.Invoke($null, @([byte]0x1F, [byte]0x04, [byte]0x05, [byte]0x07, 90, $false, [byte[]]@(0x01, 0x06, 0x40, 0x04, 0xB0, 0x00, 0x00)))
    Assert-Equal 1600 (([int]$rep[9] -shl 8) -bor [int]$rep[10]) "Razer report DPI X 1600"
    Assert-Equal 1200 (([int]$rep[11] -shl 8) -bor [int]$rep[12]) "Razer report DPI Y 1200"

    $pkt = [byte[]]$tValk.GetMethod("BuildOption1Payload", $BF).Invoke($null, @(1, [int[]]@(800, 1600), [int[]]@(600, 1200)))
    Assert-Equal 0x54 $pkt[1] "Valkyrie Option1 cmd"
    Assert-Equal 800 ([int]$pkt[9] -bor ([int]$pkt[10] -shl 8)) "Valkyrie X0=800"
    Assert-Equal 600 ([int]$pkt[25] -bor ([int]$pkt[26] -shl 8)) "Valkyrie Y0=600"

    Assert-True ($null -ne $asm.GetType("RazerBatteryTray.DpiStageRowControl").GetProperty("IsXyIndependent")) "DpiStageRowControl XY"
    Assert-True ($null -ne $asm.GetType("RazerBatteryTray.DpiManagementDialog").GetProperty("StagesY")) "DpiManagementDialog XY"
}

Invoke-TestCase "3. 回报率三态可写能力位与游戏/桌面 DPI 隔离及校准 (MainForm 单实例)" {
    # Logitech read/write capability cache
    $mGetCap = $tLogi.GetMethod("GetPollingRateWriteCapability")
    $mSetCap = $tLogi.GetMethod("SetPollingRateWriteCapability")
    $tid = "Logitech:TEST:RATECAP"
    $mSetCap.Invoke($null, @($tid, $false)) | Out-Null
    Assert-True ([bool]$mGetCap.Invoke($null, @($tid)) -eq $false) "Logi cap false"
    $mSetCap.Invoke($null, @($tid, $true)) | Out-Null
    Assert-True ([bool]$mGetCap.Invoke($null, @($tid)) -eq $true) "Logi cap true"
    Assert-True ($null -eq $mGetCap.Invoke($null, @("Logitech:TEST:UNKNOWN99"))) "Unknown logi cap null"

    $form = New-SandboxMainForm $false
    try {
        # A) Read-only polling rate grays out UI buttons & blocks SetPollingRateFromUI
        $pWrite = $tInfo.GetProperty("SupportsPollingRateWrite")
        $ro = [Activator]::CreateInstance($tInfo)
        $ro.Brand = "Logitech"; $ro.DeviceId = "Logitech:TEST:RO"; $ro.Category = [Enum]::Parse($tCat, "Mouse")
        $ro.IsConnected = $true; $ro.BatteryPercent = 80; $ro.PollingRate = 1000
        $ro.SupportedPollingRates = [int[]]@(125, 250, 500, 1000, 2000, 4000)
        $pWrite.SetValue($ro, $false, $null)
        $mUpdatePerf = $tMain.GetMethod("UpdatePerformanceSection", $BF)
        $mUpdatePerf.Invoke($form, @($ro, $ro.BrandColor)) | Out-Null

        $rateBtns = $tMain.GetField("btnGameRates", $BF).GetValue($form)
        Assert-True ($rateBtns[0].IsReadOnly) "Game rate button readonly for read-only device"
        $chkGameRate = $tMain.GetField("chkAutoGameRate", $BF).GetValue($form)
        Assert-True (-not $chkGameRate.Checked -and -not $chkGameRate.Enabled) "chkAutoGameRate locked"
        $tMain.GetField("desktopPollingRate", $BF).SetValue($form, 1000)
        $tMain.GetMethod("SetPollingRateFromUI").Invoke($form, @([int]2000)) | Out-Null
        Assert-Equal 1000 ([int]$tMain.GetField("desktopPollingRate", $BF).GetValue($form)) "SetPollingRateFromUI blocked for read-only device"

        # B) Writable device unlocks controls
        $rw = [Activator]::CreateInstance($tInfo)
        $rw.Brand = "Logitech"; $rw.DeviceId = "Logitech:TEST:RW"; $rw.Category = [Enum]::Parse($tCat, "Mouse")
        $rw.IsConnected = $true; $rw.PollingRate = 1000; $rw.SupportedPollingRates = [int[]]@(125, 500, 1000, 2000)
        $pWrite.SetValue($rw, $true, $null)
        $rw.DpiStages = [int[]]@(400, 800, 1600, 3200, 6400); $rw.DpiStagesY = [int[]]@(400, 800, 1600, 2250, 6400)
        $rw.Dpi = 3200; $rw.DpiY = 2250; $rw.DpiStage = 4; $rw.IsXyIndependent = $true
        $tMain.GetField("autoGameDpiEnabled", $BF).SetValue($form, $true)
        $mUpdatePerf.Invoke($form, @($rw, $rw.BrandColor)) | Out-Null
        Assert-True ($chkGameRate.Enabled) "Writable device unlocks chkAutoGameRate"

        # C) XY DPI badge & stage button formatting
        $lblBadge = $tMain.GetField("lblLiveRateBadge", $BF).GetValue($form)
        Assert-True ($lblBadge.Text -match "3200×2250 DPI") "XY DPI badge text: $($lblBadge.Text)"
        $rw.IsXyIndependent = $false; $rw.DpiY = 3200
        $mUpdatePerf.Invoke($form, @($rw, $rw.BrandColor)) | Out-Null
        Assert-True ($lblBadge.Text -match "3200 DPI" -and -not ($lblBadge.Text -match "×")) "Unified DPI badge text"

        # D) Gaming vs desktop DPI separation + optimistic UI + dispatch queue
        $tMain.GetField("selectedDevice", $BF).SetValue($form, $rw)
        $tMain.GetField("dpiStageValues", $BF).SetValue($form, [int[]]@(400, 800, 1600, 3200, 6400))
        $tMain.GetField("autoGameDpiEnabled", $BF).SetValue($form, $true)
        $tMain.GetField("isGamingActive", $BF).SetValue($form, $true)
        $tMain.GetField("gameDpiStage", $BF).SetValue($form, 4)
        $tMain.GetField("desktopDpiStage", $BF).SetValue($form, 2)
        $rw.DpiStage = 4

        $tMain.GetMethod("SwitchDpiStageByIndex", $BF).Invoke($form, @([int]0)) | Out-Null
        Assert-Equal 4 ([int]$tMain.GetField("gameDpiStage", $BF).GetValue($form)) "Desktop switch during gaming preserves gameDpiStage"
        Assert-Equal 1 ([int]$tMain.GetField("desktopDpiStage", $BF).GetValue($form)) "Desktop switch during gaming updates desktopDpiStage"
        Assert-Equal 4 $rw.DpiStage "Desktop switch during gaming leaves hardware untouched"

        $tMain.GetField("isGamingActive", $BF).SetValue($form, $false)
        $tMain.GetMethod("SwitchDpiStageByIndex", $BF).Invoke($form, @([int]3)) | Out-Null
        Assert-Equal 4 ([int]$tMain.GetField("desktopDpiStage", $BF).GetValue($form)) "Optimistic desktopDpiStage updated"
        Assert-True ([int64]$tMain.GetField("dpiMonitorSuppressUntilTicks", $BF).GetValue($form) -gt [DateTime]::UtcNow.Ticks) "Monitor suppress armed"

        # E) Cold-start calibration protects desktopDpiStage & ignores Keyboard
        $dict = $tMain.GetField("calibratedStartupDevices", $BF).GetValue($form); $dict.Clear()
        $tMain.GetField("desktopDpiStage", $BF).SetValue($form, 3)
        $rw.DpiStage = 1
        $tMain.GetMethod("PerformStartupDesktopCalibration", $BF).Invoke($form, @($rw)) | Out-Null
        Assert-Equal 3 ([int]$tMain.GetField("desktopDpiStage", $BF).GetValue($form)) "Cold calibration preserves desktopDpiStage"

        $kb = [Activator]::CreateInstance($tInfo); $kb.DeviceId = "NuPhy:WH80"; $kb.Brand = "NuPhy"; $kb.Category = [Enum]::Parse($tCat, "Keyboard"); $kb.IsConnected = $true
        $dict.Clear()
        $tMain.GetMethod("PerformStartupDesktopCalibration", $BF).Invoke($form, @($kb)) | Out-Null
        Assert-True (-not $dict.ContainsKey("nuphy:wh80")) "Keyboard excluded from mouse calibration"
    } finally { $form.Dispose() }

    # Segment button XY paint safety
    $seg = [Activator]::CreateInstance($asm.GetType("RazerBatteryTray.ModernSegmentButton", $true))
    try {
        $seg.Size = New-Object System.Drawing.Size(56, 28); $seg.Text = "3000×2250"
        $bmp = New-Object System.Drawing.Bitmap(56, 28); $g = [System.Drawing.Graphics]::FromImage($bmp)
        $pe = New-Object System.Windows.Forms.PaintEventArgs($g, (New-Object System.Drawing.Rectangle(0,0,56,28)))
        $seg.GetType().GetMethod("OnPaint", $BF).Invoke($seg, [object[]]@($pe.PSObject.BaseObject)) | Out-Null
        $g.Dispose(); $bmp.Dispose()
    } finally { $seg.Dispose() }
}

Invoke-TestCase "4. 源码守护与通用 DpiAwareForm 继承架构" {
    $tScale = $asm.GetType("RazerBatteryTray.DpiScaleHelper", $true)
    Assert-Equal 150 ([int]$tScale.GetMethod("Scale", [Type[]]@([int], [float])).Invoke($null, @([int]100, [float]1.5))) "Scale(100,1.5)"
    $tDpiForm = $asm.GetType("RazerBatteryTray.DpiAwareForm", $true)
    foreach ($fn in @("AboutForm", "AddDeviceWizardForm", "DevicePriorityForm", "DpiManagementDialog", "GameProcessManagerForm", "ImportAdapterDialog", "PollingRateManagementDialog", "PromptDialog", "ResetConfirmDialog")) {
        Assert-True ($tDpiForm.IsAssignableFrom($asm.GetType("RazerBatteryTray.$fn", $true))) "$fn inherits DpiAwareForm"
    }

    $eventsCs = [IO.File]::ReadAllText((Join-Path $root "src\UI\Forms\MainForm.Events.cs"))
    $hwCs = [IO.File]::ReadAllText((Join-Path $root "src\UI\Forms\MainForm.Hardware.cs"))
    Assert-True ($eventsCs -match "WM_QUERYENDSESSION" -and $eventsCs -match "WM_ENDSESSION" -and $eventsCs -match "WM_DPICHANGED") "Critical WndProc handlers"
    Assert-True ($hwCs -match "dpiMonitorSuppressUntilTicks" -and $hwCs -match "RestoreDesktopSettingsSynchronous" -and $hwCs -match "if \(!autoGameRateEnabled\) return;" -and $hwCs -match "if \(!autoGameDpiEnabled\) return;") "Hardware guards intact"
}

Write-Host "`nALL DPI & POLLING PIPELINE TESTS PASSED." -ForegroundColor Green
exit 0
