# Unit and regression test for Universal High-DPI Adaptive Form System
# Tests DpiScaleHelper, DpiAwareForm, AboutForm, and secondary dialogs

Write-Host "Verifying Universal High-DPI Adaptive Form System..." -ForegroundColor Cyan

Add-Type -AssemblyName System.Windows.Forms
Add-Type -AssemblyName System.Drawing

# Test 1: Verify DpiScaleHelper existence and mathematical accuracy
$asm = [System.Reflection.Assembly]::LoadFrom((Join-Path (Get-Location) "FerrisPulse.exe"))
$helperType = $asm.GetType("RazerBatteryTray.DpiScaleHelper")

if ($helperType -eq $null) {
    Write-Host "FAIL: RazerBatteryTray.DpiScaleHelper type not found!" -ForegroundColor Red
    exit 1
}

$mScaleInt = $helperType.GetMethod("Scale", [Type[]]@([int], [float]))
$val = $mScaleInt.Invoke($null, @([int]100, [float]1.5))
if ($val -ne 150) {
    Write-Host "FAIL: DpiScaleHelper.Scale(100, 1.5) = $val, expected 150!" -ForegroundColor Red
    exit 1
}

$mScaleSize = $helperType.GetMethod("Scale", [Type[]]@([System.Drawing.Size], [float]))
$inSz = New-Object System.Drawing.Size(100, 200)
$sz = $mScaleSize.Invoke($null, @($inSz, [float]2.0))
if ($sz.Width -ne 200 -or $sz.Height -ne 400) {
    Write-Host "FAIL: DpiScaleHelper.Scale(Size(100,200), 2.0) = $sz, expected 200x400!" -ForegroundColor Red
    exit 1
}

Write-Host "PASS: DpiScaleHelper mathematical operations verified." -ForegroundColor Green

# Test 2: Verify DpiAwareForm base class contracts
$baseFormType = $asm.GetType("RazerBatteryTray.DpiAwareForm")
if ($baseFormType -eq $null) {
    Write-Host "FAIL: RazerBatteryTray.DpiAwareForm type not found!" -ForegroundColor Red
    exit 1
}

if (-not [System.Windows.Forms.Form].IsAssignableFrom($baseFormType)) {
    Write-Host "FAIL: DpiAwareForm does not inherit from System.Windows.Forms.Form!" -ForegroundColor Red
    exit 1
}

$propScale = $baseFormType.GetProperty("DpiScale")
if ($propScale -eq $null) {
    Write-Host "FAIL: DpiAwareForm.DpiScale property missing!" -ForegroundColor Red
    exit 1
}

$mApply = $baseFormType.GetMethod("OnApplyDpiScale", [System.Reflection.BindingFlags]"NonPublic,Instance")
if ($mApply -eq $null) {
    Write-Host "FAIL: DpiAwareForm.OnApplyDpiScale method missing!" -ForegroundColor Red
    exit 1
}

Write-Host "PASS: DpiAwareForm base class contracts verified." -ForegroundColor Green

# Test 3: Verify AboutForm inherits from DpiAwareForm and has zero text overlap at 200% DPI
$aboutType = $asm.GetType("RazerBatteryTray.AboutForm")
if ($aboutType -eq $null) {
    Write-Host "FAIL: RazerBatteryTray.AboutForm type not found!" -ForegroundColor Red
    exit 1
}

if (-not $baseFormType.IsAssignableFrom($aboutType)) {
    Write-Host "FAIL: AboutForm does not inherit from DpiAwareForm!" -ForegroundColor Red
    exit 1
}

# Create AboutForm at 2.0 scale (200% 4K DPI)
$aboutForm = $aboutType.GetConstructor(@([float])).Invoke(@([float]2.0))
if ($aboutForm -eq $null) {
    # If single param constructor not yet available, try parameterless
    $aboutForm = $aboutType.GetConstructor([Type]::EmptyTypes).Invoke($null)
}

# Force handle creation for layout resolution
$h = $aboutForm.Handle

