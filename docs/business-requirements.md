# İş Gereksinimleri, İş Kuralları ve Varsayımlar

Bu doküman, uygulamanın **gerçekte uyguladığı** kuralları anlatır. Her kural koda ve (varsa) veritabanı kısıtına karşılık gelir; ilgili test adı sağ sütundadır. Belirsiz/doğrulanamayan noktalar en altta ayrıca listelenmiştir.

## 1. Amaç ve kapsam

Süt ve süt ürünleri ticareti yapan, işletmeyi tek başına yöneten bir kişi için sade, mobil uyumlu, mümkün olduğunca ücretsiz bir ticari yönetim sistemi.

| Kapsamda (MVP) | Kapsam dışı (bilinçli) |
|---|---|
| Tek işletme, tek kullanıcı | Çok kullanıcı / rol yönetimi |
| Müşteri, ürün, satış, tahsilat, cari hesap | Muhasebe fişi, bilanço, gelir-gider |
| Fatura **taslağı**, iç onay, resmî belge kaydı | GİB'e doğrulanmış canlı gönderim (bkz. `invoice-integration.md`) |
| Basit stok, alış ve tedarikçi borcu | Parti/lot, SKT, çoklu depo, e-İrsaliye |
| Temel raporlar, demo/gerçek veri ayrımı | Tevkifat, ÖTV, döviz, e-SMM, e-Defter |

## 2. Kullanılan muhasebe dili (önemli)

Cari hesapta **Borç** müşterinin bize borcunu *artıran*, **Alacak** *azaltan* harekettir.

* Satış, açılış borcu, borç düzeltmesi → **Borç**
* Tahsilat, iade, alacak düzeltmesi → **Alacak**
* **Bakiye = Σ Borç − Σ Alacak.** Pozitif ise müşteri bize borçludur (arayüzde kırmızı); negatif ise müşteri avans/fazla ödeme yapmıştır ("alacaklı", yeşil).

Tedarikçi hesabı tersidir: alış tedarikçi hesabında **Alacak** (borcumuz artar), ödeme **Borç** (azalır). Bakiye = Σ Alacak − Σ Borç; pozitif = tedarikçiye borcumuz var. Müşteri ve tedarikçi hesapları ayrı tablolardadır; birbirini etkilemez.

## 3. Modüller ve kurallar

### 3.1 Müşteri

| Kural | Test |
|---|---|
| Yalnızca ad zorunludur. VKN (10 hane, sağlama toplamı), T.C. kimlik no (11 hane, sağlama) girildiyse geçerli olmalı. | `Customer_validation_rejects_bad_ids_and_duplicates` |
| Aynı VKN / T.C. no ile ikinci müşteri açılamaz (benzersiz indeks). | aynı test |
| e-Fatura mükellefiyeti `Bilinmiyor / Mükellef / Mükellef değil`. Varsayılan **Bilinmiyor**; sistem bunu hiçbir zaman "mükellef değil" diye yorumlamaz. | `Document_type_conflicting_with_known_einvoice_status_is_blocked_and_unknown_status_warns` |
| Pasif müşteriye yeni satış yapılamaz. | kod: `SaleService.LoadCustomerAsync` |
| Ödeme vadesi (gün) vadeli satışta vade tarihini **önerir**. | `Sale_totals_are_computed_by_backend…` |

### 3.2 Açılış bakiyesi (geçmişten gelen borç)

* Müşteri kaydedilirken ya da sonradan **"Açılış bakiyesi gir"** ile girilir. Tutar, tarih, isteğe bağlı vade ve açıklama alır. Her harekete iç referans verilir (`ACL-2026-000001`).
* **Fatura veya satış oluşturmaz.** Cari hesapta tek bir `OpeningBalance` hareketi olur. Satış/fatura raporlarına, dashboard satış kartlarına ve ürün geçmişine **girmez**; yalnızca bakiye/alacak ve vadesi geçmiş hesaplarında görünür.
* Müşteri başına **bir kez**: servis `409` döner, ayrıca veritabanı kısmi benzersiz indeksi (`IX_CustLedger_OneOpening`) çift kaydı engeller. Düzeltme "Düzeltme" hareketiyle yapılır (orijinal değişmez).
* Testler: `Opening_balance_of_100000_is_recorded_as_ledger_entry_without_invoice_or_sale`, `Opening_balance_is_not_reported_as_a_new_sale_anywhere`, `Opening_balance_cannot_be_entered_twice`, `Database_itself_blocks_a_second_opening_entry`, `Correction_after_opening_is_an_adjustment_entry_not_an_edit`.

