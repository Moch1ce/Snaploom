# Copyright (C) 2026 Snaploom contributors
# SPDX-License-Identifier: GPL-3.0-or-later

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$Directory,

    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [ValidateSet('candidate-unsigned', 'stable-unsigned', 'stable-signed')]
    [string]$ExpectedSigning = 'candidate-unsigned'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$expectsSigned = $ExpectedSigning -eq 'stable-signed'

$metadataPath = Join-Path $Directory 'windows-package-metadata.json'
$metadata = Get-Content $metadataPath -Raw | ConvertFrom-Json
if ($metadata.schemaVersion -ne 1 -or $metadata.version -ne $Version -or
    $metadata.platform -ne 'windows-x64' -or $metadata.signingMode -ne $ExpectedSigning -or
    $metadata.installerBytes -gt 50000000) {
    throw 'Windows package metadata violates the distribution contract.'
}
$installer = Join-Path $Directory $metadata.installer
$hostArchive = Join-Path $Directory $metadata.host
if (-not (Test-Path $installer -PathType Leaf) -or -not (Test-Path $hostArchive -PathType Leaf)) {
    throw 'Windows installer or standalone Host archive is missing.'
}
if ((Get-FileHash $installer -Algorithm SHA256).Hash.ToLowerInvariant() -ne $metadata.installerSha256 -or
    (Get-FileHash $hostArchive -Algorithm SHA256).Hash.ToLowerInvariant() -ne $metadata.hostSha256) {
    throw 'Windows package digest does not match metadata.'
}

$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) "snaploom-windows-verify-$([Guid]::NewGuid().ToString('N'))"
$hostExtract = Join-Path $temporaryRoot 'host'
$installDirectory = Join-Path $temporaryRoot 'installed'
$installLog = Join-Path $temporaryRoot 'install.log'
$uninstallLog = Join-Path $temporaryRoot 'uninstall.log'
$registryPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Snaploom.Desktop_is1'
$hostRegistryPath = 'HKCU:\Software\Snaploom\CaptureHost'
$desktopProcess = $null

function Invoke-Installer {
    $arguments = @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', '/SP-', "/DIR=`"$installDirectory`"", "/LOG=`"$installLog`"")
    $process = Start-Process $installer -ArgumentList $arguments -Wait -PassThru
    if ($process.ExitCode -ne 0) { throw "Installer exited with $($process.ExitCode)." }
}

