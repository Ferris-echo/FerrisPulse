$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '../FerrisPulse.exe')))
$pipeName = 'FerrisPulse-test-' + [Guid]::NewGuid().ToString('N')
$server = New-Object IO.Pipes.NamedPipeServerStream($pipeName,[IO.Pipes.PipeDirection]::InOut,1,[IO.Pipes.PipeTransmissionMode]::Byte,[IO.Pipes.PipeOptions]::Asynchronous)
$client = New-Object IO.Pipes.NamedPipeClientStream('.',$pipeName,[IO.Pipes.PipeDirection]::InOut,[IO.Pipes.PipeOptions]::Asynchronous)
try {
    $client.Connect(1000)
    $server.WaitForConnection()
    $handle = $server.SafePipeHandle.DangerousGetHandle()
    foreach ($attempt in 1..8) {
        $response = $null
        if ([RazerBatteryTray.HidTransport]::ReadReport($handle,64,5,[ref]$response)) { throw 'Empty pipe unexpectedly readable' }
        [GC]::Collect()
    }
    [byte[]]$payload = @(4,1,26)
    $writeTask = $client.WriteAsync($payload,0,3)
    $response = $null
    if (-not [RazerBatteryTray.HidTransport]::ReadReport($handle,64,200,[ref]$response)) { throw 'Read after cancelled I/O failed' }
    if (-not $writeTask.Wait(500)) { throw 'Client write did not complete' }
    if ($response.Length -ne 3 -or $response[2] -ne 26) { throw 'Truncated reads must not be padded into valid protocol frames' }
    $received = New-Object byte[] 3
    $readTask = $client.ReadAsync($received,0,3)
    if (-not [RazerBatteryTray.HidTransport]::WriteReport($handle,$payload,3,200)) { throw 'Overlapped write failed' }
    if (-not $readTask.Wait(500) -or $readTask.Result -ne 3 -or $received[2] -ne 26) { throw 'Write payload mismatch' }
} finally {
    $client.Dispose()
    $server.Dispose()
}
Write-Host 'PASS: overlapped cancellation, pinned buffers, real read length, write completion'