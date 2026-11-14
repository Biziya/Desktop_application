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

if (Test-Path $outputDir) { Remove-Item -Path $outputDir -Recurse -Force }

dotnet publish $project -c $Configuration -r $Runtime --self-contained false -o $outputDir
if ($LASTEXITCODE -ne 0) { throw "Publish failed (exit code $LASTEXITCODE)." }

Write-Host "`nBackend published to $outputDir" -ForegroundColor Green
