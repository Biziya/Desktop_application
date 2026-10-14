using Frontend.Pages.Measurement;
using Frontend.Shared.Infrastructure;
using Frontend.Shared.Models;

namespace Frontend.Pages.CoreInformation;

public sealed class CoreInformationViewModel : ObservableObject
{
    private string drillholeId = "";
    private string drillholeName = "";
    private string projectArea = "";
    private string coreId = "";
    private string operatorName = "";
    private string notes = "";
    private bool showErrors;
    private readonly IMeasurementService device;

    public CoreInformationViewModel(Action<CoreSnapshot> saved, IMeasurementService device, string databasePath)
    {
        this.device = device;
        DatabasePath = databasePath;
        operatorName = CurrentWindowsAccount();
        SaveCommand = new RelayCommand(_ => Save(saved));

        // Values are always read from the live connection, so a disconnect never leaves stale details on screen.
        device.ConnectionChanged += (_, _) =>
        {
            Changed(nameof(IsDeviceConnected));
            Changed(nameof(DeviceStatus));
            Changed(nameof(DeviceName));
            Changed(nameof(DeviceIdentifier));
        };
    }

    public event Action? Edited;

    public RelayCommand SaveCommand { get; }

    /// <summary>Path of the SQLite database actually in use.</summary>
    public string DatabasePath { get; }

    public bool IsDeviceConnected => device.ConnectedDevice is not null;

    public string DeviceStatus => IsDeviceConnected ? "Connected" : "Disconnected";

    public string DeviceName => device.ConnectedDevice?.Name ?? "No CT007 device connected";

    public string DeviceIdentifier => device.ConnectedDevice?.Identifier ?? "";

    public string DrillholeId
    {
        get => drillholeId;
        set { if (Set(ref drillholeId, value)) Update(); }
    }

    public string DrillholeName
    {
        get => drillholeName;
        set { if (Set(ref drillholeName, value)) Update(); }
    }

    public string ProjectArea
    {
        get => projectArea;
        set { if (Set(ref projectArea, value)) Update(); }
    }

    public string CoreId
    {
        get => coreId;
        set { if (Set(ref coreId, value)) Update(); }
    }

    public string Operator
    {
        get => operatorName;
        set { if (Set(ref operatorName, value)) Update(); }
    }

    public string Notes
    {
        get => notes;
        set { if (Set(ref notes, value)) Update(); }
    }

    public bool IsValid => HasRequiredText;

    /// <summary>
    /// The Windows account running the app: DOMAIN\Username for a domain account, otherwise the user name
    /// (a local account's "domain" is just the computer name, so it is left out).
    /// </summary>
    public static string CurrentWindowsAccount()
    {
        var user = Environment.UserName;
        var domain = Environment.UserDomainName;

        return string.IsNullOrWhiteSpace(domain) || string.Equals(domain, Environment.MachineName, StringComparison.OrdinalIgnoreCase)
            ? user
            : $"{domain}\\{user}";
    }

    public string DrillholeIdError =>
        showErrors && string.IsNullOrWhiteSpace(DrillholeId) ? "Enter a drillhole ID." : "";

    public string DrillholeNameError =>
        showErrors && string.IsNullOrWhiteSpace(DrillholeName) ? "Enter a drillhole name." : "";

    public string CoreIdError =>
        showErrors && string.IsNullOrWhiteSpace(CoreId) ? "Enter a core ID." : "";

    private bool HasRequiredText =>
        !string.IsNullOrWhiteSpace(DrillholeId) &&
        !string.IsNullOrWhiteSpace(DrillholeName) &&
        !string.IsNullOrWhiteSpace(CoreId);

    private void Save(Action<CoreSnapshot> saved)
    {
        showErrors = true;
        RefreshValidation();

        if (!HasRequiredText)
        {
            return;
        }

        // An emptied Operator is filled with the signed-in account again; an edited one is kept as entered.
        if (string.IsNullOrWhiteSpace(Operator))
        {
            Operator = CurrentWindowsAccount();
        }

        saved(new CoreSnapshot(
            DrillholeId.Trim(),
            DrillholeName.Trim(),
            ProjectArea.Trim(),
            CoreId.Trim(),
            Operator.Trim(),
            Notes.Trim()));
    }

    private void Update()
    {
        RefreshValidation();
        Edited?.Invoke();
    }

    private void RefreshValidation()
    {
        Changed(nameof(DrillholeIdError));
        Changed(nameof(DrillholeNameError));
        Changed(nameof(CoreIdError));
    }
}
