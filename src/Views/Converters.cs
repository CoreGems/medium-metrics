using System.Globalization;
using System.Windows;
using System.Windows.Data;

namespace MediumMetrics.Views;

/// <summary>Formats a fractional ratio in [0,1] as a percentage string (e.g. 0.5 -> "50%").</summary>
public sealed class RatioToPercentConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is double d ? d.ToString("P0", culture) : "";

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Collapses an element when the bound string is null or empty.</summary>
public sealed class StringToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => string.IsNullOrEmpty(value as string) ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
