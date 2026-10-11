using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SutTicaretCRM.Application;
using SutTicaretCRM.Application.Services;
using SutTicaretCRM.Domain;
using SutTicaretCRM.Infrastructure;
using SutTicaretCRM.Infrastructure.Persistence;
using SutTicaretCRM.Infrastructure.Providers;
using Testcontainers.PostgreSql;

namespace SutTicaretCRM.IntegrationTests;

/// <summary>Tüm testler için tek PostgreSQL konteyneri (postgres:16-alpine). Her test kendi veritabanını alır.</summary>
public sealed class PostgresFixture : IAsyncLifetime
{
    private readonly PostgreSqlContainer _container = new PostgreSqlBuilder("postgres:16-alpine").Build();
    public string AdminConnectionString => _container.GetConnectionString();

    public Task InitializeAsync() => _container.StartAsync();
    public Task DisposeAsync() => _container.DisposeAsync().AsTask();

    public async Task<string> CreateDatabaseAsync()
    {
        var name = "t_" + Guid.NewGuid().ToString("N");
        await using var conn = new NpgsqlConnection(AdminConnectionString);
        await conn.OpenAsync();
        await using var cmd = new NpgsqlCommand($"CREATE DATABASE \"{name}\"", conn);
        await cmd.ExecuteNonQueryAsync();
        return new NpgsqlConnectionStringBuilder(AdminConnectionString) { Database = name, Pooling = false }.ConnectionString;
    }
}

[CollectionDefinition("pg")]
public class PgCollection : ICollectionFixture<PostgresFixture>;

public sealed class TestClock : IClock
{
    public DateOnly Today { get; set; } = new(2026, 10, 15);
    public DateTime UtcNow => Today.ToDateTime(new TimeOnly(9, 0), DateTimeKind.Utc);
}

public sealed class TestUser : ICurrentUser
{
    public Guid? UserId => null;
    public string UserName => "test";
}

/// <summary>SaveChanges sırasında isteğe bağlı hata fırlatır (transaction geri alma testleri için).</summary>
public sealed class FailingSaveInterceptor : SaveChangesInterceptor
{
    public bool FailNext { get; set; }

    public override ValueTask<InterceptionResult<int>> SavingChangesAsync(DbContextEventData eventData, InterceptionResult<int> result, CancellationToken ct = default)
    {
        if (FailNext)
        {
            FailNext = false;
            throw new DbUpdateException("Test: yapay veritabanı hatası.");
        }
        return base.SavingChangesAsync(eventData, result, ct);
    }
}

/// <summary>Bir test için izole uygulama: kendi veritabanı, sabit saat, sahte sağlayıcı. İstek başına yeni scope kullanır.</summary>
public sealed class TestApp : IAsyncDisposable
{
    public IServiceProvider Root { get; private set; } = null!;
    public TestClock Clock { get; } = new();
    public FailingSaveInterceptor Interceptor { get; } = new();
    public FakeLiveProvider Live { get; } = new();
    public FakeGatewayHandler GatewayHandler { get; } = new();
    public EArsivGatewayInvoiceProvider Gateway { get; }

    private TestApp()
    {
        // e-Arşiv ağ geçidi sağlayıcısı, sahte HTTP işleyiciyle (TEST ortamı; gerçek ağ/GİB çağrısı yok)
        Gateway = new EArsivGatewayInvoiceProvider(new HttpClient(GatewayHandler),
            Microsoft.Extensions.Options.Options.Create(new InvoiceGatewayOptions { Url = "http://127.0.0.1:5090", Token = new string('t', 40), Environment = "TEST", TimeoutSeconds = 5 }));
    }
    public MockInvoiceProvider Mock => Root.GetRequiredService<MockInvoiceProvider>();
    public string ConnectionString { get; private set; } = "";
    private string _storage = "";

