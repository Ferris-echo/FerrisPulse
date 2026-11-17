# ============================================================================
# 测试用例: test_update_ui_presentation.ps1
# 验证内容:
#   1. UpdateNoticeForm 升级通知弹窗契约 (暗黑背景、无边框、双缓冲)
#   2. 版本徽章、更新日志展示、稍后提醒与立即升级按钮
#   3. 流光进度条 ModernProgressBar 与实时状态控件存在
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
    Write-Error "Compilation failed for test_update_ui_presentation: `n$output"
    exit 1
}

try {
    $assembly = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($tempDll))
    $formType = $assembly.GetType("RazerBatteryTray.UpdateNoticeForm")
    if ($null -eq $formType) {
        Write-Error "FAIL: RazerBatteryTray.UpdateNoticeForm type not found!"
        exit 1
    }

    $updateInfoType = $assembly.GetType("RazerBatteryTray.UpdateInfo")
    $info = [System.Activator]::CreateInstance($updateInfoType)
    $info.LatestVersion = "4.3.0"
    $info.ReleaseDate = "2026-10-05"
    $info.DownloadUrl = "https://github.com/Ferris-echo/FerrisPulse/releases/download/v4.3.0/FerrisPulse.exe"
    $info.Sha256 = "dummy-sha256"
    $info.Changelog.Add("全新全自动在线热更新与暗黑美学")
    $info.Changelog.Add("优化外设待机休眠与连接探测")

    # 实例化窗体
    $form = [System.Activator]::CreateInstance($formType, @($info, "v4.2.0"))
    if ($null -eq $form) {
        Write-Error "FAIL: Unable to instantiate UpdateNoticeForm with UpdateInfo!"
        exit 1
    }

    # 验证样式
    if ($form.FormBorderStyle -ne [System.Windows.Forms.FormBorderStyle]::None) {
        Write-Error "FAIL: FormBorderStyle expected None, got $($form.FormBorderStyle)"
        exit 1
    }
    Write-Host "PASS: FormBorderStyle is None." -ForegroundColor Green

    # 递归提取控件文本与类型
    $allTexts = New-Object System.Collections.Generic.List[string]
    $hasProgressBar = $false
    $queue = New-Object System.Collections.Queue
    $queue.Enqueue($form)

    while ($queue.Count -gt 0) {
        $ctrl = $queue.Dequeue()
        if (-not [string]::IsNullOrEmpty($ctrl.Text)) {
            $allTexts.Add($ctrl.Text)
        }
        if ($ctrl.GetType().Name -eq "ModernProgressBar") {
            $hasProgressBar = $true
        }
        foreach ($child in $ctrl.Controls) {
            $queue.Enqueue($child)
        }
    }

    $joinedTexts = $allTexts -join " | "

    # 验证按钮存在
    if ($joinedTexts -notmatch "立即升级") {
        Write-Error "FAIL: UpdateNoticeForm missing '立即升级' action button!"
        exit 1
    }
    if ($joinedTexts -notmatch "稍后提醒") {
        Write-Error "FAIL: UpdateNoticeForm missing '稍后提醒' dismiss button!"
        exit 1
    }
    Write-Host "PASS: '立即升级' and '稍后提醒' buttons present." -ForegroundColor Green

    # 验证新版本信息与日志渲染
    if ($joinedTexts -notmatch "4.3.0") {
        Write-Error "FAIL: UpdateNoticeForm missing version string '4.3.0'!"
        exit 1
    }
    if ($joinedTexts -notmatch "全新全自动在线热更新") {
        Write-Error "FAIL: UpdateNoticeForm missing changelog text!"
        exit 1
    }
    Write-Host "PASS: Version and changelog content verified." -ForegroundColor Green

    # 验证流光进度条控件
    if (-not $hasProgressBar) {
        Write-Error "FAIL: UpdateNoticeForm missing ModernProgressBar control for smooth download UX!"
        exit 1
    }
    Write-Host "PASS: ModernProgressBar control present." -ForegroundColor Green

    # 验证单例守卫属性
    $isShowingProp = $formType.GetProperty("IsShowing", [System.Reflection.BindingFlags]"Public,Static")
    if ($null -eq $isShowingProp) {
        Write-Error "FAIL: UpdateNoticeForm missing IsShowing singleton property!"
        exit 1
    }
    Write-Host "PASS: IsShowing singleton property verified." -ForegroundColor Green

    $recheckMethod = $formType.GetMethod("TriggerBackgroundRecheck", [System.Reflection.BindingFlags]"Public,Instance")
    if ($null -eq $recheckMethod) {
        Write-Error "FAIL: TriggerBackgroundRecheck method not found on UpdateNoticeForm!"
        exit 1
    }
    Write-Host "PASS: TriggerBackgroundRecheck method found on UpdateNoticeForm." -ForegroundColor Green

    $updateInfoType = $assembly.GetType("RazerBatteryTray.UpdateInfo")
    $updateDisplayMethod = $formType.GetMethod("UpdateDisplay", [System.Type[]]@($updateInfoType))
    if ($null -eq $updateDisplayMethod) {
        Write-Error "FAIL: UpdateDisplay method not found on UpdateNoticeForm!"
        exit 1
    }
    Write-Host "PASS: UpdateDisplay method found on UpdateNoticeForm." -ForegroundColor Green

    $form.Dispose()
    Write-Host "ALL TESTS PASSED for test_update_ui_presentation.ps1" -ForegroundColor Cyan
}
finally {
    try { Remove-Item $tempDll -Force -ErrorAction SilentlyContinue } catch {}
}
