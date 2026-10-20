#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the SPP CT-007 Measurement Studio Windows installer with Velopack.

.DESCRIPTION
    Reads the application id, name and version from Frontend\Config\application.yaml, then restores, builds and
    publishes Frontend for win-x64 (self-contained) into .\publish and packs it with vpk into .\Releases.
    Stops at the first failing step. Does not touch backend configuration and never packages a .env file.
    See docs\INSTALLER_GUIDE.md.

.EXAMPLE
    .\build-installer.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64',

    # Velopack output folder. Keep previous releases in it: Velopack builds delta updates from them.
    [string]$OutputDir = (Join-Path $PSScriptRoot 'Releases')
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$root = $PSScriptRoot
$project = Join-Path $root 'Frontend\Frontend.csproj'
$applicationYaml = Join-Path $root 'Frontend\Config\application.yaml'
$icon = Join-Path $root 'Frontend\Icons\icon.ico'
$publishDir = Join-Path $root 'publish'
$releasesDir = $OutputDir
$mainExe = 'Frontend.exe'

# Runs a native command and stops the script when it fails.
function Invoke-Step([string]$Title, [scriptblock]$Command) {
    Write-Host "`n==> $Title" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) {
        throw "$Title failed (exit code $LASTEXITCODE)."
    }
}

# Reads the keys under 'application:' (flat "key: value" lines, optional quotes).
function Read-ApplicationYaml([string]$Path) {
    if (-not (Test-Path $Path)) {
        throw "$Path not found."
    }

    $values = @{}
    $inApplication = $false

    foreach ($line in Get-Content -Path $Path -Encoding UTF8) {
        if ($line -match '^\S') {
            $inApplication = $line -match '^application:\s*$'
        }
        elseif ($inApplication -and $line -match '^\s+(\w+):\s*(.*?)\s*$') {
            $values[$Matches[1]] = $Matches[2].Trim('"', "'")
        }
    }

    foreach ($key in 'id', 'name', 'version') {
        if (-not $values.ContainsKey($key) -or [string]::IsNullOrWhiteSpace($values[$key])) {
            throw "${Path}: application.$key is missing or empty."
        }
    }

    if ($values.id -notmatch '^[A-Za-z0-9._-]+$') {
        throw "${Path}: application.id '$($values.id)' may only contain letters, digits, '.', '-' and '_'."
    }

    if ($values.version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$') {
        throw "${Path}: application.version '$($values.version)' is not a SemVer version (for example 1.0.0)."
    }

    return $values
}

$app = Read-ApplicationYaml $applicationYaml
Write-Host "Application: $($app.name) ($($app.id)) version $($app.version)"

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw "Velopack CLI 'vpk' not found. Install it with: dotnet tool install -g vpk --version 1.2.161"
}

# Start from an empty publish folder so stale files are never packaged.
if (Test-Path $publishDir) {
    Remove-Item -Path $publishDir -Recurse -Force
}

Invoke-Step 'Restore' {
    dotnet restore $project -r $Runtime
}

Invoke-Step 'Build' {
    dotnet build $project -c $Configuration -r $Runtime --self-contained true --no-restore "-p:Version=$($app.version)"
}

Invoke-Step 'Publish' {
    dotnet publish $project -c $Configuration -r $Runtime --self-contained true --no-build -o $publishDir "-p:Version=$($app.version)"
}

# The compiled product version must be the YAML version (Frontend.csproj reads the same file).
$exeVersion = (Get-Item (Join-Path $publishDir $mainExe)).VersionInfo.ProductVersion
if ($exeVersion -notlike "$($app.version)*") {
    throw "$mainExe has version '$exeVersion', expected '$($app.version)' from application.yaml."
}

if (-not (Test-Path (Join-Path $publishDir 'Config\application.yaml'))) {
    throw "Config\application.yaml is missing from $publishDir."
}

# Per-user settings stay on the user's machine: never ship a real .env.
$envFiles = @(Get-ChildItem -Path $publishDir -Recurse -Force -File -Filter '.env*')
if ($envFiles.Count -gt 0) {
    throw ".env files found in ${publishDir}: $($envFiles.FullName -join ', '). Remove them before packaging."
}

Invoke-Step 'Velopack pack' {
    vpk pack `
        --packId $app.id `
        --packVersion $app.version `
        --packTitle $app.name `
        --packDir $publishDir `
        --mainExe $mainExe `
        --icon $icon `
        --runtime $Runtime `
        --outputDir $releasesDir
}

# assets.win.json lists the files the last pack produced.
$assets = Get-Content -Path (Join-Path $releasesDir 'assets.win.json') -Raw | ConvertFrom-Json
$installer = $assets | Where-Object { $_.Type -eq 'Installer' } | Select-Object -First 1
if (-not $installer) {
    throw "Velopack reported no installer in $releasesDir\assets.win.json."
}

$installerPath = Join-Path $releasesDir $installer.RelativeFileName
Write-Host "`nInstaller built: $installerPath" -ForegroundColor Green
Write-Host "Version:         $($app.version) (Frontend.exe $exeVersion)"
Write-Host "Other files in ${releasesDir}:"
$assets | Where-Object { $_.Type -ne 'Installer' } | ForEach-Object { Write-Host "  $($_.Type): $($_.RelativeFileName)" }
