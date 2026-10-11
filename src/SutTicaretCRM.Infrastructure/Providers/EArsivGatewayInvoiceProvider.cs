using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Options;
using SutTicaretCRM.Application;
using SutTicaretCRM.Domain;

namespace SutTicaretCRM.Infrastructure.Providers;

public sealed class InvoiceGatewayOptions
{
    /// <summary>Ağ geçidi adresi (örn. http://127.0.0.1:5090). Boşsa sağlayıcı hiç kaydedilmez.</summary>
    public string Url { get; set; } = "";
    /// <summary>CRM ile ağ geçidi arasındaki paylaşılan gizli anahtar (ortam değişkeni).</summary>
    public string Token { get; set; } = "";
    /// <summary>Ağ geçidinin bağlandığı GİB ortamı: TEST veya PROD. PROD, IsLiveCapable = true demektir.</summary>
    public string Environment { get; set; } = "TEST";
    public int TimeoutSeconds { get; set; } = 60;
}

/// <summary>
/// GİB e-Arşiv Portalı için, Node.js ağ geçidi (f/fatura) üzerinden çalışan sağlayıcı.
/// RESMÎ bir GİB API'si DEĞİLDİR: portalın web arayüzü komutlarını otomatikleştirir; portal değişirse bozulabilir.
/// Yalnızca e-Arşiv desteklenir (e-Fatura DEĞİL), imzalama SMS kodu ister, imzalı belge iptali desteklenmez.
/// </summary>
public sealed class EArsivGatewayInvoiceProvider(HttpClient http, IOptions<InvoiceGatewayOptions> options) : IInvoiceProvider
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private InvoiceGatewayOptions O => options.Value;
    private bool IsProd => O.Environment.Equals("PROD", StringComparison.OrdinalIgnoreCase);

    public string Name => "EArsivGateway";
    public string DisplayName => IsProd ? "e-Arşiv Portalı — CANLI (f/fatura ağ geçidi)" : "e-Arşiv Portalı — TEST ortamı (f/fatura ağ geçidi)";

    public InvoiceProviderCapabilities Capabilities => new(
        CanValidate: false, CanCreateDraft: true, CanIssue: true, CanQueryStatus: true, CanDownloadDocument: true, CanCancel: true,
        SupportsEArchive: true, SupportsEInvoice: false, IsLiveCapable: IsProd, RequiresSmsCode: true, CanReportCancellation: false,
        Description: "GİB e-Arşiv Portalı otomasyonu (resmî API değil). Yalnızca e-Arşiv; imzalama portalın SMS koduyla; imzalı belge için yalnızca İPTAL TALEBİ oluşturulur — iptalin sonuçlandığı bu entegrasyonla doğrulanamaz, portaldan teyit edilir.");

    private const string NoValidate = "Ayrı bir doğrulama ucu yok; doğrulama taslak oluşturulurken ağ geçidinde yapılır.";
    public Task<ProviderResult<ProviderValidation>> ValidateInvoiceAsync(InvoiceDocumentRequest r, CancellationToken ct = default) => Task.FromResult(ProviderResult<ProviderValidation>.NotSupported(NoValidate));

    public Task<ProviderResult<ProviderDraft>> CreateDraftAsync(InvoiceDocumentRequest request, CancellationToken ct = default) =>
        Call<ProviderDraft, GwDraft>(HttpMethod.Post, "/v1/drafts", request, g => new ProviderDraft(g.DraftId ?? "", g.Uuid, g.Message), ct);

    public Task<ProviderResult<ProviderChallenge>> RequestIssueChallengeAsync(ProviderReference reference, CancellationToken ct = default) =>
        Call<ProviderChallenge, GwChallenge>(HttpMethod.Post, "/v1/issue/challenge", new { submissionKey = reference.SubmissionKey }, g => new ProviderChallenge(g.MaskedPhone, g.Message), ct);

    public Task<ProviderResult<ProviderIssued>> IssueAsync(ProviderReference reference, ProviderIssueInput input, CancellationToken ct = default) =>
        Call<ProviderIssued, GwIssued>(HttpMethod.Post, "/v1/issue", new { submissionKey = reference.SubmissionKey, smsCode = input.SmsCode }, g =>
            new ProviderIssued(g.OfficialNumber ?? "", g.Uuid, DateOnly.TryParse(g.IssueDate, out var d) ? d : DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)), InvoiceDocumentType.EArchive), ct);

    public Task<ProviderResult<ProviderStatus>> GetInvoiceStatusAsync(ProviderReference reference, CancellationToken ct = default) =>
        Call<ProviderStatus, GwStatus>(HttpMethod.Get, $"/v1/status/{reference.SubmissionKey}", null, g => new ProviderStatus(
            g.State == "Issued" ? ProviderDocumentState.Issued : g.State == "Draft" ? ProviderDocumentState.Draft : ProviderDocumentState.Unknown,
            g.OfficialNumber, g.Uuid, DateOnly.TryParse(g.IssueDate, out var d) ? d : null, InvoiceDocumentType.EArchive, g.Message), ct);

    public async Task<ProviderResult<ProviderDocument>> GetInvoiceDocumentAsync(ProviderReference reference, ProviderDocumentFormat format, CancellationToken ct = default)
    {
        if (format != ProviderDocumentFormat.Html) return ProviderResult<ProviderDocument>.NotSupported("Ağ geçidi yalnızca HTML belge görünümü sunar.");
        return await Call<ProviderDocument, GwDoc>(HttpMethod.Get, $"/v1/document/{reference.SubmissionKey}", null,
            g => new ProviderDocument(Convert.FromBase64String(g.ContentBase64 ?? ""), g.ContentType ?? "text/html", g.FileName ?? "fatura.html"), ct);
    }

    /// <summary>Portalda İPTAL TALEBİ oluşturur. Belgeyi iptal ETMEZ ve iptal edildiğini iddia etmez (Completed her zaman false).</summary>
    public Task<ProviderResult<ProviderCancellation>> RequestCancellationAsync(ProviderReference reference, string reason, CancellationToken ct = default) =>
        Call<ProviderCancellation, GwSimple>(HttpMethod.Post, "/v1/cancel", new { submissionKey = reference.SubmissionKey, reason },
            g => new ProviderCancellation(false, g.Message), ct);

    // ───────────── HTTP ─────────────

    private async Task<ProviderResult<T>> Call<T, TGw>(HttpMethod method, string path, object? body, Func<TGw, T> map, CancellationToken ct) where TGw : GwBase
    {
        try
        {
            using var req = new HttpRequestMessage(method, new Uri(new Uri(O.Url.TrimEnd('/') + "/"), path.TrimStart('/')));
            req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", O.Token);
            if (body is not null) req.Content = JsonContent.Create(body, options: Json);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(O.TimeoutSeconds));
            using var res = await http.SendAsync(req, cts.Token);

            string text = await res.Content.ReadAsStringAsync(cts.Token);
            string? message = null;
            try { using var doc = JsonDocument.Parse(text); message = doc.RootElement.TryGetProperty("message", out var m) ? m.GetString() : null; } catch (JsonException) { }

            switch (res.StatusCode)
            {
                case HttpStatusCode.OK:
                    var parsed = JsonSerializer.Deserialize<TGw>(text, Json);
                    if (parsed is null || parsed.Outcome != "success") return ProviderResult<T>.Temporary("Ağ geçidinden anlaşılamayan yanıt; sonuç doğrulanamadı.");
                    return ProviderResult<T>.Ok(map(parsed), parsed.Message);
                case HttpStatusCode.NotFound: return ProviderResult<T>.Missing(message ?? "Kayıt bulunamadı.");
                case HttpStatusCode.Unauthorized: return ProviderResult<T>.Rejected("Ağ geçidi yetkilendirme hatası (token uyuşmuyor).");
                case HttpStatusCode.UnprocessableEntity: return ProviderResult<T>.Rejected(message ?? "İstek reddedildi.");
                default: // 409 / 502 / 5xx: sonuç belirsiz → asla başarısızlık varsayılmaz
                    return ProviderResult<T>.Temporary(message ?? $"Ağ geçidi geçici hata ({(int)res.StatusCode}); sonuç belirsiz.");
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or OperationCanceledException && !ct.IsCancellationRequested)
        {
            return ProviderResult<T>.Temporary("Ağ geçidine ulaşılamadı veya yanıt zamanında gelmedi: sonuç belirsiz.");
        }
        catch (Exception ex) when (ex is FormatException or JsonException)
        {
            return ProviderResult<T>.Temporary("Ağ geçidi yanıtı çözümlenemedi; sonuç doğrulanamadı.");
        }
    }

    private abstract record GwBase { public string? Outcome { get; init; } public string? Message { get; init; } }
    private sealed record GwDraft : GwBase { public string? DraftId { get; init; } public string? Uuid { get; init; } }
    private sealed record GwSimple : GwBase;
    private sealed record GwChallenge : GwBase { public string? MaskedPhone { get; init; } }
    private sealed record GwIssued : GwBase { public string? OfficialNumber { get; init; } public string? Uuid { get; init; } public string? IssueDate { get; init; } }
    private sealed record GwStatus : GwBase { public string? State { get; init; } public string? OfficialNumber { get; init; } public string? Uuid { get; init; } public string? IssueDate { get; init; } }
    private sealed record GwDoc : GwBase { public string? ContentType { get; init; } public string? FileName { get; init; } public string? ContentBase64 { get; init; } }
}
