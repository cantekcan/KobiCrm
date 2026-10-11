using Microsoft.EntityFrameworkCore;
using SutTicaretCRM.Application;
using SutTicaretCRM.Application.Services;
using SutTicaretCRM.Domain;

namespace SutTicaretCRM.IntegrationTests;

[Collection("pg")]
public class CustomerAndProductTests(PostgresFixture pg) : IntegrationTestBase(pg)
{
    [Fact] // Test 1
    public async Task Customer_can_be_created_updated_and_listed()
    {
        var c = await App.NewCustomer("Yıldız Market");
        Assert.Equal("Yıldız Market", c.Name);
        Assert.Equal(0, c.Balance);
        Assert.Equal(EInvoiceTaxpayerStatus.Unknown, c.EInvoiceStatus);

        var updated = await App.Use<CustomerService, CustomerDetailDto>(s => s.UpdateAsync(c.Id, new CustomerRequest(
            "Yıldız Market Ltd.", CustomerType.Company, "Çankaya VD", "1234567890", null, "0532 111 22 33", "info@yildiz.test", "Yeni adres", "Ankara", "Çankaya",
            45, EInvoiceTaxpayerStatus.Registered, "Not", true)));
        Assert.Equal("Yıldız Market Ltd.", updated.Name);
        Assert.Equal(45, updated.PaymentTermDays);
        Assert.Equal(EInvoiceTaxpayerStatus.Registered, updated.EInvoiceStatus);

        var list = await App.Use<CustomerService, PagedResult<CustomerListItem>>(s => s.ListAsync("yıldız", null, false, null, null, 1, 25));
        Assert.Single(list.Items);
    }

