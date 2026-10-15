using System.Globalization;
using System.Windows.Data;

namespace Frontend.Shared.Infrastructure;

/// <summary>True when both bound values are the same object (e.g. a dropdown entry and the ViewModel's closest match).</summary>
public sealed class SameItemConverter : IMultiValueConverter
{
    public object Convert(object[] values, Type targetType, object? parameter, CultureInfo culture) =>
        values.Length == 2 && values[0] is not null && ReferenceEquals(values[0], values[1]);

    public object[] ConvertBack(object value, Type[] targetTypes, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
