# ============================================================================
# 测试脚本: test_app_reset_and_takeover.ps1
# 目标: 验证一键深度重置/卸载清理中枢 (AppResetManager)、跨路径/跨版本旧进程自动接管、
#       ResetConfirmDialog 双模式弹窗以及 AboutForm 顶部右上角重置按钮布局。
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = Resolve-Path "$ScriptDir\.."
Set-Location $ProjectDir

Write-Host "[TEST] Validating AppResetManager, Process Takeover & AboutForm Reset UI..." -ForegroundColor Cyan

$exePath = "$ProjectDir\FerrisPulse.exe"
if (-not (Test-Path $exePath)) {
    throw "FerrisPulse.exe not found!"
}

$asmBytes = [System.IO.File]::ReadAllBytes($exePath)
$asm = [System.Reflection.Assembly]::Load($asmBytes)

# 1. 验证 AppResetManager 类型与核心方法
$resetType = $asm.GetType("RazerBatteryTray.AppResetManager")
if ($null -eq $resetType) {
    throw "RazerBatteryTray.AppResetManager type not found!"
}

$takeoverMethod = $resetType.GetMethod("ShouldTakeOverProcess", [System.Reflection.BindingFlags]"Public,Static")
if ($null -eq $takeoverMethod) {
    throw "AppResetManager.ShouldTakeOverProcess method not found!"
}

[System.Version]$vOld = [System.Version]::Parse("2.0.1.0")
[System.Version]$vNew = [System.Version]::Parse("2.0.5.0")

# 1.1 不同路径的旧实例 -> 必须接管 ($true)
$resDiffPath = [bool]$takeoverMethod.Invoke($null, [object[]]@("C:\OldDir\FerrisPulse.exe", $vNew.PSObject.BaseObject, "D:\NewDir\FerrisPulse.exe", $vNew.PSObject.BaseObject, "FerrisPulse"))
if (-not $resDiffPath) {
    throw "ShouldTakeOverProcess should return true when executable paths differ!"
}

# 1.2 历史旧名进程 (RazerBatteryTray) -> 必须接管 ($true)
$resLegacyName = [bool]$takeoverMethod.Invoke($null, [object[]]@("D:\NewDir\RazerBatteryTray.exe", $vNew.PSObject.BaseObject, "D:\NewDir\FerrisPulse.exe", $vNew.PSObject.BaseObject, "RazerBatteryTray"))
if (-not $resLegacyName) {
    throw "ShouldTakeOverProcess should return true for legacy process name RazerBatteryTray!"
}

# 1.3 同路径但旧版本 -> 必须接管 ($true)
$resOldVer = [bool]$takeoverMethod.Invoke($null, [object[]]@("D:\NewDir\FerrisPulse.exe", $vOld.PSObject.BaseObject, "D:\NewDir\FerrisPulse.exe", $vNew.PSObject.BaseObject, "FerrisPulse"))
if (-not $resOldVer) {
    throw "ShouldTakeOverProcess should return true when existing process has older version!"
}

# 1.4 同路径且同版本 -> 不接管，走正常唤醒已有实例 ($false)
$resSame = [bool]$takeoverMethod.Invoke($null, [object[]]@("D:\NewDir\FerrisPulse.exe", $vNew.PSObject.BaseObject, "D:\NewDir\FerrisPulse.exe", $vNew.PSObject.BaseObject, "FerrisPulse"))
if ($resSame) {
    throw "ShouldTakeOverProcess should return false when path and version are identical!"
}
Write-Host "  [PASS] ShouldTakeOverProcess accurately detects legacy names, different paths, and older versions" -ForegroundColor Green

# 2. 验证 ClearAllRegistryData 清空两棵注册表树（通过 RegistryRootOverride 虚拟隔离，绝不污染真实配置）
$clearRegMethod = $resetType.GetMethod("ClearAllRegistryData", [System.Reflection.BindingFlags]"Public,Static")
if ($null -eq $clearRegMethod) {
    throw "AppResetManager.ClearAllRegistryData method not found!"
}

$overrideProp = $resetType.GetField("RegistryRootOverride", [System.Reflection.BindingFlags]"Public,Static")
if ($null -eq $overrideProp) {
    throw "AppResetManager.RegistryRootOverride field not found!"
}

$testVirtualRoot = "Software\FerrisPulse_UnitTest_Reset_" + [Guid]::NewGuid().ToString("N")
$testLegacyVirtualRoot = $testVirtualRoot + "_Legacy"

