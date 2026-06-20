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

/// <summary>Visible when the bound bool is false (e.g. show "Sign in" only when signed out).</summary>
public sealed class InverseBooleanToVisibilityConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is true ? Visibility.Collapsed : Visibility.Visible;

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}

/// <summary>Formats an earnings delta (USD) with a sign: 1.5 -> "+$1.50", -0.2 -> "-$0.20", 0 -> "$0.00".</summary>
public sealed class EarningsDeltaConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object? parameter, CultureInfo culture)
        => value is decimal d ? d.ToString("+$0.00;-$0.00;$0.00", culture) : "";

    public object ConvertBack(object value, Type targetType, object? parameter, CultureInfo culture)
        => Binding.DoNothing;
}
