using System.Globalization;
using DicomAgent.Desktop.Converters;
using Xunit;

namespace DicomAgent.IntegrationTests;

public sealed class DesktopLocalizationTests
{
    [Theory]
    [InlineData("new", "Mới")]
    [InlineData("uploading", "Đang gửi ảnh")]
    [InlineData("done", "Hoàn tất")]
    [InlineData("error", "Lỗi")]
    [InlineData("Running", "Đang chạy")]
    [InlineData("Stopped", "Đã dừng")]
    [InlineData("Error", "Lỗi")]
    [InlineData("Disconnected", "Chưa kết nối")]
    [InlineData("Error / Disconnected", "Lỗi / Mất kết nối")]
    [InlineData("future-status", "Không xác định")]
    [InlineData(null, "Không xác định")]
    public void StatusLabelsAreVietnamese(string? status, string expected)
    {
        Assert.Equal(expected, new StatusTextConverter().Convert(status, typeof(string), null, CultureInfo.InvariantCulture));
    }

    [Theory]
    [InlineData("App restarted during upload. Check SaaS attachments before allowing another attempt.",
        "Ứng dụng đã khởi động lại khi đang gửi ảnh. Kiểm tra ảnh trên SaaS trước khi cho phép gửi lại.")]
    [InlineData("Operator checked SaaS and allowed a new upload attempt.",
        "Người dùng đã kiểm tra trên SaaS và cho phép gửi lại ảnh.")]
    [InlineData("Upload result is uncertain. Check attachments on SaaS before allowing another attempt. Local images are retained.",
        "Chưa xác định được kết quả gửi ảnh. Kiểm tra ảnh trên SaaS trước khi cho phép gửi lại. Ảnh trên máy vẫn được giữ.")]
    [InlineData("Could not prepare upload. Check that all selected files and saved credentials are available. Local images are retained.",
        "Không thể chuẩn bị gửi ảnh. Kiểm tra các tệp đã chọn và thông tin xác thực đã lưu. Ảnh trên máy vẫn được giữ.")]
    [InlineData("SaaS rejected upload (HTTP 401). For expired credentials, re-send the same request from the web with its original Idempotency-Key.",
        "SaaS từ chối nhận ảnh (HTTP 401). Nếu thông tin xác thực đã hết hạn, gửi lại yêu cầu từ web với Idempotency-Key ban đầu.")]
    [InlineData("SaaS returned HTTP 500. Check attachments on SaaS before retrying; some files may have been accepted.",
        "SaaS trả về HTTP 500. Kiểm tra ảnh trên SaaS trước khi gửi lại; một số ảnh có thể đã được nhận.")]
    [InlineData("Unrecognized diagnostic", "Unrecognized diagnostic")]
    [InlineData(null, null)]
    public void UploadMessagesKeepRecoveryInstructionsAndHttpCodes(string? message, string? expected)
    {
        Assert.Equal(expected, new CaptureMessageConverter().Convert(message, typeof(string), null, CultureInfo.InvariantCulture));
    }
}
