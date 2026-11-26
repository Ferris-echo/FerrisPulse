# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# Consolidated Multi-Tray Icons & Dual-Color Personalization Test Suite
# Pure in-memory reflection + sandbox mode; zero live hardware I/O or production registry leaks.

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
. (Join-Path $PSScriptRoot "..\tools\TestHarness.ps1")

$asm = Load-FerrisPulseAssembly
$BF = [System.Reflection.BindingFlags]"Public,NonPublic,Static,Instance"

Invoke-TestCase "Task1.ConfigDefaultsAndDualColorPersistence" {
    $tCfg = $asm.GetType("RazerBatteryTray.ConfigManager", $true)
    $tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo", $true)
    $tDm = $asm.GetType("RazerBatteryTray.DeviceManager", $true)

    # 1. Config defaults and clamp (note TestHarness backs up & restores HKCU\Software\FerrisPulse;
    # clear user's active preference inside sandbox before checking clean upgrade defaults)
    $regPath = "HKCU:\Software\FerrisPulse"
    if (Test-Path $regPath) {
        Remove-ItemProperty -Path $regPath -Name "MultiTrayEnabled" -ErrorAction SilentlyContinue
        Remove-ItemProperty -Path $regPath -Name "MultiTrayMaxCount" -ErrorAction SilentlyContinue
    }
    Assert-Equal $false ([bool]$tCfg.GetMethod("GetMultiTrayEnabled", $BF).Invoke($null, $null)) "MultiTrayEnabled defaults false"
    $maxDef = [int]$tCfg.GetMethod("GetMultiTrayMaxCount", $BF).Invoke($null, $null)
    Assert-True ($maxDef -ge 2 -and $maxDef -le 16) "MultiTrayMaxCount in 2..16"

    [void]$tCfg.GetMethod("SetMultiTrayEnabled", $BF).Invoke($null, @($true))
    Assert-Equal $true ([bool]$tCfg.GetMethod("GetMultiTrayEnabled", $BF).Invoke($null, $null)) "MultiTrayEnabled roundtrip"
    [void]$tCfg.GetMethod("SetMultiTrayMaxCount", $BF).Invoke($null, @(1))
    Assert-Equal 2 ([int]$tCfg.GetMethod("GetMultiTrayMaxCount", $BF).Invoke($null, $null)) "MultiTrayMaxCount lower clamp 2"
    [void]$tCfg.GetMethod("SetMultiTrayMaxCount", $BF).Invoke($null, @(99))
    Assert-Equal 16 ([int]$tCfg.GetMethod("GetMultiTrayMaxCount", $BF).Invoke($null, $null)) "MultiTrayMaxCount upper clamp 16"

    # 2. MouseBatteryInfo Dual-Color independence & linkage
    $dev = [Activator]::CreateInstance($tInfo)
    $dev.Brand = "Razer"; $dev.DeviceId = "Razer:1532:00BE"; $dev.DeviceName = "Razer DeathAdder V4 Pro"
    Assert-True ($dev.LinkTrayColorToTheme) "LinkTrayColorToTheme defaults true"

    # Linked mode: changing CustomThemeColorHex drives BOTH BrandColor and TrayAccentColor
    $dev.CustomThemeColorHex = "#3399FF"
    $bc = $dev.BrandColor; $tc = $dev.TrayAccentColor
    Assert-True ($bc.R -eq 0x33 -and $bc.G -eq 0x99 -and $bc.B -eq 0xFF) "BrandColor reads CustomThemeColorHex"
    Assert-Equal $bc.ToArgb() $tc.ToArgb() "Linked TrayAccentColor follows BrandColor"

    # Unlinked mode: CustomTrayColorHex is strictly independent
    $dev.LinkTrayColorToTheme = $false
    $dev.CustomTrayColorHex = "#FF5500"
    Assert-Equal $bc.ToArgb() $dev.BrandColor.ToArgb() "BrandColor stays UI color #3399FF"
    Assert-True ($dev.TrayAccentColor.R -eq 0xFF -and $dev.TrayAccentColor.G -eq 0x55 -and $dev.TrayAccentColor.B -eq 0x00) "Unlinked TrayAccentColor uses CustomTrayColorHex"

    # 3. DeviceManager SaveDeviceAppearance / ApplyDeviceAppearances persistence roundtrip
    [RazerBatteryTray.DeviceManager]::SaveDeviceAppearance($dev.PSObject.BaseObject, "我的主力蝰蛇", "#3399FF", "#FF5500", $false)
    $dev2 = [Activator]::CreateInstance($tInfo)
    $dev2.Brand = "Razer"; $dev2.DeviceId = "Razer:1532:00BF"; $dev2.DeviceName = "Razer DeathAdder V4 Pro"
    $list = New-Object 'System.Collections.Generic.List[RazerBatteryTray.MouseBatteryInfo]'
    $list.Add($dev2)
    [void]$tDm.GetMethod("ApplyDeviceAppearances", $BF).Invoke($null, [object[]]@(,$list.PSObject.BaseObject))
    Assert-Equal "我的主力蝰蛇" $dev2.CustomName "Appearance CustomName applied across PID variant"
    Assert-Equal "#3399FF" $dev2.CustomThemeColorHex "Appearance ThemeColorHex preserved"
    Assert-Equal "#FF5500" $dev2.CustomTrayColorHex "Appearance TrayColorHex preserved"
    Assert-Equal $false $dev2.LinkTrayColorToTheme "Appearance LinkTrayColorToTheme=false preserved"

    # Cleanup test appearance subkey
    $tDm.GetMethod("ClearDeviceAppearance", $BF).Invoke($null, @("Razer:1532:00BE")) | Out-Null
}

