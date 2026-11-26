# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# 「清除缓存」功能回归测试：
#   1. 清理目标与保留规则（adapters / 自启动 / 快捷方式）；
#   2. 沙盒化执行——注册表树、缓存目录、备份目录全部注入临时路径，
#      真实 HKCU\Software\FerrisPulse / RazerBatteryTray 与用户缓存永不触碰；
#   3. 备份产物（两个 .reg + README）与执行报告契约；
#   4. UI 契约：确认对话框、About 入口、共用重启方法。
# 零实机 I/O。

$ErrorActionPreference = "Stop"
Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing
. (Join-Path $PSScriptRoot "..\tools\TestHarness.ps1")

$asm = Load-FerrisPulseAssembly
$bfPubStatic = [System.Reflection.BindingFlags]"Public,Static"
$bfNonPubInst = [System.Reflection.BindingFlags]"NonPublic,Instance"

$tManager = $asm.GetType("RazerBatteryTray.CacheResetManager")
$tReport = $asm.GetType("RazerBatteryTray.CacheResetReport")

Invoke-TestCase "CacheClear.TargetsAndPreserveRules" {
    Assert-True ($null -ne $tManager) "CacheResetManager exists"
    Assert-Equal "Software\RazerBatteryTray" ([string]$tManager.GetField("DeviceRegistryTree", $bfPubStatic).GetValue($null)) "device tree"
    Assert-Equal "Software\FerrisPulse" ([string]$tManager.GetField("SettingsRegistryTree", $bfPubStatic).GetValue($null)) "settings tree"
    Assert-Equal "adapters" ([string]$tManager.GetField("PreservedAdaptersFolderName", $bfPubStatic).GetValue($null)) "adapters preserved"
    Assert-Equal "FerrisPulse-Backup" ([string]$tManager.GetField("BackupRootFolderName", $bfPubStatic).GetValue($null)) "backup root name"

    $isPreserved = $tManager.GetMethod("IsPreservedRoamingEntry", $bfPubStatic)
    Assert-True ([bool]$isPreserved.Invoke($null, @("adapters"))) "adapters is preserved"
    Assert-True ([bool]$isPreserved.Invoke($null, @("ADAPTERS"))) "case-insensitive preserve"
    Assert-True (-not [bool]$isPreserved.Invoke($null, @("ScriptCache"))) "ScriptCache not preserved"
    Assert-True (-not [bool]$isPreserved.Invoke($null, @($null))) "null not preserved"

    # 默认路径必须落在 %LOCALAPPDATA%（备份不能放进被清理的目录里）
    $backupRoot = [string]$tManager.GetMethod("GetBackupRoot", $bfPubStatic).Invoke($null, @())
    $localCache = [string]$tManager.GetMethod("GetLocalCacheRoot", $bfPubStatic).Invoke($null, @())
    Assert-True ($backupRoot.EndsWith("FerrisPulse-Backup")) "backup root folder"
    $sep = [System.IO.Path]::DirectorySeparatorChar
    $cachePrefix = $localCache.TrimEnd($sep) + $sep
    Assert-True (-not $backupRoot.TrimEnd($sep).StartsWith($cachePrefix, [System.StringComparison]::OrdinalIgnoreCase)) "backup outside cleaned cache"
}

