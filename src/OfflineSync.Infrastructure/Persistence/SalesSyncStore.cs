using Microsoft.EntityFrameworkCore;
using OfflineSync.Application.Sync;
using OfflineSync.Domain.Inventory;
using OfflineSync.Domain.Sales;

namespace OfflineSync.Infrastructure.Persistence;

public sealed class SalesSyncStore(OfflineSyncDbContext db) : ISalesSyncStore
{
    public async Task<RecordedSale?> FindAsync(Guid idempotencyKey, CancellationToken cancellationToken)
    {
        var sale = await db.Sales
            .AsNoTracking()
            .Where(item => item.IdempotencyKey == idempotencyKey)
            .Select(item => new { item.Id, item.IdempotencyKey })
            .FirstOrDefaultAsync(cancellationToken)
            .ConfigureAwait(false);

        if (sale is null)
        {
            return null;
        }

        var discrepancies = await db.StockDiscrepancies
            .AsNoTracking()
            .Where(item => item.SaleId == sale.Id)
            .Select(item => new StockDiscrepancy(item.ProductId, item.QuantityOnHand, item.Reason))
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);

        return new RecordedSale(sale.Id, sale.IdempotencyKey, discrepancies);
    }

    public async Task<RecordedSale> ApplyAsync(Sale sale, CancellationToken cancellationToken)
    {
        db.ChangeTracker.Clear();
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var recorded = await ApplyCoreAsync(sale, cancellationToken).ConfigureAwait(false);
            await transaction.CommitAsync(cancellationToken).ConfigureAwait(false);
            return recorded;
        }
        catch
        {
            await transaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
            db.ChangeTracker.Clear();
            throw;
        }
    }

    private async Task<RecordedSale> ApplyCoreAsync(Sale sale, CancellationToken cancellationToken)
    {
        var exists = await db.Sales
            .AnyAsync(item => item.IdempotencyKey == sale.IdempotencyKey, cancellationToken)
            .ConfigureAwait(false);
        if (exists)
        {
            throw new DuplicateIdempotencyKeyException();
        }

        var productIds = sale.Lines.Select(line => line.ProductId).Distinct().ToArray();
        var products = await db.Products
            .Where(product => productIds.Contains(product.Id))
            .ToDictionaryAsync(product => product.Id, cancellationToken)
            .ConfigureAwait(false);

        var missing = productIds.FirstOrDefault(id => !products.ContainsKey(id));
        if (missing != Guid.Empty)
        {
            throw new UnknownProductException($"Producto desconocido: {missing:D}.");
        }

        // La política corre sobre la fila recién leída, dentro de la transacción.
        // xmin hace fallar el guardado si otra venta tocó el mismo producto: el cliente reintenta.
        var working = products.ToDictionary(pair => pair.Key, pair => pair.Value.QuantityOnHand);
        var discrepancies = new List<StockDiscrepancy>();
        foreach (var line in sale.Lines)
        {
            var outcome = StockPolicy.Apply(working[line.ProductId], line.Quantity);
            working[line.ProductId] = outcome.QuantityOnHand;
            if (outcome.HasDiscrepancy)
            {
                discrepancies.Add(new StockDiscrepancy(
                    line.ProductId,
                    outcome.QuantityOnHand,
                    StockPolicy.NegativeStockReason));
            }
        }

        foreach (var (productId, quantity) in working)
        {
            products[productId].QuantityOnHand = quantity;
        }

        var saleId = Guid.CreateVersion7();
        var receivedAt = DateTimeOffset.UtcNow;
        db.Sales.Add(new SaleRecord
        {
            Id = saleId,
            IdempotencyKey = sale.IdempotencyKey,
            BranchId = sale.BranchId,
            OccurredAt = sale.OccurredAt,
            ReceivedAt = receivedAt,
            Lines = sale.Lines.Select(line => new SaleLineRecord
            {
                Id = Guid.CreateVersion7(),
                SaleId = saleId,
                ProductId = line.ProductId,
                Quantity = line.Quantity,
                UnitPrice = line.UnitPrice
            }).ToList()
        });

        foreach (var discrepancy in discrepancies)
        {
            db.StockDiscrepancies.Add(new StockDiscrepancyRecord
            {
                Id = Guid.CreateVersion7(),
                SaleId = saleId,
                ProductId = discrepancy.ProductId,
                QuantityOnHand = discrepancy.QuantityOnHand,
                Reason = discrepancy.Reason,
                CreatedAt = receivedAt
            });
        }

        try
        {
            await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (PostgresErrors.IsUniqueViolation(exception))
        {
            throw new DuplicateIdempotencyKeyException();
        }
        catch (DbUpdateConcurrencyException)
        {
            throw new StockConcurrencyException();
        }

        return new RecordedSale(saleId, sale.IdempotencyKey, discrepancies);
    }
}
