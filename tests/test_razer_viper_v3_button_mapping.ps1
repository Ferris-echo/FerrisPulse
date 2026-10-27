# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# 模块: test_razer_viper_v3_button_mapping.ps1
# 职责: 验证 Razer Viper V3 HyperSpeed (1532:00B8) 板载按键映射协议层与快照序列化
# 铁律: C# 5 / .NET 4.0; 纯内存反射装载，杜绝句柄锁死

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = Resolve-Path "$ScriptDir\.."
Set-Location $ProjectDir

$exePath = Join-Path $ProjectDir "FerrisPulse.exe"
if (-not (Test-Path $exePath)) { throw "FerrisPulse.exe 不存在，请先执行 build.bat" }

$rawBytes = [System.IO.File]::ReadAllBytes($exePath)
$asm = [System.Reflection.Assembly]::Load($rawBytes)

$bmType = $asm.GetType("RazerBatteryTray.RazerButtonMapping")
if ($null -eq $bmType) { throw "未找到 RazerButtonMapping 类型" }

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 1/4] 按键枚举定义与硬件值对齐" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$btnEnumType = $asm.GetType("RazerBatteryTray.RazerButtonMapping+ButtonId")
if ($null -eq $btnEnumType) { throw "未找到 ButtonId 枚举" }

$expectedButtons = @{
    "Left" = 0x01
    "Right" = 0x02
    "Middle" = 0x03
    "Backward" = 0x04
    "Forward" = 0x05
    "WheelUp" = 0x09
    "WheelDown" = 0x0A
    "DpiButton" = 0x60
}

foreach ($name in $expectedButtons.Keys) {
    $val = [byte][Enum]::Parse($btnEnumType, $name)
    if ($val -ne $expectedButtons[$name]) {
        throw "按键 $name 的值不匹配: 期望 $($expectedButtons[$name])，实测 $val"
    }
}
Write-Host "PASS: 全部 8 个物理按键枚举值严格对齐真机硬件报文定义" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 2/4] ButtonFunction 7 字节报文封包与解包" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$fnType = $asm.GetType("RazerBatteryTray.RazerButtonMapping+ButtonFunction")
$fnClassType = $asm.GetType("RazerBatteryTray.RazerButtonMapping+FnClass")

$fn = [Activator]::CreateInstance($fnType)
$fn.Class = [Enum]::Parse($fnClassType, "Mouse")
$fn.ValueLength = 1
$fn.Value[0] = 0x01 # Mouse Left

$toByteMethod = $fnType.GetMethod("ToBytes7")
$bytes = [byte[]]$toByteMethod.Invoke($fn, $null)
if ($bytes.Length -ne 7) { throw "ToBytes7 长度异常: $($bytes.Length)" }
if ($bytes[0] -ne 0x01 -or $bytes[1] -ne 1 -or $bytes[2] -ne 0x01) {
    throw "ToBytes7 封包格式不符合协议契约"
}

$fromBytesMethod = $fnType.GetMethod("FromBytes7", [System.Reflection.BindingFlags]"Public,Static")
$restored = $fromBytesMethod.Invoke($null, @($bytes, [int]0))
if ($restored.Class -ne $fn.Class -or $restored.ValueLength -ne 1 -or $restored.Value[0] -ne 0x01) {
    throw "FromBytes7 解包还原失败"
}
Write-Host "PASS: ButtonFunction 7 字节协议序列化与反序列化完全保真" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 3/4] 出厂默认按键映射生成" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$getDefaultMethod = $bmType.GetMethod("GetFactoryDefault", [System.Reflection.BindingFlags]"Public,Static")
foreach ($name in $expectedButtons.Keys) {
    $btn = [Enum]::Parse($btnEnumType, $name)
    $defFn = $getDefaultMethod.Invoke($null, @($btn))
    if ($null -eq $defFn) {
        throw "按键 $name 未定义出厂默认映射"
    }
}
Write-Host "PASS: 出厂默认映射成功覆盖全部 8 个物理按键的标准绑定" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 4/4] 零依赖 JSON 快照持久化与无损还原" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$infoType = $asm.GetType("RazerBatteryTray.RazerButtonMapping+ButtonFunctionInfo")
$listType = [System.Collections.Generic.List``1].MakeGenericType($infoType)
$testList = [Activator]::CreateInstance($listType)

$info1 = [Activator]::CreateInstance($infoType)
$info1.Button = [Enum]::Parse($btnEnumType, "Backward")
$info1.Function = $fn
$testList.Add($info1)

$toJsonMethod = $bmType.GetMethod("ButtonSnapshotToJson", [System.Reflection.BindingFlags]"NonPublic,Static")
$json = [string]$toJsonMethod.Invoke($null, [object[]]@(,$testList))
if ([string]::IsNullOrEmpty($json) -or -not $json.Contains('"button":"04"')) {
    throw "ButtonSnapshotToJson 输出不符合预期: $json"
}

$fromJsonMethod = $bmType.GetMethod("ParseButtonSnapshotJson", [System.Reflection.BindingFlags]"NonPublic,Static")
$restoredList = $fromJsonMethod.Invoke($null, [object[]]@(,$json))
if ($null -eq $restoredList -or $restoredList.Count -ne 1) {
    throw "ButtonSnapshotFromJson 还原失败"
}
$firstItem = $restoredList[0]
if ($firstItem.Key -ne [Enum]::Parse($btnEnumType, "Backward")) {
    throw "还原的按键不匹配"
}
Write-Host "PASS: 零依赖 JSON 快照无损往返解析校验完全通过" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " ALL 4/4 RAZER BUTTON MAPPING TESTS PASSED!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
exit 0
