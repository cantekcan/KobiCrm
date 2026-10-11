namespace SutTicaretCRM.Domain;

public enum CustomerType { Company = 0, Individual = 1 }

/// <summary>Müşterinin e-Fatura mükellefiyeti. Doğrulanmadıkça Unknown kalır; Unknown hiçbir zaman "mükellef değil" olarak yorumlanmaz.</summary>
public enum EInvoiceTaxpayerStatus { Unknown = 0, Registered = 1, NotRegistered = 2 }

public enum PaymentType { Cash = 0, Credit = 1 }

public enum SaleStatus { Draft = 0, Approved = 1, PartiallyPaid = 2, Paid = 3, Cancelled = 4 }

public enum PaymentMethod { Cash = 0, BankTransfer = 1, Other = 2 }

public enum PaymentStatus { Active = 0, Reversed = 1 }

public enum LedgerEntryType
{
    OpeningBalance = 0,
    Sale = 1,
    SaleReversal = 2,
    Payment = 3,
    PaymentReversal = 4,
    Return = 5,
    Adjustment = 6,
    Manual = 7,
    Purchase = 8,
    PurchaseReversal = 9,
}

public enum LedgerSourceType { None = 0, Opening = 1, Sale = 2, Payment = 3, Purchase = 4, Manual = 5 }

/// <summary>Belge türü. Alıcının mükellefiyetine göre kullanıcı seçer; sistem varsaymaz.</summary>
public enum InvoiceDocumentType { Undetermined = 0, EArchive = 1, EInvoice = 2 }

/// <summary>CRM içi iş akışı durumu. Resmî belge durumundan (<see cref="OfficialInvoiceStatus"/>) bağımsızdır.</summary>
public enum InvoiceInternalStatus
{
    Draft = 0,
    Approved = 1,
    AwaitingOfficial = 2,
    Processing = 3,
    Error = 4,
    Completed = 5,
    CancellationRequired = 6,
    Cancelled = 7,
}

/// <summary>Resmî belgenin CRM'in bildiği durumu. "Issued" yalnızca resmî numara kaydedildiğinde olur.</summary>
public enum OfficialInvoiceStatus { NotIssued = 0, Submitting = 1, ResultUnknown = 2, Issued = 3, Failed = 4, Cancelled = 5 }

/// <summary>Resmî belge bilgisinin kaynağı.</summary>
public enum OfficialSource { None = 0, ManualEntry = 1, Provider = 2 }

public enum StockMovementType { Opening = 0, Purchase = 1, Sale = 2, Return = 3, Adjustment = 4, SaleReversal = 5, PurchaseReversal = 6 }

public enum PurchaseStatus { Draft = 0, Approved = 1, Cancelled = 2 }

public enum AttachmentKind { Pdf = 0, Xml = 1, Other = 2 }
