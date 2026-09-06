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

    With -Sign, Tfx.exe is Authenticode-signed in place before it is copied into
    the archive (the same file build-installer.ps1 embeds, so running either
    script with -Sign leaves one signed binary for both packages). A file that
    already carries a valid signature from the same certificate is left as is,
    so running build-installer.ps1 -Sign first does not trigger a second signing
    (and, on a hardware token, a second PIN prompt). The signature is verified
    with signtool before the ZIP is written.
    The signing certificate is picked from the certificate store by subject name,
    taken from the CODESIGN_CERT environment variable (override with
    -CertSubject). Signing needs signtool.exe from the Windows SDK; pass
    -SignToolPath if it is neither on PATH nor under the default Windows Kits
    location.

.EXAMPLE
    pwsh scripts/build-zip.ps1

.EXAMPLE
    pwsh scripts/build-zip.ps1 -Sign
#>
param(
    [string]$Runtime = "win-x64",
    [switch]$Sign,
    [string]$CertSubject = $env:CODESIGN_CERT,
    [string]$SignToolPath,
    [string]$TimestampUrl = "http://timestamp.digicert.com"
)

$ErrorActionPreference = "Stop"

$repoRoot = Split-Path -Parent $PSScriptRoot
$projectPath = Join-Path $repoRoot "Tfx.csproj"

if ($Sign -and [string]::IsNullOrWhiteSpace($CertSubject)) {
    throw "-Sign was specified but no certificate subject is available. Set the CODESIGN_CERT environment variable or pass -CertSubject."
}

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

# Locate signtool.exe from the Windows SDK (newest SDK, native architecture first).
# Same lookup as build-installer.ps1.
function Resolve-SignTool {
    param([string]$Explicit)

    if ($Explicit) {
        if (Test-Path -LiteralPath $Explicit) { return (Resolve-Path -LiteralPath $Explicit).Path }
        throw "signtool.exe not found at the supplied -SignToolPath: $Explicit"
    }

    $onPath = Get-Command "signtool.exe" -ErrorAction SilentlyContinue
    if ($onPath) { return $onPath.Source }

    $kitRoots = @(
        (Join-Path ${env:ProgramFiles(x86)} "Windows Kits\10\bin"),
        (Join-Path $env:ProgramFiles "Windows Kits\10\bin")
    ) | Where-Object { $_ -and (Test-Path -LiteralPath $_) }

    $archOrder = if ($env:PROCESSOR_ARCHITECTURE -eq "ARM64") {
        @("arm64", "x64", "x86")
    } else {
        @("x64", "x86", "arm64")
    }

    foreach ($kitRoot in $kitRoots) {
        $versionDirs = Get-ChildItem -LiteralPath $kitRoot -Directory -ErrorAction SilentlyContinue |
            Where-Object { $_.Name -match '^\d+(\.\d+)+$' } |
            Sort-Object { [version]$_.Name } -Descending
        foreach ($versionDir in $versionDirs) {
            foreach ($arch in $archOrder) {
                $candidate = Join-Path $versionDir.FullName "$arch\signtool.exe"
                if (Test-Path -LiteralPath $candidate) { return $candidate }
            }
        }
    }

    throw @"
signtool.exe was not found.
It ships with the Windows SDK:  winget install Microsoft.WindowsSDK
Then re-run, or pass the path explicitly:  -SignToolPath "C:\Path\to\signtool.exe"
"@
}

function Invoke-CodeSign {
    param([Parameter(Mandatory = $true)][string]$Path)

    & $signTool sign /n $CertSubject /fd sha256 /tr $TimestampUrl /td sha256 /v $Path
    if ($LASTEXITCODE -ne 0) {
        throw "signtool sign failed with exit code $LASTEXITCODE for: $Path"
    }
}

function Test-CodeSign {
    param([Parameter(Mandatory = $true)][string]$Path)

    & $signTool verify /pa $Path
    if ($LASTEXITCODE -ne 0) {
        throw "signtool verify failed with exit code $LASTEXITCODE for: $Path"
    }
}

# True when the file already carries a valid Authenticode signature whose
# signer subject contains the requested subject name (signtool /n semantics).
function Test-AlreadySigned {
    param([Parameter(Mandatory = $true)][string]$Path)

    $sig = Get-AuthenticodeSignature -LiteralPath $Path
    if ($sig.Status -ne "Valid" -or -not $sig.SignerCertificate) { return $false }
    return $sig.SignerCertificate.Subject -match [regex]::Escape($CertSubject)
}

$signTool = $null
if ($Sign) {
    $signTool = Resolve-SignTool -Explicit $SignToolPath
    Write-Host "Using signtool:      $signTool"
    Write-Host "Certificate subject: $CertSubject"
    Write-Host "Timestamp server:    $TimestampUrl"

    if (Test-AlreadySigned -Path $exePath) {
        Write-Host "Already signed (skipping): $exePath"
    } else {
        # Sign the published executable in place so the same signed binary is
        # picked up by build-installer.ps1 as well.
        Write-Host "Signing $exePath"
        Invoke-CodeSign -Path $exePath
    }

    Write-Host "Verifying signature..."
    Test-CodeSign -Path $exePath
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
$signedNote = if ($Sign) { "yes (Tfx.exe)" } else { "no" }

Write-Host "Portable ZIP created:"
Write-Host "  Version: $version"
Write-Host "  Runtime: $Runtime"
Write-Host "  Output:  $zipPath"
Write-Host "  Size:    $sizeMb MB"
Write-Host "  Signed:  $signedNote"
