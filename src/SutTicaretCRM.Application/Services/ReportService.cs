using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SutTicaretCRM.Domain;

namespace SutTicaretCRM.Application.Services;

public record MeasureDto(string Title, string Definition, decimal Amount, int Count, decimal? Net = null, decimal? Vat = null, string? Note = null);

public record MeasuresDto(DateOnly From, DateOnly To, MeasureDto CommercialSales, MeasureDto OfficialInvoices, MeasureDto Collections, string Explanation);

public record SalesReportRow(string Key, string Label, int SaleCount, decimal Quantity, string? Unit, decimal NetTotal, decimal VatTotal, decimal GrandTotal);

public record SalesReportDto(DateOnly From, DateOnly To, string GroupBy, string Definition, IReadOnlyList<SalesReportRow> Rows, decimal NetTotal, decimal VatTotal, decimal GrandTotal, int SaleCount);

public record CollectionRow(Guid PaymentId, string Number, DateOnly Date, Guid CustomerId, string CustomerName, PaymentMethod Method, decimal Amount, decimal Unallocated, string? BankReference);

public record CollectionsReportDto(DateOnly From, DateOnly To, string Definition, IReadOnlyList<CollectionRow> Rows, decimal Total,
    IReadOnlyDictionary<string, decimal> ByMethod);

public record ReceivableRow(Guid CustomerId, string CustomerName, string? Phone, decimal Balance, decimal Overdue, DateOnly? LastPaymentDate, DateOnly? OldestOverdueDate);

public record ReceivablesReportDto(string Definition, IReadOnlyList<ReceivableRow> Rows, decimal TotalReceivable, decimal TotalOverdue, decimal CustomerAdvances);

public record SupplierDebtRow(Guid SupplierId, string SupplierName, string? Phone, decimal Balance);

public record SupplierDebtsDto(string Definition, IReadOnlyList<SupplierDebtRow> Rows, decimal Total);

public record InvoiceStatusRow(InvoiceInternalStatus InternalStatus, OfficialInvoiceStatus OfficialStatus, int Count, decimal GrandTotal);

public record InvoiceStatusReportDto(DateOnly From, DateOnly To, string Definition, IReadOnlyList<InvoiceStatusRow> Rows);

public record OpenInvoicesDto(string Definition, IReadOnlyList<InvoiceListItem> Rows, decimal TotalRemaining);

public class ReportService(IAppDbContext db, IClock clock, LedgerService ledger, InvoiceService invoices)
{
    private static readonly SaleStatus[] CommercialStatuses = [SaleStatus.Approved, SaleStatus.PartiallyPaid, SaleStatus.Paid];

    private IQueryable<Sale> CommercialSales(DateOnly from, DateOnly to, Guid? customerId)
    {
        var q = db.Sales.AsNoTracking().Where(s => CommercialStatuses.Contains(s.Status) && s.SaleDate >= from && s.SaleDate <= to);
        if (customerId.HasValue) q = q.Where(s => s.CustomerId == customerId);
        return q;
    }

    public const string SalesDefinition = "Ticari satış = onaylanmış (iptal edilmemiş, taslak olmayan) satışların KDV dahil tutarı. Açılış bakiyeleri ve devreden borçlar satış sayılmaz.";

