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
using Frontend.Pages.CoreInformation;
using Frontend.Pages.Measurement;
using Frontend.Pages.Measurement.CapturePanel;
using Frontend.Pages.UploadData;
using Frontend.Shared.Infrastructure;
using Frontend.Windows.ConfirmationWindow;

namespace Frontend.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private readonly ApiClient api;
    private readonly StatusAnnouncements announcements;
    private ObservableObject currentPage;
    private bool isSidebarExpanded = true;

    public MainViewModel()
        : this(new SqliteMeasurementRepository(SqliteMeasurementRepository.DefaultPath), ApiSettings.LoadConfigured(),
            DeviceSettings.Load(DeviceSettings.DefaultPath),
            alarmSettings: Application.Current?.TryFindResource(CapturePanelSettings.AlarmResource) as CapturePanelSettings.AlarmSettings)
    {
    }

    /// <param name="handler">HTTP handler for the backend; null uses the network (tests pass a stub).</param>
    /// <param name="bluetoothDevices">Bluetooth device list; null reads Windows' list (tests pass a fake).</param>
    /// <param name="captureService">Capture source; null reads the Gamma Guard database (tests pass a fake).</param>
    /// <param name="alarmSettings">CPS alarm of CapturePanel.yaml; null: no default threshold and a silent alarm (tests).</param>
    /// <param name="dialogs">Confirmations and file dialogs; null shows the real windows (tests pass a fake).</param>
    public MainViewModel(
        IMeasurementRepository repository,
        ApiSettings apiSettings,
        DeviceSettings deviceSettings,
        HttpMessageHandler? handler = null,
        Func<IReadOnlyList<KnownBluetoothDevice>>? bluetoothDevices = null,
        IMeasurementService? captureService = null,
        CapturePanelSettings.AlarmSettings? alarmSettings = null,
        IDialogService? dialogs = null)
    {
        dialogs ??= new DialogService();

        // One HTTP setup and one connection monitor for the whole app, alive until the window closes.
        api = new ApiClient(apiSettings, handler);
        Backend = new BackendConnection(api, apiSettings.StatusInterval);

        // Device Status is checked on this laptop, independently of the backend; also one instance for the whole app. A
        // Gamma Guard database chosen by the user is saved beside the measurement database (Config folder), so it is kept
        // across restarts and updates.
        Devices = new DeviceStatusMonitor(
            bluetoothDevices is null ? new Ct007BluetoothCheck(deviceSettings) : new Ct007BluetoothCheck(deviceSettings, bluetoothDevices),
            new MeasurementDatabaseCheck(repository.DatabasePath),
            new GammaGuardDatabaseCheck(deviceSettings, selectionFile: GammaGuardDatabaseCheck.SelectionFileBeside(repository.DatabasePath)),
            deviceSettings.CheckInterval,
            deviceSettings.BluetoothCheckInterval);

        // One toast list for every page; connection changes are announced once per transition.
        Toasts = new ToastService();
        announcements = new StatusAnnouncements(Backend, Devices, Toasts);

        // The Measurement page follows the drillhole selected on Core Information. At start nothing is selected, so
        // it starts empty (no earlier drillhole's rows are shown as if they belonged to the empty selection).
        // Real capture: the samples the connected CT007-M detector writes to the local Gamma Guard database after each
        // click (read-only). No simulated or demo values.
        var device = captureService ?? new DeviceCaptureService(new GammaGuardCaptureDevices(Devices));
        // The CPS alarm: a short tone; the next one can sound once it has finished.
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

        // While measuring, the drillhole cannot be changed or cleared (Stop first).
        Measurement.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(MeasurementViewModel.IsMeasurementModeActive))
            {
                Core.IsSelectionLocked = Measurement.IsMeasurementModeActive;
            }
        };

        // A successful save can add a drillhole to "Measured drillholes" or move it to the top.
        Measurement.MeasurementsSaved += () => _ = Core.RefreshMeasuredAsync();
        currentPage = Core;

        ToggleSidebarCommand = new RelayCommand(_ => IsSidebarExpanded = !IsSidebarExpanded);
        ShowCoreCommand = new RelayCommand(_ => CurrentPage = Core);
        ShowMeasurementCommand = new RelayCommand(_ => CurrentPage = Measurement);
        ShowUploadDataCommand = new RelayCommand(_ => CurrentPage = UploadData);
        Backend.Start();
        Devices.Start();
    }

    /// <summary>Backend reachability for the sidebar indicator (independent of the local Device Status).</summary>
    public BackendConnection Backend { get; }

    /// <summary>Local Device Status checks of this laptop.</summary>
    public DeviceStatusMonitor Devices { get; }

    /// <summary>Toasts shown over every page (MainWindow).</summary>
    public ToastService Toasts { get; }

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

    public RelayCommand ToggleSidebarCommand { get; }

    public CoreInformationViewModel Core { get; }

    public MeasurementViewModel Measurement { get; }

    public RelayCommand ShowCoreCommand { get; }

    public RelayCommand ShowMeasurementCommand { get; }

    /// <summary>Import a measurement CSV, review it and upload it to the central database.</summary>
    public UploadDataViewModel UploadData { get; }

    public RelayCommand ShowUploadDataCommand { get; }

    public ObservableObject CurrentPage
    {
        get => currentPage;
        private set
        {
            if (Set(ref currentPage, value))
            {
                Changed(nameof(IsCoreSelected));
                Changed(nameof(IsMeasurementSelected));
                Changed(nameof(IsUploadDataSelected));
            }
        }
    }

    // The sidebar selection is derived from the page actually shown (never a separate flag), so exactly one item is
    // selected, from start-up on and after any navigation (also Save & Continue, which opens Measurement).
    public bool IsCoreSelected => CurrentPage == Core;

    public bool IsMeasurementSelected => CurrentPage == Measurement;

    public bool IsUploadDataSelected => CurrentPage == UploadData;

    public void Dispose()
    {
        Measurement.Dispose();
        announcements.Dispose();
        Devices.Dispose();
        Backend.Dispose();
        api.Dispose();
    }
}
