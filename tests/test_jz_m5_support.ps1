$ErrorActionPreference = 'Stop'
# ============================================================================
# 测试脚本: test_jz_m5_support.ps1
# 目标: 验证京造 JZ M5 (362D:D107 Wireless Link-KM) 的抓包身份握手、电量/充电
#       解析、伪造帧拒绝、只读能力门禁、公版隔离与内置驱动挂载注册。
# 协议源: 官方 WebHID + wukong-driver 逆向 (docs/jz-m5-protocol)
# ⚠️ AI 维护准则 (AI MAINTENANCE DIRECTIVE - DO NOT REMOVE) ⚠️
# 严禁使用 Add-Type -Path，必须全内存反射装配，避免文件句柄锁死。
# ============================================================================
$ScriptDir = Split-Path -Parent $MyInvocation.MyCommand.Definition
$ProjectDir = (Resolve-Path "$ScriptDir/..").Path
$assembly = [Reflection.Assembly]::Load([IO.File]::ReadAllBytes((Join-Path $ProjectDir 'FerrisPulse.exe')))
function Assert($condition, $message) { if (-not $condition) { throw $message } }
function Parse-Hex([string]$hex) {
    $tokens = ($hex.Trim() -replace '\s+', ' ').Split(' ', [StringSplitOptions]::RemoveEmptyEntries)
    $bytes = New-Object byte[] $tokens.Length
    for ($i = 0; $i -lt $tokens.Length; $i++) { $bytes[$i] = [Convert]::ToByte($tokens[$i], 16) }
    return $bytes
}

# --- 1. 身份帧回放 (WebHID payload，无 report id) ---
$version = Parse-Hex '02 02 00 2D 36 07 D1 07 01 01 00 00 00 00 00 00 00 00 00 00'
Assert ([RazerBatteryTray.JzM5Helper]::IsVersionIdentity($version)) '版本身份帧 362D:D107 workMode=1 必须识别'
$winVersion = Parse-Hex 'B6 02 02 00 2D 36 07 D1 07 01 01 00 00 00 00 00 00 00 00 00 00 00'
Assert ([RazerBatteryTray.JzM5Helper]::IsVersionIdentity($winVersion)) 'Win32 含 report id 版本帧必须识别'
$badIdentity = Parse-Hex '02 02 00 34 34 60 D0 03 01 01 00 00 00 00 00 00 00 00 00 00'
Assert (-not [RazerBatteryTray.JzM5Helper]::IsVersionIdentity($badIdentity)) '异型号 Keychron 身份帧必须拒绝'

# --- 2. 电量样本回放：真实抓包 [19]=0x58 => 88% 放电 ---
$baseInfo = Parse-Hex '06 00 20 20 00 20 03 40 06 60 09 80 0C A0 0F 15 05 04 0A 58 00 00 00 00 00 00 32 00 00 00 04 04 04 04 04 04 04 04 04 04 00 00 00 00 01 02 00 00 00 03 05 00 00 00 00 00 00 00 00 00 00 00 00 00'
$pct = [int]0; $chg = [bool]$true
$ok = [RazerBatteryTray.JzM5Helper]::TryParseBattery($baseInfo, [ref]$pct, [ref]$chg)
Assert ($ok -and $pct -eq 88 -and -not $chg) '真实样本 0x58 必须解析为 88% 放电'

# --- 3. 充电位：bit7=1 + 0x58 => 88% 充电 ---
$charging = [byte[]]$baseInfo.Clone(); $charging[19] = 0xD8
$pct = [int]0; $chg = [bool]$false
$ok = [RazerBatteryTray.JzM5Helper]::TryParseBattery($charging, [ref]$pct, [ref]$chg)
Assert ($ok -and $pct -eq 88 -and $chg) 'bit7 充电位必须解析为 88% 充电'

