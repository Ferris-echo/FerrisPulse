# tests\test_device_lifecycle_and_sync.ps1
# 整合测试套件：设备库持久化优先顺位、隐藏/恢复、类别用户覆盖与真值校验、蓝牙双模去重与断联、
# 聚焦记忆、唤醒路径防串扰与防抖休眠、统一显示名称契约及无线传输推导
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "..\tools\TestHarness.ps1")

$asm = Load-FerrisPulseAssembly
$root = Get-FerrisPulseRoot
$BF = [System.Reflection.BindingFlags]"Public,NonPublic,Instance,Static"

$tMain = $asm.GetType("RazerBatteryTray.MainForm", $true)
$tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo", $true)
$tCat = $asm.GetType("RazerBatteryTray.DeviceCategory", $true)
$tCaps = $asm.GetType("RazerBatteryTray.DeviceCapabilities", $true)
$tDm = $asm.GetType("RazerBatteryTray.DeviceManager", $true)
$tDss = $asm.GetType("RazerBatteryTray.DriverStateStore", $true)
$tRz = $asm.GetType("RazerBatteryTray.RazerDeviceHelper", $true)
$tRzState = $asm.GetType("RazerBatteryTray.RazerDeviceHardwareState", $true)
$tBt = $asm.GetType("RazerBatteryTray.BluetoothDeviceHelper", $true)

