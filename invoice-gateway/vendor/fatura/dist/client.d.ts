import type { EnvironmentKey, ApiResponse, InvoiceListItem, InvoiceDetails, DraftInvoice, CreateInvoiceResult, UserData } from "./types";
interface DateRange {
    startDate: string;
    endDate: string;
}
interface SignedOptions {
    signed: boolean;
}
interface CreateInvoiceOptions {
    sign?: boolean;
}
export declare class FaturaClient {
    private readonly baseURL;
    private readonly loginCmd;
    private readonly logoutCmd;
    constructor(env?: EnvironmentKey);
    private buildHeaders;
    private runCommand;
    getToken(userName: string, password: string): Promise<string>;
    logout(token: string): Promise<unknown>;
    createDraftInvoice(token: string, invoiceDetails: InvoiceDetails): Promise<DraftInvoice>;
    findInvoice(token: string, draftInvoice: DraftInvoice): Promise<InvoiceListItem | undefined>;
    signDraftInvoice(token: string, draftInvoice: InvoiceListItem): Promise<ApiResponse>;
    cancelDraftInvoice(token: string, reason: string, draftInvoice: InvoiceListItem): Promise<unknown>;
    getAllInvoicesByDateRange(token: string, { startDate, endDate }: DateRange): Promise<InvoiceListItem[]>;
    getAllInvoicesIssuedToMeByDateRange(token: string, { startDate, endDate }: DateRange): Promise<InvoiceListItem[]>;
    getInvoiceHTML(token: string, uuid: string, { signed }: SignedOptions): Promise<string>;
    getDownloadURL(token: string, invoiceUUID: string, { signed }: SignedOptions): string;
    getUserData(token: string): Promise<UserData>;
    updateUserData(token: string, userData: UserData): Promise<unknown>;
    getRecipientDataByTaxIDOrTRID(token: string, taxIDOrTRID: string): Promise<unknown>;
    /**
     * İmza SMS'inin gönderileceği **kayıtlı** cep telefonunu döner.
     *
     * e-Arşiv portalı SMS'i şirket yetkilisinin sisteme kayıtlı numarasına
     * gönderir; numara dışarıdan verilmez. `sendSignSMSCode` çağrılmadan önce
     * numara buradan alınmalıdır.
     */
    getSignPhoneNumber(token: string): Promise<string | undefined>;
    sendSignSMSCode(token: string, phone: string): Promise<string | undefined>;
    /**
     * SMS kodunu doğrular **ve `invoices` listesindeki faturaları imzalar**.
     *
     * ☢️ İmzalama mali işlem oluşturur.
     *
     * `OPR: 1` ve `DATA` alanları zorunludur: bunlar olmadan GİB SMS kodunu
     * doğrular ama hiçbir faturayı imzalamaz.
     *
     * @returns GİB faturaları imzaladıysa `true` (`data.sonuc === "1"`).
     */
    verifySignSMSCode(token: string, smsCode: string, operationId: string, invoices?: InvoiceListItem[]): Promise<boolean>;
    createInvoice(userId: string, password: string, invoiceDetails: InvoiceDetails, { sign }?: CreateInvoiceOptions): Promise<CreateInvoiceResult>;
    createInvoiceAndGetDownloadURL(userId: string, password: string, invoiceDetails: InvoiceDetails, options?: CreateInvoiceOptions): Promise<string>;
    createInvoiceAndGetHTML(userId: string, password: string, invoiceDetails: InvoiceDetails, options?: CreateInvoiceOptions): Promise<string>;
}
export declare function createFaturaClient(env?: EnvironmentKey): FaturaClient;
export {};
