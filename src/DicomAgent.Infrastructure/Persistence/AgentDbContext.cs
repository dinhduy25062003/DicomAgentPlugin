using DicomAgent.Domain.Entities;
using Microsoft.EntityFrameworkCore;
namespace DicomAgent.Infrastructure.Persistence;
public sealed class AgentDbContext(DbContextOptions<AgentDbContext> options) : DbContext(options)
{
    public DbSet<DicomInstance> Instances => Set<DicomInstance>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<DicomInstance>().HasKey(x => x.Id);
        model.Entity<DicomInstance>().HasIndex(x => new { x.StorageRoot, x.SopInstanceUid }).IsUnique();
        model.Entity<DicomInstance>().HasIndex(x => x.ReceivedAtUtc);
    }
}
