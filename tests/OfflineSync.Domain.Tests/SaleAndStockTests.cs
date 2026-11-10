using OfflineSync.Domain;
using OfflineSync.Domain.Inventory;
using OfflineSync.Domain.Sales;

namespace OfflineSync.Domain.Tests;

public class StockPolicyTests
{
    [Fact]
    public void Keeps_a_non_negative_balance()
    {
        var outcome = StockPolicy.Apply(5, 2);

        Assert.Equal(3, outcome.QuantityOnHand);
        Assert.Equal(StockDecision.Accepted, outcome.Decision);
        Assert.False(outcome.HasDiscrepancy);
    }

    [Fact]
    public void Accepts_a_sale_that_drives_stock_negative()
    {
        var outcome = StockPolicy.Apply(2, 3);

        Assert.Equal(-1, outcome.QuantityOnHand);
        Assert.Equal(StockDecision.AcceptedWithDiscrepancy, outcome.Decision);
        Assert.Equal("negative-stock", StockPolicy.NegativeStockReason);
    }

    [Fact]
    public void Rejects_a_non_positive_quantity()
    {
        var error = Assert.Throws<DomainException>(() => StockPolicy.Apply(5, 0));

        Assert.Contains("cantidad", error.Message, StringComparison.OrdinalIgnoreCase);
    }
}

public class SaleTests
{
    private static readonly DateTimeOffset Now = new(2026, 11, 10, 15, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Requires_a_line_and_a_real_key()
    {
        var emptyKey = Assert.Throws<DomainException>(() =>
            Sale.Create(Guid.Empty, "pocitos", Now, [SaleLine.Create(Guid.NewGuid(), 1, 10m)], Now));
        var noLines = Assert.Throws<DomainException>(() =>
            Sale.Create(Guid.NewGuid(), "pocitos", Now, [], Now));

        Assert.Contains("idempotencia", emptyKey.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("línea", noLines.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void Rejects_a_negative_price_and_a_future_timestamp()
    {
        var price = Assert.Throws<DomainException>(() => SaleLine.Create(Guid.NewGuid(), 1, -1m));
        var future = Assert.Throws<DomainException>(() => Sale.Create(
            Guid.NewGuid(),
            "pocitos",
            Now.AddHours(2),
            [SaleLine.Create(Guid.NewGuid(), 1, 10m)],
            Now));

        Assert.Contains("precio", price.Message, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("futuro", future.Message, StringComparison.OrdinalIgnoreCase);
    }
}