Invoke-TestCase "Task2.TraySlotPlannerArbitrationAndFallback" {
    $tPlanner = $asm.GetType("RazerBatteryTray.TraySlotPlanner", $true)
    $tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo", $true)
    $tCat = $asm.GetType("RazerBatteryTray.DeviceCategory", $true)

    function New-Dev([string]$id, [string]$name, [string]$brand, [string]$cat, [int]$batt, [bool]$conn, [bool]$sleep, [bool]$dongle, [bool]$chg) {
        $d = [Activator]::CreateInstance($tInfo)
        $d.DeviceId = $id; $d.DeviceName = $name; $d.Brand = $brand
        $d.Category = [Enum]::Parse($tCat, $cat)
        $d.BatteryPercent = $batt; $d.IsConnected = $conn; $d.IsSleeping = $sleep
        $d.IsDonglePresent = $dongle; $d.IsCharging = $chg
        return $d
    }

    $m1 = New-Dev "Razer:1532:00BE" "DeathAdder V4" "Razer" "Mouse" 80 $true $false $true $false
    $k1 = New-Dev "NuPhy:19F5:3245" "NuPhy Air75" "NuPhy" "Keyboard" 65 $true $true $true $false   # Sleeping but dongle present -> retains slot with disconnected '--' visual!
    $h1 = New-Dev "Sony:054C:0DF2" "INZONE H5" "Sony" "Headset" 18 $true $false $true $false     # Low battery <=30% -> promoted!
    $m2 = New-Dev "Logitech:046D:C54D:1" "GPW 5" "Logitech" "Mouse" 15 $true $false $true $true  # Charging -> NOT promoted as low battery alarm
    $hid = New-Dev "Hidden:1234:5678" "Hidden" "Generic" "Mouse" 10 $true $false $true $false    # Hidden -> excluded!
    $off = New-Dev "Offline:9999:0001" "Removed" "Generic" "Mouse" 50 $false $false $false $false # Removed (not connected & no dongle) -> excluded!

    $devs = New-Object 'System.Collections.Generic.List[RazerBatteryTray.MouseBatteryInfo]'
    $devs.Add($m1); $devs.Add($k1); $devs.Add($h1); $devs.Add($m2); $devs.Add($hid); $devs.Add($off)

    $prio = New-Object 'System.Collections.Generic.List[string]'
    $prio.Add("Razer:1532:00BE"); $prio.Add("NuPhy:19F5:3245"); $prio.Add("Logitech:046D:C54D:1"); $prio.Add("Sony:054C:0DF2")

    $hiddenFn = [Func[string, bool]] { param([string]$id) return ($id -eq "Hidden:1234:5678") }

    # LowBatteryFirst = true, maxSlots = 3 -> Slot 0 = INZONE H5 (18%), Slot 1 = DeathAdder V4, Slot 2 = NuPhy Air75 (sleeping -> visual disconnected)
    $plan = $tPlanner.GetMethod("PlanSlots", $BF).Invoke($null, @($devs.PSObject.BaseObject, $prio.PSObject.BaseObject, $true, 3, $hiddenFn))
    Assert-Equal 3 $plan.Count "Returns up to 3 slots"
    Assert-Equal "Sony:054C:0DF2" $plan[0].DeviceId "Low battery 18% non-charging headset wins Slot 0"
    Assert-Equal "Razer:1532:00BE" $plan[1].DeviceId "Priority 1 mouse takes Slot 1"
    Assert-Equal "NuPhy:19F5:3245" $plan[2].DeviceId "Sleeping keyboard keeps Slot 2 in-place"
    Assert-Equal $true $plan[2].VisualConnected "Sleeping device with cached battery renders amber cached digits (not '--')"
    Assert-Equal $true $plan[2].IsSleeping "Sleeping device sets IsSleeping = true for warm amber sleep color"
    Assert-Equal 65 $plan[2].Percent "Sleeping device keeps cached 65% battery"
    Assert-Equal $false $plan[0].IsFallback "Non-fallback"

    # Truly offline dongle-present device (IsConnected = false) still renders disconnected '--'
    $dongleOnly = New-Dev "DongleOnly:1111:2222" "Dongle Mouse" "Generic" "Mouse" -1 $false $false $true $false
    $oneList = New-Object 'System.Collections.Generic.List[RazerBatteryTray.MouseBatteryInfo]'
    $oneList.Add($dongleOnly)
    $donglePlan = $tPlanner.GetMethod("PlanSlots", $BF).Invoke($null, @($oneList.PSObject.BaseObject, $prio.PSObject.BaseObject, $true, 3, $hiddenFn))
    Assert-Equal 1 $donglePlan.Count "Dongle-only offline device keeps 1 slot"
    Assert-Equal $false $donglePlan[0].VisualConnected "Offline dongle-only device renders disconnected '--'"
    Assert-Equal $false $donglePlan[0].IsSleeping "Offline dongle-only device IsSleeping = false"

    # Zero eligible devices -> 1 fallback disconnected icon at Slot 0
    $emptyList = New-Object 'System.Collections.Generic.List[RazerBatteryTray.MouseBatteryInfo]'
    $fallback = $tPlanner.GetMethod("PlanSlots", $BF).Invoke($null, @($emptyList.PSObject.BaseObject, $prio.PSObject.BaseObject, $true, 3, $hiddenFn))
    Assert-Equal 1 $fallback.Count "Empty device list returns 1 fallback slot"
    Assert-True ($fallback[0].IsFallback -and -not $fallback[0].VisualConnected -and $fallback[0].SlotIndex -eq 0) "Fallback Slot 0 state"
}

