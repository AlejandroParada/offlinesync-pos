using OfflineSync.Domain;

namespace OfflineSync.Domain.Sales;

public sealed class SaleLine
{
    private SaleLine(Guid productId, int quantity, decimal unitPrice)
    {
        ProductId = productId;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public Guid ProductId { get; }

    public int Quantity { get; }

    public decimal UnitPrice { get; }

    public static SaleLine Create(Guid productId, int quantity, decimal unitPrice)
    {
        if (productId == Guid.Empty)
        {
            throw new DomainException("El producto es obligatorio.");
        }

        if (quantity is <= 0 or > 100_000)
        {
            throw new DomainException("La cantidad tiene que estar entre 1 y 100000.");
        }

        if (unitPrice < 0)
        {
            throw new DomainException("El precio no puede ser negativo.");
        }

        return new SaleLine(productId, quantity, unitPrice);
    }
}

public sealed class Sale
{
    private readonly SaleLine[] _lines;

    private Sale(Guid idempotencyKey, string branchId, DateTimeOffset occurredAt, SaleLine[] lines)
    {
        IdempotencyKey = idempotencyKey;
        BranchId = branchId;
        OccurredAt = occurredAt;
        _lines = lines;
    }

    public Guid IdempotencyKey { get; }

    public string BranchId { get; }

    public DateTimeOffset OccurredAt { get; }

    public IReadOnlyList<SaleLine> Lines => _lines;

    public static Sale Create(
        Guid idempotencyKey,
        string branchId,
        DateTimeOffset occurredAt,
        IReadOnlyList<SaleLine> lines,
        DateTimeOffset utcNow)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (idempotencyKey == Guid.Empty)
        {
            throw new DomainException("La clave de idempotencia es obligatoria.");
        }

        if (string.IsNullOrWhiteSpace(branchId))
        {
            throw new DomainException("La sucursal es obligatoria.");
        }

        var normalizedBranch = branchId.Trim();
        if (normalizedBranch.Length > 64)
        {
            throw new DomainException("La sucursal admite hasta 64 caracteres.");
        }

        if (lines.Count == 0)
        {
            throw new DomainException("La venta tiene que tener al menos una línea.");
        }

        if (occurredAt > utcNow.AddMinutes(5))
        {
            throw new DomainException("La fecha de la venta no puede estar en el futuro.");
        }

        return new Sale(idempotencyKey, normalizedBranch, occurredAt, lines.ToArray());
    }
}
