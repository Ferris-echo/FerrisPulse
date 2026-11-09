# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# Installer fixtures use temporary folders only; do not touch user's adapters.
$ErrorActionPreference='Stop'
$root=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$installer=Join-Path $root 'tools/install_local_peripherals.ps1'
if (!(Test-Path -LiteralPath $installer)) { throw 'Missing reviewed-package installer' }
$temp=Join-Path $env:TEMP ('fp-package-install-'+[Guid]::NewGuid().ToString('N'))
$source=Join-Path $temp 'source'
$destination=Join-Path $temp 'adapters'
[IO.Directory]::CreateDirectory($source) | Out-Null
[IO.Directory]::CreateDirectory($destination) | Out-Null
[IO.File]::WriteAllText((Join-Path $destination 'unrelated.fp-driver.cs'),'keep')
foreach($name in 'keychron_m6','logitech_mr0097') {
 [IO.File]::WriteAllText((Join-Path $source ($name+'.fp-driver.cs')),'invalid C# source')
 [IO.File]::WriteAllText((Join-Path $destination ($name+'.fp-driver.cs')),'old-'+$name)
}
$failed=$false
try { & $installer -HostExe (Join-Path $root 'FerrisPulse.exe') -SourceDirectory $source -Destination $destination } catch { $failed=$true }
if (!$failed -or [IO.File]::ReadAllText((Join-Path $destination 'keychron_m6.fp-driver.cs')) -ne 'old-keychron_m6') { throw 'Invalid package changed installed files' }
foreach($name in 'keychron_m6','logitech_mr0097') {
 Copy-Item -LiteralPath (Join-Path $root ('packages/'+$name+'.fp-driver.cs')) -Destination $source
}
& $installer -HostExe (Join-Path $root 'FerrisPulse.exe') -SourceDirectory $source -Destination $destination -ValidateOnly
if ([IO.File]::ReadAllText((Join-Path $destination 'keychron_m6.fp-driver.cs')) -ne 'old-keychron_m6') { throw 'ValidateOnly modified installation' }
$receipt=& $installer -HostExe (Join-Path $root 'FerrisPulse.exe') -SourceDirectory $source -Destination $destination
foreach($name in 'keychron_m6','logitech_mr0097') {
 $file=$name+'.fp-driver.cs'
 if ((Get-FileHash (Join-Path $source $file)).Hash -ne (Get-FileHash (Join-Path $destination $file)).Hash) { throw 'Installed hash mismatch' }
 if ([IO.File]::ReadAllText((Join-Path $receipt.BackupDirectory $file)) -ne ('old-'+$name)) { throw 'Original package backup missing' }
}
if ([IO.File]::ReadAllText((Join-Path $destination 'unrelated.fp-driver.cs')) -ne 'keep') { throw 'Unrelated package changed' }
$rollback=Join-Path $temp 'rollback'
[IO.Directory]::CreateDirectory($rollback) | Out-Null
[IO.File]::WriteAllText((Join-Path $rollback 'keychron_m6.fp-driver.cs'),'original-keychron')
# A directory at the second fixed file name forces a failure after the first write.
[IO.Directory]::CreateDirectory((Join-Path $rollback 'logitech_mr0097.fp-driver.cs')) | Out-Null
$failed=$false
try { & $installer -HostExe (Join-Path $root 'FerrisPulse.exe') -SourceDirectory $source -Destination $rollback } catch { $failed=$true }
if (!$failed -or [IO.File]::ReadAllText((Join-Path $rollback 'keychron_m6.fp-driver.cs')) -ne 'original-keychron') { throw 'Partial installation did not roll back first package' }
Write-Host 'PASS installer preflight, validation-only, backup, exact file scope and partial-write rollback'
# Preserve this small temporary fixture as evidence; no recursive deletion.