### 3.3 Ürün ve fiyat

* Kod benzersiz. Birim ayarlardaki listeden seçilir (Adet, Koli, Kg, Gram, Litre; genişletilebilir). **Birimler arası otomatik dönüşüm yoktur**; hareket görmüş ürünün birimi değiştirilemez.
* Fiyatlar **KDV hariç**, `decimal(18,4)`. KDV oranı **sabit kodlanmaz**: yalnızca Ayarlar'daki izin verilen oranlardan seçilir (varsayılan liste 0, 1, 10, 20 yalnızca başlangıç değeridir, **doğrulanmış mevzuat bilgisi değildir**; muhasebeciyle teyit edilmelidir).
* **Müşteriye özel fiyat**: müşteri–ürün başına tek fiyat. Satışta fiyat girilmezse özel fiyat, yoksa varsayılan fiyat kullanılır; kullanıcı satırda değiştirebilir. **Satış kalemi fiyatı, ürün adı ve KDV oranı satış anında kalemde saklanır**; sonradan ürün/fiyat değişse geçmiş satış değişmez (`Customer_specific_price_is_used_and_overridable_and_history_is_frozen`).
* Varsayılan fiyat listesi (müşteri başına liste) yoktur; yalnızca müşteri–ürün fiyatı vardır.

### 3.4 Satış

Akış: müşteri → tarih → ürünler → miktar/fiyat/indirim → ödeme şekli → kaydet. İki kayıt biçimi vardır:

* **Taslak:** cariyi ve stoğu **etkilemez**; düzenlenebilir.
* **Kaydet ve onayla:** satış `Onaylandı` olur; müşteri cari hesabına **tek** `Satış` borç hareketi yazılır; stok takipli ürünler için `Satış` stok hareketi yazılır. Peşin/vadeli satışta isteğe bağlı tahsilat aynı işlemde girilebilir.

Hesaplama (sunucuda; istemciden tutar alınmaz):

```
brüt     = yuvarla(miktar × birim fiyat)
indirim  = yuvarla(brüt × indirim%) + indirim TL        (brüt'ü aşamaz)
net      = brüt − indirim
KDV      = yuvarla(net × KDV%)
satır    = net + KDV
belge    = satırların toplamı        (yuvarla = 2 hane, yarım yukarı / sıfırdan uzağa)
```

* Satış ile ondan türetilen fatura aynı satır değerlerini taşıdığı için toplamlar her zaman eşittir.
* Durum: `Taslak → Onaylandı → Kısmen tahsil edildi → Tahsil edildi`; `İptal edildi`. Tahsil durumu, **aktif tahsilat dağıtımlarından** yeniden hesaplanır.
* Aynı satış iki kez işlenemez: durum kontrolü + `IX_CustLedger_SourceOnce` + `IX_Stock_SourceOnce`. Çift tıklama/ağ hatası için istemci anahtarı (`ClientRequestId`) aynı isteği tekrar işlemez.
* **İptal:** cariye *ters kayıt*, stoğa ters hareket; bu satışa yapılan tahsilatlar silinmez, **dağıtılmamış bakiye** olarak kalır; henüz resmîleşmemiş fatura taslakları iptal edilir. Resmî belgesi olan satış iptal **edilemez** (önce resmî iptal süreci).
* Onaylı satış düzenlenemez; hata varsa iptal edip yeniden girilir (bilinen sınırlama).
* Stok yetersizse varsayılan: **uyarı** ile devam. Ayarlardan "yetersiz stokta engelle" seçilebilir.

### 3.5 Tahsilat

