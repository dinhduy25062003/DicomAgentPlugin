using System.Globalization;
using Avalonia.Data;
using Avalonia.Data.Converters;

namespace DicomAgent.Desktop.Converters;

// Shows the panel whose tab index is selected and checks the matching sidebar radio button.
public sealed class TabVisibilityConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var target = parameter switch
        {
            int number => number,
            string text when int.TryParse(text, out var parsed) => parsed,
            _ => -1
        };
        return value is int index && index == target;
    }

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        var target = parameter switch
        {
            int number => number,
            string text when int.TryParse(text, out var parsed) => parsed,
            _ => -1
        };
        return value is true && target >= 0 ? target : BindingOperations.DoNothing;
    }
}
