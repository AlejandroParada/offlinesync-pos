using Microsoft.EntityFrameworkCore;

namespace OfflineSync.Infrastructure.Persistence;

public sealed class OfflineSyncDbContext(DbContextOptions<OfflineSyncDbContext> options) : DbContext(options)
{
    public DbSet<ProductRecord> Products => Set<ProductRecord>();

    public DbSet<SaleRecord> Sales => Set<SaleRecord>();

    public DbSet<SaleLineRecord> SaleLines => Set<SaleLineRecord>();

    public DbSet<StockDiscrepancyRecord> StockDiscrepancies => Set<StockDiscrepancyRecord>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var product = modelBuilder.Entity<ProductRecord>();
        product.ToTable("products");
        product.HasKey(x => x.Id);
        product.Property(x => x.Id).HasColumnName("id");
        product.Property(x => x.Sku).HasColumnName("sku").HasMaxLength(32);
        product.Property(x => x.Name).HasColumnName("name").HasMaxLength(120);
        product.Property(x => x.QuantityOnHand).HasColumnName("quantity_on_hand");
        product.HasIndex(x => x.Sku).IsUnique().HasDatabaseName("ux_products_sku");
        product.Property<uint>("xmin").HasColumnType("xid").IsRowVersion();

        var sale = modelBuilder.Entity<SaleRecord>();
        sale.ToTable("sales");
        sale.HasKey(x => x.Id);
        sale.Property(x => x.Id).HasColumnName("id");
        sale.Property(x => x.IdempotencyKey).HasColumnName("idempotency_key");
        sale.Property(x => x.BranchId).HasColumnName("branch_id").HasMaxLength(64);
        sale.Property(x => x.OccurredAt).HasColumnName("occurred_at");
        sale.Property(x => x.ReceivedAt).HasColumnName("received_at");
        sale.HasIndex(x => x.IdempotencyKey).IsUnique().HasDatabaseName("ux_sales_idempotency_key");
        sale.HasMany(x => x.Lines)
            .WithOne()
            .HasForeignKey(x => x.SaleId)
            .OnDelete(DeleteBehavior.Cascade);

        var line = modelBuilder.Entity<SaleLineRecord>();
        line.ToTable("sale_lines");
        line.HasKey(x => x.Id);
        line.Property(x => x.Id).HasColumnName("id");
        line.Property(x => x.SaleId).HasColumnName("sale_id");
        line.Property(x => x.ProductId).HasColumnName("product_id");
        line.Property(x => x.Quantity).HasColumnName("quantity");
        line.Property(x => x.UnitPrice).HasColumnName("unit_price").HasPrecision(12, 2);
        line.HasOne<ProductRecord>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);

        var discrepancy = modelBuilder.Entity<StockDiscrepancyRecord>();
        discrepancy.ToTable("stock_discrepancies");
        discrepancy.HasKey(x => x.Id);
        discrepancy.Property(x => x.Id).HasColumnName("id");
        discrepancy.Property(x => x.SaleId).HasColumnName("sale_id");
        discrepancy.Property(x => x.ProductId).HasColumnName("product_id");
        discrepancy.Property(x => x.QuantityOnHand).HasColumnName("quantity_on_hand");
        discrepancy.Property(x => x.Reason).HasColumnName("reason").HasMaxLength(40);
        discrepancy.Property(x => x.CreatedAt).HasColumnName("created_at");
        discrepancy.HasIndex(x => x.SaleId).HasDatabaseName("ix_stock_discrepancies_sale_id");
        discrepancy.HasOne<SaleRecord>()
            .WithMany()
            .HasForeignKey(x => x.SaleId)
            .OnDelete(DeleteBehavior.Cascade);
        discrepancy.HasOne<ProductRecord>()
            .WithMany()
            .HasForeignKey(x => x.ProductId)
            .OnDelete(DeleteBehavior.Restrict);
    }
}
