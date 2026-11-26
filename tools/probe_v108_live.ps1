# Probe: live V108 battery read through the compiled FerrisPulse assembly.
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$exe = Join-Path $root "FerrisPulse.exe"

$bytes = [System.IO.File]::ReadAllBytes($exe)
$asm = [System.Reflection.Assembly]::Load($bytes)

$type = $asm.GetType("RazerBatteryTray.VgnKeyboardHelper")
if ($null -eq $type) { throw "VgnKeyboardHelper not found" }

$info = $type.GetMethod("QueryVgnV108Info").Invoke($null, $null)
if ($null -eq $info) { "RESULT: null (endpoint not found)" ; exit 0 }

"DeviceId      = " + $info.DeviceId
"HardwareFp    = " + $info.HardwareFingerprint
"DeviceName    = " + $info.DeviceName
"Brand         = " + $info.Brand
"Category      = " + $info.Category
"Transport     = " + $info.Transport
"BatteryPercent= " + $info.BatteryPercent
"IsCharging    = " + $info.IsCharging
"IsChargingUnk = " + $info.IsChargingUnknown
"IsConnected   = " + $info.IsConnected
"IsSleeping    = " + $info.IsSleeping
"IsDongle      = " + $info.IsDonglePresent
"LastUpdated   = " + $info.LastUpdated
"Tooltip       = " + $info.CustomTooltip
"Caps.Dpi      = " + $info.Capabilities.SupportsDpi
"Caps.Polling  = " + $info.Capabilities.SupportsPollingRate
