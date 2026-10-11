import { test, beforeEach, afterEach } from "node:test";
import assert from "node:assert/strict";
import { createRequire } from "node:module";
import { createGateway } from "../src/gateway.mjs";
import { StateStore } from "../src/state.mjs";
import { toFaturaDetails, validateRequest } from "../src/mapper.mjs";

const require = createRequire(import.meta.url);
const { createFaturaClient } = require("../vendor/fatura/dist/index.js");

const TOKEN = "t".repeat(40);
const KEY = "0192b3c4-d5e6-7f80-9a1b-2c3d4e5f6a7b";

const validRequest = (over = {}) => ({
  invoiceId: "11111111-1111-1111-1111-111111111111", internalRef: "TSL-2026-000007", submissionKey: KEY, documentType: "EArchive", issueDate: "2026-10-12",
  buyer: { name: "Örnek Market Ltd.", taxOffice: "Çankaya", taxNumber: "1234567890", nationalId: null, address: "Örnek Mah.", city: "Ankara", district: "Çankaya", email: null },
  lines: [{ description: "Beyaz peynir", quantity: 10, unit: "Kg", unitPrice: 200, discountAmount: 100, vatRate: 10, vatAmount: 190, netAmount: 1900 }],
  netTotal: 1900, vatTotal: 190, grandTotal: 2090, note: null, ...over,
});

/** Portalın davranışını taklit eden sahte istemci (kütüphane yerine). */
function fakeClient(over = {}) {
  const calls = { login: 0, create: 0, sms: 0, verify: 0, logout: 0 };
  const items = new Map();
  const c = {
    calls, items,
    async getToken() { calls.login++; return "tok"; },
    async logout() { calls.logout++; },
    async createDraftInvoice(_t, d) { calls.create++; const ettn = crypto.randomUUID(); items.set(ettn, { ettn, belgeNumarasi: "", onayDurumu: "Onaylanmadı", belgeTarihi: d.date.split("/").reverse().join("-"), _details: d }); return { uuid: ettn, date: d.date }; },
    async findInvoice(_t, { uuid }) { return items.get(uuid); },
    async getSignPhoneNumber() { return "05321234567"; },
    async sendSignSMSCode() { calls.sms++; return "oid-1"; },
    async verifySignSMSCode(_t, code, _oid, list) { calls.verify++; if (code !== "123456") return false; for (const i of list) { const it = items.get(i.ettn); it.onayDurumu = "Onaylandı"; it.belgeNumarasi = "ABC2026000000001"; } return true; },
    async getInvoiceHTML() { return "<html>fatura</html>"; },
    async runCommand(_t, cmd, page, jp) { calls.cancel = (calls.cancel ?? 0) + 1; calls.lastCancel = { cmd, page, jp }; return { data: "İptal talebiniz başarıyla oluşturulmuştur." }; },
    ...over,
  };
  return c;
}

let srv, base, client;
async function start(over = {}, opts = {}) {
  client = fakeClient(over);
  const gw = createGateway({ client, state: new StateStore(null), user: "u", password: "p", token: TOKEN, env: "TEST", ...opts });
  await new Promise((r) => gw.server.listen(0, "127.0.0.1", r));
  srv = gw.server; base = `http://127.0.0.1:${srv.address().port}`;
}
afterEach(() => new Promise((r) => (srv ? srv.close(r) : r())));
const call = async (method, path, body, token = TOKEN) => {
  const res = await fetch(base + path, { method, headers: { "Content-Type": "application/json", ...(token ? { Authorization: `Bearer ${token}` } : {}) }, body: body ? JSON.stringify(body) : undefined });
  return { status: res.status, body: await res.json() };
};

test("kimlik doğrulaması: token yoksa/yanlışsa 401, /health açık ve kimlik bilgisi sızdırmaz", async () => {
  await start();
  assert.equal((await call("POST", "/v1/drafts", validRequest(), null)).status, 401);
  assert.equal((await call("POST", "/v1/drafts", validRequest(), "x".repeat(40))).status, 401);
  const h = await call("GET", "/health", null, null);
  assert.equal(h.status, 200);
  assert.deepEqual(Object.keys(h.body).sort(), ["credentials", "env", "status"]);
  assert.equal(client.calls.login, 0);
});

