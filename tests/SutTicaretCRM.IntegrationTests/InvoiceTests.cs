using Microsoft.EntityFrameworkCore;
using SutTicaretCRM.Application;
using SutTicaretCRM.Application.Services;
using SutTicaretCRM.Domain;

namespace SutTicaretCRM.IntegrationTests;

[Collection("pg")]
public class InvoiceDraftTests(PostgresFixture pg) : IntegrationTestBase(pg)
{
    private async Task<(CustomerDetailDto C, ProductDto P, SaleDto S)> Setup(decimal qty = 10, EInvoiceTaxpayerStatus einv = EInvoiceTaxpayerStatus.Unknown)
    {
        var c = await App.NewCustomer("Fatura Müşterisi", einv: einv);
        var p = await App.NewProduct(sale: 100m, stock: 1000);
        var s = await App.NewSale(c.Id, p, qty);
        return (c, p, s);
    }

    [Fact] // Test 16
    public async Task Draft_invoice_is_created_from_sale_with_snapshot_and_no_official_data()
    {
        var (c, _, sale) = await Setup();
        var inv = await App.Use<InvoiceService, InvoiceDto>(s => s.CreateDraftFromSaleAsync(sale.Id, null));

        Assert.StartsWith("TSL-2026-", inv.InternalRef);
        Assert.Equal(InvoiceInternalStatus.Draft, inv.InternalStatus);
        Assert.Equal(OfficialInvoiceStatus.NotIssued, inv.OfficialStatus);
        Assert.Null(inv.OfficialNumber);
        Assert.Null(inv.OfficialUuid);
        Assert.Equal(OfficialSource.None, inv.OfficialSource);
        Assert.Equal(sale.Id, inv.SaleId);
        Assert.Equal(sale.GrandTotal, inv.GrandTotal);
        Assert.Equal(sale.NetTotal, inv.NetTotal);
        Assert.Equal(sale.VatTotal, inv.VatTotal);
        Assert.Equal(sale.Items.Count, inv.Items.Count);
        Assert.Equal(c.Name, inv.BuyerName);
        Assert.Equal(InvoiceDocumentType.Undetermined, inv.DocumentType);   // mükellefiyet bilinmiyor → varsayım yok
        Assert.Contains(inv.Issues, i => i.Field == "documentType" && i.IsBlocking);
        Assert.Contains(inv.Issues, i => i.Field == "buyerTaxNumber" && i.IsBlocking);

        // Satış faturayla ilişkili görünür
        var reloaded = await App.Use<SaleService, SaleDto>(s => s.GetAsync(sale.Id));
        Assert.Equal(inv.Id, reloaded.Invoices.Single().Id);
    }

    [Fact]
    public async Task Draft_can_only_be_created_from_approved_sale_and_only_once()
    {
        var c = await App.NewCustomer();
        var p = await App.NewProduct(stock: 100);
        var draftSale = await App.NewSale(c.Id, p, 1, approve: false);
        await Assert.ThrowsAsync<ConflictException>(() => App.Use<InvoiceService, InvoiceDto>(s => s.CreateDraftFromSaleAsync(draftSale.Id, null)));

        var sale = await App.NewSale(c.Id, p, 1);
        await App.Use<InvoiceService, InvoiceDto>(s => s.CreateDraftFromSaleAsync(sale.Id, null));
        await Assert.ThrowsAsync<ConflictException>(() => App.Use<InvoiceService, InvoiceDto>(s => s.CreateDraftFromSaleAsync(sale.Id, null)));
    }

    [Fact]
    public async Task Draft_creation_is_idempotent_with_client_key()
    {
        var (_, _, sale) = await Setup();
        var key = Guid.NewGuid();
        var a = await App.Use<InvoiceService, InvoiceDto>(s => s.CreateDraftFromSaleAsync(sale.Id, key));
        var b = await App.Use<InvoiceService, InvoiceDto>(s => s.CreateDraftFromSaleAsync(sale.Id, key));
        Assert.Equal(a.Id, b.Id);
        Assert.Equal(1, await App.Db(db => db.Invoices.CountAsync()));
    }

