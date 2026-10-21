#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the SPP CT-007 Measurement Studio Windows installer with Velopack.

.DESCRIPTION
    Reads the application id, name and version from Frontend\Config\application.yaml, validates the production API
    configuration Frontend\.env.production, then restores, builds and publishes Frontend for win-x64
    (self-contained, AppEnvironment=production) into .\publish and packs it with vpk into .\Releases.
    The installer ships Frontend\.env.production as Config\.env.production.template; no other .env file is packaged.
    Stops at the first failing step. Does not touch backend configuration.
    See docs\INSTALLER_GUIDE.md and docs\API_CONFIGURATION.md.

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
$productionEnv = Join-Path $root 'Frontend\.env.production'
$productionTemplate = 'Config\.env.production.template'   # ApiSettings.ProductionTemplate
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

# Checks the production template with the app's rules (Frontend\Api\ApiSettings.cs). Only the four known keys are
# allowed, so a password, token or connection string can never be packaged by accident.
function Test-ProductionEnv([string]$Path) {
    if (-not (Test-Path $Path)) {
        throw "$Path not found. Create it from Frontend\.env.example with APP_ENV=production and API_BASE_URL set to the production backend; it becomes the installer's configuration template."
    }

    $allowed = 'APP_ENV', 'API_BASE_URL', 'API_TIMEOUT_SECONDS', 'API_RETRY_INTERVAL_SECONDS'
    $values = @{}
    $number = 0

    foreach ($line in Get-Content -Path $Path -Encoding UTF8) {
        $number++
        $line = $line.Trim()
        if ($line -eq '' -or $line.StartsWith('#')) { continue }

        if ($line -notmatch '^([^=]+?)\s*=\s*(.*)$') {
            throw "${Path}, line ${number}: expected KEY=value."
        }

        $key = $Matches[1].Trim()
        if ($allowed -notcontains $key) {
            throw "${Path}, line ${number}: '$key' is not an API setting. The file is packaged into the installer, so it may only contain $($allowed -join ', '); never put passwords, tokens or connection strings in it."
        }

        $values[$key] = $Matches[2].Trim().Trim('"', "'")
    }

    if ($values.ContainsKey('APP_ENV') -and $values.APP_ENV -ne 'production') {
        throw "${Path}: APP_ENV is '$($values.APP_ENV)'; the production file must say APP_ENV=production (or omit the line)."
    }

    $url = $null
    if (-not $values.ContainsKey('API_BASE_URL') -or -not [Uri]::TryCreate($values.API_BASE_URL, [UriKind]::Absolute, [ref]$url) -or
        $url.Scheme -notin 'http', 'https') {
        throw "${Path}: API_BASE_URL must be set to an absolute http:// or https:// URL (for example https://ct007-api.example.com)."
    }

    if ($url.UserInfo -or $url.Query -match '(?i)(key|token|secret|password|pwd|sig)=') {
        throw "${Path}: API_BASE_URL must not contain a user name, password or token; the file is packaged into the installer."
    }

    foreach ($key in 'API_TIMEOUT_SECONDS', 'API_RETRY_INTERVAL_SECONDS') {
        $seconds = 0.0
        if ($values.ContainsKey($key) -and -not ([double]::TryParse($values[$key], [Globalization.NumberStyles]::Float,
                    [Globalization.CultureInfo]::InvariantCulture, [ref]$seconds) -and $seconds -gt 0)) {
            throw "${Path}: $key must be a number of seconds greater than 0."
        }
    }

    return $url
}

$app = Read-ApplicationYaml $applicationYaml
Write-Host "Application: $($app.name) ($($app.id)) version $($app.version)"

$productionUrl = Test-ProductionEnv $productionEnv
Write-Host "Production API: $($productionUrl.GetLeftPart([UriPartial]::Authority)) (from Frontend\.env.production)"

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
    dotnet build $project -c $Configuration -r $Runtime --self-contained true --no-restore "-p:Version=$($app.version)" "-p:AppEnvironment=production"
}

Invoke-Step 'Publish' {
    dotnet publish $project -c $Configuration -r $Runtime --self-contained true --no-build -o $publishDir "-p:Version=$($app.version)" "-p:AppEnvironment=production"
}

# The compiled product version must be the YAML version (Frontend.csproj reads the same file).
$exeVersion = (Get-Item (Join-Path $publishDir $mainExe)).VersionInfo.ProductVersion
if ($exeVersion -notlike "$($app.version)*") {
    throw "$mainExe has version '$exeVersion', expected '$($app.version)' from application.yaml."
}

if (-not (Test-Path (Join-Path $publishDir 'Config\application.yaml'))) {
    throw "Config\application.yaml is missing from $publishDir."
}

# The production template must be exactly the validated file, and it is the only .env file packaged.
$templatePath = Join-Path $publishDir $productionTemplate
if (-not (Test-Path $templatePath) -or (Get-FileHash $templatePath).Hash -ne (Get-FileHash $productionEnv).Hash) {
    throw "$productionTemplate in $publishDir is missing or differs from Frontend\.env.production."
}

$envFiles = @(Get-ChildItem -Path $publishDir -Recurse -Force -File -Filter '.env*' | Where-Object { $_.FullName -ne (Resolve-Path $templatePath).Path })
if ($envFiles.Count -gt 0) {
    throw "Unexpected .env files in ${publishDir}: $($envFiles.FullName -join ', '). Only $productionTemplate may be packaged."
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
