# Copyright (C) 2026 Snaploom contributors
# SPDX-License-Identifier: GPL-3.0-or-later

[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version,

    [ValidateSet('candidate-unsigned', 'stable-signed')]
    [string]$SigningMode = 'candidate-unsigned',

    [string]$OutputDirectory = '',
    [string]$InnoCompiler = '',
    [string]$SignTool = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $OutputDirectory = Join-Path $repoRoot 'artifacts/release/windows-x64'
}
$OutputDirectory = [System.IO.Path]::GetFullPath($OutputDirectory)
$temporaryRoot = Join-Path ([System.IO.Path]::GetTempPath()) "snaploom-windows-package-$([Guid]::NewGuid().ToString('N'))"
$desktopStaging = Join-Path $temporaryRoot 'desktop'
$hostPackageRoot = Join-Path $temporaryRoot "snaploom-capture-host-$Version-windows-x64"

function Invoke-Checked {
    param([string]$Command, [string[]]$Arguments)
    & $Command @Arguments
    if ($LASTEXITCODE -ne 0) {
        throw "$Command failed with exit code $LASTEXITCODE."
    }
}

function Resolve-SignTool {
    if (-not [string]::IsNullOrWhiteSpace($SignTool)) { return $SignTool }
    $command = Get-Command 'signtool.exe' -ErrorAction SilentlyContinue
    if ($null -ne $command) { return $command.Source }
    $kitsRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $candidate = Get-ChildItem $kitsRoot -Filter 'signtool.exe' -Recurse -ErrorAction SilentlyContinue |
        Where-Object { $_.FullName -like '*\x64\signtool.exe' } |
        Sort-Object FullName -Descending |
        Select-Object -First 1
    if ($null -eq $candidate) { throw 'signtool.exe is required for stable signing.' }
    return $candidate.FullName
}

function Sign-And-Verify {
    param([string]$Path)
    $certificate = $env:WINDOWS_SIGNING_CERTIFICATE_PATH
    $password = $env:WINDOWS_SIGNING_CERTIFICATE_PASSWORD
    $timestampUrl = $env:WINDOWS_RFC3161_TIMESTAMP_URL
    if ([string]::IsNullOrWhiteSpace($certificate) -or -not (Test-Path $certificate -PathType Leaf) -or
        [string]::IsNullOrWhiteSpace($password) -or [string]::IsNullOrWhiteSpace($timestampUrl)) {
        throw 'Stable signing requires WINDOWS_SIGNING_CERTIFICATE_PATH, WINDOWS_SIGNING_CERTIFICATE_PASSWORD, and WINDOWS_RFC3161_TIMESTAMP_URL.'
    }
    $tool = Resolve-SignTool
    Invoke-Checked $tool @('sign', '/fd', 'SHA256', '/td', 'SHA256', '/tr', $timestampUrl, '/f', $certificate, '/p', $password, $Path)
    Invoke-Checked $tool @('verify', '/pa', '/all', '/v', '/tw', $Path)
    $signature = Get-AuthenticodeSignature $Path
    if ($signature.Status -ne 'Valid') { throw "Authenticode verification failed for $Path." }
}

