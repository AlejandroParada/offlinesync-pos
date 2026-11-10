using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using OfflineSync.Infrastructure.Catalog;
using OfflineSync.Infrastructure.Persistence;

namespace OfflineSync.Api.IntegrationTests;

[Collection("api")]
public sealed class SyncSalesTests(ApiFixture fixture)
{
    [DockerFact]
    public async Task Replaying_a_sale_does_not_change_stock_again()
    {
        var client = fixture.Factory!.CreateClient();
        var key = Guid.NewGuid();
        var before = await StockAsync(DemoCatalog.Ibuprofen);

        var first = await PostAsync(client, key, DemoCatalog.Ibuprofen, 1);
        var after = await StockAsync(DemoCatalog.Ibuprofen);
        var second = await PostAsync(client, key, DemoCatalog.Ibuprofen, 1);
        var replay = await StockAsync(DemoCatalog.Ibuprofen);

        Assert.Equal("applied", first.Status);
        Assert.Equal("duplicate", second.Status);
        Assert.Equal(first.SaleId, second.SaleId);
        Assert.Equal(before - 1, after);
        Assert.Equal(after, replay);
        Assert.Equal(1, await CountSalesAsync(key));
    }

    [DockerFact]
    public async Task An_offline_sale_can_leave_stock_negative()
    {
        var client = fixture.Factory!.CreateClient();
        var result = await PostAsync(client, Guid.NewGuid(), DemoCatalog.AlcoholGel, 3);

        Assert.Equal("applied", result.Status);
        var discrepancy = Assert.Single(result.Discrepancies);
        Assert.Equal("negative-stock", discrepancy.Reason);
        Assert.Equal(-1, discrepancy.QuantityOnHand);
        Assert.Equal(-1, await StockAsync(DemoCatalog.AlcoholGel));
    }

    [DockerFact]
    public async Task Two_parallel_posts_of_the_same_key_create_one_sale()
    {
        var client = fixture.Factory!.CreateClient();
        var key = Guid.NewGuid();
        var before = await StockAsync(DemoCatalog.Amoxicillin);
        var payload = Batch(key, DemoCatalog.Amoxicillin, 1);

        var responses = await Task.WhenAll(
            client.PostAsJsonAsync("/sync/sales", payload),
            client.PostAsJsonAsync("/sync/sales", payload));

        Assert.All(responses, response => Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode));
        Assert.Equal(before - 1, await StockAsync(DemoCatalog.Amoxicillin));
        Assert.Equal(1, await CountSalesAsync(key));
    }

    [DockerFact]
    public async Task Health_reports_the_database()
    {
        var client = fixture.Factory!.CreateClient();
        var response = await client.GetAsync("/health");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Contains("Healthy", body, StringComparison.OrdinalIgnoreCase);
    }

    private async Task<SyncItem> PostAsync(HttpClient client, Guid key, Guid productId, int quantity)
    {
        var response = await client.PostAsJsonAsync("/sync/sales", Batch(key, productId, quantity));
        response.EnsureSuccessStatusCode();
        var body = await response.Content.ReadFromJsonAsync<SyncBody>();
        return Assert.Single(body!.Results);
    }

    private static SaleBatch Batch(Guid key, Guid productId, int quantity) =>
        new([
            new SaleBody(
                key,
                "pocitos",
                new DateTimeOffset(2026, 11, 1, 15, 0, 0, TimeSpan.Zero),
                [new LineBody(productId, quantity, 100m)])
        ]);

    private async Task<int> StockAsync(Guid productId)
    {
        await using var scope = fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfflineSyncDbContext>();
        return await db.Products.Where(product => product.Id == productId)
            .Select(product => product.QuantityOnHand)
            .SingleAsync();
    }

    private async Task<int> CountSalesAsync(Guid key)
    {
        await using var scope = fixture.Factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<OfflineSyncDbContext>();
        return await db.Sales.CountAsync(sale => sale.IdempotencyKey == key);
    }

    private sealed record SaleBatch(IReadOnlyList<SaleBody> Sales);

    private sealed record SyncBody(IReadOnlyList<SyncItem> Results);

    private sealed record SyncItem(
        Guid IdempotencyKey,
        string Status,
        Guid? SaleId,
        IReadOnlyList<Discrepancy> Discrepancies,
        string? Detail);

    private sealed record Discrepancy(Guid ProductId, int QuantityOnHand, string Reason);

    private sealed record SaleBody(
        Guid IdempotencyKey,
        string BranchId,
        DateTimeOffset OccurredAt,
        IReadOnlyList<LineBody> Lines);

    private sealed record LineBody(Guid ProductId, int Quantity, decimal UnitPrice);
}
