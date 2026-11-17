# tests/test_razer_link_offline_and_wake_isolation.ps1
# 1) 唤醒事件必须精确到型号：别的鼠标（含同厂不同型号）移动，不得把本卡片判成 2.4G 在线
# 2) 接收器连续回报 0x04（链路触达不到设备）→ 卡片判离线，而不是继续伪造成「休眠待机」

$ErrorActionPreference = "Stop"

$fullPath = (Resolve-Path (Join-Path $PSScriptRoot "..\FerrisPulse.exe")).Path
$bytes = [System.IO.File]::ReadAllBytes($fullPath)
$asm = [System.Reflection.Assembly]::Load($bytes)

$tMain = $asm.GetType("RazerBatteryTray.MainForm")
$tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo")
$tRz = $asm.GetType("RazerBatteryTray.RazerDeviceHelper")
$tState = $asm.GetType("RazerBatteryTray.RazerDeviceHardwareState")

if ($tMain -eq $null -or $tInfo -eq $null -or $tRz -eq $null -or $tState -eq $null) {
    Write-Host "FAIL: required types not found" -ForegroundColor Red
    exit 1
}

$flags = [System.Reflection.BindingFlags]"NonPublic,Static,Public"
$miMatch = $tMain.GetMethod("IsTargetMouseHardwarePath", $flags)
if ($miMatch -eq $null) {
    Write-Host "FAIL: IsTargetMouseHardwarePath not found" -ForegroundColor Red
    exit 1
}

$failures = 0
function Assert($cond, $msg) {
    if ($cond) {
        Write-Host ("PASS: " + $msg) -ForegroundColor Green
    } else {
        Write-Host ("FAIL: " + $msg) -ForegroundColor Red
        $script:failures++
    }
}

function NewDev($brand, $deviceId, $hardwareId) {
    $d = [System.Activator]::CreateInstance($tInfo)
    $d.Brand = $brand
    $d.DeviceId = $deviceId
    $d.HardwareId = $hardwareId
    return $d
}

$pathSe = '\\?\HID#VID_1532&PID_00DF&MI_01&COL02#9&3099e7dd&0&0001'
$pathV3 = '\\?\HID#VID_1532&PID_00C1&MI_01&COL02#9&3099e7dd&0&0002'
$pathMc = '\\?\HID#VID_3837&PID_0001&MI_01&COL02#9&3099e7dd&0&0003'

$devSe = NewDev "Razer" "Razer:1532:00DE" "HID:1532:00DF"
$devV3 = NewDev "Razer" "Razer:1532:00C0" "HID:1532:00C1"
$devMc = NewDev "MCHOSE" "MCHOSE:3837:0001" "HID:3837:0001"

Assert ([bool]$miMatch.Invoke($null, @($pathSe, $devSe))) "Viper V3 Pro SE 自身接收器路径(00DF)可唤醒"
Assert (-not [bool]$miMatch.Invoke($null, @($pathV3, $devSe))) "同厂 Viper V3 Pro(00C1) 的移动不得唤醒 SE 卡片"
Assert (-not [bool]$miMatch.Invoke($null, @($pathMc, $devSe))) "迈从 A5 的移动不得唤醒 SE 卡片"
Assert (-not [bool]$miMatch.Invoke($null, @($pathSe, $devV3))) "SE 的移动不得唤醒 Viper V3 Pro 卡片"
Assert ([bool]$miMatch.Invoke($null, @($pathMc, $devMc))) "迈从 A5 自身路径可唤醒"
Assert (-not [bool]$miMatch.Invoke($null, @($pathSe, $devMc))) "雷蛇的移动不得唤醒迈从卡片"

$miFallback = $tRz.GetMethod("CreateFallbackDeviceInfo")
if ($miFallback -eq $null) {
    Write-Host "FAIL: CreateFallbackDeviceInfo not found" -ForegroundColor Red
    exit 1
}

function NewState($linkDown) {
    $st = [System.Activator]::CreateInstance($tState)
    $st.DeviceId = "Razer:1532:00DE"
    $st.BatteryPercent = 85
    $st.ExactBatteryPercent = 85.0
    $st.ConsecutiveFailures = 3
    $st.ConsecutiveLinkDown = $linkDown
    $st.LastSuccessfulCommunication = [DateTime]::Now.AddSeconds(-60)
    $st.LastUpdated = [DateTime]::Now.AddSeconds(-60)
    return $st
}

$normId = "Razer:1532:00DE"
$curPid = 0x00DF

$st1 = NewState 1
$dev1 = $miFallback.Invoke($null, @($normId, $curPid, "Razer Viper V3 Pro SE", $true, $st1))
Assert ($dev1.IsConnected -eq $true) "单轮 0x04 仍视为在线（防抖窗口）"
Assert ($dev1.IsSleeping -eq $true) "单轮 0x04 + 3 次失败 + 60 秒静默进入休眠待机"

$st2 = NewState 2
$dev2 = $miFallback.Invoke($null, @($normId, $curPid, "Razer Viper V3 Pro SE", $true, $st2))
Assert ($dev2.IsConnected -eq $false) "连续两轮 0x04 判定为离线（关机/链路丢失）"
Assert ($dev2.IsSleeping -eq $false) "离线态不得再标记 IsSleeping"
Assert ($dev2.BatteryPercent -eq 85) "离线卡片保留上一次真实电量"

$st3 = NewState 2
$st3.BatteryPercent = 0
$st3.ExactBatteryPercent = 0
$dev3 = $miFallback.Invoke($null, @($normId, $curPid, "Razer Viper V3 Pro SE", $true, $st3))
Assert ($dev3.IsConnected -eq $false -and $dev3.BatteryPercent -eq -1) "无历史电量的离线卡片显示 -- 而不是伪造 50%"

$st5 = NewState 3
$dev5 = $miFallback.Invoke($null, @($normId, 0x00DE, "Razer Viper V3 Pro SE", $false, $st5))
Assert ($dev5.IsConnected -eq $true) "有线通道 0x04 不判离线"

$st4 = NewState 0
$dev4 = $miFallback.Invoke($null, @($normId, $curPid, "Razer Viper V3 Pro SE", $true, $st4))
Assert ($dev4.IsConnected -eq $true) "链路计数清零后恢复在线"

if ($failures -gt 0) {
    Write-Host ("FAILED: $failures assertion(s)") -ForegroundColor Red
    exit 1
}

Write-Host "ALL TESTS PASSED for test_razer_link_offline_and_wake_isolation.ps1" -ForegroundColor Green
exit 0
