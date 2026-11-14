#Requires -Version 5.1
<#
.SYNOPSIS
    Publishes the backend API into release\.

.DESCRIPTION
    Framework-dependent Release build for win-x64. The server needs the ASP.NET Core 10 runtime and the
    ConnectionStrings__DefaultConnection environment variable (see README.md).

.EXAMPLE
    .\build-backend.ps1
#>
[CmdletBinding()]
param(
    [string]$Configuration = 'Release',
    [string]$Runtime = 'win-x64'
)

$ErrorActionPreference = 'Stop'

$project = Join-Path $PSScriptRoot 'Backend.csproj'
$outputDir = Join-Path $PSScriptRoot 'release'

# release\ is committed, so the committed settings must not carry a connection string. The real one goes in
# appsettings.Local.json, which is git-ignored and published alongside.
foreach ($name in 'appsettings.json', 'appsettings.Development.json') {
    $file = Join-Path $PSScriptRoot $name
    if ((Test-Path $file) -and (Get-Content $file -Raw | ConvertFrom-Json).ConnectionStrings.DefaultConnection) {
        throw "$name has a connection string. Move it to appsettings.Local.json (git-ignored) and leave it empty in $name."
    }
}

if (Test-Path $outputDir) { Remove-Item -Path $outputDir -Recurse -Force }

dotnet publish $project -c $Configuration -r $Runtime --self-contained false -o $outputDir
if ($LASTEXITCODE -ne 0) { throw "Publish failed (exit code $LASTEXITCODE)." }

Write-Host "`nBackend published to $outputDir" -ForegroundColor Green
