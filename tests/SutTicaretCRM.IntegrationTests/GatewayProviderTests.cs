using System.Net;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using SutTicaretCRM.Application;
using SutTicaretCRM.Application.Services;
using SutTicaretCRM.Domain;
using SutTicaretCRM.Infrastructure;
using SutTicaretCRM.Infrastructure.Providers;

namespace SutTicaretCRM.IntegrationTests;

/// <summary>Sahte ağ geçidi: gerçek ağ/GİB çağrısı YAPMAZ. İstekleri kaydeder, hazır yanıtlar döner.</summary>
public sealed class FakeGatewayHandler : HttpMessageHandler
{
    public List<(HttpMethod Method, string Path, string? Body, string? Auth)> Requests { get; } = [];
    public Func<HttpRequestMessage, (HttpStatusCode, string)> Respond { get; set; } = _ => (HttpStatusCode.OK, "{}");
    public Exception? Throw { get; set; }

    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
    {
        Requests.Add((request.Method, request.RequestUri!.AbsolutePath, request.Content is null ? null : await request.Content.ReadAsStringAsync(ct), request.Headers.Authorization?.ToString()));
        if (Throw is not null) throw Throw;
        var (status, body) = Respond(request);
        return new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}

public class GatewayProviderAdapterTests
{
    private static (EArsivGatewayInvoiceProvider P, FakeGatewayHandler H) Make(string env = "TEST")
    {
        var h = new FakeGatewayHandler();
        var opts = Options.Create(new InvoiceGatewayOptions { Url = "http://127.0.0.1:5090", Token = new string('t', 40), Environment = env, TimeoutSeconds = 5 });
        return (new EArsivGatewayInvoiceProvider(new HttpClient(h), opts), h);
    }

    private static InvoiceDocumentRequest Req() => new(Guid.NewGuid(), "TSL-2026-000001", Guid.NewGuid(), InvoiceDocumentType.EArchive, new DateOnly(2026, 10, 12),
        new InvoiceBuyer("Örnek Ltd.", "Çankaya", "1234567890", null, "Adres", "Ankara", "Çankaya", null),
        [new InvoiceDocumentLine("Peynir", 10, "Kg", 200m, 100m, 10m, 190m, 1900m)], 1900m, 190m, 2090m, null);

    [Fact]
    public void Capabilities_are_honest_about_what_the_gateway_can_do()
    {
        var (test, _) = Make();
        var c = test.Capabilities;
        Assert.True(c.CanCreateDraft); Assert.True(c.CanIssue); Assert.True(c.CanQueryStatus); Assert.True(c.RequiresSmsCode);
        Assert.True(c.CanCancel);             // yalnızca iptal TALEBİ oluşturabilir
        Assert.False(c.CanReportCancellation); // iptalin sonuçlandığını doğrulayamaz
        Assert.False(c.SupportsEInvoice);     // yalnızca e-Arşiv
        Assert.True(c.SupportsEArchive);
        Assert.False(c.IsLiveCapable);        // TEST ortamı
        Assert.Contains("TEST", test.DisplayName);
        var (prod, _) = Make("PROD");
        Assert.True(prod.Capabilities.IsLiveCapable);
        Assert.Contains("CANLI", prod.DisplayName);
    }

    [Fact]
    public async Task Validate_and_pdf_are_not_supported_and_make_no_network_call()
    {
        var (p, h) = Make();
        Assert.Equal(ProviderOutcome.NotSupported, (await p.ValidateInvoiceAsync(Req())).Outcome);
        Assert.Equal(ProviderOutcome.NotSupported, (await p.GetInvoiceDocumentAsync(new ProviderReference(null, null, Guid.NewGuid()), ProviderDocumentFormat.Pdf)).Outcome);
        Assert.Empty(h.Requests);
    }

