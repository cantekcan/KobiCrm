using Microsoft.EntityFrameworkCore;
using SutTicaretCRM.Application;
using SutTicaretCRM.Application.Services;
using SutTicaretCRM.Domain;

namespace SutTicaretCRM.IntegrationTests;

[Collection("pg")]
public class StockTests(PostgresFixture pg) : IntegrationTestBase(pg)
{
    private Task<decimal> Qty(Guid productId) => App.Use<StockService, decimal>(s => s.GetQuantityAsync(productId));

    [Fact] // Test 23: alışla artış, satışla düşüş, iade, manuel düzeltme, hareket listesi, minimum uyarısı
    public async Task Stock_in_out_return_adjustment_and_low_stock_alert()
    {
        var supplier = await App.Use<SupplierService, SupplierDto>(s => s.CreateAsync(new SupplierRequest("Çiftlik", null, null, null, null, null, null)));
        var c = await App.NewCustomer();
        var p = await App.NewProduct(sale: 100m, min: 50, unit: "Litre");

        // Alış → +200
        await App.Use<SupplierService, PurchaseDto>(s => s.CreatePurchaseAsync(new PurchaseRequest(supplier.Id, App.Clock.Today, null, "F-1", null,
            [new PurchaseItemRequest(p.Id, 200, 60m, null)], true, null)));
        Assert.Equal(200m, await Qty(p.Id));

        // Satış → −120
        await App.NewSale(c.Id, p, 120);
        Assert.Equal(80m, await Qty(p.Id));

        // İade → +10 (cari iade + stok girişi)
        await App.Use<LedgerService, LedgerEntryDto>(l => l.AddManualEntryAsync(c.Id,
            new ManualLedgerRequest(LedgerEntryType.Return, App.Clock.Today, false, 110m, "iade", [new ReturnLine(p.Id, 10)])));
        Assert.Equal(90m, await Qty(p.Id));

        // Sayım düzeltmesi: sayım 45 → fark −45
        var adj = await App.Use<StockService, StockMovementDto>(s => s.AdjustAsync(new StockAdjustRequest(p.Id, App.Clock.Today, StockMovementType.Adjustment, null, 45m, "Sayım")));
        Assert.Equal(-45m, adj.QuantityDelta);
        Assert.Equal(45m, await Qty(p.Id));

        // Minimum (50) altında → uyarı
        var low = await App.Use<StockService, IReadOnlyList<StockLevelDto>>(s => s.GetLevelsAsync(true, null));
        Assert.Equal(p.Id, low.Single().ProductId);
        Assert.True(low.Single().IsLow);
        var dash = await App.Use<DashboardService, DashboardDto>(d => d.GetAsync());
        Assert.Equal(1, dash.LowStock.Count);
        Assert.Equal(p.Id, dash.LowStockItems.Single().ProductId);

        // Hareket listesi: sıra ve türler
        var moves = await App.Use<StockService, PagedResult<StockMovementDto>>(s => s.GetMovementsAsync(p.Id, null, null, 1, 50));
        Assert.Equal(4, moves.Total);
        Assert.Equal(new[] { StockMovementType.Purchase, StockMovementType.Sale, StockMovementType.Return, StockMovementType.Adjustment }.OrderBy(x => x),
            moves.Items.Select(m => m.MovementType).OrderBy(x => x));
        Assert.Equal(45m, moves.Items.Sum(m => m.QuantityDelta));
    }

