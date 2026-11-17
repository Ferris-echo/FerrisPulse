# ============================================================================
# 测试用例: test_in_place_self_update.ps1
# 验证内容:
#   1. UpdateManager.BuildSelfReplaceCommand 伴随命令契约 (无黑框、Wait-Process、Move-Item、Start-Process)
#   2. MainForm 顶栏关于按钮 btnTopAbout 存在且挂接
#   3. 托盘右键菜单包含纯净项 "关于 FerrisPulse" 与 "检查更新"，且分类判定为 Category B (收起菜单)
# ============================================================================

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Get-Item "$scriptDir\..").FullName

# 1. 编译并加载待测程序集
$tempDll = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "FerrisPulse_Test_" + [System.Guid]::NewGuid().ToString("N") + ".dll")
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}

$sourceFiles = Get-ChildItem -Path "$repoRoot\src" -Recurse -Filter "*.cs" | Select-Object -ExpandProperty FullName
$sourceArgs = $sourceFiles -join '" "'

$compileCmd = "& `"$csc`" /target:library /optimize- /codepage:65001 /r:System.Drawing.dll /r:System.Windows.Forms.dll /out:`"$tempDll`" `"$sourceArgs`""
$output = Invoke-Expression $compileCmd

if ($LASTEXITCODE -ne 0 -or -not (Test-Path $tempDll)) {
    Write-Error "Compilation failed for test_in_place_self_update: `n$output"
    exit 1
}

try {
    $assembly = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($tempDll))

    # 1. 验证自替换命令生成
    $mgrType = $assembly.GetType("RazerBatteryTray.UpdateManager")
    $cmdMethod = $mgrType.GetMethod("BuildSelfReplaceCommand", [System.Reflection.BindingFlags]"Public,Static")
    if ($null -eq $cmdMethod) {
        Write-Error "FAIL: BuildSelfReplaceCommand not found on UpdateManager!"
        exit 1
    }

    $cmd = $cmdMethod.Invoke($null, @(1234, "C:\App\FerrisPulse.exe", "C:\App\FerrisPulse.exe.update"))
    if ($cmd -notmatch "Wait-Process -Id 1234") {
        Write-Error "FAIL: Command missing 'Wait-Process -Id 1234'!"
        exit 1
    }
    if ($cmd -notmatch "Move-Item") {
        Write-Error "FAIL: Command missing 'Move-Item'!"
        exit 1
    }
    if ($cmd -notmatch "Start-Process") {
        Write-Error "FAIL: Command missing 'Start-Process'!"
        exit 1
    }
    if ($cmd -notmatch "-WindowStyle Hidden") {
        Write-Error "FAIL: Command missing '-WindowStyle Hidden' for silent background execution!"
        exit 1
    }
    Write-Host "PASS: UpdateManager.BuildSelfReplaceCommand contract verified." -ForegroundColor Green

    # 2. 验证 MainForm 顶栏关于按钮
    $mainFormType = $assembly.GetType("RazerBatteryTray.MainForm")
    $mainForm = [System.Activator]::CreateInstance($mainFormType, @($false))

    $fieldBtnAbout = $mainFormType.GetField("btnTopAbout", [System.Reflection.BindingFlags]"NonPublic,Instance")
    if ($null -eq $fieldBtnAbout) {
        Write-Error "FAIL: MainForm missing 'btnTopAbout' field in top bar!"
        exit 1
    }
    $btnAbout = $fieldBtnAbout.GetValue($mainForm)
    if ($null -eq $btnAbout) {
        Write-Error "FAIL: btnTopAbout instance is null!"
        exit 1
    }
    Write-Host "PASS: MainForm.btnTopAbout top bar button verified." -ForegroundColor Green

    # 3. 验证托盘菜单项与 Category B 分类判定
    $trayMenuProp = $mainFormType.GetField("contextMenu", [System.Reflection.BindingFlags]"NonPublic,Instance")
    $trayMenu = $trayMenuProp.GetValue($mainForm)
    if ($null -eq $trayMenu) {
        Write-Error "FAIL: trayMenu is null on MainForm!"
        exit 1
    }

    $menuTexts = New-Object System.Collections.Generic.List[string]
    $itemAbout = $null
    $itemUpdate = $null

    foreach ($item in $trayMenu.Items) {
        if (-not [string]::IsNullOrEmpty($item.Text)) {
            $menuTexts.Add($item.Text)
            if ($item.Text -eq "关于 FerrisPulse") { $itemAbout = $item }
            if ($item.Text -eq "检查更新") { $itemUpdate = $item }
        }
    }

    if ($null -eq $itemAbout) {
        Write-Error "FAIL: Tray menu missing '关于 FerrisPulse'!"
        exit 1
    }
    if ($null -eq $itemUpdate) {
        Write-Error "FAIL: Tray menu missing '检查更新'!"
        exit 1
    }
    Write-Host "PASS: Tray menu items '关于 FerrisPulse' and '检查更新' present." -ForegroundColor Green

    # 验证分类判定 (应该返回 false，即属于 Category B，点击后关闭托盘菜单)
    $classifyMethod = $mainFormType.GetMethod("ShouldKeepTrayMenuOpen", [System.Reflection.BindingFlags]"Public,NonPublic,Instance")
    if ($null -eq $classifyMethod) {
        Write-Error "FAIL: ShouldKeepTrayMenuOpen classification method not found!"
        exit 1
    }

    $keepAbout = $classifyMethod.Invoke($mainForm, @($itemAbout))
    if ($keepAbout -ne $false) {
        Write-Error "FAIL: '关于 FerrisPulse' expected Category B (false), got $keepAbout"
        exit 1
    }
    $keepUpdate = $classifyMethod.Invoke($mainForm, @($itemUpdate))
    if ($keepUpdate -ne $false) {
        Write-Error "FAIL: '检查更新' expected Category B (false), got $keepUpdate"
        exit 1
    }
    Write-Host "PASS: Both items correctly categorized as Category B (CloseOnAction)." -ForegroundColor Green

    $mainForm.Dispose()
    Write-Host "ALL TESTS PASSED for test_in_place_self_update.ps1" -ForegroundColor Cyan
}
finally {
    try { Remove-Item $tempDll -Force -ErrorAction SilentlyContinue } catch {}
}
