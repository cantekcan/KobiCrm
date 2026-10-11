using Microsoft.EntityFrameworkCore;
using SutTicaretCRM.Application;
using SutTicaretCRM.Application.Services;
using SutTicaretCRM.Domain;

namespace SutTicaretCRM.Api;

/// <summary>
/// Demo veri yükleyici. YALNIZCA veritabanı modu "Demo" olan (Database:Mode=Demo) ve içinde müşteri bulunmayan veritabanına yazar.
/// Canlı veritabanına asla demo kaydı eklemez. Tüm kayıtlar gerçek servisler üzerinden oluşturulur; böylece cari/stok kuralları aynen işler.
/// KDV oranları ve fiyatlar yalnızca ÖRNEKTİR.
/// </summary>
public static class DemoData
{
    public static async Task SeedAsync(IServiceProvider sp, ILogger logger)
    {
        var settings = sp.GetRequiredService<SettingsService>();
        var db = sp.GetRequiredService<IAppDbContext>();
        if (await settings.GetDatabaseModeAsync() != "Demo")
        {
            logger.LogError("Demo veri yalnızca Database__Mode=Demo olan ayrı bir veritabanına yüklenebilir. Canlı veritabanı korunuyor; işlem iptal edildi.");
            return;
        }
        if (await db.Customers.AnyAsync())
        {
            logger.LogWarning("Veritabanında zaten müşteri var; demo veri yüklenmedi.");
            return;
        }

        var clock = sp.GetRequiredService<IClock>();
        var products = sp.GetRequiredService<ProductService>();
        var customers = sp.GetRequiredService<CustomerService>();
        var suppliers = sp.GetRequiredService<SupplierService>();
        var sales = sp.GetRequiredService<SaleService>();
        var payments = sp.GetRequiredService<PaymentService>();
        var invoices = sp.GetRequiredService<InvoiceService>();
        var stock = sp.GetRequiredService<StockService>();
        var today = clock.Today;

        await settings.SetAsync(SettingsService.CompanyName, "DEMO Süt Ticaret (örnek firma)");
        await db.SaveChangesAsync();

        var cat = await products.CreateCategoryAsync("Süt ürünleri");
        async Task<ProductDto> P(string code, string name, string unit, decimal buy, decimal sell, decimal min) =>
            await products.CreateAsync(new ProductRequest(code, name, cat.Id, unit, buy, sell, 10m, true, min, "Örnek ürün (demo)"));
        var sut = await P("SUT-1L", "Süt 1 L", "Litre", 18m, 24m, 50);
        var yogurt = await P("YOG-1K", "Yoğurt 1 kg", "Kg", 30m, 42m, 40);
        var peynir = await P("PEY-BEY", "Beyaz peynir", "Kg", 150m, 200m, 30);
        var kasar = await P("PEY-KSR", "Kaşar peyniri", "Kg", 240m, 310m, 20);
        var ayran = await P("AYR-KOLI", "Ayran koli", "Koli", 120m, 165m, 10);

        var supplier = await suppliers.CreateAsync(new SupplierRequest("DEMO Çiftlik Süt Kooperatifi", null, null, "0555 000 00 00", null, null, "Örnek tedarikçi",
            true, new OpeningBalanceRequest(35_000m, today.AddDays(-60), null, "Devreden borç (demo)")));
        await suppliers.CreatePurchaseAsync(new PurchaseRequest(supplier.Id, today.AddDays(-30), today, "DEMO-001", null,
        [
            new PurchaseItemRequest(sut.Id, 400, 18m, null), new PurchaseItemRequest(yogurt.Id, 250, 30m, null), new PurchaseItemRequest(peynir.Id, 150, 150m, null),
            new PurchaseItemRequest(kasar.Id, 60, 240m, null), new PurchaseItemRequest(ayran.Id, 40, 120m, null),
        ], true, null));

        var a = await customers.CreateAsync(new CustomerRequest("A Market (demo)", CustomerType.Company, "Örnek VD", null, null, "0532 000 00 01", null, "Örnek Mah. 1. Sok.", "Ankara", "Çankaya",
            30, EInvoiceTaxpayerStatus.Unknown, "Geçmiş dönem bakiyesi olan örnek müşteri", true,
            new OpeningBalanceRequest(100_000m, today.AddDays(-90), today.AddDays(-45), "Geçmiş dönemden devreden bakiye (demo)")));
        var b = await customers.CreateAsync(new CustomerRequest("B Bakkal (demo)", CustomerType.Company, null, null, null, "0532 000 00 02", null, null, "Ankara", "Keçiören",
            0, EInvoiceTaxpayerStatus.Unknown, null));
        var c = await customers.CreateAsync(new CustomerRequest("C Restoran (demo)", CustomerType.Individual, null, null, null, "0532 000 00 03", null, null, "Ankara", "Yenimahalle",
            15, EInvoiceTaxpayerStatus.NotRegistered, null));

        await products.SetCustomerPriceAsync(a.Id, peynir.Id, 195m);
        await products.SetCustomerPriceAsync(b.Id, peynir.Id, 205m);

        SaleItemRequest Item(ProductDto p, decimal q, decimal disc = 0) => new(p.Id, q, null, disc, 0, null);

        await sales.CreateAsync(new SaleRequest(a.Id, today.AddDays(-20), PaymentType.Credit, null, null, [Item(peynir, 40), Item(yogurt, 60, 5)], true, null, null));
        await payments.CreateAsync(new PaymentRequest(a.Id, today.AddDays(-12), 25_000m, PaymentMethod.BankTransfer, "EFT-DEMO-1", "Kısmi ödeme (demo)", null, true, null));
        var s2 = await sales.CreateAsync(new SaleRequest(a.Id, today.AddDays(-3), PaymentType.Credit, null, null, [Item(sut, 120), Item(ayran, 8)], true, null, null));
        await sales.CreateAsync(new SaleRequest(b.Id, today.AddDays(-1), PaymentType.Cash, null, null, [Item(sut, 30), Item(peynir, 5)], true, null,
            new ImmediatePayment(1919.50m, PaymentMethod.Cash, null)));
        await sales.CreateAsync(new SaleRequest(c.Id, today, PaymentType.Credit, null, "Bugünkü teslimat", [Item(kasar, 12), Item(sut, 40)], true, null, null));
        await sales.CreateAsync(new SaleRequest(b.Id, today, PaymentType.Cash, null, "Taslak satış (henüz onaylanmadı)", [Item(yogurt, 10)], false, null, null));

        var draft = await invoices.CreateDraftFromSaleAsync(s2.Id, null);
        logger.LogInformation("Demo veri yüklendi. Örnek fatura taslağı: {Ref}. Demo'da RESMÎ fatura numarası üretilmez.", draft.InternalRef);
    }
}
