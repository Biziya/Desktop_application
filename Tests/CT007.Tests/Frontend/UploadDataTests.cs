using System.Security.Cryptography;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Frontend.Api.Measurements;
using Frontend.Pages.Measurement.CapturePanel;
using Frontend.Pages.UploadData;

namespace Ct007.Tests.FrontendTests;

/// <summary>
/// FE-060…FE-071: CSV import (MeasurementCsvReader), the Upload Data page (UploadDataViewModel), the CPS threshold alarm,
/// the CPS chart and the Measurement page layout changes.
/// </summary>
public static class UploadDataTests
{
    private static readonly MeasurementSession Session = new(Guid.Parse("44444444-4444-4444-4444-444444444444"),
        DateTimeOffset.UnixEpoch, "DH-001", "North Ridge", "2024", "CORP\\ann", "Hole collapsed at 12 m; re-logged");

    // Three exported rows, deliberately not in depth order.
    private static string ExportedCsv(MeasurementSession? session = null, string createdUsername = "CORP\\creator")
    {
        var s = session ?? Session;
        var rows = new[]
        {
            Make.Row(s.Id, 1, 10_500, reading: new DeviceReading(1_760_600_000_001, 125.5, 126, 10.5, 10.75, 0.12)),
            Make.Row(s.Id, 2, 10_000, reading: new DeviceReading(1_760_600_000_002, 175, null, null, null, null)),
            Make.Row(s.Id, 3, 11_000, reading: new DeviceReading(null, 1e-300, 0, 0, 0, 0))
        };
        return CsvExportService.Serialize(rows, s, createdUsername);
    }

    private static string Rejected(string csv)
    {
        try
        {
            MeasurementCsvReader.Read(csv);
        }
        catch (CsvImportException error)
        {
            return error.Message;
        }

        throw new AssertionException("expected the CSV to be rejected");
    }

    [Test("FE-060", "CSV import: an exported file reads back with its metadata, timestamps, depths and values, in table order")]
    public static void ImportRoundTrip()
    {
        var imported = MeasurementCsvReader.Read(ExportedCsv());

        Assert.Equal(new ImportedMetadata(Session.Id, "DH-001", "North Ridge", "2024", "CORP\\ann", "Hole collapsed at 12 m; re-logged", "CORP\\creator"),
            imported.Metadata, "metadata");
        Assert.Sequence(new[] { 2, 1, 3 }, imported.Rows.Select(r => r.CaptureNumber), "sorted by From like the Measurement page");
        Assert.Sequence(new[] { 1, 2, 3 }, imported.Rows.Select(r => r.Number), "numbered in display order");

        var first = imported.Rows[1];
        var original = Make.Row(Session.Id, 1, 10_500);
        Assert.Equal(original.StartTime, first.StartTime, "start time, offset and sub-second precision preserved");
        Assert.Equal(original.StartTime.Offset, first.StartTime.Offset, "start time offset");
        Assert.Equal(original.EndTime, first.EndTime, "end time");
        Assert.Equal(10_500L, first.FromMm, "from");
        Assert.Equal(10_750L, first.ToMm, "to");
        Assert.Equal(250L, first.StepMm, "step");
        Assert.Equal(new DeviceReading(1_760_600_000_001, 125.5, 126, 10.5, 10.75, 0.12), first.Reading, "device values");
        Assert.Equal(5, first.SampleCount, "sample count");
        Assert.Equal(Session.Id, first.SessionId, "session");
        Assert.Equal(new DeviceReading(null, 1e-300, 0, 0, 0, 0), imported.Rows[2].Reading, "empty ts_ms stays null; tiny values round-trip");
        Assert.True(imported.Rows[0].Reading.Count0Raw is null, "empty device value stays null (never 0)");

        // Text the exporter protected against spreadsheet formulas comes back as typed.
        var formula = Session with { Comment = "=1+1", Operator = "-ann" };
        var back = MeasurementCsvReader.Read(ExportedCsv(formula)).Metadata;
        Assert.Equal("=1+1", back.Notes, "formula-looking Notes");
        Assert.Equal("-ann", back.Operator, "formula-looking Operator");
    }

