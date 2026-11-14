#Requires -Version 5.1
<#
.SYNOPSIS
    Builds the SPP CT007 Windows installer with Velopack.

.DESCRIPTION
    Publishes Frontend for win-x64 (self-contained, production) into release\publish and packs it into
    release\installer. Name and version come from Config\application.yaml; the API address comes from .env.production.

.EXAMPLE
    .\build-installer.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

$project = Join-Path $PSScriptRoot 'Frontend.csproj'
$applicationYaml = Join-Path $PSScriptRoot 'Config\application.yaml'
$icon = Join-Path $PSScriptRoot 'Icons\icon.ico'
$productionEnv = Join-Path $PSScriptRoot '.env.production'
$productionTemplate = 'Config\.env.production.template'   # ApiSettings.ProductionTemplate
$publishDir = Join-Path $PSScriptRoot 'release\publish'
# Keep old packages here: Velopack builds delta updates from them.
$installerDir = Join-Path $PSScriptRoot 'release\installer'
$mainExe = 'Frontend.exe'

function Invoke-Step([string]$Title, [scriptblock]$Command) {
    Write-Host "`n==> $Title" -ForegroundColor Cyan
    & $Command
    if ($LASTEXITCODE -ne 0) { throw "$Title failed (exit code $LASTEXITCODE)." }
}

# Reads the flat "key: value" lines under 'application:'.
function Read-ApplicationYaml([string]$Path) {
    if (-not (Test-Path $Path)) { throw "$Path not found." }

    $values = @{}
    $inApplication = $false
    foreach ($line in Get-Content -Path $Path -Encoding UTF8) {
        if ($line -match '^\S') { $inApplication = $line -match '^application:\s*$' }
        elseif ($inApplication -and $line -match '^\s+(\w+):\s*(.*?)\s*$') { $values[$Matches[1]] = $Matches[2].Trim('"', "'") }
    }

    foreach ($key in 'id', 'name', 'version') {
        if ([string]::IsNullOrWhiteSpace($values[$key])) { throw "${Path}: application.$key is missing or empty." }
    }
    if ($values.id -notmatch '^[A-Za-z0-9._-]+$') {
        throw "${Path}: application.id '$($values.id)' may only contain letters, digits, '.', '-' and '_'."
    }
    if ($values.version -notmatch '^\d+\.\d+\.\d+(-[0-9A-Za-z.-]+)?(\+[0-9A-Za-z.-]+)?$') {
        throw "${Path}: application.version '$($values.version)' is not a SemVer version (for example 1.0.0)."
    }
    return $values
}

# The file is packaged into the installer, so only the four API keys are allowed: no secrets can slip in.
function Test-ProductionEnv([string]$Path) {
    if (-not (Test-Path $Path)) {
        throw "$Path not found. Copy .env.example, set APP_ENV=production and API_BASE_URL to the production backend."
    }

    $allowed = 'APP_ENV', 'API_BASE_URL', 'API_TIMEOUT_SECONDS', 'API_RETRY_INTERVAL_SECONDS'
    $values = @{}
    $number = 0
    foreach ($line in Get-Content -Path $Path -Encoding UTF8) {
        $number++
        $line = $line.Trim()
        if ($line -eq '' -or $line.StartsWith('#')) { continue }
        if ($line -notmatch '^([^=]+?)\s*=\s*(.*)$') { throw "${Path}, line ${number}: expected KEY=value." }

        $key = $Matches[1].Trim()
        if ($allowed -notcontains $key) {
            throw "${Path}, line ${number}: '$key' is not allowed. Only $($allowed -join ', '); never passwords, tokens or connection strings."
        }
        $values[$key] = $Matches[2].Trim().Trim('"', "'")
    }

    if ($values.ContainsKey('APP_ENV') -and $values.APP_ENV -ne 'production') {
        throw "${Path}: APP_ENV is '$($values.APP_ENV)'; it must be production (or omitted)."
    }

    $url = $null
    if (-not $values.ContainsKey('API_BASE_URL') -or -not [Uri]::TryCreate($values.API_BASE_URL, [UriKind]::Absolute, [ref]$url) -or
        $url.Scheme -notin 'http', 'https') {
        throw "${Path}: API_BASE_URL must be an absolute http:// or https:// URL."
    }
    if ($url.UserInfo -or $url.Query -match '(?i)(key|token|secret|password|pwd|sig)=') {
        throw "${Path}: API_BASE_URL must not contain a user name, password or token."
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
Write-Host "Production API: $($productionUrl.GetLeftPart([UriPartial]::Authority))"

if (-not (Get-Command vpk -ErrorAction SilentlyContinue)) {
    throw "Velopack CLI 'vpk' not found. Install it with: dotnet tool install -g vpk --version 1.2.161"
}

# Never package stale files.
if (Test-Path $publishDir) { Remove-Item -Path $publishDir -Recurse -Force }

$versionArgs = "-p:Version=$($app.version)", '-p:AppEnvironment=production'
Invoke-Step 'Restore' { dotnet restore $project -r $Runtime }
Invoke-Step 'Build' { dotnet build $project -c $Configuration -r $Runtime --self-contained true --no-restore @versionArgs }
Invoke-Step 'Publish' { dotnet publish $project -c $Configuration -r $Runtime --self-contained true --no-build -o $publishDir @versionArgs }

$exeVersion = (Get-Item (Join-Path $publishDir $mainExe)).VersionInfo.ProductVersion
if ($exeVersion -notlike "$($app.version)*") {
    throw "$mainExe has version '$exeVersion', expected '$($app.version)' from application.yaml."
}
if (-not (Test-Path (Join-Path $publishDir 'Config\application.yaml'))) {
    throw "Config\application.yaml is missing from $publishDir."
}

# The template must be exactly the validated file, and the only .env file packaged.
$templatePath = Join-Path $publishDir $productionTemplate
if (-not (Test-Path $templatePath) -or (Get-FileHash $templatePath).Hash -ne (Get-FileHash $productionEnv).Hash) {
    throw "$productionTemplate in $publishDir is missing or differs from .env.production."
}
$envFiles = @(Get-ChildItem -Path $publishDir -Recurse -Force -File -Filter '.env*' | Where-Object { $_.FullName -ne (Resolve-Path $templatePath).Path })
if ($envFiles.Count -gt 0) {
    throw "Unexpected .env files in ${publishDir}: $($envFiles.FullName -join ', ')."
}

Invoke-Step 'Velopack pack' {
    vpk pack --packId $app.id --packVersion $app.version --packTitle $app.name --packDir $publishDir `
        --mainExe $mainExe --icon $icon --runtime $Runtime --outputDir $installerDir
}

$assets = Get-Content -Path (Join-Path $installerDir 'assets.win.json') -Raw | ConvertFrom-Json
$installer = $assets | Where-Object { $_.Type -eq 'Installer' } | Select-Object -First 1
if (-not $installer) { throw "Velopack reported no installer in $installerDir\assets.win.json." }

Write-Host "`nInstaller: $(Join-Path $installerDir $installer.RelativeFileName)" -ForegroundColor Green
Write-Host "Version:   $($app.version)"
$assets | Where-Object { $_.Type -ne 'Installer' } | ForEach-Object { Write-Host "  $($_.Type): $($_.RelativeFileName)" }