test("taslak: portalda oluşur, aynı submissionKey ikinci kez yeni taslak YARATMAZ", async () => {
  await start();
  const a = await call("POST", "/v1/drafts", validRequest());
  assert.equal(a.status, 200); assert.equal(a.body.outcome, "success"); assert.ok(a.body.draftId);
  const b = await call("POST", "/v1/drafts", validRequest());
  assert.equal(b.status, 200); assert.equal(b.body.draftId, a.body.draftId);
  assert.equal(client.calls.create, 1);
  assert.match(a.body.message, /resmî belge değil/);
});

test("ağ hatası: sonuç 'temporary', yeniden gönderim mükerrer taslak oluşturmaz", async () => {
  await start({ async createDraftInvoice() { this.calls.create++; throw new TypeError("fetch failed"); } });
  const first = await call("POST", "/v1/drafts", validRequest());
  assert.equal(first.status, 502); assert.equal(first.body.outcome, "temporary");
  const retry = await call("POST", "/v1/drafts", validRequest());
  assert.equal(retry.status, 409); assert.equal(retry.body.outcome, "temporary");
  assert.equal(client.calls.create, 1, "ikinci deneme portala gitmemeli");
  const st = await call("GET", `/v1/status/${KEY}`);
  assert.equal(st.status, 502); assert.equal(st.body.outcome, "temporary"); // sonuç belirsiz: başarı/başarısızlık uydurulmaz
});

test("portalın açık reddi: 'rejected', durum temizlenir ve yeniden denenebilir", async () => {
  let n = 0;
  await start({ async createDraftInvoice(t, d) { n++; if (n === 1) throw new Error("Ettn ya eksik ya boş"); return fakeClient().createDraftInvoice.call(this, t, d); } });
  const r1 = await call("POST", "/v1/drafts", validRequest());
  assert.equal(r1.status, 422); assert.equal(r1.body.outcome, "rejected");
  const r2 = await call("POST", "/v1/drafts", validRequest());
  assert.equal(r2.status, 200);
});

test("sessiz başarısızlık: portal 'tamam' deyip taslak bulunamazsa başarı bildirilmez", async () => {
  await start({ async createDraftInvoice() { return { uuid: "", date: "12/10/2026" }; } });
  const r = await call("POST", "/v1/drafts", validRequest());
  assert.equal(r.status, 422); assert.equal(r.body.outcome, "rejected"); assert.match(r.body.message, /OLUŞMADI/);
  assert.equal((await call("POST", "/v1/drafts", validRequest())).status, 422); // pending bırakılmadı
});

test("kimlik bilgisi tanımlı değilse işlem yapılmaz", async () => {
  client = fakeClient();
  const gw = createGateway({ client, state: new StateStore(null), user: "", password: "", token: TOKEN });
  await new Promise((r) => gw.server.listen(0, "127.0.0.1", r)); srv = gw.server; base = `http://127.0.0.1:${srv.address().port}`;
  const r = await call("POST", "/v1/drafts", validRequest());
  assert.equal(r.status, 422); assert.match(r.body.message, /kimlik bilgileri/);
  assert.equal(client.calls.create, 0);
});

test("doğrulama: e-Fatura, tutarsız toplam, geçersiz kimlik ve boş kalem REDDEDİLİR; portala çağrı yapılmaz", async () => {
  await start();
  const bad = [
    validRequest({ documentType: "EInvoice" }),
    validRequest({ netTotal: 1800 }),
    validRequest({ grandTotal: 2000 }),
    validRequest({ buyer: { name: "X", taxNumber: "123" } }),
    validRequest({ lines: [] }),
    validRequest({ issueDate: "12.10.2026" }),
    validRequest({ submissionKey: "yok" }),
  ];
  for (const b of bad) assert.equal((await call("POST", "/v1/drafts", b)).status, 422);
  assert.equal(client.calls.login, 0);
});

test("SMS ile imzalama: challenge → doğru kod → belge numarası portal listesinden doğrulanır", async () => {
  await start();
  await call("POST", "/v1/drafts", validRequest());
  const ch = await call("POST", "/v1/issue/challenge", { submissionKey: KEY });
  assert.equal(ch.status, 200); assert.match(ch.body.maskedPhone, /\*/); assert.doesNotMatch(ch.body.maskedPhone, /05321234567/);
  const bad = await call("POST", "/v1/issue", { submissionKey: KEY, smsCode: "000000" });
  assert.equal(bad.status, 422);
  assert.equal((await call("GET", `/v1/status/${KEY}`)).body.state, "Draft");
  const ok = await call("POST", "/v1/issue", { submissionKey: KEY, smsCode: "123456" });
  assert.equal(ok.status, 200); assert.equal(ok.body.officialNumber, "ABC2026000000001");
  assert.equal((await call("GET", `/v1/status/${KEY}`)).body.state, "Issued");
  // ikinci imza denemesi tekrar imzalamaz
  const again = await call("POST", "/v1/issue", { submissionKey: KEY, smsCode: "123456" });
  assert.equal(again.status, 200); assert.equal(client.calls.verify, 2);
});

