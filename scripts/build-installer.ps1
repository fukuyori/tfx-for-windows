<#
.SYNOPSIS
    Builds a Windows installer from the already-built single-file release.

.DESCRIPTION
    Consumes the output of scripts/build-release.ps1 (does NOT rebuild). Reads the
    version from Tfx.csproj, finds the published Tfx.exe under
    artifacts/release/tfx-for-windows-<version>-<runtime>/, and compiles
    scripts/tfx.iss with Inno Setup to produce:

        artifacts/release/tfx-for-windows-<version>-setup.exe

    Requires Inno Setup 6 (ISCC.exe). Install it with:
        winget install JRSoftware.InnoSetup
    or download from https://jrsoftware.org/isdl.php
    If ISCC.exe is not on PATH or in the default location, pass -IsccPath.

.EXAMPLE
    pwsh scripts/build-installer.ps1

.EXAMPLE
    pwsh scripts/build-installer.ps1 -IsccPath "D:\Tools\Inno Setup 6\ISCC.exe"
#>
param(
    [string]$Runtime = "win-x64",
    [string]$IsccPath
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "Tfx.csproj"
$issPath = Join-Path $PSScriptRoot "tfx.iss"

if (-not (Test-Path -LiteralPath $projectPath)) {
    throw "Project file not found: $projectPath"
}
if (-not (Test-Path -LiteralPath $issPath)) {
    throw "Inno Setup script not found: $issPath"
}

[xml]$projectXml = Get-Content -LiteralPath $projectPath
$version = $projectXml.Project.PropertyGroup.Version
if ([string]::IsNullOrWhiteSpace($version)) {
    throw "Version is not set in Tfx.csproj"
}

$releaseRoot = Join-Path (Join-Path $repoRoot "artifacts") "release"
$publishDir = Join-Path $releaseRoot "tfx-for-windows-$version-$Runtime"
$exePath = Join-Path $publishDir "Tfx.exe"
$iconPath = Join-Path $repoRoot "Assets\AppIcon.ico"

if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Built executable not found: $exePath`nRun scripts/build-release.ps1 first."
}

# Locate the Inno Setup compiler.
function Resolve-Iscc {
    param([string]$Explicit)

    if ($Explicit) {
        if (Test-Path -LiteralPath $Explicit) { return (Resolve-Path -LiteralPath $Explicit).Path }
        throw "ISCC.exe not found at the supplied -IsccPath: $Explicit"
    }

    $onPath = Get-Command "ISCC.exe" -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    $candidates = @(
        (Join-Path ${env:ProgramFiles(x86)} "Inno Setup 6\ISCC.exe"),
        (Join-Path $env:ProgramFiles "Inno Setup 6\ISCC.exe")
    )
    foreach ($candidate in $candidates) {
        if ($candidate -and (Test-Path -LiteralPath $candidate)) { return $candidate }
    }

    throw @"
Inno Setup compiler (ISCC.exe) was not found.
Install it with:  winget install JRSoftware.InnoSetup
or download from: https://jrsoftware.org/isdl.php
Then re-run, or pass the path explicitly:  -IsccPath "C:\Path\to\ISCC.exe"
"@
}

$iscc = Resolve-Iscc -Explicit $IsccPath
Write-Host "Using Inno Setup compiler: $iscc"

& $iscc `
    "/DMyVersion=$version" `
    "/DMySourceExe=$exePath" `
    "/DMyOutputDir=$releaseRoot" `
    "/DMyIcon=$iconPath" `
    $issPath

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
}

$setupPath = Join-Path $releaseRoot "tfx-for-windows-$version-setup.exe"
if (-not (Test-Path -LiteralPath $setupPath)) {
    throw "Installer was not produced: $setupPath"
}

$setupInfo = Get-Item -LiteralPath $setupPath
$sizeMb = [math]::Round($setupInfo.Length / 1MB, 2)

Write-Host "Installer created:"
Write-Host "  Version: $version"
Write-Host "  Runtime: $Runtime"
Write-Host "  Output:  $setupPath"
Write-Host "  Size:    $sizeMb MB"
