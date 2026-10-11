using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SutTicaretCRM.Domain;

namespace SutTicaretCRM.Application.Services;

public record AttachmentDto(Guid Id, AttachmentKind Kind, string FileName, long SizeBytes, DateTime UploadedAtUtc);

public record InvoiceLineDto(Guid Id, int LineNo, string Description, decimal Quantity, string Unit, decimal UnitPrice,
    decimal DiscountPercent, decimal DiscountAmount, decimal VatRate, decimal NetAmount, decimal VatAmount, decimal LineTotal);

public record InvoiceIssue(string Field, string Message, bool IsBlocking);

public record InvoiceListItem(
    Guid Id, string InternalRef, string? OfficialNumber, Guid CustomerId, string CustomerName, DateOnly InvoiceDate, DateOnly? DueDate,
    InvoiceDocumentType DocumentType, decimal NetTotal, decimal VatTotal, decimal GrandTotal, decimal PaidTotal, decimal Remaining,
    InvoiceInternalStatus InternalStatus, OfficialInvoiceStatus OfficialStatus, OfficialSource OfficialSource, bool OfficialVerified,
    string? OfficialProvider, Guid SaleId, string SaleNumber, IReadOnlyList<AttachmentDto> Attachments);

public record InvoiceDto(
    Guid Id, string InternalRef, Guid CustomerId, string CustomerName, Guid SaleId, string SaleNumber, DateOnly InvoiceDate, DateOnly? DueDate,
    InvoiceDocumentType DocumentType,
    string BuyerName, string? BuyerTaxOffice, string? BuyerTaxNumber, string? BuyerNationalId, string? BuyerAddress, string? BuyerCity, string? BuyerDistrict, string? BuyerEmail,
    EInvoiceTaxpayerStatus CustomerEInvoiceStatus,
    decimal GrossTotal, decimal DiscountTotal, decimal NetTotal, decimal VatTotal, decimal GrandTotal, decimal PaidTotal, decimal Remaining, string? Note,
    InvoiceInternalStatus InternalStatus, OfficialInvoiceStatus OfficialStatus,
    string? OfficialNumber, string? OfficialUuid, DateOnly? OfficialIssueDate, InvoiceDocumentType? OfficialDocumentType,
    OfficialSource OfficialSource, string? OfficialProvider, bool OfficialVerified, bool ProviderIsLegallyValid, DateTime? OfficialRecordedAtUtc,
    string? ProviderLastMessage, string? ProviderReference,
    string? CancellationReason, DateTime? CancellationRequestedAtUtc, DateOnly? OfficialCancelDate, string? OfficialCancelReference, bool OfficialCancelVerified, string? CancelReason,
    IReadOnlyList<InvoiceLineDto> Items, IReadOnlyList<AttachmentDto> Attachments, IReadOnlyList<InvoiceIssue> Issues);

public record InvoiceLineEdit(Guid Id, string Description);

public record UpdateInvoiceRequest(
    DateOnly InvoiceDate, DateOnly? DueDate, InvoiceDocumentType DocumentType,
    string BuyerName, string? BuyerTaxOffice, string? BuyerTaxNumber, string? BuyerNationalId,
    string? BuyerAddress, string? BuyerCity, string? BuyerDistrict, string? BuyerEmail, string? Note,
    List<InvoiceLineEdit>? Lines);

public record RecordOfficialRequest(string Number, DateOnly IssueDate, string? Uuid, InvoiceDocumentType DocumentType);

public record ConfirmCancellationRequest(bool Confirmed, string Reference, DateOnly Date, bool CancelSale);

/// <summary>Gerçek işlem öncesi kullanıcının belgeyi ve alıcıyı gördüğünün kanıtı.</summary>
public record ProviderActionRequest(bool Confirm, decimal AcknowledgedGrandTotal, string AcknowledgedBuyerName, string? SmsCode = null);

public record ProviderInfoDto(string Name, string DisplayName, InvoiceProviderCapabilities Capabilities, bool LiveSendingEnabled,
    bool CanSubmit, string? SubmitBlockedReason, IReadOnlyList<string> AvailableProviders);