    [Test("FE-061", "CSV import: empty file, header only and missing required columns are rejected with a clear message")]
    public static void ImportStructureErrors()
    {
        Assert.Equal("The file is empty.", Rejected(""), "empty");
        Assert.Equal("The file is empty.", Rejected("﻿\r\n\r\n"), "BOM and blank lines only");
        Assert.Equal("The file contains no measurements.", Rejected(CsvExportService.Serialize([], Session)), "header only");

        var header = ExportedCsv().Split("\r\n")[0];
        var withoutStart = ExportedCsv().Replace("\"Start Time\"", "\"Begin\"");
        Assert.Equal("This is not a measurement CSV: missing column Start Time.", Rejected(withoutStart), "one missing column");
        Assert.Contains("missing columns Capture No, Start Time, End Time, Sample, session_id", Rejected("a,b\r\n1,2\r\n"), "unrelated CSV");
        Assert.Equal("The column 'count0' appears more than once.", Rejected(header.Replace("\"hdm\"", "\"count0\"") + "\r\n"), "duplicate header");
    }

    [Test("FE-062", "CSV import: invalid numbers, times and depths name the row; inconsistent metadata and duplicates are rejected")]
    public static void ImportValueErrors()
    {
        var csv = ExportedCsv();
        var lines = csv.Split("\r\n");
        var header = lines[0].Split(',');

        // Row 2 of the file (capture 2) with one cell replaced.
        string WithCell(string column, string value)
        {
            var cells = lines[2].Split(',');
            cells[Array.IndexOf(header, $"\"{column}\"")] = value;
            return string.Join("\r\n", lines.Take(2).Append(string.Join(",", cells)).Concat(lines.Skip(3)));
        }

        Assert.Equal("Row 2 (line 3): count0 'abc' is not a number.", Rejected(WithCell("count0", "\"abc\"")), "count0");
        Assert.Equal("Row 2 (line 3): count0 'NaN' is not a number.", Rejected(WithCell("count0", "NaN")), "NaN");
        Assert.Equal("Row 2 (line 3): Sample '5.5' is not a whole number.", Rejected(WithCell("Sample", "5.5")), "sample");
        Assert.Equal("Row 2 (line 3): Sample is empty.", Rejected(WithCell("Sample", "")), "required number empty");
        Assert.Equal("Row 2 (line 3): from_m '10.0001' is not a depth in metres (not negative, at most 3 decimals).",
            Rejected(WithCell("from_m", "10.0001")), "depth precision");
        Assert.Equal("Row 2 (line 3): from_m '-1' is not a depth in metres (not negative, at most 3 decimals).", Rejected(WithCell("from_m", "-1")), "negative depth");
        Assert.Equal("Row 2 (line 3): to_m must be greater than from_m.", Rejected(WithCell("to_m", "10")), "empty interval");
        Assert.Equal("Row 2 (line 3): Start Time 'yesterday' is not a date and time.", Rejected(WithCell("Start Time", "yesterday")), "time");
        Assert.Equal("Row 2 (line 3): End Time is before Start Time.", Rejected(WithCell("End Time", "2020-01-01T00:00:00+00:00")), "time order");
        Assert.Equal("Row 2 (line 3): Capture No must be greater than 0.", Rejected(WithCell("Capture No", "0")), "capture number");
        Assert.Equal("Row 2 (line 3): Capture No 1 appears more than once.", Rejected(WithCell("Capture No", "1")), "duplicate capture");
        Assert.Contains("Row 2 (line 3): session_id 'x' is not a session ID.", Rejected(WithCell("session_id", "x")), "session id");
        Assert.Contains("Row 2 (line 3): its session or Drillhole", Rejected(WithCell("Drillhole", "\"DH-999\"")), "metadata differs");
        Assert.Contains("Row 2 (line 3): its session or Drillhole", Rejected(WithCell("session_id", Guid.NewGuid().ToString())), "two sessions");
        Assert.Equal("Row 2 (line 3): expected 21 values, found 20.",
            Rejected(string.Join("\r\n", lines.Take(2).Append(lines[2][..lines[2].LastIndexOf(',')]).Concat(lines.Skip(3)))), "missing cell");
    }

