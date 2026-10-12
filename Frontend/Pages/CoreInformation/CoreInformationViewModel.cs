using System.Globalization;
using Frontend.Shared.Infrastructure;
using Frontend.Shared.Models;

namespace Frontend.Pages.CoreInformation;

public sealed class CoreInformationViewModel : ObservableObject
{
    private string drillholeId = "";
    private string drillholeName = "";
    private string projectArea = "";
    private string coreId = "";
    private string depthFrom = "";
    private string depthTo = "";
    private string operatorName = "";
    private string notes = "";
    private bool showErrors;

    public CoreInformationViewModel(Action<CoreSnapshot> saved) =>
        SaveCommand = new RelayCommand(_ => Save(saved));

    public event Action? Edited;

    public RelayCommand SaveCommand { get; }

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

    public string DepthFrom
    {
        get => depthFrom;
        set { if (Set(ref depthFrom, value)) Update(); }
    }

    public string DepthTo
    {
        get => depthTo;
        set { if (Set(ref depthTo, value)) Update(); }
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

    public static bool TryDepth(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.CurrentCulture, out value) && value >= 0;

    public string CoreDepth =>
        DepthRange() is { } range ? (range.To - range.From).ToString("0.00", CultureInfo.CurrentCulture) : "—";

    public bool IsValid => HasRequiredText && DepthRange() is not null;

    public string DrillholeIdError =>
        showErrors && string.IsNullOrWhiteSpace(DrillholeId) ? "Enter a drillhole ID." : "";

    public string DrillholeNameError =>
        showErrors && string.IsNullOrWhiteSpace(DrillholeName) ? "Enter a drillhole name." : "";

    public string CoreIdError =>
        showErrors && string.IsNullOrWhiteSpace(CoreId) ? "Enter a core ID." : "";

    public string DepthError =>
        (showErrors || DepthFrom.Length > 0 || DepthTo.Length > 0) && DepthRange() is null
            ? "Enter valid depths. To must be greater than From."
            : "";

    private bool HasRequiredText =>
        !string.IsNullOrWhiteSpace(DrillholeId) &&
        !string.IsNullOrWhiteSpace(DrillholeName) &&
        !string.IsNullOrWhiteSpace(CoreId);

    /// <summary>Parses both depths once. Null when either is invalid or To is not greater than From.</summary>
    private (decimal From, decimal To)? DepthRange() =>
        TryDepth(DepthFrom, out var from) && TryDepth(DepthTo, out var to) && to > from ? (from, to) : null;

    private void Save(Action<CoreSnapshot> saved)
    {
        showErrors = true;
        RefreshValidation();

        if (!HasRequiredText || DepthRange() is not { } range)
        {
            return;
        }

        saved(new CoreSnapshot(
            DrillholeId.Trim(),
            DrillholeName.Trim(),
            ProjectArea.Trim(),
            CoreId.Trim(),
            range.From,
            range.To,
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
        Changed(nameof(CoreDepth));
        Changed(nameof(DrillholeIdError));
        Changed(nameof(DrillholeNameError));
        Changed(nameof(CoreIdError));
        Changed(nameof(DepthError));
    }
}
