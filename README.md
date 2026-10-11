# Süt Ticaret CRM

Süt ve süt ürünleri ticareti yapan, işletmesini tek başına yöneten biri için **sade, güvenli, telefon/tablet/bilgisayarda çalışan** cari takip, satış, tahsilat ve fatura **taslağı** yönetimi uygulaması.

* **Müşteri, ürün, satış, tahsilat, cari hesap, stok, tedarikçi, rapor** — tek uygulamada.
* **Geçmişten gelen borçlar** "Açılış bakiyesi" olarak girilir (fatura/satış oluşturmaz).
* **Fatura taslağı ≠ resmî fatura.** İç onay ile resmî düzenleme ayrı durumlardır; hiçbir durum GİB'den doğrulanmış gibi gösterilmez. Resmî fatura bugün **LUCA / GİB portalında manuel** düzenlenir, numarası CRM'e kaydedilir. Otomatik gönderim için ağ geçidi hazırdır ama **GİB TEST portalında doğrulanmadı ve kapalıdır** (bkz. [docs/invoice-integration.md](docs/invoice-integration.md)).
* **.NET 10 (LTS) · ASP.NET Core · EF Core · PostgreSQL 16 · React + TypeScript.**

> Bu bir ERP değildir; muhasebe programının yerini tutmaz. Finansal kayıtlar silinmez, ters kayıtla düzeltilir.

## İçindekiler

