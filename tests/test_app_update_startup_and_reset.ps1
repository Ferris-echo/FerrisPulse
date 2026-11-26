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
        Assert-True ($form.ClientSize.Width -gt 580 -and $form.ClientSize.Height -gt 520) "UpdateNoticeForm enlarged dimensions"
        $lnkHist = $tForm.GetField("lnkHistory", $BF).GetValue($form)
        Assert-True ($null -ne $lnkHist -and $lnkHist.Text -match "历代") "UpdateNoticeForm has history link"
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

        # 跨版本汇总日志呈现验证 (v2.3.2 -> v2.4.1)
        $infoCross = [Activator]::CreateInstance($tInfo)
        $infoCross.LatestVersion = "2.4.1"
        $infoCross.ReleaseDate = "2026-11-26"
        $infoCross.Changelog.Add("修复部分无线键盘兼容性")
        $formCross = [Activator]::CreateInstance($tForm, @($infoCross, "v2.3.2", [float]1.0))
        try {
            $logCtrl = $tForm.GetField("logScrollPanel", $BF).GetValue($formCross)
            $lblDate = $tForm.GetField("lblReleaseDate", $BF).GetValue($formCross)
            Assert-True ($lblDate.Text -match "共包含 2 个版本") "Cross-version span subtitle"
            Assert-True ($logCtrl.Text -match "v2\.4\.1" -and $logCtrl.Text -match "v2\.4\.0" -and (-not ($logCtrl.Text -match "\[v2\.3\.2"))) "Cumulative changelog spans 2.4.1 + 2.4.0 only"
            $bmp = New-Object System.Drawing.Bitmap(660, 420)
            try { $logCtrl.DrawToBitmap($bmp, (New-Object System.Drawing.Rectangle(0, 0, 660, 420))) } finally { $bmp.Dispose() }
        } finally { $formCross.Dispose() }
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

