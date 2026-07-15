[CmdletBinding()]
param(
    [ValidatePattern('^\d+\.\d+\.\d+$')]
    [string]$Version = '1.0.0',

    [ValidateSet('Debug', 'Release')]
    [string]$Configuration = 'Release',

    [string]$InnoCompiler = ''
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$repoRoot = Split-Path -Parent $PSScriptRoot
$artifactRoot = Join-Path $repoRoot 'artifacts/windows-x64'
$publishDirectory = Join-Path $artifactRoot 'publish'
$installerDirectory = Join-Path $artifactRoot 'installer'
$iconPath = Join-Path $repoRoot 'src/Snaploom.App/Assets/Snaploom.ico'
$installerDefinition = Join-Path $repoRoot 'packaging/windows/Snaploom.iss'
$chineseLanguagePath = Join-Path $repoRoot 'packaging/windows/Languages/ChineseSimplified.isl'
$maxInstallerBytes = 50000000

if (Test-Path $artifactRoot) {
    Remove-Item $artifactRoot -Recurse -Force
}

New-Item $publishDirectory -ItemType Directory -Force | Out-Null
New-Item $installerDirectory -ItemType Directory -Force | Out-Null

& dotnet run `
    --project (Join-Path $repoRoot 'tools/Snaploom.AssetGenerator/Snaploom.AssetGenerator.csproj') `
    --configuration $Configuration `
    -- windows-icon $iconPath
if ($LASTEXITCODE -ne 0) {
    throw 'Windows icon generation failed.'
}

& dotnet publish (Join-Path $repoRoot 'src/Snaploom.App/Snaploom.App.csproj') `
    --configuration $Configuration `
    --runtime win-x64 `
    --self-contained true `
    --output $publishDirectory `
    -p:SnaploomTargetWindows=true `
    -p:Version=$Version `
    -p:FileVersion="$Version.0" `
    -p:InformationalVersion=$Version `
    -p:DebugSymbols=false `
    -p:DebugType=None
if ($LASTEXITCODE -ne 0) {
    throw 'Windows self-contained publish failed.'
}

# NuGet native assets include large debugging symbol files that are not required at runtime.
Get-ChildItem $publishDirectory -Filter '*.pdb' -Recurse | Remove-Item -Force

if ([string]::IsNullOrWhiteSpace($InnoCompiler)) {
    $compilerCommand = Get-Command 'ISCC.exe' -ErrorAction SilentlyContinue
    if ($null -ne $compilerCommand) {
        $InnoCompiler = $compilerCommand.Source
    }
    else {
        $InnoCompiler = Join-Path ${env:ProgramFiles(x86)} 'Inno Setup 6/ISCC.exe'
    }
}

if (-not (Test-Path $InnoCompiler -PathType Leaf)) {
    throw "Inno Setup compiler was not found. Pass -InnoCompiler explicitly."
}

$compilerArguments = @(
    "/DAppVersion=$Version",
    "/DPublishDir=$publishDirectory",
    "/DOutputDir=$installerDirectory",
    "/DIconPath=$iconPath",
    "/DChineseLanguagePath=$chineseLanguagePath",
    $installerDefinition
)
& $InnoCompiler @compilerArguments
if ($LASTEXITCODE -ne 0) {
    throw 'Inno Setup compilation failed.'
}

$installerName = "snaploom-$Version-windows-x64-setup.exe"
$installerPath = Join-Path $installerDirectory $installerName
if (-not (Test-Path $installerPath -PathType Leaf)) {
    throw "Expected installer was not created: $installerName"
}

$installerBytes = (Get-Item $installerPath).Length
if ($installerBytes -gt $maxInstallerBytes) {
    throw "Installer is $installerBytes bytes; the 50 MB limit is $maxInstallerBytes bytes."
}

$sha256 = (Get-FileHash $installerPath -Algorithm SHA256).Hash.ToLowerInvariant()
$utf8NoBom = [System.Text.UTF8Encoding]::new($false)
$checksumPath = "$installerPath.sha256"
[System.IO.File]::WriteAllText(
    $checksumPath,
    "$sha256  $installerName`n",
    $utf8NoBom)

$metadata = [ordered]@{
    schemaVersion = 1
    product = 'Snaploom'
    appId = 'Snaploom.Desktop'
    version = $Version
    runtime = 'win-x64'
    selfContained = $true
    installer = $installerName
    installerBytes = $installerBytes
    maxInstallerBytes = $maxInstallerBytes
    sha256 = $sha256
}
$metadataPath = Join-Path $installerDirectory 'windows-installer-metadata.json'
[System.IO.File]::WriteAllText(
    $metadataPath,
    (($metadata | ConvertTo-Json) + "`n"),
    $utf8NoBom)

Write-Host "Created $installerName ($installerBytes bytes)."
Write-Host "SHA256: $sha256"
