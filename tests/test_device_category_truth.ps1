$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$helper = Join-Path $root "src\Hardware\MchoseDeviceHelper.cs"
$mgr    = Join-Path $root "src\Hardware\DeviceManager.cs"
$wizard = Join-Path $root "src\UI\Forms\AddDeviceWizardForm.cs"

$script:pass = 0
$script:fail = 0
function OK([string]$m) { $script:pass++; Write-Host ("  PASS: " + $m) }
function NG([string]$m) { $script:fail++; Write-Host ("  FAIL: " + $m) -ForegroundColor Red }
function Chk([bool]$c, [string]$m) { if ($c) { OK $m } else { NG $m } }

Write-Host "=== [1] 源文件存在且编码合规 ===" -ForegroundColor Cyan
foreach ($f in @($helper, $mgr, $wizard)) {
    Chk (Test-Path $f) ("exists: " + (Split-Path -Leaf $f))
    $bytes = [System.IO.File]::ReadAllBytes($f)
    $hasBom = ($bytes.Length -ge 3 -and $bytes[0] -eq 0xEF -and $bytes[1] -eq 0xBB -and $bytes[2] -eq 0xBF)
    Chk $hasBom ("UTF-8 BOM: " + (Split-Path -Leaf $f))
    $txt = [System.Text.Encoding]::UTF8.GetString($bytes)
    Chk ($txt.IndexOf([string][char]13 + [string][char]13 + [string][char]10) -lt 0) ("no CRCRLF: " + (Split-Path -Leaf $f))
}

$helperTxt = [System.IO.File]::ReadAllText($helper)
$mgrTxt    = [System.IO.File]::ReadAllText($mgr)
$wizardTxt = [System.IO.File]::ReadAllText($wizard)

Write-Host "=== [2] 权威品类判定 API 存在且覆盖两族 ===" -ForegroundColor Cyan
Chk ($helperTxt.Contains("public static bool TryGetAuthoritativeCategory(")) "helper exposes TryGetAuthoritativeCategory"
Chk ($helperTxt.Contains("category = DeviceCategory.Mouse;")) "4D family resolves to Mouse"
Chk ($helperTxt.Contains("category = DeviceCategory.Keyboard;")) "keyboard family resolves to Keyboard"
Chk ($helperTxt.Contains("MchoseMouse4DHelper.IsSupportedPid(pid)")) "mouse branch uses SupportedPids"
Chk ($helperTxt.Contains("pid == PidG87V2")) "keyboard branch covers G87 V2"
Chk ($helperTxt.Contains("pid == PidG75Pro")) "keyboard branch covers G75 PRO"
Chk ($helperTxt.Contains("pid == PidZ75sWireless")) "keyboard branch covers Z75s"
Chk ($helperTxt.Contains("HardwareDriverBase.TryExtractVidPidFromString")) "uses shared VID/PID extractor"

Write-Host "=== [3] LoadCustomDevices 会改写错误品类并落盘 ===" -ForegroundColor Cyan
Chk ($mgrTxt.Contains("MchoseDeviceHelper.TryGetAuthoritativeCategory(cfg.DeviceId, cfg.HardwareFingerprint, out authoritativeCat)")) "load-time correction wired"
Chk ($mgrTxt.Contains("cfg.Category != authoritativeCat")) "load-time correction guarded by diff"
Chk ($mgrTxt.Contains("mergeTruthKnown")) "merge phase honors hardware truth"
Chk ($mgrTxt.Contains("existDev.Category = mergeTruthCat;")) "merge phase overrides stale registry value"

$loadIdx = $mgrTxt.IndexOf("public static List<CustomDeviceConfig> LoadCustomDevices()")
$corrIdx = $mgrTxt.IndexOf("MchoseDeviceHelper.TryGetAuthoritativeCategory(cfg.DeviceId, cfg.HardwareFingerprint, out authoritativeCat)")
Chk ($loadIdx -ge 0 -and $corrIdx -gt $loadIdx) "correction inside LoadCustomDevices"
$saveCall = $mgrTxt.IndexOf("SaveCustomDevice(cfg);", $corrIdx)
Chk ($saveCall -gt $corrIdx) "correction persists via SaveCustomDevice"

Write-Host "=== [4] 添加设备向导不再盲目继承错误品类 ===" -ForegroundColor Cyan
Chk ($wizardTxt.Contains("MchoseDeviceHelper.TryGetAuthoritativeCategory(devId, devId, out truthCat)")) "wizard checks hardware truth first"
Chk ($wizardTxt.Contains("return truthCat;")) "wizard returns authoritative category"
$trIdx = $wizardTxt.IndexOf("return truthCat;")
$matchIdx = $wizardTxt.IndexOf("var match = existingConfigs.Find", $wizardTxt.IndexOf("Func<string, DeviceCategory, DeviceCategory> resolveCategory"))
Chk ($trIdx -ge 0 -and $matchIdx -gt $trIdx) "truth check precedes registry lookup"

