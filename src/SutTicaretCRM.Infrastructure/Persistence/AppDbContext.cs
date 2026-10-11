using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using SutTicaretCRM.Application;
using SutTicaretCRM.Domain;

namespace SutTicaretCRM.Infrastructure.Persistence;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options), IAppDbContext
{
    public DbSet<User> Users => Set<User>();
    public DbSet<AppSetting> AppSettings => Set<AppSetting>();
    public DbSet<NumberSequence> NumberSequences => Set<NumberSequence>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Supplier> Suppliers => Set<Supplier>();
    public DbSet<ProductCategory> ProductCategories => Set<ProductCategory>();
    public DbSet<Product> Products => Set<Product>();
    public DbSet<CustomerProductPrice> CustomerProductPrices => Set<CustomerProductPrice>();
    public DbSet<Sale> Sales => Set<Sale>();
    public DbSet<SaleItem> SaleItems => Set<SaleItem>();
    public DbSet<Invoice> Invoices => Set<Invoice>();
    public DbSet<InvoiceItem> InvoiceItems => Set<InvoiceItem>();
    public DbSet<InvoiceAttachment> InvoiceAttachments => Set<InvoiceAttachment>();
    public DbSet<CustomerLedgerEntry> CustomerLedgerEntries => Set<CustomerLedgerEntry>();
    public DbSet<SupplierLedgerEntry> SupplierLedgerEntries => Set<SupplierLedgerEntry>();
    public DbSet<Payment> Payments => Set<Payment>();
    public DbSet<PaymentAllocation> PaymentAllocations => Set<PaymentAllocation>();
    public DbSet<PurchaseDocument> PurchaseDocuments => Set<PurchaseDocument>();
    public DbSet<PurchaseItem> PurchaseItems => Set<PurchaseItem>();
    public DbSet<StockMovement> StockMovements => Set<StockMovement>();

    protected override void ConfigureConventions(ModelConfigurationBuilder c)
    {
        // Para: 18,2. Miktar/fiyat/oran alanları aşağıda ayrıca belirlenir.
        c.Properties<decimal>().HavePrecision(18, 2);
        // Enum'lar okunabilirlik ve güvenli sürümleme için metin olarak saklanır.
        c.Properties<CustomerType>().HaveConversion<string>().HaveMaxLength(40);
        c.Properties<EInvoiceTaxpayerStatus>().HaveConversion<string>().HaveMaxLength(40);
        c.Properties<PaymentType>().HaveConversion<string>().HaveMaxLength(40);
        c.Properties<SaleStatus>().HaveConversion<string>().HaveMaxLength(40);
        c.Properties<PaymentMethod>().HaveConversion<string>().HaveMaxLength(40);
        c.Properties<PaymentStatus>().HaveConversion<string>().HaveMaxLength(40);
        c.Properties<LedgerEntryType>().HaveConversion<string>().HaveMaxLength(40);
        c.Properties<LedgerSourceType>().HaveConversion<string>().HaveMaxLength(40);
        c.Properties<InvoiceDocumentType>().HaveConversion<string>().HaveMaxLength(40);
        c.Properties<InvoiceInternalStatus>().HaveConversion<string>().HaveMaxLength(40);
        c.Properties<OfficialInvoiceStatus>().HaveConversion<string>().HaveMaxLength(40);
        c.Properties<OfficialSource>().HaveConversion<string>().HaveMaxLength(40);
        c.Properties<StockMovementType>().HaveConversion<string>().HaveMaxLength(40);
        c.Properties<PurchaseStatus>().HaveConversion<string>().HaveMaxLength(40);
        c.Properties<AttachmentKind>().HaveConversion<string>().HaveMaxLength(40);
    }

    private static void Xmin<T>(EntityTypeBuilder<T> b) where T : class =>
        b.Property<uint>("xmin").HasColumnType("xid").ValueGeneratedOnAddOrUpdate().IsConcurrencyToken();

