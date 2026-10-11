using Microsoft.EntityFrameworkCore;
using SutTicaretCRM.Domain;

namespace SutTicaretCRM.Application.Services;

public record StockLevelDto(Guid ProductId, string Code, string Name, string Unit, bool TrackStock, decimal Quantity, decimal MinStock, bool IsLow, decimal StockValueAtCost);

public record StockMovementDto(Guid Id, DateOnly Date, Guid ProductId, string ProductCode, string ProductName, string Unit,
    StockMovementType MovementType, decimal QuantityDelta, string? Note, LedgerSourceType SourceType, Guid? SourceId, DateTime CreatedAtUtc);

public record StockAdjustRequest(Guid ProductId, DateOnly Date, StockMovementType MovementType, decimal? QuantityDelta, decimal? CountedQuantity, string? Note);

public class StockService(IAppDbContext db, ICurrentUser user, IClock clock, SettingsService settings, IAuditLogger audit)
{
    public async Task<decimal> GetQuantityAsync(Guid productId, CancellationToken ct = default) =>
        await db.StockMovements.Where(m => m.ProductId == productId).SumAsync(m => (decimal?)m.QuantityDelta, ct) ?? 0;

    public async Task<Dictionary<Guid, decimal>> GetQuantitiesAsync(IReadOnlyCollection<Guid>? productIds = null, CancellationToken ct = default)
    {
        var q = db.StockMovements.AsQueryable();
        if (productIds is not null) q = q.Where(m => productIds.Contains(m.ProductId));
        return await q.GroupBy(m => m.ProductId).Select(g => new { g.Key, Sum = g.Sum(x => x.QuantityDelta) }).ToDictionaryAsync(x => x.Key, x => x.Sum, ct);
    }

    /// <summary>
    /// Stok hareketi yazar. Stok takibi kapalı ürün için hiçbir şey yazmaz.
    /// (SourceType, SourceId, SourceLineId, MovementType) veritabanında tekildir: aynı satış satırı iki kez stoktan düşemez.
    /// </summary>
    public async Task<StockMovement?> RecordAsync(Guid productId, DateOnly date, StockMovementType type, decimal quantityDelta,
        LedgerSourceType source, Guid? sourceId, Guid? sourceLineId, string? note, CancellationToken ct = default)
    {
        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == productId, ct) ?? throw new NotFoundException("Ürün bulunamadı.");
        if (!product.TrackStock) return null;
        if (quantityDelta == 0) return null;

        if (sourceId.HasValue && await db.StockMovements.AnyAsync(m =>
                m.SourceType == source && m.SourceId == sourceId && m.SourceLineId == sourceLineId && m.MovementType == type, ct))
            throw new ConflictException("Bu işlem için stok hareketi zaten oluşturulmuş.");

