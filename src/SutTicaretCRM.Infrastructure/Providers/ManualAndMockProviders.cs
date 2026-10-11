using System.Collections.Concurrent;
using SutTicaretCRM.Application;
using SutTicaretCRM.Domain;

namespace SutTicaretCRM.Infrastructure.Providers;

/// <summary>
/// Manuel portal akışı: CRM hiçbir resmî işlem yapmaz. Kullanıcı belgeyi LUCA / GİB portalında düzenler,
/// numarayı CRM'e kaydeder. Bu sağlayıcı hiçbir işlemi "başarılı" taklit etmez.
/// </summary>
public sealed class ManualPortalInvoiceProvider : IInvoiceProvider
{
    public string Name => "Manual";
    public string DisplayName => "Manuel portal (LUCA / GİB portalı)";

    public InvoiceProviderCapabilities Capabilities { get; } = new(
        CanValidate: false, CanCreateDraft: false, CanIssue: false, CanQueryStatus: false, CanDownloadDocument: false, CanCancel: false,
        SupportsEArchive: true, SupportsEInvoice: true, IsLiveCapable: false, RequiresSmsCode: false, CanReportCancellation: false,
        Description: "Belge resmî portalda kullanıcı tarafından düzenlenir; CRM yalnızca taslağı hazırlar ve sonucu kaydeder.");

    private const string Msg = "Manuel portal akışı bu işlemi desteklemez; işlem resmî portalda yapılmalıdır.";
    public Task<ProviderResult<ProviderValidation>> ValidateInvoiceAsync(InvoiceDocumentRequest r, CancellationToken ct = default) => Task.FromResult(ProviderResult<ProviderValidation>.NotSupported(Msg));
    public Task<ProviderResult<ProviderDraft>> CreateDraftAsync(InvoiceDocumentRequest r, CancellationToken ct = default) => Task.FromResult(ProviderResult<ProviderDraft>.NotSupported(Msg));
    public Task<ProviderResult<ProviderChallenge>> RequestIssueChallengeAsync(ProviderReference r, CancellationToken ct = default) => Task.FromResult(ProviderResult<ProviderChallenge>.NotSupported(Msg));
    public Task<ProviderResult<ProviderIssued>> IssueAsync(ProviderReference r, ProviderIssueInput i, CancellationToken ct = default) => Task.FromResult(ProviderResult<ProviderIssued>.NotSupported(Msg));
    public Task<ProviderResult<ProviderStatus>> GetInvoiceStatusAsync(ProviderReference r, CancellationToken ct = default) => Task.FromResult(ProviderResult<ProviderStatus>.NotSupported(Msg));
    public Task<ProviderResult<ProviderDocument>> GetInvoiceDocumentAsync(ProviderReference r, ProviderDocumentFormat f, CancellationToken ct = default) => Task.FromResult(ProviderResult<ProviderDocument>.NotSupported(Msg));
    public Task<ProviderResult<ProviderCancellation>> RequestCancellationAsync(ProviderReference r, string reason, CancellationToken ct = default) => Task.FromResult(ProviderResult<ProviderCancellation>.NotSupported(Msg));
}

/// <summary>
/// SAHTE sağlayıcı: yalnızca geliştirme ve otomatik testler içindir. GİB ile hiçbir bağlantısı yoktur ve ürettiği
/// numaralar yasal geçerliliği olmayan test numaralarıdır (IsLiveCapable = false). Üretimde etkinleştirilmemelidir.
/// </summary>
public sealed class MockInvoiceProvider : IInvoiceProvider
{
    public string Name => "Mock";
    public string DisplayName => "TEST sağlayıcısı (sahte — resmî değil)";

    public InvoiceProviderCapabilities Capabilities { get; } = new(
        CanValidate: true, CanCreateDraft: true, CanIssue: true, CanQueryStatus: true, CanDownloadDocument: false, CanCancel: true,
        SupportsEArchive: true, SupportsEInvoice: true, IsLiveCapable: false, RequiresSmsCode: false, CanReportCancellation: true,
        Description: "Test amaçlı sahte sağlayıcı. Üretilen belgeler resmî değildir.");

    private sealed class Doc
    {
        public required Guid Key;
        public string DraftId = "";
        public string? Number;
        public string? Uuid;
        public DateOnly? IssueDate;
        public ProviderDocumentState State = ProviderDocumentState.Draft;
        public InvoiceDocumentType Type;
    }

    private readonly ConcurrentDictionary<Guid, Doc> _docs = new();
    private int _counter;

    // ── Test için hata enjeksiyonu ──
    /// <summary>true: belge sağlayıcıda OLUŞUR ama istemciye zaman aşımı gibi hata döner (ağ hatası senaryosu).</summary>
    public bool SimulateTimeoutAfterCreateDraft { get; set; }
    public bool SimulateTimeoutAfterIssue { get; set; }
    public bool RejectNextDraft { get; set; }
    public int CreatedDocumentCount => _docs.Count;
    public int IssuedDocumentCount => _docs.Values.Count(d => d.State == ProviderDocumentState.Issued);