    /// <summary>Üç ayrı ölçü: ticari satış, düzenlenen resmî fatura, tahsilat. Birbirinden farklı olması normaldir.</summary>
    public async Task<MeasuresDto> MeasuresAsync(DateOnly from, DateOnly to, Guid? customerId, CancellationToken ct = default)
    {
        var sales = await CommercialSales(from, to, customerId).Select(s => new { s.NetTotal, s.VatTotal, s.GrandTotal }).ToListAsync(ct);

        var iq = db.Invoices.AsNoTracking().Where(i => i.OfficialStatus == OfficialInvoiceStatus.Issued && i.OfficialIssueDate >= from && i.OfficialIssueDate <= to);
        if (customerId.HasValue) iq = iq.Where(i => i.CustomerId == customerId);
        var inv = await iq.Select(i => new { i.NetTotal, i.VatTotal, i.GrandTotal, i.OfficialVerified }).ToListAsync(ct);

        var pq = db.Payments.AsNoTracking().Where(p => p.Status == PaymentStatus.Active && p.PaymentDate >= from && p.PaymentDate <= to);
        if (customerId.HasValue) pq = pq.Where(p => p.CustomerId == customerId);
        var pays = await pq.Select(p => p.Amount).ToListAsync(ct);

        var unverified = inv.Count(i => !i.OfficialVerified);
        return new MeasuresDto(from, to,
            new MeasureDto("Ticari satışlar", SalesDefinition, sales.Sum(s => s.GrandTotal), sales.Count, sales.Sum(s => s.NetTotal), sales.Sum(s => s.VatTotal)),
            new MeasureDto("Düzenlenen resmî faturalar",
                "Resmî belge bilgisi CRM'e kaydedilmiş (iptal edilmemiş) faturalar; düzenlenme tarihine göre. Taslak/iç onaylı faturalar dahil değildir.",
                inv.Sum(i => i.GrandTotal), inv.Count, inv.Sum(i => i.NetTotal), inv.Sum(i => i.VatTotal),
                unverified > 0 ? $"{unverified} faturanın resmî bilgisi kullanıcı girişidir; GİB'den doğrulanmamıştır." : null),
            new MeasureDto("Tahsil edilen ödemeler", "Geri alınmamış tahsilatların toplamı; tahsilat tarihine göre. Satış veya fatura tarihinden bağımsızdır.", pays.Sum(), pays.Count),
            "Bu üç ölçü farklı kavramlardır: satış yapılıp henüz faturalanmamış/tahsil edilmemiş olabilir; eski satışların tahsilatı bu döneme düşebilir.");
    }

    public async Task<SalesReportDto> SalesAsync(DateOnly from, DateOnly to, string groupBy, Guid? customerId, CancellationToken ct = default)
    {
        if (to < from) throw new ValidationFailedException("to", "Bitiş tarihi başlangıçtan önce olamaz.");
        groupBy = (groupBy ?? "day").ToLowerInvariant();
        var rows = new List<SalesReportRow>();

        if (groupBy == "product")
        {
            var items = await db.SaleItems.AsNoTracking()
                .Where(i => CommercialStatuses.Contains(i.Sale!.Status) && i.Sale.SaleDate >= from && i.Sale.SaleDate <= to && (customerId == null || i.Sale.CustomerId == customerId))
                .Select(i => new { i.ProductId, i.ProductName, i.Unit, i.Quantity, i.NetAmount, i.VatAmount, i.LineTotal, i.SaleId }).ToListAsync(ct);
            rows = items.GroupBy(i => (i.ProductId, i.Unit)).Select(g => new SalesReportRow(g.Key.ProductId.ToString(), g.First().ProductName, g.Select(x => x.SaleId).Distinct().Count(),
                g.Sum(x => x.Quantity), g.Key.Unit, g.Sum(x => x.NetAmount), g.Sum(x => x.VatAmount), g.Sum(x => x.LineTotal))).OrderByDescending(r => r.GrandTotal).ToList();
        }
        else
        {
            var sales = await CommercialSales(from, to, customerId)
                .Select(s => new { s.SaleDate, s.CustomerId, CustomerName = s.Customer!.Name, s.NetTotal, s.VatTotal, s.GrandTotal }).ToListAsync(ct);
            IEnumerable<IGrouping<(string Key, string Label), (DateOnly SaleDate, string CustomerName, decimal NetTotal, decimal VatTotal, decimal GrandTotal)>> groups;
            var projected = sales.Select(s => (s.SaleDate, s.CustomerName, s.NetTotal, s.VatTotal, s.GrandTotal));
            groups = groupBy switch
            {
                "customer" => projected.GroupBy(s => (s.CustomerName, s.CustomerName)),
                "week" => projected.GroupBy(s => { var m = StartOfWeek(s.SaleDate); return (m.ToString("yyyy-MM-dd"), $"{m:dd.MM.yyyy} – {m.AddDays(6):dd.MM.yyyy}"); }),
                "month" => projected.GroupBy(s => (s.SaleDate.ToString("yyyy-MM"), s.SaleDate.ToString("MMMM yyyy", new CultureInfo("tr-TR")))),
                _ => projected.GroupBy(s => (s.SaleDate.ToString("yyyy-MM-dd"), s.SaleDate.ToString("dd.MM.yyyy"))),
            };
            rows = groups.Select(g => new SalesReportRow(g.Key.Key, g.Key.Label, g.Count(), 0, null, g.Sum(x => x.NetTotal), g.Sum(x => x.VatTotal), g.Sum(x => x.GrandTotal)))
                .OrderBy(r => groupBy == "customer" ? "" : r.Key).ThenByDescending(r => groupBy == "customer" ? r.GrandTotal : 0).ToList();
        }
        var saleCount = await CommercialSales(from, to, customerId).CountAsync(ct);
        return new SalesReportDto(from, to, groupBy, SalesDefinition, rows, rows.Sum(r => r.NetTotal), rows.Sum(r => r.VatTotal), rows.Sum(r => r.GrandTotal), saleCount);
    }