Invoke-TestCase "1. 设备优先级持久库、隐藏/还原、类别用户覆写与 Scan (单轮 QueryAllDevices)" {
    $offlineId = "Razer:1532:00EF_TEST"
    $onlineId = "Mock:9999:FEFE"
    $regCustomPath = "HKCU:\Software\RazerBatteryTray\CustomDevices\$onlineId"
    $mGetPrio = $tDm.GetMethod("GetPriorityList", [System.Reflection.BindingFlags]"Static,Public")
    $mSavePrio = $tDm.GetMethod("SavePriorityList", [System.Reflection.BindingFlags]"Static,Public")
    $mHide = $tDm.GetMethod("HideOrDeleteDevice", [System.Reflection.BindingFlags]"Static,Public")
    $mIsHidden = $tDm.GetMethod("IsDeviceHidden", [System.Reflection.BindingFlags]"Static,Public")
    $mUnhide = $tDm.GetMethod("UnhideDevice", [System.Reflection.BindingFlags]"Static,Public")
    $origPrio = $mGetPrio.Invoke($null, @())

    try {
        New-Item -Path $regCustomPath -Force | Out-Null
        Set-ItemProperty -Path $regCustomPath -Name "DeviceId" -Value $onlineId
        Set-ItemProperty -Path $regCustomPath -Name "HardwareFingerprint" -Value $onlineId
        Set-ItemProperty -Path $regCustomPath -Name "OriginalName" -Value "Mock Device"
        Set-ItemProperty -Path $regCustomPath -Name "CustomName" -Value "测试模拟桌面音箱"
        Set-ItemProperty -Path $regCustomPath -Name "Category" -Value 1
        Set-ItemProperty -Path $regCustomPath -Name "Transport" -Value "2.4G 无线"
        Set-ItemProperty -Path $regCustomPath -Name "CachedBattery" -Value 66

        $mUnhide.Invoke($null, [object[]]@($offlineId)) | Out-Null
        $mUnhide.Invoke($null, [object[]]@($onlineId)) | Out-Null
        $mHide.Invoke($null, [object[]]@("TempHide:1111")) | Out-Null
        Assert-True ([bool]$mIsHidden.Invoke($null, @("TempHide:1111"))) "HideOrDeleteDevice marks hidden"
        $mUnhide.Invoke($null, [object[]]@("TempHide:1111")) | Out-Null
        Assert-True (-not [bool]$mIsHidden.Invoke($null, @("TempHide:1111"))) "UnhideDevice restores"

        $pList = New-Object 'System.Collections.Generic.List[string]'
        $pList.Add($offlineId); $pList.Add($onlineId)
        $sa = New-Object object[] 1; $sa[0] = $pList.PSObject.BaseObject
        $mSavePrio.Invoke($null, $sa) | Out-Null
        $tDss.GetMethod("SaveBatteryState", [System.Reflection.BindingFlags]"Static,Public").Invoke($null, @($offlineId, 85, $false, "Razer DeathAdder V4 Pro Carbon")) | Out-Null

        # 仅执行 1 次 QueryAllDevices 同时覆盖 BT 去重、类别用户覆盖和优先级保活
        $allDevices = $tDm.GetMethod("QueryAllDevices", [System.Reflection.BindingFlags]"Static,Public").Invoke($null, @())
        $prioAfter = $mGetPrio.Invoke($null, @())
        Assert-True ($prioAfter.Contains($offlineId) -and $prioAfter[0] -eq $offlineId) "Offline device preserved at priority 0"

        $targetCustom = $null
        $audioKeys = @{}
        foreach ($d in $allDevices) {
            if ($d.DeviceId -eq $onlineId) { $targetCustom = $d }
            if ($d.Category.ToString() -in @("Headset", "Earbuds") -and $d.Transport -like "*蓝牙*") {
                $k = if ([string]::IsNullOrEmpty($d.OriginalName)) { $d.DeviceName } else { $d.OriginalName }
                Assert-True (-not $audioKeys.ContainsKey($k)) "No duplicate BT audio card for $k"
                $audioKeys[$k] = $true
            }
        }
        Assert-True ($null -ne $targetCustom -and $targetCustom.Category.ToString() -eq "Headset") "User Category=Headset preserved over fallback"

        # GetAllKnownDevicesForPriority + FindPriorityIndex variants
        $listType = [System.Collections.Generic.List`1].MakeGenericType(@($tInfo))
        $mockAct = [Activator]::CreateInstance($listType)
        $onD = [Activator]::CreateInstance($tInfo); $onD.DeviceId = $onlineId; $onD.IsConnected = $true; $onD.BatteryPercent = 92
        $mockAct.Add($onD)
        $ka = New-Object object[] 1; $ka[0] = $mockAct.PSObject.BaseObject
        $known = $tDm.GetMethod("GetAllKnownDevicesForPriority", [System.Reflection.BindingFlags]"Static,Public").Invoke($null, $ka)
        $foundOff = $known | Where-Object { $_.DeviceId -eq $offlineId } | Select-Object -First 1
        Assert-True ($null -ne $foundOff -and -not $foundOff.IsConnected -and $foundOff.BatteryPercent -eq 85) "Offline cached device shown in GetAllKnownDevicesForPriority"

        $vList = New-Object 'System.Collections.Generic.List[string]'; $vList.Add("Razer:1532:00BE")
        $mFindP = $tDm.GetMethod("FindPriorityIndex", [System.Reflection.BindingFlags]"Static,Public")
        Assert-Equal 0 ([int]$mFindP.Invoke($null, [object[]]@($vList.PSObject.BaseObject, "Razer:1532:00BF"))) "FindPriorityIndex matches variant"
    } finally {
        Remove-Item -Path $regCustomPath -Recurse -Force -ErrorAction SilentlyContinue
        $tDss.GetMethod("ClearDeviceState", [System.Reflection.BindingFlags]"Static,Public").Invoke($null, @($offlineId)) | Out-Null
        if ($null -ne $origPrio) { $ra = New-Object object[] 1; $ra[0] = $origPrio.PSObject.BaseObject; $mSavePrio.Invoke($null, $ra) | Out-Null }
    }
}

Invoke-TestCase "2. 蓝牙断联与双模时间线 (NativeMethods.DEVICE_NOTIFY_ALL_INTERFACE_CLASSES=4)" {
    $tNative = $asm.GetType("RazerBatteryTray.NativeMethods", $true)
    $val = [uint32]$tNative.GetField("DEVICE_NOTIFY_ALL_INTERFACE_CLASSES", [System.Reflection.BindingFlags]"Public,Static").GetValue($null)
    Assert-Equal 4 $val "DEVICE_NOTIFY_ALL_INTERFACE_CLASSES == 4"

    $qMac = $tBt.GetMethod("QueryByMac", [System.Reflection.BindingFlags]"Public,Static", $null, @([string], [string], [int].MakeByRefType(), [string].MakeByRefType()), $null)
    $p = [object[]]@("D056FB203888", "Buds3 Pro", 0, "")
    $conn = [bool]$qMac.Invoke($null, $p)
    Assert-True (-not $conn -and $p[3] -eq "Buds3 Pro") "Disconnected BT mac stays offline and preserves hint name"
    $scanned = $tBt.GetMethod("ScanAllBluetoothDevices", [System.Reflection.BindingFlags]"Public,Static").Invoke($null, $null)
    Assert-True ($null -ne $scanned) "ScanAllBluetoothDevices callable"
}

Invoke-TestCase "3. 类别真值与隔离：Razer 无电池设备、MchoseMouse/Keyboard 真值、无线推断" {
    $mCat = $tRz.GetMethod("GetRazerDeviceCategory", $BF)
    Assert-Equal "Mouse" ($mCat.Invoke($null, @(0x00C0, "Razer Viper V3 Pro")).ToString()) "0x00C0 Mouse"
    Assert-Equal "Keyboard" ($mCat.Invoke($null, @(0x026F, "Razer Huntsman V2")).ToString()) "0x026F Keyboard"
    Assert-Equal "Headset" ($mCat.Invoke($null, @(0x00A5, "Razer Nommo V2 Pro")).ToString()) "0x00A5 Headset"

    $mFb = $tRz.GetMethod("CreateFallbackDeviceInfo")
    $kbd = $mFb.Invoke($null, @("Razer:1532:026F", 0x026F, "Razer Huntsman V2", $false, $null))
    Assert-True ($kbd.BatteryPercent -eq -1 -and -not $kbd.IsSleeping) "Keyboard never inherits mouse battery or sleep"

    $mDongle = $tRz.GetMethod("DetermineIsDongle")
    Assert-True ([bool]$mDongle.Invoke($null, @([int]0x00C3, "Razer Viper V3 Pro", 85, $false))) "SE 0x00C3 non-charging -> Dongle"
    Assert-True (-not [bool]$mDongle.Invoke($null, @([int]0x00C0, "Razer Viper V3 Pro", 90, $true))) "Wired charging -> not Dongle"
    Assert-Equal 0x00C0 ([int]$tRz.GetMethod("NormalizeRazerPid").Invoke($null, @([int]0x00C3))) "0x00C3 normalizes to 0x00C0"

    $mAuth = $asm.GetType("RazerBatteryTray.MchoseDeviceHelper", $true).GetMethod("TryGetAuthoritativeCategory")
    $a1 = [object[]]@("HID:3837:1018", "HID:3837:1018", [Enum]::Parse($tCat, "Mouse"))
    Assert-True ([bool]$mAuth.Invoke($null, $a1) -and $a1[2].ToString() -eq "Mouse") "3837:1018 -> Mouse"
    $a2 = [object[]]@("HID:3837:3033", "HID:3837:3033", [Enum]::Parse($tCat, "Mouse"))
    Assert-True ([bool]$mAuth.Invoke($null, $a2) -and $a2[2].ToString() -eq "Keyboard") "3837:3033 -> Keyboard"

    # Display name contract
    $d = [Activator]::CreateInstance($tInfo); $d.DeviceName = "Razer Viper V3 Pro"; $d.CustomName = "毒蝰V3Pro"
    Assert-Equal "毒蝰V3Pro" $d.DisplayName "Unified DisplayName"
    $tabSrc = [IO.File]::ReadAllText((Join-Path $root "src\UI\Controls\SmoothSegmentedTabBar.cs"), [Text.Encoding]::UTF8)
    Assert-True ($tabSrc -notmatch "dev\.FriendlyShortName") "SmoothSegmentedTabBar uses DisplayName"
}

Invoke-TestCase "4. 唤醒路径物理隔离、UpdateUI 防串扰、休眠防抖与 0x04 离线判定" {
    $mPath = $tMain.GetMethod("IsTargetMouseHardwarePath", $BF)
    $se = [Activator]::CreateInstance($tInfo); $se.DeviceId = "Razer:1532:00DF"; $se.Brand = "Razer"
    $v3 = [Activator]::CreateInstance($tInfo); $v3.DeviceId = "Razer:1532:00C1"; $v3.Brand = "Razer"
    $mc = [Activator]::CreateInstance($tInfo); $mc.DeviceId = "MCHOSE:3837:1018"; $mc.Brand = "Mchose"
    Assert-True ([bool]$mPath.Invoke($null, @("\\?\HID#VID_1532&PID_00DF&MI_00", $se))) "SE own path matches"
    Assert-True (-not [bool]$mPath.Invoke($null, @("\\?\HID#VID_1532&PID_00C1&MI_00", $se))) "V3Pro path ignored by SE"
    Assert-True (-not [bool]$mPath.Invoke($null, @("\\?\HID#VID_1532&PID_00DF&MI_00", $mc))) "Razer path ignored by MCHOSE"

    $gpw1 = [Activator]::CreateInstance($tInfo); $gpw1.DeviceId = "Logitech:046D:C539:1"; $gpw1.Brand = "Logitech"
    Assert-True ([bool]$mPath.Invoke($null, @("\\?\HID#VID_046D&PID_C539&MI_00", $gpw1))) "GPW1 C539 match"
    Assert-True (-not [bool]$mPath.Invoke($null, @("\\?\HID#VID_046D&PID_C53F&MI_00", $gpw1))) "GPW2 C53F isolated"

    # UpdateUI foreign-device guard
    $uninit = [System.Runtime.Serialization.FormatterServices]::GetUninitializedObject($tMain)
    $gpw2 = [Activator]::CreateInstance($tInfo); $gpw2.DeviceId = "Logitech:046D:C53F:1"; $gpw2.DeviceName = "PRO X 2"
    $lbl = New-Object System.Windows.Forms.Label; $lbl.Text = "PRO X 2"
    $tMain.GetField("selectedDevice", $BF).SetValue($uninit, $gpw2)
    $tMain.GetField("lblDeviceName", $BF).SetValue($uninit, $lbl)
    $gpw1.DeviceName = "GPW1"
    $tMain.GetMethod("UpdateUI", $BF).Invoke($uninit, @($gpw1, $false)) | Out-Null
    Assert-Equal "PRO X 2" $lbl.Text "Foreign UpdateUI blocked"

    # Sleep debounce & consecutive 0x04 link loss
    $mFb = $tRz.GetMethod("CreateFallbackDeviceInfo")
    $st1 = [Activator]::CreateInstance($tRzState); $st1.BatteryPercent = 85; $st1.ConsecutiveFailures = 1; $st1.LastSuccessfulCommunication = [DateTime]::Now.AddSeconds(-5)
    $d1 = $mFb.Invoke($null, @("Razer:1532:00C1", 0x00C1, "Razer Viper V3 Pro", $true, $st1))
    Assert-True ($d1.IsConnected -and -not $d1.IsSleeping) "Single failure stays awake"

    $st2 = [Activator]::CreateInstance($tRzState); $st2.BatteryPercent = 85; $st2.ConsecutiveFailures = 3; $st2.ConsecutiveLinkDown = 1; $st2.LastSuccessfulCommunication = [DateTime]::Now.AddSeconds(-60)
    $d2 = $mFb.Invoke($null, @("Razer:1532:00C1", 0x00C1, "Razer Viper V3 Pro", $true, $st2))
    Assert-True ($d2.IsConnected -and $d2.IsSleeping) "3 failures + 60s enters sleeping"

    $st3 = [Activator]::CreateInstance($tRzState); $st3.BatteryPercent = 85; $st3.ConsecutiveFailures = 3; $st3.ConsecutiveLinkDown = 2; $st3.LastSuccessfulCommunication = [DateTime]::Now.AddSeconds(-60)
    $d3 = $mFb.Invoke($null, @("Razer:1532:00C1", 0x00C1, "Razer Viper V3 Pro", $true, $st3))
    Assert-True (-not $d3.IsConnected -and -not $d3.IsSleeping -and $d3.BatteryPercent -eq 85) "2x 0x04 transitions to offline preserving battery"
}

Invoke-TestCase "5. 主窗体冷启动优选与跨形态聚焦记忆 (MainForm 单实例)" {
    $form = New-SandboxMainForm $false
    try {
        $listType = [System.Collections.Generic.List`1].MakeGenericType(@($tInfo))
        $devList = [Activator]::CreateInstance($listType)
        $kb = [Activator]::CreateInstance($tInfo); $kb.DeviceId = "MCHOSE:3837:3033"; $kb.Category = [Enum]::Parse($tCat, "Keyboard"); $kb.IsConnected = $true; $kb.Capabilities = [Activator]::CreateInstance($tCaps)
        $ms = [Activator]::CreateInstance($tInfo); $ms.DeviceId = "MCHOSE:3837:1018"; $ms.Category = [Enum]::Parse($tCat, "Mouse"); $ms.IsConnected = $false; $ms.IsDonglePresent = $true; $ms.Capabilities = [Activator]::CreateInstance($tCaps); $ms.Capabilities.SupportsDpi = $true
        $devList.Add($kb); $devList.Add($ms)

        $tMain.GetField("activeDevices", $BF).SetValue($form, $devList)
        $tMain.GetField("lastSelectedDeviceId", $BF).SetValue($form, "")
        $pick = $tMain.GetMethod("GetPreferredStartupDevice", $BF).Invoke($form, $null)
        Assert-Equal "MCHOSE:3837:1018" $pick.DeviceId "Cold start prefers DPI mouse over keyboard"

        # Cross-transport focus memory (2.4G -> BLE)
        $tMain.GetField("lastSelectedDeviceId", $BF).SetValue($form, "MCHOSE:3837:1018")
        $ble = [Activator]::CreateInstance($tInfo); $ble.DeviceId = "MCHOSE:3837:4026"; $ble.Brand = "MCHOSE"; $ble.Category = [Enum]::Parse($tCat, "Mouse"); $ble.IsConnected = $true; $ble.BatteryPercent = 75; $ble.Capabilities = [Activator]::CreateInstance($tCaps)
        $bleList = [Activator]::CreateInstance($listType); $bleList.Add($kb); $bleList.Add($ble)
        $ba = New-Object object[] 1; $ba[0] = $bleList.PSObject.BaseObject
        $foundBle = $tMain.GetMethod("FindRememberedDevice", $BF).Invoke($form, $ba)
        Assert-True ($null -ne $foundBle -and $foundBle.DeviceId -eq "MCHOSE:3837:4026") "Cross-transport 2.4G -> BLE preserved"
    } finally { $form.Dispose() }
}

