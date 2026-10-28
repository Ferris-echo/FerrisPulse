$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$ui = Join-Path $root "src\UI\Forms\MainForm.UI.cs"

$script:pass = 0
$script:fail = 0
function OK([string]$m) { $script:pass++; Write-Host ("  PASS: " + $m) }
function NG([string]$m) { $script:fail++; Write-Host ("  FAIL: " + $m) -ForegroundColor Red }
function Chk([bool]$c, [string]$m) { if ($c) { OK $m } else { NG $m } }

Write-Host "=== [1] source file and encoding ===" -ForegroundColor Cyan
Chk (Test-Path $ui) "MainForm.UI.cs exists"
$bytes = [System.IO.File]::ReadAllBytes($ui)
$hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
Chk $hasBom "MainForm.UI.cs is UTF-8 with BOM"
$txt = [System.Text.Encoding]::UTF8.GetString($bytes)
Chk ($txt.IndexOf([string][char]13 + [string][char]13 + [string][char]10) -lt 0) "MainForm.UI.cs has no CRCRLF corruption"

Write-Host "=== [2] label column width is measured, not hardcoded 82px ===" -ForegroundColor Cyan
Chk ($txt.Contains("private int MeasurePerfLabelColumnW()")) "MeasurePerfLabelColumnW helper exists"
Chk ($txt.Contains("TextRenderer.MeasureText")) "helper measures real text width"
Chk ($txt.Contains("boxAndGap = 25")) "helper accounts for checkbox box + gap"
$hardcoded = 0
$idx = 0
while (($idx = $txt.IndexOf("int lblW = (int)(82 * dpiScale);", $idx)) -ge 0) { $hardcoded++; $idx += 10 }
Chk ($hardcoded -eq 0) ("no hardcoded 82px label column left (found " + $hardcoded + ")")
$measured = 0
$idx = 0
while (($idx = $txt.IndexOf("int lblW = MeasurePerfLabelColumnW();", $idx)) -ge 0) { $measured++; $idx += 10 }
Chk ($measured -ge 4) ("all layout sites use measured width (found " + $measured + ")")

Write-Host "=== [3] runtime geometry across DPI scales ===" -ForegroundColor Cyan
$exe = Join-Path $root "FerrisPulse.exe"
Chk (Test-Path $exe) "FerrisPulse.exe built"
if (Test-Path $exe) {
    Add-Type -AssemblyName System.Drawing
    Add-Type -AssemblyName System.Windows.Forms
    $asm = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($exe))
    $t = $asm.GetType("RazerBatteryTray.MainForm")
    $BF = [System.Reflection.BindingFlags]"NonPublic,Public,Instance,Static"
    $flags = [System.Windows.Forms.TextFormatFlags]::Left -bor [System.Windows.Forms.TextFormatFlags]::VerticalCenter -bor [System.Windows.Forms.TextFormatFlags]::SingleLine
    $form = [System.Activator]::CreateInstance($t, @([bool]$false))
    $form.Show()
    $el = 0
    while ($el -lt 2500) { [System.Windows.Forms.Application]::DoEvents(); [System.Threading.Thread]::Sleep(40); $el += 40 }
    $fld = { param($n) $t.GetField($n, $BF).GetValue($form) }
    $setFld = { param($n, $v) $t.GetField($n, $BF).SetValue($form, $v) }
    $invoke = { param($n) $t.GetMethod($n, $BF).Invoke($form, $null) }
    $chkRate = & $fld "chkAutoGameRate"
    $chkDpi = & $fld "chkAutoGameDpi"
    $rateArr = @(& $fld "btnGameRates")
    Chk ($chkRate -ne $null) "chkAutoGameRate exists"
    Chk ($chkDpi -ne $null) "chkAutoGameDpi exists"
    Chk ($rateArr.Count -gt 0) "btnGameRates exists"
    foreach ($scale in @(1.0, 1.25, 1.5, 1.75, 2.0)) {
        & $setFld "dpiScale" ([float]$scale)
        & $invoke "RelayoutPerformanceControls"
        $el = 0
        while ($el -lt 400) { [System.Windows.Forms.Application]::DoEvents(); [System.Threading.Thread]::Sleep(40); $el += 40 }
        $rTw = [System.Windows.Forms.TextRenderer]::MeasureText($chkRate.Text, $chkRate.Font, [System.Drawing.Size]::Empty, $flags).Width
        $dTw = [System.Windows.Forms.TextRenderer]::MeasureText($chkDpi.Text, $chkDpi.Font, [System.Drawing.Size]::Empty, $flags).Width
        $rTextRight = $chkRate.Left + 25 + $rTw
        $dTextRight = $chkDpi.Left + 25 + $dTw
        Chk ($rTextRight -le $chkRate.Right) ("scale " + $scale + ": game-rate text inside its own control")
        Chk ($dTextRight -le $chkDpi.Right) ("scale " + $scale + ": game-DPI text inside its own control")
        $firstRate = $rateArr[0]
        if ($firstRate -ne $null -and $firstRate.Visible) {
            Chk ($rTextRight -lt $firstRate.Left) ("scale " + $scale + ": game-rate text right=" + $rTextRight + " < first rate button left=" + $firstRate.Left)
        }
    }
    & $setFld "dpiScale" ([float]1.0)
    $form.Close()
}

Write-Host ""
Write-Host ("RESULT: pass=" + $script:pass + " fail=" + $script:fail)
if ($script:fail -gt 0) {
    Write-Host "[FAILED] perf label layout tests"
    exit 1
}
Write-Host "[ALL PASSED] perf label column width is measured from real text; rate buttons no longer cover labels"
exit 0
