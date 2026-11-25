# tests\test_game_opt_engine_and_ui.ps1
# 整合测试套件：全屏游戏优化引擎、多品牌/FDAS 设备自动识别与非鼠标隔离、GameProfile 序列化/匹配/清理、
# GameProcessManagerForm 双击定制与磁盘图标缓存、开关独立性、提示音缓冲、以及 DpiOsdForm 防顶替与布局规范
$ErrorActionPreference = "Stop"
. (Join-Path $PSScriptRoot "..\tools\TestHarness.ps1")

$asm = Load-FerrisPulseAssembly
$root = Get-FerrisPulseRoot
$BF = [System.Reflection.BindingFlags]"Public,NonPublic,Instance,Static"

$tMain = $asm.GetType("RazerBatteryTray.MainForm", $true)
$tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo", $true)
$tCat = $asm.GetType("RazerBatteryTray.DeviceCategory", $true)
$tProfile = $asm.GetType("RazerBatteryTray.GameProfile", $true)
$tGpm = $asm.GetType("RazerBatteryTray.GameProcessManagerForm", $true)
$tOsd = $asm.GetType("RazerBatteryTray.DpiOsdForm", $true)
$tList = $asm.GetType("RazerBatteryTray.ModernProcessListControl", $true)

Invoke-TestCase "1. GameProfile 模型序列化、FriendlyTitle 往返与 GameProcessManagerForm 交互" {
    $p = [Activator]::CreateInstance($tProfile, @("tslgame.exe", 4, 8000, "C:\Steam\tslgame.exe", "PUBG: 绝地求生"))
    $raw = [string]$tProfile.GetMethod("ToConfigString").Invoke($p, $null)
    Assert-True ($raw.Contains("title=")) "Serialized GameProfile contains title"
    $rest = $tProfile.GetMethod("Parse").Invoke($null, @($raw))
    Assert-Equal "PUBG: 绝地求生" $rest.FriendlyTitle "FriendlyTitle roundtrip"

    $dictType = [System.Collections.Generic.Dictionary`2].MakeGenericType(@([string], $tProfile))
    $dict = [Activator]::CreateInstance($dictType, @([StringComparer]::OrdinalIgnoreCase))
    $pCs = [Activator]::CreateInstance($tProfile, @("cs2.exe", 3, 2000, "C:\cs2.exe", "CS2"))
    $pCs.CustomDpiStage = 1; $pCs.CustomPollingRate = 1000
    $dict.Add("cs2.exe", $pCs)

    $ser = [string]$tProfile.GetMethod("SerializeProfiles", $BF).Invoke($null, @($dict))
    Assert-True ($ser.Contains("cs2.exe") -and $ser.Contains("dpi=1") -and $ser.Contains("rate=1000")) "SerializeProfiles"
    $deser = $tProfile.GetMethod("DeserializeProfiles", $BF).Invoke($null, @($ser))
    Assert-Equal 1 $deser.Count "DeserializeProfiles count"

    # GameProcessManagerForm + double-click safety + icon disk cache
    $monGames = New-Object 'System.Collections.Generic.List[string]'
    $monGames.Add("cs2.exe"); $monGames.Add("dota2.exe")
    $gpm = [Activator]::CreateInstance($tGpm, [object[]]@([float]1.0, $monGames.PSObject.BaseObject, $false, 1, $dict.PSObject.BaseObject, $null))
    try {
        $ctlMon = $tGpm.GetField("ctlMonitoredList", $BF).GetValue($gpm)
        $pnlMode = $tGpm.GetField("pnlModeCard", $BF).GetValue($gpm)
        Assert-True ($ctlMon.Height -ge 240 -and $pnlMode.Height -ge 65) "Expanded GPM layout"
        $items = $tList.GetMethod("GetItems").Invoke($ctlMon, $null)
        Assert-Equal 2 $items.Count "Monitored list count"
        $csItem = $items | Where-Object { $_.ProcessName -eq "cs2.exe" } | Select-Object -First 1
        Assert-True ($csItem.Subtitle.Contains("专属策略")) "CS2 custom strategy subtitle"

        # Double-click handler does NOT point to RemoveGameProcess
        $evFld = $ctlMon.GetType().GetField("OnItemDoubleClicked", $BF)
        Assert-True ($null -ne $evFld.GetValue($ctlMon)) "Double-click handler attached"
        Assert-True ($evFld.GetValue($ctlMon).Method.Name -ne "RemoveGameProcess") "Double-click does not delete game"

        $tGpm.GetMethod("RemoveGameProcess", [Type[]]@([string], [bool])).Invoke($gpm, @("cs2.exe", $false)) | Out-Null
        $gpmProfs = $tGpm.GetField("gameProfiles", $BF).GetValue($gpm)
        Assert-True (-not $gpmProfs.ContainsKey("cs2.exe")) "Removing game cleans its profile"
    } finally { $gpm.Dispose() }

    # Disk icon roundtrip
    $testExe = "fp_icon_test_" + [Guid]::NewGuid().ToString("N").Substring(0, 8) + ".exe"
    $pngPath = Join-Path ([Environment]::GetFolderPath([Environment+SpecialFolder]::LocalApplicationData)) ("FerrisPulse\GameIcons\" + [IO.Path]::GetFileNameWithoutExtension($testExe) + ".png")
    $bmp = New-Object System.Drawing.Bitmap(22, 22)
    try {
        $tGpm.GetMethod("SaveProcessIconToDisk", $BF, $null, [Type[]]@([string], [System.Drawing.Image]), $null).Invoke($null, [object[]]@($testExe, $bmp.PSObject.BaseObject)) | Out-Null
        Assert-True (Test-Path $pngPath) "Icon saved to disk"
        $restImg = $tGpm.GetMethod("GetCachedProcessIcon", $BF, $null, [Type[]]@([string]), $null).Invoke($null, @([string]$testExe))
        Assert-True ($null -ne $restImg -and $restImg.Width -eq 22) "Icon restored from disk"
        if ($null -ne $restImg) { $restImg.Dispose() }
    } finally {
        $bmp.Dispose()
        Remove-Item $pngPath -Force -ErrorAction SilentlyContinue
    }
}

Invoke-TestCase "2. 品牌支持矩阵、FDAS 合并、动态切档、音效缓冲与配置保护 (MainForm 单实例)" {
    $form = New-SandboxMainForm $false
    try {
        $null = $form.Handle
        $form.Visible = $true
        # Empty factory game list check in clean state / brand support
        $mSupp = $tMain.GetMethod("IsDeviceSupportingAutoGameRate", $BF)
        foreach ($b in @("Razer", "Logitech", "Rapoo", "Valkyrie", "Mchose")) {
            $m = [Activator]::CreateInstance($tInfo); $m.Brand = $b; $m.Category = [Enum]::Parse($tCat, "Mouse")
            Assert-True ([bool]$mSupp.Invoke($form, @($m))) "$b mouse supported"
        }
        $fdasMouse = [Activator]::CreateInstance($tInfo); $fdasMouse.DeviceId = "FDAS:3554:FB3E"; $fdasMouse.Category = [Enum]::Parse($tCat, "Mouse")
        $fdasMouse.Capabilities.SupportsPollingRate = $true; $fdasMouse.SupportedPollingRates = [int[]]@(125, 1000, 8000)
        Assert-True ([bool]$mSupp.Invoke($form, @($fdasMouse))) "FDAS mouse supported"

        foreach ($cat in @("Keyboard", "Headset")) {
            $nm = [Activator]::CreateInstance($tInfo); $nm.Brand = "NuPhy"; $nm.Category = [Enum]::Parse($tCat, $cat)
            Assert-True (-not [bool]$mSupp.Invoke($form, @($nm))) "$cat excluded from game opt"
        }

        # Expand / retract performance card + btnManageGames visibility
        $mSetOpt = $tMain.GetMethod("SetAutoGameOptEnabled", $BF)
        $mUpdPerf = $tMain.GetMethod("UpdatePerformanceSection", $BF)
        $tMain.GetField("selectedDevice", $BF).SetValue($form, $fdasMouse)
        $mSetOpt.Invoke($form, @($true)) | Out-Null
        $mUpdPerf.Invoke($form, @($fdasMouse, [System.Drawing.Color]::Lime)) | Out-Null
        $cardPerf = $tMain.GetField("cardPerformance", $BF).GetValue($form)
        $btnManage = $tMain.GetField("btnManageGames", $BF).GetValue($form)
        $scale = [float]$tMain.GetField("dpiScale", $BF).GetValue($form)
        $expH = [int](255 * $scale); $retH = [int](148 * $scale)
        $sw = [Diagnostics.Stopwatch]::StartNew()
        while ($cardPerf.Height -lt ($expH - 5) -and $sw.ElapsedMilliseconds -lt 1500) {
            [Threading.Thread]::Sleep(25); [System.Windows.Forms.Application]::DoEvents()
        }
        Assert-True ($cardPerf.Height -ge ($expH - 5) -and $btnManage.Visible) "Perf card expanded & btnManageGames visible"

        $mSetOpt.Invoke($form, @($false)) | Out-Null
        $mUpdPerf.Invoke($form, @($fdasMouse, [System.Drawing.Color]::Lime)) | Out-Null
        $sw.Restart()
        while ($cardPerf.Height -gt ($retH + 10) -and $sw.ElapsedMilliseconds -lt 1500) {
            [Threading.Thread]::Sleep(25); [System.Windows.Forms.Application]::DoEvents()
        }
        Assert-True ($cardPerf.Height -le ($retH + 10) -and -not $btnManage.Visible) "Perf card retracted & btnManageGames hidden"

        # Active game profile matching + dynamic badge update
        $curProc = [Diagnostics.Process]::GetCurrentProcess().ProcessName + ".exe"
        $mon = New-Object 'System.Collections.Generic.List[string]'; $mon.Add("dummy.exe"); $mon.Add($curProc)
        $dictType = [System.Collections.Generic.Dictionary`2].MakeGenericType(@([string], $tProfile))
        $pDict = [Activator]::CreateInstance($dictType, @([StringComparer]::OrdinalIgnoreCase))
        $pDict.Add($curProc, [Activator]::CreateInstance($tProfile, @($curProc, 4, 4000, "", "TargetGame")))
        $tMain.GetField("monitoredGameProcesses", $BF).SetValue($form, $mon.PSObject.BaseObject)
        $tMain.GetField("gameProfiles", $BF).SetValue($form, $pDict.PSObject.BaseObject)
        $matched = $tMain.GetMethod("GetActiveGameProfile", $BF).Invoke($form, $null)
        Assert-True ($null -ne $matched -and $matched.CustomPollingRate -eq 4000) "GetActiveGameProfile matches running process"

        # Anti-override OSD while gaming
        $osd = $tMain.GetField("osdForm", $BF).GetValue($form)
        $osd.ShowGameOptimization($true, $true, 1600, 1600, 3, 5, $true, 2000, "Razer")
        $tMain.GetField("lastGameOptOsdTime", $BF).SetValue($form, [DateTime]::Now)
        $mOnDpi = $tMain.GetMethod("OnDpiChanged", $BF, $null, [Type[]]@([string],[int],[int],[int],[string],[int]), $null)
        $mOnDpi.Invoke($form, @("Razer", 3000, 4, 5, "FDAS:3554:FB3E", 2250)) | Out-Null
        Assert-True ([bool]$tOsd.GetField("isDualOptimizationMode", $BF).GetValue($osd)) "OnDpiChanged blocked during GameOpt OSD window"
    } finally { $form.Dispose() }

    # Bell WAV trailing silence guard
    $wav = [byte[]]$tMain.GetMethod("GenerateBellChimeWav", $BF).Invoke($null, @([double[]]@(784.0, 1046.5), [double[]]@(0.10, 0.26)))
    Assert-True ($wav.Length -gt 2000) "GenerateBellChimeWav produces valid buffer"
    $tailZeros = 0
    for ($i = $wav.Length - 1; $i -ge 44; $i--) { if ($wav[$i] -eq 0) { $tailZeros++ } else { break } }
    Assert-True ($tailZeros -ge 1000) "Audio tail zero padding ($tailZeros)"
}

