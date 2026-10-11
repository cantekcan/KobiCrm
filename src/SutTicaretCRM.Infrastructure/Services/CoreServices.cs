using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SutTicaretCRM.Application;
using SutTicaretCRM.Infrastructure.Persistence;

namespace SutTicaretCRM.Infrastructure.Services;

public sealed class SystemClock : IClock
{
    private static readonly TimeZoneInfo Turkey = Resolve();

    private static TimeZoneInfo Resolve()
    {
        foreach (var id in new[] { "Europe/Istanbul", "Turkey Standard Time" })
        {
            try { return TimeZoneInfo.FindSystemTimeZoneById(id); } catch (TimeZoneNotFoundException) { }
        }
        return TimeZoneInfo.CreateCustomTimeZone("TR", TimeSpan.FromHours(3), "TR", "TR");
    }

    public DateTime UtcNow => DateTime.UtcNow;
    public DateOnly Today => DateOnly.FromDateTime(TimeZoneInfo.ConvertTimeFromUtc(DateTime.UtcNow, Turkey));
}

/// <summary>Yıl bazlı artan numaralar. Aynı veritabanı işleminde çalışır; işlem geri alınırsa numara da geri alınır.</summary>
public sealed class NumberSequenceService(AppDbContext db) : INumberSequence
{
    public async Task<string> NextAsync(string prefix, int year, CancellationToken ct = default)
    {
        var key = $"{prefix}-{year}";
        var rows = await db.Database.SqlQuery<long>($"""
            INSERT INTO "NumberSequences" ("Key", "LastValue") VALUES ({key}, 1)
            ON CONFLICT ("Key") DO UPDATE SET "LastValue" = "NumberSequences"."LastValue" + 1
            RETURNING "LastValue" AS "Value"
            """).ToListAsync(ct);
        return $"{key}-{rows[0]:000000}";
    }
}

public sealed class StorageOptions
{
    public string Root { get; set; } = "storage";
}

/// <summary>Yüklenen dosyaları (dekont, fatura PDF/XML) yerel diskte saklar. Dosya adı sunucuda üretilir; kullanıcı girdisi yola karışmaz.</summary>
public sealed class LocalFileStore(IOptions<StorageOptions> options) : IFileStore
{
    private string Dir(string folder)
    {
        if (folder.Any(c => !char.IsAsciiLetterOrDigit(c))) throw new ArgumentException("Geçersiz klasör.");
        var path = Path.Combine(Path.GetFullPath(options.Value.Root), folder);
        Directory.CreateDirectory(path);
        return path;
    }

    public async Task<StoredFile> SaveAsync(string folder, Stream content, string extension, CancellationToken ct = default)
    {
        var name = $"{Guid.NewGuid():N}{extension.ToLowerInvariant()}";
        var full = Path.Combine(Dir(folder), name);
        content.Position = 0;
        using var sha = SHA256.Create();
        await using (var fs = new FileStream(full, FileMode.CreateNew, FileAccess.Write, FileShare.None))
        await using (var crypto = new CryptoStream(fs, sha, CryptoStreamMode.Write, leaveOpen: true))
        {
            await content.CopyToAsync(crypto, ct);
        }
        return new StoredFile(name, new FileInfo(full).Length, Convert.ToHexString(sha.Hash!).ToLowerInvariant());
    }

    public Task<Stream?> OpenReadAsync(string folder, string storedName, CancellationToken ct = default)
    {
        if (storedName != Path.GetFileName(storedName)) return Task.FromResult<Stream?>(null);
        var full = Path.Combine(Dir(folder), storedName);
        return Task.FromResult<Stream?>(File.Exists(full) ? new FileStream(full, FileMode.Open, FileAccess.Read, FileShare.Read) : null);
    }
}

/// <summary>PBKDF2-SHA256. Biçim: v1.iterasyon.tuz(b64).hash(b64)</summary>
public static class PasswordHasher
{
    private const int Iterations = 210_000;

    public static string Hash(string password)
    {
        var salt = RandomNumberGenerator.GetBytes(16);
        var hash = Rfc2898DeriveBytes.Pbkdf2(password, salt, Iterations, HashAlgorithmName.SHA256, 32);
        return $"v1.{Iterations}.{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }

    public static bool Verify(string password, string stored)
    {
        var parts = stored.Split('.');
        if (parts.Length != 4 || parts[0] != "v1" || !int.TryParse(parts[1], out var iter)) return false;
        var salt = Convert.FromBase64String(parts[2]);
        var expected = Convert.FromBase64String(parts[3]);
        var actual = Rfc2898DeriveBytes.Pbkdf2(password, salt, iter, HashAlgorithmName.SHA256, expected.Length);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }
}