    [Fact]
    public async Task Manual_stock_rules_are_validated()
    {
        var p = await App.NewProduct(stock: 10);
        var off = await App.NewProduct(track: false);
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<StockService, StockMovementDto>(s =>
            s.AdjustAsync(new StockAdjustRequest(p.Id, App.Clock.Today, StockMovementType.Sale, 5, null, null))));      // izinsiz tür
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<StockService, StockMovementDto>(s =>
            s.AdjustAsync(new StockAdjustRequest(p.Id, App.Clock.Today, StockMovementType.Adjustment, null, null, null))));   // miktar yok
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<StockService, StockMovementDto>(s =>
            s.AdjustAsync(new StockAdjustRequest(p.Id, App.Clock.Today, StockMovementType.Adjustment, null, 10m, null))));    // fark yok
        await Assert.ThrowsAsync<ConflictException>(() => App.Use<StockService, StockMovementDto>(s =>
            s.AdjustAsync(new StockAdjustRequest(off.Id, App.Clock.Today, StockMovementType.Adjustment, 5, null, null))));    // takip kapalı
        await App.SetAllowNegativeStock(false);
        await Assert.ThrowsAsync<ConflictException>(() => App.Use<StockService, StockMovementDto>(s =>
            s.AdjustAsync(new StockAdjustRequest(p.Id, App.Clock.Today, StockMovementType.Adjustment, -11, null, null))));
    }

    [Fact]
    public async Task Cancelled_purchase_reverses_supplier_debt_and_stock()
    {
        var s = await App.Use<SupplierService, SupplierDto>(x => x.CreateAsync(new SupplierRequest("Tedarikçi", null, null, null, null, null, null)));
        var p = await App.NewProduct();
        var purchase = await App.Use<SupplierService, PurchaseDto>(x => x.CreatePurchaseAsync(new PurchaseRequest(s.Id, App.Clock.Today, null, null, null,
            [new PurchaseItemRequest(p.Id, 10, 100m, null)], true, null)));
        Assert.Equal(1_100m, (await App.Use<SupplierService, SupplierDto>(x => x.GetAsync(s.Id))).Balance);
        Assert.Equal(10m, await Qty(p.Id));

        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<SupplierService, PurchaseDto>(x => x.CancelPurchaseAsync(purchase.Id, "")));
        var cancelled = await App.Use<SupplierService, PurchaseDto>(x => x.CancelPurchaseAsync(purchase.Id, "Yanlış girildi"));
        Assert.Equal(PurchaseStatus.Cancelled, cancelled.Status);
        Assert.Equal(0m, (await App.Use<SupplierService, SupplierDto>(x => x.GetAsync(s.Id))).Balance);
        Assert.Equal(0m, await Qty(p.Id));
        await Assert.ThrowsAsync<ConflictException>(() => App.Use<SupplierService, PurchaseDto>(x => x.CancelPurchaseAsync(purchase.Id, "tekrar")));
    }

    [Fact]
    public async Task Purchase_cannot_be_approved_twice()
    {
        var s = await App.Use<SupplierService, SupplierDto>(x => x.CreateAsync(new SupplierRequest("T", null, null, null, null, null, null)));
        var p = await App.NewProduct();
        var draft = await App.Use<SupplierService, PurchaseDto>(x => x.CreatePurchaseAsync(new PurchaseRequest(s.Id, App.Clock.Today, null, null, null,
            [new PurchaseItemRequest(p.Id, 5, 10m, null)], false, null)));
        Assert.Equal(0m, await Qty(p.Id));
        await App.Use<SupplierService, PurchaseDto>(x => x.ApprovePurchaseAsync(draft.Id));
        await Assert.ThrowsAsync<ConflictException>(() => App.Use<SupplierService, PurchaseDto>(x => x.ApprovePurchaseAsync(draft.Id)));
        Assert.Equal(5m, await Qty(p.Id));
        Assert.Equal(1, await App.Db(db => db.SupplierLedgerEntries.CountAsync(e => e.EntryType == LedgerEntryType.Purchase)));
    }
}

