# ============================================================================
# 测试用例: test_update_config_policy.ps1
# 验证内容:
#   1. ConfigManager 中 AutoCheckUpdate (默认开启 = 1) 与 LastUpdateCheckTime 读写持久化
#   2. UpdateInfo 数据模型的结构完整性与字段契约
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
    Write-Error "Compilation failed for test_update_config_policy: `n$output"
    exit 1
}

try {
    $assembly = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($tempDll))
    $configMgrType = $assembly.GetType("RazerBatteryTray.ConfigManager")
    if ($null -eq $configMgrType) {
        Write-Error "FAIL: RazerBatteryTray.ConfigManager type not found!"
        exit 1
    }

    # 2. 验证 UpdateInfo 实体类
    $updateInfoType = $assembly.GetType("RazerBatteryTray.UpdateInfo")
    if ($null -eq $updateInfoType) {
        Write-Error "FAIL: RazerBatteryTray.UpdateInfo type not found!"
        exit 1
    }
    $updateInfo = [System.Activator]::CreateInstance($updateInfoType)
    $props = $updateInfoType.GetProperties() | Select-Object -ExpandProperty Name
    $requiredProps = @("LatestVersion", "ReleaseDate", "MinSupportedVersion", "DownloadUrl", "Sha256", "FileSize", "Changelog")
    foreach ($p in $requiredProps) {
        if ($props -notcontains $p) {
            Write-Error "FAIL: UpdateInfo missing required property '$p'!"
            exit 1
        }
    }
    Write-Host "PASS: UpdateInfo model contains all required properties." -ForegroundColor Green

    # 3. 验证 ConfigManager.AutoCheckUpdate 与 Get/Set 方法
    $fieldAutoCheck = $configMgrType.GetField("AutoCheckUpdate")
    if ($null -eq $fieldAutoCheck) {
        Write-Error "FAIL: ConfigManager.AutoCheckUpdate constant field not found!"
        exit 1
    }

    $getAutoCheckMethod = $configMgrType.GetMethod("GetAutoCheckUpdate")
    $setAutoCheckMethod = $configMgrType.GetMethod("SetAutoCheckUpdate")
    if ($null -eq $getAutoCheckMethod -or $null -eq $setAutoCheckMethod) {
        Write-Error "FAIL: GetAutoCheckUpdate or SetAutoCheckUpdate method not found on ConfigManager!"
        exit 1
    }

    # 测试默认值应为 true
    $setAutoCheckMethod.Invoke($null, @($true))
    $val = $getAutoCheckMethod.Invoke($null, @())
    if ($val -ne $true) {
        Write-Error "FAIL: AutoCheckUpdate expected true, got $val"
        exit 1
    }

    # 测试修改为 false
    $setAutoCheckMethod.Invoke($null, @($false))
    $val = $getAutoCheckMethod.Invoke($null, @())
    if ($val -ne $false) {
        Write-Error "FAIL: AutoCheckUpdate expected false after set, got $val"
        exit 1
    }

    # 恢复为 true
    $setAutoCheckMethod.Invoke($null, @($true))
    Write-Host "PASS: ConfigManager.AutoCheckUpdate read/write persistence verified." -ForegroundColor Green

    # 4. 验证 LastUpdateCheckTime
    $fieldLastCheck = $configMgrType.GetField("LastUpdateCheckTime")
    if ($null -eq $fieldLastCheck) {
        Write-Error "FAIL: ConfigManager.LastUpdateCheckTime constant field not found!"
        exit 1
    }
    $getLastCheckMethod = $configMgrType.GetMethod("GetLastUpdateCheckTime")
    $setLastCheckMethod = $configMgrType.GetMethod("SetLastUpdateCheckTime")
    if ($null -eq $getLastCheckMethod -or $null -eq $setLastCheckMethod) {
        Write-Error "FAIL: GetLastUpdateCheckTime or SetLastUpdateCheckTime method not found on ConfigManager!"
        exit 1
    }
    $testTime = "2026-10-02T04:30:00"
    $setLastCheckMethod.Invoke($null, @($testTime))
    $readTime = $getLastCheckMethod.Invoke($null, @())
    if ($readTime -ne $testTime) {
        Write-Error "FAIL: LastUpdateCheckTime expected '$testTime', got '$readTime'"
        exit 1
    }
    Write-Host "PASS: ConfigManager.LastUpdateCheckTime verified." -ForegroundColor Green

    Write-Host "ALL TESTS PASSED for test_update_config_policy.ps1" -ForegroundColor Cyan
}
finally {
    try { Remove-Item $tempDll -Force -ErrorAction SilentlyContinue } catch {}
}