    [Fact] // Test 17
    public async Task Draft_can_be_edited_and_edits_are_validated()
    {
        var (_, _, sale) = await Setup();
        var inv = await App.Use<InvoiceService, InvoiceDto>(s => s.CreateDraftFromSaleAsync(sale.Id, null));
        var line = inv.Items[0];

        var edited = await App.Use<InvoiceService, InvoiceDto>(s => s.UpdateDraftAsync(inv.Id, new UpdateInvoiceRequest(
            App.Clock.Today, App.Clock.Today.AddDays(30), InvoiceDocumentType.EArchive, "Düzeltilmiş Unvan A.Ş.", "Çankaya VD", null, "10000000146",
            "Yeni Mah. No:5", "Ankara", "Çankaya", "muhasebe@test.example", "Elden teslim", [new InvoiceLineEdit(line.Id, "Tam yağlı beyaz peynir")])));

        Assert.Equal("Düzeltilmiş Unvan A.Ş.", edited.BuyerName);
        Assert.Equal(InvoiceDocumentType.EArchive, edited.DocumentType);
        Assert.Equal("Tam yağlı beyaz peynir", edited.Items[0].Description);
        Assert.Equal("Elden teslim", edited.Note);
        Assert.Equal(sale.GrandTotal, edited.GrandTotal);               // tutarlar satıştan gelir, değişmez
        Assert.DoesNotContain(edited.Issues, i => i.IsBlocking);

        UpdateInvoiceRequest Bad(string name, string? vkn, string? tckn) => new(App.Clock.Today, null, InvoiceDocumentType.EArchive, name, null, vkn, tckn, null, null, null, null, null, null);
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<InvoiceService, InvoiceDto>(s => s.UpdateDraftAsync(inv.Id, Bad("", null, null))));
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<InvoiceService, InvoiceDto>(s => s.UpdateDraftAsync(inv.Id, Bad("X", "1234567891", null))));
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<InvoiceService, InvoiceDto>(s => s.UpdateDraftAsync(inv.Id, Bad("X", null, "12345678901"))));
        var past = Bad("X", null, "10000000146") with { DueDate = App.Clock.Today.AddDays(-1) };
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<InvoiceService, InvoiceDto>(s => s.UpdateDraftAsync(inv.Id, past)));
    }

    [Fact]
    public async Task Internal_approval_requires_valid_buyer_and_document_type()
    {
        var (_, _, sale) = await Setup();
        var inv = await App.Use<InvoiceService, InvoiceDto>(s => s.CreateDraftFromSaleAsync(sale.Id, null));
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<InvoiceService, InvoiceDto>(s => s.ApproveAsync(inv.Id)));
        var approved = await App.ApproveDraft(inv.Id);
        Assert.Equal(InvoiceInternalStatus.Approved, approved.InternalStatus);
    }

    [Fact]
    public async Task Document_type_conflicting_with_known_einvoice_status_is_blocked_and_unknown_status_warns()
    {
        var (c, _, sale) = await Setup(einv: EInvoiceTaxpayerStatus.Registered);
        var inv = await App.Use<InvoiceService, InvoiceDto>(s => s.CreateDraftFromSaleAsync(sale.Id, null));
        Assert.Equal(InvoiceDocumentType.EInvoice, inv.DocumentType);   // bilinen mükellef → öneri
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.ApproveDraft(inv.Id, InvoiceDocumentType.EArchive));

        var (c2, _, sale2) = await Setup();   // Bilinmiyor
        var inv2 = await App.Use<InvoiceService, InvoiceDto>(s => s.CreateDraftFromSaleAsync(sale2.Id, null));
        Assert.Contains(inv2.Issues, i => !i.IsBlocking && i.Message.Contains("DOĞRULANMADI"));
    }

    [Fact] // Test 18
    public async Task Draft_can_be_cancelled_internally_and_leaves_audit_trail_and_no_financial_effect()
    {
        var (c, _, sale) = await Setup();
        var before = await App.Balance(c.Id);
        var inv = await App.Use<InvoiceService, InvoiceDto>(s => s.CreateDraftFromSaleAsync(sale.Id, null));

        await Assert.ThrowsAsync<DomainException>(() => App.Use<InvoiceService, InvoiceDto>(s => s.CancelDraftAsync(inv.Id, " ")));
        var cancelled = await App.Use<InvoiceService, InvoiceDto>(s => s.CancelDraftAsync(inv.Id, "Müşteri talebi"));

        Assert.Equal(InvoiceInternalStatus.Cancelled, cancelled.InternalStatus);
        Assert.Equal(OfficialInvoiceStatus.NotIssued, cancelled.OfficialStatus);
        Assert.Equal("Müşteri talebi", cancelled.CancelReason);
        Assert.Equal(1, await App.Db(db => db.Invoices.CountAsync(i => i.Id == inv.Id)));      // silinmedi
        Assert.True(await App.Db(db => db.AuditLogs.AnyAsync(a => a.Action == "Fatura taslağı iptal edildi" && a.EntityId == inv.Id)));
        Assert.Equal(before, await App.Balance(c.Id));
        Assert.Equal(SaleStatus.Approved, (await App.Use<SaleService, SaleDto>(s => s.GetAsync(sale.Id))).Status);

        // İptalden sonra aynı satış için yeni taslak açılabilir
        var again = await App.Use<InvoiceService, InvoiceDto>(s => s.CreateDraftFromSaleAsync(sale.Id, null));
        Assert.NotEqual(inv.Id, again.Id);
        // ... ama iptal edilmiş taslak düzenlenemez
        await Assert.ThrowsAsync<ConflictException>(() => App.Use<InvoiceService, InvoiceDto>(s => s.UpdateDraftAsync(inv.Id,
            new UpdateInvoiceRequest(App.Clock.Today, null, InvoiceDocumentType.EArchive, "X", null, null, "10000000146", null, null, null, null, null, null))));
    }

    [Fact]
    public async Task Database_allows_only_one_active_invoice_per_sale()
    {
        var (c, _, sale) = await Setup();
        await App.Use<InvoiceService, InvoiceDto>(s => s.CreateDraftFromSaleAsync(sale.Id, null));
        await Assert.ThrowsAsync<DbUpdateException>(() => App.Db(async db =>
        {
            db.Invoices.Add(new Invoice { InternalRef = "TSL-X-1", CustomerId = c.Id, SaleId = sale.Id, InvoiceDate = App.Clock.Today, BuyerName = "x" });
            return await db.SaveChangesAsync();
        }));
    }

    [Fact] // Test 19 + "çift borç" kuralı
    public async Task Internal_approval_and_official_record_are_separate_and_never_change_the_ledger()
    {
        var (c, _, sale) = await Setup();
        var balance = await App.Balance(c.Id);
        var ledgerCount = await App.Db(db => db.CustomerLedgerEntries.CountAsync());

        var approved = await App.NewApprovedInvoice(sale);
        Assert.Equal(InvoiceInternalStatus.Approved, approved.InternalStatus);
        Assert.Equal(OfficialInvoiceStatus.NotIssued, approved.OfficialStatus);       // iç onay ≠ resmî belge
        Assert.Null(approved.OfficialNumber);

        var waiting = await App.Use<InvoiceService, InvoiceDto>(s => s.MarkAwaitingOfficialAsync(approved.Id));
        Assert.Equal(InvoiceInternalStatus.AwaitingOfficial, waiting.InternalStatus);
        Assert.Equal(OfficialInvoiceStatus.NotIssued, waiting.OfficialStatus);

        var issued = await App.RecordOfficial(approved.Id, "GIB2026000000042", Guid.NewGuid().ToString());
        Assert.Equal(InvoiceInternalStatus.Completed, issued.InternalStatus);
        Assert.Equal(OfficialInvoiceStatus.Issued, issued.OfficialStatus);
        Assert.Equal("GIB2026000000042", issued.OfficialNumber);
        Assert.Equal(OfficialSource.ManualEntry, issued.OfficialSource);
        Assert.False(issued.OfficialVerified);                                         // GİB'den doğrulanmış gibi gösterilmez
        Assert.False(issued.ProviderIsLegallyValid);
        Assert.NotNull(issued.OfficialUuid);

        // Fatura cariyi ikinci kez artırmadı
        Assert.Equal(balance, await App.Balance(c.Id));
        Assert.Equal(ledgerCount, await App.Db(db => db.CustomerLedgerEntries.CountAsync()));
        Assert.Equal(1, await App.Db(db => db.CustomerLedgerEntries.CountAsync(e => e.EntryType == LedgerEntryType.Sale)));
    }

    [Fact]
    public async Task Official_record_requires_approval_and_validates_input()
    {
        var (_, _, sale) = await Setup();
        var draft = await App.Use<InvoiceService, InvoiceDto>(s => s.CreateDraftFromSaleAsync(sale.Id, null));
        await Assert.ThrowsAsync<DomainException>(() => App.RecordOfficial(draft.Id, "ABC2026000000001"));            // onaylanmamış
        var approved = await App.ApproveDraft(draft.Id);
        Assert.Equal(draft.Id, approved.Id);

        await Assert.ThrowsAsync<ValidationFailedException>(() => App.RecordOfficial(approved.Id, " "));
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.RecordOfficial(approved.Id, "ABC1", "bu-bir-uuid-degil"));
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<InvoiceService, InvoiceDto>(s => s.RecordOfficialAsync(approved.Id,
            new RecordOfficialRequest("ABC1", App.Clock.Today.AddDays(10), null, InvoiceDocumentType.EArchive))));
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<InvoiceService, InvoiceDto>(s => s.RecordOfficialAsync(approved.Id,
            new RecordOfficialRequest("ABC1", App.Clock.Today, null, InvoiceDocumentType.Undetermined))));
        Assert.Equal(OfficialInvoiceStatus.NotIssued, (await App.Use<InvoiceService, InvoiceDto>(s => s.GetAsync(approved.Id))).OfficialStatus);
    }

    [Fact] // Test 14
    public async Task Same_official_invoice_cannot_be_imported_twice()
    {
        var c = await App.NewCustomer();
        var p = await App.NewProduct(sale: 100m, stock: 1000);
        var sale1 = await App.NewSale(c.Id, p, 1);
        var sale2 = await App.NewSale(c.Id, p, 2);
        var uuid = Guid.NewGuid().ToString();

        var inv1 = await App.NewApprovedInvoice(sale1);
        var inv2 = await App.NewApprovedInvoice(sale2);
        await App.RecordOfficial(inv1.Id, "GIB2026000000001", uuid);

        // aynı numara
        var byNumber = await Assert.ThrowsAsync<ConflictException>(() => App.RecordOfficial(inv2.Id, "gib2026000000001"));
        Assert.Contains("ikinci kez", byNumber.Message);
        // aynı ETTN, farklı numara
        await Assert.ThrowsAsync<ConflictException>(() => App.RecordOfficial(inv2.Id, "GIB2026000000002", uuid.ToUpperInvariant()));
        // aynı faturaya ikinci kez de kaydedilemez
        await Assert.ThrowsAnyAsync<Exception>(() => App.RecordOfficial(inv1.Id, "GIB2026000000099"));

        var second = await App.Use<InvoiceService, InvoiceDto>(s => s.GetAsync(inv2.Id));
        Assert.Equal(OfficialInvoiceStatus.NotIssued, second.OfficialStatus);
        Assert.Null(second.OfficialNumber);
        Assert.Equal(1, await App.Db(db => db.Invoices.CountAsync(i => i.OfficialNumber != null)));

        // Servis kontrolü atlansa bile veritabanı kısıtı engeller
        await Assert.ThrowsAsync<DbUpdateException>(() => App.Db(async db =>
        {
            var i = await db.Invoices.FirstAsync(x => x.Id == inv2.Id);
            i.OfficialNumber = "GIB2026000000001";
            i.OfficialStatus = OfficialInvoiceStatus.Issued;
            return await db.SaveChangesAsync();
        }));
    }

    [Fact]
    public async Task Database_refuses_issued_status_without_official_number()
    {
        var (_, _, sale) = await Setup();
        var inv = await App.Use<InvoiceService, InvoiceDto>(s => s.CreateDraftFromSaleAsync(sale.Id, null));
        await Assert.ThrowsAsync<DbUpdateException>(() => App.Db(async db =>
        {
            var i = await db.Invoices.FirstAsync(x => x.Id == inv.Id);
            i.OfficialStatus = OfficialInvoiceStatus.Issued;
            return await db.SaveChangesAsync();
        }));
    }

    [Fact] // Test 20
    public async Task Official_cancellation_is_not_applied_without_confirmation()
    {
        var (c, _, sale) = await Setup();
        var inv = await App.NewApprovedInvoice(sale);
        await App.RecordOfficial(inv.Id, "GIB2026000000010");

        // Taslak iptal yolu resmî belge için kapalı
        await Assert.ThrowsAsync<DomainException>(() => App.Use<InvoiceService, InvoiceDto>(s => s.CancelDraftAsync(inv.Id, "iptal")));
        // Satış da iptal edilemez
        await Assert.ThrowsAsync<ConflictException>(() => App.Use<SaleService, SaleDto>(s => s.CancelAsync(sale.Id, "iptal")));

        var req = await App.Use<InvoiceService, InvoiceDto>(s => s.RequestCancellationAsync(inv.Id, "Hatalı tutar"));
        Assert.Equal(InvoiceInternalStatus.CancellationRequired, req.InternalStatus);
        Assert.Equal(OfficialInvoiceStatus.Issued, req.OfficialStatus);             // hâlâ resmî olarak geçerli
        Assert.Equal("GIB2026000000010", req.OfficialNumber);

        var confirmDate = App.Clock.Today;
        await Assert.ThrowsAsync<DomainException>(() => App.Use<InvoiceService, InvoiceDto>(s => s.ConfirmOfficialCancellationAsync(inv.Id,
            new ConfirmCancellationRequest(false, "Portal iptal no 1", confirmDate, true))));
        await Assert.ThrowsAsync<DomainException>(() => App.Use<InvoiceService, InvoiceDto>(s => s.ConfirmOfficialCancellationAsync(inv.Id,
            new ConfirmCancellationRequest(true, "", confirmDate, true))));

        var still = await App.Use<InvoiceService, InvoiceDto>(s => s.GetAsync(inv.Id));
        Assert.Equal(OfficialInvoiceStatus.Issued, still.OfficialStatus);
        Assert.Equal(InvoiceInternalStatus.CancellationRequired, still.InternalStatus);
        Assert.Equal(SaleStatus.Approved, (await App.Use<SaleService, SaleDto>(s => s.GetAsync(sale.Id))).Status);
        Assert.Equal(sale.GrandTotal, await App.Balance(c.Id));                     // cari etkilenmedi
    }

    [Fact] // Test 22: onaylı resmî iptal → fatura + satış + cari + stok tek işlemde
    public async Task Confirmed_official_cancellation_with_sale_cancel_applies_all_effects_consistently()
    {
        var c = await App.NewCustomer();
        var p = await App.NewProduct(sale: 100m, stock: 100);
        var sale = await App.NewSale(c.Id, p, 10);
        var inv = await App.NewApprovedInvoice(sale);
        await App.RecordOfficial(inv.Id, "GIB2026000000011");
        await App.Use<InvoiceService, InvoiceDto>(s => s.RequestCancellationAsync(inv.Id, "Mal iade edildi"));

        var done = await App.Use<InvoiceService, InvoiceDto>(s => s.ConfirmOfficialCancellationAsync(inv.Id,
            new ConfirmCancellationRequest(true, "Portal iptal talebi no: 77", App.Clock.Today, CancelSale: true)));

        Assert.Equal(OfficialInvoiceStatus.Cancelled, done.OfficialStatus);
        Assert.Equal(InvoiceInternalStatus.Cancelled, done.InternalStatus);
        Assert.Equal("Portal iptal talebi no: 77", done.OfficialCancelReference);
        Assert.Equal("GIB2026000000011", done.OfficialNumber);                      // iz korunur
        Assert.Equal(SaleStatus.Cancelled, (await App.Use<SaleService, SaleDto>(s => s.GetAsync(sale.Id))).Status);
        Assert.Equal(0m, await App.Balance(c.Id));
        Assert.Equal(100m, (await App.Use<ProductService, ProductDto>(s => s.GetAsync(p.Id))).StockQuantity);

        // Raporlar: resmî fatura ve satış sayılmaz
        var m = await App.Use<ReportService, MeasuresDto>(r => r.MeasuresAsync(App.Clock.Today, App.Clock.Today, null));
        Assert.Equal(0m, m.CommercialSales.Amount);
        Assert.Equal(0m, m.OfficialInvoices.Amount);
        var open = await App.Use<ReportService, OpenInvoicesDto>(r => r.OpenInvoicesAsync(null));
        Assert.Empty(open.Rows);
    }

    [Fact]
    public async Task Official_cancellation_without_sale_cancel_keeps_the_sale_and_allows_reinvoicing()
    {
        var (c, _, sale) = await Setup();
        var inv = await App.NewApprovedInvoice(sale);
        await App.RecordOfficial(inv.Id, "GIB2026000000012");
        await App.Use<InvoiceService, InvoiceDto>(s => s.RequestCancellationAsync(inv.Id, "Alıcı unvanı hatalı"));
        await App.Use<InvoiceService, InvoiceDto>(s => s.ConfirmOfficialCancellationAsync(inv.Id, new ConfirmCancellationRequest(true, "Portal ref", App.Clock.Today, false)));

        Assert.Equal(SaleStatus.Approved, (await App.Use<SaleService, SaleDto>(s => s.GetAsync(sale.Id))).Status);
        Assert.Equal(sale.GrandTotal, await App.Balance(c.Id));
        var replacement = await App.Use<InvoiceService, InvoiceDto>(s => s.CreateDraftFromSaleAsync(sale.Id, null));
        Assert.NotEqual(inv.Id, replacement.Id);
    }

    [Fact]
    public async Task Cancellation_request_can_be_withdrawn_and_requires_issued_invoice()
    {
        var (_, _, sale) = await Setup();
        var inv = await App.NewApprovedInvoice(sale);
        await Assert.ThrowsAsync<DomainException>(() => App.Use<InvoiceService, InvoiceDto>(s => s.RequestCancellationAsync(inv.Id, "x")));
        await App.RecordOfficial(inv.Id, "GIB2026000000013");
        await App.Use<InvoiceService, InvoiceDto>(s => s.RequestCancellationAsync(inv.Id, "x"));
        var back = await App.Use<InvoiceService, InvoiceDto>(s => s.WithdrawCancellationRequestAsync(inv.Id));
        Assert.Equal(InvoiceInternalStatus.Completed, back.InternalStatus);
    }

    [Fact]
    public async Task Cancelling_a_sale_cancels_its_unissued_invoice_draft_automatically()
    {
        var (_, _, sale) = await Setup();
        var draft = await App.Use<InvoiceService, InvoiceDto>(s => s.CreateDraftFromSaleAsync(sale.Id, null));
        await App.Use<SaleService, SaleDto>(s => s.CancelAsync(sale.Id, "Hatalı satış"));
        var after = await App.Use<InvoiceService, InvoiceDto>(s => s.GetAsync(draft.Id));
        Assert.Equal(InvoiceInternalStatus.Cancelled, after.InternalStatus);
        Assert.Equal(OfficialInvoiceStatus.NotIssued, after.OfficialStatus);
    }

    [Fact]
    public async Task Invoice_list_shows_paid_remaining_and_statuses_separately()
    {
        var (c, _, sale) = await Setup();
        var inv = await App.NewApprovedInvoice(sale);
        await App.NewPayment(c.Id, 400m, alloc: [new AllocationRequest(sale.Id, 400m)]);
        await App.RecordOfficial(inv.Id, "GIB2026000000020");

        var list = await App.Use<InvoiceService, PagedResult<InvoiceListItem>>(s => s.ListAsync(null, null, null, null, null, null, null, null, 1, 25));
        var row = list.Items.Single();
        Assert.Equal(400m, row.PaidTotal);
        Assert.Equal(sale.GrandTotal - 400m, row.Remaining);
        Assert.Equal(InvoiceInternalStatus.Completed, row.InternalStatus);
        Assert.Equal(OfficialInvoiceStatus.Issued, row.OfficialStatus);
        Assert.Equal("GIB2026000000020", row.OfficialNumber);

        var open = await App.Use<InvoiceService, PagedResult<InvoiceListItem>>(s => s.ListAsync(null, null, null, null, null, null, true, null, 1, 25));
        Assert.Single(open.Items);
        await App.NewPayment(c.Id, sale.GrandTotal - 400m, alloc: [new AllocationRequest(sale.Id, sale.GrandTotal - 400m)]);
        open = await App.Use<InvoiceService, PagedResult<InvoiceListItem>>(s => s.ListAsync(null, null, null, null, null, null, true, null, 1, 25));
        Assert.Empty(open.Items);
        var searched = await App.Use<InvoiceService, PagedResult<InvoiceListItem>>(s => s.ListAsync(null, null, null, null, null, "0020", null, null, 1, 25));
        Assert.Single(searched.Items);
    }

    [Fact]
    public async Task Payment_allocation_remembers_the_invoice_of_the_sale()
    {
        var (c, _, sale) = await Setup();
        var inv = await App.NewApprovedInvoice(sale);
        var pay = await App.NewPayment(c.Id, 100m, alloc: [new AllocationRequest(sale.Id, 100m)]);
        Assert.Equal(inv.Id, pay.Allocations.Single().InvoiceId);
        Assert.Equal(inv.InternalRef, pay.Allocations.Single().InvoiceRef);
    }

    // ───────────── Ekler ─────────────

    [Fact]
    public async Task Pdf_and_xml_attachments_can_be_linked_and_downloaded_and_bad_files_are_rejected()
    {
        var (_, _, sale) = await Setup();
        var inv = await App.NewApprovedInvoice(sale);
        var pdf = "%PDF-1.7\n%âãÏÓ\nfatura"u8.ToArray();
        var xml = "<?xml version=\"1.0\" encoding=\"UTF-8\"?><Invoice><UUID>x</UUID></Invoice>"u8.ToArray();

        var a1 = await App.Use<InvoiceService, AttachmentDto>(s => s.AddAttachmentAsync(inv.Id, new MemoryStream(pdf), "GIB2026000000030.pdf"));
        var a2 = await App.Use<InvoiceService, AttachmentDto>(s => s.AddAttachmentAsync(inv.Id, new MemoryStream(xml), "GIB2026000000030.xml"));
        Assert.Equal(AttachmentKind.Pdf, a1.Kind);
        Assert.Equal(AttachmentKind.Xml, a2.Kind);

        await Assert.ThrowsAsync<ConflictException>(() => App.Use<InvoiceService, AttachmentDto>(s => s.AddAttachmentAsync(inv.Id, new MemoryStream(pdf), "kopya.pdf")));   // aynı içerik
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<InvoiceService, AttachmentDto>(s => s.AddAttachmentAsync(inv.Id, new MemoryStream("MZ.."u8.ToArray()), "x.pdf")));
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<InvoiceService, AttachmentDto>(s => s.AddAttachmentAsync(inv.Id, new MemoryStream(pdf), "x.exe")));
        await Assert.ThrowsAsync<NotFoundException>(() => App.Use<InvoiceService, AttachmentDto>(s => s.AddAttachmentAsync(Guid.NewGuid(), new MemoryStream(pdf), "y.pdf")));

        var loaded = await App.Use<InvoiceService, InvoiceDto>(s => s.GetAsync(inv.Id));
        Assert.Equal(2, loaded.Attachments.Count);
        var (stream, type, name) = await App.Use<InvoiceService, (Stream, string, string)>(s => s.OpenAttachmentAsync(inv.Id, a1.Id));
        await using (stream) { using var ms = new MemoryStream(); await stream.CopyToAsync(ms); Assert.Equal(pdf, ms.ToArray()); }
        Assert.Equal("application/pdf", type);
        Assert.Equal("GIB2026000000030.pdf", name);

        // Başka faturanın eki istenemez
        var other = await App.NewSale((await App.NewCustomer()).Id, await App.NewProduct(stock: 10), 1);
        var otherInv = await App.NewApprovedInvoice(other);
        await Assert.ThrowsAsync<NotFoundException>(() => App.Use<InvoiceService, (Stream, string, string)>(s => s.OpenAttachmentAsync(otherInv.Id, a1.Id)));
    }

    [Fact]
    public async Task Cancelled_draft_does_not_accept_attachments()
    {
        var (_, _, sale) = await Setup();
        var inv = await App.Use<InvoiceService, InvoiceDto>(s => s.CreateDraftFromSaleAsync(sale.Id, null));
        await App.Use<InvoiceService, InvoiceDto>(s => s.CancelDraftAsync(inv.Id, "x"));
        await Assert.ThrowsAsync<ConflictException>(() => App.Use<InvoiceService, AttachmentDto>(s => s.AddAttachmentAsync(inv.Id, new MemoryStream("%PDF-1.4"u8.ToArray()), "a.pdf")));
    }
}

