using Microsoft.EntityFrameworkCore;
using SutTicaretCRM.Application;
using SutTicaretCRM.Application.Services;
using SutTicaretCRM.Domain;

namespace SutTicaretCRM.IntegrationTests;

[Collection("pg")]
public class OpeningBalanceTests(PostgresFixture pg) : IntegrationTestBase(pg)
{
    [Fact] // Test 7
    public async Task Opening_balance_of_100000_is_recorded_as_ledger_entry_without_invoice_or_sale()
    {
        var c = await App.NewCustomer("A Firması", opening: 100_000m);

        Assert.Equal(100_000m, c.Balance);
        Assert.True(c.HasOpeningBalance);
        Assert.Equal(100_000m, c.OpeningBalanceAmount);
        Assert.StartsWith("ACL-2026-", c.OpeningReference);

        var entry = await App.Db(db => db.CustomerLedgerEntries.SingleAsync(e => e.CustomerId == c.Id));
        Assert.Equal(LedgerEntryType.OpeningBalance, entry.EntryType);
        Assert.Equal(100_000m, entry.Debit);   // müşteri bize 100.000 TL borçlu
        Assert.Equal(0m, entry.Credit);
        Assert.Equal(new DateOnly(2026, 1, 1), entry.EntryDate);
        Assert.Equal("Devir", entry.Description);

        // Fatura ya da satış oluşmadı
        Assert.Equal(0, await App.Db(db => db.Invoices.CountAsync()));
        Assert.Equal(0, await App.Db(db => db.Sales.CountAsync()));
    }

    [Fact] // Test 8
    public async Task Opening_balance_is_not_reported_as_a_new_sale_anywhere()
    {
        var c = await App.NewCustomer(opening: 100_000m);

        var measures = await App.Use<ReportService, MeasuresDto>(r => r.MeasuresAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), null));
        Assert.Equal(0m, measures.CommercialSales.Amount);
        Assert.Equal(0, measures.CommercialSales.Count);

        foreach (var group in new[] { "day", "week", "month", "customer", "product" })
        {
            var rep = await App.Use<ReportService, SalesReportDto>(r => r.SalesAsync(new DateOnly(2026, 1, 1), new DateOnly(2026, 12, 31), group, null));
            Assert.Empty(rep.Rows);
            Assert.Equal(0m, rep.GrandTotal);
        }

        var dash = await App.Use<DashboardService, DashboardDto>(d => d.GetAsync());
        Assert.Equal(0m, dash.MonthSales.Amount);
        Assert.Equal(0m, dash.TodaySales.Amount);
        Assert.Equal(100_000m, dash.TotalReceivable.Amount);   // ama alacak olarak görünür

        var rec = await App.Use<ReportService, ReceivablesReportDto>(r => r.ReceivablesAsync(false, null));
        Assert.Equal(100_000m, rec.TotalReceivable);
        Assert.Equal(c.Id, rec.Rows.Single().CustomerId);

        var history = await App.Use<CustomerService, IReadOnlyList<CustomerProductHistoryItem>>(s => s.GetProductHistoryAsync(c.Id));
        Assert.Empty(history);
    }

    [Fact] // Test 9
    public async Task Opening_balance_cannot_be_entered_twice()
    {
        var c = await App.NewCustomer(opening: 100_000m);
        var again = await Assert.ThrowsAsync<ConflictException>(() => App.Use<CustomerService, CustomerDetailDto>(s =>
            s.AddOpeningBalanceAsync(c.Id, new OpeningBalanceRequest(100_000m, new DateOnly(2026, 1, 1), null, "tekrar"))));
        Assert.Contains("zaten", again.Message);
        Assert.Equal(100_000m, await App.Balance(c.Id));
        Assert.Equal(1, await App.Db(db => db.CustomerLedgerEntries.CountAsync()));
    }

    [Fact] // Test 9: servis atlansa bile veritabanı engeller
    public async Task Database_itself_blocks_a_second_opening_entry()
    {
        var c = await App.NewCustomer(opening: 100_000m);
        await Assert.ThrowsAsync<DbUpdateException>(() => App.Db(async db =>
        {
            db.CustomerLedgerEntries.Add(new CustomerLedgerEntry { CustomerId = c.Id, EntryDate = App.Clock.Today, EntryType = LedgerEntryType.OpeningBalance,
                Debit = 100_000m, SourceType = LedgerSourceType.Opening, SourceId = Guid.NewGuid(), ReferenceNo = "ACL-X", Description = "çift" });
            return await db.SaveChangesAsync();
        }));
    }

    [Fact]
    public async Task Opening_balance_can_be_added_later_to_existing_customer_and_validates_amount()
    {
        var c = await App.NewCustomer();
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<CustomerService, CustomerDetailDto>(s =>
            s.AddOpeningBalanceAsync(c.Id, new OpeningBalanceRequest(0, new DateOnly(2026, 1, 1), null, null))));
        var d = await App.Use<CustomerService, CustomerDetailDto>(s =>
            s.AddOpeningBalanceAsync(c.Id, new OpeningBalanceRequest(2500.75m, new DateOnly(2026, 3, 1), null, null)));
        Assert.Equal(2500.75m, d.Balance);
        Assert.Equal(new DateOnly(2026, 3, 1), d.OpeningBalanceDate);
    }

    [Fact]
    public async Task Correction_after_opening_is_an_adjustment_entry_not_an_edit()
    {
        var c = await App.NewCustomer(opening: 100_000m);
        await App.Use<LedgerService, LedgerEntryDto>(l => l.AddManualEntryAsync(c.Id,
            new ManualLedgerRequest(LedgerEntryType.Adjustment, App.Clock.Today, false, 5_000m, "Açılış bakiyesi fazla girilmişti")));
        Assert.Equal(95_000m, await App.Balance(c.Id));
        var opening = await App.Db(db => db.CustomerLedgerEntries.SingleAsync(e => e.EntryType == LedgerEntryType.OpeningBalance));
        Assert.Equal(100_000m, opening.Debit); // orijinal kayıt değişmedi
    }
}

