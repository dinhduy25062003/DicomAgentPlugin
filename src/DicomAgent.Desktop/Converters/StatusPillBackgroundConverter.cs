using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Styling;

namespace DicomAgent.Desktop.Converters;

public sealed class StatusPillBackgroundConverter : IMultiValueConverter
{
    public object? Convert(IList<object?> values, Type targetType, object? parameter, CultureInfo culture)
    {
        var status = values.Count > 0 && values[0] is string name && name is "new" or "uploading" or "done" or "review" ? name : "unknown";
        var theme = values.Count > 1 && values[1] is ThemeVariant variant ? variant : ThemeVariant.Light;
        return Avalonia.Application.Current?.TryGetResource($"Brush.StatusBg.{status}", theme, out var brush) == true
            ? brush : Brushes.Transparent;
    }
}
