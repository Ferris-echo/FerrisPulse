# ============================================================================
# 测试名称: test_eweadn_x87_support.ps1
# 所属区域: tests (自动化回归)
# 核心职责:
#   验证前行者 X87（松翰 0C45:FEFE 2.4G 复合接收器）的通道识别、分类与能力契约。
#   重点断言：
#     · 精确四元组隔离，绝不把 Razer / 其它键盘误判为前行者；
#     · SupportsBattery 必须为 false —— 该接收器无电量寄存器，虚标即为缺陷；
#     · BuiltIn:EweadnKeyboard 已注册且能识别 Brand / VID:PID。
#   全部断言只依赖纯逻辑，不写入硬件。
#
# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️:
# 1. 严禁 Add-Type -Path（会锁死 FerrisPulse.exe 导致后续 CS0016），必须全内存反射加载。
# 2. 本文件必须严格保存为【UTF-8 with BOM】编码。
# ============================================================================

$ErrorActionPreference = 'Stop'
$assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '../FerrisPulse.exe')))
function Assert($condition, $message) { if (-not $condition) { throw $message } }
function AssertEq($actual, $expected, $message) { if ($actual -ne $expected) { throw ($message + " (expected=$expected actual=$actual)") } }

$helper = $assembly.GetType('RazerBatteryTray.EweadnDeviceHelper', $true)

# ---------- 1. VID:PID 精确匹配 ----------
Assert ([RazerBatteryTray.EweadnDeviceHelper]::IsSupportedVidPid(0x0C45, 0xFEFE)) '0C45:FEFE receiver must be recognized'
Assert (-not [RazerBatteryTray.EweadnDeviceHelper]::IsSupportedVidPid(0x0C45, 0x0001)) 'Same vendor other PID must be rejected'
Assert (-not [RazerBatteryTray.EweadnDeviceHelper]::IsSupportedVidPid(0x1532, 0xFEFE)) 'Foreign vendor must be rejected'
Assert (-not [RazerBatteryTray.EweadnDeviceHelper]::IsSupportedVidPid(0x0000, 0x0000)) 'Empty ids must be rejected'

# ---------- 2. 标识字符串识别（不得误伤 Razer） ----------
Assert ([RazerBatteryTray.EweadnDeviceHelper]::IsEweadnReceiver('HID\VID_0C45&PID_FEFE&MI_00\7&11129455&0&0000')) 'HID instance id must match'
Assert ([RazerBatteryTray.EweadnDeviceHelper]::IsEweadnReceiver('\\?\hid#vid_0c45&pid_fefe&mi_03#7&287dc065&0&0000#{guid}')) 'Lowercase device path must match'
Assert ([RazerBatteryTray.EweadnDeviceHelper]::IsEweadnReceiver('EWEADN:0C45:FEFE:KB')) 'Normalized device id must match'
Assert (-not [RazerBatteryTray.EweadnDeviceHelper]::IsEweadnReceiver('HID\VID_1532&PID_00AB&MI_00\7&E7F0A45&0&0000')) 'Razer Basilisk must NOT match'
Assert (-not [RazerBatteryTray.EweadnDeviceHelper]::IsEweadnReceiver('HID:19F5:1028')) 'NuPhy must NOT match'
Assert (-not [RazerBatteryTray.EweadnDeviceHelper]::IsEweadnReceiver('')) 'Empty identifier must be rejected'
Assert (-not [RazerBatteryTray.EweadnDeviceHelper]::IsEweadnReceiver($null)) 'Null identifier must be rejected'
# 只有 VID 或只有 PID 都不得通过
Assert (-not [RazerBatteryTray.EweadnDeviceHelper]::IsEweadnReceiver('VID_0C45&PID_0001')) 'Vendor-only match must be rejected'
Assert (-not [RazerBatteryTray.EweadnDeviceHelper]::IsEweadnReceiver('VID_1532&PID_FEFE')) 'PID-only match must be rejected'

# ---------- 3. 常量和契约 ----------
AssertEq ([RazerBatteryTray.EweadnDeviceHelper]::VendorId) 0x0C45 'Vendor id'
AssertEq ([RazerBatteryTray.EweadnDeviceHelper]::ProductIdDongle) 0xFEFE 'Product id'
AssertEq ([RazerBatteryTray.EweadnDeviceHelper]::BrandName) 'EWEADN' 'Brand token'
AssertEq ([RazerBatteryTray.EweadnDeviceHelper]::TransportWireless) '2.4G 无线' 'Wireless transport label'