Invoke-TestCase "CacheClear.ExecuteSandboxedBackupAndDelete" {
    $sandbox = Join-Path $env:TEMP ("fp_cacheclear_" + [Guid]::NewGuid().ToString("N"))
    $regRoot = "Software\FerrisPulse_CacheUnitTest_" + [Guid]::NewGuid().ToString("N").Substring(0, 8)
    $regDevice = "HKCU:\" + $regRoot + "\Device"
    $regSettings = "HKCU:\" + $regRoot + "\Settings"
    $localCache = Join-Path $sandbox "local"
    $roamingCache = Join-Path $sandbox "roaming"
    $backupRoot = Join-Path $sandbox "backup"

    try {
        # 沙盒数据：设备树 + 设置树
        New-Item -Path $regDevice -Force | Out-Null
        Set-ItemProperty -Path $regDevice -Name "PriorityOrder" -Value "VGN:3554:F58E,LEOBOG:3554:FA09"
        Set-ItemProperty -Path $regDevice -Name "PrimaryDeviceId" -Value "VGN:3554:F58E"
        New-Item -Path ($regDevice + "\CompXFa09Cache") -Force | Out-Null
        Set-ItemProperty -Path ($regDevice + "\CompXFa09Cache") -Name "BatteryPercent" -Value 94 -Type DWord
        New-Item -Path ($regDevice + "\HiddenDevices") -Force | Out-Null
        Set-ItemProperty -Path ($regDevice + "\HiddenDevices") -Name "MCHOSE:3554:FA09" -Value 1 -Type DWord

        New-Item -Path $regSettings -Force | Out-Null
        Set-ItemProperty -Path $regSettings -Name "LastSelectedDevice" -Value "LEOBOG:3554:FA09"
        New-Item -Path ($regSettings + "\DeviceAppearance") -Force | Out-Null

        # 沙盒缓存：本地图标缓存 + 漫游运行缓存（含必须保留的 adapters）
        New-Item -Path (Join-Path $localCache "GameIcons") -ItemType Directory -Force | Out-Null
        New-Item -Path (Join-Path $localCache "GameTitles") -ItemType Directory -Force | Out-Null
        Set-Content -Path (Join-Path $localCache "GameIcons\code.png") -Value "icon" -Encoding ASCII
        Set-Content -Path (Join-Path $localCache "GameTitles\code.txt") -Value "title" -Encoding ASCII
        New-Item -Path (Join-Path $roamingCache "adapters") -ItemType Directory -Force | Out-Null
        Set-Content -Path (Join-Path $roamingCache "adapters\my.fp-adapter.json") -Value "{}" -Encoding ASCII
        New-Item -Path (Join-Path $roamingCache "ScriptCache") -ItemType Directory -Force | Out-Null
        Set-Content -Path (Join-Path $roamingCache "ScriptCache\c.dll") -Value "bin" -Encoding ASCII
        Set-Content -Path (Join-Path $roamingCache "runtime.bin") -Value "tmp" -Encoding ASCII

        # 注入沙盒路径
        $tManager.GetField("RegistryRootOverride", $bfPubStatic).SetValue($null, $regRoot)
        $tManager.GetField("LocalCacheRootOverride", $bfPubStatic).SetValue($null, $localCache)
        $tManager.GetField("RoamingCacheRootOverride", $bfPubStatic).SetValue($null, $roamingCache)
        $tManager.GetField("BackupRootOverride", $bfPubStatic).SetValue($null, $backupRoot)

        $report = $tManager.GetMethod("Execute", $bfPubStatic).Invoke($null, @())
        Assert-True ($null -ne $report) "report returned"
        Assert-True ([bool]$report.Success) ("execute succeeded: " + [string]$report.BuildSummary())

        # 注册表树必须清空
        Assert-True (-not (Test-Path $regDevice)) "device registry tree cleared"
        Assert-True (-not (Test-Path $regSettings)) "settings registry tree cleared"

        # 本地缓存整体删除
        Assert-True (-not (Test-Path $localCache)) "local cache dir removed"

        # 漫游缓存：只保留 adapters
        Assert-True (Test-Path (Join-Path $roamingCache "adapters\my.fp-adapter.json")) "adapters script preserved"
        Assert-True (-not (Test-Path (Join-Path $roamingCache "ScriptCache"))) "ScriptCache removed"
        Assert-True (-not (Test-Path (Join-Path $roamingCache "runtime.bin"))) "runtime cache file removed"

        # 备份产物
        $backupDir = [string]$report.BackupDirectory
        Assert-True ($backupDir.StartsWith($backupRoot)) "backup inside injected root"
        Assert-True (Test-Path (Join-Path $backupDir "HKCU_RazerBatteryTray.reg")) "device reg backup"
        Assert-True (Test-Path (Join-Path $backupDir "HKCU_FerrisPulse.reg")) "settings reg backup"
        Assert-True ((Get-Item (Join-Path $backupDir "HKCU_RazerBatteryTray.reg")).Length -gt 0) "device reg backup not empty"
        Assert-True ((Get-Item (Join-Path $backupDir "HKCU_FerrisPulse.reg")).Length -gt 0) "settings reg backup not empty"
        Assert-True (Test-Path (Join-Path $backupDir "README.txt")) "readme written"
        $readme = [System.IO.File]::ReadAllText((Join-Path $backupDir "README.txt"), [System.Text.Encoding]::UTF8)
        Assert-True ($readme -match "还原") "readme explains restore"
        Assert-True ($readme -match "adapters") "readme mentions adapters kept"
        Assert-True (Test-Path (Join-Path $backupDir "LocalAppData_FerrisPulse\GameIcons\code.png")) "icon cache backed up"

        # 报告契约
        Assert-True ([int]$report.ClearedRegistryTrees.Count -eq 2) "two registry trees cleared"
        Assert-True ([int]$report.PreservedPaths.Count -ge 2) "adapters + autostart preserved"
        $preservedJoined = ($report.PreservedPaths -join "|")
        Assert-True ($preservedJoined -match "adapters") "report lists adapters"
        Assert-True ($preservedJoined -match "Run") "report lists autostart kept"
    } finally {
        $tManager.GetField("RegistryRootOverride", $bfPubStatic).SetValue($null, $null)
        $tManager.GetField("LocalCacheRootOverride", $bfPubStatic).SetValue($null, $null)
        $tManager.GetField("RoamingCacheRootOverride", $bfPubStatic).SetValue($null, $null)
        $tManager.GetField("BackupRootOverride", $bfPubStatic).SetValue($null, $null)
        if (Test-Path ("HKCU:\" + $regRoot)) { Remove-Item -Path ("HKCU:\" + $regRoot) -Recurse -Force -ErrorAction SilentlyContinue }
        if (Test-Path $sandbox) { Remove-Item -Path $sandbox -Recurse -Force -ErrorAction SilentlyContinue }
    }
}

Invoke-TestCase "CacheClear.UiContractAndSharedRestart" {
    $tDialog = $asm.GetType("RazerBatteryTray.CacheClearConfirmDialog")
    Assert-True ($null -ne $tDialog) "CacheClearConfirmDialog exists"
    $confirmedProp = $tDialog.GetProperty("Confirmed", [System.Reflection.BindingFlags]"Public,Instance")
    Assert-True ($null -ne $confirmedProp) "Confirmed property"

    # About 页入口与共用重启方法
    $tAbout = $asm.GetType("RazerBatteryTray.AboutForm")
    Assert-True ($null -ne $tAbout.GetField("lnkClearCache", $bfNonPubInst)) "about link field"
    Assert-True ($null -ne $tAbout.GetMethod("OpenCacheClearDialog", $bfNonPubInst)) "about handler"
    $tReset = $asm.GetType("RazerBatteryTray.AppResetManager")
    Assert-True ($null -ne $tReset.GetMethod("RestartApplicationAndExit", $bfPubStatic)) "shared restart helper"

    # 对话框可实例化，默认未确认，且高度容纳 5 条清理项
    $dlg = [Activator]::CreateInstance($tDialog, @([float]1.0))
    try {
        Assert-True (-not [bool]$confirmedProp.GetValue($dlg, $null)) "default not confirmed"
        Assert-True ($dlg.ClientSize.Height -ge 300) "dialog tall enough for checklist"
        Assert-True (-not [string]::IsNullOrEmpty($dlg.Text)) "dialog has title"
    } finally {
        $dlg.Dispose()
    }
}
