using System.Text;
using SutTicaretCRM.Application;
using SutTicaretCRM.Application.Services;
using SutTicaretCRM.Domain;
using SutTicaretCRM.Infrastructure.Persistence;
using SutTicaretCRM.Infrastructure.Services;

namespace SutTicaretCRM.UnitTests;

public class DocumentCalculatorTests
{
    [Fact] // Test 5-6: satış toplamı, KDV ve indirim hesabı
    public void Line_applies_percent_and_amount_discount_then_vat()
    {
        // 10 kg × 200 = 2.000 brüt; %5 indirim = 100 + 50 TL sabit indirim = 150; net 1.850; KDV %10 = 185; toplam 2.035
        var r = DocumentCalculator.CalculateLine(new LineInput(10, 200m, 5m, 50m, 10m));
        Assert.Equal(2000m, r.Gross);
        Assert.Equal(150m, r.Discount);
        Assert.Equal(1850m, r.Net);
        Assert.Equal(185m, r.Vat);
        Assert.Equal(2035m, r.Total);
    }

    [Fact]
    public void Vat_is_rounded_half_away_from_zero_to_kurus()
    {
        // 3 × 10,05 = 30,15; KDV %10 = 3,015 → 3,02
        var r = DocumentCalculator.CalculateLine(new LineInput(3, 10.05m, 0, 0, 10m));
        Assert.Equal(30.15m, r.Net);
        Assert.Equal(3.02m, r.Vat);
        Assert.Equal(33.17m, r.Total);
    }

    [Fact]
    public void Document_total_is_sum_of_rounded_lines()
    {
        var a = DocumentCalculator.CalculateLine(new LineInput(1, 0.05m, 0, 0, 10m)); // vat 0,005 → 0,01
        var b = DocumentCalculator.CalculateLine(new LineInput(1, 0.05m, 0, 0, 10m));
        var t = DocumentCalculator.Sum([a, b]);
        Assert.Equal(0.02m, t.Vat);
        Assert.Equal(t.Net + t.Vat, t.Total);
    }

    [Theory]
    [InlineData(0, 10, 0, 0, 10)]      // miktar 0
    [InlineData(1, -1, 0, 0, 10)]      // negatif fiyat
    [InlineData(1, 10, 101, 0, 10)]    // %101 indirim
    [InlineData(1, 10, 0, 11, 10)]     // indirim > tutar
    [InlineData(1, 10, 0, 0, 101)]     // KDV > 100
    public void Invalid_lines_are_rejected(decimal q, decimal p, decimal pct, decimal amt, decimal vat) =>
        Assert.Throws<DomainException>(() => DocumentCalculator.CalculateLine(new LineInput(q, p, pct, amt, vat)));

    [Fact]
    public void Zero_vat_line_has_no_tax()
    {
        var r = DocumentCalculator.CalculateLine(new LineInput(2, 50m, 0, 0, 0));
        Assert.Equal(0m, r.Vat);
        Assert.Equal(100m, r.Total);
    }
}

public class TurkishTaxIdTests
{
    [Fact]
    public void Valid_tckn_passes_and_invalid_fails()
    {
        Assert.True(TurkishTaxIds.IsValidTckn("10000000146"));
        Assert.False(TurkishTaxIds.IsValidTckn("10000000147"));
        Assert.False(TurkishTaxIds.IsValidTckn("00000000000"));
        Assert.False(TurkishTaxIds.IsValidTckn("123"));
        Assert.False(TurkishTaxIds.IsValidTckn(null));
    }

    [Fact]
    public void Vkn_check_digit_roundtrip()
    {
        Assert.True(TurkishTaxIds.IsValidVkn("1234567890"));
        Assert.False(TurkishTaxIds.IsValidVkn("1234567891"));
        Assert.False(TurkishTaxIds.IsValidVkn("12345678"));
        Assert.False(TurkishTaxIds.IsValidVkn("abcdefghij"));
    }
}