Write-Host "=== [5] 运行时：3837:1018/4028/4026 必须归类为 Mouse 且能力契约与传输形态一致 ===" -ForegroundColor Cyan
$exe = Join-Path $root "FerrisPulse.exe"
Chk (Test-Path $exe) "FerrisPulse.exe built"
if (Test-Path $exe) {
    $bytes = [System.IO.File]::ReadAllBytes($exe)
    $asm = [System.Reflection.Assembly]::Load($bytes)
    $dm = $asm.GetType("RazerBatteryTray.DeviceManager")
    $devices = $dm.GetMethod("QueryAllDevices").Invoke($null, $null)
    # 同一台 K7 V2 Ultra+ 有三种硬件身份，取决于用户当前把鼠标切到哪种形态：
    #   2.4G 接收器 → MCHOSE:3837:1018（USB 厂商通道 0x4D，DPI 与回报率可读写）
    #   USB 有线直连 → MCHOSE:3837:4028（同一套 0x4D 协议，能力与 2.4G 一致）
    #   蓝牙 BLE   → MCHOSE:3837:4026（无厂商通道，只有电量，DPI/回报率必须降级）
    # 三种形态互斥：BLE 在线时 2.4G 侧那张离线占位卡会被主动去重，避免同一台
    # 物理鼠标出现两张卡片。因此这里按"当前在线的那张卡"断言，不硬编码单一 ID。
    $mouse = $null; $kbd = $null
    foreach ($d in $devices) {
        if ($d.DeviceId -eq "MCHOSE:3837:1018" -or $d.DeviceId -eq "MCHOSE:3837:4028" -or $d.DeviceId -eq "MCHOSE:3837:4026") { $mouse = $d }
        if ($d.DeviceId -eq "MCHOSE:3837:3033") { $kbd = $d }
    }
    Chk ($mouse -ne $null) ("mouse enumerated in any transport (actual " + $(if ($mouse -ne $null) { $mouse.DeviceId } else { "none" }) + ")")
    Chk ($kbd -ne $null) "keyboard 3837:3033 enumerated"
    if ($mouse -ne $null) {
        $isBle = ($mouse.DeviceId -eq "MCHOSE:3837:4026")
        Chk ($mouse.Category.ToString() -eq "Mouse") ("mouse category = Mouse (actual " + $mouse.Category + ")")
        Chk ($mouse.DriverId -eq "BuiltIn:MchoseMouse") ("mouse driver = BuiltIn:MchoseMouse (actual " + $mouse.DriverId + ")")
        Chk ($mouse.Capabilities.SupportsBattery) "mouse supports battery"
        if ($isBle) {
            # 蓝牙形态：标准 GATT 电池服务只给百分比，没有厂商通道。
            Chk (-not $mouse.Capabilities.SupportsDpi) "BLE mouse honestly reports no DPI capability"
            Chk (-not $mouse.Capabilities.SupportsPollingRate) "BLE mouse honestly reports no polling rate capability"
            Chk (-not $mouse.Capabilities.SupportsDpiStages) "BLE mouse honestly reports no DPI stages"
        } else {
            Chk ($mouse.Capabilities.SupportsDpi) "mouse supports DPI"
            Chk ($mouse.Capabilities.SupportsPollingRate) "mouse supports polling rate"
            Chk ($mouse.Capabilities.SupportsDpiStages) "mouse supports DPI stages"
            Chk ($mouse.DpiStageCount -ge 1) "mouse reports at least one onboard stage"
            Chk ($mouse.DpiStages -ne $null -and $mouse.DpiStages.Length -eq $mouse.DpiStageCount) "stage count equals stage array length"
        }
    }
    if ($kbd -ne $null) {
        Chk ($kbd.Category.ToString() -eq "Keyboard") ("keyboard category = Keyboard (actual " + $kbd.Category + ")")
        Chk ($kbd.DriverId -eq "BuiltIn:MchoseKeyboard") ("keyboard driver = BuiltIn:MchoseKeyboard (actual " + $kbd.DriverId + ")")
    }
}

Write-Host "=== [6] 注册表不再残留错误品类 ===" -ForegroundColor Cyan
$regPath = "HKCU:\Software\RazerBatteryTray\CustomDevices\HID:3837:1018"
if (Test-Path $regPath) {
    $cat = (Get-ItemProperty $regPath).Category
    Chk ($cat -eq 0) ("registry Category for HID:3837:1018 = 0/Mouse (actual " + $cat + ")")
} else {
    OK "no stale registry entry for HID:3837:1018"
}

Write-Host ""
Write-Host ("RESULT: pass=" + $script:pass + " fail=" + $script:fail)
if ($script:fail -gt 0) {
    Write-Host "[FAILED] device category truthfulness tests"
    exit 1
}
Write-Host "[ALL PASSED] 品类权威纠错测试通过：VID/PID 事实优先，错误品类可自愈"
exit 0
