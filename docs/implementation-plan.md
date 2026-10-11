# Uygulama Planı, Durum ve Test Eşlemesi

Tarih: 2026-10-12. Bu doküman **tamamlanmış**, **test edilmiş** ve **doğrulanamamış** özellikleri ayrı tutar.

## 1. Aşamalar

| Aşama | Kapsam | Durum |
|---|---|---|
| 1 — Analiz | Depo incelemesi (boş), 5 doküman, varsayımlar | ✅ Tamam |
| 2 — Backend | Çözüm/proje yapısı, EF Core + PostgreSQL, migration, müşteri/ürün/satış/açılış bakiyesi/cari/tahsilat/fatura taslağı API'leri, doğrulama ve hata yönetimi, testler | ✅ Tamam |
| 3 — Frontend | Giriş, genel bakış, müşteri/ürün/satış/cari/tahsilat/fatura/stok/tedarikçi/rapor/ayarlar ekranları, duyarlı tasarım, hata/boş durumlar | ✅ Tamam (elle doğrulandı; bkz. §3) |
| 4 — Fatura sağlayıcısı | f/fatura incelemesi, `IInvoiceProvider`, Manuel + Mock + e-Arşiv ağ geçidi adaptörü, canlı gönderim kapalı | ✅ Kod+sahte portal testi · ⚠️ **GİB TEST'inde doğrulanmadı** |
| 5 — Test ve teslim | Derleme, otomatik testler, migration doğrulama, yerel + Docker çalıştırma, uçtan uca senaryo, yedekleme/geri yükleme provası | ✅ Tamam |

## 2. Özellik durumu

**Tamamlandı ve otomatik testli (gerçek PostgreSQL ile):** müşteri/ürün CRUD ve doğrulamaları; müşteriye özel fiyat ve fiyat donması; satış hesapları (indirim+KDV, sunucuda); satış onayı → cari + stok (tek işlem); açılış bakiyesi (tekil, satış sayılmaz); tahsilat (kısmi, çoklu dağıtım, otomatik FIFO, geri alma); cari ekstre ve yürüyen bakiye; vadesi geçmiş hesabı; satış iptali ve ters kayıtlar; fatura taslağı/düzenleme/iç onay/manuel resmî kayıt/taslak iptali/resmî iptal akışı; iç–resmî durum ayrımı; mükerrer resmî numara engeli; sağlayıcı akışında ağ hatası güvenliği; stok hareketleri; alış/tedarikçi; dashboard ve rapor tanımları; kimlik/yetkilendirme; dosya yükleme doğrulaması; transaction geri alma; idempotans.

**Tamamlandı, elle doğrulandı (tarayıcıda gerçek kullanımla):** giriş (hatalı parola mesajı), genel bakış, müşteri listesi/detayı (cari ekstre), yeni satış (müşteri özel fiyatı, indirim, canlı toplam = sunucu toplamı), fatura taslağı → alan düzenleme → iç onay penceresi → "resmî belge yok" durumu; 22 ana ekranın 375 px mobil düzen ölçümü; demo bandı. Docker imajı derlendi, konteyner sağlıklı (root olmayan kullanıcı), migration'lar sıfırdan uygulandı, duman testi konteynere karşı geçti.

**Kısmen / kısıtlı:** PDF üretimi (tarayıcıdan "Yazdır → PDF"); fatura listesinden ek indirme (yalnızca detay ekranından); mobil test otomatik tarayıcı testi değil, elle çalıştırılan ölçüm betiğidir.

**DOĞRULANMADI (özellikle):**
* e-Arşiv ağ geçidinin **GİB TEST/PROD portalıyla** çalışması (TEST hesabı yok). Yalnızca sahte portalla test edildi. → `invoice-integration.md` §6 protokolü.
* Ağ geçidindeki alan eşlemelerinin portal tarafında doğru yorumlanması (özellikle indirimli kalemler, birim kodları).
* LUCA ile herhangi bir otomatik etkileşim (denenmedi).
* İşletmenin e-Fatura/e-Arşiv mükellefiyeti ve uygun resmî kanal (bilinmiyor; varsayılmadı).
* KDV oranlarının güncelliği (kullanıcı tanımlı; muhasebeci teyidi gerekir).

## 3. 30 senaryolu test eşlemesi

Toplam otomatik test: **222** (birim 46 + entegrasyon 129 + arayüz 26 + ağ geçidi 21). Komutlar README → Testler.