[Collection("pg")]
public class PaymentTests(PostgresFixture pg) : IntegrationTestBase(pg)
{
    [Fact] // Test 10, 11
    public async Task Payment_of_25000_reduces_balance_from_100000_to_75000()
    {
        var c = await App.NewCustomer(opening: 100_000m);
        var pay = await App.Use<PaymentService, PaymentDto>(s => s.CreateAsync(new PaymentRequest(
            c.Id, new DateOnly(2026, 10, 14), 25_000m, PaymentMethod.BankTransfer, "EFT-123", "Kısmi ödeme", null, false, null)));

        Assert.Equal(25_000m, pay.Amount);
        Assert.Equal(PaymentMethod.BankTransfer, pay.Method);
        Assert.Equal(new DateOnly(2026, 10, 14), pay.PaymentDate);
        Assert.Equal("EFT-123", pay.BankReference);
        Assert.Equal(25_000m, pay.UnallocatedAmount); // genel bakiyeden mahsup
        Assert.Equal(75_000m, await App.Balance(c.Id));

        var st = await App.Use<LedgerService, LedgerStatementDto>(l => l.GetCustomerStatementAsync(c.Id, null, null));
        Assert.Equal([LedgerEntryType.OpeningBalance, LedgerEntryType.Payment], st.Entries.Select(e => e.EntryType));
        Assert.Equal(75_000m, st.Entries[^1].Balance);
        Assert.Equal(25_000m, st.Entries[^1].Credit);
        Assert.Equal("test", st.Entries[^1].CreatedBy);
    }