public class SaleStateTests
{
    private static Sale NewSale(decimal total = 1000m) => new()
    {
        Number = "SAT-1", GrandTotal = total, Items = [new SaleItem { Quantity = 1 }],
    };

    [Fact]
    public void Draft_sale_can_be_approved_once_only()
    {
        var s = NewSale();
        s.Approve(DateTime.UtcNow);
        Assert.Equal(SaleStatus.Approved, s.Status);
        Assert.Throws<DomainException>(() => s.Approve(DateTime.UtcNow)); // Test 15 (alan düzeyi)
    }

    [Fact]
    public void Sale_without_items_cannot_be_approved() =>
        Assert.Throws<DomainException>(() => new Sale { Number = "S" }.Approve(DateTime.UtcNow));

    [Fact]
    public void Payment_status_follows_allocations()
    {
        var s = NewSale(1000m);
        s.Approve(DateTime.UtcNow);
        s.RefreshPaymentStatus(0); Assert.Equal(SaleStatus.Approved, s.Status);
        s.RefreshPaymentStatus(400); Assert.Equal(SaleStatus.PartiallyPaid, s.Status);
        s.RefreshPaymentStatus(1000); Assert.Equal(SaleStatus.Paid, s.Status);
        s.RefreshPaymentStatus(600); Assert.Equal(SaleStatus.PartiallyPaid, s.Status); // tahsilat geri alındı
    }

    [Fact]
    public void Cancelled_sale_ignores_payment_refresh_and_needs_reason()
    {
        var s = NewSale();
        s.Approve(DateTime.UtcNow);
        Assert.Throws<DomainException>(() => s.Cancel("  ", DateTime.UtcNow));
        s.Cancel("hata", DateTime.UtcNow);
        s.RefreshPaymentStatus(1000);
        Assert.Equal(SaleStatus.Cancelled, s.Status);
        Assert.Throws<DomainException>(() => s.Cancel("tekrar", DateTime.UtcNow));
    }
}

public class InvoiceStateTests
{
    private static Invoice Draft() => new() { InternalRef = "TSL-2026-000001", BuyerName = "A", GrandTotal = 100m };

    private static Invoice ApprovedInvoice()
    {
        var i = Draft();
        i.Approve();
        return i;
    }

    private static Invoice IssuedInvoice()
    {
        var i = ApprovedInvoice();
        i.RecordOfficialDocument("ABC2026000000001", new DateOnly(2026, 10, 1), null, InvoiceDocumentType.EArchive, OfficialSource.ManualEntry, null, false, DateTime.UtcNow);
        return i;
    }

    [Fact] // Test 16
    public void New_invoice_is_draft_and_not_official()
    {
        var i = Draft();
        Assert.Equal(InvoiceInternalStatus.Draft, i.InternalStatus);
        Assert.Equal(OfficialInvoiceStatus.NotIssued, i.OfficialStatus);
        Assert.True(i.IsEditable);
        Assert.Null(i.OfficialNumber);
    }

    [Fact] // Test 19: iç onay resmî durumu DEĞİŞTİRMEZ
    public void Internal_approval_does_not_create_official_document()
    {
        var i = Draft();
        i.Approve();
        Assert.Equal(InvoiceInternalStatus.Approved, i.InternalStatus);
        Assert.Equal(OfficialInvoiceStatus.NotIssued, i.OfficialStatus);
        Assert.Null(i.OfficialNumber);
        Assert.False(i.IsEditable);

        i.MarkAwaitingOfficial();
        Assert.Equal(InvoiceInternalStatus.AwaitingOfficial, i.InternalStatus);
        Assert.Equal(OfficialInvoiceStatus.NotIssued, i.OfficialStatus);
    }