    [Fact]
    public async Task Cancellation_creates_a_request_only_and_never_reports_completion()
    {
        var (p, h) = Make();
        var key = Guid.NewGuid();
        h.Respond = _ => (HttpStatusCode.OK, """{"outcome":"success","message":"İptal talebiniz başarıyla oluşturulmuştur."}""");
        var r = await p.RequestCancellationAsync(new ProviderReference("d", null, key), "Hatalı tutar");
        Assert.True(r.IsSuccess);
        Assert.False(r.Value!.Completed);                 // talep ≠ iptal
        var (_, path, body, _) = Assert.Single(h.Requests);
        Assert.Equal("/v1/cancel", path);
        Assert.Equal("Hatalı tutar", JsonDocument.Parse(body!).RootElement.GetProperty("reason").GetString());
        h.Respond = _ => (HttpStatusCode.BadGateway, """{"outcome":"temporary","message":"belirsiz"}""");
        Assert.Equal(ProviderOutcome.TemporaryFailure, (await p.RequestCancellationAsync(new ProviderReference("d", null, key), "x")).Outcome);
        h.Throw = new HttpRequestException("kopuk");
        Assert.Equal(ProviderOutcome.TemporaryFailure, (await p.RequestCancellationAsync(new ProviderReference("d", null, key), "x")).Outcome);
    }

    [Fact]
    public async Task Create_draft_sends_bearer_token_and_camelCase_json_and_maps_success()
    {
        var (p, h) = Make();
        h.Respond = _ => (HttpStatusCode.OK, """{"outcome":"success","draftId":"abc","uuid":"abc","message":"Portalda taslak oluşturuldu"}""");
        var r = await p.CreateDraftAsync(Req());
        Assert.True(r.IsSuccess);
        Assert.Equal("abc", r.Value!.ProviderDraftId);
        var (m, path, body, auth) = Assert.Single(h.Requests);
        Assert.Equal(HttpMethod.Post, m); Assert.Equal("/v1/drafts", path);
        Assert.Equal("Bearer " + new string('t', 40), auth);
        using var doc = JsonDocument.Parse(body!);
        Assert.Equal("EArchive", doc.RootElement.GetProperty("documentType").GetString());      // enum metin
        Assert.Equal("2026-10-12", doc.RootElement.GetProperty("issueDate").GetString());
        Assert.Equal(2090m, doc.RootElement.GetProperty("grandTotal").GetDecimal());
        Assert.Equal("Örnek Ltd.", doc.RootElement.GetProperty("buyer").GetProperty("name").GetString());
        Assert.Equal(1, doc.RootElement.GetProperty("lines").GetArrayLength());
    }

    [Theory]
    [InlineData(422, ProviderOutcome.Rejected)]
    [InlineData(401, ProviderOutcome.Rejected)]
    [InlineData(404, ProviderOutcome.NotFound)]
    [InlineData(409, ProviderOutcome.TemporaryFailure)]
    [InlineData(502, ProviderOutcome.TemporaryFailure)]
    [InlineData(500, ProviderOutcome.TemporaryFailure)]
    [InlineData(503, ProviderOutcome.TemporaryFailure)]
    public async Task Http_statuses_map_to_outcomes_and_uncertain_results_are_never_treated_as_failure(int status, ProviderOutcome expected)
    {
        var (p, h) = Make();
        h.Respond = _ => ((HttpStatusCode)status, """{"outcome":"x","message":"ileti"}""");
        var r = await p.CreateDraftAsync(Req());
        Assert.Equal(expected, r.Outcome);
        Assert.False(r.IsSuccess);
    }

    [Fact]
    public async Task Network_failures_timeouts_and_garbage_are_temporary_not_rejected()
    {
        var (p, h) = Make();
        h.Throw = new HttpRequestException("bağlantı reddedildi");
        Assert.Equal(ProviderOutcome.TemporaryFailure, (await p.CreateDraftAsync(Req())).Outcome);
        h.Throw = new TaskCanceledException("zaman aşımı");
        Assert.Equal(ProviderOutcome.TemporaryFailure, (await p.IssueAsync(new ProviderReference("d", null, Guid.NewGuid()), new ProviderIssueInput("123456"))).Outcome);
        h.Throw = null;
        h.Respond = _ => (HttpStatusCode.OK, "<html>proxy hatası</html>");
        Assert.Equal(ProviderOutcome.TemporaryFailure, (await p.CreateDraftAsync(Req())).Outcome);
        h.Respond = _ => (HttpStatusCode.OK, """{"outcome":"success"}""");     // başarı ama gerekli alan yok
        var ok = await p.GetInvoiceStatusAsync(new ProviderReference(null, null, Guid.NewGuid()));
        Assert.True(ok.IsSuccess); Assert.Equal(ProviderDocumentState.Unknown, ok.Value!.State);
    }

