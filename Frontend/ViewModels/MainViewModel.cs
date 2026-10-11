using Frontend.Infrastructure;
using Frontend.Services;
namespace Frontend.ViewModels;
public sealed class MainViewModel : ObservableObject, IDisposable
{
    private ObservableObject currentPage;
    public MainViewModel()
    {
        Measurement = new MeasurementViewModel(new MockMeasurementService(), new CsvExportService(), new DialogService());
        Core = new CoreInformationViewModel(core => { Measurement.SetCore(core); Navigate(Measurement); });
        Core.Edited += () => Measurement.SetCore(null);
        currentPage = Core;
        ShowCoreCommand = new RelayCommand(_ => Navigate(Core));
        ShowMeasurementCommand = new RelayCommand(_ => Navigate(Measurement));
    }
    public CoreInformationViewModel Core { get; }
    public MeasurementViewModel Measurement { get; }
    public RelayCommand ShowCoreCommand { get; }
    public RelayCommand ShowMeasurementCommand { get; }
    public ObservableObject CurrentPage { get => currentPage; private set => Set(ref currentPage,value); }
    public bool IsCoreSelected => CurrentPage == Core;
    public bool IsMeasurementSelected => CurrentPage == Measurement;
    private void Navigate(ObservableObject page)
    {
        CurrentPage = page; Changed(nameof(IsCoreSelected)); Changed(nameof(IsMeasurementSelected));
    }
    public void Dispose() => Measurement.Dispose();
}
