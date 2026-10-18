<#
.SYNOPSIS
  Incremental regression run: Git changes since the last verified run -> impacted test IDs -> isolated build -> run -> artifacts.

.DESCRIPTION
  1. Changed files = git diff <Baseline>..HEAD, plus uncommitted and untracked files.
  2. Selection = direct + dependent tests from impact-map.json for each change, plus the critical regression set.
     Unmapped Frontend/Backend files, shared infrastructure, build files or test changes select the full suite.
  3. Frontend/, Backend/, Tests/ and CT007.slnx are copied to a scratch folder and restored offline, then built and
     run there. The repository's tracked obj/ files are never touched. Nothing is committed.
  4. Writes selection.json, results.csv, console.log, build.log and run-info.json to qa/regression/<RunId>/artifacts.
  5. Always, also when the build or tests fail: updates qa/excel-reports/Test Results.xlsx and Bug List.xlsx
     (qa/excel-reports/generate_reports.py) and copies them into the artifacts. The exit code stays the test
     runner's (1 when a test failed); a reporting problem is only a warning.
  The baseline is read from qa/regression/baseline.json unless -Baseline is given. The script does not update it:
  record a new baseline only after the run's results have been reviewed.

.EXAMPLE
  powershell -File Tests/regression/Invoke-Regression.ps1 -RunId reg-20261016-01
  powershell -File Tests/regression/Invoke-Regression.ps1 -RunId reg-x -Full -Include Timing,Sql
  powershell -File Tests/regression/Invoke-Regression.ps1 -RunId reg-x -Tests FE-047,BE-030
#>
param(
    [Parameter(Mandatory = $true)][string]$RunId,
    [string]$Baseline,
    [switch]$Full,
    [string[]]$Include = @('Timing'),
    [string]$PackageSource = (Join-Path $env:USERPROFILE '.nuget\packages'),
    [string]$WorkRoot = (Join-Path $env:TEMP 'ct007-regression'),
    [switch]$SelectOnly,
    [switch]$WarnAsError,  # static check: compiler warnings (in the test project and the projects it builds) fail the build
    [string[]]$Tests       # run exactly these test IDs or ID prefixes instead of the change-based selection
)

$ErrorActionPreference = 'Stop'
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
$git = @('-c', "safe.directory=$($repo -replace '\\', '/')", '-C', $repo)
$artifacts = Join-Path $repo "qa\regression\$RunId\artifacts"
New-Item -ItemType Directory -Force $artifacts | Out-Null

if (-not $Baseline) {
    $Baseline = (Get-Content (Join-Path $repo 'qa\regression\baseline.json') -Raw | ConvertFrom-Json).commit
}

# ---- 1. Changes ---------------------------------------------------------------------------------------------
$head = (& git @git rev-parse HEAD).Trim()
$changes = @(& git @git diff --name-status "$Baseline..HEAD")
$changes += @(& git @git status --porcelain | ForEach-Object { 'W ' + $_.Substring(3) })
$files = @($changes | Where-Object { $_ } | ForEach-Object { ($_ -split '\s+')[-1] } | Sort-Object -Unique)

# ---- 2. Selection -------------------------------------------------------------------------------------------
$map = Get-Content (Join-Path $PSScriptRoot 'impact-map.json') -Raw | ConvertFrom-Json

function Expand([string[]]$ids) {
    foreach ($id in $ids) {
        if ($id -match '^([A-Z0-9]+)-(\d{3})\.\.\1-(\d{3})$') {
            [int]$Matches[2]..[int]$Matches[3] | ForEach-Object { '{0}-{1:D3}' -f $Matches[1], $_ }
        } else { $id }
    }
}

$direct = @(); $dependents = @(); $manual = @(); $fullReasons = @(); $trace = @()
foreach ($file in $files) {
    $hits = @($map.rules | Where-Object { $file -match $_.pattern })
    if ($hits.Count -eq 0 -and $file -match '^(Frontend|Backend)/') { $fullReasons += "unmapped production file $file" }
    foreach ($rule in $hits) {
        if ($rule.full) { $fullReasons += "$file ($($rule.reason))" }
        $d = @(Expand $rule.direct); $p = @(Expand $rule.dependents)
        $direct += $d; $dependents += $p
        if ($rule.manual) { $manual += "${file}: $($rule.manual)" }
        $trace += [pscustomobject]@{ file = $file; rule = $rule.pattern; direct = $d; dependents = $p }
    }
}

$critical = @(Expand $map.critical)
$runFull = $Full -or $fullReasons.Count -gt 0
$selected = if ($runFull) { @() } else { @($direct + $dependents + $critical | Sort-Object -Unique) }
$Tests = @($Tests | ForEach-Object { $_ -split ',' } | ForEach-Object { $_.Trim() } | Where-Object { $_ })
if ($Tests.Count -gt 0) {
    $runFull = $false
    $fullReasons = @("explicit -Tests: $($Tests -join ', ')")
    $selected = $Tests
}

