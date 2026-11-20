using System.ComponentModel;
using System.Diagnostics;
using System.Text.Json;
using DicomAgent.Application.Models;
using DicomAgent.Application.Services;

namespace DicomAgent.Desktop.Services;

public sealed class InstalledSettingsUpdater
{
    public async Task ApplyAsync(AgentSettings settings)
    {
        var directory = Path.Combine(OperatingSystem.IsWindows() ? Path.GetTempPath() : "/tmp", "dicom-settings-" + Guid.NewGuid().ToString("N"));
        if (OperatingSystem.IsWindows()) Directory.CreateDirectory(directory);
        else Directory.CreateDirectory(directory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        var file = Path.Combine(directory, "settings.json");
        try
        {
            await File.WriteAllTextAsync(file, JsonSerializer.Serialize(settings));
            // New folders in the user's home should remain owned by that user.
            if (!string.IsNullOrEmpty(settings.Dicom.StoragePath))
            {
                try { Directory.CreateDirectory(settings.Dicom.StoragePath); }
                catch (UnauthorizedAccessException) { }
            }
            var host = OperatingSystem.IsWindows()
                ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "DicomAgent", "host", "DicomAgent.Host.exe")
                : "/opt/dicom-agent/host/DicomAgent.Host";
            if (!File.Exists(host)) throw new InvalidOperationException("Cài bộ cài Agent mới để dùng chức năng Lưu và áp dụng tự động.");
            var start = OperatingSystem.IsWindows()
                ? new ProcessStartInfo(host) { UseShellExecute = true, Verb = "runas", WindowStyle = ProcessWindowStyle.Hidden }
                : new ProcessStartInfo("/usr/bin/pkexec") { UseShellExecute = false };
            if (!OperatingSystem.IsWindows())
            {
                start.ArgumentList.Add("--disable-internal-agent");
                start.ArgumentList.Add(host);
            }
            start.ArgumentList.Add("--apply-settings");
            start.ArgumentList.Add(file);
            using var process = Process.Start(start) ?? throw new IOException("Không mở được hộp xác thực quản trị.");
            // Do not abandon an elevated transaction while it is replacing configuration/restarting the service.
            await process.WaitForExitAsync();
            if (!File.Exists(file + ".result"))
                throw new InvalidOperationException("Chưa áp dụng cấu hình. Hãy chấp nhận hộp xác thực quản trị và dùng đúng bộ cài mới.");
            var result = JsonSerializer.Deserialize<ManagementResponse>(await File.ReadAllTextAsync(file + ".result"));
            if (result?.Success != true) throw new InvalidOperationException(result?.Error ?? "Không áp dụng được cấu hình.");
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            while (true)
            {
                try
                {
                    var response = await new ManagementClient().SendAsync(new("status"), timeout.Token);
                    if (response.Success && response.Status is { } status &&
                        (!settings.Agent.EnableReceiver || status.State == "Running")) return;
                }
                catch (Exception ex) when (ex is HttpRequestException or IOException or JsonException) { }
                await Task.Delay(250, timeout.Token);
            }
        }
        catch (Win32Exception ex) when (ex.NativeErrorCode == 1223)
        {
            throw new InvalidOperationException("Đã hủy xác thực quản trị. Cấu hình chưa thay đổi.", ex);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }
}