/// <summary>Sağlayıcı akışı: yalnızca SAHTE (Mock) sağlayıcıyla. Gerçek GİB'e hiçbir çağrı yapılmaz.</summary>
[Collection("pg")]
public class InvoiceProviderFlowTests(PostgresFixture pg) : IntegrationTestBase(pg)
{
    private async Task<InvoiceDto> ApprovedInvoice()
    {
        var c = await App.NewCustomer();
        var p = await App.NewProduct(sale: 100m, stock: 100);
        var sale = await App.NewSale(c.Id, p, 5);
        return await App.NewApprovedInvoice(sale);
    }

    private static ProviderActionRequest Ack(InvoiceDto i, bool confirm = true) => new(confirm, i.GrandTotal, i.BuyerName);

    [Fact]
    public async Task Manual_provider_cannot_submit_and_claims_no_capabilities()
    {
        var inv = await ApprovedInvoice();
        var info = await App.Use<InvoiceService, ProviderInfoDto>(s => s.GetProviderInfoAsync());
        Assert.Equal("Manual", info.Name);
        Assert.False(info.CanSubmit);
        Assert.False(info.Capabilities.CanCreateDraft);
        Assert.False(info.Capabilities.CanIssue);
        Assert.False(info.Capabilities.CanCancel);
        await Assert.ThrowsAsync<ConflictException>(() => App.Use<InvoiceService, InvoiceDto>(s => s.SubmitToProviderAsync(inv.Id, Ack(inv))));
        Assert.Equal(OfficialInvoiceStatus.NotIssued, (await App.Use<InvoiceService, InvoiceDto>(s => s.GetAsync(inv.Id))).OfficialStatus);
    }

