$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '../FerrisPulse.exe')))
function Assert($condition, $message) { if (-not $condition) { throw $message } }
Assert ([RazerBatteryTray.RazerDeviceHelper]::GetRazerDisplayName(0x00A6,'Razer Viper V2 Pro').Contains('Viper V2 Pro')) 'Viper V2 Pro PID display'
Assert ([RazerBatteryTray.RazerDeviceHelper]::GetMaxPollingRateForModel(0x00A6,'Razer Viper V2 Pro') -eq 1000) 'Standard receiver max rate'
$parse = $assembly.GetType('RazerBatteryTray.RazerDeviceHelper').GetMethod('ParseDpiStagesReport')
Assert ($null -ne $parse) 'DPI stages parser exists'
$report = New-Object byte[] 91
$report[1]=2; $report[7]=4; $report[8]=0x86
$report[10]=2; $report[11]=2
[byte[]]$stages=@(1,3,32,3,32,0,0,2,7,8,3,132,0,0)
[Array]::Copy($stages,0,$report,12,$stages.Length)
$state=[RazerBatteryTray.RazerDeviceHelper]::ParseDpiStagesReport($report,1)
Assert ($state.Dpi -eq 1800 -and $state.DpiY -eq 900 -and $state.DpiStagesY[0] -eq 800) 'Independent XY stages'
$report[11]=6
Assert ($null -eq [RazerBatteryTray.RazerDeviceHelper]::ParseDpiStagesReport($report,1)) 'Reject invalid stage count'
Write-Host 'PASS: Viper V2 Pro identification, receiver rates, XY stage parsing'