Invoke-TestCase "5. 内嵌历代日志、用户化文案、下载校验与 ChangelogHistoryForm 版本切换器" {
    $tRelSvc = $asm.GetType("RazerBatteryTray.ReleaseNotesService", $true)
    $tInfo = $asm.GetType("RazerBatteryTray.UpdateInfo", $true)
    $tUpd = $asm.GetType("RazerBatteryTray.UpdateManager", $true)
    $tHist = $asm.GetType("RazerBatteryTray.ChangelogHistoryForm", $true)

    $embedded = $tRelSvc.GetMethod("LoadEmbeddedReleaseNotes", $BF).Invoke($null, @())
    Assert-True ($embedded.Count -ge 10) "Embedded release-notes.json loaded >= 10 entries"
    $expectedLatest = (Get-Content (Join-Path $root "version.json") -Raw -Encoding UTF8 | ConvertFrom-Json).latestVersion
    Assert-Equal $expectedLatest $embedded[0].Version "Latest embedded release matches version.json"
    Assert-Equal "2.0.0" $embedded[$embedded.Count - 1].Version "Oldest embedded release is 2.0.0"

    $allText = New-Object System.Text.StringBuilder
    foreach ($r in $embedded) {
        $allText.AppendLine($r.Summary) | Out-Null
        foreach ($line in $r.Changelog) { $allText.AppendLine($line) | Out-Null }
    }
    $combinedNotes = $allText.ToString()
    foreach ($jargon in @("MultiTrayIconHost", "NIF_GUID", "successfulTid", "FDAS", "PerMonitorV2", "Driver Trace")) {
        Assert-True (-not $combinedNotes.Contains($jargon)) "User-facing notes free of jargon: $jargon"
    }

    # 验证 version.json 也同步去技术术语且无 history 污染
    $vjText = [IO.File]::ReadAllText((Join-Path $root "version.json"), [Text.Encoding]::UTF8)
    Assert-True (-not $vjText.Contains('"history"') -and (-not $vjText.Contains("MultiTrayIconHost"))) "version.json kept clean and jargon-free"

    # 跨版本聚合测试 (单版 vs 跨版 vs 无历史兜底)
    $mCum = $tRelSvc.GetMethod("BuildCumulativeChangelogLines", $BF)
    $upd241 = [Activator]::CreateInstance($tInfo)
    $upd241.LatestVersion = "2.4.1"
    $upd241.ReleaseDate = "2026-11-26"
    $upd241.Changelog.Add("新增多设备兼容性修复")

    $cArgs = [object[]]@($embedded, "v2.3.2", $upd241, 0)
    $cumLines = $mCum.Invoke($null, $cArgs)
    Assert-Equal 2 ([int]$cArgs[3]) "2.3.2 -> 2.4.1 spans 2 releases"
    $joinedCum = ($cumLines -join "`n")
    Assert-True ($joinedCum.Contains("@@VERSION@@v2.4.1") -and $joinedCum.Contains("@@VERSION@@v2.4.0") -and (-not $joinedCum.Contains("@@VERSION@@v2.3.2"))) "Cumulative lines contain 2.4.1 and 2.4.0 banners only"

    $sArgs = [object[]]@($embedded, "v2.4.0", $upd241, 0)
    $singleLines = $mCum.Invoke($null, $sArgs)
    Assert-Equal 1 ([int]$sArgs[3]) "2.4.0 -> 2.4.1 spans 1 release"
    Assert-True (-not (($singleLines -join "`n").Contains("@@VERSION@@"))) "Single-version upgrade omits redundant banner"

    $emptyPool = [Activator]::CreateInstance([System.Collections.Generic.List`1].MakeGenericType($asm.GetType("RazerBatteryTray.ReleaseNoteEntry")))
    $fArgs = [object[]]@($emptyPool, "v2.0.0", $upd241, 0)
    $fbLines = $mCum.Invoke($null, $fArgs)
    Assert-Equal 1 ([int]$fArgs[3]) "Empty history falls back to UpdateInfo changelog"
    Assert-True ($fbLines.Count -eq 1 -and $fbLines[0] -eq "新增多设备兼容性修复") "Fallback changelog exact match"

    # 可执行文件下载校验 (拦截 HTML / 过小文件 / 错误哈希, 放行合规 PE)
    $mVal = $tRelSvc.GetMethod("ValidateDownloadedExecutable", $BF)
    $tmpHtml = [IO.Path]::GetTempFileName()
    $tmpPe = [IO.Path]::GetTempFileName()
    try {
        [IO.File]::WriteAllText($tmpHtml, "<html>404 Not Found</html>", [Text.Encoding]::UTF8)
        $aBad = [object[]]@($tmpHtml, "", "")
        Assert-True (-not [bool]$mVal.Invoke($null, $aBad)) "Reject undersized HTML error page"

        $peBytes = New-Object byte[] 215040
        $peBytes[0] = 0x4D; $peBytes[1] = 0x5A
        [IO.File]::WriteAllBytes($tmpPe, $peBytes)
        $goodSha = [string]$tUpd.GetMethod("ComputeFileSha256", $BF).Invoke($null, @($tmpPe))

        $aOk = [object[]]@($tmpPe, $goodSha, "")
        Assert-True ([bool]$mVal.Invoke($null, $aOk)) "Accept valid PE executable with matching SHA256"

        $aNoSha = [object[]]@($tmpPe, "", "")
        Assert-True ([bool]$mVal.Invoke($null, $aNoSha)) "Accept valid PE executable when SHA256 omitted"

        $aBadSha = [object[]]@($tmpPe, ("0" * 64), "")
        Assert-True (-not [bool]$mVal.Invoke($null, $aBadSha)) "Reject valid PE executable with mismatched SHA256"
    } finally {
        Remove-Item $tmpHtml -Force -ErrorAction SilentlyContinue
        Remove-Item $tmpPe -Force -ErrorAction SilentlyContinue
    }

    # ChangelogHistoryForm UI 与版本切换按钮状态验证
    $histForm = [Activator]::CreateInstance($tHist, @([float]1.0))
    try {
        Assert-True ($histForm.ClientSize.Width -gt 600 -and $histForm.ClientSize.Height -gt 460) "ChangelogHistoryForm wide layout"
        $btnSw = $tHist.GetField("btnSwitchVersion", $BF).GetValue($histForm)
        $scrollLog = $tHist.GetField("logScrollPanel", $BF).GetValue($histForm)
        $curVer = [string]$tUpd.GetProperty("CurrentVersion", $BF).GetValue($null, $null)
        $mGetState = [System.Windows.Forms.Control].GetMethod("GetState", [System.Reflection.BindingFlags]"NonPublic,Instance")

        # 选中当前版本 -> 按钮显示且禁用，文案为“当前运行中”
        Assert-True ([bool]$histForm.SelectVersion($curVer)) "Select current version: $curVer"
        Assert-True ([bool]$mGetState.Invoke($btnSw, @(2)) -and (-not $btnSw.Enabled) -and $btnSw.Text -eq "当前运行中") "Current version button disabled"

        # 选中更旧版本 -> 回退到此版本
        Assert-True ([bool]$histForm.SelectVersion("2.0.1")) "Select historical v2.0.1"
        Assert-True ([bool]$mGetState.Invoke($btnSw, @(2)) -and $btnSw.Enabled -and $btnSw.Text -eq "回退到此版本") "Older version shows downgrade button"

        # 若当前版本低于 2.4.0，选中 2.4.0 -> 升级到此版本
        if ([int]$tUpd.GetMethod("CompareVersions", $BF).Invoke($null, @("2.4.0", $curVer)) -gt 0) {
            Assert-True ([bool]$histForm.SelectVersion("2.4.0")) "Select newer v2.4.0"
            Assert-True ([bool]$mGetState.Invoke($btnSw, @(2)) -and $btnSw.Enabled -and $btnSw.Text -eq "升级到此版本") "Newer version shows upgrade button"
        }

        # 选中全部版本 -> 隐藏切换按钮且内容含多版本横幅
        $histForm.SelectAllVersions()
        Assert-True (-not [bool]$mGetState.Invoke($btnSw, @(2))) "All-versions view hides switch button"
        Assert-True ($scrollLog.Text -match "v2\.4\.0" -and $scrollLog.Text -match "v2\.0\.0") "All-versions view contains historical releases"

        $bmp = New-Object System.Drawing.Bitmap(520, 420)
        try { $scrollLog.DrawToBitmap($bmp, (New-Object System.Drawing.Rectangle(0, 0, 520, 420))) } finally { $bmp.Dispose() }
    } finally { $histForm.Dispose() }

    # 界面缩放联动验证 (0.85x vs 1.00x)
    $histSmall = [Activator]::CreateInstance($tHist, @([float]0.85))
    $histNorm = [Activator]::CreateInstance($tHist, @([float]1.0))
    $updSmall = [Activator]::CreateInstance($asm.GetType("RazerBatteryTray.UpdateNoticeForm", $true), @($upd241, "v2.3.2", [float]0.85))
    $updNorm = [Activator]::CreateInstance($asm.GetType("RazerBatteryTray.UpdateNoticeForm", $true), @($upd241, "v2.3.2", [float]1.0))
    try {
        Assert-True ($histSmall.ClientSize.Width -lt $histNorm.ClientSize.Width -and $histSmall.ClientSize.Height -lt $histNorm.ClientSize.Height) "ChangelogHistoryForm follows UI zoom scale"
        Assert-True ($updSmall.ClientSize.Width -lt $updNorm.ClientSize.Width -and $updSmall.ClientSize.Height -lt $updNorm.ClientSize.Height) "UpdateNoticeForm follows UI zoom scale"
    } finally {
        $histSmall.Dispose(); $histNorm.Dispose(); $updSmall.Dispose(); $updNorm.Dispose()
    }

    # AboutForm 单按钮三态流转验证 (检查更新 -> 查看更新 -> 更新日志 [最新 / 失败])
    $tAbout = $asm.GetType("RazerBatteryTray.AboutForm", $true)
    $origPending = $tUpd.GetProperty("PendingUpdate", $BF).GetValue($null, $null)
    $origChecked = $tUpd.GetProperty("HasCompletedCheck", $BF).GetValue($null, $null)
    $origOk = $tUpd.GetProperty("LastCheckSucceeded", $BF).GetValue($null, $null)
    try {
        $tUpd.GetProperty("PendingUpdate", $BF).SetValue($null, $null, $null)
        $tUpd.GetProperty("HasCompletedCheck", $BF).SetValue($null, $false, $null)
        $tUpd.GetProperty("LastCheckSucceeded", $BF).SetValue($null, $false, $null)

        $ab = [Activator]::CreateInstance($tAbout, @([float]1.0))
        try {
            $btnChk = $tAbout.GetField("btnCheckUpdate", $BF).GetValue($ab)
            $lblSt = $tAbout.GetField("lblUpdateStatus", $BF).GetValue($ab)
            $mSyncBtn = $tAbout.GetMethod("UpdateAboutUpdateButtonState", $BF)

            # 1) 未检查过 -> 检查更新
            Assert-Equal "检查更新" $btnChk.Text "About button before check"

            # 2) 有新版本 -> 查看更新
            $newerUpd = [Activator]::CreateInstance($tInfo)
            $newerUpd.LatestVersion = "9.9.9"
            $tUpd.GetProperty("HasCompletedCheck", $BF).SetValue($null, $true, $null)
            $tUpd.GetProperty("LastCheckSucceeded", $BF).SetValue($null, $true, $null)
            $tUpd.GetProperty("PendingUpdate", $BF).SetValue($null, $newerUpd, $null)
            $mSyncBtn.Invoke($ab, @()) | Out-Null
            Assert-Equal "查看更新" $btnChk.Text "About button when newer version available"

            # 3) 已检查且为最新版 -> 更新日志
            $tUpd.GetProperty("PendingUpdate", $BF).SetValue($null, $null, $null)
            $tUpd.GetProperty("HasCompletedCheck", $BF).SetValue($null, $true, $null)
            $tUpd.GetProperty("LastCheckSucceeded", $BF).SetValue($null, $true, $null)
            $mSyncBtn.Invoke($ab, @()) | Out-Null
            Assert-Equal "更新日志" $btnChk.Text "About button when up-to-date"

            # 4) 检查失败 -> 更新日志 + 失败提示
            $tUpd.GetProperty("LastCheckSucceeded", $BF).SetValue($null, $false, $null)
            $mSyncBtn.Invoke($ab, @()) | Out-Null
            Assert-Equal "更新日志" $btnChk.Text "About button when check failed"
            Assert-True ($lblSt.Text -match "失败") "About status reflects check failure"
        } finally { $ab.Dispose() }
    } finally {
        $tUpd.GetProperty("PendingUpdate", $BF).SetValue($null, $origPending, $null)
        $tUpd.GetProperty("HasCompletedCheck", $BF).SetValue($null, $origChecked, $null)
        $tUpd.GetProperty("LastCheckSucceeded", $BF).SetValue($null, $origOk, $null)
    }
}

Write-Host "`nALL APP UPDATE, STARTUP & RESET TESTS PASSED." -ForegroundColor Green
exit 0