| # | Senaryo | Testler (özet) |
|---|---|---|
| 1 | Müşteri oluşturma/düzenleme | `Customer_can_be_created_updated_and_listed`, `Customer_validation_rejects_bad_ids_and_duplicates`, `Customer_opening_balance_cannot_be_changed_by_edit` |
| 2 | Ürün oluşturma | `Product_can_be_created_with_unit_vat_and_stock_tracking`, `Product_rejects_unknown_vat_rate_unit_and_duplicate_code`, `Vat_rates_are_configurable_not_hardcoded`, `Product_unit_cannot_change_after_movements` |
| 3 | Müşteriye özel fiyat | `Customer_specific_price_is_used_and_overridable_and_history_is_frozen` |
| 4 | Satış oluşturma | `Sale_totals_are_computed_by_backend_with_discount_and_vat`, `Sale_rejects_invalid_input`, `Draft_sale_does_not_touch_ledger_or_stock_until_approved`, `Repeated_request_with_same_client_key_returns_same_sale`, `Concurrent_requests_with_same_client_key_create_only_one_sale`, `Cash_sale_*` |
| 5 | Toplam hesabı | `DocumentCalculatorTests.*`, `Sale_totals_are_computed_by_backend…`, web `format.test.ts` (aynı örnekler) |
| 6 | KDV ve indirim | `Line_applies_percent_and_amount_discount_then_vat`, `Vat_is_rounded_half_away_from_zero_to_kurus`, `Document_total_is_sum_of_rounded_lines`, `Invalid_lines_are_rejected` |
| 7 | 100.000 TL açılış borcu | `Opening_balance_of_100000_is_recorded_as_ledger_entry_without_invoice_or_sale` |
| 8 | Açılış, raporlarda satış değil | `Opening_balance_is_not_reported_as_a_new_sale_anywhere` (gün/hafta/ay/müşteri/ürün raporları, dashboard, ölçüler) |
| 9 | Açılışın iki kez girilememesi | `Opening_balance_cannot_be_entered_twice`, `Database_itself_blocks_a_second_opening_entry` |
| 10 | 25.000 TL tahsilat | `Payment_of_25000_reduces_balance_from_100000_to_75000`, `Spec_example_opening_plus_sale_minus_payment` (95.000) |
| 11 | Kalan bakiye | aynı testler + `Overdue_amount_counts_expired_items_and_offsets_unallocated_credit`, `Customer_list_shows_balance_overdue_and_last_dates_and_filters` |
| 12 | Kısmi tahsilat | `Partial_payments_on_a_25000_sale_leave_15000_then_zero`, `Cash_sale_partial_immediate_payment_is_partially_paid` |
| 13 | Bir tahsilatı birden çok faturaya/satışa dağıtma | `One_payment_can_be_split_across_multiple_sales_with_separate_allocation_rows`, `Allocation_rules_are_enforced`, `Auto_allocation_pays_oldest_due_sale_first`, `Payment_allocation_remembers_the_invoice_of_the_sale` |
| 14 | Aynı resmî faturanın ikinci kez içeri alınmaması | `Same_official_invoice_cannot_be_imported_twice` (servis + DB kısıtı) |
| 15 | Aynı satışın iki kez işlenmemesi | `Same_sale_cannot_be_processed_twice_in_ledger_or_stock`, `Database_blocks_duplicate_ledger_and_stock_rows_for_same_source`, `Draft_sale_can_be_approved_once_only` |
| 16 | Taslak fatura oluşturma | `Draft_invoice_is_created_from_sale_with_snapshot_and_no_official_data`, `Draft_can_only_be_created_from_approved_sale_and_only_once`, `Draft_creation_is_idempotent_with_client_key` |
| 17 | Taslağı düzenleme | `Draft_can_be_edited_and_edits_are_validated`, `Internal_approval_requires_valid_buyer_and_document_type` |
| 18 | Taslağı iç süreçte iptal | `Draft_can_be_cancelled_internally_and_leaves_audit_trail_and_no_financial_effect`, `Cancelling_a_sale_cancels_its_unissued_invoice_draft_automatically` |
| 19 | Resmî durum ≠ iç süreç durumu | `Internal_approval_and_official_record_are_separate_and_never_change_the_ledger`, `Internal_approval_does_not_create_official_document`, `Manual_official_record_is_flagged_as_user_declared`, `Api_serializes_enums_as_strings_and_exposes_separate_invoice_statuses` |
| 20 | Resmî iptal doğrulanmadan "iptal edildi" olmaz | `Official_cancellation_is_not_applied_without_confirmation`, `Official_cancelled_requires_verified_confirmation`, `Cannot_skip_cancellation_request`, `Provider_cancellation_is_only_accepted_when_provider_confirms`, `Gateway_issued_invoice_cancellation_is_user_declared…` |
| 21 | Satış–cari tutarlılığı | `Ledger_balance_always_equals_opening_plus_sales_minus_payments` |
| 22 | İptallerin finansal etkisi | `Cancelling_sale_reverses_ledger_and_stock_and_keeps_payment_as_credit`, `Reversing_a_payment_restores_debt_and_keeps_audit_trail`, `Confirmed_official_cancellation_with_sale_cancel_applies_all_effects_consistently`, `Cancelled_purchase_reverses_supplier_debt_and_stock` |
| 23 | Stok giriş/çıkış | `Stock_in_out_return_adjustment_and_low_stock_alert`, `Stock_goes_down_on_sale_and_is_blocked_or_warned_when_insufficient`, `Products_with_stock_tracking_off_are_not_checked_or_moved`, `Manual_stock_rules_are_validated` |
| 24 | Dashboard toplamları | `Dashboard_cards_show_defined_totals_excluding_drafts_cancellations_and_opening`, `Dashboard_invoice_cards_separate_draft_awaiting_and_monthly_created` |
| 25 | Aylık satış raporu | `Monthly_sales_report_groups_by_month_and_excludes_opening_drafts_and_cancelled`, `Sales_report_supports_day_week_customer_and_product_grouping`, `Measures_report_keeps_sales_invoices_and_collections_separate` |
| 26 | Yetkisiz erişimin engellenmesi | `Every_endpoint_except_login_and_health_requires_authentication`, `Unauthenticated_requests_get_401_and_no_data` (15 uç), `Garbage_wrong_key_and_tampered_tokens_are_rejected`, `Login_failure_is_generic_and_account_locks_after_repeated_failures` |
| 27 | Hatalı dosya yükleme | `FileValidatorTests.*` (uzantı, imza, boş, büyük, bozuk/XXE XML), `Invalid_uploads_are_rejected_with_400_and_valid_ones_are_stored`, `Pdf_and_xml_attachments_can_be_linked…`, `Receipt_upload_validates_content…` |
| 28 | Veritabanı transaction hatası | `Database_failure_rolls_back_everything_including_sequence_number` |
| 29 | Ağ hatasında mükerrer fatura oluşmaması | `Network_timeout_after_draft_creation_does_not_create_duplicate_documents`, `Network_timeout_after_issue_is_resolved_by_status_query_without_second_document`, `Gateway_timeout_on_issue_leaves_result_unknown…`, `Unknown_submission_result_blocks_resubmission`; ağ geçidi: *"ağ hatası: sonuç temporary, yeniden gönderim mükerrer taslak oluşturmaz"* |
| 30 | Mobil arayüz kullanılabilirliği | **Otomatik değil.** `scripts/mobile-layout-check.js` ile 22 ekran 375 px'te ölçüldü: yatay taşma yok, küçük dokunma hedefi yok (bkz. §2). |