# --- 4. 伪帧拒绝：命令不符 / 越界百分比 / 短包 ---
$wrongCmd = [byte[]]$baseInfo.Clone(); $wrongCmd[0] = 0x02
$pct = [int]123; $chg = [bool]$true
Assert (-not [RazerBatteryTray.JzM5Helper]::TryParseBattery($wrongCmd, [ref]$pct, [ref]$chg)) '非 0x06 命令帧必须拒绝'
$over = [byte[]]$baseInfo.Clone(); $over[19] = 0x7F
Assert (-not [RazerBatteryTray.JzM5Helper]::TryParseBattery($over, [ref]$pct, [ref]$chg)) '越界电量必须拒绝'
$short = New-Object byte[] 10
Assert (-not [RazerBatteryTray.JzM5Helper]::TryParseBattery($short, [ref]$pct, [ref]$chg)) '短包必须拒绝'

# --- 5. 端点四元组隔离 + 通用引擎避让 ---
$ep = New-Object RazerBatteryTray.HidDeviceDescriptor
$ep.VendorId = 0x362D; $ep.ProductId = 0xD107; $ep.UsagePage = 0xFFC1; $ep.Usage = 1
$ep.InputReportLength = 64; $ep.OutputReportLength = 64; $ep.FeatureReportLength = 0
$ep.DevicePath = '\\?\hid#vid_362d&pid_d107&mi_01#{4d1e55b2-f16f-11cf-88cb-001111000030}'
Assert ([RazerBatteryTray.JzM5Helper]::IsControlEndpoint($ep)) 'FFC1/0001 64/64/0 必须识别为配置端点'
$epBad = New-Object RazerBatteryTray.HidDeviceDescriptor
$epBad.VendorId = 0x362D; $epBad.ProductId = 0xD107; $epBad.UsagePage = 0xFF60; $epBad.Usage = 0x61
$epBad.InputReportLength = 33; $epBad.OutputReportLength = 33
Assert (-not [RazerBatteryTray.JzM5Helper]::IsControlEndpoint($epBad)) 'FF60/33B 键盘端点必须排除'
Assert (-not [RazerBatteryTray.UniversalGamingDeviceHelper]::IsSupportedGamingDevice(0x362D, $ep.DevicePath, 'Wireless Link-KM')) '通用引擎必须避让 JZ M5 接收器'
Assert ([RazerBatteryTray.UniversalGamingDeviceHelper]::IsSupportedGamingDevice(0x373B, '\\?\hid#vid_373b&pid_1089', 'ATK X1 Pro Max Wireless')) 'ATK 鼠标通用回归不受影响'

# --- 6. 内置驱动挂载与只读能力门禁 ---
$dm = [IO.File]::ReadAllText((Join-Path $ProjectDir 'src/Hardware/DeviceManager.cs'))
Assert ($dm.Contains('JzM5Helper.QueryDevices()')) 'DeviceManager 必须挂载 JZ M5 扫描'
$drv = [RazerBatteryTray.JzM5Driver]::new()
Assert ($drv.MatchesVidPid(0x362D, 0xD107)) 'JzM5Driver 必须匹配 VID:PID'
Assert (-not $drv.MatchesVidPid(0x3434, 0xD026)) 'JzM5Driver 不得匹配 Keychron M6'
$dev = [RazerBatteryTray.JzM5Helper]::CreateState($ep, 88, $false, $false, [DateTime]::Now)
$drv.PopulateCapabilities($dev)
Assert ($dev.Capabilities.SupportsBattery) '电量能力必须开启'
Assert (-not $dev.Capabilities.SupportsDpi -and -not $dev.Capabilities.SupportsPollingRate) '未经实机验证的 DPI/回报率写能力必须关闭'
Assert (-not $dev.SupportsDpiWrite -and -not $dev.SupportsPollingRateWrite) 'SupportsDpiWrite/SupportsPollingRateWrite 必须为 false'

Write-Host 'PASS: JZ M5 身份帧、88%/充电样本、伪帧拒绝、端点隔离、只读能力门禁与驱动挂载全部通过'
