using Microsoft.EntityFrameworkCore;
using SutTicaretCRM.Domain;

namespace SutTicaretCRM.Application.Services;

public record SaleItemRequest(Guid ProductId, decimal Quantity, decimal? UnitPrice, decimal DiscountPercent, decimal DiscountAmount, decimal? VatRate);

public record ImmediatePayment(decimal Amount, PaymentMethod Method, string? BankReference);

public record SaleRequest(
    Guid CustomerId, DateOnly SaleDate, PaymentType PaymentType, DateOnly? DueDate, string? Note,
    List<SaleItemRequest> Items,
    /// <summary>true: kaydet ve onayla (cariye işler, stok düşer). false: taslak olarak sakla.</summary>
    bool Approve,
    Guid? ClientRequestId,
    ImmediatePayment? Payment);

public record SaleItemDto(
    Guid Id, int LineNo, Guid ProductId, string ProductCode, string ProductName, decimal Quantity, string Unit, decimal UnitPrice,
    decimal DiscountPercent, decimal DiscountAmount, decimal VatRate, decimal NetAmount, decimal VatAmount, decimal LineTotal);

public record SaleInvoiceRef(Guid Id, string InternalRef, string? OfficialNumber, InvoiceInternalStatus InternalStatus, OfficialInvoiceStatus OfficialStatus);

public record SaleDto(
    Guid Id, string Number, Guid CustomerId, string CustomerName, DateOnly SaleDate, PaymentType PaymentType, DateOnly DueDate,
    SaleStatus Status, decimal GrossTotal, decimal DiscountTotal, decimal NetTotal, decimal VatTotal, decimal GrandTotal,
    decimal PaidTotal, decimal Remaining, string? Note, string? CancelReason, DateTime CreatedAtUtc,
    IReadOnlyList<SaleItemDto> Items, IReadOnlyList<SaleInvoiceRef> Invoices, IReadOnlyList<string> Warnings);

public record SaleListItem(
    Guid Id, string Number, Guid CustomerId, string CustomerName, DateOnly SaleDate, DateOnly DueDate, SaleStatus Status, PaymentType PaymentType,
    decimal NetTotal, decimal VatTotal, decimal GrandTotal, decimal PaidTotal, decimal Remaining,
    Guid? InvoiceId, string? InvoiceRef, string? InvoiceOfficialNumber);