    [Fact]
    public async Task Customer_validation_rejects_bad_ids_and_duplicates()
    {
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<CustomerService, CustomerDetailDto>(s => s.CreateAsync(
            new CustomerRequest("", CustomerType.Company, null, null, null, null, null, null, null, null, 0, EInvoiceTaxpayerStatus.Unknown, null))));
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<CustomerService, CustomerDetailDto>(s => s.CreateAsync(
            new CustomerRequest("X", CustomerType.Company, null, "1234567891", null, null, null, null, null, null, 0, EInvoiceTaxpayerStatus.Unknown, null))));

        CustomerRequest R(string n) => new(n, CustomerType.Company, null, "1234567890", null, null, null, null, null, null, 0, EInvoiceTaxpayerStatus.Unknown, null);
        await App.Use<CustomerService, CustomerDetailDto>(s => s.CreateAsync(R("A")));
        await Assert.ThrowsAsync<ConflictException>(() => App.Use<CustomerService, CustomerDetailDto>(s => s.CreateAsync(R("B"))));
    }

    [Fact]
    public async Task Customer_opening_balance_cannot_be_changed_by_edit()
    {
        var c = await App.NewCustomer(opening: 5000m);
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<CustomerService, CustomerDetailDto>(s => s.UpdateAsync(c.Id, new CustomerRequest(
            c.Name, c.CustomerType, null, null, null, null, null, null, null, null, 0, EInvoiceTaxpayerStatus.Unknown, null, true,
            new OpeningBalanceRequest(1, new DateOnly(2026, 1, 1), null, null)))));
    }

    [Fact] // Test 2
    public async Task Product_can_be_created_with_unit_vat_and_stock_tracking()
    {
        var p = await App.NewProduct("SUT-1", sale: 24.50m, vat: 10m, unit: "Litre", stock: 100, min: 20);
        Assert.Equal("Litre", p.Unit);
        Assert.Equal(24.50m, p.SalePrice);
        var loaded = await App.Use<ProductService, ProductDto>(s => s.GetAsync(p.Id));
        Assert.Equal(100m, loaded.StockQuantity);
        Assert.True(loaded.TrackStock);
    }

    [Fact]
    public async Task Product_rejects_unknown_vat_rate_unit_and_duplicate_code()
    {
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.NewProduct("A1", vat: 7m));
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.NewProduct("A2", unit: "Varil"));
        await App.NewProduct("DUP");
        await Assert.ThrowsAsync<ConflictException>(() => App.NewProduct("DUP"));
    }

    [Fact]
    public async Task Vat_rates_are_configurable_not_hardcoded()
    {
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.NewProduct("V1", vat: 8m));
        var s = await App.Use<SettingsService, SettingsDto>(x => x.GetAsync());
        await App.Use<SettingsService, SettingsDto>(x => x.UpdateAsync(new UpdateSettingsRequest(s.Company, [0, 1, 8, 10, 20], s.Units.ToList(), true, "Manual")));
        var p = await App.NewProduct("V1", vat: 8m);
        Assert.Equal(8m, p.VatRate);
    }

    [Fact]
    public async Task Product_unit_cannot_change_after_movements()
    {
        var p = await App.NewProduct("U1", stock: 5);
        await Assert.ThrowsAsync<ConflictException>(() => App.Use<ProductService, ProductDto>(s => s.UpdateAsync(p.Id,
            new ProductRequest(p.Code, p.Name, null, "Koli", p.PurchasePrice, p.SalePrice, p.VatRate, true, 0, null))));
    }

    [Fact] // Test 3
    public async Task Customer_specific_price_is_used_and_overridable_and_history_is_frozen()
    {
        var a = await App.NewCustomer("A Firması");
        var b = await App.NewCustomer("B Firması");
        var peynir = await App.NewProduct("PEY", sale: 210m, stock: 1000);

        await App.Use<ProductService, CustomerPriceDto>(s => s.SetCustomerPriceAsync(a.Id, peynir.Id, 200m));
        await App.Use<ProductService, CustomerPriceDto>(s => s.SetCustomerPriceAsync(b.Id, peynir.Id, 190m));

        var la = await App.Use<ProductService, PriceLookupDto>(s => s.LookupPriceAsync(a.Id, peynir.Id));
        var lb = await App.Use<ProductService, PriceLookupDto>(s => s.LookupPriceAsync(b.Id, peynir.Id));
        Assert.Equal((200m, true), (la.Price, la.IsCustomerPrice));
        Assert.Equal((190m, true), (lb.Price, lb.IsCustomerPrice));

        // Fiyat verilmezse müşteri fiyatı kullanılır
        var saleA = await App.NewSale(a.Id, peynir, 10);
        Assert.Equal(200m, saleA.Items[0].UnitPrice);
        var saleB = await App.NewSale(b.Id, peynir, 10);
        Assert.Equal(190m, saleB.Items[0].UnitPrice);

        // Kullanıcı fiyatı değiştirebilir; o satış için kayıt altında kalır
        var custom = await App.NewSale(a.Id, peynir, 10, price: 185.5m);
        Assert.Equal(185.5m, custom.Items[0].UnitPrice);

        // Ürün ve müşteri fiyatı sonradan değişse de geçmiş satış değişmez
        await App.Use<ProductService, CustomerPriceDto>(s => s.SetCustomerPriceAsync(a.Id, peynir.Id, 260m));
        await App.Use<ProductService, ProductDto>(s => s.UpdateAsync(peynir.Id, new ProductRequest(peynir.Code, "Yeni ad", null, peynir.Unit, 100, 300m, 10m, true, 0, null)));
        var reloaded = await App.Use<SaleService, SaleDto>(s => s.GetAsync(saleA.Id));
        Assert.Equal(200m, reloaded.Items[0].UnitPrice);
        Assert.Equal(peynir.Name, reloaded.Items[0].ProductName); // satış anındaki ad
        Assert.Equal(saleA.GrandTotal, reloaded.GrandTotal);
    }
}