[Collection("pg")]
public class SupplierLedgerTests(PostgresFixture pg) : IntegrationTestBase(pg)
{
    [Fact] // Müşteri alacakları ile tedarikçi borçları ayrı hesaplarda izlenir
    public async Task Supplier_debt_is_tracked_separately_from_customer_receivables()
    {
        var customer = await App.NewCustomer(opening: 50_000m);
        var supplier = await App.Use<SupplierService, SupplierDto>(s => s.CreateAsync(new SupplierRequest("Çiftlik Koop.", null, null, null, null, null, null, true,
            new OpeningBalanceRequest(30_000m, new DateOnly(2026, 1, 1), null, "Devir"))));
        Assert.Equal(30_000m, supplier.Balance);

        var p = await App.NewProduct(stock: 0);
        await App.Use<SupplierService, PurchaseDto>(s => s.CreatePurchaseAsync(new PurchaseRequest(supplier.Id, App.Clock.Today, null, null, null,
            [new PurchaseItemRequest(p.Id, 100, 50m, null)], true, null)));       // +5.500
        await App.Use<SupplierService, LedgerEntryDto>(s => s.PaySupplierAsync(supplier.Id, new SupplierPaymentRequest(App.Clock.Today, 10_000m, PaymentMethod.BankTransfer, "EFT")));

        Assert.Equal(25_500m, (await App.Use<SupplierService, SupplierDto>(s => s.GetAsync(supplier.Id))).Balance);
        Assert.Equal(50_000m, await App.Balance(customer.Id));                     // müşteri tarafı etkilenmedi

        var debts = await App.Use<ReportService, SupplierDebtsDto>(r => r.SupplierDebtsAsync());
        Assert.Equal(25_500m, debts.Total);
        var st = await App.Use<LedgerService, LedgerStatementDto>(l => l.GetSupplierStatementAsync(supplier.Id, null, null));
        Assert.Equal([LedgerEntryType.OpeningBalance, LedgerEntryType.Purchase, LedgerEntryType.Payment], st.Entries.Select(e => e.EntryType));
        Assert.Equal(25_500m, st.ClosingBalance);

        var rec = await App.Use<ReportService, ReceivablesReportDto>(r => r.ReceivablesAsync(false, null));
        Assert.Equal(50_000m, rec.TotalReceivable);
        await Assert.ThrowsAsync<DbUpdateException>(() => App.Db(async db =>
        {
            db.SupplierLedgerEntries.Add(new SupplierLedgerEntry { SupplierId = supplier.Id, EntryDate = App.Clock.Today, EntryType = LedgerEntryType.OpeningBalance, Credit = 1, ReferenceNo = "x", Description = "çift açılış" });
            return await db.SaveChangesAsync();
        }));
    }
}

[Collection("pg")]
public class DashboardAndReportTests(PostgresFixture pg) : IntegrationTestBase(pg)
{
    /// <summary>
    /// Bugün: 2026-10-15. A: açılış 100.000 (01.01), satış 20.09 → 550 (vade 20.10), satış bugün 1.100, ödeme 25.000 (25.09, dağıtılmamış).
    /// B: satış 02.10 → 2.200 (vade 01.11), ödeme 200 bugün. Ayrıca iptal edilen satış ve taslak satış sayılmamalı.
    /// </summary>
    private async Task<(CustomerDetailDto A, CustomerDetailDto B, ProductDto P)> BuildScenario()
    {
        var a = await App.NewCustomer("A Firması", opening: 100_000m, termDays: 30);
        var b = await App.NewCustomer("B Firması", termDays: 30);
        var p = await App.NewProduct(sale: 100m, vat: 10m, stock: 10_000);
        await App.Use<SaleService, SaleDto>(s => s.CreateAsync(App.SaleOf(a.Id, p, 5, date: new DateOnly(2026, 9, 20))));       // 550, vade 20.10
        await App.Use<SaleService, SaleDto>(s => s.CreateAsync(App.SaleOf(a.Id, p, 10)));                                       // 1.100 bugün
        await App.Use<SaleService, SaleDto>(s => s.CreateAsync(App.SaleOf(b.Id, p, 20, date: new DateOnly(2026, 10, 2))));     // 2.200
        var cancelled = await App.Use<SaleService, SaleDto>(s => s.CreateAsync(App.SaleOf(b.Id, p, 100)));
        await App.Use<SaleService, SaleDto>(s => s.CancelAsync(cancelled.Id, "iptal"));
        await App.Use<SaleService, SaleDto>(s => s.CreateAsync(App.SaleOf(b.Id, p, 50, approve: false)));                      // taslak
        await App.Use<PaymentService, PaymentDto>(s => s.CreateAsync(new PaymentRequest(a.Id, new DateOnly(2026, 9, 25), 25_000m, PaymentMethod.BankTransfer, null, null, null, false, null)));
        await App.NewPayment(b.Id, 200m);
        return (a, b, p);
    }

