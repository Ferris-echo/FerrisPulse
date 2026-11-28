# tests/test_custom_devices_readonly_cache.ps1
# P2 验证：自定义设备"读路径只读 + 进程内缓存 + 启动期一次性迁移"
# 断言口径（docs/OPTIMIZATION_BACKLOG.md P2）：
#   1) LoadCustomDevices 是纯只读：脏数据不会被"顺手纠正"写回注册表；
#   2) 缓存返回的是副本，调用方改动不会污染后续读取；
#   3) MigrateCustomDevicesOnce 一次性纠正脏数据并写 MigrationVersion；
#   4) 迁移幂等：重复执行不再改写。
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "..\tools\TestHarness.ps1")

$asm = Load-FerrisPulseAssembly
$BF = [System.Reflection.BindingFlags]"Public,NonPublic,Instance,Static"
$tDm = $asm.GetType("RazerBatteryTray.DeviceManager", $true)
$mLoad = $tDm.GetMethod("LoadCustomDevices", [System.Reflection.BindingFlags]"Static,Public")
$mInvalidate = $tDm.GetMethod("InvalidateCustomDevicesCache", $BF)
$mMigrate = $tDm.GetMethod("MigrateCustomDevicesOnce", [System.Reflection.BindingFlags]"Static,Public")

$testId = "Test:P2READONLY:0001"
$cfgPath = "HKCU:\Software\RazerBatteryTray\CustomDevices\" + $testId
$migrationKey = "HKCU:\Software\FerrisPulse"

function Get-CfgValue([string]$name) {
    $v = Get-ItemProperty -Path $cfgPath -Name $name -ErrorAction SilentlyContinue
    if ($null -eq $v) { return $null }
    return $v.$name
}

try {
    # 清掉迁移版本，保证本用例的迁移步骤一定真的执行
    if (Test-Path $migrationKey) {
        Remove-ItemProperty -Path $migrationKey -Name "MigrationVersion" -ErrorAction SilentlyContinue
    }
    Remove-Item -Path $cfgPath -Recurse -Force -ErrorAction SilentlyContinue
    New-Item -Path $cfgPath -Force | Out-Null
    Set-ItemProperty -Path $cfgPath -Name "DeviceId" -Value $testId
    Set-ItemProperty -Path $cfgPath -Name "HardwareFingerprint" -Value $testId
    Set-ItemProperty -Path $cfgPath -Name "OriginalName" -Value "NuPhy Air75 V2"
    Set-ItemProperty -Path $cfgPath -Name "CustomName" -Value "NuPhy Air75 V2"
    Set-ItemProperty -Path $cfgPath -Name "Category" -Value 1
    Set-ItemProperty -Path $cfgPath -Name "Transport" -Value "2.4G 无线"
    Set-ItemProperty -Path $cfgPath -Name "CachedBattery" -Value 100
    $mInvalidate.Invoke($null, @()) | Out-Null

    Invoke-TestCase "1. 读路径纯只读：脏数据不会被顺手纠正" {
        for ($i = 0; $i -lt 3; $i++) { $mLoad.Invoke($null, @()) | Out-Null }
        Assert-Equal 1 ([int](Get-CfgValue "Category")) "registry Category untouched by read path"
        Assert-Equal 100 ([int](Get-CfgValue "CachedBattery")) "registry CachedBattery untouched by read path"
    }

    Invoke-TestCase "2. 缓存返回副本，调用方改动不污染后续读取" {
        $a = $mLoad.Invoke($null, @())
        $first = $null; $second = $null
        foreach ($c in $a) { if ($c.DeviceId -eq $testId) { $first = $c } }
        Assert-True ($null -ne $first) "test config loaded from cache"
        $first.CustomName = "MUTATED-BY-TEST"
        $b = $mLoad.Invoke($null, @())
        foreach ($c in $b) { if ($c.DeviceId -eq $testId) { $second = $c } }
        Assert-True ($null -ne $second) "test config loaded again"
        Assert-True ($second.CustomName -ne "MUTATED-BY-TEST") "cache returns defensive copies"
    }

    Invoke-TestCase "3. 启动期一次性迁移纠正脏数据并记录版本" {
        $expectKeyboard = [int][RazerBatteryTray.DeviceCategory]::Keyboard
        $mMigrate.Invoke($null, @()) | Out-Null
        Assert-Equal $expectKeyboard ([int](Get-CfgValue "Category")) "migration corrected category to Keyboard"
        Assert-Equal 4294967295 ([int64](Get-CfgValue "CachedBattery")) "migration cleared dummy 100% battery (stored as uint32 -1)"
        $ver = Get-ItemProperty -Path $migrationKey -Name "MigrationVersion" -ErrorAction SilentlyContinue
        Assert-True ($null -ne $ver -and [int]$ver.MigrationVersion -eq 1) "MigrationVersion=1 recorded"
    }

    Invoke-TestCase "4. 迁移幂等：再次执行不再改写" {
        Set-ItemProperty -Path $cfgPath -Name "CustomName" -Value "Post-Migration Marker"
        $mMigrate.Invoke($null, @()) | Out-Null
        $mMigrate.Invoke($null, @()) | Out-Null
        Assert-Equal "Post-Migration Marker" (Get-CfgValue "CustomName") "second migration run is a no-op"
        Assert-Equal ([int][RazerBatteryTray.DeviceCategory]::Keyboard) ([int](Get-CfgValue "Category")) "category stays corrected"
    }
}
finally {
    Remove-Item -Path $cfgPath -Recurse -Force -ErrorAction SilentlyContinue
    $mInvalidate.Invoke($null, @()) | Out-Null
    Remove-ItemProperty -Path $migrationKey -Name "MigrationVersion" -ErrorAction SilentlyContinue
}

Write-Host ""
Write-Host "ALL CUSTOM-DEVICE READONLY CACHE TESTS PASSED." -ForegroundColor Green
exit 0
