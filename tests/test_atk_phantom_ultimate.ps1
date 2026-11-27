# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
$ErrorActionPreference = 'Stop'
$exe = Join-Path $PSScriptRoot '../FerrisPulse.exe'
$assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes($exe))
$helper = $assembly.GetType('RazerBatteryTray.AtkPhantomDeviceHelper', $true)
$flags = [Reflection.BindingFlags]'Static,NonPublic'
$build = $helper.GetMethod('BuildRequest', $flags)
$identity = $helper.GetMethod('IsPhantomIdentity', $flags)
$response = $helper.GetMethod('IsResponse', $flags)

$report = $build.Invoke($null, @([byte]0x4A))
if ($report.Length -ne 64 -or [BitConverter]::ToString($report, 0, 8) -ne '08-CB-72-08-00-01-4A-06') {
    throw 'Unexpected Phantom identity request.'
}

[byte[]]$frame = New-Object byte[] 64
[byte[]]$capture = 0x08,0x72,0x00,0x08,0x00,0x4A,0x06,0x00,0x01,0x15,0,0,0
$capture.CopyTo($frame, 0)
if (!$identity.Invoke($null, (, $frame))) { throw 'Expected live Phantom CID/MID 1/21.' }
$frame[9] = 10
if (!$identity.Invoke($null, (, $frame))) { throw 'Expected catalog Phantom CID/MID 1/10.' }
$frame[9] = 3
if ($identity.Invoke($null, (, $frame))) { throw 'Shared receiver misidentified another mouse.' }
$frame[9] = 21
$frame[2] = 0xFF
if ($identity.Invoke($null, (, $frame))) { throw 'Accepted a failed identity reply.' }

[Array]::Clear($frame, 0, $frame.Length)
[byte[]]$notification = 0x08,0x72,0x02
$notification.CopyTo($frame, 0)
if ($response.Invoke($null, @($frame, [byte]0x07, 1))) { throw 'Accepted an unsolicited frame.' }
[byte[]]$battery = 0x08,0x72,0x00,0x03,0x00,0x07,0x01,0x49
$battery.CopyTo($frame, 0)
if (!$response.Invoke($null, @($frame, [byte]0x07, 1))) { throw 'Rejected the captured battery reply.' }
if ($response.Invoke($null, @($frame, [byte]0x0F, 1))) { throw 'Accepted a reply for a different command.' }
Write-Host '[PASS] ATK Phantom Ultimate identity, request and response frames.'
