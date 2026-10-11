// API sözleşmesinin (C# DTO'ları) TypeScript karşılığı + Türkçe etiketler.

export interface Paged<T> { items: T[]; total: number; page: number; pageSize: number }

export type CustomerType = "Company" | "Individual";
export type EInvoiceStatus = "Unknown" | "Registered" | "NotRegistered";
export type PaymentType = "Cash" | "Credit";
export type SaleStatus = "Draft" | "Approved" | "PartiallyPaid" | "Paid" | "Cancelled";
export type PaymentMethod = "Cash" | "BankTransfer" | "Other";
export type PaymentStatus = "Active" | "Reversed";
export type DocType = "Undetermined" | "EArchive" | "EInvoice";
export type InternalStatus = "Draft" | "Approved" | "AwaitingOfficial" | "Processing" | "Error" | "Completed" | "CancellationRequired" | "Cancelled";
export type OfficialStatus = "NotIssued" | "Submitting" | "ResultUnknown" | "Issued" | "Failed" | "Cancelled";
export type OfficialSource = "None" | "ManualEntry" | "Provider";
export type LedgerType = "OpeningBalance" | "Sale" | "SaleReversal" | "Payment" | "PaymentReversal" | "Return" | "Adjustment" | "Manual" | "Purchase" | "PurchaseReversal";
export type StockMoveType = "Opening" | "Purchase" | "Sale" | "Return" | "Adjustment" | "SaleReversal" | "PurchaseReversal";
export type PurchaseStatus = "Draft" | "Approved" | "Cancelled";

export const L = {
  customerType: { Company: "Şirket", Individual: "Şahıs" } as Record<CustomerType, string>,
  einvoice: { Unknown: "Bilinmiyor", Registered: "e-Fatura mükellefi", NotRegistered: "e-Fatura mükellefi değil" } as Record<EInvoiceStatus, string>,
  paymentType: { Cash: "Peşin", Credit: "Vadeli" } as Record<PaymentType, string>,
  saleStatus: { Draft: "Taslak", Approved: "Onaylandı", PartiallyPaid: "Kısmen tahsil edildi", Paid: "Tahsil edildi", Cancelled: "İptal edildi" } as Record<SaleStatus, string>,
  method: { Cash: "Nakit", BankTransfer: "Banka havalesi / EFT", Other: "Diğer" } as Record<PaymentMethod, string>,
  docType: { Undetermined: "Belirlenmedi", EArchive: "e-Arşiv Fatura", EInvoice: "e-Fatura" } as Record<DocType, string>,
  internal: {
    Draft: "Taslak", Approved: "İç onay verildi", AwaitingOfficial: "Resmî işlem bekleniyor", Processing: "Sağlayıcıda işlemde",
    Error: "İşlem hatası", Completed: "Tamamlandı", CancellationRequired: "İptal isteği gerekiyor", Cancelled: "İptal edildi",
  } as Record<InternalStatus, string>,
  official: {
    NotIssued: "Resmî belge yok", Submitting: "Sağlayıcıda taslak/işlemde", ResultUnknown: "Sonuç doğrulanamadı",
    Issued: "Resmî belge kayıtlı", Failed: "Gönderim başarısız", Cancelled: "Resmî olarak iptal edildi",
  } as Record<OfficialStatus, string>,
  ledger: {
    OpeningBalance: "Açılış bakiyesi", Sale: "Satış", SaleReversal: "Satış iptali (ters kayıt)", Payment: "Tahsilat", PaymentReversal: "Tahsilat geri alma (ters kayıt)",
    Return: "İade", Adjustment: "Düzeltme", Manual: "Manuel hareket", Purchase: "Alış", PurchaseReversal: "Alış iptali (ters kayıt)",
  } as Record<LedgerType, string>,
  stockMove: { Opening: "Açılış stoğu", Purchase: "Alış", Sale: "Satış", Return: "İade", Adjustment: "Düzeltme", SaleReversal: "Satış iptali", PurchaseReversal: "Alış iptali" } as Record<StockMoveType, string>,
  purchaseStatus: { Draft: "Taslak", Approved: "Onaylandı", Cancelled: "İptal edildi" } as Record<PurchaseStatus, string>,
};

