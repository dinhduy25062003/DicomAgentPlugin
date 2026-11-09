using DicomAgent.Application.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace DicomAgent.Infrastructure.Persistence;
public sealed class AgentDbContextFactory : IDesignTimeDbContextFactory<AgentDbContext>
{
    public AgentDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<AgentDbContext>()
        .UseSqlite($"Data Source={Path.Combine(AgentPaths.DataDirectory, "history.db")}").Options);
}
