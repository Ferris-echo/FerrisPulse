$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '../FerrisPulse.exe')))

$pinvokeDef = @"
using System;
using System.Runtime.InteropServices;

public static class PipeHelper {
    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
    public static extern IntPtr CreateNamedPipe(
        string lpName,
        uint dwOpenMode,
        uint dwPipeMode,
        uint nMaxInstances,
        uint nOutBufferSize,
        uint nInBufferSize,
        uint nDefaultTimeOut,
        IntPtr lpSecurityAttributes);

    [DllImport("kernel32.dll", SetLastError = true)]
    public static extern bool CloseHandle(IntPtr hObject);
}
"@

Add-Type -TypeDefinition $pinvokeDef

$hidTransport = $assembly.GetType("RazerBatteryTray.HidTransport")
$readReport = $hidTransport.GetMethod("ReadReport", [Reflection.BindingFlags]"Public,Static", $null, @([IntPtr], [int], [int], [byte[]].MakeByRefType()), $null)
$writeReport = $hidTransport.GetMethod("WriteReport", [Reflection.BindingFlags]"Public,Static")

$pipeName = "\\.\pipe\FerrisPulse-test-" + [Guid]::NewGuid().ToString("N")
# PIPE_ACCESS_DUPLEX (3) | FILE_FLAG_OVERLAPPED (0x40000000)
$handle = [PipeHelper]::CreateNamedPipe($pipeName, 0x40000003, 0, 1, 4096, 4096, 1000, [IntPtr]::Zero)
if ($handle -eq [IntPtr]::Zero -or $handle -eq [IntPtr]-1) {
    throw "Failed to create named pipe"
}

$client = New-Object IO.Pipes.NamedPipeClientStream('.',$pipeName.Replace("\\.\pipe\",""),[IO.Pipes.PipeDirection]::InOut,[IO.Pipes.PipeOptions]::Asynchronous)
try {
    $client.Connect(1000)
    
    foreach ($attempt in 1..8) {
        $p = [object[]]@($handle, 64, 5, $null)
        if ([bool]$readReport.Invoke($null, $p)) { throw 'Empty pipe unexpectedly readable' }
        [GC]::Collect()
    }
    [byte[]]$payload = @(4,1,26)
    $writeTask = $client.WriteAsync($payload,0,3)
    $p = [object[]]@($handle, 64, 200, $null)
    if (-not [bool]$readReport.Invoke($null, $p)) { throw 'Read after cancelled I/O failed' }
    $response = [byte[]]$p[3]
    if (-not $writeTask.Wait(500)) { throw 'Client write did not complete' }
    if ($response.Length -ne 3 -or $response[2] -ne 26) { throw 'Truncated reads must not be padded into valid protocol frames' }
    $received = New-Object byte[] 3
    $readTask = $client.ReadAsync($received,0,3)
    if (-not [bool]$writeReport.Invoke($null, @($handle, $payload, 3, 200))) { throw 'Overlapped write failed' }
    if (-not $readTask.Wait(500) -or $readTask.Result -ne 3 -or $received[2] -ne 26) { throw 'Write payload mismatch' }
} finally {
    try { $client.Dispose() } catch { }
    [PipeHelper]::CloseHandle($handle) | Out-Null
}
Write-Host 'PASS: overlapped cancellation, pinned buffers, real read length, write completion'