[pscustomobject]@{
    runId = $RunId; baseline = $Baseline; head = $head; changedFiles = $files; full = [bool]$runFull; fullReasons = $fullReasons
    direct = @($direct | Sort-Object -Unique); dependents = @($dependents | Sort-Object -Unique); critical = $critical
    selected = $selected; manualFollowUp = $manual; trace = $trace; include = $Include
} | ConvertTo-Json -Depth 5 | Set-Content -Encoding UTF8 (Join-Path $artifacts 'selection.json')

$scope = if ($runFull) { "FULL suite ($($fullReasons -join '; '))" } elseif ($Tests.Count -gt 0) { "explicit tests: $($Tests -join ', ')" } else { "$($selected.Count) test ID(s) selected" }
Write-Host "Baseline $Baseline -> HEAD $head; $($files.Count) changed file(s); $scope"
if ($SelectOnly) { return }

# ---- 5. Excel reports (always runs; never changes the outcome) ---------------------------------------------------
function Update-ExcelReports {
    $ErrorActionPreference = 'Continue'   # a reporting problem must not stop the script or hide the test result
    try {
        [pscustomobject]@{ runId = $RunId; head = $head; finished = (Get-Date -Format 'yyyy-MM-ddTHH:mm:ss'); runnerExitCode = $script:exit } |
            ConvertTo-Json | Set-Content -Encoding UTF8 (Join-Path $artifacts 'run-info.json')
        $generator = Join-Path $repo 'qa\excel-reports\generate_reports.py'
        $python = @('py', 'python', 'python3') | Where-Object { Get-Command $_ -ErrorAction SilentlyContinue } | Select-Object -First 1
        if (-not $python) { Write-Warning 'Python 3 was not found: the Excel reports were not updated.'; return }
        $pythonArgs = @(if ($python -eq 'py') { '-3' }) + @($generator, '--run', $RunId, '--copy-to', $artifacts)
        & $python @pythonArgs 2>&1 | ForEach-Object { Write-Host $_ }
        if ($LASTEXITCODE -ne 0) { Write-Warning "Excel report generation ended with exit code $LASTEXITCODE (see the message above)." }
    } catch {
        Write-Warning "Excel reports were not updated: $($_.Exception.Message)"
    }
}

$script:exit = 1
try {
    # ---- 3. Isolated build ----------------------------------------------------------------------------------------
    $work = Join-Path $WorkRoot $RunId
    # Observed (Phase 7): SQLite's native e_sqlite3.dll fails to load once its full path reaches about 252 characters,
    # even with Windows long paths enabled, and every SQLite test then fails with TypeInitializationException.
    $nativePath = Join-Path $work 'Tests\CT007.Tests\bin\Debug\net10.0-windows10.0.19041.0\runtimes\win-x64\native\e_sqlite3.dll'
    if ($nativePath.Length -gt 245) {
        throw "WorkRoot is too deep: the test build's native SQLite path would be $($nativePath.Length) characters (limit 245). Use a shorter -WorkRoot."
    }
    if (Test-Path $work) { Remove-Item -Recurse -Force $work }
    foreach ($dir in 'Frontend', 'Backend', 'Tests') {
        robocopy (Join-Path $repo $dir) (Join-Path $work $dir) /E /XD bin obj /NFL /NDL /NJH /NJS /NP | Out-Null
        if ($LASTEXITCODE -ge 8) { throw "copy of $dir failed ($LASTEXITCODE)" }
    }
    Copy-Item (Join-Path $repo 'CT007.slnx') $work
    $env:NUGET_PACKAGES = Join-Path $WorkRoot 'nuget'
    $env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
    $project = Join-Path $work 'Tests\CT007.Tests\CT007.Tests.csproj'
    $buildLog = Join-Path $artifacts 'build.log'
    & dotnet restore $project --source $PackageSource 2>&1 | Out-File -Encoding utf8 $buildLog
    & dotnet build $project --no-restore -v q $(if ($WarnAsError) { '-warnaserror' }) 2>&1 | Out-File -Encoding utf8 -Append $buildLog
    if ($LASTEXITCODE -ne 0) { throw "test build failed: see $buildLog" }

    # ---- 4. Run ---------------------------------------------------------------------------------------------------
    $exe = Join-Path $work 'Tests\CT007.Tests\bin\Debug\net10.0-windows10.0.19041.0\CT007.Tests.exe'
    $runArgs = @($selected) + @("--include=$($Include -join ',')", "--results=$(Join-Path $artifacts 'results.csv')")
    & $exe @runArgs 2>&1 | Out-File -Encoding utf8 (Join-Path $artifacts 'console.log')
    $script:exit = $LASTEXITCODE
    Get-Content (Join-Path $artifacts 'console.log') -Tail 1
} finally {
    Update-ExcelReports
}
Write-Host "Artifacts: $artifacts (runner exit code $script:exit)"
exit $script:exit
