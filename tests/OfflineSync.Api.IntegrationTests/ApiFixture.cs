using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfflineSync.Infrastructure.Catalog;
using OfflineSync.Infrastructure.Persistence;
using Testcontainers.PostgreSql;

namespace OfflineSync.Api.IntegrationTests;

public sealed class ApiFixture : IAsyncLifetime
{
    private PostgreSqlContainer? _container;
    private HttpClient? _warmup;

    public OfflineSyncApiFactory? Factory { get; private set; }

    public async Task InitializeAsync()
    {
        if (!DockerProbe.IsAvailable())
        {
            return;
        }

        _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
        await _container.StartAsync();
        Factory = new OfflineSyncApiFactory(_container.GetConnectionString());
        _warmup = Factory.CreateClient();
        var health = await _warmup.GetAsync("/health");
        health.EnsureSuccessStatusCode();
    }

    public async Task DisposeAsync()
    {
        _warmup?.Dispose();
        if (Factory is not null)
        {
            await Factory.DisposeAsync();
        }

        if (_container is not null)
        {
            await _container.DisposeAsync();
        }
    }
}

public sealed class OfflineSyncApiFactory(string connectionString) : Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory<Program>
{
    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder)
    {
        Environment.SetEnvironmentVariable("ConnectionStrings__Postgres", connectionString);
        builder.UseEnvironment("Development");
        builder.ConfigureServices(services => PostgresServiceReplacement.UsePostgres(services, connectionString));
    }
}

internal static class PostgresServiceReplacement
{
    public static void UsePostgres(IServiceCollection services, string connectionString)
    {
        var context = typeof(OfflineSyncDbContext);
        var remove = services.Where(descriptor =>
                descriptor.ServiceType == context
                || descriptor.ServiceType == typeof(DbContextOptions<OfflineSyncDbContext>)
                || descriptor.ServiceType == typeof(DbContextOptions)
                || (descriptor.ServiceType.IsGenericType
                    && descriptor.ServiceType.Name.StartsWith("IDbContextOptionsConfiguration", StringComparison.Ordinal)
                    && descriptor.ServiceType.GenericTypeArguments.Contains(context)))
            .ToList();

        foreach (var descriptor in remove)
        {
            services.Remove(descriptor);
        }

        services.AddDbContext<OfflineSyncDbContext>(options => options.UseNpgsql(connectionString));
    }
}

internal static class DockerProbe
{
    private static readonly Lazy<bool> Available = new(Detect);

    public static bool IsAvailable() => Available.Value;

    private static bool Detect()
    {
        try
        {
            using var process = Process.Start(new ProcessStartInfo("docker", "info")
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            });
            if (process is null)
            {
                return false;
            }

            var readOut = process.StandardOutput.ReadToEndAsync();
            var readErr = process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(15000))
            {
                try
                {
                    process.Kill(entireProcessTree: true);
                }
                catch (InvalidOperationException)
                {
                    // El proceso ya había terminado.
                }

                return false;
            }

            Task.WaitAll(readOut, readErr);
            return process.ExitCode == 0;
        }
        catch (Exception)
        {
            return false;
        }
    }
}

public sealed class DockerFactAttribute : FactAttribute
{
    public DockerFactAttribute()
    {
        if (!DockerProbe.IsAvailable())
        {
            Skip = "Docker no está disponible; el test de integración se omite.";
        }
    }
}

[CollectionDefinition("api")]
public sealed class ApiCollection : ICollectionFixture<ApiFixture>;
