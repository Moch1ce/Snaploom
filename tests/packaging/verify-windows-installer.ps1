[CmdletBinding()]
param(
    [Parameter(Mandatory)]
    [string]$InstallerDirectory,

    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.0'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$metadataPath = Join-Path $InstallerDirectory 'windows-installer-metadata.json'
if (-not (Test-Path $metadataPath -PathType Leaf)) {
    throw 'Installer metadata is missing.'
}

$metadata = Get-Content $metadataPath -Raw | ConvertFrom-Json
if ($metadata.product -ne 'Snaploom' -or
    $metadata.appId -ne 'Snaploom.Desktop' -or
    $metadata.version -ne $Version -or
    $metadata.runtime -ne 'win-x64' -or
    -not $metadata.selfContained) {
    throw 'Installer metadata does not match the Windows distribution contract.'
}

$installerPath = Join-Path $InstallerDirectory $metadata.installer
$installer = Get-Item $installerPath
if ($installer.Length -ne $metadata.installerBytes -or
    $installer.Length -gt $metadata.maxInstallerBytes -or
    $metadata.maxInstallerBytes -ne 50000000) {
    throw 'Installer size metadata is invalid or exceeds 50 MB.'
}

$actualHash = (Get-FileHash $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
$checksumPath = "$installerPath.sha256"
$checksum = (Get-Content $checksumPath -Raw).Trim()
if ($actualHash -ne $metadata.sha256 -or
    $checksum -ne "$actualHash  $($installer.Name)") {
    throw 'Installer SHA256 metadata does not match the generated EXE.'
}

$testRoot = Join-Path ([System.IO.Path]::GetTempPath()) "snaploom-installer-$([Guid]::NewGuid().ToString('N'))"
$installDirectory = Join-Path $testRoot 'Snaploom'
$installLog = Join-Path $testRoot 'install.log'
$uninstallLog = Join-Path $testRoot 'uninstall.log'
$uninstallRegistryPath = 'HKCU:\Software\Microsoft\Windows\CurrentVersion\Uninstall\Snaploom.Desktop_is1'
$localConfigurationDirectory = Join-Path $env:LOCALAPPDATA 'Snaploom'
$uninstaller = Join-Path $installDirectory 'unins000.exe'
$appProcess = $null

if (Test-Path $uninstallRegistryPath) {
    throw 'Refusing to run installer verification over an existing Snaploom installation.'
}

New-Item $testRoot -ItemType Directory -Force | Out-Null

function Invoke-Installer {
    $arguments = @(
        '/VERYSILENT',
        '/SUPPRESSMSGBOXES',
        '/NORESTART',
        '/SP-',
        "/DIR=`"$installDirectory`"",
        "/LOG=`"$installLog`""
    )
    $process = Start-Process $installerPath -ArgumentList $arguments -Wait -PassThru
    if ($process.ExitCode -ne 0) {
        throw "Installer exited with code $($process.ExitCode)."
    }
}

try {
    Invoke-Installer

    $appExecutable = Join-Path $installDirectory 'Snaploom.App.exe'
    if (-not (Test-Path $appExecutable -PathType Leaf)) {
        throw 'Installed Snaploom executable is missing.'
    }

    $versionInfo = (Get-Item $appExecutable).VersionInfo
    if ($versionInfo.ProductName -ne 'Snaploom' -or
        $versionInfo.ProductVersion -notlike "$Version*") {
        throw 'Installed product metadata is incorrect.'
    }

    $uninstallEntry = Get-ItemProperty $uninstallRegistryPath
    if ($uninstallEntry.DisplayName -ne 'Snaploom' -or
        $uninstallEntry.DisplayVersion -ne $Version -or
        $uninstallEntry.InstallLocation.TrimEnd('\') -ne $installDirectory.TrimEnd('\') -or
        $uninstallEntry.DisplayIcon -notlike '*Snaploom.App.exe*') {
        throw 'The per-user uninstall entry is incorrect.'
    }

    $launchStartedAt = [DateTime]::UtcNow.AddSeconds(-1)
    $appProcess = Start-Process $appExecutable -PassThru
    Start-Sleep -Seconds 5
    if ($appProcess.HasExited) {
        throw "Installed Snaploom did not remain running (exit code $($appProcess.ExitCode))."
    }

    Stop-Process $appProcess -Force
    $appProcess.WaitForExit()

    $logDirectory = Join-Path $localConfigurationDirectory 'Logs'
    $newLog = Get-ChildItem $logDirectory -Filter '*.log' -File -ErrorAction SilentlyContinue |
        Where-Object { $_.LastWriteTimeUtc -ge $launchStartedAt } |
        Select-Object -First 1
    if ($null -eq $newLog) {
        throw 'Snaploom did not use the expected per-user configuration directory.'
    }

    Invoke-Installer
    if (-not (Test-Path $appExecutable -PathType Leaf)) {
        throw 'Upgrade-style overwrite removed the application executable.'
    }

    $uninstallArguments = @(
        '/VERYSILENT',
        '/SUPPRESSMSGBOXES',
        '/NORESTART',
        "/LOG=`"$uninstallLog`""
    )
    $uninstallProcess = Start-Process $uninstaller -ArgumentList $uninstallArguments -Wait -PassThru
    if ($uninstallProcess.ExitCode -ne 0) {
        throw "Uninstaller exited with code $($uninstallProcess.ExitCode)."
    }

    if (Test-Path $appExecutable -PathType Leaf) {
        throw 'Uninstall left the application executable behind.'
    }

    if (Test-Path $uninstallRegistryPath) {
        throw 'Uninstall left the per-user uninstall entry behind.'
    }

    if (-not (Test-Path $logDirectory -PathType Container)) {
        throw 'Uninstall unexpectedly removed the per-user configuration directory.'
    }
}
finally {
    if ($null -ne $appProcess -and -not $appProcess.HasExited) {
        Stop-Process -Id $appProcess.Id -Force
    }

    if (Test-Path $uninstaller -PathType Leaf) {
        try {
            Start-Process $uninstaller -ArgumentList @(
                '/VERYSILENT',
                '/SUPPRESSMSGBOXES',
                '/NORESTART'
            ) -Wait | Out-Null
        }
        catch {
        }
    }

    if (Test-Path $uninstallRegistryPath) {
        Remove-Item $uninstallRegistryPath -Recurse -Force
    }

    if (Test-Path $installDirectory) {
        Remove-Item $installDirectory -Recurse -Force
    }

    if (Test-Path $testRoot) {
        Remove-Item $testRoot -Recurse -Force
    }
}

Write-Host 'Windows installer install, launch, overwrite, and uninstall checks passed.'
