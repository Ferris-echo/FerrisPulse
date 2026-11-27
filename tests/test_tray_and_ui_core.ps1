# tests\test_tray_and_ui_core.ps1
# 整合测试套件：托盘交互、防抖、低电量仲裁、图标绘制、菜单净化、窗体位置/缩放/控件渲染及 UI 清理
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "..\tools\TestHarness.ps1")

$asm = Load-FerrisPulseAssembly
$root = Get-FerrisPulseRoot
$BF = [System.Reflection.BindingFlags]"Public,NonPublic,Instance,Static"

$tMain = $asm.GetType("RazerBatteryTray.MainForm", $true)
$tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo", $true)
$tCat = $asm.GetType("RazerBatteryTray.DeviceCategory", $true)
$tDevMgr = $asm.GetType("RazerBatteryTray.DeviceManager", $true)
$tPrioForm = $asm.GetType("RazerBatteryTray.DevicePriorityForm", $true)
$tScale = $asm.GetType("RazerBatteryTray.DpiScaleHelper", $true)

Invoke-TestCase "1. 自定义名称 CustomName 与 DisplayName/FriendlyShortName 契约" {
    $d1 = [Activator]::CreateInstance($tInfo)
    $d1.Category = [Enum]::Parse($tCat, "Earbuds"); $d1.DeviceName = "Buds 4 Pro"; $d1.CustomName = "鹏飞的buds4pro"
    Assert-Equal "鹏飞的buds4pro" $d1.DisplayName "Custom DisplayName"
    Assert-Equal "鹏飞的buds4pro" $d1.FriendlyShortName "Custom FriendlyShortName"

    $d3 = [Activator]::CreateInstance($tInfo)
    $d3.Category = [Enum]::Parse($tCat, "Earbuds"); $d3.DeviceName = "Buds4 Pro"; $d3.CustomName = $null
    Assert-Equal "Buds4 Pro" $d3.DisplayName "Fallback DisplayName"
}

