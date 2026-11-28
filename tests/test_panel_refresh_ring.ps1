# tests/test_panel_refresh_ring.ps1
# 验证：控制面板"打开即刷新" + 刷新态环形加载条（含三大风险点规避）
# 断言口径：
#   1) 刷新收口三态契约（Suppressed/Started/Coalesced）存在且取值稳定；
#   2) StartRefreshCore 在暂停态返回 Suppressed、在途态返回 Coalesced（绝不假装已发起）；
#   3) 环形控件：最短展示 600ms 内 CompleteSpinning 不立即熄灭；超时 Abort 立即淡出收口；
#   4) 主窗"刷新完成"只由数据落地（MarkRefreshAppliedOnUi）收口，且有 15s 守卫兜底；
#   5) 接线完整：ShowWindow → 物理态串行 → 打开即刷新；加载环挂在 Card 1 内并参与 DPI 重排；
#   6) 打开即刷新带 3s 节流，连点不会把全量扫描连成 100% 占空。
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "..\tools\TestHarness.ps1")

$asm = Load-FerrisPulseAssembly
$root = Get-FerrisPulseRoot
$BF = [System.Reflection.BindingFlags]"Public,NonPublic,Instance,Static"
$tMain = $asm.GetType("RazerBatteryTray.MainForm", $true)
$tRing = $asm.GetType("RazerBatteryTray.RefreshRingControl", $true)

function Read-Source([string]$rel) {
    return [System.IO.File]::ReadAllText((Join-Path $root $rel))
}

Invoke-TestCase "1. 刷新收口三态契约与常量" {
    $tOutcome = $tMain.GetNestedType("RefreshOutcome", [System.Reflection.BindingFlags]"Public,NonPublic")
    Assert-True ($null -ne $tOutcome) "RefreshOutcome nested enum exists"
    foreach ($n in @("Suppressed", "Started", "Coalesced")) {
        Assert-True ([System.Enum]::IsDefined($tOutcome, $n)) ("RefreshOutcome." + $n + " defined")
    }
    Assert-Equal 0 ([int][System.Enum]::Parse($tOutcome, "Suppressed")) "Suppressed == 0"
    Assert-Equal 1 ([int][System.Enum]::Parse($tOutcome, "Started")) "Started == 1"
    Assert-Equal 2 ([int][System.Enum]::Parse($tOutcome, "Coalesced")) "Coalesced == 2"

    $fThrottle = $tMain.GetField("PanelOpenRefreshThrottleMs", [System.Reflection.BindingFlags]"Public,NonPublic,Static")
    Assert-True ($null -ne $fThrottle) "PanelOpenRefreshThrottleMs const exists"
    Assert-Equal 3000 ([int]$fThrottle.GetRawConstantValue()) "panel-open throttle is 3000ms"

    $fGuard = $tMain.GetField("RefreshRingGuardTimeoutMs", [System.Reflection.BindingFlags]"Public,NonPublic,Static")
    Assert-True ($null -ne $fGuard) "RefreshRingGuardTimeoutMs const exists"
    Assert-Equal 15000 ([int]$fGuard.GetRawConstantValue()) "ring guard timeout is 15000ms"

    Assert-Equal 40 ([int]$tRing.GetField("AnimIntervalMs", $BF).GetRawConstantValue()) "ring anim interval 40ms (25fps)"
    Assert-Equal 600 ([int]$tRing.GetField("MinVisibleMs", $BF).GetRawConstantValue()) "ring min visible 600ms"
    Assert-Equal 200 ([int]$tRing.GetField("FadeOutMs", $BF).GetRawConstantValue()) "ring fade-out 200ms"
}

Invoke-TestCase "2. 抑制/在途两态绝不被当成已发起" {
    $form = New-SandboxMainForm -AutoStart $false
    try {
        $mCore = $tMain.GetMethod("StartRefreshCore", $BF)
        Assert-True ($null -ne $mCore) "StartRefreshCore exists"

        $pPause = $tMain.GetProperty("PauseBackgroundRefresh")
        $fInFlight = $tMain.GetField("refreshBatteryInFlight", $BF)
        $fPending = $tMain.GetField("refreshBatteryPending", $BF)

        # ① 暂停态：永远不会有数据落地，必须报 Suppressed
        $pPause.SetValue($form, $true, $null)
        $r = $mCore.Invoke($form, @($false))
        Assert-Equal 0 ([int]$r) "suppressed when PauseBackgroundRefresh"
        $pPause.SetValue($form, $false, $null)

        # ② 在途态：返回 Coalesced（会被补跑收口），而不是 Suppressed
        $fInFlight.SetValue($form, 1)
        $r2 = $mCore.Invoke($form, @($false))
        Assert-Equal 2 ([int]$r2) "coalesced when a scan is in flight"
        Assert-Equal 1 ([int]$fPending.GetValue($form)) "pending flag raised for the in-flight scan"
        $fInFlight.SetValue($form, 0)
        $fPending.SetValue($form, 0)
    }
    finally { $form.Dispose() }
}