# ---------- 4. 驱动注册与能力契约 ----------
[Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '../FerrisPulse.exe'))) | Out-Null
$builtIn = $assembly.GetType('RazerBatteryTray.BuiltInDrivers', $true)
$builtIn.GetMethod('EnsureRegistered').Invoke($null, @()) | Out-Null

$driverType = $assembly.GetType('RazerBatteryTray.EweadnKeyboardDriver', $true)
$driver = [Activator]::CreateInstance($driverType)
AssertEq $driver.DriverId 'BuiltIn:EweadnKeyboard' 'Driver id'
AssertEq $driver.Brand 'EWEADN' 'Driver brand'
Assert ($driver.MatchesVidPid(0x0C45, 0xFEFE)) 'Driver must claim the receiver VID:PID'
Assert (-not $driver.MatchesVidPid(0x1532, 0x00AB)) 'Driver must not claim Razer devices'

# 构造一个 EWEADN 键盘条目，验证能力位
$dev = New-Object RazerBatteryTray.MouseBatteryInfo
$dev.DeviceId = 'EWEADN:0C45:FEFE:KB'
$dev.Brand = 'EWEADN'
$dev.Category = [RazerBatteryTray.DeviceCategory]::Keyboard
$dev.HardwareId = 'HID:0C45:FEFE'
$driver.PopulateCapabilities($dev)
# 2.4G 接收器不转发电量 -> 在无 BLE 连接时 SupportsBattery 必须为 false（不虚标）
$blePct = [RazerBatteryTray.EweadnBleBatteryHelper]::QueryBattery()
if ($blePct -lt 0) {
    AssertEq $dev.Capabilities.SupportsBattery $false 'Battery must NOT be advertised while BLE is unreachable'
    # 走真实设备条目构造，验证不可读时 BatteryPercent 保持 -1（而非伪造 0 或常量）
    $realDev = [RazerBatteryTray.EweadnDeviceHelper]::CreateKeyboardDeviceInfo()
    if ($null -ne $realDev) {
        Assert ($realDev.BatteryPercent -lt 0) 'Real device entry must report -1 (unknown) when BLE is unreachable'
        AssertEq $realDev.Capabilities.SupportsBattery $false 'Real device entry must not advertise battery without BLE'
    }
} else {
    AssertEq $dev.Capabilities.SupportsBattery $true 'Battery must be advertised when BLE GATT provides a value'
    AssertEq $dev.BatteryPercent $blePct 'Battery percent must equal the BLE reading'
}
AssertEq $dev.Capabilities.SupportsDpi $false 'DPI must not be advertised'
AssertEq $dev.Capabilities.SupportsPollingRate $false 'Polling rate must not be advertised'
AssertEq $dev.Capabilities.SupportsDpiStages $false 'DPI stages must not be advertised'
AssertEq $dev.Capabilities.SupportsXyIndependence $false 'XY independence must not be advertised'
AssertEq $dev.Capabilities.CustomActions.Count 0 'No custom actions claimed'

# 该驱动不得处理鼠标，避免和同一接收器下的鼠标通道互相争抢
$mouseDev = New-Object RazerBatteryTray.MouseBatteryInfo
$mouseDev.DeviceId = 'EWEADN:0C45:FEFE:MS'
$mouseDev.Brand = 'EWEADN'
$mouseDev.Category = [RazerBatteryTray.DeviceCategory]::Mouse
Assert (-not $driver.CanHandle($mouseDev)) 'Keyboard driver must not handle mouse-category devices'

# ---------- 5. 源码级铁律审计 ----------
$source = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../src/Hardware/EweadnDeviceHelper.cs'))
Assert ($source.Contains('AI MAINTENANCE DIRECTIVE')) 'Maintenance directive header must be preserved'
Assert (-not ($source -match '\$"')) 'C# 6 string interpolation is forbidden'
Assert (-not ($source.Contains('?.'))) 'C# 6 null-conditional operator is forbidden'
Assert (-not ($source.Contains('nameof('))) 'nameof is forbidden'
Assert ($source.Contains('caps.SupportsBattery = hasBleBattery')) 'Battery capability must be driven by the BLE reading, never hardcoded'
Assert ($source.Contains('EweadnBleBatteryHelper.QueryBattery()')) 'BLE GATT battery must be the only battery source'
Assert ($source.Contains('0xFF60')) 'Protocol audit notes must document the VIA-style channel'
# 该 Helper 不得包含任何伪造电量读取入口
Assert (-not ($source.Contains('TryParseBattery'))) 'Must not expose a fake battery parser'
Assert (-not ($source.Contains('QueryDevice()'))) 'Must not expose a battery query entry point'

