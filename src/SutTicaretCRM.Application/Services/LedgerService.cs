using Microsoft.EntityFrameworkCore;
using SutTicaretCRM.Domain;

namespace SutTicaretCRM.Application.Services;

public record OpeningBalanceRequest(decimal Amount, DateOnly Date, DateOnly? DueDate, string? Description);

public record LedgerEntryDto(
    Guid Id, DateOnly Date, LedgerEntryType EntryType, string ReferenceNo, string Description,
    decimal Debit, decimal Credit, decimal Balance, string CreatedBy, DateTime CreatedAtUtc,
    LedgerSourceType SourceType, Guid? SourceId, bool IsReversed, Guid? ReversesEntryId);

public record LedgerStatementDto(
    Guid AccountId, string AccountName, DateOnly? From, DateOnly? To,
    decimal CarriedBalance, decimal TotalDebit, decimal TotalCredit, decimal ClosingBalance,
    IReadOnlyList<LedgerEntryDto> Entries);

public record ManualLedgerRequest(
    /// <summary>Return, Adjustment veya Manual.</summary>
    LedgerEntryType EntryType,
    DateOnly Date,
    /// <summary>true: müşterinin borcunu artırır (Borç); false: azaltır (Alacak).</summary>
    bool IncreasesDebt,
    decimal Amount,
    string Description,
    List<ReturnLine>? ReturnLines = null);

public record ReturnLine(Guid ProductId, decimal Quantity);

public record CustomerFinancials(
    Guid CustomerId, decimal Balance, decimal OverdueAmount, decimal TotalSales, decimal TotalPayments,
    DateOnly? LastSaleDate, DateOnly? LastPaymentDate, decimal UnallocatedCredit);

