using System.Globalization;
using Frontend.Shared.Infrastructure;
using CoreInformationData = Frontend.Shared.Models.CoreInformation;
namespace Frontend.Pages.CoreInformation;
public sealed class CoreInformationViewModel : ObservableObject
{
    private string drillholeId = "", drillholeName = "", projectArea = "", coreId = "";
    private string depthFrom = "", depthTo = "", operatorName = "", notes = "";
    private bool showErrors;
    public event Action? Edited;
    public CoreInformationViewModel(Action<CoreInformationData> saved) =>
        SaveCommand = new RelayCommand(_ =>
        {
            showErrors = true;
            RefreshValidation();
            if (IsValid) saved(new CoreInformationData(DrillholeId.Trim(), DrillholeName.Trim(),
                ProjectArea.Trim(), CoreId.Trim(), Parse(DepthFrom), Parse(DepthTo), Operator.Trim(), Notes.Trim()));
        });
    public RelayCommand SaveCommand { get; }
    public string DrillholeId { get => drillholeId; set { if (Set(ref drillholeId,value)) Update(); } }
    public string DrillholeName { get => drillholeName; set { if (Set(ref drillholeName,value)) Update(); } }
    public string ProjectArea { get => projectArea; set { if (Set(ref projectArea,value)) Update(); } }
    public string CoreId { get => coreId; set { if (Set(ref coreId,value)) Update(); } }
    public string DepthFrom { get => depthFrom; set { if (Set(ref depthFrom,value)) Update(); } }
    public string DepthTo { get => depthTo; set { if (Set(ref depthTo,value)) Update(); } }
    public string Operator { get => operatorName; set { if (Set(ref operatorName,value)) Update(); } }
    public string Notes { get => notes; set { if (Set(ref notes,value)) Update(); } }
    public static bool TryDepth(string text, out decimal value) =>
        decimal.TryParse(text, NumberStyles.AllowDecimalPoint, CultureInfo.CurrentCulture, out value) && value >= 0;
    private static decimal Parse(string text) => TryDepth(text, out var value) ? value : 0;
    private bool ValidRange => TryDepth(DepthFrom, out var from) && TryDepth(DepthTo, out var to) && to > from;
    public string CoreDepth => ValidRange ? (Parse(DepthTo)-Parse(DepthFrom)).ToString("0.00", CultureInfo.CurrentCulture) : "—";
    public bool IsValid => !string.IsNullOrWhiteSpace(DrillholeId) && !string.IsNullOrWhiteSpace(DrillholeName)
        && !string.IsNullOrWhiteSpace(CoreId) && ValidRange;
    public string DrillholeIdError => showErrors && string.IsNullOrWhiteSpace(DrillholeId) ? "Enter a drillhole ID." : "";
    public string DrillholeNameError => showErrors && string.IsNullOrWhiteSpace(DrillholeName) ? "Enter a drillhole name." : "";
    public string CoreIdError => showErrors && string.IsNullOrWhiteSpace(CoreId) ? "Enter a core ID." : "";
    public string DepthError => (showErrors || DepthFrom.Length > 0 || DepthTo.Length > 0) && !ValidRange
        ? "Enter valid depths. To must be greater than From." : "";
    private void Update() { RefreshValidation(); Edited?.Invoke(); }
    private void RefreshValidation()
    {
        Changed(nameof(CoreDepth)); Changed(nameof(IsValid)); Changed(nameof(DrillholeIdError));
        Changed(nameof(DrillholeNameError)); Changed(nameof(CoreIdError)); Changed(nameof(DepthError));
    }
}
