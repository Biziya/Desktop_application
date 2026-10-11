using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Markup;
namespace Frontend.Pages.Measurement;

/// <summary>
/// Final size = default x multiplier, read from MeasurementUiConstants.xaml ("Name" and "NameMultiplier").
/// Used only by Measurement page XAML; nothing here is global.
/// </summary>
public static class MeasurementUiScale
{
    private static readonly Lazy<ResourceDictionary> Constants = new(() => new ResourceDictionary
    { Source = new Uri("/Frontend;component/Pages/Measurement/MeasurementUiConstants.xaml", UriKind.Relative) });

    private static double Number(string key)
    {
        if (Constants.Value[key] is double value && double.IsFinite(value) && value >= 0) return value;
        throw new InvalidOperationException($"MeasurementUiConstants.xaml: '{key}' is missing or is not a non-negative number.");
    }
    public static double Multiplier(string key, string? multiplierKey) => Number(multiplierKey ?? key + "Multiplier");
    public static double Scaled(string key, string? multiplierKey = null) => Number(key) * Multiplier(key, multiplierKey);
    public static Thickness ScaledThickness(string key, string? multiplierKey = null)
    {
        if (Constants.Value[key] is not Thickness t) throw new InvalidOperationException($"MeasurementUiConstants.xaml: '{key}' is not a Thickness.");
        var m = Multiplier(key, multiplierKey);
        return new Thickness(t.Left * m, t.Top * m, t.Right * m, t.Bottom * m);
    }
}

/// <summary>{local:Scale Name} or {local:Scale Name, OtherMultiplierName} -> double.</summary>
[MarkupExtensionReturnType(typeof(double))]
public sealed class ScaleExtension : MarkupExtension
{
    private readonly string key; private readonly string? multiplierKey;
    public ScaleExtension(string key) => this.key = key;
    public ScaleExtension(string key, string multiplierKey) { this.key = key; this.multiplierKey = multiplierKey; }
    public override object ProvideValue(IServiceProvider serviceProvider) => MeasurementUiScale.Scaled(key, multiplierKey);
}

/// <summary>{local:ScaleThickness Name[, MultiplierName]} -> Thickness (all sides scaled).</summary>
[MarkupExtensionReturnType(typeof(Thickness))]
public sealed class ScaleThicknessExtension : MarkupExtension
{
    private readonly string key; private readonly string? multiplierKey;
    public ScaleThicknessExtension(string key) => this.key = key;
    public ScaleThicknessExtension(string key, string multiplierKey) { this.key = key; this.multiplierKey = multiplierKey; }
    public override object ProvideValue(IServiceProvider serviceProvider) => MeasurementUiScale.ScaledThickness(key, multiplierKey);
}

/// <summary>{local:ScaleGridLength Name[, MultiplierName]} -> DataGridLength (fixed pixel width).</summary>
[MarkupExtensionReturnType(typeof(DataGridLength))]
public sealed class ScaleGridLengthExtension : MarkupExtension
{
    private readonly string key; private readonly string? multiplierKey;
    public ScaleGridLengthExtension(string key) => this.key = key;
    public ScaleGridLengthExtension(string key, string multiplierKey) { this.key = key; this.multiplierKey = multiplierKey; }
    public override object ProvideValue(IServiceProvider serviceProvider) => new DataGridLength(MeasurementUiScale.Scaled(key, multiplierKey));
}

/// <summary>
/// Splits the existing button text ("▶  Start" / "■  Stop") into glyph and label so the glyph can have its own size.
/// ConverterParameter: Icon or Label. The ViewModel string is unchanged.
/// </summary>
public sealed class ButtonTextPartConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var text = value as string ?? string.Empty;
        var split = text.IndexOf(' ');
        if (split <= 0) return parameter as string == "Icon" ? string.Empty : text;
        return parameter as string == "Icon" ? text[..split] : text[split..];
    }
    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