Invoke-TestCase "2. 托盘低电量优先 (<=30%) 与 DevicePriorityForm 开关联动" {
    $thr = $tDevMgr.GetField("LowBatteryPriorityThreshold", [System.Reflection.BindingFlags]"Public,Static").GetValue($null)
    Assert-Equal 30 $thr "LowBatteryPriorityThreshold"

    $mIs = $tDevMgr.GetMethod("IsTrayLowBatteryFirstEnabled", [System.Reflection.BindingFlags]"Public,Static")
    $mSet = $tDevMgr.GetMethod("SetTrayLowBatteryFirstEnabled", [System.Reflection.BindingFlags]"Public,Static")
    $orig = [bool]$mIs.Invoke($null, $null)
    try {
        $mSet.Invoke($null, @($false)) | Out-Null
        Assert-True (-not [bool]$mIs.Invoke($null, $null)) "SetTrayLowBatteryFirstEnabled(false)"
        $mSet.Invoke($null, @($true)) | Out-Null
        Assert-True ([bool]$mIs.Invoke($null, $null)) "SetTrayLowBatteryFirstEnabled(true)"

        $listType = [System.Collections.Generic.List`1].MakeGenericType(@($tInfo))
        $emptyList = [Activator]::CreateInstance($listType)
        $prioArgs = New-Object object[] 1
        $prioArgs[0] = $emptyList
        $pf = $tPrioForm.GetConstructor(@($listType)).Invoke($prioArgs)
        try {
            $chk = $tPrioForm.GetField("chkLowBatteryFirst", [System.Reflection.BindingFlags]"Instance,NonPublic").GetValue($pf)
            Assert-True ($null -ne $chk -and $chk.Checked) "chkLowBatteryFirst initialized true"
        } finally { $pf.Dispose() }
    } finally {
        $mSet.Invoke($null, @($orig)) | Out-Null
    }
}

Invoke-TestCase "3. 托盘绘制 DrawTrayBitmap (3 样式 x 16/24px x 在线/充电/断联/数字1)" {
    $mDraw = $tMain.GetMethod("DrawTrayBitmap", [System.Reflection.BindingFlags]"NonPublic,Static")
    Assert-True ($null -ne $mDraw) "DrawTrayBitmap exists"
    foreach ($style in @(0, 1, 2)) {
        foreach ($size in @(16, 24)) {
            foreach ($pct in @(0, 40, 85, 100)) {
                $b = $mDraw.Invoke($null, @([int]$pct, [bool]$false, [bool]$true, [int]$style, [int]$size, [bool]$false, $null))
                Assert-True ($null -ne $b -and $b.Width -eq $size -and $b.Height -eq $size) "style=$style size=$size pct=$pct"
                $b.Dispose()
            }
            $bc = $mDraw.Invoke($null, @([int]85, [bool]$true, [bool]$true, [int]$style, [int]$size, [bool]$false, $null))
            Assert-True ($null -ne $bc) "charging style=$style size=$size"
            $bc.Dispose()

            $bd = $mDraw.Invoke($null, @([int]-1, [bool]$false, [bool]$false, [int]$style, [int]$size, [bool]$false, $null))
            $hasPx = $false
            for ($y = 0; $y -lt $bd.Height -and -not $hasPx; $y++) {
                for ($x = 0; $x -lt $bd.Width; $x++) {
                    if ($bd.GetPixel($x, $y).A -gt 0) { $hasPx = $true; break }
                }
            }
            $bd.Dispose()
            Assert-True $hasPx "disconnected icon visible style=$style size=$size"
        }
    }
    $b16_1 = $mDraw.Invoke($null, @([int]1, [bool]$false, [bool]$true, [int]2, [int]16, [bool]$false, $null))
    Assert-True (($b16_1.GetPixel(6, 12).A -gt 0) -and ($b16_1.GetPixel(8, 12).A -gt 0)) "16x16 standalone 1 pedestal"
    $b16_1.Dispose()
    $b16_17 = $mDraw.Invoke($null, @([int]17, [bool]$false, [bool]$true, [int]2, [int]16, [bool]$false, $null))
    Assert-True (($b16_17.GetPixel(3, 12).A -eq 0) -and ($b16_17.GetPixel(4, 12).A -gt 0)) "16x16 multi-digit 1 bare stem"
    $b16_17.Dispose()
}

Invoke-TestCase "4. 托盘菜单、防抖、双击恢复与无闪烁布局收敛 (MainForm 单实例)" {
    $regPath = "HKCU:\Software\FerrisPulse"
    if (-not (Test-Path $regPath)) { New-Item -Path $regPath -Force | Out-Null }
    $expectedX = 500; $expectedY = 260
    Set-ItemProperty -Path $regPath -Name "WindowX" -Value $expectedX -Type DWord
    Set-ItemProperty -Path $regPath -Name "WindowY" -Value $expectedY -Type DWord

    $form = New-SandboxMainForm $false
    try {
        [System.Windows.Forms.Form]$wf = $form
        $null = $wf.Handle

        # A) Window position restore & out-of-bounds clamping
        $wa = [System.Windows.Forms.Screen]::PrimaryScreen.WorkingArea
        $expCX = [Math]::Max($wa.Left, [Math]::Min($expectedX, [Math]::Max($wa.Left, $wa.Right - $wf.Width)))
        $expCY = [Math]::Max($wa.Top, [Math]::Min($expectedY, [Math]::Max($wa.Top, $wa.Bottom - $wf.Height)))
        Assert-Equal $expCX $wf.Location.X "WindowX restored"
        Assert-Equal $expCY $wf.Location.Y "WindowY restored"
        $tMain.GetField("savedWindowX", $BF).SetValue($form, $wa.Right + 500)
        $tMain.GetField("savedWindowY", $BF).SetValue($form, $wa.Bottom + 500)
        $tMain.GetMethod("RestoreWindowPosition", $BF).Invoke($form, $null) | Out-Null
        if ($wf.Width -le $wa.Width -and $wf.Height -le $wa.Height) {
            Assert-True ($wf.Right -le $wa.Right -and $wf.Bottom -le $wa.Bottom) "Out-of-bounds position clamped"
        }

        # B) OnFormClosing daemon guards + ToggleWindow restore animation
        $mClosing = $tMain.GetMethod("OnFormClosing", $BF)
        $eUser = New-Object System.Windows.Forms.FormClosingEventArgs([System.Windows.Forms.CloseReason]::UserClosing, $false)
        $mClosing.Invoke($wf, [object[]]@($eUser.PSObject.BaseObject)) | Out-Null
        Assert-True $eUser.Cancel "UserClosing cancelled"
        $eNone = New-Object System.Windows.Forms.FormClosingEventArgs([System.Windows.Forms.CloseReason]::None, $false)
        $mClosing.Invoke($wf, [object[]]@($eNone.PSObject.BaseObject)) | Out-Null
        Assert-True $eNone.Cancel "None CloseReason cancelled"
        $eApp = New-Object System.Windows.Forms.FormClosingEventArgs([System.Windows.Forms.CloseReason]::ApplicationExitCall, $false)
        $mClosing.Invoke($wf, [object[]]@($eApp.PSObject.BaseObject)) | Out-Null
        Assert-True (-not $eApp.Cancel) "ApplicationExitCall allowed"

        $wf.Visible = $true
        $mToggle = $tMain.GetMethod("ToggleWindow", $BF)
        $mToggle.Invoke($wf, $null) | Out-Null
        Assert-True (-not $wf.Visible -and $wf.WindowState -ne [System.Windows.Forms.FormWindowState]::Minimized) "ToggleWindow hides without Minimized"
        $mToggle.Invoke($wf, $null) | Out-Null
        Assert-True ($wf.Visible -and $wf.WindowState -eq [System.Windows.Forms.FormWindowState]::Normal) "ToggleWindow restores Normal"

        # C) Tray style menu + SetTrayStyle
        $ctx = $tMain.GetField("contextMenu", $BF).GetValue($form)
        Assert-True (-not $ctx.ShowImageMargin) "ShowImageMargin must be false"
        $allMenu = New-Object System.Collections.Generic.List[System.Windows.Forms.ToolStripItem]
        $collect = { param($items) foreach ($it in $items) { if ($it -is [System.Windows.Forms.ToolStripMenuItem]) { $allMenu.Add($it); if ($it.DropDownItems.Count -gt 0) { & $collect $it.DropDownItems } } } }
        & $collect $ctx.Items
        foreach ($it in $allMenu) {
            Assert-True ($it.Text -notmatch "[\uD83C-\uDBFF\uDC00-\uDFFF]|[\u2600-\u27BF]") "MenuItem clean of emoji: $($it.Text)"
            Assert-True ($it.Text -notmatch "\(&[A-Za-z]\)") "MenuItem clean of mnemonic: $($it.Text)"
            Assert-True ($null -eq $it.Image) "MenuItem clean of image: $($it.Text)"
        }
        $mClassify = $tMain.GetMethod("ShouldKeepTrayMenuOpen", $BF)
        foreach ($kw in @("开机自动启动", "低电量气泡通知", "DPI 切换屏幕提示", "30 秒", "现代胶囊电池")) {
            $found = $allMenu | Where-Object { $_.Text -like "*$kw*" } | Select-Object -First 1
            if ($null -ne $found) { Assert-True ([bool]$mClassify.Invoke($form, [object[]]@($found.PSObject.BaseObject))) "KeepOpen: $kw" }
        }
        foreach ($kw in @("打开控制面板", "立即刷新电量", "退出程序")) {
            $found = $allMenu | Where-Object { $_.Text -like "*$kw*" } | Select-Object -First 1
            if ($null -ne $found) { Assert-True (-not [bool]$mClassify.Invoke($form, [object[]]@($found.PSObject.BaseObject))) "CloseOnAction: $kw" }
        }

        $tMain.GetMethod("SetTrayStyle", $BF).Invoke($form, @([int]2, [bool]$false)) | Out-Null
        Assert-Equal 2 ([int]$tMain.GetField("trayStyle", $BF).GetValue($form)) "SetTrayStyle(2)"

        # D) Menu flicker-free height convergence + Font reuse
        $activeList = $tMain.GetField("activeDevices", $BF).GetValue($form)
        $activeList.Clear()
        $m1 = [Activator]::CreateInstance($tInfo)
        $m1.DeviceId = "Test:M1"; $m1.Brand = "Razer"; $m1.DeviceName = "Razer Viper V2 Pro"
        $m1.Category = [Enum]::Parse($tCat, "Mouse"); $m1.IsConnected = $true; $m1.IsPrimary = $true; $m1.BatteryPercent = 80
        $m1.DpiStages = [int[]]@(400, 800, 1600); $m1.SupportedPollingRates = [int[]]@(125, 500, 1000)
        $k1 = [Activator]::CreateInstance($tInfo)
        $k1.DeviceId = "Test:K1"; $k1.Brand = "ASUS"; $k1.DeviceName = "ROG AZOTH 96 HE"
        $k1.Category = [Enum]::Parse($tCat, "Keyboard"); $k1.IsConnected = $true; $k1.BatteryPercent = 100
        $activeList.Add($m1); $activeList.Add($k1)
        $tMain.GetMethod("UpdateTrayDeviceMenuItems", $BF).Invoke($form, @()) | Out-Null
        $dyn1 = $tMain.GetField("dynamicDeviceMenuItems", $BF).GetValue($form)
        Assert-Equal 2 $dyn1.Count "Dynamic tray device items"
        Assert-True ([object]::ReferenceEquals($dyn1[0].Font, $dyn1[1].Font)) "Reusable trayDeviceItemFont"
        $h1 = $ctx.PreferredSize.Height
        $m1.IsPrimary = $false; $k1.IsPrimary = $true
        $tMain.GetMethod("UpdateTrayDeviceMenuItems", $BF).Invoke($form, @()) | Out-Null
        Assert-True ($ctx.PreferredSize.Height -lt $h1 -and $ctx.Height -eq $ctx.PreferredSize.Height) "Tray menu height converged synchronously"

        # E) Device switch UI cleanup (Mouse -> NuPhy keyboard hides GameOpt controls)
        $mPerf = $tMain.GetMethod("UpdatePerformanceSection", $BF)
        $tMain.GetField("autoGameDpiEnabled", $BF).SetValue($form, $true)
        $tMain.GetField("autoGameRateEnabled", $BF).SetValue($form, $true)
        $mPerf.Invoke($form, @($m1, [System.Drawing.Color]::Lime)) | Out-Null
        $mPerf.Invoke($form, @($k1, [System.Drawing.Color]::Cyan)) | Out-Null
        Assert-True (-not $tMain.GetField("chkAutoGameOpt", $BF).GetValue($form).Visible) "chkAutoGameOpt hidden on keyboard"
        Assert-True (-not $tMain.GetField("chkAutoGameDpi", $BF).GetValue($form).Visible) "chkAutoGameDpi hidden on keyboard"

        # F) Performance label geometry across DPI scales (no overlap)
        $flags = [System.Windows.Forms.TextFormatFlags]::Left -bor [System.Windows.Forms.TextFormatFlags]::VerticalCenter -bor [System.Windows.Forms.TextFormatFlags]::SingleLine
        $chkRate = $tMain.GetField("chkAutoGameRate", $BF).GetValue($form)
        $rateArr = @($tMain.GetField("btnGameRates", $BF).GetValue($form))
        foreach ($sc in @(1.0, 1.25, 1.5, 2.0)) {
            $tMain.GetField("dpiScale", $BF).SetValue($form, [float]$sc)
            $tMain.GetMethod("RelayoutPerformanceControls", $BF).Invoke($form, $null) | Out-Null
            [System.Windows.Forms.Application]::DoEvents()
            $tw = [System.Windows.Forms.TextRenderer]::MeasureText($chkRate.Text, $chkRate.Font, [System.Drawing.Size]::Empty, $flags).Width
            if ($null -ne $rateArr[0] -and $rateArr[0].Visible) {
                Assert-True (($chkRate.Left + 25 + $tw) -lt $rateArr[0].Left) "Scale ${sc}: game-rate label does not overlap button"
            }
        }
    } finally { $form.Dispose() }
}

Invoke-TestCase "5. 无极缩放助手、Secondary Dialogs 0.75x、AboutForm 与 ScrollPanel 渲染" {
    $minScale = [float]$tScale.GetField("MinScale", $BF).GetValue($null)
    Assert-True ([Math]::Abs($minScale - 0.60) -lt 0.001) "MinScale == 0.60"
    $mFontSz = $tScale.GetMethod("GetScaledFontSize", $BF)
    Assert-True ([Math]::Abs([float]$mFontSz.Invoke($null, @([float]20.0, [float]0.75, [float]1.5)) - 10.0) -lt 0.05) "GetScaledFontSize cross-DPI"
    Assert-True ([Math]::Abs([float]$mFontSz.Invoke($null, @([float]8.5, [float]0.65, [float]1.5)) - 3.68) -lt 0.08) "GetScaledFontSize unclamped"

    $tAbout = $asm.GetType("RazerBatteryTray.AboutForm", $true)
        $about = [Activator]::CreateInstance($tAbout, $BF, $null, [object[]]@([float]1.0), $null)
    try {
        Assert-True ($about.ClientSize.Width -eq 600 -and $about.ClientSize.Height -eq 668) "AboutForm(1.0f) 600x668"
        $texts = New-Object System.Collections.Generic.List[string]
        $q = New-Object System.Collections.Queue; $q.Enqueue($about)
        while ($q.Count -gt 0) {
            $c = $q.Dequeue()
            if (-not [string]::IsNullOrEmpty($c.Text)) { $texts.Add($c.Text) }
            foreach ($ch in $c.Controls) { $q.Enqueue($ch) }
        }
        $joined = $texts -join " | "
        Assert-True ($joined -match "Ferris-echo" -and $joined -match "FerrisPulse" -and $joined -match "检查更新" -and ($joined -notmatch "旗舰版")) "AboutForm presentation"

        $lblDesc = $tAbout.GetField("lblDonateDesc", $BF).GetValue($about)
        $descSz = [System.Windows.Forms.TextRenderer]::MeasureText($lblDesc.Text, $lblDesc.Font)
        Assert-True ($descSz.Width -le ($tAbout.GetField("cardDonate", $BF).GetValue($about).Width - 40)) "AboutForm donate desc fits single line without orphan wrap"

        $lnkQQ = $tAbout.GetField("lnkQQ", $BF).GetValue($about)
        $lnkGit = $tAbout.GetField("lnkGithub", $BF).GetValue($about)
        $lnkReset = $tAbout.GetField("lnkResetApp", $BF).GetValue($about)
        $cardDonate = $tAbout.GetField("cardDonate", $BF).GetValue($about)
        Assert-True (-not $cardDonate.Controls.Contains($lnkQQ) -and -not $cardDonate.Controls.Contains($lnkGit)) "AboutForm footer links cleanly separated outside cardDonate"
        Assert-Equal $lnkQQ.Top $lnkGit.Top "AboutForm QQ and GitHub aligned horizontally on footer row 1"
        Assert-True ($lnkReset.Top -gt $lnkGit.Top) "AboutForm Reset link sits cleanly below row 1"

        # High-DPI overlap & Card 1 equal row gap verification
        $lblLogo = $tAbout.GetField("lblLogo", $BF).GetValue($about)
        $lblAuth = $tAbout.GetField("lblAuthorPrefix", $BF).GetValue($about)
        $logoH = [System.Windows.Forms.TextRenderer]::MeasureText($lblLogo.Text, $lblLogo.Font).Height
        Assert-True ($lblAuth.Top -ge ($lblLogo.Top + $logoH)) "AboutForm author line never overlapped by main logo heading"

        $lblUpdTitle = $tAbout.GetField("lblCardUpdateTitle", $BF).GetValue($about)
        $chkUpd = $tAbout.GetField("chkAutoUpdate", $BF).GetValue($about)
        $lblUpdSt = $tAbout.GetField("lblUpdateStatus", $BF).GetValue($about)
        $h1 = [System.Windows.Forms.TextRenderer]::MeasureText($lblUpdTitle.Text, $lblUpdTitle.Font).Height
        $gap12 = $chkUpd.Top - ($lblUpdTitle.Top + $h1)
        $gap23 = $lblUpdSt.Top - ($chkUpd.Top + $chkUpd.Height)
        Assert-True ([Math]::Abs($gap12 - $gap23) -le 2) ("AboutForm Card 1 gaps equal: gap12=$gap12 gap23=$gap23")

        $aboutSrc = Get-Content (Join-Path $root "src\UI\Forms\AboutForm.cs") -Raw -Encoding UTF8
        Assert-True ($aboutSrc.Contains("new ResetConfirmDialog(ResolveChildDialogScale())")) "AboutForm passes ResolveChildDialogScale() to ResetConfirmDialog for 4K sizing"

        # 彩蛋背卡渲染与标题栏沉浸隐藏校验
        $getStateAbout = [System.Windows.Forms.Control].GetMethod("GetState", [System.Reflection.BindingFlags]"Instance,NonPublic")
        $chrBar = $asm.GetType("RazerBatteryTray.DpiAwareForm", $true).GetField("modernChromeTitleBar", $BF).GetValue($about)
        Assert-True ($null -ne $chrBar -and [bool]$getStateAbout.Invoke($chrBar, @(2))) "AboutForm front face shows chrome title bar"
        $tAbout.GetMethod("TriggerQuantumShatter", $BF).Invoke($about, @($true)) | Out-Null
        Assert-True (-not [bool]$getStateAbout.Invoke($chrBar, @(2))) "AboutForm Easter Egg transition hides chrome title bar"
        $cq = $tAbout.GetField("cardQuote", $BF).GetValue($about)
        $cqBmp = New-Object System.Drawing.Bitmap([Math]::Max(20, $cq.Width), [Math]::Max(20, $cq.Height))
        try {
            $cq.DrawToBitmap($cqBmp, (New-Object System.Drawing.Rectangle(0, 0, $cqBmp.Width, $cqBmp.Height)))
        } finally { $cqBmp.Dispose() }
    } finally { $about.Dispose() }

    $tScroll = $asm.GetType("RazerBatteryTray.ModernDarkScrollPanel", $true)
    $pnl = [Activator]::CreateInstance($tScroll)
    try {
        $pnl.Size = New-Object System.Drawing.Size(520, 240)
        $list = New-Object System.Collections.Generic.List[string]
        for ($i = 1; $i -le 18; $i++) { $list.Add("条目 $i · FerrisPulse 双缓冲视口") }
        $pnl.SetChangelog($list, 1.0)
        $b1 = New-Object System.Drawing.Bitmap(520, 240); $pnl.DrawToBitmap($b1, (New-Object System.Drawing.Rectangle(0, 0, 520, 240))); $b1.Dispose()
        $pnl.ScrollOffset = 120
        $b2 = New-Object System.Drawing.Bitmap(520, 240); $pnl.DrawToBitmap($b2, (New-Object System.Drawing.Rectangle(0, 0, 520, 240))); $b2.Dispose()
    } finally { $pnl.Dispose() }

    $mainUiSrc = Get-Content (Join-Path $root "src\UI\Forms\MainForm.UI.cs") -Raw -Encoding UTF8
    Assert-True ($mainUiSrc.Contains("private int MeasurePerfLabelColumnW()")) "MeasurePerfLabelColumnW present"
    Assert-True ($mainUiSrc -match "FormBorderStyle\.Sizable" -and $mainUiSrc -match "ApplyScaledFontsToControlTree") "Sizable & font scaling"

    $dpiFormType = $asm.GetType("RazerBatteryTray.DpiAwareForm", $true)
    $dpiScaleField = $dpiFormType.GetField("dpiScale", $BF)
    $chromeBarField = $dpiFormType.GetField("modernChromeTitleBar", $BF)
    $ratesArr = [int[]]@(125, 500, 1000)
    foreach ($chk in @(
        @{ Name = "ResetConfirmDialog"; Args = [object[]]@([float]0.75); BaseW = 460 },
        @{ Name = "PromptDialog"; Args = [object[]]@([string]"重命名", [string]"名称:", [string]"默认", [float]0.75); BaseW = 380 },
        @{ Name = "PollingRateManagementDialog"; Args = [object[]]@([string]"Razer", $ratesArr, $ratesArr, [int]1000, [float]0.75); BaseW = 420 },
        @{ Name = "ImportAdapterDialog"; Args = [object[]]@([float]0.75); BaseW = 500 },
        @{ Name = "VgnKeyboardForm"; Args = [object[]]@([float]0.75); BaseW = 460 }
    )) {
        $t = $asm.GetType("RazerBatteryTray." + $chk.Name, $true)
        $inst = [Activator]::CreateInstance($t, $BF, $null, $chk.Args, $null)
        try {
            $actualScale = [float]$dpiScaleField.GetValue($inst)
            Assert-True ([Math]::Abs($actualScale - 0.75) -lt 0.02) "$($chk.Name)(0.75f) scale preserved"
            $expectedW = [int][Math]::Round($chk.BaseW * 0.75)
            Assert-True ([Math]::Abs($inst.ClientSize.Width - $expectedW) -le 6) "$($chk.Name)(0.75f) width scaled"
            Assert-True ($inst.FormBorderStyle -eq [System.Windows.Forms.FormBorderStyle]::None) "$($chk.Name) uses unified borderless chrome"
            Assert-True ($null -ne $chromeBarField.GetValue($inst)) "$($chk.Name) attaches modernChromeTitleBar"
        } finally { $inst.Dispose() }
    }

    # 抽检代表性业务二级弹窗均接入统一无边框纯文字自绘标题栏，且具备原生 DWM 动画所需的 WS_CAPTION | WS_SYSMENU（零非客户区膨胀）
    $probeType = @"
using System;
using System.Runtime.InteropServices;
public static class Win32StyleProbe_DwmAnim {
    [StructLayout(LayoutKind.Sequential)] public struct RECT { public int Left, Top, Right, Bottom; }
    [DllImport("user32.dll")] public static extern int GetWindowLong(IntPtr hWnd, int nIndex);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool GetClientRect(IntPtr hWnd, out RECT lpRect);
}
"@
    if (-not ([System.Management.Automation.PSTypeName]"Win32StyleProbe_DwmAnim").Type) {
        Add-Type -TypeDefinition $probeType
    }

    $tUpdateDlg = $asm.GetType("RazerBatteryTray.UpdateNoticeForm", $true)
    $tHistDlg = $asm.GetType("RazerBatteryTray.ChangelogHistoryForm", $true)
    $tUpdInfo = $asm.GetType("RazerBatteryTray.UpdateInfo", $true)
    $mockInfo = [Activator]::CreateInstance($tUpdInfo)
    $mockInfo.LatestVersion = "2.4.1"
    $mockInfo.Changelog = "Test changelog"

    foreach ($dlgChk in @(
        @{ Name = "AboutForm"; Args = [object[]]@([float]1.0); ExpectChromeBar = $true },
        @{ Name = "DevicePriorityForm"; Args = [object[]]@($null, [float]1.0); ExpectChromeBar = $true },
        @{ Name = "AddDeviceWizardForm"; Args = [object[]]@([float]1.0); ExpectChromeBar = $true },
        @{ Name = "DiagnosticReportDialog"; Args = [object[]]@([float]1.0); ExpectChromeBar = $true },
        @{ Name = "LowBatteryAlertSettingsDialog"; Args = [object[]]@($true, $true, $true, [float]1.0); ExpectChromeBar = $true },
        @{ Name = "AiToolsDialog"; Args = [object[]]@([float]1.0); ExpectChromeBar = $true },
        @{ Name = "MouseRateTestDialog"; Args = [object[]]@([float]1.0); ExpectChromeBar = $true },
        @{ Name = "DpiManagementDialog"; Args = [object[]]@("Razer", [int[]]@(800, 1600), 1, $null, [float]1.0); ExpectChromeBar = $true },
        @{ Name = "PollingRateManagementDialog"; Args = [object[]]@("Razer", $ratesArr, $ratesArr, [int]1000, [float]1.0); ExpectChromeBar = $true },
        @{ Name = "VgnKeyboardForm"; Args = [object[]]@([float]1.0); ExpectChromeBar = $true },
        @{ Name = "UpdateNoticeForm"; Args = [object[]]@($mockInfo, "2.3.2", [float]1.0); ExpectChromeBar = $false },
        @{ Name = "ChangelogHistoryForm"; Args = [object[]]@([float]1.0); ExpectChromeBar = $false }
    )) {
        $dt = $asm.GetType("RazerBatteryTray." + $dlgChk.Name, $true)
        $dInst = [Activator]::CreateInstance($dt, $BF, $null, $dlgChk.Args, $null)
        try {
            Assert-True ($dInst.FormBorderStyle -eq [System.Windows.Forms.FormBorderStyle]::None) "$($dlgChk.Name) unified FormBorderStyle.None"
            if ($dlgChk.ExpectChromeBar) {
                $bar = $chromeBarField.GetValue($dInst)
                Assert-True ($null -ne $bar) "$($dlgChk.Name) attached pure-text modern chrome"
                Assert-Equal $dInst.BackColor $bar.BackColor "$($dlgChk.Name) uses seamless unified titlebar BackColor"
                $closeBtn = $dpiFormType.GetField("modernChromeCloseButton", $BF).GetValue($dInst)
                Assert-True ($null -ne $closeBtn -and $closeBtn.IsCloseButton -and $closeBtn.Width -eq $closeBtn.Height) "$($dlgChk.Name) uses unified square vector close button"
                Assert-True ([bool]$dpiFormType.GetField("modernChromeUseIntegratedHeroHeader", $BF).GetValue($dInst)) "$($dlgChk.Name) enables unified integrated hero header"
                Assert-True ($bar.Height -ge 50) "$($dlgChk.Name) hero header height >= 50"
                Assert-True ($closeBtn.NormalColor -ne [System.Drawing.Color]::Transparent) "$($dlgChk.Name) close button uses dark pill fill"
            } elseif ($dlgChk.Name -eq "UpdateNoticeForm") {
                $uc = $dt.GetField("btnClose", $BF).GetValue($dInst)
                Assert-True ($null -ne $uc -and $uc.Parent.Height -ge 50 -and $uc.NormalColor -ne [System.Drawing.Color]::Transparent) "UpdateNoticeForm uses 52px hero header & dark pill close button"
            } elseif ($dlgChk.Name -eq "ChangelogHistoryForm") {
                $hc = $dt.GetField("btnCloseTop", $BF).GetValue($dInst)
                Assert-True ($null -ne $hc -and $hc.Parent.Height -ge 50 -and $hc.NormalColor -ne [System.Drawing.Color]::Transparent) "ChangelogHistoryForm uses 52px hero header & dark pill close button"
            }
            $h = $dInst.Handle
            $st = [Win32StyleProbe_DwmAnim]::GetWindowLong($h, -16)
            Assert-True (($st -band 0x00C00000) -eq 0x00C00000) "$($dlgChk.Name) has WS_CAPTION for native DWM animation"
            Assert-True (($st -band 0x00080000) -eq 0x00080000) "$($dlgChk.Name) has WS_SYSMENU for native DWM animation"
            Assert-True (($st -band ([int]-2147483648)) -eq 0) "$($dlgChk.Name) strips WS_POPUP"
            Assert-True (($st -band 0x00040000) -eq 0) "$($dlgChk.Name) strips WS_THICKFRAME"
            $wr = New-Object Win32StyleProbe_DwmAnim+RECT
            $cr = New-Object Win32StyleProbe_DwmAnim+RECT
            [void][Win32StyleProbe_DwmAnim]::GetWindowRect($h, [ref]$wr)
            [void][Win32StyleProbe_DwmAnim]::GetClientRect($h, [ref]$cr)
            Assert-Equal ($cr.Right - $cr.Left) ($wr.Right - $wr.Left) "$($dlgChk.Name) zero NC width inflation"
            Assert-Equal ($cr.Bottom - $cr.Top) ($wr.Bottom - $wr.Top) "$($dlgChk.Name) zero NC height inflation"
        } finally { $dInst.Dispose() }
    }

    # 验证设备优先级升级版：分组圆角卡片、分段胶囊槽位、稳重底部栏及顶部三段左轨严格对齐 + 添加向导保留大标题配完整副标题
    $pForm = [Activator]::CreateInstance($tPrioForm, $BF, $null, [object[]]@($null, [float]1.0), $null)
    try {
        $chkLow = $tPrioForm.GetField("chkLowBatteryFirst", $BF).GetValue($pForm)
        $chkMulti = $tPrioForm.GetField("chkMultiTray", $BF).GetValue($pForm)
        $pnlCnt = $tPrioForm.GetField("pnlTrayCountBar", $BF).GetValue($pForm)
        $cardSet = $tPrioForm.GetField("cardSettings", $BF).GetValue($pForm)
        $reord = $tPrioForm.GetField("reorderControl", $BF).GetValue($pForm)
        $segTrack = $tPrioForm.GetField("pnlSegmentTrack", $BF).GetValue($pForm)
        $pnlFoot = $tPrioForm.GetField("pnlFooter", $BF).GetValue($pForm)
        Assert-True ($null -ne $cardSet -and $cardSet.Controls.Contains($chkLow) -and $cardSet.Controls.Contains($chkMulti) -and $cardSet.Controls.Contains($pnlCnt)) "DevicePriorityForm groups toggles in elevated cardSettings"
        Assert-True ($cardSet.Top -gt $reord.Top -and $reord.Bottom -le $cardSet.Top) "DevicePriorityForm places settings card cleanly below device list"
        $expectedSlots = if ($chkMulti.Checked) { [int]$tPrioForm.GetField("selectedTrayCount", $BF).GetValue($pForm) } else { 1 }
        Assert-Equal $expectedSlots $reord.ActiveTraySlotCount "DevicePriorityForm syncs ActiveTraySlotCount to list indices"
        Assert-True ($null -ne $segTrack -and $pnlCnt.Controls.Contains($segTrack)) "DevicePriorityForm uses segmented capsule track"
        Assert-True ($null -ne $pnlFoot) "DevicePriorityForm has grounded footer bar"
        Assert-Equal $chkLow.Left $chkMulti.Left "DevicePriorityForm checkboxes aligned left"
        Assert-True ($pnlCnt.Left -gt $chkMulti.Left) "Inline tray count selector sits to the right of chkMultiTray"
        Assert-True ([Math]::Abs(($pnlCnt.Top + $pnlCnt.Height / 2) - ($chkMulti.Top + $chkMulti.Height / 2)) -le 4) "Inline tray count selector vertically centered with chkMultiTray"
        Assert-True (($cardSet.Height - $pnlCnt.Bottom) -ge 4) "cardSettings bottom border completely clear of inline selector"
        Assert-Equal "状态栏多设备独立图标" $chkMulti.Text "DevicePriorityForm concise multi-tray label"
        if ($segTrack.OptionCount -ge 3) {
            $segTrack.SelectValue(2, $false)
            $x2 = $segTrack.CurrentThumbX
            $segTrack.SelectValue(4, $false)
            $x4 = $segTrack.CurrentThumbX
            Assert-True ($x4 -gt $x2) "SlidingSegmentTrack thumb advances across segments"
        }
        $segBmp = New-Object System.Drawing.Bitmap([Math]::Max(10, $segTrack.Width), [Math]::Max(10, $segTrack.Height))
        try {
            $segTrack.DrawToBitmap($segBmp, (New-Object System.Drawing.Rectangle(0, 0, $segBmp.Width, $segBmp.Height)))
        } finally { $segBmp.Dispose() }
    } finally { $pForm.Dispose() }

    # 验证 PriorityReorderControl 全宽卡片内嵌动态实时序号 (拖拽经过槽位时实时互换 01↔02↔03) 及零异常渲染
    $tReordCtrl = $asm.GetType("RazerBatteryTray.PriorityReorderControl", $true)
    $tPrioItem = $asm.GetType("RazerBatteryTray.PriorityItem", $true)
    $rc = [Activator]::CreateInstance($tReordCtrl)
    try {
        $rc.DpiScale = [float]1.0
        $rc.Size = New-Object System.Drawing.Size(436, 260)
        $rc.ActiveTraySlotCount = 3
        $names = @("Buds4 Pro", "Xbox Controller", "DeathAdder V4 Pro", "NuPhy WH80")
        for ($i = 0; $i -lt 4; $i++) {
            $pi = [Activator]::CreateInstance($tPrioItem)
            $pi.DeviceId = "DEV_$i"
            $pi.Name = $names[$i]
            $pi.Battery = 90 - $i * 5
            $pi.IsConnected = $true
            $pi.AccentColor = [System.Drawing.Color]::FromArgb(0, 230, 118)
            $pi.PriorityIndex = $i + 1
            $rc.Items.Add($pi)
        }
        $rc.ResetPositions()

        # 空闲状态：01, 02, 03, 04
        for ($i = 0; $i -lt 4; $i++) {
            Assert-Equal ($i + 1) $rc.GetLiveDisplayRank($i) "Idle rank item $i"
        }
        $bmpIdle = New-Object System.Drawing.Bitmap(436, 260)
        try {
            $rc.DrawToBitmap($bmpIdle, (New-Object System.Drawing.Rectangle(0, 0, 436, 260)))
        } finally { $bmpIdle.Dispose() }

        # 正向拖拽（含动态分裂左侧固定数字轨道渲染）：第 0 项拖向槽位 2 -> item0=3, item1=1, item2=2, item3=4
        $tReordCtrl.GetField("isDragging", $BF).SetValue($rc, $true)
        $tReordCtrl.GetField("draggedIndex", $BF).SetValue($rc, 0)
        $tReordCtrl.GetField("currentDropIndex", $BF).SetValue($rc, 2)
        $tReordCtrl.GetField("dragSplitProgress", $BF).SetValue($rc, [single]1.0)
        Assert-Equal 3 $rc.GetLiveDisplayRank(0) "Forward drag item 0 displays 3"
        Assert-Equal 1 $rc.GetLiveDisplayRank(1) "Forward drag item 1 displays 1"
        Assert-Equal 2 $rc.GetLiveDisplayRank(2) "Forward drag item 2 displays 2"
        Assert-Equal 4 $rc.GetLiveDisplayRank(3) "Forward drag item 3 displays 4"

        $bmpDrag = New-Object System.Drawing.Bitmap(436, 260)
        try {
            $rc.DrawToBitmap($bmpDrag, (New-Object System.Drawing.Rectangle(0, 0, 436, 260)))
        } finally { $bmpDrag.Dispose() }

        # 反向拖拽：第 3 项拖向槽位 1 -> item0=1, item1=3, item2=4, item3=2
        $tReordCtrl.GetField("draggedIndex", $BF).SetValue($rc, 3)
        $tReordCtrl.GetField("currentDropIndex", $BF).SetValue($rc, 1)
        Assert-Equal 1 $rc.GetLiveDisplayRank(0) "Reverse drag item 0 displays 1"
        Assert-Equal 3 $rc.GetLiveDisplayRank(1) "Reverse drag item 1 displays 3"
        Assert-Equal 4 $rc.GetLiveDisplayRank(2) "Reverse drag item 2 displays 4"
        Assert-Equal 2 $rc.GetLiveDisplayRank(3) "Reverse drag item 3 displays 2"
    } finally { $rc.Dispose() }

    $tWiz = $asm.GetType("RazerBatteryTray.AddDeviceWizardForm", $true)
    $wiz = [Activator]::CreateInstance($tWiz, $BF, $null, [object[]]@([float]1.0), $null)
    try {
        $foundBigTitle = $false
        $foundFullSub = $false
        $wizQueue = New-Object System.Collections.Queue
        $wizQueue.Enqueue($wiz)
        while ($wizQueue.Count -gt 0) {
            $c = $wizQueue.Dequeue()
            if ($c -is [System.Windows.Forms.Label]) {
                if ($c.Text -eq "添加与配置外设向导") { $foundBigTitle = $true }
                if ($c.Text -like "主流外设*" -and -not $c.AutoEllipsis -and $c.Width -ge 380) {
                    $sz = [System.Windows.Forms.TextRenderer]::MeasureText($c.Text, $c.Font)
                    if ($sz.Width -le $c.Width) { $foundFullSub = $true }
                }
            }
            foreach ($ch in $c.Controls) { $wizQueue.Enqueue($ch) }
        }
        Assert-True $foundBigTitle "AddDeviceWizardForm promotes big heading into integrated hero header"
        Assert-True $foundFullSub "AddDeviceWizardForm keeps full-width unclipped subtitle"
    } finally { $wiz.Dispose() }

    # 悬浮浮层类窗体保持纯无边框（不注入 WS_CAPTION）
    $tFlyout = $asm.GetType("RazerBatteryTray.ModernDeviceSwitcherFlyout", $true)
    $flyInst = [Activator]::CreateInstance($tFlyout, $BF, $null, [object[]]@([float]1.0), $null)
    try {
        $fh = $flyInst.Handle
        $fst = [Win32StyleProbe_DwmAnim]::GetWindowLong($fh, -16)
        Assert-True (($fst -band 0x00C00000) -ne 0x00C00000) "ModernDeviceSwitcherFlyout stays pure WS_POPUP without WS_CAPTION"
    } finally { $flyInst.Dispose() }
}

Invoke-TestCase "6. 耳机页零泄露鼠标齿轮、Per-Monitor V2 清晰感知与切换列表共享设备库" {
    # A) Headset / Earbuds must keep btnDpiManage & btnRateManage hidden after zoom/relayout
    $form = New-SandboxMainForm $false
    try {
        $null = ([System.Windows.Forms.Form]$form).Handle
        $earbuds = [Activator]::CreateInstance($tInfo)
        $earbuds.DeviceId = "BT:Audio:Buds4Pro"; $earbuds.Brand = "Xiaomi"; $earbuds.DeviceName = "Buds4 Pro"
        $earbuds.Category = [Enum]::Parse($tCat, "Earbuds"); $earbuds.IsConnected = $false; $earbuds.BatteryPercent = 67
        $tMain.GetField("selectedDevice", $BF).SetValue($form, $earbuds)
        $tMain.GetMethod("UpdatePerformanceSection", $BF).Invoke($form, @($earbuds, [System.Drawing.Color]::MediumPurple)) | Out-Null
        $tMain.GetField("dpiScale", $BF).SetValue($form, [float]0.90)
        $tMain.GetMethod("ApplyDpiScaleToControls", $BF).Invoke($form, $null) | Out-Null

        $getState = [System.Windows.Forms.Control].GetMethod("GetState", [System.Reflection.BindingFlags]"Instance,NonPublic")
        $btnDpiMgr = $tMain.GetField("btnDpiManage", $BF).GetValue($form)
        $btnRateMgr = $tMain.GetField("btnRateManage", $BF).GetValue($form)
        Assert-True (-not [bool]$getState.Invoke($btnDpiMgr, @(2))) "Earbuds card must hide btnDpiManage even after ApplyDpiScaleToControls"
        Assert-True (-not [bool]$getState.Invoke($btnRateMgr, @(2))) "Earbuds card must hide btnRateManage even after ApplyDpiScaleToControls"

        # B) Switcher list must expose full known library (GetAllKnownDevicesForPriority) & keep pinned offline selection
        $prioBackup = [RazerBatteryTray.DeviceManager]::GetPriorityList()
        try {
            $prioIds = New-Object 'System.Collections.Generic.List[string]'
            $prioIds.Add("BT:Audio:Buds4Pro")
            $prioIds.Add("BT:Gamepad:Xbox")
            $prioIds.Add("Razer:1532:00C0")
            $prioIds.Add("NuPhy:WH80:19F5:8F01")
            [RazerBatteryTray.DeviceManager]::SavePriorityList($prioIds)

            $activeList = $tMain.GetField("activeDevices", $BF).GetValue($form)
            $activeList.Clear()
            $activeList.Add($earbuds)
            $xbox = [Activator]::CreateInstance($tInfo)
            $xbox.DeviceId = "BT:Gamepad:Xbox"; $xbox.DeviceName = "Xbox Wireless Controller"
            $xbox.Category = [Enum]::Parse($tCat, "Gamepad"); $xbox.IsConnected = $false; $xbox.BatteryPercent = 61
            $activeList.Add($xbox)

            $expectedLibCount = [RazerBatteryTray.DeviceManager]::GetAllKnownDevicesForPriority($activeList).Count
            $mGetSwitcher = $tMain.GetMethod("GetDeviceSwitcherDevices", $BF)
            Assert-True ($null -ne $mGetSwitcher) "GetDeviceSwitcherDevices exists on MainForm"
            $switcherDevs = $mGetSwitcher.Invoke($form, $null)
            Assert-Equal $expectedLibCount $switcherDevs.Count "Switcher matches GetAllKnownDevicesForPriority count cleanly"
            Assert-True ($switcherDevs.Count -ge 4) "Switcher includes offline library devices (at least 4)"

            $wh80 = $null
            foreach ($d in $switcherDevs) { if ($d.DeviceId -eq "NuPhy:WH80:19F5:8F01") { $wh80 = $d; break } }
            Assert-True ($null -ne $wh80) "NuPhy WH80 present in switcher"
            Assert-Equal "Keyboard" $wh80.Category.ToString() "Offline NuPhy WH80 resolved as Keyboard icon, not Mouse"

            # Selecting offline Razer mouse from switcher stays pinned across subsequent ApplyDeviceStatus poll
            $razerOff = $null
            foreach ($d in $switcherDevs) { if ($d.DeviceId -eq "Razer:1532:00C0") { $razerOff = $d; break } }
            $tMain.GetMethod("SelectDeviceView", $BF).Invoke($form, @($razerOff)) | Out-Null
            $tMain.GetMethod("ApplyDeviceStatus", $BF).Invoke($form, @($activeList, $false)) | Out-Null
            $curSel = $tMain.GetField("selectedDevice", $BF).GetValue($form)
            Assert-True ($null -ne $curSel -and $curSel.DeviceId -eq "Razer:1532:00C0") "Offline library selection preserved across poll"
        } finally {
            [RazerBatteryTray.DeviceManager]::SavePriorityList($prioBackup)
        }
    } finally { $form.Dispose() }

    # C) Per-Monitor V2 DPI Awareness upgrade in NativeMethods & Program.cs
    $tNative = $asm.GetType("RazerBatteryTray.NativeMethods", $true)
    $mEnableDpi = $tNative.GetMethod("EnableBestDpiAwareness", $BF)
    Assert-True ($null -ne $mEnableDpi) "NativeMethods.EnableBestDpiAwareness exists"
    $progSrc = Get-Content (Join-Path $root "src\Core\Program.cs") -Raw -Encoding UTF8
    Assert-True ($progSrc.Contains("EnableBestDpiAwareness()")) "Program.cs invokes EnableBestDpiAwareness() for Per-Monitor V2 crisp rendering"
}

Invoke-TestCase "7. 离线鼠标库水合缓存档位/回报率、隐藏空行齿轮并消除 0001-01-01" {
    $mCreateOff = $tDevMgr.GetMethod("CreateOfflineDeviceInfo", $BF)
    Assert-True ($null -ne $mCreateOff) "CreateOfflineDeviceInfo exists"

    # Seed DriverStateStore for offline Razer Viper V3 Pro SE (0x00DE) (avoid colliding with host HardwareCache on 00BE)
    $tState = $asm.GetType("RazerBatteryTray.DriverStateStore", $true)
    $mSaveDpi = $tState.GetMethod("SaveDpiState", $BF)
    $mSaveBat = $tState.GetMethod("SaveBatteryState", $BF)
    $mClrSt = $tState.GetMethod("ClearDeviceState", $BF)
    $testId = "Razer:1532:00DE"
    try {
        $mSaveBat.Invoke($null, @($testId, [int]77, $false, "Razer Viper V3 Pro SE")) | Out-Null
        $mSaveDpi.Invoke($null, @($testId, [int]1600, [int]1600, [int]2, [int]2000)) | Out-Null

        $offDev = $mCreateOff.Invoke($null, @($testId, $null))
        Assert-True ($null -ne $offDev) "Offline Razer mouse created"
        Assert-True ($null -ne $offDev.SupportedPollingRates -and $offDev.SupportedPollingRates.Length -ge 4) "Offline Razer mouse populates SupportedPollingRates from model table"
        Assert-Equal 77 $offDev.BatteryPercent "Offline mouse hydrates cached BatteryPercent"
        Assert-Equal 1600 $offDev.Dpi "Offline mouse hydrates cached Dpi from DriverStateStore/HardwareCache"
        Assert-Equal 2000 $offDev.PollingRate "Offline mouse hydrates cached PollingRate"

        $form = New-SandboxMainForm $false
        try {
            $null = ([System.Windows.Forms.Form]$form).Handle
            $getState = [System.Windows.Forms.Control].GetMethod("GetState", [System.Reflection.BindingFlags]"Instance,NonPublic")

            # Hydrated offline mouse must render Polling Rate & DPI buttons (not disappear!)
            $tMain.GetField("selectedDevice", $BF).SetValue($form, $offDev)
            $tMain.GetMethod("UpdateUI", $BF).Invoke($form, @($offDev, $false)) | Out-Null
            $rateBtns = $tMain.GetField("btnRates", $BF).GetValue($form)
            $dpiBtns = $tMain.GetField("btnDpiStages", $BF).GetValue($form)
            Assert-True ([bool]$getState.Invoke($rateBtns[0], @(2))) "Offline hydrated mouse renders Polling Rate buttons"
            Assert-True ([bool]$getState.Invoke($dpiBtns[0], @(2))) "Offline hydrated mouse renders DPI buttons"

            # LiveRateBadge must say 离线记忆, not 硬件生效, when offline
            $tMain.GetField("autoGameRateEnabled", $BF).SetValue($form, $true)
            $tMain.GetMethod("UpdatePollingRateDisplayStates", $BF).Invoke($form, $null) | Out-Null
            $badgeLbl = $tMain.GetField("lblLiveRateBadge", $BF).GetValue($form)
            Assert-True ($badgeLbl.Text -notmatch "硬件生效") ("Offline badge must not claim 硬件生效: " + $badgeLbl.Text)

            # Uncached DateTime.MinValue offline mouse must NOT render 0001-01-01 or empty orphaned gear row
            $uncached = [Activator]::CreateInstance($tInfo)
            $uncached.DeviceId = "Generic:TEST:UNCACHED"; $uncached.Brand = "Generic"; $uncached.DeviceName = "Uncached Mouse"
            $uncached.Category = [Enum]::Parse($tCat, "Mouse"); $uncached.IsConnected = $false
            $tMain.GetField("selectedDevice", $BF).SetValue($form, $uncached)
            $tMain.GetMethod("UpdateUI", $BF).Invoke($form, @($uncached, $false)) | Out-Null
            $lblUpd = $tMain.GetField("lblUpdateTime", $BF).GetValue($form)
            Assert-True ($lblUpd.Text -notmatch "0001-01-01") ("Must not display 0001-01-01: " + $lblUpd.Text)
            $btnDpiMgr = $tMain.GetField("btnDpiManage", $BF).GetValue($form)
            Assert-True (-not [bool]$getState.Invoke($btnDpiMgr, @(2))) "Uncached offline mouse hides empty DPI gear row instead of leaving blank hole"
        } finally { $form.Dispose() }
    } finally {
        $mClrSt.Invoke($null, @($testId)) | Out-Null
    }
}

Invoke-TestCase "8. 有线无电池鼠标托盘绘制、供电中摘要与右键菜单展开防闪烁守卫" {
    # A) Tray bitmap rendering for wired battery-less connected device (percent=-1, isConnected=true) across 3 styles x 16/24px
    $mDraw = $tMain.GetMethod("DrawTrayBitmap", [System.Reflection.BindingFlags]"NonPublic,Static")
    foreach ($style in @(0, 1, 2)) {
        foreach ($size in @(16, 24)) {
            $bmp = $mDraw.Invoke($null, @([int]-1, [bool]$false, [bool]$true, [int]$style, [int]$size, [bool]$false, $null))
            $hasColoredPx = $false
            for ($y = 0; $y -lt $bmp.Height -and -not $hasColoredPx; $y++) {
                for ($x = 0; $x -lt $bmp.Width; $x++) {
                    if ($bmp.GetPixel($x, $y).A -gt 0) { $hasColoredPx = $true; break }
                }
            }
            $bmp.Dispose()
            Assert-True $hasColoredPx "Wired no-battery icon visible style=$style size=$size"
        }
    }

    # B) BuildTrayBatterySummary reports "供电中" (never "-1%") for wired battery-less mouse
    $listType = [System.Collections.Generic.List`1].MakeGenericType(@($tInfo))
    $devs = [Activator]::CreateInstance($listType)
    $wm = [Activator]::CreateInstance($tInfo)
    $wm.DeviceId = "Razer:1532:008C"; $wm.Brand = "Razer"; $wm.DeviceName = "Razer DeathAdder V2 Mini"
    $wm.Category = [Enum]::Parse($tCat, "Mouse"); $wm.IsConnected = $true; $wm.IsSleeping = $false
    $wm.IsDonglePresent = $false; $wm.Transport = "USB 有线"; $wm.BatteryPercent = -1
    $devs.Add($wm)
    $sum = [RazerBatteryTray.DeviceManager]::BuildTrayBatterySummary($devs)
    Assert-True ($sum -match "供电中" -and $sum -notmatch "-1%") ("Wired mouse tray summary: " + $sum)

    # C) UpdateTrayDeviceMenuItems defers rebuild while contextMenu is visible (prevents flicker)
    $traySrc = Get-Content (Join-Path $root "src\UI\Forms\MainForm.Tray.cs") -Raw -Encoding UTF8
    Assert-True ($traySrc.Contains("pendingTrayMenuRefresh") -and $traySrc.Contains("if (contextMenu.Visible)")) "Tray menu rebuild defer guard present"
}

Invoke-TestCase "9. 8K WM_INPUT 零分配热路径缓存与启动同回报率免写保护" {
    $mShouldCal = $tMain.GetMethod("ShouldCalibrateStartupPollingRate", [System.Reflection.BindingFlags]"Public,Static")
    Assert-True ($null -ne $mShouldCal) "ShouldCalibrateStartupPollingRate exists"
    Assert-True (-not [bool]$mShouldCal.Invoke($null, @([int]1000, [int]1000))) "Skip startup write when hardware already matches desktopPollingRate"
    Assert-True ([bool]$mShouldCal.Invoke($null, @([int]4000, [int]1000))) "Calibrate when hardware differs from desktopPollingRate"
    Assert-True ([bool]$mShouldCal.Invoke($null, @([int]0, [int]1000))) "Calibrate when hardware polling rate unknown"
    Assert-True (-not [bool]$mShouldCal.Invoke($null, @([int]1000, [int]0))) "Skip when target rate unset"

    $form = New-SandboxMainForm $false
    try {
        $null = ([System.Windows.Forms.Form]$form).Handle
        $v3 = [Activator]::CreateInstance($tInfo)
        $v3.DeviceId = "Razer:1532:00C0"; $v3.Brand = "Razer"; $v3.DeviceName = "Razer Viper V3 Pro"
        $v3.HardwareId = "HID:1532:00C1"; $v3.Category = [Enum]::Parse($tCat, "Mouse")
        $v3.IsConnected = $true; $v3.IsSleeping = $false; $v3.IsDonglePresent = $true; $v3.BatteryPercent = 85

        $activeList = $tMain.GetField("activeDevices", $BF).GetValue($form)
        $activeList.Clear(); $activeList.Add($v3)
        $tMain.GetField("selectedDevice", $BF).SetValue($form, $v3)
        $tMain.GetField("lastInfo", $BF).SetValue($form, $v3)
        $tMain.GetField("isMouseSleeping", $BF).SetValue($form, $false)
        $tMain.GetMethod("InvalidateMouseWakeCache", $BF).Invoke($form, $null) | Out-Null

        $mHandleRaw = $tMain.GetMethod("TryHandleCachedMouseRawDevice", $BF)
        Assert-True ($null -ne $mHandleRaw) "TryHandleCachedMouseRawDevice exists"

        $hDev = [IntPtr]0x1234
        $resolverOk = [Func[string]] { return "\\?\HID#VID_1532&PID_00C1&MI_00#7&1234#0000#{378de44c-56ef-11d1-bc8c-00a0c91405dd}" }
        $resolverFail = [Func[string]] { throw "Must NOT query GetRawInputDeviceInfoW once hDev is cached!" }

        $args1 = [object[]]@($hDev, $resolverOk, [bool]$false, [bool]$false)
        $matched1 = [bool]$mHandleRaw.Invoke($form, $args1)
        Assert-True ($matched1 -and $args1[2] -and (-not $args1[3])) "1st WM_INPUT resolves path once & skips wake processing when awake"

        for ($i = 0; $i -lt 10; $i++) {
            $argsN = [object[]]@($hDev, $resolverFail, [bool]$false, [bool]$false)
            $matchedN = [bool]$mHandleRaw.Invoke($form, $argsN)
            Assert-True ($matchedN -and (-not $argsN[2]) -and (-not $argsN[3])) "8K hot-path iteration $i hits cache with zero path query and zero wake alloc"
        }

        $v3.IsSleeping = $true
        $argsWake = [object[]]@($hDev, $resolverFail, [bool]$false, [bool]$false)
        $matchedWake = [bool]$mHandleRaw.Invoke($form, $argsWake)
        Assert-True ($matchedWake -and (-not $argsWake[2]) -and $argsWake[3]) "Sleeping mouse still triggers wake from cached path without re-querying kernel"
        Assert-True (-not $v3.IsSleeping) "Sleeping mouse woken immediately via cached WM_INPUT path"

        # When ANOTHER unrelated mouse is offline, 10 rapid WM_INPUT events on awake Razer V3 Pro must throttle wake checks
        $otherMouse = [Activator]::CreateInstance($tInfo)
        $otherMouse.DeviceId = "Logitech:046D:C547"; $otherMouse.Brand = "Logitech"; $otherMouse.DeviceName = "Offline Logi"
        $otherMouse.Category = [Enum]::Parse($tCat, "Mouse"); $otherMouse.IsConnected = $false
        $activeList.Add($otherMouse)
        $wakeRuns = 0
        for ($i = 0; $i -lt 10; $i++) {
            $argsThrot = [object[]]@($hDev, $resolverFail, [bool]$false, [bool]$false)
            [void]$mHandleRaw.Invoke($form, $argsThrot)
            if ($argsThrot[3]) { $wakeRuns++ }
        }
        Assert-True ($wakeRuns -le 1) ("Awake 8K mouse must throttle wake scan when unrelated mouse is offline, got " + $wakeRuns)
    } finally { $form.Dispose() }
}

Write-Host "`nALL TRAY AND UI CORE TESTS PASSED." -ForegroundColor Green
exit 0
