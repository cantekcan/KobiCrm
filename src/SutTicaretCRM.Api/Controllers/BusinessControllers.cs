using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using SutTicaretCRM.Application;
using SutTicaretCRM.Application.Services;
using SutTicaretCRM.Domain;

namespace SutTicaretCRM.Api.Controllers;

// ───────────── Faturalar ─────────────

[ApiController, Route("api/invoices")]
public class InvoicesController(InvoiceService invoices) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<InvoiceListItem>> List([FromQuery] Guid? customerId, [FromQuery] InvoiceInternalStatus? internalStatus, [FromQuery] OfficialInvoiceStatus? officialStatus,
        [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string? search, [FromQuery] bool? open, [FromQuery] bool? awaitingOfficial,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        invoices.ListAsync(customerId, internalStatus, officialStatus, from, to, search, open, awaitingOfficial, page, pageSize, ct);

    [HttpGet("provider")]
    public Task<ProviderInfoDto> Provider(CancellationToken ct) => invoices.GetProviderInfoAsync(ct);

    [HttpGet("{id:guid}")]
    public Task<InvoiceDto> Get(Guid id, CancellationToken ct) => invoices.GetAsync(id, ct);

    public record CreateDraftRequest(Guid SaleId, Guid? ClientRequestId);

    [HttpPost("draft")]
    public async Task<ActionResult<InvoiceDto>> CreateDraft(CreateDraftRequest r, CancellationToken ct)
    {
        var inv = await invoices.CreateDraftFromSaleAsync(r.SaleId, r.ClientRequestId, ct);
        return CreatedAtAction(nameof(Get), new { id = inv.Id }, inv);
    }

    [HttpPut("{id:guid}")]
    public Task<InvoiceDto> Update(Guid id, UpdateInvoiceRequest r, CancellationToken ct) => invoices.UpdateDraftAsync(id, r, ct);

    [HttpGet("{id:guid}/check")]
    public Task<IReadOnlyList<InvoiceIssue>> Check(Guid id, CancellationToken ct) => invoices.CheckAsync(id, ct);

    /// <summary>İÇ ONAY — resmî fatura oluşturmaz.</summary>
    [HttpPost("{id:guid}/approve")]
    public Task<InvoiceDto> Approve(Guid id, CancellationToken ct) => invoices.ApproveAsync(id, ct);

    [HttpPost("{id:guid}/return-to-draft")]
    public Task<InvoiceDto> ReturnToDraft(Guid id, CancellationToken ct) => invoices.ReturnToDraftAsync(id, ct);

    [HttpPost("{id:guid}/awaiting-official")]
    public Task<InvoiceDto> AwaitingOfficial(Guid id, CancellationToken ct) => invoices.MarkAwaitingOfficialAsync(id, ct);

    [HttpPost("{id:guid}/official")]
    public Task<InvoiceDto> RecordOfficial(Guid id, RecordOfficialRequest r, CancellationToken ct) => invoices.RecordOfficialAsync(id, r, ct);

    [HttpPost("{id:guid}/cancel-draft")]
    public Task<InvoiceDto> CancelDraft(Guid id, SalesController.ReasonRequest r, CancellationToken ct) => invoices.CancelDraftAsync(id, r.Reason, ct);

    [HttpPost("{id:guid}/request-cancellation")]
    public Task<InvoiceDto> RequestCancellation(Guid id, SalesController.ReasonRequest r, CancellationToken ct) => invoices.RequestCancellationAsync(id, r.Reason, ct);

    [HttpPost("{id:guid}/withdraw-cancellation")]
    public Task<InvoiceDto> WithdrawCancellation(Guid id, CancellationToken ct) => invoices.WithdrawCancellationRequestAsync(id, ct);

    [HttpPost("{id:guid}/provider/cancel-request")]
    public Task<InvoiceDto> CancelRequest(Guid id, ProviderActionRequest r, CancellationToken ct) => invoices.RequestProviderCancellationAsync(id, r, ct);

    [HttpPost("{id:guid}/confirm-cancellation")]
    public Task<InvoiceDto> ConfirmCancellation(Guid id, ConfirmCancellationRequest r, CancellationToken ct) => invoices.ConfirmOfficialCancellationAsync(id, r, ct);

    // Sağlayıcı akışı (yalnızca etkin sağlayıcı destekliyorsa ve canlı gönderim açıksa çalışır)
    [HttpPost("{id:guid}/provider/submit")]
    public Task<InvoiceDto> Submit(Guid id, ProviderActionRequest r, CancellationToken ct) => invoices.SubmitToProviderAsync(id, r, ct);

    [HttpPost("{id:guid}/provider/issue-challenge")]
    public Task<ProviderChallenge> IssueChallenge(Guid id, ProviderActionRequest r, CancellationToken ct) => invoices.RequestIssueChallengeAsync(id, r, ct);

    [HttpPost("{id:guid}/provider/issue")]
    public Task<InvoiceDto> Issue(Guid id, ProviderActionRequest r, CancellationToken ct) => invoices.IssueViaProviderAsync(id, r, ct);

    [HttpPost("{id:guid}/provider/refresh")]
    public Task<InvoiceDto> Refresh(Guid id, CancellationToken ct) => invoices.RefreshStatusAsync(id, ct);

    [HttpPost("{id:guid}/attachments"), RequestSizeLimit(11 * 1024 * 1024)]
    public async Task<AttachmentDto> Upload(Guid id, IFormFile file, CancellationToken ct)
    {
        await using var ms = new MemoryStream();
        await file.CopyToAsync(ms, ct);
        return await invoices.AddAttachmentAsync(id, ms, file.FileName, ct);
    }

    [HttpGet("{id:guid}/attachments/{attachmentId:guid}")]
    public async Task<IActionResult> Download(Guid id, Guid attachmentId, CancellationToken ct)
    {
        var (stream, type, name) = await invoices.OpenAttachmentAsync(id, attachmentId, ct);
        return File(stream, type, name);
    }
}

// ───────────── Cari hesaplar (liste) ─────────────

[ApiController, Route("api/ledger")]
public class LedgerController(LedgerService ledger, SupplierService suppliers, CustomerService customers) : ControllerBase
{
    [HttpGet("suppliers/{id:guid}/statement")]
    public Task<LedgerStatementDto> SupplierStatement(Guid id, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) =>
        ledger.GetSupplierStatementAsync(id, from, to, ct);
}

// ───────────── Tedarikçiler ve alışlar ─────────────

[ApiController, Route("api/suppliers")]
public class SuppliersController(SupplierService suppliers) : ControllerBase
{
    [HttpGet]
    public Task<IReadOnlyList<SupplierListItem>> List([FromQuery] string? search, [FromQuery] bool? active, [FromQuery] bool? withBalance, CancellationToken ct) =>
        suppliers.ListAsync(search, active, withBalance, ct);

    [HttpGet("{id:guid}")]
    public Task<SupplierDto> Get(Guid id, CancellationToken ct) => suppliers.GetAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<SupplierDto>> Create(SupplierRequest r, CancellationToken ct)
    {
        var s = await suppliers.CreateAsync(r, ct);
        return CreatedAtAction(nameof(Get), new { id = s.Id }, s);
    }

    [HttpPut("{id:guid}")]
    public Task<SupplierDto> Update(Guid id, SupplierRequest r, CancellationToken ct) => suppliers.UpdateAsync(id, r, ct);

    [HttpPost("{id:guid}/payments")]
    public Task<LedgerEntryDto> Pay(Guid id, SupplierPaymentRequest r, CancellationToken ct) => suppliers.PaySupplierAsync(id, r, ct);
}

[ApiController, Route("api/purchases")]
public class PurchasesController(SupplierService suppliers) : ControllerBase
{
    [HttpGet]
    public Task<PagedResult<PurchaseDto>> List([FromQuery] Guid? supplierId, [FromQuery] PurchaseStatus? status, [FromQuery] int page = 1, [FromQuery] int pageSize = 25, CancellationToken ct = default) =>
        suppliers.ListPurchasesAsync(supplierId, status, page, pageSize, ct);

    [HttpGet("{id:guid}")]
    public Task<PurchaseDto> Get(Guid id, CancellationToken ct) => suppliers.GetPurchaseAsync(id, ct);

    [HttpPost]
    public async Task<ActionResult<PurchaseDto>> Create(PurchaseRequest r, CancellationToken ct)
    {
        var p = await suppliers.CreatePurchaseAsync(r, ct);
        return CreatedAtAction(nameof(Get), new { id = p.Id }, p);
    }

    [HttpPost("{id:guid}/approve")]
    public Task<PurchaseDto> Approve(Guid id, CancellationToken ct) => suppliers.ApprovePurchaseAsync(id, ct);

    [HttpPost("{id:guid}/cancel")]
    public Task<PurchaseDto> Cancel(Guid id, SalesController.ReasonRequest r, CancellationToken ct) => suppliers.CancelPurchaseAsync(id, r.Reason, ct);
}

// ───────────── Stok ─────────────

[ApiController, Route("api/stock")]
public class StockController(StockService stock) : ControllerBase
{
    [HttpGet("levels")]
    public Task<IReadOnlyList<StockLevelDto>> Levels([FromQuery] bool onlyLow, [FromQuery] string? search, CancellationToken ct) => stock.GetLevelsAsync(onlyLow, search, ct);

    [HttpGet("movements")]
    public Task<PagedResult<StockMovementDto>> Movements([FromQuery] Guid? productId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to,
        [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default) =>
        stock.GetMovementsAsync(productId, from, to, page, pageSize, ct);

    [HttpPost("adjust")]
    public Task<StockMovementDto> Adjust(StockAdjustRequest r, CancellationToken ct) => stock.AdjustAsync(r, ct);
}

// ───────────── Raporlar / Genel bakış ─────────────

[ApiController, Route("api/reports")]
public class ReportsController(ReportService reports, IClock clock, LedgerService ledger) : ControllerBase
{
    private (DateOnly From, DateOnly To) Range(DateOnly? from, DateOnly? to)
    {
        var today = clock.Today;
        var f = from ?? new DateOnly(today.Year, today.Month, 1);
        var t = to ?? today;
        if (t < f) throw new ValidationFailedException("to", "Bitiş tarihi başlangıçtan önce olamaz.");
        return (f, t);
    }

    [HttpGet("measures")]
    public Task<MeasuresDto> Measures([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? customerId, CancellationToken ct)
    { var (f, t) = Range(from, to); return reports.MeasuresAsync(f, t, customerId, ct); }

    [HttpGet("sales")]
    public Task<SalesReportDto> Sales([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] string groupBy = "day", [FromQuery] Guid? customerId = null, CancellationToken ct = default)
    { var (f, t) = Range(from, to); return reports.SalesAsync(f, t, groupBy, customerId, ct); }

    [HttpGet("collections")]
    public Task<CollectionsReportDto> Collections([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, [FromQuery] Guid? customerId, [FromQuery] PaymentMethod? method, CancellationToken ct)
    { var (f, t) = Range(from, to); return reports.CollectionsAsync(f, t, customerId, method, ct); }

    [HttpGet("receivables")]
    public Task<ReceivablesReportDto> Receivables([FromQuery] bool overdueOnly, [FromQuery] Guid? customerId, CancellationToken ct) => reports.ReceivablesAsync(overdueOnly, customerId, ct);

    [HttpGet("statement/{customerId:guid}")]
    public Task<LedgerStatementDto> Statement(Guid customerId, [FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct) => ledger.GetCustomerStatementAsync(customerId, from, to, ct);

    [HttpGet("supplier-debts")]
    public Task<SupplierDebtsDto> SupplierDebts(CancellationToken ct) => reports.SupplierDebtsAsync(ct);

    [HttpGet("open-invoices")]
    public Task<OpenInvoicesDto> OpenInvoices([FromQuery] Guid? customerId, CancellationToken ct) => reports.OpenInvoicesAsync(customerId, ct);

    [HttpGet("invoice-status")]
    public Task<InvoiceStatusReportDto> InvoiceStatus([FromQuery] DateOnly? from, [FromQuery] DateOnly? to, CancellationToken ct)
    { var (f, t) = Range(from, to); return reports.InvoiceStatusAsync(f, t, ct); }
}

[ApiController, Route("api/dashboard")]
public class DashboardController(DashboardService dashboard) : ControllerBase
{
    [HttpGet]
    public Task<DashboardDto> Get(CancellationToken ct) => dashboard.GetAsync(ct);
}

// ───────────── Ayarlar ve denetim ─────────────

public record AuditLogDto(long Id, DateTime TimestampUtc, string UserName, string Action, string EntityType, Guid? EntityId, string Details);

[ApiController, Route("api/settings")]
public class SettingsController(SettingsService settings, IAppDbContext db) : ControllerBase
{
    [HttpGet]
    public Task<SettingsDto> Get(CancellationToken ct) => settings.GetAsync(ct);

    [HttpPut]
    public Task<SettingsDto> Update(UpdateSettingsRequest r, CancellationToken ct) => settings.UpdateAsync(r, ct);

    [HttpGet("audit-logs")]
    public async Task<PagedResult<AuditLogDto>> AuditLogs([FromQuery] string? entityType, [FromQuery] Guid? entityId, [FromQuery] int page = 1, [FromQuery] int pageSize = 50, CancellationToken ct = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 200);
        var q = db.AuditLogs.AsNoTracking().AsQueryable();
        if (!string.IsNullOrWhiteSpace(entityType)) q = q.Where(a => a.EntityType == entityType);
        if (entityId.HasValue) q = q.Where(a => a.EntityId == entityId);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(a => a.Id).Skip((page - 1) * pageSize).Take(pageSize)
            .Select(a => new AuditLogDto(a.Id, a.TimestampUtc, a.UserName, a.Action, a.EntityType, a.EntityId, a.Details)).ToListAsync(ct);
        return new PagedResult<AuditLogDto>(items, total, page, pageSize);
    }
}