    [Test("FE-063", "CSV import: quoted commas, quotes and line breaks, LF endings, unquoted cells, BOM, extra and reordered columns")]
    public static void ImportCsvSyntax()
    {
        var tricky = Session with { Comment = "a, \"quoted\"\r\nsecond line\nthird", Operator = "Ann, Bob" };
        var imported = MeasurementCsvReader.Read("﻿" + ExportedCsv(tricky));
        Assert.Equal("a, \"quoted\"\r\nsecond line\nthird", imported.Metadata.Notes, "Notes with comma, quotes and line breaks");
        Assert.Equal("Ann, Bob", imported.Metadata.Operator, "Operator with a comma");
        Assert.Equal(3, imported.Rows.Count, "rows after multi-line cells");

        // LF line endings, unquoted cells, columns in another order, an unknown extra column, no trailing newline.
        var session = Guid.NewGuid();
        var plain =
            "session_id,Sample,count0,End Time,Start Time,Capture No,to_m,from_m,Drillhole,Operator,Comment\n" +
            $"{session},5,150.25,2026-10-16T09:00:05+08:00,2026-10-16T09:00:00+08:00,7,1.5,1.25,DH-9,Ann,ignored\n" +
            $"{session},4,,2026-10-16T09:01:05+08:00,2026-10-16T09:01:00+08:00,8,1.25,1,DH-9,Ann,ignored";
        var rows = MeasurementCsvReader.Read(plain).Rows;
        Assert.Sequence(new[] { 8, 7 }, rows.Select(r => r.CaptureNumber), "sorted by From");
        Assert.Equal(150.25, rows[1].Reading.Count0, "unquoted number");
        Assert.True(rows[0].Reading.Count0 is null, "empty optional value");
        Assert.Equal(250L, rows[1].StepMm, "step from the interval when step_m is missing");

        Assert.Equal("Line 2: a quoted value is not closed.", Rejected("Capture No,Start Time\n\"1,2\n"), "unterminated quote");
        Assert.Equal("Line 1: unexpected text after a closing quote.", Rejected("\"a\"b,c\n"), "text after quote");
        Assert.Equal("Line 1: a quote inside an unquoted value.", Rejected("a\"b,c\n"), "stray quote");
    }

    [Test("FE-064", "Legacy CSV: older exports import (no depths, no Created Username → Not available) and are not uploaded")]
    public static async Task LegacyCsv()
    {
        // The export format before this change: no from_m / to_m / step_m and no Created Username.
        var current = ExportedCsv().Split("\r\n");
        var header = current[0].Split(',').ToList();
        var dropped = new[] { "\"from_m\"", "\"to_m\"", "\"step_m\"", "\"Created Username\"" }.Select(h => header.IndexOf(h)).ToHashSet();
        var legacy = string.Join("\r\n", current.Select(line => line.Length == 0 ? line
            : string.Join(",", line.Split(',').Where((_, i) => !dropped.Contains(i)))));

        var imported = MeasurementCsvReader.Read(legacy);
        Assert.True(imported.Metadata.CreatedUsername is null, "no Created Username column → null, not guessed");
        Assert.Equal("DH-001", imported.Metadata.Drillhole, "metadata still read");
        Assert.True(imported.Rows.All(r => r.FromMm is null && r.ToMm is null && r.StepMm is null), "no depths");
        Assert.Sequence(new[] { 1, 2, 3 }, imported.Rows.Select(r => r.CaptureNumber), "without depths: by Start Time");

        // The oldest export: measurement columns only.
        var oldest = MeasurementCsvReader.Read(
            "\"No\",\"Capture No\",\"Start Time\",\"End Time\",\"Sample\",\"ts_ms\",\"count0\",\"session_id\"\r\n" +
            $"\"1\",\"1\",\"2026-10-16T09:00:00+08:00\",\"2026-10-16T09:00:05+08:00\",\"5\",\"\",\"12\",\"{Guid.NewGuid()}\"\r\n");
        Assert.True(oldest.Metadata.Drillhole is null && oldest.Metadata.Operator is null, "no metadata columns → null");

        using var page = new UploadPage(legacy);
        await page.ImportAsync();
        Assert.Equal(UploadDataViewModel.NotAvailable, page.ViewModel.CreatedUsername, "Created Username shown as Not available");
        Assert.Equal(WindowsAccount.Current(), page.ViewModel.UploadingUsername, "Uploading Username is this Windows account");
        Assert.True(Make.ToastedStart(page.Toasts, ToastKind.Warning, "Imported, but some rows have no depth"), "warned about missing depths");

        page.ViewModel.UploadCommand.Execute(null);
        await Assert.Eventually(() => Make.ToastedStart(page.Toasts, ToastKind.Error, "Row 1 has no depth"), "upload refused");
        Assert.Equal(0, page.Backend.Requests.Count, "nothing sent");
        Assert.Equal(0, page.Dialogs.Asked, "not even asked to confirm");
    }