    [Fact]
    public async Task Manual_provider_returns_not_supported_for_every_operation()
    {
        var manual = new SutTicaretCRM.Infrastructure.Providers.ManualPortalInvoiceProvider();
        var r = new ProviderReference(null, null, Guid.NewGuid());
        Assert.Equal(ProviderOutcome.NotSupported, (await manual.GetInvoiceStatusAsync(r)).Outcome);
        Assert.Equal(ProviderOutcome.NotSupported, (await manual.IssueAsync(r, new ProviderIssueInput(null))).Outcome);
        Assert.Equal(ProviderOutcome.NotSupported, (await manual.RequestCancellationAsync(r, "x")).Outcome);
        Assert.Equal(ProviderOutcome.NotSupported, (await manual.GetInvoiceDocumentAsync(r, ProviderDocumentFormat.Pdf)).Outcome);
    }

    [Fact] // Canlı gönderim kapalıyken canlı-yetenekli sağlayıcıya HİÇ çağrı yapılmaz
    public async Task Live_capable_provider_is_never_called_while_live_sending_is_disabled()
    {
        var inv = await ApprovedInvoice();
        await App.SetProvider("FakeLive");
        var info = await App.Use<InvoiceService, ProviderInfoDto>(s => s.GetProviderInfoAsync());
        Assert.False(info.LiveSendingEnabled);
        Assert.False(info.CanSubmit);
        var ex = await Assert.ThrowsAsync<ConflictException>(() => App.Use<InvoiceService, InvoiceDto>(s => s.SubmitToProviderAsync(inv.Id, Ack(inv))));
        Assert.Contains("Canlı resmî gönderim kapalı", ex.Message);
        await Assert.ThrowsAsync<ConflictException>(() => App.Use<InvoiceService, InvoiceDto>(s => s.IssueViaProviderAsync(inv.Id, Ack(inv))));
        Assert.Equal(0, App.Live.Calls);
    }

