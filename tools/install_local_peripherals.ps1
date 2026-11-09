# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# Validate both reviewed packages before touching installation; preserve exact old files.
param(
    [Parameter(Mandatory=$true)][string]$HostExe,
    [string]$SourceDirectory = (Join-Path $PSScriptRoot '../packages'),
    [string]$Destination = (Join-Path ([Environment]::GetFolderPath('ApplicationData')) 'FerrisPulse/adapters'),
    [switch]$ValidateOnly
)
$ErrorActionPreference='Stop'
$hostPath=(Resolve-Path -LiteralPath $HostExe).Path
$hostHash=(Get-FileHash -LiteralPath $hostPath -Algorithm SHA256).Hash
$assemblyKey='FerrisPulseInstallerHost:'+ $hostHash
$assembly=[AppDomain]::CurrentDomain.GetData($assemblyKey)
if (!$assembly) {
    $assembly=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes($hostPath))
    [AppDomain]::CurrentDomain.SetData($assemblyKey,$assembly)
}
$oldDirectory=[Environment]::CurrentDirectory
$packages=@()
try {
    [Environment]::CurrentDirectory=[IO.Path]::GetDirectoryName($hostPath)
    $compiler=$assembly.GetType('RazerBatteryTray.CsScriptDriverLoader').GetMethod('CompileDriversFromSource')
    foreach($item in @(@('keychron_m6','External:KeychronM6'),@('logitech_mr0097','External:LogitechMR0097'))) {
        $name=$item[0]+'.fp-driver.cs'
        $sourcePath=(Resolve-Path -LiteralPath (Join-Path $SourceDirectory $name)).Path
        $bytes=[IO.File]::ReadAllBytes($sourcePath)
        if ($bytes.Length -lt 3 -or $bytes[0] -ne 239 -or $bytes[1] -ne 187 -or $bytes[2] -ne 191) { throw ('Package must be UTF-8 BOM: '+$name) }
        $source=[Text.Encoding]::UTF8.GetString($bytes).TrimStart([char]0xFEFF)
        $argsList=[object[]]@($source,$sourcePath,$null)
        $drivers=$compiler.Invoke($null,$argsList)
        if ($argsList[2].Count -or $drivers.Count -ne 1) { throw ('Package compile failed: '+$name+'; '+($argsList[2] -join '; ')) }
        $driver=$drivers[0]
        if (!$driver.IsExternalPackage -or $driver.Priority -ne 300 -or $driver.DriverId -ne $item[1]) { throw ('Unexpected driver contract: '+$name) }
        $packages+= [pscustomobject]@{ Name=$name; Bytes=$bytes; SHA256=(Get-FileHash -LiteralPath $sourcePath -Algorithm SHA256).Hash }
    }
} finally { [Environment]::CurrentDirectory=$oldDirectory }
if ($ValidateOnly) {
    Write-Host 'PASS both peripheral packages compile against selected host'
    return
}
if (@(Get-Process -Name FerrisPulse -ErrorAction SilentlyContinue | Where-Object {$_.Path -eq $hostPath}).Count) {
    throw 'Close this FerrisPulse instance before replacing its packages.'
}
$destinationPath=[IO.Path]::GetFullPath($Destination)
$backup=Join-Path $destinationPath ('.backups/'+(Get-Date -Format 'yyyyMMdd-HHmmss')+'-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($backup) | Out-Null
$previous=@{}
foreach($package in $packages) {
    $target=Join-Path $destinationPath $package.Name
    $previous[$package.Name]=[IO.File]::Exists($target)
    if ($previous[$package.Name]) { Copy-Item -LiteralPath $target -Destination (Join-Path $backup $package.Name) }
}
try {
    foreach($package in $packages) {
        $target=Join-Path $destinationPath $package.Name
        [IO.File]::WriteAllBytes($target,$package.Bytes)
        if ((Get-FileHash -LiteralPath $target -Algorithm SHA256).Hash -ne $package.SHA256) { throw ('Installed package hash mismatch: '+$package.Name) }
    }
} catch {
    foreach($package in $packages) {
        $target=Join-Path $destinationPath $package.Name
        if ($previous[$package.Name]) { Copy-Item -LiteralPath (Join-Path $backup $package.Name) -Destination $target -Force }
        elseif ([IO.File]::Exists($target)) { [IO.File]::Delete($target) }
    }
    throw
}
$receipt=[pscustomobject]@{
    HostExe=$hostPath
    HostSHA256=$hostHash
    Destination=$destinationPath
    BackupDirectory=$backup
    PreviousPresence=$previous
    Packages=@($packages | Select-Object Name,SHA256)
}
$receipt | ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $backup 'install-receipt.json') -Encoding UTF8
$receipt
