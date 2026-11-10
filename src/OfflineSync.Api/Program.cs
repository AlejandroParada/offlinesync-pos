using System.Text.Json;
using System.Text.Json.Serialization;
using OfflineSync.Api;
using OfflineSync.Api.Sync;
using OfflineSync.Application.Sync;
using OfflineSync.Infrastructure;
using OfflineSync.Infrastructure.Persistence;
using Serilog;
using Serilog.Formatting.Compact;

Log.Logger = new LoggerConfiguration()
    .WriteTo.Console()
    .CreateBootstrapLogger();

try
{
    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((context, _, configuration) =>
    {
        configuration
            .MinimumLevel.Information()
            .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
            .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithProperty("Application", "OfflineSync.Api");

        if (context.HostingEnvironment.IsDevelopment())
        {
            configuration.WriteTo.Console();
        }
        else
        {
            configuration.WriteTo.Console(new CompactJsonFormatter());
        }
    });

    var connectionString = builder.Configuration.GetConnectionString("Postgres")
        ?? throw new InvalidOperationException("Falta ConnectionStrings:Postgres.");

    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddScoped<SyncSalesHandler>();
    builder.Services.AddInfrastructure(connectionString);
    builder.Services.AddHealthChecks()
        .AddDbContextCheck<OfflineSyncDbContext>("postgres");
    builder.Services.ConfigureHttpJsonOptions(options =>
    {
        options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase));
    });
    builder.Services.AddOpenApi();

    var app = builder.Build();

    app.UseSerilogRequestLogging();
    app.UseOfflineSyncErrors();
    await DatabaseInitializer.ApplyAsync(app.Services).ConfigureAwait(false);

    app.MapGet("/", () => Results.Ok(new
    {
        name = "OfflineSync POS",
        health = "/health",
        openapi = "/openapi/v1.json",
        syncSales = "POST /sync/sales"
    }));
    app.MapHealthChecks("/health");
    app.MapOpenApi();
    app.MapSyncEndpoints();

    app.Lifetime.ApplicationStarted.Register(() => Log.Information("OfflineSync POS listo."));
    app.Run();
}
catch (Exception exception)
{
    Log.Fatal(exception, "La API se detuvo por un error no controlado.");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program;
