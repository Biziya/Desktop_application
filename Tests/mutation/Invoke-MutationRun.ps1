<#
.SYNOPSIS
  Controlled mutation testing for CT007 without a mutation tool package: applies each mutant from mutants.json to a
  DISPOSABLE copy of the repository, builds, runs the tests CI would select for that file, and classifies the result.

.DESCRIPTION
  - Only the copy under -WorkRoot is ever modified. The original repository is read, never written (except the
    -Output folder). Each mutated file is restored from its original bytes and its hash re-checked after every mutant.
  - Test selection per mutant = direct + dependent IDs from Tests/regression/impact-map.json for the mutated file, plus
    the map's critical set (the same rule CI uses).
  - Classification against a baseline run of the unmutated copy:
      Killed       a test that passed at baseline fails, or a baseline failure now fails with a different message
      Survived     every selected test has its baseline outcome and message
      NoCoverage   no selected test executed (none selected, or all skipped)
      CompileError the mutant does not build
      Timeout      the test run exceeded -TimeoutSeconds
  - Writes baseline.csv, mutants/<id>.csv and mutation-results.json to -Output.
#>
param(
    [Parameter(Mandatory = $true)][string]$Output,
    [string]$WorkRoot = (Join-Path $env:TEMP 'ct007-mut'),
    [string]$PackageSource = (Join-Path $env:USERPROFILE '.nuget\packages'),
    [string[]]$Include = @(),
    [string[]]$Only = @(),
    [int]$TimeoutSeconds = 600,
    [switch]$KeepWorkspace
)

