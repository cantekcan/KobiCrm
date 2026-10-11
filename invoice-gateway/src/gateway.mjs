// HTTP ağ geçidi: CRM (.NET) ↔ f/fatura ↔ GİB e-Arşiv portalı.
// Tasarım ilkeleri:
//  • Yalnızca gerçekten desteklenen işlemler vardır (taslak, SMS ile imzalama, durum, HTML, iptal TALEBİ). İptalin sonuçlandığı doğrulanamaz (durum ucu bunu raporlamaz).
//  • Her gönderim submissionKey ile durum dosyasında izlenir; ağ hatasında aynı anahtar yeni taslak oluşturmaz.
//  • Sonuç belirsizse "temporary" döner (asla uydurma başarı); portal "başarılı" dese de doğrulanamıyorsa başarı bildirilmez.
//  • GİB kimlik bilgileri yalnızca bu süreçte, ortam değişkenlerinden okunur; hiçbir yanıtta/günlükte yer almaz.
import { createServer } from "node:http";
import { timingSafeEqual } from "node:crypto";
import { RejectedError, toFaturaDetails, toPortalDate } from "./mapper.mjs";

const MAX_BODY = 1_000_000;
const CHALLENGE_TTL_MS = 10 * 60_000;

/** Ağ/zaman aşımı gibi "sonuç belirsiz" hataları ile portalın açık reddini ayırır. */
export function isIndeterminate(err) {
  if (err instanceof RejectedError) return false;
  if (err?.name === "AbortError" || err?.name === "TimeoutError") return true;
  if (err instanceof TypeError) return true; // fetch failed
  if (err instanceof SyntaxError) return true; // JSON olmayan yanıt (503/proxy HTML): istek işlendi mi bilinmez
  if (err?.cause && typeof err.cause === "object") return true;
  return false;
}

