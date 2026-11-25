# tests\test_app_update_startup_and_reset.ps1
# 整合测试套件：自更新服务/JSON解析/SHA256/弹窗呈现、开机自启自愈、桌面快捷方式规范、应用重置接管、纯净二进制及注册表防污染守卫
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "..\tools\TestHarness.ps1")

$asm = Load-FerrisPulseAssembly
$root = Get-FerrisPulseRoot
$BF = [System.Reflection.BindingFlags]"Public,NonPublic,Instance,Static"

Invoke-TestCase "1. UpdateManager 版本比对、JSON 解析、SHA256、镜像节点与原位替换命令" {
    $tUpd = $asm.GetType("RazerBatteryTray.UpdateManager", $true)
    $cmp = $tUpd.GetMethod("CompareVersions", $BF)
    Assert-True ([int]$cmp.Invoke($null, @("4.3.0", "4.2.0")) -gt 0) "4.3.0 > 4.2.0"
    Assert-Equal 0 ([int]$cmp.Invoke($null, @("v2.3.2", "2.3.2"))) "v2.3.2 == 2.3.2"
    Assert-True ([bool]$tUpd.GetMethod("IsNewerVersion", $BF).Invoke($null, @("4.3.0", "4.2.0"))) "IsNewerVersion true"
    Assert-True (-not [bool]$tUpd.GetMethod("IsNewerVersion", $BF).Invoke($null, @("4.2.0", "4.2.0"))) "IsNewerVersion equal false"

    $json = '{"latestVersion":"4.3.0","releaseDate":"2026-10-05","downloadUrl":"https://example.com/a.zip","sha256":"abc123","fileSize":12845056,"changelog":["A","B","C"]}'
    $info = $tUpd.GetMethod("ParseVersionJson", $BF).Invoke($null, @($json))
    Assert-True ($null -ne $info -and $info.LatestVersion -eq "4.3.0" -and $info.FileSize -eq 12845056 -and $info.Changelog.Count -eq 3) "ParseVersionJson"

    $eps = $tUpd.GetMethod("GetUpdateEndpoints", $BF).Invoke($null, @())
    Assert-True ($eps.Count -ge 4 -and ($eps -match "gitee\.com").Count -gt 0) "Failover endpoints include Gitee"

    $tmp = [IO.Path]::GetTempFileName()
    try {
        [IO.File]::WriteAllText($tmp, "FerrisPulse Test Content 12345", [Text.Encoding]::UTF8)
        $sha = [string]$tUpd.GetMethod("ComputeFileSha256", $BF).Invoke($null, @($tmp))
        Assert-Equal 64 $sha.Length "ComputeFileSha256 64 hex chars"
    } finally { Remove-Item $tmp -Force -ErrorAction SilentlyContinue }

    $tUpd.GetMethod("ClearPendingUpdate", $BF).Invoke($null, @()) | Out-Null
    Assert-True ($null -eq $tUpd.GetProperty("PendingUpdate", $BF).GetValue($null, $null)) "ClearPendingUpdate"

    $mgrSrc = [IO.File]::ReadAllText((Join-Path $root "src\Services\UpdateManager.cs"), [Text.Encoding]::UTF8)
    Assert-True ($mgrSrc -match "In-Process Native Rename" -and $mgrSrc -match "File\.Move\(exePath, oldExe\)") "AV-safe native rename preserved"
    Assert-True ($mgrSrc -notmatch "powershell\.exe" -and $mgrSrc -notmatch "-WindowStyle Hidden") "Zero hidden PowerShell updater payload in UpdateManager.cs"
}

