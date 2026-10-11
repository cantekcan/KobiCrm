using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.Routing;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace SutTicaretCRM.IntegrationTests;

/// <summary>Gerçek HTTP hattı (kimlik doğrulama, hata eşleme, dosya yükleme) + gerçek PostgreSQL.</summary>
public sealed class ApiFactory : WebApplicationFactory<Program>
{
    public const string AdminPassword = "Api-Test-Admin-9876";
    private readonly Dictionary<string, string?> _previous = [];
    private readonly string _environment;

    public ApiFactory(string connectionString, string environment = "Development", string? adminPassword = AdminPassword, string jwtKey = "test-jwt-key-test-jwt-key-test-jwt-key-0123456789")
    {
        _environment = environment;
        Set("ConnectionStrings__Default", connectionString);
        Set("Jwt__Key", jwtKey);
        Set("Seed__AdminPassword", adminPassword);
        Set("Seed__AdminUserName", "admin");
        Set("Storage__Root", Path.Combine(Path.GetTempPath(), "suttcrm-api-" + Guid.NewGuid().ToString("N")));
        Set("Invoice__EnableMockProvider", "true");
    }

    private void Set(string key, string? value)
    {
        _previous[key] = Environment.GetEnvironmentVariable(key);
        Environment.SetEnvironmentVariable(key, value);
    }

    protected override void ConfigureWebHost(Microsoft.AspNetCore.Hosting.IWebHostBuilder builder) => builder.UseEnvironment(_environment);

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        foreach (var (k, v) in _previous) Environment.SetEnvironmentVariable(k, v);
    }

    public async Task<HttpClient> LoggedInClientAsync()
    {
        var client = CreateClient();
        var res = await client.PostAsJsonAsync("/api/auth/login", new { userName = "admin", password = AdminPassword });
        res.EnsureSuccessStatusCode();
        var token = (await res.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("token").GetString();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return client;
    }
}

[Collection("pg")]
public class ApiSecurityTests(PostgresFixture pg)
{
    private async Task<ApiFactory> NewFactory(string env = "Development") => new(await pg.CreateDatabaseAsync(), env);

    [Fact] // Test 26
    public async Task Every_endpoint_except_login_and_health_requires_authentication()
    {
        using var f = await NewFactory();
        var endpoints = f.Services.GetRequiredService<EndpointDataSource>().Endpoints.OfType<RouteEndpoint>().ToList();
        var actions = endpoints.Where(e => e.Metadata.GetMetadata<ControllerActionDescriptor>() is not null).ToList();
        Assert.True(actions.Count > 60, $"Beklenenden az endpoint bulundu: {actions.Count}");

        var anonymous = actions.Where(e => e.Metadata.GetMetadata<IAllowAnonymous>() is not null)
            .Select(e => e.RoutePattern.RawText).ToList();
        Assert.Equal(["api/auth/login"], anonymous);
    }

    [Theory] // Test 26
    [InlineData("GET", "/api/customers")]
    [InlineData("GET", "/api/products")]
    [InlineData("GET", "/api/sales")]
    [InlineData("GET", "/api/invoices")]
    [InlineData("GET", "/api/payments")]
    [InlineData("GET", "/api/dashboard")]
    [InlineData("GET", "/api/reports/measures")]
    [InlineData("GET", "/api/settings")]
    [InlineData("GET", "/api/settings/audit-logs")]
    [InlineData("GET", "/api/stock/levels")]
    [InlineData("GET", "/api/suppliers")]
    [InlineData("POST", "/api/sales")]
    [InlineData("POST", "/api/payments")]
    [InlineData("POST", "/api/customers")]
    [InlineData("GET", "/api/auth/me")]
    public async Task Unauthenticated_requests_get_401_and_no_data(string method, string url)
    {
        using var f = await NewFactory();
        var client = f.CreateClient();
        var res = await client.SendAsync(new HttpRequestMessage(new HttpMethod(method), url) { Content = method == "POST" ? JsonContent.Create(new { }) : null });
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        var body = await res.Content.ReadAsStringAsync();
        Assert.DoesNotContain("customerId", body);
    }

