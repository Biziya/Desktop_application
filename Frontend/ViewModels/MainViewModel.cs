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
using Frontend.Pages.Measurement;
using Frontend.Pages.Measurement.CapturePanel;
using Frontend.Pages.Monitoring;
using Frontend.Pages.SavedData;
using Frontend.Pages.UploadData;
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
    private bool isSidebarExpanded = true;

    public MainViewModel()
        : this(new SqliteMeasurementRepository(SqliteMeasurementRepository.DefaultPath), ApiSettings.LoadConfigured(),
            DeviceSettings.Load(DeviceSettings.DefaultPath),
            alarmSettings: Resource<CapturePanelSettings.AlarmSettings>(CapturePanelSettings.AlarmResource),
            monitoringSettings: Resource<MonitoringSettings>(MonitoringSettings.Resource),
            retentionSettings: Resource<DataRetentionSettings>(DataRetentionSettings.Resource))
    {
    }

    /// <param name="handler">HTTP handler for the backend; null uses the network (tests pass a stub).</param>
    /// <param name="bluetoothDevices">Bluetooth device list; null reads Windows' list (tests pass a fake).</param>
    /// <param name="captureService">Capture source; null reads the Gamma Guard database (tests pass a fake).</param>
    /// <param name="alarmSettings">CPS alarm of CapturePanel.yaml; null: no default threshold and a silent alarm (tests).</param>
    /// <param name="dialogs">Confirmations and file dialogs; null shows the real windows (tests pass a fake).</param>
    /// <param name="monitoringSettings">Monitoring.yaml; null uses the defaults.</param>
    /// <param name="retentionSettings">data-retention.yaml; null: old data is never deleted (tests).</param>
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
        DataRetentionSettings? retentionSettings = null)
    {
        dialogs ??= new DialogService();

        // One HTTP client, connection monitor, device monitor and toast list for the whole app, alive until the window closes.
        api = new ApiClient(apiSettings, handler);
        Backend = new BackendConnection(api, apiSettings.StatusInterval);

        // A Gamma Guard database chosen by the user is saved beside the measurement database (Config folder), so it is
        // kept across restarts and updates.
        Devices = new DeviceStatusMonitor(
            bluetoothDevices is null ? new Ct007BluetoothCheck(deviceSettings) : new Ct007BluetoothCheck(deviceSettings, bluetoothDevices),
            new MeasurementDatabaseCheck(repository.DatabasePath),
            new GammaGuardDatabaseCheck(deviceSettings, selectionFile: GammaGuardDatabaseCheck.SelectionFileBeside(repository.DatabasePath)),
            deviceSettings.CheckInterval,
            deviceSettings.BluetoothCheckInterval);

        Toasts = new ToastService();
        announcements = new StatusAnnouncements(Backend, Devices, Toasts);

        // Real capture: the samples the connected detector writes to the local Gamma Guard database after each click
        // (read-only). The Measurement page follows the drillhole selected on Core Information and starts empty.
        var device = captureService ?? new DeviceCaptureService(new GammaGuardCaptureDevices(Devices));
        var alarm = alarmSettings is null
            ? null
            : new CpsAlarm(new AlarmTone(alarmSettings.ToneFrequency, TimeSpan.FromSeconds(alarmSettings.ToneDuration)).Play,
                TimeSpan.FromSeconds(alarmSettings.ToneDuration));
        Measurement = new MeasurementViewModel(
            device, new CsvExportService(), dialogs, repository, showDemoTsMs: false, toasts: Toasts,
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

        SavedData = new SavedDataViewModel(
            new SavedDataQueries(repository.DatabasePath),
            retentionSettings?.Description ?? "Automatic deletion of old measurements is off.",
            Toasts);

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
            new NavigationItem("Saved Data", "SavedDataIcon", SavedData),
            new NavigationItem("Upload Data", "UploadIcon", UploadData)
        ];
        currentPage = Core;
        Pages[0].IsSelected = true;

        ToggleSidebarCommand = new RelayCommand(_ => IsSidebarExpanded = !IsSidebarExpanded);
        ShowPageCommand = new RelayCommand(p => ShowPage((p as NavigationItem)?.Page ?? p as ObservableObject));
        Backend.Start();
        Devices.Start();

        if (retention is not null)
        {
            _ = RunRetentionAsync(retention, lifetime.Token);
        }
    }

    /// <summary>Backend reachability for the sidebar indicator (independent of the local Device Status).</summary>
    public BackendConnection Backend { get; }

    /// <summary>Local Device Status checks of this laptop.</summary>
    public DeviceStatusMonitor Devices { get; }

    /// <summary>Toasts shown over every page (MainWindow).</summary>
    public ToastService Toasts { get; }

    public CoreInformationViewModel Core { get; }

    public MeasurementViewModel Measurement { get; }

    /// <summary>Live CT008 detector status and 5-second samples.</summary>
    public MonitoringViewModel Monitoring { get; }

    /// <summary>Browse the sessions and captures stored in measurements.db.</summary>
    public SavedDataViewModel SavedData { get; }

    /// <summary>Import a measurement CSV, review it and upload it to the central database.</summary>
    public UploadDataViewModel UploadData { get; }

    /// <summary>The sidebar, in display order.</summary>
    public IReadOnlyList<NavigationItem> Pages { get; }

    /// <summary>Shows a page; its parameter is a <see cref="NavigationItem"/> or a page view model.</summary>
    public RelayCommand ShowPageCommand { get; }

    public RelayCommand ToggleSidebarCommand { get; }

    public bool IsSidebarExpanded
    {
        get => isSidebarExpanded;
        private set
        {
            if (Set(ref isSidebarExpanded, value))
            {
                Changed(nameof(SidebarToggleLabel));
            }
        }
    }

    public string SidebarToggleLabel => IsSidebarExpanded ? "Collapse sidebar" : "Expand sidebar";

    /// <summary>
    /// The page shown. The sidebar selection is derived from it (never a separate flag), so exactly one item is
    /// selected after any navigation, also Save &amp; Continue, which opens Measurement.
    /// </summary>
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

    // At start-up and then every CheckInterval. The deletion runs off the UI thread; the toast and the refreshes run
    // back on it.
    private async Task RunRetentionAsync(DataRetention job, CancellationToken token)
    {
        while (!token.IsCancellationRequested)
        {
            try
            {
                var keep = Measurement.SessionId;
                var removed = await Task.Run(() => job.Run(keep), token);

                if (removed > 0 && !token.IsCancellationRequested)
                {
                    Toasts.Information(
                        $"Removed {removed} session{(removed == 1 ? "" : "s")} with no capture in the last {job.Settings.RetentionDays} days.");
                    _ = Core.RefreshMeasuredAsync();

                    if (CurrentPage == SavedData)
                    {
                        SavedData.RefreshCommand.Execute(null);
                    }
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
