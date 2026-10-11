using System.Globalization;
using Microsoft.EntityFrameworkCore;
using SutTicaretCRM.Domain;

namespace SutTicaretCRM.Application.Services;

// ───────────── Denetim ─────────────

public interface IAuditLogger
{
    /// <summary>Denetim kaydını mevcut işlemle birlikte kaydedilmek üzere ekler (SaveChanges çağırmaz).</summary>
    void Log(string action, string entityType, Guid? entityId, string details);
}

public class AuditLogger(IAppDbContext db, ICurrentUser user, IClock clock) : IAuditLogger
{
    public void Log(string action, string entityType, Guid? entityId, string details) =>
        db.AuditLogs.Add(new AuditLog
        {
            TimestampUtc = clock.UtcNow,
            UserId = user.UserId,
            UserName = user.UserName,
            Action = action,
            EntityType = entityType,
            EntityId = entityId,
            Details = details.Length > 1000 ? details[..1000] : details,
        });
}

// ───────────── Ayarlar ─────────────

public record CompanySettings(string Name, string? TaxOffice, string? TaxNumber, string? Address, string? Phone, string? Email);

public record SettingsDto(
    CompanySettings Company,
    IReadOnlyList<decimal> VatRates,
    IReadOnlyList<string> Units,
    bool AllowNegativeStock,
    string InvoiceProvider,
    string DatabaseMode,
    bool LiveSendingEnabled);

public record UpdateSettingsRequest(
    CompanySettings Company,
    List<decimal> VatRates,
    List<string> Units,
    bool AllowNegativeStock,
    string InvoiceProvider);

public class SettingsService(IAppDbContext db, IAuditLogger audit, IClock clock, Microsoft.Extensions.Options.IOptions<InvoiceOptions> invoiceOptions)
{
    public const string CompanyName = "company.name";
    public const string CompanyTaxOffice = "company.taxOffice";
    public const string CompanyTaxNumber = "company.taxNumber";
    public const string CompanyAddress = "company.address";
    public const string CompanyPhone = "company.phone";
    public const string CompanyEmail = "company.email";
    public const string VatRatesKey = "vat.allowedRates";
    public const string UnitsKey = "units";
    public const string AllowNegativeStockKey = "stock.allowNegative";
    public const string InvoiceProviderKey = "invoice.provider";
    public const string DatabaseModeKey = "db.mode";

    public static readonly decimal[] DefaultVatRates = [0m, 1m, 10m, 20m];
    public static readonly string[] DefaultUnits = ["Adet", "Koli", "Kg", "Gram", "Litre"];

    public async Task<string?> GetValueAsync(string key, CancellationToken ct = default) =>
        await db.AppSettings.Where(s => s.Key == key).Select(s => s.Value).FirstOrDefaultAsync(ct);