Invoke-TestCase "Task3.MultiTrayIconHostGuidsTrimmingAndRouting" {
    $tHost = $asm.GetType("RazerBatteryTray.MultiTrayIconHost", $true)
    $tPlanItem = $asm.GetType("RazerBatteryTray.TraySlotPlanItem", $true)

    # 1. Deterministic unique GUIDs per slot (0..15)
    $guids = @{}
    $mGuid = $tHost.GetMethod("GetSlotGuid", $BF)
    for ($i = 0; $i -lt 16; $i++) {
        $g = $mGuid.Invoke($null, @($i)).ToString()
        Assert-True (-not $guids.ContainsKey($g)) "Unique slot GUID for $i"
        $guids[$g] = $true
    }
    Assert-Equal $guids.Keys.Count 16 "16 unique GUIDs"
    Assert-Equal ($mGuid.Invoke($null, @(2)).ToString()) ($mGuid.Invoke($null, @(2)).ToString()) "Deterministic GUID across calls"

    # 2. Host sync & slot trimming & click/context callback routing in sandbox
    $hostObj = [Activator]::CreateInstance($tHost)
    try {
        $clicked = New-Object 'System.Collections.Generic.List[string]'
        $rClicked = New-Object 'System.Collections.Generic.List[string]'
        $hostObj.add_SlotClicked([Action[int, string]] { param($s, $id) $clicked.Add("$s|$id") })
        $hostObj.add_SlotContextMenuRequested([Action[int, string, System.Drawing.Point]] { param($s, $id, $pt) $rClicked.Add("$s|$id") })

        function New-Plan([int]$slot, [string]$id, [int]$pct, [bool]$conn, [System.Drawing.Color]$col) {
            $it = [Activator]::CreateInstance($tPlanItem)
            $it.SlotIndex = $slot; $it.DeviceId = $id; $it.Percent = $pct
            $it.VisualConnected = $conn; $it.AccentColor = $col; $it.TooltipText = "Slot $slot $id"
            return $it
        }
        $plans = New-Object 'System.Collections.Generic.List[RazerBatteryTray.TraySlotPlanItem]'
        $plans.Add((New-Plan 0 "DevA" 88 $true ([System.Drawing.Color]::LimeGreen)))
        $plans.Add((New-Plan 1 "DevB" 66 $false ([System.Drawing.Color]::Cyan)))
        $iconFn = [Func[RazerBatteryTray.TraySlotPlanItem, System.Drawing.Icon]] { param($p) return [System.Drawing.SystemIcons]::Application }

        $mSync = $tHost.GetMethod("SyncSlots", $BF)
        [void]$mSync.Invoke($hostObj, @($plans.PSObject.BaseObject, $iconFn))
        Assert-Equal 2 $hostObj.ActiveSlotCount "Two active slots created"

        # Simulate L-click on slot 1 and R-click on slot 0
        $mProc = $tHost.GetMethod("HandleSlotMouseMessage", $BF)
        [void]$mProc.Invoke($hostObj, @(1, 0x0202)) # WM_LBUTTONUP
        [void]$mProc.Invoke($hostObj, @(0, 0x0205)) # WM_RBUTTONUP
        Assert-Equal "1|DevB" $clicked[0] "Left click routes to Slot 1 DevB"
        Assert-Equal "0|DevA" $rClicked[0] "Right click routes to Slot 0 DevA"

        # Shrink to 1 slot -> trims Slot 1 cleanly
        $plans.RemoveAt(1)
        [void]$mSync.Invoke($hostObj, @($plans.PSObject.BaseObject, $iconFn))
        Assert-Equal 1 $hostObj.ActiveSlotCount "Excess Slot 1 trimmed"
    } finally {
        $hostObj.Dispose()
    }
}