    private static DateOnly StartOfWeek(DateOnly d)
    {
        var diff = ((int)d.DayOfWeek + 6) % 7; // Pazartesi başlangıç
        return d.AddDays(-diff);
    }

    public async Task<CollectionsReportDto> CollectionsAsync(DateOnly from, DateOnly to, Guid? customerId, PaymentMethod? method, CancellationToken ct = default)
    {
        var q = db.Payments.AsNoTracking().Where(p => p.Status == PaymentStatus.Active && p.PaymentDate >= from && p.PaymentDate <= to);
        if (customerId.HasValue) q = q.Where(p => p.CustomerId == customerId);
        if (method.HasValue) q = q.Where(p => p.Method == method);
        var rows = await q.OrderByDescending(p => p.PaymentDate).ThenByDescending(p => p.Number)
            .Select(p => new CollectionRow(p.Id, p.Number, p.PaymentDate, p.CustomerId, p.Customer!.Name, p.Method, p.Amount,
                p.Amount - (p.Allocations.Where(a => a.IsActive).Sum(a => (decimal?)a.Amount) ?? 0), p.BankReference)).ToListAsync(ct);
        return new CollectionsReportDto(from, to, "Geri alınmamış tahsilatlar, tahsilat tarihine göre. Bir satışın veya faturanın tarihine bağlı değildir.",
            rows, rows.Sum(r => r.Amount), rows.GroupBy(r => r.Method.ToString()).ToDictionary(g => g.Key, g => g.Sum(x => x.Amount)));
    }

    public async Task<ReceivablesReportDto> ReceivablesAsync(bool overdueOnly, Guid? customerId, CancellationToken ct = default)
    {
        var customers = await db.Customers.AsNoTracking().Where(c => customerId == null || c.Id == customerId).ToListAsync(ct);
        var fin = await ledger.GetFinancialsAsync(customers.Select(c => c.Id).ToList(), ct);
        var today = clock.Today;

        var oldest = await db.Sales.AsNoTracking()
            .Where(s => (s.Status == SaleStatus.Approved || s.Status == SaleStatus.PartiallyPaid) && s.DueDate < today)
            .GroupBy(s => s.CustomerId).Select(g => new { g.Key, D = g.Min(x => x.DueDate) }).ToDictionaryAsync(x => x.Key, x => x.D, ct);

        var rows = customers.Select(c => (c, f: fin[c.Id]))
            .Where(x => overdueOnly ? x.f.OverdueAmount > 0 : x.f.Balance > 0)
            .Select(x => new ReceivableRow(x.c.Id, x.c.Name, x.c.Phone, x.f.Balance, x.f.OverdueAmount, x.f.LastPaymentDate, oldest.TryGetValue(x.c.Id, out var d) ? d : null))
            .OrderByDescending(r => overdueOnly ? r.Overdue : r.Balance).ToList();
        var advances = fin.Values.Where(f => f.Balance < 0).Sum(f => -f.Balance);
        return new ReceivablesReportDto(
            overdueOnly
                ? "Vadesi geçmiş borç = vadesi dolmuş açık kalemler (satışlar, açılış bakiyesi, borç düzeltmeleri); dağıtılmamış tahsilat ve iadeler önce en eski borca sayılır."
                : "Müşteri alacağı = cari hesap bakiyesi (açılış bakiyesi dahil): Σ borç − Σ alacak. Avans/fazla ödeme bakiyesi olan müşteriler dahil edilmez.",
            rows, rows.Sum(r => r.Balance), rows.Sum(r => r.Overdue), advances);
    }