Invoke-TestCase "3. DpiOsdForm 双开卡片抗顶替与视觉排版契约" {
    $osd = [Activator]::CreateInstance($tOsd, @([float]1.0))
    try {
        $osd.ShowGameOptimization($true, $true, 1600, 1600, 3, 5, $true, 4000, "Razer")
        Assert-True ([bool]$tOsd.GetField("isDualOptimizationMode", $BF).GetValue($osd)) "Enter dual opt mode"
        Assert-True ($osd.Width -ge 240 -and $osd.Height -ge 70) "Dual OSD dimensions ($($osd.Size))"
        $osd.ShowDpi(3000, 2250, 4, 5, "Razer")
        Assert-True ([bool]$tOsd.GetField("isDualOptimizationMode", $BF).GetValue($osd)) "ShowDpi cannot override active Dual OSD"
        $osd.ShowRate(4000, "电竞回报率", "Razer")
        Assert-True ([bool]$tOsd.GetField("isDualOptimizationMode", $BF).GetValue($osd)) "ShowRate cannot override active Dual OSD"
        $osd.HideOsd()
        $osd.ShowDpi(1600, 1600, 3, 5, "Razer")
        Assert-True (-not [bool]$tOsd.GetField("isDualOptimizationMode", $BF).GetValue($osd)) "Single ShowDpi works after Hide"
    } finally { $osd.Dispose() }

    $osdSrc = [IO.File]::ReadAllText((Join-Path $root "src\UI\Forms\DpiOsdForm.cs"), [Text.Encoding]::UTF8)
    Assert-True (-not $osdSrc.Contains("⚡ 全屏电竞性能优化") -and $osdSrc.Contains("18.5f * dpiScale")) "Clean OSD typography"
}

Write-Host "`nALL GAME OPT ENGINE & UI TESTS PASSED." -ForegroundColor Green
exit 0
