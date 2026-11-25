# ============================================================================
# FerrisPulse 测试公共底座 (TestHarness.ps1)
# 职责:
#   1. 严格遵循铁律 10：使用 [System.IO.File]::ReadAllBytes 全内存反射加载程序集；
#   2. 支持 $env:FERRISPULSE_TEST_BIN 指向临时校验产物，默认读取根目录 FerrisPulse.exe；
#   3. 强制确保沙盒标记激活，并提供轻量级断言与用例隔离容器。
# ============================================================================

$script:FerrisPulseCachedAsm = $null

function Get-FerrisPulseRoot {
    return (Resolve-Path (Join-Path $PSScriptRoot "..")).Path
}

function Get-FerrisPulseBinaryPath {
    $bin = $env:FERRISPULSE_TEST_BIN
    if ([string]::IsNullOrWhiteSpace($bin) -or -not (Test-Path $bin)) {
        $root = Get-FerrisPulseRoot
        $bin = Join-Path $root "FerrisPulse.exe"
        if (-not (Test-Path $bin)) {
            $bin = Join-Path $root "bin\FerrisPulse.exe"
        }
    }
    if (-not (Test-Path $bin)) {
        throw "FerrisPulse.exe not found at '$bin'"
    }
    return (Resolve-Path $bin).Path
}

function Load-FerrisPulseAssembly {
    if ($null -ne $script:FerrisPulseCachedAsm) {
        return $script:FerrisPulseCachedAsm
    }

    $env:FERRISPULSE_TEST_MODE = "1"
    $bin = Get-FerrisPulseBinaryPath

    Add-Type -AssemblyName System.Windows.Forms
    Add-Type -AssemblyName System.Drawing

    $bytes = [System.IO.File]::ReadAllBytes((Resolve-Path $bin).Path)
    $script:FerrisPulseCachedAsm = [System.Reflection.Assembly]::Load($bytes)

    $tBus = $script:FerrisPulseCachedAsm.GetType("RazerBatteryTray.HardwareCommandBus")
    if ($null -ne $tBus) {
        $prop = $tBus.GetProperty("DisableHardwareWriteForTesting", [System.Reflection.BindingFlags]"Public,Static")
        if ($null -ne $prop) { $prop.SetValue($null, $true, $null) }
    }

    return $script:FerrisPulseCachedAsm
}

function Assert-True {
    param(
        [bool]$Condition,
        [string]$Message
    )
    if (-not $Condition) {
        throw "ASSERT FAILED: $Message"
    }
}

function Assert-Equal {
    param(
        $Expected,
        $Actual,
        [string]$Message
    )
    if ($Expected -ne $Actual) {
        throw "ASSERT EQUAL FAILED ($Message): Expected='$Expected', Actual='$Actual'"
    }
}

function Invoke-TestCase {
    param(
        [string]$Name,
        [scriptblock]$Body
    )
    Write-Host "  [CASE] $Name" -ForegroundColor Cyan
    & $Body
    Write-Host "    -> PASS" -ForegroundColor Green
}

function New-SandboxMainForm {
    param(
        [bool]$AutoStart = $false
    )
    $asm = Load-FerrisPulseAssembly
    $tMain = $asm.GetType("RazerBatteryTray.MainForm", $true)
    return [Activator]::CreateInstance($tMain, @($AutoStart))
}
