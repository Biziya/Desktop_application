using Frontend.Shared.Infrastructure;

namespace Frontend.Pages.CoreInformation.DrillholeInformation;

/// <summary>
/// One entry of the Project or Campaign dropdown in New mode: an existing name, or the last entry "+ Add new …"
/// (<see cref="IsAddNew"/>; its text comes from DrillholeInformation.yaml).
/// </summary>
public sealed record NameChoice(string Name, bool IsAddNew = false);

/// <summary>
/// Project or Campaign of a new drillhole: picked from the names already in use, or typed after "+ Add new …".
/// Required; a typed name that already exists in any letter case (after trimming) is refused, so "Zoovch" and
/// "zoovch" can never both exist.
/// </summary>
public sealed class NewNameField : ObservableObject
{
    private static readonly NameChoice AddNew = new("", IsAddNew: true);
    private readonly string label;
    private IReadOnlyList<string> existing = [];
    private NameChoice? picked;
    private bool isAdding;
    private bool touched;
    private bool rebuilding;
    private string text = "";

    /// <param name="label">Field name used in the messages ("Project", "Campaign").</param>
    public NewNameField(string label)
    {
        this.label = label;
        Choices.Add(AddNew);
        CancelAddCommand = new RelayCommand(_ => CancelAdd(), _ => isAdding);
    }

    /// <summary>Raised whenever the value or its validity may have changed.</summary>
    public event Action? InputChanged;

    /// <summary>The existing names A–Z, then "+ Add new …".</summary>
    public BulkObservableCollection<NameChoice> Choices { get; } = [];

    /// <summary>The × beside the text box: back to the list (what was typed is dropped).</summary>
    public RelayCommand CancelAddCommand { get; }

    /// <summary>Dropdown SelectedItem. Picking "+ Add new …" switches the field to a text box.</summary>
    public NameChoice? Picked
    {
        get => isAdding ? null : picked;
        set
        {
            // The dropdown clears its own selection while its list is rebuilt or hidden; that is not a choice.
            if (value is null || rebuilding || (!value.IsAddNew && Equals(value, picked) && !isAdding))
            {
                return;
            }

            touched = true;

            if (value.IsAddNew)
            {
                picked = null;
                text = "";
                IsAdding = true;
            }
            else
            {
                picked = value;
                IsAdding = false;
            }

            Notify();
        }
    }

    /// <summary>True while the field is a text box for a new name.</summary>
    public bool IsAdding
    {
        get => isAdding;
        private set
        {
            if (Set(ref isAdding, value))
            {
                Changed(nameof(IsChoosing));
                CancelAddCommand.Refresh();
            }
        }
    }

    public bool IsChoosing => !isAdding;

    /// <summary>The new name being typed (only while <see cref="IsAdding"/>).</summary>
    public string Text
    {
        get => text;
        set
        {
            if (Set(ref text, value ?? ""))
            {
                touched = true;
                Notify();
            }
        }
    }

    /// <summary>The trimmed value that would be saved ("" when nothing is picked or typed).</summary>
    public string Value => isAdding ? text.Trim() : picked?.Name ?? "";

    public bool HasInput => Value.Length > 0;

    /// <summary>Shown under the field: required once touched, or the name already exists.</summary>
    public string Error =>
        Value.Length == 0 ? (touched ? $"{label} is required." : "") :
        isAdding && existing.FirstOrDefault(name => string.Equals(name, Value, StringComparison.OrdinalIgnoreCase)) is { } match
            ? $"{label} «{match}» already exists — pick it from the list."
            : "";

    public bool HasError => Error.Length > 0;

    public bool IsValid => Value.Length > 0 && !HasError;

    /// <summary>Focus left the field: an empty field now shows "required".</summary>
    public void MarkTouched()
    {
        if (!touched)
        {
            touched = true;
            Notify();
        }
    }

    /// <summary>Back to an empty, untouched list choice.</summary>
    public void Reset()
    {
        picked = null;
        text = "";
        touched = false;
        IsAdding = false;
        Notify();
    }

    /// <summary>Sets the names in use (trimmed, distinct in any letter case, A–Z). The current choice is kept.</summary>
    public void SetExisting(IEnumerable<string?> names)
    {
        var list = names
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .DistinctBy(name => name, StringComparer.OrdinalIgnoreCase)
            .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (list.SequenceEqual(existing))
        {
            return;
        }

        existing = list;
        rebuilding = true;

        try
        {
            Choices.ReplaceAll(list.Select(name => new NameChoice(name)).Append(AddNew));
        }
        finally
        {
            rebuilding = false;
        }

        Notify();
    }

    private void CancelAdd()
    {
        text = "";
        IsAdding = false;
        Notify();
    }

    private void Notify()
    {
        Changed(nameof(Picked));
        Changed(nameof(Text));
        Changed(nameof(Value));
        Changed(nameof(HasInput));
        Changed(nameof(Error));
        Changed(nameof(HasError));
        Changed(nameof(IsValid));
        InputChanged?.Invoke();
    }
}
