// Mobil düzen kontrolü — tarayıcı konsoluna yapıştırın (uygulamaya giriş yapılmış olmalı).
// Önce tarayıcıyı ~375 px genişliğe getirin (F12 → cihaz modu). Her ana ekran için raporlar:
//   overflowX   : sayfa yatay kaydırma çubuğu oluşturuyor mu (olmamalı)
//   wideEls     : görünür alanın dışına taşan öğe sayısı (kaydırılabilir sekme çubukları dışında 0 olmalı)
//   smallTargets: yüksekliği 36 px'ten küçük düğme/alan sayısı (0 olmalı)
(async () => {
  const H = { Authorization: "Bearer " + localStorage.getItem("sct_token") };
  const get = async (p) => (await fetch("/api" + p, { headers: H })).json();
  const cust = (await get("/customers?pageSize=5")).items[0];
  const sale = (await get("/sales?pageSize=1")).items[0];
  const inv = (await get("/invoices?pageSize=1")).items[0];
  const pay = (await get("/payments?pageSize=1")).items[0];
  const sup = (await get("/suppliers"))[0];
  const routes = ["/", "/musteriler", "/musteriler/yeni", cust && "/musteriler/" + cust.id, "/urunler", "/urunler/yeni", "/satislar", "/satislar/yeni",
    sale && "/satislar/" + sale.id, "/faturalar", inv && "/faturalar/" + inv.id, "/cari", cust && "/cari/musteri/" + cust.id, "/tahsilatlar",
    cust && "/tahsilatlar/yeni?musteri=" + cust.id, pay && "/tahsilatlar/" + pay.id, "/stok", "/tedarikciler", sup && "/tedarikciler/" + sup.id, "/alislar/yeni", "/raporlar", "/ayarlar"].filter(Boolean);
  const rows = [];
  for (const r of routes) {
    history.pushState({}, "", r);
    dispatchEvent(new PopStateEvent("popstate"));
    await new Promise((res) => setTimeout(res, 1500));
    const de = document.documentElement;
    const small = [...document.querySelectorAll("main button, main a.btn, main input:not([type=checkbox]):not([type=radio]):not([type=file]), main select")]
      .filter((e) => { const b = e.getBoundingClientRect(); return b.width > 0 && b.height > 0 && b.height < 36; }).length;
    const wide = [...document.querySelectorAll("main *")].filter((e) => { const b = e.getBoundingClientRect(); return b.width > 0 && b.right > de.clientWidth + 1; }).length;
    rows.push({ route: r.length > 38 ? r.slice(0, 38) + "…" : r, h1: document.querySelector("h1")?.textContent?.slice(0, 28), overflowX: de.scrollWidth > de.clientWidth, wideEls: wide, smallTargets: small });
  }
  console.table(rows);
  console.log(rows.some((x) => x.overflowX || x.smallTargets) ? "SORUN VAR" : "Yatay taşma ve küçük dokunma hedefi yok (wideEls: kaydırılabilir sekme çubukları olabilir).");
  return rows;
})();