# Find controls via reflection
$fLogo = $aboutType.GetField("lblLogo", [System.Reflection.BindingFlags]"NonPublic,Instance")
$fPrefix = $aboutType.GetField("lblAuthorPrefix", [System.Reflection.BindingFlags]"NonPublic,Instance")
$fCardDonate = $aboutType.GetField("cardDonate", [System.Reflection.BindingFlags]"NonPublic,Instance")
$fDonateDesc = $aboutType.GetField("lblDonateDesc", [System.Reflection.BindingFlags]"NonPublic,Instance")

if ($fLogo -ne $null -and $fPrefix -ne $null) {
    $lblLogo = $fLogo.GetValue($aboutForm)
    $lblPrefix = $fPrefix.GetValue($aboutForm)
    if ($lblPrefix.Top -lt $lblLogo.Bottom) {
        Write-Host "FAIL: AboutForm text overlap detected! lblAuthorPrefix.Top ($($lblPrefix.Top)) < lblLogo.Bottom ($($lblLogo.Bottom))!" -ForegroundColor Red
        $aboutForm.Dispose()
        exit 1
    }
    Write-Host "PASS: AboutForm authorship row is strictly below logo (no overlap at 200% DPI)." -ForegroundColor Green
}

if ($fCardDonate -ne $null -and $fDonateDesc -ne $null) {
    $cardDonate = $fCardDonate.GetValue($aboutForm)
    $lblDonateDesc = $fDonateDesc.GetValue($aboutForm)
    if ($lblDonateDesc.Right -gt $cardDonate.Width) {
        Write-Host "FAIL: AboutForm donate description truncated! Right ($($lblDonateDesc.Right)) > cardDonate.Width ($($cardDonate.Width))!" -ForegroundColor Red
        $aboutForm.Dispose()
        exit 1
    }
    Write-Host "PASS: AboutForm donate description fits comfortably inside cardDonate." -ForegroundColor Green

    $fQrWeChat = $aboutType.GetField("qrPanelWeChat", [System.Reflection.BindingFlags]"NonPublic,Instance")
    if ($fQrWeChat -ne $null) {
        $qrWeChat = $fQrWeChat.GetValue($aboutForm)
        if ($qrWeChat.Top -lt $lblDonateDesc.Bottom) {
            Write-Host "FAIL: AboutForm QR panel overlap detected! qrPanelWeChat.Top ($($qrWeChat.Top)) < lblDonateDesc.Bottom ($($lblDonateDesc.Bottom))!" -ForegroundColor Red
            $aboutForm.Dispose()
            exit 1
        }
        Write-Host "PASS: AboutForm QR panels are strictly below donate description with no truncation." -ForegroundColor Green
    }
}

$aboutForm.Dispose()
Write-Host "PASS: AboutForm DPI adaptation verified." -ForegroundColor Green

# Test 4: Verify all secondary dialog forms inherit from DpiAwareForm
$allSecondaryForms = @(
    "AboutForm",
    "AddDeviceWizardForm",
    "DevicePriorityForm",
    "DpiManagementDialog",
    "UpdateNoticeForm",
    "PromptDialog",
    "EasterEggQuoteForm",
    "GameProcessManagerForm",
    "KeyRolloverSpeedForm",
    "MouseRateTestDialog",
    "ModernDeviceSwitcherFlyout",
    "DpiOsdForm"
)

foreach ($formName in $allSecondaryForms) {
    $t = $asm.GetType("RazerBatteryTray.$formName")
    if ($t -eq $null) {
        Write-Host "FAIL: Form type RazerBatteryTray.$formName not found in assembly!" -ForegroundColor Red
        exit 1
    }
    if (-not $baseFormType.IsAssignableFrom($t)) {
        Write-Host "FAIL: $formName does not inherit from DpiAwareForm!" -ForegroundColor Red
        exit 1
    }
    Write-Host "PASS: $formName inherits from DpiAwareForm." -ForegroundColor Green
}

Write-Host "ALL HIGH-DPI TESTS PASSED SUCCESSFULLY!" -ForegroundColor Green
exit 0
