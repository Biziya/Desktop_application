using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Frontend.Shared.Infrastructure;

/// <summary>Looks up an application resource by the key bound to it (sidebar icons by NavigationItem.IconKey).</summary>
public sealed class ResourceKeyConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string key ? Application.Current?.TryFindResource(key) : null;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
