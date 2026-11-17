# ============================================================================
# 测试用例: test_update_service.ps1
# 验证内容:
#   1. UpdateManager.CompareVersions 版本比对算法 (两段式/三段式/带v前缀/数值比较)
#   2. UpdateManager.IsNewerVersion 判定
#   3. UpdateManager.ParseVersionJson 零依赖 JSON 解析
#   4. UpdateManager.ComputeFileSha256 文件哈希校验
#   5. UpdateManager.GetUpdateEndpoints 容灾节点列表契约
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
    Write-Error "Compilation failed for test_update_service: `n$output"
    exit 1
}

try {
    $assembly = [System.Reflection.Assembly]::Load([System.IO.File]::ReadAllBytes($tempDll))
    $mgrType = $assembly.GetType("RazerBatteryTray.UpdateManager")
    if ($null -eq $mgrType) {
        Write-Error "FAIL: RazerBatteryTray.UpdateManager type not found!"
        exit 1
    }

    # ================= 1. 验证版本号比对算法 =================
    $cmpMethod = $mgrType.GetMethod("CompareVersions", [System.Reflection.BindingFlags]"Public,Static")
    if ($null -eq $cmpMethod) {
        Write-Error "FAIL: CompareVersions method not found on UpdateManager!"
        exit 1
    }

    $cases = @(
        @{ V1 = "4.3.0"; V2 = "4.2.0"; Expected = 1 },
        @{ V1 = "4.2.0"; V2 = "4.2.0"; Expected = 0 },
        @{ V1 = "4.2.0"; V2 = "4.3.0"; Expected = -1 },
        @{ V1 = "4.2";   V2 = "4.2.0"; Expected = 0 },
        @{ V1 = "4.2.1"; V2 = "4.2.0"; Expected = 1 },
        @{ V1 = "v4.3.0"; V2 = "4.2.0"; Expected = 1 },
        @{ V1 = "4.10.0"; V2 = "4.9.0"; Expected = 1 }
    )

    foreach ($c in $cases) {
        $res = $cmpMethod.Invoke($null, @($c.V1, $c.V2))
        if ($res -ne $c.Expected) {
            Write-Error "FAIL: CompareVersions('$($c.V1)', '$($c.V2)') expected $($c.Expected), got $res"
            exit 1
        }
    }
    Write-Host "PASS: CompareVersions passed all 7 version test cases." -ForegroundColor Green

    # ================= 2. 验证 IsNewerVersion =================
    $isNewerMethod = $mgrType.GetMethod("IsNewerVersion", [System.Reflection.BindingFlags]"Public,Static")
    if ($null -eq $isNewerMethod) {
        Write-Error "FAIL: IsNewerVersion method not found on UpdateManager!"
        exit 1
    }

    if ($isNewerMethod.Invoke($null, @("4.3.0", "4.2.0")) -ne $true) {
        Write-Error "FAIL: IsNewerVersion('4.3.0', '4.2.0') expected true!"
        exit 1
    }
    if ($isNewerMethod.Invoke($null, @("4.2.0", "4.2.0")) -ne $false) {
        Write-Error "FAIL: IsNewerVersion('4.2.0', '4.2.0') expected false!"
        exit 1
    }
    if ($isNewerMethod.Invoke($null, @("4.1.9", "4.2.0")) -ne $false) {
        Write-Error "FAIL: IsNewerVersion('4.1.9', '4.2.0') expected false!"
        exit 1
    }
    Write-Host "PASS: IsNewerVersion passed all boundary cases." -ForegroundColor Green

    # ================= 3. 验证 JSON 解析 =================
    $jsonMethod = $mgrType.GetMethod("ParseVersionJson", [System.Reflection.BindingFlags]"Public,Static")
    if ($null -eq $jsonMethod) {
        Write-Error "FAIL: ParseVersionJson method not found on UpdateManager!"
        exit 1
    }

    $sampleJson = @'
{
  "latestVersion": "4.3.0",
  "releaseDate": "2026-10-05",
  "minSupportedVersion": "4.0.0",
  "downloadUrl": "https://github.com/Ferris-echo/FerrisPulse/releases/download/v4.3.0/FerrisPulse.exe",
  "sha256": "4a7d6e8b9c0f1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b1c2d3e4f5a6b",
  "fileSize": 12845056,
  "changelog": [
    "【新增】全自动在线检查更新与原地热替换功能",
    "【新增】支持开机自启静默更新与周期性检测",
    "【优化】界面动效与暗黑视觉语言统一升级"
  ]
}
'@

    $parsedInfo = $jsonMethod.Invoke($null, @($sampleJson))
    if ($null -eq $parsedInfo) {
        Write-Error "FAIL: ParseVersionJson returned null!"
        exit 1
    }

    if ($parsedInfo.LatestVersion -ne "4.3.0") {
        Write-Error "FAIL: LatestVersion expected '4.3.0', got '$($parsedInfo.LatestVersion)'"
        exit 1
    }
    if ($parsedInfo.ReleaseDate -ne "2026-10-05") {
        Write-Error "FAIL: ReleaseDate expected '2026-10-05', got '$($parsedInfo.ReleaseDate)'"
        exit 1
    }
    if ($parsedInfo.DownloadUrl -ne "https://github.com/Ferris-echo/FerrisPulse/releases/download/v4.3.0/FerrisPulse.exe") {
        Write-Error "FAIL: DownloadUrl mismatch!"
        exit 1
    }
    if ($parsedInfo.Sha256 -ne "4a7d6e8b9c0f1a2b3c4d5e6f7a8b9c0d1e2f3a4b5c6d7e8f9a0b1c2d3e4f5a6b") {
        Write-Error "FAIL: Sha256 mismatch!"
        exit 1
    }
    if ($parsedInfo.FileSize -ne 12845056) {
        Write-Error "FAIL: FileSize expected 12845056, got $($parsedInfo.FileSize)"
        exit 1
    }
    if ($parsedInfo.Changelog.Count -ne 3) {
        Write-Error "FAIL: Changelog count expected 3, got $($parsedInfo.Changelog.Count)"
        exit 1
    }
    Write-Host "PASS: ParseVersionJson parsed all fields and arrays correctly." -ForegroundColor Green

    # ================= 4. 验证 SHA256 文件计算 =================
    $shaMethod = $mgrType.GetMethod("ComputeFileSha256", [System.Reflection.BindingFlags]"Public,Static")
    if ($null -eq $shaMethod) {
        Write-Error "FAIL: ComputeFileSha256 method not found on UpdateManager!"
        exit 1
    }
    $sampleFile = [System.IO.Path]::GetTempFileName()
    [System.IO.File]::WriteAllText($sampleFile, "FerrisPulse Test Content 12345", [System.Text.Encoding]::UTF8)
    $actualHash = $shaMethod.Invoke($null, @($sampleFile))
    $expectedHash = (Get-FileHash -Path $sampleFile -Algorithm SHA256).Hash.ToLowerInvariant()
    Remove-Item $sampleFile -Force -ErrorAction SilentlyContinue

    if ($actualHash.ToLowerInvariant() -ne $expectedHash) {
        Write-Error "FAIL: ComputeFileSha256 expected '$expectedHash', got '$actualHash'"
        exit 1
    }
    Write-Host "PASS: ComputeFileSha256 hash calculation verified." -ForegroundColor Green

    # ================= 5. 验证容灾节点契约 =================
    $epMethod = $mgrType.GetMethod("GetUpdateEndpoints", [System.Reflection.BindingFlags]"Public,Static")
    if ($null -eq $epMethod) {
        Write-Error "FAIL: GetUpdateEndpoints method not found on UpdateManager!"
        exit 1
    }
    $endpoints = $epMethod.Invoke($null, @())
    if ($endpoints.Length -lt 4) {
        Write-Error "FAIL: Expected at least 4 endpoints for failover redundancy!"
        exit 1
    }
    $hasGitee = ($endpoints -match "gitee\.com").Count -gt 0
    $hasGcore = ($endpoints -match "gcore\.jsdelivr\.net").Count -gt 0
    $hasGhProxy = ($endpoints -match "ghproxy\.net").Count -gt 0
    if (-not $hasGitee -or -not $hasGcore -or -not $hasGhProxy) {
        Write-Error "FAIL: Missing critical domestic failover nodes (Gitee: $hasGitee, GCore: $hasGcore, GhProxy: $hasGhProxy)!"
        exit 1
    }
    Write-Host "PASS: GetUpdateEndpoints provides $($endpoints.Length) failover endpoints including Gitee, GCore and GhProxy." -ForegroundColor Green

    # ================= 6. 验证 PendingUpdate 状态机与事件广播 =================
    $pendingProp = $mgrType.GetProperty("PendingUpdate", [System.Reflection.BindingFlags]"Public,Static")
    if ($null -eq $pendingProp) {
        Write-Error "FAIL: PendingUpdate property not found on UpdateManager!"
        exit 1
    }
    $clearMethod = $mgrType.GetMethod("ClearPendingUpdate", [System.Reflection.BindingFlags]"Public,Static")
    if ($null -eq $clearMethod) {
        Write-Error "FAIL: ClearPendingUpdate method not found on UpdateManager!"
        exit 1
    }

    $clearMethod.Invoke($null, @())
    $curPending = $pendingProp.GetValue($null, $null)
    if ($null -ne $curPending) {
        Write-Error "FAIL: PendingUpdate should be null after ClearPendingUpdate!"
        exit 1
    }

    # 测试赋值与事件触发
    $eventTriggered = $false
    $action = [System.Action]{
        $script:eventTriggered = $true
    }
    $eventInfo = $mgrType.GetEvent("OnPendingUpdateChanged", [System.Reflection.BindingFlags]"Public,Static")
    $eventInfo.AddEventHandler($null, $action)

    $pendingProp.SetValue($null, $parsedInfo, $null)
    if (-not $eventTriggered) {
        Write-Error "FAIL: OnPendingUpdateChanged was not triggered on PendingUpdate set!"
        exit 1
    }
    $newPending = $pendingProp.GetValue($null, $null)
    if ($null -eq $newPending -or $newPending.LatestVersion -ne "4.3.0") {
        Write-Error "FAIL: PendingUpdate value mismatch after set!"
        exit 1
    }

    $clearMethod.Invoke($null, @())
    $eventInfo.RemoveEventHandler($null, $action)
    Write-Host "PASS: PendingUpdate state machine and event broadcast verified." -ForegroundColor Green

    Write-Host "ALL TESTS PASSED for test_update_service.ps1" -ForegroundColor Cyan
}
finally {
    try { Remove-Item $tempDll -Force -ErrorAction SilentlyContinue } catch {}
}