Invoke-TestCase "Task4.PersonalizeDialogAndPriorityFormAdaptiveUI" {
    # 1. DevicePersonalizeDialog dual-color linkage, unlinking, and reset
    $tDlg = $asm.GetType("RazerBatteryTray.DevicePersonalizeDialog", $true)
    $dlg = [Activator]::CreateInstance($tDlg, @(
        "Razer DeathAdder V4 Pro",
        "我的主力蝰蛇",
        [System.Drawing.Color]::FromArgb(0, 230, 118),
        "#00E676",
        "#00E676",
        $true,
        [single]1.0
    ))
    try {
        $chkLink = $tDlg.GetField("chkLinkTrayColor", $BF).GetValue($dlg)
        Assert-Equal $true $chkLink.Checked "LinkTrayColorToTheme initially checked"

        # Changing theme color while linked updates tray hex too, and tray row stays dimmed while still clickable
        $traySwatches = $tDlg.GetField("traySwatches", $BF).GetValue($dlg)
        Assert-Equal $true $traySwatches[0].IsDimmed "Tray swatches dimmed with gray veil while linked"
        $tDlg.GetMethod("SetThemeColor", $BF).Invoke($dlg, @([System.Drawing.Color]::FromArgb(0, 140, 255))) | Out-Null
        Assert-Equal "#008CFF" $dlg.ResultThemeColorHex "Theme color updated"
        Assert-Equal "#008CFF" $dlg.ResultTrayColorHex "Linked tray color followed theme"

        # Clicking the SAME color as theme keeps linked & dimmed
        $tDlg.GetMethod("SetTrayColor", $BF).Invoke($dlg, @([System.Drawing.Color]::FromArgb(0, 140, 255))) | Out-Null
        Assert-Equal $true $chkLink.Checked "Clicking same color as theme stays linked"
        Assert-Equal $true $traySwatches[0].IsDimmed "Tray swatches stay dimmed when same color clicked"

        # Clicking a DIFFERENT tray color auto-unlinks, lifts gray veil, and leaves theme untouched
        $tDlg.GetMethod("SetTrayColor", $BF).Invoke($dlg, @([System.Drawing.Color]::FromArgb(255, 110, 0))) | Out-Null
        Assert-Equal $false $chkLink.Checked "Manual different tray color edit decouples LinkTrayColorToTheme"
        Assert-Equal $false $traySwatches[0].IsDimmed "Tray swatches un-dimmed once unlinked"
        Assert-Equal "#008CFF" $dlg.ResultThemeColorHex "Theme color stays #008CFF"
        Assert-Equal "#FF6E00" $dlg.ResultTrayColorHex "Independent tray color #FF6E00"

        # Reset restores brand default & linkage
        $tDlg.GetMethod("ResetToBrandDefaults", $BF).Invoke($dlg, $null) | Out-Null
        Assert-Equal "" $dlg.ResultThemeColorHex "Reset clears custom theme hex"
        Assert-Equal "" $dlg.ResultTrayColorHex "Reset clears custom tray hex"
        Assert-Equal $true $dlg.ResultLinkTrayColorToTheme "Reset restores link=true"
    } finally { $dlg.Dispose() }

    # 2. DevicePriorityForm adaptive selector (<=5 hides custom input, >5 exposes manual input)
    $tPrioForm = $asm.GetType("RazerBatteryTray.DevicePriorityForm", $true)
    $tItem = $asm.GetType("RazerBatteryTray.PriorityItem", $true)
    $tListGeneric = [System.Type]::GetType("System.Collections.Generic.List``1").MakeGenericType(@($tItem))
    function New-PrioItems([int]$n) {
        $lst = [Activator]::CreateInstance($tListGeneric)
        for ($i = 1; $i -le $n; $i++) {
            $it = [Activator]::CreateInstance($tItem)
            $it.DeviceId = "Dev:$i"; $it.Name = "Device $i"; $it.OriginalName = "Device $i"
            $it.Battery = 80; $it.IsConnected = $true; $it.AccentColor = [System.Drawing.Color]::LimeGreen
            $it.DefaultBrandColor = [System.Drawing.Color]::LimeGreen; $it.TrayAccentColor = [System.Drawing.Color]::LimeGreen
            $it.PriorityIndex = $i
            $lst.Add($it)
        }
        return , $lst
    }

    $ctor5 = $tPrioForm.GetConstructors() | Where-Object { $_.GetParameters().Count -eq 5 } | Select-Object -First 1
    $list4 = New-PrioItems 4
    $pfSmall = $ctor5.Invoke(@($list4.PSObject.BaseObject, $false, $true, 3, [single]1.0))
    try {
        $segSmall = $tPrioForm.GetField("pnlSegmentTrack", $BF).GetValue($pfSmall)
        $barSmall = $tPrioForm.GetField("pnlTrayCountBar", $BF).GetValue($pfSmall)
        $chkMultiSmall = $tPrioForm.GetField("chkMultiTray", $BF).GetValue($pfSmall)
        Assert-True ($chkMultiSmall.Checked) "Multi-tray checkbox initialized true"
        Assert-Equal 3 $segSmall.OptionCount "Presets for 4 devices should be 2,3,4 (count=3)"
        Assert-True (-not $segSmall.HasCustomSlot) "Custom inline slot stays hidden when devices <= 5"
        Assert-Equal 3 $segSmall.SelectedValue "Preset initial value matches 3"
        Assert-Equal 1 $barSmall.Controls.Count "Unified segmented bar contains only SlidingSegmentTrack"
    } finally { $pfSmall.Dispose() }

    $list7 = New-PrioItems 7
    $pfLarge = $ctor5.Invoke(@($list7.PSObject.BaseObject, $false, $true, 6, [single]1.0))
    try {
        $segLarge = $tPrioForm.GetField("pnlSegmentTrack", $BF).GetValue($pfLarge)
        $barLarge = $tPrioForm.GetField("pnlTrayCountBar", $BF).GetValue($pfLarge)
        Assert-Equal 4 $segLarge.OptionCount "Presets for 7 devices capped at 2..5 (count=4)"
        Assert-True ($segLarge.HasCustomSlot) "Custom inline slot embedded inside SlidingSegmentTrack when devices > 5"
        Assert-Equal 6 $segLarge.SelectedValue "Custom initial value 6 preserved in unified track"
        Assert-Equal 1 $barLarge.Controls.Count "Unified segmented bar embeds custom count without extra NumericUpDown"
        $segLarge.SelectValue(8, $false)
        Assert-Equal 8 $segLarge.SelectedValue "Custom inline slot accepts arbitrary clamped value"

        # Owner-drawn inline editing must never dispose Control.DefaultFont or break AddDeviceWizardForm TextBoxes
        $tSeg = $segLarge.GetType()
        $tSeg.GetMethod("BeginInlineEdit", $BF).Invoke($segLarge, $null) | Out-Null
        Assert-True ($segLarge.IsCustomEditing) "Owner-drawn inline edit activates cleanly"
        $tSeg.GetField("editBuffer", $BF).SetValue($segLarge, "9")
        $segLarge.CommitPendingCustomInput()
        Assert-True (-not $segLarge.IsCustomEditing) "CommitPendingCustomInput closes owner-drawn edit"
        Assert-Equal 9 $segLarge.SelectedValue "Owner-drawn edit commits numeric value 9"
        Assert-True ([System.Windows.Forms.Control]::DefaultFont.Height -gt 0) "Control.DefaultFont remains intact after inline edit"

        $tAddWiz = $asm.GetType("RazerBatteryTray.AddDeviceWizardForm", $true)
        $wiz = [Activator]::CreateInstance($tAddWiz)
        try {
            Assert-True ($null -ne $wiz) "AddDeviceWizardForm opens cleanly after inline custom edit"
        } finally { $wiz.Dispose() }
    } finally { $pfLarge.Dispose() }
}