    [Test("FE-065", "Upload Data: import shows metadata and rows; delete asks, removes and renumbers; the CSV file is never changed")]
    public static async Task ImportAndDelete()
    {
        using var page = new UploadPage(ExportedCsv());
        var before = SHA256.HashData(File.ReadAllBytes(page.File));
        var vm = page.ViewModel;

        page.Dialogs.CsvFile = null;
        vm.ImportCommand.Execute(null);
        await Task.Delay(50);
        Assert.False(vm.HasImport, "cancelled file dialog: nothing imported");

        await page.ImportAsync();
        Assert.True(Make.Toasted(page.Toasts, ToastKind.Success, $"Imported 3 measurements from {Path.GetFileName(page.File)}."), "success toast");
        Assert.Equal("DH-001", vm.Drillhole, "Drillhole");
        Assert.Equal("North Ridge", vm.Project, "Project");
        Assert.Equal("2024", vm.Campaign, "Campaign");
        Assert.Equal("CORP\\ann", vm.Operator, "Operator");
        Assert.Equal("Hole collapsed at 12 m; re-logged", vm.Notes, "Notes");
        Assert.Equal("CORP\\creator", vm.CreatedUsername, "Created Username from the CSV, not replaced by the uploader");
        Assert.Equal("Total measurements: 3", vm.TotalText, "total");
        Assert.True(vm.UploadCommand.CanExecute(null), "upload available");

        var second = vm.Results[1];
        page.Dialogs.Confirm = false;
        vm.DeleteCommand.Execute(second);
        Assert.Equal(3, vm.Results.Count, "cancelled: row kept");

        page.Dialogs.Confirm = true;
        vm.DeleteCommand.Execute(second);
        Assert.Equal(2, page.Dialogs.Asked, "confirmation asked each time");
        Assert.False(vm.Results.Contains(second), "row removed");
        Assert.Sequence(new[] { 1, 2 }, vm.Results.Select(r => r.Number), "renumbered");
        Assert.Equal("Total measurements: 2", vm.TotalText, "total updated");
        Assert.Sequence(before, SHA256.HashData(File.ReadAllBytes(page.File)), "CSV file unchanged");

        // A broken file keeps the current import.
        File.WriteAllText(page.File, "Capture No,Start Time\n\"1");
        vm.ImportCommand.Execute(null);
        await Assert.Eventually(() => Make.ToastedStart(page.Toasts, ToastKind.Error, $"{Path.GetFileName(page.File)} was not imported. Line 2"), "error toast");
        Assert.Equal(2, vm.Results.Count, "previous import kept");
    }

