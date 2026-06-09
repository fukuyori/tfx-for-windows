<#
.SYNOPSIS
    Packages the already-built single-file release into a portable ZIP.

.DESCRIPTION
    Consumes the output of scripts/build-release.ps1 (does NOT rebuild). Reads the
    version from Tfx.csproj, finds the published Tfx.exe under
    artifacts/release/tfx-for-windows-<version>-<runtime>/, stages it together with
    LICENSE / NOTICE / README files, and produces:

        artifacts/release/tfx-for-windows-<version>-<runtime>-portable.zip

    The archive contains a single top-level folder so it extracts cleanly.

.EXAMPLE
    pwsh scripts/build-zip.ps1
#>
param(
    [string]$Runtime = "win-x64"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "Tfx.csproj"

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "Project file not found: $projectPath"
}

[xml]$projectXml = Get-Content -LiteralPath $projectPath
$version = $projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Version is not set in Tfx.csproj"
}

$releaseRoot = Join-Path (Join-Path $repoRoot "artifacts") "release"
$publishName = "tfx-for-windows-$version-$Runtime"
$publishDir = Join-Path $releaseRoot $publishName
$exePath = Join-Path $publishDir "Tfx.exe"

if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Built executable not found: $exePath`nRun scripts/build-release.ps1 first."
}

$portableName = "$publishName-portable"
$zipPath = Join-Path $releaseRoot "$portableName.zip"

# Stage into a folder named after the package so the ZIP has a single clean
# top-level directory (no loose files dumped into the extraction target).
$stageRoot = Join-Path $releaseRoot "_ziptmp"
$stageDir = Join-Path $stageRoot $portableName
if (Test-Path -LiteralPath $stageRoot) {
    Remove-Item -LiteralPath $stageRoot -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $stageDir | Out-Null

Copy-Item -LiteralPath $exePath -Destination $stageDir

# Include the legal / readme files when present (portable users still want them).
foreach ($extra in @("LICENSE", "NOTICE", "README.md", "README.ja.md")) {
    $src = Join-Path $repoRoot $extra
    if (Test-Path -LiteralPath $src) {
        Copy-Item -LiteralPath $src -Destination $stageDir
    }
}

if (Test-Path -LiteralPath $zipPath) {
    Remove-Item -LiteralPath $zipPath -Force
}

Compress-Archive -Path $stageDir -DestinationPath $zipPath -CompressionLevel Optimal
Remove-Item -LiteralPath $stageRoot -Recurse -Force

$zipInfo = Get-Item -LiteralPath $zipPath
$sizeMb = [math]::Round($zipInfo.Length / 1MB, 2)

Write-Host "Portable ZIP created:"
Write-Host "  Version: $version"
Write-Host "  Runtime: $Runtime"
Write-Host "  Output:  $zipPath"
Write-Host "  Size:    $sizeMb MB"
