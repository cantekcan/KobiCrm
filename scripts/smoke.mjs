// Uçtan uca duman testi: çalışan API'ye karşı gerçek iş akışını koşar.
//   node scripts/smoke.mjs                (API: http://localhost:5080, kimlik bilgileri .env'den)
//   API_URL=... ADMIN_USER=... ADMIN_PASSWORD=... node scripts/smoke.mjs
// DİKKAT: Kayıt oluşturur. Canlı veritabanında ÇALIŞTIRMAYIN; geliştirme/demo veritabanında kullanın.
import { readFileSync, existsSync } from "node:fs";

const env = {};
if (existsSync(".env")) for (const l of readFileSync(".env", "utf8").split(/\r?\n/)) { const m = l.match(/^([A-Z_]+)=(.*)$/); if (m) env[m[1]] = m[2]; }
const API = process.env.API_URL ?? `http://localhost:${env.API_PORT ?? 5080}`;
const USER = process.env.ADMIN_USER ?? env.ADMIN_USERNAME ?? "admin";
const PASS = process.env.ADMIN_PASSWORD ?? env.ADMIN_PASSWORD;
if (!PASS) { console.error("ADMIN_PASSWORD bulunamadı."); process.exit(2); }

let token = "";
async function call(method, path, body) {
  const res = await fetch(API + path, { method, headers: { "Content-Type": "application/json", ...(token ? { Authorization: `Bearer ${token}` } : {}) }, body: body ? JSON.stringify(body) : undefined });
  const text = await res.text();
  let json; try { json = text ? JSON.parse(text) : null; } catch { json = text; }
  return { status: res.status, body: json };
}
let failed = 0;
function check(name, cond, extra = "") { console.log(`${cond ? "  OK " : "  FAIL"} ${name}${extra ? "  → " + extra : ""}`); if (!cond) failed++; }
const stamp = Date.now().toString().slice(-6);
const today = new Date().toISOString().slice(0, 10);

const login = await call("POST", "/api/auth/login", { userName: USER, password: PASS });
check("giriş", login.status === 200); token = login.body.token;
check("yetkisiz istek reddedilir", (await fetch(API + "/api/customers")).status === 401);

const cust = await call("POST", "/api/customers", { name: `A Firması ${stamp}`, customerType: "Company", paymentTermDays: 30, eInvoiceStatus: "Unknown", openingBalance: { amount: 100000, date: "2026-01-01", description: "Geçmiş dönem devir" } });
check("müşteri + 100.000 TL açılış bakiyesi", cust.status === 201 && cust.body.balance === 100000, `bakiye=${cust.body.balance}`);
const cid = cust.body.id;
const dup = await call("POST", `/api/customers/${cid}/opening-balance`, { amount: 100000, date: "2026-01-01" });
check("açılış bakiyesi ikinci kez girilemez", dup.status === 409, dup.body?.title);

const prod = await call("POST", "/api/products", { code: `PEY${stamp}`, name: "Peynir", unit: "Kg", purchasePrice: 150, salePrice: 200, vatRate: 10, trackStock: true, minStock: 5 });
check("ürün", prod.status === 201); const pid = prod.body.id;
const badVat = await call("POST", "/api/products", { code: `X${stamp}`, name: "X", unit: "Kg", purchasePrice: 1, salePrice: 1, vatRate: 7, trackStock: false, minStock: 0 });
check("tanımsız KDV oranı reddedilir", badVat.status === 400);
await call("PUT", `/api/customers/${cid}/prices/${pid}`, { price: 190 });
const look = await call("GET", `/api/customers/${cid}/price-lookup/${pid}`);
check("müşteriye özel fiyat uygulanır", look.body.price === 190 && look.body.isCustomerPrice);

