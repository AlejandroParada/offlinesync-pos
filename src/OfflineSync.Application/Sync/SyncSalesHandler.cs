using OfflineSync.Domain;
using OfflineSync.Domain.Sales;

namespace OfflineSync.Application.Sync;

public sealed class SyncSalesHandler(ISalesSyncStore store, TimeProvider clock)
{
    public const int MaxBatchSize = 100;

    public async Task<SyncBatchResult> Handle(SyncSalesCommand command, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (command.Sales.Count == 0)
        {
            throw new DomainException("El lote tiene que traer al menos una venta.");
        }

        if (command.Sales.Count > MaxBatchSize)
        {
            throw new DomainException($"El lote admite hasta {MaxBatchSize} ventas.");
        }

        var seen = new HashSet<Guid>();
        var results = new List<SaleSyncItemResult>(command.Sales.Count);

        foreach (var incoming in command.Sales)
        {
            Sale sale;
            try
            {
                var lines = incoming.Lines
                    .Select(line => SaleLine.Create(line.ProductId, line.Quantity, line.UnitPrice))
                    .ToArray();
                sale = Sale.Create(
                    incoming.IdempotencyKey,
                    incoming.BranchId,
                    incoming.OccurredAt,
                    lines,
                    clock.GetUtcNow());
            }
            catch (DomainException exception)
            {
                results.Add(SaleSyncItemResult.Rejected(incoming.IdempotencyKey, exception.Message));
                continue;
            }

            if (!seen.Add(sale.IdempotencyKey))
            {
                results.Add(SaleSyncItemResult.Rejected(
                    sale.IdempotencyKey,
                    "La clave de idempotencia está repetida dentro del lote."));
                continue;
            }

            var existing = await store.FindAsync(sale.IdempotencyKey, cancellationToken).ConfigureAwait(false);
            if (existing is not null)
            {
                results.Add(SaleSyncItemResult.Duplicate(existing));
                continue;
            }

            try
            {
                var recorded = await store.ApplyAsync(sale, cancellationToken).ConfigureAwait(false);
                results.Add(SaleSyncItemResult.Applied(recorded));
            }
            catch (DuplicateIdempotencyKeyException)
            {
                var raced = await store.FindAsync(sale.IdempotencyKey, cancellationToken).ConfigureAwait(false);
                results.Add(raced is null
                    ? SaleSyncItemResult.Conflict(
                        sale.IdempotencyKey,
                        "La venta se estaba aplicando. Hay que reintentar.")
                    : SaleSyncItemResult.Duplicate(raced));
            }
            catch (UnknownProductException exception)
            {
                results.Add(SaleSyncItemResult.Rejected(sale.IdempotencyKey, exception.Message));
            }
            catch (StockConcurrencyException)
            {
                results.Add(SaleSyncItemResult.Conflict(
                    sale.IdempotencyKey,
                    "El stock cambió mientras se aplicaba la venta. Hay que reintentar."));
            }
        }

        return new SyncBatchResult(results);
    }
}
