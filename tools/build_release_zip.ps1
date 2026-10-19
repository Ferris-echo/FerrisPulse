# build_release_zip.ps1
# 自动化发布打包脚本（产物已被 .gitignore 忽略，仅供 GitHub/Gitee Releases 附件上传）
param([string]$Version = "2.0.3")
$ErrorActionPreference = "Stop"
if ([string]::IsNullOrEmpty($Version)) { $Version = "2.0.3" }
$rootDir = (Resolve-Path "$PSScriptRoot\..").Path
$cleanVer = $Version.TrimStart('v', 'V')
$zipPath = Join-Path $rootDir "FerrisPulse_v${cleanVer}_Portable.zip"

if (Test-Path $zipPath) {
    Remove-Item $zipPath -Force
}

$tempStage = Join-Path ([System.IO.Path]::GetTempPath()) ("FP_Pack_" + [System.Guid]::NewGuid().ToString("N"))
if (Test-Path $tempStage) { Remove-Item $tempStage -Recurse -Force }
New-Item -ItemType Directory -Path $tempStage | Out-Null

try {
    Copy-Item (Join-Path $rootDir "FerrisPulse.exe") (Join-Path $tempStage "FerrisPulse.exe")
    Copy-Item (Join-Path $rootDir "README.md") (Join-Path $tempStage "README.md")
    Copy-Item (Join-Path $rootDir "LICENSE") (Join-Path $tempStage "LICENSE")
    Copy-Item (Join-Path $rootDir "CHANGELOG.md") (Join-Path $tempStage "CHANGELOG.md")

    $legacyLog = Join-Path $rootDir "docs\CHANGELOG_LEGACY.txt"
    if (Test-Path $legacyLog) {
        Copy-Item $legacyLog (Join-Path $tempStage "更新日志.txt")
    }

    $faq = Join-Path $rootDir "docs\FAQ.txt"
    if (Test-Path $faq) {
        Copy-Item $faq (Join-Path $tempStage "使用说明与常见问题.txt")
    }

    Write-Host "Compressing Release artifacts to $zipPath..."
    Add-Type -AssemblyName System.IO.Compression.FileSystem
    [System.IO.Compression.ZipFile]::CreateFromDirectory($tempStage, $zipPath, [System.IO.Compression.CompressionLevel]::Optimal, $false)

    if (Test-Path $zipPath) {
        $item = Get-Item $zipPath
        Write-Host "[SUCCESS] Archive created: $($item.FullName) ($($item.Length) bytes)" -ForegroundColor Green
        Write-Host "[NOTE] Binary zip is git-ignored and intended for GitHub/Gitee Releases."
    } else {
        Write-Error "[ERROR] Failed to create zip file."
        exit 1
    }
}
finally {
    if (Test-Path $tempStage) { Remove-Item $tempStage -Recurse -Force -ErrorAction SilentlyContinue }
}
