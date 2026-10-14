using Frontend.Pages.CoreInformation;
using Frontend.Pages.Measurement;
using Frontend.Shared.Infrastructure;
using Frontend.Windows.ConfirmationWindow;

namespace Frontend.ViewModels;

public sealed class MainViewModel : ObservableObject, IDisposable
{
    private ObservableObject currentPage;
    private bool isSidebarExpanded = true;

    public MainViewModel()
        : this(new SqliteMeasurementRepository(SqliteMeasurementRepository.DefaultPath))
    {
    }

    public MainViewModel(IMeasurementRepository repository)
    {
        var device = new MockMeasurementService();
        Measurement = new MeasurementViewModel(device, new CsvExportService(), new DialogService(), repository);
        _ = Measurement.RestoreLatestSessionAsync();
        Core = new CoreInformationViewModel(
            core =>
            {
                Measurement.SetCore(core);
                Navigate(Measurement);
            },
            device,
            repository.DatabasePath);
        Core.Edited += () => Measurement.SetCore(null);
        currentPage = Core;

        ToggleSidebarCommand = new RelayCommand(_ => IsSidebarExpanded = !IsSidebarExpanded);
        ShowCoreCommand = new RelayCommand(_ => Navigate(Core));
        ShowMeasurementCommand = new RelayCommand(_ => Navigate(Measurement));
    }

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

    public ObservableObject CurrentPage
    {
        get => currentPage;
        private set => Set(ref currentPage, value);
    }

    public bool IsCoreSelected => CurrentPage == Core;

    public bool IsMeasurementSelected => CurrentPage == Measurement;

    private void Navigate(ObservableObject page)
    {
        CurrentPage = page;
        Changed(nameof(IsCoreSelected));
        Changed(nameof(IsMeasurementSelected));
    }

    public void Dispose() => Measurement.Dispose();
}
