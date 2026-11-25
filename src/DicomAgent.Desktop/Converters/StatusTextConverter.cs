using System.Globalization;
using Avalonia.Data.Converters;

namespace DicomAgent.Desktop.Converters;

public sealed class StatusTextConverter : IValueConverter
{
    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture) => value switch
    {
        "new" => "Mới nhận",
        "uploading" => "Đang gửi ảnh",
        "done" => "Đã gửi ảnh",
        "review" => "Cần kiểm tra",
        "Running" => "Đang chạy",
        "Stopped" => "Đã dừng",
        "Error" => "Lỗi",
        "Disconnected" => "Chưa kết nối",
        "Error / Disconnected" => "Lỗi / Mất kết nối",
        _ => "Không xác định"
    };

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
