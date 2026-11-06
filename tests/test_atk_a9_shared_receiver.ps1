# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# Shared ATK receiver identity must select A9 only when a valid CID/MID reply is received.
$ErrorActionPreference = 'Stop'
$projectDir = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$exePath = Join-Path $projectDir 'FerrisPulse.exe'
if (-not (Test-Path -LiteralPath $exePath)) { throw 'FerrisPulse.exe not found.' }
[void][Reflection.Assembly]::Load([IO.File]::ReadAllBytes($exePath))

$tx = [RazerBatteryTray.UniversalGamingDeviceHelper]::BuildAtkReceiverIdentityRequest()
if ($tx.Length -ne 17 -or $tx[0] -ne 8 -or $tx[1] -ne 0x10 -or $tx[16] -ne 0x3D) {
    throw 'ATK identity request framing changed.'
}

# The receiver observed on the user's A9 reported CID=1, MID=3. The enclosing
# frame here is constructed from the documented protocol because raw bytes were
# unavailable after the user switched to the direct USB connection.
[byte[]]$a9 = @(0x08,0x10,0,0,0,2,1,3,0,0,0,0,0,0,0,0,0x37)
if (-not [RazerBatteryTray.UniversalGamingDeviceHelper]::IsAtkA9UltimateIdentity($a9)) {
    throw 'Valid A9 CID/MID response was rejected.'
}
[byte[]]$other = $a9.Clone()
$other[7] = 2
$other[16] = 0x38
if ([RazerBatteryTray.UniversalGamingDeviceHelper]::IsAtkA9UltimateIdentity($other)) {
    throw 'Another ATK model was labeled A9.'
}
[byte[]]$badChecksum = $a9.Clone()
$badChecksum[16] = 0
if ([RazerBatteryTray.UniversalGamingDeviceHelper]::IsAtkA9UltimateIdentity($badChecksum)) {
    throw 'Corrupt ATK identity checksum was accepted.'
}
[byte[]]$badStatus = $a9.Clone()
$badStatus[2] = 1
$badStatus[16] = 0x36
if ([RazerBatteryTray.UniversalGamingDeviceHelper]::IsAtkA9UltimateIdentity($badStatus)) {
    throw 'Failed ATK identity status was accepted.'
}
Write-Host 'PASS: A9 receiver identity request, CID/MID, checksum, and status'
