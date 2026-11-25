using System.Globalization;
using System.Text.RegularExpressions;
using Avalonia.Data.Converters;

namespace DicomAgent.Desktop.Converters;

public sealed class CaptureMessageConverter : IValueConverter
{
    public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string text) return null;
        var translated = text switch
        {
            "App restarted during upload. Check SaaS attachments before allowing another attempt." =>
                "Ứng dụng đã khởi động lại khi đang gửi ảnh. Kiểm tra ảnh trên SaaS trước khi cho phép gửi lại.",
            "Operator checked SaaS and allowed a new upload attempt." =>
                "Người dùng đã kiểm tra trên SaaS và cho phép gửi lại ảnh.",
            "Upload result is uncertain. Check attachments on SaaS before allowing another attempt. Local images are retained." =>
                "Chưa xác định được kết quả gửi ảnh. Kiểm tra ảnh trên SaaS trước khi cho phép gửi lại. Ảnh trên máy vẫn được giữ.",
            "Could not prepare upload. Check that all selected files and saved credentials are available. Local images are retained." =>
                "Không thể chuẩn bị gửi ảnh. Kiểm tra các tệp đã chọn và thông tin xác thực đã lưu. Ảnh trên máy vẫn được giữ.",
            _ => null
        };
        if (translated is not null) return translated;

        var rejected = Regex.Match(text, @"^SaaS rejected upload \(HTTP ([0-9]{3})\)\. For expired credentials, re-send the same request from the web with its original Idempotency-Key\.$");
        if (rejected.Success)
            return $"SaaS từ chối nhận ảnh (HTTP {rejected.Groups[1].Value}). Nếu thông tin xác thực đã hết hạn, gửi lại yêu cầu từ web với Idempotency-Key ban đầu.";
        var uncertain = Regex.Match(text, @"^SaaS returned HTTP ([0-9]{3})\. Check attachments on SaaS before retrying; some files may have been accepted\.$");
        if (uncertain.Success)
            return $"SaaS trả về HTTP {uncertain.Groups[1].Value}. Kiểm tra ảnh trên SaaS trước khi gửi lại; một số ảnh có thể đã được nhận.";

        // Preserve unfamiliar diagnostics rather than guessing their meaning.
        return text;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture) => throw new NotSupportedException();
}
