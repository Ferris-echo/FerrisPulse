# tests\test_custom_name_display.ps1
$asmPath = Join-Path $PSScriptRoot "..\FerrisPulse.exe"
if (-not (Test-Path $asmPath)) { $asmPath = "FerrisPulse.exe" }
$fullPath = (Resolve-Path $asmPath).Path
$bytes = [System.IO.File]::ReadAllBytes($fullPath)
$asm = [System.Reflection.Assembly]::Load($bytes)

$tInfo = $asm.GetType("RazerBatteryTray.MouseBatteryInfo")
$tCat = $asm.GetType("RazerBatteryTray.DeviceCategory")

# Case 1: CustomName is "鹏飞的buds4pro", DeviceName is "Buds 4 Pro"
$dev1 = [Activator]::CreateInstance($tInfo)
$dev1.Category = [Enum]::Parse($tCat, "Earbuds")
$dev1.DeviceName = "Buds 4 Pro"
$dev1.CustomName = "鹏飞的buds4pro"

if ($dev1.DisplayName -ne "鹏飞的buds4pro") {
    Write-Host ("FAIL Case 1 DisplayName: Expected '鹏飞的buds4pro', got '" + $dev1.DisplayName + "'") -ForegroundColor Red
    exit 1
}
Write-Host "PASS Case 1 DisplayName" -ForegroundColor Green

if ($dev1.FriendlyShortName -ne "鹏飞的buds4pro") {
    Write-Host ("FAIL Case 1 FriendlyShortName: Expected '鹏飞的buds4pro', got '" + $dev1.FriendlyShortName + "'") -ForegroundColor Red
    exit 1
}
Write-Host "PASS Case 1 FriendlyShortName" -ForegroundColor Green

# Case 2: CustomName is "鹏飞的buds4pro", DeviceName is also "鹏飞的buds4pro" (historical conflation)
$dev2 = [Activator]::CreateInstance($tInfo)
$dev2.Category = [Enum]::Parse($tCat, "Earbuds")
$dev2.DeviceName = "鹏飞的buds4pro"
$dev2.CustomName = "鹏飞的buds4pro"

if ($dev2.DisplayName -ne "鹏飞的buds4pro") {
    Write-Host ("FAIL Case 2 DisplayName: Expected '鹏飞的buds4pro', got '" + $dev2.DisplayName + "'") -ForegroundColor Red
    exit 1
}
Write-Host "PASS Case 2 DisplayName" -ForegroundColor Green

if ($dev2.FriendlyShortName -ne "鹏飞的buds4pro") {
    Write-Host ("FAIL Case 2 FriendlyShortName: Expected '鹏飞的buds4pro', got '" + $dev2.FriendlyShortName + "'") -ForegroundColor Red
    exit 1
}
Write-Host "PASS Case 2 FriendlyShortName" -ForegroundColor Green

# Case 3: Device without CustomName (default original name)
$dev3 = [Activator]::CreateInstance($tInfo)
$dev3.Category = [Enum]::Parse($tCat, "Earbuds")
$dev3.DeviceName = "Buds4 Pro"
$dev3.CustomName = $null

if ($dev3.DisplayName -ne "Buds4 Pro") {
    Write-Host ("FAIL Case 3 DisplayName: Expected 'Buds4 Pro', got '" + $dev3.DisplayName + "'") -ForegroundColor Red
    exit 1
}
Write-Host "PASS Case 3 DisplayName (fallback to model name)" -ForegroundColor Green

# Case 4: Rapoo VT3 uses the requested exact display name; sibling VT3S stays distinct.
$rapoo = [Activator]::CreateInstance($tInfo)
$rapoo.Brand = "Rapoo"
$rapoo.Category = [Enum]::Parse($tCat, "Mouse")
$rapoo.DeviceName = "雷柏 VT3"
if ($rapoo.DisplayName -ne "雷柏VT3") {
    Write-Host ("FAIL Case 4 DisplayName: Expected '雷柏VT3', got '" + $rapoo.DisplayName + "'") -ForegroundColor Red
    exit 1
}
Write-Host "PASS Case 4 Rapoo VT3 display name" -ForegroundColor Green

$rapoo.DeviceName = "雷柏 VT3S"
if ($rapoo.DisplayName -ne "VT3S") {
    Write-Host ("FAIL Case 4 VT3S DisplayName: Expected 'VT3S', got '" + $rapoo.DisplayName + "'") -ForegroundColor Red
    exit 1
}
Write-Host "PASS Case 4 VT3S remains distinct" -ForegroundColor Green

Write-Host "`nALL CUSTOM NAME TESTS PASSED!" -ForegroundColor Green
exit 0