Invoke-TestCase "Task5.MainFormMultiTrayToggleNameEntryAndSlotScoping" {
    $tMain = $asm.GetType("RazerBatteryTray.MainForm", $true)
    $tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo", $true)
    $tCat = $asm.GetType("RazerBatteryTray.DeviceCategory", $true)
    $tCfg = $asm.GetType("RazerBatteryTray.ConfigManager", $true)
    $tListInfo = [System.Type]::GetType("System.Collections.Generic.List``1").MakeGenericType(@($tInfo))

    # Ensure clean default off state before instantiating MainForm
    $tCfg.GetMethod("SetMultiTrayEnabled").Invoke($null, @($false)) | Out-Null
    $tCfg.GetMethod("SetMultiTrayMaxCount").Invoke($null, @(3)) | Out-Null

    $mf = [Activator]::CreateInstance($tMain, @($true))
    try {
        $fDisableSave = $tMain.GetField("DisableConfigSaveForTesting", $BF)
        if ($fDisableSave -ne $null) { $fDisableSave.SetValue($mf, $true) }

        # Default single-tray mode preserved
        $mEnabledInit = $tMain.GetField("multiTrayEnabled", $BF).GetValue($mf)
        $miMulti = $tMain.GetField("multiTrayMenuItem", $BF).GetValue($mf)
        $trayIcon = $tMain.GetField("trayIcon", $BF).GetValue($mf)
        $hostObj = $tMain.GetField("multiTrayHost", $BF).GetValue($mf)

        Assert-True (-not $mEnabledInit) "MainForm defaults to multiTrayEnabled = false"
        Assert-True ($miMulti -ne $null -and (-not $miMulti.Checked)) "multiTrayMenuItem exists and defaults unchecked"
        Assert-True ($hostObj -ne $null) "multiTrayHost initialized"

        # Seed 2 active connected mice with distinct DPI stages, rates, and tray colors
        $dev1 = [Activator]::CreateInstance($tInfo)
        $dev1.DeviceId = "Test:M1"; $dev1.DeviceName = "Test Mouse 1"; $dev1.Brand = "Razer"
        $dev1.Category = [Enum]::Parse($tCat, "Mouse"); $dev1.BatteryPercent = 85
        $dev1.IsConnected = $true; $dev1.IsSleeping = $false
        $dev1.CustomTrayColorHex = "#33AAFF"; $dev1.LinkTrayColorToTheme = $false
        $dev1.Dpi = 800; $dev1.DpiStage = 1; $dev1.DpiStages = [int[]]@(800, 1600)
        $dev1.PollingRate = 1000; $dev1.SupportedPollingRates = [int[]]@(1000, 2000)

        $dev2 = [Activator]::CreateInstance($tInfo)
        $dev2.DeviceId = "Test:M2"; $dev2.DeviceName = "Test Mouse 2"; $dev2.Brand = "Logitech"
        $dev2.Category = [Enum]::Parse($tCat, "Mouse"); $dev2.BatteryPercent = 42
        $dev2.IsConnected = $true; $dev2.IsSleeping = $false
        $dev2.CustomTrayColorHex = "#FF5599"; $dev2.LinkTrayColorToTheme = $false
        $dev2.Dpi = 1600; $dev2.DpiStage = 2; $dev2.DpiStages = [int[]]@(400, 1600, 3200)
        $dev2.PollingRate = 1000; $dev2.SupportedPollingRates = [int[]]@(500, 1000, 4000)

        $devs = [Activator]::CreateInstance($tListInfo)
        $devs.Add($dev1); $devs.Add($dev2)
        $tMain.GetField("activeDevices", $BF).SetValue($mf, $devs)
        $tMain.GetField("lastInfo", $BF).SetValue($mf, $dev1)
        $tMain.GetField("selectedDevice", $BF).SetValue($mf, $dev1)

        # Toggle ON via SetMultiTrayEnabled
        $mSetMulti = $tMain.GetMethod("SetMultiTrayEnabled", $BF)
        $mSetMulti.Invoke($mf, @($true, $true)) | Out-Null

        Assert-True ($tMain.GetField("multiTrayEnabled", $BF).GetValue($mf) -eq $true) "multiTrayEnabled becomes true"
        Assert-True ($miMulti.Checked -eq $true) "multiTrayMenuItem checked when enabled"
        Assert-True ($tCfg.GetMethod("GetMultiTrayEnabled").Invoke($null, @()) -eq $true) "ConfigManager persisted true"
        Assert-True (-not $trayIcon.Visible) "Single trayIcon hidden when multi-tray active"
        Assert-True ($hostObj.ActiveSlotCount -eq 2) ("2 active slots rendered (got " + $hostObj.ActiveSlotCount + ")")

        # Scope right-click context menu to dev2 (non-primary)
        $mPrep = $tMain.GetMethod("PrepareContextMenuForDeviceId", $BF)
        $mPrep.Invoke($mf, @("Test:M2")) | Out-Null
        $resolved = $tMain.GetMethod("ResolveTrayContextDevice", $BF).Invoke($mf, @())
        Assert-True ($resolved -ne $null -and $resolved.DeviceId -eq "Test:M2") "Context device resolves to Test:M2"

        $dpiMenu = $tMain.GetField("dpiMenu", $BF).GetValue($mf)
        $rateMenu = $tMain.GetField("rateMenu", $BF).GetValue($mf)
        Assert-True ($dpiMenu.DropDownItems.Count -eq 3) ("Scoped DPI stages belong to dev2 (expected 3, got " + $dpiMenu.DropDownItems.Count + ")")
        Assert-True ($rateMenu.DropDownItems.Count -eq 3) ("Scoped rates belong to dev2 (expected 3, got " + $rateMenu.DropDownItems.Count + ")")

        # Toggle OFF restores single-tray state and clears multi-tray slots
        $mSetMulti.Invoke($mf, @($false, $true)) | Out-Null
        Assert-True (-not $tMain.GetField("multiTrayEnabled", $BF).GetValue($mf)) "multiTrayEnabled becomes false"
        Assert-True (-not $miMulti.Checked) "multiTrayMenuItem unchecked when disabled"
        Assert-True ($hostObj.ActiveSlotCount -eq 0) "multiTrayHost hides all slots when disabled"

        # Personalization entry exists on MainForm (clicking lblDeviceName opens it)
        $mOpenPers = $tMain.GetMethod("OpenPersonalizeDialogForCurrentDevice", $BF)
        Assert-True ($mOpenPers -ne $null) "OpenPersonalizeDialogForCurrentDevice exists on MainForm"
    } finally {
        try { $mf.Dispose() } catch {}
    }
}