    [Fact] // Test 24
    public async Task Dashboard_cards_show_defined_totals_excluding_drafts_cancellations_and_opening()
    {
        await BuildScenario();
        var d = await App.Use<DashboardService, DashboardDto>(x => x.GetAsync());

        Assert.Equal(new DateOnly(2026, 10, 15), d.Today);
        Assert.Equal((1_100m, 1), (d.TodaySales.Amount, d.TodaySales.Count));
        Assert.Equal((3_300m, 2), (d.MonthSales.Amount, d.MonthSales.Count));             // 1.100 + 2.200; Eylül, taslak ve iptal hariç
        Assert.Equal(76_650m + 2_000m, d.TotalReceivable.Amount);                         // A: 100.000+550+1.100−25.000; B: 2.200−200
        Assert.Equal(75_000m, d.OverdueReceivable.Amount);                                // eski açılış borcu − dağıtılmamış 25.000
        Assert.Equal(78_650m - 75_000m, d.PendingCollections.Amount);
        Assert.Equal(200m, d.MonthCollected.Amount);                                      // Eylül tahsilatı bu ay sayılmaz
        Assert.Equal(1, d.MonthCollected.Count);
        Assert.Equal(0, d.DraftInvoices.Count);
        Assert.False(d.IsDemo);
        Assert.Equal(2, d.RecentPayments.Count);
        Assert.Equal("A Firması", d.TopBalances[0].CustomerName);
        Assert.Equal(76_650m, d.TopBalances[0].Balance);
        Assert.Equal(75_000m, d.TopBalances[0].Overdue);
        // Yaklaşan vadeler: A'nın 20.10 vadeli satışı 550; B'nin vadesi 14 gün dışında
        var due = Assert.Single(d.UpcomingDues);
        Assert.Equal((new DateOnly(2026, 10, 20), 550m), (due.DueDate, due.Remaining));
        Assert.True(d.RecentSales.All(s => s.Status != SaleStatus.Cancelled));
        Assert.NotEmpty(d.Definitions);
    }

    [Fact]
    public async Task Dashboard_invoice_cards_separate_draft_awaiting_and_monthly_created()
    {
        var c = await App.NewCustomer();
        var p = await App.NewProduct(sale: 100m, stock: 100);
        var s1 = await App.NewSale(c.Id, p, 1);
        var s2 = await App.NewSale(c.Id, p, 2);
        var s3 = await App.NewSale(c.Id, p, 3);
        await App.Use<InvoiceService, InvoiceDto>(s => s.CreateDraftFromSaleAsync(s1.Id, null));    // taslak
        await App.NewApprovedInvoice(s2);                                                           // iç onaylı, resmî bekliyor
        var i3 = await App.NewApprovedInvoice(s3);
        await App.RecordOfficial(i3.Id, "GIB2026000000050");                                        // tamamlandı

        var d = await App.Use<DashboardService, DashboardDto>(x => x.GetAsync());
        Assert.Equal((110m, 1), (d.DraftInvoices.Amount, d.DraftInvoices.Count));
        Assert.Equal((220m, 1), (d.AwaitingOfficialInvoices.Amount, d.AwaitingOfficialInvoices.Count));
        Assert.Equal(3, d.MonthInvoicesCreated.Count);
        Assert.Equal(110m + 220m + 330m, d.MonthInvoicesCreated.Amount);

        // İptal edilen taslak bu ay oluşturulanlara dahil edilmez
        var draftId = (await App.Db(db => db.Invoices.FirstAsync(i => i.SaleId == s1.Id))).Id;
        await App.Use<InvoiceService, InvoiceDto>(s => s.CancelDraftAsync(draftId, "x"));
        d = await App.Use<DashboardService, DashboardDto>(x => x.GetAsync());
        Assert.Equal(0, d.DraftInvoices.Count);
        Assert.Equal(2, d.MonthInvoicesCreated.Count);
    }