    [Fact] // Test 19: resmî kayıt iki durumu birlikte ama ayrı alanlarda günceller; doğrulanmış sayılmaz
    public void Manual_official_record_is_flagged_as_user_declared()
    {
        var i = IssuedInvoice();
        Assert.Equal(OfficialInvoiceStatus.Issued, i.OfficialStatus);
        Assert.Equal(InvoiceInternalStatus.Completed, i.InternalStatus);
        Assert.Equal(OfficialSource.ManualEntry, i.OfficialSource);
        Assert.False(i.OfficialVerified);
        Assert.Equal("ABC2026000000001", i.OfficialNumber);
    }

    [Fact]
    public void Official_record_requires_approval_number_and_type()
    {
        Assert.Throws<DomainException>(() => Draft().RecordOfficialDocument("X1", new DateOnly(2026, 1, 1), null, InvoiceDocumentType.EArchive, OfficialSource.ManualEntry, null, false, DateTime.UtcNow));
        var a = ApprovedInvoice();
        Assert.Throws<DomainException>(() => a.RecordOfficialDocument(" ", new DateOnly(2026, 1, 1), null, InvoiceDocumentType.EArchive, OfficialSource.ManualEntry, null, false, DateTime.UtcNow));
        Assert.Throws<DomainException>(() => a.RecordOfficialDocument("X1", new DateOnly(2026, 1, 1), null, InvoiceDocumentType.Undetermined, OfficialSource.ManualEntry, null, false, DateTime.UtcNow));
        Assert.Throws<DomainException>(() => a.RecordOfficialDocument("X1", new DateOnly(2026, 1, 1), null, InvoiceDocumentType.EArchive, OfficialSource.None, null, false, DateTime.UtcNow));
    }

    [Fact]
    public void Official_record_cannot_be_overwritten()
    {
        var i = IssuedInvoice();
        Assert.Throws<DomainException>(() => i.RecordOfficialDocument("OTHER", new DateOnly(2026, 1, 1), null, InvoiceDocumentType.EArchive, OfficialSource.ManualEntry, null, false, DateTime.UtcNow));
    }

    [Fact] // Test 18
    public void Draft_can_be_cancelled_internally_with_reason_and_stays_traceable()
    {
        var i = Draft();
        Assert.Throws<DomainException>(() => i.CancelUnissued(""));
        i.CancelUnissued("Müşteri vazgeçti");
        Assert.Equal(InvoiceInternalStatus.Cancelled, i.InternalStatus);
        Assert.Equal(OfficialInvoiceStatus.NotIssued, i.OfficialStatus);
        Assert.Equal("Müşteri vazgeçti", i.CancelReason);
    }

    [Fact]
    public void Issued_invoice_cannot_be_cancelled_as_draft()
    {
        var i = IssuedInvoice();
        Assert.Throws<DomainException>(() => i.CancelUnissued("silmek istiyorum"));
        Assert.Equal(OfficialInvoiceStatus.Issued, i.OfficialStatus);
    }

    [Fact] // Test 20
    public void Cancellation_request_keeps_official_status_issued()
    {
        var i = IssuedInvoice();
        i.RequestCancellation("Hatalı tutar", DateTime.UtcNow);
        Assert.Equal(InvoiceInternalStatus.CancellationRequired, i.InternalStatus);
        Assert.Equal(OfficialInvoiceStatus.Issued, i.OfficialStatus); // resmî olarak hâlâ geçerli
    }

    [Fact] // Test 20
    public void Official_cancelled_requires_verified_confirmation()
    {
        var i = IssuedInvoice();
        i.RequestCancellation("Hatalı tutar", DateTime.UtcNow);

        Assert.Throws<DomainException>(() => i.ConfirmOfficialCancellation(false, "ref-1", new DateOnly(2026, 10, 2)));
        Assert.Equal(OfficialInvoiceStatus.Issued, i.OfficialStatus);
        Assert.Equal(InvoiceInternalStatus.CancellationRequired, i.InternalStatus);

        Assert.Throws<DomainException>(() => i.ConfirmOfficialCancellation(true, "  ", new DateOnly(2026, 10, 2)));
        Assert.Equal(OfficialInvoiceStatus.Issued, i.OfficialStatus);

        i.ConfirmOfficialCancellation(true, "Portal iptal no: 123", new DateOnly(2026, 10, 2));
        Assert.Equal(OfficialInvoiceStatus.Cancelled, i.OfficialStatus);
        Assert.Equal(InvoiceInternalStatus.Cancelled, i.InternalStatus);
    }