try {
    Invoke-Checked 'node' @((Join-Path $repoRoot 'tools/release/check-release-version.mjs'), '--version', $Version)
    if (Test-Path $OutputDirectory) { Remove-Item $OutputDirectory -Recurse -Force }
    New-Item $OutputDirectory -ItemType Directory -Force | Out-Null
    New-Item $desktopStaging -ItemType Directory -Force | Out-Null
    New-Item $hostPackageRoot -ItemType Directory -Force | Out-Null

    Push-Location $repoRoot
    try {
        Invoke-Checked 'pnpm' @('run', 'tauri:desktop')
        Invoke-Checked 'pnpm' @('run', 'tauri:host')
    }
    finally { Pop-Location }

    $desktopExecutable = Join-Path $repoRoot 'product/target/release/snaploom-desktop.exe'
    $hostExecutable = Join-Path $repoRoot 'product/target/release/snaploom-capture-host.exe'
    if (-not (Test-Path $desktopExecutable -PathType Leaf) -or -not (Test-Path $hostExecutable -PathType Leaf)) {
        throw 'Tauri Desktop or Capture Host executable is missing.'
    }
    Copy-Item $desktopExecutable $desktopStaging
    Copy-Item $hostExecutable $desktopStaging

    if ($SigningMode -eq 'stable-signed') {
        Sign-And-Verify (Join-Path $desktopStaging 'snaploom-desktop.exe')
        Sign-And-Verify (Join-Path $desktopStaging 'snaploom-capture-host.exe')
    }
    else {
        foreach ($path in @((Join-Path $desktopStaging 'snaploom-desktop.exe'), (Join-Path $desktopStaging 'snaploom-capture-host.exe'))) {
            if ((Get-AuthenticodeSignature $path).Status -ne 'NotSigned') {
                throw "Candidate executable unexpectedly contains an Authenticode signature: $path"
            }
        }
    }

    $licenses = Join-Path $desktopStaging 'LICENSES'
    New-Item $licenses -ItemType Directory -Force | Out-Null
    Copy-Item (Join-Path $repoRoot 'LICENSES/GPL-3.0-or-later.txt') $licenses
    Copy-Item (Join-Path $repoRoot 'product/distribution/NOTICE') (Join-Path $desktopStaging 'NOTICE')
    Invoke-Checked 'node' @(
        (Join-Path $repoRoot 'tools/compliance/generate-rust-notice.mjs'),
        (Join-Path $repoRoot 'product/Cargo.toml'),
        (Join-Path $desktopStaging 'THIRD-PARTY-NOTICES.txt'))
    Invoke-Checked 'node' @(
        (Join-Path $repoRoot 'tools/release/generate-file-sbom.mjs'),
        '--root', $desktopStaging, '--output', (Join-Path $desktopStaging 'sbom.cdx.json'),
        '--name', 'Snaploom Desktop', '--version', $Version, '--license', 'GPL-3.0-or-later')

    Copy-Item (Join-Path $desktopStaging 'snaploom-capture-host.exe') $hostPackageRoot
    New-Item (Join-Path $hostPackageRoot 'LICENSES') -ItemType Directory -Force | Out-Null
    Copy-Item (Join-Path $repoRoot 'LICENSES/GPL-3.0-or-later.txt') (Join-Path $hostPackageRoot 'LICENSES')
    Copy-Item (Join-Path $repoRoot 'product/distribution/NOTICE') (Join-Path $hostPackageRoot 'NOTICE')
    Copy-Item (Join-Path $repoRoot 'product/distribution/CAPTURE-HOST-README.md') (Join-Path $hostPackageRoot 'README.md')
    Copy-Item (Join-Path $desktopStaging 'THIRD-PARTY-NOTICES.txt') $hostPackageRoot
    Invoke-Checked 'node' @(
        (Join-Path $repoRoot 'tools/release/generate-file-sbom.mjs'),
        '--root', $hostPackageRoot, '--output', (Join-Path $hostPackageRoot 'sbom.cdx.json'),
        '--name', 'Snaploom Capture Host', '--version', $Version, '--license', 'GPL-3.0-or-later')
    $hostArchive = Join-Path $OutputDirectory "snaploom-capture-host-$Version-windows-x64.zip"
    Invoke-Checked 'node' @(
        (Join-Path $repoRoot 'tools/release/create-deterministic-archive.mjs'),
        '--root', $hostPackageRoot, '--output', $hostArchive, '--format', 'zip')

    if ([string]::IsNullOrWhiteSpace($InnoCompiler)) {
        $innoCommand = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
        $InnoCompiler = if ($null -ne $innoCommand) {
            $innoCommand.Source
        }
        else {
            Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6\ISCC.exe'
        }
    }
    if (-not (Test-Path $InnoCompiler -PathType Leaf)) {
        throw 'Inno Setup 6 compiler was not found.'
    }
    $installerDefinition = Join-Path $repoRoot 'packaging/windows/Snaploom.iss'
    $iconPath = Join-Path $repoRoot 'product/apps/desktop/icons/icon.ico'
    Invoke-Checked $InnoCompiler @(
        "/DAppVersion=$Version",
        "/DStagingDir=$desktopStaging",
        "/DOutputDir=$OutputDirectory",
        "/DIconPath=$iconPath",
        $installerDefinition)
    $installer = Join-Path $OutputDirectory "snaploom-$Version-windows-x64-setup.exe"
    if (-not (Test-Path $installer -PathType Leaf)) { throw 'Windows installer was not generated.' }
    if ((Get-Item $installer).Length -gt 50000000) { throw 'Windows installer exceeds 50,000,000 bytes.' }
    if ($SigningMode -eq 'stable-signed') {
        Sign-And-Verify $installer
    }
    elseif ((Get-AuthenticodeSignature $installer).Status -ne 'NotSigned') {
        throw 'Candidate installer unexpectedly contains an Authenticode signature.'
    }

    $thumbprint = ''
    if ($SigningMode -eq 'stable-signed') {
        $thumbprint = (Get-AuthenticodeSignature $installer).SignerCertificate.Thumbprint.ToLowerInvariant()
    }
    $metadata = [ordered]@{
        schemaVersion = 1
        version = $Version
        platform = 'windows-x64'
        signingMode = $SigningMode
        authenticodeVerified = $SigningMode -eq 'stable-signed'
        rfc3161TimestampVerified = $SigningMode -eq 'stable-signed'
        certificateThumbprint = $thumbprint
        installer = [System.IO.Path]::GetFileName($installer)
        installerBytes = (Get-Item $installer).Length
        installerSha256 = (Get-FileHash $installer -Algorithm SHA256).Hash.ToLowerInvariant()
        host = [System.IO.Path]::GetFileName($hostArchive)
        hostSha256 = (Get-FileHash $hostArchive -Algorithm SHA256).Hash.ToLowerInvariant()
        hostExecutableSha256 = (Get-FileHash (Join-Path $desktopStaging 'snaploom-capture-host.exe') -Algorithm SHA256).Hash.ToLowerInvariant()
    }
    $utf8NoBom = [System.Text.UTF8Encoding]::new($false)
    [System.IO.File]::WriteAllText(
        (Join-Path $OutputDirectory 'windows-package-metadata.json'),
        (($metadata | ConvertTo-Json -Depth 4) + "`n"),
        $utf8NoBom)
    Write-Host "Created $($metadata.installer) and $($metadata.host) in $OutputDirectory"
}
finally {
    if (Test-Path $temporaryRoot) { Remove-Item $temporaryRoot -Recurse -Force }
}