Invoke-TestCase "2. ConfigManager 策略与 UpdateNoticeForm UI 呈现" {
    $tCfg = $asm.GetType("RazerBatteryTray.ConfigManager", $true)
    $tInfo = $asm.GetType("RazerBatteryTray.UpdateInfo", $true)
    $tForm = $asm.GetType("RazerBatteryTray.UpdateNoticeForm", $true)

    $origAuto = [bool]$tCfg.GetMethod("GetAutoCheckUpdate").Invoke($null, @())
    try {
        $tCfg.GetMethod("SetAutoCheckUpdate").Invoke($null, @($false)) | Out-Null
        Assert-True (-not [bool]$tCfg.GetMethod("GetAutoCheckUpdate").Invoke($null, @())) "AutoCheckUpdate set false"
        $tCfg.GetMethod("SetAutoCheckUpdate").Invoke($null, @($true)) | Out-Null
        Assert-True ([bool]$tCfg.GetMethod("GetAutoCheckUpdate").Invoke($null, @())) "AutoCheckUpdate set true"
    } finally {
        $tCfg.GetMethod("SetAutoCheckUpdate").Invoke($null, @($origAuto)) | Out-Null
    }

    $info = [Activator]::CreateInstance($tInfo)
    $info.LatestVersion = "4.3.0"
    $info.Changelog.Add("全新全自动在线热更新")
    $form = [Activator]::CreateInstance($tForm, @($info, "v4.2.0"))
    try {
        Assert-True ($form.FormBorderStyle -eq [System.Windows.Forms.FormBorderStyle]::None) "UpdateNoticeForm borderless"
        $texts = New-Object System.Collections.Generic.List[string]
        $q = New-Object System.Collections.Queue; $q.Enqueue($form)
        $hasProgress = $false
        while ($q.Count -gt 0) {
            $c = $q.Dequeue()
            if ($c.GetType().Name -eq "ModernProgressBar") { $hasProgress = $true }
            if (-not [string]::IsNullOrEmpty($c.Text)) { $texts.Add($c.Text) }
            foreach ($ch in $c.Controls) { $q.Enqueue($ch) }
        }
        $joined = $texts -join " | "
        Assert-True ($hasProgress -and $joined -match "立即升级" -and $joined -match "稍后提醒" -and $joined -match "4\.3\.0") "UpdateNoticeForm controls"
    } finally { $form.Dispose() }
}

Invoke-TestCase "3. StartupManager 自启双通道自愈与桌面快捷方式绝不静默生成" {
    $tStartup = $asm.GetType("RazerBatteryTray.StartupManager", $true)
    $mIsCfg = $tStartup.GetMethod("IsAutoStartConfigured", $BF)
    $mSet = $tStartup.GetMethod("SetAutoStart", $BF)
    $mFirstRun = $tStartup.GetMethod("EnsureDesktopShortcutFirstRun", $BF)
    $mAumid = $tStartup.GetMethod("EnsureAppShortcutWithAumid", $BF)
    $mCreateDesk = $tStartup.GetMethod("CreateDesktopShortcut", $BF)

    $origAuto = [bool]$mIsCfg.Invoke($null, @())
    try {
        # Heal disabled StartupApproved 0x03 marker back to enabled 0x02
        $appKey = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run")
        if ($null -ne $appKey) {
            $appKey.SetValue("RazerBatteryTray", [byte[]]@(0x03,0,0,0,0,0,0,0,0,0,0,0), [Microsoft.Win32.RegistryValueKind]::Binary)
            $appKey.Close()
        }
        $a = [object[]]@($true, $null)
        Assert-True ([bool]$mSet.Invoke($null, $a)) "SetAutoStart(true)"
        Assert-True ([bool]$mIsCfg.Invoke($null, @())) "IsAutoStartConfigured true"

        $aOff = [object[]]@($false, $null)
        Assert-True ([bool]$mSet.Invoke($null, $aOff)) "SetAutoStart(false)"
        Assert-True (-not [bool]$mIsCfg.Invoke($null, @())) "IsAutoStartConfigured false"
    } finally {
        $mSet.Invoke($null, [object[]]@($origAuto, $null)) | Out-Null
    }

    # Ensure Desktop shortcut is never created silently
    $desktop = [Environment]::GetFolderPath([Environment+SpecialFolder]::Desktop)
    $lnk = Join-Path $desktop "FerrisPulse · 灵脉.lnk"
    $legacyLnk = Join-Path $desktop "RazerBatteryTray.lnk"
    $hadLnk = Test-Path -LiteralPath $lnk
    $bakLnk = Join-Path $env:TEMP ("fp_desk_bak_" + [Guid]::NewGuid().ToString("N") + ".lnk")
    if ($hadLnk) { Copy-Item -LiteralPath $lnk -Destination $bakLnk -Force; Remove-Item -LiteralPath $lnk -Force }
    try {
        $rk = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey("Software\FerrisPulse")
        $rk.SetValue("DesktopShortcutInitialized", 0, [Microsoft.Win32.RegistryValueKind]::DWord); $rk.Close()
        $mFirstRun.Invoke($null, @($false)) | Out-Null
        Assert-True (-not (Test-Path -LiteralPath $lnk) -and -not (Test-Path -LiteralPath $legacyLnk)) "FirstRun(false) never creates Desktop shortcut"
        $mFirstRun.Invoke($null, @($true)) | Out-Null
        Assert-True (-not (Test-Path -LiteralPath $lnk)) "FirstRun(true) never creates Desktop shortcut"
        $mAumid.Invoke($null, $null) | Out-Null
        Assert-True (-not (Test-Path -LiteralPath $lnk)) "EnsureAppShortcutWithAumid never touches Desktop"
        Assert-True ([bool]$mCreateDesk.Invoke($null, @($false))) "Manual CreateDesktopShortcut works"
        Assert-True (Test-Path -LiteralPath $lnk) "Manual Desktop shortcut created"
    } finally {
        Remove-Item -LiteralPath $lnk -Force -ErrorAction SilentlyContinue
        if ($hadLnk -and (Test-Path -LiteralPath $bakLnk)) { Move-Item -LiteralPath $bakLnk -Destination $lnk -Force }
    }
}

