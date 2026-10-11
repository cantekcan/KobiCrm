using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace SutTicaretCRM.Infrastructure.Persistence;

/// <summary>`dotnet ef` komutları için. Bağlantı yalnızca migration üretimi içindir; veritabanına bağlanmaz.</summary>
public class DesignTimeFactory : IDesignTimeDbContextFactory<AppDbContext>
{
    public AppDbContext CreateDbContext(string[] args)
    {
        var o = new DbContextOptionsBuilder<AppDbContext>()
            .UseNpgsql("Host=localhost;Database=design_time;Username=design;Password=design")
            .Options;
        return new AppDbContext(o);
    }
}
