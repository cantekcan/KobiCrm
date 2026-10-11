using Microsoft.EntityFrameworkCore;
using SutTicaretCRM.Domain;

namespace SutTicaretCRM.Application.Services;

public record CustomerRequest(
    string Name,
    CustomerType CustomerType,
    string? TaxOffice,
    string? TaxNumber,
    string? NationalId,
    string? Phone,
    string? Email,
    string? Address,
    string? City,
    string? District,
    int PaymentTermDays,
    EInvoiceTaxpayerStatus EInvoiceStatus,
    string? Note,
    bool IsActive = true,
    /// <summary>Yalnızca oluşturmada kullanılır. Düzenlemede açılış bakiyesi değiştirilemez (düzeltme hareketi kullanılır).</summary>
    OpeningBalanceRequest? OpeningBalance = null);

public record CustomerListItem(
    Guid Id, string Name, CustomerType CustomerType, string? Phone, string? TaxNumber, bool IsActive,
    decimal Balance, decimal OverdueAmount, DateOnly? LastSaleDate, DateOnly? LastPaymentDate);

public record CustomerDetailDto(
    Guid Id, string Name, CustomerType CustomerType, string? TaxOffice, string? TaxNumber, string? NationalId,
    string? Phone, string? Email, string? Address, string? City, string? District, int PaymentTermDays,
    EInvoiceTaxpayerStatus EInvoiceStatus, string? Note, bool IsActive,
    decimal Balance, decimal OverdueAmount, decimal TotalSales, decimal TotalPayments, decimal UnallocatedCredit,
    bool HasOpeningBalance, decimal OpeningBalanceAmount, DateOnly? OpeningBalanceDate, string? OpeningReference,
    int OpenSaleCount);

public record CustomerProductHistoryItem(Guid ProductId, string ProductName, string Unit, decimal TotalQuantity, decimal TotalAmount, decimal LastUnitPrice, DateOnly LastSaleDate, int SaleCount);

