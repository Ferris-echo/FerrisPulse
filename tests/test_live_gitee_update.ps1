# ============================================================================
# 测试用例: test_live_gitee_update.ps1
# 验证内容:
#   通过 UpdateManager 真实联网向 Gitee (feitian-zhang-fei/ferris-pulse)
#   探测 version.json，验证国内免梯子真实可用性
# ============================================================================

$ErrorActionPreference = "Stop"

if ($env:GITHUB_ACTIONS -eq "true" -or $env:CI -eq "true") {
    Write-Host "PASS: 检测到海外 CI 环境，跳过跨洋 Gitee 探测以避免网络抖动与看门狗超时" -ForegroundColor Green
    exit 0
}
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Get-Item "$scriptDir\..").FullName

$csc = "C:\Windows\Microsoft.NET\Framework64\v4.0.30319\csc.exe"
if (-not (Test-Path $csc)) {
    $csc = "C:\Windows\Microsoft.NET\Framework\v4.0.30319\csc.exe"
}

$tempSrc = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "LiveTestRunner_" + [System.Guid]::NewGuid().ToString("N") + ".cs")
$tempDll = [System.IO.Path]::Combine([System.IO.Path]::GetTempPath(), "FerrisPulse_LiveGitee_" + [System.Guid]::NewGuid().ToString("N") + ".dll")

$runnerCode = @'
using System;
using RazerBatteryTray;

namespace RazerBatteryTray.Tests
{
    public static class LiveTestRunner
    {
        public static volatile bool Completed = false;
        public static volatile bool Success = false;
        public static string LatestVersion = "";
        public static string Error = "";

        public static void Run()
        {
            Completed = false;
            Success = false;
            LatestVersion = "";
            Error = "";

            UpdateManager.CheckUpdateAsync(delegate(bool ok, UpdateInfo info, string err)
            {
                Success = ok;
                if (ok && info != null)
                {
                    LatestVersion = info.LatestVersion;
                }
                else
                {
                    Error = err;
                }
                Completed = true;
            });
        }
    }
}
'@
[System.IO.File]::WriteAllText($tempSrc, $runnerCode, [System.Text.Encoding]::UTF8)

$sourceFiles = Get-ChildItem -Path "$repoRoot\src" -Recurse -Filter "*.cs" | Select-Object -ExpandProperty FullName
$allSources = @($tempSrc) + $sourceFiles
$sourceArgs = $allSources -join '" "'

$compileCmd = "& `"$csc`" /target:library /optimize- /codepage:65001 /r:System.Drawing.dll /r:System.Windows.Forms.dll /out:`"$tempDll`" `"$sourceArgs`""
$output = Invoke-Expression $compileCmd

if ($LASTEXITCODE -ne 0 -or -not (Test-Path $tempDll)) {
    Write-Error "Compilation failed: `n$output"
    exit 1
}

try {
    $assembly = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($tempDll))
    $runnerType = $assembly.GetType("RazerBatteryTray.Tests.LiveTestRunner")
    $runMethod = $runnerType.GetMethod("Run")

    $sw = [System.Diagnostics.Stopwatch]::StartNew()
    $runMethod.Invoke($null, @())

    $completedProp = $runnerType.GetField("Completed")
    $successProp = $runnerType.GetField("Success")
    $versionProp = $runnerType.GetField("LatestVersion")
    $errorProp = $runnerType.GetField("Error")

    while (-not $completedProp.GetValue($null) -and $sw.ElapsedMilliseconds -lt 25000) {
        [System.Threading.Thread]::Sleep(100)
    }

    $isCompleted = $completedProp.GetValue($null)
    $isSuccess = $successProp.GetValue($null)
    $latestVer = $versionProp.GetValue($null)
    $err = $errorProp.GetValue($null)

    if (-not $isCompleted) {
        Write-Error "TIMEOUT: Gitee check did not complete within 25 seconds!"
        exit 1
    }

    if (-not $isSuccess) {
        Write-Error "FAIL: Live check failed with error: $err"
        exit 1
    }

    Write-Host "PASS: Live Gitee check succeeded in $($sw.ElapsedMilliseconds) ms!" -ForegroundColor Green
    Write-Host "Discovered Version from Gitee: $latestVer" -ForegroundColor Cyan

    if ([string]::IsNullOrEmpty($latestVer) -or -not ($latestVer -match '^\d+\.\d+\.\d+$')) {
        Write-Error "FAIL: Expected valid semver (e.g. 2.0.1), got $latestVer"
        exit 1
    }

    Write-Host "ALL TESTS PASSED for test_live_gitee_update.ps1" -ForegroundColor Green
}
finally {
    try { Remove-Item $tempSrc -Force -ErrorAction SilentlyContinue } catch {}
    try { Remove-Item $tempDll -Force -ErrorAction SilentlyContinue } catch {}
}
