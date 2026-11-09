using DicomAgent.Application.Abstractions;
using DicomAgent.Application.Models;
using DicomAgent.Domain.Entities;
using Microsoft.EntityFrameworkCore;
namespace DicomAgent.Infrastructure.Persistence;
public sealed class InstanceRepository(IDbContextFactory<AgentDbContext> factory) : IInstanceRepository
{
    public async Task<HashSet<string>> IndexedPathsAsync(string storageRoot, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return (await db.Instances.AsNoTracking().Where(x => x.StorageRoot == storageRoot).Select(x => x.FilePath).ToListAsync(ct)).ToHashSet(StringComparer.Ordinal);
    }
    public async Task InitializeAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        await db.Database.MigrateAsync(ct);
        await db.Database.ExecuteSqlRawAsync("PRAGMA journal_mode=WAL;", ct);
    }
    public async Task UpsertAsync(DicomInstance instance, CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        var old = await db.Instances.SingleOrDefaultAsync(x => x.StorageRoot == instance.StorageRoot && x.SopInstanceUid == instance.SopInstanceUid, ct);
        if (old is not null)
        {
            if (old.FileHash != instance.FileHash) throw new InvalidDataException("History hash conflict.");
            return;
        }
        db.Instances.Add(instance); await db.SaveChangesAsync(ct);
    }
    public async Task<HistoryItem[]> RecentAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return await db.Instances.AsNoTracking().OrderByDescending(x => x.ReceivedAtUtc).Take(100)
            .Select(x => new HistoryItem(x.ReceivedAtUtc, x.StudyInstanceUid, x.SeriesInstanceUid, x.SopInstanceUid, x.Modality, x.FileSize, x.FilePath)).ToArrayAsync(ct);
    }
    public async Task<(long Count, DateTime? Last)> SummaryAsync(CancellationToken ct)
    {
        await using var db = await factory.CreateDbContextAsync(ct);
        return (await db.Instances.LongCountAsync(ct), await db.Instances.MaxAsync(x => (DateTime?)x.ReceivedAtUtc, ct));
    }
}
