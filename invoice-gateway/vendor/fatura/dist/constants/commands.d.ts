export declare const COMMANDS: {
    readonly createDraftInvoice: readonly ["EARSIV_PORTAL_FATURA_OLUSTUR", "RG_BASITFATURA"];
    readonly getAllInvoicesByDateRange: readonly ["EARSIV_PORTAL_TASLAKLARI_GETIR", "RG_BASITTASLAKLAR"];
    readonly getAllInvoicesIssuedToMeByDateRange: readonly ["EARSIV_PORTAL_ADIMA_KESILEN_BELGELERI_GETIR", "RG_ALICI_TASLAKLAR"];
    readonly signDraftInvoice: readonly ["EARSIV_PORTAL_FATURA_HSM_CIHAZI_ILE_IMZALA", "RG_BASITTASLAKLAR"];
    readonly getInvoiceHTML: readonly ["EARSIV_PORTAL_FATURA_GOSTER", "RG_BASITTASLAKLAR"];
    readonly cancelDraftInvoice: readonly ["EARSIV_PORTAL_FATURA_SIL", "RG_BASITTASLAKLAR"];
    readonly getRecipientDataByTaxIDOrTRID: readonly ["SICIL_VEYA_MERNISTEN_BILGILERI_GETIR", "RG_BASITFATURA"];
    readonly getSignPhoneNumber: readonly ["EARSIV_PORTAL_TELEFONNO_SORGULA", "RG_SMSONAY"];
    readonly sendSignSMSCode: readonly ["EARSIV_PORTAL_SMSSIFRE_GONDER", "RG_SMSONAY"];
    /**
     * İmzalama komutu. GİB bu servisi okunabilir addan opak bir kimliğe taşıdı;
     * `EARSIV_PORTAL_SMSSIFRE_DOGRULA` canlı portalda
     * "Service Not Found" döner.
     */
    readonly verifySMSCode: readonly ["0lhozfib5410mp", "RG_SMSONAY"];
    readonly getUserData: readonly ["EARSIV_PORTAL_KULLANICI_BILGILERI_GETIR", "RG_KULLANICI"];
    readonly updateUserData: readonly ["EARSIV_PORTAL_KULLANICI_BILGILERI_KAYDET", "RG_KULLANICI"];
};
export type CommandKey = keyof typeof COMMANDS;
export type CommandTuple = readonly [string, string];
