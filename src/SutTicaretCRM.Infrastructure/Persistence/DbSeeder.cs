using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using SutTicaretCRM.Application.Services;
using SutTicaretCRM.Domain;
using SutTicaretCRM.Infrastructure.Services;

namespace SutTicaretCRM.Infrastructure.Persistence;

public static class DbSeeder
{
    private static readonly string[] PlaceholderMarkers = ["BURAYA", "CHANGE_ME", "CHANGEME", "DEGISTIR", "YER_TUTUCU", "REPLACE_ME", "EXAMPLE"];
    private static readonly string[] WeakPatterns = ["password123", "12345678", "qwerty", "admin123", "letmein"];

    /// <summary>
    /// Migration'ları uygular, varsayılan ayarları ekler ve ilk yöneticiyi oluşturur.
    /// Varsayılan/yer tutucu parola ASLA kabul edilmez: parola yapılandırmadan (Seed:AdminPassword) gelmelidir.
    /// </summary>
    public static async Task InitializeAsync(AppDbContext db, IConfiguration config, ILogger logger, CancellationToken ct = default)
    {
        if (config.GetValue("Database:AutoMigrate", true))
            await db.Database.MigrateAsync(ct);

        await EnsureSettingAsync(db, SettingsService.VatRatesKey, string.Join(",", SettingsService.DefaultVatRates), ct);
        await EnsureSettingAsync(db, SettingsService.UnitsKey, string.Join(",", SettingsService.DefaultUnits), ct);
        await EnsureSettingAsync(db, SettingsService.AllowNegativeStockKey, "true", ct);
        await EnsureSettingAsync(db, SettingsService.InvoiceProviderKey, "Manual", ct);
        await EnsureSettingAsync(db, SettingsService.DatabaseModeKey, config.GetValue("Database:Mode", "Live") ?? "Live", ct);
        await EnsureSettingAsync(db, SettingsService.CompanyName, "", ct);

        if (!await db.Users.AnyAsync(ct))
        {
            var name = config["Seed:AdminUserName"];
            var password = config["Seed:AdminPassword"];
            if (string.IsNullOrWhiteSpace(name)) name = "admin";
            var problem = ValidateAdminPassword(password);
            if (problem is not null)
                throw new InvalidOperationException(
                    $"İlk yönetici oluşturulamadı: {problem} Seed__AdminPassword (veya .env içindeki ADMIN_PASSWORD) değerini güçlü bir parolayla tanımlayın.");
            db.Users.Add(new User { UserName = name.Trim(), DisplayName = "Yönetici", PasswordHash = PasswordHasher.Hash(password!) });
            logger.LogInformation("İlk yönetici kullanıcı oluşturuldu: {UserName}", name);
        }
        await db.SaveChangesAsync(ct);
    }

    public static string? ValidateAdminPassword(string? password)
    {
        if (string.IsNullOrWhiteSpace(password)) return "Yönetici parolası tanımlı değil.";
        if (password.Length < 10) return "Yönetici parolası en az 10 karakter olmalıdır.";
        if (PlaceholderMarkers.Any(m => password.Contains(m, StringComparison.OrdinalIgnoreCase))) return "Yönetici parolası yer tutucu bir değer içeriyor.";
        if (WeakPatterns.Any(m => password.Contains(m, StringComparison.OrdinalIgnoreCase))) return "Yönetici parolası çok kolay tahmin edilebilir.";
        return null;
    }

    private static async Task EnsureSettingAsync(AppDbContext db, string key, string value, CancellationToken ct)
    {
        if (!await db.AppSettings.AnyAsync(s => s.Key == key, ct))
            db.AppSettings.Add(new AppSetting { Key = key, Value = value });
    }
}
