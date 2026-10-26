# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# Z75S boundary telemetry must reach real selectors, battery card and tray accurately.
# Skip MainForm constructor to avoid autostart/configuration/HID side effects.
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding
$project=(Resolve-Path "$PSScriptRoot\..").Path
$asm=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $project 'FerrisPulse.exe')))
$main=$asm.GetType('RazerBatteryTray.MainForm',$true)
$form=[Runtime.Serialization.FormatterServices]::GetUninitializedObject($main)
$flags=[Reflection.BindingFlags]'NonPublic,Instance'
$script:failures=@(); $script:checks=0; $script:owned=@()
function Check($condition,[string]$message) {
    $script:checks++
    if(-not $condition){$script:failures+=$message; Write-Host "FAIL: $message"}
}
function Field([string]$name){return $main.GetField($name,$flags)}
function Set-Field([string]$name,$value){(Field $name).SetValue($form,$value)}
function New-Control([string]$fieldName) {
    $item=[Activator]::CreateInstance((Field $fieldName).FieldType)
    Set-Field $fieldName $item
    $script:owned+=,$item
    return $item
}
function Invoke-UI([string]$method,$arguments){$main.GetMethod($method,$flags).Invoke($form,$arguments)|Out-Null}
function Icon-Pixels($icon) {
    $bitmap=$icon.ToBitmap(); $stream=New-Object IO.MemoryStream
    try { $bitmap.Save($stream,[Drawing.Imaging.ImageFormat]::Png); return [Convert]::ToBase64String($stream.ToArray()) }
    finally { $bitmap.Dispose(); $stream.Dispose() }
}
try {
    Set-Field 'dpiScale' ([single]1)
    Set-Field 'userSelectedInterval' 60000
    $selector=New-Control 'focusDeviceSelector'
    $status=New-Control 'statusMenuItem'
    $tray=New-Control 'trayIcon'
    $label=New-Control 'lblBatteryBig'
    $nameLabel=New-Control 'lblDeviceName'
    $updated=New-Control 'lblUpdateTime'
    $pill=New-Control 'pillStatus'
    $bar=New-Control 'barBattery'
    $tile=New-Control 'tileRapooDpi'
    $card=New-Control 'cardPerformance'
    $card.Width=450
    $dpiButton=New-Control 'btnDpiManage'
    $dpiButton.Visible=$true
    $info=[Activator]::CreateInstance($asm.GetType('RazerBatteryTray.MouseBatteryInfo'))
    $info.DeviceId='MCHOSE:258A:01BB'; $info.DeviceName='MCHOSE Z75S'; $info.Brand='MCHOSE'
    $info.Category=[Enum]::Parse($asm.GetType('RazerBatteryTray.DeviceCategory'),'Keyboard')
    $info.Transport='2.4G 无线'; $info.IsConnected=$true; $info.IsPrimary=$true
    $info.LastUpdated=[DateTime]::Parse('2026-10-26T08:00:00')
    Set-Field 'selectedDevice' $info; Set-Field 'lastInfo' $info
    $info.BatteryPercent=0; $info.ExactBatteryPercent=0
    Invoke-UI 'UpdateDeviceTabsUI' @()
    Check ($selector.BatteryText -eq '0%' -and $selector.StatusText -eq '已连接') 'Real 0% must remain a battery reading in selector'
    Invoke-UI 'UpdateStatusDisplay' @()
    Check ($status.Text.Contains('0%') -and -not $status.Text.Contains('免驱')) 'Tray status must show real 0%'
    Invoke-UI 'UpdateUI' @($info,$false)
    Check ($label.Text -eq '0%' -and -not $dpiButton.Visible) 'Battery card shows 0% and hides mouse controls'

    $info.BatteryPercent=100; $info.ExactBatteryPercent=100; $info.IsCharging=$false
    Invoke-UI 'UpdateUI' @($info,$false)
    Check ($status.Text.Contains('100%') -and -not $tray.Text.Contains('充电中')) 'Full but unplugged Z75S must not imply charging'
    $dischargeIcon=Icon-Pixels $tray.Icon
    $info.IsCharging=$true
    Invoke-UI 'UpdateUI' @($info,$false)
    Check ($bar.IsChargingFill -and $tray.Text.Contains('充电')) 'Physical charge flag reaches card and tray tooltip'
    Check ((Icon-Pixels $tray.Icon) -ne $dischargeIcon) 'Tray icon must change with physical charging flag'

    $info.IsSleeping=$true; $info.BatteryPercent=85; $info.ExactBatteryPercent=85
    Invoke-UI 'UpdateUI' @($info,$false)
    Invoke-UI 'UpdateDeviceTabsUI' @()
    Check ($label.Text -eq '85%' -and -not $bar.IsChargingFill -and $selector.StatusText -eq '休眠中') 'Cached sleeping keyboard keeps percentage without live charging animation'
    Check ($tile.Footnote.Contains('休眠')) 'Sleeping keyboard secondary card must not present cached charge as live'
    Check ($tray.Text.Contains('休眠') -and -not $tray.Text.Contains('正在充电')) 'Sleeping keyboard tray preserves stale-sample semantics'

    # The menu must never offer another keyboard brand's hardware write actions.
    $menu=New-Control 'contextMenu'
    $sep=New-Control 'deviceMenuSep'
    $menu.Items.Add($sep)|Out-Null
    $nuphySleep=New-Control 'nuphySleepMenuItem'
    Set-Field 'dynamicDeviceMenuItems' ([Activator]::CreateInstance((Field 'dynamicDeviceMenuItems').FieldType))
    $devices=[Activator]::CreateInstance((Field 'activeDevices').FieldType)
    $devices.Add($info); Set-Field 'activeDevices' $devices
    $info.IsSleeping=$false; $info.IsCharging=$false; $info.BatteryPercent=0
    Invoke-UI 'UpdateTrayDeviceMenuItems' @()
    Check (-not $nuphySleep.Available) 'Z75S must not expose NuPhy hardware write actions'
    Check ($menu.Items[0].Text.Contains('0%')) 'Dynamic keyboard menu retains real zero percentage'
    $info.IsSleeping=$true; $info.IsCharging=$true; $info.BatteryPercent=85
    Invoke-UI 'UpdateTrayDeviceMenuItems' @()
    Check ($menu.Items[0].Text.Contains('85%') -and $menu.Items[0].Text.Contains('休眠')) 'Dynamic menu marks cached charge state as sleeping'

    $info.IsSleeping=$false; $info.IsCharging=$false; $info.BatteryPercent=-1
    Invoke-UI 'UpdateUI' @($info,$false)
    Invoke-UI 'UpdateDeviceTabsUI' @()
    Check ($label.Text -eq '--' -and $selector.BatteryText -ne '0%') 'Unknown battery must remain distinct from zero'
} finally {
    # Controls were never parented to the uninitialized form.
    foreach($item in $script:owned){if($item -is [IDisposable]){$item.Dispose()}}
}
if($script:failures.Count -gt 0){throw "$($script:failures.Count) Z75S UI assertions failed"}
Write-Host "PASS: Z75S UI ($script:checks assertions)"