    [Fact]
    public async Task Status_issue_and_challenge_calls_map_gateway_payloads()
    {
        var (p, h) = Make();
        var key = Guid.NewGuid();
        h.Respond = r => r.RequestUri!.AbsolutePath switch
        {
            "/v1/issue/challenge" => (HttpStatusCode.OK, """{"outcome":"success","maskedPhone":"05*******67","message":"SMS gönderildi"}"""),
            "/v1/issue" => (HttpStatusCode.OK, """{"outcome":"success","officialNumber":"ABC2026000000001","uuid":"u-1","issueDate":"2026-10-12"}"""),
            _ => (HttpStatusCode.OK, """{"outcome":"success","state":"Issued","officialNumber":"ABC2026000000001","uuid":"u-1","issueDate":"2026-10-12"}"""),
        };
        var refx = new ProviderReference("d", null, key);
        var ch = await p.RequestIssueChallengeAsync(refx);
        Assert.Equal("05*******67", ch.Value!.MaskedPhone);
        var iss = await p.IssueAsync(refx, new ProviderIssueInput("123456"));
        Assert.Equal("ABC2026000000001", iss.Value!.OfficialNumber);
        Assert.Equal(new DateOnly(2026, 10, 12), iss.Value.IssueDate);
        Assert.Equal(InvoiceDocumentType.EArchive, iss.Value.DocumentType);
        var st = await p.GetInvoiceStatusAsync(refx);
        Assert.Equal(ProviderDocumentState.Issued, st.Value!.State);
        Assert.Contains(h.Requests, x => x.Path == $"/v1/status/{key}");
        Assert.Contains("123456", h.Requests.First(x => x.Path == "/v1/issue").Body);
    }

    [Fact]
    public void Gateway_registration_refuses_remote_hosts_and_weak_tokens()
    {
        IServiceCollection Build(Dictionary<string, string?> extra)
        {
            var cfg = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?> { ["ConnectionStrings:Default"] = "Host=x" }.Concat(extra).ToDictionary()).Build();
            return new ServiceCollection().AddInfrastructure(cfg);
        }
        var good = new Dictionary<string, string?> { ["Invoice:Gateway:Url"] = "http://127.0.0.1:5090", ["Invoice:Gateway:Token"] = new string('a', 40) };
        Build(good); // sorunsuz
        Assert.Throws<InvalidOperationException>(() => Build(new() { ["Invoice:Gateway:Url"] = "http://fatura.example.com:5090", ["Invoice:Gateway:Token"] = new string('a', 40) }));
        Build(new() { ["Invoice:Gateway:Url"] = "http://fatura.example.com:5090", ["Invoice:Gateway:Token"] = new string('a', 40), ["Invoice:Gateway:AllowRemote"] = "true" });
        Assert.Throws<InvalidOperationException>(() => Build(new() { ["Invoice:Gateway:Url"] = "http://127.0.0.1:5090", ["Invoice:Gateway:Token"] = "kisa" }));
        Assert.Throws<InvalidOperationException>(() => Build(new() { ["Invoice:Gateway:Url"] = "http://127.0.0.1:5090", ["Invoice:Gateway:Token"] = "BURAYA_RASTGELE_BIR_ANAHTAR_YAZIN_32_KARAKTER" }));
        Assert.Throws<InvalidOperationException>(() => Build(new() { ["Invoice:Gateway:Url"] = "ftp://127.0.0.1", ["Invoice:Gateway:Token"] = new string('a', 40) }));
        // Yapılandırılmamışsa sağlayıcı hiç kaydedilmez
        var none = Build([]);
        Assert.DoesNotContain(none, d => d.ServiceType == typeof(EArsivGatewayInvoiceProvider));
    }
}

/// <summary>Servis akışı, e-Arşiv ağ geçidi sağlayıcısıyla (sahte HTTP): SMS adımı, onaylar ve iptal doğrulaması.</summary>
[Collection("pg")]
public class GatewayProviderFlowTests(PostgresFixture pg) : IntegrationTestBase(pg)
{
    private static string Dummy => "{}";

    private async Task<(InvoiceDto Inv, FakeGatewayHandler H)> Prepare()
    {
        var h = App.GatewayHandler;
        await App.SetProvider("EArsivGateway");
        var c = await App.NewCustomer();
        var p = await App.NewProduct(sale: 100m, stock: 100);
        var sale = await App.NewSale(c.Id, p, 5);
        return (await App.NewApprovedInvoice(sale), h);
    }

