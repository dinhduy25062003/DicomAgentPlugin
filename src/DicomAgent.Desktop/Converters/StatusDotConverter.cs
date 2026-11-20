using System.Globalization;
using Avalonia.Media;
using Avalonia.Data.Converters;

namespace DicomAgent.Desktop.Converters;

// Colors the connection dot in the page header from the service status text.
public sealed class StatusDotConverter : IValueConverter
{
    private static readonly IBrush Running = new SolidColorBrush(Color.Parse("#3E9B6F"));
    private static readonly IBrush Stopped = new SolidColorBrush(Color.Parse("#71849A"));
    private static readonly IBrush Error = new SolidColorBrush(Color.Parse("#D05A66"));
    private static readonly IBrush Other = new SolidColorBrush(Color.Parse("#E8A33D"));

    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        "Running" => Running,
        "Stopped" => Stopped,
        string text when text.StartsWith("Error", StringComparison.Ordinal) => Error,
        _ => Other
    };

    public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