$ErrorActionPreference = 'Stop'
# "-Only M01,M02" arrives as one string when the script is started with -File: split it.
$Only = @($Only | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$Include = @($Include | ForEach-Object { $_ -split ',' } | Where-Object { $_ })
$repo = (Resolve-Path (Join-Path $PSScriptRoot '..\..')).Path
New-Item -ItemType Directory -Force (Join-Path $Output 'mutants') | Out-Null
$Output = (Resolve-Path $Output).Path
if ($WorkRoot.StartsWith($repo, [StringComparison]::OrdinalIgnoreCase)) { throw 'WorkRoot must be outside the repository.' }

# ---- Disposable workspace --------------------------------------------------------------------------------------
if (Test-Path $WorkRoot) { Remove-Item -Recurse -Force $WorkRoot }
foreach ($dir in 'Frontend', 'Backend', 'Tests') {
    robocopy (Join-Path $repo $dir) (Join-Path $WorkRoot $dir) /E /XD bin obj /NFL /NDL /NJH /NJS /NP | Out-Null
    if ($LASTEXITCODE -ge 8) { throw "copy of $dir failed" }
}
Copy-Item (Join-Path $repo 'CT007.slnx') $WorkRoot
$env:NUGET_PACKAGES = Join-Path $WorkRoot 'nuget'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$project = Join-Path $WorkRoot 'Tests\CT007.Tests\CT007.Tests.csproj'
$exe = Join-Path $WorkRoot 'Tests\CT007.Tests\bin\Debug\net10.0-windows10.0.19041.0\CT007.Tests.exe'
if ((Join-Path (Split-Path $exe) 'runtimes\win-x64\native\e_sqlite3.dll').Length -gt 245) { throw 'WorkRoot too deep for the SQLite native library (see Tests/README.md).' }

& dotnet restore $project --source $PackageSource 2>&1 | Out-File -Encoding utf8 (Join-Path $Output 'build-baseline.log')
& dotnet build $project --no-restore -v q 2>&1 | Out-File -Encoding utf8 -Append (Join-Path $Output 'build-baseline.log')
if ($LASTEXITCODE -ne 0) { throw 'baseline build failed' }

function Invoke-Tests([string[]]$ids, [string]$results) {
    $arguments = @($ids) + @("--results=$results")
    if ($Include.Count -gt 0) { $arguments += "--include=$($Include -join ',')" }
    $process = Start-Process -FilePath $exe -ArgumentList $arguments -PassThru -NoNewWindow `
        -RedirectStandardOutput "$results.out" -RedirectStandardError "$results.err"
    if (-not $process.WaitForExit($TimeoutSeconds * 1000)) { $process.Kill(); return 'timeout' }
    if (Test-Path $results) { 'done' } else { 'crash' }
}

function Read-Results([string]$path) {
    $map = @{}
    foreach ($row in (Import-Csv $path)) { $map[$row.test] = $row }
    $map
}

# ---- Baseline -----------------------------------------------------------------------------------------------
$baselinePath = Join-Path $Output 'baseline.csv'
if ((Invoke-Tests @() $baselinePath) -ne 'done') { throw 'baseline test run did not complete' }
$baseline = Read-Results $baselinePath
Remove-Item "$baselinePath.out", "$baselinePath.err" -ErrorAction SilentlyContinue

# ---- Selection rules (same as CI) ------------------------------------------------------------------------------
$map = Get-Content (Join-Path $repo 'Tests\regression\impact-map.json') -Raw | ConvertFrom-Json
function Expand([string[]]$ids) {
    foreach ($id in $ids) {
        if ($id -match '^([A-Z0-9]+)-(\d{3})\.\.\1-(\d{3})$') { [int]$Matches[2]..[int]$Matches[3] | ForEach-Object { '{0}-{1:D3}' -f $Matches[1], $_ } }
        else { $id }
    }
}
function Select-Tests([string]$file) {
    $ids = @(Expand $map.critical)
    foreach ($rule in $map.rules) {
        if ($file -match $rule.pattern) { $ids += @(Expand $rule.direct) + @(Expand $rule.dependents) }
    }
    @($ids | Sort-Object -Unique)
}

# ---- Mutants ----------------------------------------------------------------------------------------------------
$mutants = Get-Content (Join-Path $PSScriptRoot 'mutants.json') -Raw | ConvertFrom-Json
if ($Only.Count -gt 0) { $mutants = @($mutants | Where-Object { $Only -contains $_.id }) }
$utf8 = New-Object System.Text.UTF8Encoding($false)
$results = @()

foreach ($m in $mutants) {
    $target = Join-Path $WorkRoot $m.file
    $original = [IO.File]::ReadAllBytes($target)
    $originalHash = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash($original))
    $hasBom = $original.Length -ge 3 -and $original[0] -eq 0xEF -and $original[1] -eq 0xBB -and $original[2] -eq 0xBF
    $text = [IO.File]::ReadAllText($target)
    $index = -1
    for ($n = 0; $n -lt [int]$m.occurrence; $n++) { $index = $text.IndexOf($m.find, $index + 1, [StringComparison]::Ordinal); if ($index -lt 0) { break } }
    $selected = Select-Tests $m.file
    $record = [ordered]@{ id = $m.id; file = $m.file; kind = $m.kind; effect = $m.effect; selected = $selected.Count; status = ''; killedBy = @(); seconds = 0 }
    $watch = [Diagnostics.Stopwatch]::StartNew()

    try {
        if ($index -lt 0) { $record.status = 'NotApplied'; continue }
        $mutated = $text.Substring(0, $index) + $m.replace + $text.Substring($index + $m.find.Length)
        [IO.File]::WriteAllText($target, $mutated, (New-Object System.Text.UTF8Encoding($hasBom)))

        & dotnet build $project --no-restore -v q 2>&1 | Out-File -Encoding utf8 (Join-Path $Output "mutants\$($m.id).build.log")
        if ($LASTEXITCODE -ne 0) { $record.status = 'CompileError'; continue }
        Remove-Item (Join-Path $Output "mutants\$($m.id).build.log")

        $csv = Join-Path $Output "mutants\$($m.id).csv"
        $run = Invoke-Tests $selected $csv
        Remove-Item "$csv.err" -ErrorAction SilentlyContinue
        if ($run -eq 'timeout') { $record.status = 'Timeout'; continue }
        if ($run -eq 'crash') { $record.status = 'Killed'; $record.killedBy = @('test process crashed'); continue }
        Remove-Item "$csv.out" -ErrorAction SilentlyContinue

        $executed = 0
        foreach ($row in (Import-Csv $csv)) {
            if ($row.outcome -eq 'SKIP') { continue }
            $executed++
            $before = $baseline[$row.test]
            if (-not $before) { continue }
            if (($before.outcome -eq 'PASS' -and $row.outcome -eq 'FAIL') -or
                ($before.outcome -eq 'FAIL' -and $row.outcome -eq 'FAIL' -and $before.message -ne $row.message)) {
                $record.killedBy += "$($row.id) $($row.test)"
            }
        }

        $record.status = if ($executed -eq 0) { 'NoCoverage' } elseif ($record.killedBy.Count -gt 0) { 'Killed' } else { 'Survived' }
    }
    finally {
        [IO.File]::WriteAllBytes($target, $original)
        $restoredHash = [BitConverter]::ToString([Security.Cryptography.SHA256]::Create().ComputeHash([IO.File]::ReadAllBytes($target)))
        if ($restoredHash -ne $originalHash) { throw "restore of $($m.file) failed after $($m.id)" }
        $record.seconds = [math]::Round($watch.Elapsed.TotalSeconds)
        $results += [pscustomobject]$record
        [IO.File]::WriteAllText((Join-Path $Output 'mutation-results.json'), ($results | ConvertTo-Json -Depth 4), $utf8)
        Write-Output ("{0} {1,-12} {2,4}s {3}" -f $m.id, $record.status, $record.seconds, (($record.killedBy | Select-Object -First 2) -join '; '))
    }
}

# Final rebuild of the unmutated copy, then discard the workspace (it contains no user changes: it is a copy).
& dotnet build $project --no-restore -v q 2>&1 | Out-Null
if (-not $KeepWorkspace) { Remove-Item -Recurse -Force $WorkRoot }
$results | Group-Object status | ForEach-Object { "{0}: {1}" -f $_.Name, $_.Count }