    private static ProviderActionRequest Ack(InvoiceDto i, string? sms = null) => new(true, i.GrandTotal, i.BuyerName, sms);

    [Fact]
    public async Task Full_flow_draft_then_sms_challenge_then_issue_records_provider_document_with_correct_state()
    {
        var (inv, h) = await Prepare();
        h.Respond = r => r.RequestUri!.AbsolutePath switch
        {
            "/v1/drafts" => (HttpStatusCode.OK, """{"outcome":"success","draftId":"ettn-1","uuid":"ettn-1","message":"taslak"}"""),
            "/v1/issue/challenge" => (HttpStatusCode.OK, """{"outcome":"success","maskedPhone":"05*******67"}"""),
            "/v1/issue" => (HttpStatusCode.OK, """{"outcome":"success","officialNumber":"GIB2026000000099","uuid":"ettn-1","issueDate":"2026-10-15"}"""),
            _ => (HttpStatusCode.NotFound, "{}"),
        };

        var sub = await App.Use<InvoiceService, InvoiceDto>(s => s.SubmitToProviderAsync(inv.Id, Ack(inv)));
        Assert.Equal(OfficialInvoiceStatus.Submitting, sub.OfficialStatus);
        Assert.Null(sub.OfficialNumber);

        // SMS istemeden/onaysız imzalama olmaz
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<InvoiceService, InvoiceDto>(s => s.IssueViaProviderAsync(inv.Id, Ack(inv, null))));
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<InvoiceService, ProviderChallenge>(s => s.RequestIssueChallengeAsync(inv.Id, new ProviderActionRequest(false, inv.GrandTotal, inv.BuyerName))));
        Assert.DoesNotContain(h.Requests, r => r.Path == "/v1/issue");

        var ch = await App.Use<InvoiceService, ProviderChallenge>(s => s.RequestIssueChallengeAsync(inv.Id, Ack(inv)));
        Assert.Equal("05*******67", ch.MaskedPhone);
        var done = await App.Use<InvoiceService, InvoiceDto>(s => s.IssueViaProviderAsync(inv.Id, Ack(inv, "123456")));

        Assert.Equal(OfficialInvoiceStatus.Issued, done.OfficialStatus);
        Assert.Equal("GIB2026000000099", done.OfficialNumber);
        Assert.Equal(OfficialSource.Provider, done.OfficialSource);
        Assert.Equal("EArsivGateway", done.OfficialProvider);
        Assert.True(done.OfficialVerified);
        Assert.False(done.ProviderIsLegallyValid);   // TEST ortamı: yasal geçerli değil
        // Aynı SubmissionKey tüm adımlarda kullanıldı
        var keys = h.Requests.Where(r => r.Body != null).Select(r => JsonDocument.Parse(r.Body!).RootElement)
            .Select(e => e.TryGetProperty("submissionKey", out var k) ? k.GetString() : null).Where(k => k != null).Distinct().ToList();
        Assert.Single(keys);
    }

    [Fact]
    public async Task Gateway_timeout_on_issue_leaves_result_unknown_and_status_query_resolves_it()
    {
        var (inv, h) = await Prepare();
        h.Respond = r => r.RequestUri!.AbsolutePath switch
        {
            "/v1/drafts" => (HttpStatusCode.OK, """{"outcome":"success","draftId":"ettn-1","uuid":"ettn-1"}"""),
            "/v1/issue/challenge" => (HttpStatusCode.OK, """{"outcome":"success","maskedPhone":"05*"}"""),
            _ => (HttpStatusCode.NotFound, "{}"),
        };
        await App.Use<InvoiceService, InvoiceDto>(s => s.SubmitToProviderAsync(inv.Id, Ack(inv)));
        await App.Use<InvoiceService, ProviderChallenge>(s => s.RequestIssueChallengeAsync(inv.Id, Ack(inv)));

        h.Respond = r => r.RequestUri!.AbsolutePath == "/v1/issue" ? (HttpStatusCode.BadGateway, """{"outcome":"temporary","message":"sonuç belirsiz"}""") : (HttpStatusCode.NotFound, "{}");
        await Assert.ThrowsAsync<ConflictException>(() => App.Use<InvoiceService, InvoiceDto>(s => s.IssueViaProviderAsync(inv.Id, Ack(inv, "123456"))));
        var unknown = await App.Use<InvoiceService, InvoiceDto>(s => s.GetAsync(inv.Id));
        Assert.Equal(OfficialInvoiceStatus.ResultUnknown, unknown.OfficialStatus);
        Assert.Null(unknown.OfficialNumber);

        h.Respond = r => r.RequestUri!.AbsolutePath.StartsWith("/v1/status/")
            ? (HttpStatusCode.OK, """{"outcome":"success","state":"Issued","officialNumber":"GIB2026000000100","uuid":"ettn-1","issueDate":"2026-10-15"}""") : (HttpStatusCode.NotFound, "{}");
        var resolved = await App.Use<InvoiceService, InvoiceDto>(s => s.RefreshStatusAsync(inv.Id));
        Assert.Equal(OfficialInvoiceStatus.Issued, resolved.OfficialStatus);
        Assert.Equal("GIB2026000000100", resolved.OfficialNumber);
        Assert.Equal(1, h.Requests.Count(r => r.Path == "/v1/drafts"));                 // yalnızca bir kez taslak istendi
    }

    [Fact]
    public async Task Gateway_issued_invoice_cancellation_is_user_declared_because_gateway_cannot_cancel()
    {
        var (inv, h) = await Prepare();
        h.Respond = r => r.RequestUri!.AbsolutePath switch
        {
            "/v1/drafts" => (HttpStatusCode.OK, """{"outcome":"success","draftId":"e","uuid":"e"}"""),
            "/v1/issue/challenge" => (HttpStatusCode.OK, """{"outcome":"success","maskedPhone":"05*"}"""),
            "/v1/issue" => (HttpStatusCode.OK, """{"outcome":"success","officialNumber":"GIB2026000000101","uuid":"e","issueDate":"2026-10-15"}"""),
            _ => (HttpStatusCode.NotFound, "{}"),
        };
        await App.Use<InvoiceService, InvoiceDto>(s => s.SubmitToProviderAsync(inv.Id, Ack(inv)));
        await App.Use<InvoiceService, ProviderChallenge>(s => s.RequestIssueChallengeAsync(inv.Id, Ack(inv)));
        await App.Use<InvoiceService, InvoiceDto>(s => s.IssueViaProviderAsync(inv.Id, Ack(inv, "123456")));
        await App.Use<InvoiceService, InvoiceDto>(s => s.RequestCancellationAsync(inv.Id, "Hatalı"));

        // Beyan olmadan kabul edilmez
        await Assert.ThrowsAsync<DomainException>(() => App.Use<InvoiceService, InvoiceDto>(s => s.ConfirmOfficialCancellationAsync(inv.Id, new ConfirmCancellationRequest(false, "ref", App.Clock.Today, false))));
        var done = await App.Use<InvoiceService, InvoiceDto>(s => s.ConfirmOfficialCancellationAsync(inv.Id, new ConfirmCancellationRequest(true, "Portal iptal no 5", App.Clock.Today, false)));
        Assert.Equal(OfficialInvoiceStatus.Cancelled, done.OfficialStatus);
        Assert.DoesNotContain(h.Requests, r => r.Path.Contains("cancel", StringComparison.OrdinalIgnoreCase));   // beyanla kaydedilirse ağ geçidine iptal çağrısı gitmez
    }

    [Fact]
    public async Task Cancel_request_is_forwarded_to_portal_but_invoice_stays_officially_valid_until_user_confirms()
    {
        var (inv, h) = await Prepare();
        h.Respond = r => r.RequestUri!.AbsolutePath switch
        {
            "/v1/drafts" => (HttpStatusCode.OK, """{"outcome":"success","draftId":"e","uuid":"e"}"""),
            "/v1/issue/challenge" => (HttpStatusCode.OK, """{"outcome":"success","maskedPhone":"05*"}"""),
            "/v1/issue" => (HttpStatusCode.OK, """{"outcome":"success","officialNumber":"GIB2026000000102","uuid":"e","issueDate":"2026-10-15"}"""),
            "/v1/cancel" => (HttpStatusCode.OK, """{"outcome":"success","message":"İptal talebiniz başarıyla oluşturulmuştur."}"""),
            _ => (HttpStatusCode.NotFound, "{}"),
        };
        await App.Use<InvoiceService, InvoiceDto>(s => s.SubmitToProviderAsync(inv.Id, Ack(inv)));
        await App.Use<InvoiceService, ProviderChallenge>(s => s.RequestIssueChallengeAsync(inv.Id, Ack(inv)));
        await App.Use<InvoiceService, InvoiceDto>(s => s.IssueViaProviderAsync(inv.Id, Ack(inv, "123456")));

        // İptal ihtiyacı işaretlenmeden portala iptal talebi gitmez
        await Assert.ThrowsAsync<ConflictException>(() => App.Use<InvoiceService, InvoiceDto>(s => s.RequestProviderCancellationAsync(inv.Id, Ack(inv))));
        await App.Use<InvoiceService, InvoiceDto>(s => s.RequestCancellationAsync(inv.Id, "Hatalı tutar"));
        await Assert.ThrowsAsync<ValidationFailedException>(() => App.Use<InvoiceService, InvoiceDto>(s => s.RequestProviderCancellationAsync(inv.Id, new ProviderActionRequest(false, inv.GrandTotal, inv.BuyerName))));
        Assert.DoesNotContain(h.Requests, r => r.Path == "/v1/cancel");

        var after = await App.Use<InvoiceService, InvoiceDto>(s => s.RequestProviderCancellationAsync(inv.Id, Ack(inv)));
        Assert.Contains(h.Requests, r => r.Path == "/v1/cancel");
        Assert.Equal(OfficialInvoiceStatus.Issued, after.OfficialStatus);               // talep ≠ iptal: belge hâlâ geçerli
        Assert.Equal(InvoiceInternalStatus.CancellationRequired, after.InternalStatus);
        Assert.Contains("TALEBİ", after.ProviderLastMessage);

        // Portal belirsiz yanıt verirse hata gösterilir, durum değişmez
        h.Respond = r => r.RequestUri!.AbsolutePath == "/v1/cancel" ? (HttpStatusCode.BadGateway, """{"outcome":"temporary","message":"belirsiz"}""") : (HttpStatusCode.NotFound, "{}");
        await Assert.ThrowsAsync<ConflictException>(() => App.Use<InvoiceService, InvoiceDto>(s => s.RequestProviderCancellationAsync(inv.Id, Ack(inv))));
        Assert.Equal(OfficialInvoiceStatus.Issued, (await App.Use<InvoiceService, InvoiceDto>(s => s.GetAsync(inv.Id))).OfficialStatus);

        // Kullanıcı portalda sonuçlandığını beyan edince kaydedilir (GİB'den doğrulanmadı etiketiyle)
        var done = await App.Use<InvoiceService, InvoiceDto>(s => s.ConfirmOfficialCancellationAsync(inv.Id, new ConfirmCancellationRequest(true, "Portal iptal talebi onaylandı", App.Clock.Today, false)));
        Assert.Equal(OfficialInvoiceStatus.Cancelled, done.OfficialStatus);
        Assert.False(done.OfficialCancelVerified);      // iptal kullanıcı beyanıdır
    }

    [Fact]
    public async Task Gateway_rejection_marks_failed_and_allows_retry_with_new_key()
    {
        var (inv, h) = await Prepare();
        h.Respond = _ => (HttpStatusCode.UnprocessableEntity, """{"outcome":"rejected","message":"Alıcı VKN geçersiz"}""");
        var failed = await App.Use<InvoiceService, InvoiceDto>(s => s.SubmitToProviderAsync(inv.Id, Ack(inv)));
        Assert.Equal(OfficialInvoiceStatus.Failed, failed.OfficialStatus);
        Assert.Contains("VKN", failed.ProviderLastMessage);
        h.Respond = _ => (HttpStatusCode.OK, """{"outcome":"success","draftId":"e2","uuid":"e2"}""");
        var retry = await App.Use<InvoiceService, InvoiceDto>(s => s.SubmitToProviderAsync(inv.Id, Ack(inv)));
        Assert.Equal(OfficialInvoiceStatus.Submitting, retry.OfficialStatus);
        var keys = h.Requests.Select(r => JsonDocument.Parse(r.Body!).RootElement.GetProperty("submissionKey").GetString()).Distinct().Count();
        Assert.Equal(2, keys);   // hatadan sonra yeni anahtar (ağ geçidi eski anahtarın durumunu temizlemiştir)
    }
}
