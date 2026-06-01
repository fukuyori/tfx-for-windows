param(
    [string]$Runtime = "win-x64",
    [string]$Configuration = "Release",
    [switch]$FrameworkDependent
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

$releaseName = "tfx-for-windows-$version-$Runtime"
$artifactsRoot = Join-Path $repoRoot "artifacts"
$releaseRoot = Join-Path $artifactsRoot "release"
$publishDir = Join-Path $releaseRoot $releaseName

New-Item -ItemType Directory -Force -Path $releaseRoot | Out-Null

# Start from a clean publish folder so stale files from a previous build (e.g.
# the loose Assets/terminal/* that earlier versions shipped before the xterm
# assets were embedded in the exe) never linger in the single-file release.
if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}
New-Item -ItemType Directory -Force -Path $publishDir | Out-Null

dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained:$(-not $FrameworkDependent) `
    -o $publishDir `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true `
    -p:DebugType=none `
    -p:DebugSymbols=false

if ($LASTEXITCODE -ne 0) {
    throw "dotnet publish failed with exit code $LASTEXITCODE"
}

$exePath = Join-Path $publishDir "Tfx.exe"
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Release executable was not created: $exePath"
}

# Single-file sanity check: the only payload should be Tfx.exe. Warn loudly if
# any extra files slipped into the publish folder (a sign that something is no
# longer embedded / self-contained as intended).
$strays = Get-ChildItem -LiteralPath $publishDir -Recurse -File |
    Where-Object { $_.Name -ne "Tfx.exe" }
if ($strays) {
    Write-Warning "Unexpected files in the single-file publish folder:"
    $strays | ForEach-Object { Write-Warning "  $($_.FullName.Substring($publishDir.Length + 1))" }
}

$versionInfo = (Get-Item -LiteralPath $exePath).VersionInfo
if ($versionInfo.ProductVersion -ne $version) {
    throw "ProductVersion mismatch. Expected $version, got $($versionInfo.ProductVersion)"
}

Write-Host "Release build complete:"
Write-Host "  Version: $version"
Write-Host "  Runtime: $Runtime"
Write-Host "  Publish: $publishDir"
Write-Host "  Exe: $exePath"