    [Fact] // Örnek: açılış 100k + satış 20k − tahsilat 25k = 95k
    public async Task Spec_example_opening_plus_sale_minus_payment()
    {
        var c = await App.NewCustomer(opening: 100_000m);
        var p = await App.NewProduct(sale: 20_000m / 1.1m, stock: 10);       // KDV dahil 20.000
        var sale = await App.Use<SaleService, SaleDto>(s => s.CreateAsync(App.SaleOf(c.Id, p, 1, price: 18_181.82m)));
        Assert.Equal(20_000.00m, sale.GrandTotal);
        await App.NewPayment(c.Id, 25_000m);
        Assert.Equal(95_000m, await App.Balance(c.Id));
    }

    [Fact] // Test 12
    public async Task Partial_payments_on_a_25000_sale_leave_15000_then_zero()
    {
        var c = await App.NewCustomer();
        var p = await App.NewProduct(sale: 25_000m / 1.1m, stock: 10);
        var sale = await App.Use<SaleService, SaleDto>(s => s.CreateAsync(App.SaleOf(c.Id, p, 1, price: 22_727.27m)));
        Assert.Equal(25_000m, sale.GrandTotal);

        await App.NewPayment(c.Id, 10_000m, alloc: [new AllocationRequest(sale.Id, 10_000m)]);
        var after1 = await App.Use<SaleService, SaleDto>(s => s.GetAsync(sale.Id));
        Assert.Equal(SaleStatus.PartiallyPaid, after1.Status);
        Assert.Equal(15_000m, after1.Remaining);
        Assert.Equal(15_000m, await App.Balance(c.Id));

        await App.NewPayment(c.Id, 15_000m, alloc: [new AllocationRequest(sale.Id, 15_000m)]);
        var after2 = await App.Use<SaleService, SaleDto>(s => s.GetAsync(sale.Id));
        Assert.Equal(SaleStatus.Paid, after2.Status);
        Assert.Equal(0m, after2.Remaining);
        Assert.Equal(0m, await App.Balance(c.Id));
    }

    [Fact] // Test 13
    public async Task One_payment_can_be_split_across_multiple_sales_with_separate_allocation_rows()
    {
        var c = await App.NewCustomer();
        var p = await App.NewProduct(sale: 100m, stock: 100);
        var s1 = await App.NewSale(c.Id, p, 10);   // 1.100
        var s2 = await App.NewSale(c.Id, p, 20);   // 2.200
        var pay = await App.NewPayment(c.Id, 3_000m, alloc: [new AllocationRequest(s1.Id, 1_100m), new AllocationRequest(s2.Id, 1_500m)]);

        Assert.Equal(2, pay.Allocations.Count);
        Assert.Equal(2_600m, pay.AllocatedAmount);
        Assert.Equal(400m, pay.UnallocatedAmount);
        Assert.Equal(SaleStatus.Paid, (await App.Use<SaleService, SaleDto>(s => s.GetAsync(s1.Id))).Status);
        var second = await App.Use<SaleService, SaleDto>(s => s.GetAsync(s2.Id));
        Assert.Equal(SaleStatus.PartiallyPaid, second.Status);
        Assert.Equal(700m, second.Remaining);
        Assert.Equal(3_300m - 3_000m, await App.Balance(c.Id));

        // Kalan 400 TL sonradan başka satışa dağıtılabilir
        var after = await App.Use<PaymentService, PaymentDto>(s => s.AllocateAsync(pay.Id, [new AllocationRequest(s2.Id, 400m)]));
        Assert.Equal(3, after.Allocations.Count);
        Assert.Equal(0m, after.UnallocatedAmount);
        Assert.Equal(300m, (await App.Use<SaleService, SaleDto>(s => s.GetAsync(s2.Id))).Remaining);
    }

