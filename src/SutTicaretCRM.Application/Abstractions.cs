using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Infrastructure;
using SutTicaretCRM.Domain;

namespace SutTicaretCRM.Application;

public interface IAppDbContext
{
    DbSet<User> Users { get; }
    DbSet<AppSetting> AppSettings { get; }
    DbSet<NumberSequence> NumberSequences { get; }
    DbSet<AuditLog> AuditLogs { get; }
    DbSet<Customer> Customers { get; }
    DbSet<Supplier> Suppliers { get; }
    DbSet<ProductCategory> ProductCategories { get; }
    DbSet<Product> Products { get; }
    DbSet<CustomerProductPrice> CustomerProductPrices { get; }
    DbSet<Sale> Sales { get; }
    DbSet<SaleItem> SaleItems { get; }
    DbSet<Invoice> Invoices { get; }
    DbSet<InvoiceItem> InvoiceItems { get; }
    DbSet<InvoiceAttachment> InvoiceAttachments { get; }
    DbSet<CustomerLedgerEntry> CustomerLedgerEntries { get; }
    DbSet<SupplierLedgerEntry> SupplierLedgerEntries { get; }
    DbSet<Payment> Payments { get; }
    DbSet<PaymentAllocation> PaymentAllocations { get; }
    DbSet<PurchaseDocument> PurchaseDocuments { get; }
    DbSet<PurchaseItem> PurchaseItems { get; }
    DbSet<StockMovement> StockMovements { get; }

    DatabaseFacade Database { get; }
    ChangeTracker ChangeTracker { get; }
    Task<int> SaveChangesAsync(CancellationToken ct = default);
}

public interface IClock
{
    DateTime UtcNow { get; }
    /// <summary>İşletmenin yerel (Türkiye) tarihi. Bugün/ay hesapları bununla yapılır.</summary>
    DateOnly Today { get; }
}

public interface ICurrentUser
{
    Guid? UserId { get; }
    string UserName { get; }
}

public interface INumberSequence
{
    /// <summary>Aynı işlem içinde artan, yıl bazlı numara üretir (ör. SAT-2026-000001).</summary>
    Task<string> NextAsync(string prefix, int year, CancellationToken ct = default);
}

public sealed record StoredFile(string StoredName, long SizeBytes, string Sha256);

public interface IFileStore
{
    Task<StoredFile> SaveAsync(string folder, Stream content, string extension, CancellationToken ct = default);
    Task<Stream?> OpenReadAsync(string folder, string storedName, CancellationToken ct = default);
}

public class NotFoundException(string message) : Exception(message);
public class ConflictException(string message) : Exception(message);

public class ValidationFailedException(IReadOnlyDictionary<string, string[]> errors) : Exception("Girilen bilgilerde hata var.")
{
    public IReadOnlyDictionary<string, string[]> Errors { get; } = errors;
    public ValidationFailedException(string field, string message)
        : this(new Dictionary<string, string[]> { [field] = [message] }) { }
}

/// <summary>Birden çok alan hatasını biriktirip tek seferde fırlatır.</summary>
public sealed class ValidationBuilder
{
    private readonly Dictionary<string, List<string>> _errors = [];
    public ValidationBuilder Add(string field, string message)
    {
        if (!_errors.TryGetValue(field, out var list)) _errors[field] = list = [];
        list.Add(message);
        return this;
    }
    public ValidationBuilder When(bool condition, string field, string message) => condition ? Add(field, message) : this;
    public bool HasErrors => _errors.Count > 0;
    public void ThrowIfAny()
    {
        if (HasErrors) throw new ValidationFailedException(_errors.ToDictionary(k => k.Key, v => v.Value.ToArray()));
    }
}

public sealed record PagedResult<T>(IReadOnlyList<T> Items, int Total, int Page, int PageSize);
