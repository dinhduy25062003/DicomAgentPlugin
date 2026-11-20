using System.Globalization;
using Avalonia.Data.Converters;

namespace DicomAgent.Desktop.Converters;

// Hides the bottom message bar while there is nothing to show.
public sealed class MessageVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) =>
        value is string text && text.Trim().Length > 0;

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