    [Fact]
    public async Task Allocation_rules_are_enforced()
    {
        var a = await App.NewCustomer();
        var b = await App.NewCustomer();
        var p = await App.NewProduct(sale: 100m, stock: 100);
        var sa = await App.NewSale(a.Id, p, 10);
        var sb = await App.NewSale(b.Id, p, 10);
        var draft = await App.NewSale(a.Id, p, 1, approve: false);

        // Tahsilat tutarını aşan dağıtım
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.NewPayment(a.Id, 100m, alloc: [new AllocationRequest(sa.Id, 200m)]));
        // Satışın kalanını aşan dağıtım
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.NewPayment(a.Id, 5_000m, alloc: [new AllocationRequest(sa.Id, 1_200m)]));
        // Başka müşterinin satışı
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.NewPayment(a.Id, 100m, alloc: [new AllocationRequest(sb.Id, 100m)]));
        // Taslak satışa dağıtım
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.NewPayment(a.Id, 100m, alloc: [new AllocationRequest(draft.Id, 100m)]));
        // Sıfır/negatif tutar
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.NewPayment(a.Id, 0m));
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.NewPayment(a.Id, -5m));

        // Hatalı denemelerin hiçbiri cariyi etkilemedi
        Assert.Equal(1_100m, await App.Balance(a.Id));
        Assert.Equal(0, await App.Db(db => db.Payments.CountAsync()));
    }

    [Fact]
    public async Task Auto_allocation_pays_oldest_due_sale_first()
    {
        var c = await App.NewCustomer(termDays: 0);
        var p = await App.NewProduct(sale: 100m, stock: 100);
        var late = await App.Use<SaleService, SaleDto>(s => s.CreateAsync(App.SaleOf(c.Id, p, 10, date: App.Clock.Today.AddDays(-20))));   // 1.100, eski
        var recent = await App.Use<SaleService, SaleDto>(s => s.CreateAsync(App.SaleOf(c.Id, p, 10, date: App.Clock.Today.AddDays(-2))));   // 1.100, yeni
        var pay = await App.NewPayment(c.Id, 1_500m, auto: true);

        Assert.Equal(2, pay.Allocations.Count);
        Assert.Equal(SaleStatus.Paid, (await App.Use<SaleService, SaleDto>(s => s.GetAsync(late.Id))).Status);
        Assert.Equal(700m, (await App.Use<SaleService, SaleDto>(s => s.GetAsync(recent.Id))).Remaining);
    }

    [Fact] // Test 22: tahsilat geri alma izi korur
    public async Task Reversing_a_payment_restores_debt_and_keeps_audit_trail()
    {
        var c = await App.NewCustomer();
        var p = await App.NewProduct(sale: 100m, stock: 100);
        var sale = await App.NewSale(c.Id, p, 10);
        var pay = await App.NewPayment(c.Id, 1_100m, alloc: [new AllocationRequest(sale.Id, 1_100m)]);
        Assert.Equal(0m, await App.Balance(c.Id));
        Assert.Equal(SaleStatus.Paid, (await App.Use<SaleService, SaleDto>(s => s.GetAsync(sale.Id))).Status);

        var rev = await App.Use<PaymentService, PaymentDto>(s => s.ReverseAsync(pay.Id, "Dekont hatalıydı"));
        Assert.Equal(PaymentStatus.Reversed, rev.Status);
        Assert.Equal("Dekont hatalıydı", rev.ReverseReason);
        Assert.Equal(1_100m, rev.Amount);                  // kayıt silinmedi
        Assert.Equal(0m, rev.UnallocatedAmount);
        Assert.Equal(1_100m, await App.Balance(c.Id));
        var reloaded = await App.Use<SaleService, SaleDto>(s => s.GetAsync(sale.Id));
        Assert.Equal(SaleStatus.Approved, reloaded.Status);
        Assert.Equal(1_100m, reloaded.Remaining);

        var entries = await App.Db(db => db.CustomerLedgerEntries.Where(e => e.SourceId == pay.Id).OrderBy(e => e.Seq).ToListAsync());
        Assert.Equal([LedgerEntryType.Payment, LedgerEntryType.PaymentReversal], entries.Select(e => e.EntryType));
        Assert.Equal(entries[0].Id, entries[1].ReversesEntryId);
        Assert.Equal(1, await App.Db(db => db.PaymentAllocations.CountAsync(a => a.PaymentId == pay.Id && !a.IsActive && a.ReversedAtUtc != null)));
        Assert.True(await App.Db(db => db.AuditLogs.AnyAsync(a => a.Action == "Tahsilat geri alındı" && a.EntityId == pay.Id)));

        await Assert.ThrowsAsync<DomainException>(() => App.Use<PaymentService, PaymentDto>(s => s.ReverseAsync(pay.Id, "ikinci")));
        await Assert.ThrowsAsync<ConflictException>(() => App.Use<PaymentService, PaymentDto>(s => s.AllocateAsync(pay.Id, [new AllocationRequest(sale.Id, 1m)])));
        Assert.Equal(1_100m, await App.Balance(c.Id));
    }

    [Fact]
    public async Task Payment_with_same_client_key_is_created_once()
    {
        var c = await App.NewCustomer(opening: 1_000m);
        var key = Guid.NewGuid();
        PaymentRequest R() => new(c.Id, App.Clock.Today, 400m, PaymentMethod.Cash, null, null, null, false, key);
        var a = await App.Use<PaymentService, PaymentDto>(s => s.CreateAsync(R()));
        var b = await App.Use<PaymentService, PaymentDto>(s => s.CreateAsync(R()));
        Assert.Equal(a.Id, b.Id);
        Assert.Equal(600m, await App.Balance(c.Id));
    }

    [Fact]
    public async Task Receipt_upload_validates_content_and_can_be_downloaded()
    {
        var c = await App.NewCustomer(opening: 1_000m);
        var pay = await App.NewPayment(c.Id, 100m);
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<PaymentService, PaymentDto>(s =>
            s.AttachReceiptAsync(pay.Id, new MemoryStream("MZ-not-a-pdf"u8.ToArray()), "dekont.pdf")));
        var pdf = "%PDF-1.4 dekont"u8.ToArray();
        var withReceipt = await App.Use<PaymentService, PaymentDto>(s => s.AttachReceiptAsync(pay.Id, new MemoryStream(pdf), "dekont.pdf"));
        Assert.True(withReceipt.HasReceipt);
        var (stream, type, name) = await App.Use<PaymentService, (Stream, string, string)>(s => s.OpenReceiptAsync(pay.Id));
        await using (stream) { using var ms = new MemoryStream(); await stream.CopyToAsync(ms); Assert.Equal(pdf, ms.ToArray()); }
        Assert.Equal("application/pdf", type);
        Assert.Equal("dekont.pdf", name);
    }

    [Fact]
    public async Task Manual_return_and_adjustment_entries_move_balance_and_restock()
    {
        var c = await App.NewCustomer();
        var p = await App.NewProduct(sale: 100m, stock: 100);
        await App.NewSale(c.Id, p, 10);                      // 1.100, stok 90
        await App.Use<LedgerService, LedgerEntryDto>(l => l.AddManualEntryAsync(c.Id,
            new ManualLedgerRequest(LedgerEntryType.Return, App.Clock.Today, false, 220m, "2 kg iade", [new ReturnLine(p.Id, 2)])));
        Assert.Equal(880m, await App.Balance(c.Id));
        Assert.Equal(92m, (await App.Use<ProductService, ProductDto>(s => s.GetAsync(p.Id))).StockQuantity);

        await App.Use<LedgerService, LedgerEntryDto>(l => l.AddManualEntryAsync(c.Id,
            new ManualLedgerRequest(LedgerEntryType.Adjustment, App.Clock.Today, true, 50m, "Nakliye farkı")));
        Assert.Equal(930m, await App.Balance(c.Id));

        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<LedgerService, LedgerEntryDto>(l => l.AddManualEntryAsync(c.Id,
            new ManualLedgerRequest(LedgerEntryType.Return, App.Clock.Today, true, 10m, "yanlış yön"))));
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<LedgerService, LedgerEntryDto>(l => l.AddManualEntryAsync(c.Id,
            new ManualLedgerRequest(LedgerEntryType.Sale, App.Clock.Today, true, 10m, "izinsiz tür"))));
    }

    [Fact]
    public async Task Overdue_amount_counts_expired_items_and_offsets_unallocated_credit()
    {
        // Açılış: 10.000 (vadesi geçmiş). Satış: 1.100, vadesi gelecekte.
        var c = await App.NewCustomer(opening: 10_000m, termDays: 30);
        var p = await App.NewProduct(sale: 100m, stock: 100);
        await App.NewSale(c.Id, p, 10);

        var f = await App.Use<LedgerService, CustomerFinancials>(l => l.GetFinancialsAsync(c.Id));
        Assert.Equal(11_100m, f.Balance);
        Assert.Equal(10_000m, f.OverdueAmount);

        // 4.000 TL dağıtılmamış tahsilat önce vadesi geçmiş borca sayılır
        await App.NewPayment(c.Id, 4_000m);
        f = await App.Use<LedgerService, CustomerFinancials>(l => l.GetFinancialsAsync(c.Id));
        Assert.Equal(7_100m, f.Balance);
        Assert.Equal(6_000m, f.OverdueAmount);

        // Vadesi geçmiş borç bakiyeyi aşamaz
        await App.NewPayment(c.Id, 7_000m);
        f = await App.Use<LedgerService, CustomerFinancials>(l => l.GetFinancialsAsync(c.Id));
        Assert.Equal(100m, f.Balance);
        Assert.Equal(0m, f.OverdueAmount);
        Assert.Equal(App.Clock.Today, f.LastPaymentDate);
        Assert.Equal(App.Clock.Today, f.LastSaleDate);
    }

    [Fact]
    public async Task Overdue_sale_becomes_overdue_after_due_date_passes()
    {
        var c = await App.NewCustomer(termDays: 10);
        var p = await App.NewProduct(sale: 100m, stock: 100);
        await App.NewSale(c.Id, p, 10);
        Assert.Equal(0m, (await App.Use<LedgerService, CustomerFinancials>(l => l.GetFinancialsAsync(c.Id))).OverdueAmount);
        App.Clock.Today = App.Clock.Today.AddDays(11);
        Assert.Equal(1_100m, (await App.Use<LedgerService, CustomerFinancials>(l => l.GetFinancialsAsync(c.Id))).OverdueAmount);
        var report = await App.Use<ReportService, ReceivablesReportDto>(r => r.ReceivablesAsync(true, null));
        Assert.Equal(1_100m, report.TotalOverdue);
    }

    [Fact]
    public async Task Customer_list_shows_balance_overdue_and_last_dates_and_filters()
    {
        var a = await App.NewCustomer("Alacaklı A", opening: 5_000m);
        var b = await App.NewCustomer("Temiz B");
        await App.NewPayment(a.Id, 1_000m);

        var all = await App.Use<CustomerService, PagedResult<CustomerListItem>>(s => s.ListAsync(null, "balance", true, null, null, 1, 25));
        Assert.Equal(["Alacaklı A", "Temiz B"], all.Items.Select(i => i.Name));
        Assert.Equal(4_000m, all.Items[0].Balance);
        Assert.Equal(4_000m, all.Items[0].OverdueAmount);       // açılış tarihi geçmişte
        Assert.Equal(App.Clock.Today, all.Items[0].LastPaymentDate);
        Assert.Null(all.Items[1].LastSaleDate);

        var debtors = await App.Use<CustomerService, PagedResult<CustomerListItem>>(s => s.ListAsync(null, null, false, "debt", null, 1, 25));
        Assert.Single(debtors.Items);
        var clear = await App.Use<CustomerService, PagedResult<CustomerListItem>>(s => s.ListAsync(null, null, false, "clear", null, 1, 25));
        Assert.Equal(b.Id, clear.Items.Single().Id);
        var paged = await App.Use<CustomerService, PagedResult<CustomerListItem>>(s => s.ListAsync(null, null, false, null, null, 2, 1));
        Assert.Equal(2, paged.Total);
        Assert.Single(paged.Items);
    }
}