    public async Task<SupplierDebtsDto> SupplierDebtsAsync(CancellationToken ct = default)
    {
        var balances = await ledger.GetSupplierBalancesAsync(ct);
        var suppliers = await db.Suppliers.AsNoTracking().ToListAsync(ct);
        var rows = suppliers.Where(s => balances.GetValueOrDefault(s.Id) > 0)
            .Select(s => new SupplierDebtRow(s.Id, s.Name, s.Phone, balances[s.Id])).OrderByDescending(r => r.Balance).ToList();
        return new SupplierDebtsDto("Tedarikçi borcu = tedarikçi cari hesabında Σ alacak − Σ borç (alışlar ve açılış borcu eksi ödemeler).", rows, rows.Sum(r => r.Balance));
    }

    public async Task<InvoiceStatusReportDto> InvoiceStatusAsync(DateOnly from, DateOnly to, CancellationToken ct = default)
    {
        var rows = await db.Invoices.AsNoTracking().Where(i => i.InvoiceDate >= from && i.InvoiceDate <= to)
            .GroupBy(i => new { i.InternalStatus, i.OfficialStatus })
            .Select(g => new InvoiceStatusRow(g.Key.InternalStatus, g.Key.OfficialStatus, g.Count(), g.Sum(x => x.GrandTotal))).ToListAsync(ct);
        return new InvoiceStatusReportDto(from, to, "Fatura tarihine göre; iç süreç durumu ile resmî belge durumu ayrı sütunlardır. 'Resmî: Düzenlendi' kullanıcı girişi olabilir (GİB'den doğrulanmamış).",
            rows.OrderBy(r => r.InternalStatus).ThenBy(r => r.OfficialStatus).ToList());
    }

    public async Task<OpenInvoicesDto> OpenInvoicesAsync(Guid? customerId, CancellationToken ct = default)
    {
        var res = await invoices.ListAsync(customerId, null, null, null, null, null, true, null, 1, 200, ct);
        return new OpenInvoicesDto("Açık fatura = taslak/iptal olmayan, tahsilatı tamamlanmamış faturalar. Kalan, ilgili satışa yapılan tahsilat dağıtımlarından hesaplanır.",
            res.Items, res.Items.Sum(i => i.Remaining));
    }
}

// ───────────── Dashboard ─────────────

public record CardDto(decimal Amount, int Count);

public record DashboardSaleRow(Guid Id, string Number, string CustomerName, DateOnly Date, decimal GrandTotal, SaleStatus Status);
public record DashboardInvoiceRow(Guid Id, string InternalRef, string? OfficialNumber, string CustomerName, DateOnly Date, decimal GrandTotal, InvoiceInternalStatus InternalStatus, OfficialInvoiceStatus OfficialStatus);
public record DashboardPaymentRow(Guid Id, string Number, string CustomerName, DateOnly Date, decimal Amount, PaymentMethod Method);
public record DashboardDueRow(Guid SaleId, string SaleNumber, Guid CustomerId, string CustomerName, DateOnly DueDate, decimal Remaining);
public record DashboardBalanceRow(Guid CustomerId, string CustomerName, decimal Balance, decimal Overdue);
public record DashboardLowStockRow(Guid ProductId, string Name, string Unit, decimal Quantity, decimal MinStock);

