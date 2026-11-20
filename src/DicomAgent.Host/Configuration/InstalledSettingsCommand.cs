using System.Runtime.InteropServices;
using System.Security.Principal;
using System.Text.Json;
using DicomAgent.Application.Models;
using DicomAgent.Application.Services;
using DicomAgent.Infrastructure.Configuration;

namespace DicomAgent.Host.Configuration;

public static class InstalledSettingsCommand
{
    public static async Task<int> RunAsync(string input)
    {
        ManagementResponse result;
        try
        {
            var administrator = OperatingSystem.IsWindows()
                ? new WindowsPrincipal(WindowsIdentity.GetCurrent()).IsInRole(WindowsBuiltInRole.Administrator)
                : geteuid() == 0;
            if (!administrator) throw new InvalidOperationException("Cần xác thực quản trị để áp dụng cấu hình.");
            ValidateInput(input);
            // Never accept an elevated data path from the caller's environment or input file.
            Environment.SetEnvironmentVariable("DICOM_AGENT_DATA", AgentPaths.DefaultDataDirectory(OperatingSystem.IsWindows(), Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData)));
            using var lease = new FileStream(Path.Combine(AgentPaths.DataDirectory, "settings-apply.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            if (new FileInfo(input).Length > 65536) throw new ArgumentException("Tệp cấu hình quá lớn.");
            var settings = JsonSerializer.Deserialize<AgentSettings>(await File.ReadAllTextAsync(input))
                ?? throw new ArgumentException("Tệp cấu hình không hợp lệ.");
            var captures = await new ManagementClient().SendAsync(new("captures"));
            if (!captures.Success) throw new InvalidOperationException("Không đọc được trạng thái Agent. Chưa thay đổi cấu hình.");
            if (captures.Captures!.Requests.Any(request => request.Status == "uploading"))
                throw new InvalidOperationException("Đang gửi ảnh lên SaaS. Đợi gửi xong trước khi lưu cấu hình.");
            await new InstalledSettingsApplier("/etc/systemd/system/dicom-agent.service.d/90-desktop-settings.conf", operatorUid: Environment.GetEnvironmentVariable("PKEXEC_UID"), verifyRunning: async () =>
            {
                var check = await new ManagementClient().SendAsync(new("storage-check"));
                if (!check.Success) throw new IOException("Dịch vụ không ghi được vào thư mục ảnh đã chọn.");
            }).ApplyAsync(settings);
            result = new(true);
        }
        catch (Exception ex)
        {
            result = new(false, ex.Message);
        }
        // CreateNew refuses an existing file/symlink at this user-controlled result path.
        await using (var stream = new FileStream(input + ".result", FileMode.CreateNew, FileAccess.Write, FileShare.None))
            await JsonSerializer.SerializeAsync(stream, result);
        if (OperatingSystem.IsLinux())
            File.SetUnixFileMode(input + ".result", UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.GroupRead | UnixFileMode.OtherRead);
        return result.Success ? 0 : 1;
    }

    public static void ValidateInput(string input)
    {
        var file = new FileInfo(Path.GetFullPath(input));
        var directory = file.Directory;
        if (file.Name != "settings.json" || directory is null || !directory.Name.StartsWith("dicom-settings-", StringComparison.Ordinal))
            throw new ArgumentException("Chỉ nhận tệp cấu hình tạm do giao diện Agent tạo.");
        if (OperatingSystem.IsLinux() && directory.Parent?.FullName != "/tmp")
            throw new ArgumentException("Tệp cấu hình tạm phải nằm trong /tmp.");
        if (file.LinkTarget is not null) throw new ArgumentException("Tệp cấu hình tạm không được là liên kết tượng trưng.");
        for (var parent = directory; parent is not null; parent = parent.Parent)
            if (parent.LinkTarget is not null) throw new ArgumentException("Thư mục cấu hình tạm không được qua liên kết tượng trưng.");
    }

    [DllImport("libc")] private static extern uint geteuid();
}
