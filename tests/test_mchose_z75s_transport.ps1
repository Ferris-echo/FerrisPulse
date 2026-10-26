# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# Exercise the real ExchangeZ75sReport -> Win32 transport boundary using a local pipe.
# No HID devices, registry values or user files are touched.
$ErrorActionPreference='Stop'
[Console]::OutputEncoding=New-Object System.Text.UTF8Encoding
$project=(Resolve-Path "$PSScriptRoot\..").Path
$asm=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $project 'FerrisPulse.exe')))
$type=$asm.GetType('RazerBatteryTray.MchoseDeviceHelper',$true)
$exchange=$type.GetMethod('ExchangeZ75sReport',[Reflection.BindingFlags]'NonPublic,Static')
$tx=$type.GetMethod('BuildZ75sBatteryFrame').Invoke($null,@())
[byte[]]$valid=@(0x13,0x4A,1,0,2,0x32,0x10,0,0,0,0,0,0,0,0,0,0,0,0,0xA2)
# These 7 bytes sum to 256: padding the missing 13 bytes with zero manufactures a valid checksum.
[byte[]]$partial=@(0x13,0x4A,1,0,2,0,0xA0)
[byte[]]$unrelated=@(0x13,0x05,1,0,6,0x0A,0,0,0,0,9,0,0,0,0,0,0,0,0,0x32)
$script:count=0
foreach($case in @(
    @{Name='complete physical charging frame'; Bytes=$valid; Expected=$true},
    @{Name='truncated checksum-zero frame'; Bytes=$partial; Expected=$false},
    @{Name='unrelated frame before battery'; Bytes=([byte[]]($unrelated+$valid)); Expected=$true}
)) {
    $pipeName='FerrisPulse-Z75S-'+[Guid]::NewGuid().ToString('N')
    $server=New-Object IO.Pipes.NamedPipeServerStream($pipeName,[IO.Pipes.PipeDirection]::InOut,1,[IO.Pipes.PipeTransmissionMode]::Byte,[IO.Pipes.PipeOptions]::Asynchronous)
    $client=New-Object IO.Pipes.NamedPipeClientStream('.',$pipeName,[IO.Pipes.PipeDirection]::InOut,[IO.Pipes.PipeOptions]::Asynchronous)
    try {
        $pending=$server.BeginWaitForConnection($null,$null)
        $client.Connect(1000)
        $server.EndWaitForConnection($pending)
        [byte[]]$bytes=$case.Bytes
        Write-Host ("Testing " + $case.Name)
        # Queue both peer directions before the native exchange; zero-buffer pipes
        # may block a synchronous Write until the other side starts reading.
        $commandBuffer=New-Object byte[] 20
        $peerRead=$client.BeginRead($commandBuffer,0,20,$null,$null)
        $peerWrite=$client.BeginWrite($bytes,0,$bytes.Length,$null,$null)
        $argsList=New-Object object[] 4
        $argsList[0]=$server.SafePipeHandle.DangerousGetHandle(); $argsList[1]=$tx
        $argsList[2]=[Diagnostics.Stopwatch]::StartNew(); $argsList[3]=$null
        $ok=$exchange.Invoke($null,$argsList)
        if($ok -ne $case.Expected){throw "FAIL: $($case.Name): accepted=$ok, expected=$($case.Expected)"}
        if($ok -and [BitConverter]::ToString($argsList[3]) -ne [BitConverter]::ToString($valid)){throw 'FAIL: returned a different response'}
        if(-not $peerRead.AsyncWaitHandle.WaitOne(1000) -or -not $peerWrite.AsyncWaitHandle.WaitOne(1000)){throw 'FAIL: pipe peer did not complete'}
        $received=$client.EndRead($peerRead); $client.EndWrite($peerWrite)
        if($received -ne 20 -or [BitConverter]::ToString($commandBuffer) -ne [BitConverter]::ToString($tx)){throw 'FAIL: wrong command at transport boundary'}
        $script:count++
    } finally { $client.Dispose(); $server.Dispose() }
}
Write-Host "PASS: Z75S real transport boundary ($script:count cases)"