public class CustomerService(IAppDbContext db, LedgerService ledger, IAuditLogger audit, INumberSequence numbers)
{
    public async Task<PagedResult<CustomerListItem>> ListAsync(string? search, string? sort, bool desc, string? balanceFilter,
        bool? active, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 200);
        var q = db.Customers.AsNoTracking().AsQueryable();
        if (active.HasValue) q = q.Where(c => c.IsActive == active);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(c => c.Name.ToLower().Contains(s) || (c.Phone != null && c.Phone.Contains(s)) || (c.TaxNumber != null && c.TaxNumber.Contains(s)));
        }
        var customers = await q.ToListAsync(ct);
        var fin = await ledger.GetFinancialsAsync(customers.Select(c => c.Id).ToList(), ct);

        var rows = customers.Select(c =>
        {
            var f = fin[c.Id];
            return new CustomerListItem(c.Id, c.Name, c.CustomerType, c.Phone, c.TaxNumber ?? c.NationalId, c.IsActive,
                f.Balance, f.OverdueAmount, f.LastSaleDate, f.LastPaymentDate);
        });
        rows = balanceFilter switch
        {
            "debt" => rows.Where(r => r.Balance > 0),
            "overdue" => rows.Where(r => r.OverdueAmount > 0),
            "clear" => rows.Where(r => r.Balance == 0),
            "credit" => rows.Where(r => r.Balance < 0),
            _ => rows,
        };
        Func<CustomerListItem, object?> key = (sort ?? "name") switch
        {
            "balance" => r => r.Balance,
            "overdue" => r => r.OverdueAmount,
            "lastSale" => r => r.LastSaleDate,
            "lastPayment" => r => r.LastPaymentDate,
            _ => r => r.Name,
        };
        var ordered = desc ? rows.OrderByDescending(key) : rows.OrderBy(key);
        var list = ordered.ToList();
        return new PagedResult<CustomerListItem>(list.Skip((page - 1) * pageSize).Take(pageSize).ToList(), list.Count, page, pageSize);
    }

    public async Task<CustomerDetailDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var c = await db.Customers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Müşteri bulunamadı.");
        var f = await ledger.GetFinancialsAsync(id, ct);
        var opening = await db.CustomerLedgerEntries.AsNoTracking()
            .Where(e => e.CustomerId == id && e.EntryType == LedgerEntryType.OpeningBalance).FirstOrDefaultAsync(ct);
        var openSales = await db.Sales.CountAsync(s => s.CustomerId == id && (s.Status == SaleStatus.Approved || s.Status == SaleStatus.PartiallyPaid), ct);
        return new CustomerDetailDto(c.Id, c.Name, c.CustomerType, c.TaxOffice, c.TaxNumber, c.NationalId, c.Phone, c.Email, c.Address,
            c.City, c.District, c.PaymentTermDays, c.EInvoiceStatus, c.Note, c.IsActive,
            f.Balance, f.OverdueAmount, f.TotalSales, f.TotalPayments, f.UnallocatedCredit,
            opening is not null, opening is null ? 0 : opening.Debit - opening.Credit, opening?.EntryDate, opening?.ReferenceNo, openSales);
    }

    private async Task ValidateAsync(CustomerRequest r, Guid? existingId, CancellationToken ct)
    {
        var v = new ValidationBuilder();
        v.When(string.IsNullOrWhiteSpace(r.Name), "name", "Müşteri adı / firma adı zorunludur.");
        v.When(r.Name?.Length > 200, "name", "Ad en fazla 200 karakter olabilir.");
        v.When(r.PaymentTermDays is < 0 or > 365, "paymentTermDays", "Ödeme vadesi 0 ile 365 gün arasında olmalıdır.");
        v.When(!string.IsNullOrWhiteSpace(r.TaxNumber) && !TurkishTaxIds.IsValidVkn(r.TaxNumber.Trim()), "taxNumber", "Vergi kimlik numarası 10 haneli ve geçerli olmalıdır.");
        v.When(!string.IsNullOrWhiteSpace(r.NationalId) && !TurkishTaxIds.IsValidTckn(r.NationalId.Trim()), "nationalId", "T.C. kimlik numarası 11 haneli ve geçerli olmalıdır.");
        v.When(!string.IsNullOrWhiteSpace(r.Email) && !(r.Email.Contains('@') && r.Email.Contains('.')), "email", "E-posta adresi geçerli değil.");
        v.When(r.OpeningBalance is not null && existingId.HasValue, "openingBalance", "Açılış bakiyesi düzenlemede değiştirilemez; cari hesaba 'Düzeltme' hareketi ekleyin.");
        v.ThrowIfAny();

        var tax = r.TaxNumber?.Trim();
        if (!string.IsNullOrEmpty(tax) && await db.Customers.AnyAsync(c => c.TaxNumber == tax && c.Id != existingId, ct))
            throw new ConflictException("Bu vergi kimlik numarasıyla kayıtlı başka bir müşteri var.");
        var tc = r.NationalId?.Trim();
        if (!string.IsNullOrEmpty(tc) && await db.Customers.AnyAsync(c => c.NationalId == tc && c.Id != existingId, ct))
            throw new ConflictException("Bu T.C. kimlik numarasıyla kayıtlı başka bir müşteri var.");
    }

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    public async Task<CustomerDetailDto> CreateAsync(CustomerRequest r, CancellationToken ct = default)
    {
        await ValidateAsync(r, null, ct);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var c = new Customer();
        Apply(c, r);
        db.Customers.Add(c);
        audit.Log("Müşteri oluşturuldu", nameof(Customer), c.Id, c.Name);
        if (r.OpeningBalance is not null) await ledger.PostOpeningBalanceAsync(c.Id, r.OpeningBalance, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(c.Id, ct);
    }

    public async Task<CustomerDetailDto> UpdateAsync(Guid id, CustomerRequest r, CancellationToken ct = default)
    {
        await ValidateAsync(r, id, ct);
        var c = await db.Customers.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Müşteri bulunamadı.");
        Apply(c, r);
        c.UpdatedAtUtc = DateTime.UtcNow;
        audit.Log("Müşteri güncellendi", nameof(Customer), c.Id, c.Name);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Mevcut müşteriye sonradan açılış bakiyesi girer (müşteri başına en fazla bir kez).</summary>
    public async Task<CustomerDetailDto> AddOpeningBalanceAsync(Guid id, OpeningBalanceRequest r, CancellationToken ct = default)
    {
        if (!await db.Customers.AnyAsync(c => c.Id == id, ct)) throw new NotFoundException("Müşteri bulunamadı.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        await ledger.PostOpeningBalanceAsync(id, r, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    private static void Apply(Customer c, CustomerRequest r)
    {
        c.Name = r.Name.Trim();
        c.CustomerType = r.CustomerType;
        c.TaxOffice = Clean(r.TaxOffice);
        c.TaxNumber = Clean(r.TaxNumber);
        c.NationalId = Clean(r.NationalId);
        c.Phone = Clean(r.Phone);
        c.Email = Clean(r.Email);
        c.Address = Clean(r.Address);
        c.City = Clean(r.City);
        c.District = Clean(r.District);
        c.PaymentTermDays = r.PaymentTermDays;
        c.EInvoiceStatus = r.EInvoiceStatus;
        c.Note = Clean(r.Note);
        c.IsActive = r.IsActive;
    }

    public async Task<IReadOnlyList<CustomerProductHistoryItem>> GetProductHistoryAsync(Guid id, CancellationToken ct = default)
    {
        var rows = await db.SaleItems.AsNoTracking()
            .Where(i => i.Sale!.CustomerId == id && (i.Sale.Status == SaleStatus.Approved || i.Sale.Status == SaleStatus.PartiallyPaid || i.Sale.Status == SaleStatus.Paid))
            .Select(i => new { i.ProductId, i.ProductName, i.Unit, i.Quantity, i.NetAmount, i.UnitPrice, i.Sale!.SaleDate, i.Sale.Number })
            .ToListAsync(ct);
        return rows.GroupBy(r => r.ProductId).Select(g =>
        {
            var last = g.OrderByDescending(x => x.SaleDate).ThenByDescending(x => x.Number).First();
            return new CustomerProductHistoryItem(g.Key, last.ProductName, last.Unit, g.Sum(x => x.Quantity), g.Sum(x => x.NetAmount),
                last.UnitPrice, last.SaleDate, g.Select(x => x.Number).Distinct().Count());
        }).OrderByDescending(x => x.LastSaleDate).ToList();
    }
}
