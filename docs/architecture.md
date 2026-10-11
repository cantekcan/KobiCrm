# Mimari

## 1. Genel görünüm

```
 Tarayıcı (telefon / tablet / bilgisayar)
        │  HTTPS (ters vekil ile; bkz. README → HTTPS)
        ▼
 ┌──────────────────────────── ASP.NET Core (.NET 10 LTS) ───────────────────────────┐
 │  SutTicaretCRM.Api  ── JWT, hata eşleme, Swagger, statik web (wwwroot), güvenlik başlıkları │
 │        │                                                                            │
 │  SutTicaretCRM.Application  ── servisler (iş kuralları), DTO'lar, soyutlamalar        │
 │        │        (IAppDbContext, IClock, ICurrentUser, INumberSequence, IFileStore,    │
 │        │         IInvoiceProvider)                                                    │
 │  SutTicaretCRM.Domain  ── varlıklar, durum makineleri, hesaplayıcı, VKN/TCKN doğrulama │
 │        ▲                                                                              │
 │  SutTicaretCRM.Infrastructure ── EF Core + Npgsql, migration, numara üretici,         │
 │           dosya deposu, parola özeti, fatura sağlayıcıları (Manual, Mock, Gateway)    │
 └────────────┬────────────────────────────────────────────────┬─────────────────────────┘
              │ SQL                                            │ HTTP (yalnızca loopback)
              ▼                                                ▼
        PostgreSQL 16                              invoice-gateway (Node.js, f/fatura)
        (docker compose)                           → GİB e-Arşiv portalı (TEST / PROD)
```

* **React + TypeScript + Vite** (`SutTicaretCRM.Web`) derlenir ve API'nin `wwwroot` klasöründen sunulur: tek adres, tek süreç.
* Katman bağımlılığı tek yönlüdür (Api → Application/Infrastructure → Domain). `Application`, EF Core'a `IAppDbContext` üzerinden erişir; böylece servisler gerçek PostgreSQL'e karşı test edilebilir.
* Mimari bilerek sade tutuldu (CQRS/MediatR yok). Ayrıştırılanlar: **finansal kurallar** (`LedgerService`, `PaymentService`, `SaleService`), **durum makineleri** (`Invoice`, `Sale`, `Payment` varlık metotları) ve **fatura sağlayıcısı** (`IInvoiceProvider`).

## 2. Proje yapısı

