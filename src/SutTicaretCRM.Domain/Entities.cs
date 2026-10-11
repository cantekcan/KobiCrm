namespace SutTicaretCRM.Domain;

public abstract class Entity
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAtUtc { get; set; }
}

// ───────────── Kimlik / ayarlar / denetim ─────────────

public class User : Entity
{
    public string UserName { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string PasswordHash { get; set; } = "";
    public bool IsActive { get; set; } = true;
    public int FailedLoginCount { get; set; }
    public DateTime? LockoutEndUtc { get; set; }
}

public class AppSetting
{
    public string Key { get; set; } = "";
    public string Value { get; set; } = "";
}

public class NumberSequence
{
    public string Key { get; set; } = "";
    public long LastValue { get; set; }
}

public class AuditLog
{
    public long Id { get; set; }
    public DateTime TimestampUtc { get; set; } = DateTime.UtcNow;
    public Guid? UserId { get; set; }
    public string UserName { get; set; } = "";
    public string Action { get; set; } = "";
    public string EntityType { get; set; } = "";
    public Guid? EntityId { get; set; }
    /// <summary>Kısa açıklama. Vergi no/telefon gibi kişisel veriler buraya yazılmaz.</summary>
    public string Details { get; set; } = "";
}

// ───────────── Müşteri / tedarikçi / ürün ─────────────

public class Customer : Entity
{
    public string Name { get; set; } = "";
    public CustomerType CustomerType { get; set; }
    public string? TaxOffice { get; set; }
    /// <summary>Vergi kimlik no (10 hane).</summary>
    public string? TaxNumber { get; set; }
    /// <summary>T.C. kimlik no (11 hane); şahıs müşteriler için.</summary>
    public string? NationalId { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? City { get; set; }
    public string? District { get; set; }
    public int PaymentTermDays { get; set; }
    public EInvoiceTaxpayerStatus EInvoiceStatus { get; set; } = EInvoiceTaxpayerStatus.Unknown;
    public string? Note { get; set; }
    public bool IsActive { get; set; } = true;
}

public class Supplier : Entity
{
    public string Name { get; set; } = "";
    public string? TaxOffice { get; set; }
    public string? TaxNumber { get; set; }
    public string? Phone { get; set; }
    public string? Email { get; set; }
    public string? Address { get; set; }
    public string? Note { get; set; }
    public bool IsActive { get; set; } = true;
}

public class ProductCategory : Entity
{
    public string Name { get; set; } = "";
}

public class Product : Entity
{
    public string Code { get; set; } = "";
    public string Name { get; set; } = "";
    public Guid? CategoryId { get; set; }
    public ProductCategory? Category { get; set; }
    /// <summary>Birim kodu (Adet, Koli, Kg, Gram, Litre ...). Birimler arası otomatik dönüşüm yoktur.</summary>
    public string Unit { get; set; } = "Adet";
    public decimal PurchasePrice { get; set; }
    /// <summary>KDV hariç varsayılan satış fiyatı.</summary>
    public decimal SalePrice { get; set; }
    public decimal VatRate { get; set; }
    public bool TrackStock { get; set; } = true;
    public decimal MinStock { get; set; }
    public string? Description { get; set; }
    public bool IsActive { get; set; } = true;
}

public class CustomerProductPrice : Entity
{
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    /// <summary>KDV hariç birim fiyat.</summary>
    public decimal Price { get; set; }
}

// ───────────── Satış ─────────────

public class Sale : Entity
{
    public string Number { get; set; } = "";
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public DateOnly SaleDate { get; set; }
    public PaymentType PaymentType { get; set; }
    public DateOnly DueDate { get; set; }
    public SaleStatus Status { get; set; } = SaleStatus.Draft;
    public decimal GrossTotal { get; set; }
    public decimal DiscountTotal { get; set; }
    /// <summary>İndirim sonrası, KDV hariç tutar.</summary>
    public decimal NetTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public string? Note { get; set; }
    public Guid? ClientRequestId { get; set; }
    public DateTime? ApprovedAtUtc { get; set; }
    public DateTime? CancelledAtUtc { get; set; }
    public string? CancelReason { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public List<SaleItem> Items { get; set; } = [];

    public bool IsCommercialSale => Status is SaleStatus.Approved or SaleStatus.PartiallyPaid or SaleStatus.Paid;

    public void Approve(DateTime nowUtc)
    {
        if (Status != SaleStatus.Draft) throw new DomainException("Yalnızca taslak satış onaylanabilir.");
        if (Items.Count == 0) throw new DomainException("Kalemi olmayan satış onaylanamaz.");
        Status = SaleStatus.Approved;
        ApprovedAtUtc = nowUtc;
    }

    public void Cancel(string reason, DateTime nowUtc)
    {
        if (Status == SaleStatus.Cancelled) throw new DomainException("Satış zaten iptal edilmiş.");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("İptal nedeni yazılmalıdır.");
        Status = SaleStatus.Cancelled;
        CancelledAtUtc = nowUtc;
        CancelReason = reason.Trim();
    }

    /// <summary>Aktif tahsilat dağıtımlarına göre Onaylandı / Kısmen / Tahsil edildi durumunu günceller.</summary>
    public void RefreshPaymentStatus(decimal allocatedTotal)
    {
        if (Status is SaleStatus.Draft or SaleStatus.Cancelled) return;
        Status = allocatedTotal <= 0 ? SaleStatus.Approved
            : allocatedTotal >= GrandTotal ? SaleStatus.Paid
            : SaleStatus.PartiallyPaid;
    }
}

public class SaleItem : Entity
{
    public Guid SaleId { get; set; }
    public Sale? Sale { get; set; }
    public int LineNo { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    /// <summary>Satış anındaki ürün kodu/adı; ürün sonradan değişse de bu kayıt değişmez.</summary>
    public string ProductCode { get; set; } = "";
    public string ProductName { get; set; } = "";
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal VatRate { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal LineTotal { get; set; }
}

// ───────────── Fatura ─────────────

public class Invoice : Entity
{
    /// <summary>İç referans (ör. TASLAK-2026-000001). Resmî numara değildir.</summary>
    public string InternalRef { get; set; } = "";
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public Guid SaleId { get; set; }
    public Sale? Sale { get; set; }
    public DateOnly InvoiceDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public InvoiceDocumentType DocumentType { get; set; } = InvoiceDocumentType.Undetermined;

    // Alıcı bilgisi faturada donmuş kopya olarak tutulur.
    public string BuyerName { get; set; } = "";
    public string? BuyerTaxOffice { get; set; }
    public string? BuyerTaxNumber { get; set; }
    public string? BuyerNationalId { get; set; }
    public string? BuyerAddress { get; set; }
    public string? BuyerCity { get; set; }
    public string? BuyerDistrict { get; set; }
    public string? BuyerEmail { get; set; }

    public decimal GrossTotal { get; set; }
    public decimal DiscountTotal { get; set; }
    public decimal NetTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public string? Note { get; set; }

    public InvoiceInternalStatus InternalStatus { get; set; } = InvoiceInternalStatus.Draft;
    public OfficialInvoiceStatus OfficialStatus { get; set; } = OfficialInvoiceStatus.NotIssued;

    // Resmî belge bilgisi — yalnızca gerçekten elde edilen bilgiler.
    public string? OfficialNumber { get; set; }
    public string? OfficialUuid { get; set; }
    public DateOnly? OfficialIssueDate { get; set; }
    public InvoiceDocumentType? OfficialDocumentType { get; set; }
    public OfficialSource OfficialSource { get; set; } = OfficialSource.None;
    public string? OfficialProvider { get; set; }
    /// <summary>true: bilgi sağlayıcı yanıtından alındı. false: kullanıcı beyanı, GİB'den doğrulanmadı.</summary>
    public bool OfficialVerified { get; set; }
    public DateTime? OfficialRecordedAtUtc { get; set; }
    public string? ProviderLastMessage { get; set; }
    /// <summary>Sağlayıcıya gönderim denemesi için tek seferlik anahtar; ağ hatasında mükerrer belgeyi engeller.</summary>
    public Guid? SubmissionKey { get; set; }
    /// <summary>Sağlayıcının taslak kimliği (resmî ETTN olmayabilir).</summary>
    public string? ProviderReference { get; set; }

    public string? CancellationReason { get; set; }
    public DateTime? CancellationRequestedAtUtc { get; set; }
    public DateOnly? OfficialCancelDate { get; set; }
    public string? OfficialCancelReference { get; set; }
    /// <summary>true: iptal sağlayıcı tarafından doğrulandı. false: kullanıcı beyanı (GİB'den/sağlayıcıdan doğrulanmadı).</summary>
    public bool OfficialCancelVerified { get; set; }
    public string? CancelReason { get; set; }

    public Guid? ClientRequestId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public List<InvoiceItem> Items { get; set; } = [];
    public List<InvoiceAttachment> Attachments { get; set; } = [];

    public bool IsEditable => InternalStatus == InvoiceInternalStatus.Draft;

    // ── Durum geçişleri ──

    public void Approve()
    {
        Require(InternalStatus == InvoiceInternalStatus.Draft, "Yalnızca taslak fatura onaya alınabilir.");
        InternalStatus = InvoiceInternalStatus.Approved;
    }

    public void ReturnToDraft()
    {
        Require(InternalStatus is InvoiceInternalStatus.Approved or InvoiceInternalStatus.AwaitingOfficial or InvoiceInternalStatus.Error
                && OfficialStatus is OfficialInvoiceStatus.NotIssued or OfficialInvoiceStatus.Failed,
            "Bu fatura taslağa geri alınamaz.");
        InternalStatus = InvoiceInternalStatus.Draft;
        OfficialStatus = OfficialInvoiceStatus.NotIssued;
    }

    /// <summary>Manuel akış: kullanıcı resmî belgeyi portalda düzenleyecek. Hiçbir resmî işlem başlamış sayılmaz.</summary>
    public void MarkAwaitingOfficial()
    {
        Require(InternalStatus is InvoiceInternalStatus.Approved or InvoiceInternalStatus.Error, "Önce faturanın onaylanması gerekir.");
        InternalStatus = InvoiceInternalStatus.AwaitingOfficial;
    }

    public void BeginSubmission(string provider, Guid submissionKey)
    {
        Require(InternalStatus is InvoiceInternalStatus.Approved or InvoiceInternalStatus.AwaitingOfficial or InvoiceInternalStatus.Error,
            "Fatura resmî gönderime uygun durumda değil.");
        Require(OfficialStatus is OfficialInvoiceStatus.NotIssued or OfficialInvoiceStatus.Failed,
            "Bu fatura için resmî işlem zaten başlamış; önce sonucunu sorgulayın.");
        InternalStatus = InvoiceInternalStatus.Processing;
        OfficialStatus = OfficialInvoiceStatus.Submitting;
        OfficialProvider = provider;
        SubmissionKey = submissionKey;
    }

    /// <summary>Sağlayıcıda TASLAK oluştu (resmî belge DEĞİL). İmzalama/gönderme ayrı ve açık onaylı bir adımdır.</summary>
    public void MarkProviderDraftCreated(string providerReference, string? message)
    {
        Require(OfficialStatus is OfficialInvoiceStatus.Submitting or OfficialInvoiceStatus.ResultUnknown, "Sağlayıcı taslağı yalnızca gönderim aşamasındaki faturaya bağlanabilir.");
        OfficialStatus = OfficialInvoiceStatus.Submitting;
        InternalStatus = InvoiceInternalStatus.Processing;
        ProviderReference = providerReference;
        ProviderLastMessage = message;
    }

    /// <summary>Ağ hatası/zaman aşımı: belgenin oluşup oluşmadığı bilinmiyor. Yeniden gönderim engellenir.</summary>
    public void MarkResultUnknown(string message)
    {
        Require(OfficialStatus == OfficialInvoiceStatus.Submitting, "Gönderim aşamasında olmayan fatura için sonuç belirsiz işaretlenemez.");
        OfficialStatus = OfficialInvoiceStatus.ResultUnknown;
        ProviderLastMessage = message;
    }

    public void MarkSubmissionFailed(string message)
    {
        Require(OfficialStatus == OfficialInvoiceStatus.Submitting, "Gönderim aşamasında olmayan fatura için hata işaretlenemez.");
        OfficialStatus = OfficialInvoiceStatus.Failed;
        InternalStatus = InvoiceInternalStatus.Error;
        ProviderLastMessage = message;
        SubmissionKey = null;
    }

    public void RecordOfficialDocument(
        string number, DateOnly issueDate, string? uuid, InvoiceDocumentType documentType,
        OfficialSource source, string? provider, bool verified, DateTime nowUtc)
    {
        Require(InternalStatus is InvoiceInternalStatus.Approved or InvoiceInternalStatus.AwaitingOfficial
                or InvoiceInternalStatus.Processing or InvoiceInternalStatus.Error,
            "Resmî belge bilgisi yalnızca onaylanmış faturaya kaydedilebilir.");
        Require(OfficialStatus != OfficialInvoiceStatus.Issued, "Bu faturaya resmî belge bilgisi zaten kaydedilmiş.");
        Require(!string.IsNullOrWhiteSpace(number), "Resmî fatura numarası zorunludur.");
        Require(documentType != InvoiceDocumentType.Undetermined, "Resmî belge türü seçilmelidir.");
        Require(source != OfficialSource.None, "Resmî belge kaynağı belirtilmelidir.");
        OfficialNumber = number.Trim().ToUpperInvariant();
        OfficialUuid = string.IsNullOrWhiteSpace(uuid) ? null : uuid.Trim().ToLowerInvariant();
        OfficialIssueDate = issueDate;
        OfficialDocumentType = documentType;
        OfficialSource = source;
        OfficialProvider = provider;
        OfficialVerified = verified;
        OfficialRecordedAtUtc = nowUtc;
        OfficialStatus = OfficialInvoiceStatus.Issued;
        InternalStatus = InvoiceInternalStatus.Completed;
        // SubmissionKey korunur: sağlayıcıdan sonradan durum/belge sorgulamak ve iptali doğrulamak için gerekir.
    }

    /// <summary>Henüz resmî belgeye dönüşmemiş faturayı iç süreçte iptal eder.</summary>
    public void CancelUnissued(string reason)
    {
        Require(OfficialStatus is OfficialInvoiceStatus.NotIssued or OfficialInvoiceStatus.Failed
                && InternalStatus is not (InvoiceInternalStatus.Completed or InvoiceInternalStatus.Cancelled
                    or InvoiceInternalStatus.CancellationRequired or InvoiceInternalStatus.Processing),
            "Resmî belge oluşmuş ya da işlemde olan fatura bu yolla iptal edilemez. Resmî iptal sürecini kullanın.");
        Require(!string.IsNullOrWhiteSpace(reason), "İptal nedeni yazılmalıdır.");
        InternalStatus = InvoiceInternalStatus.Cancelled;
        CancelReason = reason.Trim();
    }

    /// <summary>Resmî iptal ihtiyacını işaretler. Resmî durum DEĞİŞMEZ: belge iptal edilene kadar "Düzenlendi" kalır.</summary>
    public void RequestCancellation(string reason, DateTime nowUtc)
    {
        Require(OfficialStatus == OfficialInvoiceStatus.Issued && InternalStatus == InvoiceInternalStatus.Completed,
            "Yalnızca resmî olarak düzenlenmiş fatura için iptal talebi açılabilir.");
        Require(!string.IsNullOrWhiteSpace(reason), "İptal talebinin nedeni yazılmalıdır.");
        InternalStatus = InvoiceInternalStatus.CancellationRequired;
        CancellationReason = reason.Trim();
        CancellationRequestedAtUtc = nowUtc;
    }

    public void WithdrawCancellationRequest()
    {
        Require(InternalStatus == InvoiceInternalStatus.CancellationRequired && OfficialStatus == OfficialInvoiceStatus.Issued,
            "Geri çekilecek bir iptal talebi yok.");
        InternalStatus = InvoiceInternalStatus.Completed;
        CancellationReason = null;
        CancellationRequestedAtUtc = null;
    }

    /// <summary>
    /// Resmî iptalin gerçekleştiği DOĞRULANDIĞINDA çağrılır. Doğrulama yoksa durum değişmez.
    /// Manuel akışta doğrulama = kullanıcının portaldaki işlemi tamamladığını ve referansını beyan etmesidir (GİB'den doğrulanmış değildir).
    /// </summary>
    public void ConfirmOfficialCancellation(bool confirmed, string reference, DateOnly date)
    {
        Require(OfficialStatus == OfficialInvoiceStatus.Issued && InternalStatus == InvoiceInternalStatus.CancellationRequired,
            "Resmî iptal onayı için önce iptal talebi açılmalıdır.");
        Require(confirmed, "Resmî iptalin tamamlandığı doğrulanmadan fatura 'resmî olarak iptal edildi' durumuna alınamaz.");
        Require(!string.IsNullOrWhiteSpace(reference), "Resmî iptal/itiraz/ret referansı veya açıklaması girilmelidir.");
        OfficialStatus = OfficialInvoiceStatus.Cancelled;
        InternalStatus = InvoiceInternalStatus.Cancelled;
        OfficialCancelDate = date;
        OfficialCancelReference = reference.Trim();
    }

    private static void Require(bool condition, string message)
    {
        if (!condition) throw new DomainException(message);
    }
}

public class InvoiceItem : Entity
{
    public Guid InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    public int LineNo { get; set; }
    public Guid? SaleItemId { get; set; }
    public Guid? ProductId { get; set; }
    public string Description { get; set; } = "";
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public decimal DiscountPercent { get; set; }
    public decimal DiscountAmount { get; set; }
    public decimal VatRate { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public class InvoiceAttachment : Entity
{
    public Guid InvoiceId { get; set; }
    public Invoice? Invoice { get; set; }
    public AttachmentKind Kind { get; set; }
    public string OriginalFileName { get; set; } = "";
    public string StoredFileName { get; set; } = "";
    public string ContentType { get; set; } = "";
    public long SizeBytes { get; set; }
    public string Sha256 { get; set; } = "";
}

// ───────────── Cari hesap ─────────────

/// <summary>
/// Müşteri cari hareketi. Borç = müşterinin bize borcunu artırır (satış, açılış borcu);
/// Alacak = azaltır (tahsilat, iade). Bakiye = Σ Borç − Σ Alacak (pozitif = müşteri bize borçlu).
/// Hareketler değiştirilmez/silinmez; düzeltme ters kayıtla yapılır.
/// </summary>
public class CustomerLedgerEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    /// <summary>Kayıt sırası (veritabanı üretir); aynı gün içinde sıralamayı belirler.</summary>
    public long Seq { get; set; }
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public DateOnly EntryDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public LedgerEntryType EntryType { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public LedgerSourceType SourceType { get; set; }
    public Guid? SourceId { get; set; }
    /// <summary>İç referans / belge numarası (ör. ACL-2026-000001, SAT-2026-000012).</summary>
    public string ReferenceNo { get; set; } = "";
    public string Description { get; set; } = "";
    public Guid? ReversesEntryId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public string CreatedByUserName { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Tedarikçi cari hareketi. Alacak = tedarikçiye borcumuz artar (alış); Borç = azalır (ödeme). Bakiye = Σ Alacak − Σ Borç.</summary>
public class SupplierLedgerEntry
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public long Seq { get; set; }
    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public DateOnly EntryDate { get; set; }
    public DateOnly? DueDate { get; set; }
    public LedgerEntryType EntryType { get; set; }
    public decimal Debit { get; set; }
    public decimal Credit { get; set; }
    public LedgerSourceType SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public string ReferenceNo { get; set; } = "";
    public string Description { get; set; } = "";
    public Guid? ReversesEntryId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public string CreatedByUserName { get; set; } = "";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

// ───────────── Tahsilat ─────────────

public class Payment : Entity
{
    public string Number { get; set; } = "";
    public Guid CustomerId { get; set; }
    public Customer? Customer { get; set; }
    public DateOnly PaymentDate { get; set; }
    public decimal Amount { get; set; }
    public PaymentMethod Method { get; set; }
    public string? BankReference { get; set; }
    public string? Note { get; set; }
    public PaymentStatus Status { get; set; } = PaymentStatus.Active;
    public DateTime? ReversedAtUtc { get; set; }
    public string? ReverseReason { get; set; }
    public string? ReceiptOriginalName { get; set; }
    public string? ReceiptStoredName { get; set; }
    public string? ReceiptContentType { get; set; }
    public Guid? ClientRequestId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public List<PaymentAllocation> Allocations { get; set; } = [];

    public void Reverse(string reason, DateTime nowUtc)
    {
        if (Status == PaymentStatus.Reversed) throw new DomainException("Tahsilat zaten geri alınmış.");
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("Geri alma nedeni yazılmalıdır.");
        Status = PaymentStatus.Reversed;
        ReversedAtUtc = nowUtc;
        ReverseReason = reason.Trim();
    }
}

/// <summary>
/// Tahsilatın bir satışa (ve isteğe bağlı olarak o satışın faturasına) dağıtılan kısmı.
/// Dağıtım cari bakiyeyi DEĞİŞTİRMEZ (bakiyeyi tahsilatın kendisi düşürür); yalnızca hangi borcun kapandığını gösterir.
/// </summary>
public class PaymentAllocation : Entity
{
    public Guid PaymentId { get; set; }
    public Payment? Payment { get; set; }
    public Guid SaleId { get; set; }
    public Sale? Sale { get; set; }
    public Guid? InvoiceId { get; set; }
    public decimal Amount { get; set; }
    public bool IsActive { get; set; } = true;
    public DateTime? ReversedAtUtc { get; set; }
}

// ───────────── Alış / stok ─────────────

public class PurchaseDocument : Entity
{
    public string Number { get; set; } = "";
    public Guid SupplierId { get; set; }
    public Supplier? Supplier { get; set; }
    public DateOnly DocumentDate { get; set; }
    public DateOnly? DueDate { get; set; }
    /// <summary>Tedarikçinin kendi belge/fatura numarası.</summary>
    public string? SupplierDocumentNo { get; set; }
    public PurchaseStatus Status { get; set; } = PurchaseStatus.Draft;
    public decimal NetTotal { get; set; }
    public decimal VatTotal { get; set; }
    public decimal GrandTotal { get; set; }
    public string? Note { get; set; }
    public Guid? ClientRequestId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public List<PurchaseItem> Items { get; set; } = [];
}

public class PurchaseItem : Entity
{
    public Guid PurchaseDocumentId { get; set; }
    public PurchaseDocument? PurchaseDocument { get; set; }
    public int LineNo { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public string ProductName { get; set; } = "";
    public decimal Quantity { get; set; }
    public string Unit { get; set; } = "";
    public decimal UnitPrice { get; set; }
    public decimal VatRate { get; set; }
    public decimal NetAmount { get; set; }
    public decimal VatAmount { get; set; }
    public decimal LineTotal { get; set; }
}

public class StockMovement
{
    public Guid Id { get; set; } = Guid.CreateVersion7();
    public long Seq { get; set; }
    public Guid ProductId { get; set; }
    public Product? Product { get; set; }
    public DateOnly MovementDate { get; set; }
    public StockMovementType MovementType { get; set; }
    /// <summary>İşaretli miktar: giriş +, çıkış −.</summary>
    public decimal QuantityDelta { get; set; }
    public LedgerSourceType SourceType { get; set; }
    public Guid? SourceId { get; set; }
    public Guid? SourceLineId { get; set; }
    public string? Note { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
