using System.Diagnostics;
using DicomAgent.Application.Models;
using DicomAgent.Application.Services;

namespace DicomAgent.Infrastructure.Configuration;

// Invoked only by the installed, elevated Host command. Normal HTTP saves keep their existing behavior.
public sealed class InstalledSettingsApplier(string overrideFile, Func<string, string[], Task<string>>? execute = null, string? operatorUid = null, Func<Task>? verifyRunning = null)
{
    private readonly Func<string, string[], Task<string>> run = execute ?? RunAsync;

    public static string StorageDirectory(AgentSettings settings)
    {
        CaptureRules.Validate(settings.LocalApi);
        var errors = ConfigurationRules.Validate(settings.Dicom);
        if (errors.Length > 0) throw new ArgumentException(string.Join(" ", errors));
        var path = Path.GetFullPath(settings.Dicom.StoragePath.Length == 0 ? AgentPaths.DefaultStorage : settings.Dicom.StoragePath);
        if (path.Any(char.IsControl) || path.Contains(':') && !OperatingSystem.IsWindows())
            throw new ArgumentException("Thư mục lưu ảnh không được chứa ký tự điều khiển hoặc dấu hai chấm.");
        if (path == Path.GetPathRoot(path)) throw new ArgumentException("Chọn một thư mục riêng để lưu ảnh, không chọn gốc ổ đĩa.");
        var data = Path.GetFullPath(AgentPaths.DataDirectory);
        if (Within(path, data) && !Within(path, AgentPaths.DefaultStorage))
            throw new ArgumentException("Không dùng thư mục cấu hình/request của Agent làm thư mục ảnh.");
        if (!OperatingSystem.IsWindows())
        {
            var safe = new[] { "/home/", "/srv/", "/mnt/", "/media/" }.Any(prefix => path.StartsWith(prefix, StringComparison.Ordinal));
            if (!safe && !Within(path, AgentPaths.DefaultStorage))
                throw new ArgumentException("Chọn thư mục ảnh trong /home, /srv, /mnt, /media hoặc để trống để dùng mặc định.");
            if (path.StartsWith("/home/", StringComparison.Ordinal) && path.Split('/').Length < 4)
                throw new ArgumentException("Chọn thư mục con để lưu ảnh, không chọn cả thư mục cá nhân.");
        }
        else
        {
            var protectedRoots = new[]
            {
                Environment.GetFolderPath(Environment.SpecialFolder.Windows),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86)
            };
            if (protectedRoots.Any(root => root.Length > 0 && Within(path, root)))
                throw new ArgumentException("Không dùng thư mục hệ thống hoặc cài đặt ứng dụng làm thư mục ảnh.");
        }
        for (var item = new DirectoryInfo(path); item is not null; item = item.Parent)
            if (item.LinkTarget is not null) throw new ArgumentException("Không dùng liên kết tượng trưng trong đường dẫn lưu ảnh.");
        return path;
    }

    private static bool Within(string path, string root)
    {
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
        return path.Equals(root, comparison) || path.StartsWith(root + Path.DirectorySeparatorChar, comparison);
    }

    public static string LinuxStorageOverride(string path)
    {
        var quoted = "\"" + path.Replace("\\", "\\\\").Replace("\"", "\\\"").Replace("%", "%%") + "\"";
        var home = path.StartsWith("/home/", StringComparison.Ordinal)
            ? $"ProtectHome=tmpfs\nBindPaths={quoted}\n" : "";
        return $"# Managed by DICOM Agent settings\n[Service]\n{home}ReadWritePaths={quoted}\n";
    }

    public async Task ApplyAsync(AgentSettings settings)
    {
        var storage = StorageDirectory(settings);
        var previous = File.Exists(SettingsStore.FilePath) ? await File.ReadAllBytesAsync(SettingsStore.FilePath) : null;
        var previousOverride = File.Exists(overrideFile) ? await File.ReadAllBytesAsync(overrideFile) : null;
        var aclBackup = Path.Combine(AgentPaths.DataDirectory, ".settings-acl-" + Guid.NewGuid().ToString("N"));
        var stopped = false;
        try
        {
            Directory.CreateDirectory(storage);
            if (OperatingSystem.IsLinux())
            {
                var paths = new List<string> { storage };
                // Execute-only access permits traversal to this folder without exposing the home listing.
                for (var parent = Directory.GetParent(storage); parent is not null && parent.Parent is not null; parent = parent.Parent)
                    paths.Add(parent.FullName);
                var acl = await run("getfacl", ["-p", "--", .. paths]);
                await File.WriteAllTextAsync(aclBackup, acl);
                await run("setfacl", ["-m", "u:dicom-agent:rwx,d:u:dicom-agent:rwx", "--", storage]);
                if (uint.TryParse(operatorUid, out var uid) && uid > 0)
                    await run("setfacl", ["-m", $"u:{uid}:rx,d:u:{uid}:rx", "--", storage]);
                foreach (var parent in paths.Skip(1))
                    await run("setfacl", ["-m", "u:dicom-agent:x", "--", parent]);
                Directory.CreateDirectory(Path.GetDirectoryName(overrideFile)!);
                await File.WriteAllTextAsync(overrideFile, LinuxStorageOverride(storage));
                await run("systemctl", ["daemon-reload"]);
            }
            else if (OperatingSystem.IsWindows())
            {
                await run("icacls.exe", [storage, "/save", aclBackup]);
                await run("icacls.exe", [storage, "/grant", "NT SERVICE\\DicomAgent:(OI)(CI)M"]);
            }
            // Stop drains existing associations before changing the persisted configuration.
            stopped = true;
            await ServiceAsync(false);
            await SettingsStore.WriteAsync(settings, CancellationToken.None);
            await FixSettingsOwnerAsync();
            await ServiceAsync(true);
            if (verifyRunning is not null) await verifyRunning();
        }
        catch (Exception original)
        {
            try
            {
                if (stopped)
                {
                    await ServiceAsync(false);
                    if (previous is not null) await File.WriteAllBytesAsync(SettingsStore.FilePath, previous);
                    else if (File.Exists(SettingsStore.FilePath)) File.Delete(SettingsStore.FilePath);
                    await FixSettingsOwnerAsync();
                }
                if (OperatingSystem.IsLinux())
                {
                    if (previousOverride is not null) await File.WriteAllBytesAsync(overrideFile, previousOverride);
                    else if (File.Exists(overrideFile)) File.Delete(overrideFile);
                    if (File.Exists(aclBackup)) await run("setfacl", ["--restore=" + aclBackup]);
                    await run("systemctl", ["daemon-reload"]);
                }
                else if (OperatingSystem.IsWindows() && File.Exists(aclBackup))
                {
                    await run("icacls.exe", [Directory.GetParent(storage)!.FullName, "/restore", aclBackup]);
                }
                if (stopped) await ServiceAsync(true);
            }
            catch (Exception rollback)
            {
                throw new IOException("Không áp dụng được cấu hình và chưa khôi phục được dịch vụ. Cấu hình/ảnh cũ được giữ lại; kiểm tra trạng thái dịch vụ.", new AggregateException(original, rollback));
            }
            throw new IOException("Không áp dụng được cấu hình. Đã khôi phục cấu hình trước đó. " + original.Message, original);
        }
        finally
        {
            if (File.Exists(aclBackup)) File.Delete(aclBackup);
        }
    }

    private async Task FixSettingsOwnerAsync()
    {
        if (OperatingSystem.IsLinux() && File.Exists(SettingsStore.FilePath))
            await run("chown", ["dicom-agent:dicom-agent-operators", "--", SettingsStore.FilePath]);
    }

    private async Task ServiceAsync(bool start)
    {
        if (OperatingSystem.IsLinux()) await run("systemctl", [start ? "start" : "stop", "dicom-agent.service"]);
        else
        {
            var script = start
                ? "Start-Service DicomAgent; (Get-Service DicomAgent).WaitForStatus('Running', [TimeSpan]::FromSeconds(60))"
                : "Stop-Service DicomAgent -ErrorAction Stop; (Get-Service DicomAgent).WaitForStatus('Stopped', [TimeSpan]::FromSeconds(60))";
            await run("powershell.exe", ["-NoProfile", "-NonInteractive", "-Command", "$ErrorActionPreference='Stop'; " + script]);
        }
    }

    private static async Task<string> RunAsync(string command, string[] arguments)
    {
        var executable = OperatingSystem.IsLinux() ? "/usr/bin/" + command
            : command == "powershell.exe" ? Path.Combine(Environment.SystemDirectory, "WindowsPowerShell", "v1.0", command)
            : Path.Combine(Environment.SystemDirectory, command);
        var start = new ProcessStartInfo(executable) { UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true };
        foreach (var argument in arguments) start.ArgumentList.Add(argument);
        using var process = Process.Start(start) ?? throw new IOException("Không chạy được công cụ cấu hình hệ thống.");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(90));
        try { await process.WaitForExitAsync(timeout.Token); }
        catch (OperationCanceledException)
        {
            process.Kill(true);
            throw new IOException("Hệ điều hành mất quá lâu để áp dụng cấu hình.");
        }
        await error;
        if (process.ExitCode != 0) throw new IOException($"Không thực hiện được {command} (mã lỗi {process.ExitCode}).");
        return await output;
    }
}
