# tests/test_shared_pid_identity.ps1
# M3 验证：CompX 公版共用 PID 0xFA09（KZZI K75 / MCHOSE G75 PRO / MARVO / AULA / YUNZII）的身份隔离
# 断言口径（docs/OPTIMIZATION_BACKLOG.md M3）：
#   1) 3554:FA09 不得被判为 VGN 私有协议端点；
#   2) VGN 端点路径判定必须统一走 IsVgnCandidatePath 且显式排除 pid_fa09；
#   3) BuiltIn:KzziKeyboard 已登记严格身份：只有真正的 KZZI 键盘才被判给它；
#   4) 同一接收器不会被 KZZI 驱动接管成"幽灵卡"（VGN 鼠标 / MCHOSE 键盘各自归位）。
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "..\tools\TestHarness.ps1")

$asm = Load-FerrisPulseAssembly
$root = Get-FerrisPulseRoot
$tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo", $true)
$tReg = $asm.GetType("RazerBatteryTray.HardwareDriverRegistry", $true)
$tVgn = $asm.GetType("RazerBatteryTray.VgnDeviceHelper", $true)
$mResolve = $tReg.GetMethod("ResolveDriver", [System.Reflection.BindingFlags]"Static,Public")

function New-Device([string]$id, $category, [string]$brand) {
    $d = [Activator]::CreateInstance($tInfo)
    $d.DeviceId = $id
    $d.HardwareFingerprint = $id
    $d.Brand = $brand
    $d.Category = $category
    return $d
}

Invoke-TestCase "1. 3554:FA09 不再被当作 VGN 私有协议端点" {
    $mProto = $tVgn.GetMethod("IsVgnProtocolEndpoint", [System.Reflection.BindingFlags]"Static,Public")
    $isVgnFa09 = $mProto.Invoke($null, @([int]0x3554, [int]0xFA09, [int]0xFF02, [int]0x0002, [int]20, [int]20))
    Assert-True (-not [bool]$isVgnFa09) "3554:FA09 rejected as VGN protocol endpoint"
    $isVgnMouse = $mProto.Invoke($null, @([int]0x3554, [int]0x1001, [int]0xFF02, [int]0x0002, [int]17, [int]17))
    Assert-True ([bool]$isVgnMouse) "normal VGN 3554:1001 endpoint still accepted"
}

Invoke-TestCase "2. VGN 端点路径守卫统一走 IsVgnCandidatePath 并排除 pid_fa09" {
    $vgnSrc = [System.IO.File]::ReadAllText((Join-Path $root "src\Hardware\VgnDeviceHelper.cs"), [System.Text.Encoding]::UTF8)
    Assert-True ($vgnSrc.Contains("pid_fa09")) "FA09 path exclusion present in VgnDeviceHelper"
    $guardCalls = ([regex]::Matches($vgnSrc, "IsVgnCandidatePath\(devicePath")).Count
    Assert-Equal 4 $guardCalls "all four VGN endpoint guards routed through IsVgnCandidatePath"
    $bareNeedle = 'devicePath.ToLower().Contains("vid_3554")'
    $bareGuard = ([regex]::Matches($vgnSrc, [regex]::Escape($bareNeedle))).Count
    Assert-Equal 0 $bareGuard "no inline devicePath guard inspects vid_3554 directly"
    $helperNeedle = 'lower.Contains("vid_3554")'
    Assert-Equal 1 (([regex]::Matches($vgnSrc, [regex]::Escape($helperNeedle))).Count) "IsVgnCandidatePath owns the vid_3554 check"
}

Invoke-TestCase "3. KZZI 键盘经严格身份校验后仍被 BuiltIn:KzziKeyboard 接管" {
    $kzzi = New-Device "KZZI:3554:FA09" ([RazerBatteryTray.DeviceCategory]::Keyboard) "KZZI"
    $kzzi.DriverId = "BuiltIn:KzziKeyboard"
    $resolved = $mResolve.Invoke($null, @($kzzi))
    Assert-True ($null -ne $resolved) "KZZI keyboard resolved to a driver"
    Assert-Equal "BuiltIn:KzziKeyboard" $resolved.DriverId "KZZI keyboard keeps its own driver"
}

Invoke-TestCase "4. 同 PID 的其它设备不会被 KZZI 驱动吞掉" {
    # 同接收器裂出的 VGN 幽灵卡：Category=Mouse，绝不能判给 KZZI 键盘驱动
    $ghost = New-Device "VGN:3554:FA09" ([RazerBatteryTray.DeviceCategory]::Mouse) "VGN"
    $rGhost = $mResolve.Invoke($null, @($ghost))
    Assert-True ($null -eq $rGhost -or $rGhost.DriverId -ne "BuiltIn:KzziKeyboard") "VGN ghost card not claimed by KZZI driver"

    # MCHOSE G75 PRO 也共用 3554:FA09：键盘品类必须归 MCHOSE 驱动
    $mchose = New-Device "MCHOSE:3554:FA09" ([RazerBatteryTray.DeviceCategory]::Keyboard) "MCHOSE"
    $rMchose = $mResolve.Invoke($null, @($mchose))
    Assert-True ($null -eq $rMchose -or $rMchose.DriverId -ne "BuiltIn:KzziKeyboard") "MCHOSE G75 PRO not claimed by KZZI driver"
}

Write-Host ""
Write-Host "ALL SHARED-PID IDENTITY TESTS PASSED." -ForegroundColor Green
exit 0
