# ============================================================================
# 脚本名称: publish_release.ps1
# 所属区域: tools (工程自动化流水线层)
# 核心职责:
#   自动化全托管双平台发布流水线 (GitHub & Gitee 双端同步)
#   包含：版本自增 -> C# 5.0 原生编译 -> 哈希/大小校验 -> version.json 远端更新
#         -> 便携包压缩 -> Git Tag 推送 -> GitHub Release 创建与资产上传 -> Gitee Release 创建与资产上传
#
# 依赖关系:
#   - tools/.release_env (凭证隔离文件，已被 .gitignore 严格忽略)
#   - build.bat
#   - tools/build_release_zip.ps1
#   - curl.exe (Windows 内置命令行网络传输工具)
# ============================================================================

param(
    [Parameter(Mandatory=$true)]
    [string]$Version,
    [string]$Title = "",
    [string[]]$Changelog = @()
)

$ErrorActionPreference = "Stop"
[Net.ServicePointManager]::SecurityProtocol = [Net.SecurityProtocolType]::Tls12

$rootDir = (Resolve-Path "$PSScriptRoot\..").Path
Set-Location $rootDir

$cleanVer = $Version.Trim().TrimStart('v', 'V')
$tag = "v$cleanVer"

if ([string]::IsNullOrEmpty($Title)) {
    $Title = "FerrisPulse · 灵脉 $tag 正式版"
}

Write-Host "==========================================================" -ForegroundColor Cyan
Write-Host " [FerrisPulse 发布流水线] 开始全自动双平台发布: $tag" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Cyan

# 1. 加载安全隔离的 API Token
$envFile = Join-Path $PSScriptRoot ".release_env"
if (Test-Path $envFile) {
    Get-Content $envFile -Encoding UTF8 | ForEach-Object {
        $line = $_.Trim()
        if ($line -and -not $line.StartsWith("#") -and $line.Contains("=")) {
            $parts = $line.Split("=", 2)
            [Environment]::SetEnvironmentVariable($parts[0].Trim(), $parts[1].Trim(), "Process")
        }
    }
}

$ghToken = [Environment]::GetEnvironmentVariable("GITHUB_TOKEN", "Process")
$giteeToken = [Environment]::GetEnvironmentVariable("GITEE_TOKEN", "Process")
$ghRepo = [Environment]::GetEnvironmentVariable("GITHUB_REPO", "Process")
$giteeRepo = [Environment]::GetEnvironmentVariable("GITEE_REPO", "Process")

if (-not $ghRepo) { $ghRepo = "Ferris-echo/FerrisPulse" }
if (-not $giteeRepo) { $giteeRepo = "Ferris-echo/ferris-pulse" }

if (-not $ghToken -or -not $giteeToken) {
    Write-Error "缺少 GITHUB_TOKEN 或 GITEE_TOKEN 环境变量，无法调用双平台 API！"
    exit 1
}