test("imza: SMS kodu yoksa/oturum yoksa reddedilir; kod biçimi doğrulanır", async () => {
  await start();
  await call("POST", "/v1/drafts", validRequest());
  assert.equal((await call("POST", "/v1/issue", { submissionKey: KEY, smsCode: "123456" })).status, 422); // challenge yok
  await call("POST", "/v1/issue/challenge", { submissionKey: KEY });
  assert.equal((await call("POST", "/v1/issue", { submissionKey: KEY, smsCode: "abc" })).status, 422);
  assert.equal(client.calls.verify, 0);
});

test("imza: portal imzalandı dese de belge onaylı görünmüyorsa başarı bildirilmez", async () => {
  await start({ async verifySignSMSCode() { return true; } }); // hiçbir şeyi onaylamaz
  await call("POST", "/v1/drafts", validRequest());
  await call("POST", "/v1/issue/challenge", { submissionKey: KEY });
  const r = await call("POST", "/v1/issue", { submissionKey: KEY, smsCode: "123456" });
  assert.equal(r.status, 502); assert.equal(r.body.outcome, "temporary");
  assert.notEqual((await call("GET", `/v1/status/${KEY}`)).body.state, "Issued");
});

test("durum: taslak portalda yoksa 'notfound'; bilinmeyen anahtar 404; iptal ucu yok", async () => {
  await start();
  await call("POST", "/v1/drafts", validRequest());
  client.items.clear();
  assert.equal((await call("GET", `/v1/status/${KEY}`)).status, 404);
  assert.equal((await call("GET", "/v1/status/0192b3c4-d5e6-7f80-9a1b-ffffffffffff")).status, 404);
  assert.equal((await call("POST", "/v1/nope", { submissionKey: KEY })).status, 404);
});

test("belge: HTML base64 döner", async () => {
  await start();
  await call("POST", "/v1/drafts", validRequest());
  const d = await call("GET", `/v1/document/${KEY}`);
  assert.equal(Buffer.from(d.body.contentBase64, "base64").toString(), "<html>fatura</html>");
});

test("yanıtlarda kimlik bilgisi/token yok", async () => {
  await start();
  const all = JSON.stringify([await call("POST", "/v1/drafts", validRequest()), await call("GET", `/v1/status/${KEY}`)]);
  assert.doesNotMatch(all, /"p"|tok|GATEWAY/);
});

// ───────────── Eşleme ─────────────
test("eşleme: tarih, birim kodu, şahıs ad/soyad ve tutarlar f/fatura biçimine çevrilir", () => {
  const d = toFaturaDetails(validRequest({ buyer: { name: "Ayşe Nur Yılmaz", nationalId: "10000000146", address: "Adres" } }), new Date("2026-10-12T06:07:08Z"));
  assert.equal(d.date, "12/10/2026");
  assert.equal(d.time, "09:07:08");                                // Europe/Istanbul
  assert.equal(d.taxIDOrTRID, "10000000146");
  assert.equal(d.title, ""); assert.equal(d.name, "Ayşe Nur"); assert.equal(d.surname, "Yılmaz");
  assert.equal(d.items[0].unitType, "KGM"); assert.equal(d.items[0].price, 1900); assert.equal(d.items[0].discountAmount, 100);
  assert.equal(d.items[0].discountRate, 5);
  assert.equal(d.grandTotal, 1900); assert.equal(d.totalVAT, 190); assert.equal(d.grandTotalInclVAT, 2090); assert.equal(d.paymentTotal, 2090);
  assert.equal(d.orderNumber, "TSL-2026-000007");
  assert.equal(d.invoiceType, "SATIS"); assert.equal(d.hangiTip, "5000/30000");
  assert.equal(toFaturaDetails(validRequest()).items[0].unitType, "KGM");
  assert.equal(toFaturaDetails(validRequest({ lines: [{ ...validRequest().lines[0], unit: "Koli" }] })).items[0].unitType, "C62");
  assert.throws(() => validateRequest(validRequest({ documentType: "EInvoice" })), /e-Arşiv/);
});

