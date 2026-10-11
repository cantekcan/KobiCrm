# e-Arşiv Ağ Geçidi (f/fatura)

CRM (.NET) ile **GİB e-Arşiv Portalı** arasında köprü. [`f/fatura`](https://github.com/f/fatura) kütüphanesini (MIT) sabitlenmiş kopya olarak kullanır.

> **Durum:** Sahte portala karşı 21 testle doğrulandı. **GİB TEST portalında uçtan uca doğrulanmadı** (TEST hesabı yok). **Resmî bir GİB API'si değildir**: portalın web arayüzünü otomatikleştirir; portal değişirse bozulabilir (08-2026'da olduğu gibi). Ayrıntı, riskler ve doğrulama protokolü: [`../docs/invoice-integration.md`](../docs/invoice-integration.md).

## Yapabildikleri / yapamadıkları

| Var | Yok |
|---|---|
| e-Arşiv **taslak** oluşturma | **e-Fatura** (yalnızca e-Arşiv) |
| Portalın kayıtlı telefonuna **SMS ile imzalama** | **İptalin sonuçlandığını doğrulama** (yalnızca iptal *talebi* oluşturulur; sonuç portalda teyit edilir) |
| Durum sorgusu, HTML belge görünümü | Otomatik/SMS'siz imzalama |
| `submissionKey` ile **mükerrer taslak koruması** | CAPTCHA/güvenlik kontrollerini aşma |

## Çalıştırma

```powershell
cd invoice-gateway
$env:GATEWAY_TOKEN = "<en az 32 karakter rastgele>"     # CRM'deki Invoice__Gateway__Token ile AYNI olmalı
$env:GIB_USER      = "<GİB e-Arşiv TEST kullanıcı kodu>"
$env:GIB_PASSWORD  = "<GİB TEST parolası>"
$env:GIB_ENV       = "TEST"                              # varsayılan TEST; PROD için ayrıca GIB_ALLOW_PROD=yes gerekir
node src/index.mjs                                       # http://127.0.0.1:5090 (yalnızca bu bilgisayar)
```

CRM tarafı (aynı makinede):

```powershell
$env:Invoice__Gateway__Url   = "http://127.0.0.1:5090"
$env:Invoice__Gateway__Token = $env:GATEWAY_TOKEN
$env:Invoice__Gateway__Environment = "TEST"              # ağ geçidinin GIB_ENV değeriyle aynı
```

Ardından CRM → **Ayarlar → Fatura yöntemi → EArsivGateway**. `PROD` kullanımı için ek olarak `Invoice__LiveSendingEnabled=true` gerekir; **TEST doğrulaması bitmeden açmayın**.

## Güvenlik

* Varsayılan olarak yalnızca `127.0.0.1` dinler; CRM uzak ağ geçidi adresini varsayılan olarak **reddeder**.
* `GATEWAY_TOKEN` yoksa/kısaysa/yer tutucuysa süreç başlamaz. GİB kimlik bilgileri yalnızca bu sürecin ortam değişkenlerindedir; yanıtlara ve günlüklere yazılmaz.
* Durum dosyası `data/gateway-state.json` (git'e girmez) gönderim kayıtlarını tutar; **silmeyin** — mükerrer belge korumasının hafızasıdır. Yedeğe dahil edin.
* Upstream kütüphane parolayı URL-encode etmez; bu kopyada **yamalandı** (testli); yine de özel karakterli parolayı gerçek TEST portalında deneyin.
* Docker: kök dizinde `docker compose --profile app --profile gateway up -d --build` (port yayınlamaz; durum `suttcrm-gateway` biriminde).

## Test

```powershell
npm test      # 21 test: kimlik doğrulama, eşleme/doğrulama, mükerrer koruması, ağ hatası, SMS imzalama, sessiz başarısızlık,
              # ve vendored f/fatura + SAHTE GİB (gerçek ağa çıkmaz) ile istek gövdesi kontrolü
```

## Kütüphane sürümü

`vendor/fatura/` altında commit `24cba00060c3678fd85b94da5aaffb7ea7d048f1` (2026-08-24) kaynak koddan derlenmiş kopya durur (`VENDORED.md`). npm'deki `0.2.1` sürümü bu commit'ten **önce** yayımlandığı için **kullanılmaz**. Güncelleme ancak TEST doğrulamasının yeniden koşulmasıyla yapılmalıdır.
