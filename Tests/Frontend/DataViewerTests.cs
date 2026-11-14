using Frontend.Api.Measurements;
using Frontend.Pages.DataViewer;

namespace Ct007.Tests.FrontendTests;

/// <summary>FE-DV: the Data Viewer page (uploaded measurements from the central database, through the backend).</summary>
public static class DataViewerTests
{
    private const string Drillholes = """
        [{"holeId":"GTZO_0042_1","projectCode":"Gurvan","campaign":"2026","uploadCount":2,"recordCount":3,"lastUploadedAt":"2026-11-10T09:30:00+08:00"},
         {"holeId":"KHAN_0007_2","projectCode":"Khan","campaign":null,"uploadCount":1,"recordCount":1,"lastUploadedAt":"2026-11-09T15:00:00+08:00"}]
        """;

    // Deliberately out of depth order, and one row without device values.
    private const string Records = """
        [{"sessionId":"8c4e0000-0000-0000-0000-000000000002","captureNumber":1,"startTime":"2026-11-10T09:00:10+08:00","endTime":"2026-11-10T09:00:15+08:00",
          "sampleCount":5,"depthFrom":0.250,"depthTo":0.500,"tsMs":1792115769651,"count0":140.25,"count0Raw":141,"hdm":null,"hdmRaw":null,"doseRate":0.12,
          "uploadId":8,"operator":"DOMAIN\\op","uploadedAt":"2026-11-10T09:30:00+08:00"},
         {"sessionId":"8c4e0000-0000-0000-0000-000000000001","captureNumber":1,"startTime":"2026-11-09T10:00:00+08:00","endTime":"2026-11-09T10:00:05+08:00",
          "sampleCount":5,"depthFrom":0.000,"depthTo":0.250,"tsMs":null,"count0":null,"count0Raw":null,"hdm":null,"hdmRaw":null,"doseRate":null,
          "uploadId":7,"operator":"DOMAIN\\op","uploadedAt":"2026-11-09T11:00:00+08:00"},
         {"sessionId":"8c4e0000-0000-0000-0000-000000000002","captureNumber":2,"startTime":"2026-11-10T09:00:20+08:00","endTime":"2026-11-10T09:00:25+08:00",
          "sampleCount":4,"depthFrom":0.500,"depthTo":0.750,"tsMs":1792115779651,"count0":162.5,"count0Raw":163,"hdm":1.2,"hdmRaw":1.3,"doseRate":0.14,
          "uploadId":8,"operator":"DOMAIN\\op","uploadedAt":"2026-11-10T09:30:00+08:00"}]
        """;

    [Test("FE-DV-01", "Uploaded records become Measurement table rows in depth order, numbered, with depths in mm and nulls kept")]
    public static async Task RowsInDepthOrder()
    {
        using var stub = new UploadedStub();
        var vm = new DataViewerViewModel(stub.Client);

        await vm.RefreshAsync();
        Assert.Sequence(new[] { "GTZO_0042_1", "KHAN_0007_2" }, vm.DrillholeOptions.Select(d => d.HoleId), "drillholes listed");

        vm.PickedDrillhole = vm.DrillholeOptions[0];
        await Assert.Eventually(() => vm.Results.Count == 3, "records load");

        Assert.Equal("drillhole=GTZO_0042_1", stub.LastQuery, "drillhole sent");
        Assert.Sequence(new[] { 1, 2, 3 }, vm.Results.Select(r => r.Number), "numbered from 1");
        Assert.Sequence(new long?[] { 0, 250, 500 }, vm.Results.Select(r => r.FromMm), "depth order");
        Assert.Sequence(new long?[] { 250, 250, 250 }, vm.Results.Select(r => r.StepMm), "step");
        Assert.Equal(null, vm.Results[0].Reading.Count0, "missing CPS stays empty");
        Assert.Near(140.25, vm.Results[1].Reading.Count0!.Value, 1e-9, "CPS");
        Assert.Equal("Gurvan", vm.Project, "project");
        Assert.Equal("2026", vm.Campaign, "campaign");
        Assert.Contains("2 uploads · 3 rows", vm.Summary, "summary");
    }

