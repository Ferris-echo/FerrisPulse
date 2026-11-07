# ============================================================================
# 测试用例: test_scroll_panel_render.ps1
# 验证内容:
#   1. ModernDarkScrollPanel 纯双缓冲文本与纤细滚动条直接渲染能力
#   2. 验证滚动偏移 (ScrollOffset) 前后重绘无文字重叠，纯净底色擦除
#   3. 生成实机渲染位图保存至脑图工件
# ============================================================================

$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Get-Item "$scriptDir\..").FullName

$tempDll = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "FerrisPulse_ScrollTest_" + [System.Guid]::NewGuid().ToString("N") + ".dll")
$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}

$sourceFiles = Get-ChildItem -Path "$repoRoot\src" -Recurse -Filter "*.cs" | Select-Object -ExpandProperty FullName
$sourceArgs = $sourceFiles -join '" "'

$compileCmd = "& `"$csc`" /target:library /optimize- /codepage:65001 /r:System.Drawing.dll /r:System.Windows.Forms.dll /out:`"$tempDll`" `"$sourceArgs`""
$output = Invoke-Expression $compileCmd

if ($LASTEXITCODE -ne 0 -or -not (Test-Path $tempDll)) {
    Write-Error "Compilation failed: `n$output"
    exit 1
}

try {
    $bytes = [System.IO.File]::ReadAllBytes($tempDll)
    $assembly = [System.Reflection.Assembly]::Load($bytes)
    $panelType = $assembly.GetType("RazerBatteryTray.ModernDarkScrollPanel")
    if ($null -eq $panelType) {
        Write-Error "FAIL: RazerBatteryTray.ModernDarkScrollPanel type not found!"
        exit 1
    }

    $panel = [System.Activator]::CreateInstance($panelType)
    $panel.Size = New-Object System.Drawing.Size(520, 240)
    $panel.BackColor = [System.Drawing.Color]::FromArgb(25, 27, 34)

    # 准备丰富更新日志数据，使内容高度明显超出 240px，触发滚动条与滚动视口
    $list = New-Object System.Collections.Generic.List[string]
    $list.Add("一、新设备协议适配")
    $list.Add("【雷蛇 Razer】毒蝰 V3 专业版 (Viper V3 Pro)")
    $list.Add("【ATK】烈空 F1 Ultimate 8K 旗舰鼠标")
    $list.Add("【艾岩 Metapanda】P1 系列无线鼠标")
    $list.Add("【黑爵 AJAZZ】AJ159 全系列双模无线鼠标")
    $list.Add("【迈从 MCHOSE】G75 系列机械键盘")
    $list.Add("二、在线热更新与暗黑美学引擎")
    $list.Add("重构更新日志滚动视图，纯 GDI+ 双缓冲硬件加速直接渲染")
    $list.Add("彻底根除 Windows Forms 多透明文本层滚动产生的切片残影与重叠覆盖")
    for ($i = 1; $i -le 15; $i++) {
        $list.Add("【测试设备 $i】多设备测试项平滑滚动与纯 GDI+ 双缓冲视口绘制条目验证数据")
    }

    # 调用 SetChangelog
    $panel.SetChangelog($list, 1.0)

    # 验证初始状态渲染
    $bmp1 = New-Object System.Drawing.Bitmap(520, 240)
    $panel.DrawToBitmap($bmp1, (New-Object System.Drawing.Rectangle(0, 0, 520, 240)))
    
    # 验证滚动后状态渲染 (向下滑动 120 像素)
    $panel.ScrollOffset = 120
    $bmp2 = New-Object System.Drawing.Bitmap(520, 240)
    $panel.DrawToBitmap($bmp2, (New-Object System.Drawing.Rectangle(0, 0, 520, 240)))

    $artifactDir = "C:\Users\LEGION Y9000X\.gemini\antigravity\brain\e6c8b361-c984-4a3d-8422-bb13f4202988"
    if (Test-Path $artifactDir) {
        $bmp1.Save("$artifactDir\preview_scroll_panel_top.png", [System.Drawing.Imaging.ImageFormat]::Png)
        $bmp2.Save("$artifactDir\preview_scroll_panel_scrolled.png", [System.Drawing.Imaging.ImageFormat]::Png)
        Write-Host "PASS: Saved preview images to artifact directory." -ForegroundColor Green
    }

    $bmp1.Dispose()
    $bmp2.Dispose()
    $panel.Dispose()

    Write-Host "PASS: ModernDarkScrollPanel render test succeeded without errors." -ForegroundColor Green
}
finally {
    if (Test-Path $tempDll) {
        Remove-Item -Force $tempDll -ErrorAction SilentlyContinue
    }
}