* Alanlar: müşteri, tarih, tutar, yöntem (Nakit / Havale-EFT / Diğer), banka referansı, açıklama, isteğe bağlı dekont (PDF/JPG/PNG ≤ 5 MB, içerik doğrulanır).
* **Cari etkisi tahsilatın kendisindendir**: tahsilat anında bakiye düşer. "Dağıtım" yalnızca *hangi satışın* kapandığını gösterir; bakiyeyi değiştirmez.
* Dağıtım biçimleri: **Otomatik** (en eski vadeden başlayarak), **Elle seç**, **Genel bakiyeden mahsup** (hiçbir satışa bağlama; örn. açılış borcuna sayılır). Bir tahsilat birden çok satışa dağıtılabilir; her dağıtım ayrı kayıttır (`PaymentAllocations`). Kalan tutar sonradan dağıtılabilir.
* **Fatura ile ilişki:** dağıtım **satışa** yapılır; satışın aktif faturası varsa dağıtım kaydına fatura da bağlanır. Faturanın "tahsil edilen / kalan" değeri bağlı satışın dağıtımlarından hesaplanır (varsayım A7: satış başına tek aktif fatura). Yani "bir tahsilatı birden çok faturaya dağıtmak" = birden çok satışa dağıtmak ve her satırda o satışın faturasını izlemektir.
* Doğrulamalar: dağıtım toplamı tahsilat tutarını, satış kalanını aşamaz; başka müşterinin/taslak/iptal satışa dağıtılamaz.
* **Silinmez, geri alınır:** durum `Geri alındı`, dağıtımlar pasifleşir (`IsActive=false`, zaman damgası), cariye *ters kayıt* yazılır; orijinal ve ters kayıt görünür kalır.
* Testler: `Payment_of_25000_reduces_balance_from_100000_to_75000`, `Partial_payments_on_a_25000_sale_leave_15000_then_zero`, `One_payment_can_be_split_across_multiple_sales_with_separate_allocation_rows`, `Allocation_rules_are_enforced`, `Reversing_a_payment_restores_debt_and_keeps_audit_trail`.

### 3.6 Cari hesap

Hareket türleri: Açılış bakiyesi, Satış, Satış iptali (ters kayıt), Tahsilat, Tahsilat geri alma (ters kayıt), İade, Düzeltme, Manuel. Her harekette tarih, tür, referans, açıklama, borç, alacak, **yürüyen bakiye**, kaydeden kullanıcı ve oluşturulma zamanı vardır. Ekstre tarih filtreli; "devreden bakiye" filtreden önceki hareketlerden hesaplanır. Hareketler **değiştirilmez ve silinmez**.

**Çift borç koruması:** Fatura hiçbir zaman cari hareket üretmez. Borç yalnızca onaylanan **satıştan** doğar; fatura aynı satışa bağlı bir belgedir (`Internal_approval_and_official_record_are_separate_and_never_change_the_ledger`).

**Vadesi geçmiş tutar** (müşteri başına):

1. Ters kaydı olmayan *borç* hareketleri açık kalemdir: satış (kalan = tutar − dağıtım), açılış bakiyesi, borç düzeltmeleri. Vade: satışta satış vadesi; diğerlerinde hareketin vadesi ya da tarihi.
2. Ters kaydı olmayan *alacak* hareketlerinden **dağıtılmamış** olanlar (genel mahsup tahsilatlar, iadeler, alacak düzeltmeleri) "mahsup edilmemiş alacak"tır.
3. `Vadesi geçmiş = max(0, vadesi dolmuş açık kalemler − mahsup edilmemiş alacak)`, bakiyeyi aşamaz.

Yani mahsup edilmemiş tahsilat, *önce en eski/vadesi geçmiş borca* sayılır (FIFO varsayımı). Test: `Overdue_amount_counts_expired_items_and_offsets_unallocated_credit`.

### 3.7 Fatura (kritik)

İki durum **ayrı alanlarda** tutulur ve arayüzde yan yana ama ayrı gösterilir:

| İç süreç durumu (CRM) | Resmî belge durumu |
|---|---|
| Taslak · İç onay verildi · Resmî işlem bekleniyor · Sağlayıcıda işlemde · İşlem hatası · Tamamlandı · İptal isteği gerekiyor · İptal edildi | Resmî belge yok · Sağlayıcıda taslak/işlemde · Sonuç doğrulanamadı · Resmî belge kayıtlı · Gönderim başarısız · Resmî olarak iptal edildi |

Kurallar:

