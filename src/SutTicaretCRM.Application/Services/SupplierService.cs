using Microsoft.EntityFrameworkCore;
using SutTicaretCRM.Domain;

namespace SutTicaretCRM.Application.Services;

public record SupplierRequest(string Name, string? TaxOffice, string? TaxNumber, string? Phone, string? Email, string? Address, string? Note,
    bool IsActive = true, OpeningBalanceRequest? OpeningBalance = null);

public record SupplierListItem(Guid Id, string Name, string? Phone, string? TaxNumber, bool IsActive, decimal Balance, DateOnly? LastPurchaseDate);

public record SupplierDto(Guid Id, string Name, string? TaxOffice, string? TaxNumber, string? Phone, string? Email, string? Address, string? Note,
    bool IsActive, decimal Balance, bool HasOpeningBalance);

public record SupplierPaymentRequest(DateOnly Date, decimal Amount, PaymentMethod Method, string? Note);

public record PurchaseItemRequest(Guid ProductId, decimal Quantity, decimal UnitPrice, decimal? VatRate);

public record PurchaseRequest(Guid SupplierId, DateOnly DocumentDate, DateOnly? DueDate, string? SupplierDocumentNo, string? Note,
    List<PurchaseItemRequest> Items, bool Approve, Guid? ClientRequestId);

public record PurchaseItemDto(Guid Id, int LineNo, Guid ProductId, string ProductName, decimal Quantity, string Unit, decimal UnitPrice, decimal VatRate, decimal NetAmount, decimal VatAmount, decimal LineTotal);

public record PurchaseDto(Guid Id, string Number, Guid SupplierId, string SupplierName, DateOnly DocumentDate, DateOnly? DueDate, string? SupplierDocumentNo,
    PurchaseStatus Status, decimal NetTotal, decimal VatTotal, decimal GrandTotal, string? Note, IReadOnlyList<PurchaseItemDto> Items);