export interface OpeningBalanceReq { amount: number; date: string; dueDate?: string | null; description?: string | null }

export interface CustomerListItem {
  id: string; name: string; customerType: CustomerType; phone: string | null; taxNumber: string | null; isActive: boolean;
  balance: number; overdueAmount: number; lastSaleDate: string | null; lastPaymentDate: string | null;
}

export interface CustomerDetail {
  id: string; name: string; customerType: CustomerType; taxOffice: string | null; taxNumber: string | null; nationalId: string | null;
  phone: string | null; email: string | null; address: string | null; city: string | null; district: string | null; paymentTermDays: number;
  eInvoiceStatus: EInvoiceStatus; note: string | null; isActive: boolean;
  balance: number; overdueAmount: number; totalSales: number; totalPayments: number; unallocatedCredit: number;
  hasOpeningBalance: boolean; openingBalanceAmount: number; openingBalanceDate: string | null; openingReference: string | null; openSaleCount: number;
}

export interface CustomerForm {
  name: string; customerType: CustomerType; taxOffice: string; taxNumber: string; nationalId: string; phone: string; email: string;
  address: string; city: string; district: string; paymentTermDays: number; eInvoiceStatus: EInvoiceStatus; note: string; isActive: boolean;
}

export interface Product {
  id: string; code: string; name: string; categoryId: string | null; categoryName: string | null; unit: string; purchasePrice: number; salePrice: number;
  vatRate: number; trackStock: boolean; minStock: number; stockQuantity: number; description: string | null; isActive: boolean;
}
export interface Category { id: string; name: string }
export interface CustomerPrice { id: string; customerId: string; customerName: string; productId: string; productName: string; unit: string; price: number; defaultPrice: number }
export interface PriceLookup { price: number; isCustomerPrice: boolean; defaultPrice: number; vatRate: number; unit: string }

export interface SaleItem {
  id: string; lineNo: number; productId: string; productCode: string; productName: string; quantity: number; unit: string; unitPrice: number;
  discountPercent: number; discountAmount: number; vatRate: number; netAmount: number; vatAmount: number; lineTotal: number;
}
export interface SaleInvoiceRef { id: string; internalRef: string; officialNumber: string | null; internalStatus: InternalStatus; officialStatus: OfficialStatus }
export interface Sale {
  id: string; number: string; customerId: string; customerName: string; saleDate: string; paymentType: PaymentType; dueDate: string; status: SaleStatus;
  grossTotal: number; discountTotal: number; netTotal: number; vatTotal: number; grandTotal: number; paidTotal: number; remaining: number;
  note: string | null; cancelReason: string | null; createdAtUtc: string; items: SaleItem[]; invoices: SaleInvoiceRef[]; warnings: string[];
}
export interface SaleListItem {
  id: string; number: string; customerId: string; customerName: string; saleDate: string; dueDate: string; status: SaleStatus; paymentType: PaymentType;
  netTotal: number; vatTotal: number; grandTotal: number; paidTotal: number; remaining: number; invoiceId: string | null; invoiceRef: string | null; invoiceOfficialNumber: string | null;
}

export interface Allocation { id: string; saleId: string; saleNumber: string; invoiceId: string | null; invoiceRef: string | null; officialNumber: string | null; amount: number; isActive: boolean }
export interface Payment {
  id: string; number: string; customerId: string; customerName: string; paymentDate: string; amount: number; method: PaymentMethod; bankReference: string | null;
  note: string | null; status: PaymentStatus; allocatedAmount: number; unallocatedAmount: number; hasReceipt: boolean; reversedAtUtc: string | null;
  reverseReason: string | null; createdAtUtc: string; allocations: Allocation[];
}
export interface OpenSale { saleId: string; number: string; saleDate: string; dueDate: string; grandTotal: number; paid: number; remaining: number; invoiceId: string | null; invoiceRef: string | null; officialNumber: string | null; isOverdue: boolean }

