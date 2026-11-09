using DicomAgent.Application.Abstractions;
using DicomAgent.Application.Models;
using DicomAgent.Application.Services;
using DicomAgent.Infrastructure.Configuration;
using DicomAgent.Infrastructure.Dicom;
using DicomAgent.Infrastructure.Persistence;
using DicomAgent.Infrastructure.Storage;
using FellowOakDicom;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
namespace DicomAgent.Infrastructure;
public static class DependencyInjection
{
    public static IServiceCollection AddAgent(this IServiceCollection services, IConfiguration configuration)
    {
        services.AddSingleton<IValidateOptions<DicomOptions>, DicomOptionsValidator>();
        services.AddOptions<DicomOptions>().Bind(configuration.GetSection("Dicom")).ValidateOnStart();
        services.AddOptions<AgentOptions>().Bind(configuration.GetSection("Agent"));
        // Immutable runtime snapshots; saved configuration applies after service restart.
        services.AddSingleton(sp =>
        {
            var snapshot = System.Text.Json.JsonSerializer.Deserialize<DicomOptions>(System.Text.Json.JsonSerializer.Serialize(sp.GetRequiredService<IOptionsMonitor<DicomOptions>>().CurrentValue))!;
            if (string.IsNullOrEmpty(snapshot.StoragePath)) snapshot.StoragePath = AgentPaths.DefaultStorage;
            return snapshot;
        });
        services.AddSingleton(sp => sp.GetRequiredService<IOptionsMonitor<AgentOptions>>().CurrentValue);
        services.AddSingleton<SettingsStore>(); services.AddSingleton<ReceiverState>();
        services.AddFellowOakDicom(null, o =>
        {
            o.LogDataPDUs = false; o.LogDimseDatasets = false; o.UseRemoteAEForLogName = false;
            o.RequestTimeout = TimeSpan.FromSeconds(30); o.MaxPDULength = 16384;
        });
        var connection = new SqliteConnectionStringBuilder { DataSource = Path.Combine(AgentPaths.DataDirectory, "history.db"), DefaultTimeout = 10 }.ToString();
        services.AddDbContextFactory<AgentDbContext>(o => o.UseSqlite(connection));
        services.AddSingleton<IInstanceRepository, InstanceRepository>();
        services.AddSingleton<InstanceStore>(); services.AddSingleton<ReceiverController>();
        return services;
    }
}