1. [Önkoşullar](#önkoşullar)
2. [Hızlı başlangıç (yerel)](#hızlı-başlangıç-yerel)
3. [Docker ile tam kurulum](#docker-ile-tam-kurulum)
4. [Demo veri](#demo-veri)
5. [Telefondan erişim ve HTTPS](#telefondan-erişim-ve-https)
6. [Yedekleme ve geri yükleme](#yedekleme-ve-geri-yükleme)
7. [Parola sıfırlama](#parola-sıfırlama)
8. [Testler](#testler)
9. [Migration](#migration)
10. [Geliştirme](#geliştirme)
11. [Ücretsiz barındırma riskleri](#ücretsiz-barındırma-riskleri)
12. [Doküman haritası ve bilinen sınırlamalar](#doküman-haritası-ve-bilinen-sınırlamalar)

## Önkoşullar

| Araç | Sürüm | Not |
|---|---|---|
| .NET SDK | 10.x | `dotnet --version` |
| Node.js | 20+ (22/24 önerilir) | web arayüzünü derlemek için |
| Docker Desktop | güncel | PostgreSQL için; **testler de Docker kullanır** |

## Hızlı başlangıç (yerel)

PowerShell, proje klasöründe:

```powershell
# 1) Gizli ayarlar. .env.example'ı KOPYALAYIP değerleri KENDİNİZ doldurun (varsayılan parola yoktur).
Copy-Item .env.example .env
notepad .env        # POSTGRES_PASSWORD, JWT_KEY (>=32 karakter), ADMIN_PASSWORD (>=10 karakter) yazın

# 2) Veritabanı (yalnızca bu bilgisayardan erişilir: 127.0.0.1:5433)
docker compose up -d db

# 3) Web arayüzünü derle ve API'ye kopyala
./scripts/build-web.ps1

# 4) Uygulamayı başlat (ilk açılışta migration'lar ve ilk yönetici otomatik oluşur)
dotnet run --project src/SutTicaretCRM.Api --urls http://localhost:5080
```

Tarayıcıda **http://localhost:5080** → kullanıcı adı `.env` içindeki `ADMIN_USERNAME` (varsayılan `admin`), parola `ADMIN_PASSWORD`.

* İlk açılışta **Ayarlar → Firma** bilgilerini girin; **KDV oranlarını muhasebecinizle doğrulayın** (varsayılan 0, 1, 10, 20 yalnızca başlangıç değeridir).
* Müşterileri eklerken geçmiş borcu **Açılış bakiyesi** alanına yazın.
* `.env` git'e eklenmez. `Seed__AdminPassword` yer tutucu/kısa/kolay ise uygulama **başlamaz** (bilerek).
* API belgesi (yalnızca Development): http://localhost:5080/swagger

## Docker ile tam kurulum

```powershell
Copy-Item .env.example .env ; notepad .env
docker compose --profile app up -d --build      # PostgreSQL + API + web (tek imaj)
```

* Uygulama: http://localhost:5080 (yalnızca bu bilgisayardan; ağa açmak için "Telefondan erişim"e bakın).
* Veritabanı ve yüklenen dosyalar adlandırılmış Docker birimlerindedir (`docker compose down` silmez; `down -v` **siler** — kullanmayın).
* Sağlık: `GET /health`. Günlükler: `docker compose logs -f api`. Başlangıçta görülen `libgssapi_krb5.so.2 … cannot open shared object file` satırı Npgsql'in isteğe bağlı Kerberos kütüphanesiyle ilgili zararsız bir iletidir.

## Demo veri

Demo veri **ayrı bir veritabanına** yüklenir ve her sayfada sarı **"DEMO VERİTABANI"** bandı görünür. Gerçek veritabanına asla yazmaz.

```powershell
docker compose exec -T db psql -U suttcrm -d postgres -c 'CREATE DATABASE suttcrm_demo'
$env:ConnectionStrings__Default = "Host=localhost;Port=5433;Database=suttcrm_demo;Username=suttcrm;Password=<POSTGRES_PASSWORD>"
$env:Database__Mode = "Demo"
dotnet run --project src/SutTicaretCRM.Api -- seed-demo       # örnek müşteri/ürün/satış/tahsilat/taslak fatura
dotnet run --project src/SutTicaretCRM.Api --urls http://localhost:5081
```

Demo verideki KDV oranları ve fiyatlar **örnektir**. Demo modunda resmî fatura numarası üretilmez. `Database__Mode=Live` olan veritabanında `seed-demo` reddedilir.

## Telefondan erişim ve HTTPS

Varsayılan olarak yalnızca bu bilgisayardan (`localhost`) erişilir. Telefonla kullanmak için seçenekler:

1. **Aynı Wi-Fi (en basit):** API'yi `--urls http://0.0.0.0:5080` ile başlatın, Windows Güvenlik Duvarı'nda 5080'i yalnızca **Özel ağ** için açın ve telefondan `http://<bilgisayarın-IP'si>:5080` adresine girin. Düz HTTP olduğu için yalnızca **güvendiğiniz ev/iş ağında** kullanın.
2. **İnternetten erişim (HTTPS şart):** uygulamayı doğrudan internete açmayın. Bir **ters vekil/tünel** kullanın: *Cloudflare Tunnel* (ücretsiz, port açmaz), *Caddy* (otomatik Let's Encrypt) ya da *Nginx*. Vekil `https://` sonlandırır ve `http://localhost:5080`'e iletir. Ardından `Https__Redirect=true` ve `Https__Hsts=true` ayarlayın.
3. Telefon tarayıcısında "Ana ekrana ekle" ile uygulama gibi kullanılabilir.

> İnternete açarsanız: güçlü parola, yedekleme ve güncellemeler sizin sorumluluğunuzdadır. Giriş ucu hız sınırlı ve 5 hatalı denemede 15 dk kilitlenir; yine de ek bir koruma (Cloudflare Access / IP kısıtı) önerilir.

## e-Arşiv ağ geçidi (21 test) (opsiyonel, DOĞRULANMADI)

CRM'den doğrudan GİB e-Arşiv faturası kesme kodu hazırdır ama **gerçek GİB hesabıyla doğrulanmamıştır** ve varsayılan olarak kapalıdır. Yalnızca **e-Arşiv** desteklenir; imza portalın SMS koduyla yapılır; imzalı belge için yalnızca portalda iptal **talebi** oluşturulabilir (iptalin sonuçlandığı doğrulanamaz; kullanıcı beyanıyla kaydedilir). `.env` içine `GATEWAY_TOKEN`, `INVOICE_GATEWAY_URL=http://gateway:5090`, `GIB_USER`, `GIB_PASSWORD`, `GIB_ENV=TEST` yazın (örnek: `.env.example`), sonra:

```powershell
docker compose --profile app --profile gateway up -d --build
```

CRM → Ayarlar → Fatura yöntemi → **EArsivGateway**. Önce `docs/invoice-integration.md` §6 TEST protokolünü uygulayın; canlı için ayrıca `GIB_ALLOW_PROD=yes` ve `INVOICE_LIVE_SENDING=true` gerekir. Ağ geçidi dışarı port açmaz; durum dosyası (`suttcrm-gateway` birimi) mükerrer fatura korumasının hafızasıdır, silmeyin.

## Yedekleme ve geri yükleme

> **Yedek almadığınız veri yoktur.** Veriler bu bilgisayardadır; disk bozulur, bilgisayar kaybolur, yanlış tuşa basılır. Yedekleri **bilgisayar dışında** da saklayın (harici disk / şifreli bulut). Yedekler müşteri ve vergi bilgisi içerir.

**Yedek al** (veritabanı + yüklenen dosyalar; bütünlüğü otomatik doğrulanır; son 14 yedek tutulur):

```powershell
./scripts/backup.ps1                  # yerel çalıştırma: veritabanı + src/SutTicaretCRM.Api/storage
./scripts/backup.ps1 -DockerFiles     # Docker kurulumunda dosya birimini de yedekler
```

Çıktı `backups/` klasörüne yazılır: `suttcrm-<tarih>.dump` (veritabanı) ve `files-<tarih>.zip|.tar.gz` (dekont/fatura PDF-XML).

**Geri yükle** (hedefteki veriyi DEĞİŞTİRİR; önce otomatik güvenlik yedeği alınır, "EVET" onayı ister; geri yüklerken API'yi durdurun):

```powershell
./scripts/restore.ps1 -File backups/suttcrm-20261011-120000.dump
# Önce ayrı bir veritabanında DENEYİN (üretim verisine dokunmaz):
./scripts/restore.ps1 -File backups/suttcrm-20261011-120000.dump -Database suttcrm_restore_test -Force
```

Yüklenen dosyalar için `files-*.zip` içeriğini `src/SutTicaretCRM.Api/storage/` klasörüne açın (Docker'da: `docker run --rm -v <birim>:/data -v ${PWD}/backups:/b alpine tar xzf /b/files-….tar.gz -C /data`).

**Geri yükleme provası (önerilir, ayda bir):** yukarıdaki "-Database suttcrm_restore_test" komutunu çalıştırıp müşteri sayısını ve bir müşterinin bakiyesini karşılaştırın. *(Bu depoda betikler demo veritabanıyla denendi: yedekten geri yüklenen kopyada kayıt sayıları ve bakiye toplamı birebir aynı çıktı.)*

**Otomatik günlük yedek (Windows Görev Zamanlayıcı):**

```powershell
$a = New-ScheduledTaskAction -Execute "powershell.exe" -Argument "-NoProfile -File `"$PWD\scripts\backup.ps1`"" -WorkingDirectory $PWD
$t = New-ScheduledTaskTrigger -Daily -At 23:30
Register-ScheduledTask -TaskName "SutCRM-Yedek" -Action $a -Trigger $t
```

## Parola sıfırlama

```powershell
$env:NEW_ADMIN_PASSWORD = "<yeni güçlü parola>"
dotnet run --project src/SutTicaretCRM.Api -- reset-admin-password        # veya: ... reset-admin-password <kullanıcı-adı>
```

Parola komut satırı argümanı değil ortam değişkeni olarak verilir; hesap kilidi de kaldırılır ve işlem denetim kaydına yazılır.

## Testler

```powershell
dotnet test SutTicaretCRM.sln                       # birim + entegrasyon (gerçek PostgreSQL'i Testcontainers ile açar → Docker çalışmalı)
cd src/SutTicaretCRM.Web ; npm test                  # arayüz hesap/sayı yardımcıları (sunucu hesabıyla aynı örnekler)
cd invoice-gateway ; npm test                        # e-Arşiv ağ geçidi (sahte portal; gerçek GİB çağrısı YOK)
node scripts/smoke.mjs                               # çalışan API'ye karşı uçtan uca duman testi (DEMO/geliştirme veritabanında!)
```

* Testler **hiçbir koşulda GİB'e gerçek fatura göndermez**; fatura sağlayıcısı testleri sahte sağlayıcı/sahte HTTP ile koşar. Gerçek entegrasyon **doğrulanmamıştır** (bkz. invoice-integration.md).
* Entegrasyon testleri her test için ayrı bir PostgreSQL veritabanı oluşturur ve migration'ları sıfırdan uygular.
* Mobil arayüz kontrolü (Test 30) otomatik tarayıcı testi değil, `scripts/mobile-layout-check.js` ile yapılan **elle çalıştırılan** bir ölçümdür (aşağıya bakın).

Mobil kontrol: uygulamaya giriş yapın, tarayıcıyı 375 px genişliğe getirin (F12 → cihaz modu), konsola `scripts/mobile-layout-check.js` içeriğini yapıştırın; her ekran için yatay taşma ve küçük dokunma hedefi sayısını raporlar.

## Migration

`docs/database-design.md` → "Migration komutları". Uygulama açılırken bekleyen migration'ları otomatik uygular (`Database:AutoMigrate`).

## Geliştirme

```powershell
dotnet run --project src/SutTicaretCRM.Api --urls http://localhost:5080        # API
cd src/SutTicaretCRM.Web ; npm install ; npm run dev                            # arayüz: http://localhost:5173 (/api → 5080'e yönlenir)
```

Yapılandırma sırası: `appsettings.json` < ortam değişkenleri (`ConnectionStrings__Default`, `Jwt__Key`, …) < `.env` (yalnızca boş kalan anahtarlar için, yerel geliştirme kolaylığı). Önemli anahtarlar:

| Anahtar | Anlam | Varsayılan |
|---|---|---|
| `ConnectionStrings:Default` | PostgreSQL bağlantısı | `.env`'deki POSTGRES_* |
| `Jwt:Key` | ≥ 32 karakter rastgele | **zorunlu** |
| `Seed:AdminUserName/AdminPassword` | İlk yönetici (yalnızca kullanıcı yokken) | parola **zorunlu** |
| `Database:Mode` | `Live` / `Demo` | Live |
| `Storage:Root` | Yüklenen dosyalar | `storage` |
| `Invoice:LiveSendingEnabled` | Canlı resmî gönderim | **false** |
| `Invoice:EnableMockProvider` | Sahte fatura sağlayıcısı (yalnızca geliştirme) | false |
| `Invoice:Gateway:Url/Token/Environment` | e-Arşiv ağ geçidi (opsiyonel) | kapalı |
| `Swagger:Enabled` | Üretimde Swagger | false |

## Ücretsiz barındırma riskleri

Bilgisayarınızda çalıştırmak (bu belgedeki yöntem) **ücretsiz**dir. Buluta taşımak istenirse **ücretsiz katmanlar mali veri için riskli**dir; kullanmadan önce güncel koşulları kendiniz doğrulayın (bu bilgiler sık değişir ve burada doğrulanmadı):

| Risk | Açıklama |
|---|---|
| Uyku / soğuk başlangıç | Ücretsiz web servislerinin çoğu hareketsizlikte uyur; ilk istek dakikalarca sürebilir. |
| Süreli/silinen veritabanı | Bazı sağlayıcılarda ücretsiz veritabanı belirli süre sonra silinir ya da hareketsizlikte duraklatılır. |
| Yedek garantisi yok | Ücretsiz katmanlarda geri yükleme noktası yok ya da çok kısa. **Kendi yedeğinizi alın** (`backup.ps1` mantığı; `pg_dump`). |
| Kota / bölge | Depolama (çoğunlukla yüzlerce MB), bağlantı ve işlem süresi sınırlıdır; sunucu çoğunlukla yurt dışındadır → **müşteri ve vergi verileri üçüncü tarafta/yurt dışında** durur (KVKK açısından muhasebeci/hukukla değerlendirin). |
| Süreklilik | Hizmet koşulları tek taraflı değişebilir; hesap askıya alınabilir. |
| Dosyalar | Yüklenen dekont/fatura ekleri yerel diskte tutulur; kalıcı disk yoksa kaybolur. |

Öneri: uygulamayı işletmenin kendi bilgisayarında/küçük bir mini-PC'de çalıştırıp **günlük yedek** almak, gerekirse yedeği şifreli bulut klasörüne kopyalamak.

## Doküman haritası ve bilinen sınırlamalar

| Doküman | İçerik |
|---|---|
| [docs/business-requirements.md](docs/business-requirements.md) | İş kuralları, varsayımlar, doğrulanamayanlar |
| [docs/architecture.md](docs/architecture.md) | Katmanlar, tutarlılık kararları, güvenlik |
| [docs/database-design.md](docs/database-design.md) | Tablolar, kısıtlar, migration |
| [docs/invoice-integration.md](docs/invoice-integration.md) | f/fatura incelemesi, sağlayıcı sözleşmesi, TEST doğrulama protokolü |
| [docs/implementation-plan.md](docs/implementation-plan.md) | Aşamalar, test eşlemesi, **bilinen sınırlamalar ve kalan işler** |
| [invoice-gateway/README.md](invoice-gateway/README.md) | e-Arşiv ağ geçidini çalıştırma |

Başlıca sınırlamalar (tam liste implementation-plan.md'de): tek kullanıcı; resmî fatura otomasyonu GİB TEST'inde doğrulanmadı; onaylı satış/fatura tutarı düzenlenemez (iptal edip yeniden girilir); LUCA'dan içe aktarma yok; PDF üreteci yok (tarayıcıdan yazdır); tevkifat/ÖTV/döviz/e-İrsaliye yok.