public record DashboardDto(
    DateOnly Today, DateOnly MonthFrom, DateOnly MonthTo, bool IsDemo,
    CardDto TodaySales, CardDto MonthSales, CardDto TotalReceivable, CardDto OverdueReceivable, CardDto PendingCollections, CardDto MonthCollected,
    CardDto DraftInvoices, CardDto AwaitingOfficialInvoices, CardDto MonthInvoicesCreated, CardDto LowStock, decimal CustomerAdvances,
    IReadOnlyList<DashboardSaleRow> RecentSales, IReadOnlyList<DashboardInvoiceRow> RecentInvoices, IReadOnlyList<DashboardPaymentRow> RecentPayments,
    IReadOnlyList<DashboardDueRow> UpcomingDues, IReadOnlyList<DashboardBalanceRow> TopBalances, IReadOnlyList<DashboardLowStockRow> LowStockItems,
    IReadOnlyDictionary<string, string> Definitions);

public class DashboardService(IAppDbContext db, IClock clock, LedgerService ledger, StockService stock, SettingsService settings)
{
    public async Task<DashboardDto> GetAsync(CancellationToken ct = default)
    {
        var today = clock.Today;
        var monthFrom = new DateOnly(today.Year, today.Month, 1);
        var monthTo = monthFrom.AddMonths(1).AddDays(-1);
        SaleStatus[] commercial = [SaleStatus.Approved, SaleStatus.PartiallyPaid, SaleStatus.Paid];

        async Task<CardDto> SalesCard(DateOnly f, DateOnly t)
        {
            var l = await db.Sales.AsNoTracking().Where(s => commercial.Contains(s.Status) && s.SaleDate >= f && s.SaleDate <= t).Select(s => s.GrandTotal).ToListAsync(ct);
            return new CardDto(l.Sum(), l.Count);
        }
        var todaySales = await SalesCard(today, today);
        var monthSales = await SalesCard(monthFrom, monthTo);

        var fin = await ledger.GetFinancialsAsync(null, ct);
        var receivable = fin.Values.Where(f => f.Balance > 0).ToList();
        var totalReceivable = receivable.Sum(f => f.Balance);
        var overdue = fin.Values.Sum(f => f.OverdueAmount);
        var advances = fin.Values.Where(f => f.Balance < 0).Sum(f => -f.Balance);

        var pays = await db.Payments.AsNoTracking().Where(p => p.Status == PaymentStatus.Active && p.PaymentDate >= monthFrom && p.PaymentDate <= monthTo).Select(p => p.Amount).ToListAsync(ct);

        var drafts = await db.Invoices.AsNoTracking().Where(i => i.InternalStatus == InvoiceInternalStatus.Draft).Select(i => i.GrandTotal).ToListAsync(ct);
        var awaiting = await db.Invoices.AsNoTracking().Where(i => i.InternalStatus == InvoiceInternalStatus.Approved || i.InternalStatus == InvoiceInternalStatus.AwaitingOfficial
            || i.InternalStatus == InvoiceInternalStatus.Processing || i.InternalStatus == InvoiceInternalStatus.Error).Select(i => i.GrandTotal).ToListAsync(ct);
        var monthInv = await db.Invoices.AsNoTracking().Where(i => i.InternalStatus != InvoiceInternalStatus.Cancelled && i.InvoiceDate >= monthFrom && i.InvoiceDate <= monthTo).Select(i => i.GrandTotal).ToListAsync(ct);

        var low = (await stock.GetLevelsAsync(true, null, ct)).ToList();

        var recentSales = await db.Sales.AsNoTracking().Where(s => s.Status != SaleStatus.Cancelled).OrderByDescending(s => s.SaleDate).ThenByDescending(s => s.Number).Take(6)
            .Select(s => new DashboardSaleRow(s.Id, s.Number, s.Customer!.Name, s.SaleDate, s.GrandTotal, s.Status)).ToListAsync(ct);
        var recentInvoices = await db.Invoices.AsNoTracking().Where(i => i.InternalStatus != InvoiceInternalStatus.Cancelled).OrderByDescending(i => i.CreatedAtUtc).Take(6)
            .Select(i => new DashboardInvoiceRow(i.Id, i.InternalRef, i.OfficialNumber, i.Customer!.Name, i.InvoiceDate, i.GrandTotal, i.InternalStatus, i.OfficialStatus)).ToListAsync(ct);
        var recentPayments = await db.Payments.AsNoTracking().Where(p => p.Status == PaymentStatus.Active).OrderByDescending(p => p.PaymentDate).ThenByDescending(p => p.CreatedAtUtc).Take(6)
            .Select(p => new DashboardPaymentRow(p.Id, p.Number, p.Customer!.Name, p.PaymentDate, p.Amount, p.Method)).ToListAsync(ct);

        var horizon = today.AddDays(14);
        var dueRows = await db.Sales.AsNoTracking()
            .Where(s => (s.Status == SaleStatus.Approved || s.Status == SaleStatus.PartiallyPaid) && s.DueDate >= today && s.DueDate <= horizon)
            .OrderBy(s => s.DueDate).Take(8)
            .Select(s => new { s.Id, s.Number, s.CustomerId, CustomerName = s.Customer!.Name, s.DueDate, s.GrandTotal,
                Paid = db.PaymentAllocations.Where(a => a.SaleId == s.Id && a.IsActive).Sum(a => (decimal?)a.Amount) ?? 0 }).ToListAsync(ct);
        var dues = dueRows.Select(d => new DashboardDueRow(d.Id, d.Number, d.CustomerId, d.CustomerName, d.DueDate, d.GrandTotal - d.Paid)).Where(d => d.Remaining > 0).ToList();

        var top = fin.Values.Where(f => f.Balance > 0).OrderByDescending(f => f.Balance).Take(5).ToList();
        var names = await db.Customers.AsNoTracking().Where(c => top.Select(t => t.CustomerId).Contains(c.Id)).ToDictionaryAsync(c => c.Id, c => c.Name, ct);

        return new DashboardDto(today, monthFrom, monthTo, await settings.GetDatabaseModeAsync(ct) == "Demo",
            todaySales, monthSales, new CardDto(totalReceivable, receivable.Count), new CardDto(overdue, fin.Values.Count(f => f.OverdueAmount > 0)),
            new CardDto(Math.Max(totalReceivable - overdue, 0), 0), new CardDto(pays.Sum(), pays.Count),
            new CardDto(drafts.Sum(), drafts.Count), new CardDto(awaiting.Sum(), awaiting.Count), new CardDto(monthInv.Sum(), monthInv.Count),
            new CardDto(0, low.Count), advances,
            recentSales, recentInvoices, recentPayments, dues,
            top.Select(t => new DashboardBalanceRow(t.CustomerId, names.GetValueOrDefault(t.CustomerId, "?"), t.Balance, t.OverdueAmount)).ToList(),
            low.Take(8).Select(l => new DashboardLowStockRow(l.ProductId, l.Name, l.Unit, l.Quantity, l.MinStock)).ToList(),
            new Dictionary<string, string>
            {
                ["sales"] = "Satış = onaylanmış satışlar (taslak ve iptaller hariç). Açılış bakiyesi satış değildir.",
                ["receivable"] = "Toplam alacak = müşteri cari bakiyelerinin (açılış dahil) pozitif toplamı.",
                ["overdue"] = "Vadesi geçmiş = vadesi dolmuş açık kalemler; dağıtılmamış tahsilatlar önce eski borca sayılır.",
                ["pending"] = "Bekleyen tahsilat = toplam alacak − vadesi geçmiş tutar (vadesi henüz gelmemiş).",
                ["collected"] = "Bu ay tahsil edilen = geri alınmamış tahsilatlar.",
                ["draftInvoices"] = "Taslak = iç onayı verilmemiş faturalar.",
                ["awaiting"] = "İç onayı verilmiş ama resmî belgesi kaydedilmemiş faturalar.",
                ["monthInvoices"] = "Bu ay hazırlanan (iptal edilmemiş) faturalar; resmî düzenleme anlamına gelmez.",
            });
    }
}