[Collection("pg")]
public class SaleTests(PostgresFixture pg) : IntegrationTestBase(pg)
{
    [Fact] // Test 4, 5, 6: satış oluşturma, toplamlar (backend hesaplar), KDV ve indirim
    public async Task Sale_totals_are_computed_by_backend_with_discount_and_vat()
    {
        var c = await App.NewCustomer();
        var p1 = await App.NewProduct(sale: 200m, vat: 10m, stock: 100);
        var p2 = await App.NewProduct(sale: 50m, vat: 20m, stock: 100, unit: "Adet");
        var req = new SaleRequest(c.Id, App.Clock.Today, PaymentType.Credit, null, "not",
        [
            new SaleItemRequest(p1.Id, 10, null, 5m, 50m, null), // 2000 - 100 - 50 = 1850; KDV 185 → 2035
            new SaleItemRequest(p2.Id, 4, 55m, 0, 0, null),      // 220; KDV 44 → 264 (fiyat kullanıcı tarafından değiştirildi)
        ], true, null, null);
        var sale = await App.Use<SaleService, SaleDto>(s => s.CreateAsync(req));

        Assert.Equal(2070m, sale.NetTotal);
        Assert.Equal(229m, sale.VatTotal);
        Assert.Equal(2299m, sale.GrandTotal);
        Assert.Equal(2220m, sale.GrossTotal);
        Assert.Equal(150m, sale.DiscountTotal);
        Assert.Equal(2, sale.Items.Count);
        Assert.Equal(sale.Items.Sum(i => i.LineTotal), sale.GrandTotal);
        Assert.Equal(SaleStatus.Approved, sale.Status);
        Assert.Equal(c.Id, sale.CustomerId);
        Assert.StartsWith("SAT-2026-", sale.Number);
        // 30 gün vade
        Assert.Equal(App.Clock.Today.AddDays(30), sale.DueDate);
    }