    [Test("FE-066", "Upload Data: sends the reviewed rows once; busy while uploading; failure keeps data for retry; success blocks a repeat")]
    public static async Task UploadFlow()
    {
        using var page = new UploadPage(ExportedCsv());
        var vm = page.ViewModel;
        await page.ImportAsync();
        vm.DeleteCommand.Execute(vm.Results.Single(r => r.CaptureNumber == 3));

        // 1. The backend fails: the error is shown, everything stays, a retry is possible.
        page.Backend.Respond(HttpStatusCode.ServiceUnavailable, """{"title":"x","status":503,"detail":"The measurement database is unavailable. Nothing was stored; try again later."}""");
        vm.UploadCommand.Execute(null);
        await Assert.Eventually(() => !vm.IsUploading && page.Backend.Requests.Count == 1, "first attempt finished");
        Assert.True(Make.Toasted(page.Toasts, ToastKind.Error,
            "Upload failed: The measurement database is unavailable. Nothing was stored; try again later. The data is kept; try again."), "error toast with the reason");
        Assert.Equal(ToastService.ErrorDuration, page.Toasts.Items.Single(t => t.Kind == ToastKind.Error).Duration, "error toast 5 s");
        Assert.Equal(2, vm.Results.Count, "rows kept");
        Assert.False(vm.IsUploaded, "not marked uploaded");
        Assert.True(vm.UploadCommand.CanExecute(null), "retry possible");

        // 2. Retry: busy (no second click) until the backend answers.
        page.Backend.Respond(HttpStatusCode.Created, """{"uploadId":41,"recordCount":2,"alreadyUploaded":false,"uploadedAt":"2026-10-22T08:00:00+00:00"}""");
        page.Backend.Hold = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        vm.UploadCommand.Execute(null);
        await Assert.Eventually(() => page.Backend.Requests.Count == 2, "second request sent");
        Assert.True(vm.IsUploading, "uploading state");
        Assert.Equal("Uploading…", vm.UploadLabel, "loading label");
        Assert.False(vm.UploadCommand.CanExecute(null), "no duplicate click while uploading");
        Assert.False(vm.DeleteCommand.CanExecute(vm.Results[0]), "no delete while uploading");
        vm.UploadCommand.Execute(null);
        page.Backend.Hold.SetResult();
        await Assert.Eventually(() => vm.IsUploaded, "uploaded");
        Assert.Equal(2, page.Backend.Requests.Count, "the click while busy sent nothing");
        Assert.True(Make.Toasted(page.Toasts, ToastKind.Success, "Uploaded 2 measurements of DH-001."), "success toast");
        Assert.Equal(ToastService.ShortDuration, page.Toasts.Items.Single(t => t.Kind == ToastKind.Success && t.Message.StartsWith("Uploaded")).Duration, "success toast 3 s");
        Assert.Equal(2, vm.Results.Count, "rows kept for review");
        Assert.False(vm.UploadCommand.CanExecute(null), "same dataset cannot be uploaded again");
        Assert.False(vm.DeleteCommand.CanExecute(vm.Results[0]), "uploaded rows are not edited");

        // The request: what the table shows (deleted row left out), with the original values and both user names.
        using var body = JsonDocument.Parse(page.Backend.Requests[1].Body);
        var info = body.RootElement.GetProperty("measurementInformation");
        Assert.Equal("POST /api/measurements/upload", page.Backend.Requests[1].Line, "endpoint");
        Assert.Equal(Session.Id.ToString(), info.GetProperty("sessionId").GetString(), "session id");
        Assert.Equal("DH-001", info.GetProperty("drillhole").GetString(), "drillhole");
        Assert.Equal("CORP\\creator", info.GetProperty("createdUsername").GetString(), "created username");
        Assert.Equal(WindowsAccount.Current(), info.GetProperty("uploadingUsername").GetString(), "uploading username");
        var records = body.RootElement.GetProperty("records").EnumerateArray().ToList();
        Assert.Sequence(new[] { 2, 1 }, records.Select(r => r.GetProperty("captureNumber").GetInt32()), "rows in table order, deleted row left out");
        Assert.Equal(10.0m, records[0].GetProperty("depthFrom").GetDecimal(), "depthFrom (m)");
        Assert.Equal(10.25m, records[0].GetProperty("depthTo").GetDecimal(), "depthTo (m)");
        Assert.Equal(175.0, records[0].GetProperty("count0").GetDouble(), "CPS");
        Assert.Equal(1_760_600_000_002, records[0].GetProperty("tsMs").GetInt64(), "device ts_ms");
        Assert.Equal(Make.Row(Session.Id, 2, 0).StartTime, records[0].GetProperty("startTime").GetDateTimeOffset(), "original start time");
        Assert.Equal(JsonValueKind.Null, records[0].GetProperty("count0Raw").ValueKind, "missing value sent as null");

        // 3. A new import can be uploaded; the backend reports it was stored before.
        await page.ImportAsync();
        Assert.False(vm.IsUploaded, "new import");
        page.Backend.Respond(HttpStatusCode.OK, """{"uploadId":41,"recordCount":3,"alreadyUploaded":true,"uploadedAt":"2026-10-22T08:00:00+00:00"}""");
        vm.UploadCommand.Execute(null);
        await Assert.Eventually(() => vm.IsUploaded, "answered");
        Assert.True(Make.ToastedStart(page.Toasts, ToastKind.Warning, "These 3 measurements were already uploaded on"), "already-uploaded warning");
    }

    [Test("FE-067", "Upload Data: required metadata is checked first; a cancelled confirmation sends nothing")]
    public static async Task UploadPreconditions()
    {
        using var noDrillhole = new UploadPage(ExportedCsv(Session with { DrillholeId = "" }));
        await noDrillhole.ImportAsync();
        noDrillhole.ViewModel.UploadCommand.Execute(null);
        await Assert.Eventually(() => Make.ToastedStart(noDrillhole.Toasts, ToastKind.Error, "The CSV has no Drillhole"), "drillhole required");
        Assert.Equal(0, noDrillhole.Backend.Requests.Count, "nothing sent");

        using var page = new UploadPage(ExportedCsv());
        Assert.False(page.ViewModel.UploadCommand.CanExecute(null), "nothing imported: upload unavailable");
        await page.ImportAsync();
        page.Dialogs.Confirm = false;
        page.ViewModel.UploadCommand.Execute(null);
        await Task.Delay(50);
        Assert.Equal(1, page.Dialogs.Asked, "asked to confirm");
        Assert.Equal(0, page.Backend.Requests.Count, "cancelled: nothing sent");

        // Deleting every row leaves nothing to upload.
        page.Dialogs.Confirm = true;
        foreach (var row in page.ViewModel.Results.ToList())
        {
            page.ViewModel.DeleteCommand.Execute(row);
        }

        Assert.False(page.ViewModel.UploadCommand.CanExecute(null), "no rows: upload unavailable");
    }