| Proje | Sorumluluk |
|---|---|
| `src/SutTicaretCRM.Domain` | `Entities.cs` (varlıklar + durum geçişleri), `Calculation.cs` (satır/belge hesabı, VKN/TCKN), `Enums.cs` |
| `src/SutTicaretCRM.Application` | `Services/*` (Customer, Product, Sale, Payment, Ledger, Invoice, Stock, Supplier, Report/Dashboard, Settings), `InvoiceProviderContract.cs`, `Abstractions.cs` |
| `src/SutTicaretCRM.Infrastructure` | `Persistence/AppDbContext` (eşleme, kısıtlar), `Migrations`, `DbSeeder`, `Providers/*`, `Services/CoreServices` |
| `src/SutTicaretCRM.Api` | `Program.cs`, `Controllers/*`, `Infrastructure.cs` (kimlik, hata eşleme), `DemoData.cs`, `EnvFile.cs` |
| `src/SutTicaretCRM.Web` | React arayüz (`pages/*`, `components/*`, `lib/*`) |
| `invoice-gateway/` | Node.js e-Arşiv ağ geçidi (f/fatura'nın sabitlenmiş kopyasıyla) |
| `tests/*` | xUnit birim ve entegrasyon testleri (+ `invoice-gateway/test`) |

## 3. Finansal tutarlılığı sağlayan tasarım kararları

1. **Tek yazma yolu.** Müşteri borcu yalnızca `LedgerService` üzerinden, yalnızca satış onayı / tahsilat / iade / düzeltme / açılış ile değişir. Fatura hiçbir zaman cari yazmaz.
2. **Hareketler silinmez, ters kayıtlanır.** Satış iptali ve tahsilat geri alma, orijinal hareketi bırakıp `ReversesEntryId` ile bağlı ters hareket yazar. Bir hareket en fazla bir kez ters kayıtlanabilir (benzersiz indeks).
3. **Veritabanı son savunma hattıdır.** Kritik kurallar yalnızca kodda değil kısıt olarak da vardır (bkz. `database-design.md`): açılış bakiyesi tekilliği, kaynak başına tek cari/stok hareketi, aktif fatura tekilliği, resmî numara/ETTN tekilliği, tutar CHECK'leri. Testler kodu atlayıp doğrudan DB'ye yazarak kısıtların çalıştığını doğrular.
4. **Atomik işlemler.** Birden çok tabloyu etkileyen her işlem (satış onayı + cari + stok + peşin tahsilat; satış iptali; resmî iptal onayı + satış iptali) tek veritabanı işlemindedir. Hata olursa **numara dizisi dahil** hepsi geri alınır (`Database_failure_rolls_back_everything_including_sequence_number`).
5. **İdempotans.** `ClientRequestId` (satış, tahsilat, fatura taslağı, alış) — arayüz her form için bir kez üretir; ağ hatasında aynı istek tekrarlanınca yeni kayıt oluşmaz, eşzamanlı çift istek tek kayıt üretir (`Concurrent_requests_with_same_client_key_create_only_one_sale`).
6. **İyimser eşzamanlılık.** `Sale`, `Invoice`, `Payment`, `PurchaseDocument` üzerinde PostgreSQL `xmin` sürüm jetonu; çakışmada `409` ve "yenileyip tekrar deneyin".
7. **Para tipi.** `decimal`; veritabanında tutar `numeric(18,2)`, birim fiyat `(18,4)`, miktar `(18,3)`, oran `(5,2)`. Hesap tek yerde (`DocumentCalculator`), satır bazında kuruşa yuvarlanır.
8. **Sunucu hesaplar.** İstemci yalnızca miktar/fiyat/indirim/KDV oranı gönderir; toplamlar sunucuda yeniden hesaplanır. Arayüzdeki önizleme aynı algoritmayı kullanır (TypeScript tarafı ayrıca test edilir) ve kayıt sonrası sunucu değerleri gösterilir.
9. **Durum modelleri ayrıştırılmıştır.** `Invoice.InternalStatus` ve `Invoice.OfficialStatus` ayrı sütunlardır; geçişler yalnızca varlık metotlarıyla (`Approve`, `RecordOfficialDocument`, `RequestCancellation`, `ConfirmOfficialCancellation` …) yapılır ve geçersiz geçişler `DomainException` fırlatır.

## 4. Fatura sağlayıcı soyutlaması

`IInvoiceProvider` (bkz. `invoice-integration.md`): `ValidateInvoiceAsync`, `CreateDraftAsync`, `RequestIssueChallengeAsync`, `IssueAsync`, `GetInvoiceStatusAsync`, `GetInvoiceDocumentAsync`, `RequestCancellationAsync`. Her uygulama `Capabilities` ile gerçekte neyi yapabildiğini beyan eder; desteklenmeyen işlem `ProviderOutcome.NotSupported` döner — **asla sabit "başarılı" yanıt değil**. Etkin sağlayıcı Ayarlar'dan seçilir; varsayılan `Manual`.

Canlı gönderim emniyeti (üç kilit): (1) `Invoice:LiveSendingEnabled` yapılandırması varsayılan **false**; canlı-yetenekli sağlayıcı bu açılmadan hiç çağrılmaz; (2) her gerçek işlem için arayüzde belge + alıcı + tutar gösterilir ve açık onay (ayrıca sunucuda tutar/alıcı eşleşmesi) aranır; (3) taslak oluşturma ile resmî düzenleme (imzalama) **ayrı adımlardır**.

Ağ hatası koruması: gönderim niyeti (`Submitting`, `SubmissionKey`) **sağlayıcı çağrısından önce** veritabanına yazılır. Sonuç alınamazsa durum `ResultUnknown` olur, yeniden gönderim **engellenir**; yalnızca "Durumu sorgula" sağlayıcıdan gerçeği öğrenir. Böylece mükerrer resmî belge oluşmaz (`Network_timeout_after_*` testleri).

## 5. Güvenlik

| Konu | Uygulama |
|---|---|
| Kimlik doğrulama | JWT (HS256, 12 saat). Anahtar ≥ 32 karakter, yer tutucu kabul edilmez; aksi halde uygulama **başlamaz**. |
| Parola | PBKDF2-SHA256, 210 000 iterasyon, rastgele tuz, sabit zamanlı karşılaştırma. Kullanıcı yokluğu zamanlama/mesajla sızdırılmaz. 5 hatalı denemede 15 dk kilit; giriş ucu IP başına dakikada 10 istekle sınırlı. |
| İlk yönetici | Yalnızca yapılandırmadan (`Seed__AdminPassword`). **Varsayılan parola yok**; boş/kısa/yer tutucu/kolay parola → başlangıç hatası. |
| Yetkilendirme | Varsayılan politika "kimliği doğrulanmış kullanıcı"; yalnızca `POST /api/auth/login` ve `/health` anonim (reflection testi ile doğrulanır). |
| Girdi doğrulama | Sunucuda her alan; hata `ProblemDetails` (alan bazlı Türkçe mesaj). Beklenmeyen hatada dahili ayrıntı sızdırılmaz. |
| Dosya yükleme | İzinli uzantılar (dekont: pdf/jpg/png ≤ 5 MB; fatura eki: pdf/xml ≤ 10 MB), **imza (magic bytes) doğrulaması**, XML için DTD yasak (XXE), dosya adı sunucuda üretilir (yol enjeksiyonu yok), içerik özeti (SHA-256) ile aynı dosya iki kez eklenemez. |
| Gizlilik | Vergi no/telefon denetim kaydına ve günlüğe yazılmaz; EF "sensitive data logging" kapalı; müşteri verisi üçüncü taraflara gönderilmez. |
| Sırlar | Ortam değişkeni / `.env` (git'e girmez). `.env.example` yalnızca yer tutucu içerir. |
| HTTP başlıkları | `X-Content-Type-Options`, `X-Frame-Options: DENY`, `Referrer-Policy`, üretimde CSP (`default-src 'self'`). |
| HTTPS | Uygulama düz HTTP dinler; HTTPS bir **ters vekil** (Caddy/Nginx/Cloudflare Tunnel) ile sağlanır (README). `Https:Redirect` / `Https:Hsts` ayarları vardır. İnternete **vekilsiz** açmayın. |
| Fatura ağ geçidi | Yalnızca loopback; paylaşılan token (≥ 32 karakter); GİB kimlik bilgileri yalnızca ağ geçidi sürecinin ortamında. CRM veritabanında/arayüzde GİB parolası tutulmaz. Uzak ağ geçidi adresi varsayılan olarak **reddedilir**. |
| Oturum token'ı | Tarayıcıda `localStorage`. XSS riskine karşı CSP ve yalnızca kendi kaynaklı script; ortak/halka açık bilgisayarlarda "Çıkış yap" kullanılmalı. |

## 6. Dağıtım seçenekleri

1. **Yerel (önerilen başlangıç):** `docker compose up -d db` + `dotnet run` + `scripts/build-web.ps1` (README).
2. **Docker (tam):** `docker compose --profile app up -d --build` (API + web aynı imajda; PostgreSQL ayrı kalıcı birimde).
3. **Ücretsiz barındırma:** teknik olarak mümkündür ancak **önerilmez** (bkz. README → "Ücretsiz barındırma riskleri"): uyku modu, süreli/silinen ücretsiz veritabanları, yedek garantisi yok, mali veriler üçüncü tarafta.

## 7. Bilinen mimari sınırlar

* Tek işletme/tek kullanıcı varsayımı veri modeline işlenmiştir (işletme kimliği yok).
* Cari bakiye hesabı müşteri başına tüm hareketleri belleğe alır (binlerce hareket için yeterli; on binlerce müşteri/hareket için SQL penceresine taşınmalı).
* Bildirim (e-posta/SMS), PDF üreteci (tarayıcı "yazdır → PDF" kullanılır) ve LUCA'dan içe aktarma yoktur.