    [Fact] // Test 26
    public async Task Garbage_wrong_key_and_tampered_tokens_are_rejected()
    {
        using var f = await NewFactory();
        var good = await f.LoggedInClientAsync();
        var token = good.DefaultRequestHeaders.Authorization!.Parameter!;

        foreach (var bad in new[] { "abc", "a.b.c", token[..^3] + "xyz", token.Replace('.', '-') })
        {
            var c = f.CreateClient();
            c.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", bad);
            Assert.Equal(HttpStatusCode.Unauthorized, (await c.GetAsync("/api/customers")).StatusCode);
        }

        // Başka anahtarla imzalanmış token
        using var other = new ApiFactory(await pg.CreateDatabaseAsync(), jwtKey: "baska-bir-anahtar-baska-bir-anahtar-baska-anahtar-123");
        var foreign = await other.LoggedInClientAsync();
        var c2 = f.CreateClient();
        c2.DefaultRequestHeaders.Authorization = foreign.DefaultRequestHeaders.Authorization;
        Assert.Equal(HttpStatusCode.Unauthorized, (await c2.GetAsync("/api/customers")).StatusCode);

        Assert.Equal(HttpStatusCode.OK, (await good.GetAsync("/api/customers")).StatusCode);
    }

    [Fact]
    public async Task Login_failure_is_generic_and_account_locks_after_repeated_failures()
    {
        using var f = await NewFactory();
        var client = f.CreateClient();
        var wrongUser = await client.PostAsJsonAsync("/api/auth/login", new { userName = "yok", password = "x" });
        var wrongPass = await client.PostAsJsonAsync("/api/auth/login", new { userName = "admin", password = "yanlis" });
        Assert.Equal(HttpStatusCode.Unauthorized, wrongUser.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongPass.StatusCode);
        Assert.Equal(await wrongUser.Content.ReadAsStringAsync(), await wrongPass.Content.ReadAsStringAsync());   // kullanıcı varlığı sızmaz

        for (var i = 0; i < 4; i++) await client.PostAsJsonAsync("/api/auth/login", new { userName = "admin", password = "yanlis" });
        var locked = await client.PostAsJsonAsync("/api/auth/login", new { userName = "admin", password = ApiFactory.AdminPassword });
        Assert.Equal((HttpStatusCode)423, locked.StatusCode);   // doğru parola bile kilit süresince reddedilir
    }

