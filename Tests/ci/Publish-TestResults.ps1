<#
.SYNOPSIS
  Turns a CT007.Tests results.csv into JUnit XML and a Markdown summary, and decides the CI outcome.

.DESCRIPTION
  Writes junit.xml and summary.md into the artifacts folder and appends the summary to $env:GITHUB_STEP_SUMMARY
  when it is set. GitHub annotations (::error / ::warning / ::notice) are written to stdout.
  Exit code 1 (fails the check) when:
    - results.csv is missing (the build or setup failed), or
    - a test failed that is neither listed in known-failures.json nor informational (-InformationalPrefixes).
  Skipped tests are reported as skipped, never as passed.
  Works in Windows PowerShell 5.1 and PowerShell 7.
#>
param(
    [Parameter(Mandatory = $true)][string]$Artifacts,
    [string]$KnownFailures,
    [string[]]$InformationalPrefixes = @()
)

$ErrorActionPreference = 'Stop'
if (-not $KnownFailures) { $KnownFailures = Join-Path $PSScriptRoot 'known-failures.json' }
$resultsPath = Join-Path $Artifacts 'results.csv'
$summaryPath = Join-Path $Artifacts 'summary.md'
$selectionPath = Join-Path $Artifacts 'selection.json'

$utf8 = New-Object System.Text.UTF8Encoding($false)   # no BOM: it would show up in the GitHub job summary

function Publish([string]$markdown) {
    [IO.File]::WriteAllText($summaryPath, $markdown, $utf8)
    if ($env:GITHUB_STEP_SUMMARY) { [IO.File]::AppendAllText($env:GITHUB_STEP_SUMMARY, $markdown, $utf8) }
}

function Short([string]$text, [int]$max = 300) {
    $line = ($text -replace '\s+', ' ').Trim()
    if ($line.Length -gt $max) { $line.Substring(0, $max) + '...' } else { $line }
}

$scope = 'unknown'
if (Test-Path $selectionPath) {
    $selection = Get-Content $selectionPath -Raw | ConvertFrom-Json
    $scope = if ($selection.full) { "full suite ($(($selection.fullReasons | Select-Object -First 3) -join '; '))" } else { "$($selection.selected.Count) selected test ID(s) for $($selection.changedFiles.Count) changed file(s)" }
}

if (-not (Test-Path $resultsPath)) {
    Write-Output "::error title=CT007 tests::No test results were produced. The test build or setup failed; see build.log in the artifacts."
    Publish "## CT007 tests: setup failed`n`nNo results.csv was produced (scope: $scope). See ``build.log`` in the uploaded artifacts.`n"
    exit 1
}

$rows = @(Import-Csv $resultsPath)
$known = @{}
if (Test-Path $KnownFailures) {
    foreach ($entry in (Get-Content $KnownFailures -Raw | ConvertFrom-Json).failures) { $known[$entry.test] = $entry }
}

$passed = @($rows | Where-Object outcome -eq 'PASS')
$failed = @($rows | Where-Object outcome -eq 'FAIL')
$skipped = @($rows | Where-Object outcome -eq 'SKIP')
$expected = @($failed | Where-Object { $known.ContainsKey($_.test) })
$informational = @($failed | Where-Object { -not $known.ContainsKey($_.test) } |
    Where-Object { $id = $_.id; @($InformationalPrefixes | Where-Object { $id.StartsWith($_) }).Count -gt 0 })
$blocking = @($failed | Where-Object { -not $known.ContainsKey($_.test) -and $informational -notcontains $_ })
$resolved = @($passed | Where-Object { $known.ContainsKey($_.test) })

# ---- JUnit XML ---------------------------------------------------------------------------------------------------
$esc = { param($s) [System.Security.SecurityElement]::Escape([string]$s) }
$totalSeconds = ($rows | ForEach-Object { [double]$_.ms } | Measure-Object -Sum).Sum / 1000
$xml = New-Object System.Text.StringBuilder
[void]$xml.AppendLine('<?xml version="1.0" encoding="utf-8"?>')
[void]$xml.AppendLine(('<testsuites><testsuite name="CT007.Tests" tests="{0}" failures="{1}" skipped="{2}" time="{3}">' -f
    $rows.Count, $failed.Count, $skipped.Count, $totalSeconds.ToString([Globalization.CultureInfo]::InvariantCulture)))
foreach ($row in $rows) {
    $time = ([double]$row.ms / 1000).ToString([Globalization.CultureInfo]::InvariantCulture)
    [void]$xml.Append(('<testcase classname="{0}" name="{1} {2}" time="{3}">' -f (& $esc ($row.id -split '-')[0]), (& $esc $row.id), (& $esc $row.test), $time))
    switch ($row.outcome) {
        'FAIL' { [void]$xml.Append(('<failure message="{0}" />' -f (& $esc (Short $row.message 1000)))) }
        'SKIP' { [void]$xml.Append(('<skipped message="{0}" />' -f (& $esc (Short $row.message 1000)))) }
    }
    [void]$xml.AppendLine('</testcase>')
}
[void]$xml.AppendLine('</testsuite></testsuites>')
[IO.File]::WriteAllText((Join-Path $Artifacts 'junit.xml'), $xml.ToString(), $utf8)

# ---- Annotations and summary ------------------------------------------------------------------------------------
foreach ($row in $blocking) { Write-Output "::error title=$($row.id) $($row.test)::$(Short $row.message)" }
foreach ($row in $expected) { Write-Output "::warning title=$($row.id) known defect $($known[$row.test].defect)::$(Short $row.message)" }
foreach ($row in $informational) { Write-Output "::warning title=$($row.id) informational::$(Short $row.message)" }
foreach ($row in $resolved) { Write-Output "::notice title=$($row.id) now passes::Known defect $($known[$row.test].defect) no longer fails: remove it from Tests/ci/known-failures.json." }

$status = if ($blocking.Count -gt 0) { 'FAILED' } else { 'passed' }
$md = New-Object System.Text.StringBuilder
[void]$md.AppendLine("## CT007 tests: $status")
[void]$md.AppendLine('')
[void]$md.AppendLine("Scope: $scope")
[void]$md.AppendLine('')
[void]$md.AppendLine('| Passed | Failed (blocking) | Known defects | Informational | Skipped |')
[void]$md.AppendLine('|---|---|---|---|---|')
[void]$md.AppendLine("| $($passed.Count) | $($blocking.Count) | $($expected.Count) | $($informational.Count) | $($skipped.Count) |")

if ($failed.Count -gt 0) {
    [void]$md.AppendLine('')
    [void]$md.AppendLine('| Test | Classification | Message |')
    [void]$md.AppendLine('|---|---|---|')
    foreach ($row in $failed) {
        $class = if ($known.ContainsKey($row.test)) { "known defect $($known[$row.test].defect)" } elseif ($informational -contains $row) { 'informational' } else { '**blocking**' }
        [void]$md.AppendLine("| $($row.id) ``$($row.test)`` | $class | $((Short $row.message 200) -replace '\|', '\|') |")
    }
}

if ($skipped.Count -gt 0) {
    $reasons = $skipped | Group-Object { Short $_.message 120 } | ForEach-Object { "$($_.Count)x $($_.Name)" }
    [void]$md.AppendLine('')
    [void]$md.AppendLine("Skipped (not passed): $($reasons -join '; ')")
}

if ($resolved.Count -gt 0) {
    [void]$md.AppendLine('')
    [void]$md.AppendLine("Known defects now passing (update known-failures.json): $(($resolved | ForEach-Object id) -join ', ')")
}

Publish $md.ToString()
if ($blocking.Count -gt 0) { exit 1 }
exit 0
