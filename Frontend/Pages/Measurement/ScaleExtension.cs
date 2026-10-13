using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Markup;

namespace Frontend.Pages.Measurement;

/// <summary>
/// Final size = default x multiplier, read from MeasurementUiConstants.xaml.
/// <c>{local:Scale Name}</c> uses "Name" and "NameMultiplier" (multiplier 1 if there is none).
/// <c>{local:Scale Name, OtherMultiplier}</c> names the multiplier explicitly.
/// Works for double and Thickness defaults, and returns a DataGridLength when the target property needs one.
/// The constants dictionary is loaded once and is used only by Measurement page XAML.
/// </summary>
public sealed class ScaleExtension : MarkupExtension
{
    private static readonly Lazy<ResourceDictionary> Constants = new(() => new ResourceDictionary
    {
        Source = new Uri(
            $"/{typeof(ScaleExtension).Assembly.GetName().Name};component/Pages/Measurement/MeasurementUiConstants.xaml",
            UriKind.Relative)
    });

    private readonly string name;
    private readonly string? multiplierName;

    public ScaleExtension(string name) => this.name = name;

    public ScaleExtension(string name, string multiplierName)
    {
        this.name = name;
        this.multiplierName = multiplierName;
    }

    /// <summary>Scaled double for UI built in code, such as the generated measurement table columns.</summary>
    public static double Size(string name) =>
        new ScaleExtension(name).Scaled() is double size
            ? size
            : throw new InvalidOperationException($"MeasurementUiConstants.xaml: '{name}' is not a double.");

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var scaled = Scaled();
        return scaled is double length && NeedsGridLength(serviceProvider) ? new DataGridLength(length) : scaled;
    }

    private object Scaled()
    {
        var multiplier = Multiplier();

        return Constants.Value[name] switch
        {
            double size => size * multiplier,
            Thickness t => new Thickness(t.Left * multiplier, t.Top * multiplier, t.Right * multiplier, t.Bottom * multiplier),
            _ => throw new InvalidOperationException($"MeasurementUiConstants.xaml: '{name}' is missing or is not a double/Thickness.")
        };
    }

    private double Multiplier()
    {
        var key = multiplierName ?? name + "Multiplier";

        if (Constants.Value[key] is null && multiplierName is null)
        {
            return 1;
        }

        if (Constants.Value[key] is double value && double.IsFinite(value) && value >= 0)
        {
            return value;
        }

        throw new InvalidOperationException($"MeasurementUiConstants.xaml: '{key}' is missing or is not a non-negative number.");
    }

    private static bool NeedsGridLength(IServiceProvider serviceProvider) =>
        serviceProvider.GetService(typeof(IProvideValueTarget)) is IProvideValueTarget { TargetProperty: { } property } &&
        property switch
        {
            DependencyProperty dependencyProperty => dependencyProperty.PropertyType == typeof(DataGridLength),
            PropertyInfo propertyInfo => propertyInfo.PropertyType == typeof(DataGridLength),
            _ => false
        };
}