# 2. 更新 AssemblyInfo.cs 版本号
Write-Host "`n[1/7] 更新 AssemblyInfo.cs 程序集版本到 $cleanVer..." -ForegroundColor Yellow
$assemblyInfoPath = Join-Path $rootDir "src\Properties\AssemblyInfo.cs"
$assemblyText = [System.IO.File]::ReadAllText($assemblyInfoPath, [System.Text.Encoding]::UTF8)
$assemblyText = [System.Text.RegularExpressions.Regex]::Replace($assemblyText, '\[assembly: AssemblyVersion\(".*?"\)\]', "[assembly: AssemblyVersion(`"$cleanVer.0`")]")
$assemblyText = [System.Text.RegularExpressions.Regex]::Replace($assemblyText, '\[assembly: AssemblyFileVersion\(".*?"\)\]', "[assembly: AssemblyFileVersion(`"$cleanVer.0`")]")
$assemblyText = [System.Text.RegularExpressions.Regex]::Replace($assemblyText, '\[assembly: AssemblyInformationalVersion\(".*?"\)\]', "[assembly: AssemblyInformationalVersion(`"$cleanVer-Release`")]")
$utf8WithBom = New-Object System.Text.UTF8Encoding($true)
[System.IO.File]::WriteAllText($assemblyInfoPath, $assemblyText, $utf8WithBom)

# 3. 执行纯净构建
Write-Host "`n[2/7] 执行原生 build.bat 编译 (C# 5.0 / .NET 4.0)..." -ForegroundColor Yellow
& cmd.exe /c "build.bat"
if ($LASTEXITCODE -ne 0 -or -not (Test-Path (Join-Path $rootDir "FerrisPulse.exe"))) {
    Write-Error "编译失败！请检查编译输出。"
    exit 1
}

# 4. 计算文件 SHA256 与文件大小
$exePath = Join-Path $rootDir "FerrisPulse.exe"
$sha256 = (Get-FileHash -Path $exePath -Algorithm SHA256).Hash.ToLower()
$fileSize = (Get-Item $exePath).Length
Write-Host "`n[3/7] 产物校验和已生成: SHA256=$sha256, Size=$fileSize bytes" -ForegroundColor Green

# 5. 更新 version.json 动态元数据
Write-Host "`n[4/7] 更新 version.json 云端自更新配置..." -ForegroundColor Yellow
$versionJsonPath = Join-Path $rootDir "version.json"
$today = (Get-Date).ToString("yyyy-MM-dd")
$downloadUrl = "https://gitee.com/$giteeRepo/releases/download/$tag/FerrisPulse.exe"

$vObj = Get-Content $versionJsonPath -Raw -Encoding UTF8 | ConvertFrom-Json
$vObj.latestVersion = $cleanVer
$vObj.releaseDate = $today
$vObj.downloadUrl = $downloadUrl
$vObj.sha256 = $sha256
$vObj.fileSize = [long]$fileSize
if ($Changelog.Count -gt 0) {
    $parsedLogs = @()
    foreach ($c in $Changelog) {
        if ($c -match "【" -and ($c.IndexOf("【") -ne $c.LastIndexOf("【"))) {
            $splits = [System.Text.RegularExpressions.Regex]::Split($c, "(?=【)")
            foreach ($s in $splits) {
                $cs = $s.Trim().TrimEnd(',', '，', ';', '；')
                if ($cs) { $parsedLogs += $cs }
            }
        } else {
            $parsedLogs += $c
        }
    }
    $vObj.changelog = $parsedLogs
}

$updatedJson = $vObj | ConvertTo-Json -Depth 5
$updatedJson = $updatedJson.Replace("\u0026", "&")
[System.IO.File]::WriteAllText($versionJsonPath, $updatedJson, $utf8WithBom)

# 6. 生成便携版压缩包
Write-Host "`n[5/7] 打包便携版 ZIP (FerrisPulse_${tag}_Portable.zip)..." -ForegroundColor Yellow
& "$PSScriptRoot\build_release_zip.ps1" -Version $cleanVer
$zipPath = Join-Path $rootDir "FerrisPulse_${tag}_Portable.zip"
if (-not (Test-Path $zipPath)) {
    Write-Error "ZIP 打包失败！"
    exit 1
}

# 7. Git 提交、打 Tag 并双推
Write-Host "`n[6/7] 提交 Git 代码与 Tag，同步推送到 GitHub & Gitee..." -ForegroundColor Yellow
git add src/Properties/AssemblyInfo.cs version.json tools/build_release_zip.ps1 tools/publish_release.ps1 .gitignore CHANGELOG.md
$gitDiff = git status --porcelain
if ($gitDiff) {
    git commit -m "chore(release): bump version to $tag"
}
$existingTag = git tag -l $tag
if (-not $existingTag) {
    git tag -a $tag -m "Release $tag"
}
git push origin main --tags
git push gitee main --tags

# 8. 双平台发布 Release
Write-Host "`n[7/7] 调用双平台 API 创建 Release 并上传二进制资产..." -ForegroundColor Yellow

$releaseBody = ""
if ($Changelog.Count -gt 0) {
    $releaseBody = "### 🌟 更新日志`n`n" + (($Changelog | ForEach-Object { "* " + $_ }) -join "`n")
} else {
    $releaseBody = "### 🌟 FerrisPulse $tag 正式版`n`n* 本次更新包含最新稳定性与功能优化。"
}

# 8.1 GitHub Release 创建与资产上传
Write-Host "  -> [GitHub] 检查/创建 Release $tag..." -ForegroundColor Cyan
$ghHeaders = @{
    "Authorization" = "Bearer $ghToken"
    "Accept" = "application/vnd.github.v3+json"
    "User-Agent" = "FerrisPulse-Release"
}

$ghReleaseId = $null
try {
    $existing = Invoke-RestMethod -Uri "https://api.github.com/repos/$ghRepo/releases/tags/$tag" -Headers $ghHeaders
    $ghReleaseId = $existing.id
    Write-Host "  -> [GitHub] Release 已存在 (ID: $ghReleaseId)" -ForegroundColor DarkGray
} catch {
    $ghCreateBody = @{
        tag_name = $tag
        name = $Title
        body = $releaseBody
        draft = $false
        prerelease = $false
    } | ConvertTo-Json

    $newRel = Invoke-RestMethod -Uri "https://api.github.com/repos/$ghRepo/releases" -Method Post -Headers $ghHeaders -Body $ghCreateBody -ContentType "application/json; charset=utf-8"
    $ghReleaseId = $newRel.id
    Write-Host "  -> [GitHub] Release 创建成功 (ID: $ghReleaseId)" -ForegroundColor Green
}

if ($ghReleaseId) {
    # 检查并删除已存在的同名资产（防止 422 拒绝覆盖导致校验失败）
    try {
        $existingAssets = Invoke-RestMethod -Uri "https://api.github.com/repos/$ghRepo/releases/$ghReleaseId/assets" -Headers $ghHeaders
        foreach ($ea in $existingAssets) {
            if ($ea.name -eq "FerrisPulse.exe" -or $ea.name -eq "FerrisPulse_${tag}_Portable.zip") {
                Write-Host "  -> [GitHub] 删除旧版本资产: $($ea.name) (ID: $($ea.id))..." -ForegroundColor DarkGray
                Invoke-RestMethod -Uri "https://api.github.com/repos/$ghRepo/releases/assets/$($ea.id)" -Method Delete -Headers $ghHeaders | Out-Null
            }
        }
    } catch { }

    Write-Host "  -> [GitHub] 上传 FerrisPulse.exe..." -ForegroundColor Cyan
    & curl.exe -s -X POST "https://uploads.github.com/repos/$ghRepo/releases/$ghReleaseId/assets?name=FerrisPulse.exe" -H "Authorization: Bearer $ghToken" -H "Content-Type: application/octet-stream" --data-binary "@$exePath" | Out-Null
    
    Write-Host "  -> [GitHub] 上传 FerrisPulse_${tag}_Portable.zip..." -ForegroundColor Cyan
    & curl.exe -s -X POST "https://uploads.github.com/repos/$ghRepo/releases/$ghReleaseId/assets?name=FerrisPulse_${tag}_Portable.zip" -H "Authorization: Bearer $ghToken" -H "Content-Type: application/octet-stream" --data-binary "@$zipPath" | Out-Null
    Write-Host "  -> [GitHub] 资产上传完成！" -ForegroundColor Green
}

# 8.2 Gitee Release 创建与资产上传
Write-Host "  -> [Gitee] 检查/创建 Release $tag..." -ForegroundColor Cyan
$giteeReleaseId = $null
try {
    $giteeAll = Invoke-RestMethod -Uri "https://gitee.com/api/v5/repos/$giteeRepo/releases?access_token=$giteeToken" -UserAgent "FerrisPulse-Release"
    foreach ($r in $giteeAll) {
        if ($r.tag_name -eq $tag) {
            $giteeReleaseId = $r.id
            break
        }
    }
} catch { }

if (-not $giteeReleaseId) {
    $giteeCreateUri = "https://gitee.com/api/v5/repos/$giteeRepo/releases"
    $giteeParams = @{
        access_token = $giteeToken
        tag_name = $tag
        name = $Title
        body = $releaseBody
        target_commitish = "main"
    }
    $newGiteeRel = Invoke-RestMethod -Uri $giteeCreateUri -Method Post -Body $giteeParams -UserAgent "FerrisPulse-Release"
    $giteeReleaseId = $newGiteeRel.id
    Write-Host "  -> [Gitee] Release 创建成功 (ID: $giteeReleaseId)" -ForegroundColor Green
} else {
    Write-Host "  -> [Gitee] Release 已存在 (ID: $giteeReleaseId)" -ForegroundColor DarkGray
}

if ($giteeReleaseId) {
    # 检查并删除 Gitee 已存在的同名资产
    try {
        $giteeRelInfo = Invoke-RestMethod -Uri "https://gitee.com/api/v5/repos/$giteeRepo/releases/$giteeReleaseId?access_token=$giteeToken" -UserAgent "FerrisPulse-Release"
        foreach ($ga in $giteeRelInfo.assets) {
            if ($ga.name -eq "FerrisPulse.exe" -or $ga.name -eq "FerrisPulse_${tag}_Portable.zip") {
                Write-Host "  -> [Gitee] 删除旧版本资产: $($ga.name)..." -ForegroundColor DarkGray
                try {
                    Invoke-RestMethod -Uri "https://gitee.com/api/v5/repos/$giteeRepo/releases/$giteeReleaseId/attach_files/$($ga.id)?access_token=$giteeToken" -Method Delete -UserAgent "FerrisPulse-Release" | Out-Null
                } catch { }
            }
        }
    } catch { }

    Write-Host "  -> [Gitee] 上传 FerrisPulse.exe..." -ForegroundColor Cyan
    & curl.exe -s -X POST "https://gitee.com/api/v5/repos/$giteeRepo/releases/$giteeReleaseId/attach_files" -F "access_token=$giteeToken" -F "file=@$exePath" | Out-Null
    
    Write-Host "  -> [Gitee] 上传 FerrisPulse_${tag}_Portable.zip..." -ForegroundColor Cyan
    & curl.exe -s -X POST "https://gitee.com/api/v5/repos/$giteeRepo/releases/$giteeReleaseId/attach_files" -F "access_token=$giteeToken" -F "file=@$zipPath" | Out-Null
    Write-Host "  -> [Gitee] 资产上传完成！" -ForegroundColor Green
}

Write-Host "`n==========================================================" -ForegroundColor Green
Write-Host " [SUCCESS] $tag 双平台全自动发布大获全胜！" -ForegroundColor Green
Write-Host " GitHub: https://github.com/$ghRepo/releases/tag/$tag" -ForegroundColor Cyan
Write-Host " Gitee:  https://gitee.com/$giteeRepo/releases/tag/$tag" -ForegroundColor Cyan
Write-Host "==========================================================" -ForegroundColor Green