Invoke-TestCase "Task6.LowBatteryGranularAlertControlsAndSleepAmberTray" {
    $tCfg = $asm.GetType("RazerBatteryTray.ConfigManager", $true)
    $tMain = $asm.GetType("RazerBatteryTray.MainForm", $true)
    $tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo", $true)
    $tCat = $asm.GetType("RazerBatteryTray.DeviceCategory", $true)

    # 1. ConfigManager defaults: Notify=true, UiColor=true, TrayColor=false
    $regPath = "HKCU:\Software\FerrisPulse"
    if (Test-Path $regPath) {
        Remove-ItemProperty -Path $regPath -Name "LowBatteryNotifyEnabled" -ErrorAction SilentlyContinue
        Remove-ItemProperty -Path $regPath -Name "LowBatteryUiColorEnabled" -ErrorAction SilentlyContinue
        Remove-ItemProperty -Path $regPath -Name "LowBatteryTrayColorEnabled" -ErrorAction SilentlyContinue
    }
    Assert-Equal $true ([bool]$tCfg.GetMethod("GetLowBatteryNotifyEnabled", $BF).Invoke($null, $null)) "LowBatteryNotifyEnabled defaults true"
    Assert-Equal $true ([bool]$tCfg.GetMethod("GetLowBatteryUiColorEnabled", $BF).Invoke($null, $null)) "LowBatteryUiColorEnabled defaults true"
    Assert-Equal $false ([bool]$tCfg.GetMethod("GetLowBatteryTrayColorEnabled", $BF).Invoke($null, $null)) "LowBatteryTrayColorEnabled defaults false"

    # 2. DrawTrayBitmapWithOptions pixel check: disabled keeps brand color at 15% and 8%; enabled turns yellow/red; sleep turns warm amber
    $mDrawOpt = $tMain.GetMethod("DrawTrayBitmapWithOptions", $BF)
    Assert-True ($null -ne $mDrawOpt) "DrawTrayBitmapWithOptions exists"
    $brandCol = [System.Drawing.Color]::FromArgb(0, 229, 255) # Cyan
    $yellowCol = [System.Drawing.Color]::FromArgb(255, 214, 0)
    $redCol = [System.Drawing.Color]::FromArgb(255, 50, 65)
    $amberCol = [System.Drawing.Color]::FromArgb(255, 183, 77)

    function Test-BitmapHasColor($bmp, $targetColor) {
        for ($y = 0; $y -lt $bmp.Height; $y++) {
            for ($x = 0; $x -lt $bmp.Width; $x++) {
                $px = $bmp.GetPixel($x, $y)
                if ($px.A -gt 200 -and $px.R -eq $targetColor.R -and $px.G -eq $targetColor.G -and $px.B -eq $targetColor.B) { return $true }
            }
        }
        return $false
    }

    foreach ($pct in @(15, 8)) {
        $bKeep = $mDrawOpt.Invoke($null, @([int]$pct, $false, $true, [int]2, [int]16, $false, $brandCol, $false))
        try {
            Assert-True (Test-BitmapHasColor $bKeep $brandCol) "Tray at ${pct}% with low-battery color OFF keeps brand color"
            Assert-True (-not (Test-BitmapHasColor $bKeep $yellowCol)) "Tray at ${pct}% with low-battery color OFF has no yellow"
            Assert-True (-not (Test-BitmapHasColor $bKeep $redCol)) "Tray at ${pct}% with low-battery color OFF has no red"
        } finally { $bKeep.Dispose() }
    }

    $bWarn15 = $mDrawOpt.Invoke($null, @([int]15, $false, $true, [int]2, [int]16, $false, $brandCol, $true))
    try { Assert-True (Test-BitmapHasColor $bWarn15 $yellowCol) "Tray at 15% with low-battery color ON turns yellow" } finally { $bWarn15.Dispose() }

    $bCrit8 = $mDrawOpt.Invoke($null, @([int]8, $false, $true, [int]2, [int]16, $false, $brandCol, $true))
    try { Assert-True (Test-BitmapHasColor $bCrit8 $redCol) "Tray at 8% with low-battery color ON turns red" } finally { $bCrit8.Dispose() }

    $bSleep = $mDrawOpt.Invoke($null, @([int]65, $false, $true, [int]2, [int]16, $true, $brandCol, $false))
    try { Assert-True (Test-BitmapHasColor $bSleep $amberCol) "Sleeping tray with cached battery renders warm amber digits" } finally { $bSleep.Dispose() }

    # 3. LowBatteryAlertSettingsDialog contract
    $tDlg = $asm.GetType("RazerBatteryTray.LowBatteryAlertSettingsDialog", $true)
    $dlg = [Activator]::CreateInstance($tDlg, @($true, $true, $false, [single]1.0))
    try {
        Assert-Equal $true $dlg.ResultNotifyEnabled "Dialog initial Notify=true"
        Assert-Equal $true $dlg.ResultUiColorEnabled "Dialog initial UiColor=true"
        Assert-Equal $false $dlg.ResultTrayColorEnabled "Dialog initial TrayColor=false"
    } finally { $dlg.Dispose() }

    # 4. MainForm chkLowAlert link mode, master/sub sync, and UI low-battery color gating (<=20% & <=10%)
    $mf = New-SandboxMainForm $false
    try {
        $chkLow = $tMain.GetField("chkLowAlert", $BF).GetValue($mf)
        Assert-True ($chkLow.TextActsAsActionLink) "chkLowAlert text acts as clickable action link"

        $mApply = $tMain.GetMethod("ApplyLowBatteryAlertOptions", $BF)
        Assert-True ($null -ne $mApply) "MainForm.ApplyLowBatteryAlertOptions exists"

        # All 3 off -> master switch auto turns off
        $mApply.Invoke($mf, @($false, $false, $false)) | Out-Null
        Assert-Equal $false ([bool]$tMain.GetField("lowBatteryAlertEnabled", $BF).GetValue($mf)) "Master auto-disabled when all 3 sub-options off"
        Assert-Equal $false $chkLow.Checked "chkLowAlert unchecked when all 3 off"

        # Turn on Notify only (UI Color OFF) -> master auto turns on, UI stays brand color at 15% and 8%
        $mApply.Invoke($mf, @($true, $false, $false)) | Out-Null
        Assert-Equal $true ([bool]$tMain.GetField("lowBatteryAlertEnabled", $BF).GetValue($mf)) "Master auto-enabled when any sub-option on"
        Assert-Equal $true $chkLow.Checked "chkLowAlert checked"

        $mouse = [Activator]::CreateInstance($tInfo)
        $mouse.DeviceId = "Test:LowBatMouse"; $mouse.Brand = "Razer"; $mouse.DeviceName = "Test Mouse"
        $mouse.Category = [Enum]::Parse($tCat, "Mouse"); $mouse.IsConnected = $true; $mouse.IsSleeping = $false
        $mouse.IsCharging = $false; $mouse.CustomThemeColorHex = "#00E5FF"; $mouse.BatteryPercent = 8
        $tMain.GetField("selectedDevice", $BF).SetValue($mf, $mouse)

        $lblBig = $tMain.GetField("lblBatteryBig", $BF).GetValue($mf)
        $tMain.GetMethod("UpdateUI", $BF).Invoke($mf, @($mouse, $false)) | Out-Null
        Assert-Equal $mouse.BrandColor.ToArgb() $lblBig.ForeColor.ToArgb() "UI color OFF keeps brand color even at 8%"

        # Turn UI Color ON -> 8% turns red (255,45,85) and 15% turns yellow (255,214,0)
        $mApply.Invoke($mf, @($true, $true, $false)) | Out-Null
        $tMain.GetMethod("UpdateUI", $BF).Invoke($mf, @($mouse, $false)) | Out-Null
        Assert-Equal ([System.Drawing.Color]::FromArgb(255, 45, 85).ToArgb()) $lblBig.ForeColor.ToArgb() "UI color ON turns red at 8%"
        $mouse.BatteryPercent = 15
        $tMain.GetMethod("UpdateUI", $BF).Invoke($mf, @($mouse, $false)) | Out-Null
        Assert-Equal ([System.Drawing.Color]::FromArgb(255, 214, 0).ToArgb()) $lblBig.ForeColor.ToArgb() "UI color ON turns yellow at 15%"
    } finally { $mf.Dispose() }
}

