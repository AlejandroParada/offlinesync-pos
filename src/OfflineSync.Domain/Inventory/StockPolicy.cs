using OfflineSync.Domain;

namespace OfflineSync.Domain.Inventory;

public enum StockDecision
{
    Accepted,
    AcceptedWithDiscrepancy
}

public readonly record struct StockOutcome(int QuantityOnHand, StockDecision Decision)
{
    public bool HasDiscrepancy => Decision == StockDecision.AcceptedWithDiscrepancy;
}

public static class StockPolicy
{
    public const string NegativeStockReason = "negative-stock";

    /// <summary>
    /// La venta offline ya ocurrió en la sucursal. El stock central sigue a la realidad
    /// y puede quedar negativo; ese hecho se registra, no se rechaza la venta.
    /// </summary>
    public static StockOutcome Apply(int quantityOnHand, int quantitySold)
    {
        if (quantitySold <= 0)
        {
            throw new DomainException("La cantidad vendida tiene que ser mayor a cero.");
        }

        var after = checked(quantityOnHand - quantitySold);
        var decision = after < 0
            ? StockDecision.AcceptedWithDiscrepancy
            : StockDecision.Accepted;

        return new StockOutcome(after, decision);
    }
}
