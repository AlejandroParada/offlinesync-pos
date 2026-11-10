using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfflineSync.Application.Sync;
using OfflineSync.Infrastructure.Persistence;

namespace OfflineSync.Infrastructure;

public static class InfrastructureServiceCollectionExtensions
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString)
    {
        services.AddDbContext<OfflineSyncDbContext>(options => options.UseNpgsql(connectionString));
        services.AddScoped<ISalesSyncStore, SalesSyncStore>();
        return services;
    }
}