// ───────────── Gerçek f/fatura kütüphanesi + sahte GİB (fetch) ─────────────
test("entegrasyon: vendored f/fatura ile sahte portal — taslak oluşur, istek gövdesi doğru alanları taşır", async () => {
  const realFetch = globalThis.fetch;
  const drafts = []; const seen = [];
  globalThis.fetch = async (url, init) => {
    if (!String(url).startsWith("https://earsivportaltest.efatura.gov.tr")) return realFetch(url, init); // yerel ağ geçidine giden istekler
    const body = new URLSearchParams(init.body);
    seen.push({ url: String(url), cmd: body.get("cmd") ?? body.get("assoscmd") });
    const json = (o) => ({ ok: true, status: 200, json: async () => o });
    if (String(url).endsWith("/assos-login")) return json({ token: "portal-token" });
    const cmd = body.get("cmd");
    if (cmd === "EARSIV_PORTAL_TASLAKLARI_GETIR") return json({ data: drafts.map((d) => ({ ...d })) });
    if (cmd === "EARSIV_PORTAL_FATURA_OLUSTUR") {
      const jp = JSON.parse(body.get("jp")); seen.push({ jp });
      drafts.push({ ettn: "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee", belgeNumarasi: "", onayDurumu: "Onaylanmadı", belgeTarihi: jp.faturaTarihi.split("/").reverse().join("-") });
      return json({ data: "Faturanız başarıyla oluşturulmuştur. Düzenlenen Belgeler menüsünden faturanıza ulaşabilirsiniz." });
    }
    return json({ data: {}, error: "1", messages: [{ type: "error", text: "beklenmeyen komut " + cmd }] });
  };
  try {
    client = createFaturaClient("TEST");
    const gw = createGateway({ client, state: new StateStore(null), user: "u", password: "p", token: TOKEN });
    await new Promise((r) => gw.server.listen(0, "127.0.0.1", r)); srv = gw.server; base = `http://127.0.0.1:${srv.address().port}`;
    const r = await call("POST", "/v1/drafts", validRequest());
    assert.equal(r.status, 200, JSON.stringify(r.body));
    assert.equal(r.body.draftId, "aaaaaaaa-bbbb-cccc-dddd-eeeeeeeeeeee");
    const jp = seen.find((s) => s.jp).jp;
    assert.equal(jp.vknTckn, "1234567890"); assert.equal(jp.aliciUnvan, "Örnek Market Ltd."); assert.equal(jp.faturaTarihi, "12/10/2026");
    assert.equal(jp.malHizmetTable[0].birim, "KGM"); assert.equal(jp.vergilerDahilToplamTutar, "2090.00"); assert.equal(jp.hesaplanankdv, "190.00");
    assert.equal(jp.siparisNumarasi, "TSL-2026-000007");
    assert.ok(seen.every((s) => !s.url || s.url.startsWith("https://earsivportaltest.efatura.gov.tr")), "yalnızca TEST portalı");
  } finally { globalThis.fetch = realFetch; }
});

test("özel karakterli parola GİB'e doğru kodlanarak gider (kütüphane yaması)", async () => {
  const realFetch = globalThis.fetch; let body;
  globalThis.fetch = async (url, init) => { body = String(init.body); return { ok: true, status: 200, json: async () => ({ token: "t" }) }; };
  try {
    await createFaturaClient("TEST").getToken("kullanici", "p&ss+w%rd=1 ü");
    const p = new URLSearchParams(body);
    assert.equal(p.get("sifre"), "p&ss+w%rd=1 ü"); assert.equal(p.get("sifre2"), "p&ss+w%rd=1 ü"); assert.equal(p.get("userid"), "kullanici");
  } finally { globalThis.fetch = realFetch; }
});

test("portal JSON olmayan (503/HTML) yanıt verirse sonuç BELİRSİZ sayılır, mükerrer taslak oluşmaz", async () => {
  await start({ async createDraftInvoice() { this.calls.create++; throw new SyntaxError("Unexpected token <"); } });
  const r1 = await call("POST", "/v1/drafts", validRequest());
  assert.equal(r1.status, 502); assert.equal(r1.body.outcome, "temporary");
  assert.equal((await call("POST", "/v1/drafts", validRequest())).status, 409);
  assert.equal(client.calls.create, 1);
});

