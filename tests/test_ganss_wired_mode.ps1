# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# 有线 / 2.4G 传输判别回归：官方 config.xml 中 USB 模式=产品串 GS3108T-PRO + MI_00，
# 2.4G 模式=产品串 GS3108T-PRO 2.4G + MI_03。必须两种物理身份同时成立才判有线。
$ErrorActionPreference='Stop'
$a=[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '..\FerrisPulse.exe')))
$t=$a.GetType('RazerBatteryTray.GanssDeviceHelper')
if(!$t){throw 'Missing GanssDeviceHelper'}
$dType=$a.GetType('RazerBatteryTray.HidDeviceDescriptor')
function New-Desc([string]$prod,[string]$inst,[int]$up,[int]$usage,[int]$inLen,[int]$outLen,[string]$mfg){
  $d=[Activator]::CreateInstance($dType)
  $d.ProductString=$prod; $d.InstanceId=$inst; $d.UsagePage=$up; $d.Usage=$usage
  $d.InputReportLength=$inLen; $d.OutputReportLength=$outLen; $d.ManufacturerString=$mfg
  return $d
}
function Make-Array($items){
  $arr=[Array]::CreateInstance($dType,$items.Count)
  for($i=0;$i -lt $items.Count;$i++){ $arr.SetValue($items[$i],$i) }
  return ,$arr
}
function Invoke-One($method,$arr){
  $args=New-Object 'object[]' 1
  $args[0]=$arr
  return $method.Invoke($null,$args)
}
$wiredKbd   = New-Desc 'GS3108T-PRO'      'HID\VID_05AC&PID_024F&MI_00\8&23A5F33F&0&0000' 0x01 0x06 9 2 'SONiX'
$wiredKbd2  = New-Desc 'GS3108T-PRO'      'HID\VID_05AC&PID_024F&MI_00\8&239300BB&0&0000' 0x01 0x06 9 2 'SONiX'
$wiredVend  = New-Desc 'GS3108T-PRO'      'HID\VID_05AC&PID_024F&MI_03\8&312EA49C&0&0000' 0xFF13 0x01 65 65 'SONiX'
$wiredCfg   = New-Desc 'GS3108T-PRO'      'HID\VID_05AC&PID_024F&MI_02\8&18B10753&0&0000' 0xFF68 0x61 65 4097 'SONiX'
$rxKbd      = New-Desc 'GS3108T-PRO 2.4G' 'HID\VID_05AC&PID_024F&MI_00\8&239300BB&0&0000' 0x01 0x06 9 2 ''
$rxBattery  = New-Desc 'GS3108T-PRO 2.4G' 'HID\VID_05AC&PID_024F&MI_03\8&259D7608&0&0000' 0xFF60 0x61 33 33 ''
$rxVend     = New-Desc 'GS3108T-PRO 2.4G' 'HID\VID_05AC&PID_024F&MI_04\8&126D59CA&0&0000' 0xFF59 0x61 65 65 ''

$mRes=$t.GetMethod('ResolveTransport')
if(!$mRes){throw 'Missing ResolveTransport: transport is still hard-coded'}
$mBat=$t.GetMethod('FindBatteryEndpoint')
if(!$mBat){throw 'Missing FindBatteryEndpoint'}

# 1. 有线 + 接收器同时在线（用户当前实机状态）=> 必须判有线
$both=Make-Array @($wiredKbd,$wiredVend,$wiredCfg,$rxKbd,$rxBattery,$rxVend)
if((Invoke-One $mRes $both) -ne 'USB 有线'){throw 'Wired identity present but transport still reported as 2.4G wireless'}
# 2. 只有接收器 => 无线
$rxOnly=Make-Array @($rxKbd,$rxBattery,$rxVend)
if((Invoke-One $mRes $rxOnly) -ne '2.4G 无线'){throw 'Receiver-only host misreported as wired'}
# 3. 有线且拔掉接收器 => 仍有线
$wiredOnly=Make-Array @($wiredKbd,$wiredVend,$wiredCfg)
if((Invoke-One $mRes $wiredOnly) -ne 'USB 有线'){throw 'Cable-only keyboard not reported as wired'}
# 4. 只有 FF13 厂商接口、没有 MI_00 键盘集合 => 证据不足，保守判无线
$ambiguous=Make-Array @($wiredVend,$rxKbd,$rxBattery)
if((Invoke-One $mRes $ambiguous) -ne '2.4G 无线'){throw 'Single vendor interface alone must not force wired mode'}
# 5. 名称带 2.4G 的接收器即使带 MI_00 + FF13 也不能判有线
$fakeWired=Make-Array @($wiredVend,$rxKbd,$rxBattery)
if((Invoke-One $mRes $fakeWired) -ne '2.4G 无线'){throw 'Wireless identity accepted as wired'}
# 6. 电池端点必须选 FF60/33 字节接收器，不得误选有线 FF13/65 字节
$bat=Invoke-One $mBat $both
if($bat -eq $null -or $bat.UsagePage -ne 0xFF60 -or $bat.InputReportLength -ne 33){throw 'Battery endpoint selection drifted away from FF60/33'}
$batWiredOnly=Invoke-One $mBat $wiredOnly
if($batWiredOnly -ne $null){throw 'Wired FF13 endpoint must not be used as the 2.4G battery endpoint'}
# 7. 有线脚注：无历史电量时明确“不可读”，有历史时标注为“上次 2.4G 读数”，不得冒充实时值
$mFoot=$t.GetMethod('FormatWiredFootnote')
if(!$mFoot){throw 'Missing FormatWiredFootnote'}
$noLast=[string]$mFoot.Invoke($null,@([int]-1,[DateTime]::MinValue))
if(!$noLast.Contains('供电中') -or !$noLast.Contains('不可读')){throw ('Missing-battery wired footnote is ambiguous: ' + $noLast)}
$stamp=Get-Date -Year 2026 -Month 10 -Day 23 -Hour 23 -Minute 1 -Second 27
$withLast=[string]$mFoot.Invoke($null,@([int]87,$stamp))
if(!$withLast.Contains('87%') -or !$withLast.Contains('23:01:27') -or !$withLast.Contains('上次')){throw ('Last-known battery not labelled as historical: ' + $withLast)}
Write-Host 'PASS: GANSS wired/2.4G transport resolution, endpoint isolation and stale-battery footnote'