Invoke-TestCase "3. 环形控件：最短展示 / 收口淡出 / 强制收口" {
    $ring = [Activator]::CreateInstance($tRing)
    try {
        $ring.Size = New-Object System.Drawing.Size(48, 48)
        $mTick = $tRing.GetMethod("OnAnimTick", $BF)
        $mShift = $tRing.GetMethod("ShiftClockForTest", $BF)
        $pAnimating = $tRing.GetProperty("IsAnimating", $BF)
        $pFading = $tRing.GetProperty("IsFading", $BF)

        Assert-True (-not [bool]$pAnimating.GetValue($ring, $null)) "initially idle"
        Assert-True (-not [bool]$ring.Visible) "initially hidden"

        $ring.BeginSpinning()
        Assert-True ([bool]$pAnimating.GetValue($ring, $null)) "spinning after BeginSpinning"
        Assert-True ([bool]$ring.Visible) "visible while spinning"

        # 扫描快于 600ms：立即 CompleteSpinning 不得熄灭，必须先跑满最短展示
        $ring.CompleteSpinning()
        Assert-True ([bool]$pAnimating.GetValue($ring, $null)) "still animating inside min-visible window"
        Assert-True (-not [bool]$pFading.GetValue($ring, $null)) "not fading before min-visible elapses"

        # 越过 600ms：进入淡出
        $mShift.Invoke($ring, @(700))
        $mTick.Invoke($ring, @())
        Assert-True ([bool]$pFading.GetValue($ring, $null)) "fading after min-visible elapsed"

        # 越过 200ms 淡出：彻底收口
        $mShift.Invoke($ring, @(300))
        $mTick.Invoke($ring, @())
        Assert-True (-not [bool]$pAnimating.GetValue($ring, $null)) "stopped after fade-out"
        Assert-True (-not [bool]$ring.Visible) "hidden after fade-out"

        # 强制收口：不等最短展示，立即淡出（超时/抑制路径）
        $ring.BeginSpinning()
        $ring.AbortSpinning()
        Assert-True ([bool]$pFading.GetValue($ring, $null)) "abort enters fade immediately"
    }
    finally { $ring.Dispose() }
}

Invoke-TestCase "4. 主窗刷新收口状态机与守卫兜底" {
    $form = New-SandboxMainForm -AutoStart $false
    try {
        $mBegin = $tMain.GetMethod("BeginRefreshUiWait", $BF)
        $mApplied = $tMain.GetMethod("MarkRefreshAppliedOnUi", $BF)
        $mAbort = $tMain.GetMethod("AbortRefreshUiWait", $BF)
        Assert-True ($null -ne $mBegin) "BeginRefreshUiWait exists"
        Assert-True ($null -ne $mApplied) "MarkRefreshAppliedOnUi exists"
        Assert-True ($null -ne $mAbort) "AbortRefreshUiWait exists"

        $fWaiting = $tMain.GetField("refreshUiWaiting", $BF)
        $fAppliedUtc = $tMain.GetField("lastRefreshAppliedUtc", $BF)
        $fGuard = $tMain.GetField("refreshRingGuardTimer", $BF)

        $mBegin.Invoke($form, @())
        Assert-True ([bool]$fWaiting.GetValue($form)) "waiting after begin"
        $guard = $fGuard.GetValue($form)
        Assert-True ($null -ne $guard) "guard timer created"
        Assert-True ([bool]$guard.Enabled) "guard timer armed"
        Assert-Equal 15000 ([int]$guard.Interval) "guard interval 15s"

        $mApplied.Invoke($form, @())
        Assert-True (-not [bool]$fWaiting.GetValue($form)) "waiting cleared on data landing"
        Assert-True (-not [bool]$guard.Enabled) "guard timer disarmed on completion"
        Assert-True ([DateTime]$fAppliedUtc.GetValue($form) -gt [DateTime]::MinValue) "last applied timestamp recorded"

        # 超时路径：收口 + 降级提示，绝不留永转的环
        $mBegin.Invoke($form, @())
        $mAbort.Invoke($form, @($true))
        Assert-True (-not [bool]$fWaiting.GetValue($form)) "waiting cleared on abort"
        $lbl = $tMain.GetField("lblUpdateTime", $BF).GetValue($form)
        Assert-True ($lbl.Text -like "*超时*") "timeout degrades the footnote text"
    }
    finally { $form.Dispose() }
}

