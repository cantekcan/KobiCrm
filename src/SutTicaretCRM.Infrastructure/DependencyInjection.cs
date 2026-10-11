using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using SutTicaretCRM.Application;
using SutTicaretCRM.Infrastructure.Persistence;
using SutTicaretCRM.Infrastructure.Providers;
using SutTicaretCRM.Infrastructure.Services;

namespace SutTicaretCRM.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, IConfiguration config, Action<DbContextOptionsBuilder>? extraDbOptions = null)
    {
        var cs = config.GetConnectionString("Default") ?? throw new InvalidOperationException("ConnectionStrings:Default tanımlı değil.");
        services.AddDbContext<AppDbContext>(o =>
        {
            o.UseNpgsql(cs, npg =>
            {
                npg.MigrationsAssembly(typeof(AppDbContext).Assembly.FullName);
                npg.UseQuerySplittingBehavior(QuerySplittingBehavior.SplitQuery);
            });
            extraDbOptions?.Invoke(o);
        });
        services.AddScoped<IAppDbContext>(sp => sp.GetRequiredService<AppDbContext>());
        services.AddScoped<INumberSequence, NumberSequenceService>();
        services.AddSingleton<IClock, SystemClock>();
        services.Configure<StorageOptions>(config.GetSection("Storage"));
        services.AddSingleton<IFileStore, LocalFileStore>();

        services.Configure<InvoiceOptions>(config.GetSection("Invoice"));
        services.AddSingleton<IInvoiceProvider, ManualPortalInvoiceProvider>();
        if (config.GetValue<bool>("Invoice:EnableMockProvider"))
        {
            services.AddSingleton<MockInvoiceProvider>();
            services.AddSingleton<IInvoiceProvider>(sp => sp.GetRequiredService<MockInvoiceProvider>());
        }
        // e-Arşiv ağ geçidi (f/fatura): yalnızca açıkça yapılandırılırsa kaydedilir. Müşteri verisi yalnızca yerel ağ geçidine gider.
        var gwUrl = config["Invoice:Gateway:Url"];
        if (!string.IsNullOrWhiteSpace(gwUrl))
        {
            if (!Uri.TryCreate(gwUrl, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                throw new InvalidOperationException("Invoice:Gateway:Url geçerli bir http(s) adresi olmalı.");
            if (!uri.IsLoopback && !config.GetValue<bool>("Invoice:Gateway:AllowRemote"))
                throw new InvalidOperationException("Fatura ağ geçidi yerel (loopback) adreste olmalıdır; müşteri verisi uzak bir sunucuya gönderilmez. Bilerek izin vermek için Invoice:Gateway:AllowRemote=true.");
            var gwToken = config["Invoice:Gateway:Token"] ?? "";
            if (gwToken.Length < 32 || gwToken.Contains("BURAYA", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Invoice:Gateway:Token en az 32 karakterlik rastgele bir değer olmalı (yer tutucu kabul edilmez).");
            services.Configure<InvoiceGatewayOptions>(config.GetSection("Invoice:Gateway"));
            services.AddHttpClient<EArsivGatewayInvoiceProvider>();
            services.AddTransient<IInvoiceProvider>(sp => sp.GetRequiredService<EArsivGatewayInvoiceProvider>());
        }
        return services;
    }
}
