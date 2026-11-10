namespace OfflineSync.Infrastructure.Persistence;

public sealed class ProductRecord
{
    public Guid Id { get; set; }

    public string Sku { get; set; } = "";

    public string Name { get; set; } = "";

    public int QuantityOnHand { get; set; }
}

public sealed class SaleRecord
{
    public Guid Id { get; set; }

    public Guid IdempotencyKey { get; set; }

    public string BranchId { get; set; } = "";

    public DateTimeOffset OccurredAt { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public List<SaleLineRecord> Lines { get; set; } = [];
}

public sealed class SaleLineRecord
{
    public Guid Id { get; set; }

    public Guid SaleId { get; set; }

    public Guid ProductId { get; set; }

    public int Quantity { get; set; }

    public decimal UnitPrice { get; set; }
}

public sealed class StockDiscrepancyRecord
{
    public Guid Id { get; set; }

    public Guid SaleId { get; set; }

    public Guid ProductId { get; set; }

    public int QuantityOnHand { get; set; }

    public string Reason { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }
}