Invoke-TestCase "4. AppResetManager 接管/隔离清理、纯净二进制与 TestRegistryGuard 验证" {
    $tReset = $asm.GetType("RazerBatteryTray.AppResetManager", $true)
    $mTake = $tReset.GetMethod("ShouldTakeOverProcess", $BF)
    $vOld = [Version]::Parse("2.0.1.0"); $vNew = [Version]::Parse("2.0.5.0")
    Assert-True ([bool]$mTake.Invoke($null, [object[]]@("C:\Old\FerrisPulse.exe", $vNew.PSObject.BaseObject, "D:\New\FerrisPulse.exe", $vNew.PSObject.BaseObject, "FerrisPulse"))) "Takeover diff path"
    Assert-True ([bool]$mTake.Invoke($null, [object[]]@("D:\New\FerrisPulse.exe", $vOld.PSObject.BaseObject, "D:\New\FerrisPulse.exe", $vNew.PSObject.BaseObject, "FerrisPulse"))) "Takeover old version"
    Assert-True (-not [bool]$mTake.Invoke($null, [object[]]@("D:\New\FerrisPulse.exe", $vNew.PSObject.BaseObject, "D:\New\FerrisPulse.exe", $vNew.PSObject.BaseObject, "FerrisPulse"))) "Skip identical process"

    $fRoot = $tReset.GetField("RegistryRootOverride", $BF)
    $vr = "Software\FerrisPulse_Reset_" + [Guid]::NewGuid().ToString("N")
    $vl = $vr + "_Legacy"
    $oR = $fRoot.GetValue($null)
    try {
        $fRoot.SetValue($null, $vr)
        [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($vr + "\Probe").Close()
        [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($vl + "\Hidden").Close()
        $tReset.GetMethod("ClearAllRegistryData", $BF).Invoke($null, @()) | Out-Null
        Assert-True ($null -eq [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($vr, $false)) "Virtual root deleted"
        Assert-True ($null -eq [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($vl, $false)) "Legacy virtual root deleted"
    } finally {
        $fRoot.SetValue($null, $oR)
        try { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($vr, $false) } catch {}
        try { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($vl, $false) } catch {}
    }

    # Build cleanliness + AboutForm Reset link placement
    $bat = [IO.File]::ReadAllText((Join-Path $root "build.bat"), [Text.Encoding]::UTF8)
    Assert-True ($bat -notmatch "Obfuscar\.Console\.exe") "Open-source un-obfuscated build"
    Assert-True ($null -ne $asm.GetType("RazerBatteryTray.Program")) "Clear Program type"
    $about = [Activator]::CreateInstance($asm.GetType("RazerBatteryTray.AboutForm"), @([float]1.0))
    try {
        $lnkReset = $about.GetType().GetField("lnkResetApp", $BF).GetValue($about)
        $lnkGit = $about.GetType().GetField("lnkGithub", $BF).GetValue($about)
        Assert-True ($null -ne $lnkReset -and $null -ne $lnkGit -and $lnkReset.Top -gt $lnkGit.Top) "Reset link below GitHub"
    } finally { $about.Dispose() }

    # TestRegistryGuard snapshot/restore integrity check
    . (Join-Path $root "tools\TestRegistryGuard.ps1")
    $testReg = "HKCU:\Software\FerrisPulse_GuardSelfTest"
    if (Test-Path $testReg) { Remove-Item $testReg -Recurse -Force }
    New-Item -Path $testReg -Force | Out-Null
    Set-ItemProperty -Path $testReg -Name "KeepKey" -Value "orig"
    $snap = Save-TestRegistrySnapshot -Roots @($testReg)
    try {
        Set-ItemProperty -Path $testReg -Name "KeepKey" -Value "dirty"
        New-Item -Path ($testReg + "\SubDirty") -Force | Out-Null
    } finally {
        Restore-TestRegistrySnapshot -Snapshot $snap
    }
    Assert-Equal "orig" ((Get-ItemProperty -Path $testReg).KeepKey) "TestRegistryGuard restored key"
    Assert-True (-not (Test-Path ($testReg + "\SubDirty"))) "TestRegistryGuard deleted dirty subkey"
    Remove-Item $testReg -Recurse -Force
}

Write-Host "`nALL APP UPDATE, STARTUP & RESET TESTS PASSED." -ForegroundColor Green
exit 0