public class LedgerService(
    IAppDbContext db, ICurrentUser user, IClock clock, INumberSequence numbers, IAuditLogger audit, StockService stock)
{
    // ───────────── Müşteri cari yazımı (yalnızca diğer servisler çağırır) ─────────────

    private CustomerLedgerEntry NewEntry(Guid customerId, DateOnly date, LedgerEntryType type, decimal debit, decimal credit,
        LedgerSourceType src, Guid? srcId, string reference, string description, DateOnly? due = null, Guid? reverses = null) => new()
    {
        CustomerId = customerId, EntryDate = date, DueDate = due, EntryType = type, Debit = debit, Credit = credit,
        SourceType = src, SourceId = srcId, ReferenceNo = reference, Description = description, ReversesEntryId = reverses,
        CreatedByUserId = user.UserId, CreatedByUserName = user.UserName, CreatedAtUtc = clock.UtcNow,
    };

    /// <summary>Açılış bakiyesi: müşteri başına en fazla bir kez. Fatura/satış OLUŞTURMAZ, satış raporlarına girmez.</summary>
    public async Task<CustomerLedgerEntry> PostOpeningBalanceAsync(Guid customerId, OpeningBalanceRequest r, CancellationToken ct = default)
    {
        var v = new ValidationBuilder();
        v.When(r.Amount == 0, "amount", "Açılış bakiyesi sıfır olamaz.");
        v.When(Money.Round(r.Amount) != r.Amount, "amount", "Tutar en fazla 2 ondalık basamaklı olabilir.");
        v.When(r.DueDate.HasValue && r.DueDate < r.Date, "dueDate", "Vade tarihi açılış tarihinden önce olamaz.");
        v.ThrowIfAny();

        if (await db.CustomerLedgerEntries.AnyAsync(e => e.CustomerId == customerId && e.EntryType == LedgerEntryType.OpeningBalance, ct))
            throw new ConflictException("Bu müşteri için açılış bakiyesi zaten girilmiş. Değişiklik gerekiyorsa cari hesapta 'Düzeltme' hareketi ekleyin.");

        var reference = await numbers.NextAsync("ACL", r.Date.Year, ct);
        var desc = string.IsNullOrWhiteSpace(r.Description) ? "Geçmiş dönemden devreden bakiye (açılış)" : r.Description.Trim();
        var entry = NewEntry(customerId, r.Date, LedgerEntryType.OpeningBalance,
            r.Amount > 0 ? r.Amount : 0, r.Amount < 0 ? -r.Amount : 0,
            LedgerSourceType.Opening, customerId, reference, desc, r.DueDate ?? r.Date);
        db.CustomerLedgerEntries.Add(entry);
        audit.Log("Açılış bakiyesi girildi", nameof(Customer), customerId, $"{reference}: {r.Amount:N2} TL, tarih {r.Date:yyyy-MM-dd}");
        return entry;
    }

    public async Task<CustomerLedgerEntry> PostSaleAsync(Sale sale, CancellationToken ct = default)
    {
        if (await db.CustomerLedgerEntries.AnyAsync(e => e.SourceType == LedgerSourceType.Sale && e.SourceId == sale.Id && e.EntryType == LedgerEntryType.Sale, ct))
            throw new ConflictException("Bu satış cari hesaba zaten işlenmiş.");
        var entry = NewEntry(sale.CustomerId, sale.SaleDate, LedgerEntryType.Sale, sale.GrandTotal, 0,
            LedgerSourceType.Sale, sale.Id, sale.Number, $"Satış {sale.Number}", sale.DueDate);
        db.CustomerLedgerEntries.Add(entry);
        return entry;
    }

    public async Task ReverseSaleAsync(Sale sale, string reason, CancellationToken ct = default)
    {
        var original = await db.CustomerLedgerEntries.FirstOrDefaultAsync(e =>
            e.SourceType == LedgerSourceType.Sale && e.SourceId == sale.Id && e.EntryType == LedgerEntryType.Sale, ct)
            ?? throw new DomainException("Satışın cari hareketi bulunamadı.");
        await AddReversalAsync(original, LedgerEntryType.SaleReversal, LedgerSourceType.Sale, sale.Id, $"Satış iptali {sale.Number}: {reason}", ct);
    }

    public CustomerLedgerEntry PostPayment(Payment p) =>
        db.CustomerLedgerEntries.Add(NewEntry(p.CustomerId, p.PaymentDate, LedgerEntryType.Payment, 0, p.Amount,
            LedgerSourceType.Payment, p.Id, p.Number, $"Tahsilat {p.Number}")).Entity;

    public async Task ReversePaymentAsync(Payment p, string reason, CancellationToken ct = default)
    {
        var original = await db.CustomerLedgerEntries.FirstOrDefaultAsync(e =>
            e.SourceType == LedgerSourceType.Payment && e.SourceId == p.Id && e.EntryType == LedgerEntryType.Payment, ct)
            ?? throw new DomainException("Tahsilatın cari hareketi bulunamadı.");
        await AddReversalAsync(original, LedgerEntryType.PaymentReversal, LedgerSourceType.Payment, p.Id, $"Tahsilat geri alma {p.Number}: {reason}", ct);
    }

    private async Task AddReversalAsync(CustomerLedgerEntry original, LedgerEntryType type, LedgerSourceType src, Guid srcId, string desc, CancellationToken ct)
    {
        if (await db.CustomerLedgerEntries.AnyAsync(e => e.ReversesEntryId == original.Id, ct))
            throw new ConflictException("Bu hareket zaten ters kayıtla kapatılmış.");
        db.CustomerLedgerEntries.Add(NewEntry(original.CustomerId, clock.Today, type, original.Credit, original.Debit,
            src, srcId, original.ReferenceNo, desc, null, original.Id));
    }

    /// <summary>İade / düzeltme / diğer manuel hareket. Hareket ayrı bir kayıt olarak eklenir; geçmiş kayıt değiştirilmez.</summary>
    public async Task<LedgerEntryDto> AddManualEntryAsync(Guid customerId, ManualLedgerRequest r, CancellationToken ct = default)
    {
        var v = new ValidationBuilder();
        v.When(r.EntryType is not (LedgerEntryType.Return or LedgerEntryType.Adjustment or LedgerEntryType.Manual), "entryType", "Yalnızca İade, Düzeltme veya Manuel hareket eklenebilir.");
        v.When(r.Amount <= 0, "amount", "Tutar sıfırdan büyük olmalıdır.");
        v.When(Money.Round(r.Amount) != r.Amount, "amount", "Tutar en fazla 2 ondalık basamaklı olabilir.");
        v.When(string.IsNullOrWhiteSpace(r.Description), "description", "Açıklama zorunludur.");
        v.When(r.EntryType == LedgerEntryType.Return && r.IncreasesDebt, "increasesDebt", "İade, müşterinin borcunu azaltır.");
        v.ThrowIfAny();

        if (!await db.Customers.AnyAsync(c => c.Id == customerId, ct)) throw new NotFoundException("Müşteri bulunamadı.");

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var prefix = r.EntryType switch { LedgerEntryType.Return => "IAD", LedgerEntryType.Adjustment => "DZL", _ => "MNL" };
        var reference = await numbers.NextAsync(prefix, r.Date.Year, ct);
        var entry = NewEntry(customerId, r.Date, r.EntryType, r.IncreasesDebt ? r.Amount : 0, r.IncreasesDebt ? 0 : r.Amount,
            LedgerSourceType.Manual, null, reference, r.Description.Trim(), r.IncreasesDebt ? r.Date : null);
        db.CustomerLedgerEntries.Add(entry);

        if (r.EntryType == LedgerEntryType.Return && r.ReturnLines is { Count: > 0 })
        {
            foreach (var line in r.ReturnLines)
                await stock.RecordAsync(line.ProductId, r.Date, StockMovementType.Return, line.Quantity,
                    LedgerSourceType.Manual, entry.Id, line.ProductId, $"İade {reference}", ct);
        }
        audit.Log("Cari hareket eklendi", nameof(CustomerLedgerEntry), entry.Id, $"{r.EntryType} {reference}: {(r.IncreasesDebt ? "+" : "-")}{r.Amount:N2} TL");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return ToDto(entry, 0, false);
    }

    // ───────────── Ekstre ─────────────

    public async Task<LedgerStatementDto> GetCustomerStatementAsync(Guid customerId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var customer = await db.Customers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == customerId, ct)
            ?? throw new NotFoundException("Müşteri bulunamadı.");
        var all = await db.CustomerLedgerEntries.AsNoTracking().Where(e => e.CustomerId == customerId)
            .OrderBy(e => e.EntryDate).ThenBy(e => e.Seq).ToListAsync(ct);
        var reversed = all.Where(e => e.ReversesEntryId != null).Select(e => e.ReversesEntryId!.Value).ToHashSet();

        decimal running = 0, carried = 0, totalDebit = 0, totalCredit = 0;
        var rows = new List<LedgerEntryDto>();
        foreach (var e in all)
        {
            running += e.Debit - e.Credit;
            if (from.HasValue && e.EntryDate < from.Value) { carried = running; continue; }
            if (to.HasValue && e.EntryDate > to.Value) continue;
            totalDebit += e.Debit; totalCredit += e.Credit;
            rows.Add(ToDto(e, running, reversed.Contains(e.Id)));
        }
        var closing = rows.Count > 0 ? rows[^1].Balance : carried;
        return new LedgerStatementDto(customerId, customer.Name, from, to, carried, totalDebit, totalCredit, closing, rows);
    }

    public async Task<LedgerStatementDto> GetSupplierStatementAsync(Guid supplierId, DateOnly? from, DateOnly? to, CancellationToken ct = default)
    {
        var supplier = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(c => c.Id == supplierId, ct)
            ?? throw new NotFoundException("Tedarikçi bulunamadı.");
        var all = await db.SupplierLedgerEntries.AsNoTracking().Where(e => e.SupplierId == supplierId)
            .OrderBy(e => e.EntryDate).ThenBy(e => e.Seq).ToListAsync(ct);
        var reversed = all.Where(e => e.ReversesEntryId != null).Select(e => e.ReversesEntryId!.Value).ToHashSet();

        // Tedarikçi bakiyesi = Σ Alacak − Σ Borç (pozitif = tedarikçiye borcumuz var)
        decimal running = 0, carried = 0, totalDebit = 0, totalCredit = 0;
        var rows = new List<LedgerEntryDto>();
        foreach (var e in all)
        {
            running += e.Credit - e.Debit;
            if (from.HasValue && e.EntryDate < from.Value) { carried = running; continue; }
            if (to.HasValue && e.EntryDate > to.Value) continue;
            totalDebit += e.Debit; totalCredit += e.Credit;
            rows.Add(new LedgerEntryDto(e.Id, e.EntryDate, e.EntryType, e.ReferenceNo, e.Description, e.Debit, e.Credit, running,
                e.CreatedByUserName, e.CreatedAtUtc, e.SourceType, e.SourceId, reversed.Contains(e.Id), e.ReversesEntryId));
        }
        var closing = rows.Count > 0 ? rows[^1].Balance : carried;
        return new LedgerStatementDto(supplierId, supplier.Name, from, to, carried, totalDebit, totalCredit, closing, rows);
    }

    private static LedgerEntryDto ToDto(CustomerLedgerEntry e, decimal balance, bool reversed) =>
        new(e.Id, e.EntryDate, e.EntryType, e.ReferenceNo, e.Description, e.Debit, e.Credit, balance,
            e.CreatedByUserName, e.CreatedAtUtc, e.SourceType, e.SourceId, reversed, e.ReversesEntryId);

    // ───────────── Bakiye / vade hesabı ─────────────

    /// <summary>
    /// Müşteri başına bakiye, vadesi geçmiş tutar ve özet toplamları hesaplar.
    /// Bakiye = Σ Borç − Σ Alacak (açılış dahil, iptal/ters kayıtlar dahil). Raporlanan satış/tahsilat toplamları ise
    /// ters kaydı olmayan Satış ve Tahsilat hareketlerinden gelir; açılış bakiyesi ASLA satış sayılmaz.
    /// Vadesi geçmiş: vadesi dolmuş açık kalemler (açılış, satış, borç düzeltmeleri) toplamından, mahsup edilmemiş
    /// alacakların (dağıtılmamış tahsilat, iade) önce en eski/vadesi geçmiş borca sayılmasıyla bulunan tutar; bakiyeyi aşamaz.
    /// </summary>
    public async Task<Dictionary<Guid, CustomerFinancials>> GetFinancialsAsync(IReadOnlyCollection<Guid>? customerIds = null, CancellationToken ct = default)
    {
        var today = clock.Today;
        var q = db.CustomerLedgerEntries.AsNoTracking();
        if (customerIds is not null) q = q.Where(e => customerIds.Contains(e.CustomerId));
        var entries = await q.Select(e => new
        {
            e.Id, e.CustomerId, e.EntryType, e.Debit, e.Credit, e.EntryDate, e.DueDate, e.SourceType, e.SourceId, e.ReversesEntryId,
        }).ToListAsync(ct);

        var allocBySale = await db.PaymentAllocations.AsNoTracking().Where(a => a.IsActive)
            .GroupBy(a => a.SaleId).Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        var allocByPayment = await db.PaymentAllocations.AsNoTracking().Where(a => a.IsActive)
            .GroupBy(a => a.PaymentId).Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);

        var reversedIds = entries.Where(e => e.ReversesEntryId != null).Select(e => e.ReversesEntryId!.Value).ToHashSet();
        var result = new Dictionary<Guid, CustomerFinancials>();

        foreach (var g in entries.GroupBy(e => e.CustomerId))
        {
            decimal balance = 0, overdueItems = 0, unallocated = 0, totalSales = 0, totalPayments = 0;
            DateOnly? lastSale = null, lastPayment = null;
            foreach (var e in g)
            {
                balance += e.Debit - e.Credit;
                var active = e.ReversesEntryId == null && !reversedIds.Contains(e.Id);
                if (!active) continue;

                if (e.EntryType == LedgerEntryType.Sale)
                {
                    totalSales += e.Debit;
                    if (lastSale is null || e.EntryDate > lastSale) lastSale = e.EntryDate;
                }
                if (e.EntryType == LedgerEntryType.Payment)
                {
                    totalPayments += e.Credit;
                    if (lastPayment is null || e.EntryDate > lastPayment) lastPayment = e.EntryDate;
                }
                if (e.Debit > 0)
                {
                    var remaining = e.Debit - (e.SourceType == LedgerSourceType.Sale && e.SourceId is { } sid && allocBySale.TryGetValue(sid, out var a) ? a : 0);
                    var due = e.DueDate ?? e.EntryDate;
                    if (remaining > 0 && due < today) overdueItems += remaining;
                }
                if (e.Credit > 0)
                    unallocated += e.Credit - (e.SourceType == LedgerSourceType.Payment && e.SourceId is { } pid && allocByPayment.TryGetValue(pid, out var pa) ? pa : 0);
            }
            var overdue = Math.Clamp(overdueItems - unallocated, 0, Math.Max(balance, 0));
            result[g.Key] = new CustomerFinancials(g.Key, balance, overdue, totalSales, totalPayments, lastSale, lastPayment, unallocated);
        }
        if (customerIds is not null)
            foreach (var id in customerIds.Where(id => !result.ContainsKey(id)))
                result[id] = new CustomerFinancials(id, 0, 0, 0, 0, null, null, 0);
        return result;
    }

    public async Task<CustomerFinancials> GetFinancialsAsync(Guid customerId, CancellationToken ct = default) =>
        (await GetFinancialsAsync([customerId], ct))[customerId];

    public async Task<Dictionary<Guid, decimal>> GetSupplierBalancesAsync(CancellationToken ct = default) =>
        await db.SupplierLedgerEntries.AsNoTracking().GroupBy(e => e.SupplierId)
            .Select(g => new { g.Key, Balance = g.Sum(x => x.Credit - x.Debit) }).ToDictionaryAsync(x => x.Key, x => x.Balance, ct);
}
