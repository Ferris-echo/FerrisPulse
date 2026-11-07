$ErrorActionPreference = "Stop"
$scriptDir = Split-Path -Parent $MyInvocation.MyCommand.Path
$repoRoot = (Get-Item "$scriptDir\..").FullName
Set-Location $repoRoot

# 从 CHANGELOG.md 提取 2.3.0 正文
$changelogContent = [System.IO.File]::ReadAllText("$repoRoot\CHANGELOG.md", [System.Text.Encoding]::UTF8)
$startTag = "## 🚀 [v2.3.0]"
$endTag = "## 🚀 [v2.2.0]"

$startIdx = $changelogContent.IndexOf($startTag)
$endIdx = $changelogContent.IndexOf($endTag)

$releaseBody = ""
if ($startIdx -ge 0 -and $endIdx -gt $startIdx) {
    $rawSection = $changelogContent.Substring($startIdx, $endIdx - $startIdx).Trim()
    $firstLineBreak = $rawSection.IndexOf("`n")
    if ($firstLineBreak -ge 0) {
        $releaseBody = $rawSection.Substring($firstLineBreak).Trim().Trim("-`r`n ")
    }
}

Write-Host "提取到 Release 说明，长度: $($releaseBody.Length) 字符" -ForegroundColor Cyan

# 执行双平台发布
& "$scriptDir\publish_release.ps1" -Version "2.3.0" -Title "FerrisPulse · 灵脉 v2.3.0 正式版" -Body $releaseBody
