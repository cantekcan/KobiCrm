using SutTicaretCRM.Domain;

namespace SutTicaretCRM.Application;

/// <summary>
/// Fatura sağlayıcı sözleşmesi. Her uygulama yalnızca GERÇEKTEN desteklediği işlemleri yapar;
/// desteklemediği işlem için <see cref="ProviderOutcome.NotSupported"/> döner — asla sabit "başarılı" yanıt değil.
/// Bu sözleşme GİB'in resmî bir API'si değildir; sağlayıcılar portal otomasyonu ya da özel entegratör olabilir.
/// </summary>
public interface IInvoiceProvider
{
    /// <summary>Ayarlarda saklanan sağlayıcı adı (ör. Manual, Mock, EArsivNode).</summary>
    string Name { get; }
    string DisplayName { get; }
    InvoiceProviderCapabilities Capabilities { get; }

    Task<ProviderResult<ProviderValidation>> ValidateInvoiceAsync(InvoiceDocumentRequest request, CancellationToken ct = default);
    /// <summary>Sağlayıcıda TASLAK oluşturur. Taslak resmî belge değildir.</summary>
    Task<ProviderResult<ProviderDraft>> CreateDraftAsync(InvoiceDocumentRequest request, CancellationToken ct = default);
    /// <summary>İmzalama için SMS/OTP doğrulama oturumu başlatır (sağlayıcı gerektiriyorsa). Henüz belge düzenlenmez.</summary>
    Task<ProviderResult<ProviderChallenge>> RequestIssueChallengeAsync(ProviderReference reference, CancellationToken ct = default);
    /// <summary>Taslağı resmî belgeye dönüştürür (imzalar). Hukuki sonuç doğurur; yalnızca kullanıcının açık onayıyla çağrılır.</summary>
    Task<ProviderResult<ProviderIssued>> IssueAsync(ProviderReference reference, ProviderIssueInput input, CancellationToken ct = default);
    Task<ProviderResult<ProviderStatus>> GetInvoiceStatusAsync(ProviderReference reference, CancellationToken ct = default);
    Task<ProviderResult<ProviderDocument>> GetInvoiceDocumentAsync(ProviderReference reference, ProviderDocumentFormat format, CancellationToken ct = default);
    Task<ProviderResult<ProviderCancellation>> RequestCancellationAsync(ProviderReference reference, string reason, CancellationToken ct = default);
}

public sealed record InvoiceProviderCapabilities(
    bool CanValidate,
    bool CanCreateDraft,
    bool CanIssue,
    bool CanQueryStatus,
    bool CanDownloadDocument,
    bool CanCancel,
    bool SupportsEArchive,
    bool SupportsEInvoice,
    /// <summary>true ise sağlayıcı gerçek (canlı) belge üretebilir; yalnızca açık konfigürasyonla etkinleşir.</summary>
    bool IsLiveCapable,
    bool RequiresSmsCode,
    /// <summary>true: sağlayıcı bir belgenin İPTAL EDİLDİĞİNİ de raporlayabilir (durum sorgusuyla doğrulanabilir). false ise iptal sonucu kullanıcı beyanıyla kaydedilir.</summary>
    bool CanReportCancellation,
    string Description);

public enum ProviderOutcome { Success, NotSupported, Rejected, TemporaryFailure, NotFound }

public sealed record ProviderResult<T>(ProviderOutcome Outcome, T? Value, string? Message)
{
    public bool IsSuccess => Outcome == ProviderOutcome.Success;
    public static ProviderResult<T> Ok(T value, string? message = null) => new(ProviderOutcome.Success, value, message);
    public static ProviderResult<T> NotSupported(string message) => new(ProviderOutcome.NotSupported, default, message);
    public static ProviderResult<T> Rejected(string message) => new(ProviderOutcome.Rejected, default, message);
    public static ProviderResult<T> Temporary(string message) => new(ProviderOutcome.TemporaryFailure, default, message);
    public static ProviderResult<T> Missing(string message) => new(ProviderOutcome.NotFound, default, message);
}

public sealed record ProviderReference(string? ProviderDraftId, string? OfficialUuid, Guid SubmissionKey);

public sealed record InvoiceDocumentRequest(
    Guid InvoiceId,
    string InternalRef,
    /// <summary>Tekrar denemelerde sağlayıcıda mükerrer belge oluşmaması için sabit anahtar.</summary>
    Guid SubmissionKey,
    InvoiceDocumentType DocumentType,
    DateOnly IssueDate,
    InvoiceBuyer Buyer,
    IReadOnlyList<InvoiceDocumentLine> Lines,
    decimal NetTotal,
    decimal VatTotal,
    decimal GrandTotal,
    string? Note);

public sealed record InvoiceBuyer(string Name, string? TaxOffice, string? TaxNumber, string? NationalId, string? Address, string? City, string? District, string? Email);

public sealed record InvoiceDocumentLine(string Description, decimal Quantity, string Unit, decimal UnitPrice, decimal DiscountAmount, decimal VatRate, decimal VatAmount, decimal NetAmount);

public sealed record ProviderValidation(bool IsValid, IReadOnlyList<string> Problems);

public sealed record ProviderDraft(string ProviderDraftId, string? Uuid, string? ProviderMessage);

public sealed record ProviderIssueInput(string? SmsCode);

public sealed record ProviderChallenge(string? MaskedPhone, string? Message);

public sealed record ProviderIssued(string OfficialNumber, string? OfficialUuid, DateOnly IssueDate, InvoiceDocumentType DocumentType);

public enum ProviderDocumentState { Unknown, Draft, Issued, Cancelled, Rejected }

public sealed record ProviderStatus(ProviderDocumentState State, string? OfficialNumber, string? OfficialUuid, DateOnly? IssueDate, InvoiceDocumentType? DocumentType, string? Message);

public enum ProviderDocumentFormat { Html, Pdf, Xml }

public sealed record ProviderDocument(byte[] Content, string ContentType, string FileName);

public sealed record ProviderCancellation(bool Completed, string? Message);

/// <summary>Ayarlardan bağımsız, dağıtım konfigürasyonu: canlı gönderim varsayılan olarak KAPALIDIR.</summary>
public sealed class InvoiceOptions
{
    public bool LiveSendingEnabled { get; set; }
    /// <summary>Test amaçlı sahte sağlayıcıyı seçilebilir yapar. Üretimde kapalı olmalıdır.</summary>
    public bool EnableMockProvider { get; set; }
}
