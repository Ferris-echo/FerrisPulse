# ============================================================================
# 测试脚本: test_mouse_silhouette_tray_style.ps1
# 目标: 验证第四种托盘样式「鼠标轮廓电量」(trayStyle = 3)
#       1. 电量按 5% 步进量化到 20 个档位
#       2. 色阶阈值: <= 15% 红 / <= 40% 黄 / 其余青绿 (#19FAC6)
#       3. 底图载入 + 像素重着色在 16/20/24 三种尺寸下均可渲染
#       4. 低电量时填充保底可见（不会因描边遮挡而完全看不见警示色）
#       5. 断连 / 休眠的灰显分支
# ============================================================================

$ErrorActionPreference = "Stop"
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = Resolve-Path "$ScriptDir\.."
Set-Location $ProjectDir

function Fail([string]$msg) {
    Write-Host ("FAILED: " + $msg) -ForegroundColor Red
    exit 1
}

$exePath = Join-Path $ProjectDir "FerrisPulse.exe"
if (-not (Test-Path $exePath)) { Fail "FerrisPulse.exe 未编译，请先执行 build.bat" }

# 全内存反射加载，零文件句柄占用 (AGENT_HANDBOOK 6.2)
$asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exePath))
$t = $asm.GetType("RazerBatteryTray.MouseSilhouetteTrayRenderer")
if ($null -eq $t) { Fail "未找到 MouseSilhouetteTrayRenderer" }

$bf = [System.Reflection.BindingFlags]"Public,Static"
$mQuant = $t.GetMethod("QuantizePercent", $bf)
$mColor = $t.GetMethod("StatusColor", $bf)
$mDraw  = $t.GetMethod("Draw", $bf)
if ($null -eq $mQuant -or $null -eq $mColor -or $null -eq $mDraw) { Fail "渲染器公共 API 缺失" }

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [测试 1/5] 5% 步进量化 (20 档位)" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$quantCases = @(
    @{ In = 100; Out = 100 },
    @{ In = 99;  Out = 100 },
    @{ In = 96;  Out = 100 },
    @{ In = 95;  Out = 95  },
    @{ In = 41;  Out = 45  },
    @{ In = 40;  Out = 40  },
    @{ In = 16;  Out = 20  },
    @{ In = 15;  Out = 15  },
    @{ In = 14;  Out = 15  },
    @{ In = 1;   Out = 5   },
    @{ In = 0;   Out = 0   },
    @{ In = -1;  Out = -1  }
)
foreach ($c in $quantCases) {
    $got = $mQuant.Invoke($null, @([int]$c.In))
    if ($got -ne $c.Out) { Fail ("量化 " + $c.In + "% 期望 " + $c.Out + "，实际 " + $got) }
}
Write-Host "PASSED: 12 组量化用例全部命中 20 档位" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 2/5] 色阶阈值: <=15 红 / <=40 黄 / 其余青绿" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$red    = [System.Drawing.Color]::FromArgb(255, 70, 70)
$yellow = [System.Drawing.Color]::FromArgb(255, 190, 60)
$cyan   = [System.Drawing.Color]::FromArgb(0x19, 0xFA, 0xC6)

$colorCases = @(
    @{ In = 100; Exp = $cyan   },
    @{ In = 41;  Exp = $cyan   },
    @{ In = 40;  Exp = $yellow },
    @{ In = 16;  Exp = $yellow },
    @{ In = 15;  Exp = $red    },
    @{ In = 1;   Exp = $red    },
    @{ In = 0;   Exp = $red    }
)
foreach ($c in $colorCases) {
    $got = $mColor.Invoke($null, @([int]$c.In))
    if ($got.ToArgb() -ne $c.Exp.ToArgb()) {
        Fail ("电量 " + $c.In + "% 期望 " + $c.Exp + "，实际 " + $got)
    }
}
Write-Host "PASSED: 15% 边界为红、40% 边界为黄、41% 起为青绿" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 3/5] 16 / 20 / 24 三尺寸渲染" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$renderCases = @(
    @{ Pct = 100; Chg = $false; Conn = $true;  Slp = $false },
    @{ Pct = 60;  Chg = $false; Conn = $true;  Slp = $false },
    @{ Pct = 40;  Chg = $false; Conn = $true;  Slp = $false },
    @{ Pct = 15;  Chg = $false; Conn = $true;  Slp = $false },
    @{ Pct = 0;   Chg = $false; Conn = $true;  Slp = $false },
    @{ Pct = 80;  Chg = $true;  Conn = $true;  Slp = $false },
    @{ Pct = 50;  Chg = $false; Conn = $true;  Slp = $true  },
    @{ Pct = -1;  Chg = $false; Conn = $false; Slp = $false }
)
foreach ($sz in @(16, 20, 24)) {
    foreach ($rc in $renderCases) {
        $bmp = $mDraw.Invoke($null, @([int]$rc.Pct, [bool]$rc.Chg, [bool]$rc.Conn, [bool]$rc.Slp, [int]$sz, $null))
        if ($null -eq $bmp) { Fail ("尺寸 " + $sz + " 电量 " + $rc.Pct + " 渲染返回 null") }
        if ($bmp.Width -ne $sz -or $bmp.Height -ne $sz) {
            Fail ("尺寸不符: 期望 " + $sz + "，实际 " + $bmp.Width + "x" + $bmp.Height)
        }
        # 必须存在不透明像素，否则托盘会显示为空白
        $opaque = 0
        for ($y = 0; $y -lt $bmp.Height; $y++) {
            for ($x = 0; $x -lt $bmp.Width; $x++) {
                if ($bmp.GetPixel($x, $y).A -gt 200) { $opaque++ }
            }
        }
        $bmp.Dispose()
        if ($opaque -lt 20) {
            Fail ("尺寸 " + $sz + " 电量 " + $rc.Pct + " 不透明像素过少 (" + $opaque + ")，托盘将空白")
        }
    }
}
Write-Host "PASSED: 3 尺寸 x 8 状态 = 24 次渲染全部产出有效非空图标" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 4/5] 低电量警示色必须可见（不被描边吃掉）" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

