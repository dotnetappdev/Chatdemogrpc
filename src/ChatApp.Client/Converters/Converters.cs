using System.Globalization;
using System.Windows;
using System.Windows.Data;
using System.Windows.Markup;

namespace ChatApp.Client.Converters;

/// <summary>
/// Converts bool → Visibility and can be used as both a MarkupExtension and an IValueConverter.
/// <para>Default:          true → Visible,   false → Collapsed</para>
/// <para>ConverterParameter="inverse" or "neg": true → Collapsed, false → Visible</para>
/// </summary>
[ValueConversion(typeof(bool), typeof(Visibility))]
public class BoolToVisibilityConverter : MarkupExtension, IValueConverter
{
    // Singleton instances used from XAML as converter resources
    public static readonly BoolToVisibilityConverter Instance        = new();
    public static readonly BoolToVisibilityConverter InverseInstance = new() { Inverse = true };
    public static readonly BoolToVisibilityConverter NegInstance     = InverseInstance;

    /// <summary>When true, the logic is flipped: true → Collapsed, false → Visible.</summary>
    public bool Inverse { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider) => this;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var flag = value is bool b && b;

        // Support ConverterParameter="inverse" / "neg" to flip on the spot
        var param = parameter?.ToString()?.ToLowerInvariant();
        var invert = Inverse || param is "inverse" or "neg";

        if (invert) flag = !flag;
        return flag ? Visibility.Visible : Visibility.Collapsed;
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var vis = value is Visibility v && v == Visibility.Visible;
        var param = parameter?.ToString()?.ToLowerInvariant();
        return Inverse || param is "inverse" or "neg" ? !vis : vis;
    }
}

/// <summary>
/// Collapses an element when the integer value is zero (used for unread-message badges).
/// </summary>
[ValueConversion(typeof(int), typeof(Visibility))]
public class ZeroToCollapsedConverter : MarkupExtension, IValueConverter
{
    public static readonly ZeroToCollapsedConverter Instance = new();

    public override object ProvideValue(IServiceProvider serviceProvider) => this;

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        value is int n && n > 0 ? Visibility.Visible : Visibility.Collapsed;

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
        throw new NotSupportedException();
}
