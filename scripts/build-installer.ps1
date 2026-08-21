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

    With -Sign, Authenticode signatures are applied to all three binaries:
      * Tfx.exe       - signed in place before the installer is compiled, so the
                        installed copy is signed as well.
      * unins000.exe  - signed by Inno Setup itself (SignedUninstaller=yes).
      * setup.exe     - signed by Inno Setup after compilation (SignTool=).
    The signing certificate is picked from the certificate store by subject name,
    taken from the CODESIGN_CERT environment variable (override with
    -CertSubject). Signing needs signtool.exe from the Windows SDK; pass
    -SignToolPath if it is neither on PATH nor under the default Windows Kits
    location.

    Note: when the key lives on a hardware token, the PIN is prompted once per
    signtool.exe process - i.e. three times per build. Enable "single logon" in
    SafeNet Authentication Client Tools (gear icon -> Client Settings ->
    Advanced) to bring that down to one prompt per Windows session.

.EXAMPLE
    pwsh scripts/build-installer.ps1

.EXAMPLE
    pwsh scripts/build-installer.ps1 -Sign

.EXAMPLE
    pwsh scripts/build-installer.ps1 -IsccPath "D:\Tools\Inno Setup 6\ISCC.exe"
#>
param(
    [string]$Runtime = "win-x64",
    [string]$IsccPath,
    [switch]$Sign,
    [string]$CertSubject = $env:CODESIGN_CERT,
    [string]$SignToolPath,
    [string]$TimestampUrl = "http://timestamp.digicert.com"
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

# Locate signtool.exe from the Windows SDK (newest SDK, native architecture first).
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

$signTool = $null
if ($Sign) {
    if ([string]::IsNullOrWhiteSpace($CertSubject)) {
        throw "-Sign was specified but no certificate subject is available. Set the CODESIGN_CERT environment variable or pass -CertSubject."
    }

    $signTool = Resolve-SignTool -Explicit $SignToolPath
    Write-Host "Using signtool:      $signTool"
    Write-Host "Certificate subject: $CertSubject"
    Write-Host "Timestamp server:    $TimestampUrl"

    # Sign the application executable before compiling, so the copy embedded in
    # the installer (and therefore the installed one) carries the signature.
    Write-Host "Signing $exePath"
    Invoke-CodeSign -Path $exePath
}

$iscc = Resolve-Iscc -Explicit $IsccPath
Write-Host "Using Inno Setup compiler: $iscc"

$isccArgs = @(
    "/DMyVersion=$version",
    "/DMySourceExe=$exePath",
    "/DMyOutputDir=$releaseRoot",
    "/DMyIcon=$iconPath"
)

if ($Sign) {
    # Inno Setup runs this command for the uninstaller (SignedUninstaller=yes)
    # and for the finished setup.exe (SignTool=tfxsign, see tfx.iss).
    # $q is Inno Setup's placeholder for a double quote, $f for the target file.
    $signCommand = '$q' + $signTool + '$q sign /n $q' + $CertSubject + '$q /fd sha256 /tr $q' + $TimestampUrl + '$q /td sha256 $f'
    $isccArgs += "/Stfxsign=$signCommand"
    $isccArgs += "/DMySignTool=tfxsign"
}

& $iscc @isccArgs $issPath

if ($LASTEXITCODE -ne 0) {
    throw "Inno Setup compilation failed with exit code $LASTEXITCODE"
}

$setupPath = Join-Path $releaseRoot "tfx-for-windows-$version-setup.exe"
if (-not (Test-Path -LiteralPath $setupPath)) {
    throw "Installer was not produced: $setupPath"
}

if ($Sign) {
    Write-Host "Verifying signatures..."
    Test-CodeSign -Path $exePath
    Test-CodeSign -Path $setupPath
}

$setupInfo = Get-Item -LiteralPath $setupPath
$sizeMb = [math]::Round($setupInfo.Length / 1MB, 2)
$signedNote = if ($Sign) { "yes (Tfx.exe / uninstaller / setup.exe)" } else { "no" }

Write-Host "Installer created:"
Write-Host "  Version: $version"
Write-Host "  Runtime: $Runtime"
Write-Host "  Output:  $setupPath"
Write-Host "  Size:    $sizeMb MB"
Write-Host "  Signed:  $signedNote"