    [Fact] // Test 20: talep açmadan doğrudan iptal edilemez
    public void Cannot_skip_cancellation_request()
    {
        var i = IssuedInvoice();
        Assert.Throws<DomainException>(() => i.ConfirmOfficialCancellation(true, "ref", new DateOnly(2026, 10, 2)));
        Assert.Equal(OfficialInvoiceStatus.Issued, i.OfficialStatus);
    }

    [Fact]
    public void Cancellation_request_can_be_withdrawn()
    {
        var i = IssuedInvoice();
        i.RequestCancellation("x", DateTime.UtcNow);
        i.WithdrawCancellationRequest();
        Assert.Equal(InvoiceInternalStatus.Completed, i.InternalStatus);
        Assert.Equal(OfficialInvoiceStatus.Issued, i.OfficialStatus);
    }

    [Fact] // Test 29 (alan düzeyi): gönderim sırasında sonuç belirsizse yeniden gönderim engellenir
    public void Unknown_submission_result_blocks_resubmission()
    {
        var i = ApprovedInvoice();
        i.BeginSubmission("Mock", Guid.NewGuid());
        i.MarkResultUnknown("zaman aşımı");
        Assert.Equal(OfficialInvoiceStatus.ResultUnknown, i.OfficialStatus);
        Assert.Throws<DomainException>(() => i.BeginSubmission("Mock", Guid.NewGuid()));
    }

    [Fact]
    public void Failed_submission_allows_retry()
    {
        var i = ApprovedInvoice();
        i.BeginSubmission("Mock", Guid.NewGuid());
        i.MarkSubmissionFailed("reddedildi");
        Assert.Equal(InvoiceInternalStatus.Error, i.InternalStatus);
        Assert.Equal(OfficialInvoiceStatus.Failed, i.OfficialStatus);
        i.BeginSubmission("Mock", Guid.NewGuid()); // hata sonrası yeniden denenebilir
        Assert.Equal(OfficialInvoiceStatus.Submitting, i.OfficialStatus);
    }

    [Fact]
    public void Approved_invoice_can_return_to_draft_only_before_official_document()
    {
        var a = ApprovedInvoice();
        a.ReturnToDraft();
        Assert.Equal(InvoiceInternalStatus.Draft, a.InternalStatus);
        Assert.Throws<DomainException>(() => IssuedInvoice().ReturnToDraft());
    }
}

public class PaymentStateTests
{
    [Fact]
    public void Payment_reverse_keeps_record_and_requires_reason()
    {
        var p = new Payment { Number = "THS-1", Amount = 100m };
        Assert.Throws<DomainException>(() => p.Reverse("", DateTime.UtcNow));
        p.Reverse("Yanlış müşteri", DateTime.UtcNow);
        Assert.Equal(PaymentStatus.Reversed, p.Status);
        Assert.Equal(100m, p.Amount);
        Assert.Throws<DomainException>(() => p.Reverse("tekrar", DateTime.UtcNow));
    }
}

public class FileValidatorTests
{
    private static MemoryStream Bytes(params byte[] b) => new(b);
    private static MemoryStream Text(string s) => new(Encoding.UTF8.GetBytes(s));
    private static MemoryStream Pdf() => Text("%PDF-1.4\n1 0 obj\n<<>>\nendobj\n%%EOF");