    [Fact]
    public async Task Provider_submission_requires_explicit_acknowledgement_of_document_and_buyer()
    {
        var inv = await ApprovedInvoice();
        await App.SetProvider("Mock");
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<InvoiceService, InvoiceDto>(s => s.SubmitToProviderAsync(inv.Id, Ack(inv, confirm: false))));
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<InvoiceService, InvoiceDto>(s => s.SubmitToProviderAsync(inv.Id, new ProviderActionRequest(true, inv.GrandTotal + 1, inv.BuyerName))));
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<InvoiceService, InvoiceDto>(s => s.SubmitToProviderAsync(inv.Id, new ProviderActionRequest(true, inv.GrandTotal, "Başka Firma"))));
        Assert.Equal(0, App.Mock.CreatedDocumentCount);
        Assert.Equal(OfficialInvoiceStatus.NotIssued, (await App.Use<InvoiceService, InvoiceDto>(s => s.GetAsync(inv.Id))).OfficialStatus);
    }

    [Fact]
    public async Task Provider_happy_path_creates_draft_then_requires_second_confirmation_to_issue()
    {
        var inv = await ApprovedInvoice();
        await App.SetProvider("Mock");

        var submitted = await App.Use<InvoiceService, InvoiceDto>(s => s.SubmitToProviderAsync(inv.Id, Ack(inv)));
        Assert.Equal(OfficialInvoiceStatus.Submitting, submitted.OfficialStatus);       // taslak: resmî değil
        Assert.Equal(InvoiceInternalStatus.Processing, submitted.InternalStatus);
        Assert.Null(submitted.OfficialNumber);
        Assert.NotNull(submitted.ProviderReference);
        Assert.Equal(1, App.Mock.CreatedDocumentCount);
        Assert.Equal(0, App.Mock.IssuedDocumentCount);

        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<InvoiceService, InvoiceDto>(s => s.IssueViaProviderAsync(inv.Id, Ack(inv, confirm: false))));
        var issued = await App.Use<InvoiceService, InvoiceDto>(s => s.IssueViaProviderAsync(inv.Id, Ack(inv)));
        Assert.Equal(OfficialInvoiceStatus.Issued, issued.OfficialStatus);
        Assert.Equal(InvoiceInternalStatus.Completed, issued.InternalStatus);
        Assert.Equal(OfficialSource.Provider, issued.OfficialSource);
        Assert.Equal("Mock", issued.OfficialProvider);
        Assert.True(issued.OfficialVerified);                  // sağlayıcı yanıtından geldi
        Assert.False(issued.ProviderIsLegallyValid);           // ama TEST sağlayıcısı: yasal geçerli değil
        Assert.StartsWith("TST", issued.OfficialNumber);
        Assert.Equal(1, App.Mock.IssuedDocumentCount);

        // İkinci gönderim engellenir
        await Assert.ThrowsAnyAsync<Exception>(() => App.Use<InvoiceService, InvoiceDto>(s => s.SubmitToProviderAsync(inv.Id, Ack(issued))));
        Assert.Equal(1, App.Mock.CreatedDocumentCount);
    }

    [Fact] // Test 29
    public async Task Network_timeout_after_draft_creation_does_not_create_duplicate_documents()
    {
        var inv = await ApprovedInvoice();
        await App.SetProvider("Mock");
        App.Mock.SimulateTimeoutAfterCreateDraft = true;

        var first = await App.Use<InvoiceService, InvoiceDto>(s => s.SubmitToProviderAsync(inv.Id, Ack(inv)));
        Assert.Equal(OfficialInvoiceStatus.ResultUnknown, first.OfficialStatus);   // "gönderilemedi" denmez: belirsiz
        Assert.Equal(1, App.Mock.CreatedDocumentCount);                            // sağlayıcıda oluştu

        // Kullanıcı tekrar "gönder"e basarsa: engellenir, ikinci belge oluşmaz
        await Assert.ThrowsAnyAsync<Exception>(() => App.Use<InvoiceService, InvoiceDto>(s => s.SubmitToProviderAsync(inv.Id, Ack(inv))));
        Assert.Equal(1, App.Mock.CreatedDocumentCount);

        // Durum sorgusu gerçeği bulur
        App.Mock.SimulateTimeoutAfterCreateDraft = false;
        var refreshed = await App.Use<InvoiceService, InvoiceDto>(s => s.RefreshStatusAsync(inv.Id));
        Assert.Equal(OfficialInvoiceStatus.Submitting, refreshed.OfficialStatus);
        Assert.NotNull(refreshed.ProviderReference);

        var issued = await App.Use<InvoiceService, InvoiceDto>(s => s.IssueViaProviderAsync(inv.Id, Ack(inv)));
        Assert.Equal(OfficialInvoiceStatus.Issued, issued.OfficialStatus);
        Assert.Equal(1, App.Mock.CreatedDocumentCount);
        Assert.Equal(1, App.Mock.IssuedDocumentCount);
    }

    [Fact] // Test 29
    public async Task Network_timeout_after_issue_is_resolved_by_status_query_without_second_document()
    {
        var inv = await ApprovedInvoice();
        await App.SetProvider("Mock");
        await App.Use<InvoiceService, InvoiceDto>(s => s.SubmitToProviderAsync(inv.Id, Ack(inv)));

        App.Mock.SimulateTimeoutAfterIssue = true;
        var ex = await Assert.ThrowsAsync<ConflictException>(() => App.Use<InvoiceService, InvoiceDto>(s => s.IssueViaProviderAsync(inv.Id, Ack(inv))));
        Assert.Contains("Yeniden göndermeyin", ex.Message);
        var unknown = await App.Use<InvoiceService, InvoiceDto>(s => s.GetAsync(inv.Id));
        Assert.Equal(OfficialInvoiceStatus.ResultUnknown, unknown.OfficialStatus);
        Assert.Null(unknown.OfficialNumber);                                       // doğrulanmadan resmî sayılmaz

        // Tekrar issue denemesi engellenir
        await Assert.ThrowsAnyAsync<Exception>(() => App.Use<InvoiceService, InvoiceDto>(s => s.IssueViaProviderAsync(inv.Id, Ack(inv))));

        App.Mock.SimulateTimeoutAfterIssue = false;
        var resolved = await App.Use<InvoiceService, InvoiceDto>(s => s.RefreshStatusAsync(inv.Id));
        Assert.Equal(OfficialInvoiceStatus.Issued, resolved.OfficialStatus);
        Assert.NotNull(resolved.OfficialNumber);
        Assert.Equal(1, App.Mock.IssuedDocumentCount);
        Assert.Equal(1, await App.Db(db => db.Invoices.CountAsync(i => i.OfficialNumber != null)));
    }

    [Fact]
    public async Task Rejected_submission_is_marked_failed_and_can_be_retried()
    {
        var inv = await ApprovedInvoice();
        await App.SetProvider("Mock");
        App.Mock.RejectNextDraft = true;
        var failed = await App.Use<InvoiceService, InvoiceDto>(s => s.SubmitToProviderAsync(inv.Id, Ack(inv)));
        Assert.Equal(OfficialInvoiceStatus.Failed, failed.OfficialStatus);
        Assert.Equal(InvoiceInternalStatus.Error, failed.InternalStatus);
        Assert.Equal(0, App.Mock.CreatedDocumentCount);

        var retry = await App.Use<InvoiceService, InvoiceDto>(s => s.SubmitToProviderAsync(inv.Id, Ack(inv)));
        Assert.Equal(OfficialInvoiceStatus.Submitting, retry.OfficialStatus);
        Assert.Equal(1, App.Mock.CreatedDocumentCount);
    }

    [Fact]
    public async Task Provider_cancellation_is_only_accepted_when_provider_confirms()
    {
        var inv = await ApprovedInvoice();
        await App.SetProvider("Mock");
        await App.Use<InvoiceService, InvoiceDto>(s => s.SubmitToProviderAsync(inv.Id, Ack(inv)));
        await App.Use<InvoiceService, InvoiceDto>(s => s.IssueViaProviderAsync(inv.Id, Ack(inv)));
        await App.Use<InvoiceService, InvoiceDto>(s => s.RequestCancellationAsync(inv.Id, "Hata"));

        // Kullanıcı "doğruladım" dese bile sağlayıcı iptal etmediyse kabul edilmez
        await Assert.ThrowsAsync<ConflictException>(() => App.Use<InvoiceService, InvoiceDto>(s => s.ConfirmOfficialCancellationAsync(inv.Id,
            new ConfirmCancellationRequest(true, "ben iptal ettim", App.Clock.Today, false))));
        Assert.Equal(OfficialInvoiceStatus.Issued, (await App.Use<InvoiceService, InvoiceDto>(s => s.GetAsync(inv.Id))).OfficialStatus);

        // Sağlayıcı iptal edince kabul edilir
        var loaded = await App.Use<InvoiceService, InvoiceDto>(s => s.GetAsync(inv.Id));
        await App.Mock.RequestCancellationAsync(new ProviderReference(loaded.ProviderReference, loaded.OfficialUuid, (await App.Db(db => db.Invoices.FirstAsync(i => i.Id == inv.Id))).SubmissionKey ?? Guid.Empty), "Hata");
        var done = await App.Use<InvoiceService, InvoiceDto>(s => s.ConfirmOfficialCancellationAsync(inv.Id,
            new ConfirmCancellationRequest(true, "sağlayıcı iptali", App.Clock.Today, false)));
        Assert.Equal(OfficialInvoiceStatus.Cancelled, done.OfficialStatus);
    }
}
