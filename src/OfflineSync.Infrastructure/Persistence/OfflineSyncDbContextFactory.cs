using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace OfflineSync.Infrastructure.Persistence;

public sealed class OfflineSyncDbContextFactory : IDesignTimeDbContextFactory<OfflineSyncDbContext>
{
    public OfflineSyncDbContext CreateDbContext(string[] args)
    {
        var options = new DbContextOptionsBuilder<OfflineSyncDbContext>()
            .UseNpgsql("Host=localhost;Port=5432;Database=offlinesync;Username=offlinesync;Password=offlinesync")
            .Options;

        return new OfflineSyncDbContext(options);
    }
}
