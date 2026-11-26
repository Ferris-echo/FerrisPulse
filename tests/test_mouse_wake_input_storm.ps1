# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# tests\test_mouse_wake_input_storm.ps1
# 回归测试：8K 高回报率鼠标的原始输入 (WM_INPUT) 唤醒路径
#   1. 幽灵休眠卡（同接收器重复建卡 / 离线残留）绝不得把「已在线清醒」的鼠标误判成刚被唤醒；
#      旧实现用窗体级聚合标记 isMouseSleeping 判唤醒，只要有任意一台鼠标休眠/离线就永久为 true，
#      导致每一个输入报文都触发全量 UpdateUI，UI 线程被输入流淹没（开启软件数秒后整窗卡死、CPU 100%）。
#   2. 真实「休眠 -> 运动」唤醒契约必须保持即时生效（IsSleeping 清零 + 托盘颜色即时刷新）。
#   3. 唤醒后的全量界面重建必须合并为至多一次，绝不能在输入回调内按报文频次堆积。
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "..\tools\TestHarness.ps1")

$asm = Load-FerrisPulseAssembly
$BF = [System.Reflection.BindingFlags]"Public,NonPublic,Instance,Static"

$tMain = $asm.GetType("RazerBatteryTray.MainForm", $true)
$tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo", $true)
$tCat = $asm.GetType("RazerBatteryTray.DeviceCategory", $true)
$tCaps = $asm.GetType("RazerBatteryTray.DeviceCapabilities", $true)
$listType = [System.Collections.Generic.List`1].MakeGenericType(@($tInfo))

$ViperPath = "\\?\HID#VID_1532&PID_00C1&MI_00#8&16CEB4D8&0&0000"
$ViperIdlePath = "\\?\HID#VID_1532&PID_00DF&MI_00#test"

function New-TestInfo {
    param(
        [string]$DeviceId,
        [string]$Brand,
        [string]$DeviceName,
        [string]$Category,
        [bool]$Connected,
        [bool]$Sleeping,
        [bool]$Primary,
        [bool]$Dongle
    )
    $d = [Activator]::CreateInstance($tInfo)
    $d.DeviceId = $DeviceId
    $d.Brand = $Brand
    $d.DeviceName = $DeviceName
    $d.Category = [Enum]::Parse($tCat, $Category)
    $d.IsConnected = $Connected
    $d.IsSleeping = $Sleeping
    $d.IsPrimary = $Primary
    $d.IsDonglePresent = $Dongle
    $d.BatteryPercent = 88
    $d.Capabilities = [Activator]::CreateInstance($tCaps)
    return $d
}

Invoke-TestCase "1. 幽灵休眠鼠标绝不污染在线鼠标的唤醒判定（8K 卡死根因回归）" {
    $form = New-SandboxMainForm $false
    try {
        $viper = New-TestInfo "Razer:1532:00C0" "Razer" "Razer Viper V3 Pro" "Mouse" $true $false $true $true
        # 现场实测的干扰源：同一个 3554:FA09 接收器被重复建成「键盘 + 鼠标」两张离线卡
        $phantomMouse = New-TestInfo "VGN:3554:FA09" "VGN" "CompX Receiver (legacy)" "Mouse" $false $true $false $true
        $phantomKeyboard = New-TestInfo "CompX:3554:FA09" "CompX" "CompX 2.4G Wireless Keyboard" "Keyboard" $false $true $false $true

        $devs = [Activator]::CreateInstance($listType)
        $devs.Add($viper); $devs.Add($phantomMouse); $devs.Add($phantomKeyboard)

        $tMain.GetField("activeDevices", $BF).SetValue($form, $devs)
        $tMain.GetField("lastInfo", $BF).SetValue($form, $viper)
        $tMain.GetField("selectedDevice", $BF).SetValue($form, $viper)
        # 模拟被幽灵卡污染的窗体级聚合标记
        $tMain.GetField("isMouseSleeping", $BF).SetValue($form, $true)
        $tMain.GetField("pendingWakeUiTarget", $BF).SetValue($form, $null)

        $mWake = $tMain.GetMethod("TryProcessMouseWakePath", $BF)
        $woken = [bool]$mWake.Invoke($form, @($ViperPath))

        Assert-True (-not $woken) "在线清醒鼠标的输入报文不得被判定为唤醒转换"
        Assert-True (-not $viper.IsSleeping) "在线鼠标 IsSleeping 保持 false"
        Assert-True $viper.IsConnected "在线鼠标 IsConnected 保持 true"
        Assert-True ($null -eq $tMain.GetField("pendingWakeUiTarget", $BF).GetValue($form)) "不得为该报文排队任何全量界面重建"

        # 连续 50 个报文（8K 回报率下一瞬间的自然量）仍必须全部判定为「无唤醒」
        $hit = 0
        for ($i = 0; $i -lt 50; $i++) {
            if ([bool]$mWake.Invoke($form, @($ViperPath))) { $hit++ }
        }
        Assert-Equal 0 $hit "50 个连续报文不得出现任何一次伪唤醒"
        Assert-True ($null -eq $tMain.GetField("pendingWakeUiTarget", $BF).GetValue($form)) "50 个报文后仍不得排队界面重建"
    } finally { $form.Dispose() }
}

Invoke-TestCase "2. 真实休眠到运动的唤醒契约保持即时生效" {
    $form = New-SandboxMainForm $false
    try {
        $mouse = New-TestInfo "Razer:1532:00DF" "Razer" "Razer Viper V3 Pro SE" "Mouse" $true $true $true $true
        $devs = [Activator]::CreateInstance($listType)
        $devs.Add($mouse)

        $tMain.GetField("activeDevices", $BF).SetValue($form, $devs)
        $tMain.GetField("lastInfo", $BF).SetValue($form, $mouse)
        $tMain.GetField("selectedDevice", $BF).SetValue($form, $mouse)
        $tMain.GetField("isMouseSleeping", $BF).SetValue($form, $true)
        $tMain.GetField("pendingWakeUiTarget", $BF).SetValue($form, $null)

        $tMain.GetMethod("SetMultiTrayEnabled", $BF).Invoke($form, @($false, $false)) | Out-Null
        $trayObj = $tMain.GetField("trayIcon", $BF).GetValue($form)
        $tMain.GetMethod("UpdateTrayIcon", $BF).Invoke($form, @($mouse.BatteryPercent, $false, $true, $true, $mouse.BrandColor)) | Out-Null
        $amberBmp = $trayObj.Icon.ToBitmap()

        $mWake = $tMain.GetMethod("TryProcessMouseWakePath", $BF)
        $woken = [bool]$mWake.Invoke($form, @($ViperIdlePath))
        Assert-True $woken "休眠鼠标的输入报文必须命中唤醒"
        Assert-True (-not $mouse.IsSleeping) "休眠标记必须即时清零"
        Assert-True (-not [bool]$tMain.GetField("isMouseSleeping", $BF).GetValue($form)) "聚合休眠标记随之清零"
        Assert-True ($null -ne $tMain.GetField("pendingWakeUiTarget", $BF).GetValue($form)) "唤醒必须排队一次界面重建"

        $greenBmp = $trayObj.Icon.ToBitmap()
        try {
            $changed = $false
            for ($y = 0; $y -lt $greenBmp.Height -and -not $changed; $y++) {
                for ($x = 0; $x -lt $greenBmp.Width; $x++) {
                    if ($amberBmp.GetPixel($x, $y).ToArgb() -ne $greenBmp.GetPixel($x, $y).ToArgb()) { $changed = $true; break }
                }
            }
            Assert-True $changed "托盘图标必须同步由琥珀色切换为品牌色"
        } finally { $amberBmp.Dispose(); $greenBmp.Dispose() }

        # 唤醒后续报文（此时已在线清醒）不得再触发新一轮界面重建
        $tMain.GetField("pendingWakeUiTarget", $BF).SetValue($form, $null)
        for ($i = 0; $i -lt 20; $i++) { [void]$mWake.Invoke($form, @($ViperIdlePath)) }
        Assert-True ($null -eq $tMain.GetField("pendingWakeUiTarget", $BF).GetValue($form)) "唤醒后的连续报文不得重复排队界面重建"
    } finally { $form.Dispose() }
}

Invoke-TestCase "3. 原始输入廉价闸门与路径级唤醒判定真值表" {
    $form = New-SandboxMainForm $false
    try {
        $awake = New-TestInfo "Razer:1532:00C0" "Razer" "Razer Viper V3 Pro" "Mouse" $true $false $true $true
        $devs = [Activator]::CreateInstance($listType)
        $devs.Add($awake)
        $tMain.GetField("activeDevices", $BF).SetValue($form, $devs)
        $tMain.GetField("lastInfo", $BF).SetValue($form, $awake)
        $tMain.GetField("selectedDevice", $BF).SetValue($form, $awake)

        $mGate = $tMain.GetMethod("HasSleepingOrOfflineMouseCandidate", $BF)
        $mNeed = $tMain.GetMethod("IsWakeNeededForPath", $BF)
        $cands = $tMain.GetMethod("CollectWakeCandidateMice", $BF).Invoke($form, $null)

        Assert-True (-not [bool]$mGate.Invoke($form, $null)) "全部鼠标在线清醒时闸门必须关闭"
        Assert-True (-not [bool]$mNeed.Invoke($form, @($ViperPath, $cands))) "清醒设备路径不得被判定为待唤醒"

        $sleeping = New-TestInfo "Razer:1532:00DF" "Razer" "Razer Viper V3 Pro SE" "Mouse" $true $true $true $true
        $devs2 = [Activator]::CreateInstance($listType)
        $devs2.Add($sleeping)
        $tMain.GetField("activeDevices", $BF).SetValue($form, $devs2)
        $tMain.GetField("lastInfo", $BF).SetValue($form, $sleeping)
        $tMain.GetField("selectedDevice", $BF).SetValue($form, $sleeping)
        $cands2 = $tMain.GetMethod("CollectWakeCandidateMice", $BF).Invoke($form, $null)

        Assert-True ([bool]$mGate.Invoke($form, $null)) "存在休眠鼠标时闸门必须打开"
        Assert-True ([bool]$mNeed.Invoke($form, @($ViperIdlePath, $cands2))) "休眠设备路径必须被判定为待唤醒"
        Assert-True (-not [bool]$mNeed.Invoke($form, @($ViperPath, $cands2))) "无关设备路径不得被判定为待唤醒"
    } finally { $form.Dispose() }
}

Write-Host "`nALL MOUSE WAKE INPUT STORM TESTS PASSED." -ForegroundColor Green
exit 0
