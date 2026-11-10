using OfflineSync.BranchClient.Outbox;

var builder = Host.CreateApplicationBuilder(args);

builder.Services.AddHttpClient<SyncApi>((serviceProvider, client) =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    client.BaseAddress = new Uri(configuration["Sync:BaseUrl"] ?? "http://localhost:5080");
    client.Timeout = TimeSpan.FromSeconds(15);
});

builder.Services.AddSingleton(serviceProvider =>
{
    var configuration = serviceProvider.GetRequiredService<IConfiguration>();
    return new SqliteOutbox(configuration["Outbox:Path"] ?? "branch-outbox.db");
});

builder.Services.AddHostedService<OutboxSyncService>();

var host = builder.Build();
await host.RunAsync().ConfigureAwait(false);
