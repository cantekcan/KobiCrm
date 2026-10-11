# Fatura Entegrasyonu: Değerlendirme, Tasarım ve Durum

> **Durum özeti (dürüst):** Resmî fatura oluşturma **otomatik DEĞİL, manuel portal akışıyla** yapılır ve **varsayılan/doğrulanmış yöntem budur**. e-Arşiv portalı otomasyonu (f/fatura) için ağ geçidi ve adaptör yazıldı ve **sahte portala karşı** test edildi; **GİB TEST portalında uçtan uca doğrulanmadı** (TEST hesabı yok). Canlı gönderim **kapalıdır**.

## 0. Güncel durum kontrolü (2026-10-12, 2. tur)

* `f/fatura` üst akışı yeniden kontrol edildi: **yeni commit yok** (en güncel `24cba00`, 2026-08-24); npm sürümü hâlâ `0.2.1` (2026-04-08, eski). Sabitlenmiş kopya güncel.
* GİB portalıyla uyumluluk **doğrulanamadı**: kimlik bilgisi olmadan yalnızca `login.jsp` sayfasına erişilebildi (HTTP 200, TEST ve canlı). Kimlik bilgisi olmayan tek bir giriş yoklaması TEST `assos-login` ucundan **503** aldı (portalın o anki durumu belirsiz). Oturum açılamadığı için taslak/SMS/imza komutlarının bugünkü portalda çalışıp çalışmadığı **bilinmiyor**; 08-2026'da portal bu komutları sessizce değiştirmişti.
* Bu turda yapılanlar: kütüphane yaması (kullanıcı adı/parola/token `encodeURIComponent` ile kodlanır; `& + % =` içeren GİB parolaları bozulmaz — testli), JSON olmayan portal yanıtı (503/proxy HTML) **belirsiz sonuç** sayılır ve mükerrer taslak oluşturmaz, ağ geçidi Docker imajı + `docker compose --profile gateway`, arayüzde "Sağlayıcıya gönder" sonrası doğrudan SMS/imza adımına geçiş.
* Kalan engeller: en sondaki **"Engeller"** bölümü.

## 1. Kaynaklar (belirtilen adresler)

| Kaynak | Ne olduğu |
|---|---|
| https://turmobefatura.luca.com.tr/ | Mevcut LUCA (TÜRMOB e-Fatura) portalı. Manuel akışta resmî belge burada düzenlenebilir. Programatik arayüzü değerlendirilmedi. |
| https://portal.efatura.gov.tr/ | GİB **e-Fatura** Portalı (alıcı e-Fatura mükellefiyeti sorgusu ve e-Fatura düzenleme için). |
| https://earsivportal.efatura.gov.tr/ | GİB **e-Arşiv** Portalı (canlı). |
| https://earsivportaltest.efatura.gov.tr/ | e-Arşiv **TEST** portalı (giriş sayfasına erişim doğrulandı: HTTP 200; kimlik bilgisi olmadan başka işlem yapılmadı). |
| https://ebelge.gib.gov.tr/ | GİB e-Belge bilgi sayfası. |

e-Fatura ve e-Arşiv **aynı belge türü değildir**; hangisinin kullanılacağı alıcının e-Fatura mükellefiyetine ve güncel mevzuata bağlıdır. Uygulama bunu varsaymaz: müşterinin mükellefiyeti `Bilinmiyor` ise belge türünü kullanıcı seçer ve uyarı gösterilir; mükellef işaretli müşteriye e-Arşiv seçilemez. **Yalnızca e-Arşiv için bir kütüphane bulunması bütün müşterilere e-Fatura kesilebileceği anlamına gelmez** — ağ geçidi bu yüzden e-Fatura isteğini **reddeder**.

## 2. `f/fatura` incelemesi (kaynak koddan)

| Konu | Bulgu |
|---|---|
| Depo / dil | https://github.com/f/fatura — TypeScript/Node.js (≥ 18). Yazar: Fatih Kadir Akın. |
| Lisans | **MIT**. Çalışma zamanı bağımlılığı **yok** (yalnızca yerleşik `fetch`); geliştirme bağımlılıkları (typescript, vitest, eslint…) ürüne girmez. |
| Ne yapar | **GİB e-Arşiv Portalı'nın web arayüzü komutlarını taklit eder**: `assos-login` ile giriş, `earsiv-services/dispatch` ile `EARSIV_PORTAL_FATURA_OLUSTUR` (taslak), taslak listeleme, SMS ile imzalama, HTML/indirme, **taslak silme**, kullanıcı/alıcı sorgusu. **Resmî bir GİB API'si değildir.** |
| Ortamlar | `PROD` (varsayılan!) ve `TEST` (`earsivportaltest.efatura.gov.tr`). TEST için GİB'den/portaldan alınmış test kullanıcısı gerekir; kütüphane test kullanıcısı üretmez. |
| **e-Fatura** | **Desteği yok.** Yalnızca e-Arşiv (`hangiTip: "5000/30000"`). (Projenin üst düzey tanıtımı "e-Fatura" kelimesini içerse de kod yalnızca e-Arşiv portalına konuşur.) |
| İmzalama | Portalın kayıtlı telefona gönderdiği **SMS kodu** gerekir (insan etkileşimi). `verifySignSMSCode` mali işlem oluşturur. Komut adı portal tarafında opak bir kimliğe taşınmış (`0lhozfib5410mp`). |
| İptal | **İmzalı belge iptali yok.** Yalnızca *taslak silme* (`EARSIV_PORTAL_FATURA_SIL`). İmzalı e-Arşiv belgesinin iptal/itiraz süreci portaldan yürütülür. |
| Test kapsamı | Kütüphane birim testlerini sahte `fetch` ile yapar; portalla entegrasyon testi dosyası var ama gerçek hesap gerektirir. |
| README ile kod uyumsuzluğu | README "uuid vermezseniz yeni UUID atanır" der; kod ise (08-2026 düzeltmesinden sonra) **ETTN'i GİB'in ürettiğini** söyler ve istemci UUID'sini göndermez. Yani **README güncel değil**; kaynak koda güvenildi. |
| Parola işleme | Upstream `getToken`, parolayı form gövdesine **URL-encode etmeden** ekler; `&`, `+`, `%` içeren parolalar bozulur. **Bu depodaki kopyada yamalandı** (`VENDORED.md` "Yerel yama"; testli). İndirme adresi `token`'ı sorgu dizesinde taşır; ağ geçidi indirme URL'si kullanmaz, HTML'i sunucuda alıp iletir. |
| Bakım riski | **Yüksek ve kanıtlı.** Son commit (`24cba00`, 2026-08-24): *"GİB portal değişiklikleri — fatura oluşturma ve SMS imzalama sessizce çalışmıyor"* — portal arayüzü değişince kütüphane **hata vermeden belge oluşturmuyordu**. `npm` üzerindeki sürüm (`0.2.1`, 2026-04-08) bu düzeltmeden **önce** yayımlandı; yani npm paketi bugün **bozuk** olabilir. Portal tekrar değişirse aynısı olur. |