    [Test("FE-068", "CPS alarm: above the threshold only, never overlapping, reset on Stop, a sound failure is ignored")]
    public static void AlarmRules()
    {
        Assert.False(CpsAlarm.Exceeds(125, 150), "below");
        Assert.False(CpsAlarm.Exceeds(150, 150), "equal");
        Assert.True(CpsAlarm.Exceeds(175, 150), "above");
        Assert.False(CpsAlarm.Exceeds(null, 150), "no CPS");
        Assert.False(CpsAlarm.Exceeds(175, null), "no threshold");

        var now = DateTimeOffset.UnixEpoch;
        var played = 0;
        var alarm = new CpsAlarm(() => played++, TimeSpan.FromSeconds(1), () => now);
        Assert.True(alarm.Check(175, 150), "sounds");
        Assert.False(alarm.Check(200, 150), "still sounding: no overlap");
        now += TimeSpan.FromSeconds(1);
        Assert.True(alarm.Check(200, 150), "after the tone: sounds again");
        Assert.False(alarm.Check(150, 150), "equal: silent");
        alarm.Reset();
        Assert.True(alarm.Check(151, 150), "reset (Stop): no pending state");
        Assert.Equal(3, played, "played three times");

        var broken = new CpsAlarm(() => throw new InvalidOperationException("no audio device"), TimeSpan.Zero);
        Assert.False(broken.Check(175, 150), "a sound failure does not throw");

        Assert.True(CpsAlarm.TryParseThreshold("150", out var t) && t == 150, "whole");
        Assert.True(CpsAlarm.TryParseThreshold("12,5", out t) && t == 12.5, "comma decimal");
        Assert.True(CpsAlarm.TryParseThreshold("0", out t) && t == 0, "zero allowed");
        Assert.False(CpsAlarm.TryParseThreshold("-1", out _), "negative");
        Assert.False(CpsAlarm.TryParseThreshold("1.234", out _), "more than two decimals");
        Assert.False(CpsAlarm.TryParseThreshold("abc", out _), "text");

        // The tone used by the app plays without waiting (returns at once).
        var tone = new AlarmTone(880, TimeSpan.FromSeconds(0.6));
        var watch = System.Diagnostics.Stopwatch.StartNew();
        try
        {
            tone.Play();
        }
        catch (InvalidOperationException)
        {
            // A machine without an audio device; non-blocking is still checked by the timing below.
        }

        Assert.True(watch.ElapsedMilliseconds < 400, $"Play returned in {watch.ElapsedMilliseconds} ms (does not block for the 600 ms tone)");
    }

    [Test("FE-069", "Measurement page: the alarm sounds once per completed row above the threshold; capture continues normally")]
    public static async Task AlarmDuringMeasurement()
    {
        using var folder = new TestFolder();
        var repository = new MemoryRepository(folder.Path("m.db"));
        var device = new ControlledMeasurements();
        var toasts = new ToastService();
        var played = 0;
        using var vm = new MeasurementViewModel(device, new FakeExport(), new FakeDialogs(), repository, toasts: toasts,
            durationSettings: new CaptureDurationSettings(null), alarm: new CpsAlarm(() => played++, TimeSpan.Zero), defaultThreshold: 150);

        Assert.Equal("150", vm.Threshold, "default threshold from the YAML value");
        await Make.Arm(vm, Make.Core);

        void Complete(ControlledMeasurements d, double? cps) => d.CompleteWith(new CaptureData(
            d.LastStart, d.LastStart + d.LastDuration, [1.0], "cps", d.LastStart + d.LastDuration, new DeviceReading(Count0: cps)));

        await Make.Capture(vm, device, d => Complete(d, 125));
        Assert.Equal(0, played, "125 CPS: no alarm");
        await Make.Capture(vm, device, d => Complete(d, 150));
        Assert.Equal(0, played, "150 CPS (equal): no alarm");
        await Make.Capture(vm, device, d => Complete(d, 175));
        Assert.Equal(1, played, "175 CPS: alarm");
        Assert.Equal(MeasurementState.Ready, vm.State, "still measuring after the alarm");
        Assert.Equal(175.0, vm.Results.Single(r => r.CaptureNumber == 3).Reading.Count0, "measured value unchanged");
        Assert.Equal("10.75", vm.NextFrom, "depth advanced as usual");
        await Make.Capture(vm, device, d => Complete(d, 180));
        Assert.Equal(2, played, "one alarm per completed row");
        Assert.Equal(4, vm.Results.Count, "every row added");

        vm.Threshold = "abc";
        Assert.True(vm.IsThresholdInvalid, "invalid threshold: red border");
        vm.CommitThreshold();
        Assert.True(Make.Toasted(toasts, ToastKind.Error, MeasurementViewModel.ThresholdError), "invalid threshold: error toast");
        await Make.Capture(vm, device, d => Complete(d, 999));
        Assert.Equal(2, played, "invalid threshold: no alarm, capture still works");
        Assert.Equal(5, vm.Results.Count, "row added");

        vm.Threshold = "";
        Assert.False(vm.IsThresholdInvalid, "empty is valid (no alarm)");
        vm.Threshold = "200";
        await Make.Capture(vm, device, d => Complete(d, 201));
        Assert.Equal(3, played, "edited threshold applies to the next row");
    }