    public static async Task<TestApp> CreateAsync(PostgresFixture pg)
    {
        var app = new TestApp { ConnectionString = await pg.CreateDatabaseAsync() };
        app._storage = Path.Combine(Path.GetTempPath(), "suttcrm-test-" + Guid.NewGuid().ToString("N"));
        var config = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["ConnectionStrings:Default"] = app.ConnectionString,
            ["Invoice:EnableMockProvider"] = "true",
            ["Storage:Root"] = app._storage,
            ["Seed:AdminPassword"] = "Test-Parola-12345",
        }).Build();

        var services = new ServiceCollection();
        services.AddLogging();
        services.AddApplication();
        services.AddInfrastructure(config, o => o.AddInterceptors(app.Interceptor));
        services.AddSingleton<IClock>(app.Clock);
        services.AddScoped<ICurrentUser, TestUser>();
        services.AddSingleton<IInvoiceProvider>(app.Live);
        services.AddSingleton<IInvoiceProvider>(app.Gateway);
        app.Root = services.BuildServiceProvider();

        using var scope = app.Root.CreateScope();
        await DbSeeder.InitializeAsync(scope.ServiceProvider.GetRequiredService<AppDbContext>(), config,
            scope.ServiceProvider.GetRequiredService<Microsoft.Extensions.Logging.ILoggerFactory>().CreateLogger("test"));
        return app;
    }

    /// <summary>Her çağrı yeni scope (= yeni DbContext) kullanır; böylece gerçek istek izolasyonu taklit edilir.</summary>
    public async Task<TResult> Use<TService, TResult>(Func<TService, Task<TResult>> action) where TService : notnull
    {
        using var scope = Root.CreateScope();
        return await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public async Task Use<TService>(Func<TService, Task> action) where TService : notnull
    {
        using var scope = Root.CreateScope();
        await action(scope.ServiceProvider.GetRequiredService<TService>());
    }

    public async Task<TResult> Db<TResult>(Func<AppDbContext, Task<TResult>> query)
    {
        using var scope = Root.CreateScope();
        return await query(scope.ServiceProvider.GetRequiredService<AppDbContext>());
    }

    // ── Hazır veri yardımcıları ──

    private int _seq;

    public async Task<CustomerDetailDto> NewCustomer(string? name = null, decimal? opening = null, int termDays = 30,
        EInvoiceTaxpayerStatus einv = EInvoiceTaxpayerStatus.Unknown)
    {
        var n = Interlocked.Increment(ref _seq);
        return await Use<CustomerService, CustomerDetailDto>(s => s.CreateAsync(new CustomerRequest(
            name ?? $"Müşteri {n}", CustomerType.Company, null, null, null, "0532 000 00 00", null, "Adres", "Ankara", "Çankaya",
            termDays, einv, null, true,
            opening is null ? null : new OpeningBalanceRequest(opening.Value, new DateOnly(2026, 1, 1), null, "Devir"))));
    }

    public async Task<ProductDto> NewProduct(string? code = null, decimal sale = 200m, decimal vat = 10m, bool track = true, decimal? stock = null, string unit = "Kg", decimal min = 0)
    {
        var n = Interlocked.Increment(ref _seq);
        var p = await Use<ProductService, ProductDto>(s => s.CreateAsync(new ProductRequest(code ?? $"P{n:000}", $"Ürün {n}", null, unit, sale / 2, sale, vat, track, min, null)));
        if (stock is > 0)
            await Use<StockService, StockMovementDto>(s => s.AdjustAsync(new StockAdjustRequest(p.Id, Clock.Today, StockMovementType.Opening, stock, null, "açılış")));
        return p;
    }

    public SaleRequest SaleOf(Guid customerId, ProductDto product, decimal qty, decimal? price = null, decimal discountPct = 0, bool approve = true,
        PaymentType type = PaymentType.Credit, Guid? clientKey = null, ImmediatePayment? pay = null, DateOnly? date = null) =>
        new(customerId, date ?? Clock.Today, type, null, null, [new SaleItemRequest(product.Id, qty, price, discountPct, 0, null)], approve, clientKey, pay);

    public Task<SaleDto> NewSale(Guid customerId, ProductDto product, decimal qty, decimal? price = null, decimal discountPct = 0, bool approve = true) =>
        Use<SaleService, SaleDto>(s => s.CreateAsync(SaleOf(customerId, product, qty, price, discountPct, approve)));

    public Task<PaymentDto> NewPayment(Guid customerId, decimal amount, bool auto = false, List<AllocationRequest>? alloc = null) =>
        Use<PaymentService, PaymentDto>(s => s.CreateAsync(new PaymentRequest(customerId, Clock.Today, amount, PaymentMethod.BankTransfer, null, null, alloc, auto, null)));

    public Task<decimal> Balance(Guid customerId) =>
        Use<LedgerService, decimal>(async l => (await l.GetFinancialsAsync(customerId)).Balance);

    public async ValueTask DisposeAsync()
    {
        if (Root is IAsyncDisposable d) await d.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
        try { if (Directory.Exists(_storage)) Directory.Delete(_storage, true); } catch { }
    }
}

public abstract class IntegrationTestBase(PostgresFixture pg) : IAsyncLifetime
{
    protected TestApp App { get; private set; } = null!;
    public async Task InitializeAsync() => App = await TestApp.CreateAsync(pg);
    public async Task DisposeAsync() => await App.DisposeAsync();
}