Invoke-TestCase "5. 接线完整性（源文本契约）" {
    $mainForm = Read-Source "src\UI\Forms\MainForm.cs"
    $hardware = Read-Source "src\UI\Forms\MainForm.Hardware.cs"
    $uiSrc = Read-Source "src\UI\Forms\MainForm.UI.cs"
    $traySrc = Read-Source "src\UI\Forms\MainForm.Tray.cs"
    $ringSrc = Read-Source "src\UI\Controls\RefreshRingControl.cs"

    # ① 打开面板：物理态读取串行完成后才发起刷新
    Assert-True ($mainForm.Contains("SyncAllDevicesPhysicalState(RequestRefreshOnPanelOpen)")) "ShowWindow chains panel-open refresh"
    # ② 数据落地是唯一收口点
    Assert-True ($hardware.Contains("MarkRefreshAppliedOnUi();")) "ApplyDeviceStatus marks refresh applied"
    Assert-True ($hardware.Contains("AbortRefreshUiWait(false);")) "read failure aborts the ring"
    # ③ HID 串行化：物理态同步带一次性 after 回调，避免与全量扫描并发抢端点
    Assert-True ($hardware.Contains("afterPhysicalStateSync")) "physical-state sync has chained callback"
    Assert-True ($hardware.Contains("GetHardwareSettings(out hwSettings)")) "physical-state read retained"
    # ④ 加载环挂载进 Card 1 且参与 DPI 重排
    Assert-True ($uiSrc.Contains("InitRefreshRing();")) "ring mounted during UI build"
    Assert-True ($uiSrc.Contains("LayoutRefreshRing();")) "ring re-laid out on DPI scale"
    # ⑤ 手动刷新与托盘刷新走可观测路径
    Assert-True ($uiSrc.Contains("RequestRefreshAfterPhysicalSync(true)")) "manual refresh button observable"
    Assert-True ($traySrc.Contains("RequestVisibleRefresh(true)")) "tray refresh observable"
    # ⑥ 控件契约
    Assert-True ($ringSrc.Contains("AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE)")) "ring keeps maintenance directive"
    Assert-True ($ringSrc.Contains("OnAnimTick")) "ring drives animation by tick"
}

Invoke-TestCase "6. 加载环挂载位置与尺寸（含 DPI 重排）" {
    $form = New-SandboxMainForm -AutoStart $false
    try {
        $card = $tMain.GetField("cardBattery", $BF).GetValue($form)
        Assert-True ($null -ne $card) "cardBattery exists"

        $ring = $tMain.GetField("refreshRing", $BF).GetValue($form)
        Assert-True ($null -ne $ring) "refreshRing field populated"
        Assert-True ($ring.GetType() -eq $tRing) "ring is RefreshRingControl"
        Assert-True (-not [bool]$ring.Visible) "ring hidden at rest"

        $found = $false
        foreach ($c in $card.Controls) { if ($c -eq $ring) { $found = $true } }
        Assert-True $found "ring is a child of cardBattery"

        $mLayout = $tMain.GetMethod("LayoutRefreshRing", $BF)
        $mLayout.Invoke($form, @())
        Assert-True ($ring.Width -ge 24) "ring has usable size"
        Assert-Equal $ring.Width $ring.Height "ring stays square"
        Assert-True ($ring.Left -ge 0) "ring left edge inside card"
        Assert-True (($ring.Left + $ring.Width) -le $card.ClientSize.Width) "ring right edge inside card"
        Assert-True (($ring.Top + $ring.Height) -le $card.ClientSize.Height) "ring bottom edge inside card"
        # 不遮挡电量进度条（y=118*scale 起）
        Assert-True ($ring.Bottom -le [int](118 * 1.0)) "ring avoids battery progress bar"
    }
    finally { $form.Dispose() }
}

Invoke-TestCase "7. 绘制实测：环形真的画出可见像素（防止画成空白）" {
    $ring = [Activator]::CreateInstance($tRing)
    $bmp = $null
    try {
        $ring.Size = New-Object System.Drawing.Size(48, 48)
        $ring.BeginSpinning()
        $tRing.GetMethod("OnAnimTick", $BF).Invoke($ring, @())

        $bmp = New-Object System.Drawing.Bitmap 48, 48
        $rect = New-Object System.Drawing.Rectangle 0, 0, 48, 48
        $ring.DrawToBitmap($bmp, $rect)

        $bg = [System.Drawing.Color]::FromArgb(25, 27, 34)
        $diff = 0
        for ($y = 0; $y -lt 48; $y++) {
            for ($x = 0; $x -lt 48; $x++) {
                $p = $bmp.GetPixel($x, $y)
                if ($p.R -ne $bg.R -or $p.G -ne $bg.G -or $p.B -ne $bg.B) { $diff++ }
            }
        }
        Assert-True ($diff -gt 60) ("ring paints visible pixels, diff=" + $diff)
    }
    finally {
        if ($null -ne $bmp) { $bmp.Dispose() }
        $ring.Dispose()
    }
}
