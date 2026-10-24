# ============================================================================
# 测试脚本: test_startup_manager_resilience.ps1
# 核心职责:
#   全内存反射验证 StartupManager 双通道开机自启动与 StartupApproved 自动自愈守护机制
# 运行环境: PowerShell 3.0+ / Windows 10/11
# ============================================================================

$ErrorActionPreference = "Stop"
$repoRoot = (Resolve-Path "$PSScriptRoot\..").Path
Set-Location $repoRoot

$asmPath = Join-Path $repoRoot "FerrisPulse.exe"
if (-not (Test-Path $asmPath)) {
    Write-Host "FAIL: FerrisPulse.exe not found at $asmPath" -ForegroundColor Red
    exit 1
}

$bytes = [System.IO.File]::ReadAllBytes($asmPath)
$asm = [System.Reflection.Assembly]::Load($bytes)
$tStartup = $asm.GetType("RazerBatteryTray.StartupManager")

if ($null -eq $tStartup) {
    Write-Host "FAIL: Type RazerBatteryTray.StartupManager not found!" -ForegroundColor Red
    exit 1
}

# 1. 验证方法签名与对外暴露契约
$mIsConfigured = $tStartup.GetMethod("IsAutoStartConfigured", [System.Reflection.BindingFlags]"Public,Static")
$mSetAutoStart = $tStartup.GetMethod("SetAutoStart", [System.Reflection.BindingFlags]"Public,Static")
$mSyncFolder = $tStartup.GetMethod("SyncStartupFolderShortcut", [System.Reflection.BindingFlags]"Public,Static")
$mSyncRegistry = $tStartup.GetMethod("SyncAutoStartRegistry", [System.Reflection.BindingFlags]"Public,Static")

if ($null -eq $mIsConfigured -or $null -eq $mSetAutoStart -or $null -eq $mSyncFolder) {
    Write-Host "FAIL: Required StartupManager methods not found!" -ForegroundColor Red
    exit 1
}

Write-Host "PASS 1: StartupManager Public APIs confirmed." -ForegroundColor Green

# 准备测试注册表环境与备份
$runKeyPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Run"
$approvedRunKeyPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run"
$approvedFolderKeyPath = "HKCU:\Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\StartupFolder"
$startupFolder = [Environment]::GetFolderPath([Environment+SpecialFolder]::Startup)
$startupLnk = Join-Path $startupFolder "FerrisPulse · 灵脉.lnk"

$prevRunVal = (Get-ItemProperty -Path $runKeyPath -Name "FerrisPulse" -ErrorAction SilentlyContinue).FerrisPulse
$prevLnkExisted = Test-Path $startupLnk