public class SupplierService(IAppDbContext db, ICurrentUser user, IClock clock, INumberSequence numbers, SettingsService settings,
    StockService stock, LedgerService ledger, IAuditLogger audit)
{
    private SupplierLedgerEntry NewEntry(Guid supplierId, DateOnly date, LedgerEntryType type, decimal debit, decimal credit,
        LedgerSourceType src, Guid? srcId, string reference, string desc, DateOnly? due = null, Guid? reverses = null) => new()
    {
        SupplierId = supplierId, EntryDate = date, DueDate = due, EntryType = type, Debit = debit, Credit = credit, SourceType = src, SourceId = srcId,
        ReferenceNo = reference, Description = desc, ReversesEntryId = reverses, CreatedByUserId = user.UserId, CreatedByUserName = user.UserName, CreatedAtUtc = clock.UtcNow,
    };

    // ───────────── Tedarikçi ─────────────

    private static string? Clean(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    private static void Validate(SupplierRequest r, bool update)
    {
        var v = new ValidationBuilder();
        v.When(string.IsNullOrWhiteSpace(r.Name), "name", "Tedarikçi adı zorunludur.");
        v.When(!string.IsNullOrWhiteSpace(r.TaxNumber) && !TurkishTaxIds.IsValidVkn(r.TaxNumber.Trim()) && !TurkishTaxIds.IsValidTckn(r.TaxNumber.Trim()), "taxNumber", "Vergi/T.C. kimlik numarası geçerli değil.");
        v.When(update && r.OpeningBalance is not null, "openingBalance", "Açılış bakiyesi düzenlemede değiştirilemez.");
        v.ThrowIfAny();
    }

    public async Task<SupplierDto> CreateAsync(SupplierRequest r, CancellationToken ct = default)
    {
        Validate(r, false);
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var s = new Supplier { Name = r.Name.Trim(), TaxOffice = Clean(r.TaxOffice), TaxNumber = Clean(r.TaxNumber), Phone = Clean(r.Phone), Email = Clean(r.Email), Address = Clean(r.Address), Note = Clean(r.Note), IsActive = r.IsActive };
        db.Suppliers.Add(s);
        if (r.OpeningBalance is { } ob)
        {
            if (ob.Amount <= 0) throw new ValidationFailedException("openingBalance", "Tedarikçi açılış bakiyesi (borcumuz) sıfırdan büyük olmalıdır.");
            var reference = await numbers.NextAsync("ACT", ob.Date.Year, ct);
            db.SupplierLedgerEntries.Add(NewEntry(s.Id, ob.Date, LedgerEntryType.OpeningBalance, 0, ob.Amount, LedgerSourceType.Opening, s.Id, reference,
                string.IsNullOrWhiteSpace(ob.Description) ? "Geçmiş dönemden devreden borç (açılış)" : ob.Description.Trim(), ob.DueDate ?? ob.Date));
        }
        audit.Log("Tedarikçi oluşturuldu", nameof(Supplier), s.Id, s.Name);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(s.Id, ct);
    }

    public async Task<SupplierDto> UpdateAsync(Guid id, SupplierRequest r, CancellationToken ct = default)
    {
        Validate(r, true);
        var s = await db.Suppliers.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Tedarikçi bulunamadı.");
        s.Name = r.Name.Trim(); s.TaxOffice = Clean(r.TaxOffice); s.TaxNumber = Clean(r.TaxNumber); s.Phone = Clean(r.Phone);
        s.Email = Clean(r.Email); s.Address = Clean(r.Address); s.Note = Clean(r.Note); s.IsActive = r.IsActive; s.UpdatedAtUtc = clock.UtcNow;
        audit.Log("Tedarikçi güncellendi", nameof(Supplier), s.Id, s.Name);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<SupplierDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var s = await db.Suppliers.AsNoTracking().FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Tedarikçi bulunamadı.");
        var bal = await db.SupplierLedgerEntries.Where(e => e.SupplierId == id).SumAsync(e => (decimal?)(e.Credit - e.Debit), ct) ?? 0;
        var hasOpening = await db.SupplierLedgerEntries.AnyAsync(e => e.SupplierId == id && e.EntryType == LedgerEntryType.OpeningBalance, ct);
        return new SupplierDto(s.Id, s.Name, s.TaxOffice, s.TaxNumber, s.Phone, s.Email, s.Address, s.Note, s.IsActive, bal, hasOpening);
    }

    public async Task<IReadOnlyList<SupplierListItem>> ListAsync(string? search, bool? active, bool? withBalance, CancellationToken ct = default)
    {
        var q = db.Suppliers.AsNoTracking().AsQueryable();
        if (active.HasValue) q = q.Where(s => s.IsActive == active);
        if (!string.IsNullOrWhiteSpace(search)) { var t = search.Trim().ToLower(); q = q.Where(s => s.Name.ToLower().Contains(t)); }
        var suppliers = await q.OrderBy(s => s.Name).ToListAsync(ct);
        var balances = await ledger.GetSupplierBalancesAsync(ct);
        var lastPurchase = await db.PurchaseDocuments.AsNoTracking().Where(p => p.Status == PurchaseStatus.Approved)
            .GroupBy(p => p.SupplierId).Select(g => new { g.Key, D = g.Max(x => x.DocumentDate) }).ToDictionaryAsync(x => x.Key, x => x.D, ct);
        var rows = suppliers.Select(s => new SupplierListItem(s.Id, s.Name, s.Phone, s.TaxNumber, s.IsActive, balances.GetValueOrDefault(s.Id),
            lastPurchase.TryGetValue(s.Id, out var d) ? d : null));
        if (withBalance == true) rows = rows.Where(r => r.Balance != 0);
        return rows.ToList();
    }

    /// <summary>Tedarikçiye yapılan ödeme: tedarikçi cari hesabında Borç hareketi (borcumuzu azaltır).</summary>
    public async Task<LedgerEntryDto> PaySupplierAsync(Guid supplierId, SupplierPaymentRequest r, CancellationToken ct = default)
    {
        var v = new ValidationBuilder();
        v.When(r.Amount <= 0, "amount", "Ödeme tutarı sıfırdan büyük olmalıdır.");
        v.When(Money.Round(r.Amount) != r.Amount, "amount", "Tutar en fazla 2 ondalık basamaklı olabilir.");
        v.ThrowIfAny();
        if (!await db.Suppliers.AnyAsync(s => s.Id == supplierId, ct)) throw new NotFoundException("Tedarikçi bulunamadı.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var reference = await numbers.NextAsync("ODM", r.Date.Year, ct);
        var e = NewEntry(supplierId, r.Date, LedgerEntryType.Payment, r.Amount, 0, LedgerSourceType.Manual, null, reference,
            $"Tedarikçi ödemesi ({r.Method}){(string.IsNullOrWhiteSpace(r.Note) ? "" : ": " + r.Note.Trim())}");
        db.SupplierLedgerEntries.Add(e);
        audit.Log("Tedarikçi ödemesi", nameof(Supplier), supplierId, $"{reference}: {r.Amount:N2} TL");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return new LedgerEntryDto(e.Id, e.EntryDate, e.EntryType, e.ReferenceNo, e.Description, e.Debit, e.Credit, 0, e.CreatedByUserName, e.CreatedAtUtc, e.SourceType, e.SourceId, false, null);
    }

    // ───────────── Alış belgeleri ─────────────

    public async Task<PurchaseDto> CreatePurchaseAsync(PurchaseRequest r, CancellationToken ct = default)
    {
        if (r.ClientRequestId is { } key)
        {
            var existing = await db.PurchaseDocuments.Where(p => p.ClientRequestId == key).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
            if (existing.HasValue) return await GetPurchaseAsync(existing.Value, ct);
        }
        var v = new ValidationBuilder();
        v.When(r.Items is null || r.Items.Count == 0, "items", "Alışa en az bir ürün eklenmelidir.");
        v.When(r.DueDate.HasValue && r.DueDate < r.DocumentDate, "dueDate", "Vade tarihi belge tarihinden önce olamaz.");
        v.ThrowIfAny();

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var supplier = await db.Suppliers.FirstOrDefaultAsync(s => s.Id == r.SupplierId, ct) ?? throw new NotFoundException("Tedarikçi bulunamadı.");
        var ids = r.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products.Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);

        var doc = new PurchaseDocument
        {
            Number = await numbers.NextAsync("ALS", r.DocumentDate.Year, ct), SupplierId = supplier.Id, DocumentDate = r.DocumentDate, DueDate = r.DueDate,
            SupplierDocumentNo = Clean(r.SupplierDocumentNo), Note = Clean(r.Note), ClientRequestId = r.ClientRequestId, CreatedByUserId = user.UserId,
        };
        var results = new List<LineResult>();
        var n = 0;
        foreach (var item in r.Items)
        {
            n++;
            var p = products.GetValueOrDefault(item.ProductId) ?? throw new ValidationFailedException($"items[{n - 1}]", $"{n}. satırdaki ürün bulunamadı.");
            var vat = item.VatRate ?? p.VatRate;
            await settings.EnsureVatRateAllowedAsync(vat, $"items[{n - 1}]", ct);
            LineResult calc;
            try { calc = DocumentCalculator.CalculateLine(new LineInput(item.Quantity, item.UnitPrice, 0, 0, vat)); }
            catch (DomainException ex) { throw new ValidationFailedException($"items[{n - 1}]", $"{n}. satır: {ex.Message}"); }
            results.Add(calc);
            doc.Items.Add(new PurchaseItem { PurchaseDocumentId = doc.Id, LineNo = n, ProductId = p.Id, ProductName = p.Name, Quantity = item.Quantity, Unit = p.Unit,
                UnitPrice = item.UnitPrice, VatRate = vat, NetAmount = calc.Net, VatAmount = calc.Vat, LineTotal = calc.Total });
        }
        var t = DocumentCalculator.Sum(results);
        doc.NetTotal = t.Net; doc.VatTotal = t.Vat; doc.GrandTotal = t.Total;
        db.PurchaseDocuments.Add(doc);
        audit.Log("Alış oluşturuldu", nameof(PurchaseDocument), doc.Id, $"{doc.Number}: {doc.GrandTotal:N2} TL");
        if (r.Approve) await ApproveCoreAsync(doc, ct);
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) when (r.ClientRequestId.HasValue)
        {
            await tx.RollbackAsync(ct); db.ChangeTracker.Clear();
            var existing = await db.PurchaseDocuments.Where(p => p.ClientRequestId == r.ClientRequestId).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
            if (existing.HasValue) return await GetPurchaseAsync(existing.Value, ct);
            throw;
        }
        await tx.CommitAsync(ct);
        return await GetPurchaseAsync(doc.Id, ct);
    }

    public async Task<PurchaseDto> ApprovePurchaseAsync(Guid id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var doc = await db.PurchaseDocuments.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Alış belgesi bulunamadı.");
        await ApproveCoreAsync(doc, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetPurchaseAsync(id, ct);
    }

    private async Task ApproveCoreAsync(PurchaseDocument doc, CancellationToken ct)
    {
        if (doc.Status != PurchaseStatus.Draft) throw new ConflictException("Yalnızca taslak alış belgesi onaylanabilir.");
        doc.Status = PurchaseStatus.Approved;
        if (await db.SupplierLedgerEntries.AnyAsync(e => e.SourceType == LedgerSourceType.Purchase && e.SourceId == doc.Id && e.EntryType == LedgerEntryType.Purchase, ct))
            throw new ConflictException("Bu alış cari hesaba zaten işlenmiş.");
        db.SupplierLedgerEntries.Add(NewEntry(doc.SupplierId, doc.DocumentDate, LedgerEntryType.Purchase, 0, doc.GrandTotal, LedgerSourceType.Purchase, doc.Id, doc.Number,
            $"Alış {doc.Number}{(doc.SupplierDocumentNo is null ? "" : " / " + doc.SupplierDocumentNo)}", doc.DueDate));
        foreach (var i in doc.Items)
            await stock.RecordAsync(i.ProductId, doc.DocumentDate, StockMovementType.Purchase, i.Quantity, LedgerSourceType.Purchase, doc.Id, i.Id, $"Alış {doc.Number}", ct);
        audit.Log("Alış onaylandı", nameof(PurchaseDocument), doc.Id, doc.Number);
    }

    public async Task<PurchaseDto> CancelPurchaseAsync(Guid id, string reason, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new ValidationFailedException("reason", "İptal nedeni yazılmalıdır.");
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var doc = await db.PurchaseDocuments.Include(p => p.Items).FirstOrDefaultAsync(p => p.Id == id, ct) ?? throw new NotFoundException("Alış belgesi bulunamadı.");
        if (doc.Status == PurchaseStatus.Cancelled) throw new ConflictException("Alış zaten iptal edilmiş.");
        var wasApproved = doc.Status == PurchaseStatus.Approved;
        doc.Status = PurchaseStatus.Cancelled;
        if (wasApproved)
        {
            var original = await db.SupplierLedgerEntries.FirstAsync(e => e.SourceType == LedgerSourceType.Purchase && e.SourceId == doc.Id && e.EntryType == LedgerEntryType.Purchase, ct);
            db.SupplierLedgerEntries.Add(NewEntry(doc.SupplierId, clock.Today, LedgerEntryType.PurchaseReversal, original.Credit, original.Debit,
                LedgerSourceType.Purchase, doc.Id, doc.Number, $"Alış iptali {doc.Number}: {reason.Trim()}", null, original.Id));
            foreach (var i in doc.Items)
                await stock.RecordAsync(i.ProductId, clock.Today, StockMovementType.PurchaseReversal, -i.Quantity, LedgerSourceType.Purchase, doc.Id, i.Id, $"Alış iptali {doc.Number}", ct);
        }
        audit.Log("Alış iptal edildi", nameof(PurchaseDocument), doc.Id, $"{doc.Number}: {reason}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetPurchaseAsync(id, ct);
    }

    public async Task<PurchaseDto> GetPurchaseAsync(Guid id, CancellationToken ct = default)
    {
        var p = await db.PurchaseDocuments.AsNoTracking().Include(x => x.Supplier).Include(x => x.Items).FirstOrDefaultAsync(x => x.Id == id, ct)
            ?? throw new NotFoundException("Alış belgesi bulunamadı.");
        return ToDto(p);
    }

    private static PurchaseDto ToDto(PurchaseDocument p) => new(p.Id, p.Number, p.SupplierId, p.Supplier!.Name, p.DocumentDate, p.DueDate, p.SupplierDocumentNo, p.Status,
        p.NetTotal, p.VatTotal, p.GrandTotal, p.Note,
        p.Items.OrderBy(i => i.LineNo).Select(i => new PurchaseItemDto(i.Id, i.LineNo, i.ProductId, i.ProductName, i.Quantity, i.Unit, i.UnitPrice, i.VatRate, i.NetAmount, i.VatAmount, i.LineTotal)).ToList());

    public async Task<PagedResult<PurchaseDto>> ListPurchasesAsync(Guid? supplierId, PurchaseStatus? status, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 200);
        var q = db.PurchaseDocuments.AsNoTracking().AsQueryable();
        if (supplierId.HasValue) q = q.Where(p => p.SupplierId == supplierId);
        if (status.HasValue) q = q.Where(p => p.Status == status);
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(p => p.DocumentDate).ThenByDescending(p => p.Number).Skip((page - 1) * pageSize).Take(pageSize)
            .Include(p => p.Supplier).Include(p => p.Items).ToListAsync(ct);
        return new PagedResult<PurchaseDto>(rows.Select(ToDto).ToList(), total, page, pageSize);
    }
}