    protected override void OnModelCreating(ModelBuilder m)
    {
        m.Entity<User>(b =>
        {
            b.HasIndex(x => x.UserName).IsUnique();
            b.Property(x => x.UserName).HasMaxLength(100);
            b.Property(x => x.DisplayName).HasMaxLength(200);
            b.Property(x => x.PasswordHash).HasMaxLength(500);
        });
        m.Entity<AppSetting>(b => { b.HasKey(x => x.Key); b.Property(x => x.Key).HasMaxLength(100); b.Property(x => x.Value).HasMaxLength(4000); });
        m.Entity<NumberSequence>(b => { b.HasKey(x => x.Key); b.Property(x => x.Key).HasMaxLength(50); });
        m.Entity<AuditLog>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Id).ValueGeneratedOnAdd();
            b.Property(x => x.UserName).HasMaxLength(100);
            b.Property(x => x.Action).HasMaxLength(200);
            b.Property(x => x.EntityType).HasMaxLength(100);
            b.Property(x => x.Details).HasMaxLength(1000);
            b.HasIndex(x => x.TimestampUtc);
            b.HasIndex(x => new { x.EntityType, x.EntityId });
        });

        m.Entity<Customer>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(200);
            b.Property(x => x.TaxOffice).HasMaxLength(100);
            b.Property(x => x.TaxNumber).HasMaxLength(11);
            b.Property(x => x.NationalId).HasMaxLength(11);
            b.Property(x => x.Phone).HasMaxLength(40);
            b.Property(x => x.Email).HasMaxLength(200);
            b.Property(x => x.Address).HasMaxLength(500);
            b.Property(x => x.City).HasMaxLength(100);
            b.Property(x => x.District).HasMaxLength(100);
            b.Property(x => x.Note).HasMaxLength(1000);
            b.HasIndex(x => x.Name);
            b.HasIndex(x => x.TaxNumber).IsUnique().HasFilter("\"TaxNumber\" IS NOT NULL");
            b.HasIndex(x => x.NationalId).IsUnique().HasFilter("\"NationalId\" IS NOT NULL");
        });
        m.Entity<Supplier>(b =>
        {
            b.Property(x => x.Name).HasMaxLength(200);
            b.Property(x => x.TaxOffice).HasMaxLength(100);
            b.Property(x => x.TaxNumber).HasMaxLength(11);
            b.Property(x => x.Phone).HasMaxLength(40);
            b.Property(x => x.Email).HasMaxLength(200);
            b.Property(x => x.Address).HasMaxLength(500);
            b.Property(x => x.Note).HasMaxLength(1000);
            b.HasIndex(x => x.Name);
        });
        m.Entity<ProductCategory>(b => { b.Property(x => x.Name).HasMaxLength(100); b.HasIndex(x => x.Name).IsUnique(); });
        m.Entity<Product>(b =>
        {
            b.Property(x => x.Code).HasMaxLength(50);
            b.Property(x => x.Name).HasMaxLength(200);
            b.Property(x => x.Unit).HasMaxLength(30);
            b.Property(x => x.PurchasePrice).HasPrecision(18, 4);
            b.Property(x => x.SalePrice).HasPrecision(18, 4);
            b.Property(x => x.VatRate).HasPrecision(5, 2);
            b.Property(x => x.MinStock).HasPrecision(18, 3);
            b.Property(x => x.Description).HasMaxLength(1000);
            b.HasIndex(x => x.Code).IsUnique();
            b.HasIndex(x => x.Name);
            b.HasOne(x => x.Category).WithMany().HasForeignKey(x => x.CategoryId).OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => { t.HasCheckConstraint("CK_Products_Prices", "\"PurchasePrice\" >= 0 AND \"SalePrice\" >= 0"); });
        });
        m.Entity<CustomerProductPrice>(b =>
        {
            b.Property(x => x.Price).HasPrecision(18, 4);
            b.HasIndex(x => new { x.CustomerId, x.ProductId }).IsUnique();
            b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<Sale>(b =>
        {
            Xmin(b);
            b.Property(x => x.Number).HasMaxLength(30);
            b.Property(x => x.Note).HasMaxLength(1000);
            b.Property(x => x.CancelReason).HasMaxLength(500);
            b.HasIndex(x => x.Number).IsUnique();
            b.HasIndex(x => x.ClientRequestId).IsUnique().HasFilter("\"ClientRequestId\" IS NOT NULL");
            b.HasIndex(x => new { x.CustomerId, x.SaleDate });
            b.HasIndex(x => x.SaleDate);
            b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Items).WithOne(i => i.Sale).HasForeignKey(i => i.SaleId).OnDelete(DeleteBehavior.Cascade);
            b.Ignore(x => x.IsCommercialSale);
            b.ToTable(t => t.HasCheckConstraint("CK_Sales_Totals", "\"GrandTotal\" >= 0 AND \"NetTotal\" >= 0 AND \"VatTotal\" >= 0"));
        });
        m.Entity<SaleItem>(b =>
        {
            b.Property(x => x.ProductCode).HasMaxLength(50);
            b.Property(x => x.ProductName).HasMaxLength(200);
            b.Property(x => x.Unit).HasMaxLength(30);
            b.Property(x => x.Quantity).HasPrecision(18, 3);
            b.Property(x => x.UnitPrice).HasPrecision(18, 4);
            b.Property(x => x.DiscountPercent).HasPrecision(5, 2);
            b.Property(x => x.VatRate).HasPrecision(5, 2);
            b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.ProductId);
            b.ToTable(t => t.HasCheckConstraint("CK_SaleItems_Quantity", "\"Quantity\" > 0"));
        });

        m.Entity<Invoice>(b =>
        {
            Xmin(b);
            b.Property(x => x.InternalRef).HasMaxLength(30);
            b.Property(x => x.BuyerName).HasMaxLength(200);
            b.Property(x => x.BuyerTaxOffice).HasMaxLength(100);
            b.Property(x => x.BuyerTaxNumber).HasMaxLength(11);
            b.Property(x => x.BuyerNationalId).HasMaxLength(11);
            b.Property(x => x.BuyerAddress).HasMaxLength(500);
            b.Property(x => x.BuyerCity).HasMaxLength(100);
            b.Property(x => x.BuyerDistrict).HasMaxLength(100);
            b.Property(x => x.BuyerEmail).HasMaxLength(200);
            b.Property(x => x.Note).HasMaxLength(1000);
            b.Property(x => x.OfficialNumber).HasMaxLength(30);
            b.Property(x => x.OfficialUuid).HasMaxLength(40);
            b.Property(x => x.OfficialProvider).HasMaxLength(50);
            b.Property(x => x.ProviderLastMessage).HasMaxLength(1000);
            b.Property(x => x.ProviderReference).HasMaxLength(200);
            b.Property(x => x.CancellationReason).HasMaxLength(500);
            b.Property(x => x.OfficialCancelReference).HasMaxLength(500);
            b.Property(x => x.CancelReason).HasMaxLength(500);
            b.Ignore(x => x.IsEditable);
            b.HasIndex(x => x.InternalRef).IsUnique();
            // Aynı resmî belgenin ikinci kez içeri alınmasını veritabanı düzeyinde engeller.
            b.HasIndex(x => x.OfficialNumber).IsUnique().HasDatabaseName("IX_Invoices_OfficialNumber").HasFilter("\"OfficialNumber\" IS NOT NULL");
            b.HasIndex(x => x.OfficialUuid).IsUnique().HasDatabaseName("IX_Invoices_OfficialUuid").HasFilter("\"OfficialUuid\" IS NOT NULL");
            b.HasIndex(x => x.SubmissionKey).IsUnique().HasFilter("\"SubmissionKey\" IS NOT NULL");
            b.HasIndex(x => x.ClientRequestId).IsUnique().HasFilter("\"ClientRequestId\" IS NOT NULL");
            // Bir satış için aynı anda yalnızca bir aktif (iptal edilmemiş) fatura: MVP kuralı.
            b.HasIndex(x => x.SaleId).IsUnique().HasDatabaseName("IX_Invoices_ActivePerSale").HasFilter("\"InternalStatus\" <> 'Cancelled'");
            b.HasIndex(x => new { x.CustomerId, x.InvoiceDate });
            b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            b.HasOne(x => x.Sale).WithMany().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Items).WithOne(i => i.Invoice).HasForeignKey(i => i.InvoiceId).OnDelete(DeleteBehavior.Cascade);
            b.HasMany(x => x.Attachments).WithOne(a => a.Invoice).HasForeignKey(a => a.InvoiceId).OnDelete(DeleteBehavior.Restrict);
            // Resmî durum "Düzenlendi" ise numara zorunlu: numarasız "resmî" fatura olamaz.
            b.ToTable(t => t.HasCheckConstraint("CK_Invoices_IssuedHasNumber", "\"OfficialStatus\" <> 'Issued' OR \"OfficialNumber\" IS NOT NULL"));
        });
        m.Entity<InvoiceItem>(b =>
        {
            b.Property(x => x.Description).HasMaxLength(300);
            b.Property(x => x.Unit).HasMaxLength(30);
            b.Property(x => x.Quantity).HasPrecision(18, 3);
            b.Property(x => x.UnitPrice).HasPrecision(18, 4);
            b.Property(x => x.DiscountPercent).HasPrecision(5, 2);
            b.Property(x => x.VatRate).HasPrecision(5, 2);
        });
        m.Entity<InvoiceAttachment>(b =>
        {
            b.Property(x => x.OriginalFileName).HasMaxLength(260);
            b.Property(x => x.StoredFileName).HasMaxLength(100);
            b.Property(x => x.ContentType).HasMaxLength(100);
            b.Property(x => x.Sha256).HasMaxLength(64);
            b.HasIndex(x => new { x.InvoiceId, x.Sha256 }).IsUnique();
        });

        m.Entity<CustomerLedgerEntry>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Seq).ValueGeneratedOnAdd().UseIdentityAlwaysColumn();
            b.Property(x => x.ReferenceNo).HasMaxLength(40);
            b.Property(x => x.Description).HasMaxLength(500);
            b.Property(x => x.CreatedByUserName).HasMaxLength(100);
            b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.CustomerId, x.EntryDate, x.Seq });
            b.HasIndex(x => x.Seq).IsUnique();
            // Açılış bakiyesi müşteri başına tek kez.
            b.HasIndex(x => x.CustomerId).IsUnique().HasDatabaseName("IX_CustLedger_OneOpening").HasFilter("\"EntryType\" = 'OpeningBalance'");
            // Aynı satışın/tahsilatın aynı türde ikinci cari hareketi olamaz (çift kayıt koruması).
            b.HasIndex(x => new { x.SourceType, x.SourceId, x.EntryType }).IsUnique().HasDatabaseName("IX_CustLedger_SourceOnce")
                .HasFilter("\"SourceId\" IS NOT NULL AND \"EntryType\" IN ('Sale','SaleReversal','Payment','PaymentReversal')");
            // Bir hareket en fazla bir kez ters kayıtla kapatılabilir.
            b.HasIndex(x => x.ReversesEntryId).IsUnique().HasFilter("\"ReversesEntryId\" IS NOT NULL");
            b.ToTable(t => t.HasCheckConstraint("CK_CustLedger_Amounts",
                "\"Debit\" >= 0 AND \"Credit\" >= 0 AND ((\"Debit\" > 0 AND \"Credit\" = 0) OR (\"Credit\" > 0 AND \"Debit\" = 0))"));
        });
        m.Entity<SupplierLedgerEntry>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Seq).ValueGeneratedOnAdd().UseIdentityAlwaysColumn();
            b.Property(x => x.ReferenceNo).HasMaxLength(40);
            b.Property(x => x.Description).HasMaxLength(500);
            b.Property(x => x.CreatedByUserName).HasMaxLength(100);
            b.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.SupplierId, x.EntryDate, x.Seq });
            b.HasIndex(x => x.SupplierId).IsUnique().HasDatabaseName("IX_SuppLedger_OneOpening").HasFilter("\"EntryType\" = 'OpeningBalance'");
            b.HasIndex(x => new { x.SourceType, x.SourceId, x.EntryType }).IsUnique().HasDatabaseName("IX_SuppLedger_SourceOnce")
                .HasFilter("\"SourceId\" IS NOT NULL AND \"EntryType\" IN ('Purchase','PurchaseReversal')");
            b.HasIndex(x => x.ReversesEntryId).IsUnique().HasFilter("\"ReversesEntryId\" IS NOT NULL");
            b.ToTable(t => t.HasCheckConstraint("CK_SuppLedger_Amounts",
                "\"Debit\" >= 0 AND \"Credit\" >= 0 AND ((\"Debit\" > 0 AND \"Credit\" = 0) OR (\"Credit\" > 0 AND \"Debit\" = 0))"));
        });

        m.Entity<Payment>(b =>
        {
            Xmin(b);
            b.Property(x => x.Number).HasMaxLength(30);
            b.Property(x => x.BankReference).HasMaxLength(100);
            b.Property(x => x.Note).HasMaxLength(1000);
            b.Property(x => x.ReverseReason).HasMaxLength(500);
            b.Property(x => x.ReceiptOriginalName).HasMaxLength(260);
            b.Property(x => x.ReceiptStoredName).HasMaxLength(100);
            b.Property(x => x.ReceiptContentType).HasMaxLength(100);
            b.HasIndex(x => x.Number).IsUnique();
            b.HasIndex(x => x.ClientRequestId).IsUnique().HasFilter("\"ClientRequestId\" IS NOT NULL");
            b.HasIndex(x => new { x.CustomerId, x.PaymentDate });
            b.HasOne(x => x.Customer).WithMany().HasForeignKey(x => x.CustomerId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Allocations).WithOne(a => a.Payment).HasForeignKey(a => a.PaymentId).OnDelete(DeleteBehavior.Restrict);
            b.ToTable(t => t.HasCheckConstraint("CK_Payments_Amount", "\"Amount\" > 0"));
        });
        m.Entity<PaymentAllocation>(b =>
        {
            b.HasOne(x => x.Sale).WithMany().HasForeignKey(x => x.SaleId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => x.SaleId);
            b.HasIndex(x => x.InvoiceId);
            b.ToTable(t => t.HasCheckConstraint("CK_PaymentAllocations_Amount", "\"Amount\" > 0"));
        });

        m.Entity<PurchaseDocument>(b =>
        {
            Xmin(b);
            b.Property(x => x.Number).HasMaxLength(30);
            b.Property(x => x.SupplierDocumentNo).HasMaxLength(60);
            b.Property(x => x.Note).HasMaxLength(1000);
            b.HasIndex(x => x.Number).IsUnique();
            b.HasIndex(x => x.ClientRequestId).IsUnique().HasFilter("\"ClientRequestId\" IS NOT NULL");
            b.HasOne(x => x.Supplier).WithMany().HasForeignKey(x => x.SupplierId).OnDelete(DeleteBehavior.Restrict);
            b.HasMany(x => x.Items).WithOne(i => i.PurchaseDocument).HasForeignKey(i => i.PurchaseDocumentId).OnDelete(DeleteBehavior.Cascade);
        });
        m.Entity<PurchaseItem>(b =>
        {
            b.Property(x => x.ProductName).HasMaxLength(200);
            b.Property(x => x.Unit).HasMaxLength(30);
            b.Property(x => x.Quantity).HasPrecision(18, 3);
            b.Property(x => x.UnitPrice).HasPrecision(18, 4);
            b.Property(x => x.VatRate).HasPrecision(5, 2);
            b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
        });

        m.Entity<StockMovement>(b =>
        {
            b.HasKey(x => x.Id);
            b.Property(x => x.Seq).ValueGeneratedOnAdd().UseIdentityAlwaysColumn();
            b.Property(x => x.QuantityDelta).HasPrecision(18, 3);
            b.Property(x => x.Note).HasMaxLength(500);
            b.HasOne(x => x.Product).WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Restrict);
            b.HasIndex(x => new { x.ProductId, x.MovementDate });
            // Aynı kaynak satırı aynı türde ikinci kez stok hareketi üretemez (çift stok düşümü koruması).
            b.HasIndex(x => new { x.SourceType, x.SourceId, x.SourceLineId, x.MovementType }).IsUnique().HasDatabaseName("IX_Stock_SourceOnce")
                .HasFilter("\"SourceId\" IS NOT NULL");
            b.ToTable(t => t.HasCheckConstraint("CK_Stock_Delta", "\"QuantityDelta\" <> 0"));
        });
    }
}