class Mutex {
  #p = Promise.resolve();
  run(fn) { const r = this.#p.then(fn, fn); this.#p = r.catch(() => {}); return r; }
}

const mask = (p) => (p ? String(p).replace(/\d(?=\d{2})/g, "*") : null);

export function createGateway({ client, state, user, password, token: gatewayToken, env = "TEST", now = () => Date.now(), log = () => {} }) {
  const mutex = new Mutex();
  const challenges = new Map(); // submissionKey → { token, oid, expires }

  const send = (res, status, body) => {
    const text = JSON.stringify(body);
    res.writeHead(status, { "Content-Type": "application/json; charset=utf-8", "Cache-Control": "no-store", "X-Content-Type-Options": "nosniff" });
    res.end(text);
  };
  const authorized = (req) => {
    const h = req.headers.authorization ?? "";
    if (!h.startsWith("Bearer ")) return false;
    const a = Buffer.from(h.slice(7)), b = Buffer.from(gatewayToken);
    return a.length === b.length && timingSafeEqual(a, b);
  };
  const readJson = (req) => new Promise((resolve, reject) => {
    const chunks = []; let size = 0;
    req.on("data", (c) => { size += c.length; if (size > MAX_BODY) { reject(new RejectedError("İstek çok büyük.")); req.destroy(); } else chunks.push(c); });
    req.on("end", () => { try { resolve(chunks.length ? JSON.parse(Buffer.concat(chunks).toString("utf8")) : {}); } catch { reject(new RejectedError("Geçersiz JSON.")); } });
    req.on("error", reject);
  });

  async function login() {
    if (!user || !password) throw new RejectedError("GİB kimlik bilgileri ağ geçidinde tanımlı değil (GIB_USER / GIB_PASSWORD).");
    return client.getToken(user, password);
  }
  const safeLogout = async (t) => { try { await client.logout(t); } catch { /* yoksay */ } };
  const keyOf = (v) => { if (typeof v !== "string" || !/^[0-9a-fA-F-]{36}$/.test(v)) throw new RejectedError("submissionKey (GUID) zorunlu."); return v.toLowerCase(); };

  // ─────────── POST /v1/drafts ───────────
  async function createDraft(body) {
    const key = keyOf(body.submissionKey);
    const existing = state.get(key);
    if (existing?.ettn) return [200, { outcome: "success", draftId: existing.ettn, uuid: existing.ettn, message: "Bu gönderim için taslak zaten var (mükerrer oluşturulmadı)." }];
    if (existing?.status === "pending")
      return [409, { outcome: "temporary", message: "Bu gönderimin önceki denemesinin sonucu belirsiz. Yeniden göndermeyin; portalın taslak listesini kontrol edin / durum sorgulayın." }];

    const details = toFaturaDetails(body); // doğrulama burada; geçersizse RejectedError
    state.set(key, { status: "pending", internalRef: body.internalRef, date: details.date, startedAt: new Date(now()).toISOString() });
    let token;
    try {
      token = await login();
      const draft = await client.createDraftInvoice(token, details);
      if (!draft?.uuid) {
        // Kütüphane/portal "başarılı" dese de taslak listede bulunamadı → başarı iddia edilmez.
        state.delete(key);
        throw new RejectedError("Portal taslağı oluşturduğunu bildirdi ancak taslak listesinde bulunamadı. Portal arayüzü değişmiş olabilir; belge OLUŞMADI kabul edilir.");
      }
      state.set(key, { status: "draft", ettn: draft.uuid, date: details.date });
      return [200, { outcome: "success", draftId: draft.uuid, uuid: draft.uuid, message: "Portalda taslak oluşturuldu (resmî belge değil)." }];
    } catch (e) {
      if (isIndeterminate(e)) throw e; // pending kalır → mükerrer engellenir
      if (state.get(key)?.status === "pending") state.delete(key);
      throw e;
    } finally { if (token) await safeLogout(token); }
  }

  // ─────────── POST /v1/issue/challenge ───────────
  async function issueChallenge(body) {
    const key = keyOf(body.submissionKey);
    const rec = state.get(key);
    if (!rec?.ettn) throw new RejectedError("Bu gönderim için portal taslağı bilinmiyor.");
    if (rec.status === "issued") throw new RejectedError("Belge zaten imzalanmış.");
    const token = await login();
    try {
      const phone = await client.getSignPhoneNumber(token);
      if (!phone) throw new RejectedError("Portalda imza SMS'i için kayıtlı telefon bulunamadı.");
      const oid = await client.sendSignSMSCode(token, phone);
      if (!oid) throw new RejectedError("Portal SMS işlemini başlatmadı (oid alınamadı).");
      challenges.set(key, { token, oid, expires: now() + CHALLENGE_TTL_MS });
      return [200, { outcome: "success", maskedPhone: mask(phone), message: "SMS kodu portala kayıtlı telefona gönderildi." }];
    } catch (e) { await safeLogout(token); throw e; }
  }

  // ─────────── POST /v1/issue ───────────  (hukuki sonuç doğurur)
  async function issue(body) {
    const key = keyOf(body.submissionKey);
    const rec = state.get(key);
    if (!rec?.ettn) throw new RejectedError("Bu gönderim için portal taslağı bilinmiyor.");
    if (rec.status === "issued") return [200, { outcome: "success", officialNumber: rec.number, uuid: rec.ettn, issueDate: rec.issueDate, message: "Belge zaten imzalanmış." }];
    if (typeof body.smsCode !== "string" || !/^\d{4,8}$/.test(body.smsCode)) throw new RejectedError("SMS kodu 4–8 haneli rakam olmalı.");
    const ch = challenges.get(key);
    if (!ch || ch.expires < now()) { challenges.delete(key); throw new RejectedError("SMS doğrulama oturumu yok ya da süresi doldu; önce SMS kodu isteyin."); }

    try {
      const item = await client.findInvoice(ch.token, { date: rec.date, uuid: rec.ettn });
      if (!item) throw new RejectedError("Portalda bu taslak bulunamadı.");
      state.set(key, { status: "signing" }); // imzalama denendi: sonuç belirsiz olabilir
      const signed = await client.verifySignSMSCode(ch.token, body.smsCode, ch.oid, [item]);
      if (!signed) { state.set(key, { status: "draft" }); throw new RejectedError("Portal imzalamayı onaylamadı (SMS kodu hatalı olabilir). Belge düzenlenmedi."); }
      challenges.delete(key);
      // Başarıyı portal yanıtından değil, belgenin onaylı göründüğünden doğrula.
      const after = await client.findInvoice(ch.token, { date: rec.date, uuid: rec.ettn });
      if (after?.onayDurumu === "Onaylandı" && after.belgeNumarasi) {
        const issueDate = after.belgeTarihi ? after.belgeTarihi.split("-").reverse().join("-") : null;
        state.set(key, { status: "issued", number: after.belgeNumarasi, issueDate });
        return [200, { outcome: "success", officialNumber: after.belgeNumarasi, uuid: rec.ettn, issueDate, message: "Belge imzalandı." }];
      }
      return [502, { outcome: "temporary", message: "İmzalama isteği kabul edildi ancak belgenin onaylandığı doğrulanamadı. Yeniden denemeyin; durum sorgulayın." }];
    } catch (e) {
      if (isIndeterminate(e)) state.set(key, { status: "signing" });
      throw e;
    } finally { const c = challenges.get(key); if (!c) await safeLogout(ch.token); }
  }

  // ─────────── POST /v1/cancel ───────────
  // Portalın resmî "İptal Talebi Oluştur" komutu (mlevent/fatura, bilaleren/e-fatura, bevren/gib-fatura-mcp ortak komutu).
  // Bu bir TALEP oluşturur; belgenin iptal EDİLDİĞİNİ göstermez ve gateway bunu iddia etmez.
  const CANCEL_OK = /İptal talebiniz başarıyla oluşturulmuş/i;
  async function cancelRequest(body) {
    const key = keyOf(body.submissionKey);
    const rec = state.get(key);
    if (!rec?.ettn || rec.status !== "issued") throw new RejectedError("İptal talebi yalnızca imzalanmış (resmî) belge için oluşturulabilir.");
    const reason = typeof body.reason === "string" ? body.reason.trim() : "";
    if (reason.length < 3 || reason.length > 500) throw new RejectedError("İptal nedeni 3–500 karakter olmalı.");
    if (rec.cancel === "requested") return [200, { outcome: "success", message: "Bu belge için iptal talebi zaten oluşturulmuş (tekrar gönderilmedi). Sonucu portalda doğrulayın." }];
    if (rec.cancel === "pending") return [409, { outcome: "temporary", message: "Önceki iptal talebi denemesinin sonucu belirsiz. Yeniden göndermeyin; portalda İptal/İtiraz Talepleri ekranını kontrol edin." }];
    state.set(key, { cancel: "pending" });
    const token = await login();
    try {
      const res = await client.runCommand(token, "EARSIV_PORTAL_IPTAL_TALEBI_OLUSTUR", "RG_TASLAKLAR", { ettn: rec.ettn, onayDurumu: "Onaylandı", belgeTuru: "FATURA", talepAciklama: reason });
      const text = typeof res?.data === "string" ? res.data : "";
      if (CANCEL_OK.test(text)) { state.set(key, { cancel: "requested" }); return [200, { outcome: "success", message: text }]; }
      // Beklenen başarı iletisi yok: sonuç doğrulanamadı → başarı iddia edilmez, yeniden gönderim engellenir.
      return [502, { outcome: "temporary", message: "Portal yanıtı iptal talebinin oluştuğunu doğrulamadı: " + (text || "boş yanıt").slice(0, 200) + " — Portalda kontrol edin." }];
    } catch (e) {
      if (isIndeterminate(e)) throw e; // pending kalır
      state.set(key, { cancel: undefined });
      throw e;
    } finally { await safeLogout(token); }
  }

  // ─────────── GET /v1/status/:key ───────────
  async function status(key) {
    key = keyOf(key);
    const rec = state.get(key);
    if (!rec) return [404, { outcome: "notfound", message: "Bu gönderim ağ geçidinde kayıtlı değil." }];
    if (!rec.ettn) return [502, { outcome: "temporary", message: "Taslak oluşturma denemesinin sonucu bilinmiyor. Portalın taslak listesinde sipariş no alanında iç referansı arayın; yoksa belge oluşmamıştır." }];
    const token = await login();
    try {
      const item = await client.findInvoice(token, { date: rec.date, uuid: rec.ettn });
      if (!item) return [404, { outcome: "notfound", message: "Portalda bu taslak/belge bulunamadı." }];
      if (item.onayDurumu === "Onaylandı" && item.belgeNumarasi) {
        const issueDate = item.belgeTarihi ? item.belgeTarihi.split("-").reverse().join("-") : null;
        state.set(key, { status: "issued", number: item.belgeNumarasi, issueDate });
        return [200, { outcome: "success", state: "Issued", officialNumber: item.belgeNumarasi, uuid: rec.ettn, issueDate }];
      }
      return [200, { outcome: "success", state: "Draft", uuid: rec.ettn }];
    } finally { await safeLogout(token); }
  }

  // ─────────── GET /v1/document/:key ───────────
  async function document(key) {
    key = keyOf(key);
    const rec = state.get(key);
    if (!rec?.ettn) return [404, { outcome: "notfound", message: "Belge bilinmiyor." }];
    const token = await login();
    try {
      const html = await client.getInvoiceHTML(token, rec.ettn, { signed: rec.status === "issued" });
      return [200, { outcome: "success", contentType: "text/html", fileName: `${rec.ettn}.html`, contentBase64: Buffer.from(html, "utf8").toString("base64") }];
    } finally { await safeLogout(token); }
  }

  async function route(req, res) {
    const url = new URL(req.url, "http://localhost");
    if (req.method === "GET" && url.pathname === "/health") return send(res, 200, { status: "ok", env, credentials: !!(user && password) });
    if (!authorized(req)) return send(res, 401, { outcome: "rejected", message: "Yetkisiz." });

    const [method, path] = [req.method, url.pathname];
    const body = method === "POST" ? await readJson(req) : null;
    const run = (fn) => mutex.run(fn).then(([s, b]) => send(res, s, b));
    if (method === "POST" && path === "/v1/drafts") return run(() => createDraft(body));
    if (method === "POST" && path === "/v1/issue/challenge") return run(() => issueChallenge(body));
    if (method === "POST" && path === "/v1/issue") return run(() => issue(body));
    if (method === "POST" && path === "/v1/cancel") return run(() => cancelRequest(body));
    let m;
    if (method === "GET" && (m = path.match(/^\/v1\/status\/([^/]+)$/))) return run(() => status(m[1]));
    if (method === "GET" && (m = path.match(/^\/v1\/document\/([^/]+)$/))) return run(() => document(m[1]));
    return send(res, 404, { outcome: "notfound", message: "Bilinmeyen uç nokta. Bilinmeyen uç nokta." });
  }

  const server = createServer((req, res) => {
    route(req, res).catch((e) => {
      if (res.headersSent) return;
      if (e instanceof RejectedError) return send(res, 422, { outcome: "rejected", message: e.message });
      if (isIndeterminate(e)) { log("indeterminate", e?.message); return send(res, 502, { outcome: "temporary", message: "Portala ulaşılamadı veya yanıt alınamadı; sonuç belirsiz." }); }
      // Portalın açık reddi (kütüphane Error'u): ileti kullanıcıya gösterilebilir (kimlik bilgisi içermez)
      log("rejected", e?.message);
      return send(res, 422, { outcome: "rejected", message: String(e?.message ?? "Portal isteği reddetti.").slice(0, 500) });
    });
  });
  return { server, createDraft, issueChallenge, issue, cancelRequest, status, document };
}