public class InvoiceService(
    IAppDbContext db, ICurrentUser user, IClock clock, INumberSequence numbers, SettingsService settings, SaleService sales,
    IAuditLogger audit, IFileStore files, IEnumerable<IInvoiceProvider> providers, IOptions<InvoiceOptions> options)
{
    public const string AttachmentFolder = "invoices";

    // ───────────── Sağlayıcı seçimi ─────────────

    private async Task<IInvoiceProvider> ActiveProviderAsync(CancellationToken ct)
    {
        var name = await settings.GetInvoiceProviderNameAsync(ct);
        return providers.FirstOrDefault(p => p.Name.Equals(name, StringComparison.OrdinalIgnoreCase))
            ?? providers.First(p => p.Name == "Manual");
    }

    public async Task<ProviderInfoDto> GetProviderInfoAsync(CancellationToken ct = default)
    {
        var p = await ActiveProviderAsync(ct);
        var (can, reason) = CanSubmit(p);
        return new ProviderInfoDto(p.Name, p.DisplayName, p.Capabilities, options.Value.LiveSendingEnabled, can, reason, providers.Select(x => x.Name).ToList());
    }

    private (bool Can, string? Reason) CanSubmit(IInvoiceProvider p)
    {
        if (!p.Capabilities.CanCreateDraft)
            return (false, $"Aktif yöntem ({p.DisplayName}) otomatik gönderimi desteklemiyor. Belgeyi resmî portalda düzenleyip bilgilerini buraya kaydedin.");
        if (p.Capabilities.IsLiveCapable && !options.Value.LiveSendingEnabled)
            return (false, "Canlı resmî gönderim kapalıdır (güvenlik nedeniyle varsayılan). Test ortamında doğrulanmadan açılmamalıdır.");
        return (true, null);
    }

    // ───────────── Taslak oluşturma / düzenleme ─────────────

    public async Task<InvoiceDto> CreateDraftFromSaleAsync(Guid saleId, Guid? clientRequestId, CancellationToken ct = default)
    {
        if (clientRequestId is { } key)
        {
            var existing = await db.Invoices.Where(i => i.ClientRequestId == key).Select(i => (Guid?)i.Id).FirstOrDefaultAsync(ct);
            if (existing.HasValue) return await GetAsync(existing.Value, ct);
        }

        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var sale = await db.Sales.Include(s => s.Items).Include(s => s.Customer).FirstOrDefaultAsync(s => s.Id == saleId, ct)
            ?? throw new NotFoundException("Satış bulunamadı.");
        if (!sale.IsCommercialSale) throw new ConflictException("Yalnızca onaylanmış satıştan fatura taslağı hazırlanabilir.");
        var active = await db.Invoices.Where(i => i.SaleId == saleId && i.InternalStatus != InvoiceInternalStatus.Cancelled).Select(i => i.InternalRef).FirstOrDefaultAsync(ct);
        if (active is not null) throw new ConflictException($"Bu satış için zaten bir fatura var ({active}). Yeni taslak için önce onu iptal edin.");

        var c = sale.Customer!;
        var inv = new Invoice
        {
            InternalRef = await numbers.NextAsync("TSL", clock.Today.Year, ct),
            CustomerId = c.Id, SaleId = sale.Id, InvoiceDate = clock.Today,
            DueDate = sale.PaymentType == PaymentType.Credit ? sale.DueDate : null,
            // Müşterinin mükellefiyeti BİLİNİYORSA öneri olarak doldurulur; bilinmiyorsa kullanıcı seçer.
            DocumentType = c.EInvoiceStatus switch { EInvoiceTaxpayerStatus.Registered => InvoiceDocumentType.EInvoice, EInvoiceTaxpayerStatus.NotRegistered => InvoiceDocumentType.EArchive, _ => InvoiceDocumentType.Undetermined },
            BuyerName = c.Name, BuyerTaxOffice = c.TaxOffice, BuyerTaxNumber = c.TaxNumber, BuyerNationalId = c.NationalId,
            BuyerAddress = c.Address, BuyerCity = c.City, BuyerDistrict = c.District, BuyerEmail = c.Email,
            GrossTotal = sale.GrossTotal, DiscountTotal = sale.DiscountTotal, NetTotal = sale.NetTotal, VatTotal = sale.VatTotal, GrandTotal = sale.GrandTotal,
            Note = sale.Note, ClientRequestId = clientRequestId, CreatedByUserId = user.UserId,
        };
        foreach (var i in sale.Items.OrderBy(x => x.LineNo))
            inv.Items.Add(new InvoiceItem
            {
                InvoiceId = inv.Id, LineNo = i.LineNo, SaleItemId = i.Id, ProductId = i.ProductId, Description = i.ProductName, Quantity = i.Quantity,
                Unit = i.Unit, UnitPrice = i.UnitPrice, DiscountPercent = i.DiscountPercent, DiscountAmount = i.DiscountAmount, VatRate = i.VatRate,
                NetAmount = i.NetAmount, VatAmount = i.VatAmount, LineTotal = i.LineTotal,
            });
        db.Invoices.Add(inv);
        audit.Log("Fatura taslağı oluşturuldu", nameof(Invoice), inv.Id, $"{inv.InternalRef} ← {sale.Number}: {inv.GrandTotal:N2} TL");
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException) when (clientRequestId.HasValue)
        {
            await tx.RollbackAsync(ct); db.ChangeTracker.Clear();
            var existing = await db.Invoices.Where(i => i.ClientRequestId == clientRequestId).Select(i => (Guid?)i.Id).FirstOrDefaultAsync(ct);
            if (existing.HasValue) return await GetAsync(existing.Value, ct);
            throw new ConflictException("Bu satış için zaten bir fatura oluşturulmuş.");
        }
        await tx.CommitAsync(ct);
        return await GetAsync(inv.Id, ct);
    }

    /// <summary>Taslak düzenleme: tarih, belge türü, alıcı bilgisi, not ve satır açıklamaları. Tutarlar satıştan gelir ve burada değiştirilemez.</summary>
    public async Task<InvoiceDto> UpdateDraftAsync(Guid id, UpdateInvoiceRequest r, CancellationToken ct = default)
    {
        var inv = await db.Invoices.Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Fatura bulunamadı.");
        if (!inv.IsEditable) throw new ConflictException("Yalnızca taslak durumundaki fatura düzenlenebilir.");

        var v = new ValidationBuilder();
        v.When(string.IsNullOrWhiteSpace(r.BuyerName), "buyerName", "Alıcı adı/unvanı zorunludur.");
        v.When(!string.IsNullOrWhiteSpace(r.BuyerTaxNumber) && !TurkishTaxIds.IsValidVkn(r.BuyerTaxNumber.Trim()), "buyerTaxNumber", "Vergi kimlik numarası geçerli değil.");
        v.When(!string.IsNullOrWhiteSpace(r.BuyerNationalId) && !TurkishTaxIds.IsValidTckn(r.BuyerNationalId.Trim()), "buyerNationalId", "T.C. kimlik numarası geçerli değil.");
        v.When(r.DueDate.HasValue && r.DueDate < r.InvoiceDate, "dueDate", "Vade tarihi fatura tarihinden önce olamaz.");
        v.When(r.InvoiceDate > clock.Today.AddDays(1), "invoiceDate", "Fatura tarihi ileri bir tarih olamaz.");
        v.ThrowIfAny();

        string? C(string? s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
        inv.InvoiceDate = r.InvoiceDate; inv.DueDate = r.DueDate; inv.DocumentType = r.DocumentType;
        inv.BuyerName = r.BuyerName.Trim(); inv.BuyerTaxOffice = C(r.BuyerTaxOffice); inv.BuyerTaxNumber = C(r.BuyerTaxNumber);
        inv.BuyerNationalId = C(r.BuyerNationalId); inv.BuyerAddress = C(r.BuyerAddress); inv.BuyerCity = C(r.BuyerCity);
        inv.BuyerDistrict = C(r.BuyerDistrict); inv.BuyerEmail = C(r.BuyerEmail); inv.Note = C(r.Note);
        foreach (var edit in r.Lines ?? [])
        {
            var line = inv.Items.FirstOrDefault(i => i.Id == edit.Id) ?? throw new ValidationFailedException("lines", "Fatura satırı bulunamadı.");
            if (string.IsNullOrWhiteSpace(edit.Description)) throw new ValidationFailedException("lines", "Satır açıklaması boş olamaz.");
            line.Description = edit.Description.Trim();
        }
        inv.UpdatedAtUtc = clock.UtcNow;
        audit.Log("Fatura taslağı güncellendi", nameof(Invoice), inv.Id, inv.InternalRef);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    // ───────────── Kontrol ─────────────

    public async Task<IReadOnlyList<InvoiceIssue>> CheckAsync(Guid id, CancellationToken ct = default)
    {
        var inv = await db.Invoices.AsNoTracking().Include(i => i.Customer).Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == id, ct)
            ?? throw new NotFoundException("Fatura bulunamadı.");
        return Check(inv);
    }

    private static List<InvoiceIssue> Check(Invoice inv)
    {
        var issues = new List<InvoiceIssue>();
        void Err(string f, string m) => issues.Add(new InvoiceIssue(f, m, true));
        void Warn(string f, string m) => issues.Add(new InvoiceIssue(f, m, false));

        if (string.IsNullOrWhiteSpace(inv.BuyerName)) Err("buyerName", "Alıcı adı/unvanı eksik.");
        var hasVkn = TurkishTaxIds.IsValidVkn(inv.BuyerTaxNumber);
        var hasTckn = TurkishTaxIds.IsValidTckn(inv.BuyerNationalId);
        if (!hasVkn && !hasTckn) Err("buyerTaxNumber", "Alıcının geçerli bir vergi kimlik numarası (10 hane) veya T.C. kimlik numarası (11 hane) olmalıdır.");
        if (inv.DocumentType == InvoiceDocumentType.Undetermined) Err("documentType", "Belge türü (e-Fatura / e-Arşiv) seçilmedi.");
        if (inv.Items.Count == 0) Err("items", "Faturada kalem yok.");
        if (inv.GrandTotal <= 0) Err("grandTotal", "Fatura tutarı sıfırdan büyük olmalıdır.");
        if (string.IsNullOrWhiteSpace(inv.BuyerAddress)) Warn("buyerAddress", "Alıcı adresi boş; resmî belgede adres gerekebilir.");
        if (string.IsNullOrWhiteSpace(inv.BuyerCity)) Warn("buyerCity", "Alıcı ili boş.");
        if (string.IsNullOrWhiteSpace(inv.BuyerTaxOffice) && hasVkn) Warn("buyerTaxOffice", "Vergi dairesi boş.");

        var status = inv.Customer?.EInvoiceStatus ?? EInvoiceTaxpayerStatus.Unknown;
        if (status == EInvoiceTaxpayerStatus.Unknown)
            Warn("documentType", "Müşterinin e-Fatura mükellefiyeti DOĞRULANMADI. Belge türünü seçmeden önce GİB e-Fatura portalındaki mükellef sorgusuyla kontrol edin.");
        if (status == EInvoiceTaxpayerStatus.Registered && inv.DocumentType == InvoiceDocumentType.EArchive)
            Err("documentType", "Müşteri e-Fatura mükellefi olarak işaretli; e-Arşiv seçilemez. Müşteri bilgisini düzeltin ya da e-Fatura seçin.");
        if (status == EInvoiceTaxpayerStatus.NotRegistered && inv.DocumentType == InvoiceDocumentType.EInvoice)
            Err("documentType", "Müşteri e-Fatura mükellefi değil olarak işaretli; e-Fatura seçilemez.");
        return issues;
    }

    // ───────────── Durum akışı ─────────────

    /// <summary>
    /// İÇ ONAY. Tutarlar ve alıcı bilgisi kontrol edilmiş sayılır. Bu işlem RESMÎ fatura oluşturmaz, göndermez ve cariyi etkilemez.
    /// </summary>
    public async Task<InvoiceDto> ApproveAsync(Guid id, CancellationToken ct = default)
    {
        var inv = await db.Invoices.Include(i => i.Customer).Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Fatura bulunamadı.");
        var blocking = Check(inv).Where(i => i.IsBlocking).ToList();
        if (blocking.Count > 0)
            throw new ValidationFailedException(blocking.GroupBy(b => b.Field).ToDictionary(g => g.Key, g => g.Select(x => x.Message).ToArray()));
        inv.Approve();
        inv.UpdatedAtUtc = clock.UtcNow;
        audit.Log("Fatura iç onayı", nameof(Invoice), inv.Id, $"{inv.InternalRef}: {inv.GrandTotal:N2} TL (resmî belge OLUŞTURULMADI)");
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<InvoiceDto> ReturnToDraftAsync(Guid id, CancellationToken ct = default)
    {
        var inv = await LoadAsync(id, ct);
        inv.ReturnToDraft();
        audit.Log("Fatura taslağa alındı", nameof(Invoice), inv.Id, inv.InternalRef);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Manuel akış: "Resmî belgeyi portalda düzenleyeceğim". Hiçbir resmî işlem yapılmaz; yalnızca iş akışı durumu değişir.</summary>
    public async Task<InvoiceDto> MarkAwaitingOfficialAsync(Guid id, CancellationToken ct = default)
    {
        var inv = await LoadAsync(id, ct);
        inv.MarkAwaitingOfficial();
        audit.Log("Resmî işlem bekleniyor", nameof(Invoice), inv.Id, inv.InternalRef);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Manuel akış: kullanıcı resmî belgeyi (LUCA / GİB portalı) düzenledikten sonra numarayı kaydeder.
    /// Kaynak "Kullanıcı girişi"dir ve GİB'den DOĞRULANMAMIŞTIR. Aynı resmî numara/ETTN ikinci kez kaydedilemez.
    /// </summary>
    public async Task<InvoiceDto> RecordOfficialAsync(Guid id, RecordOfficialRequest r, CancellationToken ct = default)
    {
        var v = new ValidationBuilder();
        v.When(string.IsNullOrWhiteSpace(r.Number), "number", "Resmî fatura numarası zorunludur.");
        v.When(r.Number?.Trim().Length > 30, "number", "Resmî fatura numarası en fazla 30 karakter olabilir.");
        v.When(r.IssueDate > clock.Today.AddDays(1), "issueDate", "Düzenlenme tarihi ileri bir tarih olamaz.");
        v.When(r.DocumentType == InvoiceDocumentType.Undetermined, "documentType", "Resmî belge türü seçilmelidir.");
        v.When(!string.IsNullOrWhiteSpace(r.Uuid) && !Guid.TryParse(r.Uuid.Trim(), out _), "uuid", "ETTN/UUID biçimi geçerli değil (örnek: 3fa85f64-5717-4562-b3fc-2c963f66afa6).");
        v.ThrowIfAny();

        var inv = await LoadAsync(id, ct);
        await EnsureOfficialIdentifiersFreeAsync(id, r.Number.Trim().ToUpperInvariant(), r.Uuid?.Trim().ToLowerInvariant(), ct);
        inv.RecordOfficialDocument(r.Number, r.IssueDate, r.Uuid, r.DocumentType, OfficialSource.ManualEntry, null, false, clock.UtcNow);
        inv.UpdatedAtUtc = clock.UtcNow;
        audit.Log("Resmî belge bilgisi kaydedildi (kullanıcı girişi)", nameof(Invoice), inv.Id, $"{inv.InternalRef} → {inv.OfficialNumber}");
        await SaveOfficialAsync(ct);
        return await GetAsync(id, ct);
    }

    private async Task EnsureOfficialIdentifiersFreeAsync(Guid invoiceId, string number, string? uuid, CancellationToken ct)
    {
        var clash = await db.Invoices.Where(i => i.Id != invoiceId && (i.OfficialNumber == number || (uuid != null && i.OfficialUuid == uuid)))
            .Select(i => i.InternalRef).FirstOrDefaultAsync(ct);
        if (clash is not null)
            throw new ConflictException($"Bu resmî fatura numarası/ETTN zaten {clash} referanslı faturada kayıtlı. Aynı belge ikinci kez içeri alınamaz.");
    }

    private async Task SaveOfficialAsync(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateException ex) when (ex.InnerException?.Message.Contains("IX_Invoices_Official", StringComparison.OrdinalIgnoreCase) == true)
        {
            throw new ConflictException("Bu resmî fatura numarası/ETTN başka bir faturada kayıtlı. Aynı belge ikinci kez içeri alınamaz.");
        }
    }

    public async Task<InvoiceDto> CancelDraftAsync(Guid id, string reason, CancellationToken ct = default)
    {
        var inv = await LoadAsync(id, ct);
        inv.CancelUnissued(reason);
        audit.Log("Fatura taslağı iptal edildi", nameof(Invoice), inv.Id, $"{inv.InternalRef}: {reason}");
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    // ───────────── Resmî iptal ─────────────

    public async Task<InvoiceDto> RequestCancellationAsync(Guid id, string reason, CancellationToken ct = default)
    {
        var inv = await LoadAsync(id, ct);
        inv.RequestCancellation(reason, clock.UtcNow);
        audit.Log("Resmî iptal talebi açıldı", nameof(Invoice), inv.Id, $"{inv.InternalRef} ({inv.OfficialNumber}): {reason} — resmî durum DEĞİŞMEDİ");
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    public async Task<InvoiceDto> WithdrawCancellationRequestAsync(Guid id, CancellationToken ct = default)
    {
        var inv = await LoadAsync(id, ct);
        inv.WithdrawCancellationRequest();
        audit.Log("Resmî iptal talebi geri çekildi", nameof(Invoice), inv.Id, inv.InternalRef);
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Sağlayıcıdan (portaldan) resmî İPTAL TALEBİ oluşturulmasını ister. Belge iptal EDİLMİŞ sayılmaz: resmî durum "Kayıtlı" kalır;
    /// iptalin portalda sonuçlandığı doğrulanınca ayrıca "Resmî iptal tamamlandı — kaydet" adımı yapılır. Talep gönderimi idempotenttir.
    /// </summary>
    public async Task<InvoiceDto> RequestProviderCancellationAsync(Guid id, ProviderActionRequest r, CancellationToken ct = default)
    {
        var inv = await db.Invoices.Include(i => i.Customer).Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Fatura bulunamadı.");
        if (inv.InternalStatus != InvoiceInternalStatus.CancellationRequired || inv.OfficialStatus != OfficialInvoiceStatus.Issued)
            throw new ConflictException("Önce \"Resmî iptal talebi aç\" adımıyla iptal ihtiyacı işaretlenmelidir.");
        if (inv.OfficialSource != OfficialSource.Provider)
            throw new ConflictException("Bu belge sağlayıcı üzerinden düzenlenmedi; iptal talebini resmî portalda oluşturun ve sonucu kaydedin.");
        var provider = providers.FirstOrDefault(p => p.Name.Equals(inv.OfficialProvider, StringComparison.OrdinalIgnoreCase))
            ?? throw new ConflictException("Belgeyi düzenleyen sağlayıcı artık tanımlı değil.");
        if (!provider.Capabilities.CanCancel) throw new ConflictException($"{provider.DisplayName} iptal talebi oluşturmayı desteklemiyor; portaldan yapın.");
        var (can, reason) = CanSubmit(provider);
        if (!can) throw new ConflictException(reason!);
        RequireAcknowledgement(inv, r);

        ProviderResult<ProviderCancellation> res;
        try { res = await provider.RequestCancellationAsync(RefOf(inv), inv.CancellationReason ?? "İptal", ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { res = ProviderResult<ProviderCancellation>.Temporary("Sağlayıcıya ulaşılamadı: " + ex.Message); }
        if (!res.IsSuccess)
        {
            inv.ProviderLastMessage = res.Message;
            await db.SaveChangesAsync(ct);
            throw new ConflictException(res.Outcome == ProviderOutcome.TemporaryFailure
                ? "İptal talebinin oluşup oluşmadığı doğrulanamadı; yeniden göndermeyin, portalda İptal/İtiraz Talepleri ekranını kontrol edin. " + res.Message
                : res.Message ?? "İptal talebi reddedildi.");
        }
        inv.ProviderLastMessage = "İptal TALEBİ portala iletildi: " + res.Value!.Message;
        audit.Log("Resmî iptal talebi portala iletildi", nameof(Invoice), inv.Id, $"{inv.InternalRef} ({inv.OfficialNumber}) — belge hâlâ resmî olarak geçerli, sonuç portalda doğrulanmalı");
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>
    /// Resmî iptalin gerçekleştiğini kaydeder. Sağlayıcıdan gelen belgede, sağlayıcı durumu sorgulanabiliyorsa iptal SAĞLAYICIDAN doğrulanmadan kabul edilmez.
    /// Fatura + (isteğe bağlı) satış iptali tek veritabanı işleminde yapılır.
    /// </summary>
    public async Task<InvoiceDto> ConfirmOfficialCancellationAsync(Guid id, ConfirmCancellationRequest r, CancellationToken ct = default)
    {
        await using var tx = await db.Database.BeginTransactionAsync(ct);
        var inv = await LoadAsync(id, ct);
        var confirmed = r.Confirmed;
        var providerVerified = false;

        if (inv.OfficialSource == OfficialSource.Provider)
        {
            var provider = providers.FirstOrDefault(p => p.Name.Equals(inv.OfficialProvider, StringComparison.OrdinalIgnoreCase));
            // Sağlayıcı iptali hem YAPABİLİYOR hem RAPORLAYABİLİYORSA doğrulama sağlayıcıdan zorunludur; aksi halde (örn. e-Arşiv ağ geçidi)
            // iptal portalda yapılır ve kullanıcı beyanı "GİB den doğrulanmadı" etiketiyle kaydedilir.
            if (provider is { Capabilities: { CanQueryStatus: true, CanReportCancellation: true } })
            {
                var res = await SafeStatusAsync(provider, RefOf(inv), ct);
                confirmed = res is { IsSuccess: true, Value.State: ProviderDocumentState.Cancelled };
                if (!confirmed)
                    throw new ConflictException("Sağlayıcı belgenin iptal edildiğini doğrulamadı; belge 'resmî olarak iptal edildi' durumuna alınamaz. " + (res.Message ?? ""));
                providerVerified = true;
            }
        }

        inv.ConfirmOfficialCancellation(confirmed, r.Reference, r.Date);
        inv.OfficialCancelVerified = providerVerified;
        inv.UpdatedAtUtc = clock.UtcNow;
        audit.Log("Resmî iptal kaydedildi", nameof(Invoice), inv.Id,
            $"{inv.InternalRef} ({inv.OfficialNumber}): {inv.OfficialCancelReference} — {(providerVerified ? "sağlayıcı doğrulaması" : "kullanıcı beyanı, GİB'den/sağlayıcıdan doğrulanmadı")}");

        if (r.CancelSale)
        {
            var sale = await db.Sales.Include(s => s.Items).FirstAsync(s => s.Id == inv.SaleId, ct);
            if (sale.Status != SaleStatus.Cancelled)
                await sales.CancelCoreAsync(sale, $"Resmî fatura iptali ({inv.OfficialNumber})", ct);
        }
        await db.SaveChangesAsync(ct);
        await tx.CommitAsync(ct);
        return await GetAsync(id, ct);
    }

    // ───────────── Sağlayıcı akışı ─────────────

    private static ProviderReference RefOf(Invoice i) => new(i.ProviderReference, i.OfficialUuid, i.SubmissionKey ?? Guid.Empty);

    private async Task<ProviderResult<ProviderStatus>> SafeStatusAsync(IInvoiceProvider p, ProviderReference r, CancellationToken ct)
    {
        try { return await p.GetInvoiceStatusAsync(r, ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { return ProviderResult<ProviderStatus>.Temporary("Sağlayıcıya ulaşılamadı: " + ex.Message); }
    }

    private static void RequireAcknowledgement(Invoice inv, ProviderActionRequest r)
    {
        if (!r.Confirm) throw new ValidationFailedException("confirm", "Gerçek işlem için açık onay gerekir.");
        if (r.AcknowledgedGrandTotal != inv.GrandTotal)
            throw new ValidationFailedException("acknowledgedGrandTotal", "Onaylanan tutar fatura tutarıyla eşleşmiyor; ekranı yenileyip belgeyi yeniden kontrol edin.");
        if (!string.Equals(r.AcknowledgedBuyerName?.Trim(), inv.BuyerName.Trim(), StringComparison.Ordinal))
            throw new ValidationFailedException("acknowledgedBuyerName", "Onaylanan alıcı bilgisi fatura ile eşleşmiyor.");
    }

    private static InvoiceDocumentRequest ToRequest(Invoice inv, Guid key) => new(
        inv.Id, inv.InternalRef, key, inv.DocumentType, inv.InvoiceDate,
        new InvoiceBuyer(inv.BuyerName, inv.BuyerTaxOffice, inv.BuyerTaxNumber, inv.BuyerNationalId, inv.BuyerAddress, inv.BuyerCity, inv.BuyerDistrict, inv.BuyerEmail),
        inv.Items.OrderBy(i => i.LineNo).Select(i => new InvoiceDocumentLine(i.Description, i.Quantity, i.Unit, i.UnitPrice,
            Money.Round(i.Quantity * i.UnitPrice) - i.NetAmount, i.VatRate, i.VatAmount, i.NetAmount)).ToList(),
        inv.NetTotal, inv.VatTotal, inv.GrandTotal, inv.Note);

    /// <summary>
    /// 1. adım: sağlayıcıda TASLAK oluşturur (resmî belge değil). Önce "Gönderiliyor" durumu veritabanına yazılır;
    /// ağ hatasında durum "Sonuç belirsiz" olur ve yeniden gönderim engellenir — önce durum sorgulanmalıdır. Böylece mükerrer belge oluşmaz.
    /// </summary>
    public async Task<InvoiceDto> SubmitToProviderAsync(Guid id, ProviderActionRequest r, CancellationToken ct = default)
    {
        var provider = await ActiveProviderAsync(ct);
        var (can, reason) = CanSubmit(provider);
        if (!can) throw new ConflictException(reason!);

        var inv = await db.Invoices.Include(i => i.Customer).Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Fatura bulunamadı.");
        RequireAcknowledgement(inv, r);
        var blocking = Check(inv).Where(i => i.IsBlocking).ToList();
        if (blocking.Count > 0) throw new ValidationFailedException("invoice", string.Join(" ", blocking.Select(b => b.Message)));

        var key = Guid.CreateVersion7();
        inv.BeginSubmission(provider.Name, key);       // durum kuralları burada denetlenir (zaten gönderilmişse hata)
        audit.Log("Sağlayıcıya gönderim başlatıldı", nameof(Invoice), inv.Id, $"{inv.InternalRef} → {provider.Name}");
        await db.SaveChangesAsync(ct);                  // gönderim niyeti kalıcı: çökme/ağ hatasında kayıp yok

        ProviderResult<ProviderDraft> result;
        try { result = await provider.CreateDraftAsync(ToRequest(inv, key), ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = ProviderResult<ProviderDraft>.Temporary("Sağlayıcıya ulaşılamadı veya yanıt alınamadı: " + ex.Message);
        }

        switch (result.Outcome)
        {
            case ProviderOutcome.Success:
                inv.MarkProviderDraftCreated(result.Value!.ProviderDraftId, result.Value.ProviderMessage ?? "Sağlayıcıda taslak oluşturuldu.");
                break;
            case ProviderOutcome.TemporaryFailure:
                inv.MarkResultUnknown(result.Message ?? "Sonuç alınamadı.");
                break;
            default:
                inv.MarkSubmissionFailed(result.Message ?? "Sağlayıcı isteği reddetti.");
                break;
        }
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>2a. adım: imzalama için SMS/OTP oturumu başlatır (sağlayıcı gerektiriyorsa). Henüz belge düzenlenmez.</summary>
    public async Task<ProviderChallenge> RequestIssueChallengeAsync(Guid id, ProviderActionRequest r, CancellationToken ct = default)
    {
        var provider = await ActiveProviderAsync(ct);
        var (can, reason) = CanSubmit(provider);
        if (!can) throw new ConflictException(reason!);
        if (!provider.Capabilities.RequiresSmsCode) throw new ConflictException("Bu sağlayıcı SMS doğrulaması gerektirmiyor.");
        var inv = await db.Invoices.Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Fatura bulunamadı.");
        RequireAcknowledgement(inv, r);
        if (inv.OfficialStatus != OfficialInvoiceStatus.Submitting || inv.ProviderReference is null)
            throw new ConflictException("SMS istenecek bir sağlayıcı taslağı yok. Önce \"Sağlayıcıya gönder\" adımını tamamlayın.");
        ProviderResult<ProviderChallenge> res;
        try { res = await provider.RequestIssueChallengeAsync(RefOf(inv), ct); }
        catch (Exception ex) when (ex is not OperationCanceledException) { throw new ConflictException("Sağlayıcıya ulaşılamadı: " + ex.Message); }
        if (!res.IsSuccess) throw new ConflictException(res.Message ?? "SMS kodu istenemedi.");
        audit.Log("İmza SMS kodu istendi", nameof(Invoice), inv.Id, inv.InternalRef);
        await db.SaveChangesAsync(ct);
        return res.Value!;
    }

    /// <summary>2. adım: sağlayıcıdaki taslağı RESMÎ belgeye dönüştürür. Hukuki sonuç doğurur; açık onay ve (gerekiyorsa) SMS kodu gerekir.</summary>
    public async Task<InvoiceDto> IssueViaProviderAsync(Guid id, ProviderActionRequest r, CancellationToken ct = default)
    {
        var provider = await ActiveProviderAsync(ct);
        var (can, reason) = CanSubmit(provider);
        if (!can) throw new ConflictException(reason!);
        if (!provider.Capabilities.CanIssue) throw new ConflictException($"{provider.DisplayName} resmî belge düzenlemeyi desteklemiyor.");

        var inv = await db.Invoices.Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Fatura bulunamadı.");
        RequireAcknowledgement(inv, r);
        if (inv.OfficialStatus != OfficialInvoiceStatus.Submitting || inv.ProviderReference is null)
            throw new ConflictException("Resmîleştirilecek bir sağlayıcı taslağı yok. Önce 'Sağlayıcıya gönder' adımını tamamlayın.");
        if (!string.Equals(inv.OfficialProvider, provider.Name, StringComparison.OrdinalIgnoreCase))
            throw new ConflictException("Taslak başka bir sağlayıcıda oluşturulmuş; ayarlardaki sağlayıcı değişti.");
        if (provider.Capabilities.RequiresSmsCode && string.IsNullOrWhiteSpace(r.SmsCode))
            throw new ValidationFailedException("smsCode", "Bu sağlayıcı için SMS onay kodu gereklidir.");

        ProviderResult<ProviderIssued> result;
        try { result = await provider.IssueAsync(RefOf(inv), new ProviderIssueInput(r.SmsCode), ct); }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            result = ProviderResult<ProviderIssued>.Temporary("Sağlayıcıya ulaşılamadı veya yanıt alınamadı: " + ex.Message);
        }

        if (result.IsSuccess)
        {
            var d = result.Value!;
            await EnsureOfficialIdentifiersFreeAsync(id, d.OfficialNumber.Trim().ToUpperInvariant(), d.OfficialUuid, ct);
            inv.RecordOfficialDocument(d.OfficialNumber, d.IssueDate, d.OfficialUuid, d.DocumentType, OfficialSource.Provider, provider.Name, true, clock.UtcNow);
            audit.Log("Resmî belge düzenlendi (sağlayıcı)", nameof(Invoice), inv.Id, $"{inv.InternalRef} → {inv.OfficialNumber} [{provider.Name}]");
        }
        else if (result.Outcome == ProviderOutcome.TemporaryFailure)
        {
            inv.MarkResultUnknown(result.Message ?? "Sonuç alınamadı.");
            await db.SaveChangesAsync(ct);
            throw new ConflictException("Sonuç alınamadı; belgenin düzenlenip düzenlenmediği bilinmiyor. Yeniden göndermeyin — 'Durumu sorgula' ile kontrol edin.");
        }
        else
        {
            inv.ProviderLastMessage = result.Message;
            await db.SaveChangesAsync(ct);
            throw new ConflictException(result.Message ?? "Sağlayıcı işlemi reddetti.");
        }
        await SaveOfficialAsync(ct);
        return await GetAsync(id, ct);
    }

    /// <summary>Sağlayıcıdan durum sorgular; belgenin oluştuğu doğrulanırsa resmî bilgiyi kaydeder, hiç oluşmadığı doğrulanırsa yeniden denemeye izin verir.</summary>
    public async Task<InvoiceDto> RefreshStatusAsync(Guid id, CancellationToken ct = default)
    {
        var inv = await db.Invoices.Include(i => i.Items).FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Fatura bulunamadı.");
        if (inv.OfficialStatus is not (OfficialInvoiceStatus.Submitting or OfficialInvoiceStatus.ResultUnknown))
            throw new ConflictException("Bu fatura için bekleyen bir sağlayıcı işlemi yok.");
        var provider = providers.FirstOrDefault(p => p.Name.Equals(inv.OfficialProvider, StringComparison.OrdinalIgnoreCase))
            ?? throw new ConflictException("İşlemi başlatan sağlayıcı artık tanımlı değil.");
        if (!provider.Capabilities.CanQueryStatus) throw new ConflictException($"{provider.DisplayName} durum sorgulamayı desteklemiyor; portaldan kontrol edip resmî bilgiyi elle kaydedin.");

        var res = await SafeStatusAsync(provider, RefOf(inv), ct);
        switch (res.Outcome)
        {
            case ProviderOutcome.Success when res.Value!.State == ProviderDocumentState.Issued && res.Value.OfficialNumber is not null:
                var s = res.Value;
                await EnsureOfficialIdentifiersFreeAsync(id, s.OfficialNumber!.Trim().ToUpperInvariant(), s.OfficialUuid, ct);
                inv.RecordOfficialDocument(s.OfficialNumber!, s.IssueDate ?? clock.Today, s.OfficialUuid, s.DocumentType ?? inv.DocumentType,
                    OfficialSource.Provider, provider.Name, true, clock.UtcNow);
                audit.Log("Resmî belge doğrulandı (sağlayıcı)", nameof(Invoice), inv.Id, $"{inv.InternalRef} → {inv.OfficialNumber}");
                break;
            case ProviderOutcome.Success when res.Value!.State == ProviderDocumentState.Draft:
                inv.MarkProviderDraftCreated(inv.ProviderReference ?? "", "Sağlayıcıda taslak mevcut; resmî belge henüz oluşmadı.");
                break;
            case ProviderOutcome.NotFound:
                // Sağlayıcı bu gönderim anahtarını hiç görmedi: belge oluşmadı, güvenle yeniden denenebilir.
                if (inv.OfficialStatus == OfficialInvoiceStatus.ResultUnknown) inv.OfficialStatus = OfficialInvoiceStatus.Submitting;
                inv.MarkSubmissionFailed("Sağlayıcıda bu gönderim için kayıt bulunamadı; belge oluşmamış. Yeniden gönderebilirsiniz.");
                break;
            default:
                inv.ProviderLastMessage = res.Message ?? "Durum belirlenemedi.";
                break;
        }
        await SaveOfficialAsync(ct);
        return await GetAsync(id, ct);
    }

    // ───────────── Ekler ─────────────

    public async Task<AttachmentDto> AddAttachmentAsync(Guid invoiceId, Stream content, string fileName, CancellationToken ct = default)
    {
        var inv = await db.Invoices.FirstOrDefaultAsync(i => i.Id == invoiceId, ct) ?? throw new NotFoundException("Fatura bulunamadı.");
        if (inv.InternalStatus == InvoiceInternalStatus.Cancelled && inv.OfficialStatus == OfficialInvoiceStatus.NotIssued)
            throw new ConflictException("İptal edilmiş taslağa dosya eklenemez.");
        var check = await FileValidator.ValidateAsync(content, fileName, invoiceFile: true, ct);
        var stored = await files.SaveAsync(AttachmentFolder, content, check.Extension, ct);
        if (await db.InvoiceAttachments.AnyAsync(a => a.InvoiceId == invoiceId && a.Sha256 == stored.Sha256, ct))
            throw new ConflictException("Aynı dosya bu faturaya zaten eklenmiş.");
        var att = new InvoiceAttachment
        {
            InvoiceId = invoiceId, Kind = check.Kind, OriginalFileName = Path.GetFileName(fileName), StoredFileName = stored.StoredName,
            ContentType = check.ContentType, SizeBytes = stored.SizeBytes, Sha256 = stored.Sha256,
        };
        db.InvoiceAttachments.Add(att);
        audit.Log("Fatura eki yüklendi", nameof(Invoice), invoiceId, $"{inv.InternalRef}: {att.OriginalFileName}");
        await db.SaveChangesAsync(ct);
        return new AttachmentDto(att.Id, att.Kind, att.OriginalFileName, att.SizeBytes, att.CreatedAtUtc);
    }

    public async Task<(Stream Content, string ContentType, string FileName)> OpenAttachmentAsync(Guid invoiceId, Guid attachmentId, CancellationToken ct = default)
    {
        var a = await db.InvoiceAttachments.AsNoTracking().FirstOrDefaultAsync(x => x.Id == attachmentId && x.InvoiceId == invoiceId, ct)
            ?? throw new NotFoundException("Ek bulunamadı.");
        var s = await files.OpenReadAsync(AttachmentFolder, a.StoredFileName, ct) ?? throw new NotFoundException("Dosya bulunamadı.");
        return (s, a.ContentType, a.OriginalFileName);
    }

    // ───────────── Sorgular ─────────────

    private async Task<Invoice> LoadAsync(Guid id, CancellationToken ct) =>
        await db.Invoices.FirstOrDefaultAsync(i => i.Id == id, ct) ?? throw new NotFoundException("Fatura bulunamadı.");

    public async Task<InvoiceDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var i = await db.Invoices.AsNoTracking().Include(x => x.Customer).Include(x => x.Sale).Include(x => x.Items).Include(x => x.Attachments)
            .FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Fatura bulunamadı.");
        var paid = await db.PaymentAllocations.Where(a => a.SaleId == i.SaleId && a.IsActive).SumAsync(a => (decimal?)a.Amount, ct) ?? 0;
        var counted = i.InternalStatus != InvoiceInternalStatus.Cancelled;
        var legal = i.OfficialSource == OfficialSource.Provider
            && providers.FirstOrDefault(p => p.Name.Equals(i.OfficialProvider, StringComparison.OrdinalIgnoreCase)) is { Capabilities.IsLiveCapable: true };
        var issues = i.InternalStatus is InvoiceInternalStatus.Draft or InvoiceInternalStatus.Approved or InvoiceInternalStatus.AwaitingOfficial
            ? Check(i) : [];
        return new InvoiceDto(i.Id, i.InternalRef, i.CustomerId, i.Customer!.Name, i.SaleId, i.Sale!.Number, i.InvoiceDate, i.DueDate, i.DocumentType,
            i.BuyerName, i.BuyerTaxOffice, i.BuyerTaxNumber, i.BuyerNationalId, i.BuyerAddress, i.BuyerCity, i.BuyerDistrict, i.BuyerEmail,
            i.Customer.EInvoiceStatus,
            i.GrossTotal, i.DiscountTotal, i.NetTotal, i.VatTotal, i.GrandTotal, counted ? paid : 0, counted ? i.GrandTotal - paid : 0, i.Note,
            i.InternalStatus, i.OfficialStatus, i.OfficialNumber, i.OfficialUuid, i.OfficialIssueDate, i.OfficialDocumentType,
            i.OfficialSource, i.OfficialProvider, i.OfficialVerified, legal, i.OfficialRecordedAtUtc, i.ProviderLastMessage, i.ProviderReference,
            i.CancellationReason, i.CancellationRequestedAtUtc, i.OfficialCancelDate, i.OfficialCancelReference, i.OfficialCancelVerified, i.CancelReason,
            i.Items.OrderBy(x => x.LineNo).Select(x => new InvoiceLineDto(x.Id, x.LineNo, x.Description, x.Quantity, x.Unit, x.UnitPrice,
                x.DiscountPercent, x.DiscountAmount, x.VatRate, x.NetAmount, x.VatAmount, x.LineTotal)).ToList(),
            i.Attachments.OrderBy(a => a.CreatedAtUtc).Select(a => new AttachmentDto(a.Id, a.Kind, a.OriginalFileName, a.SizeBytes, a.CreatedAtUtc)).ToList(),
            issues);
    }

    public async Task<PagedResult<InvoiceListItem>> ListAsync(Guid? customerId, InvoiceInternalStatus? internalStatus, OfficialInvoiceStatus? officialStatus,
        DateOnly? from, DateOnly? to, string? search, bool? onlyOpen, bool? awaitingOfficial, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 200);
        var q = db.Invoices.AsNoTracking().AsQueryable();
        if (customerId.HasValue) q = q.Where(i => i.CustomerId == customerId);
        if (internalStatus.HasValue) q = q.Where(i => i.InternalStatus == internalStatus);
        if (officialStatus.HasValue) q = q.Where(i => i.OfficialStatus == officialStatus);
        if (from.HasValue) q = q.Where(i => i.InvoiceDate >= from);
        if (to.HasValue) q = q.Where(i => i.InvoiceDate <= to);
        if (awaitingOfficial == true)
            q = q.Where(i => i.InternalStatus == InvoiceInternalStatus.Approved || i.InternalStatus == InvoiceInternalStatus.AwaitingOfficial
                || i.InternalStatus == InvoiceInternalStatus.Processing || i.InternalStatus == InvoiceInternalStatus.Error);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var t = search.Trim().ToLower();
            q = q.Where(i => i.InternalRef.ToLower().Contains(t) || (i.OfficialNumber != null && i.OfficialNumber.ToLower().Contains(t)) || i.Customer!.Name.ToLower().Contains(t));
        }
        if (onlyOpen == true)
            q = q.Where(i => i.InternalStatus != InvoiceInternalStatus.Draft && i.InternalStatus != InvoiceInternalStatus.Cancelled
                && i.GrandTotal > (db.PaymentAllocations.Where(a => a.SaleId == i.SaleId && a.IsActive).Sum(a => (decimal?)a.Amount) ?? 0));

        var total = await q.CountAsync(ct);
        var rows = await q.OrderByDescending(i => i.InvoiceDate).ThenByDescending(i => i.InternalRef)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(i => new
            {
                i, CustomerName = i.Customer!.Name, SaleNumber = i.Sale!.Number,
                Paid = db.PaymentAllocations.Where(a => a.SaleId == i.SaleId && a.IsActive).Sum(a => (decimal?)a.Amount) ?? 0,
                Atts = i.Attachments.OrderBy(a => a.CreatedAtUtc).Select(a => new AttachmentDto(a.Id, a.Kind, a.OriginalFileName, a.SizeBytes, a.CreatedAtUtc)).ToList(),
            }).ToListAsync(ct);
        var items = rows.Select(x =>
        {
            var counted = x.i.InternalStatus != InvoiceInternalStatus.Cancelled;
            return new InvoiceListItem(x.i.Id, x.i.InternalRef, x.i.OfficialNumber, x.i.CustomerId, x.CustomerName, x.i.InvoiceDate, x.i.DueDate, x.i.DocumentType,
                x.i.NetTotal, x.i.VatTotal, x.i.GrandTotal, counted ? x.Paid : 0, counted ? x.i.GrandTotal - x.Paid : 0,
                x.i.InternalStatus, x.i.OfficialStatus, x.i.OfficialSource, x.i.OfficialVerified, x.i.OfficialProvider, x.i.SaleId, x.SaleNumber, x.Atts);
        }).ToList();
        return new PagedResult<InvoiceListItem>(items, total, page, pageSize);
    }
}