async function issued(over) {
  await start(over);
  await call("POST", "/v1/drafts", validRequest());
  await call("POST", "/v1/issue/challenge", { submissionKey: KEY });
  assert.equal((await call("POST", "/v1/issue", { submissionKey: KEY, smsCode: "123456" })).status, 200);
}

test("iptal talebi: yalnızca imzalı belge için; doğru komut/alanlar; tekrar gönderim yeni talep açmaz; sonuç 'iptal edildi' diye raporlanmaz", async () => {
  await start();
  await call("POST", "/v1/drafts", validRequest());
  assert.equal((await call("POST", "/v1/cancel", { submissionKey: KEY, reason: "Hatalı tutar" })).status, 422); // henüz imzalı değil
  await call("POST", "/v1/issue/challenge", { submissionKey: KEY });
  await call("POST", "/v1/issue", { submissionKey: KEY, smsCode: "123456" });
  assert.equal((await call("POST", "/v1/cancel", { submissionKey: KEY, reason: "x" })).status, 422);          // neden çok kısa
  const r = await call("POST", "/v1/cancel", { submissionKey: KEY, reason: "Hatalı tutar" });
  assert.equal(r.status, 200); assert.equal(r.body.outcome, "success");
  const lc = client.calls.lastCancel;
  assert.equal(lc.cmd, "EARSIV_PORTAL_IPTAL_TALEBI_OLUSTUR"); assert.equal(lc.page, "RG_TASLAKLAR");
  assert.deepEqual([lc.jp.belgeTuru, lc.jp.onayDurumu, lc.jp.talepAciklama], ["FATURA", "Onaylandı", "Hatalı tutar"]);
  assert.equal((await call("POST", "/v1/cancel", { submissionKey: KEY, reason: "Hatalı tutar" })).status, 200);
  assert.equal(client.calls.cancel, 1, "ikinci çağrı portala gitmemeli");
  assert.equal((await call("GET", `/v1/status/${KEY}`)).body.state, "Issued"); // durum iptal edildi demez
});

test("iptal talebi: portal beklenen başarı iletisini vermezse başarı bildirilmez ve yeniden gönderim engellenir", async () => {
  await issued({ async runCommand() { this.calls.cancel = (this.calls.cancel ?? 0) + 1; return { data: "Beklenmeyen ileti" }; } });
  const r = await call("POST", "/v1/cancel", { submissionKey: KEY, reason: "Hatalı tutar" });
  assert.equal(r.status, 502); assert.equal(r.body.outcome, "temporary");
  assert.equal((await call("POST", "/v1/cancel", { submissionKey: KEY, reason: "Hatalı tutar" })).status, 409);
  assert.equal(client.calls.cancel, 1);
});

test("iptal talebi: ağ hatasında sonuç belirsiz, mükerrer talep gitmez", async () => {
  await issued({ async runCommand() { this.calls.cancel = (this.calls.cancel ?? 0) + 1; throw new TypeError("fetch failed"); } });
  assert.equal((await call("POST", "/v1/cancel", { submissionKey: KEY, reason: "Hatalı tutar" })).status, 502);
  assert.equal((await call("POST", "/v1/cancel", { submissionKey: KEY, reason: "Hatalı tutar" })).status, 409);
  assert.equal(client.calls.cancel, 1);
});

test("TEST ortamında liste tipi 'Buyuk', canlıda '5000/30000' (vendored yama)", async () => {
  const realFetch = globalThis.fetch; const types = [];
  globalThis.fetch = async (url, init) => { const b = new URLSearchParams(init.body); const jp = b.get("jp"); if (jp) types.push(JSON.parse(jp).hangiTip); return { ok: true, status: 200, json: async () => ({ data: [], token: "t" }) }; };
  try {
    await createFaturaClient("TEST").getAllInvoicesByDateRange("t", { startDate: "01/01/2026", endDate: "01/01/2026" });
    await createFaturaClient("PROD").getAllInvoicesByDateRange("t", { startDate: "01/01/2026", endDate: "01/01/2026" });
    assert.deepEqual(types, ["Buyuk", "5000/30000"]);
  } finally { globalThis.fetch = realFetch; }
});