    [Test("FE-070", "Chart: bars are the rows' CPS (count0), not ts_ms; the y-axis label is CPS")]
    public static void ChartShowsCps()
    {
        WpfResources();
        var session = Guid.NewGuid();
        var rows = new[]
        {
            Make.Row(session, 1, 0, reading: new DeviceReading(TsMs: 1_760_600_000_000, Count0: 10)),
            Make.Row(session, 2, 250, reading: new DeviceReading(TsMs: 5, Count0: 20)),
            Make.Row(session, 3, 500, reading: new DeviceReading(TsMs: 1_760_600_000_000, Count0: null))
        };
        var chart = new DepthBarChart { ItemsSource = rows, Width = 600, Height = 300, YAxisLabel = (string)Application.Current.FindResource("ChartYAxisLabel") };
        chart.Measure(new Size(600, 300));
        chart.Arrange(new Rect(0, 0, 600, 300));
        new RenderTargetBitmap(600, 300, 96, 96, PixelFormats.Pbgra32).Render(chart);

        var bars = ((System.Collections.IEnumerable)typeof(DepthBarChart).GetField("bars", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!
            .GetValue(chart)!).Cast<object>().Select(b => ((Rect Area, MeasurementResult Row))b).ToList();
        Assert.Sequence(new[] { 1, 2 }, bars.Select(b => b.Row.CaptureNumber), "rows with a CPS are drawn; no CPS, no bar");
        Assert.Equal(2.0, Math.Round(bars[1].Area.Height / bars[0].Area.Height, 6), "bar heights follow CPS 20 : 10 (ts_ms would be tiny : huge)");
        Assert.Equal("CPS", chart.YAxisLabel, "y-axis label from CapturePanel.yaml");
    }

    [Test("FE-071", "Layout: Starting Depth, Step and Threshold are right-aligned; the shared table's No. column is compact")]
    public static async Task InputAlignmentAndNumberColumn()
    {
        WpfResources();
        using var folder = new TestFolder();
        using var vm = Make.Measurement(new MemoryRepository(folder.Path("m.db")));
        var panel = new CapturePanelView { DataContext = vm };
        var window = new Window { Content = panel, Width = 900, Height = 400, ShowActivated = false, WindowStyle = WindowStyle.None, Left = -10_000 };

        try
        {
            window.Show();
            await Task.Delay(50);
            var boxes = Descendants<TextBox>(panel).Where(b => b.IsVisible).ToList();
            Assert.Sequence(new[] { "Starting depth in metres", "Depth step in centimetres", "CPS alarm threshold" },
                boxes.Select(System.Windows.Automation.AutomationProperties.GetName), "the three inputs, in order");
            Assert.True(boxes.All(b => b.TextAlignment == TextAlignment.Right), "values right-aligned");
            Assert.True(boxes.Select(b => b.ActualWidth).Distinct().Count() == 1, "same width for all three");

            var grid = new MeasurementGrid();
            var number = ((DataGrid)grid.FindName("ResultsGrid")).Columns[0];
            Assert.Equal("No", number.Header, "first column");
            Assert.True(number.Width.IsAbsolute && number.Width.Value == 44, $"No. column: compact width from MeasurementTable.yaml ({number.Width})");
        }
        finally
        {
            window.Close();
        }

        // The Upload Data page renders with the app's resources and shows the same table columns as the Measurement page.
        using var upload = new UploadPage(ExportedCsv());
        await upload.ImportAsync();
        var page = new UploadDataPage { DataContext = upload.ViewModel };
        var uploadWindow = new Window { Content = page, Width = 1100, Height = 700, ShowActivated = false, WindowStyle = WindowStyle.None, Left = -10_000 };

        try
        {
            uploadWindow.Show();
            await Task.Delay(100);
            var table = Descendants<DataGrid>(page).Single();
            var measurementColumns = ((IReadOnlyList<MeasurementTableColumn>)Application.Current.FindResource("MeasurementTableColumns"))
                .Where(c => c.Visible).Select(c => c.Header).Append("Delete");
            Assert.Sequence(measurementColumns, table.Columns.Select(c => (string)c.Header), "same columns as the Measurement table");
            Assert.Equal(3, table.Items.Count, "imported rows shown");
            Assert.True(Descendants<TextBlock>(page).Any(t => t.Text == "CORP\\creator") && Descendants<TextBlock>(page).Any(t => t.Text == "Total measurements: 3"),
                "metadata and total shown");
        }
        finally
        {
            uploadWindow.Close();
        }
    }

    // ---------- helpers ----------

    private static bool resourcesLoaded;

    // The app's resources (Theme.xaml + the section YAML files), as App.OnStartup loads them, for view tests.
    private static void WpfResources()
    {
        if (resourcesLoaded)
        {
            return;
        }

        var app = Application.Current ?? new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        app.Resources.MergedDictionaries.Add(new ResourceDictionary { Source = new Uri("pack://application:,,,/Frontend;component/Shared/Styles/Theme.xaml") });
        Frontend.Pages.PageSettings.AddTo(app.Resources, AppContext.BaseDirectory);
        resourcesLoaded = true;
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (var i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);

            if (child is T match)
            {
                yield return match;
            }

            foreach (var nested in Descendants<T>(child))
            {
                yield return nested;
            }
        }
    }

