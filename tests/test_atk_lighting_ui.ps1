$ErrorActionPreference='Stop'
$assembly=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '../FerrisPulse.exe')))
function Assert($condition,$message) { if (-not $condition) { throw $message } }
$flags=[Reflection.BindingFlags]'Instance,NonPublic'
$dialogType=$assembly.GetType('RazerBatteryTray.AtkLightingDialog',$true)
$dialog=[Activator]::CreateInstance($dialogType,@('ATK:320F:5088',[single]1))
try {
    [byte[]]$config=New-Object byte[] 56
    $config[1]=6; $config[2]=2; $config[3]=3
    $settings=$assembly.GetType('RazerBatteryTray.AtkDeviceHelper', $true).GetMethod('DecodeLightingConfig').Invoke($null, [object[]]@(,$config))
    $dialogType.GetMethod('DisplaySettings',$flags).Invoke($dialog,@($settings))
    $mode=$dialogType.GetField('modeBox',$flags).GetValue($dialog)
    $speed=$dialogType.GetField('speedBox',$flags).GetValue($dialog)
    $color=$dialogType.GetField('colorButton',$flags).GetValue($dialog)
    $brightness=$dialogType.GetField('brightnessBox',$flags).GetValue($dialog)
    $apply=$dialogType.GetField('applyButton',$flags).GetValue($dialog)
    Assert ($mode.Items.Count -eq 20 -and $mode.SelectedIndex -eq 5) '20 official effects including existing custom and off'
    Assert (-not $speed.Enabled -and $color.Enabled -and $brightness.Enabled) 'Steady supports color but not speed'
    $mode.SelectedIndex=4
    Assert $speed.Enabled 'Breathing supports speed'
    $mode.SelectedIndex=10
    Assert (-not $color.Enabled) 'Fixed-color effect disables unsupported color'
    $mode.SelectedIndex=19
    Assert (-not $brightness.Enabled -and -not $speed.Enabled -and -not $color.Enabled) 'Off effect disables unsupported options'
    $dialogType.GetMethod('SetBusy',$flags).Invoke($dialog,@($true))
    Assert (-not $apply.Enabled -and -not $mode.Enabled) 'No concurrent apply while busy'
    $dialogType.GetMethod('SetBusy',$flags).Invoke($dialog,@($false))
    foreach ($scale in @([single]1,[single]1,[single]1.5,[single]1.5,[single]2)) {
        $dialogType.GetMethod('OnApplyDpiScale',$flags).Invoke($dialog,@($scale))
        foreach ($control in $dialog.Controls) {
            Assert ($control.Right -le $dialog.ClientSize.Width -and $control.Bottom -le $dialog.ClientSize.Height) 'All dialog controls fit at each DPI'
            Assert ($control.Font.GetHeight() -gt 0) 'Repeated DPI layout must not dispose an active equal font'
        }
    }
} finally { $dialog.Dispose() }
$mainType=$assembly.GetType('RazerBatteryTray.MainForm',$true)
$main=[Activator]::CreateInstance($mainType,@($false))
try {
    $button=$mainType.GetField('btnAtkLighting',$flags).GetValue($main)
    $windows=$mainType.GetField('btnKeyboardSettings',$flags).GetValue($main)
    $update=$mainType.GetMethod('UpdatePerformanceSection',$flags)
    $visibleState=[System.Windows.Forms.Control].GetMethod('GetState',$flags)
    $info=[Activator]::CreateInstance($assembly.GetType('RazerBatteryTray.MouseBatteryInfo',$true))
    $info.Brand='ATK'; $info.DeviceId='ATK:320F:5088'; $info.Category=[RazerBatteryTray.DeviceCategory]::Keyboard
    $info.DeviceName='ATK V75X'; $info.BatteryPercent=87; $info.IsConnected=$true; $info.Transport='2.4G 无线'
    $null=$update.Invoke($main,@($info,[Drawing.Color]::Gold))
    Assert ($visibleState.Invoke($button,@(2)) -and $button.Enabled) 'Confirmed ATK receiver exposes lighting entry'
    Assert ($windows.Right -lt $button.Left -and $button.Bottom -le $button.Parent.Height) 'Keyboard buttons do not overlap'
    $info.IsConnected=$false
    $null=$update.Invoke($main,@($info,[Drawing.Color]::Gold))
    Assert (-not $button.Enabled) 'Disconnected keyboard cannot write'
    $info.IsConnected=$true; $info.DeviceId='ATK:320F:5055'
    $null=$update.Invoke($main,@($info,[Drawing.Color]::Gold))
    Assert (-not $visibleState.Invoke($button,@(2))) 'Untested wired keyboard has no lighting entry'
    Assert ($windows.Text.Contains('打开 Windows')) 'Generic keyboard layout restored'
    $info.Brand='Sony'; $info.Category=[RazerBatteryTray.DeviceCategory]::Gamepad; $info.DeviceId='Sony:054C:0DF2'
    $null=$update.Invoke($main,@($info,[Drawing.Color]::Blue))
    Assert (-not $visibleState.Invoke($button,@(2))) 'No lighting entry leaks into gamepad'
} finally { $main.Dispose() }
$source=[IO.File]::ReadAllText((Join-Path $PSScriptRoot '../src/UI/Forms/MainForm.Hardware.cs'))
Assert ($source.Contains('btnAtkLighting.Visible = false') -and $source.Contains('ConfigureAtkLightingControl(info,')) 'Device-switch visibility and keyboard entry'
Write-Host 'PASS: ATK lighting dialog capabilities, busy state, effects, DPI layout and main entry'