Ek güvenlik/dayanıklılık testleri: uygulama zayıf/yer tutucu yönetici parolası ya da JWT anahtarıyla **başlamaz**; ağ geçidi uzak adres/kısa token reddi; canlı-yetenekli sağlayıcıya `LiveSendingEnabled` kapalıyken **hiç çağrı yapılmadığı**; Manuel sağlayıcının her işlemi `NotSupported` döndürmesi.

## 4. Bilinen sınırlamalar ve kalan işler

**Fonksiyonel sınırlamalar (bilinçli, MVP):**
1. Tek işletme, tek kullanıcı; rol yok.
2. Onaylı satış ve faturadaki tutarlar düzenlenemez (hata → iptal et, yeniden gir). Kısmi fatura / bir satışa birden çok aktif fatura yok.
3. Açılış bakiyesi sonradan yalnızca "Düzeltme" hareketiyle değişir.
4. Müşteri bazında fiyat listesi ve çoklu adres yok; yalnızca müşteri–ürün fiyatı.
5. Tevkifat, ÖTV, döviz, e-İrsaliye, e-SMM, e-Defter yok.
6. Birim dönüşümü yok (1 koli = X kg gibi); her ürün tek birimle.
7. LUCA'dan veri içe aktarma yok; müşteri/borç listesi elle veya açılış bakiyesiyle girilir.
8. PDF üretimi tarayıcıya bırakıldı; e-posta/SMS bildirimi yok.
9. Fatura listesinde ek indirme yok (detay ekranında var).

**Teknik borç / izlenecekler:**
* Cari bakiye hesabı müşteri başına tüm hareketleri belleğe alır; çok büyük hacimde SQL penceresine taşınmalı.
* Arayüzde tarayıcı otomasyon testi (Playwright) yok; mobil kontrol elle çalıştırılan betiktir.
* `f/fatura` portal otomasyonudur: sessiz bozulma riski kalıcıdır; ayda bir test faturası önerilir.
* Oturum token'ı `localStorage`'da (CSP ile korunur); çıkışta sunucu tarafı iptal listesi yok (12 saat geçerli).
* Bu geliştirme ortamında Windows'un `MAX_PATH` ve MSIX yol sanallaştırması nedeniyle Vite dev sunucusu çalıştırılamadı; arayüz üretim derlemesiyle (API'nin `wwwroot`'u) doğrulandı. Normal bir klasörde `npm run dev` beklenen şekilde çalışmalıdır.

**Önerilen sonraki adımlar (öncelik sırasıyla):**
1. Muhasebeciyle: e-Fatura/e-Arşiv mükellefiyeti, doğru KDV oranları, resmî iptal prosedürleri teyidi.
2. GİB TEST e-Arşiv hesabı edinip `invoice-integration.md` §6 protokolünü koşmak; sonuçları kaydetmek.
3. Gerçek müşteri/borç listesiyle açılış bakiyelerini girip LUCA bakiyeleriyle karşılaştırmak (ilk hafta paralel çalışma).
4. Günlük yedek görevini kurmak ve ayda bir geri yükleme provası yapmak.
5. İhtiyaca göre: toplu müşteri içe aktarma (CSV), PDF/e-posta, çoklu fatura/kısmi fatura.