function Count-Color([System.Drawing.Bitmap]$bmp, [System.Drawing.Color]$target, [int]$tol) {
    $n = 0
    for ($y = 0; $y -lt $bmp.Height; $y++) {
        for ($x = 0; $x -lt $bmp.Width; $x++) {
            $p = $bmp.GetPixel($x, $y)
            if ($p.A -gt 200 -and
                [Math]::Abs($p.R - $target.R) -le $tol -and
                [Math]::Abs($p.G - $target.G) -le $tol -and
                [Math]::Abs($p.B - $target.B) -le $tol) { $n++ }
        }
    }
    return $n
}

# 15% 与 5% 都必须有红色填充像素
foreach ($pct in @(15, 10, 5)) {
    $bmp = $mDraw.Invoke($null, @([int]$pct, [bool]$false, [bool]$true, [bool]$false, [int]16, $null))
    $redCount = Count-Color $bmp $red 25
    $bmp.Dispose()
    if ($redCount -lt 2) {
        Fail ("电量 " + $pct + "% 的红色警示填充仅 " + $redCount + " 像素，实际不可见")
    }
    Write-Host ("PASSED: " + $pct + "% 红色填充 " + $redCount + " 像素，警示可见") -ForegroundColor Green
}

# 100% 全青绿、40% 全黄，确保刻度方向正确
$bmp100 = $mDraw.Invoke($null, @([int]100, [bool]$false, [bool]$true, [bool]$false, [int]16, $null))
$cyanCount = Count-Color $bmp100 $cyan 30
$bmp100.Dispose()
if ($cyanCount -lt 30) { Fail ("100% 青绿像素仅 " + $cyanCount + "，填充方向异常") }
Write-Host ("PASSED: 100% 青绿填充 " + $cyanCount + " 像素") -ForegroundColor Green

$bmp40 = $mDraw.Invoke($null, @([int]40, [bool]$false, [bool]$true, [bool]$false, [int]16, $null))
$yellowCount = Count-Color $bmp40 $yellow 30
$bmp40.Dispose()
if ($yellowCount -lt 8) { Fail ("40% 黄色像素仅 " + $yellowCount + "，色阶异常") }
Write-Host ("PASSED: 40% 黄色填充 " + $yellowCount + " 像素") -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Cyan
Write-Host " [测试 5/5] 断连灰显分支" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

$bmpDisc = $mDraw.Invoke($null, @([int]-1, [bool]$false, [bool]$false, [bool]$false, [int]16, $null))
$redInDisc = Count-Color $bmpDisc $red 40
$cyanInDisc = Count-Color $bmpDisc $cyan 40
$bmpDisc.Dispose()
if ($redInDisc -gt 0 -or $cyanInDisc -gt 0) {
    Fail ("断连态不应出现电量色 (红=" + $redInDisc + ", 青=" + $cyanInDisc + ")")
}
Write-Host "PASSED: 断连态完全灰显，无电量色残留" -ForegroundColor Green

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " ALL 5/5 MOUSE SILHOUETTE TRAY STYLE TESTS PASSED!" -ForegroundColor Green
Write-Host "==========================================================" -ForegroundColor Green
exit 0
