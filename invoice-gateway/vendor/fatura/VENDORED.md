# f/fatura — sabitlenmiş kopya

- Kaynak: https://github.com/f/fatura (MIT, bkz. LICENSE)
- Commit: `24cba00060c3678fd85b94da5aaffb7ea7d048f1` (2026-08-24)
- Derleme: `npm ci --ignore-scripts && npx tsc` (kaynak koddan; npm'deki 0.2.1 paketi kullanılmadı çünkü 08-24 tarihli
  "GİB portal değişiklikleri — fatura oluşturma ve SMS imzalama sessizce çalışmıyor" düzeltmesinden önce yayımlanmış).
- Çalışma zamanı bağımlılığı yok (yalnızca Node 18+ `fetch`).
- Güncellemek için: depoyu yeni commit'e alıp yeniden derleyin, `docs/invoice-integration.md` içindeki doğrulama
  adımlarını (TEST portalı) tekrar koşun. Üstteki commit'i değiştirmeden otomatik güncelleme YAPMAYIN.
- Resmî bir GİB API'si DEĞİLDİR: e-Arşiv portalının web arayüzü komutlarını taklit eder; portal değişirse bozulabilir
  (08-2026'daki gibi, belge oluşmuyor ama hata da verilmiyor durumuna düşebilir).

## Yerel yama (upstream'de YOK)

`dist/client.js` `getToken`/`logout`: kullanıcı adı, parola ve token `encodeURIComponent` ile kodlanır. Upstream, parolayı form gövdesine kodlamadan ekler; `& + % =` içeren GİB parolaları bozulur. Test: `invoice-gateway/test/gateway.test.mjs` → "özel karakterli parola". Upstream güncellenirse bu yama yeniden uygulanmalı/doğrulanmalıdır.

`dist/client.js` yama 2: TEST ortamında taslak/belge **listesi** `hangiTip: "Buyuk"` ile sorgulanır (mlevent/fatura `Gib::getAll` test modu); PROD'da `5000/30000`. Upstream her ortamda `5000/30000` kullanıyor; TEST'te taslak bulunamayıp "sessiz başarısızlık" korumasını tetikleyebilir. **Gerçek TEST portalında doğrulanmadı.**