### Karar

* Kütüphane **npm'den değil**, doğrulanmış commit'ten **kaynak koddan derlenip** `invoice-gateway/vendor/fatura` altına **sabitlenerek** alındı (`VENDORED.md`: commit `24cba00060c3678fd85b94da5aaffb7ea7d048f1`, MIT lisansı korunur). Otomatik güncelleme yok.
* **Sessiz başarısızlığa karşı:** ağ geçidi, "başarılı" yanıtına güvenmez. Taslak oluşturma sonrası taslak kimliği (ETTN) alınamıyorsa **başarı bildirmez**; imzalamadan sonra belgenin portal listesinde **"Onaylandı" ve numaralı** göründüğünü doğrular, aksi halde `temporary` döner.
* Canlı kullanıma geçiş **yalnızca** §6'daki TEST doğrulaması başarıyla tamamlanıp kayda geçirilirse yapılmalıdır.

## 3. Sağlayıcı sözleşmesi (`IInvoiceProvider`)

```csharp
Task<ProviderResult<ProviderValidation>> ValidateInvoiceAsync(InvoiceDocumentRequest, ct);
Task<ProviderResult<ProviderDraft>>      CreateDraftAsync(InvoiceDocumentRequest, ct);          // TASLAK, resmî değil
Task<ProviderResult<ProviderChallenge>>  RequestIssueChallengeAsync(ProviderReference, ct);      // SMS/OTP oturumu
Task<ProviderResult<ProviderIssued>>     IssueAsync(ProviderReference, ProviderIssueInput, ct);  // RESMÎ düzenleme (hukuki sonuç)
Task<ProviderResult<ProviderStatus>>     GetInvoiceStatusAsync(ProviderReference, ct);
Task<ProviderResult<ProviderDocument>>   GetInvoiceDocumentAsync(ProviderReference, format, ct);
Task<ProviderResult<ProviderCancellation>> RequestCancellationAsync(ProviderReference, reason, ct);
```

* Sonuçlar `Success | NotSupported | Rejected | TemporaryFailure | NotFound`. **Desteklenmeyen işlem asla "başarılı" dönmez.** `Capabilities` (CanCreateDraft, CanIssue, CanCancel, SupportsEInvoice, `IsLiveCapable`, `RequiresSmsCode`…) her sağlayıcının gerçekte ne yapabildiğini bildirir; arayüz ve servis buna göre davranır.
* `ProviderReference` her adımda aynı `SubmissionKey`'i taşır; sağlayıcı bunu mükerrer belge önleme için kullanır.

### Uygulamalar

| Sağlayıcı | Ne yapar | Durum |
|---|---|---|
| **Manual** (varsayılan) | Hiçbir resmî işlem yapmaz; her çağrı `NotSupported`. Kullanıcı belgeyi LUCA/GİB'de düzenler, numarayı CRM'e kaydeder. | **Tamamlandı ve testli** |
| **Mock** (`Invoice:EnableMockProvider=true`) | Yalnızca geliştirme/test: bellek içi sahte sağlayıcı, ağ hatası/ret enjeksiyonu. `IsLiveCapable=false`; ürettiği numaralar `TST…` ve arayüzde "TEST belgesi — yasal geçerli değil" etiketli. **Üretimde kapalı olmalı.** | Testli |
| **EArsivGateway** (`Invoice:Gateway:Url` verilirse) | Node ağ geçidi üzerinden GİB e-Arşiv: taslak, SMS ile imzalama, durum, HTML. **İptal yok, e-Fatura yok.** `IsLiveCapable` = ağ geçidi `PROD` ortamında mı. | Sahte portalla testli; **GİB TEST'inde doğrulanMADI** |

## 4. Fatura durum modeli

İç süreç ve resmî durum **ayrı sütunlardır** (bkz. `business-requirements.md` §3.7). Hiçbir durum GİB'den doğrulanmış gibi gösterilmez:

* `Resmî belge kayıtlı` + `Kullanıcı girişi` → "GİB'den doğrulanmadı" rozeti.
* `Resmî belge kayıtlı` + `Sağlayıcı` → "Sağlayıcı: <ad>"; sağlayıcı canlı-yetenekli değilse (Mock/TEST) ek olarak **"TEST sağlayıcısı — yasal geçerli DEĞİLDİR"**.
* `Sonuç doğrulanamadı` → yeniden gönderim **engellenir**, "Durumu sorgula" önerilir.

Başarılı resmî işlem sonrası kaydedilen bilgiler: resmî numara, düzenlenme tarihi, ETTN/UUID, resmî belge türü, **kaynak**, sağlayıcı adı, doğrulandı mı, kaydedilme zamanı, (sağlayıcıysa) `SubmissionKey`/taslak kimliği, dosya ekleri (PDF/XML).

## 5. Ağ geçidi (`invoice-gateway/`)

