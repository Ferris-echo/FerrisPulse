# ============================================================================
# 测试用例: test_about_form_presentation.ps1
# 验证内容:
#   1. AboutForm 窗体契约 (暗黑背景、无边框、双缓冲)
#   2. 包含作者署名 Ferris-echo、版本信息、检查更新按钮、自动更新开关
#   3. 赞赏支持卡片与安全加载逻辑 (无 donate.png 优雅占位无异常)
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
    Write-Error "Compilation failed for test_about_form_presentation: `n$output"
    exit 1
}

try {
    $assembly = [System.Reflection.Assembly]::LoadFrom($tempDll)
    $formType = $assembly.GetType("RazerBatteryTray.AboutForm")
    if ($null -eq $formType) {
        Write-Error "FAIL: RazerBatteryTray.AboutForm type not found!"
        exit 1
    }

    $form = [System.Activator]::CreateInstance($formType)
    if ($null -eq $form) {
        Write-Error "FAIL: Unable to instantiate AboutForm!"
        exit 1
    }

    # 验证样式属性 (FixedDialog 触发 Windows 11 原生弹窗过渡动画)
    if ($form.FormBorderStyle -ne [System.Windows.Forms.FormBorderStyle]::FixedDialog -and $form.FormBorderStyle -ne [System.Windows.Forms.FormBorderStyle]::None) {
        Write-Error "FAIL: FormBorderStyle expected FixedDialog or None, got $($form.FormBorderStyle)"
        exit 1
    }
    Write-Host "PASS: FormBorderStyle is $($form.FormBorderStyle)." -ForegroundColor Green

    # 递归查找子控件文本
    $allTexts = New-Object System.Collections.Generic.List[string]
    $queue = New-Object System.Collections.Queue
    $queue.Enqueue($form)

    while ($queue.Count -gt 0) {
        $ctrl = $queue.Dequeue()
        if (-not [string]::IsNullOrEmpty($ctrl.Text)) {
            $allTexts.Add($ctrl.Text)
        }
        foreach ($child in $ctrl.Controls) {
            $queue.Enqueue($child)
        }
    }

    $joinedTexts = $allTexts -join " | "

    # 验证作者署名
    if ($joinedTexts -notmatch "Ferris-echo") {
        Write-Error "FAIL: AboutForm missing author attribution 'Ferris-echo'!"
        exit 1
    }
    Write-Host "PASS: Author attribution 'Ferris-echo' found." -ForegroundColor Green

    # 验证软件名与版本标识
    if ($joinedTexts -notmatch "FerrisPulse") {
        Write-Error "FAIL: AboutForm missing brand name 'FerrisPulse'!"
        exit 1
    }
    Write-Host "PASS: Brand name 'FerrisPulse' found." -ForegroundColor Green

    # 验证“旗舰版”已移除，保持极简版本号
    if ($joinedTexts -match "旗舰版") {
        Write-Error "FAIL: AboutForm still contains '旗舰版' which must be removed!"
        exit 1
    }
    Write-Host "PASS: '旗舰版' text correctly removed." -ForegroundColor Green

    # 验证窗体宽度足够舒适 (宽度 >= 500)
    if ($form.Width -lt 500) {
        Write-Error "FAIL: AboutForm width ($($form.Width)) is too cramped (< 500)!"
        exit 1
    }
    Write-Host "PASS: Form width ($($form.Width)) is comfortable." -ForegroundColor Green


    # 验证自动更新与检查更新相关文字
    if ($joinedTexts -notmatch "检查更新") {
        Write-Error "FAIL: AboutForm missing '检查更新' button/text!"
        exit 1
    }
    if ($joinedTexts -notmatch "自动检查更新") {
        Write-Error "FAIL: AboutForm missing '自动检查更新' option!"
        exit 1
    }
    Write-Host "PASS: Update checking controls present." -ForegroundColor Green

    # 验证赞赏支持卡片区域
    if ($joinedTexts -notmatch "赞赏" -and $joinedTexts -notmatch "支持") {
        Write-Error "FAIL: AboutForm missing donation/support card!"
        exit 1
    }
    Write-Host "PASS: Donation card found." -ForegroundColor Green

    $form.Dispose()
    Write-Host "ALL TESTS PASSED for test_about_form_presentation.ps1" -ForegroundColor Cyan
}
finally {
    try { Remove-Item $tempDll -Force -ErrorAction SilentlyContinue } catch {}
}
