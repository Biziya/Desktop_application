"""
Builds Tests/mutation/mutants.json: controlled single-site mutations of high-risk production logic (qa/planning/test-plan.md
risk priorities). Each entry names a file, an exact source fragment, its replacement and the occurrence to change
(1-based). The script refuses fragments that do not occur exactly as specified, so the list stays valid as code moves.
Run from the repository root: python Tests/mutation/generate_mutants.py
"""
import json, os, sys

F = {
    "depth": "Frontend/Calculation/Measurement/Depth.cs",
    "samples": "Frontend/Calculation/Measurement/DeviceSamples.cs",
    "capture": "Frontend/Calculation/Measurement/DeviceCaptureService.cs",
    "order": "Frontend/Calculation/Measurement/MeasurementOrder.cs",
    "duration": "Frontend/Calculation/Measurement/CaptureDurationSettings.cs",
    "pick": "Frontend/Calculation/Core-Information/GammaGuardDatabase/GammaGuardCaptureDevices.cs",
    "ggcheck": "Frontend/Calculation/Core-Information/GammaGuardDatabase/GammaGuardDatabaseCheck.cs",
    "filecheck": "Frontend/Calculation/Core-Information/SqliteFileCheck.cs",
    "bt": "Frontend/Calculation/Core-Information/BluetoothDevice/Ct007BluetoothCheck.cs",
    "repo": "Frontend/Pages/Measurement/MeasurementRepository.cs",
    "csv": "Frontend/Pages/Measurement/CsvExportService.cs",
    "vm": "Frontend/Pages/Measurement/MeasurementViewModel.cs",
    "result": "Frontend/Pages/Measurement/MeasurementResult.cs",
    "cell": "Frontend/Pages/Measurement/MeasurementCellFormat.cs",
    "api": "Frontend/Api/ApiClient.cs",
    "apisettings": "Frontend/Api/ApiSettings.cs",
    "conn": "Frontend/Api/BackendConnection.cs",
    "catalog": "Frontend/Pages/CoreInformation/DrillholeCatalog.cs",
    "core": "Frontend/Pages/CoreInformation/CoreInformationViewModel.cs",
    "announce": "Frontend/ViewModels/StatusAnnouncements.cs",
    "toast": "Frontend/Shared/Infrastructure/ToastService.cs",
    "controller": "Backend/Controllers/CoreInformation/Drillhole/DrillholeController.cs",
    "sqlrepo": "Backend/Repositories/CoreInformation/Drillhole/DrillholeRepository.cs",
}