export interface LedgerEntry {
  id: string; date: string; entryType: LedgerType; referenceNo: string; description: string; debit: number; credit: number; balance: number;
  createdBy: string; createdAtUtc: string; sourceType: string; sourceId: string | null; isReversed: boolean; reversesEntryId: string | null;
}
export interface Statement {
  accountId: string; accountName: string; from: string | null; to: string | null; carriedBalance: number; totalDebit: number; totalCredit: number;
  closingBalance: number; entries: LedgerEntry[];
}

export interface Attachment { id: string; kind: "Pdf" | "Xml" | "Other"; fileName: string; sizeBytes: number; uploadedAtUtc: string }
export interface InvoiceLine { id: string; lineNo: number; description: string; quantity: number; unit: string; unitPrice: number; discountPercent: number; discountAmount: number; vatRate: number; netAmount: number; vatAmount: number; lineTotal: number }
export interface InvoiceIssue { field: string; message: string; isBlocking: boolean }
export interface Invoice {
  id: string; internalRef: string; customerId: string; customerName: string; saleId: string; saleNumber: string; invoiceDate: string; dueDate: string | null;
  documentType: DocType; buyerName: string; buyerTaxOffice: string | null; buyerTaxNumber: string | null; buyerNationalId: string | null; buyerAddress: string | null;
  buyerCity: string | null; buyerDistrict: string | null; buyerEmail: string | null; customerEInvoiceStatus: EInvoiceStatus;
  grossTotal: number; discountTotal: number; netTotal: number; vatTotal: number; grandTotal: number; paidTotal: number; remaining: number; note: string | null;
  internalStatus: InternalStatus; officialStatus: OfficialStatus; officialNumber: string | null; officialUuid: string | null; officialIssueDate: string | null;
  officialDocumentType: DocType | null; officialSource: OfficialSource; officialProvider: string | null; officialVerified: boolean; providerIsLegallyValid: boolean;
  officialRecordedAtUtc: string | null; providerLastMessage: string | null; providerReference: string | null;
  cancellationReason: string | null; cancellationRequestedAtUtc: string | null; officialCancelDate: string | null; officialCancelReference: string | null; officialCancelVerified: boolean; cancelReason: string | null;
  items: InvoiceLine[]; attachments: Attachment[]; issues: InvoiceIssue[];
}
export interface InvoiceListItem {
  id: string; internalRef: string; officialNumber: string | null; customerId: string; customerName: string; invoiceDate: string; dueDate: string | null;
  documentType: DocType; netTotal: number; vatTotal: number; grandTotal: number; paidTotal: number; remaining: number;
  internalStatus: InternalStatus; officialStatus: OfficialStatus; officialSource: OfficialSource; officialVerified: boolean; officialProvider: string | null;
  saleId: string; saleNumber: string; attachments: Attachment[];
}
export interface ProviderInfo {
  name: string; displayName: string; liveSendingEnabled: boolean; canSubmit: boolean; submitBlockedReason: string | null; availableProviders: string[];
  capabilities: { canValidate: boolean; canCreateDraft: boolean; canIssue: boolean; canQueryStatus: boolean; canDownloadDocument: boolean; canCancel: boolean;
    supportsEArchive: boolean; supportsEInvoice: boolean; isLiveCapable: boolean; requiresSmsCode: boolean; canReportCancellation: boolean; description: string };
}

export interface StockLevel { productId: string; code: string; name: string; unit: string; trackStock: boolean; quantity: number; minStock: number; isLow: boolean; stockValueAtCost: number }
export interface StockMovement { id: string; date: string; productId: string; productCode: string; productName: string; unit: string; movementType: StockMoveType; quantityDelta: number; note: string | null; sourceType: string; sourceId: string | null; createdAtUtc: string }

