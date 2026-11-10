using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using OfflineSync.Infrastructure.Catalog;
using OfflineSync.Infrastructure.Persistence;

namespace OfflineSync.Infrastructure;

public static class DatabaseInitializer
{
    public static async Task ApplyAsync(IServiceProvider services, CancellationToken cancellationToken = default)
    {
        var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DatabaseInitializer));
        const int attempts = 5;
        for (var attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<OfflineSyncDbContext>();
                await db.Database.MigrateAsync(cancellationToken).ConfigureAwait(false);
                await DemoCatalog.EnsureAsync(db, cancellationToken).ConfigureAwait(false);
                logger.LogInformation("Base lista y catálogo de demostración verificado.");
                return;
            }
            catch (Exception exception) when (attempt < attempts && exception is not OperationCanceledException)
            {
                logger.LogWarning(exception, "Postgres todavía no acepta conexiones (intento {Attempt}).", attempt);
                await Task.Delay(TimeSpan.FromSeconds(attempt * 2), cancellationToken).ConfigureAwait(false);
            }
        }

        throw new InvalidOperationException("No se pudo preparar la base.");
    }
}