public class SaleService(
    IAppDbContext db, ICurrentUser user, IClock clock, INumberSequence numbers, SettingsService settings,
    PricingService pricing, LedgerService ledger, StockService stock, PaymentService payments, IAuditLogger audit)
{
    // ───────────── Oluşturma / düzenleme ─────────────

    public async Task<SaleDto> CreateAsync(SaleRequest r, CancellationToken ct = default)
    {
        if (r.ClientRequestId is { } key)
        {
            var existing = await db.Sales.Where(s => s.ClientRequestId == key).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
            if (existing.HasValue) return await GetAsync(existing.Value, ct);
        }

        var warnings = new List<string>();
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        Sale sale;
        try
        {
            var customer = await LoadCustomerAsync(r.CustomerId, ct);
            sale = new Sale
            {
                Number = await numbers.NextAsync("SAT", r.SaleDate.Year, ct),
                CustomerId = customer.Id, ClientRequestId = r.ClientRequestId, CreatedByUserId = user.UserId,
            };
            await ApplyAsync(sale, customer, r, ct);
            db.Sales.Add(sale);
            audit.Log("Satış oluşturuldu", nameof(Sale), sale.Id, $"{sale.Number}: {sale.GrandTotal:N2} TL");

            if (r.Approve)
            {
                warnings.AddRange(await ApproveCoreAsync(sale, ct));
                if (r.Payment is { } p)
                    await ApplyImmediatePaymentAsync(sale, p, ct);
            }
            else if (r.Payment is not null)
                throw new ValidationFailedException("payment", "Tahsilat yalnızca onaylanan satış için girilebilir.");

            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (r.ClientRequestId.HasValue)
        {
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var existing = await db.Sales.Where(s => s.ClientRequestId == r.ClientRequestId).Select(s => (Guid?)s.Id).FirstOrDefaultAsync(ct);
            if (existing.HasValue) return await GetAsync(existing.Value, ct);
            throw;
        }
        await tx.CommitAsync(ct);
        var dto = await GetAsync(sale.Id, ct);
        return dto with { Warnings = warnings };
    }

    public async Task<SaleDto> UpdateDraftAsync(Guid id, SaleRequest r, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var sale = await db.Sales.Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Satış bulunamadı.");
        if (sale.Status != SaleStatus.Draft) throw new ConflictException("Yalnızca taslak satış düzenlenebilir. Onaylı satışta hata varsa satışı iptal edip yenisini girin.");
        var customer = await LoadCustomerAsync(r.CustomerId, ct);
        db.SaleItems.RemoveRange(sale.Items);
        sale.Items.Clear();
        sale.CustomerId = customer.Id;
        await ApplyAsync(sale, customer, r, ct);
        // İstemci tarafında üretilen Guid anahtarlı yeni kalemler, izlenen satışa eklenince "Modified" sanılır; açıkça Added yapılır.
        foreach (var item in sale.Items) db.SaleItems.Add(item);
        sale.UpdatedAtUtc = clock.UtcNow;

        var warnings = new List<string>();
        if (r.Approve)
        {
            warnings.AddRange(await ApproveCoreAsync(sale, ct));
            if (r.Payment is { } p) await ApplyImmediatePaymentAsync(sale, p, ct);
        }
        audit.Log("Satış güncellendi", nameof(Sale), sale.Id, $"{sale.Number}: {sale.GrandTotal:N2} TL");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetAsync(id, ct)) with { Warnings = warnings };
    }

    private async Task<Customer> LoadCustomerAsync(Guid id, CancellationToken ct)
    {
        var c = await db.Customers.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Müşteri bulunamadı.");
        if (!c.IsActive) throw new ConflictException("Pasif müşteriye satış yapılamaz.");
        return c;
    }

    /// <summary>Kalemleri sunucuda hesaplar. İstemciden gelen toplamlara güvenilmez; yalnızca miktar/fiyat/indirim/KDV oranı girdi olarak alınır.</summary>
    private async Task ApplyAsync(Sale sale, Customer customer, SaleRequest r, CancellationToken ct)
    {
        var v = new ValidationBuilder();
        v.When(r.Items is null || r.Items.Count == 0, "items", "Satışa en az bir ürün eklenmelidir.");
        v.When(r.SaleDate > clock.Today.AddDays(1), "saleDate", "Satış tarihi ileri bir tarih olamaz.");
        v.When(r.PaymentType == PaymentType.Credit && r.DueDate.HasValue && r.DueDate < r.SaleDate, "dueDate", "Vade tarihi satış tarihinden önce olamaz.");
        v.ThrowIfAny();
        if (r.Items!.Count > 200) throw new ValidationFailedException("items", "Bir satışta en fazla 200 kalem olabilir.");

        var productIds = r.Items.Select(i => i.ProductId).Distinct().ToList();
        var products = await db.Products.Where(p => productIds.Contains(p.Id)).ToDictionaryAsync(p => p.Id, ct);
        var customerPrices = await pricing.GetCustomerPricesAsync(customer.Id, ct);
        var allowedVat = await settings.GetVatRatesAsync(ct);

        var results = new List<LineResult>();
        var lineNo = 0;
        foreach (var item in r.Items)
        {
            lineNo++;
            var field = $"items[{lineNo - 1}]";
            if (!products.TryGetValue(item.ProductId, out var product)) throw new ValidationFailedException(field, $"{lineNo}. satırdaki ürün bulunamadı.");
            if (!product.IsActive) throw new ValidationFailedException(field, $"'{product.Name}' pasif bir ürün.");
            var vat = item.VatRate ?? product.VatRate;
            if (!allowedVat.Contains(vat)) throw new ValidationFailedException(field, $"{lineNo}. satırdaki %{vat} KDV oranı tanımlı değil.");
            var price = item.UnitPrice ?? (customerPrices.TryGetValue(product.Id, out var special) ? special : product.SalePrice);

            LineResult calc;
            try { calc = DocumentCalculator.CalculateLine(new LineInput(item.Quantity, price, item.DiscountPercent, item.DiscountAmount, vat)); }
            catch (DomainException ex) { throw new ValidationFailedException(field, $"{lineNo}. satır: {ex.Message}"); }
            results.Add(calc);

            sale.Items.Add(new SaleItem
            {
                SaleId = sale.Id, LineNo = lineNo, ProductId = product.Id, ProductCode = product.Code, ProductName = product.Name,
                Quantity = item.Quantity, Unit = product.Unit, UnitPrice = price,
                DiscountPercent = item.DiscountPercent, DiscountAmount = item.DiscountAmount, VatRate = vat,
                NetAmount = calc.Net, VatAmount = calc.Vat, LineTotal = calc.Total,
            });
        }
        var totals = DocumentCalculator.Sum(results);
        sale.SaleDate = r.SaleDate;
        sale.PaymentType = r.PaymentType;
        sale.DueDate = r.PaymentType == PaymentType.Cash ? r.SaleDate : r.DueDate ?? r.SaleDate.AddDays(customer.PaymentTermDays);
        sale.Note = string.IsNullOrWhiteSpace(r.Note) ? null : r.Note.Trim();
        sale.GrossTotal = totals.Gross; sale.DiscountTotal = totals.Discount; sale.NetTotal = totals.Net;
        sale.VatTotal = totals.Vat; sale.GrandTotal = totals.Total;
    }

    // ───────────── Onay ─────────────

    public async Task<SaleDto> ApproveAsync(Guid id, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var sale = await db.Sales.Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Satış bulunamadı.");
        var warnings = await ApproveCoreAsync(sale, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return (await GetAsync(id, ct)) with { Warnings = warnings };
    }

    /// <summary>
    /// Satışı onaylar: cariye TEK borç hareketi işler ve stok düşer. Aynı satış ikinci kez işlenemez
    /// (durum kontrolü + veritabanında kaynak bazlı benzersiz indeks).
    /// </summary>
    private async Task<List<string>> ApproveCoreAsync(Sale sale, CancellationToken ct)
    {
        var warnings = new List<string>();
        sale.Approve(clock.UtcNow);

        var shortages = await stock.CheckAvailabilityAsync(sale.Items.Select(i => (i.ProductId, i.Quantity)), ct);
        if (shortages.Count > 0)
        {
            if (!await settings.GetAllowNegativeStockAsync(ct))
                throw new ConflictException("Yetersiz stok: " + string.Join("; ", shortages));
            warnings.AddRange(shortages.Select(s => "Stok negatife düşecek — " + s));
        }

        await ledger.PostSaleAsync(sale, ct);
        foreach (var item in sale.Items)
            await stock.RecordAsync(item.ProductId, sale.SaleDate, StockMovementType.Sale, -item.Quantity,
                LedgerSourceType.Sale, sale.Id, item.Id, $"Satış {sale.Number}", ct);
        audit.Log("Satış onaylandı", nameof(Sale), sale.Id, $"{sale.Number}: {sale.GrandTotal:N2} TL");
        return warnings;
    }

    private async Task ApplyImmediatePaymentAsync(Sale sale, ImmediatePayment p, CancellationToken ct)
    {
        if (p.Amount > sale.GrandTotal)
            throw new ValidationFailedException("payment.amount", $"Tahsilat ({p.Amount:N2}) satış tutarını ({sale.GrandTotal:N2}) aşamaz.");
        await payments.CreateCoreAsync(new PaymentRequest(sale.CustomerId, sale.SaleDate, p.Amount, p.Method, p.BankReference,
            $"{sale.Number} satışı peşin tahsilatı", [new AllocationRequest(sale.Id, p.Amount)], false, null), ct);
    }

    // ───────────── İptal ─────────────

    public async Task<SaleDto> CancelAsync(Guid id, string reason, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var sale = await db.Sales.Include(s => s.Items).FirstOrDefaultAsync(s => s.Id == id, ct) ?? throw new NotFoundException("Satış bulunamadı.");
        await CancelCoreAsync(sale, reason, ct);
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Satışı iptal eder: cari ters kayıt, stok ters hareket, tahsilat dağıtımlarını serbest bırakma ve henüz resmîleşmemiş taslak faturaları iptal.
    /// Resmî belgesi olan satış bu yolla iptal EDİLEMEZ; önce faturanın resmî iptal süreci tamamlanmalıdır. Hepsi tek işlemdir.
    /// </summary>
    public async Task CancelCoreAsync(Sale sale, string reason, CancellationToken ct)
    {
        var wasCommercial = sale.IsCommercialSale;

        // Süzme bellekte yapılır: aynı işlemde (ör. resmî iptal onayı) henüz kaydedilmemiş durum değişiklikleri de dikkate alınır.
        var invoices = (await db.Invoices.Where(i => i.SaleId == sale.Id).ToListAsync(ct)).Where(i => i.InternalStatus != InvoiceInternalStatus.Cancelled).ToList();
        var blocking = invoices.FirstOrDefault(i =>
            i.OfficialStatus is OfficialInvoiceStatus.Issued or OfficialInvoiceStatus.Submitting or OfficialInvoiceStatus.ResultUnknown
            || i.InternalStatus is InvoiceInternalStatus.Completed or InvoiceInternalStatus.CancellationRequired or InvoiceInternalStatus.Processing);
        if (blocking is not null)
            throw new ConflictException($"Bu satışın resmî işlemde/düzenlenmiş bir faturası var ({blocking.InternalRef}). Önce faturanın resmî iptal sürecini tamamlayın.");

        sale.Cancel(reason, clock.UtcNow);
        foreach (var inv in invoices) inv.CancelUnissued($"Satış iptal edildi: {reason}");

        if (wasCommercial)
        {
            await ledger.ReverseSaleAsync(sale, reason, ct);
            foreach (var item in sale.Items)
                await stock.RecordAsync(item.ProductId, clock.Today, StockMovementType.SaleReversal, item.Quantity,
                    LedgerSourceType.Sale, sale.Id, item.Id, $"Satış iptali {sale.Number}", ct);

            // Tahsilatlar iptal edilmez; yalnızca bu satışa yapılan dağıtım kalkar ve tutar "dağıtılmamış" bakiyeye döner.
            var allocations = await db.PaymentAllocations.Where(a => a.SaleId == sale.Id && a.IsActive).ToListAsync(ct);
            foreach (var a in allocations) { a.IsActive = false; a.ReversedAtUtc = clock.UtcNow; }
        }
        audit.Log("Satış iptal edildi", nameof(Sale), sale.Id, $"{sale.Number}: {reason}");
    }

    // ───────────── Sorgular ─────────────

    public async Task<SaleDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var s = await db.Sales.AsNoTracking().Include(x => x.Customer).Include(x => x.Items.OrderBy(i => i.LineNo))
            .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Satış bulunamadı.");
        var paid = await db.PaymentAllocations.Where(a => a.SaleId == id && a.IsActive).SumAsync(a => (decimal?)a.Amount, ct) ?? 0;
        var invoices = await db.Invoices.AsNoTracking().Where(i => i.SaleId == id).OrderBy(i => i.CreatedAtUtc)
            .Select(i => new SaleInvoiceRef(i.Id, i.InternalRef, i.OfficialNumber, i.InternalStatus, i.OfficialStatus)).ToListAsync(ct);
        return new SaleDto(s.Id, s.Number, s.CustomerId, s.Customer!.Name, s.SaleDate, s.PaymentType, s.DueDate, s.Status,
            s.GrossTotal, s.DiscountTotal, s.NetTotal, s.VatTotal, s.GrandTotal, paid, s.IsCommercialSale ? s.GrandTotal - paid : 0,
            s.Note, s.CancelReason, s.CreatedAtUtc,
            s.Items.Select(i => new SaleItemDto(i.Id, i.LineNo, i.ProductId, i.ProductCode, i.ProductName, i.Quantity, i.Unit, i.UnitPrice,
                i.DiscountPercent, i.DiscountAmount, i.VatRate, i.NetAmount, i.VatAmount, i.LineTotal)).ToList(),
            invoices, []);
    }

    public async Task<PagedResult<SaleListItem>> ListAsync(Guid? customerId, SaleStatus? status, DateOnly? from, DateOnly? to,
        string? search, bool? hasOpenBalance, bool? invoiced, bool? overdue, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 200);
        var today = clock.Today;
        var q = db.Sales.AsNoTracking().AsQueryable();
        if (customerId.HasValue) q = q.Where(s => s.CustomerId == customerId);
        if (status.HasValue) q = q.Where(s => s.Status == status);
        if (from.HasValue) q = q.Where(s => s.SaleDate >= from);
        if (to.HasValue) q = q.Where(s => s.SaleDate <= to);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var t = search.Trim().ToLower();
            q = q.Where(s => s.Number.ToLower().Contains(t) || s.Customer!.Name.ToLower().Contains(t));
        }
        if (hasOpenBalance == true) q = q.Where(s => s.Status == SaleStatus.Approved || s.Status == SaleStatus.PartiallyPaid);
        if (overdue == true) q = q.Where(s => (s.Status == SaleStatus.Approved || s.Status == SaleStatus.PartiallyPaid) && s.DueDate < today);
        if (invoiced == true) q = q.Where(s => db.Invoices.Any(i => i.SaleId == s.Id && i.InternalStatus != InvoiceInternalStatus.Cancelled));
        if (invoiced == false) q = q.Where(s => s.Status != SaleStatus.Cancelled && s.Status != SaleStatus.Draft
            && !db.Invoices.Any(i => i.SaleId == s.Id && i.InternalStatus != InvoiceInternalStatus.Cancelled));

        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(s => s.SaleDate).ThenByDescending(s => s.Number)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(s => new
            {
                s.Id, s.Number, s.CustomerId, CustomerName = s.Customer!.Name, s.SaleDate, s.DueDate, s.Status, s.PaymentType,
                s.NetTotal, s.VatTotal, s.GrandTotal,
                Paid = db.PaymentAllocations.Where(a => a.SaleId == s.Id && a.IsActive).Sum(a => (decimal?)a.Amount) ?? 0,
                Inv = db.Invoices.Where(i => i.SaleId == s.Id && i.InternalStatus != InvoiceInternalStatus.Cancelled)
                    .Select(i => new { i.Id, i.InternalRef, i.OfficialNumber }).FirstOrDefault(),
            }).ToListAsync(ct);
        var rows = items.Select(x => new SaleListItem(x.Id, x.Number, x.CustomerId, x.CustomerName, x.SaleDate, x.DueDate, x.Status, x.PaymentType,
            x.NetTotal, x.VatTotal, x.GrandTotal, x.Paid,
            x.Status is SaleStatus.Approved or SaleStatus.PartiallyPaid or SaleStatus.Paid ? x.GrandTotal - x.Paid : 0,
            x.Inv?.Id, x.Inv?.InternalRef, x.Inv?.OfficialNumber)).ToList();
        return new PagedResult<SaleListItem>(rows, total, page, pageSize);
    }
}