    public async Task<IReadOnlyList<decimal>> GetVatRatesAsync(CancellationToken ct = default)
    {
        var raw = await GetValueAsync(VatRatesKey, ct);
        var parsed = (raw ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(x => decimal.TryParse(x, NumberStyles.Number, CultureInfo.InvariantCulture, out var d) ? (decimal?)d : null)
            .Where(x => x.HasValue).Select(x => x!.Value).Distinct().OrderBy(x => x).ToList();
        return parsed.Count > 0 ? parsed : DefaultVatRates;
    }

    public async Task<IReadOnlyList<string>> GetUnitsAsync(CancellationToken ct = default)
    {
        var raw = await GetValueAsync(UnitsKey, ct);
        var parsed = (raw ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Distinct().ToList();
        return parsed.Count > 0 ? parsed : DefaultUnits;
    }

    public async Task<bool> GetAllowNegativeStockAsync(CancellationToken ct = default) =>
        !string.Equals(await GetValueAsync(AllowNegativeStockKey, ct), "false", StringComparison.OrdinalIgnoreCase);

    public async Task<string> GetInvoiceProviderNameAsync(CancellationToken ct = default) =>
        await GetValueAsync(InvoiceProviderKey, ct) ?? "Manual";

    public async Task<string> GetDatabaseModeAsync(CancellationToken ct = default) =>
        await GetValueAsync(DatabaseModeKey, ct) ?? "Live";

    /// <summary>KDV oranı yalnızca ayarlarda tanımlı oranlardan biri olabilir. Sistem yürürlükteki oranları kendisi bilmez.</summary>
    public async Task EnsureVatRateAllowedAsync(decimal rate, string field = "vatRate", CancellationToken ct = default)
    {
        var allowed = await GetVatRatesAsync(ct);
        if (!allowed.Contains(rate))
            throw new ValidationFailedException(field,
                $"%{rate} KDV oranı tanımlı değil. Tanımlı oranlar: {string.Join(", ", allowed.Select(r => "%" + r))}. Gerekirse Ayarlar'dan oran ekleyebilirsiniz.");
    }

    public async Task<SettingsDto> GetAsync(CancellationToken ct = default)
    {
        var all = await db.AppSettings.ToDictionaryAsync(s => s.Key, s => s.Value, ct);
        string? V(string k) => all.TryGetValue(k, out var v) && !string.IsNullOrWhiteSpace(v) ? v : null;
        return new SettingsDto(
            new CompanySettings(V(CompanyName) ?? "", V(CompanyTaxOffice), V(CompanyTaxNumber), V(CompanyAddress), V(CompanyPhone), V(CompanyEmail)),
            await GetVatRatesAsync(ct), await GetUnitsAsync(ct), await GetAllowNegativeStockAsync(ct),
            V(InvoiceProviderKey) ?? "Manual", V(DatabaseModeKey) ?? "Live", invoiceOptions.Value.LiveSendingEnabled);
    }

    public async Task<SettingsDto> UpdateAsync(UpdateSettingsRequest r, CancellationToken ct = default)
    {
        var v = new ValidationBuilder();
        v.When(r.VatRates.Count == 0, "vatRates", "En az bir KDV oranı tanımlı olmalıdır.");
        v.When(r.VatRates.Any(x => x < 0 || x > 100), "vatRates", "KDV oranları 0 ile 100 arasında olmalıdır.");
        v.When(r.Units.Count == 0 || r.Units.Any(string.IsNullOrWhiteSpace), "units", "Birim adları boş olamaz.");
        v.When(r.Units.Any(u => u.Contains(',')), "units", "Birim adında virgül kullanılamaz.");
        v.When(r.Company.TaxNumber is { Length: > 0 } tn && !TurkishTaxIds.IsValidVkn(tn) && !TurkishTaxIds.IsValidTckn(tn), "company.taxNumber", "Vergi/T.C. kimlik numarası geçerli değil.");
        v.ThrowIfAny();

        await SetAsync(CompanyName, r.Company.Name, ct);
        await SetAsync(CompanyTaxOffice, r.Company.TaxOffice, ct);
        await SetAsync(CompanyTaxNumber, r.Company.TaxNumber, ct);
        await SetAsync(CompanyAddress, r.Company.Address, ct);
        await SetAsync(CompanyPhone, r.Company.Phone, ct);
        await SetAsync(CompanyEmail, r.Company.Email, ct);
        await SetAsync(VatRatesKey, string.Join(",", r.VatRates.Distinct().OrderBy(x => x).Select(x => x.ToString(CultureInfo.InvariantCulture))), ct);
        await SetAsync(UnitsKey, string.Join(",", r.Units.Select(u => u.Trim()).Distinct()), ct);
        await SetAsync(AllowNegativeStockKey, r.AllowNegativeStock ? "true" : "false", ct);
        await SetAsync(InvoiceProviderKey, string.IsNullOrWhiteSpace(r.InvoiceProvider) ? "Manual" : r.InvoiceProvider.Trim(), ct);
        audit.Log("Ayarlar güncellendi", "AppSettings", null, "Firma/KDV/birim/stok/fatura sağlayıcı ayarları");
        await db.SaveChangesAsync(ct);
        return await GetAsync(ct);
    }

    public async Task SetAsync(string key, string? value, CancellationToken ct = default)
    {
        var s = await db.AppSettings.FirstOrDefaultAsync(x => x.Key == key, ct);
        if (s is null) db.AppSettings.Add(new AppSetting { Key = key, Value = value ?? "" });
        else s.Value = value ?? "";
    }
}

// ───────────── Dosya doğrulama ─────────────

public static class FileValidator
{
    public const long MaxReceiptBytes = 5 * 1024 * 1024;
    public const long MaxInvoiceFileBytes = 10 * 1024 * 1024;

    public record Result(string Extension, string ContentType, AttachmentKind Kind);

    /// <summary>Uzantıya göre izin verir ve dosya içeriğinin (ilk baytlar) bildirilen türle uyuştuğunu doğrular.</summary>
    public static async Task<Result> ValidateAsync(Stream content, string fileName, bool invoiceFile, CancellationToken ct = default)
    {
        var ext = Path.GetExtension(fileName ?? "").ToLowerInvariant();
        var allowed = invoiceFile ? new[] { ".pdf", ".xml" } : new[] { ".pdf", ".jpg", ".jpeg", ".png" };
        if (!allowed.Contains(ext))
            throw new ValidationFailedException("file", $"Bu dosya türü kabul edilmiyor. İzin verilenler: {string.Join(", ", allowed)}.");

        var max = invoiceFile ? MaxInvoiceFileBytes : MaxReceiptBytes;
        if (content.Length == 0) throw new ValidationFailedException("file", "Dosya boş.");
        if (content.Length > max) throw new ValidationFailedException("file", $"Dosya çok büyük (en fazla {max / 1024 / 1024} MB).");

        var head = new byte[16];
        content.Position = 0;
        var read = await content.ReadAsync(head.AsMemory(0, head.Length), ct);
        content.Position = 0;

        bool ok = ext switch
        {
            ".pdf" => read >= 5 && head[0] == '%' && head[1] == 'P' && head[2] == 'D' && head[3] == 'F' && head[4] == '-',
            ".png" => read >= 8 && head[0] == 0x89 && head[1] == 'P' && head[2] == 'N' && head[3] == 'G',
            ".jpg" or ".jpeg" => read >= 3 && head[0] == 0xFF && head[1] == 0xD8 && head[2] == 0xFF,
            ".xml" => LooksLikeXml(head, read),
            _ => false,
        };
        if (!ok) throw new ValidationFailedException("file", "Dosya içeriği uzantısıyla uyuşmuyor ya da bozuk.");

        if (ext == ".xml") await EnsureWellFormedXmlAsync(content, ct);

        var contentType = ext switch { ".pdf" => "application/pdf", ".png" => "image/png", ".xml" => "application/xml", _ => "image/jpeg" };
        var kind = ext == ".pdf" ? AttachmentKind.Pdf : ext == ".xml" ? AttachmentKind.Xml : AttachmentKind.Other;
        return new Result(ext == ".jpeg" ? ".jpg" : ext, contentType, kind);
    }

    private static bool LooksLikeXml(byte[] head, int read)
    {
        var i = 0;
        if (read >= 3 && head[0] == 0xEF && head[1] == 0xBB && head[2] == 0xBF) i = 3;
        while (i < read && (head[i] == ' ' || head[i] == '\r' || head[i] == '\n' || head[i] == '\t')) i++;
        return i < read && head[i] == '<';
    }

    private static async Task EnsureWellFormedXmlAsync(Stream content, CancellationToken ct)
    {
        try
        {
            var settings = new System.Xml.XmlReaderSettings { DtdProcessing = System.Xml.DtdProcessing.Prohibit, XmlResolver = null, Async = true };
            using var reader = System.Xml.XmlReader.Create(content, settings);
            while (await reader.ReadAsync()) { ct.ThrowIfCancellationRequested(); }
        }
        catch (System.Xml.XmlException)
        {
            throw new ValidationFailedException("file", "XML dosyası geçerli değil.");
        }
        finally { content.Position = 0; }
    }
}

// ───────────── Fiyatlandırma ─────────────

public class PricingService(IAppDbContext db)
{
    /// <summary>Müşteriye özel fiyat varsa onu, yoksa ürünün varsayılan KDV hariç satış fiyatını döndürür.</summary>
    public async Task<(decimal Price, bool IsCustomerPrice)> GetEffectivePriceAsync(Guid customerId, Product product, CancellationToken ct = default)
    {
        var special = await db.CustomerProductPrices
            .Where(p => p.CustomerId == customerId && p.ProductId == product.Id)
            .Select(p => (decimal?)p.Price).FirstOrDefaultAsync(ct);
        return special.HasValue ? (special.Value, true) : (product.SalePrice, false);
    }

    public async Task<Dictionary<Guid, decimal>> GetCustomerPricesAsync(Guid customerId, CancellationToken ct = default) =>
        await db.CustomerProductPrices.Where(p => p.CustomerId == customerId).ToDictionaryAsync(p => p.ProductId, p => p.Price, ct);
}