* Fatura **yalnızca onaylı bir satıştan** hazırlanır; bir satış için aynı anda en fazla **bir aktif** fatura olabilir (`IX_Invoices_ActivePerSale`). Alıcı bilgisi faturada *donmuş kopya* olarak saklanır; kalemler/tutarlar satıştan kopyalanır ve faturada **değiştirilemez**. Düzenlenebilenler: tarih, vade, belge türü, alıcı bilgileri, not, kalem açıklamaları.
* **"Faturayı onayla" yalnızca iç onaydır.** Resmî belge oluşturmaz, GİB/LUCA'ya hiçbir şey göndermez, cariyi değiştirmez (`Internal_approval_and_official_record_are_separate…`). Onay penceresi bunu açıkça yazar.
* İç onaydan önce **doğrulama** çalışır: alıcı adı; geçerli VKN **veya** T.C. no; belge türü seçilmiş olmalı; müşteri "e-Fatura mükellefi" işaretliyse e-Arşiv seçilemez (ve tersi). Mükellefiyet *Bilinmiyor* ise bloklamaz ama **uyarı** verir.
* **Manuel akış (varsayılan, doğrulanmış yöntem):** kullanıcı belgeyi LUCA / GİB portalında düzenler, çıkan **resmî numarayı** (+ ETTN, tarih, tür) CRM'e kaydeder. Kayıt `Kullanıcı girişi · GİB'den doğrulanmadı` olarak etiketlenir. Aynı resmî numara/ETTN ikinci faturaya kaydedilemez (benzersiz indeksler `IX_Invoices_OfficialNumber/OfficialUuid`). Veritabanı, numarasız "Düzenlendi" durumunu reddeder (`CK_Invoices_IssuedHasNumber`).
* **Taslak iptali:** resmî belgesi olmayan fatura, nedenle iç süreçte iptal edilir; kayıt silinmez, denetim izi kalır; satış/cari etkilenmez.
* **Resmî fatura iptali:** `İptal talebi aç` yalnızca iptal ihtiyacını işaretler; **resmî durum "kayıtlı" kalır**. "Resmî olarak iptal edildi" ancak (a) kullanıcının portalda sürecin tamamlandığını açıkça doğrulayıp referans yazmasıyla *(kullanıcı beyanı, doğrulanmadı etiketiyle)* ya da (b) sağlayıcı hem iptal edebiliyor hem durum raporlayabiliyorsa **sağlayıcının "iptal edildi" demesiyle** olur. Bu adımda isteğe bağlı olarak bağlı satış da iptal edilir; fatura + satış + cari + stok **tek veritabanı işleminde** düzelir (`Confirmed_official_cancellation_with_sale_cancel_applies_all_effects_consistently`). Hangi resmî sürecin (iptal / itiraz / ret / düzeltme) uygulanacağı belge türüne göre değişir; uygulama bunu kendisi bilmez — muhasebeciyle teyit edilmelidir.
* **Taslak çıktısı/önizleme** her zaman "TASLAK — Resmî fatura değildir" filigranı taşır; yalnızca resmî numara kayıtlıysa filigran kalkar.

### 3.8 Stok

Stok miktarı = stok hareketlerinin toplamı (artış +, azalış −). Hareket türleri: açılış, alış, satış, iade, düzeltme, satış iptali, alış iptali. Takibi kapalı ürün için hareket yazılmaz ve kontrol yapılmaz. Minimum stok altı "azalan" listesine/dashboard'a düşer. Sayım düzeltmesinde fark otomatik hesaplanır. Aynı satış satırı iki kez stoktan düşemez (`IX_Stock_SourceOnce`).

### 3.9 Alış ve tedarikçi

Alış onayı: tedarikçi hesabına **Alacak** (borcumuz artar) + stok girişi. İptalde ters kayıt ve ters stok. Tedarikçiye ödeme tedarikçi hesabında **Borç** hareketidir. Açılış borcu (geçmişten kalan) tedarikçi başına bir kez girilebilir.

### 3.10 Raporlar ve ölçüler

Üç ölçü bilerek **ayrı** gösterilir (`Raporlar → Satış · Fatura · Tahsilat özeti`, Genel Bakış kartları):

| Ölçü | Tanım |
|---|---|
| **Ticari satış** | Onaylı (iptal/taslak olmayan) satışların KDV dahil tutarı; satış tarihine göre. Açılış bakiyesi **dahil değildir**. |
| **Düzenlenen resmî fatura** | Resmî belge bilgisi kayıtlı, iptal edilmemiş faturalar; resmî düzenlenme tarihine göre. Kullanıcı girişi olanlar için "doğrulanmamıştır" notu çıkar. |
| **Tahsilat** | Geri alınmamış tahsilatlar; tahsilat tarihine göre. |

Raporlar: günlük/haftalık(Pazartesi başlangıçlı)/aylık/tarih aralığı satış, müşteri ve ürün bazında satış, tahsilat, müşteri alacakları, vadesi geçmiş borçlar, cari ekstre, tedarikçi borçları, açık faturalar, fatura durumu, stok durumu. Her raporun ekranında "Bu rapor neyi ölçer?" açıklaması vardır.

### 3.11 Demo ve gerçek veri

Demo veri **ayrı bir veritabanındadır** (`Database:Mode=Demo`) ve her sayfada sarı "DEMO VERİTABANI" bandı görünür. Demo yükleyici, canlı modda çalışmayı reddeder ve boş olmayan veritabanına yazmaz. Canlı veritabanında sahte kayıt oluşmaz.

## 4. Güvenlik ve veri koruma kuralları

Güvenli giriş (PBKDF2-SHA256, 210 000 iterasyon; 5 hatalı denemede 15 dk kilit; genel hata mesajı), JWT (12 saat), tüm uçlar yetkili (yalnızca `login` ve `health` açık — testle doğrulanır), sunucu tarafı doğrulama, dosya içerik doğrulama (uzantı + imza + boyut; XML için DTD yasak), denetim kaydı (kim, ne zaman, ne), vergi no/telefon günlüğe yazılmaz, sırlar ortam değişkeninden gelir, **varsayılan yönetici parolası yoktur** (parola verilmeden uygulama başlamaz; yer tutucu/zayıf parola reddedilir). Ayrıntı: `architecture.md`.

## 5. Kritik varsayımlar (açıkça)

| # | Varsayım | Neden / etkisi |
|---|---|---|
| A1 | Tek işletme, tek kullanıcı. | İlk sürüm kapsamı. Veri modelinde işletme kimliği yoktur. |
| A2 | Para birimi TRY; tutarlar KDV hariç fiyat + KDV olarak tutulur. | Tevkifat/ÖTV/döviz yok. |
| A3 | Yuvarlama satır bazında kuruş, yarım yukarı. | Satış = fatura toplamı garantisi. Resmî sistemin yuvarlamasıyla küçük fark olabilir; ilk gerçek faturada kontrol edin. |
| A4 | "Bugün/bu ay" Türkiye saatine (Europe/Istanbul) göre. | Sunucu başka saat diliminde olsa bile gün sınırı doğru. |
| A5 | Peşin satışta vade = satış günü; vadeli satışta vade = satış tarihi + müşteri vadesi (değiştirilebilir). | Vadesi geçmiş hesabı buna dayanır. |
| A6 | Mahsup edilmemiş tahsilat önce vadesi geçmiş borca sayılır. | Müşteriye lehine ama mantıklı FIFO yorumu; raporda açıklanır. |
| A7 | Bir satış = en fazla bir aktif fatura; kısmi fatura/birden çok fatura yok. | Çift borç/çift fatura riskini kökten engeller; modelde `SaleId` ilişkisi çoğa genişletilebilir. |
| A8 | Faturada tutar düzenlenmez. | Fatura–satış tutarsızlığı oluşmasın. |
| A9 | Yetersiz stokta satış uyarıyla devam eder (ayarlanabilir). | Başlangıçta stok girişi eksik olabilir; satışı engellemek günlük işi aksatır. |
| A10 | KDV oranları kullanıcı tanımlı listedir. | Mevzuatı otomatik bildiğimizi varsaymıyoruz. |
| A11 | Birim dönüşümü yok. | "1 koli = ? kg" bilgisi güvenilir değil. |
| A12 | Mükellefiyet "Bilinmiyor" ise belge türü kullanıcıdan istenir. | Varsayım yapılmaz. |
| A13 | Manuel girilen resmî numara doğrulanmış kabul edilmez. | GİB'e erişim yok. |
| A14 | Müşteri adresi tek alan (CustomerAddresses tablosu yok). | Basitlik. |

## 6. Doğrulanamayan / bilinmeyen noktalar

* İşletmenin **e-Fatura mükellefiyeti, e-Arşiv kullanım koşulları, şirket türü, mali mühür/e-imza durumu, doğrudan GİB entegrasyonuna uygunluğu** bilinmiyor. Hiçbiri varsayılmadı; resmî gönderim kapalı, manuel akış varsayılan.
* e-Arşiv portalı otomasyonunun (f/fatura) **GİB TEST portalında uçtan uca çalıştığı doğrulanmadı** (test hesabı yok). Yalnızca sahte portalla birim/entegrasyon testi yapıldı (bkz. `invoice-integration.md`).
* Güncel KDV oranları ve belge türüne göre iptal/itiraz/ret/düzeltme süreci muhasebeci tarafından teyit edilmelidir.
* LUCA'dan veri içe aktarma yapılmadı; mevcut müşteri/borç listesi elle ya da açılış bakiyesiyle girilir.
