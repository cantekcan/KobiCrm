// CRM ↔ GERÇEK ağ geçidi HTTP sözleşme testi için yardımcı süreç.
// Gerçek gateway + vendored f/fatura çalışır; GİB yerine YEREL SAHTE PORTAL kullanılır (fetch yakalanır).
// Gerçek ağa ÇIKMAZ: earsivportaltest dışındaki hiçbir adres sahte portala yönlendirilmez, dış adres çağrısı hata verir.
// Bu bir mock'tur: dönen numara/ETTN gerçek GİB belgesi değildir, imzalı fatura kesildiğini KANITLAMAZ.
import { createRequire } from "node:module";
import { createGateway } from "../src/gateway.mjs";
import { StateStore } from "../src/state.mjs";

const require = createRequire(import.meta.url);
const { createFaturaClient } = require("../vendor/fatura/dist/index.js");
const PORTAL = "https://earsivportaltest.efatura.gov.tr";
const mode = process.env.FAKE_PORTAL_MODE ?? "ok"; // ok | down (503 HTML)
const drafts = [];
const realFetch = globalThis.fetch;
globalThis.fetch = async (url, init) => {
  if (!String(url).startsWith(PORTAL)) {
    const u = new URL(String(url));
    if (u.hostname !== "127.0.0.1") throw new Error("Dış ağ çağrısı engellendi: " + u.hostname);
    return realFetch(url, init);
  }
  if (mode === "down") return { ok: false, status: 503, json: async () => { throw new SyntaxError("Unexpected token <"); }, text: async () => "<html>503</html>" };
  const body = new URLSearchParams(init.body);
  const json = (o) => ({ ok: true, status: 200, json: async () => o, text: async () => JSON.stringify(o) });
  if (String(url).endsWith("/assos-login")) return json({ token: "portal-token" });
  const cmd = body.get("cmd");
  const jp = body.get("jp") ? JSON.parse(body.get("jp")) : {};
  switch (cmd) {
    case "EARSIV_PORTAL_TASLAKLARI_GETIR": return json({ data: drafts.map((d) => ({ ...d })) });
    case "EARSIV_PORTAL_FATURA_OLUSTUR": {
      const ettn = jp.faturaUuid || crypto.randomUUID();
      drafts.push({ ettn, belgeNumarasi: "", onayDurumu: "Onaylanmadı", belgeTarihi: jp.faturaTarihi.split("/").reverse().join("-") });
      return json({ data: "Faturanız başarıyla oluşturulmuştur. Düzenlenen Belgeler menüsünden faturanıza ulaşabilirsiniz." });
    }
    case "EARSIV_PORTAL_TELEFONNO_SORGULA": return json({ data: { telefon: "05321234567" } });
    case "EARSIV_PORTAL_SMSSIFRE_GONDER": return json({ data: { oid: "oid-1" } });
    case "0lhozfib5410mp": {
      if (jp.SIFRE !== "123456") return json({ data: { sonuc: "0" } });
      for (const d of drafts) { d.onayDurumu = "Onaylandı"; d.belgeNumarasi = "MOCK2026000000001"; }
      return json({ data: { sonuc: "1" } });
    }
    case "EARSIV_PORTAL_FATURA_GOSTER": return json({ data: "<html>MOCK FATURA</html>" });
    default: return json({ data: {}, error: "1", messages: [{ type: "error", text: "beklenmeyen komut " + cmd }] });
  }
};

const token = process.env.GATEWAY_TOKEN;
const gw = createGateway({ client: createFaturaClient("TEST"), state: new StateStore(null), user: "mock-user", password: "mock-pass", token, env: "TEST" });
gw.server.listen(0, "127.0.0.1", () => console.log("PORT=" + gw.server.address().port));