try {
    # 注入测试隔离树，确保永远不碰触用户真实的 HKCU\Software\FerrisPulse
    $overrideProp.SetValue($null, $testVirtualRoot)

    # 写入测试哨兵数据
    $k1 = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($testVirtualRoot + "\UnitTestSentinel")
    $k1.SetValue("Dummy", 1)
    $k1.Close()

    $k2 = [Microsoft.Win32.Registry]::CurrentUser.CreateSubKey($testLegacyVirtualRoot + "\HiddenDevices")
    $k2.SetValue("VID_1532&PID_00BE", 1)
    $k2.Close()

    # 执行清空
    $clearRegMethod.Invoke($null, @()) | Out-Null

    $checkVirtualFp = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($testVirtualRoot, $false)
    $checkVirtualLegacy = [Microsoft.Win32.Registry]::CurrentUser.OpenSubKey($testLegacyVirtualRoot, $false)
    if ($null -ne $checkVirtualFp) {
        $checkVirtualFp.Close()
        throw "Virtual test tree was not completely removed by ClearAllRegistryData!"
    }
    if ($null -ne $checkVirtualLegacy) {
        $checkVirtualLegacy.Close()
        throw "Virtual legacy test tree was not completely removed by ClearAllRegistryData!"
    }
    Write-Host "  [PASS] ClearAllRegistryData wiped test registry trees cleanly without touching real user keys" -ForegroundColor Green
}
finally {
    $overrideProp.SetValue($null, $null)
    try { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($testVirtualRoot, $false) } catch { }
    try { [Microsoft.Win32.Registry]::CurrentUser.DeleteSubKeyTree($testLegacyVirtualRoot, $false) } catch { }
}

# 3. 验证 ResetConfirmDialog 与 ResetActionMode
$modeEnum = $asm.GetType("RazerBatteryTray.ResetActionMode")
$dlgType = $asm.GetType("RazerBatteryTray.ResetConfirmDialog")
if ($null -eq $modeEnum -or $null -eq $dlgType) {
    throw "ResetActionMode or ResetConfirmDialog type not found!"
}

$dlg = [Activator]::CreateInstance($dlgType, @([single]1.0))
try {
    $propMode = $dlgType.GetProperty("SelectedMode")
    if ($null -eq $propMode -or $propMode.GetValue($dlg, $null).ToString() -ne "None") {
        throw "ResetConfirmDialog.SelectedMode default value should be None!"
    }
    Write-Host "  [PASS] ResetConfirmDialog instantiated cleanly with ResetActionMode.None default" -ForegroundColor Green
}
finally {
    $dlg.Dispose()
}

# 4. 验证 AboutForm 故障排查与深度重置链接布局（优雅置于 cardDonate 底部 GitHub 链接下方，顶部头部彻底恢复清爽）
$aboutType = $asm.GetType("RazerBatteryTray.AboutForm")
$about = [Activator]::CreateInstance($aboutType, @([single]1.0))
try {
    $bf = [System.Reflection.BindingFlags]"NonPublic,Instance"
    $fLnkReset = $aboutType.GetField("lnkResetApp", $bf)
    $fCardDonate = $aboutType.GetField("cardDonate", $bf)
    $fLnkGithub = $aboutType.GetField("lnkGithub", $bf)
    if ($null -eq $fLnkReset) {
        throw "AboutForm.lnkResetApp field not found!"
    }
    $lnkReset = $fLnkReset.GetValue($about)
    $cardDonate = $fCardDonate.GetValue($about)
    $lnkGithub = $fLnkGithub.GetValue($about)
    if ($null -eq $lnkReset) {
        throw "AboutForm.lnkResetApp is null!"
    }
    if ($lnkReset.Parent -ne $cardDonate) {
        throw "lnkResetApp must be placed inside cardDonate at the bottom!"
    }
    if ($lnkReset.Top -le $lnkGithub.Top) {
        throw "lnkResetApp must be placed below lnkGithub (lnkReset.Top=$($lnkReset.Top), lnkGithub.Top=$($lnkGithub.Top))!"
    }
    # 验证顶部头部不再包含突兀的方块重置按钮
    $fBtnReset = $aboutType.GetField("btnResetApp", $bf)
    if ($null -ne $fBtnReset) {
        throw "Old btnResetApp field in top header must be completely removed!"
    }
    Write-Host "  [PASS] AboutForm.lnkResetApp placed cleanly at bottom of cardDonate below GitHub link (Top=$($lnkReset.Top))" -ForegroundColor Green
}
finally {
    $about.Dispose()
}

Write-Host "[ALL PASSED] AppResetManager, Process Takeover & AboutForm Reset UI test suite succeeded!" -ForegroundColor Green
exit 0
