using System.Collections;
using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace Frontend.Shared.Infrastructure;

/// <summary>False → Visible, true → Collapsed.</summary>
public sealed class InverseBoolVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Non-empty string → Visible, else Collapsed; reversed with <see cref="Invert"/>.</summary>
public sealed class NonEmptyVisibilityConverter : IValueConverter
{
    public bool Invert { get; set; }

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string { Length: > 0 } != Invert ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}

/// <summary>Zero count, empty list or null → Visible (empty-state text), else Collapsed.</summary>
public sealed class ZeroVisibilityConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value switch
        {
            int count => count == 0 ? Visibility.Visible : Visibility.Collapsed,
            ICollection items => items.Count == 0 ? Visibility.Visible : Visibility.Collapsed,
            null => Visibility.Visible,
            _ => Visibility.Collapsed
        };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
