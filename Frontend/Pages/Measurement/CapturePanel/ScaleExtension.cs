using System.Windows;
using System.Windows.Markup;

namespace Frontend.Pages.Measurement.CapturePanel;

/// <summary>
/// Final size = default x multiplier, read from CapturePanelConstants.xaml.
/// <c>{local:Scale Name}</c> uses "Name" and "NameMultiplier" (multiplier 1 if there is none).
/// <c>{local:Scale Name, OtherMultiplier}</c> names the multiplier explicitly.
/// Works for double and Thickness defaults. The constants dictionary is loaded once.
/// </summary>
public sealed class ScaleExtension : MarkupExtension
{
    private static readonly Lazy<ResourceDictionary> Constants = new(() => new ResourceDictionary
    {
        Source = new Uri(
            $"/{typeof(ScaleExtension).Assembly.GetName().Name};component/Pages/Measurement/CapturePanel/CapturePanelConstants.xaml",
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

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var multiplier = Multiplier();

        return Constants.Value[name] switch
        {
            double size => size * multiplier,
            Thickness t => new Thickness(t.Left * multiplier, t.Top * multiplier, t.Right * multiplier, t.Bottom * multiplier),
            _ => throw new InvalidOperationException($"CapturePanelConstants.xaml: '{name}' is missing or is not a double/Thickness.")
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

        throw new InvalidOperationException($"CapturePanelConstants.xaml: '{key}' is missing or is not a non-negative number.");
    }
}
