using Microsoft.Extensions.DependencyInjection;
using SutTicaretCRM.Application.Services;

namespace SutTicaretCRM.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services)
    {
        services.AddScoped<IAuditLogger, AuditLogger>();
        services.AddScoped<SettingsService>();
        services.AddScoped<PricingService>();
        services.AddScoped<StockService>();
        services.AddScoped<LedgerService>();
        services.AddScoped<CustomerService>();
        services.AddScoped<ProductService>();
        services.AddScoped<PaymentService>();
        services.AddScoped<SaleService>();
        services.AddScoped<InvoiceService>();
        services.AddScoped<SupplierService>();
        services.AddScoped<ReportService>();
        services.AddScoped<DashboardService>();
        return services;
    }
}