* Node.js, bağımlılıksız (`node:http`), varsayılan `127.0.0.1:5090`. `GATEWAY_TOKEN` (≥ 32 karakter, yer tutucu reddedilir) ile korunur. `GIB_USER/GIB_PASSWORD` **yalnızca bu sürecin ortamında**; CRM'e/arayüze/günlüğe girmez.
* `GIB_ENV=TEST` varsayılan. `PROD` için ayrıca `GIB_ALLOW_PROD=yes` gerekir (kasıtlı ikinci onay) ve CRM tarafında `Invoice:LiveSendingEnabled=true`.
* Uç noktalar: `POST /v1/drafts`, `POST /v1/issue/challenge`, `POST /v1/issue`, `POST /v1/cancel` (yalnızca iptal **talebi**, bkz. §12), `GET /v1/status/{key}`, `GET /v1/document/{key}`.
* **Mükerrer koruması:** her `submissionKey` durum dosyasında (`data/gateway-state.json`, atomik yazım) izlenir. Ağ hatasında kayıt `pending` kalır; aynı anahtarla yeniden gelen istek **portala gitmez** (409 `temporary`). Açık ret (portal metni/doğrulama) kaydı temizler ve yeniden denemeye izin verir. İşlemler tek kuyrukta seri yürür.
* Gelen istek ağ geçidinde **yeniden doğrulanır**: yalnızca e-Arşiv, tutarlar kendi içinde tutarlı (kalem net/KDV/genel toplam), VKN/TCKN biçimi, kalem sayısı. Tutarsız belge portala **gönderilmez**.
* İç referans, portalda `Sipariş No` alanına yazılır (portal taslak listesinde eşleştirme için).
* Eşleme **doğrulanmadı** olarak işaretlidir: birim kodları (Adet→C62, Kg→KGM, Gram→GRM, Litre→LTR, diğer→C62), `fiyat` alanının "indirim sonrası net satır tutarı" olarak yorumlanması, şahıs ad/soyad bölme. **İndirimli kalemleri TEST portalında mutlaka deneyin.**

## 6. TEST portalında doğrulama protokolü (canlıya geçmeden ÖNCE)

Önkoşul: GİB'den alınmış **TEST e-Arşiv kullanıcısı** ve imza SMS'inin gideceği test telefonu. (Bu hesap olmadığı için aşağıdakiler yapılamadı.)