try {
    New-Item $hostExtract -ItemType Directory -Force | Out-Null
    Expand-Archive $hostArchive $hostExtract
    $standaloneHost = Get-ChildItem $hostExtract -Filter 'snaploom-capture-host.exe' -Recurse -File | Select-Object -First 1
    if ($null -eq $standaloneHost) { throw 'Standalone Host executable is missing.' }
    foreach ($required in @('LICENSES\GPL-3.0-or-later.txt', 'NOTICE', 'README.md', 'THIRD-PARTY-NOTICES.txt', 'sbom.cdx.json')) {
        if ($null -eq (Get-ChildItem $hostExtract -Filter ([System.IO.Path]::GetFileName($required)) -Recurse -File | Select-Object -First 1)) {
            throw "Standalone Host package is missing $required."
        }
    }
    if ((Get-FileHash $standaloneHost.FullName -Algorithm SHA256).Hash.ToLowerInvariant() -ne $metadata.hostExecutableSha256) {
        throw 'Standalone Host differs from the installer staging binary.'
    }

    if (-not $expectsSigned) {
        if ((Get-AuthenticodeSignature $installer).Status -ne 'NotSigned' -or
            (Get-AuthenticodeSignature $standaloneHost.FullName).Status -ne 'NotSigned' -or
            $metadata.authenticodeVerified -or $metadata.rfc3161TimestampVerified -or
            -not $metadata.unknownPublisherWarning) {
            throw 'Unsigned Windows package does not disclose its unknown-publisher status.'
        }
    }
    else {
        if ((Get-AuthenticodeSignature $installer).Status -ne 'Valid' -or
            (Get-AuthenticodeSignature $standaloneHost.FullName).Status -ne 'Valid' -or
            -not $metadata.authenticodeVerified -or -not $metadata.rfc3161TimestampVerified -or
            [string]::IsNullOrWhiteSpace($metadata.certificateThumbprint)) {
            throw 'Stable Windows package lacks verified Authenticode/RFC3161 evidence.'
        }
    }

    if (Test-Path $registryPath) { throw 'Refusing to overwrite an existing Snaploom installation.' }
    Invoke-Installer
    $desktop = Join-Path $installDirectory 'snaploom-desktop.exe'
    $installedHost = Join-Path $installDirectory 'snaploom-capture-host.exe'
    if (-not (Test-Path $desktop -PathType Leaf) -or -not (Test-Path $installedHost -PathType Leaf)) {
        throw 'Installer did not install both Desktop and Capture Host.'
    }
    if ((Get-FileHash $installedHost -Algorithm SHA256).Hash.ToLowerInvariant() -ne $metadata.hostExecutableSha256) {
        throw 'Installed Host differs from the standalone Host asset.'
    }
    $uninstall = Get-ItemProperty $registryPath
    if ($uninstall.DisplayName -ne 'Snaploom') {
        throw "Per-user uninstall DisplayName is '$($uninstall.DisplayName)', expected 'Snaploom'."
    }
    if ($uninstall.DisplayVersion -ne $Version) {
        throw "Per-user uninstall DisplayVersion is '$($uninstall.DisplayVersion)', expected '$Version'."
    }
    if ($uninstall.InstallLocation.TrimEnd('\') -ne $installDirectory.TrimEnd('\')) {
        throw "Per-user InstallLocation is '$($uninstall.InstallLocation)', expected '$installDirectory'."
    }
    $hostRegistration = Get-ItemProperty $hostRegistryPath
    if ($hostRegistration.InstallPath -ne $installedHost) { throw 'Capture Host registration is invalid.' }

    $desktopProcess = Start-Process $desktop -PassThru
    Start-Sleep -Seconds 5
    if ($desktopProcess.HasExited) { throw "Installed Desktop exited with $($desktopProcess.ExitCode)." }
    Stop-Process $desktopProcess -Force
    $desktopProcess.WaitForExit()
    $desktopProcess = $null

    Invoke-Installer
    if (-not (Test-Path $desktop -PathType Leaf)) { throw 'Overwrite upgrade removed Desktop.' }
    $uninstaller = Join-Path $installDirectory 'unins000.exe'
    $uninstallProcess = Start-Process $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART', "/LOG=`"$uninstallLog`"") -Wait -PassThru
    if ($uninstallProcess.ExitCode -ne 0) { throw "Uninstaller exited with $($uninstallProcess.ExitCode)." }
    if ((Test-Path $desktop -PathType Leaf) -or (Test-Path $registryPath) -or (Test-Path $hostRegistryPath)) {
        throw 'Uninstall left application files or per-user registration behind.'
    }
}
finally {
    if ($null -ne $desktopProcess -and -not $desktopProcess.HasExited) { Stop-Process $desktopProcess -Force }
    $uninstaller = Join-Path $installDirectory 'unins000.exe'
    if (Test-Path $uninstaller -PathType Leaf) {
        try { Start-Process $uninstaller -ArgumentList @('/VERYSILENT', '/SUPPRESSMSGBOXES', '/NORESTART') -Wait | Out-Null } catch {}
    }
    foreach ($path in @($registryPath, $hostRegistryPath)) {
        if (Test-Path $path) { Remove-Item $path -Recurse -Force }
    }
    if (Test-Path $temporaryRoot) { Remove-Item $temporaryRoot -Recurse -Force }
}

Write-Host 'Windows installer, standalone Host, signing, install, launch, overwrite, and uninstall checks passed.'
