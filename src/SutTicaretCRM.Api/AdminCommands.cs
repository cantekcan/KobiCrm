using Microsoft.EntityFrameworkCore;
using SutTicaretCRM.Infrastructure.Persistence;
using SutTicaretCRM.Infrastructure.Services;

namespace SutTicaretCRM.Api;

/// <summary>
/// Komut satırından yönetim işlemleri. Parola unutulursa:
///   $env:NEW_ADMIN_PASSWORD = "<yeni parola>"
///   dotnet run --project src/SutTicaretCRM.Api -- reset-admin-password [kullanıcı-adı]
/// Parola komut satırı argümanı olarak verilmez (kabuk geçmişinde kalmasın), ortam değişkeninden okunur.
/// </summary>
public static class AdminCommands
{
    public static async Task<int> ResetPasswordAsync(AppDbContext db, string[] args, ILogger logger)
    {
        var idx = Array.IndexOf(args, "reset-admin-password");
        var userName = args.Length > idx + 1 && !args[idx + 1].StartsWith('-') ? args[idx + 1] : "admin";
        var password = Environment.GetEnvironmentVariable("NEW_ADMIN_PASSWORD");
        var problem = DbSeeder.ValidateAdminPassword(password);
        if (problem is not null) { logger.LogError("Parola sıfırlanamadı: {Problem} NEW_ADMIN_PASSWORD ortam değişkenini güçlü bir parolayla ayarlayın.", problem); return 2; }

        var user = await db.Users.FirstOrDefaultAsync(u => u.UserName == userName);
        if (user is null) { logger.LogError("Kullanıcı bulunamadı: {UserName}", userName); return 3; }
        user.PasswordHash = PasswordHasher.Hash(password!);
        user.FailedLoginCount = 0;
        user.LockoutEndUtc = null;
        db.AuditLogs.Add(new Domain.AuditLog { Action = "Parola komut satırından sıfırlandı", EntityType = nameof(Domain.User), EntityId = user.Id, UserName = "sistem", Details = userName });
        await db.SaveChangesAsync();
        logger.LogInformation("{UserName} kullanıcısının parolası sıfırlandı; hesap kilidi kaldırıldı.", userName);
        return 0;
    }
}