await call("POST", "/api/stock/adjust", { productId: pid, date: today, movementType: "Opening", quantityDelta: 500, note: "Açılış stoğu" });
const key = crypto.randomUUID();
const saleReq = { customerId: cid, saleDate: today, paymentType: "Credit", items: [{ productId: pid, quantity: 100, discountPercent: 0, discountAmount: 0 }], approve: true, clientRequestId: key };
const sale = await call("POST", "/api/sales", saleReq);
check("satış (100 kg × 190 + %10 KDV = 20.900)", sale.status === 201 && sale.body.grandTotal === 20900, `toplam=${sale.body.grandTotal} durum=${sale.body.status}`);
const again = await call("POST", "/api/sales", saleReq);
check("aynı istek tekrarlanınca mükerrer satış oluşmaz", again.status === 201 && again.body.id === sale.body.id);
let c = (await call("GET", `/api/customers/${cid}`)).body;
check("bakiye = 100.000 + 20.900", c.balance === 120900, `bakiye=${c.balance}`);

const pay = await call("POST", "/api/payments", { customerId: cid, paymentDate: today, amount: 25000, method: "BankTransfer", autoAllocate: false });
check("25.000 TL tahsilat", pay.status === 201, `dağıtılmamış=${pay.body.unallocatedAmount}`);
c = (await call("GET", `/api/customers/${cid}`)).body;
check("bakiye = 95.900", c.balance === 95900, `bakiye=${c.balance}`);

const st = (await call("GET", `/api/customers/${cid}/statement`)).body;
check("cari ekstre 3 hareket (açılış, satış, tahsilat)", st.entries.length === 3 && st.closingBalance === 95900, st.entries.map(e => e.entryType).join(","));

const m = (await call("GET", `/api/reports/measures?from=${today}&to=${today}&customerId=${cid}`)).body;
check("rapor: satış 20.900 (açılış satış sayılmaz), tahsilat 25.000, resmî fatura 0",
  m.commercialSales.amount === 20900 && m.collections.amount === 25000 && m.officialInvoices.amount === 0,
  `satış=${m.commercialSales.amount} tahsilat=${m.collections.amount} fatura=${m.officialInvoices.amount}`);

const inv = await call("POST", "/api/invoices/draft", { saleId: sale.body.id });
check("fatura taslağı", inv.status === 201 && inv.body.internalStatus === "Draft" && inv.body.officialStatus === "NotIssued");
const appr0 = await call("POST", `/api/invoices/${inv.body.id}/approve`);
check("eksik alıcı bilgisi/belge türü varsa iç onay reddedilir", appr0.status === 400);
const upd = await call("PUT", `/api/invoices/${inv.body.id}`, { invoiceDate: today, dueDate: null, documentType: "EArchive", buyerName: `A Firması ${stamp}`, buyerTaxNumber: "1234567891", buyerAddress: "Ankara", buyerCity: "Ankara", note: null, lines: [] });
check("geçersiz VKN reddedilir", upd.status === 400);
const c2 = await call("PUT", `/api/customers/${cid}`, { ...(await call("GET", `/api/customers/${cid}`)).body, taxNumber: null });
const upd2 = await call("PUT", `/api/invoices/${inv.body.id}`, { invoiceDate: today, dueDate: null, documentType: "EArchive", buyerName: `A Firması ${stamp}`, buyerNationalId: "10000000146", buyerAddress: "Ankara", buyerCity: "Ankara", note: null, lines: [] });
check("taslak düzenlenir", upd2.status === 200, upd2.body?.title);
const appr = await call("POST", `/api/invoices/${inv.body.id}/approve`);
check("iç onay: resmî belge OLUŞMAZ", appr.status === 200 && appr.body.internalStatus === "Approved" && appr.body.officialStatus === "NotIssued" && !appr.body.officialNumber);
c = (await call("GET", `/api/customers/${cid}`)).body;
check("fatura bakiyeyi ikinci kez artırmaz", c.balance === 95900, `bakiye=${c.balance}`);
const off = await call("POST", `/api/invoices/${inv.body.id}/official`, { number: `GIB${stamp}0000001`, issueDate: today, uuid: null, documentType: "EArchive" });
check("resmî numara kaydı: kaynak kullanıcı girişi, doğrulanmamış", off.status === 200 && off.body.officialStatus === "Issued" && off.body.officialSource === "ManualEntry" && off.body.officialVerified === false);

console.log(failed === 0 ? "\nTÜM KONTROLLER GEÇTİ" : `\n${failed} KONTROL BAŞARISIZ`);
process.exit(failed === 0 ? 0 : 1);