$driverSource = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../src/Hardware/BuiltInDrivers.cs'))
Assert ($driverSource.Contains('EweadnKeyboardDriver')) 'Driver must be registered in BuiltInDrivers'
Assert ($driverSource.Contains('HardwareDriverRegistry.RegisterDriver(new EweadnKeyboardDriver())')) 'Driver must be added to EnsureRegistered'

$managerSource = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../src/Hardware/DeviceManager.cs'))
Assert ($managerSource.Contains('EweadnDeviceHelper.CreateKeyboardDeviceInfo()')) 'DeviceManager must query EWEADN devices'
Assert ($managerSource.Contains('"EWEADN"')) 'Device id variant prefixes must include EWEADN'

$bytes = [IO.File]::ReadAllBytes((Join-Path $PSScriptRoot '../src/Hardware/EweadnDeviceHelper.cs'))
Assert ($bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF) 'UTF-8 BOM required for EweadnDeviceHelper.cs'

# ---------- 6. BLE 电量通道契约 ----------
$ble = $assembly.GetType('RazerBatteryTray.EweadnBleBatteryHelper', $true)
AssertEq ([RazerBatteryTray.EweadnBleBatteryHelper]::BatteryServiceUuid) '0000180f-0000-1000-8000-00805f9b34fb' 'GATT Battery Service UUID'
AssertEq ([RazerBatteryTray.EweadnBleBatteryHelper]::BatteryLevelCharacteristicUuid) '00002a19-0000-1000-8000-00805f9b34fb' 'GATT Battery Level UUID'

# 已配对的 X87 应能被注册表识别（本机实测名 X87-5.0-BT2）
$nameArgs = [object[]]@('')
$pairedMac = [RazerBatteryTray.EweadnBleBatteryHelper]::FindPairedKeyboardMac([ref]$nameArgs[0])
if (-not [string]::IsNullOrEmpty($pairedMac)) {
    Assert ($pairedMac -match '^([0-9a-f]{2}:){5}[0-9a-f]{2}$') 'Paired MAC must be formatted AA:BB:CC:DD:EE:FF'
    # 名称取自注册表 LEName，部分系统上可能为空；此处只要求可用时不报错
    Assert ($null -ne $nameArgs[0]) 'Paired device name slot must be filled'
}

$bleSource = [IO.File]::ReadAllText((Join-Path $PSScriptRoot '../src/Hardware/EweadnBleBatteryHelper.cs'))
Assert ($bleSource.Contains('AI MAINTENANCE DIRECTIVE')) 'BLE helper must keep the maintenance header'
Assert (-not ($bleSource -match '\$"')) 'BLE helper must not use C# 6 interpolation'
Assert (-not ($bleSource.Contains('?.'))) 'BLE helper must not use null-conditional operator'
Assert ($bleSource.Contains('return -1')) 'Every failure path must degrade to -1, never throw'
Assert (-not ($bleSource.Contains('BatteryPercent = 50'))) 'No fabricated battery fallback allowed'

# 2.4G 私有通道只回环回显，源码必须记录该结论，避免后人重复盲扫
Assert ($source.Contains('逐字节回环回显')) 'Source must document that FF60 only echoes the request'

# ---------- 7. 官方驱动判定逻辑对齐 ----------
AssertEq ([RazerBatteryTray.EweadnDeviceHelper]::ProductIdKeyboardUsb) 0x8006 'Keyboard body USB PID'
# 官方 kSe()：0x8006 必须带 0xFF68/0x61 才算「支持驱动」；本机当前 2.4G 形态应为 false
$has8806Driver = [RazerBatteryTray.EweadnDeviceHelper]::HasDriverCapableInterface()
$bodyPresent = [RazerBatteryTray.EweadnDeviceHelper]::IsKeyboardBodyPresent()
if (-not $bodyPresent) {
    AssertEq $has8806Driver $false 'Without keyboard body (2.4G mode) driver-capable interface must be false'
}
Assert ($source.Contains('0xFF68')) 'Source must document the official 0xFF68/0x61 capability probe'
Assert ($source.Contains('kSe')) 'Source must reference the official kSe() verdict logic'

Write-Host 'PASS: EWEADN X87 identification and capability contract'
