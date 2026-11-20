using DicomAgent.Application.Services;
using DicomAgent.Application.Abstractions;
using DicomAgent.Infrastructure;
using DicomAgent.Infrastructure.Configuration;
using DicomAgent.Host.Api;
using DicomAgent.Infrastructure.Management;
using DicomAgent.Host.Workers;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

if (OperatingSystem.IsWindows()) Directory.CreateDirectory(AgentPaths.DataDirectory);
else Directory.CreateDirectory(AgentPaths.DataDirectory, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute | UnixFileMode.GroupRead | UnixFileMode.GroupExecute);
var receiveTemp = Path.Combine(AgentPaths.DataDirectory, "receive-temp");
Directory.CreateDirectory(receiveTemp);
if (!OperatingSystem.IsWindows()) File.SetUnixFileMode(receiveTemp, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
FellowOakDicom.IO.TemporaryFile.StoragePath = receiveTemp;
// Hold an exclusive process lease before starting the local HTTP API or touching storage.
using var lease = new FileStream(Path.Combine(AgentPaths.DataDirectory, "host.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { Args = args, ContentRootPath = AppContext.BaseDirectory });
builder.Configuration.AddJsonFile(SettingsStore.FilePath, optional: true, reloadOnChange: true);
// Explicit environment/CLI overrides have highest priority on startup.
builder.Configuration.AddEnvironmentVariables("DICOM_AGENT_"); builder.Configuration.AddCommandLine(args);
builder.Logging.AddFilter("FellowOakDicom", LogLevel.Critical);
builder.Logging.AddFilter("Microsoft.EntityFrameworkCore", LogLevel.Critical);
builder.Services.AddWindowsService(o => o.ServiceName = "DicomAgent");
builder.Services.AddSystemd();
builder.Services.Configure<HostOptions>(o => o.ShutdownTimeout = TimeSpan.FromSeconds(30));
builder.Services.AddSingleton<ICaptureRequestService, CaptureRequestService>();
builder.Services.AddSingleton<ManagementService>();
builder.Services.AddAgent(builder.Configuration);
builder.Services.AddHostedService<CaptureUploadWorker>();
builder.Services.AddHostedService<LocalApiServer>();
builder.Services.AddHostedService<DicomReceiverWorker>();

await builder.Build().RunAsync();