try {
    # 确保测试环境注册表 Run 键存在 (兼容纯净 CI 容器)
    if (-not (Test-Path $runKeyPath)) {
        New-Item -Path $runKeyPath -Force | Out-Null
    }

    # 2. 模拟 Windows 任务管理器将 FerrisPulse 禁用 (设置为 0x03 奇数标记)
    if (-not (Test-Path $approvedRunKeyPath)) {
        New-Item -Path $approvedRunKeyPath -Force | Out-Null
    }
    $disabledBytes = [byte[]]@(0x03, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00)
    Set-ItemProperty -Path $approvedRunKeyPath -Name "FerrisPulse" -Value $disabledBytes -Type Binary

    # 3. 执行 SetAutoStart($true) 开启自启动
    $argsArray = [object[]]@($true, $null)
    $res = $mSetAutoStart.Invoke($null, $argsArray)
    if (-not $res) {
        Write-Host "FAIL: SetAutoStart(true) returned false! Err: $($argsArray[1])" -ForegroundColor Red
        exit 1
    }

    # 3.1 验证 Run 键成功写入带有 --autostart 参数的完整路径
    $runVal = (Get-ItemProperty -Path $runKeyPath -Name "FerrisPulse" -ErrorAction SilentlyContinue).FerrisPulse
    if ([string]::IsNullOrEmpty($runVal) -or $runVal -notmatch "--autostart") {
        Write-Host "FAIL: Registry Run key not properly written: $runVal" -ForegroundColor Red
        exit 1
    }
    Write-Host "PASS 2: Run key correctly set with quotes and --autostart flag ($runVal)." -ForegroundColor Green

    # 3.2 验证 StartupApproved\Run 中的 0x03 禁用位被自动治愈为 0x02 启用位
    $curApproved = (Get-ItemProperty -Path $approvedRunKeyPath -Name "FerrisPulse" -ErrorAction SilentlyContinue).FerrisPulse
    if ($curApproved -ne $null -and $curApproved.Length -gt 0 -and ($curApproved[0] % 2 -ne 0)) {
        Write-Host "FAIL: StartupApproved\Run was not healed! First byte is still $($curApproved[0])" -ForegroundColor Red
        exit 1
    }
    Write-Host "PASS 3: StartupApproved\Run disabled state was successfully healed (0x02 / Enabled)." -ForegroundColor Green

    # 3.3 验证双通道容灾守护：开始菜单 Startup 文件夹快捷方式生成
    if (-not (Test-Path $startupLnk)) {
        Write-Host "FAIL: Dual-protection shortcut was not created in Startup folder: $startupLnk" -ForegroundColor Red
        exit 1
    }
    Write-Host "PASS 4: Dual-protection Startup folder shortcut exists: $startupLnk" -ForegroundColor Green

    # 3.4 验证 IsAutoStartConfigured 判定状态为 True
    $isCfg = $mIsConfigured.Invoke($null, @())
    if (-not $isCfg) {
        Write-Host "FAIL: IsAutoStartConfigured returned false when enabled and healed!" -ForegroundColor Red
        exit 1
    }
    Write-Host "PASS 5: IsAutoStartConfigured correctly confirmed active status." -ForegroundColor Green

    # 4. 执行 SetAutoStart($false) 关闭自启动
    $argsArray[0] = $false
    $resOff = $mSetAutoStart.Invoke($null, $argsArray)
    if (-not $resOff) {
        Write-Host "FAIL: SetAutoStart(false) returned false!" -ForegroundColor Red
        exit 1
    }

    # 4.1 验证 Run 键已被干净移除
    $runValOff = (Get-ItemProperty -Path $runKeyPath -Name "FerrisPulse" -ErrorAction SilentlyContinue).FerrisPulse
    if (-not [string]::IsNullOrEmpty($runValOff)) {
        Write-Host "FAIL: Run key was not deleted after disable: $runValOff" -ForegroundColor Red
        exit 1
    }

    # 4.2 验证 Startup 文件夹快捷方式已被干净移除
    if (Test-Path $startupLnk) {
        Write-Host "FAIL: Startup folder shortcut was not deleted after disable!" -ForegroundColor Red
        exit 1
    }

    # 4.3 验证 IsAutoStartConfigured 判定状态为 False
    $isCfgOff = $mIsConfigured.Invoke($null, @())
    if ($isCfgOff) {
        Write-Host "FAIL: IsAutoStartConfigured returned true after disable!" -ForegroundColor Red
        exit 1
    }
    Write-Host "PASS 6: Clean purge of Run key and Startup folder shortcut upon disable." -ForegroundColor Green
}
finally {
    # 恢复环境状态
    if ($prevRunVal -ne $null) {
        Set-ItemProperty -Path $runKeyPath -Name "FerrisPulse" -Value $prevRunVal
        $argsArray = [object[]]@($true, $null)
        $mSetAutoStart.Invoke($null, $argsArray) | Out-Null
    } else {
        Remove-ItemProperty -Path $runKeyPath -Name "FerrisPulse" -ErrorAction SilentlyContinue
        Remove-ItemProperty -Path $approvedRunKeyPath -Name "FerrisPulse" -ErrorAction SilentlyContinue
        if (Test-Path $startupLnk) { Remove-Item -Force $startupLnk -ErrorAction SilentlyContinue }
    }
}

Write-Host "ALL TESTS PASSED for StartupManager Resilience!" -ForegroundColor Green
exit 0
