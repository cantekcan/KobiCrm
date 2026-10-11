using Microsoft.EntityFrameworkCore;
using SutTicaretCRM.Domain;

namespace SutTicaretCRM.Application.Services;

public record ProductRequest(
    string Code, string Name, Guid? CategoryId, string Unit, decimal PurchasePrice, decimal SalePrice, decimal VatRate,
    bool TrackStock, decimal MinStock, string? Description, bool IsActive = true);

public record ProductDto(
    Guid Id, string Code, string Name, Guid? CategoryId, string? CategoryName, string Unit, decimal PurchasePrice, decimal SalePrice,
    decimal VatRate, bool TrackStock, decimal MinStock, decimal StockQuantity, string? Description, bool IsActive);

public record CategoryDto(Guid Id, string Name);

public record CustomerPriceDto(Guid Id, Guid CustomerId, string CustomerName, Guid ProductId, string ProductName, string Unit, decimal Price, decimal DefaultPrice);

public record PriceLookupDto(decimal Price, bool IsCustomerPrice, decimal DefaultPrice, decimal VatRate, string Unit);

public class ProductService(IAppDbContext db, SettingsService settings, StockService stock, PricingService pricing, IAuditLogger audit)
{
    public async Task<PagedResult<ProductDto>> ListAsync(string? search, Guid? categoryId, bool? active, bool? lowStock, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 500);
        var q = db.Products.AsNoTracking().Include(p => p.Category).AsQueryable();
        if (active.HasValue) q = q.Where(p => p.IsActive == active);
        if (categoryId.HasValue) q = q.Where(p => p.CategoryId == categoryId);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(p => p.Name.ToLower().Contains(s) || p.Code.ToLower().Contains(s));
        }
        var products = await q.OrderBy(p => p.Name).ToListAsync(ct);
        var levels = await stock.GetQuantitiesAsync(null, ct);
        var rows = products.Select(p => ToDto(p, p.TrackStock ? levels.GetValueOrDefault(p.Id) : 0));
        if (lowStock == true) rows = rows.Where(r => r.TrackStock && r.StockQuantity <= r.MinStock);
        var list = rows.ToList();
        return new PagedResult<ProductDto>(list.Skip((page - 1) * pageSize).Take(pageSize).ToList(), list.Count, page, pageSize);
    }

    public async Task<ProductDto> GetAsync(Guid id, CancellationToken ct = default)
    {
        var p = await db.Products.AsNoTracking().Include(x => x.Category).FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Ürün bulunamadı.");
        return ToDto(p, p.TrackStock ? await stock.GetQuantityAsync(id, ct) : 0);
    }

    private static ProductDto ToDto(Product p, decimal qty) =>
        new(p.Id, p.Code, p.Name, p.CategoryId, p.Category?.Name, p.Unit, p.PurchasePrice, p.SalePrice, p.VatRate, p.TrackStock, p.MinStock, qty, p.Description, p.IsActive);

    private async Task ValidateAsync(ProductRequest r, Guid? existingId, CancellationToken ct)
    {
        var units = await settings.GetUnitsAsync(ct);
        var v = new ValidationBuilder();
        v.When(string.IsNullOrWhiteSpace(r.Code), "code", "Ürün kodu zorunludur.");
        v.When(string.IsNullOrWhiteSpace(r.Name), "name", "Ürün adı zorunludur.");
        v.When(!units.Contains(r.Unit ?? ""), "unit", $"Birim tanımlı değil. Tanımlı birimler: {string.Join(", ", units)}.");
        v.When(r.PurchasePrice < 0, "purchasePrice", "Alış fiyatı negatif olamaz.");
        v.When(r.SalePrice < 0, "salePrice", "Satış fiyatı negatif olamaz.");
        v.When(r.MinStock < 0, "minStock", "Minimum stok negatif olamaz.");
        v.ThrowIfAny();
        await settings.EnsureVatRateAllowedAsync(r.VatRate, "vatRate", ct);
        var code = r.Code.Trim();
        if (await db.Products.AnyAsync(p => p.Code == code && p.Id != existingId, ct))
            throw new ConflictException($"'{code}' kodlu başka bir ürün var.");
    }

    public async Task<ProductDto> CreateAsync(ProductRequest r, CancellationToken ct = default)
    {
        await ValidateAsync(r, null, ct);
        var p = new Product();
        Apply(p, r);
        db.Products.Add(p);
        audit.Log("Ürün oluşturuldu", nameof(Product), p.Id, $"{p.Code} {p.Name}");
        await db.SaveChangesAsync(ct);
        return await GetAsync(p.Id, ct);
    }

    public async Task<ProductDto> UpdateAsync(Guid id, ProductRequest r, CancellationToken ct = default)
    {
        await ValidateAsync(r, id, ct);
        var p = await db.Products.FirstOrDefaultAsync(x => x.Id == id, ct) ?? throw new NotFoundException("Ürün bulunamadı.");
        // Birim değişimi mevcut stok/satış anlamını bozar; hareket varsa engellenir.
        if (p.Unit != r.Unit && (await db.StockMovements.AnyAsync(m => m.ProductId == id, ct) || await db.SaleItems.AnyAsync(i => i.ProductId == id, ct)))
            throw new ConflictException("Hareket görmüş bir ürünün birimi değiştirilemez (birimler arası otomatik dönüşüm yapılmaz). Yeni bir ürün açın.");
        Apply(p, r);
        p.UpdatedAtUtc = DateTime.UtcNow;
        audit.Log("Ürün güncellendi", nameof(Product), p.Id, $"{p.Code} {p.Name}");
        await db.SaveChangesAsync(ct);
        return await GetAsync(id, ct);
    }

    private static void Apply(Product p, ProductRequest r)
    {
        p.Code = r.Code.Trim(); p.Name = r.Name.Trim(); p.CategoryId = r.CategoryId; p.Unit = r.Unit;
        p.PurchasePrice = r.PurchasePrice; p.SalePrice = r.SalePrice; p.VatRate = r.VatRate;
        p.TrackStock = r.TrackStock; p.MinStock = r.MinStock;
        p.Description = string.IsNullOrWhiteSpace(r.Description) ? null : r.Description.Trim();
        p.IsActive = r.IsActive;
    }

    // ───────────── Kategoriler ─────────────

    public async Task<IReadOnlyList<CategoryDto>> ListCategoriesAsync(CancellationToken ct = default) =>
        await db.ProductCategories.AsNoTracking().OrderBy(c => c.Name).Select(c => new CategoryDto(c.Id, c.Name)).ToListAsync(ct);

    public async Task<CategoryDto> CreateCategoryAsync(string name, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new ValidationFailedException("name", "Kategori adı zorunludur.");
        var n = name.Trim();
        if (await db.ProductCategories.AnyAsync(c => c.Name.ToLower() == n.ToLower(), ct)) throw new ConflictException("Bu kategori zaten var.");
        var c = new ProductCategory { Name = n };
        db.ProductCategories.Add(c);
        await db.SaveChangesAsync(ct);
        return new CategoryDto(c.Id, c.Name);
    }

    // ───────────── Müşteriye özel fiyatlar ─────────────

    public async Task<IReadOnlyList<CustomerPriceDto>> ListCustomerPricesAsync(Guid? customerId, Guid? productId, CancellationToken ct = default)
    {
        var q = db.CustomerProductPrices.AsNoTracking().AsQueryable();
        if (customerId.HasValue) q = q.Where(p => p.CustomerId == customerId);
        if (productId.HasValue) q = q.Where(p => p.ProductId == productId);
        return await q.OrderBy(p => p.Customer!.Name).ThenBy(p => p.Product!.Name)
            .Select(p => new CustomerPriceDto(p.Id, p.CustomerId, p.Customer!.Name, p.ProductId, p.Product!.Name, p.Product.Unit, p.Price, p.Product.SalePrice))
            .ToListAsync(ct);
    }

    /// <summary>Müşteri–ürün için özel fiyat ekler veya günceller. Geçmiş satışlar etkilenmez (kalemlerde fiyat donmuştur).</summary>
    public async Task<CustomerPriceDto> SetCustomerPriceAsync(Guid customerId, Guid productId, decimal price, CancellationToken ct = default)
    {
        if (price < 0) throw new ValidationFailedException("price", "Fiyat negatif olamaz.");
        var customer = await db.Customers.FirstOrDefaultAsync(c => c.Id == customerId, ct) ?? throw new NotFoundException("Müşteri bulunamadı.");
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId, ct) ?? throw new NotFoundException("Ürün bulunamadı.");
        var existing = await db.CustomerProductPrices.FirstOrDefaultAsync(p => p.CustomerId == customerId && p.ProductId == productId, ct);
        if (existing is null)
        {
            existing = new CustomerProductPrice { CustomerId = customerId, ProductId = productId, Price = price };
            db.CustomerProductPrices.Add(existing);
        }
        else { existing.Price = price; existing.UpdatedAtUtc = DateTime.UtcNow; }
        audit.Log("Müşteri özel fiyatı", nameof(CustomerProductPrice), existing.Id, $"{customer.Name} / {product.Code}: {price:N2}");
        await db.SaveChangesAsync(ct);
        return new CustomerPriceDto(existing.Id, customerId, customer.Name, productId, product.Name, product.Unit, price, product.SalePrice);
    }

    public async Task DeleteCustomerPriceAsync(Guid customerId, Guid productId, CancellationToken ct = default)
    {
        var e = await db.CustomerProductPrices.FirstOrDefaultAsync(p => p.CustomerId == customerId && p.ProductId == productId, ct)
            ?? throw new NotFoundException("Özel fiyat bulunamadı.");
        db.CustomerProductPrices.Remove(e);
        audit.Log("Müşteri özel fiyatı silindi", nameof(CustomerProductPrice), e.Id, "");
        await db.SaveChangesAsync(ct);
    }

    public async Task<PriceLookupDto> LookupPriceAsync(Guid customerId, Guid productId, CancellationToken ct = default)
    {
        var p = await db.Products.AsNoTracking().FirstOrDefaultAsync(x => x.Id == productId, ct) ?? throw new NotFoundException("Ürün bulunamadı.");
        var (price, special) = await pricing.GetEffectivePriceAsync(customerId, p, ct);
        return new PriceLookupDto(price, special, p.SalePrice, p.VatRate, p.Unit);
    }
}
