// CRM fatura isteği (InvoiceDocumentRequest, camelCase JSON) → f/fatura "InvoiceDetails" dönüşümü + doğrulama.
// DOĞRULAMA UYARISI: alan eşlemeleri f/fatura kaynak kodundan ve README'den çıkarılmıştır; GİB TEST portalında uçtan uca
// doğrulanmamıştır (TEST hesabı olmadan yapılamadı). İndirimli kalemler için özellikle test edin.

const UNIT_CODES = { adet: "C62", kg: "KGM", gram: "GRM", litre: "LTR" }; // diğer birimler "C62" (adet) kabul edilir
const GENERIC_CONSUMER_ID = "11111111111";

export class RejectedError extends Error {
  constructor(message) { super(message); this.name = "RejectedError"; }
}

const num = (v, name) => {
  if (typeof v !== "number" || !Number.isFinite(v)) throw new RejectedError(`${name} sayısal olmalı.`);
  return v;
};
const r2 = (v) => Math.round((v + Number.EPSILON) * 100) / 100;

/** Gelen isteği doğrular. Tutarlar kendi içinde tutarlı değilse REDDEDER (portala hatalı belge gönderilmez). */
export function validateRequest(req) {
  if (!req || typeof req !== "object") throw new RejectedError("Geçersiz istek.");
  if (typeof req.submissionKey !== "string" || !/^[0-9a-fA-F-]{36}$/.test(req.submissionKey)) throw new RejectedError("submissionKey (GUID) zorunlu.");
  if (req.documentType !== "EArchive") throw new RejectedError("Bu ağ geçidi yalnızca e-Arşiv destekler; e-Fatura desteklenmez (f/fatura e-Arşiv portalı otomasyonudur).");
  if (!/^\d{4}-\d{2}-\d{2}$/.test(req.issueDate ?? "")) throw new RejectedError("issueDate yyyy-MM-dd olmalı.");
  const b = req.buyer ?? {};
  const id = b.taxNumber || b.nationalId;
  if (!/^\d{10,11}$/.test(id ?? "")) throw new RejectedError("Alıcı VKN (10) veya TCKN (11 hane) zorunlu.");
  if (!b.name || !String(b.name).trim()) throw new RejectedError("Alıcı adı zorunlu.");
  if (!Array.isArray(req.lines) || req.lines.length === 0 || req.lines.length > 200) throw new RejectedError("Kalem sayısı 1–200 olmalı.");

  let net = 0, vat = 0;
  for (const [i, l] of req.lines.entries()) {
    const n = i + 1;
    if (!l.description || !String(l.description).trim()) throw new RejectedError(`${n}. kalem açıklaması boş.`);
    if (num(l.quantity, `${n}. kalem miktarı`) <= 0) throw new RejectedError(`${n}. kalem miktarı > 0 olmalı.`);
    if (num(l.unitPrice, `${n}. kalem fiyatı`) < 0) throw new RejectedError(`${n}. kalem fiyatı negatif.`);
    num(l.discountAmount, `${n}. kalem indirimi`); num(l.vatRate, `${n}. kalem KDV oranı`);
    num(l.vatAmount, `${n}. kalem KDV tutarı`); num(l.netAmount, `${n}. kalem net tutarı`);
    const expectedNet = r2(r2(l.quantity * l.unitPrice) - l.discountAmount);
    if (Math.abs(expectedNet - l.netAmount) > 0.011) throw new RejectedError(`${n}. kalem net tutarı tutarsız.`);
    net += l.netAmount; vat += l.vatAmount;
  }
  if (Math.abs(r2(net) - num(req.netTotal, "netTotal")) > 0.011) throw new RejectedError("Kalem netleri toplamı netTotal ile uyuşmuyor.");
  if (Math.abs(r2(vat) - num(req.vatTotal, "vatTotal")) > 0.011) throw new RejectedError("Kalem KDV'leri toplamı vatTotal ile uyuşmuyor.");
  if (Math.abs(r2(net + vat) - num(req.grandTotal, "grandTotal")) > 0.011) throw new RejectedError("grandTotal = net + KDV olmalı.");
  if (req.grandTotal <= 0) throw new RejectedError("Fatura tutarı sıfırdan büyük olmalı.");
}

export const toPortalDate = (iso) => { const [y, m, d] = iso.split("-"); return `${d}/${m}/${y}`; };

export function istanbulTime(now = new Date()) {
  return new Intl.DateTimeFormat("en-GB", { timeZone: "Europe/Istanbul", hour12: false, hour: "2-digit", minute: "2-digit", second: "2-digit" }).format(now);
}

export function toFaturaDetails(req, now = new Date()) {
  validateRequest(req);
  const b = req.buyer;
  const isCompany = !!b.taxNumber;
  const name = String(b.name).trim();
  let first = "", last = "";
  if (!isCompany) { const i = name.lastIndexOf(" "); first = i > 0 ? name.slice(0, i) : name; last = i > 0 ? name.slice(i + 1) : ""; }
  const totalDiscount = r2(req.lines.reduce((a, l) => a + l.discountAmount, 0));
  return {
    // mlevent/fatura, bevren/gib-fatura-mcp ve buhari-earsiv-api: faturaTipi=SATIS, hangiTip=5000/30000.
    // f/fatura varsayılanları bunun tersidir (faturaTipi="5000/30000", hangiTip="Buyuk"); açıkça geçersiz kılınır.
    invoiceType: "SATIS",
    hangiTip: "5000/30000",
    date: toPortalDate(req.issueDate),
    time: istanbulTime(now),
    taxIDOrTRID: b.taxNumber || b.nationalId || GENERIC_CONSUMER_ID,
    taxOffice: b.taxOffice ?? "",
    title: isCompany ? name : "",
    name: first,
    surname: last,
    fullAddress: b.address ?? "",
    district: b.district ?? "",
    city: b.city ?? " ",
    email: b.email ?? "",
    // Yeniden eşleştirme için iç referansı sipariş numarası olarak taşır (faturada görünür).
    orderNumber: String(req.internalRef ?? "").slice(0, 30),
    items: req.lines.map((l) => ({
      name: String(l.description).trim(),
      quantity: l.quantity,
      unitType: UNIT_CODES[String(l.unit ?? "").toLowerCase()] ?? "C62",
      unitPrice: l.unitPrice,
      price: l.netAmount, // KDV hariç, indirim sonrası satır tutarı (DOĞRULANMADI: portal "fiyat" alanı)
      discountAmount: l.discountAmount,
      discountRate: l.quantity * l.unitPrice > 0 ? r2((l.discountAmount / (l.quantity * l.unitPrice)) * 100) : 0,
      VATRate: l.vatRate,
      VATAmount: l.vatAmount,
    })),
    totalDiscount,
    grandTotal: req.netTotal,           // KDV hariç toplam
    totalVAT: req.vatTotal,
    grandTotalInclVAT: req.grandTotal,
    paymentTotal: req.grandTotal,
  };
}
