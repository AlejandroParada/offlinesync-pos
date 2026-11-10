using OfflineSync.Domain.Sales;

namespace OfflineSync.Application.Sync;

public sealed record SyncSalesCommand(IReadOnlyList<IncomingSale> Sales);

public sealed record IncomingSale(
    Guid IdempotencyKey,
    string BranchId,
    DateTimeOffset OccurredAt,
    IReadOnlyList<IncomingSaleLine> Lines);

public sealed record IncomingSaleLine(Guid ProductId, int Quantity, decimal UnitPrice);

public enum SyncItemStatus
{
    Applied,
    Duplicate,
    Rejected,
    Conflict
}

public sealed record StockDiscrepancy(Guid ProductId, int QuantityOnHand, string Reason);

public sealed record RecordedSale(
    Guid SaleId,
    Guid IdempotencyKey,
    IReadOnlyList<StockDiscrepancy> Discrepancies);

public sealed record SyncBatchResult(IReadOnlyList<SaleSyncItemResult> Results);

public sealed record SaleSyncItemResult(
    Guid IdempotencyKey,
    SyncItemStatus Status,
    Guid? SaleId,
    IReadOnlyList<StockDiscrepancy> Discrepancies,
    string? Detail)
{
    public static SaleSyncItemResult Applied(RecordedSale sale) =>
        new(sale.IdempotencyKey, SyncItemStatus.Applied, sale.SaleId, sale.Discrepancies, null);

    public static SaleSyncItemResult Duplicate(RecordedSale sale) =>
        new(sale.IdempotencyKey, SyncItemStatus.Duplicate, sale.SaleId, sale.Discrepancies, null);

    public static SaleSyncItemResult Rejected(Guid idempotencyKey, string detail) =>
        new(idempotencyKey, SyncItemStatus.Rejected, null, [], detail);

    public static SaleSyncItemResult Conflict(Guid idempotencyKey, string detail) =>
        new(idempotencyKey, SyncItemStatus.Conflict, null, [], detail);
}

public interface ISalesSyncStore
{
    Task<RecordedSale?> FindAsync(Guid idempotencyKey, CancellationToken cancellationToken);

    Task<RecordedSale> ApplyAsync(Sale sale, CancellationToken cancellationToken);
}

public sealed class DuplicateIdempotencyKeyException : Exception
{
    public DuplicateIdempotencyKeyException()
        : base("La clave de idempotencia ya fue aplicada.")
    {
    }
}

public sealed class UnknownProductException : Exception
{
    public UnknownProductException(string message)
        : base(message)
    {
    }
}

public sealed class StockConcurrencyException : Exception
{
    public StockConcurrencyException()
        : base("El stock fue modificado por otra sincronización.")
    {
    }
}
