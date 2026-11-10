using OfflineSync.Application.Sync;
using OfflineSync.Domain;
using OfflineSync.Domain.Inventory;
using OfflineSync.Domain.Sales;

namespace OfflineSync.Application.Tests;

public class SyncSalesHandlerTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 15, 0, 0, TimeSpan.Zero);
    private readonly Guid _product = Guid.NewGuid();
    private readonly FakeSalesSyncStore _store = new();
    private readonly SyncSalesHandler _handler;

    public SyncSalesHandlerTests()
    {
        _store.Seed(_product, 20);
        _handler = new SyncSalesHandler(_store, new FixedClock(Now));
    }

    [Fact]
    public async Task Replaying_a_sale_does_not_apply_it_twice()
    {
        var sale = Sale(Guid.NewGuid(), _product, 1);

        var first = await _handler.Handle(new SyncSalesCommand([sale]), CancellationToken.None);
        var second = await _handler.Handle(new SyncSalesCommand([sale]), CancellationToken.None);

        Assert.Equal(SyncItemStatus.Applied, first.Results[0].Status);
        Assert.Equal(SyncItemStatus.Duplicate, second.Results[0].Status);
        Assert.Equal(first.Results[0].SaleId, second.Results[0].SaleId);
        Assert.Equal(19, _store.Quantity(_product));
    }

    [Fact]
    public async Task Negative_stock_is_applied_and_reported()
    {
        _store.Seed(_product, 2);
        var sale = Sale(Guid.NewGuid(), _product, 3);

        var result = await _handler.Handle(new SyncSalesCommand([sale]), CancellationToken.None);

        Assert.Equal(SyncItemStatus.Applied, result.Results[0].Status);
        var discrepancy = Assert.Single(result.Results[0].Discrepancies);
        Assert.Equal(StockPolicy.NegativeStockReason, discrepancy.Reason);
        Assert.Equal(-1, discrepancy.QuantityOnHand);
        Assert.Equal(-1, _store.Quantity(_product));
    }

    [Fact]
    public async Task Two_lines_of_the_same_product_accumulate()
    {
        var sale = new IncomingSale(
            Guid.NewGuid(),
            "pocitos",
            Now.AddMinutes(-10),
            [new IncomingSaleLine(_product, 2, 10m), new IncomingSaleLine(_product, 3, 10m)]);

        var result = await _handler.Handle(new SyncSalesCommand([sale]), CancellationToken.None);

        Assert.Equal(SyncItemStatus.Applied, result.Results[0].Status);
        Assert.Equal(15, _store.Quantity(_product));
    }

    [Fact]
    public async Task Unknown_product_rejects_only_that_sale()
    {
        var good = Sale(Guid.NewGuid(), _product, 1);
        var bad = Sale(Guid.NewGuid(), Guid.NewGuid(), 1);

        var result = await _handler.Handle(new SyncSalesCommand([good, bad]), CancellationToken.None);

        Assert.Equal(SyncItemStatus.Applied, result.Results[0].Status);
        Assert.Equal(SyncItemStatus.Rejected, result.Results[1].Status);
        Assert.Equal(19, _store.Quantity(_product));
    }

    [Fact]
    public async Task A_repeated_key_inside_the_batch_applies_once()
    {
        var key = Guid.NewGuid();
        var result = await _handler.Handle(
            new SyncSalesCommand([Sale(key, _product, 1), Sale(key, _product, 4)]),
            CancellationToken.None);

        Assert.Equal(SyncItemStatus.Applied, result.Results[0].Status);
        Assert.Equal(SyncItemStatus.Rejected, result.Results[1].Status);
        Assert.Equal(19, _store.Quantity(_product));
    }

    [Fact]
    public async Task A_stock_conflict_leaves_the_balance_untouched()
    {
        _store.ConflictOnce = true;

        var result = await _handler.Handle(new SyncSalesCommand([Sale(Guid.NewGuid(), _product, 1)]), CancellationToken.None);

        Assert.Equal(SyncItemStatus.Conflict, result.Results[0].Status);
        Assert.Equal(20, _store.Quantity(_product));
    }

    [Fact]
    public async Task A_future_sale_is_rejected()
    {
        var sale = Sale(Guid.NewGuid(), _product, 1, Now.AddHours(2));

        var result = await _handler.Handle(new SyncSalesCommand([sale]), CancellationToken.None);

        Assert.Equal(SyncItemStatus.Rejected, result.Results[0].Status);
        Assert.Equal(20, _store.Quantity(_product));
    }

    [Fact]
    public async Task An_empty_batch_is_a_request_error()
    {
        var error = await Assert.ThrowsAsync<DomainException>(() =>
            _handler.Handle(new SyncSalesCommand([]), CancellationToken.None));

        Assert.Contains("lote", error.Message, StringComparison.OrdinalIgnoreCase);
    }

    private static IncomingSale Sale(Guid key, Guid product, int quantity, DateTimeOffset? occurredAt = null) =>
        new(key, "pocitos", occurredAt ?? Now.AddMinutes(-10), [new IncomingSaleLine(product, quantity, 100m)]);

    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FakeSalesSyncStore : ISalesSyncStore
    {
        private readonly Dictionary<Guid, int> _stock = [];
        private readonly Dictionary<Guid, RecordedSale> _sales = [];

        public bool ConflictOnce { get; set; }

        public void Seed(Guid productId, int quantity) => _stock[productId] = quantity;

        public int Quantity(Guid productId) => _stock[productId];

        public Task<RecordedSale?> FindAsync(Guid idempotencyKey, CancellationToken cancellationToken) =>
            Task.FromResult(_sales.TryGetValue(idempotencyKey, out var sale) ? sale : null);

        public Task<RecordedSale> ApplyAsync(Sale sale, CancellationToken cancellationToken)
        {
            if (ConflictOnce)
            {
                ConflictOnce = false;
                throw new StockConcurrencyException();
            }

            if (_sales.ContainsKey(sale.IdempotencyKey))
            {
                throw new DuplicateIdempotencyKeyException();
            }

            var working = new Dictionary<Guid, int>();
            foreach (var line in sale.Lines)
            {
                if (!_stock.ContainsKey(line.ProductId))
                {
                    throw new UnknownProductException($"Producto desconocido: {line.ProductId:D}.");
                }

                working.TryAdd(line.ProductId, _stock[line.ProductId]);
            }

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
                _stock[productId] = quantity;
            }

            var recorded = new RecordedSale(Guid.CreateVersion7(), sale.IdempotencyKey, discrepancies);
            _sales[sale.IdempotencyKey] = recorded;
            return Task.FromResult(recorded);
        }
    }
}