        var m = new StockMovement
        {
            ProductId = productId, MovementDate = date, MovementType = type, QuantityDelta = quantityDelta,
            SourceType = source, SourceId = sourceId, SourceLineId = sourceLineId, Note = note,
            CreatedByUserId = user.UserId, CreatedAtUtc = clock.UtcNow,
        };
        db.StockMovements.Add(m);
        return m;
    }

    /// <summary>Satış onayında stok kontrolü: takip açık ürünler için, negatif stoğa izin verilmiyorsa yetersiz stokta hata.</summary>
    public async Task<List<string>> CheckAvailabilityAsync(IEnumerable<(Guid ProductId, decimal Quantity)> needs, CancellationToken ct = default)
    {
        var grouped = needs.GroupBy(n => n.ProductId).Select(g => (Id: g.Key, Qty: g.Sum(x => x.Quantity))).ToList();
        var products = await db.Products.Where(p => grouped.Select(g => g.Id).Contains(p.Id) && p.TrackStock).ToListAsync(ct);
        var levels = await GetQuantitiesAsync(products.Select(p => p.Id).ToList(), ct);
        var problems = new List<string>();
        foreach (var p in products)
        {
            var have = levels.GetValueOrDefault(p.Id);
            var need = grouped.First(g => g.Id == p.Id).Qty;
            if (have < need) problems.Add($"{p.Name}: stok {have:0.###} {p.Unit}, satılmak istenen {need:0.###} {p.Unit}");
        }
        return problems;
    }

    public async Task<IReadOnlyList<StockLevelDto>> GetLevelsAsync(bool onlyLow, string? search, CancellationToken ct = default)
    {
        var q = db.Products.AsNoTracking().Where(p => p.IsActive);
        if (!string.IsNullOrWhiteSpace(search))
        {
            var s = search.Trim().ToLower();
            q = q.Where(p => p.Name.ToLower().Contains(s) || p.Code.ToLower().Contains(s));
        }
        var products = await q.OrderBy(p => p.Name).ToListAsync(ct);
        var levels = await GetQuantitiesAsync(null, ct);
        var list = products.Select(p =>
        {
            var qty = p.TrackStock ? levels.GetValueOrDefault(p.Id) : 0;
            return new StockLevelDto(p.Id, p.Code, p.Name, p.Unit, p.TrackStock, qty, p.MinStock,
                p.TrackStock && qty <= p.MinStock, p.TrackStock ? Money.Round(qty * p.PurchasePrice) : 0);
        });
        return (onlyLow ? list.Where(x => x.IsLow) : list).ToList();
    }

    public async Task<PagedResult<StockMovementDto>> GetMovementsAsync(Guid? productId, DateOnly? from, DateOnly? to, int page, int pageSize, CancellationToken ct = default)
    {
        page = Math.Max(1, page); pageSize = Math.Clamp(pageSize, 1, 200);
        var q = db.StockMovements.AsNoTracking().AsQueryable();
        if (productId.HasValue) q = q.Where(m => m.ProductId == productId);
        if (from.HasValue) q = q.Where(m => m.MovementDate >= from);
        if (to.HasValue) q = q.Where(m => m.MovementDate <= to);
        var total = await q.CountAsync(ct);
        var items = await q.OrderByDescending(m => m.MovementDate).ThenByDescending(m => m.Seq)
            .Skip((page - 1) * pageSize).Take(pageSize)
            .Select(m => new StockMovementDto(m.Id, m.MovementDate, m.ProductId, m.Product!.Code, m.Product.Name, m.Product.Unit,
                m.MovementType, m.QuantityDelta, m.Note, m.SourceType, m.SourceId, m.CreatedAtUtc))
            .ToListAsync(ct);
        return new PagedResult<StockMovementDto>(items, total, page, pageSize);
    }

    /// <summary>Manuel stok düzeltmesi / açılış stoğu / iade girişi. Sayım miktarı verilirse fark otomatik hesaplanır.</summary>
    public async Task<StockMovementDto> AdjustAsync(StockAdjustRequest r, CancellationToken ct = default)
    {
        var v = new ValidationBuilder();
        v.When(r.MovementType is not (StockMovementType.Adjustment or StockMovementType.Opening or StockMovementType.Return or StockMovementType.Purchase),
            "movementType", "Bu türde manuel stok hareketi eklenemez.");
        v.When(r.QuantityDelta is null && r.CountedQuantity is null, "quantityDelta", "Miktar veya sayım sonucu girilmelidir.");
        v.When(r.CountedQuantity is < 0, "countedQuantity", "Sayım miktarı negatif olamaz.");
        v.ThrowIfAny();

        var product = await db.Products.FirstOrDefaultAsync(p => p.Id == r.ProductId, ct) ?? throw new NotFoundException("Ürün bulunamadı.");
        if (!product.TrackStock) throw new ConflictException("Bu ürün için stok takibi kapalı.");

        var delta = r.QuantityDelta ?? (r.CountedQuantity!.Value - await GetQuantityAsync(product.Id, ct));
        if (delta == 0) throw new ValidationFailedException("quantityDelta", "Stok miktarında değişiklik yok.");
        if (r.MovementType is StockMovementType.Return or StockMovementType.Purchase or StockMovementType.Opening && delta < 0)
            throw new ValidationFailedException("quantityDelta", "Bu hareket türü stok artırır; miktar pozitif olmalıdır.");
        if (delta < 0 && !await settings.GetAllowNegativeStockAsync(ct) && await GetQuantityAsync(product.Id, ct) + delta < 0)
            throw new ConflictException("Stok negatife düşemez.");

        var m = await RecordAsync(product.Id, r.Date, r.MovementType, delta, LedgerSourceType.Manual, null, null, r.Note, ct);
        audit.Log("Stok düzeltme", nameof(StockMovement), m!.Id, $"{product.Code}: {delta:+0.###;-0.###} {product.Unit}");
        await db.SaveChangesAsync(ct);
        return new StockMovementDto(m.Id, m.MovementDate, product.Id, product.Code, product.Name, product.Unit, m.MovementType, m.QuantityDelta, m.Note, m.SourceType, m.SourceId, m.CreatedAtUtc);
    }
}
