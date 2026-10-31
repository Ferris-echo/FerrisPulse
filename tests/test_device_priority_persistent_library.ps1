# ============================================================================
# 模块名称: test_device_priority_persistent_library.ps1
# 核心职责:
#   验证“设备优先级管理”真正成为持久化静态设备库：
#   1. 离线设备不会在后台轮询中被清空清除，优先级顺位永久记忆；
#   2. GetAllKnownDevicesForPriority 正确合并在线与离线设备，且准确标记离线状态；
#   3. 用户主动点击删除（HideOrDeleteDevice）时，设备才真正从优先级列表与监控中移除。
# ============================================================================

$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$root = Split-Path -Parent $root
$exePath = Join-Path $root "FerrisPulse.exe"

$rawBytes = [System.IO.File]::ReadAllBytes($exePath)
$asm = [System.Reflection.Assembly]::Load($rawBytes)
$dm = $asm.GetType('RazerBatteryTray.DeviceManager')
$dss = $asm.GetType('RazerBatteryTray.DriverStateStore')
$mbiType = $asm.GetType('RazerBatteryTray.MouseBatteryInfo')

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 1/3] 验证离线设备不会被 QueryAllDevices 冲掉优先级列表" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$mGetPrio = $dm.GetMethod('GetPriorityList', [System.Reflection.BindingFlags]'Static,Public')
$mSavePrio = $dm.GetMethod('SavePriorityList', [System.Reflection.BindingFlags]'Static,Public')
$mUnhide = $dm.GetMethod('UnhideDevice', [System.Reflection.BindingFlags]'Static,Public')
$mHide = $dm.GetMethod('HideOrDeleteDevice', [System.Reflection.BindingFlags]'Static,Public')

$testOnlineId = "Razer:1532:00E5"
$testOfflineId = "Razer:1532:00EF"

# 确保两台设备都不在黑名单
$mUnhide.Invoke($null, [object[]]@($testOnlineId))
$mUnhide.Invoke($null, [object[]]@($testOfflineId))

# 保存期望的优先级：离线设备排第 1，在线设备排第 2
$prioListToSave = [System.Collections.Generic.List[string]]::new()
$prioListToSave.Add($testOfflineId)
$prioListToSave.Add($testOnlineId)
$mSavePrio.Invoke($null, [object[]]@(, $prioListToSave))

# 模拟 DriverStateStore 记录了离线设备的电量和名称
$mSaveState = $dss.GetMethod('SaveBatteryState', [System.Reflection.BindingFlags]'Static,Public')
$mSaveState.Invoke($null, [object[]]@($testOfflineId, 85, $false, "Razer DeathAdder V4 Pro Carbon"))

# 执行 QueryAllDevices (模拟后台轮询，此时测试环境没有物理插入 0x00EF)
$mQueryAll = $dm.GetMethod('QueryAllDevices', [System.Reflection.BindingFlags]'Static,Public')
$activeDevs = $mQueryAll.Invoke($null, [object[]]@())

# 检查 QueryAllDevices 执行后，离线设备的优先级是否依然完好保留！
$prioAfterQuery = $mGetPrio.Invoke($null, [object[]]@())
if ($prioAfterQuery -eq $null -or -not $prioAfterQuery.Contains($testOfflineId)) {
    Write-Host "FAILED: Offline device $testOfflineId was wrongfully purged from PriorityList!" -ForegroundColor Red
    exit 1
}
if ($prioAfterQuery[0] -ne $testOfflineId) {
    Write-Host "FAILED: Priority order was disturbed! Expected $testOfflineId at index 0, but got: $($prioAfterQuery[0])" -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: 离线设备在后台轮询中未被清除，优先级顺位完全保留！" -ForegroundColor Green

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 2/3] 验证 GetAllKnownDevicesForPriority 正确呈现离线设备" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$mGetAllKnown = $dm.GetMethod('GetAllKnownDevicesForPriority', [System.Reflection.BindingFlags]'Static,Public')
if ($mGetAllKnown -eq $null) {
    Write-Host "FAILED: Method GetAllKnownDevicesForPriority not found!" -ForegroundColor Red
    exit 1
}

# 动态反射构造 List<MouseBatteryInfo>
$genericListDef = [System.Type]::GetType("System.Collections.Generic.List``1")
$closedListType = $genericListDef.MakeGenericType(@($mbiType))
$mockActive = [Activator]::CreateInstance($closedListType)

$onlineDev = [Activator]::CreateInstance($mbiType)
$mbiType.GetProperty("DeviceId").SetValue($onlineDev, $testOnlineId, $null)
$mbiType.GetProperty("DeviceName").SetValue($onlineDev, "Razer Viper V4 Pro", $null)
$mbiType.GetProperty("BatteryPercent").SetValue($onlineDev, 90, $null)
$mbiType.GetProperty("IsConnected").SetValue($onlineDev, $true, $null)
$closedListType.GetMethod("Add").Invoke($mockActive, [object[]]@($onlineDev))

$allKnown = $mGetAllKnown.Invoke($null, [object[]]@(, $mockActive))
if ($allKnown -eq $null -or $allKnown.Count -lt 2) {
    Write-Host "FAILED: GetAllKnownDevicesForPriority did not return both online and offline devices! Count: $($allKnown.Count)" -ForegroundColor Red
    exit 1
}

$offlineItem = $null
foreach ($it in $allKnown) {
    if ($it.DeviceId -eq $testOfflineId) { $offlineItem = $it; break }
}

if ($offlineItem -ne $null) {
    if ($offlineItem.IsConnected) {
        Write-Host "FAILED: Offline device should have IsConnected = false!" -ForegroundColor Red
        exit 1
    }
    if ($offlineItem.BatteryPercent -ne 85) {
        Write-Host "FAILED: Offline device should retain cached battery 85%, got: $($offlineItem.BatteryPercent)" -ForegroundColor Red
        exit 1
    }
    if ($allKnown[0].DeviceId -ne $testOfflineId) {
        Write-Host "FAILED: Expected $testOfflineId to be top priority (index 0) in allKnown list!" -ForegroundColor Red
        exit 1
    }
} else {
    Write-Host "FAILED: $testOfflineId not found in allKnown devices!" -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: GetAllKnownDevicesForPriority 正确整合离线设备、记忆电量与准确顺位！" -ForegroundColor Green

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 3/3] 验证主动点击删除时（HideOrDeleteDevice）才真正剔除" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$mHide.Invoke($null, [object[]]@($testOfflineId))
$allKnownAfterHide = $mGetAllKnown.Invoke($null, [object[]]@(, $mockActive))
$foundHidden = $false
foreach ($it in $allKnownAfterHide) {
    if ($it.DeviceId -eq $testOfflineId) { $foundHidden = $true; break }
}

if ($foundHidden) {
    Write-Host "FAILED: Hidden device should not appear in GetAllKnownDevicesForPriority!" -ForegroundColor Red
    exit 1
}

$prioAfterHide = $mGetPrio.Invoke($null, [object[]]@())
if ($prioAfterHide.Contains($testOfflineId)) {
    Write-Host "FAILED: Hidden device should be removed from PriorityList!" -ForegroundColor Red
    exit 1
}
Write-Host "PASSED: 主动删除/拉黑设备时，设备彻底从优先级列表及已知库中剔除！" -ForegroundColor Green

# 恢复现场
$mUnhide.Invoke($null, [object[]]@($testOnlineId))
$mUnhide.Invoke($null, [object[]]@($testOfflineId))

Write-Host "==========================================================" -ForegroundColor Green
Write-Host " ALL PASS: 设备优先级管理静态已知设备库机制验证全部通过！" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