    [Fact] // Test 27
    public async Task Valid_pdf_and_xml_are_accepted()
    {
        var pdf = await FileValidator.ValidateAsync(Pdf(), "fatura.pdf", invoiceFile: true);
        Assert.Equal(AttachmentKind.Pdf, pdf.Kind);
        var xml = await FileValidator.ValidateAsync(Text("<?xml version=\"1.0\"?><Invoice><ID>1</ID></Invoice>"), "fatura.xml", invoiceFile: true);
        Assert.Equal(AttachmentKind.Xml, xml.Kind);
    }

    [Theory] // Test 27
    [InlineData("virus.exe")]
    [InlineData("script.js")]
    [InlineData("page.html")]
    [InlineData("noextension")]
    public async Task Disallowed_extensions_are_rejected(string name) =>
        await Assert.ThrowsAsync<ValidationFailedException>(() => FileValidator.ValidateAsync(Pdf(), name, invoiceFile: true));

    [Fact] // Test 27: uzantı PDF ama içerik değil
    public async Task Content_must_match_extension()
    {
        await Assert.ThrowsAsync<ValidationFailedException>(() => FileValidator.ValidateAsync(Text("MZ bu bir exe"), "sahte.pdf", true));
        await Assert.ThrowsAsync<ValidationFailedException>(() => FileValidator.ValidateAsync(Pdf(), "sahte.png", false));
    }

    [Fact]
    public async Task Empty_and_oversized_files_are_rejected()
    {
        await Assert.ThrowsAsync<ValidationFailedException>(() => FileValidator.ValidateAsync(new MemoryStream(), "bos.pdf", true));
        var big = new byte[FileValidator.MaxReceiptBytes + 10];
        Array.Copy(Encoding.ASCII.GetBytes("%PDF-"), big, 5);
        await Assert.ThrowsAsync<ValidationFailedException>(() => FileValidator.ValidateAsync(new MemoryStream(big), "buyuk.pdf", invoiceFile: false));
    }

    [Fact]
    public async Task Malformed_xml_and_doctype_are_rejected()
    {
        await Assert.ThrowsAsync<ValidationFailedException>(() => FileValidator.ValidateAsync(Text("<a><b></a>"), "bozuk.xml", true));
        // DTD/XXE içeren XML reddedilir
        await Assert.ThrowsAsync<ValidationFailedException>(() => FileValidator.ValidateAsync(
            Text("<?xml version=\"1.0\"?><!DOCTYPE x [<!ENTITY e SYSTEM \"file:///c:/windows/win.ini\">]><x>&e;</x>"), "xxe.xml", true));
    }

    [Fact]
    public async Task Receipt_accepts_png_and_jpg_with_real_signatures()
    {
        var png = await FileValidator.ValidateAsync(Bytes(0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A, 0, 0), "dekont.png", false);
        Assert.Equal("image/png", png.ContentType);
        var jpg = await FileValidator.ValidateAsync(Bytes(0xFF, 0xD8, 0xFF, 0xE0, 0, 0), "dekont.jpeg", false);
        Assert.Equal(".jpg", jpg.Extension);
    }
}

public class SecurityTests
{
    [Fact]
    public void Password_hash_roundtrip_and_unique_salt()
    {
        var h1 = PasswordHasher.Hash("Güçlü-Parola-123");
        var h2 = PasswordHasher.Hash("Güçlü-Parola-123");
        Assert.NotEqual(h1, h2);
        Assert.True(PasswordHasher.Verify("Güçlü-Parola-123", h1));
        Assert.False(PasswordHasher.Verify("yanlış", h1));
        Assert.DoesNotContain("Güçlü", h1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("kisa")]
    [InlineData("BURAYA_GUCLU_BIR_PAROLA_YAZIN")]
    [InlineData("password123456")]
    public void Weak_or_placeholder_admin_passwords_are_rejected(string? pw) =>
        Assert.NotNull(DbSeeder.ValidateAdminPassword(pw));

    [Fact]
    public void Strong_admin_password_is_accepted() =>
        Assert.Null(DbSeeder.ValidateAdminPassword("xK9#mQ2$vL7pRt"));
}