    [Fact]
    public async Task Login_returns_token_but_never_the_password_hash()
    {
        using var f = await NewFactory();
        var res = await f.CreateClient().PostAsJsonAsync("/api/auth/login", new { userName = "admin", password = ApiFactory.AdminPassword });
        var text = await res.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.DoesNotContain("passwordHash", text, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("v1.", text);
    }

    [Fact]
    public async Task Password_change_requires_current_password_and_strong_new_password()
    {
        using var f = await NewFactory();
        var c = await f.LoggedInClientAsync();
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = "yanlış", newPassword = "Yeni-Guclu-Parola-1" })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = ApiFactory.AdminPassword, newPassword = "kisa" })).StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, (await c.PostAsJsonAsync("/api/auth/change-password", new { currentPassword = ApiFactory.AdminPassword, newPassword = "Yeni-Guclu-Parola-1" })).StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, (await f.CreateClient().PostAsJsonAsync("/api/auth/login", new { userName = "admin", password = ApiFactory.AdminPassword })).StatusCode);
        Assert.Equal(HttpStatusCode.OK, (await f.CreateClient().PostAsJsonAsync("/api/auth/login", new { userName = "admin", password = "Yeni-Guclu-Parola-1" })).StatusCode);
    }

    [Fact]
    public async Task Application_refuses_to_start_without_a_real_admin_password_or_jwt_key()
    {
        var db = await pg.CreateDatabaseAsync();
        foreach (var pw in new string?[] { null, "", "kisa", "BURAYA_GUCLU_BIR_PAROLA_YAZIN", "password123456" })
        {
            using var f = new ApiFactory(db, adminPassword: pw);
            Assert.ThrowsAny<Exception>(() => f.CreateClient());
        }
        using var weakJwt = new ApiFactory(await pg.CreateDatabaseAsync(), jwtKey: "kisa");
        Assert.ThrowsAny<Exception>(() => weakJwt.CreateClient());
        using var placeholderJwt = new ApiFactory(await pg.CreateDatabaseAsync(), jwtKey: "BURAYA_EN_AZ_32_KARAKTERLIK_RASTGELE_ANAHTAR_YAZIN");
        Assert.ThrowsAny<Exception>(() => placeholderJwt.CreateClient());
    }

    [Fact]
    public async Task Security_headers_are_set_and_swagger_is_off_in_production()
    {
        using var prod = await NewFactory("Production");
        var res = await prod.CreateClient().GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        Assert.Equal("nosniff", res.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Equal("DENY", res.Headers.GetValues("X-Frame-Options").Single());
        Assert.Contains("default-src 'self'", res.Headers.GetValues("Content-Security-Policy").Single());
        Assert.NotEqual(HttpStatusCode.OK, (await prod.CreateClient().GetAsync("/swagger/v1/swagger.json")).StatusCode);

        using var dev = await NewFactory("Development");
        Assert.Equal(HttpStatusCode.OK, (await dev.CreateClient().GetAsync("/swagger/v1/swagger.json")).StatusCode);
    }

    [Fact]
    public async Task Errors_are_returned_as_localized_problem_details_without_internal_details()
    {
        using var f = await NewFactory();
        var c = await f.LoggedInClientAsync();

        var validation = await c.PostAsJsonAsync("/api/customers", new { name = "", customerType = "Company", paymentTermDays = 0, eInvoiceStatus = "Unknown" });
        Assert.Equal(HttpStatusCode.BadRequest, validation.StatusCode);
        var v = await validation.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(v.GetProperty("errors").TryGetProperty("name", out _));

        var notFound = await c.GetAsync($"/api/customers/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, notFound.StatusCode);
        Assert.Contains("bulunamadı", await notFound.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.NotFound, (await c.GetAsync("/api/customers/not-a-guid")).StatusCode);

        var bad = await c.PostAsync("/api/customers", new StringContent("{ bozuk json", System.Text.Encoding.UTF8, "application/json"));
        Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);
        var text = await bad.Content.ReadAsStringAsync();
        Assert.DoesNotContain("at SutTicaretCRM", text);
        Assert.DoesNotContain("Npgsql", text);
    }

    // ───────────── Test 27: hatalı dosya yükleme (HTTP üzerinden) ─────────────

    private static async Task<(Guid InvoiceId, Guid PaymentId)> SeedInvoiceAndPayment(HttpClient c)
    {
        async Task<JsonElement> Post(string url, object body)
        {
            var r = await c.PostAsJsonAsync(url, body);
            var txt = await r.Content.ReadAsStringAsync();
            Assert.True(r.IsSuccessStatusCode, $"{url}: {(int)r.StatusCode} {txt}");
            return JsonSerializer.Deserialize<JsonElement>(txt);
        }
        var cust = await Post("/api/customers", new { name = "Dosya Müşterisi", customerType = "Company", paymentTermDays = 0, eInvoiceStatus = "Unknown" });
        var prod = await Post("/api/products", new { code = "D1", name = "Ürün", unit = "Adet", purchasePrice = 1, salePrice = 10, vatRate = 10, trackStock = false, minStock = 0 });
        var sale = await Post("/api/sales", new
        {
            customerId = cust.GetProperty("id"), saleDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)).ToString("yyyy-MM-dd"), paymentType = "Credit",
            items = new[] { new { productId = prod.GetProperty("id"), quantity = 2, discountPercent = 0, discountAmount = 0 } }, approve = true,
        });
        var inv = await Post("/api/invoices/draft", new { saleId = sale.GetProperty("id") });
        var pay = await Post("/api/payments", new
        {
            customerId = cust.GetProperty("id"), paymentDate = DateOnly.FromDateTime(DateTime.UtcNow.AddHours(3)).ToString("yyyy-MM-dd"),
            amount = 5, method = "Cash", autoAllocate = false,
        });
        return (inv.GetProperty("id").GetGuid(), pay.GetProperty("id").GetGuid());
    }

    private static MultipartFormDataContent File(string name, byte[] bytes, string contentType = "application/octet-stream")
    {
        var content = new MultipartFormDataContent();
        var part = new ByteArrayContent(bytes);
        part.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        content.Add(part, "file", name);
        return content;
    }

    [Fact] // Test 27
    public async Task Invalid_uploads_are_rejected_with_400_and_valid_ones_are_stored()
    {
        using var f = await NewFactory();
        var c = await f.LoggedInClientAsync();
        var (invoiceId, paymentId) = await SeedInvoiceAndPayment(c);

        // Çalıştırılabilir dosya, sahte uzantı, boş dosya, bozuk XML, desteklenmeyen tür
        var cases = new (string Name, byte[] Bytes)[]
        {
            ("virus.exe", "MZ\x90\0"u8.ToArray()),
            ("fatura.pdf", "bu pdf degil"u8.ToArray()),
            ("bos.pdf", []),
            ("bozuk.xml", "<a><b></a>"u8.ToArray()),
            ("sayfa.html", "<html></html>"u8.ToArray()),
            ("..\\..\\evil.pdf", "bu pdf degil"u8.ToArray()),
        };
        foreach (var (name, bytes) in cases)
        {
            var res = await c.PostAsync($"/api/invoices/{invoiceId}/attachments", File(name, bytes));
            Assert.True(res.StatusCode == HttpStatusCode.BadRequest, $"{name}: {(int)res.StatusCode}");
        }
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync($"/api/payments/{paymentId}/receipt", File("dekont.pdf", "MZ.."u8.ToArray()))).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, (await c.PostAsync($"/api/payments/{paymentId}/receipt", File("dekont.xml", "<a/>"u8.ToArray()))).StatusCode);   // dekontta XML yok

        // Çok büyük dosya
        var big = new byte[11 * 1024 * 1024 + 10];
        "%PDF-"u8.CopyTo(big);
        var tooBig = await c.PostAsync($"/api/invoices/{invoiceId}/attachments", File("buyuk.pdf", big, "application/pdf"));
        Assert.True(tooBig.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge, $"{(int)tooBig.StatusCode}");

        // Hiçbir geçersiz dosya fatura kaydına bağlanmadı
        var inv = await (await c.GetAsync($"/api/invoices/{invoiceId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, inv.GetProperty("attachments").GetArrayLength());

        // Geçerli PDF kabul edilir ve indirilebilir
        var ok = await c.PostAsync($"/api/invoices/{invoiceId}/attachments", File("fatura.pdf", "%PDF-1.4 icerik"u8.ToArray(), "application/pdf"));
        Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
        var att = await ok.Content.ReadFromJsonAsync<JsonElement>();
        var dl = await c.GetAsync($"/api/invoices/{invoiceId}/attachments/{att.GetProperty("id").GetGuid()}");
        Assert.Equal(HttpStatusCode.OK, dl.StatusCode);
        Assert.Equal("application/pdf", dl.Content.Headers.ContentType!.MediaType);
        Assert.Equal("%PDF-1.4 icerik", await dl.Content.ReadAsStringAsync());

        // Yüklenen dosya adı disk yoluna taşınmadı
        var storage = f.Services.GetRequiredService<Microsoft.Extensions.Options.IOptions<SutTicaretCRM.Infrastructure.Services.StorageOptions>>().Value.Root;
        var stored = Directory.GetFiles(storage, "*", SearchOption.AllDirectories);
        Assert.Single(stored);
        Assert.DoesNotContain("evil", stored[0]);
    }

    [Fact]
    public async Task Api_serializes_enums_as_strings_and_exposes_separate_invoice_statuses()
    {
        using var f = await NewFactory();
        var c = await f.LoggedInClientAsync();
        var (invoiceId, _) = await SeedInvoiceAndPayment(c);
        var inv = await (await c.GetAsync($"/api/invoices/{invoiceId}")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("Draft", inv.GetProperty("internalStatus").GetString());
        Assert.Equal("NotIssued", inv.GetProperty("officialStatus").GetString());
        Assert.Equal(JsonValueKind.Null, inv.GetProperty("officialNumber").ValueKind);
        var list = await (await c.GetAsync("/api/invoices?awaitingOfficial=false")).Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(1, list.GetProperty("total").GetInt32());
    }
}