/// <summary>"Canlı gönderim kapalıyken hiçbir çağrı yapılmaz" testi için: IsLiveCapable = true, çağrıları sayar.</summary>
public sealed class FakeLiveProvider : IInvoiceProvider
{
    public int Calls;
    public string Name => "FakeLive";
    public string DisplayName => "Sahte canlı sağlayıcı";
    public InvoiceProviderCapabilities Capabilities { get; } = new(true, true, true, true, false, false, true, true, true, false, false, "test");
    private ProviderResult<T> Hit<T>() { Interlocked.Increment(ref Calls); return ProviderResult<T>.NotSupported("kullanılmamalıydı"); }
    public Task<ProviderResult<ProviderValidation>> ValidateInvoiceAsync(InvoiceDocumentRequest r, CancellationToken ct = default) => Task.FromResult(Hit<ProviderValidation>());
    public Task<ProviderResult<ProviderDraft>> CreateDraftAsync(InvoiceDocumentRequest r, CancellationToken ct = default) => Task.FromResult(Hit<ProviderDraft>());
    public Task<ProviderResult<ProviderChallenge>> RequestIssueChallengeAsync(ProviderReference r, CancellationToken ct = default) => Task.FromResult(Hit<ProviderChallenge>());
    public Task<ProviderResult<ProviderIssued>> IssueAsync(ProviderReference r, ProviderIssueInput i, CancellationToken ct = default) => Task.FromResult(Hit<ProviderIssued>());
    public Task<ProviderResult<ProviderStatus>> GetInvoiceStatusAsync(ProviderReference r, CancellationToken ct = default) => Task.FromResult(Hit<ProviderStatus>());
    public Task<ProviderResult<ProviderDocument>> GetInvoiceDocumentAsync(ProviderReference r, ProviderDocumentFormat f, CancellationToken ct = default) => Task.FromResult(Hit<ProviderDocument>());
    public Task<ProviderResult<ProviderCancellation>> RequestCancellationAsync(ProviderReference r, string reason, CancellationToken ct = default) => Task.FromResult(Hit<ProviderCancellation>());
}

public static class TestAppExtensions
{
    public static async Task SetProvider(this TestApp app, string name)
    {
        using var scope = app.Root.CreateScope();
        var settings = scope.ServiceProvider.GetRequiredService<SettingsService>();
        await settings.SetAsync(SettingsService.InvoiceProviderKey, name);
        await scope.ServiceProvider.GetRequiredService<IAppDbContext>().SaveChangesAsync();
    }

    public static async Task SetAllowNegativeStock(this TestApp app, bool allow) =>
        await app.Db(async db =>
        {
            var s = await db.AppSettings.FirstAsync(x => x.Key == SettingsService.AllowNegativeStockKey);
            s.Value = allow ? "true" : "false";
            return await db.SaveChangesAsync();
        });

    public static Task<InvoiceDto> NewApprovedInvoice(this TestApp app, SaleDto sale, InvoiceDocumentType type = InvoiceDocumentType.EArchive) =>
        app.Use<InvoiceService, InvoiceDto>(async s =>
        {
            var d = await s.CreateDraftFromSaleAsync(sale.Id, null);
            await s.UpdateDraftAsync(d.Id, new UpdateInvoiceRequest(app.Clock.Today, null, type, d.BuyerName, null, null, "10000000146",
                "Adres", "Ankara", "Çankaya", null, null, null));
            return await s.ApproveAsync(d.Id);
        });

    public static Task<InvoiceDto> RecordOfficial(this TestApp app, Guid invoiceId, string number, string? uuid = null) =>
        app.Use<InvoiceService, InvoiceDto>(s => s.RecordOfficialAsync(invoiceId, new RecordOfficialRequest(number, app.Clock.Today, uuid, InvoiceDocumentType.EArchive)));
}

public static class TestAppInvoiceExtensions
{
    public static Task<InvoiceDto> ApproveDraft(this TestApp app, Guid invoiceId, InvoiceDocumentType type = InvoiceDocumentType.EArchive) =>
        app.Use<InvoiceService, InvoiceDto>(async s =>
        {
            var d = await s.GetAsync(invoiceId);
            await s.UpdateDraftAsync(invoiceId, new UpdateInvoiceRequest(app.Clock.Today, null, type, d.BuyerName, null, null, "10000000146", "Adres", "Ankara", "Çankaya", null, null, null));
            return await s.ApproveAsync(invoiceId);
        });
}
