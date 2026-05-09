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

if (Test-Path -LiteralPath $publishDir) {
    Remove-Item -LiteralPath $publishDir -Recurse -Force
}

dotnet publish $projectPath `
    -c $Configuration `
    -r $Runtime `
    --self-contained:$(-not $FrameworkDependent) `
    -o $publishDir `
    -p:PublishSingleFile=true `
    -p:IncludeNativeLibrariesForSelfExtract=true `
    -p:EnableCompressionInSingleFile=true

$exePath = Join-Path $publishDir "Tfx.exe"
if (-not (Test-Path -LiteralPath $exePath)) {
    throw "Release executable was not created: $exePath"
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