Invoke-TestCase "6. 休眠鼠标移动即时唤醒：多候选匹配、键盘前台防屏蔽、BLE/HardwareId 匹配与托盘变绿" {
    # 6.1 HardwareId receiver match + BLE VID&PID match while preserving receiver isolation
    $mPath = $tMain.GetMethod("IsTargetMouseHardwarePath", $BF)
    $logiPaired = [Activator]::CreateInstance($tInfo)
    $logiPaired.DeviceId = "Logitech:046D:40A9:1"
    $logiPaired.HardwareId = "\\?\HID#VID_046D&PID_C54D&MI_02&COL02#test"
    $logiPaired.Brand = "Logitech"
    Assert-True ([bool]$mPath.Invoke($null, @("\\?\HID#VID_046D&PID_C54D&MI_00#test", $logiPaired))) "Logitech paired DeviceId matches receiver HardwareId path"

    $logiBle = [Activator]::CreateInstance($tInfo)
    $logiBle.DeviceId = "Logitech:046D:B034"
    $logiBle.Brand = "Logitech"
    Assert-True ([bool]$mPath.Invoke($null, @("\\?\HID#{00001812-0000-1000-8000-00805F9B34FB}_DEV_VID&02046D_PID&B034_REV&0009#test", $logiBle))) "BLE VID&02046D_PID&B034 path matches"

    # 6.2 Sleeping primary mouse must wake even when a Keyboard is selectedDevice
    $form = New-SandboxMainForm $false
    try {
        $listType = [System.Collections.Generic.List`1].MakeGenericType(@($tInfo))
        $devs = [Activator]::CreateInstance($listType)

        $mouse = [Activator]::CreateInstance($tInfo)
        $mouse.DeviceId = "Razer:1532:00DF"; $mouse.Brand = "Razer"; $mouse.DeviceName = "Razer Viper V3 Pro SE"
        $mouse.Category = [Enum]::Parse($tCat, "Mouse"); $mouse.IsPrimary = $true; $mouse.IsConnected = $true
        $mouse.IsSleeping = $true; $mouse.IsDonglePresent = $true; $mouse.BatteryPercent = 88

        $kb = [Activator]::CreateInstance($tInfo)
        $kb.DeviceId = "NuPhy:19F5:3245"; $kb.Brand = "NuPhy"; $kb.DeviceName = "NuPhy Air75"
        $kb.Category = [Enum]::Parse($tCat, "Keyboard"); $kb.IsPrimary = $false; $kb.IsConnected = $true
        $kb.IsSleeping = $false; $kb.BatteryPercent = 90

        $devs.Add($mouse); $devs.Add($kb)
        $tMain.GetField("activeDevices", $BF).SetValue($form, $devs)
        $tMain.GetField("lastInfo", $BF).SetValue($form, $mouse)
        $tMain.GetField("selectedDevice", $BF).SetValue($form, $kb)

        # Updating UI for Keyboard must keep ShouldMonitorMouseWakeInput() = true because primary mouse is sleeping
        $tMain.GetMethod("UpdateUI", $BF).Invoke($form, @($kb, $false)) | Out-Null
        $mShould = $tMain.GetMethod("ShouldMonitorMouseWakeInput", $BF)
        Assert-True ($null -ne $mShould) "ShouldMonitorMouseWakeInput method exists"
        Assert-True ([bool]$mShould.Invoke($form, $null)) "Wake detector stays active while keyboard tab is viewed and primary mouse sleeps"

        # Capture amber sleep tray bitmap, then process wake path -> must flip IsSleeping=false and change tray icon to brand green immediately
        $tMain.GetMethod("SetMultiTrayEnabled", $BF).Invoke($form, @($false, $false)) | Out-Null
        $trayField = $tMain.GetField("trayIcon", $BF)
        $trayObj = $trayField.GetValue($form)
        $tMain.GetMethod("UpdateTrayIcon", $BF).Invoke($form, @($mouse.BatteryPercent, $false, $true, $true, $mouse.BrandColor)) | Out-Null
        $amberBmp = $trayObj.Icon.ToBitmap()
        $mWake = $tMain.GetMethod("TryProcessMouseWakePath", $BF)
        Assert-True ($null -ne $mWake) "TryProcessMouseWakePath method exists"
        $woken = [bool]$mWake.Invoke($form, @("\\?\HID#VID_1532&PID_00DF&MI_00#test"))
        Assert-True $woken "Primary sleeping mouse matched and woke"
        Assert-True (-not $mouse.IsSleeping) "Primary mouse IsSleeping cleared immediately"
        Assert-True (-not $tMain.GetField("isMouseSleeping", $BF).GetValue($form)) "isMouseSleeping cleared"
        $greenBmp = $trayObj.Icon.ToBitmap()
        try {
            $changed = $false
            for ($y = 0; $y -lt $greenBmp.Height -and -not $changed; $y++) {
                for ($x = 0; $x -lt $greenBmp.Width; $x++) {
                    $c1 = $amberBmp.GetPixel($x, $y); $c2 = $greenBmp.GetPixel($x, $y)
                    if ($c1.ToArgb() -ne $c2.ToArgb()) { $changed = $true; break }
                }
            }
            Assert-True $changed "Tray icon pixels updated immediately from sleep amber to awake brand color"
        } finally {
            $amberBmp.Dispose(); $greenBmp.Dispose()
        }
    } finally { $form.Dispose() }
}

Write-Host "`nALL DEVICE LIFECYCLE & SYNC TESTS PASSED." -ForegroundColor Green
exit 0
