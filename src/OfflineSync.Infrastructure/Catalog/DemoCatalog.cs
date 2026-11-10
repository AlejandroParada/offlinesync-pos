using Microsoft.EntityFrameworkCore;
using OfflineSync.Infrastructure.Persistence;

namespace OfflineSync.Infrastructure.Catalog;

public static class DemoCatalog
{
    public static readonly Guid Ibuprofen = Guid.Parse("11111111-1111-1111-1111-111111111111");
    public static readonly Guid Amoxicillin = Guid.Parse("22222222-2222-2222-2222-222222222222");
    public static readonly Guid AlcoholGel = Guid.Parse("33333333-3333-3333-3333-333333333333");

    public static async Task EnsureAsync(OfflineSyncDbContext db, CancellationToken cancellationToken)
    {
        if (await db.Products.AnyAsync(cancellationToken).ConfigureAwait(false))
        {
            return;
        }

        db.Products.AddRange(
            new ProductRecord
            {
                Id = Ibuprofen,
                Sku = "MED-IBU-400",
                Name = "Ibuprofeno 400 mg",
                QuantityOnHand = 20
            },
            new ProductRecord
            {
                Id = Amoxicillin,
                Sku = "MED-AMX-500",
                Name = "Amoxicilina 500 mg",
                QuantityOnHand = 8
            },
            new ProductRecord
            {
                Id = AlcoholGel,
                Sku = "HIG-ALC-250",
                Name = "Alcohol en gel 250 ml",
                QuantityOnHand = 2
            });

        await db.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }
}
