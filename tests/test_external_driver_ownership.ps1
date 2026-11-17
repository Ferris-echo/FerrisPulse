# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# Offline contract test: exact endpoint ownership and retained script instances.
$ErrorActionPreference = 'Stop'
$root = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$a = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $root 'FerrisPulse.exe')))
[Environment]::CurrentDirectory = $root
$registry = $a.GetType('RazerBatteryTray.HardwareDriverRegistry')
$query = $registry.GetMethod('QueryExternalDevices')
if (!$query) { throw 'Missing script-first endpoint ownership API' }
$source = @'
using System;
using System.Collections.Generic;
using System.Threading;
using RazerBatteryTray;
public sealed class OwnershipExample : HardwareDriverBase {
 public override string DriverId { get { return "Script:OwnershipExample"; } }
 public override string Brand { get { return "Fixture"; } }
 public override bool IsExternalPackage { get { return true; } }
 public int Calls;
 public bool Throw;
 public bool EmptyClaims;
 public bool ThrowClaims;
 public bool NoBattery;
 public static int InFlight;
 public static int MaxInFlight;
 public static void ConcurrentScans() {
  MaxInFlight=0;
  Thread first=new Thread(delegate() { HardwareDriverRegistry.QueryExternalDevices(); });
  Thread second=new Thread(delegate() { HardwareDriverRegistry.QueryExternalDevices(); });
  first.Start(); second.Start(); first.Join(); second.Join();
 }
 public string[] ClaimedDevicePaths { get { if (ThrowClaims) throw new InvalidOperationException("claims"); return EmptyClaims ? new string[0] : new string[] { "hid#owned20", "hid#owned7" }; } }
 public override bool MatchesVidPid(int v,int p) { return v==0x046D && p==0xC54D; }
 public override bool CanHandle(MouseBatteryInfo d) { return d!=null && d.DeviceName=="Verified model"; }
 public override List<MouseBatteryInfo> QueryDevices() {
  int busy=Interlocked.Increment(ref InFlight);
  int previous=MaxInFlight;
  while (busy>previous) { int observed=Interlocked.CompareExchange(ref MaxInFlight,busy,previous); if(observed==previous) break; previous=observed; }
  Thread.Sleep(20); Interlocked.Decrement(ref InFlight);
  Calls++; if(Throw) throw new InvalidOperationException("fixture");
  if(NoBattery) return new List<MouseBatteryInfo>();
  return new List<MouseBatteryInfo> { new MouseBatteryInfo { DeviceId="Logitech:046D:C54D", HardwareId="hid#owned20", DeviceName="Verified model", DriverId=DriverId, IsConnected=true, Category=DeviceCategory.Mouse } };
 }
}
public sealed class FallbackExample : HardwareDriverBase {
 public override string DriverId { get { return "BuiltIn:OwnershipFallback"; } }
 public override string Brand { get { return "Fixture"; } }
 public override bool MatchesVidPid(int v,int p) { return v==0x046D && p==0xC54D; }
 public override bool CanHandle(MouseBatteryInfo d) { return true; }
}
'@
$argsList = [object[]]@($source,'ownership-fixture.fp-driver.cs',$null)
$drivers = $a.GetType('RazerBatteryTray.CsScriptDriverLoader').GetMethod('CompileDriversFromSource').Invoke($null,$argsList)
if ($argsList[2].Count -or $drivers.Count -ne 2) { throw ($argsList[2] -join '; ') }
$external = @($drivers | Where-Object IsExternalPackage)[0]
foreach ($driver in $drivers) { [RazerBatteryTray.HardwareDriverRegistry]::RegisterDriver($driver) }
$result = @([RazerBatteryTray.HardwareDriverRegistry]::QueryExternalDevices())
if ($external.Calls -ne 1 -or $result.Count -ne 1) { throw 'Script must be queried once per scan' }
foreach ($path in 'hid#owned20','HID#OWNED7') {
 if (![RazerBatteryTray.HardwareDriverRegistry]::IsExternallyOwnedPath($path)) { throw 'Missing exact sibling claim' }
}
if ([RazerBatteryTray.HardwareDriverRegistry]::IsExternallyOwnedPath('hid#another-c54d')) { throw 'Ownership escaped physical endpoint' }
$other = New-Object RazerBatteryTray.MouseBatteryInfo
$other.DeviceId='Logitech:046D:C54D'; $other.DeviceName='Different paired model'; $other.DriverId=$external.DriverId
if ([RazerBatteryTray.HardwareDriverRegistry]::ResolveDriver($other).IsExternalPackage) { throw 'Shared PID overrode paired identity check' }
$other.HardwareId='hid#another-c54d'; $other.Category='Mouse'
$list=New-Object 'System.Collections.Generic.List[RazerBatteryTray.MouseBatteryInfo]'
$list.Add($other)
[RazerBatteryTray.DeviceManager]::MergeExternalDriverDevice($list,$result[0])
if ($list.Count -ne 2) { throw 'Two explicit physical endpoints were merged by shared PID' }
$other.HardwareId=$null
$list.Clear(); $list.Add($other)
[RazerBatteryTray.DeviceManager]::MergeExternalDriverDevice($list,$result[0])
if ($list.Count -ne 2) { throw 'Foreign paired model without a path was overwritten by equal logical ID' }
$usb=New-Object RazerBatteryTray.MouseBatteryInfo
$usb.DeviceId='Test:1234:5678'; $usb.PhysicalUnitId='TestUnit'; $usb.IsConnected=$true; $usb.BatteryPercent=89; $usb.IsCharging=$true
$rf=New-Object RazerBatteryTray.MouseBatteryInfo
$rf.DeviceId='Test:1234:5679'; $rf.PhysicalUnitId='TestUnit'; $rf.IsConnected=$true; $rf.IsDonglePresent=$true; $rf.IsSleeping=$true; $rf.BatteryPercent=70
foreach($reverse in $false,$true) {
 $list.Clear()
 if($reverse) {$list.Add($rf); [RazerBatteryTray.DeviceManager]::MergeExternalDriverDevice($list,$usb)}
 else {$list.Add($usb); [RazerBatteryTray.DeviceManager]::MergeExternalDriverDevice($list,$rf)}
 if($list.Count -ne 1 -or $list[0].IsSleeping -or !$list[0].IsCharging -or $list[0].BatteryPercent -ne 89) {throw 'Cached RF replaced live USB because of enumeration order'}
}
$external.ThrowClaims=$true
$broken=@([RazerBatteryTray.HardwareDriverRegistry]::QueryExternalDevices())
if($broken.Count -or [RazerBatteryTray.HardwareDriverRegistry]::IsExternallyOwnedPath('hid#owned20')) {throw 'Failed claims getter leaked partial package output'}
$external.ThrowClaims=$false
$null=[RazerBatteryTray.HardwareDriverRegistry]::QueryExternalDevices()
$external.NoBattery=$true
$pending=@([RazerBatteryTray.HardwareDriverRegistry]::QueryExternalDevices())
if ($pending.Count -or ![RazerBatteryTray.HardwareDriverRegistry]::IsExternallyOwnedPath('hid#owned20')) { throw 'Verified identity with no battery sample lost its explicit endpoint claim' }
$external.NoBattery=$false
[RazerBatteryTray.HardwareDriverRegistry]::UnregisterDriverInstance($external) | Out-Null
if ([RazerBatteryTray.HardwareDriverRegistry]::IsExternallyOwnedPath('hid#owned20')) { throw 'Unregister left stale path ownership' }
[RazerBatteryTray.HardwareDriverRegistry]::RegisterDriver($external)
$external.Throw=$true
$result=@([RazerBatteryTray.HardwareDriverRegistry]::QueryExternalDevices())
if ($result.Count -or [RazerBatteryTray.HardwareDriverRegistry]::IsExternallyOwnedPath('hid#owned20')) { throw 'Failed query retained stale endpoint claim' }
$external.Throw=$false; $external.EmptyClaims=$true
$null=[RazerBatteryTray.HardwareDriverRegistry]::QueryExternalDevices()
if ([RazerBatteryTray.HardwareDriverRegistry]::IsExternallyOwnedPath('hid#owned20')) { throw 'Explicit empty claims were replaced with HardwareId ownership' }
$replacement=[Activator]::CreateInstance($external.GetType())
[RazerBatteryTray.HardwareDriverRegistry]::RegisterDriver($replacement)
$null=[RazerBatteryTray.HardwareDriverRegistry]::QueryExternalDevices()
if ([RazerBatteryTray.HardwareDriverRegistry]::UnregisterDriverInstance($external)) { throw 'Stale instance removed newer replacement' }
if (![RazerBatteryTray.HardwareDriverRegistry]::IsExternallyOwnedPath('hid#owned20')) { throw 'Stale removal revoked replacement ownership' }
$replacement.GetType().GetMethod('ConcurrentScans').Invoke($null,@())
if ($replacement.GetType().GetField('MaxInFlight').GetValue($null) -ne 1) { throw 'Concurrent scans raced endpoint ownership' }
[RazerBatteryTray.HardwareDriverRegistry]::UnregisterDriverInstance($replacement) | Out-Null
Write-Host 'PASS exact endpoint claims, shared PID routing and scan freshness'

