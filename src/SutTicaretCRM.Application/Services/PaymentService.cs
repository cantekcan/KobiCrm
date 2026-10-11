using Microsoft.EntityFrameworkCore;
using SutTicaretCRM.Domain;

namespace SutTicaretCRM.Application.Services;

public record AllocationRequest(Guid SaleId, decimal Amount);

public record PaymentRequest(
    Guid CustomerId, DateOnly PaymentDate, decimal Amount, PaymentMethod Method, string? BankReference, string? Note,
    List<AllocationRequest>? Allocations, bool AutoAllocate, Guid? ClientRequestId);

public record AllocationDto(Guid Id, Guid SaleId, string SaleNumber, Guid? InvoiceId, string? InvoiceRef, string? OfficialNumber, decimal Amount, bool IsActive);

public record PaymentDto(
    Guid Id, string Number, Guid CustomerId, string CustomerName, DateOnly PaymentDate, decimal Amount, PaymentMethod Method,
    string? BankReference, string? Note, PaymentStatus Status, decimal AllocatedAmount, decimal UnallocatedAmount, bool HasReceipt,
    DateTime? ReversedAtUtc, string? ReverseReason, DateTime CreatedAtUtc, IReadOnlyList<AllocationDto> Allocations);

public record OpenSaleDto(
    Guid SaleId, string Number, DateOnly SaleDate, DateOnly DueDate, decimal GrandTotal, decimal Paid, decimal Remaining,
    Guid? InvoiceId, string? InvoiceRef, string? OfficialNumber, bool IsOverdue);