    [Fact] // Test 25
    public async Task Monthly_sales_report_groups_by_month_and_excludes_opening_drafts_and_cancelled()
    {
        await BuildScenario();
        var rep = await App.Use<ReportService, SalesReportDto>(r => r.SalesAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 31), "month", null));
        Assert.Equal(2, rep.Rows.Count);
        Assert.Equal(("2026-09", 550m, 1), (rep.Rows[0].Key, rep.Rows[0].GrandTotal, rep.Rows[0].SaleCount));
        Assert.Equal(("2026-10", 3_300m, 2), (rep.Rows[1].Key, rep.Rows[1].GrandTotal, rep.Rows[1].SaleCount));
        Assert.Contains("Ekim 2026", rep.Rows[1].Label);
        Assert.Equal(3_850m, rep.GrandTotal);
        Assert.Equal(3_500m, rep.NetTotal);
        Assert.Equal(350m, rep.VatTotal);
        Assert.Equal(3, rep.SaleCount);
        Assert.Contains("Açılış bakiyeleri", rep.Definition);

        // Yalnızca Ekim ve müşteri filtresi
        var bId = await App.Db(db => db.Customers.Where(c => c.Name == "B Firması").Select(c => c.Id).FirstAsync());
        var onlyB = await App.Use<ReportService, SalesReportDto>(r => r.SalesAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), "month", bId));
        Assert.Equal(2_200m, onlyB.GrandTotal);
    }

    [Fact]
    public async Task Sales_report_supports_day_week_customer_and_product_grouping()
    {
        var (a, b, p) = await BuildScenario();
        var from = new DateOnly(2026, 9, 1); var to = new DateOnly(2026, 10, 31);

        var day = await App.Use<ReportService, SalesReportDto>(r => r.SalesAsync(from, to, "day", null));
        Assert.Equal(3, day.Rows.Count);
        Assert.Equal(["2026-09-20", "2026-10-02", "2026-10-15"], day.Rows.Select(r => r.Key));

        var week = await App.Use<ReportService, SalesReportDto>(r => r.SalesAsync(from, to, "week", null));
        Assert.Equal(["2026-09-14", "2026-09-28", "2026-10-12"], week.Rows.Select(r => r.Key));   // pazartesi başlangıçlı
        Assert.Equal(3_850m, week.GrandTotal);

        var cust = await App.Use<ReportService, SalesReportDto>(r => r.SalesAsync(from, to, "customer", null));
        Assert.Equal(["B Firması", "A Firması"], cust.Rows.Select(r => r.Label));                  // tutara göre azalan
        Assert.Equal(2_200m, cust.Rows[0].GrandTotal);
        Assert.Equal(1_650m, cust.Rows[1].GrandTotal);

        var prod = await App.Use<ReportService, SalesReportDto>(r => r.SalesAsync(from, to, "product", null));
        var row = Assert.Single(prod.Rows);
        Assert.Equal(35m, row.Quantity);                                                          // 5 + 10 + 20
        Assert.Equal("Kg", row.Unit);
        Assert.Equal(3_850m, row.GrandTotal);

        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<ReportService, SalesReportDto>(r => r.SalesAsync(to, from, "day", null)));
    }

    [Fact] // Satış ≠ resmî fatura ≠ tahsilat
    public async Task Measures_report_keeps_sales_invoices_and_collections_separate()
    {
        var c = await App.NewCustomer();
        var p = await App.NewProduct(sale: 1000m, vat: 0m, stock: 1000);
        var s1 = await App.NewSale(c.Id, p, 500);          // 500.000 ticari satış
        var s2 = await App.NewSale(c.Id, p, 100);          // 100.000
        var inv = await App.NewApprovedInvoice(s1);
        await App.RecordOfficial(inv.Id, "GIB2026000000060");   // 450.000 DEĞİL: resmî fatura satış tutarı kadar (500.000)
        await App.NewPayment(c.Id, 300_000m);

        var m = await App.Use<ReportService, MeasuresDto>(r => r.MeasuresAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31), null));
        Assert.Equal(600_000m, m.CommercialSales.Amount);
        Assert.Equal(500_000m, m.OfficialInvoices.Amount);
        Assert.Equal(300_000m, m.Collections.Amount);
        Assert.Equal(1, m.OfficialInvoices.Count);
        Assert.Contains("GİB'den doğrulanmamıştır", m.OfficialInvoices.Note);                // kullanıcı girişi uyarısı
        Assert.NotEqual(m.CommercialSales.Amount, m.OfficialInvoices.Amount);
        Assert.False(string.IsNullOrWhiteSpace(m.Explanation));
        Assert.All(new[] { m.CommercialSales, m.OfficialInvoices, m.Collections }, x => Assert.False(string.IsNullOrWhiteSpace(x.Definition)));
    }

    [Fact]
    public async Task Collections_and_invoice_status_and_open_invoice_reports()
    {
        var (a, b, p) = await BuildScenario();
        var rep = await App.Use<ReportService, CollectionsReportDto>(r => r.CollectionsAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 31), null, null));
        Assert.Equal(25_200m, rep.Total);
        Assert.Equal(25_200m, rep.ByMethod["BankTransfer"]);
        Assert.Equal(2, rep.Rows.Count);
        var onlyA = await App.Use<ReportService, CollectionsReportDto>(r => r.CollectionsAsync(new DateOnly(2026, 9, 1), new DateOnly(2026, 10, 31), a.Id, null));
        Assert.Equal(25_000m, onlyA.Total);

        var sale = await App.Db(db => db.Sales.FirstAsync(s => s.CustomerId == b.Id && s.Status == SaleStatus.PartiallyPaid || s.CustomerId == b.Id && s.Status == SaleStatus.Approved));
        var s = await App.Use<SaleService, SaleDto>(x => x.GetAsync(sale.Id));
        var inv = await App.NewApprovedInvoice(s);
        var status = await App.Use<ReportService, InvoiceStatusReportDto>(r => r.InvoiceStatusAsync(new DateOnly(2026, 10, 1), new DateOnly(2026, 10, 31)));
        var row = Assert.Single(status.Rows);
        Assert.Equal((InvoiceInternalStatus.Approved, OfficialInvoiceStatus.NotIssued, 1), (row.InternalStatus, row.OfficialStatus, row.Count));
        var open = await App.Use<ReportService, OpenInvoicesDto>(r => r.OpenInvoicesAsync(null));
        Assert.Equal(inv.Id, open.Rows.Single().Id);
        Assert.Equal(inv.GrandTotal, open.TotalRemaining);
    }

    [Fact]
    public async Task Statement_supports_date_filter_with_carried_balance()
    {
        var c = await App.NewCustomer(opening: 1_000m);              // 01.01
        var p = await App.NewProduct(sale: 100m, stock: 100);
        await App.Use<SaleService, SaleDto>(s => s.CreateAsync(App.SaleOf(c.Id, p, 1, date: new DateOnly(2026, 6, 1))));   // +110
        await App.Use<SaleService, SaleDto>(s => s.CreateAsync(App.SaleOf(c.Id, p, 2, date: new DateOnly(2026, 9, 1))));   // +220
        var st = await App.Use<LedgerService, LedgerStatementDto>(l => l.GetCustomerStatementAsync(c.Id, new DateOnly(2026, 8, 1), new DateOnly(2026, 9, 30)));
        Assert.Equal(1_110m, st.CarriedBalance);
        Assert.Single(st.Entries);
        Assert.Equal(1_330m, st.Entries[0].Balance);
        Assert.Equal(1_330m, st.ClosingBalance);
        Assert.Equal(220m, st.TotalDebit);
    }

    [Fact]
    public async Task Demo_flag_is_driven_by_database_mode_only()
    {
        Assert.False((await App.Use<DashboardService, DashboardDto>(d => d.GetAsync())).IsDemo);
        await App.Db(async db =>
        {
            (await db.AppSettings.FirstAsync(s => s.Key == SettingsService.DatabaseModeKey)).Value = "Demo";
            return await db.SaveChangesAsync();
        });
        Assert.True((await App.Use<DashboardService, DashboardDto>(d => d.GetAsync())).IsDemo);
    }
}