Invoke-TestCase "Task7.MultiTrayStyleSyncAndPriorityBarAnimation" {
    # 1. BuildVisualSignature detects TrayStyle, AllowLowBatteryTrayColor, and IsSleeping changes
    $tHost = $asm.GetType("RazerBatteryTray.MultiTrayIconHost", $true)
    $tPlanItem = $asm.GetType("RazerBatteryTray.TraySlotPlanItem", $true)
    $mSig = $tHost.GetMethod("BuildVisualSignature", $BF)
    $p = [Activator]::CreateInstance($tPlanItem)
    $p.SlotIndex = 0; $p.DeviceId = "Dev1"; $p.Percent = 85; $p.VisualConnected = $true
    $p.AccentColor = [System.Drawing.Color]::LimeGreen; $p.TrayStyle = 0; $p.AllowLowBatteryTrayColor = $false
    $sig0 = [string]$mSig.Invoke($null, @($p))
    $p.TrayStyle = 2
    $sigStyle2 = [string]$mSig.Invoke($null, @($p))
    Assert-True ($sig0 -ne $sigStyle2) "TrayStyle change invalidates MultiTray visual signature"
    $p.AllowLowBatteryTrayColor = $true
    $sigLow = [string]$mSig.Invoke($null, @($p))
    Assert-True ($sigStyle2 -ne $sigLow) "AllowLowBatteryTrayColor change invalidates MultiTray visual signature"
    $p.IsSleeping = $true
    $sigSleep = [string]$mSig.Invoke($null, @($p))
    Assert-True ($sigLow -ne $sigSleep) "IsSleeping change invalidates MultiTray visual signature"

    # 2. MainForm.SetTrayStyle refreshes active MultiTray slots immediately
    $tMain = $asm.GetType("RazerBatteryTray.MainForm", $true)
    $tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo", $true)
    $tCat = $asm.GetType("RazerBatteryTray.DeviceCategory", $true)
    $tListInfo = [System.Type]::GetType("System.Collections.Generic.List``1").MakeGenericType(@($tInfo))
    $mf = New-SandboxMainForm $false
    try {
        $d1 = [Activator]::CreateInstance($tInfo)
        $d1.DeviceId = "Test:MultiStyle"; $d1.DeviceName = "Mouse"; $d1.Brand = "Razer"
        $d1.Category = [Enum]::Parse($tCat, "Mouse"); $d1.BatteryPercent = 72; $d1.IsConnected = $true
        $devs = [Activator]::CreateInstance($tListInfo); $devs.Add($d1)
        $tMain.GetField("activeDevices", $BF).SetValue($mf, $devs)
        $tMain.GetField("lastInfo", $BF).SetValue($mf, $d1)
        $tMain.GetField("selectedDevice", $BF).SetValue($mf, $d1)

        $tMain.GetMethod("SetMultiTrayEnabled", $BF).Invoke($mf, @($true, $false)) | Out-Null
        $tMain.GetMethod("SetTrayStyle", $BF).Invoke($mf, @([int]2, $false)) | Out-Null
        $hostObj = $tMain.GetField("multiTrayHost", $BF).GetValue($mf)
        $slotsDict = $tHost.GetField("slots", $BF).GetValue($hostObj)
        $slot0 = $slotsDict[0]
        Assert-Equal 2 $slot0.LastPlan.TrayStyle "SetTrayStyle propagates style=2 to MultiTray slot"
    } finally { try { $mf.Dispose() } catch {} }

    # 3. DevicePriorityForm hides pnlTrayCountBar cleanly when multiTray=false without disabling (no black text)
    $tPrioForm = $asm.GetType("RazerBatteryTray.DevicePriorityForm", $true)
    $tItem = $asm.GetType("RazerBatteryTray.PriorityItem", $true)
    $tListGeneric = [System.Type]::GetType("System.Collections.Generic.List``1").MakeGenericType(@($tItem))
    $lst = [Activator]::CreateInstance($tListGeneric)
    $it = [Activator]::CreateInstance($tItem); $it.DeviceId = "Dev:1"; $it.Name = "Dev1"; $lst.Add($it)
    $ctor5 = $tPrioForm.GetConstructors() | Where-Object { $_.GetParameters().Count -eq 5 } | Select-Object -First 1
    $getStateMethod = [System.Windows.Forms.Control].GetMethod("GetState", [System.Reflection.BindingFlags]"Instance,NonPublic")

    $pfOff = $ctor5.Invoke(@($lst.PSObject.BaseObject, $false, $false, 3, [single]1.0))
    try {
        $bar = $tPrioForm.GetField("pnlTrayCountBar", $BF).GetValue($pfOff)
        $reord = $tPrioForm.GetField("reorderControl", $BF).GetValue($pfOff)
        $cardSet = $tPrioForm.GetField("cardSettings", $BF).GetValue($pfOff)
        $chkM = $tPrioForm.GetField("chkMultiTray", $BF).GetValue($pfOff)
        Assert-True ($bar.Enabled) "pnlTrayCountBar stays Enabled=true so label text never turns black"
        Assert-Equal 0 $bar.Width "pnlTrayCountBar collapsed horizontally to Width=0 when disabled"
        Assert-True (-not [bool]$getStateMethod.Invoke($bar, @(2))) "pnlTrayCountBar hidden when disabled"
        $stableReordH = $reord.Height
        $stableCardH = $cardSet.Height
        $chkM.Checked = $true
        Assert-True ($bar.Width -gt 0 -and $bar.Left -gt $chkM.Left -and $cardSet.Height -eq $stableCardH -and $reord.Height -eq $stableReordH -and $reord.Bottom -le $cardSet.Top) "Toggling multi-tray unfolds inline selector on Row 2 without vertical layout shift"
    } finally { $pfOff.Dispose() }
}