# (id, file key, kind, find, replace, occurrence, behaviour expected to change)
M = [
    ("M01", "depth", "relational", "out var metres) || metres < 0)", "out var metres) || metres < -1)", 1, "negative starting depth accepted"),
    ("M02", "depth", "arithmetic", "var mm = metres * 1000m;", "var mm = metres * 100m;", 1, "metres converted with the wrong factor"),
    ("M03", "depth", "remove condition", "if (mm != decimal.Truncate(mm) || mm > long.MaxValue / 2)", "if (mm > long.MaxValue / 2)", 1, "sub-millimetre depth accepted"),
    ("M04", "depth", "boundary", "centimetres) || centimetres < 0)", "centimetres) || centimetres <= 0)", 1, "parser rejects a 0 cm step"),
    ("M05", "depth", "arithmetic", "var mm = centimetres * 10m;", "var mm = centimetres * 100m;", 1, "step converted with the wrong factor"),
    ("M06", "depth", "format", "(millimetres / 10m).ToString(\"0.#\"", "(millimetres / 10m).ToString(\"0\"", 1, "step shown without its decimal"),
    ("M07", "samples", "boundary", "tsMs > TriggerMs && tsMs <= EndMs", "tsMs >= TriggerMs && tsMs <= EndMs", 1, "sample at the click itself accepted"),
    ("M08", "samples", "boundary", "tsMs > TriggerMs && tsMs <= EndMs", "tsMs > TriggerMs && tsMs < EndMs", 1, "sample at the window end rejected"),
    ("M09", "samples", "constant", "Math.Max(1, durationMs / 1000)", "Math.Max(0, durationMs / 1000)", 1, "expected samples can be 0"),
    ("M10", "samples", "remove condition", "!string.Equals(row.Serial, serial, StringComparison.Ordinal) ||", "false ||", 1, "samples of another detector accepted"),
    ("M11", "samples", "remove condition", "row.Seq is { } seq && !seenReadings.Add(", "row.Seq is { } seq && false && !seenReadings.Add(", 1, "duplicate readings accepted"),
    ("M12", "samples", "predicate", "double.IsFinite(x)", "!double.IsNaN(x)", 1, "infinite values averaged"),
    ("M13", "samples", "relational", "sessions.Count == 1 ? sessions[0] : null", "sessions.Count >= 1 ? sessions[0] : null", 1, "mixed device sessions reported as one"),
    ("M14", "samples", "ordering", ".OrderBy(s => s.TsMs).ThenBy(s => s.SourceId).FirstOrDefault()?.TsMs", ".OrderByDescending(s => s.TsMs).ThenBy(s => s.SourceId).FirstOrDefault()?.TsMs", 1, "last instead of first ts_ms"),
    ("M15", "samples", "constant", "latestTsMs < 100_000_000_000", "latestTsMs < 10_000_000_000", 1, "some non-millisecond time bases accepted"),
    ("M16", "samples", "boundary", "> (long)MaxClockDifference.TotalMilliseconds", ">= (long)MaxClockDifference.TotalMilliseconds", 1, "exactly 12 h difference refused"),
    ("M17", "pick", "boundary", "<= (long)SimultaneousWindow.TotalMilliseconds", "< (long)SimultaneousWindow.TotalMilliseconds", 1, "detectors exactly 5 s apart not refused"),
    ("M18", "pick", "ordering", "recent.OrderByDescending(r => r.LastTsMs)", "recent.OrderBy(r => r.LastTsMs)", 1, "oldest detector picked"),
    ("M19", "pick", "condition", "State: LocalState.Available", "State: not LocalState.Checking", 1, "capture allowed from a missing/busy database"),
    ("M20", "capture", "remove lookback", "Math.Max(0, baseline - LookbackRows)", "Math.Max(0, baseline)", 1, "rows written just before the click missed"),
    ("M21", "capture", "remove branch", "window.Count < window.ExpectedSamples && clock.NowMs < finalDeadline", "false && clock.NowMs < finalDeadline", 1, "no final read for late rows"),
    ("M22", "capture", "remove condition", "DeviceTime.Problem(latest, clock.NowMs) is { } problem)", "DeviceTime.Problem(latest, clock.NowMs) is { } problem && false)", 1, "time-basis check skipped"),
    ("M23", "order", "return value", "(null, _) => 1,", "(null, _) => -1,", 1, "rows without depth sorted first"),
    ("M24", "order", "negation", "if (byTime != 0) return byTime;", "if (byTime != 0) return -byTime;", 1, "equal depths ordered newest first"),
    ("M25", "order", "boundary", "Compare(sorted[middle], row) <= 0)", "Compare(sorted[middle], row) < 0)", 1, "insert position among equal rows"),
    ("M26", "duration", "boundary", "seconds is >= MinSeconds and <= MaxSeconds", "seconds is >= MinSeconds and < MaxSeconds", 1, "10 s duration rejected"),
    ("M27", "duration", "validation", "NumberStyles.None", "NumberStyles.Integer", 1, "signed/whitespace durations accepted"),
    ("M28", "repo", "remove validation", "if (row.SessionId != session.Id)", "if (false)", 1, "row of another session saved"),
    ("M29", "repo", "update scope", "DO UPDATE SET SortOrder = excluded.SortOrder", "DO UPDATE SET SortOrder = excluded.SortOrder, Count0 = excluded.Count0", 1, "stored values overwritten on re-save"),
    ("M30", "repo", "boundary", "if (version > SchemaVersion)", "if (version > SchemaVersion + 1)", 1, "newer schema accepted"),
    ("M31", "repo", "rounding", "(long)Math.Round(centimetres * 10, MidpointRounding.AwayFromZero)", "(long)(centimetres * 10)", 1, "cm -> mm truncated instead of rounded"),
    ("M32", "repo", "arithmetic", "progress.NextFromMm / 10.0", "progress.NextFromMm / 100.0", 1, "progress stored in the wrong unit"),
    ("M33", "repo", "conflict rule", "ON CONFLICT (DrillholeID) DO NOTHING;", "ON CONFLICT (DrillholeID) DO UPDATE SET \"operator\" = excluded.\"operator\";", 1, "stored session metadata overwritten"),
    ("M34", "repo", "remove check", "if (orphans.Read())", "if (false && orphans.Read())", 1, "orphan records accepted by migration"),
    ("M35", "repo", "boundary", "if (duplicates.Count > 0)", "if (duplicates.Count > 1)", 1, "single duplicate Drillhole ID accepted"),
    ("M36", "repo", "remove statement", "DELETE FROM CaptureSamples WHERE record_id IN", "DELETE FROM CaptureSamples WHERE 0 AND record_id IN", 1, "samples not deleted with their record"),
    ("M37", "csv", "remove case", "trimmed[0] is '=' or '+' or '-' or '@'", "trimmed[0] is '=' or '+' or '@'", 1, "'-' formula prefix not neutralised"),
    ("M38", "csv", "remove trim", "value.TrimStart() is { Length: > 0 } trimmed", "value is { Length: > 0 } trimmed", 1, "formula after leading spaces not neutralised"),
    ("M39", "csv", "remove escaping", "value.Replace(\"\\\"\", \"\\\"\\\"\")", "value", 1, "embedded quotes not doubled"),
    ("M40", "csv", "remove validation", "rows.Any(r => r.SessionId != session.Id)", "rows.Any(r => false)", 1, "mixed-session export allowed"),
    ("M41", "csv", "culture", "number.ToString(\"R\", CultureInfo.InvariantCulture)", "number.ToString(CultureInfo.CurrentCulture)", 1, "numbers exported in the local culture"),
    ("M42", "vm", "condition", "if (nextFromMm == fromMm)", "if (true)", 1, "depth advances although edited during capture"),
    ("M43", "vm", "remove validation", "&& size > 0 ? size : null", "? size : null", 1, "0 cm step accepted"),
    ("M44", "vm", "remove condition", "activeCapture is null && !isEditingDuration;", "activeCapture is null;", 1, "capture while the duration editor is open"),
    ("M45", "vm", "negation", "if (row.IsIncomplete)", "if (!row.IsIncomplete)", 1, "incomplete warning on complete captures"),
    ("M46", "vm", "remove condition", "if (disposed || savedCore != core || version != viewVersion)", "if (disposed || savedCore != core)", 1, "late session open not discarded"),
    ("M47", "vm", "remove condition", "if (!disposed && version == viewVersion)", "if (!disposed)", 1, "late stored-drillhole load shown"),
    ("M48", "vm", "constant", "hasUnsavedRows = !saved;", "hasUnsavedRows = false;", 1, "failed save not retried"),
    ("M49", "result", "boundary", "SampleCount < expected", "SampleCount <= expected", 1, "complete capture shown as incomplete"),
    ("M50", "cell", "rounding", "Math.Round(d, 2, MidpointRounding.AwayFromZero)", "Math.Round(d, 2, MidpointRounding.ToEven)", 1, "display rounding mode"),
    ("M51", "cell", "remove clean-up", "text == \"-0\" ? \"0\" : text", "text", 1, "'-0' shown"),
    ("M52", "api", "remove branch", "if (!response.IsSuccessStatusCode)", "if (false)", 1, "HTTP errors parsed as data"),
    ("M53", "api", "swap messages", "error is TaskCanceledException ? \"Backend did not respond in time.\" : \"Backend is offline.\"", "error is TaskCanceledException ? \"Backend is offline.\" : \"Backend did not respond in time.\"", 1, "timeout and refusal messages swapped"),
    ("M54", "apisettings", "boundary", "value.TryGetDouble(out var seconds) && seconds > 0", "value.TryGetDouble(out var seconds) && seconds >= 0", 1, "0 s timeout accepted"),
    ("M55", "conn", "condition", "if (reachable && !wasOnline)", "if (reachable)", 1, "CameOnline raised on every successful probe"),
    ("M56", "conn", "remove condition", "if (check is { IsCompleted: false })", "if (false)", 1, "overlapping probes"),
    ("M57", "catalog", "predicate", ".Where(d => !string.IsNullOrWhiteSpace(d.HoleId))", ".Where(d => d.HoleId is not null)", 1, "blank Hole IDs kept"),
    ("M58", "catalog", "comparer", ".DistinctBy(d => d.HoleId!, StringComparer.OrdinalIgnoreCase)", ".DistinctBy(d => d.HoleId!, StringComparer.Ordinal)", 1, "case-variant duplicates kept"),
    ("M59", "catalog", "constant", "StringComparison.OrdinalIgnoreCase) ? 0 :", "StringComparison.OrdinalIgnoreCase) ? 2 :", 1, "exact match ranked last"),
    ("M60", "catalog", "boundary", "if (rank < best)", "if (rank <= best)", 1, "closest match tie-break"),
    ("M61", "core", "condition", "if (fresh.Count > 0 || catalog.Count == 0)", "if (true)", 1, "empty server list replaces the saved list"),
    ("M62", "core", "condition", "if (drillholesStatus is not ApiStatus.Ok)", "if (true)", 1, "reload on every CameOnline"),
    ("M63", "core", "remove lock", "if (isSelectionLocked && !string.Equals(value, DrillholeId, StringComparison.OrdinalIgnoreCase))", "if (false)", 1, "typing changes the drillhole while measuring"),
    ("M64", "core", "predicate", "if (string.IsNullOrWhiteSpace(Operator))", "if (Operator.Length == 0)", 1, "blank Operator not autofilled"),
    ("M65", "core", "remove validation", "if (!HasRequiredText)", "if (false)", 1, "save without a drillhole"),
    ("M66", "filecheck", "remove case", "SqliteErrorCode is 5 or 6", "SqliteErrorCode is 6", 1, "busy database not reported as Busy"),
    ("M67", "ggcheck", "constant", "\"Database\", StringComparison.OrdinalIgnoreCase", "\"Logs\", StringComparison.OrdinalIgnoreCase", 1, "wrong subfolder searched"),
    ("M68", "ggcheck", "ordering", ".OrderByDescending(f => f.LastWriteTimeUtc)", ".OrderBy(f => f.LastWriteTimeUtc)", 1, "oldest database chosen"),
    ("M69", "ggcheck", "remove normalisation", "Path.GetFileName(directory).Replace(\" \", \"\")", "Path.GetFileName(directory)", 1, "'Gamma Guard' folder not recognised"),
    ("M70", "bt", "remove check", "if (device is not null && device.Address != selected.Address)", "if (false)", 1, "device with another address accepted"),
    ("M71", "announce", "negation", "previous == BackendState.Offline", "previous != BackendState.Offline", 1, "online announcement condition inverted"),
    ("M72", "announce", "remove condition", "if (!known || previous == status.State)", "if (previous == status.State)", 1, "first known state announced"),
    ("M73", "toast", "boundary", "while (Items.Count >= MaxVisible)", "while (Items.Count > MaxVisible)", 1, "five toasts visible"),
    ("M74", "toast", "constant", "kind == ToastKind.Error ? ErrorDuration : ShortDuration", "ShortDuration", 1, "errors shown for 3 s"),
    ("M75", "controller", "exception type", "catch (SqlException ex)", "catch (InvalidCastException ex)", 1, "SQL outage becomes HTTP 500"),
    ("M76", "controller", "constant", "statusCode: StatusCodes.Status503ServiceUnavailable", "statusCode: StatusCodes.Status500InternalServerError", 1, "outage status 500 instead of 503"),
    ("M77", "sqlrepo", "sql", "RIGHT(PROSPECT, 4)", "LEFT(PROSPECT, 4)", 1, "campaign taken from the wrong end"),
]

root = os.getcwd()
out, errors = [], []
for mid, key, kind, find, replace, occurrence, effect in M:
    path = F[key]
    text = open(os.path.join(root, path), encoding="utf-8-sig").read()
    count = text.count(find)
    if count < occurrence:
        errors.append(f"{mid}: fragment found {count} time(s) in {path}: {find}")
    out.append({"id": mid, "file": path, "kind": kind, "find": find, "replace": replace, "occurrence": occurrence,
                "occurrencesInFile": count, "effect": effect})

if errors:
    print("\n".join(errors)); sys.exit(1)

json.dump(out, open(os.path.join(root, "Tests", "mutation", "mutants.json"), "w", encoding="utf-8"), indent=1)
print(f"{len(out)} mutants written; fragments with several occurrences: {[m['id'] for m in out if m['occurrencesInFile'] > 1]}")
