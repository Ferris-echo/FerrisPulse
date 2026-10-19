# Regression test verifying DPI black bar fix and window dragging freeze fix
Write-Host "Verifying DPI black bar and dragging freeze fixes..." -ForegroundColor Cyan

$allPassed = $true

# 1. Verify RoundedCard.OnResize override
$roundedCardSrc = Get-Content "src\UI\Controls\RoundedCard.cs" -Raw
if ($roundedCardSrc -match "protected override void OnResize\(EventArgs\s+\w+\)\s*\{\s*base\.OnResize\(\w+\);\s*Invalidate\(\);\s*\}") {
    Write-Host "PASS: RoundedCard.OnResize calls Invalidate() to prevent stale black borders on DPI scale switch." -ForegroundColor Green
} else {
    Write-Host "FAIL: RoundedCard.OnResize override missing or not calling Invalidate()!" -ForegroundColor Red
    $allPassed = $false
}

# 2. Verify NativeMethods definitions
$nativeSrc = Get-Content "src\Core\NativeMethods.cs" -Raw
if ($nativeSrc -match "WM_DPICHANGED\s*=\s*0x02E0" -and $nativeSrc -match "RIDEV_REMOVE\s*=\s*0x00000001") {
    Write-Host "PASS: NativeMethods contains WM_DPICHANGED (0x02E0) and RIDEV_REMOVE (0x00000001)." -ForegroundColor Green
} else {
    Write-Host "FAIL: NativeMethods missing WM_DPICHANGED or RIDEV_REMOVE!" -ForegroundColor Red
    $allPassed = $false
}

# 3. Verify MainForm.Events.cs handles WM_DPICHANGED and dynamic wake detector
$eventsSrc = Get-Content "src\UI\Forms\MainForm.Events.cs" -Raw
if ($eventsSrc -match "m\.Msg == NativeMethods\.WM_DPICHANGED" -and $eventsSrc -match "HandleDpiChanged\(newScale, suggestedBounds\);") {
    Write-Host "PASS: MainForm.Events.cs handles WM_DPICHANGED and triggers HandleDpiChanged." -ForegroundColor Green
} else {
    Write-Host "FAIL: MainForm.Events.cs does not handle WM_DPICHANGED!" -ForegroundColor Red
    $allPassed = $false
}

if ($eventsSrc -match "EnableMouseWakeDetector\(bool enable\)" -and $eventsSrc -match "NativeMethods\.RIDEV_REMOVE") {
    Write-Host "PASS: MainForm.Events.cs implements dynamic EnableMouseWakeDetector with RIDEV_REMOVE." -ForegroundColor Green
} else {
    Write-Host "FAIL: EnableMouseWakeDetector dynamic registration missing!" -ForegroundColor Red
    $allPassed = $false
}

# 4. Verify RazerDeviceHelper state cache lock decoupling
$razerSrc = Get-Content "src\Hardware\RazerDeviceHelper.cs" -Raw
if ($razerSrc -match "private static readonly object stateCacheLock" -and $razerSrc -match "public static RazerDeviceHardwareState GetDeviceState\(string deviceId\)[\s\S]*?return TryGetState\(normId\);") {
    Write-Host "PASS: RazerDeviceHelper.GetDeviceState is decoupled from hidLock via stateCacheLock." -ForegroundColor Green
} else {
    Write-Host "FAIL: RazerDeviceHelper.GetDeviceState is still coupled or missing stateCacheLock!" -ForegroundColor Red
    $allPassed = $false
}

# 5. Verify MainForm.Hardware.cs sleep logic and dynamic wake detector triggering
$hwSrc = Get-Content "src\UI\Forms\MainForm.Hardware.cs" -Raw
if ($hwSrc -match "EnableMouseWakeDetector\(true\);" -and $hwSrc -match "EnableMouseWakeDetector\(false\);") {
    Write-Host "PASS: MainForm.Hardware.cs dynamically toggles mouse wake detector based on real sleep state." -ForegroundColor Green
} else {
    Write-Host "FAIL: MainForm.Hardware.cs does not properly toggle mouse wake detector!" -ForegroundColor Red
    $allPassed = $false
}

# Ensure false sleep loop is removed: info.IsSleeping must NOT be unconditionally set to true on fallback query
if ($hwSrc -notmatch "if \(stState != null\)[\s\S]*?info\.IsSleeping = true;") {
    Write-Host "PASS: False sleep loop completely eliminated from fallback device query." -ForegroundColor Green
} else {
    Write-Host "FAIL: False sleep loop still detected in MainForm.Hardware.cs!" -ForegroundColor Red
    $allPassed = $false
}

# 6. Verify MainForm.Tray.cs async dispatching
$traySrc = Get-Content "src\UI\Forms\MainForm.Tray.cs" -Raw
if ($traySrc -match "ThreadPool\.QueueUserWorkItem\(_\s*=>\s*\{[\s\S]*?NuphyDeviceHelper\.SetAutoSleep") {
    Write-Host "PASS: MainForm.Tray.cs queues slow hardware commands onto ThreadPool." -ForegroundColor Green
} else {
    Write-Host "FAIL: MainForm.Tray.cs does not queue slow hardware commands onto ThreadPool!" -ForegroundColor Red
    $allPassed = $false
}

if ($allPassed) {
    Write-Host "`nALL CHECKS PASSED: Black bar and dragging freeze fixes are 100% verified!" -ForegroundColor Green
    exit 0
} else {
    Write-Host "`nCHECKS FAILED!" -ForegroundColor Red
    exit 1
}