public class PaymentService(
    IAppDbContext db, ICurrentUser user, IClock clock, INumberSequence numbers, LedgerService ledger, IAuditLogger audit, IFileStore files)
{
    public const string ReceiptFolder = "receipts";

    // ───────────── Oluşturma ─────────────

    public async Task<PaymentDto> CreateAsync(PaymentRequest r, CancellationToken ct = default)
    {
        if (r.ClientRequestId is { } key)
        {
            var existing = await db.Payments.Where(p => p.ClientRequestId == key).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
            if (existing.HasValue) return await GetAsync(existing.Value, ct);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var payment = await CreateCoreAsync(r, ct);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (r.ClientRequestId.HasValue)
        {
            // Aynı istek anahtarıyla eşzamanlı ikinci istek: mükerrer kayıt yerine mevcut kaydı döndür.
            await tx.RollbackAsync(ct);
            db.ChangeTracker.Clear();
            var existing = await db.Payments.Where(p => p.ClientRequestId == r.ClientRequestId).Select(p => (Guid?)p.Id).FirstOrDefaultAsync(ct);
            if (existing.HasValue) return await GetAsync(existing.Value, ct);
            throw;
        }
        await tx.CommitAsync(ct);
        return await GetAsync(payment.Id, ct);
    }

    /// <summary>Çağıran işlem transaction ve SaveChanges'i yönetir (ör. peşin satış tahsilatı).</summary>
    public async Task<Payment> CreateCoreAsync(PaymentRequest r, CancellationToken ct)
    {
        var v = new ValidationBuilder();
        v.When(r.Amount <= 0, "amount", "Tahsilat tutarı sıfırdan büyük olmalıdır.");
        v.When(Money.Round(r.Amount) != r.Amount, "amount", "Tutar en fazla 2 ondalık basamaklı olabilir.");
        v.When(r.PaymentDate > clock.Today.AddDays(1), "paymentDate", "Tahsilat tarihi ileri bir tarih olamaz.");
        v.When(r.BankReference?.Length > 100, "bankReference", "Banka referansı en fazla 100 karakter olabilir.");
        v.When(r.AutoAllocate && r.Allocations is { Count: > 0 }, "allocations", "Otomatik dağıtım ile elle dağıtım birlikte kullanılamaz.");
        v.ThrowIfAny();

        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == r.CustomerId, ct) ?? throw new NotFoundException("Müşteri bulunamadı.");

        var payment = new Payment
        {
            Number = await numbers.NextAsync("THS", r.PaymentDate.Year, ct),
            CustomerId = customer.Id, PaymentDate = r.PaymentDate, Amount = r.Amount, Method = r.Method,
            BankReference = string.IsNullOrWhiteSpace(r.BankReference) ? null : r.BankReference.Trim(),
            Note = string.IsNullOrWhiteSpace(r.Note) ? null : r.Note.Trim(),
            ClientRequestId = r.ClientRequestId, CreatedByUserId = user.UserId,
        };
        db.Payments.Add(payment);
        ledger.PostPayment(payment);

        List<AllocationRequest> plan;
        if (r.AutoAllocate)
        {
            plan = [];
            var left = r.Amount;
            foreach (var open in await GetOpenSalesAsync(customer.Id, ct))
            {
                if (left <= 0) break;
                var take = Math.Min(left, open.Remaining);
                plan.Add(new AllocationRequest(open.SaleId, take));
                left -= take;
            }
        }
        else plan = r.Allocations ?? [];

        if (plan.Count > 0) await AllocateCoreAsync(payment, plan, ct);
        audit.Log("Tahsilat girildi", nameof(Payment), payment.Id, $"{payment.Number}: {payment.Amount:N2} TL, {payment.Method}");
        return payment;
    }

    private async Task AllocateCoreAsync(Payment payment, List<AllocationRequest> plan, CancellationToken ct)
    {
        var v = new ValidationBuilder();
        v.When(plan.Any(a => a.Amount <= 0), "allocations", "Dağıtım tutarları sıfırdan büyük olmalıdır.");
        v.When(plan.GroupBy(a => a.SaleId).Any(g => g.Count() > 1), "allocations", "Aynı satış birden fazla kez seçilemez.");
        v.ThrowIfAny();

        var alreadyAllocated = payment.Allocations.Where(a => a.IsActive).Sum(a => a.Amount);
        var newTotal = plan.Sum(a => a.Amount);
        if (alreadyAllocated + newTotal > payment.Amount)
            throw new ValidationFailedException("allocations", $"Dağıtılan tutar ({alreadyAllocated + newTotal:N2}) tahsilat tutarını ({payment.Amount:N2}) aşıyor.");

        var saleIds = plan.Select(a => a.SaleId).ToList();
        // Aynı işlemde henüz kaydedilmemiş (peşin) satış olabilir: önce izlenen kayıtlara, sonra veritabanına bak.
        var sales = db.Sales.Local.Where(s => saleIds.Contains(s.Id)).ToList();
        var missing = saleIds.Except(sales.Select(s => s.Id)).ToList();
        if (missing.Count > 0) sales.AddRange(await db.Sales.Where(s => missing.Contains(s.Id)).ToListAsync(ct));
        var paidBySale = await db.PaymentAllocations.Where(a => a.IsActive && saleIds.Contains(a.SaleId))
            .GroupBy(a => a.SaleId).Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        var invoiceBySale = await db.Invoices.Where(i => saleIds.Contains(i.SaleId) && i.InternalStatus != InvoiceInternalStatus.Cancelled)
            .Select(i => new { i.SaleId, i.Id }).ToListAsync(ct);

        foreach (var a in plan)
        {
            var sale = sales.FirstOrDefault(s => s.Id == a.SaleId) ?? throw new NotFoundException("Dağıtım yapılan satış bulunamadı.");
            if (sale.CustomerId != payment.CustomerId) throw new ValidationFailedException("allocations", $"{sale.Number} numaralı satış bu müşteriye ait değil.");
            if (sale.Status is not (SaleStatus.Approved or SaleStatus.PartiallyPaid))
                throw new ValidationFailedException("allocations", $"{sale.Number} numaralı satışa tahsilat dağıtılamaz (durum: {sale.Status}).");
            var paid = paidBySale.GetValueOrDefault(sale.Id);
            var remaining = sale.GrandTotal - paid;
            if (a.Amount > remaining)
                throw new ValidationFailedException("allocations", $"{sale.Number} için kalan borç {remaining:N2} TL; {a.Amount:N2} TL dağıtılamaz.");

            payment.Allocations.Add(new PaymentAllocation
            {
                PaymentId = payment.Id, SaleId = sale.Id, Amount = a.Amount, IsActive = true,
                InvoiceId = invoiceBySale.FirstOrDefault(i => i.SaleId == sale.Id)?.Id,
            });
            db.PaymentAllocations.Add(payment.Allocations[^1]);
            sale.RefreshPaymentStatus(paid + a.Amount);
            sale.UpdatedAtUtc = clock.UtcNow;
        }
    }

    /// <summary>Dağıtılmamış tahsilat bakiyesini sonradan açık satışlara dağıtır.</summary>
    public async Task<PaymentDto> AllocateAsync(Guid paymentId, List<AllocationRequest> allocations, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var payment = await db.Payments.Include(p => p.Allocations).FirstOrDefaultAsync(p => p.Id == paymentId, ct)
            ?? throw new NotFoundException("Tahsilat bulunamadı.");
        if (payment.Status != PaymentStatus.Active) throw new ConflictException("Geri alınmış tahsilata dağıtım yapılamaz.");
        if (allocations.Count == 0) throw new ValidationFailedException("allocations", "Dağıtılacak satış seçilmedi.");
        await AllocateCoreAsync(payment, allocations, ct);
        audit.Log("Tahsilat dağıtıldı", nameof(Payment), payment.Id, $"{payment.Number}: {allocations.Sum(a => a.Amount):N2} TL");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(paymentId, ct);
    }

    // ───────────── Geri alma ─────────────

    /// <summary>Tahsilatı siler değil geri alır: durum değişir, dağıtımlar pasifleşir, cariye ters kayıt düşer. İz korunur.</summary>
    public async Task<PaymentDto> ReverseAsync(Guid paymentId, string reason, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var payment = await db.Payments.Include(p => p.Allocations).FirstOrDefaultAsync(p => p.Id == paymentId, ct)
            ?? throw new NotFoundException("Tahsilat bulunamadı.");
        payment.Reverse(reason, clock.UtcNow);

        var affected = payment.Allocations.Where(a => a.IsActive).ToList();
        foreach (var a in affected) { a.IsActive = false; a.ReversedAtUtc = clock.UtcNow; }
        await ledger.ReversePaymentAsync(payment, reason, ct);

        var saleIds = affected.Select(a => a.SaleId).Distinct().ToList();
        var sales = await db.Sales.Where(s => saleIds.Contains(s.Id)).ToListAsync(ct);
        var paidBySale = await db.PaymentAllocations.Where(a => a.IsActive && saleIds.Contains(a.SaleId) && a.PaymentId != paymentId)
            .GroupBy(a => a.SaleId).Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        foreach (var s in sales) s.RefreshPaymentStatus(paidBySale.GetValueOrDefault(s.Id));

        audit.Log("Tahsilat geri alındı", nameof(Payment), payment.Id, $"{payment.Number}: {payment.Amount:N2} TL — {reason}");
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(paymentId, ct);
    }

    // ───────────── Sorgular ─────────────

    public async Task<IReadOnlyList<OpenSaleDto>> GetOpenSalesAsync(Guid customerId, CancellationToken ct = default)
    {
        var today = clock.Today;
        var sales = await db.Sales.AsNoTracking()
            .Where(s => s.CustomerId == customerId && (s.Status == SaleStatus.Approved || s.Status == SaleStatus.PartiallyPaid))
            .OrderBy(s => s.DueDate).ThenBy(s => s.SaleDate).ThenBy(s => s.Number).ToListAsync(ct);
        var ids = sales.Select(s => s.Id).ToList();
        var paid = await db.PaymentAllocations.AsNoTracking().Where(a => a.IsActive && ids.Contains(a.SaleId))
            .GroupBy(a => a.SaleId).Select(g => new { g.Key, Sum = g.Sum(x => x.Amount) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
        var invoices = await db.Invoices.AsNoTracking().Where(i => ids.Contains(i.SaleId) && i.InternalStatus != InvoiceInternalStatus.Cancelled)
            .Select(i => new { i.SaleId, i.Id, i.InternalRef, i.OfficialNumber }).ToListAsync(ct);
        return sales.Select(s =>
        {
            var p = paid.GetValueOrDefault(s.Id);
            var inv = invoices.FirstOrDefault(i => i.SaleId == s.Id);
            return new OpenSaleDto(s.Id, s.Number, s.SaleDate, s.DueDate, s.GrandTotal, p, s.GrandTotal - p, inv?.Id, inv?.InternalRef, inv?.OfficialNumber, s.DueDate < today);
        }).Where(o => o.Remaining > 0).ToList();
    }

    public async Task<PaymentDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var p = await db.Payments.AsNoTracking().Include(x => x.Customer).Include(x => x.Allocations).ThenInclude(a => a.Sale)
            .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Tahsilat bulunamadı.");
        var invIds = p.Allocations.Where(a => a.InvoiceId.HasValue).Select(a => a.InvoiceId!.Value).ToList();
        var invoices = await db.Invoices.AsNoTracking().Where(i => invIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        return ToDto(p, invoices);
    }

    private static PaymentDto ToDto(Payment p, Dictionary<Guid, Invoice> invoices)
    {
        var active = p.Allocations.Where(a => a.IsActive).Sum(a => a.Amount);
        var allocs = p.Allocations.OrderBy(a => a.CreatedAtUtc).Select(a =>
        {
            invoices.TryGetValue(a.InvoiceId ?? Guid.Empty, out var inv);
            return new AllocationDto(a.Id, a.SaleId, a.Sale?.Number ?? "", a.InvoiceId, inv?.InternalRef, inv?.OfficialNumber, a.Amount, a.IsActive);
        }).ToList();
        var unallocated = p.Status == PaymentStatus.Active ? p.Amount - active : 0;
        return new PaymentDto(p.Id, p.Number, p.CustomerId, p.Customer?.Name ?? "", p.PaymentDate, p.Amount, p.Method, p.BankReference, p.Note,
            p.Status, active, unallocated, p.ReceiptStoredName != null, p.ReversedAtUtc, p.ReverseReason, p.CreatedAtUtc, allocs);
    }

    public async Task<PagedResult<PaymentDto>> ListAsync(Guid? customerId, DateOnly? from, DateOnly? to, PaymentMethod? method,
        PaymentStatus? status, bool? unallocatedOnly, string? search, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 200);
        var q = db.Payments.AsNoTracking().AsQueryable();
        if (customerId.HasValue) q = q.Where(p => p.CustomerId == customerId);
        if (from.HasValue) q = q.Where(p => p.PaymentDate >= from);
        if (to.HasValue) q = q.Where(p => p.PaymentDate <= to);
        if (method.HasValue) q = q.Where(p => p.Method == method);
        if (status.HasValue) q = q.Where(p => p.Status == status);
        if (unallocatedOnly == true)
            q = q.Where(p => p.Status == PaymentStatus.Active && p.Amount > (p.Allocations.Where(a => a.IsActive).Sum(a => (decimal?)a.Amount) ?? 0));
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(p => p.Number.ToLower().Contains(s) || p.Customer!.Name.ToLower().Contains(s) || (p.BankReference != null && p.BankReference.ToLower().Contains(s)));
        }
        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(p => p.PaymentDate).ThenByDescending(p => p.CreatedAtUtc)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Include(p => p.Customer).Include(p => p.Allocations).ThenInclude(a => a.Sale).ToListAsync(ct);
        var invIds = rows.SelectMany(p => p.Allocations).Where(a => a.InvoiceId.HasValue).Select(a => a.InvoiceId!.Value).Distinct().ToList();
        var invoices = await db.Invoices.AsNoTracking().Where(i => invIds.Contains(i.Id)).ToDictionaryAsync(i => i.Id, ct);
        return new PagedResult<PaymentDto>(rows.Select(p => ToDto(p, invoices)).ToList(), total, page, pageSize);
    }

    // ───────────── Dekont ─────────────

    public async Task<PaymentDto> AttachReceiptAsync(Guid paymentId, Stream content, string fileName, CancellationToken ct = default)
    {
        var payment = await db.Payments.FirstOrDefaultAsync(p => p.Id == paymentId, ct) ?? throw new NotFoundException("Tahsilat bulunamadı.");
        var check = await FileValidator.ValidateAsync(content, fileName, invoiceFile: false, ct);
        var stored = await files.SaveAsync(ReceiptFolder, content, check.Extension, ct);
        payment.ReceiptStoredName = stored.StoredName;
        payment.ReceiptOriginalName = Path.GetFileName(fileName);
        payment.ReceiptContentType = check.ContentType;
        audit.Log("Dekont yüklendi", nameof(Payment), payment.Id, payment.Number);
        await db.SaveChangesAsync(ct);
        return await GetAsync(paymentId, ct);
    }

    public async Task<(Stream Content, string ContentType, string FileName)> OpenReceiptAsync(Guid paymentId, CancellationToken ct = default)
    {
        var p = await db.Payments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == paymentId, ct) ?? throw new NotFoundException("Tahsilat bulunamadı.");
        if (p.ReceiptStoredName is null) throw new NotFoundException("Bu tahsilat için dekont yok.");
        var s = await files.OpenReadAsync(ReceiptFolder, p.ReceiptStoredName, ct) ?? throw new NotFoundException("Dekont dosyası bulunamadı.");
        return (s, p.ReceiptContentType ?? "application/octet-stream", p.ReceiptOriginalName ?? "dekont");
    }
}