    public void Reset()
    {
        _docs.Clear();
        _counter = 0;
        SimulateTimeoutAfterCreateDraft = SimulateTimeoutAfterIssue = RejectNextDraft = false;
    }

    public Task<ProviderResult<ProviderValidation>> ValidateInvoiceAsync(InvoiceDocumentRequest r, CancellationToken ct = default)
    {
        var problems = new List<string>();
        if (string.IsNullOrWhiteSpace(r.Buyer.TaxNumber) && string.IsNullOrWhiteSpace(r.Buyer.NationalId)) problems.Add("Alıcı kimlik/vergi numarası yok.");
        return Task.FromResult(ProviderResult<ProviderValidation>.Ok(new ProviderValidation(problems.Count == 0, problems)));
    }

    public Task<ProviderResult<ProviderDraft>> CreateDraftAsync(InvoiceDocumentRequest r, CancellationToken ct = default)
    {
        if (RejectNextDraft)
        {
            RejectNextDraft = false;
            return Task.FromResult(ProviderResult<ProviderDraft>.Rejected("Test sağlayıcısı isteği reddetti."));
        }
        // Aynı SubmissionKey ile tekrar gelen istek yeni belge üretmez (idempotent).
        var doc = _docs.GetOrAdd(r.SubmissionKey, k => new Doc { Key = k, DraftId = $"MOCK-DRAFT-{Interlocked.Increment(ref _counter):0000}", Type = r.DocumentType });
        if (SimulateTimeoutAfterCreateDraft) throw new TimeoutException("Test: istek zaman aşımına uğradı (belge sağlayıcıda oluştu).");
        return Task.FromResult(ProviderResult<ProviderDraft>.Ok(new ProviderDraft(doc.DraftId, null, "Test taslağı oluşturuldu.")));
    }

    public Task<ProviderResult<ProviderChallenge>> RequestIssueChallengeAsync(ProviderReference r, CancellationToken ct = default) =>
        Task.FromResult(ProviderResult<ProviderChallenge>.NotSupported("Test sağlayıcısı SMS doğrulaması gerektirmez."));

    public Task<ProviderResult<ProviderIssued>> IssueAsync(ProviderReference r, ProviderIssueInput input, CancellationToken ct = default)
    {
        var doc = Find(r);
        if (doc is null) return Task.FromResult(ProviderResult<ProviderIssued>.Missing("Test sağlayıcısında taslak yok."));
        if (doc.State != ProviderDocumentState.Issued)
        {
            doc.State = ProviderDocumentState.Issued;
            doc.Number = $"TST{DateTime.UtcNow.Year}{Interlocked.Increment(ref _counter):000000000}";
            doc.Uuid = Guid.NewGuid().ToString();
            doc.IssueDate = DateOnly.FromDateTime(DateTime.UtcNow);
        }
        if (SimulateTimeoutAfterIssue) throw new TimeoutException("Test: yanıt alınamadı (belge sağlayıcıda düzenlendi).");
        return Task.FromResult(ProviderResult<ProviderIssued>.Ok(new ProviderIssued(doc.Number!, doc.Uuid, doc.IssueDate!.Value, doc.Type)));
    }

    public Task<ProviderResult<ProviderStatus>> GetInvoiceStatusAsync(ProviderReference r, CancellationToken ct = default)
    {
        var doc = Find(r);
        if (doc is null) return Task.FromResult(ProviderResult<ProviderStatus>.Missing("Kayıt yok."));
        return Task.FromResult(ProviderResult<ProviderStatus>.Ok(new ProviderStatus(doc.State, doc.Number, doc.Uuid, doc.IssueDate, doc.Type, null)));
    }

    public Task<ProviderResult<ProviderDocument>> GetInvoiceDocumentAsync(ProviderReference r, ProviderDocumentFormat f, CancellationToken ct = default) =>
        Task.FromResult(ProviderResult<ProviderDocument>.NotSupported("Test sağlayıcısı belge dosyası üretmez."));

    public Task<ProviderResult<ProviderCancellation>> RequestCancellationAsync(ProviderReference r, string reason, CancellationToken ct = default)
    {
        var doc = Find(r);
        if (doc is null) return Task.FromResult(ProviderResult<ProviderCancellation>.Missing("Kayıt yok."));
        doc.State = ProviderDocumentState.Cancelled;
        return Task.FromResult(ProviderResult<ProviderCancellation>.Ok(new ProviderCancellation(true, "Test belgesi iptal edildi.")));
    }

    private Doc? Find(ProviderReference r) =>
        _docs.TryGetValue(r.SubmissionKey, out var d) ? d : _docs.Values.FirstOrDefault(x => x.DraftId == r.ProviderDraftId || (r.OfficialUuid != null && x.Uuid == r.OfficialUuid));
}