    /// <summary>An Upload Data page with a CSV file on disk, a fake dialog service and a recording HTTP stub.</summary>
    private sealed class UploadPage : IDisposable
    {
        private readonly TestFolder folder = new();
        private readonly ApiClient api;

        public UploadPage(string csv)
        {
            File = folder.Path("export.csv");
            System.IO.File.WriteAllText(File, csv, new UTF8Encoding(true));
            Dialogs.CsvFile = File;
            api = new ApiClient(new ApiSettings("http://stub.test", TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5)), Backend);
            ViewModel = new UploadDataViewModel(new MeasurementUploadClient(api), Dialogs, Toasts);
        }

        public string File { get; }

        public FakeDialogs Dialogs { get; } = new();

        public ToastService Toasts { get; } = new();

        public UploadStub Backend { get; } = new();

        public UploadDataViewModel ViewModel { get; }

        /// <summary>Import CSV with the page's file chosen; waits for the import's toast.</summary>
        public async Task ImportAsync()
        {
            Dialogs.CsvFile = File;
            Toasts.Items.Clear();
            ViewModel.ImportCommand.Execute(null);
            await Assert.Eventually(() => ViewModel.HasImport && Toasts.Items.Count > 0, "imported");
        }

        public void Dispose()
        {
            api.Dispose();
            folder.Dispose();
        }
    }

    /// <summary>Records POST bodies and answers with a configurable status and JSON (optionally held).</summary>
    private sealed class UploadStub : HttpMessageHandler
    {
        private HttpStatusCode status = HttpStatusCode.Created;
        private string json = """{"uploadId":1,"recordCount":0,"alreadyUploaded":false,"uploadedAt":"2026-10-22T08:00:00+00:00"}""";

        public List<(string Line, string Body)> Requests { get; } = [];

        public TaskCompletionSource? Hold { get; set; }

        public void Respond(HttpStatusCode code, string body)
        {
            status = code;
            json = body;
        }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var body = request.Content is null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);
            lock (Requests) Requests.Add(($"{request.Method} {request.RequestUri!.AbsolutePath}", body));

            if (Hold is { } hold)
            {
                await hold.Task.WaitAsync(cancellationToken);
            }

            var mediaType = (int)status >= 400 ? "application/problem+json" : "application/json";
            return new HttpResponseMessage(status) { Content = new StringContent(json, Encoding.UTF8, mediaType) };
        }
    }
}
