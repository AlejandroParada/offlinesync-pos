using OfflineSync.Application.Sync;

namespace OfflineSync.Api.Sync;

public sealed record SyncSalesRequest(IReadOnlyList<SaleRequest>? Sales);

public sealed record SaleRequest(
    Guid IdempotencyKey,
    string? BranchId,
    DateTimeOffset OccurredAt,
    IReadOnlyList<SaleLineRequest>? Lines);

public sealed record SaleLineRequest(Guid ProductId, int Quantity, decimal UnitPrice);

public sealed record SyncSalesResponse(IReadOnlyList<SyncSaleResponse> Results);

public sealed record SyncSaleResponse(
    Guid IdempotencyKey,
    SyncItemStatus Status,
    Guid? SaleId,
    IReadOnlyList<StockDiscrepancyResponse> Discrepancies,
    string? Detail);

public sealed record StockDiscrepancyResponse(Guid ProductId, int QuantityOnHand, string Reason);

public static class SyncMapping
{
    public static SyncSalesCommand ToCommand(this SyncSalesRequest? request)
    {
        var sales = request?.Sales ?? [];
        return new SyncSalesCommand(sales.Select(sale => new IncomingSale(
            sale.IdempotencyKey,
            sale.BranchId ?? "",
            sale.OccurredAt,
            (sale.Lines ?? []).Select(line => new IncomingSaleLine(line.ProductId, line.Quantity, line.UnitPrice)).ToArray()))
            .ToArray());
    }

    public static SyncSalesResponse ToResponse(this SyncBatchResult result) =>
        new(result.Results.Select(item => new SyncSaleResponse(
            item.IdempotencyKey,
            item.Status,
            item.SaleId,
            item.Discrepancies.Select(discrepancy =>
                new StockDiscrepancyResponse(discrepancy.ProductId, discrepancy.QuantityOnHand, discrepancy.Reason)).ToArray(),
            item.Detail)).ToArray());
}