1. `invoice-gateway` `GIB_ENV=TEST` ile başlat; CRM'de `Invoice__Gateway__Url/Token` ayarla, **Ayarlar → Fatura yöntemi → EArsivGateway** seç. `Invoice:LiveSendingEnabled` kapalı kalabilir (TEST canlı-yetenekli değildir).
2. İndirimsiz, tek kalemli, VKN'li bir fatura taslağı → **Sağlayıcıya gönder**. Portalın "Taslaklar" ekranında taslağın oluştuğunu **gözle doğrulayın**; tutarları ve KDV'yi karşılaştırın.
3. İndirimli ve çok kalemli, farklı KDV oranlı fatura ile tekrarlayın; portal toplamı = CRM toplamı olmalı (yuvarlama farkı varsa `business-requirements.md` A3'ü güncelleyin).
4. TCKN'li şahıs alıcı ile tekrarlayın (ad/soyad ayrımı).
5. **SMS kodu iste** → doğru kod → portalda belgenin imzalandığını ve numarayı doğrulayın; CRM numarayı aynı gösteriyor mu?
6. **Hatalı SMS kodu** → belgenin imzalanMADIĞINI doğrulayın.
7. Ağ hatası senaryosu: ağ geçidi çalışırken `taslak` sırasında bağlantıyı kesin → CRM "Sonuç doğrulanamadı" göstermeli; yeniden gönderim engellenmeli; portal taslak listesinde **tek** taslak olmalı.
8. Özel karakterli (`& + %`) parolayla giriş deneyin (yama sahte portalda testli; gerçek portalda doğrulayın). SMS kodu ile imza arasında 10 dk'dan uzun beklenirse oturum düşer; "SMS kodu iste"yi yeniden kullanın.
9. Sonuçları (tarih, kütüphane commit'i, portal davranışı) bu dosyaya "Doğrulama kaydı" olarak ekleyin.

### Doğrulama kaydı

| Tarih | Ortam | Sonuç |
|---|---|---|
| 2026-10-12 | TEST portalı `POST /earsiv-services/esign` (`assoscmd=kullaniciOner`, portalın kendi giriş sayfasındaki çağrıyla aynı): önce **404**, birkaç dakika sonra **503** (nginx, `text/html`) — **portal tarafı arıza**, kod hatası değil; test kullanıcısı alınamadı | 
| 2026-10-12 | Yalnızca sahte portal (birim/entegrasyon testleri) | Ağ geçidi + adaptör + servis akışı geçti. **GİB TEST/PROD ile gerçek doğrulama YAPILMADI.** |

## 7. Canlıya geçiş kontrol listesi

- [ ] §6 protokolü TEST'te eksiksiz geçti ve kaydedildi.
- [ ] Muhasebeci: e-Fatura mükellefiyet durumu, e-Arşiv kullanım koşulları, şirket türü, mali mühür/e-imza durumu teyit edildi; e-Arşiv'in işletme için uygun kanal olduğu doğrulandı.
- [ ] Müşteri mükellefiyetleri (e-Fatura portalı sorgusuyla) girildi.
- [ ] Ağ geçidi `GIB_ENV=PROD`, `GIB_ALLOW_PROD=yes`; CRM `Invoice:LiveSendingEnabled=true`.
- [ ] İlk gerçek belge **tek bir düşük tutarlı** faturayla, kullanıcı başında denendi.
- [ ] Portal değişikliklerini izleme/rutin kontrol sorumlusu belli (kütüphane sessiz bozulabilir; her ay bir test faturası önerilir).

## 8. Alternatifler (değerlendirme notları)

* **LUCA'yı kullanmaya devam (manuel akış):** şu an en güvenli yol; ek maliyet yok; kullanıcı iki ekran arasında numara taşır.
* **Özel entegratör (ücretli API):** resmî olarak desteklenen, SLA'lı yöntemdir; `IInvoiceProvider` ile aynı sözleşmeye bağlanabilir. Maliyet nedeniyle ilk tercih değildir, ancak portal otomasyonu bakım riski kabul edilemezse önerilir.
* **GİB e-Fatura doğrudan entegrasyonu:** mali mühür/e-imza, test akışı ve başvuru gerektirir; işletme uygunluğu bilinmediği için araştırılmadı.

## 9. Engeller — CRM'den doğrudan gerçek fatura kesilebilir mi?

**Kod tarafı hazır; gerçek faturanın kesilebildiği KANITLANMADI, bu yüzden "çalışıyor" denemez.** Akış (CRM'den ayrılmadan): taslak → iç onay → **Sağlayıcıya gönder** (portalda taslak) → otomatik **SMS iste / kod gir / imzala** → resmî numara, ETTN, tarih, durum CRM'e yazılır. Hata gerçek mesajla gösterilir; belirsiz sonuçta yeniden gönderim engellenir ("Durumu sorgula"); mükerrer: `SubmissionKey` + ağ geçidi durum dosyası + DB kısıtları.

Kalan engeller:
1. **GİB e-Arşiv (TEST veya canlı) kullanıcı bilgisi yok** → portal komutlarının bugünkü portalda çalıştığı doğrulanamadı.
2. **İmza SMS'i** insan etkileşimi gerektirir; otomatik test edilemez.
3. İndirimli kalem, birim kodu, şahıs ad/soyad eşlemeleri portalda doğrulanmadı.
4. **İmzalı belge iptali** ağ geçidiyle yapılamaz (kütüphane desteklemiyor; resmî yöntem portalda iptal/itiraz). CRM sahte iptal yapmaz: iptal talebini işaretler, iptal portalda yapılır, kullanıcı beyanıyla kaydedilir ("GİB'den doğrulanmadı" etiketiyle).
5. **e-Fatura desteklenmiyor** (yalnızca e-Arşiv); e-Fatura mükellefi müşteriye e-Arşiv kesilemez, CRM bunu engeller.
6. İşletmenin e-Arşiv'i kullanıp kullanamayacağı (GİB e-Arşiv izni/başvurusu, mükellefiyet, mali mühür/e-imza durumu) bilinmiyor.

Sizin sağlamanız gerekenler: GİB e-Arşiv **TEST kullanıcı kodu + parola** (ve imza SMS'inin gideceği telefon); canlı için işletmenin e-Arşiv portal kullanıcı kodu/parolası ve kayıtlı cep telefonu; muhasebeciden mükellefiyet teyidi. Bunlar `.env` içine (`GIB_USER`, `GIB_PASSWORD`, `GATEWAY_TOKEN`, `GIB_ENV`) yazılır; kaynak koda yazılmaz.

## 10. Alternatif değerlendirme: `xBuhari/buhari-earsiv-api` (2026-10-12)

Kaynak koddan incelendi (https://github.com/xBuhari/buhari-earsiv-api). **Seçilmedi; CRM'ye bağlanmadı.**

| Konu | Bulgu |
|---|---|
| Lisans / geçmiş | MIT; **tek commit** (2026-05-09, "First commit"); **test dosyası yok**; sürüm 1.0.0. Olgunluk/bakım güvencesi yok. |
| Tür | Fastify (Node) REST servisi; yine **GİB e-Arşiv portal otomasyonu** (resmî API değil; README'si de bunu söyler). Yalnızca e-Arşiv; **e-Fatura yok**. |
| Artıları | Hazır REST + OpenAPI, API anahtarı, IP filtresi, oturum havuzu/otomatik yeniden giriş, sağlık ucu, PDF/ZIP indirme, isteğe bağlı SMTP, Docker. Mayıs 2026 portal payload notlarını içerir. |
| **Kritik eksik: imzalama yok** | Kodda **SMS/OTP, imzalama (`HSM`/`imzala`) komutu hiç yok**. Yalnızca `EARSIV_PORTAL_FATURA_OLUSTUR` (taslak), liste, görüntüle, indir ve `EARSIV_PORTAL_FATURA_SIL`. Yani ürettiği belge **imzasız taslaktır**; resmî fatura olmaz. README "fatura oluştur" der ama imzayı anlatmaz. |
| İptal | `DELETE` = `FATURA_SIL` yani **taslak silme**; imzalı belge iptali yok (bizim ağ geçidindeki durumla aynı). |
| Test | README varsayılan GİB **test kullanıcılarını** (`.env.example`'da) belgeliyor. Bu bilgileri sizin adınıza kullanmadım: üçüncü tarafın README'sinden gelen kimlik bilgileriyle harici portala giriş yapmak benim yapmayacağım bir işlem; isterseniz kendiniz denemeniz için yöntem aşağıda. |
| Güvenlik | API anahtarı + IP filtresi iyi; ancak GİB parolası/oturumu servis içinde bellek deposunda; paylaşılan yönetim yüzeyi (Swagger, mail, PDF üretimi: puppeteer/html-pdf bağımlılıkları) CRM için gereksiz saldırı yüzeyi. |

### Karşılaştırma ve karar

| Ölçüt | `f/fatura` ağ geçidimiz | buhari-earsiv-api |
|---|---|---|
| **Resmî fatura için SMS ile imza** | **Var** (challenge + verify, sonuç portal listesinden doğrulanır) | **Yok** (yalnızca taslak) |
| İmzalı belge iptali | Yok (portaldan) | Yok (yalnızca taslak silme) |
| e-Fatura | Yok | Yok |
| Mükerrer fatura koruması | `submissionKey` + durum dosyası + DB kısıtları | Yok (yeniden deneme var, idempotans yok) |
| Sessiz başarısızlık koruması | Var (taslak/imza doğrulanır) | Kısmen (ETTN yoksa listeden eşleştirir) |
| Testler | 17 ağ geçidi + CRM tarafı testleri | Yok |
| Bakım | Upstream: 2 yıl+ topluluk, son düzeltme 2026-08; riskli ama sabitlenmiş | Tek commit, tek kişi |
| CRM'ye entegrasyon | Hazır (IInvoiceProvider) | Yeni adaptör + imza adımının sıfırdan yazılması gerekir |

**Karar: `f/fatura` tabanlı ağ geçidi korunur.** buhari-earsiv-api amaca (CRM'den *imzalı* resmî fatura) yetmez; imza eksik. Faydalı fikirleri (otomatik yeniden giriş, sağlık ucunda GİB erişim yoklaması) gelecekte ağ geçidine alınabilir. Kodu kopyalamadık.

### Diğer açık kaynak seçenekler (araştırma)

Arama sonuçlarında çıkan açık kaynak projelerin tümü aynı yaklaşım ailesi (GİB e-Arşiv portal otomasyonu): PHP `mlevent/fatura`, `furkankadioglu/efatura`, `evrenonur/fatura`, `bercanozcan/earsiv`, `gurmehub/gib-client`. Bunlar PHP olduğundan .NET+Node mimarimize ek bir çalışma zamanı getirir, güven/bakım açısından `f/fatura`dan üstün olduklarına dair kanıt bulunmadı; **incelenmedi**. **e-Fatura** için açık kaynak, resmî ve ücretsiz bir yol bulunamadı: e-Fatura ya GİB'e özel entegratör başvurusu (mali mühür + test süreci) ya da ücretli/izinli bir entegratör API'si gerektirir. e-Fatura ile e-Arşiv bu yüzden ayrı değerlendirilir: **e-Arşiv** için (SMS ile) portal otomasyonu mümkün ama doğrulanmamış; **e-Fatura** için şu an çözüm yok (müşteri e-Fatura mükellefiyse fatura portaldan manuel veya entegratörle kesilir).

### Bu turda gerçek GİB testi

**Yapılmadı.** Kullanıcıya ait bir GİB hesabı yok; bu yüzden hiçbir entegrasyon "gerçek GİB'de çalıştı" diye sunulamaz. Kendi başınıza TEST denemesi için en güvenli yol `invoice-integration.md` §6'dır (kendi GİB TEST kullanıcınızla ve kendi telefonunuzla).

## 11. Resmî kaynaklar ve alternatif yöntemler (araştırma, 2026-10-12)

> Yöntem: web araması + sayfa okuma. **Hiçbir hesap açılmadı, hiçbir ücretli hizmet alınmadı, hiçbir fatura gönderilmedi.** Fiyatlar üçüncü taraf karşılaştırma sitelerinden (tarihleri belirtilmiş) alınmıştır; **satıcı sayfalarında doğrulanmadı**, satın almadan önce satıcıdan teyit edin. GİB'in teknik kılavuz PDF'i bu ortamda metne çevrilemedi (okunamadı); kılavuzların içeriği hakkında yalnızca bağlantılar ve arama özetleri kullanıldı.

### 11.1 GİB'in resmî çerçevesi

| Konu | Bulgu | Kaynak |
|---|---|---|
| e-Arşiv'i kullanma yolları | (1) **GİB e-Arşiv Portalı** (ücretsiz, elle), (2) **doğrudan bilgi sistemi entegrasyonu** (başvuru + BİS raporu + test tanım formu, test hesapları GİB tarafından tanımlanır), (3) **özel entegratör**. Entegratör kullanımı zorunlu değil. | [e-Arşiv Başvuru Kılavuzu](https://www.alomaliye.com/2018/01/09/e-arsiv-basvuru-kilavuzu/), [TÜRMOB e-Arşiv başvuru sayfası](https://turmob.org.tr/e-donusum/eArsivfatura/basvuru) |
| Özel entegratör | GİB'den "özel entegrasyon" izni almış kuruluşlar; test başarıyla geçilmeden izin verilmez. **Resmî izinli entegratör listesi GİB'de yayımlanır.** | [GİB Özel Entegrasyon](https://ebelge.gib.gov.tr/efaturaozelentegratorluk.html) |
| Teknik kılavuzlar | e-Arşiv Portalı Entegrasyon Kılavuzu (GİB, Eylül 2021), e-Fatura Entegrasyon/Özel Entegrasyon Kılavuzları | [e-Arşiv Entegrasyon Kılavuzu (PDF)](https://ebelge.gib.gov.tr/dosyalar/kilavuzlar/e-Arsiv_Fatura_Portali_Entegrasyon_Kilavuzu.pdf), [e-Fatura Entegrasyon Kılavuzu v1.9](https://www.turmob.org.tr/arsiv/mbs/resmigazete/-e-FaturaUygulamasiEntegrasyonKilavuzu-v1.9.pdf), [e-Arşiv Özel Entegratör ile kullanım](https://www.turmob.org.tr/dokumanlar/download/8b6d8fac-031b-469e-8e8e-c689234914d0) |
| **Portal otomasyonu** | GİB **herkese açık, belgelenmiş bir e-Arşiv Portalı API'si yayımlamaz**; portal komutlarını taklit eden kütüphaneler resmî değildir. Otomasyonu **açıkça yasaklayan/izin veren bir kullanım koşulu metni bulamadım** (bu belirsizliğin kendisi risktir). | arama sonuçları; ayrıca bkz. §2 |
| Portal sınırları | Portal yönteminde yalnızca e-Arşiv; e-Fatura için alıcı/gönderici mükellefiyeti ve ayrı kayıt gerekir. Alıcı e-Fatura mükellefiyse e-Arşiv değil e-Fatura düzenlenir. | [GİB e-Belge](https://ebelge.gib.gov.tr/) |

### 11.2 Mevcut LUCA (TÜRMOB Luca e-Entegratör) hesabımız

* `turmobefatura.luca.com.tr`, **TÜRMOB'un Luca e-Entegratör** portalıdır: e-Fatura, e-Arşiv, e-İrsaliye, e-SMM düzenlenir; başvuru/katılım ücretsiz, **kontör** ile kullanılır ([kobitime özeti](https://kobitime.com/turmob-luca/), [App Store: TÜRMOB Luca e-Entegratör](https://apps.apple.com/eg/app/t%C3%BCrmob-luca-e-entegrat%C3%B6r/id6448848501)). TÜRMOB–İŞNET işbirliği duyurusu: [Dünya](https://www.dunya.com/sirketler/turmob-ve-isnet-isbirligi-anlasmasi-imzaladi-haberi-244130).
* **Luca NET (muhasebe yazılımı)**, e-Arşiv faturayı **Belge.app** üzerinden GİB'e gönderiyor ve **SMS ile imzalıyor**; Belge.app panelinden *API Key + API Secret* üretilip Luca NET'e giriliyor ([Luca: Belge.App entegrasyonu](https://lucayazilim.freshdesk.com/support/solutions/articles/67000694322-luca-net-belge-app-entegrasyon-i%CC%87le-fatura-g%C3%B6nderme-i%CC%87mzalama-onaylama-g%C3%B6r%C3%BCnt%C3%BCleme)). Bu, **resmî, entegratör üzerinden "CRM'den gönder + SMS ile imzala" akışının Luca ekosisteminde zaten var olduğunu** gösterir.
* TÜRMOB'un **web servis entegrasyonu** yazılım firmaları için sunuluyor; Luca destek notları web servis kullanımının gizlilik sözleşmesiyle ücretsiz verildiğini ve **servis anahtarının** [e-Birlik](https://ebirlik.turmob.org.tr/) → *Entegrasyon Anahtarı* menüsünden üretildiğini belirtiyor ([Luca: TÜRMOB servis anahtarı](https://lucayazilim.freshdesk.com/support/solutions/articles/67000686858-t%C3%BCrmob-servi%CC%87s-anahtari-alma-api%CC%87-key-sorgulama-i%CC%87%C5%9Flemleri%CC%87), [Luca: TÜRMOB entegratörlük modülü](https://lucayazilim.freshdesk.com/support/solutions/articles/67000715288-luca-net-te-t%C3%BCrmob-entegrat%C3%B6rl%C3%BCk-mod%C3%BCl%C3%BC-aktif-etme)). **Ancak**: bulduğum anahtar açıklaması VKN/TCKN sorguları içindir; **CRM'imizin fatura *gönderebileceğine* dair herkese açık bir API dokümanı/fiyat sayfası bulamadım.** Belge.app'in üçüncü taraf API dokümantasyonu da bulunamadı (`belge.app` ana sayfası içerik vermedi).
* **Sonuç:** LUCA ile API imkânı *muhtemel ama doğrulanmadı*. Doğrulama yolu: Luca/TÜRMOB e-Dönüşüm desteğine yazılı soru (aşağıdaki "Sorulacaklar").

### 11.3 Özel entegratörler (kıyas)

| Seçenek | e-Arşiv / e-Fatura | API ve test | Maliyet (doğrulanmamış, tarihli) | Not |
|---|---|---|---|---|
| **TÜRMOB Luca e-Entegratör** (mevcut) | İkisi de | Web servis/anahtar var; CRM için fatura gönderme API'si **doğrulanmadı**; sandbox bilgisi yok | Katılım ücretsiz, kontör ile; **kontör fiyatı bulunamadı** | Zaten hesabınız var; yasal; değişiklik/başvuru gerekebilir |
| **BirFatura** (GİB izinli özel entegratör) | İkisi de | [REST API belgeleri](https://developers.birfatura.com/), **sandbox hesabı**, [API ücretsiz](https://birfatura.com/birfatura-api-entegrasyonu/); **dikkat:** belgelenen model, yazılımınızın BirFatura'nın çağıracağı uç noktaları (`/api/orders` …) sunması; doğrudan "fatura oluştur" push uç noktası olup olmadığı **doğrulanmadı** | 14 gün ücretsiz deneme; kontör: 500 kontör ≈ 400–480 TL ([BirFatura fiyatlar](https://birfatura.com/efaturafiyatlar/), [eticaretradari, 19.09.2026](https://eticaretradari.com/e-fatura-kontor-fiyatlari/)) | Düşük hacim için en ucuz sınıflardan |
| **Fatura Entegratör (Gurmehub)** — aracı API | Çoklu entegratör (TÜRMOB, EDM, Sovos… listeli); e-Fatura+e-Arşiv | [REST/JSON, sandbox var](https://faturaentegrator.com/fatura-api/), [dev dokümanı](https://dev.faturaentegrator.com/) | Kontör başına 1,63 TL'den; aylık planlar; ücretsiz deneme | **Üçüncü taraf aracı**: GİB/entegratör bilgileriniz ona emanet edilir (güven + KVKK) |
| Mükellef, Paraşüt, Bizim Hesap, Faturaport | İkisi de | API var (ayrıntı doğrulanmadı) | 100 kontör 199–400 TL; Bizim Hesap 200 kontör 490 TL ([eticaretradari](https://eticaretradari.com/e-fatura-kontor-fiyatlari/)) | Çoğu ön muhasebe paketi; yalnızca API için paket almak gerekebilir |
| Turkcell e-Şirket, Logo, QNB, Sovos, Uyumsoft, EDM | İkisi de | Kurumsal REST/SOAP; [Turkcell e-Şirket REST kılavuzu](https://developer.turkcellesirket.com/fatura) | 1.000 kontör ~3,4–5,3 TL/kontör (Turkcell), Logo 150 kontör 2.250 TL; diğerleri teklif usulü | Küçük işletme için pahalı/ağır |
| **GİB doğrudan entegrasyon** | İkisi de | GİB test hesabı + BİS raporu + test tanım formu | Entegratör ücreti yok; **mali mühür/e-imza, geliştirme ve sertifikasyon emeği** | Küçük işletme için orantısız |
| **Portal otomasyonu** (bizim ağ geçidi) | Yalnızca e-Arşiv | Resmî API yok | Ücretsiz | §11.4 |

### 11.4 Portal otomasyonunun riskleri

1. **Resmî destek ve sözleşme yok**; arayüz değişince sessizce bozulur (2026-08'de kanıtlandı, bkz. §2).
2. Kullanım koşulları belirsiz; GİB hesabınızın otomasyon nedeniyle kısıtlanması riski **doğrulanamadı** ama göz ardı edilemez.
3. **GİB parolanız** (ve SMS ile imza yetkisi) sizin sunucunuzda tutulur; yasal sorumluluk (yanlış fatura) tamamen işletmeye aittir.
4. Yalnızca e-Arşiv; e-Fatura mükellefi müşteriler için çözüm yok.
5. Doğrulanmadı; SMS insan etkileşimi ister.

### 11.5 Değerlendirme ve öneri

Ölçütler: ücret, kurulum zorluğu, yasal/sürdürülebilir olma, e-Arşiv+e-Fatura.

| Sıra | Seçenek | Maliyet | Kurulum | e-Arşiv / e-Fatura | Yasal/sürdürülebilir |
|---|---|---|---|---|---|
| **1 (öneri)** | **Mevcut LUCA'nın (TÜRMOB e-Entegratör / Belge.app) web servis/API erişimi** | En düşük (zaten müşterisiniz; kontör maliyeti aynı; entegrasyon ücretsiz olabilir — *teyit gerekli*) | Orta (destekle yazışma, anahtar/sözleşme, adaptör yazımı) | İkisi de (Luca portalı ikisini de destekler; API kapsamı teyit edilmeli) | Resmî izinli entegratör, SMS/imza entegratör tarafında |
| **2 (yedek)** | **BirFatura** (sandbox + ücretsiz API + 14 gün deneme) | ~400–480 TL / 500 kontör + ücretsiz API | Orta (hesap, sandbox, adaptör; push-API modeli teyit edilmeli) | İkisi de | Resmî izinli entegratör |
| **3** | **Portal otomasyonu (mevcut ağ geçidi)** | Ücretsiz | Hazır ama doğrulanmadı | Yalnızca e-Arşiv | Resmî değil, kırılgan |

**Öneri:** Önce **LUCA/TÜRMOB e-Dönüşüm desteğine** yazılı soru gönderilsin (aşağıda). Olumlu yanıt gelirse `IInvoiceProvider` altında bir "TÜRMOB/Luca" adaptörü yazılır (e-Arşiv + e-Fatura ayrı belge türleri olarak; imza entegratörde). Yanıt olumsuz/belirsizse BirFatura sandbox'ı (ücretsiz) ile *tek bir deneme faturası* akışı doğrulanır. Mevcut ağ geçidi **silinmez**, yedek seçenek olarak kalır.

**Sorulacaklar (Luca/TÜRMOB e-Dönüşüm destek):**
1. Mevcut Luca e-Entegratör hesabımızla **üçüncü taraf yazılımdan (kendi CRM'imizden) e-Arşiv ve e-Fatura oluşturup göndermek** için bir web servis/API var mı? Dokümanı ve başvuru süreci nedir?
2. Test/sandbox ortamı veriliyor mu? Belge.app API'si (Api Key/Secret) üçüncü taraf kullanımına açık mı?
3. Ek ücret var mı (kontör fiyatı, API/entegrasyon bedeli)? Sözleşme/gizlilik şartları nelerdir?
4. e-Arşiv imzası (SMS/mali mühür) API ile nasıl yapılıyor? İptal/itiraz işlemleri API'de destekleniyor mu?
5. Fatura numarası serisi, ETTN, PDF/XML geri dönüşü ve durum sorgusu nasıl?

**Onay bekleniyor.** Uygulamaya geçmeden önce: (a) bu soruların Luca'ya sizin tarafınızdan iletilmesi, (b) alternatif olarak BirFatura sandbox hesabı açılmasına izniniz. Hesap açma, ücretli hizmet ve canlı gönderim yapılmamıştır.

## 12. Açık kaynak kaynak-kod incelemesi (GitHub/GitLab) ve karar (2026-10-12)

Yöntem: depolar klonlandı, giriş/taslak/SMS/imza/indirme/iptal kodu okundu, commit/lisans/bağımlılık/test kontrol edildi, çalıştırılabilen testler koşuldu. **Hiçbiri gerçek GİB portalına karşı çalıştırılmadı** (kimlik bilgisi yok; başkasının README'sindeki test kullanıcılarıyla da giriş yapılmadı). GitLab'da ilgili bir proje **bulunamadı** (arama sonuçları yalnızca GitHub/Packagist/npm döndürdü).

| Proje | Dil / lisans | Commit · son · başlangıç | Test | Giriş / taslak | SMS+imza | İndirme | İptal | Portal uyumu (Eyl 2026 bilgisiyle) |
|---|---|---|---|---|---|---|---|---|
| [f/fatura](https://github.com/f/fatura) | TS (Node) · MIT · 0 bağımlılık | 42 · 2026-08-24 · 2020 | 247 test **geçti** (sahte `fetch`; gerçek portal yok) | evet / evet | **evet** (`0lhozfib…`, OPR=1, DATA) | evet (URL/HTML) | yalnızca **taslak silme** | Ağustos düzeltmesi (boş `faturaUuid`) var; **ama** `faturaTipi`/`hangiTip` varsayılanları diğerlerinin tersi, TEST liste tipi eksik (§12.2) |
| [keyiflerolsun/eArsivPortal](https://github.com/keyiflerolsun/eArsivPortal) | Python ≥3.14 · **GPLv3** | 38 · **2026-09-11** · 2023 | pytest (çoğu birim; **çalıştırılamadı**: Python yok) | evet / evet | **evet** (`SMSSIFRE_DOGRULA`=`0lhozfib5410mp` **09-09'da düzeltildi**) + HSM imza | evet | taslak silme | **En güncel**: 06-10 ve 09-09 portal düzeltmelerini içerir |
| [mlevent/fatura](https://github.com/mlevent/fatura) | PHP · MIT | 137 · 2026-05-17 · 2022 | PHPUnit (**çalıştırılamadı**: PHP yok) | evet / evet | evet (`0lhozfib…`) | evet | **iptal talebi + itiraz talebi + gelen talepleri listeleme** | `faturaUuid`'i istemci üretiyor (`create_uuid`) → Ağustos portal değişikliğinden **önce**, büyük olasılıkla bozuk |
| [bilaleren/e-fatura](https://github.com/bilaleren/e-fatura) | TS · MIT (paket) | 53 · 2025-12-07 · 2023 | vitest (kaynakta mevcut; **çalıştırılmadı**) | evet / evet | evet | evet | **iptal talebi** | 2025-12 → 2026 portal değişikliklerinden önce, eski |
| [bevren/gib-fatura-mcp](https://github.com/bevren/gib-fatura-mcp) | TS · **lisans dosyası yok** | **1** · 2026-07-17 | model/duman testleri; **model testi geçti** (yerel hesap; portal yok) | evet / evet (`faturaUuid` göndermiyor) | evet | evet | **iptal talebi + itiraz** | mlevent'in TS **portu** (README'de belirtilmiş); MCP sunucusu (LLM aracı) — CRM'e uygun değil |
| [BFYDigital/e-arsiv-fatura-dotnet](https://github.com/BFYDigital/e-arsiv-fatura-dotnet) | C# · lisans yok | 6 · 2022-03-08 | yok | evet / evet (`Guid.NewGuid()` ETTN) | eski komut adı (`SMSSIFRE_DOGRULA`) → **bozuk** | evet | taslak silme | 4 yıl eski; **kullanılamaz** |
| [xBuhari/buhari-earsiv-api](https://github.com/xBuhari/buhari-earsiv-api) (önceki tur) | TS · MIT | 1 · 2026-05-09 | yok | evet / evet | **yok** | evet | taslak silme | imza yok → resmî fatura üretmez |

### 12.1 Projeler arası ilişki

Hepsi aynı bilgiyi yeniden uygular: GİB e-Arşiv portalının tarayıcı komutları (`earsiv-services/dispatch`). Doğrudan fork zinciri: **bevren = mlevent'in TypeScript portu**. PHP, JS, Python ve C# sürümleri birbirinin kopyası değil; **bağımsız olarak aynı komut adlarına** ulaşıyor (`EARSIV_PORTAL_FATURA_OLUSTUR`, `…SMSSIFRE_GONDER`, opak imza komutu `0lhozfib5410mp`). Bu, komutların portalda gerçekten var olduğuna dair **dolaylı** kanıttır, ama doğrulama değildir (hepsi aynı kaynaktan — portalın tarayıcı trafiği — türemiş olabilir). Eylül 2026'da keyiflerolsun ve Ağustos'ta f/fatura portal değişikliklerini düzeltmiş; diğerleri geride.

### 12.2 f/fatura ile çapraz kontrolde bulunan uyumsuzluklar (düzeltildi)

1. **`faturaTipi` / `hangiTip`**: mlevent, bevren ve buhari-notları `faturaTipi="SATIS"`, `hangiTip="5000/30000"` kullanıyor; f/fatura varsayılanları ters (`faturaTipi="5000/30000"`, `hangiTip="Buyuk"`). Ağ geçidi artık açıkça `SATIS` / `5000/30000` gönderiyor (testli; portalda doğrulanmadı).
2. **TEST liste tipi**: mlevent test modunda taslak listesini `hangiTip="Buyuk"` ile sorguluyor; f/fatura her ortamda `5000/30000`. TEST'te bu, taslağın bulunamayıp "sessiz başarısızlık" korumasına takılmasına yol açabilirdi. Vendored kopya yamalandı (TEST→`Buyuk`, PROD→`5000/30000`; testli, **portalda doğrulanmadı**).
3. **İptal**: f/fatura'da yok; mlevent/bilaleren/bevren'de **ortak** `EARSIV_PORTAL_IPTAL_TALEBI_OLUSTUR` komutu var (`ettn`, `onayDurumu="Onaylandı"`, `belgeTuru="FATURA"`, `talepAciklama`; başarı iletisi "İptal talebiniz başarıyla oluşturulmuş…"). `pageName` kaynaklarda farklı (`RG_TASLAKLAR` ×2, `RG_BASITTASLAKLAR` ×1); çoğunluk seçildi.

### 12.3 Karar

**Motor olarak `f/fatura` korundu** (zaten Node ağ geçidimizde; MIT, bağımlılıksız, SMS+imza var; .NET CRM'e HTTP ile bağlı — dil farkı sorun değil). Diğer adaylar değiştirmeyi haklı çıkarmadı:
* **keyiflerolsun** en güncel olsa da **GPLv3** (CRM'e bağlamak için ayrı süreç/servis gerekir; ek Python≥3.14 çalışma zamanı), SMS/imza akışı f/fatura ile aynı → ek kazanç yok. Portal değiştikçe **yedek referans** olarak izlenir (komut adı değişikliklerini ilk o yakalıyor).
* **mlevent** (iptal/itiraz kodu için tek tam kaynak) Ağustos öncesi; bozuk olma riski yüksek. **İptal talebi komutu** ve alan adları ondan (ve bilaleren/bevren'den) alındı.
* bevren, bilaleren, BFY, buhari: eski, tek commit, lisanssız veya imzasız.

**Uygulanan geliştirmeler (mevcut ağ geçidi + CRM, ayrı bir motor eklenmeden):** §12.2'deki düzeltmeler; `POST /v1/cancel` (portalda **iptal TALEBİ**; yalnızca imzalı belge için; idempotent; ağ hatasında belirsiz → tekrar gönderim yok; beklenen başarı iletisi görülmezse başarı bildirilmez); CRM'de "İptal talebini portala ilet" adımı. **Talep ≠ iptal**: belge resmî olarak "Kayıtlı" kalır; sonuç portalda doğrulanınca "Resmî iptal tamamlandı — kaydet" ile **kullanıcı beyanı** olarak işlenir (`OfficialCancelVerified=false`, arayüzde "doğrulanmadı" etiketi; yeni yetenek `CanReportCancellation=false`). Akış: taslak → kullanıcı onayı (iç onay) → portalda taslak → SMS iste → kod → imza → numara+ETTN kaydı → (gerekirse) iptal talebi.

### 12.4 Doğrulama durumu (dürüst özet)

* **Geçen testler:** .NET 175 (birim 46 + entegrasyon 129), ağ geçidi 21, arayüz 26, f/fatura kendi 247 testi, bevren model testi. Hepsi **sahte/yerel**; hiçbiri GİB'e bağlanmadı.
* **Kanıtlanmadı:** gerçek GİB'de giriş, taslak, SMS, imza, numara/ETTN, indirme, iptal talebi. Bu yüzden **"gerçek fatura kesiliyor" denemez**; canlı gönderim kapalı kalır.
* **Eksik girdi:** GİB e-Arşiv TEST (veya canlı) kullanıcı kodu/parolası ve imza SMS'ini alacak telefon; ardından §6 protokolü.

## 13. SERDAR-AKIN/gib-earsiv-mcp-server değerlendirmesi ve gerçek-HTTP mock yığını (2026-10-12)

**Proje:** https://github.com/SERDAR-AKIN/gib-earsiv-mcp-server — MIT, 8 commit, son 2026-06-23, Python ≥3.14 (`fastmcp`, `httpx`, `weasyprint`), testler pytest. **Python bu makinede yok; proje çalıştırılmadı.** Bir **MCP sunucusu**dur (LLM araç protokolü, stdio) — HTTP servisi değildir; `GIB_ENV=mock` yalnızca süreç içi `MockGibClient` sınıfını seçer ve **sabit, durumsuz örnek veriler** döner (sabit "Fatura başarıyla oluşturuldu", sabit liste, `mock_sms_oid_12345`). Bu yüzden .NET adaptörü doğrudan buna bağlanamaz ve taslak/imza durum geçişlerini sınayamaz. Kendi kodu da GİB'e karşı doğrulanmış değildir (README ve commit'lerde canlı test kanıtı yok).

**Karar:** Bu proje CRM'e bağlanmadı. Yerine, mevcut gateway'i **değiştirmeden**, ayrı bir test yardımcısı eklendi: `invoice-gateway/test/stack-server.mjs` — gerçek gateway + vendored f/fatura + **durumlu yerel sahte portal** (dış ağ çağrısı kodla engelli). `tests/.../GatewayRealHttpStackTests.cs`, CRM'in .NET adaptörünü bu sürece **gerçek HTTP** ile bağlar: taslak, mükerrer anahtar (aynı ETTN), durum, SMS challenge, yanlış kod (imzasız kalır), doğru kod (numara+ETTN eşleme), yanlış gateway token'ı (reddedilir), bilinmeyen anahtar (NotFound), portal 503/HTML (geçici hata + kör yeniden gönderim engeli), gateway kapalı (istisna değil geçici hata). Sonuç: entegrasyon testleri 133/133 geçti.

**Sınır:** Bunlar **mock yanıtlardır**; gerçek GİB yanıtı değildir ve imzalı resmî fatura kesildiğini **kanıtlamaz**. Sahte portalın komut/alan adları vendored kütüphaneden alındı; gerçek portalın bunlarla aynı davrandığı doğrulanmadı.