export interface SupplierListItem { id: string; name: string; phone: string | null; taxNumber: string | null; isActive: boolean; balance: number; lastPurchaseDate: string | null }
export interface Supplier { id: string; name: string; taxOffice: string | null; taxNumber: string | null; phone: string | null; email: string | null; address: string | null; note: string | null; isActive: boolean; balance: number; hasOpeningBalance: boolean }
export interface PurchaseItem { id: string; lineNo: number; productId: string; productName: string; quantity: number; unit: string; unitPrice: number; vatRate: number; netAmount: number; vatAmount: number; lineTotal: number }
export interface Purchase { id: string; number: string; supplierId: string; supplierName: string; documentDate: string; dueDate: string | null; supplierDocumentNo: string | null; status: PurchaseStatus; netTotal: number; vatTotal: number; grandTotal: number; note: string | null; items: PurchaseItem[] }

export interface Card { amount: number; count: number }
export interface Dashboard {
  today: string; monthFrom: string; monthTo: string; isDemo: boolean;
  todaySales: Card; monthSales: Card; totalReceivable: Card; overdueReceivable: Card; pendingCollections: Card; monthCollected: Card;
  draftInvoices: Card; awaitingOfficialInvoices: Card; monthInvoicesCreated: Card; lowStock: Card; customerAdvances: number;
  recentSales: { id: string; number: string; customerName: string; date: string; grandTotal: number; status: SaleStatus }[];
  recentInvoices: { id: string; internalRef: string; officialNumber: string | null; customerName: string; date: string; grandTotal: number; internalStatus: InternalStatus; officialStatus: OfficialStatus }[];
  recentPayments: { id: string; number: string; customerName: string; date: string; amount: number; method: PaymentMethod }[];
  upcomingDues: { saleId: string; saleNumber: string; customerId: string; customerName: string; dueDate: string; remaining: number }[];
  topBalances: { customerId: string; customerName: string; balance: number; overdue: number }[];
  lowStockItems: { productId: string; name: string; unit: string; quantity: number; minStock: number }[];
  definitions: Record<string, string>;
}

export interface Settings {
  company: { name: string; taxOffice: string | null; taxNumber: string | null; address: string | null; phone: string | null; email: string | null };
  vatRates: number[]; units: string[]; allowNegativeStock: boolean; invoiceProvider: string; databaseMode: string; liveSendingEnabled: boolean;
}
export interface AuditLog { id: number; timestampUtc: string; userName: string; action: string; entityType: string; entityId: string | null; details: string }

export interface Measure { title: string; definition: string; amount: number; count: number; net: number | null; vat: number | null; note: string | null }
export interface Measures { from: string; to: string; commercialSales: Measure; officialInvoices: Measure; collections: Measure; explanation: string }
export interface SalesReport { from: string; to: string; groupBy: string; definition: string; rows: { key: string; label: string; saleCount: number; quantity: number; unit: string | null; netTotal: number; vatTotal: number; grandTotal: number }[]; netTotal: number; vatTotal: number; grandTotal: number; saleCount: number }
export interface CollectionsReport { from: string; to: string; definition: string; rows: { paymentId: string; number: string; date: string; customerId: string; customerName: string; method: PaymentMethod; amount: number; unallocated: number; bankReference: string | null }[]; total: number; byMethod: Record<string, number> }
export interface ReceivablesReport { definition: string; rows: { customerId: string; customerName: string; phone: string | null; balance: number; overdue: number; lastPaymentDate: string | null; oldestOverdueDate: string | null }[]; totalReceivable: number; totalOverdue: number; customerAdvances: number }
export interface SupplierDebts { definition: string; rows: { supplierId: string; supplierName: string; phone: string | null; balance: number }[]; total: number }
export interface InvoiceStatusReport { from: string; to: string; definition: string; rows: { internalStatus: InternalStatus; officialStatus: OfficialStatus; count: number; grandTotal: number }[] }
export interface OpenInvoices { definition: string; rows: InvoiceListItem[]; totalRemaining: number }