    [Fact]
    public async Task Sale_rejects_invalid_input()
    {
        var c = await App.NewCustomer();
        var p = await App.NewProduct(stock: 10);
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<SaleService, SaleDto>(s => s.CreateAsync(App.SaleOf(c.Id, p, 0))));
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<SaleService, SaleDto>(s => s.CreateAsync(App.SaleOf(c.Id, p, 1, price: -5))));
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<SaleService, SaleDto>(s => s.CreateAsync(App.SaleOf(c.Id, p, 1, discountPct: 150))));
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<SaleService, SaleDto>(s => s.CreateAsync(
            new SaleRequest(c.Id, App.Clock.Today, PaymentType.Credit, null, null, [], true, null, null))));
        await Assert.ThrowsAsync<NotFoundException>(() => App.Use<SaleService, SaleDto>(s => s.CreateAsync(App.SaleOf(Guid.NewGuid(), p, 1))));
        var due = App.SaleOf(c.Id, p, 1) with { DueDate = App.Clock.Today.AddDays(-3) };
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<SaleService, SaleDto>(s => s.CreateAsync(due)));
    }

    [Fact]
    public async Task Draft_sale_does_not_touch_ledger_or_stock_until_approved()
    {
        var c = await App.NewCustomer();
        var p = await App.NewProduct(stock: 50);
        var draft = await App.NewSale(c.Id, p, 10, approve: false);
        Assert.Equal(SaleStatus.Draft, draft.Status);
        Assert.Equal(0m, await App.Balance(c.Id));
        Assert.Equal(50m, (await App.Use<ProductService, ProductDto>(s => s.GetAsync(p.Id))).StockQuantity);

        var edited = await App.Use<SaleService, SaleDto>(s => s.UpdateDraftAsync(draft.Id, App.SaleOf(c.Id, p, 20, approve: false)));
        Assert.Equal(20m, edited.Items[0].Quantity);

        var approved = await App.Use<SaleService, SaleDto>(s => s.ApproveAsync(draft.Id));
        Assert.Equal(SaleStatus.Approved, approved.Status);
        Assert.Equal(approved.GrandTotal, await App.Balance(c.Id));
        Assert.Equal(30m, (await App.Use<ProductService, ProductDto>(s => s.GetAsync(p.Id))).StockQuantity);
        await Assert.ThrowsAsync<ConflictException>(() => App.Use<SaleService, SaleDto>(s => s.UpdateDraftAsync(draft.Id, App.SaleOf(c.Id, p, 1))));
    }

    [Fact] // Test 15
    public async Task Same_sale_cannot_be_processed_twice_in_ledger_or_stock()
    {
        var c = await App.NewCustomer();
        var p = await App.NewProduct(stock: 100);
        var sale = await App.NewSale(c.Id, p, 10);

        await Assert.ThrowsAnyAsync<Exception>(() => App.Use<SaleService, SaleDto>(s => s.ApproveAsync(sale.Id)));
        Assert.Equal(1, await App.Db(db => db.CustomerLedgerEntries.CountAsync(e => e.SourceId == sale.Id && e.EntryType == LedgerEntryType.Sale)));
        Assert.Equal(1, await App.Db(db => db.StockMovements.CountAsync(m => m.SourceId == sale.Id && m.MovementType == StockMovementType.Sale)));
        Assert.Equal(90m, (await App.Use<ProductService, ProductDto>(s => s.GetAsync(p.Id))).StockQuantity);
        Assert.Equal(sale.GrandTotal, await App.Balance(c.Id));
    }

    [Fact] // Test 15: veritabanı düzeyinde son savunma hattı
    public async Task Database_blocks_duplicate_ledger_and_stock_rows_for_same_source()
    {
        var c = await App.NewCustomer();
        var p = await App.NewProduct(stock: 100);
        var sale = await App.NewSale(c.Id, p, 10);
        var item = sale.Items[0];

        await Assert.ThrowsAsync<DbUpdateException>(() => App.Db(async db =>
        {
            db.CustomerLedgerEntries.Add(new CustomerLedgerEntry { CustomerId = c.Id, EntryDate = App.Clock.Today, EntryType = LedgerEntryType.Sale,
                Debit = sale.GrandTotal, SourceType = LedgerSourceType.Sale, SourceId = sale.Id, ReferenceNo = "X", Description = "çift kayıt" });
            return await db.SaveChangesAsync();
        }));
        await Assert.ThrowsAsync<DbUpdateException>(() => App.Db(async db =>
        {
            db.StockMovements.Add(new StockMovement { ProductId = p.Id, MovementDate = App.Clock.Today, MovementType = StockMovementType.Sale, QuantityDelta = -10,
                SourceType = LedgerSourceType.Sale, SourceId = sale.Id, SourceLineId = item.Id });
            return await db.SaveChangesAsync();
        }));
    }

    [Fact] // Test 4: ağ hatası / çift tıklama
    public async Task Repeated_request_with_same_client_key_returns_same_sale()
    {
        var c = await App.NewCustomer();
        var p = await App.NewProduct(stock: 100);
        var key = Guid.NewGuid();
        var req = App.SaleOf(c.Id, p, 10, clientKey: key);
        var first = await App.Use<SaleService, SaleDto>(s => s.CreateAsync(req));
        var second = await App.Use<SaleService, SaleDto>(s => s.CreateAsync(req));
        Assert.Equal(first.Id, second.Id);
        Assert.Equal(1, await App.Db(db => db.Sales.CountAsync()));
        Assert.Equal(first.GrandTotal, await App.Balance(c.Id));
    }

    [Fact]
    public async Task Concurrent_requests_with_same_client_key_create_only_one_sale()
    {
        var c = await App.NewCustomer();
        var p = await App.NewProduct(stock: 100);
        var req = App.SaleOf(c.Id, p, 10, clientKey: Guid.NewGuid());
        var results = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ =>
            App.Use<SaleService, SaleDto>(s => s.CreateAsync(req)).ContinueWith(t => t.IsCompletedSuccessfully ? t.Result.Id : Guid.Empty)));
        Assert.Equal(1, await App.Db(db => db.Sales.CountAsync()));
        Assert.Equal(1, await App.Db(db => db.CustomerLedgerEntries.CountAsync(e => e.EntryType == LedgerEntryType.Sale)));
        Assert.Contains(results, id => id != Guid.Empty);
    }

    [Fact]
    public async Task Cash_sale_with_immediate_full_payment_is_paid_and_leaves_no_debt()
    {
        var c = await App.NewCustomer();
        var p = await App.NewProduct(sale: 100m, stock: 100);
        var sale = await App.Use<SaleService, SaleDto>(s => s.CreateAsync(
            App.SaleOf(c.Id, p, 10, type: PaymentType.Cash, pay: new ImmediatePayment(1100m, PaymentMethod.Cash, null))));
        Assert.Equal(1100m, sale.GrandTotal);
        Assert.Equal(SaleStatus.Paid, sale.Status);
        Assert.Equal(0m, sale.Remaining);
        Assert.Equal(0m, await App.Balance(c.Id));
        Assert.Equal(sale.SaleDate, sale.DueDate);
    }

    [Fact]
    public async Task Cash_sale_partial_immediate_payment_is_partially_paid()
    {
        var c = await App.NewCustomer();
        var p = await App.NewProduct(sale: 100m, stock: 100);
        var sale = await App.Use<SaleService, SaleDto>(s => s.CreateAsync(
            App.SaleOf(c.Id, p, 10, pay: new ImmediatePayment(400m, PaymentMethod.Cash, null))));
        Assert.Equal(SaleStatus.PartiallyPaid, sale.Status);
        Assert.Equal(700m, sale.Remaining);
        Assert.Equal(700m, await App.Balance(c.Id));
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<SaleService, SaleDto>(s => s.CreateAsync(
            App.SaleOf(c.Id, p, 1, pay: new ImmediatePayment(99999m, PaymentMethod.Cash, null)))));
    }

    [Fact] // Test 23
    public async Task Stock_goes_down_on_sale_and_is_blocked_or_warned_when_insufficient()
    {
        var c = await App.NewCustomer();
        var p = await App.NewProduct(stock: 10);

        // Varsayılan: negatif stoğa izin var → uyarı verir
        var warn = await App.Use<SaleService, SaleDto>(s => s.CreateAsync(App.SaleOf(c.Id, p, 15)));
        Assert.NotEmpty(warn.Warnings);
        Assert.Equal(-5m, (await App.Use<ProductService, ProductDto>(s => s.GetAsync(p.Id))).StockQuantity);

        await App.SetAllowNegativeStock(false);
        await Assert.ThrowsAsync<ConflictException>(() => App.Use<SaleService, SaleDto>(s => s.CreateAsync(App.SaleOf(c.Id, p, 1))));
        Assert.Equal(1, await App.Db(db => db.Sales.CountAsync())); // ikinci satış hiç kaydedilmedi
    }

    [Fact] // Test 23: takip kapalı ürün
    public async Task Products_with_stock_tracking_off_are_not_checked_or_moved()
    {
        var c = await App.NewCustomer();
        var p = await App.NewProduct(track: false);
        await App.SetAllowNegativeStock(false);
        var sale = await App.NewSale(c.Id, p, 1000);
        Assert.Empty(sale.Warnings);
        Assert.Equal(0, await App.Db(db => db.StockMovements.CountAsync(m => m.ProductId == p.Id)));
    }

    [Fact] // Test 22
    public async Task Cancelling_sale_reverses_ledger_and_stock_and_keeps_payment_as_credit()
    {
        var c = await App.NewCustomer(opening: 1000m);
        var p = await App.NewProduct(sale: 100m, stock: 50);
        var sale = await App.NewSale(c.Id, p, 10);          // 1.100
        await App.NewPayment(c.Id, 300m, alloc: [new AllocationRequest(sale.Id, 300m)]);
        Assert.Equal(1000m + 1100m - 300m, await App.Balance(c.Id));

        var cancelled = await App.Use<SaleService, SaleDto>(s => s.CancelAsync(sale.Id, "Yanlış girildi"));
        Assert.Equal(SaleStatus.Cancelled, cancelled.Status);
        Assert.Equal(0m, cancelled.Remaining);
        Assert.Equal(1000m - 300m, await App.Balance(c.Id));                       // satış silindi, tahsilat avans olarak kaldı
        Assert.Equal(50m, (await App.Use<ProductService, ProductDto>(s => s.GetAsync(p.Id))).StockQuantity);

        // İz korunur: ters kayıt var, orijinal silinmedi
        var entries = await App.Db(db => db.CustomerLedgerEntries.Where(e => e.SourceId == sale.Id).OrderBy(e => e.Seq).ToListAsync());
        Assert.Equal([LedgerEntryType.Sale, LedgerEntryType.SaleReversal], entries.Select(e => e.EntryType));
        Assert.Equal(entries[0].Id, entries[1].ReversesEntryId);
        var allocs = await App.Db(db => db.PaymentAllocations.Where(a => a.SaleId == sale.Id).ToListAsync());
        Assert.All(allocs, a => Assert.False(a.IsActive));

        await Assert.ThrowsAsync<DomainException>(() => App.Use<SaleService, SaleDto>(s => s.CancelAsync(sale.Id, "tekrar")));
    }

    [Fact]
    public async Task Cancelling_draft_sale_has_no_financial_effect()
    {
        var c = await App.NewCustomer();
        var p = await App.NewProduct(stock: 5);
        var d = await App.NewSale(c.Id, p, 3, approve: false);
        await App.Use<SaleService, SaleDto>(s => s.CancelAsync(d.Id, "Vazgeçildi"));
        Assert.Equal(0, await App.Db(db => db.CustomerLedgerEntries.CountAsync()));
        Assert.Equal(0, await App.Db(db => db.StockMovements.CountAsync(m => m.MovementType == StockMovementType.SaleReversal)));
    }

    [Fact] // Test 21: satış – cari tutarlılığı
    public async Task Ledger_balance_always_equals_opening_plus_sales_minus_payments()
    {
        var c = await App.NewCustomer(opening: 12345.67m);
        var p = await App.NewProduct(sale: 33.33m, stock: 1000);
        var s1 = await App.NewSale(c.Id, p, 7);
        var s2 = await App.NewSale(c.Id, p, 3, discountPct: 10);
        var s3 = await App.NewSale(c.Id, p, 5);
        await App.NewPayment(c.Id, 100m, auto: true);
        var pay2 = await App.NewPayment(c.Id, 250.50m);
        await App.Use<SaleService, SaleDto>(s => s.CancelAsync(s3.Id, "iptal"));
        await App.Use<PaymentService, PaymentDto>(s => s.ReverseAsync(pay2.Id, "hatalı"));

        var expected = 12345.67m + s1.GrandTotal + s2.GrandTotal - 100m;
        Assert.Equal(expected, await App.Balance(c.Id));

        var statement = await App.Use<LedgerService, LedgerStatementDto>(l => l.GetCustomerStatementAsync(c.Id, null, null));
        Assert.Equal(expected, statement.ClosingBalance);
        Assert.Equal(expected, statement.TotalDebit - statement.TotalCredit);
        // Satır bakiyeleri artımlı tutarlı
        decimal run = 0;
        foreach (var e in statement.Entries) { run += e.Debit - e.Credit; Assert.Equal(run, e.Balance); }
    }

    [Fact] // Test 28: veritabanı hatasında hiçbir şey yarım kalmaz
    public async Task Database_failure_rolls_back_everything_including_sequence_number()
    {
        var c = await App.NewCustomer();
        var p = await App.NewProduct(sale: 100m, stock: 20);

        App.Interceptor.FailNext = true;
        await Assert.ThrowsAsync<DbUpdateException>(() => App.Use<SaleService, SaleDto>(s => s.CreateAsync(
            App.SaleOf(c.Id, p, 5, pay: new ImmediatePayment(100m, PaymentMethod.Cash, null)))));

        Assert.Equal(0, await App.Db(db => db.Sales.CountAsync()));
        Assert.Equal(0, await App.Db(db => db.CustomerLedgerEntries.CountAsync()));
        Assert.Equal(0, await App.Db(db => db.Payments.CountAsync()));
        Assert.Equal(20m, (await App.Use<ProductService, ProductDto>(s => s.GetAsync(p.Id))).StockQuantity);

        // Numara da geri alındı: yeniden deneme ilk numarayı alır
        var retry = await App.NewSale(c.Id, p, 5);
        Assert.EndsWith("-000001", retry.Number);
    }
}
