using System.Diagnostics;
using Microsoft.Extensions.Options;
using SutTicaretCRM.Application;
using SutTicaretCRM.Domain;
using SutTicaretCRM.Infrastructure.Providers;

namespace SutTicaretCRM.IntegrationTests;

/// <summary>
/// CRM'in .NET adaptörü → GERÇEK HTTP → GERÇEK ağ geçidi süreci (Node) → vendored f/fatura → YEREL SAHTE PORTAL.
/// Bu bir MOCK testidir: GİB'e bağlanmaz (dış ağ çağrısı kodla engellenir); numara/ETTN gerçek GİB belgesi değildir
/// ve imzalı resmî fatura kesildiğini KANITLAMAZ. Mevcut FakeGatewayHandler testlerine ek olarak HTTP sözleşmesini doğrular.
/// </summary>
public class GatewayRealHttpStackTests
{
    private static readonly string Token = new('s', 40);

    private sealed class Stack : IDisposable
    {
        public Process Proc = null!; public int Port;
        public void Dispose() { try { if (!Proc.HasExited) Proc.Kill(true); } catch { } Proc.Dispose(); }
    }

    private static string? GatewayDir()
    {
        for (var d = new DirectoryInfo(AppContext.BaseDirectory); d != null; d = d.Parent)
            if (File.Exists(Path.Combine(d.FullName, "invoice-gateway", "test", "stack-server.mjs"))) return Path.Combine(d.FullName, "invoice-gateway");
        return null;
    }

    private static Stack? Start(string mode)
    {
        var dir = GatewayDir(); if (dir is null) return null;
        var psi = new ProcessStartInfo("node", "test/stack-server.mjs") { WorkingDirectory = dir, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false };
        psi.Environment["GATEWAY_TOKEN"] = Token; psi.Environment["FAKE_PORTAL_MODE"] = mode;
        Process p;
        try { p = Process.Start(psi)!; } catch { return null; } // node yoksa test atlanır
        var line = p.StandardOutput.ReadLineAsync().WaitAsync(TimeSpan.FromSeconds(20)).GetAwaiter().GetResult();
        if (line is null || !line.StartsWith("PORT=")) { try { p.Kill(true); } catch { } throw new InvalidOperationException("Sahte yığın başlamadı: " + p.StandardError.ReadToEnd()); }
        return new Stack { Proc = p, Port = int.Parse(line[5..]) };
    }

    private static EArsivGatewayInvoiceProvider Provider(int port, string? token = null) =>
        new(new HttpClient(), Options.Create(new InvoiceGatewayOptions { Url = $"http://127.0.0.1:{port}", Token = token ?? Token, Environment = "TEST", TimeoutSeconds = 15 }));

    private static InvoiceDocumentRequest Req(Guid key) => new(Guid.NewGuid(), "TSL-2026-000001", key, InvoiceDocumentType.EArchive, new DateOnly(2026, 10, 12),
        new InvoiceBuyer("Örnek Ltd.", "Çankaya", "1234567890", null, "Adres", "Ankara", "Çankaya", null),
        [new InvoiceDocumentLine("Peynir", 10, "Kg", 200m, 100m, 10m, 190m, 1900m)], 1900m, 190m, 2090m, null);

    [Fact]
    public async Task Draft_challenge_issue_and_status_work_over_real_http_against_the_mock_portal()
    {
        using var s = Start("ok"); if (s is null) return; // node/gateway yok: atlandı
        var p = Provider(s.Port); var key = Guid.NewGuid();

        var draft = await p.CreateDraftAsync(Req(key));
        Assert.True(draft.IsSuccess, draft.Message);
        var ettn = draft.Value!.ProviderDraftId; Assert.False(string.IsNullOrWhiteSpace(ettn));

        // Aynı SubmissionKey ikinci kez yeni taslak YARATMAZ: mevcut taslak (aynı ETTN) döner
        var again = await p.CreateDraftAsync(Req(key));
        Assert.True(again.IsSuccess, again.Message);
        Assert.Equal(ettn, again.Value!.ProviderDraftId);

        var refr = new ProviderReference(ettn, null, key);
        var st = await p.GetInvoiceStatusAsync(refr);
        Assert.True(st.IsSuccess, st.Message); Assert.Equal(ProviderDocumentState.Draft, st.Value!.State);

        var ch = await p.RequestIssueChallengeAsync(refr);
        Assert.True(ch.IsSuccess, ch.Message); Assert.Contains("*", ch.Value!.MaskedPhone);

        var bad = await p.IssueAsync(refr, new ProviderIssueInput("000000"));
        Assert.False(bad.IsSuccess);
        Assert.NotEqual(ProviderDocumentState.Issued, (await p.GetInvoiceStatusAsync(refr)).Value!.State); // yanlış kodla imzalı görünmez

        var ok = await p.IssueAsync(refr, new ProviderIssueInput("123456"));
        Assert.True(ok.IsSuccess, ok.Message);
        Assert.Equal("MOCK2026000000001", ok.Value!.OfficialNumber);   // MOCK numara: gerçek GİB belgesi değil
        Assert.Equal(ettn, ok.Value.OfficialUuid);
        Assert.Equal(ProviderDocumentState.Issued, (await p.GetInvoiceStatusAsync(refr)).Value!.State);
    }

    [Fact]
    public async Task Wrong_gateway_token_is_rejected_and_unknown_key_is_not_found()
    {
        using var s = Start("ok"); if (s is null) return;
        Assert.Equal(ProviderOutcome.Rejected, (await Provider(s.Port, new string('x', 40)).CreateDraftAsync(Req(Guid.NewGuid()))).Outcome);
        Assert.Equal(ProviderOutcome.NotFound, (await Provider(s.Port).GetInvoiceStatusAsync(new ProviderReference(null, null, Guid.NewGuid()))).Outcome);
    }

    [Fact]
    public async Task Portal_outage_503_html_gives_temporary_failure_and_blocks_blind_resubmission()
    {
        using var s = Start("down"); if (s is null) return;
        var p = Provider(s.Port); var key = Guid.NewGuid();
        var r1 = await p.CreateDraftAsync(Req(key));
        Assert.Equal(ProviderOutcome.TemporaryFailure, r1.Outcome);
        Assert.Equal(ProviderOutcome.TemporaryFailure, (await p.CreateDraftAsync(Req(key))).Outcome); // belirsiz sonuç: tekrar gönderim engelli (409)
    }

    [Fact]
    public async Task Gateway_not_running_gives_temporary_failure_not_an_exception()
    {
        var r = await Provider(1).CreateDraftAsync(Req(Guid.NewGuid()));
        Assert.Equal(ProviderOutcome.TemporaryFailure, r.Outcome);
    }
}