    [Test("FE-DV-02", "Search filters the uploaded drillholes; typing keeps the shown table")]
    public static async Task SearchKeepsTable()
    {
        using var stub = new UploadedStub();
        var vm = new DataViewerViewModel(stub.Client);
        await vm.RefreshAsync();
        vm.PickedDrillhole = vm.DrillholeOptions[0];
        await Assert.Eventually(() => vm.Results.Count == 3, "records load");

        vm.DrillholeSearch = "khan";
        Assert.Sequence(new[] { "KHAN_0007_2" }, vm.DrillholeOptions.Select(d => d.HoleId), "case-insensitive filter");
        vm.PickedDrillhole = null;
        Assert.Equal(3, vm.Results.Count, "table kept while typing");
        Assert.Equal("GTZO_0042_1", vm.Shown?.HoleId, "shown drillhole kept");
    }

    [Test("FE-DV-03", "Missing upload tables, an offline backend and no uploads each show their own message")]
    public static async Task States()
    {
        using var stub = new UploadedStub();
        var vm = new DataViewerViewModel(stub.Client);

        stub.Respond = () => Problem("The upload tables have not been created on the central database yet.");
        await vm.RefreshAsync();
        Assert.Contains("have not been created", vm.Message, "backend text shown");
        Assert.True(vm.CanRetry, "retry offered");

        stub.Respond = () => throw new HttpRequestException("refused");
        await vm.RefreshAsync();
        Assert.Contains("Backend is offline", vm.Message, "offline");

        stub.Respond = () => Json("[]");
        await vm.RefreshAsync();
        Assert.Contains("No measurements have been uploaded yet", vm.Message, "empty");
        Assert.False(vm.CanRetry, "no retry after success");
    }

    [Test("FE-DV-04", "Data Viewer page renders the Measurement table without the Delete column and without binding errors")]
    public static async Task PageRenders()
    {
        Wpf.EnsureResources();
        using var stub = new UploadedStub();
        var vm = new DataViewerViewModel(stub.Client);
        await vm.RefreshAsync();
        vm.PickedDrillhole = vm.DrillholeOptions[0];
        await Assert.Eventually(() => vm.Results.Count == 3, "records load");

        var page = new DataViewerPage { DataContext = vm };
        var errors = await Wpf.Render(page, "data-viewer");
        Assert.Sequence(Array.Empty<string>(), errors, "binding errors");

        var grid = Find<MeasurementGrid>(page)!;
        Assert.False(grid.ShowDelete, "read-only");
        Assert.Equal(System.Windows.Visibility.Collapsed, Find<System.Windows.Controls.DataGrid>(grid)!.Columns[^1].Visibility, "Delete column hidden");
        Assert.True(new MeasurementGrid().ShowDelete, "shown by default");
    }

    private static T? Find<T>(System.Windows.DependencyObject root) where T : System.Windows.DependencyObject
    {
        if (root is T found)
        {
            return found;
        }

        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(root); i++)
        {
            if (Find<T>(System.Windows.Media.VisualTreeHelper.GetChild(root, i)) is { } result)
            {
                return result;
            }
        }

        return null;
    }

    private static HttpResponseMessage Json(string json) =>
        new(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };

    private static HttpResponseMessage Problem(string detail) =>
        new(HttpStatusCode.ServiceUnavailable)
        {
            Content = new StringContent($$"""{"status":503,"detail":"{{detail}}"}""", Encoding.UTF8, "application/problem+json")
        };

    private sealed class UploadedStub : HttpMessageHandler
    {
        private readonly ApiClient api;

        public UploadedStub()
        {
            api = new ApiClient(new ApiSettings("http://stub.test", TimeSpan.FromSeconds(5), TimeSpan.FromMinutes(5)), this);
            Client = new UploadedMeasurementsClient(api);
        }

        public UploadedMeasurementsClient Client { get; }

        /// <summary>Overrides every answer; null serves the drillholes and records above.</summary>
        public Func<HttpResponseMessage>? Respond { get; set; }

        public string? LastQuery { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            await Task.Yield();

            if (Respond is { } respond)
            {
                return respond();
            }

            var path = request.RequestUri!.AbsolutePath;

            if (path == UploadedMeasurementsClient.RecordsPath)
            {
                LastQuery = Uri.UnescapeDataString(request.RequestUri.Query.TrimStart('?'));
                return Json(Records);
            }

            return path == UploadedMeasurementsClient.DrillholesPath ? Json(Drillholes) : new HttpResponseMessage(HttpStatusCode.NotFound);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                api.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}