$loader=$a.GetType('RazerBatteryTray.CsScriptDriverLoader')
$load=$loader.GetMethod('LoadAndRegisterScriptFiles',[Reflection.BindingFlags]'Static,NonPublic')
if (!$load) { throw 'Missing isolated script cache loader' }
$temp=Join-Path $env:TEMP ('fp-script-cache-'+[Guid]::NewGuid().ToString('N'))
[IO.Directory]::CreateDirectory($temp) | Out-Null
$file=Join-Path $temp 'cache.fp-driver.cs'
$cacheSource=@'
using RazerBatteryTray;
public sealed class CachedExample : HardwareDriverBase {
 public override string DriverId { get { return "Script:CacheFixture"; } }
 public override string Brand { get { return "CacheFixture"; } }
 public override bool IsExternalPackage { get { return true; } }
 public int Version { get { return 1; } }
}
'@
function Load-Files([string[]]$files) {
 $argsList=[object[]]@($files,$null)
 $value=$load.Invoke($null,$argsList)
 [pscustomobject]@{ Drivers=$value; Errors=$argsList[1] }
}
try {
 [IO.File]::WriteAllText($file,$cacheSource,(New-Object Text.UTF8Encoding($true)))
 $first=Load-Files @($file); $second=Load-Files @($file)
 if ($first.Errors.Count -or ![object]::ReferenceEquals($first.Drivers[0],$second.Drivers[0])) { throw 'Unchanged script recompiled and lost its cache instance' }
 $stamp=[IO.File]::GetLastWriteTimeUtc($file)
 [IO.File]::WriteAllText($file,$cacheSource.Replace('return 1;','return 2;'),(New-Object Text.UTF8Encoding($true)))
 [IO.File]::SetLastWriteTimeUtc($file,$stamp)
 $changed=Load-Files @($file)
 if ($changed.Drivers[0].Version -ne 2 -or [object]::ReferenceEquals($changed.Drivers[0],$first.Drivers[0])) { throw 'Same-size same-time source update ignored' }
 [IO.File]::WriteAllText($file,'invalid C# source',(New-Object Text.UTF8Encoding($true)))
 $invalid=Load-Files @($file)
 if (!$invalid.Errors.Count -or $invalid.Drivers.Count) { throw 'Invalid update silently retained old driver' }
 if (@([RazerBatteryTray.HardwareDriverRegistry]::GetAllDriversOrdered() | Where-Object DriverId -eq 'Script:CacheFixture').Count) { throw 'Invalid update kept old registry entry' }
 [IO.File]::WriteAllText($file,$cacheSource,(New-Object Text.UTF8Encoding($true)))
 $restored=Load-Files @($file)
 $empty=Load-Files @()
 if (@([RazerBatteryTray.HardwareDriverRegistry]::GetAllDriversOrdered() | Where-Object DriverId -eq 'Script:CacheFixture').Count) { throw 'Removed script remained registered' }
} finally {
 if ([IO.File]::Exists($file)) { [IO.File]::Delete($file) }
 [IO.Directory]::Delete($temp)
}
Write-Host 'PASS script cache reuse, content invalidation and removal'
