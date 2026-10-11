"use strict";
Object.defineProperty(exports, "__esModule", { value: true });
exports.FaturaClient = void 0;
exports.createFaturaClient = createFaturaClient;
const uuid_1 = require("./utils/uuid");
const number_1 = require("./utils/number");
const environment_1 = require("./constants/environment");
const commands_1 = require("./constants/commands");
/** GİB API hata mesajını okur ve varsa Error fırlatır. */
function assertApiSuccess(response) {
    var _a;
    if (response.error && response.error !== "0") {
        const raw = (_a = response.messages) === null || _a === void 0 ? void 0 : _a[0];
        const text = typeof raw === "string" ? raw : typeof raw === "object" && raw !== null ? raw.text : "GİB API hatası";
        throw new Error(text !== null && text !== void 0 ? text : "GİB API hatası");
    }
}
/**
 * GİB taslak oluşturma cevabını doğrular.
 *
 * `EARSIV_PORTAL_FATURA_OLUSTUR` hata durumunda da `error` alanı göndermez;
 * sonucu yalnızca `data` içindeki serbest metinden anlaşılır. Bu yüzden
 * `assertApiSuccess` bu komut için yeterli değildir.
 */
function assertDraftCreated(response) {
    const text = typeof response.data === "string" ? response.data : "";
    if (!/başarıyla oluşturulmuştur/i.test(text)) {
        throw new Error(text || "GİB taslak faturayı oluşturmadı");
    }
}
// ─── FaturaClient ─────────────────────────────────────────────────────────────
class FaturaClient {
    constructor(env = "PROD") {
        // YEREL YAMA 2: TEST portalında taslak listesi 'Buyuk' tipiyle sorgulanır (mlevent/fatura Gib.php test modu); canlıda '5000/30000'.
        this.listType = env === "PROD" ? "5000/30000" : "Buyuk";
        this.baseURL = environment_1.ENV[env].BASE_URL;
        this.loginCmd = env === "PROD" ? "anologin" : "login";
        this.logoutCmd = env === "PROD" ? "anologin" : "logout";
    }
    // ─── Core HTTP ─────────────────────────────────────────────────────────────
    buildHeaders() {
        return {
            accept: "*/*",
            "accept-language": "tr,en-US;q=0.9,en;q=0.8",
            "cache-control": "no-cache",
            "content-type": "application/x-www-form-urlencoded;charset=UTF-8",
            pragma: "no-cache",
            "sec-fetch-mode": "cors",
            "sec-fetch-site": "same-origin",
        };
    }
    async runCommand(token, command, pageName, data = {}) {
        const response = await fetch(`${this.baseURL}/earsiv-services/dispatch`, {
            method: "POST",
            headers: this.buildHeaders(),
            body: `cmd=${command}&callid=${(0, uuid_1.generateUUID)()}&pageName=${pageName}&token=${token}&jp=${encodeURIComponent(JSON.stringify(data))}`,
        });
        const json = (await response.json());
        assertApiSuccess(json);
        return json;
    }
    // ─── Auth ──────────────────────────────────────────────────────────────────
    async getToken(userName, password) {
        const response = await fetch(`${this.baseURL}/earsiv-services/assos-login`, {
            method: "POST",
            headers: this.buildHeaders(),
            body: `assoscmd=${this.loginCmd}&rtype=json&userid=${encodeURIComponent(userName)}&sifre=${encodeURIComponent(password)}&sifre2=${encodeURIComponent(password)}&parola=1&`,
        });
        const json = (await response.json());
        assertApiSuccess(json);
        return json.token;
    }
    async logout(token) {
        const response = await fetch(`${this.baseURL}/earsiv-services/assos-login`, {
            method: "POST",
            headers: this.buildHeaders(),
            body: `assoscmd=${this.logoutCmd}&rtype=json&token=${encodeURIComponent(token)}&`,
        });
        const json = (await response.json());
        return json.data;
    }
    // ─── Invoice CRUD ──────────────────────────────────────────────────────────
    async createDraftInvoice(token, invoiceDetails) {
        var _a, _b, _c, _d, _e, _f, _g, _h, _j, _k, _l, _m, _o, _p, _q, _r, _s, _t, _u, _v, _w, _x, _y, _z, _0, _1, _2, _3, _4, _5, _6, _7, _8, _9, _10, _11, _12, _13, _14, _15, _16, _17, _18, _19, _20, _21, _22, _23, _24, _25, _26, _27, _28, _29, _30, _31, _32, _33, _34, _35, _36, _37, _38, _39, _40, _41, _42, _43, _44, _45, _46, _47, _48, _49, _50, _51, _52;
        const invoiceData = {
            // GİB ETTN'i sunucu tarafında üretir; istemciden gelen bir UUID
            // "Ettn ya eksik ya boş ya da 36 uzunluk sınırına uymuyor." hatası verir.
            faturaUuid: "",
            belgeNumarasi: (_a = invoiceDetails.documentNumber) !== null && _a !== void 0 ? _a : "",
            faturaTarihi: invoiceDetails.date,
            saat: invoiceDetails.time,
            paraBirimi: (_b = invoiceDetails.currency) !== null && _b !== void 0 ? _b : "TRY",
            dovzTLkur: (_c = invoiceDetails.currencyRate) !== null && _c !== void 0 ? _c : "0",
            faturaTipi: (_d = invoiceDetails.invoiceType) !== null && _d !== void 0 ? _d : "5000/30000",
            hangiTip: (_e = invoiceDetails.hangiTip) !== null && _e !== void 0 ? _e : "Buyuk",
            siparisNumarasi: (_f = invoiceDetails.orderNumber) !== null && _f !== void 0 ? _f : "",
            siparisTarihi: (_g = invoiceDetails.orderDate) !== null && _g !== void 0 ? _g : "",
            irsaliyeNumarasi: (_h = invoiceDetails.dispatchNumber) !== null && _h !== void 0 ? _h : "",
            irsaliyeTarihi: (_j = invoiceDetails.dispatchDate) !== null && _j !== void 0 ? _j : "",
            fisNo: (_k = invoiceDetails.slipNumber) !== null && _k !== void 0 ? _k : "",
            fisTarihi: (_l = invoiceDetails.slipDate) !== null && _l !== void 0 ? _l : "",
            fisSaati: (_m = invoiceDetails.slipTime) !== null && _m !== void 0 ? _m : " ",
            fisTipi: (_o = invoiceDetails.slipType) !== null && _o !== void 0 ? _o : " ",
            zRaporNo: (_p = invoiceDetails.zReportNumber) !== null && _p !== void 0 ? _p : "",
            okcSeriNo: (_q = invoiceDetails.okcSerialNumber) !== null && _q !== void 0 ? _q : "",
            vknTckn: (_r = invoiceDetails.taxIDOrTRID) !== null && _r !== void 0 ? _r : "11111111111",
            aliciUnvan: (_s = invoiceDetails.title) !== null && _s !== void 0 ? _s : "",
            aliciAdi: (_t = invoiceDetails.name) !== null && _t !== void 0 ? _t : "",
            aliciSoyadi: (_u = invoiceDetails.surname) !== null && _u !== void 0 ? _u : "",
            bulvarcaddesokak: (_v = invoiceDetails.fullAddress) !== null && _v !== void 0 ? _v : "",
            binaAdi: (_w = invoiceDetails.buildingName) !== null && _w !== void 0 ? _w : "",
            binaNo: (_x = invoiceDetails.buildingNumber) !== null && _x !== void 0 ? _x : "",
            kapiNo: (_y = invoiceDetails.doorNumber) !== null && _y !== void 0 ? _y : "",
            kasabaKoy: (_z = invoiceDetails.town) !== null && _z !== void 0 ? _z : "",
            mahalleSemtIlce: (_0 = invoiceDetails.district) !== null && _0 !== void 0 ? _0 : "",
            sehir: (_1 = invoiceDetails.city) !== null && _1 !== void 0 ? _1 : " ",
            ulke: (_2 = invoiceDetails.country) !== null && _2 !== void 0 ? _2 : "",
            postaKodu: (_3 = invoiceDetails.zipCode) !== null && _3 !== void 0 ? _3 : "",
            tel: (_4 = invoiceDetails.phoneNumber) !== null && _4 !== void 0 ? _4 : "",
            fax: (_5 = invoiceDetails.faxNumber) !== null && _5 !== void 0 ? _5 : "",
            eposta: (_6 = invoiceDetails.email) !== null && _6 !== void 0 ? _6 : "",
            websitesi: (_7 = invoiceDetails.webSite) !== null && _7 !== void 0 ? _7 : "",
            vergiDairesi: (_8 = invoiceDetails.taxOffice) !== null && _8 !== void 0 ? _8 : "",
            komisyonOrani: (_9 = invoiceDetails.commissionRate) !== null && _9 !== void 0 ? _9 : 0,
            navlunOrani: (_10 = invoiceDetails.freightRate) !== null && _10 !== void 0 ? _10 : 0,
            hammaliyeOrani: (_11 = invoiceDetails.hammaliyeOrani) !== null && _11 !== void 0 ? _11 : 0,
            nakliyeOrani: (_12 = invoiceDetails.nakliyeOrani) !== null && _12 !== void 0 ? _12 : 0,
            komisyonTutari: (_13 = invoiceDetails.komisyonTutari) !== null && _13 !== void 0 ? _13 : "0",
            navlunTutari: (_14 = invoiceDetails.navlunTutari) !== null && _14 !== void 0 ? _14 : "0",
            hammaliyeTutari: (_15 = invoiceDetails.hammaliyeTutari) !== null && _15 !== void 0 ? _15 : "0",
            nakliyeTutari: (_16 = invoiceDetails.nakliyeTutari) !== null && _16 !== void 0 ? _16 : "0",
            komisyonKDVOrani: (_17 = invoiceDetails.komisyonKDVOrani) !== null && _17 !== void 0 ? _17 : 0,
            navlunKDVOrani: (_18 = invoiceDetails.navlunKDVOrani) !== null && _18 !== void 0 ? _18 : 0,
            hammaliyeKDVOrani: (_19 = invoiceDetails.hammaliyeKDVOrani) !== null && _19 !== void 0 ? _19 : 0,
            nakliyeKDVOrani: (_20 = invoiceDetails.nakliyeKDVOrani) !== null && _20 !== void 0 ? _20 : 0,
            komisyonKDVTutari: (_21 = invoiceDetails.komisyonKDVTutari) !== null && _21 !== void 0 ? _21 : "0",
            navlunKDVTutari: (_22 = invoiceDetails.navlunKDVTutari) !== null && _22 !== void 0 ? _22 : "0",
            hammaliyeKDVTutari: (_23 = invoiceDetails.hammaliyeKDVTutari) !== null && _23 !== void 0 ? _23 : "0",
            nakliyeKDVTutari: (_24 = invoiceDetails.nakliyeKDVTutari) !== null && _24 !== void 0 ? _24 : "0",
            gelirVergisiOrani: (_25 = invoiceDetails.gelirVergisiOrani) !== null && _25 !== void 0 ? _25 : 0,
            bagkurTevkifatiOrani: (_26 = invoiceDetails.bagkurTevkifatiOrani) !== null && _26 !== void 0 ? _26 : 0,
            gelirVergisiTevkifatiTutari: (_27 = invoiceDetails.gelirVergisiTevkifatiTutari) !== null && _27 !== void 0 ? _27 : "0",
            bagkurTevkifatiTutari: (_28 = invoiceDetails.bagkurTevkifatiTutari) !== null && _28 !== void 0 ? _28 : "0",
            halRusumuOrani: (_29 = invoiceDetails.halRusumuOrani) !== null && _29 !== void 0 ? _29 : 0,
            ticaretBorsasiOrani: (_30 = invoiceDetails.ticaretBorsasiOrani) !== null && _30 !== void 0 ? _30 : 0,
            milliSavunmaFonuOrani: (_31 = invoiceDetails.milliSavunmaFonuOrani) !== null && _31 !== void 0 ? _31 : 0,
            digerOrani: (_32 = invoiceDetails.digerOrani) !== null && _32 !== void 0 ? _32 : 0,
            halRusumuTutari: (_33 = invoiceDetails.halRusumuTutari) !== null && _33 !== void 0 ? _33 : "0",
            ticaretBorsasiTutari: (_34 = invoiceDetails.ticaretBorsasiTutari) !== null && _34 !== void 0 ? _34 : "0",
            milliSavunmaFonuTutari: (_35 = invoiceDetails.milliSavunmaFonuTutari) !== null && _35 !== void 0 ? _35 : "0",
            digerTutari: (_36 = invoiceDetails.digerTutari) !== null && _36 !== void 0 ? _36 : "0",
            halRusumuKDVOrani: (_37 = invoiceDetails.halRusumuKDVOrani) !== null && _37 !== void 0 ? _37 : 0,
            ticaretBorsasiKDVOrani: (_38 = invoiceDetails.ticaretBorsasiKDVOrani) !== null && _38 !== void 0 ? _38 : 0,
            milliSavunmaFonuKDVOrani: (_39 = invoiceDetails.milliSavunmaFonuKDVOrani) !== null && _39 !== void 0 ? _39 : 0,
            digerKDVOrani: (_40 = invoiceDetails.digerKDVOrani) !== null && _40 !== void 0 ? _40 : 0,
            halRusumuKDVTutari: (_41 = invoiceDetails.halRusumuKDVTutari) !== null && _41 !== void 0 ? _41 : "0",
            ticaretBorsasiKDVTutari: (_42 = invoiceDetails.ticaretBorsasiKDVTutari) !== null && _42 !== void 0 ? _42 : "0",
            milliSavunmaFonuKDVTutari: (_43 = invoiceDetails.milliSavunmaFonuKDVTutari) !== null && _43 !== void 0 ? _43 : "0",
            digerKDVTutari: (_44 = invoiceDetails.digerKDVTutari) !== null && _44 !== void 0 ? _44 : "0",
            iadeTable: ((_45 = invoiceDetails.returnItems) !== null && _45 !== void 0 ? _45 : []).map(() => ({})),
            ozelMatrahTutari: (_46 = invoiceDetails.specialTaxBaseAmount) !== null && _46 !== void 0 ? _46 : "0",
            ozelMatrahOrani: (_47 = invoiceDetails.specialTaxBaseRate) !== null && _47 !== void 0 ? _47 : 0,
            ozelMatrahVergiTutari: ((_48 = invoiceDetails.specialTaxBaseTaxAmount) !== null && _48 !== void 0 ? _48 : 0).toFixed(2),
            vergiCesidi: (_49 = invoiceDetails.taxType) !== null && _49 !== void 0 ? _49 : " ",
            malHizmetTable: invoiceDetails.items.map((item) => {
                var _a, _b, _c, _d, _e, _f, _g, _h, _j, _k, _l, _m, _o;
                return ({
                    iskontoArttm: (_a = item.discount) !== null && _a !== void 0 ? _a : "İskonto",
                    malHizmet: item.name,
                    miktar: (_b = item.quantity) !== null && _b !== void 0 ? _b : 1,
                    birim: (_c = item.unitType) !== null && _c !== void 0 ? _c : "C62",
                    birimFiyat: ((_d = item.unitPrice) !== null && _d !== void 0 ? _d : 0).toFixed(2),
                    fiyat: item.price.toFixed(2),
                    iskontoOrani: (_e = item.discountRate) !== null && _e !== void 0 ? _e : 0,
                    iskontoTutari: ((_f = item.discountAmount) !== null && _f !== void 0 ? _f : 0).toFixed(2),
                    iskontoNedeni: (_g = item.discountReason) !== null && _g !== void 0 ? _g : "",
                    malHizmetTutari: (((_h = item.quantity) !== null && _h !== void 0 ? _h : 0) * ((_j = item.unitPrice) !== null && _j !== void 0 ? _j : 0)).toFixed(2),
                    kdvOrani: ((_k = item.VATRate) !== null && _k !== void 0 ? _k : 0).toFixed(0),
                    vergiOrani: (_l = item.taxRate) !== null && _l !== void 0 ? _l : 0,
                    kdvTutari: ((_m = item.VATAmount) !== null && _m !== void 0 ? _m : 0).toFixed(2),
                    vergininKdvTutari: ((_o = item.VATAmountOfTax) !== null && _o !== void 0 ? _o : 0).toFixed(2),
                });
            }),
            tip: "İskonto",
            matrah: invoiceDetails.grandTotal.toFixed(2),
            malhizmetToplamTutari: invoiceDetails.grandTotal.toFixed(2),
            toplamIskonto: ((_50 = invoiceDetails.totalDiscount) !== null && _50 !== void 0 ? _50 : 0).toFixed(2),
            hesaplanankdv: invoiceDetails.totalVAT.toFixed(2),
            vergilerToplami: invoiceDetails.totalVAT.toFixed(2),
            vergilerDahilToplamTutar: invoiceDetails.grandTotalInclVAT.toFixed(2),
            toplamMasraflar: (_51 = invoiceDetails.toplamMasraflar) !== null && _51 !== void 0 ? _51 : "0",
            odenecekTutar: invoiceDetails.paymentTotal.toFixed(2),
            not: (0, number_1.convertPriceToText)(invoiceDetails.paymentTotal),
        };
        // ETTN sunucu tarafında atandığı için, oluşturmadan önce/sonra o güne ait
        // taslak listesi karşılaştırılarak yeni kayıt bulunur.
        const before = new Set((await this.getAllInvoicesByDateRange(token, {
            startDate: invoiceDetails.date,
            endDate: invoiceDetails.date,
        })).map((item) => item.ettn));
        const invoice = await this.runCommand(token, ...commands_1.COMMANDS.createDraftInvoice, invoiceData);
        assertDraftCreated(invoice);
        const after = await this.getAllInvoicesByDateRange(token, {
            startDate: invoiceDetails.date,
            endDate: invoiceDetails.date,
        });
        const created = after.find((item) => !before.has(item.ettn));
        return {
            date: invoiceDetails.date,
            uuid: (_52 = created === null || created === void 0 ? void 0 : created.ettn) !== null && _52 !== void 0 ? _52 : "",
            documentNumber: created === null || created === void 0 ? void 0 : created.belgeNumarasi,
            listItem: created,
            ...invoice,
        };
    }
    async findInvoice(token, draftInvoice) {
        const invoices = await this.getAllInvoicesByDateRange(token, {
            startDate: draftInvoice.date,
            endDate: draftInvoice.date,
        });
        return invoices.find((inv) => inv.ettn === draftInvoice.uuid);
    }
    async signDraftInvoice(token, draftInvoice) {
        return this.runCommand(token, ...commands_1.COMMANDS.signDraftInvoice, {
            imzalanacaklar: [draftInvoice],
        });
    }
    async cancelDraftInvoice(token, reason, draftInvoice) {
        const result = await this.runCommand(token, ...commands_1.COMMANDS.cancelDraftInvoice, {
            silinecekler: [draftInvoice],
            aciklama: reason,
        });
        return result.data;
    }
    // ─── Queries ───────────────────────────────────────────────────────────────
    async getAllInvoicesByDateRange(token, { startDate, endDate }) {
        const result = await this.runCommand(token, ...commands_1.COMMANDS.getAllInvoicesByDateRange, { baslangic: startDate, bitis: endDate, hangiTip: this.listType, table: [] });
        return result.data;
    }
    async getAllInvoicesIssuedToMeByDateRange(token, { startDate, endDate }) {
        const result = await this.runCommand(token, ...commands_1.COMMANDS.getAllInvoicesIssuedToMeByDateRange, { baslangic: startDate, bitis: endDate, hangiTip: this.listType, table: [] });
        return result.data;
    }
    // ─── Download & HTML ───────────────────────────────────────────────────────
    async getInvoiceHTML(token, uuid, { signed }) {
        const result = await this.runCommand(token, ...commands_1.COMMANDS.getInvoiceHTML, {
            ettn: uuid,
            onayDurumu: signed ? "Onaylandı" : "Onaylanmadı",
        });
        return result.data;
    }
    getDownloadURL(token, invoiceUUID, { signed }) {
        return (`${this.baseURL}/earsiv-services/download` +
            `?token=${token}&ettn=${invoiceUUID}&belgeTip=FATURA` +
            `&onayDurumu=${encodeURIComponent(signed ? "Onaylandı" : "Onaylanmadı")}` +
            `&cmd=downloadResource&`);
    }
    // ─── User ──────────────────────────────────────────────────────────────────
    async getUserData(token) {
        const result = await this.runCommand(token, ...commands_1.COMMANDS.getUserData);
        const d = result.data;
        return {
            taxIDOrTRID: d.vknTckn,
            title: d.unvan,
            name: d.ad,
            surname: d.soyad,
            registryNo: d.sicilNo,
            mersisNo: d.mersisNo,
            taxOffice: d.vergiDairesi,
            fullAddress: d.cadde,
            buildingName: d.apartmanAdi,
            buildingNumber: d.apartmanNo,
            doorNumber: d.kapiNo,
            town: d.kasaba,
            district: d.ilce,
            city: d.il,
            zipCode: d.postaKodu,
            country: d.ulke,
            phoneNumber: d.telNo,
            faxNumber: d.faksNo,
            email: d.ePostaAdresi,
            webSite: d.webSitesiAdresi,
            businessCenter: d.isMerkezi,
        };
    }
    async updateUserData(token, userData) {
        const result = await this.runCommand(token, ...commands_1.COMMANDS.updateUserData, {
            vknTckn: userData.taxIDOrTRID,
            unvan: userData.title,
            ad: userData.name,
            soyad: userData.surname,
            sicilNo: userData.registryNo,
            mersisNo: userData.mersisNo,
            vergiDairesi: userData.taxOffice,
            cadde: userData.fullAddress,
            apartmanAdi: userData.buildingName,
            apartmanNo: userData.buildingNumber,
            kapiNo: userData.doorNumber,
            kasaba: userData.town,
            ilce: userData.district,
            il: userData.city,
            postaKodu: userData.zipCode,
            ulke: userData.country,
            telNo: userData.phoneNumber,
            faksNo: userData.faxNumber,
            ePostaAdresi: userData.email,
            webSitesiAdresi: userData.webSite,
            isMerkezi: userData.businessCenter,
        });
        return result.data;
    }
    // ─── Recipient ─────────────────────────────────────────────────────────────
    async getRecipientDataByTaxIDOrTRID(token, taxIDOrTRID) {
        const result = await this.runCommand(token, ...commands_1.COMMANDS.getRecipientDataByTaxIDOrTRID, {
            vknTcknn: taxIDOrTRID,
        });
        return result.data;
    }
    // ─── SMS ───────────────────────────────────────────────────────────────────
    /**
     * İmza SMS'inin gönderileceği **kayıtlı** cep telefonunu döner.
     *
     * e-Arşiv portalı SMS'i şirket yetkilisinin sisteme kayıtlı numarasına
     * gönderir; numara dışarıdan verilmez. `sendSignSMSCode` çağrılmadan önce
     * numara buradan alınmalıdır.
     */
    async getSignPhoneNumber(token) {
        var _a;
        const result = await this.runCommand(token, ...commands_1.COMMANDS.getSignPhoneNumber, {});
        return (_a = result.data) === null || _a === void 0 ? void 0 : _a.telefon;
    }
    async sendSignSMSCode(token, phone) {
        var _a, _b;
        const result = await this.runCommand(token, ...commands_1.COMMANDS.sendSignSMSCode, {
            CEPTEL: phone,
            KCEPTEL: false,
            TIP: "",
        });
        return (_b = (_a = result.data) === null || _a === void 0 ? void 0 : _a.oid) !== null && _b !== void 0 ? _b : result.oid;
    }
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
    async verifySignSMSCode(token, smsCode, operationId, invoices = []) {
        var _a, _b;
        const result = await this.runCommand(token, ...commands_1.COMMANDS.verifySMSCode, {
            SIFRE: smsCode,
            OID: operationId,
            OPR: 1,
            DATA: invoices,
        });
        // GİB sonucu `data.sonuc` alanında bildirir: "1" imzalandı demektir.
        return String((_b = (_a = result.data) === null || _a === void 0 ? void 0 : _a.sonuc) !== null && _b !== void 0 ? _b : "") === "1";
    }
    // ─── High-level composite ──────────────────────────────────────────────────
    async createInvoice(userId, password, invoiceDetails, { sign = true } = {}) {
        const token = await this.getToken(userId, password);
        const draft = await this.createDraftInvoice(token, invoiceDetails);
        const details = await this.findInvoice(token, draft);
        if (sign && details !== undefined) {
            await this.signDraftInvoice(token, details);
        }
        return { token, uuid: draft.uuid, signed: sign };
    }
    async createInvoiceAndGetDownloadURL(userId, password, invoiceDetails, options) {
        const { token, uuid, signed } = await this.createInvoice(userId, password, invoiceDetails, options);
        return this.getDownloadURL(token, uuid, { signed });
    }
    async createInvoiceAndGetHTML(userId, password, invoiceDetails, options) {
        const { token, uuid, signed } = await this.createInvoice(userId, password, invoiceDetails, options);
        return this.getInvoiceHTML(token, uuid, { signed });
    }
}
exports.FaturaClient = FaturaClient;
// ─── Factory ──────────────────────────────────────────────────────────────────
function createFaturaClient(env = "PROD") {
    return new FaturaClient(env);
}
//# sourceMappingURL=client.js.map