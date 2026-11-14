using Frontend.Api.Measurements;
using Frontend.Pages.UploadData;
using Ct007.Tests.BackendTests;

namespace Ct007.Tests.IntegrationTests;

/// <summary>INT-020: Measurement CSV export → Upload Data import → HTTP → backend upload API, including a client timeout.</summary>
public static class UploadIntegrationTests
{
    [Test("INT-020", "Export → import → upload over HTTP; a retry after a client timeout does not store the dataset twice")]
    public static async Task ExportImportUploadWithTimeout()
    {
        // The backend stores slowly: the client (1 s timeout) gives up while the first save still completes.
        var repository = new MemoryUploadRepository { Delay = TimeSpan.FromSeconds(2) };
        await using var backend = await TestBackend.StartAsync(new FakeDrillholeRepository(), uploads: repository);
        using var folder = new TestFolder();

        var session = Make.Session();
        var rows = new[] { Make.Row(session.Id, 1, 0), Make.Row(session.Id, 2, 250), Make.Row(session.Id, 3, 500) };
        var file = folder.Path("export.csv");
        File.WriteAllText(file, CsvExportService.Serialize(rows, session, "CORP\\creator"), new UTF8Encoding(true));

        using var api = new ApiClient(new ApiSettings(backend.BaseUrl, TimeSpan.FromSeconds(1), TimeSpan.FromMinutes(5)));
        var toasts = new ToastService();
        var dialogs = new FakeDialogs { CsvFile = file };
        var vm = new UploadDataViewModel(new MeasurementUploadClient(api), dialogs, toasts);

        vm.ImportCommand.Execute(null);
        await Assert.Eventually(() => vm.Results.Count == 3, "imported");

        vm.UploadCommand.Execute(null);
        await Assert.Eventually(() => Make.ToastedStart(toasts, ToastKind.Error, "Upload failed: Backend did not respond in time."), "client timeout", 5000);
        Assert.False(vm.IsUploaded, "not marked uploaded");
        Assert.Equal(3, vm.Results.Count, "rows kept");
        await Assert.Eventually(() => repository.Uploads.Count == 1, "the first attempt was stored after all", 5000);

        repository.Delay = TimeSpan.Zero;
        vm.UploadCommand.Execute(null);
        await Assert.Eventually(() => vm.IsUploaded, "retry answered", 5000);
        Assert.True(Make.ToastedStart(toasts, ToastKind.Warning, "These 3 measurements were already uploaded on"), "retry: already uploaded");
        Assert.Equal(1, repository.Uploads.Count, "one dataset stored");
        Assert.Equal(3, repository.StoredRecords, "three records stored, not six");

        var stored = repository.Uploads.Single().Request;
        Assert.Equal(session.Id, stored.MeasurementInformation!.SessionId, "session id");
        Assert.Equal("CORP\\creator", stored.MeasurementInformation.CreatedUsername, "created username");
        Assert.Equal(WindowsAccount.Current(), stored.MeasurementInformation.UploadingUsername, "uploading username");
        Assert.Sequence(rows.Select(r => r.StartTime), stored.Records!.Select(r => r.StartTime!.Value), "original timestamps");
        Assert.Sequence(new[] { 0m, 0.25m, 0.5m }, stored.Records!.Select(r => r.DepthFrom!.Value), "depths");
        Assert.Sequence(rows.Select(r => r.Reading.Count0), stored.Records!.Select(r => r.Count0), "CPS values");
    }
}
