# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# VT3 HID 诊断：只读取鼠标状态/配置，不下发 DPI、回报率或休眠配置。
# 缓存沿用程序行为写入 HKCU；全内存装配，不锁定 FerrisPulse.exe。
param([ValidateRange(1,60)][int]$Seconds = 12)
$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot '..\FerrisPulse.exe'
$a = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Resolve-Path $exe).Path))
$transport = $a.GetType('RazerBatteryTray.HidTransport')
$helper = $a.GetType('RazerBatteryTray.RapooDeviceHelper')
$devices = $transport.GetMethod('Enumerate').Invoke($null,@([int]0x24AE,[int]0))
$devices | Select-Object ProductId,UsagePage,Usage,InputReportLength,OutputReportLength,FeatureReportLength,DevicePath | Format-List
if (!$devices -or $devices.Count -eq 0) { Write-Host '未发现雷柏 HID 设备。'; exit 2 }
Write-Host '正在采样；请打开鼠标、移动或按机身 DPI 键。没有广播不能单独证明已断开。'
$deadline = [DateTime]::UtcNow.AddSeconds($Seconds)
$lastHex = ''
try {
    do {
        $info = $helper.GetMethod('QueryRapooDeviceInfo').Invoke($null,$null)
        if ($null -ne $info) {
            $info | Select-Object DeviceName,Transport,BatteryPercent,IsCharging,IsSleeping,Dpi,DpiY,DpiStage,DpiStageCount,PollingRate,LastUpdated | ConvertTo-Json -Compress
        }
        $hex = $helper.GetField('LastStatusReportHex').GetValue($null)
        if ($hex -and $hex -ne $lastHex) { Write-Output ('RAW ' + $hex); $lastHex = $hex }
        Start-Sleep -Milliseconds 500
    } while ([DateTime]::UtcNow -lt $deadline)
    if (!$lastHex) { Write-Host '本次未收到 0x07 报告；电量/充电/设置生效仍需开机实测。' }
    $diagnostic = $helper.GetField('LastStatusError').GetValue($null)
    if ($diagnostic) { Write-Output ('STATUS_ERROR ' + $diagnostic) }
} finally {
    [void]$helper.GetMethod('CloseRapooStream').Invoke($null,$null)
    Start-Sleep -Milliseconds 500
}
