using System.Net.Http;
using System.Windows;
using Frontend.Api;
using Frontend.Api.Measurements;
using Frontend.Api.CoreInformation.Drillholes;
using Frontend.Calculation.CoreInformation;
using Frontend.Calculation.CoreInformation.BluetoothDevice;
using Frontend.Calculation.CoreInformation.GammaGuardDatabase;
using Frontend.Calculation.CoreInformation.MeasurementDatabase;
using Frontend.Calculation.Measurement;
using Frontend.Config;
using Frontend.Pages.CoreInformation;
using Frontend.Pages.DataViewer;
using Frontend.Pages.Measurement;
using Frontend.Pages.Measurement.CapturePanel;
using Frontend.Pages.Monitoring;
using Frontend.Pages.UploadData;
using Frontend.Shared.Help;
using Frontend.Shared.Infrastructure;
using Frontend.Windows.ConfirmationWindow;

namespace Frontend.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly ApiClient api;
    private readonly StatusAnnouncements announcements;
    private readonly DataRetention? retention;
    private readonly CancellationTokenSource lifetime = new();
    private ObservableObject currentPage;

    public MainViewModel()
        : this(new SqliteMeasurementRepository(SqliteMeasurementRepository.DefaultPath), ApiSettings.LoadConfigured(),
            DeviceSettings.Load(DeviceSettings.DefaultPath),
            alarmSettings: Resource<CapturePanelSettings.AlarmSettings>(CapturePanelSettings.AlarmResource),
            monitoringSettings: Resource<MonitoringSettings>(MonitoringSettings.Resource),
            retentionSettings: Resource<DataRetentionSettings>(DataRetentionSettings.Resource),
            helpSettings: Resource<HelpSettings>(HelpSettings.Resource))
    {
    }

    /// <summary>Optional parameters are test seams; null means the real implementation, except:</summary>
    /// <param name="alarmSettings">null: no default threshold and no alarm.</param>
    /// <param name="retentionSettings">null: old data is never deleted.</param>
    /// <param name="helpSettings">null: the Help panel is empty.</param>
    public MainViewModel(
        IMeasurementRepository repository,
        ApiSettings apiSettings,
        DeviceSettings deviceSettings,
        HttpMessageHandler? handler = null,
        Func<IReadOnlyList<KnownBluetoothDevice>>? bluetoothDevices = null,
        IMeasurementService? captureService = null,
        CapturePanelSettings.AlarmSettings? alarmSettings = null,
        IDialogService? dialogs = null,
        MonitoringSettings? monitoringSettings = null,
        DataRetentionSettings? retentionSettings = null,
        HelpSettings? helpSettings = null)
    {
        dialogs ??= new DialogService();

        // App-wide singletons, alive until the window closes.
        api = new ApiClient(apiSettings, handler);
        Backend = new BackendConnection(api, apiSettings.StatusInterval);

        Devices = new DeviceStatusMonitor(
            bluetoothDevices is null ? new Ct007BluetoothCheck(deviceSettings) : new Ct007BluetoothCheck(deviceSettings, bluetoothDevices),
            new MeasurementDatabaseCheck(repository.DatabasePath),
            new GammaGuardDatabaseCheck(deviceSettings, selectionFile: GammaGuardDatabaseCheck.SelectionFileBeside(repository.DatabasePath)),
            deviceSettings.CheckInterval,
            deviceSettings.BluetoothCheckInterval);

        Toasts = new ToastService();
        announcements = new StatusAnnouncements(Backend, Devices, Toasts);

        var alarm = alarmSettings is null
            ? null
            : new CpsAlarm(new AlarmTone(alarmSettings.ToneFrequency, TimeSpan.FromSeconds(alarmSettings.ToneDuration)).Play,
                TimeSpan.FromSeconds(alarmSettings.ToneDuration));
        Measurement = new MeasurementViewModel(
            captureService ?? new DeviceCaptureService(new GammaGuardCaptureDevices(Devices)), new CsvExportService(), dialogs, repository, showDemoTsMs: false, toasts: Toasts,
            durationSettings: new CaptureDurationSettings(CaptureDurationSettings.PathBeside(repository.DatabasePath)),
            alarm: alarm, defaultThreshold: alarmSettings?.DefaultThreshold);
        UploadData = new UploadDataViewModel(new MeasurementUploadClient(api), dialogs, Toasts);
        Core = new CoreInformationViewModel(
            core =>
            {
                Measurement.SetCore(core);
                CurrentPage = Measurement;
            },
            new DrillholesClient(api),
            Backend,
            new DrillholeCache(repository.DatabasePath),
            repository,
            Devices,
            toasts: Toasts,
            dialogs: dialogs);
        Core.Edited += () => Measurement.SetCore(null);
        Core.DrillholeChanged += drillholeId => Measurement.SelectDrillhole(drillholeId);
        Monitoring = new MonitoringViewModel(Devices, monitoringSettings ?? MonitoringSettings.Default, () => CurrentPage = Core);

        if (retentionSettings is not null)
        {
            retention = new DataRetention(repository.DatabasePath, retentionSettings);
        }

        DataViewer = new DataViewerViewModel(new UploadedMeasurementsClient(api), Backend);

        // While measuring, the drillhole cannot be changed or cleared (Stop first).
        Measurement.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MeasurementViewModel.IsMeasurementModeActive))
            {
                Core.IsSelectionLocked = Measurement.IsMeasurementModeActive;
            }
        };

        // A save can add a drillhole to "Measured drillholes" or move it to the top.
        Measurement.MeasurementsSaved += () => _ = Core.RefreshMeasuredAsync();

        Pages =
        [
            new NavigationItem("Core Information", "CoreIcon", Core),
            new NavigationItem("Measurement", "MeasurementIcon", Measurement),
            new NavigationItem("Monitoring", "MonitoringIcon", Monitoring),
            new NavigationItem("Upload Data", "UploadIcon", UploadData),
            new NavigationItem("Data Viewer", "DataViewerIcon", DataViewer)
        ];
        currentPage = Core;
        Pages[0].IsSelected = true;

        ShowPageCommand = new RelayCommand(p => ShowPage((p as NavigationItem)?.Page ?? p as ObservableObject));
        Help = new HelpPanel(helpSettings);
        Backend.Start();
        Devices.Start();

        if (retention is not null)
        {
            _ = RunRetentionAsync(retention, lifetime.Token);
        }
    }

    public BackendConnection Backend { get; }

    public DeviceStatusMonitor Devices { get; }

    public ToastService Toasts { get; }

    public CoreInformationViewModel Core { get; }

    public MeasurementViewModel Measurement { get; }

    public MonitoringViewModel Monitoring { get; }

    public UploadDataViewModel UploadData { get; }

    public DataViewerViewModel DataViewer { get; }

    public HelpPanel Help { get; }

    public IReadOnlyList<NavigationItem> Pages { get; }

    /// <summary>Parameter: a <see cref="NavigationItem"/> or a page view model.</summary>
    public RelayCommand ShowPageCommand { get; }

    // Sidebar selection is derived from this, never stored separately, so exactly one item is ever selected.
    public ObservableObject CurrentPage
    {
        get => currentPage;
        private set
        {
            var previous = currentPage;

            if (!Set(ref currentPage, value))
            {
                return;
            }

            foreach (var item in Pages)
            {
                item.IsSelected = item.Page == value;
            }

            Help?.Show(CurrentTitle);

            (previous as INavigationAware)?.OnHidden();
            (value as INavigationAware)?.OnShown();
        }
    }

    public void ShowPage(ObservableObject? page)
    {
        if (page is not null && Pages.Any(p => p.Page == page))
        {
            CurrentPage = page;
        }
    }

    public bool IsShown(ObservableObject page) => CurrentPage == page;

    public void ShowHelp() => Help.Open(CurrentTitle);

    private string CurrentTitle => Pages.FirstOrDefault(p => p.Page == CurrentPage)?.Title ?? "";

    public void Dispose()
    {
        lifetime.Cancel();
        lifetime.Dispose();
        Monitoring.Dispose();
        Measurement.Dispose();
        announcements.Dispose();
        Devices.Dispose();
        Backend.Dispose();
        api.Dispose();
    }

    // At start-up, then every CheckInterval. Deletion runs off the UI thread; the toast and refresh run back on it.
    private async Task RunRetentionAsync(DataRetention job, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var keep = Measurement.SessionId; // read on the UI thread, before Task.Run
                var removed = await Task.Run(() => job.Run(keep), token);

                if (removed > 0 && !token.IsCancellationRequested)
                {
                    Toasts.Information(
                        $"Removed {removed} session{(removed == 1 ? "" : "s")} with no capture in the last {job.Settings.RetentionDays} days.");
                    _ = Core.RefreshMeasuredAsync();
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            catch (Exception error)
            {
                Toasts.Warning($"Old measurements could not be removed: {error.Message}");
            }

            try
            {
                await Task.Delay(job.Settings.CheckInterval, token);
            }
            catch (OperationCanceledException)
            {
                return;
            }
        }
    }

    private static T? Resource<T>(string key) where T : class => Application.Current?.TryFindResource(key) as T;
}
